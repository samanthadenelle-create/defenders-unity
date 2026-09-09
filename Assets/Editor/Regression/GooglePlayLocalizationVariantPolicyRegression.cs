using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;

namespace DeNelle.Editor.Regression
{
    /// <summary>Non-mutating closure gate for the Google Play localization variant policy.</summary>
    public static class GooglePlayLocalizationVariantPolicyRegression
    {
        private const string PolicyPath =
            "Assets/Editor/Localization/GooglePlayLocalizationVariantPolicy.json";
        private const string EnginePath =
            "Assets/Editor/Localization/GooglePlayLocalizationVariant.cs";
        private const string AndroidBuildPath = "Assets/Editor/AndroidBuild.cs";
        private const string ContentExclusionPath = "Assets/Editor/GooglePlayContentExclusion.cs";
        private const string SharedTablePath =
            "Assets/Localization/Tables/GameStrings Shared Data.asset";

        private static readonly string[] RequiredLocales =
        {
            "en", "es", "pt-BR", "de", "fr", "ru", "ar", "ja", "ko", "zh-Hans",
        };

        private static readonly string[] EnabledLocales =
        {
            "en", "es", "pt-BR", "de", "fr", "ru",
        };

        private static readonly string[] VisibleReplacementKeys =
        {
            "heroSelect.subtitle",
            "jewelerFtue.stakeChecking",
            "jewelerFtue.stakeVerified",
            "jewelerFtue.stakeVerifiedHighTier",
            "jewelerFtue.stakeNotVerified",
        };

        private static readonly string[] SettingsWalletKeys =
        {
            "settings.section.wallet",
            "settings.wallet.connect",
            "settings.wallet.disconnect",
            "settings.wallet.disconnectAddress",
        };

        private static readonly Regex SharedEntryPattern = new Regex(
            @"- m_Id: (?<id>[0-9]+)\r?\n\s+m_Key: (?<key>[^\r\n]+)", RegexOptions.Compiled);
        private static readonly Regex PlaceholderPattern = new Regex(
            @"\{[A-Za-z_][A-Za-z0-9_]*\}|\{[0-9]+\}", RegexOptions.Compiled);

        public static bool Run(out string reason)
        {
            var failures = new List<string>();
            JObject policy = ReadObject(PolicyPath, failures);
            JObject english = ReadObject("Assets/Resources/Data/Canonical/en.json", failures);
            if (policy == null || english == null)
            {
                reason = "PLAY_LOCALIZATION_VARIANT_POLICY_FAIL: " + string.Join(" | ", failures);
                return false;
            }

            string[] required = policy["requiredLocales"]?.Values<string>().ToArray() ?? Array.Empty<string>();
            string[] enabled = policy["enabledTableLocales"]?.Values<string>().ToArray() ?? Array.Empty<string>();
            RequireSet(required, RequiredLocales, "required locale contract", failures);
            RequireSet(enabled, EnabledLocales, "enabled StringTable contract", failures);

            var dispositions = new HashSet<string>(StringComparer.Ordinal);
            var stripKeys = new HashSet<string>(StringComparer.Ordinal);
            foreach (JObject group in policy["stripGroups"]?.OfType<JObject>() ?? Enumerable.Empty<JObject>())
            {
                string owner = group["owner"]?.Value<string>();
                string groupReason = group["reason"]?.Value<string>();
                string proofPath = group["proofPath"]?.Value<string>();
                string proofToken = group["proofToken"]?.Value<string>();
                string[] keys = group["keys"]?.Values<string>().ToArray() ?? Array.Empty<string>();
                if (string.IsNullOrWhiteSpace(owner) || string.IsNullOrWhiteSpace(groupReason) ||
                    string.IsNullOrWhiteSpace(proofPath) || string.IsNullOrWhiteSpace(proofToken) || keys.Length == 0)
                    failures.Add("strip group is missing owner/reason/proof/exact keys");
                else if (!File.Exists(proofPath) ||
                         File.ReadAllText(proofPath).IndexOf(proofToken, StringComparison.Ordinal) < 0)
                    failures.Add("strip proof failed for " + owner + ": " + proofPath + " lacks " + proofToken);

                foreach (string key in keys)
                {
                    if (string.IsNullOrWhiteSpace(key) || key.IndexOf('*') >= 0)
                        failures.Add("strip policy contains an empty/wildcard key: " + key);
                    if (!dispositions.Add(key)) failures.Add("duplicate disposition: " + key);
                    stripKeys.Add(key);
                }
            }

            var replacementKeys = new HashSet<string>(StringComparer.Ordinal);
            foreach (JObject row in policy["replacementRows"]?.OfType<JObject>() ?? Enumerable.Empty<JObject>())
            {
                string key = row["key"]?.Value<string>();
                JObject values = row["values"] as JObject;
                if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(row["owner"]?.Value<string>()) ||
                    string.IsNullOrWhiteSpace(row["reason"]?.Value<string>()))
                    failures.Add("replacement row is missing key/owner/reason");
                if (!dispositions.Add(key ?? string.Empty)) failures.Add("duplicate disposition: " + key);
                replacementKeys.Add(key ?? string.Empty);

                RequireSet(values?.Properties().Select(property => property.Name).ToArray() ?? Array.Empty<string>(),
                    RequiredLocales, "replacement locales for " + key, failures);
                foreach (string locale in RequiredLocales)
                {
                    string value = values?[locale]?.Value<string>();
                    if (string.IsNullOrWhiteSpace(value))
                        failures.Add("replacement is empty for " + locale + ":" + key);
                }
            }

            RequireSet(replacementKeys, VisibleReplacementKeys, "visible replacement rows", failures);
            if (stripKeys.Contains("storeWordmark"))
                failures.Add("storeWordmark must remain for GooglePlayStorefront");

            string[] expectedStore = english.Properties()
                .Select(property => property.Name)
                .Where(key => key.StartsWith("store", StringComparison.Ordinal) && key != "storeWordmark")
                .ToArray();
            string[] expectedSwap = english.Properties()
                .Select(property => property.Name)
                .Where(key => key.StartsWith("swap.", StringComparison.Ordinal))
                .ToArray();
            RequireSubset(stripKeys, expectedStore, "Wallet-owned Store rows", failures);
            RequireSubset(stripKeys, expectedSwap, "Web3 swap rows", failures);
            RequireSubset(stripKeys, SettingsWalletKeys, "compile-hidden Settings wallet rows", failures);
            if (stripKeys.Count != expectedStore.Length + expectedSwap.Length + SettingsWalletKeys.Length)
                failures.Add("strip policy contains an unreviewed extra/missing row; expected 57 exact keys, got " +
                             stripKeys.Count);

            foreach (string locale in RequiredLocales)
            {
                string resourcePath = "Assets/Resources/Data/Canonical/" + locale + ".json";
                string streamingPath = "Assets/StreamingAssets/Data/Canonical/" + locale + ".json";
                if (!File.Exists(resourcePath) || !File.Exists(streamingPath))
                {
                    failures.Add("canonical locale pair is missing: " + locale);
                    continue;
                }
                if (!File.ReadAllBytes(resourcePath).SequenceEqual(File.ReadAllBytes(streamingPath)))
                    failures.Add("canonical mirrors differ: " + locale);
                JObject source = ReadObject(resourcePath, failures);
                if (source == null) continue;
                foreach (string key in dispositions)
                    if (source[key]?.Type != JTokenType.String)
                        failures.Add(locale + " source is missing disposition key " + key);

                foreach (JObject row in policy["replacementRows"].OfType<JObject>())
                {
                    string key = row["key"].Value<string>();
                    string original = source[key]?.Value<string>() ?? string.Empty;
                    string replacement = row["values"]?[locale]?.Value<string>() ?? string.Empty;
                    if (!string.Equals(PlaceholderSignature(original), PlaceholderSignature(replacement),
                            StringComparison.Ordinal))
                        failures.Add("placeholder drift for " + locale + ":" + key);
                }
                AssertForbiddenClosure(source, dispositions, locale, failures);
            }

            AssertTableCoverage(dispositions, enabled, failures);
            AssertEngineShape(failures);
            AssertBuildOrder(failures);

            var synthetic = new JObject { ["future.visible"] = "Connect wallet to continue" };
            var syntheticFailures = new List<string>();
            AssertForbiddenClosure(synthetic, dispositions, "negative-self-test", syntheticFailures);
            if (syntheticFailures.Count != 1)
                failures.Add("unmapped forbidden-copy self-test did not fail exactly once");

            if (failures.Count == 0)
            {
                reason = "PLAY_LOCALIZATION_VARIANT_POLICY_OK - 57 exact strip rows + 5 visible replacements; " +
                         "10 locale pairs and 6 tables closed with byte-safe transaction source";
                return true;
            }

            reason = "PLAY_LOCALIZATION_VARIANT_POLICY_FAIL: " + string.Join(" | ", failures.Distinct());
            return false;
        }

        private static void AssertTableCoverage(
            IEnumerable<string> dispositions,
            IEnumerable<string> enabledLocales,
            ICollection<string> failures)
        {
            string sharedSource = File.Exists(SharedTablePath) ? File.ReadAllText(SharedTablePath) : string.Empty;
            var ids = SharedEntryPattern.Matches(sharedSource).Cast<Match>().ToDictionary(
                match => match.Groups["key"].Value.Trim(),
                match => match.Groups["id"].Value,
                StringComparer.Ordinal);

            foreach (string key in dispositions)
                if (!ids.ContainsKey(key)) failures.Add("shared GameStrings lacks disposition key " + key);

            foreach (string locale in enabledLocales)
            {
                string path = "Assets/Localization/Tables/GameStrings_" + locale + ".asset";
                string table = File.Exists(path) ? File.ReadAllText(path) : string.Empty;
                foreach (string key in dispositions)
                    if (ids.TryGetValue(key, out string id) &&
                        !Regex.IsMatch(table, @"(?m)^\s*- m_Id: " + Regex.Escape(id) + @"\s*$"))
                        failures.Add(path + " lacks disposition key id for " + key);
            }
        }

        private static void AssertEngineShape(ICollection<string> failures)
        {
            string source = File.Exists(EnginePath) ? File.ReadAllText(EnginePath) : string.Empty;
            Require(source, "PrepareForAddressables", "engine lacks pre-Addressables entry point", failures);
            Require(source, "AssertPrepared", "engine lacks prepared-state assertion", failures);
            Require(source, "Restore(\"aborted prepare\")", "engine lacks failed-prepare restore", failures);
            Require(source, "PLAY_LOCALIZATION_VARIANT_REUSED", "engine is not idempotent", failures);
            Require(source, "PLAY_LOCALIZATION_VARIANT_RESTORED", "engine lacks restore marker", failures);
            Require(source, "File.WriteAllBytes(parts[0]", "engine does not restore source bytes", failures);
            Require(source, "SequenceEqual(File.ReadAllBytes(parts[1]))", "engine does not verify byte restore", failures);
            Require(source, "shared.RemoveKey(sharedEntry.Id)", "engine does not strip shared key ids", failures);
            Require(source, "table.RemoveEntry(sharedEntry.Id)", "engine does not strip locale table rows", failures);
            Require(source, "InitializeOnLoadMethod", "engine lacks interrupted-transaction repair", failures);
        }

        private static void AssertBuildOrder(ICollection<string> failures)
        {
            string build = File.Exists(AndroidBuildPath) ? File.ReadAllText(AndroidBuildPath) : string.Empty;
            string content = File.Exists(ContentExclusionPath) ? File.ReadAllText(ContentExclusionPath) : string.Empty;
            const string prepareToken = "GooglePlayLocalizationVariant.PrepareForAddressables(options.extraScriptingDefines)";
            const string addressablesToken = "AddressablesContentBuild.EnsureBuilt";
            const string restoreToken = "GooglePlayLocalizationVariant.Restore(\"Android build finally\")";
            int prepareAt = build.IndexOf(prepareToken, StringComparison.Ordinal);
            int addressablesAt = build.IndexOf(addressablesToken, StringComparison.Ordinal);
            int restoreAt = build.IndexOf(restoreToken, StringComparison.Ordinal);
            if (prepareAt < 0 || addressablesAt < 0 || prepareAt > addressablesAt)
                failures.Add("AndroidBuild does not prepare the Play localization variant before Addressables");
            if (restoreAt < addressablesAt)
                failures.Add("AndroidBuild does not restore the Play localization variant after the build transaction");
            Require(build, "finally", "AndroidBuild localization restoration is not protected by finally", failures);
            Require(content, "GooglePlayLocalizationVariant.AssertPrepared()",
                "BuildPlayer content hook does not require the prebuilt localization variant", failures);
            Require(content, "GooglePlayLocalizationVariant.Restore(\"post-build\")",
                "post-build content hook does not restore the localization transaction", failures);
            Require(content, "GooglePlayLocalizationVariant.Restore(\"pre-build sweep\")",
                "pre-build repair does not restore an interrupted localization transaction", failures);
        }

        private static void AssertForbiddenClosure(
            JObject source,
            ISet<string> dispositions,
            string locale,
            ICollection<string> failures)
        {
            foreach (JProperty property in source.Properties())
            {
                if (property.Value.Type != JTokenType.String || property.Name.StartsWith("_", StringComparison.Ordinal))
                    continue;
                string value = property.Value.Value<string>() ?? string.Empty;
                if (ContainsForbiddenToken(value) && !dispositions.Contains(property.Name))
                    failures.Add(locale + " has forbidden copy with no exact Play disposition: " + property.Name);
            }
        }

        private static bool ContainsForbiddenToken(string value)
        {
            string[] tokens = { "solana", "jupiter", "$skr", " skr", "usdc", "crypto", "web3", "wallet" };
            return tokens.Any(token => (value ?? string.Empty).IndexOf(token,
                StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private static string PlaceholderSignature(string value) =>
            string.Join("|", PlaceholderPattern.Matches(value ?? string.Empty).Cast<Match>()
                .Select(match => match.Value).OrderBy(token => token, StringComparer.Ordinal));

        private static JObject ReadObject(string path, ICollection<string> failures)
        {
            try { return JObject.Parse(File.ReadAllText(path)); }
            catch (Exception exception)
            {
                failures.Add("could not parse " + path + ": " + exception.Message);
                return null;
            }
        }

        private static void RequireSet(
            IEnumerable<string> actual,
            IEnumerable<string> expected,
            string label,
            ICollection<string> failures)
        {
            var left = new HashSet<string>(actual ?? Array.Empty<string>(), StringComparer.Ordinal);
            var right = new HashSet<string>(expected ?? Array.Empty<string>(), StringComparer.Ordinal);
            if (!left.SetEquals(right)) failures.Add(label + " differs (actual=" + string.Join(",", left) + ")");
        }

        private static void RequireSubset(
            ISet<string> actual,
            IEnumerable<string> expected,
            string label,
            ICollection<string> failures)
        {
            string[] missing = expected.Where(key => !actual.Contains(key)).ToArray();
            if (missing.Length > 0) failures.Add(label + " missing: " + string.Join(",", missing));
        }

        private static void Require(string source, string token, string message, ICollection<string> failures)
        {
            if (source.IndexOf(token, StringComparison.Ordinal) < 0) failures.Add(message);
        }
    }
}

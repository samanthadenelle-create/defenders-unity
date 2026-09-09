using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;
using UnityEngine.Localization.Tables;

namespace DeNelle.Editor.Localization
{
    /// <summary>
    /// Crash-safe, byte-restored Google Play localization transaction. The Play
    /// player must consume the transformed JSON and StringTables before they are
    /// packed; the tracked Seeker sources are never left in their transient state.
    /// </summary>
    public static class GooglePlayLocalizationVariant
    {
        public const string PlayDefine = "GOOGLE_PLAY";
        public const string PolicyPath =
            "Assets/Editor/Localization/GooglePlayLocalizationVariantPolicy.json";
        public const string LedgerPath =
            "Builds/google-play-localization-variant-ledger.txt";
        public const string BackupRoot =
            "Builds/google-play-localization-variant-backups";

        private const string SharedTablePath =
            "Assets/Localization/Tables/GameStrings Shared Data.asset";
        private const string ActiveSessionKey =
            "GooglePlayLocalizationVariant.Active";
        private const string LogTag = "[PlayLocalizationVariant]";

        private static readonly Regex PlaceholderPattern =
            new Regex(@"\{[A-Za-z_][A-Za-z0-9_]*\}|\{[0-9]+\}", RegexOptions.Compiled);

        public static bool PrepareForAddressables(IReadOnlyList<string> defines)
        {
            var defineSet = new HashSet<string>(defines ?? Array.Empty<string>(), StringComparer.Ordinal);
            if (!defineSet.Contains(PlayDefine))
            {
                Restore("non-Play content build");
                return false;
            }

            if (SessionState.GetBool(ActiveSessionKey, false) && File.Exists(LedgerPath))
            {
                AssertPrepared(LoadAndValidatePolicy());
                Debug.Log(LogTag + " PLAY_LOCALIZATION_VARIANT_REUSED - active transaction is already prepared.");
                return true;
            }

            Restore("pre-prepare sweep");
            VariantPolicy policy = LoadAndValidatePolicy();
            ValidateTrackedSources(policy);

            Directory.CreateDirectory(BackupRoot);
            var ledger = new List<string>();
            try
            {
                foreach (string locale in policy.RequiredLocales)
                {
                    RewriteCanonical(CanonicalPath("Resources", locale), locale, policy, ledger);
                    RewriteCanonical(CanonicalPath("StreamingAssets", locale), locale, policy, ledger);
                }

                RewriteStringTables(policy, ledger);
                SessionState.SetBool(ActiveSessionKey, true);
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                AssertPrepared(policy);
                Debug.Log(LogTag + " PLAY_LOCALIZATION_VARIANT_OK - transformed " + ledger.Count +
                          " source asset(s); originals are ledgered for byte restoration.");
                return true;
            }
            catch (Exception exception)
            {
                try { Restore("aborted prepare"); }
                catch (Exception restoreException)
                {
                    Debug.LogError(LogTag + " restore after prepare failure also failed: " + restoreException);
                }
                throw exception as BuildFailedException ??
                      new BuildFailedException("Google Play localization variant failed: " + exception.Message);
            }
        }

        public static void AssertPrepared()
        {
            AssertPrepared(LoadAndValidatePolicy());
        }

        public static void Restore(string reason)
        {
            SessionState.SetBool(ActiveSessionKey, false);
            if (!File.Exists(LedgerPath))
            {
                if (Directory.Exists(BackupRoot)) Directory.Delete(BackupRoot, true);
                return;
            }

            var failures = new List<string>();
            string[] rows = File.ReadAllLines(LedgerPath);
            foreach (string raw in rows.Reverse())
            {
                string[] parts = raw.Split('\t');
                if (parts.Length != 2 || !File.Exists(parts[1]))
                {
                    failures.Add("invalid or missing backup row: " + raw);
                    continue;
                }

                File.WriteAllBytes(parts[0], File.ReadAllBytes(parts[1]));
                if (!File.ReadAllBytes(parts[0]).SequenceEqual(File.ReadAllBytes(parts[1])))
                    failures.Add("byte restore mismatch: " + parts[0]);
            }

            if (failures.Count > 0)
                throw new BuildFailedException(LogTag + " PLAY_LOCALIZATION_VARIANT_RESTORE_FAIL - " +
                                               string.Join(" | ", failures));

            File.Delete(LedgerPath);
            if (Directory.Exists(BackupRoot)) Directory.Delete(BackupRoot, true);
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            Debug.Log(LogTag + " PLAY_LOCALIZATION_VARIANT_RESTORED - " + rows.Length +
                      " source asset(s) restored byte-for-byte (" + reason + ").");
        }

        private static void RewriteCanonical(
            string path,
            string locale,
            VariantPolicy policy,
            ICollection<string> ledger)
        {
            Backup(path, ledger);
            JObject root = JObject.Parse(File.ReadAllText(path));
            foreach (StripGroup group in policy.StripGroups)
                foreach (string key in group.Keys)
                    root.Property(key)?.Remove();

            foreach (ReplacementRow replacement in policy.ReplacementRows)
                root[replacement.Key] = replacement.Values[locale];

            File.WriteAllText(path, root.ToString(Formatting.Indented) + Environment.NewLine);
        }

        private static void RewriteStringTables(VariantPolicy policy, ICollection<string> ledger)
        {
            var tablePaths = policy.EnabledTableLocales.ToDictionary(
                locale => locale,
                locale => "Assets/Localization/Tables/GameStrings_" + locale + ".asset",
                StringComparer.Ordinal);

            Backup(SharedTablePath, ledger);
            foreach (string path in tablePaths.Values) Backup(path, ledger);

            SharedTableData shared = AssetDatabase.LoadAssetAtPath<SharedTableData>(SharedTablePath);
            if (shared == null)
                throw new BuildFailedException(LogTag + " could not load " + SharedTablePath);

            var tables = new Dictionary<string, StringTable>(StringComparer.Ordinal);
            foreach (var pair in tablePaths)
            {
                StringTable table = AssetDatabase.LoadAssetAtPath<StringTable>(pair.Value);
                if (table == null)
                    throw new BuildFailedException(LogTag + " could not load " + pair.Value);
                tables[pair.Key] = table;
            }

            foreach (string key in StripKeys(policy))
            {
                SharedTableData.SharedTableEntry sharedEntry = shared.GetEntry(key);
                if (sharedEntry == null)
                    throw new BuildFailedException(LogTag + " shared GameStrings key is missing: " + key);
                foreach (StringTable table in tables.Values) table.RemoveEntry(sharedEntry.Id);
                shared.RemoveKey(sharedEntry.Id);
            }

            foreach (ReplacementRow replacement in policy.ReplacementRows)
            {
                if (shared.GetEntry(replacement.Key) == null)
                    throw new BuildFailedException(LogTag + " replacement key is missing from shared GameStrings: " +
                                                   replacement.Key);
                foreach (var pair in tables)
                {
                    StringTableEntry entry = pair.Value.GetEntry(replacement.Key);
                    if (entry == null)
                        throw new BuildFailedException(LogTag + " replacement key is missing from " + pair.Key +
                                                       ": " + replacement.Key);
                    entry.Value = replacement.Values[pair.Key];
                }
            }

            foreach (StringTable table in tables.Values)
            {
                EditorUtility.SetDirty(table);
                AssetDatabase.SaveAssetIfDirty(table);
            }
            EditorUtility.SetDirty(shared);
            AssetDatabase.SaveAssetIfDirty(shared);
        }

        private static void AssertPrepared(VariantPolicy policy)
        {
            foreach (string locale in policy.RequiredLocales)
            {
                AssertCanonicalPrepared(CanonicalPath("Resources", locale), locale, policy);
                AssertCanonicalPrepared(CanonicalPath("StreamingAssets", locale), locale, policy);
                byte[] resource = File.ReadAllBytes(CanonicalPath("Resources", locale));
                byte[] streaming = File.ReadAllBytes(CanonicalPath("StreamingAssets", locale));
                if (!resource.SequenceEqual(streaming))
                    throw new BuildFailedException(LogTag + " transformed canonical mirrors differ for " + locale);
            }

            SharedTableData shared = AssetDatabase.LoadAssetAtPath<SharedTableData>(SharedTablePath);
            if (shared == null) throw new BuildFailedException(LogTag + " prepared shared GameStrings is missing");
            foreach (string key in StripKeys(policy))
                if (shared.GetEntry(key) != null)
                    throw new BuildFailedException(LogTag + " stripped key remains in shared GameStrings: " + key);

            foreach (string locale in policy.EnabledTableLocales)
            {
                string path = "Assets/Localization/Tables/GameStrings_" + locale + ".asset";
                StringTable table = AssetDatabase.LoadAssetAtPath<StringTable>(path);
                if (table == null) throw new BuildFailedException(LogTag + " prepared table is missing: " + path);
                foreach (string key in StripKeys(policy))
                    if (table.GetEntry(key) != null)
                        throw new BuildFailedException(LogTag + " stripped key remains in " + path + ": " + key);
                foreach (ReplacementRow replacement in policy.ReplacementRows)
                {
                    string actual = table.GetEntry(replacement.Key)?.Value;
                    if (!string.Equals(actual, replacement.Values[locale], StringComparison.Ordinal))
                        throw new BuildFailedException(LogTag + " replacement drift in " + path + ": " +
                                                       replacement.Key);
                }
            }
        }

        private static void AssertCanonicalPrepared(
            string path,
            string locale,
            VariantPolicy policy)
        {
            JObject root = JObject.Parse(File.ReadAllText(path));
            foreach (string key in StripKeys(policy))
                if (root.Property(key) != null)
                    throw new BuildFailedException(LogTag + " stripped key remains in " + path + ": " + key);
            foreach (ReplacementRow replacement in policy.ReplacementRows)
                if (!string.Equals(root[replacement.Key]?.Value<string>(), replacement.Values[locale],
                        StringComparison.Ordinal))
                    throw new BuildFailedException(LogTag + " replacement drift in " + path + ": " +
                                                   replacement.Key);
        }

        private static VariantPolicy LoadAndValidatePolicy()
        {
            if (!File.Exists(PolicyPath))
                throw new BuildFailedException(LogTag + " policy is missing: " + PolicyPath);
            VariantPolicy policy = JsonConvert.DeserializeObject<VariantPolicy>(File.ReadAllText(PolicyPath));
            if (policy == null || policy.SchemaVersion != 1)
                throw new BuildFailedException(LogTag + " policy schemaVersion must be 1");
            if (policy.RequiredLocales == null || policy.RequiredLocales.Count != 10 ||
                policy.RequiredLocales.Distinct(StringComparer.Ordinal).Count() != 10)
                throw new BuildFailedException(LogTag + " policy must name ten distinct required locales");
            if (policy.EnabledTableLocales == null || policy.EnabledTableLocales.Count != 6 ||
                policy.EnabledTableLocales.Any(locale => !policy.RequiredLocales.Contains(locale)))
                throw new BuildFailedException(LogTag + " policy must name six enabled locales drawn from the required set");

            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (StripGroup group in policy.StripGroups ?? new List<StripGroup>())
            {
                if (string.IsNullOrWhiteSpace(group.Owner) || string.IsNullOrWhiteSpace(group.Reason) ||
                    string.IsNullOrWhiteSpace(group.ProofPath) || string.IsNullOrWhiteSpace(group.ProofToken) ||
                    group.Keys == null || group.Keys.Count == 0)
                    throw new BuildFailedException(LogTag + " every strip group needs owner/reason/proof and exact keys");
                if (!File.Exists(group.ProofPath) ||
                    File.ReadAllText(group.ProofPath).IndexOf(group.ProofToken, StringComparison.Ordinal) < 0)
                    throw new BuildFailedException(LogTag + " compile proof failed for " + group.Owner + ": " +
                                                   group.ProofPath + " lacks " + group.ProofToken);
                foreach (string key in group.Keys)
                    if (string.IsNullOrWhiteSpace(key) || !seen.Add(key))
                        throw new BuildFailedException(LogTag + " duplicate/empty disposition key: " + key);
            }

            foreach (ReplacementRow replacement in policy.ReplacementRows ?? new List<ReplacementRow>())
            {
                if (string.IsNullOrWhiteSpace(replacement.Key) || !seen.Add(replacement.Key) ||
                    string.IsNullOrWhiteSpace(replacement.Owner) || string.IsNullOrWhiteSpace(replacement.Reason))
                    throw new BuildFailedException(LogTag + " replacement rows need a unique key, owner, and reason");
                if (replacement.Values == null || replacement.Values.Count != policy.RequiredLocales.Count)
                    throw new BuildFailedException(LogTag + " replacement locale coverage is incomplete: " + replacement.Key);
                foreach (string locale in policy.RequiredLocales)
                    if (!replacement.Values.TryGetValue(locale, out string value) || string.IsNullOrWhiteSpace(value))
                        throw new BuildFailedException(LogTag + " replacement is missing " + locale + ": " +
                                                       replacement.Key);
            }
            return policy;
        }

        private static void ValidateTrackedSources(VariantPolicy policy)
        {
            foreach (string locale in policy.RequiredLocales)
            {
                string resourcePath = CanonicalPath("Resources", locale);
                string streamingPath = CanonicalPath("StreamingAssets", locale);
                if (!File.Exists(resourcePath) || !File.Exists(streamingPath))
                    throw new BuildFailedException(LogTag + " canonical locale pair is missing: " + locale);
                if (!File.ReadAllBytes(resourcePath).SequenceEqual(File.ReadAllBytes(streamingPath)))
                    throw new BuildFailedException(LogTag + " canonical locale mirrors differ before transform: " + locale);

                JObject root = JObject.Parse(File.ReadAllText(resourcePath));
                foreach (string key in StripKeys(policy))
                    if (root[key]?.Type != JTokenType.String)
                        throw new BuildFailedException(LogTag + " strip key is missing/non-string in " + locale + ": " + key);
                foreach (ReplacementRow replacement in policy.ReplacementRows)
                {
                    string source = root[replacement.Key]?.Value<string>();
                    if (string.IsNullOrEmpty(source))
                        throw new BuildFailedException(LogTag + " replacement source is missing in " + locale + ": " +
                                                       replacement.Key);
                    string expectedPlaceholders = PlaceholderSignature(source);
                    string actualPlaceholders = PlaceholderSignature(replacement.Values[locale]);
                    if (!string.Equals(expectedPlaceholders, actualPlaceholders, StringComparison.Ordinal))
                        throw new BuildFailedException(LogTag + " placeholder drift for " + locale + ":" +
                                                       replacement.Key + " (" + expectedPlaceholders + " -> " +
                                                       actualPlaceholders + ")");
                }
            }
        }

        private static string PlaceholderSignature(string value) =>
            string.Join("|", PlaceholderPattern.Matches(value ?? string.Empty)
                .Cast<Match>().Select(match => match.Value).OrderBy(token => token, StringComparer.Ordinal));

        private static IEnumerable<string> StripKeys(VariantPolicy policy) =>
            policy.StripGroups.SelectMany(group => group.Keys);

        private static string CanonicalPath(string root, string locale) =>
            "Assets/" + root + "/Data/Canonical/" + locale + ".json";

        private static void Backup(string path, ICollection<string> ledger)
        {
            if (!File.Exists(path)) throw new BuildFailedException(LogTag + " source is missing: " + path);
            Directory.CreateDirectory(BackupRoot);
            string backup = Path.Combine(BackupRoot, path.Replace('/', '_').Replace('\\', '_') + ".bytes");
            File.WriteAllBytes(backup, File.ReadAllBytes(path));
            ledger.Add(path + "\t" + backup);
            string ledgerDirectory = Path.GetDirectoryName(LedgerPath);
            if (!string.IsNullOrEmpty(ledgerDirectory)) Directory.CreateDirectory(ledgerDirectory);
            File.WriteAllLines(LedgerPath, ledger.ToArray());
        }

        [InitializeOnLoadMethod]
        private static void ScheduleInterruptedTransactionRepair()
        {
            EditorApplication.delayCall += RepairInterruptedTransaction;
        }

        private static void RepairInterruptedTransaction()
        {
            if (SessionState.GetBool(ActiveSessionKey, false) || !File.Exists(LedgerPath)) return;
            Debug.LogWarning(LogTag + " interrupted transaction ledger found; restoring Seeker localization sources.");
            Restore("domain-load repair");
        }

        [Serializable]
        private sealed class VariantPolicy
        {
            [JsonProperty("schemaVersion")] public int SchemaVersion;
            [JsonProperty("requiredLocales")] public List<string> RequiredLocales;
            [JsonProperty("enabledTableLocales")] public List<string> EnabledTableLocales;
            [JsonProperty("replacementRows")] public List<ReplacementRow> ReplacementRows;
            [JsonProperty("stripGroups")] public List<StripGroup> StripGroups;
        }

        [Serializable]
        private sealed class ReplacementRow
        {
            [JsonProperty("key")] public string Key;
            [JsonProperty("owner")] public string Owner;
            [JsonProperty("reason")] public string Reason;
            [JsonProperty("values")] public Dictionary<string, string> Values;
        }

        [Serializable]
        private sealed class StripGroup
        {
            [JsonProperty("owner")] public string Owner;
            [JsonProperty("reason")] public string Reason;
            [JsonProperty("proofPath")] public string ProofPath;
            [JsonProperty("proofToken")] public string ProofToken;
            [JsonProperty("keys")] public List<string> Keys;
        }
    }
}

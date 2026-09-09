// =============================================================================
// SmartArgumentRegression -- placeholder integrity before translators receive copy.
// Package-free parser supports positional and named top-level arguments, escaped
// braces, format suffixes and nested Smart String bodies.
//
// RED-FIRST RECIPE (revert immediately): in a temporary policy-listed qps-ploc
// locale change jewelerPolish.body from {0} to {1}, then unbalance a brace in
// feedback.overCapacity. Expect an argument mismatch and malformed-template red.
// The in-memory cases below ensure parser coverage even while English is alone.
// =============================================================================
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using DeNelle.Core.UI;

namespace DeNelle.Editor.Regression
{
    public static class SmartArgumentRegression
    {
        public static bool Run(out string reason)
        {
            var failures = new List<string>();
            RunSelfTests(failures);
            string collectorCount = HudStrings.Format(HudStrings.KeyCollectorsCount, 2, 3);
            if (!string.Equals(collectorCount, "Collectors 2/3 full", StringComparison.Ordinal))
                failures.Add("live HudStrings -> LocalText.Format positional path returned '" + collectorCount + "'");

            LocalizationAuditIO.Policy policy;
            if (!LocalizationAuditIO.TryLoadPolicy(out policy, failures))
            {
                reason = "SMART ARGUMENT FAIL -- " + LocalizationAuditIO.JoinFailures(failures);
                return false;
            }

            try
            {
                foreach (var collection in policy.collections) CheckCollection(policy, collection, failures);
                CheckManifestDeclarations(policy, failures);
            }
            catch (Exception ex)
            {
                failures.Add("smart argument audit threw: " + ex.GetType().Name + ": " + ex.Message);
            }

            if (failures.Count > 0)
            {
                reason = "SMART ARGUMENT FAIL -- " + LocalizationAuditIO.JoinFailures(failures);
                return false;
            }
            reason = "SMART ARGUMENT OK -- parser self-tests green and all policy-listed locale argument sets match English";
            return true;
        }

        private static void CheckCollection(LocalizationAuditIO.Policy policy, LocalizationAuditIO.CollectionPolicy collection, ICollection<string> failures)
        {
            var english = new Dictionary<string, string>();
            var ioFailures = new List<string>();
            if (!LocalizationAuditIO.TryReadFlatJson(collection.source, out english, ioFailures))
            {
                foreach (string failure in ioFailures) failures.Add(failure);
                return;
            }

            var englishArguments = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
            foreach (var pair in english)
            {
                HashSet<string> args;
                string error;
                if (!TryExtractArguments(pair.Value, out args, out error)) failures.Add("English " + pair.Key + " malformed: " + error);
                else englishArguments[pair.Key] = args;
            }

            string sourceDirectory = Path.GetDirectoryName(collection.source.Replace('\\', '/')) ?? string.Empty;
            foreach (var locale in policy.supportedLocales.Where(l => l != null && !string.Equals(l.code, policy.baseLocale, StringComparison.OrdinalIgnoreCase)))
            {
                string path = sourceDirectory + "/" + locale.code + ".json";
                if (!File.Exists(LocalizationAuditIO.FullPath(path)) && !locale.required) continue;
                var translated = new Dictionary<string, string>();
                ioFailures.Clear();
                if (!LocalizationAuditIO.TryReadFlatJson(path, out translated, ioFailures))
                {
                    foreach (string failure in ioFailures) failures.Add(failure);
                    continue;
                }
                foreach (var pair in translated.Where(p => englishArguments.ContainsKey(p.Key)))
                {
                    HashSet<string> args;
                    string error;
                    if (!TryExtractArguments(pair.Value, out args, out error))
                    {
                        failures.Add(locale.code + " " + pair.Key + " malformed: " + error);
                        continue;
                    }
                    if (!args.SetEquals(englishArguments[pair.Key]))
                        failures.Add(locale.code + " " + pair.Key + " arguments [" + string.Join(",", args.OrderBy(v => v).ToArray()) +
                                     "] != English [" + string.Join(",", englishArguments[pair.Key].OrderBy(v => v).ToArray()) + "]");
                }
            }
        }

        private static void CheckManifestDeclarations(LocalizationAuditIO.Policy policy, ICollection<string> failures)
        {
            string relative = string.IsNullOrWhiteSpace(policy.manifestPath) ? "docs/localization/manifest.json" : policy.manifestPath;
            string path = LocalizationAuditIO.FullPath(relative);
            if (!File.Exists(path)) return;
            JObject manifest;
            try { manifest = JObject.Parse(File.ReadAllText(path)); }
            catch (Exception ex) { failures.Add("manifest malformed while checking smart arguments: " + ex.Message); return; }
            var entries = manifest["entries"] as JArray;
            if (manifest["schemaVersion"] == null || entries == null)
            {
                failures.Add("manifest must contain schemaVersion and entries while checking smart arguments");
                return;
            }

            // A generated report-only inventory has not had its argument metadata reviewed;
            // locale-table parity above still runs, but empty generated arrays are not treated
            // as an owner's declaration. Once reviewed=true is set, declarations become binding.
            if (manifest.Value<bool?>("reviewed") != true) return;

            var english = new Dictionary<string, string>();
            var ioFailures = new List<string>();
            foreach (var collection in policy.collections)
                if (LocalizationAuditIO.TryReadFlatJson(collection.source, out english, ioFailures)) break;
            foreach (JObject entry in entries.OfType<JObject>())
            {
                string key = entry.Value<string>("key");
                var declared = entry["arguments"] as JArray;
                if (string.IsNullOrEmpty(key) || declared == null || !english.ContainsKey(key)) continue;
                HashSet<string> actual;
                string error;
                if (!TryExtractArguments(english[key], out actual, out error)) continue;
                var expected = new HashSet<string>(declared.Values<string>(), StringComparer.Ordinal);
                if (!actual.SetEquals(expected)) failures.Add("manifest arguments for " + key + " do not match English placeholders");
            }
        }

        internal static bool TryExtractArguments(string value, out HashSet<string> arguments, out string error)
        {
            arguments = new HashSet<string>(StringComparer.Ordinal);
            error = null;
            if (value == null) return true;
            bool sawNumeric = false, sawNamed = false;
            for (int i = 0; i < value.Length; i++)
            {
                if (value[i] == '}' && (i + 1 >= value.Length || value[i + 1] != '}'))
                { error = "unmatched closing brace at " + i; return false; }
                if (value[i] == '}' && i + 1 < value.Length && value[i + 1] == '}') { i++; continue; }
                if (value[i] != '{') continue;
                if (i + 1 < value.Length && value[i + 1] == '{') { i++; continue; }

                int depth = 1;
                int end = i + 1;
                for (; end < value.Length && depth > 0; end++)
                {
                    if (value[end] == '{') depth++;
                    else if (value[end] == '}') depth--;
                }
                if (depth != 0) { error = "unmatched opening brace at " + i; return false; }
                string body = value.Substring(i + 1, end - i - 2);
                int delimiter = FirstTopLevelDelimiter(body);
                string argument = (delimiter < 0 ? body : body.Substring(0, delimiter)).Trim();
                if (argument.Length > 0)
                {
                    bool numeric = argument.All(char.IsDigit);
                    if (!numeric && !IsIdentifier(argument)) { error = "invalid argument name '" + argument + "'"; return false; }
                    arguments.Add(argument);
                    sawNumeric |= numeric;
                    sawNamed |= !numeric;
                }
                i = end - 1;
            }
            if (sawNumeric && sawNamed) { error = "positional and named arguments are mixed"; return false; }
            return true;
        }

        private static int FirstTopLevelDelimiter(string body)
        {
            int depth = 0;
            for (int i = 0; i < body.Length; i++)
            {
                if (body[i] == '{') depth++;
                else if (body[i] == '}') depth--;
                else if (depth == 0 && (body[i] == ':' || body[i] == ',')) return i;
            }
            return -1;
        }

        private static bool IsIdentifier(string value)
        {
            if (string.IsNullOrEmpty(value) || !(char.IsLetter(value[0]) || value[0] == '_')) return false;
            return value.Skip(1).All(c => char.IsLetterOrDigit(c) || c == '_' || c == '.');
        }

        private static void RunSelfTests(ICollection<string> failures)
        {
            AssertArguments("A {0} B {{literal}}", new[] { "0" }, true, failures, "escaped/positional");
            AssertArguments("{count:plural:one item|many items}", new[] { "count" }, true, failures, "named smart suffix");
            AssertArguments("{Minimum} {Duration} {Resource} {AmountOver} {Gem}",
                new[] { "Minimum", "Duration", "Resource", "AmountOver", "Gem" }, true, failures, "named argument properties");
            AssertArguments("{0} {name}", new string[0], false, failures, "mixed argument styles");
            AssertArguments("broken {0", new string[0], false, failures, "unbalanced opening brace");
            AssertArguments("broken }", new string[0], false, failures, "unbalanced closing brace");
        }

        private static void AssertArguments(string value, IEnumerable<string> expected, bool expectedSuccess,
            ICollection<string> failures, string label)
        {
            HashSet<string> actual;
            string error;
            bool success = TryExtractArguments(value, out actual, out error);
            if (success != expectedSuccess || (success && !actual.SetEquals(expected)))
                failures.Add("smart argument parser self-test failed: " + label + " (" + (error ?? "wrong argument set") + ")");
        }
    }
}

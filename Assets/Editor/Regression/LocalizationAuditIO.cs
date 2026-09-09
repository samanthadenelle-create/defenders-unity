// =============================================================================
// Shared, package-free IO for the localization Phase-0 regressions.
// Unity Localization types are deliberately not referenced: EditorRegression does
// not own that package dependency. String tables are inspected through
// AssetDatabase + SerializedObject instead.
// =============================================================================
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace DeNelle.Editor.Regression
{
    internal static class LocalizationAuditIO
    {
        internal const string PolicyPath = "Assets/Editor/Localization/LocalizationPolicy.json";
        internal const string LiteralBaselineProperty = "literalDebtBaseline";
        internal const string LiteralFingerprintAlgorithm =
            "sha256(normalizedPath\\0member\\0sink\\0decodedValue\\0samePathMemberSinkValueOrdinal)";

        internal sealed class Policy
        {
            public int schemaVersion;
            public int scannerVersion;
            public string baseLocale;
            public string manifestPath;
            public List<string> scanRoots;
            public List<CollectionPolicy> collections;
            public List<LocalePolicy> supportedLocales;
            public List<AuthorityPolicy> approvedAuthorities;
            public List<AuthorityPolicy> legacyAuthorities;
            public List<ExemptionPolicy> exemptions;
        }

        internal sealed class CollectionPolicy
        {
            public string id;
            public string source;
            public string mirror;
            public string unityShared;
            public string unityTablePattern;
        }

        internal sealed class LocalePolicy
        {
            public string code;
            public bool required;
            // Policy-owned release state. `required` controls parity enforcement;
            // `enabledInBuild` records what is actually shipped. A locale may be
            // validated while deliberately remaining disabled for linguistic QA.
            public bool enabledInBuild;
            public string status;
            public int sortOrder;
        }

        internal sealed class AuthorityPolicy
        {
            public string path;
            public string kind;
            public string reason;
            public string owner;
            public string removeBy;
        }

        internal sealed class ExemptionPolicy
        {
            public string fingerprint;
            public string path;
            public string member;
            public string category;
            public string reason;
            public string owner;
            public string reviewBy;
        }

        internal sealed class UnityStringTable
        {
            public readonly Dictionary<long, string> KeysById = new Dictionary<long, string>();
            public readonly Dictionary<long, string> ValuesById = new Dictionary<long, string>();
        }

        internal sealed class LiteralDebtBaseline
        {
            public int schemaVersion;
            public int scannerVersion;
            public bool reviewed;
            public string fingerprintAlgorithm;
            public List<LiteralDebtEntry> entries;
        }

        internal sealed class LiteralDebtEntry
        {
            public string fingerprint;
            public string path;
            public string member;
            public string sink;
            public int line;
            public string valueHash;
            public string classification;
        }

        internal static string ProjectRoot
        {
            get { return Path.GetDirectoryName(Application.dataPath) ?? string.Empty; }
        }

        internal static string FullPath(string projectRelative)
        {
            return Path.GetFullPath(Path.Combine(ProjectRoot, (projectRelative ?? string.Empty).Replace('/', Path.DirectorySeparatorChar)));
        }

        internal static string NormalizePath(string path)
        {
            if (string.IsNullOrEmpty(path)) return string.Empty;
            string full = Path.GetFullPath(path).Replace('\\', '/');
            string root = ProjectRoot.Replace('\\', '/').TrimEnd('/') + "/";
            return full.StartsWith(root, StringComparison.OrdinalIgnoreCase) ? full.Substring(root.Length) : full;
        }

        internal static bool TryLoadPolicy(out Policy policy, List<string> failures)
        {
            policy = null;
            string path = FullPath(PolicyPath);
            if (!File.Exists(path))
            {
                failures.Add("localization policy missing: " + PolicyPath);
                return false;
            }

            try
            {
                policy = JsonConvert.DeserializeObject<Policy>(File.ReadAllText(path));
            }
            catch (Exception ex)
            {
                failures.Add("localization policy malformed: " + ex.Message);
                return false;
            }

            if (policy == null || policy.schemaVersion != 1 || policy.scannerVersion != 1)
                failures.Add("localization policy must declare schemaVersion=1 and scannerVersion=1");
            if (string.IsNullOrWhiteSpace(policy.baseLocale)) failures.Add("localization policy baseLocale is empty");
            if (policy.collections == null || policy.collections.Count == 0) failures.Add("localization policy has no collections");
            if (policy.supportedLocales == null || policy.supportedLocales.Count == 0) failures.Add("localization policy has no supportedLocales");
            if (policy.scanRoots == null || policy.scanRoots.Count == 0) failures.Add("localization policy has no scanRoots");
            if (policy.approvedAuthorities == null) policy.approvedAuthorities = new List<AuthorityPolicy>();
            if (policy.legacyAuthorities == null) policy.legacyAuthorities = new List<AuthorityPolicy>();
            if (policy.exemptions == null) policy.exemptions = new List<ExemptionPolicy>();

            foreach (var collection in policy.collections ?? new List<CollectionPolicy>())
                if (collection == null || string.IsNullOrWhiteSpace(collection.id) ||
                    string.IsNullOrWhiteSpace(collection.source) || string.IsNullOrWhiteSpace(collection.mirror) ||
                    string.IsNullOrWhiteSpace(collection.unityShared) || string.IsNullOrWhiteSpace(collection.unityTablePattern))
                    failures.Add("every localization collection must include id, source, mirror, unityShared, and unityTablePattern");

            var localeCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var localeSortOrders = new HashSet<int>();
            foreach (var locale in policy.supportedLocales ?? new List<LocalePolicy>())
            {
                if (locale == null || string.IsNullOrWhiteSpace(locale.code)) failures.Add("supported locale code is empty");
                else
                {
                    if (!localeCodes.Add(locale.code)) failures.Add("duplicate supported locale code: " + locale.code);
                    if (string.IsNullOrWhiteSpace(locale.status)) failures.Add("supported locale status is empty: " + locale.code);
                    if (locale.sortOrder < 0 || locale.sortOrder > ushort.MaxValue)
                        failures.Add("supported locale sortOrder must fit UInt16: " + locale.code);
                    else if (!localeSortOrders.Add(locale.sortOrder))
                        failures.Add("duplicate supported locale sortOrder: " + locale.sortOrder);
                    if (locale.enabledInBuild && !locale.required)
                        failures.Add("build-enabled locale must be required by parity: " + locale.code);
                }
            }
            if (!string.IsNullOrWhiteSpace(policy.baseLocale) && !localeCodes.Contains(policy.baseLocale))
                failures.Add("baseLocale must also appear in supportedLocales: " + policy.baseLocale);

            var duplicateAuthority = policy.approvedAuthorities.Concat(policy.legacyAuthorities)
                .Where(a => a != null && !string.IsNullOrWhiteSpace(a.path))
                .GroupBy(a => a.path.Replace('\\', '/'), StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault(g => g.Count() > 1);
            if (duplicateAuthority != null) failures.Add("duplicate authority policy path: " + duplicateAuthority.Key);
            foreach (var authority in policy.approvedAuthorities)
                if (authority == null || string.IsNullOrWhiteSpace(authority.path) ||
                    string.IsNullOrWhiteSpace(authority.kind) || string.IsNullOrWhiteSpace(authority.reason))
                    failures.Add("every approved authority must include an exact path, kind, and reason");
            foreach (var authority in policy.legacyAuthorities)
                if (authority == null || string.IsNullOrWhiteSpace(authority.path) ||
                    string.IsNullOrWhiteSpace(authority.kind) || string.IsNullOrWhiteSpace(authority.owner) ||
                    string.IsNullOrWhiteSpace(authority.removeBy))
                    failures.Add("every legacy authority must include an exact path, kind, owner, and removeBy phase");

            foreach (var exemption in policy.exemptions)
            {
                if (exemption == null || string.IsNullOrWhiteSpace(exemption.fingerprint) ||
                    string.IsNullOrWhiteSpace(exemption.path) || string.IsNullOrWhiteSpace(exemption.category) ||
                    string.IsNullOrWhiteSpace(exemption.reason) || string.IsNullOrWhiteSpace(exemption.owner) ||
                    string.IsNullOrWhiteSpace(exemption.reviewBy))
                    failures.Add("every localization exemption must be exact and include fingerprint, path, category, reason, owner, and reviewBy");
            }
            return failures.Count == 0;
        }

        internal static bool TryReadFlatJson(string projectRelative, out Dictionary<string, string> flat, List<string> failures)
        {
            flat = new Dictionary<string, string>(StringComparer.Ordinal);
            string path = FullPath(projectRelative);
            if (!File.Exists(path))
            {
                failures.Add("localization JSON missing: " + projectRelative);
                return false;
            }
            try
            {
                JObject root;
                using (var reader = new JsonTextReader(new StringReader(File.ReadAllText(path))))
                {
                    root = JObject.Load(reader, new JsonLoadSettings
                    {
                        DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error
                    });
                }
                Flatten(root, null, flat);
                return true;
            }
            catch (Exception ex)
            {
                failures.Add("localization JSON malformed at " + projectRelative + ": " + ex.Message);
                return false;
            }
        }

        internal static bool TryReadManifestAndBaseline(Policy policy, out JObject manifest,
            out LiteralDebtBaseline baseline, out bool hasBaseline, List<string> failures)
        {
            manifest = null;
            baseline = null;
            hasBaseline = false;
            string relative = string.IsNullOrWhiteSpace(policy.manifestPath)
                ? "docs/localization/manifest.json" : policy.manifestPath;
            string path = FullPath(relative);
            if (!File.Exists(path)) return true;
            try { manifest = JObject.Parse(File.ReadAllText(path)); }
            catch (Exception ex)
            {
                failures.Add("localization manifest malformed at " + relative + ": " + ex.Message);
                return false;
            }

            if (manifest["schemaVersion"] == null || manifest["entries"] == null || manifest["entries"].Type != JTokenType.Array)
            {
                failures.Add("localization manifest must contain schemaVersion and the broader inventory entries array");
                return false;
            }

            JToken token = manifest[LiteralBaselineProperty];
            if (token == null) return true;
            hasBaseline = true;
            if (token.Type != JTokenType.Object)
            {
                failures.Add(LiteralBaselineProperty + " must be an object");
                return false;
            }
            var raw = (JObject)token;
            if (raw["reviewed"] == null || raw["reviewed"].Type != JTokenType.Boolean ||
                raw["entries"] == null || raw["entries"].Type != JTokenType.Array)
            {
                failures.Add(LiteralBaselineProperty + " must contain an explicit reviewed boolean and entries array");
                return false;
            }
            try { baseline = raw.ToObject<LiteralDebtBaseline>(); }
            catch (Exception ex)
            {
                failures.Add(LiteralBaselineProperty + " has an invalid shape: " + ex.Message);
                return false;
            }
            if (baseline == null || baseline.schemaVersion != 1)
                failures.Add(LiteralBaselineProperty + " must declare schemaVersion=1");
            if (baseline != null && baseline.scannerVersion != policy.scannerVersion)
                failures.Add(LiteralBaselineProperty + " scannerVersion must equal policy scannerVersion=" + policy.scannerVersion);
            if (baseline != null && !string.Equals(baseline.fingerprintAlgorithm, LiteralFingerprintAlgorithm, StringComparison.Ordinal))
                failures.Add(LiteralBaselineProperty + " fingerprintAlgorithm does not match the shared scanner");
            if (baseline == null || baseline.entries == null)
            {
                failures.Add(LiteralBaselineProperty + " entries could not be read");
                return false;
            }

            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var entry in baseline.entries)
            {
                if (entry == null || string.IsNullOrWhiteSpace(entry.fingerprint) ||
                    string.IsNullOrWhiteSpace(entry.path) || string.IsNullOrWhiteSpace(entry.member) ||
                    string.IsNullOrWhiteSpace(entry.sink) || entry.line <= 0 ||
                    string.IsNullOrWhiteSpace(entry.valueHash) || string.IsNullOrWhiteSpace(entry.classification))
                {
                    failures.Add(LiteralBaselineProperty + " entries require fingerprint, path, member, sink, positive line, valueHash, and classification");
                    continue;
                }
                if (!entry.fingerprint.StartsWith("sha256:", StringComparison.Ordinal) ||
                    !entry.valueHash.StartsWith("sha256:", StringComparison.Ordinal))
                    failures.Add(LiteralBaselineProperty + " hashes must use the sha256: prefix at " + entry.path + ":" + entry.line);
                if (!seen.Add(entry.fingerprint)) failures.Add("duplicate literal debt fingerprint: " + entry.fingerprint);
            }
            return failures.Count == 0;
        }

        internal static LiteralDebtBaseline BuildLiteralBaseline(Policy policy,
            IEnumerable<LocalizationLiteralScanner.Finding> findings, bool reviewed)
        {
            return new LiteralDebtBaseline
            {
                schemaVersion = 1,
                scannerVersion = policy.scannerVersion,
                reviewed = reviewed,
                fingerprintAlgorithm = LiteralFingerprintAlgorithm,
                entries = findings.OrderBy(f => f.Path, StringComparer.Ordinal)
                    .ThenBy(f => f.Member, StringComparer.Ordinal).ThenBy(f => f.Line)
                    .ThenBy(f => f.Fingerprint, StringComparer.Ordinal)
                    .Select(f => new LiteralDebtEntry
                    {
                        fingerprint = f.Fingerprint,
                        path = f.Path,
                        member = f.Member,
                        sink = f.Sink,
                        line = f.Line,
                        valueHash = f.ValueHash,
                        classification = f.Classification
                    }).ToList()
            };
        }

        private static void Flatten(JToken token, string prefix, IDictionary<string, string> output)
        {
            if (token == null) return;
            if (token.Type == JTokenType.Object)
            {
                foreach (var property in (JObject)token)
                {
                    if (property.Key.StartsWith("_", StringComparison.Ordinal)) continue;
                    string key = string.IsNullOrEmpty(prefix) ? property.Key : prefix + "." + property.Key;
                    Flatten(property.Value, key, output);
                }
                return;
            }
            if (token.Type == JTokenType.Array)
            {
                int index = 0;
                foreach (var child in (JArray)token)
                {
                    Flatten(child, (prefix ?? string.Empty) + "." + index, output);
                    index++;
                }
                return;
            }
            if (token.Type != JTokenType.Null && !string.IsNullOrEmpty(prefix)) output[prefix] = token.ToString();
        }

        internal static bool TryReadUnityTable(string sharedPath, string localizedPath, out UnityStringTable table, List<string> failures)
        {
            table = new UnityStringTable();
            UnityEngine.Object sharedObject = AssetDatabase.LoadMainAssetAtPath(sharedPath);
            UnityEngine.Object localizedObject = AssetDatabase.LoadMainAssetAtPath(localizedPath);
            if (sharedObject == null || localizedObject == null)
            {
                failures.Add("Unity localization asset missing: " + (sharedObject == null ? sharedPath : localizedPath));
                return false;
            }
            try
            {
                var shared = new SerializedObject(sharedObject);
                var entries = shared.FindProperty("m_Entries");
                if (entries == null || !entries.isArray) throw new InvalidDataException("shared table has no m_Entries array");
                for (int i = 0; i < entries.arraySize; i++)
                {
                    var row = entries.GetArrayElementAtIndex(i);
                    long id = row.FindPropertyRelative("m_Id").longValue;
                    string key = row.FindPropertyRelative("m_Key").stringValue;
                    if (table.KeysById.ContainsKey(id)) throw new InvalidDataException("duplicate shared id " + id);
                    if (table.KeysById.Values.Contains(key)) throw new InvalidDataException("duplicate shared key " + key);
                    table.KeysById[id] = key;
                }

                var localized = new SerializedObject(localizedObject);
                var values = localized.FindProperty("m_TableData");
                if (values == null || !values.isArray) throw new InvalidDataException("localized table has no m_TableData array");
                for (int i = 0; i < values.arraySize; i++)
                {
                    var row = values.GetArrayElementAtIndex(i);
                    long id = row.FindPropertyRelative("m_Id").longValue;
                    if (table.ValuesById.ContainsKey(id)) throw new InvalidDataException("duplicate localized id " + id);
                    table.ValuesById[id] = row.FindPropertyRelative("m_Localized").stringValue;
                }
                return true;
            }
            catch (Exception ex)
            {
                failures.Add("Unity localization table malformed: " + ex.Message);
                return false;
            }
        }

        internal static bool DictionariesEqual(IDictionary<string, string> left, IDictionary<string, string> right, out string detail)
        {
            var missing = left.Keys.Where(k => !right.ContainsKey(k)).OrderBy(k => k, StringComparer.Ordinal).ToList();
            var extra = right.Keys.Where(k => !left.ContainsKey(k)).OrderBy(k => k, StringComparer.Ordinal).ToList();
            var changed = left.Keys.Where(right.ContainsKey).Where(k => !string.Equals(left[k], right[k], StringComparison.Ordinal))
                .OrderBy(k => k, StringComparer.Ordinal).ToList();
            detail = "missing=" + missing.Count + Sample(missing) + ", extra=" + extra.Count + Sample(extra) +
                     ", changed=" + changed.Count + Sample(changed);
            return missing.Count == 0 && extra.Count == 0 && changed.Count == 0;
        }

        internal static string Sample(IList<string> values)
        {
            if (values == null || values.Count == 0) return string.Empty;
            return " [" + string.Join(", ", values.Take(6).ToArray()) + (values.Count > 6 ? ", ..." : string.Empty) + "]";
        }

        internal static string Sha256(string value)
        {
            using (var sha = SHA256.Create())
            {
                byte[] bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(value ?? string.Empty));
                var result = new StringBuilder(bytes.Length * 2);
                foreach (byte b in bytes) result.Append(b.ToString("x2"));
                return "sha256:" + result;
            }
        }

        internal static string ReplaceLocale(string pattern, string locale)
        {
            return (pattern ?? string.Empty).Replace("{locale}", locale ?? string.Empty);
        }

        internal static string DescribeLocaleStates(Policy policy)
        {
            if (policy == null || policy.supportedLocales == null) return "validated-required=[]; build-enabled=[]; status=[]";
            var ordered = policy.supportedLocales.Where(l => l != null && !string.IsNullOrWhiteSpace(l.code))
                .OrderBy(l => l.sortOrder).ThenBy(l => l.code, StringComparer.Ordinal).ToList();
            string required = string.Join(",", ordered.Where(l => l.required).Select(l => l.code).ToArray());
            string enabled = string.Join(",", ordered.Where(l => l.enabledInBuild).Select(l => l.code).ToArray());
            string statuses = string.Join(",", ordered.Select(l => l.code + ":" +
                (string.IsNullOrWhiteSpace(l.status) ? "unspecified" : l.status)).ToArray());
            return "validated-required=[" + required + "]; policy-build-enabled=[" + enabled + "]; status=[" + statuses + "]";
        }

        internal static string JoinFailures(IList<string> failures)
        {
            return string.Join("; ", failures.Take(12).ToArray()) + (failures.Count > 12 ? "; ..." : string.Empty);
        }
    }
}

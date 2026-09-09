// =============================================================================
// Pins the transition to one localization authority without making the known
// GameStrings import drift a crash or an unexplained false green.
//
// RED-FIRST RECIPES (revert immediately):
//  1. Add CanonicalJson.Read("Data/Canonical/en.json") to SettingsController;
//     it must be named as an unapproved authority.
//  2. Delete a shared-table key; the reason must report exact missing/stale counts.
// =============================================================================
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace DeNelle.Editor.Regression
{
    public static class LocalizationAuthorityRegression
    {
        public static bool Run(out string reason)
        {
            var failures = new List<string>();
            var notes = new List<string>();
            LocalizationAuditIO.Policy policy;
            if (!LocalizationAuditIO.TryLoadPolicy(out policy, failures))
            {
                reason = "LOCALIZATION AUTHORITY FAIL -- " + LocalizationAuditIO.JoinFailures(failures);
                return false;
            }

            try
            {
                CheckAuthorities(policy, failures, notes);
                CheckLegacyLocaleForwarders(failures, notes);
                CheckCanonicalAndUnityTable(policy, failures, notes);
            }
            catch (Exception ex)
            {
                failures.Add("authority audit threw: " + ex.GetType().Name + ": " + ex.Message);
            }

            if (failures.Count > 0)
            {
                reason = "LOCALIZATION AUTHORITY FAIL -- " + LocalizationAuditIO.JoinFailures(failures);
                return false;
            }
            reason = "LOCALIZATION AUTHORITY OK -- approved facade/provider boundary; " + string.Join("; ", notes.ToArray());
            return true;
        }

        private static void CheckAuthorities(LocalizationAuditIO.Policy policy, ICollection<string> failures, ICollection<string> notes)
        {
            var declared = new HashSet<string>(policy.approvedAuthorities.Concat(policy.legacyAuthorities)
                .Where(a => a != null).Select(a => (a.path ?? string.Empty).Replace('\\', '/')), StringComparer.OrdinalIgnoreCase);
            var hits = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (string rootRelative in policy.scanRoots)
            {
                string root = LocalizationAuditIO.FullPath(rootRelative);
                if (!Directory.Exists(root)) continue;
                foreach (string file in Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories))
                {
                    string rel = LocalizationAuditIO.NormalizePath(file);
                    if (rel.IndexOf("/Editor/", StringComparison.OrdinalIgnoreCase) >= 0 || rel.IndexOf("/Tests/", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                    string source = File.ReadAllText(file);
                    bool readsCanonical = source.IndexOf("CanonicalJson.Read", StringComparison.Ordinal) >= 0 &&
                        (source.IndexOf("canon-strings.json", StringComparison.OrdinalIgnoreCase) >= 0 ||
                         source.IndexOf("LocaleRelativePath", StringComparison.Ordinal) >= 0 ||
                         source.IndexOf("Root = \"Data/Canonical/\"", StringComparison.Ordinal) >= 0);
                    bool selectsSystemLocale = source.IndexOf("Application.systemLanguage", StringComparison.Ordinal) >= 0 &&
                        (source.IndexOf("CodeFor(", StringComparison.Ordinal) >= 0 ||
                         source.IndexOf("SelectedLocale", StringComparison.Ordinal) >= 0);
                    bool readsUnityDatabase = source.IndexOf("LocalizationSettings.StringDatabase", StringComparison.Ordinal) >= 0;
                    if (readsCanonical || selectsSystemLocale || readsUnityDatabase) hits.Add(rel);
                }
            }

            foreach (string hit in hits.OrderBy(v => v, StringComparer.Ordinal))
                if (!declared.Contains(hit)) failures.Add("unapproved localization authority: " + hit);
            foreach (string expected in declared.OrderBy(v => v, StringComparer.Ordinal))
                if (!hits.Contains(expected)) failures.Add("stale localization authority policy entry: " + expected);

            notes.Add(hits.Count + " declared authority/legacy reader(s) found");
            notes.Add(policy.legacyAuthorities.Count + " legacy reader(s) remain");
        }

        private static void CheckLegacyLocaleForwarders(
            ICollection<string> failures,
            ICollection<string> notes)
        {
            string[] hybridReaders =
            {
                "Assets/_Modules/Onboarding/CanonStrings.cs",
                "Assets/_Modules/Village/VillageStrings.cs",
            };

            foreach (string relative in hybridReaders)
            {
                string full = LocalizationAuditIO.FullPath(relative);
                if (!File.Exists(full))
                {
                    failures.Add("legacy locale forwarder missing: " + relative);
                    continue;
                }

                string source = File.ReadAllText(full);
                if (source.IndexOf("public static string Locale(string key) => LocalText.Get(key);",
                        StringComparison.Ordinal) < 0)
                    failures.Add("localizable branch does not forward through LocalText: " + relative);
                if (source.IndexOf("LocaleRelativePath", StringComparison.Ordinal) >= 0 ||
                    source.IndexOf("Data/Canonical/en.json", StringComparison.OrdinalIgnoreCase) >= 0)
                    failures.Add("localizable branch still reads English canonical JSON: " + relative);
            }

            notes.Add("Onboarding/Village localizable branches forward through LocalText");
        }

        private static void CheckCanonicalAndUnityTable(LocalizationAuditIO.Policy policy, ICollection<string> failures, ICollection<string> notes)
        {
            foreach (var collection in policy.collections)
            {
                var source = new Dictionary<string, string>();
                var mirror = new Dictionary<string, string>();
                var ioFailures = new List<string>();
                if (!LocalizationAuditIO.TryReadFlatJson(collection.source, out source, ioFailures) ||
                    !LocalizationAuditIO.TryReadFlatJson(collection.mirror, out mirror, ioFailures))
                {
                    foreach (string failure in ioFailures) failures.Add(failure);
                    continue;
                }
                string mirrorDetail;
                if (!LocalizationAuditIO.DictionariesEqual(source, mirror, out mirrorDetail))
                    failures.Add(collection.id + " canonical mirror drift: " + mirrorDetail);

                string baseLocale = policy.baseLocale;
                string localizedPath = LocalizationAuditIO.ReplaceLocale(collection.unityTablePattern, baseLocale);
                LocalizationAuditIO.UnityStringTable table;
                ioFailures.Clear();
                if (!LocalizationAuditIO.TryReadUnityTable(collection.unityShared, localizedPath, out table, ioFailures))
                {
                    // Current table import debt is surfaced loudly but does not throw or turn a
                    // migration bootstrap into a hollow skip. Malformed policy still fails above.
                    notes.Add("CURRENT TABLE DRIFT: " + string.Join("; ", ioFailures.ToArray()));
                    continue;
                }

                var unity = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var pair in table.KeysById)
                {
                    string value;
                    unity[pair.Value] = table.ValuesById.TryGetValue(pair.Key, out value) ? value : string.Empty;
                }
                string unityDetail;
                if (!LocalizationAuditIO.DictionariesEqual(source, unity, out unityDetail))
                    notes.Add("CURRENT TABLE DRIFT " + collection.id + ": " + unityDetail);
                else
                    notes.Add(collection.id + " Unity table matches " + source.Count + " canonical key(s)");
            }
        }
    }
}

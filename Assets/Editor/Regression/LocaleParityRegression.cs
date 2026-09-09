// =============================================================================
// LocaleParityRegression -- exact key/value-shape parity for policy-listed locales.
// It never treats every JSON file in Canonical as a locale.
//
// RED-FIRST RECIPE (revert immediately): policy-list a temporary qps-ploc copy,
// remove feedback.title, add orphan.probe, and blank feedback.body. The result must
// name one missing, one extra and one empty entry.
// =============================================================================
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace DeNelle.Editor.Regression
{
    public static class LocaleParityRegression
    {
        public static bool Run(out string reason)
        {
            var failures = new List<string>();
            LocalizationAuditIO.Policy policy;
            if (!LocalizationAuditIO.TryLoadPolicy(out policy, failures))
            {
                reason = "LOCALE PARITY FAIL -- " + LocalizationAuditIO.JoinFailures(failures);
                return false;
            }

            try
            {
                foreach (var collection in policy.collections)
                    CheckCollection(policy, collection, failures);
            }
            catch (Exception ex)
            {
                failures.Add("locale parity audit threw: " + ex.GetType().Name + ": " + ex.Message);
            }

            if (failures.Count > 0)
            {
                reason = "LOCALE PARITY FAIL -- " + LocalizationAuditIO.JoinFailures(failures);
                return false;
            }
            reason = "LOCALE PARITY OK -- " + policy.supportedLocales.Count + " policy-listed locale(s), exact canonical key parity";
            return true;
        }

        private static void CheckCollection(LocalizationAuditIO.Policy policy, LocalizationAuditIO.CollectionPolicy collection, ICollection<string> failures)
        {
            var baseTable = new Dictionary<string, string>();
            var ioFailures = new List<string>();
            if (!LocalizationAuditIO.TryReadFlatJson(collection.source, out baseTable, ioFailures))
            {
                foreach (string failure in ioFailures) failures.Add(failure);
                return;
            }

            var mirror = new Dictionary<string, string>();
            ioFailures.Clear();
            if (!LocalizationAuditIO.TryReadFlatJson(collection.mirror, out mirror, ioFailures))
                foreach (string failure in ioFailures) failures.Add(failure);
            else
            {
                string detail;
                if (!LocalizationAuditIO.DictionariesEqual(baseTable, mirror, out detail))
                    failures.Add(collection.id + " English source/mirror mismatch: " + detail);
            }

            string sourceDirectory = Path.GetDirectoryName(collection.source.Replace('\\', '/')) ?? string.Empty;
            string mirrorDirectory = Path.GetDirectoryName(collection.mirror.Replace('\\', '/')) ?? string.Empty;
            foreach (var locale in policy.supportedLocales.Where(l => l != null))
            {
                if (string.Equals(locale.code, policy.baseLocale, StringComparison.OrdinalIgnoreCase)) continue;
                string localeSource = sourceDirectory + "/" + locale.code + ".json";
                string localeMirror = mirrorDirectory + "/" + locale.code + ".json";
                bool exists = File.Exists(LocalizationAuditIO.FullPath(localeSource));
                if (!exists && !locale.required) continue;

                var translated = new Dictionary<string, string>();
                ioFailures.Clear();
                if (!LocalizationAuditIO.TryReadFlatJson(localeSource, out translated, ioFailures))
                {
                    foreach (string failure in ioFailures) failures.Add(failure);
                    continue;
                }
                var missing = baseTable.Keys.Where(k => !translated.ContainsKey(k)).OrderBy(k => k, StringComparer.Ordinal).ToList();
                var extra = translated.Keys.Where(k => !baseTable.ContainsKey(k)).OrderBy(k => k, StringComparer.Ordinal).ToList();
                var empty = translated.Where(p => string.IsNullOrWhiteSpace(p.Value)).Select(p => p.Key).OrderBy(k => k, StringComparer.Ordinal).ToList();
                if (missing.Count > 0) failures.Add(locale.code + " missing=" + missing.Count + LocalizationAuditIO.Sample(missing));
                if (extra.Count > 0) failures.Add(locale.code + " extra=" + extra.Count + LocalizationAuditIO.Sample(extra));
                if (empty.Count > 0) failures.Add(locale.code + " empty=" + empty.Count + LocalizationAuditIO.Sample(empty));

                if (File.Exists(LocalizationAuditIO.FullPath(localeMirror)))
                {
                    var translatedMirror = new Dictionary<string, string>();
                    ioFailures.Clear();
                    if (!LocalizationAuditIO.TryReadFlatJson(localeMirror, out translatedMirror, ioFailures))
                        foreach (string failure in ioFailures) failures.Add(failure);
                    else
                    {
                        string detail;
                        if (!LocalizationAuditIO.DictionariesEqual(translated, translatedMirror, out detail))
                            failures.Add(locale.code + " source/mirror mismatch: " + detail);
                    }
                }
                else if (locale.required) failures.Add("required locale mirror missing: " + localeMirror);
            }
        }
    }
}

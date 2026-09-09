// =============================================================================
// LocaleParityRegression -- exact key/value-shape parity for policy-listed locales.
// It never treats every JSON file in Canonical as a locale.
//
// RED-FIRST RECIPE (revert immediately): on any required non-base locale, remove
// feedback.title from the Resources copy, add orphan.probe to StreamingAssets, and
// blank feedback.body. The result must name mirror drift, one missing/extra key and
// one empty entry. Adding a probe key to English alone must report it missing from
// every required locale, which is the forward-change enforcement this gate owns.
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
            reason = "LOCALE PARITY OK -- " + policy.supportedLocales.Count +
                     " policy-listed locale(s), exact canonical key parity and nonempty values; " +
                     LocalizationAuditIO.DescribeLocaleStates(policy);
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

            AddEmptyFailures(policy.baseLocale, collection.id, baseTable, failures);

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
                bool sourceExists = File.Exists(LocalizationAuditIO.FullPath(localeSource));
                bool mirrorExists = File.Exists(LocalizationAuditIO.FullPath(localeMirror));
                if (!sourceExists || !mirrorExists)
                {
                    if (locale.required)
                    {
                        if (!sourceExists) failures.Add("required locale source missing: " + localeSource);
                        if (!mirrorExists) failures.Add("required locale mirror missing: " + localeMirror);
                    }
                    // Optional locales are audited once either canonical copy exists:
                    // a one-sided mirror is still drift and must not silently pass.
                    else if (sourceExists != mirrorExists)
                        failures.Add("optional locale has only one canonical copy: " + locale.code);
                    if (!sourceExists) continue;
                }

                var translated = new Dictionary<string, string>();
                ioFailures.Clear();
                if (!LocalizationAuditIO.TryReadFlatJson(localeSource, out translated, ioFailures))
                {
                    foreach (string failure in ioFailures) failures.Add(failure);
                    continue;
                }
                var missing = baseTable.Keys.Where(k => !translated.ContainsKey(k)).OrderBy(k => k, StringComparer.Ordinal).ToList();
                var extra = translated.Keys.Where(k => !baseTable.ContainsKey(k)).OrderBy(k => k, StringComparer.Ordinal).ToList();
                if (missing.Count > 0) failures.Add(locale.code + " missing=" + missing.Count + LocalizationAuditIO.Sample(missing));
                if (extra.Count > 0) failures.Add(locale.code + " extra=" + extra.Count + LocalizationAuditIO.Sample(extra));
                AddEmptyFailures(locale.code, collection.id, translated, failures);

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
            }
        }

        private static void AddEmptyFailures(string localeCode, string collectionId,
            IDictionary<string, string> table, ICollection<string> failures)
        {
            var empty = table.Where(p => string.IsNullOrWhiteSpace(p.Value)).Select(p => p.Key)
                .OrderBy(k => k, StringComparer.Ordinal).ToList();
            if (empty.Count > 0)
                failures.Add(collectionId + "/" + localeCode + " empty=" + empty.Count + LocalizationAuditIO.Sample(empty));
        }
    }
}

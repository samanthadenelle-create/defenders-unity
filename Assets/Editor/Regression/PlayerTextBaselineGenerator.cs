// =============================================================================
// PlayerTextBaselineGenerator -- the ONLY writer of literalDebtBaseline.
// It preserves the broader report inventory in docs/localization/manifest.json,
// replaces only the focused baseline block, and always regenerates as UNREVIEWED.
// An armed baseline cannot be silently refreshed to absorb new debt: generation
// refuses until a reviewer deliberately disarms/removes that block.
//
// Batch/editor entry points:
//   -executeMethod DeNelle.Editor.Regression.PlayerTextBaselineGenerator.GenerateUnreviewedBaseline
//   -executeMethod DeNelle.Editor.Regression.PlayerTextBaselineGenerator.ArmReviewedBaseline
// =============================================================================
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace DeNelle.Editor.Regression
{
    public static class PlayerTextBaselineGenerator
    {
        [MenuItem("Defenders/Localization/Generate Unreviewed Literal Debt Baseline")]
        public static void GenerateUnreviewedBaseline()
        {
            string outcome;
            if (!TryGenerate(out outcome))
            {
                Debug.LogError(outcome);
                throw new InvalidOperationException(outcome);
            }
            Debug.Log(outcome);
        }

        [MenuItem("Defenders/Localization/Arm Reviewed Literal Debt Baseline")]
        public static void ArmReviewedBaseline()
        {
            string outcome;
            if (!TryArm(out outcome))
            {
                Debug.LogError(outcome);
                throw new InvalidOperationException(outcome);
            }
            Debug.Log(outcome);
        }

        internal static bool TryGenerate(out string outcome)
        {
            var failures = new List<string>();
            LocalizationAuditIO.Policy policy;
            if (!LocalizationAuditIO.TryLoadPolicy(out policy, failures))
            {
                outcome = "LITERAL BASELINE GENERATION FAIL -- " + LocalizationAuditIO.JoinFailures(failures);
                return false;
            }

            JObject manifest;
            LocalizationAuditIO.LiteralDebtBaseline existing;
            bool hasExisting;
            if (!LocalizationAuditIO.TryReadManifestAndBaseline(policy, out manifest, out existing, out hasExisting, failures))
            {
                outcome = "LITERAL BASELINE GENERATION FAIL -- " + LocalizationAuditIO.JoinFailures(failures);
                return false;
            }
            if (hasExisting && existing.reviewed)
            {
                outcome = "LITERAL BASELINE GENERATION FAIL -- reviewed baseline is armed; refusing to absorb current code. " +
                          "Review the regression delta, then explicitly set literalDebtBaseline.reviewed=false before regeneration.";
                return false;
            }

            if (manifest == null)
            {
                manifest = new JObject
                {
                    ["schemaVersion"] = 1,
                    ["generatedBy"] = "PlayerTextBaselineGenerator",
                    ["mode"] = "report-only",
                    ["entries"] = new JArray()
                };
            }

            List<LocalizationLiteralScanner.Finding> findings = LocalizationLiteralScanner.Scan(policy, failures);
            string deterministic;
            LocalizationLiteralScanner.VerifyDeterministic(policy, findings, out deterministic, failures);
            if (failures.Count > 0)
            {
                outcome = "LITERAL BASELINE GENERATION FAIL -- " + LocalizationAuditIO.JoinFailures(failures);
                return false;
            }

            var exempt = new HashSet<string>(policy.exemptions.Select(e => e.fingerprint), StringComparer.Ordinal);
            var debt = findings.Where(f => !exempt.Contains(f.Fingerprint)).ToList();
            LocalizationAuditIO.LiteralDebtBaseline baseline = LocalizationAuditIO.BuildLiteralBaseline(policy, debt, false);

            // Building twice and requiring token equality pins serialization order and content.
            // This is a deterministic check, not a count-only confidence statement.
            JToken first = JToken.FromObject(baseline);
            JToken second = JToken.FromObject(LocalizationAuditIO.BuildLiteralBaseline(policy, debt, false));
            if (!JToken.DeepEquals(first, second))
            {
                outcome = "LITERAL BASELINE GENERATION FAIL -- identical inputs produced different baseline tokens";
                return false;
            }

            manifest[LocalizationAuditIO.LiteralBaselineProperty] = first;
            string relative = string.IsNullOrWhiteSpace(policy.manifestPath)
                ? "docs/localization/manifest.json" : policy.manifestPath;
            string fullPath = LocalizationAuditIO.FullPath(relative);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath) ?? LocalizationAuditIO.ProjectRoot);
            File.WriteAllText(fullPath, SerializeManifest(manifest), new UTF8Encoding(false));

            failures.Clear();
            JObject writtenManifest;
            LocalizationAuditIO.LiteralDebtBaseline written;
            bool hasWritten;
            if (!LocalizationAuditIO.TryReadManifestAndBaseline(policy, out writtenManifest, out written, out hasWritten, failures) ||
                !hasWritten || written.reviewed || written.entries.Count != debt.Count ||
                !JToken.DeepEquals(first, writtenManifest[LocalizationAuditIO.LiteralBaselineProperty]))
            {
                if (failures.Count == 0) failures.Add("written baseline did not round-trip byte-semantically to the generated token");
                outcome = "LITERAL BASELINE GENERATION FAIL -- " + LocalizationAuditIO.JoinFailures(failures);
                return false;
            }

            outcome = "LITERAL BASELINE GENERATED UNREVIEWED -- " + debt.Count + " exact debt fingerprint(s), " +
                      exempt.Count + " exemption(s); " + deterministic + "; broader inventory preserved";
            return true;
        }

        internal static bool TryArm(out string outcome)
        {
            var failures = new List<string>();
            LocalizationAuditIO.Policy policy;
            if (!LocalizationAuditIO.TryLoadPolicy(out policy, failures))
            {
                outcome = "LITERAL BASELINE ARM FAIL -- " + LocalizationAuditIO.JoinFailures(failures);
                return false;
            }

            JObject manifest;
            LocalizationAuditIO.LiteralDebtBaseline baseline;
            bool hasBaseline;
            if (!LocalizationAuditIO.TryReadManifestAndBaseline(policy, out manifest, out baseline, out hasBaseline, failures) || !hasBaseline)
            {
                if (failures.Count == 0) failures.Add("generate the shared-scanner baseline before arming it");
                outcome = "LITERAL BASELINE ARM FAIL -- " + LocalizationAuditIO.JoinFailures(failures);
                return false;
            }

            List<LocalizationLiteralScanner.Finding> findings = LocalizationLiteralScanner.Scan(policy, failures);
            string deterministic;
            LocalizationLiteralScanner.VerifyDeterministic(policy, findings, out deterministic, failures);
            var exempt = new HashSet<string>(policy.exemptions.Select(e => e.fingerprint), StringComparer.Ordinal);
            var current = new HashSet<string>(findings.Where(f => !exempt.Contains(f.Fingerprint)).Select(f => f.Fingerprint), StringComparer.Ordinal);
            var recorded = new HashSet<string>(baseline.entries.Select(e => e.fingerprint), StringComparer.Ordinal);
            var added = current.Where(f => !recorded.Contains(f)).OrderBy(f => f, StringComparer.Ordinal).ToList();
            var stale = recorded.Where(f => !current.Contains(f)).OrderBy(f => f, StringComparer.Ordinal).ToList();
            if (added.Count > 0) failures.Add("cannot arm with NEW current fingerprint(s)=" + added.Count + LocalizationAuditIO.Sample(added));
            if (stale.Count > 0) failures.Add("cannot arm with STALE recorded fingerprint(s)=" + stale.Count + LocalizationAuditIO.Sample(stale));
            if (failures.Count > 0)
            {
                outcome = "LITERAL BASELINE ARM FAIL -- " + LocalizationAuditIO.JoinFailures(failures);
                return false;
            }

            ((JObject)manifest[LocalizationAuditIO.LiteralBaselineProperty])["reviewed"] = true;
            string relative = string.IsNullOrWhiteSpace(policy.manifestPath)
                ? "docs/localization/manifest.json" : policy.manifestPath;
            File.WriteAllText(LocalizationAuditIO.FullPath(relative), SerializeManifest(manifest), new UTF8Encoding(false));

            string regressionReason;
            if (!PlayerTextLiteralLeakRegression.Run(out regressionReason) ||
                regressionReason.IndexOf("PLAYER TEXT LITERAL LEAK OK", StringComparison.Ordinal) < 0)
            {
                outcome = "LITERAL BASELINE ARM FAIL -- post-write regression did not produce an armed OK: " + regressionReason;
                return false;
            }
            outcome = "LITERAL BASELINE ARMED -- " + recorded.Count + " reviewed debt fingerprint(s); " +
                      deterministic + "; post-write fail-on-new/stale regression green";
            return true;
        }

        private static string SerializeManifest(JObject manifest)
        {
            // Match Windows PowerShell ConvertTo-Json's deterministic escaping so the
            // repository-side -Check and the Unity-side baseline writer agree byte-for-byte.
            string json = JsonConvert.SerializeObject(manifest, Formatting.None);
            // Windows PowerShell 5.1 emits apostrophes as \u0027 while retaining the
            // ordinary JSON \" escape for quotes inside values. JSON syntax never uses
            // a raw apostrophe, so this replacement is unambiguous.
            return json.Replace("'", "\\u0027")
                .Replace("<", "\\u003c")
                .Replace(">", "\\u003e")
                .Replace("&", "\\u0026") + "\n";
        }
    }
}

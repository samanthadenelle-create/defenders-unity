// =============================================================================
// PlayerTextLiteralLeakRegression -- shrinking baseline for hard-coded player copy.
// No reviewed manifest means REPORT-ONLY, explicitly, not a green claim that the
// migration is complete. A present malformed policy/manifest is always red.
//
// RED-FIRST RECIPES (revert immediately):
//  1. Add a BuildObsidianButton(..., "LOCALIZATION RATCHET PROBE", ...). With a
//     reviewed manifest it must be a NEW fingerprint and fail.
//  2. Change an existing baselined sentence. It must appear once NEW and once
//     STALE, proving the baseline is not a blanket file exemption.
// =============================================================================
using System;
using System.Collections.Generic;
using System.Linq;

namespace DeNelle.Editor.Regression
{
    public static class PlayerTextLiteralLeakRegression
    {
        public static bool Run(out string reason)
        {
            var failures = new List<string>();
            LocalizationAuditIO.Policy policy;
            if (!LocalizationAuditIO.TryLoadPolicy(out policy, failures))
            {
                reason = "PLAYER TEXT LITERAL LEAK FAIL -- " + LocalizationAuditIO.JoinFailures(failures);
                return false;
            }

            List<LocalizationLiteralScanner.Finding> findings;
            try { findings = LocalizationLiteralScanner.Scan(policy, failures); }
            catch (Exception ex)
            {
                failures.Add("literal scanner threw: " + ex.GetType().Name + ": " + ex.Message);
                findings = new List<LocalizationLiteralScanner.Finding>();
            }
            string determinismDetail;
            if (failures.Count == 0)
                LocalizationLiteralScanner.VerifyDeterministic(policy, findings, out determinismDetail, failures);
            else
                determinismDetail = "initial scan failed";
            if (failures.Count > 0)
            {
                reason = "PLAYER TEXT LITERAL LEAK FAIL -- " + LocalizationAuditIO.JoinFailures(failures);
                return false;
            }

            Newtonsoft.Json.Linq.JObject manifest;
            LocalizationAuditIO.LiteralDebtBaseline baselineDocument;
            bool hasBaseline;
            if (!LocalizationAuditIO.TryReadManifestAndBaseline(policy, out manifest, out baselineDocument,
                out hasBaseline, failures))
            {
                reason = "PLAYER TEXT LITERAL LEAK FAIL -- " + LocalizationAuditIO.JoinFailures(failures);
                return false;
            }
            string manifestRelative = string.IsNullOrWhiteSpace(policy.manifestPath)
                ? "docs/localization/manifest.json" : policy.manifestPath;
            if (manifest == null)
            {
                reason = "PLAYER TEXT LITERAL LEAK FAIL -- required inventory manifest absent at " + manifestRelative;
                return false;
            }
            if (!hasBaseline)
            {
                reason = RegressionOutcome.PartialSkip("player-text literal baseline",
                    "broader inventory exists but " + LocalizationAuditIO.LiteralBaselineProperty +
                    " has not been generated; " + determinismDetail + "; fail-on-new/stale debt is not armed");
                return true;
            }

            var baseline = new HashSet<string>(baselineDocument.entries.Select(e => e.fingerprint), StringComparer.Ordinal);

            var exempt = new HashSet<string>(policy.exemptions.Select(e => e.fingerprint), StringComparer.Ordinal);
            var current = new HashSet<string>(findings.Where(f => !exempt.Contains(f.Fingerprint)).Select(f => f.Fingerprint), StringComparer.Ordinal);
            var added = findings.Where(f => current.Contains(f.Fingerprint) && !baseline.Contains(f.Fingerprint)).ToList();
            var stale = baseline.Where(f => !current.Contains(f)).OrderBy(f => f, StringComparer.Ordinal).ToList();
            if (!baselineDocument.reviewed)
            {
                reason = RegressionOutcome.PartialSkip("player-text literal baseline",
                    "shared-scanner baseline is UNREVIEWED; current=" + current.Count + ", baseline=" + baseline.Count +
                    ", new=" + added.Count + ", stale=" + stale.Count + "; " + determinismDetail +
                    "; arm only through PlayerTextBaselineGenerator.ArmReviewedBaseline after review");
                return true;
            }
            if (added.Count > 0)
            {
                failures.Add("NEW hard-coded player-copy candidate(s)=" + added.Count + ": " +
                    string.Join(" | ", added.Take(8).Select(Describe).ToArray()));
            }
            if (stale.Count > 0) failures.Add("STALE debt fingerprint(s)=" + stale.Count + LocalizationAuditIO.Sample(stale));

            if (failures.Count > 0)
            {
                reason = "PLAYER TEXT LITERAL LEAK FAIL -- " + LocalizationAuditIO.JoinFailures(failures);
                return false;
            }
            reason = "PLAYER TEXT LITERAL LEAK OK -- reviewed baseline armed; " + current.Count +
                     " existing debt candidate(s), 0 new, 0 stale; " + exempt.Count + " exact exemption(s); " +
                     determinismDetail;
            return true;
        }

        private static string Describe(LocalizationLiteralScanner.Finding finding)
        {
            string preview = (finding.Value ?? string.Empty).Replace('\n', ' ').Replace('\r', ' ');
            if (preview.Length > 64) preview = preview.Substring(0, 64) + "...";
            return finding.Path + ":" + finding.Line + " " + finding.Member + " " + finding.Sink + " [" + preview + "]";
        }
    }
}

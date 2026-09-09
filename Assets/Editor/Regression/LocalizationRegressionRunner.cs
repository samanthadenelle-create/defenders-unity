using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace DeNelle.Editor.Regression
{
    /// <summary>Fast focused gate for WO-1605 localization infrastructure.</summary>
    public static class LocalizationRegressionRunner
    {
        public static void RunAll()
        {
            var failures = new List<string>();
            var log = new StringBuilder();
            int passed = 0;

            Run("authority", LocalizationAuthorityRegression.Run, failures, log, ref passed);
            Run("literal-leak", PlayerTextLiteralLeakRegression.Run, failures, log, ref passed);
            Run("locale-parity", LocaleParityRegression.Run, failures, log, ref passed);
            Run("smart-arguments", SmartArgumentRegression.Run, failures, log, ref passed);
            Run("settings", SettingsLocalizationRegression.Run, failures, log, ref passed);
            Run("glyph-coverage", GlyphCoverageRegression.Run, failures, log, ref passed);

            if (failures.Count == 0)
            {
                log.AppendLine("LOCALIZATION_REGRESSION_OK " + passed + "/6 suites");
                Debug.Log(log.ToString());
                return;
            }

            log.AppendLine("LOCALIZATION_REGRESSION_FAIL " + failures.Count + " failure(s)");
            foreach (string failure in failures) log.AppendLine("  - " + failure);
            Debug.LogError(log.ToString());
        }

        private delegate bool Suite(out string reason);

        private static void Run(
            string tag,
            Suite suite,
            ICollection<string> failures,
            StringBuilder log,
            ref int passed)
        {
            if (!suite(out string reason))
            {
                failures.Add(tag + ": " + reason);
                return;
            }

            passed++;
            log.AppendLine("[" + tag + "] " + reason);
        }
    }
}

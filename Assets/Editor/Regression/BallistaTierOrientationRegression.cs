using System;
using System.Collections.Generic;
using DeNelle.Core.Catalog;
using DeNelle.Village;
using UnityEngine;

namespace DeNelle.Editor.Regression
{
    /// <summary>Guards the independently measured L2/L3 Ballista art correction.</summary>
    public static class BallistaTierOrientationRegression
    {
        public static bool Run(out string reason)
        {
            var failures = new List<string>();
            var entry = new CatalogEntry
            {
                id = "tower_ballista",
                repo = new RepoProps
                {
                    upgradeOrientationEuler = new[]
                    {
                        new[] { 90f, 0f, 0f },
                        new[] { 90f, 0f, 0f }
                    }
                }
            };

            var expected = Quaternion.Euler(90f, 0f, 0f);
            for (int level = 2; level <= 3; level++)
            {
                var actual = StructureFactory.OptsForUpgradeLevel(entry, level).LocalRotation;
                if (!actual.HasValue || Quaternion.Angle(actual.Value, expected) > 0.01f)
                    failures.Add("Ballista L" + level + " did not receive its authored +90 X tier correction");
            }

            if (StructureFactory.OptsForUpgradeLevel(entry, 4).LocalRotation.HasValue)
                failures.Add("an unauthored tier inherited a rotation correction");

            reason = failures.Count == 0
                ? "BALLISTA_TIER_ORIENTATION_OK L2/L3 corrected independently; unauthored tiers unchanged"
                : "BALLISTA_TIER_ORIENTATION_FAIL x" + failures.Count + " :: " + string.Join(" | ", failures);
            return failures.Count == 0;
        }
    }
}

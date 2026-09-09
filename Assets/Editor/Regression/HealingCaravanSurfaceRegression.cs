using System;
using System.Collections.Generic;
using System.IO;
using DeNelle.Village;
using UnityEngine;

namespace DeNelle.Editor.Regression
{
    public static class HealingCaravanSurfaceRegression
    {
        public static bool Run(out string reason)
        {
            var failures = new List<string>();
            if (HealingFountain.CanOfferUpgrade(3, 3))
                failures.Add("max-level Caravan still advertises an upgrade interaction");
            if (!HealingFountain.CanOfferUpgrade(2, 3))
                failures.Add("level-2 Caravan lost its real final upgrade");

            string path = Path.Combine(Application.dataPath,
                "_Modules/Village/Buildings/CaravanHealField.cs");
            string source = File.Exists(path) ? File.ReadAllText(path) : string.Empty;
            if (!source.Contains("Find(\"ShockWave\")") ||
                !source.Contains("filledCentre.gameObject.SetActive(false)"))
                failures.Add("the marker's filled ShockWave centre is not disabled");

            reason = failures.Count == 0
                ? "HEALING_CARAVAN_SURFACE_OK max level is passive; perimeter retained; yellow centre disabled"
                : "HEALING_CARAVAN_SURFACE_FAIL x" + failures.Count + " :: " + string.Join(" | ", failures);
            return failures.Count == 0;
        }
    }
}

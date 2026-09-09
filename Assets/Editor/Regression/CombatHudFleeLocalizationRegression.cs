using System;
using System.Collections.Generic;
using System.IO;
using DeNelle.Core.UI;

namespace DeNelle.Editor.Regression
{
    /// <summary>Protects both meanings of the two-tap combat Flee control.</summary>
    public static class CombatHudFleeLocalizationRegression
    {
        private const string HudPath = "Assets/_Modules/HUD/Kit/HudKitController.cs";

        public static bool Run(out string reason)
        {
            var failures = new List<string>();
            if (!string.Equals(CombatHudText.ResolveFlee(false), "Flee", StringComparison.Ordinal))
                failures.Add("initial action does not resolve hud.combat.flee.action");
            if (!string.Equals(CombatHudText.ResolveFlee(true), "Flee?", StringComparison.Ordinal))
                failures.Add("armed action does not resolve hud.combat.flee.confirm");

            string source = File.Exists(HudPath) ? File.ReadAllText(HudPath) : string.Empty;
            if (source.Length == 0) failures.Add("HudKitController source is unavailable");
            else
            {
                Require(source, "BuildObsidianButton(pool, CombatHudText.ResolveFlee(false)", failures,
                    "initial button construction bypasses CombatHudText");
                Require(source, "_fleeLabel.text = CombatHudText.ResolveFlee(false)", failures,
                    "confirmed/disarmed state bypasses CombatHudText");
                Require(source, "_fleeLabel.text = CombatHudText.ResolveFlee(true)", failures,
                    "armed state bypasses CombatHudText");
                Require(source,
                    "CombatHudText.ResolveFlee(Time.unscaledTime < _fleeArmedUntil)", failures,
                    "locale refresh does not preserve the current Flee meaning");
                if (source.Contains("BuildObsidianButton(pool, \"Flee\"") ||
                    source.Contains("_fleeLabel.text = \"Flee"))
                    failures.Add("executable Flee copy remains hard-coded in HudKitController");
            }

            if (failures.Count > 0)
            {
                reason = "COMBAT HUD FLEE LOCALIZATION FAIL -- " + string.Join(" | ", failures);
                return false;
            }
            reason = "COMBAT HUD FLEE LOCALIZATION OK -- initial, armed, disarmed and locale-refresh states use two semantic keys";
            return true;
        }

        private static void Require(string source, string token, ICollection<string> failures, string message)
        {
            if (source.IndexOf(token, StringComparison.Ordinal) < 0) failures.Add(message);
        }
    }
}

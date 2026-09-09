using System;
using System.Collections.Generic;
using System.IO;
using DeNelle.Village;
using UnityEngine;

namespace DeNelle.Editor.Regression
{
    public static class MageProtectionDockRegression
    {
        public static bool Run(out string reason)
        {
            var failures = new List<string>();
            var shell = AbilityCatalog.Find("mage", AbilitySlot.W);
            if (shell == null || shell.Id != "mage.shell" || shell.Effect != "shield")
                failures.Add("Mage W no longer resolves to the authored Arcane Shell shield");
            else if (shell.VfxCast != "ShieldBuff_Cast" || shell.VfxResidual != "ShieldBuff_Aura")
                failures.Add("Arcane Shell lost its cast/shield-aura presentation");

            string hud = Read("_Modules/HUD/Kit/HudKitController.cs");
            string bridge = Read("_Modules/Village/HUD/HudKitCommandBridge.cs");
            if (!hud.Contains("SetCaption(\"SHELL\")") || !hud.Contains("HeroVitals.ClassId"))
                failures.Add("the adaptive defensive medallion is not class-aware for Mage");
            if (!bridge.Contains("abilities.TryCast(AbilitySlot.W)"))
                failures.Add("the Mage protection face does not cast its W spell");

            reason = failures.Count == 0
                ? "MAGE_PROTECTION_DOCK_OK Arcane Shell replaces physical Block and carries shield aura"
                : "MAGE_PROTECTION_DOCK_FAIL x" + failures.Count + " :: " + string.Join(" | ", failures);
            return failures.Count == 0;
        }

        private static string Read(string relative)
        {
            string path = Path.Combine(Application.dataPath, relative);
            return File.Exists(path) ? File.ReadAllText(path) : string.Empty;
        }
    }
}

// =============================================================================
// HeroElementCastVfxRegression - WO-875 focused standalone oracle.
// -----------------------------------------------------------------------------
// Registered in DataRegression.RunAll. The source assertion deliberately counts
// the committed PlayCast call so a legacy fallback cannot double-flash keyless casts.
//
// Batchmode:
//   run-unity-method.ps1 -Method DeNelle.Editor.HeroElementCastVfxRegression.RunStandalone
//                        -LogName wo875-element-cast.log
//                        -ExpectMarker HERO_ELEMENT_CAST_VFX_OK
// =============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using DeNelle.Village;

namespace DeNelle.Editor
{
    public static class HeroElementCastVfxRegression
    {
        private const string HeroPath = "Assets/_Modules/Village/Hero/HeroAbilities.cs";
        private const string StreamingRegistry =
            "Assets/StreamingAssets/Data/Canonical/motion-castings.json";
        private const string ResourcesRegistry =
            "Assets/Resources/Data/Canonical/motion-castings.json";

        public static void RunStandalone()
        {
            Run(out _);
        }

        public static bool Run(out string reason)
        {
            var failures = new List<string>();
            try
            {
                PinCommittedCastPath(failures);
                PinElementSemantics(failures);
                PinRegistryMirror(failures);
            }
            catch (Exception ex)
            {
                failures.Add("oracle threw " + ex.GetType().Name + ": " + ex.Message);
            }

            if (failures.Count > 0)
            {
                reason = "[hero-element-cast] FAIL " + failures.Count + ": " +
                         string.Join("; ", failures);
                Debug.LogError("HERO_ELEMENT_CAST_VFX_FAIL " + reason);
                return false;
            }

            reason = "[hero-element-cast] OK element flash + interruptible windup + registry mirror";
            Debug.Log("HERO_ELEMENT_CAST_VFX_OK " + reason);
            return true;
        }

        private static void PinCommittedCastPath(List<string> failures)
        {
            string source = File.ReadAllText(HeroPath);
            int resolved = source.IndexOf("private void CastResolved", StringComparison.Ordinal);
            int factory = source.IndexOf(
                "SpellVfxFactory.PlayCast(def.EffectEnum, _heroClass, def.UnityColor, origin);",
                resolved,
                StringComparison.Ordinal);
            // WO-1614 (f4e4630e3, 2026-09-09): the registry beat now receives the RESOLVED
            // animVariant, not the pressed-slot castVariant - that rename IS the fix for "VFX
            // followed the hotbar seat". Re-pointed 2026-09-09 by the lead with that ruling; the
            // order this pins (flash -> registry beat -> effect) is unchanged at the producer.
            int registry = source.IndexOf("PlayCastVfxKey(def, origin, animVariant);", resolved,
                StringComparison.Ordinal);
            int effect = source.IndexOf("ResolveEffect(def, origin);", resolved,
                StringComparison.Ordinal);

            if (resolved < 0 || factory < resolved || registry < factory || effect < registry)
                failures.Add("CastResolved must play the element flash, preserve the registry beat, then resolve the effect");

            const string committedCall =
                "SpellVfxFactory.PlayCast(def.EffectEnum, _heroClass, def.UnityColor, origin);";
            int committedCount = Count(source, committedCall);
            if (committedCount != 1)
                failures.Add("exactly one committed element PlayCast authority required; found " + committedCount);

            int spawnVfx = source.IndexOf("private void SpawnVfx", StringComparison.Ordinal);
            if (spawnVfx >= 0 && source.IndexOf("SpellVfxFactory.PlayCast", spawnVfx,
                    StringComparison.Ordinal) >= 0)
                failures.Add("SpawnVfx must retain AbilityVfxKit fallback without replaying the committed cast flash");

            int tryCastWindup = source.IndexOf("BeginWindupTelegraph(def);", StringComparison.Ordinal);
            int extraCastWindup = tryCastWindup < 0 ? -1 : source.IndexOf(
                "BeginWindupTelegraph(def);", tryCastWindup + 1, StringComparison.Ordinal);
            int routine = source.IndexOf("IEnumerator CastRoutine", StringComparison.Ordinal);
            int interruptedEnd = source.IndexOf("EndWindupTelegraph(\"move-interrupt\")", routine,
                StringComparison.Ordinal);
            int committedEnd = source.IndexOf("EndWindupTelegraph(\"cast-committed\")", routine,
                StringComparison.Ordinal);
            if (tryCastWindup < 0 || extraCastWindup < 0 || routine < 0 ||
                interruptedEnd < routine || committedEnd < routine)
                failures.Add("slot and extra casts must start the shared windup and CastRoutine must end it on interrupt/commit");

            int gate = source.IndexOf("private const bool RegistryOnlyMotionVfx", StringComparison.Ordinal);
            if (gate >= 0 && factory > gate && source.Substring(gate, factory - gate).Contains(
                    "if (RegistryOnlyMotionVfx)\n                SpellVfxFactory.PlayCast"))
                failures.Add("RegistryOnlyMotionVfx must not gate the semantic element flash");
        }

        private static void PinElementSemantics(List<string> failures)
        {
            // abilities.json Arcane Bolt / Arcane Shell: #b388ff.
            Color arcane = new Color(179f / 255f, 136f / 255f, 1f);
            if (SpellVfxFactory.ResolveElement(AbilityEffect.Meteor, "mage", Color.red) != SpellElement.Fire)
                failures.Add("Meteor must resolve to Fire");
            if (SpellVfxFactory.ResolveElement(AbilityEffect.Aoe, "mage", Color.cyan) != SpellElement.Frost)
                failures.Add("Mage AoE must resolve to Frost");
            if (SpellVfxFactory.ResolveElement(AbilityEffect.Strike, "mage", arcane) != SpellElement.Arcane)
                failures.Add("violet Mage strike must resolve to Arcane");
            if (SpellVfxFactory.ResolveElement(AbilityEffect.Heal, "mage", Color.white) != SpellElement.Holy)
                failures.Add("Heal must resolve to Holy");
            if (SpellVfxFactory.ResolveElement(AbilityEffect.Strike, "ranger", Color.red) != SpellElement.Physical)
                failures.Add("Ranger strike must remain Physical");
        }

        private static void PinRegistryMirror(List<string> failures)
        {
            if (!File.Exists(StreamingRegistry) || !File.Exists(ResourcesRegistry))
            {
                failures.Add("motion-castings canonical pair must exist because ActionBundleCatalog reads the Resources-first path");
                return;
            }

            if (!string.Equals(File.ReadAllText(StreamingRegistry), File.ReadAllText(ResourcesRegistry),
                    StringComparison.Ordinal))
                failures.Add("motion-castings StreamingAssets and Resources copies differ");
        }

        private static int Count(string source, string token)
        {
            int count = 0;
            int at = 0;
            while ((at = source.IndexOf(token, at, StringComparison.Ordinal)) >= 0)
            {
                count++;
                at += token.Length;
            }
            return count;
        }
    }
}

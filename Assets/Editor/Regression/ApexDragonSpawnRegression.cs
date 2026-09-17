using System;
using System.Collections.Generic;
using System.IO;
using DeNelle.Village;
using UnityEngine;

namespace DeNelle.Editor.Regression
{
    /// <summary>Pins the Addressables type contract proven by the Wave-20 player log.</summary>
    public static class ApexDragonSpawnRegression
    {
        public static bool Run(out string reason)
        {
            var failures = new List<string>();
            string path = Path.Combine(Application.dataPath, "_Modules/Village/Waves/WaveManager.cs");
            string source = File.Exists(path) ? File.ReadAllText(path) : string.Empty;

            if (source.Contains("LoadEnemyAsset<DragonBoss>(\"Enemies/Boss_Dragon\")"))
                failures.Add("Wave 20 still requests a component type from a GameObject Addressable");
            if (!source.Contains("LoadEnemyPrefab(\"Boss_Dragon\")") ||
                !source.Contains("GetComponentInChildren<DragonBoss>(true)"))
                failures.Add("the GameObject prefab-to-DragonBoss resolution is incomplete");
            if (!source.Contains("_apexSpawnPending ||"))
                failures.Add("the wave can clear while its apex prefab is still loading");

            foreach (int wave in new[] { 20, 25, 30, 35, 40 })
                if (!WaveManager.IsRecurringDragonWave(wave))
                    failures.Add($"wave {wave} is missing from the five-wave dragon cadence");
            foreach (int wave in new[] { 19, 21, 24, 26, 29 })
                if (WaveManager.IsRecurringDragonWave(wave))
                    failures.Add($"non-cadence wave {wave} incorrectly fields the dragon");

            float hp20 = WaveManager.DragonHpForWave(4200f, 20);
            float hp25 = WaveManager.DragonHpForWave(4200f, 25);
            float hp30 = WaveManager.DragonHpForWave(4200f, 30);
            if (!(Math.Abs(hp20 - 4200f) < 0.01f && hp25 > hp20 && hp30 > hp25))
                failures.Add($"dragon HP is not progressive: {hp20}/{hp25}/{hp30}");

            float damage20 = WaveManager.DragonDamageMultiplierForWave(20);
            float damage25 = WaveManager.DragonDamageMultiplierForWave(25);
            float damage30 = WaveManager.DragonDamageMultiplierForWave(30);
            if (!(Math.Abs(damage20 - 1f) < 0.001f && damage25 > damage20 && damage30 > damage25))
                failures.Add($"dragon damage is not progressive: {damage20}/{damage25}/{damage30}");

            float cadence20 = WaveManager.DragonAttackIntervalMultiplierForWave(20);
            float cadence25 = WaveManager.DragonAttackIntervalMultiplierForWave(25);
            float cadence30 = WaveManager.DragonAttackIntervalMultiplierForWave(30);
            if (!(Math.Abs(cadence20 - 1f) < 0.001f && cadence25 < cadence20 && cadence30 < cadence25))
                failures.Add($"dragon attacks do not accelerate: {cadence20}/{cadence25}/{cadence30}");
            if (!source.Contains("ResolveRecurringDragon(waveId") ||
                !source.Contains("ApplyEncounterDifficulty("))
                failures.Add("recurring schedule/scaling is calculated but not applied to spawned dragons");
            if (!source.Contains("waveId > FirstDragonWave ? null : current"))
                failures.Add("legacy endless replay can still add an off-cadence dragon (for example wave 37)");

            // -- WO-1836: the dive-swoop's ground-clearance clamp -------------------
            // Calls the REAL clamp (DragonBoss.ResolveSwoopLowY) - pure + static, so no
            // scene, no raycast. Proves the math only; the self-excluded ground cast and
            // the live mesh extent are proven by a headless [Flow:DragonBoss] capture.
            const float lowHeight = 4.5f;
            const float cruise = 100f;   // high enough that the Min(cruise) cap never bites here

            // Ground higher than the target's transform -> the GROUND wins.
            float groundWins = DragonBoss.ResolveSwoopLowY(12f, 0f, cruise, lowHeight, 2f);
            if (Math.Abs(groundWins - 18.5f) > 0.001f)
                failures.Add($"swoop low point ignores the sampled ground: {groundWins} (want 18.5)");

            // Target higher than the ground (on a rooftop) -> the TARGET wins.
            float targetWins = DragonBoss.ResolveSwoopLowY(0f, 12f, cruise, lowHeight, 2f);
            if (Math.Abs(targetWins - 18.5f) > 0.001f)
                failures.Add($"swoop low point ignores the target floor: {targetWins} (want 18.5)");

            // The model's own under-extent must ADD clearance, never be dropped.
            float noExtent = DragonBoss.ResolveSwoopLowY(10f, 0f, cruise, lowHeight, 0f);
            float withExtent = DragonBoss.ResolveSwoopLowY(10f, 0f, cruise, lowHeight, 3f);
            if (!(withExtent > noExtent && Math.Abs(withExtent - noExtent - 3f) < 0.001f))
                failures.Add($"the mesh under-extent is not added to swoop clearance: {noExtent}/{withExtent}");

            // A negative extent (degenerate bounds) must never LOWER the clamp.
            if (Math.Abs(DragonBoss.ResolveSwoopLowY(10f, 0f, cruise, lowHeight, -5f) - noExtent) > 0.001f)
                failures.Add("a negative mesh extent lowers the swoop clamp below ground");

            // The clamp can never exceed cruise height - a floor above cruise would
            // invert the arc and turn the dive into a climb.
            if (DragonBoss.ResolveSwoopLowY(500f, 500f, 40f, lowHeight, 6f) > 40f + 0.001f)
                failures.Add("the swoop low point can rise above cruise height (inverted dive)");

            // And the clamp is never below the sampled ground for any of these cases.
            foreach (float g in new[] { 0f, 3.5f, 20f, 47.25f })
                if (DragonBoss.ResolveSwoopLowY(g, 0f, cruise, lowHeight, 1.75f) < g)
                    failures.Add($"swoop low point falls below sampled ground at groundY={g}");

            reason = failures.Count == 0
                ? "APEX_DRAGON_SPAWN_OK GameObject address resolved; pending load holds clear gate; " +
                  "dragon waves=20/25/30/... with progressive HP/damage/attack cadence; " +
                  "swoop low point clamps to max(ground,target)+clearance+mesh extent, capped at cruise"
                : "APEX_DRAGON_SPAWN_FAIL x" + failures.Count + " :: " + string.Join(" | ", failures);
            return failures.Count == 0;
        }
    }
}

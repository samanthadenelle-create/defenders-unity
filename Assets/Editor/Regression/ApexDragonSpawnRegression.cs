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

            reason = failures.Count == 0
                ? "APEX_DRAGON_SPAWN_OK GameObject address resolved; pending load holds clear gate; " +
                  "dragon waves=20/25/30/... with progressive HP/damage/attack cadence"
                : "APEX_DRAGON_SPAWN_FAIL x" + failures.Count + " :: " + string.Join(" | ", failures);
            return failures.Count == 0;
        }
    }
}

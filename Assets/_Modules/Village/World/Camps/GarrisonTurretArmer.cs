// =============================================================================
// GarrisonTurretArmer — the SHARED "arm the Watchtower_* props as EnemyOwned
// turrets" scan. EXTRACTED VERBATIM from GarrisonController.ArmGarrisonTurrets so
// BOTH the additive-scene GarrisonController AND the config-driven
// RaidGarrisonSpawner light up the authored watchtowers with the EXACT same logic.
// -----------------------------------------------------------------------------
// Assembly: DeNelle.Village   Namespace: DeNelle.Village.World.Camps
//
// Author label is "Watchtower_*" (GarrisonSceneBuilder.PlaceRing /
// RaidBaseGenerator name the corner/court towers with that token on purpose). Here
// we attach an EnemyOwned DefenseTower so they open fire on the hero + companions.
// Runtime-only (no scene re-bake) and HARD-GUARDED by SceneOwnership.IsEnemyOwned
// so this can NEVER arm a friendly village tower against the player. Idempotent:
// a tower that already carries a DefenseTower is skipped.
//
// Canon: the village is Elarion (never Avalon). ASCII-only runtime strings.
// =============================================================================

using UnityEngine;
using UnityEngine.SceneManagement;
using DeNelle.Core.Diagnostics;   // FlowTrace — WO-1868 style-resolution trace

namespace DeNelle.Village.World.Camps
{
    /// <summary>
    /// Stateless watchtower-arming scan shared by <see cref="GarrisonController"/> and
    /// <see cref="RaidGarrisonSpawner"/>. Scans the given scene's roots, attaches an
    /// EnemyOwned <see cref="DefenseTower"/> to every "Watchtower_*" prop, and returns
    /// how many it armed. No-op (returns 0) unless the active scene is enemy-owned.
    /// </summary>
    public static class GarrisonTurretArmer
    {
        /// <summary>
        /// Arm every "Watchtower_*" prop in <paramref name="scene"/> as an EnemyOwned
        /// turret with the given range/damage/fire-rate. Returns the count armed.
        /// </summary>
        public static int ArmWatchtowers(Scene scene, float range, float damage, float fireRate)
        {
            // HARD GUARD: only ever arm turrets in an enemy-owned context. If the
            // ownership flag is not set (false / not yet wired) we do nothing —
            // friendly village towers are never touched (safe default).
            if (!SceneOwnership.IsEnemyOwned) return 0;

            int armed = 0;
            // Scan this controller's own scene roots only (not other additive scenes).
            if (!scene.IsValid()) return 0;
            var roots = scene.GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++)
            {
                if (roots[i] == null) continue;
                var towers = roots[i].GetComponentsInChildren<Transform>(true);
                for (int j = 0; j < towers.Length; j++)
                {
                    var t = towers[j];
                    if (t == null || t.name == null) continue;
                    // Author label is "Watchtower_*"; "Keep_Core" (stone tower) is the
                    // fortress core, not a firing turret, so it is deliberately excluded.
                    if (t.name.IndexOf("Watchtower", System.StringComparison.OrdinalIgnoreCase) < 0)
                        continue;
                    if (t.GetComponent<DefenseTower>() != null) continue;   // idempotent

                    var dt = t.gameObject.AddComponent<DefenseTower>();
                    dt.Allegiance = TowerAllegiance.EnemyOwned;
                    dt.Range      = range;
                    dt.Damage     = damage;
                    dt.FireRate   = fireRate;
                    dt.CanHitAir  = true;   // elevated turret; hero/companions are ground anyway
                    dt.BoltColor  = new Color(0.95f, 0.3f, 0.2f);   // hostile red bolt
                    // WO-1868 (owner: "can we have the towers in raids shoot more than yellow
                    // pellets?"): an EnemyOwned garrison turret never had a catalog row, so
                    // DefenseTower.ProjectileStyle stayed null/"" -> BoltStyle.Pellet -> the
                    // legacy 0.4m emissive sphere (BuildPelletVisual) — no shape, no motion cue
                    // beyond travel, only a colour (which reads as a plain "yellow"/orange dot to
                    // a colorblind player). "bolt" swaps the primitive to the shaft+tip arrow
                    // silhouette (BuildBoltVisual, already used by every player Archer/Ballista
                    // tower) that visibly re-orients along its flight line, plus the matching
                    // per-tier Hovl arrow key in ProjectileKeyFor (Tier defaults to 1 with no
                    // PlacedStructure, exactly this turret's shape — see
                    // TowerProjectileTierTests.NoPlacedStructure_DefaultsToTier1Key). Distinct in
                    // SHAPE + MOTION, not just colour; damage/range/fire-rate/targeting untouched.
                    dt.ProjectileStyle = "bolt";
                    armed++;
                }
            }

            if (armed > 0)
                FlowTrace.Step("Garrison",
                    $"ArmWatchtowers: armed {armed} EnemyOwned turret(s) in scene '{scene.name}' " +
                    "with projectileStyle='bolt' (WO-1868 — was the unset default 'pellet').");

            return armed;
        }
    }
}

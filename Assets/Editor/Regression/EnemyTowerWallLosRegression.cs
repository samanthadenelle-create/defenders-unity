// =============================================================================
// EnemyTowerWallLosRegression — WO-1808. An ENEMY-OWNED DefenseTower must not
// acquire (and therefore must not shoot) a party member standing behind a wall
// on the "Structure" physics layer.
//
// THE TICKET (owner, build 372984, RaidBase_fortified_garrison, 19:55 2026-09-16):
//   "the tower is attacking through the wall, maybe over but feels like through".
// THE PROVING DATA (logs/device/raid-window-1955.txt): 107 lines of
//   [Flow:DefenseTower] -> FireAtParty (EnemyOwned)
// and ZERO [Flow:TowerLoS] lines in the same window. DefenseTower.BlockedByWall
// emits a throttled TowerLoS line on EVERY call, so it never ran on that path —
// its only caller was the PLAYER-owned pick Acquire(). AcquireParty (the
// enemy-owned pick) filtered on range + the air gate and nothing else.
//
// WHAT THIS ORACLE PROVES, headless, in milliseconds (no play mode, no scene):
//   1. clear line            -> the party candidate IS acquired (the fix did not
//                               make garrison turrets inert).
//   2. Structure wall on the line -> the candidate is REJECTED (AcquireParty
//                               returns null), i.e. the turret has no target and
//                               UpdateEnemyOwned's `if (target == null) return;`
//                               means FireAtParty is never reached.
//   3. Structure wall + a FLYING candidate -> acquired anyway (the flyer
//                               exemption the player path has is preserved).
//   4. SOURCE assertion: BlockedByWall is still called from the player pick AND
//                        BlockedByWallToParty from the party pick, so neither gate
//                        can be silently dropped by a later edit.
//
// HONEST LIMITS (never claimed):
//   * This drives AcquireParty through the public AcquirePartyForTest seam. It does
//     NOT prove the per-frame UpdateEnemyOwned tick runs in a real raid — that is
//     already proven by the owner's own 107 FireAtParty lines.
//   * It does not measure the RAID SCENE's wall colliders; those were read from
//     Assets/Scenes/RaidBase_fortified_garrison.unity in WO-1808 (118 WallSegments,
//     layer 8 = Structure, collider 0.00->5.00 m, enabled, non-trigger).
//   * DEGRADE-OPEN is deliberately NOT exercised as a pass: if the project has no
//     "Structure" layer this suite FAILS with a named reason rather than reading
//     green off a mask of 0.
//
// Callable from DataRegression.RunAll as
//   [enemy-tower-wall-los]
// =============================================================================
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using DeNelle.Village;
using DeNelle.Core.Combat;

namespace DeNelle.Editor.Regression
{
    public static class EnemyTowerWallLosRegression
    {
        // Geometry: tower at the origin, candidate 8 m down +Z (inside the default
        // Range of 14 m), the blocking box halfway between them. The box spans
        // y 0..5 to mirror the raid scene's measured wall collider, so the
        // muzzle (y = 2) -> target (y = 0.5) line passes THROUGH it, not over.
        private const float TargetDistance = 8f;
        private const float WallHeight     = 5f;
        private const float TargetY        = 0.5f;

        /// <summary>
        /// Stand-in party member: the seam DefenseTower.AcquireParty consumes.
        /// <c>Faction</c> mirrors the REAL party bodies exactly — an explicit-interface
        /// <see cref="CombatFaction.Friendly"/>, as declared by <c>HeroHealth:2331</c> and
        /// <c>TroopController:448</c> (<c>SelfFaction</c>, `:458`). Note read at source: AcquireParty
        /// (`DefenseTower.cs:743-772`) filters on IsAlive, range, the air gate and now LoS — it has NO
        /// faction filter — so this value cannot reject the candidate before the wall check; it is set
        /// to Friendly because that is what the party IS, not to satisfy a gate.
        /// </summary>
        private sealed class DummyPartyMember : MonoBehaviour, IDamageableStructure
        {
            public bool IsAlive => true;
            CombatFaction IDamageableStructure.Faction => CombatFaction.Friendly;
            public void ApplyContactDamage(float amount) { }
        }

        /// <summary>Same seam plus the flying flag, to exercise the exemption.</summary>
        private sealed class DummyFlyingPartyMember : MonoBehaviour, IDamageableStructure, ICombatLayered
        {
            public bool IsAlive => true;
            CombatFaction IDamageableStructure.Faction => CombatFaction.Friendly;
            public void ApplyContactDamage(float amount) { }
            public CombatLayer Layer => CombatLayer.Flying;
        }

        public static bool Run(out string reason)
        {
            var failures = new List<string>();
            var notes = new List<string>();
            var created = new List<GameObject>();

            try
            {
                int structureLayer = LayerMask.NameToLayer("Structure");
                if (structureLayer < 0)
                {
                    // NOT a pass. BlockedByWallAt degrades open on a missing layer by design, so a
                    // silent "acquired" here would look identical to the bug being back.
                    reason = "no 'Structure' physics layer in ProjectSettings/TagManager.asset — the " +
                             "tower LoS mask would be 0 and every DefenseTower would degrade to " +
                             "shooting through walls. Cannot decide the WO-1808 gate.";
                    return false;
                }

                // --- the tower (EnemyOwned garrison turret) ---
                var towerGo = new GameObject("WO1808_EnemyTower");
                created.Add(towerGo);
                towerGo.transform.position = Vector3.zero;
                var tower = towerGo.AddComponent<DefenseTower>();
                tower.Allegiance = TowerAllegiance.EnemyOwned;
                tower.Range = 14f;
                tower.CanHitAir = false;
                tower.AirOnly = false;

                // --- the ground candidate ---
                var groundGo = new GameObject("WO1808_PartyGround");
                created.Add(groundGo);
                groundGo.transform.position = new Vector3(0f, TargetY, TargetDistance);
                var ground = groundGo.AddComponent<DummyPartyMember>();

                // --- the flying candidate (same spot, so only the layer differs) ---
                var flyerGo = new GameObject("WO1808_PartyFlyer");
                created.Add(flyerGo);
                flyerGo.transform.position = new Vector3(0f, TargetY, TargetDistance);
                var flyer = flyerGo.AddComponent<DummyFlyingPartyMember>();

                // --- CASE 1: clear line -> acquired ---
                Physics.SyncTransforms();
                var pick = tower.AcquirePartyForTest(ground, out Vector3 clearPos);
                if (!ReferenceEquals(pick, ground))
                    failures.Add("[case1 clear-line] AcquireParty did NOT acquire an in-range live party " +
                                 "member with no wall between them (returned " +
                                 (pick == null ? "null" : pick.GetType().Name) +
                                 ") — the WO-1808 LoS gate has made garrison turrets inert.");
                else notes.Add($"case1 clear-line acquired at pos={clearPos}");

                // --- the wall, on the Structure layer, halfway along the line ---
                var wallGo = new GameObject("WO1808_StructureWall");
                created.Add(wallGo);
                wallGo.layer = structureLayer;
                wallGo.transform.position = new Vector3(0f, WallHeight * 0.5f, TargetDistance * 0.5f);
                var box = wallGo.AddComponent<BoxCollider>();
                box.size = new Vector3(6f, WallHeight, 1f);
                box.isTrigger = false;
                Physics.SyncTransforms();

                // Prove the geometry itself before trusting the verdict: the muzzle->target line
                // MUST intersect the box, or case 2 would pass for the wrong reason.
                Vector3 muzzle = towerGo.transform.position + Vector3.up * 2f;
                int mask = 1 << structureLayer;
                if (!Physics.Linecast(muzzle, groundGo.transform.position, mask, QueryTriggerInteraction.Ignore))
                    failures.Add($"[geometry] the test box on layer 'Structure' does NOT intersect the " +
                                 $"muzzle{muzzle}->target{groundGo.transform.position} line, so a 'blocked' " +
                                 "verdict below would be meaningless. Edit-mode physics did not register " +
                                 "the collider (Physics.SyncTransforms ran).");

                // --- CASE 2: wall on the line -> rejected ---
                var blockedPick = tower.AcquirePartyForTest(ground, out _);
                if (blockedPick != null)
                    failures.Add("[case2 wall-blocks] AcquireParty STILL acquired the party member with a " +
                                 "Structure-layer wall between the muzzle and the target — this is the " +
                                 "WO-1808 defect (107 FireAtParty (EnemyOwned) shots, 0 TowerLoS lines).");
                else notes.Add("case2 wall-blocks rejected (no target -> UpdateEnemyOwned never reaches FireAtParty)");

                // --- CASE 3: wall + flyer -> acquired (exemption) ---
                var flyerPick = tower.AcquirePartyForTest(flyer, out Vector3 flyerPos);
                if (!ReferenceEquals(flyerPick, flyer))
                    failures.Add("[case3 flyer-exempt] a FLYING party member behind the same wall was " +
                                 "rejected — the flyer exemption the player pick carries " +
                                 "(ICombatLayered.Layer == Flying) was lost on the enemy-owned path.");
                else notes.Add($"case3 flyer-exempt acquired at pos={flyerPos}");

                // --- CASE 4: both gates still WIRED in source ---
                CheckSourceWiring(failures, notes);
            }
            catch (System.Exception ex)
            {
                failures.Add($"[threw] {ex.GetType().Name}: {ex.Message}");
            }
            finally
            {
                for (int i = 0; i < created.Count; i++)
                    if (created[i] != null) Object.DestroyImmediate(created[i]);
            }

            if (failures.Count > 0)
            {
                var sb = new StringBuilder();
                sb.Append($"enemy-tower-wall-los FAIL ({failures.Count}): ");
                sb.Append(string.Join(" | ", failures));
                reason = sb.ToString();
                return false;
            }

            reason = "enemy-tower-wall-los OK — EnemyOwned AcquireParty honours the Structure-layer LoS " +
                     "gate (clear=acquired, walled=rejected, flyer=exempt) and both picks still call " +
                     "their gate in source. " + string.Join("; ", notes);
            return true;
        }

        /// <summary>
        /// Source lint, NOT a behaviour test: the player pick must still call BlockedByWall and the
        /// party pick must still call BlockedByWallToParty. Case 2 proves the gate works TODAY; this
        /// makes its REMOVAL a gate failure tomorrow. A MISSING fixture is a hard FAIL naming the path
        /// — the same shape <c>TowerWallLosRegression.RequireLosGate</c> (`:75`) uses. It used to
        /// `notes.Add(...)` and return, which is a hollow pass: the suite would read green precisely
        /// when it had checked nothing.
        /// </summary>
        private static void CheckSourceWiring(List<string> failures, List<string> notes)
        {
            string path = Path.Combine(Application.dataPath, "_Modules/Village/Buildings/DefenseTower.cs");
            if (!File.Exists(path))
            {
                failures.Add("[case4 source-wiring] DefenseTower.cs NOT FOUND at " + path +
                             " — the LoS wiring assertions could not run, so this suite proves nothing " +
                             "about them. Not a skip: the file is the subject of the ticket.");
                return;
            }
            string src = File.ReadAllText(path);
            int before = failures.Count;
            if (!src.Contains("BlockedByWallToParty(d, p)"))
                failures.Add("[case4 source-wiring] AcquireParty no longer calls BlockedByWallToParty(d, p) — " +
                             "the WO-1808 enemy-owned LoS gate has been removed from the pick.");
            if (!src.Contains("if (BlockedByWall(d)) continue;"))
                failures.Add("[case4 source-wiring] Acquire() no longer calls BlockedByWall(d) — the " +
                             "2026-07 player-owned LoS gate has been removed.");
            if (!src.Contains("owner={ownerMode}"))
                failures.Add("[case4 source-wiring] the TowerLoS trace no longer names the owner mode, so a " +
                             "capture cannot tell which pick ran the linecast (WO-1808 requirement).");
            if (failures.Count == before) notes.Add("case4 source-wiring OK (both gates + owner-mode trace present)");
        }
    }
}

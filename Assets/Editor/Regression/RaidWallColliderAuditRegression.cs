// =============================================================================
// RaidWallColliderAuditRegression [raid-wall-audit]
//   markers RAID_WALL_AUDIT_OK / RAID_WALL_AUDIT_FAIL
// -----------------------------------------------------------------------------
// Assembly: DeNelle.EditorRegression. Edit mode. Opens every BAKED
// Assets/Scenes/RaidBase_*.unity Single and restores the prior active scene.
// Registered ONCE in DataRegression.RunAll. NEVER throws.
//
// WHY THIS EXISTS (WO-1812, from the WO-1808 hand-back, every fact re-read at
// source 2026-09-16):
//
//   * A raid WallSegment NEVER runs Configure() -> RebuildCollider(). The class
//     says so itself: WallSegment.cs:511-512 — "raid walls never get
//     Configure()'d, so _height sits at its serialized default for them".
//     RebuildCollider is the ONLY place that sizes the BoxCollider and the ONLY
//     place that assigns the "Structure" physics layer (WallSegment.cs:632-648).
//   * WallSegment.Awake (:626-629) caches the collider and NOTHING ELSE. It does
//     not set the layer and it does not size anything.
//   * So for a raid wall, the collider height AND the physics layer are whatever
//     the BAKE wrote into the .unity file. There is no runtime repair.
//   * DefenseTower.BlockedByWallAt (:965-970) linecasts from
//     transform.position + Vector3.up * 2f on a mask of "Structure" only. A wall
//     whose collider is SHORTER than its art, or whose GameObject is off the
//     Structure layer, is invisible to that linecast — and the player, who sees
//     a wall, watches the turret shoot through it. That is the owner's WO-1808
//     symptom ("attacking through the wall, maybe over but feels like through").
//
// WHAT IT PINS, per baked scene:
//   1. every WallSegment is on the "Structure" layer            -> offLayer
//   2. every WallSegment has an ENABLED, NON-TRIGGER Collider    -> shortCollider
//   3. the collider reaches the ART: its world bounds.max.y is within
//      ArtToleranceM of the encapsulated renderer bounds.max.y   -> shortCollider
//   4. the collider reaches the GROUND: its bounds.min.y is not more than
//      ArtToleranceM above the lowest rendered point             -> shortCollider
//   5. every ENEMY-OWNED DefenseTower's muzzle (position + up*2) is BELOW the
//      tallest WallSegment collider top within NeighbourRadiusM  -> margin
//
// ⛔ DEGRADE-OPEN NEVER READS GREEN. No "Structure" layer, no RaidBase scene, a
// scene with no WallSegment, or a WallSegment with no renderer at all are each a
// FAILURE with a named reason — not a quiet pass. (CLAUDE.md §8: an absent proof
// is a failure, not an unknown.)
//
// ⚠ IT DOES NOT PROPOSE A NUMBER. Pin 5 reports the margin; it does not assert a
// minimum clearance, because WHICH SIDE owns that invariant (the muzzle height or
// the wall collider) is an OPEN owner question recorded in
// WorkOrders/WORK_ORDER_1812_raid_wall_collider_vs_art_audit.md. This oracle
// proves the sign, and prints the number so a ruling can be made on data.
//
// HONEST LIMITS:
//   * It measures the BAKED SCENE, not a running raid. Whether AcquireParty
//     honours the linecast is EnemyTowerWallLosRegression's job [enemy-tower-wall-los].
//   * `up * 2f` is restated here on purpose: DefenseTower has no MuzzleOffset
//     const, the literal appears at five sites in that file, and an oracle that
//     imported the subject's own helper could not catch the subject changing it.
//     If a MuzzleOffset seam is ever added, this constant points at it.
//   * Scene list is DISCOVERED from disk, never copied from a doc or from
//     RaidNavBake.RaidScenes (CLAUDE.md §2/§5/§16 — copied state is the bug).
//
// Entry points:
//   Run(out report)  — DataRegression suite shape.
//   RunAll()         — standalone, prints the marker.
// =============================================================================

using System;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using DeNelle.Village;

namespace DeNelle.Editor.Regression
{
    public static class RaidWallColliderAuditRegression
    {
        /// <summary>
        /// How far the blocker may fall short of the ART, top or bottom, before the wall
        /// counts as short. 0.25 m is a bake-rounding allowance, NOT a design margin.
        /// </summary>
        private const float ArtToleranceM = 0.25f;

        /// <summary>XZ radius around a turret within which a wall counts as "in the way".</summary>
        private const float NeighbourRadiusM = 12f;

        /// <summary>
        /// DefenseTower's muzzle offset, restated (see HONEST LIMITS above). It is
        /// `transform.position + Vector3.up * 2f` at DefenseTower.cs:785/970/1032/1459/1482.
        /// </summary>
        private const float MuzzleUpM = 2f;

        /// <summary>Longest offender list any one failure sentence may carry.</summary>
        private const int MaxNamesReported = 12;

        private const string Remedy =
            "REMEDY: raid walls are BAKED — nothing repairs them at runtime (WallSegment.Configure/" +
            "RebuildCollider never run on them, WallSegment.cs:511-512 + :626-629). The collider height " +
            "and the physics layer must be written by the generator: " +
            "Assets/Editor/WallTools/RaidBaseGenerator.cs (PlaceSegment) / RaidBaseDresser.cs. After any " +
            "change there, RE-BAKE: DeNelle.Editor.RaidBaseGenerator.BuildAllRaidScenes then " +
            "DeNelle.Editor.RaidNavBake.BakeAll.";

        // -- entry points -----------------------------------------------------

        /// <summary>Standalone batch entry — prints the RAID_WALL_AUDIT_OK/_FAIL marker.</summary>
        public static void RunAll()
        {
            if (Run(out string report)) Debug.Log("RAID_WALL_AUDIT_OK " + report);
            else Debug.LogError("RAID_WALL_AUDIT_FAIL " + report);
        }

        public static bool Run(out string report)
        {
            try { return RunCore(out report); }
            catch (Exception ex)
            {
                // Guard.Try at the DataRegression call site swallows a throw, which would read
                // as a PASS. Catching here is what makes this suite decidable.
                report = "raid-wall-audit: oracle threw " + ex.GetType().Name + ": " + ex.Message;
                return false;
            }
        }

        // ---------------------------------------------------------------------

        private static bool RunCore(out string report)
        {
            int structureLayer = LayerMask.NameToLayer("Structure");
            if (structureLayer < 0)
            {
                // NOT a pass. WallSegment.RebuildCollider and DefenseTower.BlockedByWallAt both
                // degrade OPEN on a missing layer (mask 0 hits nothing), so a green here would
                // look identical to every turret shooting through every wall.
                report = "raid-wall-audit: no 'Structure' physics layer in ProjectSettings/TagManager.asset. " +
                         "The tower LoS mask would be 0 and EVERY wall would be transparent to it. " +
                         "Cannot decide this gate. " + Remedy;
                return false;
            }

            var scenes = DiscoverScenes();
            if (scenes.Count == 0)
            {
                report = "raid-wall-audit: NO RaidBase_*.unity under Assets/Scenes — the raid bake has " +
                         "never run on this clone, so NOTHING was checked. This is a gap, not a pass. " + Remedy;
                return false;
            }

            string restore = SceneManager.GetActiveScene().path;
            var failures = new List<string>();
            var summaries = new List<string>();

            for (int i = 0; i < scenes.Count; i++)
                AuditScene(scenes[i], structureLayer, failures, summaries);

            RestoreScene(restore);

            if (failures.Count > 0)
            {
                report = $"raid-wall-audit: {failures.Count} fault(s) across {scenes.Count} baked raid scene(s): " +
                         string.Join(" | ", failures) + " -- [" + string.Join("; ", summaries) + "] -- " + Remedy;
                return false;
            }

            report = $"raid-wall-audit: every WallSegment in {scenes.Count} baked raid scene(s) sits on the " +
                     $"'Structure' layer with an enabled non-trigger collider that reaches its own art " +
                     $"(±{ArtToleranceM:0.00}m top and bottom), and every enemy-owned turret muzzle sits BELOW " +
                     $"the tallest wall top within {NeighbourRadiusM:0}m. [" + string.Join("; ", summaries) + "]";
            return true;
        }

        /// <summary>
        /// Opens one baked scene, measures it, appends its one summary line, and adds a
        /// failure sentence per non-zero fault count. Never throws to the caller.
        /// </summary>
        private static void AuditScene(string path, int structureLayer,
                                       List<string> failures, List<string> summaries)
        {
            string sceneName = System.IO.Path.GetFileNameWithoutExtension(path);
            Scene scene;
            try
            {
                // SINGLE, mirroring RaidKeepReachRegression / RaidPostOrientationRegression: an
                // additive open in batchmode is non-deterministic, and a flaky oracle trains
                // everyone to ignore it.
                scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
            }
            catch (Exception ex)
            {
                failures.Add($"{sceneName} would not open ({ex.GetType().Name}: {ex.Message})");
                return;
            }
            if (!scene.IsValid())
            {
                failures.Add($"{sceneName} opened INVALID");
                return;
            }

            // Collider.bounds is read below; without this the physics world can still hold the
            // PREVIOUS scene's transforms.
            Physics.SyncTransforms();

            var walls = new List<WallSegment>();
            var towers = new List<DefenseTower>();
            var roots = scene.GetRootGameObjects();
            for (int r = 0; r < roots.Length; r++)
            {
                if (roots[r] == null) continue;
                walls.AddRange(roots[r].GetComponentsInChildren<WallSegment>(true));
                towers.AddRange(roots[r].GetComponentsInChildren<DefenseTower>(true));
            }

            var offLayer = new List<string>();
            var shortCollider = new List<string>();
            var noArt = new List<string>();
            // Collider tops, paired with their XZ centre, for the turret margin pass.
            var tops = new List<KeyValuePair<Vector2, float>>();

            for (int w = 0; w < walls.Count; w++)
            {
                var seg = walls[w];
                if (seg == null) continue;
                var go = seg.gameObject;

                if (go.layer != structureLayer)
                    offLayer.Add($"{go.name}(layer {go.layer} '{LayerMask.LayerToName(go.layer)}')");

                // PIN 2 — an enabled, non-trigger collider ON THE SEGMENT ITSELF. A trigger or a
                // disabled collider is not hit by Physics.Linecast with QueryTriggerInteraction.Ignore,
                // so it is exactly as transparent as no collider at all.
                Bounds blocker = default;
                bool haveBlocker = false;
                var cols = go.GetComponents<Collider>();
                for (int c = 0; c < cols.Length; c++)
                {
                    var col = cols[c];
                    if (col == null || !col.enabled || col.isTrigger) continue;
                    if (!haveBlocker) { blocker = col.bounds; haveBlocker = true; }
                    else blocker.Encapsulate(col.bounds);
                }
                if (!haveBlocker)
                {
                    shortCollider.Add($"{go.name}(no enabled non-trigger collider)");
                    continue;
                }

                tops.Add(new KeyValuePair<Vector2, float>(
                    new Vector2(blocker.center.x, blocker.center.z), blocker.max.y));

                // PINS 3+4 — the blocker must cover the ART it stands for.
                if (!Encapsulate(go, out Bounds art))
                {
                    // Vacuous, not green: with no renderer there is nothing to compare the
                    // collider against, and a wall with no art is its own defect.
                    noArt.Add(go.name);
                    continue;
                }

                if (blocker.max.y < art.max.y - ArtToleranceM)
                    shortCollider.Add($"{go.name}(top: collider {blocker.max.y:0.00}m vs art {art.max.y:0.00}m, " +
                                      $"short by {(art.max.y - blocker.max.y):0.00}m)");
                else if (blocker.min.y > art.min.y + ArtToleranceM)
                    shortCollider.Add($"{go.name}(bottom: collider starts {blocker.min.y:0.00}m, art starts " +
                                      $"{art.min.y:0.00}m, gap {(blocker.min.y - art.min.y):0.00}m under it)");
            }

            // PIN 5 — the enemy-owned turret margin.
            int enemyTowers = 0;
            float minMargin = float.PositiveInfinity;
            var overWall = new List<string>();
            for (int t = 0; t < towers.Count; t++)
            {
                var tower = towers[t];
                if (tower == null) continue;
                if (tower.Allegiance != TowerAllegiance.EnemyOwned) continue;
                enemyTowers++;

                Vector3 muzzle = tower.transform.position + Vector3.up * MuzzleUpM;
                var muzzleXZ = new Vector2(muzzle.x, muzzle.z);
                float tallest = float.NegativeInfinity;
                for (int k = 0; k < tops.Count; k++)
                {
                    if (Vector2.Distance(muzzleXZ, tops[k].Key) > NeighbourRadiusM) continue;
                    if (tops[k].Value > tallest) tallest = tops[k].Value;
                }
                if (float.IsNegativeInfinity(tallest)) continue;   // no wall near it — nothing to clear

                float margin = tallest - muzzle.y;
                if (margin < minMargin) minMargin = margin;
                if (margin <= 0f)
                    overWall.Add($"{tower.gameObject.name}(muzzle y={muzzle.y:0.00}m, tallest wall top " +
                                 $"{tallest:0.00}m within {NeighbourRadiusM:0}m, margin {margin:+0.00;-0.00}m)");
            }

            string marginText = float.IsPositiveInfinity(minMargin) ? "n/a" : minMargin.ToString("0.00");

            // ONE summary line per scene, always — pass or fail. This is the number a later
            // ruling on the muzzle-vs-collider invariant gets made on.
            Debug.Log($"[raid-wall-audit] scene={sceneName} walls={walls.Count} offLayer={offLayer.Count} " +
                      $"shortCollider={shortCollider.Count} towers={enemyTowers} minMuzzleMargin={marginText}");
            summaries.Add($"{sceneName}: walls={walls.Count} offLayer={offLayer.Count} " +
                          $"shortCollider={shortCollider.Count} towers={enemyTowers} minMuzzleMargin={marginText}");

            if (walls.Count == 0)
                failures.Add($"{sceneName}: ZERO WallSegments — the perimeter was never baked, so nothing " +
                             "about its colliders was checked. A gap, not a pass.");
            if (offLayer.Count > 0)
                failures.Add($"{sceneName}: {offLayer.Count} WallSegment(s) NOT on 'Structure' — the tower " +
                             "LoS linecast is masked to that layer and passes straight through them: " +
                             Names(offLayer));
            if (shortCollider.Count > 0)
                failures.Add($"{sceneName}: {shortCollider.Count} WallSegment(s) whose blocker does not cover " +
                             $"their art (±{ArtToleranceM:0.00}m) — a shot passes where the player sees stone: " +
                             Names(shortCollider));
            if (noArt.Count > 0)
                failures.Add($"{sceneName}: {noArt.Count} WallSegment(s) carry NO renderer, so the " +
                             "collider-covers-art check could not be decided for them: " + Names(noArt));
            if (overWall.Count > 0)
                failures.Add($"{sceneName}: {overWall.Count} enemy turret muzzle(s) AT OR ABOVE the tallest " +
                             $"wall top within {NeighbourRadiusM:0}m — the turret shoots OVER the wall the " +
                             "player is sheltering behind: " + Names(overWall));
        }

        private static string Names(List<string> names)
        {
            if (names.Count <= MaxNamesReported) return string.Join(", ", names);
            return string.Join(", ", names.GetRange(0, MaxNamesReported)) +
                   $", +{names.Count - MaxNamesReported} more";
        }

        /// <summary>World renderer bounds of the whole hierarchy, root included.</summary>
        private static bool Encapsulate(GameObject go, out Bounds bounds)
        {
            bounds = default;
            var rends = go.GetComponentsInChildren<Renderer>(true);
            bool any = false;
            for (int i = 0; i < rends.Length; i++)
            {
                if (rends[i] == null) continue;
                if (!any) { bounds = rends[i].bounds; any = true; }
                else bounds.Encapsulate(rends[i].bounds);
            }
            return any;
        }

        private static List<string> DiscoverScenes()
        {
            var found = new List<string>();
            string[] guids = AssetDatabase.FindAssets("t:Scene", new[] { "Assets/Scenes" });
            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                if (string.IsNullOrEmpty(path)) continue;
                string file = System.IO.Path.GetFileName(path);
                if (file.StartsWith("RaidBase_", StringComparison.Ordinal)) found.Add(path);
            }
            found.Sort(StringComparer.Ordinal);
            return found;
        }

        /// <summary>
        /// Restores the scene that was open before. ⛔ When there was none (the usual batchmode
        /// case) it opens an EMPTY scene rather than returning: leaving a raid scene loaded puts
        /// 100+ Structure-layer colliders into the physics world, where ANY later suite that
        /// raycasts or linecasts (the tower LoS oracles use exactly that mask, near the origin)
        /// would silently measure this scene instead of its own fixture. A suite that poisons its
        /// neighbours is worse than no suite. The other scene-opening raid suites return early on
        /// an empty path, so this also cleans up after them.
        /// </summary>
        private static void RestoreScene(string path)
        {
            try
            {
                if (!string.IsNullOrEmpty(path) && System.IO.File.Exists(path))
                {
                    if (SceneManager.GetActiveScene().path != path)
                        EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
                    return;
                }
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[raid-wall-audit] could not restore the prior scene (" +
                                 (string.IsNullOrEmpty(path) ? "none was open" : path) + "): " + ex.Message);
            }
        }
    }
}

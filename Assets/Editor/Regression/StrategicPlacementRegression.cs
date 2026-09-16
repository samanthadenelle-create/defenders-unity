// =============================================================================
// StrategicPlacementRegression — WO-673 L6: the five §5 permission-gate tests
// for strategic building placement (docs/WO673_ARCHITECTURE_REVIEW.md §5 + L6).
// -----------------------------------------------------------------------------
// Assembly: DeNelle.EditorRegression (references DeNelle.Core + DeNelle.Village).
// Headless, no scene load. Follows the CoreSaveContractRegression precedent
// (real objects in, real response out; reflection only on private seams, fail
// LOUD when a seam moves — never a vacuous pass).
//
// The gates (WO-673 §5; ff.strategicplacement REMOVED by WO-682 — strategic
// placement is ALWAYS ON, so gate 1's old flag-off parity is now MARKER parity):
//   1. MARKER PARITY     — with the marker UNSET no standdown/migration path is
//      reachable (StanddownActive false, managed records withheld from replay,
//      RunIfNeeded writes nothing outside the home hub), and the palette verbs
//      keep the owner taxonomy (Defense = towers/gates only, Walls = walls,
//      Town = Resource+Collector with the jeweler locked).
//   2. MIGRATION ROUND-TRIP — drive the REAL one-shot writer seam
//      (StrategicPlacementMigration.TryWriteRecord) over the REAL census table
//      (the private BakedRows/StationRows — read by reflection, not re-derived):
//      every censused functional id with a catalog row yields exactly ONE
//      BaseLayout record with a sane grid-quantized position + yaw; a census id
//      with NO catalog row (the runtime stations today) is skipped, never
//      half-written; the v29→v30 migration seeds the marker to FALSE (default-
//      on-read — the flip belongs to the writer, not the migrator); the writer
//      never runs outside the home hub and never runs twice (marker latch).
//   3. ONE BUILDING PER ID — after migration no managed functional id has two
//      BaseLayout records (the double-spawn gate), and the replay filter keys
//      replay strictly off StanddownActive (bake-owns XOR record-replays).
//   4. SAVE ROUND-TRIP v30 — CurrentVersion/migrator cover v30; the marker +
//      a full-fat PlacedStructureData record survive serialize→deserialize→
//      Validate through the REAL SaveSchema.JsonSettings.
//   5. PLACEMENT→DAMAGE→REPAIR CHAIN (data-level) — a placed functional
//      structure's catalog row is COSTED in materials, and the repair pricing
//      path (WallRepairController.BuildCostForComponent + CostForFraction — the
//      exact composition of CostForStructure, minus the MonoBehaviour shell)
//      resolves a NON-ZERO in-kind materials cost for it; both canonical catalog
//      copies stay byte-equal.
//   6. 45° YAW ROUND-TRIP (L5 cross-lane) — every eighth-turn facing k = 0..7
//      persists as (yawSteps = k>>1, yawOffset = (k&1)·45) on the EXISTING
//      schema fields and survives SaveSchema.JsonSettings unchanged (world yaw
//      = yawSteps·90 + yawOffset = k·45); a legacy record with no yawOffset in
//      its JSON reads back 0 (default-on-read, byte-identical replay).
//   7. CLAIM INFLATION MATH (L5 cross-lane) — PlacementGrid.FootprintCells
//      (metres, yawDegrees) claims the |sin|+|cos|-inflated (√2 at 45°) cell
//      count at a diagonal yaw, and at every CARDINAL yaw returns exactly the
//      legacy single-arg claim (the byte-identical guarantee for old saves +
//      flag-off placements).
//
// Wire into the suite from DataRegression.RunAll (one line):
//   if (!StrategicPlacementRegression.Run(out var stratPlaceReason)) failures.Add(stratPlaceReason); else log.AppendLine("[strategic-placement] " + stratPlaceReason);
// =============================================================================
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using UnityEngine;
using DeNelle.Core.Catalog;
using DeNelle.Core.State;
using DeNelle.Village;

namespace DeNelle.Editor
{
    public static class StrategicPlacementRegression
    {
        public static bool Run(out string reason)
        {
            var failures = new List<string>();
            var log = new StringBuilder();
            log.AppendLine("--- STRATEGIC PLACEMENT (WO-673 §5 gates; always-on per WO-682) ---");

            // ── Global-state bookkeeping: restore EVERYTHING in finally ─────────
            var prevServiceInstance = ReadServiceInstance();
            var created = new List<UnityEngine.Object>();

            try
            {
                EnsureCatalogLoaded(failures, log);

                // Throwaway GameStateService (INACTIVE GO so Awake never runs; the
                // writer's HasRecord belt reads GameStateService.Instance.State, so the
                // installed state must BE the fixture state).
                var state = ScriptableObject.CreateInstance<GameState>();
                created.Add(state);
                var svcGo = new GameObject("Oracle_GameStateService");
                svcGo.SetActive(false);
                created.Add(svcGo);
                var svc = svcGo.AddComponent<GameStateService>();
                SetPrivate(svc, "_state", state);
                WriteServiceInstance(svc);

                // Real PlacementGrid (headless-pure WorldToCell/CellToWorld math).
                var gridGo = new GameObject("Oracle_PlacementGrid");
                created.Add(gridGo);
                var grid = gridGo.AddComponent<PlacementGrid>();

                GateOne_MarkerParity(state, failures, log);
                GateTwo_MigrationRoundTrip(state, grid, failures, log);
                GateThree_OneBuildingPerId(state, failures, log);
                GateFour_SaveRoundTripV30(failures, log);
                GateFive_PlacementDamageRepairChain(created, failures, log);
                GateSix_YawRoundTrip45(failures, log);
                GateSeven_ClaimInflation(grid, failures, log);
                GateEight_SeedIdsPaletteVisible(failures, log);
            }
            catch (Exception ex)
            {
                failures.Add($"strategic-placement oracle threw: {ex.GetType().Name}: {ex.Message}");
            }
            finally
            {
                WriteServiceInstance(prevServiceInstance);
                foreach (var o in created)
                    if (o != null) UnityEngine.Object.DestroyImmediate(o);
            }

            if (failures.Count == 0)
            {
                Debug.Log(log.ToString() + "STRATEGIC_PLACEMENT_OK");
                reason = "STRATEGIC PLACEMENT OK — marker parity + migration round-trip + one-per-id + " +
                         "save v30 round-trip + placement/repair cost chain all hold (WO-673 §5 gates, always-on per WO-682)";
                return true;
            }
            reason = "strategic-placement: " + string.Join("; ", failures);
            Debug.LogError(log.ToString() + "STRATEGIC_PLACEMENT_FAIL: " + reason);
            return false;
        }

        // =====================================================================
        //  GATE 1 — marker parity (was flag-off parity; WO-682 removed the flag)
        // =====================================================================
        private static void GateOne_MarkerParity(GameState state, List<string> failures, StringBuilder log)
        {
            log.AppendLine("[gate 1] marker parity (always-on, WO-682)");

            // 1a. With the marker UNSET no standdown path is reachable: StanddownActive
            //     false, managed ids withheld from replay (bake owns them), non-managed
            //     ids (towers) always replay.
            state.StrategicPlacementMigrated = false;
            if (StrategicPlacementMigration.StanddownActive)
                failures.Add("marker UNSET but StanddownActive == true — the standdown path is reachable before migration");
            if (StrategicPlacementMigration.ShouldReplayRecord("forge"))
                failures.Add("marker UNSET but ShouldReplayRecord('forge') == true — a managed functional record would replay while the bake owns it (double-spawn)");
            if (!StrategicPlacementMigration.ShouldReplayRecord("tower_ground_archer"))
                failures.Add("marker UNSET but ShouldReplayRecord('tower_ground_archer') == false — non-managed records (player towers) must ALWAYS replay");
            if (!StrategicPlacementMigration.IsManagedId("forge") || StrategicPlacementMigration.IsManagedId("tower_ground_archer"))
                failures.Add("IsManagedId census membership wrong ('forge' must be managed, 'tower_ground_archer' must not)");

            // 1b. The one-shot writer writes NOTHING outside the home hub (the headless
            //     harness scene is never SceneRouter.Castle — the scene gate holds).
            int before = state.BaseLayout != null ? state.BaseLayout.Count : 0;
            StrategicPlacementMigration.RunIfNeeded();
            int after = state.BaseLayout != null ? state.BaseLayout.Count : 0;
            if (after != before || state.StrategicPlacementMigrated)
                failures.Add($"RunIfNeeded mutated state outside the home hub (records {before}->{after}, marker={state.StrategicPlacementMigrated}) — the scene gate failed");
            else log.AppendLine("  marker-unset standdown unreachable + non-hub RunIfNeeded no-op ok");

            // 1c. Palette category parity — the Defense verb renders towers/gates
            //     ONLY (no functional building/collector/wall leaked into it), Walls
            //     is walls-only, Town is Resource+Collector with the jeweler locked.
            var defense = BuildCategoryRegistry.Get(DeNelle.Core.Catalog.BuildType.Defense);
            foreach (var t in defense.Types)
                if (t != CatalogType.Tower && t != CatalogType.Gate)
                    failures.Add($"Defense verb feeds CatalogType.{t} — post-673 Defense must be Tower/Gate only (walls split to Walls, functional to Town)");
            int defenseRendered = 0;
            foreach (var t in defense.Types)
                foreach (var e in CatalogRegistry.OfType(t))
                {
                    if (e == null || defense.LockedIds.Contains(e.id)) continue;
                    defenseRendered++;
                    if (e.type != CatalogType.Tower && e.type != CatalogType.Gate)
                        failures.Add($"Defense palette renders '{e.id}' (type {e.type}) — a non-defense row leaked into the Defense verb");
                }
            if (defenseRendered == 0)
                failures.Add("Defense verb renders 0 entries — the Defenses palette would be EMPTY (parity broken)");
            else log.AppendLine($"  Defense verb renders {defenseRendered} tower/gate entrie(s) ok");

            var walls = BuildCategoryRegistry.Get(DeNelle.Core.Catalog.BuildType.Walls);
            if (walls.Types.Length != 1 || walls.Types[0] != CatalogType.Wall)
                failures.Add($"Walls verb must feed exactly [Wall] — got [{string.Join(",", walls.Types)}]");

            var town = BuildCategoryRegistry.Get(DeNelle.Core.Catalog.BuildType.Town);
            bool townHasResource = false, townHasCollector = false;
            foreach (var t in town.Types)
            {
                if (t == CatalogType.Resource) townHasResource = true;
                else if (t == CatalogType.Collector) townHasCollector = true;
                else failures.Add($"Town verb feeds CatalogType.{t} — must be Resource+Collector only (owner taxonomy ruling)");
            }
            if (!townHasResource || !townHasCollector)
                failures.Add($"Town verb must feed Resource AND Collector — got [{string.Join(",", town.Types)}]");
            if (!town.LockedIds.Contains("jeweler"))
                failures.Add("Town verb does not lock 'jeweler' — the jeweler stays unlock-gated (moved from Defense lockedIds)");
        }

        // =====================================================================
        //  GATE 2 — migration round-trip (review §5 test 1 / L6 test 2)
        // =====================================================================
        private static void GateTwo_MigrationRoundTrip(GameState state, PlacementGrid grid,
            List<string> failures, StringBuilder log)
        {
            log.AppendLine("[gate 2] migration round-trip");

            // v29 → v30 seeds the marker to FALSE (default-on-read). The FLIP belongs
            // to the one-shot writer, never the pure save migrator.
            var migrated = SaveMigrator.Migrate(new SaveSchema.PersistedState(), 29);
            if (migrated == null || !migrated.StrategicPlacementMigrated.HasValue)
                failures.Add("migrate v29->current did not seed strategicPlacementMigrated (v30 step missing)");
            else if (migrated.StrategicPlacementMigrated.Value)
                failures.Add("migrate v29->current seeded strategicPlacementMigrated = TRUE — the migrator must seed FALSE (an old save loads with bakes owning everything; only the one-shot writer flips it)");
            else log.AppendLine("  v29->v30 marker seeded FALSE ok");

            // Drive the REAL writer seam over the REAL census table (reflection on the
            // private BakedRows/StationRows + TryWriteRecord — the exact production
            // record path, not a re-derivation).
            var tryWrite = typeof(StrategicPlacementMigration).GetMethod("TryWriteRecord",
                BindingFlags.NonPublic | BindingFlags.Static);
            if (tryWrite == null)
            { failures.Add("StrategicPlacementMigration.TryWriteRecord not found by reflection — the writer seam moved; re-point this oracle"); return; }

            var bakedIds = ReadCensusIds("BakedRows", failures);
            var stationIds = ReadCensusIds("StationRows", failures);
            if (bakedIds.Count == 0)
            { failures.Add("StrategicPlacementMigration.BakedRows read empty by reflection — the census table moved; re-point this oracle"); return; }
            log.AppendLine($"  census: {bakedIds.Count} baked + {stationIds.Count} station id(s)");

            state.StrategicPlacementMigrated = false;
            state.BaseLayout = new List<PlacedStructureData>();
            // Pre-existing player tower — migration must never touch non-managed records.
            state.BaseLayout.Add(new PlacedStructureData("tower_ground_archer", 1, 1, 0, 1));

            int written = 0, skippedNoRow = 0, i = 0;
            var allCensus = new List<string>(bakedIds);
            allCensus.AddRange(stationIds);
            foreach (var itemId in allCensus)
            {
                // Varied off-grid poses + non-step yaws so quantization is exercised.
                var pos = new Vector3(10.7f + 7f * i, 0f, -20.3f + 5f * i);
                float yaw = 90f * (i % 4) + 22f;   // nearest step = i % 4
                int countBefore = state.BaseLayout.Count;
                bool ok = (bool)tryWrite.Invoke(null, new object[] { state, grid, itemId, pos, yaw });
                bool rowExists = CatalogRegistry.Get(itemId) != null;

                if (rowExists)
                {
                    if (!ok || state.BaseLayout.Count != countBefore + 1)
                    { failures.Add($"migration writer did not emit exactly one record for censused id '{itemId}' (catalog row present)"); i++; continue; }
                    written++;
                    var rec = state.BaseLayout[state.BaseLayout.Count - 1];
                    if (rec.itemId != itemId)
                        failures.Add($"migration record itemId mismatch: wrote for '{itemId}', record says '{rec.itemId}'");
                    if (rec.level != 1)
                        failures.Add($"migrated '{itemId}' level {rec.level} — must be 1");
                    int expectSteps = i % 4;
                    if (rec.yawSteps != expectSteps)
                        failures.Add($"migrated '{itemId}' yawSteps {rec.yawSteps} — expected nearest-90 step {expectSteps} for yaw {yaw}");
                    // Sane position: the grid-snapped cell must round-trip to within one
                    // cell's half-diagonal of the source pose (the accepted ~1.5m drift;
                    // 3m cells → max legal snap drift = 3·√2/2 ≈ 2.12m).
                    var snapped = grid.CellToWorld(new Vector2Int(rec.cellX, rec.cellZ));
                    float drift = Vector2.Distance(new Vector2(pos.x, pos.z), new Vector2(snapped.x, snapped.z));
                    if (float.IsNaN(drift) || drift > grid.cellSize * 0.7072f + 0.01f)
                        failures.Add($"migrated '{itemId}' cell ({rec.cellX},{rec.cellZ}) snaps {drift:0.##}m from its source pose — beyond the accepted quantization drift");
                }
                else
                {
                    // No catalog row (the runtime stations today): tolerated SKIP — no
                    // record, no half-write; the injector keeps owning the structure.
                    skippedNoRow++;
                    if (ok || state.BaseLayout.Count != countBefore)
                        failures.Add($"censused id '{itemId}' has NO catalog row but the writer emitted a record — must skip + Warn (bake/injector keeps ownership)");
                }
                i++;
            }
            log.AppendLine($"  writer: {written} record(s) written, {skippedNoRow} skipped (no catalog row) ok");
            if (written == 0)
                failures.Add("migration writer emitted ZERO records across the whole census — every functional id lost its migration");

            // Idempotence belt: a second write for an already-recorded id adds nothing.
            int total = state.BaseLayout.Count;
            bool again = (bool)tryWrite.Invoke(null, new object[]
                { state, grid, "forge", new Vector3(99f, 0f, 99f), 0f });
            if (again || state.BaseLayout.Count != total)
                failures.Add("second TryWriteRecord('forge') emitted a duplicate — the HasRecord idempotency belt failed (running twice must add nothing)");
            else log.AppendLine("  second write for 'forge' skipped (idempotent) ok");

            // One-shot latch: with the marker SET, RunIfNeeded is a hard no-op.
            state.StrategicPlacementMigrated = true;
            StrategicPlacementMigration.RunIfNeeded();
            if (state.BaseLayout.Count != total)
                failures.Add("RunIfNeeded ran AGAIN with the marker set — the one-shot latch failed (migration must never run twice)");

            // Home-hub scoping: marker unset but NOT the castle scene →
            // the writer must not fire (records only ever migrate in the home hub).
            state.StrategicPlacementMigrated = false;
            StrategicPlacementMigration.RunIfNeeded();
            if (state.BaseLayout.Count != total || state.StrategicPlacementMigrated)
                failures.Add("RunIfNeeded wrote outside the home hub (active scene is not SceneRouter.Castle) — the scene gate failed");
            else log.AppendLine("  marker latch + home-hub scene gate hold ok");
        }

        // =====================================================================
        //  GATE 3 — one Building per id (review §5 test 3's data half / L6 test 3)
        // =====================================================================
        private static void GateThree_OneBuildingPerId(GameState state, List<string> failures, StringBuilder log)
        {
            log.AppendLine("[gate 3] one building per id");

            // After migration+standdown state (gate 2's output): no managed functional
            // id may carry two BaseLayout records — the record IS the spawn authority
            // once standdown activates, so a duplicate record = a double-spawn.
            var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            if (state.BaseLayout != null)
                foreach (var rec in state.BaseLayout)
                {
                    if (string.IsNullOrEmpty(rec.itemId)) { failures.Add("BaseLayout contains a record with a null/empty itemId"); continue; }
                    counts.TryGetValue(rec.itemId, out int n);
                    counts[rec.itemId] = n + 1;
                }
            foreach (var kv in counts)
                if (StrategicPlacementMigration.IsManagedId(kv.Key) && kv.Value != 1)
                    failures.Add($"managed functional id '{kv.Key}' has {kv.Value} BaseLayout records — exactly one Building per id (the double-spawn gate)");
            if (!counts.ContainsKey("tower_ground_archer") || counts["tower_ground_archer"] != 1)
                failures.Add("the pre-existing non-managed tower record was lost/duplicated by migration — non-managed records must pass through untouched");
            log.AppendLine($"  {counts.Count} distinct id(s) in BaseLayout, managed ids all single ok");

            // Mutual exclusion is structural: replay of a managed id keys strictly off
            // StanddownActive — the SAME property that hides the bake — so both owners
            // can never spawn in one frame. Headless (not the castle scene, migration
            // load latched) StanddownActive is false → managed records are withheld
            // even with the marker set.
            state.StrategicPlacementMigrated = true;
            if (StrategicPlacementMigration.StanddownActive)
                failures.Add("StanddownActive == true outside the home hub — standdown/replay must be castle-scoped");
            if (StrategicPlacementMigration.ShouldReplayRecord("forge"))
                failures.Add("ring storefront 'forge' must NEVER catalog-replay — injector owns the ring on every load");
            state.StrategicPlacementMigrated = false;
            if (StrategicPlacementMigration.ShouldReplayRecord("forge"))
                failures.Add("marker cleared but the managed record would still replay — ownership must return to the bakes (no double-spawn)");
            else log.AppendLine("  standdown mutual-exclusion + marker-cleared withhold ok");
        }

        // =====================================================================
        //  GATE 4 — save round-trip v30 (CoreSaveContractRegression pattern / L6 test 4)
        // =====================================================================
        private static void GateFour_SaveRoundTripV30(List<string> failures, StringBuilder log)
        {
            log.AppendLine("[gate 4] save round-trip v30");

            if (SaveSchema.CurrentVersion < 30)
                failures.Add($"SaveSchema.CurrentVersion = {SaveSchema.CurrentVersion} — the WO-673 marker requires the v30 bump");
            int top = ReadMigratorTopStep(out string reflectErr);
            if (reflectErr != null) failures.Add(reflectErr);
            else if (top < 30)
                failures.Add($"SaveMigrator top step = {top} — no v30 migration step registered for the marker");

            // Marker + a full-fat functional record through the REAL save settings.
            var outState = new SaveSchema.PersistedState
            {
                StrategicPlacementMigrated = true,
                BaseLayout = new List<PlacedStructureData>
                {
                    new PlacedStructureData("forge", 4, 7, 3, 2, yawOffset: 45f, worldY: 1.5f, wallMounted: false),
                    new PlacedStructureData("collector_forge", 2, 9, 1, 1),
                },
            };
            string json = JsonConvert.SerializeObject(outState, SaveSchema.JsonSettings);
            var back = JsonConvert.DeserializeObject<SaveSchema.PersistedState>(json, SaveSchema.JsonSettings);
            if (back == null) { failures.Add("v30 save round-trip deserialized to null"); return; }

            var vr = SaveSchema.Validate(back);
            if (!vr.Ok)
                failures.Add($"v30 marker+records save FAILED validation: field '{vr.FieldPath}' ({vr.Reason})");
            if (!back.StrategicPlacementMigrated.HasValue || !back.StrategicPlacementMigrated.Value)
                failures.Add("strategicPlacementMigrated did not survive the save round-trip (wrote true, read back " +
                             (back.StrategicPlacementMigrated.HasValue ? "false" : "null") + ")");
            if (back.BaseLayout == null || back.BaseLayout.Count != 2)
                failures.Add($"BaseLayout records did not survive the v30 round-trip (wrote 2, read back {(back.BaseLayout != null ? back.BaseLayout.Count : 0)})");
            else
            {
                var r = back.BaseLayout[0];
                if (r.itemId != "forge" || r.cellX != 4 || r.cellZ != 7 || r.yawSteps != 3 || r.level != 2 ||
                    Math.Abs(r.yawOffset - 45f) > 0.001f || Math.Abs(r.worldY - 1.5f) > 0.001f || r.wallMounted)
                    failures.Add($"the functional 'forge' record mutated in the round-trip: got ({r.itemId},{r.cellX},{r.cellZ},steps={r.yawSteps},lvl={r.level},offset={r.yawOffset},y={r.worldY},wall={r.wallMounted})");
                else log.AppendLine("  marker + forge/collector records survived serialize->deserialize->validate ok");
            }

            // Default-on-read: an old (marker-less) payload reads back with NO marker,
            // and the v29 migrate seeds it false (gate 2 proved the seed value).
            var old = JsonConvert.DeserializeObject<SaveSchema.PersistedState>("{}", SaveSchema.JsonSettings);
            if (old == null || old.StrategicPlacementMigrated.HasValue)
                failures.Add("an old marker-less save did not read back with a null marker (default-on-read broken)");
        }

        // =====================================================================
        //  GATE 5 — placement→damage→repair chain, data-level (L6 test 5)
        // =====================================================================
        private static void GateFive_PlacementDamageRepairChain(List<UnityEngine.Object> created,
            List<string> failures, StringBuilder log)
        {
            log.AppendLine("[gate 5] placement->damage->repair cost chain");

            // 5a. The placed functional structure's catalog row is COSTED in materials
            //     (placement charges in-kind; a zero-materials row also can't price a repair).
            var forge = CatalogRegistry.Get("forge");
            if (forge == null || forge.repo == null)
            { failures.Add("catalog row 'forge' missing — the placed functional structure has no cost/pricing source"); return; }
            var buildCost = forge.repo.cost;
            if (buildCost.wood + buildCost.iron + buildCost.stone <= 0)
                failures.Add($"'forge' repo.cost has NO materials (w{buildCost.wood}/i{buildCost.iron}/f{buildCost.stone}) — placement would fall back to crystals-only and repair could not price it");
            var collForge = CatalogRegistry.Get("collector_forge");
            if (collForge == null || collForge.repo == null ||
                collForge.repo.cost.wood + collForge.repo.cost.iron + collForge.repo.cost.stone <= 0)
                failures.Add("'collector_forge' catalog row missing or material-less — the collector half of the chain is unpriced");

            // 5b. The REAL repair pricing path resolves that row for a live Building.
            //     BuildCostForComponent + CostForFraction is the exact composition of
            //     WallRepairController.CostForStructure (WaveDamageReport's entry price).
            var buildCostFor = typeof(WallRepairController).GetMethod("BuildCostForComponent",
                BindingFlags.NonPublic | BindingFlags.Static);
            if (buildCostFor == null)
            { failures.Add("WallRepairController.BuildCostForComponent not found by reflection — the repair pricing seam moved; re-point this oracle"); return; }

            var bGo = new GameObject("Oracle_ForgeBuilding");
            created.Add(bGo);
            var building = bGo.AddComponent<Building>();
            building.Configure(BuildingType.Forge, "forge", "Armorer");

            var priced = (DeNelle.Core.Catalog.ResourceCost)buildCostFor.Invoke(null, new object[] { building });
            if (priced.wood != buildCost.wood || priced.iron != buildCost.iron || priced.stone != buildCost.stone)
                failures.Add($"repair pricing resolved (w{priced.wood}/i{priced.iron}/f{priced.stone}) for the placed forge — expected its own catalog row (w{buildCost.wood}/i{buildCost.iron}/f{buildCost.stone}); it fell through to a fallback row");
            else log.AppendLine($"  Building('forge') prices from its own row (w{priced.wood}/i{priced.iron}/f{priced.stone}) ok");

            // Half damage → non-zero in-kind cost; destroyed → the FULL build cost
            // (the rebuild price); crystals are NEVER charged on repair.
            var half = WallRepairController.CostForFraction(0.5f, priced);
            if (WallRepairController.MaterialsZero(half))
                failures.Add("half-damaged forge repairs for ZERO materials — the damage->repair chain resolves no cost");
            if (half.crystals != 0)
                failures.Add($"repair charged {half.crystals} crystals — crystals are never spent on repair (owner ruling 2026-07-11)");
            var full = WallRepairController.CostForFraction(1f, priced);
            if (full.wood != priced.wood || full.iron != priced.iron || full.stone != priced.stone)
                failures.Add($"destroyed forge rebuild price (w{full.wood}/i{full.iron}/f{full.stone}) != its full build cost (w{priced.wood}/i{priced.iron}/f{priced.stone})");
            else log.AppendLine($"  repair: half=(w{half.wood}/i{half.iron}) full=(w{full.wood}/i{full.iron}) crystals=0 ok");

            // 5c. Catalog integrity: every censused id that HAS a row renders resolvable,
            //     and both canonical copies stay byte-equal (the dual-copy rule).
            foreach (string file in new[] { "build-categories.json", "structures-catalog.json" })
            {
                string res = Application.dataPath + "/Resources/Data/Canonical/" + file;
                string sa = Application.dataPath + "/StreamingAssets/Data/Canonical/" + file;
                try
                {
                    var a = System.IO.File.ReadAllBytes(res);
                    var b = System.IO.File.ReadAllBytes(sa);
                    if (!StructuralComparisons.StructuralEqualityComparer.Equals(a, b))
                        failures.Add($"{file}: Resources and StreamingAssets copies are NOT byte-equal (CanonicalJson dual-copy rule)");
                }
                catch (Exception ex)
                { failures.Add($"{file}: dual-copy check could not read both canonical copies ({ex.Message})"); }
            }
            log.AppendLine("  dual-copy byte-equality checked ok");
        }

        // =====================================================================
        //  GATE 6 — 45° yaw round-trip (L5 cross-lane: existing schema fields)
        // =====================================================================
        private static void GateSix_YawRoundTrip45(List<string> failures, StringBuilder log)
        {
            log.AppendLine("[gate 6] 45-degree yaw round-trip");

            // Every eighth-turn facing k = 0..7 commits as (yawSteps = k>>1,
            // yawOffset = (k&1)·45) — EXISTING v27 fields, no schema change — and must
            // survive the REAL save settings with the same k (world yaw = k·45 exactly).
            var records = new List<PlacedStructureData>();
            for (int k = 0; k < 8; k++)
                records.Add(new PlacedStructureData("forge", k, -k, k >> 1, 1, yawOffset: (k & 1) * 45f));
            var outState = new SaveSchema.PersistedState { BaseLayout = records };

            string json = JsonConvert.SerializeObject(outState, SaveSchema.JsonSettings);
            var back = JsonConvert.DeserializeObject<SaveSchema.PersistedState>(json, SaveSchema.JsonSettings);
            if (back == null || back.BaseLayout == null || back.BaseLayout.Count != 8)
            { failures.Add($"45-yaw round-trip: wrote 8 eighth-turn records, read back {(back?.BaseLayout != null ? back.BaseLayout.Count : 0)}"); return; }

            for (int k = 0; k < 8; k++)
            {
                var r = back.BaseLayout[k];
                int readK = r.yawSteps * 2 + (Mathf.Approximately(r.yawOffset, 45f) ? 1 : 0);
                float worldYaw = r.yawSteps * 90f + r.yawOffset;   // the BaseLayoutLoader replay formula
                if (r.yawSteps != (k >> 1) || Mathf.Abs(r.yawOffset - (k & 1) * 45f) > 0.001f)
                    failures.Add($"45-yaw k={k}: persisted (steps={r.yawSteps}, offset={r.yawOffset}) — expected (steps={k >> 1}, offset={(k & 1) * 45f})");
                else if (readK != k || Mathf.Abs(worldYaw - k * 45f) > 0.001f)
                    failures.Add($"45-yaw k={k}: round-trip reconstructs k={readK} / world yaw {worldYaw}° — expected {k} / {k * 45f}°");
            }

            // Legacy record — yawOffset ABSENT from the JSON — must read back 0
            // (default-on-read) so an old save replays byte-identically (steps·90 only).
            const string legacyJson =
                "{\"baseLayout\":[{\"itemId\":\"tower_ground_archer\",\"cellX\":3,\"cellZ\":5,\"yawSteps\":2,\"level\":1}]}";
            var legacy = JsonConvert.DeserializeObject<SaveSchema.PersistedState>(legacyJson, SaveSchema.JsonSettings);
            if (legacy == null || legacy.BaseLayout == null || legacy.BaseLayout.Count != 1)
                failures.Add("45-yaw legacy: the yawOffset-less fixture record did not deserialize");
            else
            {
                var r = legacy.BaseLayout[0];
                if (!Mathf.Approximately(r.yawOffset, 0f) || r.yawSteps != 2)
                    failures.Add($"45-yaw legacy: yawOffset-less record read back (steps={r.yawSteps}, offset={r.yawOffset}) — must be (2, 0) for byte-identical replay (world yaw 180°)");
                else log.AppendLine("  k=0..7 eighth-turns + legacy offset-less record round-trip ok");
            }
        }

        // =====================================================================
        //  GATE 7 — rotation-honest claim inflation (L5 cross-lane)
        // =====================================================================
        private static void GateSeven_ClaimInflation(PlacementGrid grid, List<string> failures, StringBuilder log)
        {
            log.AppendLine("[gate 7] footprint claim inflation");

            // Realistic catalog footprints (forge 3.4m) + a larger one where the √2
            // inflation crosses a cell boundary, + an exact cell multiple (the epsilon
            // trap: trig at cardinals must still yield the LEGACY count exactly).
            float[] metresCases = { 3.4f, 4.9f, 6.0f };
            float[] cardinals = { 0f, 90f, 180f, 270f };
            foreach (float m in metresCases)
            {
                Vector2Int legacy = grid.FootprintCells(m);

                // Cardinal yaws: byte-identical to the legacy claim (old saves + flag-off
                // placements replay with yaw = steps·90 and must Occupy the SAME cells).
                foreach (float yaw in cardinals)
                {
                    Vector2Int card = grid.FootprintCells(m, yaw);
                    if (card != legacy)
                        failures.Add($"claim inflation: FootprintCells({m}m, {yaw}°) = {card.x}x{card.y} but legacy = {legacy.x}x{legacy.y} — cardinal claims must be byte-identical (inflate must resolve to exactly 1, trig epsilon included)");
                }

                // 45° diagonal: the claim must be the |sin|+|cos| = √2 inflated count —
                // exactly Ceil(m·√2 / cellSize) — and never smaller than the cardinal
                // claim (under-claiming is the G-F "ghost lies about its cells" bug).
                Vector2Int diag = grid.FootprintCells(m, 45f);
                int expect = Mathf.Max(1, Mathf.CeilToInt(m * Mathf.Sqrt(2f) / grid.cellSize));
                if (diag.x != expect || diag.y != expect)
                    failures.Add($"claim inflation: FootprintCells({m}m, 45°) = {diag.x}x{diag.y} — expected the √2-inflated {expect}x{expect} (|sin|+|cos| claim)");
                if (diag.x < legacy.x || diag.y < legacy.y)
                    failures.Add($"claim inflation: 45° claim {diag.x}x{diag.y} is SMALLER than the cardinal {legacy.x}x{legacy.y} — a rotated AABB under-claim (G-F veto)");
                log.AppendLine($"  {m}m: legacy {legacy.x}x{legacy.y}, 45° {diag.x}x{diag.y} (expected {expect}x{expect})");
            }

            // The distinguishing case must actually distinguish: at 4.9m the √2 claim
            // crosses a cell boundary (2 → 3 cells @ 3m cells), proving the overload is
            // not silently ignoring yaw.
            if (grid.FootprintCells(4.9f, 45f) == grid.FootprintCells(4.9f))
                failures.Add("claim inflation: FootprintCells(4.9m, 45°) equals the unrotated claim — the yaw overload is not inflating (silently ignoring yawDegrees)");
        }

        // =====================================================================
        //  GATE 8 -- the Default-Town seed never writes a palette-HIDDEN id
        //  (WO felt-bug: a Farm/Lumbermill re-offered because the seed recorded
        //   'mill'/'lumbermill' -- Town lockedIds -- which the singleton gate
        //   scans by exact record.itemId==card.id and so never matches the
        //   'collector_farm'/'collector_lumbermill' cards the palette renders)
        // =====================================================================
        private static void GateEight_SeedIdsPaletteVisible(List<string> failures, StringBuilder log)
        {
            log.AppendLine("[gate 8] seeded baked ids are palette-visible (not Town-locked)");

            // Every id the Default-Town seed writes (the REAL BakedRows census, read by
            // reflection) must be a card the Town palette actually RENDERS -- otherwise the
            // singleton 'already-built' gate (IsSingletonBuilt: scans BaseLayout for
            // record.itemId == card.id) can never match the seeded record, and the
            // building is offered for placement while a copy already exists. A baked id
            // that lands in Town lockedIds is exactly that leak (Farm, Lumbermill).
            var town = BuildCategoryRegistry.Get(DeNelle.Core.Catalog.BuildType.Town);
            if (town == null || town.LockedIds == null)
            { failures.Add("Town build-category / lockedIds not loaded -- cannot verify the seed against the palette"); return; }

            var bakedIds = ReadCensusIds("BakedRows", failures);
            if (bakedIds.Count == 0)
            { failures.Add("BakedRows read empty by reflection -- the census table moved; re-point this oracle"); return; }
            var bakedSet = new HashSet<string>(bakedIds, StringComparer.OrdinalIgnoreCase);

            // POSITIVE guard (the exact WO-707 fix -- pins the rename so it can't regress):
            // the seed MUST record the collector rename TARGETS ('collector_farm' /
            // 'collector_lumbermill' -- the cards the Town palette renders) and MUST NOT
            // record the RETIRED source ids ('mill' / 'lumbermill' -- Town lockedIds the
            // palette hides). Recording a retired id while its replacement card is offered
            // is exactly the duplicate-offer leak this gate exists to catch.
            foreach (var target in new[] { "collector_farm", "collector_lumbermill" })
                if (!bakedSet.Contains(target))
                    failures.Add($"Default-Town seed does NOT record the WO-707 rename target '{target}' -- the food/wood collector the Town palette renders is unseeded (rename regressed)");
            foreach (var retired in new[] { "mill", "lumbermill" })
                if (bakedSet.Contains(retired))
                    failures.Add($"Default-Town seed records the RETIRED id '{retired}' (a Town lockedId) while the palette renders its replacement 'collector_farm'/'collector_lumbermill' -- the singleton gate scans record.itemId==card.id, never matches the retired record, and the replacement is re-offered while a copy exists (WO-707 rename leak)");

            // Ids that are LEGITIMATELY unlock-gated: a Town lockedId that has NO rendered
            // replacement card the palette offers is NOT a duplicate-offer leak -- it simply
            // hasn't been unlocked yet, so it can never be re-offered. 'jeweler' is exactly
            // this: Gate 1 (~line 194) ASSERTS the Town verb MUST lock 'jeweler' ("stays
            // unlock-gated"), and there is no 'collector_jeweler' card to re-offer it. Exempt
            // it here so this gate does not contradict Gate 1. (A same-visualPrefabPath check
            // was rejected: the real leak pair 'mill'->'collector_farm' uses DIFFERENT models
            // -- Structures/Windmill_Medieval vs Structures/farm -- so a same-model heuristic
            // would miss the very regression this gate must fail on. The retired-id census
            // check above is the general, precise leak detector; jeweler is the one documented
            // unlock-gate exemption.)
            var unlockGateExempt = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "jeweler" };

            // Town-type catalog rows (Resource/Collector) are the ids the Town palette can
            // render; only those are subject to the Town singleton gate. A baked id that is
            // a Town-type row MUST NOT be filtered out by Town lockedIds.
            var townTypes = new HashSet<CatalogType>(town.Types);
            int checkedTownIds = 0;
            foreach (var id in bakedIds)
            {
                if (town.LockedIds.Contains(id) && !unlockGateExempt.Contains(id))
                    failures.Add($"Default-Town seed writes '{id}', but it is a Town lockedId (palette-HIDDEN) with a rendered replacement -- the singleton gate can never match this record, so the building is re-offered while a copy exists (Farm/Lumbermill leak)");

                var entry = CatalogRegistry.Get(id);
                if (entry != null && townTypes.Contains(entry.type) && !unlockGateExempt.Contains(id))
                {
                    checkedTownIds++;
                    // Positive parity: the seeded Town-type id resolves to a row the palette
                    // would enumerate (CatalogRegistry.OfType) and is not locked out.
                    bool rendered = false;
                    foreach (var e in CatalogRegistry.OfType(entry.type))
                        if (e != null && e.id == id && !town.LockedIds.Contains(e.id)) { rendered = true; break; }
                    if (!rendered)
                        failures.Add($"Default-Town seed writes Town-type id '{id}' (type {entry.type}) that the Town palette does NOT render -- the singleton gate has no card to match it against");
                }
            }
            if (checkedTownIds == 0)
                failures.Add("no seeded baked id resolved to a rendered Town-type (Resource/Collector) catalog row -- the food/wood collector seed ('collector_farm'/'collector_lumbermill') is missing or mis-typed");
            else log.AppendLine($"  {bakedIds.Count} seeded id(s) checked, {checkedTownIds} rendered Town-type; retired-id census clean, jeweler unlock-gate exempt ok");
        }

        // =====================================================================
        //  Reflection helpers (fail-loud seams, CoreSaveContractRegression style)
        // =====================================================================

        /// <summary>Reads the itemIds out of a private census table (BakedRows / StationRows)
        /// on StrategicPlacementMigration — the REAL migration census, not a re-derivation.</summary>
        private static List<string> ReadCensusIds(string tableField, List<string> failures)
        {
            var ids = new List<string>();
            var f = typeof(StrategicPlacementMigration).GetField(tableField,
                BindingFlags.NonPublic | BindingFlags.Static);
            if (f == null)
            {
                failures.Add($"StrategicPlacementMigration.{tableField} not found by reflection — the census table moved; re-point this oracle");
                return ids;
            }
            if (!(f.GetValue(null) is Array rows)) return ids;
            foreach (var row in rows)
            {
                var idField = row.GetType().GetField("itemId");
                var id = idField != null ? idField.GetValue(row) as string : null;
                if (!string.IsNullOrEmpty(id)) ids.Add(id);
            }
            return ids;
        }

        /// <summary>Highest target version in SaveMigrator's private Steps table (the
        /// CoreSaveContractRegression seam — duplicated here so this gate stands alone).</summary>
        private static int ReadMigratorTopStep(out string err)
        {
            err = null;
            var field = typeof(SaveMigrator).GetField("Steps", BindingFlags.NonPublic | BindingFlags.Static);
            var dict = field != null ? field.GetValue(null) as IDictionary : null;
            if (dict == null || dict.Count == 0)
            {
                err = "could not read SaveMigrator.Steps by reflection — the migrator seam moved; re-point this oracle";
                return -1;
            }
            int top = int.MinValue;
            foreach (var key in dict.Keys)
                if (key is int k && k > top) top = k;
            return top;
        }

        /// <summary>CatalogRegistry is play-mode-bootstrapped; in the headless editor
        /// harness, hydrate it from the SAME canonical JSON + parse settings the real
        /// CatalogBootstrap uses (only when the functional rows are absent).</summary>
        private static void EnsureCatalogLoaded(List<string> failures, StringBuilder log)
        {
            if (CatalogRegistry.Get("forge") != null && CatalogRegistry.Get("tower_ground_archer") != null) return;

            string json = DeNelle.Core.CanonicalJson.Read("Data/Canonical/structures-catalog.json");
            if (string.IsNullOrEmpty(json))
            { failures.Add("structures-catalog.json unreadable — cannot hydrate CatalogRegistry for the gates"); return; }
            try
            {
                var settings = new JsonSerializerSettings
                {
                    Converters = { new StringEnumConverter() },
                    NullValueHandling = NullValueHandling.Ignore,
                    MissingMemberHandling = MissingMemberHandling.Ignore,
                };
                var file = JsonConvert.DeserializeObject<StructuresFile>(json, settings);
                int added = 0;
                if (file != null && file.Entries != null)
                    foreach (var e in file.Entries)
                        if (e != null && !string.IsNullOrEmpty(e.id) && CatalogRegistry.Get(e.id) == null)
                        { CatalogRegistry.Register(e); added++; }
                log.AppendLine($"  hydrated CatalogRegistry with {added} entrie(s) from structures-catalog.json");
            }
            catch (Exception ex)
            { failures.Add($"structures-catalog.json failed to parse for the gates: {ex.Message}"); }
        }

        [Serializable]
        private sealed class StructuresFile
        {
            [JsonProperty("version")] public int Version;
            [JsonProperty("entries")] public List<CatalogEntry> Entries = new List<CatalogEntry>();
        }

        private static void SetPrivate(object obj, string field, object value)
        {
            var f = obj.GetType().GetField(field, BindingFlags.NonPublic | BindingFlags.Instance);
            if (f != null) f.SetValue(obj, value);
        }

        private static GameStateService ReadServiceInstance()
        {
            var f = typeof(GameStateService).GetField("_instance", BindingFlags.NonPublic | BindingFlags.Static);
            return f != null ? f.GetValue(null) as GameStateService : null;
        }

        private static void WriteServiceInstance(GameStateService svc)
        {
            var f = typeof(GameStateService).GetField("_instance", BindingFlags.NonPublic | BindingFlags.Static);
            if (f != null) f.SetValue(null, svc);
        }
    }
}

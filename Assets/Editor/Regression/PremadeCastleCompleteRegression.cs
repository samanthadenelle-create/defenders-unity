// =============================================================================
// PremadeCastleCompleteRegression — WO-1710 + WO-1711: the premade castle is a
// FINISHED town, not a shopping list.
// -----------------------------------------------------------------------------
// Assembly: DeNelle.EditorRegression   Namespace: DeNelle.Editor
//
// THE DEFECT THIS PINS (owner's live Firebase tester-build report, 2026-09-14,
// release 2026.09.14.369302, verbatim):
//   "when I go to the build menu, it's saying that I can't make troops because it
//    says I don't have a barracks, but I do see I have barracks ... also on every
//    single structure. That's there. We need to ensure that if it exists that it's
//    marked as built so that you can't place another one on the Singleton ones so I
//    was seeing things like it was telling me to add an iron mine"
//
// TWO INDEPENDENT CAUSES (WO-1710 RCA verdict — the shared-cause hypothesis was
// DISPROVEN), so this suite has two independent halves and they must stay separate:
//
//   A. THE TROOP DOOR NEVER ASKED WHETHER A BARRACKS EXISTS. Every "no Barracks"
//      surface resolves to BarracksUnlock.IsUnlocked = FeatureFlags.Barracks &&
//      FoundingComplete, and FoundingComplete read ONLY GameState.Onboarded — the
//      FTUE completion flag. So no placement could ever open that door, which is
//      exactly why removing and re-adding the barracks changed nothing. The premade
//      castle is founded by CHOOSING Default Town, not by finishing the FTUE, so a
//      premade town sat locked forever. Fixed by making FoundingComplete accept the
//      premade founding signal too.
//
//   B. TWO AUTHORED ROOTS WERE IN NO REGISTRY AT ALL. StructureSingleton.IsPlayerBuilt
//      (the build-card / offer filter) answers from a BaseLayout record, a live
//      PlacedStructure, or a live non-baked-twin Building. The owner's castle layout
//      carries TEN authored canonicalIds; only EIGHT were in
//      StrategicPlacementMigration.BakedRows, so 'collector_forge' (Iron Mine) and
//      'workshop' (Crafting Station) got no record from any writer, authored no
//      repo.bakedTwins, and — for the collector — have their Building component
//      DestroyImmediate'd by OwnerCastleLayoutRepair.ConfigureCapabilities. Three
//      clauses, all blind, so the palette re-offered a building the player was
//      standing in front of.
//
// ⛔ WHY THIS SUITE DOES NOT LOAD A SCENE. It drives the REAL production seams
// (BarracksUnlock, the private StrategicPlacementMigration.TryWriteRecord over the
// private BakedRows table, StructureSingleton.IsPlayerBuilt) against a real GameState
// — the CoreSaveContractRegression / StrategicPlacementRegression precedent. The one
// thing it reads as an ARTIFACT rather than as source is the shipped hub scene and the
// authored layout prefab: those are DATA (what actually ships), not a source-text lint.
//
// Wire into the suite from DataRegression.RunAll (one line):
//   if (!PremadeCastleCompleteRegression.Run(out var premadeCastleReason)) failures.Add(premadeCastleReason); else log.AppendLine("[premade-castle] " + premadeCastleReason);
// =============================================================================
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEngine;
using DeNelle.Core;
using DeNelle.Core.Catalog;
using DeNelle.Core.State;
using DeNelle.Village;

namespace DeNelle.Editor
{
    public static class PremadeCastleCompleteRegression
    {
        private const string LayoutPrefabPath = "Assets/Prefabs/Village/OwnerCastleStorefrontLayout.prefab";
        private const string HubScenePath     = "Assets/Scenes/Main_Castle_Overworld.unity";
        private const string BuilderPath      = "Assets/Editor/CastleHubBuilder.cs";

        /// <summary>The persisted key FoundingChoiceController.OnDefaultTown writes — the
        /// ONE signal that says "this town was founded from the premade castle". Build Your
        /// Own writes nothing, which is why this key (and not visible geometry) is the test.</summary>
        private const string DefaultTownSelectedKey = "founding.default_town_selected";

        /// <summary>'barracks' is deliberately NOT a BakedRow — it has its own adoption path
        /// (StrategicPlacementMigration.AdoptBakedBarracksIfNeeded), which the file says in so
        /// many words. Gate 2 exempts it by name rather than by silence.</summary>
        private const string BarracksId = "barracks";

        public static bool Run(out string reason)
        {
            var failures = new List<string>();
            var log = new StringBuilder();
            log.AppendLine("--- PREMADE CASTLE COMPLETE (WO-1710 both symptoms + WO-1711 rulings A/B) ---");

            var prevServiceInstance = ReadServiceInstance();
            var created = new List<UnityEngine.Object>();

            try
            {
                var state = ScriptableObject.CreateInstance<GameState>();
                created.Add(state);
                var svcGo = new GameObject("Oracle_PremadeCastle_GameStateService");
                svcGo.SetActive(false);      // inactive so Awake never runs
                created.Add(svcGo);
                var svc = svcGo.AddComponent<GameStateService>();
                SetPrivate(svc, "_state", state);
                WriteServiceInstance(svc);

                var gridGo = new GameObject("Oracle_PremadeCastle_PlacementGrid");
                created.Add(gridGo);
                var grid = gridGo.AddComponent<PlacementGrid>();

                GateOne_TroopDoorOpensOnThePremadeFounding(state, failures, log);
                var authoredIds = GateTwo_EveryAuthoredRootIsInSomeRegistry(failures, log);
                GateThree_CensusAndCatalogAgreeOnTheTwoNewRows(failures, log);
                GateFour_NothingTheCastleSeedsIsStillOffered(state, grid, authoredIds, failures, log);
                GateFive_HomeCastleSeedsNoWalls(failures, log);
                GateSix_TowerIsTypedNotRoled(failures, log);
                GateSeven_AlreadyMigratedSaveIsBackfilled(state, created, failures, log);
            }
            catch (Exception ex)
            {
                failures.Add("premade-castle oracle threw: " + ex.GetType().Name + ": " + ex.Message);
            }
            finally
            {
                WriteServiceInstance(prevServiceInstance);
                foreach (var o in created)
                    if (o != null) UnityEngine.Object.DestroyImmediate(o);
            }

            if (failures.Count == 0)
            {
                Debug.Log(log.ToString() + "PREMADE_CASTLE_OK");
                reason = "PREMADE CASTLE OK - premade founding opens the troop door, every authored castle root " +
                         "is registered, nothing the castle seeds is re-offered, and the home build path seeds no walls";
                return true;
            }
            reason = "premade-castle: " + string.Join("; ", failures);
            Debug.LogError(log.ToString() + "PREMADE_CASTLE_FAIL: " + reason);
            return false;
        }

        // =====================================================================
        //  GATE 1 — WO-1710 symptom 1 / WO-1711 AC#3: troop training is available
        //  IMMEDIATELY on a premade town, with no FTUE lap and no remove/re-add.
        // =====================================================================
        private static void GateOne_TroopDoorOpensOnThePremadeFounding(
            GameState state, List<string> failures, StringBuilder log)
        {
            log.AppendLine("[gate 1] troop door vs the two founding paths");

            if (!FeatureFlags.Barracks)
            {
                // Not a silent skip: IsUnlocked is flag AND founding, so a flag flipped off
                // under this suite would make every assertion below vacuously meaningless.
                failures.Add("FeatureFlags.Barracks is OFF in this run - every IsUnlocked assertion below would " +
                             "pass for the wrong reason. Read the default at FeatureFlags.cs and clear PlayerPrefs 'ff.barracks'.");
                return;
            }

            // 1a. NEITHER founding signal -> shut. This is the state a brand-new save is in
            //     before the player has chosen anything, and it must stay shut.
            state.Onboarded = false;
            ClearSeen(state, DefaultTownSelectedKey);
            if (BarracksUnlock.FoundingComplete || BarracksUnlock.IsUnlocked)
                failures.Add("BarracksUnlock is OPEN on a save with neither founding signal (Onboarded=false, no " +
                             "Default-Town selection) - the unlock has lost its gate entirely");

            // 1b. THE FIX. Premade founding: the player chose Default Town, so the castle
            //     already carries a Barracks - the door opens WITHOUT the FTUE.
            MarkSeen(state, DefaultTownSelectedKey);
            if (!BarracksUnlock.FoundingComplete)
                failures.Add("FoundingComplete is FALSE on a PREMADE founding (founding.default_town_selected " +
                             "persisted, Onboarded=false) - this is the owner's 2026-09-14 defect verbatim: a " +
                             "visible, placed Barracks with the army door shut and no placement able to open it");
            if (!BarracksUnlock.IsUnlocked)
                failures.Add("BarracksUnlock.IsUnlocked is FALSE on a PREMADE founding - ArmyMusterPanel would " +
                             "still toast 'The Barracks is not built yet.' and the Manage ARMY card would still read BUILD BARRACKS");
            else log.AppendLine("  premade founding (Default Town selected, Onboarded=false) opens the door ok");

            // 1c. THE INTERACTIVE PATH IS UNTOUCHED. A Build-Your-Own player who finished the
            //     FTUE still passes on Onboarded alone - the original rule, not replaced.
            state.Onboarded = true;
            ClearSeen(state, DefaultTownSelectedKey);
            if (!BarracksUnlock.FoundingComplete || !BarracksUnlock.IsUnlocked)
                failures.Add("FoundingComplete is FALSE with Onboarded=true - the WO-724 charter Option A FTUE " +
                             "signal was REPLACED instead of joined; the premade clause must be an OR, never a swap");
            else log.AppendLine("  FTUE founding (Onboarded=true, no premade key) still opens the door ok");

            // 1d. ⛔ THE TRAP THIS PREDICATE MUST NEVER FALL INTO. FoundingComplete has to stay
            //     PURELY persisted state. HubStructureVisualInjector SetActive(false)s
            //     CastleBarracks while it reads false, and both AuthoredCastleStorefront.Find
            //     and FindObjectsByType default-exclude inactive objects - so an existence-based
            //     unlock latches OFF on the first locked load and can never recover. Prove it is
            //     scene-independent: the answer must not change when the scene has no barracks
            //     in it at all (which is exactly this headless harness).
            state.Onboarded = false;
            MarkSeen(state, DefaultTownSelectedKey);
            bool openWithNoBarracksObjectAnywhere = BarracksUnlock.IsUnlocked;
            if (!openWithNoBarracksObjectAnywhere)
                failures.Add("BarracksUnlock.IsUnlocked went FALSE in a scene containing no barracks GameObject " +
                             "while the premade founding signal is persisted - the predicate has started reading a " +
                             "live object. That is the self-latching shape: the injector deactivates the barracks " +
                             "while this reads false, so it can never read true again. Persisted state ONLY.");
            else log.AppendLine("  predicate is scene-independent (persisted state only) ok");

            // Leave the fixture on the premade founding for the gates below.
        }

        // =====================================================================
        //  GATE 2 — WO-1710 symptom 2, the general rule: EVERY authored castle root
        //  must be reachable by SOME registry. This is the gate that would have
        //  caught collector_forge and workshop the day the layout was authored.
        // =====================================================================
        private static List<string> GateTwo_EveryAuthoredRootIsInSomeRegistry(
            List<string> failures, StringBuilder log)
        {
            log.AppendLine("[gate 2] every authored castle root is in a registry");

            var authoredIds = ReadAuthoredCanonicalIds(failures);
            if (authoredIds.Count == 0)
            {
                failures.Add("read ZERO authored canonicalIds out of " + LayoutPrefabPath +
                             " - the owner's castle layout moved or the marker serialization changed; re-point this oracle");
                return authoredIds;
            }
            log.AppendLine("  " + authoredIds.Count + " authored canonicalId(s) in the shipped layout prefab");

            var censusIds = new HashSet<string>(ReadCensusIds("BakedRows", failures), StringComparer.OrdinalIgnoreCase);
            foreach (var id in ReadCensusIds("StationRows", failures)) censusIds.Add(id);

            foreach (var id in authoredIds)
            {
                if (string.Equals(id, BarracksId, StringComparison.OrdinalIgnoreCase))
                {
                    // Exempt BY NAME and for a stated reason, never by silence: the barracks
                    // has its own adoption path and StrategicPlacementMigration says, in its
                    // own comment, why it is not a BakedRow.
                    log.AppendLine("  '" + id + "' exempt - owns AdoptBakedBarracksIfNeeded (deliberately not a BakedRow)");
                    continue;
                }
                if (!censusIds.Contains(id))
                    failures.Add("authored castle root '" + id + "' is in NO registry: not a " +
                                 "StrategicPlacementMigration BakedRow/StationRow, so no writer ever gives it a " +
                                 "BaseLayout record and StructureSingleton.IsPlayerBuilt reads FALSE for a building " +
                                 "standing in front of the player - the build menu will OFFER it again (WO-1710 symptom 2)");
            }

            // The two the owner actually hit, pinned by name so a revert is loud rather than
            // merely one fewer row in a table nobody re-counts.
            foreach (var id in new[] { "collector_forge", "workshop" })
                if (!censusIds.Contains(id))
                    failures.Add("'" + id + "' is missing from the migration census - this is the exact row whose " +
                                 "absence produced the owner's 2026-09-14 report (Iron Mine / Crafting Station re-offered)");
            return authoredIds;
        }

        // =====================================================================
        //  GATE 3 — the census and the catalog must agree for the two added rows.
        //  DataRegression pins the general rule; this pins the two rows by name.
        // =====================================================================
        private static void GateThree_CensusAndCatalogAgreeOnTheTwoNewRows(
            List<string> failures, StringBuilder log)
        {
            log.AppendLine("[gate 3] the two added rows: census <-> catalog bakedTwins");

            var expected = new (string itemId, string bakedName)[]
            {
                ("collector_forge", "IronMine"),
                ("workshop",        "Crafting"),
            };

            int before = failures.Count;
            var rows = ReadCensusRows(failures);
            foreach (var (itemId, bakedName) in expected)
            {
                if (!rows.TryGetValue(itemId, out var censusBaked))
                { failures.Add("BakedRows has no row for '" + itemId + "'"); continue; }
                if (!string.Equals(censusBaked, bakedName, StringComparison.Ordinal))
                    failures.Add("BakedRows maps '" + itemId + "' to baked name '" + censusBaked +
                                 "' - the authored root's legacyName is '" + bakedName +
                                 "' (read at source in the layout prefab / hub scene), so the writer will never find it");

                var entry = CatalogRegistry.Get(itemId);
                if (entry == null)
                { failures.Add("no structures-catalog row for '" + itemId + "' - the writer skips it and the registry stays open"); continue; }
                var twins = entry.repo != null ? entry.repo.bakedTwins : null;
                bool listed = false;
                if (twins != null)
                    for (int i = 0; i < twins.Length; i++)
                        if (string.Equals(twins[i], bakedName, StringComparison.Ordinal)) listed = true;
                if (!listed)
                    failures.Add("'" + itemId + "'.repo.bakedTwins does not list '" + bakedName +
                                 "' - StructureSingleton is catalog-only since v2, so IsBuilt clause 2 stays blind " +
                                 "and DataRegression's migration-census coverage check goes RED");

                if (!StrategicPlacementMigration.IsBakedStorefrontId(itemId))
                    failures.Add("IsBakedStorefrontId('" + itemId + "') is FALSE - without it ShouldReplayRecord " +
                                 "returns TRUE and BaseLayoutLoader would catalog-replay a SECOND copy on top of the " +
                                 "owner's authored art. The record must register the building, never duplicate it.");
            }
            if (failures.Count == before) log.AppendLine("  both rows agree across census, catalog and the replay filter ok");
        }

        // =====================================================================
        //  GATE 4 — WO-1711 ruling A: after the premade founding writes its records,
        //  nothing the castle seeds is still offered as buildable.
        // =====================================================================
        private static void GateFour_NothingTheCastleSeedsIsStillOffered(
            GameState state, PlacementGrid grid, List<string> authoredIds,
            List<string> failures, StringBuilder log)
        {
            log.AppendLine("[gate 4] the premade town offers nothing it already owns");

            var tryWrite = typeof(StrategicPlacementMigration).GetMethod("TryWriteRecord",
                BindingFlags.NonPublic | BindingFlags.Static);
            if (tryWrite == null)
            { failures.Add("StrategicPlacementMigration.TryWriteRecord not found by reflection - the writer seam moved; re-point this oracle"); return; }

            state.StrategicPlacementMigrated = false;
            state.BaseLayout = new List<PlacedStructureData>();

            // Drive the REAL writer over the REAL census, exactly as the one-shot writer's
            // BakedRows loop does. Pose values are irrelevant here - the record's EXISTENCE
            // is what the offer filter reads.
            var censusIds = new List<string>(ReadCensusIds("BakedRows", failures));
            int written = 0, skippedNoRow = 0, i = 0;
            foreach (var id in censusIds)
            {
                bool ok = (bool)tryWrite.Invoke(null, new object[]
                    { state, grid, id, new Vector3(6f * i, 0f, 6f * i), 0f });
                if (ok) written++; else if (CatalogRegistry.Get(id) == null) skippedNoRow++;
                i++;
            }
            log.AppendLine("  writer produced " + written + " record(s) over the census (" + skippedNoRow + " lacked a catalog row)");
            if (written == 0)
            { failures.Add("the writer produced ZERO records over the census - gate 4 would pass vacuously"); return; }

            // The offer filter, verbatim: BuildModeController.IsSingletonBuilt /
            // StructureCardVM / BuildCollectionBrowser all ask IsPlayerBuilt, never IsBuilt.
            foreach (var id in authoredIds)
            {
                if (string.Equals(id, BarracksId, StringComparison.OrdinalIgnoreCase)) continue;   // adoption path, gate 2
                if (CatalogRegistry.Get(id) == null) continue;                                     // no row, nothing to offer
                if (!StructureSingleton.IsSingleton(id)) continue;                                 // non-singletons are legitimately re-buildable
                if (!StructureSingleton.IsPlayerBuilt(id))
                    failures.Add("authored castle root '" + id + "' still reads NOT player-built after the premade " +
                                 "founding wrote its records - the build menu offers a singleton the player already owns " +
                                 "(the owner's 'it was telling me to add an iron mine')");
            }

            // And the converse, so this gate can never be satisfied by making everything true:
            // a row the castle does NOT seed must still be buildable.
            const string unseeded = "mine_crystal";
            if (CatalogRegistry.Get(unseeded) != null && StructureSingleton.IsPlayerBuilt(unseeded))
                failures.Add("'" + unseeded + "' reads player-built on a premade town that does not seed one - the " +
                             "registry has started answering TRUE for everything, which would hide legitimate offers. " +
                             "(WO-1710 RCA verified at source: no Crystal Mine exists in the layout prefab or the hub " +
                             "scene, so its offer is LEGITIMATE and must not be 'fixed'.)");
            else log.AppendLine("  unseeded '" + unseeded + "' stays buildable (the offer filter still discriminates) ok");
        }

        // =====================================================================
        //  GATE 5 — WO-1711 ruling B: the home castle build path seeds no walls.
        // =====================================================================
        private static void GateFive_HomeCastleSeedsNoWalls(List<string> failures, StringBuilder log)
        {
            log.AppendLine("[gate 5] the home castle seeds no perimeter walls");

            // (a) THE SHIPPED ARTIFACT. What the player actually loads must carry none of the
            //     home-build wall objects. This reads the scene FILE - the thing that ships -
            //     not a source lint.
            string scene = ReadAllTextOrNull(HubScenePath);
            if (scene == null)
                failures.Add("could not read " + HubScenePath + " - cannot prove the shipped hub carries no home-built walls");
            else
            {
                foreach (var marker in new[]
                {
                    "m_Name: OuterWalls_Towers_Battlements",
                    "m_Name: InnerWallRing_CoC",
                    "m_Name: Wall_South_-", "m_Name: Wall_North_",
                    "m_Name: QWall_West_",  "m_Name: QWall_East_",
                })
                    if (scene.Contains(marker))
                        failures.Add("the shipped hub scene contains '" + marker + "' - a home-castle perimeter wall " +
                                     "object the owner's 2026-09-14 ruling removes (walls belong to a raid-flipped base)");
                log.AppendLine("  shipped hub carries no OuterWalls/Wall_South_*/QWall_* objects ok");
            }

            // (b) THE BUILDER. The loops must not come back. Named markers, not a prose scan:
            //     these are the exact object names the removed loops emitted.
            string builder = ReadAllTextOrNull(BuilderPath);
            if (builder == null)
                failures.Add("could not read " + BuilderPath + " - cannot prove the wall loops stay removed");
            else
            {
                foreach (var emitted in new[] { "$\"Wall_South_{x}\"", "$\"Wall_North_{x}\"", "$\"QWall_West_{i}\"", "$\"QWall_East_{i}\"" })
                    if (builder.Contains(emitted))
                        failures.Add("CastleHubBuilder still emits " + emitted + " - the home-castle perimeter wall " +
                                     "seeding came back (owner ruling 2026-09-14, WO-1711 B: walls only on a player-flipped base)");
                log.AppendLine("  CastleHubBuilder emits no Wall_South_/Wall_North_/QWall_ objects ok");
            }

            // (c) THE RESIDUAL, RECORDED RATHER THAN HIDDEN. The walls the owner can SEE are
            //     baked CastleSide_* scene objects from CastleWallsFromRecipe, not from the
            //     path above. Removing THOSE needs an editor tool + a re-bake and an owner
            //     ruling first, because the CastleSide_* roots also carry the gates, the
            //     OuterWorld exit seam and the Gate_* nav markers. This suite states that
            //     plainly instead of letting a green gate imply the castle has no walls.
            if (scene != null)
            {
                int sides = 0;
                foreach (var side in new[] { "North", "East", "South", "West" })
                    if (scene.Contains("m_Name: CastleSide_" + side)) sides++;
                log.AppendLine("  RESIDUAL (not a failure): " + sides + "/4 CastleSide_* baked wall roots still in the " +
                               "shipped hub. WO-1711 ruling B is only HALF done until an editor tool strips them and " +
                               "the owner rules on the gate/exit-seam/nav dependency. See the WO-1711 RESULT.");
            }
        }

        // =====================================================================
        //  GATE 6 — "which of these is a tower?" is answered by TYPE, never by ROLE.
        // =====================================================================
        private static void GateSix_TowerIsTypedNotRoled(List<string> failures, StringBuilder log)
        {
            log.AppendLine("[gate 6] tower classification comes from CatalogType, not StructureRole");

            int towers = 0, rolelessTowers = 0;
            foreach (var e in CatalogRegistry.OfType(CatalogType.Tower))
            {
                if (e == null) continue;
                towers++;
                if (string.IsNullOrEmpty(e.role)) rolelessTowers++;
            }
            if (towers == 0)
                failures.Add("CatalogRegistry.OfType(CatalogType.Tower) returned ZERO rows - the tower filter this " +
                             "lane relies on has no members; re-point before using type as the tower discriminator");
            else
                log.AppendLine("  CatalogType.Tower selects " + towers + " row(s) ok");

            // ⛔ THE FINDING WO-1711 SECTION 3 ASKED FOR, PINNED SO IT CANNOT BE FORGOTTEN:
            // StructureRole CANNOT model "is a tower". StructureRoles.Index refuses two rows
            // claiming one role (FlowTrace.Fail on collision), so a role identifies exactly ONE
            // building - it is structurally incapable of naming a CLASS of buildings. Read at
            // source 2026-09-14: every CatalogType.Tower row authors no role at all.
            if (towers > 0 && rolelessTowers != towers)
                log.AppendLine("  NOTE: " + (towers - rolelessTowers) + " Tower row(s) now author a role. That is legal " +
                               "(roles are an open vocabulary) but it still does NOT make role a tower CLASS test - " +
                               "a role resolves to one row by construction. Keep using CatalogType.Tower.");
            else
                log.AppendLine("  all " + towers + " Tower row(s) are unroled - role is NOT a tower discriminator (WO-1711 s.3) ok");
        }

        // =====================================================================
        //  GATE 7 — ⛔ THE ONE THAT ACTUALLY REACHES THE OWNER'S SAVE.
        //  A census row added AFTER a save already migrated must still be
        //  registered on that save. Without this, WO-1710's fix serves only
        //  brand-new foundings and her tester save stays broken.
        // =====================================================================
        private static void GateSeven_AlreadyMigratedSaveIsBackfilled(
            GameState state, List<UnityEngine.Object> created, List<string> failures, StringBuilder log)
        {
            log.AppendLine("[gate 7] an ALREADY-MIGRATED Default Town save gets the new census rows");

            var backfill = typeof(StrategicPlacementMigration).GetMethod("BackfillNewCensusRows",
                BindingFlags.NonPublic | BindingFlags.Static);
            if (backfill == null)
            {
                failures.Add("StrategicPlacementMigration.BackfillNewCensusRows not found by reflection. Either the " +
                             "seam moved, or the already-migrated branch of RunIfNeeded went back to a bare return - " +
                             "which silently limits the WO-1710 fix to FRESH foundings and leaves every existing " +
                             "Default-Town save (the owner's included) offering an Iron Mine it already owns.");
                return;
            }

            // The save under test: founded Default Town, ALREADY migrated (so the one-shot writer
            // is closed), and carrying records for the eight rows that existed at founding time -
            // but NOT for the two added afterwards. That is the owner's 2026-09-14 save's shape.
            state.StrategicPlacementMigrated = true;
            MarkSeen(state, DefaultTownSelectedKey);
            state.BaseLayout = new List<PlacedStructureData>();
            foreach (var alreadyOwned in new[] { "forge", "armorer" })
                state.BaseLayout.Add(new PlacedStructureData(alreadyOwned, 2, 2, 0, 1));
            int seeded = state.BaseLayout.Count;

            // The baked root has to be STANDING for the backfill to register it - the same
            // condition the one-shot writer applies. Give it the authored marker identity so the
            // lookup resolves the way it does in the hub.
            var ironMine = new GameObject("IronMine");
            created.Add(ironMine);
            ironMine.transform.position = new Vector3(9f, 0f, -12f);

            int written = (int)backfill.Invoke(null, new object[] { state, "regression" });
            if (written <= 0)
                failures.Add("BackfillNewCensusRows wrote NOTHING for an already-migrated Default Town save whose " +
                             "'IronMine' root is standing and whose BaseLayout has no 'collector_forge' record - the " +
                             "owner's existing save would still be offered an Iron Mine she owns");
            if (!StructureSingleton.IsPlayerBuilt("collector_forge"))
                failures.Add("'collector_forge' still reads NOT player-built after the backfill - the build menu keeps " +
                             "offering the Iron Mine on every save founded before the census row was added");
            else
                log.AppendLine("  backfill registered 'collector_forge' on a migrated save (" + written + " row(s)) ok");

            // Idempotent: a second pass must add nothing (records stay strictly one-shot).
            int afterFirst = state.BaseLayout.Count;
            int again = (int)backfill.Invoke(null, new object[] { state, "regression-second-pass" });
            if (again != 0 || state.BaseLayout.Count != afterFirst)
                failures.Add("a second BackfillNewCensusRows pass added " + (state.BaseLayout.Count - afterFirst) +
                             " record(s) - the HasRecord guard failed and every hub load would grow the save");
            else log.AppendLine("  second pass adds nothing (HasRecord idempotency) ok");

            // It must never touch a row the save already owns, and never invent one that is not
            // standing in the scene.
            int forgeRecords = 0, absentRecords = 0;
            for (int i = 0; i < state.BaseLayout.Count; i++)
            {
                if (state.BaseLayout[i].itemId == "forge") forgeRecords++;
                if (state.BaseLayout[i].itemId == "pet-house") absentRecords++;
            }
            // Only meaningful if that root really is absent from this harness scene — an earlier
            // suite in the same run could have left one standing, and a false RED is worse than a
            // skipped assertion.
            bool petHouseRootAbsent = AuthoredCastleStorefront.Find("EchoHollow_Pets_RoamingArea", true) == null;
            if (!petHouseRootAbsent) absentRecords = 0;
            if (forgeRecords != 1)
                failures.Add("'forge' has " + forgeRecords + " record(s) after backfill - an already-migrated row was " +
                             "re-written (duplicate) or lost; migrated rows must stay strictly one-shot");
            if (absentRecords != 0)
                failures.Add("backfill wrote a record for 'pet-house' whose baked root is NOT in this scene - it must " +
                             "register only what is actually standing, never conjure a building from the census table");
            if (state.BaseLayout.Count < seeded)
                failures.Add("backfill REMOVED records (" + seeded + " -> " + state.BaseLayout.Count + ") - it must only ever add");

            // ⛔ AND THE AUTHORIZATION. Build-Your-Own must be untouched: the branch this runs in
            // is gated on the EXPLICIT persisted Default Town key, never on visible geometry.
            ClearSeen(state, DefaultTownSelectedKey);
            if (StrategicPlacementMigration.HasExplicitDefaultTownSelection(state))
                failures.Add("HasExplicitDefaultTownSelection is TRUE with the key cleared - the backfill's " +
                             "authorization no longer discriminates and a Build-Your-Own save would be handed a " +
                             "pre-built town it never chose");
            else log.AppendLine("  backfill authorization is the explicit Default-Town key only (Build-Your-Own untouched) ok");
        }

        // =====================================================================
        //  Helpers
        // =====================================================================

        /// <summary>Every non-empty canonicalId serialized on an AuthoredCastleStorefront in the
        /// owner's layout prefab - read off the ARTIFACT so a root added tomorrow is covered
        /// without anybody remembering to extend a list in here.</summary>
        private static List<string> ReadAuthoredCanonicalIds(List<string> failures)
        {
            var ids = new List<string>();
            string text = ReadAllTextOrNull(LayoutPrefabPath);
            if (text == null)
            {
                failures.Add("could not read " + LayoutPrefabPath + " - the owner's castle layout prefab is the " +
                             "census of what the premade castle seeds; without it this suite cannot judge coverage");
                return ids;
            }
            const string key = "canonicalId:";
            int at = text.IndexOf(key, StringComparison.Ordinal);
            while (at >= 0)
            {
                int lineEnd = text.IndexOf('\n', at);
                if (lineEnd < 0) lineEnd = text.Length;
                string value = text.Substring(at + key.Length, lineEnd - at - key.Length).Trim().Trim('\r');
                if (value.Length > 0 && !ids.Contains(value)) ids.Add(value);
                at = text.IndexOf(key, lineEnd, StringComparison.Ordinal);
            }
            return ids;
        }

        private static string ReadAllTextOrNull(string path)
        {
            try { return File.Exists(path) ? File.ReadAllText(path) : null; }
            catch (Exception) { return null; }
        }

        private static Dictionary<string, string> ReadCensusRows(List<string> failures)
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var f = typeof(StrategicPlacementMigration).GetField("BakedRows",
                BindingFlags.NonPublic | BindingFlags.Static);
            if (f == null)
            {
                failures.Add("StrategicPlacementMigration.BakedRows not found by reflection - the census table moved; re-point this oracle");
                return map;
            }
            if (!(f.GetValue(null) is Array rows)) return map;
            foreach (var row in rows)
            {
                var t = row.GetType();
                var id = t.GetField("itemId")?.GetValue(row) as string;
                var baked = t.GetField("bakedName")?.GetValue(row) as string;
                if (!string.IsNullOrEmpty(id) && !map.ContainsKey(id)) map[id] = baked;
            }
            return map;
        }

        private static List<string> ReadCensusIds(string tableField, List<string> failures)
        {
            var ids = new List<string>();
            var f = typeof(StrategicPlacementMigration).GetField(tableField,
                BindingFlags.NonPublic | BindingFlags.Static);
            if (f == null)
            {
                failures.Add("StrategicPlacementMigration." + tableField + " not found by reflection - the census table moved; re-point this oracle");
                return ids;
            }
            if (!(f.GetValue(null) is Array rows)) return ids;
            foreach (var row in rows)
            {
                var id = row.GetType().GetField("itemId")?.GetValue(row) as string;
                if (!string.IsNullOrEmpty(id)) ids.Add(id);
            }
            return ids;
        }

        private static void MarkSeen(GameState state, string key)
        {
            if (state.SeenTutorials == null) state.SeenTutorials = new SerializableDict<string, bool>();
            state.SeenTutorials[key] = true;
        }

        private static void ClearSeen(GameState state, string key)
        {
            if (state.SeenTutorials == null) state.SeenTutorials = new SerializableDict<string, bool>();
            state.SeenTutorials.Remove(key);
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

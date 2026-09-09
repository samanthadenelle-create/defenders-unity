// =============================================================================
// ManageBuildDoorRegression [manage-build-door] -- WO-1571
// -----------------------------------------------------------------------------
// THE MANAGE > BUILD CARD'S BUILD BUTTON MUST OPEN PLACEMENT FOR ITS OWN ID.
//
// THE DEFECT THIS PINS (device build 358872, logcat 2026-09-07 00:58:40, owner
// report "clicking BUILD on it takes me back to build collection"):
//   Manage > BUILD > Cathedral of Magic ('arcane-tower', manageFilters CRAFT,
//   NOT BUILT) -> tap BUILD ->
//       [Flow:Navigation] opened workspace 'Build Collections' at root
//       [Flow:Build]      BuildMode.Enter - palette shown
//   ... and nothing else. The id was DROPPED at ManageScreenVM.cs:4193
//   (Invoke = () => OpenTownBuilderRequested?.Invoke()).
//
// WHY THAT IS A DEAD END, NOT A MISSING TAP: the Build Collections ROOT offers
// COLLECTIONS, and the owner's frame
// Logs/device/screens/owner-screen-20260907-005742.png shows exactly three cards
// -- Towers, Walls and Gates, Manage Placed -- so a row carrying an ECONOMY,
// CRAFT or STORAGE manageFilter could not be reached from it by any amount of
// further tapping.
//
// !! CORRECTED 2026-09-07 (WO-1572). THIS HEADER USED TO SAY card-collections.json
// AUTHORS ONLY THOSE THREE COLLECTIONS. IT DOES NOT, AND NEVER DID: it authors
// SEVEN active build collections -- Gathering / Realm / Towers / Crafting /
// Storage / Walls and Gates / Trade. The root was not authored short, it was
// EMPTIED: BuildCollectionBrowser.CollectionHasVisibleItems dropped a collection
// whose every item was a singleton reading StructureSingleton.IsBuilt, and IsBuilt
// COUNTS AN ACTIVE BAKED TWIN. Every item of build-realm (barracks / pet-house /
// arcane-tower) and build-trade (market / forge / armorer) authors a bakedTwin, so
// the scene bake hid those categories outright. WO-1572 re-points that predicate
// (and the item card, and StructureCardVM.AffordableCount) to IsPlayerBuilt -- the
// query BuildModeController.IsSingletonBuilt has asked since WO-843. The new case
// CheckCollectionRootSurvivesBakedTwins pins it.
//
// -----------------------------------------------------------------------------
// WHAT THIS SUITE PROVES, AND WHY IT IS NOT A SOURCE-TEXT ORACLE
// -----------------------------------------------------------------------------
// A grep for "PlaceStructureRequested" would pass the day the field exists and
// say nothing about where the BUTTON goes. So this drives the LIVE model:
// it stands a GameStateService fixture, walks the BUILD grid the model actually
// OFFERS, opens each not-built item's detail card, invokes that card's primary
// action, and asserts the id arrives at the direct-placement command with the
// collections-root command never firing at all.
//
// It iterates WHAT THE MODEL OFFERS rather than the whole catalog on purpose: a
// row hidden by BuildInventoryModel/IsCollectionItemVisible (locked, singleton
// already built, no manageFilters) is not a door and must never redden this.
// The non-DEFENSE subset is asserted SEPARATELY and named, because that is the
// class the defect belongs to -- a defence row at least had a collection to land
// in, so a regression that only counted defence rows would have stayed green
// through the whole outage.
//
// RED RECIPE: restore `Invoke = () => OpenTownBuilderRequested?.Invoke()` in
// ManageScreenVM.ComposeUnplacedItem. Case [build-door-carries-the-id] fails on
// every offered not-built row, and [build-door-never-lands-on-root] names the
// root hits.
// =============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using Newtonsoft.Json;
using DeNelle.Core.Catalog;
using DeNelle.Core.Jobs;
using DeNelle.Core.Manage;
using DeNelle.Core.State;
using DeNelle.Village;
using DeNelle.Village.UI;
using UnityEngine;

namespace DeNelle.Editor
{
    /// <summary>Headless contract for WO-1571's direct-placement BUILD door.</summary>
    public static class ManageBuildDoorRegression
    {
        public static bool Run(out string reason)
        {
            var failures = new List<string>();
            var log = new StringBuilder("=== ManageBuildDoorRegression ===\n");
            try
            {
                CheckLiveDoor(failures, log);
                CheckPlacementSeam(failures, log);
                CheckCollectionRootSurvivesBakedTwins(failures, log);
            }
            catch (Exception ex)
            {
                failures.Add("suite threw " + ex.GetType().Name + ": " + ex.Message);
            }

            if (failures.Count == 0)
            {
                reason = "MANAGE_BUILD_DOOR_OK every offered not-built row opens its own placement";
                Debug.Log(reason + "\n" + log);
                return true;
            }

            reason = "MANAGE_BUILD_DOOR_FAIL " + string.Join(" | ", failures);
            Debug.LogError(reason + "\n" + log);
            return false;
        }

        // ── the live half ────────────────────────────────────────────────────

        private static void CheckLiveDoor(List<string> failures, StringBuilder log)
        {
            GameStateService prior = GameStateService.Instance;
            GameObject host = null;
            GameState fixture = null;
            try
            {
                fixture = ScriptableObject.CreateInstance<GameState>();
                fixture.Onboarded = true;
                fixture.VillageTier = 4;
                // ONE placed row only, so the grid is dominated by NOT-BUILT rows -- which is the
                // population this suite is about. A blank BaseLayout would also work; a single
                // forge keeps the placed/unplaced split honest and exercises ComposeBuildingItem
                // alongside ComposeUnplacedItem.
                fixture.BaseLayout = new List<PlacedStructureData>
                {
                    new PlacedStructureData("forge", 2, 2, 0, 1),
                };
                fixture.BuildingTiers["forge"] = 1;
                fixture.Wood = 100000;
                fixture.Iron = 100000;
                var balances = fixture.Resources;
                balances.Food = 100000;
                balances.Coins = 100000;
                balances.Crystals = 100000;
                fixture.Resources = balances;
                fixture.ObsidianQueue = ObsidianQueueState.Empty();

                host = new GameObject("GSS (manage-build-door oracle)");
                var service = host.AddComponent<GameStateService>();
                if (!InstallState(service, fixture))
                {
                    failures.Add("[fixture] GameStateService state seam is unavailable; the live case cannot run");
                    return;
                }

                var placed = new List<string>();
                int rootHits = 0;

                var vm = new ManageScreenVM();
                vm.PlaceStructureRequested = id => placed.Add(id);
                vm.OpenTownBuilderRequested = () => rootHits++;
                vm.EnterTab(ManageTabId.Build);
                vm.Rebuild();

                // BUILD's first screen is now category navigation. Walk every authored category
                // and collect its disclosed item ids so this door oracle still measures the full
                // inventory instead of mistaking category ids for catalog ids.
                var tileIds = new List<string>();
                for (int categoryIndex = 0; categoryIndex < BuildFilter.Membership.Length; categoryIndex++)
                {
                    vm.SetFilter(BuildFilter.Membership[categoryIndex]);
                    var grid = vm.ComposeWorkspace();
                    var buildTab = grid != null ? grid.ActiveTab : null;
                    if (buildTab == null || buildTab.Tiles == null) continue;
                    for (int i = 0; i < buildTab.Tiles.Count; i++)
                    {
                        var tile = buildTab.Tiles[i];
                        if (tile == null || string.IsNullOrEmpty(tile.Id) || tileIds.Contains(tile.Id)) continue;
                        tileIds.Add(tile.Id);
                    }
                }
                if (tileIds.Count == 0)
                {
                    failures.Add("[build-grid-offers-nothing] the BUILD categories disclosed zero item tiles, " +
                                 "so the door cannot be proven either way");
                    return;
                }

                int doorsChecked = 0, nonDefenceDoors = 0;
                var missedNonDefence = new List<string>();

                for (int i = 0; i < tileIds.Count; i++)
                {
                    string id = tileIds[i];
                    vm.OpenDetail(ManageTabId.Build, id, null, null);
                    var detail = vm.ComposeWorkspace();
                    var tab = detail != null ? detail.ActiveTab : null;
                    var selection = tab != null ? tab.Selection : null;
                    var action = selection != null ? selection.PrimaryAction : null;
                    // Only the BUILD face is this suite's business. An UPGRADE / MAX / QUEUED face
                    // belongs to a placed row and is pinned by the Buildings + Defense suites.
                    if (action == null || !string.Equals(action.Label, "BUILD", StringComparison.Ordinal))
                        continue;

                    var entry = CatalogRegistry.Get(id);
                    bool nonDefence = entry != null && !BuildFilter.Matches(entry, BuildFilter.Defense);

                    int before = placed.Count;
                    if (action.Activate == null)
                    {
                        failures.Add("[build-door-carries-the-id] '" + id + "' shows a BUILD face with NO command");
                        continue;
                    }
                    action.Activate.Invoke();
                    doorsChecked++;
                    if (nonDefence) nonDefenceDoors++;

                    if (placed.Count != before + 1 ||
                        !string.Equals(placed[placed.Count - 1], id, StringComparison.Ordinal))
                    {
                        failures.Add("[build-door-carries-the-id] BUILD on '" + id + "' did not request placement " +
                                     "for that id (got " + (placed.Count > before ? "'" + placed[placed.Count - 1] + "'" : "nothing") + ")");
                        if (nonDefence) missedNonDefence.Add(id);
                    }
                }

                if (doorsChecked == 0)
                    failures.Add("[build-door-carries-the-id] no BUILD face was found on any offered tile, so the " +
                                 "door is unproven -- the fixture, not the door, is what changed");

                // The class of the defect, stated as its own case so a green run says the words.
                if (nonDefenceDoors == 0)
                    failures.Add("[non-defence-row-has-a-door] the BUILD grid offered no NON-DEFENCE not-built row " +
                                 "at all, so the arcane-tower class is untested. Check manageFilters authoring.");
                if (missedNonDefence.Count > 0)
                    failures.Add("[non-defence-row-has-a-door] these non-DEFENSE rows still lose their id: " +
                                 string.Join(", ", missedNonDefence));

                // RED: this is the whole outage in one number.
                if (rootHits != 0)
                    failures.Add("[build-door-never-lands-on-root] the collections-root command fired " + rootHits +
                                 " time(s) from a card BUILD button -- that root authors no ECONOMY/CRAFT/STORAGE " +
                                 "collection and is a dead end for those rows");

                log.AppendLine("build tiles=" + tileIds.Count + " BUILD doors=" + doorsChecked +
                               " (non-defence=" + nonDefenceDoors + ") root hits=" + rootHits);
            }
            finally
            {
                SetGssInstance(prior);
                if (host != null) UnityEngine.Object.DestroyImmediate(host);
                if (fixture != null) UnityEngine.Object.DestroyImmediate(fixture);
            }
        }

        // ── WO-1572: the collection ROOT under a surfaced bake ───────────────

        /// <summary>
        /// A save with ZERO placed structures and EVERY baked twin standing must still offer
        /// all seven authored build collections, and must still offer 'arcane-tower'.
        ///
        /// RED RECIPE: restore <c>StructureSingleton.IsBuilt(entry)</c> in
        /// BuildCollectionBrowser.CollectionHasVisibleItems. build-realm and build-trade drop
        /// (every item of each authors a bakedTwin) and this case names them.
        ///
        /// The twins are STOOD UP HERE ON PURPOSE. With no twin GameObjects in the scene
        /// IsBuilt and IsPlayerBuilt agree, and a suite that skipped this setup would pass
        /// against the broken predicate and prove nothing. The twin NAMES are read from the
        /// catalog (repo.bakedTwins) rather than hardcoded, so a re-authored bake cannot
        /// leave this case quietly testing an empty scene.
        /// </summary>
        private static void CheckCollectionRootSurvivesBakedTwins(List<string> failures, StringBuilder log)
        {
            GameStateService prior = GameStateService.Instance;
            GameObject host = null;
            GameState fixture = null;
            var twins = new List<GameObject>();
            try
            {
                var predicate = typeof(BuildCollectionBrowser).GetMethod("CollectionHasVisibleItems",
                    BindingFlags.NonPublic | BindingFlags.Static);
                var itemVisible = typeof(BuildCollectionBrowser).GetMethod("IsCollectionItemVisible",
                    BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static);
                if (predicate == null || itemVisible == null)
                {
                    failures.Add("[collection-root-survives-bake] BuildCollectionBrowser.CollectionHasVisibleItems / " +
                                 "IsCollectionItemVisible could not be resolved -- the root filter was renamed or " +
                                 "removed and this contract is unpinned");
                    return;
                }

                const string CollectionsPath = "Assets/Resources/Data/Canonical/card-collections.json";
                if (!File.Exists(CollectionsPath))
                {
                    failures.Add("[collection-root-survives-bake] " + CollectionsPath + " is missing");
                    return;
                }
                var document = JsonConvert.DeserializeObject<DeNelle.Core.CardCollectionDocument>(
                    File.ReadAllText(CollectionsPath));
                var build = new List<DeNelle.Core.CardCollectionDefinition>();
                if (document != null && document.Collections != null)
                    foreach (var c in document.Collections)
                        if (c != null && c.Active &&
                            string.Equals(c.Context, "build", StringComparison.OrdinalIgnoreCase))
                            build.Add(c);
                if (build.Count == 0)
                {
                    failures.Add("[collection-root-survives-bake] card-collections.json authors no active build " +
                                 "collection at all -- the fixture, not the filter, is what changed");
                    return;
                }

                // Blank town: no BaseLayout rows at all, so IsPlayerBuilt is false everywhere.
                fixture = ScriptableObject.CreateInstance<GameState>();
                fixture.Onboarded = true;
                fixture.VillageTier = 4;
                fixture.BaseLayout = new List<PlacedStructureData>();
                fixture.ObsidianQueue = ObsidianQueueState.Empty();
                host = new GameObject("GSS (collection-root oracle)");
                var service = host.AddComponent<GameStateService>();
                if (!InstallState(service, fixture))
                {
                    failures.Add("[collection-root-survives-bake] GameStateService state seam is unavailable");
                    return;
                }

                // Stand every authored baked twin up, active, named exactly as the catalog says.
                foreach (var c in build)
                {
                    if (c.Items == null) continue;
                    foreach (var item in c.Items)
                    {
                        var entry = item != null ? CatalogRegistry.Get(item.ItemId) : null;
                        var names = entry != null && entry.repo != null ? entry.repo.bakedTwins : null;
                        if (names == null) continue;
                        foreach (var bakedName in names)
                            if (!string.IsNullOrEmpty(bakedName))
                                twins.Add(new GameObject(bakedName));
                    }
                }

                // StructureSingleton memoizes both queries by Time.frameCount, which does NOT
                // advance inside a headless suite -- CheckLiveDoor placed a forge one method
                // ago and that answer would leak straight into this blank-town case. Clearing
                // is the honest fix; ordering the cases would only hide the coupling.
                ClearSingletonMemos();

                // The fixture is only meaningful if it actually surfaced a twin. Assert the
                // twin-counting query SEES it while the player-owned query does not: that pair
                // is what makes the seven-collection assertion below a real red recipe.
                bool twinSeen = StructureSingleton.IsBuilt("arcane-tower");
                bool playerBuilt = StructureSingleton.IsPlayerBuilt("arcane-tower");
                if (!twinSeen || playerBuilt)
                    failures.Add("[baked-twin-fixture-is-real] expected arcane-tower IsBuilt=true (twin standing) and " +
                                 "IsPlayerBuilt=false (nothing placed); got IsBuilt=" + twinSeen +
                                 " IsPlayerBuilt=" + playerBuilt + " over " + twins.Count + " twin object(s)");

                var dropped = new List<string>();
                foreach (var c in build)
                {
                    bool shown = predicate.Invoke(null, new object[] { c }) is bool b && b;
                    if (!shown) dropped.Add(c.CollectionId);
                }
                if (dropped.Count > 0)
                    failures.Add("[collection-root-survives-bake] a blank town with every baked twin standing lost " +
                                 dropped.Count + " of " + build.Count + " authored collection(s): " +
                                 string.Join(", ", dropped) + " -- the root filter is counting a baked twin as built");

                // The row the owner's report was about, named on its own so a green run says it.
                bool arcaneOffered = itemVisible.Invoke(null, new object[] { "arcane-tower" }) is bool a && a;
                if (!arcaneOffered)
                    failures.Add("[arcane-tower-is-offered] 'arcane-tower' is not offered on a blank town, so " +
                                 "Manage > BUILD > Cathedral of Magic still has nowhere to land");

                log.AppendLine("collection root: authored=" + build.Count + " shown=" + (build.Count - dropped.Count) +
                               " twins stood up=" + twins.Count + " arcane-tower offered=" + arcaneOffered);
            }
            finally
            {
                for (int i = 0; i < twins.Count; i++)
                    if (twins[i] != null) UnityEngine.Object.DestroyImmediate(twins[i]);
                ClearSingletonMemos();
                SetGssInstance(prior);
                if (host != null) UnityEngine.Object.DestroyImmediate(host);
                if (fixture != null) UnityEngine.Object.DestroyImmediate(fixture);
            }
        }

        /// <summary>Drops both per-frame memos so a fixture change is actually observed.</summary>
        private static void ClearSingletonMemos()
        {
            string[] names = { "s_builtMemo", "s_playerBuiltMemo" };
            for (int i = 0; i < names.Length; i++)
            {
                var field = typeof(StructureSingleton).GetField(names[i],
                    BindingFlags.NonPublic | BindingFlags.Static);
                var dictionary = field != null ? field.GetValue(null) as System.Collections.IDictionary : null;
                if (dictionary != null) dictionary.Clear();
            }
        }

        // ── the seam half ────────────────────────────────────────────────────

        /// <summary>
        /// The door is only worth anything if the thing it calls exists and routes through the
        /// browser's OWN pick seam. Reflection, not grep: a renamed or deleted method reds here.
        /// </summary>
        private static void CheckPlacementSeam(List<string> failures, StringBuilder log)
        {
            var controller = typeof(BuildModeController).GetMethod("EnterBuildModeForStructure",
                BindingFlags.Public | BindingFlags.Instance, null, new[] { typeof(string) }, null);
            if (controller == null || controller.ReturnType != typeof(bool))
                failures.Add("[placement-seam-exists] BuildModeController.EnterBuildModeForStructure(string):bool " +
                             "is gone -- the Manage BUILD door has nothing to call");

            var palette = typeof(BuildPaletteUI).GetMethod("PlaceById",
                BindingFlags.Public | BindingFlags.Instance, null, new[] { typeof(string) }, null);
            if (palette == null || palette.ReturnType != typeof(bool))
                failures.Add("[placement-seam-exists] BuildPaletteUI.PlaceById(string):bool is gone");

            var browser = typeof(BuildCollectionBrowser).GetMethod("PlaceById",
                BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance,
                null, new[] { typeof(string) }, null);
            if (browser == null || browser.ReturnType != typeof(bool))
                failures.Add("[placement-seam-exists] BuildCollectionBrowser.PlaceById(string):bool is gone -- the " +
                             "direct door can no longer reuse the browser's own Place/Done commit");

            var command = typeof(ManageScreenVM).GetField("PlaceStructureRequested",
                BindingFlags.Public | BindingFlags.Instance);
            if (command == null || command.FieldType != typeof(Action<string>))
                failures.Add("[placement-seam-exists] ManageScreenVM.PlaceStructureRequested is gone or no longer " +
                             "carries an id");

            log.AppendLine("placement seam: controller/palette/browser/command resolved by reflection");
        }

        // ── helpers (mirroring ManageBuildingsCardRegression's) ──────────────

        private static bool InstallState(GameStateService service, GameState state)
        {
            var stateField = typeof(GameStateService).GetField("_state",
                BindingFlags.NonPublic | BindingFlags.Instance);
            if (stateField == null) return false;
            stateField.SetValue(service, state);
            return SetGssInstance(service);
        }

        private static bool SetGssInstance(GameStateService service)
        {
            var instance = typeof(GameStateService).GetField("_instance",
                BindingFlags.NonPublic | BindingFlags.Static);
            if (instance == null) return false;
            instance.SetValue(null, service);
            return true;
        }
    }
}

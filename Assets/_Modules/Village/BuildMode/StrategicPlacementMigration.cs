// =============================================================================
// StrategicPlacementMigration — WO-673 L3: standdown + ONE-SHOT migration
// writer for the auto-placed functional structures. ALWAYS ON since WO-682
// removed ff.strategicplacement (owner ruling 2026-07-12).
// -----------------------------------------------------------------------------
// Assembly: DeNelle.Village   Namespace: DeNelle.Village
//
// THE INJECTOR-SHAPE RULING (docs/WO673_ARCHITECTURE_REVIEW.md §3, BINDING):
// flag-gated standdown + a one-time "default layout writer" migration — NEVER a
// permanently dual-mode injector (one system that places AND one that replays
// the same id is the double-spawn factory; ONE owner per concern).
//
// WHAT THIS FILE IS:
//   1. THE MIGRATION WRITER — on the first marker-unset load of the
//      HOME hub (SceneRouter.Castle), each auto-placed functional structure
//      (the baked ring storefronts + the two runtime crafting stations) is
//      converted into a BaseLayout PlacedStructureData record at its CURRENT
//      position/yaw (grid-quantized: cell snap drift ≤ ~1.5m is an ACCEPTED
//      felt-pass item — architect ruling §3(a); yaw preserved as the nearest
//      90° yawStep). Then the persisted migration marker
//      (GameState.StrategicPlacementMigrated, save schema v30) is set and the
//      save written. Migration NEVER runs twice — the marker is the one-shot latch.
//   2. THE STANDDOWN ORACLE — the single place the rest of the lane asks
//      "does the bake/injector own this structure, or does BaseLayout?".
//      HubStructureVisualInjector (baked storefront SetActive(false), the
//      proven Barracks pattern), the two station injectors (skip spawn), and
//      BaseLayoutLoader (replay filter) ALL route through this class, so the
//      marker gates BOTH sides and there is never a frame where two owners
//      spawn the same id:
//        • no marker  → bakes/injectors visible, no records replayed.
//        • marker set → records replay, bakes hidden / injectors dark.
//      PER STRUCTURE (bakes): standdown applies to a baked id that HAS a migrated
//      BaseLayout record OR a structures-catalog row (the player can build it —
//      WO-682 blank-template new game: marker set + zero records still hides the
//      row-having bakes). A bake with NO catalog row AND no record keeps owning
//      its structure. RUNTIME STATIONS (apothecary / jewelers-bench): WO-703 /
//      BLANK-1 (owner ruling 2026-07-13) SUPERSEDED the "never lost" carve-out —
//      once the marker is set the station injectors stand down unconditionally
//      (fresh start = tree + well + walls/gates, nothing else); a save carrying a
//      station record still replays it via BaseLayoutLoader.
//
// SAME-LOAD ORDERING (structural double-spawn guard): during the very load the
// migration runs in, the records are brand new — the injectors already ran
// (bakes visible) and standdown must NOT flip mid-session. StanddownActive is
// therefore latched OFF for the scene-load the migration executed in (scene
// handle compare — no event-order dependence); the ownership handover happens
// atomically on the NEXT home-hub load: records replay, bakes stand down.
//
// WO-682 (owner 2026-07-12): ff.strategicplacement is REMOVED — this lane is
// ALWAYS ON. New games set the marker in ResetToNewGame (nothing to migrate =
// blank template); pre-existing saves load marker false (SaveMigrator v30) and
// migrate once on their next home-hub load.
// =============================================================================

using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using DeNelle.Core.Catalog;
using DeNelle.Core.Diagnostics;
using DeNelle.Core.State;

namespace DeNelle.Village
{
    /// <summary>
    /// WO-673 L3 — one-shot migration of the auto-placed functional structures into
    /// BaseLayout records, plus the standdown gates the injectors/loader ask.
    /// </summary>
    public static class StrategicPlacementMigration
    {
        // ── THE MIGRATION ID TABLE (census: CastleHubBuilder.cs:288-301 + the
        //    HubStructureVisualInjector Swaps rows). bakedName = the scene object
        //    the bake/injector owns today; itemId = the structures-catalog row the
        //    BaseLayout record replays through.
        //    WO-1250 (2026-08-27): the WO-1161 identity straightening left this
        //    table one rename behind. The bake skins:
        //      Blacksmith_Weapons_Storefront ← Structures/Forge  = Weaponsmith = id "forge"
        //      Forge_Armor_Storefront        ← Structures/armorer = Armorer     = id "armorer"
        //    The retired mapping (workshop→Blacksmith / forge→Forge_Armor) is what
        //    handed a new player those two buildings: Default-Town template grant of
        //    "workshop"+"forge" surfaced the Weaponsmith and Armorer visuals.
        //    Jeweler_Gems_Storefront was removed from the current bake but legacy
        //    scenes may still carry it — tolerated-missing-in-scene like every other row. ─────
        private struct BakedRow
        {
            public string bakedName;
            public string itemId;
        }

        private static readonly BakedRow[] BakedRows =
        {
            new BakedRow { bakedName = "Blacksmith_Weapons_Storefront", itemId = "forge" },
            new BakedRow { bakedName = "Lumbermill_Wood_Storefront",    itemId = "collector_lumbermill" },
            new BakedRow { bakedName = "Windmill_Food_Storefront",      itemId = "collector_farm" },
            new BakedRow { bakedName = "EchoHollow_Pets_RoamingArea",   itemId = "pet-house" },
            new BakedRow { bakedName = "Forge_Armor_Storefront",        itemId = "armorer" },
            new BakedRow { bakedName = "ArcaneTower_MagicUpgrades",     itemId = "arcane-tower" },
            new BakedRow { bakedName = "Marketplace_Monetization",      itemId = "market" },
            new BakedRow { bakedName = "Jeweler_Gems_Storefront",       itemId = "jeweler" },

            // ── WO-1710 sym.2 (2026-09-14): the two authored roots NO registry could see ──
            // The owner's castle layout (OwnerCastleStorefrontLayout.prefab -> the shipped
            // Main_Castle_Overworld) carries ELEVEN AuthoredCastleStorefront markers, TEN with
            // a canonicalId. Eight matched a row above. These two did not, and were therefore
            // unknown to EVERY registry in the game at once: no BakedRow, so the one-shot writer
            // never reached them; no repo.bakedTwins, so IsBuilt clause 2 was blind; and
            // ConfigureCapabilities DestroyImmediate's the Building on the collector branch
            // (OwnerCastleLayoutRepair.cs:270-296 — "a producer is not the weapons vendor"), so
            // HasPlacedInstance clause 3 could not see collector_forge either. Result: the build
            // menu kept OFFERING an Iron Mine and a Crafting Station that were standing in front
            // of the player (owner's live tester-build report 2026-09-14: "it was telling me to
            // add an iron mine"). Registering them here is the SAME shape as the eight above —
            // and, critically, it also makes IsBakedStorefrontId true for them, so
            // ShouldReplayRecord returns FALSE and BaseLayoutLoader never catalog-replays a
            // second copy over the owner's authored Tripo art.
            // bakedName = the marker's legacyName (AuthoredCastleStorefront.Find matches it):
            // Main_Castle_Overworld.unity:7704-7705 and :28360-28361, read at source 2026-09-14.
            // ⚠ Each new row MUST also author its repo.bakedTwins in structures-catalog.json —
            // DataRegression.cs:3252-3260 pins that census/catalog agreement and goes RED
            // otherwise. Both catalog copies (Resources + StreamingAssets) were updated with it.
            new BakedRow { bakedName = "IronMine",                      itemId = "collector_forge" },
            new BakedRow { bakedName = "Crafting",                      itemId = "workshop" },
        };

        // ── Runtime crafting stations (the true auto-placers, review G-C). Their
        //    holders hardcode (11,0,2)/(-11,0,2); if the holder is absent when the
        //    writer runs (injector order), the hardcoded constant is the position of
        //    record. Their catalog rows do NOT exist yet (L1 owns the catalog) — the
        //    writer tolerates the missing row (skip + Warn) so lanes compose at gate
        //    time; until a row lands, the injector keeps owning the station. ───────
        private struct StationRow
        {
            public string  holderName;
            public string  itemId;
            public Vector3 fallbackPos;
        }

        private static readonly StationRow[] StationRows =
        {
            new StationRow { holderName = "ApothecaryStation (runtime)",    itemId = "apothecary",     fallbackPos = new Vector3( 11f, 0f, 2f) },
            new StationRow { holderName = "JewelersBenchStation (runtime)", itemId = "jewelers-bench", fallbackPos = new Vector3(-11f, 0f, 2f) },
        };

        /// <summary>bakedName → catalog itemId lookup (standdown queries).</summary>
        private static string ItemIdForBaked(string bakedName)
        {
            for (int i = 0; i < BakedRows.Length; i++)
                if (BakedRows[i].bakedName == bakedName) return BakedRows[i].itemId;
            return null;
        }

        // ── LEVER 1 read-only census accessors (owner 2026-07-24, "stores pre-stand on a
        //    fresh hub", WWCD) ─────────────────────────────────────────────────────────
        // CastleVendorNpcInjector anchors a vendor NPC to EVERY baked storefront / runtime
        // station even when standdown deactivated it on a fresh save (nothing replayed ->
        // the old poll waited forever -> zero vendors). It reads THESE tables so the
        // role->anchor map stays single-sourced here (no duplicated list, no reflection).

        /// <summary>Read-only view of the baked storefront census (bakedName, itemId).</summary>
        public static IReadOnlyList<(string bakedName, string itemId)> BakedStorefronts()
        {
            var list = new List<(string, string)>(BakedRows.Length);
            for (int i = 0; i < BakedRows.Length; i++)
                list.Add((BakedRows[i].bakedName, BakedRows[i].itemId));
            return list;
        }

        /// <summary>Read-only view of the runtime crafting-station census
        /// (holderName, itemId, fallbackPos) — so a station's speaker NPC can be seated at
        /// its anchor even when the station injector stood down on a fresh save.</summary>
        public static IReadOnlyList<(string holderName, string itemId, Vector3 fallbackPos)> StationAnchors()
        {
            var list = new List<(string, string, Vector3)>(StationRows.Length);
            for (int i = 0; i < StationRows.Length; i++)
                list.Add((StationRows[i].holderName, StationRows[i].itemId, StationRows[i].fallbackPos));
            return list;
        }

        // Scene-load latch: the handle of the scene load the migration executed in.
        // StanddownActive stays FALSE for that load (bakes already visible; loader
        // must not replay the freshly-written records) and flips true on the next
        // home-hub load — the atomic ownership handover. int.MinValue = "never".
        private static int _migratedSceneHandle = int.MinValue;

        // ── PUBLIC GATES (the whole lane asks these) ─────────────────────────────

        /// <summary>True when the one-shot migration has run (persisted marker — set by
        /// the writer on migrated saves, or by ResetToNewGame on a blank-template new
        /// game, WO-682), and we are NOT still inside the scene-load the migration ran
        /// in. Scene-scoped to the HOME hub — the only scene BaseLayout replays in.</summary>
        public static bool StanddownActive
        {
            get
            {
                var svc = GameStateService.Instance;
                if (svc == null || svc.State == null || !svc.State.StrategicPlacementMigrated) return false;
                var scene = SceneManager.GetActiveScene();
                if (scene.name != DeNelle.Core.SceneRouter.Castle) return false;   // records only replay here
                return scene.handle != _migratedSceneHandle;                       // not the migration load itself
            }
        }

        /// <summary>
        /// Baked-storefront standdown (HubStructureVisualInjector): true ONLY when standdown
        /// is active AND a BaseLayout RECORD will actually replace the bake named
        /// <paramref name="bakedName"/> (migrated save, or a player-built replacement; the
        /// record replays via BaseLayoutLoader). Hide it (Barracks SetActive(false) pattern)
        /// so the record's live Building takes over — no double. False → the bake KEEPS
        /// owning + rendering the structure.
        ///
        /// LEVER 1 RECONCILIATION (owner 2026-07-24, WWCD — supersedes the WO-682 blank-template
        /// carve-out for baked STOREFRONTS): the gate was `HasRecord || HasCatalogRow`, which stood
        /// a baked store DOWN the moment it had a catalog row EVEN WITH NO RECORD. On a fresh/blank
        /// save (marker set by ResetToNewGame, BaseLayout empty) every storefront has a catalog row
        /// but NO record, so all 8 hid with nothing to replay → empty grass under floating vendor
        /// NPCs (the captured on-device screenshot). The owner ruling is the opposite: on a fresh
        /// hub the baked stores PRE-STAND, VISIBLE + STAFFED (CoC). So standdown now keys on
        /// HasRecord ALONE — stand down a bake ONLY when a record genuinely replaces it. Un-built
        /// baked stores stay as the pre-stand staffed store; a player-built replacement (its record)
        /// still hides its baked original exactly as before. (Runtime STATIONS keep the WO-703
        /// unconditional standdown via StanddownActiveForStation — this change is baked-only.)
        /// </summary>
        public static bool StanddownActiveForBaked(string bakedName, out string itemId)
        {
            // Owner 2026-09-12: BOTH founding paths keep HubStructureVisualInjector models.
            // WO-834's "!MayBakedTwinSurface" clause hid the 8 on EMPTY REALM (Seeker 365996
            // 07:38: BaseLayout=0 then standdown migrated->forge with nothing to replay).
            // Stand down a bake ONLY when a BaseLayout record will replace it (placed wins).
            // MayBakedTwinSurface remains the barracks / vendor blank-town gate.
            itemId = ItemIdForBaked(bakedName);
            // Owner 2026-09-12: never restyle the ring on a later load. Injector LightSkins
            // stay on the bake; BaseLayout records may exist for move/upgrade but must not
            // SetActive(false) this host (that kills the LightSkin child) or catalog-replay it.
            return false;
        }

        /// <summary>
        /// Runtime-station standdown (Apothecary / Jeweler's Bench injectors): true whenever
        /// standdown is active (marker set + not the migration load). WO-703 / BLANK-1
        /// (owner ruling 2026-07-13, supersedes the "never lost" carve-out): a fresh start is
        /// the TREE, the WELL, and the WALLS (gates included) — NOTHING else, so a
        /// marker-set save with NO station record shows NO station (and, downstream, no
        /// vendor NPC — CastleVendorNpcInjector anchors to the live Building). A save that
        /// DOES carry a station record replays it through BaseLayoutLoader as before
        /// (ShouldReplayRecord keys off the same StanddownActive). The old
        /// HasRecord||HasCatalogRow qualifier kept row-less stations spawning on blank
        /// saves — that is exactly the residual the ruling stands down.
        /// </summary>
        public static bool StanddownActiveForStation(string itemId)
        {
            return StanddownActive;
        }

        /// <summary>
        /// Replay filter (BaseLayoutLoader.Rebuild): a MIGRATION-MANAGED id replays only
        /// while standdown is active (marker set + not the migration load) — otherwise
        /// the bake/injector owns that structure and replaying the record would
        /// double-spawn it. Non-managed ids (towers, walls, player-placed defenses)
        /// always replay.
        /// </summary>
        public static bool ShouldReplayRecord(string itemId)
        {
            if (IsBakedStorefrontId(itemId)) return false;
            if (!IsManagedId(itemId)) return true;
            return StanddownActive;
        }

        /// <summary>The 8 ring storefronts HubStructureVisualInjector skins. Not stations.</summary>
        public static bool IsBakedStorefrontId(string itemId)
        {
            if (string.IsNullOrEmpty(itemId)) return false;
            for (int i = 0; i < BakedRows.Length; i++)
                if (BakedRows[i].itemId == itemId) return true;
            return false;
        }

        /// <summary>True when <paramref name="itemId"/> is one this migration owns.</summary>
        public static bool IsManagedId(string itemId)
        {
            if (string.IsNullOrEmpty(itemId)) return false;
            for (int i = 0; i < BakedRows.Length; i++)
                if (BakedRows[i].itemId == itemId) return true;
            for (int i = 0; i < StationRows.Length; i++)
                if (StationRows[i].itemId == itemId) return true;
            return false;
        }

        /// <summary>True when <paramref name="itemId"/> has a structures-catalog row —
        /// i.e. the player can build it through the palette (WO-682 standdown rule).</summary>
        private static bool HasCatalogRow(string itemId)
        {
            return !string.IsNullOrEmpty(itemId) && CatalogRegistry.Get(itemId) != null;
        }

        private static bool HasRecord(string itemId)
        {
            var svc = GameStateService.Instance;
            var layout = svc != null && svc.State != null ? svc.State.BaseLayout : null;
            if (layout == null) return false;
            for (int i = 0; i < layout.Count; i++)
                if (layout[i].itemId == itemId) return true;
            return false;
        }

        // =====================================================================
        //  THE BAKED-BARRACKS ADOPTION (owner ruling 2026-08-06)
        //  "place it, give it the NPC, make it COUNT, and do NOT offer it as a
        //   buildable item."
        // =====================================================================
        //
        // OWNER, VERBATIM: "there's no person that stands with the barracks that comes out
        // of the shipped version, which means that it doesn't count as one. so it's just
        // counting as an object just as scenery."
        //
        // WHAT WAS WRONG (verified at source, not inferred): the shipped 'CastleBarracks'
        // is a raw polyperfect prefab seated by an EDITOR MENU TOOL
        // (Assets/Editor/WallTools/CastleBarracksPlacer.cs) that attaches NO components at
        // all - confirmed in the scene YAML (m_AddedComponents: []). No Building, no
        // BuildingInteractable, no PlacedStructure, no BaseLayout record, no
        // VillageController registration. So StructureSingleton.IsPlayerBuilt("barracks")
        // answered FALSE and the structure counted as scenery. Two symptoms, ONE root:
        //   1. it is not owned, not on the roster, not upgradeable; and
        //   2. THE LIVE TRAP - the Barracks build card still read BUILDABLE, and placing
        //      one fired NotifyPlaced -> Enforce -> StandDownBakedTwins, which SILENTLY
        //      DELETED the shipped barracks (the catalog row is repo.singleton with
        //      repo.bakedTwins ["CastleBarracks"] - ONE per town).
        //
        // THE FIX: ADOPT it. Convert the shipped barracks into a real, owned BaseLayout
        // record at its own pose, materialise the catalog structure live through the ONE
        // creation path (BaseLayoutLoader.Spawn -> StructureFactory "GameplayBuilding" =
        // Building + BuildingInteractable + VillageController registration => owned,
        // rostered, upgradeable), then hand the singleton to it via
        // StructureSingleton.NotifyPlaced, which (a) stands the raw bake down so only ever
        // ONE exists and (b) raises SingletonResolved('barracks') - the seam
        // BarracksNpcInjector already subscribes to, which reseats the drillmaster.
        // NOTHING IN THE NPC PATH IS REBUILT: this rings the exact bell the owner's own
        // accidental delete/respawn cycle rang ("IT MIGHT HAVE BEEN FROM THE WALL I DELETED
        // AND RESPAWNED AND NOW HAS A NPC") - the proof that the spawn logic already works.
        // Once the record exists, IsPlayerBuilt is TRUE, so the build card renders "Built"
        // and the id can no longer be armed or placed: the trap closes with no change to
        // the build-menu gate itself (BuildModeController.IsSingletonBuilt already asks
        // IsPlayerBuilt - it was being told the truth about a structure that genuinely was
        // not owned).
        //
        // ── WHY 'barracks' IS DELIBERATELY *NOT* A BakedRows ENTRY ────────────────────
        // A one-line add to the table above is NOT the fix, and would be a regression.
        // Adding it makes IsManagedId("barracks") true, which switches on three behaviours
        // the barracks must NOT have:
        //   (a) StanddownActiveForBaked would start answering for 'CastleBarracks',
        //       taking the standdown decision away from the WO-724 unlock rule;
        //   (b) ShouldReplayRecord('barracks') would become conditional on StanddownActive,
        //       so the record would be WITHHELD from replay for a whole session; and
        //   (c) the WO-673 latch in StructureSingleton.EnforceInternal would SKIP the
        //       baked-twin standdown whenever StanddownActive is false - a placed barracks
        //       and the raw bake standing at the same time = two barracks.
        // On top of that the timing is wrong at the root: the one-shot writer runs on the
        // FOUNDING hub load, where the barracks is still LOCKED (WO-724 =
        // BarracksUnlock.IsUnlocked = ff.barracks AND founding-complete), therefore
        // DEACTIVATED and invisible to the writer's active-only scan - and a record written
        // there would replay a barracks that WO-724 says must not exist yet.
        // So 'barracks' stays OUT of BakedRows: IsManagedId('barracks') remains FALSE, the
        // WO-673 latch never engages for it, ShouldReplayRecord stays unconditionally true,
        // StanddownActiveForBaked keeps returning false for 'CastleBarracks', and the twin
        // standdown remains the ordinary StructureSingleton placed-wins path that already
        // works today. The adoption is instead timed to the WO-724 UNLOCK - the first
        // moment the shipped barracks legitimately exists.
        //
        // ── WO-834 IS PRESERVED ──────────────────────────────────────────────────────
        // The "is this the predefined map?" test is StructureSingleton.MayBakedTwinSurface
        // ("barracks"): OPEN on Default-Town / legacy saves (the template grant written by
        // RunIfNeeded below, and SaveMigrator's v36 seed), CLOSED on a Build-Your-Own blank
        // founding. A blank town therefore NEVER adopts - its player builds a barracks from
        // the palette exactly as before, and WO-834's "Fresh Default Town: unchanged" is
        // honoured in the only direction that matters (nothing is taken away; the town
        // simply now OWNS the barracks it was always shown). everBuiltStructureIds is still
        // read ONLY as the WO-834 surface permission - never as ownership.
        //
        // ── WO-843 IS PRESERVED ──────────────────────────────────────────────────────
        // A baked twin still does not count as player-built. Adoption does not change that
        // rule; it removes the twin from the equation by making a REAL placed structure.
        // And it is deliberately ONE-WAY per session: if the player later sells or destroys
        // the barracks, the twin resurfaces as the WO-819 stand-in and the card correctly
        // reads BUILDABLE again (rebuild fresh at full cost) - adoption does not re-fire and
        // re-gift it.

        private const string BarracksItemId    = "barracks";
        private const string BarracksBakedName = "CastleBarracks";

        // Terminal latch: set once the adoption has RUN or has been RULED OUT, so the
        // bootstrap poll stops asking (and the trace never spams). Domain-load scoped.
        private static bool _barracksAdoptionSettled;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetAdoptionStatics()
        {
            _barracksAdoptionSettled = false;
        }

        // The castle-hub merge convention used by every barracks surface
        // (BarracksNpcInjector.IsCastleHubScene / StructureSingletonBootstrap): the chrome
        // fires on BOTH hub scene names, because SceneRouter.Castle is flag-dependent.
        internal static bool IsCastleHubScene(string n) =>
            n == "MainCastle_Hall" || n == "Main_Castle_Overworld";

        /// <summary>
        /// Adopt the SHIPPED baked barracks into the player's base as a real, owned
        /// structure (see the block comment above for the ruling, the root cause, and why
        /// this is NOT a <see cref="BakedRows"/> entry).
        /// <para>Returns TRUE when the question is SETTLED for this domain load - either it
        /// adopted, or it ruled the adoption out - so the caller's poll can stop. Returns
        /// FALSE while the answer is still pending (not in the hub yet, save service not up,
        /// the WO-724 unlock has not flipped yet, or a transient refusal worth retrying).</para>
        /// </summary>
        public static bool AdoptBakedBarracksIfNeeded()
        {
            if (_barracksAdoptionSettled) return true;

            var svc = GameStateService.Instance;
            var state = svc != null ? svc.State : null;
            if (state == null) return false;                       // save service not up yet - ask again

            string sceneName = SceneManager.GetActiveScene().name;
            if (!IsCastleHubScene(sceneName)) return false;        // the shipped barracks only exists in the hub

            // WO-724 UNLOCK GATE - the ONE reason this is not part of the one-shot writer.
            // Until ff.barracks is ON *and* founding is complete the shipped barracks does
            // not exist (HubStructureVisualInjector.TrySwap deactivates it), so there is
            // nothing to adopt. The unlock can flip LIVE mid-session (the FTUE completes
            // in-hub with no scene reload), which is why the caller polls instead of asking
            // once. NOT settled - keep watching.
            if (!BarracksUnlock.IsUnlocked) return false;

            // Already owned: the player built one, or a previous pass/session adopted.
            // StructureSingleton already owns the twin standdown from here.
            if (StructureSingleton.IsPlayerBuilt(BarracksItemId))
                return SettleAdoption(
                    "a placed/recorded barracks already owns the singleton - nothing to adopt (build card correctly reads Built)");

            // WO-834 blank-town gate = the "is this the PREDEFINED map?" test.
            if (!StructureSingleton.MayBakedTwinSurface(BarracksItemId))
                return SettleAdoption(
                    "blank-town surface gate CLOSED (Build Your Own founding) - the shipped barracks is not this save's; " +
                    "the player builds one from the palette (WO-834 unchanged)");

            if (CatalogRegistry.Get(BarracksItemId) == null)
            {
                FlowTrace.Warn("Barracks",
                    $"adoption: no structures-catalog row for '{BarracksItemId}' - the shipped barracks cannot be adopted " +
                    "(it stays scenery). Restore the row and the adoption runs on the next hub load.");
                return SettleAdoption("no structures-catalog row for 'barracks'");
            }

            if (FindByNameInclInactive(BarracksBakedName) == null)
                return SettleAdoption(
                    $"no baked '{BarracksBakedName}' in scene '{sceneName}' - this is not the predefined map, nothing to adopt");

            using var _ = FlowTrace.Enter("Barracks", "StrategicPlacementMigration.AdoptBakedBarracksIfNeeded");

            // ORDER MATTERS - the pose OF RECORD must be read only AFTER the visual injector
            // has surfaced, skinned and re-seated the bake: the 'CastleBarracks' swap row
            // carries setLocalPos (38.3, 0, 36), and while the barracks was LOCKED TrySwap
            // returned BEFORE applying it. Reading the transform first would therefore record
            // a stale spot and the adopted barracks would replay somewhere else.
            bool stands = Guard.Try("Barracks", "EnsureBarracksSurfaced (settle the pose before adopting)",
                HubStructureVisualInjector.EnsureBarracksSurfaced, fallback: false);
            if (!stands)
            {
                FlowTrace.Warn("Barracks",
                    "adoption: EnsureBarracksSurfaced refused this pass (one of its own gates said no) - " +
                    "NOT settling; retrying on the next poll.");
                return false;
            }

            var baked = FindByNameInclInactive(BarracksBakedName);
            if (baked == null)
            {
                FlowTrace.Warn("Barracks",
                    $"adoption: '{BarracksBakedName}' disappeared between the gate and the pose read - retrying on the next poll.");
                return false;
            }

            var grid = PlacementGrid.Instance;
            if (grid == null)
                grid = new GameObject("PlacementGrid").AddComponent<PlacementGrid>();
            if (state.BaseLayout == null)
                state.BaseLayout = new List<PlacedStructureData>();

            Vector3 pose = baked.position;
            float yaw = baked.eulerAngles.y;
            FlowTrace.Step("Barracks",
                $"adoption: shipped '{BarracksBakedName}' stands at {pose} yaw {yaw:0.#} - converting it into an OWNED BaseLayout record.");

            if (!TryWriteRecord(state, grid, BarracksItemId, pose, yaw))
            {
                FlowTrace.Warn("Barracks",
                    "adoption: no record was written (see the line above for why) - the shipped barracks stays scenery.");
                return SettleAdoption("record write refused");
            }

            // The RECORD is the ownership. MarkEverBuilt is a belt for legacy saves whose
            // template grant predates this path - it is the WO-834 SURFACE PERMISSION only,
            // never ownership, and nothing reads it as such.
            var authoredMarker = baked.GetComponent<AuthoredCastleStorefront>();
            if (authoredMarker != null && authoredMarker.PreserveAuthoredVisual && authoredMarker.CanonicalId == BarracksItemId)
            {
                int recordIndex = state.BaseLayout.Count - 1;
                var authoredRecord = state.BaseLayout[recordIndex];
                authoredRecord.authoredSourceId = BarracksItemId;
                authoredRecord.authoredPose = PlacedStructure.CaptureAuthoredPose(baked);
                if (!BaseLayoutLoader.TryGetAuthoredFootprint(baked, grid, authoredRecord.authoredPose, out var authoredCell, out var authoredFootprint))
                {
                    state.BaseLayout.RemoveAt(recordIndex); // only the record written by this adoption attempt
                    FlowTrace.Fail("Barracks", "Authored adoption footprint is unavailable/outside the grid; no record saved and geometry unchanged.");
                    return false;
                }
                authoredRecord.cellX = authoredCell.x;
                authoredRecord.cellZ = authoredCell.y;
                authoredRecord.worldY = baked.position.y;
                state.BaseLayout[recordIndex] = authoredRecord;
            }
            state.MarkEverBuilt(BarracksItemId);
            Guard.Try("Barracks", "persist the adopted barracks record", () => svc.Save());

            var adopted = state.BaseLayout[state.BaseLayout.Count - 1];

            // Materialise it NOW, through the ONE creation path, so the owner never needs a
            // place/delete cycle - or even a scene reload - for the shipped barracks to count.
            var loader = BaseLayoutLoader.Instance != null
                ? BaseLayoutLoader.Instance : BaseLayoutLoader.EnsureExists();
            PlacedStructure placed = Guard.Try<PlacedStructure>("Barracks",
                "spawn the adopted barracks (BaseLayoutLoader.Spawn)",
                () => loader != null ? loader.Spawn(adopted, grid) : null, fallback: null);
            if (placed == null)
                FlowTrace.Fail("Barracks",
                    "adoption: the record persisted but the owned barracks did NOT spawn this session - it replays on the " +
                    "next hub load, and the build card already reads Built (the trap is closed either way).");
            else
                FlowTrace.Step("Barracks",
                    $"adoption: spawned the OWNED barracks at cell ({adopted.cellX},{adopted.cellZ}) - Building + " +
                    "BuildingInteractable + VillageController registration (counted, rostered, upgradeable).");

            // Hand the singleton over: stands the raw shipped bake down (only ever ONE,
            // placed wins) and raises SingletonResolved('barracks'), which BarracksNpcInjector
            // already subscribes to - it reseats the drillmaster onto the owned barracks. This
            // is the SAME seam the owner's accidental delete/respawn cycle rang; the NPC path
            // is reused verbatim, not rebuilt.
            Guard.Try("Barracks", "StructureSingleton.NotifyPlaced('barracks') after adoption",
                () => StructureSingleton.NotifyPlaced(BarracksItemId, placed != null ? placed.gameObject : null));

            return SettleAdoption(
                "ADOPTED - the shipped barracks is now an owned, counted, upgradeable structure with its drillmaster, " +
                "and the Barracks build card reads Built (no longer offered)");
        }

        private static bool SettleAdoption(string reason)
        {
            _barracksAdoptionSettled = true;
            FlowTrace.Step("Barracks", $"baked-barracks adoption SETTLED: {reason}.");
            return true;
        }

        // ── THE ONE-SHOT WRITER ──────────────────────────────────────────────────

        /// <summary>
        /// Convert every auto-placed functional structure into a BaseLayout record at its
        /// current position/yaw, set the persisted marker, and save. Idempotent: no-ops
        /// when the marker is already set or we're not in the home hub.
        /// Called by the bootstrap below; public for the regression harness.
        /// </summary>
        public static void RunIfNeeded()
        {
            var svc = GameStateService.Instance;
            if (svc == null || svc.State == null)
            {
                FlowTrace.Warn("Placement", "migration writer: GameStateService not ready — retry on next home-hub load.");
                return;
            }
            var state = svc.State;
            var scene = SceneManager.GetActiveScene();
            if (scene.name != DeNelle.Core.SceneRouter.Castle) return;   // home hub only
            if (state.StrategicPlacementMigrated)
            {
                // Only an explicit persisted Default Town selection authorizes adding
                // newly approved template rights to an already-migrated save. Visible
                // authored geometry alone is NOT evidence (both founding paths retain it).
                if (HasExplicitDefaultTownSelection(state))
                {
                    int added = GrantAuthoredTemplateIds(state);
                    int backfilled = BackfillNewCensusRows(state, "already-migrated Default Town save");
                    if (added > 0 || backfilled > 0)
                    {
                        Buildings.Progression.ResourceCollectorBootstrap.RetryAllAfterStateReady();
                        svc.Save();
                        FlowTrace.Step("Placement",
                            $"Explicit Default Town selection: granted {added} newly authored template identities " +
                            $"and BACKFILLED {backfilled} newly censused record(s).");
                    }
                }
                // Records for the rows this save ALREADY migrated stay strictly one-shot — the
                // backfill above only ever adds a row that had NO record, guarded by HasRecord.
                return;
            }

            using var _ = FlowTrace.Enter("Placement", "StrategicPlacementMigration.RunIfNeeded (one-shot writer)");

            var grid = PlacementGrid.Instance;
            if (grid == null)
                grid = new GameObject("PlacementGrid").AddComponent<PlacementGrid>();
            if (state.BaseLayout == null)
                state.BaseLayout = new List<PlacedStructureData>();

            int migrated = 0, skippedNoRow = 0, skippedAbsent = 0;

            // Baked ring storefronts — position of record = the live scene object.
            for (int i = 0; i < BakedRows.Length; i++)
            {
                var row = BakedRows[i];
                var t = FindByName(row.bakedName);
                if (t == null)
                {
                    // Not in this scene bake (e.g. jeweler was removed from the ring) — fine.
                    FlowTrace.Step("Placement",
                        $"migration: baked '{row.bakedName}' not present in scene — nothing to migrate for '{row.itemId}'.");
                    skippedAbsent++;
                    continue;
                }
                if (TryWriteRecord(state, grid, row.itemId, t.position, t.eulerAngles.y)) migrated++;
                else skippedNoRow++;
            }

            // Runtime crafting stations — live holder if spawned, else the hardcoded const.
            for (int i = 0; i < StationRows.Length; i++)
            {
                var row = StationRows[i];
                var t = FindByName(row.holderName);
                Vector3 pos = t != null ? t.position : row.fallbackPos;
                float yaw = t != null ? t.eulerAngles.y : 0f;
                if (TryWriteRecord(state, grid, row.itemId, pos, yaw)) migrated++;
                else skippedNoRow++;
            }

            // WO-834 TEMPLATE GRANT: this save was granted the prebuilt town (a WO-748
            // Default-Town founding, or a legacy pre-v30 save migrating its auto-placed
            // town) — mark the WHOLE template as ever-built so the blank-town surface
            // gate (StructureSingleton.MayBakedTwinSurface) stays OPEN for it once the
            // marker below flips true. Deliberately includes rows the loops above
            // SKIPPED (no catalog row / absent in scene): the grant is a right of the
            // TEMPLATE, not of one bake — a skipped station's Lever-1 speaker and a
            // later-added catalog row must keep behaving as today. Plus 'barracks': the
            // WO-724 baked-barracks-at-unlock surface is part of the prebuilt town
            // (a Build-Your-Own player builds theirs from the palette instead).
            int granted = 0;
            for (int i = 0; i < BakedRows.Length; i++)
                if (state.MarkEverBuilt(BakedRows[i].itemId)) granted++;
            for (int i = 0; i < StationRows.Length; i++)
                if (state.MarkEverBuilt(StationRows[i].itemId)) granted++;
            if (state.MarkEverBuilt("barracks")) granted++;
            granted += GrantAuthoredTemplateIds(state);
            FlowTrace.Step("Placement",
                $"migration: default-town template grant -> {granted} id(s) marked ever-built " +
                "(WO-834 blank-town gate stays open for this save's baked pieces).");
            // Seeker 365962 22:31:56: grant flipped ever-built AFTER WireScene, so farm/lumbermill
            // ticked liveCollector=no for the whole session. Re-run fallbacks now that the ledger is true.
            Buildings.Progression.ResourceCollectorBootstrap.RetryAllAfterStateReady();

            // Set the one-shot marker + latch this scene load (standdown flips on the
            // NEXT home-hub load — the atomic bake→BaseLayout ownership handover), then
            // persist. The marker is set even when some rows skipped: a missing catalog
            // row keeps its bake/injector alive via the per-structure HasRecord gate, so
            // nothing is lost — and the writer stays strictly one-shot.
            state.StrategicPlacementMigrated = true;
            _migratedSceneHandle = scene.handle;
            svc.Save();

            FlowTrace.Step("Placement",
                $"migration COMPLETE: {migrated} structure(s) -> BaseLayout, {skippedNoRow} skipped (no catalog row), " +
                $"{skippedAbsent} absent in scene. Marker persisted (save v{SaveSchema.CurrentVersion}); " +
                "standdown activates on the NEXT home-hub load.");
        }

        public static bool HasExplicitDefaultTownSelection(GameState state) =>
            state?.SeenTutorials != null && state.SeenTutorials.TryGetValue(StarterSettlementCompletion.SelectedKey, out bool selected) && selected;

        /// <summary>
        /// WO-1710 (2026-09-14): write records for census rows this save has NEVER had a record
        /// for, on an ALREADY-MIGRATED Default Town save.
        /// <para>⛔ WHY THIS EXISTS, AND WHY "IT IS ONE-SHOT" WAS NOT AN ANSWER. The one-shot
        /// writer runs ONCE, at the first home-hub load after the founding — so it can only ever
        /// register the census rows that existed ON THAT DAY. Add a row to
        /// <see cref="BakedRows"/> afterwards (which is exactly what WO-1710 did for
        /// <c>collector_forge</c> and <c>workshop</c>) and EVERY save founded before the edit
        /// keeps the defect forever: the building stands in the town, no record names it,
        /// <c>StructureSingleton.IsPlayerBuilt</c> reads FALSE, and the palette re-offers it. The
        /// owner's own 2026-09-14 tester save is in precisely that state, so a fix that only
        /// served FRESH foundings would have read as fixed here and failed her felt-test
        /// unchanged.</para>
        /// <para>This is the SAME authorization the branch it lives in was built for on 09-12 —
        /// "newly approved template rights for an already-migrated save", gated on the EXPLICIT
        /// persisted Default Town selection, never on visible geometry (both founding paths keep
        /// the authored models). A Build-Your-Own save is untouched.</para>
        /// <para>Safe by three properties already proven for these rows: <see cref="HasRecord"/>
        /// makes it idempotent and keeps migrated rows strictly one-shot; the row is present in
        /// the scene or it is skipped; and because it is a <see cref="BakedRows"/> member,
        /// <see cref="ShouldReplayRecord"/> returns FALSE (BaseLayoutLoader never spawns a second
        /// catalog copy over the owner's authored art) and <c>StructureSingleton.Enforce</c>
        /// takes its LatchSkipped branch (the bake is never stood down).</para>
        /// <para>The pose of record is the LIVE object's, exactly as the one-shot writer reads it.</para>
        /// </summary>
        private static int BackfillNewCensusRows(GameState state, string why)
        {
            if (state == null) return 0;
            var grid = PlacementGrid.Instance;
            if (grid == null)
                grid = new GameObject("PlacementGrid").AddComponent<PlacementGrid>();
            if (state.BaseLayout == null)
                state.BaseLayout = new List<PlacedStructureData>();

            int written = 0;
            for (int i = 0; i < BakedRows.Length; i++)
            {
                var row = BakedRows[i];
                if (HasRecord(row.itemId)) continue;                 // already owned - one-shot holds
                var t = FindByNameInclInactive(row.bakedName);
                if (t == null) continue;                             // not in this scene bake - nothing to register
                if (TryWriteRecord(state, grid, row.itemId, t.position, t.eulerAngles.y))
                {
                    written++;
                    FlowTrace.Step("Placement",
                        $"backfill ({why}): censused '{row.itemId}' had NO record on this save and its baked root " +
                        $"'{row.bakedName}' is standing - registered it so the build menu stops offering a building the player owns.");
                }
            }
            return written;
        }

        private static int GrantAuthoredTemplateIds(GameState state)
        {
            if (state == null) return 0;
            int granted = 0;
            var scene = SceneManager.GetActiveScene();
            foreach (var marker in Object.FindObjectsByType<AuthoredCastleStorefront>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (marker == null || marker.gameObject.scene != scene || string.IsNullOrEmpty(marker.CanonicalId)) continue;
                if (AuthoredCastleStorefront.Find(marker.LegacyName, includeInactive: true) != marker.transform) continue;
                if (state.MarkEverBuilt(marker.CanonicalId)) granted++;
            }
            return granted;
        }

        /// <summary>
        /// Write one PlacedStructureData for <paramref name="itemId"/> at the given world
        /// pose. Tolerates a missing catalog row (skip + Warn naming the id — lanes
        /// compose at gate time) and an already-present record (idempotency belt).
        /// Grid-quantizes the position (accepted ~1.5m drift, named in the trace) and
        /// snaps yaw to the nearest 90° step.
        /// </summary>
        private static bool TryWriteRecord(GameState state, PlacementGrid grid,
            string itemId, Vector3 worldPos, float yawDeg)
        {
            if (CatalogRegistry.Get(itemId) == null)
            {
                FlowTrace.Warn("Placement",
                    $"migration: no structures-catalog row for '{itemId}' — record NOT written (bake/injector keeps " +
                    "owning it via the per-structure standdown gate; add the row and re-migrate a fresh save).");
                return false;
            }
            if (HasRecord(itemId))
            {
                FlowTrace.Step("Placement",
                    $"migration: BaseLayout already has a record for '{itemId}' — not duplicated (idempotent).");
                return false;
            }

            var cell = grid.WorldToCell(worldPos);
            Vector3 snapped = grid.CellToWorld(cell);
            float driftM = Vector2.Distance(new Vector2(worldPos.x, worldPos.z),
                                            new Vector2(snapped.x, snapped.z));
            int yawSteps = ((Mathf.RoundToInt(yawDeg / 90f)) % 4 + 4) % 4;

            state.BaseLayout.Add(new PlacedStructureData(
                itemId, cell.x, cell.y, yawSteps, level: 1,
                yawOffset: 0f, worldY: 0f, wallMounted: false));

            FlowTrace.Step("Placement",
                $"migrated {itemId} @ {worldPos} -> BaseLayout (cell {cell.x},{cell.y}, yawSteps {yawSteps}, " +
                $"snap drift {driftM:0.##}m — accepted felt-pass item).");
            return true;
        }

        // Name match across the loaded scene(s) — mirrors HubStructureVisualInjector.
        private static Transform FindByName(string name)
        {
            return AuthoredCastleStorefront.Find(name);
        }

        // Name match INCLUDING inactive objects. The plain FindByName above is active-only,
        // and the shipped 'CastleBarracks' is DEACTIVATED for the whole pre-unlock life of a
        // save (WO-724) — an active-only scan reads that as "not in this scene" and would
        // rule the adoption out on a map that plainly has one. Mirrors
        // StructureSingleton.FindByNameInclInactive / CastleVendorNpcInjector.
        private static Transform FindByNameInclInactive(string name)
        {
            return AuthoredCastleStorefront.Find(name, includeInactive: true);
        }
    }

    /// <summary>
    /// Self-bootstrapping DDOL runner (mirrors <see cref="BaseLayoutLoaderBootstrap"/> —
    /// no scene edit, CLAUDE.md §3) that fires the one-shot migration writer when the
    /// HOME hub loads with the marker unset. Runs a frame late (coroutine) so
    /// GameStateService / the injector-spawned stations exist first; waits briefly for
    /// the save service on a cold boot.
    /// </summary>
    internal sealed class StrategicPlacementMigrationBootstrap : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            var go = new GameObject("StrategicPlacementMigrationBootstrap");
            DontDestroyOnLoad(go);
            go.AddComponent<StrategicPlacementMigrationBootstrap>();
        }

        private void Awake()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
            TryArm();   // the boot scene may already BE the home hub
        }

        private void OnDestroy()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (mode != LoadSceneMode.Single) return;   // additive streams never migrate
            TryArm();
        }

        private void TryArm()
        {
            if (SceneManager.GetActiveScene().name != DeNelle.Core.SceneRouter.Castle) return;
            StopAllCoroutines();
            StartCoroutine(RunDeferred());
        }

        private IEnumerator RunDeferred()
        {
            // One frame so same-load Awake/Start bootstraps (GameStateService, the station
            // injectors) settle; then a short bounded wait for the save service on cold boot.
            yield return null;
            int waited = 0;
            while ((GameStateService.Instance == null || GameStateService.Instance.State == null) && waited < 300)
            {
                waited++;
                yield return null;
            }
            if (GameStateService.Instance == null || GameStateService.Instance.State == null)
            {
                FlowTrace.Warn("Placement",
                    "migration bootstrap: GameStateService never appeared (300 frames) — migration deferred to next hub load.");
                yield break;
            }
            StrategicPlacementMigration.RunIfNeeded();

            // OWNER RULING 2026-08-06 — the SHIPPED barracks must COUNT. Its adoption cannot
            // ride the one-shot writer above: at founding the barracks is still LOCKED
            // (WO-724 = ff.barracks AND founding-complete) and therefore deactivated, and a
            // record written then would replay a barracks the unlock says must not exist yet.
            // So watch for the unlock instead — it can flip LIVE mid-session (the FTUE
            // completes in-hub with no reload, the same reason BarracksNpcInjector runs its
            // own 1 Hz poll) — and adopt the moment it does. See
            // StrategicPlacementMigration.AdoptBakedBarracksIfNeeded for the full ruling and
            // for why 'barracks' is deliberately NOT a BakedRows entry.
            yield return AdoptBarracksWhenUnlocked();
        }

        // 0.5 s cadence: this bounds the ONLY residual window in which the shipped barracks
        // can be standing while the Barracks build card still reads BUILDABLE (the
        // silent-delete trap). Realtime so a paused / time-scaled hub still adopts.
        private const float AdoptionPollSeconds = 0.5f;

        private IEnumerator AdoptBarracksWhenUnlocked()
        {
            while (StrategicPlacementMigration.IsCastleHubScene(SceneManager.GetActiveScene().name))
            {
                // fallback TRUE on a throw: a permanently-throwing adoption must settle rather
                // than re-throw twice a second forever. The Guard line names the failure.
                bool settled = Guard.Try("Barracks", "baked-barracks adoption poll",
                    StrategicPlacementMigration.AdoptBakedBarracksIfNeeded, fallback: true);
                if (settled) yield break;
                yield return new WaitForSecondsRealtime(AdoptionPollSeconds);
            }
        }
    }
}

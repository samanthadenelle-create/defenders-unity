// =============================================================================
// CastleVendorNpcInjector — runtime, NON-DESTRUCTIVE placement of a STATIC vendor
// NPC at each of the 8 castle storefronts, wired to the existing YarnSpinner
// structure dialogue. Mirrors VillageNpcInjector's self-bootstrap, but spawns
// STAND-STILL NPCs (no wander / no follow) and gates to the castle hub scene.
// -----------------------------------------------------------------------------
// WHY a runtime injector (not a scene edit / regen):
//   CastleHubBuilder bakes the 8 storefronts into MainCastle_Hall.unity, each
//   with an EMPTY marker child named "NPC_<Role>_Interactable" at front-offset
//   (0,0,6). Re-saving / regenerating that scene to drop real NPC bodies in
//   carries the project's known scene-resave corruption risk (CLAUDE.md §3), and
//   the markers are otherwise inert. So this self-bootstrapping DDOL singleton,
//   on every MainCastle_Hall load, FINDS those markers and spawns a real NPC body
//   at each — WITHOUT ever touching the .unity file. Idempotent per load.
//
// STATIC, not townsfolk: we reuse VillageNpcInjector's townsfolk body source
//   (the Resources/NPCs People-pack prefabs: mesh + URP material + Animator +
//   AmbientNPC + TownsfolkBubble), but Configure(arch, wander:FALSE, ...) — which
//   makes AmbientNPC itself disable the NavMeshAgent and stand its ground (see
//   AmbientNPC.Start ~line 190). No roam, no follow. The NPC just stands at the
//   shopfront with its idle sway and faces outward toward the approaching hero.
//
// INTERACTION: a slim CastleNpcInteractable (same proximity F / mobile-tap pattern
//   as BuildingInteractable) opens the ONE parameterized Yarn structure dialogue
//   via DialogueService.PlayStructure(structureId, label). No new dialogue is
//   invented — it's the existing StructureMenu / PetHouse node path.
//
// Village -> Core only; uses DialogueService + PanelManager exactly as the
// existing village interaction code does (no reflection, no cross-asmdef ref).
// =============================================================================

using System.Collections;
using DeNelle.Core.Catalog;   // StructureRoles — the single naming authority
using DeNelle.Core.Diagnostics;
using DeNelle.Core.UI;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

namespace DeNelle.Village
{
    /// <summary>Runtime, non-destructive placement of static vendor NPCs at the castle storefronts.</summary>
    public sealed class CastleVendorNpcInjector : MonoBehaviour
    {
        public static CastleVendorNpcInjector Instance { get; private set; }

        // TIMING-PROOF NOTIFY (owner F8 "still no NPC"): NotifyBuildingPlaced can fire from
        // BuildModeController.Place / BaseLayoutLoader.Spawn BEFORE the injector's AfterSceneLoad
        // Bootstrap set Instance (a placement/replay that beats Awake). The old guard DROPPED those
        // — the collector's only non-poll seat path — so a reloaded Farm/Lumbermill silently got no
        // NPC. Instead we ENQUEUE the (id, transform) here and DRAIN it in Awake once Instance is set.
        private static readonly System.Collections.Generic.List<(string id, Transform tf)> s_pendingPlacements =
            new System.Collections.Generic.List<(string id, Transform tf)>();

        private const string TargetScene = "MainCastle_Hall";
        // WO-608 merge: MainCastle_Hall + OuterWorld collapse into Main_Castle_Overworld.
        // Castle-hub chrome must fire on the merged scene too, while staying castle-only
        // (never Village2/raids). Mirrors CastleBeamHider / CastleSpawnPointInjector.
        private const string MergedTargetScene = "Main_Castle_Overworld";
        private static bool IsCastleHubScene(string n) => n == TargetScene || n == MergedTargetScene;

        // (WO-682: the baked "NPC_<Role>_Interactable" marker constants were deleted with
        // the flag-off marker loop — vendors anchor to the Building collection instead.)

        // Resources/NPCs People-pack bodies — same source VillageNpcInjector uses. We
        // pick a body per role for a little visual variety (smith for the metal trades,
        // merchant for the commerce stalls, peasants for the rest). All fall back to the
        // merchant if a specific body is missing.
        private const string BodySmith    = "NPCs/NPC_Blacksmith";
        private const string BodyMerchant = "NPCs/NPC_Merchant";
        private const string BodyPeasantA = "NPCs/NPC_Peasant_Mevina";
        private const string BodyPeasantB = "NPCs/NPC_Peasant_Tob";

        /// <summary>
        /// One vendor definition keyed by the marker ROLE (the first token of the
        /// storefront name, e.g. "Blacksmith"). StructureId is the id the Yarn
        /// StructureMenu / DialogueCommandBridge + ResourceBuildingProgression expect.
        /// </summary>
        private struct Vendor
        {
            public string BodyRes;
            public string StructureId;
            public string Label;
            public TownsfolkDialogue.Archetype Arch;
        }

        // Role -> vendor. The roles come from CastleHubBuilder's 8 structures
        // (header ~25-27): Blacksmith, Lumbermill, Windmill, EchoHollow, Forge,
        // ArcaneTower, Jeweler, Marketplace.
        //
        // structureId mapping rationale (ids verified against
        // Buildings/Progression/ResourceBuildingProgression.cs + Resources/Portraits/*):
        //   - REAL data + portrait exist for: farm, lumbermill, forge, market, pet-house.
        //   - Blacksmith -> "armorer"   (2026-08-23: this line said "forge" and had been wrong
        //                                since WO-444 — the BLACKSMITH sells ARMOUR, so the code
        //                                below returns "armorer". Corrected, not deleted, so the
        //                                next reader is not sent back to the pre-WO-444 mapping.)
        //   - Lumbermill -> "lumbermill"
        //   - Windmill   -> "farm"      (food production == Farm's Food resource)
        //   - EchoHollow -> "pet-house" (routes to the dedicated PetHouse Yarn node)
        //   - Forge      -> "forge"
        //   - ArcaneTower-> "arcane-tower" (no progression def yet; StructureMenu still
        //                                   opens gracefully — no yield/portrait, talk works)
        //   - Jeweler    -> "jeweler"   (own shoppable storefront — Jeweler's Bench, gem/jewelry goods)
        //   - Marketplace-> "market"
        // The "no def" ids (arcane-tower) are SAFE: CmdStructureStatus tolerates a null
        // Find() and a missing Portraits/<id> image, so the menu still shows + the Talk
        // path runs. See "Uncertainty" in the work-order report.
        // ⛔ THE `StructureId` LITERALS BELOW ARE FROZEN JOIN KEYS — into dialogues.json
        // conversations, VendorStockContract.AllowedFor and the save/ever-built ledgers. The
        // game is LIVE, so an id never moves. Only the `Label` (the word the player reads on
        // the interact prompt, via BuildingInteractable.Configure) is allowed to change, and
        // it is now taken from the CATALOG rather than typed here (WO-1161).
        //
        // The catalog is the single naming authority: StructureRoles.By[role].DisplayName
        // reads the word off the row that claims the role, so a creative rename reaches the
        // NPC prompt with no code change. `neutral` is reached ONLY when the catalog has not
        // loaded and is deliberately generic — never a rival proper noun.
        private static string RoleWord(string role, string neutral)
            => StructureRoles.By[role].DisplayName ?? neutral;

        private static Vendor VendorFor(string role)
        {
            switch (role.ToLowerInvariant())
            {
                case "blacksmith":
                    // WO-444 (owner 2026-06-13): the BLACKSMITH sells ARMOR, the FORGE sells weapons.
                    // StructureId drives VendorStockContract.AllowedFor — "armorer" => Armor (was "forge"
                    // => Weapon, which made the blacksmith wrongly sell weapons). "armorer" is a recognized
                    // vendor context (AutoPilotDriver storefront set); missing portrait/def degrades gracefully.
                    return new Vendor { BodyRes = BodySmith,    StructureId = "armorer",      Label = RoleWord(StructureRole.Armorer, "Armorer"), Arch = TownsfolkDialogue.Archetype.Blacksmith };
                case "lumbermill":
                    // StructureId "lumbermill" is the DIALOGUE key and stays; the word the
                    // player reads is the catalog's ("Lumber Mill", off collector_lumbermill).
                    return new Vendor { BodyRes = BodyPeasantB, StructureId = "lumbermill",   Label = RoleWord(StructureRole.WoodProducer, "Lumber Mill"), Arch = TownsfolkDialogue.Archetype.Villager };
                case "windmill":
                    // StructureId "farm" is the DIALOGUE key and stays - a live key, not a word.
                    // The catalog settles the word, and as of WO-1416 (owner 2026-09-05 "quarry
                    // pays stone" / "the farm was retired nothiong uses food") that word is
                    // "Quarry". This named StructureRole.FoodProducer, a role NO row claims, so
                    // the null fallback printed the retired proper noun "Farm" on the NPC label.
                    return new Vendor { BodyRes = BodyPeasantA, StructureId = "farm",         Label = RoleWord(StructureRole.StoneProducer, "Quarry"), Arch = TownsfolkDialogue.Archetype.Villager };
                case "echohollow":
                    // No catalog row claims a role for the Echo Hollow yet — word stays local.
                    return new Vendor { BodyRes = BodyPeasantA, StructureId = "pet-house",    Label = "Echo Hollow", Arch = TownsfolkDialogue.Archetype.Villager };
                case "forge":
                    return new Vendor { BodyRes = BodySmith,    StructureId = "forge",        Label = RoleWord(StructureRole.Weaponsmith, "Forge"), Arch = TownsfolkDialogue.Archetype.Blacksmith };
                case "arcanetower":
                    // No catalog row claims a role for the Arcane Tower yet — word stays local.
                    return new Vendor { BodyRes = BodyPeasantB, StructureId = "arcane-tower", Label = DeNelle.Core.Catalog.CatalogRegistry.Get("arcane-tower")?.displayName ?? "Cathedral of Learning", Arch = TownsfolkDialogue.Archetype.Elder };
                case "jeweler":
                    return new Vendor { BodyRes = BodyMerchant, StructureId = "jeweler",      Label = RoleWord(StructureRole.Jeweler, "Jeweler"),   Arch = TownsfolkDialogue.Archetype.Quartermaster };
                case "marketplace":
                    // StructureId "market" is the DIALOGUE key and stays. The label used to
                    // read "Marketplace"; the catalog row calls it "Store".
                    return new Vendor { BodyRes = BodyMerchant, StructureId = "market",       Label = RoleWord(StructureRole.Marketplace, "Store"), Arch = TownsfolkDialogue.Archetype.Quartermaster };
                case "apothecary":
                    // Owner F8 2026-07-02 ("should have a NPC"): the Apothecary is a RUNTIME
                    // station (CraftingStationInjector) with no baked marker, so it's spawned by
                    // the anchor pass (AnchorRoles). structureId "apothecary" has
                    // an authored conversation (dialogues.json) ending in OpenAlchemy — the same
                    // ConsumableCrafting panel the station's own interact opens.
                    return new Vendor { BodyRes = BodyPeasantA, StructureId = "apothecary",   Label = "Apothecary", Arch = TownsfolkDialogue.Archetype.Villager };
                case "jewelersbench":
                    // Owner 2026-07-03 ("every building in town needs an NPC as the speaker; jeweler
                    // lacks that"): the Jeweler's Bench is a RUNTIME station (JewelerStationInjector,
                    // BuildingType.JewelersBench, id "jewelers-bench") with no baked marker, so it's
                    // spawned by the anchor pass like the apothecary. structureId MUST match the
                    // station's Building id ("jewelers-bench") so MarkNpcCovered defers the bench's own
                    // prompt to this NPC, and so PlayStructure finds the authored "jewelers-bench"
                    // conversation (Sable) that ends in OpenJeweler -> the SAME JewelerCrafting panel
                    // the station's BuildingInteractable opens. Mirrors the Apothecary/Herbalist wiring.
                    return new Vendor { BodyRes = BodyMerchant, StructureId = "jewelers-bench", Label = RoleWord(StructureRole.Jeweler, "Jeweler"), Arch = TownsfolkDialogue.Archetype.Quartermaster };
            }
            // Unknown role -> generic merchant talking to the market. Never silently skip,
            // so a future storefront still gets a working NPC.
            return new Vendor { BodyRes = BodyMerchant, StructureId = "market", Label = role, Arch = TownsfolkDialogue.Archetype.Quartermaster };
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            // ── CANDIDATE-CAUSE #1 PROBE (owner device 2026-08-21 "no npc for armor") ──
            // "Did the injector run AT ALL, and in which scene?" was previously unanswerable from a
            // capture: Bootstrap/Awake were silent, and a NON-hub scene produced literally zero lines,
            // so "injector never ran" and "injector ran and found nothing" read identically. Name the
            // bootstrap + the active scene unconditionally so one run separates them.
            string boot = SceneManager.GetActiveScene().name;
            if (Instance != null)
            {
                FlowTrace.Step("Vendor",
                    $"Bootstrap skipped — injector already up (active scene '{boot}').");
                return;
            }
            FlowTrace.Step("Vendor",
                $"Bootstrap creating CastleVendorNpcInjector (AfterSceneLoad, active scene '{boot}', " +
                $"isCastleHub={IsCastleHubScene(boot)}).");
            new GameObject("CastleVendorNpcInjector").AddComponent<CastleVendorNpcInjector>();
        }

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            DontDestroyOnLoad(gameObject);

            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
            string active = SceneManager.GetActiveScene().name;
            bool hub = IsCastleHubScene(active);
            // CANDIDATE #1: injector alive but the scene gate closed. Stated PLAINLY — a capture that
            // shows this Warn and no Inject line proves the injector ran in the WRONG scene, which is a
            // different bug from "ran in the right scene and found no armorer".
            if (hub)
            {
                FlowTrace.Step("Vendor",
                    $"Awake: injector up in castle-hub scene '{active}' — injecting now.");
                Inject();
            }
            else
            {
                FlowTrace.Warn("Vendor",
                    $"Awake: injector up but active scene '{active}' is NOT a castle-hub scene " +
                    $"(expected '{TargetScene}' or '{MergedTargetScene}') — NO vendor injection this Awake. " +
                    "The sceneLoaded hook is armed; if the hub never logs an Inject line after this, the hub " +
                    "scene name drifted and IsCastleHubScene must be widened.");
            }

            // Drain any placement that fired before Instance was set (timing race). AFTER Inject so the
            // fresh HolderName exists — draining first would parent vendors to a holder Inject then nukes.
            DrainPendingPlacements();
        }

        /// <summary>Seat any vendor NPCs whose NotifyBuildingPlaced arrived before this injector's
        /// Awake set <see cref="Instance"/> (the AfterSceneLoad/BaseLayoutLoader replay race). Idempotent:
        /// SpawnVendorForPlaced is scene-gated + per-building idempotent (VendorSeatMarker).</summary>
        private void DrainPendingPlacements()
        {
            if (s_pendingPlacements.Count == 0) return;
            var queued = s_pendingPlacements.ToArray();
            s_pendingPlacements.Clear();
            FlowTrace.Step("NpcSeat", $"draining {queued.Length} deferred placement(s) now that the injector is up.");
            foreach (var (id, tf) in queued)
            {
                if (tf == null)
                {
                    FlowTrace.Warn("NpcSeat",
                        $"deferred placement '{id}' dropped — building transform was destroyed before the injector booted.");
                    continue;
                }
                SpawnVendorForPlaced(id, tf);
            }
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
            SceneManager.sceneLoaded -= OnSceneLoaded;
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            // CANDIDATE #1 (cont.): log EVERY scene load the injector observes, hub or not. A capture
            // that lists only non-hub loads proves the hub scene never arrived under a name this gate
            // recognises — instead of the old total silence, which looked the same as "never ran".
            if (IsCastleHubScene(scene.name))
            {
                FlowTrace.Step("Vendor", $"sceneLoaded '{scene.name}' ({mode}) IS a castle hub — injecting.");
                Inject();
            }
            else
            {
                FlowTrace.Once("Vendor", $"nonhub-scene-{scene.name}",
                    $"sceneLoaded '{scene.name}' ({mode}) is NOT a castle-hub scene — no vendor injection here (by design).");
            }
        }

        // Holder so re-injection (idempotent) is trivial: clear the prior holder, respawn.
        private const string HolderName = "CastleVendorNPCs (runtime)";

        // The body SpawnVendor/SpawnPlaceholder most recently produced — read by the
        // placement hook to tag the building's VendorSeatMarker (per-building idempotency).
        private GameObject _lastSpawnedVendor;

        private void Inject()
        {
            // Idempotent: nuke any prior runtime holder so a re-load doesn't double-spawn,
            // and stop any in-flight anchor poll from the previous load so a stale
            // coroutine can never race the fresh pass below.
            var prior = GameObject.Find(HolderName);
            if (prior != null) Destroy(prior);
            StopCoroutine(nameof(AnchorVendorsToPlacedBuildings));

            new GameObject(HolderName);

            // WO-673 L4 (always on — WO-682 removed ff.strategicplacement): the baked
            // storefronts (and their NPC_<Role>_Interactable markers) are stood down —
            // buildings exist only where the PLAYER placed them (plus the row-less
            // injector stations). Vendors anchor to the live Building COLLECTION by id
            // (One Model: readers query the collection, never a baked name). The old
            // marker loop + the apothecary/jeweler deferred passes were deleted with the
            // flag — AnchorVendorsToPlacedBuildings covers every role, stations included.
            // CANDIDATE #4 PROBE (ordering) + the ROLE-COLLISION fact. Name the scene, and dump the
            // anchor table AS THE INJECTOR SEES IT — including the fact that a ROLE can carry MORE THAN
            // ONE anchor id (Blacksmith is listed twice: "armorer" AND "forge"). `pending` is keyed by
            // ROLE, so the FIRST anchor to resolve SETTLES the role and every sibling anchor is
            // abandoned. That is a live candidate for "no npc for armor": if the "forge" building seats
            // Blacksmith first, the "armorer" anchor is never consulted again and the armorer storefront
            // stays speaker-less with no failure anywhere. Log it up front so the read is one line.
            var byRole = new System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<string>>();
            foreach (var a in AnchorRoles)
            {
                if (!byRole.TryGetValue(a.Role, out var ids)) { ids = new System.Collections.Generic.List<string>(); byRole[a.Role] = ids; }
                ids.Add(a.BuildingId);
            }
            var tableSb = new System.Text.StringBuilder();
            foreach (var kv in byRole)
            {
                if (tableSb.Length > 0) tableSb.Append("; ");
                tableSb.Append(kv.Key).Append("->[").Append(string.Join(",", kv.Value)).Append(']');
                if (kv.Value.Count > 1) tableSb.Append("(MULTI-ANCHOR: first to resolve settles the role)");
            }
            FlowTrace.Step("Vendor",
                $"Inject in scene '{SceneManager.GetActiveScene().name}': anchoring vendors to the Building " +
                $"collection (strategic placement always on). {byRole.Count} distinct role(s), " +
                $"{AnchorRoles.Length} anchor row(s): {tableSb}");
            StartCoroutine(nameof(AnchorVendorsToPlacedBuildings));
        }

        // (WO-682: the flag-off marker loop + the apothecary/jeweler deferred passes were
        // deleted with ff.strategicplacement — AnchorVendorsToPlacedBuildings below is the
        // ONE vendor-spawn path; its AnchorRoles table covers the stations too.)

        // ── WO-673 L4 (always on — WO-682) — vendor anchoring by Building collection ──
        // Role (VendorFor key) -> the Building.BuildingId that anchors it. Under strategic
        // placement the baked storefronts are stood down, so each vendor waits for a LIVE
        // Building carrying its id — placed by the player (StructureFactory "GameplayBuilding"
        // sets BuildingId == catalog id), replayed from a migrated save, or (for the two
        // runtime stations) injected by their station injector. Ids verified against
        // structures-catalog.json (:483-773) + CraftingStationInjector/JewelerStationInjector
        // StationId constants.
        private static readonly (string Role, string BuildingId)[] AnchorRoles =
        {
            // 2026-08-23 (WO-1161): this line used to read "no placed armorer catalog row yet
            // (L1) - awaits one", an INSTRUCTION to go author something that already exists.
            // The catalog carries a row id "armorer", displayName "Armorer", role `armorer`
            // (the ARMOUR vendor). Nothing is awaited; this anchor resolves.
            ("Blacksmith",    "armorer"),
            ("Lumbermill",    "collector_lumbermill"), // WO-707: Sawmill retires from the palette — anchor to the surviving Lumbermill tile; dialogue structureId stays "lumbermill" (VendorFor)
            ("Windmill",      "collector_farm"),       // WO-707: Mill retires from the palette — anchor to the Farm tile; dialogue structureId stays "farm" (VendorFor)
            ("EchoHollow",    "pet-house"),
            // Owner 2026-09-13 confirmed distinct authored trade and production hosts.
            // Weaponsmith owns the weapons vendor; IronMine only owns iron collection.
            ("Forge",         "forge"),
            ("ArcaneTower",   "arcane-tower"),
            ("Jeweler",       "jeweler"),
            ("Marketplace",   "market"),
            ("Apothecary",    "apothecary"),     // runtime station (CraftingStationInjector.StationId)
            ("JewelersBench", "jewelers-bench"), // runtime station (JewelerStationInjector.StationId)
        };

        // Generalized deferred pass (the apothecary/jeweler pattern above, for EVERY role):
        // poll the live Building collection on a slow tick; when a role's building exists,
        // spawn its vendor through the SAME SpawnVendor path via a synthetic marker child at
        // the baked front offset (local (0,0,6)), so placement/interaction/sign composition
        // is identical to the baked storefronts. A role whose building doesn't exist yet
        // simply isn't spawned — the poll keeps watching (no timeout: placement is a
        // player-paced event) so the vendor appears the moment the building does.
        private IEnumerator AnchorVendorsToPlacedBuildings()
        {
            const float PollSeconds = 2f;   // slow tick — placement happens on player time
            var pending = new System.Collections.Generic.HashSet<string>();
            foreach (var a in AnchorRoles) pending.Add(a.Role);
            int pass = 0;   // F8 2026-07-30: pass 0 runs BEFORE BaseLayoutLoader's replay — see below

            while (pending.Count > 0)
            {
                if (!IsCastleHubScene(SceneManager.GetActiveScene().name)) yield break; // scene moved on
                var holder = GameObject.Find(HolderName);
                if (holder == null) yield break;   // injector re-ran / tearing down

                var live = FindObjectsByType<Building>();
                // COLLECTOR VISION (owner F8 2026-07-24 "Lumbermill/Farm have no NPC"): the
                // Lumbermill/Farm/Forge are placed as ResourceCollector components (StructureFactory
                // "ResourceCollector" case, StructureFactory.cs:744 — NO Building component), so the
                // Building scan above is STRUCTURALLY BLIND to them and their roles (Lumbermill/Windmill/
                // Forge) awaited a building that never arrives. Enumerate the live collectors too and
                // match a "collector_*" anchor by the collector's BARE BuildingId (repo.collectorBuildingId
                // in structures-catalog.json: collector_lumbermill->"lumbermill", collector_farm->"farm",
                // collector_forge->"forge").
                var liveCollectors = FindObjectsByType<DeNelle.Village.Buildings.Progression.ResourceCollector>();

                // ── CANDIDATE #3 + #4 PROBE: the WORLD CENSUS, per pass ───────────────────────
                // The old poll never said what it actually SAW. Its only skip line was
                // Once("await-<id>") — which fires on PASS 0, before BaseLayoutLoader replays the
                // save, and then NEVER AGAIN. So a capture read "armorer awaiting building" forever
                // even after the town had fully populated, and "the player never built an armorer"
                // was indistinguishable from "the injector looked before anything existed".
                // Emit the ids the injector can actually see, and state the ordering fact PLAINLY
                // when the world is still empty — never report an empty scan as if it were the world.
                bool censusPass = pass <= 2 || (pass % 15) == 0;
                if (censusPass)
                {
                    var ids = new System.Collections.Generic.List<string>();
                    foreach (var b in live) if (b != null && b.IsAlive) ids.Add($"B:{b.BuildingId}");
                    foreach (var c in liveCollectors) if (c != null && c.IsAlive) ids.Add($"C:{c.BuildingId}");
                    string idList = ids.Count == 0 ? "<none>" : string.Join(",", ids);
                    if (live.Length == 0 && liveCollectors.Length == 0)
                    {
                        // THE ORDERING FACT, said out loud. Pass 0 runs synchronously inside
                        // OnSceneLoaded — BEFORE BaseLayoutLoader.Start replays the placed buildings.
                        // An empty scan here is NOT evidence about what the player built.
                        FlowTrace.Warn("Vendor",
                            $"poll pass {pass}: scanned ZERO Buildings and ZERO ResourceCollectors. " +
                            (pass == 0
                                ? "This is pass 0, which runs INSIDE OnSceneLoaded, BEFORE BaseLayoutLoader replays the " +
                                  "saved structures — so this proves NOTHING about what the player has built. Read the " +
                                  "pass-1+ census below for the real world state."
                                : "The world is genuinely empty at this pass (replay should have finished by now) — " +
                                  "either the save has no structures or the replay failed upstream of this injector.") +
                            $" Still pending role(s): {string.Join(",", pending)}");
                    }
                    else
                    {
                        FlowTrace.Step("Vendor",
                            $"poll pass {pass} census: {live.Length} Building(s) + {liveCollectors.Length} " +
                            $"ResourceCollector(s) live -> ids [{idList}]. Pending role(s): {string.Join(",", pending)}");
                    }
                }

                foreach (var (role, buildingId) in AnchorRoles)
                {
                    if (!pending.Contains(role))
                    {
                        // ROLE-COLLISION PROBE (candidate #3/#5): this anchor id is being SKIPPED not
                        // because its building is missing, but because a SIBLING anchor already settled
                        // the role. For "armorer" that is the whole bug shape — say which id won.
                        FlowTrace.Once("Vendor", $"role-settled-skip-{buildingId}",
                            $"anchor '{buildingId}' (role {role}) SKIPPED — the role was already settled by a " +
                            "sibling anchor row, so this building will never be examined again this poll. " +
                            "If this id is the one missing its NPC, the multi-anchor role table is the cause, " +
                            "not a missing building or a missing body.");
                        continue;
                    }

                    // GUARDED per-role seat (§12): every lookup below — the live-Building scan, the
                    // collector scan, ResolveBakedOrStationAnchor's inactive-name walk, the spawn —
                    // can throw on ONE malformed vendor. Un-guarded, that exception unwound the whole
                    // coroutine and silently killed the seat pass for EVERY REMAINING ROLE, which
                    // looks exactly like "the armorer just never got an NPC". Guard.Try logs the
                    // failure via FlowTrace.Fail and lets the next role proceed.
                    Guard.Try("Vendor", $"anchor vendor role '{role}' -> building id '{buildingId}' (pass {pass})", () =>
                    {
                    // Already placed (a prior pass / re-load survivor)? Settle the role.
                    if (GameObject.Find($"CastleVendor_{role}") != null ||
                        GameObject.Find($"CastleVendor_{role}_Placeholder") != null)
                    {
                        pending.Remove(role);
                        FlowTrace.Once("Vendor", $"already-seated-{role}",
                            $"{role} already has a live vendor body in the scene — role settled without a new spawn " +
                            $"(anchor row '{buildingId}' not consulted).");
                        return;
                    }

                    Transform anchorTf = null;
                    foreach (var b in live)
                        if (b != null && b.IsAlive && b.BuildingId == buildingId) { anchorTf = b.transform; break; }

                    // Collector branch: a "collector_*" anchor whose Building scan missed — look for a
                    // live ResourceCollector carrying the matching BARE id (the poll's structural blind
                    // spot that left the Lumbermill/Farm/Forge NPC-less).
                    if (anchorTf == null && buildingId.StartsWith("collector_"))
                    {
                        string bareId = buildingId.Substring("collector_".Length);
                        foreach (var c in liveCollectors)
                            if (c != null && c.IsAlive && c.BuildingId == bareId)
                            {
                                // ORIGIN GUARD (F8 2026-07-30 "vendors stacked at the Heart"):
                                // ResourceCollectorBootstrap.EnsureFallbackCollector creates LOGICAL
                                // economy collectors 'Collector_<id>' under the DDOL
                                // 'ResourceCollectorHost' GameObject, which is never positioned —
                                // world (0,0,0). Those are accounting hosts, not buildings: anchoring
                                // to one seated the Lumbermill/Windmill vendors at the Heart
                                // (captured: "anchored to 'Collector_lumbermill' ... @ (0.00, 0.00,
                                // 0.00)"). A real placed/replayed collector root carries
                                // PlacedStructure (BaseLayoutLoader.Spawn); the logical host does not.
                                if (c.GetComponentInParent<PlacedStructure>() == null)
                                {
                                    FlowTrace.Once("Vendor", $"skip-logical-{buildingId}",
                                        $"{role}: ignoring LOGICAL collector '{c.name}' @ {c.transform.position} " +
                                        "(no PlacedStructure — economy fallback host at world origin); awaiting the placed collector.");
                                    continue;
                                }
                                anchorTf = c.transform;
                                FlowTrace.Once("Vendor", $"collector-match-{buildingId}",
                                    $"{role}: matched live ResourceCollector (bare id '{bareId}') for anchor " +
                                    $"'{buildingId}' — the Building scan is blind to collectors, so this is the seat path.");
                                break;
                            }
                    }

                    // LEVER 1 FALLBACK (owner 2026-07-24, WWCD "stores pre-stand on a fresh
                    // hub"): no live/replayed Building for this role — anchor the vendor to the
                    // BAKED storefront (or the runtime station's anchor) so every trade gets its
                    // NPC on a FRESH hub WITHOUT the player having to place it. On a fresh save
                    // the baked ring is stood down (SetActive false) + the stations skipped, so
                    // nothing replayed and the old poll waited forever (the captured "awaiting
                    // building — vendor not spawned" for every role). Safe: this only fires when
                    // NO live building carries the id, so it can never double-seat a placed one.
                    bool anchorIsTemp = false;
                    // F8 2026-07-30 "duplicated NPCs": this coroutine's FIRST pass runs
                    // synchronously inside OnSceneLoaded — BEFORE BaseLayoutLoader.Start
                    // replays the placed storefronts. On the first hub reload after the
                    // WO-673 migration, pass 0 found no live Building for ANY role, took
                    // this baked fallback for all of them, and the replay then seated a
                    // SECOND per-role vendor via NotifyBuildingPlaced (captured: 10 poll
                    // spawns at sceneLoaded + 8 "vendor NPC spawned for placed" right after;
                    // 'CastleVendor_Forge' Talk-registered count=1->2 — the two identical
                    // smiths + doubled weapon signs in the owner's screenshot). The old
                    // "can never double-seat a placed one" claim was FALSE on pass 0: the
                    // placed building did not EXIST yet. Defer the fallback to pass 1 (one
                    // 2s tick): replayed buildings now win the seat, and the settle check
                    // above retires those roles; fallback-only roles appear 2s later.
                    if (anchorTf == null && pass > 0)
                    {
                        var fb = ResolveBakedOrStationAnchor(role, buildingId);
                        anchorTf = fb.tf;
                        anchorIsTemp = fb.temp;
                    }

                    if (anchorTf == null)
                    {
                        // Skip decision. The key now carries a PASS BUCKET so the line re-reports as the
                        // world changes instead of firing once on pass 0 and going silent forever (the
                        // old Once("await-<id>") — the reason a capture could not distinguish "not built"
                        // from "looked too early"). Buckets: 0 (pre-replay), 1 (post-replay), then decade.
                        int bucket = pass == 0 ? 0 : (pass < 10 ? 1 : pass / 10 * 10);
                        FlowTrace.Once("Vendor", $"await-{buildingId}-p{bucket}",
                            $"{role} awaiting building/collector '{buildingId}' at poll pass {pass} — no live " +
                            $"Building, no live ResourceCollector, and no baked/station anchor in the scene; vendor not spawned. " +
                            (pass == 0
                                ? "PASS 0 IS PRE-REPLAY — this is an ordering observation, NOT evidence the player lacks this building."
                                : "Post-replay: the player genuinely has no '" + buildingId + "' placed, OR the Lever-1 baked/station " +
                                  "fallback was withheld by the blank-town gate (look for the blank-gate line above)."));
                        return;
                    }

                    // Capture BEFORE spawning: the synthetic marker (and a temp anchor) are
                    // destroyed right after SpawnVendor, so read the trace inputs first.
                    string anchorLabel = anchorIsTemp ? $"fallback anchor '{anchorTf.name}'" : $"'{anchorTf.name}'";
                    Vector3 anchorPos = anchorTf.position;

                    // Synthetic marker at the baked marker's front offset (local (0,0,6)) —
                    // SpawnVendor derives the front DISTANCE from it and redirects the vendor
                    // to the building's Heart-facing side, exactly like the baked markers and
                    // the station deferred passes above.
                    var marker = new GameObject($"NPC_{role}_Marker (runtime)");
                    marker.transform.SetParent(anchorTf, false);
                    marker.transform.localPosition = new Vector3(0f, 0f, 6f);

                    bool ok = SpawnVendor(marker.transform, role, ResolveHero(), holder.transform, buildingId);
                    Destroy(marker);
                    if (anchorIsTemp) Destroy(anchorTf.gameObject);   // temp anchor served its purpose
                    if (ok)
                    {
                        pending.Remove(role);
                        // Name the anchors this settle ABANDONS. A role with >1 anchor row keeps only the
                        // winner; every sibling id silently stops being watched from here on. Making the
                        // abandonment explicit is what turns "the armorer has no NPC" from a mystery into
                        // a one-line read.
                        var abandoned = new System.Collections.Generic.List<string>();
                        foreach (var a in AnchorRoles)
                            if (a.Role == role && a.BuildingId != buildingId) abandoned.Add(a.BuildingId);
                        FlowTrace.Step("Vendor",
                            $"{role} anchored to {anchorLabel} for '{buildingId}' @ {anchorPos} — role SETTLED." +
                            (abandoned.Count > 0
                                ? $" ABANDONING sibling anchor(s) [{string.Join(",", abandoned)}] for this role: those " +
                                  "buildings will get NO poll-seated NPC (only the placement hook can still seat them)."
                                : string.Empty));
                    }
                    else
                    {
                        // SpawnVendor self-reported the failure — keep the role pending so the
                        // next tick retries (e.g. Resources not yet loaded).
                        FlowTrace.Fail("Vendor",
                            $"{role} spawn FAILED at {anchorLabel} for '{buildingId}' — will retry next poll.");
                    }
                    });   // Guard.Try — a throw here is logged and the NEXT role still gets its turn
                }

                if (pending.Count == 0) break;
                pass++;
                yield return new WaitForSecondsRealtime(PollSeconds);
            }
            FlowTrace.Step("Vendor",
                $"vendor anchor poll complete after {pass} pass(es) — every ROLE settled. NOTE: 'every role' " +
                "is not 'every building': a role with multiple anchor rows settled on ONE of them (see the " +
                "ABANDONING lines above), and the poll now stops watching entirely.");
        }

        // ── LEVER 1 baked/station anchor resolver (owner 2026-07-24, WWCD) ─────────────
        // Resolve the anchor for a role whose live Building/collector does not exist yet, so
        // a FRESH hub still seats every trade's speaker. Returns the transform to seat at and
        // whether it is a TEMP anchor the caller must destroy after spawning:
        //   • runtime station role (apothecary/jewelers-bench) -> the live station holder if
        //     present, else a temp anchor at the station's census fallbackPos (WO-703 stands
        //     the station injector down on a fresh save; the speaker still seats). Closes gap #2c.
        //   • baked storefront role -> the baked GameObject named "<Role>_..." (census:
        //     StrategicPlacementMigration.BakedStorefronts). Matched by the ROLE TOKEN, so the
        //     Blacksmith role resolves to Blacksmith_Weapons_Storefront even though its
        //     migration itemId is 'workshop' (closes gap #2a — its OWN vendor, not a Forge one).
        //     Re-surfaced (made visible) so the store pre-stands instead of the NPC floating.
        //   • Jeweler (removed from the baked ring + no station) -> a temp anchor at a commerce
        //     fallback beside the Marketplace so the trade still gets an NPC without a placed/
        //     replayed jeweler (closes gap #2b). OWNER-TUNABLE position.
        private (Transform tf, bool temp) ResolveBakedOrStationAnchor(string role, string buildingId)
        {
            // WO-834 blank-town gate: the Lever-1 fallback below RESURFACES the baked
            // storefront (ResurfaceStorefront) and seats a vendor at it — on a
            // Build-Your-Own (migrated, never-built) save that would refurnish the blank
            // town every poll pass. Gate the WHOLE fallback (stations + baked stores +
            // the jeweler temp anchor): vendors come online as buildings are placed
            // (NotifyBuildingPlaced — the WO-707 ruling). Default-Town/legacy saves carry
            // the template grant, so their pre-stand staffing is unchanged.
            if (!StructureSingleton.MayBakedTwinSurface(buildingId))
            {
                FlowTrace.Once("Vendor", $"blank-gate-{buildingId}",
                    $"{role}: Lever-1 baked/station fallback withheld for '{buildingId}' - never player-built " +
                    "on this save (blank-town gate, WO-834); vendor seats when the building is placed.");
                return (null, false);
            }

            // Runtime crafting stations first (matched by catalog id).
            foreach (var (holderName, itemId, fallbackPos) in StrategicPlacementMigration.StationAnchors())
            {
                if (!string.Equals(itemId, buildingId, System.StringComparison.OrdinalIgnoreCase)) continue;
                var holder = FindByNameInclInactive(holderName);
                if (holder != null) return (holder, false);
                var anchor = new GameObject($"CastleVendorAnchor_{role}");
                anchor.transform.position = fallbackPos;
                return (anchor.transform, true);
            }

            // Baked storefronts (matched by ROLE token — CastleHubBuilder names them "<Role>_...").
            foreach (var (bakedName, itemId) in StrategicPlacementMigration.BakedStorefronts())
            {
                var baked = FindByNameInclInactive(bakedName);
                if (baked == null) continue;                                // not in this scene bake
                // Authored names describe the owner's art. Explicit gameplay identity selects
                // the speaker; the old Blacksmith/Forge name tokens describe opposite trades.
                var authored = baked.GetComponent<AuthoredCastleStorefront>();
                if (authored != null)
                {
                    string tradeId = authored.CanonicalId;
                    if (tradeId == "collector_farm") tradeId = "farm";
                    if (tradeId == "collector_lumbermill") tradeId = "lumbermill";
                    if (!string.Equals(tradeId, VendorFor(role).StructureId, System.StringComparison.OrdinalIgnoreCase)) continue;
                }
                else if (!FirstTokenEquals(bakedName, role)) continue;
                HubStructureVisualInjector.ResurfaceStorefront(bakedName);  // pre-stand: make the store visible
                return (baked, false);
            }

            // Jeweler: no baked object (removed from the ring) + no station. Seat its speaker at a
            // commerce-cluster fallback (beside the Marketplace if present) so the trade still gets
            // an NPC. OWNER-TUNABLE — flagged in the work-order report.
            if (string.Equals(role, "Jeweler", System.StringComparison.OrdinalIgnoreCase))
            {
                Vector3 pos = new Vector3(12f, 0f, 32f);   // beside Marketplace_Monetization (0,0,32)
                var market = FindByNameInclInactive("Marketplace_Monetization");
                if (market != null) pos = market.position + market.right * 8f;
                var anchor = new GameObject($"CastleVendorAnchor_{role}");
                anchor.transform.position = pos;
                return (anchor.transform, true);
            }

            return (null, false);
        }

        // Baked names encode the role as their first "_"-delimited token (CastleHubBuilder:
        // "Blacksmith_Weapons_Storefront" -> "Blacksmith"). Same convention CastleHubBuilder
        // uses to name the NPC_<Role>_Interactable marker.
        private static bool FirstTokenEquals(string bakedName, string role)
        {
            if (string.IsNullOrEmpty(bakedName) || string.IsNullOrEmpty(role)) return false;
            int us = bakedName.IndexOf('_');
            string token = us > 0 ? bakedName.Substring(0, us) : bakedName;
            return string.Equals(token, role, System.StringComparison.OrdinalIgnoreCase);
        }

        // Name match across the loaded scene(s), INCLUDING inactive — the baked ring is
        // SetActive(false) under standdown, so the active-only lookups can't see the anchors.
        private static Transform FindByNameInclInactive(string name)
        {
            return AuthoredCastleStorefront.Find(name, true);
        }

        /// <summary>The DISTINCT vendor roles the hub must seat an NPC for (every action
        /// storefront/collector/station). The AutoPilot coverage oracle (AssertVendorCoverage)
        /// walks THIS — the injector's own role map, not a hardcoded test list.</summary>
        public static System.Collections.Generic.IReadOnlyList<string> VendorRoles()
        {
            var seen = new System.Collections.Generic.List<string>();
            foreach (var a in AnchorRoles)
                if (!seen.Contains(a.Role)) seen.Add(a.Role);
            return seen;
        }

        // ── PLACEMENT HOOK (owner device felt-test 2026-07-16, SHOW-STOPPER) ──────────
        // A storefront the player PLACES in build mode must get its vendor NPC RIGHT NOW,
        // not up to 2s later (the poll tick) and not never (the AnchorRoles poll misses the
        // WO-707 palette ids workshop/lumberyard/foundry/silo/collector_*, and settles each
        // role ONCE). BuildModeController.Place calls this the instant a placement commits;
        // it reuses the SAME SpawnVendor path the scene-load poll uses — NO parallel NPC
        // system, one vendor per trade.
        /// <summary>Spawn the vendor NPC for a just-placed building, immediately. Idempotent
        /// (skips if that role's vendor already exists — the poll or an earlier placement may
        /// have beaten us). No-op until the injector's Awake ran (Instance null) and outside a
        /// castle-hub scene. Reuses <see cref="SpawnVendor"/>, so a missing prefab still logs
        /// + falls back to a placeholder rather than silently leaving the storefront vendorless.</summary>
        public static void NotifyBuildingPlaced(string buildingId, Transform buildingTransform)
        {
            if (buildingTransform == null || string.IsNullOrEmpty(buildingId))
            {
                // Genuinely un-actionable input (no transform / no id) — Fail-loud (lands in the
                // errors-only break-log) rather than vanishing. This is NOT the timing race below.
                FlowTrace.Fail("NpcSeat",
                    $"NotifyBuildingPlaced NO-OP: hasTransform={(buildingTransform != null)}, " +
                    $"id='{buildingId ?? "<null>"}' — cannot spawn/queue a vendor NPC.");
                return;
            }
            if (Instance == null)
            {
                // TIMING RACE (AfterSceneLoad Bootstrap ordering vs BaseLayoutLoader replay / a fast
                // placement): the notify fired before Awake set Instance. DO NOT DROP it (the old
                // silent-then-Fail no-op that left reloaded collectors NPC-less) — ENQUEUE so Awake's
                // DrainPendingPlacements seats it the moment the injector boots. Warn (captured) so a
                // capture shows the defer, not a vanish.
                s_pendingPlacements.Add((buildingId, buildingTransform));
                FlowTrace.Warn("NpcSeat",
                    $"NotifyBuildingPlaced DEFERRED: injector not up yet (Instance null) — queued '{buildingId}' " +
                    $"(pending={s_pendingPlacements.Count}) to seat in Awake.");
                return;
            }
            Instance.SpawnVendorForPlaced(buildingId, buildingTransform);
        }

        private void SpawnVendorForPlaced(string buildingId, Transform buildingTransform)
        {
            using var _ = FlowTrace.Enter("NpcSeat", $"CastleVendorNpcInjector.SpawnVendorForPlaced id='{buildingId}'");
            string activeScene = SceneManager.GetActiveScene().name;
            if (!IsCastleHubScene(activeScene))
            {
                // Build mode only runs in a buildable (castle-hub) scene, so a placement whose ACTIVE
                // scene isn't a hub is anomalous — Warn (lands in the errors-only break-log) naming the
                // scene, so a scene-name drift (e.g. OuterWorld active instead of Main_Castle_Overworld)
                // is captured instead of silently dropping every placed vendor.
                FlowTrace.Warn("NpcSeat",
                    $"placed '{buildingId}' — active scene '{activeScene}' is NOT a castle-hub scene " +
                    $"(expected '{TargetScene}' or '{MergedTargetScene}') — no vendor spawned. If the owner IS in the " +
                    "hub, the hub scene name drifted; add it to IsCastleHubScene.");
                return;
            }
            string role = RoleForBuildingId(buildingId);
            if (string.IsNullOrEmpty(role))
            {
                // Tower / wall / gate / mine / fountain / decoration are storefront-less BY DESIGN
                // (quiet Step). But an UNRECOGNIZED placeable id (a real building the mapping forgot)
                // is the "no NPC" bug class — Warn it (captured) so the missing id names itself.
                if (IsKnownNonStorefront(buildingId))
                    FlowTrace.Step("NpcSeat", $"placed '{buildingId}' is a non-storefront (tower/wall/gate/deco) — no NPC by design.");
                else
                    FlowTrace.Warn("NpcSeat",
                        $"placed '{buildingId}' maps to NO vendor role but is NOT a known non-storefront — " +
                        "UNMAPPED building id, so it gets no NPC. Add it to RoleForBuildingId/AnchorRoles.");
                return;
            }
            // PER-BUILDING idempotency (owner "every building in town needs an NPC as the speaker"):
            // the old check was per-ROLE-GLOBAL — it no-op'd whenever that trade already had a vendor
            // ANYWHERE (market/silo/foundry/lumberyard all map to "Marketplace"; forge/workshop ->
            // "Forge"), so a freshly-placed second building of a shared trade got NO NPC. Now we only
            // skip if THIS building already carries a live vendor (double-notify guard), so every
            // placed building gets its own speaker while a re-notify of the same building can't stack one.
            var seated = buildingTransform.GetComponent<VendorSeatMarker>();
            if (seated != null && seated.Vendor != null)
            {
                FlowTrace.Step("NpcSeat",
                    $"building '{buildingId}' already has its vendor ('{seated.Vendor.name}') — placement hook no-op.");
                return;
            }

            var holder = GameObject.Find(HolderName);
            if (holder == null) holder = new GameObject(HolderName);   // poll not up yet — make the parent

            // Synthetic marker at the baked front offset (local (0,0,6)) so SpawnVendor derives the
            // building's front DISTANCE + facing exactly like the poll and the baked storefronts.
            var marker = new GameObject($"NPC_{role}_Marker (placed)");
            marker.transform.SetParent(buildingTransform, false);
            marker.transform.localPosition = new Vector3(0f, 0f, 6f);

            _lastSpawnedVendor = null;
            bool ok = SpawnVendor(marker.transform, role, ResolveHero(), holder.transform, buildingId);
            Destroy(marker);

            if (ok)
            {
                // Tag the building so a re-notify (double placement event) can't stack a second vendor,
                // and so the tag self-clears if the vendor is ever destroyed (Vendor -> Unity fake-null).
                if (seated == null) seated = buildingTransform.gameObject.AddComponent<VendorSeatMarker>();
                seated.Vendor = _lastSpawnedVendor;
                FlowTrace.Step("NpcSeat",
                    $"vendor NPC spawned for placed '{buildingId}' (role '{role}') at {buildingTransform.position}.");

                // BAKED-STRAY EVICTION (owner F8 seq524 "Doubles still" / "armorer (Twice)"):
                // the per-ROLE poll can seat this trade from a BAKED storefront BEFORE the
                // replay places the real building — then this per-BUILDING seat adds a second
                // body (captured: Talk-registered count=2 for Blacksmith/Forge/JewelersBench;
                // the pass-0 defer only closed the pass-0 ordering). A vendor owned by a real
                // building carries a VendorSeatMarker; a poll/baked-seated one does NOT. When
                // a PLACED building takes the trade, evict every marker-less same-role body —
                // placed wins (it is also the correctly ground-seated one; the baked
                // Blacksmith anchor sits sunk at y=-0.03, the owner's wrong-height sighting).
                // Deliberately keeps OTHER placed buildings' vendors (the owner's
                // every-building-has-a-speaker ruling) — only ownerless strays go.
                Guard.Try("NpcSeat", "evict baked-stray same-role vendors", () =>
                {
                    var markers = Object.FindObjectsByType<VendorSeatMarker>(FindObjectsSortMode.None);
                    foreach (var go in GameObject.FindObjectsByType<Transform>(FindObjectsSortMode.None))
                    {
                        if (go == null || go.gameObject == _lastSpawnedVendor) continue;
                        string n = go.name;
                        if (n != "CastleVendor_" + role && n != "CastleVendor_" + role + "_Placeholder") continue;
                        bool owned = false;
                        foreach (var m in markers)
                            if (m != null && m.Vendor == go.gameObject) { owned = true; break; }
                        if (owned) continue;
                        FlowTrace.Step("NpcSeat",
                            $"evicted baked-stray vendor '{n}' @ {go.position} — the placed '{buildingId}' owns the {role} trade now.");
                        Destroy(go.gameObject);
                    }
                });
            }
            else
                FlowTrace.Fail("NpcSeat",
                    $"vendor NPC spawn FAILED for placed '{buildingId}' (role '{role}').");
        }

        /// <summary>Reverse of <see cref="AnchorRoles"/> (buildingId -> vendor role), PLUS the
        /// WO-707 palette ids that carry no AnchorRoles entry but ARE storefronts the player
        /// places (workshop/mill/lumbermill/lumberyard/foundry/silo). Returns null for
        /// non-storefront ids (towers/walls/gates/deco) so placing those never spawns a
        /// spurious merchant. PUBLIC so the AutoPilot coverage oracle (AssertVendorCoverage)
        /// asserts the non-action exclusion against the injector's OWN map, not a copy.</summary>
        public static string RoleForBuildingId(string buildingId)
        {
            if (string.IsNullOrEmpty(buildingId)) return null;
            foreach (var (role, id) in AnchorRoles)
                if (string.Equals(id, buildingId, System.StringComparison.OrdinalIgnoreCase))
                    return role;
            // Placeable storefront ids WO-707 grooming left out of AnchorRoles — give each a
            // sensible vendor so every trade building gets an NPC to Talk/trade with.
            switch (buildingId.ToLowerInvariant())
            {
                case "workshop":        return "Forge";       // in-world label "Forge" (weapons)
                case "mill":            return "Windmill";
                case "lumbermill":      return "Lumbermill";
                case "collector_forge": return "Forge";       // structures-catalog id AnchorRoles missed -> was NPC-less
                case "lumberyard":
                case "foundry":
                case "silo":            return "Marketplace"; // storage container — generic merchant
                default:                return null;          // not a storefront -> no vendor
            }
        }

        /// <summary>True for the storefront-less placeable ids (towers/walls/gates/mines/fountains/
        /// decorations/repair) that legitimately get NO vendor — so <see cref="SpawnVendorForPlaced"/>
        /// stays quiet for those but WARNS (captures) on an UNRECOGNIZED building id it forgot to map.</summary>
        private static bool IsKnownNonStorefront(string buildingId)
        {
            if (string.IsNullOrEmpty(buildingId)) return true;
            string id = buildingId.ToLowerInvariant();
            return id.StartsWith("tower_") || id.StartsWith("wall_") || id.StartsWith("gate_") ||
                   id.StartsWith("mine_")  || id.StartsWith("deco_") || id.StartsWith("fountain_") ||
                   id.StartsWith("repair_");
        }

        /// <summary>Spawns ONE static NPC at the marker and attaches the interaction.</summary>
        // The castle centre to face NPCs toward — the Heart (world-tree). Runtime-found; CastleHubBuilder
        // places it at (0,0,12), the fallback used if the controller isn't up yet.
        private static Vector3 HeartCenter()
        {
            var h = FindAnyObjectByType<HeartController>();
            return h != null ? h.transform.position : new Vector3(0f, 0f, 12f);
        }

        private bool SpawnVendor(Transform marker, string role, Transform hero, Transform parent, string catalogId = null)
        {
            using var _ = FlowTrace.Enter("Village", $"CastleVendorNpcInjector.SpawnVendor role='{role}'");
            Vendor v = VendorFor(role);

            // WO-818: the ANCHORING catalog row's repo.npcModel (KayKit slug, owner mapping
            // table) is the FIRST body source — a data retag swaps a vendor's body with zero
            // code. catalogId is the building id this vendor anchors to (AnchorRoles poll /
            // placement hook); rows with no npcModel (and null catalogId) fall straight
            // through to the legacy People chain — a bad slug warns ONCE in the resolver.
            string bodyRes = v.BodyRes;
            string kayKitRes = null;   // WO-833: non-null marks a KayKit body -> arm the shared idle below
            GameObject prefab = null;
            // ── CANDIDATE-CAUSE #2 PROBE: "did the body resolve, and from WHERE?" ─────────────
            // Previously only the total MISS was logged, so a body that resolved via an unexpected
            // source (or fell through the data-driven npcModel path silently, which is exactly what
            // a catalog row with npcModel None does) left no line at all. Name the source AND the
            // exact Resources path for every outcome, including the quiet not-authored fall-through.
            string bodySource;
            if (!string.IsNullOrEmpty(catalogId))
            {
                prefab = KayKitNpcBody.Load(catalogId, "Village", out kayKitRes);
                if (prefab != null) bodyRes = kayKitRes;
            }
            if (prefab != null)
            {
                bodySource = $"catalog repo.npcModel of '{catalogId}' -> Resources/{kayKitRes}";
            }
            else
            {
                // Not authored (npcModel None / no catalog row) or the slug missed — KayKitNpcBody
                // already Warns on a MISS, and is deliberately quiet when unauthored. Say which.
                string why = string.IsNullOrEmpty(catalogId)
                    ? "no catalogId supplied (baked/marker seat path)"
                    : $"catalog row '{catalogId}' authored no usable repo.npcModel (npcModel absent/None, or the slug missed — see any KayKit Warn above)";
                var primary = Resources.Load<GameObject>(v.BodyRes);
                if (primary != null)
                {
                    prefab = primary;
                    bodySource = $"legacy People chain -> Resources/{v.BodyRes} ({why})";
                }
                else
                {
                    var merchant = Resources.Load<GameObject>(BodyMerchant);
                    if (merchant != null)
                    {
                        prefab = merchant;
                        bodyRes = BodyMerchant;
                        // A vendor wearing the generic merchant instead of its own body is a REAL
                        // defect signal (the Armorer should be the smith), so Warn, not Step.
                        FlowTrace.Warn("Village",
                            $"vendor role '{role}' (structureId '{v.StructureId}'): its OWN body " +
                            $"Resources/{v.BodyRes} FAILED to load — falling back to the generic " +
                            $"Resources/{BodyMerchant}. {why}. Stage the missing prefab at " +
                            $"Assets/Resources/{v.BodyRes}.prefab to restore this vendor's identity.");
                        bodySource = $"GENERIC merchant fallback -> Resources/{BodyMerchant} (own body Resources/{v.BodyRes} missing)";
                    }
                    else bodySource = $"NONE — Resources/{v.BodyRes} and Resources/{BodyMerchant} both missing ({why})";
                }
            }
            FlowTrace.Step("Village",
                $"vendor role '{role}' (structureId '{v.StructureId}', label '{v.Label}', catalogId " +
                $"'{catalogId ?? "<null>"}') body resolution: {bodySource}; prefabResolved={(prefab != null)}.");
            if (prefab == null)
            {
                // T/U: load-miss — fall back to a placeholder so the storefront still gets a working
                // vendor, and self-report (Warn -> break-log instead of a swallowed LogWarning).
                FlowTrace.Warn("Village",
                    $"CastleVendorNpcInjector: no body prefab for role '{role}' (missing Resources/{v.BodyRes}) — placeholder used.");
                Debug.LogWarning($"[CastleVendorNpcInjector] no body prefab for role '{role}' (missing Resources/{v.BodyRes}) — placeholder used.");
                return SpawnPlaceholder(marker, role, v, hero, parent);
            }

            // FRONT-OF-BUILDING PLACEMENT (owner 2026-07-13 "the agents should be at the
            // front of each building" — supersedes the 2026-06-21 Heart-facing rule for
            // player-placed structures): the vendor stands on the side the building FACES
            // (the door side = the placed root's forward, i.e. the yaw the player chose at
            // placement). Fallback: when the building root has no meaningful facing
            // (identity yaw baked shell), keep the old Heart-facing side so baked-era
            // saves look unchanged. Preserves the hand-baked front-offset DISTANCE.
            Vector3 buildingPos = marker.parent != null ? marker.parent.position : marker.position;
            Vector3 flatBuild = new Vector3(buildingPos.x, 0f, buildingPos.z);
            float frontDist = Vector3.Distance(flatBuild, new Vector3(marker.position.x, 0f, marker.position.z));
            if (frontDist < 1f) frontDist = 5f;   // marker sits at the building -> default front distance
            Vector3 center = HeartCenter();
            Vector3 toHeart = new Vector3(center.x - buildingPos.x, 0f, center.z - buildingPos.z);
            toHeart = toHeart.sqrMagnitude < 0.01f ? Vector3.forward : toHeart.normalized;
            Vector3 front = toHeart;   // fallback: the Heart-facing side
            if (marker.parent != null)
            {
                Vector3 fwd = marker.parent.forward; fwd.y = 0f;
                if (fwd.sqrMagnitude > 0.01f) front = fwd.normalized;
            }
            Vector3 pos = flatBuild + front * frontDist;
            // WO-703 / BLANK-1: constrain the spawn sample to the GROUND RING — the 3D
            // sample near a wall-adjacent building can resolve to the elevated wall-walk
            // navmesh (the "NPC on top of the gatehouse" symptom). Accept the hit only
            // inside the ground band around the scripted flat ground y=0 (mirrors
            // CastleTownsfolkInjector / NpcGroundSeat bands); out-of-band -> keep the
            // computed courtyard position instead.
            if (NavMesh.SamplePosition(pos, out var hit, 4f, NavMesh.AllAreas))
            {
                if (hit.position.y >= -0.35f && hit.position.y <= 0.75f)
                    pos = hit.position;
                else
                    FlowTrace.Step("Village",
                        $"vendor '{role}' spawn sample rejected: navmesh hit y={hit.position.y:F2} outside " +
                        "ground band [-0.35..0.75] (wall-top/elevated mesh) — using courtyard position.");
            }
            // Face the Heart / approaching hero (the hero comes from the centre).
            Quaternion rot = Quaternion.LookRotation(toHeart, Vector3.up);

            GameObject go = null;
            Guard.Try("Village", $"instantiate vendor body role='{role}'", () =>
            {
                go = Instantiate(prefab, pos, rot, parent);
            });
            if (go == null)
            {
                // G/R: Instantiate returned/threw null — fall back to a placeholder so the storefront
                // is never left vendorless, and self-report.
                FlowTrace.Fail("Village",
                    $"CastleVendorNpcInjector: Instantiate returned null for role '{role}' ('{bodyRes}') — placeholder used.");
                return SpawnPlaceholder(marker, role, v, hero, parent);
            }
            go.name = $"CastleVendor_{role}";

            // V (render-verify): a body with no enabled mesh reads as an invisible vendor. Prove it
            // renders; on failure drop it and fall back to the placeholder (never an invisible vendor).
            if (!VerifyNpcRenders(go, bodyRes))
            {
                FlowTrace.Fail("Village",
                    $"CastleVendorNpcInjector: vendor body role='{role}' ('{bodyRes}') has no visible mesh — dropping, placeholder used.");
                Destroy(go);
                return SpawnPlaceholder(marker, role, v, hero, parent);
            }

            // WO-833: a KayKit body ships an Animator + Humanoid avatar but NO controller,
            // so it renders its bind pose (owner F8 "NPC Stuck in T Pose") - arm the shared
            // retargeted idle. ⛔ ONLY a KayKit body: Load() now returns a non-null resolvedRes for
            // a PROD-002 "CraftPixPeople/..." slug too, and ALL TWELVE vendor rows are CraftPix
            // today - so this line was arming every storefront speaker with KayKitNpcIdle's
            // m-standby-idle, the Knight's COMBAT stance, over the townsfolk controller's civilian
            // idle (owner 2026-08-20: "they need to use ide not combat idle"). Guard on the PATH
            // so a future slug retag cannot silently re-introduce it.
            if (KayKitNpcBody.IsKayKitPath(kayKitRes)) KayKitNpcBody.ArmIdle(go, kayKitRes, "Village");

            NormalizeToHeroHeight(go);
            // T-033 ("NPCs floating"): scaling about a non-feet pivot lifts the model's
            // feet off the floor AND the NavMesh-sampled Y sits a touch ABOVE the visual
            // floor (voxel cell height). Raycast DOWN to the real floor collider and seat
            // the renderer-bounds bottom onto it; falls back to the navmesh Y if no floor
            // is hit. (Replaces the old seat-to-navmesh-Y that left them hovering.)
            NpcGroundSeat.Seat(go, pos.y);

            // STATIC: Configure with wander=FALSE. AmbientNPC.Start then disables its
            // NavMeshAgent and the NPC stands its ground (idle sway only — no roam/follow).
            var npc = go.GetComponent<AmbientNPC>();
            if (npc != null)
            {
                npc.Configure(v.Arch, /*wander*/ false, pos);
                // Do NOT hand it the hero: with no hero the townsfolk proximity-chatter
                // bubble stays quiet, so it never competes with the structure dialogue our
                // CastleNpcInteractable opens. (The idle visual + animator still run.)
            }
            // Belt-and-braces: if a NavMeshAgent slips through (e.g. AmbientNPC missing),
            // make sure nothing tries to move the body.
            var agent = go.GetComponent<NavMeshAgent>();
            if (agent != null) { agent.enabled = false; }

            AttachInteraction(go, v, hero);
            _lastSpawnedVendor = go;
            return true;
        }

        // Minimal capsule fallback if the People-pack body is absent (Models gitignored on
        // a fresh clone). Getting the INTERACTION working is the priority. // TODO real NPC art
        private bool SpawnPlaceholder(Transform marker, string role, Vendor v, Transform hero, Transform parent)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            go.name = $"CastleVendor_{role}_Placeholder";
            go.transform.SetParent(parent, false);

            Vector3 pos = marker.position;
            if (NavMesh.SamplePosition(pos, out var hit, 4f, NavMesh.AllAreas)) pos = hit.position;
            go.transform.position = pos + Vector3.up * 1f;
            go.transform.rotation = marker.rotation;

            // The capsule's collider would block the hero; the interaction is proximity-based,
            // so make it non-blocking.
            var col = go.GetComponent<Collider>();
            if (col != null) col.isTrigger = true;

            AttachInteraction(go, v, hero);
            _lastSpawnedVendor = go;
            return true;
        }

        private void AttachInteraction(GameObject body, Vendor v, Transform hero)
        {
            // G: a throw while wiring the interaction would otherwise spawn a mute, uninteractable
            // vendor with no log. Guard it so the failure self-reports (Fail -> break-log) and is skipped.
            // PROD-002 Deliverable A — this structure's flow moved to Manage, so the NPC opens
            // nothing. ⛔ RETURN BEFORE MarkNpcCovered, not after: MarkNpcCovered is what makes the
            // BUILDING defer its own prompt, so calling it here while attaching no interactable
            // would suppress the building's door on behalf of an NPC door that does not exist —
            // closing both by accident and for the wrong reason. BuildingInteractable.HasNoTalkDoor
            // closes the building side deliberately; this side just declines to open one.
            // ⚠ THE BODY IS NOT TOUCHED. It has already been spawned, seated and animated above;
            // only the affordance is withheld. "They add no value" was true of the door, not the
            // person — a town with people working in it is not a diorama.
            if (BuildingInteractable.HasNoTalkDoor(v.StructureId))
            {
                FlowTrace.Once("Village", "npc-no-talk-door-" + v.StructureId,
                    $"CastleVendorNpcInjector: '{v.StructureId}' has no service door (PROD-002 A) — " +
                    "body kept as ambient life, NO CastleNpcInteractable, no sign, building not marked covered.");
                return;
            }

            Guard.Try("Village", $"attach vendor interaction '{v.StructureId}'", () =>
            {
                var interact = body.AddComponent<CastleNpcInteractable>();
                interact.Configure(v.StructureId, v.Label, hero);
                BuildingInteractable.MarkNpcCovered(v.StructureId);   // the matching building defers its prompt — NPC owns the talk

                // T-034: an always-visible type sign floats above the vendor so the player
                // can tell a shop from an upgrade from a talk NPC from a distance. The body
                // is rescaled to hero height, so place the sign in the body's LOCAL space at
                // a height that clears the head regardless of the (already-applied) scale.
                float localHeadClear = SignHeightAboveHead(body);
                InteractableSign.ForStructureId(body, v.StructureId, localHeadClear);
            });

            // Ticket F8-14 (owner 2026-07-08: "when the other NPCs leave we should hide the
            // vendor ones, then show after"): vendors are wander=false so AmbientNPC's flee
            // state machine deliberately skips them — this watcher hides the body + kills the
            // Talk registration on the SAME combat signal the townsfolk flee on, restores after.
            if (body.GetComponent<CastleVendorWaveHider>() == null)
                body.AddComponent<CastleVendorWaveHider>();
        }

        // V (render-verify): the spawned body must carry >=1 ENABLED Renderer with an actual mesh.
        // Traces the counts so a capture splits "no mesh" from a real spawn. Returns false => caller
        // drops it + uses a placeholder (never an invisible vendor).
        private static bool VerifyNpcRenders(GameObject go, string res)
        {
            if (go == null) return false;
            int total = 0, enabledWithMesh = 0;
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            {
                if (r == null) continue;
                total++;
                if (!r.enabled) continue;
                bool hasMesh =
                    (r is SkinnedMeshRenderer smr && smr.sharedMesh != null) ||
                    (r.TryGetComponent<MeshFilter>(out var mf) && mf.sharedMesh != null);
                if (hasMesh) enabledWithMesh++;
            }
            bool ok = enabledWithMesh > 0;
            if (!ok)
                FlowTrace.Warn("Village",
                    $"VerifyNpcRenders '{res}': {total} renderer(s), {enabledWithMesh} enabled-with-mesh — reads invisible.");
            return ok;
        }

        /// <summary>
        /// Local-space Y (in the body's already-scaled frame) that floats the sign just
        /// above the NPC's head. Converts a fixed world clearance through the body's
        /// lossy scale so the sign sits the same world distance above every NPC, no
        /// matter the pack body's native scale.
        /// </summary>
        private static float SignHeightAboveHead(GameObject body)
        {
            const float WorldClearAboveOrigin = 2.6f; // ~head height (1.95) + a touch of air
            float scaleY = body.transform.lossyScale.y;
            return scaleY > 0.01f ? WorldClearAboveOrigin / scaleY : WorldClearAboveOrigin;
        }

        // Reuse VillageNpcInjector's height normalization so the People-pack bodies sit at
        // ~hero height instead of towering (the packs import at varying native scales).
        private static void NormalizeToHeroHeight(GameObject go)
        {
            float npcScale = 1f;
            var rends = go.GetComponentsInChildren<Renderer>();
            if (rends.Length > 0)
            {
                Bounds b = rends[0].bounds;
                for (int i = 1; i < rends.Length; i++) b.Encapsulate(rends[i].bounds);
                if (b.size.y > 0.01f) npcScale = 1.95f / b.size.y;   // ~hero height (1.8) + a touch
            }
            if (npcScale > 0.01f && !Mathf.Approximately(npcScale, 1f))
            {
                go.transform.localScale *= npcScale;
                // Keep any speech bubble at real world size on the rescaled body.
                var bubbleRoot = go.transform.Find("BubbleRoot");
                if (bubbleRoot != null) bubbleRoot.localScale = Vector3.one / Mathf.Max(0.01f, npcScale);
            }
        }

        // (Ground-seating now lives in the shared NpcGroundSeat helper — it raycasts to
        // the real floor instead of the navmesh Y, fixing the residual hover. See T-033.)

        // Name-based hero lookup (matches AmbientNPC / VillageNpcInjector): the hero rig is
        // named "Hero (...)"; the project also tags it "Player".
        private static Transform ResolveHero()
        {
            var tagged = GameObject.FindWithTag("Player");
            if (tagged != null) return tagged.transform;
            foreach (var t in FindObjectsByType<Transform>())
                if (t != null && t.name.StartsWith("Hero")) return t;
            return null;
        }
    }

    // =========================================================================
    // VendorSeatMarker — a lightweight tag added to a PLACED building once its vendor
    // NPC exists, so the placement hook is idempotent PER BUILDING (never stacks a
    // second vendor on a double placement event) without falling back to the old
    // per-role-global check that starved same-trade buildings of their own speaker.
    // Vendor is a UnityEngine.Object reference: if the NPC is ever destroyed it becomes
    // fake-null, so the building naturally re-qualifies for a fresh vendor.
    // =========================================================================
    [DisallowMultipleComponent]
    public sealed class VendorSeatMarker : MonoBehaviour
    {
        public GameObject Vendor;
    }

    // =========================================================================
    // CastleVendorWaveHider — ticket F8-14: vendor NPCs duck OUT OF SIGHT for the
    // duration of a wave/battle, exactly like the fleeing townsfolk, then reappear
    // at their storefronts when the fight ends. Vendors are configured wander=false
    // so AmbientNPC's flee state machine deliberately skips them — this watcher
    // REUSES the SAME combat authority (AmbientNPC.IsCombatActive: wave
    // Countdown/Active OR BattleLock, shared 0.25s poll) instead of inventing a
    // second signal.
    //
    // Hide = renderers off (body + floating sign) + CastleNpcInteractable disabled —
    // its OnDisable releases the MobileInteractButton, deregisters from
    // TalkPromptRegistry (so the HUD Talk light dies) and clears HudBuildingFocus.
    // The GameObject stays ACTIVE so this watcher keeps polling for the all-clear
    // (mirrors AmbientNPC.SetBodyVisible's hide-without-deactivate rule).
    // =========================================================================
    /// <summary>Hides a castle vendor NPC while combat is live (ticket F8-14) and
    /// restores it after — same signal the townsfolk flee-to-shelter system uses.</summary>
    [DisallowMultipleComponent]
    public sealed class CastleVendorWaveHider : MonoBehaviour
    {
        // Only the renderers WE disabled get restored — a renderer something else
        // deliberately disabled (e.g. an injector fallback) stays off.
        private readonly System.Collections.Generic.List<Renderer> _hiddenRenderers =
            new System.Collections.Generic.List<Renderer>();
        private CastleNpcInteractable _interact;
        private bool _hidden;
        private bool _counted;

        // Observability: one Step per global combat transition, carrying the counts.
        private static int s_registered;
        private static int s_hiddenCount;
        private static bool s_lastCombat;

        private void Start()
        {
            _interact = GetComponent<CastleNpcInteractable>();
            s_registered++;
            _counted = true;
        }

        private void OnDestroy()
        {
            if (_counted && s_registered > 0) s_registered--;
            if (_hidden && s_hiddenCount > 0) s_hiddenCount--;
        }

        private void Update()
        {
            bool combat = AmbientNPC.IsCombatActive;

            // One announcement per global transition (first hider to notice logs it),
            // instrumented per the ticket: "vendors hidden (wave)" / "vendors restored".
            if (combat != s_lastCombat)
            {
                s_lastCombat = combat;
                FlowTrace.Step("Village", combat
                    ? $"vendors hidden (wave): {s_registered} vendor NPC(s) duck out of sight"
                    : $"vendors restored: {s_registered} vendor NPC(s) back at their storefronts " +
                      $"(were hidden={s_hiddenCount})");
            }

            if (combat && !_hidden) Hide();
            else if (!combat && _hidden) Show();
        }

        private void Hide()
        {
            _hidden = true;
            s_hiddenCount++;
            _hiddenRenderers.Clear();
            // Fetch fresh (not cached at Start): the InteractableSign / bubble children
            // may be built after this component attaches.
            foreach (var r in GetComponentsInChildren<Renderer>(true))
                if (r != null && r.enabled) { r.enabled = false; _hiddenRenderers.Add(r); }
            // OnDisable releases the interact button, the TalkPromptRegistry entry
            // (HUD Talk light) and HudBuildingFocus — and its Update never re-registers
            // while disabled, so talk-shopping is unreachable for the whole wave.
            if (_interact != null) _interact.enabled = false;
        }

        private void Show()
        {
            _hidden = false;
            if (s_hiddenCount > 0) s_hiddenCount--;
            foreach (var r in _hiddenRenderers)
                if (r != null) r.enabled = true;
            _hiddenRenderers.Clear();
            if (_interact != null) _interact.enabled = true;
        }
    }

    // =========================================================================
    // CastleNpcInteractable — slim proximity [F] / mobile-tap interaction that
    // opens the parameterized Yarn structure dialogue for ONE vendor. Mirrors
    // BuildingInteractable's pattern (ActivateRadius ~6m, registers the shared
    // MobileInteractButton, only the NEAREST in-range acts on the global F key,
    // suppressed while a dialogue / modal panel is open) but is keyed by an explicit
    // structureId instead of a Building component — the castle NPCs have no Building.
    // =========================================================================
    [DisallowMultipleComponent]
    public sealed class CastleNpcInteractable : MonoBehaviour
    {
        private const float ActivateRadius = 6f;
        private const float StructureCloseRadius = ActivateRadius + 4f;

        private string _structureId;
        private string _label;

        // TEST SEAM (data-verify, 2026-06-20): the last routing DECISION Interact() made, exposed so
        // the headless AutoPilot oracle (AssertVendorTalkRoute) can assert a SHOPPABLE vendor's Talk
        // press routes to the dialogue ("talk-dialogue"), NOT the upgrade panel ("upgrade-panel").
        // This is observable WITHOUT rendering — the Yarn dialogue + UITK upgrade panel are both
        // invisible in -nographics, so the opened surface can't distinguish the routes; the decision can.
        public static string LastInteractRoute;
        public static string LastInteractId;
        private Transform _hero;
        private bool _openedStructure;

        public void Configure(string structureId, string label, Transform hero)
        {
            _structureId = structureId;
            _label = label;
            _hero = hero;
        }

        private void Update()
        {
            if (_hero == null) { ResolveHero(); return; }

            // Build mode: release + bail (player is authoring, not interacting).
            if (MobileInteractButton.Suppressed)
            {
                MobileInteractButton.Release(this);
                TalkPromptRegistry.Deregister(transform);
                return;
            }

            float distSqr = (_hero.position - transform.position).sqrMagnitude;
            bool inRange = distSqr <= ActivateRadius * ActivateRadius;

            // Walk-away auto-close: if WE opened the structure dialogue and the hero left.
            if (_openedStructure)
            {
                if (!DialogueService.IsRunning) _openedStructure = false;
                else if (distSqr > StructureCloseRadius * StructureCloseRadius)
                {
                    DialogueService.Stop();
                    _openedStructure = false;
                }
            }

            // While any dialogue is on screen, drop our prompt so it doesn't stack under it.
            if (DialogueService.IsRunning)
            {
                MobileInteractButton.Release(this);
                TalkPromptRegistry.Deregister(transform);
                return;
            }

            if (inRange)
            {
                // WO-416: do NOT raise the shared MobileInteractButton for talk NPCs. The HUD
                // TALK button (+ its glow) is the canonical interaction trigger now, so the old
                // bottom-centre "Talk: <name>" element was a redundant duplicate at vendors. We
                // still register with TalkPromptRegistry so the HUD TALK button fires us, and the
                // desktop [F] path below is untouched.
                TalkPromptRegistry.Register(transform, Interact);
                // Owner severe: flip the HUD context button to its UPGRADE face when near an
                // upgradable storefront. The NPC-fronted path never set HudBuildingFocus, so the
                // button never swapped (trace: focus='<none>'). Match BuildingInteractable.
                if (IsUpgradableId(_structureId)) HudBuildingFocus.Set(_structureId);
                else                              HudBuildingFocus.Clear(_structureId);
            }
            else
            {
                MobileInteractButton.Release(this);
                TalkPromptRegistry.Deregister(transform);
                HudBuildingFocus.Clear(_structureId);
            }

            // Mobile-first: the HUD TALK button (via TalkPromptRegistry above) is the
            // canonical trigger. The desktop F-key trigger was removed.
        }

        private void Interact()
        {
            if (string.IsNullOrEmpty(_structureId)) return;
            // DATA-VERIFY (owner 2026-06-20, never inference-fix): log the routing inputs + chosen
            // branch so a HEADLESS capture PROVES Talk reaches the Buy/Sell dialogue for shoppable
            // vendors (forge/armorer/market/jeweler) instead of being stolen by the upgrade panel.
            LastInteractId = _structureId;
            LastInteractRoute = ResolveRoute(_structureId);
            FlowTrace.Step("Village", $"CastleNpc.Interact '{_label}' id='{_structureId}' -> route={LastInteractRoute}");
            // WO-951 (owner 2026-08-10): the Echo Hollow keeper's Talk opens the EXISTING Echo
            // roster popup — NOT the legacy Yarn grant menu. EchoRoster.Open self-traces
            // ([Flow:Echo] RosterOpen) and registers with PanelManager (single-modal discipline),
            // so a rejected/failed open is already a logged line, never a silent no-op.
            if (LastInteractRoute == EchoRosterRoute)
            {
                FlowTrace.Step("Village", $"CastleNpc '{_label}' -> Echo roster (WO-951 Hollow repurpose).");
                EchoRoster.Open();
                return;
            }
            // §12 / WO-413: the castle vendor NPCs are the primary live interaction surface (the
            // home hub is MainCastle_Hall). They open the SAME parameterized StructureMenu, so the
            // shop-vs-upgrade split is decided data-driven by that node's gates (seeded from
            // BuildingCatalog caps in CmdStructureStatus) — never here. Trace the id used so a
            // "wrongly offers shop" report maps to the catalog entry behind it.
            // UPGRADE-ONLY buildings route STRAIGHT to the code-built Building Upgrade panel (owner:
            // Mill/Old Pell, Arcane Tower must NOT show a Yarn menu). But a SHOPPABLE vendor (forge,
            // armorer, market, jeweler) is a TALK target first: Talk fires the vendor DIALOGUE
            // (Buy/Sell/Leave + quest options), per owner 2026-06-21. Its upgrade, if any, is reached
            // through the dedicated HUD context/Upgrade button — never by stealing the Talk press.
            if (LastInteractRoute == "upgrade-panel")
            {
                if (PanelRouter.Open(PanelId.BuildingUpgrade, _structureId))
                    FlowTrace.Step("Village", $"CastleNpc '{_label}' -> MVVM Building Upgrade (focus='{_structureId}').");
                else
                    FlowTrace.Warn("Village", $"Building Upgrade panel opener not registered for '{_structureId}' — NOT falling through to Yarn.");
                return;
            }

            FlowTrace.Step("Village", $"CastleNpc '{_label}' -> structure '{_structureId}' (StructureMenu gates shop/upgrade)");
            if (DialogueService.PlayStructure(_structureId, _label))
            {
                _openedStructure = true;
                Debug.Log($"[CastleNpcInteractable] {_label} -> structure dialogue '{_structureId}'.");
                return;
            }

            // WO-576: PlayStructure returned false — no conversation authored AND not a shoppable vendor
            // (the deleted-Yarn hole). NEVER dead-end the Talk: fall back to this building's upgrade panel
            // if it has one (mirrors BuildingInteractable's TryPanelFor fallback), else self-report. This
            // is the safety net behind the flavor-dialogue fix, so a future content gap can't silently no-op.
            if (IsUpgradableId(_structureId) && PanelRouter.Open(PanelId.BuildingUpgrade, _structureId))
            {
                FlowTrace.Step("Village", $"CastleNpc '{_label}' -> Building Upgrade (no conversation/shop fallback, focus='{_structureId}').");
                return;
            }
            FlowTrace.Warn("Village", $"CastleNpc '{_label}' id='{_structureId}': Talk had no conversation, shop, or upgrade panel — nothing to open.");
        }

        // Upgradable = city tiers (BuildingTierCatalog) OR legacy resource buildings — same test
        // BuildingInteractable uses, so the building's own interact and its NPC agree. A collector's
        // catalog id ("collector_lumbermill"/"collector_farm") is first resolved to its bare
        // upgrade-keyed id ("lumbermill"/"farm") so a collector NPC's upgrade fallback fires
        // (ResolveUpgradeId is a pass-through for every non-collector id).
        private static bool IsUpgradableId(string id)
        {
            if (string.IsNullOrEmpty(id)) return false;
            string upg = DeNelle.Core.Catalog.CatalogRegistry.ResolveUpgradeId(id);
            return DeNelle.Core.State.BuildingTierCatalog.IsUpgradable(upg) ||
                   Buildings.Progression.ResourceBuildingProgression.IsResourceBuilding(upg);
        }

        // Shoppable = the catalog entry opts in (buildings.json isShoppable). A shoppable vendor's
        // Talk press opens the Buy/Sell/Leave dialogue (StructureMenu), never the upgrade panel —
        // upgrade is reached through the dedicated HUD context/Upgrade button (owner 2026-06-21).
        private static bool IsShoppableId(string id) =>
            !string.IsNullOrEmpty(id) &&
            BuildingCatalog.Find(id)?.IsShoppable == true;

        // A building whose Talk opens a NON-shop interactive FUNCTION (e.g. the barracks drillmaster's troop
        // TRAINING menu). Like a shoppable vendor, its Talk press opens that function and its upgrade is
        // reached via the HUD context/Upgrade button — NEVER by stealing the Talk press (owner 2026-06-21:
        // "match what we use everywhere else"). Extend this set as other "has a menu" buildings appear.
        // Ticket #11: barracks became upgradable, which (without this) routed its NPC to the upgrade panel
        // and made the troop-TRAINING flow unreachable through the drillmaster — this restores the vendor pattern.
        private static readonly System.Collections.Generic.HashSet<string> TalkFunctionIds =
            new System.Collections.Generic.HashSet<string> { "barracks" };
        public static bool HasTalkFunctionId(string id) =>
            !string.IsNullOrEmpty(id) && TalkFunctionIds.Contains(id);

        // True if a CONVERSATION is authored for this structure in dialogues.json (WO-576). A
        // resource/upgrade-only NPC with a flavor line (farm/lumbermill/arcane-tower) is a Talk
        // target FIRST — its upgrade rides the HUD context/Upgrade button (HudBuildingFocus),
        // never the Talk press. Without this, the upgrade short-circuit stole the Talk and the
        // farmer/woodcutter/arcanist "Talk" went nowhere (the deleted Yarn StructureMenu's hole).
        private static bool HasConversation(string id) =>
            DeNelle.Core.Dialogue.DialogueCatalog.Find(id) != null;

        // ── WO-951 (owner ruling 2026-08-10): the Echo Hollow is repurposed ──────────
        // Interacting with the Hollow — building tap (BuildingInteractable) OR keeper Talk
        // (this class) — opens the EXISTING Echo roster popup (EchoRoster.Open), verbatim:
        // "so then when they go to the store they open the echos pop up on right? Simple
        // and easy." One verb, no new UI. The old Yarn grant menu (Echo Warden choose-a-
        // pet) is superseded as the interact surface: Echoes unlock by level now
        // (EchoService), and the starter grant rides the founding-arc ARRIVE beat, not
        // this menu. These constants + the predicate are the single chokepoint both
        // interact surfaces AND the regression suite key on.
        public const string EchoHollowId = "pet-house";
        public const string EchoRosterRoute = "echo-roster";
        public static bool IsEchoHollowId(string id) =>
            !string.IsNullOrEmpty(id) &&
            string.Equals(id, EchoHollowId, System.StringComparison.OrdinalIgnoreCase);

        // SHARED routing decision — the SINGLE source of truth for Interact()'s branch AND the headless
        // oracle (AssertVendorTalkRoute), so the test can never drift from the real route. PURE, no side
        // effects: the Echo Hollow opens the Echo roster popup (WO-951 — checked FIRST so neither the
        // upgrade short-circuit nor the Yarn menu can steal it); a structure with an authored
        // CONVERSATION, a SHOPPABLE vendor, or a TALK-FUNCTION building opens the Talk dialogue (its
        // primary function); the upgrade panel is reached ONLY when upgrade is the building's ONLY
        // function (upgradable, NOT shoppable, no conversation, no talk function). Upgrade for the
        // talk-first ones is the HUD context button (owner 2026-06-21).
        // Verifiable headless WITHOUT rendering.
        public static string ResolveRoute(string structureId) =>
            IsEchoHollowId(structureId) ? EchoRosterRoute
            : (IsUpgradableId(structureId) && !IsShoppableId(structureId)
                && !HasTalkFunctionId(structureId) && !HasConversation(structureId))
                ? "upgrade-panel" : "talk-dialogue";

        private void ResolveHero()
        {
            var tagged = GameObject.FindWithTag("Player");
            if (tagged != null) { _hero = tagged.transform; return; }
            var loco = FindAnyObjectByType<HeroLocomotion>();
            if (loco != null) _hero = loco.transform;
        }

        private void OnDisable()
        {
            MobileInteractButton.Release(this);
            TalkPromptRegistry.Deregister(transform);
            if (!string.IsNullOrEmpty(_structureId)) HudBuildingFocus.Clear(_structureId);
        }
    }
}

// =============================================================================
// DataRegression — headless "pass the real data object in, see the real response"
// regression harness. Owner directive 2026-06-13: instrument + run headless; this is
// the start of a robust regression script.
//
// Runs in batchmode (Unity closed) via:
//   run-unity-method.ps1 -Method DeNelle.Editor.DataRegression.RunAll -LogName data-regression.log
//
// It loads the REAL canonical catalogs through the SAME code path the game uses
// (GearCatalog -> CanonicalJson -> Newtonsoft), enumerates the resulting OBJECTS, and
// validates the response — so a silent JSON->object mapping break (wrong top-level key,
// renamed field, parse-to-empty) becomes a hard REGRESSION FAIL line instead of an
// empty store at runtime with no error. Prints a single authoritative marker:
//   REGRESSION_OK <n>/<n> suites          (all checks passed)
//   REGRESSION_FAIL: <n> failure(s) ...   (>=1 check failed)
//
// THE MARKER IS THE VERDICT (project law: judge by marker, never exit code) — so it
// must say WHICH suite produced it. Until 2026-08-02 THREE classes emitted a bare
// `REGRESSION_OK` (this file, SessionRegression, and the 22-case legacy
// Assets/Editor/RegressionSuite.cs) and the check-in gate ran the LEGACY one while
// every RESULT file read its marker as this one's. Distinct markers now:
//   DataRegression.RunAll    -> REGRESSION_OK <n>/<n> suites   (THE gate)
//   RegressionSuite.RunAll   -> CHECKIN_SUITE_OK <p>/<n> cases (legacy smoke battery)
//   SessionRegression.RunAll -> SESSION_GUARDS_OK
//   CompileGate.Run          -> COMPILE_GATE_OK  (whole-gate verdict)
//                            -> COMPILE_GATE_WEBGL_OK / _FAIL / _SKIPPED reason=<why>
//                               (WO-1575, the WebGL player-script stage INSIDE that gate;
//                                substring-disjoint from COMPILE_GATE_OK on purpose, so a
//                                grep for the whole gate cannot match the stage and a grep
//                                for the stage's pass cannot match its failure. _SKIPPED is
//                                a THIRD state, not a pass: it does NOT withhold
//                                COMPILE_GATE_OK, so on a machine with no WebGL module the
//                                proof is the named reason PLUS the absence of _WEBGL_OK.)
// RegressionMarkerRegression [regression-marker] keeps that invariant true.
// =============================================================================
using System.Collections.Generic;
using System.Text;
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.ResourceManagement.ResourceLocations;
using DeNelle.Village;
using DeNelle.Village.Arena;
using DeNelle.Village.Items;
using DeNelle.Village.Population;
using DeNelle.Core.State;
using DeNelle.Core.Catalog;

namespace DeNelle.Editor
{
    public static class DataRegression
    {
        public static void RunAll()
        {
            var failures = new List<string>();
            var log = new StringBuilder();
            log.AppendLine("=== DataRegression: real catalog objects in, real response out ===");

            // --- GEAR (the active 'empty store' suspect) ---------------------------
            // Force a fresh read through the real loader (CanonicalJson, Resources-first).
            GearCatalog.Reload();

            var weapons = new List<WeaponDef>(GearCatalog.AllWeapons());
            var armors  = new List<ArmorDef>(GearCatalog.AllArmors());

            log.AppendLine($"weapons.json -> {weapons.Count} WeaponDef objects");
            log.AppendLine($"armor.json   -> {armors.Count} ArmorDef objects");

            // Response check 1: did the JSON map to objects AT ALL? (catches the silent
            // parse-to-empty: file present but top-level key / field names mismatch.)
            if (weapons.Count == 0) failures.Add("weapons.json deserialized to 0 objects (mapping break or empty 'weapons' array)");
            if (armors.Count == 0)  failures.Add("armor.json deserialized to 0 objects (mapping break or empty 'armor' array)");

            // Response check 2: did the DISPLAY fields populate? A row renders blank if
            // name/id came through null/empty even when the count is right. This is exactly
            // the 'rows exist but look empty' case the owner suspected.
            int badWeapon = 0, badArmor = 0;
            foreach (var w in weapons)
            {
                bool ok = w != null && !string.IsNullOrEmpty(w.id) && !string.IsNullOrEmpty(w.name);
                if (!ok) badWeapon++;
                log.AppendLine($"  W {(w != null ? w.id : "<null>")} | name='{(w != null ? w.name : "<null>")}' " +
                               $"| dmg={(w != null ? w.damageMult : 0f):0.00} | cost={CostStr(GearCatalog.GetBuyCost(w))}");
            }
            foreach (var a in armors)
            {
                bool ok = a != null && !string.IsNullOrEmpty(a.id) && !string.IsNullOrEmpty(a.name);
                if (!ok) badArmor++;
                log.AppendLine($"  A {(a != null ? a.id : "<null>")} | name='{(a != null ? a.name : "<null>")}' " +
                               $"| def={(a != null ? a.defense : 0f):0.00} | cost={CostStr(GearCatalog.GetBuyCost(a))}");
            }
            if (badWeapon > 0) failures.Add($"{badWeapon} weapon(s) have null/empty id or name (would render as blank rows)");
            if (badArmor  > 0) failures.Add($"{badArmor} armor(s) have null/empty id or name (would render as blank rows)");

            // Response check 3: store would have NON-EMPTY stock for a general vendor.
            int generalStock = weapons.Count + armors.Count;
            if (generalStock == 0) failures.Add("general vendor stock is EMPTY (no weapons + no armors)");
            else log.AppendLine($"general vendor stock = {generalStock} gear rows (+ potions added at runtime)");

            // --- ABILITIES (abilities.json -> AbilityCatalog) ----------------------
            // Same shape as the gear checks: load through the REAL loader, assert the
            // JSON mapped to objects and every entry's DISPLAY fields populated. There
            // is no Resources PATH to resolve here on purpose: AbilityDef.Icon is a HUD
            // GLYPH (e.g. "✦"), NOT a Resources path (see AbilityCatalog.cs Icon doc +
            // HeroAbilities), and Color is a hex string — neither is Resources.Load'able,
            // so asserting a path on them would INVENT an expectation. We validate only
            // what the catalog actually declares.
            CheckAbilities(failures, log);

            // --- ENEMIES (enemies.json -> EnemyCatalog) ----------------------------
            // This is the catalog that carries the #22 archer->lumber CLASS of bug: an
            // entry's id resolves (via EnemyFactory.ModelForEnemy) to a MODEL PATH, and
            // a wrong/missing path silently degrades to a tinted capsule at runtime
            // (EnemyFactory.cs:100-114 fallback) — varied ids, one look, no error. We
            // load the catalog through the same CanonicalJson bytes WaveDataLoader reads,
            // then for EVERY enemy resolve its model the way the factory does and assert the
            // runtime seam DeNelle.Core.EnemyAssetLoader.LoadEnemyPrefab("<model>") returns a
            // real prefab. The seam (Addressables-FIRST, Resources-FALLBACK) is asked instead
            // of Resources.Load directly so this assertion stays true across the
            // Assets/EnemyContent -> Addressables migration; a raw Resources.Load would
            // null out for the whole roster the day the art moves (a false red), and asking
            // the loader proves MORE: that the real load path resolves.
            CheckEnemies(failures, log);

            // --- WAVE-SCALING (CITY-01 / CITY-06) + KILL REWARDS (BLIND-03-01) -----
            // Proves the most-played mode (a) escalates per wave (the runtime DEFAULT
            // WaveScalingCurve applies a multiplier >1 past wave 1 - the fallback
            // WaveManager.EnsureScalingCurve now always creates) and (b) pays progression
            // (every enemies.json row carries xp+coin rewards). Both were DEAD in the audit.
            CheckWaveScaling(failures, log);

            // --- STRUCTURES (structures-catalog.json -> CatalogRegistry) -----------
            // The build-mode tower/structure catalog. Each CatalogEntry.visualPrefabPath
            // is a Resources-relative prefab path that StructureFactory.Create feeds to
            // VisualFactory.Skin -> Resources.Load<GameObject>. A path that loads null is
            // EXACTLY the archer->lumber class (a tower wired to the wrong/missing visual)
            // — caught here as a FAIL naming the entry + path. Parsed identically to the
            // real CatalogBootstrap.LoadFromJson (StringEnumConverter + ignore-null/miss).
            CheckStructures(failures, log);

            // --- SINGLETON + BAKED-TWIN INTEGRITY (StructureSingleton v2) ----------
            // Owner only-ever-one ruling: a catalog row with repo.singleton=true (plus
            // repo.bakedTwins when a legacy baked twin exists) must be FULLY enforced
            // with ZERO code. Gates: (a) bakedTwins shape (non-empty, unique across the
            // catalog, only on singleton rows); (b) singleton+bakedTwins field parity
            // between the StreamingAssets source and the Resources copy; (c) every
            // migration-census (bakedName,itemId) pair on a singleton row is listed in
            // that row's bakedTwins (+ the barracks pin); (d) CatalogRegistry.All()
            // exists and BarracksNpcInjector carries no bespoke standdown seam.
            CheckSingletons(failures, log);

            // --- BLANK-TOWN BAKED-TWIN GATE (WO-834) -------------------------------
            // Owner F8 seq 592: a "Build Your Own" founding loaded FULL of baked
            // default-town structures. Gates: (a) the pure surfacing rule's truth
            // table (blank+migrated suppresses; pre-migration and ever-built surface);
            // (b) the v35->v36 migrator seed (blank save seeds EMPTY; established save
            // gets BaseLayout + FreeBuildsUsed + the template grant); (c) source-lint
            // that every surfacing path carries the gate.
            CheckBlankTownGate(failures, log);

            // --- NPC MODEL BINDING (WO-818: repo.npcModel -> KayKit body) ----------
            // Owner mapping table 2026-08-01: 12 structure rows author repo.npcModel
            // (a KayKit slug) that the NPC injectors resolve as Resources/NPCs/KayKit/
            // <slug>. Gates: (a) npcModel field parity between the two catalog copies;
            // (b) every authored slug resolves to a STAGED FBX (a typo would warn +
            // People-fallback every load); (c) the 12 owner-approved rows carry the
            // owner's slugs VERBATIM (creative pick is owner-only).
            CheckNpcModels(failures, log);

            // --- ASSET-MOVE MANIFEST (Addressables migration, 2026-08-17) ----------
            // MOVED INSIDE THE FENCE (WO-1496, 2026-09-06). The call used to sit HERE, and the
            // reasoning that put it here was right about the wrong thing: registering it stopped
            // it being a menu item nobody runs, but ABOVE the START FENCE it ran UNCOUNTED - its
            // [move-manifest] line was absorbed into the pre-fence baseline and no `.Run(out`
            // call-site existed for the pinned denominator. It is now a registered suite line
            // like every other, below the fence. Do not re-add a call here.

            // --- BUILDINGS (buildings.json -> BuildingCatalog) ---------------------
            // Load through the real loader; assert non-zero + non-empty id/displayName.
            // NOTE (conservative): BuildingDef.Model is a KayKit mesh KEY, NOT a
            // Resources path — gameplay buildings render through the structures catalog /
            // build pipeline, never via Resources.Load(Model). So we do NOT assert a path
            // load on Model (that would invent an expectation the catalog doesn't declare).
            CheckBuildings(failures, log);

            // --- POPULATION MILESTONES (population-milestones.json -> PopulationMilestonesCatalog) ---
            // WO-587: the milestone TABLE that drives Echo workforce slot unlocks. Assert the JSON
            // maps to >0 milestones, echo slots ascend 2..5 with NO gaps, and every entry carries at
            // least one real condition — so an owner edit can't silently break the unlock cadence.
            CheckPopulationMilestones(failures, log);

            // --- BARRACKS & TROOP UPGRADE PROGRESSION (WO-771.9) -------------------
            // Source-lint the committed barracks.json + troop-upgrades.json against
            // troops.json: the barracks ladder is contiguous (level 1 free, no gaps),
            // every unlocksTroopId resolves, the unlock encodings RECONCILE (barracks
            // level N lists exactly the troop whose UnlockBarracksTier == N), and every
            // upgrade curve starts at the 1.0 baseline. Emits BARRACKS_PROGRESSION_OK.
            CheckBarracksProgression(failures, log);

            // --- GAME GUIDE (guide-content.json -> GuideContentCatalog) ------------
            // WO-588: the opt-in tutorial codex content. Load through the real loader; assert
            // the JSON maps to >0 sections and every section carries a non-empty id/tab/title
            // and at least one non-empty body paragraph — so a content edit can't ship a blank
            // tab or an empty body to the guide panel.
            CheckGuideContent(failures, log);

            // --- DIALOGUE SPEAKER CARDS (dialogues.json speakers block) -------------
            // Owner-ratified card standard (2026-07-02 audit): every NPC dialogue card shows
            // name + guild/shop AFFILIATION + portrait. Assert every spoken line's speaker
            // resolves to a speakers-block record with a non-empty name + affiliation, and
            // every DECLARED portrait path (speakers block AND legacy per-node `portrait`
            // command args) loads a NON-NULL sprite — a dangling portrait path fails the gate.
            // An EMPTY portrait is legal by design (styled silhouette fallback in DialogueView).
            CheckDialogueSpeakers(failures, log);

            // --- ITEM-MODEL CAPABILITY INVARIANTS (WO-Item-1, docs/ITEM_MODEL.md §2c) ---
            // OWNER-RATIFIED 2026-06-18: the model invariants live in the regression test,
            // not just the doc — so every change/regen is gated by data, not faith. HARD
            // asserts on the resolved capability flags + a SOFT prefabPath coverage count
            // (WO-Item-2's generator fills those — do NOT fail on them yet).
            CheckItemCapabilities(weapons, armors, failures, log);
            CheckCraftingChain(failures, log);
            CheckJewelerChain(failures, log);
            CheckTalentLayout(failures, log);

            // --- ARMED-HERO INVARIANT (WO-Item Addressables equip) -----------------
            // At scale (433+ weapons, Blink Addressable-keyed) BestWeapon(job,1) may now
            // return a weapon whose prefab is an Addressable key. If neither the Addressable
            // key resolves NOR the EquipmentController's Resources map yields an attachable
            // mesh, the hero spawns UNARMED (WO-425 regression). This is the permission gate
            // that the armed-hero invariant holds at scale: for each class the level-1 auto-
            // equip is non-null AND its prefab reference resolves.
            CheckArmedHeroInvariant(failures, log);

            // --- WO-996: armor dual-copy — Resources (curated) ⊆ StreamingAssets (library) ---
            CheckArmorDualCopy(failures, log);

            // --- WO-975: Gear.asset GUIDs resolve on disk (not dangling / gitignored hollow) ---
            CheckGearAddressableGroup(failures, log);

            // --- HAND-SLOT EQUIP RULES (owner 2026-06-18, docs/STORE_EQUIP_SPEC.md) -
            // Drive the REAL GearLoadout equip flow on a throwaway GameObject and assert the
            // mutually-exclusive main-hand/off-hand rules hold: a 2H clears the off-hand; an
            // off-hand clears a 2H main; a 1H + shield coexist; the swap never leaves the hero
            // unarmed when a 1H exists. Exercises the actual enforcement, not a re-derivation.
            CheckHandSlotRules(failures, log);

            // --- BATTLE CLOSING (WO-505) — victory/defeat audio + star rating ------
            // Two provable bones from the silent-climax gap: (a) the victory + defeat
            // music clips resolve to a NON-NULL AudioClip through the SAME Resources path
            // AudioBootstrap uses (Resources.Load<AudioClip>("victory"/"defeat")) — this
            // catches the silent-track bug class (e.g. Resources.Load("dungeon") == null);
            // (b) BattleStarRating computes the right tier + multiplier for sample durations.
            CheckBattleClosing(failures, log);

            // --- WEAPON SWING-TRAIL VFX (WO-504 slice 3) ---------------------------
            // The Knight swings one shared mesh, so the rarity must read through the
            // swing-trail color/width. Assert the pure WeaponVfxMap resolver returns a
            // DISTINCT color per band, the gold const at legendary, the steel default
            // for null, and a MONOTONICALLY escalating width. Bones — owner felt-tunes
            // the exact colors later; this gates the MAPPING, not the aesthetic.
            CheckWeaponVfx(failures, log);

            // --- ACCESSORIES (accessories.json -> GearCatalog.Accessories) ---------
            // WO-543: the third gear category (rings + amulets). Assert the JSON maps to 10
            // AccessoryDef objects, the (additive) stat bonuses stay within the non-legendary
            // caps (damageMult < 0.20, defense < 0.15), and every entry carries an iconPath
            // (the shop/equip sprite) — the same display-field gate the weapon/armor checks use.
            CheckAccessories(failures, log);

            // --- VENDOR STOCK QUERIES (vendors.json -> VendorRegistry/VendorStockResolver) ---
            // WO-598 "the honest shelf": every registered vendor's query must resolve >=1 item
            // OR carry an authored emptyLine (never a raw empty grid); no roster-unobtainable
            // class (Mage under Knight-only V1) may appear in ANY vendor result; and each
            // trade's result stays inside its declared bands (Market never weapons, Jeweler
            // never armor, Forge never consumables).
            CheckVendorStock(failures, log);

            // --- ARMOR/ACCESSORY RIM-LIGHT VFX (WO-543 ArmorVfxMap) ----------------
            // The armor/accessory rarity must read through the hero rim-light glow. Assert the
            // pure ArmorVfxMap resolver returns a DISTINCT color per band, the gold const at
            // legendary, common == OFF (intensity 0), and a MONOTONICALLY escalating intensity.
            CheckArmorVfx(failures, log);

            // --- ENEMY STRUCTURE-AWARE SWEEP (ff.enemystructureaware) ---------------
            // Closes the UNVERIFIED targeting item (commit 8aa24c32): the verify-capture
            // showed 0 sweep acquires. Construct a REAL Enemy + a side structure (so the
            // forward probe misses and only the all-direction sweep can catch it) and drive
            // the REAL ProbeForStructure across three cases — proving from data, not faith,
            // that the sweep fires (no hero), stays suppressed (hero in aggro), and is inert
            // when the flag is off (reversible).
            CheckEnemyStructureSweep(failures, log);

            // =====================================================================
            //  >>> REGISTERED ORACLE SUITES — START FENCE <<<
            // ---------------------------------------------------------------------
            // Everything between this fence and the END fence is ONE registered
            // oracle suite per line: `Class.Run(out reason)` -> failures.Add(reason)
            // on red, `log.AppendLine("[tag] " + reason)` on green.
            //
            // The two counters below make the verdict marker SELF-DESCRIBING
            // (REGRESSION_OK <n>/<n> suites). Without a count in the marker, a small
            // suite's log reads identically to this one's — which is exactly how the
            // check-in gate ran the 22-case legacy battery for months while every
            // RESULT file claimed the full set had passed. See RegressionMarkerRegression.
            //
            // *** ADD NEW SUITE REGISTRATIONS ABOVE THE END FENCE, NOT BELOW IT. ***
            // A line added below the end fence still RUNS but is not COUNTED.
            // =====================================================================
            int suiteTagLinesBefore = CountOracleTagLines(log);
            int suiteSkipLinesBefore = CollectSkippedSuiteTags(log).Count;
            int suiteFailuresBefore = failures.Count;

            // --- FLAG HYGIENE (WO-1540): snapshot every ff.* PlayerPrefs key before the
            //     registered suites run, and restore + DIFF it after the END fence. A suite
            //     that sets a feature flag and does not restore it changes the environment
            //     for every LATER suite in this run AND for the owner's next editor session
            //     (PlayerPrefs in batchmode is process-external state), which makes results
            //     depend silently on run ORDER. This is the fence-level half of the fix and
            //     it is honest about its limit: it DETECTS and RESTORES drift, it does not
            //     isolate suite N from suite N-1 — the region below is ~200 flat
            //     `if (!X.Run(out var r))` lines, not a loop, so per-suite wrapping would
            //     mean editing every registration and is a separate, larger call. Do NOT
            //     hardcode a flag list here; FeatureFlagSnapshot derives the key set from
            //     FeatureFlags.cs itself so a flag added tomorrow is watched automatically.
            var flagSnapshotBefore = DeNelle.Editor.Regression.FeatureFlagSnapshot.Capture();

            // --- monetization covenant gate (LB-5) + tower upgrade perks (overnight silos C/E) ---
            if (!MonetizationCovenantRegression.Run(out var covReason)) failures.Add(covReason); else log.AppendLine("[covenant] " + covReason);
            // --- WORK_ORDER_battle_and_monthly_packs: the pay-to-win firewall of the Battle Pass +
            //     Monthly Ledger families, as a BUILD GATE. Sits beside the covenant gate on purpose
            //     — it is the same promise, policed over the two reward tables PackDef cannot hold.
            //     It polices TWO OPPOSITE failures: what may not be GRANTED (combat power, a sold
            //     tier, bought XP, a glimmer line, a randomized reward) and what cannot be DELIVERED
            //     (a cosmetic while no cosmetic art exists in the tree, an skr credit while no
            //     ledger exists anywhere). Plus three STRUCTURAL pins no value check could replace:
            //     the loader actually invokes the firewall, Battle XP has exactly ONE door and it
            //     takes a battle outcome rather than an amount, and the Monthly Ledger carries no
            //     countdown because under the pool model nothing expires.
            //     ⛔ REGISTERED EXACTLY ONCE. Do not add a second line for it near the end fence. ---
            if (!DeNelle.Editor.Regression.BattleMonthlyRegression.Run(out var battleMonthlyReason)) failures.Add(battleMonthlyReason); else log.AppendLine("[battle-monthly] " + battleMonthlyReason);
            if (!TowerPerkRegression.Run(out var towerPerkReason)) failures.Add(towerPerkReason); else log.AppendLine("[tower-perks] " + towerPerkReason);
            // --- F8 open-ticket oracles (data-decidable roots, seconds-fast) ------
            if (!TowerRespawnRegression.Run(out var towerRespawnReason)) failures.Add(towerRespawnReason); else log.AppendLine("[tower-respawn] " + towerRespawnReason);
            if (!DeNelle.Editor.Regression.HubSceneLiteralRegression.Run(out var hubLiteralReason)) failures.Add(hubLiteralReason); else log.AppendLine("[hub-scene-literal] " + hubLiteralReason);
            if (!DefenseTargetableRegression.Run(out var defTargetReason)) failures.Add(defTargetReason); else log.AppendLine("[def-target] " + defTargetReason);
            if (!ArenaPrefabAuditRegression.Run(out var arenaReason)) failures.Add(arenaReason); else log.AppendLine("[arena-prefab] " + arenaReason);
            // --- Wave-1 full-coverage oracles (docs/FULL_COVERAGE_PLAN_2026-07-08.md) ---
            if (!CoreDataHubRegression.Run(out var coreDataHubReason)) failures.Add(coreDataHubReason); else log.AppendLine("[core-datahub] " + coreDataHubReason);
            if (!CoreCatalogRegression.Run(out var coreCatalogReason)) failures.Add(coreCatalogReason); else log.AppendLine("[core-catalog] " + coreCatalogReason);
            if (!CoreWorldLogicRegression.Run(out var coreWorldReason)) failures.Add(coreWorldReason); else log.AppendLine("[core-world] " + coreWorldReason);
            if (!CoreSaveContractRegression.Run(out var coreSaveReason)) failures.Add(coreSaveReason); else log.AppendLine("[core-save] " + coreSaveReason);
            if (!HeroProgressionRegression.Run(out var heroProgReason)) failures.Add(heroProgReason); else log.AppendLine("[hero-prog] " + heroProgReason);
            if (!AegisSetReachabilityRegression.Run(out var aegisReason)) failures.Add(aegisReason); else log.AppendLine("[aegis] " + aegisReason);
            if (!BuildingUpgradeRegression.Run(out var buildUpgReason)) failures.Add(buildUpgReason); else log.AppendLine("[build-upgrade] " + buildUpgReason);
            if (!OfflineHarvestRegression.Run(out var offlineReason)) failures.Add(offlineReason); else log.AppendLine("[offline-harvest] " + offlineReason);
            if (!OfflineClaimFanOutRegression.Run(out var offlineFanOutReason)) failures.Add(offlineFanOutReason); else log.AppendLine("[offline-fanout] " + offlineFanOutReason);
            // --- WO-1026 PvE siege / the defence consequence loop. Three oracles, one lane:
            //     the record CONTRACT (incl. the model-(c) source-swap proof and the ⛔ all-zero
            //     UNRULED stakes guard), the CADENCE (incl. the WO-1147 "never write
            //     LastHarvestClaimMs" invariant), and the DUPLICATE-AUTHORITY lint (the siege
            //     never spawns; WaveManager stays the single town-attack authority). ---
            if (!DefenseReportContractRegression.Run(out var defReportReason)) failures.Add(defReportReason); else log.AppendLine("[defense-report] " + defReportReason);
            if (!SiegeCadenceRegression.Run(out var siegeCadenceReason)) failures.Add(siegeCadenceReason); else log.AppendLine("[siege-cadence] " + siegeCadenceReason);
            if (!SiegeSpawnAuthorityRegression.Run(out var siegeAuthorityReason)) failures.Add(siegeAuthorityReason); else log.AppendLine("[siege-spawn-authority] " + siegeAuthorityReason);
            if (!LookoutAlertRegression.Run(out var lookoutAlertReason)) failures.Add(lookoutAlertReason); else log.AppendLine("[lookout-alert] " + lookoutAlertReason);
            if (!SiegeLossStakesRegression.Run(out var siegeStakesReason)) failures.Add(siegeStakesReason); else log.AppendLine("[siege-loss-stakes] " + siegeStakesReason);
            // --- WO-1128: every offline window must DECLARE which clock produced it and its own
            //     endpoints, so api/game/save.js can reconcile it against the server's elapsed
            //     time. The server-side clamp itself is JavaScript and is gated separately by
            //     `node api/game/save.js` (marker ACCRUAL_RECONCILE_OK) — see that oracle's header.
            if (!OfflineAccrualTrustRegression.Run(out var accrualTrustReason)) failures.Add(accrualTrustReason); else log.AppendLine("[accrual-trust] " + accrualTrustReason);
            // --- Dev queue time-skip (owner 2026-08-04): the skip is exact/additive/resettable,
            //     isolated from the WO-120 ServerOffsetMs lane, forward-only, release-stripped —
            //     and, the load-bearing one, COMBAT STILL READS NO TimeSource (so it can never
            //     warp the battle timer, which is the owner's whole constraint). ---
            if (!DevTimeSkipRegression.Run(out var devSkipReason)) failures.Add(devSkipReason); else log.AppendLine("[dev-time-skip] " + devSkipReason);
            if (!VillageEconomyRegression.Run(out var villEconReason)) failures.Add(villEconReason); else log.AppendLine("[village-econ] " + villEconReason);
            if (!ArenaCatalogRegression.Run(out var arenaCatReason)) failures.Add(arenaCatReason); else log.AppendLine("[arena-cat] " + arenaCatReason);
            if (!CompanionRosterRegression.Run(out var compRosterReason)) failures.Add(compRosterReason); else log.AppendLine("[companion-roster] " + compRosterReason);
            // --- WO-736: Barracks 7-type troop roster + tier-unlock ladder (program 732-737 close) ---
            if (!TroopRosterRegression.Run(out var troopRosterReason)) failures.Add(troopRosterReason); else log.AppendLine("[troop-roster] " + troopRosterReason);
            // --- WO-771.6/771.11: raid V1 win/stars/loot + live HUD (LOCKED teleport/deploy loop) ---
            if (!RaidScoringRegression.Run(out var raidScoringReason)) failures.Add(raidScoringReason); else log.AppendLine("[raid-scoring] " + raidScoringReason);
            // --- WO-912 sec.10.5: the ad provider stays BEHIND IAdService (registered BEFORE any SDK) ---
            if (!AdServiceSeamRegression.Run(out var adSeamReason)) failures.Add(adSeamReason); else log.AppendLine("[ad-seam] " + adSeamReason);
            // --- WO-1320: a Pi rewarded ad pays out ONLY after /api/pi/ads-verify answers
            //     mediator_ack_status == "granted". Pins the two cases that matter: an unverified
            //     client-side AD_REWARDED grants nothing, and AD_CLOSED grants nothing (the latent
            //     always-true defect this WO was minted for). ---
            //     Namespace is DeNelle.Editor (NOT DeNelle.Editor.Regression) - read from the suite
            //     file itself, not from a neighbour line; this folder holds both conventions. ---
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "pi-ad-reward suite", () => { if (!DeNelle.Editor.PiAdRewardVerificationRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[pi-ad-reward] " + r); });
            if (!AndroidContentTargetRegression.Run(out var androidTargetReason)) failures.Add(androidTargetReason); else log.AppendLine("[android-content-target] " + androidTargetReason);
            // --- WO-1187: every repo .ps1 is ASCII-or-BOM and parses non-vacuously (a BOM-less
            //     non-ASCII script is read as ANSI by PS 5.1 and can silently never run) ---
            if (!PowerShellEncodingRegression.Run(out var ps1EncodingReason)) failures.Add(ps1EncodingReason); else log.AppendLine("[ps1-encoding] " + ps1EncodingReason);
            if (!DeNelle.Editor.Regression.SoftlockClassifierRegression.Run(out var softlockClassifierReason)) failures.Add(softlockClassifierReason); else log.AppendLine("[softlock-classifier] " + softlockClassifierReason);
            if (!BattleQuiescenceRegression.Run(out var quiescenceReason)) failures.Add(quiescenceReason); else log.AppendLine("[battle-quiescence] " + quiescenceReason);
            if (!KnightDirectionalDeathRegression.Run(out var knightDeathReason)) failures.Add(knightDeathReason); else log.AppendLine("[knight-directional-death] " + knightDeathReason);
            if (!DeNelle.Editor.Regression.ArmyMusterLayoutRegression.Run(out var armyMusterLayoutReason)) failures.Add(armyMusterLayoutReason); else log.AppendLine("[army-muster-layout] " + armyMusterLayoutReason);
            if (!StructureSeatRegression.Run(out var seatReason)) failures.Add(seatReason); else log.AppendLine("[structure-seat] " + seatReason);
            if (!StructureCadenceRegression.Run(out var cadenceReason)) failures.Add(cadenceReason); else log.AppendLine("[structure-cadence] " + cadenceReason);
            if (!StructureLoadBoundedRegression.Run(out var loadBoundedReason)) failures.Add(loadBoundedReason); else log.AppendLine("[structure-load-bounded] " + loadBoundedReason);
            if (!DeNelle.Editor.Regression.StructureFactoryResidencyRetryRegression.Run(out var residencyRetryReason)) failures.Add(residencyRetryReason); else log.AppendLine("[structure-factory-residency-retry] " + residencyRetryReason);
            if (!SheathePoseRegression.Run(out var sheatheReason)) failures.Add(sheatheReason); else log.AppendLine("[sheathe-pose] " + sheatheReason);
            if (!OfflinePullRegression.Run(out var offlinePullReason)) failures.Add(offlinePullReason); else log.AppendLine("[offline-pull] " + offlinePullReason);
            if (!EnemyLoadBoundedRegression.Run(out var enemyBoundedReason)) failures.Add(enemyBoundedReason); else log.AppendLine("[enemy-load-bounded] " + enemyBoundedReason);
            if (!ContentPackingRegression.Run(out var packingReason)) failures.Add(packingReason); else log.AppendLine("[content-packing] " + packingReason);
            if (!DungeonCameraFeelRegression.Run(out var dungeonCamReason)) failures.Add(dungeonCamReason); else log.AppendLine("[dungeon-camera-feel] " + dungeonCamReason);
            if (!DungeonMovementOwnerRegression.Run(out var dungeonMoveReason)) failures.Add(dungeonMoveReason); else log.AppendLine("[dungeon-movement-owner] " + dungeonMoveReason);
            if (!EnemyTintRegression.Run(out var enemyTintReason)) failures.Add(enemyTintReason); else log.AppendLine("[enemy-tint] " + enemyTintReason);
            if (!EnemyBodyTextureRegression.Run(out var bodyTexReason)) failures.Add(bodyTexReason); else log.AppendLine("[enemy-body-texture] " + bodyTexReason);
            // --- WO-912 sec.9.3 + D4/D7: no ad reward may ever grant a real-money currency ---
            if (!AdPlacementCovenantRegression.Run(out var adCovReason)) failures.Add(adCovReason); else log.AppendLine("[ad-covenant] " + adCovReason);
            // --- WO-976: the `hasSurface` false green stays dead — each of the four visibility
            //     failure classes must still be able to FIRE, and the named skip is not a pass ---
            if (!UiSurfaceProbeRegression.Run(out var uiSurfaceReason)) failures.Add(uiSurfaceReason); else log.AppendLine("[ui-surface-probe] " + uiSurfaceReason);
            // --- WO-935/991/910/994: CombatCast + caravan mobility + Hunter mark + shield port ---
            if (!CombatCastCaravanMarkRegression.Run(out var castCaravanReason)) failures.Add(castCaravanReason); else log.AppendLine("[combat-cast-caravan-mark] " + castCaravanReason);
            if (!HeroElementCastVfxRegression.Run(out var heroElementCastReason)) failures.Add(heroElementCastReason); else log.AppendLine("[hero-element-cast] " + heroElementCastReason);
            if (!TownsfolkDialogueRegression.Run(out var townsfolkReason)) failures.Add(townsfolkReason); else log.AppendLine("[townsfolk] " + townsfolkReason);
            if (!AtbEngineRegression.Run(out var atbReason)) failures.Add(atbReason); else log.AppendLine("[atb-engine] " + atbReason);
            if (!EconomyMetaCatalogRegression.Run(out var econMetaReason)) failures.Add(econMetaReason); else log.AppendLine("[econ-meta] " + econMetaReason);
            if (!GlimmerEconomyRegression.Run(out var glimmerReason)) failures.Add(glimmerReason); else log.AppendLine("[glimmer] " + glimmerReason);
            if (!SceneRoutingRegression.Run(out var sceneRouteReason)) failures.Add(sceneRouteReason); else log.AppendLine("[scene-route] " + sceneRouteReason);
            // --- WO-1109: the raid hero is the CARRIED town hero, not the emergency fallback.
            //     Every raid entry used to land an "EMERGENCY pill spawned" FlowTrace.Fail in the
            //     break-log (SceneRouter.GoRaid carried nothing), which trained every seat to
            //     ignore Hero Fails. Pins the carry, the DDOL re-home (leak guard), and — just as
            //     hard — that the Fail alarm and its fallback both SURVIVED the fix. ---
            if (!RaidHeroCarryRegression.Run(out var raidHeroCarryReason)) failures.Add(raidHeroCarryReason); else log.AppendLine("[raid-hero-carry] " + raidHeroCarryReason);
            if (!ComposedDungeonRunRegression.Run(out var composedRunReason)) failures.Add(composedRunReason); else log.AppendLine("[composed-dungeon-run] " + composedRunReason);
            // --- WO-1131: the hero Ensure() operates on is the SURVIVOR of the dedupe, never an
            //     object it just destroyed. Destroy is DEFERRED to end of frame, so the old
            //     "DedupeHeroes(); then FindLoco();" pair re-read a mid-mutation world and handed
            //     Ensure the doomed hero — whose root scene is the destination, not DDOL, so the
            //     carried-hero guard went FALSE, TryRecoverCarriedHero never ran, and the real
            //     hero kept its TOWN pose ~130m outside dg_hollow_roads (owner F8 seq 3587). ---
            if (!HeroDedupeSurvivorRegression.Run(out var heroDedupeReason)) failures.Add(heroDedupeReason); else log.AppendLine("[hero-dedupe-survivor] " + heroDedupeReason);
            if (!ArtResourceRegression.Run(out var artResReason)) failures.Add(artResReason); else log.AppendLine("[art-resource] " + artResReason);
            // --- WO-682: Sfx WebGL import invariant (no divergent WebGL overrides -> no FSB decode failures) ---
            if (!SfxWebglAudioRegression.Run(out var sfxWebglReason)) failures.Add(sfxWebglReason); else log.AppendLine("[sfx-webgl] " + sfxWebglReason);
            // --- 2026-07-12 SME suites (owner: "a SME per architect path, full suite each") ---
            if (!CoreSaveRegression.Run(out var coreSaveSmeReason)) failures.Add(coreSaveSmeReason); else log.AppendLine("[core-save-sme] " + coreSaveSmeReason);
            if (!BuildEconomyRegression.Run(out var buildEconReason)) failures.Add(buildEconReason); else log.AppendLine("[build-econ] " + buildEconReason);
            if (!ObsidianQueueRegression.Run(out var obsidianQueueReason)) failures.Add(obsidianQueueReason); else log.AppendLine("[obsidian-queue] " + obsidianQueueReason);
            // --- LANE D: mercenary hire system (gold skips training time) ---
            if (!BuildTimerMercenaryRegression.Run(out var mercenaryReason)) failures.Add(mercenaryReason); else log.AppendLine("[mercenary-hire] " + mercenaryReason);
            // --- WO-897: army composition musters the whole build-out onto the EXISTING Train queue
            //     (no second queue) and never silently drops what does not fit the five-per-line cap ---
            if (!ArmyMusterRegression.Run(out var armyMusterReason)) failures.Add(armyMusterReason); else log.AppendLine("[army-muster] " + armyMusterReason);
            // --- PROD-013 (2026-08-20): the Manage screen's Troops tab is the ONE door to troop
            //     training and it must actually open. PROD-002 closed the barracks talk-door on the
            //     premise that "Manage owns training" while Manage emitted UPGRADE rows only, so the
            //     entire (fully built, fully suite-covered) training stack became unreachable and no
            //     existing suite noticed — every one tested a LAYER, none tested the DOOR ---
            if (!ManageTroopsTrainDoorRegression.Run(out var manageTrainDoorReason)) failures.Add(manageTrainDoorReason); else log.AppendLine("[manage-train-door] " + manageTrainDoorReason);
            // WO-1566/1567 - the nine Manage screens vs the owner mockup (fills screen, hub, tiles, detail, picker). Registered by the lead 2026-09-07.
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "manage-mockup-conformance suite", () => { if (!ManageMockupConformanceRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[manage-mockup-conformance] " + r); });
            if (!ManageProgressiveDisclosureRegression.Run(out var manageDisclosureReason)) failures.Add(manageDisclosureReason); else log.AppendLine("[manage-progressive-disclosure] " + manageDisclosureReason);
            // The two DOOR oracles for the owner's 2026-08-30 felt-test ("I do not see a way to get
            // to Skill Tree now" / "no way from manage or anywhere else to the upgrade defensive
            // screen"). Both sit beside the train-door suite deliberately: all three exist because a
            // whole feature became unreachable while every LAYER suite over it kept passing.
            if (!HeroSkillTreeDoorRegression.Run(out var skillTreeDoorReason)) failures.Add(skillTreeDoorReason); else log.AppendLine("[skill-tree-door] " + skillTreeDoorReason);
            // --- WO-1410 Hero screens: ONE source for BAG / SKILLS / LOADOUT (canon-strings twins via HudStrings), Wisdom copy, Loadout-only socket ownership (Codex dev lane) ---
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "hero-name-single-source suite", () => { if (!DeNelle.Editor.Regression.HeroNameSingleSourceRegression.Run(out var rHns)) failures.Add(rHns); else log.AppendLine("[hero-name-single-source] " + rHns); });
            if (!ManageDefenseUpgradeDoorRegression.Run(out var manageDefenseDoorReason)) failures.Add(manageDefenseDoorReason); else log.AppendLine("[manage-defense-door] " + manageDefenseDoorReason);
            // --- 2026-08-07: two fixes that shipped WITHOUT a pin, both "must never come back":
            //     the rewarded-ad stub that GRANTED THE REWARD with no SDK (a free timer skip on
            //     every channel), and the arena home-return that lived on a UI object three paths
            //     destroy without firing - which stranded the owner 7km out on BOTH platforms ---
            if (!AdGateAndArenaReturnRegression.Run(out var adArenaReason)) failures.Add(adArenaReason); else log.AppendLine("[ad-gate-arena] " + adArenaReason);
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "arena-return-music suite", () => { if (!DeNelle.Editor.Regression.ArenaReturnMusicRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[arena-return-music] " + r); });
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "addressable-troop-visual suite", () => { if (!DeNelle.Editor.Regression.AddressableTroopVisualRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[addressable-troop-visual] " + r); });
            // --- WO-781: wounded-troop recovery advance (TickRecovery live+offline callers) ---
            if (!ArmyRecoveryRegression.Run(out var troopRecoveryReason)) failures.Add(troopRecoveryReason); else log.AppendLine("[troop-recovery] " + troopRecoveryReason);
            if (!DataWebRegression.Run(out var dataWebReason)) failures.Add(dataWebReason); else log.AppendLine("[data-web] " + dataWebReason);
            if (!HudUiRegression.Run(out var hudUiSmeReason)) failures.Add(hudUiSmeReason); else log.AppendLine("[hud-ui-sme] " + hudUiSmeReason);
            // --- WO-1436 (owner ruling 2026-09-06): the hero ABILITY ROW owns the thumb band at
            //     the bottom of the screen; the raid deploy bar stacks ABOVE it. Two surfaces in
            //     two assemblies that cannot reference each other were authored into the SAME
            //     band, and the one 26 000 layers above buried the one the player fights with ---
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "raid-thumb-band suite", () => { if (!DeNelle.Editor.Regression.RaidHudThumbBandRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[raid-thumb-band] " + r); });
            if (!CombatAtbRegression.Run(out var combatAtbReason)) failures.Add(combatAtbReason); else log.AppendLine("[combat-atb] " + combatAtbReason);
            if (!DialogueRegression.Run(out var dialogueReason)) failures.Add(dialogueReason); else log.AppendLine("[dialogue] " + dialogueReason);
            if (!EnemyRigColorRegression.Run(out var enemyRigColorReason)) failures.Add(enemyRigColorReason); else log.AppendLine("[enemy-rig-color] " + enemyRigColorReason);
            // --- WO-772 Phase 1: EnemyResolver id->family->DISTINCT model (generic-skeleton fix, ENEMY_RESOLVER_OK) ---
            if (!EnemyResolverRegression.Run(out var enemyResolverReason)) failures.Add(enemyResolverReason); else log.AppendLine("[enemy-resolver] " + enemyResolverReason);
            // --- Resources -> Addressables migration guard: the enemy ADDRESSES must be in the
            //     catalog once Assets/EnemyContent is gone. Migration-state aware (a progress
            //     note pre-move, a hard assertion post-move); a DANGLING entry fails in either
            //     state. Without it a quietly-unmarked group ships a roster of tinted capsules ---
            if (!EnemyAddressableCatalogRegression.Run(out var enemyAddrCatalogReason)) failures.Add(enemyAddrCatalogReason); else log.AppendLine("[enemy-addr-catalog] " + enemyAddrCatalogReason);
            // --- 2026-07-26: retired walk-up outpost (ff.raidwalk) + ambient region roam (ff.regionroam OFF) ---
            if (!OverworldCombatGateRegression.Run(out var owCombatReason)) failures.Add(owCombatReason); else log.AppendLine("[overworld-combat-gate] " + owCombatReason);
            // --- destroyed-structure owner ruling (repair no-op + exclusion predicates; play-mode remove is note-only) ---
            if (!DestroyedStructureRegression.Run(out var destroyedStructReason)) failures.Add(destroyedStructReason); else log.AppendLine("[destroyed-structure] " + destroyedStructReason);
            // --- Village->HUD REPAIR REFLECTION CONTRACT (2026-08-24): the bridge binds
            //     ShowRepairPrompt/HideRepairPrompt/ShowRepairFeedback + the two command events
            //     by NAME across the asmdef gap, so nothing compiles the seam. It drifted, the
            //     prompt became a silent no-op, and a selected structure said "Repair?" with no
            //     way to confirm. This is the only automated detector that seam has. ---
            if (!RepairHudContractRegression.Run(out var repairHudReason)) failures.Add(repairHudReason); else log.AppendLine("[repair-hud-contract] " + repairHudReason);
            if (!RepairPromptReadabilityRegression.Run(out var repairPromptReadabilityReason)) failures.Add(repairPromptReadabilityReason); else log.AppendLine("[repair-prompt-readability] " + repairPromptReadabilityReason);
            if (!OrcRigBindingAudit.Run(out var orcBindingReason)) failures.Add(orcBindingReason); else log.AppendLine("[orc-binding] " + orcBindingReason);
            if (!HeroLocomotionClipRegression.Run(out var heroLocoClipReason)) failures.Add(heroLocoClipReason); else log.AppendLine("[hero-loco-clips] " + heroLocoClipReason);
            // --- UI-Obsidian conformance (style-everything-obsidian LAW): flags NEW hand-rolled uGUI vs baseline debt ---
            if (!UiObsidianConformanceRegression.Run(out var uiObsidianReason)) failures.Add(uiObsidianReason); else log.AppendLine("[ui-obsidian] " + uiObsidianReason);
            if (!UiMvvmConformanceRegression.Run(out var uiMvvmReason)) failures.Add(uiMvvmReason); else log.AppendLine("[ui-mvvm] " + uiMvvmReason);
            // --- UI-capture FIDELITY guard (2026-08-05): the headless capture harness was
            // geometry-BLIND — RenderCanvasToPng only rewrote canvas.scaleFactor and never
            // Screen.*, while the kit computes zone geometry AT BUILD TIME from Screen.*, so
            // every PNG shared ONE layout and the resolution in the filename was a LABEL, not
            // a layout. Two panels shipped broken behind a green UI_CAPTURE_OK. This is the
            // source-text ratchet that stops the fix being silently reverted; the live
            // geometry assertions run inside the harness itself. ---
            // Fully qualified: this suite lives in DeNelle.Editor.Regression, not DeNelle.Editor
            // (same as RuntimeSpawnVisualRegression below).
            if (!DeNelle.Editor.Regression.UiCaptureFidelityRegression.Run(out var uiCapFidelityReason)) failures.Add(uiCapFidelityReason); else log.AppendLine("[ui-capture-fidelity] " + uiCapFidelityReason);
            if (!HudPostureRegression.Run(out var hudPostureReason)) failures.Add(hudPostureReason); else log.AppendLine("[hud-posture] " + hudPostureReason);
            // --- WO-1436 P0 — the SCENE/POSTURE SEAM. HudPostureRegression above asks "does
            // the pursuit pulse arc behave?"; this asks the question none of the 394+ green
            // suites could: "is the resolved posture RIGHT for the scene the player is standing
            // in?" A raid scene resolving to a peaceful posture FAILS the build. ---
            if (!ScenePostureSeamRegression.Run(out var scenePostureReason)) failures.Add(scenePostureReason); else log.AppendLine("[scene-posture-seam] " + scenePostureReason);
            // --- WO-673 strategic placement — the §5 permission gates (flag-off parity,
            // migration round-trip, one-per-id, save v30, repair chain, 45° yaw + claim) ---
            if (!StrategicPlacementRegression.Run(out var stratPlaceReason)) failures.Add(stratPlaceReason); else log.AppendLine("[strategic-placement] " + stratPlaceReason);
            // --- WO-676 skill-tree strategic redesign — §C gates G1-G3 (data/dual-copy/
            // vocabulary + StatSum stacking/clamps + NO DEAD NODES consumer registry) ---
            if (!TalentStrategyRegression.Run(out var talentStratReason)) failures.Add(talentStratReason); else log.AppendLine("[talent-strategy] " + talentStratReason);
            // --- WO-738 echo per-echo specialization — §2c permission gate (roster identity +
            // balance dual-copy + token/legacy round-trip + bonus math + save v33 + EchoLaneBonuses) ---
            if (!EchoSpecializationRegression.Run(out var echoSpecReason)) failures.Add(echoSpecReason); else log.AppendLine("[echo-spec] " + echoSpecReason);
            // --- WO-745 Room Forge pipeline gate (catalog/dual-copy/mate/seal/drift/overlap + spine+demo green) ---
            if (!DeNelle.Editor.Regression.RoomForgeRegression.Run(out var roomForgeReason)) failures.Add(roomForgeReason); else log.AppendLine("[room-forge] " + roomForgeReason);

            // --- P1 FIX-PROOF SUITES (14) -- each PROVES one architect-plan P1 fix headless.
            // Guard.Try-wrapped so one bad suite logs + is skipped, never aborting the batch.
            // FAIL-BY-DESIGN suites (crystal-production, dungeon-dressing, modal-registration,
            // ftue-honesty) fail TRUTHFULLY today and flip green when their fix lands.
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "wave-scaling suite", () => { if (!WaveScalingRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[wave-scaling] " + r); });
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "enemy-rewards suite", () => { if (!EnemyRewardRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[enemy-rewards] " + r); });
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "wall-mitigation suite", () => { if (!WallHeartMitigationRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[wall-mitigation] " + r); });
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "pack-grant suite", () => { if (!PackGrantRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[pack-grant] " + r); });
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "builder-sku suite", () => { if (!DeNelle.Editor.Regression.BuilderSkuRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[builder-sku] " + r); });
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "temporary-builder suite", () => { if (!DeNelle.Editor.Regression.TemporaryBuilderRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[temporary-builder] " + r); });
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "card-collection-foundation suite", () => { if (!DeNelle.Editor.Regression.CardCollectionFoundationRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[card-collection-foundation] " + r); });
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "build-collection-player suite", () => { if (!DeNelle.Editor.Regression.BuildCollectionPlayerRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[build-collection-player] " + r); });
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "build-affordability-words suite", () => { if (!DeNelle.Editor.Regression.BuildAffordabilityWordsRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[build-affordability-words] " + r); });
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "post-wave-victory-modal suite", () => { if (!DeNelle.Editor.Regression.PostWaveVictoryModalRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[post-wave-victory-modal] " + r); });
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "night-market-shared-card suite", () => { if (!DeNelle.Editor.Regression.NightMarketSharedCardRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[night-market-shared-card] " + r); });
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "upgrade-authority suite", () => { if (!BuildingUpgradeAuthorityRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[upgrade-authority] " + r); });
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "queue-full-surface suite", () => { if (!UpgradeQueueFullSurfaceRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[queue-full-surface] " + r); });
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "upgrade-family suite", () => { if (!UpgradeFamilyPrecedenceRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[upgrade-family] " + r); });
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "dualfamily-level-reset suite", () => { if (!DualFamilyLevelResetRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[dualfamily-level-reset] " + r); });
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "crystal-production suite", () => { if (!CrystalProductionRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[crystal-production] " + r); });
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "sfx-resolve suite", () => { if (!SfxResolveRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[sfx-resolve] " + r); });
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "dungeon-exit suite", () => { if (!DungeonExitRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[dungeon-exit] " + r); });
            // WO-1568 - dungeon doors read as doors: leaf on the hinge, frame + lintel, no letterbox. Registered by the lead 2026-09-07.
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "dungeon-door-shape suite", () => { if (!DeNelle.Editor.Regression.DungeonDoorShapeRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[dungeon-door-shape] " + r); });
            // WO-1596 - earning the first Rough Stone is a full-screen moment, and the exit WAITS for it.
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "rough-stone-fanfare suite", () => { if (!DeNelle.Editor.Regression.RoughStoneFanfareRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[rough-stone-fanfare] " + r); });
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "dungeon-dressing suite", () => { if (!DungeonDressingRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[dungeon-dressing] " + r); });
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "dungeon-return suite", () => { if (!DungeonReturnSceneRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[dungeon-return] " + r); });
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "dungeon-lore suite", () => { if (!DungeonLoreReadableRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[dungeon-lore] " + r); });
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "dungeon-state-reset suite", () => { if (!DungeonStateResetRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[dungeon-state-reset] " + r); });
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "dungeon-defeat suite", () => { if (!DungeonDefeatEndsRunRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[dungeon-defeat] " + r); });
            // NOTE: tag was a DUPLICATE of the DungeonExitRegression line above ("[dungeon-exit]"),
            // so two different suites reported under one tag and one of them was invisible in the
            // log. Renamed to [dungeon-exit-reachable]. (The two suites ALSO share the
            // DUNGEON_EXIT_OK marker literal inside their own bodies — that is tracked as known
            // debt in RegressionMarkerRegression's allowlist; fixing it means editing those files.)
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "dungeon-exit-reachable suite", () => { if (!DungeonExitReachableRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[dungeon-exit-reachable] " + r); });
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "dungeon-defeat-realtime suite", () => { if (!DungeonRealtimeSettleRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[dungeon-defeat-realtime] " + r); });
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "dungeon-toast suite", () => { if (!DungeonToastRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[dungeon-toast] " + r); });
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "dungeon-fpv suite", () => { if (!DungeonFpvRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[dungeon-fpv] " + r); });
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "modal-registration suite", () => { if (!ModalArbiterRegistrationRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[modal-registration] " + r); });
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "founding-reach suite", () => { if (!FoundingReachabilityRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[founding-reach] " + r); });
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "ftue-honesty suite", () => { if (!FtueHonestyRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[ftue-honesty] " + r); });
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "echo-card-copy suite", () => { if (!EchoCardCopyRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[echo-card-copy] " + r); });
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "shader-pin suite", () => { if (!ShaderPinRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[shader-pin] " + r); });
            // --- WO-761: fire leaves a lingering burn on <=50% structures until repaired/destroyed ---
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "structure-burn suite", () => { if (!StructureBurnRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[structure-burn] " + r); });
            // --- audit P1 closers (owner 2026-07-20): EW-3 waves.json schema guard + ECON-1 pack->cosmetic grantability integrity ---
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "waves-schema suite", () => { if (!WavesSchemaRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[waves-schema] " + r); });
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "wave-authoring suite", () => { if (!WaveAuthoringLiveRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[wave-authoring] " + r); });
            // --- WO-808 Option A: gear power-level ladder data integrity ---
            if (!DeNelle.Editor.Regression.GeneratedFallbackParityRegression.Run(out var generatedFallbackReason)) failures.Add(generatedFallbackReason); else log.AppendLine(generatedFallbackReason);
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "gear-levels suite", () => { if (!GearLevelsRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[gear-levels] " + r); });
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "pack-cosmetic-integrity suite", () => { if (!PackCosmeticIntegrityRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[pack-cosmetic-integrity] " + r); });
            // --- WO-992 (2026-08-21): an EQUIPPED cosmetic reaches a real renderer. The pack/catalog suites above only ever checked DATA, and data was never the problem — CosmeticApplier.ApplyCosmetic was called from NOWHERE, so a player could earn or BUY Glimmer (packs.json sells it), purchase a skin, equip it, and see nothing change. Rule 1 reads the colour back off a live Renderer. ---
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "cosmetic-apply suite", () => { if (!DeNelle.Editor.Regression.CosmeticApplyRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[cosmetic-apply] " + r); });
            // --- WO-1129 (2026-08-21): the gate AssetRoots.cs:46 has claimed since 08-18 that it had ("AssetRootsRegression fails the build if the string reappears") and NEVER DID — 16 re-typed root literals were live in 14 files, two of them other regression suites. Also carries the §3.5 enemy-art token ratchet. ---
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "asset-roots suite", () => { if (!DeNelle.Editor.Regression.AssetRootsRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[asset-roots] " + r); });
            // --- WO-1485 (2026-09-07): textures were 82% of user assets, and 177.2 MB of that was ONE defect —
            // Android format Automatic + crunch falls back to uncompressed RGBA32 whenever the post-clamp
            // dimensions are not both multiples of 4, silently, on textures that DID carry an override.
            // Rule 1 hard-fails that state; rules 2-3 carry the no-override and duplicate-content debt as
            // frozen shrinking ledgers, because hard-failing 2114 files and 623 duplicate groups would make
            // this gate permanently red rather than useful. Registered by the lead. ---
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "texture-import-budget suite", () => { if (!DeNelle.Editor.Regression.TextureImportBudgetRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[texture-import-budget] " + r); });
            // --- WO-1037 single-resource impulse packs (legalised by the WO-947 §12 amendment): exactly ONE economy key per SKU, $5 ceiling, resources-only, smallest-sufficient resolver, no grant route ---
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "impulse-pack suite", () => { if (!DeNelle.Editor.Regression.ImpulsePackRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[impulse-pack] " + r); });
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "tower-wall-los suite", () => { if (!TowerWallLosRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[tower-wall-los] " + r); });
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "vfx-aura-diff suite", () => { if (!VfxAuraDifferentiationRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[vfx-aura-diff] " + r); });
            // --- owner VfxManualPicks per-tier tower projectiles: archer tier ladder + arcane base/upgraded wired + every key catalogued ---
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "tower-proj-map suite", () => { if (!TowerProjectileMapRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[tower-proj-map] " + r); });
            // --- WO-869 dungeon portal rebuild: robust shader resolve + MagentaGuard widening (protected primitive art + deferred re-sweep) + real additive state + arch structure ---
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "portal-rebuild suite", () => { if (!PortalRebuildRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[portal-rebuild] " + r); });
            // --- WO-826 Realm Map: realm-map.json dual-copy field parity + RealmMapCatalog loader oracle ---
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "realm-map suite", () => { if (!RealmMapRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[realm-map] " + r); });
            // --- WO-839 raid deploy screen: FrameCore footer/subHeader zones + F8 harness dev-guard + ScoutReport honesty ---
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "raid-deploy-ui suite", () => { if (!RaidDeployUiRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[raid-deploy-ui] " + r); });
            // --- WO-1403 raid deploy at zero troops: TRAIN TROOPS primary, one Manage door, spoils line shares WO-1402's producer ---
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "raid-deploy-zero-army suite", () => { if (!RaidDeployZeroArmyRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[raid-deploy-zero-army] " + r); });
            // WO-1519 - the raid deploy screen's MEASURED layout (band table, enemy card, spoils chips, army band; Echo Guide gone). Registered by the lead 2026-09-06.
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "raid-deploy-layout suite", () => { if (!DeNelle.Editor.Regression.RaidDeployLayoutRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[raid-deploy-layout] " + r); });
            // --- WO-766 wallet provider: Android-only SOLANA_SDK define + real-provider selection + transfer confinement ---
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "wallet-provider suite", () => { if (!WalletProviderSelectionRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[wallet-provider] " + r); });
            // WO-1255: a Play AAB is impossible to emit until source isolation is proven, then
            // the produced archive is inspected for physical Solana/MWA/crypto material.
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "play-packaging suite", () => { if (!GooglePlayPackagingRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[play-packaging] " + r); });
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "audio-startup-bounded suite", () => { if (!AudioStartupBoundedRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[audio-startup-bounded] " + r); });
            // --- wallet session (2026-08-17): the MWA grant survives a relaunch (she force-quit and was asked to connect again), is SEALED not plaintext, is BOUND to its wallet, is cleared on disconnect, and is never logged ---
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "wallet-session suite", () => { if (!DeNelle.Editor.Regression.WalletSessionPersistenceRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[wallet-session] " + r); });
            // --- WO-1211: boot reads are cached-only (never sign); writes retain fail-closed shared auth ---
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "backend-save-auth suite", () => { if (!DeNelle.Editor.Regression.BackendSaveAuthRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[backend-save-auth] " + r); });
            // --- WO-1420 + WO-1441 (2026-09-06): a connect refused in 0.4s reported "TIMED OUT after 30s", and NOTHING ever minted the backend session for an auto-resumed wallet, so every cloud save refused fail-closed with why=missing all day. Pins the measured refusal-vs-deadline branch, the one-line association-close correlation, and the explicit-connect mint call site ---
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "wallet-connect-attribution suite", () => { if (!DeNelle.Editor.Regression.WalletConnectFailureAttributionRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[wallet-connect-attribution] " + r); });
            // --- login gate (2026-08-18): her wallet auto-resumed at boot and the SIGN IN wall was presented anyway 5s later. The gate read Firebase ONLY on a wallet-first build; it must continue for connected OR attested-bound OR signed-in, and still present on a genuine first run ---
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "login-gate suite", () => { if (!DeNelle.Editor.Regression.LoginGateRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[login-gate] " + r); });
            // --- WO-1322 pi login gate (2026-09-02): she signed in with Pi in real Pi Browser ([Flow:Pi] Signed in as ..., skin auth=PiSdk) and the CHOOSE YOUR WALLET modal was presented anyway - the gate sampled only the two WALLET inputs and was skin-blind. Pins (a) Pi skin + signed in => CONTINUE, (b) Pi skin + not signed in => unchanged, (c) the SKR/Solana truth table byte-for-byte identical ---
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "pi-login-gate suite", () => { if (!DeNelle.Editor.Regression.PiLoginGateRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[pi-login-gate] " + r); });
            // --- promo redeem door: the Realm Store's ungated Redeem-a-Code entry routes through PromoCodeService, never logs the code, gives every failure its own canon sentence in both copies, and grants on the uncapped pack seam ---
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "promo-redeem-entry suite", () => { if (!PromoRedeemEntryRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[promo-redeem-entry] " + r); });
            // --- WO-835 action bar: Core applicability model invariants + View purity ---
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "hud-actionbar suite", () => { if (!HudActionBarRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[hud-actionbar] " + r); });
            // WO-1144: measured label fit (real glyph advances vs the authored box) at two
            // landscape aspects. RE-ADDED 2026-08-22 after a wholesale file copy from a
            // sibling worktree clobbered it - the registration lived only in the working
            // tree, so git had no record to conflict on. Copy HUNKS from another lane, never
            // a whole shared file.
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "hud-label-fit suite", () => { if (!DeNelle.Editor.Regression.HudLabelFitRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[hud-label-fit] " + r); });
            // WO-1248: hero-select carousel rotate control. "Previous" rendered "Pr..." because
            // a 0.068-well kit word-button armed FitSingleLine (NoWrap+Ellipsis). This suite
            // MEASURES the designed ICON+word against its plate at four surfaces and proves
            // the pre-fix box still goes RED on "Previous" (WO-1138).
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "hero-select-carousel suite", () => { if (!DeNelle.Editor.Regression.HeroSelectCarouselRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[hero-select-carousel] " + r); });
            // --- WO-1008 raids discoverability: a built Barracks ALWAYS shows the Raids face. She played a save with a Barracks and an empty army, the face was absent entirely, and she reported "I do not see a way to start a raid" - a feature that hides itself is indistinguishable from a broken one. Zero troops is now a greyed face with a WORDED reason (she is red/green colourblind, so hue carries nothing), and the full-army gate underneath is untouched. ---
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "raids-discoverability suite", () => { if (!RaidsDiscoverabilityRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[raids-discoverability] " + r); });
            // --- WO-830 echo resource picker: picker/token/affinity contract (sibling to the echo-spec suite) ---
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "echo-picker suite", () => { if (!EchoResourcePickerRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[echo-picker] " + r); });
            // --- WO-797 dungeon room ownership: encounter schema + wake/confine math + exit beacon (F8 seq 622) ---
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "dungeon-room-ownership suite", () => { if (!DeNelle.Editor.Regression.DungeonRoomOwnershipRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[dungeon-room-ownership] " + r); });
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "dungeon-kit suite", () => { if (!DeNelle.Editor.Regression.DungeonKitRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[dungeon-kit] " + r); });
            // --- WO-854 quest completability: EVERY story stage must have a reachable completion. This suite existed for four days and was registered NOWHERE - its own header carried this exact line as un-applied text, QUEST_REACH_OK appears in no log on disk, and four commits ratcheted MinCompletableStages up to 63 while nothing checked it. A ratchet defended by nothing is worse than no ratchet: it reads as proof. ---
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "quest-reach suite", () => { if (!DeNelle.Editor.Regression.QuestCompletabilityRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[quest-reach] " + r); });
            // --- WO-1001 slices 1b-8 composed pillars: the baker places EVERY pillar through FindType reflection (Editor cannot reference DeNelle.Dungeons), so a rename WARNs and places nothing while the bake still says saved=True; plus the bake-time-Configure-must-survive-SaveScene pin, authored-vs-placed parity in the baked scenes, the key bag, darkness actually feeding the roll, and a lock whose key is never granted (an unwinnable run) ---
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "dungeon-composed-pillars suite", () => { if (!DeNelle.Editor.Regression.DungeonComposedPillarsRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[dungeon-composed-pillars] " + r); });
            // --- WO-1001 slice 1 multi-level dungeons: StairDown/StairUp sockets oppose and carry half a floor each, a vertical mate drops exactly one floor, a stacked pair is not an overlap (that abort is what made descents impossible), doors keep the planar-only nudge, and the GENERATED stair prefabs on disk actually carry the poses ---
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "dungeon-multilevel suite", () => { if (!DeNelle.Editor.Regression.DungeonMultiLevelRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[dungeon-multilevel] " + r); });
            // --- WO-957 egress trim (owner F8 seq 2508: "Should be single entry point in maybe 2 total out"): a CONTENT dungeon authors AT MOST ONE extract - the BACK exit, seated in the room DungeonTreasureCache resolves as deepest - plus the one injected front exit. Nothing asserted the count before, which is how 13 per-stairwell pads accreted and gave dg_ember_deep SIX ways out. Also pins the WO-930 control-group exemption and the Resources/StreamingAssets dual-copy hash. ---
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "dungeon-egress suite", () => { if (!DeNelle.Editor.Regression.DungeonEgressRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[dungeon-egress] " + r); });
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "biome-roads suite", () => { if (!DeNelle.Editor.Regression.BiomeRoadsRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[biome-roads] " + r); });
            // --- WO-1604 (F8 seq 4703): a biome drop is EITHER classified as the region its prompt
            // names, OR it does not exist. Two authorities used to answer "where does Ashwood start"
            // -- BiomeRoads.EdgeFraction x the measured reach, and ZoneManager.GetZone -- and nothing
            // made them agree, so the drop seated, the player walked through, the hero teleported,
            // and only THEN did the arrival check discover the prompt had lied. ZoneManager is now
            // the owner (probed by bisection, never copied) and the drop is refused before the door
            // exists. Case 1 is RED on the pre-fix code with the capture's own (0, y, 50). Case 5
            // pins the instrumentation gap that made this ticket blame the wrong system: drift was
            // computed and printed only on SUCCESS, so a warp that never landed read as a
            // region-split disagreement. ---
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "biome-drop-classification suite", () => { if (!DeNelle.Editor.Regression.BiomeRoadDropClassificationRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[biome-drop-classification] " + r); });
            // --- WO-1101 ground textures: every terrain layer carries CURATED (tracked) BaseColor + Normal art, measured Rec.709 luminance matches the WO-1044 per-march value targets, ADJACENT marches on the compass cycle separate by ΔL >= 0.15 (the colourblind gate as arithmetic - today's tints are ΔL 0.074 and fail it), Ashwood's ground stays PALE ("ink on ash", not the shipped inverted L=0.176), and the layer-index contract has exactly ONE authority (TerrainLayerSet) that both the bake and the DEF-108 runtime repaint read. ---
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "terrain-layer suite", () => { if (!DeNelle.Editor.Regression.TerrainLayerRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[terrain-layer] " + r); });
            // --- WO-850 dungeon treasure cache: fixed-bundle validity against materials.json, deepest-room BFS (undirected + ordinal tie-break), per-dungeon first-clear one-shot, panel single-exit ---
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "dungeon-treasure suite", () => { if (!DeNelle.Editor.Regression.DungeonTreasureRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[dungeon-treasure] " + r); });
            // --- WO-1132 loot chest: the container is OPENABLE, not attackable. Pins that BreakableContainer implements NEITHER damage interface and declares no CombatFaction, is not relayered to "Enemy" (the WO-1047 hostile-reticle defect class, removed at source rather than filtered), that opening is gated on the ONE combat authority (BattleLock.IsInBattle, re-checked inside Open) and refuses with a real canon sentence in BOTH dual copies, that the loot roll/spawn path is unchanged, that Create's reflection signature survives (DungeonBaker invokes it by name), and that WO-1047's [hostile-admit] instrumentation is still in place with both branches ---
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "chest suite", () => { if (!DeNelle.Editor.Regression.BreakableContainerChestRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[chest] " + r); });
            // --- WO-852 Echo card layout: chip rows at/above MinTouchPx, fixed-pixel bands (no 1f/n fraction slicing), scroll well, per-frame rebuild guard ---
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "echo-card-layout suite", () => { if (!DeNelle.Editor.Regression.EchoCardLayoutRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[echo-card-layout] " + r); });
            // --- WO-866 rumor board layout: every filter tab fits the list well at the touch floor (the clipped "Gear"), the tab band is X-bounded by the list column so the detail pane cannot cross it, and the detail stack + a two-line body fits the pane (the -11px culled body) ---
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "rumor-board-layout suite", () => { if (!DeNelle.Editor.Regression.RumorBoardLayoutRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[rumor-board-layout] " + r); });
            // --- WO-1515 defense report layout: the detail well is an OPAQUE obsidian plate with the bezel as a SEPARATE image (the one-image version took the hollow frame sprite and let the kit's TwoToneParchmentFill read through it - the owner's tan slab under light ink), every detail ink clears 4.5:1 on that plate, and the list row band is derived from the row font with a one-line fitted label (the two-line label that painted over the next row's bezel) ---
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "defense-report-layout suite", () => { if (!DeNelle.Editor.Regression.DefenseReportLayoutRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[defense-report-layout] " + r); });
            // --- WO-878 build menu layout: every band that carries a button is at the kit touch floor (so ClampMinTouch cannot grow it into a neighbour - the root verbs, "< Back" and the Upgrade CTA all overlapped), the ladder fits the derived body at every capture aspect, and the cost/preview/CTA strings are the VM's ---
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "buildmenu-layout suite", () => { if (!DeNelle.Editor.Regression.BuildMenuLayoutRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[buildmenu-layout] " + r); });
            // --- WO-880 tower manager: the VM reads the SAME stat source the game builds from (the catalog repo block StructureFactory copies onto DefenseTower), every catalog tower row resolves to non-zero rng/dmg, a stat-less row says "(building)"/"(no stats)" instead of a fabricated "rng 0, dmg 0", and the list well is an exact whole number of row pitches (the half-clipped third row) ---
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "tower-manager suite", () => { if (!DeNelle.Editor.Regression.TowerManagerRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[tower-manager] " + r); });
            // --- WO-882 help menu: the VM emits only entries that are AVAILABLE and RENDERABLE, so the View can never build a blank button. The capture's "blank" row was two defects -- a Dev Tools label force-painted ElarionUi.Ink (near-black) onto what has resolved to a dark grey plate since 2026-07-16, and a third row clipped to a 36px sliver because the well's fraction-anchored height was not a whole multiple of the row pitch. Wired here by the committer: the authoring lane was fenced out of this file while five siblings held it. ---
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "help-menu-entry suite", () => { if (!DeNelle.Editor.Regression.HelpMenuEntryRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[help-menu-entry] " + r); });
            // --- WO-860 starter loadout + shelf: new game clears dotr-equip-*, Knight starts sword+shield (not the stale axe / not auto-best Flameblade), shelf capped + equippable-only + no blink_* ---
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "starter-loadout suite", () => { if (!DeNelle.Editor.Regression.StarterLoadoutRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[starter-loadout] " + r); });
            // --- shields: every shield carries a real defense value, the ladder climbs with req.level, and GearLoadout actually SUMS the off-hand (all three were missing - shields were pure decoration) ---
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "shield-defense suite", () => { if (!DeNelle.Editor.Regression.ShieldDefenseRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[shield-defense] " + r); });
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "shield-load-restore suite", () => { if (!DeNelle.Editor.Regression.ShieldLoadRestoreRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[shield-load-restore] " + r); });
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "jeweler-discovery-ftue suite", () => { if (!DeNelle.Editor.Regression.JewelerDiscoveryFtueRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[jeweler-discovery-ftue] " + r); });
            // --- tower empowerment reachability: Tower.TryEmpower (tower-perks tier 4 + TowerCombat's GlacialCore/TrueAim/ManaSurge/EternalEmber) is gated by ONE affordance. This suite resolves the path outward from the gate - callers, then their referrers, then scene/prefab placements - and declares whether any of it is anchored in shipping code. It PINS today's orphan state, so wiring the affordance fails the suite until the expectation flag is flipped (which is what forces the owner's felt-verify of the new power). ---
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "tower-empower-reach suite", () => { if (!DeNelle.Editor.Regression.TowerEmpowermentReachabilityRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[tower-empower-reach] " + r); });

            // --- 2026-08-02 oracle wave: suites written by the parallel lanes tonight.
            // Each class was VERIFIED to exist on disk with a public static bool Run(out string)
            // before being registered here (a phantom registration is a compile break).
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "modifier-key-coverage suite", () => { if (!DeNelle.Editor.Regression.ModifierKeyCoverageRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[modifier-key-coverage] " + r); });
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "hub-foliage suite", () => { if (!HubFoliageRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[hub-foliage] " + r); });
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "glossary suite", () => { if (!DeNelle.Editor.Regression.GlossaryRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[glossary] " + r); });
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "item-identity suite", () => { if (!DeNelle.Editor.Regression.ItemIdentityRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[item-identity] " + r); });
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "drop-mote suite", () => { if (!DeNelle.Editor.Regression.ItemDropMoteIdentityRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[drop-mote] " + r); });

            // Second wave of the 2026-08-02 program (PM spec). Each class + its declared
            // tag were read off disk before registering; tags are the ones the suite
            // headers themselves declare, not the ones the spec guessed.
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "enemy-pool-reset suite", () => { if (!DeNelle.Editor.Regression.EnemyPoolResetRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[enemy-pool-reset] " + r); });
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "enemy-death-ground suite", () => { if (!DeNelle.Editor.Regression.EnemyDeathGroundRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[enemy-death-ground] " + r); });
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "healing-caravan-surface suite", () => { if (!DeNelle.Editor.Regression.HealingCaravanSurfaceRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[healing-caravan-surface] " + r); });
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "ballista-tier-orientation suite", () => { if (!DeNelle.Editor.Regression.BallistaTierOrientationRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[ballista-tier-orientation] " + r); });
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "storage-stack-placement suite", () => { if (!DeNelle.Editor.Regression.StorageStackPlacementRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[storage-stack-placement] " + r); });
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "apex-dragon-spawn suite", () => { if (!DeNelle.Editor.Regression.ApexDragonSpawnRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[apex-dragon-spawn] " + r); });
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "mage-protection-dock suite", () => { if (!DeNelle.Editor.Regression.MageProtectionDockRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[mage-protection-dock] " + r); });
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "mage-spell-identity suite", () => { if (!DeNelle.Editor.Regression.MageSpellIdentityRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[mage-spell-identity] " + r); });
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "tutorial-reach suite", () => { if (!DeNelle.Editor.Regression.TutorialStepReachabilityRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[tutorial-reach] " + r); });
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "runtime-spawn-visual suite", () => { if (!DeNelle.Editor.Regression.RuntimeSpawnVisualRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[runtime-spawn-visual] " + r); });
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "wallet-identity suite", () => { if (!DeNelle.Editor.Regression.WalletIdentityRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[wallet-identity] " + r); });
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "loot-class-gate suite", () => { if (!DeNelle.Editor.Regression.LootClassGateRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[loot-class-gate] " + r); });
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "shader-predicate-authority suite", () => { if (!DeNelle.Editor.Regression.ShaderPredicateSingleAuthorityRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[shader-predicate-authority] " + r); });
            // --- dynamic difficulty: neutral lands EXACTLY on the authored target, both rails reachable, spike expires at read time, no dead authored key ---
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "dynamic-difficulty suite", () => { if (!DeNelle.Editor.Regression.DynamicDifficultyRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[dynamic-difficulty] " + r); });
            // --- raid arena shape: footprint is a real fraction of the plane (the 2.4% square can never return), the spire is reachable by the HERO's seam, navmesh present ---
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "raid-arena-shape suite", () => { if (!DeNelle.Editor.Regression.RaidArenaShapeRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[raid-arena-shape] " + r); });
            // WO-1520 — the raid STAGING area: the marker is measured against every turret's reach and every
            // defender's awareness radius, and the 180s clock cannot advance before first engagement.
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "raid-staging suite", () => { if (!DeNelle.Editor.Regression.RaidStagingMarkerRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[raid-staging] " + r); });
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "raid-base-layout suite", () => { if (!DeNelle.Editor.Regression.RaidBaseLayoutRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[raid-base-layout] " + r); });
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "reset-full-clear suite", () => { if (!DeNelle.Editor.Regression.ResetToNewGameFullClearRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[reset-full-clear] " + r); });
            // WO-1371 — the OTHER axis. reset-full-clear sweeps GameState FIELDS and says in its own
            // comments that a PlayerPrefs store "is not one"; this sweeps those stores, which is where
            // the 14,089-resource inherited collector fill lived while that suite ran green.
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "newgame-pref-sweep suite", () => { if (!DeNelle.Editor.Regression.NewGamePrefStoreSweepRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[newgame-pref-sweep] " + r); });
            // WO-1370 — the HARVEST RESULT modal's copy (name beside its own figure, loss named, agreement).
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "harvest-result-copy suite", () => { if (!DeNelle.Editor.Regression.HarvestResultCopyRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[harvest-result-copy] " + r); });
            // WO-1525 - the HARVEST RESULT modal's SHAPE (three rows, a bar, one action each; VM-composed). Registered by the lead 2026-09-06.
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "harvest-result-shape suite", () => { if (!DeNelle.Editor.Regression.HarvestResultShapeRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[harvest-result-shape] " + r); });

            // WO-1369 (P0 freeze) + WO-952 - registered by the lead 2026-09-04. The implementing
            // agent authored these three and was rate-limited before registering them; the
            // registry meta-oracle caught all three as unregistered, which is exactly the WO-973
            // failure ("an oracle sat unregistered and never ran") working as designed.
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "worldhold-liveness suite", () => { if (!DeNelle.Editor.Regression.WorldHoldLivenessRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[worldhold-liveness] " + r); });

            DeNelle.Core.Diagnostics.Guard.Try("Regression", "gameover-lifecycle suite", () => { if (!DeNelle.Editor.Regression.GameOverScreenLifecycleRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[gameover-lifecycle] " + r); });

            DeNelle.Core.Diagnostics.Guard.Try("Regression", "endstate-body-fit suite", () => { if (!DeNelle.Editor.Regression.EndStateBodyFitRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[endstate-body-fit] " + r); });
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "cathedral-cumulative suite", () => { if (!DeNelle.Editor.Regression.CathedralCumulativeRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[cathedral-cumulative] " + r); });
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "hero-equip-hub suite", () => { if (!DeNelle.Editor.Regression.HeroEquipHudHubRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[hero-equip-hub] " + r); });
            // The gear lane FOLDED shield-improvement + defense-cap into this one file rather
            // than shipping the three classes the spec named - registered as it actually landed.
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "armed-hero suite", () => { if (!DeNelle.Editor.Regression.ArmedHeroInvariantRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[armed-hero] " + r); });
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "buildmenu-economy suite", () => { if (!DeNelle.Editor.Regression.BuildMenuRealEconomyRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[buildmenu-economy] " + r); });
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "hero-death-pin suite", () => { if (!DeNelle.Editor.Regression.HeroDeathPinRebaseRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[hero-death-pin] " + r); });
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "wall-build-l1 suite", () => { if (!DeNelle.Editor.Regression.WallBuildL1Regression.Run(out var r)) failures.Add(r); else log.AppendLine("[wall-build-l1] " + r); });
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "synty-perimeter-grounding suite", () => { if (!DeNelle.Editor.Regression.SyntyPerimeterGroundingRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[synty-perimeter-grounding] " + r); });
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "castle-plans suite", () => { if (!DeNelle.Editor.CastlePlansUnlockRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[castle-plans] " + r); });
            // WO-1105: the SEAT half (where the drop stands), sister to the unlock half above.
            // Distinct marker CASTLE_PLANS_SEAT_OK -- a shared marker is how a 22-case pass once
            // read as the full suite's pass (canon sec 8).
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "castle-plans-seat suite", () => { if (!DeNelle.Editor.CastlePlansSeatRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[castle-plans-seat] " + r); });
            // 2026-08-16: three player-visible defects pinned together - the talent panel must
            // resolve the LIVE hero class (a Ranger was spending Wisdom on the knight tree), the
            // ranger bow de-dupe must not be defeatable by component-add order (two bows), and an
            // unaffordable upgrade tap must not log as an F8 error. Distinct marker
            // LIVE_CLASS_BOW_AFFORD_OK (canon sec 8: a shared marker hides which suite really ran).
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "live-class-bow-afford suite", () => { if (!DeNelle.Editor.Regression.LiveClassBowAndAffordSeverityRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[live-class-bow-afford] " + r); });

            // --- WO-853 structures are targetable: Faction derived (never serialized) on every
            // IDamageable, walls stay on layer Structure (towers must not shoot through them),
            // a wall at 100 damage drops its solid colliders, and DefenseTower's two IsAlive
            // answers stay deliberately different (player seam = liveness, enemy seam = +PlayerOwned) ---
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "structure-targetable suite", () => { if (!DeNelle.Editor.Regression.StructureTargetableRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[structure-targetable] " + r); });

            // --- THE QUEUED TOWER THAT FOUGHT (owner F8, 2026-08-04): a structure with an
            // in-flight build job must not acquire, fire or damage. The WO-612 scaffold silenced
            // exactly ONE component type (DefenseTower), so 'tower_arcane_spire' -> ArcaneTower
            // slipped the gate entirely and defended live waves for its whole timer -- five spires
            // at remaining=270s in the owner's own capture, a hole WO-855 Phase 4 stretched from
            // 15s to up to 2h. Pins: the gate silences EVERY combat family, Reveal restores exactly
            // what it silenced, baked/EnemyOwned towers with no scaffold stay untouched, and the
            // job key survives the save round trip so a pending tower reloads inert ---
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "under-construction-gate suite", () => { if (!DeNelle.Editor.Regression.UnderConstructionGateRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[under-construction-gate] " + r); });

            // --- PHANTOM COLLECTOR INCOME (2026-08-04): an empty town must earn ZERO. The
            // harvest tick's only guard was `GetLevel(id) < 1`, and GetLevel defaults to 1
            // and never asks whether the building exists, so all three resource buildings
            // paid out from t=0 - straight to the wallet, uncapped, no Collect tap. Pins the
            // existence gate (WO-834 everBuiltStructureIds / a live collector), the deleted
            // direct-grant fallback, and the zero-seed founding bootstrap it must not break ---
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "collector-income suite", () => { if (!DeNelle.Editor.Regression.CollectorIncomeRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[collector-income] " + r); });

            // --- TOWN BANK CAP (WO-857 / WO-901 Phase F, 2026-08-04): the first UPPER clamp ever
            // put on EconomyService.Grant -- the single path every income source in the game flows
            // through. Get it wrong and resources silently vanish or a fresh save soft-locks, so this
            // suite is the permission gate (ARCHITECTURE_PRINCIPLES §2c): crystals+coins uncapped BY
            // DESIGN, baseCap can never resolve to 0, a fresh 0-wood/0-iron save can still found and
            // buy, a spend is never upper-clamped, every clamped grant EMITS THE WARN (the only thing
            // between the player and vaporised resources), capacity scales with container level,
            // fill/drain is ONE pure capacity-ascending function, and an over-cap save is grandfathered ---
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "town-bank-cap suite", () => { if (!DeNelle.Editor.Regression.TownBankCapRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[town-bank-cap] " + r); });

            // WO-1206: a RETIRED resource word must never reach a player surface again. Two Food
            // leaks in one hour were found by the OWNER, not by a gate, because WO-1163's conversion
            // was applied per-surface with nothing asserting the retirement. The retirement list is
            // DATA (retired-vocabulary.json, dual-copy) - never a C# list, which would be one fact
            // written twice. ⛔ It scopes itself by SYNTAX, not by a hand-maintained name list:
            // frozen persistence vocabulary (JsonProperty, const string, case ", PlayerPrefs,
            // FlowTrace/Debug) is excluded, because an oracle that cannot tell a display string from
            // a wire key gets switched off within a week.
            // Registered by the COMMITTER; the lane that wrote it is fenced out of this file.
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "retired-vocabulary suite", () => { if (!DeNelle.Editor.Regression.RetiredVocabularyRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[retired-vocabulary] " + r); });

            // WO-1265: ClanService/ClanChatPanel remain a local PlayerPrefs prototype. Both the
            // dock door and direct bootstrap stay gated until the signed-wallet backend,
            // moderation, operator readiness and two-wallet acceptance exist.
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "clan-feature-gate suite", () => { if (!DeNelle.Editor.Regression.ClanFeatureGateRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[clan-feature-gate] " + r); });

            // WO-1207 (owner rulings 2026-08-25): a trimmed HARVEST is TOLD, a trimmed BATTLE REWARD
            // is SILENT. "they get a warn on harvest but no warn on battle rewards cause one is
            // choice" - collecting is timed by the player, a reward is not. Both halves are pinned
            // here so a later refactor cannot quietly widen the warning to every grant path.
            // Registered by the COMMITTER, never by the lane that wrote the suite (this line is the
            // one file every parallel oracle lane would otherwise collide in).
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "harvest-trim-warn suite", () => { if (!DeNelle.Editor.Regression.WO1207HarvestTrimWarnRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[harvest-trim-warn] " + r); });

            // --- WO-1191: income while a resource sits ABOVE its cap. Ruling: `FOUNDATIONAL_RULINGS.md`
            // section 7 -- read it there, it is not restated. The suite above proves the clamp helper
            // RETURNS the right number, which is the same act as reading the code; this one reads the
            // WALLET before and after a real EconomyService credit and asserts the DELTA -- the only
            // shape that catches the WO-978 failure where four callers logged the request as though it
            // were the credit. Paid overflows in full; earned adds exactly zero above the cap; spending
            // back under restarts it; uncapped resources (enumerated from IsCapped, never a name list)
            // are untouched; and the above-cap state is published on a named axis so the toast stops
            // calling a purchase a loss ---
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "over-cap-income suite", () => { if (!DeNelle.Editor.Regression.WO1191OverCapIncomeRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[over-cap-income] " + r); });

            // --- WO-1590: the kill-grant materials warn must NAME the cause it was handed ---
            // The owner's 2026-09-07 dungeon session warned on every kill that a material grant
            // "did not land in full (missing EconomyService/GameState, or the town bank cap
            // clamped that axis)" while the adjacent [Flow:Bank] line already read "BANK FULL
            // [Grant] Stone ... (wallet 34000/34000)". The mechanics were correct throughout; the
            // SENTENCE misdiagnosed itself and cost a debugging session, so the composed sentence
            // is the oracle here, alongside a measured Stone grant with and without headroom.
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "kill-grant-shortfall-reason suite", () => { if (!DeNelle.Editor.Regression.KillGrantShortfallReasonRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[kill-grant-shortfall-reason] " + r); });

            // --- ECON-SWEEP 2026-08-16: the four economy-silo defects from the cross-silo sweep ---
            // (1) no spend/grant may move the UNSAVED _wood/_iron pool during play without a hard,
            // F8-visible FlowTrace.Fail; (2) a bank-cap-clamped grant logs and pops the APPLIED amount,
            // never the request (the Echo silo dump popped pre-clamp numbers for resources the player
            // never got); (3) a cancel notice never says "Nothing to refund." when a currency outside
            // the refundable basket WAS taken (research is gold-priced and JobCost has no coins lane —
            // the MESSAGE was the defect, the refund policy is the owner's call); (4) the Echo "Lv N"
            // readout stays off the card/roster while EchoAssignments.SetLevel has no production
            // caller, with the level DATA axis untouched ---
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "econ-sweep suite", () => { if (!DeNelle.Editor.Regression.EconomySweepRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[econ-sweep] " + r); });

            // --- THE EMPTY UI REVIEW (owner, 2026-08-04): INDEX.html showed "mostly just the
            // blank templates and nothing else". Not an un-fed review -- a POISONED one. The exe
            // was built 21:18:09 and at 21:21:06 an AutoPilot fleet running in its DEFAULT
            // -nographics mode rewrote 35 panel_*.png review shots at exactly 33150 bytes each
            // (flat black), because CaptureRawShot fired ScreenCapture with no graphics device
            // and its own comment called that acceptable. build-ui-review.ps1 then badged the
            // blanks "PAIR COMPLETE". Pins: neither capture path can write an unmeasured frame,
            // every _mapping.json panelId has a real AutoPilot route or an argued exemption, and
            // each route writes the EXACT deliveredShot filename the review reads ---
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "ui-capture-coverage suite", () => { if (!DeNelle.Editor.Regression.UiCaptureCoverageRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[ui-capture-coverage] " + r); });

            // --- WO-865 Skills panel layout: the body is three DISJOINT fixed-pixel bands
            // (columns / ability / action), never fractions of a ~493px body well. The 2026-08-04
            // Seeker capture showed a 32px fraction action row that ClampMinTouch grew to the
            // 112px touch floor SYMMETRICALLY -- straight over the graph well and quick-slot 4 --
            // plus a centre-pivoted graph content rect sliced at both mask edges, a section band
            // 15.6px from a node row, and a 23px name band that ellipsized "Emberbrand Throw".
            // Pins: every tappable band >= MinTouchPx and every text band >= a TMP line box; the
            // stack replayed at the reference body leaves a positive graph well + a two-line
            // description; the graph pad covers half a node plate and the fixed px-per-unit
            // lattice clears the tightest gap authored in hero-talents.json; the longest catalog
            // word/name fits at the FontFloor; and the source laws (RectMask2D, top-left pivot,
            // band pins, reserved section row, no 1/n slicing, no green ButtonConfirm overlay) ---
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "skills-panel-layout suite", () => { if (!DeNelle.Editor.Regression.SkillsPanelLayoutRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[skills-panel-layout] " + r); });

            // --- HUD DOCK LAYOUT (WO-1319, owner web capture "BUILDTALKHERO...QUEUE MANAGE"):
            // the bottom action dock sliced a mount that is 46% of a canvas whose LOCAL width
            // collapses with the aspect into 1/5 fractions; the fractions fell under MinTouchPx
            // and the touch clamp then grew every face into its neighbour. Replays the SHIPPING
            // solver (HudDockLayout.Solve) across an aspect ladder at 5 AND 6 faces and pins:
            // no overlap, no growth into the MoveCluster column, landscape geometry unchanged,
            // and the source laws (solver still wired, caption still fitted, clamp still called).
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "dock-layout suite", () => { if (!DeNelle.Editor.Regression.HudDockLayoutRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[dock-layout] " + r); });

            // --- TALENT FOCUS SINGLETON (WO-1021 sec 2.1d, owner "Still Messy" at WIS 252):
            // SkillNodeState.Next is a PER-TRACK signal, so a view that renders it oversized
            // grows one shouting gold plate per track. Pins: at most ONE plate above NodeSizePx
            // on a multi-track board, a per-track NEXT cue that is normal-size and separable in
            // GREYSCALE, and the sec.2.1b lattice solver holding the minimum pitch ---
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "talent-focus suite", () => { if (!DeNelle.Editor.Regression.TalentFocusSingletonRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[talent-focus] " + r); });

            // --- WO-1027 SESSION SHAPE: CoC's retention engine was never the queue, it was the ACHE
            // of an idle builder -- and CoC carries that ache on a RED BADGE, which is banned here
            // (the owner is red/green colourblind). The ache is carried by SHAPE + NUMBER instead.
            // Pins the ONE idle-line authority (ObsidianQueueGate.WorkQueueStatus.IdleLineCount --
            // idle means zero ACTIVE, never Busy<Slots, which is the single most likely wrong turn),
            // that the empty-slot SOCKET clears the same 0.45 Rec.709 luma bar the talent oracle sets
            // (the free card shipped at 0.015 and was invisible), that a calm bar shows the BARE word
            // so the rejected nudge cannot return as a permanent adornment, and that the bar stays 6
            // visible / 7 identities with Map dormant at ordinal 4 ---
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "session-shape suite", () => { if (!DeNelle.Editor.Regression.SessionShapeRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[session-shape] " + r); });
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "dungeon-status suite", () => { if (!DeNelle.Editor.Regression.DungeonStatusRegression.Run(out var rDgStatus)) failures.Add(rDgStatus); else log.AppendLine("[dungeon-status] " + rDgStatus); });

            // --- WO-900 COLLECTOR TELL: CollectorStackView was a complete 437-line "I am full" tell
            // with ZERO CALLERS -- a collector filling up showed the player nothing, Accrue clamped
            // silently and the wallet number just stopped moving. Pins BOTH halves: the diegetic view
            // has a caller in StructureFactory, and the ambient HUD chip is built, occupied in
            // hud-areas.json, published to by the Village side, and says "Collectors ... full" and
            // never "Storage" (that word is the WALLET's, WO-857 -- two notions of "full" on one
            // screen is the confusion the copy law exists to prevent) ---
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "collector-tell suite", () => { if (!DeNelle.Editor.Regression.CollectorTellRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[collector-tell] " + r); });

            // --- TALENT TREE SHAPE (owner ruling 2026-08-16): "start with three and they can
            // branch wider", "common or specialty should still start from a few simple then
            // really refine to the playstyle of the user". The shared pool was reshaped to
            // 3/4/4 while the three CLASS trees still fanned five-to-eight flat across the
            // bottom rank, and ranger/mage carried no authored position at all -- so the
            // runtime auto-placer, not the designer, decided the tree the player looked at.
            // Pins: at most THREE root, cheapest-cost nodes on every bottom row (classes AND
            // the common pool), a strictly wider row above it and no funnel above that, every
            // node positioned inside 0..1, no orphan / cycle / unreachable node, and no
            // visible node stranded behind a hidden prerequisite ---
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "talent-tree-shape suite", () => { if (!DeNelle.Editor.Regression.TalentTreeShapeRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[talent-tree-shape] " + r); });

            // --- NUMERAL LEGIBILITY (owner defect 2026-08-05, QueueCardRail_2670x1200.png):
            // no typographic role may render its numeral 1 as a bare vertical stroke. The chip
            // font (Alata) drew '1' at 7.23 ink units against its own 'l' 6.84 and '|' 6.14, so
            // "Builders 1/2 | Train 1" read as three identical marks with three meanings. This
            // measures the LIVE glyph metrics of the font each role actually renders with ---
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "numeral-legibility suite", () => { if (!DeNelle.Editor.Regression.NumeralLegibilityRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[numeral-legibility] " + r); });

            // --- WO-879 daily-quest empty state: ONE empty-state fact owned by DailyQuestVM (assigned at a single site, IsEmpty projects it), proven on a live null-source VM, and DailyQuestHud reads it once + renders it once in one chrome (no BuildParchmentDetailEmpty second column, no View-authored copy, no View-side emptiness test), on fixed-pixel bands ---
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "daily-quest-empty suite", () => { if (!DeNelle.Editor.Regression.DailyQuestEmptyStateRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[daily-quest-empty] " + r); });

            // --- VFX LOOP FLAGS (2026-08-05): a catalog row's IsLoop must equal what its
            // prefab's emission actually does. IsLoop was a sticky manual checkbox in
            // VfxCasterWindow (force-set true for the Projectile/Aura roles), so 95 of 135
            // HovlVfxCatalog rows read IsLoop:1 -- including a pile of rate-0 burst prefabs
            // (PP_BigExplosion, PP_MuzzleFlash, PP_EarthShatter ...). A loop row never
            // auto-returns its pool slot (VFXManager.Hovl.cs ~283-288 registers no reclaim
            // deadline; the only loop reclaim frees DESTROYED hosts, which pooled objects
            // never are), so each fire-and-forget play permanently burned one of the 20
            // slots -- six F8 captures caught the cap saturated at 20/20 ---
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "vfx-loop-flag suite", () => { if (!DeNelle.Editor.Regression.VfxLoopFlagRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[vfx-loop-flag] " + r); });

            // --- ELITE/BOSS VFX WIRING (WO-874): the owner ruled 2026-08-04 and again
            // 2026-08-21 that EliteVFXController must be WIRED. Commit 4c1da079 delivered
            // the visible half via STATICS and never attached the component, so
            // AddComponent<EliteVFXController> sat at zero hits repo-wide while the aura
            // and OnEliteAttack had never run in the shipped game - and the ticket read as
            // progressed. Every gate we had asked "does the effect play?", which the
            // shortcut satisfied. This asks the question the ruling turns on: is the
            // component attached, and is its INSTANCE api called ---
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "elite-vfx-wire suite", () => { if (!DeNelle.Editor.Regression.EliteVfxWiringRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[elite-vfx-wire] " + r); });

            // --- SURFACE IMPACT VFX (WO-887 surface half): the owner tagged the five
            // surfaces on 2026-08-21 and ruled the defaults. The five pack recipes carry
            // demo geometry (mesh + SPHERE COLLIDER) on the prefab ROOT and each has one
            // layer emitting 5/sec ON LOOP, so a straight copy would drop a physics
            // collider at every impact and permanently burn a global loop slot per hit.
            // This proves the shipped mirrors are stripped + forced one-shot, that the
            // code keys still agree with her VfxManualPicks rows, and it EXERCISES the
            // surface resolution against her defaults rather than restating a table ---
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "surface-impact-vfx suite", () => { if (!DeNelle.Editor.Regression.SurfaceImpactVfxRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[surface-impact-vfx] " + r); });

            // --- WO-1344: the FTUE "where to go" pointer. Pins the code key against the
            // owner's VfxManualPicks row (key AND the prefab she tagged it to, so a refactor
            // cannot silently re-point her tag), and pins the pointer's INPUT TRANSPARENCY --
            // the FocusMask it replaces never blocks on a world target, and a pointer that
            // swallowed a tap would soft-lock the FTUE on the step it is teaching ---
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "ftue-pointer-vfx suite", () => { if (!DeNelle.Editor.Regression.FtuePointerVfxRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[ftue-pointer-vfx] " + r); });

            // --- AOE RETICLE RADIUS (WO-1345, 2026-09-03): the reticle's ground radius is
            // DERIVED from the ability's own Range -- the same value Blast() sweeps for damage --
            // scaled against the prefab's measured 2.42m authored ring radius, with the owner's
            // tag `scale` applied only as a multiplier ON TOP. The defect this pins is a reticle
            // pinned to a CONSTANT: it looks correct on one spell and silently lies about every
            // other, and the player aims by it. Registered HERE by the committer, never by the
            // lane that wrote the suite, so two agents cannot both append to this file. ---
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "aoe-reticle-radius suite", () => { if (!DeNelle.Editor.AoeReticleRadiusRegression.Run(out var rAoe)) failures.Add(rAoe); else log.AppendLine("[aoe-reticle-radius] " + rAoe); });

            // --- OWNER-TAGGED AURA + CHEST WIRING (WO-1346/1347, 2026-09-03): pins the arcane
            // tower aura's BUILT-STATE gate INSIDE StartAura (an event-only gate loses the reload
            // case -- an already-built tower would go dark on every relaunch), the chest shimmer
            // stopping on open, the collect burst staying unparented + time-bounded (its modal is
            // destroyed on the next statement), and the no-row invariant answering the built-in
            // 45% rather than 0, because a 0 emission multiplier is an INVISIBLE aura. ---
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "owner-aura-chest suite", () => { if (!DeNelle.Editor.OwnerTaggedAuraChestWiringRegression.Run(out var rOac)) failures.Add(rOac); else log.AppendLine("[owner-aura-chest] " + rOac); });

            // --- VFX SELF-CONTAINMENT (2026-08-05): the shipped Resources/VFX prefabs
            // were committed with a message claiming the tracked copy is what ships.
            // FALSE: AssetDatabase.CopyAsset duplicates the PREFAB ONLY, so all 28
            // prefabs kept pointing their materials/textures/shaders/meshes at
            // Assets/UnityTechnologies (.gitignore:399) and Assets/Spells Pack
            // (.gitignore:214) -- 73 distinct gitignored assets, Boss_FireBreath alone
            // reaching 6. On a fresh clone / the laptop / CI those resolve to nothing
            // and the effects render MAGENTA or untextured, which is exactly the
            // "no magenta leak through, no missing shaders" criterion that work was
            // signed off against. Latent only because this machine has the packs.
            // Fixed by DeNelle.Editor.VfxResourceArtMirror; this oracle is what stops
            // it silently coming back ---
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "vfx-self-contained suite", () => { if (!DeNelle.Editor.Regression.VfxResourceSelfContainmentRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[vfx-self-contained] " + r); });

            // --- VFX NULL SLOTS (WO-1100, 2026-08-16): a catalogued prefab carrying an
            // ENABLED ParticleSystemRenderer with ALL material slots null draws
            // engine-default MAGENTA, the runtime deliberately refuses to repaint a
            // particle slot (the 08-05 white-blob lesson), and every spawn F8-spams a
            // MagentaProbe M2 FAIL -- 12 owner captures per session for the portal
            // threshold aura, whose slot-level shape NO existing gate asserted (the
            // self-containment gate measures gitignored REACH, and this prefab's reach
            // is owner-baselined on purpose). DISABLED all-null renderers are the
            // vendor container pattern -- noted, normalized at spawn by VFXManager,
            // never failed. Ratcheted over the 5 known ParticlePack offenders. ---
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "vfx-null-slot suite", () => { if (!DeNelle.Editor.Regression.VfxParticleNullSlotRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[vfx-null-slot] " + r); });

            // --- ENEMY RIG <-> CONTROLLER COHERENCE (2026-08-09): a Humanoid model on a
            // Generic-clip controller (or the reverse) T-poses and slides. The runtime
            // ALREADY detects this -- but only for an enemy that actually spawns in a play
            // session, so a rarely-spawned boss ships broken in silence. This asks the same
            // question statically, over every model in Resources/Enemies, and fails the gate.
            // It is what stopped the seven AccuRig intakes from being wired to Boss/LargeEnemy. ---
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "enemy-rig-coherence suite", () => { if (!DeNelle.Editor.Regression.EnemyRigControllerCoherenceRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[enemy-rig-coherence] " + r); });

            // --- BUILD CARD ART (WO-1010, 2026-08-09): a catalog row whose art does not
            // resolve renders as a bare LETTER on the card. The capture caught "Lumberyard"
            // as an "L" among illustrated neighbours — a content gap no gate could see, on
            // the exact screen testers called unreadable. Ratcheted: today's artless rows are
            // recorded debt, any NEW one fails. ---
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "build-card-art suite", () => { if (!DeNelle.Editor.Regression.BuildCardArtRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[build-card-art] " + r); });

            // --- DUNGEON ENCOUNTER FAMILY (WO-1001 slice 2): EncounterSpec.kind was
            // compared ONLY to "none" by DungeonBaker, and OutpostEnemyGroupSpawner's
            // id picker was four hardcoded hollow-* literals whose hand-written stats
            // ignored enemies.json outright -- so authoring "orc-group" SILENTLY SPAWNED
            // HOLLOWS. This oracle pins that every family table emits REAL non-boss
            // roster ids, that hollow-group still reproduces the retired picker's stream
            // exactly, that an unknown kind falls back LOUDLY, and that the baker/binder
            // still write the serialized kind the bake depends on ---
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "dungeon-encounter-family suite", () => { if (!DeNelle.Editor.Regression.DungeonEncounterFamilyRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[dungeon-encounter-family] " + r); });

            // --- TOWNSFOLK BODIES (owner ruling 2026-08-07): the whole town wandered on
            // TWO People-pack peasants; CastleTownsfolkInjector.BodyPool now names the 14
            // CraftPix bodies. Every failure in that chain is SILENT - a bad Resources path,
            // an unbuilt prefab, a body with no visible mesh, or a URP material with no
            // albedo bound all produce a warning + a grey capsule or a flat grey person, not
            // an error. The albedo half is the one that has actually shipped here before
            // (WO-719's white spire, the white Knight, the 73 gitignored VFX dependencies of
            // 2026-08-05), because a shader-only check passes an untextured URP mesh ---
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "townsfolk-bodies suite", () => { if (!DeNelle.Editor.Regression.TownsfolkBodyPoolRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[townsfolk-bodies] " + r); });

            // --- COLLECTOR UPGRADE LADDER (WO-936 Finding C, 2026-08-09): a placed
            // collector's tier tree is authored on the row its repo.collectorBuildingId
            // points at, not on itself — the live Lumber Mill upgrades through
            // 'lumbermill', which build-categories lockedIds RETIRES from the palette.
            // It therefore reads as dead content while being the sole home of a live
            // building's progression. Deleting it would not crash: BuildingUpgradeVM
            // falls through to ResourceBuildingProgression's legacy level curve and
            // silently draws a DIFFERENT ladder, with no log line and no symptom. ---
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "collector-ladder suite", () => { if (!DeNelle.Editor.Regression.CollectorLadderRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[collector-ladder] " + r); });

            // --- WO-1167 (2026-08-24): the build palette groups itself by catalog ROLE
            // (build-categories 'paletteGroups' — Producers/Storage/Trade/Civic + a trailing
            // Other bucket). Pins the owner's standing rule at both ends: a brand-new role
            // lands in Other with ZERO code change (driven through the real shipped
            // projection, BuildPaletteVM.GroupCards), and NO role literal may exist in the
            // palette C# — the membership lives in the data and only there (WO-1161). ---
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "palette-groups suite", () => { if (!DeNelle.Editor.Regression.BuildPaletteGroupsRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[palette-groups] " + r); });

            // --- 2026-08-10 wave-3 lanes. Each lane authored its oracle but left the
            // registration to the committer on purpose (this file is lane-fenced, so
            // nine agents editing it in parallel would collide). Registered here in the
            // same commit as the lane work, which is also what keeps [regression-marker]
            // green - an oracle written and never registered is a FAIL by design. ---
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "barracks-blanktown suite", () => { if (!DeNelle.Editor.BarracksBlankTownRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[barracks-blanktown] " + r); });
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "echo-hollow-route suite", () => { if (!DeNelle.Editor.EchoHollowRouteRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[echo-hollow-route] " + r); });
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "harvest-drip suite", () => { if (!DeNelle.Editor.HarvestDripRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[harvest-drip] " + r); });
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "hostile-green suite", () => { if (!DeNelle.Editor.HostileGreenCueRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[hostile-green] " + r); });
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "aggro-leash suite", () => { if (!DeNelle.Editor.AggroLeashRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[aggro-leash] " + r); });
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "dungeon-cam-958 suite", () => { if (!DeNelle.Editor.DungeonCameraTightRoomRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[dungeon-cam-958] " + r); });
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "camera-wall-occlusion suite", () => { if (!DeNelle.Editor.Regression.CameraWallOcclusionRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[camera-wall-occlusion] " + r); });
            // --- WO-2001: the Manage screen graph - a prerequisite JUMP returns to its ORIGIN (ruling 28)
            // while a plain BROWSE returns to its tree parent, Manage opens ON a tab (the launcher chooser
            // is retired), and there is exactly ONE Manage art loader. Registered by the COMMITTER, not by
            // the implementing lane: it correctly declined to edit this committer-fenced file and handed
            // the line over instead. ---
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "manage-navigation suite", () => { if (!DeNelle.Editor.Regression.ManageNavigationRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[manage-navigation] " + r); });
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "manage-queue-drawer suite", () => { if (!DeNelle.Editor.Regression.ManageQueueDrawerRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[manage-queue-drawer] " + r); });
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "manage-approved-launcher suite", () => { if (!DeNelle.Editor.Regression.ManageApprovedLauncherRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[manage-approved-launcher] " + r); });
            // --- WO-1418 Manage - Buildings re-layout: rail + selected card + BUILDING NOW; ten RED-first cases (Codex dev lane) ---
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "manage-buildings-card suite", () => { if (!DeNelle.Editor.ManageBuildingsCardRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[manage-buildings-card] " + r); });
            // --- WO-1571 Manage - a not-built card's BUILD button opens ITS OWN placement, never the Build Collections root ---
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "manage-build-door suite", () => { if (!DeNelle.Editor.ManageBuildDoorRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[manage-build-door] " + r); });
            // --- WO-1422 Manage - Defense and Research take the WO-1418 Buildings shape; the paged list is retired ---
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "manage-defense-card suite", () => { if (!DeNelle.Editor.ManageDefenseCardRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[manage-defense-card] " + r); });
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "manage-research-card suite", () => { if (!DeNelle.Editor.ManageResearchCardRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[manage-research-card] " + r); });
            // --- WO-1423 progression reachability: no authored gate may demand a village tier above VillageTierService.MaxTier ---
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "progression-reachability suite", () => { if (!DeNelle.Editor.ProgressionReachabilityRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[progression-reachability] " + r); });
            // --- WO-2003 / WO-2017 Heart surface: the spine has a direct route and ONE player-facing name ---
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "heart-surface suite", () => { if (!DeNelle.Editor.Regression.HeartSurfaceRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[heart-surface] " + r); });
            // --- WO-2004 requirements lane: a Heart Level's cost, PREREQUISITES and unlocks all resolve
            //     from heart-progression.json through one traced seam, and a level the data forgot is a
            //     named Fail AND a refusal. Before 2026-09-07 an unauthored level inside the ceiling cost
            //     0, and TryUpgrade skips the spend at cost 0 - so the Fail fired and the realm was granted
            //     free. Runs AFTER heart-surface, which owns the ladder cases ([ladder-*]). ---
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "heart-bundle suite", () => { if (!DeNelle.Editor.Regression.HeartUnlockBundleRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[heart-bundle] " + r); });
            // --- WO-2011 + ruling 21: the barracks BUILDING tier is the troop gate, and every troop is reachable ---
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "troop-reachability suite", () => { if (!DeNelle.Editor.TroopReachabilityRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[troop-reachability] " + r); });
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "manage-state-model suite", () => { if (!DeNelle.Editor.ManageStateModelRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[manage-state-model] " + r); });
            // --- WO-2005 BUILD inventory + filters (ALL/ECONOMY/DEFENSE/CRAFT/STORAGE/CIVIC), storage singleton, art-key mapping ---
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "build-inventory-filters suite", () => { if (!DeNelle.Editor.Regression.BuildInventoryFilterRegression.Run(out var r)) failures.Add(r); else log.AppendLine(r); });
            // --- 2026-09-06 Manage portrait lane: every id a Manage tab can DISPLAY resolves to a sprite, or is named on a DATED art exemption ---
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "manage-portrait-coverage suite", () => { if (!DeNelle.Editor.Regression.ManagePortraitCoverageRegression.Run(out var r)) failures.Add(r); else log.AppendLine(r); });
            // --- WO-1655: every id a Builder queue job can carry NAMES ITSELF in at least one catalog, so no row paints "Unknown structure" ---
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "queue-job-catalog-coverage suite", () => { if (!DeNelle.Editor.Regression.QueueJobCatalogCoverageRegression.Run(out var r)) failures.Add(r); else log.AppendLine(r); });
            // --- SEAM ORACLES (CLI driving plan section 1): every panel has a door; no authored field goes unread ---
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "panel-door suite", () => { if (!DeNelle.Editor.PanelDoorRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[panel-door] " + r); });
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "authored-field-reader suite", () => { if (!DeNelle.Editor.AuthoredFieldReaderRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[authored-field-reader] " + r); });
            // WO-2006 / OWNER_RULINGS_LOCKED sec.25 -- the same door question one layer below a panel:
            // a WIRED VERB (move/upgrade/sell on a placed structure) with no signpost is as dead to a
            // player as a panel with no spawner. RED on C3/C4/C5 before WO-2006.
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "placed-door suite", () => { if (!DeNelle.Editor.PlacedStructureDoorRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[placed-door] " + r); });
            // --- WO-1432: the honest-feedback thank-you DELIVERS 1000/1000/1000 against a near-cap bank (an EarnedIncome control proves the fixture bites), and a second claim is a traced no-op ---
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "honest-feedback-grant suite", () => { if (!DeNelle.Editor.HonestFeedbackGrantRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[honest-feedback-grant] " + r); });
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "honest-feedback-once suite", () => { if (!DeNelle.Editor.HonestFeedbackClaimOnceRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[honest-feedback-once] " + r); });
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "honest-feedback-surface suite", () => { if (!DeNelle.Editor.Regression.HonestFeedbackSurfaceRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[honest-feedback-surface] " + r); });
            // --- WO-1429: a refused primary falls through to the FREE melee sweep; the per-class table is gone ---
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "primary-fallback suite", () => { if (!DeNelle.Editor.Regression.PrimaryFallbackRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[primary-fallback] " + r); });
            // --- WO-2002: the common Manage renderer is DUMB - 16 banned shapes, each with a planted-fixture proof ---
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "manage-dumb-view suite", () => { if (!DeNelle.Editor.ManageDumbViewRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[manage-dumb-view] " + r); });
            // --- WO-1443 panel 8: the queue OVERLAY has three channel tabs (counts from the live ChannelSummary, never literal), numbered rows, and SPEED UP on the ONE existing TryInstantFinish path ---
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "manage-queue-panel8 suite", () => { if (!DeNelle.Editor.Regression.ManageQueuePanel8Regression.Run(out var r)) failures.Add(r); else log.AppendLine("[manage-queue-panel8] " + r); });
            // --- WO-1443: ONE heading per Manage screen (the host title binds the model breadcrumb; the shared renderer paints no copy on BUILD/ARMY/RESEARCH), and the selection band COLLAPSES when nothing is selected ---
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "manage-one-heading suite", () => { if (!DeNelle.Editor.Regression.ManageOneHeadingRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[manage-one-heading] " + r); });
            // --- OWNER RULING 26b: a full collector spills into its matching storage; nothing is ever burned ---
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "collector-overflow suite", () => { if (!DeNelle.Editor.Regression.CollectorOverflowRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[collector-overflow] " + r); });
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "public-navigation-retirement suite", () => { if (!DeNelle.Editor.Regression.PublicNavigationRetirementRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[public-navigation-retirement] " + r); });
            // --- WO-1500: the fresh-save FTUE is a STANDING fleet lane (five 2026-09-06 logs carried zero [Flow:Onboard*] lines), and the Bag's retired Map section is gone rather than labelled "coming" ---
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "freshsave-ftue-lane suite", () => { if (!DeNelle.Editor.Regression.FreshSaveFtueLaneRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[freshsave-ftue-lane] " + r); });
            // --- WO-1404 Journey deck subtitles carry state (Quests / Raids), one pure Core VM, change-only publisher (Codex dev lane) ---
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "journey-deck-subtitle suite", () => { if (!DeNelle.Editor.Regression.JourneyDeckSubtitleRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[journey-deck-subtitle] " + r); });
            // --- WO-1421 Journey deck is TWO cards: Dungeons / Realm Map / Season removed by owner ruling 2026-09-06 ---
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "journey-deck-two-card suite", () => { if (!DeNelle.Editor.Regression.JourneyDeckTwoCardRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[journey-deck-two-card] " + r); });
            // --- WO-1413 copy hygiene: fixture verbs, retired numbered combat faces, EMPTY/live skill binding, dialogue-twin parity, retired Pet/multiplier copy (Codex dev lane) ---
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "copy-hygiene suite", () => { if (!DeNelle.Editor.Regression.CopyHygieneRegression.Run(out var rCh)) failures.Add(rCh); else log.AppendLine("[copy-hygiene] " + rCh); });
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "pause-medieval-skin suite", () => { if (!DeNelle.Editor.Regression.PauseMedievalSkinRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[pause-medieval-skin] " + r); });
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "combat-item-picker suite", () => { if (!DeNelle.Editor.Regression.CombatItemPickerRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[combat-item-picker] " + r); });
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "settings-medieval-skin suite", () => { if (!DeNelle.Editor.Regression.SettingsMedievalSkinRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[settings-medieval-skin] " + r); });
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "gear-aura-carry suite", () => { if (!DeNelle.Editor.Regression.GearAuraCarryGateRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[gear-aura-carry] " + r); });
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "armor-store-window suite", () => { if (!DeNelle.Editor.Regression.ArmorStoreLockedWindowRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[armor-store-window] " + r); });

            // --- 2026-08-10 evening, minted from two live F8 captures while the owner played.
            // Same fencing reason as the block above: each lane authored its oracle, the
            // committer registers it. ---
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "tutorial-anchor-latch suite", () => { if (!DeNelle.Editor.Regression.TutorialAnchorLatchRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[tutorial-anchor-latch] " + r); });
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "tutorial-watchdog-bound suite", () => { if (!DeNelle.Editor.Regression.TutorialWatchdogBoundRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[tutorial-watchdog-bound] " + r); });
            // WO-1300 (2026-09-02): registered by the COMMITTER, per the fencing note above - the
            // lane that authored the suite is deliberately kept out of this file. Pins the
            // founding_defend publisher chain: TickScriptedWave is the SOLE publisher of
            // wave.tutorial_band_repelled and refuses to poll until _townWaveSpawnSettled, which is
            // set only AFTER an await that could fault - so an unguarded fault left the step waiting
            // on a signal whose only publisher could never run. Also pins the forbidden fixes.
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "tutorial-completion-publisher suite", () => { if (!DeNelle.Editor.Regression.TutorialCompletionPublisherRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[tutorial-completion-publisher] " + r); });
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "build-carousel-order suite", () => { if (!DeNelle.Editor.Regression.BuildCarouselTutorialOrderRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[build-carousel-order] " + r); });
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "build-first-use-guide suite", () => { if (!DeNelle.Editor.Regression.BuildFirstUseGuideRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[build-first-use-guide] " + r); });
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "founding-guide-wolf suite", () => { if (!DeNelle.Editor.Regression.FoundingGuideWolfBodyRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[founding-guide-wolf] " + r); });
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "hub-tree-aura suite", () => { if (!DeNelle.Editor.Regression.HubTreeAuraWithholdRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[hub-tree-aura] " + r); });
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "hud-class-fallback suite", () => { if (!DeNelle.Editor.Regression.HudHeroClassFallbackRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[hud-class-fallback] " + r); });
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "tutorial-guide-identity suite", () => { if (!DeNelle.Editor.Regression.TutorialGuideIdentityRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[tutorial-guide-identity] " + r); });
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "endstate-handoff suite", () => { if (!DeNelle.Editor.Regression.EndStateTransitionHandoffRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[endstate-handoff] " + r); });
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "wave-modal-safety suite", () => { if (!DeNelle.Editor.Regression.WaveModalSafetyRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[wave-modal-safety] " + r); });
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "town-suspend-floor suite", () => { if (!DeNelle.Editor.Regression.TownSuspendSceneFloorRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[town-suspend-floor] " + r); });
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "equipment-screen-layout suite", () => { if (!DeNelle.Editor.Regression.EquipmentScreenLayoutRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[equipment-screen-layout] " + r); });
            // --- WO-1133: the Bag is "The Armory Rail" - three abutting zones matching the ratified
            //     D3 ratios, every sentence in BOTH canon copies and MEASURED to fit its real box at
            //     the legibility floor, nothing interactive under MinTouchPx, the deleted preview box
            //     / VIEW GEAR ribbon / tab row still deleted, and the hero preview mounted ONLY
            //     through the DrewContent evidence gate (a blank RT and a drawn hero are the same
            //     pixels, which is how the owner's empty navy rectangle shipped). ---
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "inventory-armory-rail suite", () => { if (!DeNelle.Editor.Regression.InventoryArmoryRailRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[inventory-armory-rail] " + r); });
            // --- WO-1664: five controls were AUTHORED under ElarionUiKit.MinTouchPx and shipped
            //     only because ClampMinTouch rescued them at runtime -- the owner's Seeker printed
            //     five CLAMP FIRED lines, byte-identical across two consecutive APKs. The gate-time
            //     rule (LayoutOracle's SUB-TOUCH-FLOOR BAND) already existed and already ran; two
            //     capture entry points computed its verdict and never reported it. This suite pins
            //     the three DRIVER bands arithmetically and pins that those two paths now read the
            //     tally, so the same class cannot go quiet again. ---
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "touch-floor-authoring suite", () => { if (!DeNelle.Editor.Regression.TouchFloorAuthoringRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[touch-floor-authoring] " + r); });
            // --- WO-1059: the hero preview must frame the MODEL. The captured defect was
            //     ComputeBounds summing a cloned WeaponTrail's world-space AABB, which aimed the
            //     preview camera at the midpoint between the world origin and the rig origin and
            //     rendered an empty frustum (F8 seq 3585/3586). Case A measures the mechanism,
            //     Case B drives the real rig, Case C pins the neutralise-then-frame ordering. ---
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "hero-preview-framing suite", () => { if (!DeNelle.Editor.Regression.HeroPreviewFramingRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[hero-preview-framing] " + r); });
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "dungeon-mover-ownership suite", () => { if (!DeNelle.Editor.Regression.DungeonMoverOwnershipRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[dungeon-mover-ownership] " + r); });
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "hero-bar-rebind suite", () => { if (!DeNelle.Editor.Regression.HeroBarClassRebindRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[hero-bar-rebind] " + r); });
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "mage-spell-kit suite", () => { if (!DeNelle.Editor.Regression.MageSpellKitAuthoringRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[mage-spell-kit] " + r); });
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "guide-lead-move suite", () => { if (!DeNelle.Editor.Regression.GuideLeadMovementRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[guide-lead-move] " + r); });
            // WO-1336 (2026-09-03): registered by the COMMITTER, per this file's fencing rule.
            // Pins that the guide's lead carrot is placed along a REAL NavMesh route rather than a
            // straight-line projection. Pet.MoveToward calls _agent.Move(), which slides and computes
            // NO path - so before this, any carving structure on the line stopped the Echo dead
            // (owner: "there is a tower in his way so gets stuck and doesn't move"). It had been
            // reproducing for weeks at the same spot; the escort never routed, it just never had
            // anything in its way. The oracle's dogleg case fails against the old straight-line rule.
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "guide-lead-route suite", () => { if (!DeNelle.Editor.Regression.GuideLeadRoutingRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[guide-lead-route] " + r); });
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "town-movement-floor suite", () => { if (!DeNelle.Editor.Regression.TownMovementFloorRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[town-movement-floor] " + r); });
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "one-guide-body suite", () => { if (!DeNelle.Editor.Regression.OneGuideBodyRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[one-guide-body] " + r); });
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "wall-adjacency suite", () => { if (!DeNelle.Editor.Regression.WallAdjacencyRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[wall-adjacency] " + r); });
            // --- WO-1105: the ranged primary is DERIVED (strike-shaped effect whose authored range
            //     far exceeds measured melee reach), never a per-class table; ranger.q is a costed
            //     cooldown'd ranged strike; and the runtime weapons catalog carries NO crossbow
            //     while the R4a exclusion stands (the Generate Gear Catalog menu would import 125). ---
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "ranged-primary suite", () => { if (!DeNelle.Editor.Regression.RangedPrimaryRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[ranged-primary] " + r); });
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "ranged-facing suite", () => { if (!DeNelle.Editor.Regression.RangedFacingLockRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[ranged-facing] " + r); });
            // --- ICON_CATALOG 2026-08-16: no MAGE ability may paint KNIGHT art. Resolves every mage
            //     ability through the real ResolveKey(id, effect) -> Resolve -> DefaultSprite chain and
            //     compares the resulting Sprite REFERENCE to attack_sword / icon_shield / icon_combat. ---
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "mage-ability-icons suite", () => { if (!DeNelle.Editor.Regression.MageAbilityIconRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[mage-ability-icons] " + r); });
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "knight-heal-icon suite", () => { if (!DeNelle.Editor.Regression.KnightHealIconRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[knight-heal-icon] " + r); });
            // --- WO-1166: the hero-select cards are a HAND-MIRROR of abilities.json (Onboarding cannot
            //     reference AbilityCatalog), and had drifted: the mage advertised Frost Nova / Arcane Bolt /
            //     Healing Beacon on a slot letter "F" that does not exist. This pins every advertised name +
            //     slot against AbilityCatalog.GetLoadout at test time, both directions. ---
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "hero-kit-mirror suite", () => { if (!DeNelle.Editor.Regression.HeroKitMirrorRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[hero-kit-mirror] " + r); });
            // --- WO-1104: the spire-plans moment subscribes to the PlansCollected seam, plays ONCE
            //     ever, registers with the arbiter, never touches roster/unlock state, and reads its
            //     speaker from EchoRosterCatalog rather than a name literal. ---
            // NOTE: this oracle declares `namespace DeNelle.Editor` (not .Regression) -- cite it as
            // written rather than "correcting" the file to match its neighbours mid-wave.
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "spire-celebration suite", () => { if (!DeNelle.Editor.SpirePlansCelebrationRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[spire-celebration] " + r); });
            // --- Owner VFX bans (2026-08-16: "Spell_Fire_6 - Do Not use anywhere"): Spell_Fire_6 +
            //     'Magic circle sun loop' stay dead - source lint over all runtime+editor code plus a
            //     baked-catalog GUID scan (both VFXCatalog.asset and HovlVfxCatalog.asset). Colour
            //     Variants are deliberately NOT banned (scope note in the suite header). Replacement
            //     pick = BigExplosion (owner-tagged the same day).
            //     ⚠ THE ONLY REGISTRATION OF THIS SUITE (audit 2026-08-15). It was registered TWICE
            //     here, so every run emitted two [banned-vfx] lines and inflated the suite count; the
            //     duplicate is now pinned by BannedVfxRegression's own single-registration case, which
            //     FAILS if a second call site reappears in this file.
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "banned-vfx suite", () => { if (!DeNelle.Editor.Regression.BannedVfxRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[banned-vfx] " + r); });
            // --- WO-997 class resource economy: every playable class authors a valid resource block,
            //     every cost fits its owning class's pool, at least one costed non-ultimate per kit
            //     (kills the "everything is cooldown-gated" gap), both abilities.json copies identical. ---
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "class-resource suite", () => { if (!DeNelle.Editor.Regression.ClassResourceRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[class-resource] " + r); });
            // --- Owner 2026-08-20 ("mana does not draw down on use ... i can spam spells non stop"):
            //     the SPEND half of that economy, asserted on a LIVE HeroAbilities probe, not on JSON —
            //     a costed cast charges EXACTLY its effective cost, an unaffordable cast is refused and
            //     charges nothing, the FlowTrace charge/refusal lines survive, and the producer ->
            //     ManaExact -> kit ManaFill presentation chain is intact. Balance-neutral by design. ---
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "mana-spend suite", () => { if (!DeNelle.Editor.Regression.ManaSpendRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[mana-spend] " + r); });
            // --- WO-973 Bryn bubble legibility: code defaults small + scene copy agrees + ratio vs the
            //     shipped TownsfolkBubble. Case 2 is a DRIFT CATCHER that stays red until the
            //     Dungeon_HealersCottage bake rewrites the serialised copy — an honest red, not noise. ---
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "wanderer-bubble suite", () => { if (!DeNelle.Editor.Regression.WandererBubbleLegibilityRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[wanderer-bubble] " + r); });

            // --- COST-BASKET SEPARATION (WO-947, owner economy ruling 2026-08-10): regular
            // structures cost wood + iron and NEVER crystals; magical/ethereal structures are
            // crystal-based; no basket ever touches all three. Enforced on the AUTHORED
            // baskets so any NEW catalog row obeys the ruling from day one. Five rows are
            // carried as a dated, cited pending-pin list because their classification is an
            // OPEN OWNER call (WO-947 section 4) -- the list can only shrink; the oracle FAILS
            // if a listed row stops violating without being removed.
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "cost-basket suite", () => { if (!DeNelle.Editor.Regression.CostBasketSeparationRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[cost-basket] " + r); });

            // --- ECONOMY SINK CAP (2026-08-21 sink pass): NO AUTHORED COST MAY EXCEED THE
            // MAXIMUM BANKABLE AMOUNT OF THAT RESOURCE. A 3,000-wood upgrade under a 2,000-wood
            // cap is UNCOMPLETABLE -- the player can never hold enough at once, the button never
            // lights, and nothing in the game says why. Same silent-wall shape as the day-1 daily
            // quest that force-returned forever because it could never tick; the symptom is the
            // ABSENCE of an event, so only a gate can see it. Also pins the two constraints the
            // sink pass leaned on: the storage ladder must be SELF-affordable (a container upgrade
            // is paid at the level BELOW the one it buys), and troop training -- the recurring
            // loop -- must stay affordable at ZERO storage. Crystals/coins are UNCAPPED by design
            // (TownBankCapacity.UncappableResources) and are deliberately out of scope.
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "sink-cap suite", () => { if (!DeNelle.Editor.Regression.EconomySinkCapRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[sink-cap] " + r); });

            // --- VFX POOL SHAPE (WO-955, 2026-08-10): a pooled host was DESTROYED while it
            // still sat in a free list, and the next Acquire dereferenced it -- captured twice
            // in one session (HeroHpStateAura in town after arena deaths, then EnemyAuraVFX in
            // dg_ember_deep, a scene the first caller never touched: the pool hangs off the
            // DDOL singleton, so a poisoned list outlives the scene that poisoned it). Asserts
            // both halves of VfxPoolGuard: drain past corpses on the way out, and refuse to
            // enqueue any host that is not parked under the pool root on the way in.
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "vfx-pool-shape suite", () => { if (!DeNelle.Editor.Regression.VfxPoolShapeRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[vfx-pool-shape] " + r); });

            // --- TALENT ICON MAP (WO-1023, 2026-08-15): talent-icon-map.json had NO
            // oracle -- 83/83 coverage, unique art per talent, resolvable iconPaths and
            // the byte-identical Resources/StreamingAssets twin were all true by care
            // alone (the WO-996 armor.json shape: Resources wins at runtime, so drift
            // is invisible in the Editor). Also pins the two WO-1023 re-tags (ranger
            // Venomcraft off Rogue7, shared Arcane Bolt off Arcanist1) so the
            // duplicate-icon defect cannot return.
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "talent-icons suite", () => { if (!DeNelle.Editor.Regression.TalentIconMapRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[talent-icons] " + r); });

            // --- WO-1294 ONE-ICON IDENTITY. [talent-icons] above guards the TREE side only, and
            // concept-icons.json guards nothing at all -- so a skill could show one picture on its
            // talent node and a DIFFERENT one in the hot-swap slot and combat HUD with both files
            // individually clean. That is exactly what had happened to the shared/universal pool.
            // This suite closes the gap BETWEEN the two files, and additionally pins the three-slot
            // hot-swap rail and the nine canonical troop portraits.
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "concept-icons suite", () => { if (!DeNelle.Editor.Regression.ConceptIconIdentityRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[concept-icons] " + r); });

            // --- SHIPPED SURFACE GATE (2026-08-16): three surfaces that SHIPPED in the release
            // APK while believing they were dev-only, off, or wired up. (a) HeroGaitForensics
            // gated on a RAW PlayerPrefs key that DEFAULTED ON and was in no flag table, so a
            // per-frame boxed string.Format + CSV write ran on players' devices with no reachable
            // off-switch -- now a declared flag, default OFF (flagged off, NOT stripped: §12).
            // (b) The Settings screen-shake toggle was INERT IN BOTH DIRECTIONS -- it wrote a key
            // nothing read while the gameplay bridge read a key nothing wrote -- so a visible
            // accessibility control moved no shake at all. (c) JupiterSwapBootstrap auto-spawned a
            // crypto swap CTA gated by NOTHING, in the build store-hardening had stripped every
            // other crypto surface out of. All three defaults are pinned as SOURCE TEXT on purpose:
            // FeatureFlags.Get reads PlayerPrefs first, so a runtime read describes the gate
            // machine, never what ships.
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "shipped-surface-gate suite", () => { if (!DeNelle.Editor.Regression.ShippedSurfaceGateRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[shipped-surface-gate] " + r); });

            // (BANNED VFX used to be registered a SECOND time here - removed 2026-08-15. The suite
            //  is registered ONCE, above with the other VFX oracles; two call sites emitted two
            //  [banned-vfx] lines per run and inflated the suite count. Do not re-add.)

            // --- HERO DEATH SEVERITY (audit 2026-08-15): a NORMAL hero death must not raise an F8
            // ERROR. HeroHealth's death-freeze state dump was a FlowTrace.Fail because break-log was
            // errors-only on device, so the most common event in the game woke a live triage seat
            // every time the owner died. Pins the new FlowTrace.Capture channel (INFO severity +
            // kind:"note" straight into break-log.jsonl) and the F8 daemon's skip of note rows --
            // two-sided, so DELETING the dump fails just as loudly as restoring the Fail.
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "hero-death-severity suite", () => { if (!DeNelle.Editor.Regression.HeroDeathSeverityRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[hero-death-severity] " + r); });

            // --- DEV GRANT UNCAPPED (audit 2026-08-15): the dev resource grants resolved
            // GetMethod("GrantSpendable") BY STRING -- the TownBankCapacity-clamped path -- so a
            // 50,000 wood dev grant into a 2,500 bank silently lost ~95% of itself. Reflection by
            // string is invisible to the compiler and to ordinary source lint, so this oracle reads
            // the literal method-name strings the dev surfaces pass to GetMethod.
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "dev-grant-uncapped suite", () => { if (!DeNelle.Editor.Regression.DevGrantUncappedRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[dev-grant-uncapped] " + r); });

            // --- ECHO ENGAGE DIALOGUE (WO-1030, 2026-08-16): options were clipped by a
            // text+options sum clamp (48px rows under the touch floor, fraction overlay) and
            // Echo portraits fell to silhouette for want of speaker records. Pins the
            // reserve-first sizing tokens, the speaker-record portraits, and the fit math.
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "echo-engage-dialogue suite", () => { if (!DeNelle.Editor.Regression.EchoEngageDialogueRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[echo-engage-dialogue] " + r); });

            // --- ATTACHMENT OFFSETS (WO-994, 2026-08-16): the 08-16 harness audit found
            // NOTHING covered AttachmentOffsetRegistry or seated-prop transforms - the
            // owner's dialed shield seat could vanish (row lost, fullOverride flipped,
            // mirror unparseable) with every marker green. Pins the shield_A rows through
            // the real Resources-first read path + the WO-994 seat-drift tripwire wiring.
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "attachment-offset suite", () => { if (!DeNelle.Editor.Regression.AttachmentOffsetRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[attachment-offset] " + r); });

            // --- STORE ASSET MESH READABILITY (WO-1284, 2026-08-30): generalises
            // AttachmentOffsetRegression.Case11 from the one PROD-019 shield to the whole
            // held-prop catalogue. An FBX imported with isReadable=0 keeps its verts CPU-side
            // in the EDITOR and returns ZERO of them in a player build, so every orientation
            // measurement degrades silently and no editor-side proof can see it.
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "store-mesh-readable suite", () => { if (!DeNelle.Editor.Regression.StoreAssetMeshReadabilityRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[store-mesh-readable] " + r); });

            // --- GEAR PROP RENDERS (owner report 2026-08-18, build 2026.08.19.331306:
            // "shield is missing and sword is now wrong"). The device trace carried
            // "parent-scale compensate: ... -> worldBounds=(0, 0, 0)" — a held prop with no
            // volume at all. Every existing gate was green for that build, because they all
            // ask "did the pipeline RUN" and none asks "does what it produced have VOLUME".
            // Also pins the address half: c072e5736 records weapons.json naming
            // "gear/weapon/ShieldWithItemLogic" while no group published it, so the load
            // failed and the equip fell back to the legacy mesh — the swap looked done and
            // changed nothing, silently, in a shippable tree.
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "gear-prop-renders suite", () => { if (!DeNelle.Editor.GearPropRendersRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[gear-prop-renders] " + r); });

            // --- HERO REMOTE CONTENT (WO-1187, 2026-09-03): ~100 MB of hero art shipped in the
            // initial download before the player had even picked a class, because BOTH hero loaders
            // resolved Resources BEFORE Addressables — making any remote hero group a guaranteed
            // NO-OP with no error anywhere, and making the move look like it simply did not work.
            // Pins the loader statement ORDER (a SOURCE oracle on purpose: once both paths resolve,
            // no runtime assertion can tell "Addressables first" from "Resources first"), that no
            // hero .fbx/.fbm/atlas is left under Assets/Resources/Heroes outside the documented
            // local allowlist (Props/, Emotes/, SC_*.prefab, the .controller files), that every
            // Hero_* group binds Remote.BuildPath/Remote.LoadPath exactly like Enemy_Art, and that
            // every hero .fbx in Assets/HeroContent is reachable at "Heroes/<slug>".
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "hero-remote-content suite", () => { if (!DeNelle.Editor.Regression.HeroRemoteContentRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[hero-remote-content] " + r); });

            // --- ECHO WORLD PRESENCE (WO-1108 Lane B, 2026-08-16): the owner's rule is
            // "it takes you to the gate, gives you your dialogue, then it disappears... The
            // only time it reappears is after your battle." Until this WO there was NO
            // despawn path for a pet anywhere, and TWO independent appearance owners. Drives
            // the real state machine (body present during the escort -> gone on completion ->
            // back exactly once after a battle) and pins the single-owner rule by scanning
            // every runtime file for a second PetDeployer.SummonAt caller.
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "echo-world-presence suite", () => { if (!DeNelle.Editor.Regression.EchoWorldPresenceRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[echo-world-presence] " + r); });

            // WO-1380: Echo Guides and the 24 memory lines. The Echo does not fight;
            // it REMEMBERS. Six Echoes x four raid targets = the exact 24 lines the owner
            // ruled ALL 24 ship or the feature does not. Narrative only; no Guide grants
            // a stat, yield or combat effect in V1. Corvin at the Forsaken Camp, Aldwin
            // at the Iron Bastion, Doran's double-recognition stonework — the three canon
            // quotations must never be reworded. Player name = nil (verified no first-name
            // field anywhere). Both JSON copies byte-identical. The Guide picker button
            // must have a real band, not zero pixels. No second spawner; EchoWorldPresence
            // remains sole appearance owner, and the Guide adds VOICE only.
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "echo-guide-memories suite", () => { if (!DeNelle.Editor.Regression.EchoGuideMemoryRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[echo-guide-memories] " + r); });

            // --- EQUIP DRAWER CONTENTS (WO-1061, 2026-08-22): the equip drawer listed NOTHING,
            // so the player could not change weapons — and an item the hero was WEARING was
            // absent from its own slot's list. Measures the ROW COUNT the drawer produces for
            // known fixtures (grant path through the real InventoryStore, hand split, class
            // gate, and a true-zero anti-tautology case), because a suite that merely asserted
            // the builder runs would have passed on every day this defect shipped.
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "equip-drawer-contents suite", () => { if (!DeNelle.Editor.Regression.EquipDrawerContentsRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[equip-drawer-contents] " + r); });

            // WO-1110: the raid's three non-victory exits must PAY THE SAME (death used to
            // forfeit razing credit that retreat paid), Start must bind the clock-expiry exit
            // BEFORE it builds the HUD (an unguarded presentation throw was the raid's only
            // exitless state), and the four named raid catches must not swallow silently again.
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "raid-exit-parity suite", () => { if (!DeNelle.Editor.Regression.RaidExitParityRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[raid-exit-parity] " + r); });

            // WO-1437 (P0): the sibling above asks whether each raid exit PAYS correctly. This
            // one asks the question none of them did - CAN THE PLAYER GET OUT AT ALL. The owner
            // won a raid on 2026-09-06 and could not leave it: every part worked, and nothing
            // asserted that the session as a whole terminates. It MEASURES the raid clock across
            // real Update ticks, pins both hero-death readers onto RaidScoring.RaidInProgress
            // instead of the faction flag the victory claim flips (the same scene read
            // enemyOwned=True at 12:59:45 and False at 13:02:47, and only the first settled), and
            // requires a non-view stranding watchdog so no exit's only owner is an EndState panel
            // that any other modal can destroy.
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "raid-terminal-state suite", () => { if (!DeNelle.Editor.Regression.RaidTerminalStateRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[raid-terminal-state] " + r); });

            // --- TROOP TARGET PREFERENCE (WO-1438, 2026-09-06): the owner's warband spent a raid
            // chewing wall panels outward from a breach it never walked through. Proven from her
            // own capture, not inferred - `accepted[unit=1,struct=17] ... won='Wall_Outer_SS_11'
            // dist=4.2m preferStruct=False`: a live defender was inside the sweep and a wall
            // 4.2 m away won, because a hostile STRUCTURE competed in the same nearest-wins
            // bucket as a live body for every non-siege role. Pins the extracted pick rule
            // itself (RaidAssaultAi.PreferUnit in Breach - the WO-1595 live pick rule NearestHostile
            // calls via PickBucket) so the suite cannot stay green while the selector drifts, and
            // pins the anti-pinning half: an unreachable defender must NOT be preferred, or the
            // troop pushes into an intact wall with NoObstacleAvoidance and freezes.
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "troop-target-preference suite", () => { if (!DeNelle.Editor.TroopTargetPreferenceRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[troop-target-preference] " + r); });

            // WO-1595 - raid assault: Peel > Breach > Push spire; no wall-ring farm after
            // breach; formation Front ahead of Ranged. Pins the RaidAssaultAi pure helpers the
            // live TroopController / TroopDeployer call. Grok worktree lane, merged 2026-09-07
            // by explicit path from 7879bc2e8 (this registration hand-applied - the worktree's
            // DataRegression.cs also carried a WO-1593 suite that does not exist here).
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "raid-assault-ai suite", () => { if (!DeNelle.Editor.RaidAssaultAiRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[raid-assault-ai] " + r); });

            // --- COLLECTOR STACK PROPS (2026-08-16): CollectorStackPropCatalog.cs told
            // everyone to "place the asset at Assets/Resources/Collectors/..." and nobody
            // ever did - the folder did not exist and git history shows the asset was
            // never added on any branch. So EnsureCatalog() resolved null on every run and
            // every farm/lumbermill/forge silently drew the abstract fill bar instead of
            // its diegetic prop pile, for months, with nothing red. A graceful degradation
            // with no gate over it is indistinguishable from working software. Pins BOTH
            // branches: the asset exists with the owner's 2026-08-16 picks committed as
            // GUIDs (a TEXT assertion - the KayKit pack is gitignored, so a loaded-
            // reference check would go red on a pack-less machine for a non-defect), AND
            // TryGet still reports null-prop / unmapped rows as NOT FOUND so that machine
            // keeps the fill bar rather than reaching Instantiate(null).
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "collector-props suite", () => { if (!DeNelle.Editor.Regression.CollectorStackPropCatalogRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[collector-props] " + r); });

            DeNelle.Core.Diagnostics.Guard.Try("Regression", "placed-upgrade-page suite", () => { if (!DeNelle.Editor.PlacedUpgradePageTruthRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[placed-upgrade-page] " + r); });

            // WO-1041/WO-1042 — the dungeon's justification, pinned. Gems must stay unbuyable
            // (the Jeweler was SELLING all three for gold before this ticket, which voided the
            // pillar's whole thesis); the polish job must stay unbuyable-to-completion (a paid
            // instant resolve of a random outcome is a loot box, owner ruling 2026-08-16); every
            // completed run must pay; the grade must move ODDS ONLY; free/ad/paid must share one
            // odds table with DISCLOSED percentages derived from it.
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "dungeon-gem-exclusivity suite", () => { if (!DeNelle.Editor.DungeonGemExclusivityRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[dungeon-gem-exclusivity] " + r); });

            // 2026-08-16 combat silo: every enemy must RESOLVE a non-null type-VFX set (the
            // per-prefab assignment never landed, so every telegraph/sound/hit cue was dead);
            // exactly ONE authority may field a wave's heavy (wave 5 fielded an authored 1050 HP
            // troll AND a generated elite); and a boss spawn id must resolve to a real marker or
            // fail LOUDLY (the hardcoded "spawn-0" could never match, so the boss entered from an
            // arbitrary gate behind a Debug.LogWarning F8 never saw).
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "combat-cue-authority suite", () => { if (!DeNelle.Editor.Regression.CombatCueAuthorityRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[combat-cue-authority] " + r); });

            // 2026-08-16: the ranger's Attack/Cast/CastUpper all bound Ranger_Aim_Idle (a static pose) so every shot froze the hero mid-aim; pins the real bow clip in the BUILT controller AND its generator.
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "ranger-bow-fire suite", () => { if (!DeNelle.Editor.Regression.RangerBowFireRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[ranger-bow-fire] " + r); });
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "class-primary-block suite", () => { if (!DeNelle.Editor.Regression.ClassPrimaryAndKnightBlockRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[class-primary-block] " + r); });

            DeNelle.Core.Diagnostics.Guard.Try("Regression", "raid-repeat-clear suite", () => { if (!DeNelle.Editor.Regression.RaidRepeatClearRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[raid-repeat-clear] " + r); });
            // WO-728: the per-camp raid COOLDOWN — the only bound on the game's one unbounded
            // crystal faucet (raid loot is food + crystals, zero wood/iron). Pins that the window
            // survives a real save/load cold boot, that a BACKWARDS clock can never shorten it,
            // that the service reads TimeSource and never DateTime.UtcNow (invisible to every
            // behavioural assertion — only a source-lint can see it), that a camp on cooldown is
            // refused at the one door and told so in canon WORDS (the owner is colourblind; a tint
            // is not a signal), and that the owner-ruled 4h/8h/12h + 5/20/45min numbers agree
            // between scene-configs.json and the code fallback table.
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "raid-cooldown suite", () => { if (!DeNelle.Editor.Regression.RaidCooldownRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[raid-cooldown] " + r); });
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "heartfire suite", () => { if (!DeNelle.Editor.Regression.HeartfireRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[heartfire] " + r); });
            // --- WO-1419 the Heart plate paints flame ICONS (ember medallion), not [*] [ ] ASCII pips ---
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "heartfire-pips suite", () => { if (!DeNelle.Editor.Regression.HeartfirePipsRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[heartfire-pips] " + r); });

            // 2026-09-04 (coverage audit before the production regression): commit 1ef5f6ad4 added
            // fourteen suites and registered FOUR. The ten below existed on disk, compiled (or did
            // not - two of them referenced runtime that had never landed, and nothing noticed,
            // because nothing ran them) and never executed once. A suite that is not registered is
            // not coverage; it is a file. Registered here in the order the raid economy map reads
            // (docs/PROGRAM_RAID_ECONOMY_2026-09-04.md): loot -> gold arrow -> payout -> escalation
            // -> season XP -> funnel -> starter army -> discoverability -> hire -> away summary.
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "raid-loot-currency suite", () => { if (!DeNelle.Editor.Regression.RaidLootCurrencyRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[raid-loot-currency] " + r); });
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "raid-gold-arrow suite", () => { if (!DeNelle.Editor.Regression.RaidGoldArrowRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[raid-gold-arrow] " + r); });
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "raid-payout-visibility suite", () => { if (!DeNelle.Editor.Regression.RaidPayoutVisibilityRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[raid-payout-visibility] " + r); });
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "raid-escalation suite", () => { if (!DeNelle.Editor.RaidEscalationRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[raid-escalation] " + r); });
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "raid-selection-spoils suite", () => { if (!DeNelle.Editor.Regression.RaidSelectionSpoilsRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[raid-selection-spoils] " + r); });
            // WO-1442 - the raid camp list's GEOMETRY (a different axis from the spoils suite's
            // words + band-height case F): card rects measured on a live canvas at four camps
            // AND at eight, plus the source guards for the three defects on the 2026-09-06 frame.
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "raid-selection-layout suite", () => { if (!DeNelle.Editor.Regression.RaidSelectionLayoutRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[raid-selection-layout] " + r); });
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "raid-season-xp suite", () => { if (!DeNelle.Editor.Regression.RaidSeasonXpRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[raid-season-xp] " + r); });
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "raid-funnel suite", () => { if (!DeNelle.Editor.Regression.RaidFunnelRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[raid-funnel] " + r); });
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "starter-army-grant suite", () => { if (!DeNelle.Editor.Regression.StarterArmyGrantRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[starter-army-grant] " + r); });
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "raid-discoverability-copy suite", () => { if (!DeNelle.Editor.Regression.RaidDiscoverabilityCopyRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[raid-discoverability-copy] " + r); });
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "hire-reinforcements suite", () => { if (!DeNelle.Editor.HireReinforcementsRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[hire-reinforcements] " + r); });
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "away-summary-report suite", () => { if (!DeNelle.Editor.Regression.AwaySummaryReportRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[away-summary-report] " + r); });

            // WO-1361 (P0, save data loss): the first oracle at the persistence seam. 17 real-id
            // BaseLayout records must survive save -> reload -> migrate (from v14 and from current),
            // an absent/null field must not replace a populated list, and the only sanctioned public
            // clearer is ResetToNewGame. Before tonight no suite asserted more than ONE record.
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "baselayout-roundtrip suite", () => { if (!DeNelle.Editor.Regression.BaseLayoutRoundTripRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[baselayout-roundtrip] " + r); });

            // WO-1387 (owner 2026-09-04: "training free ... just time ... gold is to hire mercenaries if they
            // dont want to wait"): a Train or troop-Upgrade job enqueues with a ZERO wallet; the ONLY gold
            // spend on the Train line is the instant-finish skip. Proven RED first (mutations in the suite header).
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "training-costs-time-only suite", () => { if (!DeNelle.Editor.TrainingCostsTimeOnlyRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[training-costs-time-only] " + r); });
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "spawn-budget-vfx-warm suite", () => { if (!DeNelle.Editor.Regression.SpawnBudgetAndVfxWarmRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[spawn-budget-vfx-warm] " + r); });

            DeNelle.Core.Diagnostics.Guard.Try("Regression", "forge-shelf-kind suite", () => { if (!DeNelle.Editor.Regression.ForgeShelfClassKindRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[forge-shelf-kind] " + r); });

            // PROD-003 (2026-08-18): the Realm Store storefront is BAKED into the hub, so a gate on
            // the placer cannot see a stale bake — and there was one: an FBX axis re-import left the
            // saved collider describing a mesh shape that no longer existed, and every code-level
            // check stayed green. This pins the ARTIFACT: present exactly once, standing where the
            // producer says, fit to the town's height cadence, seated, with its door, NOT an
            // IDamageableStructure, and absent from both dual copies of structures-catalog.json and
            // build-categories.json (a catalog row is the failure the ticket exists to prevent).
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "realm-storefront suite", () => { if (!DeNelle.Editor.Regression.RealmStorefrontRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[realm-storefront] " + r); });
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "welcome-back-doors suite", () => { if (!DeNelle.Editor.Regression.WelcomeBackDoorsRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[welcome-back-doors] " + r); });

            // --- THE ORACLE THAT GUARDS THIS FILE: distinct markers, no unregistered
            // oracle, no gate script grepping a marker nobody emits. Registered LAST so
            // it sees the fully-built registry above it (it reads SOURCE, not runtime
            // state, so its own registration line is what satisfies its self-reference).
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "enemy-warm-order suite", () => { if (!DeNelle.Editor.Regression.EnemyWarmOrderRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[enemy-warm-order] " + r); });
            // WO-1303: the family PRE-FETCH key. EnemyAnimatorLateBinder passed the controller
            // name where the model belongs, so every skeleton/orc/large humanoid asked for a
            // label that does not exist - an InvalidKeyException per spawn and no family fetch.
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "enemy-family-label suite", () => { if (!DeNelle.Editor.Regression.EnemyFamilyLabelRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[enemy-family-label] " + r); });
            // WO-1315 follow-up: NO player-build entry point may call the target-less
            // EnsureBuilt overload. Addressables builds for the ACTIVE editor target, so a
            // call that cannot name its platform ships another platform's catalog with every
            // marker green - five occurrences of that class between 2026-08-18 and 2026-09-02.
            // Source-only, so it catches the sixth without building anything.
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "content-build-target suite", () => { if (!DeNelle.Editor.Regression.ContentBuildTargetRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[content-build-target] " + r); });
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "npc-idle-controller suite", () => { if (!DeNelle.Editor.Regression.NpcIdleControllerRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[npc-idle-controller] " + r); });
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "spawn-area-enemy-ids suite", () => { if (!DeNelle.Editor.Regression.SpawnAreaEnemyIdRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[spawn-area-enemy-ids] " + r); });
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "regression-marker suite", () => { if (!DeNelle.Editor.Regression.RegressionMarkerRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[regression-marker] " + r); });
            // WO-838 Phase E: the raid-base wall art must be reachable from TRACKED assets,
            // never from an FBX-embedded material. This is the ONLY detector for the
            // white-slab class — MagentaGuard is structurally blind to a textureless-but-
            // valid URP/Lit material on a non-ground renderer, and nothing shows on screen.
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "raid-wall-material suite", () => { if (!DeNelle.Editor.Regression.RaidWallMaterialRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[raid-wall-material] " + r); });
            // --- WO-1121 owner rulings 2026-08-21: the price ceiling is $49.99 (the $4.99 cap was
            //     EARLY-ACCESS, not permanent) and a pack above $4.99 requires a connected wallet,
            //     enforced on the CHARGE PATH and not in the UI alone. Also pins the vapor rule on
            //     the browsable shelf and the same-day "no glimmer in any pack" ruling. ---
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "buy-gate suite", () => { if (!DeNelle.Editor.Regression.BuyGateAndPriceLadderRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[buy-gate] " + r); });
            // --- WO-1246: a VISIBLE SKU cannot sell nothing. Unreadable packs.json / battle_monthly.json
            //     is a FAIL, never a quiet green (WO-1138). Live grant drives ApplyPackContents for
            //     every PackCatalog.IsOnBrowsableShelf row (the same helper PackStore uses). ---
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "store-sku-grant suite", () => { if (!DeNelle.Editor.Regression.StoreSkuGrantRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[store-sku-grant] " + r); });
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "monetization-activation suite", () => { if (!DeNelle.Editor.Regression.MonetizationActivationRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[monetization-activation] " + r); });
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "mainnet-canary suite", () => { if (!DeNelle.Editor.Regression.MainnetCanaryRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[mainnet-canary] " + r); });
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "store-commerce-state suite", () => { if (!DeNelle.Editor.Regression.StoreCommerceStateRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[store-commerce-state] " + r); });
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "store-presentation-localization suite", () => { if (!DeNelle.Editor.Regression.StorePresentationLocalizationRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[store-presentation-localization] " + r); });
            // --- WO-1323: the owner opened the Night Market signed in as Pi, in real Pi Browser, and
            //     was quoted "1022 SKR / 2555 SKR / BUY - 255 SKR" plus a Solana wallet chip. SKR is
            //     Solana Mobile's token - never minted, never held, and unspendable by a Pi player.
            //     This pins BOTH halves: no SKR string is reachable under the Pi skin, AND the SKR
            //     skin's own paths and copy are still present (a pass earned by deleting them would
            //     fail). It also pins that no static 'pi' price was authored into packs.json and that
            //     hearth-spark's storeVisible was not flipped to paper over an empty Pi shelf. ---
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "store-pi-skin suite", () => { if (!DeNelle.Editor.Regression.StorePiSkinCurrencyRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[store-pi-skin] " + r); });
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "structure-orientation suite", () => { if (!DeNelle.Editor.StructureOrientationOracle.Run(out var r)) failures.Add(r); else log.AppendLine("[structure-orientation] " + r); });
            // Registered 2026-08-22 in the same breath as the marker failure that caught it: this
            // oracle exposed Run(out string) and was referenced by NOTHING, which the marker suite
            // words exactly right -- "an unregistered oracle is a file that never runs." Second one
            // found today; StructureOrientationOracle above sat unregistered too, and on its FIRST
            // real run it immediately caught the tilt-90 ballista rows. That is the cost of the
            // defect class: the check existed the whole time and proved nothing.
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "night-market-ui suite", () => { if (!DeNelle.Editor.Regression.NightMarketUiRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[night-market-ui] " + r); });
            // --- WO-1162 §1 FIX 3: the source oracle above proves the NUMBERS are sane; this one
            //     builds a canvas at four surfaces plus a notched safe area, calls the SAME
            //     NightMarketComposition.Compose / StorePackCard.Build the player gets, forces the
            //     layout, and MEASURES the resolved RectTransforms for sibling overlap, safe-area
            //     intrusion, touch floors and text truncation. A constant can be legal and still
            //     resolve on top of its neighbour - the contents-block-over-price-lane defect was
            //     three legal literals summing to a card that overdrew itself.
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "night-market-runtime-layout suite", () => { if (!DeNelle.Editor.Regression.NightMarketRuntimeLayoutRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[night-market-runtime-layout] " + r); });
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "cost-format-source suite", () => { if (!DeNelle.Editor.Regression.CostFormatSourceRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[cost-format-source] " + r); });
            // --- WO-1511: reflection into DeNelle.Core from an assembly whose OWN .asmdef already
            //     references Core. Seven such sites existed; each carried a "type missing" null path
            //     that could never fire. The oracle reads the .asmdef rather than an allowlist, so it
            //     cannot go stale, and it never touches the SANCTIONED HUD->Village seam (§5). ---
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "core-reflection-source suite", () => { if (!DeNelle.Editor.Regression.CoreReflectionSourceRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[core-reflection-source] " + r); });
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "cathedral-mage-hp suite", () => { if (!DeNelle.Editor.Regression.CathedralMageHpRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[cathedral-mage-hp] " + r); });
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "echo-harvest-assignment suite", () => { if (!DeNelle.Editor.Regression.EchoHarvestAssignmentRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[echo-harvest-assignment] " + r); });
            // --- WO-1149 (owner, on device 2026-08-22: "we need to stop game during transactions got
            //     killed while making purchase test"): a transaction freezes the world through the
            //     single WorldHold owner, and EVERY exit unfreezes it. The suite measures the clock
            //     after driving real paths rather than grepping for a Resume() call, because the only
            //     failure that matters is the branch that returns without releasing. ---
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "world-hold suite", () => { if (!DeNelle.Editor.Regression.TransactionWorldHoldRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[world-hold] " + r); });
            // --- WO-1060 (owner, 2026-08-22 "yes do this"): the clamp/overlap oracle's OWN oracle.
            //     UICaptureLaunch measures the real panels, but it can only report what it found --
            //     it can never show that it is CAPABLE of finding anything, and a rule with an
            //     inverted comparison reports a clean run that looks exactly like a healthy one.
            //     This suite hands DeNelle.Core.UI.LayoutOracle canvases whose defects are authored
            //     on purpose (a 21.6px band; two controls stacked, as siblings AND across parents)
            //     and fails if the oracle stays quiet -- then proves it silent on the same controls
            //     laid apart, at two landscape aspects. PROD-008's rule: an oracle never seen red is
            //     not evidence, so until this line is green a clean UI_TOUCH_OK proves nothing. ---
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "ui-touch-oracle suite", () => { if (!DeNelle.Editor.Regression.UiTouchClampRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[ui-touch-oracle] " + r); });
            // WO-978 5F: registration is COMMITTER-FENCED - the seat writes the suite, the lead
            // registers it, so two seats never edit this file. An unregistered oracle is a file that
            // never runs, which this registry has now been bitten by twice.
            // NOTE THE NAMESPACE: DeNelle.Editor, NOT DeNelle.Editor.Regression. This folder holds
            // both conventions (StructureOrientationOracle is also DeNelle.Editor), so the neighbour
            // line above is NOT a safe template -- copying it cost a full regression run. Read the
            // suite file's own namespace before registering it.
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "economy-credit-reporting suite", () => { if (!DeNelle.Editor.EconomyCreditReportingRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[economy-credit-reporting] " + r); });

            // --- CAPTURE PROVENANCE (WO-1080, 2026-08-25): four layout tickets were minted from
            // ONE aged capture log and described a tree that had moved on; a capture log's mtime
            // is NOT evidence of the tree it measured (that log is NEWER than the commit it does
            // not contain). This suite proves the fix is ALIVE, not merely present: the resolver
            // really answers on this machine, the UI_CAPTURE_HEAD wire shape round-trips, the
            // parser REFUSES an abbreviated/hand-typed sha, and RunCaptureHeadless still stamps
            // itself. Namespace is DeNelle.Editor.Regression (read the suite file, not this
            // neighbour). ---
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "capture-provenance suite", () => { if (!DeNelle.Editor.Regression.CaptureProvenanceRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[capture-provenance] " + r); });
            // WO-1214 — drops bank to INVENTORY and never auto-equip; the equip seam refuses
            // class/level-ineligible gear instead of disarming the hero. Registered here by the
            // LEAD: the implementing agent was lane-fenced out of this file and said so, and an
            // oracle that is written but never registered is an oracle that never runs — exactly
            // what RegressionMarkerRegression exists to catch.
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "drops-to-inventory suite", () => { if (!DeNelle.Editor.Regression.DropsGoToInventoryRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[drops-to-inventory] " + r); });
            // WO-1225 — the reward acknowledgement flies to the gold chip and counts up to the
            // MEASURED post-grant balance. Registered here by the LEAD, per the note above: this
            // file is the one every parallel oracle lane would otherwise collide in.
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "reward-fly suite", () => { if (!DeNelle.Editor.Regression.WO1225RewardFlyRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[reward-fly] " + r); });
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "kill-reward-raid-suppression suite", () => { if (!DeNelle.Editor.Regression.KillRewardRaidSuppressionRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[kill-reward-raid-suppression] " + r); });
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "wo1232 enemy-level source suite", () => { if (!DeNelle.Editor.WO1232EnemyLevelSourceRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[wo1232-enemy-level] " + r); });
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "vfx-ambient-budget suite", () => { if (!DeNelle.Editor.Regression.VfxAmbientLoopBudgetRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[vfx-ambient-budget] " + r); });
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "vfx-perf-gate suite", () => { if (!DeNelle.Editor.Regression.VfxPerformanceGateRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[vfx-perf-gate] " + r); });
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "cost-row-fit suite", () => { if (!DeNelle.Editor.Regression.CostRowFitRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[cost-row-fit] " + r); });
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "first-raid-soft-gate suite", () => { if (!DeNelle.Editor.Regression.FirstRaidSoftGateRegression.Run(out var firstRaidReason)) failures.Add(firstRaidReason); else log.AppendLine("[first-raid-soft-gate] " + firstRaidReason); });
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "maintenance-toggles suite", () => { if (!DeNelle.Editor.Regression.MaintenanceTogglesRegression.Run(out var rMaint)) failures.Add(rMaint); else log.AppendLine("[maintenance-toggles] " + rMaint); });
            // PROD-022 - THE INVARIANT EVERY OFFLINE PLAYER DEPENDS ON: with no database
            // row and no reachable backend, all 8 remote knobs resolve to their SHIPPING
            // DEFAULTS, i.e. today's behaviour byte for byte. A break here is INVISIBLE
            // (nothing crashes; the build just stops behaving the way it says it does).
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "tunable-defaults suite", () => { if (!DeNelle.Editor.Regression.RemoteTunablesDefaultsRegression.Run(out var rTun)) failures.Add(rTun); else log.AppendLine("[tunable-defaults] " + rTun); });
            // WO-1343 - THE OWNER'S VFX TAGS, AND THE NIGHT STORE'S AURA CHOICE. Two things
            // that fail SILENTLY and have both already happened once: the VFX Caster can
            // overwrite an existing tag's prefabPath with no warning (it did, to her
            // tree-foot pick, within an hour of her making it), and "no row = today's
            // behaviour" is asserted only in prose everywhere else. This suite reads her
            // Assets/Editor/VfxManualPicks.json at run time and goes red on a re-point, and
            // it drives the selector with an empty, a corrupt and a readOk=false table.
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "night-store-aura suite", () => { if (!DeNelle.Editor.Regression.NightStoreAuraSelectionRegression.Run(out var rNsa)) failures.Add(rNsa); else log.AppendLine("[night-store-aura] " + rNsa); });
            // WO-1330 - THE PROOF THAT AN OVER-TIME EFFECT ACTUALLY TICKS. Not a data
            // lint: it drives DeNelle.Core.Combat.OverTimeEngine with a fake clock and
            // COUNTS the pulses (applied -> N ticks -> expires), on both signs. An
            // over-time effect built the obvious way - a coroutine - is one no gate can
            // ever observe, because EditMode never runs Update; that is why the engine
            // takes its clock as a parameter, and this suite is the reason it matters.
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "over-time suite", () => { if (!DeNelle.Editor.Regression.OverTimeEffectRegression.Run(out var rOt)) failures.Add(rOt); else log.AppendLine("[over-time] " + rOt); });
            // WO-1331 - THE INVARIANT WITH THE WIDEST BLAST RADIUS IN THE GAME: with no
            // database row and no reachable backend, EVERY canonical catalog resolves the
            // copy compiled into the player, byte for byte. CanonicalJson is how every
            // catalog in the game loads, so a break here does not crash - it silently
            // serves the wrong data, or half a catalog, to a build that is live on a store.
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "catalog-seam suite", () => { if (!DeNelle.Editor.Regression.RemoteCatalogSeamRegression.Run(out var rCat)) failures.Add(rCat); else log.AppendLine("[catalog-seam] " + rCat); });
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "troop-strike-vfx suite", () => { if (!DeNelle.Editor.Regression.TroopStrikeVfxRegression.Run(out var troopVfxReason)) failures.Add(troopVfxReason); else log.AppendLine("[troop-strike-vfx] " + troopVfxReason); });
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "starter-armour suite", () => { if (!DeNelle.Editor.Regression.StarterArmourOwnershipRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[starter-armour] " + r); });
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "armour-catalog-job suite", () => { if (!DeNelle.Editor.Regression.ArmourCatalogJobRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[armour-catalog-job] " + r); });
            // PERF D3 - Log and Warning must not walk a managed stack (ProjectSettings.asset:59
            // m_StackTraceTypes), while Error/Assert/Exception keep theirs. The editor rewrites that
            // file wholesale, so a restored ScriptOnly is SILENT: nothing crashes, no marker reddens,
            // and the build simply pays for a stack walk on every one of ~35 logs/s again. NOT a
            // FlowTrace strip (sec.12) - every line still prints, only the appended stack is dropped.
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "stacktrace-logtype suite", () => { if (!DeNelle.Editor.Regression.StackTraceLogTypeRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[stacktrace-logtype] " + r); });
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "early-ladder suite", () => { if (!DeNelle.Editor.WO1217EarlyEconomyLadderRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[early-ladder] " + r); });
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "palette-storage-tail suite", () => { if (!DeNelle.Editor.Regression.PaletteStorageTailRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[palette-storage-tail] " + r); });
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "json-only-source suite", () => { if (!DeNelle.Editor.Regression.JsonMirrorLiteralRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[json-only-source] " + r); });
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "siege-untouchable suite", () => { if (!DeNelle.Editor.Regression.SiegeUntouchableRegression.Run(out var siegeUntouchableReason)) failures.Add(siegeUntouchableReason); else log.AppendLine("[siege-untouchable] " + siegeUntouchableReason); });
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "resource-authority suite", () => { if (!DeNelle.Editor.Regression.ResourceAuthorityRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[resource-authority] " + r); });
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "echo-passive-mend suite", () => { if (!DeNelle.Editor.Regression.EchoPassiveMendCommsRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[echo-passive-mend] " + r); });
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "tutorial-coach suite", () => { if (!DeNelle.Editor.Regression.TutorialCoachEscalationRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[tutorial-coach] " + r); });
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "mana-scroll suite", () => { if (!DeNelle.Editor.Regression.ManaScrollFtueRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[mana-scroll] " + r); });
            // WO-1073 - the Founders Monument stand-in beside the Heart and the global
            // Benefactors of the Realm wall it is the ONE door onto. Pins the stand-in address
            // against api/_lib/benefactors.js, the per-patron (never global) monument state, the
            // near-the-Heart siting ruling, and the single-door ruling.
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "founders-wall suite", () => { if (!DeNelle.Editor.Regression.FoundersMonumentWallRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[founders-wall] " + r); });
            // WO-1251: no MeshRenderer/SkinnedMeshRenderer on a catalogued / Structure_Art
            // structure may carry a NULL material slot (F8 seq 3618 CrystalMine engine-default).
            // Stands down via Skip if the set cannot be enumerated -- never quiet green.
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "structure-null-slot suite", () => { if (!DeNelle.Editor.Regression.StructureNullMaterialSlotRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[structure-null-slot] " + r); });
            // WO-1398: the store has ONE player-facing name (canon-strings storeWordmark) and every
            // face that opens PanelId.RealmStore renders it; no "Night Market" / "Realm Store"
            // literal survives in module code.
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "store-name-single-source suite", () => { if (!DeNelle.Editor.Regression.StoreNameSingleSourceRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[store-name-single-source] " + r); });
            // WO-1395: PanelId.RealmStore has exactly ONE registrar per shipped artifact (the two
            // registrar files sit in asmdefs constrained GOOGLE_PLAY vs !GOOGLE_PLAY), every registrar
            // also registers the door-context opener so a plain and a door-tagged open land on the
            // same screen, and each names itself in a [Flow:Store] registration line.
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "realm-store-single-registrar suite", () => { if (!DeNelle.Editor.Regression.RealmStoreSingleRegistrarRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[realm-store-single-registrar] " + r); });
            // WO-1399: the gear dock's "Settings" row opens the REAL Settings (SettingsController,
            // via Core SettingsGate - PauseGate's twin), never HelpMenu; Help is a row INSIDE
            // Settings through the append-only PanelId.Help; the dock grid stays 2x3 / six cells.
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "dock-settings-route suite", () => { if (!DeNelle.Editor.Regression.DockSettingsRouteRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[dock-settings-route] " + r); });
            // WO-1400: a panel opened FROM a deck returns to that deck on close (arbiter-level
            // return door in PanelManager, one mechanism, honours the WO-1393 close-frame grace);
            // a HUD-opened panel's close does not return anywhere.
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "deck-return-door suite", () => { if (!DeNelle.Editor.Regression.DeckReturnDoorRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[deck-return-door] " + r); });
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "textfit-guard-arm suite", () => { if (!DeNelle.Editor.Regression.TextFitGuardArmRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[textfit-guard-arm] " + r); });
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "fitguard-relax-allowlist suite", () => { if (!DeNelle.Editor.Regression.FitGuardRelaxAllowlistRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[fitguard-relax-allowlist] " + r); });
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "jewel-polish suite", () => { if (!DeNelle.Editor.Regression.JewelPolishRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[jewel-polish] " + r); });
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "startnew-confirm-gate suite", () => { if (!DeNelle.Editor.Regression.StartNewConfirmGateRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[startnew-confirm-gate] " + r); });
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "save-wipe-backup suite", () => { if (!DeNelle.Editor.Regression.SaveWipeBackupRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[save-wipe-backup] " + r); });
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "staking-compliance suite", () => { if (!DeNelle.Editor.Regression.StakingComplianceRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[staking-compliance] " + r); });
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "vfx-pick-override suite", () => { if (!DeNelle.Editor.Regression.VfxPickOverrideRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[vfx-pick-override] " + r); });
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "heartbound-events suite", () => { if (!DeNelle.Editor.Regression.HeartboundEventRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[heartbound-events] " + r); });
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "heartbound-benefits suite", () => { if (!DeNelle.Editor.Regression.HeartboundBenefitsRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[heartbound-benefits] " + r); });
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "arena-inscene-suspend suite", () => { if (!DeNelle.Editor.Regression.ArenaInSceneSuspensionRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[arena-inscene-suspend] " + r); });
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "knight-combat-dock-icons suite", () => { if (!DeNelle.Editor.Regression.KnightCombatDockIconRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[knight-combat-dock-icons] " + r); });
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "hero-webgl-load suite", () => { if (!DeNelle.Editor.Regression.HeroAssetLoaderWebGlRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[hero-webgl-load] " + r); });
            // WO-1397: the Cosmetic Shop is reachable - a Hero-deck "Wardrobe" card routes to the
            // already-registered PanelId.CosmeticShop; the deck grid derives its rows from the card
            // count (2x3 for five cards) so no card lands under the purpose line. WO-1523: that
            // fifth card is now CONDITIONAL - with no cosmetic unlocked the deck is four cards on
            // its original 2x2 grid, and cases G/H pin the hide/show + NEW rule.
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "cosmetic-shop-reach suite", () => { if (!DeNelle.Editor.Regression.CosmeticShopReachabilityRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[cosmetic-shop-reach] " + r); });
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "manage-row-benefit suite", () => { if (!DeNelle.Editor.Regression.ManageRowBenefitRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[manage-row-benefit] " + r); });
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "night-market-no-wallet suite", () => { if (!DeNelle.Editor.Regression.NightMarketNoWalletRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[night-market-no-wallet] " + r); });
            // WO-1451 (2026-09-06): registered by the WO-1451 lane at the committer's explicit
            // dispatch instruction - a deliberate EXCEPTION to the fencing note above, not
            // compliance with it. The fencing rule normally keeps the authoring lane out of this
            // file and leaves registration to the committer. Pins that the tower
            // preview's RenderTexture and its camera agree on sample count - HEAD rendered 1 sample
            // (allowMSAA=false) into a 2-sample RT and threw 260 [BREAK]s in 144 seconds under
            // TowerPreviewCamera:Begin. The invariant is RT == CAMERA, never RT == the URP asset.
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "preview-rt-samples suite", () => { if (!DeNelle.Editor.Regression.PreviewRenderTextureSamplesRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[preview-rt-samples] " + r); });

            // WO-1450 + WO-1459 sec.2 suspect 3 (2026-09-06): registered per this file's fencing
            // rule. Pins the SHAPE of two guards in Enemy.cs - the structure-acquire trace is a
            // change-gated Throttle (it was a Step: 38,018 device lines at ~320/sec, evicting the
            // 256 KiB Android ring in under two seconds) and the probe itself is cadence-gated
            // (a SphereCast + an all-layer OverlapSphere had been running per frame per enemy).
            // It is a SOURCE LINT and says so in every reason string - the line count and the
            // frame cost are proven by a device capture, never by this suite.
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "enemy-probe-cadence suite", () => { if (!DeNelle.Editor.Regression.EnemyProbeCadenceRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[enemy-probe-cadence] " + r); });

            // WO-1483 + WO-1459 (2026-09-06): registered per this file's fencing rule. Pins that
            // every named town frame-path tick carries a FlowTrace.Measure scope IN ITS OWN BODY,
            // that each uses the ACCUMULATING 4-arg overload (the 3-arg one logs per dispose -
            // ~400 lines/sec across the sites, which evicts the Android ring), and that
            // PerfReporter still emits the 1s "frame budget:" roll-up on its own timer. It is a
            // SOURCE LINT and says so in every reason string - the ms numbers come from a
            // headless + device capture, never from this suite.
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "frame-budget-measure suite", () => { if (!DeNelle.Editor.Regression.FrameBudgetMeasureRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[frame-budget-measure] " + r); });

            // WO-1602 (2026-09-07): registered per this file's fencing rule. The THIRD sibling on
            // that same ring problem, applied to the global atmosphere. Pins that every method
            // which ASSIGNS a RenderSettings fog/ambient/sky property signs its own body with a
            // [Flow:Atmos] trace, that the per-frame ones use Throttle/Once rather than a bare
            // Step, that AtmosphereProbe still samples a BOUNDED ladder out to T+300s and writes
            // nothing, and that MagentaGuard still emits the [Flow:Terrain] BIND verdict. The
            // ticket exists because the owner's first-minutes water/haze transient had NO writer
            // attribution at all - the device break-log for that session held 11421 lines and not
            // one fog line. It is a SOURCE LINT and says so in every reason string; whether the
            // town LOOKS right is the fleet run plus the owner's eyes, never this suite.
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "atmosphere-trace suite", () => { if (!DeNelle.Editor.Regression.AtmosphereTraceRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[atmosphere-trace] " + r); });

            // WO-1582 (2026-09-07): registered per this file's fencing rule. The sibling of the
            // suite above, on the OTHER half of the same ring problem. Where frame-budget-measure
            // pins that per-frame PERF scopes use the accumulating overload, this one pins that the
            // per-frame SHEATHE-POSE trace de-duplicates by RESULT rather than by a clock: a
            // FlowTrace.Throttle on ApplyHoldPose's path logged twelve identical lines a minute on
            // the owner's device, and the 256 KiB Android ring cannot hold that plus a boot window.
            // Cases 1-5 are a real fixture (they count lines through a swapped FlowTrace.Sink);
            // case 6 is a source lint and says so. Neither proves the device ring - that is a capture.
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "sheathe-trace-latch suite", () => { if (!DeNelle.Editor.Regression.SheatheTraceLatchRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[sheathe-trace-latch] " + r); });

            // WO-1447 + WO-1448 (2026-09-06): registered by the implementing lane at the
            // committer's explicit dispatch instruction. Pins what a cloud LOAD restores (the
            // WHOLE row through MigrateForImport + ApplyPersisted, not the retired seven-field
            // copy list that left a reinstalled player with currencies on a blank town) and
            // WHEN it is allowed to overwrite local state (strictly newer only - every scene
            // enter used to write a possibly-stale server row over freshly spent resources).
            // Third arm: a row bound to a different wallet is refused, fail closed.
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "cloud-load-restore suite", () => { if (!DeNelle.Editor.Regression.CloudLoadRestoreRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[cloud-load-restore] " + r); });

            // WO-1587 - the OTHER end of the same rail: a cloud SAVE must DECLARE its schemaVersion,
            // and a drain failure must NAME ITS OWN CAUSE instead of pointing readers at a
            // [Flow:Wallet] line that nothing prints (six drains failed behind a 400 on 2026-09-07
            // while the wallet session minted and renewed fine). ⚠ The 400 itself is HISTORY, not
            // the pin: api/game/save.js retired SCHEMA_VERSION_MISSING the same day and now accepts
            // an absent version (200, note SCHEMA_VERSION_ABSENT) - but an accepted omission leaves
            // the row's stored version frozen, which the LOAD path then trusts, so the client
            // declaring it is still the fix. The suite pins the client side and the cause mechanic.
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "sync-drain-reason suite", () => { if (!DeNelle.Editor.Regression.SyncDrainReasonRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[sync-drain-reason] " + r); });

            // =====================================================================
            //  WO-1496 (2026-09-06) - SEVEN SUITE FILES THAT EXISTED AND RAN NOWHERE
            // =====================================================================
            // Each of the seven below sat in Assets/Editor/Regression with no entry point
            // calling it. An unregistered suite is worse than no suite: the file reads as
            // coverage in every audit, and asserts nothing on every run. Four of them
            // (arena-combat, blank-start-census, combat-foundation, gear-addressable-group)
            // exposed only a void Run() and so were invisible even to RegressionMarkerRegression
            // RULE 2, whose scope is `public static bool Run(out string)`; they were given that
            // contract in this WO, with the process-exit and marker printing left behind in the
            // standalone entry point (an EditorApplication.Exit inside RunAll would kill the
            // batch before REGRESSION_OK is written).
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "repair-probe suite", () => { if (!DeNelle.Editor.RepairProbeRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[repair-probe] " + r); });
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "combat-foundation suite", () => { if (!DeNelle.Editor.CombatFoundationRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[combat-foundation] " + r); });
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "arena-combat suite", () => { if (!DeNelle.Editor.ArenaCombatOracle.Run(out var r)) failures.Add(r); else log.AppendLine("[arena-combat] " + r); });
            // ⚠ DUPLICATE ASSERTION, REPORTED NOT COLLAPSED (WO-1496): CheckGearAddressableGroup
            // in THIS file (the [gear-addressable-group] line above) is an inline copy of the same
            // regex, the same asset and the same rule as GearAddressableGroupRegression. Two
            // sources of truth for one invariant is the disease this repo keeps paying for, but
            // collapsing them is a decision about another suite's file, so the oracle is
            // registered under its OWN tag and the duplication is ticketed for the lead.
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "gear-addressable-group-oracle suite", () => { if (!DeNelle.Editor.GearAddressableGroupRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[gear-addressable-group-oracle] " + r); });
            // MOVED, NOT ADDED (WO-1496): this call sat at line ~170, ABOVE the START FENCE. It
            // ran on every batch, but uncounted - its [move-manifest] line landed in the
            // pre-fence baseline and it exposed no `.Run(out` call-site for the denominator to
            // pin, so a throw inside it would have been silent in exactly the way the fence
            // exists to prevent. Same suite, same body; it is now shaped and counted like one.
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "move-manifest suite", () => { if (!DeNelle.Editor.Regression.AssetMoveManifestRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[move-manifest] " + r); });
            // (!) DO NOT WEAKEN THIS SUITE. Its header held it standalone until the orc art landed.
            // WAS RED ON [every-model-has-art] until WO-1536 (2026-09-07): enemies.json:400 said
            // `"modelKey": "OgreMage"` and that mesh is not in the tree - it lived at
            // Assets/Resources/Enemies/OgreMage.fbx and was deleted in 0cec81a78 (2026-07-01,
            // size cuts). WO-1496 named the two honest resolutions - ticket the row, or
            // hoist the art-pending declaration into one shared source - and WO-1536 took a third
            // that removes the split entirely: the row was corrected AT THE DATA AUTHORITY to
            // Orc_Shaman (the body every ogre already wore), and the art-pending HashSet in
            // EnemyResolverRegression was DELETED, so no exemption exists that this suite cannot
            // see. An exemption added HERE would still be neither (WO-1496 sec.3).
            // (!) STILL RED as of Builds/reg-wave5c.log on its SECOND case, [binding-and-sentinel]:
            // 7 FBX under Assets/EnemyContent carry no '.tripo-extracted' sentinel. That is a
            // separate defect in a separate lane - WO-1536 does not touch it, and REGRESSION_OK
            // stays blocked until it is closed by whoever owns the Tripo import binding.
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "enemy-art-coverage suite", () => { if (!DeNelle.Editor.EnemyArtCoverageRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[enemy-art-coverage] " + r); });
            // WO-1540: the flag-hygiene pin. A dummy "suite" inside it sets a feature flag and
            // does NOT restore it; the oracle asserts the snapshot NAMES that key as drift and
            // puts the environment back, so the next suite reads the compiled default. It
            // restores in a finally, so it can never become the bleed it polices.
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "flag-snapshot suite", () => { if (!DeNelle.Editor.Regression.FeatureFlagSnapshotRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[flag-snapshot] " + r); });
            // WO-1589: loot that BANKS is announced once, through the kill path's own
            // bounded CombatText(Reward) stamp; loot that has only DROPPED says nothing.
            // Two-sided on purpose - a one-sided suite would go green on a toast fired at
            // the chest open, which claims loot still lying on the floor.
            if (!DeNelle.Editor.ChestLootToastRegression.Run(out var chestLootToastReason)) failures.Add(chestLootToastReason); else log.AppendLine("[chest-loot-toast] " + chestLootToastReason);

            // WO-1605 -- one global locale resolver, exact authority policy, a shrinking
            // literal-debt inventory, locale parity, and translator-safe Smart arguments.
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "localization-authority suite", () => { if (!DeNelle.Editor.Regression.LocalizationAuthorityRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[localization-authority] " + r); });
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "player-text-literal-leak suite", () => { if (!DeNelle.Editor.Regression.PlayerTextLiteralLeakRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[player-text-literal-leak] " + r); });
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "locale-parity suite", () => { if (!DeNelle.Editor.Regression.LocaleParityRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[locale-parity] " + r); });
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "smart-argument suite", () => { if (!DeNelle.Editor.Regression.SmartArgumentRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[smart-argument] " + r); });
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "settings-localization suite", () => { if (!DeNelle.Editor.Regression.SettingsLocalizationRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[settings-localization] " + r); });
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "glyph-coverage suite", () => { if (!DeNelle.Editor.Regression.GlyphCoverageRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[glyph-coverage] " + r); });
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "combat-flee-localization suite", () => { if (!DeNelle.Editor.Regression.CombatHudFleeLocalizationRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[combat-flee-localization] " + r); });
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "play-localization-variant-policy suite", () => { if (!DeNelle.Editor.Regression.GooglePlayLocalizationVariantPolicyRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[play-localization-variant-policy] " + r); });

            // LAST LINE ABOVE THE END FENCE, DELIBERATELY: this suite opens
            // Main_Castle_Overworld in Single mode, so any suite registered after it would
            // census a different world than the one it was written against.
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "blank-start-census suite", () => { if (!DeNelle.Editor.BlankStartCensusRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[blank-start-census] " + r); });

            // WO-1495 (2026-09-06): the exemption ratchet. Every allowlist/exemption/known-debt
            // collection under Assets/Editor/Regression must carry a WO pointer, an origin date
            // and an unexpired remove-by in the five lines above its declaration - because an
            // exemption with no owner and no expiry keeps a suite green forever on the exact
            // content it was written to cover. Four definitional blocks are excluded BY SHAPE and
            // named in the suite, not by an opt-out token anyone could paste over real debt.
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "allowlist-expiry suite", () => { if (!DeNelle.Editor.Regression.AllowlistExpiryRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[allowlist-expiry] " + r); });

            // WO-1584 — the vendor Store SELL shelf: every row labelled, every material row
            // carrying the art keys the ONE material seam needs, and SelectedId as the single
            // truth behind both the lit row and the detail column.
            if (!DeNelle.Editor.Regression.StoreSellRowIdentityRegression.Run(out var storeSellRowReason)) failures.Add(storeSellRowReason); else log.AppendLine("[store-sell-row] " + storeSellRowReason);

            // WO-1580 — RepairAvailabilityProbe's severity is SCENE-CLASS dependent. Both-surfaces-
            // absent stays FlowTrace.Fail (error level, and error is what the F8 harness records)
            // everywhere except HubScenes.IsRaid, where no repair surface is the authored state and
            // the same line reports at Step. Two-sided plus a scope pin on purpose: a one-sided
            // suite would go green on a probe that had simply been silenced everywhere.
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "repair-probe-severity suite", () => { if (!DeNelle.Editor.Regression.RepairProbeSeverityRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[repair-probe-severity] " + r); });

            // WO-1575 — the compile gate never compiled WebGL, so a `#if UNITY_WEBGL` block
            // carried a CS1501 arity error indefinitely while every desktop gate read green;
            // it only surfaced inside the Addressables WebGL content build. CompileGate now
            // runs a WebGL player-script pass (PlayerBuildInterface.CompilePlayerScripts, no
            // target switch) and emits COMPILE_GATE_WEBGL_OK / _FAIL / _SKIPPED. This suite is
            // a SOURCE LINT on the gate's shape — the compile itself is proven by that marker
            // on a fresh CompileGate log, not here.
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "webgl-compile-gate suite", () => { if (!DeNelle.Editor.Regression.WebGlCompileGateRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[webgl-compile-gate] " + r); });

            // WO-1598 — the CLIENT half of the reset epoch. api/game/save.js's sanity guard reads a
            // legitimate New Game as an implausible drop / a rollback and rejects those fields, so the
            // cloud row keeps the OLD town and hands it back on the next load (the owner's 2026-09-07
            // reset: `implausible_drop crystals 901 -> 36`, eleven times; 177 such rows in 14 days).
            // A save now DECLARES a monotonic resetEpoch. Six arms: the body carries it top-level as
            // an integer on EVERY write, ResetToNewGame raises it and never clears it, a backend row
            // from before that reset is refused EVEN THOUGH its timestamp is newer (a guard-rejected
            // save still bumps updated_at, which is why the WO-1448 recency gate alone cannot see it),
            // an equal or absent epoch still applies so nothing changes for a player who has never
            // reset, and an APPLIED row's newer epoch is adopted locally (the server keeps the epoch in
            // its own column and strips it from the state blob, so without the adoption a reinstall
            // would be refused SAVE_RESET_STALE on every save it made), and the server's 409
            // SAVE_RESET_STALE is its own NON-RETRYABLE category whose markers the drain DROPS rather
            // than looping on an answer that can never change. The server half is tested under test/
            // in the api lane (test/game.save.reset-epoch.test.js).
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "reset-epoch suite", () => { if (!DeNelle.Editor.Regression.ResetEpochRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[reset-epoch] " + r); });

            // WO-1096 (2026-09-09, lane SHOP): the shop preview loader branch is decided by the row's
            // own loadVia, never by a "blink_" id prefix; the armor flag governs armor only.
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "shop-preview-loader-branch suite", () => { if (!DeNelle.Editor.PartyShopPreviewLoaderBranchRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[shop-preview-loader-branch] " + r); });

            // WO-1094 (2026-09-09, lane LOCOMOTION): the playable bound is measured from the world
            // authority, never a literal; the teleport guard spans the warp frame and the next Update.
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "hero-playable-bounds suite", () => { if (!DeNelle.Editor.Regression.HeroPlayableBoundsRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[hero-playable-bounds] " + r); });

            // WO-1431 (2026-09-09, lane HERO-GRIP): the staff grip is DERIVED (0.75 up) on the live
            // melee attach path, not only measured; swords keep the lower-hilt rule.
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "staff-grip-seat suite", () => { if (!DeNelle.Editor.Regression.StaffGripSeatRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[staff-grip-seat] " + r); });

            // WO-1095 + WO-1594 (2026-09-09, lane RAID): the stranding watchdog measures ENGAGED time
            // like the clock, never scene age; honor stars degrade by a pure ComputeHonorStars.
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "raid-watchdog-honor suite", () => { if (!DeNelle.Editor.Regression.RaidWatchdogHonorRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[raid-watchdog-honor] " + r); });

            // WO-1092 (2026-09-09, lane CACHE): an abandoned Addressables cache transaction (lock-only
            // version dir) is repaired before the pull; chunks are planned by UNIQUE bundle; verifier unchanged.
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "offline-cache-repair suite", () => { if (!DeNelle.Editor.Regression.OfflineCacheRepairRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[offline-cache-repair] " + r); });

            // WO-1099 (2026-09-09, lane HARVEST-COPY): the over-cap harvest result exposes banked /
            // pending / over-cap truthfully and its footer leads with the SPEND recovery, never a reassurance.
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "harvest-overcap-copy suite", () => { if (!DeNelle.Editor.Regression.HarvestOverCapCopyRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[harvest-overcap-copy] " + r); });

            // WO-1412 (2026-09-09, lane STORE-RETURN): the store close returns through the opener
            // arbiter to the SAME Manage tab; the return door is set, held through the grace frame, consumed.
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "store-return-to-manage suite", () => { if (!DeNelle.Editor.Regression.StoreReturnToManageRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[store-return-to-manage] " + r); });

            // WO-1090 / WO-1091 / WO-1093 (2026-09-09, lane PINS): the three checkpoint-landed fixes
            // get their first oracles - modal deferral off the step clock, the grounded biome drop, the
            // pursuit ring that clears on deaggro.
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "ftue-modal-deferral suite", () => { if (!DeNelle.Editor.Regression.FtueModalDeferralRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[ftue-modal-deferral] " + r); });
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "biome-drop-ground-probe suite", () => { if (!DeNelle.Editor.Regression.BiomeDropGroundProbeRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[biome-drop-ground-probe] " + r); });
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "rep-chase-leash suite", () => { if (!DeNelle.Editor.Regression.RepChaseLeashRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[rep-chase-leash] " + r); });

            // WO-1097 item 4 (2026-09-09, lane LOADER): a Component-typed request against a GameObject
            // address is REFUSED and traced (null, never a throw); the doc example is the working shape.
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "enemy-asset-type-screen suite", () => { if (!DeNelle.Editor.Regression.EnemyAssetTypeScreenRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[enemy-asset-type-screen] " + r); });

            // WO-1615 (2026-09-09, lane PLACE): the PLACE chip traces before it invokes, the over-UI
            // suppression names the raycast owner, and only the UI latch commits a move.
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "place-latch-trace suite", () => { if (!DeNelle.Editor.BuildPlaceLatchTraceRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[place-latch-trace] " + r); });

            // WO-1377 (2026-09-09, lane META, owner ruling "move only what is safe"): the Jupiter swap
            // surface is compiled out under GOOGLE_PLAY; PaymentChannel / SkinAuthMode stay (name-bound,
            // one Arena code path). Source-level; the physical metadata scan stays a ship-chain step.
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "play-metadata-identifiers suite", () => { if (!DeNelle.Editor.Regression.PlayMetadataIdentifierRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[play-metadata-identifiers] " + r); });

            // WO-1461 (2026-09-09, lane SPOILS): quoted == banked + pending; spoils above cap are RETAINED
            // in the raid cache, never burned; the repeat-clear share is the ruled 60% off the rail.
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "spoils-bankable suite", () => { if (!DeNelle.Editor.Regression.SpoilsAreBankableRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[spoils-bankable] " + r); });

            // WO-1617 (2026-09-09, lane BALLISTA): ONE siege-machine decider shared by PlaceSpire,
            // PlaceTowerProp and the dresser's art resolver; a siege id in the spire slot never stands on edge.
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "raid-spire-siege suite", () => { if (!DeNelle.Editor.Regression.RaidSpireSiegeRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[raid-spire-siege] " + r); });

            // WO-1616 (2026-09-09, lane NPC-SHIELD): the troop off-hand shield seats through the hero's
            // shield authority (one owner), never a hard-coded triple; control case proves the old seat was wrong.
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "troop-shield-seat suite", () => { if (!DeNelle.Editor.Regression.TroopShieldSeatRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[troop-shield-seat] " + r); });

            // WO-1430 Group B (2026-09-09, lane FIELDS): unlockMethod gates achievement grants and
            // requiresHero gates daily-quest templates - the authored fields now have a production reader.
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "authored-field-gate suite", () => { if (!DeNelle.Editor.AuthoredFieldGateRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[authored-field-gate] " + r); });

            // WO-1373 (2026-09-09, lane RAID-3, owner ruling): one rough stone; only the top two raid
            // tiers drop it, at most one per UTC day; dungeons 5% off the rail, starter dungeons excluded.
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "raid-rough-stone suite", () => { if (!DeNelle.Editor.Regression.RaidRoughStoneDropRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[raid-rough-stone] " + r); });
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "seating-preview-shield suite", () => { if (!DeNelle.Editor.Regression.SeatingPreviewShieldRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[seating-preview-shield] " + r); });
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "screen-orientation suite", () => { if (!DeNelle.Editor.Regression.ScreenOrientationRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[screen-orientation] " + r); });

            // =====================================================================
            //  >>> REGISTERED ORACLE SUITES — END FENCE <<<  (new lines go ABOVE)
            // =====================================================================

            // --- FLAG HYGIENE, second half (WO-1540) -------------------------------
            // Restore every ff.* key to what it was before the fence and NAME anything
            // that drifted. On green this deliberately logs a line that does NOT start
            // with '[' so it is not miscounted as an extra oracle suite by
            // CountOracleTagLines; on red it lands in `failures` like any other suite
            // result, which is where a bleed belongs.
            if (!DeNelle.Editor.Regression.FeatureFlagSnapshot.RestoreAndDiff(flagSnapshotBefore, out var flagDriftReason))
                failures.Add("[flag-hygiene] " + flagDriftReason);
            else
                log.AppendLine(flagDriftReason);
            // --- SKIPPED IS A THIRD STATE, NOT A PASS (2026-08-16 coverage audit) ---
            // A suite that stands down (GameStateService will not install headless, a
            // data file is absent) reports TRUE so a harness limitation is not read as a
            // product defect -- but the bool is the caller's only channel, so until now
            // that stand-down landed in the GREEN column. In an environment where the
            // state seam does not install, six economy oracles asserted NOTHING and this
            // marker still read full green. Those suites now stamp RegressionOutcome.
            // SkipToken into their reason, and the tally subtracts them here. The
            // denominator (suitesTotal) is unchanged on purpose: the suite was registered
            // and it DID run, so it must stay in the denominator -- what changed is that
            // it no longer counts as evidence in the numerator.
            int suiteTagLines = CountOracleTagLines(log) - suiteTagLinesBefore;
            var skippedTags   = CollectSkippedSuiteTags(log);
            int suitesSkipped = skippedTags.Count - suiteSkipLinesBefore;
            if (suitesSkipped < 0) suitesSkipped = 0;
            int suitesGreen = suiteTagLines - suitesSkipped;
            int suitesRed   = failures.Count - suiteFailuresBefore;
            int suitesTotal = suiteTagLines + suitesRed;

            // --- G1: the denominator must be PINNED, not self-reported -------------
            // suitesTotal is derived from what the run PRODUCED (tag lines + failures).
            // A suite registered inside Guard.Try that THROWS produces neither, so it
            // silently leaves this total and the marker still reads green at a smaller
            // number. Joining the runtime count against the count of registration
            // call-sites in SOURCE makes a vanished suite loud. Both sides are measured;
            // neither is a literal (a hardcoded expectation would be audit finding G8).
            int expectedSuites;
            string expectDetail;
            if (!DeNelle.Editor.Regression.RegressionMarkerRegression.TryGetExpectedSuiteCount(out expectedSuites, out expectDetail))
            {
                failures.Add("[suite-count] could not derive the expected registered-suite count from source (" +
                             expectDetail + "). The denominator is therefore UNPINNED: a suite that throws " +
                             "inside Guard.Try would vanish from the total and the marker would still read green.");
            }
            // ⛔ SHORTFALL, NOT INEQUALITY (2026-08-22). The hazard this guards is a suite that
            // THROWS inside Guard.Try: it emits neither a [tag] line nor a failure, so it silently
            // leaves the total. That is EXCLUSIVELY a shortfall (actual < expected) -- arithmetic,
            // not judgement. The old `!=` also fired on a SURPLUS and printed the vanish message at
            // it, which is unreachable by the failure mode described: on 2026-08-22 it reported
            // "SUITE VANISHED" at 264 accounted vs 261 registered, with 264 green and 0 red. A
            // by-name reconciliation that day confirmed EVERY registered suite reported.
            // The two sides measure different quantities on purpose -- expected counts registration
            // call-sites, actual counts emitted tag lines, and one suite may legitimately emit more
            // than one line -- so equality was never the right assertion. Shortfall keeps 100% of
            // the detection. Residual, ticketed: a vanish MASKED by a co-occurring surplus still
            // slips, which the old `!=` did not catch either (it just fired the wrong message).
            else if (suitesTotal < expectedSuites)
            {
                failures.Add("[suite-count] SUITE VANISHED FROM THE DENOMINATOR: source registers " +
                             expectedSuites + " oracle suite(s) between the fences, but this run only " +
                             "accounted for " + suitesTotal + " (" + suitesGreen + " green + " + suitesRed +
                             " red). The difference threw inside its Guard.Try, which swallows the exception " +
                             "and returns false, so it emitted no [tag] line and no failure. Search the log " +
                             "for 'FAILED at' to find it. " + expectDetail);
                // Deliberately NOT adjusting suitesRed/suitesTotal here: those are the
                // measurement of what this run produced, and doctoring them to match the
                // expectation would erase the very discrepancy being reported. The marker
                // keeps reporting the honest (smaller) number; this failure explains it.
            }
            else
            {
                log.AppendLine("[suite-count] denominator pinned: source registers " + expectedSuites +
                               " suite(s) and the run accounted for all " + suitesTotal + ".");
            }

            // --- Store/Inventory icon coverage (key data: real art vs glyph fallback) ---
            CheckItemIconCoverage(weapons, armors, failures, log);

            // --- TUTORIAL V2 REGISTRY (WO-T1, spec §2.5.4) ---------------------------
            // tutorial-steps.json invariants: steps parse; every dialogue id exists in
            // dialogues.json; every highlight id is a known registry key; every completion
            // signal is a known bus id/prefix; mandatory order strictly increasing; every
            // speaker used by tut_* dialogues has a speaker record with a portrait (the
            // yellow-disc class of bug becomes a build failure).
            CheckTutorialSteps(failures, log);

            // --- verdict -----------------------------------------------------------
            // THE marker is SELF-DESCRIBING: it carries the registered-suite count on the
            // SAME line, so a log can never be mistaken for a different (smaller) suite's
            // pass. Consumers grep the shaped form  REGRESSION_OK <n>/<n> suites  — see
            // tools/regression/checkin_gate.ps1 and RegressionMarkerRegression.
            log.AppendLine("=== verdict ===");
            log.AppendLine($"registered oracle suites: {suitesTotal} ({suitesGreen} green, {suitesRed} red, {suitesSkipped} skipped)");
            if (suitesSkipped > 0)
            {
                // Named, not just counted: "7 skipped" is only actionable if the log says WHICH.
                log.AppendLine("SKIPPED SUITES (asserted nothing this run): " +
                               string.Join(", ", skippedTags.GetRange(suiteSkipLinesBefore, suitesSkipped).ToArray()));
            }
            if (failures.Count == 0)
            {
                log.AppendLine($"REGRESSION_OK {suitesGreen}/{suitesTotal} suites -- {suitesGreen} green, {suitesRed} red, {suitesSkipped} skipped");
                Debug.Log(log.ToString());
            }
            else
            {
                log.AppendLine($"REGRESSION_FAIL: {failures.Count} failure(s) ({suitesGreen}/{suitesTotal} registered suites green, {suitesSkipped} skipped):");
                foreach (var f in failures) log.AppendLine("  - " + f);
                // LogError so it also lands in break-log.jsonl and fails loudly in the log scan.
                Debug.LogError(log.ToString());
            }
        }

        // =====================================================================
        //  Registered-suite counter (feeds the self-describing REGRESSION_OK marker)
        // =====================================================================
        // Every registered oracle suite reports green by appending a line that STARTS
        // with its "[tag] " prefix, so counting lines that begin with '[' between the
        // START and END fences yields the exact number of suites that reported green —
        // without touching (and churning) the ~90 registration lines themselves.
        private static int CountOracleTagLines(StringBuilder log)
        {
            if (log == null) return 0;
            string s = log.ToString();
            int n = 0;
            if (s.Length > 0 && s[0] == '[') n++;
            for (int i = 0; i + 1 < s.Length; i++)
                if (s[i] == '\n' && s[i + 1] == '[') n++;
            return n;
        }

        // =====================================================================
        //  Skipped-suite collector (the THIRD state)
        // =====================================================================
        // A stand-down rides in the reason string as RegressionOutcome.SkipToken, so a
        // suite reporting it still appends its "[tag] " line (it did not fail) but is
        // subtracted from the green numerator and NAMED in the verdict. Returns the tags
        // rather than a bare count so the log can say WHICH suites asserted nothing --
        // "7 skipped" with no names is the same unactionable number the old "125/125" was.
        private static List<string> CollectSkippedSuiteTags(StringBuilder log)
        {
            var tags = new List<string>();
            if (log == null) return tags;
            foreach (var line in log.ToString().Replace("\r\n", "\n").Split('\n'))
            {
                if (line.Length == 0 || line[0] != '[') continue;
                if (line.IndexOf(DeNelle.Editor.Regression.RegressionOutcome.SkipToken,
                                 System.StringComparison.Ordinal) < 0) continue;
                int close = line.IndexOf(']');
                tags.Add(close > 1 ? line.Substring(1, close - 1) : "unnamed-suite");
            }
            return tags;
        }

        // =====================================================================
        //  TUTORIAL V2 REGISTRY — tutorial-steps.json invariants (WO-T1)
        // =====================================================================
        private static void CheckTutorialSteps(List<string> failures, StringBuilder log)
        {
            log.AppendLine("=== tutorial-steps.json (Tutorial V2 registry) ===");
            DeNelle.Core.Tutorial.TutorialStepCatalog.Reload();
            DeNelle.Core.Dialogue.DialogueCatalog.Reload();

            var all = DeNelle.Core.Tutorial.TutorialStepCatalog.All;
            var mandatory = DeNelle.Core.Tutorial.TutorialStepCatalog.MandatorySteps();
            var contextual = DeNelle.Core.Tutorial.TutorialStepCatalog.ContextualSteps();
            log.AppendLine($"tutorial-steps.json -> {all.Count} steps ({mandatory.Count} mandatory, {contextual.Count} contextual)");

            if (mandatory.Count == 0)
            { failures.Add("tutorial-steps.json deserialized to 0 mandatory steps (mapping break or empty)"); return; }
            // WO-1012 (owner-ruled arc, 2026-08-09/10): the founding flow is the 8-beat
            // pet-Echo-guided arc — ARRIVE, WALK, BUILD ONE, ACK, ONE CANNON, TIMERS,
            // ENEMIES AT THE GATE, WIN+HANDOFF. Supersedes the 2026-07-24 end-after-defend
            // 7-step pin (that ruling's substance — no venture-out back half, no whole-village
            // build — survives inside the 8 beats; the count moved because ACK/WIN became
            // structural beats, not because scope grew).
            if (mandatory.Count != 8)
                failures.Add($"tutorial mandatory chain has {mandatory.Count} steps — the WO-1012 owner-ruled arc is exactly 8 beats (2026-08-10)");

            // Known highlight-registry ids + completion-signal vocabulary.
            var knownHighlights = new HashSet<string>(DeNelle.Core.UI.TutorialHighlightRegistry.KnownIds);
            bool KnownSignal(string s) =>
                !string.IsNullOrEmpty(s) && (
                    s == DeNelle.Core.Tutorial.TutorialSignals.BuildModeEntered ||
                    s == DeNelle.Core.Tutorial.TutorialSignals.TowerPlaced ||
                    s == DeNelle.Core.Tutorial.TutorialSignals.WaveCleared ||
                    s == DeNelle.Core.Tutorial.TutorialSignals.ArenaWin ||
                    s == DeNelle.Core.Tutorial.TutorialSignals.ArenaLoss ||
                    s == DeNelle.Core.Tutorial.TutorialSignals.CanAffordUpgrade ||
                    s == DeNelle.Core.Tutorial.TutorialSignals.EchoBornSecond ||
                    s == DeNelle.Core.Tutorial.TutorialSignals.FirstGearAdded ||
                    s == DeNelle.Core.Tutorial.TutorialSignals.FirstSkillPoint ||
                    // WO-1340: a talent actually LEARNED - the completion of the spend teach.
                    // The companion to FirstSkillPoint above (earned) is this one (spent).
                    s == DeNelle.Core.Tutorial.TutorialSignals.FirstTalentLearned ||
                    // WO-1012 P3: the scripted teaching band's repelled signal (ENEMIES beat).
                    s == DeNelle.Core.Tutorial.TutorialSignals.TutorialBandRepelled ||
                    // WO-1389: the post-first-raid HOW beat completes on a troop job actually landing
                    // (BarracksService.UpgradeTroop / EnqueueTraining success points), and its TRAINING
                    // NOW coach-mark completes on the OPEN QUEUE drawer opening (ManageScreenPanel).
                    s == DeNelle.Core.Tutorial.TutorialSignals.TroopJobQueued ||
                    s == DeNelle.Core.Tutorial.TutorialSignals.ManageQueueOpened ||
                    s.StartsWith(DeNelle.Core.Tutorial.TutorialSignals.DialogueEndedPrefix) ||
                    s.StartsWith(DeNelle.Core.Tutorial.TutorialSignals.HeroReachedPrefix) ||
                    s.StartsWith(DeNelle.Core.Tutorial.TutorialSignals.PanelOpenedPrefix) ||
                    // WO-702: per-item placement signals (build.structure_placed:<entryId>)
                    s.StartsWith(DeNelle.Core.Tutorial.TutorialSignals.StructurePlacedPrefix));

            int lastOrder = int.MinValue;
            var tutSpeakers = new HashSet<string>();
            foreach (var s in mandatory)
            {
                if (s.Order <= lastOrder)
                    failures.Add($"tutorial step '{s.Id}' order {s.Order} is not strictly increasing");
                lastOrder = s.Order;
            }

            foreach (var s in all)
            {
                if (s == null || string.IsNullOrEmpty(s.Id))
                { failures.Add("tutorial step with null/empty id"); continue; }

                // Completion signal present + known vocabulary.
                string sig = s.Completion != null ? s.Completion.Signal : null;
                if (!KnownSignal(sig))
                    failures.Add($"tutorial step '{s.Id}' completion signal '{sig ?? "<null>"}' is not a known bus id");

                // Dialogue ids resolve in dialogues.json.
                foreach (var did in new[] { s.Dialogue?.Intro, s.Dialogue?.Outro })
                {
                    if (string.IsNullOrEmpty(did)) continue;
                    var def = DeNelle.Core.Dialogue.DialogueCatalog.Find(did);
                    if (def == null)
                    { failures.Add($"tutorial step '{s.Id}' dialogue '{did}' does not exist in dialogues.json"); continue; }
                    foreach (var node in def.Nodes)
                        if (node?.Lines != null)
                            foreach (var line in node.Lines)
                                if (line != null && !string.IsNullOrEmpty(line.Speaker))
                                    tutSpeakers.Add(line.Speaker);
                }

                // Highlight ids come from the registry's build-time contract.
                if (s.Highlight != null)
                    foreach (var h in s.Highlight)
                        if (!string.IsNullOrEmpty(h) && !knownHighlights.Contains(h))
                            failures.Add($"tutorial step '{s.Id}' highlight '{h}' is not a known TutorialHighlightRegistry id");

                // WO-780: first taught tower must be prepaid so the player can place it.
                if (s.Id == "founding_defense" && (s.Grant == null || !s.Grant.PrepaidTower))
                    failures.Add("tutorial step 'founding_defense' must have grant.prepaidTower:true (WO-780 — taught tower must be affordable on ENTER)");

                // Contextual rules: oneShot + never pausePressure (a hint never gates).
                if (s.IsContextual)
                {
                    if (!s.OneShot) failures.Add($"contextual step '{s.Id}' must be oneShot:true");
                    if (s.PausePressure) failures.Add($"contextual step '{s.Id}' must never pausePressure");
                    if (s.Trigger == null || s.Trigger.Type != "signal" || string.IsNullOrEmpty(s.Trigger.Signal))
                        failures.Add($"contextual step '{s.Id}' must trigger on a signal");
                }
            }

            // Every tut_* speaker resolves to a card record WITH a portrait (or an
            // explicit-silhouette empty) — the yellow-disc bug class becomes a failure.
            foreach (var sp in tutSpeakers)
            {
                var rec = DeNelle.Core.Dialogue.DialogueCatalog.FindSpeaker(sp);
                if (rec == null)
                    failures.Add($"tutorial dialogue speaker '{sp}' has no speaker record in dialogues.json (blank NPC card)");
                else if (string.IsNullOrEmpty(rec.Portrait))
                    log.AppendLine($"  note: speaker '{sp}' has an empty portrait — renders the styled silhouette (deliberate).");
                else log.AppendLine($"  speaker '{sp}' -> portrait '{rec.Portrait}' ok");
            }
        }

        // =====================================================================
        //  ENEMY STRUCTURE-AWARE SWEEP — real Enemy.ProbeForStructure, 3 cases
        // =====================================================================
        // A minimal live IDamageableStructure stand-in (a "tower/wall") for the sweep to
        // acquire. Only the two interface members + a collider are needed.
        private sealed class OracleStructure : MonoBehaviour, DeNelle.Core.Combat.IDamageableStructure
        {
            public bool Alive = true;
            public bool IsAlive => Alive;
            public void ApplyContactDamage(float amount) { }

            // WO-1439 — the stand-in now has to declare a side. Settable, because the new
            // friendly-fire case (below) needs a HOSTILE stand-in to prove a Hostile enemy
            // refuses it; the three pre-existing sweep cases keep the Friendly default and
            // therefore keep their old outcomes exactly.
            public DeNelle.Core.Combat.CombatFaction Side = DeNelle.Core.Combat.CombatFaction.Friendly;
            public DeNelle.Core.Combat.CombatFaction Faction => Side;
        }

        private static void SetPrivateField(object obj, string field, object value)
        {
            var f = obj.GetType().GetField(field, BindingFlags.NonPublic | BindingFlags.Instance);
            if (f != null) f.SetValue(obj, value);
        }

        // =====================================================================
        //  VENDOR STOCK QUERIES (WO-598) — the honest shelf, gated by data
        // =====================================================================
        // Resolves every registered vendor's stock query through the REAL resolver
        // (vendors.json -> VendorRegistry -> VendorStockResolver, the same path the
        // shop VM binds) with the roster PINNED to Knight-only (the V1 canon) and
        // asserts:
        //   1. vendors.json maps to >=1 VendorDef and covers the shoppable set
        //      (forge/armorer/market/jeweler — buildings.json isShoppable).
        //   2. every vendor resolves >=1 item OR carries an authored emptyLine
        //      (never a raw empty grid — flag_11).
        //   3. NO roster-unobtainable item leaks: under Knight-only no weapon with a
        //      non-knight job and no light-weight armor may appear (flag_08's Mage
        //      wands at the Forge, as a permanent gate).
        //   4. each trade stays inside its bands: goods vendors never surface
        //      weapons/armor/accessories; the jeweler never weapons/armor/consumables;
        //      gear vendors never consumables/materials (flag_03).
        //   5. the Forge resolves >=1 ELIGIBLE item for a level-1 Knight (the V1
        //      player can actually shop on day one).
        private static void CheckVendorStock(List<string> failures, StringBuilder log)
        {
            log.AppendLine("--- VENDOR STOCK (vendors.json + VendorStockResolver, WO-598) ---");

            VendorRegistry.Reload();
            GearCatalog.Reload();
            var vendors = VendorRegistry.All;
            log.AppendLine($"vendors.json -> {vendors.Count} VendorDef objects");
            if (vendors.Count == 0)
            {
                failures.Add("vendors.json deserialized to 0 vendors (mapping break or missing file)");
                return;
            }

            // 1. Coverage: every shoppable storefront id has a registry row.
            foreach (var required in new[] { "forge", "armorer", "market", "jeweler" })
            {
                bool found = false;
                foreach (var v in vendors)
                    if (v != null && string.Equals(v.Id, required, System.StringComparison.OrdinalIgnoreCase))
                    { found = true; break; }
                if (!found)
                    failures.Add($"vendors.json is missing the shoppable vendor '{required}' (buildings.json isShoppable)");
            }

            var knightOnly = new[] { "knight" };
            foreach (var v in vendors)
            {
                if (v == null || string.IsNullOrEmpty(v.Id)) { failures.Add("vendors.json entry with null/empty id"); continue; }

                // 2. Authored empty line — required so a 0-item resolve can never render raw.
                if (string.IsNullOrEmpty(v.EmptyLine))
                    failures.Add($"vendor '{v.Id}' has no authored emptyLine (raw empty grid would render)");

                // Resolve as the V1 shopper (Knight, generous level so level gates don't hide leaks),
                // roster PINNED to knight-only so the assert is deterministic regardless of flags.
                var wares = DeNelle.Village.Hero.VendorStockResolver.Resolve(v.Id, "knight", 99, knightOnly);
                var layout = DeNelle.Village.Hero.VendorStockResolver.LayoutFor(v.Id);
                log.AppendLine($"  vendor '{v.Id}' (layout={layout}) resolved {wares.Count} ware(s)");

                if (wares.Count == 0 && string.IsNullOrEmpty(v.EmptyLine))
                    failures.Add($"vendor '{v.Id}' resolves 0 items AND has no authored emptyLine");

                foreach (var ware in wares)
                {
                    // 3. Roster leak gate: under Knight-only, no non-knight weapon / light armor.
                    if (ware.Kind == DeNelle.Village.Hero.VendorWareKind.Weapon)
                    {
                        var w = GearCatalog.FindWeapon(ware.Id);
                        if (w == null) { failures.Add($"vendor '{v.Id}' weapon '{ware.Id}' resolves to no def"); continue; }
                        if (!GearCatalog.WeaponFitsClass(w, "knight"))
                            failures.Add($"vendor '{v.Id}' stocks '{w.id}' (job='{w.job}') — roster-unobtainable under Knight-only (the flag_08 Mage-wand class of bug)");
                    }
                    else if (ware.Kind == DeNelle.Village.Hero.VendorWareKind.Armor)
                    {
                        var a = GearCatalog.FindArmor(ware.Id);
                        if (a == null) { failures.Add($"vendor '{v.Id}' armor '{ware.Id}' resolves to no def"); continue; }
                        if (!GearCatalog.ArmorFitsClass(a, "knight"))
                            failures.Add($"vendor '{v.Id}' stocks '{a.id}' (weight='{a.weight}') — roster-unobtainable under Knight-only");
                    }

                    // 4. Trade-band gate: a ware outside the vendor's layout is a wrong-shelf leak.
                    bool isGear = ware.Kind == DeNelle.Village.Hero.VendorWareKind.Weapon ||
                                  ware.Kind == DeNelle.Village.Hero.VendorWareKind.Armor;
                    bool isGoods = ware.Kind == DeNelle.Village.Hero.VendorWareKind.Consumable ||
                                   ware.Kind == DeNelle.Village.Hero.VendorWareKind.Material;
                    bool isJewel = ware.Kind == DeNelle.Village.Hero.VendorWareKind.Ring ||
                                   ware.Kind == DeNelle.Village.Hero.VendorWareKind.Amulet ||
                                   ware.Kind == DeNelle.Village.Hero.VendorWareKind.Gem;
                    switch (layout)
                    {
                        case DeNelle.Village.Hero.VendorLayout.Goods:
                            if (isGear || isJewel)
                                failures.Add($"GOODS vendor '{v.Id}' surfaced a {ware.Kind} ('{ware.Id}') — the Market must never sell gear/jewelry (flag_03)");
                            break;
                        case DeNelle.Village.Hero.VendorLayout.Jeweler:
                            if (isGear || ware.Kind == DeNelle.Village.Hero.VendorWareKind.Consumable)
                                failures.Add($"JEWELER vendor '{v.Id}' surfaced a {ware.Kind} ('{ware.Id}') — the Jeweler must never sell weapons/armor (flag_11)");
                            break;
                        case DeNelle.Village.Hero.VendorLayout.Gear:
                            if (isGoods || isJewel)
                                failures.Add($"GEAR vendor '{v.Id}' surfaced a {ware.Kind} ('{ware.Id}') — outside its trade");
                            break;
                    }
                }
            }

            // 5. The V1 day-one Knight can actually buy at the Forge (>=1 ELIGIBLE ware at Lv 1).
            var forgeLv1 = DeNelle.Village.Hero.VendorStockResolver.Resolve("forge", "knight", 1, knightOnly);
            int eligible = 0;
            foreach (var wr in forgeLv1) if (wr.Eligible) eligible++;
            log.AppendLine($"  forge @ Knight Lv1 -> {forgeLv1.Count} ware(s), {eligible} eligible");
            if (eligible == 0)
                failures.Add("the Forge resolves 0 ELIGIBLE items for a level-1 Knight — the V1 player can't shop on day one");
        }

        // =====================================================================
        //  STORE / INVENTORY ICON COVERAGE — does each real item resolve ART?
        // =====================================================================
        // Answers the felt bug ("items show letters") with DATA, no screenshot:
        // call ItemIconCatalog on every real WeaponDef/ArmorDef and count real-sprite
        // vs glyph-fallback. wand/staff/censer -> null is BY DESIGN (no art); the V1
        // failure case is a KNIGHT sword resolving to glyph (sword art exists).
        private static void CheckItemIconCoverage(List<WeaponDef> weapons, List<ArmorDef> armors,
                                                  List<string> failures, StringBuilder log)
        {
            log.AppendLine("--- ICON COVERAGE (store/inventory) ---");

            int wReal = 0, wGlyph = 0; var wGlyphSample = new List<string>();
            foreach (var w in weapons)
            {
                if (w == null) continue;
                if (ItemIconCatalog.ForWeapon(w) != null) wReal++;
                else { wGlyph++; if (wGlyphSample.Count < 24) wGlyphSample.Add((w.id ?? "?") + "/" + (w.name ?? "?")); }
            }
            int aReal = 0, aGlyph = 0; var aGlyphSample = new List<string>();
            foreach (var a in armors)
            {
                if (a == null) continue;
                if (ItemIconCatalog.ForArmor(a) != null) aReal++;
                else { aGlyph++; if (aGlyphSample.Count < 24) aGlyphSample.Add((a.id ?? "?") + "/" + (a.name ?? "?")); }
            }
            log.AppendLine($"[icon-coverage] weapons: {wReal} real / {wGlyph} glyph   armors: {aReal} real / {aGlyph} glyph");
            if (wGlyph > 0) log.AppendLine("  weapon glyphs: " + string.Join(", ", wGlyphSample));
            if (aGlyph > 0) log.AppendLine("  armor glyphs:  " + string.Join(", ", aGlyphSample));

            // V1-critical: the Knight's actual starting weapon is a sword and MUST resolve art.
            try
            {
                var knightWeapon = GearCatalog.BestWeapon("knight", 1);
                if (knightWeapon != null && ItemIconCatalog.ForWeapon(knightWeapon) == null)
                    failures.Add($"icon: Knight starting weapon '{knightWeapon.id}/{knightWeapon.name}' falls to GLYPH — sword art should resolve (real bug, not the by-design staff/censer glyph)");
                else
                    log.AppendLine($"  [icon-coverage] Knight start weapon '{(knightWeapon?.id ?? "null")}' -> {(knightWeapon != null && ItemIconCatalog.ForWeapon(knightWeapon) != null ? "REAL art OK" : "no weapon/glyph")}");
            }
            catch (System.Exception e) { log.AppendLine("[icon-coverage] knight-weapon probe threw: " + e.Message); }
        }

        private static void CheckEnemyStructureSweep(List<string> failures, StringBuilder log)
        {
            log.AppendLine("--- ENEMY STRUCTURE SWEEP (ff.enemystructureaware) ---");

            // FeatureFlags are read-only properties backed by PlayerPrefs ("ff.<name>":
            // 0=off, 1=on, -1=default). Drive the flag via the SAME key Get() reads, and
            // restore the prior pref exactly (delete if it was unset).
            const string FlagKey = "ff.enemystructureaware";
            int prevPref = PlayerPrefs.GetInt(FlagKey, -1);
            var created = new List<GameObject>();
            try
            {
                PlayerPrefs.SetInt(FlagKey, 1);   // force ON

                // Enemy at origin facing +Z. Structure 2.5m to the +X SIDE so the forward
                // probe (a short SphereCast along +Z) misses — only the all-direction sweep
                // can acquire it. This models a marching enemy with a tower off to the side.
                var enemyGo = new GameObject("OracleEnemy");
                created.Add(enemyGo);
                enemyGo.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                var enemy = enemyGo.AddComponent<Enemy>();   // auto-adds NavMeshAgent + EnemyDamageable

                var structGo = new GameObject("OracleTower");
                created.Add(structGo);
                structGo.transform.position = new Vector3(2.5f, 0f, 0f);
                structGo.AddComponent<BoxCollider>().size = Vector3.one;
                var structure = structGo.AddComponent<OracleStructure>();

                SetPrivateField(enemy, "_enemyId", "oracle-enemy");
                SetPrivateField(enemy, "_structureSweepRadius", 5f);
                SetPrivateField(enemy, "_contactProbeDistance", 1.1f);
                SetPrivateField(enemy, "_heroAggroRadius", 7f);
                SetPrivateField(enemy, "_heroAggroDropMargin", 2.5f);
                // _structureScanBuffer is a field initializer (non-null); ensure anyway.
                var bufF = typeof(Enemy).GetField("_structureScanBuffer", BindingFlags.NonPublic | BindingFlags.Instance);
                if (bufF != null && bufF.GetValue(enemy) == null) bufF.SetValue(enemy, new Collider[16]);

                Physics.SyncTransforms();

                var probe = typeof(Enemy).GetMethod("ProbeForStructure", BindingFlags.NonPublic | BindingFlags.Instance);
                if (probe == null) { failures.Add("structure sweep: Enemy.ProbeForStructure not found (renamed?)"); return; }

                // CASE A — no hero present -> the sweep MUST acquire the side structure.
                var a = probe.Invoke(enemy, null) as DeNelle.Core.Combat.IDamageableStructure;
                if (!ReferenceEquals(a, structure))
                    failures.Add($"structure sweep CASE A (no hero): expected to acquire the side structure, got '{(a as MonoBehaviour)?.name ?? "null"}' — the ff.enemystructureaware sweep did NOT fire");
                else
                    log.AppendLine("  CASE A no-hero: sweep acquired side structure OK");

                // CASE B — hero within aggro -> sweep SUPPRESSED (hero stays primary).
                var heroGo = new GameObject("OracleHero");
                created.Add(heroGo);
                heroGo.transform.position = new Vector3(0f, 0f, 1.5f);   // inside aggro radius
                SetPrivateField(enemy, "_heroTransform", heroGo.transform);
                Physics.SyncTransforms();
                var b = probe.Invoke(enemy, null) as DeNelle.Core.Combat.IDamageableStructure;
                if (b != null)
                    failures.Add($"structure sweep CASE B (hero in aggro): should SUPPRESS, but returned '{(b as MonoBehaviour)?.name}' — hero-primary gate broken");
                else
                    log.AppendLine("  CASE B hero-in-aggro: sweep suppressed OK");

                // CASE C — flag OFF -> legacy forward-only; sweep must be inert (reversible).
                SetPrivateField(enemy, "_heroTransform", null);
                PlayerPrefs.SetInt(FlagKey, 0);   // force OFF (legacy)
                var c = probe.Invoke(enemy, null) as DeNelle.Core.Combat.IDamageableStructure;
                if (c != null)
                    failures.Add($"structure sweep CASE C (flag off): should be legacy forward-only, but sweep returned '{(c as MonoBehaviour)?.name}' — not reversible");
                else
                    log.AppendLine("  CASE C flag-off: sweep inert (legacy) OK");

                // =============================================================
                //  WO-1439 — FRIENDLY FIRE. A defender must never select a structure
                //  of its OWN faction. Cases D/E cover the all-direction sweep lane,
                //  F/G the forward SphereCast lane; both lanes could acquire the spire.
                //
                //  ⛔ RED PROOF (state it in-file, per the WO's acceptance criteria):
                //  against the pre-WO-1439 build CASE D and CASE F both FAIL. Before the
                //  fix, IDamageableStructure carried no Faction at all, so this file did
                //  not even compile — and with the stub's Faction stubbed out, the sweep's
                //  filter chain was null -> IsAlive -> `is HeroHealth` and NOTHING ELSE
                //  (Enemy.cs SweepForNearestStructure), while the forward lane's was
                //  `structure != null && structure.IsAlive` (ProbeForStructureForward).
                //  A Hostile stand-in in front of a Hostile Enemy was therefore ACQUIRED
                //  and RETURNED by both lanes — which is precisely the shipped defect:
                //  11,620 `[Flow:EnemyAggro] raidguard-*: ProbeForStructure hit 'RaidSpire'`
                //  lines in logs/debug/raid-ai-and-pets-2026-09-06.log, 8,359 of them after
                //  the scene had already resolved Enemy-owned. Cases E and G are the other
                //  half of the proof: they pin that the gate is FACTION-specific and did
                //  not simply break acquisition for everyone.
                // =============================================================
                PlayerPrefs.SetInt(FlagKey, 1);            // sweep lane back ON
                SetPrivateField(enemy, "_heroTransform", null);
                structGo.transform.position = new Vector3(2.5f, 0f, 0f);   // to the SIDE => sweep lane
                Physics.SyncTransforms();

                // The Enemy under test is Hostile (EnemyDamageable, auto-added by RequireComponent).
                // Read it rather than asserting it, so a future faction change surfaces here.
                var selfFactionProp = typeof(Enemy).GetProperty("SelfFaction");
                var self = selfFactionProp != null
                    ? (DeNelle.Core.Combat.CombatFaction)selfFactionProp.GetValue(enemy)
                    : DeNelle.Core.Combat.CombatFaction.Hostile;
                if (selfFactionProp == null)
                    failures.Add("WO-1439: Enemy.SelfFaction not found (renamed?) — the friendly-fire " +
                                 "cases below cannot prove which side the attacker is on");
                log.AppendLine($"  WO-1439 attacker faction = {self}");

                // CASE D — SAME-faction structure in sweep range -> MUST be refused.
                structure.Side = self;
                var d = probe.Invoke(enemy, null) as DeNelle.Core.Combat.IDamageableStructure;
                if (d != null)
                    failures.Add($"WO-1439 CASE D (sweep, same faction): a {self} enemy acquired the " +
                                 $"{structure.Side} structure '{(d as MonoBehaviour)?.name}'. A defender is " +
                                 "attacking its own side — this is the raid-spire defect (a garrison razing " +
                                 "the objective it guards).");
                else
                    log.AppendLine("  CASE D sweep same-faction: refused OK");

                // CASE E — OPPOSING structure in the same spot -> must STILL be acquired.
                structure.Side = self == DeNelle.Core.Combat.CombatFaction.Hostile
                    ? DeNelle.Core.Combat.CombatFaction.Friendly
                    : DeNelle.Core.Combat.CombatFaction.Hostile;
                var e = probe.Invoke(enemy, null) as DeNelle.Core.Combat.IDamageableStructure;
                if (!ReferenceEquals(e, structure))
                    failures.Add($"WO-1439 CASE E (sweep, opposing faction): expected the {structure.Side} " +
                                 $"structure to still be acquired, got '{(e as MonoBehaviour)?.name ?? "null"}' — " +
                                 "the faction gate broke acquisition for EVERYONE, not just friendlies.");
                else
                    log.AppendLine("  CASE E sweep opposing-faction: acquired OK");

                // CASE F/G — the FORWARD SphereCast lane. It returns before the sweep ever
                // runs, so a faction gate on the sweep alone would leave the defect wide open
                // for anything the defender happens to be facing — which is exactly how the
                // spire was hit (the captured line is the forward lane's "ProbeForStructure hit").
                // Dead AHEAD and clear of the cast's start sphere: the cast begins at
                // (0, 0.5, 0) with radius 0.4, so a box centred at z=1.0 (spanning 0.5..1.5)
                // is reached by the sweep but does NOT overlap at t=0 — an initial overlap
                // makes SphereCast's hit degenerate and the case would prove nothing.
                structGo.transform.position = new Vector3(0f, 0f, 1.0f);   // probe dist 1.1
                Physics.SyncTransforms();

                structure.Side = self;
                var f = probe.Invoke(enemy, null) as DeNelle.Core.Combat.IDamageableStructure;
                if (f != null)
                    failures.Add($"WO-1439 CASE F (forward probe, same faction): a {self} enemy acquired the " +
                                 $"{structure.Side} structure '{(f as MonoBehaviour)?.name}' straight ahead. " +
                                 "ProbeForStructureForward is the lane the shipped capture fired on.");
                else
                    log.AppendLine("  CASE F forward same-faction: refused OK");

                structure.Side = self == DeNelle.Core.Combat.CombatFaction.Hostile
                    ? DeNelle.Core.Combat.CombatFaction.Friendly
                    : DeNelle.Core.Combat.CombatFaction.Hostile;
                var g = probe.Invoke(enemy, null) as DeNelle.Core.Combat.IDamageableStructure;
                if (!ReferenceEquals(g, structure))
                    failures.Add($"WO-1439 CASE G (forward probe, opposing faction): expected the " +
                                 $"{structure.Side} structure straight ahead to still be acquired, got " +
                                 $"'{(g as MonoBehaviour)?.name ?? "null"}' — the forward lane now refuses " +
                                 "everything, which would stop enemies hitting the hero.");
                else
                    log.AppendLine("  CASE G forward opposing-faction: acquired OK");

                // =============================================================
                //  WO-1503 — CASE H/I: the HERO's melee is bound by the same one
                //  authority. D-G prove an ENEMY refuses its own side; nothing pinned
                //  the reciprocal, and that gap is what let a work order be minted
                //  claiming the hero was chewing through its own hub root.
                //
                //  It never was. Proven from source, not inferred:
                //    * CastleHubRoot has ONE component, a Transform (Main_Castle_Overworld.unity,
                //      GameObject fileID 1385856591) -> no IDamageable, no faction, not a target.
                //    * Every wave enemy is parented under WaveEnemies -> CastleHubRoot
                //      (WaveManager._enemyRoot fileID 414338686), so `transform.root.name` is
                //      "CastleHubRoot" for EVERY hostile in the hub. The old trace printed only
                //      that root, so a correct kill read as an attack on the castle.
                //  PlayerAttackController now names the target AND calls MayAttack; these two
                //  cases pin the refusal so the reciprocal can never regress unnoticed.
                //
                //  ⛔ RED PROOF, stated per case because they go red on DIFFERENT edits:
                //    * CASE H fails if CombatFactionRules.Decide regresses — change it to admit a
                //      same-faction target (e.g. return `alive` alone) and MayAttack(Friendly,
                //      Friendly-and-alive) returns true.
                //    * CASE I fails if the rule becomes a blanket refusal (e.g. return false
                //      unconditionally) — it pins that H is FACTION-specific, not a broken
                //      predicate that would also stop the hero breaking an enemy wall in a raid.
                //    * CASE J (the source pin) fails if PlayerAttackController stops calling the
                //      authority — restore the old inline `damageable.Faction !=
                //      CombatFaction.Hostile` guard and J goes red. H and I would stay GREEN
                //      through that revert, because they call the rule directly and never touch
                //      the melee path; without J this block would pin the rule and claim to pin
                //      the caller. That gap is the whole reason J exists.
                // =============================================================
                const DeNelle.Core.Combat.CombatFaction heroSide = DeNelle.Core.Combat.CombatFaction.Friendly;   // HeroHealth.cs:1630

                var ownedGo = new GameObject("OraclePlayerStructure");
                created.Add(ownedGo);
                var owned = ownedGo.AddComponent<OracleStructure>();
                owned.Side = heroSide;    // a player-owned town structure (hub root, wall, tower)
                owned.Alive = true;

                // CASE H — hero strike on a player-owned structure -> REFUSED by the one rule.
                DeNelle.Core.Combat.IDamageableStructure ownedAsStruct = owned;
                if (DeNelle.Core.Combat.CombatFactionRules.MayAttack(heroSide, ownedAsStruct))
                    failures.Add("WO-1503 CASE H (hero vs player-owned structure): CombatFactionRules.MayAttack " +
                                 $"({heroSide}, {owned.Side} structure) returned TRUE — the hero may damage its own " +
                                 "town. PlayerAttackController's melee guard calls this exact predicate.");
                else
                    log.AppendLine("  CASE H hero vs player-owned structure: refused OK");

                if (!DeNelle.Core.Combat.CombatFactionRules.IsFriendlyFire(heroSide, ownedAsStruct))
                    failures.Add("WO-1503 CASE H (friendly-fire classification): IsFriendlyFire" +
                                 $"({heroSide}, {owned.Side} structure) returned FALSE — the instrumentation can no " +
                                 "longer name same-faction as the rejection reason.");
                else
                    log.AppendLine("  CASE H friendly-fire classification: same-faction named OK");

                // CASE I — the SAME stand-in flipped Hostile must still be attackable, so H is
                // proven to be a faction gate and not a broken predicate refusing everything.
                owned.Side = DeNelle.Core.Combat.CombatFaction.Hostile;
                if (!DeNelle.Core.Combat.CombatFactionRules.MayAttack(heroSide, ownedAsStruct))
                    failures.Add("WO-1503 CASE I (hero vs hostile structure): MayAttack" +
                                 $"({heroSide}, Hostile structure) returned FALSE — the hero's melee now refuses " +
                                 "EVERY structure, which would stop the player breaking enemy walls in a raid.");
                else
                    log.AppendLine("  CASE I hero vs hostile structure: attackable OK");

                // CASE J — the CALLER pin. H and I prove the rule; only this proves the hero's
                // melee is bound BY it. Source-text pin, the same shape sibling suites already
                // use on this exact file (CombatCastCaravanMarkRegression:228,
                // PrimaryFallbackRegression Case4_FallbackIsFree).
                string pacPath = System.IO.Path.Combine(Application.dataPath,
                    "_Modules/Village/Enemies/PlayerAttackController.cs");
                if (!System.IO.File.Exists(pacPath))
                {
                    failures.Add("WO-1503 CASE J: PlayerAttackController.cs not found at " + pacPath +
                                 " (moved?) — the hero's melee guard could not be pinned to the faction authority.");
                }
                else
                {
                    string pacSrc = System.IO.File.ReadAllText(pacPath);
                    if (pacSrc.IndexOf("CombatFactionRules.MayAttack(HeroFaction, damageable)",
                                       System.StringComparison.Ordinal) < 0)
                        failures.Add("WO-1503 CASE J: PlayerAttackController's melee sweep no longer calls " +
                                     "CombatFactionRules.MayAttack(HeroFaction, damageable). The hero's friend-or-foe " +
                                     "test has left the ONE authority — a second answer on faction is exactly what " +
                                     "CombatFactionRules exists to prevent (see its header).");
                    else
                        log.AppendLine("  CASE J melee guard calls the faction authority: OK");

                    // The inline copy this replaced must not come back alongside it. Comments
                    // quoting it are legitimate (the WO-1503 note does); a live statement is not.
                    if (System.Text.RegularExpressions.Regex.IsMatch(
                            pacSrc, @"^(?!\s*//).*damageable\.Faction\s*!=\s*CombatFaction\.Hostile",
                            System.Text.RegularExpressions.RegexOptions.Multiline))
                        failures.Add("WO-1503 CASE J: the inline `damageable.Faction != CombatFaction.Hostile` " +
                                     "predicate is back in PlayerAttackController. One rule, called everywhere — " +
                                     "a hand-copy is the duplicated-state failure this repo has paid for repeatedly.");
                    else
                        log.AppendLine("  CASE J no inline faction copy in the melee path: OK");
                }

                // =============================================================
                //  WO-1524 — CASE K: the SAME caller pin, widened past the melee lane.
                //
                //  CASE J pins ONE file. That is exactly how the gap it closed was allowed to
                //  exist: WO-1438's own header claimed Pet was "the REMAINING copy" (singular),
                //  WO-1503 counted them and found THIRTEEN across five more files, and a
                //  hand-maintained census in a comment is duplicated state by construction
                //  (CLAUDE.md §2/§5/§8/§16). So this case does not carry a list of line
                //  numbers - it re-derives the answer from the files themselves, every run.
                //
                //  Converted by WO-1524 (verified at source that date):
                //    Pets/Pet.cs:556,635 · Buildings/ArcaneTower.cs:386,397 ·
                //    Buildings/DefenseTower.cs:717,730 · Buildings/TowerCombat.cs:229,243,283,294 ·
                //    Hero/HeroAbilities.cs:3097,3125
                //  Those numbers are provenance, NOT a check - the check below is line-agnostic.
                //
                //  ⛔ RED PROOF: restore any one of them (e.g. put
                //  `if (dmg == null || !dmg.IsAlive || dmg.Faction != CombatFaction.Hostile) continue;`
                //  back into Pet.NearestHostile) and (a) goes red naming that file and line.
                //  Delete a CombatFactionRules call instead and (b) goes red. The two halves fail
                //  on DIFFERENT edits on purpose: (a) alone would pass a file that had deleted the
                //  guard outright, and (b) alone would pass a file that called the authority AND
                //  kept a second inline answer beside it - which is precisely the divergence
                //  WO-1503 found inside PlayerAttackController (melee routed, ability lane not).
                //
                //  Comments quoting the retired predicate stay legal - every converted file
                //  documents what it replaced, and the ^(?!\s*//) guard skips // and /// lines.
                // =============================================================
                string[] factionCallers =
                {
                    "_Modules/Pets/Pet.cs",
                    "_Modules/Village/Buildings/ArcaneTower.cs",
                    "_Modules/Village/Buildings/DefenseTower.cs",
                    "_Modules/Village/Buildings/TowerCombat.cs",
                    "_Modules/Village/Hero/HeroAbilities.cs",
                };
                foreach (string rel in factionCallers)
                {
                    string abs = System.IO.Path.Combine(Application.dataPath, rel);
                    if (!System.IO.File.Exists(abs))
                    {
                        failures.Add($"WO-1524 CASE K: {rel} not found at {abs} (moved/renamed?) — a " +
                                     "faction call site could not be pinned to the one authority.");
                        continue;
                    }

                    string src = System.IO.File.ReadAllText(abs);

                    // (a) NO live inline second answer. Any non-comment line comparing a
                    //     .Faction against a CombatFaction member is a hand-copy of the one
                    //     predicate. Line number is reported so the fix is one jump away.
                    var inline = System.Text.RegularExpressions.Regex.Matches(
                        src, @"^(?!\s*//).*\.Faction\s*(?:!=|==)\s*CombatFaction\.",
                        System.Text.RegularExpressions.RegexOptions.Multiline);
                    if (inline.Count > 0)
                    {
                        var where = new System.Text.StringBuilder();
                        foreach (System.Text.RegularExpressions.Match m in inline)
                        {
                            int line = 1;
                            for (int ci = 0; ci < m.Index && ci < src.Length; ci++)
                                if (src[ci] == '\n') line++;
                            if (where.Length > 0) where.Append(", ");
                            where.Append(rel).Append(':').Append(line);
                        }
                        failures.Add($"WO-1524 CASE K (inline faction copy): {inline.Count} live " +
                                     $"`Faction != / == CombatFaction.*` comparison(s) at {where} — a SECOND " +
                                     "authority on friend-or-foe. Route it through " +
                                     "CombatFactionRules.MayAttack / IsFriendlyFire (Core/Combat/" +
                                     "CombatFactionRules.cs, whose header forbids exactly this copy). " +
                                     "If a site genuinely needs different behaviour, that belongs in the " +
                                     "rule as a named case, never as inline code.");
                    }
                    else
                    {
                        log.AppendLine($"  CASE K no inline faction copy in {rel}: OK");
                    }

                    // (b) …AND the authority is actually CALLED. Without this half, deleting the
                    //     guard outright would read as a pass.
                    if (src.IndexOf("CombatFactionRules.", System.StringComparison.Ordinal) < 0)
                        failures.Add($"WO-1524 CASE K (authority uncalled): {rel} no longer calls " +
                                     "CombatFactionRules at all. Its target selection either lost its " +
                                     "friend-or-foe test entirely (pets/towers/hero abilities would " +
                                     "engage their own side — the WO-1439 defect) or answered it some " +
                                     "other way. One rule, called everywhere.");
                    else
                        log.AppendLine($"  CASE K {rel} calls the faction authority: OK");
                }
            }
            catch (System.Exception ex)
            {
                failures.Add($"structure sweep oracle threw: {ex.GetType().Name}: {ex.Message}");
            }
            finally
            {
                if (prevPref == -1) PlayerPrefs.DeleteKey(FlagKey);
                else PlayerPrefs.SetInt(FlagKey, prevPref);
                foreach (var go in created) if (go != null) UnityEngine.Object.DestroyImmediate(go);
            }
        }

        // =====================================================================
        //  ABILITIES — abilities.json via AbilityCatalog (the real loader)
        // =====================================================================
        private static void CheckAbilities(List<string> failures, StringBuilder log)
        {
            AbilityCatalog.Reload();

            // Enumerate every class loadout the catalog exposes. We probe the known
            // hero classes (the catalog is keyed by lowercase class id); the default
            // 'mage' is the v2-foundation class that MUST be present.
            string[] classes = { "mage", "knight", "ranger", "cleric" };
            int total = 0;
            int bad = 0;
            foreach (var cls in classes)
            {
                var loadout = AbilityCatalog.GetLoadout(cls);
                if (loadout == null || loadout.Count == 0) continue;   // class simply not authored yet
                foreach (var ab in loadout)
                {
                    total++;
                    bool ok = ab != null
                              && !string.IsNullOrEmpty(ab.Slot)
                              && !string.IsNullOrEmpty(ab.Name);
                    if (!ok) bad++;
                    log.AppendLine($"  AB [{cls}] slot='{(ab != null ? ab.Slot : "<null>")}' " +
                                   $"name='{(ab != null ? ab.Name : "<null>")}' " +
                                   $"effect='{(ab != null ? ab.Effect : "<null>")}'");
                }
            }

            log.AppendLine($"abilities.json -> {total} AbilityDef object(s) across {classes.Length} probed class(es)");

            // The default 'mage' loadout is the proven v2 content: if it is empty, the
            // JSON->object mapping broke (wrong top-level key 'classes', renamed slots,
            // or a parse-to-empty) exactly like the gear case.
            if (AbilityCatalog.GetLoadout(AbilityCatalog.DefaultClass).Count == 0)
                failures.Add($"abilities.json: default class '{AbilityCatalog.DefaultClass}' has 0 abilities (mapping break or empty 'classes')");
            if (total == 0)
                failures.Add("abilities.json deserialized to 0 AbilityDef objects (mapping break or empty 'classes')");
            if (bad > 0)
                failures.Add($"{bad} ability(ies) have null/empty slot or name (would render blank on the hotbar)");
        }

        // =====================================================================
        //  ENEMIES — enemies.json via EnemyCatalog + the FACTORY model-path resolve
        // =====================================================================
        private static void CheckEnemies(List<string> failures, StringBuilder log)
        {
            // Load the catalog through the same WebGL-safe bytes WaveDataLoader reads
            // (its step 1 is CanonicalJson.Read; we deserialize the same way it does so
            // a schema/key break here is the SAME break the game would hit). The async
            // WaveDataLoader.LoadEnemiesAsync isn't awaitable in this sync harness, so we
            // mirror its exact parse: CanonicalJson.Read -> JsonConvert<EnemyCatalog>.
            string json = DeNelle.Core.CanonicalJson.Read(WaveDataLoader.EnemiesRelativePath);
            EnemyCatalog catalog = null;
            if (!string.IsNullOrEmpty(json))
            {
                try { catalog = JsonConvert.DeserializeObject<EnemyCatalog>(json); }
                catch (System.Exception ex)
                {
                    failures.Add($"enemies.json failed to parse: {ex.Message}");
                    log.AppendLine($"enemies.json -> PARSE ERROR: {ex.Message}");
                    return;
                }
            }

            if (catalog == null || catalog.Enemies == null || catalog.Enemies.Count == 0)
            {
                failures.Add("enemies.json deserialized to 0 EnemyDef objects (mapping break or empty 'enemies')");
                log.AppendLine("enemies.json -> 0 EnemyDef objects");
                return;
            }

            log.AppendLine($"enemies.json -> {catalog.Enemies.Count} EnemyDef object(s)");

            int badField = 0;
            foreach (var e in catalog.Enemies)
            {
                // Skip the schema-doc placeholder row (its id is the field description, not
                // a real enemy) — be conservative, don't fail on a documented non-entry.
                if (e != null && e.Id != null && e.Id.Contains(" ")) continue;

                bool ok = e != null && !string.IsNullOrEmpty(e.Id) && !string.IsNullOrEmpty(e.Name);
                if (!ok) { badField++; continue; }

                // PREFAB-PATH CHECK (catches the archer->lumber class). Resolve the model
                // EXACTLY as the single enemy-creation path does (EnemyFactory.ModelForEnemy),
                // then attempt the same load the factory's VisualFactory.Skin call performs —
                // through DeNelle.Core.EnemyAssetLoader (Addressables-first, Resources-fallback),
                // NOT a raw Resources.Load, so the check survives the Addressables migration.
                // A null load means this enemy ships as a tinted-capsule fallback at runtime —
                // a silent regression.
                string model = EnemyFactory.ModelForEnemy(e);
                string path = "Enemies/" + model;
                var prefab = DeNelle.Core.EnemyAssetLoader.LoadEnemyPrefab(model);
                if (prefab == null)
                {
                    failures.Add($"enemies.json: '{e.Id}' resolves to model '{model}' but EnemyAssetLoader could not resolve \"{path}\" via Addressables OR Resources (would spawn as a tinted capsule — wrong/missing prefab)");
                    log.AppendLine($"  EN {e.Id} -> model '{model}' | PREFAB UNRESOLVABLE via EnemyAssetLoader ('{path}': neither Addressables nor Resources)");
                }
                else
                {
                    log.AppendLine($"  EN {e.Id} -> model '{model}' | prefab OK via EnemyAssetLoader ('{path}')");
                }
            }
            if (badField > 0)
                failures.Add($"{badField} enemy(ies) have null/empty id or name");
        }

        // =====================================================================
        //  WAVE-SCALING (CITY-01 / CITY-06) + KILL REWARDS (BLIND-03-01)
        //  Two assertions that headlessly prove the core wave loop is no longer a
        //  no-progression, no-escalation treadmill:
        //   (1) the runtime DEFAULT WaveScalingCurve (the fallback WaveManager now
        //       ALWAYS creates when no asset is wired) applies a stat multiplier >1
        //       past wave 1 and keeps climbing - so wave 19 enemies are NOT wave-1
        //       enemies (the CITY-01 "no headless proof" gap, CITY-06).
        //   (2) every real enemies.json row carries xpReward>0 AND coinReward>0 - so
        //       the most-played mode pays hero XP + gold on every kill (BLIND-03-01).
        // =====================================================================
        private static void CheckWaveScaling(List<string> failures, StringBuilder log)
        {
            log.AppendLine("--- [wave-scaling] default-curve escalation + kill rewards ---");

            // (1) DEFAULT curve escalation. CreateInstance runs WaveScalingCurve's field
            //     initializers, which seed the default HP/speed/damage curves - the exact
            //     object WaveManager.EnsureScalingCurve now returns when no asset is wired.
            var curve = ScriptableObject.CreateInstance<WaveScalingCurve>();
            float hp1  = curve.HpMultiplier(1);
            float hp10 = curve.HpMultiplier(10);
            float hp19 = curve.HpMultiplier(19);
            float dmg19 = curve.DamageMultiplier(19);
            float spd19 = curve.SpeedMultiplier(19);
            log.AppendLine($"  default curve HP x{hp1:0.00}@w1 -> x{hp10:0.00}@w10 -> x{hp19:0.00}@w19; " +
                           $"dmg x{dmg19:0.00}@w19; spd x{spd19:0.00}@w19");
            if (!(hp10 > 1f))
                failures.Add($"[wave-scaling] default HP multiplier at wave 10 is {hp10:0.00} (expected >1 - wave scaling would be DEAD)");
            if (!(hp19 > hp1))
                failures.Add($"[wave-scaling] default HP multiplier does not increase by wave 19 ({hp19:0.00} <= wave-1 {hp1:0.00})");
            if (!(dmg19 > 1f))
                failures.Add($"[wave-scaling] default contact-damage multiplier at wave 19 is {dmg19:0.00} (expected >1)");
            UnityEngine.Object.DestroyImmediate(curve);

            // (2) enemies.json kill rewards. Parse through the same CanonicalJson bytes the
            //     game reads (WaveDataLoader path), then assert every real row pays out.
            string json = DeNelle.Core.CanonicalJson.Read(WaveDataLoader.EnemiesRelativePath);
            EnemyCatalog catalog = null;
            if (!string.IsNullOrEmpty(json))
            {
                try { catalog = JsonConvert.DeserializeObject<EnemyCatalog>(json); }
                catch (System.Exception ex)
                {
                    failures.Add($"[wave-scaling] enemies.json failed to parse for reward check: {ex.Message}");
                    return;
                }
            }
            if (catalog == null || catalog.Enemies == null || catalog.Enemies.Count == 0)
            {
                failures.Add("[wave-scaling] enemies.json produced 0 rows for the reward check");
                return;
            }

            int checkedRows = 0, noReward = 0;
            foreach (var e in catalog.Enemies)
            {
                // Skip the schema-doc placeholder row (id carries a space - see CheckEnemies).
                if (e == null || string.IsNullOrEmpty(e.Id) || e.Id.Contains(" ")) continue;
                checkedRows++;
                if (e.XpReward <= 0 || e.CoinReward <= 0)
                {
                    noReward++;
                    failures.Add($"[wave-scaling] enemy '{e.Id}' missing kill rewards " +
                                 $"(xpReward={e.XpReward}, coinReward={e.CoinReward}; both must be > 0)");
                }
            }
            log.AppendLine($"  reward coverage: {checkedRows - noReward}/{checkedRows} enemy rows carry xp+coin rewards");
        }

        // =====================================================================
        //  STRUCTURES — structures-catalog.json visualPrefabPath load + type check
        // =====================================================================
        [System.Serializable]
        private sealed class StructuresCatalogFile
        {
            [JsonProperty("version")] public int Version;
            [JsonProperty("entries")] public List<CatalogEntry> Entries = new List<CatalogEntry>();
        }

        private static void CheckStructures(List<string> failures, StringBuilder log)
        {
            // Parse identically to the production CatalogBootstrap.LoadFromJson so a
            // schema break shows up HERE the same way it would at startup.
            string json = DeNelle.Core.CanonicalJson.Read("Data/Canonical/structures-catalog.json");
            StructuresCatalogFile file = null;
            if (!string.IsNullOrEmpty(json))
            {
                try
                {
                    var settings = new JsonSerializerSettings
                    {
                        Converters = { new StringEnumConverter() },
                        NullValueHandling = NullValueHandling.Ignore,
                        MissingMemberHandling = MissingMemberHandling.Ignore,
                    };
                    file = JsonConvert.DeserializeObject<StructuresCatalogFile>(json, settings);
                }
                catch (System.Exception ex)
                {
                    failures.Add($"structures-catalog.json failed to parse: {ex.Message}");
                    log.AppendLine($"structures-catalog.json -> PARSE ERROR: {ex.Message}");
                    return;
                }
            }

            if (file == null || file.Entries == null || file.Entries.Count == 0)
            {
                failures.Add("structures-catalog.json deserialized to 0 CatalogEntry objects (mapping break or empty 'entries')");
                log.AppendLine("structures-catalog.json -> 0 CatalogEntry objects");
                return;
            }

            log.AppendLine($"structures-catalog.json -> {file.Entries.Count} CatalogEntry object(s)");

            int badField = 0;
            foreach (var entry in file.Entries)
            {
                bool ok = entry != null && !string.IsNullOrEmpty(entry.id) && !string.IsNullOrEmpty(entry.displayName);
                if (!ok) { badField++; continue; }

                // Composites have no own mesh (they bundle cell entries) and a sparse
                // decoration row may legitimately omit visualPrefabPath — only assert the
                // ones the catalog ACTUALLY declares a path for (conservative).
                if (string.IsNullOrEmpty(entry.visualPrefabPath))
                {
                    log.AppendLine($"  ST {entry.id} | no visualPrefabPath (composite/decoration) — skipped");
                    continue;
                }

                // PREFAB-PATH CHECK: StructureFactory.Create -> VisualFactory.Skin does
                // Resources.Load<GameObject>(visualPrefabPath). A null load = the structure
                // builds with NO mesh (StructureFactory.cs:88-90 warning) — the archer->
                // lumber class for towers/structures.
                var prefab = DeNelle.Core.StructureAssetLoader.LoadStructurePrefab(entry.visualPrefabPath);
                if (prefab == null)
                {
                    failures.Add($"structures-catalog.json: '{entry.id}' visualPrefabPath '{entry.visualPrefabPath}' loads NULL (structure would build with no mesh — wrong/missing prefab)");
                    log.AppendLine($"  ST {entry.id} -> '{entry.visualPrefabPath}' | PREFAB MISSING");
                }
                else
                {
                    log.AppendLine($"  ST {entry.id} -> '{entry.visualPrefabPath}' | prefab OK");
                }
            }
            if (badField > 0)
                failures.Add($"{badField} structure entry(ies) have null/empty id or displayName");

            // The founding Default Town was previously disabled after the player-placeable
            // jeweler rendered upside down. Pin the actual shared creation seam: +90 is the
            // render-proven upright sign, and OptsFor must carry it into PRE-fit LocalRotation.
            var jeweler = file.Entries.Find(e => e != null &&
                string.Equals(e.id, "jeweler", System.StringComparison.OrdinalIgnoreCase));
            if (jeweler == null)
            {
                failures.Add("structures-catalog.json: missing 'jeweler' entry (Default Town contract)");
            }
            else if (jeweler.orientation == null || !jeweler.orientation.manual ||
                     Mathf.Abs(Mathf.DeltaAngle(90f, jeweler.orientation.Euler.x)) > 0.1f)
            {
                failures.Add("structures-catalog.json: jeweler must author manual Euler X=+90 (render-proven upright orientation)");
            }
            else
            {
                var jewelerOpts = StructureFactory.OptsFor(jeweler);
                Quaternion appliedRotation = jewelerOpts.LocalRotation ?? Quaternion.identity;
                float appliedX = Mathf.DeltaAngle(0f, appliedRotation.eulerAngles.x);
                if (Mathf.Abs(appliedX - 90f) > 0.1f)
                    failures.Add($"StructureFactory.OptsFor(jeweler) applied X={appliedX:0.0}, expected +90 before Fit");
                else
                    log.AppendLine("[jeweler-upright] catalog + StructureFactory pre-fit LocalRotation = +90 degrees");
            }
        }

        // =====================================================================
        //  SINGLETON + BAKED-TWIN INTEGRITY - StructureSingleton v2 (owner
        //  only-ever-one ruling): a repo.singleton row + repo.bakedTwins must be
        //  fully enforced with ZERO code, so the DATA must hold these invariants.
        // =====================================================================
        private static void CheckSingletons(List<string> failures, StringBuilder log)
        {
            int before = failures.Count;
            log.AppendLine("[singletons] repo.singleton + bakedTwins integrity (StructureSingleton v2):");

            var settings = new JsonSerializerSettings
            {
                Converters = { new StringEnumConverter() },
                NullValueHandling = NullValueHandling.Ignore,
                MissingMemberHandling = MissingMemberHandling.Ignore,
            };

            StructuresCatalogFile Load(string path, string label)
            {
                if (!System.IO.File.Exists(path))
                {
                    failures.Add($"[singletons] {label} catalog copy MISSING at '{path}'");
                    return null;
                }
                try
                {
                    return JsonConvert.DeserializeObject<StructuresCatalogFile>(
                        System.IO.File.ReadAllText(path), settings);
                }
                catch (System.Exception ex)
                {
                    failures.Add($"[singletons] {label} catalog copy failed to parse: {ex.Message}");
                    return null;
                }
            }

            // Raw file reads on purpose: the dual-copy contract is between the two
            // COMMITTED files, not whatever CanonicalJson happens to resolve first.
            string srcPath = System.IO.Path.Combine(Application.dataPath, "StreamingAssets/Data/Canonical/structures-catalog.json");
            string resPath = System.IO.Path.Combine(Application.dataPath, "Resources/Data/Canonical/structures-catalog.json");
            var src = Load(srcPath, "StreamingAssets");
            var res = Load(resPath, "Resources");
            if (src == null || res == null || src.Entries == null || res.Entries == null) return;

            // (a) shape: every bakedTwins entry non-empty; twin names UNIQUE across the
            //     catalog (a twin represents exactly one row); bakedTwins only on
            //     singleton-flagged rows (the enforcement sweep only walks those).
            var twinOwner = new Dictionary<string, string>();
            var srcById = new Dictionary<string, CatalogEntry>();
            int singletonRows = 0, twinRows = 0;
            foreach (var e in src.Entries)
            {
                if (e == null || string.IsNullOrEmpty(e.id) || e.repo == null) continue;
                srcById[e.id] = e;
                if (e.repo.singleton) singletonRows++;
                var twins = e.repo.bakedTwins;
                if (twins == null || twins.Length == 0) continue;
                twinRows++;
                if (!e.repo.singleton)
                    failures.Add($"[singletons] '{e.id}' authors bakedTwins but is NOT flagged repo.singleton - " +
                                 "twin standdown/resurface only runs for singleton rows (StructureSingleton.EnforceAll)");
                foreach (var name in twins)
                {
                    if (string.IsNullOrWhiteSpace(name))
                    {
                        failures.Add($"[singletons] '{e.id}' has a null/empty bakedTwins entry");
                        continue;
                    }
                    if (twinOwner.TryGetValue(name, out var owner))
                        failures.Add($"[singletons] baked twin '{name}' is claimed by BOTH '{owner}' and '{e.id}' - " +
                                     "a baked twin must represent exactly ONE catalog row");
                    else
                        twinOwner[name] = e.id;
                }
            }
            log.AppendLine($"  {singletonRows} singleton row(s); {twinRows} row(s) author bakedTwins " +
                           $"({twinOwner.Count} unique twin name(s))");

            // (b) dual-copy parity on the singleton + bakedTwins fields (the two files
            //     must stay byte-identical in these fields; compare the parsed values).
            var resById = new Dictionary<string, CatalogEntry>();
            foreach (var e in res.Entries)
                if (e != null && !string.IsNullOrEmpty(e.id)) resById[e.id] = e;
            foreach (var e in src.Entries)
            {
                if (e == null || string.IsNullOrEmpty(e.id) || e.repo == null) continue;
                if (!resById.TryGetValue(e.id, out var r) || r.repo == null)
                {
                    failures.Add($"[singletons] row '{e.id}' present in the StreamingAssets copy but missing " +
                                 "(or repo-less) in the Resources copy");
                    continue;
                }
                if (e.repo.singleton != r.repo.singleton)
                    failures.Add($"[singletons] '{e.id}' repo.singleton differs between copies " +
                                 $"(StreamingAssets={e.repo.singleton}, Resources={r.repo.singleton})");
                string a = e.repo.bakedTwins == null ? "" : string.Join("|", e.repo.bakedTwins);
                string b = r.repo.bakedTwins == null ? "" : string.Join("|", r.repo.bakedTwins);
                if (a != b)
                    failures.Add($"[singletons] '{e.id}' repo.bakedTwins differs between copies " +
                                 $"(StreamingAssets='{a}', Resources='{b}')");
            }

            // (c) migration-census coverage: every (bakedName,itemId) the WO-673 census
            //     maps onto a SINGLETON row must appear in that row's bakedTwins - else
            //     StructureSingleton (catalog-only in v2) cannot see the twin the
            //     migration/injector lane manages. Plus the explicit barracks pin.
            foreach (var (bakedName, itemId) in StrategicPlacementMigration.BakedStorefronts())
            {
                if (!srcById.TryGetValue(itemId, out var row) || row.repo == null) continue;   // row not authored yet
                if (!row.repo.singleton) continue;                                             // census row not singleton - out of scope
                bool listed = row.repo.bakedTwins != null &&
                              System.Array.IndexOf(row.repo.bakedTwins, bakedName) >= 0;
                if (!listed)
                    failures.Add($"[singletons] migration census maps baked '{bakedName}' -> singleton row '{itemId}' " +
                                 "but that row's repo.bakedTwins does not list it (StructureSingleton v2 reads ONLY the catalog)");
            }
            {
                bool barracksPinned = srcById.TryGetValue("barracks", out var barracksRow) &&
                                      barracksRow.repo != null && barracksRow.repo.bakedTwins != null &&
                                      System.Array.IndexOf(barracksRow.repo.bakedTwins, "CastleBarracks") >= 0;
                if (!barracksPinned)
                    failures.Add("[singletons] 'barracks'.repo.bakedTwins must contain 'CastleBarracks' " +
                                 "(the v1 SupplementalBaked row moved to data - losing it revives the two-barracks leak)");
            }

            // (d) seam asserts: CatalogRegistry.All() exists (EnforceAll depends on it),
            //     and BarracksNpcInjector no longer carries its bespoke baked-twin
            //     standdown (source-lint - reflection cannot see method bodies) but DOES
            //     subscribe the SingletonResolved reseat seam.
            var allMethod = typeof(CatalogRegistry).GetMethod("All", BindingFlags.Public | BindingFlags.Static);
            if (allMethod == null)
                failures.Add("[singletons] CatalogRegistry.All() is MISSING - StructureSingleton.EnforceAll sweeps it");
            string injPath = System.IO.Path.Combine(Application.dataPath, "_Modules/Village/NPCs/BarracksNpcInjector.cs");
            if (!System.IO.File.Exists(injPath))
            {
                failures.Add($"[singletons] BarracksNpcInjector.cs not found at '{injPath}' (seam lint skipped = FAIL)");
            }
            else
            {
                string injSrc = System.IO.File.ReadAllText(injPath);
                if (injSrc.Contains("SetActive(false)"))
                    failures.Add("[singletons] BarracksNpcInjector still contains a 'SetActive(false)' bespoke " +
                                 "baked-twin standdown - StructureSingleton.Enforce owns twin standdown in v2");
                if (!injSrc.Contains("SingletonResolved"))
                    failures.Add("[singletons] BarracksNpcInjector does not subscribe StructureSingleton.SingletonResolved - " +
                                 "the placed-barracks reseat seam is missing");
            }

            log.AppendLine(failures.Count == before
                ? "  SINGLETON_TWINS_OK"
                : $"  SINGLETON_TWINS_FAIL ({failures.Count - before} failure(s))");
        }

        // =====================================================================
        //  BLANK-TOWN BAKED-TWIN GATE - WO-834 (owner F8 seq 592): a baked twin
        //  may only surface for an id the player has EVER built (or while the
        //  WO-673 marker is unset - the bake still owns the town). Pins the pure
        //  rule, the v35->v36 seed, and the gate's presence at every surfacing seam.
        // =====================================================================
        private static void CheckBlankTownGate(List<string> failures, StringBuilder log)
        {
            int before = failures.Count;
            log.AppendLine("[blankTown] WO-834 baked-twin surface gate:");

            // (a) The pure rule's truth table (StructureSingleton.MayBakedTwinSurface).
            var none = new List<string>();
            var farmOnly = new List<string> { "collector_farm" };
            if (StructureSingleton.MayBakedTwinSurface("collector_farm", none, true))
                failures.Add("[blankTown] migrated save + empty everBuilt must SUPPRESS the baked twin (the seq-592 fix)");
            if (StructureSingleton.MayBakedTwinSurface("collector_farm", null, true))
                failures.Add("[blankTown] migrated save + NULL everBuilt must suppress (null-tolerant blank town)");
            if (!StructureSingleton.MayBakedTwinSurface("collector_farm", none, false))
                failures.Add("[blankTown] marker-false save must SURFACE (legacy pre-migration + Default-Town founding load)");
            if (!StructureSingleton.MayBakedTwinSurface("collector_farm", farmOnly, true))
                failures.Add("[blankTown] ever-built id must SURFACE on a migrated save (WO-819 sell-resurface leg)");
            if (!StructureSingleton.MayBakedTwinSurface("COLLECTOR_FARM", farmOnly, true))
                failures.Add("[blankTown] ever-built compare must be OrdinalIgnoreCase (catalog-id convention)");

            // (b) The v35->v36 migrator seed (SaveMigrator.MigrateToV36 via the chain).
            //     BLANK save (the owner's seq-592 shape: empty layout, no freebies burned)
            //     must seed an EMPTY list - present-but-empty is the fix.
            var blank = new SaveSchema.PersistedState
            {
                BaseLayout = new List<PlacedStructureData>(),
                FreeBuildsUsed = new List<string>(),
            };
            blank = SaveMigrator.Migrate(blank, 35);
            if (blank.EverBuiltStructureIds == null)
                failures.Add("[blankTown] MigrateToV36 left everBuiltStructureIds NULL on a blank save (must seed empty list)");
            else if (blank.EverBuiltStructureIds.Count != 0)
                failures.Add($"[blankTown] MigrateToV36 seeded {blank.EverBuiltStructureIds.Count} id(s) on a BLANK save (must be 0 - blank founding stays blank)");

            //     ESTABLISHED save: BaseLayout + FreeBuildsUsed union + the frozen
            //     default-town template grant (incl. 'barracks' - the WO-724 unlock right).
            var estab = new SaveSchema.PersistedState
            {
                BaseLayout = new List<PlacedStructureData>
                {
                    new PlacedStructureData("collector_farm", 1, 1, 0, level: 1,
                        yawOffset: 0f, worldY: 0f, wallMounted: false),
                },
                FreeBuildsUsed = new List<string> { "workshop" },
            };
            estab = SaveMigrator.Migrate(estab, 35);
            foreach (var id in new[] { "collector_farm", "workshop", "barracks", "pet-house" })
                if (estab.EverBuiltStructureIds == null || !estab.EverBuiltStructureIds.Contains(id))
                    failures.Add($"[blankTown] MigrateToV36 established-save seed missing '{id}' (existing towns must keep today's baked twins)");

            //     SOLD-SINGLETON save: empty layout but a burned freebie - the id must
            //     survive (WO-819 sell-resurface) WITHOUT dragging the template grant in.
            var sold = new SaveSchema.PersistedState
            {
                BaseLayout = new List<PlacedStructureData>(),
                FreeBuildsUsed = new List<string> { "pet-house" },
            };
            sold = SaveMigrator.Migrate(sold, 35);
            if (sold.EverBuiltStructureIds == null || !sold.EverBuiltStructureIds.Contains("pet-house"))
                failures.Add("[blankTown] MigrateToV36 dropped a FreeBuildsUsed id (placed-then-sold twin would stop resurfacing)");
            else if (sold.EverBuiltStructureIds.Contains("barracks"))
                failures.Add("[blankTown] MigrateToV36 granted the template to an EMPTY-BaseLayout save (blank founding must stay blank)");

            // (c) Source-lint: every surfacing seam carries the gate (reflection cannot
            //     see method bodies - same contract as the CheckSingletons seam lint).
            void Lint(string relPath, string needle, string why)
            {
                string path = System.IO.Path.Combine(Application.dataPath, relPath);
                if (!System.IO.File.Exists(path))
                {
                    failures.Add($"[blankTown] {relPath} not found (gate lint skipped = FAIL)");
                    return;
                }
                if (!System.IO.File.ReadAllText(path).Contains(needle))
                    failures.Add($"[blankTown] {relPath} no longer references '{needle}' - {why}");
            }
            Lint("_Modules/Village/BuildMode/StructureSingleton.cs", "MayBakedTwinSurface",
                "the Enforce resurface branch must be gated (WO-834)");
            Lint("_Modules/Village/BuildMode/StrategicPlacementMigration.cs", "MayBakedTwinSurface",
                "StanddownActiveForBaked must stand never-built bakes down at scene load");
            Lint("_Modules/Village/BuildMode/StrategicPlacementMigration.cs", "MarkEverBuilt",
                "the migration writer must grant the default-town template ids");
            Lint("_Modules/Village/NPCs/CastleVendorNpcInjector.cs", "MayBakedTwinSurface",
                "the Lever-1 baked-anchor fallback must not resurface/staff a suppressed store");
            Lint("_Modules/Village/NPCs/BarracksNpcInjector.cs", "MayBakedTwinSurface",
                "the WO-724 unlock poll must not resurface the barracks on a blank town");
            Lint("_Modules/Village/HubStructureVisualInjector.cs", "MayBakedTwinSurface",
                "EnsureBarracksSurfaced must respect the blank-town gate");
            Lint("_Modules/Village/BuildMode/BuildModeController.cs", "MarkEverBuilt",
                "the placement commit seam must grow the ever-built ledger");
            Lint("_Modules/Core/State/GameStateService.cs", "SeedBlankFoundingOnMissingSave",
                "a missing save must seed blank founding (WO-1250: else the bake grants Weaponsmith+Armorer)");

            // (d) WO-1250 — Weaponsmith (catalog id "forge") and Armorer (catalog id
            //     "armorer") must NOT be owned or surfaceable on a brand-new save, and
            //     the bake hosts that wear their art must map to those ids (not the
            //     retired workshop/forge crossing). If the catalog rows cannot be
            //     resolved this MUST NOT read green — PartialSkip, never a quiet pass.
            CheckBlankTownWeaponsmithArmorer(failures, log);

            log.AppendLine(failures.Count == before
                ? "  BLANK_TOWN_GATE_OK"
                : $"  BLANK_TOWN_GATE_FAIL ({failures.Count - before} failure(s))");
        }

        /// <summary>
        /// WO-1250: a brand-new save owns the empty founding set (tree/well/walls in
        /// the scene bake; zero BaseLayout; zero everBuilt). Weaponsmith and Armorer
        /// are player-placed, never granted. The RED shape: if either id is in the
        /// new-game ledger, or if the bake hosts still map to the retired ids, this
        /// fails — that is the bug (those two remaining standing on a new load).
        /// </summary>
        private static void CheckBlankTownWeaponsmithArmorer(List<string> failures, StringBuilder log)
        {
            const string WeaponsmithId = "forge";
            const string ArmorerId = "armorer";
            const string WeaponsmithHost = "Blacksmith_Weapons_Storefront";
            const string ArmorerHost = "Forge_Armor_Storefront";

            // Founding owned set: a brand-new save (ResetToNewGame / missing-save seed)
            // is migrated + empty everBuilt. If forge or armorer were seeded there,
            // MayBakedTwinSurface would OPEN and those two would remain standing.
            // These asserts do not need the catalog — they pin the pure gate.
            var empty = new List<string>();
            if (StructureSingleton.MayBakedTwinSurface(WeaponsmithId, empty, true))
                failures.Add("[blankTown] WO-1250 RED: a brand-new save surfaces 'forge' (Weaponsmith) — " +
                             "that is the bug (weaponsmith already built on a new load)");
            if (StructureSingleton.MayBakedTwinSurface(ArmorerId, empty, true))
                failures.Add("[blankTown] WO-1250 RED: a brand-new save surfaces 'armorer' (Armorer) — " +
                             "that is the bug (armorer already built on a new load)");

            // Prove the assertion is live: seeding those ids into everBuilt WOULD fail
            // the surface check (WO-1138: a hollow gate that cannot go red is worse than none).
            var wouldStand = new List<string> { WeaponsmithId, ArmorerId };
            if (!StructureSingleton.MayBakedTwinSurface(WeaponsmithId, wouldStand, true) ||
                !StructureSingleton.MayBakedTwinSurface(ArmorerId, wouldStand, true))
                failures.Add("[blankTown] WO-1250: MayBakedTwinSurface does not honour forge/armorer in everBuilt — " +
                             "the RED proof (adding them to the founding set) is dead");

            string srcPath = System.IO.Path.Combine(Application.dataPath,
                "StreamingAssets/Data/Canonical/structures-catalog.json");
            StructuresCatalogFile src = null;
            var srcById = new Dictionary<string, CatalogEntry>();
            if (!System.IO.File.Exists(srcPath))
            {
                log.AppendLine("  " + DeNelle.Editor.Regression.RegressionOutcome.PartialSkip(
                    "[blankTown] WO-1250 weaponsmith/armorer founding set",
                    "structures-catalog.json missing — cannot pin bake hosts or the founding set"));
            }
            else
            {
                try
                {
                    src = JsonConvert.DeserializeObject<StructuresCatalogFile>(
                        System.IO.File.ReadAllText(srcPath),
                        new JsonSerializerSettings
                        {
                            Converters = { new StringEnumConverter() },
                            NullValueHandling = NullValueHandling.Ignore,
                            MissingMemberHandling = MissingMemberHandling.Ignore,
                        });
                }
                catch (System.Exception ex)
                {
                    log.AppendLine("  " + DeNelle.Editor.Regression.RegressionOutcome.PartialSkip(
                        "[blankTown] WO-1250 weaponsmith/armorer founding set",
                        "structures-catalog.json failed to parse (" + ex.Message + ") — cannot pin bake hosts"));
                }
            }
            if (src != null && src.Entries != null)
                foreach (var e in src.Entries)
                    if (e != null && !string.IsNullOrEmpty(e.id)) srcById[e.id] = e;

            if (srcById.Count == 0)
            {
                // Parse failed or file empty: the PartialSkip above already named it.
                // Do NOT continue into mapping asserts that would invent a pass.
                return;
            }

            bool HasTwin(string id, string host)
            {
                if (!srcById.TryGetValue(id, out var row) || row.repo == null) return false;
                return row.repo.bakedTwins != null && System.Array.IndexOf(row.repo.bakedTwins, host) >= 0;
            }

            if (!srcById.ContainsKey(WeaponsmithId))
            {
                log.AppendLine("  " + DeNelle.Editor.Regression.RegressionOutcome.PartialSkip(
                    "[blankTown] WO-1250 weaponsmith row",
                    "catalog has no 'forge' row — cannot pin the Weaponsmith bake host"));
            }
            else if (!HasTwin(WeaponsmithId, WeaponsmithHost))
                failures.Add("[blankTown] WO-1250: 'forge' (Weaponsmith) must author baked twin '" +
                             WeaponsmithHost + "' — the bake host wearing Structures/Forge. " +
                             "A missing/wrong twin is the owner's 'weaponsmith already built' bug");

            if (!srcById.ContainsKey(ArmorerId))
            {
                log.AppendLine("  " + DeNelle.Editor.Regression.RegressionOutcome.PartialSkip(
                    "[blankTown] WO-1250 armorer row",
                    "catalog has no 'armorer' row — cannot pin the Armorer bake host"));
            }
            else if (!HasTwin(ArmorerId, ArmorerHost))
                failures.Add("[blankTown] WO-1250: 'armorer' (Armorer) must author baked twin '" +
                             ArmorerHost + "' — the bake host wearing Structures/armorer. " +
                             "A missing/wrong twin is the owner's 'armorer already built' bug");

            // Census mapping (the standdown allow-list). RED if the retired
            // workshop→Blacksmith / forge→Forge_Armor crossing is restored.
            bool sawWeaponsmithHost = false, sawArmorerHost = false;
            foreach (var (bakedName, itemId) in StrategicPlacementMigration.BakedStorefronts())
            {
                if (bakedName == WeaponsmithHost)
                {
                    sawWeaponsmithHost = true;
                    if (itemId != WeaponsmithId)
                        failures.Add($"[blankTown] WO-1250: bake host '{WeaponsmithHost}' maps to '{itemId}' " +
                                     $"(must be '{WeaponsmithId}' = Weaponsmith). Retired mapping workshop/forge " +
                                     "is what left those two standing on a new load");
                }
                if (bakedName == ArmorerHost)
                {
                    sawArmorerHost = true;
                    if (itemId != ArmorerId)
                        failures.Add($"[blankTown] WO-1250: bake host '{ArmorerHost}' maps to '{itemId}' " +
                                     $"(must be '{ArmorerId}' = Armorer). Retired mapping is the pre-built Armorer");
                }
            }
            if (!sawWeaponsmithHost)
                failures.Add($"[blankTown] WO-1250: BakedRows no longer lists '{WeaponsmithHost}' — " +
                             "standdown cannot cover the Weaponsmith visual");
            if (!sawArmorerHost)
                failures.Add($"[blankTown] WO-1250: BakedRows no longer lists '{ArmorerHost}' — " +
                             "standdown cannot cover the Armorer visual");

            log.AppendLine("  WO-1250: brand-new save does not own/surface forge (Weaponsmith) or armorer; " +
                           "bake hosts map to those ids (not the retired workshop/forge crossing).");
        }

        // =====================================================================
        //  NPC MODEL BINDING - WO-818 (owner mapping table 2026-08-01): mapped
        //  structure rows author repo.npcModel (a KayKit slug) that the Village
        //  NPC injectors resolve as Resources/NPCs/KayKit/<slug>. The DATA must
        //  hold: (a) dual-copy field parity; (b) every authored slug resolves to
        //  a staged FBX; (c) the 12 owner-approved rows carry the owner's slugs
        //  VERBATIM (creative pick is owner-only - drift here is a code pick).
        // =====================================================================
        private static void CheckNpcModels(List<string> failures, StringBuilder log)
        {
            int before = failures.Count;
            log.AppendLine("[npcModel] repo.npcModel -> KayKit body binding (WO-818):");

            var settings = new JsonSerializerSettings
            {
                Converters = { new StringEnumConverter() },
                NullValueHandling = NullValueHandling.Ignore,
                MissingMemberHandling = MissingMemberHandling.Ignore,
            };

            StructuresCatalogFile Load(string path, string label)
            {
                if (!System.IO.File.Exists(path))
                {
                    failures.Add($"[npcModel] {label} catalog copy MISSING at '{path}'");
                    return null;
                }
                try
                {
                    return JsonConvert.DeserializeObject<StructuresCatalogFile>(
                        System.IO.File.ReadAllText(path), settings);
                }
                catch (System.Exception ex)
                {
                    failures.Add($"[npcModel] {label} catalog copy failed to parse: {ex.Message}");
                    return null;
                }
            }

            // Raw file reads on purpose (same contract as CheckSingletons): the dual-copy
            // guarantee is between the two COMMITTED files.
            string srcPath = System.IO.Path.Combine(Application.dataPath, "StreamingAssets/Data/Canonical/structures-catalog.json");
            string resPath = System.IO.Path.Combine(Application.dataPath, "Resources/Data/Canonical/structures-catalog.json");
            var src = Load(srcPath, "StreamingAssets");
            var res = Load(resPath, "Resources");
            if (src == null || res == null || src.Entries == null || res.Entries == null) return;

            var resById = new Dictionary<string, CatalogEntry>();
            foreach (var e in res.Entries)
                if (e != null && !string.IsNullOrEmpty(e.id)) resById[e.id] = e;

            // (a) dual-copy parity + (b) every authored slug resolves to a staged FBX
            //     under the TRACKED Resources/NPCs/KayKit (a typo'd slug would warn +
            //     People-fallback on every load - catch it at the gate instead).
            // PROD-002: npcModel may now be FOLDER-QUALIFIED ("CraftPixPeople/NPC_Peasant_1") or a
            // bare legacy slug ("Ranger" -> the KayKit stage). This check MIRRORS
            // KayKitNpcBody.Load's rule deliberately: if the gate resolved paths differently from
            // the runtime, it would pass on bodies the game cannot load, which is worse than having
            // no gate. It also accepts .prefab OR .fbx — KayKit bodies are staged as raw FBXs,
            // the purchased CraftPix bodies are built prefabs, and both are legitimate answers to
            // "does Resources.Load<GameObject> find a body here".
            string npcRootDir = System.IO.Path.Combine(Application.dataPath, "Resources/NPCs");
            int authored = 0;
            var srcModelById = new Dictionary<string, string>();
            foreach (var e in src.Entries)
            {
                if (e == null || string.IsNullOrEmpty(e.id)) continue;
                string a = (e.repo != null) ? e.repo.npcModel : null;
                srcModelById[e.id] = a;
                string b = (resById.TryGetValue(e.id, out var r) && r.repo != null) ? r.repo.npcModel : null;
                if ((a ?? "") != (b ?? ""))
                    failures.Add($"[npcModel] '{e.id}' repo.npcModel differs between copies " +
                                 $"(StreamingAssets='{a ?? "<null>"}', Resources='{b ?? "<null>"}')");
                if (string.IsNullOrEmpty(a)) continue;
                authored++;
                // Same rule as KayKitNpcBody.Load: a '/' means the slug names its own pack folder.
                string rel  = a.Contains("/") ? a : "KayKit/" + a;
                string stem = System.IO.Path.Combine(npcRootDir, rel.Replace('/', System.IO.Path.DirectorySeparatorChar));
                string found = null;
                foreach (var ext in new[] { ".prefab", ".fbx" })
                    if (System.IO.File.Exists(stem + ext)) { found = stem + ext; break; }

                if (found == null)
                    failures.Add($"[npcModel] '{e.id}' npcModel '{a}' has NO body at '{stem}.prefab' or '{stem}.fbx' " +
                                 "(the injector would warn + fall back to the People chain every load)");
                else
                    log.AppendLine($"  NM {e.id} -> '{a}' | body OK ({System.IO.Path.GetExtension(found)})");
            }

            // (c) the 12 owner-approved rows (WO-818 mapping table, VERBATIM - a change
            //     here is an owner retag applied to BOTH this table and the catalog,
            //     never a code-side pick).
            // PROD-002 (2026-08-17) — RETAGGED FROM KAYKIT PLACEHOLDERS TO THE OWNER-PURCHASED
            // CraftPix bodies. Owner: "can we replace the placeholder kay kat with the people i
            // purchased?" and, on the casting itself, "so you can pick". The picks below read
            // STATUS against what each post sells or does, which is why they are legible rather
            // than arbitrary: the two skilled trades that sell gear are CityDwellers, the
            // high-value/high-status posts are RichCitizens, and everyone working the land or a
            // production building is a Peasant.
            //   ⛔ NPC_King and NPC_Queen are DELIBERATELY UNCAST. They are the two most
            //      distinctive bodies in the set and would read as absurd behind a shop counter;
            //      holding them back keeps royalty available for a throne-room or quest beat
            //      instead of spending it on a vendor.
            // 12 rows, 12 non-royal bodies, strict 1:1 — no body appears twice, which matters
            // because a duplicated body reads to the player as the same person working two jobs.
            // This table stays VERBATIM-pinned: a change is a retag applied to BOTH this table and
            // the catalog, never a code-side pick.
            var expected = new Dictionary<string, string>
            {
                { "barracks",             "CraftPixPeople/NPC_RichCitizen_4" },  // officer
                { "workshop",             "CraftPixPeople/NPC_RichCitizen_2" },  // master artisan
                { "forge",                "CraftPixPeople/NPC_CityDweller_1" },  // SELLS WEAPONS
                { "armorer",              "CraftPixPeople/NPC_CityDweller_2" },  // SELLS ARMOUR
                { "jeweler",              "CraftPixPeople/NPC_RichCitizen_1" },  // rings/gems - highest value
                { "market",               "CraftPixPeople/NPC_Peasant_1" },      // Coppin, produce
                { "arcane-tower",         "CraftPixPeople/NPC_RichCitizen_3" },  // scholar
                { "pet-house",            "CraftPixPeople/NPC_Peasant_6" },      // Echo keeper
                { "collector_farm",       "CraftPixPeople/NPC_Peasant_3" },
                { "mill",                 "CraftPixPeople/NPC_Peasant_2" },
                { "collector_lumbermill", "CraftPixPeople/NPC_Peasant_4" },
                { "healing_caravan",      "CraftPixPeople/NPC_Peasant_5" },
            };
            foreach (var kv in expected)
            {
                if (!srcModelById.TryGetValue(kv.Key, out var actual))
                    failures.Add($"[npcModel] mapped structure row '{kv.Key}' is MISSING from the catalog");
                else if (actual != kv.Value)
                    failures.Add($"[npcModel] '{kv.Key}' npcModel '{actual ?? "<null>"}' != owner-approved '{kv.Value}' (WO-818 table)");
            }
            if (authored != expected.Count)
                failures.Add($"[npcModel] {authored} row(s) author npcModel but the WO-818 owner table maps exactly {expected.Count} " +
                             "- an extra/missing binding is not an owner-approved pick");

            // (d) WO-833: the shared idle controller KayKitNpcBody.ArmIdle arms on a body that
            //     arrives with NO controller must EXIST under Resources and reference >=1 clip -
            //     an empty/missing controller renders the FBX bind pose (owner F8 "NPC Stuck in
            //     T Pose"). Catch it at the gate, not the felt-test.
            //     ⚠ SCOPE NARROWED BY PROD-002, and the check is kept anyway. It no longer covers
            //     "all 12 structure NPCs": those now resolve to CraftPix prefabs that ship
            //     AC_CraftPixTownsfolk already bound, and ArmController leaves a bound controller
            //     alone rather than overwriting it. This controller still drives every KayKit body
            //     that remains live (the hero body-swap set, the construction worker), so deleting
            //     the check would drop cover on a T-pose bug that is still reachable - it simply
            //     protects fewer NPCs than the original comment claimed.
            const string idleCtrlPath = "Assets/Resources/NPCs/KayKit/KayKitNpcIdle.controller";
            var idleCtrl = UnityEditor.AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(idleCtrlPath);
            if (idleCtrl == null)
                failures.Add($"[npcModel] WO-833 idle controller MISSING at '{idleCtrlPath}' - staged KayKit NPCs " +
                             "would T-pose; run Defenders/Art/Build KayKit NPC Idle Controller " +
                             "(DeNelle.Editor.KayKitNpcAnimatorSetup.Build)");
            else
            {
                int clipCount = 0;
                string firstClip = null;
                var clips = idleCtrl.animationClips;
                if (clips != null)
                    foreach (var c in clips)
                        if (c != null) { clipCount++; if (firstClip == null) firstClip = c.name; }
                if (clipCount == 0)
                    failures.Add($"[npcModel] WO-833 idle controller at '{idleCtrlPath}' references NO animation clip - " +
                                 "the Idle state is motion-less, NPCs would still T-pose (rebuild via KayKitNpcAnimatorSetup.Build)");
                else
                    log.AppendLine($"  NM idle controller OK ({clipCount} clip(s), first '{firstClip}')");
            }

            log.AppendLine(failures.Count == before
                ? $"  NPC_MODELS_OK ({authored} bound row(s))"
                : $"  NPC_MODELS_FAIL ({failures.Count - before} failure(s))");
        }

        // =====================================================================
        //  BUILDINGS — buildings.json via BuildingCatalog (the real loader)
        // =====================================================================
        private static void CheckBuildings(List<string> failures, StringBuilder log)
        {
            BuildingCatalog.Reload();
            var buildings = new List<BuildingDef>(BuildingCatalog.Buildings);

            log.AppendLine($"buildings.json -> {buildings.Count} BuildingDef object(s)");
            if (buildings.Count == 0)
                failures.Add("buildings.json deserialized to 0 BuildingDef objects (mapping break or empty 'buildings')");

            int bad = 0;
            foreach (var b in buildings)
            {
                // displayName is a canon-strings KEY (not a literal) but must be non-empty
                // so the build menu can resolve a name; id is the build/cooldown key.
                bool ok = b != null && !string.IsNullOrEmpty(b.Id) && !string.IsNullOrEmpty(b.DisplayName);
                if (!ok) bad++;
                log.AppendLine($"  BD {(b != null ? b.Id : "<null>")} | displayName='{(b != null ? b.DisplayName : "<null>")}' " +
                               $"| model='{(b != null ? b.Model : "<null>")}' (mesh key, not a Resources path)");
            }
            if (bad > 0)
                failures.Add($"{bad} building(s) have null/empty id or displayName");
        }

        // =====================================================================
        //  WO-771.9 — BARRACKS & TROOP UPGRADE PROGRESSION source-lint
        //  Emits BARRACKS_PROGRESSION_OK when the ladder + curves + reconcile pass.
        // =====================================================================
        private static void CheckBarracksProgression(List<string> failures, StringBuilder log)
        {
            int before = failures.Count;

            BarracksCatalog.Reload();
            TroopUpgradeCatalog.Reload();
            TroopCatalog.Reload();

            var levels = new List<BarracksDef>(BarracksCatalog.All);
            var troops = new List<TroopDef>(TroopCatalog.All);
            var upgrades = new List<TroopUpgradeDef>(TroopUpgradeCatalog.All);

            log.AppendLine($"barracks.json -> {levels.Count} level(s); troop-upgrades.json -> {upgrades.Count} row(s)");

            if (levels.Count == 0) failures.Add("barracks.json deserialized to 0 levels (mapping break or empty 'levels')");
            if (troops.Count == 0) failures.Add("troops.json deserialized to 0 troops (barracks progression needs the roster)");

            // Level 1 must be the free day-one baseline; the ladder must be contiguous 1..Max.
            int max = BarracksCatalog.MaxLevel;
            for (int lvl = 1; lvl <= max; lvl++)
            {
                var def = BarracksCatalog.Find(lvl);
                if (def == null) { failures.Add($"barracks.json is missing level {lvl} (ladder must be contiguous 1..{max})"); continue; }
                if (lvl == 1 && !def.Cost.IsZero)
                    failures.Add("barracks.json level 1 must be FREE (zero cost) — it is the day-one baseline");
                if (lvl == 1 && def.BuildTimeSeconds != 0f)
                    failures.Add("barracks.json level 1 must have zero build time (day-one baseline)");

                // Every unlocked troop id must resolve AND its UnlockBarracksTier must == this level (reconcile).
                if (def.UnlocksTroopIds != null)
                {
                    foreach (var id in def.UnlocksTroopIds)
                    {
                        var t = TroopCatalog.Find(id);
                        if (t == null) { failures.Add($"barracks.json level {lvl} unlocks unknown troop id '{id}'"); continue; }
                        if (t.UnlockBarracksTier != lvl)
                            failures.Add($"reconcile mismatch: '{id}' listed at barracks level {lvl} but troops.json UnlockBarracksTier={t.UnlockBarracksTier}");
                    }
                }
            }

            // Every troop should be unlocked by exactly the barracks level == its UnlockBarracksTier.
            foreach (var t in troops)
            {
                if (t == null) continue;
                bool unlockedAtTier = BarracksProgression.IsTroopUnlocked(t.Id, t.UnlockBarracksTier);
                bool lockedBelow = t.UnlockBarracksTier <= 1 || !BarracksProgression.IsTroopUnlocked(t.Id, t.UnlockBarracksTier - 1);
                if (!unlockedAtTier) failures.Add($"troop '{t.Id}' is NOT unlocked at its own UnlockBarracksTier {t.UnlockBarracksTier}");
                if (!lockedBelow) failures.Add($"troop '{t.Id}' is unlocked BELOW its UnlockBarracksTier {t.UnlockBarracksTier} (gate leak)");
            }

            // Every upgrade row: troop resolves; curves present + start at the 1.0 baseline.
            int abilitiesUnresolved = 0;
            foreach (var upg in upgrades)
            {
                if (upg == null) continue;
                if (TroopCatalog.Find(upg.TroopId) == null)
                    failures.Add($"troop-upgrades.json row '{upg.TroopId}' has no matching troop in troops.json");

                CheckCurveBaseline(failures, upg.TroopId, "reach", upg.Reach);
                CheckCurveBaseline(failures, upg.TroopId, "strength", upg.Strength);

                if (upg.SpecialAbilities != null)
                {
                    foreach (var a in upg.SpecialAbilities)
                    {
                        if (a == null) continue;
                        if (a.LevelThreshold < 1)
                            failures.Add($"'{upg.TroopId}' ability '{a.AbilityId}' has a non-positive levelThreshold");
                        // Ability id resolution is INFORMATIONAL only (abilities.json population is WO-771.14) — log, don't fail.
                        if (string.IsNullOrEmpty(a.AbilityId) || AbilityCatalog.FindById(a.AbilityId) == null)
                            abilitiesUnresolved++;
                    }
                }
            }
            if (abilitiesUnresolved > 0)
                log.AppendLine($"[barracks] note: {abilitiesUnresolved} upgrade ability id(s) not yet in abilities.json (informational; WO-771.14 owns ability wiring)");

            if (failures.Count == before)
                log.AppendLine("BARRACKS_PROGRESSION_OK");
            else
                log.AppendLine($"BARRACKS_PROGRESSION_FAIL: {failures.Count - before} issue(s)");
        }

        // A StatCurve must be authored and start at the 1.0 baseline (values[0] == 1.0).
        private static void CheckCurveBaseline(List<string> failures, string troopId, string which, StatCurve curve)
        {
            if (curve == null || curve.Values == null || curve.Values.Length == 0)
            {
                failures.Add($"'{troopId}' {which} curve is empty (must define per-level multipliers)");
                return;
            }
            if (System.Math.Abs(curve.Values[0] - 1.0f) > 0.001f)
                failures.Add($"'{troopId}' {which} curve must start at 1.0 baseline (values[0]={curve.Values[0]:0.###})");
        }

        // WO-588: validate the Game Guide codex content (guide-content.json).
        // =====================================================================
        //  DIALOGUE SPEAKER CARDS — name + affiliation + portrait all resolve
        // =====================================================================
        private static void CheckDialogueSpeakers(List<string> failures, StringBuilder log)
        {
            DeNelle.Core.Dialogue.DialogueCatalog.Reload();
            var dialogues = DeNelle.Core.Dialogue.DialogueCatalog.Dialogues;
            var speakers  = DeNelle.Core.Dialogue.DialogueCatalog.Speakers;
            log.AppendLine($"dialogues.json -> {dialogues.Count} DialogueDef, {speakers.Count} speaker record(s)");

            if (dialogues.Count == 0) { failures.Add("dialogues.json deserialized to 0 dialogues (mapping break)"); return; }
            if (speakers.Count == 0)  { failures.Add("dialogues.json has no 'speakers' block (card standard: name+affiliation+portrait per speaker)"); return; }

            // 1) Every speaker record carries a name + affiliation; a DECLARED portrait path loads.
            foreach (var s in speakers)
            {
                if (s == null || string.IsNullOrEmpty(s.Name))
                { failures.Add("speakers block contains a record with null/empty name"); continue; }
                if (string.IsNullOrEmpty(s.Affiliation))
                    failures.Add($"speaker '{s.Name}' has no affiliation (card standard requires guild/shop affiliation)");
                string portraitState = "silhouette";
                if (!string.IsNullOrEmpty(s.Portrait))
                {
                    var sp = Resources.Load<Sprite>(s.Portrait);
                    if (sp == null) failures.Add($"speaker '{s.Name}' declares portrait '{s.Portrait}' but Resources.Load<Sprite> returned null (dangling path)");
                    else portraitState = s.Portrait;
                }
                log.AppendLine($"  S {s.Name} | affiliation='{s.Affiliation}' | portrait={portraitState}");
            }

            // 2) Every spoken line's speaker resolves to a record (the card can always render
            //    name + affiliation); every legacy per-node `portrait` command arg loads.
            foreach (var d in dialogues)
            {
                if (d == null || d.Nodes == null) continue;
                foreach (var node in d.Nodes)
                {
                    if (node == null) continue;
                    if (node.Lines != null)
                        foreach (var line in node.Lines)
                        {
                            if (line == null || string.IsNullOrEmpty(line.Speaker)) continue; // narration is legal
                            if (DeNelle.Core.Dialogue.DialogueCatalog.FindSpeaker(line.Speaker) == null)
                                failures.Add($"dialogue '{d.Id}' node '{node.Id}': speaker '{line.Speaker}' has no speakers-block record (card cannot show affiliation)");
                        }
                    if (node.Commands != null)
                        foreach (var cmd in node.Commands)
                        {
                            if (cmd == null || cmd.Verb != "portrait") continue;
                            string path = (cmd.Args != null && cmd.Args.Count > 0) ? cmd.Args[0] : null;
                            if (string.IsNullOrEmpty(path) || Resources.Load<Sprite>(path) == null)
                                failures.Add($"dialogue '{d.Id}' node '{node.Id}': `portrait` command path '{path}' does not resolve a sprite");
                        }
                }
            }
        }

        private static void CheckGuideContent(List<string> failures, StringBuilder log)
        {
            GuideContentCatalog.Reload();
            var sections = new List<GuideSection>(GuideContentCatalog.Sections);

            log.AppendLine($"guide-content.json -> {sections.Count} GuideSection object(s)");
            if (sections.Count == 0)
            {
                failures.Add("guide-content.json deserialized to 0 sections (mapping break or empty 'sections')");
                return;
            }

            int badField = 0, emptyBody = 0;
            var seenIds = new HashSet<string>();
            foreach (var s in sections)
            {
                bool ok = s != null
                          && !string.IsNullOrEmpty(s.Id)
                          && !string.IsNullOrEmpty(s.Tab)
                          && !string.IsNullOrEmpty(s.Title);
                if (!ok) { badField++; continue; }

                if (!seenIds.Add(s.Id))
                    failures.Add($"guide-content.json: duplicate section id '{s.Id}'");

                // Body must have at least one non-empty paragraph (a blank body renders as an empty tab).
                bool hasBody = false;
                if (s.Body != null)
                    foreach (var p in s.Body)
                        if (!string.IsNullOrEmpty(p)) { hasBody = true; break; }
                if (!hasBody) emptyBody++;

                log.AppendLine($"  GG {s.Id} | tab='{s.Tab}' status='{s.Status}' " +
                               $"body={(s.Body != null ? s.Body.Count : 0)} tips={(s.Tips != null ? s.Tips.Count : 0)}");
            }
            if (badField > 0) failures.Add($"{badField} guide section(s) have null/empty id, tab, or title");
            if (emptyBody > 0) failures.Add($"{emptyBody} guide section(s) have an empty body (no non-empty paragraph)");
        }

        // WO-587: validate the Population milestone table that drives Echo slot unlocks.
        private static void CheckPopulationMilestones(List<string> failures, StringBuilder log)
        {
            PopulationMilestonesCatalog.Reload();
            var milestones = new List<PopulationMilestone>(PopulationMilestonesCatalog.Milestones);

            log.AppendLine($"population-milestones.json -> {milestones.Count} PopulationMilestone object(s)");
            if (milestones.Count == 0)
            {
                failures.Add("population-milestones.json deserialized to 0 milestones (mapping break or empty 'milestones')");
                return;
            }

            // Echo slots must ascend 2,3,4,... with NO gaps and a condition on every entry
            // (the catalog already sorts by EchoSlot ascending).
            int expected = 2;
            foreach (var m in milestones)
            {
                if (m == null) { failures.Add("population-milestones.json: a null milestone entry"); continue; }

                if (m.EchoSlot != expected)
                    failures.Add($"population-milestones.json: echoSlot {m.EchoSlot} out of order/gapped (expected {expected}; slots must ascend 2..5 with no gaps)");

                if (!m.HasAnyCondition)
                    failures.Add($"population-milestones.json: echoSlot {m.EchoSlot} has NO condition (needs at least one 'any' or 'all' threshold)");

                log.AppendLine($"  PM slot={m.EchoSlot} " +
                               $"any=[{CondStr(m.Any)}] all=[{CondStr(m.All)}]");
                expected++;
            }

            // Slots should reach the design max of 5 (3 organic + 2 flex).
            if (milestones[milestones.Count - 1].EchoSlot != 5)
                failures.Add($"population-milestones.json: top echoSlot is {milestones[milestones.Count - 1].EchoSlot}, expected 5 (3 organic + 2 flex cap)");
        }

        private static string CondStr(MilestoneCondition c)
        {
            if (c == null || c.IsEmpty) return "-";
            var parts = new List<string>();
            if (c.Xp > 0) parts.Add($"xp>={c.Xp}");
            if (c.QuestsCompleted > 0) parts.Add($"quests>={c.QuestsCompleted}");
            if (c.OutpostsCleared > 0) parts.Add($"outposts>={c.OutpostsCleared}");
            if (c.WavesCleared > 0) parts.Add($"waves>={c.WavesCleared}");
            if (c.VillageLevel > 0) parts.Add($"village>={c.VillageLevel}");
            return string.Join(",", parts);
        }

        // =====================================================================
        //  ITEM-MODEL CAPABILITIES — WO-Item-1 invariants (docs/ITEM_MODEL.md §2c)
        // -----------------------------------------------------------------------
        //  HARD (fail REGRESSION_FAIL when violated):
        //   - every Weapon entry resolves Carriable|Equippable
        //   - every Armor/Gear entry resolves Carriable|Equippable
        //   - every Consumable entry resolves Carriable|Usable
        //   - NO entry resolves both Carriable and AI (an item is never an enemy)
        //  SOFT (report a coverage count, do NOT fail — WO-Item-2's generator fills
        //  prefabPath; failing now would block the additive foundation):
        //   - how many Carriable entries resolve a non-null prefabPath
        // =====================================================================
        private static void CheckItemCapabilities(
            List<WeaponDef> weapons, List<ArmorDef> armors,
            List<string> failures, StringBuilder log)
        {
            const ItemCapability EQUIP = ItemCapability.Carriable | ItemCapability.Equippable;
            const ItemCapability USE   = ItemCapability.Carriable | ItemCapability.Usable;

            // Load consumables through the same real loader the game uses.
            ConsumableCatalog.Reload();
            var consumables = new List<ConsumableDef>(ConsumableCatalog.All);
            log.AppendLine($"consumables.json -> {consumables.Count} ConsumableDef object(s)");

            int carriableTotal = 0;     // SOFT denominator
            int prefabResolved = 0;     // SOFT numerator (prefabPath non-null on a Carriable)

            // --- Weapons: must resolve Carriable|Equippable, never AI ---
            foreach (var w in weapons)
            {
                if (w == null) continue;
                var cap = w.Capabilities;
                if ((cap & EQUIP) != EQUIP)
                    failures.Add($"weapons.json: '{w.id}' resolves {cap} — must retain Carriable|Equippable");
                if ((cap & ItemCapability.Carriable) != 0 && (cap & ItemCapability.AI) != 0)
                    failures.Add($"weapons.json: '{w.id}' resolves BOTH Carriable and AI (an item is never an enemy)");
                if ((cap & ItemCapability.Carriable) != 0)
                {
                    carriableTotal++;
                    if (!string.IsNullOrEmpty(w.prefabPath)) prefabResolved++;
                }
            }

            // --- Armor/Gear: must resolve Carriable|Equippable, never AI ---
            foreach (var a in armors)
            {
                if (a == null) continue;
                var cap = a.Capabilities;
                if ((cap & EQUIP) != EQUIP)
                    failures.Add($"armor.json: '{a.id}' resolves {cap} — must retain Carriable|Equippable");
                if ((cap & ItemCapability.Carriable) != 0 && (cap & ItemCapability.AI) != 0)
                    failures.Add($"armor.json: '{a.id}' resolves BOTH Carriable and AI (an item is never an enemy)");
                if ((cap & ItemCapability.Carriable) != 0)
                {
                    carriableTotal++;
                    if (!string.IsNullOrEmpty(a.prefabPath)) prefabResolved++;
                }
            }

            // --- Consumables: must resolve Carriable|Usable, never AI ---
            foreach (var c in consumables)
            {
                if (c == null) continue;
                var cap = c.Capabilities;
                if ((cap & USE) != USE)
                    failures.Add($"consumables.json: '{c.Id}' resolves {cap} — must retain Carriable|Usable");
                if ((cap & ItemCapability.Carriable) != 0 && (cap & ItemCapability.AI) != 0)
                    failures.Add($"consumables.json: '{c.Id}' resolves BOTH Carriable and AI (an item is never an enemy)");
                if ((cap & ItemCapability.Carriable) != 0)
                {
                    carriableTotal++;
                    if (!string.IsNullOrEmpty(c.PrefabPath)) prefabResolved++;
                }
            }

            // SOFT coverage line — WO-Item-2's generator populates prefabPath; until then
            // 0/N is EXPECTED and must NOT fail (the foundation is additive, no behavior change).
            log.AppendLine($"[item-model] capability invariants checked on " +
                           $"{weapons.Count}W + {armors.Count}A + {consumables.Count}C entries");
            log.AppendLine($"  [item-model] SOFT prefabPath coverage: {prefabResolved}/{carriableTotal} " +
                           $"Carriable entries resolve a non-null prefabPath (WO-Item-2 fills the rest)");
        }

        // =====================================================================
        //  CRAFTING CHAIN — drops -> craft -> inventory data smoke (WO Consumable-
        //  Crafting-V1 + overnight stretch). The runtime transaction reuses the
        //  already-shipping VillageInventory larder (proven elsewhere); this oracle
        //  guards the DATA chain so new content can never break craftability:
        //   HARD (fail REGRESSION):
        //    - every recipe Output resolves in the consumable catalog
        //    - every recipe ingredient resolves in the material catalog (legacy ids OK)
        //    - every art-backed (ing_*) ingredient is DROPPABLE in >=1 loot table
        //      (so the ingredient is obtainable -> the recipe is actually craftable)
        //    - every loot-table drop materialId resolves in the material catalog
        //      (no phantom drop) — legacy scaffolding ids excepted
        //   SOFT (log only): ing_* materials used by no recipe (orphan ingredient)
        // =====================================================================
        private static void CheckCraftingChain(List<string> failures, StringBuilder log)
        {
            // Documented legacy scaffolding ids: referenced by the 4 pre-existing
            // recipes + the default loot tables, intentionally have NO MaterialDef
            // (glyph fallback). Must not fail the gate.
            // WO-850 (2026-08-02): "ember-resin" REMOVED from this set - the owner ruled the
            // torch ingredients be promoted into materials.json so the larder can hold them, so
            // it now has a real MaterialDef and no longer needs the exception. Leaving it here
            // would have been a comment that lies ("intentionally have NO MaterialDef") and
            // would mask a genuine future regression if the row were ever deleted.
            var legacy = new HashSet<string>
            {
                "wild-herb", "rare-essence", "monster-hide", "tattered-cloth"
            };

            MaterialCatalog.Reload();
            ConsumableCatalog.Reload();
            ConsumableCraftingCatalog.Reload();
            LootTableCatalog.Reload();
            VendorRegistry.Reload();

            var materialIds = new HashSet<string>();
            foreach (var m in MaterialCatalog.All)
                if (m != null && !string.IsNullOrEmpty(m.Id)) materialIds.Add(m.Id);

            // Union of every materialId dropped by any loot table (obtainable set).
            var droppable = new HashSet<string>();
            foreach (var t in LootTableCatalog.All)
            {
                if (t == null || t.Drops == null) continue;
                foreach (var d in t.Drops)
                {
                    if (d == null || string.IsNullOrEmpty(d.MaterialId)) continue;
                    droppable.Add(d.MaterialId);
                    if (!materialIds.Contains(d.MaterialId) && !legacy.Contains(d.MaterialId))
                        failures.Add($"loot-tables.json: table '{t.Id}' drops phantom material '{d.MaterialId}' (no MaterialDef)");
                }
            }

            // Union of every material/consumable id any vendor actually SELLS, resolved
            // through the REAL shelf path (vendors.json -> VendorStockResolver — the WO-598
            // Market surfaces every non-gem priced material). Buy-only ingredients are a
            // legitimate acquisition mode; "obtainable" = droppable OR purchasable.
            var purchasable = new HashSet<string>();
            foreach (var v in VendorRegistry.All)
            {
                if (v == null || string.IsNullOrEmpty(v.Id)) continue;
                foreach (var ware in DeNelle.Village.Hero.VendorStockResolver.Resolve(v.Id, "knight", 99, new[] { "knight" }))
                    if (ware.Kind == DeNelle.Village.Hero.VendorWareKind.Material ||
                        ware.Kind == DeNelle.Village.Hero.VendorWareKind.Consumable)
                        purchasable.Add(ware.Id);
            }

            var recipes = ConsumableCraftingCatalog.All;
            var usedIngredients = new HashSet<string>();
            int chainOk = 0;
            foreach (var r in recipes)
            {
                if (r == null || string.IsNullOrEmpty(r.Id)) continue;

                if (string.IsNullOrEmpty(r.Output) || !ConsumableCatalog.IsConsumable(r.Output))
                    failures.Add($"consumable-recipes.json: '{r.Id}' output '{r.Output}' is not a known consumable");

                bool ingredientsOk = true;
                if (r.Ingredients != null)
                {
                    foreach (var ing in r.Ingredients)
                    {
                        if (ing == null || string.IsNullOrEmpty(ing.Id)) continue;
                        usedIngredients.Add(ing.Id);

                        bool isMat = materialIds.Contains(ing.Id);
                        if (!isMat && !legacy.Contains(ing.Id))
                        {
                            failures.Add($"consumable-recipes.json: '{r.Id}' needs unknown ingredient '{ing.Id}' (no MaterialDef)");
                            ingredientsOk = false;
                        }
                        // Art-backed ingredient must be obtainable — from a drop OR a vendor
                        // shelf — else the recipe is uncraftable in normal play. (Was drops-only;
                        // that false-failed the 5 Market buy-only herbs/liquids, WO-600 reclassed.)
                        if (isMat && ing.Id.StartsWith("ing_") && !droppable.Contains(ing.Id) && !purchasable.Contains(ing.Id))
                        {
                            failures.Add($"consumable-recipes.json: '{r.Id}' ingredient '{ing.Id}' has NO acquisition path (no loot drop AND no vendor shelf) (uncraftable)");
                            ingredientsOk = false;
                        }
                    }
                }
                if (ingredientsOk && ConsumableCatalog.IsConsumable(r.Output)) chainOk++;
            }

            // SOFT: art-backed materials that no recipe consumes (dead-end drops).
            int orphan = 0;
            foreach (var id in materialIds)
                if (id.StartsWith("ing_") && !usedIngredients.Contains(id)) orphan++;

            log.AppendLine($"[crafting] chain checked: {recipes.Count} recipe(s), {materialIds.Count} material(s), " +
                           $"{droppable.Count} droppable id(s); {chainOk} recipe(s) fully craftable drops->craft->consumable");
            if (orphan > 0)
                log.AppendLine($"  [crafting] SOFT: {orphan} ing_* material(s) used by no recipe (dead-end drop)");
        }

        // =====================================================================
        //  JEWELER JEWELRY-CRAFTING CHAIN (WO-553) — guards the jeweler-recipes.json
        //  data + the atomic JewelerCraftingService loop:
        //   HARD per recipe: (a) OutputAccessoryId resolves in GearCatalog.FindAccessory;
        //         (b) base.id resolves as an accessory; (c) every gem.id resolves in
        //         MaterialCatalog.
        //   SOFT (log only): a gem id that drops from NO loot table yet — gem boss-drops
        //         are owned by a SEPARATE agent (owner decision 2026-06-28); not a fail.
        //   HARD simulated craft (first iron/wood-only recipe — no GameState dependency):
        //         seed VillageInventory with base + gems + the wallet, call
        //         JewelerCraftingService.Craft, assert success + base/gems consumed +
        //         wallet debited + output granted (+1); then a no-funds craft returns
        //         false and consumes nothing (rollback).
        // =====================================================================
        private static void CheckJewelerChain(List<string> failures, StringBuilder log)
        {
            GearCatalog.Reload();
            MaterialCatalog.Reload();
            LootTableCatalog.Reload();
            DeNelle.Village.Crafting.JewelerRecipeCatalog.Reload();

            var materialIds = new HashSet<string>();
            foreach (var m in MaterialCatalog.All)
                if (m != null && !string.IsNullOrEmpty(m.Id)) materialIds.Add(m.Id);

            var droppable = new HashSet<string>();
            foreach (var t in LootTableCatalog.All)
            {
                if (t == null || t.Drops == null) continue;
                foreach (var d in t.Drops)
                    if (d != null && !string.IsNullOrEmpty(d.MaterialId)) droppable.Add(d.MaterialId);
            }

            var recipes = DeNelle.Village.Crafting.JewelerRecipeCatalog.All;
            var gemIds = new HashSet<string>();
            int chainOk = 0;
            DeNelle.Village.Crafting.JewelerRecipeDef simRecipe = null;

            foreach (var r in recipes)
            {
                if (r == null || string.IsNullOrEmpty(r.Id)) continue;

                bool ok = true;

                // (a) output resolves as a real accessory.
                if (string.IsNullOrEmpty(r.OutputAccessoryId) || GearCatalog.FindAccessory(r.OutputAccessoryId) == null)
                { failures.Add($"jeweler-recipes.json: '{r.Id}' output '{r.OutputAccessoryId}' is not a known accessory"); ok = false; }

                // (b) base resolves as a real accessory.
                if (r.Base == null || string.IsNullOrEmpty(r.Base.Id) || GearCatalog.FindAccessory(r.Base.Id) == null)
                { failures.Add($"jeweler-recipes.json: '{r.Id}' base '{r.Base?.Id}' is not a known accessory"); ok = false; }

                // (c) every gem resolves in MaterialCatalog; SOFT droppability.
                if (r.Gems != null)
                {
                    foreach (var g in r.Gems)
                    {
                        if (g == null || string.IsNullOrEmpty(g.Id)) continue;
                        gemIds.Add(g.Id);
                        if (!materialIds.Contains(g.Id))
                        { failures.Add($"jeweler-recipes.json: '{r.Id}' needs unknown gem '{g.Id}' (no MaterialDef)"); ok = false; }
                        else if (!droppable.Contains(g.Id))
                            log.AppendLine($"[jeweler] SOFT: gem '{g.Id}' drops from NO loot table yet (boss-drop lane pending — separate agent)");
                    }
                }

                if (ok)
                {
                    chainOk++;
                    // Earmark the first iron/wood-only recipe (no GameState-backed crystals/food)
                    // for the simulated craft so the wallet path needs only EconomyService.
                    if (simRecipe == null && (r.Cost == null || (r.Cost.Crystals == 0 && r.Cost.Food == 0)))
                        simRecipe = r;
                }
            }

            log.AppendLine($"[jeweler] chain checked: {recipes.Count} recipe(s), {gemIds.Count} gem(s); " +
                           $"{chainOk} fully craftable base+gems->output");

            // ── HARD simulated craft (atomic consume->grant + no-funds rollback) ──
            if (simRecipe == null)
            {
                log.AppendLine("[jeweler] SOFT: " + DeNelle.Editor.Regression.RegressionOutcome.PartialSkip(
                    "simulated craft", "no iron/wood-only recipe to simulate without GameState"));
                return;
            }

            var ecoGo = new GameObject("JewelerRegressionEconomy");
            var invGo = new GameObject("JewelerRegressionInventory");
            EconomyService eco = null;
            DeNelle.Village.Crafting.VillageInventory inv = null;
            try
            {
                eco = ecoGo.AddComponent<EconomyService>();
                inv = invGo.AddComponent<DeNelle.Village.Crafting.VillageInventory>();
                // Awake does not fire on AddComponent in edit mode — assign the singletons directly.
                SetStaticInstance(typeof(EconomyService), eco);
                SetStaticInstance(typeof(DeNelle.Village.Crafting.VillageInventory), inv);

                // Seed exactly the base + gems the recipe needs.
                inv.Clear();
                if (simRecipe.Base != null) inv.Add(simRecipe.Base.Id, simRecipe.Base.Count);
                if (simRecipe.Gems != null)
                    foreach (var g in simRecipe.Gems)
                        if (g != null && !string.IsNullOrEmpty(g.Id)) inv.Add(g.Id, g.Count);

                // WALLET SEEDING NOW FOLLOWS THE RECIPE (2026-08-21). This used to read
                // "iron/wood cost covered by the in-session EconomyService pool defaults --
                // wood 200 / iron 80", i.e. the sim silently depended on an AMBIENT constant
                // in EconomyService being bigger than an AUTHORED cost in jeweler-recipes.json.
                // The economy sink pass took 'jewel_ring_steadfast' from iron 30 to iron 150 and
                // the craft started returning "Not enough resources." -- a GREEN oracle turning
                // red on a deliberate, owner-ruled balance change, with nothing wrong in the game.
                // Two independently-authored numbers that must stay ordered is the same
                // duplicated-state trap CLAUDE.md keeps naming, so the coupling is now DERIVED:
                // fund the wallet FROM the recipe's own basket and the sim can never go stale
                // behind a re-price again. GrantUncapped is the sanctioned headless-harness seam
                // (it bypasses the town bank cap, which this sim is not testing); in edit mode
                // with no GameStateService it lands in the fallback pool that eco.Iron reads,
                // which is exactly the wallet TrySpend will charge.
                int seedWood = simRecipe.Cost?.Wood ?? 0;
                int seedIron = simRecipe.Cost?.Iron ?? 0;
                if (seedWood > 0 || seedIron > 0)
                    eco.GrantUncapped(new DeNelle.Village.ResourceCost(seedWood, 0, seedIron, 0));

                int outBefore = inv.Get(simRecipe.OutputAccessoryId);
                int ironBefore = eco.Iron;

                var res = DeNelle.Village.Crafting.JewelerCraftingService.Craft(simRecipe.Id);
                if (!res.Success)
                    failures.Add($"[jeweler] sim '{simRecipe.Id}': Craft returned FAILURE ('{res.FailReason}') with inputs seeded");
                else
                {
                    if (simRecipe.Base != null && inv.Get(simRecipe.Base.Id) != 0)
                        failures.Add($"[jeweler] sim '{simRecipe.Id}': base '{simRecipe.Base.Id}' NOT consumed");
                    if (simRecipe.Gems != null)
                        foreach (var g in simRecipe.Gems)
                            if (g != null && inv.Get(g.Id) != 0)
                                failures.Add($"[jeweler] sim '{simRecipe.Id}': gem '{g.Id}' NOT consumed");
                    if (inv.Get(simRecipe.OutputAccessoryId) != outBefore + 1)
                        failures.Add($"[jeweler] sim '{simRecipe.Id}': output '{simRecipe.OutputAccessoryId}' not granted (+1)");
                    int ironCost = simRecipe.Cost?.Iron ?? 0;
                    if (ironCost > 0 && eco.Iron != ironBefore - ironCost)
                        failures.Add($"[jeweler] sim '{simRecipe.Id}': wallet iron not debited ({ironBefore}->{eco.Iron}, expected -{ironCost})");
                }

                // No-funds rollback: empty inventory -> Craft must fail + consume/grant nothing.
                inv.Clear();
                int outAfterClear = inv.Get(simRecipe.OutputAccessoryId);
                var res2 = DeNelle.Village.Crafting.JewelerCraftingService.Craft(simRecipe.Id);
                if (res2.Success)
                    failures.Add($"[jeweler] sim '{simRecipe.Id}': Craft SUCCEEDED with empty inventory (should fail)");
                if (inv.Get(simRecipe.OutputAccessoryId) != outAfterClear)
                    failures.Add($"[jeweler] sim '{simRecipe.Id}': failed craft still granted output (no rollback)");

                log.AppendLine($"  [jeweler] sim '{simRecipe.Id}' -> consume base+gems, grant '{simRecipe.OutputAccessoryId}', " +
                               "debit wallet; no-funds craft rejected (rollback) OK");
            }
            catch (System.Exception ex)
            {
                failures.Add($"[jeweler] sim threw: {ex.Message}");
                log.AppendLine($"  jeweler sim EXCEPTION: {ex}");
            }
            finally
            {
                // Leave no durable state: clear inventory, reset singletons, destroy the GOs.
                if (inv != null) inv.Clear();
                SetStaticInstance(typeof(EconomyService), null);
                SetStaticInstance(typeof(DeNelle.Village.Crafting.VillageInventory), null);
                if (ecoGo != null) Object.DestroyImmediate(ecoGo);
                if (invGo != null) Object.DestroyImmediate(invGo);
            }
        }

        /// <summary>Assigns a MonoBehaviour-singleton's <c>public static Instance { get; private set; }</c>
        /// backing field by reflection — Awake (which normally sets it) does not fire on AddComponent in
        /// edit-mode batchmode. Null clears it. Best-effort (no-op if the field isn't found).</summary>
        private static void SetStaticInstance(System.Type type, object value)
        {
            if (type == null) return;
            var f = type.GetField("<Instance>k__BackingField",
                                  BindingFlags.NonPublic | BindingFlags.Static);
            if (f != null) f.SetValue(null, value);
        }

        // =====================================================================
        //  TALENT NODE-GRAPH LAYOUT (Path B) — guards the authored graph data so a
        //  bad position/edge can't ship a broken tree:
        //   HARD: a node sets BOTH x and y or NEITHER; positions stay within 0..1;
        //         every prerequisite + edge id resolves to a real node (no dangling).
        //   SOFT (log): how many nodes carry an authored position.
        // =====================================================================
        private static void CheckTalentLayout(List<string> failures, StringBuilder log)
        {
            var checkNodes = new List<DeNelle.Village.Talents.HeroTalentNodeDef>();
            var allIds = new HashSet<string>();

            foreach (var slug in new[] { "knight", "ranger", "mage" })
            {
                var tree = DeNelle.Village.Talents.HeroTalentCatalog.GetTree(slug);
                if (tree?.Nodes == null) continue;
                foreach (var n in tree.Nodes)
                    if (n != null && !string.IsNullOrEmpty(n.Id)) { checkNodes.Add(n); allIds.Add(n.Id); }
            }
            var shared = DeNelle.Village.Talents.HeroTalentCatalog.SharedNodes;
            if (shared != null)
                foreach (var n in shared)
                    if (n != null && !string.IsNullOrEmpty(n.Id)) { checkNodes.Add(n); allIds.Add(n.Id); }

            int positioned = 0;
            foreach (var n in checkNodes)
            {
                bool xs = n.X >= 0f, ys = n.Y >= 0f;
                if (xs != ys)
                    failures.Add($"hero-talents.json: '{n.Id}' has only one of x/y set (x={n.X}, y={n.Y}) — set both or neither");
                if (n.HasPosition)
                {
                    positioned++;
                    if (n.X > 1f || n.Y > 1f)
                        failures.Add($"hero-talents.json: '{n.Id}' position out of 0..1 (x={n.X}, y={n.Y})");
                }
                if (n.Prerequisites != null)
                    foreach (var pr in n.Prerequisites)
                        if (!string.IsNullOrEmpty(pr) && !allIds.Contains(pr))
                            failures.Add($"hero-talents.json: '{n.Id}' prerequisite '{pr}' is not a known node");
                if (n.Edges != null)
                    foreach (var e in n.Edges)
                        if (!string.IsNullOrEmpty(e) && !allIds.Contains(e))
                            failures.Add($"hero-talents.json: '{n.Id}' edge '{e}' is not a known node");
            }
            log.AppendLine($"[talents] layout checked: {checkNodes.Count} node(s), {positioned} positioned; all prereq/edge ids resolve");
        }

        // =====================================================================
        //  ARMED-HERO INVARIANT — BestWeapon(job,1) non-null + prefab resolves
        // -----------------------------------------------------------------------
        //  For each playable class: the level-1 auto-equip MUST return a WeaponDef
        //  (never null → the hero would spawn unarmed), AND that def's prefab MUST
        //  resolve to something attachable:
        //    • Addressable def (loadVia=="addressable" / "gear/" prefabPath) → the key
        //      must be present in the Gear group (Addressables.LoadResourceLocations).
        //    • otherwise → the EquipmentController Resources map must yield a mesh
        //      (Resources.Load of "Heroes/Props/Weapons/<mesh>"). Resolve never returns
        //      null for a non-empty id, so this always yields a path; we assert the prop
        //      actually exists in Resources so the hero shows the real mesh, not just the
        //      tinted-primitive last-resort.
        //  HARD-fails REGRESSION_FAIL on a null pick or an unresolvable Addressable key.
        // =====================================================================
        private static void CheckArmedHeroInvariant(List<string> failures, StringBuilder log)
        {
            GearCatalog.Reload();
            string[] classes = { "knight", "mage", "ranger", "cleric" };
            // Tag disambiguated 2026-08-02: the registered ArmedHeroInvariantRegression suite
            // owns "[armed-hero]". This INLINE check keeps a distinct tag so a log grep names
            // exactly which of the two produced a line.
            log.AppendLine("[armed-hero-inline] BestWeapon(job,1) resolves an attachable prefab per class:");

            foreach (var job in classes)
            {
                WeaponDef w = GearCatalog.BestWeapon(job, 1);
                if (w == null)
                {
                    failures.Add($"armed-hero: BestWeapon('{job}', 1) returned NULL — hero would spawn UNARMED");
                    log.AppendLine($"  AH [{job}] -> <null> | UNARMED");
                    continue;
                }

                if (EquipmentController.IsAddressableWeapon(w))
                {
                    // Blink Addressable weapon: the prefabPath must be a present key in the catalog.
                    bool keyPresent = AddressableKeyExists(w.prefabPath);
                    if (!keyPresent)
                    {
                        failures.Add($"armed-hero: BestWeapon('{job}', 1) = '{w.id}' is Addressable " +
                                     $"'{w.prefabPath}' but that key is NOT present in the Addressables " +
                                     "catalog (Gear group) — Blink prefab would fail to load");
                        log.AppendLine($"  AH [{job}] -> '{w.id}' | Addressable '{w.prefabPath}' | KEY MISSING");
                    }
                    else
                    {
                        log.AppendLine($"  AH [{job}] -> '{w.id}' | Addressable '{w.prefabPath}' | key OK");
                    }
                }
                else
                {
                    // Legacy/Tripo weapon: resolve the Resources mesh path the controller would load.
                    string path = EquipmentController.ResolveWeaponMeshResourcePath(w.id);
                    var prefab = string.IsNullOrEmpty(path) ? null : DeNelle.Core.StructureAssetLoader.LoadStructurePrefab(path);
                    if (prefab == null)
                    {
                        failures.Add($"armed-hero: BestWeapon('{job}', 1) = '{w.id}' maps to Resources " +
                                     $"prop '{path ?? "<null>"}' which loads NULL — hero would show only the " +
                                     "tinted-primitive fallback (real weapon mesh missing from Resources)");
                        log.AppendLine($"  AH [{job}] -> '{w.id}' | Resources '{path}' | PROP MISSING (primitive fallback)");
                    }
                    else
                    {
                        log.AppendLine($"  AH [{job}] -> '{w.id}' | Resources '{path}' | prop OK");
                    }
                }
            }
        }

        // =====================================================================
        //  HAND-SLOT EQUIP RULES — main-hand / off-hand mutual exclusion
        // -----------------------------------------------------------------------
        //  Drives the REAL GearLoadout equip methods on a throwaway hero GO and asserts:
        //   1. equip 1H + shield -> BOTH slots filled (the allowed combo).
        //   2. equip 2H (over a 1H+shield) -> off-hand CLEARED (2H takes both hands).
        //   3. equip shield while a 2H is held -> 2H REMOVED, main falls back to a 1H
        //      (never left unarmed when a 1H exists — armed-hero invariant).
        //  Discovers test ids from the catalog (knight = the class with both a 1H and a 2H,
        //  shield = job 'any') so it stays valid as the catalog grows.
        // =====================================================================
        private static void CheckHandSlotRules(List<string> failures, StringBuilder log)
        {
            GearCatalog.Reload();
            log.AppendLine("[hand-slot] main-hand / off-hand mutual-exclusion rules:");

            const string Job = "knight";   // has BOTH a 1H main and a 2H in the catalog
            // WO-1214: the probe below is a bare GearLoadout with no HeroProgression, so the equip
            // seam sees it as LEVEL 1 - and the seam now enforces the level requirement and fails
            // closed (Ruling 3). Picking ids at level 99 used to "work" only because a manual
            // equip enforced no gate at all; it would now be refused and this case would report a
            // phantom hand-slot failure. Ids are therefore chosen at the level the probe HAS.
            // Verified against weapons.json: a level-1 knight has a 1H, a 2H and a shield, so the
            // three rules below are all still exercised for real.
            int level = 1;

            WeaponDef oneH   = GearCatalog.BestOneHandedWeapon(Job, level);
            WeaponDef twoH   = FindTwoHanded(Job, level);
            WeaponDef shield = FindShield(level);

            if (oneH == null)   { failures.Add("[hand-slot] no 1H weapon found for 'knight' — cannot test the rules"); return; }
            if (twoH == null)   { failures.Add("[hand-slot] no 2H weapon found for 'knight' — cannot test the rules"); return; }
            if (shield == null) { failures.Add("[hand-slot] no shield/off-hand item found in the catalog — cannot test the rules"); return; }

            log.AppendLine($"  test ids: 1H='{oneH.id}' 2H='{twoH.id}' shield='{shield.id}'");

            // Clear any persisted choices for this class so the test starts from a clean slate
            // and doesn't write durable state for the real game.
            string key = Job.ToLowerInvariant();
            PlayerPrefs.DeleteKey("dotr-equip-weapon-" + key);
            PlayerPrefs.DeleteKey("dotr-equip-offhand-" + key);
            PlayerPrefs.DeleteKey("dotr-equip-armor-" + key);

            var go = new GameObject("HandSlotRegressionHero");
            GearLoadout loadout = null;
            try
            {
                loadout = go.AddComponent<GearLoadout>();
                loadout.BindOwnerClass(Job);   // sets the class + runs an initial Refresh

                // --- 1. 1H + shield coexist ---
                loadout.EquipWeaponById(oneH.id);
                loadout.EquipOffHandById(shield.id);
                if (loadout.EquippedWeapon == null || loadout.EquippedWeapon.id != oneH.id)
                    failures.Add($"[hand-slot] 1H+shield: main-hand expected '{oneH.id}' but was '{loadout.EquippedWeapon?.id ?? "<null>"}'");
                if (loadout.EquippedOffHand == null || loadout.EquippedOffHand.id != shield.id)
                    failures.Add($"[hand-slot] 1H+shield: off-hand expected '{shield.id}' but was '{loadout.EquippedOffHand?.id ?? "<null>"}'");
                log.AppendLine($"  R1 1H+shield -> main='{loadout.EquippedWeapon?.id ?? "<null>"}' off='{loadout.EquippedOffHand?.id ?? "<null>"}'");

                // --- 2. equip 2H over 1H+shield -> off-hand cleared ---
                loadout.EquipWeaponById(twoH.id);
                if (loadout.EquippedWeapon == null || loadout.EquippedWeapon.id != twoH.id)
                    failures.Add($"[hand-slot] equip 2H: main-hand expected '{twoH.id}' but was '{loadout.EquippedWeapon?.id ?? "<null>"}'");
                if (loadout.EquippedOffHand != null)
                    failures.Add($"[hand-slot] equip 2H: off-hand should be CLEARED but was '{loadout.EquippedOffHand.id}' (2H takes both hands)");
                log.AppendLine($"  R2 equip 2H -> main='{loadout.EquippedWeapon?.id ?? "<null>"}' off='{loadout.EquippedOffHand?.id ?? "<null>"}'");

                // --- 3. equip shield while 2H held -> 2H removed, main falls back to a 1H ---
                loadout.EquipOffHandById(shield.id);
                if (loadout.EquippedOffHand == null || loadout.EquippedOffHand.id != shield.id)
                    failures.Add($"[hand-slot] shield-over-2H: off-hand expected '{shield.id}' but was '{loadout.EquippedOffHand?.id ?? "<null>"}'");
                if (loadout.EquippedWeapon != null && loadout.EquippedWeapon.IsTwoHanded)
                    failures.Add($"[hand-slot] shield-over-2H: 2H '{loadout.EquippedWeapon.id}' should have been REMOVED but is still in the main hand");
                // Armed-hero invariant: a 1H exists for this class, so the main hand must NOT be empty.
                if (loadout.EquippedWeapon == null)
                    failures.Add("[hand-slot] shield-over-2H: main hand left UNARMED though a 1H fallback exists (armed-hero invariant broken)");
                else if (loadout.EquippedWeapon.IsOffHandItem)
                    failures.Add($"[hand-slot] shield-over-2H: main hand holds an off-hand item '{loadout.EquippedWeapon.id}' (a shield can never be the main hand)");
                log.AppendLine($"  R3 shield-over-2H -> main='{loadout.EquippedWeapon?.id ?? "<null>"}' off='{loadout.EquippedOffHand?.id ?? "<null>"}'");
            }
            catch (System.Exception ex)
            {
                failures.Add($"[hand-slot] rule check threw: {ex.Message}");
                log.AppendLine($"  hand-slot check EXCEPTION: {ex}");
            }
            finally
            {
                if (go != null) Object.DestroyImmediate(go);
                // Leave no durable test state behind.
                PlayerPrefs.DeleteKey("dotr-equip-weapon-" + key);
                PlayerPrefs.DeleteKey("dotr-equip-offhand-" + key);
                PlayerPrefs.DeleteKey("dotr-equip-armor-" + key);
            }
        }

        private static WeaponDef FindTwoHanded(string job, int level)
        {
            foreach (var w in GearCatalog.AllWeapons())
            {
                if (w == null || !w.IsTwoHanded) continue;
                if (GearCatalog.WeaponFitsClass(w, job) && (w.req == null || level >= w.req.level)) return w;
            }
            return null;
        }

        private static WeaponDef FindShield(int level)
        {
            foreach (var w in GearCatalog.AllWeapons())
            {
                if (w == null || !w.IsOffHandItem) continue;
                if (w.req == null || level >= w.req.level) return w;
            }
            return null;
        }

        // True when <paramref name="key"/> resolves to at least one Addressable resource
        // location (i.e. the address is registered in the content catalog — the Gear group
        // entries marked by BlinkAddressableMarker). Synchronous via WaitForCompletion; the
        // handle is released after the check so the locations probe never leaks.
        private static bool AddressableKeyExists(string key)
        {
            if (string.IsNullOrEmpty(key)) return false;
            try
            {
                AsyncOperationHandle<IList<IResourceLocation>> h =
                    Addressables.LoadResourceLocationsAsync(key);
                IList<IResourceLocation> locs = h.WaitForCompletion();
                bool exists = locs != null && locs.Count > 0;
                if (h.IsValid()) Addressables.Release(h);
                return exists;
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[DataRegression] Addressable key probe threw for '{key}': {ex.Message}");
                return false;
            }
        }

        // WO-996: after the library merge, every Resources armor id must exist in StreamingAssets
        // (same subset shape as weapons). Schema versions must agree.
        private static void CheckArmorDualCopy(List<string> failures, StringBuilder log)
        {
            log.AppendLine("[armor-dual-copy] Resources armor ids ⊆ StreamingAssets (WO-996):");
            string rPath = "Assets/Resources/Data/Canonical/armor.json";
            string sPath = "Assets/StreamingAssets/Data/Canonical/armor.json";
            if (!System.IO.File.Exists(rPath) || !System.IO.File.Exists(sPath))
            {
                failures.Add("armor-dual-copy: one or both armor.json copies are missing on disk");
                return;
            }
            try
            {
                var rTok = Newtonsoft.Json.Linq.JObject.Parse(System.IO.File.ReadAllText(rPath));
                var sTok = Newtonsoft.Json.Linq.JObject.Parse(System.IO.File.ReadAllText(sPath));
                int rVer = rTok.Value<int?>("version") ?? 0;
                int sVer = sTok.Value<int?>("version") ?? 0;
                if (rVer != sVer)
                    failures.Add($"armor-dual-copy: schema version mismatch Resources=v{rVer} StreamingAssets=v{sVer}");
                var rIds = new HashSet<string>();
                var sIds = new HashSet<string>();
                foreach (var row in rTok["armor"] as Newtonsoft.Json.Linq.JArray ?? new Newtonsoft.Json.Linq.JArray())
                    if (row["id"] != null) rIds.Add(row["id"].ToString());
                foreach (var row in sTok["armor"] as Newtonsoft.Json.Linq.JArray ?? new Newtonsoft.Json.Linq.JArray())
                    if (row["id"] != null) sIds.Add(row["id"].ToString());
                int missing = 0;
                foreach (var id in rIds)
                {
                    if (!sIds.Contains(id))
                    {
                        missing++;
                        if (missing <= 8)
                            failures.Add($"armor-dual-copy: Resources id '{id}' missing from StreamingAssets library");
                    }
                }
                if (missing > 8)
                    failures.Add($"armor-dual-copy: …and {missing - 8} more Resources-only ids");
                log.AppendLine($"  Resources={rIds.Count} StreamingAssets={sIds.Count} version R={rVer}/S={sVer} missingFromLibrary={missing}");
            }
            catch (System.Exception ex)
            {
                failures.Add($"armor-dual-copy: parse threw {ex.GetType().Name}: {ex.Message}");
            }
        }

        // WO-975: every Gear.asset serialize-entry GUID must resolve to an on-disk asset.
        private static void CheckGearAddressableGroup(List<string> failures, StringBuilder log)
        {
            log.AppendLine("[gear-addressable-group] Gear.asset entry GUIDs resolve on disk (WO-975):");
            const string gearAsset = "Assets/AddressableAssetsData/AssetGroups/Gear.asset";
            if (!System.IO.File.Exists(gearAsset))
            {
                failures.Add("gear-addressable-group: Gear.asset missing");
                return;
            }
            var re = new System.Text.RegularExpressions.Regex(
                @"^\s+-\s+m_GUID:\s+([0-9a-fA-F]{32})\s*$",
                System.Text.RegularExpressions.RegexOptions.Multiline);
            string text = System.IO.File.ReadAllText(gearAsset);
            var seen = new HashSet<string>();
            int total = 0, ok = 0, dangling = 0;
            foreach (System.Text.RegularExpressions.Match m in re.Matches(text))
            {
                string guid = m.Groups[1].Value.ToLowerInvariant();
                if (!seen.Add(guid)) continue;
                total++;
                string path = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
                if (string.IsNullOrEmpty(path) ||
                    (!System.IO.File.Exists(path) && !System.IO.Directory.Exists(path)))
                {
                    dangling++;
                    if (dangling <= 6)
                        failures.Add($"gear-addressable-group: dangling GUID {guid} path='{path}'");
                }
                else ok++;
            }
            if (total == 0)
                failures.Add("gear-addressable-group: zero serialize-entry GUIDs in Gear.asset");
            if (dangling > 6)
                failures.Add($"gear-addressable-group: …and {dangling - 6} more dangling GUIDs");
            log.AppendLine($"  entries={total} resolvable={ok} dangling={dangling}");
            // Soft note: AddressableKeyExists remains advisory for per-key probes (WO-975 §3).
            log.AppendLine("  note: AddressableKeyExists() is deliberately advisory for single-key probes; this suite is the hard fence.");
        }

        // =====================================================================
        //  BATTLE CLOSING — WO-505: victory/defeat clip resolve + star-rating math
        // -----------------------------------------------------------------------
        //  (a) AUDIO: the win/loss climax must not be silent. The clips ship at
        //      Assets/Audio/Resources/{victory,defeat}.mp3 and AudioBootstrap loads
        //      them by short name via Resources.Load<AudioClip>("victory"/"defeat").
        //      We do the EXACT same load and FAIL if either returns null — that is the
        //      silent-track bug class (the known Resources.Load("dungeon") == null).
        //  (b) STARS: BattleStarRating.StarsForDuration must map sample durations to the
        //      right tier (60s->3, 100s->2, 200s->1) and MultiplierForStars must match
        //      (3->1.50x, 2->1.25x, 1->1.00x). Pure math; deterministic.
        //  Emits FlowTrace.Fail per violation so it lands in the break-log marker, in
        //  addition to the REGRESSION_FAIL line.
        // =====================================================================
        private static void CheckBattleClosing(List<string> failures, StringBuilder log)
        {
            log.AppendLine("[battle-closing] victory/defeat clip resolve + star-rating tiers:");

            // (a) AUDIO — resolve through the same Resources path AudioBootstrap uses.
            string[] cueNames = { "victory", "defeat" };
            foreach (var name in cueNames)
            {
                var clip = Resources.Load<AudioClip>(name);
                if (clip == null)
                {
                    string msg = $"battle-closing: Resources.Load<AudioClip>(\"{name}\") is NULL — " +
                                 "the win/loss climax would play SILENT (clip missing from " +
                                 "Assets/Audio/Resources/ or not imported as an AudioClip)";
                    failures.Add(msg);
                    DeNelle.Core.Diagnostics.FlowTrace.Fail("Regression", msg);
                    log.AppendLine($"  AUDIO '{name}' -> NULL (SILENT CLIMAX)");
                }
                else
                {
                    log.AppendLine($"  AUDIO '{name}' -> clip OK ('{clip.name}', {clip.length:0.0}s)");
                }
            }

            // (b) STARS — sample durations -> expected tier, and the matching multiplier.
            // (duration, expectedStars, expectedMultiplier)
            var samples = new (float dur, int stars, float mult)[]
            {
                (60f,  3, 1.50f),   // fast clean win
                (90f,  3, 1.50f),   // exactly the 3-star boundary (inclusive)
                (100f, 2, 1.25f),   // mid
                (120f, 2, 1.25f),   // exactly the 2-star boundary (inclusive)
                (200f, 1, 1.00f),   // slow win
            };
            foreach (var s in samples)
            {
                int gotStars = BattleStarRating.StarsForDuration(s.dur);
                float gotMult = BattleStarRating.MultiplierForStars(gotStars);
                bool starOk = gotStars == s.stars;
                bool multOk = Mathf.Approximately(gotMult, s.mult);
                if (!starOk)
                {
                    string msg = $"battle-closing: StarsForDuration({s.dur:0}s) = {gotStars}, expected {s.stars}";
                    failures.Add(msg);
                    DeNelle.Core.Diagnostics.FlowTrace.Fail("Regression", msg);
                }
                if (!multOk)
                {
                    string msg = $"battle-closing: MultiplierForStars({gotStars}) = {gotMult:0.00}, expected {s.mult:0.00}";
                    failures.Add(msg);
                    DeNelle.Core.Diagnostics.FlowTrace.Fail("Regression", msg);
                }
                log.AppendLine($"  STARS dur={s.dur:0}s -> {gotStars} star(s) x{gotMult:0.00} " +
                               $"(expected {s.stars} x{s.mult:0.00}) {((starOk && multOk) ? "OK" : "FAIL")}");
            }
        }

        // =====================================================================
        //  WEAPON SWING-TRAIL VFX - WO-504 slice 3 (WeaponVfxMap pure resolver)
        // -----------------------------------------------------------------------
        //  Gates the rarity -> trail color/width MAPPING (not the aesthetic - the
        //  exact colors are owner-felt-tune bones). Asserts:
        //   1. each band resolves a DISTINCT color (common != legendary, etc.);
        //   2. legendary (and elarion) == the GoldColor const, common/null == SteelColor;
        //   3. a null weapon -> the steel common default (null-safe);
        //   4. trail WIDTH escalates MONOTONICALLY common < uncommon < rare < epic < legendary.
        //  Emits FlowTrace.Fail per violation so it lands in the break-log marker.
        // =====================================================================
        private static void CheckWeaponVfx(List<string> failures, StringBuilder log)
        {
            log.AppendLine("[weapon-vfx] rarity -> swing-trail color/width mapping (WO-504 s3):");

            // (1) distinct color per band - build the per-band colors via the resolver.
            string[] bands = { "common", "uncommon", "rare", "epic", "legendary", "elarion" };
            var colors = new Dictionary<string, Color>();
            var widths = new Dictionary<string, float>();
            foreach (var b in bands)
            {
                var w = new WeaponDef { id = "vfx_" + b, name = b, rarity = b };
                var profile = WeaponVfxMap.Resolve(w);
                colors[b] = profile.TrailColor;
                widths[b] = profile.TrailWidth;
                log.AppendLine($"  VFX {b} -> color=({profile.TrailColor.r:0.00},{profile.TrailColor.g:0.00}," +
                               $"{profile.TrailColor.b:0.00},{profile.TrailColor.a:0.00}) width={profile.TrailWidth:0.000}");
            }

            // The five DISTINCT visual tiers (legendary & elarion intentionally SHARE the gold apex).
            string[] distinct = { "common", "uncommon", "rare", "epic", "legendary" };
            for (int i = 0; i < distinct.Length; i++)
                for (int j = i + 1; j < distinct.Length; j++)
                {
                    if (ApproxColor(colors[distinct[i]], colors[distinct[j]]))
                    {
                        string msg = $"weapon-vfx: bands '{distinct[i]}' and '{distinct[j]}' resolve the SAME trail color " +
                                     "(each rarity tier must read distinct)";
                        failures.Add(msg);
                        DeNelle.Core.Diagnostics.FlowTrace.Fail("Regression", msg);
                    }
                }

            // common vs legendary must differ (the headline read).
            if (ApproxColor(colors["common"], colors["legendary"]))
            {
                string msg = "weapon-vfx: common and legendary resolve the same trail color (a legendary blade must read legendary)";
                failures.Add(msg);
                DeNelle.Core.Diagnostics.FlowTrace.Fail("Regression", msg);
            }

            // (2) apex/default consts pinned by name.
            if (!ApproxColor(colors["legendary"], WeaponVfxMap.GoldColor))
            {
                string msg = "weapon-vfx: legendary color != WeaponVfxMap.GoldColor (the gold apex const)";
                failures.Add(msg);
                DeNelle.Core.Diagnostics.FlowTrace.Fail("Regression", msg);
            }
            if (!ApproxColor(colors["elarion"], WeaponVfxMap.GoldColor))
            {
                string msg = "weapon-vfx: elarion mark color != WeaponVfxMap.GoldColor (top band shares the gold apex)";
                failures.Add(msg);
                DeNelle.Core.Diagnostics.FlowTrace.Fail("Regression", msg);
            }

            // (3) null weapon -> steel common default (null-safe).
            var nullProfile = WeaponVfxMap.Resolve(null);
            if (!ApproxColor(nullProfile.TrailColor, WeaponVfxMap.SteelColor))
            {
                string msg = "weapon-vfx: Resolve(null) color != WeaponVfxMap.SteelColor (null weapon must fall back to the steel default)";
                failures.Add(msg);
                DeNelle.Core.Diagnostics.FlowTrace.Fail("Regression", msg);
            }
            if (!Mathf.Approximately(nullProfile.TrailWidth, WeaponVfxMap.CommonWidth))
            {
                string msg = "weapon-vfx: Resolve(null) width != WeaponVfxMap.CommonWidth (null weapon must fall back to the common width)";
                failures.Add(msg);
                DeNelle.Core.Diagnostics.FlowTrace.Fail("Regression", msg);
            }

            // (4) width escalates MONOTONICALLY common < uncommon < rare < epic < legendary.
            for (int i = 1; i < distinct.Length; i++)
            {
                float prev = widths[distinct[i - 1]];
                float cur  = widths[distinct[i]];
                if (!(cur > prev))
                {
                    string msg = $"weapon-vfx: trail width does not escalate '{distinct[i - 1]}'({prev:0.000}) -> " +
                                 $"'{distinct[i]}'({cur:0.000}) (must be monotonically increasing)";
                    failures.Add(msg);
                    DeNelle.Core.Diagnostics.FlowTrace.Fail("Regression", msg);
                }
            }
        }

        // =====================================================================
        //  ACCESSORIES — accessories.json via GearCatalog.Accessories (WO-543)
        // =====================================================================
        private static void CheckAccessories(List<string> failures, StringBuilder log)
        {
            var accessories = new List<AccessoryDef>(GearCatalog.Accessories);
            log.AppendLine($"accessories.json -> {accessories.Count} AccessoryDef object(s)");

            if (accessories.Count != 10)
                failures.Add($"accessories.json deserialized to {accessories.Count} objects, expected 10 (mapping break or roster drift)");

            int badField = 0, noIcon = 0, overDmg = 0, overDef = 0;
            foreach (var ac in accessories)
            {
                bool ok = ac != null && !string.IsNullOrEmpty(ac.id) && !string.IsNullOrEmpty(ac.name);
                if (!ok) { badField++; continue; }

                bool legendary = !string.IsNullOrEmpty(ac.rarity) &&
                                 ac.rarity.Trim().ToLowerInvariant() == "legendary";

                // Non-legendary balance caps (legendary is the apex and may exceed them).
                if (!legendary && ac.damageMult >= 0.20f) { overDmg++;
                    failures.Add($"accessories.json: '{ac.id}' damageMult {ac.damageMult:0.00} >= 0.20 cap (non-legendary)"); }
                if (!legendary && ac.defense >= 0.15f) { overDef++;
                    failures.Add($"accessories.json: '{ac.id}' defense {ac.defense:0.00} >= 0.15 cap (non-legendary)"); }

                if (string.IsNullOrEmpty(ac.iconPath)) { noIcon++;
                    failures.Add($"accessories.json: '{ac.id}' has no iconPath (would render with no shop sprite)"); }

                log.AppendLine($"  AC {ac.id} | name='{ac.name}' | slot={ac.slot} rarity={ac.rarity} " +
                               $"| dmg={ac.damageMult:0.00} def={ac.defense:0.00} hp={ac.hpBonus} " +
                               $"| icon='{ac.iconPath}' | cost={CostStr(GearCatalog.GetBuyCost(ac))}");
            }
            if (badField > 0) failures.Add($"{badField} accessory(ies) have null/empty id or name");
            log.AppendLine($"[accessories] caps: {overDmg} over-dmg, {overDef} over-def, {noIcon} missing-icon");
        }

        // =====================================================================
        //  ARMOR/ACCESSORY RIM-LIGHT VFX — WO-543 ArmorVfxMap pure resolver
        // =====================================================================
        private static void CheckArmorVfx(List<string> failures, StringBuilder log)
        {
            log.AppendLine("[armor-vfx] rarity -> rim-light color/intensity mapping (WO-543):");

            string[] bands = { "common", "uncommon", "rare", "epic", "legendary", "elarion" };
            var colors = new Dictionary<string, Color>();
            var intensities = new Dictionary<string, float>();
            foreach (var b in bands)
            {
                var profile = ArmorVfxMap.Resolve(b);
                colors[b] = profile.RimColor;
                intensities[b] = profile.RimIntensity;
                log.AppendLine($"  AVFX {b} -> color=({profile.RimColor.r:0.00},{profile.RimColor.g:0.00}," +
                               $"{profile.RimColor.b:0.00}) intensity={profile.RimIntensity:0.000} burst={profile.LegendaryBurst}");
            }

            // Distinct color per visual tier (legendary & elarion share the gold apex).
            string[] distinct = { "common", "uncommon", "rare", "epic", "legendary" };
            for (int i = 0; i < distinct.Length; i++)
                for (int j = i + 1; j < distinct.Length; j++)
                    if (ApproxColor(colors[distinct[i]], colors[distinct[j]]))
                    {
                        string msg = $"armor-vfx: bands '{distinct[i]}' and '{distinct[j]}' resolve the SAME rim color";
                        failures.Add(msg);
                        DeNelle.Core.Diagnostics.FlowTrace.Fail("Regression", msg);
                    }

            // legendary / elarion == gold apex.
            if (!ApproxColor(colors["legendary"], ArmorVfxMap.GoldColor))
            {
                string msg = "armor-vfx: legendary color != ArmorVfxMap.GoldColor (the gold apex const)";
                failures.Add(msg);
                DeNelle.Core.Diagnostics.FlowTrace.Fail("Regression", msg);
            }
            if (!ApproxColor(colors["elarion"], ArmorVfxMap.GoldColor))
            {
                string msg = "armor-vfx: elarion color != ArmorVfxMap.GoldColor (top band shares the gold apex)";
                failures.Add(msg);
                DeNelle.Core.Diagnostics.FlowTrace.Fail("Regression", msg);
            }

            // common == OFF (no glow) + null-safe default off.
            if (intensities["common"] != 0f)
            {
                string msg = $"armor-vfx: common intensity {intensities["common"]:0.00} != 0 (common must be OFF — no glow)";
                failures.Add(msg);
                DeNelle.Core.Diagnostics.FlowTrace.Fail("Regression", msg);
            }
            if (ArmorVfxMap.Resolve((string)null).RimIntensity != 0f)
            {
                string msg = "armor-vfx: Resolve(null) intensity != 0 (no gear must be OFF)";
                failures.Add(msg);
                DeNelle.Core.Diagnostics.FlowTrace.Fail("Regression", msg);
            }

            // intensity escalates MONOTONICALLY common < uncommon < rare < epic < legendary.
            for (int i = 1; i < distinct.Length; i++)
            {
                float prev = intensities[distinct[i - 1]];
                float cur  = intensities[distinct[i]];
                if (!(cur > prev))
                {
                    string msg = $"armor-vfx: rim intensity does not escalate '{distinct[i - 1]}'({prev:0.00}) -> " +
                                 $"'{distinct[i]}'({cur:0.00}) (must be monotonically increasing)";
                    failures.Add(msg);
                    DeNelle.Core.Diagnostics.FlowTrace.Fail("Regression", msg);
                }
            }

            // legendary drives the apex burst; lower bands do not.
            if (!ArmorVfxMap.Resolve("legendary").LegendaryBurst)
                failures.Add("armor-vfx: legendary band must set LegendaryBurst (the Burst_rings apex)");
            if (ArmorVfxMap.Resolve("rare").LegendaryBurst)
                failures.Add("armor-vfx: rare band must NOT set LegendaryBurst");

            // Dominant-rarity selection: an epic ring on common armor -> epic profile.
            var dom = ArmorVfxMap.Resolve(
                new ArmorDef { id = "a", rarity = "common" },
                new AccessoryDef { id = "r", rarity = "epic", slot = "ring" },
                null);
            if (!ApproxColor(dom.RimColor, ArmorVfxMap.EpicColor))
                failures.Add("armor-vfx: dominant-rarity pick wrong (epic ring + common armor should resolve EPIC)");
        }

        private static bool ApproxColor(Color a, Color b)
        {
            return Mathf.Approximately(a.r, b.r) && Mathf.Approximately(a.g, b.g)
                && Mathf.Approximately(a.b, b.b) && Mathf.Approximately(a.a, b.a);
        }

        private static string CostStr(DeNelle.Village.ResourceCost c)
        {
            var parts = DeNelle.Core.UI.CostFormat.Parts(new[]
            {
                ("wood", "Wood", c.Wood),
                ("iron", "Iron", c.Iron),
                ("stone", "Stone", c.Food),
                ("crystal", "Crystals", c.Crystals),
            });
            return parts.Count == 0 ? "Free" : DeNelle.Core.UI.CostFormat.Words(parts);
        }
    }
}

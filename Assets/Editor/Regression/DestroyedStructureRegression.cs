// =============================================================================
// DestroyedStructureRegression — WO-753 owner ruling oracle.
// -----------------------------------------------------------------------------
// Owner ruling 2026-07-19 (memory `destroyed-items-no-rebuild-full-cost-and-vfx-
// cleanup`, SUPERSEDES the WO-672 "persistent inoperable shell"): a structure
// DESTROYED by enemies is LOST — NO in-place repair, the object + its bound NPC
// are removed, the player rebuilds fresh at FULL cost.
//
// REGISTERED in DataRegression.RunAll (the [destroyed-structure] line) — this header
// claimed the opposite until WO-1496 (2026-09-06) re-read the registry and found the
// registration already there. Also invokable standalone via:
//   run-unity-method.ps1 -Method DeNelle.Editor.DestroyedStructureRegression.RunStandalone
//                        -LogName destroyed-structure.log
// Contract mirrors the other suites: public static bool Run(out string reason);
// markers DESTROYED_STRUCTURE_OK (Debug.Log) / DESTROYED_STRUCTURE_FAIL (LogError).
//
// PROBES (one run, step-in/step-out lines per CLAUDE.md §12):
//   A. REPAIR NO-OP ON DESTROYED (Point 3) — DefenseTower / ArcaneTower / Tower /
//      ResourceCollector / WallSegment: drive to broken, call Repair(), assert HP
//      stays 0 / _broken stays true (destroyed = lost, no repair-back-online).
//   B. DESTROYED EXCLUDED FROM REPAIR-ALL (Point 3) — assert the exclusion
//      PREDICATES the WallRepairController offer now uses: a broken tower reports
//      IsBroken (skipped), a collapsed wall reports DamageFraction >=
//      WallRepairController.DestroyedFraction (skipped).
//   C. OBJECT + VENDOR REMOVED ON DEATH (Points 1/2) — PLAY-MODE ONLY: the removal
//      (Destructible.NotifyBroken → free grid cell + drop persisted record +
//      despawn vendor + Destroy(gameObject)) needs the live PlacementGrid /
//      GameStateService singletons (their Awake only runs in play mode) and a
//      runtime Destroy (edit-mode Destroy is forbidden). Skipped with a logged
//      note under an edit-mode batch run — drive it from the AutoPilot/play harness.
//   D. WO-843 REBUILD-CARD STATE (owner F8 2026-08-02 "lumber mill destroyed no
//      option to rebuild it") — with ONLY a resurfaced baked twin standing, the
//      card gate (StructureSingleton.IsPlayerBuilt) reads BUILDABLE while the
//      enforcement query (IsBuilt) still sees the twin; the free-build burn is
//      idempotent (full-cost rebuild) and the monotonic ever-built set keeps the
//      WO-834 resurface gate open.
//   E. RELOAD MUST NOT HALF-REVIVE A DESTROYED COLLECTOR (WO-1865, owner F8
//      2026-09-18 "the wave report said the Forge and Lumber Mill were destroyed
//      but both are standing") — a persisted hp=0 collector must reload as
//      hp=0/broken, NOT the impossible hp=1.00/broken=True the old post-LoadState
//      seed produced; an UNINITIALISED collector must still reload at full health;
//      and WaveDamageReport's own predicate must be untouched (it was never the
//      defect — it read IsBroken live and correctly).
// =============================================================================
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using UnityEngine;
using DeNelle.Village;
using DeNelle.Village.Buildings.Progression;

namespace DeNelle.Editor
{
    public static class DestroyedStructureRegression
    {
        /// <summary>Batchmode entry point (run-unity-method.ps1).</summary>
        public static void RunStandalone()
        {
            Run(out _);   // Run() emits the DESTROYED_STRUCTURE_OK / DESTROYED_STRUCTURE_FAIL marker
        }

        public static bool Run(out string reason)
        {
            var failures = new List<string>();
            var log = new StringBuilder();
            var created = new List<GameObject>();
            // WO-1496: notes ride out on the REASON string, not just this log. Section C stands
            // down in edit-mode batch, and until now it did so with a bare `return` into a local
            // StringBuilder the caller never reads - so the suite reported a full green while one
            // of its four probes had asserted nothing. The stand-down is now DECLARED.
            var notes = new List<string>();
            log.AppendLine("=== DestroyedStructureRegression (WO-753): destroyed = lost, no repair, rebuild full-cost ===");

            try
            {
                ProbeRepairNoOp(created, failures, log);
                ProbeRepairAllExclusion(created, failures, log);
                ProbeObjectRemoval(created, failures, log, notes);
                ProbeRebuildCardState(failures, log);
                ProbeReloadDoesNotHalfReviveCollector(created, failures, log);
            }
            catch (System.Exception ex)
            {
                failures.Add($"DestroyedStructureRegression threw: {ex.GetType().Name}: {ex.Message}");
            }
            finally
            {
                foreach (var go in created)
                    if (go != null) Object.DestroyImmediate(go);
            }

            return Verdict(failures, log, notes, out reason);
        }

        // =====================================================================
        //  A. Repair() no-ops on a DESTROYED structure (Point 3)
        // =====================================================================
        private static void ProbeRepairNoOp(List<GameObject> created, List<string> failures, StringBuilder log)
        {
            // DefenseTower — Hp lazy-inits to _maxHp; break it, then Repair() must NOT un-break.
            {
                var go = new GameObject("Destroyed_DefenseTower"); created.Add(go);
                var t = go.AddComponent<DefenseTower>();
                t.ApplyContactDamage(100000f);   // hp -> 0, _broken = true
                bool brokeOk = t.IsBroken && t.HpFraction <= 0.0001f;
                t.Repair();
                bool stayed = t.IsBroken && t.HpFraction <= 0.0001f;
                log.AppendLine($"  DefenseTower: broke={brokeOk} after Repair() broken={t.IsBroken} hpFrac={t.HpFraction:0.00}");
                if (!brokeOk) failures.Add("DefenseTower did not reach broken/hp0 on lethal contact damage");
                else if (!stayed) failures.Add("DefenseTower.Repair() REVIVED a destroyed tower (ruling: destroyed = lost, no repair)");
            }

            // ArcaneTower — same lazy-Hp path.
            {
                var go = new GameObject("Destroyed_ArcaneTower"); created.Add(go);
                var t = go.AddComponent<ArcaneTower>();
                t.ApplyContactDamage(100000f);
                bool brokeOk = t.IsBroken && t.HpFraction <= 0.0001f;
                t.Repair();
                bool stayed = t.IsBroken && t.HpFraction <= 0.0001f;
                log.AppendLine($"  ArcaneTower: broke={brokeOk} after Repair() broken={t.IsBroken} hpFrac={t.HpFraction:0.00}");
                if (!brokeOk) failures.Add("ArcaneTower did not reach broken/hp0 on lethal contact damage");
                else if (!stayed) failures.Add("ArcaneTower.Repair() REVIVED a destroyed spire (ruling: destroyed = lost, no repair)");
            }

            // Tower (legacy TowerCombat tower) — _hp starts 0 (Awake/Configure don't run on an
            // edit-mode AddComponent), so seed it to _maxHp for the probe. Editor-test seeding only.
            {
                var go = new GameObject("Destroyed_Tower"); created.Add(go);
                var t = go.AddComponent<Tower>();
                SeedPrivateFloat(t, "_hp", t.MaxHp);
                t.ApplyContactDamage(t.MaxHp * 2f);   // hp -> 0, _broken = true
                bool brokeOk = t.IsBroken && t.HpFraction <= 0.0001f;
                t.Repair();
                bool stayed = t.IsBroken && t.HpFraction <= 0.0001f;
                log.AppendLine($"  Tower: broke={brokeOk} after Repair() broken={t.IsBroken} hpFrac={t.HpFraction:0.00}");
                if (!brokeOk) failures.Add("Tower did not reach broken/hp0 on lethal contact damage");
                else if (!stayed) failures.Add("Tower.Repair() REVIVED a destroyed tower (ruling: destroyed = lost, no repair)");
            }

            // ResourceCollector — Configure sets _hp = _maxHp (no Awake needed); break via siege.
            {
                var go = new GameObject("Destroyed_Collector"); created.Add(go);
                var c = go.AddComponent<ResourceCollector>();
                c.Configure(ResourceBuildingProgression.FarmId);
                // Edit-mode determinism: the collector round-trips HP through PlayerPrefs
                // (SaveState/LoadState), so a prior probe run can leave FarmId broken and the
                // siege below then no-ops at the `if (!IsAlive) return;` guard. Seed a known
                // ALIVE state first (mirrors the Tower _hp seed above). The assertion — siege
                // drives it broken, then Repair() must NOT revive it — is unchanged.
                SeedPrivateFloat(c, "_maxHp", 120f);
                SeedPrivateFloat(c, "_hp", 120f);
                SeedPrivateBool(c, "_broken", false);
                c.ApplyContactDamage(100000f);   // OnSiegeDestroyed -> _broken = true, hp 0
                bool brokeOk = c.IsBroken && c.HpFraction <= 0.0001f;
                c.Repair();
                bool stayed = c.IsBroken && c.HpFraction <= 0.0001f;
                log.AppendLine($"  ResourceCollector: broke={brokeOk} after Repair() broken={c.IsBroken} hpFrac={c.HpFraction:0.00}");
                if (!brokeOk) failures.Add("ResourceCollector did not reach broken/hp0 on lethal siege damage");
                else if (!stayed) failures.Add("ResourceCollector.Repair() REVIVED a destroyed collector (ruling: destroyed = lost, no repair)");
            }

            // WallSegment — collapse the 0..100 damage track; Repair(amount) must no-op at rubble.
            {
                var go = new GameObject("Destroyed_Wall"); created.Add(go);
                var w = go.AddComponent<WallSegment>();
                w.ApplyContactDamage(100000f);   // _damage clamps to 100 -> IsDestroyed
                bool brokeOk = w.IsDestroyed && w.Damage >= 99.999f;
                w.Repair(50f);
                bool stayed = w.IsDestroyed && w.Damage >= 99.999f;
                log.AppendLine($"  WallSegment: collapsed={brokeOk} after Repair(50) destroyed={w.IsDestroyed} damage={w.Damage:0.0}");
                if (!brokeOk) failures.Add("WallSegment did not collapse (damage 100) on lethal contact damage");
                else if (!stayed) failures.Add("WallSegment.Repair() REVIVED a collapsed section (ruling: destroyed = lost, no repair)");
            }
        }

        // =====================================================================
        //  B. Destroyed structures are EXCLUDED from the Repair-All offer (Point 3)
        //     Asserts the exclusion PREDICATES CollectRepairAllSet / AddDamagedOfType now use.
        // =====================================================================
        private static void ProbeRepairAllExclusion(List<GameObject> created, List<string> failures, StringBuilder log)
        {
            // Tower-family skip predicate = IsBroken.
            {
                var go = new GameObject("RepairAll_DefenseTower"); created.Add(go);
                var t = go.AddComponent<DefenseTower>();
                t.ApplyContactDamage(100000f);
                log.AppendLine($"  RepairAll exclusion: broken DefenseTower IsBroken={t.IsBroken} (skip predicate)");
                if (!t.IsBroken) failures.Add("broken DefenseTower does not report IsBroken — the Repair-All skip predicate would not fire");
            }

            // Wall skip predicate = DamageFraction >= WallRepairController.DestroyedFraction.
            {
                var go = new GameObject("RepairAll_Wall"); created.Add(go);
                var w = go.AddComponent<WallSegment>();
                w.ApplyContactDamage(100000f);
                var target = RepairTarget.TryWrap(w);
                if (target == null || !target.IsValid)
                {
                    failures.Add("RepairTarget.TryWrap returned null/invalid for a collapsed WallSegment");
                }
                else
                {
                    bool wouldSkip = target.DamageFraction >= WallRepairController.DestroyedFraction;
                    log.AppendLine($"  RepairAll exclusion: collapsed wall DamageFraction={target.DamageFraction:0.000} " +
                                   $">= DestroyedFraction({WallRepairController.DestroyedFraction:0.000}) => skip={wouldSkip}");
                    if (!wouldSkip)
                        failures.Add($"collapsed WallSegment DamageFraction {target.DamageFraction:0.000} < DestroyedFraction " +
                                     "— the CollectAllDamaged skip predicate would not exclude a destroyed wall");
                }
            }
        }

        // =====================================================================
        //  C. Object + bound vendor removed on death (Points 1/2) — PLAY MODE ONLY
        // =====================================================================
        private static void ProbeObjectRemoval(List<GameObject> created, List<string> failures, StringBuilder log,
                                               List<string> notes)
        {
            if (!Application.isPlaying)
            {
                // WO-1496: a DECLARED stand-down (RegressionOutcome.PartialSkip), not a prose line
                // in a log the caller drops. The note is carried out on the reason string so the
                // registered [destroyed-structure] line names the hole; the suite still counts
                // green because its other three probes DID assert. PartialSkip is correct here
                // rather than Skip: this is one section standing down, not the whole suite.
                string note = DeNelle.Editor.Regression.RegressionOutcome.PartialSkip(
                    "[destroyed-structure] C. object + bound-vendor removal",
                    "edit-mode batch — Destructible.NotifyBroken removal needs the live PlacementGrid/" +
                    "GameStateService singletons (Awake runs only in play mode) and a runtime Destroy; " +
                    "drive from the AutoPilot/play harness to assert grid-free + record-drop + vendor despawn");
                log.AppendLine("  " + note);
                if (notes != null) notes.Add(note);
                return;
            }

            var building = new GameObject("Removal_Building"); created.Add(building);
            var placed = building.AddComponent<PlacedStructure>();
            placed.itemId = "arcane_tower";
            placed.gridCell = new Vector2Int(3, 3);
            placed.footprint = Vector2Int.one;

            var vendorGo = new GameObject("Removal_Vendor");   // NOT added to `created` — death must destroy it
            var seat = building.AddComponent<VendorSeatMarker>();
            seat.Vendor = vendorGo;

            var d = Destructible.Ensure(building);
            d.NotifyBroken("regression-death");

            // Vendor ref cleared (and the vendor GameObject scheduled for destruction).
            if (seat.Vendor != null)
                failures.Add("NotifyBroken did not clear the bound VendorSeatMarker.Vendor ref on death");
            else
                log.AppendLine("  ObjectRemoval: bound vendor ref cleared on death (vendor despawned).");

            log.AppendLine("  ObjectRemoval: NotifyBroken invoked in play mode — grid-free + record-drop exercised " +
                           "(assert live PlacementGrid.CanPlace + BaseLayout record absence in the play harness).");
        }

        // =====================================================================
        //  D. WO-843 — destroyed singleton: the build card returns to BUILDABLE
        //     while the resurfaced baked twin stands (owner F8 2026-08-02
        //     "lumber mill destroyed no option to rebuild it"). The card gate
        //     (BuildModeController.IsSingletonBuilt -> StructureSingleton.
        //     IsPlayerBuilt) must ignore the WO-819 twin; the ENFORCEMENT query
        //     (IsBuilt) must still see it; the free-build burn stays idempotent
        //     (rebuild at full cost, never free); the monotonic ever-built set
        //     keeps the resurface gate open (WO-834).
        // =====================================================================
        private static void ProbeRebuildCardState(List<string> failures, StringBuilder log)
        {
            const string Id = "collector_lumbermill";
            const string TwinName = "Lumbermill_Wood_Storefront";

            var entry = DeNelle.Core.Catalog.CatalogRegistry.Get(Id);
            if (entry == null || entry.repo == null || !entry.repo.singleton)
            { failures.Add($"WO-843: catalog row '{Id}' missing or not repo.singleton - the rebuild-card probe lost its subject"); return; }
            bool twinAuthored = false;
            if (entry.repo.bakedTwins != null)
                foreach (var t in entry.repo.bakedTwins) if (t == TwinName) { twinAuthored = true; break; }
            if (!twinAuthored)
            { failures.Add($"WO-843: catalog row '{Id}' no longer names baked twin '{TwinName}' - re-point this probe"); return; }

            // Headless GameStateService install (the VillageEconomyRegression pattern -
            // editmode batch never runs Awake, so seat the singleton + state by reflection).
            var instField = typeof(DeNelle.Core.State.GameStateService).GetField("_instance", BindingFlags.NonPublic | BindingFlags.Static);
            var stateField = typeof(DeNelle.Core.State.GameStateService).GetField("_state", BindingFlags.NonPublic | BindingFlags.Instance);
            if (instField == null || stateField == null)
            { log.AppendLine("  RebuildCard (WO-843): SKIPPED - GameStateService reflection seams moved (see VillageEconomyRegression)"); return; }

            var prior = DeNelle.Core.State.GameStateService.Instance;
            GameObject gssGo = null, twinGo = null;
            DeNelle.Core.State.GameState throwaway = null;
            try
            {
                throwaway = ScriptableObject.CreateInstance<DeNelle.Core.State.GameState>();
                gssGo = new GameObject("GameStateService (rebuild-card oracle)");
                var gss = gssGo.AddComponent<DeNelle.Core.State.GameStateService>();
                stateField.SetValue(gss, throwaway);
                instField.SetValue(null, gss);
                var state = gss.State;

                // (1) PLACED: a persisted record -> both queries read built (card correctly "Built").
                state.BaseLayout.Add(new DeNelle.Core.State.PlacedStructureData(Id, 2, 2, 0, 1));
                if (!StructureSingleton.IsPlayerBuilt(Id))
                    failures.Add("WO-843 (1): a persisted BaseLayout record did not read as player-built - the palette would offer a duplicate of a placed singleton");
                if (!StructureSingleton.IsBuilt(Id))
                    failures.Add("WO-843 (1): a persisted BaseLayout record did not read as built on the enforcement query");

                // (2) DESTROYED: record dropped (the WO-753 death path) + twin RESURFACED
                //     (WO-819) - the exact captured F8 state. Enforcement still sees the twin;
                //     the CARD must read BUILDABLE.
                state.BaseLayout.Clear();
                twinGo = new GameObject(TwinName);   // active = the resurfaced baked twin
                if (!StructureSingleton.IsBuilt(Id))
                    failures.Add("WO-843 (2): an ACTIVE baked twin did not register on the enforcement query (IsBuilt) - stand-down/resurface would misfire");
                if (StructureSingleton.IsPlayerBuilt(Id))
                    failures.Add("WO-843 (2): THE CAPTURED BUG - with only the resurfaced baked twin standing, IsPlayerBuilt read true, so the build card locks as 'Built' and the destroyed structure can never be rebuilt");

                // (3) NO FREEBIE: destruction burns the free-build flag (WO-753), idempotently.
                var burn = typeof(Destructible).GetMethod("BurnFreeBuild", BindingFlags.NonPublic | BindingFlags.Static);
                if (burn == null)
                { failures.Add("WO-843 (3): Destructible.BurnFreeBuild seam moved - re-point this probe"); }
                else
                {
                    burn.Invoke(null, new object[] { Id });
                    burn.Invoke(null, new object[] { Id });   // second burn must not double-record
                    int count = 0;
                    if (state.FreeBuildsUsed != null)
                        foreach (var f in state.FreeBuildsUsed)
                            if (string.Equals(f, Id, System.StringComparison.OrdinalIgnoreCase)) count++;
                    if (count != 1)
                        failures.Add($"WO-843 (3): FreeBuildsUsed holds '{Id}' x{count} after a double burn - expected exactly 1 (rebuild charges full cost; once burned stays burned)");
                }

                // (4) The MONOTONIC ever-built set keeps the WO-834 resurface gate open - the
                //     card fix must never be "solved" by clearing it (WO-819 contract).
                if (state.EverBuiltStructureIds == null)
                    state.EverBuiltStructureIds = new List<string>();
                state.EverBuiltStructureIds.Add(Id);
                if (!StructureSingleton.MayBakedTwinSurface(Id, state.EverBuiltStructureIds, true))
                    failures.Add("WO-843 (4): MayBakedTwinSurface false for an ever-built id on a migrated save - the WO-819 resurface contract broke");

                log.AppendLine("  RebuildCard (WO-843): record => built on both queries; twin-only => enforcement built, CARD buildable; free-build burn idempotent; ever-built resurface gate intact.");
            }
            finally
            {
                if (twinGo != null) Object.DestroyImmediate(twinGo);
                if (gssGo != null) Object.DestroyImmediate(gssGo);
                if (throwaway != null) Object.DestroyImmediate(throwaway);
                instField.SetValue(null, prior);
            }
        }

        // =====================================================================
        //  E. A RELOAD MUST NOT HALF-REVIVE A DESTROYED COLLECTOR (WO-1865)
        //     Owner F8 2026-09-18: "the wave report said the Forge and Lumber Mill were
        //     destroyed but both are standing and built". Proven from the device log
        //     (Logs/device/raid-trace-20260918-080200.txt): the pair broke honestly at
        //     07:27:13 / 07:28:26 (hp=0.00 broken=True), and at the next scene load the
        //     RepairProbe printed BURNING 'forge' hp=1.00 broken=True - FULL HP while
        //     flagged destroyed. ResourceCollector.Awake/Configure ran
        //         LoadState();                      // _broken = _hp <= 0f  -> TRUE
        //         if (_hp <= 0f) _hp = _maxHp;       // ...and undid the evidence
        //     two mutually contradictory lines from the same birth commit (b08293c93).
        //     In that state IsAlive/IsActive are false (no accrual), Repair() no-ops on
        //     the WO-753 guard so nothing can clear it, and WaveDamageReport - reading
        //     IsBroken LIVE, correctly - reported "2 destroyed" at every wave clear for
        //     the next 31 minutes. THE REPORT WAS NEVER THE DEFECT; this is.
        //
        //     RED ON HEAD BEFORE THE FIX: case 1 read hpFrac=1.00 with IsBroken true.
        //     Case 2 is the GREEN half of the pair - a genuinely uninitialised collector
        //     (no persisted pref) must still come up at FULL health, so the fix cannot be
        //     "never seed HP". Case 3 pins that the report's own predicate is unchanged.
        // =====================================================================
        private static void ProbeReloadDoesNotHalfReviveCollector(
            List<GameObject> created, List<string> failures, StringBuilder log)
        {
            log.AppendLine("E. reload does not half-revive a destroyed collector (WO-1865)");

            const string BrokenId = "wo1861_broken_collector";
            const string FreshId = "wo1861_fresh_collector";
            string brokenKey = DeNelle.Core.State.GameStateService.CollectorHpPrefPrefix + BrokenId;
            string freshKey = DeNelle.Core.State.GameStateService.CollectorHpPrefPrefix + FreshId;
            bool hadBroken = PlayerPrefs.HasKey(brokenKey);
            float priorBroken = PlayerPrefs.GetFloat(brokenKey, 0f);
            bool hadFresh = PlayerPrefs.HasKey(freshKey);
            float priorFresh = PlayerPrefs.GetFloat(freshKey, 0f);

            try
            {
                // ── Case 1 (the captured bug): a PERSISTED-DESTROYED collector reloads ──
                PlayerPrefs.SetFloat(brokenKey, 0f);
                PlayerPrefs.Save();
                var go1 = new GameObject("WO1865_Broken_Collector"); created.Add(go1);
                var broken = go1.AddComponent<ResourceCollector>();
                broken.Configure(BrokenId);   // LoadState -> hp 0 -> _broken; then the seed decides
                bool brokenFlag = broken.IsBroken;
                float brokenHp = broken.HpFraction;
                log.AppendLine($"  case1 persisted-destroyed reload: IsBroken={brokenFlag} hpFrac={brokenHp:0.000}");
                if (!brokenFlag)
                    failures.Add("WO-1865 (1): a collector with a persisted hp=0 did not load as broken - " +
                                 "the destroyed fact is no longer persisted at all");
                else if (brokenHp > 0.0001f)
                    failures.Add($"WO-1865 (1): THE CAPTURED BUG - a destroyed collector reloaded at " +
                                 $"hpFrac={brokenHp:0.000} while IsBroken stayed true. FULL HP + broken is an " +
                                 "impossible state: Repair() no-ops on the WO-753 guard so nothing can ever " +
                                 "clear it, and the wave-clear damage report reads DESTROYED forever while " +
                                 "the building stands at full health (owner F8 2026-09-18, device line " +
                                 "BURNING 'forge' hp=1.00 broken=True).");

                // ── Case 2: an UNINITIALISED collector must still come up at full HP ──
                PlayerPrefs.DeleteKey(freshKey);
                PlayerPrefs.Save();
                var go2 = new GameObject("WO1865_Fresh_Collector"); created.Add(go2);
                var fresh = go2.AddComponent<ResourceCollector>();
                fresh.Configure(FreshId);
                log.AppendLine($"  case2 no persisted pref: IsBroken={fresh.IsBroken} hpFrac={fresh.HpFraction:0.000}");
                if (fresh.IsBroken || fresh.HpFraction < 0.9999f)
                    failures.Add($"WO-1865 (2): a collector with NO persisted HP pref came up " +
                                 $"broken={fresh.IsBroken} hpFrac={fresh.HpFraction:0.000} - the fix over-reached: " +
                                 "the post-load seed must still give an uninitialised collector full health, " +
                                 "it must only stop reviving a collector that is flagged destroyed.");

                // ── Case 3: WaveDamageReport's OWN predicate, unchanged, on both states ──
                float reportedBroken = broken.IsBroken ? 1f : 1f - broken.HpFraction;
                float reportedFresh = fresh.IsBroken ? 1f : 1f - fresh.HpFraction;
                log.AppendLine($"  case3 WaveDamageReport predicate: destroyed-row frac={reportedBroken:0.000} " +
                               $"pristine-row frac={reportedFresh:0.000}");
                if (reportedBroken < 0.9999f)
                    failures.Add($"WO-1865 (3): the report predicate scored a destroyed collector at " +
                                 $"{reportedBroken:0.000} - it must stay 1.000 (destroyed IS destroyed; the " +
                                 "report was never the defect and must not be 'fixed')");
                if (reportedFresh > 0.0001f)
                    failures.Add($"WO-1865 (3): the report predicate scored a PRISTINE collector at " +
                                 $"{reportedFresh:0.000} - a healthy structure must produce no row at all");
            }
            finally
            {
                if (hadBroken) PlayerPrefs.SetFloat(brokenKey, priorBroken); else PlayerPrefs.DeleteKey(brokenKey);
                if (hadFresh) PlayerPrefs.SetFloat(freshKey, priorFresh); else PlayerPrefs.DeleteKey(freshKey);
                // The probe's two synthetic ids are not real buildings - leave no pending/away
                // keys behind for GameStateService's collector-id index to enumerate.
                foreach (var id in new[] { BrokenId, FreshId })
                {
                    PlayerPrefs.DeleteKey(DeNelle.Core.State.GameStateService.CollectorPendingPrefPrefix + id);
                    PlayerPrefs.DeleteKey(DeNelle.Core.State.GameStateService.CollectorLastAccrualPrefPrefix + id);
                }
                PlayerPrefs.Save();
            }
        }

        // Editor-test seeding: set a private serialized float when Awake/Configure did not run.
        private static void SeedPrivateFloat(object target, string field, float value)
        {
            var f = target.GetType().GetField(field, BindingFlags.NonPublic | BindingFlags.Instance);
            if (f != null) f.SetValue(target, value);
        }

        // Editor-test seeding: set a private serialized bool when Awake/Configure did not run.
        private static void SeedPrivateBool(object target, string field, bool value)
        {
            var f = target.GetType().GetField(field, BindingFlags.NonPublic | BindingFlags.Instance);
            if (f != null) f.SetValue(target, value);
        }

        // =====================================================================
        //  Verdict + markers
        // =====================================================================
        private static bool Verdict(List<string> failures, StringBuilder log, List<string> notes, out string reason)
        {
            if (failures.Count == 0)
            {
                reason = "DESTROYED STRUCTURE OK — Repair() no-ops on every destroyed tower/spire/collector/wall, " +
                         "the Repair-All exclusion predicates (IsBroken / DamageFraction>=DestroyedFraction) fire on destroyed structures, " +
                         "the WO-843 rebuild-card state holds (twin-only => card buildable at full cost), " +
                         "and a reload does NOT half-revive a destroyed collector (WO-1865: no hp=1.00 + broken=True)";
                if (notes != null && notes.Count > 0)
                    reason += " || " + string.Join(" || ", notes.ToArray());
                Debug.Log("DESTROYED_STRUCTURE_OK\n" + log);
                return true;
            }
            reason = $"DESTROYED STRUCTURE: {failures.Count} failure(s): " + string.Join(" | ", failures);
            Debug.LogError($"DESTROYED_STRUCTURE_FAIL: {failures.Count} failure(s)\n" + log +
                           "\n - " + string.Join("\n - ", failures));
            return false;
        }
    }
}

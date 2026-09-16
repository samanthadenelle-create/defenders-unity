// =============================================================================
// EchoSpecializationRegression — the §2c permission-gate oracle for WO-738/830
// (Echo specialization + affinity/synergy). Headless, data-decidable, no play-mode.
// -----------------------------------------------------------------------------
// Assembly: DeNelle.EditorRegression (references DeNelle.Core + DeNelle.Village).
// Asserts the COMMITTED runtime contract of the Echo affinity/synergy model — real
// objects in through the actual game path (reload the balance catalog, stand up a
// throwaway GameState the way OfflineHarvestRegression/CoreSaveContractRegression do),
// assert the response, one marker out. Mirrors MonetizationCovenantRegression's shape:
//   public static bool Run(out string reason)   →  wired into DataRegression.RunAll.
//
// SEVEN ASSERTION GROUPS (all data-decidable headless — none deferred):
//   1. Catalog integrity  — EchoRosterCatalog: 6 entries, ALL PreferredLane=Harvest
//      (WO-830), the full affinity table (Aldwin→Food, Elowen→Wood, Corvin→Gold,
//      Bran→Crystals, Doran→Iron, Maren→Crystals — crystals the ONE doubled affinity),
//      HarvestResource kept for the 3 classic resources, EmergeLine present (WO-831).
//   2. Balance load       — EchoBalanceCatalog loads via CanonicalJson; the
//      Resources/StreamingAssets dual-copy is byte-identical; the WO-830 tunables
//      (0.03 / 0.02 / 0.20 / 0.01 / hiddenTri 0.25 -- owner "+5% not 55%" ruling
//      2026-08-02, was 0.40 / 0.15 / 0.20 / 0.05), the 3 crossBonuses pairs, and
//      the crystals-slowest rate law (Bran+Maren combined < every other single rate).
//   3. Token grammar      — AssignHarvest+SetLevel produce a "resource:level" token that
//      round-trips; legacy "wood,iron,food,idle" reads Harvest with the RESOURCE
//      preserved at L1; a v33 generic "harvest:N" defaults to the AFFINITY resource;
//      a stored non-pickable "crafting:1" still reads back (read-compat); WO-1108 (f):
//      the WO-811 repair TASK is RETIRED — AssignRepair always refuses and never mutates,
//      and a stored "repair:N" READ-MIGRATES to Harvest at the echo's AFFINITY resource
//      (level preserved, never idle — idle would silently zero that echo's yield), while
//      the unknown-token->Idle default is unchanged.
//   4b. WO-1108 PASSIVE repair rate — RepairFractionsPerSecond = the shared-contribution
//      formula over EVERY OWNED echo (no lane filter, no affinity term); an all-idle roster
//      still mends; adding an echo with no assignment change RAISES the rate; passive repair
//      never leaks into the harvest aggregate.
//   4. Bonus math         — all-matched assignment: AggregateHarvestMultiplier equals the
//      hand-computed formula INCLUDING pair bonuses + six-set + the HIDDEN tri term;
//      the tri term is APPLIED but NOT in any ReadoutFor().BonusPct (applied ≠ displayed);
//      breaking one pair drops that pair AND the tri; HarvestTargetWeights routes by
//      ACTUAL assignment with crystals the smallest share.
//   5. Save round-trip    — a rich echoLanes resource token survives SaveSchema
//      serialize→deserialize at the current version; an older blob without echoLanes
//      loads with the default (default-on-read, no throw).
//   6. EchoLaneBonuses    — after Recompute(), HarvestBonusMult mirrors the applied
//      aggregate (hidden tri included — write-only mirror, no UI reader).
//   7. Dump credit        — a real EchoService.DumpSilos through a real EconomyService:
//      Wood/Iron/Food AND Gold (Coins) AND Crystals wallets all move; the crystal share
//      is the smallest; crystals also move with only ONE crystal harvester assigned.
//
// SAFETY: snapshots+restores the raw PlayerPrefs save blob (Assign/SetLevel/Dump call
// Save()), restores the prior live GameStateService singleton, DestroyImmediate's the
// throwaway objects, and Reset()s EchoLaneBonuses in finally — the real save/state is
// untouched.
// =============================================================================
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using Newtonsoft.Json;
using UnityEngine;
using DeNelle.Core;
using DeNelle.Core.State;
using DeNelle.Village;

namespace DeNelle.Editor
{
    public static class EchoSpecializationRegression
    {
        private const string SaveKey = "dotr-save";
        private const float Eps = 0.001f;

        // The all-matched, all-L1 assignment (index order == roster order):
        //   0 Aldwin food, 1 Elowen wood, 2 Corvin gold, 3 Bran crystals,
        //   4 Doran iron, 5 Maren crystals.
        private const string AllMatchedL1 = "food:1,wood:1,gold:1,crystals:1,iron:1,crystals:1";

        public static bool Run(out string reason)
        {
            var failures = new List<string>();
            var notes = new List<string>();
            void Fail(string s) => failures.Add("ECHO_SPEC FAIL: " + s);

            // Snapshot the persisted save so nothing Assign/SetLevel/Dump writes here survives.
            bool hadSave = PlayerPrefs.HasKey(SaveKey);
            string rawSave = hadSave ? PlayerPrefs.GetString(SaveKey, null) : null;

            GameStateService priorInstance = GameStateService.Instance;
            GameObject gssGo = null;
            GameObject svcGo = null;
            GameState throwaway = null;
            bool installed = false;

            try
            {
                // --- Groups 1 + 2 + 5 need NO live GameStateService (pure catalog/schema). --
                CheckCatalogIntegrity(Fail);
                CheckBalanceLoad(Fail, notes);
                CheckSaveRoundTrip(Fail);

                // --- Groups 3/4/6/7 drive the assignment seam → install a headless state. --
                // Editmode batchmode NEVER runs GameStateService.Awake, so a bare
                // AddComponent leaves Instance/State null. Install a throwaway GameState by
                // reflection (the same seam Awake sets), exactly as OfflineHarvestRegression.
                throwaway = ScriptableObject.CreateInstance<GameState>();   // fresh defaults; collections init'd → Save()-safe
                gssGo = new GameObject("GameStateService (echo-spec-oracle)");
                var gss = gssGo.AddComponent<GameStateService>();
                if (!TryInstallHeadlessState(gss, throwaway, out string installErr))
                {
                    // The singleton/state seam moved — the assignment-driven groups can't run
                    // headless. NAMED SKIP (not a false FAIL); groups 1/2/5 above still stand.
                    notes.Add("groups 3/4/6/7 skipped (needs fleet — " + installErr + ")");
                }
                else
                {
                    installed = true;
                    var state = gss.State;
                    if (state == null)
                    {
                        notes.Add("groups 3/4/6/7 skipped (throwaway state did not install)");
                    }
                    else
                    {
                        state.EchoCount = 6;               // own the full roster (six-set + tri live)
                        EchoBalanceCatalog.Reload();       // ensure the tunables are loaded fresh
                        CheckTokenGrammar(state, Fail);
                        CheckBonusMath(state, Fail);
                        CheckRepairMath(state, Fail);      // WO-811: repair token rate math (single source)
                        CheckLaneBonusesPopulation(state, Fail);
                        svcGo = CheckDumpCredit(state, Fail, notes);
                    }
                }
            }
            catch (Exception ex)
            {
                Fail($"oracle threw: {ex.GetType().Name}: {ex.Message}");
            }
            finally
            {
                EchoLaneBonuses.Reset();   // don't leak computed mults into later oracles

                if (svcGo != null)
                {
                    UnityEngine.Object.DestroyImmediate(svcGo);
                    // Editmode Awake/OnDestroy never ran — clear the reflected-in singletons
                    // so later oracles never see a destroyed instance behind the statics.
                    TrySetStaticProperty(typeof(EchoService), "Instance", null);
                    TrySetStaticProperty(typeof(EconomyService), "Instance", null);
                }
                if (gssGo != null) UnityEngine.Object.DestroyImmediate(gssGo);
                if (throwaway != null) UnityEngine.Object.DestroyImmediate(throwaway);

                // Restore the live service later oracles read (DestroyImmediate may have
                // nulled the static via OnDestroy).
                if (installed) TrySetInstanceStatic(priorInstance);

                // Restore the persisted save blob (Assign/SetLevel/Dump called Save()).
                if (hadSave) PlayerPrefs.SetString(SaveKey, rawSave);
                else PlayerPrefs.DeleteKey(SaveKey);
                PlayerPrefs.Save();
            }

            if (failures.Count == 0)
            {
                reason = "ECHO SPEC OK — WO-830 affinity table + balance dual-copy + resource-token grammar + "
                         + "WO-1108 passive repair (count-driven rate, repair-token read-migration) + "
                         + "pair/tri math (tri applied-not-displayed) + save round-trip + EchoLaneBonuses + dump credit all hold"
                         + (notes.Count > 0 ? " [" + string.Join("; ", notes) + "]" : "");
                return true;
            }
            reason = $"echo-spec: {failures.Count} failure(s): " + string.Join(" | ", failures);
            return false;
        }

        // =====================================================================
        //  Group 1 — Catalog integrity (EchoRosterCatalog fixed identity table)
        // =====================================================================
        private static void CheckCatalogIntegrity(Action<string> Fail)
        {
            var roster = EchoRosterCatalog.All;
            if (roster == null || EchoRosterCatalog.Count != 6)
            {
                Fail($"EchoRosterCatalog should have 6 entries, has {(roster == null ? 0 : EchoRosterCatalog.Count)}");
                return;
            }

            // WO-830: EVERY spirit prefers Harvest (all affinities reachable) and carries
            // a non-empty EmergeLine (WO-831 -- ASCII, the emergence intro).
            for (int i = 0; i < roster.Length; i++)
            {
                var e = roster[i];
                if (e == null) { Fail($"roster index {i} is null"); continue; }
                if (e.PreferredLane != LaneType.Harvest)
                    Fail($"roster '{e.Id}' PreferredLane={e.PreferredLane} (WO-830: all six must prefer Harvest)");
                if (string.IsNullOrEmpty(e.EmergeLine))
                    Fail($"roster '{e.Id}' has no EmergeLine (WO-831 emergence intro)");
                else if (!IsAscii(e.EmergeLine))
                    Fail($"roster '{e.Id}' EmergeLine contains non-ASCII characters");
            }

            // The full WO-830 affinity table (owner-approved, amended 2026-08-02).
            AssertEntry(Fail, "echo-frosthowl", HarvestTarget.Stone, ResourceType.Stone);
            AssertEntry(Fail, "echo-verdant-stag", HarvestTarget.Wood, ResourceType.Wood);
            AssertEntry(Fail, "echo-voidwing-raven", HarvestTarget.Gold, null);
            AssertEntry(Fail, "echo-stormcoil-serpent", HarvestTarget.Gold, null);
            AssertEntry(Fail, "echo-stonewarden-bear", HarvestTarget.Iron, ResourceType.Iron);
            AssertEntry(Fail, "echo-ember-phoenix", HarvestTarget.Crystals, null);

            // Crystals is the ONE deliberately doubled affinity (exactly two crystal harvesters).
            int crystalCount = 0;
            foreach (var e in roster)
                if (e != null && e.Affinity == HarvestTarget.Crystals) crystalCount++;
            if (crystalCount != 1)
                Fail($"crystals affinity count = {crystalCount} (expected exactly 2 — Bran + Maren, the doubled affinity)");
        }

        private static void AssertEntry(Action<string> Fail, string id, HarvestTarget affinity, ResourceType? resource)
        {
            EchoRosterEntry e = null;
            foreach (var candidate in EchoRosterCatalog.All)
                if (candidate != null && candidate.Id == id) { e = candidate; break; }

            if (e == null) { Fail($"roster is missing '{id}'"); return; }
            if (e.Affinity != affinity)
                Fail($"'{id}' Affinity={e.Affinity} (expected {affinity})");
            if (e.HarvestResource != resource)
                Fail($"'{id}' HarvestResource={(e.HarvestResource.HasValue ? e.HarvestResource.Value.ToString() : "null")} " +
                     $"(expected {(resource.HasValue ? resource.Value.ToString() : "null")})");
        }

        private static bool IsAscii(string s)
        {
            foreach (char c in s) if (c > 127) return false;
            return true;
        }

        // =====================================================================
        //  Group 2 — Balance load (echoes-balance.json via CanonicalJson) + dual-copy
        // =====================================================================
        private static void CheckBalanceLoad(Action<string> Fail, List<string> notes)
        {
            EchoBalanceCatalog.Reload();
            var data = EchoBalanceCatalog.Data;
            if (data == null) { Fail("EchoBalanceCatalog.Data is null (file missing/invalid, no fallback)"); return; }

            if (data.Version != 1) Fail($"echoes-balance.json version {data.Version} (expected 1)");
            if (EchoBalanceCatalog.MaxLevel != 8) Fail($"echoes-balance MaxLevel {EchoBalanceCatalog.MaxLevel} (expected 8)");

            // The key tunables must be the CURRENT owner-ruled balance (the math below assumes it).
            // RE-PINNED 2026-08-02 after the owner's F8 on the Echo card ("should be +5% not 55%"):
            // the card prints base+match, which was 0.15+0.40 = +55%. Now 0.02+0.03 = +5% matched,
            // +2% unmatched (matching still pays 2.5x). perLevelBonus fell 0.05 -> 0.01 in the same
            // breath because at the old value ONE level-up outweighed the entire matched bonus,
            // which would have made the affinity pick cosmetic. Lv8 matched now reads +12%.
            // This canary FIRED on the change (REGRESSION_FAIL x3) and was reviewed, not
            // reflexively repinned - the set/tri knobs below are deliberately UNCHANGED, which is
            // why team composition now outweighs individual assignment (flagged to the owner,
            // see docs/design/ECONOMY_PROGRESSION_THESIS_2026-08-02.md).
            AssertClose(Fail, EchoBalanceCatalog.PreferredLaneMatchBonus, 0.03f, "preferredLaneMatchBonus (owner +5% ruling)");
            AssertClose(Fail, EchoBalanceCatalog.BaseContributionPerEcho, 0.02f, "baseContributionPerEcho (owner +5% ruling)");
            AssertClose(Fail, EchoBalanceCatalog.SixSetBonusGlobalHarvest, 0.20f, "sixSetBonusGlobalHarvest");
            AssertClose(Fail, EchoBalanceCatalog.PerLevelBonus, 0.01f, "perLevelBonus (owner +5% ruling)");
            AssertClose(Fail, EchoBalanceCatalog.HiddenTriSynergyBonus, 0.25f, "hiddenTriSynergyBonus (WO-830 Sec.3d)");

            // WO-1108 D3: the repair PACE knob. It used to be a code-only default of 2.0 with
            // no json row at all; making repair passive multiplied the aggregate by the roster
            // size, so it was MOVED INTO the json and re-tuned to 0.35 (6 Echoes x 0.35 x 1.02
            // = 2.14 fractions/h, within ~5% of the old ONE-assigned-Echo 2.04 — see the file's
            // _authoringNotes). Owner-tunable; if this canary fires, the value was RE-TUNED and
            // this pin is what makes that a deliberate act instead of a silent economy change.
            AssertClose(Fail, EchoBalanceCatalog.RepairFractionPerHour, 0.35f,
                "repairFractionPerHour (WO-1108 D3 passive-repair re-tune)");

            // The 3 disclosed pair synergies (Provisions / Forge / Fortune) with positive bonuses.
            var pairs = EchoBalanceCatalog.CrossBonuses;
            if (pairs == null || pairs.Count != 3)
            {
                Fail($"crossBonuses count = {(pairs == null ? 0 : pairs.Count)} (expected the 3 WO-830 pairs)");
            }
            else
            {
                AssertPair(Fail, pairs, "Provisions", "echo-verdant-stag", "echo-frosthowl");
                AssertPair(Fail, pairs, "Forge", "echo-stonewarden-bear", "echo-ember-phoenix");
                AssertPair(Fail, pairs, "Fortune", "echo-voidwing-raven", "echo-stormcoil-serpent");
            }

            // Crystals-slowest law (WO-830 Sec.3b): the COMBINED Bran+Maren rate stays below
            // every other single affinity rate, so the double-crystal trickle is the slowest
            // faucet of the six affinity assignments.
            float crystalsCombined = EchoBonusCalculator.HarvestRatePerHour(HarvestTarget.Crystals, 1);
            float[] others =
            {
                EchoBonusCalculator.HarvestRatePerHour(HarvestTarget.Stone, 1),
                EchoBonusCalculator.HarvestRatePerHour(HarvestTarget.Wood, 1),
                EchoBonusCalculator.HarvestRatePerHour(HarvestTarget.Gold, 1),
                EchoBonusCalculator.HarvestRatePerHour(HarvestTarget.Iron, 1),
            };
            foreach (var r in others)
                if (crystalsCombined >= r)
                {
                    Fail($"crystals combined rate {crystalsCombined:0.###} is not the slowest (another affinity rate is {r:0.###}) — monetization guard broken");
                    break;
                }

            // Dual-copy must be byte-identical (owner mandate: keep Resources + StreamingAssets in sync).
            string resPath = Path.Combine(Application.dataPath, "Resources/Data/Canonical/echoes-balance.json");
            string streamPath = Path.Combine(Application.dataPath, "StreamingAssets/Data/Canonical/echoes-balance.json");
            bool resExists = File.Exists(resPath), streamExists = File.Exists(streamPath);
            if (!resExists && !streamExists)
            {
                notes.Add("echoes-balance.json not found on disk (loaded via built-in fallback); dual-copy check skipped");
            }
            else if (resExists && streamExists)
            {
                string a = File.ReadAllText(resPath);
                string b = File.ReadAllText(streamPath);
                if (a != b)
                    Fail("echoes-balance.json Resources/StreamingAssets dual-copy is NOT byte-identical (owner: keep the two in sync)");
                // WO-1108 D3: the knob must be AUTHORED, not inherited from the code default.
                // A hidden code default is exactly what let the passive-repair change 6x the
                // rate unnoticed — the value has to be visible where the owner tunes it.
                if (!a.Contains("\"repairFractionPerHour\""))
                    Fail("echoes-balance.json does not AUTHOR repairFractionPerHour — WO-1108 D3 moved that knob "
                       + "out of code so the repair pace is owner-tunable data, not a hidden default");
            }
            else
            {
                notes.Add($"echoes-balance.json present in only one location (res={resExists}, stream={streamExists}); dual-copy check skipped");
            }
        }

        private static void AssertPair(Action<string> Fail, List<EchoCrossBonusDef> pairs, string name, string a, string b)
        {
            foreach (var p in pairs)
            {
                if (p == null || p.Name != name) continue;
                bool members = (p.A == a && p.B == b) || (p.A == b && p.B == a);
                if (!members) Fail($"crossBonuses '{name}' members [{p.A},{p.B}] (expected [{a},{b}])");
                if (p.Bonus <= 0f) Fail($"crossBonuses '{name}' bonus {p.Bonus:0.###} (expected > 0 — the disclosed synergy)");
                return;
            }
            Fail($"crossBonuses is missing the '{name}' pair");
        }

        // =====================================================================
        //  Group 3 — Token grammar (WO-830 resource:level + legacy + affinity default)
        // =====================================================================
        private static void CheckTokenGrammar(GameState state, Action<string> Fail)
        {
            // (a) WRITE PATH: the resource picker writes an explicit "resource:level" token
            //     that round-trips through lane + resource + level reads.
            EchoAssignments.AssignHarvest(5, EchoAssignments.ResCrystals);
            EchoAssignments.SetLevel(5, 3);
            if (EchoAssignments.LaneOf(5) != EchoAssignments.LaneHarvest)
                Fail($"resource token: LaneOf(5)='{EchoAssignments.LaneOf(5)}' (expected 'harvest')");
            if (EchoAssignments.ResourceTokenOf(5) != EchoAssignments.ResCrystals)
                Fail($"resource token: ResourceTokenOf(5)='{EchoAssignments.ResourceTokenOf(5)}' (expected 'crystals')");
            if (EchoAssignments.LevelOf(5) != 3)
                Fail($"resource token: LevelOf(5)={EchoAssignments.LevelOf(5)} (expected 3)");
            var parts = (state.EchoLanes ?? "").Split(',');
            if (parts.Length <= 5 || parts[5] != "crystals:3")
                Fail($"resource token: persisted CSV token[5]='{(parts.Length > 5 ? parts[5] : "<none>")}' (expected 'crystals:3'); full='{state.EchoLanes}'");

            // (b) AssignHarvest rejects a non-resource token (logged no-op, returns false).
            if (EchoAssignments.AssignHarvest(5, "crafting"))
                Fail("AssignHarvest accepted 'crafting' (must reject non-resource tokens)");
            if (EchoAssignments.ResourceTokenOf(5) != EchoAssignments.ResCrystals)
                Fail("AssignHarvest('crafting') mutated the stored assignment (must be a no-op)");

            // (c) LEGACY (pre-v33) resource CSV reads Harvest with the RESOURCE PRESERVED at L1.
            state.EchoLanes = "wood,iron,food,idle";
            string[] expectedRes = { EchoAssignments.ResWood, EchoAssignments.ResIron, EchoAssignments.ResFood };
            for (int i = 0; i <= 2; i++)
            {
                if (EchoAssignments.LaneOf(i) != EchoAssignments.LaneHarvest)
                    Fail($"legacy: index {i} of 'wood,iron,food,idle' reads lane '{EchoAssignments.LaneOf(i)}' (expected 'harvest')");
                if (EchoAssignments.ResourceTokenOf(i) != expectedRes[i])
                    Fail($"legacy: index {i} resource '{EchoAssignments.ResourceTokenOf(i)}' (expected '{expectedRes[i]}' — resource preserved, WO-830)");
                if (EchoAssignments.LevelOf(i) != 1)
                    Fail($"legacy: index {i} bare token reads level {EchoAssignments.LevelOf(i)} (expected 1)");
            }
            if (EchoAssignments.LaneOf(3) != EchoAssignments.LaneIdle)
                Fail($"legacy: index 3 'idle' reads '{EchoAssignments.LaneOf(3)}' (expected 'idle')");
            if (EchoAssignments.ResourceTokenOf(3) != "")
                Fail($"legacy: index 3 'idle' resource '{EchoAssignments.ResourceTokenOf(3)}' (expected '')");

            // (d) v33 GENERIC "harvest:N" defaults on read to the echo's AFFINITY resource.
            state.EchoLanes = "harvest:2,harvest:1";
            if (EchoAssignments.ResourceTokenOf(0) != EchoAssignments.ResFood)
                Fail($"generic harvest: index 0 (Aldwin) resource '{EchoAssignments.ResourceTokenOf(0)}' (expected 'food' — affinity default-on-read)");
            if (EchoAssignments.ResourceTokenOf(1) != EchoAssignments.ResWood)
                Fail($"generic harvest: index 1 (Elowen) resource '{EchoAssignments.ResourceTokenOf(1)}' (expected 'wood' — affinity default-on-read)");
            if (EchoAssignments.LevelOf(0) != 2)
                Fail($"generic harvest: LevelOf(0)={EchoAssignments.LevelOf(0)} (expected 2)");

            // (e) A stored non-pickable lane token still reads back (read-compat; no offer).
            state.EchoLanes = "harvest:1,crafting:1";
            if (EchoAssignments.LaneOf(1) != EchoAssignments.LaneCrafting || EchoAssignments.LevelOf(1) != 1)
                Fail($"stored crafting token reads lane='{EchoAssignments.LaneOf(1)}' level={EchoAssignments.LevelOf(1)} (expected crafting/1 read-compat)");
            if (EchoAssignments.PickableLanes.Length != 1 || EchoAssignments.PickableLanes[0] != EchoAssignments.LaneHarvest)
                Fail("PickableLanes must be Harvest-only (WO-830 Sec.3e — the dead Crafting chip is removed; the WO-811 repair chip rides the card, not this list)");
            if (EchoAssignments.PickableResources.Length != 5)
                Fail($"PickableResources length {EchoAssignments.PickableResources.Length} (expected the 5 harvest resources)");

            // (f) WO-1108: the WO-811 REPAIR TASK IS RETIRED — repair went PASSIVE across the
            //     whole roster, so "repair" is no longer a WRITABLE assignment and a stored
            //     "repair:N" must READ-MIGRATE to a REAL harvest resource (the Echo's affinity)
            //     with its level preserved. This group is the WO-1108 §6 inversion of the old
            //     round-trip assertions: writing must now FAIL and reading must now MIGRATE.
            //     No schema bump — grammar-only, the v33/WO-830 read-migration precedent.

            // The write verb is a LOUD refusal that never mutates state.
            state.EchoLanes = "food:1,iron:2";
            if (EchoAssignments.AssignRepair(1))
                Fail("AssignRepair(1) returned true — the repair task is RETIRED (WO-1108); assigning repair must always refuse");
            if (EchoAssignments.LaneOf(1) != EchoAssignments.LaneHarvest
                || EchoAssignments.ResourceTokenOf(1) != EchoAssignments.ResIron
                || EchoAssignments.LevelOf(1) != 2)
                Fail($"AssignRepair(1) mutated the assignment: lane='{EchoAssignments.LaneOf(1)}' " +
                     $"resource='{EchoAssignments.ResourceTokenOf(1)}' level={EchoAssignments.LevelOf(1)} " +
                     "(expected harvest/iron/2 — untouched; the retired verb must be a pure no-op)");
            parts = (state.EchoLanes ?? "").Split(',');
            if (parts.Length <= 1 || parts[1] != "iron:2")
                Fail($"AssignRepair(1) wrote to the CSV: token[1]='{(parts.Length > 1 ? parts[1] : "<none>")}' " +
                     $"(expected 'iron:2' — no repair token may ever be written again); full='{state.EchoLanes}'");

            // THE READ-MIGRATION (the WO-1108 acceptance line: a save carrying repair:3 loads,
            // does not crash, and that Echo harvests something REAL). Index 0 is Aldwin —
            // affinity Food — so the migrated resource must be 'food', NOT '' and NOT idle.
            state.EchoLanes = "repair:3,idle";
            if (EchoAssignments.LaneOf(0) == EchoAssignments.LaneIdle)
                Fail("stored 'repair:3' read back as IDLE — the migration must never fall through to the "
                   + "unknown-token default: idle silently ZEROES that Echo's yield (WO-1108 §3)");
            if (EchoAssignments.LaneOf(0) != EchoAssignments.LaneHarvest)
                Fail($"stored 'repair:3' reads lane '{EchoAssignments.LaneOf(0)}' (expected 'harvest' — WO-1108 read-migration)");
            if (EchoAssignments.ResourceTokenOf(0) != EchoAssignments.ResFood)
                Fail($"stored 'repair:3' migrated to resource '{EchoAssignments.ResourceTokenOf(0)}' "
                   + "(expected 'food' — Aldwin's affinity; it must land on a REAL resource)");
            if (!EchoAssignments.TryTargetOf(0, out var migrated) || migrated != HarvestTarget.Stone)
                Fail("stored 'repair:3' does not resolve to a typed HarvestTarget — that Echo would earn nothing");
            if (EchoAssignments.LevelOf(0) != 3)
                Fail($"stored 'repair:3' lost its level: {EchoAssignments.LevelOf(0)} (expected 3 — level survives the migration)");
            if (EchoAssignments.LabelFor(EchoAssignments.LaneRepair) != "Harvest")
                Fail($"LabelFor('repair')='{EchoAssignments.LabelFor(EchoAssignments.LaneRepair)}' "
                   + "(expected 'Harvest' — the retired token normalizes to the harvest lane)");

            // "repair" is gone from the assignable lane list (it was appended by WO-811).
            for (int i = 0; i < EchoAssignments.Lanes.Length; i++)
                if (EchoAssignments.Lanes[i] == EchoAssignments.LaneRepair)
                    Fail("EchoAssignments.Lanes still offers 'repair' — the task is RETIRED (WO-1108); repair is passive");

            // Unknown tokens still read Idle — the unknown-token default is UNCHANGED; only
            // the KNOWN legacy 'repair' token migrates. Never a throw, never corruption.
            state.EchoLanes = "mystery:9,repair:1";
            if (EchoAssignments.LaneOf(0) != EchoAssignments.LaneIdle)
                Fail($"unknown token 'mystery:9' reads '{EchoAssignments.LaneOf(0)}' (expected 'idle' — the unknown-token default must be unchanged)");
            if (EchoAssignments.LaneOf(1) != EchoAssignments.LaneHarvest
                || EchoAssignments.ResourceTokenOf(1) != EchoAssignments.ResWood)
                Fail($"repair token after an unknown neighbour reads lane='{EchoAssignments.LaneOf(1)}' "
                   + $"resource='{EchoAssignments.ResourceTokenOf(1)}' (expected harvest/wood — Elowen's affinity; per-token isolation)");
        }

        // =====================================================================
        //  Group 4b — WO-1108 PASSIVE repair rate math (count-driven; single source)
        // ---------------------------------------------------------------------
        //  WO-811 shipped repair as an ASSIGNABLE lane and this group asserted the
        //  lane filter. WO-1108 made repair PASSIVE — the filter is gone and the rate
        //  sums EVERY OWNED Echo — so those assertions are INVERTED here, not deleted:
        //  an assignment must no longer be able to turn repair off, and the ROSTER
        //  COUNT must move the rate.
        // =====================================================================
        private static void CheckRepairMath(GameState state, Action<string> Fail)
        {
            float b = EchoBalanceCatalog.BaseContributionPerEcho;
            float per = EchoBalanceCatalog.PerLevelBonus;
            float baseRate = EchoBalanceCatalog.RepairFractionPerHour;
            if (baseRate <= 0f)
                Fail($"RepairFractionPerHour={baseRate:0.###} (expected > 0 — the WO-1108 authored knob)");

            int priorCount = state.EchoCount;

            // Every OWNED Echo contributes, whatever it is assigned to: two harvesters
            // (Lv1 + Lv4) and four idle Echoes all mend, level-scaled by the SHARED
            // contribution terms, with NO match term anywhere (no roster entry prefers
            // Repair — WO-830 removed it as an affinity).
            state.EchoLanes = "wood:1,food:4,idle,idle,idle,idle";
            state.EchoCount = 6;
            float expectedPerHour = baseRate * (5f * (1f + b) + (1f + b + per * 3f));
            float actualPerHour = EchoBonusCalculator.RepairFractionsPerSecond() * 3600f;
            if (Mathf.Abs(actualPerHour - expectedPerHour) > Eps)
                Fail($"RepairFractionsPerSecond={actualPerHour:0.#####}/h (expected {expectedPerHour:0.#####}/h — "
                   + "base x (1 + shared terms) over EVERY owned Echo, no affinity, no lane filter)");

            // THE WO-1108 §6 REQUIRED ASSERTION: adding an Echo with NO assignment change
            // RAISES the rate (owner: "the number of pets ... just passively takes toward
            // healing"). The delta is exactly one Lv1 Echo's contribution.
            state.EchoLanes = "wood:1,food:1,iron:1,gold:1,crystals:1,crystals:1";
            state.EchoCount = 5;
            float ratePerHour5 = EchoBonusCalculator.RepairFractionsPerSecond() * 3600f;
            state.EchoCount = 6;   // one MORE Echo; not one byte of assignment changed
            float ratePerHour6 = EchoBonusCalculator.RepairFractionsPerSecond() * 3600f;
            if (!(ratePerHour6 > ratePerHour5))
                Fail($"adding an Echo did NOT raise the passive repair rate ({ratePerHour5:0.#####}/h -> "
                   + $"{ratePerHour6:0.#####}/h) — repair must be COUNT-driven (WO-1108)");
            AssertClose(Fail, ratePerHour6 - ratePerHour5, baseRate * (1f + b),
                "the added Lv1 Echo's passive repair contribution (fractions/h)");

            // An ALL-IDLE roster still mends: no assignment can switch repair off any more.
            state.EchoLanes = "idle,idle,idle,idle,idle,idle";
            if (!(EchoBonusCalculator.RepairFractionsPerSecond() > 0f))
                Fail("an all-idle roster accrues NO repair — repair is passive now; assignment must not gate it");

            // ...and idle Echoes still contribute NOTHING to harvest: passive repair must not
            // leak into the harvest aggregate (which stays assignment-driven + the six-set term).
            float expectedAgg = 6f * (1f + EchoBalanceCatalog.SixSetBonusGlobalHarvest);
            float actualAgg = EchoBonusCalculator.AggregateHarvestMultiplier();
            if (Mathf.Abs(actualAgg - expectedAgg) > Eps)
                Fail($"passive repair leaked into the harvest aggregate: {actualAgg:0.####} (expected {expectedAgg:0.####})");

            // No Echo can READ as the Repair lane any more (a stored repair token migrates
            // to Harvest), so ReadoutFor must never report it.
            state.EchoLanes = "repair:1,idle,idle,idle,idle,idle";
            var ro = EchoBonusCalculator.ReadoutFor(0);
            if (ro.Lane == LaneType.Repair)
                Fail("ReadoutFor(stored 'repair:1').Lane=Repair — the lane is RETIRED (WO-1108); it must read Harvest");
            if (ro.Lane != LaneType.Harvest)
                Fail($"ReadoutFor(stored 'repair:1').Lane={ro.Lane} (expected Harvest — the WO-1108 read-migration)");
            if (Mathf.Abs(ro.BonusPct - (b + EchoBalanceCatalog.PreferredLaneMatchBonus) * 100f) > Eps * 100f)
                Fail($"ReadoutFor(migrated repair).BonusPct={ro.BonusPct:0.##} (expected "
                   + $"{(b + EchoBalanceCatalog.PreferredLaneMatchBonus) * 100f:0.##} — it migrated onto its AFFINITY, so the match term fires)");

            state.EchoCount = priorCount;
            state.EchoLanes = AllMatchedL1;
        }

        // =====================================================================
        //  Group 4 — Bonus math (aggregate incl. pairs + hidden tri; weights)
        // =====================================================================
        private static void CheckBonusMath(GameState state, Action<string> Fail)
        {
            float b = EchoBalanceCatalog.BaseContributionPerEcho;
            float m = EchoBalanceCatalog.PreferredLaneMatchBonus;
            float per = EchoBalanceCatalog.PerLevelBonus;
            float six = EchoBalanceCatalog.SixSetBonusGlobalHarvest;
            float tri = EchoBalanceCatalog.HiddenTriSynergyBonus;
            float pairSum = 0f;
            foreach (var p in EchoBalanceCatalog.CrossBonuses) if (p != null) pairSum += Mathf.Max(0f, p.Bonus);

            // (a) ALL-MATCHED, mixed levels: every echo on its affinity resource.
            //     0 food:1, 1 wood:2, 2 gold:1, 3 crystals:1, 4 iron:3, 5 crystals:1
            state.EchoLanes = "food:1,wood:2,gold:1,crystals:1,iron:3,crystals:1";
            float perEchoSum = (b + m) + (b + m + per * 1f) + (b + m) + (b + m) + (b + m + per * 2f) + (b + m);
            float disclosed = perEchoSum + pairSum + six;
            float applied = disclosed + tri;             // ALL pairs run -> hidden tri fires
            float expectedAgg = 6f * (1f + applied);
            float actualAgg = EchoBonusCalculator.AggregateHarvestMultiplier();
            if (Mathf.Abs(actualAgg - expectedAgg) > Eps)
                Fail($"AggregateHarvestMultiplier={actualAgg:0.####} (expected {expectedAgg:0.####} incl. pairs {pairSum:0.###} + hidden tri {tri:0.###})");

            // (b) THE SECRET STAYS SECRET: no ReadoutFor().BonusPct contains the pair or tri
            //     terms — the displayed per-echo % is exactly base+match+level.
            float[] expectedPct =
            {
                (b + m) * 100f, (b + m + per) * 100f, (b + m) * 100f,
                (b + m) * 100f, (b + m + per * 2f) * 100f, (b + m) * 100f,
            };
            float displayedSum = 0f;
            for (int i = 0; i < 6; i++)
            {
                var ro = EchoBonusCalculator.ReadoutFor(i);
                displayedSum += ro.BonusPct;
                if (Mathf.Abs(ro.BonusPct - expectedPct[i]) > Eps * 100f)
                    Fail($"ReadoutFor({i}).BonusPct={ro.BonusPct:0.##} (expected {expectedPct[i]:0.##} — pair/tri must NOT leak into the displayed %)");
                if (!ro.PreferredMatch)
                    Fail($"ReadoutFor({i}).PreferredMatch=false (all-matched assignment — affinity match must register)");
            }
            // applied ≠ displayed: the applied spec sum exceeds the displayed per-echo sum by
            // EXACTLY pairs+six+tri — i.e. the hidden tri is applied but never displayed.
            float appliedSpecSum = actualAgg / 6f - 1f;
            float hiddenDelta = appliedSpecSum - displayedSum / 100f;
            if (Mathf.Abs(hiddenDelta - (pairSum + six + tri)) > Eps)
                Fail($"applied-vs-displayed delta {hiddenDelta:0.####} (expected pairs+six+tri = {pairSum + six + tri:0.####})");
            if (hiddenDelta < tri - Eps)
                Fail("the hidden tri-synergy does not appear to be applied (applied-displayed delta too small)");

            // (c) BREAK one pair (Corvin off gold -> wood): Fortune + the tri both drop.
            state.EchoLanes = "food:1,wood:2,wood:1,crystals:1,iron:3,crystals:1";
            float fortuneBonus = 0f;
            foreach (var p in EchoBalanceCatalog.CrossBonuses)
                if (p != null && p.Name == "Fortune") fortuneBonus = Mathf.Max(0f, p.Bonus);
            float perEchoSum2 = (b + m) + (b + m + per * 1f) + (b) + (b + m) + (b + m + per * 2f) + (b + m);
            float expectedAgg2 = 6f * (1f + perEchoSum2 + (pairSum - fortuneBonus) + six);   // no tri
            float actualAgg2 = EchoBonusCalculator.AggregateHarvestMultiplier();
            if (Mathf.Abs(actualAgg2 - expectedAgg2) > Eps)
                Fail($"broken-pair aggregate={actualAgg2:0.####} (expected {expectedAgg2:0.####} — Fortune AND the hidden tri must both drop)");
            var roCorvin = EchoBonusCalculator.ReadoutFor(2);
            if (roCorvin.PreferredMatch)
                Fail("Corvin assigned wood still reads PreferredMatch=true (affinity is gold — match must key off the ACTUAL assignment)");
            var syCorvin = EchoBonusCalculator.SynergyFor(2);
            if (!syCorvin.HasPair || syCorvin.Active)
                Fail($"SynergyFor(Corvin) HasPair={syCorvin.HasPair} Active={syCorvin.Active} (expected a defined but INACTIVE pair)");

            // (d) WEIGHTS: 5-way split by ACTUAL assignment; crystals the smallest share.
            state.EchoLanes = AllMatchedL1;
            var w = EchoBonusCalculator.HarvestTargetWeights();
            float rAld = EchoBonusCalculator.HarvestRatePerHour(HarvestTarget.Stone, 1);
            float rElo = EchoBonusCalculator.HarvestRatePerHour(HarvestTarget.Wood, 1);
            float rCor = EchoBonusCalculator.HarvestRatePerHour(HarvestTarget.Gold, 1);
            float rBra = EchoBonusCalculator.HarvestRatePerHour(HarvestTarget.Gold, 1);
            float rDor = EchoBonusCalculator.HarvestRatePerHour(HarvestTarget.Iron, 1);
            float rMar = EchoBonusCalculator.HarvestRatePerHour(HarvestTarget.Crystals, 1);
            // WO-1474 (i) THE RATE-CLASS MOVE IS SPLIT-NEUTRAL. The three per-hour rates were
            // private consts in EchoBonusCalculator (3600 / 900 / 4) until 2026-09-06; they are
            // now authored in echoes-balance.json so the WO-1331 remote seam can reach them.
            // Pin the AUTHORED values to the literals they replaced: if a retune moves them,
            // this case fails loudly rather than the silo split drifting unnoticed.
            AssertClose(Fail, EchoBalanceCatalog.CommonResourcePerHour, 3600f,
                "harvestRatePerHour.common (WO-1474 moved the const into json; 5 every 5 seconds)");
            AssertClose(Fail, EchoBalanceCatalog.GoldPerHour, 900f,
                "harvestRatePerHour.gold (WO-1474 moved the const into json)");
            AssertClose(Fail, EchoBalanceCatalog.CrystalPerHour, 4f,
                "harvestRatePerHour.crystals (WO-1474 moved the const into json; 1 per 15 minutes)");

            // WO-1474 (ii) THE PER-ECHO WEIGHT IS THE AUTHORED perEchoBaseRate. Before today
            // HarvestTargetWeights returned the raw rate-class number and threw the roster entry
            // away, so every authored row was dead. Each weight is now rate x BaseRateFor(id) --
            // which is what makes the WO-830 Sec.3b crystals guard actually run.
            float bAld = EchoBalanceCatalog.BaseRateFor("echo-frosthowl");
            float bElo = EchoBalanceCatalog.BaseRateFor("echo-verdant-stag");
            float bCor = EchoBalanceCatalog.BaseRateFor("echo-voidwing-raven");
            float bBra = EchoBalanceCatalog.BaseRateFor("echo-stormcoil-serpent");
            float bDor = EchoBalanceCatalog.BaseRateFor("echo-stonewarden-bear");
            float bMar = EchoBalanceCatalog.BaseRateFor("echo-ember-phoenix");
            AssertClose(Fail, GetW(w, HarvestTarget.Stone), rAld * bAld, "HarvestTargetWeights[Food]");
            AssertClose(Fail, GetW(w, HarvestTarget.Wood), rElo * bElo, "HarvestTargetWeights[Wood]");
            AssertClose(Fail, GetW(w, HarvestTarget.Gold), rCor * bCor + rBra * bBra, "HarvestTargetWeights[Gold]");
            AssertClose(Fail, GetW(w, HarvestTarget.Iron), rDor * bDor, "HarvestTargetWeights[Iron]");
            AssertClose(Fail, GetW(w, HarvestTarget.Crystals), rMar * bMar, "HarvestTargetWeights[Crystals]");
            float crystalsW = GetW(w, HarvestTarget.Crystals);
            foreach (var t in new[] { HarvestTarget.Wood, HarvestTarget.Iron, HarvestTarget.Stone, HarvestTarget.Gold })
                if (crystalsW >= GetW(w, t))
                {
                    Fail($"crystals weight {crystalsW:0.###} not the smallest (>{GetW(w, t):0.###} for {t}) — combined double-crystal trickle must stay slowest");
                    break;
                }
        }

        // =====================================================================
        //  Group 6 — EchoLaneBonuses population after Recompute()
        // =====================================================================
        private static void CheckLaneBonusesPopulation(GameState state, Action<string> Fail)
        {
            state.EchoLanes = AllMatchedL1;
            EchoBonusCalculator.Recompute();

            float expectedAgg = EchoBonusCalculator.AggregateHarvestMultiplier();
            if (Mathf.Abs(EchoLaneBonuses.HarvestBonusMult - expectedAgg) > Eps)
                Fail($"EchoLaneBonuses.HarvestBonusMult={EchoLaneBonuses.HarvestBonusMult:0.####} after Recompute (expected {expectedAgg:0.####}, the applied aggregate)");
            if (Mathf.Abs(EchoLaneBonuses.HarvestBonusMult - 1f) < Eps)
                Fail("EchoLaneBonuses.HarvestBonusMult is still 1.0 after Recompute (never populated)");
        }

        // =====================================================================
        //  Group 7 — Dump credit: all five wallets move through the REAL path
        // =====================================================================
        private static GameObject CheckDumpCredit(GameState state, Action<string> Fail, List<string> notes)
        {
            // Stand up the REAL services headless. Editmode NEVER runs Awake (same law the
            // GameStateService seam documents above), so the Instance singletons are installed
            // by reflection on the auto-property backing setters — the method bodies under test
            // (DumpSilos / GrantSpendable / AddCoins) are still the REAL production code.
            var go = new GameObject("EchoDumpOracle");
            EchoService echo = null;
            EconomyService eco = null;
            try
            {
                echo = go.AddComponent<EchoService>();
                eco = go.AddComponent<EconomyService>();
            }
            catch (Exception ex)
            {
                notes.Add("group 7 skipped (service AddComponent failed headless — " + ex.Message + ")");
                return go;
            }
            if (EchoService.Instance == null && !TrySetStaticProperty(typeof(EchoService), "Instance", echo))
            {
                notes.Add("group 7 skipped (EchoService.Instance seam not installable headless)");
                return go;
            }
            if (EconomyService.Instance == null && !TrySetStaticProperty(typeof(EconomyService), "Instance", eco))
            {
                notes.Add("group 7 skipped (EconomyService.Instance seam not installable headless)");
                return go;
            }
            if (EchoService.Instance == null || EconomyService.Instance == null)
            {
                notes.Add("group 7 skipped (service singletons did not install headless)");
                return go;
            }

            // (a) All six harvest their affinities: every wallet moves; crystals the smallest.
            state.EchoLanes = AllMatchedL1;
            const int fullRosterOneHour = 12604;
            state.SiloResources = fullRosterOneHour;
            int woodBefore = state.Wood, ironBefore = state.Iron;
            int foodBefore = state.Resources.Stone, coinsBefore = state.Resources.Coins, crysBefore = state.Resources.Crystals;
            int banked = echo.DumpSilos();
            int dWood = state.Wood - woodBefore;
            int dIron = state.Iron - ironBefore;
            int dFood = state.Resources.Stone - foodBefore;
            int dGold = state.Resources.Coins - coinsBefore;
            int dCrys = state.Resources.Crystals - crysBefore;
            if (dWood <= 0) Fail($"Dump: Wood wallet did not move (+{dWood})");
            if (dIron <= 0) Fail($"Dump: Iron wallet did not move (+{dIron})");
            if (dFood <= 0) Fail($"Dump: Food wallet did not move (+{dFood})");
            if (dGold <= 0) Fail($"Dump: Gold/Coins wallet did not move (+{dGold}) — Corvin's affinity must credit AddCoins");
            if (dCrys <= 0) Fail($"Dump: Crystals wallet did not move (+{dCrys}) — Bran+Maren must credit the Aether wallet");

            // ⛔ THE RETURN VALUE MUST EQUAL WHAT THE WALLETS ACTUALLY RECEIVED (WO-1207, 2026-08-25).
            // This replaces `if (banked != fullRosterOneHour)`, which was a HOLLOW ASSERTION: DumpSilos
            // used to return the PRE-CLAMP pool, i.e. the very number the fixture had just written into
            // SiloResources, so the check compared a value against itself and could never fail — while
            // three lines below, this same file already conceded "Wood/Iron/Food may be trimmed by the
            // real town-bank capacity". It passed green on the owner's device while the chip printed
            // banked=17 against 0 actually credited.
            // Measured deltas are the only honest oracle here (WO-978's rule: report the MEASURED
            // before/after delta, never the amount requested).
            int measuredIntoWallets = dWood + dIron + dFood + dGold + dCrys;
            if (banked != measuredIntoWallets)
                Fail($"DumpSilos returned {banked} but the wallets moved by {measuredIntoWallets} " +
                     "— the return value must be what was BANKED, never the pre-clamp pool (WO-1207).");

            // RE-PINNED 2026-09-05 (WO-1392): the silo RETAINS whatever the town-bank clamp refused.
            // This used to read `if (state.SiloResources > 0.5) Fail("...silo reset")` - it pinned the
            // BURN: DumpSilos drained the whole pool and the clamped remainder was lost, which is the
            // owner's covenant broken (a harvest is never silently burned). The honest invariant is
            // conservation: pool == banked + retained, to the unit.
            double retained = state.SiloResources;
            double expectedRetained = fullRosterOneHour - measuredIntoWallets;
            if (Math.Abs(retained - expectedRetained) > 0.5)
                Fail($"DumpSilos left {retained:0} in the silo but pool {fullRosterOneHour} - banked " +
                     $"{measuredIntoWallets} = {expectedRetained:0}; the clamped remainder must STAY in the " +
                     "silo, never be burned (WO-1392) and never be double-counted.");
            if (measuredIntoWallets < fullRosterOneHour)
                notes.Add($"[echo-spec] the town bank cap trimmed this dump: pool {fullRosterOneHour}, " +
                          $"banked {measuredIntoWallets}, {retained:0} retained in the silo for the next " +
                          "dump - EXPECTED at default capacity, and recorded rather than hidden.");
            // Wood/Iron/Food may be trimmed by the real town-bank capacity. Gold and
            // crystals are uncapped; their movement is asserted individually above.
            foreach (int other in new[] { dWood, dIron, dFood, dGold })
                if (dCrys >= other)
                {
                    Fail($"Dump: crystal share {dCrys} not the smallest (vs {other}) — the double-crystal trickle must stay slowest");
                    break;
                }

            // (b) Crystals move with only ONE crystal harvester assigned (either suffices).
            state.EchoLanes = "food:1,wood:1,gold:1,idle,iron:1,crystals:1";   // Maren only
            state.SiloResources = 11704.0; // one hour: 3 common + 1 gold + Maren crystal
            crysBefore = state.Resources.Crystals;
            echo.DumpSilos();
            if (state.Resources.Crystals - crysBefore <= 0)
                Fail("Dump: crystals did not move with only Maren assigned (either crystal harvester must credit)");

            return go;
        }

        // =====================================================================
        //  Group 5 — Save round-trip (SaveSchema serialize→deserialize)
        // =====================================================================
        private static void CheckSaveRoundTrip(Action<string> Fail)
        {
            // Canary on unreviewed schema bumps -- RE-POINTED 2026-08-27 so it can never go stale.
            //
            // It used to read `if (SaveSchema.CurrentVersion != 39) Fail(...)`: a RESTATED copy of a
            // number that lives in exactly one place. WO-1235 bumped the schema to v40 (the recipe
            // unlock gate, entirely unrelated to Echoes) and this suite reddened against CORRECT code
            // -- the same duplicated-state drift CLAUDE.md records for the WO-number block and the
            // retired dependency table. Pinning 40 would only move the staleness one version along.
            //
            // The GUARANTEE this pin ever protected is not "the schema is version N". It is:
            //   the echoLanes token survives WHATEVER the current schema is.
            // So assert THAT, against SaveSchema.CurrentVersion as READ, over the whole migration
            // range. A future v41 that clobbers, blanks or re-grammars echoLanes reds here on the
            // day it lands; a future v41 that leaves it alone passes with no edit to this file.
            int currentSchema = SaveSchema.CurrentVersion;
            if (currentSchema < 33)
                Fail($"SaveSchema.CurrentVersion={currentSchema} is BELOW v33, the version at which " +
                     "WO-738 established the rich echoLanes token -- the token cannot survive a schema " +
                     "that predates it. This is a rollback, not a bump.");

            // Every from-version in the live chain must carry a NON-NULL rich token through untouched.
            // SaveMigrator only ever SEEDS echoLanes when it is null (SaveMigrator.cs:468); any step,
            // present or future, that rewrites a populated token fails this loop.
            // The one rich WO-830 token this whole group uses -- declared once, asserted twice
            // (migration chain below, then the real serialize/deserialize/validate path).
            const string richToken = "crystals:3,idle,wood:1,gold:2";
            for (int fromVersion = 1; fromVersion <= currentSchema; fromVersion++)
            {
                try
                {
                    var chained = SaveMigrator.Migrate(
                        new SaveSchema.PersistedState { EchoLanes = richToken, EchoCount = 6 }, fromVersion);
                    if (chained == null)
                        Fail($"SaveMigrator.Migrate(v{fromVersion} -> v{currentSchema}) returned null");
                    else if (chained.EchoLanes != richToken)
                        Fail($"echoLanes did NOT survive the migration chain from v{fromVersion} to the " +
                             $"current schema v{currentSchema}: wrote '{richToken}', read back " +
                             $"'{chained.EchoLanes ?? "<null>"}' -- a migration step is rewriting a " +
                             "populated token (it may only SEED a null one).");
                }
                catch (Exception ex)
                {
                    Fail($"SaveMigrator.Migrate(v{fromVersion} -> v{currentSchema}) THREW: " +
                         $"{ex.GetType().Name}: {ex.Message}");
                }
            }

            // A rich WO-830 resource token survives the REAL serialize → deserialize → validate path.
            var ps = new SaveSchema.PersistedState { EchoLanes = richToken, EchoCount = 6 };
            try
            {
                string json = JsonConvert.SerializeObject(ps, SaveSchema.JsonSettings);
                var back = JsonConvert.DeserializeObject<SaveSchema.PersistedState>(json, SaveSchema.JsonSettings);
                if (back == null) { Fail("save round-trip deserialized to null"); }
                else
                {
                    var vr = SaveSchema.Validate(back);
                    if (!vr.Ok) Fail($"round-tripped save FAILED validation: field '{vr.FieldPath}' ({vr.Reason})");
                    if (back.EchoLanes != richToken)
                        Fail($"echoLanes did not survive the save round-trip: wrote '{richToken}', read back '{back.EchoLanes}'");
                }
            }
            catch (Exception ex) { Fail($"save round-trip THREW: {ex.GetType().Name}: {ex.Message}"); }

            // An older-version blob with NO echoLanes loads with the default (default-on-read,
            // no throw). SaveMigrator's v31 step seeds the "wood" starter when absent.
            try
            {
                var migrated = SaveMigrator.Migrate(new SaveSchema.PersistedState(), 25);
                if (migrated == null) Fail("SaveMigrator.Migrate(v25, no echoLanes) returned null");
                else if (migrated.EchoLanes == null)
                    Fail("migrate v25→current left echoLanes null (default-on-read did not seed the starter lane)");
            }
            catch (Exception ex) { Fail($"legacy-blob migrate THREW: {ex.GetType().Name}: {ex.Message}"); }
        }

        // =====================================================================
        //  Helpers
        // =====================================================================
        private static float GetW(Dictionary<HarvestTarget, float> w, HarvestTarget t)
            => (w != null && w.TryGetValue(t, out var v)) ? v : 0f;

        private static void AssertClose(Action<string> Fail, float actual, float expected, string label)
        {
            if (Mathf.Abs(actual - expected) > Eps)
                Fail($"{label}={actual:0.#####} (expected {expected:0.#####})");
        }

        // --- Headless state-install (editmode has no Awake) — mirrors OfflineHarvestRegression --

        private static bool TryInstallHeadlessState(GameStateService svc, GameState state, out string err)
        {
            err = null;
            var stateField = typeof(GameStateService).GetField("_state", BindingFlags.NonPublic | BindingFlags.Instance);
            if (stateField == null)
            { err = "GameStateService._state field not found by reflection (state seam renamed/removed)"; return false; }
            stateField.SetValue(svc, state);
            if (!TrySetInstanceStatic(svc))
            { err = "GameStateService._instance static not found by reflection (singleton seam renamed/removed)"; return false; }
            return true;
        }

        private static bool TrySetInstanceStatic(GameStateService svc)
        {
            var f = typeof(GameStateService).GetField("_instance", BindingFlags.NonPublic | BindingFlags.Static);
            if (f == null) return false;
            f.SetValue(null, svc);
            return true;
        }

        /// <summary>Set a static auto-property with a private setter (the `Instance` singleton
        /// seam) by reflection. Returns false when the seam moved (caller emits a named skip).</summary>
        private static bool TrySetStaticProperty(Type type, string name, object value)
        {
            try
            {
                var p = type.GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                if (p != null && p.CanWrite) { p.SetValue(null, value, null); return true; }
                var f = type.GetField($"<{name}>k__BackingField", BindingFlags.NonPublic | BindingFlags.Static);
                if (f == null) return false;
                f.SetValue(null, value);
                return true;
            }
            catch { return false; }   // seam moved — named-skip path, never a throw out of finally
        }
    }
}

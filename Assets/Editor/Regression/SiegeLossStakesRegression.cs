// =============================================================================
// SiegeLossStakesRegression -- [siege-loss-stakes]
// -----------------------------------------------------------------------------
// Assembly: DeNelle.Editor   Registered ONCE in DataRegression.RunAll.
//
// THE ORACLE FOR THE LIVE STAKES RULING (owner, 2026-08-27):
//
//        BANK THEFT REPLACES COLLECTOR LOOTING.
//        A SIEGE BILLS ONCE PER ATTACK, NOT TWICE.
//
//        A siege takes exactly three things: structural damage, a repair bill,
//        and theft of a PERCENTAGE of UNPROTECTED bank resources under a
//        PROTECTED FLOOR and a PER-ATTACK CAP.
//
//        LOOTABLE      Wood, Iron, Stone, Coins
//        UNTOUCHABLE   Crystals, SKR, purchased goods, equipped gear
//
// ============================================================================
//  ! THIS SUITE WAS RE-POINTED, NOT REWRITTEN FROM NOTHING, AND THAT IS THE POINT
// ----------------------------------------------------------------------------
//  Until 2026-08-27 this file was the oracle for the OPPOSITE rule -- WO-1139's
//  "COLLECTOR LOOTING ONLY. NO BANK THEFT." Its headline case measured the wallet
//  across a full BuildStakes + ApplyStakes and FAILED THE GATE ON A SINGLE POINT OF
//  MOVEMENT. The owner superseded that ruling, so that case necessarily went RED --
//  and a green oracle going red on a ruling change is THE ORACLE DOING ITS JOB. It
//  was re-pointed rather than deleted or routed around, and it is re-pointed AT THE
//  SAME STRENGTH: the direction of case A is inverted (the bank must now move, and
//  by EXACTLY the ledger), and every other guard it carried is kept.
//
//  ! WO-1139 IS SUPERSEDED, NOT WRONG-IN-HINDSIGHT. The failure mode it named --
//    two theft authorities charging the player twice for one siege -- is REAL, and
//    it is now closed BY REMOVAL rather than by abstinence: collector looting is
//    gone (case B proves it, structurally and behaviourally), so there is exactly
//    ONE theft in the game and case A is what proves it happens exactly once.
// ============================================================================
//
//  THE CASES
//    A. THE BANK MOVES BY EXACTLY THE LEDGER -- and by nothing else. The headline.
//       Measured across a real ApplyStakes with a live EconomyService installed.
//    B. COLLECTOR LOOTING IS REMOVED. Structurally (the theft members are gone from
//       ResourceCollector) and behaviourally (a collector holding 800 breaks and
//       keeps all 800). This is the NO-DOUBLE-BILL proof.
//    C. ! CRYSTALS ARE NEVER TAKEN. Asserted at the classification, at the writer,
//       and at the live wallet, three times independently -- a player cannot tell a
//       harvested crystal from a PURCHASED one, so a crystal loss is a refund and a
//       one-star review on a live published title.
//    D. THE FLOOR AND THE CAP, against an AUTHORED table of hand-worked literals.
//       Never re-derived from the production constants: an expectation computed from
//       the code under test asserts nothing. The table's rows were each proved to go
//       RED against a mutated implementation before this suite was written.
//    E. THE REPORT FIGURE AND THE WALLET MOVEMENT ARE ONE NUMBER, by identity.
//    F. IDEMPOTENCE -- a re-filed or re-opened report cannot bill a second time.
//    G. NOTHING PERMANENT MOVES: no downgrade, no lost layout, no BestWave, no
//       ever-built id, no camp cooldown.
//    H. THE RULE IDS STAY DISTINCT, so an old report keeps describing the ruling
//       that actually wrote it.
// =============================================================================

using System.Collections.Generic;
using System.Reflection;
using DeNelle.Core.Defense;
using DeNelle.Core.Economy;
using DeNelle.Core.State;
using DeNelle.Village;
using DeNelle.Village.Buildings.Progression;
using UnityEngine;

namespace DeNelle.Editor
{
    /// <summary>Oracle for the WO-1026 loss stakes: bounded bank theft, billed once.</summary>
    public static class SiegeLossStakesRegression
    {
        public static bool Run(out string reason)
        {
            var failures = new List<string>();

            try
            {
                RuleIdCases(failures);                 // H
                LootableSetCases(failures);            // C (classification half)
                RequiredArithmeticCases(failures);     // D (shape half)
                ArithmeticTableCases(failures);        // D (the authored table)
                LedgerWriterCases(failures);           // C (writer half)
                CollectorLootingRemovedCases(failures);// B (structural half)

                LiveCases(failures, out bool skipped, out string skipWhy);
                if (skipped)
                {
                    // The GameStateService / EconomyService install seam moved -- genuinely
                    // unrunnable headless. NAMED SKIP, never a false pass (harness-integrity rule).
                    return DeNelle.Editor.Regression.RegressionOutcome.Skip(out reason,
                        "SIEGE LOSS STAKES", "needs fleet -- " + skipWhy);
                }
            }
            catch (System.Exception ex)
            {
                failures.Add($"oracle threw: {ex.GetType().Name}: {ex.Message}");
            }

            if (failures.Count == 0)
            {
                reason = "SIEGE LOSS STAKES OK -- bounded BANK THEFT, billed ONCE: the wallet moves by " +
                         "EXACTLY the ledger the player reads and by nothing else; COLLECTOR LOOTING IS " +
                         "REMOVED (a broken collector keeps every point of its pending, and the theft " +
                         "members are gone from the class) so no double-bill is expressible; the protected " +
                         "floor and the per-attack cap match an AUTHORED table of hand-worked literals " +
                         "(not re-derived from the production constants); a HELD defence takes nothing; " +
                         "CRYSTALS ARE NEVER TAKEN at the classification, at the writer AND at the live " +
                         "wallet; the seal is idempotent so a re-filed report cannot bill twice; and no " +
                         "structure level, layout entry, ever-built id, camp cooldown or best-wave moved";
                return true;
            }

            reason = $"SIEGE LOSS STAKES FAIL x{failures.Count}: " + string.Join(" | ", failures);
            return false;
        }

        // =====================================================================
        //  H -- the rule ids stay distinct and self-describing
        // =====================================================================

        private static void RuleIdCases(List<string> f)
        {
            if (string.IsNullOrEmpty(StakeRules.RuleId))
                f.Add("StakeRules.RuleId is empty -- a report could not name the ruling that wrote it");

            if (StakeRules.RuleId == StakesLedger.InterimRuleId)
                f.Add("[HARD-RULE]the live rule id collapsed into the INTERIM id -- every pre-stakes " +
                      "report would start claiming it was written under a ruling that did not exist yet");

            if (StakeRules.RuleId == StakeRules.CollectorLootRuleId)
                f.Add("[HARD-RULE]the live rule id collapsed into the SUPERSEDED collector-loot id " +
                      "(WO-1139). A report written under the old ruling would then be indistinguishable " +
                      "from one written under this one, and the two took from different pools");

            var empty = StakeRules.Empty();
            if (empty == null || empty.StakesRuleId != StakeRules.RuleId)
                f.Add("StakeRules.Empty() is not self-describing: it must carry the live rule id");
            if (empty != null && empty.Applied)
                f.Add("StakeRules.Empty() came back APPLIED -- an unsettled ledger must not look settled");
            if (empty != null && !empty.IsEmpty)
                f.Add("StakeRules.Empty() is not empty");
        }

        // =====================================================================
        //  C -- classification. THE WHOLE ENUM, not the buckets we remembered.
        // =====================================================================

        /// <summary>
        /// The lootable set, HAND-AUTHORED from the owner's ruling of 2026-08-27 (verbatim:
        /// "LOOTABLE Wood, Iron, Stone, Coins"). "Stone" is the balance internally NAMED Food --
        /// BankResource has no Stone member and must never grow one, because the name is a live
        /// save and wire key.
        /// </summary>
        private static readonly BankResource[] Lootable =
        {
            BankResource.Wood,
            BankResource.Iron,
            BankResource.Stone,    // "STONE"
            BankResource.Coins,   // "GOLD"
        };

        private static void LootableSetCases(List<string> f)
        {
            var all = (BankResource[])System.Enum.GetValues(typeof(BankResource));
            if (all == null || all.Length == 0)
            {
                f.Add("classification: BankResource enumerated to NOTHING -- the reflection seam moved");
                return;
            }

            foreach (var r in all)
            {
                bool expected = System.Array.IndexOf(Lootable, r) >= 0;
                bool actual = StakeRules.IsLootable(r);
                if (actual == expected) continue;

                f.Add(expected
                    ? $"classification: {r} SHOULD be lootable and is not -- the ruling's stake has a " +
                      "hole in it and the consequence loop silently loses a consequence"
                    : $"[HARD-RULE]classification: {r} IS CLASSIFIED LOOTABLE AND MUST NOT BE. Crystals " +
                      "are indistinguishable from PURCHASED crystals -- the same wallet -- so taking one " +
                      "turns a gameplay loss into a refund request and a one-star review on a LIVE " +
                      "published title. Hard exemption, not a balance knob.");
            }
        }

        // =====================================================================
        //  D -- the arithmetic MUST EXIST. (The inverse of the retired guard.)
        // =====================================================================

        /// <summary>
        /// Until 2026-08-27 this case asserted that <c>Build</c> / <c>TakeFrom</c> /
        /// <c>ProtectedFloor</c> were ABSENT -- the WO-1139 ruling had deleted them and a re-add was
        /// the defect. The ruling reversed, so the guard reverses with it: the ruling REQUIRES a
        /// floor and a cap, and a build that silently lost them would take an unbounded percentage
        /// of an unprotected balance. Their absence is now the defect.
        /// </summary>
        private static void RequiredArithmeticCases(List<string> f)
        {
            foreach (string required in new[] { "ProtectedFloor", "CapPerAttack", "TakeFrom", "Build", "StealFractionFor" })
                if (typeof(StakeRules).GetMethod(required, BindingFlags.Public | BindingFlags.Static) == null)
                    f.Add($"[HARD-RULE]StakeRules.{required} is GONE. The 2026-08-27 ruling requires a " +
                          "PROTECTED FLOOR and a PER-ATTACK CAP; without them a siege takes an unbounded " +
                          "share of an unprotected balance, which is the mechanic the owner explicitly " +
                          "bounded.");

            // The numbers must be AUTHORED IN DATA, not re-hardcoded in the arithmetic.
            foreach (string knob in new[]
                     {
                         "BreachedStealFraction", "OverrunStealFraction",
                         "ProtectedFloorFractionOfCapacity", "PerAttackCapFractionOfCapacity",
                         "CoinsProtectedFloor", "CoinsPerAttackCap",
                     })
                if (typeof(SiegeStakesBalance).GetProperty(knob, BindingFlags.Public | BindingFlags.Static) == null)
                    f.Add($"[HARD-RULE]SiegeStakesBalance.{knob} is gone -- the floor/cap numbers are " +
                          "OWNER-PENDING and must stay authored in data so a ruling is a JSON edit, " +
                          "never a recompile.");
        }

        // =====================================================================
        //  D -- THE AUTHORED TABLE. Hand-worked literals, never re-derived.
        // =====================================================================

        /// <summary>
        /// One hand-worked row of the ruling. <see cref="Expected"/> is a LITERAL, computed by hand
        /// from the authored bounds -- NEVER an expression over SiegeStakesBalance. An oracle that
        /// re-derives its expectation from the code under test asserts nothing, and these numbers
        /// are player money.
        /// <para>! WHEN THE OWNER RULES THE FLOOR AND THE CAP, THIS TABLE IS RE-WORKED BY HAND in
        /// the same change as the json. Do not "fix" it with a formula.</para>
        /// </summary>
        private struct Fixture
        {
            public string Note;
            public BankResource Resource;
            public int Banked;
            public int Capacity;
            public DefenseOutcome Outcome;
            public int Expected;
        }

        /// <summary>
        /// Worked BY HAND against the PROVISIONAL authored bounds (siege-stakes.json):
        /// floor = 25% of capacity, cap = 5% of capacity, steal = 5% breached / 10% overrun,
        /// coins floor 500 / coins cap 2000 flat. Every row below was proved to go RED against a
        /// mutated implementation (floor removed, cap removed, coins de-listed, crystals listed,
        /// held stealing, unknown-capacity failing closed) before this suite was written.
        /// </summary>
        private static Fixture[] Table()
        {
            return new[]
            {
                new Fixture { Note = "the worked example: floor 5000, unprotected 7000, 10% = 700",
                              Resource = BankResource.Wood, Banked = 12000, Capacity = 20000,
                              Outcome = DefenseOutcome.Overrun, Expected = 700 },

                new Fixture { Note = "a BREACH costs half of an overrun -- partial success is worth something",
                              Resource = BankResource.Wood, Banked = 12000, Capacity = 20000,
                              Outcome = DefenseOutcome.Breached, Expected = 350 },

                new Fixture { Note = "a HELD defence takes NOTHING -- structural, never a knob",
                              Resource = BankResource.Wood, Banked = 12000, Capacity = 20000,
                              Outcome = DefenseOutcome.Held, Expected = 0 },

                new Fixture { Note = "exactly AT the protected floor: untouchable",
                              Resource = BankResource.Iron, Banked = 5000, Capacity = 20000,
                              Outcome = DefenseOutcome.Overrun, Expected = 0 },

                new Fixture { Note = "UNDER the floor: a player already down is never kicked",
                              Resource = BankResource.Iron, Banked = 4000, Capacity = 20000,
                              Outcome = DefenseOutcome.Overrun, Expected = 0 },

                new Fixture { Note = "STONE, a maxed L6 store: raw 2550 CLIPPED BY THE PER-ATTACK CAP to 1700",
                              Resource = BankResource.Stone, Banked = 34000, Capacity = 34000,
                              Outcome = DefenseOutcome.Overrun, Expected = 1700 },

                new Fixture { Note = "STONE, a grandfathered OVER-cap save: still clipped to 1700",
                              Resource = BankResource.Stone, Banked = 40000, Capacity = 34000,
                              Outcome = DefenseOutcome.Overrun, Expected = 1700 },

                new Fixture { Note = "GOLD is uncapped: flat floor 500, 10% of the 4500 above it",
                              Resource = BankResource.Coins, Banked = 5000, Capacity = StakeRules.UncappedCapacity,
                              Outcome = DefenseOutcome.Overrun, Expected = 450 },

                new Fixture { Note = "GOLD, a rich player: CLIPPED BY THE FLAT COIN CAP",
                              Resource = BankResource.Coins, Banked = 100000, Capacity = StakeRules.UncappedCapacity,
                              Outcome = DefenseOutcome.Overrun, Expected = 2000 },

                new Fixture { Note = "GOLD under the flat coin floor",
                              Resource = BankResource.Coins, Banked = 400, Capacity = StakeRules.UncappedCapacity,
                              Outcome = DefenseOutcome.Overrun, Expected = 0 },

                new Fixture { Note = "! CRYSTALS ARE UNTOUCHABLE at any amount, under any cap",
                              Resource = BankResource.Crystals, Banked = 999999, Capacity = StakeRules.UncappedCapacity,
                              Outcome = DefenseOutcome.Overrun, Expected = 0 },

                new Fixture { Note = "UNKNOWN capacity -> take NOTHING (fail open, in the player's favour)",
                              Resource = BankResource.Wood, Banked = 12000, Capacity = 0,
                              Outcome = DefenseOutcome.Overrun, Expected = 0 },

                new Fixture { Note = "just above the floor: 10% of the 100 that is unprotected",
                              Resource = BankResource.Wood, Banked = 5100, Capacity = 20000,
                              Outcome = DefenseOutcome.Overrun, Expected = 10 },

                new Fixture { Note = "one point above the floor rounds DOWN to nothing",
                              Resource = BankResource.Wood, Banked = 5001, Capacity = 20000,
                              Outcome = DefenseOutcome.Breached, Expected = 0 },

                new Fixture { Note = "an empty store",
                              Resource = BankResource.Wood, Banked = 0, Capacity = 20000,
                              Outcome = DefenseOutcome.Overrun, Expected = 0 },

                new Fixture { Note = "a negative balance cannot produce a take",
                              Resource = BankResource.Wood, Banked = -500, Capacity = 20000,
                              Outcome = DefenseOutcome.Overrun, Expected = 0 },
            };
        }

        private static void ArithmeticTableCases(List<string> f)
        {
            var table = Table();

            for (int i = 0; i < table.Length; i++)
            {
                var t = table[i];
                var standing = new BankStanding { Resource = t.Resource, Banked = t.Banked, Capacity = t.Capacity };

                int actual = StakeRules.TakeFrom(standing, t.Outcome);
                if (actual != t.Expected)
                    f.Add($"[HARD-RULE]take[{i}] ({t.Note}): {t.Resource} banked={t.Banked} cap={t.Capacity} " +
                          $"{t.Outcome} took {actual}, the hand-worked answer is {t.Expected}. THIS NUMBER IS " +
                          "PLAYER MONEY. If the owner has re-ruled the floor or the cap, re-work this table BY " +
                          "HAND in the same change as siege-stakes.json -- never replace a literal with an " +
                          "expression over the production constant.");

                if (t.Banked > 0 && actual > t.Banked)
                    f.Add($"[HARD-RULE]take[{i}]: carried off {actual} from a balance of {t.Banked} -- more " +
                          "than exists");
            }

            // The same table through Build(), so the aggregator and the per-bucket rule agree.
            var standings = new List<BankStanding>();
            foreach (var t in table)
                if (t.Outcome == DefenseOutcome.Overrun)
                    standings.Add(new BankStanding { Resource = t.Resource, Banked = t.Banked, Capacity = t.Capacity });

            var built = StakeRules.Build(DefenseOutcome.Overrun, standings);
            if (built == null) { f.Add("Build() returned null"); return; }
            if (built.Crystals != 0 || built.Magic != 0)
                f.Add($"[HARD-RULE]Build() filled an UNTOUCHABLE bucket (c{built.Crystals} m{built.Magic}) " +
                      "from a standings list that included crystals");
            if (built.StakesRuleId != StakeRules.RuleId)
                f.Add($"Build() stamped rule id '{built.StakesRuleId}', expected '{StakeRules.RuleId}'");

            // A HELD defence, through the aggregator, takes nothing at all.
            var heldLedger = StakeRules.Build(DefenseOutcome.Held, standings);
            if (heldLedger == null || !heldLedger.IsEmpty)
                f.Add("[HARD-RULE]Build(Held) produced a NON-EMPTY ledger. A held defence takes nothing -- " +
                      "if holding still cost resources the report would have nothing riding on it.");
        }

        // =====================================================================
        //  C -- the one writer
        // =====================================================================

        private static void LedgerWriterCases(List<string> f)
        {
            var all = (BankResource[])System.Enum.GetValues(typeof(BankResource));

            // The refusal, at an absurd amount so no cap or floor could excuse a partial take.
            foreach (var r in all)
            {
                if (System.Array.IndexOf(Lootable, r) >= 0) continue;
                var ledger = StakeRules.Empty();
                if (StakeRules.Add(ledger, r, 999999))
                    f.Add($"[HARD-RULE]writer: StakeRules.Add accepted {r} -- the untouchable list is not " +
                          "enforced at the one writer");
                if (!ledger.IsEmpty)
                    f.Add($"[HARD-RULE]writer: a refused {r} still landed on the ledger");
            }

            // The SUCCESS path, so this cannot pass on a writer that refuses everything.
            var l = StakeRules.Empty();
            StakeRules.Add(l, BankResource.Wood, 400);
            StakeRules.Add(l, BankResource.Wood, 150);
            StakeRules.Add(l, BankResource.Iron, 25);
            StakeRules.Add(l, BankResource.Stone, 60);
            StakeRules.Add(l, BankResource.Coins, 30);

            if (l.Wood != 550) f.Add($"writer [GOOD PATH]: two wood adds summed to {l.Wood}, hand-worked 550 " +
                                     "-- a second add must ADD, not overwrite");
            if (l.Iron != 25) f.Add($"writer [GOOD PATH]: iron bucket is {l.Iron}, expected 25");
            if (l.Stone != 60) f.Add($"writer [GOOD PATH]: stone bucket is {l.Stone}, expected 60");
            if (l.Coins != 30) f.Add($"writer [GOOD PATH]: gold bucket is {l.Coins}, expected 30");
            if (l.Crystals != 0 || l.Magic != 0)
                f.Add($"[HARD-RULE]writer: an untouchable bucket moved (c{l.Crystals} m{l.Magic})");

            // Zero and negative are not stakes.
            if (StakeRules.Add(l, BankResource.Wood, 0) || StakeRules.Add(l, BankResource.Wood, -300))
                f.Add("writer: StakeRules.Add accepted a zero/negative amount -- a 'loss' that hands " +
                      "resources back is a bug, and it must never reach a ledger the player reads");
            if (l.Wood != 550)
                f.Add($"writer: a zero/negative Add moved the wood bucket to {l.Wood}");
        }

        // =====================================================================
        //  B -- COLLECTOR LOOTING IS REMOVED (structural half)
        // =====================================================================

        /// <summary>
        /// The owner ruled that bank theft REPLACES collector looting: a siege bills ONCE. The
        /// structural half asserts the collector's theft members are GONE, so a second take cannot
        /// be re-introduced by a one-line edit -- there is nothing left for it to hang off.
        /// </summary>
        private static void CollectorLootingRemovedCases(List<string> f)
        {
            var t = typeof(ResourceCollector);

            foreach (string gone in new[] { "LootTakenFrom", "IsResourceLootable" })
                if (t.GetMethod(gone, BindingFlags.Public | BindingFlags.Static) != null)
                    f.Add($"[HARD-RULE]ResourceCollector.{gone} is BACK. Collector looting was REMOVED by " +
                          "the owner's 2026-08-27 ruling because BANK THEFT REPLACES IT. The two together " +
                          "charge the player TWICE for one siege -- once out of the collector's pending, " +
                          "once out of the wallet -- which is precisely what the superseded WO-1139 ruling " +
                          "was written to prevent.");

            if (t.GetField("RaidLootFraction", BindingFlags.NonPublic | BindingFlags.Static) != null
                || t.GetField("RaidLootFraction", BindingFlags.Public | BindingFlags.Static) != null)
                f.Add("[HARD-RULE]ResourceCollector.RaidLootFraction is BACK -- there must be no collector " +
                      "steal constant for a future edit to hang a second theft off.");

            if (t.GetProperty("IsLootable", BindingFlags.Public | BindingFlags.Instance) != null)
                f.Add("[HARD-RULE]ResourceCollector.IsLootable is BACK. It existed only to bound the " +
                      "collector steal that the ruling removed; its return is the first half of that steal " +
                      "coming back.");
        }

        // =====================================================================
        //  A / B / C / E / F / G -- the LIVE cases
        // =====================================================================

        private static void LiveCases(List<string> f, out bool skipped, out string skipWhy)
        {
            skipped = false; skipWhy = null;

            var priorState = GameStateService.Instance;
            var priorEconomy = EconomyService.Instance;
            string rawSave = HeadlessState.SnapshotSave(out bool hadSave);

            GameObject gssGo = null;
            GameObject ecoGo = null;
            GameObject collectorGo = null;
            GameState throwaway = null;
            bool installed = false;

            // The registry is a process-wide static. Park whatever is in it so a real scene's
            // collectors cannot leak into these cases (or be lost by them).
            var parked = new List<ResourceCollector>(ResourceCollectorRegistry.All);
            foreach (var c in parked) ResourceCollectorRegistry.Unregister(c);

            // The collector persists pending/hp in PlayerPrefs keyed by building id, so the fixture
            // would otherwise dirty a real save's lumbermill. Snapshot and restore.
            const string FixtureId = ResourceBuildingProgression.LumbermillId;   // yields Wood
            string[] prefKeys =
            {
                "dotr.collector.pending." + FixtureId,
                "dotr.collector.hp." + FixtureId,
                "dotr.collector.lastaccrual." + FixtureId,
            };
            var prefWas = new string[prefKeys.Length];
            var prefHad = new bool[prefKeys.Length];
            for (int i = 0; i < prefKeys.Length; i++)
            {
                prefHad[i] = PlayerPrefs.HasKey(prefKeys[i]);
                prefWas[i] = prefHad[i] ? PlayerPrefs.GetString(prefKeys[i], string.Empty) : string.Empty;
            }

            try
            {
                throwaway = ScriptableObject.CreateInstance<GameState>();
                gssGo = new GameObject("GameStateService (loss-stakes-oracle)");
                var gss = gssGo.AddComponent<GameStateService>();
                if (!HeadlessState.TryInstall(gss, throwaway, out skipWhy)) { skipped = true; return; }
                installed = true;
                var s = throwaway;

                // A LIVE ECONOMY SERVICE. Case A is not "nothing could be debited" -- it is
                // "everything needed to debit was present, and it debited EXACTLY the ledger".
                ecoGo = new GameObject("EconomyService (loss-stakes-oracle)");
                var eco = ecoGo.AddComponent<EconomyService>();
                if (!TrySetEconomyInstance(eco, out string ecoWhy))
                { skipped = true; skipWhy = ecoWhy; return; }

                // Permanent progress to prove untouched.
                s.BaseLayout.Clear();
                s.BaseLayout.Add(Placed("tower_ground_archer", 3, 4, 5));
                s.BaseLayout.Add(Placed("wall_stone", -1, 2, 6));
                s.BestWave = 17;
                s.MarkEverBuilt("tower_ground_archer");

                s.Wood = 12000;
                s.Iron = 9000;
                var r = s.Resources; r.Stone = 7000; r.Crystals = 4242; r.Coins = 5000; s.Resources = r;

                HeldTakesNothingCase(f, s);
                TheSingleDebitCase(f, s);
                BuildStakesInvariantCase(f, s);

                // Clear the fixture's persisted pending/hp BEFORE Configure loads them: a real
                // save's lumbermill could be sitting at hp 0, which would hand the oracle an
                // ALREADY-BROKEN collector and quietly invalidate the case.
                for (int i = 0; i < prefKeys.Length; i++) PlayerPrefs.DeleteKey(prefKeys[i]);

                collectorGo = new GameObject("ResourceCollector (loss-stakes-oracle)");
                var collector = collectorGo.AddComponent<ResourceCollector>();
                // Awake/OnEnable do NOT run for a component added outside play mode, so Configure
                // (which loads state, seeds hp and registers) is what brings it online here.
                collector.Configure(FixtureId, maxHp: 100f);
                ResourceCollectorRegistry.Register(collector);
                if (ResourceCollectorRegistry.Get(FixtureId) != collector)
                { skipped = true; skipWhy = "the collector registry would not take the fixture"; return; }

                if (!SetPrivate(collector, "_pending", 800.0, out string pendWhy))
                { skipped = true; skipWhy = pendWhy; return; }

                CollectorKeepsItsPendingCase(f, s, collector);
            }
            finally
            {
                if (installed)
                {
                    HeadlessState.TrySetInstance(priorState);
                    TrySetEconomyInstance(priorEconomy, out _);
                }
                HeadlessState.RestoreSave(hadSave, rawSave);

                if (collectorGo != null) Object.DestroyImmediate(collectorGo);
                if (ecoGo != null) Object.DestroyImmediate(ecoGo);
                if (gssGo != null) Object.DestroyImmediate(gssGo);
                if (throwaway != null) Object.DestroyImmediate(throwaway);

                foreach (var c in new List<ResourceCollector>(ResourceCollectorRegistry.All))
                    ResourceCollectorRegistry.Unregister(c);
                foreach (var c in parked) ResourceCollectorRegistry.Register(c);

                for (int i = 0; i < prefKeys.Length; i++)
                {
                    if (prefHad[i]) PlayerPrefs.SetString(prefKeys[i], prefWas[i]);
                    else PlayerPrefs.DeleteKey(prefKeys[i]);
                }
                PlayerPrefs.Save();
            }
        }

        /// <summary>A defence that HELD costs the player nothing, through the whole real path.</summary>
        private static void HeldTakesNothingCase(List<string> f, GameState s)
        {
            var before = Bank.Of(s);

            var record = Record(DefenseOutcome.Held);
            record.ResourcesLost = DefenseReportBuilder.BuildStakes(record);
            bool billed = DefenseReportBuilder.ApplyStakes(record);

            if (billed || record.ResourcesLost == null || !record.ResourcesLost.IsEmpty)
                f.Add("[HARD-RULE]held: a defence that HELD produced a bill. Holding the line must cost " +
                      "nothing, or the report's 'your east wall fell first' story has nothing riding on it.");

            Bank.AssertUnchanged(f, "held", before, s);
        }

        /// <summary>
        /// ** THE HEADLINE CASE (A + C + E + F + G). A hand-built ledger goes through the REAL
        /// ApplyStakes with a live EconomyService, and the wallet must move by EXACTLY those
        /// buckets -- no more, no less, and NOT ONE CRYSTAL.
        ///
        /// <para>The ledger is hand-built rather than taken from BuildStakes on purpose: BuildStakes
        /// reads TownBankCapacity, which needs the structures catalog, and a case whose expectation
        /// depends on catalog state headlessly is a case that will one day pass by accident. The
        /// arithmetic is proved separately, against the authored table.</para>
        /// </summary>
        private static void TheSingleDebitCase(List<string> f, GameState s)
        {
            var before = Bank.Of(s);
            int levelsBefore = LevelsOf(s);
            int bestWaveBefore = s.BestWave;
            int everBuiltBefore = s.EverBuiltStructureIds != null ? s.EverBuiltStructureIds.Count : 0;
            int campCooldowns = s.RaidCooldowns != null ? s.RaidCooldowns.Count : 0;
            int layoutBefore = s.BaseLayout.Count;

            var record = Record(DefenseOutcome.Overrun);
            var ledger = StakeRules.Empty();
            StakeRules.Add(ledger, BankResource.Wood, 700);
            StakeRules.Add(ledger, BankResource.Stone, 250);
            StakeRules.Add(ledger, BankResource.Coins, 450);
            record.ResourcesLost = ledger;

            bool billed = DefenseReportBuilder.ApplyStakes(record);

            if (!billed)
            {
                f.Add("[HARD-RULE]debit: ApplyStakes did NOT bill a non-zero ledger against a wallet that " +
                      "could plainly afford it (w12000 i9000 stone7000 gold5000). A siege that takes " +
                      "nothing is the hollow loop this whole ticket exists to close.");
                return;
            }

            if (!ledger.Applied)
                f.Add("[HARD-RULE]debit: the ledger billed but did not latch Applied -- a re-file could bill again");
            if (ledger.StakesRuleId != StakeRules.RuleId)
                f.Add($"debit: StakesRuleId is '{ledger.StakesRuleId}', expected '{StakeRules.RuleId}'");

            var after = Bank.Of(s);

            // ** A + E. THE WALLET MOVED BY EXACTLY THE LEDGER, AND BY EXACTLY NOTHING ELSE.
            AssertMoved(f, "wood", before.Wood, after.Wood, ledger.Wood);
            AssertMoved(f, "iron", before.Iron, after.Iron, ledger.Iron);
            AssertMoved(f, "stone", before.Food, after.Food, ledger.Stone);
            AssertMoved(f, "gold", before.Coins, after.Coins, ledger.Coins);

            // ! C. CRYSTALS. Not "moved by the ledger" -- NOT MOVED AT ALL, at any amount.
            if (after.Crystals != before.Crystals)
                f.Add($"[HARD-RULE]debit: CRYSTALS MOVED across a siege ({before.Crystals} -> " +
                      $"{after.Crystals}). Crystals are purchasable with real money and a player cannot " +
                      "tell a harvested one from a bought one. This is not a balance defect, it is a " +
                      "refund and a one-star review on a LIVE published title.");
            if (ledger.Crystals != 0 || ledger.Magic != 0)
                f.Add($"[HARD-RULE]debit: the report CLAIMS c{ledger.Crystals} m{ledger.Magic} were taken");

            // ! G. Nothing permanent moved.
            if (LevelsOf(s) != levelsBefore)
                f.Add($"[HARD-RULE]debit: a structure DOWNGRADED across the loss ({levelsBefore} -> " +
                      $"{LevelsOf(s)}). The ruling: no building downgrade, ever.");
            if (s.BaseLayout.Count != layoutBefore)
                f.Add($"[HARD-RULE]debit: the base layout lost a structure ({s.BaseLayout.Count} of " +
                      $"{layoutBefore} left) -- no permanent progress is ever destroyed");
            if (s.BestWave != bestWaveBefore)
                f.Add($"[HARD-RULE]debit: BestWave moved {bestWaveBefore} -> {s.BestWave}");
            int everBuiltAfter = s.EverBuiltStructureIds != null ? s.EverBuiltStructureIds.Count : 0;
            if (everBuiltAfter != everBuiltBefore)
                f.Add("[HARD-RULE]debit: the ever-built set changed across a loss");
            int campsAfter = s.RaidCooldowns != null ? s.RaidCooldowns.Count : 0;
            if (campsAfter != campCooldowns)
                f.Add("[HARD-RULE]debit: camp cooldown state changed -- a cleared camp stays cleared");

            // ! F. IDEMPOTENCE -- "a siege bills ONCE per attack" is exactly this assertion.
            var beforeSecond = Bank.Of(s);
            int woodClaimed = ledger.Wood;
            if (DefenseReportBuilder.ApplyStakes(record))
                f.Add("[HARD-RULE]idempotence: ApplyStakes billed the SAME report a second time. The " +
                      "owner's ruling is 'a siege bills ONCE per attack, not twice'.");
            if (ledger.Wood != woodClaimed)
                f.Add("[HARD-RULE]idempotence: the second call rewrote the ledger the player already read");
            Bank.AssertUnchanged(f, "idempotence", beforeSecond, s);
        }

        /// <summary>
        /// BuildStakes against the LIVE bank. Its exact figures depend on TownBankCapacity (and so on
        /// the structures catalog), which is not stable headlessly -- so this case asserts the
        /// INVARIANTS that must hold whatever the capacity reads: nothing untouchable is ever
        /// claimed, no bucket exceeds what the bank holds, and the ledger names the live ruling.
        /// It also proves BuildStakes TAKES NOTHING on its own: only ApplyStakes bills.
        /// </summary>
        private static void BuildStakesInvariantCase(List<string> f, GameState s)
        {
            var before = Bank.Of(s);

            var record = Record(DefenseOutcome.Breached);
            var built = DefenseReportBuilder.BuildStakes(record);

            if (built == null) { f.Add("BuildStakes returned null"); return; }

            if (built.Crystals != 0 || built.Magic != 0)
                f.Add($"[HARD-RULE]build: an UNTOUCHABLE bucket was claimed (c{built.Crystals} " +
                      $"m{built.Magic}) against a live bank holding {before.Crystals} crystals");
            if (built.StakesRuleId != StakeRules.RuleId)
                f.Add($"build: StakesRuleId is '{built.StakesRuleId}', expected '{StakeRules.RuleId}'");
            if (built.Applied)
                f.Add("[HARD-RULE]build: BuildStakes returned an ALREADY-APPLIED ledger -- ApplyStakes " +
                      "would then refuse to bill it and the siege would silently cost nothing");

            if (built.Wood > before.Wood || built.Iron > before.Iron
                || built.Stone > before.Food || built.Coins > before.Coins)
                f.Add($"[HARD-RULE]build: a bucket claims more than the bank holds (w{built.Wood}/{before.Wood} " +
                      $"i{built.Iron}/{before.Iron} s{built.Stone}/{before.Food} g{built.Coins}/{before.Coins})");

            // BuildStakes is a READ plus arithmetic. It must not move a single point on its own.
            Bank.AssertUnchanged(f, "build (compute must not take)", before, s);
        }

        /// <summary>
        /// ** B, behavioural. A collector holding 800 BREAKS and KEEPS ALL 800.
        /// This is the no-double-bill proof: the bank is the only pool a siege bills.
        /// </summary>
        private static void CollectorKeepsItsPendingCase(List<string> f, GameState s, ResourceCollector collector)
        {
            double pendingBefore = collector.PendingAmount;
            if (Mathf.RoundToInt((float)pendingBefore) != 800)
            { f.Add($"collector case: fixture pending is {pendingBefore}, expected the authored 800"); return; }

            var bank = Bank.Of(s);

            // BREAK IT through the real damage surface -- no reflection on the behaviour under test.
            collector.ApplyContactDamage(10000f);

            if (!collector.IsBroken)
            { f.Add("collector case: the collector survived 10,000 damage -- the fixture never broke"); return; }

            int pendingAfter = Mathf.RoundToInt((float)collector.PendingAmount);
            if (pendingAfter != 800)
                f.Add($"[HARD-RULE]collector case: a broken collector's pending fell 800 -> {pendingAfter}. " +
                      "COLLECTOR LOOTING IS REMOVED (owner ruling 2026-08-27: bank theft REPLACES it, and a " +
                      "siege bills ONCE per attack). A collector steal on top of the bank debit charges the " +
                      "player TWICE for one siege.");

            if (Mathf.RoundToInt(collector.LastLootStolen) != 0)
                f.Add($"[HARD-RULE]collector case: LastLootStolen is {collector.LastLootStolen}, and it must " +
                      "be 0 forever -- the report would otherwise announce a second, phantom loss beside " +
                      "the bank debit the player was actually charged.");

            // Breaking a structure is stake (1) -- the STRUCTURE. It bills no resources by itself.
            Bank.AssertUnchanged(f, "collector break (structure loss is not a bill)", bank, s);
        }

        // =====================================================================
        //  Helpers
        // =====================================================================

        private static void AssertMoved(List<string> f, string word, int before, int after, int claimed)
        {
            int moved = before - after;
            if (moved == claimed) return;

            f.Add($"[ONE-NUMBER]debit: the report says {claimed} {word} was taken, the WALLET moved by " +
                  $"{moved} ({before} -> {after}). These are supposed to be ONE value -- the report renders " +
                  "the very buckets ApplyStakes spends. A report that lies about a loss is worse than no " +
                  "report, and an unexplained shrinking number is the resented version of this mechanic.");
        }

        /// <summary>Every wallet number a siege could conceivably move, snapshotted.</summary>
        private struct Bank
        {
            public int Wood, Iron, Food, Crystals, Coins;

            public static Bank Of(GameState s) => new Bank
            {
                Wood = s.Wood,
                Iron = s.Iron,
                Food = s.Resources.Stone,
                Crystals = s.Resources.Crystals,
                Coins = s.Resources.Coins,
            };

            /// <summary>Asserts NOT ONE POINT moved -- for the paths that must cost nothing at all.</summary>
            public static void AssertUnchanged(List<string> f, string tag, Bank before, GameState s)
            {
                var now = Of(s);
                if (now.Wood == before.Wood && now.Iron == before.Iron && now.Food == before.Food
                    && now.Crystals == before.Crystals && now.Coins == before.Coins) return;

                f.Add($"[HARD-RULE]{tag}: THE WALLET MOVED on a path that must cost nothing " +
                      $"(w{before.Wood}->{now.Wood} i{before.Iron}->{now.Iron} " +
                      $"s{before.Food}->{now.Food} c{before.Crystals}->{now.Crystals} " +
                      $"g{before.Coins}->{now.Coins}). Every point a siege takes must be attached to the " +
                      "ledger on a report the player can open -- a silent debit is the resented version " +
                      "of this mechanic.");
            }
        }

        private static DefenseOutcomeRecord Record(DefenseOutcome outcome)
        {
            var r = DefenseOutcomeRecord.NewEmpty();
            r.Outcome = outcome;
            r.WaveId = 9;
            r.StartedAtUnixMs = TimeSource.NowUnixMs() - 1000.0;
            return r;
        }

        private static PlacedStructureData Placed(string id, int x, int z, int level)
        {
            var p = new PlacedStructureData();
            p.itemId = id; p.cellX = x; p.cellZ = z; p.yawSteps = 0; p.level = level;
            return p;
        }

        /// <summary>Sum of every placed structure's level -- the cheapest proof nothing downgraded.</summary>
        private static int LevelsOf(GameState s)
        {
            int total = 0;
            if (s == null || s.BaseLayout == null) return 0;
            for (int i = 0; i < s.BaseLayout.Count; i++) total += s.BaseLayout[i].level;
            return total;
        }

        /// <summary>
        /// Seeds a private field on the fixture collector. Reflection is confined to SEEDING the
        /// fixture -- never to the behaviour under test: the break goes through the real
        /// <c>ApplyContactDamage</c> surface and the pending is read off the real public property.
        /// </summary>
        private static bool SetPrivate(object target, string field, object value, out string err)
        {
            err = null;
            var fld = target.GetType().GetField(field, BindingFlags.NonPublic | BindingFlags.Instance);
            if (fld == null)
            {
                err = $"ResourceCollector.{field} not found by reflection (the fixture seam changed)";
                return false;
            }
            fld.SetValue(target, value);
            return true;
        }

        /// <summary>Sets the private static backing field behind <c>EconomyService.Instance</c>.
        /// Mirrors HeadlessState.TrySetInstance; false (named) only if the seam is gone.</summary>
        private static bool TrySetEconomyInstance(EconomyService svc, out string err)
        {
            err = null;
            var fld = typeof(EconomyService).GetField("<Instance>k__BackingField",
                BindingFlags.NonPublic | BindingFlags.Static);
            if (fld == null)
            {
                err = "EconomyService.Instance backing field not found by reflection (singleton seam changed)";
                return false;
            }
            fld.SetValue(null, svc);
            return true;
        }
    }
}

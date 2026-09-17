// =============================================================================
// StarterArmyGrantRegression [starter-army]
// -----------------------------------------------------------------------------
// Assembly: DeNelle.EditorRegression.  Markers: STARTER_ARMY_OK / _FAIL.
//
// WO-1374 / north-star map section 2 - "the first army is free".
//
//   Owner: "A player starts with 200 gold but needs 1,650 to participate in the
//   thing you're trying to teach them. That's basically putting a nightclub
//   behind a velvet rope and handing the player twelve cents."
//   "On Barracks completion, grant 3 free Footmen (a starter raid squad)."
//
// WO-1803 (2026-09-16) re-sized it and made it a COMPOSITION. Owner, verbatim:
//   "Instead of giving them three troops, because that's shit, let's give them ten
//    troops. Let's give them five footmen and five archers."
// So cases A2/A4/D/E were added: the squad is asserted PER TROOP ID (a total-only
// count would pass ten Footmen, the exact thing she rejected), it must FIT the fresh
// housing cap, the derived split must lose no body at any knob value, and the ten must
// actually be fieldable against the first camp's garrison.
//
// -----------------------------------------------------------------------------
// THE WAYS THIS FEATURE CAN GO WRONG, EACH WITH A CASE.
// -----------------------------------------------------------------------------
//   (A) IT NEVER FIRES  - the player still faces the velvet rope. Case A drives
//       the real grant against a fixture GameState and counts the roster.
//   (B) IT FIRES TWICE  - a free squad on every Barracks makes demolish-and-
//       rebuild a troop faucet, which is a worse bug than the one being fixed
//       because it is silent and compounding. Case B calls TryGrant again on the
//       SAME state and requires zero.
//   (C) IT CHARGES      - a "free" army that quietly spends is the velvet rope
//       with extra steps. Case C is a source lint for any spend on that path.
//
// PROVEN RED FIRST: every case in (A) fails by construction against the
// pre-WO-1374 tree, where StarterArmyGrant did not exist. Case (B) was red-proved
// by temporarily latching with a Has/Mark PAIR instead of the single
// MarkEverAcquired call - the pair grants twice if two callers read before either
// writes, and B catches the second grant.
//
// -----------------------------------------------------------------------------
// (!) WHY THIS IS TESTABLE AT ALL WITHOUT A SCENE.
// -----------------------------------------------------------------------------
// StarterArmyGrant.TryGrant is deliberately a PUBLIC STATIC taking a GameState,
// with the MonoBehaviour poll as a thin caller. A grant buried inside Update()
// would be provable only in PlayMode, i.e. not provable in the batchmode gate,
// i.e. not actually pinned. The shape is the assertion.
// =============================================================================

using System.Collections.Generic;
using System.Text;
using UnityEngine;
using DeNelle.Core.State;
using DeNelle.Village;
using DeNelle.Village.Hero;

namespace DeNelle.Editor.Regression
{
    /// <summary>
    /// Pins the WO-1374 free starter squad: it fires, it fires exactly once per
    /// save, it grants the right unit, and it costs nothing.
    /// </summary>
    public static class StarterArmyGrantRegression
    {
        /// <summary>
        /// The OWNER'S number, as a literal. Never read off the knob being checked - a case that
        /// reads its expectation from the thing under test proves only that the code agrees with
        /// itself. WO-1803, 2026-09-16: <i>"let's give them ten troops. Let's give them five
        /// footmen and five archers."</i> (Was 3 from WO-1374 until that ruling.)
        /// </summary>
        private const int MapStarterCount = 10;

        /// <summary>The melee half of the owner's composition, as a literal.</summary>
        private const int MapStarterFootmen = 5;

        /// <summary>The ranged half of the owner's composition, as a literal.</summary>
        private const int MapStarterArchers = 5;

        /// <summary>
        /// The FIRST camp the free squad is sized against - the one the FTUE points at.
        /// Its garrison is AUTHORED (scene-configs.json), deliberately not restated here:
        /// case E reads it through <c>RaidSelectionVM.GarrisonCount</c> so a re-tune of the
        /// camp cannot leave a stale copy in this file (CLAUDE.md sections 2/5/8/16).
        /// </summary>
        private const string FirstCampConfigId = "raider_camp_small";

        public static bool Run(out string reason)
        {
            var failures = new List<string>();
            var log = new StringBuilder();
            log.AppendLine("--- STARTER ARMY (WO-1374, map section 2) ---");

            // The funnel latches in PlayerPrefs and GrantTrainedTroop emits step 2, so the
            // run is fenced the same way RaidFunnelRegression fences itself: nothing this
            // suite does may survive it, or it becomes un-rerunnable.
            var snapshot = RaidFunnelRegression.SnapshotFunnelPrefs();

            try
            {
                // =============================================================
                //  (A) IT FIRES, AND GRANTS THE RIGHT THING.
                // =============================================================
                // ⚠ GameState is a ScriptableObject, so it is CreateInstance'd, never
                // `new`ed - a `new GameState()` compiles nowhere and, in the shapes where it
                // would, produces an object Unity never initialised.
                var state = ScriptableObject.CreateInstance<GameState>();
                state.Army = new ArmyStorage();
                state.EverAcquiredItemIds = new List<string>();

                int granted = StarterArmyGrant.TryGrant(null, state);
                int roster = state.Army.Owned != null ? state.Army.Owned.Count : 0;
                log.AppendLine("  first grant -> " + granted + " troop(s), roster " + roster);

                if (granted != MapStarterCount)
                    failures.Add("[A1] the first Barracks granted " + granted + " troops, the owner said " +
                                 MapStarterCount + " ('let's give them ten troops', WO-1803 2026-09-16)");
                if (roster != MapStarterCount)
                    failures.Add("[A1] the roster holds " + roster + " after the grant, expected " + MapStarterCount);

                // =============================================================
                //  (A2) THE COMPOSITION, BY TROOP ID. WO-1803's whole point.
                // =============================================================
                // Counting the roster's TOTAL would pass a squad of ten Footmen, which is the
                // exact thing the owner rejected ("five footmen and five archers"). So the
                // assertion is per-id, against literals.
                int sawFootmen = 0, sawArchers = 0, sawOther = 0;
                if (state.Army.Owned != null)
                {
                    foreach (var t in state.Army.Owned)
                    {
                        if (t == null) { failures.Add("[A2] a null troop landed in the roster"); continue; }
                        if (t.TroopDefId == StarterArmyGrant.StarterTroopId) sawFootmen++;
                        else if (t.TroopDefId == StarterArmyGrant.ArcherTroopId) sawArchers++;
                        else
                        {
                            sawOther++;
                            failures.Add("[A2] the starter squad contains '" + t.TroopDefId + "', which is " +
                                         "neither '" + StarterArmyGrant.StarterTroopId + "' nor '" +
                                         StarterArmyGrant.ArcherTroopId + "' - the owner named exactly two units");
                        }
                        if (t.Wounded)
                            failures.Add("[A2] a starter troop arrived WOUNDED - the squad must be deployable " +
                                         "immediately, or the first raid is not minutes away");
                    }
                }
                log.AppendLine("  composition -> " + sawFootmen + " x footman + " + sawArchers +
                               " x archer (" + sawOther + " other)");
                if (sawFootmen != MapStarterFootmen)
                    failures.Add("[A2] the starter squad holds " + sawFootmen + " x '" +
                                 StarterArmyGrant.StarterTroopId + "', the owner said " + MapStarterFootmen +
                                 " ('five footmen and five archers', 2026-09-16)");
                if (sawArchers != MapStarterArchers)
                    failures.Add("[A2] the starter squad holds " + sawArchers + " x '" +
                                 StarterArmyGrant.ArcherTroopId + "', the owner said " + MapStarterArchers +
                                 " - a squad of ten identical melee bodies teaches nothing about composition");

                // A2b - both ids must be REAL, day-one-trainable defs. A typo'd id would still
                // land in the roster (GrantTrained is unconditional) and then draw a capsule with
                // no stats, which is a silent failure of exactly the kind section 12 forbids.
                foreach (var id in new[] { StarterArmyGrant.StarterTroopId, StarterArmyGrant.ArcherTroopId })
                {
                    var def = DeNelle.Village.TroopCatalog.Find(id);
                    if (def == null)
                    {
                        failures.Add("[A2b] '" + id + "' is not in troops.json - the free squad would be " +
                                     "bodies with no def, drawn as tinted capsules with no stats");
                        continue;
                    }
                    int slots = def.Slots > 0 ? def.Slots : 1;
                    if (slots != 1)
                        failures.Add("[A2b] '" + id + "' costs " + slots + " army slots, not 1 - the " +
                                     "starter squad is sized in BODIES against a housing cap counted in " +
                                     "SLOTS, so any slot cost above 1 overfills a fresh town");
                    if (def.UnlockBarracksTier > 1)
                        failures.Add("[A2b] '" + id + "' needs Barracks tier " + def.UnlockBarracksTier +
                                     " - the grant fires on the FIRST Barracks, so it would hand over a " +
                                     "unit the player is not yet allowed to have");
                }

                // =============================================================
                //  (A4) IT FITS. The housing proof WO-1803 asks for.
                // =============================================================
                // ArmyStorage.DefaultMaxArmySize is the fresh-save cap and both starter ids cost
                // 1 slot, so ten bodies = ten slots = exactly the cap. Asserted against the const,
                // never a literal 10, so raising either number without the other goes red here.
                if (StarterArmyGrant.DefaultStarterCount > ArmyStorage.DefaultMaxArmySize)
                    failures.Add("[A4] the starter squad is " + StarterArmyGrant.DefaultStarterCount +
                                 " troops but a fresh save houses only " + ArmyStorage.DefaultMaxArmySize +
                                 " (ArmyStorage.DefaultMaxArmySize) - the free squad would arrive OVER " +
                                 "CAP and close training on a brand-new town");
                int slotsUsed = state.Army.SlotsUsed(_ => 1);
                if (slotsUsed != MapStarterCount)
                    failures.Add("[A4] the granted squad occupies " + slotsUsed + " army slots, expected " +
                                 MapStarterCount + " (both starter units cost 1 slot)");
                // And the knob's own ceiling is that same const, not a literal that agrees today.
                string resolveSrc = RaidLootCurrencyRegression.ReadStripped("StarterArmyGrant.cs");
                if (resolveSrc != null && !resolveSrc.Contains("ArmyStorage.DefaultMaxArmySize"))
                    failures.Add("[A4] StarterArmyGrant.cs does not clamp the size knob to " +
                                 "ArmyStorage.DefaultMaxArmySize - a hand-copied ceiling there is the " +
                                 "duplicated state CLAUDE.md sections 2/5/8/16 each exist to forbid");

                // A3 - the ledger key is what makes it idempotent, and it must be namespaced
                // so it cannot be mistaken for an item id by VillageInventory's discovery reads.
                if (!state.HasEverAcquired(StarterArmyGrant.GrantLedgerKey))
                    failures.Add("[A3] the grant did not latch '" + StarterArmyGrant.GrantLedgerKey +
                                 "' in the acquired ledger - it will re-grant on the next launch");
                if (!StarterArmyGrant.GrantLedgerKey.StartsWith("grant.", System.StringComparison.Ordinal))
                    failures.Add("[A3] the idempotency key '" + StarterArmyGrant.GrantLedgerKey + "' is not " +
                                 "namespaced 'grant.' - it shares a list with real item ids, and a bare id " +
                                 "there makes a phantom item read as discovered");

                // =============================================================
                //  (B) IT NEVER FIRES TWICE. The faucet case.
                // =============================================================
                int again = StarterArmyGrant.TryGrant(null, state);
                int rosterAfter = state.Army.Owned != null ? state.Army.Owned.Count : 0;
                log.AppendLine("  second grant -> " + again + " troop(s), roster " + rosterAfter);
                if (again != 0)
                    failures.Add("[B1] a SECOND call granted " + again + " more troops. The squad is once per " +
                                 "save; re-granting turns demolish-and-rebuild into a troop faucet.");
                if (rosterAfter != MapStarterCount)
                    failures.Add("[B1] the roster grew to " + rosterAfter + " on the second call, expected it to " +
                                 "stay at " + MapStarterCount);

                // B2 - and a save that ALREADY carries the ledger key (a returning player, or
                // one whose barracks was destroyed and rebuilt) gets nothing at all.
                var returning = ScriptableObject.CreateInstance<GameState>();
                returning.Army = new ArmyStorage();
                returning.EverAcquiredItemIds = new List<string> { StarterArmyGrant.GrantLedgerKey };
                int forReturning = StarterArmyGrant.TryGrant(null, returning);
                if (forReturning != 0)
                    failures.Add("[B2] a save that already holds the ledger key was granted " + forReturning +
                                 " troops - a rebuilt Barracks must never re-issue the free squad");

                // B3 - a null state is a no-op, not a crash. Headless and pre-boot both hit this.
                if (StarterArmyGrant.TryGrant(null, null) != 0)
                    failures.Add("[B3] TryGrant(null state) granted troops - it must be a logged no-op");

                // =============================================================
                //  (C) IT IS FREE, AND THE COPY IS HONEST.
                // =============================================================
                string grantCode = RaidLootCurrencyRegression.ReadStripped("StarterArmyGrant.cs");
                if (grantCode == null)
                {
                    failures.Add("[C1] StarterArmyGrant.cs not found under Assets/_Modules - the free-ness lint " +
                                 "cannot run, and a lint that silently skips is worse than none");
                }
                else
                {
                    string[] spends = { "TrySpend", "CanAfford", "costGold", "SpendCrystals", "Coins" };
                    foreach (var s in spends)
                        if (grantCode.Contains(s))
                            failures.Add("[C1] StarterArmyGrant.cs live code contains '" + s + "' - the starter " +
                                         "squad must be FREE. A grant that charges is the velvet rope with " +
                                         "extra steps, and it would also make this feature depend on the " +
                                         "troop-cost fork WO-1374 is blocked on.");
                    // It must go through the ONE roster owner, so funnel step 2 fires from the
                    // same place a paid train fires it.
                    if (!grantCode.Contains("BarracksProgression.GrantTrainedTroop"))
                        failures.Add("[C1] StarterArmyGrant.cs does not grant through " +
                                     "BarracksProgression.GrantTrainedTroop - a second roster-write path means " +
                                     "'army trained' can be reached by one route and missed by the other");
                }

                // C2 - the toast counts what it granted, BY UNIT. A toast that says "10 Footmen"
                // while half the squad draws a bow is a small lie that costs trust in every other
                // number the game prints - and both the size and the split are derived, so this
                // is reachable, not theoretical.
                var toastCases = new[]
                {
                    new[] { MapStarterFootmen, MapStarterArchers },   // the shipping 5/5
                    new[] { 2, 1 },                                   // the old size, split
                    new[] { 1, 1 },                                   // both singular
                    new[] { 1, 0 },                                   // melee only
                    new[] { 0, 1 },                                   // ranged only
                };
                foreach (var tc in toastCases)
                {
                    int f = tc[0], a = tc[1];
                    string toast = StarterArmyGrant.GrantToastFor(f, a);
                    string label = f + "f/" + a + "a";
                    if (string.IsNullOrEmpty(toast)) { failures.Add("[C2] GrantToastFor(" + label + ") is empty"); continue; }
                    if (f > 0 && !toast.Contains(f + " " + (f == 1 ? "Footman" : "Footmen")))
                        failures.Add("[C2] the " + label + " toast does not name '" + f + " " +
                                     (f == 1 ? "Footman" : "Footmen") + "': \"" + toast + "\"");
                    if (a > 0 && !toast.Contains(a + " " + (a == 1 ? "Archer" : "Archers")))
                        failures.Add("[C2] the " + label + " toast does not name '" + a + " " +
                                     (a == 1 ? "Archer" : "Archers") + "': \"" + toast + "\"");
                    if (f == 0 && toast.IndexOf("Footm", System.StringComparison.Ordinal) >= 0)
                        failures.Add("[C2] the " + label + " toast promises Footmen the player did not " +
                                     "receive: \"" + toast + "\"");
                    if (a == 0 && toast.IndexOf("Archer", System.StringComparison.Ordinal) >= 0)
                        failures.Add("[C2] the " + label + " toast promises Archers the player did not " +
                                     "receive: \"" + toast + "\"");
                    // The map's FTUE line points at Journey -> Raids, and it must say the SAME
                    // thing the Game Guide now says. One destination, worded one way.
                    if (toast.IndexOf("Journey", System.StringComparison.Ordinal) < 0 ||
                        toast.IndexOf("Raids", System.StringComparison.Ordinal) < 0)
                        failures.Add("[C2] the grant toast does not direct the player to Journey -> Raids: \"" +
                                     toast + "\". The map's whole point is that the first raid happens within " +
                                     "MINUTES, which requires telling them where it is.");
                    foreach (char c in toast)
                        if (c > 126 || c < 32)
                        { failures.Add("[C2] the grant toast is not 7-bit ASCII (mobile font-atlas law): \"" + toast + "\""); break; }
                }

                // C3 - the squad size is on the tunable rail, clamped, and answers the owner's
                // number with no override present.
                int resolved = StarterArmyGrant.ResolveCount();
                if (resolved != MapStarterCount)
                    failures.Add("[C3] ResolveCount() answered " + resolved + " with no override, expected the " +
                                 "shipping default " + MapStarterCount);
                int resF, resA;
                StarterArmyGrant.ResolveComposition(out resF, out resA);
                if (resF != MapStarterFootmen || resA != MapStarterArchers)
                    failures.Add("[C3] the shipping composition resolves to " + resF + " Footmen + " + resA +
                                 " Archers, the owner said " + MapStarterFootmen + " + " + MapStarterArchers);

                // =============================================================
                //  (D) THE SPLIT RULE ITSELF. Total honours the knob, remainder to melee.
                // =============================================================
                // Pure and total-driven, so every value the knob can take is checkable without a
                // tunable override seam. The PROPERTY is what matters: footmen + archers == total
                // at EVERY value, so no setting of the knob can silently drop a body.
                var splitCases = new[]
                {
                    new[] { 10, 5, 5 },   // the shipping value, the owner's words
                    new[] {  3, 2, 1 },   // the retired WO-1374 value
                    new[] {  2, 1, 1 },
                    new[] {  1, 1, 0 },   // one body -> melee, never a lone archer
                    new[] {  0, 0, 0 },   // the grant disabled
                    new[] { -4, 0, 0 },   // a negative knob is not a negative squad
                    new[] {  7, 4, 3 },
                };
                foreach (var sc in splitCases)
                {
                    int total = sc[0];
                    int f, a;
                    StarterArmyGrant.SplitComposition(total, out f, out a);
                    int expectTotal = total > 0 ? total : 0;
                    if (f != sc[1] || a != sc[2])
                        failures.Add("[D1] SplitComposition(" + total + ") -> " + f + "/" + a +
                                     ", expected " + sc[1] + "/" + sc[2] + " (even, remainder to Footmen)");
                    if (f + a != expectTotal)
                        failures.Add("[D1] SplitComposition(" + total + ") loses bodies: " + f + " + " + a +
                                     " = " + (f + a) + ", not " + expectTotal + ". The knob is the TOTAL " +
                                     "and the split must never silently truncate it.");
                    if (a > f)
                        failures.Add("[D1] SplitComposition(" + total + ") put the remainder in the RANGED " +
                                     "half (" + f + "/" + a + ") - a front line the archers can stand " +
                                     "behind is the readable default");
                }

                // =============================================================
                //  (E) THE TEN CAN ACTUALLY MARCH. The deploy + door proof.
                // =============================================================
                // A grant that fits housing but cannot be FIELDED would be ten troops the player
                // watches from the deploy tray, which is worse than three they can use. So this
                // drives the real readiness formula over the real granted roster (case A's state),
                // and then the real door word over the real Camp I def.
                var readiness = ArmyReadiness.Compute(state.Army, slotsUsed, 0);
                log.AppendLine("  readiness -> deployable " + readiness.DeployableSlots + " / required " +
                               readiness.RequiredSlots + " / cap " + readiness.CapSlots +
                               ", ready=" + readiness.Ready);
                if (readiness.DeployableSlots != MapStarterCount)
                    failures.Add("[E1] the deploy tray would field " + readiness.DeployableSlots +
                                 " of the " + MapStarterCount + " granted troops");
                if (!readiness.Ready)
                    failures.Add("[E1] a save holding the full free squad reads NOT READY to raid " +
                                 "(deployable " + readiness.DeployableSlots + " vs required " +
                                 readiness.RequiredSlots + ") - the grant exists to open that door");

                // E2 - Camp I. Its authored garrison is 9 (scene-configs.json, read at source
                // 2026-09-16), so ten deployable bodies must clear the door's warning word while
                // the OLD three did not. Both halves are asserted: a word that never appears is
                // as broken as one that always does.
                var campI = SceneConfigCatalog.Find(FirstCampConfigId);
                if (campI == null)
                {
                    failures.Add("[E2] scene config '" + FirstCampConfigId + "' not found - the first " +
                                 "camp the free squad is sized against cannot be read, and a check that " +
                                 "silently skips is worse than none");
                }
                else
                {
                    int garrison = RaidSelectionVM.GarrisonCount(campI);
                    string wordAtTen = RaidSelectionVM.ArmyWarnWord(campI, MapStarterCount);
                    string wordAtThree = RaidSelectionVM.ArmyWarnWord(campI, 3);
                    log.AppendLine("  camp I '" + FirstCampConfigId + "' garrison " + garrison +
                                   " -> word at " + MapStarterCount + ": " + (wordAtTen ?? "(none)") +
                                   " | at 3: " + (wordAtThree ?? "(none)"));
                    if (garrison > MapStarterCount)
                        failures.Add("[E2] Camp I garrisons " + garrison + " defenders against a free " +
                                     "squad of " + MapStarterCount + " - the first camp is still above " +
                                     "the army the game hands out, which is the wall this ticket removes");
                    if (wordAtTen != null)
                        failures.Add("[E2] the raid grid still prints \"" + wordAtTen + "\" on Camp I with " +
                                     "the full free squad fielded - the door's copy disagrees with the " +
                                     "army the game just gave the player");
                    if (wordAtThree == null)
                        failures.Add("[E2] the door prints NO warning on Camp I even at 3 deployable, so " +
                                     "[E2]'s pass at " + MapStarterCount + " proves nothing - the compare " +
                                     "has gone inert");
                }
            }
            finally
            {
                RaidFunnelRegression.RestoreFunnelPrefs(snapshot);
            }

            if (failures.Count == 0)
            {
                reason = "STARTER ARMY OK - the first Barracks grants " + MapStarterCount + " free deployable " +
                         "troops as an AUTHORED COMPOSITION (" + MapStarterFootmen + " x '" +
                         StarterArmyGrant.StarterTroopId + "' + " + MapStarterArchers + " x '" +
                         StarterArmyGrant.ArcherTroopId + "', both 1-slot tier-1 defs in troops.json) through " +
                         "the one roster owner; the total rides raid.starterArmySize and the split is derived " +
                         "even-with-remainder-to-melee at every value with no body lost; the squad fits a fresh " +
                         "town EXACTLY (" + ArmyStorage.DefaultMaxArmySize + " slots, the knob clamped to that " +
                         "same const); the full squad reads READY and clears Camp I's garrison so the door stops " +
                         "saying Outmatched; it latches on the monotonic acquired-ledger so a second call, a " +
                         "rebuilt Barracks and a save that already took the old 3-troop squad all grant nothing; " +
                         "no-ops on a null state; spends no resource of any kind; and names both units and their " +
                         "real counts in 7-bit ASCII pointing at Journey -> Raids";
                Debug.Log(log.ToString() + "STARTER_ARMY_OK");
                return true;
            }

            reason = "starter-army: " + string.Join("; ", failures);
            Debug.LogError(log.ToString() + "STARTER_ARMY_FAIL: " + reason);
            return false;
        }
    }
}

// =============================================================================
// RaidSelectionSpoilsRegression - the Raid Selection rows say what a raid PAYS,
// the pips carry data or nothing, and a camp above the army says so in words.
// -----------------------------------------------------------------------------
// Assembly: DeNelle.EditorRegression   Namespace: DeNelle.Editor.Regression
//
// WO-1402 (merged UI review 2026-09-05 row 1). WHAT WAS BROKEN, SEEN ON THE FRAME
// Builds/ui-capture/RaidSelection_2670x1200.png (09-05 07:02):
//   1. Rows read "Wood walls . 9 defenders" + a difficulty word and a "- x1.5 Loot"
//      hint. No row carried a resource word or number: nothing said what a win is
//      WORTH. RaidSelectionVM.cs:133 read rewardMultiplier only - the camp authors a
//      multiplier, not a loot list; the real spoils were computed once, at settle
//      (RaidScoring.LootFor -> ComputeLoot, surfaced by EndStateVM.FromRaidVictory).
//   2. Three identical gold pips (StarRatingRow.Build(.., 3, 3, ..)) sat on every row
//      and varied on none - they carried nothing.
//   3. Nothing compared the camp to the army the player has. The scout report on the
//      deploy screen compares ("Garrison: 9 defenders - you field 3", WO-1389); the
//      selection rows did not.
//
// WHAT THIS PINS (behaviour first, source lint last):
//   A. Every row VM exposes a non-empty spoils line: "Spoils: ~<n> wood, ~<n> iron,
//      ~<n> gold" - starts with the prefix, names wood AND iron, every number is a
//      "~" estimate (owner ruling: a range/estimate, never exact), ASCII only.
//   B. ONE PRODUCER. The line equals FormatSpoils(RaidScoring.EstimateSpoils(id, mult)),
//      and EstimateSpoils equals ComputeLoot at the 3-star rung with the live tunable
//      bases - i.e. the settle payout's own arithmetic, no second table. A x1.5 camp
//      estimates 1.5x the wood of a x1.0 camp (wood and iron ride the multiplier; gold
//      does not - RaidLootTunables header).
//   C. THE ARMY WORD - A WARNING SINCE WO-1542, NEVER A LOCK. With 0 fieldable troops
//      every garrisoned camp reads "Outmatched - Army <garrison> advised"; with an army
//      that covers the garrison the word is absent; with the army UNKNOWN (-1, headless)
//      it is absent - a frame must never print advice it cannot prove. The BEGIN ASSAULT
//      confirm toast fires on the SAME predicate from the SAME producer, and neither the
//      word nor the toast may read as a refusal (the door is unchanged, WO-1379 PIN F).
//   C2. WO-1562 - A CLEARED CAMP IS MARKED, from RaidClaimService through ClaimedProvider
//      and never a second claim predicate, and the marker DISCLOSES the live repeat-clear
//      rate formatted off RaidClaimService.RepeatClearLootMultiplier (never typed).
//   C3. WO-1562 - A VICTORY ANNOUNCES A CROSSED LADDER RUNG and stays silent otherwise,
//      reading the catalog's authored unlockVictories - the same authority the grid's
//      lock sentences read, so there is one ladder and no second copy of the thresholds.
//   D. THE PIPS. ShowStarPips is false with no rating producer (today's wiring), false
//      when every known rating is identical, true only when known ratings differ.
//   E. SOURCE LINT (the MVVM seam): RaidSelectionVM calls RaidScoring.EstimateSpoils and
//      types no spoils literal; RaidScoring.LootFor and .EstimateSpoils both route
//      through ProjectLoot; RaidSelectionScreen reads SpoilsLineFor / ArmyWarnWordFor /
//      ClearedWordFor / ShowStarPips, wires ClaimedProvider, gates StarRatingRow.Build
//      behind ShowStarPips, and types neither "Spoils:" nor "LOCKED" itself (the VM owns
//      the words).
//   F. THE BANDS ARE TALL ENOUGH TO RENDER. Every entry of RaidSelectionScreen.CardBands
//      must satisfy HavePx >= NeedsPx. This case exists because on 2026-09-05 the WO-1402
//      spoils line shipped INVISIBLE and no suite noticed: the VM composed it
//      (Builds/cap2:13574), the View built it (cap2:13909), and TMP's Ellipsis overflow
//      then culled the entire line because its band was 22.7 px and a 22 pt line needs
//      ~29. FitSingleLine cannot save it - the kit clamps fontSizeMin UP to the label's
//      own fontSize, so there is no shrink room - and the runtime relax guard does not
//      run in a headless capture. Four of the card's five rows were gone (clock, lock
//      sentence, spoils, canon flavour) and only a pixel scan of the screenshot found it.
//      A band is not "tight" when it fails this: it renders NOTHING, silently.
//
// MUTATIONS THIS SUITE CATCHES (named, so the RED is reproducible):
//   M1. In RaidSelectionVM.ArmyWarnWord, delete the `garrison > deployableTroops`
//       compare (return null) -> C fails on every garrisoned camp, AND its confirm-toast
//       parity half fails, because the grid word and the BEGIN ASSAULT confirm read the
//       one predicate.
//   M1b. Put the word back to "LOCKED - needs Army " -> C fails on the advice-not-a-lock
//       assertion. That is WO-1542 acceptance 4 held from the word's side: a card face
//       may not claim a refusal the tap never gives, and the tap is deliberately
//       unchanged (no readiness gate; HeartfireRegression PIN F owns the door).
//   M1c. Make RaidDeployScreen.OnDeploy `return` after the outmatch toast without ever
//       calling AcknowledgeOutmatch -> the confirm becomes a permanent refusal, i.e. the
//       second gate WO-1379 forbids. Held by the toast's own wording assertion plus the
//       latch in RaidDeployVM.
//   M2. In RaidSelectionVM.Rebuild, replace `EstimateSpoils(d)` with a literal basket
//       (`new ResourceCost(wood: 600, iron: 250)`) -> B fails (line != the scorer's
//       estimate) and E fails (no RaidScoring.EstimateSpoils call).
//   M3. In RaidSelectionScreen.CreateRaidCard, drop the `if (showPips)` guard so the
//       pips draw unconditionally -> E fails (Build reached before ShowStarPips read).
//   M4. In RaidSelectionScreen, restore the shipped-and-invisible geometry - set
//       CardHeightPx back to 142f, or SpoilsBandY0/Y1 back to 0.18f/0.34f -> F fails
//       naming the band, its px, and the px its font needs. (At 142f with today's
//       fractions FOUR of the five bands red: title 32.7/38.6, scout 24.9/29.1, lock
//       24.9/29.1, spoils 24.9/29.1 - flavour squeaks through at 24.9/24.4 because 18 pt
//       is the cheapest row. That is why the card is 178: it cannot seat five legible
//       rows at 142, and the four that fail are four rows the player cannot see.)
//
// Contract mirrors the other suites - Run(out string reason): true = pass.
// Orchestrator registration (DataRegression.RunAll), covenant style:
//   if (!RaidSelectionSpoilsRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[raid-selection-spoils] " + r);
// =============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using DeNelle.Village;
using DeNelle.Village.Hero;

namespace DeNelle.Editor.Regression
{
    public static class RaidSelectionSpoilsRegression
    {
        private const string VmRel     = "_Modules/Village/Hero/RaidSelectionVM.cs";
        private const string ScreenRel = "_Modules/Village/Hero/RaidSelectionScreen.cs";
        private const string ScorerRel = "_Modules/Village/Troops/RaidScoring.cs";

        // The ladder as scene-configs.json authors it on 2026-09-05 (garrison headcounts are
        // the composition sums: 7+2 / 4+2+6+3 / 7+5+7 / 7+5+7). Fakes, so the suite proves the
        // VM's arithmetic and not the catalog's presence; the catalog is pinned elsewhere
        // (RaidEscalationRegression A/B).
        private sealed class Camp
        {
            public string Id; public string Name; public float Mult; public int Unlock; public int[] Garrison;
        }

        private static readonly Camp[] Ladder =
        {
            new Camp { Id = "raider_camp_small",  Name = "The Forsaken Camp",   Mult = 1.0f, Unlock = 0,  Garrison = new[] { 7, 2 } },
            new Camp { Id = "fortified_garrison", Name = "The Broken Garrison", Mult = 1.5f, Unlock = 0,  Garrison = new[] { 4, 2, 6, 3 } },
            new Camp { Id = "mage_enclave",       Name = "The Veiled Enclave",  Mult = 2.2f, Unlock = 0,  Garrison = new[] { 7, 5, 7 } },
            new Camp { Id = "iron_bastion",       Name = "The Iron Bastion",    Mult = 2.2f, Unlock = 0,  Garrison = new[] { 7, 5, 7 } },
        };

        public static void RunStandalone() { Run(out _); }

        public static bool Run(out string reason)
        {
            var failures = new List<string>();
            var log = new StringBuilder();
            log.AppendLine("--- RAID SELECTION SPOILS (WO-1402: rows say what a raid pays; pips carry data or nothing; army lock is a word) ---");

            var defs = BuildDefs();

            CheckSpoilsLines(defs, failures, log);          // A
            CheckOneProducer(defs, failures, log);          // B
            CheckArmyLockWord(defs, failures, log);         // C
            CheckClearedMarker(defs, failures, log);       // C2 (WO-1562 part 2)
            CheckUnlockAnnouncement(defs, failures, log);  // C3 (WO-1562 part 1)
            CheckPipsGate(defs, failures, log);             // D
            CheckSourceSeams(failures, log);                // E
            CheckCardBands(failures, log);                  // F

            if (failures.Count == 0)
            {
                Debug.Log(log.ToString() + "RAID_SELECTION_SPOILS_OK");
                reason = "RAID SELECTION SPOILS OK - every row carries a '~' spoils line from RaidScoring's own " +
                         "estimate (one producer), the pips hide until ratings vary, a camp above the army reads " +
                         "'Outmatched - Army N advised' in words (a WARNING - the card stays tappable and the door " +
                         "is unchanged) with a matching BEGIN ASSAULT confirm from the same predicate, a CLEARED " +
                         "camp is marked from RaidClaimService and discloses the live repeat rate, and a victory " +
                         "announces only a ladder rung it actually crossed";
                return true;
            }

            reason = "raid-selection-spoils: " + string.Join("; ", failures);
            Debug.LogError(log.ToString() + "RAID_SELECTION_SPOILS_FAIL: " + reason);
            return false;
        }

        private static List<SceneConfigDef> BuildDefs()
        {
            var defs = new List<SceneConfigDef>();
            foreach (var c in Ladder)
            {
                var comp = new List<GarrisonUnitDef>();
                for (int i = 0; i < c.Garrison.Length; i++)
                    comp.Add(new GarrisonUnitDef { enemyId = "fake-" + i, count = c.Garrison[i] });
                defs.Add(new SceneConfigDef
                {
                    id = c.Id, displayName = c.Name, ownership = "Enemy", sceneName = "RaidBase_" + c.Id,
                    unlockVictories = c.Unlock, rewardMultiplier = c.Mult, wallTier = "Wood",
                    garrison = new GarrisonDef { composition = comp },
                });
            }
            return defs;
        }

        private static int GarrisonOf(Camp c)
        {
            int n = 0;
            foreach (var g in c.Garrison) n += g;
            return n;
        }

        // -- A: every row says what it pays ---------------------------------------------
        private static void CheckSpoilsLines(List<SceneConfigDef> defs, List<string> failures, StringBuilder log)
        {
            using (var vm = new RaidSelectionVM(defs, null, 20, _ => true, deployableTroops: 0))
            {
                foreach (var c in Ladder)
                {
                    string line = vm.SpoilsLineFor(c.Id);
                    if (string.IsNullOrEmpty(line))
                    {
                        failures.Add($"A: '{c.Id}' has NO spoils line - the row still says only how hard the camp is, " +
                                     "never what a raid pays (the merged-review defect this WO exists for)");
                        continue;
                    }
                    if (!line.StartsWith(RaidSelectionVM.SpoilsPrefix, StringComparison.Ordinal))
                        failures.Add($"A: '{c.Id}' spoils line does not start with '{RaidSelectionVM.SpoilsPrefix}' - found \"{line}\"");
                    if (line.IndexOf(" wood", StringComparison.Ordinal) < 0)
                        failures.Add($"A: '{c.Id}' spoils line names no WOOD - \"{line}\"");
                    if (line.IndexOf(" iron", StringComparison.Ordinal) < 0)
                        failures.Add($"A: '{c.Id}' spoils line names no IRON - \"{line}\"");
                    if (line.IndexOf('~') < 0)
                        failures.Add($"A: '{c.Id}' spoils line carries no '~' - the owner ruled a RANGE/estimate, never exact - \"{line}\"");
                    foreach (char ch in line)
                        if (ch > 127) { failures.Add($"A: '{c.Id}' spoils line carries a non-ASCII character (tofu in the build font) - \"{line}\""); break; }
                    // An exact number would be a lie the settle screen has to keep: every amount
                    // must be rounded to the estimate grid (50 below 1000, 100 at/above).
                    foreach (var token in line.Substring(RaidSelectionVM.SpoilsPrefix.Length).Split(','))
                    {
                        string t = token.Trim();
                        int tilde = t.IndexOf('~');
                        int sp = t.IndexOf(' ');
                        if (tilde < 0 || sp < tilde) continue;
                        if (int.TryParse(t.Substring(tilde + 1, sp - tilde - 1), out int n) && RaidSelectionVM.Approx(n) != n)
                            failures.Add($"A: '{c.Id}' spoils amount {n} is not on the estimate grid (Approx({n}) = {RaidSelectionVM.Approx(n)}) - \"{line}\"");
                    }
                    log.AppendLine($"OK: '{c.Id}' -> \"{line}\"");
                }
            }
        }

        // -- B: one producer - the settle payout's own formula --------------------------
        private static void CheckOneProducer(List<SceneConfigDef> defs, List<string> failures, StringBuilder log)
        {
            using (var vm = new RaidSelectionVM(defs, null, 20, _ => true))
            {
                foreach (var def in defs)
                {
                    var est = RaidScoring.EstimateSpoils(def.id, def.rewardMultiplier);
                    string expected = RaidSelectionVM.FormatSpoils(est);
                    string actual = vm.SpoilsLineFor(def.id);
                    if (!string.Equals(expected, actual, StringComparison.Ordinal))
                        failures.Add($"B: '{def.id}' row line \"{actual}\" != FormatSpoils(RaidScoring.EstimateSpoils) " +
                                     $"\"{expected}\" - the row is fed by something other than the scorer's estimate (a second producer)");

                    // The estimate IS ComputeLoot at the 3-star rung with the live bases - the
                    // exact arithmetic LootFor pays through at settle.
                    var settle = RaidScoring.ComputeLoot(RaidScoring.EstimateStars, 0f,
                        RaidLootTunables.CrystalsBase, 0, RaidLootTunables.CrystalsPerStar, 0,
                        def.rewardMultiplier, RaidLootTunables.WoodBase, RaidLootTunables.IronBase,
                        RaidLootTunables.CoinsBaseFor(def.id));
                    if (settle.Wood != est.Wood || settle.Iron != est.Iron || settle.Coins != est.Coins)
                        failures.Add($"B: '{def.id}' EstimateSpoils = {est.Wood}w/{est.Iron}i/{est.Coins}g but ComputeLoot at the " +
                                     $"same rung = {settle.Wood}w/{settle.Iron}i/{settle.Coins}g - the preview and the payout have " +
                                     "forked; there must be exactly one formula");
                    if (est.Wood <= 0 || est.Iron <= 0)
                        failures.Add($"B: '{def.id}' estimate pays {est.Wood} wood / {est.Iron} iron - the tunable rail answered " +
                                     "zero, so the line would be empty on a real row (RaidLootTunables.WoodBase/IronBase)");
                }

                // Wood and iron ride the camp multiplier (RaidLootTunables header); gold does not.
                var flat = RaidScoring.EstimateSpoils("raider_camp_small", 1.0f);
                var hard = RaidScoring.EstimateSpoils("raider_camp_small", 1.5f);
                if (hard.Wood != Mathf.RoundToInt(flat.Wood * 1.5f))
                    failures.Add($"B: a x1.5 camp estimates {hard.Wood} wood against {flat.Wood} at x1.0 - the estimate does not " +
                                 "ride rewardMultiplier the way the settle payout does");
                if (hard.Coins != flat.Coins)
                    failures.Add($"B: gold estimate changed with the multiplier ({flat.Coins} -> {hard.Coins}) - gold escalates through " +
                                 "its per-camp base and must NOT ride rewardMultiplier (RaidLootTunables header)");
                log.AppendLine($"OK: estimate == ComputeLoot at the {RaidScoring.EstimateStars}-star rung; x1.0 -> {flat.Wood}w, x1.5 -> {hard.Wood}w, gold flat {flat.Coins}");
            }
        }

        // -- C: the army word ---------------------------------------------------------------
        //
        // WO-1542 (owner ruling 2026-09-06, "Warning, not a lock"): the word this case pins
        // CHANGED, and the change is the point. It read "LOCKED - needs Army N" while
        // OnCardTapped refused on exactly the escalation lock and Heartfire and then opened the
        // deploy screen anyway, under a lit BEGIN ASSAULT - a refusal the tap never gave. It now
        // reads "Outmatched - Army N advised": same fact, same producer, same predicate
        // (garrison BODIES > deployable BODIES), without claiming a gate.
        //
        // RED-FIRST NOTE: this case CANNOT COMPILE against the pre-change tree - ArmyWarnPrefix,
        // ArmyWarnWordFor and OutmatchConfirmToast do not exist there. That build failure IS the
        // honest red; the oracle asserts a contract whose absence was the defect.
        private static void CheckArmyLockWord(List<SceneConfigDef> defs, List<string> failures, StringBuilder log)
        {
            // Army 0: every garrisoned camp is above it.
            using (var vm = new RaidSelectionVM(defs, null, 20, _ => true, deployableTroops: 0))
            {
                foreach (var c in Ladder)
                {
                    string expected = RaidSelectionVM.ArmyWarnPrefix + GarrisonOf(c) + RaidSelectionVM.ArmyWarnSuffix;
                    string word = vm.ArmyWarnWordFor(c.Id);
                    if (!string.Equals(word, expected, StringComparison.Ordinal))
                        failures.Add($"C: with 0 fieldable troops '{c.Id}' (garrison {GarrisonOf(c)}) reads " +
                                     $"\"{word ?? "(null)"}\" - expected \"{expected}\"; the row must SAY the camp is above the army");
                }
                log.AppendLine("OK: army 0 -> every garrisoned camp carries the outmatch warning with its garrison count");
            }

            // Army 9: the Forsaken Camp (9) is covered, the Broken Garrison (15) is not.
            using (var vm = new RaidSelectionVM(defs, null, 20, _ => true, deployableTroops: 9))
            {
                if (vm.ArmyWarnWordFor("raider_camp_small") != null)
                    failures.Add("C: with 9 fieldable troops 'raider_camp_small' (garrison 9) still reads the warning - " +
                                 "the compare is garrison > army, and 9 is not above 9");
                string expect15 = RaidSelectionVM.ArmyWarnPrefix + "15" + RaidSelectionVM.ArmyWarnSuffix;
                if (vm.ArmyWarnWordFor("fortified_garrison") != expect15)
                    failures.Add($"C: with 9 fieldable troops 'fortified_garrison' (garrison 15) does not read \"{expect15}\"");
                log.AppendLine("OK: army 9 -> camp of 9 open, camp of 15 warned in words");
            }

            // Army covers everything: no words.
            using (var vm = new RaidSelectionVM(defs, null, 20, _ => true, deployableTroops: 99))
            {
                foreach (var c in Ladder)
                    if (vm.ArmyWarnWordFor(c.Id) != null)
                        failures.Add($"C: with 99 fieldable troops '{c.Id}' still reads \"{vm.ArmyWarnWordFor(c.Id)}\" - a covered camp must carry no warning");
            }

            // Army UNKNOWN (headless / unwired): no words, because none can be proven.
            using (var vm = new RaidSelectionVM(defs, null, 20, _ => true))
            {
                if (vm.DeployableTroops != RaidSelectionVM.Unknown)
                    failures.Add($"C: an unwired VM reports DeployableTroops={vm.DeployableTroops}, expected Unknown ({RaidSelectionVM.Unknown})");
                foreach (var c in Ladder)
                    if (vm.ArmyWarnWordFor(c.Id) != null)
                        failures.Add($"C: with the army UNKNOWN '{c.Id}' reads \"{vm.ArmyWarnWordFor(c.Id)}\" - a headless frame " +
                                     "must never print advice it cannot prove");
                log.AppendLine("OK: army unknown / army 99 -> no warning on any row");
            }

            // THE WORD IS ADVICE, NOT A LOCK - the WO-1542 ruling in one assertion. A word that
            // says LOCKED while the door opens anyway IS the defect, so it must not claim one.
            if (RaidSelectionVM.ArmyWarnPrefix.IndexOf("LOCK", StringComparison.OrdinalIgnoreCase) >= 0 ||
                RaidSelectionVM.ArmyWarnSuffix.IndexOf("LOCK", StringComparison.OrdinalIgnoreCase) >= 0)
                failures.Add("C: the army word says LOCK again. Owner ruling WO-1542: it is a WARNING - the card stays " +
                             "tappable and the door is unchanged, so a word claiming a refusal the tap never gives is " +
                             "the exact defect that ticket closed");
            if (RaidSelectionVM.ArmyWarnPrefix != "Outmatched - Army " ||
                RaidSelectionVM.ArmyWarnSuffix != " advised")
                failures.Add($"C: the army warning is \"{RaidSelectionVM.ArmyWarnPrefix}N{RaidSelectionVM.ArmyWarnSuffix}\" - " +
                             "WO-1542 spells it 'Outmatched - Army N advised'");

            // THE CONFIRM TOAST fires on exactly the same predicate, from the same producer, so
            // the grid warning and the BEGIN ASSAULT confirm can never disagree about which camp
            // is over-matched. That drift is what WO-1542 exists to close.
            foreach (var c in Ladder)
            {
                var def = FindDef(defs, c.Id);
                bool warned = RaidSelectionVM.ArmyWarnWord(def, 9) != null;
                bool asks = RaidSelectionVM.OutmatchConfirmToast(def, 9) != null;
                if (warned != asks)
                    failures.Add($"C: '{c.Id}' at army 9 warns={warned} but confirms={asks} - the grid word and the " +
                                 "BEGIN ASSAULT confirm read different predicates. One producer, or they drift");
            }
            string toast = RaidSelectionVM.OutmatchConfirmToast(FindDef(defs, "fortified_garrison"), 9);
            if (string.IsNullOrEmpty(toast))
                failures.Add("C: an over-matched camp composes no confirm toast - BEGIN ASSAULT would march silently");
            else
            {
                if (toast.IndexOf("15", StringComparison.Ordinal) < 0 || toast.IndexOf("9", StringComparison.Ordinal) < 0)
                    failures.Add($"C: the confirm toast \"{toast}\" does not carry BOTH numbers - the player must be told " +
                                 "what they are marching into, not merely that something is wrong");
                if (toast.IndexOf("annot", StringComparison.Ordinal) >= 0 ||
                    toast.IndexOf("LOCK", StringComparison.OrdinalIgnoreCase) >= 0)
                    failures.Add($"C: the confirm toast \"{toast}\" reads as a REFUSAL. It is a confirm STEP - it asks once " +
                                 "and never refuses (WO-1542; a second gate is the shape WO-1379 forbids)");
            }
        }

        // -- C2: WO-1562 - a cleared camp is marked, and it discloses the repeat rate ---------
        //
        // The clear was persisted by RaidClaimService.MarkClaimed from the victory seam and then
        // read by NOTHING: grepping the VM and the screen for RaidClaimService / IsClaimed /
        // Cleared returned comments only. So the return leg of the raid loop had no memory, and
        // nothing warned that a repeat clear pays a fraction - which the player then discovered
        // after committing, one screen later, on the deploy card.
        //
        // RED-FIRST NOTE: cannot compile against the pre-change tree (ClearedWordFor and the
        // claimed constructor argument do not exist there) - that is the honest red.
        private static void CheckClearedMarker(List<SceneConfigDef> defs, List<string> failures, StringBuilder log)
        {
            // Nothing claimed -> no marker anywhere. A grid must never advertise a win.
            using (var vm = new RaidSelectionVM(defs, null, 20, _ => true, 99, null, _ => false))
                foreach (var c in Ladder)
                    if (vm.ClearedWordFor(c.Id) != null)
                        failures.Add($"C2: '{c.Id}' reads CLEARED with nothing claimed - the marker must come from " +
                                     "RaidClaimService, never from a second predicate");

            // Unwired provider (headless / EditMode) -> no marker, for the same reason.
            using (var vm = new RaidSelectionVM(defs, null, 20, _ => true, 99))
                foreach (var c in Ladder)
                    if (vm.ClearedWordFor(c.Id) != null)
                        failures.Add($"C2: '{c.Id}' reads CLEARED with NO ClaimedProvider wired - a frame that cannot " +
                                     "prove a clear must not claim one");

            // One camp claimed -> exactly that camp is marked, and it states the repeat rate.
            using (var vm = new RaidSelectionVM(defs, null, 20, _ => true, 99, null,
                                                id => string.Equals(id, "raider_camp_small", StringComparison.OrdinalIgnoreCase)))
            {
                string word = vm.ClearedWordFor("raider_camp_small");
                if (string.IsNullOrEmpty(word))
                    failures.Add("C2: a CLAIMED camp carries no cleared marker - the return leg of the loop still has no memory");
                else
                {
                    if (word.IndexOf(RaidSelectionVM.ClearedPrefix, StringComparison.Ordinal) < 0)
                        failures.Add($"C2: the cleared marker \"{word}\" does not carry the word CLEARED. The state is carried " +
                                     "by WORDS - the owner is red/green colourblind and a tint would say nothing to her");
                    // The rate is READ from RaidClaimService.RepeatClearLootMultiplier, never typed:
                    // WO-1461 owns that number and this row only discloses whatever it lands.
                    int pct = (int)Math.Round(
                        DeNelle.Village.World.Camps.RaidClaimService.RepeatClearLootMultiplier * 100.0);
                    if (word.IndexOf(pct + "%", StringComparison.Ordinal) < 0)
                        failures.Add($"C2: the cleared marker \"{word}\" does not state the live repeat rate ({pct}%). It must " +
                                     "format RaidClaimService.RepeatClearLootMultiplier so it can never advertise a rate the " +
                                     "settle does not pay");
                }
                if (vm.ClearedWordFor("fortified_garrison") != null)
                    failures.Add("C2: an UNCLAIMED camp reads CLEARED while a sibling is claimed - the marker is not per-camp");
                log.AppendLine("OK: cleared marker is per-camp, comes from the claim provider, and discloses the live repeat rate");
            }
        }

        // -- C3: WO-1562 part 1 - the victory screen announces a crossed ladder rung ----------
        //
        // RaidVictoryController.ResolveUnlockLine returned null UNCONDITIONALLY, and the lane its
        // comment deferred to (WO-1375) CLOSED 2026-09-06 without claiming the seam - so the
        // announcement was orphaned, not deferred. It now reads the SAME authored unlockVictories
        // the grid's own lock sentences read: one ladder, no second copy of the thresholds.
        private static void CheckUnlockAnnouncement(List<SceneConfigDef> defs, List<string> failures, StringBuilder log)
        {
            // Resolve before classifying: zero unlock thresholds are valid authoring,
            // not missing catalog rows. Fixture IDs supply order; copied thresholds do not.
            int before = failures.Count;
            var live = new List<SceneConfigDef>();
            var thresholds = new SortedSet<int>();
            int positiveRows = 0;
            foreach (var c in Ladder)
            {
                var row = SceneConfigCatalog.Find(c.Id);
                if (row == null) { failures.Add($"C3: missing required flagship '{c.Id}'"); continue; }
                live.Add(row);
                if (row.unlockVictories < 0) failures.Add($"C3: negative unlock threshold on '{c.Id}'");
                if (row.unlockVictories > 0) { positiveRows++; thresholds.Add(row.unlockVictories); }
            }
            if (failures.Count != before)
            {
                log.AppendLine($"C3 FAIL: resolved {live.Count}/{Ladder.Length}; invalid/missing data; 0 producer assertions.");
                return;
            }

            // Bounded, deduplicated samples and neighbours. A formerly quiet sample
            // that becomes a live threshold must assert its crossing instead of silence.
            var probes = new SortedSet<int> { int.MinValue, -1, 0, 1, 2, 3, 4, 9, 10, 11, 19, 20, 21, 100, 1000, int.MaxValue };
            foreach (int t in thresholds)
            {
                probes.Add(t); probes.Add(t - 1);
                if (t < int.MaxValue) probes.Add(t + 1);
            }
            int assertions = 0, crossingProbes = 0, silentProbes = 0;
            foreach (int count in probes)
            {
                // Independent expected identity and wording from resolved catalog rows.
                // First flagship wins ties. Never use one tested producer as the oracle for another.
                var expected = count > 0 ? live.Find(row => row.unlockVictories == count) : null;
                string expectedLine = expected == null ? null : RaidSelectionVM.UnlockPrefix +
                    (string.IsNullOrEmpty(expected.displayName) ? expected.id : expected.displayName) + RaidSelectionVM.UnlockSuffix;
                var actual = RaidSelectionVM.CampUnlockedAt(count);
                string line = RaidSelectionVM.UnlockAnnouncementFor(count);
                assertions += 2; // Both calls and comparisons execute even if the first one fails.
                if ((expected == null && actual != null) || (expected != null &&
                    (actual == null || !string.Equals(expected.id, actual.id, StringComparison.Ordinal))))
                    failures.Add($"C3: CampUnlockedAt({count}) expected '{expected?.id ?? "<null>"}', got '{actual?.id ?? "<null>"}'");
                if (!string.Equals(expectedLine, line, StringComparison.Ordinal))
                    failures.Add($"C3: UnlockAnnouncementFor({count}) expected '{expectedLine ?? "<null>"}', got '{line ?? "<null>"}'");
                if (expected == null) silentProbes++; else crossingProbes++;
            }
            int nextProbes = 0;
            if (thresholds.Count == 0)
                foreach (int count in new[] { int.MinValue, -1, 0, 1, 1000, int.MaxValue })
                {
                    var next = RaidSelectionVM.NextLockedCamp(count);
                    nextProbes++; assertions++;
                    if (next != null) failures.Add($"C3: all-unlocked catalog returned next target '{next.id}' at {count}");
                }
            bool ok = failures.Count == before;
            log.AppendLine($"C3 {(ok ? "OK" : "FAIL")}: resolved {live.Count}/{Ladder.Length}; positive rows={positiveRows}; " +
                $"configured positive rungs={thresholds.Count}; crossing probes={crossingProbes}; silent probes={silentProbes}; " +
                $"next-target probes={nextProbes}; producer assertions={assertions}." +
                (ok && thresholds.Count == 0 ? " Intentionally all-unlocked; no positive announcement integration exercised." : ""));
        }

        /// <summary>Fixture lookup by id (this suite's def list, never the live catalog).</summary>
        private static SceneConfigDef FindDef(List<SceneConfigDef> defs, string id)
        {
            if (defs == null) return null;
            foreach (var d in defs)
                if (d != null && string.Equals(d.id, id, StringComparison.OrdinalIgnoreCase)) return d;
            return null;
        }

        // -- D: the pips carry data or nothing ----------------------------------------------
        private static void CheckPipsGate(List<SceneConfigDef> defs, List<string> failures, StringBuilder log)
        {
            using (var vm = new RaidSelectionVM(defs, null, 20, _ => true))
                if (vm.ShowStarPips)
                    failures.Add("D: ShowStarPips is TRUE with no rating producer - three identical pips on every row carry nothing " +
                                 "and must not draw (merged UI review row 1)");

            using (var vm = new RaidSelectionVM(defs, null, 20, _ => true, bestStars: _ => 3))
                if (vm.ShowStarPips)
                    failures.Add("D: ShowStarPips is TRUE when every camp is rated 3 - uniform ratings say nothing, hide the pips");

            var varied = new Dictionary<string, int> { { "raider_camp_small", 3 }, { "fortified_garrison", 1 }, { "mage_enclave", 0 }, { "iron_bastion", -1 } };
            using (var vm = new RaidSelectionVM(defs, null, 20, _ => true, bestStars: id => varied.TryGetValue(id, out var s) ? s : -1))
            {
                if (!vm.ShowStarPips)
                    failures.Add("D: ShowStarPips is FALSE when ratings differ (3/1/0/unknown) - the pips are the one place the " +
                                 "player's own record per camp would show, and they stay hidden");
                if (vm.BestStarsFor("fortified_garrison") != 1)
                    failures.Add($"D: BestStarsFor('fortified_garrison') = {vm.BestStarsFor("fortified_garrison")}, provider said 1");
                if (vm.BestStarsFor("iron_bastion") != RaidSelectionVM.Unknown)
                    failures.Add($"D: BestStarsFor('iron_bastion') = {vm.BestStarsFor("iron_bastion")}, expected Unknown for a -1 rating");
            }

            // A single known rating cannot vary.
            using (var vm = new RaidSelectionVM(defs, null, 20, _ => true, bestStars: id => id == "raider_camp_small" ? 2 : -1))
                if (vm.ShowStarPips)
                    failures.Add("D: ShowStarPips is TRUE with only ONE rated camp - one number cannot vary");

            log.AppendLine("OK: pips hidden with no producer / uniform / one rating; shown when ratings differ");
        }

        // -- F: the card's bands can actually seat their rows ---------------------------------
        //
        // ⛔ THE CASE THAT WOULD HAVE CAUGHT A SHIPPED-INVISIBLE FEATURE. A/B/E all passed on
        // 2026-09-05 while the spoils line was not on the screen at all: they prove the STRING,
        // and a string that is composed, handed to the View and built into a label is still not
        // a string the player can read. TMP culls a whole Ellipsis line whose box cannot seat in
        // its rect, and ElarionUiKit.FitSingleLine has no shrink room to give (it clamps
        // fontSizeMin up to the label's authored fontSize). So the last mile is arithmetic on
        // the live constants - not a source-text lint, which would go stale the moment someone
        // renamed a band.
        private static void CheckCardBands(List<string> failures, StringBuilder log)
        {
            var bands = RaidSelectionScreen.CardBands;
            if (bands == null || bands.Length == 0)
            {
                failures.Add("F: RaidSelectionScreen.CardBands is empty - the card's band table is the ONLY " +
                             "thing standing between an authored fraction and a row that renders nothing. " +
                             "Every text band on the card belongs in it");
                return;
            }
            foreach (var b in bands)
            {
                if (b.Y1 <= b.Y0)
                    failures.Add($"F: band '{b.Name}' is inverted or empty (Y0 {b.Y0:0.###} >= Y1 {b.Y1:0.###})");
                else if (b.HavePx < b.NeedsPx)
                    failures.Add($"F: band '{b.Name}' gives {b.HavePx:0.0}px but its {b.FontPt}pt row needs " +
                                 $"{b.NeedsPx:0.0}px - TMP's Ellipsis overflow will CULL THE WHOLE LINE and the " +
                                 "row will be blank on the card with no error, no exception and no trace (this is " +
                                 "exactly how the WO-1402 spoils line, the clock, the lock sentence and the canon " +
                                 "flavour line all shipped invisible on 2026-09-05). Raise CardHeightPx or widen " +
                                 "the band - never shrink the font below the kit's readable floor");
            }

            // The bands must also not OVERLAP, or two rows paint over each other - a different
            // way for the same words to become unreadable.
            for (int i = 0; i < bands.Length; i++)
                for (int j = i + 1; j < bands.Length; j++)
                    if (bands[i].Y0 < bands[j].Y1 && bands[j].Y0 < bands[i].Y1)
                        failures.Add($"F: bands '{bands[i].Name}' ({bands[i].Y0:0.###}-{bands[i].Y1:0.###}) and " +
                                     $"'{bands[j].Name}' ({bands[j].Y0:0.###}-{bands[j].Y1:0.###}) OVERLAP - two rows " +
                                     "of text on the same pixels");

            var sb = new StringBuilder("OK: card bands seat their rows -");
            foreach (var b in bands) sb.Append($" {b.Name} {b.HavePx:0.0}/{b.NeedsPx:0.0}px");
            log.AppendLine(sb.ToString());
        }

        // -- E: the MVVM seams, at source ----------------------------------------------------
        private static void CheckSourceSeams(List<string> failures, StringBuilder log)
        {
            string vm = ReadStripped(VmRel, failures);
            string screen = ReadStripped(ScreenRel, failures);
            string scorer = ReadStripped(ScorerRel, failures);
            if (vm == null || screen == null || scorer == null) return;

            // VM: calls the scorer's estimate, types no spoils literal.
            if (vm.IndexOf("RaidScoring.EstimateSpoils(", StringComparison.Ordinal) < 0)
                failures.Add("E: RaidSelectionVM.cs no longer calls RaidScoring.EstimateSpoils( - the row's number must come from the " +
                             "settle payout's own formula, never a literal or a second table (mutation M2)");
            if (vm.IndexOf("\"Spoils: ~", StringComparison.Ordinal) >= 0)
                failures.Add("E: RaidSelectionVM.cs types a literal \"Spoils: ~...\" - the line is COMPOSED from the estimate, never typed");

            // Scorer: payout and preview share ProjectLoot.
            string lootFor = Slice(scorer, "public ResourceCost LootFor(", "public string ResolveCampConfigId(");
            // Comment lines are stripped, so the slice ends at the next CODE token after the method.
            string estimate = Slice(scorer, "public static ResourceCost EstimateSpoils(", "[RuntimeInitializeOnLoadMethod");
            if (lootFor == null || lootFor.IndexOf("ProjectLoot(", StringComparison.Ordinal) < 0)
                failures.Add("E: RaidScoring.LootFor no longer routes through ProjectLoot( - the settle payout and the row estimate " +
                             "have forked into two formulas");
            if (estimate == null || estimate.IndexOf("ProjectLoot(", StringComparison.Ordinal) < 0)
                failures.Add("E: RaidScoring.EstimateSpoils no longer routes through ProjectLoot( - the row estimate is its own arithmetic");

            // Screen: renders the VM's strings, owns none of them, gates the pips.
            if (screen.IndexOf("_vm.SpoilsLineFor(", StringComparison.Ordinal) < 0)
                failures.Add("E: RaidSelectionScreen.cs does not read _vm.SpoilsLineFor( - the spoils line is not painted");
            if (screen.IndexOf("_vm.ArmyWarnWordFor(", StringComparison.Ordinal) < 0)
                failures.Add("E: RaidSelectionScreen.cs does not read _vm.ArmyWarnWordFor( - the army warning is not painted");
            // WO-1562: the cleared marker and its ONE input. Source-lint because a VM-only case
            // cannot see whether the View ever asked, and "the model composed it and the renderer
            // discarded it" is a defect this repo has already shipped once (WO-1534 B2).
            if (screen.IndexOf("_vm.ClearedWordFor(", StringComparison.Ordinal) < 0)
                failures.Add("E: RaidSelectionScreen.cs does not read _vm.ClearedWordFor( - a cleared camp still looks " +
                             "exactly like one the player never fought (WO-1562)");
            if (screen.IndexOf("RaidSelectionVM.ClaimedProvider", StringComparison.Ordinal) < 0)
                failures.Add("E: RaidSelectionScreen.cs never wires RaidSelectionVM.ClaimedProvider - the cleared marker " +
                             "has no input, so it can never fire in a build");
            int pipsGate = screen.IndexOf("_vm.ShowStarPips", StringComparison.Ordinal);
            int pipsBuild = screen.IndexOf("StarRatingRow.Build(", StringComparison.Ordinal);
            if (pipsGate < 0)
                failures.Add("E: RaidSelectionScreen.cs never reads _vm.ShowStarPips - the pips draw ungated (mutation M3)");
            else if (pipsBuild >= 0 && pipsBuild < pipsGate)
                failures.Add("E: RaidSelectionScreen.cs calls StarRatingRow.Build( BEFORE reading _vm.ShowStarPips - the pips are not gated (mutation M3)");
            if (screen.IndexOf("\"Spoils:", StringComparison.Ordinal) >= 0)
                failures.Add("E: RaidSelectionScreen.cs types \"Spoils: - the View owns no words; the VM composes the line");
            if (screen.IndexOf("\"LOCKED", StringComparison.Ordinal) >= 0)
                failures.Add("E: RaidSelectionScreen.cs types \"LOCKED - the View owns no words, and since WO-1542 the " +
                             "army word is not a lock at all; RaidSelectionVM owns both halves");

            log.AppendLine("OK: VM -> RaidScoring.EstimateSpoils; LootFor + EstimateSpoils -> ProjectLoot; screen renders VM strings, pips gated");
        }

        // Source with every //-comment line and every /* */ block removed, so a comment that
        // quotes a forbidden literal (this suite's own header quotes several) cannot trip a lint
        // or satisfy one.
        private static string ReadStripped(string rel, List<string> failures)
        {
            string path = Path.Combine(Application.dataPath, rel);
            if (!File.Exists(path)) { failures.Add("E: " + rel + " missing at " + path); return null; }
            string text;
            try { text = File.ReadAllText(path); }
            catch (Exception e) { failures.Add("E: could not read " + rel + " (" + e.GetType().Name + ")"); return null; }

            var sb = new StringBuilder(text.Length);
            foreach (var raw in text.Replace("\r\n", "\n").Split('\n'))
            {
                string line = raw;
                int slash = line.IndexOf("//", StringComparison.Ordinal);
                if (slash >= 0)
                {
                    // Keep a "//" that sits inside a string literal (an odd count of quotes before it).
                    int quotes = 0;
                    for (int i = 0; i < slash; i++) if (line[i] == '"') quotes++;
                    if (quotes % 2 == 0) line = line.Substring(0, slash);
                }
                sb.Append(line).Append('\n');
            }
            string s = sb.ToString();
            int open;
            while ((open = s.IndexOf("/*", StringComparison.Ordinal)) >= 0)
            {
                int close = s.IndexOf("*/", open + 2, StringComparison.Ordinal);
                if (close < 0) break;
                s = s.Remove(open, close + 2 - open);
            }
            return s;
        }

        private static string Slice(string text, string fromMarker, string toMarker)
        {
            int a = text.IndexOf(fromMarker, StringComparison.Ordinal);
            if (a < 0) return null;
            int b = text.IndexOf(toMarker, a + fromMarker.Length, StringComparison.Ordinal);
            return b < 0 ? text.Substring(a) : text.Substring(a, b - a);
        }
    }
}

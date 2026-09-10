// =============================================================================
// ManageTroopsTrainDoorRegression — headless oracle for PROD-013: the Manage
// screen's TROOPS tab is the ONE door to troop training, and it actually opens.
// Marker: MANAGE_TRAIN_DOOR_OK / MANAGE_TRAIN_DOOR_FAIL.
// -----------------------------------------------------------------------------
// Assembly: DeNelle.EditorRegression (editor-only). Wired into DataRegression.RunAll.
// Style/contract mirrors the other Run(out reason) oracles (ArmyMusterRegression,
// ObsidianQueueRegression, UpgradeQueueFullSurfaceRegression).
//
// WHY THIS SUITE EXISTS — the defect it is shaped around:
//   PROD-002 (commit 233613615, 2026-08-18) closed the barracks talk-door on the
//   stated premise that "Manage owns training". It did not: ManageScreenVM
//   .BuildTroopsBrowse emitted UPGRADE rows only, and the door it replaced was the
//   last player-reachable entrance. The whole training stack — ChannelId.Train,
//   JobKind.TrainTroop, BarracksService.EnqueueTraining, TroopTrainingPanel,
//   ArmyMusterService — was built, regression-covered and UNREACHABLE. The owner
//   found it by playing: "under manage i see option to upgrade the troops, but i
//   dont se a way to train troops".
//
//   Every existing suite passed the whole time, because every one of them tested a
//   LAYER (the queue engine, the muster planner, the roster) and none of them tested
//   the DOOR. This one drives the real ManageScreenVM against real live services and
//   asserts what a player can actually tap. Case 1 is the case that would have caught
//   it; it FAILS against pre-PROD-013 code and passes after.
//
// Proves, with REAL types, real catalogs and real services (no play mode):
//   1. the Troops tab emits at least one TRAIN row for a trainable troop;
//   2. TRAIN and UPGRADE rows are BOTH present and labelled distinguishably;
//   3. an ARMIES / muster entry exists (the v38 loadout bank has a door too);
//   4. activating a Train row produces a real JobKind.TrainTroop job on ChannelId.Train;
//   5. the door stays SINGLE — the source still routes through BarracksService and
//      the barracks talk-door stays closed (canon CLAUDE.md §7: one Queues entry).
//   7. (WO-1389, 2026-09-05) the UPGRADE face has a DESTINATION: the Footman choice's
//      NextUnlockText reads "L3 unlocks Sweeping Cut" (BarracksProgression.NextAbilityLine
//      over troop-upgrades.json) and the panel composes it into the upgrade sub-line.
//      Measured RED FIRST: before WO-1389 TroopChoiceVM had no NextUnlockText and the
//      button said "UPGRADE TO L2" with a time under it and nothing about what L3 buys.
//      Mutation that reds it: `choice.NextUnlockText = "";` in ManageScreenVM.FillUpgradeFacts.
// =============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEngine;
using DeNelle.Core.Jobs;
using DeNelle.Core.Manage;
using DeNelle.Core.State;
using DeNelle.Village;
using DeNelle.Village.UI;

namespace DeNelle.Editor
{
    public static class ManageTroopsTrainDoorRegression
    {
        private const string VmPath = "Assets/_Modules/Village/UI/Manage/ManageScreenVM.cs";
        private const string InteractablePath = "Assets/_Modules/Village/Buildings/BuildingInteractable.cs";
        private const string PanelPath = "Assets/_Modules/Village/UI/Manage/ManageScreenPanel.cs";

        public static bool Run(out string reason)
        {
            var failures = new List<string>();
            var log = new StringBuilder();
            log.AppendLine("=== ManageTroopsTrainDoorRegression: Manage -> Troops is the ONE training door (PROD-013) ===");

            try
            {
                RunLiveDoorChecks(failures, log);
                CheckSingleDoorSource(failures, log);
                CheckNoSecondCampComposer(failures, log);
                CheckArmyDoorRowIsTallEnoughAndTheCardGrew(failures, log);
            }
            catch (Exception ex)
            {
                failures.Add($"ManageTroopsTrainDoorRegression threw: {ex.GetType().Name}: {ex.Message}");
            }

            return Verdict(failures, log, out reason);
        }

        // ── 1-4. the LIVE door: drive the real VM against real services ────────
        private static void RunLiveDoorChecks(List<string> failures, StringBuilder log)
        {
            var priorInstance = GameStateService.Instance;
            // The live spend path calls GameStateService.Save(), which writes the editor
            // PlayerPrefs save slot. Back it up and restore it so a regression run can never
            // eat a developer's editor save.
            string priorSave = PlayerPrefs.GetString(SaveSchema.PlayerPrefsKey, null);
            // Cases 8/9 navigate, and EnterTab persists the last-used tab. Same courtesy as the
            // save slot: a regression run never moves a developer's editor state.
            bool hadTabPref = PlayerPrefs.HasKey(ManageScreenVM.LastTabPrefKey);
            int priorTabPref = PlayerPrefs.GetInt(ManageScreenVM.LastTabPrefKey, 0);

            GameObject gssGo = null, svcGo = null;
            GameState throwaway = null;
            try
            {
                throwaway = ScriptableObject.CreateInstance<GameState>();
                gssGo = new GameObject("GSS (manage-train-door oracle)");
                var gss = gssGo.AddComponent<GameStateService>();
                if (!InstallState(gss, throwaway))
                {
                    // NOT A SKIP (the UpgradeQueueFullSurfaceRegression ruling): a suite that
                    // green-passes on an unreachable seam asserts nothing, most eagerly on the
                    // day the seam breaks. The seam being unreflectable is itself the defect.
                    failures.Add("[manage-train-door] GameStateService state seam is not reflectable, so the LIVE " +
                                 "checks (train row present, train+upgrade distinguishable, muster entry, real " +
                                 "TrainTroop enqueue) could not run. This is a FAIL, not a skip.");
                    return;
                }

                svcGo = new GameObject("BuildTimerService (manage-train-door oracle)");
                var svc = svcGo.AddComponent<BuildTimerService>();
                // Awake does not run on AddComponent outside play mode, so the singleton is
                // installed explicitly. If this seam moves the suite FAILS rather than skipping.
                if (!InstallQueueInstance(svc))
                {
                    failures.Add("[manage-train-door] BuildTimerService.Instance backing field is not reflectable — " +
                                 "the oracle cannot install the queue singleton the Troops tab reads. FAIL, not a skip.");
                    return;
                }

                // A founded town with a working barracks and a full purse: the state the owner
                // was in when she reported the defect. No balance is asserted here — the point
                // is only that the DOOR exists, so every gate is deliberately wide open.
                throwaway.Onboarded = true;
                throwaway.BarracksLevel = 3;
                // The VM derives visible categories from THIS town's placed layout. BarracksLevel
                // is account progression, not proof that a barracks stands in the current town.
                // Keep the fixture honest: this is the "founded town with a working barracks"
                // described above, not an impossible empty-town/account-level hybrid.
                throwaway.BaseLayout = new List<PlacedStructureData>
                {
                    new PlacedStructureData("barracks", 2, 2, 0, 1),
                };
                throwaway.Wood = 100000;
                throwaway.Iron = 100000;
                var bal = throwaway.Resources;
                bal.Food = 100000;
                bal.Crystals = 100000;
                // 2026-09-04 WO-1387: Coins is NOT load-bearing for the door any more - training
                // charges nothing (owner: "training free ... just time"). Left rich only because this
                // fixture is "every gate wide open"; the zero-gold train is pinned by
                // TrainingCostsTimeOnlyRegression, not here.
                bal.Coins = 100000;
                throwaway.Resources = bal;
                throwaway.ObsidianQueue = ObsidianQueueState.Empty();

                var vm = new ManageScreenVM();
                vm.SelectTab(ManageTab.Troops);
                vm.Rebuild();

                var rows = new List<BrowseRowVM>(vm.BrowseRows);
                log.AppendLine($"  Troops tab produced {rows.Count} browse row(s):");
                for (int i = 0; i < rows.Count; i++)
                    log.AppendLine($"    [{rows[i].ActionText}] \"{rows[i].Label}\"  ({rows[i].CostText}) - {rows[i].StateText}");

                // ── CASE 1: at least one TRAIN row. THE case. ──────────────────
                var trainRows = rows.FindAll(r => r != null &&
                                                  string.Equals(r.ActionText, "Train", StringComparison.Ordinal));
                if (trainRows.Count == 0)
                {
                    failures.Add("[case 1] the Manage > Troops tab emitted NO row with ActionText \"Train\". This is " +
                                 "THE PROD-013 defect: the barracks talk-door is closed (BuildingInteractable._noTalkDoor) " +
                                 "so this tab is the only entrance to training, and a player cannot train at all. " +
                                 "See ManageScreenVM.BuildTroopsBrowse.");
                }
                else
                {
                    log.AppendLine($"  case 1 OK - {trainRows.Count} Train row(s)");
                }

                // ── CASE 2: UPGRADE rows survive, and the two are distinguishable ──
                var upgradeRows = rows.FindAll(r => r != null &&
                                                    string.Equals(r.ActionText, "Upgrade", StringComparison.Ordinal));
                if (upgradeRows.Count == 0)
                    failures.Add("[case 2] no row with ActionText \"Upgrade\" — adding Train must be ADDITIVE. " +
                                 "Train and Upgrade are different actions on the same troop and both belong on this tab.");

                foreach (var r in trainRows)
                    if (!r.Label.StartsWith("Train ", StringComparison.Ordinal))
                        failures.Add($"[case 2] a Train row is labelled \"{r.Label}\" — it does not lead with the verb, " +
                                     "so a player cannot tell it apart from the Upgrade row for the same troop.");
                foreach (var r in upgradeRows)
                    if (!r.Label.StartsWith("Upgrade ", StringComparison.Ordinal))
                        failures.Add($"[case 2] an Upgrade row is labelled \"{r.Label}\" — it does not lead with the verb, " +
                                     "so a player cannot tell it apart from the Train row for the same troop.");

                // No two rows may share a label: identical text on two different actions is the
                // mistake this case exists to prevent, whatever the verbs happen to be.
                var seen = new HashSet<string>(StringComparer.Ordinal);
                foreach (var r in rows)
                    if (r != null && !string.IsNullOrEmpty(r.Label) && !seen.Add(r.Label))
                        failures.Add($"[case 2] two browse rows carry the IDENTICAL label \"{r.Label}\" — " +
                                     "two different actions reading the same is unactionable for the player.");

                if (upgradeRows.Count > 0)
                    log.AppendLine($"  case 2 OK - {upgradeRows.Count} Upgrade row(s), all verb-led and distinct");

                // ── CASE 3: the ARMIES / muster entry (WO-897, save schema v38) ──
                var musterRow = rows.Find(r => r != null && r.Label != null &&
                                               r.Label.IndexOf("Armies", StringComparison.OrdinalIgnoreCase) >= 0);
                if (musterRow == null)
                    failures.Add("[case 3] no ARMIES/muster entry on the Troops tab. The v38 army loadout bank " +
                                 "(3 named composition slots) ships and its only other door was the closed barracks " +
                                 "Yarn verb <<ShowMusterUI>> — without this row it is unreachable too.");
                else if (musterRow.Activate == null)
                    failures.Add("[case 3] the Armies entry has a null Activate — a row that does nothing is not a door.");
                else
                    log.AppendLine($"  case 3 OK - muster entry \"{musterRow.Label}\" [{musterRow.ActionText}]");

                // -- CASE 7 (WO-1389): the UPGRADE face names what the next level BUYS --
                // Read from the SAME Rebuild() as cases 1-3, before case 4 mutates the queue.
                // The fixture's troop level is the state default (L1), so the Footman's next
                // authored ability is the L3 one; the oracle asks the ONE composer for the exact
                // sentence rather than retyping "Sweeping Cut" (that string is troop-upgrades.json's).
                const string taughtTroop = "troop-footman";
                var footman = vm.TroopChoices.Find(c => c != null && string.Equals(c.Id, taughtTroop, StringComparison.Ordinal));
                if (footman == null)
                {
                    failures.Add($"[case 7] the Troops tab emitted no TroopChoiceVM for '{taughtTroop}' - the StarterArmyGrant " +
                                 "unit the post-raid beat teaches on is not on the card rail at all.");
                }
                else
                {
                    string expected = BarracksProgression.NextAbilityLine(taughtTroop, footman.Level) ?? "";
                    if (string.IsNullOrEmpty(expected))
                        failures.Add($"[case 7] BarracksProgression.NextAbilityLine('{taughtTroop}', L{footman.Level}) returned nothing - " +
                                     "troop-upgrades.json authors abilities at L3/L5/L7 for the Footman, so the composer lost them.");
                    else if (!expected.StartsWith("L", StringComparison.Ordinal) || expected.IndexOf(" unlocks ", StringComparison.Ordinal) < 0)
                        failures.Add($"[case 7] the next-unlock sentence reads \"{expected}\" - the shape is \"L<n> unlocks <Ability>\" " +
                                     "so the button has a destination the player can read, not a whole flavour line.");
                    if (!string.Equals(footman.NextUnlockText, expected, StringComparison.Ordinal))
                        failures.Add($"[case 7] Footman NextUnlockText is \"{footman.NextUnlockText}\" but the composer says \"{expected}\" - " +
                                     "the UPGRADE TO L<n> face has lost its destination (WO-1389 pressure point 3: " +
                                     "\"the button has a destination, not just a number\").");
                    else
                        log.AppendLine($"  case 7 OK - Footman L{footman.Level} next-unlock line \"{footman.NextUnlockText}\"");
                }

                // ── CASE 8 (WO-1517): the troop DETAIL card carries STATS, before -> after ──
                // Owner ruling 2026-09-06 20:10, verbatim: "see screen needs clear should show
                // stats and what upgrade will promote to", against
                // Logs/device/screens/owner-screen-20260906-201037.png - an Archer card with a
                // portrait, a flavour line, two MISLABELLED timer rows ("Next" over a TRAIN fact,
                // "Time" over an UPGRADE fact) and no stat of any kind, and no UPGRADE button
                // although the row beside it said the upgrade was Ready.
                // EVERY troop id, not the one the capture happens to open (the WO's own wording).
                // RED PROOF: revert ComposeDetail's Army arm to
                //   stats = TwoFacts("Next", ..., "Time", ...)   -> the label + row-count checks fire.
                //   delete the SecondaryAction seat in ComposeDetail                -> the face check fires.
                CheckTroopDetailStats(vm, failures, log);

                // ── CASE 4: activating a Train row makes a REAL job on the REAL line ──
                if (trainRows.Count > 0)
                {
                    int before = svc.QueueDepth(ChannelId.Train);
                    trainRows[0].Activate?.Invoke();

                    var jobs = new List<BuildJobData>();
                    jobs.AddRange(svc.ActiveJobsOf(ChannelId.Train));
                    jobs.AddRange(svc.PendingJobsOf(ChannelId.Train));

                    var trainJob = jobs.Find(j => !string.IsNullOrEmpty(j.StructureId) &&
                                                  j.StructureId.StartsWith(BarracksService.TrainPrefix, StringComparison.Ordinal));
                    if (trainJob.StructureId == null)
                    {
                        failures.Add($"[case 4] tapping \"{trainRows[0].Label}\" did NOT put a job on the Train line " +
                                     $"(depth {before} -> {svc.QueueDepth(ChannelId.Train)}). The CTA must route through " +
                                     "BarracksService.EnqueueTraining, which mints a job id prefixed " +
                                     $"\"{BarracksService.TrainPrefix}\". Notice was: \"{vm.Notice}\"");
                    }
                    else
                    {
                        if (trainJob.Kind != (int)JobKind.TrainTroop)
                            failures.Add($"[case 4] the enqueued job's Kind is {trainJob.Kind}, expected " +
                                         $"{(int)JobKind.TrainTroop} (JobKind.TrainTroop) — a training job on the wrong " +
                                         "kind will be applied by the wrong IJobEffect at completion.");
                        if (trainJob.Channel != (int)ChannelId.Train)
                            failures.Add($"[case 4] the enqueued job's Channel is {trainJob.Channel}, expected " +
                                         $"{(int)ChannelId.Train} (ChannelId.Train) — training must ride the Train line, " +
                                         "never the shared Builder line.");
                        log.AppendLine($"  case 4 OK - \"{trainRows[0].Label}\" enqueued jobId={trainJob.StructureId} " +
                                       $"kind={trainJob.Kind} channel={trainJob.Channel} (depth {before} -> " +
                                       $"{svc.QueueDepth(ChannelId.Train)})");
                    }
                }

                // ── CASE 10 (WO-1517 acceptance 1): the OTHER TWO STRINGS ─────────────
                // Runs BEFORE case 9, because case 9 fills the ROSTER and ArmyFull leads
                // QueueFull in the tile precedence (ManageScreenVM.cs:4347-4348) - measuring
                // QUEUE FULL after the army is capped would measure nothing.
                CheckQueueFullAndUpgradeWords(vm, failures, log);

                // ── CASE 9 (WO-1517 + WO-1518): the ARMY CAP SPEAKS, and its sentence stays put ──
                // Runs LAST because it fills the fixture's roster, which no earlier case may see.
                CheckArmyFullIsSaidAndDoesNotTravel(vm, throwaway, failures, log);

                // ── CASE 11 (WO-1541): ONE PRODUCER names the camp, and the line has a door ──
                CheckArmyLineReadsThePublishedCamp(vm, failures, log);

                // ── CASE 15: a MAXED troop reads MAX even when the train line is full ──
                // Runs AFTER case 10, which is what fills the line - this case measures the state
                // case 10 leaves behind rather than setting up a second one.
                CheckMaxTroopOutranksTheFullLine(vm, throwaway, failures, log);

                // ── CASE 13 (WO-1564 part 2): the QUEUE DRAWER speaks WORDS, not ids ──
                CheckQueueRowsNameThingsInWords(vm, svc, throwaway, failures, log);
            }
            finally
            {
                if (svcGo != null) UnityEngine.Object.DestroyImmediate(svcGo);
                if (gssGo != null) UnityEngine.Object.DestroyImmediate(gssGo);
                if (throwaway != null) UnityEngine.Object.DestroyImmediate(throwaway);
                SetGssInstance(priorInstance);
                if (priorSave != null) PlayerPrefs.SetString(SaveSchema.PlayerPrefsKey, priorSave);
                else PlayerPrefs.DeleteKey(SaveSchema.PlayerPrefsKey);
                if (hadTabPref) PlayerPrefs.SetInt(ManageScreenVM.LastTabPrefKey, priorTabPref);
                else PlayerPrefs.DeleteKey(ManageScreenVM.LastTabPrefKey);
            }
        }

        // ── CASE 8 body (WO-1517 §1B items 1-4) ───────────────────────────────
        /// <summary>
        /// For EVERY unlocked troop the model offers, open its detail screen and measure the card:
        /// a real stat table (not two timers), labels that name their values, a level+1 column
        /// wherever a next level exists, and an UPGRADE face beside TRAIN whenever it is Ready.
        /// </summary>
        private static void CheckTroopDetailStats(ManageScreenVM vm, List<string> failures, StringBuilder log)
        {
            // ⚠ AvailableTabIds is refreshed ONLY by the navigation writers - EnterTab /
            // OpenDefaultScreen / ComposeWorkspace (ManageScreenVM.RefreshAvailableTabs). The cases
            // above drive the LEGACY pair SelectTab + Rebuild, which fills VisibleTabs and never
            // touches _availableTabs - so without this call the list is still EMPTY here and ARMY
            // reads "not available" on a fixture that plainly offers it. EnterTab is also the state
            // this case wants: the ARMY grid, the screen a player opens a troop detail FROM.
            vm.EnterTab(ManageTabId.Army);

            if (!vm.AvailableTabIds.Contains(ManageTabId.Army))
            {
                // FAIL, not a skip: the fixture places a barracks precisely so this tab exists.
                failures.Add("[case 8] the ARMY tab is not available on a fixture that places a barracks at " +
                             "BarracksLevel 3, so no troop detail card could be composed and WO-1517's ruling is " +
                             "unmeasured.");
                return;
            }

            int measured = 0;
            for (int i = 0; i < vm.TroopChoices.Count; i++)
            {
                var c = vm.TroopChoices[i];
                if (c == null || !c.Unlocked) continue;

                vm.OpenDetail(ManageTabId.Army, c.Id, null, null);
                var ws = vm.ComposeWorkspace();
                if (ws == null || ws.Tabs == null || ws.Tabs.Count == 0)
                {
                    failures.Add($"[case 8] ComposeWorkspace produced no tabs for troop '{c.Id}'.");
                    continue;
                }
                int index = Mathf.Clamp(ws.ActiveTabIndex, 0, ws.Tabs.Count - 1);
                var sel = ws.Tabs[index].Selection;
                if (sel == null || !sel.Visible)
                {
                    failures.Add($"[case 8] the detail screen for troop '{c.Id}' has no visible selection card - " +
                                 "the player taps a troop and gets nothing.");
                    continue;
                }
                measured++;

                var stats = sel.Stats;
                int rows = stats != null ? stats.Count : 0;

                // ⛔ RE-POINTED 2026-09-07 (WO-1567 panel row 5), WITH THE RETIRED READING KEPT SO
                // IT IS NOT MOVED BACK. It read:
                //     if (rows < 5) ... "the four stats ... plus its train time"
                // i.e. it required the DURATION to be a fifth row IN THE STATS TABLE. The panel-5
                // ruling moved it out: mockup panel 5 draws the time under the costs with a clock
                // glyph, not among the stats, and ComposeDetail now sets costCaption "Train Time"
                // + timeText from TrainTimeText. ManageSelectionVM.TimeText exists for exactly that.
                //
                // ⚠ TWO ORACLES CANNOT BOTH BE RIGHT, AND THIS WAS THE STALE ONE.
                // ManageMockupConformanceRegression's [detail-clock-on-its-own] FAILS if the card
                // stops painting sel.TimeText - "a duration has no bank and no affordability
                // verdict, so it is never a cost row", and it is not a stat either. A count of 5
                // and a clock band are mutually exclusive; the count was the assertion that had to
                // move.
                //
                // ⭐ NOTHING IS LOST AND THE COUNT IS NO LONGER THE TEST. The four curve stats are
                // now demanded BY NAME (a count of five could be met by any five rows, including
                // the two mislabelled timer rows the owner photographed), and the duration is
                // demanded on the channel that now carries it. Strictly stronger than "rows >= 5".
                bool sawAttack = false, sawRange = false, sawSpeed = false;
                if (string.IsNullOrWhiteSpace(sel.TimeText))
                    failures.Add($"[case 8] troop '{c.Id}' carries no TimeText. The train duration left the stats " +
                                 "table for the clock band under the costs (mockup panel 5) - if it is on neither, " +
                                 "the card never says how long training takes, which is worse than the two " +
                                 "mislabelled timer rows this case was written against.");

                bool sawDelta = false, sawHealth = false;
                for (int s = 0; s < rows; s++)
                {
                    var row = stats[s];
                    if (row == null) { failures.Add($"[case 8] troop '{c.Id}' has a null stat row."); continue; }
                    if (string.IsNullOrWhiteSpace(row.Label))
                        failures.Add($"[case 8] troop '{c.Id}' has a stat row with no label - the value is a " +
                                     "number with nothing saying what it measures.");
                    // ⛔ THE TWO RETIRED LABELS. They are named literally because they are exactly
                    // what the owner photographed: "Next" over "Train one: 1m 0s . Ready" and
                    // "Time" over "Upgrade: 12m 0s . Ready". A label that contradicts its value is
                    // worse than none - the player reads it and learns something false.
                    if (string.Equals(row.Label, "Next", StringComparison.Ordinal) ||
                        string.Equals(row.Label, "Time", StringComparison.Ordinal))
                        failures.Add($"[case 8] troop '{c.Id}' still carries the retired row label \"{row.Label}\" " +
                                     $"over the value \"{row.Value}\" (WO-1517 §1B item 4).");
                    // ⛔ RE-POINTED "Damage" -> "Attack" (WO-1654 row 5.3, 2026-09-10), NOT
                    // DELETED. The delivered glyph is stat-ATTACK.png and the yardstick row reads
                    // "Health / Attack / Range / Speed"; the card was the last surface calling it
                    // Damage. This pin still fails if the row disappears - only its expected WORD
                    // moved, with the label it names.
                    if (string.Equals(row.Label, "Health", StringComparison.Ordinal)) sawHealth = true;
                    if (string.Equals(row.Label, "Attack", StringComparison.Ordinal)) sawAttack = true;
                    if (string.Equals(row.Label, "Range", StringComparison.Ordinal)) sawRange = true;
                    if (string.Equals(row.Label, "Speed", StringComparison.Ordinal)) sawSpeed = true;
                    if (string.Equals(row.Label, "Damage", StringComparison.Ordinal))
                        failures.Add($"[case 8] troop '{c.Id}' still labels its attack row \"Damage\". " +
                                     "WO-1654 row 5.3 and the delivered glyph (stat-attack.png) both say " +
                                     "ATTACK; a card that names a stat differently from its own art teaches " +
                                     "the player two names for one number.");
                    if (!string.IsNullOrEmpty(row.DeltaText)) sawDelta = true;

                    // ⭐ WO-1654 row 5.3 - THE ICON CHANNEL, pinned on the COMPOSED VM.
                    // The mockup draws a glyph against each of the four stats and the contract
                    // had NO field to put one in until this WO. Pinned here, on the four rows
                    // that own a delivered sheet - the building / storage / placed rows author no
                    // glyph and must not be forced one.
                    // ⛔ THIS IS NOT THE RESOLUTION PROOF. A key that resolves to nothing would
                    // still pass here; ManagePortraitCoverageRegression's [stat-glyphs-resolve]
                    // loads all four sprites and is the case that fails with the PNGs deleted.
                    // RED PROOF: drop the ManageArt.Stat* argument from any StatRow call in
                    // ManageScreenVM.TroopStatRows.
                    bool isFourStat =
                        string.Equals(row.Label, "Health", StringComparison.Ordinal) ||
                        string.Equals(row.Label, "Attack", StringComparison.Ordinal) ||
                        string.Equals(row.Label, "Range", StringComparison.Ordinal) ||
                        string.Equals(row.Label, "Speed", StringComparison.Ordinal);
                    if (isFourStat && string.IsNullOrEmpty(row.IconKey))
                        failures.Add($"[case 8] troop '{c.Id}' stat row \"{row.Label}\" carries NO IconKey, " +
                                     "so the renderer has nothing to paint and mockup panel 5's glyph column " +
                                     "is blank. The four sheets have been on disk since the WO-1567 art wave.");
                }
                // BY NAME, all four. These are the stats troop-upgrades.json's curves actually move
                // (strength scales MaxHp + DPS, reach scales AttackRange + AggroRadius), read
                // through TroopStatResolver.Effective - the SAME resolver TroopDeployer applies to
                // the live unit, so the number on the card is the number that fights.
                if (rows > 0 && !(sawHealth && sawAttack && sawRange && sawSpeed))
                    failures.Add($"[case 8] troop '{c.Id}' shows {rows} stat row(s) and is missing " +
                                 (!sawHealth ? "Health " : "") + (!sawAttack ? "Attack " : "") +
                                 (!sawRange ? "Range " : "") + (!sawSpeed ? "Speed " : "") +
                                 "- the stat table is not being read from TroopStatResolver.");
                if (c.HasNextLevel && !sawDelta)
                    failures.Add($"[case 8] troop '{c.Id}' has a next level (L{c.Level} -> L{c.Level + 1}) and not " +
                                 "one stat row carries a DeltaText, so the card never says what the upgrade " +
                                 "promotes to (WO-1517 §1B item 2).");

                // §1B item 3 - "An UPGRADE button beside TRAIN whenever upgrade is Ready".
                var second = sel.SecondaryAction;
                if (c.UpgradeReady)
                {
                    if (second == null || !second.Visible)
                        failures.Add($"[case 8] troop '{c.Id}' reports UpgradeReady and the card seats NO second " +
                                     "face. ComposeTroopItem has always composed an Upgrade action; it fell on the " +
                                     "floor because ProjectSelection fills the secondary slot from " +
                                     "ActionOf(Cancel) and a troop has no Cancel. See ComposeDetail.");
                    else if (string.IsNullOrWhiteSpace(second.CostText))
                        failures.Add($"[case 8] troop '{c.Id}' seats an UPGRADE face with no cost/time on it - the " +
                                     "ruling asks for \"its time and cost on its face\".");
                    else
                        log.AppendLine($"  case 8 - {c.Id}: {rows} stats, upgrade face \"{second.Label}\" " +
                                       $"({second.CostText})");
                }
                else
                {
                    log.AppendLine($"  case 8 - {c.Id}: {rows} stats, no upgrade face (UpgradeReady=false, " +
                                   $"word='{c.UpgradeWord}')");
                }
            }

            if (measured == 0)
                failures.Add("[case 8] no unlocked troop produced a detail card, so every assertion above passed " +
                             "on an empty roster. FAIL, not a skip.");
            else
                log.AppendLine($"  case 8 OK - {measured} troop detail card(s) measured");

            vm.EnterTab(ManageTabId.Army);   // leave the model on a grid, as the earlier cases found it
        }

        // ── CASE 9 body (WO-1517 army-full band + WO-1518 the travelling notice) ──
        /// <summary>
        /// Fills the fixture's army to its cap, then proves three things the owner's two frames
        /// showed were missing:
        ///   1. the model SAYS the army is full BEFORE the tap (owner-screen-20260906-201037.png
        ///      invited TRAIN . 1M 0S while a footnote underneath contradicted it);
        ///   2. a TRAIN tap is REFUSED with that reason and enqueues nothing;
        ///   3. the refusal sentence does NOT follow the player to another screen
        ///      (owner-screen-20260906-201242.png is the ARMORER RESEARCH screen and it still reads
        ///      "Army is full." in the bottom-left - WO-1518).
        /// RED PROOF: delete the ArmyReadiness block in FillTrainFacts -> 1 and 2 fire; delete the
        /// ClearStaleNotice calls in EnterTab/GoTo -> 3 fires.
        /// </summary>
        private static void CheckArmyFullIsSaidAndDoesNotTravel(ManageScreenVM vm, GameState state,
            List<string> failures, StringBuilder log)
        {
            if (state == null || state.Army == null)
            {
                failures.Add("[case 9] the fixture has no ArmyStorage, so the army cap could not be exercised. " +
                             "FAIL, not a skip.");
                return;
            }

            var seed = vm.TroopChoices.Find(c => c != null && c.Unlocked);
            if (seed == null)
            {
                failures.Add("[case 9] no unlocked troop on the fixture, so the army cap could not be filled.");
                return;
            }

            // Fill to the cap through the GRANT path the completed training job itself uses -
            // never by writing a roster by hand, so the numbers this case reads are the numbers
            // the game produces. Bounded so a cap change can never hang the suite.
            int guard = 0;
            while (guard++ < 500)
            {
                var snap = ArmyReadiness.Compute(state);
                if (snap.CapSlots <= 0) break;
                if (snap.RosterSlots + snap.QueuedSlots >= snap.CapSlots) break;
                if (BarracksProgression.GrantTrainedTroop(state, seed.Id, "manage-train-door oracle") <= 0) break;
            }
            var filled = ArmyReadiness.Compute(state);
            log.AppendLine($"  case 9 fixture: roster={filled.RosterSlots} queued={filled.QueuedSlots} " +
                           $"cap={filled.CapSlots}");
            if (filled.CapSlots <= 0 || filled.RosterSlots + filled.QueuedSlots < filled.CapSlots)
            {
                failures.Add("[case 9] the fixture's army could not be filled to its cap (roster " +
                             filled.RosterSlots + " + queued " + filled.QueuedSlots + " of " + filled.CapSlots +
                             "), so the ARMY FULL band is unmeasured. FAIL, not a skip.");
                return;
            }

            vm.EnterTab(ManageTabId.Army);
            var c2 = vm.TroopChoices.Find(c => c != null && string.Equals(c.Id, seed.Id, StringComparison.Ordinal));
            if (c2 == null)
            {
                failures.Add($"[case 9] '{seed.Id}' vanished from the roster after the rebuild.");
                return;
            }
            if (!c2.ArmyFull)
                failures.Add($"[case 9] the army is at its cap ({filled.RosterSlots + filled.QueuedSlots}/" +
                             $"{filled.CapSlots} slots) and '{seed.Id}' still reports ArmyFull=false. " +
                             "FillTrainFacts must read ArmyReadiness.Compute - the SAME formula " +
                             "BarracksService.EnqueueTraining seeds its own refusal from.");
            if (c2.TrainReady)
                failures.Add($"[case 9] '{seed.Id}' still reports TrainReady at the army cap, so the card invites a " +
                             "tap the service will refuse - the 20:10 defect exactly.");
            if (string.IsNullOrEmpty(c2.TrainStateText) ||
                c2.TrainStateText.IndexOf("Army is full", StringComparison.OrdinalIgnoreCase) < 0)
                failures.Add($"[case 9] '{seed.Id}' TrainStateText reads \"{c2.TrainStateText}\" - at the cap it must " +
                             "NAME the army as the blocker, with the numbers.");

            // The TILE's one word.
            var ws = vm.ComposeWorkspace();
            int index = ws != null && ws.Tabs != null && ws.Tabs.Count > 0
                ? Mathf.Clamp(ws.ActiveTabIndex, 0, ws.Tabs.Count - 1) : -1;
            var tiles = index >= 0 ? ws.Tabs[index].Tiles : null;
            var tile = tiles != null ? FindTile(tiles, seed.Id) : null;
            if (tile == null)
                failures.Add($"[case 9] no ARMY tile for '{seed.Id}' - the grid could not be measured.");
            else if (!string.Equals(tile.StateText, "ARMY FULL", StringComparison.Ordinal))
                failures.Add($"[case 9] the ARMY tile for '{seed.Id}' reads \"{tile.StateText}\" at the cap, not " +
                             "\"ARMY FULL\". Owner 20:10: \"should show if queue is full and army is full\".");
            else
                log.AppendLine("  case 9 OK (band) - the tile reads ARMY FULL and the card refuses TRAIN");

            // 2. the TAP is refused, and it says why.
            var trainRow = vm.BrowseRows.Find(r => r != null &&
                string.Equals(r.ActionText, "Train", StringComparison.Ordinal) &&
                string.Equals(r.SubjectId, seed.Id, StringComparison.Ordinal));
            if (trainRow == null)
            {
                failures.Add($"[case 9] no Train row for '{seed.Id}' to press.");
                return;
            }
            var queue = BuildTimerService.Instance;
            int depthBefore = queue != null ? queue.QueueDepth(ChannelId.Train) : 0;
            trainRow.Activate?.Invoke();
            int depthAfter = queue != null ? queue.QueueDepth(ChannelId.Train) : 0;
            if (depthAfter != depthBefore)
                failures.Add($"[case 9] a TRAIN tap at the army cap enqueued a job anyway (Train depth " +
                             $"{depthBefore} -> {depthAfter}).");
            if (string.IsNullOrEmpty(vm.Notice) ||
                vm.Notice.IndexOf("Army is full", StringComparison.OrdinalIgnoreCase) < 0)
                failures.Add($"[case 9] the refused TRAIN left the notice \"{vm.Notice}\" - a refused verb must say " +
                             "its reason, never a silent no-op (WO-1517 section 3).");
            else
                log.AppendLine($"  case 9 OK (refusal) - notice \"{vm.Notice}\"");

            // 3. WO-1518 - and it STAYS on the screen it belongs to.
            var elsewhere = vm.AvailableTabIds.Contains(ManageTabId.Research)
                ? ManageTabId.Research : ManageTabId.Build;
            vm.EnterTab(elsewhere);
            if (!string.IsNullOrEmpty(vm.Notice))
                failures.Add($"[case 9] the sentence \"{vm.Notice}\" followed the player to the " +
                             $"{ManageScreenVM.TabWordOf(elsewhere)} screen. That is exactly " +
                             "Logs/device/screens/owner-screen-20260906-201242.png: the ARMORER RESEARCH screen " +
                             "reading \"Army is full.\" where the army cap refuses nothing at all (WO-1518). " +
                             "See ManageScreenVM.ClearStaleNotice.");
            else
                log.AppendLine($"  case 9 OK (WO-1518) - the notice does not travel to " +
                               ManageScreenVM.TabWordOf(elsewhere));
        }

        // -- CASE 10 body (WO-1517 acceptance 1: "one MEASURED case per string") -----
        /// <summary>
        /// WO-1517 acceptance line 1 asks for a measured case per string. Case 9 proves ARMY FULL.
        /// This case proves the OTHER TWO: the QUEUE FULL band, and the per-troop upgrade word.
        ///
        /// Owner ruling 2026-09-06 20:10, verbatim: "on train army screens should show if queue is
        /// full and army is full also should show if a troop type can be upgraded".
        ///
        /// The upgrade half is measured FIRST because it is a pure read - filling the Train line
        /// changes UpgradeInProgress for nothing, but it does change the tile badge, and the badge
        /// precedence (ManageScreenVM.cs:4347-4356) puts QUEUE FULL above every upgrade word. So a
        /// full line would hide exactly the word this half exists to measure.
        ///
        /// RED PROOF: delete the CanUpgradeTroop arm in FillUpgradeFacts -> the vocabulary half
        /// fires on the first unlocked troop. Delete the lineFull arm in FillTrainFacts -> the
        /// QUEUE FULL half fires on both the text and the tile.
        /// </summary>
        private static void CheckQueueFullAndUpgradeWords(ManageScreenVM vm,
            List<string> failures, StringBuilder log)
        {
            // -- HALF A: "should show if a troop type can be upgraded" --------------
            // The vocabulary is CLOSED by FillUpgradeFacts: exactly one of these four, always
            // non-empty for an unlocked troop. Asserting the closed set is what makes this a
            // measurement of the WORD rather than of one fixture's happenstance state.
            vm.EnterTab(ManageTabId.Army);
            int measured = 0;
            var seen = new List<string>();
            // ⚠ SNAPSHOT, not the live list. TileFor below calls vm.ComposeWorkspace(), which is one
            // of the writers that REBUILDS TroopChoices - iterating the live list would throw
            // InvalidOperationException on the first troop that reaches the tile check.
            foreach (var c in vm.TroopChoices.ToList())
            {
                if (c == null || !c.Unlocked) continue;
                measured++;
                string w = c.UpgradeWord ?? "";
                if (w.Length == 0)
                {
                    failures.Add($"[case 10] unlocked troop '{c.Id}' carries an EMPTY UpgradeWord, so its tile can " +
                                 "say nothing about whether it can be upgraded. Owner 20:10: \"also should show " +
                                 "if a troop type can be upgraded\".");
                    continue;
                }
                bool known = string.Equals(w, "UPGRADE AVAILABLE", StringComparison.Ordinal) ||
                             string.Equals(w, "MAX", StringComparison.Ordinal) ||
                             string.Equals(w, "UPGRADING", StringComparison.Ordinal) ||
                             w.StartsWith("NEEDS ", StringComparison.Ordinal);
                if (!known)
                    failures.Add($"[case 10] troop '{c.Id}' reads UpgradeWord \"{w}\", which is outside the four " +
                                 "words FillUpgradeFacts composes (UPGRADE AVAILABLE / MAX / UPGRADING / " +
                                 "NEEDS <blocker>). A fifth word means a second predicate appeared.");
                // "NEEDS " with nothing after it is the SHORT defect of WO-1518 wearing another hat:
                // a state word that names no blocker.
                if (w.StartsWith("NEEDS ", StringComparison.Ordinal) && w.Trim().Length <= "NEEDS ".Length)
                    failures.Add($"[case 10] troop '{c.Id}' reads \"{w}\" - the word names no blocker, which is " +
                                 "the WO-1518 defect on the ARMY tab.");
                if (!seen.Contains(w)) seen.Add(w);

                // The word must reach the TILE, or it is composed-but-unpainted again.
                if (string.Equals(w, "UPGRADE AVAILABLE", StringComparison.Ordinal) && !c.ArmyFull)
                {
                    var t = TileFor(vm, c.Id);
                    if (t != null && string.IsNullOrEmpty(t.StateText))
                        failures.Add($"[case 10] troop '{c.Id}' composes \"{w}\" and its ARMY tile paints no state " +
                                     "word at all - composed but unpainted (the WO-1444 / WO-1491 family).");
                }
            }
            if (measured == 0)
            {
                failures.Add("[case 10] no unlocked troop on the fixture, so no upgrade word was measured. " +
                             "FAIL, not a skip - the acceptance asks for a measured case per string.");
                return;
            }
            log.AppendLine($"  case 10 OK (upgrade words) - {measured} troop(s), vocabulary {{{string.Join(", ", seen)}}}");

            // -- HALF B: "should show if queue is full" -----------------------------
            var svc = BuildTimerService.Instance;
            if (svc == null)
            {
                failures.Add("[case 10] BuildTimerService.Instance is null, so the Train line depth cap could not " +
                             "be exercised. FAIL, not a skip.");
                return;
            }
            // ⚠ `HasNextLevel` ADDED 2026-09-06 with the ARMY badge-precedence amendment
            // (ManageScreenVM.cs:4469-4494): a MAXED troop now reads MAX at the line cap, because
            // MAX describes the ITEM and QUEUE FULL describes the shared LINE. So this half must
            // seed on a troop that still HAS an upgrade left, or it would measure the new MAX arm
            // and report it as a missing QUEUE FULL. Case 15 measures the maxed troop.
            var seed = vm.TroopChoices.Find(c => c != null && c.Unlocked && c.TrainReady && c.HasNextLevel);
            if (seed == null)
            {
                failures.Add("[case 10] no unlocked, NON-MAX troop reports TrainReady, so the Train line could not " +
                             "be filled to its depth cap with a troop whose tile can read QUEUE FULL. FAIL, not a skip.");
                return;
            }

            // Fill through the SERVICE, never by writing the channel by hand, so the depth this
            // case reads is the depth the game produces. Bounded so a cap change cannot hang it.
            // The line cap is BuildTimerConfig.queueDepthPerLine (5) and each job costs ONE army
            // slot, so a default 10-slot army cannot cap first - and if the fixture ever changes
            // so that it can, the ArmyFull assertion below names that rather than passing quietly.
            int guard = 0;
            while (guard++ < 64 && !svc.IsLineFull(ChannelId.Train))
                if (BarracksService.EnqueueTraining(seed.Id, 1, out _) <= 0) break;

            int depth = svc.QueueDepth(ChannelId.Train);
            int cap = svc.QueueDepthLimit(ChannelId.Train);
            if (!svc.IsLineFull(ChannelId.Train))
            {
                failures.Add($"[case 10] the Train line could not be filled to its cap (depth {depth}/{cap}), so " +
                             "the QUEUE FULL band is unmeasured. FAIL, not a skip.");
                return;
            }

            vm.EnterTab(ManageTabId.Army);
            var c3 = vm.TroopChoices.Find(c => c != null && string.Equals(c.Id, seed.Id, StringComparison.Ordinal));
            if (c3 == null)
            {
                failures.Add($"[case 10] '{seed.Id}' vanished from the roster after the rebuild.");
                return;
            }
            if (c3.ArmyFull)
            {
                // Not a pass and not a failure of the queue band: it is a fixture that can no
                // longer measure it. Say so loudly rather than green-lighting an unmeasured string.
                failures.Add($"[case 10] filling the Train line to {depth}/{cap} also capped the ARMY " +
                             $"({c3.ArmyUsedSlots}/{c3.ArmyCapSlots} slots), and ArmyFull leads QueueFull in the " +
                             "badge precedence - so QUEUE FULL is unmeasurable on this fixture. Give the fixture " +
                             "a larger army cap, or a troop cheaper in slots.");
                return;
            }
            if (c3.TrainReady)
                failures.Add($"[case 10] the Train line is FULL ({depth}/{cap}) and '{seed.Id}' still reports " +
                             "TrainReady, so the card invites a tap the queue will refuse.");
            if (string.IsNullOrEmpty(c3.QueueFullText) || !HasDigit(c3.QueueFullText))
                failures.Add($"[case 10] at the line cap '{seed.Id}' composes QueueFullText \"{c3.QueueFullText}\" - " +
                             "the band must NAME the line and carry its numbers, the way the ARMY FULL band does. " +
                             "Owner 20:10: \"should show if queue is full\".");
            var tile2 = TileFor(vm, seed.Id);
            if (tile2 == null)
                failures.Add($"[case 10] no ARMY tile for '{seed.Id}' - the grid could not be measured.");
            else if (!string.Equals(tile2.StateText, "QUEUE FULL", StringComparison.Ordinal))
                failures.Add($"[case 10] the ARMY tile for '{seed.Id}' reads \"{tile2.StateText}\" at the line cap, " +
                             "not \"QUEUE FULL\" (ManageScreenVM.cs:4348).");
            else
                log.AppendLine($"  case 10 OK (queue band) - depth {depth}/{cap}, tile \"QUEUE FULL\", " +
                               $"text \"{c3.QueueFullText}\"");
        }

        // ── CASE 15: THE STATE WORD DESCRIBES THE ITEM, NOT THE LINE ──────────────
        //
        // CAUSE CAPTURED, NOT INFERRED (CLAUDE.md section 12). Builds/cap-manage-wave3.log:3832:
        //   "ManageFlow_ARMY_max capture threw: no max item reachable on the ARMY tab -- the
        //    fixture did not produce that state ... States actually present: QueueBlocked,Locked
        //    over 9 tiles"
        // -> MANAGE_FLOW_MAP_FAIL frames=14/15 (`:8251`). The same run had already traced, at
        // `:3775`, "troop state id=troop-footman word=Max upgrading=False hasNext=False" beside
        // "lineFull=True": a MAXED troop was wearing QUEUE FULL, so no Max tile existed to shoot.
        //
        // QUEUE FULL and ARMY FULL are properties of the SHARED line and the roster cap - the same
        // on all nine tiles, so they discriminate nothing. MAX is a property of THIS troop. The
        // precedence at ManageScreenVM.cs:4469-4494 now hoists `atMax` above both capacity
        // blockers, and this case is what stops it being un-hoisted.
        //
        // RED RECIPE: move the `else if (atMax)` arm back below the ArmyFull / trainLineFull arms.
        private static void CheckMaxTroopOutranksTheFullLine(ManageScreenVM vm, GameState state,
            List<string> failures, StringBuilder log)
        {
            var svc = BuildTimerService.Instance;
            if (svc == null || !svc.IsLineFull(ChannelId.Train))
            {
                failures.Add("[case 15] the Train line is not at its cap when this case runs, so 'MAX outranks a " +
                             "full line' is unmeasured. Case 10 fills it; this case must run after it. " +
                             "FAIL, not a skip.");
                return;
            }

            // ⛔ THE FIXTURE MUST MAKE ITS OWN MAXED TROOP - the roster does not start with one.
            // Measured 2026-09-06 (Builds/reg-wave3e.log): this case FAILED with "no unlocked troop
            // on the fixture has run out of upgrades" while the capture chain's
            // ManageFlow_ARMY_max frame shot fine in the same wave. The capture seeds it
            // explicitly, and this is the SAME seam, copied deliberately rather than invented:
            //   UICaptureLaunch.cs:7997-7998
            //     fixture.TroopLevels["troop-footman"] = BarracksProgression.MaxTroopLevel("troop-footman");
            // ⚠ NOT a magic 99, for the reason that file records at :7991-7996: TroopLevelOf
            // returns the stored value UNCLAMPED, so a sentinel would paint "Level 99" on the card.
            // MaxTroopLevel reads troop-upgrades.json through TroopUpgradeCatalog, which resolves
            // in edit mode.
            const string maxTroopId = "troop-footman";
            if (state == null || state.TroopLevels == null)
            {
                failures.Add("[case 15] the fixture GameState has no TroopLevels dictionary, so a maxed troop " +
                             "cannot be seeded and the MAX word is unmeasured. FAIL, not a skip.");
                return;
            }
            int ceiling = BarracksProgression.MaxTroopLevel(maxTroopId);
            if (ceiling <= 0)
            {
                failures.Add($"[case 15] BarracksProgression.MaxTroopLevel('{maxTroopId}') returned {ceiling}, so " +
                             "troop-upgrades.json did not resolve and no ceiling is known. FAIL, not a skip - " +
                             "this is the same catalog the ARMY_max capture frame depends on.");
                return;
            }
            state.TroopLevels[maxTroopId] = ceiling;

            vm.EnterTab(ManageTabId.Army);   // rebuilds TroopChoices off the seeded level
            var maxed = vm.TroopChoices.Find(c => c != null && c.Unlocked && !c.HasNextLevel);
            if (maxed == null)
            {
                failures.Add($"[case 15] '{maxTroopId}' was seeded to its ceiling (level {ceiling}) and STILL no " +
                             "unlocked troop reports HasNextLevel == false. The ladder seam moved: either " +
                             "BarracksProgression.MaxTroopLevel and TroopChoiceVM.HasNextLevel now read different " +
                             "ceilings, or TroopLevels is no longer the level source. ManageFlow_ARMY_max has no " +
                             "item to photograph either. FAIL, not a skip.");
                return;
            }

            var tile = TileFor(vm, maxed.Id);
            if (tile == null)
            {
                failures.Add($"[case 15] no ARMY tile for the maxed troop '{maxed.Id}' - the grid could not be read.");
                return;
            }
            if (!string.Equals(tile.StateText, "MAX", StringComparison.Ordinal))
                failures.Add($"[case 15] the maxed troop '{maxed.Id}' reads \"{tile.StateText}\" while the Train " +
                             $"line is at its cap ({svc.QueueDepth(ChannelId.Train)}/" +
                             $"{svc.QueueDepthLimit(ChannelId.Train)}). It must read MAX: the tile's one word " +
                             "describes the ITEM, and QUEUE FULL is a property of the shared line that is " +
                             "identical on every tile. This is the captured ManageFlow_ARMY_max failure " +
                             "(cap-manage-wave3.log:3832) - with no Max tile the frame cannot be shot honestly.");
            else if (tile.VisualState != ManageTileVisualState.Max)
                failures.Add($"[case 15] the maxed troop '{maxed.Id}' reads the WORD \"MAX\" but its VisualState is " +
                             $"{tile.VisualState}. The capture selects the frame by visual state, so the word alone " +
                             "does not make ManageFlow_ARMY_max shootable.");
            else
                log.AppendLine($"  case 15 OK - maxed troop '{maxed.Id}' reads MAX at line cap " +
                               $"{svc.QueueDepth(ChannelId.Train)}/{svc.QueueDepthLimit(ChannelId.Train)}");
        }

        private static bool HasDigit(string s)
        {
            if (string.IsNullOrEmpty(s)) return false;
            for (int i = 0; i < s.Length; i++) if (s[i] >= '0' && s[i] <= '9') return true;
            return false;
        }

        /// <summary>The ARMY tile the model is currently composing for <paramref name="id"/>, or null.</summary>
        private static ManageTileVM TileFor(ManageScreenVM vm, string id)
        {
            var ws = vm.ComposeWorkspace();
            if (ws == null || ws.Tabs == null || ws.Tabs.Count == 0) return null;
            int index = Mathf.Clamp(ws.ActiveTabIndex, 0, ws.Tabs.Count - 1);
            var tiles = ws.Tabs[index].Tiles;
            return tiles != null ? FindTile(tiles, id) : null;
        }

        private static ManageTileVM FindTile(IReadOnlyList<ManageTileVM> tiles, string id)
        {
            for (int i = 0; i < tiles.Count; i++)
                if (tiles[i] != null && string.Equals(tiles[i].Id, id, StringComparison.Ordinal))
                    return tiles[i];
            return null;
        }

        // ── 5. the door stays SINGLE (canon CLAUDE.md §7: one Queues entry) ────
        // ── CASE 11 (WO-1541): the army line READS the published camp; it does not re-derive it ──
        //
        // THE DEFECT THIS CASE IS SHAPED AROUND. ManageScreenVM.BuildTroopArmySummary built its own
        // Hero.RaidSelectionVM over SceneConfigCatalog.All-where-IsEnemy and walked it for the
        // lowest unlockVictories, to compose "Army 8 / 10 - The Forsaken Camp fields 12". The
        // Journey deck composed a sibling sentence from PostureSignals.RaidOpenCampCount. TWO
        // independent derivations of "which camp is next", different separators, nothing keeping
        // them equal - the duplicated-state class PlayerDeckWorkspace.cs:719-723 names in words.
        //
        // The fixture PUBLISHES a camp no catalog contains and asserts the VM says it back. A VM
        // that re-derives from the catalog CANNOT pass this: it would name a real camp, or none.
        // RED RECIPE: restore the RaidSelectionVM walk in BuildTroopArmySummary -> the summary
        // stops carrying "Fixture Hollow" and this case fires.
        private static void CheckArmyLineReadsThePublishedCamp(ManageScreenVM vm, List<string> failures, StringBuilder log)
        {
            if (vm == null) { failures.Add("[case 11] no ManageScreenVM to measure."); return; }

            // The posture rail is STATIC. Snapshot and restore, or every later suite in this
            // batchmode run reads a fixture camp (the UICaptureLaunch discipline).
            int wasUsed = DeNelle.Core.HudModel.PostureSignals.ArmyFillUsed;
            int wasCap = DeNelle.Core.HudModel.PostureSignals.ArmyFillCap;
            string wasCamp = DeNelle.Core.HudModel.PostureSignals.RaidNextCampName;
            int wasGarrison = DeNelle.Core.HudModel.PostureSignals.RaidNextCampGarrison;
            try
            {
                // A name that exists in NO catalog, deliberately - see the header above.
                const string fixtureCamp = "Fixture Hollow";
                DeNelle.Core.HudModel.PostureSignals.SetArmyFill(8, 10);
                DeNelle.Core.HudModel.PostureSignals.SetRaidNextCamp(fixtureCamp, 12);
                vm.Rebuild();

                string line = vm.TroopArmySummaryText ?? "";
                if (line.IndexOf("Army 8 / 10", StringComparison.Ordinal) < 0)
                    failures.Add($"[case 11] the army line reads \"{line}\" - it has lost the published army fill " +
                                 "(PostureSignals.ArmyFillUsed / ArmyFillCap).");
                if (line.IndexOf(fixtureCamp, StringComparison.Ordinal) < 0)
                    failures.Add($"[case 11] the army line reads \"{line}\" but the raid authority published " +
                                 $"\"{fixtureCamp}\". THIS IS THE ONE-PRODUCER CASE: the VM is deriving its own " +
                                 "next camp again instead of reading PostureSignals.RaidNextCampName, and the two " +
                                 "derivations will drift. See ManageScreenVM.BuildTroopArmySummary.");
                if (line.IndexOf("12", StringComparison.Ordinal) < 0)
                    failures.Add($"[case 11] the army line reads \"{line}\" - the published garrison " +
                                 "(RaidNextCampGarrison = 12) is not on it, so the sentence names an enemy " +
                                 "without saying what the player is up against.");

                // WO-1541 ruling 2 - the model AUTHORS the door. It is not painted yet (the card has
                // no seat clearing MinTouchPx; the View reports that), but the model must publish it
                // so the seat call is the only thing left to make.
                if (vm.TroopArmyDoor == null)
                    failures.Add("[case 11] a camp is published and TroopArmyDoor is NULL - WO-1541 ruling 2 says " +
                                 "the army line carries a door to the raid grid, and the model is where it is decided.");
                if (string.IsNullOrEmpty(vm.TroopArmyDoorLabel) ||
                    vm.TroopArmyDoorLabel.IndexOf("FIXTURE HOLLOW", StringComparison.Ordinal) < 0)
                    failures.Add($"[case 11] the door face reads \"{vm.TroopArmyDoorLabel}\" - it must name the SAME " +
                                 "published camp the sentence does, or the two disagree about where the door goes.");

                // The other half of the fact: NO camp published => no clause, and no door. A live
                // button with no destination is the defect the null check exists to prevent.
                DeNelle.Core.HudModel.PostureSignals.SetRaidNextCamp(null, 0);
                vm.Rebuild();
                string bare = vm.TroopArmySummaryText ?? "";
                if (bare.IndexOf(fixtureCamp, StringComparison.Ordinal) >= 0)
                    failures.Add($"[case 11] with no camp published the army line still reads \"{bare}\" - a stale " +
                                 "camp name is cached somewhere instead of being read fresh from the authority.");
                if (vm.TroopArmyDoor != null || !string.IsNullOrEmpty(vm.TroopArmyDoorLabel))
                    failures.Add("[case 11] no camp is published and the army line still offers a door - it would " +
                                 "open the raid grid on nothing. Door and label are set and cleared together.");
                else
                    log.AppendLine($"  case 11 OK - army line \"{line}\" reads the ONE published camp; no camp, no door");
            }
            finally
            {
                DeNelle.Core.HudModel.PostureSignals.SetArmyFill(wasUsed, wasCap);
                DeNelle.Core.HudModel.PostureSignals.SetRaidNextCamp(wasCamp, wasGarrison);
            }
        }

        // ── CASE 13 (WO-1564 part 2): the queue drawer names the STRUCTURE and the LEVEL ──
        //
        // THE DEFECT. ManageFlow_BUILD_queue rows read "Tower Ground Archer -> L2" and
        // "Barracks -> L4". Composed in the MODEL, not the View: ManageScreenVM.MakeJobRow wrote
        // name + " -> L" + TargetTier, and on a catalog MISS fell through to
        // BuildTimerService.PrettyJobLabel, which title-cases the id's OWN TOKENS
        // (tower_ground_archer -> "Tower Ground Archer") with its comment conceding "no catalog
        // lookup". The player read an internal identifier as a name and a developer's arrow as a
        // level - while Manage canon 9's ban on the UI parsing ids was technically honoured,
        // because it was the VM doing it. The rule binds wherever the string is MADE.
        //
        // RED RECIPE: restore `label = name + " -> L" + job.TargetTier` in MakeJobRow, or delete
        // the catalog-miss branch beside it.
        private static void CheckQueueRowsNameThingsInWords(ManageScreenVM vm, BuildTimerService svc,
            GameState state, List<string> failures, StringBuilder log)
        {
            if (vm == null || svc == null || state == null)
            { failures.Add("[case 13] no live VM/service/state to measure."); return; }

            // A REAL structure the catalog knows, and a BOGUS one NEITHER catalog does - the two
            // halves of the acceptance, on the same channel, in one pass.
            // ⛔ THE BOGUS ID IS DELIBERATELY NOT "tower_ground_archer". That is a REAL structures-
            // catalog id (it only misses BuildingTierCatalog, which holds tier-ladder buildings
            // only), so using it here would assert the opposite of the fix: towers must resolve to
            // their display name, not to the placeholder.
            const string bogusId = "no_such_structure_zz";
            svc.Enqueue(JobKind.Upgrade, ChannelId.Builder, "barracks", 600.0, 4);
            svc.Enqueue(JobKind.Upgrade, ChannelId.Builder, bogusId, 600.0, 2);

            vm.SelectQueueOverlayChannel(ChannelId.Builder);
            vm.Rebuild();

            var rows = new List<QueueRowVM>(vm.QueueRows);
            log.AppendLine($"  case 13 read {rows.Count} Builder queue row(s):");
            for (int i = 0; i < rows.Count; i++)
                log.AppendLine($"    \"{rows[i].Label}\"");

            if (rows.Count == 0)
            {
                failures.Add("[case 13] the Builder queue composed ZERO rows after two Enqueue calls, so the " +
                             "label assertions would pass on nothing. FAIL, not a skip.");
                return;
            }

            foreach (var row in rows)
            {
                string label = row != null ? (row.Label ?? "") : "";
                // ⛔ ARROW NOTATION. "-> L2" is a developer's shorthand, not a level.
                if (label.IndexOf("->", StringComparison.Ordinal) >= 0)
                    failures.Add($"[case 13] queue row \"{label}\" carries the developer arrow notation. The row " +
                                 "must name the structure and the level IN WORDS (\"Barracks - Level 4\").");
                // ⛔ ID GRAMMAR. Underscores and colons are id grammar, never display grammar - a
                // title-cased id is still an id, which is exactly what PrettyJobLabel produced.
                if (label.IndexOf('_') >= 0 || label.IndexOf(':') >= 0)
                    failures.Add($"[case 13] queue row \"{label}\" carries raw id grammar. A " +
                                 "'tower_ground_archer'-shaped string must never reach the player, prettified " +
                                 "or otherwise.");
                // ⛔ THE TITLE-CASED ID ITSELF - the shape the capture showed, and the one a silent
                // id-prettifier fallback puts back.
                if (label.IndexOf("No Such Structure", StringComparison.Ordinal) >= 0)
                    failures.Add($"[case 13] queue row \"{label}\" is the title-cased id '{bogusId}' presented as " +
                                 "a structure name. A catalog miss must be a TRACED FAILURE (FlowTrace.Fail) with " +
                                 "an honest placeholder, never quietly prettified - CLAUDE.md section 12.");
            }

            // ⛔ THE MISS ROW MUST BE ON SCREEN AT ALL. Without this the loop above green-passes on
            // the barracks row alone whenever Enqueue refuses the bogus job, and the entire
            // catalog-miss half of the acceptance would be asserted against nothing.
            var missRow = rows.Find(r => r != null && string.Equals(r.JobId, bogusId, StringComparison.Ordinal));
            if (missRow == null)
                failures.Add($"[case 13] the bogus job '{bogusId}' produced no queue row, so the catalog-miss " +
                             "half of this case measured nothing. FAIL, not a skip.");
            else if (missRow.Label == null || missRow.Label.IndexOf("Unknown structure", StringComparison.Ordinal) < 0)
                failures.Add($"[case 13] the row for the uncatalogued job '{bogusId}' reads \"{missRow.Label}\" - a " +
                             "miss must paint an HONEST placeholder beside its FlowTrace.Fail, never a name the " +
                             "player will believe.");

            var named = rows.Find(r => r != null && r.Label != null &&
                                       r.Label.IndexOf("Barracks", StringComparison.Ordinal) >= 0);
            if (named == null)
                failures.Add("[case 13] no Builder queue row names the Barracks at all - the catalog display name " +
                             "is no longer reaching the row, so every row would be falling through to the " +
                             "id-prettifier.");
            else if (named.Label.IndexOf("Level 4", StringComparison.Ordinal) < 0)
                failures.Add($"[case 13] the Barracks row reads \"{named.Label}\" - the target level is not stated " +
                             "in words, so the row does not say what the job is actually going to do.");
            else
                log.AppendLine($"  case 13 OK - \"{named.Label}\"; no arrow notation, no id grammar in any row");
        }

        // ── CASE 12 (WO-1541 acceptance 1): THE SECOND-COMPOSER TRIPWIRE ──────────
        //
        // ⚠ THIS HALF IS A SOURCE CHECK BY NATURE, and that is stated rather than dressed up as a
        // fixture. Case 11 proves the VM READS the authority today; it cannot prove that nobody
        // adds a SECOND derivation tomorrow beside it, because a second producer that agrees with
        // the first on the fixture's inputs passes every behavioural assertion - right up until the
        // day the two disagree, which is the whole defect. The WO-1521 ClaimableCount fix closed
        // the identical class for quests the identical way.
        private static void CheckNoSecondCampComposer(List<string> failures, StringBuilder log)
        {
            string vm = ReadSource(VmPath, failures);
            if (vm != null)
            {
                string code = StripLineComments(vm);
                if (code.IndexOf("new Hero.RaidSelectionVM(", StringComparison.Ordinal) >= 0 ||
                    code.IndexOf("RaidSelectionVM.CreateDefault(", StringComparison.Ordinal) >= 0)
                    failures.Add("[case 12] ManageScreenVM constructs a RaidSelectionVM again - that is a SECOND " +
                                 "producer of 'which camp is next' beside BuildTimerService.PublishJourneyOpenCamps, " +
                                 "and the drift between two derivations IS the WO-1541 defect. Read " +
                                 "PostureSignals.RaidNextCampName instead. (It is not an assembly violation, which " +
                                 "is exactly why only an oracle can stop it.)");
                if (code.IndexOf("PostureSignals.RaidNextCampName", StringComparison.Ordinal) < 0)
                    failures.Add("[case 12] ManageScreenVM no longer reads PostureSignals.RaidNextCampName - the army " +
                                 "line has stopped consuming the one published camp fact.");
            }

            string producer = ReadSource("Assets/_Modules/Village/Buildings/BuildTimerService.cs", failures);
            if (producer != null && StripLineComments(producer)
                    .IndexOf("PostureSignals.SetRaidNextCamp", StringComparison.Ordinal) < 0)
                failures.Add("[case 12] BuildTimerService no longer publishes SetRaidNextCamp - the ONE producer of " +
                             "the next-camp fact is gone, so every reader falls back to nothing.");

            // WO-1541 acceptance 4: the line is not typeset at the kit's smallest authored role.
            // ElarionUi.cs reserves FontMicro for "hotkey badge, rune strip"; the most motivating
            // sentence on the card was ranked there. RED RECIPE: put FontMicro back on the `army`
            // label in ManageScreenPanel.FillTroopCard.
            string panel = ReadSource(PanelPath, failures);
            if (panel != null)
            {
                string code = StripLineComments(panel);
                int at = code.IndexOf("_vm.TroopArmySummaryText", StringComparison.Ordinal);
                if (at < 0)
                    failures.Add("[case 12] ManageScreenPanel no longer paints TroopArmySummaryText - the army/camp " +
                                 "sentence has left the screen entirely.");
                else
                {
                    // The label call spans a few lines; read the statement, not the whole file.
                    int end = Math.Min(code.Length, at + 400);
                    string stmt = code.Substring(at, end - at);
                    if (stmt.IndexOf("ElarionUi.FontMicro", StringComparison.Ordinal) >= 0)
                        failures.Add("[case 12] the army/camp line is typeset at ElarionUi.FontMicro again - the kit's " +
                                     "SMALLEST authored role (ElarionUi.cs: \"hotkey badge, rune strip\"). WO-1541 " +
                                     "acceptance 4. Do not pay for the rank-up by shrinking a neighbouring band.");
                    else
                        log.AppendLine("  case 12 OK - one camp producer; the army line is above FontMicro");
                }
            }
        }

        // ── CASE 14 (WO-1541 ruling 2, owner 2026-09-06 "raise the card, tappable row") ──────
        //
        // The ruling: the army line grows into a MinTouchPx(112) row carrying a chevron and becomes
        // the door to the raid grid; the ARMY card grows past its 256px floor to pay for it;
        // NOTHING ELSE SHRINKS. This case pins all three halves, because each one is a rule someone
        // could quietly undo while the screen still looked plausible:
        //   * a 26px row would ship a P0 touch-floor violation with a visible affordance on it;
        //   * a card that did NOT grow means the row was paid for by a neighbour, which is exactly
        //     what WO-1422 ruling 3.10 and the 24px-band note forbid;
        //   * a door with no label is a button the player cannot read.
        //
        // ⚠ SOURCE-DERIVED ARITHMETIC, and said so rather than dressed up: DataRegression cannot
        // instantiate ManageScreenPanel, so the band ladder is replayed from the constants exactly
        // as ManageQueueDrawerRegression case 9 replays the drawer's. The constants are the
        // authority the renderer reads, so the sum is the shipped geometry, not a restatement.
        // RED RECIPE: set TroopArmyDoorRowPx to 26f, or seat the workspace at TroopWorkspacePx again.
        private static void CheckArmyDoorRowIsTallEnoughAndTheCardGrew(List<string> failures, StringBuilder log)
        {
            string panel = ReadSource(PanelPath, failures);
            if (panel == null) return;
            int before = failures.Count;   // this CASE's verdict, not the whole suite's

            float row = SourceConst(panel, "TroopArmyDoorRowPx");
            float legacy = SourceConst(panel, "TroopWorkspacePx");
            float gap = SourceConst(panel, "ArmyGapPx");
            float tight = SourceConst(panel, "ArmyTightGapPx");
            float cta = SourceConst(panel, "ArmyCtaPx");
            float fact = SourceConst(panel, "ArmyFactPx");
            float desc = SourceConst(panel, "ArmyDescPx");
            float nameBand = SourceConst(panel, "ArmyNamePx");
            if (row <= 0f || legacy <= 0f || gap <= 0f || tight <= 0f || cta <= 0f ||
                fact <= 0f || desc <= 0f || nameBand <= 0f)
            {
                failures.Add("[case 14] could not read the ARMY card's band constants off the source, so the " +
                             "touch-floor and card-growth arithmetic cannot be replayed. FAIL, not a skip - a " +
                             "suite that green-passes on an unreadable seam asserts nothing.");
                return;
            }

            // 1. THE ROW CLEARS THE TOUCH FLOOR, AUTHORED not clamped.
            const float MinTouchPx = 112f;   // ElarionUiKit.MinTouchPx, the P0 floor
            if (row < MinTouchPx)
                failures.Add($"[case 14] the army door row is {row}px, under MinTouchPx({MinTouchPx}). The 26px " +
                             "band is exactly why this door was refused a seat before the ruling; shipping a " +
                             "visible affordance on a sub-floor rect is worse than shipping no door.");

            // 2. THE CARD GREW - the row was NOT paid for by a neighbour.
            float card = gap + cta + gap + fact + gap + desc + tight + row + tight + nameBand;
            if (card <= legacy)
                failures.Add($"[case 14] the ARMY card sums to {card}px against the legacy {legacy}px - it did NOT " +
                             "grow, so the door row was paid for by shrinking a neighbouring band. The owner's " +
                             "ruling is 'raise the card'; WO-1422 ruling 3.10 forbids the squeeze.");
            // Every other band must still hold the pixels it had on the 260px card. These are the
            // measured originals; if one drops, a neighbour paid for the row after all.
            if (cta < 113.1f) failures.Add($"[case 14] the CTA band is {cta}px, under the 113.1px TRAIN/UPGRADE had.");
            if (desc < 39.0f) failures.Add($"[case 14] the description band is {desc}px, under the 39.0px it had.");
            if (nameBand < 40.3f) failures.Add($"[case 14] the NAME band is {nameBand}px, under the 40.3px it had.");
            if (fact < 31.2f) failures.Add($"[case 14] the train-fact band is {fact}px, under the 31.2px it had.");

            // 3. THE WORKSPACE IS SEATED AT THE TALLER CARD. A grown ladder that nothing hosts is
            //    a card that still renders at 260px with its bands overlapping.
            string code = StripLineComments(panel);
            if (code.IndexOf("MakeRowHost(\"TroopSplitWorkspace\", TroopCardPx)", StringComparison.Ordinal) < 0)
                failures.Add("[case 14] the Troops workspace is not seated at TroopCardPx - the band ladder grew " +
                             "but its host did not, so the card paints at the old height with the door row " +
                             "overlapping whatever is under it.");

            // 4. THE DOOR IS A REAL, LABELLED, MODEL-DRIVEN CONTROL - and it opens the raid grid by
            //    the SAME call the Journey deck's Raids card makes (PlayerDeckWorkspace: RequestOpen).
            if (code.IndexOf("TroopCta_RaidDoor", StringComparison.Ordinal) < 0)
                failures.Add("[case 14] no TroopCta_RaidDoor control is built - the army row is a label again and " +
                             "the sentence that names the player's enemy offers nothing to press.");
            if (code.IndexOf("_vm.TroopArmyDoor", StringComparison.Ordinal) < 0)
                failures.Add("[case 14] the View no longer reads _vm.TroopArmyDoor - the door would be decided in " +
                             "the View, which the MVVM conformance rule forbids.");
            string vm = ReadSource(VmPath, failures);
            if (vm != null)
            {
                string vmCode = StripLineComments(vm);
                if (vmCode.IndexOf("RaidEntryGate.RequestOpen", StringComparison.Ordinal) < 0)
                    failures.Add("[case 14] the army door no longer opens the raid grid through " +
                                 "RaidEntryGate.RequestOpen - the exact call the Journey deck's Raids card makes. " +
                                 "A second entry path (a direct RaidSelectionScreen.Open, or a new PanelId) forks " +
                                 "the one raid door, which is the defect this whole ticket is about.");
                if (vmCode.IndexOf("TroopArmyDoorLabel", StringComparison.Ordinal) < 0)
                    failures.Add("[case 14] the VM no longer publishes TroopArmyDoorLabel - the door has no face.");
            }

            if (failures.Count == before)
                log.AppendLine($"  case 14 OK - army door row {row}px >= MinTouchPx({MinTouchPx}) on a {card}px " +
                               $"card (was {legacy}px); no neighbouring band shrank");
        }

        /// <summary>
        /// A <c>private const float Name = 12.5f;</c> value read off source, or -1 when absent.
        /// The ManageQueueDrawerRegression case-9 idiom: the constants ARE the shipped geometry, so
        /// replaying their arithmetic measures the screen without instantiating a panel.
        /// </summary>
        private static float SourceConst(string src, string name)
        {
            if (string.IsNullOrEmpty(src) || string.IsNullOrEmpty(name)) return -1f;
            int at = src.IndexOf(name + " = ", StringComparison.Ordinal);
            if (at < 0) return -1f;
            int from = at + name.Length + 3;
            int i = from;
            while (i < src.Length && (char.IsDigit(src[i]) || src[i] == '.')) i++;
            if (i == from) return -1f;
            return float.TryParse(src.Substring(from, i - from),
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : -1f;
        }

        private static void CheckSingleDoorSource(List<string> failures, StringBuilder log)
        {
            string vm = ReadSource(VmPath, failures);
            if (vm != null)
            {
                string code = StripLineComments(vm);
                if (code.IndexOf("BarracksService.EnqueueTraining", StringComparison.Ordinal) < 0)
                    failures.Add("[case 5] ManageScreenVM no longer calls BarracksService.EnqueueTraining — a forked " +
                                 "enqueue would bypass the unlock gate, the army cap and the resource spend.");
                if (code.IndexOf("TroopDialogueCommands.ShowMusterUI", StringComparison.Ordinal) < 0)
                    failures.Add("[case 5] ManageScreenVM no longer routes the Armies entry to " +
                                 "TroopDialogueCommands.ShowMusterUI — the muster panel has one opener, not two.");
                if (code.IndexOf("BuildTimerService.Instance.Enqueue", StringComparison.Ordinal) >= 0)
                    failures.Add("[case 5] ManageScreenVM enqueues on BuildTimerService DIRECTLY — training must go " +
                                 "through BarracksService so the charge and the cap cannot be skipped.");
            }

            string inter = ReadSource(InteractablePath, failures);
            if (inter != null)
            {
                string code = StripLineComments(inter);
                if (code.IndexOf("\"barracks\"", StringComparison.Ordinal) < 0)
                    failures.Add("[case 5] \"barracks\" is no longer in BuildingInteractable._noTalkDoor — the barracks " +
                                 "talk-door has been REOPENED. That is a SECOND training door and it contradicts " +
                                 "PROD-002 + canon §7 (one entry). If training is unreachable, fix ManageScreenVM.");
                else
                    log.AppendLine("  case 5 OK - barracks talk-door still closed; Manage is the single door");
            }

            // ── CASE 6 (added 2026-09-04, WO-1382): the door has TWO VERBS and NO MODE SWITCH ──
            // Owner ruling 2026-09-04 22:50, verbatim: "Delete this entirely: TRAIN | UPGRADE
            // OPTIONS. That should not be a mode switch." and "The review recommends removing
            // _troopMode entirely rather than trying to make the segmented control prettier. I
            // strongly agree." The old boxed TRAIN face was a view toggle that never enqueued
            // anything, sitting above the real priced CTA - three things said TRAIN and one
            // trained. This pin fails the build if the toggle field returns, or if either verb
            // face ("TRAIN 1 <NAME>" / "UPGRADE TO L<n>") is no longer authored on the panel.
            string panel = ReadSource(PanelPath, failures);
            if (panel != null)
            {
                string code = StripLineComments(panel);
                if (code.IndexOf("_troopMode", StringComparison.Ordinal) >= 0)
                    failures.Add("[case 6] ManageScreenPanel carries a _troopMode field again - the TRAIN | UPGRADE " +
                                 "OPTIONS mode switch was deleted by owner ruling (WO-1382): \"That should not be a " +
                                 "mode switch.\" Two verbs, two buttons, no toggle.");
                if (code.IndexOf("\"TRAIN 1 \"", StringComparison.Ordinal) < 0)
                    failures.Add("[case 6] the primary verb face \"TRAIN 1 <NAME>\" is not authored on the panel - the " +
                                 "Troops card must carry ONE priced TRAIN verb with the unit count in its label.");
                if (code.IndexOf("\"UPGRADE TO L\"", StringComparison.Ordinal) < 0)
                    failures.Add("[case 6] the secondary verb face \"UPGRADE TO L<n>\" is not authored on the panel - " +
                                 "upgrade is a second BUTTON on the same card, never a mode.");
                if (failures.Count == 0 || !failures.Exists(f => f.StartsWith("[case 6]", StringComparison.Ordinal)))
                    log.AppendLine("  case 6 OK - no _troopMode; TRAIN 1 <NAME> and UPGRADE TO L<n> are both authored");

                // -- CASE 7 (source half, WO-1389): the View actually PAINTS the destination --
                // The VM half above proves the sentence is composed; this proves the panel reads
                // it into the UPGRADE face's sub-line (a composed-but-unpainted line is invisible).
                if (code.IndexOf("NextUnlockText", StringComparison.Ordinal) < 0)
                    failures.Add("[case 7] ManageScreenPanel never reads TroopChoiceVM.NextUnlockText - the next-unlock " +
                                 "sentence is composed by the VM but never painted under the UPGRADE face.");
                else
                    log.AppendLine("  case 7 OK (source) - ManageScreenPanel composes NextUnlockText into the UPGRADE sub-line");
            }
        }

        // ── helpers ───────────────────────────────────────────────────────────

        private static bool InstallState(GameStateService svc, GameState state)
        {
            var f = typeof(GameStateService).GetField("_state", BindingFlags.NonPublic | BindingFlags.Instance);
            if (f == null) return false;
            f.SetValue(svc, state);
            return SetGssInstance(svc);
        }

        private static bool SetGssInstance(GameStateService svc)
        {
            var f = typeof(GameStateService).GetField("_instance", BindingFlags.NonPublic | BindingFlags.Static);
            if (f == null) return false;
            f.SetValue(null, svc);
            return true;
        }

        /// <summary>
        /// Installs the BuildTimerService singleton without play mode. Instance is an auto-property
        /// with a private setter, so the compiler-generated backing field is the seam; the property
        /// setter is tried first so a hand-written field would also be found.
        /// </summary>
        private static bool InstallQueueInstance(BuildTimerService svc)
        {
            var t = typeof(BuildTimerService);
            var prop = t.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static);
            if (prop != null && prop.GetSetMethod(true) != null)
            {
                prop.GetSetMethod(true).Invoke(null, new object[] { svc });
                return ReferenceEquals(BuildTimerService.Instance, svc);
            }
            var f = t.GetField("<Instance>k__BackingField", BindingFlags.NonPublic | BindingFlags.Static)
                    ?? t.GetField("_instance", BindingFlags.NonPublic | BindingFlags.Static);
            if (f == null) return false;
            f.SetValue(null, svc);
            return ReferenceEquals(BuildTimerService.Instance, svc);
        }

        /// <summary>Drops every whole-line // and /// comment so a source oracle matches CODE, not prose.</summary>
        private static string StripLineComments(string src)
        {
            if (string.IsNullOrEmpty(src)) return src;
            var sb = new StringBuilder(src.Length);
            foreach (string line in src.Split('\n'))
            {
                string t = line.TrimStart();
                if (t.StartsWith("//", StringComparison.Ordinal) || t.StartsWith("*", StringComparison.Ordinal)) continue;
                sb.Append(line).Append('\n');
            }
            return sb.ToString();
        }

        /// <summary>Repo-relative read. The repo ROOT is machine-dependent (CLAUDE.md §0), so it is
        /// resolved at runtime from the working directory and never hardcoded.</summary>
        private static string ReadSource(string relativePath, List<string> failures)
        {
            string full = Path.Combine(Directory.GetCurrentDirectory(),
                                       relativePath.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(full))
            {
                failures.Add($"source file missing: {relativePath}");
                return null;
            }
            return File.ReadAllText(full);
        }

        private static bool Verdict(List<string> failures, StringBuilder log, out string reason)
        {
            if (failures.Count == 0)
            {
                reason = "MANAGE TRAIN DOOR OK - the Troops tab emits Train rows + Upgrade rows (verb-led, distinct) " +
                         "+ an Armies/muster entry, a Train tap lands a real JobKind.TrainTroop job on ChannelId.Train, " +
                         "and Manage is still the SINGLE door (barracks talk-door closed, no forked enqueue)";
                Debug.Log("MANAGE_TRAIN_DOOR_OK\n" + log);
                return true;
            }
            reason = $"MANAGE TRAIN DOOR: {failures.Count} failure(s): " + string.Join(" | ", failures);
            Debug.LogError($"MANAGE_TRAIN_DOOR_FAIL: {failures.Count} failure(s)\n" + log +
                           "\n - " + string.Join("\n - ", failures));
            return false;
        }
    }
}

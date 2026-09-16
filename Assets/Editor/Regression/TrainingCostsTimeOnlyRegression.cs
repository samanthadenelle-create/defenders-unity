// =============================================================================
// TrainingCostsTimeOnlyRegression [training-costs-time-only] - WO-1387.
// Marker: TRAINING_COSTS_TIME_ONLY_OK / TRAINING_COSTS_TIME_ONLY_FAIL.
// -----------------------------------------------------------------------------
// Assembly: DeNelle.EditorRegression (editor-only). Register in DataRegression.RunAll.
// Contract mirrors the other Run(out reason) oracles (HireReinforcementsRegression,
// ManageTroopsTrainDoorRegression).
//
// THE RULING (owner 2026-09-04 23:14-23:16, Seeker, build 355905), verbatim:
//   "can you check the training. seems brutal almsot 4000 stone to upgrade, and gold?
//    Seems should be resources and the speed up is gold" -> "we agreed earlier training
//    free" -> "just time" -> "and gold is to hire mercenaries if they dont want to wait"
//    -> "the last CLI did bad changes"
//
// THE REVERSAL CHAIN this suite exists to END (WO-1387, KEY_FACTS 23:16 block):
//   WO-1372 said "FREE. Time only." -> commit 281902df0 re-read her as "gold is the
//   PRICE of a troop" (550 to train, CostGold*level to upgrade) -> tonight she saw it
//   on the device and reversed it. A third swing is the defect; this pin makes it RED.
//
// THE INTENT (owner 23:20): "start them with a free army to get them into raids".
//   StarterArmyGrant's 3 free Footmen -> the WO-823 first-raid soft gate opens on 3
//   deployable slots -> the first raid pays gold -> gold hires mercenaries when
//   impatient. Everything AFTER the free three costs only time, so a FRESH SAVE REACHES
//   THE RAID DOOR WITH ZERO GOLD SPENT. StarterArmyGrantRegression and
//   RaidFunnelRegression cover the first two arrows and are untouched by WO-1387; this
//   suite covers the third (train/upgrade are free) and the fourth (gold still skips).
//
// Cases (each proven RED first - the mutation is named on the case):
//   A  FREE TRAIN    - with Coins=0, Wood=0, Iron=0, Food=0, Crystals=0 a Train job
//                      enqueues, its duration is EXACTLY the troop's BuildSeconds, and
//                      the wallet is still all zero afterwards.
//                      RED: restore `if (!TrySpend(new ResourceCost(coins: def.CostGold)))`
//                      in BarracksService.EnqueueTraining -> returns 0 "Need more gold.".
//   B  FREE UPGRADE  - with the same zero wallet TroopUpgradeCost is EMPTY (all five
//                      fields 0), CanUpgradeTroop says yes, UpgradeTroop enqueues on the
//                      Research line, duration == TroopUpgradeSeconds.
//                      RED: restore `return new ResourceCost(coins: def.CostGold * m)` in
//                      BarracksProgression.TroopUpgradeCost -> the empty-basket pin fails;
//                      or re-add `if (!CanAfford(cost)) return false` to CanUpgradeTroop ->
//                      UpgradeTroop refuses on 0 coins.
//   C  GOLD SKIPS    - with Coins=100000 TryInstantFinish on the Train job from A still
//                      charges EXACTLY the quoted gold price and the troop lands.
//                      RED: `FinishPaysGold(JobKind kind) => false` -> priced in crystals,
//                      the fixture holds 0 crystals, the hire is refused.
//   E  FREE SWAP     - WO-1586. With the SAME zero wallet and a roster of owned troops,
//                      ArmyMusterService.Preview of a swap between OWNED kinds projects
//                      Cost.Gold == 0, Affordable == true and a ShortOf that never says
//                      "Gold"; owned/toTrain read the roster (E). A plan that outruns the
//                      roster is priced in TIME and still not in gold (E2). The instant-
//                      finish skip still quotes gold (E3) - it is the ONLY gold price left
//                      on the path.
//                      RED: restore `p.Cost.Gold += def.CostGold * row.Count` and
//                      `p.Affordable = state.Resources.Coins >= p.Cost.Gold` in
//                      ArmyMusterService.Preview -> E fails on gold=1650 and Affordable=false,
//                      which is exactly what the owner saw on 2026-09-07.
//   D  SHAPE         - BarracksService.cs, BarracksProgression.cs, ArmyMusterService.cs and
//                      ArmyMusterPanel.cs CODE never read
//                      CostGold. The value stays authored in troops.json as the reward
//                      anchor / hire basis (do not touch it) but no train/upgrade seam may
//                      price with it.
//                      RED: any restoration of the 281902df0 lines.
// =============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEngine;
using DeNelle.Core.Jobs;
using DeNelle.Core.State;
using DeNelle.Village;

namespace DeNelle.Editor
{
    public static class TrainingCostsTimeOnlyRegression
    {
        private const string ServicePath     = "Assets/_Modules/Village/Troops/BarracksService.cs";
        private const string ProgressionPath = "Assets/_Modules/Village/Troops/BarracksProgression.cs";
        private const string MusterPath      = "Assets/_Modules/Village/Troops/ArmyMusterService.cs";
        private const string MusterPanelPath = "Assets/_Modules/Village/Troops/ArmyMusterPanel.cs";
        private const string TroopId = "troop-footman";
        /// <summary>The second OWNED kind case E swaps to. Tier-1 like the footman, so both are
        /// unlocked at the fixture's Barracks level and neither depends on troop-upgrades.json tiers.</summary>
        private const string SwapTroopId = "troop-archer";

        public static bool Run(out string reason)
        {
            var failures = new List<string>();
            var log = new StringBuilder();
            log.AppendLine("=== TrainingCostsTimeOnlyRegression: training + troop upgrades cost TIME only; gold buys the skip (WO-1387) ===");

            try
            {
                RunLiveCases(failures, log);
                CheckShape(failures, log);
            }
            catch (Exception ex)
            {
                failures.Add($"TrainingCostsTimeOnlyRegression threw: {ex.GetType().Name}: {ex.Message}");
            }

            return Verdict(failures, log, out reason);
        }

        // -- CASES A-C: the LIVE seams, against real services and the real catalog --
        private static void RunLiveCases(List<string> failures, StringBuilder log)
        {
            string priorSave = PlayerPrefs.GetString(SaveSchema.PlayerPrefsKey, null);
            var priorGss = GameStateService.Instance;
            var priorQueue = BuildTimerService.Instance;

            GameObject gssGo = null, svcGo = null;
            GameState throwaway = null;
            try
            {
                throwaway = ScriptableObject.CreateInstance<GameState>();
                gssGo = new GameObject("GSS (training-costs-time-only oracle)");
                var gss = gssGo.AddComponent<GameStateService>();
                if (!InstallState(gss, throwaway))
                {
                    // NOT A SKIP: a suite that green-passes on an unreachable seam asserts nothing.
                    failures.Add("[fixture] GameStateService state seam is not reflectable, so the live cases " +
                                 "could not run. This is a FAIL, not a skip.");
                    return;
                }

                svcGo = new GameObject("BuildTimerService (training-costs-time-only oracle)");
                var svc = svcGo.AddComponent<BuildTimerService>();
                if (!InstallQueueInstance(svc))
                {
                    failures.Add("[fixture] BuildTimerService.Instance backing field is not reflectable - the " +
                                 "oracle cannot install the queue singleton. FAIL, not a skip.");
                    return;
                }

                // The completion effects are registered by a [RuntimeInitializeOnLoadMethod], which
                // does NOT run in edit mode. Invoke it so case C's roster grant is real.
                InvokeRuntimeInit(typeof(BarracksService), "RegisterEffects");

                var def = TroopCatalog.Find(TroopId);
                if (def == null)
                {
                    failures.Add($"[fixture] TroopCatalog.Find(\"{TroopId}\") is null - troops.json did not load, " +
                                 "so nothing below can be measured. FAIL, not a skip.");
                    return;
                }

                // (!) THE ACCEPTANCE FIXTURE: a founded town, a working barracks, and a wallet of
                // NOTHING AT ALL. Every gate that is not "time" must be wide open with zero held.
                throwaway.Onboarded = true;
                throwaway.BarracksLevel = 3;
                throwaway.Army = new ArmyStorage();
                throwaway.ObsidianQueue = ObsidianQueueState.Empty();
                throwaway.Wood = 0;
                throwaway.Iron = 0;
                var bal = throwaway.Resources;
                bal.Stone = 0;
                bal.Crystals = 0;
                bal.Coins = 0;
                throwaway.Resources = bal;

                // -- CASE A: train with nothing --------------------------------
                int enqueued = BarracksService.EnqueueTraining(TroopId, 1, out string stopReason);
                var trainJobs = new List<BuildJobData>();
                trainJobs.AddRange(svc.ActiveJobsOf(ChannelId.Train));
                trainJobs.AddRange(svc.PendingJobsOf(ChannelId.Train));
                var trainJob = trainJobs.Find(j => !string.IsNullOrEmpty(j.StructureId) &&
                                                   j.StructureId.StartsWith(BarracksService.TrainPrefix + TroopId + ":",
                                                                            StringComparison.Ordinal));
                log.AppendLine($"  case A - zero wallet: EnqueueTraining -> {enqueued} (reason=\"{stopReason}\"), " +
                               $"trainDepth={svc.QueueDepth(ChannelId.Train)} jobId={trainJob.StructureId} " +
                               $"durationMs={trainJob.DurationMs} authoredBuildSeconds={def.BuildSeconds}");

                if (enqueued != 1 || trainJob.StructureId == null)
                {
                    failures.Add($"[case A] with ZERO gold and ZERO resources, training one '{TroopId}' was refused " +
                                 $"(enqueued={enqueued}, reason=\"{stopReason}\"). Owner 2026-09-04 23:16: " +
                                 "\"training free ... just time\". A charge has been re-added to " +
                                 "BarracksService.EnqueueTraining - this is the 281902df0 reversal repeating.");
                }
                else
                {
                    double expectedMs = def.BuildSeconds * 1000.0;
                    if (Math.Abs(trainJob.DurationMs - expectedMs) > 1.0)
                        failures.Add($"[case A] the Train job's duration is {trainJob.DurationMs}ms, expected exactly " +
                                     $"{expectedMs}ms (TroopDef.BuildSeconds={def.BuildSeconds}). Time is the ONLY price, " +
                                     "so the time must be the authored one.");
                    if (trainJob.Kind != (int)JobKind.TrainTroop || trainJob.Channel != (int)ChannelId.Train)
                        failures.Add($"[case A] the free train landed as kind={trainJob.Kind} channel={trainJob.Channel}; " +
                                     $"expected JobKind.TrainTroop ({(int)JobKind.TrainTroop}) on ChannelId.Train " +
                                     $"({(int)ChannelId.Train}).");
                }
                var after = throwaway.Resources;
                if (after.Coins != 0 || throwaway.Wood != 0 || throwaway.Iron != 0 || after.Stone != 0 || after.Crystals != 0)
                    failures.Add($"[case A] the wallet MOVED on a free train: coins={after.Coins} wood={throwaway.Wood} " +
                                 $"iron={throwaway.Iron} food={after.Stone} crystals={after.Crystals}. Nothing may be debited.");

                // -- CASE B: upgrade with nothing ------------------------------
                int level = BarracksService.TroopLevel(TroopId);
                var upgradeCost = BarracksProgression.TroopUpgradeCost(TroopId, level + 1);
                float upgradeSeconds = BarracksProgression.TroopUpgradeSeconds(TroopId, level + 1);
                bool canUpgrade = BarracksService.CanUpgradeTroop(TroopId, out string upgradeReason);
                log.AppendLine($"  case B - TroopUpgradeCost(L{level + 1}) wood={upgradeCost.Wood} food={upgradeCost.Stone} " +
                               $"iron={upgradeCost.Iron} crystals={upgradeCost.Crystals} coins={upgradeCost.Coins}; " +
                               $"seconds={upgradeSeconds}; CanUpgradeTroop={canUpgrade} (\"{upgradeReason}\")");

                if (upgradeCost.Wood != 0 || upgradeCost.Stone != 0 || upgradeCost.Iron != 0 ||
                    upgradeCost.Crystals != 0 || upgradeCost.Coins != 0)
                    failures.Add("[case B] BarracksProgression.TroopUpgradeCost is NOT empty. Owner 2026-09-04 23:16: " +
                                 "\"just time\" - the only price of a troop upgrade is TroopUpgradeSeconds. The " +
                                 "`coins: CostGold * targetLevel` curve of commit 281902df0 has come back.");
                if (upgradeSeconds <= 0f)
                    failures.Add("[case B] TroopUpgradeSeconds is 0 - an upgrade that costs neither resources nor time " +
                                 "is not a progression step, it is a free stat button.");
                if (!BarracksProgression.HasNextTroopLevel(TroopId, level))
                    failures.Add($"[case B] '{TroopId}' has no level above L{level} in troop-upgrades.json, so the free " +
                                 "upgrade cannot be exercised. Fixture defect, FAIL not skip.");
                else if (!canUpgrade)
                    failures.Add($"[case B] CanUpgradeTroop refused with ZERO held: \"{upgradeReason}\". An " +
                                 "affordability test has been re-added; the upgrade's only gate besides unlock/max/" +
                                 "in-flight is the Research line's depth.");
                else
                {
                    bool upgraded = BarracksService.UpgradeTroop(TroopId);
                    var researchJobs = new List<BuildJobData>();
                    researchJobs.AddRange(svc.ActiveJobsOf(ChannelId.Research));
                    researchJobs.AddRange(svc.PendingJobsOf(ChannelId.Research));
                    var upgradeJob = researchJobs.Find(j => string.Equals(j.StructureId,
                        BarracksService.TroopUpgradePrefix + TroopId, StringComparison.Ordinal));
                    log.AppendLine($"  case B - UpgradeTroop -> {upgraded}; researchDepth={svc.QueueDepth(ChannelId.Research)} " +
                                   $"jobId={upgradeJob.StructureId} durationMs={upgradeJob.DurationMs}");
                    if (!upgraded || upgradeJob.StructureId == null)
                        failures.Add("[case B] UpgradeTroop with ZERO held did not land a TroopUpgrade job on the Research " +
                                     "line. Time is the only price; nothing else may refuse it.");
                    else if (Math.Abs(upgradeJob.DurationMs - upgradeSeconds * 1000.0) > 1.0)
                        failures.Add($"[case B] the upgrade job's duration is {upgradeJob.DurationMs}ms, expected " +
                                     $"{upgradeSeconds * 1000.0}ms (TroopUpgradeSeconds).");
                }
                var afterB = throwaway.Resources;
                if (afterB.Coins != 0 || throwaway.Wood != 0 || throwaway.Iron != 0 || afterB.Stone != 0 || afterB.Crystals != 0)
                    failures.Add($"[case B] the wallet MOVED on a free upgrade: coins={afterB.Coins} wood={throwaway.Wood} " +
                                 $"iron={throwaway.Iron} food={afterB.Stone} crystals={afterB.Crystals}.");

                // -- CASE E: REBALANCING AN ARMY YOU ALREADY OWN QUOTES NO GOLD (WO-1586) --
                // Owner, 2026-09-07 (Seeker 2026.09.07.359076): "i couldnt seem to rebalance my army
                // for the raids. Now that I upgraded troops, I should be able to change out troops but
                // everytime showed as need gold. But we agreed the one need for gold was if you didnt
                // want to wait on troops to train". The train SEAM was already free (case A); the
                // Armies panel's PROJECTION was not. Runs with the wallet still at ZERO - case C
                // funds it afterwards, so the order of these two blocks matters.
                throwaway.Army.GrantTrained(TroopId);
                throwaway.Army.GrantTrained(TroopId);
                throwaway.Army.GrantTrained(SwapTroopId);

                var swap = new ArmyComposition { Name = "Rebalance" };
                swap.Add(TroopId, 2);
                swap.Add(SwapTroopId, 1);
                var swapPreview = ArmyMusterService.Preview(swap);
                int coinsAtPreview = throwaway.Resources.Coins;
                log.AppendLine($"  case E - swap between OWNED kinds with coins={coinsAtPreview}: " +
                               $"units={swapPreview.TotalUnits} cost=\"{swapPreview.Cost}\" gold={swapPreview.Cost.Gold} " +
                               $"affordable={swapPreview.Affordable} shortOf=\"{swapPreview.ShortOf}\" " +
                               $"owned={swapPreview.AlreadyOwned} toTrain={swapPreview.ToTrain} " +
                               $"planSlots={swapPreview.PlanSlots} newArmySlots={swapPreview.NewArmySlots} " +
                               $"armyRoom={swapPreview.ArmyRoom} lineRoom={swapPreview.LineRoom}");

                if (coinsAtPreview != 0)
                    failures.Add($"[case E] fixture defect: the wallet holds {coinsAtPreview} coins, so a " +
                                 "zero-gold projection would prove nothing. Case E must run before case C funds it.");
                if (swapPreview.Cost.Gold != 0 || !swapPreview.Cost.IsZero)
                    failures.Add($"[case E] ArmyMusterService.Preview priced a swap between troops the player " +
                                 $"ALREADY OWNS at {swapPreview.Cost.Gold} gold (\"{swapPreview.Cost}\"). Training has " +
                                 "charged nothing since WO-1387; a projection that quotes gold is the panel telling " +
                                 "the player she cannot afford something the action takes for free. The " +
                                 "`p.Cost.Gold += def.CostGold * row.Count` line has come back.");
                if (!swapPreview.Affordable)
                    failures.Add($"[case E] the swap is NOT Affordable with {coinsAtPreview} coins " +
                                 $"(shortOf=\"{swapPreview.ShortOf}\", armyRoom={swapPreview.ArmyRoom}, " +
                                 $"lineRoom={swapPreview.LineRoom}). Affordable means \"fits the army cap and the " +
                                 "train line\" (WO-1586), never \"coins >= Cost.Gold\".");
                if (swapPreview.ShortOf.IndexOf("Gold", StringComparison.OrdinalIgnoreCase) >= 0)
                    failures.Add($"[case E] the panel's SHORT OF chip would read \"{swapPreview.ShortOf}\" - a " +
                                 "train-side surface may never name gold. Gold belongs to the skip verb alone.");
                if (swapPreview.AlreadyOwned != 3 || swapPreview.ToTrain != 0)
                    failures.Add($"[case E] the projection reads owned={swapPreview.AlreadyOwned} " +
                                 $"toTrain={swapPreview.ToTrain} for a plan of 2x{TroopId} + 1x{SwapTroopId} against a " +
                                 "roster holding exactly those three. Expected owned=3 toTrain=0 - the owned-vs-train " +
                                 "reading is what a capture uses to tell a rebalance from a new training order.");
                // NewArmySlots is the owned-vs-train READING, not the cap input (the cap is judged on
                // PlanSlots, because Muster enqueues every staged unit). Pinned so the reading itself
                // stays honest - a capture uses it to tell a rebalance from a recruitment drive.
                if (swapPreview.NewArmySlots != 0)
                    failures.Add($"[case E] a swap between OWNED kinds reads {swapPreview.NewArmySlots} NEW army " +
                                 "slots; troops already on the roster add none. The owned-vs-train reading is wrong, " +
                                 "so a capture can no longer tell a rebalance from a recruitment drive.");

                // E2: a plan that OUTRUNS the roster is priced in TIME, still never in gold.
                var grow = new ArmyComposition { Name = "Grow" };
                grow.Add(SwapTroopId, 5);
                var growPreview = ArmyMusterService.Preview(grow);
                log.AppendLine($"  case E2 - 5x{SwapTroopId} with 1 owned: gold={growPreview.Cost.Gold} " +
                               $"owned={growPreview.AlreadyOwned} toTrain={growPreview.ToTrain} " +
                               $"seconds={growPreview.TotalSeconds} affordable={growPreview.Affordable}");
                if (growPreview.Cost.Gold != 0)
                    failures.Add($"[case E2] training NEW troops was priced at {growPreview.Cost.Gold} gold. " +
                                 "Owner: \"the one need for gold was if you didnt want to wait\".");
                if (growPreview.TotalSeconds <= 0d)
                    failures.Add("[case E2] a plan that must TRAIN four new troops projects 0 seconds - with gold " +
                                 "gone, TIME is the only price left and it must be quoted.");
                if (growPreview.ToTrain != 4)
                    failures.Add($"[case E2] toTrain={growPreview.ToTrain} for 5 staged against 1 owned; expected 4.");

                // E3: the ONE gold price on this whole path is the SKIP. Measured here so the pin
                // says "gold moved to the skip verb", not merely "gold left the projection".
                if (trainJob.StructureId != null)
                {
                    int skipPrice = svc.InstantFinishPrice(ChannelId.Train, trainJob.StructureId);
                    log.AppendLine($"  case E3 - skip price on the free Train job = {skipPrice} gold " +
                                   $"(paysGold={svc.FinishPaysGold(ChannelId.Train, trainJob.StructureId)})");
                    if (skipPrice <= 0)
                        failures.Add("[case E3] with training free AND the projection free, the instant-finish skip " +
                                     "quotes 0 gold - the ruling's single gold sink has vanished entirely.");
                }

                // -- CASE C: gold still buys the skip, on the SAME free Train job --
                if (trainJob.StructureId != null)
                {
                    var rich = throwaway.Resources;
                    rich.Coins = 100000;
                    rich.Crystals = 0;         // broke on crystals: the skip must be priced in GOLD
                    throwaway.Resources = rich;

                    int price = svc.InstantFinishPrice(ChannelId.Train, trainJob.StructureId);
                    bool paysGold = svc.FinishPaysGold(ChannelId.Train, trainJob.StructureId);
                    int rosterBefore = throwaway.Army.CountOfDef(TroopId);
                    int coinsBefore = throwaway.Resources.Coins;
                    bool skipped = svc.TryInstantFinish(ChannelId.Train, trainJob.StructureId, out string skipFailure);
                    int coinsAfter = throwaway.Resources.Coins;
                    int rosterAfter = throwaway.Army.CountOfDef(TroopId);
                    log.AppendLine($"  case C - price={price} paysGold={paysGold} skip={skipped} reason=\"{skipFailure}\" " +
                                   $"coins {coinsBefore}->{coinsAfter} roster {rosterBefore}->{rosterAfter} " +
                                   $"trainDepth={svc.QueueDepth(ChannelId.Train)}");

                    if (!paysGold)
                        failures.Add("[case C] the free Train job does NOT report FinishPaysGold - the skip is priced in " +
                                     "crystals. Owner: \"gold is to hire mercenaries if they dont want to wait\".");
                    if (price <= 0)
                        failures.Add("[case C] the Train job's instant-finish price is 0 - with training free, the skip " +
                                     "is the ONLY gold sink, and a 0 price also hides the HIRE REINFORCEMENTS face.");
                    if (!skipped)
                        failures.Add($"[case C] TryInstantFinish on the free Train job FAILED with {coinsBefore} gold: " +
                                     $"\"{skipFailure}\". Making training free must not break the gold skip.");
                    else
                    {
                        if (coinsBefore - coinsAfter != price)
                            failures.Add($"[case C] Coins moved by {coinsBefore - coinsAfter}, expected exactly {price}. " +
                                         "The one gold spend must charge the quoted number.");
                        if (throwaway.Resources.Crystals != 0)
                            failures.Add("[case C] Crystals moved on a gold hire - the wrong wallet was charged.");
                        if (rosterAfter <= rosterBefore)
                            failures.Add($"[case C] the skip completed but the roster did not grow ({rosterBefore} -> " +
                                         $"{rosterAfter}). A hire that lands no troop is a purchase of nothing.");
                    }
                }
                else
                {
                    failures.Add("[case C] no free Train job existed to skip (case A failed), so the gold skip is unproven.");
                }
            }
            finally
            {
                if (svcGo != null) UnityEngine.Object.DestroyImmediate(svcGo);
                if (gssGo != null) UnityEngine.Object.DestroyImmediate(gssGo);
                if (throwaway != null) UnityEngine.Object.DestroyImmediate(throwaway);
                RestoreQueueInstance(priorQueue);
                RestoreGssInstance(priorGss);
                // The live skip calls GameStateService.Save(), which writes the editor save slot.
                // Put the developer's save back exactly as it was.
                if (priorSave != null) PlayerPrefs.SetString(SaveSchema.PlayerPrefsKey, priorSave);
                else PlayerPrefs.DeleteKey(SaveSchema.PlayerPrefsKey);
                PlayerPrefs.Save();
            }
        }

        // -- CASE D: shape - the train/upgrade seams never price with CostGold ---
        private static void CheckShape(List<string> failures, StringBuilder log)
        {
            // WO-1586 added the two MUSTER files: the train SEAM was already clean, but the Armies
            // panel's PROJECTION (ArmyMusterService.Preview) and its roster rows (ArmyMusterPanel.
            // PerUnitLine) still read CostGold and put "550 Gold" / "SHORT OF: Gold" in front of a
            // player rebalancing troops she owned. A ruling that holds in the service and breaks in
            // the panel is not a ruling that holds.
            foreach (var path in new[] { ServicePath, ProgressionPath, MusterPath, MusterPanelPath })
            {
                string src = ReadSource(path, failures);
                if (src == null) continue;
                string code = StripLineComments(src);
                if (code.IndexOf("CostGold", StringComparison.Ordinal) >= 0)
                    failures.Add($"[case D] {path} CODE reads CostGold again. troops.json costGold is the raid-reward " +
                                 "anchor and the mercenary-hire basis, never a train or upgrade price (WO-1387) and " +
                                 "never a muster projection or a roster row (WO-1586). " +
                                 "Comments may discuss it; live code may not price with it.");
                if (path == ServicePath && code.IndexOf("Resources.Coins -=", StringComparison.Ordinal) >= 0)
                    failures.Add("[case D] BarracksService debits Coins again. The ONE coins debit is " +
                                 "BuildTimerService.SpendCoins behind TryInstantFinish (HireReinforcementsRegression 5a).");
            }
            if (!failures.Exists(f => f.StartsWith("[case D]", StringComparison.Ordinal)))
                log.AppendLine("  case D OK - BarracksService / BarracksProgression / ArmyMusterService / " +
                               "ArmyMusterPanel code never prices with CostGold");
        }

        // -- helpers (same shape as HireReinforcementsRegression) ---------------

        private static bool InstallState(GameStateService svc, GameState state)
        {
            var f = typeof(GameStateService).GetField("_state", BindingFlags.NonPublic | BindingFlags.Instance);
            if (f == null) return false;
            f.SetValue(svc, state);
            var i = typeof(GameStateService).GetField("_instance", BindingFlags.NonPublic | BindingFlags.Static);
            if (i == null) return false;
            i.SetValue(null, svc);
            return true;
        }

        private static void RestoreGssInstance(GameStateService prior)
        {
            var i = typeof(GameStateService).GetField("_instance", BindingFlags.NonPublic | BindingFlags.Static);
            if (i != null) i.SetValue(null, prior);
        }

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

        private static void RestoreQueueInstance(BuildTimerService prior)
        {
            var t = typeof(BuildTimerService);
            var prop = t.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static);
            if (prop != null && prop.GetSetMethod(true) != null)
            {
                prop.GetSetMethod(true).Invoke(null, new object[] { prior });
                return;
            }
            var f = t.GetField("<Instance>k__BackingField", BindingFlags.NonPublic | BindingFlags.Static)
                    ?? t.GetField("_instance", BindingFlags.NonPublic | BindingFlags.Static);
            if (f != null) f.SetValue(null, prior);
        }

        /// <summary>Invokes a private [RuntimeInitializeOnLoadMethod] that edit mode never fires.</summary>
        private static void InvokeRuntimeInit(Type type, string method)
        {
            var m = type.GetMethod(method, BindingFlags.NonPublic | BindingFlags.Static);
            if (m != null) m.Invoke(null, null);
        }

        /// <summary>Drops whole-line // and /// comments so a source oracle matches CODE, not prose.</summary>
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

        /// <summary>Repo-relative read. The repo ROOT is machine-dependent (CLAUDE.md s0), so it is
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
                reason = "TRAINING COSTS TIME ONLY OK - with a wallet of nothing a Train job and a troop upgrade both " +
                         "enqueue for exactly their authored seconds, TroopUpgradeCost is empty, the wallet never " +
                         "moves, gold still buys the instant-finish skip for exactly the quoted price, a swap " +
                         "between OWNED troops projects zero gold and stays Affordable on an empty wallet, and no " +
                         "train/upgrade/muster seam prices with CostGold";
                Debug.Log("TRAINING_COSTS_TIME_ONLY_OK\n" + log);
                return true;
            }
            reason = $"TRAINING COSTS TIME ONLY: {failures.Count} failure(s): " + string.Join(" | ", failures);
            Debug.LogError($"TRAINING_COSTS_TIME_ONLY_FAIL: {failures.Count} failure(s)\n" + log +
                           "\n - " + string.Join("\n - ", failures));
            return false;
        }
    }
}

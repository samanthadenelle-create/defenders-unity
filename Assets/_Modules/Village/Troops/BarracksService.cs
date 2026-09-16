// =============================================================================
// BarracksService — the LIVE facade for the WO-771.9 Barracks & troop-upgrade
// progression (integration half), riding the committed WO-773 Obsidian queue.
// -----------------------------------------------------------------------------
// Assembly: DeNelle.Village   Namespace: DeNelle.Village
//
// The player-facing API the deploy/training gates call. Reads the live GameState
// (BuildingTiers["barracks"] / TroopLevels) + EconomyService (afford/spend) and
// ENQUEUES timed jobs on the common Obsidian queue (BuildTimerService), NEVER a
// private timer:
//
// ⛔ OWNER RULING 21 (2026-09-06) — THE TROOP GATE READS THE BARRACKS BUILDING TIER.
// BarracksLevel below is now BarracksProgression.EffectiveBarracksLevelOf, i.e.
// MAX(the legacy stored GameState.BarracksLevel, BuildingTiers["barracks"]) floored to 1.
// Before the merge this facade compared troops against the stored field ALONE while
// TroopUnlock.IsTrainable already took the MAX — a SPLIT-BRAIN gate. The Manage ARMY tab
// requires BOTH (ManageScreenVM.cs:1892-1893, `unlocked && trainable`), so the stricter,
// permanently-1 half dominated and seven of nine troops were unreachable. One authority now.
// The BarracksPanel this file's header used to name had ZERO CALLERS and was DELETED on
// 2026-09-06 (WO-1430 Group A) - do not go looking for it. The BarracksUpgrade job path
// below is likewise dead (see BarracksProgression.ApplyBarracksUpgrade), kept only so a
// pre-existing queued job still completes.
//   • Barracks upgrade  -> JobKind.BarracksUpgrade on the BUILDER channel.
//   • Troop track upgrade -> JobKind.TroopUpgrade   on the RESEARCH channel.
//   • Troop training      -> JobKind.TrainTroop     on the TRAIN channel.
// The COMPLETION effect for each is an IJobEffect (below), registered once at
// startup with the shared JobEffectRegistry. The pure decision + mutation logic
// lives in BarracksProgression (headlessly testable); this class only wires the
// live singletons to it.
//
// One job per structure-id per channel (BuildTimerService enforces), so exactly one
// barracks upgrade and one upgrade-per-troop can be in flight at a time (CoC parity);
// training mints a UNIQUE id per unit so many can queue in parallel on the Train channel.
// =============================================================================

using System;
using UnityEngine;
using DeNelle.Core.Jobs;
using DeNelle.Core.State;
using DeNelle.Core.Diagnostics;
using DeNelle.Core.Tutorial;   // WO-1389: TutorialSignals.TroopJobQueued / TroopLineBusy at the enqueue success points
using Ledger = DeNelle.Village.Buildings.Progression;

namespace DeNelle.Village
{
    /// <summary>
    /// Live facade over <see cref="BarracksProgression"/> + the Obsidian queue for the
    /// WO-771.9 progression: barracks-level upgrades, per-troop track upgrades and timed
    /// training, each enqueued on its channel and applied by a registered IJobEffect.
    /// </summary>
    public static class BarracksService
    {
        /// <summary>Obsidian job id of the (single) in-flight barracks-level upgrade.</summary>
        public const string BarracksJobId = "barracks-upgrade";
        /// <summary>Prefix for a per-troop upgrade job id: <c>barracks-troop-upgrade:&lt;troopId&gt;</c>.</summary>
        public const string TroopUpgradePrefix = "barracks-troop-upgrade:";
        /// <summary>Prefix for a training job id: <c>barracks-train:&lt;troopId&gt;:&lt;uid&gt;</c>.</summary>
        public const string TrainPrefix = "barracks-train:";

        /// <summary>Raised whenever a level changes (upgrade start OR a job completed) so the UI refreshes.</summary>
        public static event Action Changed;

        // ── Live state reads ──────────────────────────────────────────────────

        private static GameState State =>
            GameStateService.Instance != null ? GameStateService.Instance.State : null;

        /// <summary>
        /// The player's current barracks level for UNLOCK MATH - the barracks BUILDING TIER,
        /// floored by the legacy stored field and by 1 (owner ruling 21, 2026-09-06:
        /// <i>"Merge them - the building tier gates troops."</i>).
        ///
        /// <para>This one line is the merge. It used to read
        /// <c>BarracksProgression.BarracksLevelOf(State)</c> - the stored <c>GameState.BarracksLevel</c>
        /// alone - which no reachable code path could raise, so <see cref="IsTroopUnlocked"/> and both
        /// train gates below (the unlock refusals in <c>CanUpgradeTroop</c> and
        /// <c>EnqueueTraining</c>) said no to seven of the nine troops forever. Now it reads
        /// <see cref="BarracksProgression.EffectiveBarracksLevelOf"/>; the stored key is
        /// read-migrated, never deleted (CLAUDE.md §8) - the migration rule is documented there.</para>
        /// </summary>
        public static int BarracksLevel => BarracksProgression.EffectiveBarracksLevelOf(State);

        /// <summary>A troop's current upgrade level (1 = baseline; 1 with no live state).</summary>
        public static int TroopLevel(string troopId) => BarracksProgression.TroopLevelOf(State, troopId);

        /// <summary>True when <paramref name="troopId"/> is unlocked at the current barracks level.</summary>
        public static bool IsTroopUnlocked(string troopId) =>
            BarracksProgression.IsTroopUnlocked(troopId, BarracksLevel);

        /// <summary>True while a barracks-level upgrade is running/queued on the Builder channel.</summary>
        public static bool IsUpgradingBarracks =>
            IsInFlight(ChannelId.Builder, BarracksJobId);

        /// <summary>True while an upgrade for <paramref name="troopId"/> is running/queued on the Research channel.</summary>
        public static bool IsUpgradingTroop(string troopId) =>
            IsInFlight(ChannelId.Research, TroopUpgradePrefix + troopId);

        // ── Wallet (F8 2026-07-30 "barracks upgrade fails WITH resources") ────
        // Afford/spend against ResourceLedger — the GameState-backed single-source
        // wallet the HUD shows — NEVER EconomyService's divergent in-session
        // Wood/Iron pool (defaults 200/80, reset on every scene load; nothing
        // mirrors GameState back into it). Same migration BuildingUpgradeService
        // already did for city tiers (see its spend comment); this facade had been
        // left on the old pool, so from Barracks L3 (320 wood > the 200-wood pool)
        // every upgrade was ARITHMETICALLY unaffordable no matter what the player
        // owned — and the refusal mis-blamed "Not enough resources".

        /// <summary>The economy cost as GameState-ledger lines (Wood/Food/Iron/Crystals; barracks data never charges Coins).</summary>
        private static System.Collections.Generic.List<Ledger.ResourceCost> LedgerCost(ResourceCost cost)
        {
            var list = new System.Collections.Generic.List<Ledger.ResourceCost>(4);
            if (cost.Wood > 0)     list.Add(new Ledger.ResourceCost(Ledger.HarvestResource.Wood, cost.Wood));
            if (cost.Stone > 0)     list.Add(new Ledger.ResourceCost(Ledger.HarvestResource.Stone, cost.Stone));
            if (cost.Iron > 0)     list.Add(new Ledger.ResourceCost(Ledger.HarvestResource.Iron, cost.Iron));
            if (cost.Crystals > 0) list.Add(new Ledger.ResourceCost(Ledger.HarvestResource.Crystals, cost.Crystals));
            if (cost.Coins > 0)    FlowTrace.Warn("Barracks", "cost carries Coins — not ledger-charged (barracks data should never price in Coins).");
            return list;
        }

        private static bool CanAfford(ResourceCost cost) => State != null &&
            State.Resources.Coins >= cost.Coins && Ledger.ResourceLedger.CanAfford(LedgerCost(cost));

        // WO-1387 (2026-09-04 23:16): the private TrySpend/Refund pair that debited GameState
        // Coins for a TRAIN unit is DELETED, not disabled. Training and troop upgrades charge
        // nothing (owner: "training free ... just time"); the one coins debit in the game is
        // BuildTimerService.SpendCoins behind TryInstantFinish (gold hires mercenaries = skips
        // the clock). Re-adding a coins debit here is the reversal chain on WO-1387 repeating.

        /// <summary>
        /// WO-911 — hand a charged basket straight back when the ENQUEUE that followed it is
        /// refused (the depth cap of ruling Q4 can refuse AFTER the spend has landed). Credits the
        /// same GameState ledger the spend debited, uncapped, so the refund cannot evaporate against
        /// the town bank cap. Without this a full line would silently eat the player's resources.
        /// </summary>
        private static void RefundLedgerCost(System.Collections.Generic.List<Ledger.ResourceCost> cost)
        {
            if (cost == null) return;
            for (int i = 0; i < cost.Count; i++)
                Ledger.ResourceLedger.Credit(cost[i].Resource, cost[i].Amount);
        }

        /// <summary>Ledger affordability of the next barracks level — the panel's cost-row tint (same wallet the spend charges).</summary>
        public static bool CanAffordBarracksUpgrade(int currentLevel) =>
            CanAfford(BarracksProgression.BarracksUpgradeCost(currentLevel));

        /// <summary>Ledger affordability of a troop's next level — the panel's cost-row tint (same wallet the spend charges).</summary>
        public static bool CanAffordTroopUpgrade(string troopId, int nextLevel) =>
            CanAfford(BarracksProgression.TroopUpgradeCost(troopId, nextLevel));

        /// <summary>Names the short resources so the block reason tells the player WHAT is missing.</summary>
        private static string MissingOf(System.Collections.Generic.List<Ledger.ResourceCost> lines)
        {
            var sb = new System.Text.StringBuilder();
            foreach (var line in lines)
                if (Ledger.ResourceLedger.Balance(line.Resource) < line.Amount)
                    sb.Append(sb.Length > 0 ? ", " : "").Append(line.Resource);
            return sb.Length > 0 ? "Need more " + sb + "." : "Not enough resources.";
        }

        // ── Barracks upgrade ──────────────────────────────────────────────────

        /// <summary>
        /// True when the barracks can be upgraded right now: a higher level exists, one is not
        /// already in flight, and the player can afford the next level's cost. <paramref name="reason"/>
        /// carries the player-facing block reason otherwise.
        /// </summary>
        public static bool CanUpgradeBarracks(out string reason)
        {
            reason = null;
            if (State == null) { reason = "No game state."; return false; }

            int level = BarracksLevel;
            if (!BarracksProgression.HasNextBarracksLevel(level)) { reason = "Barracks is at max level."; return false; }
            if (IsUpgradingBarracks) { reason = "Barracks upgrade already in progress."; return false; }

            var cost = LedgerCost(BarracksProgression.BarracksUpgradeCost(level));
            if (!Ledger.ResourceLedger.CanAfford(cost)) { reason = MissingOf(cost); return false; }
            return true;
        }

        /// <summary>
        /// Spends the next barracks level's cost and ENQUEUES a BarracksUpgrade job on the Builder
        /// channel (the level applies at completion via <see cref="BarracksUpgradeEffect"/>). Returns
        /// false (no spend, no enqueue) when <see cref="CanUpgradeBarracks"/> refuses or the spend fails.
        /// </summary>
        public static bool UpgradeBarracks()
        {
            if (!CanUpgradeBarracks(out string reason))
            {
                FlowTrace.Warn("Barracks", "UpgradeBarracks refused: " + reason);
                return false;
            }

            int level = BarracksLevel;
            var cost = LedgerCost(BarracksProgression.BarracksUpgradeCost(level));
            float seconds = BarracksProgression.BarracksUpgradeSeconds(level);

            // Queue check BEFORE the spend — the old order committed the charge and THEN
            // returned on a null queue, losing the player's resources (charge-loss window).
            var queue = BuildTimerService.Instance;
            if (queue == null) { FlowTrace.Warn("Barracks", "UpgradeBarracks: no BuildTimerService (nothing charged)."); return false; }

            if (!Ledger.ResourceLedger.TrySpend(cost)) { FlowTrace.Warn("Barracks", "UpgradeBarracks spend failed."); return false; }
            // WO-911 (M2): the charged basket rides the job for the 100%-flat cancel refund (Q1).
            // (M1): the Builder line can now REFUSE at its depth cap — the charge already landed,
            // so a refusal must give the resources straight back rather than eat them.
            if (queue.Enqueue(JobKind.BarracksUpgrade, BarracksJobId, seconds, level + 1,
                              BuildTimerService.ToJobCost(cost)) == null)
            {
                RefundLedgerCost(cost);
                FlowTrace.Warn("Barracks",
                    "UpgradeBarracks enqueue refused (" + (queue.LastEnqueueFailure ?? "unknown") + ") — charge refunded.");
                return false;
            }

            FlowTrace.Step("Barracks", $"barracks upgrade L{level}->L{level + 1} enqueued (Builder, {seconds:0}s).");
            Changed?.Invoke();
            return true;
        }

        // ── Troop track upgrade ───────────────────────────────────────────────

        /// <summary>
        /// True when <paramref name="troopId"/> can be upgraded now: unlocked, below its max
        /// level and not already upgrading. <paramref name="reason"/> carries the block.
        /// WO-1387 (owner 2026-09-04 23:16, "just time"): there is NO affordability test - a troop
        /// upgrade costs wall-clock time on the Research line and nothing else.
        /// </summary>
        public static bool CanUpgradeTroop(string troopId, out string reason)
        {
            reason = null;
            if (State == null) { reason = "No game state."; return false; }
            if (string.IsNullOrEmpty(troopId)) { reason = "Unknown troop."; return false; }

            if (!IsTroopUnlocked(troopId)) { reason = "Unlocks at Barracks Level " + BarracksProgression.UnlockLevelFor(troopId) + "."; return false; }

            int level = TroopLevel(troopId);
            if (!BarracksProgression.HasNextTroopLevel(troopId, level)) { reason = "Troop is at max level."; return false; }
            if (IsUpgradingTroop(troopId)) { reason = "Upgrade already in progress."; return false; }
            return true;
        }

        /// <summary>
        /// ENQUEUES a TroopUpgrade job on the Research channel (the level applies at completion via
        /// <see cref="TroopUpgradeEffect"/>). Charges NOTHING (WO-1387): the price is
        /// <see cref="BarracksProgression.TroopUpgradeSeconds"/>. Returns false (no enqueue) when
        /// <see cref="CanUpgradeTroop"/> refuses or the Research line is at its depth cap.
        /// </summary>
        public static bool UpgradeTroop(string troopId)
        {
            if (!CanUpgradeTroop(troopId, out string reason))
            {
                FlowTrace.Warn("Barracks", "UpgradeTroop refused (" + troopId + "): " + reason);
                return false;
            }

            int level = TroopLevel(troopId);
            float seconds = BarracksProgression.TroopUpgradeSeconds(troopId, level + 1);

            var queue = BuildTimerService.Instance;
            if (queue == null) { FlowTrace.Warn("Barracks", "UpgradeTroop: no BuildTimerService."); return false; }

            // JobKind.TroopUpgrade's default channel is Research (JobChannels) — the single-arg overload routes it there.
            // WO-1387: no basket rides the job because nothing was paid; a cancel refunds zero, honestly.
            // (M1) a full Research line still refuses at its depth cap.
            if (queue.Enqueue(JobKind.TroopUpgrade, TroopUpgradePrefix + troopId, seconds, level + 1) == null)
            {
                FlowTrace.Warn("Barracks",
                    "UpgradeTroop enqueue refused (" + (queue.LastEnqueueFailure ?? "unknown") + ").");
                return false;
            }

            FlowTrace.Step("Barracks", $"troop upgrade '{troopId}' L{level}->L{level + 1} enqueued (Research, {seconds:0}s).");
            Changed?.Invoke();
            RaiseJobQueuedSignals(troopId);
            return true;
        }

        /// <summary>
        /// WO-1389 - the tutorial bus ids for "a troop job actually landed", raised at the TWO
        /// success points every train/upgrade path funnels through (this and EnqueueTraining).
        /// ORDER IS LOAD-BEARING: <see cref="TutorialSignals.TroopJobQueued"/> completes the
        /// post-raid HOW beat, and TutorialFlow.OnSignal RETURNS after completing a beat - so the
        /// TRAINING NOW coach-mark (its own contextual beat) is triggered by the SEPARATE
        /// <see cref="TutorialSignals.TroopLineBusy"/> raise that follows, by which time the first
        /// beat has already closed. Swapping the order would refuse the second beat forever.
        /// Guarded: bus subscribers must never be able to unwind a job that was already enqueued.
        /// </summary>
        private static void RaiseJobQueuedSignals(string troopId)
        {
            DeNelle.Core.Diagnostics.Guard.Try("Barracks", "raise troop job-queued signals", () =>
            {
                TutorialSignals.Raise(TutorialSignals.TroopJobQueued);
                if (!string.IsNullOrEmpty(troopId))
                    TutorialSignals.Raise(TutorialSignals.TroopJobQueuedPrefix + troopId);
                TutorialSignals.Raise(TutorialSignals.TroopLineBusy);
            });
        }

        // ── Timed training (Train channel; WO-771.9 §2 / WO-771.8) ─────────────

        /// <summary>
        /// COC-parity TIMED training: for each of <paramref name="qty"/> units, checks the troop is
        /// unlocked + there is army room, and ENQUEUES a TrainTroop job on the Train channel (the
        /// unit lands in the army at completion via <see cref="TrainTroopEffect"/> - NOT instantly,
        /// NO private timer). Stops early when a gate fails (that unit is not enqueued). Returns
        /// the number enqueued.
        /// WO-1387 (owner 2026-09-04 23:16): training CHARGES NOTHING - no coins, no resources.
        /// "we agreed earlier training free ... just time ... and gold is to hire mercenaries if
        /// they dont want to wait". The price is TroopDef.BuildSeconds on the Train line; gold is
        /// spent only by BuildTimerService.TryInstantFinish to skip it. TroopDef.CostGold is NOT
        /// read here (it stays authored as the raid-reward anchor / hire basis).
        /// This is the sanctioned timed path; the existing instant TroopTrainingVM/DialogueCommands
        /// path is untouched (its felt-behaviour flip to this queue is WO-771.8 / PO-gated).
        /// </summary>
        public static int EnqueueTraining(string troopId, int qty) => EnqueueTraining(troopId, qty, out _);

        /// <summary>
        /// WO-897 — the SAME timed-training path as <see cref="EnqueueTraining(string,int)"/>, additionally
        /// reporting WHY it stopped short. <paramref name="stopReason"/> is null when all
        /// <paramref name="qty"/> units were enqueued, and otherwise carries an ASCII player-facing
        /// sentence naming the blocker ("Army is full.", "Need more Wood, Iron.", ...).
        ///
        /// This overload exists so the army-muster batch (WO-897) can tell the player exactly what did
        /// NOT fit instead of silently dropping the remainder — a silent truncation is forbidden
        /// (CLAUDE.md §12 "no silent failures"). It is the ONE enqueue path; the muster does not fork it.
        /// </summary>
        public static int EnqueueTraining(string troopId, int qty, out string stopReason)
        {
            stopReason = null;
            if (string.IsNullOrEmpty(troopId) || qty <= 0) { stopReason = "Unknown troop."; return 0; }
            var state = State;
            if (state == null || state.Army == null) { stopReason = "No game state."; return 0; }
            if (!IsTroopUnlocked(troopId))
            {
                stopReason = "Locked - unlocks at Barracks Level " + BarracksProgression.UnlockLevelFor(troopId) + ".";
                return 0;
            }

            var def = TroopCatalog.Find(troopId);
            if (def == null) { stopReason = "Unknown troop."; return 0; }

            var queue = BuildTimerService.Instance;
            if (queue == null) { stopReason = "Training queue is not running."; return 0; }

            // WO-1387: no cost is built and nothing is charged. The old lines here priced the
            // unit at `new ResourceCost(coins: def.CostGold)` and TrySpend'd it per unit - deleted.

            // OVER-QUEUE FIX (full-army gate lane): the old per-unit army.CanTrain check read
            // the LIVE roster only — in-flight Train jobs were invisible to it, so 20+ units
            // could be enqueued against cap 10 and the roster overflowed at completion (the
            // grant is unconditional by design, ArmyStorage.GrantTrained). Refuse any unit
            // that would push roster + committed past the cap. The SEED numbers (roster incl.
            // wounded, committed Train slots, cap) come from ArmyReadiness.Compute — the ONE
            // readiness formula (owner review 2026-08-01); only the per-unit growth of
            // `committed` inside the loop stays local.
            var readiness = ArmyReadiness.Compute(state);
            int unitSlots = TroopDialogueCommands.SlotOf(troopId);
            int rosterSlots = readiness.RosterSlots;
            int committedBefore = readiness.QueuedSlots;
            int committed = committedBefore;
            int cap = readiness.CapSlots;

            // WO-933: per-type ownership cap (CoC scarcity). maxOwned 0 = unlimited.
            // Counts roster (incl. wounded) + in-flight Train jobs of this def — wounded
            // still blocks a second train until recovery (preferred product ruling).
            int maxOwned = def.MaxOwned;
            int ownedOfType = state.Army.CountOfDef(troopId);
            int inFlightOfType = CountInFlightTrainOf(troopId);

            int enqueued = 0;
            for (int i = 0; i < qty; i++)
            {
                if (maxOwned > 0 && ownedOfType + inFlightOfType + enqueued >= maxOwned)
                {
                    string name = string.IsNullOrEmpty(def.DisplayName) ? troopId : def.DisplayName;
                    stopReason = maxOwned == 1
                        ? "Only one " + name + " may be owned at a time."
                        : "Owned limit reached for " + name + ".";
                    FlowTrace.Step("Barracks",
                        $"train refused maxOwned id={troopId} owned={ownedOfType} " +
                        $"inFlight={inFlightOfType} enqueued={enqueued} max={maxOwned}.");
                    break;
                }
                if (rosterSlots + committed + unitSlots > cap)
                {
                    stopReason = "Army is full.";                       // cap full (incl. in-flight jobs)
                    break;
                }
                string jobId = TrainPrefix + troopId + ":" + Guid.NewGuid().ToString("N").Substring(0, 8);
                // WO-1387: the job carries NO basket because nothing was paid (a cancel refunds zero,
                // honestly - WO-911 Q1 "100% of what was paid" is 100% of nothing). (M1): the Train
                // line refuses at its depth cap; the muster stops with a readable reason rather than
                // truncating silently.
                if (queue.Enqueue(JobKind.TrainTroop, jobId, def.BuildSeconds, 0) == null)
                {
                    stopReason = queue.LastEnqueueFailure ?? "Training queue is full.";
                    FlowTrace.Warn("Barracks",
                        $"train enqueue refused at {enqueued}/{qty} '{troopId}' ({stopReason}).");
                    break;
                }
                committed += unitSlots;
                enqueued++;
                // §12: one Step per troop actually enqueued - a muster of N units leaves N proving
                // lines with the job id, so "did all 8 land on the Train channel?" is READ, not guessed.
                FlowTrace.Step("Barracks",
                    $"train job enqueued {enqueued}/{qty} '{troopId}' jobId={jobId} (Train, {def.BuildSeconds:0}s).");
            }

            if (enqueued > 0)
            {
                FlowTrace.Step("Barracks", $"training enqueued {enqueued}/{qty}x '{troopId}' (Train, {def.BuildSeconds:0}s each).");
                Changed?.Invoke();
                RaiseJobQueuedSignals(troopId);   // WO-1389 - once per call, not per unit
            }
            if (enqueued < qty)
                FlowTrace.Step("Barracks", $"training stopped at {enqueued}/{qty} '{troopId}' " +
                    $"(cap {cap}, roster {rosterSlots}, in-flight {committedBefore}).");
            return enqueued;
        }

        // ── Army fullness accounting (full-army gate lane) ─────────────────────

        /// <summary>
        /// Army slots already COMMITTED to in-flight timed training: the summed slot cost
        /// of every active + pending Train-channel job (job ids "barracks-train:...").
        /// 0 with no queue service. Consumed by <see cref="ArmyReadiness.Compute(GameState)"/>
        /// — the ONE readiness formula every gate/publisher reads (owner review 2026-08-01).
        /// </summary>
        public static int CommittedTrainingSlots()
        {
            var queue = BuildTimerService.Instance;
            if (queue == null) return 0;
            int total = 0;
            foreach (var j in queue.ActiveJobsOf(ChannelId.Train)) total += TrainJobSlots(j);
            foreach (var j in queue.PendingJobsOf(ChannelId.Train)) total += TrainJobSlots(j);
            return total;
        }

        /// <summary>Slot cost of one Train-channel job; 0 for a null/non-training job id.</summary>
        private static int TrainJobSlots(BuildJobData j)
        {
            if (string.IsNullOrEmpty(j.StructureId) || !j.StructureId.StartsWith(TrainPrefix))
                return 0;
            return TroopDialogueCommands.SlotOf(TroopIdFromTrain(j.StructureId));
        }

        /// <summary>
        /// WO-933 — how many Train-channel jobs (active + pending) are training
        /// <paramref name="troopDefId"/>. Used with <see cref="ArmyStorage.CountOfDef"/> to
        /// enforce <c>maxOwned</c> before a second siege piece can be paid for.
        /// </summary>
        public static int CountInFlightTrainOf(string troopDefId)
        {
            if (string.IsNullOrEmpty(troopDefId)) return 0;
            var queue = BuildTimerService.Instance;
            if (queue == null) return 0;
            int n = 0;
            foreach (var j in queue.ActiveJobsOf(ChannelId.Train))
                if (TrainJobMatchesTroop(j, troopDefId)) n++;
            foreach (var j in queue.PendingJobsOf(ChannelId.Train))
                if (TrainJobMatchesTroop(j, troopDefId)) n++;
            return n;
        }

        private static bool TrainJobMatchesTroop(BuildJobData j, string troopDefId)
        {
            // BuildJobData is a class-like payload; StructureId is the train job key.
            if (string.IsNullOrEmpty(j.StructureId) || !j.StructureId.StartsWith(TrainPrefix))
                return false;
            return string.Equals(TroopIdFromTrain(j.StructureId), troopDefId, System.StringComparison.Ordinal);
        }

        // ── Internals ─────────────────────────────────────────────────────────

        private static bool IsInFlight(ChannelId channel, string id)
        {
            var queue = BuildTimerService.Instance;
            if (queue == null || string.IsNullOrEmpty(id)) return false;
            foreach (var j in queue.ActiveJobsOf(channel)) if (j.StructureId == id) return true;
            foreach (var j in queue.PendingJobsOf(channel)) if (j.StructureId == id) return true;
            return false;
        }

        private static GameState LiveState() => State;

        private static void PersistAndNotify()
        {
            GameStateService.Instance?.Save();
            // Ruling 21: OnModifiersChanged now bridges ModifierService.Changed -> Changed. Suppress
            // it across our own Recompute so this path fires Changed exactly ONCE, not twice.
            _suppressModifierBridge = true;
            try { ModifierService.Recompute(); }   // barracks unlock refresh (perk/tier listeners)
            finally { _suppressModifierBridge = false; }
            Changed?.Invoke();
        }

        // ── Completion effects (registered once with the shared JobEffectRegistry) ──

        /// <summary>BarracksUpgrade job complete -> raise BarracksLevel by one, persist, notify.</summary>
        private sealed class BarracksUpgradeEffect : IJobEffect
        {
            public JobKind Kind => JobKind.BarracksUpgrade;
            public void Apply(BuildJobData job)
            {
                int level = BarracksProgression.ApplyBarracksUpgrade(LiveState());
                FlowTrace.Step("Barracks", "BarracksUpgrade job complete -> barracks L" + level + ".");
                PersistAndNotify();
            }
        }

        /// <summary>TroopUpgrade job complete -> raise that troop's upgrade level by one, persist, notify.</summary>
        private sealed class TroopUpgradeEffect : IJobEffect
        {
            public JobKind Kind => JobKind.TroopUpgrade;
            public void Apply(BuildJobData job)
            {
                string troopId = TroopIdFromUpgrade(job.StructureId);
                int level = BarracksProgression.ApplyTroopUpgrade(LiveState(), troopId);
                FlowTrace.Step("Barracks", "TroopUpgrade job complete -> '" + troopId + "' L" + level + ".");
                PersistAndNotify();
            }
        }

        /// <summary>TrainTroop job complete -> grant the trained troop into the army, persist, notify.</summary>
        private sealed class TrainTroopEffect : IJobEffect
        {
            public JobKind Kind => JobKind.TrainTroop;
            public void Apply(BuildJobData job)
            {
                string troopId = TroopIdFromTrain(job.StructureId);
                int count = BarracksProgression.GrantTrainedTroop(LiveState(), troopId);
                FlowTrace.Step("Barracks", "TrainTroop job complete -> +1 '" + troopId + "' (roster " + count + ").");
                PersistAndNotify();
            }
        }

        /// <summary>Recovers the troop id from a "barracks-troop-upgrade:&lt;troopId&gt;" job id.</summary>
        private static string TroopIdFromUpgrade(string jobId)
        {
            if (string.IsNullOrEmpty(jobId) || !jobId.StartsWith(TroopUpgradePrefix)) return jobId;
            return jobId.Substring(TroopUpgradePrefix.Length);
        }

        /// <summary>Recovers the troop id from a "barracks-train:&lt;troopId&gt;:&lt;uid&gt;" job id.
        /// Internal (was private): BuildTimerService's army-status publisher parses Train
        /// job ids through this ONE authority instead of duplicating the split.</summary>
        internal static string TroopIdFromTrain(string jobId)
        {
            if (string.IsNullOrEmpty(jobId)) return jobId;
            // Format: barracks-train:<troopId>:<uid>. Troop ids carry hyphens, never colons,
            // so split on ':' yields [prefix, troopId, uid].
            var parts = jobId.Split(':');
            return parts.Length >= 2 ? parts[1] : jobId;
        }

        // ── Ruling 21 seam: a BARRACKS BUILDING upgrade now changes the troop roster ──
        // Before the merge, only this service's own jobs could change the unlock level, so
        // BarracksService.Changed was a complete signal. Now the level moves when a barracks
        // BUILDING tier lands (CompletedUpgradeApplier -> ModifierService.Recompute), and the two
        // surfaces that listen for a roster change subscribe to THIS event only
        // (ArmyMusterPanel.cs:364; BarracksPanelVM.cs:166 was the second, until that file was DELETED
        // on 2026-09-06 by WO-1430 - the bridge is still needed for the surviving one). Without it a freshly unlocked
        // troop would appear on the next panel OPEN rather than live - a silent staleness, which is
        // the class of failure CLAUDE.md §12 forbids. Re-raise, guarded against the re-entrant
        // double-fire from our own PersistAndNotify (which calls Recompute and then Changed).
        private static bool _suppressModifierBridge;

        private static void OnModifiersChanged()
        {
            if (_suppressModifierBridge) return;   // our own PersistAndNotify raises Changed itself
            _suppressModifierBridge = true;
            try { Changed?.Invoke(); }
            finally { _suppressModifierBridge = false; }
        }

        // Register the three completion effects once at startup (idempotent; re-registered on domain reload).
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void RegisterEffects()
        {
            JobEffectRegistry.Register(new BarracksUpgradeEffect());
            JobEffectRegistry.Register(new TroopUpgradeEffect());
            JobEffectRegistry.Register(new TrainTroopEffect());

            // Idempotent: unsubscribe first so a domain reload cannot stack handlers.
            ModifierService.Changed -= OnModifiersChanged;
            ModifierService.Changed += OnModifiersChanged;

            FlowTrace.Step("Barracks", "WO-771.9 job effects registered (BarracksUpgrade/TroopUpgrade/TrainTroop); " +
                                       "ruling 21 - subscribed to ModifierService.Changed so a barracks BUILDING tier " +
                                       "refreshes the troop roster live.");
        }
    }
}

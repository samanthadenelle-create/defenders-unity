// =============================================================================
// JewelPolishService — the Jeweler's rough-stone POLISH, as an Obsidian queue job
// (WO-1042; owner rulings 2026-08-16).
// -----------------------------------------------------------------------------
// Assembly: DeNelle.Village   Namespace: DeNelle.Village.Crafting
//
// THE LOOP (every arrow after the polish was ALREADY BUILT — WO-1041 §2):
//   descend -> rough stone (ing_rough_stone, guaranteed on a completed run)
//           -> leave with the Jeweler -> [ OBSIDIAN QUEUE JOB ] -> refined gem
//           -> jeweler-recipes.json -> upgraded ring -> stronger hero in town
//
// ⛔ NO SECOND TIMER SYSTEM (CLAUDE.md §8, WO-1042 §4). The polish is a JobKind on the
// EXISTING Obsidian queue, on the EXISTING Research channel. It therefore inherits, for
// free and with zero new code: persistence, offline accrual, the depth cap of 5 per line,
// the slot economy, the Echo-gated bought slot, and the WO-911 cancel contract. There is
// no Update(), no coroutine, no timestamp of its own anywhere in this file — if you are
// about to add one, you are building the duplicate authority §8 exists to prevent.
//
// WHY THE RESEARCH CHANNEL AND NOT A NEW ONE (WO-1042 §5(1)): a 4th channel is a real
// addition to the queue model AND the HUD, and it would remove the one thing that makes
// the choice interesting. Polishing now COMPETES with troop-track and building-perk
// research for lab slots, which is the CoC ratchet applied to a new verb — a feature, and
// it cost nothing to build.
//
// ⛔ MONETISATION — SELL THE WAIT, NEVER THE ROLL (owner ruling 2026-08-16).
// JobKind.JewelPolish is registered in JobRushPolicy.IsRandomOutcome, so the queue's
// paid instant-finish verb REFUSES it at three separate sites (InstantFinishPrice returns
// 0, TryInstantFinish refuses, CompleteAnyJob refuses). Waiting and ad-skip stay open.
// Read JobRushPolicy's header before touching any of that.
//
// ⚠ HONEST NOTE ON THE RUN GRADE: WO-1040 is NOT implemented — there is no run-stat record
// in the tree. DungeonRunGrade is the shared rubric seam WO-1040 will fill; this service
// consumes DungeonRunGrade.PolishScore and nothing else, so when WO-1040 lands, NOTHING
// here changes. There is exactly one rubric.
// =============================================================================

using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using UnityEngine;
using DeNelle.Core.Catalog;
using DeNelle.Core.Diagnostics;
using DeNelle.Core.Jobs;
using DeNelle.Core.State;

namespace DeNelle.Village.Crafting
{
    // ── Tuning model (jewel-polish.json) ──────────────────────────────────────

    /// <summary>One weighted gem outcome within a score row.</summary>
    [Serializable]
    public sealed class PolishWeight
    {
        [JsonProperty("id")] public string Id;
        [JsonProperty("weight")] public float Weight;
    }

    /// <summary>The outcome table for one polish score (0..3).</summary>
    [Serializable]
    public sealed class PolishOutcomeRow
    {
        [JsonProperty("score")] public int Score;
        [JsonProperty("weights")] public List<PolishWeight> Weights = new List<PolishWeight>();
    }

    /// <summary>jewel-polish.json, whole.</summary>
    [Serializable]
    public sealed class PolishTuningData
    {
        [JsonProperty("version")] public int Version;
        [JsonProperty("polishSeconds")] public float PolishSeconds = 1800f;
        [JsonProperty("rePolishSeconds")] public float RePolishSeconds = 900f;
        [JsonProperty("rePolishScore")] public int RePolishScore = 2;
        [JsonProperty("rePolishShatterChance")] public float RePolishShatterChance = 0.15f;
        [JsonProperty("outcomes")] public List<PolishOutcomeRow> Outcomes = new List<PolishOutcomeRow>();
    }

    /// <summary>
    /// One disclosed outcome line: a gem id and its REAL probability on this roll.
    /// <para>
    /// ⛔ ALWAYS DERIVED FROM THE ROLL TABLE, NEVER AUTHORED SEPARATELY. See
    /// <see cref="JewelPolishService.DescribeOdds"/>.
    /// </para>
    /// </summary>
    public struct PolishOdds
    {
        /// <summary>The item id granted, or "" for the shatter line.</summary>
        public string Id;
        /// <summary>Player-facing ASCII label.</summary>
        public string Label;
        /// <summary>Probability in 0..1, summing to 1 across the whole disclosed set.</summary>
        public float Chance;
        /// <summary>True for the "the stone is destroyed" line.</summary>
        public bool IsShatter;
    }

    /// <summary>
    /// Loader + query surface for jewel-polish.json. Mirrors MaterialCatalog / JewelerRecipeCatalog
    /// exactly: CanonicalJson (Resources first, StreamingAssets fallback), Newtonsoft, never throws.
    /// </summary>
    public static class JewelPolishCatalog
    {
        private const string Sys = "JewelPolish";
        private const string CanonicalRelativePath = "Data/Canonical/jewel-polish.json";
        private static PolishTuningData _data;

        public static void Reload() { _data = null; EnsureLoaded(); }

        public static PolishTuningData Data { get { EnsureLoaded(); return _data; } }

        /// <summary>Seconds a first polish takes. Constant across grades by design (§5(2)).</summary>
        public static float PolishSeconds => Data.PolishSeconds;

        /// <summary>Seconds a RE-polish takes — shorter than a first polish (owner ruling).</summary>
        public static float RePolishSeconds => Data.RePolishSeconds;

        /// <summary>The bench-baseline score a re-polish rolls on (no run behind it).</summary>
        public static int RePolishScore => Data.RePolishScore;

        /// <summary>
        /// Chance a RE-polish destroys the stone. Zero for a first polish — see the json's
        /// schema note: it is the anti-pay-to-win mechanism, not flavour.
        /// </summary>
        public static float RePolishShatterChance => Mathf.Clamp01(Data.RePolishShatterChance);

        /// <summary>
        /// The outcome row for <paramref name="score"/>, falling back to the nearest authored row.
        /// Never null when the catalog loaded; null only when the file is missing entirely.
        /// </summary>
        public static PolishOutcomeRow RowFor(int score)
        {
            EnsureLoaded();
            if (_data.Outcomes == null || _data.Outcomes.Count == 0) return null;
            PolishOutcomeRow best = null;
            foreach (var r in _data.Outcomes)
            {
                if (r == null) continue;
                if (r.Score == score) return r;
                if (best == null || Math.Abs(r.Score - score) < Math.Abs(best.Score - score)) best = r;
            }
            return best;
        }

        private static void EnsureLoaded()
        {
            if (_data != null) return;
            try
            {
                string json = DeNelle.Core.CanonicalJson.Read(CanonicalRelativePath);
                if (!string.IsNullOrEmpty(json))
                {
                    var parsed = JsonConvert.DeserializeObject<PolishTuningData>(json);
                    if (parsed != null)
                    {
                        _data = parsed;
                        if (_data.Outcomes == null) _data.Outcomes = new List<PolishOutcomeRow>();
                        FlowTrace.Step(Sys, $"jewel-polish.json loaded: {_data.Outcomes.Count} score rows, " +
                                            $"polish {_data.PolishSeconds:0}s / repolish {_data.RePolishSeconds:0}s.");
                        return;
                    }
                }
                // NO SILENT FAILURE (§12.2): without this table a polish would land and grant nothing.
                FlowTrace.Fail(Sys, "jewel-polish.json NOT FOUND (Resources or StreamingAssets) - " +
                                    "polishing is disabled; every enqueue will refuse rather than eat a stone.");
            }
            catch (Exception ex)
            {
                FlowTrace.Fail(Sys, "jewel-polish.json parse FAILED: " + ex.Message + " - polishing disabled.");
            }
            _data = new PolishTuningData { Outcomes = new List<PolishOutcomeRow>() };
        }
    }

    // ── The service ───────────────────────────────────────────────────────────

    /// <summary>
    /// Starts polish jobs on the Obsidian Research channel and applies their outcome. Stateless:
    /// every bit of durable state lives on the queue's job record, so save/reload and offline
    /// accrual are the queue's problem, already solved.
    /// </summary>
    public static class JewelPolishService
    {
        /// <summary>Raised only after the authoritative queue accepts a polish job.</summary>
        public static event System.Action FirstPolishActionStarted;
        private const string Sys = "JewelPolish";

        /// <summary>Job-id prefix. Full id: <c>polish:&lt;inputItemId&gt;:&lt;seq&gt;</c>.</summary>
        public const string PolishJobPrefix = "polish:";

        /// <summary>The channel polish runs on. See the file header for why it is not a new one.</summary>
        public const ChannelId Channel = ChannelId.Research;

        // ── Enqueue ───────────────────────────────────────────────────────────

        /// <summary>
        /// Leave a ROUGH STONE with the Jeweler. Consumes one <c>ing_rough_stone</c> from the larder
        /// and queues a polish whose outcome odds are set by <paramref name="polishScore"/>
        /// (<see cref="DungeonRunGrade.PolishScore"/> of the run that produced the stone).
        /// </summary>
        public static bool TryStartPolish(int polishScore, out string failure)
            => TryStart(DungeonExclusiveItems.RoughStoneId, polishScore,
                        JewelPolishCatalog.PolishSeconds, "polish", out failure);

        /// <summary>
        /// Leave a rough stone with the Jeweler, taking the grade from the run that produced it
        /// (<see cref="DungeonRunPayout"/>'s FIFO). This is the call the Jeweler UI makes — the
        /// player never picks a score, they earned it underground.
        /// <para>
        /// The score is popped only AFTER the enqueue succeeds, so a refusal (bench full, no stone)
        /// never silently burns the grade of a run the player actually completed.
        /// </para>
        /// </summary>
        public static bool TryStartPolish(out string failure)
        {
            int score = DungeonRunPayout.PendingCount > 0 ? DungeonRunPayout.LastPolishScore : 0;
            if (!TryStartPolish(score, out failure)) return false;
            DungeonRunPayout.Pop();
            return true;
        }

        /// <summary>
        /// RE-polish an already-refined gem for another roll (owner ruling 2026-08-16). Consumes the
        /// gem and queues a SHORTER job that rolls on the bench-baseline row.
        /// <para>
        /// ⚠⚠ THIS CAN TRADE **DOWN**, ON PURPOSE. DO NOT ADD A FLOOR. ⚠⚠
        /// A floored re-roll ("never worse than what you put in") is not a gamble, it is a CHORE:
        /// with no downside, re-polishing becomes strictly dominant, the optimal play is to spam it
        /// to max, and the decision evaporates. The downside is what makes it a real choice — and it
        /// is SELF-BALANCING, which is the elegant part: a player holding a top-tier heartstone has
        /// everything to lose and will decline, while a player holding a common ember has nothing to
        /// lose and will play. Owner, 2026-08-16: "if you know that you already have the one that's
        /// in the twenty percent range, why would you risk it?" She overruled a proposed floor
        /// explicitly. If a downgrade ever looks like a bug to you, it is not — read this paragraph
        /// again before "fixing" it.
        /// </para>
        /// </summary>
        public static bool TryStartRePolish(string gemId, out string failure)
        {
            if (!DungeonExclusiveItems.Contains(gemId) || gemId == DungeonExclusiveItems.RoughStoneId)
            {
                failure = "That cannot be re-polished.";
                FlowTrace.Warn(Sys, $"re-polish refused: '{gemId}' is not a refined gem.");
                return false;
            }

            // ⛔ THE ROLL CAP (owner ruling 2026-08-16: "no more than five roles"). Counted per STONE
            // across its whole life — the first polish plus every re-roll. It is the second half of
            // the anti-pay-to-win pair: shatter re-ties attempts to material, and this caps how many
            // attempts a single stone can ever absorb even if none of them shatter. Without it, an
            // unlucky-but-patient player could still grind one stone indefinitely.
            // ⚠ The cap may be RAISED by an attempt bonus (staking grants ATTEMPTS, never odds).
            bool useWeeklyStakeReroll = DungeonRunPayout.RollsLeft <= 0 &&
                                        PolishBonuses.WeeklyRerollsRemaining > 0;
            if (DungeonRunPayout.RollsLeft <= 0 && !useWeeklyStakeReroll)
            {
                failure = "This stone has been worked as far as it will go.";
                FlowTrace.Step(Sys, $"re-polish refused: roll cap reached " +
                                    $"({DungeonRunPayout.RollsUsed}/{DungeonRunPayout.RollCap}).");
                return false;
            }

            bool started = TryStart(gemId, JewelPolishCatalog.RePolishScore,
                                    JewelPolishCatalog.RePolishSeconds, "re-polish", out failure);
            if (started && useWeeklyStakeReroll)
            {
                if (PolishBonuses.TryConsumeWeeklyReroll())
                    FlowTrace.Step(Sys, "re-polish used the verified native SKR weekly bonus attempt.");
                else
                    FlowTrace.Fail(Sys, "re-polish started after a native SKR weekly bonus check, but the " +
                                        "allowance could not be consumed. Inspect the stake/time authority.");
            }
            return started;
        }

        private static bool TryStart(string inputItemId, int score, float seconds, string verb,
                                     out string failure)
        {
            using var _ = FlowTrace.Enter(Sys, $"{verb} '{inputItemId}' (score {score})");
            failure = null;

            if (JewelPolishCatalog.RowFor(score) == null)
            {
                // Refuse BEFORE consuming, so a missing tuning file can never eat the player's stone.
                failure = "The bench is not ready.";
                FlowTrace.Fail(Sys, $"{verb} refused: no outcome row for score {score} " +
                                    "(jewel-polish.json missing or empty). Nothing was consumed.");
                return false;
            }

            var queue = BuildTimerService.Instance;
            if (queue == null)
            {
                failure = "The work queue is not ready.";
                FlowTrace.Warn(Sys, $"{verb} refused: no BuildTimerService. Nothing was consumed.");
                return false;
            }

            var inv = VillageInventory.Instance;
            if (inv == null || inv.Get(inputItemId) <= 0)
            {
                failure = "You do not have that stone.";
                FlowTrace.Warn(Sys, $"{verb} refused: no '{inputItemId}' in the larder.");
                return false;
            }

            // A FIRST polish begins a new stone's life, so it starts the roll allowance fresh. (A
            // re-polish must NOT reset, or the cap would never bind.)
            if (inputItemId == DungeonExclusiveItems.RoughStoneId) DungeonRunPayout.ResetRolls();

            EnsureCancelHook();     // late-bind for scenes where the queue spawned after startup
            string jobId = NextJobId(queue, inputItemId);

            // Consume FIRST, then enqueue — and hand it straight back if the enqueue is refused (the
            // depth cap can refuse AFTER the consume). Same shape as BuildingPerkService.RefundGold.
            if (!inv.TryConsume(inputItemId, 1))
            {
                failure = "You do not have that stone.";
                FlowTrace.Warn(Sys, $"{verb} refused: consume of '{inputItemId}' failed.");
                return false;
            }

            // Cost is TIME ONLY (WO-1042 §5(3)): the stone was already earned by descending, and
            // charging twice dulls the reward. The paid basket is therefore all-zero, which makes the
            // v37 cancel refund trivially correct — nothing was paid, so nothing is owed. The STONE
            // is returned separately by OnJobCancelled, because JobCost has no item lane.
            if (queue.Enqueue(JobKind.JewelPolish, Channel, jobId, seconds, score) == null)
            {
                inv.Add(inputItemId, 1);
                failure = "The bench is full. Try again when a slot frees up.";
                FlowTrace.Warn(Sys, $"{verb} enqueue REFUSED ({queue.LastEnqueueFailure ?? "unknown"}) - " +
                                    $"'{inputItemId}' returned to the larder.");
                return false;
            }

            // A roll is spent the moment the stone goes on the bench, not when it lands — otherwise a
            // player could queue, cancel and re-queue forever and the cap would mean nothing. The
            // cancel path returns the STONE (see OnJobCancelled) but deliberately does NOT refund the
            // roll: the attempt was taken.
            DungeonRunPayout.NoteRollSpent();
            if (inputItemId == DungeonExclusiveItems.RoughStoneId)
                FirstPolishActionStarted?.Invoke();

            FlowTrace.Step(Sys, $"{verb} ENQUEUED jobId={jobId} on {Channel} ({seconds:0}s, score {score}); " +
                                $"{DungeonRunPayout.RollsLeftLabel()}");
            return true;
        }

        /// <summary>
        /// Build a job id unique within the channel. The queue dedupes by targetId, so two stones
        /// polishing at once need distinct ids; the input item id rides along so the completion and
        /// cancel handlers know what went in without any new persisted state.
        /// </summary>
        private static string NextJobId(BuildTimerService queue, string inputItemId)
        {
            string stem = PolishJobPrefix + inputItemId + ":";
            for (int n = 1; n < 1000; n++)
            {
                string candidate = stem + n;
                if (!IsQueued(queue, candidate)) return candidate;
            }
            return stem + Guid.NewGuid().ToString("N").Substring(0, 8);
        }

        private static bool IsQueued(BuildTimerService queue, string jobId)
        {
            foreach (var j in queue.ActiveJobsOf(Channel)) if (j.StructureId == jobId) return true;
            foreach (var j in queue.PendingJobsOf(Channel)) if (j.StructureId == jobId) return true;
            return false;
        }

        /// <summary>The item id that went into a polish job, parsed from its id. "" when unparseable.</summary>
        public static string InputItemIdOf(string jobId)
        {
            if (string.IsNullOrEmpty(jobId) || !jobId.StartsWith(PolishJobPrefix, StringComparison.Ordinal))
                return "";
            string rest = jobId.Substring(PolishJobPrefix.Length);
            int colon = rest.LastIndexOf(':');
            return colon > 0 ? rest.Substring(0, colon) : rest;
        }

        // ── The roll ──────────────────────────────────────────────────────────

        /// <summary>
        /// Pick a refined gem id for <paramref name="score"/> from the authored weights.
        /// <paramref name="roll01"/> lets a regression pin the outcome deterministically; pass a
        /// negative value for a live random roll. Returns "" only when the tuning file is missing.
        /// </summary>
        public static string RollOutcome(int score, float roll01 = -1f)
        {
            var row = JewelPolishCatalog.RowFor(score);
            if (row == null || row.Weights == null || row.Weights.Count == 0) return "";

            float total = 0f;
            foreach (var w in row.Weights) if (w != null && w.Weight > 0f) total += w.Weight;
            if (total <= 0f) return "";

            float r = (roll01 >= 0f ? Mathf.Clamp01(roll01) : UnityEngine.Random.value) * total;
            foreach (var w in row.Weights)
            {
                if (w == null || w.Weight <= 0f) continue;
                r -= w.Weight;
                if (r <= 0f) return w.Id;
            }
            return row.Weights[row.Weights.Count - 1].Id;   // float drift guard
        }

        // ── Disclosed odds (the confirmation screen's ONLY source) ────────────

        /// <summary>
        /// The full disclosed outcome set for a roll at <paramref name="score"/>, as real
        /// probabilities summing to 1. Includes the shatter line when
        /// <paramref name="isRePolish"/> is true.
        /// <para>
        /// ⛔⛔ THIS IS THE ONE PLACE THE ODDS ARE COMPUTED, AND THE CONFIRMATION UI MUST RENDER
        /// EXACTLY WHAT IT RETURNS. ⛔⛔
        /// The percentages shown to the player are DERIVED FROM THE ROLL TABLE THE ROLL ACTUALLY
        /// USES — the same <see cref="JewelPolishCatalog.RowFor"/> and the same shatter constant that
        /// <see cref="RollOutcome"/> and the effect consume. They are never authored a second time.
        /// </para>
        /// <para>
        /// WHY THAT IS A HARD RULE HERE AND NOT A STYLE PREFERENCE: two hand-maintained copies of the
        /// same numbers is the defect class that produced several bugs in this project already — but
        /// in THIS feature a drift between displayed and actual odds is not merely a bug, it is
        /// MISREPRESENTATION of a paid random outcome, which is exactly what the disclosure regimes
        /// (Apple, Google, UK/EU) exist to police. JewelPolishRegression fails if a second odds source
        /// appears or if these numbers stop matching the table.
        /// </para>
        /// </summary>
        public static IReadOnlyList<PolishOdds> DescribeOdds(int score, bool isRePolish)
        {
            var result = new List<PolishOdds>();
            var row = JewelPolishCatalog.RowFor(score);
            if (row == null || row.Weights == null || row.Weights.Count == 0) return result;

            float total = 0f;
            foreach (var w in row.Weights) if (w != null && w.Weight > 0f) total += w.Weight;
            if (total <= 0f) return result;

            // The shatter roll happens FIRST and independently, so the gem odds are the remaining
            // probability mass. Modelling it any other way would overstate the gem chances.
            float shatter = isRePolish ? JewelPolishCatalog.RePolishShatterChance : 0f;
            float gemMass = 1f - shatter;

            foreach (var w in row.Weights)
            {
                if (w == null || w.Weight <= 0f) continue;
                result.Add(new PolishOdds
                {
                    Id = w.Id,
                    Label = DeNelle.Village.Items.MaterialCatalog.DisplayName(w.Id),
                    Chance = (w.Weight / total) * gemMass,
                    IsShatter = false,
                });
            }

            if (shatter > 0f)
            {
                result.Add(new PolishOdds
                {
                    Id = "",
                    Label = "The stone shatters",
                    Chance = shatter,
                    IsShatter = true,
                });
            }
            return result;
        }

        /// <summary>
        /// The disclosed odds as ASCII lines ("Ember Crystal  46%"), ready for a confirmation
        /// screen. Same derivation, so the screen cannot drift from the roll.
        /// </summary>
        public static IReadOnlyList<string> DescribeOddsLines(int score, bool isRePolish)
        {
            var lines = new List<string>();
            foreach (var o in DescribeOdds(score, isRePolish))
                lines.Add(o.Label + "  " + Mathf.RoundToInt(o.Chance * 100f) + "%");
            return lines;
        }

        // ── Completion + cancel ───────────────────────────────────────────────

        /// <summary>
        /// The polish landed: roll the tier and grant the gem. Reached from
        /// BuildTimerService.OnJobCompleted via JobEffectRegistry.Apply — live expiry, ad skip, or
        /// the OFFLINE SWEEP, all identically, which is exactly what a bespoke timer would have got
        /// wrong on reload.
        /// </summary>
        private sealed class JewelPolishEffect : IJobEffect
        {
            public JobKind Kind => JobKind.JewelPolish;

            public void Apply(BuildJobData job)
            {
                using var _ = FlowTrace.Enter(Sys, $"JewelPolishEffect.Apply '{job.StructureId}'");

                int score = job.TargetTier;                 // reused as the polish score (no schema bump)
                string input = InputItemIdOf(job.StructureId);
                bool isRePolish = input != DungeonExclusiveItems.RoughStoneId;

                // ⛔ THE SHATTER — RE-POLISH ONLY, AND IT IS THE ANTI-PAY-TO-WIN MECHANISM, NOT
                // FLAVOUR (owner ruling 2026-08-16). Re-rolling consumes no new material, so
                // unlimited attempts would converge on the top tier with CERTAINTY. Trade-down does
                // NOT prevent that on its own — the player simply stops when satisfied — so without
                // shatter, buying enough attempts would buy an OUTCOME rather than a CHANCE, which
                // breaks the owner's own fairness model (money buys attempts, never better odds).
                // Shatter re-ties attempts to earned material: a destroyed stone means another
                // dungeon run. A FIRST polish can never shatter, because that stone is the run's
                // guaranteed payout and destroying it would break "every completed run pays".
                // Do not soften this without understanding what it holds up.
                if (isRePolish && UnityEngine.Random.value < JewelPolishCatalog.RePolishShatterChance)
                {
                    // The stone's life is over, so the next stone starts with a full allowance.
                    DungeonRunPayout.ResetRolls();
                    FlowTrace.Step(Sys, $"re-polish SHATTERED '{input}' (chance " +
                                        $"{JewelPolishCatalog.RePolishShatterChance:P0}) - nothing granted, " +
                                        "roll counter reset. The risk was disclosed before the player confirmed.");
                    return;
                }

                string gemId = RollOutcome(score);

                if (string.IsNullOrEmpty(gemId))
                {
                    // The stone is gone and there is nothing to grant. Say so loudly, and hand the
                    // input back rather than silently destroying the player's property.
                    FlowTrace.Fail(Sys, $"polish '{job.StructureId}' completed but the outcome table is " +
                                        $"EMPTY for score {score} - returning '{input}' instead of granting.");
                    ReturnInput(input, "empty outcome table");
                    return;
                }

                var inv = VillageInventory.Instance;
                if (inv == null)
                {
                    FlowTrace.Fail(Sys, $"polish '{job.StructureId}' completed with NO VillageInventory - " +
                                        $"the '{gemId}' is LOST.");
                    return;
                }

                inv.Add(gemId, 1);
                FlowTrace.Step(Sys, $"polish COMPLETE: '{input}' (score {score}) -> '{gemId}' granted.");
                JewelPolishFlowPanel.ShowReveal(gemId);
            }
        }

        /// <summary>
        /// A polish job was cancelled — give the stone back.
        /// <para>
        /// WHY THIS EXISTS: the v37 paid basket refunds RESOURCES and is the right contract for every
        /// job that spends them. A polish spends an ITEM, and JobCost has no item lane, so without
        /// this the WO-911 cancel would hand back a correct-and-empty basket while quietly eating the
        /// player's stone. The refund is 100% and flat, matching the v37 contract exactly: the player
        /// loses the elapsed TIME, which is the real cost, and nothing else.
        /// </para>
        /// </summary>
        private static void OnJobCancelled(BuildJobData job)
        {
            if (job.JobKind != JobKind.JewelPolish) return;
            ReturnInput(InputItemIdOf(job.StructureId), "job cancelled");
        }

        private static void ReturnInput(string inputItemId, string why)
        {
            if (string.IsNullOrEmpty(inputItemId)) return;
            var inv = VillageInventory.Instance;
            if (inv == null)
            {
                FlowTrace.Fail(Sys, $"cannot return '{inputItemId}' ({why}) - no VillageInventory. " +
                                    "The player is down one stone.");
                return;
            }
            inv.Add(inputItemId, 1);
            FlowTrace.Step(Sys, $"returned '{inputItemId}' to the larder ({why}) - 100% flat, per the v37 contract.");
        }

        // ── Wiring ────────────────────────────────────────────────────────────

        /// <summary>
        /// Register the completion effect and the cancel hook once at startup (idempotent across
        /// domain reloads, exactly like BuildingPerkService.RegisterEffects). Without the effect
        /// registration a polish would finish and grant NOTHING — an unregistered kind is a silent
        /// no-op by design in JobEffectRegistry.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void RegisterEffects()
        {
            JobEffectRegistry.Register(new JewelPolishEffect());

            var queue = BuildTimerService.Instance;
            if (queue != null)
            {
                queue.JobCancelled -= OnJobCancelled;   // idempotent across domain reloads
                queue.JobCancelled += OnJobCancelled;
                FlowTrace.Step(Sys, "JewelPolish effect + cancel hook registered.");
            }
            else
            {
                // The service spawns later in some scenes; retry on the next queue creation is not
                // available, so say it plainly rather than leaving a silent hole in the cancel path.
                FlowTrace.Warn(Sys, "JewelPolish effect registered, but BuildTimerService was not up yet - " +
                                    "cancel-return will be wired by EnsureCancelHook on the first enqueue.");
            }
        }

        /// <summary>
        /// Late-bind the cancel hook for scenes where BuildTimerService spawns after
        /// <see cref="RegisterEffects"/>. Idempotent; safe to call often.
        /// </summary>
        public static void EnsureCancelHook()
        {
            var queue = BuildTimerService.Instance;
            if (queue == null) return;
            queue.JobCancelled -= OnJobCancelled;
            queue.JobCancelled += OnJobCancelled;
        }
    }
}

// =============================================================================
// ObsidianQueueEngine — the PURE, per-channel queue state machine (WO-773).
// -----------------------------------------------------------------------------
// Assembly: DeNelle.Core   Namespace: DeNelle.Core.Jobs
//
// No MonoBehaviour, no statics, no clock of its own — it takes a ChannelState, a
// derived slotCount, and an explicit nowMs, and mutates the lists. This is what
// makes the queue behaviour headlessly UNIT-TESTABLE: a test drives Resolve with a
// simulated wall-clock (advancing nowMs = "ticking the TimeSource") and asserts the
// slot cap, FIFO pull order, effect-on-completion and offline catch-up cascade.
//
// BuildTimerService is the thin MonoBehaviour wrapper that owns the GameState-backed
// ChannelStates, feeds this engine TimeSource.NowUnixMs(), and dispatches the
// completion effect. The engine only moves jobs between the two lists + reports
// completions; it never applies an effect itself.
//
// RULES the engine enforces (per channel, independently):
//   • A job STARTS immediately (StartMs = now) iff active.Count < slotCount, else it
//     lands at the TAIL of the FIFO pending queue (StartMs = 0 = "not started").
//   • A PENDING job (StartMs ≤ 0) NEVER completes — only running jobs count down.
//   • On completion the freed slot AUTO-PULLS the head of the pending queue
//     (StartMs = now) and the cascade repeats until no slot can pull + nothing is due
//     (so a long offline gap resolves a whole chain in one Resolve).
//   • Completions are reported in FINISH-TIME order (earliest first) — deterministic.
//   • WO-911: a channel REFUSES new work at its DEPTH cap (active + pending >= maxDepth).
//     Depth is a SEPARATE axis from slotCount/concurrency — see IsFull's remarks. The
//     cap belongs in Enqueue and NEVER in Resolve: Resolve's offline catch-up chain
//     deliberately re-starts a freed slot at done.FinishMs (not now) so a long offline
//     gap drains correctly, and a cap check there would strand a legitimately-queued job.
// =============================================================================

using System;
using DeNelle.Core.State;
using DeNelle.Core.Diagnostics;

namespace DeNelle.Core.Jobs
{
    /// <summary>Pure per-channel queue math for the Obsidian work queue. Stateless static.</summary>
    public static class ObsidianQueueEngine
    {
        /// <summary>Effective minimum slot count — a channel always has at least one worker.</summary>
        private static int Clamp(int slotCount) => slotCount < 1 ? 1 : slotCount;

        /// <summary>
        /// WO-911 (M1) — true when <paramref name="ch"/> is at its DEPTH cap and may accept no more
        /// work. Depth = active + pending on THIS channel (owner ruling Q4: 5 total per line, per
        /// channel, never global). <paramref name="maxDepth"/> &lt;= 0 means uncapped.
        /// </summary>
        /// <remarks>
        /// ⚠ Depth is NOT concurrency. <c>slotCount</c> bounds how many run at once; this bounds how
        /// many may be lined up. Conflating them (by raising freeBuildSlots to 5) would delete the
        /// waiting pain the crystal sink monetizes — see WO-911 section 2d.
        /// </remarks>
        public static bool IsFull(ChannelState ch, int maxDepth)
        {
            if (ch == null) return false;
            if (maxDepth <= 0) return false;
            ch.EnsureLists();
            return ch.ActiveJobs.Count + ch.PendingQueue.Count >= maxDepth;
        }

        /// <summary>
        /// Enqueue <paramref name="job"/> into <paramref name="ch"/>: start it now (StartMs =
        /// <paramref name="nowMs"/>, added to ActiveJobs) if a slot is free, else append it to the
        /// FIFO PendingQueue with StartMs = 0. Returns true if it started immediately.
        /// </summary>
        public static bool Enqueue(ChannelState ch, int slotCount, BuildJobData job, double nowMs)
            => Enqueue(ch, slotCount, job, nowMs, 0, out _);

        /// <summary>
        /// WO-911 (M1) — depth-capped enqueue. Identical to the legacy overload except the channel
        /// is REFUSED once it holds <paramref name="maxDepth"/> items (active + pending). On refusal
        /// NOTHING is mutated, <paramref name="accepted"/> is false and the caller must not treat
        /// the false return as "queued" — the legacy overload's <c>false</c> means "queued, not
        /// started", so the two outcomes are deliberately split across two out-values.
        /// </summary>
        /// <param name="accepted">False ONLY when the line was full. True for both started and queued.</param>
        /// <returns>True when the job STARTED immediately (a slot was free).</returns>
        public static bool Enqueue(ChannelState ch, int slotCount, BuildJobData job, double nowMs,
                                   int maxDepth, out bool accepted)
        {
            accepted = false;
            if (ch == null) return false;
            ch.EnsureLists();

            if (IsFull(ch, maxDepth))
            {
                // No silent failure (§12): the refusal is the interesting event, not the happy path.
                FlowTrace.Warn("Obsidian",
                    $"enqueue REFUSED — line full at depth {maxDepth} " +
                    $"({ch.ActiveJobs.Count} active + {ch.PendingQueue.Count} queued) for '{job.StructureId}'.");
                return false;
            }

            accepted = true;
            if (ch.ActiveJobs.Count < Clamp(slotCount))
            {
                job.StartMs = nowMs;
                ch.ActiveJobs.Add(job);
                return true;
            }
            job.StartMs = 0;               // pending / not-yet-started
            ch.PendingQueue.Add(job);
            return false;
        }

        /// <summary>
        /// Resolve <paramref name="ch"/> at <paramref name="nowMs"/>: complete every running job
        /// whose FinishMs has passed (earliest-finish first), auto-pulling the pending head into
        /// each freed slot and cascading until nothing more is due and no slot can pull. Each
        /// completed job is passed to <paramref name="onComplete"/> in completion order. Returns
        /// the number completed. Idempotent — a second call with the same nowMs completes nothing.
        /// </summary>
        public static int Resolve(ChannelState ch, int slotCount, double nowMs, Action<BuildJobData> onComplete,
            Func<BuildJobData, bool> beforeRemove = null)
        {
            if (ch == null) return 0;
            ch.EnsureLists();
            int slots = Clamp(slotCount);
            int completed = 0;
            int guard = 0;

            // Fill any slot that is free RIGHT NOW from the queue (covers a load where actives
            // were removed / the slot count grew): those jobs start "now" (they only became
            // eligible when the slot opened, which we treat as now for a live free slot).
            PullIntoFreeSlots(ch, slots, nowMs);

            while (guard++ < 100000)
            {
                // Find the earliest-finishing RUNNING job that is due (StartMs > 0 guards pending).
                int best = -1;
                double bestFinish = double.MaxValue;
                for (int i = 0; i < ch.ActiveJobs.Count; i++)
                {
                    var j = ch.ActiveJobs[i];
                    if (j.StartMs > 0 && j.FinishMs <= nowMs && j.FinishMs < bestFinish)
                    {
                        best = i;
                        bestFinish = j.FinishMs;
                    }
                }

                if (best < 0) break;   // nothing due

                var done = ch.ActiveJobs[best];
                // Durable effect owners can refuse removal until their idempotent result is saved.
                if (beforeRemove != null && !beforeRemove(done)) break;
                ch.ActiveJobs.RemoveAt(best);
                completed++;
                onComplete?.Invoke(done);

                // Auto-pull the next queued job into the just-freed slot. CRITICAL for offline
                // catch-up: it STARTS at the moment the slot freed (done.FinishMs), NOT at now —
                // so back-to-back offline jobs CHAIN (job2.start = job1.finish, job3.start =
                // job2.finish, …) and each becomes due in turn, draining the whole queue in one
                // Resolve. Clamp not to exceed now (a freed slot's finish is always ≤ now here,
                // but the guard keeps a future edge case honest). A 0-duration pulled job is
                // due immediately and completes on the next iteration (cascade).
                if (ch.PendingQueue.Count > 0 && ch.ActiveJobs.Count < slots)
                {
                    var next = ch.PendingQueue[0];
                    ch.PendingQueue.RemoveAt(0);
                    double startAt = done.FinishMs;
                    if (startAt > nowMs) startAt = nowMs;
                    if (startAt <= 0) startAt = nowMs;   // never leave it "pending" (StartMs 0)
                    next.StartMs = startAt;
                    ch.ActiveJobs.Add(next);
                }
            }
            return completed;
        }

        /// <summary>Move pending-queue heads into free active slots (StartMs = now) until full or empty.</summary>
        public static void PullIntoFreeSlots(ChannelState ch, int slotCount, double nowMs)
        {
            if (ch == null) return;
            ch.EnsureLists();
            int slots = Clamp(slotCount);
            while (ch.ActiveJobs.Count < slots && ch.PendingQueue.Count > 0)
            {
                var next = ch.PendingQueue[0];
                ch.PendingQueue.RemoveAt(0);
                next.StartMs = nowMs;
                ch.ActiveJobs.Add(next);
            }
        }
    }
}

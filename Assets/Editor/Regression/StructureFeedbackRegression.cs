// =============================================================================
// StructureFeedbackRegression [structure-feedback]
// -----------------------------------------------------------------------------
// Assembly: DeNelle.EditorRegression   Namespace: DeNelle.Editor
// Markers:  STRUCTURE_FEEDBACK_OK / STRUCTURE_FEEDBACK_FAIL
// Registered in DataRegression.RunAll.
//
// THE DEFECT THIS ORACLE EXISTS TO KILL (WO-1717 section 6C, owner-reported 2026-09-14).
//
//   The owner, mid-raid on the Seeker: "when troops attack you should see the damage
//   numbers so you can tell something is happening". The RCA proved the pool was already
//   there and simply never called: DamageNumberSpawner.Spawn had exactly TWO call sites
//   in the entire tree, both inside Enemy.cs. Killing a unit numbered. Hitting a wall,
//   a gate, a tower or the spire produced no number, no sound, and - because the HP bar
//   was attached by a 0.3 s Evaluate poll sitting behind a 2.0 s Scan poll - could show
//   nothing at all for up to 2.3 s. A wall under attack read as inert.
//
// THE INVARIANTS, and they are behavioural, not prose:
//   1. BASELINE IS NEVER A HIT. The first sample after an attach establishes the
//      baseline and fires nothing - otherwise every scene load would rain numbers.
//   2. THE NUMBER IS THE POINTS THAT LANDED. drop-fraction x the structure's own max HP.
//      For a wall that identity is exact and it is what makes the number POST-tier-divide:
//      WallSegment.MaxHp is a const 100 (WallSegment.cs:109), the wall's HP fraction is
//      1 - Damage/100 (RepairTarget.cs:141), and ApplyDamage adds `effective = amount /
//      ToughnessFor(t)` to that same 0-100 track (WallSegment.cs:334). So the fraction can
//      only ever move by the POST-divide value, and the number cannot lie about a steel
//      wall even in principle - it is not a value someone remembered to forward.
//   3. IT IS RATE-LIMITED. A six-troop warband on one panel must not stack a wall of
//      overlapping text.
//   4. AND RATE-LIMITING NEVER DISCARDS DAMAGE. Damage landing inside a cooldown is held
//      and flushed into the next number. A throttle on the TELL must not become a lie
//      about the TOTAL - that is the trap a naive cooldown walks straight into, and the
//      reason this case exists.
//   5. AN IMPLAUSIBLE DROP PRINTS NOTHING. > MaxCredibleDrop is a re-scale or a
//      save-restore; the flinch still plays, the number and the sound are withheld.
//   6. THE HOST FLINCH FIRES ON THE HIT SAMPLE ITSELF - that is the hook the first-hit
//      HP-bar attach hangs off, so proving it proves the 2.3 s latency is gone.
//   7. ONE POOL (WO-953). DamageNumberSpawner is the only floating-text sink.
//   8. THE SOUND EXISTS AND IS WIRED. SfxId.StructureImpact is declared and played.
//
// WHY SOURCE LINTS FOR 7 + 8: DeNelle.EditorRegression does NOT reference DeNelle.Audio
// (read at Assets/Editor/Regression/DeNelle.EditorRegression.asmdef), so SfxId is not a
// type this assembly can name. The lint strips comments FIRST - this very file and the
// files it reads discuss the ticket at length in prose, and an unstripped match would
// pass on the commentary after the code was gone.
//
// Deterministic: fixture GameObjects + an injected clock + editor source reads. No scene
// load, no PlayMode, no camera, no audio device, no VFX pool. That is precisely why
// StructureHitReaction.TrySample was split out of Update.
//
// Standalone batch entry:
//   -Method DeNelle.Editor.StructureFeedbackRegression.RunStandalone
// =============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using DeNelle.Village;
using UnityEngine;
using Object = UnityEngine.Object;

namespace DeNelle.Editor
{
    public static class StructureFeedbackRegression
    {
        private const string MarkerOk   = "STRUCTURE_FEEDBACK_OK";
        private const string MarkerFail = "STRUCTURE_FEEDBACK_FAIL";

        private const string ReactionPath = "Assets/_Modules/Village/Vfx/StructureHitReaction.cs";
        private const string VisualsPath  = "Assets/_Modules/Village/Vfx/StructureDamageVisuals.cs";
        private const string SfxIdPath    = "Assets/_Modules/Audio/SfxId.cs";

        // The wall's whole HP track. Named here so the arithmetic below is readable;
        // the AUTHORITY is WallSegment.MaxHp and case 2 asserts they agree.
        private const float WallMaxHp = 100f;

        /// <summary>Standalone batch entry point.</summary>
        public static void RunStandalone()
        {
            bool ok = Run(out string reason);
            Debug.Log((ok ? MarkerOk : MarkerFail) + ": " + reason);
        }

        /// <summary>
        /// Runs every case. Returns true when all pass; <paramref name="reason"/> always
        /// carries a one-line human summary (the failure list, or the pass count).
        /// </summary>
        public static bool Run(out string reason)
        {
            var failures = new List<string>();

            Case1_BaselineIsNeverAHit(failures);
            Case2_NumberIsPointsActuallyApplied(failures);
            Case3_RateLimited(failures);
            Case4_ThrottleNeverDiscardsDamage(failures);
            Case5_ImplausibleDropPrintsNothing(failures);
            Case6_UnknownMaxHpStillFlinches(failures);
            Case7_HostFlinchFiresOnTheHitSample(failures);
            Case8_SourceContract(failures);

            if (failures.Count > 0)
            {
                reason = failures.Count + " failure(s): " + string.Join(" | ", failures);
                return false;
            }
            reason = "8/8 cases: structure hits number the POST-tier-divide points, on one pool, "
                   + "rate-limited without discarding damage, with a masonry SfxId and a same-frame HP bar.";
            return true;
        }

        // ── Fixture ────────────────────────────────────────────────────────────

        /// <summary>
        /// A bare structure: one GameObject, no renderer (so the tell falls back to the
        /// transform anchor deterministically), a settable HP fraction, and a known max HP.
        /// </summary>
        private sealed class Fixture : IDisposable
        {
            public readonly GameObject Host;
            public readonly StructureHitReaction Reaction;
            public float Fraction = 1f;
            public int HostFlinches;

            public Fixture(float maxHp, bool withMaxHp = true)
            {
                Host = new GameObject("StructureFeedbackFixture");
                Reaction = StructureHitReaction.Attach(
                    Host, () => Fraction, "fixture",
                    () => HostFlinches++,
                    withMaxHp ? (Func<float>)(() => maxHp) : null);
            }

            /// <summary>Move the HP fraction to <paramref name="fraction"/> and sample at <paramref name="time"/>.</summary>
            public bool Step(float fraction, float time, out StructureHitReaction.HitSample hit)
            {
                Fraction = fraction;
                return Reaction.TrySample(fraction, time, out hit);
            }

            public void Dispose()
            {
                if (Host != null) Object.DestroyImmediate(Host);
            }
        }

        private static bool Near(float a, float b, float eps = 0.01f) => Mathf.Abs(a - b) <= eps;

        // ── Case 1 — the baseline sample fires nothing ─────────────────────────

        private static void Case1_BaselineIsNeverAHit(List<string> failures)
        {
            using (var f = new Fixture(WallMaxHp))
            {
                if (f.Reaction == null) { failures.Add("case1: Attach returned null on a live GameObject"); return; }

                if (f.Step(1f, 0f, out _))
                    failures.Add("case1: the FIRST sample reported a hit - a scene load would rain numbers");
                if (f.HostFlinches != 0)
                    failures.Add("case1: the host flinch fired on the baseline sample");

                // A no-change sample is likewise silent.
                if (f.Step(1f, 0.5f, out _))
                    failures.Add("case1: an unchanged HP fraction reported a hit");
            }
        }

        // ── Case 2 — the number is the points actually applied ─────────────────

        private static void Case2_NumberIsPointsActuallyApplied(List<string> failures)
        {
            // The identity this whole ticket rests on: the wall's own constant is the
            // divisor behind the fraction, so fraction x MaxHp is the post-tier-divide
            // `effective`. If WallSegment.MaxHp ever stops being 100 this case says so.
            if (!Near(WallSegment.MaxHp, WallMaxHp))
                failures.Add($"case2: WallSegment.MaxHp is {WallSegment.MaxHp}, this oracle assumed {WallMaxHp}");

            using (var f = new Fixture(WallSegment.MaxHp))
            {
                f.Step(1f, 0f, out _);   // baseline

                // A tier-3 (ReinforcedSteel) wall taking a 29-damage archer hit absorbs
                // 29 / 1.6^2 = 11.33 points on the shared 0-100 track. That is what must
                // show - NOT the 29 that was requested.
                const float effective = 29f / (1.6f * 1.6f);
                float after = 1f - effective / WallSegment.MaxHp;

                if (!f.Step(after, 0f, out var hit))
                {
                    failures.Add("case2: a real blow reported NO hit");
                    return;
                }
                if (!hit.Credible)
                    failures.Add("case2: an 11-point blow was judged implausible");
                if (!Near(hit.Damage, effective, 0.05f))
                    failures.Add($"case2: number was {hit.Damage:0.00}, expected the post-tier-divide {effective:0.00} "
                               + "- the number is reporting the REQUEST, not what landed");
                if (Near(hit.Damage, 29f, 0.05f))
                    failures.Add("case2: number equals the RAW 29 request - the tier divide is being bypassed");
            }
        }

        // ── Case 3 — rate-limited ──────────────────────────────────────────────

        private static void Case3_RateLimited(List<string> failures)
        {
            using (var f = new Fixture(WallSegment.MaxHp))
            {
                f.Step(1f, 0f, out _);                 // baseline
                if (!f.Step(0.88f, 0f, out var first)) { failures.Add("case3: first blow did not fire"); return; }
                if (!(first.Damage > 0f)) failures.Add("case3: the first blow printed no number");

                // A second troop lands on the same panel two hundredths of a second later.
                // The TELL must not fire again - that is the wall-of-text the owner would see.
                if (f.Step(0.80f, 0.02f, out _))
                    failures.Add("case3: a second blow 0.02 s later fired another burst - numbers would stack");

                // And the number runs on its own, slower cadence: the burst may come back
                // at 0.15 s but a second NUMBER must not.
                if (f.Step(0.72f, 0.20f, out var third))
                {
                    if (third.Damage > 0f)
                        failures.Add("case3: a second number printed 0.20 s after the first - "
                                   + "numbers outlive their own cadence (Lifetime 0.55 s) and would overlap");
                }
                else failures.Add("case3: the dust burst did not resume at 0.20 s");
            }
        }

        // ── Case 4 — the throttle holds damage, it never drops it ──────────────

        private static void Case4_ThrottleNeverDiscardsDamage(List<string> failures)
        {
            using (var f = new Fixture(WallSegment.MaxHp))
            {
                f.Step(1f, 0f, out _);                 // baseline
                f.Step(0.88f, 0f, out var first);      // 12 points, printed

                // Three more blows inside the number cadence: 8 + 8 + 4 = 20 points.
                f.Step(0.80f, 0.05f, out _);
                f.Step(0.72f, 0.20f, out _);
                f.Step(0.68f, 0.30f, out _);

                // Now past MinNumberInterval. The number owed is the SUM of everything the
                // structure actually lost since the last one - not the last blow alone.
                if (!f.Step(0.68f, 0.40f, out var flush))
                {
                    failures.Add("case4: nothing flushed after the cadence expired - held damage was stranded");
                    return;
                }
                float owed = (0.88f - 0.68f) * WallSegment.MaxHp;   // 20 points
                if (!Near(flush.Damage, owed, 0.05f))
                    failures.Add($"case4: flushed number was {flush.Damage:0.00}, expected the accumulated {owed:0.00} "
                               + "- the throttle is DISCARDING damage, so the on-screen total under-reports a warband");
                if (!(first.Damage > 0f))
                    failures.Add("case4: the opening blow printed nothing");
            }
        }

        // ── Case 5 — an implausible drop prints nothing ────────────────────────

        private static void Case5_ImplausibleDropPrintsNothing(List<string> failures)
        {
            using (var f = new Fixture(WallSegment.MaxHp))
            {
                f.Step(1f, 0f, out _);                 // baseline
                // A save-restore drops a pristine wall to 5% in one frame.
                if (!f.Step(0.05f, 0f, out var hit))
                {
                    failures.Add("case5: an implausible drop fired nothing at all - the flinch should still play");
                    return;
                }
                if (hit.Credible)
                    failures.Add("case5: a 95% one-frame drop was judged a credible BLOW");
                if (hit.Damage > 0f)
                    failures.Add($"case5: a save-restore printed a {hit.Damage:0} damage number - the tell is lying");
            }
        }

        // ── Case 6 — a structure with no knowable max HP still flinches ────────

        private static void Case6_UnknownMaxHpStillFlinches(List<string> failures)
        {
            using (var f = new Fixture(0f, withMaxHp: false))
            {
                f.Step(1f, 0f, out _);                 // baseline
                if (!f.Step(0.90f, 0f, out var hit))
                {
                    failures.Add("case6: a structure with no max-HP source stopped flinching entirely");
                    return;
                }
                if (hit.Damage > 0f)
                    failures.Add("case6: a number was invented for a structure whose max HP is unknown");
                if (!(hit.DropFraction > 0f))
                    failures.Add("case6: the drop fraction was lost, so the dust burst has nothing to report");
            }
        }

        // ── Case 7 — the host flinch fires on the hit sample ───────────────────

        private static void Case7_HostFlinchFiresOnTheHitSample(List<string> failures)
        {
            using (var f = new Fixture(WallSegment.MaxHp))
            {
                f.Step(1f, 0f, out _);                 // baseline
                f.Step(0.90f, 0f, out _);
                if (f.HostFlinches != 1)
                    failures.Add($"case7: host flinch fired {f.HostFlinches}x on the FIRST hit, expected exactly 1 "
                               + "- this callback is what attaches the HP bar, so the 2.3 s poll latency is back");

                // Suppressed samples must not fire it either (one blow, one flinch).
                f.Step(0.86f, 0.02f, out _);
                if (f.HostFlinches != 1)
                    failures.Add("case7: the host flinch fired inside the cooldown - the bar attach would run per tick");
            }
        }

        // ── Case 8 — the source contract (one pool, and the sound exists) ──────

        private static void Case8_SourceContract(List<string> failures)
        {
            string reaction = ReadStripped(ReactionPath, failures);
            string visuals  = ReadStripped(VisualsPath, failures);
            string sfxIds   = ReadStripped(SfxIdPath, failures);
            if (reaction == null || visuals == null || sfxIds == null) return;

            // 7. ONE POOL (WO-953). The number goes through the existing spawner and the
            //    hit reaction builds no floating text of its own.
            if (!reaction.Contains("DamageNumberSpawner.Spawn("))
                failures.Add("case8: StructureHitReaction no longer calls DamageNumberSpawner.Spawn - "
                           + "structure hits are silent numbers again (the exact WO-1717 defect)");
            if (reaction.Contains("TextMesh") || reaction.Contains("new GameObject("))
                failures.Add("case8: StructureHitReaction is building its own floating text - "
                           + "WO-953 allows exactly ONE floating-text pool");

            // 8. THE SOUND. This assembly cannot name SfxId (no DeNelle.Audio reference in
            //    DeNelle.EditorRegression.asmdef), so the declaration and the call site are
            //    both asserted against comment-stripped source.
            if (!Regex.IsMatch(sfxIds, @"\bStructureImpact\s*,"))
                failures.Add("case8: SfxId.StructureImpact is no longer declared - the masonry impact sound is gone");
            if (!reaction.Contains("SfxId.StructureImpact"))
                failures.Add("case8: StructureHitReaction no longer plays SfxId.StructureImpact - "
                           + "the owner is colourblind and SOUND is one of the three required channels");

            // The MOTION channel, unchanged and still required alongside the other two.
            if (!reaction.Contains("VFXType.Env_DestructionDust"))
                failures.Add("case8: the dust burst is gone - MOTION is the third required channel");

            // 6/5. The HP bar attaches from the hit callback, not from the 2.0 s + 0.3 s polls,
            //      and FloatingHealthBar.Attach is called from exactly ONE place in that file.
            if (!Regex.IsMatch(visuals, @"StructureHitReaction\.Attach\([^;]*AttachBar\("))
                failures.Add("case8: StructureDamageVisuals no longer attaches the HP bar from the hit flinch - "
                           + "a struck structure goes back to waiting up to 2.3 s for the Scan+Evaluate polls");
            int attachCalls = CountOccurrences(visuals, "FloatingHealthBar.Attach(");
            if (attachCalls != 1)
                failures.Add($"case8: FloatingHealthBar.Attach( appears {attachCalls}x in StructureDamageVisuals, "
                           + "expected exactly 1 (the shared AttachBar helper) - two copies will drift");
        }

        // ── Source helpers ─────────────────────────────────────────────────────

        /// <summary>
        /// Reads a source file with COMMENTS AND STRING LITERALS REMOVED. Every file this
        /// oracle reads argues about WO-1717 in prose and in trace strings; an unstripped
        /// match would keep passing long after the code it describes was deleted.
        /// </summary>
        private static string ReadStripped(string path, List<string> failures)
        {
            if (!File.Exists(path)) { failures.Add("case8: missing source file " + path); return null; }

            string src;
            try { src = File.ReadAllText(path); }
            catch (Exception e) { failures.Add($"case8: could not read {path}: {e.Message}"); return null; }

            var sb = new StringBuilder(src.Length);
            bool inLine = false, inBlock = false, inStr = false, inChar = false, escaped = false;
            for (int i = 0; i < src.Length; i++)
            {
                char c = src[i];
                char n = i + 1 < src.Length ? src[i + 1] : '\0';

                if (inLine) { if (c == '\n') { inLine = false; sb.Append(c); } continue; }
                if (inBlock) { if (c == '*' && n == '/') { inBlock = false; i++; } continue; }
                if (inStr)
                {
                    if (escaped) escaped = false;
                    else if (c == '\\') escaped = true;
                    else if (c == '"') inStr = false;
                    continue;
                }
                if (inChar)
                {
                    if (escaped) escaped = false;
                    else if (c == '\\') escaped = true;
                    else if (c == '\'') inChar = false;
                    continue;
                }

                if (c == '/' && n == '/') { inLine = true; i++; continue; }
                if (c == '/' && n == '*') { inBlock = true; i++; continue; }
                if (c == '"') { inStr = true; continue; }
                if (c == '\'') { inChar = true; continue; }
                sb.Append(c);
            }
            return sb.ToString();
        }

        private static int CountOccurrences(string haystack, string needle)
        {
            int count = 0, at = 0;
            while ((at = haystack.IndexOf(needle, at, StringComparison.Ordinal)) >= 0) { count++; at += needle.Length; }
            return count;
        }
    }
}

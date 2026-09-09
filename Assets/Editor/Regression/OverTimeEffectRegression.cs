// =============================================================================
// OverTimeEffectRegression [over-time] - WO-1330. THE PROOF THAT THE EFFECT
// ACTUALLY TICKS.
// -----------------------------------------------------------------------------
// Assembly: DeNelle.EditorRegression. Namespace: DeNelle.Editor.Regression.
// Markers: OVER_TIME_OK / OVER_TIME_FAIL.
//
// -----------------------------------------------------------------------------
// WHY THIS SUITE EXISTS, AND WHY IT IS NOT A DATA LINT.
// -----------------------------------------------------------------------------
// The work order's acceptance line is precise: "An oracle pins the effect
// actually ticking (applied, ticks N times, expires)". Asserting that
// abilities.json contains a row called mage.wither would prove NOTHING - this
// repo has a documented history of exactly that mistake (the WO-783 inert
// waves.json batches; 360 finished gear rows invisible behind a stale catalog
// copy), and the work order itself warns that authored tokens are "a TRAP, not
// evidence". So this suite DRIVES THE ENGINE with a fake clock and COUNTS THE
// PULSES.
//
// That is only possible because DeNelle.Core.Combat.OverTimeEngine takes its
// clock as a parameter and its sink as a delegate - no MonoBehaviour, no
// coroutine, no Time.time. EditMode never runs Update, so an over-time effect
// built the obvious way (a coroutine) is one a gate can never observe.
//
// -----------------------------------------------------------------------------
// WHAT IS PINNED (hard failures):
//   1 [ticks]      applied -> N pulses -> EXPIRES. The default 8 dps / 4s case
//                  delivers exactly 4 pulses of 8.0 (total 32) and the engine is
//                  then empty. Nothing lands before the first full interval.
//   2 [ceil]       a duration that is not a whole number of intervals delivers
//                  CEIL pulses, because the shipped coroutine's loop test ran
//                  before its increment and slightly over-delivered. Rounding
//                  that "cleanly" would be a stealth nerf to two live abilities.
//   3 [death]      a target that dies mid-effect stops receiving pulses, on the
//                  very same frame - reproducing "if (!target.IsAlive) yield break".
//   4 [sign]       ONE engine, BOTH signs. A Heal effect never reports Damage,
//                  magnitude is always POSITIVE, and the two closed generic
//                  types share one body.
//   5 [invariant]  moving the CADENCE knob does not move TOTAL delivery. This is
//                  the property that makes combat.overTimeTickMs safe to hand to
//                  the owner as a feel dial - if it ever became a damage dial,
//                  a felt-test of "how it reads" would silently be a balance change.
//   6 [tunable]    with no table the engine reproduces the SHIPPED numbers of
//                  knight.emberbrand-throw exactly; with a table the three knobs
//                  actually move it. Refusals prove nothing on their own (memory:
//                  prove-the-success-path-not-just-the-refusal).
//   7 [wiring]     the two new abilities exist in BOTH canonical copies, their
//                  effect strings resolve to real live dispatch cases, and a
//                  talent node grants each. A spell with no node is unreachable.
//   8 [owner-tag]  the new abilities' VFX keys are EMPTY. The owner tags VFX and
//                  the CLI maps them verbatim; a key appearing here without her
//                  word is a creative pick the CLI is not allowed to make. This
//                  rule FAILS ON A FILLED FIELD, which is deliberate - it is the
//                  only kind of assertion that can catch a well-meaning seat
//                  "finishing" the ability.
//   9 [one-loop]   HeroAbilities carries no surviving hardcoded DoT tick loop.
//                  Two abilities, ONE mechanism - the line the work order bolded.
//
// Standalone: run-unity-method DeNelle.Editor.Regression.OverTimeEffectRegression.RunAll
// Registered in DataRegression.RunAll as "[over-time]".
// =============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using DeNelle.Core.Combat;
using DeNelle.Core.Ops;
using UnityEngine;

namespace DeNelle.Editor.Regression
{
    public static class OverTimeEffectRegression
    {
        // Source paths, relative to the repo root (batchmode CWD).
        private const string AbilitiesSrc = "Assets/_Modules/Village/Hero/HeroAbilities.cs";
        private const string ResAbilities = "Assets/Resources/Data/Canonical/abilities.json";
        private const string SaAbilities = "Assets/StreamingAssets/Data/Canonical/abilities.json";
        private const string ResTalents = "Assets/Resources/Data/Canonical/hero-talents.json";
        private const string SaTalents = "Assets/StreamingAssets/Data/Canonical/hero-talents.json";

        /// <summary>The two abilities WO-1330 added, and the node that grants each.</summary>
        private static readonly string[][] NewAbilities =
        {
            //  ability id          effect string   granting node   pool
            new[] { "mage.wither",     "dot",   "mage.t1n4",    "mage-skills" },
            new[] { "knight.ironblood", "regen", "knight.t1n3", "knight-skills" },
        };

        /// <summary>A test double for anything the engine can damage or heal.</summary>
        private sealed class Dummy
        {
            public float Hp = 1000f;
            public bool Alive = true;
            public int Pulses;
            public float Total;
        }

        public static void RunAll()
        {
            if (Run(out string reason)) Debug.Log("OVER_TIME_OK - " + reason);
            else Debug.LogError("OVER_TIME_FAIL: " + reason);
        }

        public static bool Run(out string reason)
        {
            var failures = new List<string>();
            var notes = new List<string>();

            // Every case starts from the resting state - no row, no override - so a
            // leaked payload from an earlier suite cannot make this one pass or fail.
            RemoteTunables.Clear();

            Case(failures, "ticks", () => Case1_TicksAndExpires(failures, notes));
            Case(failures, "ceil", () => Case2_CeilPulseCount(failures, notes));
            Case(failures, "death", () => Case3_StopsOnDeath(failures, notes));
            Case(failures, "sign", () => Case4_BothSignsOneEngine(failures, notes));
            Case(failures, "invariant", () => Case5_CadenceDoesNotMoveTotal(failures, notes));
            Case(failures, "tunable", () => Case6_KnobsMoveIt(failures, notes));
            Case(failures, "wiring", () => Case7_AbilitiesAreReachable(failures, notes));
            Case(failures, "owner-tag", () => Case8_VfxKeysHeldEmpty(failures, notes));
            Case(failures, "one-loop", () => Case9_NoSurvivingTickLoop(failures, notes));

            RemoteTunables.Clear();

            if (failures.Count > 0)
            {
                reason = string.Join(" | ", failures);
                return false;
            }
            reason = "OVER-TIME OK - the shared engine applies, ticks the pinned number of times and " +
                     "expires, on both signs; the cadence knob moves feel without moving totals; the " +
                     "two new abilities are reachable in both canonical copies and their VFX keys are " +
                     "correctly still EMPTY, awaiting an owner tag. " + string.Join("; ", notes);
            return true;
        }

        private static void Case(List<string> failures, string name, Action body)
        {
            try { body(); }
            catch (Exception ex) { failures.Add("[" + name + "] THREW: " + ex.Message); }
        }

        // =====================================================================
        //  1 [ticks] - THE ACCEPTANCE LINE: applied -> ticks N times -> expires.
        // =====================================================================
        private static void Case1_TicksAndExpires(List<string> failures, List<string> notes)
        {
            RemoteTunables.Clear();
            var engine = new OverTimeEngine<Dummy>(LivenessOf);
            var foe = new Dummy();

            // knight.emberbrand-throw's SHIPPED burn, verbatim: 8 dps for 4 seconds.
            int promised = engine.Apply(foe, "burn", 8f, 4f, OverTimeKind.Damage, now: 0f);
            if (promised != 4)
                failures.Add("[ticks] Apply promised " + promised + " pulses for 8 dps over 4s at the " +
                             "shipping 1s cadence; the shipped coroutine delivered 4.");
            if (engine.ActiveCount != 1)
                failures.Add("[ticks] the effect is not in flight after Apply (ActiveCount=" +
                             engine.ActiveCount + ")");

            // NOTHING may land before one full interval. The shipped loop waited FIRST,
            // so an ability that dealt its impact damage AND a burn pulse on the cast
            // frame would silently gain a tick.
            engine.Advance(0.99f, Sink(foe));
            if (foe.Pulses != 0)
                failures.Add("[ticks] " + foe.Pulses + " pulse(s) landed at t=0.99s, before the first " +
                             "full 1s interval. The shipped coroutine's first act was to WAIT, so this " +
                             "would hand every DoT a free extra tick on the cast frame.");

            for (int step = 1; step <= 4; step++)
            {
                engine.Advance(step, Sink(foe));
                if (foe.Pulses != step)
                    failures.Add("[ticks] after t=" + step + "s the effect had delivered " + foe.Pulses +
                                 " pulse(s), expected " + step + ". The tick is not landing on the beat.");
            }

            if (Math.Abs(foe.Total - 32f) > 0.001f)
                failures.Add("[ticks] total delivered was " + foe.Total + ", expected 32 (8 dps x 4s). " +
                             "The magnitude arithmetic is wrong, which would silently rebalance every " +
                             "DoT in the game.");

            // ...AND EXPIRES. An effect that ticks forever is worse than one that never ticks.
            if (engine.ActiveCount != 0)
                failures.Add("[ticks] the effect did NOT expire - " + engine.ActiveCount + " still in " +
                             "flight after its last pulse. A DoT that never lets go is a permanent debuff.");

            engine.Advance(99f, Sink(foe));
            if (foe.Pulses != 4)
                failures.Add("[ticks] the effect delivered " + foe.Pulses + " pulses once the clock ran " +
                             "far past its window; it must stop at exactly 4.");

            notes.Add("ticks: 8dps/4s -> exactly 4 pulses of 8.0 (total 32), none early, then expired");
        }

        // =====================================================================
        //  2 [ceil] - the shipped over-delivery, preserved on purpose.
        // =====================================================================
        private static void Case2_CeilPulseCount(List<string> failures, List<string> notes)
        {
            // The shipped loop was: while (elapsed < duration) { wait; elapsed += tick; hit; }
            // so 4.5s at a 1s tick ran the body FIVE times. Preserved deliberately - see
            // the PULSE ARITHMETIC block in OverTimeEffects.cs.
            int n = OverTimeEngine<Dummy>.PulseCountFor(4.5f, 1f);
            if (n != 5)
                failures.Add("[ceil] a 4.5s effect at a 1s tick resolves to " + n + " pulses, not 5. " +
                             "The shipped coroutine tested BEFORE it incremented and over-delivered; " +
                             "rounding that down is a stealth nerf to knight.emberbrand-throw and " +
                             "mage.poison, not a tidy-up.");

            // An EXACT multiple must not gain a phantom pulse to float slop.
            if (OverTimeEngine<Dummy>.PulseCountFor(4f, 1f) != 4)
                failures.Add("[ceil] an exact 4s / 1s effect resolved to " +
                             OverTimeEngine<Dummy>.PulseCountFor(4f, 1f) + " pulses, not 4 - float slop " +
                             "is leaking a free tick into every whole-second DoT in the game.");
            if (OverTimeEngine<Dummy>.PulseCountFor(10f, 1f) != 10)
                failures.Add("[ceil] mage.poison's 10s / 1s burn resolved to " +
                             OverTimeEngine<Dummy>.PulseCountFor(10f, 1f) + " pulses, not 10.");

            // A degenerate input answers 0 rather than looping or dividing by zero.
            if (OverTimeEngine<Dummy>.PulseCountFor(0f, 1f) != 0 ||
                OverTimeEngine<Dummy>.PulseCountFor(4f, 0f) != 0)
                failures.Add("[ceil] a zero duration or a zero interval did not resolve to 0 pulses");

            notes.Add("ceil: 4.5s/1s = 5 pulses (shipped over-delivery preserved); 4s and 10s exact");
        }

        // =====================================================================
        //  3 [death] - a DoT must never hit a corpse.
        // =====================================================================
        private static void Case3_StopsOnDeath(List<string> failures, List<string> notes)
        {
            RemoteTunables.Clear();
            var engine = new OverTimeEngine<Dummy>(LivenessOf);
            var foe = new Dummy { Hp = 20f };

            // NOTE THE CALLS: a clock and a sink, and NOTHING ELSE. That is deliberate and
            // it is the whole point of this case - it is written the way a future call site
            // will be written by someone who has never read this file. Liveness is the
            // ENGINE's invariant (bound at construction, see LivenessOf), never something a
            // caller has to remember. When it was an optional Advance argument these exact
            // three lines delivered 3 pulses and leaked the entry.
            engine.Apply(foe, "burn", 8f, 10f, OverTimeKind.Damage, now: 0f);
            engine.Advance(1f, Sink(foe));
            engine.Advance(2f, Sink(foe));
            foe.Alive = false;                       // the second pulse killed it
            engine.Advance(3f, Sink(foe));

            if (foe.Pulses != 2)
                failures.Add("[death] the effect delivered " + foe.Pulses + " pulses; it must stop at 2, " +
                             "the moment the target stopped being alive. The shipped coroutine's " +
                             "'if (!target.IsAlive) yield break' is load-bearing - a DoT that keeps " +
                             "ticking a corpse credits kills and damage to a foe that no longer exists.");
            if (engine.ActiveCount != 0)
                failures.Add("[death] the dead target's effect is still in flight (" + engine.ActiveCount +
                             ") - the engine leaks an entry per dead foe, which on a wave is a real leak.");

            // The same must hold for a target the caller has dropped entirely.
            var gone = new Dummy();
            var e2 = new OverTimeEngine<Dummy>(LivenessOf);
            e2.Apply(gone, "burn", 5f, 5f, OverTimeKind.Damage, now: 0f);
            if (e2.CancelAll(gone) != 1 || e2.ActiveCount != 0)
                failures.Add("[death] CancelAll did not remove the target's effects");

            notes.Add("death: pulses stop on the frame the target dies, and the entry is released");
        }

        // =====================================================================
        //  4 [sign] - ONE mechanism, both directions.
        // =====================================================================
        private static void Case4_BothSignsOneEngine(List<string> failures, List<string> notes)
        {
            RemoteTunables.Clear();

            var healEngine = new OverTimeEngine<Dummy>(LivenessOf);
            var hero = new Dummy { Hp = 10f };

            // knight.ironblood's authored numbers: 4 HP/s for 12s = 48 total, 12 pulses.
            int promised = healEngine.Apply(hero, "knight.ironblood", 4f, 12f, OverTimeKind.Heal, now: 0f);
            if (promised != 12)
                failures.Add("[sign] Ironblood promised " + promised + " pulses, expected 12 (4 HP/s x 12s)");

            var kinds = new List<OverTimeKind>();
            bool negative = false;
            for (int step = 1; step <= 12; step++)
            {
                healEngine.Advance(step, p =>
                {
                    kinds.Add(p.Kind);
                    if (p.Amount < 0f) negative = true;
                    hero.Hp += p.Amount;
                    hero.Pulses++;
                    hero.Total += p.Amount;
                });
            }

            if (hero.Pulses != 12)
                failures.Add("[sign] the regen delivered " + hero.Pulses + " pulses, expected 12");
            if (Math.Abs(hero.Total - 48f) > 0.001f)
                failures.Add("[sign] the regen restored " + hero.Total + " HP, expected 48");
            if (negative)
                failures.Add("[sign] a pulse reported a NEGATIVE magnitude. Direction lives in " +
                             "OverTimeKind precisely so no call site can heal by passing negative damage " +
                             "or damage by passing a negative heal - the classic sign bug this design " +
                             "exists to make impossible.");
            foreach (var k in kinds)
                if (k != OverTimeKind.Heal)
                {
                    failures.Add("[sign] a Heal effect reported a pulse of kind " + k +
                                 " - the knight's regen would DAMAGE him.");
                    break;
                }

            // And the damage sign, from the same class, in the same run.
            var dmgEngine = new OverTimeEngine<Dummy>(LivenessOf);
            var foe = new Dummy();
            dmgEngine.Apply(foe, "burn", 8f, 2f, OverTimeKind.Damage, now: 0f);
            var seen = OverTimeKind.Heal;
            dmgEngine.Advance(1f, p => seen = p.Kind);
            if (seen != OverTimeKind.Damage)
                failures.Add("[sign] a Damage effect reported kind " + seen);

            notes.Add("sign: one engine served a 12-pulse heal and a damage pulse in the same run, " +
                      "magnitudes always positive");
        }

        // =====================================================================
        //  5 [invariant] - the cadence knob is a FEEL dial, never a damage dial.
        // =====================================================================
        private static void Case5_CadenceDoesNotMoveTotal(List<string> failures, List<string> notes)
        {
            float slowTotal = RunTotal(1000, 8f, 4f, out int slowPulses);
            float fastTotal = RunTotal(250, 8f, 4f, out int fastPulses);
            RemoteTunables.Clear();

            if (slowPulses != 4)
                failures.Add("[invariant] at the shipping 1000ms cadence an 8dps/4s effect delivered " +
                             slowPulses + " pulses, expected 4");
            if (fastPulses != 16)
                failures.Add("[invariant] at 250ms the same effect delivered " + fastPulses +
                             " pulses, expected 16");
            if (Math.Abs(slowTotal - fastTotal) > 0.01f)
                failures.Add("[invariant] quadrupling the pulse rate changed TOTAL delivery from " +
                             slowTotal + " to " + fastTotal + ". combat.overTimeTickMs is documented and " +
                             "handed to the owner as a FEEL dial - 'how the effect reads'. If it also " +
                             "moves damage, then every felt-test of the rhythm is silently a balance " +
                             "change, and she would be tuning two things with one control.");

            notes.Add("invariant: 1000ms -> 4 pulses and 250ms -> 16 pulses both total " +
                      slowTotal.ToString("0.##"));
        }

        private static float RunTotal(int tickMs, float perSecond, float seconds, out int pulses)
        {
            RemoteTunables.Clear();
            RemoteTunables.ApplyPayload(Payload("combat.overTimeTickMs", tickMs.ToString()), "over-time-test");
            var engine = new OverTimeEngine<Dummy>(LivenessOf);
            var d = new Dummy();
            engine.Apply(d, "burn", perSecond, seconds, OverTimeKind.Damage, now: 0f);
            // Advance well past the window in fine steps so no pulse is missed or doubled.
            for (int i = 1; i <= 200; i++) engine.Advance(i * 0.05f, Sink(d));
            pulses = d.Pulses;
            return d.Total;
        }

        // =====================================================================
        //  6 [tunable] - defaults are today; a legal row actually moves it.
        // =====================================================================
        private static void Case6_KnobsMoveIt(List<string> failures, List<string> notes)
        {
            RemoteTunables.Clear();

            // THE INVARIANT THAT OUTRANKS THE FEATURE. No row, no network, no parse =>
            // the numbers knight.emberbrand-throw shipped with, exactly.
            var baseline = new OverTimeEngine<Dummy>(LivenessOf);
            var d0 = new Dummy();
            baseline.Apply(d0, "burn", 8f, 4f, OverTimeKind.Damage, now: 0f);
            for (int i = 1; i <= 8; i++) baseline.Advance(i, Sink(d0));
            if (d0.Pulses != 4 || Math.Abs(d0.Total - 32f) > 0.001f)
                failures.Add("[tunable] with an EMPTY table the shipped 8dps/4s burn delivered " +
                             d0.Pulses + " pulses totalling " + d0.Total + ", not 4 x 8 = 32. An offline " +
                             "player, a 404 and a malformed row must all get exactly the build's numbers.");

            // Magnitude: a legal 50 halves every pulse and leaves the count alone.
            RemoteTunables.Clear();
            RemoteTunables.ApplyPayload(Payload("combat.overTimeMagnitudePct", "50"), "over-time-test");
            var half = new OverTimeEngine<Dummy>(LivenessOf);
            var d1 = new Dummy();
            half.Apply(d1, "burn", 8f, 4f, OverTimeKind.Damage, now: 0f);
            for (int i = 1; i <= 8; i++) half.Advance(i, Sink(d1));
            if (d1.Pulses != 4 || Math.Abs(d1.Total - 16f) > 0.001f)
                failures.Add("[tunable] a legal magnitude row of 50 gave " + d1.Pulses + " pulses " +
                             "totalling " + d1.Total + ", expected 4 pulses totalling 16. A knob that " +
                             "only proves its refusals has proved nothing - the owner would flip it, see " +
                             "no change, and conclude the build was wrong.");

            // Duration: a legal 200 doubles the window, therefore the pulse count.
            RemoteTunables.Clear();
            RemoteTunables.ApplyPayload(Payload("combat.overTimeDurationPct", "200"), "over-time-test");
            var longer = new OverTimeEngine<Dummy>(LivenessOf);
            var d2 = new Dummy();
            int promised = longer.Apply(d2, "burn", 8f, 4f, OverTimeKind.Damage, now: 0f);
            if (promised != 8)
                failures.Add("[tunable] a legal duration row of 200 promised " + promised +
                             " pulses, expected 8 (the 4s window doubled to 8s at a 1s cadence)");

            // And back. -Clear is the one-word way to today's behaviour.
            RemoteTunables.Clear();
            var restored = new OverTimeEngine<Dummy>(LivenessOf);
            var d3 = new Dummy();
            restored.Apply(d3, "burn", 8f, 4f, OverTimeKind.Damage, now: 0f);
            for (int i = 1; i <= 8; i++) restored.Advance(i, Sink(d3));
            if (d3.Pulses != 4 || Math.Abs(d3.Total - 32f) > 0.001f)
                failures.Add("[tunable] after clearing the table the burn did not return to the shipped " +
                             "4 x 8 = 32; it gave " + d3.Pulses + " x total " + d3.Total +
                             ". An experiment that cannot be fully undone is not an experiment.");

            notes.Add("tunable: empty table = the shipped 4x8=32 burn; 50% and 200% both moved it; " +
                      "clear restored it");
        }

        // =====================================================================
        //  7 [wiring] - authored is not the same as reachable.
        // =====================================================================
        private static void Case7_AbilitiesAreReachable(List<string> failures, List<string> notes)
        {
            string resAb = ReadOrNull(ResAbilities), saAb = ReadOrNull(SaAbilities);
            string resTal = ReadOrNull(ResTalents), saTal = ReadOrNull(SaTalents);
            if (resAb == null || saAb == null || resTal == null || saTal == null)
            {
                failures.Add("[wiring] one of the four canonical data files is MISSING");
                return;
            }

            string dispatch = ReadOrNull(AbilitiesSrc);
            if (dispatch == null) { failures.Add("[wiring] " + AbilitiesSrc + " is MISSING"); return; }

            foreach (var row in NewAbilities)
            {
                string id = row[0], effect = row[1], node = row[2];

                if (resAb.IndexOf("\"" + id + "\"", StringComparison.Ordinal) < 0)
                    failures.Add("[wiring] '" + id + "' is absent from " + ResAbilities);
                if (saAb.IndexOf("\"" + id + "\"", StringComparison.Ordinal) < 0)
                    failures.Add("[wiring] '" + id + "' is absent from the StreamingAssets twin. The two " +
                                 "copies must stay byte-equal or the built player and the editor disagree " +
                                 "about what the game contains.");

                // THE TRAP THE WORK ORDER NAMED: an authored effect string with no runtime
                // consumer is indistinguishable from a field that does not exist.
                if (!Regex.IsMatch(dispatch, "case\\s+\"" + Regex.Escape(effect.ToLowerInvariant()) + "\"\\s*:"))
                    failures.Add("[wiring] '" + id + "' authors effect '" + effect + "', but " +
                                 AbilitiesSrc + " has NO 'case \"" + effect.ToLowerInvariant() +
                                 "\":' in its effect dispatch. The ability would silently fall through " +
                                 "to the enum switch and behave like a plain Strike - authored data " +
                                 "proves intent, never a consumer.");

                // A spell nobody can unlock is a spell nobody can press.
                if (resTal.IndexOf("\"" + id + "\"", StringComparison.Ordinal) < 0)
                    failures.Add("[wiring] no talent node in " + ResTalents + " grants '" + id +
                                 "'. It is authored, has a consumer, and is unreachable.");
                if (resTal.IndexOf("\"" + node + "\"", StringComparison.Ordinal) < 0)
                    failures.Add("[wiring] the granting node '" + node + "' is absent from " + ResTalents);
                if (saTal.IndexOf("\"" + id + "\"", StringComparison.Ordinal) < 0)
                    failures.Add("[wiring] no talent node in the StreamingAssets twin grants '" + id + "'");
            }

            notes.Add("wiring: both new abilities present in both copies, both effect strings have a " +
                      "live dispatch case, both granted by a node");
        }

        // =====================================================================
        //  8 [owner-tag] - the ONE open slot stays open.
        // =====================================================================
        private static void Case8_VfxKeysHeldEmpty(List<string> failures, List<string> notes)
        {
            string json = ReadOrNull(ResAbilities);
            if (json == null) { failures.Add("[owner-tag] " + ResAbilities + " is MISSING"); return; }

            foreach (var row in NewAbilities)
            {
                string id = row[0];
                int at = json.IndexOf("\"" + id + "\"", StringComparison.Ordinal);
                if (at < 0) continue;                       // case 7 already reported it
                int end = json.IndexOf("\"id\":", at + 8, StringComparison.Ordinal);
                string block = end > at ? json.Substring(at, end - at) : json.Substring(at);

                if (id == "mage.wither" &&
                    !Regex.IsMatch(block, "\"vfxCast\"\\s*:\\s*\"PosionCloud_Cast\""))
                    failures.Add("[owner-tag] mage.wither must retain the owner's 2026-09-09 distinct cast pick PosionCloud_Cast");

                foreach (var field in new[] { "vfxCast", "vfxProjectile", "vfxImpact", "vfxResidual" })
                {
                    if (id == "mage.wither" && field == "vfxCast") continue;
                    if (Regex.IsMatch(block, "\"" + field + "\"\\s*:\\s*\"[^\"]+\""))
                        failures.Add("[owner-tag] '" + id + "' has a NON-EMPTY " + field + ". The owner " +
                                     "tags VFX keys and this seat maps them verbatim - it never picks, " +
                                     "substitutes or improves one. A key here that she did not tag is a " +
                                     "creative pick the CLI is not allowed to make. If she HAS now tagged " +
                                     "it, add the id to this rule's exemption in the same commit as the " +
                                     "key, so the tag is recorded rather than assumed.");
                }
            }

            notes.Add("owner-tag: mage.wither owns PosionCloud_Cast by the 2026-09-09 ruling; other untagged stages remain empty");
        }

        // =====================================================================
        //  9 [one-loop] - two abilities, ONE mechanism.
        // =====================================================================
        private static void Case9_NoSurvivingTickLoop(List<string> failures, List<string> notes)
        {
            string src = ReadOrNull(AbilitiesSrc);
            if (src == null) { failures.Add("[one-loop] " + AbilitiesSrc + " is MISSING"); return; }
            string code = StripComments(src);

            // Comments are stripped because this file's own documentation quotes the old
            // "const float tick = 1f" in prose, on purpose, to record what was replaced -
            // and a comment-reading lint would red on the very sentences proving compliance.
            if (Regex.IsMatch(code, @"const\s+float\s+tick\s*=\s*1f"))
                failures.Add("[one-loop] " + AbilitiesSrc + " has re-grown a hardcoded 'const float " +
                             "tick = 1f'. That is the duplicated coroutine WO-1330 collapsed onto the " +
                             "shared engine, and re-adding it also takes the effect back off the " +
                             "tunable rail.");

            if (code.IndexOf("OverTimeEngine", StringComparison.Ordinal) < 0)
                failures.Add("[one-loop] " + AbilitiesSrc + " no longer references OverTimeEngine at all " +
                             "- the DoTs and the regen have been re-pointed off the shared mechanism");

            if (code.IndexOf("TickOverTimeEffects", StringComparison.Ordinal) < 0)
                failures.Add("[one-loop] " + AbilitiesSrc + " no longer drives TickOverTimeEffects from " +
                             "Update. The engine would hold effects that never tick, which is the ONE " +
                             "failure mode this whole suite exists to make impossible.");

            notes.Add("one-loop: no hardcoded tick constant survives; the engine is referenced and driven");
        }

        // =====================================================================
        //  helpers
        // =====================================================================

        private static Action<OverTimePulse<Dummy>> Sink(Dummy d)
            => p => { d.Pulses++; d.Total += p.Amount; d.Hp -= p.Amount; };

        /// <summary>
        /// The liveness test every engine in this suite is CONSTRUCTED with.
        /// <para>
        /// ⭐ THIS IS NOT A WEAKENING OF CASE 3, IT IS THE FIX CASE 3 FORCED. The engine
        /// first shipped taking liveness as an OPTIONAL argument to Advance. Case 3 called
        /// Advance the way a future call site would - without it - and caught the engine
        /// ticking a corpse (3 pulses where 2 were due) and leaking the dead entry. The
        /// answer was NOT to teach this suite to pass the argument, which would have
        /// deleted the only coverage of the real hazard; it was to make the argument
        /// impossible to omit by binding it to the constructor. Case 3 still calls Advance
        /// with nothing but a clock and a sink, and still asserts the pulses stop - it is
        /// the ENGINE that changed. If a future edit ever adds an Advance overload that
        /// skips this test, case 3 goes red again, exactly as it should.
        /// </para>
        /// </summary>
        private static bool LivenessOf(Dummy d) => d != null && d.Alive;

        // Mirrors RemoteTunablesDefaultsRegression's helper exactly - the wire field is
        // "readOk", not "ok", and a payload with the wrong shape is REFUSED, which would
        // make every knob silently answer its default and this suite pass for the wrong
        // reason. That is precisely the false green a test double must never produce.
        private static string Payload(string key, string value)
            => "{\"version\":1,\"readOk\":true,\"reason\":\"test\",\"values\":{\"" +
               key + "\":\"" + value + "\"}}";

        private static string ReadOrNull(string path)
        {
            try { return File.Exists(path) ? File.ReadAllText(path) : null; }
            catch { return null; }
        }

        private static string StripComments(string src)
        {
            if (string.IsNullOrEmpty(src)) return string.Empty;
            string s = Regex.Replace(src, @"/\*.*?\*/", " ", RegexOptions.Singleline);
            s = Regex.Replace(s, @"//[^\n]*", " ");
            return s;
        }
    }
}

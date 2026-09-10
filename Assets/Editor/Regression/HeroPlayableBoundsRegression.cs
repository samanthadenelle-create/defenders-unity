// =============================================================================
// HeroPlayableBoundsRegression — WO-1094. TWO defects, one file, one suite.
// -----------------------------------------------------------------------------
// Assembly: DeNelle.EditorRegression. Shape: public static bool Run(out string reason)
// — registered into DataRegression.RunAll by the orchestrator.
//
// THE TWO DEFECTS THIS PINS (both read at source on HEAD 184c8ff06, 2026-09-09):
//
//  1. THE BOUND WAS A LITERAL FROM A WORLD THAT NO LONGER EXISTS.
//     `const float PlayableHalf = 50f` clamped the hero into a ±50 box while the home
//     hub is a MERGED 1000x1000 world. The WO-1091 capture caught it moving a
//     deliberately-warped hero from (-400,0) to (-50,0) on one tick. The bound is now
//     MEASURED from the world authority (BiomeRoads.TryMeasureWorldBounds, which reads
//     the live Terrain extent and REFUSES rather than typing a fallback).
//
//  2. THE TELEPORT GUARD PROTECTED ZERO FRAMES.
//     WarpTo raised `_isTeleporting` and lowered it inside the SAME synchronous call,
//     so the clamp — which runs in Update — never saw it raised. The guard is now a
//     FRAME STAMP spanning the warp frame and the next Update.
//
// -----------------------------------------------------------------------------
// HONEST SCOPE (WO-1494: six suites claimed to MEASURE and were source text lint).
// THIS SUITE IS HALF BEHAVIOUR, HALF LINT, AND EACH REASON STRING SAYS WHICH.
//   • [clamp-math] and [teleport-span] are REAL BEHAVIOUR: they call the pure statics
//     HeroLocomotion.ClampToPlayableBounds / TeleportGuardHeld / TeleportGuardEndFrame
//     with real values and assert the real answers. No scene, no PlayMode, no Terrain.
//   • [no-typed-bound], [warp-stamps-guard] and [warn-names-source] are SOURCE LINTS.
//     They cannot prove the running hero is not clamped — that is the owner's felt-test
//     at the far edges of the overworld (WO-1094 acceptance, last box). What a lint CAN
//     do is stop the literal and the stamp being quietly reintroduced by a later edit,
//     which is precisely how a ±50 survived a world getting 20x bigger.
//
// RED-FIRST, and the proof is mechanical rather than asserted: on HEAD 184c8ff06 this
// file does not COMPILE — HeroLocomotion declares none of the three statics it calls —
// and every lint below is written against a string that HEAD still contains
// ("PlayableHalf") or still lacks ("_teleportGuardUntilFrame"). Both halves fail before
// the fix and pass after it.
//
// ASCII only outside comments. Never strip the FlowTrace assertions.
// =============================================================================
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using DeNelle.Village;

namespace DeNelle.Editor.Regression
{
    public static class HeroPlayableBoundsRegression
    {
        private const string HeroLocoPath = "Assets/_Modules/Village/Hero/HeroLocomotion.cs";

        // Declared as a PAIR so this file's own brace tally stays balanced — CLAUDE.md §1
        // runs a naive open-vs-close count over every .cs, and a lone open-brace char literal
        // in the body matcher below would read to that gate as a missing close.
        private const char OpenBrace = '{', CloseBrace = '}';

        /// <summary>The merged home world, as seated in Main_Castle_Overworld: terrain origin
        /// (-500,-4,-500), size 1000x42x1000. Used ONLY as regression input — the shipping code
        /// measures it and must never carry a copy of these numbers.</summary>
        private static Bounds MergedWorldBounds()
            => new Bounds(new Vector3(0f, 17f, 0f), new Vector3(1000f, 42f, 1000f));

        public static bool Run(out string reason)
        {
            var failures = new List<string>();
            var notes = new List<string>();

            string src = null;
            try { src = File.Exists(HeroLocoPath) ? File.ReadAllText(HeroLocoPath) : null; }
            catch (Exception ex) { failures.Add("[hero-playable-bounds] could not read " + HeroLocoPath + ": " + ex.Message); }

            if (src == null)
                failures.Add("[hero-playable-bounds] " + HeroLocoPath + " not found — the suite cannot lint what it cannot open.");

            // ── 1. BEHAVIOUR: the clamp, against the real merged-world extent ────────────
            //    Defect 1's exact captured case: a hero legitimately at x=-400 must SURVIVE.
            {
                Bounds world = MergedWorldBounds();
                Vector3 far = new Vector3(-400f, 17f, 0f);
                Vector3 keptResult = HeroLocomotion.ClampToPlayableBounds(far, world);
                if (Math.Abs(keptResult.x - far.x) > 0.0001f || Math.Abs(keptResult.z - far.z) > 0.0001f)
                    failures.Add("[hero-playable-bounds][clamp-math] BEHAVIOUR: a hero at " + far +
                                 " inside the measured 1000x1000 world was RELOCATED to " + keptResult +
                                 " — this is WO-1094 defect 1 (the castle-era +/-50 literal) reappearing.");
                else
                    notes.Add("[clamp-math] BEHAVIOUR: (-400,0) survives the measured 1000x1000 bound (the WO-1091 captured case).");

                // ...and the guard's ACTUAL JOB still works: genuinely outside the world is recovered.
                Vector3 outside = new Vector3(-4000f, 17f, 6000f);
                Vector3 recovered = HeroLocomotion.ClampToPlayableBounds(outside, world);
                if (Math.Abs(recovered.x - world.min.x) > 0.0001f || Math.Abs(recovered.z - world.max.z) > 0.0001f)
                    failures.Add("[hero-playable-bounds][clamp-math] BEHAVIOUR: a hero at " + outside +
                                 " (far outside the world) was left at " + recovered +
                                 " instead of being pulled to the measured edge (" + world.min.x + "," + world.max.z +
                                 ") — widening the bound must not DISABLE the off-mesh guard.");
                else
                    notes.Add("[clamp-math] BEHAVIOUR: a genuinely out-of-world hero is still recovered to the measured edge.");

                // Asymmetry: the bound is a Bounds, not a half-extent. An off-centre world must
                // clamp to its real min/max, or a centred-on-origin assumption has crept back in.
                Bounds offCentre = new Bounds(new Vector3(200f, 0f, -100f), new Vector3(100f, 10f, 100f));
                Vector3 offResult = HeroLocomotion.ClampToPlayableBounds(new Vector3(0f, 0f, 0f), offCentre);
                if (Math.Abs(offResult.x - 150f) > 0.0001f || Math.Abs(offResult.z - (-50f)) > 0.0001f)
                    failures.Add("[hero-playable-bounds][clamp-math] BEHAVIOUR: an OFF-CENTRE world " +
                                 "x[150..250] z[-150..-50] clamped (0,0) to " + offResult +
                                 " instead of (150,-50) — the clamp is assuming a world centred on the " +
                                 "origin, which is a typed assumption by the back door.");
                else
                    notes.Add("[clamp-math] BEHAVIOUR: an off-centre world clamps to its real min/max, not a symmetric half.");
            }

            // ── 1b. BEHAVIOUR: AN UNMEASURABLE SCENE STILL CLAMPS ───────────────────────
            //    Lead ruling 2026-09-09. Village2, RaidBase_IronBastion and the legacy
            //    MainCastle_Hall carry ZERO Terrain components (counted in the scene files), so
            //    "unmeasurable" is not a corner case — it is the raid loop. A derived-only bound
            //    would mean NO bound there, and this component's off-mesh path is
            //    `transform.position += step`, so the hero drifts without a ceiling. KEY_FACTS:
            //    no measurement => TODAY'S BEHAVIOUR, EXACTLY.
            {
                // The rail's DEFAULT must be the bound this build shipped. Read from the Registry
                // const, not retyped, so this asserts the two agree rather than restating one.
                int railDefault = DeNelle.Core.Ops.RemoteTunables.HeroPlayableFallbackHalfDefault;
                if (railDefault != 50)
                    failures.Add("[hero-playable-bounds][fallback-clamp] BEHAVIOUR: the rail default for " +
                                 DeNelle.Core.Ops.RemoteTunables.KeyHeroPlayableFallbackHalf + " is " + railDefault +
                                 ", not 50. The invariant is 'no row, no measurement => today's behaviour EXACTLY', " +
                                 "and today's behaviour was the +/-50 box this build shipped. Changing the SHIPPED " +
                                 "bound is an owner ruling, not a default edit.");
                else
                    notes.Add("[fallback-clamp] BEHAVIOUR: the rail default is 50 — an empty tunables table reproduces the shipped bound.");

                Bounds fb = HeroLocomotion.FallbackPlayableBounds(railDefault);
                Vector3 clamped = HeroLocomotion.ClampToPlayableBounds(new Vector3(-400f, 17f, 0f), fb);
                if (Math.Abs(clamped.x - (-50f)) > 0.0001f)
                    failures.Add("[hero-playable-bounds][fallback-clamp] BEHAVIOUR: in an UNMEASURABLE scene a hero " +
                                 "at x=-400 resolved to x=" + clamped.x + " instead of -50. Either the fallback bound " +
                                 "is gone (the hero now drifts unbounded through every raid base — the lead's rework " +
                                 "item) or it is no longer today's number.");
                else
                    notes.Add("[fallback-clamp] BEHAVIOUR: an unmeasurable scene still clamps, and at exactly the shipped +/-50.");

                // A console typo must never pin every unmeasured scene's hero to the origin.
                Bounds zeroed = HeroLocomotion.FallbackPlayableBounds(0);
                Vector3 nearOrigin = HeroLocomotion.ClampToPlayableBounds(new Vector3(0.5f, 0f, 0f), zeroed);
                if (Math.Abs(nearOrigin.x - 0.5f) > 0.0001f)
                    failures.Add("[hero-playable-bounds][fallback-clamp] BEHAVIOUR: a rail value of 0 collapsed the " +
                                 "fallback box to the origin (x=0.5 became " + nearOrigin.x + "). The clamp floor of 1 " +
                                 "exists so one bad database row cannot freeze the hero in every unmeasured scene.");
                else
                    notes.Add("[fallback-clamp] BEHAVIOUR: a rail value of 0 is floored at 1 — a bad row cannot pin the hero to the origin.");

                // The MEASURED extent must still win where it exists: the fallback is a floor,
                // never a competitor. 1000x1000 keeps (-400); the fallback would have moved it.
                Vector3 hubKept = HeroLocomotion.ClampToPlayableBounds(new Vector3(-400f, 17f, 0f), MergedWorldBounds());
                if (Math.Abs(hubKept.x - (-400f)) > 0.0001f)
                    failures.Add("[hero-playable-bounds][fallback-clamp] BEHAVIOUR: adding the fallback has changed the " +
                                 "MEASURED path — (-400) no longer survives the 1000x1000 world. The fallback applies " +
                                 "only where nothing can be measured.");
                else
                    notes.Add("[fallback-clamp] BEHAVIOUR: the measured extent still wins where it exists — the fallback is a floor, not a competitor.");
            }

            // ── 2. BEHAVIOUR: the teleport guard spans the frames a warp needs ───────────
            {
                const int warpFrame = 1000;
                int until = HeroLocomotion.TeleportGuardEndFrame(warpFrame);

                if (!HeroLocomotion.TeleportGuardHeld(false, warpFrame, until))
                    failures.Add("[hero-playable-bounds][teleport-span] BEHAVIOUR: the guard is DOWN on the " +
                                 "warp frame itself (" + warpFrame + ", until=" + until + ") — the agent " +
                                 "disable/warp/re-enable frame is unprotected.");
                else
                    notes.Add("[teleport-span] BEHAVIOUR: guard holds on the warp frame.");

                if (!HeroLocomotion.TeleportGuardHeld(false, warpFrame + 1, until))
                    failures.Add("[hero-playable-bounds][teleport-span] BEHAVIOUR: the guard is DOWN on the " +
                                 "frame AFTER the warp (" + (warpFrame + 1) + ", until=" + until + "). This is " +
                                 "WO-1094 defect 2 exactly: the old bool was cleared inside WarpTo, so the " +
                                 "next Update clamped the landing away.");
                else
                    notes.Add("[teleport-span] BEHAVIOUR: guard still holds on the next Update — the frame the old bool lost.");

                if (HeroLocomotion.TeleportGuardHeld(false, warpFrame + 2, until))
                    failures.Add("[hero-playable-bounds][teleport-span] BEHAVIOUR: the guard is STILL UP two " +
                                 "frames after the warp (" + (warpFrame + 2) + ", until=" + until + ") — a guard " +
                                 "that never expires disables the off-mesh recovery permanently, which is the " +
                                 "opposite failure and just as bad.");
                else
                    notes.Add("[teleport-span] BEHAVIOUR: guard expires two frames after the warp — it is a span, not a latch-forever.");

                if (!HeroLocomotion.TeleportGuardHeld(true, warpFrame + 500, until))
                    failures.Add("[hero-playable-bounds][teleport-span] BEHAVIOUR: the SPAN latch " +
                                 "(_isTeleporting, raised by BeginSeamCross and lowered frames later at the " +
                                 "seam-cross DONE branch) no longer holds the guard up. The frame stamp is " +
                                 "ADDITIVE to the span, never a replacement for it.");
                else
                    notes.Add("[teleport-span] BEHAVIOUR: the seam-slide span latch still holds the guard independently of the stamp.");
            }

            // ── 3. LINT: no typed bound survives in the shipping file ────────────────────
            if (src != null)
            {
                if (src.Contains("PlayableHalf"))
                    failures.Add("[hero-playable-bounds][no-typed-bound] SOURCE LINT: " + HeroLocoPath +
                                 " still mentions 'PlayableHalf'. WO-1094 defect 1 is a TYPED bound in a " +
                                 "measured world; a second literal — even a bigger one — is the same defect " +
                                 "with a later expiry date (CLAUDE.md §8, the RepoProps.MaxStructureLevel lesson).");
                else
                    notes.Add("[no-typed-bound] SOURCE LINT: no 'PlayableHalf' literal remains.");

                if (!src.Contains("BiomeRoads.TryMeasureWorldBounds"))
                    failures.Add("[hero-playable-bounds][no-typed-bound] SOURCE LINT: " + HeroLocoPath +
                                 " no longer reads the world authority (DeNelle.Core.World.BiomeRoads." +
                                 "TryMeasureWorldBounds). The clamp bound must be MEASURED, and there is " +
                                 "exactly one measurer.");
                else
                    notes.Add("[no-typed-bound] SOURCE LINT: the bound is read from BiomeRoads.TryMeasureWorldBounds.");

                // The CACHE KEY is load-bearing. The town hero travels DontDestroyOnLoad
                // (SceneRouter.cs:666-676) and is re-homed by a Guard-wrapped
                // MoveGameObjectToScene that can FAIL (HeroControlEnsurer.cs:244-247). Keyed on
                // gameObject.scene the handle can stop changing, and the extent measured in one
                // world would then be enforced in the next — this ticket's defect, renumbered.
                if (!TryExtractBody(src, "private Bounds ResolvePlayableBounds(out string source)", out string resolverBody))
                {
                    failures.Add("[hero-playable-bounds][no-typed-bound] SOURCE LINT: could not locate the body of " +
                                 "ResolvePlayableBounds in " + HeroLocoPath + " — the bounds resolver is gone or renamed.");
                }
                else
                {
                    if (!resolverBody.Contains("SceneManager.GetActiveScene()"))
                        failures.Add("[hero-playable-bounds][no-typed-bound] SOURCE LINT: the bounds cache is not keyed " +
                                     "on the ACTIVE scene. The hero travels DontDestroyOnLoad, so gameObject.scene is " +
                                     "the DDOL scene mid-crossing (and permanently if the re-home fails) — a handle that " +
                                     "stops changing carries one world's extent into the next.");
                    else
                        notes.Add("[no-typed-bound] SOURCE LINT: the bounds cache is keyed on the ACTIVE scene, not the DDOL-prone gameObject.scene.");

                    // Probed ONCE per scene on purpose: BiomeRoads' miss is a FlowTrace.Fail, which
                    // routes to Sink.Error (FlowTrace.cs:169-172) — the severity break-log records and
                    // the F8 daemon wakes on. A retry timer would fire a live ERROR at the owner on a
                    // loop in every terrain-less scene (Village2, RaidBase_*, MainCastle_Hall all
                    // carry zero Terrain components).
                    if (resolverBody.Contains("realtimeSinceStartup") || resolverBody.Contains("Reprobe"))
                        failures.Add("[hero-playable-bounds][no-typed-bound] SOURCE LINT: the bounds resolver has " +
                                     "regained a RETRY TIMER. BiomeRoads.TryMeasureWorldBounds emits FlowTrace.Fail " +
                                     "(-> Sink.Error) on a miss, so a timed re-probe manufactures a looping false " +
                                     "capture at the owner in every terrain-less scene. Probe once per active scene.");
                    else
                        notes.Add("[no-typed-bound] SOURCE LINT: the extent is probed once per active scene — no retry timer, no looping false F8 capture.");
                }
            }

            // ── 4. LINT: WarpTo stamps the guard forward ─────────────────────────────────
            if (src != null)
            {
                if (!TryExtractBody(src, "public void WarpTo(Vector3 worldPos, Quaternion? rot = null)", out string warpBody))
                {
                    failures.Add("[hero-playable-bounds][warp-stamps-guard] SOURCE LINT: could not locate the " +
                                 "body of 'public void WarpTo(Vector3 worldPos, Quaternion? rot = null)' in " +
                                 HeroLocoPath + ". NOTE: that signature is load-bearing beyond this suite — " +
                                 "BattleArena.WarpHero resolves it by EXACT-signature reflection.");
                }
                else
                {
                    if (!warpBody.Contains("_teleportGuardUntilFrame = TeleportGuardEndFrame("))
                        failures.Add("[hero-playable-bounds][warp-stamps-guard] SOURCE LINT: WarpTo does not stamp " +
                                     "_teleportGuardUntilFrame forward. Without the stamp the guard is lowered " +
                                     "inside the same synchronous call that raised it and protects ZERO frames — " +
                                     "WO-1094 defect 2, restored.");
                    else
                        notes.Add("[warp-stamps-guard] SOURCE LINT: WarpTo stamps the guard forward before returning.");

                    if (!warpBody.Contains("FlowTrace.Step"))
                        failures.Add("[hero-playable-bounds][warp-stamps-guard] SOURCE LINT: WarpTo carries no " +
                                     "FlowTrace.Step. CLAUDE.md §12: instrumentation is PERMANENT — a warp that " +
                                     "does not announce itself is how this defect stayed invisible.");
                    else
                        notes.Add("[warp-stamps-guard] SOURCE LINT: WarpTo still traces.");
                }
            }

            // ── 5. LINT: the clamp warn names the bound AND where it came from ───────────
            if (src != null)
            {
                bool namesBound  = src.Contains("playable-bounds CLAMP relocated the hero");
                bool namesSource = src.Contains("source=") && src.Contains("bound x[");

                if (!namesBound)
                    failures.Add("[hero-playable-bounds][warn-names-source] SOURCE LINT: the clamp-relocation " +
                                 "FlowTrace.Warn is gone. A relocation that does not announce itself is an " +
                                 "unattributable teleport in a capture — the exact thing the ~7km-to-(50,50) " +
                                 "jump was before it was instrumented.");
                else if (!namesSource)
                    failures.Add("[hero-playable-bounds][warn-names-source] SOURCE LINT: the clamp-relocation Warn " +
                                 "does not name BOTH the bound it enforced and its SOURCE (expected 'bound x[' " +
                                 "and 'source='). WO-1094 fix spec item 5: naming the value alone is why +/-50 " +
                                 "read as intentional.");
                else
                    notes.Add("[warn-names-source] SOURCE LINT: the relocation Warn names the enforced bound and its source.");

                // The trace must distinguish WHICH bound applied. "source=" alone is not enough:
                // a capture from a raid base and a capture from the hub would read identically.
                if (!src.Contains("fallback-tunable"))
                    failures.Add("[hero-playable-bounds][warn-names-source] SOURCE LINT: the resolver never labels a " +
                                 "bound 'fallback-tunable'. A capture must say whether the clamp that moved the hero " +
                                 "was the MEASURED world extent or the rail's fallback — otherwise a raid-base " +
                                 "relocation and a hub relocation read identically in break-log.");
                else
                    notes.Add("[warn-names-source] SOURCE LINT: the trace labels the fallback bound 'fallback-tunable', distinct from 'measured-terrain'.");

                if (!src.Contains("measured-terrain"))
                    failures.Add("[hero-playable-bounds][warn-names-source] SOURCE LINT: the resolver never labels a " +
                                 "bound 'measured-terrain' — the other half of the same distinction.");
                else
                    notes.Add("[warn-names-source] SOURCE LINT: the measured bound is labelled 'measured-terrain'.");
            }

            if (failures.Count > 0)
            {
                reason = "hero-playable-bounds FAIL (" + failures.Count + "): " + string.Join(" | ", failures);
                return false;
            }

            reason = "hero-playable-bounds OK (" + notes.Count + " checks; behaviour + source lint, WO-1094): "
                   + string.Join(" | ", notes);
            return true;
        }

        /// <summary>
        /// Extract the brace-matched body that follows <paramref name="signature"/>. Returns false
        /// rather than guessing when the signature is absent or the braces do not close.
        /// </summary>
        private static bool TryExtractBody(string src, string signature, out string body)
        {
            body = null;
            int sig = src.IndexOf(signature, StringComparison.Ordinal);
            if (sig < 0) return false;

            int open = src.IndexOf(OpenBrace, sig + signature.Length);
            if (open < 0) return false;

            int depth = 0;
            for (int i = open; i < src.Length; i++)
            {
                char c = src[i];
                if (c == OpenBrace) depth++;
                else if (c == CloseBrace)
                {
                    depth--;
                    if (depth == 0)
                    {
                        body = src.Substring(open, i - open + 1);
                        return true;
                    }
                }
            }
            return false;
        }
    }
}

// =============================================================================
// ArenaInSceneSuspensionRegression [arena-inscene-suspend]
// -----------------------------------------------------------------------------
// Assembly: DeNelle.EditorRegression (references DeNelle.Core).
//
// WO-1694. Owner's Seeker, build 363866, session 2026-09-10 15:20. She won a
// battle-arena fight and the game came home with combat input still suppressed:
//
//   [Flow:Quiescence] BATTLE_QUIESCENCE_FAIL (arena win) - 2 invariant(s) NOT
//   restored ... battle-lock: still HELD ... HOLDER(S):
//   WaveManager.<OnEnable>b__129_0 ... phase=Active wave=1 ... liveEnemies=4
//
// THE FRAMING THAT MATTERS, AND IT IS NOT THE OBVIOUS ONE: nobody declared a win
// over four live enemies. The arena's own release was CLEAN -
//   L714122  BATTLE_SESSION_RELEASED (#1, arena win) ... holders before=[none] after=[none]
// and the same session released cleanly on two retreats (#2, #3). The four enemies
// are a VILLAGE WAVE that spawned 0.8 s AFTER the win:
//   L714859  [Flow:Wave] TickCountdown: countdown hit 0 for wave=1 - calling StartWave
//
// WHY THE VILLAGE CLOCK WAS RUNNING DURING AN ARENA FIGHT - the whole ticket:
//   L710344  town SUSPENDED (arena battle staged at ArenaCentre (hero 7km away...))
//            <- BattleArena.cs:516 drove the hold BY HAND, exactly as its own comment
//               at :500-506 says it must, because the arena has no scene change.
//   L710750..L714808  [Flow:HUD] Countdown wave 1 cd19.8, cd8.0, cd5.9 ... cd0.8
//            <- the clock ticked every second THROUGH the whole fight anyway.
//   grep "HELD at phase" over all 732k lines of that logcat => ZERO hits. The
//            SuspendAndResume hold (WaveManager.cs:1307-1310) never engaged, once,
//            all session.
// The SAME-SESSION CONTROL is the dungeon at L398282-L405463: a real scene change,
// therefore a FLOOR, and not one countdown tick in that entire window.
//
// ROOT CAUSE: TownSuspension.SuspendedFor's ACTIVE-SCENE EXEMPTION. It answers one
// question - "did the player LEAVE this scene?" - and it was applied unconditionally.
// The arena stages 7 km away in the SAME scene, so WaveManager (in that active scene)
// was exempted and the hand-driven Suspend was a NO-OP for the one system it was
// written to stop. The fix scopes the exemption to a hold a scene CHANGE created
// (the FLOOR); a floorless, hand-driven hold covers the active scene too.
//
// WHAT THIS SUITE PROVES:
//   (a) THE FIX, BEHAVIOURALLY - a floorless arena hold really does report
//       SuspendedFor=true for a town object in the ACTIVE scene. RED before the fix.
//   (b) NO OVER-FIX - with a FLOOR (a dungeon), the active-scene carve-out still
//       holds. Killing it would freeze the dungeon's own enemies mid-fight, which is
//       worse than the bug (see [town-suspend-floor] case 6, deliberately duplicated
//       here from this ticket's angle so a fix to either cannot break the other).
//   (c) THE RETURN GRACE MEANS SOMETHING IN-HUB - after the arena Resume, the 3.5 s
//       grace holds town objects in the active scene. Before the fix that grace was
//       decorative for every in-hub hold: the exemption fired throughout it.
//   (d) THE CALLERS ARE STILL THERE - BattleArena still drives the pair by hand and
//       WaveManager still consults the gate per tick. Both behaviour cases would pass
//       on a tree where either call had been deleted, and the game would be broken.
//   (e) THE EXEMPTION IS FLOOR-SCOPED AT SOURCE - the batch fallback, see below.
//
// ⚠ HONEST LIMIT, STATED RATHER THAN HIDDEN: cases (a)(b)(c) need a probe GameObject
// in a NAMED scene. SuspendedFor deliberately treats an unnamed scene as
// DontDestroyOnLoad (TownSuspension.cs:210), which is the check BEFORE the handle
// test, so in a batch run with only an Untitled scene open those asserts are not
// observable. They say so out loud and stand down - and case (e) is the source-shape
// pin that keeps this suite RED on a reverted tree even then. A source pin is not
// coverage on its own (CLAUDE.md S7); it is the fallback UNDER the behavioural cases,
// never instead of them.
//
// Markers: ARENA_INSCENE_SUSPEND_OK / ARENA_INSCENE_SUSPEND_FAIL.
// Standalone: run-unity-method DeNelle.Editor.Regression.ArenaInSceneSuspensionRegression.RunAll
// Covenant contract Run(out reason) is DataRegression-shaped; wiring into
// DataRegression.RunAll is left to the committer (that file is lane-fenced).
// =============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using DeNelle.Core;

namespace DeNelle.Editor.Regression
{
    public static class ArenaInSceneSuspensionRegression
    {
        private const string SuspendSrc = "Assets/_Modules/Core/TownSuspension.cs";
        private const string ArenaSrc   = "Assets/_Modules/Village/Arena/BattleArena.cs";
        private const string WaveSrc    = "Assets/_Modules/Village/Waves/WaveManager.cs";

        /// <summary>
        /// The EXACT string BattleArena.cs:516 passes to Suspend. Using the shipped literal
        /// rather than a test-only reason is deliberate: it is what makes case (d)'s source
        /// pin and case (a)'s behaviour describe one and the same call.
        /// </summary>
        private const string ArenaReason =
            "arena battle staged at ArenaCentre (hero 7km away, player active)";

        private static string Hub => DeNelle.Core.SceneRouter.Castle;

        /// <summary>
        /// Set by case 1 when it actually got to compare a named scene. A static rather than a
        /// ref parameter because the case bodies run inside lambdas, and C# forbids capturing a
        /// ref in one - a detail worth stating so the next seat does not "tidy" it back.
        /// </summary>
        private static bool _behaviourObserved;

        /// <summary>Standalone batch entry - prints the marker.</summary>
        public static void RunAll()
        {
            if (Run(out string reason)) Debug.Log("ARENA_INSCENE_SUSPEND_OK - " + reason);
            else Debug.LogError("ARENA_INSCENE_SUSPEND_FAIL: " + reason);
        }

        /// <summary>Covenant contract (DataRegression-shaped). Never throws.</summary>
        public static bool Run(out string reason)
        {
            var failures = new List<string>();
            _behaviourObserved = false;

            try
            {
                Case(failures, "arena-hold-covers-town", () => Case1_FloorlessHoldCoversActiveScene(failures));
                Case(failures, "floor-carve-out",        () => Case2_FlooredHoldStillExemptsActiveScene(failures));
                Case(failures, "grace-covers-town",      () => Case3_ReturnGraceCoversActiveScene(failures));
                Case(failures, "ddol-still-held",        () => Case4_UnownedOwnerStillHeld(failures));
                Case(failures, "callers-intact",         () => Case5_CallersIntact(failures));
                Case(failures, "floor-scoped-at-source", () => Case6_ExemptionIsFloorScopedAtSource(failures));
            }
            catch (Exception ex)
            {
                failures.Add("[suite] THREW " + ex.GetType().Name + ": " + ex.Message);
            }
            finally
            {
                Cleanup();
            }

            if (failures.Count == 0)
            {
                reason = "ARENA IN-SCENE SUSPENSION OK - a floorless (hand-driven) town hold now covers " +
                         "ACTIVE-scene town objects, so the arena's Suspend actually stops the village " +
                         "wave clock 7 km away; a FLOORED hold still exempts the active scene, so a " +
                         "dungeon's own enemies keep running; the in-hub return grace covers the town; " +
                         "BattleArena still drives the Suspend/Resume pair and WaveManager still consults " +
                         "the gate per tick; and the exemption is floor-scoped at source. " +
                         (_behaviourObserved
                            ? "The behavioural cases ran against a real named scene."
                            : "⚠ The behavioural cases STOOD DOWN this run (unnamed/Untitled scene) - " +
                              "only the source-shape and floor-independent cases were asserted.");
                return true;
            }

            reason = "arena in-scene suspension BROKEN (" + failures.Count + "): " + string.Join(" | ", failures);
            return false;
        }

        // =====================================================================
        //  Case 1 - THE FIX. A floorless hold must cover the ACTIVE scene.
        // =====================================================================

        private static void Case1_FloorlessHoldCoversActiveScene(List<string> failures)
        {
            GameObject go = null;
            try
            {
                // Stand in the hub: no floor, exactly the shape of the owner's session
                // (scene='Main_Castle_Overworld' activeScene='Main_Castle_Overworld').
                TownSuspension.ApplySceneBaseline(Hub, "regression:in-hub");
                TownSuspension.Resume("regression:in-hub", 0f);

                // Drive the arena's OWN call, verbatim (BattleArena.cs:516).
                TownSuspension.Suspend(ArenaReason);
                if (!TownSuspension.IsSuspended)
                {
                    failures.Add("[arena-hold-covers-town] the arena's hand-driven Suspend did not engage at all. " +
                                 "It is the arena's ONLY pause - there is no scene change to ride.");
                    return;
                }

                go = NewProbe("ArenaInSceneSuspendProbe");
                if (!IsNamedScene(go))
                {
                    StandDown("arena-hold-covers-town");
                    return;
                }

                _behaviourObserved = true;
                if (!TownSuspension.SuspendedFor(go))
                {
                    failures.Add("[arena-hold-covers-town] a TOWN object in the ACTIVE scene reports " +
                                 "SuspendedFor=FALSE during a FLOORLESS arena hold. This is WO-1694 exactly: the " +
                                 "arena stages 7 km away in this same scene, so the active-scene exemption has no " +
                                 "scene change to reason about and silently voids BattleArena.cs:516. Proven on the " +
                                 "owner's Seeker 2026-09-10 15:20 - the wave countdown ticked cd19.8 -> cd0.8 through " +
                                 "the whole fight (logcat L710750-L714808) and StartWave(1) landed 0.8s after the win " +
                                 "(L714859), which re-raised the battle-lock and failed the quiescence gate (L715193).");
                }
            }
            finally
            {
                if (go != null) UnityEngine.Object.DestroyImmediate(go);
            }
        }

        // =====================================================================
        //  Case 2 - the OVER-FIX guard. A floored hold still exempts.
        // =====================================================================

        private static void Case2_FlooredHoldStillExemptsActiveScene(List<string> failures)
        {
            GameObject go = null;
            try
            {
                // A real scene change: the player walked into a dungeon. The objects around
                // them ARE the active scene, and freezing those is the Time.timeScale mistake
                // arriving by another road.
                TownSuspension.ApplySceneBaseline(Hub, "regression:floor-reset");
                TownSuspension.Resume("regression:floor-reset", 0f);
                TownSuspension.ApplySceneBaseline("Dungeon_HealersCottage", "regression:enter-dungeon");
                if (!TownSuspension.IsSuspended)
                {
                    failures.Add("[floor-carve-out] could not stage a FLOORED suspension to test the carve-out under.");
                    return;
                }

                go = NewProbe("ArenaInSceneCarveOutProbe");
                if (!IsNamedScene(go))
                {
                    StandDown("floor-carve-out");
                    return;
                }

                if (TownSuspension.SuspendedFor(go))
                {
                    failures.Add("[floor-carve-out] an object in the ACTIVE scene reports SuspendedFor=TRUE during a " +
                                 "FLOORED (scene-change) suspension. WO-1694 narrowed the exemption; it must NOT have " +
                                 "removed it. The player is standing among these objects - suspending them stops the " +
                                 "dungeon's own enemies mid-fight, which is worse than the bug being fixed.");
                }
            }
            finally
            {
                if (go != null) UnityEngine.Object.DestroyImmediate(go);
            }
        }

        // =====================================================================
        //  Case 3 - the in-hub RETURN GRACE must actually hold the town
        // =====================================================================

        private static void Case3_ReturnGraceCoversActiveScene(List<string> failures)
        {
            GameObject go = null;
            try
            {
                TownSuspension.ApplySceneBaseline(Hub, "regression:grace-reset");
                TownSuspension.Resume("regression:grace-reset", 0f);
                TownSuspension.Suspend(ArenaReason);
                TownSuspension.Resume("arena battle resolved");   // BattleArena.cs:2723, default grace

                if (TownSuspension.IsSuspended)
                {
                    failures.Add("[grace-covers-town] the arena Resume did not release the town. A hub scene has no " +
                                 "floor, so this hold must resume - leaving it held freezes the village for the session.");
                    return;
                }
                if (!TownSuspension.ReturnGraceActive)
                {
                    failures.Add("[grace-covers-town] no return grace was started by the arena Resume. That grace is the " +
                                 "shipped answer to 'a wave cleared 2.7s after an arena victory and stranded the player'.");
                    return;
                }

                go = NewProbe("ArenaInSceneGraceProbe");
                if (!IsNamedScene(go))
                {
                    StandDown("grace-covers-town");
                    return;
                }

                if (!TownSuspension.SuspendedFor(go))
                {
                    failures.Add("[grace-covers-town] a town object in the ACTIVE scene is NOT held during the post-arena " +
                                 "return grace. For an in-hub hold the grace was decorative before WO-1694 - the " +
                                 "exemption fired for every hub object throughout it, which is why the wave could land " +
                                 "the instant the fight ended.");
                }
            }
            finally
            {
                if (go != null) UnityEngine.Object.DestroyImmediate(go);
            }
        }

        // =====================================================================
        //  Case 4 - the other half of the rule, and it needs no scene
        // =====================================================================

        private static void Case4_UnownedOwnerStillHeld(List<string> failures)
        {
            TownSuspension.ApplySceneBaseline(Hub, "regression:ddol-reset");
            TownSuspension.Resume("regression:ddol-reset", 0f);
            TownSuspension.Suspend(ArenaReason);

            if (!TownSuspension.SuspendedFor((GameObject)null))
            {
                failures.Add("[ddol-still-held] a null/unowned owner is NOT held during a floorless arena hold. " +
                             "DontDestroyOnLoad town services (RegionMobSpawner) are exactly the things that keep " +
                             "acting on a town the player cannot reach.");
            }
        }

        // =====================================================================
        //  Case 5 - the callers. Both behaviour cases pass without them.
        // =====================================================================

        private static void Case5_CallersIntact(List<string> failures)
        {
            if (!File.Exists(ArenaSrc))
            {
                failures.Add("[callers-intact] " + ArenaSrc + " is gone.");
            }
            else
            {
                string arena = File.ReadAllText(ArenaSrc);
                if (!arena.Contains("TownSuspension.Suspend(\"" + ArenaReason + "\")"))
                    failures.Add("[callers-intact] " + ArenaSrc + " no longer drives TownSuspension.Suspend with the " +
                                 "arena's reason. The arena has NO scene change, so this hand-driven call is its only " +
                                 "pause - delete it and the village clock runs through every fight again, silently.");
                if (!arena.Contains("TownSuspension.Resume(\"arena battle resolved\")"))
                    failures.Add("[callers-intact] " + ArenaSrc + " no longer Resumes on resolve. An unpaired Suspend " +
                                 "leaks a permanent town freeze for the rest of the session.");
            }

            if (!File.Exists(WaveSrc))
            {
                failures.Add("[callers-intact] " + WaveSrc + " is gone.");
            }
            else
            {
                string wave = File.ReadAllText(WaveSrc);
                if (!wave.Contains("TownSuspension.SuspendedFor(this)"))
                    failures.Add("[callers-intact] " + WaveSrc + " no longer consults TownSuspension.SuspendedFor. " +
                                 "The gate is checked PER TICK on purpose: checked only at the door, an already-armed " +
                                 "countdown runs to zero and spawns anyway.");
            }
        }

        // =====================================================================
        //  Case 6 - the fix's shape at source (the batch fallback, see header)
        // =====================================================================

        private static void Case6_ExemptionIsFloorScopedAtSource(List<string> failures)
        {
            if (!File.Exists(SuspendSrc))
            {
                failures.Add("[floor-scoped-at-source] " + SuspendSrc + " is gone.");
                return;
            }

            string src = File.ReadAllText(SuspendSrc);
            if (!src.Contains("if (_floorReason != null) return false;"))
                failures.Add("[floor-scoped-at-source] SuspendedFor's active-scene exemption is no longer scoped to the " +
                             "FLOOR. Unconditional, it answers 'did the player leave this scene?' for a hold that no " +
                             "scene change created - and voids every hand-driven Suspend, the arena's included " +
                             "(WO-1694). This case is also the batch fallback for cases 1-3, which need a named scene.");
            if (!src.Contains("FLOORLESS"))
                failures.Add("[floor-scoped-at-source] " + SuspendSrc + " no longer names the FLOORLESS coverage in its " +
                             "trace. CLAUDE.md S12: instrumentation is permanent. Before WO-1694 the 'town SUSPENDED' " +
                             "line read identically whether the hold covered the active scene or not, which is why a " +
                             "no-op suspension sat in the log looking exactly like a working one.");
        }

        // =====================================================================
        //  Plumbing
        // =====================================================================

        private static GameObject NewProbe(string name) => new GameObject(name);

        private static bool IsNamedScene(GameObject go)
            => go != null && go.scene.IsValid() && !string.IsNullOrEmpty(go.scene.name);

        private static void StandDown(string caseName)
        {
            Debug.LogWarning("[Flow:TownSuspend] arena-inscene-suspend: case [" + caseName + "] NOT asserted this " +
                             "run - the probe object's scene is unnamed (batch/Untitled), which SuspendedFor " +
                             "deliberately treats as DontDestroyOnLoad (TownSuspension.cs:210), BEFORE the " +
                             "active-scene handle test this case exercises. Re-run with a real scene open. The " +
                             "[floor-scoped-at-source] case still pins the fix's shape.");
        }

        private static void Case(List<string> failures, string name, Action body)
        {
            try { body(); }
            catch (Exception ex) { failures.Add("[" + name + "] THREW " + ex.GetType().Name + ": " + ex.Message); }
        }

        /// <summary>Restore the town to a clean, un-held hub state for whatever runs next.</summary>
        private static void Cleanup()
        {
            try
            {
                TownSuspension.ApplySceneBaseline(Hub, "arena-inscene-suspend regression cleanup");
                TownSuspension.Suspend("arena-inscene-suspend regression cleanup");
                TownSuspension.Resume("arena-inscene-suspend regression cleanup", 0f);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[Flow:TownSuspend] arena-inscene-suspend cleanup threw " + ex.GetType().Name +
                                 ": " + ex.Message + " - the next suite may see a held town.");
            }
        }
    }
}

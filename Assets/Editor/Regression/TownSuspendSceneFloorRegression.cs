// =============================================================================
// TownSuspendSceneFloorRegression [town-suspend-floor]
// -----------------------------------------------------------------------------
// Assembly: DeNelle.EditorRegression (references DeNelle.Core, and DeNelle.Village
// for case (h) below, which calls TownActivityProbe's scene gate directly).
//
// WO-1017 (F8 seq 2314, 2026-08-10, scene Dungeon_HealersCottage). The harness
// raised this one: TownActivityProbe failed its own invariant with
//   suspended=False grace=2.7s reason='none'
// while the player was standing in a dungeon.
//
// WHAT THE CAPTURED LOG ACTUALLY PROVED (Player.log of that session, in order):
//   L195137  town SUSPENDED (player active in 'Dungeon_HealersCottage')   <- the
//            scene gate DID fire. The probe's "the scene-driven gate did not fire
//            for this scene" sentence is the probe guessing at a cause it cannot
//            see; the classification was never the defect.
//   L213948  town already suspended - reason updated to 'arena battle staged...'
//            <- a real-time BattleArena encounter staged INSIDE the dungeon and
//            called Suspend() again. Suspend is FLAT and idempotent, so this only
//            rewrote the reason - it recorded no nesting.
//   L224706  town RESUMED (arena battle resolved) ... holding 3.5s return grace
//            <- the fight's paired Resume released the DUNGEON's baseline too, and
//            armed a "welcome home" grace for a player who had not gone home.
//   L225357  the probe's Fail, 0.8 s into that 3.5 s grace (hence grace=2.7s).
//
// So the defect was never a missed scene classification - it was that a NESTED
// hold could lift the town on its way out. The fix makes the active scene a FLOOR:
// while it is off-hub it holds the town down, ad-hoc holds may only ADD, and their
// Resume falls back TO the floor rather than through it.
//
// WHAT THIS SUITE PROVES HEADLESSLY:
//   (a) CLASSIFICATION BY KIND - SceneDemandsSuspension is "not a hub and not the
//       front-end", so every dungeon family (hand-built Dungeon_*, composed dg_*,
//       the hand-coded outpost), raid scenes and ATBBattle are all covered with no
//       whitelist to edit, and a dungeon baked tomorrow is right on day one.
//   (b) THE FLOOR ENGAGES WITH A REAL REASON - entering a non-hub scene leaves
//       IsSuspended true and Reason naming the scene, never the 'none' the capture
//       showed.
//   (c) THE CAPTURED SEQUENCE, REPLAYED - dungeon -> nested arena Suspend -> arena
//       Resume MUST leave the town suspended, on the floor's reason, with NO return
//       grace. This case fails on the pre-fix code and is the point of the suite.
//   (d) BOTH HALVES - returning to the hub really does resume, with the grace. A
//       suspend that never resumes is the worse bug.
//   (e) THE ARENA-IN-TOWN PATH IS NOT COLLATERAL - an arena staged 7 km away in the
//       hub scene (no scene change, so no floor) still resumes normally. Guards
//       against over-fixing into a town that can never restart.
//   (f) THE ACTIVE-SCENE CARVE-OUT SURVIVES - an object in the scene the player is
//       standing in is NEVER suspended, at the height of a floor suspension. The
//       capture's "Enemy x1 in the ACTIVE scene (these MUST keep running)" is the
//       thing a bad fix would kill.
//   (g) THE DETECTOR IS STILL LOUD - TownActivityProbe's FlowTrace.Fail is pinned at
//       source. It is the only reason this was ever visible (S12); silencing it to
//       make a capture clean would be the real regression.
//   (h) THE PROBE IS GATED OUT OF RAID SCENES, AND ONLY RAID SCENES (WO-1779). The
//       probe's OnSceneLoaded discarded its Scene argument, so the DontDestroyOnLoad
//       town probe re-spawned into every scene - including RaidBase_*, where the
//       owner's 2026-09-16 Seeker capture measured its per-frame scope over budget
//       19 times (logcat_full.txt lines 3038288-3059147, max 10.5ms of a 16ms frame)
//       in a window that also recorded LOW fps=18 ms=56.5. This case asserts the
//       gate BOTH ways on a real component instance: a raid scene name disables it,
//       the hub re-enables it, and - the half a careless fix would break - every
//       DUNGEON family still ticks, because observing a dungeon is the entire reason
//       this probe (and this suite) exists.
//
// Markers: TOWN_SUSPEND_FLOOR_OK / TOWN_SUSPEND_FLOOR_FAIL.
// Standalone: run-unity-method DeNelle.Editor.Regression.TownSuspendSceneFloorRegression.RunAll
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
    public static class TownSuspendSceneFloorRegression
    {
        private const string ProbeSrc = "Assets/_Modules/Village/World/TownActivityProbe.cs";

        // A hub the floor must NOT engage for -- the live home hub (CLAUDE.md S7), which is also
        // the clean state this suite restores to. WO-1112: RESOLVED from SceneRouter.Castle rather
        // than the "Main_Castle_Overworld" literal it used to be, so the day ff.MergedWorld flips
        // or the hub is renamed this suite still restores the town to a scene that exists.
        // (The legacy names in Case1's mustNotSuspend array below stay LITERAL on purpose: they are
        // deliberate coverage of retired names, i.e. test INPUTS, not a resolution of "the hub".)
        private static string Hub => DeNelle.Core.SceneRouter.Castle;

        /// <summary>Standalone batch entry - prints the marker.</summary>
        public static void RunAll()
        {
            if (Run(out string reason)) Debug.Log("TOWN_SUSPEND_FLOOR_OK - " + reason);
            else Debug.LogError("TOWN_SUSPEND_FLOOR_FAIL: " + reason);
        }

        /// <summary>Covenant contract (DataRegression-shaped). Never throws.</summary>
        public static bool Run(out string reason)
        {
            var failures = new List<string>();
            try
            {
                Case(failures, "kind-classification", () => Case1_ClassificationByKind(failures));
                Case(failures, "floor-engages",       () => Case2_FloorEngagesWithReason(failures));
                Case(failures, "nested-hold",         () => Case3_NestedHoldCannotLiftFloor(failures));
                Case(failures, "resume-on-return",    () => Case4_ReturnToHubResumes(failures));
                Case(failures, "arena-in-town",       () => Case5_FloorlessHoldStillResumes(failures));
                Case(failures, "active-scene-carve",  () => Case6_ActiveSceneCarveOut(failures));
                Case(failures, "detector-loud",       () => Case7_DetectorStillFails(failures));
                Case(failures, "raid-scene-gate",     () => Case8_ProbeGatedOutOfRaids(failures));
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
                reason = "TOWN SUSPEND FLOOR OK - every non-hub scene kind (hand-built dungeon, " +
                         "composed dg_*, outpost, raid, ATB) engages the suspension with a reason " +
                         "naming the scene; a nested arena hold resolving INSIDE a dungeon can no " +
                         "longer lift that floor (F8 seq 2314 replayed); returning to the hub still " +
                         "resumes with the return grace; a floorless in-town arena hold still resumes; " +
                         "the active-scene carve-out holds at the height of a suspension; " +
                         "TownActivityProbe's FlowTrace.Fail is still present at source; and the probe's " +
                         "WO-1779 scene gate disables its tick in every RaidBase_* scene while every " +
                         "dungeon family, the outpost and the hub still tick.";
                return true;
            }
            reason = "town-suspend-floor FAIL x" + failures.Count + ": " + string.Join(" | ", failures);
            return false;
        }

        private static void Case(List<string> failures, string name, Action body)
        {
            try { body(); }
            catch (Exception ex) { failures.Add("[" + name + "] THREW " + ex.GetType().Name + ": " + ex.Message); }
        }

        /// <summary>Return the statics to a known-clean, unsuspended state. These are process-wide
        /// in the editor, so a suite that left the town frozen would poison every later suite.</summary>
        private static void Cleanup()
        {
            try
            {
                // Land on the hub (clears the floor), then take + release a floorless hold with a
                // ZERO grace so nothing is left Held for a later suite to trip over.
                TownSuspension.ApplySceneBaseline(Hub, "town-suspend-floor regression cleanup");
                TownSuspension.Suspend("town-suspend-floor regression cleanup");
                TownSuspension.Resume("town-suspend-floor regression cleanup", 0f);
            }
            catch { /* cleanup must never mask a real failure */ }
        }

        // =====================================================================
        //  Case 1 - classification is by KIND, not by a dungeon whitelist
        // =====================================================================

        private static void Case1_ClassificationByKind(List<string> failures)
        {
            // Off-hub: the town must stand still. Deliberately includes scene names that
            // exist only as conventions (a dg_* that has not been baked yet, a RaidBase_*)
            // because "correct the day it is baked, with no list edit" is the acceptance
            // criterion this case exists to hold.
            string[] mustSuspend =
            {
                "Dungeon_HealersCottage",   // hand-built (the captured scene)
                "Dungeon_FolksGranary",     // hand-built
                "dg_starter_loop",          // composed
                "dg_ember_deep",            // composed
                "dg_not_yet_baked",         // composed, does not exist yet - still classified
                "KayKitChallengeOutpost",   // hand-coded outpost, named by neither convention
                "RaidBase_Ashfell",         // raid target
                "ATBBattle",                // turn-based battle scene
            };
            foreach (var s in mustSuspend)
            {
                if (!TownSuspension.SceneDemandsSuspension(s, out string why))
                    failures.Add("[kind-classification] '" + s + "' does NOT demand suspension - the player " +
                                 "is active OFF-HUB there, so the town must stand still. The rule is " +
                                 "'not a hub and not front-end'; something re-narrowed it to a name list.");
                else if (string.IsNullOrEmpty(why) || !why.Contains(s))
                    failures.Add("[kind-classification] '" + s + "' suspends but its reason ('" + (why ?? "<null>") +
                                 "') does not name the scene - the capture that opened WO-1017 was read from " +
                                 "the reason field, so it has to say where the player is.");
            }

            // On-hub / front-end: the town is where the player is, or there is no town.
            string[] mustNotSuspend = { Hub, "Village2", "MainCastle_Hall", "CastleHub_MainKeep", "Title", "HeroSelect" };
            foreach (var s in mustNotSuspend)
            {
                if (TownSuspension.SceneDemandsSuspension(s, out _))
                    failures.Add("[kind-classification] '" + s + "' demands suspension - a hub / front-end scene " +
                                 "must never freeze the town the player is standing in.");
            }

            if (TownSuspension.SceneDemandsSuspension(null, out _) ||
                TownSuspension.SceneDemandsSuspension(string.Empty, out _))
                failures.Add("[kind-classification] a null/empty scene name demands suspension - an unnamed " +
                             "scene is a load in flight, not a place the player is; suspending on it would " +
                             "freeze the town on transient events.");
        }

        // =====================================================================
        //  Case 2 - entering a non-hub scene really engages it, with a reason
        // =====================================================================

        private static void Case2_FloorEngagesWithReason(List<string> failures)
        {
            TownSuspension.ApplySceneBaseline(Hub, "regression:start-in-hub");
            TownSuspension.Resume("regression:start-in-hub", 0f);

            TownSuspension.ApplySceneBaseline("Dungeon_HealersCottage", "regression:enter-dungeon");

            if (!TownSuspension.IsSuspended)
                failures.Add("[floor-engages] entering 'Dungeon_HealersCottage' left IsSuspended=false - this " +
                             "is the exact state F8 seq 2314 captured.");
            if (string.IsNullOrEmpty(TownSuspension.Reason) || TownSuspension.Reason == "none")
                failures.Add("[floor-engages] the suspension engaged with reason='" + TownSuspension.Reason +
                             "'. 'none' is the fingerprint of nothing having asked - the reason must name why.");
            if (!TownSuspension.Held)
                failures.Add("[floor-engages] Held=false while suspended - Held is what every tick site " +
                             "consults, so a false here means no town system actually stands down.");
        }

        // =====================================================================
        //  Case 3 - THE CAPTURED SEQUENCE. A nested hold cannot lift the floor.
        // =====================================================================

        private static void Case3_NestedHoldCannotLiftFloor(List<string> failures)
        {
            TownSuspension.ApplySceneBaseline(Hub, "regression:start-in-hub");
            TownSuspension.Resume("regression:start-in-hub", 0f);

            // 1. The player walks into the dungeon. (Player.log L195137)
            TownSuspension.ApplySceneBaseline("Dungeon_HealersCottage", "regression:enter-dungeon");
            string floorReason = TownSuspension.Reason;

            // 2. A real-time BattleArena encounter stages INSIDE the dungeon. (L213948)
            TownSuspension.Suspend("arena battle staged at ArenaCentre (hero 7km away, player active)");
            if (!TownSuspension.IsSuspended)
                failures.Add("[nested-hold] the nested arena Suspend un-suspended the town - Suspend must " +
                             "never be able to reduce the hold.");

            // 3. The fight resolves and BattleArena calls its paired Resume. (L224706)
            TownSuspension.Resume("arena battle resolved");

            // The player is STILL IN THE DUNGEON. This is the whole ticket.
            if (!TownSuspension.IsSuspended)
                failures.Add("[nested-hold] a nested arena Resume released the town while the player is still " +
                             "in 'Dungeon_HealersCottage'. This is WO-1017 verbatim: the arena's Resume ate the " +
                             "dungeon's own baseline, and the town ran on with the player away.");
            if (TownSuspension.Reason != floorReason)
                failures.Add("[nested-hold] after the nested Resume the reason is '" + TownSuspension.Reason +
                             "' but the floor's reason is '" + floorReason + "' - the town must fall back TO " +
                             "the floor, keeping the reason that explains the real state.");
            if (TownSuspension.ReturnGraceRemaining > 0f || TownSuspension.ReturnGraceActive)
                failures.Add("[nested-hold] a return grace (" + TownSuspension.ReturnGraceRemaining.ToString("0.0") +
                             "s) started while the player is still in the dungeon. The grace exists for a player " +
                             "who has just come HOME; arming it here is what put grace=2.7s in the capture.");

            // Belt-and-braces: a same-scene re-evaluation must RE-ASSERT a lost floor rather
            // than dedup past it (the pre-fix _lastActiveScene short-circuit could not self-heal).
            TownSuspension.ApplySceneBaseline("Dungeon_HealersCottage", "regression:same-scene-reevaluate");
            if (!TownSuspension.IsSuspended)
                failures.Add("[nested-hold] a same-scene re-evaluation left the town running - the floor must " +
                             "be re-assertable, or a single lost suspension is permanent for that scene visit.");
        }

        // =====================================================================
        //  Case 4 - and it really does resume on the way home (both halves)
        // =====================================================================

        private static void Case4_ReturnToHubResumes(List<string> failures)
        {
            TownSuspension.ApplySceneBaseline("dg_starter_loop", "regression:enter-composed-dungeon");
            if (!TownSuspension.IsSuspended)
                failures.Add("[resume-on-return] a composed dg_* dungeon did not suspend - see case 1.");

            TownSuspension.ApplySceneBaseline(Hub, "regression:return-home");

            if (TownSuspension.IsSuspended)
                failures.Add("[resume-on-return] returning to '" + Hub + "' left the town SUSPENDED. A freeze " +
                             "that never lifts is worse than the bug it fixed - the village would never tick again.");
            if (!TownSuspension.ReturnGraceActive)
                failures.Add("[resume-on-return] no return grace after coming home - a held wave would land on " +
                             "the player the instant they load in, which is the stranding this grace was added for.");
            if (!TownSuspension.Held)
                failures.Add("[resume-on-return] Held=false during the return grace - Held must stay true for " +
                             "the whole grace window or the grace holds nothing.");
        }

        // =====================================================================
        //  Case 5 - the floorless (in-town arena) hold must still resume
        // =====================================================================

        private static void Case5_FloorlessHoldStillResumes(List<string> failures)
        {
            // The arena stages 7 km away in the SAME hub scene: no scene change, so no floor.
            // Its Suspend/Resume pair must keep working exactly as before - over-fixing this
            // into "the town can never resume" is the failure mode of this ticket.
            TownSuspension.ApplySceneBaseline(Hub, "regression:in-town");
            TownSuspension.Resume("regression:in-town", 0f);

            TownSuspension.Suspend("arena battle staged at ArenaCentre (hero 7km away, player active)");
            if (!TownSuspension.IsSuspended)
                failures.Add("[arena-in-town] an in-town arena Suspend did not engage - the arena has no scene " +
                             "change to ride, so this hand-driven call is its ONLY pause.");

            TownSuspension.Resume("arena battle resolved");
            if (TownSuspension.IsSuspended)
                failures.Add("[arena-in-town] an in-town arena Resume did NOT release the town. There is no floor " +
                             "in a hub scene, so this hold must resume normally; leaving it held would freeze the " +
                             "village for the rest of the session.");
            if (!TownSuspension.ReturnGraceActive)
                failures.Add("[arena-in-town] the in-town arena resume started no return grace - that grace is the " +
                             "shipped fix for 'a wave cleared 2.7s after an arena victory and stranded the player'.");
        }

        // =====================================================================
        //  Case 6 - the ACTIVE-SCENE carve-out, at the height of a suspension
        // =====================================================================

        private static void Case6_ActiveSceneCarveOut(List<string> failures)
        {
            GameObject go = null;
            try
            {
                TownSuspension.ApplySceneBaseline("Dungeon_HealersCottage", "regression:carve-out");
                if (!TownSuspension.IsSuspended)
                {
                    failures.Add("[active-scene-carve] could not stage a suspension to test the carve-out under.");
                    return;
                }

                // The dungeon's own enemies live in the ACTIVE scene. The capture named one
                // explicitly - "Enemy x1 in the ACTIVE scene (these MUST keep running)" - and a
                // fix that suspends it is worse than the bug. A plain GameObject created in the
                // editor lands in the currently-open scene, which IS the active scene, so it
                // stands in for that enemy.
                go = new GameObject("TownSuspendCarveOutProbe");
                if (!go.scene.IsValid() || string.IsNullOrEmpty(go.scene.name))
                {
                    // A batch run with only an Untitled scene open gives the probe an unnamed
                    // scene, which SuspendedFor treats as DDOL by design (the check that comes
                    // BEFORE the handle test). The carve-out is simply not observable here - say
                    // so out loud rather than either failing on the harness or ticking it green.
                    Debug.LogWarning("[Flow:TownSuspend] town-suspend-floor: active-scene carve-out NOT asserted " +
                                     "this run - the probe object's scene is unnamed (batch/Untitled), which " +
                                     "SuspendedFor deliberately treats as DontDestroyOnLoad. Re-run with a real " +
                                     "scene open to exercise it.");
                }
                else if (TownSuspension.SuspendedFor(go))
                {
                    failures.Add("[active-scene-carve] an object in the ACTIVE scene reports SuspendedFor=true " +
                                 "during a town suspension. The player is standing among these objects - " +
                                 "suspending them is the Time.timeScale mistake arriving by another road, and it " +
                                 "would stop the dungeon's own enemies mid-fight.");
                }

                // The other half of the same rule: a DDOL / unowned town service IS held.
                if (!TownSuspension.SuspendedFor((GameObject)null))
                    failures.Add("[active-scene-carve] a null/unowned owner is NOT held during a suspension - " +
                                 "DontDestroyOnLoad town services (RegionMobSpawner) are exactly the things that " +
                                 "keep acting on a town the player cannot see.");
            }
            finally
            {
                if (go != null) UnityEngine.Object.DestroyImmediate(go);
            }
        }

        // =====================================================================
        //  Case 7 - the detector stays loud (S12: never strip instrumentation)
        // =====================================================================

        private static void Case7_DetectorStillFails(List<string> failures)
        {
            if (!File.Exists(ProbeSrc))
            {
                failures.Add("[detector-loud] " + ProbeSrc + " is gone. TownActivityProbe is the ONLY reason " +
                             "WO-1017 was ever visible; deleting it makes the next occurrence silent.");
                return;
            }

            string src = File.ReadAllText(ProbeSrc);
            if (!src.Contains("FlowTrace.Fail"))
                failures.Add("[detector-loud] " + ProbeSrc + " no longer calls FlowTrace.Fail - the invariant " +
                             "was softened instead of the gate being fixed. CLAUDE.md S12: instrumentation is " +
                             "permanent; a stripped Fail turns a logged failure back into a silent one.");
            if (!src.Contains("MUST keep running"))
                failures.Add("[detector-loud] " + ProbeSrc + " no longer reports active-scene objects separately " +
                             "from town-side ones. That split is how a suspension that starts eating the " +
                             "dungeon's own enemies gets caught.");
        }

        // =====================================================================
        //  Case 8 - the probe does not tick in a RAID, and still ticks everywhere
        //           else it was built for (WO-1779)
        // =====================================================================
        //
        // WHY THIS IS A BEHAVIOURAL CASE AND NOT A SOURCE LINT. Case 7 above is a
        // source lint because what it pins is the PRESENCE of a log call. What this
        // case pins is a DECISION - which scenes the probe runs in - and a lint that
        // greps for the string "IsRaid" would pass a gate wired backwards. So it calls
        // the real predicate and the real component gate.
        //
        // NO PLAYMODE, and the probe is never TrySpawn()ed: that path calls
        // DontDestroyOnLoad, which is not valid on an edit-mode object. The gate is
        // shaped to take its instance explicitly for exactly this reason, so an
        // AddComponent'ed probe (whose Awake the editor never runs) is enough.
        private static void Case8_ProbeGatedOutOfRaids(List<string> failures)
        {
            // --- the predicate, on the captured scene and its family ------------------
            // RaidBase_IronBastion is the scene the owner's 2026-09-16 capture measured;
            // RaidBase_Ashfell is the same family (and is already a Case-1 input); the
            // lowercase spelling is here because HubScenes.IsRaid matches
            // OrdinalIgnoreCase, so a gate that accidentally became case-sensitive would
            // let a renamed scene back through.
            string[] mustNotTick = { "RaidBase_IronBastion", "RaidBase_Ashfell", "raidbase_lowercase_spelling" };
            foreach (var s in mustNotTick)
            {
                if (DeNelle.Village.TownActivityProbe.ShouldTickIn(s))
                    failures.Add("[raid-scene-gate] the probe still ticks in '" + s + "'. WO-1779: inside " +
                                 "RaidBase_IronBastion its poll went over the 4ms frame budget 19 times, " +
                                 "peaking at 10.5ms, in a window that recorded LOW fps=18 ms=56.5 - and it " +
                                 "had nothing to report there, because every enemy in a raid is in the " +
                                 "ACTIVE scene.");
            }

            // --- and the half a careless fix breaks ----------------------------------
            // The probe exists to watch the town from OFF-hub (this suite's own WO-1017
            // capture came from Dungeon_HealersCottage). Gating it to "hub only" would
            // delete the observer, so every non-raid family must still tick. The hub is
            // included because Poll no-ops there by its own design, not by this gate -
            // switching the component off in the hub would break the return path.
            string[] mustTick =
            {
                Hub,                        // home hub - Poll self-no-ops, the component must stay live
                "Dungeon_HealersCottage",   // hand-built - the scene WO-1017 was captured in
                "dg_starter_loop",          // composed
                "KayKitChallengeOutpost",   // hand-coded outpost
                "Garrison_Northwatch",      // open-air enemy outpost - deliberately NOT a raid
                "ATBBattle",                // staged battle
            };
            foreach (var s in mustTick)
            {
                if (!DeNelle.Village.TownActivityProbe.ShouldTickIn(s))
                    failures.Add("[raid-scene-gate] the probe no longer ticks in '" + s + "'. The WO-1779 " +
                                 "gate is for RAID scenes ONLY; widening it to 'hub only' deletes the very " +
                                 "observer that made WO-1017 visible, and this suite would then be pinning " +
                                 "a detector that can never fire.");
            }

            if (DeNelle.Village.TownActivityProbe.ShouldTickIn(null) == false ||
                DeNelle.Village.TownActivityProbe.ShouldTickIn(string.Empty) == false)
                failures.Add("[raid-scene-gate] a null/empty scene name gates the probe OFF. An unnamed " +
                             "scene is a load in flight, not a raid; treating it as one would switch the " +
                             "probe off on a transient and never switch it back on.");

            // --- the COMPONENT gate, both directions, on a real instance --------------
            GameObject go = null;
            try
            {
                go = new GameObject("TownActivityProbeGateCase");
                var probe = go.AddComponent<DeNelle.Village.TownActivityProbe>();
                if (probe == null)
                {
                    failures.Add("[raid-scene-gate] could not stage a TownActivityProbe instance to gate.");
                    return;
                }

                if (DeNelle.Village.TownActivityProbe.ApplyGate(probe, "RaidBase_IronBastion") || probe.enabled)
                    failures.Add("[raid-scene-gate] ApplyGate left the probe ENABLED for " +
                                 "'RaidBase_IronBastion'. Disabling the Behaviour is what stops Unity " +
                                 "calling Update at all; merely early-returning inside Update would keep " +
                                 "the per-frame call and the scope alive for no reason.");

                if (!DeNelle.Village.TownActivityProbe.ApplyGate(probe, Hub) || !probe.enabled)
                    failures.Add("[raid-scene-gate] ApplyGate did NOT re-enable the probe on returning to '" +
                                 Hub + "'. A gate that only ever switches off is a permanent silence: the " +
                                 "probe would stop observing the town for the rest of the session after one " +
                                 "raid.");

                // Idempotent: re-applying the same verdict must not flip anything.
                DeNelle.Village.TownActivityProbe.ApplyGate(probe, Hub);
                if (!probe.enabled)
                    failures.Add("[raid-scene-gate] re-applying the SAME non-raid verdict disabled the " +
                                 "probe. The gate rides sceneLoaded AND activeSceneChanged, which both fire " +
                                 "for one transition, so it has to be idempotent or the two callbacks " +
                                 "fight each other.");
            }
            finally
            {
                if (go != null) UnityEngine.Object.DestroyImmediate(go);
            }
        }
    }
}

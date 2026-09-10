// =============================================================================
// EchoWorldPresenceRegression [echo-world-presence] — WO-1108 Lane B.
// -----------------------------------------------------------------------------
// Assembly: DeNelle.EditorRegression. Namespace: DeNelle.Editor.Regression.
//
// THE OWNER RULE THIS PINS (verbatim, 2026-08-16): "The only thing that should
// happen for the pet or the echo is it takes you to the gate, gives you your
// dialogue, then it disappears. The only time it reappears is after your battle."
//
// Before WO-1108 there was NO despawn path for a pet anywhere in the codebase and
// TWO independent appearance owners (the FTUE guide grant, and the WO-360 outpost
// trigger whose own header said the Echo "is never despawned here"). Both halves of
// the rule were unenforceable, so both are pinned here:
//
//   1 [lifecycle]  THE REAL STATE MACHINE, driven live: the Echo body EXISTS during
//                  the escort beat, is GONE the moment that beat completes, and
//                  REAPPEARS EXACTLY ONCE after a battle resolves — a second
//                  battle-resolve brings nothing back. Asserted by counting live
//                  Pet bodies in the scene (PetDeployer.LiveBodyCount) and reading
//                  EchoWorldPresence's session state, never by rendering.
//   2 [verb]       PetDeployer carries the despawn verb (DespawnEcho /
//                  DespawnAllEchoBodies / LiveBodyCount) — reflection, so a rename
//                  or removal fails here instead of silently restoring "pets can
//                  only ever be spawned".
//   3 [one-owner]  Exactly ONE file calls PetDeployer.SummonAt: the appearance
//                  owner's file. A third seam (any other caller) fails this suite.
//                  This is the assertion that keeps the two-owner defect from
//                  regrowing — the WO forbade adding a third.
//   4 [seam]       TutorialFlow fires the vanish at the EXISTING lead-clear point
//                  (arrival and vanish are the same event, so they cannot disagree)
//                  and routes its escort summon through the appearance owner.
//   5 [husk]       PetTaskController runs NO update/repair loop and PetTaskInstaller
//                  is gone — after WO-1031 that component's Update() did only
//                  TickRepair(), a SECOND uncoordinated repairer of the same walls
//                  that Lane A makes passive. Reflection + source-lint.
//   6 [hygiene]    No embedded NUL in the touched sources (CLAUDE.md Sec. 0).
//   7 [arena-stage] WO-1696. The battle arena is a MASKED 7km WARP inside the same
//                  scene, and on a WIN the battle lock drops seconds BEFORE the return
//                  warp - so the resolve edge could fire with the hero still on the
//                  stage and the companion was seated there, permanently (owner capture
//                  2026-09-10 1520: REAPPEAR at (4997.29, 0.08, 5002.89), 860 lines
//                  before the warp home). Driven live in [lifecycle] (b2) and
//                  source-linted here. The deferral must NOT consume the once-per-session
//                  guard, and the watcher must keep retrying until the hero is home:
//                  ONE transition, deferred - never a second appearance owner.
//
// EVERY source-lint in this suite reads CODE ONLY (CodeText: comment lines dropped,
// trailing // comments dropped, string-literal CONTENTS blanked). Fixed 2026-08-16
// after this suite's first run: WO-1108 Lane B deleted TutorialFlow.EnsureGuidePetDeployer
// and PetTaskController's RepairAll loop and left a COMMENT at each site documenting the
// removal -- and the [seam] and [husk] rules matched their own tombstones, reporting
// "the self-heal is back" / "RepairAll is called again" against files that call neither.
// A lint that cannot tell a call from a sentence punishes exactly the self-documenting
// removal notes CLAUDE.md Sec. 12/15 asks for. The RULES ARE UNCHANGED AND UNWEAKENED;
// only what they read changed. Same defect, same fix, as SpirePlansCelebrationRegression.
//
// Group 1 needs real assets (Resources/Pets/ice-wolf) and the headless GameState
// seam. When either is unavailable it takes a NAMED SKIP recorded in the reason
// line — never a silent pass.
//
// Markers: ECHO_WORLD_PRESENCE_OK / ECHO_WORLD_PRESENCE_FAIL.
// Standalone: run-unity-method
//   -Method DeNelle.Editor.Regression.EchoWorldPresenceRegression.RunAll
// Registered in DataRegression.RunAll as the "echo-world-presence suite".
// =============================================================================
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using DeNelle.Core.State;
using DeNelle.Pets;
using DeNelle.Village.World.Camps;
using UnityEngine;

namespace DeNelle.Editor.Regression
{
    public static class EchoWorldPresenceRegression
    {
        private const string FlowSrc     = "Assets/_Modules/Village/Tutorial/V2/TutorialFlow.cs";
        private const string PresenceSrc = "Assets/_Modules/Village/World/Camps/EchoAutoDeployTrigger.cs";
        private const string DeployerSrc = "Assets/_Modules/Pets/PetDeployer.cs";
        private const string TaskSrc     = "Assets/_Modules/Village/Pets/PetTaskController.cs";

        private const string EscortSpecies = "ice-wolf";   // the WO-961 founding guide body

        public static void RunAll()
        {
            if (Run(out string reason)) Debug.Log("ECHO_WORLD_PRESENCE_OK - " + reason);
            else Debug.LogError("ECHO_WORLD_PRESENCE_FAIL - " + reason);
        }

        public static bool Run(out string reason)
        {
            var failures = new List<string>();
            var notes = new List<string>();
            try
            {
                CheckDespawnVerb(failures);
                CheckSingleSummonOwner(failures);
                CheckTutorialSeam(failures);
                CheckHuskRetired(failures);
                CheckHygiene(failures);
                CheckReturnSeat(failures);
                CheckArenaStageGate(failures);
                CheckLifecycle(failures, notes);
            }
            catch (Exception ex)
            {
                failures.Add("[suite] threw: " + ex.GetType().Name + ": " + ex.Message);
            }

            if (failures.Count > 0)
            {
                reason = failures.Count + " failure(s): " + string.Join(" | ", failures.ToArray());
                return false;
            }
            reason = "WO-1108 Lane B holds: the Echo body appears for the escort, VANISHES when that " +
                     "beat completes (fired at the same lead-clear point, so arrival and vanish cannot " +
                     "disagree), and reappears EXACTLY ONCE after a battle resolves; PetDeployer carries " +
                     "the despawn verb; exactly one file calls SummonAt (one appearance owner, the WO-360 " +
                     "outpost summon retired); PetTaskController's repair husk and its installer are gone; " +
                     "a resolve on the staged arena DEFERS that one return instead of stranding the Echo " +
                     "7km from town (WO-1696, guard unconsumed, watcher retries); no NULs." + (notes.Count > 0 ? " NOTES: " + string.Join("; ", notes.ToArray()) : "");
            return true;
        }

        // -- 7 [arena-stage] --------------------------------------------------
        // WO-1696 source-lint. The live behaviour is driven in [lifecycle] (b2); this is the
        // cheap half that survives a headless run with no body asset, and it is what stops a
        // later "cleanup" from quietly deleting the gate: the appearance owner must READ
        // BattleArena.IsArenaPosition (the ONE authority on the 7km offset) rather than
        // re-implement the radius, and the watcher must keep the deferred retry.
        private static void CheckArenaStageGate(List<string> failures)
        {
            string src = ReadCode(PresenceSrc, failures);
            if (src == null) return;

            if (src.IndexOf("IsArenaPosition", StringComparison.Ordinal) < 0)
                failures.Add("[arena-stage] the appearance owner no longer reads " +
                             "BattleArena.IsArenaPosition. Without that gate a battle that resolves while " +
                             "the hero is still on the staged arena (a WIN drops the battle lock seconds " +
                             "before the return warp) seats the Echo 7km from town and strands it - the " +
                             "2026-09-10 device capture");
            if (src.IndexOf("5000", StringComparison.Ordinal) >= 0)
                failures.Add("[arena-stage] a literal arena coordinate appeared in the appearance owner. " +
                             "The 7km offset and its radius live ONCE, in BattleArena; a second copy is the " +
                             "duplicated-state failure CLAUDE.md sec.2/5/8/16 each describe - read the " +
                             "predicate, never re-derive the geometry");
            if (src.IndexOf("_reappearPending", StringComparison.Ordinal) < 0)
                failures.Add("[arena-stage] EchoPresenceWatcher lost its deferred-return retry. The gate " +
                             "alone only REFUSES the stage seat; without the retry the owed reappearance " +
                             "never lands after the return warp and the Echo is gone for the session");
        }

        private static void CheckReturnSeat(List<string> failures)
        {
            var hero = new Vector3(2.53f, 0.08f, -19.99f);
            Vector3 seat = EchoWorldPresence.ReturnSeat(hero, Vector3.forward, Vector3.right);
            float planarDistance = Vector2.Distance(new Vector2(hero.x, hero.z), new Vector2(seat.x, seat.z));
            if (planarDistance < 2f)
                failures.Add("[return-seat] post-battle Echo seat is only " + planarDistance.ToString("0.00") +
                             "m from the hero. The live Wave 1 trace proved both were exactly " + hero +
                             "; the companion must return beside/behind, never inside the hero capsule");
            if (Mathf.Abs(seat.y - hero.y) > 0.001f)
                failures.Add("[return-seat] the pure offset changed Y before the NavMesh grounding step");
        }

        // -- 1 [lifecycle] ----------------------------------------------------
        // Drives the three real transitions in one process and counts real bodies.
        // No rendering is involved: the assertion is the live Pet count in the scene
        // plus the appearance owner's own session state.
        private static void CheckLifecycle(List<string> failures, List<string> notes)
        {
            if (Resources.Load<GameObject>("Pets/" + EscortSpecies) == null)
            {
                notes.Add("group [lifecycle] SKIPPED - no body asset at Resources/Pets/" + EscortSpecies +
                          " (the summon would take the billboard fallback and Fail-trace on assets, " +
                          "which is an art gap, not a lifecycle regression)");
                return;
            }

            GameStateService priorInstance = GameStateService.Instance;
            GameObject gssGo = null;
            GameObject heroGo = null;
            GameState throwaway = null;
            bool installed = false;

            // Only deployers THIS group creates are torn down at the end — a scene-loaded
            // fixture must survive the oracle untouched.
            var preexistingDeployers = new HashSet<int>();
            try
            {
                var before = UnityEngine.Object.FindObjectsByType<PetDeployer>(FindObjectsSortMode.None);
                if (before != null)
                    foreach (var d in before) if (d != null) preexistingDeployers.Add(d.GetInstanceID());
            }
            catch (Exception ex) { notes.Add("pre-existing deployer snapshot failed: " + ex.Message); }

            try
            {
                throwaway = ScriptableObject.CreateInstance<GameState>();
                gssGo = new GameObject("GameStateService (echo-presence-oracle)");
                var gss = gssGo.AddComponent<GameStateService>();
                if (!TryInstallHeadlessState(gss, throwaway, out string installErr))
                {
                    notes.Add(DeNelle.Editor.Regression.RegressionOutcome.PartialSkip(
                        "group [lifecycle]", "needs the headless state seam - " + installErr));
                    return;
                }
                installed = true;
                if (gss.State == null)
                {
                    notes.Add(DeNelle.Editor.Regression.RegressionOutcome.PartialSkip(
                        "group [lifecycle]", "throwaway state did not install"));
                    return;
                }

                // The pet-acquisition gate: SummonAt refuses to conjure a pet nobody owns.
                var def = PetCatalog.FindBySpecies(EscortSpecies);
                gss.State.StarterPetId = def != null && !string.IsNullOrEmpty(def.Id)
                    ? def.Id : "pet-" + EscortSpecies;

                // A "Player"-tagged stand-in so the reappearance resolves the hero's side
                // (the builtin tag always exists; the Heart/origin fallbacks are the Warn paths).
                heroGo = new GameObject("Hero (echo-presence-oracle)") { tag = "Player" };

                // Baseline: clear any body a previous oracle left, THEN zero the session state.
                EchoWorldPresence.NotifyEscortComplete("oracle baseline sweep");
                EchoWorldPresence.ResetSessionState();
                if (EchoWorldPresence.LiveBodyCount != 0)
                {
                    notes.Add("group [lifecycle] SKIPPED (the scene still holds " +
                              EchoWorldPresence.LiveBodyCount + " pet body/bodies the sweep could not " +
                              "remove - a scene-loaded fixture, not a lifecycle defect)");
                    return;
                }

                // --- (a) the body EXISTS during the escort beat --------------------
                if (!EchoWorldPresence.SummonEscortBody(Vector3.zero, "oracle: escort beat"))
                {
                    failures.Add("[lifecycle] SummonEscortBody returned false with the body asset present " +
                                 "and a pet owned - the FTUE beat 'Follow {guide} to the gate' would have " +
                                 "nothing in the world to follow (the WO-961 defect)");
                    return;
                }
                if (EchoWorldPresence.LiveBodyCount != 1)
                    failures.Add("[lifecycle] after the escort summon the world holds " +
                                 EchoWorldPresence.LiveBodyCount + " pet body/bodies, expected exactly 1 " +
                                 "(the guide is the ONLY Echo with a world body - the roster stays portrait cards)");

                // --- (b) it is GONE the moment the beat completes ------------------
                EchoWorldPresence.NotifyEscortComplete("oracle: gate beat complete");
                if (EchoWorldPresence.LiveBodyCount != 0)
                    failures.Add("[lifecycle] the Echo body SURVIVED the escort beat (" +
                                 EchoWorldPresence.LiveBodyCount + " still in the world). Owner ruling: " +
                                 "'it takes you to the gate, gives you your dialogue, then it disappears'");
                if (!EchoWorldPresence.DespawnedAfterEscort)
                    failures.Add("[lifecycle] NotifyEscortComplete did not record the vanish - the " +
                                 "reappearance gate reads this flag, so the Echo could never come back");
                if (!EchoWorldPresence.AwaitingBattleReappear)
                    failures.Add("[lifecycle] after the vanish the Echo is not awaiting its post-battle " +
                                 "return - it would be gone for the rest of the session");

                // --- (b2) WO-1696: it does NOT come back ON THE ARENA STAGE ---------
                // The battle arena is a MASKED WARP, not a scene: the hero is teleported to
                // BattleArena's ArenaCentre (5000,0,5000) inside Main_Castle_Overworld and warped
                // home on resolve. On a WIN the battle lock drops SECONDS before that return warp,
                // so the resolve edge can fire while the hero still stands on the stage. Captured
                // proof, Builds/device-frames/2026-09-10_1520_logcat.txt: "battle RESOLVED" :714168
                // -> "echo REAPPEAR: 'Echo' returned at (4997.29, 0.08, 5002.89)" :714180 -> the
                // return warp only at :715040. The companion was BORN 7km from town and stayed
                // there; every later arena entry warped the hero back to the same wolf, which is
                // what the owner saw. The once-per-session rule is NOT the defect and is untouched
                // here: the guard must survive the deferral so the one return still happens in town.
                //
                // ⚠ DRIVE THE TRANSFORM THE PRODUCTION CODE ACTUALLY READS, NOT OUR STAND-IN.
                // The first version of this case moved `heroGo` and asserted nothing happened. It
                // FAILED on the fixed tree (chain 49, Builds/wave10h-reg1) with all four assertions
                // red, and the run's own trace says why: the appearance owner resolves its hero with
                // GameObject.FindWithTag("Player"), and in a loaded editor scene that returns a
                // DIFFERENT Player-tagged object -- "escort staging moved from overlapping anchor
                // (0.00, 0.00, 0.00) to navmesh-safe (0.00, 0.04, 3.25); hero=(0.00, 0.93, 0.00)".
                // Our stand-in has y=0; the object the code saw has y=0.93. The reappearance then
                // seated at "(1.40, 0.03, -2.40)" -- a ReturnSeat around the ORIGIN hero. Moving
                // `heroGo` was invisible to the gate AND to the seat, so the case proved nothing
                // about the gate; it only proved which transform the oracle was allowed to move.
                // (The pre-existing "Player-tagged stand-in" comment above carries the same
                // assumption and is equally inert in a loaded scene -- left alone here, but noted.)
                //
                // So: stage EVERY "Player"-tagged object, restore them all in a finally, and make the
                // case self-proving. All of them, not the one FindWithTag happens to return today:
                // that call's choice among several tagged objects is unspecified, and an oracle that
                // depends on it is the same unproven assumption in a new costume. Stage the whole
                // set and the gate's lookup CANNOT resolve a hero that is off the stage.
                GameObject[] taggedHeroes = GameObject.FindGameObjectsWithTag("Player");
                if (taggedHeroes == null || taggedHeroes.Length == 0)
                {
                    notes.Add(DeNelle.Editor.Regression.RegressionOutcome.PartialSkip(
                        "group [arena-stage]", "no 'Player'-tagged object resolved, so the gate's own hero " +
                        "lookup has nothing to stage"));
                }
                else
                {
                    var townPoses = new Vector3[taggedHeroes.Length];
                    for (int i = 0; i < taggedHeroes.Length; i++)
                        if (taggedHeroes[i] != null) townPoses[i] = taggedHeroes[i].transform.position;
                    try
                    {
                        // A seat inside BattleArena's staged arena. Deliberately NOT a re-derivation
                        // of the arena geometry: the very next line asks the predicate whether this
                        // seat is arena, so if that centre or radius ever moves the case SKIPS
                        // loudly rather than passing on a stale coordinate.
                        Vector3 staged = new Vector3(5000f, 0.08f, 4991f);
                        foreach (GameObject tagged in taggedHeroes)
                            if (tagged != null) tagged.transform.position = staged;
                        if (!DeNelle.Village.Arena.BattleArena.IsArenaPosition(staged))
                        {
                            notes.Add(DeNelle.Editor.Regression.RegressionOutcome.PartialSkip(
                                "group [arena-stage]", "BattleArena.IsArenaPosition does not read " + staged +
                                " as the staged arena - the arena centre/radius moved, so this case can no " +
                                "longer stage a hero on it and must be re-pointed, not trusted"));
                        }
                        else
                        {
                            bool seatedOnStage = EchoWorldPresence.TryReappearAfterBattle(
                                "oracle: battle resolved while the hero was still on the arena stage");
                            if (seatedOnStage)
                                failures.Add("[arena-stage] the Echo reappeared with the hero standing INSIDE " +
                                             "the staged arena (IsArenaPosition true at " + staged + ", measured). " +
                                             "It is seated ~7km from town and stranded there for the session - " +
                                             "the 2026-09-10 device capture of exactly that (REAPPEAR at " +
                                             "(4997.29, 0.08, 5002.89), 860 log lines BEFORE the return warp)");
                            if (EchoWorldPresence.LiveBodyCount != 0)
                                failures.Add("[arena-stage] the arena-stage resolve left " +
                                             EchoWorldPresence.LiveBodyCount + " body/bodies in the world; the " +
                                             "Echo must not be summoned onto the stage");
                            if (EchoWorldPresence.ReappearedThisSession)
                                failures.Add("[arena-stage] the deferred reappearance CONSUMED the " +
                                             "once-per-session guard. The Echo would then never return at all - " +
                                             "the deferral must leave the guard unconsumed (same contract as the " +
                                             "null-summon branch)");
                            if (!EchoWorldPresence.AwaitingBattleReappear)
                                failures.Add("[arena-stage] after the deferral the Echo is no longer awaiting " +
                                             "its return, so EchoPresenceWatcher's retry would never fire and " +
                                             "the companion is gone for the rest of the session");
                        }
                    }
                    finally
                    {
                        // Leave every scene fixture exactly as found - restored even if an assertion
                        // above throws, and BEFORE (c) runs, so the owed return lands in town.
                        for (int i = 0; i < taggedHeroes.Length; i++)
                            if (taggedHeroes[i] != null) taggedHeroes[i].transform.position = townPoses[i];
                    }
                }

                // --- (c) it REAPPEARS after the battle, exactly ONCE ---------------
                if (!EchoWorldPresence.TryReappearAfterBattle("oracle: first battle resolved"))
                    failures.Add("[lifecycle] the Echo did NOT reappear after a resolved battle - " +
                                 "'The only time it reappears is after your battle' is unmet");
                else if (EchoWorldPresence.LiveBodyCount != 1)
                    failures.Add("[lifecycle] the post-battle reappearance left " +
                                 EchoWorldPresence.LiveBodyCount + " bodies in the world, expected exactly 1");
                if (!EchoWorldPresence.ReappearedThisSession)
                    failures.Add("[lifecycle] the reappearance did not consume its once-per-session guard");

                int afterFirst = EchoWorldPresence.LiveBodyCount;
                if (EchoWorldPresence.TryReappearAfterBattle("oracle: a SECOND battle resolved"))
                    failures.Add("[lifecycle] a second battle-resolve summoned the Echo AGAIN - the " +
                                 "reappearance must fire exactly once per session (the WO-360 static-guard " +
                                 "idiom this reuses exists precisely to stop that)");
                if (EchoWorldPresence.LiveBodyCount != afterFirst)
                    failures.Add("[lifecycle] the second battle-resolve changed the body count from " +
                                 afterFirst + " to " + EchoWorldPresence.LiveBodyCount);
            }
            catch (Exception ex)
            {
                failures.Add("[lifecycle] threw: " + ex.GetType().Name + ": " + ex.Message);
            }
            finally
            {
                // Leave the process exactly as found: no body, no deployer, no session state.
                try { EchoWorldPresence.NotifyEscortComplete("oracle teardown"); }
                catch (Exception ex) { notes.Add("teardown sweep threw: " + ex.Message); }
                try { EchoWorldPresence.ResetSessionState(); }
                catch (Exception ex) { notes.Add("teardown state reset threw: " + ex.Message); }
                try
                {
                    var deployers = UnityEngine.Object.FindObjectsByType<PetDeployer>(FindObjectsSortMode.None);
                    if (deployers != null)
                        foreach (var d in deployers)
                            if (d != null && !preexistingDeployers.Contains(d.GetInstanceID()))
                                UnityEngine.Object.DestroyImmediate(d.gameObject);
                }
                catch (Exception ex) { notes.Add("teardown deployer cleanup threw: " + ex.Message); }
                if (heroGo != null) UnityEngine.Object.DestroyImmediate(heroGo);
                if (gssGo != null) UnityEngine.Object.DestroyImmediate(gssGo);
                if (throwaway != null) UnityEngine.Object.DestroyImmediate(throwaway);
                if (installed) TrySetInstanceStatic(priorInstance);
            }
        }

        // -- 2 [verb] ---------------------------------------------------------
        private static void CheckDespawnVerb(List<string> failures)
        {
            const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic |
                                     BindingFlags.Static | BindingFlags.Instance;
            var t = typeof(PetDeployer);
            // NOTE: by name, not GetMethod(name, flags) — DespawnEcho is overloaded
            // (the public verb plus the private torn-set form), and GetMethod would
            // throw AmbiguousMatchException rather than answer the question asked.
            if (!HasMethod(t, "DespawnEcho", All))
                failures.Add("[verb] PetDeployer.DespawnEcho is gone - WO-1108 added the FIRST despawn " +
                             "path any pet has ever had; without it 'then it disappears' is unbuildable");
            if (!HasMethod(t, "DespawnAllEchoBodies", All))
                failures.Add("[verb] PetDeployer.DespawnAllEchoBodies is gone - the orphan sweep is what " +
                             "removes a body whose deployer died, i.e. the body the player still sees");
            if (t.GetProperty("LiveBodyCount", All) == null)
                failures.Add("[verb] PetDeployer.LiveBodyCount is gone - the honest scene-counted 'is the " +
                             "Echo in the world' predicate this suite asserts against");

            string src = ReadCode(DeployerSrc, failures);
            if (src == null) return;
            if (src.IndexOf("PetHeroLeash.ClearLeadTarget()", StringComparison.Ordinal) < 0)
                failures.Add("[verb] PetDeployer's despawn no longer clears the guide-lead. Lead state is " +
                             "STATIC: a body destroyed mid-lead strands an anchor nothing can consume and " +
                             "every later SetLeadTarget warns 'ZERO enabled PetHeroLeash' forever");
        }

        // -- 3 [one-owner] ----------------------------------------------------
        // The two-owner defect, made falsifiable: only the appearance owner's file may
        // CALL PetDeployer.SummonAt (its own declaration lives in PetDeployer.cs).
        private static void CheckSingleSummonOwner(List<string> failures)
        {
            var callers = new List<string>();
            try
            {
                // Runtime code only: Assets/_Modules. Editor oracles (this file included)
                // legitimately NAME the method in their own assertions.
                foreach (var path in Directory.GetFiles("Assets/_Modules", "*.cs", SearchOption.AllDirectories))
                {
                    string norm = path.Replace('\\', '/');
                    if (norm.EndsWith(DeployerSrc, StringComparison.OrdinalIgnoreCase)) continue;  // the declaration
                    // CODE ONLY: a file that merely NAMES the summon path in a header or a
                    // FlowTrace message is not a second appearance owner (EchoAutoDeployTrigger's
                    // own header line 45 does exactly that). The rule is about CALLS.
                    string text = CodeText(File.ReadAllText(path));
                    if (text.IndexOf(".SummonAt(", StringComparison.Ordinal) >= 0) callers.Add(norm);
                }
            }
            catch (Exception ex) { failures.Add("[one-owner] scan failed: " + ex.Message); return; }

            foreach (var caller in callers)
                if (!caller.EndsWith(PresenceSrc, StringComparison.OrdinalIgnoreCase))
                    failures.Add("[one-owner] " + caller + " calls PetDeployer.SummonAt - that is a SECOND " +
                                 "owner of when the Echo appears. WO-1108 collapsed the FTUE grant and the " +
                                 "WO-360 outpost trigger into one owner (EchoWorldPresence) and forbade a " +
                                 "third; route the summon through EchoWorldPresence instead");
            if (callers.Count == 0)
                failures.Add("[one-owner] NOTHING calls PetDeployer.SummonAt any more - the Echo can never " +
                             "get a world body, so the FTUE beat 'Follow {guide} to the gate' points at nothing");
        }

        // -- 4 [seam] ---------------------------------------------------------
        private static void CheckTutorialSeam(List<string> failures)
        {
            string src = ReadCode(FlowSrc, failures);
            if (src == null) return;

            if (src.IndexOf("EchoWorldPresence.NotifyEscortComplete", StringComparison.Ordinal) < 0)
                failures.Add("[seam] TutorialFlow no longer fires the vanish (EchoWorldPresence." +
                             "NotifyEscortComplete). The escort would end with the guide still standing there");
            if (src.IndexOf("EchoWorldPresence.SummonEscortBody", StringComparison.Ordinal) < 0)
                failures.Add("[seam] TutorialFlow no longer routes the escort summon through the appearance " +
                             "owner - a body it spawns directly is a body the owner does not know exists");

            // The vanish must ride the EXISTING lead-clear, not a step-id special case: the
            // lead being in force IS the proof this was the escort beat.
            int clear = src.IndexOf("PetHeroLeash.IsLeading", StringComparison.Ordinal);
            if (clear < 0)
                failures.Add("[seam] TutorialFlow no longer reads PetHeroLeash.IsLeading before clearing the " +
                             "lead - the vanish is then hung on something other than arrival, and the two " +
                             "can disagree about when the escort ended");

            // CODE ONLY (see the header note): the deletion left a tombstone comment naming
            // EnsureGuidePetDeployer at TutorialFlow.cs:1521, and matching that is matching the
            // proof of the removal. The rule below still fails the instant the helper is
            // DECLARED or CALLED again -- which is the thing it was ever protecting.
            if (src.IndexOf("EnsureGuidePetDeployer", StringComparison.Ordinal) >= 0)
                failures.Add("[seam] TutorialFlow's private PetDeployer self-heal is back - it was the " +
                             "FOURTH spelling of the same helper and a private deployer here is how a " +
                             "second body-spawning seam grows back unseen by the appearance owner");
        }

        // -- 5 [husk] ---------------------------------------------------------
        private static void CheckHuskRetired(List<string> failures)
        {
            const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic |
                                     BindingFlags.Static | BindingFlags.Instance;
            var t = typeof(DeNelle.Village.PetTaskController);
            foreach (var member in new[] { "Update", "TickRepair", "EnsureRepair" })
                if (HasMethod(t, member, All))
                    failures.Add("[husk] PetTaskController." + member + " is back - it drove " +
                                 "WallRepairController from a SECOND uncoordinated place on its own cadence, " +
                                 "racing EchoRepairService over the same walls and the same wallet once " +
                                 "WO-1108 made repair passive and count-driven");

            var installer = typeof(DeNelle.Village.PetTaskController).Assembly
                                .GetType("DeNelle.Village.PetTaskInstaller", false);
            if (installer != null)
                failures.Add("[husk] PetTaskInstaller is back - a DontDestroyOnLoad poller that ran " +
                             "FindObjectsByType<Pet> every second for the whole session purely to attach a " +
                             "component with no loop left in it");

            // CODE ONLY (see the header note): PetTaskController.cs:23 documents the retired
            // loop by name. The rule still fails the instant a real RepairAll( CALL returns.
            string src = ReadCode(TaskSrc, failures);
            if (src != null && src.IndexOf("RepairAll(", StringComparison.Ordinal) >= 0)
                failures.Add("[husk] PetTaskController.cs calls RepairAll again - EchoRepairService is the " +
                             "single scanner/spender for structure repair (WO-1108 Lane A)");
        }

        // -- 6 [hygiene] ------------------------------------------------------
        private static void CheckHygiene(List<string> failures)
        {
            foreach (var path in new[] { FlowSrc, PresenceSrc, DeployerSrc, TaskSrc })
            {
                try
                {
                    if (!File.Exists(path)) { failures.Add("[hygiene] missing " + path); continue; }
                    var bytes = File.ReadAllBytes(path);
                    for (int i = 0; i < bytes.Length; i++)
                        if (bytes[i] == 0) { failures.Add("[hygiene] embedded NUL in " + path); break; }
                }
                catch (Exception ex) { failures.Add("[hygiene] " + path + ": " + ex.Message); }
            }
        }

        // -- helpers ----------------------------------------------------------
        /// <summary>Name-only member probe. GetMethod(name, flags) throws
        /// AmbiguousMatchException on an overloaded name, which would read as a suite
        /// crash instead of the yes/no these rules actually ask.</summary>
        private static bool HasMethod(Type t, string name, BindingFlags flags)
        {
            if (t == null || string.IsNullOrEmpty(name)) return false;
            foreach (var m in t.GetMethods(flags))
                if (m != null && string.Equals(m.Name, name, StringComparison.Ordinal)) return true;
            return false;
        }

        private static string ReadSrc(string path, List<string> failures)
        {
            try
            {
                if (File.Exists(path)) return File.ReadAllText(path);
                failures.Add("[src] missing " + path);
            }
            catch (Exception ex) { failures.Add("[src] " + path + ": " + ex.Message); }
            return null;
        }

        /// <summary>ReadSrc reduced to CODE ONLY. Every source-lint in this suite uses this,
        /// never the raw text (the [hygiene] NUL scan reads bytes and is deliberately raw).</summary>
        private static string ReadCode(string path, List<string> failures)
        {
            string raw = ReadSrc(path, failures);
            return raw == null ? null : CodeText(raw);
        }

        /// <summary>Non-comment lines only, with STRING LITERAL CONTENTS blanked -- so neither a
        /// header paragraph, a tombstone comment recording a DELETION, nor a seam named inside a
        /// FlowTrace message can satisfy or trip a rule. Mirrors
        /// SpirePlansCelebrationRegression.CodeLines/StripStringLiterals (same defect, same fix,
        /// 2026-08-16); kept file-local, matching the convention across this folder.</summary>
        private static string CodeText(string source)
        {
            if (string.IsNullOrEmpty(source)) return string.Empty;
            var sb = new StringBuilder(source.Length);
            foreach (var raw in source.Split('\n'))
            {
                string t = raw.TrimStart();
                // Whole-line comments: `//`, `///`, and the body/opening of a /* */ block.
                if (t.StartsWith("//", StringComparison.Ordinal) ||
                    t.StartsWith("*",  StringComparison.Ordinal) ||
                    t.StartsWith("/*", StringComparison.Ordinal)) { sb.Append('\n'); continue; }
                sb.Append(StripStringLiterals(raw)).Append('\n');
            }
            return sb.ToString();
        }

        /// <summary>Blanks the CONTENTS of every double-quoted literal on the line (the quotes stay,
        /// so the line still reads), honouring backslash escapes, and drops a trailing `//` comment
        /// that follows code.</summary>
        private static string StripStringLiterals(string line)
        {
            var sb = new StringBuilder(line.Length);
            bool inStr = false;
            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (!inStr && c == '/' && i + 1 < line.Length && line[i + 1] == '/') break;   // trailing comment
                if (c == '"' && (i == 0 || line[i - 1] != '\\'))
                {
                    inStr = !inStr;
                    sb.Append(c);
                    continue;
                }
                sb.Append(inStr ? ' ' : c);
            }
            return sb.ToString();
        }

        // Headless state-install (editmode never runs Awake) — mirrors
        // EchoSpecializationRegression / OfflineHarvestRegression, not a new spelling.
        private static bool TryInstallHeadlessState(GameStateService svc, GameState state, out string err)
        {
            err = null;
            var stateField = typeof(GameStateService).GetField("_state",
                BindingFlags.NonPublic | BindingFlags.Instance);
            if (stateField == null)
            { err = "GameStateService._state field not found by reflection"; return false; }
            stateField.SetValue(svc, state);
            if (!TrySetInstanceStatic(svc))
            { err = "GameStateService._instance static not found by reflection"; return false; }
            return true;
        }

        private static bool TrySetInstanceStatic(GameStateService svc)
        {
            var f = typeof(GameStateService).GetField("_instance",
                BindingFlags.NonPublic | BindingFlags.Static);
            if (f == null) return false;
            f.SetValue(null, svc);
            return true;
        }
    }
}

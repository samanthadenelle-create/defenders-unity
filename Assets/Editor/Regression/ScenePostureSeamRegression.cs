// =============================================================================
// ScenePostureSeamRegression [scene-posture-seam] -- WO-1436.
// -----------------------------------------------------------------------------
// FOR EVERY SCENE IN THE BUILD LIST, the resolved HUD posture is asserted against
// that scene's DECLARED KIND. A raid scene resolving to a peaceful posture FAILS
// the build.
//
// THE TICKET (owner felt-test 2026-09-06, build 2026.09.06.358161, verbatim):
//     "in the raid, i had no way to fight. No combast skills"
//
// THE PROVEN ROOT (logs/debug/raid-no-abilities-2026-09-06.log -- read BEFORE any
// theory, per CLAUDE.md section 12). The abilities were pushed INSIDE the raid
// scene, 22 ms after the kit bootstrapped:
//
//   12:58:57.512 [Flow:HudKit] VillageHudController bootstrapped the kit
//                              (scene 'RaidBase_raider_camp_small')
//   12:58:57.534 [HeroAbilitiesHudBridge] Pushed ability bar for class 'mage':
//                              Fireball, Arcane Shell, Drain, Poison Cloud
//
// ...and the context resolver answered, for the whole raid:
//
//   [Flow:HUD] context inputs: wave=False battleLock=False pursuit=False
//              inVillage=False modal=False buildMode=False
//              scene='RaidBase_raider_camp_small' -> Overworld
//
// EVERY combat input False inside a live assault, so the posture resolved to the
// peaceful dock. Measured off the same capture, the posture flapped
// calm(explore) <-> hostile(*) SEVEN times across 49 s, tracking transient pursuit
// pulses instead of the committed fight.
//
// ── WHY NO ORACLE CAUGHT IT, AND WHY THIS ONE IS SHAPED LIKE THIS ────────────
// 394+ suites were green while the raid HUD had no reachable abilities. Every
// existing HUD oracle asks "does the bar render its faces correctly FOR a given
// posture?" -- and it does; the bar was never broken. NONE asked "is the posture
// RIGHT for the scene the player is standing in?" The parts all worked and the
// CONNECTION between them was never checked (the same species as WO-1430's
// doorless panels).
//
// So this suite deliberately does NOT test a widget, a layout or a face count. It
// tests the SEAM: scene kind -> HUD context -> posture. It enumerates the build
// list rather than a hand-written scene array, because a hand-written list is the
// duplicated state that goes stale (CLAUDE.md sections 2/5/16) -- a raid scene added
// tomorrow is covered on the day it is added, and a scene NO predicate can name is
// itself a FAILURE, never a silent skip.
//
// Assembly note: DeNelle.EditorRegression gained a "DeNelle.HUD" reference for
// HudPosture/PostureEvaluator.Derive. Editor -> HUD is legal; the one enforced
// invariant is HUD <-> Village, which is untouched (DeNelle.Tests.EditMode already
// carries the same reference).
// =============================================================================

using System.Collections.Generic;
using System.Text;
using DeNelle.Core;
using DeNelle.Core.HudModel;
using DeNelle.HUD.Kit;
using UnityEditor;

namespace DeNelle.Editor
{
    public static class ScenePostureSeamRegression
    {
        public static bool Run(out string summary)
        {
            var log = new StringBuilder();
            var failures = new List<string>();

            log.AppendLine("[scene-posture-seam] build-list scene kind -> HUD context -> posture:");

            var scenes = EditorBuildSettings.scenes;
            if (scenes == null || scenes.Length == 0)
            {
                summary = "EditorBuildSettings.scenes is EMPTY - the seam oracle has nothing to " +
                          "assert, which is itself a failure (a green tick over zero scenes is " +
                          "the exact false confidence WO-1436 was shipped under).";
                return false;
            }

            int checkedCount = 0;

            foreach (var entry in scenes)
            {
                if (entry == null || !entry.enabled || string.IsNullOrEmpty(entry.path)) continue;

                string scene = SceneNameFromPath(entry.path);
                var kind = HubScenes.Classify(scene);

                // Resolve exactly what the game resolves, through the SAME code the runtime
                // runs (HudContextResolver is the hoisted precedence chain the live
                // HudContextEvaluator calls; Derive is the hoisted PostureEvaluator chain).
                // No second implementation to drift from the thing it claims to test.
                var ctx = HudContextResolver.ResolveForSceneAtRest(scene);
                var posture = PostureEvaluator.Derive(
                    hasContext: true, context: ctx,
                    endStateVisible: false, pursuitActive: false, manualLock: false);

                checkedCount++;
                log.AppendLine($"  {scene,-38} kind={kind,-12} ctx={ctx,-9} posture={HudPostureKeys.Key(posture)}");

                string problem = Check(scene, kind, ctx, posture);
                if (problem != null) failures.Add(problem);
            }

            if (checkedCount == 0)
            {
                summary = "no ENABLED scenes in the build list - nothing was asserted.";
                return false;
            }

            log.AppendLine($"  {checkedCount} enabled build-list scene(s) asserted.");

            if (failures.Count > 0)
            {
                summary = "scene/posture seam FAILED (" + failures.Count + "): " +
                          string.Join(" | ", failures.ToArray());
                return false;
            }

            summary = log.ToString().TrimEnd();
            return true;
        }

        /// <summary>
        /// The per-kind expectation. Returns null when the scene is correct, else the
        /// sentence that says what is wrong -- in words, never a colour or a bare enum pair
        /// (the owner is red/green colourblind; the meaning has to survive as text).
        /// </summary>
        private static string Check(string scene, HubScenes.SceneKind kind,
                                    HudContext ctx, HudPosture posture)
        {
            bool peaceful = posture == HudPosture.CalmTown ||
                            posture == HudPosture.CalmExplore ||
                            posture == HudPosture.Build;

            switch (kind)
            {
                // ── THE ONE THIS TICKET IS ABOUT ─────────────────────────────
                // A raid is entered only through BEGIN ASSAULT, with troops committed and a
                // scored 180 s clock. It must resolve to the ACTIVE-BATTLE posture from the
                // moment it loads -- not once something happens to chase the hero.
                case HubScenes.SceneKind.Raid:
                case HubScenes.SceneKind.TownPractice:
                    if (posture != HudPosture.HostileActiveBattle)
                        return $"'{scene}' is a RAID but resolves posture " +
                               $"{HudPostureKeys.Key(posture)} (context {ctx}). A raid must declare " +
                               "combat the moment it loads - the player entered through BEGIN " +
                               "ASSAULT with troops committed and a scored clock running. This is " +
                               "the WO-1436 P0: a peaceful posture here means the action bar " +
                               "renders the peaceful dock and the hero has no ability faces. Check " +
                               "HubScenes.SceneDeclaresCombat.";
                    return null;

                // A hub is home. It must NEVER boot hostile -- a wave or a pursuit raises the
                // posture at runtime, and that path is covered by HudPostureRegression.
                case HubScenes.SceneKind.Hub:
                case HubScenes.SceneKind.OwnedTown:
                    if (posture != HudPosture.CalmTown)
                        return $"'{scene}' is a HUB but resolves posture " +
                               $"{HudPostureKeys.Key(posture)} (context {ctx}); a hub at rest must " +
                               "be calm(town). A hub reading hostile at rest would pin the combat " +
                               "dock over the town and hide Build/Manage.";
                    return null;

                // Front-end scenes have no world. Anything hostile here is chrome leaking onto
                // the title screen.
                case HubScenes.SceneKind.FrontEnd:
                    if (!peaceful)
                        return $"'{scene}' is a FRONT-END scene but resolves posture " +
                               $"{HudPostureKeys.Key(posture)}; menus must never resolve hostile.";
                    return null;

                // Dungeons are EXPLORED before they are fought (owner 2026-07-05 peaceful
                // default; WO-1112). The pursuit arc raises the posture when something
                // actually threatens the hero -- deliberately NOT the scene itself.
                case HubScenes.SceneKind.Dungeon:
                    if (posture != HudPosture.CalmExplore)
                        return $"'{scene}' is a DUNGEON but resolves posture " +
                               $"{HudPostureKeys.Key(posture)} at rest; a dungeon is explored " +
                               "before it is fought and must stay calm(explore) until something " +
                               "pursues the hero (owner 2026-07-05).";
                    return null;

                // Open-air enemy outposts. PINNED TO TODAY'S BEHAVIOUR ON PURPOSE, not because
                // it is known to be right: WO-1436 was raised and proven on RaidBase_* only,
                // and whether a garrison is also a committed assault is an owner ruling nobody
                // has made. Pinning it means the day someone widens SceneDeclaresCombat, THIS
                // LINE FAILS and forces the ruling to be recorded instead of absorbed.
                case HubScenes.SceneKind.EnemyOutpost:
                    if (posture != HudPosture.CalmExplore)
                        return $"'{scene}' is an ENEMY OUTPOST and resolves " +
                               $"{HudPostureKeys.Key(posture)} at rest, where the pinned behaviour " +
                               "is calm(explore). If this changed deliberately, get the owner's " +
                               "ruling on whether Garrison_*/Outpost* are committed assaults like " +
                               "RaidBase_*, record it, and update HubScenes.SceneDeclaresCombat " +
                               "and this case together.";
                    return null;

                // ATBBattle owns its own screen. At REST it is calm, and that is correct: the
                // staged fight raises BattleLock.IsInBattle at runtime, which the at-rest model
                // deliberately excludes (it models the GROUND, not a live session).
                case HubScenes.SceneKind.Battle:
                    if (!peaceful)
                        return $"'{scene}' (ATB battle) resolves {HudPostureKeys.Key(posture)} at " +
                               "rest; the ATB scene's posture comes from BattleLock at runtime, " +
                               "not from the ground.";
                    return null;

                case HubScenes.SceneKind.Overworld:
                    if (posture != HudPosture.CalmExplore)
                        return $"'{scene}' is OVERWORLD but resolves " +
                               $"{HudPostureKeys.Key(posture)} at rest; open world is calm(explore) " +
                               "until something threatens the hero.";
                    return null;

                // THE GENERAL-FORM GUARD. An unnamed scene is not "probably fine" - it is a
                // scene whose posture nobody has decided, shipping to players. This is the half
                // of the oracle that catches the NEXT WO-1436 rather than this one.
                default:
                    return $"'{scene}' is in the build list but NO HubScenes predicate names it " +
                           $"(SceneKind.Unknown, resolves {HudPostureKeys.Key(posture)}). Classify " +
                           "it in HubScenes.Classify and give it an expectation here - do not " +
                           "delete this check to go green.";
            }
        }

        private static string SceneNameFromPath(string path)
        {
            int slash = path.LastIndexOf('/');
            int dot = path.LastIndexOf('.');
            int start = slash + 1;
            int len = (dot > start ? dot : path.Length) - start;
            return len > 0 ? path.Substring(start, len) : path;
        }
    }
}

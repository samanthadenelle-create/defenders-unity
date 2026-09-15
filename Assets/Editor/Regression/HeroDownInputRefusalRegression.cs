// =============================================================================
// HeroDownInputRefusalRegression (WO-1750) — a hero the health model calls DEAD
// must not still be swinging. Source-structural + pure-predicate, headless,
// milliseconds, no play mode.
// -----------------------------------------------------------------------------
// THE DEFECT THIS PINS (captured, not theorised):
//   Owner felt-test on the Seeker, tester build 2026.09.15.371127, scene
//   RaidBase_IronBastion. Screenshot_20260915-134632.png: Grom Lv15, the top-left
//   HP bar with NO fill, hero mid-swing on a live Hollow Acolyte. Device logcat,
//   the same seconds:
//     09-15 13:46:29.381 [Flow:EnemyAggro] raidboss-iron_bastion: still steered at
//     the hero via Enemy.DriveNav/brain while HeroHealth.IsAlive=false - pursuit
//     pulse NOT stamped and this body's own claim revoked
//   HeroHealth.IsAlive is `_hp > 0f`, so that line and the empty bar are the same
//   fact: the health model had the hero at zero while the player was still playing.
//
// THE MECHANISM THIS SUITE EXISTS TO KEEP CLOSED:
//   The death path turns the hero's input surfaces off BY COMPONENT —
//   HeroHealth.HandleDeath sets `_locomotion.enabled = false` and
//   `_abilities.enabled = false`, and EnterDeathFreeze sets `_pac.enabled = false`.
//   `enabled = false` suppresses Unity's own callbacks. It does NOT make a public
//   method unreachable, and the phone's one attack button does not go through
//   Update — HudKitCommandBridge resolves its targets with
//     Object.FindAnyObjectByType<PlayerAttackController>()   (:108)
//     Object.FindAnyObjectByType<HeroAbilities>()            (:109)
//   which filter on GameObject ACTIVE state, never on component ENABLED state, and
//   then calls abilities.TryCast(Q) and atk.TriggerBasicAttack() DIRECTLY. Neither
//   method had a dead-hero gate. So on mobile the death freeze disabled three
//   components and changed nothing about what the attack button does — which is
//   exactly why this was only ever felt on the Seeker.
//
// WHAT IS PINNED:
//   1 [predicate]  HeroHealth.EvaluateInputRefusedForDeath's truth table, including
//                  the "no health model at all = never refuse" row that keeps test
//                  scenes and headless rigs playable.
//   2 [attack]     PlayerAttackController.TriggerBasicAttack refuses through that
//                  predicate BEFORE it can start a swing.
//   3 [cast]       HeroAbilities.TryCast refuses through the same predicate. Pinned
//                  separately because the bridge calls TryCast(Q) one line BEFORE
//                  the melee fallback: guarding only the sweep would leave a downed
//                  ranger/mage still firing its locked Q.
//   4 [one-rule]   Both refusals read HeroHealth's own death state rather than
//                  re-deriving one, and neither re-uses HeroLocomotion.InputSuppressed
//                  (that latch is the dialogue beat's, and its WO-1714 stuck-gate
//                  watchdog would Fail through every raid-long hero death).
//   5 [watchdog]   HeroHealth.Update still carries the zero-HP-with-no-death-latch
//                  alarm — the instrument that discriminates "the death path ran and
//                  input survived it" from "HP reached zero without ever entering
//                  the lethal branch". §12 forbids stripping instrumentation.
//   6 [raid-hud]   The raid-side death handler still exists and still says so on
//                  screen: HeroHealth's live-raid branch latches the death on the
//                  scorer and calls RaidDeployController.NotifyHeroDown, which sets
//                  the EndStateVM.HeroDownArmyFightsOn status line. This is the
//                  "HUD shows the death state" half of the acceptance, pinned
//                  against the behaviour that ALREADY EXISTS (WO-1526) rather than
//                  a new screen invented by this ticket.
//
// Contract mirrors the other covenant suites: Run(out string reason).
//   true  = pass (reason = one-line summary)
//   false = fail (reason = the exact invariant that broke)
// Registered in DataRegression.RunAll with the DISTINCT [hero-down-input] tag.
// Standalone: run-unity-method DeNelle.Editor.Regression.HeroDownInputRefusalRegression.RunAll
// =============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using DeNelle.Village;

namespace DeNelle.Editor.Regression
{
    public static class HeroDownInputRefusalRegression
    {
        private const string HealthSrc    = "Assets/_Modules/Village/Hero/HeroHealth.cs";
        private const string AttackSrc    = "Assets/_Modules/Village/Enemies/PlayerAttackController.cs";
        private const string AbilitiesSrc = "Assets/_Modules/Village/Hero/HeroAbilities.cs";
        private const string DeploySrc    = "Assets/_Modules/Village/Troops/RaidDeployController.cs";

        /// <summary>The one predicate both refusals must route through.</summary>
        private const string PredicateCall = "HeroHealth.InputRefusedForDeath(gameObject)";

        /// <summary>Standalone batch entry - prints its own distinct marker.</summary>
        public static void RunAll()
        {
            if (Run(out string reason)) Debug.Log("HERO_DOWN_INPUT_OK - " + reason);
            else Debug.LogError("HERO_DOWN_INPUT_FAIL: " + reason);
        }

        /// <summary>Covenant contract (DataRegression-shaped). Never throws.</summary>
        public static bool Run(out string reason)
        {
            var failures = new List<string>();

            Case1Predicate(failures);
            Case2AttackRefuses(failures);
            Case3CastRefuses(failures);
            Case4OneRule(failures);
            Case5ZeroHpWatchdog(failures);
            Case6RaidHandlerAndHud(failures);
            Case7RecoveryRunsTheOnePath(failures);

            if (failures.Count > 0)
            {
                reason = "WO-1750 hero-down input refusal: " + string.Join(" | ", failures);
                return false;
            }
            reason = "WO-1750 hero-down input refusal: predicate truth table + attack/cast direct-call " +
                     "refusals through the ONE death rule + zero-HP-no-latch watchdog + the live-raid " +
                     "hero-down handler and its status line + the single-body death sequence the " +
                     "recovery re-enters exactly once all intact (7/7 cases).";
            return true;
        }

        // ── 1 [predicate] ───────────────────────────────────────────────────────
        private static void Case1Predicate(List<string> failures)
        {
            // A live hero is never refused, latch clear.
            if (HeroHealth.EvaluateInputRefusedForDeath(true, true, false))
                failures.Add("[predicate] a LIVING hero with no death latch is being refused input - " +
                             "that is a frozen player, the opposite defect and a worse one.");

            // HP at zero -> refused. THIS row is the owner's screenshot.
            if (!HeroHealth.EvaluateInputRefusedForDeath(true, false, false))
                failures.Add("[predicate] a hero the health model reports NOT alive is still allowed to " +
                             "act - this is the captured RaidBase_IronBastion state (empty HP bar, hero " +
                             "mid-swing, enemy brain logging IsAlive=false).");

            // Latched dead but HP somehow back above zero -> still refused until the
            // death cycle clears the latch (Respawn / RestoreToFull both clear it).
            if (!HeroHealth.EvaluateInputRefusedForDeath(true, true, true))
                failures.Add("[predicate] the death LATCH alone no longer refuses input - a hero part-way " +
                             "through the death cycle (topped up but not yet cleared) would act during " +
                             "its own down-beat.");

            // No health model at all = never refuse. Test scenes and headless rigs must
            // stay playable; this is the same conservative reading BattleArena takes
            // ("bool heroAlive = hh == null || hh.IsAlive").
            if (HeroHealth.EvaluateInputRefusedForDeath(false, false, true))
                failures.Add("[predicate] a rig with NO HeroHealth at all now refuses input - every test " +
                             "scene and headless hero rig without a health model just went inert.");
        }

        // ── 2 [attack] ──────────────────────────────────────────────────────────
        private static void Case2AttackRefuses(List<string> failures)
        {
            string body = ExtractMethod(AttackSrc, "public bool TriggerBasicAttack()", failures);
            if (body == null) return;

            int guard = body.IndexOf(PredicateCall, StringComparison.Ordinal);
            if (guard < 0)
            {
                failures.Add("[attack] TriggerBasicAttack no longer refuses through " + PredicateCall +
                             ". It is a DIRECT-CALL entry point reached by HudKitCommandBridge past the " +
                             "death freeze's `_pac.enabled = false`, so without this gate a downed hero " +
                             "keeps swinging from the phone's one attack button.");
                return;
            }

            int start = body.IndexOf("StartAttack()", StringComparison.Ordinal);
            if (start >= 0 && guard > start)
                failures.Add("[attack] the hero-down refusal in TriggerBasicAttack sits AFTER StartAttack() - " +
                             "the swing has already begun by the time the guard is read.");

            if (!body.Contains("input-while-down-attack"))
                failures.Add("[attack] the hero-down refusal in TriggerBasicAttack is SILENT (no FlowTrace " +
                             "throttle key 'input-while-down-attack'). CLAUDE.md sec.12 forbids a silent " +
                             "refusal, and this line IS the instrument that proves a caller is still " +
                             "reaching the method past the component disable.");
        }

        // ── 3 [cast] ────────────────────────────────────────────────────────────
        private static void Case3CastRefuses(List<string> failures)
        {
            string body = ExtractMethod(AbilitiesSrc, "public bool TryCast(AbilitySlot slot)", failures);
            if (body == null) return;

            int guard = body.IndexOf(PredicateCall, StringComparison.Ordinal);
            if (guard < 0)
            {
                failures.Add("[cast] HeroAbilities.TryCast no longer refuses through " + PredicateCall +
                             ". HudKitCommandBridge calls TryCast(Q) one line BEFORE it falls through to " +
                             "the melee sweep, so with only the sweep guarded a downed ranger/mage still " +
                             "fires its locked Q.");
                return;
            }

            int resolve = body.IndexOf("Resolve(slot)", StringComparison.Ordinal);
            if (resolve >= 0 && guard > resolve)
                failures.Add("[cast] the hero-down refusal in TryCast sits AFTER the def resolve - the gate " +
                             "must be the first thing the method does, before any cast bookkeeping.");

            if (!body.Contains("input-while-down-cast"))
                failures.Add("[cast] the hero-down refusal in TryCast is SILENT (no FlowTrace throttle key " +
                             "'input-while-down-cast') - see sec.12.");
        }

        // ── 4 [one-rule] ────────────────────────────────────────────────────────
        private static void Case4OneRule(List<string> failures)
        {
            string health = ReadOrFail(HealthSrc, failures);
            if (health == null) return;

            if (!health.Contains("public static bool EvaluateInputRefusedForDeath("))
                failures.Add("[one-rule] HeroHealth.EvaluateInputRefusedForDeath is gone - the refusal rule " +
                             "no longer has a single testable owner, so the two input surfaces are free to " +
                             "drift apart (the duplicated-state failure CLAUDE.md sec.2/5/16 each describe).");

            if (!health.Contains("public static bool InputRefusedForDeath(GameObject"))
                failures.Add("[one-rule] HeroHealth.InputRefusedForDeath(GameObject) is gone - the live " +
                             "reading the input surfaces call has no home.");

            if (!health.Contains("public bool IsDeathLatched"))
                failures.Add("[one-rule] HeroHealth.IsDeathLatched is gone - the predicate can no longer see " +
                             "the death latch the lethal branch sets, so a hero topped up mid-death-cycle " +
                             "reads as fully playable.");

            // The dialogue latch must stay out of this. WO-1714 arms a stuck-gate watchdog
            // on HeroLocomotion.InputSuppressed that Fails when it is held too long; a hero
            // who stays down for the rest of a live raid (WO-1526) would trip it every time.
            string attack = ReadOrFail(AttackSrc, failures);
            if (attack != null)
            {
                string body = ExtractMethod(AttackSrc, "public bool TriggerBasicAttack()", failures);
                if (body != null && body.Contains("_inputSuppressRaw"))
                    failures.Add("[one-rule] the hero-down refusal is writing HeroLocomotion's dialogue " +
                                 "suppression latch. That latch is owned by the dialogue/tutorial beat and " +
                                 "carries the WO-1714 stuck-gate watchdog; a raid-long hero death would " +
                                 "Fail through it on every death.");
            }
        }

        // ── 5 [watchdog] ────────────────────────────────────────────────────────
        private static void Case5ZeroHpWatchdog(List<string> failures)
        {
            string health = ReadOrFail(HealthSrc, failures);
            if (health == null) return;

            if (!health.Contains("WatchZeroHpWithoutDeath"))
            {
                failures.Add("[watchdog] HeroHealth's zero-HP-with-no-death-latch alarm is gone. It is the " +
                             "ONLY instrument that separates 'the death path ran and input survived it' " +
                             "from 'HP reached zero without ever entering the lethal branch' - the exact " +
                             "question the 2026-09-15 capture could not answer. sec.12: instrumentation is " +
                             "permanent; flag it off, never delete it.");
                return;
            }

            if (!health.Contains("ZERO HP WITH NO DEATH LATCH"))
                failures.Add("[watchdog] the zero-HP alarm no longer carries its pullable phrase 'ZERO HP " +
                             "WITH NO DEATH LATCH' - a capture cannot be grepped for it.");

            if (!health.Contains("lethalFrom="))
                failures.Add("[watchdog] the lethal-hit trace no longer names its CALLER (lethalFrom=). " +
                             "Without it the death line's presence proves only that something killed the " +
                             "hero, not what.");
        }

        // ── 6 [raid-hud] ────────────────────────────────────────────────────────
        private static void Case6RaidHandlerAndHud(List<string> failures)
        {
            string health = ReadOrFail(HealthSrc, failures);
            string deploy = ReadOrFail(DeploySrc, failures);
            if (health == null || deploy == null) return;

            // The raid-side handler EXISTS (WO-1526) - this ticket found it, it did not
            // invent one. It is two calls, and both must survive.
            if (!health.Contains("raidScorer.NotifyHeroDied()"))
                failures.Add("[raid-hud] HeroHealth no longer latches the hero death on RaidScoring. The " +
                             "2-star cap is the entire cost of dying inside a live raid (WO-1526); without " +
                             "the latch the result over-pays.");

            if (!health.Contains("NotifyHeroDown()"))
                failures.Add("[raid-hud] HeroHealth's live-raid death branch no longer calls " +
                             "RaidDeployController.NotifyHeroDown - the raid continues with NOTHING on " +
                             "screen telling the player the hero is down, which is the felt half of this " +
                             "ticket's acceptance.");

            if (!deploy.Contains("public void NotifyHeroDown()"))
                failures.Add("[raid-hud] RaidDeployController.NotifyHeroDown is gone - the raid-side hero " +
                             "death handler this ticket cites no longer exists.");

            if (!deploy.Contains("EndStateVM.HeroDownArmyFightsOn"))
                failures.Add("[raid-hud] NotifyHeroDown no longer sets the HeroDownArmyFightsOn status " +
                             "line. A live raid deliberately shows NO end-state modal (it would cover the " +
                             "deploy tray the player now needs more, not less), so that one status line is " +
                             "the whole presentation of the death - losing it means a dead hero with no " +
                             "on-screen death state at all.");
        }

        // ── 7 [recovery] ────────────────────────────────────────────────────────
        // A hero at zero HP with no death latch is a state NO RULING DESCRIBES. Canon already
        // rules what zero HP means (WO-1526 in a live raid: the raid continues, capped at 2
        // stars, "HERO DOWN - your army fights on"), and that outcome is produced by the death
        // sequence. The recovery re-enters THAT sequence rather than inventing a second one.
        // This case pins the three things that make it safe: ONE body, ONE latch writer, and
        // the latch set before anything that could re-enter.
        private static void Case7RecoveryRunsTheOnePath(List<string> failures)
        {
            string health = ReadOrFail(HealthSrc, failures);
            if (health == null) return;

            // (a) ONE death sequence, and it has one home.
            if (!health.Contains("private bool BeginDeathSequence(string cause)"))
            {
                failures.Add("[recovery] HeroHealth.BeginDeathSequence is gone - the lethal block has " +
                             "no single callable home, so the zero-HP recovery can only be re-implemented " +
                             "as a COPY of the death sequence. Two death bodies for one death is exactly " +
                             "the duplicated state this repo keeps paying for.");
                return;
            }

            // Count on a COMMENT-STRIPPED copy. The file documents its own invariants in prose
            // ("_isDead = true is the first mutation"), and a doc line is not a second writer -
            // counting raw text would fail the suite for explaining itself.
            string code = StripComments(health);

            if (CountOf(code, "StartCoroutine(HandleDeath())") != 1)
                failures.Add("[recovery] there is no longer EXACTLY ONE StartCoroutine(HandleDeath()) in " +
                             "HeroHealth. A second one means a second death sequence exists and the two " +
                             "will drift; the recovery must re-enter the first, not grow its own.");

            if (CountOf(code, "_isDead = true") != 1)
                failures.Add("[recovery] the death latch (_isDead = true) has more than one writer. " +
                             "Idempotence is the latch - with two writers neither can promise 'exactly once'.");

            // (b) The recovery actually calls it, with its own distinct cause.
            string watch = ExtractMethod(HealthSrc, "private void WatchZeroHpWithoutDeath()", failures);
            if (watch == null) return;

            if (!watch.Contains("BeginDeathSequence(DeathCauseZeroHpNoLatch)"))
                failures.Add("[recovery] the zero-HP watchdog no longer runs the death sequence. It would " +
                             "then only NAME the anomaly and leave the hero as a spectator who cannot " +
                             "fight and never dies - a state no ruling describes (the felt risk WO-1750 " +
                             "was re-opened to remove).");

            if (!watch.Contains("FlowTrace.Fail"))
                failures.Add("[recovery] the anomaly is no longer raised as a Fail. Recovering silently " +
                             "would hide the defect behind its own fix and the shape would never be " +
                             "diagnosed (sec.12).");

            if (!watch.Contains("ZeroHpNoDeathGraceSeconds"))
                failures.Add("[recovery] the debounce is gone. The effective max is assembled across " +
                             "Awake / Start / SyncGearHp, so a rig mid-assembly can read a transient " +
                             "zero - converting a single frame of that into a hero death would be a far " +
                             "worse defect than the one being closed.");

            // (c) Idempotence, proven by ORDER in the source: the guard is the first statement,
            //     and the latch is set before anything that could re-enter.
            string begin = ExtractMethod(HealthSrc, "private bool BeginDeathSequence(string cause)", failures);
            if (begin == null) return;

            int guardAt   = begin.IndexOf("if (_isDead) return false;", StringComparison.Ordinal);
            int latchAt   = begin.IndexOf("_isDead = true", StringComparison.Ordinal);
            int coroutine = begin.IndexOf("StartCoroutine(HandleDeath())", StringComparison.Ordinal);
            int events    = begin.IndexOf("OnDeath?.Invoke()", StringComparison.Ordinal);

            if (guardAt < 0)
                failures.Add("[recovery] BeginDeathSequence no longer opens with the `if (_isDead) return " +
                             "false;` re-entry guard - a second call would run a second death.");
            if (latchAt < 0 || coroutine < 0)
                failures.Add("[recovery] BeginDeathSequence no longer sets the latch and starts HandleDeath - " +
                             "the sequence this ticket re-enters is not the one that produces the WO-1526 " +
                             "raid outcome.");
            else
            {
                if (guardAt >= 0 && guardAt > latchAt)
                    failures.Add("[recovery] the re-entry guard sits AFTER the latch is set - it is reading " +
                                 "a flag its own body already wrote and can never refuse anything.");
                if (latchAt > coroutine)
                    failures.Add("[recovery] the latch is set AFTER HandleDeath starts. HandleDeath yields, " +
                                 "so a caller re-entering inside that window sees _isDead false and starts " +
                                 "a SECOND death coroutine - the exact swarm defect the original inline " +
                                 "comment warned about.");
                if (events >= 0 && latchAt > events)
                    failures.Add("[recovery] the latch is set AFTER OnDeath fires. A listener that calls " +
                                 "back into the hero during that invoke would re-enter an unlatched death.");
            }

            // (d) The exemptions are explicit, not implied.
            if (!begin.Contains("TutorialFlow.HostilesSuppressedForTutorial"))
                failures.Add("[recovery] the FTUE exemption is gone. TakeDamage floors a would-be-lethal " +
                             "blow at 1 HP during onboarding precisely so the hero cannot die there; a " +
                             "recovery death that ignores that rule reintroduces the 'died in tutorial' " +
                             "defect through a new door.");
            if (!begin.Contains("PracticeCombatPolicy.SceneName"))
                failures.Add("[recovery] the practice-scene exemption is gone - local sparring would start " +
                             "firing real death penalties and global death events.");

            // (e) WHY the recovery cannot simply call TakeDamage. Pinning the early return keeps
            //     the reasoning durable: it is also what makes the bad state terminal.
            string take = ExtractMethod(HealthSrc, "public void TakeDamage(float amount)", failures);
            if (take != null && !take.Contains("if (_hp <= 0f || amount <= 0f) return;"))
                failures.Add("[recovery] TakeDamage's `if (_hp <= 0f || amount <= 0f) return;` early exit " +
                             "has changed. That line is why a hero already at zero can never re-enter the " +
                             "lethal branch through damage (the state is terminal) and therefore why the " +
                             "recovery enters the sequence directly. If it really changed, re-derive the " +
                             "recovery rather than leaving this comment lying.");
            if (take != null && !take.Contains("BeginDeathSequence(DeathCauseLethalHit)"))
                failures.Add("[recovery] TakeDamage no longer routes its lethal branch through " +
                             "BeginDeathSequence - the ordinary death and the recovery death are no longer " +
                             "the same body.");
        }

        // ── helpers ─────────────────────────────────────────────────────────────

        /// <summary>
        /// Blanks `//` line comments and `/* */` blocks so a COUNT of code occurrences is not
        /// inflated by prose that quotes the code. Deliberately naive - it does not model string
        /// literals - which is safe here because every needle counted is a statement, and a
        /// statement appearing inside a string literal in this file would itself be a finding.
        /// </summary>
        private static string StripComments(string src)
        {
            var sb = new System.Text.StringBuilder(src.Length);
            for (int i = 0; i < src.Length; i++)
            {
                if (src[i] == '/' && i + 1 < src.Length && src[i + 1] == '/')
                {
                    while (i < src.Length && src[i] != '\n') i++;
                    if (i < src.Length) sb.Append('\n');
                    continue;
                }
                if (src[i] == '/' && i + 1 < src.Length && src[i + 1] == '*')
                {
                    i += 2;
                    while (i + 1 < src.Length && !(src[i] == '*' && src[i + 1] == '/')) i++;
                    i++;
                    continue;
                }
                sb.Append(src[i]);
            }
            return sb.ToString();
        }

        private static int CountOf(string haystack, string needle)
        {
            int n = 0, at = 0;
            while ((at = haystack.IndexOf(needle, at, StringComparison.Ordinal)) >= 0) { n++; at += needle.Length; }
            return n;
        }

        private static string ReadOrFail(string relPath, List<string> failures)
        {
            try
            {
                string full = Path.Combine(GetProjectRoot(), relPath.Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(full))
                {
                    failures.Add("[io] missing source file '" + relPath + "' - nothing below it can be measured.");
                    return null;
                }
                return File.ReadAllText(full);
            }
            catch (Exception e)
            {
                failures.Add("[io] could not read '" + relPath + "' (" + e.GetType().Name + ": " + e.Message + ").");
                return null;
            }
        }

        /// <summary>
        /// Brace-matched body of the method whose signature line is <paramref name="signature"/>.
        /// Returns the text BETWEEN the outermost braces. Null (with a recorded failure) when the
        /// signature cannot be found - a suite that silently passes over a missing method is worse
        /// than no suite.
        /// </summary>
        private static string ExtractMethod(string relPath, string signature, List<string> failures)
        {
            string src = ReadOrFail(relPath, failures);
            if (src == null) return null;

            int sig = src.IndexOf(signature, StringComparison.Ordinal);
            if (sig < 0)
            {
                failures.Add("[shape] could not locate '" + signature + "' in " + relPath +
                             " - the call site this ticket traced to is unverifiable.");
                return null;
            }

            int open = src.IndexOf('{', sig);
            if (open < 0)
            {
                failures.Add("[shape] '" + signature + "' in " + relPath + " has no body.");
                return null;
            }

            int depth = 0;
            for (int i = open; i < src.Length; i++)
            {
                if (src[i] == '{') depth++;
                else if (src[i] == '}')
                {
                    depth--;
                    if (depth == 0) return src.Substring(open + 1, i - open - 1);
                }
            }

            failures.Add("[shape] '" + signature + "' in " + relPath + " never closes - unbalanced braces.");
            return null;
        }

        private static string GetProjectRoot()
        {
            // Application.dataPath is "<root>/Assets"; the repo root is its parent. Resolved at
            // runtime, never hardcoded - CLAUDE.md sec.0 (the repo root is machine-dependent).
            return Directory.GetParent(Application.dataPath).FullName;
        }
    }
}

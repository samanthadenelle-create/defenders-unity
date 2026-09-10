// =============================================================================
// RepChaseLeashRegression [rep-chase-leash]
//   Markers: REP_CHASE_LEASH_OK / REP_CHASE_LEASH_FAIL
// -----------------------------------------------------------------------------
// Assembly: DeNelle.EditorRegression. Real static drive + source only - no scene,
// no play mode (the rep is a MonoBehaviour that needs a hero, a navmesh and an arena).
//
// THE PIN WO-1093 SHIPPED WITHOUT. The fix landed in 6a5c7a36d; its RESULT file says
// "No regression pin was added ... the WO asked for 'both regression cases present and
// passing' - that acceptance item is OPEN." This suite closes the half of that item
// which is decidable without a driven arena win, mapped to
// WorkOrders/WORK_ORDER_1093_rep_chase_latch_holds_battle_lock.md's ordered checklist:
//
//   item 2 "within 1-2 frames, rep de-aggro (hero lost the leash: d > deaggro)" -> C, D, E, F
//   item 3 "pursuit revoked (key=..., owner=OverworldEncounterSpawner/rep-chase)"-> A, C
//   item 5 "both regression cases present and passing"                           -> this file
//
// Items 1, 4 and 6 (the battle-end clear line, the absence of a quiescence failure, and
// the owner's felt-test) are facts about a DRIVEN arena win. Nothing here substitutes
// for them and this suite does not claim to.
//
// ⛔ WHAT THIS SUITE DELIBERATELY DOES **NOT** RE-TEST, so it is not a second copy:
//   * HudPostureRegression already proves revoke-one-keeps-others and that ClearPursuits
//     returns the HUD to peaceful. Case A therefore drives the ONE lifecycle fact it
//     leaves uncovered and that Deaggro's doc-comment explicitly relies on: revoking a
//     key that was never reported is a NO-OP, not a clear. Deaggro calls RevokePursuit
//     unconditionally on a rep that may never have stamped.
//   * BattleQuiescenceRegression already lints that this file NAMES itself when it
//     pulses (the 'OverworldEncounterSpawner/rep-chase' owner tag) and owns the
//     battle-lock teardown contract. This suite owns the LEASH: the break itself.
//
// THE DEFECT (F8 seq 4767/4768, quoted in the producer's own RCA at
// OverworldEncounterSpawner.cs:1012-1024): the chase had no distance test at all. A rep
// stung before an arena fight kept stamping the pursuit ring from ~7 km away, one frame
// after BattleSessionEnd.Release cleared it - so the ring never emptied, the battle-lock
// never released, and the town stayed locked in combat state after a win.
//
// NO COPIED CONSTANTS (CLAUDE.md sec.8): the ranges are REFLECTED. Case B pins the
// hysteresis RELATION, never the numbers - a retune moves with the code.
//
// NO HOLLOW PASS: a missing producer file or const FAILS, naming what could not be read;
// case A asserts its own presence precondition (a ring left full by an earlier suite
// would make ReportPursuit a silent no-op and every later assertion vacuous). Exactly
// one `return` in Run and it is the failure count.
//
// Standalone batch entry:
//   -Method DeNelle.Editor.Regression.RepChaseLeashRegression.RunStandalone
// =============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using DeNelle.Core.HudModel;
using DeNelle.Village;
using UnityEditor;
using UnityEngine;

namespace DeNelle.Editor.Regression
{
    /// <summary>WO-1093: the rep chase has a leash, the break clears the latch and stands the
    /// rep's OWN pursuit claim down - it never force-clears the ring for everyone.</summary>
    public static class RepChaseLeashRegression
    {
        private const string MarkerOk   = "REP_CHASE_LEASH_OK";
        private const string MarkerFail = "REP_CHASE_LEASH_FAIL";

        private const string SpawnerSrc = "_Modules/Village/Enemies/OverworldEncounterSpawner.cs";
        private const string RepOwnerTag = "OverworldEncounterSpawner/rep-chase";

        [MenuItem("Defenders/Regression/Rep Chase Leash")]
        public static void RunStandalone()
        {
            bool ok = Run(out string reason);
            if (ok) Debug.Log(reason);
            else Debug.LogError(reason);
        }

        public static bool Run(out string reason)
        {
            var failures = new List<string>();
            var log = new StringBuilder();

            // ── A [absent-key] the idempotence Deaggro depends on, driven for real ──
            // Deaggro calls RevokePursuit on every break, including on a rep that never stamped
            // (it holds no separate "did I pulse" flag). If RevokePursuit ever became a clear-all,
            // one rep losing its leash would drop the combat HUD while other bodies still hunt
            // her - the WO-1337 rule this file's own doc-comment cites.
            PostureSignals.ClearPursuits();
            if (PostureSignals.PursuitCount != 0)
            {
                failures.Add("[absent-key] the pursuit ring is not empty after ClearPursuits (count=" +
                             PostureSignals.PursuitCount + "). The precondition failed, so the rest of this case " +
                             "would have asserted nothing: a full ring makes ReportPursuit a silent no-op.");
            }
            else
            {
                const int liveKey = 909101;
                const int neverStampedKey = 909102;
                PostureSignals.ReportPursuit(liveKey, RepOwnerTag);
                if (PostureSignals.PursuitCount != 1)
                {
                    failures.Add("[absent-key] one ReportPursuit did not open exactly one pulse (count=" +
                                 PostureSignals.PursuitCount + ") - the ring is not in the state the rest of this " +
                                 "case reads.");
                }
                else
                {
                    PostureSignals.RevokePursuit(neverStampedKey);
                    if (PostureSignals.PursuitCount != 1 || !PostureSignals.PursuitActive)
                        failures.Add("[absent-key] revoking a key that was never reported dropped a LIVE pulse " +
                                     "(count=" + PostureSignals.PursuitCount + "). Deaggro revokes unconditionally, " +
                                     "so a non-idempotent revoke means one rep breaking off its chase can take the " +
                                     "combat HUD down while other bodies are still hunting the hero.");
                    else
                    {
                        PostureSignals.RevokePursuit(liveKey);
                        if (PostureSignals.PursuitActive || PostureSignals.PursuitCount != 0)
                            failures.Add("[absent-key] revoking the last live key left the pursuit window open " +
                                         "(count=" + PostureSignals.PursuitCount + "). The de-aggro'd rep would " +
                                         "keep the town in combat posture with nothing chasing.");
                        else
                            log.AppendLine("  [absent-key] revoke(unknown) is a no-op; revoke(last live) closes the window");
                    }
                }
            }
            PostureSignals.ClearPursuits();   // leave the shared ring as we found it: peaceful

            // ── B [hysteresis] the leash sits ABOVE the notice range ──
            // ⚠ THE RANGES LIVE ON RepEngageWatcher, NOT ON OverworldEncounterSpawner. They share a
            //   FILE, which is exactly how the first cut of this case got it wrong: the consts were
            //   read out of OverworldEncounterSpawner.cs by grep and reflected off the type the FILE
            //   is named after, so both lookups returned null and the case failed on a healthy tree
            //   (Builds/wave1-reg, 2026-09-09 23:34). Verified at source 2026-09-09:
            //     Assets/_Modules/Village/Enemies/OverworldEncounterSpawner.cs:981  public sealed class RepEngageWatcher
            //     :999   private const float AggroRange   = 14f;
            //     :1047  private const float DeaggroRange = 26f;
            //   The chase, the leash break and the sting all live on the WATCHER; the spawner seats it.
            //   A file name is not a type name - CLAUDE.md S11B, "assert only what you read at source".
            bool gotDeaggro = TryFloatConst(typeof(RepEngageWatcher), "DeaggroRange", out float deaggro);
            bool gotAggro   = TryFloatConst(typeof(RepEngageWatcher), "AggroRange",   out float aggro);
            if (!gotDeaggro)
                failures.Add("[hysteresis] RepEngageWatcher.DeaggroRange could not be read as a float constant " +
                             "(declared at OverworldEncounterSpawner.cs:1047 when this case was written - the two " +
                             "types share that file). It is the distance test the chase never had; if it became a " +
                             "serialized field or moved to the tunables rail, re-point this case in the SAME " +
                             "change rather than deleting it.");
            if (!gotAggro)
                failures.Add("[hysteresis] RepEngageWatcher.AggroRange could not be read as a float constant " +
                             "(declared at OverworldEncounterSpawner.cs:999).");
            if (gotDeaggro && gotAggro)
            {
                if (deaggro <= aggro)
                    failures.Add($"[hysteresis] deaggro={deaggro}m is not above aggro={aggro}m. Without hysteresis a " +
                                 "rep hovering at the notice edge breaks off and re-stings on alternate frames: " +
                                 "sting audio spam, a de-aggro trace per frame, and the pursuit pulse stamped " +
                                 "anyway - the guard defeated and now loud.");
                else
                    log.AppendLine($"  [hysteresis] deaggro={deaggro}m > aggro={aggro}m (both read by reflection)");
            }

            string raw = ReadCode(SpawnerSrc);
            if (raw == null)
            {
                failures.Add("[fixture] " + SpawnerSrc + " is MISSING - the leash break cannot be verified. The " +
                             "file under test is not an optional dependency.");
            }
            else
            {
                // Comment lines are stripped before every source assertion below. The file carries a
                // long in-code RCA that QUOTES the forbidden calls (PostureSignals.ClearPursuits at
                // :1020 is prose describing BattleArena's own teardown), and a lint that cannot tell
                // a citation from a call is the trap RegressionMarkerRegression RULE 1 was rewritten
                // to escape - a mention is not an emission.
                string code = CodeOnly(raw);

                // ── C [break-body] the break clears the latch AND stands its own claim down ──
                string deaggroBody = Between(code, "private void Deaggro(string why)", "private bool ChaseBrokeOff(");
                if (deaggroBody == null)
                {
                    failures.Add("[break-body] Deaggro(string) and/or ChaseBrokeOff(...) are gone. They ARE the " +
                                 "ticket: before them the chase re-read nothing and a stung rep pursued forever.");
                }
                else
                {
                    if (!deaggroBody.Contains("_stung = false;"))
                        failures.Add("[break-body] Deaggro no longer clears _stung. The latch is the bug: with it " +
                                     "still set, QuietNonPursuersOnBattleEnd PRESERVES the rep (owner ruling) and " +
                                     "the chase resumes stamping on the next frame.");
                    if (!deaggroBody.Contains("PostureSignals.RevokePursuit(_enemy.GetInstanceID())"))
                        failures.Add("[break-body] Deaggro no longer revokes its OWN pursuit key. Clearing the latch " +
                                     "without revoking leaves the stale pulse in the ring for its whole TTL - which " +
                                     "is exactly the one-frame re-stamp after a full clear in F8 seq 4768.");
                    if (!deaggroBody.Contains("FlowTrace.Step("))
                        failures.Add("[break-body] the leash break is no longer instrumented. The WO's runtime " +
                                     "checklist is an ORDERED read of three trace lines; a silent break makes " +
                                     "items 2 and 3 unobservable (CLAUDE.md sec.12).");
                    if (deaggroBody.Contains("_stung = false;") &&
                        deaggroBody.Contains("PostureSignals.RevokePursuit(_enemy.GetInstanceID())"))
                        log.AppendLine("  [break-body] the break clears _stung and revokes only this body's own key");
                }

                // ── D [order] the break is evaluated BEFORE the sting can re-stamp ──
                int iBreak = code.IndexOf("if (_stung && ChaseBrokeOff(", StringComparison.Ordinal);
                int iSting = code.IndexOf("if (!_stung && heroAlive && d <= AggroRange)", StringComparison.Ordinal);
                if (iBreak < 0)
                    failures.Add("[order] the per-frame leash break is not called from the chase update at all. " +
                                 "ChaseBrokeOff existing but never being asked is the same as not having it.");
                else if (iSting < 0)
                    failures.Add("[order] the aggro test no longer reads 'if (!_stung && heroAlive && d <= " +
                                 "AggroRange)'. heroAlive must gate BOTH ends or a downed hero produces a " +
                                 "break/re-sting flicker every frame.");
                else if (iBreak > iSting)
                    failures.Add("[order] the leash break is evaluated AFTER the sting test in the same frame. The " +
                                 "order is load-bearing: breaking after re-stinging cannot end a chase.");
                else
                    log.AppendLine("  [order] the leash break runs before the sting test, and both gate on heroAlive");

                // ── E [dead-hero] a body has no combat inputs for a pursuit ring to serve ──
                if (!code.Contains("bool heroAlive = HeroHealth.Instance == null || HeroHealth.Instance.IsAlive;"))
                    failures.Add("[dead-hero] the chase no longer computes heroAlive with a null HeroHealth counting " +
                                 "as ALIVE. Narrowing it to 'a HeroHealth must exist' silently kills pursuit in " +
                                 "every scene that has none - the conservative reading BattleArena's own outcome " +
                                 "arbitration takes.");
                else if (!code.Contains("if (!heroAlive)"))
                    failures.Add("[dead-hero] ChaseBrokeOff no longer breaks the chase on a DOWN hero. A pulse " +
                                 "stamped over a corpse holds the battle-lock with no battle behind it.");
                else
                    log.AppendLine("  [dead-hero] a downed hero breaks the chase, and a null HeroHealth reads ALIVE");

                // ── F [own-key-only] no producer may force-clear the ring or write the lock ──
                if (code.Contains("PostureSignals.ClearPursuits"))
                    failures.Add("[own-key-only] the spawner force-clears the WHOLE pursuit ring. Forbidden by " +
                                 "WO-1337 and by this file's own doc-comment: one body may only ever revoke its own " +
                                 "key, or a single rep losing its leash drops the combat HUD while other bodies are " +
                                 "still hunting the hero.");
                else if (code.Contains("BattleLock.SetInBattle"))
                    failures.Add("[own-key-only] the spawner writes BattleLock directly. No producer may - the lock " +
                                 "is derived from the ring, and a producer that writes it hides the holder the " +
                                 "quiescence gate exists to name.");
                else
                    log.AppendLine("  [own-key-only] the spawner neither clears the ring nor writes the battle lock");

                // ── G [preserve-sweep] the battle-end sweep must stay distance-free ──
                string quiet = Between(code, "private bool QuietIfNotPursuing()", "private void Deaggro(string why)");
                if (quiet == null)
                    failures.Add("[preserve-sweep] QuietIfNotPursuing() is gone or no longer precedes Deaggro. It " +
                                 "carries the owner's PRESERVE rule for the battle-end sweep.");
                else if (!quiet.Contains("if (_stung || _engaged) return false;"))
                    failures.Add("[preserve-sweep] the battle-end sweep no longer preserves actively pursuing or " +
                                 "engaged reps. That is the owner's ruling and it stands; what made it safe is that " +
                                 "_stung is now HONEST, not that the rule changed.");
                else if (quiet.Contains("Vector3.Distance"))
                    failures.Add("[preserve-sweep] a distance test has been added to QuietIfNotPursuing. It runs " +
                                 "from BattleArena.Resolve while the hero is still at the far arena (~7 km), so it " +
                                 "would quiet EVERY rep on the map. The distance test belongs in the per-frame " +
                                 "chase, where the producer put it.");
                else
                    log.AppendLine("  [preserve-sweep] the battle-end sweep preserves pursuers and takes no distance");

                // ── H [stall-diagnostic] the instrument that tells the two variants apart ──
                if (!code.Contains("chase-stall-"))
                    failures.Add("[stall-diagnostic] the chase-stall diagnostic is gone. It is the ONLY thing that " +
                                 "distinguishes a far-away latch from a BLOCKED chase (navmesh island / unreachable " +
                                 "hero) on the next occurrence - the WO's own open question.");
                else if (!code.Contains("FlowTrace.Throttle(\"Encounter\", $\"chase-stall-{gameObject.name}\""))
                    failures.Add("[stall-diagnostic] the chase-stall line is no longer THROTTLED on a per-rep key. A " +
                                 "bare per-frame log here floods the device ring and evicts the very boot window the " +
                                 "capture needs (INSTRUMENTATION_STANDARD sec.8.2); a SHARED key throttles the whole " +
                                 "fleet to one voice and hides the rep that matters.");
                else if (!code.Contains("TouchDistance(hero)"))
                    failures.Add("[stall-diagnostic] the chase-stall line no longer reports TouchDistance. Without " +
                                 "the contact distance beside d and the leash, the reader cannot tell a rep that is " +
                                 "closing from one that can never engage.");
                else
                    log.AppendLine("  [stall-diagnostic] a per-rep throttled stall line reports d / best / touch / leash");
            }

            reason = failures.Count == 0
                ? MarkerOk + " the rep chase has a leash: the break clears the latch, revokes only its own pursuit " +
                  "key, gates both ends on a live hero, and the battle-end sweep stays distance-free\n" +
                  log.ToString().TrimEnd()
                : MarkerFail + " x" + failures.Count + " :: " + string.Join(" | ", failures);
            return failures.Count == 0;
        }

        /// <summary>Read a private/public float CONSTANT off a producer type, so a retune moves the
        /// assertion with the code instead of leaving a copied literal to rot (CLAUDE.md sec.8).</summary>
        private static bool TryFloatConst(Type type, string name, out float value)
        {
            value = 0f;
            if (type == null) return false;
            FieldInfo f = type.GetField(name, BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static);
            if (f == null || !f.IsLiteral || f.IsInitOnly) return false;
            object raw = f.GetRawConstantValue();
            if (!(raw is float)) return false;
            value = (float)raw;
            return true;
        }

        /// <summary>Read a tracked producer source file. Null when absent - every caller treats that
        /// as a FAILURE (fixture rule, INSTRUMENTATION_STANDARD sec.8.5), never as a skip.</summary>
        private static string ReadCode(string assetsRelativePath)
        {
            string full = Path.Combine(Application.dataPath, assetsRelativePath);
            return File.Exists(full) ? File.ReadAllText(full) : null;
        }

        /// <summary>Drop whole-line comments so a lint cannot fire on the file's own in-code RCA,
        /// which quotes the very calls case F forbids. Line-based on purpose: it is enough for this
        /// file's shape and it cannot mangle a string literal the way a naive block parser can.</summary>
        private static string CodeOnly(string src)
        {
            if (string.IsNullOrEmpty(src)) return src;
            var sb = new StringBuilder(src.Length);
            foreach (string line in src.Split('\n'))
            {
                string t = line.TrimStart();
                if (t.StartsWith("//", StringComparison.Ordinal) ||
                    t.StartsWith("*",  StringComparison.Ordinal) ||
                    t.StartsWith("/*", StringComparison.Ordinal)) continue;
                sb.Append(line).Append('\n');
            }
            return sb.ToString();
        }

        /// <summary>The slice between two anchors, so a case asserts a branch is where it must be.
        /// Null when either anchor is gone or out of order - reported, never silently passed.</summary>
        private static string Between(string src, string startToken, string endToken)
        {
            if (string.IsNullOrEmpty(src)) return null;
            int a = src.IndexOf(startToken, StringComparison.Ordinal);
            if (a < 0) return null;
            int b = src.IndexOf(endToken, a + startToken.Length, StringComparison.Ordinal);
            if (b < 0) return null;
            return src.Substring(a, b - a);
        }
    }
}

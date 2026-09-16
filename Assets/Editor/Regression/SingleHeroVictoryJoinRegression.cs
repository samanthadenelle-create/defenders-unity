// =============================================================================
// SingleHeroVictoryJoinRegression [singlehero-victory-join]
// -----------------------------------------------------------------------------
// Assembly: DeNelle.EditorRegression. Namespace: DeNelle.Editor.Regression.
// Markers: SINGLEHERO_VICTORY_JOIN_OK / SINGLEHERO_VICTORY_JOIN_FAIL.
// Registered ONCE in DataRegression.RunAll. NEVER throws.
//
// THE INCIDENT THIS GUARDS (owner felt-test, 2026-09-15). Verbatim:
//   "They still say things like Sylas joined the team or Grom joined the team.
//    It doesn't make any sense why they join the team if they don't offer any
//    benefit."
//
// MECHANISM. The SINGLE-HERO pivot (owner 2026-06-22, FeatureFlags.SingleHero,
// default ON) shipped on the CONSUMER side only. Four consumers honour it --
// BattleController BuildParty, StoryCompanionInjector, PartyHudBridge and
// HudModelProducers -- so a recruited companion never fights, never spawns a
// body and never takes a HUD slot. But the three raid VICTORY controllers still
// ran the recruit unconditionally on a new claim, really calling
// GameStateService.AddToParty and really persisting the roster. The player was
// told somebody joined, and then that somebody was hidden everywhere. The save
// and the screen disagreed, and the screen was the honest one.
//
// The recruit sites were the only three AddToParty callers with no flag check
// (git grep "AddToParty(" over Assets/_Modules, run 2026-09-15: nine hits -- these
// three, the service's own definition plus one internal call, and two tutorial
// paths that are a DIFFERENT beat and deliberately out of scope):
//   * RaidVictoryController.UnlockNextCompanion
//   * OutpostVictoryController.UnlockNextCompanion
//   * Village2RaidController.UnlockNextCompanion
//
// WHAT THIS ASSERTS (four rules; all deterministic, none needs a scene):
//   RULE 1  FeatureFlags.SingleHero still DEFAULTS ON with no PlayerPrefs
//           override. Every other rule here is conditional on that default, so
//           a silent flip would make this whole suite guard nothing.
//   RULE 2  The two EndState factories omit the join line for a null/empty name
//           (which is what the gated controllers now pass) and DO emit it for a
//           real name. The second half is the positive control: without it,
//           RULE 2 could pass on a factory that never emits the line at all.
//           The "The base is CLAIMED" half is asserted PRESENT -- the owner's
//           ruling drops the join, not the claim.
//   RULE 3  RaidDeployVM.BuildPartyClasses answers the HERO ALONE under
//           SingleHero, and the full roster with the flag off. That screen was
//           the last surface still painting the dead companion portraits
//           (RaidDeployScreen.BuildPartyRow reads exactly this list).
//   RULE 4  Each of the three controllers references FeatureFlags.SingleHero
//           within a few lines of its UnlockNextCompanion() CALL site. This is
//           the only available pin for Village2RaidController, whose banner text
//           is built inline in BuildVictoryBanner rather than through EndStateVM
//           -- there is no VM to interrogate, so the gate itself is the artifact.
//
// WHY RULE 4 STRIPS COMMENTS FIRST. This suite's own subject files carry RCA
// comments that name FeatureFlags.SingleHero on purpose, and so does this header.
// A raw IndexOf would pass on prose -- a hollow green of exactly the kind
// RegressionOutcome exists to end. The scan therefore blanks comment bodies
// (preserving newlines, so line numbers survive) and only then looks for the
// flag near a LIVE call.
//
// HOLLOW-PASS GUARD: a controller source that cannot be read reports as a
// PARTIAL-SKIP naming the file; if NONE of the three can be read, the suite
// stands down via RegressionOutcome.Skip and says it asserted nothing.
//
// PLAYERPREFS: RULE 3 has to move ff.singlehero to test both sides. The prior
// value is captured before and restored in a finally (the shape
// OverworldCombatGateRegression.cs:52-95 established) -- a throw must not leave
// a developer's editor, or every suite after this one, running with the flag off.
//
// POSITIVE CONTROL (prove it can go red): delete the `FeatureFlags.SingleHero`
// term from RaidVictoryController's recruit gate -- RULE 4 must name that file.
// Make BuildPartyClasses return the companions unconditionally -- RULE 3 must
// report a party of 4 where 1 was required.
// =============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using DeNelle.Core;
using DeNelle.Core.Diagnostics;
using DeNelle.Core.State;
using UnityEngine;

// EndStateVM and RaidDeployVM are named FULLY QUALIFIED below rather than imported:
// DeNelle.Village.UI and DeNelle.Core.UI both sit on this suite's using list's doorstep
// and the editor regression assembly references both, so a bare type name here is one
// future class away from an ambiguity that would read as an unrelated compile break.

namespace DeNelle.Editor.Regression
{
    public static class SingleHeroVictoryJoinRegression
    {
        private const string FlowSys    = "SingleHeroJoin";
        private const string MarkerOk   = "SINGLEHERO_VICTORY_JOIN_OK";
        private const string MarkerFail = "SINGLEHERO_VICTORY_JOIN_FAIL";
        private const string Tag        = "singlehero-victory-join";

        /// <summary>The sentence the owner called out. Held as a const so RULE 2's two
        /// halves (absent for null, present for a name) cannot drift apart.</summary>
        private const string JoinPhrase  = "joins your party";
        /// <summary>The half of the victory text the ruling KEEPS.</summary>
        private const string ClaimPhrase = "CLAIMED";

        private const string SingleHeroPref = "ff.singlehero";
        private const string FlagToken      = "FeatureFlags.SingleHero";
        private const string RecruitCall    = "UnlockNextCompanion()";

        /// <summary>How many lines either side of the recruit CALL the flag must appear
        /// within. Generous enough for a commented gate, tight enough that a reference
        /// somewhere else in a 900-line controller cannot satisfy it.</summary>
        private const int GateWindowLines = 8;

        private const string CampsDir = "Assets/_Modules/Village/World/Camps";

        /// <summary>The three victory controllers that recruit. Read at source 2026-09-15:
        /// the recruit call sits at RaidVictoryController.cs:282, OutpostVictoryController.cs:196
        /// and Village2RaidController.cs:253.</summary>
        private static readonly string[] ControllerFiles =
        {
            CampsDir + "/RaidVictoryController.cs",
            CampsDir + "/OutpostVictoryController.cs",
            CampsDir + "/Village2RaidController.cs"
        };

        public static void RunStandalone()
        {
            string reason;
            bool pass = Run(out reason);
            Debug.Log("[" + Tag + "] standalone result: " + (pass ? "PASS" : "FAIL") + " - " + reason);
        }

        public static bool Run(out string reason)
        {
            try { return RunCore(out reason); }
            catch (Exception ex)
            {
                reason = Tag + ": oracle threw " + ex.GetType().Name + ": " + ex.Message;
                Debug.LogError(MarkerFail + " - " + reason);
                return false;
            }
        }

        private static bool RunCore(out string reason)
        {
            using var _scope = FlowTrace.Enter(FlowSys, "SingleHeroVictoryJoin.RunCore");

            var failures = new List<string>();
            var partials = new List<string>();
            int assertions = 0;

            string projectRoot = Directory.GetParent(Application.dataPath).FullName;

            // ---- RULE 1: the flag this whole suite rests on still defaults ON -----
            int priorPref = PlayerPrefs.GetInt(SingleHeroPref, -1);
            try
            {
                PlayerPrefs.DeleteKey(SingleHeroPref);
                assertions++;
                if (!FeatureFlags.SingleHero)
                    failures.Add("RULE 1 FeatureFlags.SingleHero is OFF with no PlayerPrefs override. " +
                                 "The single-hero pivot (owner 2026-06-22) is the reason the victory " +
                                 "recruit was dropped (WO-1761); with it off by default the gate is " +
                                 "inert and every companion join comes back unannounced.");

                // ---- RULE 2: the end-state text -----------------------------------
                assertions += CheckEndStateText(failures, partials);

                // ---- RULE 3: the deploy party list --------------------------------
                assertions += CheckDeployParty(failures, partials);
            }
            finally
            {
                if (priorPref == -1) PlayerPrefs.DeleteKey(SingleHeroPref);
                else PlayerPrefs.SetInt(SingleHeroPref, priorPref);
                PlayerPrefs.Save();
            }

            // ---- RULE 4: each controller gates its recruit on the flag ------------
            int readable = 0;
            foreach (string rel in ControllerFiles)
            {
                string abs = Path.Combine(projectRoot, rel);
                string src = ReadOrEmpty(abs);
                if (string.IsNullOrEmpty(src))
                {
                    partials.Add(RegressionOutcome.PartialSkip("controller-source",
                        rel + " unreadable -- could not assert its recruit gate"));
                    continue;
                }
                readable++;
                assertions += CheckRecruitGate(rel, src, failures);
            }

            if (readable == 0 && failures.Count == 0)
            {
                FlowTrace.Warn(FlowSys, "no victory controller source could be read");
                return RegressionOutcome.Skip(out reason, Tag,
                    "none of the three victory controller sources under " + CampsDir +
                    " could be read -- the recruit gates were not asserted");
            }

            // ---- verdict ----------------------------------------------------------
            if (failures.Count > 0)
            {
                FlowTrace.Fail(FlowSys, "offenders=" + failures.Count + " assertions=" + assertions);
                reason = Tag + " FAIL (" + failures.Count + " finding(s); " + assertions +
                         " assertion(s), " + readable + "/" + ControllerFiles.Length +
                         " controller source(s) read): " + string.Join(" | ", failures.ToArray());
                Debug.LogError(MarkerFail + " - " + reason);
                return false;
            }

            string extra = partials.Count > 0 ? " " + string.Join(" ", partials.ToArray()) : "";
            FlowTrace.Step(FlowSys, "clean: assertions=" + assertions + " controllers=" + readable +
                                    " partials=" + partials.Count);
            reason = Tag + " OK - " + assertions + " assertion(s): SingleHero defaults ON, the raid and " +
                     "outpost end-state text carries no '" + JoinPhrase + "' line for a null name (and " +
                     "still says " + ClaimPhrase + "), the deploy party resolves to the hero alone, and " +
                     readable + " victory controller(s) gate the recruit on " + FlagToken + "." + extra;
            Debug.Log(MarkerOk + " - " + reason);
            return true;
        }

        // =====================================================================
        // RULE 2 -- the two EndState factories
        // ---------------------------------------------------------------------
        // The gated controllers pass null. EndStateVM.cs:423-424 and :685-686 build
        // the join line only for a non-empty name, so null must yield a one-line
        // body. The named case is the positive control: it proves the phrase is
        // still reachable, so the null case passing means something.
        // =====================================================================
        private static int CheckEndStateText(List<string> failures, List<string> partials)
        {
            const string ProbeName = "Sylas";
            int assertions = 0;

            string raidNull, raidNamed, outpostNull, outpostNamed;
            try
            {
                raidNull    = DeNelle.Village.UI.EndStateVM.FromRaidVictory(null, null).Subtitle ?? string.Empty;
                raidNamed   = DeNelle.Village.UI.EndStateVM.FromRaidVictory(ProbeName, null).Subtitle ?? string.Empty;
                outpostNull = DeNelle.Village.UI.EndStateVM.FromOutpostVictory(null, true).Subtitle ?? string.Empty;
                outpostNamed= DeNelle.Village.UI.EndStateVM.FromOutpostVictory(ProbeName, true).Subtitle ?? string.Empty;
            }
            catch (Exception ex)
            {
                partials.Add(RegressionOutcome.PartialSkip("endstate-text",
                    "EndStateVM factories threw in-editor (" + ex.GetType().Name + ": " + ex.Message +
                    ") -- the victory body text was not asserted"));
                return assertions;
            }

            assertions++;
            if (Contains(raidNull, JoinPhrase))
                failures.Add("RULE 2 EndStateVM.FromRaidVictory(null) still prints '" + JoinPhrase +
                             "'. The gated controllers pass null precisely so this line disappears " +
                             "(owner ruling 2026-09-15). Body was: " + Quote(raidNull));

            assertions++;
            if (!Contains(raidNull, ClaimPhrase))
                failures.Add("RULE 2 EndStateVM.FromRaidVictory(null) no longer says '" + ClaimPhrase +
                             "'. The ruling drops the JOIN, not the claim -- the player must still be " +
                             "told the base is theirs. Body was: " + Quote(raidNull));

            assertions++;
            if (Contains(outpostNull, JoinPhrase))
                failures.Add("RULE 2 EndStateVM.FromOutpostVictory(null, true) still prints '" +
                             JoinPhrase + "'. Body was: " + Quote(outpostNull));

            assertions++;
            if (!Contains(raidNamed, JoinPhrase))
                failures.Add("RULE 2 POSITIVE CONTROL: FromRaidVictory(\"" + ProbeName + "\") does NOT " +
                             "print '" + JoinPhrase + "'. Either the flag-OFF join path was deleted " +
                             "(it must survive -- ff.singlehero=0 restores the companion loop) or the " +
                             "wording moved, in which case the null-case assertion above is now vacuous " +
                             "and this const must be re-read from EndStateVM.cs. Body was: " + Quote(raidNamed));

            assertions++;
            if (!Contains(outpostNamed, JoinPhrase))
                failures.Add("RULE 2 POSITIVE CONTROL: FromOutpostVictory(\"" + ProbeName + "\", true) " +
                             "does NOT print '" + JoinPhrase + "' -- see above. Body was: " + Quote(outpostNamed));

            return assertions;
        }

        // =====================================================================
        // RULE 3 -- RaidDeployVM.BuildPartyClasses, both sides of the flag
        // ---------------------------------------------------------------------
        // Called directly rather than through CreateDefault: that path reads the
        // live GameStateService, which is not installed in a plain editor run, so
        // going through it would test the harness instead of the rule.
        // =====================================================================
        private static int CheckDeployParty(List<string> failures, List<string> partials)
        {
            int assertions = 0;

            // ⚠ GameState is a ScriptableObject, so it is CreateInstance'd, never `new`ed --
            // the note StarterArmyGrantRegression.cs:72-74 carries for the same reason.
            GameState state = null;
            try
            {
                state = ScriptableObject.CreateInstance<GameState>();
                state.HeroClass = HeroClassOpt.Knight;
                state.PartyMemberIds = new List<string> { "Ranger", "Cleric", "Mage" };
            }
            catch (Exception ex)
            {
                if (state != null) UnityEngine.Object.DestroyImmediate(state);
                partials.Add(RegressionOutcome.PartialSkip("deploy-party",
                    "a synthetic GameState could not be built (" + ex.GetType().Name + ": " + ex.Message +
                    ") -- the deploy party row was not asserted"));
                return assertions;
            }

            try { assertions += CheckDeployPartyCore(state, failures, partials); }
            finally { UnityEngine.Object.DestroyImmediate(state); }

            return assertions;
        }

        private static int CheckDeployPartyCore(GameState state, List<string> failures, List<string> partials)
        {
            int assertions = 0;

            // --- SingleHero ON: hero alone -------------------------------------
            PlayerPrefs.SetInt(SingleHeroPref, 1);
            List<string> single;
            try { single = DeNelle.Village.Hero.RaidDeployVM.BuildPartyClasses(state); }
            catch (Exception ex)
            {
                partials.Add(RegressionOutcome.PartialSkip("deploy-party",
                    "RaidDeployVM.BuildPartyClasses threw (" + ex.GetType().Name + ": " + ex.Message + ")"));
                return assertions;
            }

            assertions++;
            if (single == null || single.Count != 1)
                failures.Add("RULE 3 with " + SingleHeroPref + "=1 RaidDeployVM.BuildPartyClasses answered " +
                             (single == null ? "null" : single.Count + " entries [" + string.Join(", ", single.ToArray()) + "]") +
                             ", must be exactly 1 (the hero). RaidDeployScreen.BuildPartyRow paints one " +
                             "portrait plate per entry, so every extra entry is a companion the player " +
                             "can see and can never use (owner ruling 2026-09-15).");
            else
            {
                assertions++;
                if (!string.Equals(single[0], HeroClassOpt.Knight.ToString(), StringComparison.Ordinal))
                    failures.Add("RULE 3 the single deploy entry is '" + single[0] +
                                 "', expected the hero's own class '" + HeroClassOpt.Knight +
                                 "' -- the survivor must be the hero, not the first companion.");
            }

            // --- SingleHero OFF: the reversible party path is intact -------------
            PlayerPrefs.SetInt(SingleHeroPref, 0);
            List<string> party;
            try { party = DeNelle.Village.Hero.RaidDeployVM.BuildPartyClasses(state); }
            catch (Exception ex)
            {
                partials.Add(RegressionOutcome.PartialSkip("deploy-party-flagoff",
                    "BuildPartyClasses threw with the flag off (" + ex.GetType().Name + ": " + ex.Message + ")"));
                return assertions;
            }

            assertions++;
            if (party == null || party.Count != 4)
                failures.Add("RULE 3 POSITIVE CONTROL: with " + SingleHeroPref + "=0 BuildPartyClasses " +
                             "answered " + (party == null ? "null" : party.Count + " entries") +
                             ", expected 4 (hero + three companions). The single-hero pivot is " +
                             "flag-GATED, not a deletion -- if the companion path is gone, the ON case " +
                             "above proves nothing and the flag no longer reverses anything.");

            return assertions;
        }

        // =====================================================================
        // RULE 4 -- the recruit call is gated, in live code
        // ---------------------------------------------------------------------
        // Comments are blanked first (this file, and all three subjects, name the
        // flag in prose on purpose). Newlines survive the blanking so the reported
        // line number is the real one.
        // =====================================================================
        private static int CheckRecruitGate(string rel, string src, List<string> failures)
        {
            string stripped = StripComments(src);
            string[] lines = stripped.Replace("\r\n", "\n").Split('\n');

            int callLine = -1;
            for (int i = 0; i < lines.Length; i++)
            {
                if (lines[i].IndexOf(RecruitCall, StringComparison.Ordinal) < 0) continue;
                // Skip the declaration itself ("private string UnlockNextCompanion()").
                if (lines[i].IndexOf("string " + RecruitCall, StringComparison.Ordinal) >= 0) continue;
                callLine = i;
                break;
            }

            if (callLine < 0)
            {
                failures.Add("RULE 4 no live call to " + RecruitCall + " found in " + rel +
                             ". Either the recruit was DELETED (the flag-OFF companion loop must " +
                             "survive -- WO-1761 gates it, it does not remove it) or it was renamed, " +
                             "in which case this rule is no longer guarding anything.");
                return 1;
            }

            int from = Math.Max(0, callLine - GateWindowLines);
            int to   = Math.Min(lines.Length - 1, callLine + GateWindowLines);
            for (int i = from; i <= to; i++)
                if (lines[i].IndexOf(FlagToken, StringComparison.Ordinal) >= 0)
                    return 1;

            failures.Add("RULE 4 " + rel + " calls " + RecruitCall + " at stripped line " + (callLine + 1) +
                         " with no " + FlagToken + " within " + GateWindowLines + " lines. That controller " +
                         "would enrol a companion into the persisted roster whom BattleController, " +
                         "StoryCompanionInjector, PartyHudBridge and HudModelProducers all hide -- the " +
                         "exact 'Sylas joined the team' the owner called out on 2026-09-15.");
            return 1;
        }

        // ---- small helpers ---------------------------------------------------

        private static bool Contains(string haystack, string needle)
            => !string.IsNullOrEmpty(haystack) &&
               haystack.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;

        /// <summary>A body string folded to one line for the failure message.</summary>
        private static string Quote(string s)
            => "\"" + (s ?? string.Empty).Replace("\r", " ").Replace("\n", " / ") + "\"";

        private static string ReadOrEmpty(string path)
        {
            try { return File.Exists(path) ? File.ReadAllText(path) : string.Empty; }
            catch { return string.Empty; }
        }

        /// <summary>Blanks // and /* */ comment BODIES, preserving every newline so line
        /// numbers survive, and leaving string literals intact (a gate is a literal-free
        /// expression; an RCA note is prose). Same state machine as
        /// ArcaneSpireArtAuthorityRegression.StripComments, with the newline-preserving
        /// difference this rule's line arithmetic needs.</summary>
        private static string StripComments(string src)
        {
            if (string.IsNullOrEmpty(src)) return string.Empty;
            var sb = new System.Text.StringBuilder(src.Length);
            bool inLine = false, inBlock = false, inString = false, inChar = false;
            for (int i = 0; i < src.Length; i++)
            {
                char c = src[i];
                char next = i + 1 < src.Length ? src[i + 1] : '\0';

                if (inLine) { if (c == '\n') { inLine = false; sb.Append(c); } continue; }
                if (inBlock)
                {
                    if (c == '\n') sb.Append(c);
                    else if (c == '*' && next == '/') { inBlock = false; i++; }
                    continue;
                }
                if (inString)
                {
                    sb.Append(c);
                    if (c == '\\') { if (i + 1 < src.Length) { sb.Append(next); i++; } continue; }
                    if (c == '"') inString = false;
                    continue;
                }
                if (inChar)
                {
                    sb.Append(c);
                    if (c == '\\') { if (i + 1 < src.Length) { sb.Append(next); i++; } continue; }
                    if (c == '\'') inChar = false;
                    continue;
                }
                if (c == '/' && next == '/') { inLine = true; i++; continue; }
                if (c == '/' && next == '*') { inBlock = true; i++; continue; }
                if (c == '"')  { inString = true; sb.Append(c); continue; }
                if (c == '\'') { inChar  = true; sb.Append(c); continue; }
                sb.Append(c);
            }
            return sb.ToString();
        }
    }
}

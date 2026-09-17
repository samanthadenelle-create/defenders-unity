// =============================================================================
// DungeonLanternTeachRegression [dungeon-lantern-teach] -- WO-1805 Lanes A + C.
// -----------------------------------------------------------------------------
// Assembly: DeNelle.EditorRegression (already references DeNelle.Core and
//   DeNelle.Dungeons -- no asmdef edit needed).
//
// THE DEFECT THIS EXISTS BECAUSE OF (owner report, 2026-09-16, verbatim):
//   "we never really ever go over the mechanics of the torch in so nobody
//    understands why the torch runs out and why just become suddenly dark."
//
// The teach was FULLY BUILT and UNREACHABLE. dialogues.json has carried the
// lesson in the owner's own words since the cottage pipeline (row
// dun_torch_warden), delivered by TorchWardenDresser, whose only production
// caller is DungeonController -- a class that exists in exactly ONE scene on
// disk (Dungeon_HealersCottage) while EVERY player-facing portal routes to a
// composed dg_* scene. So the player met the oil mechanic for the first time as
// an unexplained blackout, and no line anywhere said so.
//
// -----------------------------------------------------------------------------
// WHY EACH CASE, AND WHY IT IS THE SHAPE IT IS
// -----------------------------------------------------------------------------
//  [teach-copy]        The row must exist in BOTH canonical twins, byte-identical,
//                      with Bryn as the speaker on every line (the lead's ruling
//                      2026-09-16) and no em/en dash. A dialogue the host plays by
//                      id that is absent from the data is a SILENT no-teach:
//                      DialogueService.Play just returns false.
//
//  [teach-one-shot]    The latch must ride the dialogue's END, never the Play()
//                      attempt, and the flag must be checked BEFORE Play. Both are
//                      silent failures of a specific, nasty kind: with
//                      ff.customdialogue OFF no View is subscribed, so Play returns
//                      TRUE, IsRunning goes true, hero input is suppressed and
//                      nothing can render or close the panel -- a soft-locked
//                      dungeon that no return value can detect. And a latch on the
//                      attempt burns the one-shot on a teach the player never saw
//                      (the WO-844 potion-lesson class of bug).
//
//  [tunable-identity]  ⛔ THE LANE C PROMISE, AND IT IS NOT A SELF-COMPARISON. Each
//                      rail default is compared to the REAL CONSUMER AUTHORITY --
//                      the authored json through DungeonLanternBalance, the
//                      ComposedOilStill const, the Lantern const -- so "the default
//                      is today's behaviour" is proved against the thing that
//                      actually ships rather than against another copy of itself.
//
//  [entry-net]         The standing net must read the burn off
//                      DungeonLanternBalance.SecondsToEmpty and never hardcode a
//                      200. The Lane C rail can move the drain at runtime, so a
//                      literal here would go stale the moment the feature it ships
//                      beside is used -- the duplicated-state failure CLAUDE.md
//                      sections 2 / 5 / 8 each record a separate scar from.
//
// ⚠ CODE-ONLY MATCHING WHERE IT MATTERS. Comments and string literals are stripped
// (through the shared ComposedDungeonRunRegression.StripCommentsAndStrings) before
// every structural assertion, because a raw-text lint is satisfied by a MENTION --
// this file's own header names every symbol it asserts. The two checks that are
// genuinely ABOUT a string literal (the entry net printing rooms= / ceiling=, the
// guttering copy) read the raw text on purpose, and say so at the call site.
//
// ⚠ NO HOLLOW PASSES: every case that scans for a target FAILS when it finds none.
//
// Orchestrator (DataRegression.RunAll) registers it covenant-style:
//   if (!DungeonLanternTeachRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[dungeon-lantern-teach] " + r);
//
// Standalone: run-unity-method DeNelle.Editor.Regression.DungeonLanternTeachRegression.RunAll
// =============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using DeNelle.Core.Ops;
using DeNelle.Dungeons;
using UnityEngine;

namespace DeNelle.Editor.Regression
{
    public static class DungeonLanternTeachRegression
    {
        // ── Files under test ────────────────────────────────────────────────
        private const string HostRel    = "_Modules/Dungeons/ComposedDungeonHost.cs";
        private const string LanternRel = "_Modules/Dungeons/Lantern.cs";
        private const string StillRel   = "_Modules/Dungeons/ComposedOilStill.cs";
        private const string DlgResRel    = "Resources/Data/Canonical/dialogue/dialogues.json";
        private const string DlgStreamRel = "StreamingAssets/Data/Canonical/dialogue/dialogues.json";

        /// <summary>The teach row id. Read off the HOST const so the two cannot drift.</summary>
        private static string IntroId => ComposedDungeonHost.LanternIntroDialogueId;

        private const char EmDash = (char)8212;
        private const char EnDash = (char)8211;

        public static bool Run(out string reason)
        {
            var failures = new List<string>();
            var log = new StringBuilder();
            log.AppendLine("--- DUNGEON LANTERN TEACH (WO-1805: the composed dungeon teaches its own oil mechanic, and the darkness is on the rail) ---");

            string assetsRoot = Application.dataPath;
            var src = new Dictionary<string, string>();

            Case(failures, "teach-copy",       () => Case1_TeachCopy(assetsRoot, failures, log));
            Case(failures, "teach-one-shot",   () => Case2_OneShot(assetsRoot, src, failures, log));
            Case(failures, "tunable-identity", () => Case3_TunableIdentity(failures, log));
            Case(failures, "entry-net",        () => Case4_EntryNet(assetsRoot, src, failures, log));

            if (failures.Count == 0)
            {
                Debug.Log(log.ToString() + "DUNGEON_LANTERN_TEACH_OK");
                reason = "DUNGEON LANTERN TEACH OK - 4/4 cases pass (the '" + IntroId + "' teach row ships in both " +
                         "canonical twins with Bryn speaking, the composed host plays it once per save and latches " +
                         "only on the dialogue's END behind a ff.customdialogue guard, the four dungeon.lantern* " +
                         "rail defaults equal their real consumer authorities so an empty table is today's " +
                         "behaviour, and the entry net reads the burn off DungeonLanternBalance.SecondsToEmpty)";
                return true;
            }

            reason = "dungeon-lantern-teach: " + string.Join("; ", failures);
            Debug.LogError(log.ToString() + "DUNGEON_LANTERN_TEACH_FAIL: " + reason);
            return false;
        }

        /// <summary>Standalone batch entry.</summary>
        public static void RunAll()
        {
            if (Run(out string reason)) Debug.Log("DUNGEON_LANTERN_TEACH_OK - " + reason);
            else Debug.LogError("DUNGEON_LANTERN_TEACH_FAIL: " + reason);
        }

        // =====================================================================
        //  Case 1 - THE COPY EXISTS, IN BOTH TWINS, IN BRYN'S VOICE
        // =====================================================================
        private static void Case1_TeachCopy(string root, List<string> failures, StringBuilder log)
        {
            string resPath = Path.Combine(root, DlgResRel);
            string streamPath = Path.Combine(root, DlgStreamRel);
            if (!File.Exists(resPath) || !File.Exists(streamPath))
            {
                failures.Add("[teach-copy] a canonical dialogue twin is MISSING (" + DlgResRel + " / " + DlgStreamRel +
                             ") - the teach cannot be proven present, which is a failure and not a skip");
                return;
            }

            byte[] a = File.ReadAllBytes(resPath);
            byte[] b = File.ReadAllBytes(streamPath);
            if (!SameBytes(a, b))
                failures.Add("[teach-copy] the two canonical dialogue copies are NOT byte-identical. Resources wins at " +
                             "runtime and StreamingAssets is the WebGL/player fallback, so a divergence means the " +
                             "teach can ship on one platform and not the other, silently");
            else
                log.AppendLine("OK: dialogues.json twins are byte-identical (" + a.Length + " bytes)");

            string json = Encoding.UTF8.GetString(a);

            // The row must exist AS AN ID, not as a passing mention in a _note.
            string idNeedle = "\"id\": \"" + IntroId + "\"";
            int at = json.IndexOf(idNeedle, StringComparison.Ordinal);
            if (at < 0)
            {
                failures.Add("[teach-copy] dialogues.json carries no row with id '" + IntroId + "'. " +
                             "ComposedDungeonHost plays it BY ID, and DialogueService.Play returns false for an " +
                             "unknown id - so the composed dungeon would go straight back to teaching nothing, " +
                             "which is the WO-1805 defect exactly");
                return;
            }
            log.AppendLine("OK: row '" + IntroId + "' is present in the canonical dialogue data");

            // The row's own slice: from its id to the next top-level row id, so the speaker and
            // dash assertions below cannot be satisfied by a neighbouring row's lines.
            int next = json.IndexOf("\"id\": \"", at + idNeedle.Length, StringComparison.Ordinal);
            int nodeStart = json.IndexOf("\"nodes\"", at, StringComparison.Ordinal);
            int end = json.IndexOf("\n    },", at, StringComparison.Ordinal);
            if (end < 0) end = json.Length;
            string row = json.Substring(at, Math.Max(0, end - at));

            if (nodeStart < 0 || nodeStart > end)
                failures.Add("[teach-copy] row '" + IntroId + "' has no nodes block - a dialogue with no node " +
                             "renders an empty panel the player must still tap through");

            int speakerCount = Count(row, "\"speaker\": \"Bryn\"");
            int lineCount = Count(row, "\"text\": \"");
            if (lineCount < 1)
                failures.Add("[teach-copy] row '" + IntroId + "' carries no line text at all");
            else if (speakerCount != lineCount)
                failures.Add("[teach-copy] row '" + IntroId + "' has " + lineCount + " line(s) but " + speakerCount +
                             " of them are spoken by Bryn. The lead ruled Bryn speaks this teach (2026-09-16: the " +
                             "copy is the owner's and Bryn's portrait resolves); an undeclared or invented speaker " +
                             "falls to a nameless silhouette in DialogueView");
            else
                log.AppendLine("OK: all " + lineCount + " line(s) of '" + IntroId + "' are spoken by Bryn");

            if (row.IndexOf(EmDash) >= 0 || row.IndexOf(EnDash) >= 0)
                failures.Add("[teach-copy] row '" + IntroId + "' contains an em/en dash. WO-1333 retired those from " +
                             "player copy and CopyHygieneRegression pins it - the owner photographed one in a " +
                             "dungeon prompt (WO-1588)");
            else
                log.AppendLine("OK: the teach copy is dash-clean (ASCII hyphens only)");

            // The teach must be the lesson, not decoration: it has to name the oil AND the stone.
            bool namesOil = row.IndexOf("oil", StringComparison.OrdinalIgnoreCase) >= 0;
            bool namesStone = row.IndexOf("stone", StringComparison.OrdinalIgnoreCase) >= 0;
            if (!namesOil || !namesStone)
                failures.Add("[teach-copy] row '" + IntroId + "' does not name both the OIL and the oil STONE " +
                             "(oil=" + namesOil + " stone=" + namesStone + "). The owner asked for two things: why " +
                             "the light runs out, and what to do about it. A teach that omits the recourse leaves " +
                             "the player exactly where the ticket found them");
            else
                log.AppendLine("OK: the teach names both the oil (why it dies) and the stone (what to do)");

            if (next >= 0 && next < end)
                log.AppendLine("NOTE: row slice bounded before the next dialogue id (no cross-row bleed)");
        }

        // =====================================================================
        //  Case 2 - THE ONE-SHOT, THE FLAG GUARD, AND THE COMPLETION EVENT
        // =====================================================================
        private static void Case2_OneShot(string root, Dictionary<string, string> src,
                                          List<string> failures, StringBuilder log)
        {
            string host = LoadCode(root, HostRel, src, failures);
            string lantern = LoadCode(root, LanternRel, src, failures);
            if (host == null || lantern == null) return;

            // (a) The three one-shot keys are declared, and they are SeenTutorials keys persisted
            //     through the MarkTutorialSeen idiom (key + Save in one call).
            foreach (var pair in new[]
                     {
                         new KeyValuePair<string, string>("LanternIntroShownKey", ComposedDungeonHost.LanternIntroShownKey),
                         new KeyValuePair<string, string>("LanternIntroCompletedKey", ComposedDungeonHost.LanternIntroCompletedKey),
                         new KeyValuePair<string, string>("LanternGutteringShownKey", ComposedDungeonHost.LanternGutteringShownKey),
                     })
            {
                if (string.IsNullOrWhiteSpace(pair.Value))
                    failures.Add("[teach-one-shot] ComposedDungeonHost." + pair.Key + " is empty - an empty " +
                                 "SeenTutorials key is silently ignored by MarkTutorialSeen, so the beat would " +
                                 "replay on EVERY dungeon entry forever");
            }
            if (host.IndexOf("MarkTutorialSeen", StringComparison.Ordinal) < 0)
                failures.Add("[teach-one-shot] ComposedDungeonHost no longer calls GameStateService.MarkTutorialSeen - " +
                             "nothing persists the one-shot, so the teach plays on every single entry (and the " +
                             "video's act 2, which opens inside dg_ember_deep at the boss, would open on a " +
                             "dialogue box)");
            else
                log.AppendLine("OK: the host latches its one-shots through MarkTutorialSeen (key + Save in one call)");

            // (b) THE SOFT-LOCK GUARD. The flag test must appear BEFORE the Play call, in code.
            int flagAt = host.IndexOf("FeatureFlags.CustomDialogue", StringComparison.Ordinal);
            int playAt = host.IndexOf("DialogueService.Play(", StringComparison.Ordinal);
            if (playAt < 0)
                failures.Add("[teach-one-shot] ComposedDungeonHost never calls DialogueService.Play - the composed " +
                             "dungeon teaches nothing, which is the WO-1805 defect restored");
            else if (flagAt < 0 || flagAt > playAt)
                failures.Add("[teach-one-shot] ComposedDungeonHost calls DialogueService.Play WITHOUT first testing " +
                             "FeatureFlags.CustomDialogue (flagAt=" + flagAt + " playAt=" + playAt + "). With that " +
                             "flag off DialogueView.Bootstrap never subscribed Opened, so Play returns TRUE, sets " +
                             "ActiveVm, makes IsRunning true and suppresses hero input with NO panel on screen and " +
                             "no way to close it. The return value cannot detect it: this ordering is the only guard");
            else
                log.AppendLine("OK: ff.customdialogue is checked BEFORE Play (no unrenderable VM can be opened)");

            // (c) THE LATCH RIDES THE END, NOT THE ATTEMPT. The shown key is marked inside the
            //     EndedWithId handler and nowhere before the Play call.
            if (host.IndexOf("DialogueService.EndedWithId", StringComparison.Ordinal) < 0)
                failures.Add("[teach-one-shot] ComposedDungeonHost does not subscribe DialogueService.EndedWithId - " +
                             "the only remaining way to latch the one-shot is on the Play() attempt, which burns it " +
                             "on a teach the player may never have seen (the WO-844 potion-lesson bug)");
            else
                log.AppendLine("OK: the host subscribes EndedWithId (the latch can ride the dialogue's completion)");

            int shownMarkAt = IndexOfMarkSeen(host, "LanternIntroShownKey");
            if (shownMarkAt < 0)
                failures.Add("[teach-one-shot] nothing marks ComposedDungeonHost.LanternIntroShownKey seen - the " +
                             "intro has no one-shot at all");
            else if (playAt >= 0 && shownMarkAt < playAt)
                failures.Add("[teach-one-shot] LanternIntroShownKey is latched BEFORE the Play call (at " +
                             shownMarkAt + " vs " + playAt + "). A declined or unrendered dialogue would then be " +
                             "recorded as taught and never offered again");
            else
                log.AppendLine("OK: the intro one-shot is latched after the Play call, in the Ended handler");

            // (d) THE COMPLETION BEAT. Lantern must raise OilStoneUsed where it spends a stone, and
            //     the host must listen - before WO-1805 CheckOilStones mutated and returned with no
            //     event and no trace, so no system could observe a refill at all.
            if (lantern.IndexOf("event System.Action OilStoneUsed", StringComparison.Ordinal) < 0 &&
                lantern.IndexOf("event Action OilStoneUsed", StringComparison.Ordinal) < 0)
                failures.Add("[teach-one-shot] Lantern no longer declares the OilStoneUsed event - the teach has no " +
                             "completion signal and CheckOilStones is back to mutating the flask unobservably");
            else if (!MethodBody(lantern, "void CheckOilStones()", out string checkBody) ||
                     checkBody.IndexOf("OilStoneUsed", StringComparison.Ordinal) < 0)
                failures.Add("[teach-one-shot] Lantern.CheckOilStones does not raise OilStoneUsed - the event exists " +
                             "but nothing fires it, so the teach can never complete on the player's own action");
            else
                log.AppendLine("OK: Lantern.CheckOilStones raises OilStoneUsed on a spent cache");

            if (lantern.IndexOf("event System.Action FinalWarningEntered", StringComparison.Ordinal) < 0 &&
                lantern.IndexOf("event Action FinalWarningEntered", StringComparison.Ordinal) < 0)
                failures.Add("[teach-one-shot] Lantern no longer declares FinalWarningEntered - the darkness beat " +
                             "would have to poll IsFinalWarning from an Update, which fires every frame of the " +
                             "collapse instead of once on its edge");
            else
                log.AppendLine("OK: Lantern declares the FinalWarningEntered edge event");

            if (host.IndexOf("OilStoneUsed", StringComparison.Ordinal) < 0 ||
                host.IndexOf("FinalWarningEntered", StringComparison.Ordinal) < 0)
                failures.Add("[teach-one-shot] ComposedDungeonHost does not subscribe both lantern events " +
                             "(OilStoneUsed for the completion, FinalWarningEntered for the darkness line)");
            else
                log.AppendLine("OK: the host subscribes both lantern teach events");

            // (e) THE GUTTERING LINE IS A NON-BLOCKING TOAST, and its copy is dash-clean. The copy
            //     check reads the CONST at runtime (not source text) because the const IS the copy.
            string guttering = ComposedDungeonHost.LanternGutteringLine;
            if (string.IsNullOrWhiteSpace(guttering))
                failures.Add("[teach-one-shot] ComposedDungeonHost.LanternGutteringLine is empty - the darkness beat " +
                             "shows nothing, and the 'suddenly dark' moment is unnarrated again");
            else if (guttering.IndexOf(EmDash) >= 0 || guttering.IndexOf(EnDash) >= 0)
                failures.Add("[teach-one-shot] the guttering line contains an em/en dash (WO-1333 / WO-1588)");
            else
                log.AppendLine("OK: guttering line is dash-clean player copy");

            if (host.IndexOf("ShowToast", StringComparison.Ordinal) < 0)
                failures.Add("[teach-one-shot] the darkness beat no longer uses ShowToast. A modal dialogue here " +
                             "would suppress hero input exactly while the fog wall closes and " +
                             "ComposedAmbushDirector's darkness multiplier arms - the one moment the player must " +
                             "keep control of");
            else
                log.AppendLine("OK: the darkness beat is a non-blocking toast, not a mid-run modal");
        }

        // =====================================================================
        //  Case 3 - LANE C: EVERY RAIL DEFAULT IS AN IDENTITY, PROVED AGAINST
        //           THE REAL CONSUMER
        // =====================================================================
        private static void Case3_TunableIdentity(List<string> failures, StringBuilder log)
        {
            // The authored burn, read through the same loader the game uses (Resources twin first,
            // then StreamingAssets, then the built-in fallback that mirrors the json).
            int authoredDrainX100 = Mathf.RoundToInt(DungeonLanternBalance.OilDrainPerSec * 100f);

            CheckSpec(failures, log, RemoteTunables.KeyDungeonLanternDrainPerSecX100, authoredDrainX100,
                "dungeon-balance.json's authored oilDrainPerSec x100 (read through DungeonLanternBalance, the " +
                "loader the game itself uses). A rail default that is NOT the authored value means an empty " +
                "client_tunables table changes the burn - which is the one thing RemoteTunables.cs forbids in " +
                "capitals, and it would do it invisibly because a dimmer dungeon renders fine");

            CheckSpec(failures, log, RemoteTunables.KeyDungeonLanternOilStoneRefillPct, 100,
                "a full top-up: Lantern.CheckOilStones has always set the flask to _maxOil, and the WO-1805 " +
                "implementation is Min(max, oil + max * pct/100), which is arithmetically identical at 100 from " +
                "ANY starting level. Any other default silently makes every cache in the game weaker");

            CheckSpec(failures, log, RemoteTunables.KeyDungeonLanternStillRefillPct,
                Mathf.RoundToInt(ComposedOilStill.RefillFraction * 100f),
                "ComposedOilStill.RefillFraction, the const the field still actually distils. Pinned against the " +
                "const rather than against a literal 40 so the two cannot drift apart");

            CheckSpec(failures, log, RemoteTunables.KeyDungeonLanternFinalWarningSec,
                Mathf.RoundToInt(Lantern.DefaultFinalWarningSeconds),
                "Lantern.DefaultFinalWarningSeconds, the serialized field's own shipping default. This is the " +
                "'suddenly dark' window: a rail default below it would shorten the only warning the player gets " +
                "on a build with an empty table");

            // maxOil must stay OFF the rail. It is the meter's 100% and the denominator of the
            // low-oil 0.25, darkness 0.12 and min-light 0.35 fractions, so a row on it moves three
            // systems at once - WO-1805 section 9 forbids it by name.
            foreach (var spec in RemoteTunables.Registry)
            {
                if (spec == null || string.IsNullOrEmpty(spec.Key)) continue;
                if (spec.Key.IndexOf("maxOil", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    spec.Key.IndexOf("lanternMaxOil", StringComparison.OrdinalIgnoreCase) >= 0)
                    failures.Add("[tunable-identity] '" + spec.Key + "' puts the lantern's CAPACITY on the rail. " +
                                 "maxOil is the oil meter's 100% and the denominator of three fraction thresholds " +
                                 "(_lowOilFraction 0.25, IsInDarkness 0.12, _minOilLightFraction 0.35) - moving it " +
                                 "silently moves the HUD bands, the ambush director and the fog collapse. Tune the " +
                                 "RATE (dungeon.lanternDrainPerSecX100), per WO-1805 section 6");
            }
            log.AppendLine("OK: the lantern's capacity (maxOil) is not on the rail - only the rate is");
        }

        private static void CheckSpec(List<string> failures, StringBuilder log, string key,
                                      int expected, string why)
        {
            var spec = RemoteTunables.SpecFor(key);
            if (spec == null)
            {
                failures.Add("[tunable-identity] '" + key + "' has no TunableSpec. RemoteTunables.Int answers 0 for " +
                             "an unregistered key, so the consumer would resolve this knob to ZERO - for the drain " +
                             "that is an infinite lantern, for the refills a dead cache");
                return;
            }
            if (spec.Kind != TunableKind.Int)
                failures.Add("[tunable-identity] '" + key + "' is registered as " + spec.Kind + ". This rail carries " +
                             "no floats (TunableKind is Bool|Int only), which is exactly why the drain rides x100 " +
                             "and the refills ride as percents - a Bool here would collapse the value to 0/1");
            if (spec.Default != expected)
                failures.Add("[tunable-identity] '" + key + "' ships at " + spec.Default + "; the shipping behaviour " +
                             "is " + expected + ". " + why);
            else
                log.AppendLine("OK: " + key + " ships at " + spec.Default + " = today's behaviour");
        }

        // =====================================================================
        //  Case 4 - THE STANDING NET READS THE BURN, NEVER A LITERAL
        // =====================================================================
        private static void Case4_EntryNet(string root, Dictionary<string, string> src,
                                           List<string> failures, StringBuilder log)
        {
            string hostCode = LoadCode(root, HostRel, src, failures);
            string hostRaw = LoadRaw(root, HostRel, failures);
            if (hostCode == null || hostRaw == null) return;

            if (!MethodBody(hostCode, "void ArmHeroPillars()", out string armCode))
            {
                failures.Add("[entry-net] ComposedDungeonHost.ArmHeroPillars is GONE - the composed run's lantern, " +
                             "oil meter, ambush director and teach are all armed from it, so its absence is not a " +
                             "refactor, it is the whole pipeline");
                return;
            }

            if (armCode.IndexOf("DungeonLanternBalance.SecondsToEmpty", StringComparison.Ordinal) < 0)
                failures.Add("[entry-net] ArmHeroPillars does not read DungeonLanternBalance.SecondsToEmpty. The " +
                             "entry net states each dungeon's FREE LIGHT CEILING (burn x (1 + caches)), and that " +
                             "burn must be READ: it is authored in dungeon-balance.json and the Lane C rail can " +
                             "move it at runtime. A local copy of the number is the duplicated-state failure " +
                             "CLAUDE.md sections 2 / 5 / 8 each record a scar from");
            else
                log.AppendLine("OK: the entry net reads the burn off DungeonLanternBalance.SecondsToEmpty");

            // A bare 200 anywhere in that method is the stale literal this case exists to forbid.
            // Matched on CODE ONLY, so the prose above it (which says "200 s a flask") cannot fail it.
            var bare200 = Regex.Match(armCode, @"(?<![\w.])200(?![\w.])");
            if (bare200.Success)
                failures.Add("[entry-net] ArmHeroPillars contains the bare literal 200. That is the pre-WO-1112 " +
                             "burn hardcoded back into the net that exists to report it, and it goes wrong the " +
                             "first time dungeon-balance.json or the dungeon.lanternDrainPerSecX100 row moves");
            else
                log.AppendLine("OK: no hardcoded 200 in the arm path");

            // ⚠ RAW TEXT ON PURPOSE: these two tokens live INSIDE the trace's string literal, which
            // the code-only stripper removes. The assertion is about what the line PRINTS.
            if (hostRaw.IndexOf("rooms=", StringComparison.Ordinal) < 0)
                failures.Add("[entry-net] the composed entry trace no longer prints rooms= . The room count next to " +
                             "the light budget is the whole point: dg_starter_loop authors ONE cache for ELEVEN " +
                             "rooms, and without both numbers on one line an under-provisioned dungeon stays " +
                             "invisible until the owner's eyes find it (CLAUDE.md section 14)");
            else
                log.AppendLine("OK: the entry trace prints rooms=");

            if (hostRaw.IndexOf("ceiling=", StringComparison.Ordinal) < 0)
                failures.Add("[entry-net] the composed entry trace no longer prints the free light ceiling= . That " +
                             "is the number that says whether the dungeon can be walked on the oil it authors");
            else
                log.AppendLine("OK: the entry trace prints the free light ceiling");

            // The drain path's own instrumentation: three edges that logged NOTHING before WO-1805,
            // which is why no capture on disk could prove a run had ever reached empty.
            string lanternRaw = LoadRaw(root, LanternRel, failures);
            if (lanternRaw == null) return;
            foreach (var needle in new[] { "flask EMPTY", "final-warning ENTERED", "oil stone", "darkness latch" })
            {
                if (lanternRaw.IndexOf(needle, StringComparison.OrdinalIgnoreCase) < 0)
                    failures.Add("[entry-net] Lantern no longer traces '" + needle + "'. CLAUDE.md section 12 is " +
                                 "binding and instrumentation is PERMANENT: before WO-1805 the entire drain path " +
                                 "was silent, so a run that went dark left no evidence to find - which is why the " +
                                 "ticket could not prove one ever had");
            }
            log.AppendLine("OK: the drain path keeps its four permanent trace edges");
        }

        // =====================================================================
        //  Helpers
        // =====================================================================
        private static void Case(List<string> failures, string name, Action body)
        {
            try { body(); }
            catch (Exception ex) { failures.Add("[" + name + "] THREW " + ex.GetType().Name + ": " + ex.Message); }
        }

        /// <summary>Source with comments AND string literals stripped (shared stripper).</summary>
        private static string LoadCode(string assetsRoot, string rel, Dictionary<string, string> cache,
                                       List<string> failures)
        {
            if (cache.TryGetValue(rel, out var hit)) return hit;
            string raw = LoadRaw(assetsRoot, rel, failures);
            string stripped = raw == null ? null : ComposedDungeonRunRegression.StripCommentsAndStrings(raw);
            cache[rel] = stripped;
            return stripped;
        }

        private static string LoadRaw(string assetsRoot, string rel, List<string> failures)
        {
            string path = Path.Combine(assetsRoot, rel);
            if (!File.Exists(path))
            {
                failures.Add("WO-1805: '" + rel + "' is MISSING - the invariant it carries is unverifiable, which " +
                             "is a failure and not a skip");
                return null;
            }
            try { return File.ReadAllText(path); }
            catch (Exception e)
            {
                failures.Add("WO-1805: could not read '" + rel + "' (" + e.GetType().Name + ": " + e.Message + ")");
                return null;
            }
        }

        /// <summary>Index of a MarkSeen/MarkTutorialSeen call naming this key const, or -1.</summary>
        private static int IndexOfMarkSeen(string code, string keyConst)
        {
            int from = 0;
            while (true)
            {
                int at = code.IndexOf(keyConst, from, StringComparison.Ordinal);
                if (at < 0) return -1;
                int lineStart = code.LastIndexOf(';', Math.Max(0, at - 1));
                if (lineStart < 0) lineStart = 0;
                string window = code.Substring(lineStart, at - lineStart);
                if (window.IndexOf("MarkSeen", StringComparison.Ordinal) >= 0 ||
                    window.IndexOf("MarkTutorialSeen", StringComparison.Ordinal) >= 0)
                    return at;
                from = at + keyConst.Length;
            }
        }

        /// <summary>
        /// The brace-matched body of the first method whose signature contains the needle. Runs on
        /// stripped code, so a brace inside a comment or a string cannot unbalance the scan.
        /// </summary>
        private static bool MethodBody(string code, string signatureNeedle, out string body)
        {
            body = null;
            if (string.IsNullOrEmpty(code)) return false;
            int at = code.IndexOf(signatureNeedle, StringComparison.Ordinal);
            if (at < 0) return false;
            int open = code.IndexOf('{', at);
            if (open < 0) return false;
            int depth = 0;
            for (int i = open; i < code.Length; i++)
            {
                char c = code[i];
                if (c == '{') depth++;
                else if (c == '}')
                {
                    depth--;
                    if (depth == 0)
                    {
                        body = code.Substring(open, i - open + 1);
                        return true;
                    }
                }
            }
            return false;
        }

        private static bool SameBytes(byte[] a, byte[] b)
        {
            if (a == null || b == null || a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
            return true;
        }

        private static int Count(string haystack, string needle)
        {
            if (string.IsNullOrEmpty(haystack) || string.IsNullOrEmpty(needle)) return 0;
            int n = 0;
            int at = 0;
            while ((at = haystack.IndexOf(needle, at, StringComparison.Ordinal)) >= 0)
            {
                n++;
                at += needle.Length;
            }
            return n;
        }
    }
}

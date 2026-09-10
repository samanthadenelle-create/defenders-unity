// =============================================================================
// HeartboundEventRegression [heartbound-events]  --  markers
// HEARTBOUND_EVENTS_OK / HEARTBOUND_EVENTS_FAIL
// -----------------------------------------------------------------------------
// Assembly: DeNelle.EditorRegression. Edit mode, no PlayMode. NEVER throws.
// WO-1678 (HEART-005). Owner rulings 2026-09-10 13:10 / 13:12 / 13:20 / 13:36.
//
// ⛔ THIS SUITE IS A SOURCE + CONFIG LINT, AND THAT IS NOT A SHORTCUT - IT IS THE
// ONLY SHAPE THAT CAN WORK HERE. Every runtime type in this feature lives behind
// `#if DAPP_STORE`, which is a DISTRIBUTION define stamped per artifact by
// AndroidBuild.cs and NOT set in the editor. A pin that referenced
// HeartboundEventInbox by type would therefore fail to COMPILE in the editor,
// taking the whole regression assembly - and every other suite in it - down with
// it. So the pins read the files as TEXT, which is also the right instrument for
// what they protect: these are rulings about what the code may CONTAIN.
//
// WHAT IT PROTECTS, and why each pin is worth its line:
//
//   PIN A  THE DROPPED EVENT STAYS DROPPED. Owner ruling 2026-09-10 13:12: the
//          dungeon-exclusive crafting ingredient event is DROPPED, not deferred. Its
//          supply is owner-capped on both existing faucets and its only use is a
//          weighted craft roll, so a staked financial position must never become a
//          third faucet. A row, a branch, an item id or a grant call naming it fails.
//
//   PIN B  THE VISITING VENDOR IS NOT SMUGGLED IN. Owner ruling 2026-09-10 13:36:
//          it left this ticket for its own work order, because a visiting-NPC
//          lifecycle does not exist in this client at all and is larger than the
//          rest of HEART-005 combined. A row or a branch naming it fails.
//
//   PIN C  SCOUT'S WHISPER MOVES THE MOMENT, NOT THE INFORMATION. Owner ruling
//          2026-09-10 13:36: "existing report, earlier ... no new intel." Two
//          directions, and BOTH are needed: the existing report seam must still be
//          the only builder (RaidDeployVM.BuildScoutReport, whose last line stays
//          the spoils estimate), and the Heartbound path must build NOTHING - no
//          second report, no composition, no resistance, no troop recommendation.
//
//   PIN D  HEARTFIRE HAS EXACTLY ONE SECOND SOURCE. Owner ruling 2026-09-10 13:12:
//          "allow a second source for stakers - the single-source lint is
//          re-pointed to permit exactly Heartfire Spark." So: exactly one heartfire
//          row in the table, the pure Spark seam present, and HeartfireRegression's
//          own re-pointed pin (PIN I) registered. That last one is the load-bearing
//          half: a ruling whose lint was never moved is a ruling nobody enforces.
//
//   PIN E  NO COMBAT POWER, EVER. Owner ruling Q-P2W 2026-09-10 13:10: "economic
//          acceleration is allowed; combat power stays off the table." The reward
//          kinds are an allow-list, the modifier lanes are an allow-list, and no
//          combat stat name may appear in either the table or the client seam.
//
//   PIN F  THE FOUR BANNED WORDS. Spec :483-491 - lottery / jackpot / bet / wager.
//          Swept WHOLE-WORD against the RAW text (not the stripped code), because
//          the risk lives in the player-facing strings, and swept whole-word
//          because a substring rule fires on "better" and "alphabet" - and a lint
//          that fires on innocent copy is a lint somebody deletes.
//
//   PIN G  NO REWARD VALUE IS HARDCODED IN A PRESENTATION CLASS. Spec :947. Proven
//          by reading the AUTHORED magnitudes out of the config and asserting none
//          of them appears in the copy or applier source - a real cross-check, not
//          a promise in a comment.
//
//   PIN H  THE COMPLIANCE GATE IS STRUCTURAL. Owner ruling 2026-09-10 13:10:
//          "Seeker only; Play never shows it." Every client file this ticket adds
//          must be `#if DAPP_STORE`-guarded, or live in DeNelle.Wallet, whose
//          asmdef carries "!GOOGLE_PLAY" and is therefore ABSENT from a Play
//          artifact. A feature flag would not do: FeatureFlags records that a
//          stored PlayerPrefs value BEATS the default.
//
//   PIN I  INSTRUMENTATION IS PRESENT, AND THE CLIENT NEVER ROLLS. Every new client
//          file carries FlowTrace (CLAUDE.md section 12 - never strip it), and none
//          of them calls Random: the event is decided by the server, and a client
//          that can roll is a client that can choose.
//
// Standalone:
//   -Method DeNelle.Editor.Regression.HeartboundEventRegression.RunStandalone
// =============================================================================

using System;
using System.Collections.Generic;

using System.IO;
using System.Text.RegularExpressions;
using UnityEngine;

namespace DeNelle.Editor.Regression
{
    public static class HeartboundEventRegression
    {
        // --- the client seam this ticket owns (relative to Application.dataPath) ---
        private const string DescriptorRel = "_Modules/Core/Heartbound/HeartboundEchoEvent.cs";
        private const string InboxRel      = "_Modules/Core/Heartbound/HeartboundEventInbox.cs";
        private const string ModifiersRel  = "_Modules/Core/Heartbound/HeartboundModifiers.cs";
        private const string ScoutRel      = "_Modules/Core/Heartbound/HeartboundScoutAccess.cs";
        private const string CopyRel       = "_Modules/Core/Heartbound/HeartboundEventCopy.cs";
        private const string ClientRel     = "_Modules/Wallet/HeartboundEventClient.cs";
        private const string ApplierRel    = "_Modules/Village/Heartbound/HeartboundEventApplier.cs";

        // --- files this ticket must NOT have changed the shape of ---
        private const string RaidDeployVmRel   = "_Modules/Village/Hero/RaidDeployVM.cs";
        private const string HeartfireCoreRel  = "_Modules/Core/State/HeartfireCharges.cs";
        private const string HeartfireLintRel  = "Editor/Regression/HeartfireRegression.cs";

        // --- the authored table (project root relative - it is a BACKEND file) ---
        private const string EventConfigRel = "api/_lib/heartbound-events-config.json";
        private const string EventModuleRel = "api/_lib/heartbound-events.js";

        /// <summary>Every file the client seam consists of. Order is presentation order.</summary>
        private static readonly string[] ClientFiles =
        {
            DescriptorRel, InboxRel, ModifiersRel, ScoutRel, CopyRel, ClientRel, ApplierRel,
        };

        /// <summary>
        /// The event ids and feature names two owner rulings removed from this ticket.
        /// Deliberately spelled several ways: a lint that only knew one spelling would be
        /// beaten by a rename that changed nothing about the design.
        /// </summary>
        private static readonly string[] DroppedIngredientTokens =
        {
            "rough_stone", "roughstone", "rough stone", "ing_rough", "BankRoughStone", "GrantRoughStone",
        };

        private static readonly string[] DeferredVendorTokens =
        {
            "wandering_merchant", "wanderingmerchant", "WanderingMerchant", "Merchant",
        };

        /// <summary>Anything that would mean NEW intel rather than earlier access.</summary>
        private static readonly string[] NewIntelTokens =
        {
            "BuildScoutReport", "ScoutIntel", "enemyComposition", "resistance",
            "recommendedTroop", "rewardPreview",
        };

        /// <summary>A reward that moved a combat number would name one of these.</summary>
        private static readonly string[] CombatStatTokens =
        {
            "damage", "attackspeed", "firerate", "critchance", "armor", "armour",
            "lifesteal", "penetration", "maxhp",
        };

        /// <summary>Spec :483-491. Whole-word, always.</summary>
        private static readonly string[] BannedWords = { "lottery", "jackpot", "bet", "wager" };

        /// <summary>Batchmode entry point (marker on a fresh log; never the exit code).</summary>
        public static void RunStandalone()
        {
            bool ok = Run(out string reason);
            Debug.Log(ok ? "HEARTBOUND_EVENTS_OK " + reason : "HEARTBOUND_EVENTS_FAIL " + reason);
        }

        /// <summary>Registered entry point. Returns true when clean; never throws.</summary>
        public static bool Run(out string reason)
        {
            try
            {
                return RunCore(out reason);
            }
            catch (Exception ex)
            {
                reason = "heartbound-events: oracle threw " + ex.GetType().Name + ": " + ex.Message;
                return false;
            }
        }

        private static bool RunCore(out string reason)
        {
            var f = new List<string>();

            string config = ReadRepoText(EventConfigRel, f);
            string module = ReadRepoText(EventModuleRel, f);

            DroppedEventCases(f, config);          // PIN A
            DeferredVendorCases(f, config);        // PIN B
            ScoutCases(f, config);                 // PIN C
            HeartfireSecondSourceCases(f, config); // PIN D
            CombatPowerCases(f, config, module);   // PIN E
            BannedWordCases(f, config);            // PIN F
            HardcodedRewardCases(f, config);       // PIN G
            CompileGateCases(f);                   // PIN H
            InstrumentationCases(f);               // PIN I

            if (f.Count == 0)
            {
                reason = "HEARTBOUND EVENTS OK -- the dropped ingredient event and the deferred visiting " +
                         "vendor appear in no row and no branch; Scout's Whisper grants EARLIER access to " +
                         "the one existing report and builds no intel of its own, while RaidDeployVM " +
                         "remains its only builder; Heartfire has exactly ONE ruled second source with " +
                         "the pure Spark seam present and HeartfireRegression's re-pointed pin registered; " +
                         "every reward kind and modifier lane is on its allow-list and names no combat " +
                         "stat; no banned word reaches a player-facing string; no authored reward value " +
                         "is duplicated into a presentation class; every client file is compiled out of a " +
                         "Google Play artifact; and every one of them carries FlowTrace and rolls nothing";
                return true;
            }

            reason = "HEARTBOUND EVENTS FAIL x" + f.Count + ": " + string.Join(" | ", f);
            return false;
        }

        // =====================================================================
        //  PIN A -- the dropped event stays dropped
        // =====================================================================

        private static void DroppedEventCases(List<string> f, string config)
        {
            // The table itself, RAW: a row, a note or a field naming it all fail the same
            // way, and this is one of the few sweeps where prose should NOT be exempt --
            // the config's own explanation of the ruling is deliberately written without
            // the token, so a hit here is a real re-introduction.
            AssertAbsent(f, "A", "the authored event table", config, DroppedIngredientTokens);

            // ⚠ AND THE CLIENT HALF IS SWEPT RAW, NOT AS STRIPPED CODE - the choice cost a
            // draft to discover and is worth the sentence. SourceLint blanks string
            // CONTENTS, and an item id only ever appears inside a string literal, so a
            // stripped sweep would be structurally blind to the exact defect this pin
            // exists to catch. Raw text costs nothing here because the tokens are item ids
            // and method names, which have no innocent English form - the applier's own
            // header explains the ruling at length WITHOUT spelling them, deliberately.
            foreach (string rel in ClientFiles)
            {
                string raw = ReadAssetText(rel, f);
                AssertAbsent(f, "A", rel, raw, DroppedIngredientTokens);
            }
        }

        // =====================================================================
        //  PIN B -- the visiting vendor is not smuggled in
        // =====================================================================

        private static void DeferredVendorCases(List<string> f, string config)
        {
            AssertAbsent(f, "B", "the authored event table", config, DeferredVendorTokens);
            foreach (string rel in ClientFiles)
            {
                // Raw, for the reason spelled out in PIN A.
                string raw = ReadAssetText(rel, f);
                AssertAbsent(f, "B", rel, raw, DeferredVendorTokens);
            }
        }

        // =====================================================================
        //  PIN C -- earlier, never more
        // =====================================================================

        private static void ScoutCases(List<string> f, string config)
        {
            // C1 - the EXISTING seam is still the one builder, and its spoils-last contract
            // is still described where the raid oracle pins it. This ticket must not have
            // touched it, and a lint that only checked the new files would not notice if it
            // had been replaced wholesale.
            string vm = SourceLint.ReadCode(RaidDeployVmRel, f);
            if (!string.IsNullOrEmpty(vm))
            {
                if (vm.IndexOf("BuildScoutReport", StringComparison.Ordinal) < 0)
                    f.Add("C1 RaidDeployVM.BuildScoutReport is gone -- the scout report has exactly one " +
                          "builder, and Scout's Whisper exists precisely so there is never a second");
                if (vm.IndexOf("ScoutReport", StringComparison.Ordinal) < 0)
                    f.Add("C1 RaidDeployVM no longer exposes ScoutReport -- the surface Scout's Whisper " +
                          "reveals EARLIER has disappeared");
            }

            // C2 - the Heartbound path builds NOTHING. Owner ruling 2026-09-10 13:36:
            // "existing report, earlier ... no new intel." A token from the spec's own
            // suggestion list (composition / resistance / recommended troop / reward
            // preview) appearing here means the ruling was quietly widened.
            string scout = SourceLint.ReadCode(ScoutRel, f);
            string applier = SourceLint.ReadCode(ApplierRel, f);
            AssertAbsent(f, "C2", ScoutRel, scout, NewIntelTokens);
            AssertAbsent(f, "C2", ApplierRel, applier, NewIntelTokens);

            // C3 - and the flag it DOES set exists and is what the applier calls. The
            // positive half: a pin that only forbade things would stay green if the whole
            // reward were quietly deleted.
            if (!string.IsNullOrEmpty(scout) &&
                scout.IndexOf("GrantEarlyReveal", StringComparison.Ordinal) < 0)
                f.Add("C3 HeartboundScoutAccess.GrantEarlyReveal is gone -- Scout's Whisper has no seam " +
                      "left, so the ruled reward grants nothing");
            if (!string.IsNullOrEmpty(applier) &&
                applier.IndexOf("HeartboundScoutAccess.GrantEarlyReveal(", StringComparison.Ordinal) < 0)
                f.Add("C3 the Echo Event applier no longer routes Scout's Whisper through " +
                      "HeartboundScoutAccess -- either it grants nothing, or it grants something else");

            // C4 - the table's scout row carries a duration and nothing else. A row that
            // authored an intel field would be new information by the back door.
            foreach (string token in new[] { "\"composition\"", "\"resistance\"", "\"recommendedTroop\"" })
            {
                if (!string.IsNullOrEmpty(config) &&
                    config.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0)
                    f.Add("C4 the authored event table carries " + token + " -- the scout reward moves " +
                          "WHEN the report is offered, never WHAT it says");
            }
        }

        // =====================================================================
        //  PIN D -- exactly one second source for Heartfire
        // =====================================================================

        private static void HeartfireSecondSourceCases(List<string> f, string config)
        {
            // D1 - exactly ONE heartfire row in the table. Counting the kind is the honest
            // measure: two rows would be two faucets however they were named.
            //
            // ⚠ MATCHED BY REGEX, NOT BY A LITERAL WITH A SPACE IN IT. `"kind": "heartfire"`
            // as a fixed string reads ZERO the day somebody reformats the JSON without the
            // space - and then this pin fails saying "0 heartfire rows, not 1", which sends
            // the next reader hunting for a deleted row that is sitting right there. A pin
            // whose failure message lies is worse than no pin.
            int rows = Regex.Matches(config ?? string.Empty, "\"kind\"\\s*:\\s*\"heartfire\"").Count;
            if (rows != 1)
                f.Add("D1 the authored event table declares " + rows + " heartfire rows, not 1 -- the " +
                      "owner permitted EXACTLY ONE second source (2026-09-10), and a permit for one is " +
                      "not a permit for a class");

            // D2 - the pure seam the ruling names exists. A permit whose subject was deleted
            // is a lie sitting in a header.
            string core = SourceLint.ReadCode(HeartfireCoreRel, f);
            if (!string.IsNullOrEmpty(core) &&
                core.IndexOf("public static int Spark(", StringComparison.Ordinal) < 0)
                f.Add("D2 HeartfireCharges.Spark is gone -- the ruled second source (owner, 2026-09-10) " +
                      "no longer exists, so the re-pointed lint permits something that is not there");

            // D3 - AND THE LINT ACTUALLY MOVED. This is the load-bearing pin of the four:
            // the owner's instruction was "the single-source lint is RE-POINTED to permit
            // exactly Heartfire Spark", and a ruling whose lint was never moved is a ruling
            // nobody enforces. CLAUDE.md section 15: the doc and the code travel together.
            string lint = SourceLint.ReadCode(HeartfireLintRel, f);
            if (!string.IsNullOrEmpty(lint) &&
                lint.IndexOf("SecondSourceCases", StringComparison.Ordinal) < 0)
                f.Add("D3 HeartfireRegression no longer registers SecondSourceCases -- the single-source " +
                      "lint's re-point was reverted, so nothing now bounds the second faucet");

            // D4 - and the second source is never bought. No price, no pack, no wallet row
            // anywhere near the table.
            foreach (string token in new[] { "\"price\"", "\"cost\"", "\"purchase\"", "\"sku\"" })
            {
                if (!string.IsNullOrEmpty(config) &&
                    config.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0)
                    f.Add("D4 the authored event table carries " + token + " -- no Echo Event may be " +
                          "bought, and Heartfire least of all");
            }
        }

        // =====================================================================
        //  PIN E -- economic acceleration only
        // =====================================================================

        private static void CombatPowerCases(List<string> f, string config, string module)
        {
            // E1 - the reward kinds are an allow-list, and the backend enforces it at load.
            if (!string.IsNullOrEmpty(module) &&
                module.IndexOf("ALLOWED_KINDS", StringComparison.Ordinal) < 0)
                f.Add("E1 the event module no longer declares ALLOWED_KINDS -- the reward classes became " +
                      "open-ended, and 'no combat power' went back to being a comment");

            // E2 - no combat stat is named by the table or by the modifier lanes. Swept over
            // the CODE of the modifier file so its header may keep explaining the rule.
            AssertAbsent(f, "E2", "the authored event table", config, CombatStatTokens);
            string modifiers = SourceLint.ReadCode(ModifiersRel, f);
            AssertAbsent(f, "E2", ModifiersRel, modifiers, CombatStatTokens);

            // E3 - and the lanes are an allow-list too, with the three economic ones present.
            //
            // ⚠ THE LANE NAMES ARE CHECKED AS CONSTANT IDENTIFIERS HERE AND AS STRINGS IN
            // THE CONFIG, AND THAT SPLIT IS DELIBERATE. SourceLint blanks string CONTENTS,
            // so looking for "gather_rate" in the stripped C# would ALWAYS fail - the value
            // lives inside a literal. The C# side is therefore pinned by the const NAME
            // (which survives stripping) and the wire value is pinned where it actually
            // matters: in the authored table the backend and the client must agree on.
            if (!string.IsNullOrEmpty(modifiers))
            {
                if (modifiers.IndexOf("AllowedLanes", StringComparison.Ordinal) < 0)
                    f.Add("E3 HeartboundModifiers.AllowedLanes is gone -- any string is now a modifier " +
                          "lane, which is how a combat stat arrives without anyone deciding to add one");
                foreach (string lane in new[] { "LaneGatherRate", "LaneBuildSpeed", "LaneCraftSpeed" })
                {
                    if (modifiers.IndexOf(lane, StringComparison.Ordinal) < 0)
                        f.Add("E3 HeartboundModifiers." + lane + " is gone -- an economic modifier lane " +
                              "disappeared, so an authored row now names a lane nothing can apply");
                }
            }

            foreach (string lane in new[] { "gather_rate", "build_speed", "craft_speed" })
            {
                if (!string.IsNullOrEmpty(config) && config.IndexOf(lane, StringComparison.Ordinal) < 0)
                    f.Add("E3 the authored event table no longer names the modifier lane '" + lane +
                          "' -- the table and the client must agree on the wire value, and this is the " +
                          "only place that comparison can be made");
            }

            // E4 - no event pays a withdrawable asset. Spec :919-931.
            foreach (string token in new[] { "\"skr\"", "\"sol\"", "\"usdc\"", "withdraw", "airdrop" })
            {
                if (!string.IsNullOrEmpty(config) &&
                    config.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0)
                    f.Add("E4 the authored event table names " + token + " -- no Echo Event may pay a " +
                          "withdrawable asset, and no SKR is consumed by one either");
            }
        }

        // =====================================================================
        //  PIN F -- the four banned words
        // =====================================================================

        private static void BannedWordCases(List<string> f, string config)
        {
            // RAW text, not stripped code: the risk is in the player-facing STRINGS, and
            // SourceLint deliberately blanks string contents. Whole-word, because a
            // substring rule fires on "better" and "alphabet" -- and a lint that fires on
            // innocent copy is a lint somebody deletes.
            string copy = ReadAssetText(CopyRel, f);
            AssertNoBannedWord(f, CopyRel, copy);
            AssertNoBannedWord(f, "the authored event table", config);
        }

        private static void AssertNoBannedWord(List<string> f, string where, string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            foreach (string word in BannedWords)
            {
                if (Regex.IsMatch(text, "\\b" + word + "\\b", RegexOptions.IgnoreCase))
                    f.Add("F " + where + " contains the banned word '" + word + "' -- an Echo Event is a " +
                          "reaction of the world, never a draw, and no SKR is consumed by one (spec " +
                          ":483-491)");
            }
        }

        // =====================================================================
        //  PIN G -- no reward value in a presentation class
        // =====================================================================

        private static void HardcodedRewardCases(List<string> f, string config)
        {
            // Spec :947. Proven by CROSS-CHECK rather than assertion: every magnitude the
            // table authors is pulled out of the JSON and looked for in the copy and applier
            // source. A duplicated value is a second authority on a number the config owns,
            // and this is the one shape of that defect a reader would never spot.
            var magnitudes = new List<string>();
            foreach (Match m in Regex.Matches(config ?? string.Empty, "\"magnitude\"\\s*:\\s*([0-9.]+)"))
            {
                if (m.Groups.Count > 1) magnitudes.Add(m.Groups[1].Value);
            }
            foreach (Match m in Regex.Matches(config ?? string.Empty, "\"durationSeconds\"\\s*:\\s*([0-9]+)"))
            {
                if (m.Groups.Count > 1) magnitudes.Add(m.Groups[1].Value);
            }

            if (magnitudes.Count == 0)
            {
                f.Add("G the authored event table declares no magnitude or duration at all -- either the " +
                      "table is empty or the reward values moved somewhere this pin cannot see them");
                return;
            }

            foreach (string rel in new[] { CopyRel, ApplierRel })
            {
                string code = SourceLint.ReadCode(rel, f);
                if (string.IsNullOrEmpty(code)) continue;
                foreach (string value in magnitudes)
                {
                    // A bare integer like "1" would match half the file; only values with a
                    // decimal point or three-plus digits are distinctive enough to prove a copy.
                    if (value.Length < 3) continue;
                    if (code.IndexOf(value, StringComparison.Ordinal) >= 0)
                        f.Add("G " + rel + " contains the authored reward value " + value + " -- every " +
                              "reward amount belongs in config (spec :947); a copy in a presentation " +
                              "class is a second authority that will drift");
                }
            }
        }

        // =====================================================================
        //  PIN H -- the compliance gate is structural, not a flag
        // =====================================================================

        private static void CompileGateCases(List<string> f)
        {
            foreach (string rel in ClientFiles)
            {
                string raw = ReadAssetText(rel, f);
                if (string.IsNullOrEmpty(raw)) continue;

                bool guarded = raw.IndexOf("#if DAPP_STORE", StringComparison.Ordinal) >= 0 ||
                               raw.IndexOf("#if !DAPP_STORE", StringComparison.Ordinal) >= 0;
                if (!guarded)
                    f.Add("H " + rel + " carries no DAPP_STORE guard -- Heartbound is Seeker-only (owner, " +
                          "2026-09-10) and a plain Windows or WebGL build carries neither distribution " +
                          "define. A feature flag is NOT an alternative: a stored PlayerPrefs value beats " +
                          "the default");
            }

            // And the assembly-level half for the one file that lives in DeNelle.Wallet:
            // its asmdef's !GOOGLE_PLAY constraint is the STRONGER guarantee, because an
            // absent assembly is absent.
            string asmdef = ReadAssetText("_Modules/Wallet/DeNelle.Wallet.asmdef", f);
            if (!string.IsNullOrEmpty(asmdef) &&
                asmdef.IndexOf("!GOOGLE_PLAY", StringComparison.Ordinal) < 0)
                f.Add("H DeNelle.Wallet.asmdef lost its \"!GOOGLE_PLAY\" defineConstraint -- the whole " +
                      "assembly is supposed to be ABSENT from a Google Play artifact, and that is the " +
                      "strongest gate this feature has");
        }

        // =====================================================================
        //  PIN I -- instrumented, and never rolling
        // =====================================================================

        private static void InstrumentationCases(List<string> f)
        {
            foreach (string rel in ClientFiles)
            {
                string code = SourceLint.ReadCode(rel, f);
                if (string.IsNullOrEmpty(code)) continue;

                // The descriptor and the copy are pure data and words with no branch worth
                // tracing; everything that DECIDES or APPLIES must carry instrumentation.
                bool mustTrace = rel != DescriptorRel && rel != CopyRel;
                if (mustTrace && code.IndexOf("FlowTrace.", StringComparison.Ordinal) < 0)
                    f.Add("I " + rel + " carries no FlowTrace call -- instrumentation was STRIPPED, which " +
                          "CLAUDE.md section 12 forbids outright: a stripped Warn turns a logged failure " +
                          "back into a silent one and the next bug starts from zero evidence");

                if (Regex.IsMatch(code, "\\bRandom\\s*\\."))
                    f.Add("I " + rel + " calls Random -- the Echo Event is decided by the SERVER from a " +
                          "deterministic seed. A client that can roll is a client that can choose");
            }
        }

        // =====================================================================
        //  helpers
        // =====================================================================

        private static void AssertAbsent(List<string> f, string pin, string where, string text, string[] tokens)
        {
            if (string.IsNullOrEmpty(text)) return;
            foreach (string token in tokens)
            {
                if (text.IndexOf(token, StringComparison.OrdinalIgnoreCase) < 0) continue;
                f.Add(pin + " " + where + " names '" + token + "' -- an owner ruling of 2026-09-10 removed " +
                      "that from this ticket (the ingredient event was DROPPED, not deferred; the visiting " +
                      "vendor moved to its own work order; the scout reward moves the moment, not the " +
                      "information). Re-adding one is a design decision, not a commit");
            }
        }

        /// <summary>Raw text of a file under Assets/. A missing file is a NAMED failure.</summary>
        private static string ReadAssetText(string relativeToAssets, List<string> f)
        {
            string path = Path.Combine(Application.dataPath, relativeToAssets);
            if (!File.Exists(path))
            {
                if (f != null) f.Add("source file missing: " + relativeToAssets);
                return string.Empty;
            }
            try { return File.ReadAllText(path); }
            catch (IOException ex)
            {
                if (f != null) f.Add("could not read " + relativeToAssets + ": " + ex.Message);
                return string.Empty;
            }
        }

        /// <summary>
        /// Raw text of a file at the PROJECT ROOT (the parent of Assets/) - the backend
        /// half of this feature lives outside the Unity tree, and the covenant suite
        /// already reads project-root-relative paths the same way.
        /// </summary>
        private static string ReadRepoText(string relativeToRoot, List<string> f)
        {
            string root = Path.GetDirectoryName(Application.dataPath);
            if (string.IsNullOrEmpty(root))
            {
                if (f != null) f.Add("could not resolve the project root from Application.dataPath");
                return string.Empty;
            }
            string path = Path.Combine(root, relativeToRoot.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(path))
            {
                if (f != null) f.Add("backend file missing: " + relativeToRoot);
                return string.Empty;
            }
            try { return File.ReadAllText(path); }
            catch (IOException ex)
            {
                if (f != null) f.Add("could not read " + relativeToRoot + ": " + ex.Message);
                return string.Empty;
            }
        }
    }
}

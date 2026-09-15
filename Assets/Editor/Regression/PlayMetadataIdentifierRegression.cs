// =============================================================================
// PlayMetadataIdentifierRegression [play-metadata-identifiers]
// -----------------------------------------------------------------------------
// Assembly: the editor regression assembly (editor-only).
// Markers: PLAY_METADATA_IDENTIFIERS_OK (Debug.Log) / PLAY_METADATA_IDENTIFIERS_FAIL
// (LogError). Contract mirrors every other oracle here:
//   public static bool Run(out string reason)  -- and it NEVER throws.
//
// WO-1377 (owner ruling 2026-09-09: "Prove persistence first; move only what is
// safe, accept the rest with a recorded reason").
//
// WHAT IT GUARDS -- and why a #if inside a method would not have been enough.
// IL2CPP's global-metadata.dat carries TYPE AND MEMBER NAMES, not just string
// literals. WO-1363 drove the shipping token-bearing STRING literals from 24 down
// to 2 and could not reach zero, because a `#if` around a string removes the
// string and does nothing at all to an identifier. A policy reviewer runs
// `strings` on the artifact, so `IJupiterService`, `RegisterJupiter`, `JupiterSwap`,
// `SwapInputToken` and `USDC` were shipping in the Google Play AAB as names even
// though no reachable code could ever call them.
//
// The fix WO-1377 landed is TYPE-LEVEL compile-out: whole namespace bodies and
// whole member blocks wrapped in `#if !GOOGLE_PLAY`, never a runtime guard.
//
// ⛔ THIS SUITE IS DELIBERATELY TWO-SIDED, AND THAT IS THE WHOLE POINT.
// An "absent under GOOGLE_PLAY" assertion on its own is satisfied by DELETING the
// identifier -- which WO-1377 forbids in its own WHAT NOT TO TOUCH section, because
// Jupiter swap is a real dApp-lane feature that is only ABSENT on Play. So every
// identifier is checked TWICE:
//     * with GOOGLE_PLAY defined     -> it must NOT survive the preprocessor
//     * with GOOGLE_PLAY undefined   -> it MUST survive
// A deletion goes red on the second assertion; a lost `#if` goes red on the first.
//
// ⛔ WHAT THIS SUITE IS NOT: it is a SOURCE-LEVEL oracle. It reads .cs text and
// evaluates the GOOGLE_PLAY preprocessor arms; it does NOT open an artifact.
// **Scanning the physical AAB's global-metadata.dat for `solana` / `jupiter` /
// `usdc` remains a SHIP-CHAIN step** (GooglePlayPackagingGate / the AAB build
// chain), and it is the only thing that can prove the shipped bytes. A source grep
// is exactly what certified the dirty build on 2026-09-01 -- this suite exists to
// stop the SOURCE regressing between ship-chain runs, not to replace that scan.
//
// RESIDUALS, ACCEPTED BY THE OWNER, DELIBERATELY NOT PINNED HERE (see WO-1377 s4):
//   * PaymentChannel.SolanaDappStore -- live, un-#if'd switch case in DeNelle.Core
//     at CurrencySkinResolver.ResolveWagerCurrency, and the owner's Arena ruling is
//     ONE code path with the currency injected per channel, explicitly NOT a
//     `#if GOOGLE_PLAY` inside Arena. Compiling the member out would break that
//     switch on Play.
//   * SkinAuthMode.SolanaWallet -- name-bound to shipped canonical data
//     (skin.json "authMode": "SolanaWallet") through CurrencySkinResolver.ParseAuth,
//     with live references in shipping assemblies.
// Neither is persisted in a save; neither may be RENAMED or REORDERED regardless.
// =============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;

namespace DeNelle.Editor.Regression
{
    /// <summary>
    /// Pins that the WO-1377 "safe set" of crypto identifiers is compiled OUT of the
    /// Google Play variant at the TYPE/MEMBER level, and still compiled IN everywhere else.
    /// </summary>
    public static class PlayMetadataIdentifierRegression
    {
        private const string Tag = "[play-metadata-identifiers]";

        /// <summary>One pinned identifier: the file it lives in and the pattern that finds it.</summary>
        private readonly struct Pin
        {
            public readonly string RelPath;
            public readonly string Pattern;
            public readonly string What;

            public Pin(string relPath, string pattern, string what)
            {
                RelPath = relPath;
                Pattern = pattern;
                What = what;
            }
        }

        private const string JupiterSvcRel  = "Assets/_Modules/Core/Backend/IJupiterService.cs";
        private const string CoreServicesRel = "Assets/_Modules/Core/CoreServices.cs";
        private const string FlagsRel        = "Assets/_Modules/Core/FeatureFlags.cs";
        private const string Web3AsmdefRel   = "Assets/_Modules/Web3/DeNelle.Web3.asmdef";
        // WO-1759. The two SOURCES of the only `skr` occurrences the packaging gate's matcher
        // actually fires on in a Play artifact - measured, see the Pins below.
        private const string StakeResolverRel = "Assets/_Modules/Core/Platform/StakeRewardsResolver.cs";
        private const string StakeSnapshotRel = "Assets/_Modules/Core/Platform/VerifiedStakeSnapshot.cs";
        private const string ArenaWalletRel   = "Assets/_Modules/Village/Arena/ArenaWalletService.cs";

        /// <summary>
        /// WO-1759. One entry of case 4: a file whose GOOGLE_PLAY arm must carry NO string
        /// literal containing a short crypto token — stated as THREE assertions (Play carrier
        /// present, dApp spelling present, token absent), never as one guarded absence.
        /// <para>
        /// ⛔ WHY THE TWO PROOF PATTERNS EXIST AT ALL. The first shape of this case asserted
        /// only the ABSENCE, and `HollowPassScanner` arm D
        /// (<c>D-vacuous-against-absent-fixture</c>) was right to reject it: every assertion sat
        /// inside a positive-existence guard, so if the subject had simply VANISHED — someone
        /// deleting the trace line, or the whole member — the case would have passed over
        /// nothing and reported OK. A suite that passes because its subject disappeared is worse
        /// than no suite, and it is precisely how a reverted exclusion ships unnoticed. So each
        /// entry names what MUST be there in each variant, and those presences are asserted
        /// outright.
        /// </para>
        /// </summary>
        private readonly struct LiteralPin
        {
            /// <summary>The file, repo-relative.</summary>
            public readonly string RelPath;
            /// <summary>Must MATCH in the GOOGLE_PLAY arm — the Play-neutral carrier.</summary>
            public readonly string PlayProof;
            /// <summary>Must MATCH without GOOGLE_PLAY — the dApp / Seeker spelling.</summary>
            public readonly string OffPlayProof;
            public readonly string What;

            public LiteralPin(string relPath, string playProof, string offPlayProof, string what)
            {
                RelPath = relPath;
                PlayProof = playProof;
                OffPlayProof = offPlayProof;
                What = what;
            }
        }

        private static readonly LiteralPin[] LiteralFreeUnderPlay =
        {
            // The trace at VerifiedStakeSnapshot.cs:202 no longer holds the spelling at all: it
            // reads it from StakeStanding.DefaultCurrencySymbol, which is itself `#if
            // GOOGLE_PLAY "pts" / #else "SKR"`. So the SAME reference must be present in BOTH
            // arms, and the Pin above separately holds the const's two spellings. If the trace
            // is deleted outright, this goes red in both variants.
            new LiteralPin(StakeSnapshotRel,
                           @"StakeStanding\.DefaultCurrencySymbol",
                           @"StakeStanding\.DefaultCurrencySymbol",
                           "the stake-verification trace's currency symbol (WO-1759: routed through " +
                           "the one compile-time-neutral symbol, not a literal in this file)"),

            // The Arena stub key swaps spelling per variant, so each arm proves its own.
            new LiteralPin(ArenaWalletRel,
                           @"""dotr-arena-wager-balance""",
                           @"""dotr-arena-skr-balance""",
                           "ArenaWalletService.PrefBalanceKey (WO-1366 s4 ruled key off-Play, " +
                           "neutral spelling on Play)"),
        };


        /// <summary>
        /// The SAFE SET, proven unpersisted and free of any GOOGLE_PLAY-side consumer in
        /// Phase 1 of WO-1377. Patterns match DECLARATIONS, after comments are stripped --
        /// the explanatory headers added by WO-1377 deliberately sit OUTSIDE the guards and
        /// would otherwise read as survivors.
        /// </summary>
        private static readonly Pin[] Pins =
        {
            new Pin(JupiterSvcRel,   @"interface\s+IJupiterService\b",      "IJupiterService (the interface type)"),
            new Pin(JupiterSvcRel,   @"class\s+SwapQuote\b",                "SwapQuote (the quote DTO)"),
            new Pin(JupiterSvcRel,   @"enum\s+SwapInputToken\b",            "SwapInputToken (the enum type)"),
            new Pin(JupiterSvcRel,   @"\bUSDC\s*=\s*0\b",                   "SwapInputToken.USDC (the enum member)"),
            new Pin(CoreServicesRel, @"IJupiterService\s+Jupiter\b",        "CoreServices.Jupiter (the slot)"),
            new Pin(CoreServicesRel, @"\bRegisterJupiter\s*\(",             "CoreServices.RegisterJupiter"),
            new Pin(CoreServicesRel, @"\bUnregisterJupiter\s*\(",           "CoreServices.UnregisterJupiter"),
            new Pin(FlagsRel,        @"bool\s+JupiterSwap\s*=>",            "FeatureFlags.JupiterSwap (the property)"),
            new Pin(FlagsRel,        @"""jupiterswap""",                    "the \"jupiterswap\" PlayerPrefs key literal"),

            // ── WO-1759 ────────────────────────────────────────────────────────────────
            // MEASURED, not assumed. The 2026-09-15 16:55 rejected Play AAB's
            // global-metadata.dat (19,874,336 bytes) holds 43 raw `skr` occurrences, and the
            // packaging gate's own matcher fires on exactly THREE of them. Both pins below are
            // the SOURCE of one of those three; nothing else in that file fired.
            //
            // ⚠ The other 40 - costSkr, stakedSkr, SkrShowcasePanel, NativeSkrPolishBonus,
            // VoidTaskResult, colorMaskRtHandle and the rest - are ALREADY suppressed by
            // MatchesTokenInWindow's leading/trailing word-boundary rules and the
            // MinPrintableRunForShortTokens floor. They are not pinned here because they never
            // fired; PlayGateChunkSeamRegression pins that they keep not firing.

            // offset 238,770: the folded literal " SKR, age=". Roslyn merges the adjacent
            // constants `" SKR" + ", age="`, so a reviewer's `strings` pass reads the token.
            // The spelling now comes from the one compile-time-neutral symbol, so this pin is
            // on THAT symbol: it must be "SKR" off-Play (the dApp build is unchanged) and must
            // not survive GOOGLE_PLAY.
            new Pin(StakeResolverRel, @"DefaultCurrencySymbol\s*=\s*""SKR""",
                    "StakeStanding.DefaultCurrencySymbol = \"SKR\" (the single owner of the spelling)"),

            // offsets 1,587,839 and 10,556,064: the Arena stub PlayerPrefs key. Not migratable
            // and not allowlistable - see the reasoning block at ArenaWalletService.cs:47-67 -
            // so the Play arm spells it differently and the dApp arm keeps the ruled key.
            new Pin(ArenaWalletRel,   @"""dotr-arena-skr-balance""",
                    "ArenaWalletService.PrefBalanceKey = \"dotr-arena-skr-balance\" (the WO-1366 s4 ruled key)"),
        };

        /// <summary>Standalone batch entry - prints the marker.</summary>
        public static void RunAll()
        {
            if (Run(out string reason)) Debug.Log("PLAY_METADATA_IDENTIFIERS_OK - " + reason);
            else Debug.LogError("PLAY_METADATA_IDENTIFIERS_FAIL: " + reason);
        }

        public static bool Run(out string reason)
        {
            var failures = new List<string>();
            var log = new StringBuilder();
            log.AppendLine("=== PlayMetadataIdentifierRegression " + Tag + " ===");

            try
            {
                CaseIdentifiersCompiledOutOnPlay(failures, log);
                CaseIdentifiersStillExistOffPlay(failures, log);
                CaseWeb3AssemblyStillExcluded(failures, log);
                CaseNoShortTokenLiteralSurvivesPlay(failures, log);
            }
            catch (Exception ex)
            {
                failures.Add(Tag + " THREW: " + ex.GetType().Name + ": " + ex.Message);
            }

            if (failures.Count > 0)
            {
                reason = string.Join(" | ", failures);
                Debug.Log(log.ToString());
                return false;
            }

            reason = Pins.Length + " identifier(s) absent under GOOGLE_PLAY and present without it; " +
                     "DeNelle.Web3 still !GOOGLE_PLAY-constrained; " +
                     LiteralFreeUnderPlay.Length + " file(s) carry no `skr` string literal under GOOGLE_PLAY. " +
                     "(SOURCE-level only - the global-metadata.dat scan is a ship-chain step.)";
            Debug.Log(log.ToString());
            return true;
        }

        // =====================================================================
        //  CASE 1 - with GOOGLE_PLAY defined, none of the safe set survives
        // =====================================================================
        private static void CaseIdentifiersCompiledOutOnPlay(List<string> failures, StringBuilder log)
        {
            log.AppendLine("-- case 1 with GOOGLE_PLAY defined: the safe set must be GONE --");

            foreach (Pin pin in Pins)
            {
                string src = ReadPreprocessed(pin.RelPath, googlePlay: true, failures, log);
                if (src == null) continue;

                if (Regex.IsMatch(src, pin.Pattern))
                {
                    failures.Add(Tag + " " + pin.What + " STILL COMPILES under GOOGLE_PLAY (" +
                                 pin.RelPath + "). IL2CPP ships type and member NAMES, so this " +
                                 "identifier lands in the Play AAB's global-metadata.dat and a " +
                                 "reviewer's `strings` run finds it -- exactly the WO-1377 defect. " +
                                 "A runtime guard INSIDE a method does not fix this: the guard must " +
                                 "be `#if !GOOGLE_PLAY` around the whole TYPE or MEMBER.");
                }
                else
                {
                    log.AppendLine("   GONE under GOOGLE_PLAY: " + pin.What + "  OK");
                }
            }
        }

        // =====================================================================
        //  CASE 2 - without GOOGLE_PLAY, every one of them is still there
        // =====================================================================
        private static void CaseIdentifiersStillExistOffPlay(List<string> failures, StringBuilder log)
        {
            log.AppendLine("-- case 2 without GOOGLE_PLAY: the safe set must STILL EXIST --");

            foreach (Pin pin in Pins)
            {
                string src = ReadPreprocessed(pin.RelPath, googlePlay: false, failures, log);
                if (src == null) continue;

                if (!Regex.IsMatch(src, pin.Pattern))
                {
                    failures.Add(Tag + " " + pin.What + " NO LONGER EXISTS even without GOOGLE_PLAY (" +
                                 pin.RelPath + "). DO NOT DELETE: every pinned surface here (WO-1377's " +
                                 "Jupiter swap, WO-1759's SKR spelling and the WO-1366 s4 Arena stub key) " +
                                 "is a real dApp / Seeker-lane surface and is only ABSENT on Play. Case 1 " +
                                 "passing by DELETION is the failure this case exists to catch.");
                }
                else
                {
                    log.AppendLine("   present off-Play: " + pin.What + "  OK");
                }
            }
        }

        // =====================================================================
        //  CASE 3 - the assembly exclusion the whole shape rests on
        // =====================================================================
        private static void CaseWeb3AssemblyStillExcluded(List<string> failures, StringBuilder log)
        {
            log.AppendLine("-- case 3 DeNelle.Web3 still carries \"!GOOGLE_PLAY\" --");

            string path = Path.Combine(RepoRoot(), Web3AsmdefRel.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(path))
            {
                failures.Add(Tag + " " + Web3AsmdefRel + " NOT FOUND. WO-1377's safe set is only safe " +
                             "because every implementation of these types lives in DeNelle.Web3 and that " +
                             "assembly is excluded from the Play variant. If the asmdef moved, re-prove " +
                             "the exclusion before trusting case 1.");
                return;
            }

            string text = File.ReadAllText(path);
            if (!text.Contains("!GOOGLE_PLAY"))
            {
                failures.Add(Tag + " DeNelle.Web3.asmdef no longer carries \"!GOOGLE_PLAY\" in its " +
                             "defineConstraints. WO-1362 measured this as the ONE exclusion tier that " +
                             "genuinely works (the merged dex contains no MWA and no Solana), and " +
                             "WO-1377 says DO NOT WIDEN IT. Without it, JupiterSwapService and " +
                             "JupiterSwapBootstrap compile into the Play AAB and every identifier this " +
                             "suite just proved absent from Core comes back through DeNelle.Web3.");
            }
            else
            {
                log.AppendLine("   DeNelle.Web3.asmdef defineConstraints contains \"!GOOGLE_PLAY\"  OK");
            }
        }

        // =====================================================================
        //  CASE 4 - WO-1759: no `skr` STRING LITERAL survives the GOOGLE_PLAY arm
        // =====================================================================
        /// <summary>
        /// The half a two-sided Pin cannot express. Cases 1+2 catch a lost <c>#if</c> around a
        /// literal that exists in BOTH spellings; they cannot catch a revert that puts a BARE
        /// literal back where the fix installed a neutral symbol, because "absent under
        /// GOOGLE_PLAY" would then still pass on the symbol's own pin.
        /// <para>
        /// ⚠ Why a literal and not an identifier: this suite's header explains that a `#if`
        /// inside a method removes neither. The measurement behind WO-1759 is narrower and
        /// worth stating - in the 16:55 rejected AAB the gate's matcher fired on THREE
        /// occurrences, and all three were STRING LITERALS. Every `skr`-bearing IDENTIFIER in
        /// that artifact (costSkr, stakedSkr, SkrShowcasePanel, NativeSkrPolishBonus, and the
        /// BCL's own VoidTaskResult / colorMaskRtHandle) was already suppressed by
        /// GooglePlayPackagingGate's word-boundary rules. So the literal is the live axis here.
        /// </para>
        /// </summary>
        private static void CaseNoShortTokenLiteralSurvivesPlay(List<string> failures, StringBuilder log)
        {
            log.AppendLine("-- case 4 with GOOGLE_PLAY defined: no `skr` STRING LITERAL may survive --");

            foreach (LiteralPin pin in LiteralFreeUnderPlay)
            {
                string rel = pin.RelPath;

                // ---- 4a. the Play-side carrier EXISTS. Not a guard: an assertion. ----------
                // This is the half arm D demanded. Absence of a token is only meaningful when
                // the thing that replaced it is proven present; otherwise a deletion reads as a
                // pass.
                // `?? string.Empty` and NOT a `!= null` guard, deliberately: a guard reading
                // `playArm != null` is itself a POSITIVE-EXISTENCE guard, so wrapping the
                // assertion in one would land straight back in HollowPassScanner arm D. A
                // missing file is already a recorded failure, and an empty arm fails the match
                // below on its own, which is the honest verdict either way.
                string playArm = ReadPreprocessed(rel, googlePlay: true, failures, log) ?? string.Empty;
                if (!Regex.IsMatch(playArm, pin.PlayProof))
                {
                    failures.Add(Tag + " " + pin.What + " is GONE from the GOOGLE_PLAY arm of " + rel +
                                 ". Case 4 asserts an ABSENCE (no `skr` literal), and an absence " +
                                 "means nothing if the subject itself vanished - that is exactly the " +
                                 "vacuous pass HollowPassScanner arm D rejects. Restore the Play-neutral " +
                                 "carrier; do not satisfy this by deleting the check.");
                }

                // ---- 4b. the dApp / Seeker spelling SURVIVES. --------------------------------
                // The variant-scoping half: this WO is an exclusion, never a deletion, and the
                // Solana build must still compile its own spelling.
                string offPlayArm = ReadPreprocessed(rel, googlePlay: false, failures, log) ?? string.Empty;
                if (!Regex.IsMatch(offPlayArm, pin.OffPlayProof))
                {
                    failures.Add(Tag + " " + pin.What + " is GONE from the NON-GOOGLE_PLAY arm of " +
                                 rel + ". WO-1759 is variant SCOPING, not removal: the dApp Store / " +
                                 "Seeker build must keep its SKR surface intact (memory " +
                                 "android-seeker-distribution-and-wallet-strategy), and the WO-1377 " +
                                 "rule still stands - never renamed, never reordered.");
                }

                // ---- 4c. and only THEN, the absence itself. ----------------------------------
                // ⛔ NOT A REGEX OVER THE SOURCE, and the difference is not style.
                // A quote-to-quote pattern cannot tell a literal from the GAP BETWEEN two
                // literals: `"stake=" + stakedSkr + "s"` and `$"stake={activeStakeSkr}"` would
                // both match on the identifier in the middle, which never reaches metadata.
                // These are staking and arena files, where that shape is the norm -
                // StakeRewardsResolver.cs:231 already has one - so the regex would have gone
                // red on working code. RegressionSourceText is LENGTH-PRESERVING by design, so
                // the string bodies are EXACTLY the indices where the two strippers differ.
                // Both passes run through the same preprocessor over the same line structure,
                // so the pair stays index-aligned.
                string blanked = ReadPreprocessed(rel, googlePlay: true, failures, log,
                                                  blankStringBodies: true);

                // A missing file was ALREADY reported as a failure by ReadPreprocessed above,
                // so skipping here double-reports nothing and swallows nothing.
                string found = FirstSkrBearingLiteral(playArm, blanked);
                if (found != null)
                {
                    failures.Add(Tag + " a string literal containing `skr` SURVIVES GOOGLE_PLAY in " +
                                 rel + ": " + found + ". IL2CPP writes every literal into " +
                                 "global-metadata.dat whether its branch runs or not, so a reviewer's " +
                                 "`strings` pass reads it and GooglePlayPackagingGate rejects the AAB " +
                                 "with PLAY_ARTIFACT_DIRTY token:skr - measured at offsets 238,770 / " +
                                 "1,587,839 / 10,556,064 in the 2026-09-15 16:55 rejected build. Put " +
                                 "the spelling behind a compile-time-neutral symbol (see " +
                                 "StakeStanding.DefaultCurrencySymbol) or a `#if GOOGLE_PLAY` arm - " +
                                 "never a runtime guard, and never by deleting the dApp-side spelling.");
                }
                else
                {
                    log.AppendLine("   no `skr` literal under GOOGLE_PLAY: " + rel + "  OK");
                }
            }
        }

        /// <summary>
        /// Context around the first <c>skr</c> that is inside a SHIPPED string-literal body, or
        /// null when there is none.
        /// <para>
        /// ⛔ THE TEST IS PER-CHARACTER, NOT PER-RUN, AND THAT WAS A MEASURED CORRECTION.
        /// The obvious form - take maximal runs where <paramref name="kept"/> and
        /// <paramref name="blanked"/> differ and call each run a literal body - is WRONG,
        /// because a SPACE inside a literal is blanked to itself: the two views agree there, so
        /// one literal splits into several runs and every run loses the <c>$</c> that says it
        /// was interpolated. The token has no spaces, so the exact question is asked directly
        /// of its three characters: they are blank in the string-stripped view iff they sit in
        /// a literal.
        /// </para>
        /// Length mismatch is treated as "cannot decide" and returns null rather than guessing;
        /// cases 1 and 2 still cover these files either way.
        /// </summary>
        private static string FirstSkrBearingLiteral(string kept, string blanked)
        {
            if (kept == null || blanked == null || kept.Length != blanked.Length) return null;

            int at = -1;
            while ((at = kept.IndexOf("skr", at + 1, StringComparison.OrdinalIgnoreCase)) >= 0)
            {
                if (at + 2 >= blanked.Length) break;
                if (blanked[at] != ' ' || blanked[at + 1] != ' ' || blanked[at + 2] != ' ') continue;
                if (InInterpolationHole(kept, blanked, at)) continue;

                int from = Math.Max(0, at - 40);
                int to = Math.Min(kept.Length, at + 43);
                return kept.Substring(from, to - from).Replace('\n', ' ').Replace('\r', ' ').Trim();
            }
            return null;
        }

        /// <summary>
        /// True when the hit sits in a <c>{...}</c> hole of an INTERPOLATED literal. A hole is
        /// compiled away - <c>$"stake={activeStakeSkr}"</c> puts only <c>"stake="</c> into
        /// global-metadata.dat, never the identifier - so reporting one would be a leak that
        /// cannot exist, and these are staking and arena files where that shape is the norm
        /// (StakeRewardsResolver.cs:231 already has one). A plain, un-interpolated
        /// <c>"{skr}"</c> is still read as the shipped text it is.
        /// </summary>
        private static bool InInterpolationHole(string kept, string blanked, int at)
        {
            // Back up to the start of the blanked stretch, then take the first quote in the
            // KEPT view: that is this literal's opening quote (the stripper blanks quotes too,
            // so the stretch always reaches back past it).
            int j = at;
            while (j > 0 && blanked[j - 1] == ' ') j--;

            int q = kept.IndexOf('"', j);
            if (q < 0 || q > at) return false;

            bool interpolated = (q > 0 && kept[q - 1] == '$') ||
                                (q > 1 && kept[q - 1] == '@' && kept[q - 2] == '$');
            if (!interpolated) return false;

            int depth = 0;
            for (int k = q + 1; k < at; k++)
            {
                char c = kept[k];
                if (c == HoleOpen)
                {
                    if (depth == 0 && k + 1 < at && kept[k + 1] == HoleOpen) { k++; continue; }
                    depth++;
                }
                else if (c == HoleClose && depth > 0) depth--;
            }
            return depth > 0;
        }

        // Declared as a balanced PAIR on one line, following HollowPassScanner.cs:143-144:
        // the compile gate's comment- and string-aware scan reads a lone brace CHAR LITERAL
        // correctly, but CLAUDE.md rule 1's raw counter does not, and three of them here left
        // the file reading 61/60 while the gate read 55/55. Named constants settle both.
        private const char HoleOpen = '{', HoleClose = '}';

        // =====================================================================
        //  Source reading: strip comments, then evaluate the GOOGLE_PLAY arms
        // =====================================================================

        private static string RepoRoot()
        {
            // Application.dataPath is <repo>/Assets.
            return Directory.GetParent(Application.dataPath).FullName;
        }

        /// <summary>
        /// Reads a .cs file, strips comments, and removes the preprocessor arms that would
        /// NOT compile for the given GOOGLE_PLAY state. Returns null (and records a failure)
        /// when the file is missing.
        /// </summary>
        private static string ReadPreprocessed(string relPath, bool googlePlay,
                                               List<string> failures, StringBuilder log,
                                               bool blankStringBodies = false)
        {
            string path = Path.Combine(RepoRoot(), relPath.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(path))
            {
                failures.Add(Tag + " source file NOT FOUND: " + relPath +
                             " - the pin cannot be evaluated, so this is a FAILURE, not a skip. " +
                             "If the file moved, move the pin with it in the same change.");
                return null;
            }

            string raw;
            try { raw = File.ReadAllText(path); }
            catch (Exception ex)
            {
                failures.Add(Tag + " could not read " + relPath + ": " + ex.Message);
                return null;
            }

            // RegressionSourceText.StripComments, NOT a hand-rolled stripper and NOT
            // StripCommentsAndStrings: one of the pins IS a quoted literal ("jupiterswap"),
            // which the full stripper would blank out from under the very assertion that needs
            // it - the same reasoning ShippedSurfaceGateRegression.ReadStripComments records at
            // its own call site. Comment bodies MUST go: comments never reach IL2CPP metadata,
            // and WO-1377's own explanatory headers deliberately sit OUTSIDE the guards and name
            // every identifier they removed, so matching comment text would invert the result.
            return EvaluateGooglePlayArms(blankStringBodies
                                              ? RegressionSourceText.StripCommentsAndStrings(raw)
                                              : RegressionSourceText.StripComments(raw),
                                          googlePlay);
        }

        /// <summary>
        /// A deliberately NARROW preprocessor: it resolves only conditions that are exactly
        /// <c>GOOGLE_PLAY</c> or <c>!GOOGLE_PLAY</c>. Every other <c>#if</c> is treated as
        /// "keep every arm".
        /// <para>
        /// That conservatism is chosen on purpose and in the SAFE direction. Keeping an arm
        /// that would really have been dropped can only make an identifier look PRESENT, so
        /// case 1 can produce a false FAILURE (someone reads the source and finds the guard is
        /// fine) but never a false PASS (an identifier silently shipping). A general C#
        /// preprocessor here would be more code, more wrong, and would fail in the other
        /// direction.
        /// </para>
        /// </summary>
        private static string EvaluateGooglePlayArms(string src, bool googlePlay)
        {
            var outp = new StringBuilder(src.Length);

            // Each frame: relevant = this #if is a bare GOOGLE_PLAY / !GOOGLE_PLAY test;
            // emit = whether lines in the CURRENT arm are kept.
            var relevant = new Stack<bool>();
            var emit = new Stack<bool>();

            foreach (string line in src.Split('\n'))
            {
                string t = line.TrimStart();

                if (t.StartsWith("#if", StringComparison.Ordinal) &&
                    !t.StartsWith("#ifdef", StringComparison.Ordinal))
                {
                    string cond = Condition(t, "#if");
                    bool parentEmits = emit.Count == 0 || emit.Peek();

                    if (cond == "GOOGLE_PLAY") { relevant.Push(true); emit.Push(parentEmits && googlePlay); }
                    else if (cond == "!GOOGLE_PLAY") { relevant.Push(true); emit.Push(parentEmits && !googlePlay); }
                    else { relevant.Push(false); emit.Push(parentEmits); }
                    continue;
                }

                if (t.StartsWith("#elif", StringComparison.Ordinal))
                {
                    // A GOOGLE_PLAY frame that reaches an #elif is beyond this narrow evaluator.
                    // Degrade to "keep everything" - the safe direction (see the doc comment).
                    if (relevant.Count > 0)
                    {
                        relevant.Pop(); relevant.Push(false);
                        bool parentEmits = ParentEmits(emit);
                        emit.Pop(); emit.Push(parentEmits);
                    }
                    continue;
                }

                if (t.StartsWith("#else", StringComparison.Ordinal))
                {
                    if (relevant.Count > 0)
                    {
                        bool isRelevant = relevant.Peek();
                        bool parentEmits = ParentEmits(emit);
                        bool current = emit.Pop();
                        emit.Push(isRelevant ? (parentEmits && !current) : parentEmits);
                    }
                    continue;
                }

                if (t.StartsWith("#endif", StringComparison.Ordinal))
                {
                    if (relevant.Count > 0) relevant.Pop();
                    if (emit.Count > 0) emit.Pop();
                    continue;
                }

                // Any other directive (#region, #pragma, #define, #warning) is not content.
                if (t.StartsWith("#", StringComparison.Ordinal)) continue;

                if (emit.Count == 0 || emit.Peek()) outp.Append(line).Append('\n');
            }

            return outp.ToString();
        }

        /// <summary>Whether the frame ENCLOSING the top of the stack is emitting.</summary>
        private static bool ParentEmits(Stack<bool> emit)
        {
            if (emit.Count <= 1) return true;
            bool top = emit.Pop();
            bool parent = emit.Peek();
            emit.Push(top);
            return parent;
        }

        /// <summary>The normalised text of a directive's condition (whitespace removed).</summary>
        private static string Condition(string trimmedLine, string directive)
        {
            string rest = trimmedLine.Substring(directive.Length);
            return Regex.Replace(rest, @"\s+", string.Empty);
        }
    }
}

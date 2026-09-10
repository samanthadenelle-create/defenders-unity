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

        private const string JupiterSvcRel  = "Assets/_Modules/Core/Web3/IJupiterService.cs";
        private const string CoreServicesRel = "Assets/_Modules/Core/CoreServices.cs";
        private const string FlagsRel        = "Assets/_Modules/Core/FeatureFlags.cs";
        private const string Web3AsmdefRel   = "Assets/_Modules/Web3/DeNelle.Web3.asmdef";

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
                     "DeNelle.Web3 still !GOOGLE_PLAY-constrained. " +
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
                                 pin.RelPath + "). WO-1377 says DO NOT DELETE: Jupiter swap is a real " +
                                 "dApp-lane feature and is only ABSENT on Play. Case 1 passing by " +
                                 "DELETION is the failure this case exists to catch.");
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
                                               List<string> failures, StringBuilder log)
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
            return EvaluateGooglePlayArms(RegressionSourceText.StripComments(raw), googlePlay);
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

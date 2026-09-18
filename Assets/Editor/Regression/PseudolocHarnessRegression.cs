// =============================================================================
// PseudolocHarnessRegression [pseudoloc-harness] — WO-1861.
// -----------------------------------------------------------------------------
// Assembly: DeNelle.EditorRegression. Namespace: DeNelle.Editor.Regression.
//
// Pins BOTH halves of the pseudolocalization harness: the SHIP QUARANTINE around it
// (source-lints, mirroring DeviceScenarioKitRegression for WO-1775's kit) and — the
// half a source-lint cannot reach — the ORACLE ACTUALLY GOING RED on a planted leak.
//
//   1 [gate]             PseudolocTextProvider.cs compiles ONLY under
//                         `#if UNITY_EDITOR || QA_SCENARIO_BUILD` — the whole file body is
//                         wrapped, first real line to last.
//   2 [localtext-gate]   LocalText's PseudolocHook field sits inside that same guard, the
//                         three table-resolved returns route through Pseudo(), and a
//                         call-site englishFallback is NEVER transformed (transforming it
//                         would paint the very leak WO-1857 hunts).
//   3 [store-quarantine] Neither AndroidBuild.cs nor any known ship/distribution script
//                         names the provider, the pref key or the sweep entry point; and
//                         FeatureFlags.cs does not declare a reachable pseudoloc property.
//   4 [transform]        MEASURED behaviour: Latin -> Cyrillic; digits, punctuation,
//                         {placeholders} and <rich text> preserved verbatim; idempotent;
//                         and every one of the 52 target characters is present in the live
//                         ru.json, so the transform can never be the thing that tofus.
//   5 [oracle-red]       THE ANTI-TAUTOLOGY CHECK. A synthetic canvas carrying a PLANTED
//                         English leak, an allowlisted brand token, two already-transformed
//                         labels and a digits-only label. Asserts the EXACT counts: three
//                         findings, all on the planted label, one suppression, five labels
//                         scanned, two transformed. An oracle nobody has seen go red is not
//                         evidence (this repo's own words, UICaptureLaunch.cs:6490-6502).
//   6 [allowlist]        docs/localization/PSEUDOLOC_ALLOWLIST.md exists, parses with zero
//                         problems, yields entries, and every entry carries a reason.
//   7 [capture-wiring]   UICaptureLaunch calls AuditPseudolocLeaks from RenderCanvasToPng
//                         (the one point every captured panel passes through), names both
//                         verdict markers, and exposes RunPseudolocCaptureHeadless.
//   8 [hygiene]          No embedded NUL in the touched sources (CLAUDE.md §0).
//
// Every source-lint reads CODE ONLY where it matters (comment lines dropped, string
// literal CONTENTS blanked) — same CodeText/StripStringLiterals idiom as
// DeviceScenarioKitRegression, so this file's own header, which necessarily NAMES every
// token it forbids, can never satisfy or trip a rule that is about a CALL or a LITERAL.
//
// Markers: PSEUDOLOC_HARNESS_OK / PSEUDOLOC_HARNESS_FAIL.
// Standalone: run-unity-method -Method DeNelle.Editor.Regression.PseudolocHarnessRegression.RunAll
// Registered in DataRegression.RunAll as the "pseudoloc-harness suite".
// =============================================================================
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using DeNelle.Core.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DeNelle.Editor.Regression
{
    public static class PseudolocHarnessRegression
    {
        private const string ProviderSrc = "Assets/_Modules/Core/UI/PseudolocTextProvider.cs";
        private const string LocalTextSrc = "Assets/_Modules/Core/UI/LocalText.cs";
        private const string FeatureFlagsSrc = "Assets/_Modules/Core/FeatureFlags.cs";
        private const string CaptureSrc = "Assets/Editor/UICaptureLaunch.cs";
        private const string OracleSrc = "Assets/Editor/Regression/PseudolocLeakOracle.cs";
        private const string AndroidBuildSrc = "Assets/Editor/AndroidBuild.cs";
        private const string RuTableSrc = "Assets/Resources/Data/Canonical/ru.json";

        private const string GuardText = "#if UNITY_EDITOR || QA_SCENARIO_BUILD";

        /// <summary>The brand token check 5 relies on. Named here so the suite fails loudly if
        /// somebody removes it from the allowlist rather than silently losing its suppression
        /// assertion.</summary>
        private const string AllowlistedBrand = "Solana";

        // Known ship/distribution scripts this harness must never reach. Read-only membership
        // list, matching DeviceScenarioKitRegression's — WO-1741 / CLAUDE.md §16 own the chain
        // itself; this only asserts none of its scripts NAME the pseudoloc harness.
        private static readonly string[] ShipChainScripts =
        {
            "morning-ship-chain.ps1",
            "distribute-android.ps1",
            "install-apk-to-seeker.ps1",
            "tools/r2-ship.ps1",
        };

        public static void RunAll()
        {
            if (Run(out string reason)) Debug.Log("PSEUDOLOC_HARNESS_OK - " + reason);
            else Debug.LogError("PSEUDOLOC_HARNESS_FAIL - " + reason);
        }

        public static bool Run(out string reason)
        {
            var failures = new List<string>();
            int findings = -1, labels = -1;
            try
            {
                CheckProviderFileGate(failures);
                CheckLocalTextGate(failures);
                CheckStoreQuarantine(failures);
                CheckTransform(failures);
                CheckOracleGoesRed(failures, ref findings, ref labels);
                CheckAllowlist(failures);
                CheckCaptureWiring(failures);
                CheckHygiene(failures);
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
            reason = "WO-1861 pseudoloc harness holds: the provider file compiles only under " +
                     GuardText + "; LocalText's hook sits inside the same guard, transforms the three " +
                     "table-resolved returns and never a call-site englishFallback; no ship script, " +
                     "AndroidBuild.cs or FeatureFlags property names it; the transform maps Latin to " +
                     "Cyrillic while preserving digits, {placeholders} and <rich text>, is idempotent, " +
                     "and every one of its 52 targets is present in " + RuTableSrc + "; the leak oracle " +
                     "PROVABLY goes red (" + findings + " findings over " + labels + " synthetic labels, " +
                     "one brand suppression, planted leak caught, allowlisted token not flagged); the " +
                     "allowlist parses clean; the capture harness wires the scan into RenderCanvasToPng; " +
                     "no NULs.";
            return true;
        }

        // -- 1 [gate] -----------------------------------------------------
        private static void CheckProviderFileGate(List<string> failures)
        {
            string raw = ReadSrc(ProviderSrc, failures);
            if (raw == null) return;
            var lines = raw.Replace("\r\n", "\n").TrimStart().Split('\n');

            int firstCode = -1;
            for (int i = 0; i < lines.Length; i++)
            {
                string l = lines[i].TrimStart();
                if (l.Length == 0 || l.StartsWith("//", StringComparison.Ordinal)) continue;
                firstCode = i;
                break;
            }
            if (firstCode < 0 || !lines[firstCode].TrimStart().StartsWith(GuardText, StringComparison.Ordinal))
                failures.Add("[gate] " + ProviderSrc + " no longer opens with '" + GuardText + "' as its " +
                             "first real line — a dev-only Cyrillic transform over every player-facing " +
                             "string would compile into a shipping DeNelle.Core");

            string lastNonEmpty = null;
            for (int j = lines.Length - 1; j >= 0; j--)
            {
                if (lines[j].Trim().Length == 0) continue;
                lastNonEmpty = lines[j].Trim();
                break;
            }
            if (lastNonEmpty != "#endif")
                failures.Add("[gate] " + ProviderSrc + " does not end on a bare '#endif' — the " +
                             "whole-file wrap is no longer provably closing the file");
        }

        // -- 2 [localtext-gate] ---------------------------------------------
        private static void CheckLocalTextGate(List<string> failures)
        {
            string raw = ReadSrc(LocalTextSrc, failures);
            if (raw == null) return;
            string[] lines = raw.Replace("\r\n", "\n").Split('\n');

            // Directive-aware: track the #if stack and which branch we are in, so "inside the
            // guard" is decided by structure and not by a nearby text match.
            var conditions = new List<string>();
            var inElse = new List<bool>();
            int hookMentions = 0, hookGuarded = 0;

            for (int i = 0; i < lines.Length; i++)
            {
                string t = lines[i].TrimStart();
                if (t.StartsWith("#if", StringComparison.Ordinal))
                {
                    conditions.Add(t);
                    inElse.Add(false);
                    continue;
                }
                if (t.StartsWith("#elif", StringComparison.Ordinal) ||
                    t.StartsWith("#else", StringComparison.Ordinal))
                {
                    if (inElse.Count > 0) inElse[inElse.Count - 1] = true;
                    continue;
                }
                if (t.StartsWith("#endif", StringComparison.Ordinal))
                {
                    if (conditions.Count > 0)
                    {
                        conditions.RemoveAt(conditions.Count - 1);
                        inElse.RemoveAt(inElse.Count - 1);
                    }
                    continue;
                }
                if (t.StartsWith("//", StringComparison.Ordinal)) continue;
                if (lines[i].IndexOf("PseudolocHook", StringComparison.Ordinal) < 0) continue;

                hookMentions++;
                for (int f = 0; f < conditions.Count; f++)
                {
                    if (inElse[f]) continue;
                    if (conditions[f].IndexOf("UNITY_EDITOR", StringComparison.Ordinal) >= 0 &&
                        conditions[f].IndexOf("QA_SCENARIO_BUILD", StringComparison.Ordinal) >= 0)
                    {
                        hookGuarded++;
                        break;
                    }
                }
            }

            if (hookMentions == 0)
                failures.Add("[localtext-gate] " + LocalTextSrc + " no longer mentions PseudolocHook in " +
                             "code — the post-resolve seam is gone, so pseudoloc cannot reach the JSON " +
                             "fallback path the edit-mode capture harness actually uses");
            else if (hookGuarded != hookMentions)
                failures.Add("[localtext-gate] " + (hookMentions - hookGuarded) + " of " + hookMentions +
                             " PseudolocHook code line(s) in " + LocalTextSrc + " sit OUTSIDE a " +
                             "'" + GuardText + "' #if branch — a shipping build would carry the hook");

            string code = CodeText(raw);
            int wrapped = CountOccurrences(code, "Pseudo(");
            // 3 table-resolved returns + the 2 definitions (#if and #else branches) = 5.
            if (wrapped < 5)
                failures.Add("[localtext-gate] only " + wrapped + " 'Pseudo(' site(s) in " + LocalTextSrc +
                             " (expected at least 5: the three table-resolved returns in TryGet plus the " +
                             "guarded and unguarded definitions). A return that stopped routing through it " +
                             "is a screen that silently stays English under pseudoloc.");

            foreach (string line in lines)
            {
                if (line.TrimStart().StartsWith("//", StringComparison.Ordinal)) continue;
                if (line.IndexOf("englishFallback", StringComparison.Ordinal) >= 0 &&
                    line.IndexOf("Pseudo(", StringComparison.Ordinal) >= 0)
                {
                    failures.Add("[localtext-gate] a call-site englishFallback in " + LocalTextSrc +
                                 " is routed through Pseudo(). Reaching that fallback MEANS the key is " +
                                 "absent from the table — i.e. it is exactly the leak WO-1857 exists to " +
                                 "find. Transforming it paints the leak Cyrillic and deletes it from the " +
                                 "report. Leave it in English.");
                    break;
                }
            }
        }

        // -- 3 [store-quarantine] -------------------------------------------
        private static void CheckStoreQuarantine(List<string> failures)
        {
            string[] forbidden =
            {
                "PseudolocTextProvider", "ff.pseudoloc", "RunPseudolocCaptureHeadless", "PseudolocHook",
            };

            string android = ReadSrc(AndroidBuildSrc, failures);
            if (android != null)
                foreach (string token in forbidden)
                    if (android.IndexOf(token, StringComparison.Ordinal) >= 0)
                        failures.Add("[store-quarantine] " + AndroidBuildSrc + " names '" + token +
                                     "' — the Android build pipeline must never bake in the pseudoloc " +
                                     "harness in any form");

            foreach (string script in ShipChainScripts)
            {
                if (!File.Exists(script)) continue; // named by path, not required on every checkout
                string raw;
                try { raw = File.ReadAllText(script); }
                catch (Exception ex) { failures.Add("[store-quarantine] " + script + ": " + ex.Message); continue; }
                foreach (string token in forbidden)
                    if (raw.IndexOf(token, StringComparison.Ordinal) >= 0)
                        failures.Add("[store-quarantine] " + script + " names '" + token +
                                     "' — a store/Play/Firebase-tester distribution script must never " +
                                     "carry the pseudoloc harness");
            }

            // FeatureFlags is unguarded shipping code. A property there would be a reachable door
            // into the transform. The breadcrumb comment pointing at PseudolocTextProvider.PrefKey
            // is fine and intended — CodeText drops comment lines, so only real code can trip this.
            string flagsCode = ReadCode(FeatureFlagsSrc, failures);
            if (flagsCode != null && flagsCode.IndexOf("pseudoloc", StringComparison.OrdinalIgnoreCase) >= 0)
                failures.Add("[store-quarantine] " + FeatureFlagsSrc + " declares 'pseudoloc' in CODE. " +
                             "The flag's reader belongs on PseudolocTextProvider.PrefKey, inside the " +
                             "guarded file; a FeatureFlags property is reachable from a store build.");
        }

        // -- 4 [transform] ---------------------------------------------------
        private static void CheckTransform(List<string> failures)
        {
            // Latin out, Cyrillic in.
            string built = PseudolocTextProvider.Transform("Build");
            if (HasLatinLetter(built))
                failures.Add("[transform] Transform(\"Build\") still contains a Latin letter ('" +
                             built + "') — the mapping is incomplete and every leak finding would be " +
                             "indistinguishable from an unmapped letter");
            if (!PseudolocLeakOracle.ContainsCyrillic(built))
                failures.Add("[transform] Transform(\"Build\") produced no Cyrillic ('" + built + "')");

            // Idempotent — the property that lets the decorator and the hook coexist without an
            // ordering rule. Asserted, not assumed.
            if (!string.Equals(PseudolocTextProvider.Transform(built), built, StringComparison.Ordinal))
                failures.Add("[transform] Transform is not idempotent: a second pass over '" + built +
                             "' changed it. The decorator and the post-resolve hook both apply it, so a " +
                             "non-idempotent transform would double-map whatever the provider resolved.");

            // Format placeholders — LocalText.FormatNamedArguments matches "{" + Name + "}"
            // literally, so a transformed placeholder NAME breaks substitution silently.
            const string pattern = "Level {0} of {Minimum} ready";
            string formatted = PseudolocTextProvider.Transform(pattern);
            if (formatted.IndexOf("{0}", StringComparison.Ordinal) < 0 ||
                formatted.IndexOf("{Minimum}", StringComparison.Ordinal) < 0)
                failures.Add("[transform] format placeholders were mangled: '" + pattern + "' -> '" +
                             formatted + "'. {0}/{Minimum} must survive byte-for-byte or every " +
                             "argument substitution in the game breaks and proves nothing about " +
                             "localization coverage.");
            // ⚠ THE ESCAPE PAIR IS PRESERVED; THE TEXT BETWEEN IT IS NOT — and that IS the
            // correct behaviour, not a near-miss. "{{" / "}}" are .NET's escaped literal
            // braces, so "{{literal}}" RENDERS as "{literal}": the word between them is real
            // player-facing copy and must be pseudolocalized like any other word, while the
            // escapes themselves must survive byte-for-byte or string.Format throws on the
            // pattern. Asserting the whole string unchanged (the obvious-looking assertion)
            // would have demanded the opposite and pinned a bug as the contract.
            const string escaped = "{{literal}}";
            string escapedOut = PseudolocTextProvider.Transform(escaped);
            if (!escapedOut.StartsWith("{{", StringComparison.Ordinal) ||
                !escapedOut.EndsWith("}}", StringComparison.Ordinal))
                failures.Add("[transform] the '{{' / '}}' escape pair was not preserved: '" +
                             escaped + "' -> '" + escapedOut + "'. string.Format would throw on " +
                             "the resulting pattern.");
            if (HasLatinLetter(escapedOut))
                failures.Add("[transform] the copy INSIDE an escaped brace pair was not " +
                             "transformed: '" + escaped + "' -> '" + escapedOut + "'. '{{x}}' " +
                             "renders as '{x}', so x is real copy and a leak would hide there.");

            // Rich text — a transformed tag name renders as literal angle-bracket garbage.
            const string rich = "<b><color=#FF0000>Danger</color></b>";
            string richOut = PseudolocTextProvider.Transform(rich);
            if (richOut.IndexOf("<b>", StringComparison.Ordinal) < 0 ||
                richOut.IndexOf("<color=#FF0000>", StringComparison.Ordinal) < 0 ||
                richOut.IndexOf("</color>", StringComparison.Ordinal) < 0)
                failures.Add("[transform] rich-text tags were mangled: '" + rich + "' -> '" + richOut +
                             "'. A transformed tag is not a tag; it renders as visible garbage on every " +
                             "styled label and buries the signal this harness exists to produce.");
            if (HasLatinLetter(PseudolocLeakOracle.StripRichText(richOut)))
                failures.Add("[transform] the BODY of '" + rich + "' was not transformed: '" + richOut + "'");

            // Digits and punctuation. ⚠ DELIBERATELY LETTER-FREE: an earlier draft of this
            // fixture ended "3.5s", and the trailing 's' IS a Latin letter, so the transform
            // correctly mapped it and the assertion would have failed on working code. A
            // "digits are preserved" fixture must contain no letters at all, or it is really
            // testing something else.
            const string numeric = "12,345 / 60 (+5%) — 3.5";
            if (!string.Equals(PseudolocTextProvider.Transform(numeric), numeric, StringComparison.Ordinal))
                failures.Add("[transform] digits/punctuation changed: '" + numeric + "' -> '" +
                             PseudolocTextProvider.Transform(numeric) + "'");

            // Empty / null must not throw.
            if (PseudolocTextProvider.Transform(null) != null ||
                PseudolocTextProvider.Transform(string.Empty) != string.Empty)
                failures.Add("[transform] null/empty input was not returned unchanged");

            // THE GLYPH-COVERAGE CLAIM, SELF-CHECKING. The provider's banner asserts every target
            // character appears in the live ru table (whose font coverage was fixed this session).
            // A claim in a comment rots; this measures it, so adding an unproven letter fails here
            // instead of shipping as tofu on a screenshot nobody can explain.
            string ru = ReadSrc(RuTableSrc, failures);
            if (ru != null)
            {
                var present = new HashSet<char>(ru.ToCharArray());
                var missing = new List<string>();
                for (char c = 'a'; c <= 'z'; c++)
                {
                    char lower = PseudolocTextProvider.MapChar(c);
                    char upper = PseudolocTextProvider.MapChar(char.ToUpperInvariant(c));
                    if (!present.Contains(lower))
                        missing.Add(c + "->U+" + ((int)lower).ToString("X4") + " (lower)");
                    if (!present.Contains(upper))
                        missing.Add(char.ToUpperInvariant(c) + "->U+" + ((int)upper).ToString("X4") + " (upper)");
                }
                if (missing.Count > 0)
                    failures.Add("[transform] " + missing.Count + " target character(s) do NOT appear in " +
                                 RuTableSrc + ", so their glyph coverage is UNPROVEN and the transform " +
                                 "could be the thing that tofus: " + string.Join(", ", missing.ToArray()) +
                                 ". Pick a target the ru table already uses.");
            }
        }

        // -- 5 [oracle-red] --------------------------------------------------
        //  ⛔ THE CHECK THAT MAKES EVERY OTHER NUMBER IN THIS HARNESS WORTH READING.
        //  Acceptance criteria 3 and 4 of WO-1861 are "the detector flags a deliberately
        //  reintroduced hardcoded string" and "the allowlist suppresses known-legitimate
        //  Latin WITHOUT suppressing a genuine leak planted in the same test". Doing that by
        //  hand means a seat remembering to plant a string, and CLAUDE.md §16 is explicit
        //  that a step whose remedy is "someone remembers" is not a gate. So the plant lives
        //  here, runs headless on every gate, and asserts EXACT counts — a >0 assertion would
        //  pass on an oracle that flagged everything.
        private static void CheckOracleGoesRed(List<string> failures, ref int findingCount, ref int labelCount)
        {
            GameObject root = null;
            try
            {
                var allow = PseudolocLeakOracle.Allowlist.Load();
                if (allow.Error != null)
                {
                    failures.Add("[oracle-red] cannot run the planted-leak proof: " + allow.Error);
                    return;
                }

                root = new GameObject("~PseudolocProbeCanvas");
                root.AddComponent<Canvas>();

                // (a) THE PLANTED LEAK. Three Latin words, none of them allowlisted.
                AddTmp(root, "LeakLabel", "Reforge the Heart");
                // (b) An allowlisted brand — must be suppressed, and must NOT take the leak with it.
                AddTmp(root, "BrandLabel", AllowlistedBrand);
                // (c) Already transformed, via the real transform. Proves the Cyrillic tally moves.
                AddUguiText(root, "TransformedLabel", PseudolocTextProvider.Transform("Manage"));
                // (d) Rich-text wrapper: "color"/"FF" must NOT be read as words.
                AddUguiText(root, "StyledLabel",
                    "<color=#FF0000>" + PseudolocTextProvider.Transform("Danger") + "</color>");
                // (e) Digits only.
                AddUguiText(root, "NumberLabel", "12 / 30 (+5%)");

                var scan = PseudolocLeakOracle.Scan(root, "PseudolocProbe_1920x1080", allow);
                findingCount = scan.Findings.Count;
                labelCount = scan.LabelsScanned;

                if (scan.LabelsScanned != 5)
                    failures.Add("[oracle-red] scanned " + scan.LabelsScanned + " label(s), expected 5 — " +
                                 "the walker is missing a component family (TMP_Text vs uGUI Text) or a " +
                                 "visibility predicate is excluding a live label");

                if (scan.Findings.Count != 3)
                {
                    var names = new List<string>();
                    for (int i = 0; i < scan.Findings.Count; i++)
                        names.Add(scan.Findings[i].Path + ":" + scan.Findings[i].Offending);
                    failures.Add("[oracle-red] expected EXACTLY 3 findings from the planted label " +
                                 "(\"Reforge the Heart\"), got " + scan.Findings.Count + ": [" +
                                 string.Join(", ", names.ToArray()) + "]. Too few means a real leak would " +
                                 "go unreported; too many means rich text or an allowlisted brand is being " +
                                 "flagged and the first live run would be unreadable.");
                }
                else
                {
                    for (int i = 0; i < scan.Findings.Count; i++)
                        if (scan.Findings[i].Path.IndexOf("LeakLabel", StringComparison.Ordinal) < 0)
                            failures.Add("[oracle-red] finding attributed to '" + scan.Findings[i].Path +
                                         "' instead of the planted LeakLabel — source attribution is the " +
                                         "whole value of this oracle over an eyes-on pass");
                }

                if (scan.Suppressed != 1)
                    failures.Add("[oracle-red] allowlist suppressed " + scan.Suppressed + " run(s), " +
                                 "expected exactly 1 (the '" + AllowlistedBrand + "' brand token). " +
                                 "0 means the allowlist is not consulted and every brand name will read " +
                                 "as a leak; >1 means an entry is broad enough to be hiding real copy.");

                if (scan.LabelsWithCyrillic != 2)
                    failures.Add("[oracle-red] " + scan.LabelsWithCyrillic + " label(s) counted as " +
                                 "transformed, expected 2 — the transform-did-not-take detector in " +
                                 "UICaptureLaunch.ReportPseudolocOracle depends on this tally being right");
            }
            catch (Exception ex)
            {
                failures.Add("[oracle-red] threw: " + ex.GetType().Name + ": " + ex.Message);
            }
            finally
            {
                if (root != null) UnityEngine.Object.DestroyImmediate(root);
            }
        }

        private static void AddTmp(GameObject parent, string name, string text)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent.transform, false);
            var label = go.AddComponent<TextMeshProUGUI>();
            label.text = text;
            label.color = UnityEngine.Color.white;
        }

        private static void AddUguiText(GameObject parent, string name, string text)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent.transform, false);
            var label = go.AddComponent<Text>();
            label.text = text;
            label.color = UnityEngine.Color.white;
        }

        // -- 6 [allowlist] ---------------------------------------------------
        private static void CheckAllowlist(List<string> failures)
        {
            var allow = PseudolocLeakOracle.Allowlist.Load();
            if (allow.Error != null)
            {
                failures.Add("[allowlist] " + allow.Error);
                return;
            }
            bool brandFound = false;
            foreach (var entry in allow.Entries)
            {
                if (string.IsNullOrEmpty(entry.Reason))
                    failures.Add("[allowlist] entry at line " + entry.Line + " ('" + entry.Value +
                                 "') has no reason — an entry with no reason is how a real leak gets " +
                                 "parked in the dictionary");
                if (entry.Kind == PseudolocLeakOracle.Allowlist.Kind.Token &&
                    string.Equals(entry.Value, AllowlistedBrand, StringComparison.OrdinalIgnoreCase))
                    brandFound = true;
            }
            if (!brandFound)
                failures.Add("[allowlist] the '" + AllowlistedBrand + "' token entry is gone from " +
                             PseudolocLeakOracle.AllowlistPath + ". Check 5 uses it to prove suppression " +
                             "works; without it that assertion silently stops testing anything.");
        }

        // -- 7 [capture-wiring] ----------------------------------------------
        private static void CheckCaptureWiring(List<string> failures)
        {
            string raw = ReadSrc(CaptureSrc, failures);
            if (raw == null) return;
            string code = CodeText(raw);

            if (code.IndexOf("AuditPseudolocLeaks(canvasGo", StringComparison.Ordinal) < 0)
                failures.Add("[capture-wiring] " + CaptureSrc + " no longer calls " +
                             "AuditPseudolocLeaks(canvasGo, ...) — RenderCanvasToPng is the ONE point " +
                             "every captured panel passes through, and unwiring it there makes whole " +
                             "screen families structurally invisible to the leak rule (the WO-1645 hole)");
            if (code.IndexOf("RunPseudolocCaptureHeadless", StringComparison.Ordinal) < 0)
                failures.Add("[capture-wiring] " + CaptureSrc + " no longer exposes " +
                             "RunPseudolocCaptureHeadless — the overnight loop has no entry point");
            if (code.IndexOf("ReportPseudolocOracle", StringComparison.Ordinal) < 0)
                failures.Add("[capture-wiring] " + CaptureSrc + " no longer calls ReportPseudolocOracle " +
                             "— the run would scan and never publish a verdict, and marker-absent on a " +
                             "fresh log is read as a FAILURE, not an unknown");

            // Both verdict markers must exist as literals. Read from the RAW text on purpose: they
            // ARE string literals, which CodeText blanks.
            foreach (string marker in new[] { "UI_PSEUDOLOC_OK", "UI_PSEUDOLOC_FAIL", "UI_PSEUDOLOC_INACTIVE" })
                if (raw.IndexOf("\"" + marker, StringComparison.Ordinal) < 0)
                    failures.Add("[capture-wiring] " + CaptureSrc + " no longer emits the '" + marker +
                                 "' marker literal");

            // The oracle must NOT be folded into the capture gate. §5 of the touch oracle and the
            // WO-1648 banner both record what happens then: everything reds and the gate is
            // suppressed instead of fixed. A pseudoloc run legitimately moves the layout markers.
            if (code.IndexOf("_pseudolocFindings.Count == 0 &&", StringComparison.Ordinal) >= 0)
                failures.Add("[capture-wiring] the pseudoloc finding count has been wired into a " +
                             "capture-gate verdict in " + CaptureSrc + ". It must carry its OWN marker: " +
                             "the scan is inert unless pseudoloc is active, so folding it in makes the " +
                             "normal nightly capture's verdict depend on a mode it never runs in.");
        }

        // -- 8 [hygiene] ---------------------------------------------------
        private static void CheckHygiene(List<string> failures)
        {
            foreach (string path in new[] { ProviderSrc, LocalTextSrc, OracleSrc, CaptureSrc,
                                            PseudolocLeakOracle.AllowlistPath })
            {
                try
                {
                    if (!File.Exists(path)) { failures.Add("[hygiene] missing " + path); continue; }
                    byte[] bytes = File.ReadAllBytes(path);
                    for (int i = 0; i < bytes.Length; i++)
                        if (bytes[i] == 0) { failures.Add("[hygiene] embedded NUL in " + path); break; }
                }
                catch (Exception ex) { failures.Add("[hygiene] " + path + ": " + ex.Message); }
            }
        }

        // -- helpers --------------------------------------------------------
        private static bool HasLatinLetter(string s)
        {
            if (string.IsNullOrEmpty(s)) return false;
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if ((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z')) return true;
            }
            return false;
        }

        private static int CountOccurrences(string haystack, string needle)
        {
            int n = 0, i = 0;
            while ((i = haystack.IndexOf(needle, i, StringComparison.Ordinal)) >= 0) { n++; i += needle.Length; }
            return n;
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

        private static string ReadCode(string path, List<string> failures)
        {
            string raw = ReadSrc(path, failures);
            return raw == null ? null : CodeText(raw);
        }

        /// <summary>ReadSrc reduced to CODE ONLY (comment lines dropped, string literal CONTENTS
        /// blanked) — same idiom as DeviceScenarioKitRegression.CodeText, kept file-local per this
        /// folder's convention.</summary>
        private static string CodeText(string source)
        {
            if (string.IsNullOrEmpty(source)) return string.Empty;
            var sb = new StringBuilder(source.Length);
            foreach (string rawLine in source.Split('\n'))
            {
                string t = rawLine.TrimStart();
                if (t.StartsWith("//", StringComparison.Ordinal) ||
                    t.StartsWith("*", StringComparison.Ordinal) ||
                    t.StartsWith("/*", StringComparison.Ordinal)) { sb.Append('\n'); continue; }
                sb.Append(StripStringLiterals(rawLine)).Append('\n');
            }
            return sb.ToString();
        }

        private static string StripStringLiterals(string line)
        {
            var sb = new StringBuilder(line.Length);
            bool inStr = false;
            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (!inStr && c == '/' && i + 1 < line.Length && line[i + 1] == '/') break;
                if (c == '"' && (i == 0 || line[i - 1] != '\\')) { inStr = !inStr; sb.Append(c); continue; }
                sb.Append(inStr ? ' ' : c);
            }
            return sb.ToString();
        }
    }
}

// =============================================================================
// PseudolocLeakOracle — WO-1861 Part B. The cheap, deterministic, FREE leak detector.
// -----------------------------------------------------------------------------
// Assembly: DeNelle.EditorRegression. Namespace: DeNelle.Editor.Regression.
//
// WHAT IT DOES
//   With pseudolocalization active (PseudolocTextProvider.Active), every string that
//   came through LocalText is Cyrillic. So: walk every TMP_Text / uGUI Text on a
//   captured canvas, strip rich-text tags, and flag every maximal run of two or more
//   consecutive Latin letters that the allowlist does not explain. Each finding
//   carries the SCREEN, the full HIERARCHY PATH, the offending run and the label's
//   whole text -- exact source attribution, no vision model, no cost, repeatable.
//
// =============================================================================
//  ⛔ WHY THIS LIVES HERE AND NOT INSIDE UICaptureLaunch.cs. READ ONCE.
// -----------------------------------------------------------------------------
//  UICaptureLaunch.cs:6490-6502 records, in its own words, why three layout rules
//  "used to live here, AND THAT IS WHY NOBODY HAD EVER SEEN THEM GO RED": they ran
//  only on the headless screenshot path, and nothing could reach them from a
//  regression suite, because DeNelle.Editor references DeNelle.EditorRegression and
//  the reverse reference would be a cycle. The fix was to move the rules to where
//  BOTH callers share one implementation.
//
//  This oracle is built that way from its first line. Two callers, one rule:
//     UICaptureLaunch.AuditPseudolocLeaks  -> every captured panel, three aspects
//     PseudolocHarnessRegression           -> a synthetic canvas with a PLANTED leak,
//                                             an allowlisted token and a transformed
//                                             label, asserting the exact counts
//  So the ticket's acceptance criteria 3 and 4 ("flags a deliberately-reintroduced
//  hardcoded string"; "the allowlist suppresses known-legitimate Latin WITHOUT
//  suppressing a genuine leak planted in the same test") are proved by the headless
//  regression run, not by a human remembering to plant a string by hand.
//
//  It is NOT in DeNelle.Core: Core ships, and a leak scanner is QA tooling with no
//  runtime caller. DeNelle.EditorRegression is Editor-only (includePlatforms:
//  ["Editor"]) and already references Unity.TextMeshPro + UnityEngine.UI, so the
//  scan needs no new assembly reference.
//
// =============================================================================
//  THE ALLOWLIST FAILS CLOSED, ON PURPOSE.
// -----------------------------------------------------------------------------
//  A missing or unparseable docs/localization/PSEUDOLOC_ALLOWLIST.md returns an
//  allowlist in a FAULTED state whose Error is non-null. Callers must surface that
//  as a FAILURE. An allowlist that failed OPEN would suppress every finding and
//  report a clean sweep over a game nobody checked -- the exact class of lie
//  CLAUDE.md sec.8 is about ("marker absence on a fresh log is a FAILURE, not an
//  unknown"). An allowlist that failed CLOSED-but-empty would flag every brand name
//  on every screen and be switched off within the hour. Neither: it reports the
//  parse error by name and the caller refuses to render a verdict.
// =============================================================================
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DeNelle.Editor.Regression
{
    public static class PseudolocLeakOracle
    {
        /// <summary>The one authored path. Named ONCE, here.</summary>
        public const string AllowlistPath = "docs/localization/PSEUDOLOC_ALLOWLIST.md";

        /// <summary>Minimum consecutive Latin letters that counts as a word. One stray letter is
        /// noise (an initial, a units suffix, a slash-joined glyph); two is a word.</summary>
        public const int MinRunLength = 2;

        /// <summary>Label alpha at or below which a label is invisible and not judged. Same
        /// threshold AuditGeometry uses for its own text rules, so the two oracles never disagree
        /// about whether a label was on screen.</summary>
        public const float MinVisibleAlpha = 0.05f;

        // -----------------------------------------------------------------
        //  Findings
        // -----------------------------------------------------------------
        public struct Finding
        {
            /// <summary>Panel-build label, e.g. "ManageWorkspace_2670x1200" -- the SAME string
            /// AuditGeometry uses, so a pseudoloc finding and a glyph finding on the same panel
            /// are trivially cross-referenced.</summary>
            public string Screen;
            /// <summary>Hierarchy path from the canvas root to the label.</summary>
            public string Path;
            /// <summary>The offending Latin run.</summary>
            public string Offending;
            /// <summary>The label's whole plain text (tags stripped), for context.</summary>
            public string FullText;
            /// <summary>Component type name -- TextMeshProUGUI, Text, ...</summary>
            public string Component;

            public string Message
            {
                get
                {
                    return "ENGLISH SURVIVED PSEUDOLOC [" + Screen + "] '" + Path + "' (" +
                           Component + ") shows the Latin word \"" + Offending +
                           "\" in text \"" + Clip(FullText) + "\". Either the string never went " +
                           "through LocalText (a leak -> wire it to a locale key, WO-1857) or it " +
                           "is legitimately Latin in every language (-> add the narrowest entry " +
                           "to " + AllowlistPath + ", with the reason).";
                }
            }

            private static string Clip(string s)
            {
                if (string.IsNullOrEmpty(s)) return string.Empty;
                s = s.Replace('\n', ' ').Replace('\r', ' ');
                return s.Length <= 90 ? s : s.Substring(0, 87) + "...";
            }
        }

        /// <summary>One panel's scan result. Counts travel WITH the findings: zero findings from
        /// zero scanned labels is not a clean panel, it is a panel nothing looked at, and the two
        /// must never print the same line (the WO-1630 glyph-oracle lesson, applied here from the
        /// start rather than after a bad run).</summary>
        public struct ScanResult
        {
            public int LabelsScanned;
            public int LabelsWithCyrillic;
            public int Suppressed;
            public List<Finding> Findings;
        }

        // -----------------------------------------------------------------
        //  THE SCAN
        // -----------------------------------------------------------------
        /// <summary>
        /// Walk every visible label under <paramref name="root"/> and flag un-allowlisted Latin
        /// words. Never throws: a throw here would cost a capture run its screenshot, so the
        /// caller gets a result with the failure recorded as a finding instead.
        /// </summary>
        public static ScanResult Scan(GameObject root, string screen, Allowlist allow)
        {
            var result = new ScanResult { Findings = new List<Finding>() };
            if (root == null) return result;
            string safeScreen = screen ?? "<unnamed>";

            try
            {
                foreach (var label in root.GetComponentsInChildren<TMP_Text>(false))
                {
                    if (label == null || !label.enabled || !label.gameObject.activeInHierarchy) continue;
                    if (label.color.a <= MinVisibleAlpha) continue;
                    ScanOne(label.text, label.transform, root.transform, safeScreen,
                            label.GetType().Name, allow, ref result);
                }

                foreach (var label in root.GetComponentsInChildren<Text>(false))
                {
                    if (label == null || !label.enabled || !label.gameObject.activeInHierarchy) continue;
                    if (label.color.a <= MinVisibleAlpha) continue;
                    ScanOne(label.text, label.transform, root.transform, safeScreen,
                            label.GetType().Name, allow, ref result);
                }
            }
            catch (Exception ex)
            {
                result.Findings.Add(new Finding
                {
                    Screen = safeScreen,
                    Path = "<scan threw>",
                    Offending = ex.GetType().Name,
                    FullText = ex.Message,
                    Component = "PseudolocLeakOracle",
                });
            }
            return result;
        }

        private static void ScanOne(string raw, Transform label, Transform root, string screen,
                                    string component, Allowlist allow, ref ScanResult result)
        {
            if (string.IsNullOrEmpty(raw)) return;
            string plain = StripRichText(raw);
            if (string.IsNullOrEmpty(plain)) return;

            result.LabelsScanned++;
            if (ContainsCyrillic(plain)) result.LabelsWithCyrillic++;

            string path = PathOf(label, root);

            foreach (string run in LatinRuns(plain))
            {
                if (allow != null && allow.Suppresses(path, screen, plain, run))
                {
                    result.Suppressed++;
                    continue;
                }
                result.Findings.Add(new Finding
                {
                    Screen = screen,
                    Path = path,
                    Offending = run,
                    FullText = plain,
                    Component = component,
                });
            }
        }

        /// <summary>
        /// Maximal runs of <see cref="MinRunLength"/>+ consecutive ASCII Latin letters.
        /// Deliberately hand-written rather than a Regex: it runs over every label on every
        /// captured panel at three aspects, and the rule is three comparisons.
        /// </summary>
        public static List<string> LatinRuns(string text)
        {
            var runs = new List<string>();
            if (string.IsNullOrEmpty(text)) return runs;
            int start = -1;
            for (int i = 0; i <= text.Length; i++)
            {
                bool latin = i < text.Length && IsLatinLetter(text[i]);
                if (latin)
                {
                    if (start < 0) start = i;
                    continue;
                }
                if (start >= 0)
                {
                    int len = i - start;
                    if (len >= MinRunLength) runs.Add(text.Substring(start, len));
                    start = -1;
                }
            }
            return runs;
        }

        private static bool IsLatinLetter(char c)
        {
            return (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z');
        }

        public static bool ContainsCyrillic(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;
            for (int i = 0; i < text.Length; i++)
                if (text[i] >= 'Ѐ' && text[i] <= 'ӿ') return true;
            return false;
        }

        /// <summary>
        /// Drop <c>&lt;...&gt;</c> spans. MANDATORY, not tidiness: TMP markup carries Latin in
        /// every tag it has -- <c>&lt;color=#FF0000&gt;</c>, <c>&lt;b&gt;</c>,
        /// <c>&lt;sprite name="coin"&gt;</c> -- so without this, "color", "sprite", "coin" and "FF"
        /// would be reported as leaks on every styled label in the game and the report would be
        /// unreadable on its first run. Same idiom as LocaleSmokeCapture.cs:38's RichTextTag.
        /// </summary>
        public static string StripRichText(string text)
        {
            if (string.IsNullOrEmpty(text) || text.IndexOf('<') < 0) return text;
            var sb = new StringBuilder(text.Length);
            int i = 0;
            while (i < text.Length)
            {
                if (text[i] == '<')
                {
                    int close = text.IndexOf('>', i + 1);
                    if (close > i) { i = close + 1; continue; }
                }
                sb.Append(text[i]);
                i++;
            }
            return sb.ToString();
        }

        public static string PathOf(Transform t, Transform stopAt)
        {
            if (t == null) return "<null>";
            var parts = new List<string>();
            for (Transform cur = t; cur != null && cur != stopAt; cur = cur.parent)
                parts.Add(cur.name);
            parts.Reverse();
            return parts.Count == 0 ? t.name : string.Join("/", parts.ToArray());
        }

        // =================================================================
        //  THE ALLOWLIST
        // =================================================================
        public sealed class Allowlist
        {
            public enum Kind { Token, Phrase, Path, Screen }

            public struct Entry
            {
                public Kind Kind;
                public string Value;
                public string Reason;
                public int Line;
            }

            private readonly List<Entry> _entries = new List<Entry>();

            /// <summary>Non-null when the file was missing or an entry was malformed. A caller
            /// MUST refuse to publish a verdict while this is set.</summary>
            public string Error { get; private set; }

            public IReadOnlyList<Entry> Entries => _entries;
            public int Count => _entries.Count;

            /// <summary>Loads and parses the authored allowlist. Fails CLOSED and by name.</summary>
            public static Allowlist Load(string path = AllowlistPath)
            {
                var list = new Allowlist();
                string full = path;
                try
                {
                    if (!File.Exists(full))
                    {
                        list.Error = "allowlist file not found at '" + full + "'. The oracle cannot " +
                                     "distinguish a brand name from a leak without it, and an absent " +
                                     "allowlist must never read as 'nothing to suppress'.";
                        return list;
                    }

                    var problems = new List<string>();
                    string[] lines = File.ReadAllText(full).Replace("\r\n", "\n").Split('\n');
                    bool inBlock = false;
                    for (int i = 0; i < lines.Length; i++)
                    {
                        string line = lines[i];
                        string trimmed = line.Trim();

                        if (trimmed.StartsWith("```", StringComparison.Ordinal))
                        {
                            // Only blocks tagged exactly `allowlist` are data. Every other fenced
                            // block in the doc (the grammar example is tagged `text`) is prose, so
                            // the example row can never be parsed as a live entry.
                            string tag = trimmed.Substring(3).Trim();
                            inBlock = !inBlock && string.Equals(tag, "allowlist", StringComparison.Ordinal);
                            continue;
                        }
                        if (!inBlock) continue;
                        if (trimmed.Length == 0 || trimmed[0] == '#') continue;

                        int hash = trimmed.IndexOf('#');
                        if (hash < 0)
                        {
                            problems.Add("line " + (i + 1) + ": '" + trimmed + "' has no '# reason'. " +
                                         "Every entry must state WHY the string is legitimately Latin " +
                                         "in every language; an entry with no reason is how a real leak " +
                                         "gets parked here.");
                            continue;
                        }
                        string reason = trimmed.Substring(hash + 1).Trim();
                        string head = trimmed.Substring(0, hash).Trim();
                        if (reason.Length == 0)
                        {
                            problems.Add("line " + (i + 1) + ": empty reason for '" + head + "'.");
                            continue;
                        }

                        int colon = head.IndexOf(':');
                        if (colon <= 0)
                        {
                            problems.Add("line " + (i + 1) + ": '" + head + "' is not '<kind>: <value>'.");
                            continue;
                        }
                        string kindText = head.Substring(0, colon).Trim().ToLowerInvariant();
                        string value = head.Substring(colon + 1).Trim();
                        if (value.Length == 0)
                        {
                            problems.Add("line " + (i + 1) + ": empty value for kind '" + kindText + "'.");
                            continue;
                        }

                        Kind kind;
                        switch (kindText)
                        {
                            case "token": kind = Kind.Token; break;
                            case "phrase": kind = Kind.Phrase; break;
                            case "path": kind = Kind.Path; break;
                            case "screen": kind = Kind.Screen; break;
                            default:
                                problems.Add("line " + (i + 1) + ": unknown kind '" + kindText +
                                             "' (token|phrase|path|screen).");
                                continue;
                        }

                        if (kind == Kind.Token && HasWhitespace(value))
                        {
                            problems.Add("line " + (i + 1) + ": token '" + value + "' contains whitespace. " +
                                         "A multi-word token would suppress each of its words " +
                                         "everywhere -- 'The Night Market' would allowlist 'The' on " +
                                         "every screen in the game. Use 'phrase:' instead.");
                            continue;
                        }

                        list._entries.Add(new Entry
                        {
                            Kind = kind, Value = value, Reason = reason, Line = i + 1,
                        });
                    }

                    if (inBlock)
                        problems.Add("an `allowlist` fenced block is never closed -- the parse cannot " +
                                     "be trusted to have seen the whole file.");
                    if (list._entries.Count == 0)
                        problems.Add("zero entries parsed. The file exists but yields nothing, which " +
                                     "is indistinguishable at the marker from a healthy run over a game " +
                                     "with no brand names. Treated as a parse failure.");

                    if (problems.Count > 0)
                        list.Error = problems.Count + " allowlist problem(s) in " + full + ": " +
                                     string.Join(" | ", problems.ToArray());
                }
                catch (Exception ex)
                {
                    list.Error = "allowlist read threw: " + ex.GetType().Name + ": " + ex.Message;
                }
                return list;
            }

            private static bool HasWhitespace(string s)
            {
                for (int i = 0; i < s.Length; i++) if (char.IsWhiteSpace(s[i])) return true;
                return false;
            }

            /// <summary>
            /// True when this flagged run is explained by an authored entry. A FAULTED allowlist
            /// suppresses NOTHING -- it must not be able to hide findings while its own error is
            /// the thing the caller is about to report.
            /// </summary>
            public bool Suppresses(string path, string screen, string plainText, string run)
            {
                if (Error != null) return false;
                for (int i = 0; i < _entries.Count; i++)
                {
                    var e = _entries[i];
                    switch (e.Kind)
                    {
                        case Kind.Token:
                            if (string.Equals(run, e.Value, StringComparison.OrdinalIgnoreCase)) return true;
                            break;
                        case Kind.Phrase:
                            if (!string.IsNullOrEmpty(plainText) &&
                                plainText.IndexOf(e.Value, StringComparison.OrdinalIgnoreCase) >= 0) return true;
                            break;
                        case Kind.Path:
                            if (!string.IsNullOrEmpty(path) &&
                                path.IndexOf(e.Value, StringComparison.OrdinalIgnoreCase) >= 0) return true;
                            break;
                        case Kind.Screen:
                            if (!string.IsNullOrEmpty(screen) &&
                                screen.IndexOf(e.Value, StringComparison.OrdinalIgnoreCase) >= 0) return true;
                            break;
                    }
                }
                return false;
            }
        }
    }
}

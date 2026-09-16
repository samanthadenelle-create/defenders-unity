// =============================================================================
// RetiredVocabularyRegression [retired-vocabulary]
// Player-visible vocabulary only. Frozen persistence/wire identifiers are excluded.
// DataRegression.cs registration is committer-fenced; the lead adds the one-line call.
// =============================================================================
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace DeNelle.Editor.Regression
{
    public static class RetiredVocabularyRegression
    {
        private const string Canon = "Assets/Resources/Data/Canonical/retired-vocabulary.json";
        private const string Mirror = "Assets/StreamingAssets/Data/Canonical/retired-vocabulary.json";
        private static readonly HashSet<string> VisibleJsonFields = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "displayName", "name", "title", "label", "description", "objectiveText", "tagline",
            "message", "copy", "buttonText", "emptyText", "statusText", "toast"
        };
        // The owner authorized retiring Food throughout the game on 2026-09-11.
        // All previously tolerated copy has been corrected; retain zero exceptions.
        private static readonly Dictionary<string, int> KnownCopyDebt2026_08_25 = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
        };

        // Populated per run so the SUCCESS string can name the debt it tolerated - a green
        // that silently skips things is the hollow-pass class this repo hunts.
        private static readonly Dictionary<string, int> debtHits = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        private static readonly Regex FrozenSourceSyntax = new Regex(
            "(?i)(JsonProperty\\s*\\(|\\bconst\\s+string\\b|\\bcase\\s+\\\"|FlowTrace\\.|Debug\\.|PlayerPrefs|Regex\\.)",
            RegexOptions.Compiled);
        private static readonly Regex VisibleSourceHint = new Regex(
            @"(?i)(label|text|toast|popup|display|option|entry|title|message|resourcegain|AddCurrencyRow|SetReadout|Harvest/|\bPop\s*\()",
            RegexOptions.Compiled);

        public static void RunAll()
        {
            if (Run(out string reason)) Debug.Log("RETIRED_VOCABULARY_OK - " + reason);
            else Debug.LogError("RETIRED_VOCABULARY_FAIL: " + reason);
        }

        public static bool Run(out string reason)
        {
            var failures = new List<string>();
            debtHits.Clear();        // per-run; the success string reports what was tolerated
            var selfTest = new List<string>();
            try
            {
                // STOP: THE GOOD PATH IS ASSERTED FIRST, BEFORE ANY REAL FILE IS OPENED.
                // A failure-only oracle is not acceptance: a detector that has silently stopped
                // matching reports the same clean green as a clean tree, and this repo has already
                // shipped a guard that aborted every good run while exiting 0. SelfTest drives the
                // SAME predicates over synthetic copy that MUST be caught and synthetic wire
                // vocabulary that MUST NOT be, and fails loudly either way round. It runs on every
                // execution, so the red proof is standing rather than a one-off someone did once.
                SelfTest(failures, selfTest);

                if (!File.Exists(Canon) || !File.Exists(Mirror))
                    failures.Add("[data] canonical retirement list or StreamingAssets mirror is missing");
                else if (!string.Equals(File.ReadAllText(Canon), File.ReadAllText(Mirror), StringComparison.Ordinal))
                    failures.Add("[data] retired-vocabulary canonical mirrors differ byte-for-byte");
                else
                {
                    var root = JObject.Parse(File.ReadAllText(Canon));
                    if ((root["version"]?.Value<int>() ?? 0) < 1)
                        failures.Add("[data] version must be >= 1");
                    var rows = root["retirements"] as JArray;
                    if (rows == null || rows.Count == 0) failures.Add("[data] no retirement rows authored");
                    else foreach (var token in rows)
                    {
                        var row = token as JObject;
                        string retired = row?["retired"]?.Value<string>()?.Trim();
                        string replacement = row?["replacement"]?.Value<string>()?.Trim();
                        string ticket = row?["ticket"]?.Value<string>()?.Trim();
                        string date = row?["date"]?.Value<string>()?.Trim();
                        if (string.IsNullOrEmpty(retired) || string.IsNullOrEmpty(replacement) ||
                            string.IsNullOrEmpty(ticket) || string.IsNullOrEmpty(date))
                        {
                            failures.Add("[data] every row requires retired/replacement/date/ticket");
                            continue;
                        }
                        ScanCanonicalJson(retired, replacement, failures);
                        ScanPlayerSource(retired, replacement, failures);
                    }
                    AssertDebtLedger(failures);
                }
            }
            catch (Exception ex) { failures.Add("[suite] THREW " + ex.GetType().Name + ": " + ex.Message); }

            if (failures.Count == 0)
            {
                reason = "player-visible strings, picker declarations and resource-art routes contain no retired vocabulary"
                         + " [" + string.Join("; ", selfTest) + "]"
                         + (debtHits.Count > 0
                            ? " [TOLERATED known copy debt (dated 2026-08-25, owner copy owed): "
                              + DebtSummary() + "]"
                            : " [no tolerated debt - the dated baseline is now EMPTY and its list should be deleted]");
                return true;
            }
            reason = "retired-vocabulary FAIL x" + failures.Count + ": " + string.Join(" | ", failures);
            return false;
        }

        private static void ScanCanonicalJson(string retired, string replacement, List<string> failures)
        {
            foreach (string file in Directory.GetFiles("Assets/Resources/Data/Canonical", "*.json", SearchOption.AllDirectories))
            {
                if (file.Replace('\\', '/').EndsWith("/retired-vocabulary.json", StringComparison.OrdinalIgnoreCase)) continue;
                // DescendantsAndSelf() lives on JContainer, not JToken - the JToken overload is an
                // extension constrained to `T : JContainer`, so parsing into a bare JToken does not
                // compile (CS0311). Fixed by the committer, 2026-08-25.
                // A document whose root is a bare scalar has no descendants to walk, so it is
                // skipped rather than treated as an error: `null` here means "nothing to inspect",
                // never "inspection failed".
                // STOP: NOT A SILENT `continue`. The previous version swallowed both an unparseable
                // document and a non-container root with the note "syntax belongs to the canonical
                // data gate" - but the consequence here is that THIS suite certified a file it
                // never read, and reported the same green as if it had. That is the hollow-pass
                // shape: a missing FIXTURE fails naming the path, it does not quietly pass.
                JContainer root = null;
                string parseError = null;
                try { root = JToken.Parse(File.ReadAllText(file)) as JContainer; }
                catch (Exception ex) { parseError = ex.GetType().Name + ": " + ex.Message; }
                if (parseError != null)
                {
                    failures.Add("[unreadable] " + file.Replace('\\', '/') +
                                 " could not be parsed, so it was NEVER SCANNED for retired vocabulary (" +
                                 parseError + "). Fix the file or the canonical-data gate; do not skip it.");
                    continue;
                }
                if (root == null)
                {
                    failures.Add("[unreadable] " + file.Replace('\\', '/') +
                                 " has a bare scalar root, so DescendantsAndSelf walks nothing and the file " +
                                 "was NEVER SCANNED. A scalar root can still be player copy - give it a container.");
                    continue;
                }
                ScanJsonDocument(file, root, retired, replacement, failures);
            }
        }

        /// <summary>One parsed document, scanned. Split out so <see cref="SelfTest"/> can drive the
        /// exact same predicates over synthetic copy instead of asserting a second implementation.</summary>
        private static void ScanJsonDocument(string file, JContainer root, string retired, string replacement,
                                             List<string> failures)
        {
            foreach (var value in root.DescendantsAndSelf())
            {
                if (!(value is JValue scalar) || scalar.Type != JTokenType.String) continue;
                var prop = value.Parent as JProperty;
                if (!IsVisibleJsonValue(file, value, prop)) continue;
                string text = scalar.Value<string>() ?? "";
                if (!WholeWord(text, retired)) continue;
                // Known copy debt is RECORDED, not silently passed: it still shows in the
                // success string, so "green" never means "clean". Anything not on the dated
                // list fails, which is what makes this a ratchet rather than a pardon.
                string surfaceKey = file.Replace('\\', '/') + ":" + SurfaceName(value, prop);
                if (TolerateKnownDebt(surfaceKey)) continue;
                failures.Add($"[catalog-copy] {file.Replace('\\', '/')}:{LineLabel(file, text)} field '{SurfaceName(value, prop)}' exposes retired '{retired}' (use '{replacement}')");
            }
        }

        /// <summary>Spends one unit of a baselined surface's authored allowance. Returns false once
        /// the allowance is exhausted, so the (count+1)th leak in a forgiven surface still FAILS.</summary>
        private static bool TolerateKnownDebt(string surfaceKey)
        {
            if (!KnownCopyDebt2026_08_25.TryGetValue(surfaceKey, out int allowed)) return false;
            debtHits.TryGetValue(surfaceKey, out int seen);
            if (seen >= allowed) return false;
            debtHits[surfaceKey] = seen + 1;
            return true;
        }

        /// <summary>STOP: The ledger may only ever SHRINK, and this is what enforces it. A row that has
        /// stopped matching means the copy was authored - delete the row - and a row matching fewer
        /// than it tolerates means the debt shrank. Either way an unmaintained ledger goes RED
        /// instead of quietly growing into a permanent pardon.</summary>
        private static void AssertDebtLedger(List<string> failures)
        {
            foreach (var row in KnownCopyDebt2026_08_25)
            {
                debtHits.TryGetValue(row.Key, out int seen);
                if (seen == row.Value) continue;
                if (seen == 0)
                    failures.Add("[baseline-stale] '" + row.Key + "' tolerates " + row.Value +
                                 " retired-word hit(s) and now matches NONE -- the copy was fixed. " +
                                 "DELETE the row; this ledger may only ever shrink.");
                else
                    failures.Add("[baseline-drift] '" + row.Key + "' tolerates " + row.Value +
                                 " retired-word hit(s) but only " + seen + " remain -- lower the count to " +
                                 seen + " so the ratchet cannot slip back.");
            }
        }

        private static string DebtSummary()
        {
            var parts = new List<string>();
            foreach (var kv in debtHits) parts.Add(kv.Key + " x" + kv.Value);
            parts.Sort(StringComparer.Ordinal);
            return string.Join("; ", parts);
        }

        private static void ScanPlayerSource(string retired, string replacement, List<string> failures)
        {
            foreach (string file in Directory.GetFiles("Assets/_Modules", "*.cs", SearchOption.AllDirectories))
            {
                string normalizedFile = file.Replace('\\', '/');
                if (normalizedFile.IndexOf("/Generated/", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                string source = StripComments(File.ReadAllText(file));
                ScanSourceLines(normalizedFile, source.Replace("\r\n", "\n").Split('\n'),
                                retired, replacement, failures);
            }
        }

        /// <summary>One file's already-comment-stripped lines. Split out for the same reason as
        /// <see cref="ScanJsonDocument"/>: the self-test must exercise THIS predicate, not a copy.</summary>
        private static void ScanSourceLines(string file, string[] lines, string retired, string replacement,
                                            List<string> failures)
        {
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];
                // A variable/parameter named `food` is frozen implementation vocabulary, not
                // player copy. Require the retired word inside a source string literal, then
                // exclude the explicit serialization/diagnostic syntaxes that own wire words.
                bool word = QuotedWholeWord(line, retired);
                bool visibleContext = VisibleSourceHint.IsMatch(line) || RecentVisibleDeclaration(lines, i);
                if (!word || !visibleContext || FrozenSourceSyntax.IsMatch(line)) continue;
                failures.Add($"[source-surface] {file.Replace('\\', '/')}:{i + 1} exposes retired '{retired}' (use '{replacement}')");
            }
        }

        // =====================================================================================
        //  THE STANDING RED PROOF (WO-1206 acceptance 4, WO-1138's rule).
        // -------------------------------------------------------------------------------------
        //  "Prove it RED before green - reintroduce a retired word on a display surface, watch
        //  the suite name it, then remove it." Done ONCE by hand, that proof expires the moment
        //  someone edits a predicate. So it is done on EVERY run instead, against synthetic
        //  documents and synthetic source lines driven through the SAME ScanJsonDocument /
        //  ScanSourceLines the real sweep uses - never a second implementation, which would only
        //  prove the copy agrees with itself.
        //
        //  Both directions are asserted, because both failures are silent:
        //    * a RED case that produces NOTHING means the detector has gone blind and every
        //      green after that is meaningless;
        //    * a GREEN case that produces a finding means the detector cannot tell display copy
        //      from the frozen wire vocabulary WO-1163 kept on purpose - and an oracle that
        //      flags EconomyService.Food gets switched off within a week, which the ticket
        //      names as worse than no oracle at all.
        // =====================================================================================
        private const string SelfRetired = "food";
        private const string SelfReplacement = "stone";

        private static void SelfTest(List<string> failures, List<string> notes)
        {
            int red = 0, green = 0;

            // ---- JSON half: copy that MUST be caught -----------------------------------------
            red += ExpectJson(failures, "whitelisted display field", 1,
                "SelfTest/synthetic-catalog.json", "{\"displayName\":\"Food Depot\"}");
            red += ExpectJson(failures, "copy held in a tips[] array", 1,
                "SelfTest/synthetic-guide.json", "{\"tips\":[\"Food feeds crafting.\"]}");
            red += ExpectJson(failures, "dotted locale key in en.json", 1,
                "SelfTest/en.json", "{\"tooltip.resource.detail\":\"Food\"}");

            // ---- JSON half: vocabulary that MUST NOT be caught --------------------------------
            green += ExpectJson(failures, "frozen wire keys and slot names", 0,
                "SelfTest/synthetic-catalog.json", "{\"id\":\"food_store\",\"paidFood\":7,\"resourceKey\":\"food\"}");
            green += ExpectJson(failures, "already-converted copy", 0,
                "SelfTest/synthetic-catalog.json", "{\"displayName\":\"Stone Depot\",\"description\":\"Grinds grain into Stone.\"}");
            green += ExpectJson(failures, "retired word as a substring", 0,
                "SelfTest/synthetic-catalog.json", "{\"displayName\":\"Seafood Platter\"}");

            // ---- Source half: display surfaces that MUST be caught ----------------------------
            red += ExpectSource(failures, "assignment to a label", 1, "hud.label = \"Food\";");
            red += ExpectSource(failures, "tester currency row", 1, "AddCurrencyRow(\"Food\", Currency.Stone);");
            red += ExpectSource(failures, "tester wallet readout", 1, "SetReadout($\"Food {economy.Stone}\");");
            red += ExpectSource(failures, "toast copy", 1, "ShowToast(\"Food is running low\");");

            // ---- Source half: frozen syntax that MUST NOT be caught ---------------------------
            green += ExpectSource(failures, "JsonProperty wire name", 0,
                "[JsonProperty(\"food\")] public int labelValue;");
            green += ExpectSource(failures, "const wire key", 0,
                "const string FoodLabel = \"food\";");
            green += ExpectSource(failures, "switch case on a wire token", 0,
                "case \"food\": return DisplayLabel();");
            green += ExpectSource(failures, "PlayerPrefs key", 0,
                "PlayerPrefs.SetInt(\"food\", labelCount);");
            green += ExpectSource(failures, "diagnostic trace", 0,
                "FlowTrace.Step(\"Harvest\", \"food label\");");
            green += ExpectSource(failures, "retired word as a substring", 0,
                "string label = \"seafood stew\";");
            green += ExpectSource(failures, "unquoted identifier", 0,
                "int food = 3; var labelText = Compose(food);");

            notes.Add("self-test " + red + " RED case(s) named the leak and " + green +
                      " GREEN case(s) stayed silent -- the detector still sees display copy and " +
                      "still ignores the frozen wire vocabulary");
        }

        /// <summary>Drives one synthetic document through the REAL json scan. Returns 1 when the
        /// case behaved, 0 when it did not (and appends the failure).</summary>
        private static int ExpectJson(List<string> failures, string what, int expected, string file, string json)
        {
            var found = new List<string>();
            try
            {
                var root = JToken.Parse(json) as JContainer;
                if (root == null)
                {
                    failures.Add("[self-test] the '" + what + "' fixture is not a container -- the case proves nothing.");
                    return 0;
                }
                ScanJsonDocument(file, root, SelfRetired, SelfReplacement, found);
            }
            catch (Exception ex)
            {
                failures.Add("[self-test] the '" + what + "' json case THREW " + ex.GetType().Name + ": " + ex.Message);
                return 0;
            }
            return Judge(failures, "json", what, expected, found);
        }

        /// <summary>Drives one synthetic source line through the REAL source scan, comment-stripped
        /// exactly as a file on disk would be.</summary>
        private static int ExpectSource(List<string> failures, string what, int expected, string line)
        {
            var found = new List<string>();
            try
            {
                ScanSourceLines("SelfTest/Synthetic.cs",
                                StripComments(line).Replace("\r\n", "\n").Split('\n'),
                                SelfRetired, SelfReplacement, found);
            }
            catch (Exception ex)
            {
                failures.Add("[self-test] the '" + what + "' source case THREW " + ex.GetType().Name + ": " + ex.Message);
                return 0;
            }
            return Judge(failures, "source", what, expected, found);
        }

        private static int Judge(List<string> failures, string half, string what, int expected, List<string> found)
        {
            if (found.Count == expected) return 1;
            if (expected > 0)
                failures.Add("[self-test] BLIND: the " + half + " half produced " + found.Count +
                             " finding(s) for '" + what + "', expected " + expected +
                             " -- a retired word on a real surface would now pass unnoticed.");
            else
                failures.Add("[self-test] OVER-BROAD: the " + half + " half flagged '" + what +
                             "' (" + found.Count + " finding(s)), which is FROZEN vocabulary WO-1163 kept " +
                             "on purpose -- narrow the predicate, do not baseline the surface. First: " +
                             (found.Count > 0 ? found[0] : "<none>"));
            return 0;
        }

        private static bool WholeWord(string text, string word) =>
            Regex.IsMatch(text ?? "", @"(?i)(?<![A-Za-z0-9_])" + Regex.Escape(word) + @"(?![A-Za-z0-9_])");

        private static bool QuotedWholeWord(string line, string word) =>
            Regex.IsMatch(line ?? "", "(?i)\\\"[^\\\"\\r\\n]*(?<![A-Za-z0-9_])" +
                Regex.Escape(word) + "(?![A-Za-z0-9_])[^\\\"\\r\\n]*\\\"");

        private static bool RecentVisibleDeclaration(string[] lines, int index)
        {
            // Switch-return labels commonly place the method name several lines above the literal
            // (TargetLabel -> case HarvestTarget.Stone -> "Food"). Carry only a short method-local
            // window; frozen TargetToken is a different declaration and remains outside it.
            int floor = Math.Max(0, index - 10);
            for (int i = index - 1; i >= floor; i--)
            {
                string prior = lines[i];
                if (prior.IndexOf("TargetLabel(", StringComparison.OrdinalIgnoreCase) >= 0) return true;
                if (Regex.IsMatch(prior, @"\b(public|private|internal|protected)\b.*\([^;]*\)")) break;
            }
            return false;
        }

        private static bool IsVisibleJsonValue(string file, JToken value, JProperty immediate)
        {
            string normalized = file.Replace('\\', '/');
            // Localization values use dynamic/dotted keys, so their property names cannot be
            // whitelisted. Every string value in the canonical locale is player copy.
            if (normalized.EndsWith("/en.json", StringComparison.OrdinalIgnoreCase)) return true;
            if (immediate != null && VisibleJsonFields.Contains(immediate.Name)) return true;

            // Copy arrays (guide tips, tutorial hints) have JArray as the immediate parent. Walk
            // ancestors so array-held strings are not invisible to the gate.
            for (JToken cur = value.Parent; cur != null; cur = cur.Parent)
                if (cur is JProperty p &&
                    (p.Name.Equals("tips", StringComparison.OrdinalIgnoreCase) ||
                     p.Name.Equals("hints", StringComparison.OrdinalIgnoreCase) ||
                     p.Name.Equals("lines", StringComparison.OrdinalIgnoreCase)))
                    return true;
            return false;
        }

        private static string SurfaceName(JToken value, JProperty immediate)
        {
            if (immediate != null) return immediate.Name;
            for (JToken cur = value.Parent; cur != null; cur = cur.Parent)
                if (cur is JProperty p) return p.Name + "[]";
            return "<array-copy>";
        }

        /// <summary>Line number for the fix, or "line?" when the value cannot be located verbatim
        /// (escaped or re-wrapped copy). STOP: It used to return 1 in that case, which points the
        /// reader at the opening brace of the file and reads as a located line - a confidently
        /// wrong coordinate is worse than an honest unknown.</summary>
        private static string LineLabel(string file, string value)
        {
            string[] lines;
            try { lines = File.ReadAllLines(file); }
            catch { return "line?"; }
            for (int i = 0; i < lines.Length; i++)
                if (lines[i].IndexOf(value, StringComparison.Ordinal) >= 0) return (i + 1).ToString();
            return "line?";
        }

        private static string StripComments(string source)
        {
            var sb = new StringBuilder(source.Length);
            bool line = false, block = false, str = false, chr = false, escape = false;
            for (int i = 0; i < source.Length; i++)
            {
                char c = source[i], n = i + 1 < source.Length ? source[i + 1] : '\0';
                if (line) { if (c == '\n') { line = false; sb.Append(c); } else sb.Append(' '); continue; }
                if (block) { if (c == '*' && n == '/') { sb.Append("  "); i++; block = false; } else sb.Append(c == '\n' ? '\n' : ' '); continue; }
                if (!str && !chr && c == '/' && n == '/') { sb.Append("  "); i++; line = true; continue; }
                if (!str && !chr && c == '/' && n == '*') { sb.Append("  "); i++; block = true; continue; }
                sb.Append(c);
                if (escape) { escape = false; continue; }
                if ((str || chr) && c == '\\') { escape = true; continue; }
                if (!chr && c == '"') str = !str;
                else if (!str && c == '\'') chr = !chr;
            }
            return sb.ToString();
        }
    }
}

// =============================================================================
// LocalizationLiteralScanner -- inverse of SourceLint.
// SourceLint removes literal contents for call-site pins; this lexer preserves
// ordinary/verbatim/interpolated literal text, source position and its nearest
// member/sink so hard-coded player copy can be inventoried without matching comments.
//
// RED-FIRST RECIPE: temporarily pass "LOCALIZATION RATCHET PROBE" to a
// BuildObsidianButton call. Once a reviewed manifest is armed the new fingerprint
// must fail PlayerTextLiteralLeakRegression. Revert the mutation immediately.
// =============================================================================
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace DeNelle.Editor.Regression
{
    internal static class LocalizationLiteralScanner
    {
        internal sealed class Finding
        {
            public string Fingerprint;
            public string Path;
            public int Line;
            public string Member;
            public string Sink;
            public string Value;
            public string ValueHash;
            public string Classification;
        }

        private static readonly Regex MemberPattern = new Regex(
            @"\b(?:public|private|protected|internal)\s+(?:static\s+)?(?:async\s+)?[A-Za-z_][A-Za-z0-9_<>,\.\[\]\?\s]*\s+([A-Za-z_][A-Za-z0-9_]*)\s*\(",
            RegexOptions.Compiled);

        private static readonly string[] TechnicalContextTokens =
        {
            "Debug.Log", "Debug.LogWarning", "Debug.LogError", "FlowTrace.", "Guard.",
            "new GameObject", "PlayerPrefs.", "JsonProperty", "Animator.", "Shader.",
            "Resources.Load", "Addressables.", "SceneManager.", "AssetDatabase.", "Profiler."
        };

        internal static List<Finding> Scan(LocalizationAuditIO.Policy policy, List<string> failures)
        {
            var findings = new List<Finding>();
            foreach (string rootRelative in policy.scanRoots.OrderBy(v => v, StringComparer.Ordinal))
            {
                string root = LocalizationAuditIO.FullPath(rootRelative);
                if (!Directory.Exists(root))
                {
                    failures.Add("localization scan root missing: " + rootRelative);
                    continue;
                }

                string[] files;
                try { files = Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories); }
                catch (Exception ex)
                {
                    failures.Add("could not enumerate localization scan root " + rootRelative + ": " + ex.Message);
                    continue;
                }

                foreach (string file in files.OrderBy(v => v, StringComparer.OrdinalIgnoreCase))
                {
                    string normalized = LocalizationAuditIO.NormalizePath(file);
                    if (normalized.IndexOf("/Editor/", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        normalized.IndexOf("/Tests/", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        normalized.EndsWith("Test.cs", StringComparison.OrdinalIgnoreCase) ||
                        normalized.EndsWith("Tests.cs", StringComparison.OrdinalIgnoreCase)) continue;
                    try { ScanFile(normalized, File.ReadAllText(file), findings); }
                    catch (Exception ex) { failures.Add("literal scan failed for " + normalized + ": " + ex.Message); }
                }
            }
            return findings.OrderBy(f => f.Path, StringComparer.Ordinal).ThenBy(f => f.Member, StringComparer.Ordinal)
                .ThenBy(f => f.Line).ThenBy(f => f.Fingerprint, StringComparer.Ordinal).ToList();
        }

        internal static bool VerifyDeterministic(LocalizationAuditIO.Policy policy, IList<Finding> first,
            out string detail, List<string> failures)
        {
            var secondFailures = new List<string>();
            List<Finding> second = Scan(policy, secondFailures);
            if (secondFailures.Count > 0)
            {
                foreach (string failure in secondFailures) failures.Add("determinism rescan: " + failure);
                detail = "second scan failed";
                return false;
            }

            string firstSignature = Signature(first);
            string secondSignature = Signature(second);
            if (first == null || first.Count == 0)
            {
                failures.Add("literal scanner produced zero findings and therefore asserted no debt surface");
                detail = "zero findings";
                return false;
            }
            if (first.Count != second.Count || !string.Equals(firstSignature, secondSignature, StringComparison.Ordinal))
            {
                failures.Add("literal scanner is nondeterministic: first count/signature=" + first.Count + "/" + firstSignature +
                             ", second=" + second.Count + "/" + secondSignature);
                detail = "count/signature mismatch";
                return false;
            }
            detail = first.Count + " finding(s), signature=" + firstSignature;
            return true;
        }

        private static string Signature(IEnumerable<Finding> findings)
        {
            if (findings == null) return LocalizationAuditIO.Sha256("<null>");
            var value = new StringBuilder();
            foreach (var finding in findings)
                value.Append(finding.Fingerprint).Append('\0').Append(finding.Path).Append('\0')
                    .Append(finding.Member).Append('\0').Append(finding.Sink).Append('\0')
                    .Append(finding.Line).Append('\0').Append(finding.ValueHash).Append('\0')
                    .Append(finding.Classification).Append('\n');
            return LocalizationAuditIO.Sha256(value.ToString());
        }

        private static void ScanFile(string path, string source, ICollection<Finding> output)
        {
            string[] lines = source.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            var ordinalByIdentity = new Dictionary<string, int>(StringComparer.Ordinal);
            int line = 1;
            for (int i = 0; i < source.Length;)
            {
                char c = source[i];
                char next = i + 1 < source.Length ? source[i + 1] : '\0';
                if (c == '\n') { line++; i++; continue; }
                if (c == '/' && next == '/')
                {
                    i += 2;
                    while (i < source.Length && source[i] != '\n') i++;
                    continue;
                }
                if (c == '/' && next == '*')
                {
                    i += 2;
                    while (i + 1 < source.Length && !(source[i] == '*' && source[i + 1] == '/'))
                    {
                        if (source[i] == '\n') line++;
                        i++;
                    }
                    i = Math.Min(source.Length, i + 2);
                    continue;
                }
                if (c == '\'')
                {
                    i = SkipQuoted(source, i, '\'', false, ref line);
                    continue;
                }

                int quoteIndex;
                bool verbatim;
                bool interpolated;
                if (!TryStringStart(source, i, out quoteIndex, out verbatim, out interpolated)) { i++; continue; }

                int startLine = line;
                int end = ReadString(source, quoteIndex, verbatim, interpolated, ref line);
                string token = source.Substring(quoteIndex + 1, Math.Max(0, end - quoteIndex - 2));
                string value = Decode(token, verbatim);
                string member = FindMember(lines, startLine);
                string context = startLine > 0 && startLine <= lines.Length ? lines[startLine - 1].Trim() : string.Empty;
                string sink = ClassifySink(context);
                string classification = Classify(value, context, source, i, quoteIndex);
                // The CI ratchet owns high-confidence UI sinks. Unclassified literals
                // remain discoverable in the broader report manifest and are promoted
                // when their domain is reviewed; treating every technical string as UI
                // would create a permanent false-positive baseline.
                if (classification != "ignore" && classification != "review-required")
                {
                    string identity = path + "\0" + member + "\0" + sink + "\0" + value;
                    int ordinal;
                    ordinalByIdentity.TryGetValue(identity, out ordinal);
                    ordinalByIdentity[identity] = ordinal + 1;
                    string fingerprint = LocalizationAuditIO.Sha256(identity + "\0" + ordinal);
                    output.Add(new Finding
                    {
                        Fingerprint = fingerprint,
                        Path = path,
                        Line = startLine,
                        Member = member,
                        Sink = sink,
                        Value = value,
                        ValueHash = LocalizationAuditIO.Sha256(value),
                        Classification = classification
                    });
                }
                i = Math.Max(end, i + 1);
            }
        }

        private static bool TryStringStart(string source, int index, out int quoteIndex, out bool verbatim, out bool interpolated)
        {
            quoteIndex = -1;
            verbatim = false;
            interpolated = false;
            if (source[index] == '"') { quoteIndex = index; return true; }
            if (source[index] == '@' && index + 1 < source.Length && source[index + 1] == '"')
            { quoteIndex = index + 1; verbatim = true; return true; }
            if (source[index] == '$' && index + 1 < source.Length && source[index + 1] == '"')
            { quoteIndex = index + 1; interpolated = true; return true; }
            if (index + 2 < source.Length &&
                ((source[index] == '$' && source[index + 1] == '@') || (source[index] == '@' && source[index + 1] == '$')) &&
                source[index + 2] == '"')
            { quoteIndex = index + 2; verbatim = true; interpolated = true; return true; }
            return false;
        }

        private static int ReadString(string source, int quote, bool verbatim, bool interpolated, ref int line)
        {
            int braces = 0;
            for (int i = quote + 1; i < source.Length; i++)
            {
                char c = source[i];
                if (c == '\n') line++;
                if (!verbatim && c == '\\') { i++; continue; }
                if (verbatim && c == '"' && i + 1 < source.Length && source[i + 1] == '"') { i++; continue; }
                if (interpolated && c == '{')
                {
                    if (i + 1 < source.Length && source[i + 1] == '{') { i++; continue; }
                    braces++;
                    continue;
                }
                if (interpolated && c == '}' && braces > 0) { braces--; continue; }
                if (c == '"' && braces == 0) return i + 1;
            }
            return source.Length;
        }

        private static int SkipQuoted(string source, int quote, char delimiter, bool verbatim, ref int line)
        {
            for (int i = quote + 1; i < source.Length; i++)
            {
                if (source[i] == '\n') line++;
                if (!verbatim && source[i] == '\\') { i++; continue; }
                if (source[i] == delimiter) return i + 1;
            }
            return source.Length;
        }

        private static string Decode(string token, bool verbatim)
        {
            if (verbatim) return token.Replace("\"\"", "\"");
            try { return Regex.Unescape(token); }
            catch { return token; }
        }

        private static string FindMember(string[] lines, int oneBasedLine)
        {
            for (int i = Math.Min(oneBasedLine - 1, lines.Length - 1); i >= 0; i--)
            {
                Match match = MemberPattern.Match(lines[i]);
                if (match.Success) return match.Groups[1].Value + "()";
                if (i < oneBasedLine - 80) break;
            }
            return "<type>";
        }

        private static string ClassifySink(string context)
        {
            if (context.IndexOf("new LocalizedText", StringComparison.Ordinal) >= 0) return "LocalizedText-key";
            if (context.IndexOf("BuildObsidianModal", StringComparison.Ordinal) >= 0) return "BuildObsidianModal";
            if (context.IndexOf("BuildObsidianButton", StringComparison.Ordinal) >= 0) return "BuildObsidianButton";
            if (context.IndexOf("SetText", StringComparison.Ordinal) >= 0) return "SetText";
            if (Regex.IsMatch(context, @"\.text\s*=")) return "text-assignment";
            if (Regex.IsMatch(context, @"\b(title|body|label|caption|tooltip|message|placeholder)\b", RegexOptions.IgnoreCase)) return "named-player-copy";
            return "unclassified-literal";
        }

        private static string Classify(string value, string context, string source, int prefixIndex, int quoteIndex)
        {
            if (string.IsNullOrWhiteSpace(value) || !value.Any(char.IsLetter)) return "ignore";
            foreach (string token in TechnicalContextTokens)
                if (context.IndexOf(token, StringComparison.Ordinal) >= 0) return "ignore";

            int lineStart = source.LastIndexOf('\n', Math.Max(0, prefixIndex)) + 1;
            string beforeLiteral = source.Substring(lineStart, Math.Max(0, quoteIndex - lineStart));
            int localText = beforeLiteral.LastIndexOf("LocalText.Get", StringComparison.Ordinal);
            if (localText >= 0)
            {
                string invocationPrefix = beforeLiteral.Substring(localText);
                return invocationPrefix.IndexOf(',') >= 0 ? "localized-fallback-debt" : "ignore";
            }

            // Common generic wrappers and thin feature catalogs carry semantic keys,
            // not English. Their dotted key literal is localized by construction;
            // a human sentence accidentally passed to the wrapper remains a finding.
            if (context.IndexOf("new LocalizedText", StringComparison.Ordinal) >= 0 && LooksTechnicalValue(value)) return "ignore";
            if (LooksTechnicalValue(value)) return "ignore";
            return ClassifySink(context) == "unclassified-literal" ? "review-required" : "player-copy";
        }

        private static bool LooksTechnicalValue(string value)
        {
            if (value.IndexOf(' ') >= 0 || value.IndexOf('\n') >= 0) return false;
            if (Regex.IsMatch(value, @"^(https?://|[A-Za-z]:[/\\]|[0-9a-fA-F]{16,}|[A-Za-z0-9_-]+\.(json|asset|prefab|unity|png|jpg|mat|wav|mp3))")) return true;
            if (Regex.IsMatch(value, @"^[a-z0-9_-]+(\.[a-z0-9_-]+)+$")) return true;
            if (value.StartsWith("Assets/", StringComparison.Ordinal) || value.IndexOf('/') >= 0 || value.IndexOf('\\') >= 0) return true;
            return false;
        }
    }
}

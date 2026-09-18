// =============================================================================
// LiveClassBowAndAffordSeverityRegression [live-class-bow-afford]
// -----------------------------------------------------------------------------
// Assembly: DeNelle.EditorRegression (editor-only). Contract mirrors the other
// Run(out reason) oracles:
//   public static bool Run(out string reason)   -- NEVER throws
//   markers: LIVE_CLASS_BOW_AFFORD_OK (Debug.Log) / LIVE_CLASS_BOW_AFFORD_FAIL (LogError)
//
// WHY THIS EXISTS (three player-visible defects, 2026-08-16). All three were bugs
// that a compile-green tree carried happily, and two of them had already been
// "fixed" once and regressed, so the pins below are written to fail on the CLASS
// of mistake, not the instance:
//
//   PIN 1  THE TALENT PANEL MUST RESOLVE THE LIVE HERO CLASS.
//          HeroSkillTreeVM carried `public const string HeroSlug = "knight"` as its
//          ctor default and the ONE construction site passed no slug, so a Ranger
//          player browsed and BOUGHT knight nodes while HeroTalentModifiers folded
//          stats from the real class -- every point spent was inert. A hardcoded
//          slug, in the ctor default or at a call site, must fail this suite.
//
//   PIN 2  THE BOW DE-DUPE MUST NOT BE DEFEATABLE BY COMPONENT-ADD ORDER.
//          EquipmentController cached `GetComponent<HeroBowAttachment>() != null`
//          in Awake. HeroBodySwapper adds the EquipmentController BEFORE it calls
//          HeroBowAttachment.AttachTo, so the latch was permanently false, the bow
//          skip never fired, and the Ranger wore TWO bows. Caching that answer at
//          Awake (or any construction-time point) must fail this suite.
//
//   PIN 3  "TOO POOR TO UPGRADE" IS NOT AN ERROR.
//          BuildingUpgradeService + BuildingUpgradeVM logged the affordability
//          rejection at FlowTrace.Fail, on the very branch that sets the player-
//          facing "You can't afford that yet." A broke player minted two F8 error
//          captures + a screenshot per tap, burying real captures. Every sibling
//          spend path uses a non-error severity; these two must too.
//
// Source-lint method: comments are stripped before matching (a doc-comment quoting
// the banned pattern must never fail the oracle, and prose must never PASS it),
// and string literals are stripped too wherever the assertion is about CODE.
// =============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;
using DeNelle.Village.Talents;

namespace DeNelle.Editor.Regression
{
    public static class LiveClassBowAndAffordSeverityRegression
    {
        private const string SkillTreeVmSrc   = "Assets/_Modules/Village/Talents/HeroSkillTreeVM.cs";
        private const string EquipSrc         = "Assets/_Modules/Village/Hero/EquipmentController.cs";
        private const string BodySwapperSrc   = "Assets/_Modules/Village/Hero/HeroBodySwapper.cs";
        private const string UpgradeSvcSrc    = "Assets/_Modules/Village/Buildings/Progression/BuildingUpgradeService.cs";
        private const string UpgradeVmSrc     = "Assets/_Modules/Village/Buildings/Progression/BuildingUpgradeVM.cs";

        private static readonly string[] CallSiteRoots = { "Assets/_Modules", "Assets/Editor" };

        public static bool Run(out string reason)
        {
            var failures = new List<string>();
            var log = new StringBuilder();
            log.AppendLine("=== LiveClassBowAndAffordSeverityRegression [live-class-bow-afford] ===");

            try
            {
                CaseLiveHeroSlug(failures, log);
                CaseBowDedupeOrderProof(failures, log);
                CaseAffordSeverity(failures, log);
            }
            catch (Exception ex)
            {
                failures.Add("LiveClassBowAndAffordSeverityRegression THREW: " + ex.GetType().Name + ": " + ex.Message);
            }

            if (failures.Count == 0)
            {
                reason = "LIVE CLASS / BOW / AFFORD OK - the talent panel resolves the LIVE hero class " +
                         "(no hardcoded slug in the ctor default or at any call site) and honours the slug it " +
                         "is given; the bow de-dupe is evaluated live so component-add order cannot defeat it; " +
                         "and neither upgrade-affordability site logs a normal 'can't afford' outcome as an error.";
                Debug.Log("LIVE_CLASS_BOW_AFFORD_OK\n" + log);
                return true;
            }

            reason = "LIVE CLASS / BOW / AFFORD: " + failures.Count + " failure(s): " + string.Join(" | ", failures);
            Debug.LogError("LIVE_CLASS_BOW_AFFORD_FAIL: " + failures.Count + " failure(s)\n" + log +
                           "\n - " + string.Join("\n - ", failures));
            return false;
        }

        // =====================================================================
        //  CASE 1 [live-slug]
        //  Behavioural: the VM builds the tree it is ASKED for (ranger nodes for
        //  "ranger", knight nodes for "knight") -- so the panel is capable of
        //  showing a non-knight tree at all.
        //  Source-lint: nothing hardcodes the slug back in.
        // =====================================================================
        private static void CaseLiveHeroSlug(List<string> failures, StringBuilder log)
        {
            // -- behavioural half -------------------------------------------------
            foreach (string slug in new[] { "ranger", "knight" })
            {
                HeroSkillTreeVM vm = null;
                try
                {
                    vm = new HeroSkillTreeVM(null, slug);
                    var nodes = vm.Nodes;
                    if (nodes == null || nodes.Count == 0)
                    {
                        failures.Add("[live-slug] HeroSkillTreeVM(\"" + slug + "\") produced ZERO nodes - " +
                                     "the '" + slug + "' tree is unreachable through the panel");
                        continue;
                    }
                    int foreign = 0;
                    string firstForeign = null;
                    foreach (var n in nodes)
                    {
                        if (string.IsNullOrEmpty(n.Id)) continue;
                        if (!n.Id.StartsWith(slug + ".", StringComparison.Ordinal))
                        {
                            foreign++;
                            if (firstForeign == null) firstForeign = n.Id;
                        }
                    }
                    if (foreign > 0)
                        failures.Add("[live-slug] HeroSkillTreeVM(\"" + slug + "\") returned " + foreign +
                                     " node(s) from ANOTHER hero tree (e.g. '" + firstForeign +
                                     "') - the panel is not honouring the slug it was given");
                    else
                        log.AppendLine("  [live-slug] '" + slug + "' -> " + nodes.Count + " node(s), all '" + slug + ".' OK");
                }
                catch (Exception ex)
                {
                    failures.Add("[live-slug] constructing HeroSkillTreeVM(\"" + slug + "\") THREW: " +
                                 ex.GetType().Name + ": " + ex.Message);
                }
                finally
                {
                    try { vm?.Dispose(); } catch (Exception) { /* disposal noise never fails the oracle */ }
                }
            }

            // -- source-lint half: the ctor default --------------------------------
            string raw = ReadSource(SkillTreeVmSrc);
            if (raw == null)
            {
                failures.Add("[live-slug] cannot read " + SkillTreeVmSrc + " - the hardcoded-slug lint could not run");
            }
            else
            {
                // Comments stripped, STRINGS KEPT: a string-literal default is exactly what we hunt.
                string decl = StripComments(raw);
                var m = Regex.Match(decl, @"public\s+HeroSkillTreeVM\s*\(([^)]*)\)");
                if (!m.Success)
                {
                    failures.Add("[live-slug] the HeroSkillTreeVM constructor declaration was not found (renamed?) - " +
                                 "the hardcoded-slug pin is unverifiable");
                }
                else
                {
                    // Whitespace COLLAPSED, not removed - the parameter grammar needs its spaces.
                    string args = Regex.Replace(m.Groups[1].Value, @"\s+", " ").Trim();
                    var def = Regex.Match(args, @"string\s+\w+\s*=\s*(?<d>[^,)]+)");
                    if (!def.Success)
                    {
                        failures.Add("[live-slug] the HeroSkillTreeVM slug parameter has no default at all - " +
                                     "the one call site relies on it resolving the live class (args: " + args + ")");
                    }
                    else
                    {
                        string d = def.Groups["d"].Value.Trim();
                        if (d != "null")
                            failures.Add("[live-slug] the HeroSkillTreeVM slug parameter defaults to '" + d +
                                         "' instead of null - a constant default is the ORIGINAL bug (a Ranger " +
                                         "browsing the knight tree). Default to null and resolve the live class.");
                        else
                            log.AppendLine("  [live-slug] ctor slug default is null (live-resolved) OK");
                    }
                }

                // Comments AND strings stripped: the live resolver must actually be called.
                string code = StripCommentsAndStrings(raw);
                if (!code.Contains("HeroTalentClassReader.Slug"))
                    failures.Add("[live-slug] HeroSkillTreeVM no longer calls HeroTalentClassReader.Slug() - " +
                                 "the panel's hero slug is not coming from the LIVE GameState class any more");
            }

            // -- source-lint half: no call site passes a literal slug ---------------
            int sites = 0;
            foreach (string file in EnumerateSources())
            {
                if (file.Replace('\\', '/').EndsWith("LiveClassBowAndAffordSeverityRegression.cs", StringComparison.Ordinal))
                    continue;   // this oracle constructs with explicit slugs BY DESIGN
                string src = ReadSourceAbs(file);
                if (src == null || src.IndexOf("new HeroSkillTreeVM", StringComparison.Ordinal) < 0) continue;
                string decl = StripComments(src);   // strings KEPT - a literal slug is the smell
                foreach (Match call in Regex.Matches(decl, @"new\s+HeroSkillTreeVM\s*\(([^)]*)\)"))
                {
                    sites++;
                    string args = call.Groups[1].Value;
                    if (args.IndexOf('"') >= 0)
                        failures.Add("[live-slug] " + Rel(file) + " constructs HeroSkillTreeVM with a LITERAL slug (" +
                                     Squash(args) + ") - production must pass no slug so the live hero class wins");
                }
            }
            if (sites == 0)
                failures.Add("[live-slug] no HeroSkillTreeVM construction site found in the tree - the call-site " +
                             "lint would pass vacuously; the panel may have been renamed");
            else
                log.AppendLine("  [live-slug] " + sites + " production construction site(s), none passing a literal slug OK");
        }

        // =====================================================================
        //  CASE 2 [bow-dedupe]
        //  The de-dupe answer must be computed AT USE, because the ordering that
        //  broke it is still the real ordering in HeroBodySwapper (asserted here
        //  so this pin keeps its teeth rather than depending on a fragile order).
        // =====================================================================
        private static void CaseBowDedupeOrderProof(List<string> failures, StringBuilder log)
        {
            string equipRaw = ReadSource(EquipSrc);
            if (equipRaw == null)
            {
                failures.Add("[bow-dedupe] cannot read " + EquipSrc + " - the bow de-dupe pin could not run");
            }
            else
            {
                string code = Squash(StripCommentsAndStrings(equipRaw));

                // (a) the answer must never be STORED. Any assignment of the component probe to a
                //     field/local that outlives the call is the cached-latch bug returning.
                if (Regex.IsMatch(code, @"\w+\s*=\s*GetComponent<HeroBowAttachment>\(\)\s*!=\s*null\s*;"))
                    failures.Add("[bow-dedupe] EquipmentController CACHES GetComponent<HeroBowAttachment>() != null " +
                                 "into a variable - that latch is false forever (HeroBodySwapper adds this component " +
                                 "BEFORE the bow attachment), which is how the Ranger ended up holding two bows");

                // NOTE: `code` is whitespace-SQUASHED, so the needles below carry no spaces.
                if (code.Contains("bool_deferBowToBowAttachment;"))
                    failures.Add("[bow-dedupe] the cached _deferBowToBowAttachment FIELD is back in EquipmentController - " +
                                 "the de-dupe must be evaluated live, not stored");

                // (b) the answer must be computed live, as an expression-bodied member.
                if (!code.Contains("boolDeferBowToBowAttachment=>GetComponent<HeroBowAttachment>()!=null;"))
                    failures.Add("[bow-dedupe] EquipmentController has no live 'DeferBowToBowAttachment => " +
                                 "GetComponent<HeroBowAttachment>() != null' member - the bow skip is no longer " +
                                 "order-proof");

                // (c) and it must actually GATE the bow attach.
                if (!Regex.IsMatch(code, @"if\(DeferBowToBowAttachment&&"))
                    failures.Add("[bow-dedupe] the bow-skip guard in EquipmentController no longer reads " +
                                 "DeferBowToBowAttachment - the second bow can come back");
                else
                    log.AppendLine("  [bow-dedupe] EquipmentController evaluates the de-dupe LIVE at the skip site OK");
            }

            // (d) the hostile ordering is REAL: prove EquipmentController is added before the bow
            //     attachment, so a future cached latch would once again be false forever.
            string bodyRaw = ReadSource(BodySwapperSrc);
            if (bodyRaw == null)
            {
                failures.Add("[bow-dedupe] cannot read " + BodySwapperSrc + " - the ordering proof could not run");
            }
            else
            {
                string body = StripCommentsAndStrings(bodyRaw);
                int addEquip = body.IndexOf("AddComponent<EquipmentController>", StringComparison.Ordinal);
                int attachBow = body.IndexOf("HeroBowAttachment.AttachTo", StringComparison.Ordinal);
                if (addEquip < 0 || attachBow < 0)
                {
                    log.AppendLine("  [bow-dedupe] NOTE: HeroBodySwapper no longer shows both the EquipmentController " +
                                   "add and the HeroBowAttachment.AttachTo call - ordering proof skipped (the live " +
                                   "evaluation above is what actually holds the invariant).");
                }
                else if (addEquip < attachBow)
                {
                    log.AppendLine("  [bow-dedupe] ordering hazard CONFIRMED: EquipmentController is added before " +
                                   "HeroBowAttachment.AttachTo - a cached latch would be false forever, which is why " +
                                   "(a)-(c) above are asserted.");
                }
                else
                {
                    log.AppendLine("  [bow-dedupe] NOTE: the bow attachment now precedes the EquipmentController add. " +
                                   "The live evaluation is still required - order must never be the thing holding " +
                                   "this invariant up.");
                }
            }
        }

        // =====================================================================
        //  CASE 3 [afford-severity]
        //  A normal, player-facing "you can't afford that" must not be logged as
        //  an error -- it wakes the F8 triage daemon and buries real captures.
        // =====================================================================
        private static void CaseAffordSeverity(List<string> failures, StringBuilder log)
        {
            // The anchor is a literal ON the affordability branch; `searchForward` says which side
            // of it the branch's own FlowTrace call sits. Both files carry OTHER, LEGITIMATE
            // FlowTrace.Fail calls within a few lines (a null catalog def genuinely IS an error),
            // so this pin resolves the NEAREST trace call in the stated direction rather than
            // sweeping a window - a window would condemn the honest Fail next door.
            AssertBranchSeverity(failures, log, UpgradeSvcSrc, "spend REJECTED", searchForward: false,
                                 what: "BuildingUpgradeService's tier-spend rejection");
            // WO-1857: the VM's affordability sentence moved into the English string table, so the
            // anchor is now the resolved KEY. It still sits ON the affordability branch and still
            // occurs first at that branch, so `searchForward` resolves the same FlowTrace call as
            // before. This was a LIVE red, not a hypothetical: AssertBranchSeverity strips comments
            // before searching, so the retired sentence surviving in three BuildingUpgradeVM comments
            // did not keep the old anchor findable.
            AssertBranchSeverity(failures, log, UpgradeVmSrc,
                                 "village.buildings.progression.upgrade_unaffordable", searchForward: true,
                                 what: "BuildingUpgradeVM's affordability branch");
        }

        /// <summary>
        /// Locates <paramref name="anchor"/> (a literal on the affordability branch), resolves the
        /// NEAREST <c>FlowTrace.*</c> call in the given direction, and asserts that call is not an
        /// error severity. Comments are stripped so a doc-comment mentioning FlowTrace.Fail cannot
        /// fail the oracle; string literals are KEPT because the anchor itself is one.
        /// </summary>
        private static void AssertBranchSeverity(List<string> failures, StringBuilder log,
                                                 string relPath, string anchor, bool searchForward, string what)
        {
            string raw = ReadSource(relPath);
            if (raw == null)
            {
                failures.Add("[afford-severity] cannot read " + relPath + " - " + what + " could not be checked");
                return;
            }
            string src = StripComments(raw);
            int at = src.IndexOf(anchor, StringComparison.Ordinal);
            if (at < 0)
            {
                failures.Add("[afford-severity] anchor \"" + anchor + "\" not found in " + relPath +
                             " - " + what + " moved or was reworded; the severity pin is unverifiable");
                return;
            }

            const string Needle = "FlowTrace.";
            int callAt = searchForward
                ? src.IndexOf(Needle, at, StringComparison.Ordinal)
                : src.LastIndexOf(Needle, Math.Min(at, src.Length - 1), StringComparison.Ordinal);
            if (callAt < 0)
            {
                failures.Add("[afford-severity] " + what + " has NO FlowTrace call " +
                             (searchForward ? "after" : "before") + " its anchor - the state dump must NOT be " +
                             "stripped, only its severity corrected (canon sec 12: never strip instrumentation)");
                return;
            }

            var sev = Regex.Match(src.Substring(callAt + Needle.Length), @"^(?<s>\w+)\s*\(");
            string severity = sev.Success ? sev.Groups["s"].Value : "<unparsed>";

            if (severity == "Fail")
                failures.Add("[afford-severity] " + what + " logs at FlowTrace.Fail - an unaffordable tap is a " +
                             "NORMAL outcome (the same branch tells the player \"You can't afford that yet.\"), so " +
                             "this mints an F8 error capture + screenshot on every tap by a broke player. Use " +
                             "FlowTrace.Capture (kind note, skipped by the watch daemon) and keep the state dump.");
            else if (severity != "Capture")
                failures.Add("[afford-severity] " + what + " logs at FlowTrace." + severity + " - expected " +
                             "Capture, the severity that keeps the state dump without waking the F8 triage daemon");
            else
                log.AppendLine("  [afford-severity] " + what + " -> FlowTrace.Capture, not Fail OK");
        }

        // =====================================================================
        //  Helpers
        // =====================================================================

        /// <summary>The folder ABOVE Assets/ -- never the process cwd, which batchmode does not pin.</summary>
        private static string ProjectRoot
        {
            get
            {
                var parent = Directory.GetParent(Application.dataPath);
                return parent != null ? parent.FullName : Directory.GetCurrentDirectory();
            }
        }

        private static IEnumerable<string> EnumerateSources()
        {
            foreach (string root in CallSiteRoots)
            {
                string abs = Path.Combine(ProjectRoot, root);
                if (!Directory.Exists(abs)) continue;
                string[] files;
                try { files = Directory.GetFiles(abs, "*.cs", SearchOption.AllDirectories); }
                catch (Exception) { continue; }
                foreach (string f in files) yield return f;
            }
        }

        private static string Rel(string abs)
        {
            string root = ProjectRoot.Replace('\\', '/');
            string p = abs.Replace('\\', '/');
            return p.StartsWith(root, StringComparison.OrdinalIgnoreCase) ? p.Substring(root.Length).TrimStart('/') : p;
        }

        private static string ReadSource(string rel) => ReadSourceAbs(Path.Combine(ProjectRoot, rel));

        private static string ReadSourceAbs(string path)
        {
            try { return File.Exists(path) ? File.ReadAllText(path) : null; }
            catch (Exception) { return null; }
        }

        private static string Squash(string s) => Regex.Replace(s ?? "", @"\s+", "");

        /// <summary>Blanks // and /* */ comments, preserving offsets and newlines. Strings are KEPT.</summary>
        private static string StripComments(string src) => Strip(src, stripStrings: false);

        /// <summary>Blanks comments AND string/char literals, preserving offsets.</summary>
        private static string StripCommentsAndStrings(string src) => Strip(src, stripStrings: true);

        private static string Strip(string src, bool stripStrings)
        {
            if (string.IsNullOrEmpty(src)) return "";
            var outp = src.ToCharArray();
            int i = 0, n = src.Length;
            while (i < n)
            {
                char c = src[i];
                if (c == '/' && i + 1 < n && src[i + 1] == '/')
                {
                    while (i < n && src[i] != '\n') { outp[i] = ' '; i++; }
                }
                else if (c == '/' && i + 1 < n && src[i + 1] == '*')
                {
                    outp[i] = ' '; outp[i + 1] = ' '; i += 2;
                    while (i + 1 < n && !(src[i] == '*' && src[i + 1] == '/'))
                    {
                        if (src[i] != '\n') outp[i] = ' ';
                        i++;
                    }
                    if (i + 1 < n) { outp[i] = ' '; outp[i + 1] = ' '; i += 2; }
                }
                else if (c == '@' && i + 1 < n && src[i + 1] == '"')
                {
                    if (stripStrings) { outp[i] = ' '; outp[i + 1] = ' '; }
                    i += 2;
                    while (i < n)
                    {
                        if (src[i] == '"')
                        {
                            if (i + 1 < n && src[i + 1] == '"')
                            {
                                if (stripStrings) { outp[i] = ' '; outp[i + 1] = ' '; }
                                i += 2; continue;
                            }
                            if (stripStrings) outp[i] = ' ';
                            i++; break;
                        }
                        if (stripStrings && src[i] != '\n') outp[i] = ' ';
                        i++;
                    }
                }
                else if (c == '"' || c == '\'')
                {
                    char quote = c;
                    if (stripStrings) outp[i] = ' ';
                    i++;
                    while (i < n)
                    {
                        if (src[i] == '\\')
                        {
                            if (stripStrings) { outp[i] = ' '; if (i + 1 < n) outp[i + 1] = ' '; }
                            i += 2; continue;
                        }
                        if (src[i] == quote) { if (stripStrings) outp[i] = ' '; i++; break; }
                        if (src[i] == '\n') break;   // unterminated - bail rather than eat the file
                        if (stripStrings) outp[i] = ' ';
                        i++;
                    }
                }
                else i++;
            }
            return new string(outp);
        }
    }
}

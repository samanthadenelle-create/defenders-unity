// =============================================================================
// PanelDoorRegression [panel-door] -- WAVE 0 LANE C seam oracle #2 of the family
// opened by ProgressionReachabilityRegression (WO-1423).
// -----------------------------------------------------------------------------
// EVERY PANEL MUST HAVE A DOOR. A MonoBehaviour panel that no OTHER production
// file constructs or opens, that no scene references and that no prefab
// references, is a dead system that LOOKS shipped: it compiles, it has art, it
// has a ViewModel, it has suites -- and no player can ever see it.
//
// WHY THIS SUITE EXISTS (the class of bug, not one instance): on 2026-09-06 the
// full suite ran 394 green while SEVEN OF NINE troop types were unreachable,
// because BarracksPanel had exactly this shape. Every oracle in the tree asked
// "does this system do its job"; none asked "can a player get here at all".
// HeartPanelBootstrap.cs:12-14 records the same finding in prose: "A panel with
// no spawner is a panel with no door - that is exactly how BarracksPanel sat
// unreachable in the tree (OWNER_RULINGS_LOCKED §21)."
//
// -----------------------------------------------------------------------------
// WHAT "PANEL-LIKE" MEANS HERE (the definition, stated so it can be argued with)
// -----------------------------------------------------------------------------
// A type is panel-like iff ALL of:
//   (a) it is declared in a .cs file under Assets/_Modules/,
//   (b) its base list contains MonoBehaviour, and
//   (c) its type name ends with "Panel" or "PanelMvvm", OR its base list
//       contains IPanelView.
//
// Deliberately EXCLUDED by that definition, and why:
//   * PanelOpenCloseFx, VillageCraftingPanelInput -- helper components that merely
//     CONTAIN the word "Panel"; they are behaviours attached to a panel, not
//     destinations. A "contains Panel" rule would drag them in and make the
//     oracle noise.
//   * DevPanelController, LoginPanelController, CraftingPanelController,
//     JupiterSwapPanelController -- *Controller types. They are not screens the
//     player routes to; including them would require a second, different door
//     rule and would blur what this oracle proves.
//   * Everything outside Assets/_Modules (Editor tooling, tests, DevTools drivers
//     live under _Modules/DevTools and are handled below as NON-doors).
// Measured 2026-09-06: 35 types satisfy (a)+(b)+(c) -- 32 after WO-1430 Group A
// deleted BarracksPanel, ShopPanel and TalentTreePanel the same day.
//
// -----------------------------------------------------------------------------
// WHAT COUNTS AS A DOOR (and what deliberately does not)
// -----------------------------------------------------------------------------
// For panel type P declared in <P>.cs, define P's HOME SET -- the files that are
// part of P's own View/VM/Bootstrap loop and therefore cannot vouch for it:
//     <P>.cs, <P>VM.cs, <P>Bootstrap.cs, and with Stem = P minus its
//     "Panel"/"PanelMvvm" suffix: <Stem>VM.cs, <Stem>Panel.cs, <Stem>PanelVM.cs,
//     <Stem>Bootstrap.cs, <Stem>PanelBootstrap.cs.
// The home set exists because of BarracksPanel: its ONLY construction site was
// BarracksPanelVM.ResolveOrCreateHost (BarracksPanelVM.cs:183-187), which was
// called only from BarracksPanel.Open (BarracksPanel.cs:82). A View and its VM
// constructing each other is a closed loop, not a door.
// ⚠ Both files were DELETED 2026-09-06 (WO-1430 Group A) once the oracle proved
// the loop, so those two citations are HISTORY, not paths you can open. The rule
// they motivated stands and is why the home set is still computed.
//
// P has a door iff ANY of:
//   D1  some .cs under Assets/_Modules/ that is NOT in P's home set and NOT under
//       Assets/_Modules/DevTools/ names P in real code; or
//   D2  a home-set file OTHER than <P>.cs names P and itself carries
//       [RuntimeInitializeOnLoadMethod] -- an engine-invoked root, e.g.
//       HeartPanelBootstrap.Install (HeartPanelBootstrap.cs:31-37); or
//   D3  a .unity or .prefab under Assets/ serialises P's SCRIPT GUID.
//
// ⚠ D3 reads the GUID out of <P>.cs.meta and searches for THAT. Unity serialises
// components by script GUID, never by class name -- a grep for the class name
// across .unity/.prefab proves NOTHING. (Measured 2026-09-06: zero of the 35
// panels is referenced by any scene or prefab; this project builds its UI in
// code, PIPELINE_STATE §8 "UXML in builds: does NOT work".) D3 is kept anyway so
// a scene-wired panel is never mis-reported.
//
// NOT doors, on purpose:
//   * Assets/_Modules/DevTools/** (AutoPilotDriver.CaptureComponentPanel) and
//     Assets/Editor/** (UICaptureLaunch.CaptureBuiltModal). Both find-or-
//     AddComponent every panel in the game so it can be photographed. If they
//     counted, EVERY panel would pass trivially and this oracle would be a
//     hollow pass. A panel reachable ONLY from them is reported as
//     [panel-door-is-harness-only], which names the harness files -- so the
//     exclusion is visible in the failure, never silent.
//   * Comments and string literals. Both are stripped before matching, because
//     this repo leaves tombstone prose naming retired types (FeatureFlags.cs:1536
//     mentions SkrShowcasePanel inside a Debug.Log string) and a naive name grep
//     would read those as doors.
//   * `class P` declaration lines, so a `partial class` split across files
//     (HeroInventoryController) cannot vouch for itself.
//
// -----------------------------------------------------------------------------
// WHAT THIS ORACLE DOES **NOT** PROVE -- read before trusting a green
// -----------------------------------------------------------------------------
//   * It proves a CONSTRUCTION-OR-OPEN ROOT outside the panel's own View/VM loop.
//     It does NOT prove a pressable route: a bootstrap that installs a host
//     component proves the object exists, not that any button reaches it. A
//     "every locked tile's CTA opens something that genuinely opens" oracle is a
//     SEPARATE, UNWRITTEN member of this family (CLI_DRIVING_PLAN §2 Wave 3).
//   * It is source-text analysis, not a call graph. A door that is itself dead
//     code still counts as a door here.
//   * Reflection-only opens are invisible to it. None exist today; if one is
//     added it must go in the allowlist below with a reason, never be worked
//     around by weakening the rule.
//
// Marker: PANEL_DOOR_OK / PANEL_DOOR_FAIL <case>.
// EXPECTED ON ARRIVAL (2026-09-06): **RED**, on three real defects. All three were
// RETIRED the same day by WO-1430 Group A (BarracksPanel, ShopPanel, TalentTreePanel
// deleted with their VMs), so the allowlist is now genuinely EMPTY and the suite is
// expected GREEN. A fourth doorless panel fails the build immediately.
//
// Wire (DataRegression.RunAll):
//   DeNelle.Core.Diagnostics.Guard.Try("Regression", "panel-door suite", () => { if (!DeNelle.Editor.PanelDoorRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[panel-door] " + r); });
// =============================================================================
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;

namespace DeNelle.Editor
{
    /// <summary>Source oracle: no panel-like MonoBehaviour may exist without a door.</summary>
    public static class PanelDoorRegression
    {
        private const string Tag = "[panel-door]";

        // ---------------------------------------------------------------------
        // THE ALLOWLIST. It is EMPTY, and that is the point.
        //
        // Three panels failed this oracle on the day it was written. They were
        // parked here for ONE DAY with a decision owed, and on 2026-09-06 the CLI
        // seat took all three decisions in WO-1430 GROUP A: every one was RETIRED,
        // deleted from the tree with its ViewModel, its harness callers and its
        // stale canon. So there is nothing left to excuse.
        //   * BarracksPanel + BarracksPanelVM -- DELETED. Obsolete as a level
        //     control after OWNER_RULINGS_LOCKED §21 merged the two barracks
        //     levels; ruling 21 point 4 routes WO-2008's locked-tile CTA to the
        //     barracks BUILDING card in Build ("no new screen"), and WO-2009's
        //     troop detail is the Manage Army tab, whose View "may not call
        //     BarracksService directly" -- the exact opposite of this panel's
        //     shape. WO-2009 therefore loses nothing.
        //   * ShopPanel + ShopVM -- DELETED. Superseded by PartyShopPanelMvvm,
        //     which DialogueCommandSink routes to unconditionally. Restoring the
        //     flag branch was rejected as gaming the oracle: it would have been a
        //     door to a screen FeatureFlags itself calls "two sell bars, no party
        //     selection, blank icons", behind a branch that is dead while
        //     ff.partyshop defaults ON. The stale FeatureFlags claim was corrected
        //     in the same change (CLAUDE.md §15).
        //   * TalentTreePanel -- DELETED. Superseded by HeroSkillTreePanelMvvm;
        //     the "OpenTalents" verb was re-pointed to PanelId.HeroSkillTree and
        //     the legacy PanelId.HeroTalents route REMOVED. Also UI-Toolkit, which
        //     CLAUDE.md §8 records as not working in builds.
        //
        // To add an entry: name the type, and state in the comment WHY it is
        // legitimately doorless (editor-only tooling, reflection-only open,
        // deliberately parked system) and what would retire the entry. An
        // unexplained exclusion is how the next dead panel hides. An exemption that
        // outlives its finding is the exact rot this oracle exists to prevent --
        // DELETE the entry in the same change that resolves the finding, as
        // WO-1430 did with all three.
        // ---------------------------------------------------------------------
        // WO-1495 2026-09-06 remove-by 2026-12-06 (origin WO-1430) - legitimately doorless panel
        // types. MEASURED EMPTY on 2026-09-06: WO-1430 resolved all three and deleted them, which
        // is the shape this block wants; the remove-by forces a re-read if entries come back.
        private static readonly HashSet<string> Allowlist = new HashSet<string>(StringComparer.Ordinal)
        {
            // WO-1876 2026-09-19 remove-by 2026-12-19 — rebuild door retired; castle
            // HudKit is the player path. Harnesses still AddComponent it for photos.
            "OwnedTownPanel",
        };

        // A panel-like type and where it was declared.
        private sealed class PanelType
        {
            public string Name;
            public string Path;      // absolute
            public string Rel;       // "Assets/..."-relative, for messages
        }

        public static bool Run(out string reason)
        {
            var failures = new List<string>();
            var log = new StringBuilder("=== PanelDoorRegression (Wave 0 Lane C) ===\n");
            try
            {
                CheckEveryPanelHasADoor(failures, log);
            }
            catch (Exception ex)
            {
                failures.Add(Tag + " suite threw " + ex.GetType().Name + ": " + ex.Message);
            }

            if (failures.Count == 0)
            {
                reason = "PANEL_DOOR_OK every panel-like MonoBehaviour under Assets/_Modules is constructed " +
                         "or opened from outside its own View/VM loop, or is referenced by a scene/prefab script GUID";
                Debug.Log(reason + "\n" + log);
                return true;
            }

            reason = "PANEL_DOOR_FAIL " + string.Join(" | ", failures);
            Debug.LogError(reason + "\n" + log);
            return false;
        }

        // =====================================================================
        private static void CheckEveryPanelHasADoor(List<string> failures, StringBuilder log)
        {
            string assets = Application.dataPath.Replace('\\', '/');       // <project>/Assets
            string modules = assets + "/_Modules";
            string editorRoot = assets + "/Editor";

            if (!Directory.Exists(modules))
            {
                // A MISSING FIXTURE FAILS AND NAMES ITSELF. It never silently passes.
                failures.Add(Tag + " Assets/_Modules does not exist at '" + modules + "' - the panel inventory " +
                             "cannot be built, so no claim about doors can be made. FAIL, not a skip");
                return;
            }

            var moduleFiles = Directory.GetFiles(modules, "*.cs", SearchOption.AllDirectories);
            if (moduleFiles.Length == 0)
            {
                failures.Add(Tag + " Assets/_Modules contains ZERO .cs files - the scan would vacuously pass " +
                             "every panel. FAIL, not a skip");
                return;
            }

            // ---- one pass: strip every module source once -------------------
            var stripped = new Dictionary<string, string>(moduleFiles.Length, StringComparer.OrdinalIgnoreCase);
            foreach (var f in moduleFiles)
            {
                string norm = f.Replace('\\', '/');
                stripped[norm] = StripCommentsAndStrings(SafeRead(norm));
            }

            // ---- 1. the inventory -------------------------------------------
            var panels = new Dictionary<string, PanelType>(StringComparer.Ordinal);
            var decl = new Regex(@"\bclass\s+([A-Za-z0-9_]+)\s*:\s*([^\{]{0,200})", RegexOptions.Compiled);
            foreach (var kv in stripped)
            {
                foreach (Match m in decl.Matches(kv.Value))
                {
                    string name = m.Groups[1].Value;
                    string bases = m.Groups[2].Value;
                    if (bases.IndexOf("MonoBehaviour", StringComparison.Ordinal) < 0) continue;
                    bool named = name.EndsWith("Panel", StringComparison.Ordinal) ||
                                 name.EndsWith("PanelMvvm", StringComparison.Ordinal);
                    bool view = bases.IndexOf("IPanelView", StringComparison.Ordinal) >= 0;
                    if (!named && !view) continue;
                    // Dedupe by NAME: SkrShowcasePanel is declared twice behind #if GOOGLE_PLAY
                    // (SkrShowcasePanel.cs:56 and :289) but is ONE type with ONE door question.
                    if (panels.ContainsKey(name)) continue;
                    panels[name] = new PanelType { Name = name, Path = kv.Key, Rel = ToRel(kv.Key, assets) };
                }
            }

            // PRESENCE, so an absence assertion can never pass vacuously on a deleted tree.
            // REVERT RECIPE (RED): rename Assets/_Modules/Village/UI/Manage/ManageScreenPanel.cs's
            // class to ManageScreenSurface -- the inventory drops below the floor and this fires.
            const int InventoryFloor = 20;
            if (panels.Count < InventoryFloor)
            {
                failures.Add(Tag + " only " + panels.Count + " panel-like types were discovered under " +
                             "Assets/_Modules (floor " + InventoryFloor + ", measured 35 on 2026-09-06, 32 after " +
                             "WO-1430 retired three doorless panels the same day). The " +
                             "detector is broken or the tree moved; an empty inventory would pass every case " +
                             "vacuously. FAIL, not a skip");
                return;
            }
            log.AppendLine("panel-like types discovered: " + panels.Count);

            // ---- 2. the scene/prefab GUID index (D3) ------------------------
            var referencedGuids = CollectSerialisedScriptGuids(assets, log);

            // ---- 3. the editor-harness corpus (NOT a door, but named on failure)
            var editorStripped = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (Directory.Exists(editorRoot))
            {
                foreach (var f in Directory.GetFiles(editorRoot, "*.cs", SearchOption.AllDirectories))
                {
                    string norm = f.Replace('\\', '/');
                    // The regression suites themselves name every panel; they are not harnesses that
                    // OPEN one, and including them would just add noise to the "harness-only" line.
                    if (norm.IndexOf("/Editor/Regression/", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                    editorStripped[norm] = StripCommentsAndStrings(SafeRead(norm));
                }
            }

            // ---- 4. the door question, per panel ----------------------------
            int passed = 0;
            foreach (var name in SortedKeys(panels))
            {
                var p = panels[name];
                if (Allowlist.Contains(name))
                {
                    log.AppendLine("  ALLOWLISTED " + name);
                    continue;
                }

                var home = HomeSet(name, Path.GetFileName(p.Path));
                var word = new Regex(@"\b" + Regex.Escape(name) + @"\b", RegexOptions.Compiled);
                var selfDecl = new Regex(@"\bclass\s+" + Regex.Escape(name) + @"\b", RegexOptions.Compiled);

                var d1 = new List<string>();          // real doors
                var rootedSat = new List<string>();   // D2
                var deadSat = new List<string>();     // home-set files that name it but are not rooted
                var harness = new List<string>();     // DevTools + Assets/Editor only

                foreach (var kv in stripped)
                {
                    if (string.Equals(kv.Key, p.Path, StringComparison.OrdinalIgnoreCase)) continue;
                    if (!word.IsMatch(kv.Value)) continue;
                    if (!HasNonDeclarationHit(kv.Value, word, selfDecl)) continue;

                    string file = Path.GetFileName(kv.Key);
                    bool isDevTools = kv.Key.IndexOf("/_Modules/DevTools/", StringComparison.OrdinalIgnoreCase) >= 0;
                    if (isDevTools) { harness.Add(file); continue; }

                    if (home.Contains(file))
                    {
                        if (kv.Value.IndexOf("RuntimeInitializeOnLoadMethod", StringComparison.Ordinal) >= 0)
                            rootedSat.Add(file);
                        else
                            deadSat.Add(file);
                        continue;
                    }
                    d1.Add(file);
                }

                foreach (var kv in editorStripped)
                {
                    if (!word.IsMatch(kv.Value)) continue;
                    if (!HasNonDeclarationHit(kv.Value, word, selfDecl)) continue;
                    harness.Add(Path.GetFileName(kv.Key));
                }

                bool d3 = false;
                string guid = ScriptGuid(p.Path);
                if (!string.IsNullOrEmpty(guid) && referencedGuids.Contains(guid)) d3 = true;

                // CASE 1  [panel-has-a-door]
                // ** the case that would have caught the BarracksPanel defect **
                // REVERT RECIPE (RED): in Assets/_Modules/Village/UI/Manage/HeartPanelBootstrap.cs,
                // delete the [RuntimeInitializeOnLoadMethod(...)] attribute line above Install().
                // HeartPanel's only root is that attribute, so it immediately reports doorless --
                // which is the truth: nothing else in the tree would ever create it.
                if (d1.Count == 0 && rootedSat.Count == 0 && !d3)
                {
                    if (harness.Count > 0)
                    {
                        // CASE 1b  [panel-door-is-harness-only] -- the exclusion, made loud.
                        // REVERT RECIPE (RED): same as case 1; a panel whose only remaining
                        // constructor is AutoPilotDriver/UICaptureLaunch lands here instead.
                        failures.Add(Tag + " [panel-door-is-harness-only] " + name + " (" + p.Rel + ") is " +
                                     "constructed ONLY by capture/autopilot harnesses (" + Join(harness) + "). No " +
                                     "production file outside its own View/VM loop opens it, no scene or prefab " +
                                     "references its script GUID " + Describe(guid) + ", so no player can reach it. " +
                                     "A harness that AddComponents every panel so it can be photographed is not a door");
                    }
                    else
                    {
                        failures.Add(Tag + " [panel-has-a-door] " + name + " (" + p.Rel + ") has NO door: no " +
                                     "production .cs outside its home set names it" +
                                     (deadSat.Count > 0
                                        ? " (its own View/VM loop does - " + Join(deadSat) + " - which is the " +
                                          "BarracksPanel shape, not a door)"
                                        : "") +
                                     ", no [RuntimeInitializeOnLoadMethod] bootstrap installs it, and no scene or " +
                                     "prefab references its script GUID " + Describe(guid) + ". It is a built system " +
                                     "no player action can open");
                    }
                    continue;
                }

                // CASE 2  [panel-script-guid-readable]
                // The GUID half of D3 must remain askable: if a .cs.meta loses its guid line the D3
                // arm silently degrades to "never a door" and case 1 starts firing for the wrong reason.
                // REVERT RECIPE (RED): delete the `guid:` line from
                // Assets/_Modules/Village/UI/Manage/ManageScreenPanel.cs.meta.
                if (string.IsNullOrEmpty(guid))
                    failures.Add(Tag + " [panel-script-guid-readable] " + name + " (" + p.Rel + ") has no readable " +
                                 "guid in its .cs.meta, so the scene/prefab arm of the door test cannot be asked " +
                                 "for it. Unity serialises components by GUID; without it a scene-wired panel " +
                                 "would read as doorless");

                passed++;
                log.AppendLine("  OK " + name + " doors=" + Join(d1) + " rootedBootstrap=" + Join(rootedSat) +
                               (d3 ? " sceneOrPrefab=yes" : ""));
            }

            log.AppendLine("panels with a door: " + passed + "/" + (panels.Count - Allowlist.Count) +
                           "  scenes+prefabs indexed for script GUIDs: " + referencedGuids.Count + " distinct");
        }

        // =====================================================================
        // Helpers
        // =====================================================================

        /// <summary>Every script GUID serialised by any .unity/.prefab under Assets/ (m_Script rows).</summary>
        private static HashSet<string> CollectSerialisedScriptGuids(string assets, StringBuilder log)
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var rx = new Regex(@"m_Script:\s*\{fileID:\s*11500000,\s*guid:\s*([0-9a-fA-F]{32})", RegexOptions.Compiled);
            int files = 0;
            foreach (var pattern in new[] { "*.unity", "*.prefab" })
            {
                string[] found;
                try { found = Directory.GetFiles(assets, pattern, SearchOption.AllDirectories); }
                catch (Exception ex) { log.AppendLine("  GUID index: " + pattern + " walk threw " + ex.GetType().Name); continue; }
                foreach (var f in found)
                {
                    files++;
                    string body = SafeRead(f.Replace('\\', '/'));
                    foreach (Match m in rx.Matches(body)) set.Add(m.Groups[1].Value.ToLowerInvariant());
                }
            }
            log.AppendLine("  GUID index built from " + files + " scene/prefab files");
            return set;
        }

        /// <summary>The guid line out of &lt;file&gt;.meta, lowercased; empty when unreadable.</summary>
        private static string ScriptGuid(string csPath)
        {
            string meta = csPath + ".meta";
            if (!File.Exists(meta)) return string.Empty;
            var m = Regex.Match(SafeRead(meta), @"guid:\s*([0-9a-fA-F]{32})");
            return m.Success ? m.Groups[1].Value.ToLowerInvariant() : string.Empty;
        }

        private static string Describe(string guid)
        {
            return string.IsNullOrEmpty(guid) ? "(guid unreadable)" : "(guid " + guid + ")";
        }

        /// <summary>
        /// The file names that belong to P's own View/VM/Bootstrap loop and therefore
        /// cannot vouch for P. See the header for why this set exists.
        /// </summary>
        private static HashSet<string> HomeSet(string name, string ownFile)
        {
            string stem = name;
            if (stem.EndsWith("PanelMvvm", StringComparison.Ordinal)) stem = stem.Substring(0, stem.Length - "PanelMvvm".Length);
            else if (stem.EndsWith("Panel", StringComparison.Ordinal)) stem = stem.Substring(0, stem.Length - "Panel".Length);

            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                ownFile,
                name + ".cs", name + "VM.cs", name + "Bootstrap.cs",
            };
            if (!string.IsNullOrEmpty(stem))
            {
                set.Add(stem + "VM.cs");
                set.Add(stem + "Panel.cs");
                set.Add(stem + "PanelVM.cs");
                set.Add(stem + "Bootstrap.cs");
                set.Add(stem + "PanelBootstrap.cs");
                set.Add(stem + "PanelMvvmBootstrap.cs");
            }
            return set;
        }

        /// <summary>
        /// True when the file names the type on a line that is NOT a declaration of it.
        /// Declaration lines are excluded so a `partial class` split across files
        /// (HeroInventoryController) cannot vouch for itself.
        /// </summary>
        private static bool HasNonDeclarationHit(string strippedBody, Regex word, Regex selfDecl)
        {
            foreach (var line in strippedBody.Split('\n'))
            {
                if (!word.IsMatch(line)) continue;
                if (selfDecl.IsMatch(line)) continue;
                return true;
            }
            return false;
        }

        /// <summary>
        /// Removes // line comments, /* */ block comments and "string literals" so a name
        /// mentioned in prose or in a Debug.Log message is never read as a reference.
        /// Char literals and verbatim strings are handled conservatively (a stray quote
        /// only ever blanks MORE text, never less -- this can hide a door, which turns
        /// into a loud FAIL, never into a silent pass).
        /// </summary>
        private static string StripCommentsAndStrings(string src)
        {
            if (string.IsNullOrEmpty(src)) return string.Empty;
            var sb = new StringBuilder(src.Length);
            int i = 0, n = src.Length;
            while (i < n)
            {
                char c = src[i];
                if (c == '/' && i + 1 < n && src[i + 1] == '/')
                {
                    while (i < n && src[i] != '\n') i++;
                }
                else if (c == '/' && i + 1 < n && src[i + 1] == '*')
                {
                    i += 2;
                    while (i + 1 < n && !(src[i] == '*' && src[i + 1] == '/'))
                    {
                        if (src[i] == '\n') sb.Append('\n');   // keep line structure
                        i++;
                    }
                    i += 2;
                }
                else if (c == '"')
                {
                    i++;
                    while (i < n && src[i] != '"')
                    {
                        if (src[i] == '\\') i++;
                        if (i < n && src[i] == '\n') sb.Append('\n');
                        i++;
                    }
                    i++;
                    sb.Append(' ');
                }
                else
                {
                    sb.Append(c);
                    i++;
                }
            }
            return sb.ToString();
        }

        private static string SafeRead(string path)
        {
            try { return File.ReadAllText(path); }
            catch { return string.Empty; }
        }

        private static string ToRel(string abs, string assets)
        {
            return abs.StartsWith(assets, StringComparison.OrdinalIgnoreCase)
                ? "Assets" + abs.Substring(assets.Length)
                : abs;
        }

        private static string Join(List<string> items)
        {
            if (items == null || items.Count == 0) return "none";
            var uniq = new List<string>();
            foreach (var s in items) if (!uniq.Contains(s)) uniq.Add(s);
            uniq.Sort(StringComparer.Ordinal);
            if (uniq.Count > 6) uniq.RemoveRange(6, uniq.Count - 6);
            return string.Join(",", uniq.ToArray());
        }

        private static List<string> SortedKeys(Dictionary<string, PanelType> d)
        {
            var keys = new List<string>(d.Keys);
            keys.Sort(StringComparer.Ordinal);
            return keys;
        }
    }
}

// =============================================================================
// HelpMenuEntryRegression [help-menu-entry] (WO-882) - the Help menu can never
// ship a blank, label-less button again.
// -----------------------------------------------------------------------------
// Assembly: DeNelle.EditorRegression. Namespace: DeNelle.Editor.Regression.
//
// WHAT BROKE (measured off docs/ui-review/screens-2026-08-04/HelpMenu_2340x1080.png):
// the menu read "Report a Bug / Controls / [blank] / Close". Two independent
// defects produced label-less rows:
//
//   D1 CLIPPED ROW - the WO-795 scroll well is fraction-anchored to the modal
//      body, so its height (395 screen px) is not a whole multiple of the row
//      pitch (146 px row + 20 px gap). The RectMask2D cut row 3 at 36 of its 146
//      px: plate and top bevel drew, the vertically-centred label did not. Pixel
//      proof: rows at y 281-427 and 447-592 are 146 px; the third band is
//      y 612-648 and is the same grey face, then the mask ends.
//
//   D2 INVISIBLE LABEL - HelpMenu.cs forced ElarionUi.Ink (0.137/0.098/0.055,
//      near-black) onto the "Dev Tools" label. That override was correct when
//      ObsidianButtonColor.Yellow meant a GOLD plate; since 2026-07-16
//      ElarionUiKitObsidian.ObsidianButtonSpriteName resolves EVERY colour to
//      "button<style>_gray" (face RGB ~50-57), so it painted dark ink on a dark
//      grey button - a genuinely label-less row, and a DEV-ONLY one.
//
// THE OWNER'S RULING: "the VM filters out the unavailable (dev-only) entry so the
// View never builds a blank button." So the ENTRY LIST is VM state (HelpMenuVM),
// and the View is layout/render only. This oracle pins BOTH halves so a FUTURE
// dead/dev-only/blank entry fails HERE instead of shipping as a blank button:
//
//   1 [vm-filter]  Construct HelpMenuVM with an injected dev context (a RELEASE
//                  build simulated from the editor, where UNITY_EDITOR keeps the
//                  dev rows compiled in) and assert EVERY emitted entry is
//                  renderable - non-empty id, non-null command, non-blank
//                  printable-ASCII label - and that dev-only entries are ABSENT
//                  outside a dev context and the gated grant row is absent until
//                  its gate opens.
//   2 [view-binds] Source law on HelpMenu.cs: it stamps rows from vm.Entries and
//                  contains NO literal-label AddColumnButton call. A literal row
//                  is exactly how an unvetted entry bypasses the VM filter, so it
//                  is banned outright.
//   3 [whole-rows] Source + numeric law: the well carries ScrollWellRowSnap, the
//                  row height is at or above the kit touch floor (MinTouchPx), and
//                  the overflow hint is a FIXED-pixel band at least one FontLabel
//                  line box tall (never a fraction of the parent).
//
// HelpMenuVM / HelpMenu live in DeNelle.HUD, which this assembly does NOT
// reference, so every touch is by REFLECTION (the EchoCardLayoutRegression recipe).
//
// Markers: HELP_MENU_ENTRY_OK / HELP_MENU_ENTRY_FAIL.
// Standalone: run-unity-method DeNelle.Editor.Regression.HelpMenuEntryRegression.RunAll
// Covenant contract Run(out reason) is DataRegression-shaped; wiring into
// DataRegression.RunAll is left to the committer (that file is lane-fenced):
//   DeNelle.Core.Diagnostics.Guard.Try("Regression", "help-menu-entry suite", () => { if (!DeNelle.Editor.Regression.HelpMenuEntryRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[help-menu-entry] " + r); });
// =============================================================================
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using UnityEngine;

namespace DeNelle.Editor.Regression
{
    public static class HelpMenuEntryRegression
    {
        private const string ViewSrc = "Assets/_Modules/HUD/HelpMenu.cs";
        private const string VmSrc = "Assets/_Modules/HUD/HelpMenuVM.cs";

        private const string VmType = "DeNelle.HUD.HelpMenuVM";
        private const string ViewType = "DeNelle.HUD.HelpMenu";
        private const string KitType = "DeNelle.Core.UI.ElarionUiKit";
        private const string PaletteType = "DeNelle.Core.UI.ElarionUi";

        // The TMP line box multiplier a fixed text band must clear (~1.25em).
        private const float LineBoxMul = 1.25f;

        public static void RunAll()
        {
            if (Run(out string reason)) Debug.Log("HELP_MENU_ENTRY_OK - " + reason);
            else Debug.LogError("HELP_MENU_ENTRY_FAIL: " + reason);
        }

        /// <summary>Covenant contract (DataRegression-shaped). Never throws.</summary>
        public static bool Run(out string reason)
        {
            var failures = new List<string>();
            var notes = new List<string>();
            try
            {
                Case(failures, "vm-filter", () => Case1_VmNeverEmitsUnrenderable(failures, notes));
                Case(failures, "view-binds", () => Case2_ViewStampsOnlyTheVmList(failures, notes));
                Case(failures, "whole-rows", () => Case3_WholeRowsAndFixedBands(failures, notes));
                Case(failures, "tester-open", () => Case4_TesterBuildOpensDevTools(failures, notes));
            }
            catch (Exception ex)
            {
                failures.Add("[suite] THREW " + ex.GetType().Name + ": " + ex.Message);
            }

            string noteStr = notes.Count > 0 ? " [notes: " + string.Join("; ", notes.ToArray()) + "]" : "";
            if (failures.Count == 0)
            {
                reason = "HELP MENU ENTRY OK - the VM emits only renderable, available entries " +
                         "(dev-only rows absent outside a dev context), the View stamps that list " +
                         "verbatim with no literal rows, and the well is snapped to WHOLE rows so " +
                         "the mask can never cut across a label" + noteStr;
                return true;
            }
            reason = "help-menu-entry FAIL x" + failures.Count + ": " + string.Join(" | ", failures.ToArray()) + noteStr;
            return false;
        }

        private static void Case(List<string> failures, string name, Action body)
        {
            try { body(); }
            catch (Exception ex) { failures.Add("[" + name + "] THREW " + ex.GetType().Name + ": " + ex.Message); }
        }

        // =====================================================================
        //  CASE 1 - the VM never offers an entry the View cannot render
        // =====================================================================
        private static void Case1_VmNeverEmitsUnrenderable(List<string> failures, List<string> notes)
        {
            Type vm = FindType(VmType);
            if (vm == null)
            {
                failures.Add("[vm-filter] " + VmType + " not found - the Help menu ViewModel was renamed " +
                             "or removed; re-point this oracle (it is the ONLY guard on the entry list)");
                return;
            }

            Type hostType = vm.GetNestedType("IHost", BindingFlags.Public);
            if (hostType == null)
            {
                failures.Add("[vm-filter] HelpMenuVM.IHost not found - the injectable seam is gone, so a " +
                             "release build can no longer be simulated headlessly");
                return;
            }

            ConstructorInfo ctor = vm.GetConstructor(new[] { hostType, typeof(bool), typeof(bool) });
            if (ctor == null)
            {
                failures.Add("[vm-filter] HelpMenuVM(IHost, bool devContext, bool devUnlocked) not found - " +
                             "the oracle cannot inject a dev context; keep this ctor");
                return;
            }

            PropertyInfo entriesProp = vm.GetProperty("Entries", BindingFlags.Public | BindingFlags.Instance);
            if (entriesProp == null)
            {
                failures.Add("[vm-filter] HelpMenuVM.Entries not found");
                return;
            }

            // (devContext, devUnlocked) -> the four states the menu can be in.
            var release = ReadEntries(ctor, entriesProp, false, false, "release", failures);
            var releaseUnlocked = ReadEntries(ctor, entriesProp, false, true, "release+unlock", failures);
            var dev = ReadEntries(ctor, entriesProp, true, false, "dev", failures);
            var devUnlocked = ReadEntries(ctor, entriesProp, true, true, "dev+unlock", failures);

            if (release == null || dev == null || devUnlocked == null || releaseUnlocked == null) return;

            // -- THE INVARIANT: nothing unrenderable is ever emitted, in ANY state. --
            AssertAllRenderable(release, "release", failures);
            AssertAllRenderable(releaseUnlocked, "release+unlock", failures);
            AssertAllRenderable(dev, "dev", failures);
            AssertAllRenderable(devUnlocked, "dev+unlock", failures);

            if (release.Count == 0)
                failures.Add("[vm-filter] the RELEASE entry list is EMPTY - the filter is now eating every row");

            // -- Dev-only entries must not be OFFERED outside a dev context. --------
            foreach (var e in release)
                if (e.Id.StartsWith("dev_", StringComparison.OrdinalIgnoreCase))
                    failures.Add("[vm-filter] release build still offers dev-only entry '" + e.Id + "' (" + e.Label +
                                 ") - a dev row in a store build is exactly WO-882's blank slot");
            foreach (var e in releaseUnlocked)
                if (e.Id.StartsWith("dev_", StringComparison.OrdinalIgnoreCase))
                    failures.Add("[vm-filter] a persisted dev unlock leaked dev entry '" + e.Id +
                                 "' into a RELEASE build - IsDevContext must win over the unlock");

            // -- The gated grant row appears only once its gate opens. --------------
            if (Has(dev, "dev_grant"))
                failures.Add("[vm-filter] 'dev_grant' is offered before the 5-tap unlock - a gated entry must " +
                             "not be emitted (the View used to build it and SetActive(false) it, which is the " +
                             "banned View-side filter)");
            if (!Has(devUnlocked, "dev_grant"))
                notes.Add("dev_grant absent even after unlock (compile-stripped in this build?)");

            // -- Ids are unique (a duplicate id means two rows share one identity). --
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var e in devUnlocked)
                if (!seen.Add(e.Id))
                    failures.Add("[vm-filter] duplicate entry id '" + e.Id + "'");

            // -- Source law: the filter really is the emission gate. ----------------
            string vmText = ReadSource(VmSrc, failures, "[vm-filter]");
            if (vmText != null)
            {
                if (vmText.IndexOf("IsRenderable", StringComparison.Ordinal) < 0)
                    failures.Add("[vm-filter] HelpMenuVM no longer has an IsRenderable gate - the blank-button " +
                                 "guard was deleted");
                if (!Regex.IsMatch(vmText, @"if\s*\(\s*!\s*\w+\.IsRenderable\s*\)\s*continue"))
                    failures.Add("[vm-filter] HelpMenuVM.Rebuild no longer SKIPS unrenderable candidates " +
                                 "(`if (!entry.IsRenderable) continue;`) - the gate is decorative");
                if (!Regex.IsMatch(vmText, @"DevOnly\s*&&\s*!\s*_devContext"))
                    failures.Add("[vm-filter] HelpMenuVM no longer drops DevOnly candidates outside a dev " +
                                 "context - the owner's WO-882 ruling");
            }

            notes.Add("release=" + release.Count + " dev=" + dev.Count + " dev+unlock=" + devUnlocked.Count + " entries");
        }

        private struct Row
        {
            public string Id;
            public string Label;
            public bool HasCommand;
            public bool Renderable;
        }

        private static List<Row> ReadEntries(ConstructorInfo ctor, PropertyInfo entriesProp,
            bool devContext, bool devUnlocked, string stateName, List<string> failures)
        {
            object vmInstance;
            try { vmInstance = ctor.Invoke(new object[] { null, devContext, devUnlocked }); }
            catch (Exception ex)
            {
                failures.Add("[vm-filter] constructing HelpMenuVM(" + stateName + ") THREW " +
                             ex.GetType().Name + ": " + ex.Message +
                             " - the VM must build its list with a NULL host (no Unity, no services)");
                return null;
            }

            object list = entriesProp.GetValue(vmInstance, null);
            if (list == null)
            {
                failures.Add("[vm-filter] HelpMenuVM.Entries is NULL in state '" + stateName + "' - it must " +
                             "never be null (the View foreaches it)");
                return null;
            }

            var rows = new List<Row>();
            var seq = list as IEnumerable;
            if (seq == null)
            {
                failures.Add("[vm-filter] HelpMenuVM.Entries is not enumerable in state '" + stateName + "'");
                return null;
            }

            foreach (object boxed in seq)
            {
                if (boxed == null)
                {
                    rows.Add(new Row { Id = "<null>", Label = null, HasCommand = false, Renderable = false });
                    continue;
                }
                Type et = boxed.GetType();
                var row = new Row();
                row.Id = FieldString(et, boxed, "Id");
                row.Label = FieldString(et, boxed, "Label");
                FieldInfo cmd = et.GetField("Command", BindingFlags.Public | BindingFlags.Instance);
                row.HasCommand = cmd != null && cmd.GetValue(boxed) != null;
                PropertyInfo rp = et.GetProperty("IsRenderable", BindingFlags.Public | BindingFlags.Instance);
                row.Renderable = rp != null && rp.GetValue(boxed, null) is bool && (bool)rp.GetValue(boxed, null);
                rows.Add(row);
            }
            return rows;
        }

        /// <summary>Re-derives renderability INDEPENDENTLY of Entry.IsRenderable, so weakening the
        /// VM's own predicate cannot silence this oracle.</summary>
        private static void AssertAllRenderable(List<Row> rows, string stateName, List<string> failures)
        {
            for (int i = 0; i < rows.Count; i++)
            {
                Row r = rows[i];
                string where = "[vm-filter] " + stateName + " entry #" + i + " (id='" + (r.Id ?? "<null>") + "')";

                if (string.IsNullOrEmpty(r.Id))
                    failures.Add(where + " has no id");
                if (!r.HasCommand)
                    failures.Add(where + " has a NULL command - a tappable row that does nothing");
                if (string.IsNullOrEmpty(r.Label) || r.Label.Trim().Length == 0)
                {
                    failures.Add(where + " has a BLANK label - this IS the WO-882 blank button; the VM must " +
                                 "not offer an entry whose label the View cannot draw");
                    continue;
                }
                for (int c = 0; c < r.Label.Length; c++)
                {
                    char ch = r.Label[c];
                    if (ch < ' ' || ch > '~')
                    {
                        failures.Add(where + " label '" + r.Label + "' has a non-ASCII char (U+" +
                                     ((int)ch).ToString("X4") + ") - it renders as tofu on the device");
                        break;
                    }
                }
                if (!r.Renderable)
                    failures.Add(where + " was emitted with IsRenderable=false - the VM emitted an entry it " +
                                 "itself considers unrenderable");
            }
        }

        private static bool Has(List<Row> rows, string id)
        {
            foreach (var r in rows)
                if (string.Equals(r.Id, id, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        // =====================================================================
        //  CASE 2 - the View stamps the VM list and nothing else
        // =====================================================================
        private static void Case2_ViewStampsOnlyTheVmList(List<string> failures, List<string> notes)
        {
            string text = ReadSource(ViewSrc, failures, "[view-binds]");
            if (text == null) return;

            if (text.IndexOf("_vm.Entries", StringComparison.Ordinal) < 0)
                failures.Add("[view-binds] HelpMenu.cs no longer renders from _vm.Entries - the View is " +
                             "deciding its own rows again (the WO-882 regression)");
            if (text.IndexOf("HelpMenuVM.CreateDefault", StringComparison.Ordinal) < 0)
                failures.Add("[view-binds] HelpMenu.cs no longer binds HelpMenuVM.CreateDefault");

            // A literal-label row is the bypass that ships blank/dead buttons. Ban it.
            var literalRow = new Regex("AddColumnButton\\s*\\(\\s*[^,()]+,\\s*\"");
            var m = literalRow.Match(text);
            if (m.Success)
                failures.Add("[view-binds] HelpMenu.cs stamps a row with a LITERAL label (" +
                             Snippet(text, m.Index) + ") - every row must come from a HelpMenuVM candidate " +
                             "so the availability filter can drop it; a literal row is unfilterable");

            // The old View-side hide (build the row, then SetActive(false)) is the banned filter.
            if (Regex.IsMatch(text, @"_grantResourcesBtn"))
                failures.Add("[view-binds] HelpMenu.cs still holds _grantResourcesBtn - the View is building a " +
                             "row it then hides; the VM must not offer it at all");

            // D2: the dark-ink label override on a grey plate (an invisible label).
            if (Regex.IsMatch(text, @"\.color\s*=\s*ElarionUi\.Ink"))
                failures.Add("[view-binds] HelpMenu.cs forces ElarionUi.Ink onto a button label. Since " +
                             "2026-07-16 every ObsidianButtonColor resolves to the GREY plate, so dark ink is " +
                             "invisible - a label-less button. Let the kit own label ink.");
        }

        // =====================================================================
        //  CASE 3 - whole rows + fixed-pixel bands (no half-row, no fractions)
        // =====================================================================
        private static void Case3_WholeRowsAndFixedBands(List<string> failures, List<string> notes)
        {
            string text = ReadSource(ViewSrc, failures, "[whole-rows]");
            if (text != null)
            {
                if (text.IndexOf("ScrollWellRowSnap", StringComparison.Ordinal) < 0)
                    failures.Add("[whole-rows] HelpMenu.cs no longer snaps its scroll well to whole rows - the " +
                                 "mask will cut across a row again and draw a label-less button (WO-882 D1)");
                if (text.IndexOf("HintBandPx", StringComparison.Ordinal) < 0)
                    failures.Add("[whole-rows] the fixed-pixel overflow hint band is gone - rows scrolled out of " +
                                 "view become invisible with no text-encoded cue");
            }

            Type view = FindType(ViewType);
            Type kit = FindType(KitType);
            Type palette = FindType(PaletteType);
            if (view == null) { failures.Add("[whole-rows] " + ViewType + " not found"); return; }
            if (kit == null) { failures.Add("[whole-rows] " + KitType + " not found"); return; }

            float minTouch = NumberMember(kit, "MinTouchPx", failures, "[whole-rows]");
            float rowH = NumberMember(view, "RowHeightPx", failures, "[whole-rows]");
            float hintH = NumberMember(view, "HintBandPx", failures, "[whole-rows]");
            float gap = NumberMember(view, "RowGapPx", failures, "[whole-rows]");

            if (rowH > 0f && minTouch > 0f && rowH < minTouch)
                failures.Add("[whole-rows] HelpMenu.RowHeightPx (" + rowH + ") is under the kit touch floor (" +
                             minTouch + ") - a sub-floor row is untappable on the owner's phone");
            if (gap <= 0f)
                failures.Add("[whole-rows] HelpMenu.RowGapPx must be a positive fixed gap (got " + gap + ")");

            if (palette != null && hintH > 0f)
            {
                float fontLabel = NumberMember(palette, "FontLabel", failures, "[whole-rows]");
                if (fontLabel > 0f && hintH < fontLabel * LineBoxMul)
                    failures.Add("[whole-rows] HelpMenu.HintBandPx (" + hintH + ") is under one FontLabel line " +
                                 "box (" + (fontLabel * LineBoxMul) + ") - the hint text would be culled");
            }

            notes.Add("rowH=" + rowH + " gap=" + gap + " hint=" + hintH + " minTouch=" + minTouch);
        }

        // Seeker 365962 22:37:37: Help "Dev Tools" compiled in under TESTER_BUILD, then
        // AdminOverlay.SetOpen blocked on the store wallet gate (Debug.isDebugBuild false).
        // Help had already Closed, so the tap bounced to the HUD.
        private static void Case4_TesterBuildOpensDevTools(List<string> failures, List<string> notes)
        {
            string src = ReadSource("Assets/_Modules/HUD/AdminOverlay.cs", failures, "[tester-open]");
            if (src == null) return;
            int blocked = src.IndexOf("DevPanel open BLOCKED", StringComparison.Ordinal);
            if (blocked < 0)
            {
                failures.Add("[tester-open] AdminOverlay lost the owner-wallet block warn; re-point this oracle");
                return;
            }
            int guard = src.IndexOf("#if !TESTER_BUILD", StringComparison.Ordinal);
            int endif = guard >= 0 ? src.IndexOf("#endif", guard, StringComparison.Ordinal) : -1;
            if (guard < 0 || endif < 0 || blocked < guard || blocked > endif)
                failures.Add("[tester-open] SetOpen still wallet-blocks TESTER_BUILD APKs — Help Dev Tools will bounce to the HUD");
            else
                notes.Add("tester SetOpen bypass present");
        }

        // =====================================================================
        //  helpers
        // =====================================================================
        private static string FieldString(Type t, object boxed, string name)
        {
            FieldInfo f = t.GetField(name, BindingFlags.Public | BindingFlags.Instance);
            if (f == null) return null;
            return f.GetValue(boxed) as string;
        }

        private static string ReadSource(string relPath, List<string> failures, string tag)
        {
            string full = Path.Combine(Path.GetDirectoryName(Application.dataPath) ?? ".", relPath);
            try
            {
                if (!File.Exists(full)) { failures.Add(tag + " source not found: " + relPath); return null; }
                return File.ReadAllText(full);
            }
            catch (Exception ex)
            {
                failures.Add(tag + " could not read " + relPath + " (" + ex.Message + ")");
                return null;
            }
        }

        private static string Snippet(string text, int index)
        {
            int start = Math.Max(0, index - 10);
            int len = Math.Min(70, text.Length - start);
            return text.Substring(start, len).Replace("\r", " ").Replace("\n", " ");
        }

        /// <summary>Reads a public const / static readonly numeric member (float or int) by name.</summary>
        private static float NumberMember(Type t, string name, List<string> failures, string tag)
        {
            FieldInfo f = t.GetField(name, BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy);
            if (f == null)
            {
                failures.Add(tag + " " + t.Name + "." + name + " not found - re-point this oracle");
                return -1f;
            }
            object v = f.GetValue(null);
            if (v is float) return (float)v;
            if (v is int) return (int)v;
            if (v is double) return (float)(double)v;
            failures.Add(tag + " " + t.Name + "." + name + " is not numeric (" + (v == null ? "null" : v.GetType().Name) + ")");
            return -1f;
        }

        private static Type FindType(string fullName)
        {
            Type t = Type.GetType(fullName);
            if (t != null) return t;
            var asms = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < asms.Length; i++)
            {
                try
                {
                    t = asms[i].GetType(fullName, false);
                    if (t != null) return t;
                }
                catch { /* dynamic/unloadable assembly - skip */ }
            }
            return null;
        }
    }
}

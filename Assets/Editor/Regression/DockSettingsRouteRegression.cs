// =============================================================================
// DockSettingsRouteRegression [dock-settings-route] (WO-1399) - the gear dock's row
// labelled "Settings" opens SETTINGS, and Help is a row inside it.
// -----------------------------------------------------------------------------
// WHAT BROKE (docs/qa/UI_SCREEN_GRAPH_2026-09-04.md:128-129, dead end 8): the dock row
// "Settings" called DeNelle.HUD.HelpMenu.Instance.ToggleOverlay() - the bug-report /
// Controls / Credits menu - because DeNelle.HUD cannot reference DeNelle.Settings
// (DeNelle.HUD.asmdef: Core + Data only; DeNelle.Settings.asmdef: Core only). The real
// SettingsController (quality / difficulty / wallet / privacy / offline) was reachable
// ONLY through Pause -> Settings: a door hidden behind another door.
//
// THE FIX SHAPE THIS PINS (one mechanism, PauseGate's twin):
//   dock "Settings" -> SettingsGate.RequestOpen("dock")   (Core, event-based)
//                   -> SettingsController subscribes SettingsOpenRequested -> Open()
//   Settings row "Help" -> PanelRouter.Open(PanelId.Help) -> HelpMenu (registers the id)
//   the dock grid stays 2 columns x 3 rows = six cells (no seventh row for Help).
//
// CASES (source law - the three assemblies cannot be exercised together in an
// EditMode suite without a scene, and the route is a compile-time wiring fact):
//   1 [dock-route]     HudKitController.OpenSettings calls SettingsGate.RequestOpen and the
//                      file no longer references HelpMenu.Instance. The "Settings" row line
//                      itself is unchanged (label -> OpenSettings).
//   2 [gate-shape]     Core SettingsGate exists with the Action<string> event, RequestOpen, and
//                      FlowTrace.Fail on the no-subscriber branch (never a silent no-op).
//   3 [gate-subscriber] SettingsController subscribes AND unsubscribes SettingsOpenRequested
//                      and its handler calls Open().
//   4 [help-row]       SettingsController builds a "Help" button whose handler opens
//                      PanelId.Help; PanelId.Help = 25 exists (append-only); HelpMenu
//                      registers and unregisters PanelId.Help against a public Open().
//   5 [six-cells]      AddDockTab is still a 2x3 grid and DockTabCount is still 6.
//   6 [trace-honest]   HelpMenu no longer traces itself as "Settings".
//
// RED-FIRST: on the pre-WO tree case 1 fails (OpenSettings calls HelpMenu.Instance.
// ToggleOverlay), 2 fails (no SettingsGate.cs), 3 and 4 fail (no subscriber, no Help row,
// no PanelId.Help). ONE-LINE MUTATION that reds it today: in HudKitController.OpenSettings
// replace `SettingsGate.RequestOpen("dock");` with
// `DeNelle.HUD.HelpMenu.Instance.ToggleOverlay();` -> [dock-route] fails twice (the call is
// missing and HelpMenu.Instance is back).
//
// Marker: DOCK_SETTINGS_ROUTE_OK / DOCK_SETTINGS_ROUTE_FAIL.
// Standalone: run-unity-method DeNelle.Editor.Regression.DockSettingsRouteRegression.RunAll
// =============================================================================
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEngine;

namespace DeNelle.Editor.Regression
{
    public static class DockSettingsRouteRegression
    {
        private const string HudSrc = "_Modules/HUD/Kit/HudKitController.cs";
        private const string GateSrc = "_Modules/Core/UI/SettingsGate.cs";
        private const string RouterSrc = "_Modules/Core/UI/PanelRouter.cs";
        private const string SettingsSrc = "_Modules/Settings/SettingsController.cs";
        private const string HelpSrc = "_Modules/HUD/HelpMenu.cs";

        public static void RunAll()
        {
            Run(out string reason);
            Debug.Log("[dock-settings-route] " + reason);
        }

        public static bool Run(out string reason)
        {
            var failures = new List<string>();
            string root = Application.dataPath;
            string hud = Read(root, HudSrc, failures);
            string gate = Read(root, GateSrc, failures);
            string router = Read(root, RouterSrc, failures);
            string settings = Read(root, SettingsSrc, failures);
            string help = Read(root, HelpSrc, failures);

            // 1 [dock-route]
            Require(hud, "AddDockTab(_slideDock.panel, dockRow++, \"Settings\", OpenSettings);", failures,
                "[dock-route] the dock's \"Settings\" row no longer routes to OpenSettings");
            string openSettings = Between(hud, "private void OpenSettings()", "\n        private ");
            if (openSettings == null)
                failures.Add("[dock-route] HudKitController.OpenSettings() not found");
            else if (openSettings.IndexOf("SettingsGate.RequestOpen(", StringComparison.Ordinal) < 0)
                failures.Add("[dock-route] OpenSettings does not call SettingsGate.RequestOpen - the row " +
                             "labelled Settings opens something other than Settings");
            if (hud.IndexOf("HelpMenu.Instance", StringComparison.Ordinal) >= 0)
                failures.Add("[dock-route] HudKitController still references HelpMenu.Instance - the dock is " +
                             "opening the Help menu directly again (the WO-1399 defect)");

            // 2 [gate-shape]
            Require(gate, "namespace DeNelle.Core.UI", failures, "[gate-shape] SettingsGate is not in DeNelle.Core.UI");
            Require(gate, "public static event Action<string> SettingsOpenRequested;", failures,
                "[gate-shape] SettingsGate lacks the Action<string> SettingsOpenRequested event");
            Require(gate, "public static void RequestOpen(string source)", failures,
                "[gate-shape] SettingsGate lacks RequestOpen(string source)");
            string requestOpen = MemberBody(gate, "public static void RequestOpen(string source)");
            if (requestOpen == null || requestOpen.IndexOf("FlowTrace.Fail(", StringComparison.Ordinal) < 0)
                failures.Add("[gate-shape] SettingsGate.RequestOpen does not FlowTrace.Fail when no subscriber " +
                             "is attached - a dead Settings row would be silent");

            // 3 [gate-subscriber]
            Require(settings, "SettingsGate.SettingsOpenRequested += OnSettingsOpenRequested;", failures,
                "[gate-subscriber] SettingsController does not subscribe SettingsGate.SettingsOpenRequested");
            Require(settings, "SettingsGate.SettingsOpenRequested -= OnSettingsOpenRequested;", failures,
                "[gate-subscriber] SettingsController never unsubscribes SettingsOpenRequested (leaks across scenes)");
            string handler = MemberBody(settings, "private void OnSettingsOpenRequested(string source)");
            if (handler == null || !Regex.IsMatch(handler, @"(^|[^\w.])Open\(\);"))
                failures.Add("[gate-subscriber] OnSettingsOpenRequested does not call Open()");
            if (handler != null && handler.IndexOf("FlowTrace.Step(\"Settings\", \"opened via SettingsGate from \"", StringComparison.Ordinal) < 0)
                failures.Add("[gate-subscriber] the gate handler lost its 'opened via SettingsGate from <door>' trace");

            // 4 [help-row]
            Require(router, "Help = 25,", failures, "[help-row] PanelId.Help = 25 is missing from the append-only enum");
            if (Regex.Matches(router, @"=\s*25\s*,").Count != 1)
                failures.Add("[help-row] PanelId value 25 is not unique in the enum");
            if (!settings.Contains("LocalizedButton(body, () => SettingsText.Help.Resolve()"))
                failures.Add("[help-row] SettingsController builds no \"Help\" button - Help has no door inside Settings");
            string helpClick = MemberBody(settings, "private void OnHelpClicked()");
            if (helpClick == null || helpClick.IndexOf("PanelRouter.Open(PanelId.Help)", StringComparison.Ordinal) < 0)
                failures.Add("[help-row] SettingsController.OnHelpClicked does not open PanelId.Help");
            else if (helpClick.IndexOf("FlowTrace.Fail(", StringComparison.Ordinal) < 0)
                failures.Add("[help-row] OnHelpClicked swallows a FALSE PanelRouter.Open - a missing HelpMenu would be a silent dead button");
            Require(help, "PanelRouter.Register(PanelId.Help, Open);", failures,
                "[help-row] HelpMenu does not register PanelId.Help");
            Require(help, "PanelRouter.Unregister(PanelId.Help, Open);", failures,
                "[help-row] HelpMenu does not unregister PanelId.Help on destroy");
            Require(help, "public void Open()", failures, "[help-row] HelpMenu has no public Open() for the router");

            // 5 [six-cells]
            string dockTab = Between(hud, "private void AddDockTab(", "\n        private ");
            if (dockTab == null || dockTab.IndexOf("const int columns = 2;", StringComparison.Ordinal) < 0 ||
                dockTab.IndexOf("const int rows = 3;", StringComparison.Ordinal) < 0)
                failures.Add("[six-cells] AddDockTab is no longer the fixed 2x3 grid");
            Require(hud, "DockTabCount = 6", failures, "[six-cells] DockTabCount moved off 6 - a seventh dock row has no cell");
            if (Regex.Matches(hud, "AddDockTab\\(_slideDock\\.panel, dockRow(\\+\\+)?, \"").Count != 6)
                failures.Add("[six-cells] the dock does not stamp exactly six rows (Chat/Leaderboard/Music/Settings/Realm/Pause)");
            if (Regex.IsMatch(hud, "AddDockTab\\(_slideDock\\.panel, dockRow(\\+\\+)?, \"Help\""))
                failures.Add("[six-cells] a \"Help\" dock row was added - Help lives INSIDE Settings, the grid has six cells");

            // 6 [trace-honest]
            if (help.IndexOf("Settings open requested", StringComparison.Ordinal) >= 0)
                failures.Add("[trace-honest] HelpMenu still traces itself as \"Settings open requested\" - the misnomer is back in the evidence");

            if (failures.Count == 0)
            {
                Debug.Log("DOCK_SETTINGS_ROUTE_OK");
                reason = "dock Settings row -> SettingsGate -> SettingsController; Help is a Settings row via PanelId.Help; dock grid 2x3 / six cells";
                return true;
            }
            reason = "dock-settings-route: " + string.Join("; ", failures);
            Debug.LogError("DOCK_SETTINGS_ROUTE_FAIL: " + reason);
            return false;
        }

        private static string Read(string root, string relative, List<string> failures)
        {
            string path = Path.Combine(root, relative);
            if (!File.Exists(path)) { failures.Add("[dock-settings-route] missing " + relative); return string.Empty; }
            try { return File.ReadAllText(path); }
            catch (Exception ex) { failures.Add("[dock-settings-route] unreadable " + relative + ": " + ex.Message); return string.Empty; }
        }

        private static void Require(string text, string needle, List<string> failures, string why)
        {
            if (text.IndexOf(needle, StringComparison.Ordinal) < 0)
                failures.Add(why + " (missing '" + needle + "')");
        }

        /// <summary>The member declared at <paramref name="signature"/> through its matching
        /// close brace (brace-depth scan from the first open brace after the signature); null
        /// when the signature is absent. Comments/strings carrying braces inside the member
        /// would skew the depth, so keep the pinned members brace-free in text - they are.</summary>
        private static string MemberBody(string text, string signature)
        {
            int a = text.IndexOf(signature, StringComparison.Ordinal);
            if (a < 0) return null;
            int depth = 0;
            bool entered = false;
            for (int i = a; i < text.Length; i++)
            {
                char c = text[i];
                if (c == '{') { depth++; entered = true; }
                else if (c == '}') { depth--; if (entered && depth == 0) return text.Substring(a, i + 1 - a); }
            }
            return text.Substring(a);
        }

        /// <summary>Text from the first occurrence of <paramref name="start"/> up to the next
        /// <paramref name="end"/> after it; null when <paramref name="start"/> is absent.</summary>
        private static string Between(string text, string start, string end)
        {
            int a = text.IndexOf(start, StringComparison.Ordinal);
            if (a < 0) return null;
            int b = text.IndexOf(end, a + start.Length, StringComparison.Ordinal);
            return b < 0 ? text.Substring(a) : text.Substring(a, b - a);
        }
    }
}

// =============================================================================
// HomesSwitcherRegression - WO-1884 Homes chip / castle <-> owned-town switcher.
// Markers: HOMES_SWITCHER_OK / HOMES_SWITCHER_FAIL.  Tag: [homes-switcher]
// -----------------------------------------------------------------------------
// (A) without an owned base the door is gated absent (Validate / ShouldShowHomesChip)
// (B) a production file outside the panel constructs it (Bootstrap D2)
// (C) HUD files do not `using DeNelle.Village`
// (D) GoCastle is reachable from owned-town Homes source without OwnedTownPanel
//
// Source oracle; NEVER throws. Registered into DataRegression.RunAll.
// =============================================================================

using System;
using System.IO;
using System.Text;
using UnityEngine;

namespace DeNelle.Editor.Regression
{
    /// <summary>WO-1884 oracle: Homes switcher door + HUD assembly edge.</summary>
    public static class HomesSwitcherRegression
    {
        private const string Tag = "[homes-switcher]";
        private const string PanelRel = "_Modules/HUD/HomesSwitcherPanel.cs";
        private const string BootRel = "_Modules/HUD/HomesSwitcherPanelBootstrap.cs";
        private const string KitRel = "_Modules/HUD/Kit/HudKitController.cs";
        private const string DeckRel = "_Modules/HUD/PlayerDeckWorkspace.cs";
        private const string PanelIdRel = "_Modules/Core/UI/PanelRouter.cs";

        public static bool Run(out string reason)
        {
            try
            {
                return RunCore(out reason);
            }
            catch (Exception ex)
            {
                reason = "HOMES_SWITCHER_FAIL " + Tag + " suite THREW: " +
                         ex.GetType().Name + ": " + ex.Message;
                return false;
            }
        }

        private static bool RunCore(out string reason)
        {
            var notes = new StringBuilder();

            if (!CaseA_DoorAbsentWithoutOwnedBase(out string a))
            { reason = "HOMES_SWITCHER_FAIL " + a; return false; }
            notes.Append(a);

            if (!CaseB_ProductionConstructsPanel(out string b))
            { reason = "HOMES_SWITCHER_FAIL " + b; return false; }
            notes.Append("; ").Append(b);

            if (!CaseC_HudNeverUsesVillage(out string c))
            { reason = "HOMES_SWITCHER_FAIL " + c; return false; }
            notes.Append("; ").Append(c);

            if (!CaseD_GoCastleWithoutOwnedTownPanel(out string d))
            { reason = "HOMES_SWITCHER_FAIL " + d; return false; }
            notes.Append("; ").Append(d);

            reason = "HOMES_SWITCHER_OK " + notes;
            Debug.Log(Tag + " " + reason);
            return true;
        }

        // A — chip / open path must gate on OwnedBaseProgression.Validate
        private static bool CaseA_DoorAbsentWithoutOwnedBase(out string note)
        {
            note = null;
            string kit = ReadSource(KitRel);
            string panel = ReadSource(PanelRel);
            if (kit == null) { note = Tag + " [A] missing " + KitRel; return false; }
            if (panel == null) { note = Tag + " [A] missing " + PanelRel; return false; }

            if (kit.IndexOf("ShouldShowHomesChip", StringComparison.Ordinal) < 0 ||
                kit.IndexOf("OwnedBaseProgression.Validate", StringComparison.Ordinal) < 0 ||
                kit.IndexOf("BuildHomesChip", StringComparison.Ordinal) < 0)
            {
                note = Tag + " [A] HudKitController no longer gates the Homes chip on " +
                       "OwnedBaseProgression.Validate / ShouldShowHomesChip / BuildHomesChip";
                return false;
            }
            if (panel.IndexOf("OwnedBaseProgression.Validate", StringComparison.Ordinal) < 0)
            {
                note = Tag + " [A] HomesSwitcherPanel.Open no longer refuses without Validate";
                return false;
            }
            // Realm deck visit button must stay retired so the chip is the one public door.
            string deck = ReadSource(DeckRel);
            if (deck == null) { note = Tag + " [A] missing " + DeckRel; return false; }
            if (deck.IndexOf("OwnedTownReturn", StringComparison.Ordinal) >= 0 ||
                deck.IndexOf("GoOwnedTown()", StringComparison.Ordinal) >= 0)
            {
                note = Tag + " [A] PlayerDeckWorkspace still constructs the Realm visit / GoOwnedTown " +
                       "door — WO-1884 prefers one public Homes door (the chip)";
                return false;
            }
            note = Tag + " [A] Homes door gated on Validate; Realm visit button retired";
            return true;
        }

        // B — Bootstrap (D2) constructs HomesSwitcherPanel outside the panel file
        private static bool CaseB_ProductionConstructsPanel(out string note)
        {
            note = null;
            string boot = ReadSource(BootRel);
            string panelId = ReadSource(PanelIdRel);
            if (boot == null) { note = Tag + " [B] missing " + BootRel; return false; }
            if (panelId == null) { note = Tag + " [B] missing " + PanelIdRel; return false; }

            if (boot.IndexOf("[RuntimeInitializeOnLoadMethod]", StringComparison.Ordinal) < 0 ||
                boot.IndexOf("AddComponent<HomesSwitcherPanel>", StringComparison.Ordinal) < 0)
            {
                note = Tag + " [B] HomesSwitcherPanelBootstrap is not a RuntimeInitializeOnLoadMethod " +
                       "D2 root that AddComponent<HomesSwitcherPanel>s";
                return false;
            }
            if (panelId.IndexOf("Homes = 30", StringComparison.Ordinal) < 0)
            {
                note = Tag + " [B] PanelId.Homes = 30 missing (append-only PanelId required)";
                return false;
            }
            string kit = ReadSource(KitRel);
            if (kit == null || kit.IndexOf("PanelRouter.Open(PanelId.Homes)", StringComparison.Ordinal) < 0)
            {
                note = Tag + " [B] HudKitController Homes chip does not open PanelId.Homes";
                return false;
            }
            note = Tag + " [B] Bootstrap constructs HomesSwitcherPanel; PanelId.Homes wired";
            return true;
        }

        // C — no HUD production file may using DeNelle.Village
        private static bool CaseC_HudNeverUsesVillage(out string note)
        {
            note = null;
            string hudRoot = Path.Combine(Application.dataPath, "_Modules", "HUD");
            if (!Directory.Exists(hudRoot))
            {
                note = Tag + " [C] Assets/_Modules/HUD missing";
                return false;
            }
            var offenders = new StringBuilder();
            foreach (var file in Directory.GetFiles(hudRoot, "*.cs", SearchOption.AllDirectories))
            {
                string src = File.ReadAllText(file);
                // Strip // line comments so tombstone prose cannot false-positive.
                var sb = new StringBuilder(src.Length);
                using (var reader = new StringReader(src))
                {
                    string line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        int cut = line.IndexOf("//", StringComparison.Ordinal);
                        sb.AppendLine(cut >= 0 ? line.Substring(0, cut) : line);
                    }
                }
                string stripped = sb.ToString();
                if (stripped.IndexOf("using DeNelle.Village", StringComparison.Ordinal) >= 0)
                {
                    if (offenders.Length > 0) offenders.Append(", ");
                    offenders.Append(file.Replace('\\', '/').Replace(Application.dataPath.Replace('\\', '/') + "/", "Assets/"));
                }
            }
            if (offenders.Length > 0)
            {
                note = Tag + " [C] HUD file(s) using DeNelle.Village: " + offenders;
                return false;
            }
            note = Tag + " [C] no HUD file uses DeNelle.Village";
            return true;
        }

        // D — GoCastle from HomesSwitcherPanel, not OwnedTownPanel
        private static bool CaseD_GoCastleWithoutOwnedTownPanel(out string note)
        {
            note = null;
            string panel = ReadSource(PanelRel);
            if (panel == null) { note = Tag + " [D] missing " + PanelRel; return false; }
            if (panel.IndexOf("SceneRouter.GoCastle", StringComparison.Ordinal) < 0)
            {
                note = Tag + " [D] HomesSwitcherPanel does not call SceneRouter.GoCastle";
                return false;
            }
            if (panel.IndexOf("SceneRouter.GoOwnedTown", StringComparison.Ordinal) < 0)
            {
                note = Tag + " [D] HomesSwitcherPanel does not call SceneRouter.GoOwnedTown";
                return false;
            }
            if (panel.IndexOf("OwnedTownPanel", StringComparison.Ordinal) >= 0)
            {
                note = Tag + " [D] HomesSwitcherPanel names OwnedTownPanel — WO-1884 must not restore it";
                return false;
            }
            note = Tag + " [D] GoCastle/GoOwnedTown reachable from HomesSwitcherPanel without OwnedTownPanel";
            return true;
        }

        private static string ReadSource(string assetsRelative)
        {
            string path = Path.Combine(Application.dataPath, assetsRelative.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(path)) return null;
            string raw = File.ReadAllText(path);
            // Strip // line comments for source pins (history prose must not count).
            var sb = new StringBuilder(raw.Length);
            using (var reader = new StringReader(raw))
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    int cut = line.IndexOf("//", StringComparison.Ordinal);
                    sb.AppendLine(cut >= 0 ? line.Substring(0, cut) : line);
                }
            }
            return sb.ToString();
        }
    }
}

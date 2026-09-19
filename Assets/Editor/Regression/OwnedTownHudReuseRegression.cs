// =============================================================================
// OwnedTownHudReuseRegression - WO-1876: captured town reuses castle HUD/build.
// Markers: OWNED_TOWN_HUD_OK / OWNED_TOWN_HUD_FAIL.  Tag: [owned-town-hud]
// -----------------------------------------------------------------------------
// Assembly: DeNelle.EditorRegression. Shape: public static bool Run(out string
// reason) - registered into DataRegression.RunAll with ONE line. NEVER throws.
//
// RED-FIRST (WO-1876):
//   A  OwnedTownController still Show()s OwnedTownPanel after reconstruct -> A red
//   B  owned SelectStructure still FindAnyObjectByType<OwnedTownPanel>() -> B red
//   C  CapturedTownStanddown.IsRepairableDamage still treats razed rubble as
//      repairable damage (WO-1872 predicate must stay) -> C red
//
// Also pins: HudContextResolver treats owned town as inVillage (Town context),
// and BuildMode Enter/Exit no longer HideForBuild/Show the panel.
//
// Source pins are COMMENT-STRIPPED (this repo documents history in prose).
// NO PlayMode / scene load — headless DataRegression only.
// =============================================================================

using System;
using System.IO;
using System.Text;
using DeNelle.Core.State;
using DeNelle.Village.World.Camps;

namespace DeNelle.Editor.Regression
{
    /// <summary>
    /// WO-1876 oracle: owned town stays on the castle HUD/build stack; OwnedTownPanel
    /// is not the rebuild door. DataRegression-shaped; NEVER throws.
    /// </summary>
    public static class OwnedTownHudReuseRegression
    {
        private const string ControllerRel = "_Modules/Village/World/Camps/OwnedTownController.cs";
        private const string BuildModeRel = "_Modules/Village/BuildMode/BuildModeController.cs";
        private const string ResolverRel = "_Modules/Core/HudModel/HudContextResolver.cs";
        private const string StanddownRel = "_Modules/Core/State/CapturedTownStanddown.cs";

        public static bool Run(out string reason)
        {
            try
            {
                return RunCore(out reason);
            }
            catch (Exception ex)
            {
                reason = "OWNED_TOWN_HUD_FAIL owned-town-hud suite THREW: " +
                         ex.GetType().Name + ": " + ex.Message;
                return false;
            }
        }

        private static bool RunCore(out string reason)
        {
            var notes = new StringBuilder();

            if (!CaseA_ControllerDoesNotShowPanel(out string a)) { reason = "OWNED_TOWN_HUD_FAIL " + a; return false; }
            notes.Append(a);

            if (!CaseB_SelectStructureStaysOnCastleUi(out string b)) { reason = "OWNED_TOWN_HUD_FAIL " + b; return false; }
            notes.Append("; ").Append(b);

            if (!CaseC_RubbleIsNotRepairableDamage(out string c)) { reason = "OWNED_TOWN_HUD_FAIL " + c; return false; }
            notes.Append("; ").Append(c);

            if (!CaseD_HudContextTownForOwned(out string d)) { reason = "OWNED_TOWN_HUD_FAIL " + d; return false; }
            notes.Append("; ").Append(d);

            if (!CaseE_EnterExitDoNotReshowPanel(out string e)) { reason = "OWNED_TOWN_HUD_FAIL " + e; return false; }
            notes.Append("; ").Append(e);

            reason = "OWNED_TOWN_HUD_OK " + notes;
            return true;
        }

        // A — reconstruct path must not call panel.Show / HoldThenReveal
        private static bool CaseA_ControllerDoesNotShowPanel(out string note)
        {
            note = null;
            string src = ReadSource(ControllerRel);
            if (src == null) { note = "[A] missing " + ControllerRel; return false; }
            if (src.IndexOf("HoldThenReveal", StringComparison.Ordinal) >= 0 ||
                src.IndexOf("panel.Show(", StringComparison.Ordinal) >= 0 ||
                src.IndexOf("AddComponent<OwnedTownPanel>", StringComparison.Ordinal) >= 0)
            {
                note = "[A] OwnedTownController still auto-adds or Show()s OwnedTownPanel after reconstruct " +
                       "(HoldThenReveal / panel.Show / AddComponent<OwnedTownPanel> present in comment-stripped source). " +
                       "WO-1876: first frame is the town + HudKit; peaceful dock Build is the door.";
                return false;
            }
            if (src.IndexOf("EnsureCaptureMilestonesForDesign", StringComparison.Ordinal) < 0 ||
                src.IndexOf("OWNED_TOWN_READY_NO_PANEL", StringComparison.Ordinal) < 0)
            {
                note = "[A] OwnedTownController no longer folds pristine capture milestones / " +
                       "OWNED_TOWN_READY_NO_PANEL — CanEdit would stay gated without the panel's Begin/Inspect.";
                return false;
            }
            note = "[A] controller reconstructs without OwnedTownPanel.Show and folds pristine milestones";
            return true;
        }

        // B — SelectStructure must not Find OwnedTownPanel; must EnsureSelectionUi
        private static bool CaseB_SelectStructureStaysOnCastleUi(out string note)
        {
            note = null;
            string src = ReadSource(BuildModeRel);
            if (src == null) { note = "[B] missing " + BuildModeRel; return false; }

            // Isolate the SelectStructure method body roughly by name + next private method.
            int selectAt = src.IndexOf("private void SelectStructure(PlacedStructure ps)", StringComparison.Ordinal);
            if (selectAt < 0) { note = "[B] SelectStructure method missing from BuildModeController."; return false; }
            int nextMethod = src.IndexOf("private void ShowSelectionPanel", selectAt + 1, StringComparison.Ordinal);
            if (nextMethod < 0) nextMethod = src.Length;
            string selectBody = src.Substring(selectAt, nextMethod - selectAt);

            if (selectBody.IndexOf("FindAnyObjectByType<World.Camps.OwnedTownPanel>", StringComparison.Ordinal) >= 0 ||
                selectBody.IndexOf("FindAnyObjectByType<OwnedTownPanel>", StringComparison.Ordinal) >= 0 ||
                selectBody.IndexOf("townPanel?.SelectStructure", StringComparison.Ordinal) >= 0)
            {
                note = "[B] owned SelectStructure still routes to OwnedTownPanel " +
                       "(FindAnyObjectByType / townPanel.SelectStructure). Revert recipe: that bounce is the defect.";
                return false;
            }
            if (selectBody.IndexOf("IsClearableRubble", StringComparison.Ordinal) < 0 ||
                selectBody.IndexOf("EnsureSelectionUi", StringComparison.Ordinal) < 0)
            {
                note = "[B] SelectStructure must allow clearable rubble and call EnsureSelectionUi " +
                       "(castle selection strip) when IsOwnedTown.";
                return false;
            }
            if (src.IndexOf("TryClearRubble", StringComparison.Ordinal) < 0 ||
                src.IndexOf("OwnedTownDesignService.TryBeginMove", StringComparison.Ordinal) < 0)
            {
                note = "[B] BuildModeController must route owned clear/move through " +
                       "TryClearRubble / OwnedTownDesignService.TryBeginMove.";
                return false;
            }
            note = "[B] owned SelectStructure stays on EnsureSelectionUi; clear/move use owned adapters";
            return true;
        }

        // C — WO-1872 predicate: razed inherited rubble is NOT repairable damage
        private static bool CaseC_RubbleIsNotRepairableDamage(out string note)
        {
            note = null;
            var ruin = new OwnedBaseStructure {
                instanceId = "owned-town-hud-ruin",
                condition01 = CapturedTownStanddown.RazedCondition,
                placement = new PlacedStructureData("wall_stone", 0, 0, 0, 1),
                inheritedPose = new OwnedStructurePose {
                    sourceScene = "RaidBase_IronBastion",
                    sourcePath = "0/1",
                    sourceName = "wall",
                    templateStructureId = "iron_bastion.wall.hud",
                    parentFrame = Identity16(),
                    qw = 1f, sx = 1f, sy = 1f, sz = 1f
                }
            };
            if (CapturedTownStanddown.IsRepairableDamage(ruin))
            {
                note = "[C] razed inherited ruin is offered as repairable damage — WO-1872 predicate regressed.";
                return false;
            }
            if (!CapturedTownStanddown.IsClearableRubble(ruin))
            {
                note = "[C] razed inherited ruin is not clearable rubble — clear-for-salvage verb is unreachable.";
                return false;
            }
            string standdown = ReadSource(StanddownRel);
            if (standdown == null) { note = "[C] missing " + StanddownRel; return false; }
            if (standdown.IndexOf("IsRepairableDamage", StringComparison.Ordinal) < 0 ||
                standdown.IndexOf("condition01 > 0f", StringComparison.Ordinal) < 0)
            {
                note = "[C] CapturedTownStanddown.IsRepairableDamage no longer requires condition01 > 0 " +
                       "(rubble at 0 would be repairable again).";
                return false;
            }
            note = "[C] razed rubble is clearable and not repairable damage";
            return true;
        }

        // D — HUD context Town for owned town at rest
        private static bool CaseD_HudContextTownForOwned(out string note)
        {
            note = null;
            string src = ReadSource(ResolverRel);
            if (src == null) { note = "[D] missing " + ResolverRel; return false; }
            if (src.IndexOf("HubScenes.IsOwnedTown(sceneName)", StringComparison.Ordinal) < 0)
            {
                note = "[D] HudContextResolver.ResolveForSceneAtRest no longer treats IsOwnedTown as inVillage — " +
                       "owned town would leave Town context (the reuse we want).";
                return false;
            }
            var ctx = DeNelle.Core.HudModel.HudContextResolver.ResolveForSceneAtRest(
                OwnedTownScenePose.SceneName);
            if (ctx != DeNelle.Core.HudModel.HudContext.Town)
            {
                note = "[D] ResolveForSceneAtRest('" + OwnedTownScenePose.SceneName + "') returned " +
                       ctx + ", expected Town.";
                return false;
            }
            note = "[D] owned town at rest resolves HudContext.Town";
            return true;
        }

        // E — Enter/Exit must not HideForBuild / Show the panel
        private static bool CaseE_EnterExitDoNotReshowPanel(out string note)
        {
            note = null;
            string src = ReadSource(BuildModeRel);
            if (src == null) { note = "[E] missing " + BuildModeRel; return false; }
            if (src.IndexOf("HideForBuild()", StringComparison.Ordinal) >= 0 ||
                src.IndexOf("OwnedTownPanel>()?.Show()", StringComparison.Ordinal) >= 0 ||
                src.IndexOf("OwnedTownPanel>()?.HideForBuild()", StringComparison.Ordinal) >= 0)
            {
                note = "[E] BuildModeController Enter/Exit still HideForBuild/Show OwnedTownPanel — " +
                       "exiting build would reopen the retired modal door.";
                return false;
            }
            note = "[E] Enter/Exit no longer HideForBuild/Show OwnedTownPanel";
            return true;
        }

        private static float[] Identity16()
        {
            var frame = new float[16];
            frame[0] = frame[5] = frame[10] = frame[15] = 1f;
            return frame;
        }

        private static string ReadSource(string relative)
        {
            string path = Path.Combine(UnityEngine.Application.dataPath,
                relative.Replace('/', Path.DirectorySeparatorChar));
            return File.Exists(path) ? StripComments(File.ReadAllText(path)) : null;
        }

        private static string StripComments(string src)
        {
            if (string.IsNullOrEmpty(src)) return src;
            var sb = new StringBuilder(src.Length);
            for (int i = 0; i < src.Length; i++)
            {
                char ch = src[i];
                if (ch == '/' && i + 1 < src.Length && src[i + 1] == '/')
                {
                    while (i < src.Length && src[i] != '\n') i++;
                    if (i < src.Length) sb.Append('\n');
                    continue;
                }
                if (ch == '/' && i + 1 < src.Length && src[i + 1] == '*')
                {
                    i += 2;
                    while (i + 1 < src.Length && !(src[i] == '*' && src[i + 1] == '/'))
                    {
                        if (src[i] == '\n') sb.Append('\n');
                        i++;
                    }
                    i++;
                    continue;
                }
                if (ch == '"' || ch == '\'')
                {
                    char quote = ch;
                    sb.Append(ch);
                    i++;
                    while (i < src.Length)
                    {
                        sb.Append(src[i]);
                        if (src[i] == '\\' && i + 1 < src.Length) { sb.Append(src[i + 1]); i += 2; continue; }
                        if (src[i] == quote) break;
                        i++;
                    }
                    continue;
                }
                sb.Append(ch);
            }
            return sb.ToString();
        }
    }
}

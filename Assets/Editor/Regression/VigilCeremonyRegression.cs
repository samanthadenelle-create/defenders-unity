// =============================================================================
// VigilCeremonyRegression  [vigil-ceremony]  --  WO-1874.
// Markers: VIGIL_CEREMONY_OK / VIGIL_CEREMONY_FAIL: <reason>
// -----------------------------------------------------------------------------
// Source-lint only. NEVER throws. HUD must not using Village; Village must not
// using HUD. Five words, no "ancestors stir" on Dawn, no effect binding on the
// plate, PanelId.CeremonyOfVigil appended.
// =============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEngine;

namespace DeNelle.Editor.Regression
{
    public static class VigilCeremonyRegression
    {
        private const string Tag = "[vigil-ceremony]";

        private const string WordsRel = "Assets/_Modules/Core/Circle/VigilCeremonyWords.cs";
        private const string LedgerRel = "Assets/_Modules/Core/Circle/VigilCeremonyLedger.cs";
        private const string VmRel = "Assets/_Modules/HUD/Circle/VigilCeremonyVM.cs";
        private const string PanelRel = "Assets/_Modules/HUD/Circle/VigilCeremonyPanel.cs";
        private const string BootstrapRel = "Assets/_Modules/HUD/Circle/VigilCeremonyPanelBootstrap.cs";
        private const string DressRel = "Assets/_Modules/Village/Heart/HeartVigilDressingController.cs";
        private const string RouterRel = "Assets/_Modules/Core/UI/PanelRouter.cs";
        private const string WireRel = "Assets/_Modules/HUD/Circle/CircleWire.cs";
        private static readonly string[] FiveWords = { "Ember", "Flame", "Beacon", "Pyre", "Dawn" };

        public static bool Run(out string reason)
        {
            try
            {
                return RunCore(out reason);
            }
            catch (Exception ex)
            {
                reason = "VIGIL_CEREMONY_FAIL " + Tag + " suite THREW: " +
                         ex.GetType().Name + ": " + ex.Message;
                return false;
            }
        }

        private static bool RunCore(out string reason)
        {
            var failures = new List<string>();
            var notes = new List<string>();

            CaseFilesExist(failures, notes);
            CaseFiveWords(failures, notes);
            CaseDawnLine(failures, notes);
            CaseNoCrossAsm(failures, notes);
            CasePanelId(failures, notes);
            CaseNoEffectBinding(failures, notes);
            CaseDoors(failures, notes);

            if (failures.Count > 0)
            {
                reason = "VIGIL_CEREMONY_FAIL: " + failures.Count + " case failure(s) -- " +
                         string.Join(" | ", failures);
                return false;
            }

            reason = "VIGIL_CEREMONY_OK WO-1874 vigil-ceremony: " + notes.Count +
                     " check(s) green; " + string.Join("; ", notes);
            return true;
        }

        private static void CaseFilesExist(List<string> failures, List<string> notes)
        {
            const string C = "[vigil-files]";
            string[] required =
            {
                WordsRel, LedgerRel, VmRel, PanelRel, BootstrapRel, DressRel, RouterRel, WireRel
            };
            for (int i = 0; i < required.Length; i++)
            {
                if (ReadOrNull(required[i]) == null)
                    failures.Add(C + " missing " + required[i]);
            }
            if (!HasFailure(failures, C))
                notes.Add(C + " " + required.Length + " WO-1874 files present");
        }

        private static void CaseFiveWords(List<string> failures, List<string> notes)
        {
            const string C = "[vigil-five-words]";
            string raw = ReadOrNull(WordsRel);
            if (raw == null) { failures.Add(C + " cannot read " + WordsRel); return; }
            string src = RegressionSourceText.StripComments(raw);
            for (int i = 0; i < FiveWords.Length; i++)
            {
                if (src.IndexOf("\"" + FiveWords[i] + "\"", StringComparison.Ordinal) < 0)
                    failures.Add(C + " " + WordsRel + " is missing the locked word " + FiveWords[i]);
            }
            if (!HasFailure(failures, C))
                notes.Add(C + " Ember/Flame/Beacon/Pyre/Dawn present");
        }

        private static void CaseDawnLine(List<string> failures, List<string> notes)
        {
            const string C = "[vigil-dawn-line]";
            string raw = ReadOrNull(WordsRel);
            if (raw == null) { failures.Add(C + " cannot read " + WordsRel); return; }
            if (Regex.IsMatch(raw, @"LineDawn\s*=\s*""[^""]*ancestors stir", RegexOptions.IgnoreCase))
            {
                failures.Add(C + " Dawn line still contains 'ancestors stir'. Owner trimmed that " +
                             "because it is the live Tier-5 perk title.");
            }
            if (raw.IndexOf("The canopy shifts. The Circle has been heard.", StringComparison.Ordinal) < 0)
                failures.Add(C + " locked Dawn line is missing from " + WordsRel);
            if (!HasFailure(failures, C))
                notes.Add(C + " Dawn line is the trimmed canopy line");
        }

        private static void CaseNoCrossAsm(List<string> failures, List<string> notes)
        {
            const string C = "[vigil-no-cross-asm]";
            string[] hud = { VmRel, PanelRel, BootstrapRel, WireRel };
            for (int i = 0; i < hud.Length; i++)
            {
                string raw = ReadOrNull(hud[i]);
                if (raw == null) { failures.Add(C + " cannot read " + hud[i]); continue; }
                string src = RegressionSourceText.StripComments(raw);
                if (Regex.IsMatch(src, @"using\s+DeNelle\.Village\b"))
                    failures.Add(C + " " + hud[i] + " uses DeNelle.Village. HUD NEVER references Village.");
            }

            string dress = ReadOrNull(DressRel);
            if (dress == null) failures.Add(C + " cannot read " + DressRel);
            else
            {
                string src = RegressionSourceText.StripComments(dress);
                if (Regex.IsMatch(src, @"using\s+DeNelle\.HUD\b"))
                    failures.Add(C + " " + DressRel + " uses DeNelle.HUD. Village NEVER references HUD.");
            }
            if (!HasFailure(failures, C))
                notes.Add(C + " HUD/Village stay on Core only");
        }

        private static void CasePanelId(List<string> failures, List<string> notes)
        {
            const string C = "[vigil-panel-id]";
            string raw = ReadOrNull(RouterRel);
            if (raw == null) { failures.Add(C + " cannot read " + RouterRel); return; }
            string src = RegressionSourceText.StripComments(raw);
            if (!Regex.IsMatch(src, @"\bCircle\s*=\s*28\b"))
                failures.Add(C + " Circle = 28 moved. PanelId is append-only.");
            if (!Regex.IsMatch(src, @"\bCeremonyOfVigil\s*=\s*29\b"))
                failures.Add(C + " " + RouterRel + " does not declare CeremonyOfVigil = 29.");
            if (!HasFailure(failures, C))
                notes.Add(C + " PanelId.CeremonyOfVigil = 29 appended");
        }

        private static void CaseNoEffectBinding(List<string> failures, List<string> notes)
        {
            const string C = "[vigil-no-effect]";
            string panel = ReadOrNull(PanelRel);
            if (panel == null) { failures.Add(C + " cannot read " + PanelRel); return; }
            string src = RegressionSourceText.StripComments(panel);
            if (Regex.IsMatch(src, @"\bEffectText\b"))
                failures.Add(C + " " + PanelRel + " binds EffectText. Ruling: never render effect/stat.");
            if (Regex.IsMatch(src, @"\beffect\b", RegexOptions.IgnoreCase) &&
                Regex.IsMatch(src, @"\.text\s*="))
            {
                // only fail if a .text assignment mentions effect
                foreach (Match m in Regex.Matches(src, @"\.text\s*=\s*([^;]{0,200});"))
                {
                    if (m.Groups[1].Value.IndexOf("effect", StringComparison.OrdinalIgnoreCase) >= 0)
                        failures.Add(C + " plate .text assignment mentions effect: " + m.Value);
                }
            }

            string wire = ReadOrNull(WireRel);
            if (wire != null)
            {
                string wsrc = RegressionSourceText.StripComments(wire);
                int ceremony = wsrc.IndexOf("class CeremonyDto", StringComparison.Ordinal);
                if (ceremony < 0) failures.Add(C + " CircleWire has no CeremonyDto");
                else
                {
                    int end = wsrc.IndexOf("class ", ceremony + 10, StringComparison.Ordinal);
                    string block = end > ceremony ? wsrc.Substring(ceremony, end - ceremony) : wsrc.Substring(ceremony);
                    if (Regex.IsMatch(block, @"\beffect\b"))
                        failures.Add(C + " CeremonyDto declares an effect field.");
                    if (Regex.IsMatch(block, @"\bperk_id\b"))
                        failures.Add(C + " CeremonyDto declares perk_id.");
                }
            }
            if (!HasFailure(failures, C))
                notes.Add(C + " no effect binding on the plate or CeremonyDto");
        }

        private static void CaseDoors(List<string> failures, List<string> notes)
        {
            const string C = "[vigil-doors]";
            string boot = ReadOrNull(BootstrapRel);
            if (boot == null) failures.Add(C + " cannot read " + BootstrapRel);
            else
            {
                string src = RegressionSourceText.StripComments(boot);
                if (src.IndexOf("[RuntimeInitializeOnLoadMethod]", StringComparison.Ordinal) < 0)
                    failures.Add(C + " bootstrap has no [RuntimeInitializeOnLoadMethod]");
                int gate = src.IndexOf("ClanFeatureGate.PlayerFacingEnabled", StringComparison.Ordinal);
                int spawn = src.IndexOf("new GameObject(", StringComparison.Ordinal);
                if (gate < 0)
                    failures.Add(C + " bootstrap does not check ClanFeatureGate.PlayerFacingEnabled");
                else if (spawn >= 0 && spawn < gate)
                    failures.Add(C + " bootstrap constructs a GameObject BEFORE the feature-gate check");
            }

            string circlePanel = ReadOrNull("Assets/_Modules/HUD/Circle/CircleScreenPanel.cs");
            if (circlePanel == null) failures.Add(C + " cannot read CircleScreenPanel.cs");
            else
            {
                string src = RegressionSourceText.StripComments(circlePanel);
                if (src.IndexOf("VigilCeremonyPanel", StringComparison.Ordinal) < 0)
                    failures.Add(C + " CircleScreenPanel does not name VigilCeremonyPanel (D1)");
                if (src.IndexOf("PanelId.CeremonyOfVigil", StringComparison.Ordinal) < 0)
                    failures.Add(C + " CircleScreenPanel does not name PanelId.CeremonyOfVigil (D1)");
            }

            if (!HasFailure(failures, C))
                notes.Add(C + " D2 bootstrap gated first; D1 CircleScreenPanel names the plate");
        }

        private static bool HasFailure(List<string> failures, string tag)
        {
            for (int i = 0; i < failures.Count; i++)
                if (failures[i].StartsWith(tag, StringComparison.Ordinal)) return true;
            return false;
        }

        private static string RepoPath(string relative)
        {
            string root;
            try { root = Path.GetDirectoryName(Application.dataPath); }
            catch { root = null; }
            if (string.IsNullOrEmpty(root)) return relative;
            return Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
        }

        private static string ReadOrNull(string relative)
        {
            try
            {
                string p = RepoPath(relative);
                return File.Exists(p) ? File.ReadAllText(p) : null;
            }
            catch { return null; }
        }
    }
}

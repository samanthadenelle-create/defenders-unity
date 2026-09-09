// WO-1410: one canon noun per Hero destination, plus the Wisdom and Loadout doors.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using DeNelle.Core.UI;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace DeNelle.Editor.Regression
{
    public static class HeroNameSingleSourceRegression
    {
        private const string CanonResources = "Assets/Resources/Data/Canonical/canon-strings.json";
        private const string CanonStreaming = "Assets/StreamingAssets/Data/Canonical/canon-strings.json";
        private const string ModulesRoot = "Assets/_Modules";
        private const string SkillPanel = "Assets/_Modules/Village/Talents/HeroSkillTreePanelMvvm.cs";
        private const string SkillVm = "Assets/_Modules/Village/Talents/HeroSkillTreeVM.cs";
        private const string LoadoutPanel = "Assets/_Modules/Village/Talents/HeroLoadoutPanelMvvm.cs";
        private const string Progression = "Assets/_Modules/Village/Hero/HeroProgression.cs";

        public static void RunAll()
        {
            if (Run(out string report)) Debug.Log(report);
            else Debug.LogError(report);
        }

        public static bool Run(out string report)
        {
            var failures = new List<string>();
            try
            {
                // RED recipe: delete the heroLoadout row from only the StreamingAssets twin.
                CaseCanonTwins(failures);

                // RED recipe: change HeroLoadoutVM.Title back to the literal Hot-Swap Skills.
                CaseNoRetiredNames(failures);

                // RED recipe: change the EquipmentPanel Skills button back to a typed label.
                CaseEveryFaceUsesCanon(failures);

                // RED recipe: remove OpenSkillsFromLoadout from the empty-state button callback.
                CaseEmptyLoadoutDoor(failures);

                // RED recipe: remove NextWisdomLevel from the Wisdom label assignment.
                CaseWisdomExplainsNextGrant(failures);

                // RED recipe: restore AssignSelectedToSlot as the Skills rail callback.
                CaseLoadoutOwnsSockets(failures);

                // RED recipe: delete the Hero FlowTrace.Step from HudStrings.HeroFaceLabel.
                CaseFaceTrace(failures);

                // RED recipe: restore the ternary at the confirm label -
                //   _popupConfirmLabel.text = canSpend ? "LEARN" : "OWNED";
                CaseConfirmWordIsNotATernary(failures);
            }
            catch (Exception ex)
            {
                failures.Add("[suite] THREW " + ex.GetType().Name + ": " + ex.Message);
            }

            report = failures.Count == 0
                ? "HERO_NAME_SINGLE_SOURCE_OK: BAG/SKILLS/LOADOUT share canon; Wisdom names next level; empty Loadout opens Skills; Skills sockets are read-only; confirm word names the real node state"
                : "HERO_NAME_SINGLE_SOURCE_FAIL: " + string.Join(" | ", failures);
            return failures.Count == 0;
        }

        private static void CaseCanonTwins(List<string> failures)
        {
            byte[] a = File.ReadAllBytes(CanonResources);
            byte[] b = File.ReadAllBytes(CanonStreaming);
            if (!SameBytes(a, b)) failures.Add("[canon-twins] canonical copies differ byte-for-byte");

            var rows = JObject.Parse(Encoding.UTF8.GetString(a));
            ExpectRow(rows, HudStrings.KeyHeroBag, "BAG", failures);
            ExpectRow(rows, HudStrings.KeyHeroSkills, "SKILLS", failures);
            ExpectRow(rows, HudStrings.KeyHeroLoadout, "LOADOUT", failures);
        }

        private static void CaseNoRetiredNames(List<string> failures)
        {
            var retired = new Regex("\"(?:TALENT TREE|Hot-Swap Skills|INVENTORY|TALENTS)\"");
            foreach (string path in Directory.GetFiles(ModulesRoot, "*.cs", SearchOption.AllDirectories))
            {
                string code = StripComments(File.ReadAllText(path));
                if (retired.IsMatch(code))
                    failures.Add("[retired-name] " + path + " still emits a retired Hero screen noun");
            }
        }

        private static void CaseEveryFaceUsesCanon(List<string> failures)
        {
            ExpectToken("Assets/_Modules/HUD/PlayerDeckWorkspace.cs", "KeyHeroBag", failures, "deck-bag");
            ExpectToken("Assets/_Modules/HUD/PlayerDeckWorkspace.cs", "KeyHeroSkills", failures, "deck-skills");
            ExpectToken("Assets/_Modules/HUD/PlayerDeckWorkspace.cs", "KeyHeroLoadout", failures, "deck-loadout");
            ExpectToken("Assets/_Modules/Village/Hero/HeroEquipHud.cs", "KeyHeroBag", failures, "bag-button");
            ExpectToken("Assets/_Modules/Village/Hero/InventoryUIBuilder.cs", "KeyHeroBag", failures, "bag-chrome");
            ExpectToken("Assets/_Modules/Village/Hero/InventoryVM.cs", "KeyHeroBag", failures, "bag-vm");
            ExpectToken("Assets/_Modules/Village/Hero/InventoryPaperDoll.cs", "KeyHeroSkills", failures, "bag-skills-button");
            ExpectToken("Assets/_Modules/Village/Hero/EquipmentPanel.cs", "KeyHeroSkills", failures, "equipment-skills-button");
            ExpectToken("Assets/_Modules/Village/Talents/HeroLoadoutVM.cs", "KeyHeroLoadout", failures, "loadout-vm");
            ExpectToken(SkillVm, "KeyHeroSkills", failures, "skills-vm");
        }

        private static void CaseEmptyLoadoutDoor(List<string> failures)
        {
            string code = StripComments(File.ReadAllText(LoadoutPanel));
            if (!code.Contains("No skills unlocked yet.") ||
                !code.Contains("\"OPEN \" + HudStrings.HeroFaceLabel(HudStrings.KeyHeroSkills, \"button\")") ||
                !code.Contains("OpenSkillsFromLoadout") ||
                !code.Contains("PanelRouter.Open(PanelId.HeroSkillTree)") ||
                !code.Contains("ClampMinTouch(openSkills)"))
                failures.Add("[empty-loadout] missing sentence, canon-composed OPEN SKILLS touch target, or HeroSkillTree route");
        }

        private static void CaseWisdomExplainsNextGrant(List<string> failures)
        {
            string panel = StripComments(File.ReadAllText(SkillPanel));
            string vm = StripComments(File.ReadAllText(SkillVm));
            string progression = StripComments(File.ReadAllText(Progression));
            if (!panel.Contains("next point at Level ") || !panel.Contains("_vm.NextWisdomLevel"))
                failures.Add("[wisdom-copy] Wisdom chip does not render the next point-bearing level");
            if (!vm.Contains("progression.Level") || !vm.Contains("+ 1"))
                failures.Add("[wisdom-rule] NextWisdomLevel is not derived from the live HeroProgression level");
            if (!progression.Contains("WisdomCurrencyService.Instance?.Grant(WisdomForLevel(newLevel))"))
                failures.Add("[wisdom-rule] progression no longer grants Wisdom at every reached level");
        }

        private static void CaseLoadoutOwnsSockets(List<string> failures)
        {
            string panel = StripComments(File.ReadAllText(SkillPanel));
            string vm = StripComments(File.ReadAllText(SkillVm));
            if (panel.Contains("AssignSelectedToSlot") || panel.Contains("ConfirmOrAssign"))
                failures.Add("[socket-owner] Skills still exposes an assignment callback");
            if (!panel.Contains("btn.interactable = false") || !panel.Contains("KeyHeroLoadout"))
                failures.Add("[socket-owner] Skills rail is not visibly read-only or does not point to Loadout");
            if (vm.Contains("AssignableSkillBarAccess.Assign(") || vm.Contains("AssignableSkillBarAccess.Clear("))
                failures.Add("[socket-owner] Skills VM still mutates a quick-swap socket");
        }

        private static void CaseFaceTrace(List<string> failures)
        {
            string hud = StripComments(File.ReadAllText("Assets/_Modules/Core/UI/HudStrings.cs"));
            if (!hud.Contains("FlowTrace.Step(\"Hero\"") ||
                !hud.Contains("source=LocalText site="))
                failures.Add("[face-trace] canon Hero face decisions are no longer traced");
        }

        /// <summary>
        /// COLOURBLIND LAW: the confirm button's WORD is the state, so it may not be a two-way
        /// ternary. CanSpendSelected is false for OWNED, prereq-LOCKED and UNAFFORDABLE alike -
        /// `canSpend ? "LEARN" : "OWNED"` therefore paints OWNED over a talent the player does
        /// not own, which is a lie on the one cue the owner can read. The word must come from
        /// the node's own state (ConfirmWordFor), and all three words must exist.
        /// </summary>
        private static void CaseConfirmWordIsNotATernary(List<string> failures)
        {
            string panel = StripComments(File.ReadAllText(SkillPanel));
            var assign = Regex.Match(panel, @"_popupConfirmLabel\s*\.\s*text\s*=\s*([^;]+);");
            if (!assign.Success)
            {
                failures.Add("[confirm-word] the spend popup's confirm label is never assigned - " +
                             "the affirmative action would render with no word at all");
                return;
            }
            string rhs = assign.Groups[1].Value;
            if (rhs.IndexOf('?') >= 0)
                failures.Add("[confirm-word] the confirm label is a TERNARY (" + rhs.Trim() + "). " +
                             "CanSpendSelected is false for owned AND prereq-locked AND unaffordable " +
                             "nodes, so a two-way choice must mislabel two of the three states - the " +
                             "shipped form painted OWNED on a locked talent");
            if (panel.IndexOf("ConfirmWordFor", StringComparison.Ordinal) < 0)
                failures.Add("[confirm-word] ConfirmWordFor is gone - nothing derives the confirm " +
                             "word from the selected node's SkillNodeState");
            foreach (string word in new[] { "\"LEARN\"", "\"OWNED\"", "\"LOCKED\"" })
                if (panel.IndexOf(word, StringComparison.Ordinal) < 0)
                    failures.Add("[confirm-word] the confirm word " + word + " is gone - that state " +
                                 "would borrow another state's word");
        }

        private static void ExpectRow(JObject rows, string key, string expected,
                                      List<string> failures)
        {
            string actual = rows != null ? (string)rows[key] : null;
            if (!string.Equals(actual, expected, StringComparison.Ordinal))
                failures.Add("[canon-row] " + key + " must equal " + expected);
        }

        private static void ExpectToken(string path, string token, List<string> failures, string caseName)
        {
            if (!StripComments(File.ReadAllText(path)).Contains(token))
                failures.Add("[" + caseName + "] " + path + " does not resolve " + token);
        }

        private static bool SameBytes(byte[] a, byte[] b)
        {
            if (a == null || b == null || a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
            return true;
        }

        private static string StripComments(string source)
        {
            string noBlocks = Regex.Replace(source ?? "", "/\\*.*?\\*/", "", RegexOptions.Singleline);
            return Regex.Replace(noBlocks, "//.*?$", "", RegexOptions.Multiline);
        }
    }
}

#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using DeNelle.Village;
using UnityEditor;

namespace DeNelle.Editor.Regression
{
    /// <summary>
    /// WO-1411 — THE BUILD FLOW SAYS WHAT IT COSTS, WHAT IT TAKES, AND WHAT YOU CAN AFFORD.
    ///
    /// Minted RED against the tree at commit 949e848a0. Each pin below names the exact fact
    /// that was ABSENT there:
    ///   1. no collection card carried an affordability count — the subtitle was the authored
    ///      verb phrase (`c.Subtitle`, BuildCollectionBrowser.cs:182 pre-change);
    ///   2. the ghost rail's three verbs were the D17 GLYPHS with two-letter ASCII stubs
    ///      ("OK" / "Rot" / "X", BuildHudController.cs:688-696 pre-change) — the review read
    ///      them as three unlabelled symbols;
    ///   3. the confirm modal showed orientation only — no cost term, no duration;
    ///   4. an 8th category card titled "Upgrade Defenses" sat in the build grid
    ///      (BuildCollectionBrowser.cs:214 pre-change);
    ///   5. the first-use banner could print the Step.Category copy over an armed ghost,
    ///      because only BuildCollectionBrowser ever advanced the guide past the pick steps.
    ///
    /// SOURCE-TEXT SHAPE, like its sibling BuildCollectionPlayerRegression: these are copy and
    /// wiring facts about screens that need a scene + an economy to render, and the alternative
    /// (spinning a live build session headlessly) would measure the fixture, not the code.
    /// The BEHAVIOURAL half — the guide's own phase machine — is driven for real below, because
    /// it is pure static state and there is no excuse for asserting it from a string.
    /// </summary>
    public static class BuildAffordabilityWordsRegression
    {
        private const string BrowserPath = "Assets/_Modules/Village/BuildMode/BuildCollectionBrowser.cs";
        private const string VmPath      = "Assets/_Modules/Village/BuildMode/StructureCardVM.cs";
        private const string HudPath     = "Assets/_Modules/Village/BuildMode/BuildHudController.cs";
        private const string ModalPath   = "Assets/_Modules/Village/BuildMode/BuildPreviewModal.cs";
        private const string GuidePath   = "Assets/_Modules/Village/BuildMode/BuildFirstUseGuide.cs";
        private const string CollectionsPath = "Assets/Resources/Data/Canonical/card-collections.json";
        private const string StreamedCollectionsPath = "Assets/StreamingAssets/Data/Canonical/card-collections.json";

        [MenuItem("Tools/Regression/Run Build Affordability Words")]
        public static void RunMenu() { if (!Run(out var r)) throw new Exception(r); UnityEngine.Debug.Log(r); }

        public static bool Run(out string reason)
        {
            var failures = new List<string>();

            string browser = Read(BrowserPath, failures);
            string vm      = Read(VmPath, failures);
            string hud     = Read(HudPath, failures);
            string modal   = Read(ModalPath, failures);
            string guide   = Read(GuidePath, failures);

            // ── 1. Every collection card subtitle is an AFFORDABILITY COUNT ──────────────
            // The words live in the VM so the card and this oracle read one sentence; the
            // browser must render THAT, not a second wording of its own.
            if (vm.IndexOf("you can build now", StringComparison.Ordinal) < 0 ||
                vm.IndexOf("nothing affordable yet", StringComparison.Ordinal) < 0)
                failures.Add("StructureCardVM does not own the affordability words ('N you can build now' / 'nothing affordable yet')");
            if (vm.IndexOf("public static int AffordableCount(", StringComparison.Ordinal) < 0 ||
                vm.IndexOf("BuildCollectionBrowser.IsCollectionItemVisible(", StringComparison.Ordinal) < 0)
                failures.Add("the affordability count is not a fold over the ONE offer authority (IsCollectionItemVisible) -- a second visibility predicate would let a door promise builds the cards inside contradict");
            if (browser.IndexOf("StructureCardVM.AffordabilityWords(", StringComparison.Ordinal) < 0 ||
                browser.IndexOf("StructureCardVM.AffordableCount(", StringComparison.Ordinal) < 0)
                failures.Add("collection cards no longer render the VM's affordability subtitle");
            if (browser.IndexOf("\"collection=\" + c.CollectionId + \" affordable=\"", StringComparison.Ordinal) < 0)
                failures.Add("the WO-1411 proving trace (collection=<id> affordable=<n>) is missing from the card render");

            // ── 2. The ghost stage speaks WORDS ─────────────────────────────────────────
            // Pinned in BuildHudController, which is where the rail actually is. (The ticket
            // named GhostPreview / BuildPlaceButton; neither builds these verbs -- see the
            // WO-1411 report. BuildPlaceButton already contained the literal "PLACE" and a pin
            // there would have been green on the defective tree.)
            if (hud.IndexOf("\"ROTATE\"", StringComparison.Ordinal) < 0 ||
                hud.IndexOf("\"CANCEL\"", StringComparison.Ordinal) < 0 ||
                hud.IndexOf("PlaceVerbWord = \"PLACE\"", StringComparison.Ordinal) < 0)
                failures.Add("the ghost rail's three verbs are not the words PLACE / ROTATE / CANCEL");
            if (hud.IndexOf("MakeVerb(railRt, \"OkChip\", \"OK\"", StringComparison.Ordinal) >= 0 ||
                hud.IndexOf("\"Rot\"", StringComparison.Ordinal) >= 0)
                failures.Add("the retired D17 icon-only chips (OK / Rot / X) are back on the ghost rail");
            if (hud.IndexOf("BlockedVerbWord = \"BLOCKED\"", StringComparison.Ordinal) < 0)
                failures.Add("the blocked verdict no longer carries a WORD -- a refusal would rest on colour/alpha alone (colorblind law)");
            // The named seats must survive: the capture harness walks the live hierarchy by them.
            if (hud.IndexOf("\"OkChip\"", StringComparison.Ordinal) < 0 ||
                hud.IndexOf("\"CancelChip\"", StringComparison.Ordinal) < 0 ||
                hud.IndexOf("gameObject.name = \"BuildHudPlaceCancel\"", StringComparison.Ordinal) < 0)
                failures.Add("a ghost verb seat was renamed -- UICaptureLaunch.AssertConfirmChipInvalid finds 'OkChip' by name and would stop measuring the blocked verdict");

            // ── 3. The confirm modal prices the tap — AND THE MODEL COMPOSES IT ─────────
            // RE-POINTED before commit: the first cut composed the line inside the modal, and
            // UiMvvmConformanceRegression failed BuildPreviewModal for reading GameStateService
            // (canon 9 — derived text is model work). The terms are pinned in the VM now; the
            // View is pinned to PAINT and to READ NO STATE, which is the stronger assertion.
            if (modal.IndexOf("BuildCostTimeLine(", StringComparison.Ordinal) < 0 ||
                modal.IndexOf("StructureCardVM.PlacementSummaryFor(", StringComparison.Ordinal) < 0)
                failures.Add("the confirm modal still shows orientation only -- no cost/time line above CONFIRM, or it no longer takes it from the VM");
            if (modal.IndexOf("confirm cost='", StringComparison.Ordinal) < 0)
                failures.Add("the WO-1411 proving trace (confirm cost='...' time=<s>) is missing from the modal");
            if (vm.IndexOf("public static PlacementSummary PlacementSummaryFor(", StringComparison.Ordinal) < 0 ||
                vm.IndexOf("CostFormat.Words(", StringComparison.Ordinal) < 0 ||
                vm.IndexOf("ElarionUi.Duration(", StringComparison.Ordinal) < 0)
                failures.Add("the placement summary (basket + duration) is not composed by the VM");
            if (vm.IndexOf("\"Builder free\"", StringComparison.Ordinal) < 0)
                failures.Add("the confirm line does not say whether a builder is free");
            // The duration must be the one the placement will ACTUALLY run: same three steps
            // BuildModeController takes at commit. A literal would be a guess with a suffix.
            if (vm.IndexOf("TierForCost(", StringComparison.Ordinal) < 0 ||
                vm.IndexOf("GraceAdjustedDurationMs(", StringComparison.Ordinal) < 0 ||
                vm.IndexOf("GraceReasonFor(", StringComparison.Ordinal) < 0)
                failures.Add("the confirm duration is not derived through TierForCost + the grace rule, so the modal can quote a wait the build does not run");
            // MVVM: the View may not reach game state. Comment lines are excluded exactly as
            // UiMvvmConformanceRegression excludes them, so the retired-approach note survives.
            foreach (string ln in modal.Split('\n'))
            {
                string t = ln.TrimStart();
                if (t.StartsWith("//") || t.StartsWith("*") || t.StartsWith("/*")) continue;
                if (t.IndexOf("GameStateService", StringComparison.Ordinal) >= 0)
                { failures.Add("BuildPreviewModal reads GameStateService again -- the View must paint the VM's line, not derive it"); break; }
            }

            // ── 4. No card is titled 'Upgrade Defenses'; the door is a footer link ───────
            if (browser.IndexOf("\"Upgrade Defenses\"", StringComparison.Ordinal) >= 0)
                failures.Add("a category card is still titled 'Upgrade Defenses' (ruling section 2 #13: footer link, not card)");
            if (browser.IndexOf("village.build_mode.build_collections.manage_defenses_link", StringComparison.Ordinal) < 0 ||
                browser.IndexOf("PanelRouter.Open(PanelId.Manage, \"Defense\")", StringComparison.Ordinal) < 0)
                failures.Add("the Manage > Defense door left the build screen entirely -- the footer link or its route is gone");

            // ── 5. Ruling section 2 #13 renames, in the canonical data (BOTH copies) ────
            foreach (string path in new[] { CollectionsPath, StreamedCollectionsPath })
            {
                string json = Read(path, failures);
                if (json.IndexOf("\"title\": \"Towers\"", StringComparison.Ordinal) < 0 ||
                    json.IndexOf("\"title\": \"Walls & Gates\"", StringComparison.Ordinal) < 0)
                    failures.Add("ruling #13 renames missing in " + path + " (Defenses -> Towers, Protection -> Walls & Gates)");
                if (json.IndexOf("\"title\": \"Defenses\"", StringComparison.Ordinal) >= 0 ||
                    json.IndexOf("\"title\": \"Protection\"", StringComparison.Ordinal) >= 0)
                    failures.Add("the retired category titles are back in " + path);
            }

            // ── 6. The banner takes the PHASE. Driven for real, not read. ───────────────
            // Step.Category copy must never survive a ghost being armed, WHICHEVER door armed
            // it -- the palette carousel raises no CategorySelected/ItemSelected at all, which
            // is why the 07:02 capture showed "First build: select a category." over a ghost.
            BuildFirstUseGuide.ResetForTests();
            try
            {
                string categoryCopy = BuildFirstUseGuide.Copy;
                BuildFirstUseGuide.GhostArmed();
                if (BuildFirstUseGuide.Current == BuildFirstUseGuide.Step.Category ||
                    BuildFirstUseGuide.Current == BuildFirstUseGuide.Step.Item)
                    failures.Add("arming a ghost does not advance the first-use guide past the pick steps");
                if (string.Equals(BuildFirstUseGuide.Copy, categoryCopy, StringComparison.Ordinal))
                    failures.Add("the first-use banner still prints the Step.Category copy while a ghost is armed");
                if (BuildFirstUseGuide.Copy.IndexOf("PLACE", StringComparison.Ordinal) < 0)
                    failures.Add("the armed-ghost banner does not name the PLACE button the rail now carries");
            }
            finally
            {
                BuildFirstUseGuide.ResetForTests();
            }
            if (guide.IndexOf("public static void GhostArmed()", StringComparison.Ordinal) < 0)
                failures.Add("BuildFirstUseGuide lost the phase entry point the build HUD calls on Placing");
            if (hud.IndexOf("BuildFirstUseGuide.GhostArmed();", StringComparison.Ordinal) < 0)
                failures.Add("the build HUD no longer hands the guide its phase when placement starts");

            reason = failures.Count == 0
                ? "BUILD_AFFORDABILITY_WORDS_OK: collection subtitles carry the VM's affordability count, the ghost rail says PLACE / ROTATE / CANCEL (BLOCKED when refused), the confirm modal prices the tap with the real graced duration and crew, the 8th 'Upgrade Defenses' card is a footer link, ruling #13 renames landed in both canonical copies, and the banner takes the placement phase from any door"
                : "BUILD_AFFORDABILITY_WORDS_FAIL: " + string.Join(" | ", failures.ToArray());
            return failures.Count == 0;
        }

        private static string Read(string path, List<string> failures)
        {
            try { return File.ReadAllText(path); }
            catch (Exception ex) { failures.Add("unreadable source " + path + ": " + ex.Message); return string.Empty; }
        }
    }
}
#endif

// =============================================================================
// HudActionBarRegression — headless oracle for the WO-835 action-bar
// applicability repack. Marker: HUD_ACTIONBAR_OK / HUD_ACTIONBAR_FAIL.
// -----------------------------------------------------------------------------
// Assembly: DeNelle.EditorRegression (editor-only). Registered by the
// orchestrator into DataRegression.RunAll (sibling-suite protocol).
// Style/contract mirrors the other Run(out reason) oracles.
//
// Proves, with the REAL Core model (no scene/play mode):
//   1. MODEL INVARIANTS — HudActionBarModel emits the exact ordered active set
//      per signal combination (WO-835 §3b table): town baseline Build/Bag/Map/
//      Quests; Talk packs in/out; Raids HIDES when not capable yet DIMS-visible
//      when capable-but-not-full (WO-820/823 semantics preserved); Map obeys
//      Onboarded (WO-825 R4); Quests is never replaced by Upgrade (§3c split);
//      explore = Talk?/Bag only; non-calm postures empty the bar; events are
//      edge-triggered (never per-frame relayout).
//   1b. THE MEASURED DOCK (WO-1467) — the bar the PLAYER touches is the adaptive
//      peaceful dock, which HudKitController.BindActionBar builds INSTEAD of
//      subscribing the model above. It is built live here and its faces are read
//      out of the tree. See CheckMeasuredPeacefulDock for the full reasoning.
//   2. VIEW PURITY (source oracle on HudKitController.cs): the View binds the
//      model and no longer holds the retired gate reads (GameStateService /
//      RaidEntryGate.ArmyStatus / the Talk interactable dim), and the Upgrade
//      face is registered.
//      ⚠ The old note here said "DeNelle.HUD is not referenced by this asmdef".
//      That was FALSE and it is why the shipped dock ended up under source lint:
//      DeNelle.EditorRegression.asmdef lists DeNelle.HUD (read the asmdef, §5),
//      and HudUiRegression has been calling HudKitController statics typed for
//      some time. Source lint is a choice here, not a constraint.
//   3. WIRING — PostureSignals carries the RaidCapable mirror seam; the Village
//      RaidCapabilityHudBridge exists and cites ArmyReadiness.Compute (WO-823
//      single-source law); hud-areas.json dual copies carry the upgradeButton
//      row and have not diverged.
// =============================================================================

using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using TMPro;
using UnityEngine;
using DeNelle.Core.HudModel;

namespace DeNelle.Editor
{
    public static class HudActionBarRegression
    {
        // Deterministic fake source (mirrors the EditMode fixture) — drives the
        // REAL compute through every combination.
        private sealed class FakeSource : HudActionBarModel.ISource
        {
            public bool Talk;
            public bool Capable;
            public bool ArmyReady = true;
            public bool Onboarded = true;
            public bool Focused;
            // WO-1008 dim-reason inputs (default: a partially filled army, so the pre-existing
            // "capable but not full" cases keep asserting the ArmyNotFull reason they always meant).
            public int Deployable = 3;
            public int Queued;
            public int Cap = 5;

            public bool TalkAvailable => Talk;
            public bool RaidCapable => Capable;
            public bool RaidArmyReady => ArmyReady;
            public int RaidDeployableSlots => Deployable;
            public int RaidQueuedSlots => Queued;
            public int RaidCapSlots => Cap;
            public bool MapUnlocked => Onboarded;
            public bool BuildingFocused => Focused;
        }

        public static bool Run(out string reason)
        {
            var failures = new List<string>();
            var log = new StringBuilder();
            log.AppendLine("=== HudActionBarRegression: WO-835 applicability repack ===");

            try
            {
                CheckModelInvariants(failures, log);
                CheckMeasuredPeacefulDock(failures, log);
                CheckMeasuredOutsideDock(failures, log);
                CheckViewPurity(failures, log);
                CheckWiring(failures, log);
                CheckDungeonFlagAcknowledgement(failures, log);
            }
            catch (System.Exception ex)
            {
                failures.Add($"HudActionBarRegression threw: {ex.GetType().Name}: {ex.Message}");
            }

            return Verdict(failures, log, out reason);
        }

        // ── 1. model invariants (the §3b predicate table, REAL compute) ───────
        private static void CheckModelInvariants(List<string> failures, StringBuilder log)
        {
            var src = new FakeSource();
            var model = new HudActionBarModel(src);
            model.SetPosture(HudActionBarModel.PostureTown);

            // ⚠ WO-911 (owner ruling Q10+Q13, 2026-08-06) — THE EXPECTED SET CHANGED, 7 -> 6.
            // Map LEFT the bar (it is a tab inside Bag now) and the Upgrade face was RE-POINTED to
            // the unified Manage/Queues screen, which makes it always-applicable in town instead of
            // context-gated on a focused building. These oracles are UPDATED to the new set and
            // order — never deleted or weakened to make a smaller bar pass.
            ExpectSet(model, failures, "town baseline (no NPC/raid)",
                ActionBarButtonId.Build, ActionBarButtonId.Bag, ActionBarButtonId.Quests,
                ActionBarButtonId.Upgrade);

            src.Talk = true; model.Tick();
            ExpectSet(model, failures, "NPC in range",
                ActionBarButtonId.Build, ActionBarButtonId.Bag,
                ActionBarButtonId.Quests, ActionBarButtonId.Upgrade);
            src.Talk = false; model.Tick();
            if (model.Active.Contains(ActionBarButtonId.Talk))
                failures.Add("Talk did not repack OUT when the NPC left range (hide, not dim)");

            // Raids: not capable => absent; capable + not full => visible AND dimmed (WO-820).
            // ⚠ WO-1008: "not capable" now means NO BARRACKS (or the flag off) — an empty army is
            // capable-and-dimmed, never absent. See RaidsDiscoverabilityRegression.
            src.Capable = false; src.ArmyReady = false; model.Tick();
            if (model.Active.Contains(ActionBarButtonId.Raids))
                failures.Add("Raids visible while NOT capable (no barracks / flag off) — hide default broken");
            if (model.RaidsDimmed)
                failures.Add("RaidsDimmed true while the face is absent");
            src.Capable = true; model.Tick();
            if (model.Active.Contains(ActionBarButtonId.Raids))
                failures.Add("Raids returned as a duplicate bottom-bar face; Journey is its stable door");
            if (model.RaidsDimmed)
                failures.Add("a dormant Raids face reported presentation state");
            src.ArmyReady = true; model.Tick();
            if (model.RaidsDimmed)
                failures.Add("full army did not restore the Raids face");

            // WO-911 — Map must NEVER pack into the bar again, in EITHER Onboarded state. The
            // WO-825 R4 gate is not "lost": the whole face moved into Bag (flag-gated there by
            // FeatureFlags.MapTab). A returning Map face is the regression now.
            src.Onboarded = false; model.Tick();
            if (model.Active.Contains(ActionBarButtonId.Map))
                failures.Add("Map is back on the bar (pre-onboard) — WO-911 moved it into Bag as a tab");
            src.Onboarded = true; model.Tick();
            if (model.Active.Contains(ActionBarButtonId.Map))
                failures.Add("Map is back on the bar (Onboarded) — WO-911 moved it into Bag as a tab; the bar is 6 faces");

            // WO-911 — the re-pointed Manage face is ALWAYS applicable in town, focus or not.
            // Gating it on a focused building is exactly the undiscoverability the WO removes.
            src.Focused = false; model.Tick();
            if (!model.Active.Contains(ActionBarButtonId.Upgrade))
                failures.Add("the Manage face (ActionBarButtonId.Upgrade, re-pointed) is absent with no focused " +
                             "building — it is the single door to the queues and must always be in town");
            src.Focused = true; model.Tick();
            if (!model.Active.Contains(ActionBarButtonId.Upgrade))
                failures.Add("the Manage face vanished when a building took focus");
            if (!model.Active.Contains(ActionBarButtonId.Quests))
                failures.Add("Quests vanished while a building is focused — the §3c split regressed to the relabel hijack");

            // Canonical order at the 6-face MAX (WO-911: Build, Talk, Bag, Raids, Quests, Manage).
            src.Talk = true; model.Tick();
            ExpectSet(model, failures, "four stable destinations order",
                ActionBarButtonId.Build, ActionBarButtonId.Bag,
                ActionBarButtonId.Quests, ActionBarButtonId.Upgrade);
            if (model.Active.Count > HudActionBarModel.MaxVisibleFaces)
                failures.Add($"the bar renders {model.Active.Count} faces but the View sizes slots from " +
                             $"MaxVisibleFaces = {HudActionBarModel.MaxVisibleFaces} — the group would overflow its zone");

            // WO-1236 cause pin: a dungeon forwards calm(explore). With no nearby talk target its
            // exact mask is Bag only (ordinal 2 => 0x04). The owner's capture was therefore a
            // correct one-face MASK, not a layout failure. Adding dungeon faces is an owner ruling.
            model.SetPosture(HudActionBarModel.PostureExplore);
            src.Talk = false; model.Tick();
            ExpectSet(model, failures, "peaceful explore dock",
                ActionBarButtonId.Build, ActionBarButtonId.Bag,
                ActionBarButtonId.Quests, ActionBarButtonId.Upgrade);
            src.Talk = true; model.Tick();
            ExpectSet(model, failures, "explore (talk contextual)",
                ActionBarButtonId.Build, ActionBarButtonId.Bag,
                ActionBarButtonId.Quests, ActionBarButtonId.Upgrade);
            model.SetPosture("build");
            if (model.Active.Count != 0)
                failures.Add("build posture did not empty the bar set");

            // Edge-triggered event contract.
            model.SetPosture(HudActionBarModel.PostureTown);
            int events = 0;
            model.ActiveButtonsChanged += () => events++;
            model.Tick(); model.Tick();
            if (events != 0)
                failures.Add($"ActiveButtonsChanged fired {events}x with no input change — per-frame relayout risk");
            model.SetPosture("build");
            if (events != 1)
                failures.Add($"one set change raised {events} events (expected exactly 1)");

            log.AppendLine("  model invariants (baseline/talk/raids hide+dim/map/quests-upgrade/explore/edge-trigger) OK-checked");
        }

        private static void CheckDungeonFlagAcknowledgement(List<string> failures, StringBuilder log)
        {
            string harnessPath = Path.Combine(Application.dataPath, "_Modules/Core/Diagnostics/BreakCaptureHarness.cs");
            string buttonPath = Path.Combine(Application.dataPath, "_Modules/Core/Dev/FlagCaptureButton.cs");
            if (!File.Exists(harnessPath) || !File.Exists(buttonPath))
            {
                failures.Add("[dungeon-flag-ack] flag presentation source missing");
                return;
            }

            string harness = File.ReadAllText(harnessPath);
            string button = File.ReadAllText(buttonPath);
            if (harness.IndexOf("HudLayoutBands.ToastZone", System.StringComparison.Ordinal) < 0)
                failures.Add("[dungeon-flag-ack] BreakCaptureHarness does not seat FLAGGED in the shared ToastZone");
            if (harness.IndexOf("new Rect(18, 16, 260, 34)", System.StringComparison.Ordinal) >= 0)
                failures.Add("[dungeon-flag-ack] retired top-left FLAGGED rect returned over the minimap");
            if (button.IndexOf("_label.text = FlaggedLabel", System.StringComparison.Ordinal) >= 0)
                failures.Add("[dungeon-flag-ack] FlagCaptureButton paints a second acknowledgement on its own chip");
            if (harness.IndexOf("_toastUntil = Time.realtimeSinceStartup + 1.6f", System.StringComparison.Ordinal) < 0)
                failures.Add("[dungeon-flag-ack] shared FLAGGED acknowledgement lost its realtime timeout");

            log.AppendLine("  WO-1236 dungeon flag acknowledgement: one timed ToastZone band, no minimap/second-chip seat");
        }

        private static void ExpectSet(HudActionBarModel model, List<string> failures,
                                      string label, params ActionBarButtonId[] expected)
        {
            var got = model.Active;
            bool match = got.Count == expected.Length;
            if (match)
                for (int i = 0; i < expected.Length; i++)
                    if (got[i] != expected[i]) { match = false; break; }
            if (!match)
                failures.Add($"{label}: expected [{string.Join(" ", expected)}], got [{string.Join(" ", got)}]");
        }

        // =====================================================================
        //  1b. THE MEASURED DOCK ORACLE (WO-1467) — the bar the player touches
        // =====================================================================
        // WHAT WAS WRONG. Case 1 above exercises HudActionBarModel, and until this ticket TWO
        // other suites asserted a literal against HudActionBarModel.MaxVisibleFaces as if that
        // constant described the shipped bottom bar. It does not, and cannot:
        // HudKitController.BindActionBar returns early whenever the adaptive peaceful dock exists
        // (it disables every legacy face and never subscribes the model at all), so on the
        // shipping path this model drives NOTHING. The dock the player actually touches is built
        // by HudKitController.BuildAdaptivePeacefulDock, and its only coverage was source-text
        // lint — which cannot see a face that moved, re-ordered, lost its caption, or fell under
        // the touch floor, because every one of those leaves the literals intact.
        //
        // WHAT THIS CASE DOES INSTEAD. It BUILDS the real dock on a throwaway HudKitController
        // (WO-1467 measurement hook: BuildPeacefulDockProbe, which calls the one builder and
        // returns the same root the runtime registers) and reads the answers OUT OF THE BUILT
        // TREE:
        //   * the face COUNT is discovered, never assumed — it is then fed into the shipping
        //     solver, so the geometry assertions below are measured against what was found;
        //   * the CAPTIONS are read off each slot's TMP_Text;
        //   * the ORDER is taken from anchorMin.x (the build-time seed HudDockSlotLayout later
        //     overwrites with the same left-to-right ordering), NOT from sibling index — a
        //     dock that builds its slots in the right order but seeds them in the wrong x is
        //     exactly the defect a lint cannot see;
        //   * the TOUCH FLOOR and LABEL FIT are re-derived through DeNelle.Core.UI.HudDockLayout
        //     at the shipping surfaces, because rects are 0 pre-layout in batch mode (the kit's
        //     own ClampMinTouch no-ops there) and asserting on them would be asserting on noise.
        //
        // ONE-LINE MUTATION THAT REDS IT TODAY: in HudKitController.BuildPeacefulDockSlot, change
        //   float x0 = gap + index * (width + gap);
        // to
        //   float x0 = gap + (count - 1 - index) * (width + gap);
        // Every BuildPeacefulDockSlot(i, "CAPTION" literal is untouched, so every source lint in
        // this repo stays green; this case fails with "face 0 is 'MANAGE', expected 'BUILD'".

        /// <summary>The faces the calm dock ships, left to right. This is the ONE place the set is
        /// written down, and it is asserted against a dock that was actually built — not against a
        /// constant, and not against the source text that authored it.</summary>
        private static readonly string[] ShippedDockKeys =
        {
            DeNelle.Core.UI.HudStrings.KeyNavBuild,
            DeNelle.Core.UI.HudStrings.KeyNavTalk,
            DeNelle.Core.UI.HudStrings.KeyNavHero,
            DeNelle.Core.UI.HudStrings.KeyNavJourney,
            DeNelle.Core.UI.HudStrings.KeyNavManage,
        };

        /// <summary>Shipping surfaces (w, h): a tall-phone landscape, a 16:9 desktop window and a
        /// tablet. The dock solves in reference pixels, so the aspect is the variable.
        /// ⭐ WO-1667 (b, follow-up 2026-09-10) — THE 1080x1920 PORTRAIT ROW IS REMOVED, AND IT IS
        /// AN OWNER RULING, NOT A CONVENIENCE.
        /// ---------------------------------------------------------------------
        /// The game is LANDSCAPE ONLY (owner ruling 2026-09-10, WO-1631). Proven at source, not
        /// taken from a doc:
        ///   * ProjectSettings/ProjectSettings.asset:63-64 — allowedAutorotateToPortrait: 0 and
        ///     allowedAutorotateToPortraitUpsideDown: 0;
        ///   * Assets/Editor/Regression/ScreenOrientationRegression.cs pins exactly that, and its
        ///     CASE 2 [no-portrait-autorotate] FAILS the build if either flag returns to 1;
        ///   * UICaptureLaunch.LandscapeTargets (:215-220) carries NO portrait row — 1920x1080,
        ///     2340x1080, 2670x1200.
        /// So this row asserted a presentation THE GAME REFUSES TO ENTER, and it was the only
        /// surface that solved to tier 2 (slot pinned at the MinSlotPx 112 touch floor, box 98.6).
        /// It failed chain 38 on 'JOURNEY' at 105.1 vs 98.6 — a RED over a screen no player can
        /// reach, which is the same "pin over a retired surface" defect WO-1666 was written to
        /// remove, one ticket later and in the opposite direction.
        /// ⛔ Do NOT re-add a portrait row here to "cover the phone": the phone IS landscape.
        /// ⚠ This list is PRIVATE to this suite (nothing else reads DockSurfaces), so removing the
        /// row changes no other oracle. Other 1080x1920 literals in the tree are the CanvasScaler
        /// REFERENCE resolution (HudDockLayout.CanvasLocalWidthPx's refW/refH default,
        /// UICaptureLaunch's scaler setup) — a different axis entirely, and NOT to be touched.
        /// ⚠ 2670x1200 (the Seeker's real surface, per LandscapeTargets) is deliberately NOT added
        /// here by this change: adding a surface is a scope decision, and its numbers are reported
        /// in the WO-1667 RESULT so the lead can rule on it.</summary>
        private static readonly float[][] DockSurfaces =
        {
            new[] { 2340f, 1080f },
            new[] { 1920f, 1080f },
            new[] { 2048f, 1536f },
        };

        private static void CheckMeasuredPeacefulDock(List<string> failures, StringBuilder log)
        {
            const string Tag = "[dock-measured]";
            GameObject probeGo = null;
            try
            {
                probeGo = new GameObject("WO1467_DockProbe", typeof(RectTransform));
                var kit = probeGo.AddComponent<DeNelle.HUD.Kit.HudKitController>();
                var pool = new GameObject("Pool", typeof(RectTransform));
                pool.transform.SetParent(probeGo.transform, false);

                GameObject dock = kit.BuildPeacefulDockProbe(pool.transform);
                if (dock == null)
                {
                    failures.Add(Tag + " BuildPeacefulDockProbe built no dock root — the bar the player " +
                                 "touches did not construct, so nothing below was measured");
                    return;
                }

                // ── read the faces out of the tree ───────────────────────────
                var captions = new List<string>();
                var seeds = new List<float>();
                int slotsWithoutOneCaption = 0;
                var dockRt = (RectTransform)dock.transform;
                for (int i = 0; i < dockRt.childCount; i++)
                {
                    var child = dockRt.GetChild(i) as RectTransform;
                    if (child == null) continue;
                    // A face is a tappable child. Register() deactivates the widget, so every
                    // search here is includeInactive — a false zero would read as a RED for
                    // entirely the wrong reason.
                    if (child.GetComponentInChildren<UnityEngine.UI.Button>(true) == null) continue;

                    // The caption is a DIRECT child of the slot root (ActionSlotHandle.SetCaption
                    // builds it at y 0.02..0.26 - the bottom strip). The slot's other TMP_Texts
                    // (cooldown seconds, stack count) are constructed EMPTY today, so the
                    // non-empty direct children are normally exactly one. Should a prefab-mode
                    // slot ever arrive carrying pre-filled text, the LOWEST band is still the
                    // caption strip - resolve it that way rather than red-flagging a working dock.
                    string word = null;
                    float bestBand = float.MaxValue;
                    int found = 0;
                    for (int c = 0; c < child.childCount; c++)
                    {
                        var kid = child.GetChild(c) as RectTransform;
                        if (kid == null) continue;
                        var t = kid.GetComponent<TMP_Text>();
                        if (t == null || string.IsNullOrEmpty(t.text) || t.text.Trim().Length == 0) continue;
                        found++;
                        if (kid.anchorMax.y >= bestBand) continue;
                        bestBand = kid.anchorMax.y;
                        word = t.text.Trim();
                    }
                    if (found == 0) { slotsWithoutOneCaption++; continue; }
                    captions.Add(word);
                    seeds.Add(child.anchorMin.x);

                    // ── WO-1671: the caption's obsidian backing plate ─────────
                    // RED-FIRST BY CONSTRUCTION: against HEAD before this ticket no slot carries a
                    // "CaptionPlate" child at all, so this block emits
                    //   "[dock-measured] face 'BUILD' has NO obsidian caption plate ..."
                    // for all five faces. It cannot pass on the old tree.
                    //
                    // WHY THESE FOUR ASSERTIONS AND NOT "a plate exists". Each one is a way the
                    // fix silently stops working while the object is still there:
                    //  * COVERS the caption rect on both axes - a plate that is narrower than the
                    //    word leaves the ends of "JOURNEY" back on the grass, which is exactly the
                    //    defect, with a green test next to it;
                    //  * DRAWS UNDER it (lower sibling index) - a plate above the caption hides the
                    //    word completely, and a "plate exists" test would call that a fix;
                    //  * IS THE KIT'S OBSIDIAN FILL BY VALUE - the owner ruled the kit idiom, never
                    //    a hand-tinted colour, and she is red/green colourblind so a drifted hue is
                    //    not something a felt-test will reliably catch;
                    //  * TAKES NO TAPS - a raycasting plate over the bottom of the medallion would
                    //    eat presses near the word.
                    var plateRt = child.Find(DeNelle.Core.UI.ElarionUiKit.CaptionPlateObjectName) as RectTransform;
                    var capRt = null as RectTransform;
                    for (int c = 0; c < child.childCount; c++)
                    {
                        var kid = child.GetChild(c) as RectTransform;
                        if (kid == null) continue;
                        var t = kid.GetComponent<TMP_Text>();
                        if (t != null && !string.IsNullOrEmpty(t.text) && t.text.Trim() == word)
                        { capRt = kid; break; }
                    }
                    if (plateRt == null)
                    {
                        failures.Add(Tag + " face '" + word + "' has NO obsidian caption plate (no '" +
                                     DeNelle.Core.UI.ElarionUiKit.CaptionPlateObjectName + "' child) — the " +
                                     "owner's device frames measure this caption band at 1.87–2.97:1 " +
                                     "against the terrain it draws over, under the 3:1 floor, and the word " +
                                     "is the only colour-free carrier of the face's meaning (WO-1671)");
                    }
                    else if (capRt != null)
                    {
                        if (plateRt.anchorMin.x > capRt.anchorMin.x + 1e-4f ||
                            plateRt.anchorMin.y > capRt.anchorMin.y + 1e-4f ||
                            plateRt.anchorMax.x < capRt.anchorMax.x - 1e-4f ||
                            plateRt.anchorMax.y < capRt.anchorMax.y - 1e-4f)
                            failures.Add(Tag + " face '" + word + "': the caption plate " +
                                         plateRt.anchorMin + ".." + plateRt.anchorMax +
                                         " does NOT cover the caption rect " + capRt.anchorMin + ".." +
                                         capRt.anchorMax + " — part of the word still reads against the world");
                        if (plateRt.GetSiblingIndex() >= capRt.GetSiblingIndex())
                            failures.Add(Tag + " face '" + word + "': the caption plate draws at sibling " +
                                         plateRt.GetSiblingIndex() + ", at or ABOVE the caption (" +
                                         capRt.GetSiblingIndex() + ") — it would cover the word instead of " +
                                         "backing it");
                        var plateImg = plateRt.GetComponent<UnityEngine.UI.Image>();
                        if (plateImg == null)
                            failures.Add(Tag + " face '" + word + "': the caption plate carries no Image — " +
                                         "nothing is drawn behind the word");
                        else
                        {
                            var want = DeNelle.Core.UI.ElarionUiKit.ObsidianFill;
                            var got = plateImg.color;
                            if (Mathf.Abs(got.r - want.r) > 0.01f || Mathf.Abs(got.g - want.g) > 0.01f ||
                                Mathf.Abs(got.b - want.b) > 0.01f || Mathf.Abs(got.a - want.a) > 0.01f)
                                failures.Add(Tag + " face '" + word + "': the caption plate is " + got +
                                             ", not the kit's ObsidianFill " + want + " — the owner ruled the " +
                                             "kit's obsidian idiom, never a hand-tinted colour");
                            if (plateImg.raycastTarget)
                                failures.Add(Tag + " face '" + word + "': the caption plate takes raycasts — " +
                                             "it would swallow taps aimed at the medallion");
                        }
                    }
                }

                if (slotsWithoutOneCaption > 0)
                    failures.Add(Tag + " " + slotsWithoutOneCaption + " dock face(s) carry NO caption — " +
                                 "the word is what makes the face readable without colour (the owner is " +
                                 "red/green colourblind), so an unlabelled medallion is a defect, not a style");

                // Left-to-right by the seeded x anchor, not by sibling index.
                var order = Enumerable.Range(0, captions.Count).ToList();
                order.Sort((a, b) => seeds[a].CompareTo(seeds[b]));
                var measured = order.Select(i => captions[i]).ToList();

                string[] expected = ShippedDockKeys.Select(DeNelle.Core.UI.HudStrings.Get).ToArray();
                if (measured.Count != expected.Length)
                {
                    failures.Add(Tag + " the built dock carries " + measured.Count + " face(s) [" +
                                 string.Join(" ", measured) + "], expected " + expected.Length +
                                 " [" + string.Join(" ", expected) + "]");
                }
                else
                {
                    for (int i = 0; i < measured.Count; i++)
                        if (!string.Equals(measured[i], expected[i], System.StringComparison.Ordinal))
                            failures.Add(Tag + " face " + i + " is '" + measured[i] + "', expected '" +
                                         expected[i] + "' — measured order [" +
                                         string.Join(" ", measured) + "]. A face that swaps places keeps " +
                                         "every source literal intact, which is why this is measured.");
                }

                // ── geometry, re-derived from the COUNT that was found ────────
                int n = measured.Count > 0 ? measured.Count : expected.Length;
                float headroom = DeNelle.HUD.Kit.HudAreasHost.ActionBarRightHeadroomRatio;
                float mountFrac = DeNelle.HUD.Kit.HudAreasHost.ActionBarMaxX -
                                  DeNelle.HUD.Kit.HudAreasHost.ActionBarMinX;
                foreach (var s in DockSurfaces)
                {
                    float mount = DeNelle.Core.UI.HudDockLayout.CanvasLocalWidthPx(s[0], s[1]) * mountFrac;
                    var sol = DeNelle.Core.UI.HudDockLayout.Solve(n, mount, mount * (1f + headroom));
                    string at = " at " + s[0].ToString("0") + "x" + s[1].ToString("0");

                    if (sol.Overflowed)
                    {
                        failures.Add(Tag + " the dock OVERFLOWS" + at + " with the " + n +
                                     " faces it actually builds — the touch floor is unreachable in one " +
                                     "row on a surface we ship");
                        continue;
                    }
                    if (sol.SlotWidthPx < DeNelle.Core.UI.HudDockLayout.MinSlotPx - 0.01f)
                        failures.Add(Tag + " a face solves to " + sol.SlotWidthPx.ToString("0.#") +
                                     " px" + at + ", under the touch floor (" +
                                     DeNelle.Core.UI.HudDockLayout.MinSlotPx.ToString("0") + " px)");

                    if (!sol.ShowCaptions) continue;   // icon-only tier: no word to fit
                    float boxW = sol.SlotWidthPx * CaptionInset;

                    // ⭐ WO-1667 (b) — THE CAPTION IS DRAWN BOLD, SO IT IS CHARGED FOR THE WEIGHT.
                    // ---------------------------------------------------------------------
                    // MeasureLineWidthPx sums REGULAR-weight advances and says so in its own docs
                    // (ElarionUiKitObsidian.cs:2896-2897). This caption is bold, so measuring it
                    // raw under-reported the shipped bar. Traced at source 2026-09-10:
                    //   ActionSlotHandle.SetCaption (ElarionUiKitObsidian.cs:1095) builds it with
                    //   Label(..., bold: true) and then EnsureFont(caption, FontRole.Body), and
                    //   ElarionUiKit.Label (:1905-1923) does `if (bold) t.fontStyle = FontStyles.Bold`.
                    //
                    // ⛔ THE ROLE STAYS Body, AND THAT IS DELIBERATE. EnsureFont installs Body on
                    // this caption EXPLICITLY; nothing on the dock path calls
                    // MedievalUiSkin.ApplyButton or EnsureFont(..., FontRole.Title). So this is
                    // NOT an obsidian face: do NOT re-point it to HudLabelFitRegression's
                    // MeasureFacePx / SkinnedFaceRole, which carry Title AND a characterSpacing-2
                    // allowance this caption does not earn (Label leaves spacing at 0 — SetCaption
                    // never passes one). Re-pointing to Title would make the LIVE-bar oracle
                    // falsely pessimistic, the hazard WO-1663 §7 kept three sites clear of.
                    //
                    // ONE constant, shared, not a third copy: see the reasoning at its declaration,
                    // HudLabelFitRegression.BoldOnlyWidthSlack (same assembly, internal).
                    const float bold = DeNelle.Editor.Regression.HudLabelFitRegression.BoldOnlyWidthSlack;
                    foreach (string word in measured)
                    {
                        float raw = DeNelle.Core.UI.ElarionUiKit.MeasureLineWidthPx(
                            DeNelle.Core.UI.ElarionUiKit.FontRole.Body, word,
                            DeNelle.Core.UI.ElarionUiKit.FontHardFloor, out string detail);
                        if (raw < 0f)
                        {
                            // -1 means NO font was resolvable, never "it fits". Say so rather than
                            // letting an unmeasurable caption read as a pass.
                            // ⛔ The slack is applied BELOW this branch on purpose: multiplying the
                            // sentinel would turn -1 into -1.1 and quietly break the "< 0" test
                            // every caller of MeasureLineWidthPx relies on.
                            log.AppendLine("  " + Tag + " caption '" + word + "' NOT MEASURED (" +
                                           detail + ") - label fit asserted nothing this run");
                            continue;
                        }
                        float w = raw * bold;

                        // PROOF-OF-TAKE (WO-1667 b4). This half is not expected to red — English
                        // captions fit even charged — so the evidence that the change took is the
                        // NUMBERS MOVING, printed per surface and per caption. A reader comparing
                        // two gate logs must be able to see raw -> charged and the box they ran at.
                        log.AppendLine("  " + Tag + " caption '" + word + "'" + at + ": raw " +
                                       raw.ToString("0.0") + " -> charged " + w.ToString("0.0") +
                                       " px (x" + bold.ToString("0.00") + " bold weight) in a " +
                                       boxW.ToString("0.0") + " px face (slot " +
                                       sol.SlotWidthPx.ToString("0.0") + " x inset " +
                                       CaptionInset.ToString("0.00") + ")");

                        if (w > boxW)
                            failures.Add(Tag + " the caption '" + word + "' MEASURES " + w.ToString("0.0") +
                                         " px at the hard font floor (regular advances " +
                                         raw.ToString("0.0") + " x" + bold.ToString("0.00") +
                                         " for the bold weight it is DRAWN at) against a " +
                                         boxW.ToString("0.0") +
                                         " px face" + at + " (" + detail + ") — it can only render " +
                                         "elided. ⛔ The remedy is FEWER CHARACTERS in the hud.nav.* " +
                                         "canon copy, or more room from the dock solver — never a " +
                                         "lower FontHardFloor, never a smaller CaptionInset, and " +
                                         "never weakening the weight term to make this pass");
                    }
                }

                log.AppendLine("  measured peaceful dock: [" + string.Join(" ", measured) + "] built live, " +
                               "ordered by seeded x, solved at " + DockSurfaces.Length + " shipping surfaces");
            }
            catch (System.Exception ex)
            {
                // NOT a stand-down. The dock is plain GameObject construction (HudUiRegression
                // already builds ElarionUiKit action slots headlessly), so a throw here means the
                // shipping bar cannot be constructed — that is the product defect this case exists
                // to catch, and swallowing it would restore the hollow green WO-1467 removed.
                failures.Add(Tag + " building the shipped peaceful dock THREW " + ex.GetType().Name +
                             ": " + ex.Message);
            }
            finally
            {
                if (probeGo != null) UnityEngine.Object.DestroyImmediate(probeGo);
            }
        }

        /// <summary>The caption strip is inset from the medallion rim (ActionSlotHandle.SetCaption
        /// authors it at x 0.06..0.94), so the word's box is not the whole face.</summary>
        private const float CaptionInset = 0.88f;

        // =====================================================================
        //  1c. THE MEASURED OUTSIDE DOCK (WO-1672) — the bar past the walls
        // =====================================================================
        // OWNER RULING 2026-09-10: "when you are outside the castle should not be the peaceful UI,
        // not combat, but should not be able to build or talk but can use items still."
        //
        // WHY THIS IS MEASURED THE SAME WAY, AND NOT LINTED. The whole point of the ruling is a
        // SET DIFFERENCE — two faces gone, one face added — and a set difference is precisely what
        // source text cannot prove: BuildAdaptiveOutsideDock could keep every literal in place and
        // still construct BUILD by copy-paste, or lose ITEM to an early return, with every lint
        // green. So this builds the real dock through BuildOutsideDockProbe (the twin of
        // BuildPeacefulDockProbe) and reads the faces out of the tree.
        //
        // ⛔ AND THE ONE TRAP THAT DECIDED THE IMPLEMENTATION. The face walk below is
        // GetComponentInChildren<Button>(true) — INCLUDE-INACTIVE, deliberately, because Register()
        // deactivates the dock root and an active-only walk would read zero faces and RED for
        // entirely the wrong reason. The consequence is that this oracle CANNOT distinguish a
        // SetActive(false) BUILD face from a live one. That is why the ruling is implemented as
        // ABSENT (never constructed) rather than disabled: "absent" is the only form of "the player
        // cannot build out here" that a test can hold on to. If the owner later rules
        // present-but-dead, this case must be rewritten to assert interactable/dim copy instead —
        // do not weaken it to "a BUILD face may exist".
        private static void CheckMeasuredOutsideDock(List<string> failures, StringBuilder log)
        {
            const string Tag = "[dock-outside]";
            GameObject probeGo = null;
            try
            {
                probeGo = new GameObject("WO1672_OutsideDockProbe", typeof(RectTransform));
                var kit = probeGo.AddComponent<DeNelle.HUD.Kit.HudKitController>();
                var pool = new GameObject("Pool", typeof(RectTransform));
                pool.transform.SetParent(probeGo.transform, false);

                GameObject dock = kit.BuildOutsideDockProbe(pool.transform);
                if (dock == null)
                {
                    failures.Add(Tag + " BuildOutsideDockProbe built no dock root — the bar the " +
                                 "player gets outside the walls did not construct");
                    return;
                }

                var captions = new List<string>();
                var seeds = new List<float>();
                var dockRt = (RectTransform)dock.transform;
                for (int i = 0; i < dockRt.childCount; i++)
                {
                    var child = dockRt.GetChild(i) as RectTransform;
                    if (child == null) continue;
                    if (child.GetComponentInChildren<UnityEngine.UI.Button>(true) == null) continue;
                    string word = null;
                    float bestBand = float.MaxValue;
                    for (int c = 0; c < child.childCount; c++)
                    {
                        var kid = child.GetChild(c) as RectTransform;
                        if (kid == null) continue;
                        var t = kid.GetComponent<TMP_Text>();
                        if (t == null || string.IsNullOrEmpty(t.text) || t.text.Trim().Length == 0) continue;
                        if (kid.anchorMax.y >= bestBand) continue;
                        bestBand = kid.anchorMax.y;
                        word = t.text.Trim();
                    }
                    if (word == null)
                    {
                        failures.Add(Tag + " an outside-dock face carries NO caption — the word is " +
                                     "the colour-free carrier of the face's meaning");
                        continue;
                    }
                    captions.Add(word);
                    seeds.Add(child.anchorMin.x);

                    // WO-1671 travels with the face: this dock's captions sit in the same band over
                    // the same terrain, so a plateless face here is the same defect one posture over.
                    if (child.Find(DeNelle.Core.UI.ElarionUiKit.CaptionPlateObjectName) == null)
                        failures.Add(Tag + " outside face '" + word + "' has NO obsidian caption " +
                                     "plate — outside the walls the ground behind the dock is the " +
                                     "same terrain that measured 1.87–2.97:1 in town (WO-1671)");
                }

                var order = Enumerable.Range(0, captions.Count).ToList();
                order.Sort((a, b) => seeds[a].CompareTo(seeds[b]));
                var measured = order.Select(i => captions[i]).ToList();

                // THE RULED SET. BUILD and TALK must be gone; ITEM must be there. HERO/JOURNEY/
                // MANAGE are UNRULED and are asserted as CARRIED OVER from the calm dock — if the
                // owner rules on them, change this array and this comment together.
                string[] expected =
                {
                    DeNelle.Core.UI.HudStrings.Get(DeNelle.Core.UI.HudStrings.KeyNavHero),
                    DeNelle.Core.UI.HudStrings.Get(DeNelle.Core.UI.HudStrings.KeyNavJourney),
                    DeNelle.Core.UI.HudStrings.Get(DeNelle.Core.UI.HudStrings.KeyNavManage),
                    "ITEM",
                };
                if (measured.Count != expected.Length ||
                    !measured.SequenceEqual(expected, System.StringComparer.Ordinal))
                    failures.Add(Tag + " the outside dock builds [" + string.Join(" ", measured) +
                                 "], expected [" + string.Join(" ", expected) + "]");

                // The ruling, stated as the two things it forbids — held separately from the set
                // above so a future re-order cannot make this pass by accident.
                string build = DeNelle.Core.UI.HudStrings.Get(DeNelle.Core.UI.HudStrings.KeyNavBuild);
                string talk = DeNelle.Core.UI.HudStrings.Get(DeNelle.Core.UI.HudStrings.KeyNavTalk);
                if (measured.Contains(build))
                    failures.Add(Tag + " the outside dock still carries a '" + build + "' face — the " +
                                 "owner ruled the player cannot build outside the castle");
                if (measured.Contains(talk))
                    failures.Add(Tag + " the outside dock still carries a '" + talk + "' face — the " +
                                 "owner ruled the player cannot talk outside the castle");
                if (!measured.Contains("ITEM"))
                    failures.Add(Tag + " the outside dock has NO ITEM face — the owner ruled the " +
                                 "player 'can use items still'");

                // Geometry, re-derived from the count FOUND, exactly as the calm case does.
                int n = measured.Count > 0 ? measured.Count : expected.Length;
                float headroom = DeNelle.HUD.Kit.HudAreasHost.ActionBarRightHeadroomRatio;
                float mountFrac = DeNelle.HUD.Kit.HudAreasHost.ActionBarMaxX -
                                  DeNelle.HUD.Kit.HudAreasHost.ActionBarMinX;
                foreach (var s in DockSurfaces)
                {
                    float mount = DeNelle.Core.UI.HudDockLayout.CanvasLocalWidthPx(s[0], s[1]) * mountFrac;
                    var sol = DeNelle.Core.UI.HudDockLayout.Solve(n, mount, mount * (1f + headroom));
                    string at = " at " + s[0].ToString("0") + "x" + s[1].ToString("0");
                    if (sol.Overflowed)
                        failures.Add(Tag + " the outside dock OVERFLOWS" + at + " with " + n + " faces");
                    else if (sol.SlotWidthPx < DeNelle.Core.UI.HudDockLayout.MinSlotPx - 0.01f)
                        failures.Add(Tag + " an outside face solves to " + sol.SlotWidthPx.ToString("0.#") +
                                     " px" + at + ", under the touch floor (" +
                                     DeNelle.Core.UI.HudDockLayout.MinSlotPx.ToString("0") + " px)");
                }

                // THE LAST HOP, and the one that was actually missing before this ticket: the HUD
                // already knew the hero was outside (HudContextEvaluator.IsInTownRing ->
                // HudContext.Overworld -> HudPosture.CalmExplore), but hud-areas.json listed the
                // SAME peacefulDock under calm(town) AND calm(explore), so the dock was identical
                // on both sides of the boundary. Assert the explore row now names this dock — a
                // perfectly-built outside dock that no posture row mounts is invisible.
                string resJson = Path.Combine(Application.dataPath, "Resources/Data/Canonical/hud-areas.json");
                string samJson = Path.Combine(Application.dataPath, "StreamingAssets/Data/Canonical/hud-areas.json");
                foreach (var p in new[] { resJson, samJson })
                {
                    if (!File.Exists(p)) continue;   // the calm case already fails a missing file
                    string json = File.ReadAllText(p);
                    int explore = json.IndexOf("calm(explore)", System.StringComparison.Ordinal);
                    if (explore < 0) { failures.Add(Tag + " hud-areas.json has no calm(explore) posture row: " + p); continue; }
                    // The row ends where the next posture begins.
                    int next = json.IndexOf("\"posture\"", explore, System.StringComparison.Ordinal);
                    string row = next > explore ? json.Substring(explore, next - explore) : json.Substring(explore);
                    if (row.IndexOf("\"outsideDock\"", System.StringComparison.Ordinal) < 0)
                        failures.Add(Tag + " hud-areas.json calm(explore) does not mount \"outsideDock\" — " +
                                     "the outside dock builds but no posture ever shows it: " + p);
                    if (row.IndexOf("\"peacefulDock\"", System.StringComparison.Ordinal) >= 0)
                        failures.Add(Tag + " hud-areas.json calm(explore) still mounts \"peacefulDock\" — " +
                                     "the player would get BUILD and TALK outside the castle: " + p);
                }

                log.AppendLine("  measured outside dock: [" + string.Join(" ", measured) + "] built live; " +
                               "BUILD/TALK absent by construction, ITEM present, calm(explore) mounts it");
            }
            catch (System.Exception ex)
            {
                failures.Add(Tag + " building the outside dock THREW " + ex.GetType().Name + ": " + ex.Message);
            }
            finally
            {
                if (probeGo != null) UnityEngine.Object.DestroyImmediate(probeGo);
            }
        }

        // ── 2. View purity (source oracle — the WO-835 architecture law) ──────
        private static void CheckViewPurity(List<string> failures, StringBuilder log)
        {
            string kitPath = Path.Combine(Application.dataPath, "_Modules/HUD/Kit/HudKitController.cs");
            if (!File.Exists(kitPath))
            {
                failures.Add("HudKitController.cs missing at " + kitPath);
                return;
            }
            string kitSrc = File.ReadAllText(kitPath);

            if (kitSrc.IndexOf("HudActionBarModel.Shared") < 0)
                failures.Add("HudKitController does not bind HudActionBarModel.Shared (the View lost its one action-bar input)");
            if (kitSrc.IndexOf("ActiveButtonsChanged") < 0)
                failures.Add("HudKitController does not consume ActiveButtonsChanged (repack render pass unbound)");
            if (kitSrc.IndexOf("\"upgradeButton\"") < 0)
                failures.Add("HudKitController does not register the upgradeButton face (WO-835 §3c split missing)");
            // ⚠ WO-1467 — the source lint that used to sit here (BuildPeacefulDockSlot(1, "TALK"
            // plus a `const int count = 5;` literal) is RETIRED, not weakened. It asserted the
            // TEXT that authors the dock; CheckMeasuredPeacefulDock now asserts the dock that text
            // produces — count, captions, order, touch floor and label fit, read from the built
            // tree. Keeping both would be the doubled state this repo keeps paying for: the lint
            // stays green through every re-order and every dropped caption, so its green was worth
            // nothing while looking like coverage.

            // The retired gate reads must NOT return to the View (predicates live in Core).
            if (kitSrc.IndexOf("GameStateService") >= 0)
                failures.Add("HudKitController reads GameStateService again — the Onboarded predicate belongs to HudActionBarModel");
            if (kitSrc.IndexOf("RaidEntryGate.ArmyStatus") >= 0)
                failures.Add("HudKitController reads RaidEntryGate.ArmyStatus again — the army-dim predicate belongs to HudActionBarModel");
            if (kitSrc.IndexOf(".interactable = PostureSignals.TalkAvailable") >= 0)
                failures.Add("HudKitController re-grew the Talk dim gate — availability belongs to HudActionBarModel");

            log.AppendLine("  View purity (model bound, upgrade face present, retired gate reads absent) OK");
        }

        // ── 3. wiring — mirror seam + Village publisher + occupancy rows ──────
        private static void CheckWiring(List<string> failures, StringBuilder log)
        {
            // Core mirror seam (the SetTalkAvailable pattern).
            var sig = typeof(PostureSignals);
            if (sig.GetProperty("RaidCapable") == null)
                failures.Add("PostureSignals.RaidCapable missing (the Village->Core mirror target)");
            if (sig.GetMethod("SetRaidCapable") == null)
                failures.Add("PostureSignals.SetRaidCapable missing (the producer seam)");
            if (sig.GetEvent("RaidCapableChanged") == null)
                failures.Add("PostureSignals.RaidCapableChanged missing (the edge event)");

            // Village publisher exists (typed — same asmdef family) + single-source law.
            var bridge = typeof(DeNelle.Village.RaidCapabilityHudBridge);
            if (!typeof(MonoBehaviour).IsAssignableFrom(bridge))
                failures.Add("RaidCapabilityHudBridge is not a MonoBehaviour bridge");
            string bridgePath = Path.Combine(Application.dataPath, "_Modules/Village/Troops/RaidCapabilityHudBridge.cs");
            if (!File.Exists(bridgePath))
            {
                failures.Add("RaidCapabilityHudBridge.cs missing at " + bridgePath);
            }
            else
            {
                string bridgeSrc = File.ReadAllText(bridgePath);
                // ⚠ WO-1008 (owner ask 2026-08-16) — the OLD assertion here demanded the bridge
                // CALL ArmyReadiness.Compute, because the visibility predicate used to include
                // ">=1 deployable troop". That clause was deliberately deleted: an empty army now
                // DIMS the face instead of hiding it. The WO-823 single-source law is not lost —
                // it moved to the surfaces that still judge readiness (RaidSelectionScreen.Open,
                // BuildTimerService.PublishArmyStatus), and RaidsDiscoverabilityRegression pins
                // that the troop clause never comes back here.
                if (bridgeSrc.IndexOf("StructureSingleton.IsBuilt") < 0)
                    failures.Add("RaidCapabilityHudBridge does not check StructureSingleton.IsBuilt (the raid-building half of the predicate)");
                if (bridgeSrc.IndexOf("SetRaidCapable") < 0)
                    failures.Add("RaidCapabilityHudBridge never publishes SetRaidCapable");
            }

            // Occupancy rows: the approved peacefulDock in BOTH dual copies (WO-1467: no face
            // count restated here — CheckMeasuredPeacefulDock owns that, measured);
            // copies identical (CanonicalJson law — the Work-button-dark lesson).
            string resJson = Path.Combine(Application.dataPath, "Resources/Data/Canonical/hud-areas.json");
            string samJson = Path.Combine(Application.dataPath, "StreamingAssets/Data/Canonical/hud-areas.json");
            foreach (var p in new[] { resJson, samJson })
            {
                if (!File.Exists(p)) { failures.Add("hud-areas.json missing: " + p); continue; }
                string json = File.ReadAllText(p);
                if (json.IndexOf("peacefulDock") < 0)
                    failures.Add("hud-areas.json missing peacefulDock (the approved calm HUD would be behavior-absent): " + p);
            }
            if (File.Exists(resJson) && File.Exists(samJson) &&
                File.ReadAllText(resJson) != File.ReadAllText(samJson))
                failures.Add("hud-areas.json dual copies diverged (Resources vs StreamingAssets — CanonicalJson law)");

            log.AppendLine("  wiring (RaidCapable seam + Village bridge + ArmyReadiness single-source + occupancy rows) OK");
        }

        private static bool Verdict(List<string> failures, StringBuilder log, out string reason)
        {
            if (failures.Count == 0)
            {
                reason = "HUD ACTION BAR OK — WO-835 applicability model invariants + View purity + " +
                         "RaidCapable seam/bridge + occupancy rows all hold";
                Debug.Log("HUD_ACTIONBAR_OK\n" + log);
                return true;
            }
            reason = $"HUD ACTION BAR: {failures.Count} failure(s): " + string.Join(" | ", failures);
            Debug.LogError($"HUD_ACTIONBAR_FAIL: {failures.Count} failure(s)\n" + log +
                           "\n - " + string.Join("\n - ", failures));
            return false;
        }
    }
}

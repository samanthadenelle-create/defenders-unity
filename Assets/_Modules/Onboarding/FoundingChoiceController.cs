// =============================================================================
// FoundingChoiceController — the founding "Default Town vs Build Your Own" screen
// (WO-748). Shown ONCE at founding, after PetSelect and BEFORE first hub entry.
// -----------------------------------------------------------------------------
// Assembly: DeNelle.Onboarding   Namespace: DeNelle.Onboarding
// Module isolation (port-spec Part 2, mirrors OnboardingFlow / PetSelectController):
// this references DeNelle.Core ONLY. It never touches DeNelle.Village — so the
// apply below is a CORE-ONLY GameState write, never a Village call.
//
// THE CHOICE (owner-requested 2026-07-18):
//   * "Default Town"   — drop in the prebuilt ring town, but as INDIVIDUALLY
//                        MOVABLE BaseLayout records (owner's explicit requirement).
//   * "Build Your Own" — today's blank template + the FTUE.
//
// HOW "Default Town" APPLIES (the reuse, resolving the coordinate landmine):
//   The prebuilt ring already exists as a scene bake (CastleHubBuilder) and there
//   is a proven ONE-SHOT writer — StrategicPlacementMigration.RunIfNeeded — that
//   converts each baked ring storefront into a BaseLayout PlacedStructureData
//   record AT ITS LIVE SCENE POSITION (grid-quantised via the live PlacementGrid),
//   then stands the bakes down so the records are the movable owners. On a normal
//   New Game that writer is disabled because ResetToNewGame sets
//   StrategicPlacementMigrated = true (the blank template).
//
//   So "Default Town" is simply: set StrategicPlacementMigrated = FALSE before the
//   Castle scene loads. With the marker false the injector keeps the baked ring
//   VISIBLE and the migration writer runs on Castle load, migrating the ring into
//   movable records at the LIVE grid cells — NOT the 2026-06 authored locals
//   (WO-748 RISK #1). "Build Your Own" leaves the marker true (blank template).
//
//   This is CORE-ONLY (a GameState field + Save) — no Village reference, no seam,
//   no coordinate authoring. The town positions come from the live scene, so the
//   merged-world castle flatten (WorldMergeBuilder.LowerCastleToGround) is honoured
//   automatically. See the RESIDUAL note at the foot of this file.
//
// UI: code-built uGUI on the Blink Obsidian kit (ElarionUiKit) — its own
// ScreenSpaceOverlay canvas, exactly like OnboardingFlow's coach-marks. NO UXML /
// UIDocument / PanelSettings (CLAUDE.md Section 8). ASCII-only copy (device tofu).
// Colour never carries meaning (owner colourblind) — each button is labelled.
// =============================================================================

using System;
using DeNelle.Core.Diagnostics;
using DeNelle.Core.State;
using DeNelle.Core.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DeNelle.Onboarding
{
    /// <summary>
    /// The founding layout choice. Presented once, after PetSelect and before the
    /// first hub load, by <see cref="PresentOrContinue"/>. "Default Town" flips
    /// <see cref="GameState.StrategicPlacementMigrated"/> to false (so the Castle-load
    /// migration writer converts the live baked ring into movable records); "Build
    /// Your Own" is a no-op (blank template + FTUE). Then it routes onward.
    /// </summary>
    public sealed class FoundingChoiceController : MonoBehaviour
    {
        public const string DefaultTownSelectedKey = "founding.default_town_selected";
        // The ornate frame has transparent shoulders beyond the kit's content rect. Let the
        // opaque body field run underneath those shoulders so the previous screen cannot show
        // through as two bright side strips (owner capture 2026-09-01).
        private const float BodyFillHorizontalOverscan = 0.06f;
        // Session latch — the choice is offered at most once per session. New Game +
        // this being pre-hub means it never needs to re-offer within a run.
        private static bool _decidedThisSession;

        private Action _onContinue;
        private bool _routed;
        private GameObject _canvas;

        // UIF: single-modal arbiter handle. Registering the forced founding choice makes it
        // visible to the back-button / battle-lock arbiter and closes any prior panel. The
        // close delegate routes onward (Continue) since this is a no-dismiss forced choice.
        private PanelHandle _panelHandle;

        /// <summary>
        /// True when a founding choice should be offered: a genuinely FRESH founding —
        /// the player has NOT completed onboarding and nothing is built yet (empty
        /// BaseLayout). A returning player (Onboarded) or one who already founded
        /// (non-empty BaseLayout) is never re-offered. Also false once decided this
        /// session, or when the save service is not ready.
        /// </summary>
        public static bool ShouldOffer
        {
            get
            {
                if (_decidedThisSession) return false;
                var svc = GameStateService.Instance;
                if (svc == null || svc.State == null) return false;
                if (svc.State.Onboarded) return false;                    // returning player
                var layout = svc.State.BaseLayout;
                bool blank = layout == null || layout.Count == 0;         // nothing founded yet
                return blank;
            }
        }

        /// <summary>
        /// Entry point the intro flow calls in place of <c>SceneRouter.GoCastle</c>.
        /// If a founding choice is due (<see cref="ShouldOffer"/>) it builds the
        /// two-button screen and invokes <paramref name="onContinue"/> only once the
        /// player picks. Otherwise it invokes <paramref name="onContinue"/> immediately
        /// (no screen). <paramref name="onContinue"/> is the "enter the hub" action —
        /// typically <c>SceneRouter.GoCastle</c>.
        /// </summary>
        public static void PresentOrContinue(Action onContinue)
        {
            // WO-769: gate the new-game founding on LOGIN-OR-GUEST first, then the founding
            // choice. This is the single new-game chokepoint (HeroSelect/PetSelect route here);
            // returning players (Title -> Continue) never pass through, so they aren't re-prompted.
            // LoginPanelController.PresentOrContinue skips itself when the player is already in
            // (connected or attested-wallet-bound — WO-837-B made that the only "already in"
            // signal), and Play-as-Guest always proceeds — the boot flow can never soft-lock.
            LoginPanelController.PresentOrContinue(() => PresentFoundingChoice(onContinue));
        }

        private static void PresentFoundingChoice(Action onContinue)
        {
            using var _ = FlowTrace.Enter("Founding", "FoundingChoiceController.PresentFoundingChoice");

            // Emergency rollback only. Default Town is normally ON: both founding paths are part
            // of the player contract. ff.defaulttown=0 can suppress the choice if a future live
            // regression is discovered without stranding new saves.
            if (!DeNelle.Core.FeatureFlags.FoundingDefaultTown)
            {
                FlowTrace.Step("Founding",
                    "founding choice SUPPRESSED - ff.defaulttown emergency rollback is OFF. " +
                    "Founding as Build Your Own (blank template + FTUE).");
                onContinue?.Invoke();
                return;
            }

            if (!ShouldOffer)
            {
                FlowTrace.Step("Founding",
                    "not offering the founding choice (already decided / returning player / already founded / no save) — continuing straight to the hub.");
                onContinue?.Invoke();
                return;
            }

            _decidedThisSession = true;   // this presentation IS the decision surface — never re-offer

            var host = new GameObject("FoundingChoiceUI");
            var ctrl = host.AddComponent<FoundingChoiceController>();
            ctrl._onContinue = onContinue;
            ctrl.Build();
            FlowTrace.Step("Founding", "founding choice presented (Default Town vs Build Your Own).");
        }

        // =====================================================================
        //  Overlay construction (code-built uGUI on the Blink Obsidian kit)
        // =====================================================================

        private void Build()
        {
            using var _ = FlowTrace.Enter("Founding", "FoundingChoiceController.Build (uGUI Obsidian)");

            // Own overlay canvas, well above the intro UIDocument. Parent it into the
            // active (intro) scene so it tears down with the scene on the hub load.
            _canvas = ElarionUiKit.BuildModalCanvas("FoundingChoiceCanvas", 31000);
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(
                _canvas, gameObject.scene);

            // Raycast-blocking scrim (mirrors OnboardingFlow) — swallows taps to the
            // intro UI beneath while the choice is up.
            var scrim = ElarionUiKit.AddImage(_canvas.transform, "Scrim",
                Vector2.zero, Vector2.one, new Color(0f, 0f, 0f, 0.94f), rounded: false);
            var scrimImg = scrim.GetComponent<Image>();
            if (scrimImg != null) scrimImg.raycastTarget = true;

            // Centred Obsidian panel — the shared Close is hidden (this is a forced
            // choice, no dismiss). withBackdrop:false — the scrim above already dims.
            // Cosmetic flag C (owner 2026-07-24): the panel was short (0.24-0.76) so the header
            // crowded the copy top and the copy band sat tight to the buttons. Raise it taller
            // (0.18-0.82) to open the header<->copy and copy<->button gaps.
            // OWNER F8 2026-08-05 (Seeker 2670x1200, "the two options render as ONE two-line
            // panel and slice the paragraph above"): 0.18-0.82 was still too short. Measured at
            // source: panelFracH 0.64 * postScaleCanvasH 965 = 618 units; the kit's close-band
            // reservation then raised the body floor to 0.359 (BuildObsidianPanel PROCEDURAL
            // path, ElarionUiKit.cs:796-810), leaving a ~313-unit body. The old 0.18-tall button
            // bands resolved to ~56px — UNDER MinTouchPx(112) — so ClampMinTouch grew each rect
            // ~28px ABOUT ITS CENTRE, closing the authored gap (buttons overlapped) and pushing
            // the top button up into the copy band. Taller panel (0.12-0.88) + the fixed-pixel
            // bands below remove BOTH symptoms at their one shared cause.
            var chrome = ElarionUiKit.BuildObsidianPanel(_canvas.transform,
                new LocalizedText("onboarding.founding_choice.title").Resolve(),
                new Vector2(0.12f, 0.12f), new Vector2(0.88f, 0.88f), onClose: null,
                withBackdrop: false);
            MedievalUiSkin.ApplyShell(chrome, compact: false);
            if (chrome.close != null) chrome.close.gameObject.SetActive(false);

            Transform body = chrome.layout != null && chrome.layout.body != null
                ? chrome.layout.body.transform
                : chrome.content.transform;

            // RECLAIM THE DEAD CLOSE BAND. The kit raises every body zone's floor to reserve
            // room for the ONE shared Close box (~36% of this panel's height here). THIS panel
            // HIDES that Close two lines above (chrome.close.SetActive(false) — a forced choice
            // has no dismiss), so the reservation is pure dead space. Restore the kit's DEFAULT
            // body floor (ZonesFor(null).body.y == 0.10, ElarionUiKit.cs:326). Valid ONLY because
            // the Close is hidden — do NOT re-enable the Close without reverting this.
            var bodyRt = body as RectTransform;
            if (bodyRt != null)
            {
                bodyRt.anchorMin = new Vector2(bodyRt.anchorMin.x, 0.10f);   // kit default; no Close to clear
                bodyRt.offsetMin = Vector2.zero;
            }

            // The frame art is transparent through its centre. Give the forced-choice
            // body the same opaque black-iron field as the rest of the reskinned modal
            // family so Hero Select cannot bleed through its copy and actions.
            var bodyFill = ElarionUiKit.AddImage(body, "FoundingBodyFill",
                Vector2.zero, Vector2.one, ElarionUiKit.ObsidianFill, rounded: false);
            var bodyFillRt = bodyFill.transform as RectTransform;
            if (bodyFillRt != null)
            {
                bodyFillRt.anchorMin = new Vector2(-BodyFillHorizontalOverscan, 0f);
                bodyFillRt.anchorMax = new Vector2(1f + BodyFillHorizontalOverscan, 1f);
                bodyFillRt.offsetMin = Vector2.zero;
                bodyFillRt.offsetMax = Vector2.zero;
            }
            bodyFill.transform.SetAsFirstSibling();
            var bodyFillImage = bodyFill.GetComponent<Image>();
            if (bodyFillImage != null) bodyFillImage.raycastTarget = false;

            // Body copy — teaches that BOTH options stay editable, so the choice is
            // low-stakes (owner: every building is movable).
            // WO-1857: the old comment here said this was an "inline ASCII literal (not a
            // canon key) so it never shows a missing-key placeholder" — that trade is
            // retired: the key is authored in every required locale, and an English-only
            // literal is the defect the sweep exists to remove.
            var copy = ElarionUiKit.Label(body,
                new LocalizedText("onboarding.founding_choice.body").Resolve(),
                0.56f, 0.94f, ElarionUi.Parchment, ElarionUi.FontBody,
                TextAlignmentOptions.Center, 0.06f, 0.94f);
            copy.textWrappingMode = TextWrappingModes.Normal;
            copy.raycastTarget = false;
            // PIN THE PARAGRAPH TO A FIXED-PIXEL BAND (owner F8 2026-08-05). The 0.56-0.94
            // fraction band shrank with the body, and the grown button below reached UP into it
            // and sliced the last line. A top-anchored FIXED band cannot be reached by any
            // future grow, at any resolution (CANON_GROUND_TRUTH 2026-08-02 Section 4: a text
            // band is fixed px >= the font's line box, never a fraction of a parent).
            // CopyBandH 160 >= 4 lines at the FontFloorMobile(30) line box (~34.5px).
            const float CopyBandH   = 160f;
            const float CopyTopPad  = 16f;
            var copyRt = copy.rectTransform;
            copyRt.anchorMin = new Vector2(0.06f, 1f);
            copyRt.anchorMax = new Vector2(0.94f, 1f);
            copyRt.pivot     = new Vector2(0.5f, 1f);          // seat by the TOP edge -> grows down
            copyRt.sizeDelta        = new Vector2(0f, CopyBandH);
            copyRt.anchoredPosition = new Vector2(0f, -CopyTopPad);
            // Autosize + truncate inside that fixed band, floored at the mobile readability
            // floor — the copy re-flows to fit; it never overflows onto the buttons.
            ElarionUiKit.FitBlock(copy, ElarionUi.FontFloorMobile);

            // Two stacked full-width buttons (large mobile touch targets). Meaning is in
            // the LABEL, not the colour (owner colourblind): "Default Town" and "Build Your Own".
            // Owner 2026-07-25: the two choices must read as SEPARATE buttons (wider gap, not
            // edge-connected) + a benefit subtitle on each (inline, single-line-safe).
            //
            // OWNER F8 2026-08-05 — THE FIX. The two hand-placed 0.18-of-body bands resolved to
            // ~56px on the Seeker, under MinTouchPx(112); ClampMinTouch then grew each rect
            // symmetrically about its centre, which CLOSED the authored gap (the two buttons
            // read as one two-line panel) and pushed the upper one into the copy. Use the kit's
            // shared fixed-pixel button COLUMN instead — the canonical fix the kit already ships
            // for exactly this defect class (ElarionUiKitObsidian.cs:574-615): a
            // VerticalLayoutGroup with a FIXED px gap, each row pinned to a FIXED px height by a
            // LayoutElement. No band is a fraction of the body, every row is >= MinTouchPx, so
            // ClampMinTouch can never fire here at ANY resolution.
            //
            // STYLE1 FOR BOTH (2026-08-05): ObsidianButtonSpriteName (ElarionUiKitObsidian.cs:525-532)
            // resolves "button<style>_gray" — colour was standardized to grey game-wide but the
            // STYLE still picks the sprite, and button1_gray is SQUARE while button2_gray is
            // ROUNDED. That mismatch IS the owner's "one rounded, one square" report. Style carries
            // no semantic on this panel (meaning is in the label), so both rows use Style1.
            const float BtnH   = ElarionUiKit.CanonCtaHeight;   // 132 px, >= MinTouchPx 112
            const float BtnGap = 28f;                           // FIXED px gap, never a fraction
            const float BtnBottomPad = 28f;

            var column = ElarionUiKit.BuildButtonColumn(body, BtnGap, 0.08f, 0f, 0f);
            column.anchorMin = new Vector2(0.08f, 0f);
            column.anchorMax = new Vector2(0.92f, 0f);
            column.pivot     = new Vector2(0.5f, 0f);           // seat by the BOTTOM edge -> grows up
            column.sizeDelta        = new Vector2(0f, BtnH * 2f + BtnGap);
            column.anchoredPosition = new Vector2(0f, BtnBottomPad);

            // ⚠ WO-1857 REGRESSION NEEDLE: FoundingReachabilityRegression.cs:123-126 asserts
            // this file's SOURCE TEXT still contains the two English button captions
            // verbatim. Those two needles must be re-pointed at the two KEYS below.
            // Deliberately NOT quoted here: a comment carrying the old caption would make
            // that pin pass on a comment, which is worse than a red pin. Reported in the
            // lane sidecar; this lane may not edit Assets/Editor/Regression.
            var readyButton = ElarionUiKit.AddColumnButton(column,
                new LocalizedText("onboarding.founding_choice.ready_settlement").Resolve(),
                ElarionUiKit.ObsidianButtonColor.Green, OnDefaultTown,
                ElarionUiKit.ObsidianButtonStyle.Style1, BtnH);
            var emptyButton = ElarionUiKit.AddColumnButton(column,
                new LocalizedText("onboarding.founding_choice.empty_realm").Resolve(),
                ElarionUiKit.ObsidianButtonColor.Gray, OnBuildYourOwn,
                ElarionUiKit.ObsidianButtonStyle.Style1, BtnH);
            FitChoiceLabel(readyButton);
            FitChoiceLabel(emptyButton);

            // UIF: join the single-modal arbiter. isOpen reflects the live overlay; the close
            // delegate routes onward (Continue) — the arbiter's back/close proceeds to the hub.
            if (_panelHandle == null)
                _panelHandle = PanelManager.Register("FoundingChoice", Continue, () => !_routed && _canvas != null);
            PanelManager.NotifyOpened(_panelHandle);
        }

        private static void FitChoiceLabel(Button button)
        {
            if (button == null) return;
            var label = button.GetComponentInChildren<TMP_Text>(true);
            if (label != null) ElarionUiKit.FitSingleLine(label, 20f, 32f);
        }

        // =====================================================================
        //  Choices
        // =====================================================================

        /// <summary>
        /// "Default Town" — flip StrategicPlacementMigrated to false so the Castle-load
        /// migration writer converts the LIVE baked ring into movable BaseLayout records,
        /// then continue to the hub. GRANTED: no cost, no Place(), does NOT touch
        /// FreeBuildsUsed (the one-free-total first placement is preserved).
        /// </summary>
        private void OnDefaultTown()
        {
            if (_routed) return;
            FlowTrace.Step("Founding", "choice = DEFAULT TOWN.");

            var svc = GameStateService.Instance;
            if (svc == null || svc.State == null)
            {
                // Save service vanished between offer and tap — cannot arm the town.
                // Fail-loud, then fall through to Build-Your-Own (blank) rather than strand.
                FlowTrace.Fail("Founding",
                    "Default Town chosen but GameStateService is null — cannot clear StrategicPlacementMigrated; " +
                    "falling back to the blank template.");
                Continue();
                return;
            }

            // Guard the state write (Section 12): one bad op logs (break-log) and is
            // skipped, never a silent failure. Clearing the marker re-enables the proven
            // one-shot migration writer, which reads LIVE positions (RISK #1 resolved).
            bool ok = Guard.Try("Founding", "arm Default Town (clear StrategicPlacementMigrated)", () =>
            {
                svc.State.StrategicPlacementMigrated = false;
                svc.MarkTutorialSeen(DefaultTownSelectedKey);
                svc.Save();
            });

            DeNelle.Core.Analytics.EventTracker.Track("founding_path_selected", new
            {
                path = "starter_settlement",
                recommended = true,
            });

            if (ok)
                FlowTrace.Step("Founding",
                    "StrategicPlacementMigrated cleared + saved — the Castle-load migration writer will convert " +
                    "the live baked ring into MOVABLE BaseLayout records at the live grid cells. " +
                    "(Handover contract: the ring is visible on the founding load and becomes movable on the next hub load.)");
            else
                FlowTrace.Warn("Founding",
                    "Default Town arm FAILED (see the Guard line above) — the marker may still be set; the hub could load blank. " +
                    "Residual: verify StrategicPlacementMigrated == false in the save before entering the hub.");

            Continue();
        }

        /// <summary>"Build Your Own" — no-op: leave the blank template + FTUE. Continue.</summary>
        private void OnBuildYourOwn()
        {
            if (_routed) return;
            FlowTrace.Step("Founding", "choice = BUILD YOUR OWN (blank template + FTUE) — no state change.");
            DeNelle.Core.Analytics.EventTracker.Track("founding_path_selected", new
            {
                path = "start_from_scratch",
                recommended = false,
            });
            Continue();
        }

        /// <summary>Tear the overlay down and invoke the continue action exactly once.</summary>
        private void Continue()
        {
            if (_routed) return;
            _routed = true;
            // UIF: release the arbiter slot (no-op if already swapped out).
            if (_panelHandle != null) PanelManager.NotifyClosed(_panelHandle);
            var cont = _onContinue;
            _onContinue = null;
            if (_canvas != null) Destroy(_canvas);
            Destroy(gameObject);
            // Owner felt-test 2026-07-24: cont is SceneRouter.GoCastle — a fade-load of the
            // big Castle/hub scene (streams OuterWorld additively). With this founding overlay
            // torn down above, the screen would otherwise go blank/frozen for the whole load.
            // Put a loading cover up FIRST; it DontDestroyOnLoads and auto-dismisses once the
            // first hub frame settles. Both buttons funnel through here, so this covers both.
            DeNelle.Core.UI.LoadingOverlay.Show("Founding your town...");
            cont?.Invoke();
        }

        private void OnDestroy()
        {
            // UIF: don't leak the arbiter slot if destroyed while open (scene unload).
            if (_panelHandle != null) PanelManager.NotifyClosed(_panelHandle);
            if (_canvas != null) Destroy(_canvas);
        }
    }
}

// =============================================================================
// RESIDUAL RISKS FLAGGED FOR THE PO / HEADLESS VERIFY (WO-748) — could not be
// fully validated from static reading; the orchestrator should confirm headless:
//
//  R1 (RISK #1 coordinate mismatch — RESOLVED by design, verify):
//      Default Town does NOT author cell coordinates. It re-enables
//      StrategicPlacementMigration.RunIfNeeded, which reads each baked ring
//      storefront's LIVE world position and grid-quantises it via the live
//      PlacementGrid. So the merged-world castle flatten is honoured. VERIFY the
//      baked ring objects (Blacksmith_Weapons_Storefront, Lumbermill_Wood_Storefront,
//      Windmill_Food_Storefront, EchoHollow_Pets_RoamingArea, Forge_Armor_Storefront,
//      ArcaneTower_MagicUpgrades, Marketplace_Monetization) are actually present +
//      ACTIVE in the live home hub (Main_Castle_Overworld / MainCastle_Hall) on the
//      founding load with the marker false — RunIfNeeded's FindByName excludes
//      inactive objects, so any that are baked-inactive migrate as "absent" (skipped).
//
//  R2 (handover timing): with the marker cleared, the migration writer runs on the
//      FIRST hub load and latches that scene handle, so the records replay as MOVABLE
//      on the NEXT hub load (the established one-shot-migration contract). On the
//      founding load itself the player sees the baked ring (immovable that session).
//      This matches how legacy saves migrate. Confirm this is acceptable founding UX.
//
//  R3 (entry coverage): this is presented from PetSelectController's route-to-hub
//      (the canonical Start New chokepoint: card-pick + BypassPetSelect). If any
//      OTHER path reaches the hub for a fresh founding WITHOUT passing PetSelect
//      (e.g. a Play-Intro cinematic that routes straight to GoCastle), the choice is
//      not offered and that player gets the blank template. Verify the live intro
//      graph funnels every fresh founding through PetSelect.
// =============================================================================

// =============================================================================
// HomesSwitcherPanel - WO-1884 first-class Homes plate (castle <-> owned town).
// -----------------------------------------------------------------------------
// Assembly: DeNelle.HUD   Namespace: DeNelle.HUD
//
// Thin code-built Obsidian modal. Two rows only: Elarion (SceneRouter.GoCastle)
// and Your town (SceneRouter.GoOwnedTown). The row for the ACTIVE scene is marked
// and not tappable; the other is the gold face. Never restores OwnedTownPanel.
// Core only: SceneRouter + OwnedBaseProgression. ASCII TMP. FlowTrace kept.
// =============================================================================

using System;
using DeNelle.Core;
using DeNelle.Core.Diagnostics;
using DeNelle.Core.State;
using DeNelle.Core.UI;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace DeNelle.HUD
{
    [DisallowMultipleComponent]
    public sealed class HomesSwitcherPanel : MonoBehaviour
    {
        private const string Sys = "Homes";

        private ElarionUiKit.ObsidianModal _modal;
        private PanelHandle _panelHandle;
        private Button _castleBtn;
        private Button _townBtn;
        private TMP_Text _castleLabel;
        private TMP_Text _townLabel;

        public bool IsOpen => _modal != null && _modal.canvas != null && _modal.canvas.activeSelf;

        private void Awake()
        {
            _panelHandle = PanelManager.Register("Homes", Close, () => IsOpen);
            PanelRouter.Register(PanelId.Homes, Open);
            FlowTrace.Step(Sys, "HomesSwitcherPanel registered PanelId.Homes");
        }

        private void OnDestroy()
        {
            PanelRouter.Unregister(PanelId.Homes, Open);
            if (IsOpen) PanelManager.NotifyClosed(_panelHandle);
            if (_modal != null && _modal.canvas != null) Destroy(_modal.canvas);
        }

        public void Open()
        {
            Guard.Try(Sys, "open Homes switcher", OpenCore);
        }

        private void OpenCore()
        {
            if (!OwnedBaseProgression.Validate(
                    GameStateService.Instance?.State?.OwnedBase, out var reason))
            {
                FlowTrace.Warn(Sys, "Homes open refused - no valid owned base: " + reason);
                return;
            }

            EnsureBuilt();
            if (_modal == null || _modal.canvas == null)
            {
                FlowTrace.Fail(Sys, "Homes modal build failed - nothing to show.");
                return;
            }

            RefreshRows();
            _modal.canvas.SetActive(true);
            if (!PanelManager.NotifyOpened(_panelHandle))
            {
                FlowTrace.Warn(Sys, "PanelManager refused Homes open (a lock is held).");
                Close();
                return;
            }

            FlowTrace.Step(Sys, "Homes opened scene=" + SceneManager.GetActiveScene().name);
        }

        public void Close()
        {
            if (_modal != null && _modal.canvas != null) _modal.canvas.SetActive(false);
            PanelManager.NotifyClosed(_panelHandle);
        }

        private void EnsureBuilt()
        {
            if (_modal != null && _modal.canvas != null) return;

            _modal = ElarionUiKit.BuildObsidianModal(
                "HomesSwitcher",
                LocalText.Get("ownedTown.homes"),
                new Vector2(0.22f, 0.28f), new Vector2(0.78f, 0.72f),
                Close,
                frameName: RpgUiCatalog.FrameCore,
                medallionIcon: "castle");

            var body = _modal.chrome.layout != null && _modal.chrome.layout.body != null
                ? (Transform)_modal.chrome.layout.body
                : _modal.chrome.content.transform;

            if (_modal.chrome.title != null)
                LocalizedLabel.Attach(_modal.chrome.title, "ownedTown.homes");

            _castleBtn = ElarionUiKit.BuildObsidianButton(
                body, LocalText.Get("ownedTown.castle"),
                ElarionUiKit.ObsidianButtonStyle.Style1, ElarionUiKit.ObsidianButtonColor.Yellow,
                new Vector2(0.08f, 0.52f), new Vector2(0.92f, 0.78f), OnCastleTapped);
            _townBtn = ElarionUiKit.BuildObsidianButton(
                body, LocalText.Get("ownedTown.enter"),
                ElarionUiKit.ObsidianButtonStyle.Style1, ElarionUiKit.ObsidianButtonColor.Yellow,
                new Vector2(0.08f, 0.18f), new Vector2(0.92f, 0.44f), OnTownTapped);

            if (_castleBtn != null)
            {
                _castleBtn.gameObject.name = "HomesRow_Castle";
                _castleLabel = _castleBtn.GetComponentInChildren<TMP_Text>(true);
                if (_castleLabel != null)
                {
                    LocalizedLabel.Attach(_castleLabel, "ownedTown.castle");
                    ElarionUiKit.FitSingleLine(_castleLabel, 28f, 40f);
                }
            }
            if (_townBtn != null)
            {
                _townBtn.gameObject.name = "HomesRow_Town";
                _townLabel = _townBtn.GetComponentInChildren<TMP_Text>(true);
                if (_townLabel != null)
                {
                    LocalizedLabel.Attach(_townLabel, "ownedTown.enter");
                    ElarionUiKit.FitSingleLine(_townLabel, 28f, 40f);
                }
            }
        }

        private void RefreshRows()
        {
            string scene = SceneManager.GetActiveScene().name;
            bool atTown = HubScenes.IsOwnedTown(scene);
            bool atCastle = IsCastleScene(scene);

            // Re-resolve base labels first (LocalizedLabel may have stamped them).
            if (_castleLabel != null) _castleLabel.text = LocalText.Get("ownedTown.castle");
            if (_townLabel != null) _townLabel.text = LocalText.Get("ownedTown.enter");

            // Current home: gray + not tappable + ASCII "(here)". Other: gold face.
            ApplyRow(_castleBtn, _castleLabel, current: atCastle);
            ApplyRow(_townBtn, _townLabel, current: atTown);
        }

        private static void ApplyRow(Button btn, TMP_Text label, bool current)
        {
            if (btn == null) return;
            btn.interactable = !current;
            MedievalUiSkin.ApplyButton(btn, primary: !current);
            if (label == null) return;
            if (current)
            {
                string text = label.text ?? "";
                if (text.IndexOf("(here)", StringComparison.OrdinalIgnoreCase) < 0)
                    label.text = text + " (here)";
            }
            ElarionUiKit.FitSingleLine(label, 28f, 40f);
        }

        private void OnCastleTapped()
        {
            Guard.Try(Sys, "switch to Elarion (castle)", () =>
            {
                if (IsCastleScene(SceneManager.GetActiveScene().name))
                {
                    FlowTrace.Step(Sys, "castle row tapped while already at castle - no switch");
                    return;
                }
                FlowTrace.Step(Sys, "Homes switch -> SceneRouter.GoCastle");
                Close();
                SceneRouter.GoCastle();
            });
        }

        private void OnTownTapped()
        {
            Guard.Try(Sys, "switch to owned town", () =>
            {
                if (HubScenes.IsOwnedTown(SceneManager.GetActiveScene().name))
                {
                    FlowTrace.Step(Sys, "town row tapped while already at owned town - no switch");
                    return;
                }
                FlowTrace.Step(Sys, "Homes switch -> SceneRouter.GoOwnedTown");
                Close();
                SceneRouter.GoOwnedTown();
            });
        }

        private static bool IsCastleScene(string sceneName)
        {
            if (string.IsNullOrEmpty(sceneName)) return false;
            var candidates = SceneRouter.CastleCandidates;
            for (int i = 0; i < candidates.Length; i++)
            {
                if (string.Equals(sceneName, candidates[i], StringComparison.Ordinal))
                    return true;
            }
            return false;
        }
    }
}

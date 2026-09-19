// =============================================================================
// VigilCeremonyPanel - the Ceremony of Vigil plate (WO-1874).
// -----------------------------------------------------------------------------
// Assembly: DeNelle.HUD   Namespace: DeNelle.HUD
//
// A DUMB SKIN over VigilCeremonyVM. Code-built uGUI via ElarionUiKit. ASCII only.
// Colour never meaning alone. Skip after the first frame. Never opens in a raid.
// =============================================================================

using System;
using System.Collections;
using DeNelle.Core;
using DeNelle.Core.Diagnostics;
using DeNelle.Core.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DeNelle.HUD
{
    [DisallowMultipleComponent]
    public sealed class VigilCeremonyPanel : MonoBehaviour
    {
        private const string Sys = "VigilCeremony";
        private const float SequenceWaitSeconds = 3.2f;

        private ElarionUiKit.ObsidianModal _modal;
        private Transform _body;
        private PanelHandle _panelHandle;
        private VigilCeremonyVM _vm;
        private Coroutine _wait;
        private bool _skipArmed;
        private bool _skipped;
        private bool _plateShown;
        private bool _replay;
        private Action _sequenceHandler;
        private static bool s_playOnce;

        private TextMeshProUGUI _held;
        private TextMeshProUGUI _word;
        private TextMeshProUGUI _line;
        private TextMeshProUGUI _chosen;
        private TextMeshProUGUI _perk;
        private TextMeshProUGUI _next;
        private Button _continue;
        private GameObject _plateRoot;
        private Button _skipCatcher;

        public bool IsOpen => _modal != null && _modal.canvas != null;

        private void Awake()
        {
            _panelHandle = PanelManager.Register("CeremonyOfVigil", Close, () => IsOpen);
            PanelRouter.Register(PanelId.CeremonyOfVigil, (Action)Open);
            PanelRouter.Register(PanelId.CeremonyOfVigil, (Action<string>)OpenWithContext);
            FlowTrace.Step(Sys, "VigilCeremonyPanel registered PanelId.CeremonyOfVigil");
        }

        private void OnDestroy()
        {
            PanelRouter.Unregister(PanelId.CeremonyOfVigil, (Action)Open);
            PanelRouter.Unregister(PanelId.CeremonyOfVigil, (Action<string>)OpenWithContext);
            Teardown();
        }

        /// <summary>Plain open is a replay of the last vigil (Ballots door + router).</summary>
        public void Open() => OpenWithContext("replay");

        public void OpenWithContext(string context)
        {
            if (string.Equals(context, "play", StringComparison.OrdinalIgnoreCase))
            {
                OpenCannedPlay();
                return;
            }
            bool replay = string.Equals(context, "replay", StringComparison.OrdinalIgnoreCase)
                          || string.IsNullOrEmpty(context);
            OpenInternal(replay);
        }

        /// <summary>
        /// QA extra dotr.ceremony=play: open the last ledger payload once, no live fetch.
        /// </summary>
        private void OpenCannedPlay()
        {
            if (s_playOnce)
            {
                FlowTrace.Step(Sys, "canned ceremony plate already shown this process — not looping.");
                return;
            }
            if (RaidBlocks())
            {
                FlowTrace.Warn(Sys, "CeremonyOfVigil play refused — never blocks a raid.");
                return;
            }
            if (IsOpen) Close();
            EnsureVm();
            var payload = _vm.Ledger != null ? _vm.Ledger.LastPayload : null;
            if (payload == null || !payload.HasContent)
            {
                FlowTrace.Warn(Sys, "canned ceremony play has no payload — not opening the plate.");
                return;
            }
            _vm.ApplyCannedPayload(payload);
            if (!_vm.HasPayload)
            {
                FlowTrace.Warn(Sys, "canned ceremony apply produced no payload — not opening.");
                return;
            }
            OpenInternal(replay: false);
            if (IsOpen)
            {
                s_playOnce = true;
                _vm.RememberIfNeeded();
            }
        }

        /// <summary>
        /// Hub entry: fetch, dress immediately, auto-play the plate only when the
        /// epoch is unseen, the Circle passed, and this is not a raid.
        /// </summary>
        public void Consider()
        {
            if (RaidBlocks())
            {
                FlowTrace.Step(Sys, "Consider skipped — raid / enemy-owned scene.");
                return;
            }

            EnsureVm();
            _vm.Fetch(() =>
            {
                if (_vm == null) return;
                if (_vm.ShouldAutoPlay) OpenInternal(replay: false);
            });
        }

        private void OpenInternal(bool replay)
        {
            if (RaidBlocks())
            {
                FlowTrace.Warn(Sys, "CeremonyOfVigil refused — never blocks a raid.");
                return;
            }
            if (IsOpen) return;

            _replay = replay;
            _skipArmed = false;
            _skipped = false;
            _plateShown = false;
            EnsureVm();
            if (replay) _vm.MarkReplay();

            using var _ = FlowTrace.Enter(Sys, "VigilCeremonyPanel.Open");

            Action start = () =>
            {
                if (_vm == null || !_vm.HasPayload)
                {
                    FlowTrace.Warn(Sys, "no ceremony payload — not opening the plate.");
                    return;
                }
                BuildModal();
                if (_modal == null || _modal.canvas == null) return;
                if (!PanelManager.NotifyOpened(_panelHandle))
                {
                    FlowTrace.Warn(Sys, "PanelManager refused CeremonyOfVigil (a lock is held).");
                    Teardown();
                    return;
                }
                _vm.BeginPresentation();
                if (_wait != null) StopCoroutine(_wait);
                _wait = StartCoroutine(WaitThenShowPlate());
            };

            if (_vm.Fetched && _vm.HasPayload) start();
            else _vm.Fetch(start);
        }

        private void BuildModal()
        {
            Guard.Try(Sys, "build the vigil ceremony plate", () =>
            {
                _modal = ElarionUiKit.BuildObsidianModal("VigilCeremonyUI", _vm.Title,
                    new Vector2(0.16f, 0.10f), new Vector2(0.84f, 0.90f), OnContinue,
                    frameName: RpgUiCatalog.FrameCore, medallionIcon: "crest");

                _body = _modal.chrome.layout != null && _modal.chrome.layout.body != null
                    ? (Transform)_modal.chrome.layout.body
                    : _modal.chrome.content.transform;

                var skipGo = new GameObject("SkipCatcher", typeof(RectTransform), typeof(Image), typeof(Button));
                skipGo.transform.SetParent(_modal.canvas.transform, false);
                var skipRt = (RectTransform)skipGo.transform;
                skipRt.anchorMin = Vector2.zero;
                skipRt.anchorMax = Vector2.one;
                skipRt.offsetMin = Vector2.zero;
                skipRt.offsetMax = Vector2.zero;
                var skipImg = skipGo.GetComponent<Image>();
                skipImg.color = new Color(0f, 0f, 0f, 0.01f);
                skipImg.raycastTarget = true;
                _skipCatcher = skipGo.GetComponent<Button>();
                _skipCatcher.onClick.AddListener(OnSkip);

                _plateRoot = new GameObject("Plate", typeof(RectTransform));
                _plateRoot.transform.SetParent(_body, false);
                var plateRt = (RectTransform)_plateRoot.transform;
                plateRt.anchorMin = Vector2.zero;
                plateRt.anchorMax = Vector2.one;
                plateRt.offsetMin = Vector2.zero;
                plateRt.offsetMax = Vector2.zero;
                _plateRoot.SetActive(false);

                _held = ElarionUiKit.Label(_plateRoot.transform, "", 0.82f, 0.94f,
                    ElarionUi.Parchment, ElarionUi.FontLabel, TextAlignmentOptions.Center, 0.06f, 0.94f);
                _held.textWrappingMode = TextWrappingModes.Normal;

                _word = ElarionUiKit.Label(_plateRoot.transform, "", 0.70f, 0.82f,
                    ElarionUi.Gold, ElarionUi.FontBody, TextAlignmentOptions.Center, 0.06f, 0.94f);

                _line = ElarionUiKit.Label(_plateRoot.transform, "", 0.54f, 0.70f,
                    ElarionUi.Parchment, ElarionUi.FontBody, TextAlignmentOptions.Center, 0.06f, 0.94f);
                _line.textWrappingMode = TextWrappingModes.Normal;

                _chosen = ElarionUiKit.Label(_plateRoot.transform, "", 0.42f, 0.54f,
                    ElarionUi.Parchment, ElarionUi.FontLabel, TextAlignmentOptions.Center, 0.06f, 0.94f);
                _chosen.textWrappingMode = TextWrappingModes.Normal;

                _perk = ElarionUiKit.Label(_plateRoot.transform, "", 0.28f, 0.42f,
                    ElarionUi.ParchmentDim, ElarionUi.FontLabel, TextAlignmentOptions.Center, 0.06f, 0.94f);
                _perk.textWrappingMode = TextWrappingModes.Normal;

                _next = ElarionUiKit.Label(_plateRoot.transform, "", 0.18f, 0.28f,
                    ElarionUi.ParchmentDim, ElarionUi.FontMicro, TextAlignmentOptions.Center, 0.06f, 0.94f);
                _next.textWrappingMode = TextWrappingModes.Normal;

                _continue = ElarionUiKit.Button(_plateRoot.transform,
                    new LocalizedText(VigilCeremonyVM.KeyContinue).Resolve(),
                    ElarionUiKit.ButtonKind.Gold,
                    new Vector2(0.28f, 0.04f), new Vector2(0.72f, 0.16f), OnContinue);
                ElarionUiKit.ClampMinTouch(_continue);
            });
        }

        private IEnumerator WaitThenShowPlate()
        {
            BindSequence();
            float t = 0f;
            bool completed = _sequenceDone;
            while (t < SequenceWaitSeconds && !completed && !_skipped)
            {
                t += Time.unscaledDeltaTime;
                completed = _sequenceDone;
                yield return null;
            }
            UnbindSequence();
            if (_skipped) yield break;
            ShowPlate();
        }

        private bool _sequenceDone;

        private void BindSequence()
        {
            _sequenceDone = false;
            if (_vm == null || _vm.Ledger == null) return;
            _sequenceHandler = () => { _sequenceDone = true; };
            _vm.Ledger.SequenceCompleted += _sequenceHandler;
        }

        private void UnbindSequence()
        {
            if (_vm != null && _vm.Ledger != null && _sequenceHandler != null)
                _vm.Ledger.SequenceCompleted -= _sequenceHandler;
            _sequenceHandler = null;
        }

        private void ShowPlate()
        {
            if (_plateShown || _skipped || _vm == null) return;
            _plateShown = true;
            if (_plateRoot != null) _plateRoot.SetActive(true);
            PaintPlate();
            FlowTrace.Step(Sys, "ceremony plate shown replay=" + _replay);
        }

        private void PaintPlate()
        {
            if (_vm == null) return;
            if (_held != null) _held.text = _vm.HeldText;
            if (_word != null)
            {
                string word = _vm.WordTitleText ?? string.Empty;
                _word.text = word.EndsWith(".", StringComparison.Ordinal) ? word : word + ".";
            }
            if (_line != null) _line.text = _vm.LineText;
            if (_chosen != null) _chosen.text = _vm.ChosenText;
            if (_perk != null) _perk.text = _vm.PerkDescriptionText;
            if (_next != null) _next.text = _vm.NextText;
            if (_continue != null)
            {
                var label = _continue.GetComponentInChildren<TextMeshProUGUI>();
                if (label != null) label.text = new LocalizedText(VigilCeremonyVM.KeyContinue).Resolve();
            }
        }

        private void Update()
        {
            if (!IsOpen) return;
            if (!_skipArmed) _skipArmed = true;
        }

        private void OnSkip()
        {
            if (!_skipArmed || _skipped) return;
            _skipped = true;
            FlowTrace.Step(Sys, "ceremony skip replay=" + _replay);
            if (_vm != null) _vm.Skip();
            else Close();
        }

        private void OnContinue()
        {
            if (_skipped) return;
            if (!_plateShown)
            {
                OnSkip();
                return;
            }
            FlowTrace.Step(Sys, "ceremony continue replay=" + _replay);
            if (_vm != null) _vm.Continue();
            else Close();
        }

        public void Close()
        {
            if (!IsOpen && _vm == null) return;
            FlowTrace.Step(Sys, "CeremonyOfVigil closed.");
            PanelManager.NotifyClosed(_panelHandle);
            Teardown();
        }

        private void Teardown()
        {
            UnbindSequence();
            if (_wait != null)
            {
                StopCoroutine(_wait);
                _wait = null;
            }
            if (_vm != null)
            {
                _vm.Dispose();
                _vm = null;
            }
            if (_modal != null && _modal.canvas != null) Destroy(_modal.canvas);
            _modal = null;
            _body = null;
            _plateRoot = null;
            _held = null;
            _word = null;
            _line = null;
            _chosen = null;
            _perk = null;
            _next = null;
            _continue = null;
            _skipCatcher = null;
            _skipArmed = false;
            _skipped = false;
            _plateShown = false;
        }

        private void EnsureVm()
        {
            if (_vm != null) return;
            _vm = VigilCeremonyVM.CreateDefault(Close);
            _vm.Changed += PaintPlate;
        }

        private static bool RaidBlocks()
        {
            string scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
            return HubScenes.IsRaid(scene) || HubScenes.SuppressTownHud(scene);
        }
    }
}

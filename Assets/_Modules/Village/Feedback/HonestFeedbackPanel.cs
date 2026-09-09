// =============================================================================
// HonestFeedbackPanel (WO-1432) - the one-time "tell us honestly" screen.
// -----------------------------------------------------------------------------
// Assembly: DeNelle.Village   Namespace: DeNelle.Village.Feedback
//
// Code-built uGUI on the Obsidian master frame (the BenefactorsWallPanel recipe).
// Nothing is authored in a .uxml - UI Toolkit does not work in builds
// (CLAUDE.md section 8), so a UXML screen ships blank.
//
// -----------------------------------------------------------------------------
// ITS DOORS (PanelDoorRegression - the allowlist is EMPTY and stays empty)
// -----------------------------------------------------------------------------
//   D1  HonestFeedbackService.TryOpenOffer names this type in real code
//       (FindFirstObjectByType<HonestFeedbackPanel>) and that file is OUTSIDE this
//       panel's home set. That is the strong door.
//   D2  HonestFeedbackPanelBootstrap carries [RuntimeInitializeOnLoadMethod] and
//       installs the host - the HeartPanelBootstrap shape.
// Both are real; neither is a capture harness. A panel whose only constructor is
// AutoPilotDriver / UICaptureLaunch is reported [panel-door-is-harness-only] and
// fails the build, which is exactly the defect WO-1430 found three of.
//
// -----------------------------------------------------------------------------
// ⛔ WHAT THIS SCREEN MAY SAY, AND WHAT IT MAY NEVER SAY (WO-1432 section 3)
// -----------------------------------------------------------------------------
// MAY:  ask for honest words; state the thank-you plainly; offer the store as a
//       SEPARATE, UNREWARDED action and say so in words.
// NEVER: ask how the player FEELS first and branch on the answer. There is one
//       text box, one Send button, and the same reward regardless of what is in
//       the box. Routing happy players to the store and unhappy ones to a
//       suggestion box is review-gating - a Play and Apple violation on its own,
//       and the opposite of the owner's ruling ("its contingent on an honest
//       review", never a positive one).
// NEVER: claim a review happened or was checked. No store tells a client that.
//       Nothing on this screen and nothing in its telemetry says otherwise, and
//       the store button's own caption says we cannot tell.
//
// -----------------------------------------------------------------------------
// THE OVER-CAP SENTENCE IS A RULING, NOT A NICETY
// -----------------------------------------------------------------------------
// This grant is PurchasedOrPromised, so it lands in full and CAN push a resource
// above its storage ceiling. FOUNDATIONAL_RULINGS.md section 7: that is a
// legitimate state, there is no overflow wallet, nothing is taken away - AND
// "The player must be TOLD, in words, when they are above capacity and earning
// nothing into that resource". OverCapLineFor is that sentence. It is shown only
// for resources actually above the line, it never uses the word "lost", it never
// tells the player to go build a bigger silo, and it carries its meaning in WORDS
// - the owner is red/green colourblind, so strip every colour from this file and
// the screen must still read correctly. Verify it that way, not by eye.
//
// Instrumentation: FlowTrace tag "HonestFeedback". Permanent - CLAUDE.md sec.12.
// ASCII only - the tofu oracle fails a non-ASCII player-facing string.
// =============================================================================

using System.Text;
using DeNelle.Core.Diagnostics;
using DeNelle.Core.Economy;
using DeNelle.Core.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DeNelle.Village.Feedback
{
    [DisallowMultipleComponent]
    public sealed class HonestFeedbackPanel : MonoBehaviour
    {
        // ── Player-facing copy. Public consts so the oracles can read the exact
        //    strings rather than a paraphrase of them. ──────────────────────────

        /// <summary>Modal title.</summary>
        public static string Title => HonestFeedbackText.Title.Resolve();

        /// <summary>
        /// The ask. Note what it does NOT do: it does not ask whether the player is
        /// enjoying the game before deciding what to show them.
        /// </summary>
        public static string BodyLine => HonestFeedbackText.Body.Resolve();

        /// <summary>Input placeholder. Neutral on purpose - no leading question.</summary>
        public static string InputPlaceholder => HonestFeedbackText.Placeholder.Resolve();

        /// <summary>Primary action.</summary>
        public static string SendLabel => HonestFeedbackText.Send.Resolve();

        /// <summary>What the primary action pays, stated before it is pressed.</summary>
        public static string RewardLine => HonestFeedbackText.Reward.Resolve();

        /// <summary>The SECONDARY, UNREWARDED action (WO-1432 section 4).</summary>
        public static string StoreLabel => HonestFeedbackText.Store.Resolve();

        /// <summary>
        /// The caption under the store button. It is deliberately blunt about the two things
        /// that are true: nothing is paid for it, and no store tells us whether you did it.
        /// </summary>
        public static string StoreCaption => HonestFeedbackText.StoreCaption.Resolve();

        /// <summary>Shown while the post is in flight.</summary>
        public static string SendingLine => HonestFeedbackText.Sending.Resolve();

        /// <summary>Shown on the happy path, before the per-resource lines.</summary>
        public static string ThanksLine => HonestFeedbackText.Thanks.Resolve();

        /// <summary>Shown when the words landed but the thank-you was already claimed.</summary>
        public static string AlreadyClaimedLine => HonestFeedbackText.AlreadyClaimed.Resolve();

        private const string SysTag = HonestFeedbackGrant.Sys;

        // ── State ────────────────────────────────────────────────────────────────

        private ElarionUiKit.ObsidianModal _modal;
        private TMP_InputField _input;
        private TextMeshProUGUI _status;
        private Button _sendButton;
        private bool _visible;
        private bool _sending;
        private PanelHandle _panelHandle;

        // =====================================================================
        //  Arbiter registration (ModalArbiterRegistrationRegression reads this)
        // =====================================================================

        private void Awake()
        {
            _panelHandle = PanelManager.Register("HonestFeedback", () => SetVisible(false), () => _visible);
            PanelRouter.Register(PanelId.HonestFeedback, Open);
            FlowTrace.Step(SysTag, "HonestFeedbackPanel registered PanelId.HonestFeedback - the offer gate " +
                                   "now has a panel to open.");
        }

        private void OnDestroy()
        {
            PanelRouter.Unregister(PanelId.HonestFeedback, Open);
            if (_modal != null && _modal.canvas != null) Destroy(_modal.canvas);
        }

        /// <summary>The PanelRouter entry point.</summary>
        public void Open() => SetVisible(true);

        private void SetVisible(bool on)
        {
            if (on) EnsureBuilt();
            if (_modal == null || _modal.canvas == null) { _visible = false; return; }
            _visible = on;
            _modal.canvas.SetActive(on);
            if (on)
            {
                if (!PanelManager.NotifyOpened(_panelHandle))
                {
                    // Battle-lock or one-modal refusal. NEVER force-show; the service will
                    // retry, and it has not recorded the offer as shown.
                    _visible = false;
                    _modal.canvas.SetActive(false);
                    FlowTrace.Warn(SysTag, "PanelManager refused the open (battle lock or another modal). " +
                                           "The offer is not consumed.");
                    return;
                }
                FlowTrace.Step(SysTag, "HonestFeedbackPanel visible.");
            }
            else
            {
                PanelManager.NotifyClosed(_panelHandle);
            }
        }

        // =====================================================================
        //  UI construction (kit modal, lazy on first open)
        // =====================================================================

        private void EnsureBuilt()
        {
            if (_modal != null && _modal.canvas != null) return;

            Guard.Try(SysTag, "build the honest-feedback panel", () =>
            {
                _modal = ElarionUiKit.BuildObsidianModal("HonestFeedbackUI", Title,
                    ElarionUiKit.ModalArchetype.Browse, () => SetVisible(false),
                    frameName: RpgUiCatalog.FrameCore, medallionIcon: "quest");

                // Browse's stock body is deliberately shallow. Compose against the full content
                // while leaving every shared chrome zone active: the title and Close artwork
                // depend on that hierarchy even though our own copy does not use its body band.
                var body = _modal.chrome.content.transform;

                // WO-2020: two-column landscape composition. The device screenshot showed every
                // full-width fraction colliding after ClampMinTouch expanded both action bands.
                // Copy and input own the left; the two actions own the right. No control shares a
                // vertical lane with prose, so the touch floor cannot grow a button over text.
                var ask = ElarionUiKit.Label(body, BodyLine, 0.67f, 0.84f,
                    ElarionUi.Parchment, ElarionUi.FontLabel, TextAlignmentOptions.TopLeft, 0.08f, 0.58f);
                ask.textWrappingMode = TextWrappingModes.Normal;

                BuildNoteInput(body, new Vector2(0.08f, 0.49f), new Vector2(0.58f, 0.65f));

                // What it pays, stated BEFORE the button is pressed.
                var reward = ElarionUiKit.Label(body, RewardLine, 0.40f, 0.48f,
                    ElarionUi.Gilt, ElarionUi.FontMicro, TextAlignmentOptions.Left, 0.08f, 0.58f,
                    bold: true);
                reward.textWrappingMode = TextWrappingModes.Normal;

                _sendButton = ElarionUiKit.Button(body, SendLabel, ElarionUiKit.ButtonKind.Gold,
                    new Vector2(0.63f, 0.60f), new Vector2(0.93f, 0.80f), OnSendClicked);
                ElarionUiKit.ClampMinTouch(_sendButton);

                // ── the SECONDARY, UNREWARDED action ──────────────────────────
                // Quiet kind, smaller band, sat below the primary and under a caption
                // that says plainly that nothing is paid for it. Visually secondary is a
                // requirement of WO-1432 section 4, not a layout preference.
                var store = ElarionUiKit.Button(body, StoreLabel, ElarionUiKit.ButtonKind.Quiet,
                    new Vector2(0.63f, 0.33f), new Vector2(0.93f, 0.53f), OnStoreClicked);
                ElarionUiKit.ClampMinTouch(store);

                var storeCaption = ElarionUiKit.Label(body, StoreCaption, 0.14f, 0.30f,
                    ElarionUi.ParchmentDim, ElarionUi.FontMicro, TextAlignmentOptions.Center, 0.63f, 0.93f);
                storeCaption.textWrappingMode = TextWrappingModes.Normal;

                _status = ElarionUiKit.Label(body, "", 0.27f, 0.39f,
                    ElarionUi.Parchment, ElarionUi.FontMicro, TextAlignmentOptions.Left, 0.08f, 0.58f);
                _status.textWrappingMode = TextWrappingModes.Normal;

                RefreshSendInteractable();
                _modal.canvas.SetActive(false);   // built hidden; SetVisible shows it
            });
        }

        /// <summary>
        /// The multiline note field. Copied from BugReportView.BuildNoteInput - there is no kit
        /// helper for an input, and the plate + gilt outline exist because a 0.45-alpha well is
        /// INVISIBLE against the black Obsidian frame (eyes-sweep 2026-07-06 #5). Do not thin it.
        /// </summary>
        private void BuildNoteInput(Transform parent, Vector2 anchorMin, Vector2 anchorMax)
        {
            var host = new GameObject("FeedbackInput", typeof(Image), typeof(TMP_InputField));
            host.transform.SetParent(parent, false);
            var rt = (RectTransform)host.transform;
            rt.anchorMin = anchorMin; rt.anchorMax = anchorMax;
            rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;

            var bg = host.GetComponent<Image>();
            bg.color = new Color(0.10f, 0.09f, 0.07f, 0.92f);
            ElarionUiKit.ApplyRounded(bg);
            var edge = host.AddComponent<Outline>();
            edge.effectColor = new Color(ElarionUi.Gilt.r, ElarionUi.Gilt.g, ElarionUi.Gilt.b, 0.8f);
            edge.effectDistance = new Vector2(1.5f, -1.5f);

            var areaGo = new GameObject("TextArea", typeof(RectTransform), typeof(RectMask2D));
            areaGo.transform.SetParent(host.transform, false);
            var art = (RectTransform)areaGo.transform;
            art.anchorMin = Vector2.zero; art.anchorMax = Vector2.one;
            art.offsetMin = new Vector2(14f, 10f); art.offsetMax = new Vector2(-14f, -10f);

            var text = ElarionUiKit.Label(areaGo.transform, "", 0f, 1f,
                ElarionUi.Parchment, ElarionUi.FontBody, TextAlignmentOptions.TopLeft, 0f, 1f);
            var placeholder = ElarionUiKit.Label(areaGo.transform, InputPlaceholder, 0f, 1f,
                ElarionUi.ParchmentDim, ElarionUi.FontBody, TextAlignmentOptions.TopLeft, 0f, 1f);
            placeholder.fontStyle = FontStyles.Italic;

            _input = host.GetComponent<TMP_InputField>();
            _input.targetGraphic = bg;
            _input.textViewport = art;
            _input.textComponent = text;
            _input.placeholder = placeholder;
            _input.lineType = TMP_InputField.LineType.MultiLineNewline;
            _input.characterLimit = HonestFeedbackTuning.MaxCharacters;
            _input.onValueChanged.AddListener(_ => RefreshSendInteractable());
        }

        private void RefreshSendInteractable()
        {
            if (_sendButton == null) return;
            int len = _input != null ? (_input.text ?? string.Empty).Trim().Length : 0;
            _sendButton.interactable = !_sending && len >= HonestFeedbackTuning.MinCharacters;
        }

        // =====================================================================
        //  Actions
        // =====================================================================

        private void OnSendClicked()
        {
            if (_sending) return;
            var svc = HonestFeedbackService.Instance;
            if (svc == null)
            {
                SetStatus(HonestFeedbackText.ServiceUnavailable.Resolve());
                FlowTrace.Fail(SysTag, "Send pressed but HonestFeedbackService.Instance is null - nothing " +
                                       "was sent and nothing was granted.");
                return;
            }

            _sending = true;
            RefreshSendInteractable();
            SetStatus(SendingLine);
            SubmitAsync(svc).Forget();
        }

        private async Cysharp.Threading.Tasks.UniTaskVoid SubmitAsync(HonestFeedbackService svc)
        {
            string note = _input != null ? _input.text : string.Empty;
            var result = await svc.SubmitAsync(note);
            _sending = false;
            RefreshSendInteractable();
            SetStatus(MessageFor(result));
        }

        /// <summary>
        /// The store link. UNREWARDED: it opens a URL, traces that it did, and touches no flag
        /// and no grant seam. WO-1432 section 3 forbids a second grant path, and this is the
        /// place a well-meaning future edit would try to add one.
        /// </summary>
        private void OnStoreClicked()
        {
            string url = HonestFeedbackTuning.StoreUrl;
            if (string.IsNullOrWhiteSpace(url))
            {
                SetStatus(HonestFeedbackText.StoreUnavailable.Resolve());
                FlowTrace.Warn(SysTag, "store button pressed but HonestFeedbackTuning.StoreUrl is empty - " +
                                       "nothing opened. Author storeLink.url in honest-feedback.json.");
                return;
            }
            FlowTrace.Step(SysTag, "store link opened (UNREWARDED - no flag written, no grant seam touched).");
            DeNelle.Core.Analytics.EventTracker.Track("store_link_opened", new { from = "honest_feedback" });
            Application.OpenURL(url);
        }

        // =====================================================================
        //  Status copy
        // =====================================================================

        private void SetStatus(string line)
        {
            if (_status != null) _status.text = line ?? string.Empty;
            FlowTrace.Step(SysTag, "status: " + (line ?? "(cleared)"));
        }

        /// <summary>The player-facing sentence for each submit outcome. Every branch has one -
        /// a silent screen is the failure mode this exists to prevent.</summary>
        public static string MessageFor(FeedbackSubmitResult result)
        {
            switch (result)
            {
                case FeedbackSubmitResult.StoredAndGranted:
                    return ThanksLine + OverCapSuffix();
                case FeedbackSubmitResult.StoredAlreadyClaimed:
                    return AlreadyClaimedLine;
                case FeedbackSubmitResult.StoredGrantUnavailable:
                    return HonestFeedbackText.GrantUnavailable.Resolve();
                case FeedbackSubmitResult.TooShort:
                    return HonestFeedbackText.TooShort.Resolve(
                        new MinimumCharactersArguments(HonestFeedbackTuning.MinCharacters));
                case FeedbackSubmitResult.NoIdentity:
                    return HonestFeedbackText.NoIdentity.Resolve();
                case FeedbackSubmitResult.ServerRefused:
                    return HonestFeedbackText.ServerRefused.Resolve();
                default:
                    return HonestFeedbackText.NetworkFailed.Resolve();
            }
        }

        /// <summary>
        /// Appends the over-capacity sentence for each resource that is ACTUALLY above its
        /// ceiling after the grant, and nothing at all when none are. Empty is the common case.
        /// </summary>
        private static string OverCapSuffix()
        {
            var sb = new StringBuilder();
            AppendOverCap(sb, BankResource.Wood);
            AppendOverCap(sb, BankResource.Food);
            AppendOverCap(sb, BankResource.Iron);
            return sb.ToString();
        }

        private static void AppendOverCap(StringBuilder sb, BankResource r)
        {
            int over = HonestFeedbackGrant.OverCapUnits(r);
            if (over <= 0) return;
            sb.Append(' ').Append(OverCapLineFor(r, over));
        }

        /// <summary>
        /// ⭐ THE RULING-7 SENTENCE. Public and const-shaped so an oracle can assert what it
        /// says. Read it against FOUNDATIONAL_RULINGS.md section 7 before changing a word:
        ///   - nothing is called LOST, because nothing is;
        ///   - it does not tell the player to build a bigger container, which is the framing
        ///     that reads as a punishment for having been given something;
        ///   - it DOES state, in words, that the earned faucet into that resource has paused
        ///     and how it resumes, which the ruling makes an obligation and not an option;
        ///   - it carries its meaning in words, never in colour (the owner is colourblind).
        /// </summary>
        public static string OverCapLineFor(BankResource r, int over)
        {
            string word = TownBankCapacity.DisplayName(r);
            return HonestFeedbackText.OverCapacity.Resolve(new CapacityOverflowArguments(word, over));
        }
    }
}

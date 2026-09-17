// =============================================================================
// RedeemCodePanel — the DOOR on the promo-code system (promo-redeem door WO).
// -----------------------------------------------------------------------------
// Assembly: DeNelle.Wallet   Namespace: DeNelle.Wallet
//
// WHAT THIS IS
// A code-built Obsidian panel — field + Redeem button + one status line — that
// drives DeNelle.Core.Promo.PromoCodeService. The whole redemption stack (the
// identity-gated api/promo/redeem endpoint, the client, the local dedup set, the
// reward grant) was already built and shipped with NO way for a player to reach
// it. This is the entry, nothing more: it owns no HTTP, no error taxonomy and no
// grant logic. If you find yourself adding a UnityWebRequest here, stop — the
// service is the only thing that talks to the endpoint, and the oracle
// (PromoRedeemEntryRegression) fails the build if this file names one.
//
// ⛔ WHY NOT PromoCodeUI.cs (Assets/_Modules/Core/Promo/PromoCodeUI.cs)
// That panel requires a UIDocument. UXML DOES NOT RENDER IN PLAYER BUILDS
// (CLAUDE.md §8 — this project shipped a blank BattleHUD UIDocument to players).
// It is also never instantiated, so its Instance is permanently null. It stays on
// disk (deleting it is a separate call) with a header pointing here. Do not wire it.
//
// ⛔ NOT GATED ON FeatureFlags.RealmStorePurchase
// That flag gates BUYING (the Buy CTA + the WalletService.Pay entry). Redeeming
// spends no money and touches no wallet rail, and the owner needs it usable while
// purchases stay disabled. This file must never name that flag; the oracle checks.
//
// HOW IT LAYERS
// It is built INSIDE the Realm Store's own modal canvas as a child overlay, not as
// a second top-band modal, on purpose:
//   • a second modal would have to register with PanelManager (the arbiter law —
//     ModalArbiterRegistrationRegression), and NotifyOpened CLOSES the previously
//     open panel. That would tear the store down through its handle — bypassing
//     PackStore.CloseStore — leaving MarketplaceInteractor._storeOpen stuck true
//     and the store un-reopenable. A child overlay dodges that entirely.
//   • as a later sibling on the same canvas it draws ABOVE the store, and it dies
//     with the store's canvas, so there is no orphan lifetime to manage.
// Its own scrim swallows taps meant for the store underneath.
//
// COPY: every sentence comes from canon-strings.json through PromoStrings
// (CLAUDE.md §7). ASCII only — TMP renders non-ASCII as tofu.
// SECURITY: ⛔ the code string is NEVER logged or traced, here or in the service.
// =============================================================================

using System;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using DeNelle.Core.Diagnostics;
using DeNelle.Core.Promo;
using DeNelle.Core.UI;

namespace DeNelle.Wallet
{
    /// <summary>
    /// The Realm Store's "Redeem a Code" overlay. Built lazily on first open, parented to the
    /// store's modal canvas. Owns nothing but presentation + the two service subscriptions.
    /// </summary>
    public sealed class RedeemCodePanel
    {
        private const string Sys = "Promo";
        private const int MaxCodeLength = 32;

        private readonly Transform _host;

        private GameObject _root;
        private TMP_InputField _input;
        private Button _submit;
        private TextMeshProUGUI _status;

        private bool _busy;

        // WO-1512: the promo SERVICE, the code normalisation and the redeem call moved to
        // RedeemCodeVM. This panel binds the VM's two outcome events and paints; it never resolves
        // PromoCodeService and never invokes a redeem. See RedeemCodeVM's header.
        private RedeemCodeVM _vm;

        /// <summary>True while the overlay is on screen.</summary>
        public bool IsOpen => _root != null && _root.activeSelf;

        /// <param name="host">The Realm Store's modal canvas transform — the overlay parents here.</param>
        public RedeemCodePanel(Transform host)
        {
            _host = host;
        }

        // =====================================================================
        //  Open / close
        // =====================================================================

        /// <summary>Builds (first time) and shows the overlay, and subscribes to the service.</summary>
        public void Open()
        {
            if (_host == null)
            {
                FlowTrace.Fail(Sys, "Open: no host transform — the redeem panel cannot be shown (the store's canvas is missing).");
                return;
            }

            EnsureBuilt();
            if (_root == null) return;                 // EnsureBuilt already self-reported

            _root.SetActive(true);
            _root.transform.SetAsLastSibling();        // draw above the store content
            SetEntryVisible(true);
            if (_input != null) { _input.text = string.Empty; _input.ActivateInputField(); }
            SetStatus(string.Empty);
            SetBusy(false);
            Subscribe();
            FlowTrace.Step(Sys, "redeem panel opened (Realm Store entry).");
        }

        /// <summary>Hides the overlay and drops the subscriptions. Safe to call when already closed.</summary>
        public void Close()
        {
            Unsubscribe();
            if (_root != null) _root.SetActive(false);
        }

        private void Subscribe()
        {
            if (_vm == null)
            {
                _vm = RedeemCodeVM.CreateDefault();
                _vm.Redeemed += HandleRedeemed;
                _vm.Failed   += HandleFailed;
            }
            _vm.Attach();
        }

        private void Unsubscribe()
        {
            if (_vm != null) _vm.Detach();
        }

        // =====================================================================
        //  Build (code-built Obsidian; NO UIDocument — see the header)
        // =====================================================================

        private void EnsureBuilt()
        {
            if (_root != null) return;
            using var _ = FlowTrace.Enter(Sys, "build redeem overlay");

            var rootGo = new GameObject("RedeemCodeOverlay", typeof(RectTransform));
            rootGo.transform.SetParent(_host, false);
            var rrt = (RectTransform)rootGo.transform;
            rrt.anchorMin = Vector2.zero; rrt.anchorMax = Vector2.one;
            rrt.offsetMin = Vector2.zero; rrt.offsetMax = Vector2.zero;

            // Swallows taps aimed at the store underneath; tapping it closes, like every kit modal.
            ElarionUiKit.Scrim(rootGo.transform, Close);

            var chrome = ElarionUiKit.BuildObsidianPanel(rootGo.transform,
                PromoStrings.Get(PromoStrings.KeyTitle),
                new Vector2(0.16f, 0.18f), new Vector2(0.84f, 0.82f),
                onClose: Close, frameName: RpgUiCatalog.FrameCore);

            if (chrome == null || chrome.content == null)
            {
                FlowTrace.Fail(Sys, "BuildObsidianPanel returned no usable chrome — redeem overlay NOT shown (player would face a blank scrim).");
                UnityEngine.Object.Destroy(rootGo);
                return;
            }

            var body = chrome.layout != null && chrome.layout.body != null
                ? (Transform)chrome.layout.body
                : chrome.content.transform;

            // ⚠ LAYOUT FIXED 2026-08-17 FROM A DEVICE SCREENSHOT, and the screenshot IS the evidence
            // — the numbers below looked correct on paper and were wrong on glass.
            //
            // The first build put four elements in non-overlapping bands (0.78-0.95 blurb,
            // 0.53-0.75 field, 0.44-0.52 hint, 0.25-0.43 status) and they still overprinted into an
            // unreadable pile on a 2670x1200 Seeker. The bands never overlapped; THE TEXT
            // OVERFLOWED THEM. A TMP label does not clip to its RectTransform by default, so a
            // two-line blurb spills DOWN into the field and a long status sentence spills UP into
            // the hint. Owner's words: "the screen is hard to read".
            //
            // This is the WO-1040 defect class exactly (three text blocks colliding on TREASURE
            // FOUND): fixed fractional anchors carrying content that grows. The lesson there was
            // that a band must SOLVE for its content; here the cheapest correct form of that is
            // auto-sizing — the text shrinks to fit rather than escaping.
            //
            // ⛔ DO NOT "fix" a recurrence by nudging these fractions. That treats the symptom and
            // it re-breaks the moment a string is translated, lengthened, or a device is narrower.
            // The invariant is: EVERY GROWABLE LABEL ON THIS PANEL AUTO-SIZES INTO ITS BAND.

            // What this screen is for. Auto-sized: it wraps to two lines on a narrow device.
            var blurb = ElarionUiKit.Label(body, PromoStrings.Get(PromoStrings.KeyBlurb),
                0.76f, 0.95f, ElarionUi.ParchmentDim, ElarionUi.FontBody,
                TextAlignmentOptions.Center, 0.06f, 0.94f);
            if (blurb != null)
            {
                blurb.textWrappingMode = TextWrappingModes.Normal;
                blurb.enableAutoSizing = true;
                blurb.fontSizeMin = 14f;
                blurb.fontSizeMax = blurb.fontSize;
            }

            // The field. Mirrors ClanChatPanel/LoginPanelController's inline TMP_InputField over a
            // rounded well — the kit has no input-field builder, and UXML is not an option (§8).
            _input = MakeInputField(body, PromoStrings.Get(PromoStrings.KeyPlaceholder),
                new Vector2(0.08f, 0.52f), new Vector2(0.92f, 0.72f));

            // ⚠ THE SEPARATE "codes are not case sensitive" HINT LABEL IS RETIRED. It occupied an
            // 8%-tall band (0.44-0.52) wedged between the field and the status — the tightest strip
            // on the panel — to say something the player does not need until they are already
            // typing. Its content belongs in the PLACEHOLDER, where it is read at exactly the
            // moment it matters and costs no vertical space. Removing it hands its band to the
            // status line, which is the label that actually needed room: those sentences are
            // deliberately long because every one of them has to say whether the code was spent.

            // The ONE feedback surface. Every outcome — success and each distinct failure — lands
            // here as a full sentence. Auto-sized because those sentences vary from "Enter a code
            // first." to a two-line refusal, and a fixed size that fits one clips the other.
            _status = ElarionUiKit.Label(body, string.Empty,
                0.24f, 0.48f, ElarionUi.Gold, ElarionUi.FontBody,
                TextAlignmentOptions.Center, 0.05f, 0.95f);
            if (_status != null)
            {
                _status.textWrappingMode = TextWrappingModes.Normal;
                _status.enableAutoSizing = true;
                _status.fontSizeMin = 14f;
                _status.fontSizeMax = _status.fontSize;
            }

            _submit = ElarionUiKit.BuildObsidianButton(body,
                PromoStrings.Get(PromoStrings.KeyAction),
                ElarionUiKit.ObsidianButtonStyle.Style1,
                ElarionUiKit.ObsidianButtonColor.Yellow,
                new Vector2(0.26f, 0.02f), new Vector2(0.74f, 0.23f),
                () => SubmitAsync().Forget());

            _root = rootGo;
            _root.SetActive(false);

            if (_input == null || _submit == null || _status == null)
                FlowTrace.Fail(Sys, $"redeem overlay built INCOMPLETE (input:{_input != null} button:{_submit != null} status:{_status != null}) — " +
                                    "the player could face a screen that cannot accept or answer an entry.");
            else
                FlowTrace.Step(Sys, "redeem overlay built: field + Redeem button + status line.");
        }

        // =====================================================================
        //  Submit
        // =====================================================================

        // WO-1512: a THIN ROUTE. The VM normalises the entry, resolves the service, makes the call
        // and reports the outcome on its events; this method chooses the words and the busy state.
        private async UniTask SubmitAsync()
        {
            if (_busy || _vm == null) return;

            SetBusy(true);
            SetStatus(PromoStrings.Get(PromoStrings.KeyBusy));

            // The VM hands back the code EXACTLY as submitted so the field shows what was sent.
            var refusal = await _vm.RedeemAsync(
                _input != null ? _input.text : null,
                code => { if (_input != null) _input.text = code; });

            SetBusy(false);

            switch (refusal)
            {
                case RedeemCodeVM.RedeemRefusal.Empty:
                    SetStatus(PromoStrings.Get(PromoStrings.KeyErrEmpty));
                    break;
                case RedeemCodeVM.RedeemRefusal.ServiceUnavailable:
                    SetStatus(PromoStrings.Get(PromoStrings.KeyErrUnknown));
                    break;
                // None / AlreadyBusy: the outcome arrives on HandleRedeemed / HandleFailed, which
                // own the status line from here. Writing one now would overwrite the real answer.
            }
        }

        private void HandleRedeemed(PromoReward reward)
        {
            // Redemption is now a confirmation state, not another invitation to submit the
            // same one-per-wallet code. The welcome letter may temporarily cover this panel;
            // when the player closes it they should return to the receipt + Close, never to a
            // live Redeem button. Open() restores the controls for a later visit/other code.
            SetEntryVisible(false);

            int crystals = reward != null ? Mathf.Max(0, reward.Crystals) : 0;
            int coins    = reward != null ? Mathf.Max(0, reward.Coins)    : 0;
            string packSku = reward != null ? reward.PackSku : null;

            if (crystals <= 0 && coins <= 0 && string.IsNullOrEmpty(packSku))
            {
                // Say so plainly rather than claiming a reward the player will not find in their bank.
                SetStatus(PromoStrings.Get(PromoStrings.KeySuccessNoReward));
                return;
            }

            string summary = crystals > 0 ? PromoStrings.Format(PromoStrings.KeyRewardCrystals, crystals) : string.Empty;
            if (coins > 0)
            {
                string coinPart = PromoStrings.Format(PromoStrings.KeyRewardCoins, coins);
                summary = summary.Length > 0 ? summary + " + " + coinPart : coinPart;
            }
            if (!string.IsNullOrEmpty(packSku))
            {
                var pack = PackCatalog.Find(packSku);
                string packName = pack != null && !string.IsNullOrEmpty(pack.Name) ? pack.Name : packSku;
                string packPart = PromoStrings.Format(PromoStrings.KeyRewardPack, packName);
                summary = summary.Length > 0 ? summary + " + " + packPart : packPart;
            }

            SetStatus(PromoStrings.Format(PromoStrings.KeySuccess, summary));
            if (_input != null) _input.text = string.Empty;
        }

        private void HandleFailed(string playerSentence)
        {
            // The service already resolved the canon sentence for the specific cause; a blank one
            // would be a silent failure on the one screen where that reads as a scam.
            SetStatus(string.IsNullOrWhiteSpace(playerSentence)
                ? PromoStrings.Get(PromoStrings.KeyErrUnknown)
                : playerSentence);
        }

        private void SetStatus(string message)
        {
            if (_status != null) _status.text = message ?? string.Empty;
            else if (!string.IsNullOrEmpty(message))
                FlowTrace.Warn(Sys, "redeem status has no on-screen surface — the player is told nothing.");
        }

        private void SetBusy(bool busy)
        {
            _busy = busy;
            if (_submit != null) _submit.interactable = !busy;
            if (_input != null) _input.interactable = !busy;
        }

        private void SetEntryVisible(bool visible)
        {
            if (_input != null) _input.gameObject.SetActive(visible);
            if (_submit != null) _submit.gameObject.SetActive(visible);
        }

        // =====================================================================
        //  Input field (hand-built — the kit has none)
        // =====================================================================

        private static TMP_InputField MakeInputField(Transform parent, string placeholder, Vector2 min, Vector2 max)
        {
            var host = new GameObject("CodeInput", typeof(Image), typeof(TMP_InputField));
            host.transform.SetParent(parent, false);
            var rt = (RectTransform)host.transform;
            rt.anchorMin = min; rt.anchorMax = max;
            rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;

            var bg = host.GetComponent<Image>();
            bg.color = new Color(0f, 0f, 0f, 0.45f);
            ElarionUiKit.ApplyRounded(bg);

            var areaGo = new GameObject("TextArea", typeof(RectTransform), typeof(RectMask2D));
            areaGo.transform.SetParent(host.transform, false);
            var art = (RectTransform)areaGo.transform;
            art.anchorMin = Vector2.zero; art.anchorMax = Vector2.one;
            art.offsetMin = new Vector2(14f, 4f); art.offsetMax = new Vector2(-14f, -4f);

            var text = ElarionUiKit.Label(areaGo.transform, string.Empty, 0f, 1f,
                ElarionUi.Parchment, ElarionUi.FontBody, TextAlignmentOptions.Center, 0f, 1f);
            var ph = ElarionUiKit.Label(areaGo.transform, placeholder, 0f, 1f,
                ElarionUi.ParchmentDim, ElarionUi.FontBody, TextAlignmentOptions.Center, 0f, 1f);
            ph.fontStyle = FontStyles.Italic;

            var field = host.GetComponent<TMP_InputField>();
            field.targetGraphic  = bg;
            field.textViewport   = art;
            field.textComponent  = text;
            field.placeholder    = ph;
            field.lineType       = TMP_InputField.LineType.SingleLine;
            field.characterLimit = MaxCodeLength;
            field.text           = string.Empty;
            return field;
        }
    }
}

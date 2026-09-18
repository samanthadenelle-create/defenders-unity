// =============================================================================
// LoginPanelController (WO-769, narrowed to wallet-only by WO-837-B) — the
// "connect wallet or play as guest" surface. Presented at boot before the hub,
// modeled on FoundingChoiceController.
//
// ⛔ ONE SURFACE, EVERY PLATFORM (owner ruling 2026-08-21). The player sees a gold
// Connect Wallet primary + Play as Guest. There is NO email form, NO Create Account,
// NO Google button and NO forgot-password, on ANY platform. WO-847 had scoped
// wallet-first to Android/Seeker and kept the WO-787/845 email layout on
// desktop/WebGL for a Google Play release; the owner closed that:
//   "That's only true with the Play Store, which we are not in. We are only in the
//    dApp Store, which is all wallet authentication based."
// The LoginSurfacePlatform / LoginSurfaceLayout seam that carried the split is
// DELETED (not left resolving to a constant) — a one-armed layout switch is how the
// other arm grows back.
//
// Wallet connect routes through LoginWalletBridge -> WalletSkinBootstrap ->
// WalletService.Connect with an EXPLICIT GameStateService.BindWallet (never
// skin-config-gated on this path).
// -----------------------------------------------------------------------------
// Assembly: DeNelle.Onboarding   Namespace: DeNelle.Onboarding (references DeNelle.Core only).
// UI: code-built uGUI on the Obsidian kit (ElarionUiKit) — its own overlay canvas,
// NO UXML/UIDocument (CLAUDE.md §8). Colour never carries meaning (each control labelled).
// PRESENTATION ONLY: identity logic lives in LoginViewModel; this file builds UI.
// Guest = the existing device-hash fallback (GameStateService.EnsureAccount mints
// guest-local-* on load), so guest just continues.
//
// SOFTLOCK LAW (security audit 2026-08-02, BINDING): this is the FIRST screen an Android
// tester sees and there is no way past it except through this file. Two invariants keep
// it from becoming a kill-the-app dead end:
//   1. "Play as Guest" is NEVER disabled. SetBusy locks every OTHER control; the escape
//      hatch stays live for the whole busy window. Previously SetBusy(true) disabled it
//      too, so an unanswered wallet handshake (no wallet app installed, or the player
//      backgrounded the wallet and came back) left the screen on "Opening your wallet..."
//      with EVERY button dead.
//   2. Every await on this surface is TIME-BOUNDED. The wallet connect await gets a
//      35s ceiling here on top of WalletService's own 30s provider ceiling, so the UI
//      un-busies and tells the truth even if the wallet layer below is rewritten.
// =============================================================================

using System;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using DeNelle.Core.Auth;
using DeNelle.Core.Diagnostics;
using DeNelle.Core.Platform;
using DeNelle.Core.State;
using DeNelle.Core.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DeNelle.Onboarding
{
    /// <summary>
    /// Obsidian Connect-Wallet + Play-as-Guest surface. Call <see cref="Present"/> with
    /// the "enter the game" continuation; the panel invokes it once the player connects a
    /// wallet or chooses guest.
    /// </summary>
    public sealed class LoginPanelController : MonoBehaviour
    {
        private readonly LoginViewModel _vm = LoginViewModel.CreateDefault();

        private Action _onContinue;
        private bool _routed;
        private GameObject _canvas;
        private PanelHandle _panelHandle;

        private TextMeshProUGUI _status;
        private Button _guest;
        private Button _connectWallet;
        private bool _busy;

#if UNITY_EDITOR
        /// <summary>
        /// Capture-only presentation seam. It lets editor evidence build the exact
        /// production layout with GOOGLE_PLAY copy without changing player defines.
        /// Player builds do not contain this property.
        /// </summary>
        public static bool? EditorGooglePlayPresentationOverride { get; set; }
#endif

        private static bool IsGooglePlayPresentation
        {
            get
            {
#if GOOGLE_PLAY
                return true;
#elif UNITY_EDITOR
                return EditorGooglePlayPresentationOverride ?? false;
#else
                return false;
#endif
            }
        }

        /// <summary>
        /// The connect CTA label, resolved at COMPILE time per channel (WO-1363). The
        /// wallet-facing caption is an audit token: a runtime ternary on
        /// <see cref="IsGooglePlayPresentation"/> would still compile the English literal into the
        /// Play artifact's global-metadata.dat, where a policy reviewer's `strings` pass finds it.
        /// The <c>#if GOOGLE_PLAY</c> split therefore stays, and WO-1857 only moves the two captions
        /// out of the source and into the string table — which PRESERVES the audit property rather
        /// than weakening it: `onboarding.login_panel.connect_cta_wallet` carries a channel-neutral
        /// Google Play `replacementRow` (same precedent as
        /// `onboarding.login_panel.title_wallet`), so the value the Play artifact ships is the
        /// replacement, never the wallet wording. The editor presentation override is preserved on
        /// non-Play builds, where BOTH labels legitimately exist.
        /// </summary>
        private static string ConnectCtaLabel
        {
            get
            {
#if GOOGLE_PLAY
                return new LocalizedText("onboarding.login_panel.connect_cta_google").Resolve();
#else
                return new LocalizedText(IsGooglePlayPresentation
                    ? "onboarding.login_panel.connect_cta_google"
                    : "onboarding.login_panel.connect_cta_wallet").Resolve();
#endif
            }
        }

        /// <summary>
        /// Show the login surface, then run <paramref name="onContinue"/> once the player
        /// gets in (wallet connect / guest). Always presents; the caller decides when to
        /// call it in the boot flow.
        /// </summary>
        public static void Present(Action onContinue)
        {
            using var _ = FlowTrace.Enter("Auth", "LoginPanelController.Present");
            var host = new GameObject("LoginUI");
            var ctrl = host.AddComponent<LoginPanelController>();
            ctrl._onContinue = onContinue;
            ctrl.Build();
            FlowTrace.Step("Auth",
                "login panel presented (first-run connect or guest; production path, same on every build).");
        }

        /// <summary>
        /// THE GATE DECISION, pure and testable (2026-08-18 defect: the owner's wallet
        /// auto-resumed at boot and the SIGN IN panel was presented anyway ~5s later —
        /// device capture 20:21:38 "auto-resume SUCCEEDED", 20:21:43 LoginPanelController.Build).
        /// <para>
        /// ROOT CAUSE (fixed here): this gate read ONE source — <c>FirebaseAuthService.IsSignedIn</c>
        /// — while the identity that actually keys this game is the WALLET. A player who has only
        /// ever connected a wallet was not Firebase-signed-in, so the gate presented the login
        /// surface on TOP of an already-connected, already-bound session.
        /// </para>
        /// <para>
        /// <paramref name="legacySignedIn"/> is RETAINED-BUT-DEAD (WO-837-B): email/Firebase login is
        /// removed, so <see cref="PresentOrContinue"/> now always passes <c>false</c>. The parameter
        /// stays because it is the pure seam's contract — LoginGateRegression drives a truth table
        /// against <c>(bool,bool,bool)</c> by reflection, and a wallet-only build must still prove
        /// "any already-in signal => CONTINUE". Do not repurpose it as a second identity source.
        /// </para>
        /// <para>
        /// <paramref name="walletIdentityBound"/> is the RACE-PROOF half: it comes from the persisted
        /// save + this device's attestation, so it is true SYNCHRONOUSLY at boot — before any silent
        /// reconnect finishes. That is why this fix needs no delay, no timeout and no extra await.
        /// <paramref name="walletConnected"/> is the live published state for the in-session case.
        /// </para>
        /// <para>FIRST RUN IS PRESERVED: a genuine first run has no connected wallet and no attested
        /// bound identity (its save key is the <c>guest-local-</c> device hash, which is not
        /// cloud-identity-shaped) — every input is false, so this returns false and the panel
        /// presents. That is its one legitimate purpose.</para>
        /// </summary>
        public static bool ShouldContinueWithoutLogin(bool walletConnected, bool walletIdentityBound,
                                                      bool legacySignedIn)
            => ShouldContinueWithoutLogin(walletConnected, walletIdentityBound, legacySignedIn, false);

        /// <summary>
        /// THE GATE DECISION with the Pi identity input (WO-1322). Identical to the
        /// three-input overload except for <paramref name="piIdentitySignedIn"/>.
        /// <para>
        /// WO-1322 DEFECT (owner-captured in REAL Pi Browser, 2026-09-02): the skin resolved to
        /// 'pi' (auth=PiSdk, identity=PiUid), <c>[Flow:Pi] Signed in as ...</c> succeeded, and the
        /// CHOOSE YOUR WALLET surface was presented anyway. <see cref="PresentOrContinue"/> sampled
        /// only the two WALLET inputs and hardcoded the third, so the gate was SKIN-BLIND.
        /// </para>
        /// <para>
        /// <paramref name="piIdentitySignedIn"/> is a SEPARATE, NAMED input on purpose — it is NOT
        /// folded into <paramref name="legacySignedIn"/>. Under <c>SkinAuthMode.PiSdk</c> THE WALLET
        /// IS NOT THE IDENTITY: the server-verified Pi uid is (it is established only after the
        /// access token is verified against api.minepi.com). Hiding a live, correct
        /// identity source behind a parameter documented as "permanently dead" is how the next
        /// reader deletes it. <paramref name="legacySignedIn"/> stays dead; this one is alive.
        /// </para>
        /// <para>
        /// ⛔ THE SKR / SOLANA SKIN IS UNCHANGED. Off the Pi skin the caller passes false here
        /// (see PresentOrContinue: the input is AND-ed with <c>AuthMode == SkinAuthMode.PiSdk</c>),
        /// so the SKR truth table is byte-for-byte what WO-1249 pinned — a first run with every
        /// input false still PRESENTS, on a tester APK exactly as on the store build.
        /// </para>
        /// </summary>
        public static bool ShouldContinueWithoutLogin(bool walletConnected, bool walletIdentityBound,
                                                      bool legacySignedIn, bool piIdentitySignedIn)
            => walletConnected || walletIdentityBound || legacySignedIn || piIdentitySignedIn;

        /// <summary>
        /// Boot entry: if the player is ALREADY IN — a connected wallet or an attested
        /// wallet-bound save — continue straight through; otherwise present the
        /// connect-or-guest surface.
        /// <para>CORRECTED 2026-08-18: this doc used to say "already signed in (Firebase caches the
        /// session)" and the code matched it — Firebase-only. On a wallet-first build that is the
        /// wrong source, and it re-prompted a player whose wallet had just auto-resumed. The
        /// decision now lives in <see cref="ShouldContinueWithoutLogin"/>.</para>
        /// <para>WO-837-B: the boot-time Firebase init probe is GONE with the email login. It was a
        /// blocking, up-to-12s network await that ran BEFORE any UI existed — the worst softlock
        /// site on the whole surface — in service of an identity source that binds nothing. Removing
        /// it makes this method synchronous, so there is no longer any await between app start and
        /// the first screen. Do not reintroduce a network call here.</para>
        /// <para>WO-1322 (owner felt-test in REAL Pi Browser, 2026-09-02): the gate was SKIN-BLIND.
        /// It sampled the two WALLET inputs only, so a player who had just signed in with Pi
        /// (skin 'pi', auth=PiSdk, identity=PiUid) was shown CHOOSE YOUR WALLET anyway. A FOURTH
        /// input is now sampled - <c>PiSignInController.IsSignedIn</c> AND-ed with
        /// <c>CurrencySkinResolver.Active.AuthMode == SkinAuthMode.PiSdk</c> - both static and
        /// synchronous, so this method stays await-free. The SKR/Solana skin is untouched: off the
        /// Pi skin the new input is false and the WO-1249 truth table is bit-identical.</para>
        /// </summary>
        public static void PresentOrContinue(Action onContinue)
        {
            // The WALLET is the data identity, so it is sampled here and not assumed:
            //   * walletConnected      - live published state (CurrencySkinResolver.PublishWalletConnected,
            //                            raised by the Wallet assembly on connect AND on silent auto-resume);
            //   * walletIdentityBound  - the persisted, provider-ATTESTED save key. Available with no
            //                            await, which is what makes the boot-time resume race a non-event.
            bool walletConnected = false, walletIdentityBound = false;
            Guard.Try("Auth", "sample wallet state for the login gate", () =>
            {
                walletConnected = CurrencySkinResolver.IsWalletConnected;
                var svc = GameStateService.Instance;
                walletIdentityBound = svc != null && svc.HasAttestedWalletIdentity;
            });

            // WO-1322: the FOURTH input, and the reason the gate is no longer skin-blind.
            //   * piIdentitySignedIn - under SkinAuthMode.PiSdk the WALLET IS NOT THE IDENTITY;
            //     the server-verified Pi uid is. Only the BOOLEAN is read here - never the uid or
            //     the username, which must never reach Player.log (same rule as the wallet address,
            //     WO-1249). The owner signed
            //     in successfully in real Pi Browser on 2026-09-02 and was shown CHOOSE YOUR WALLET
            //     anyway, because this file sampled only the two wallet inputs.
            //
            // BOTH halves are required, and the AuthMode half is what keeps the SKR skin exactly as
            // it was: off the Pi skin this is false, so ShouldContinueWithoutLogin sees the same
            // three inputs WO-1249 pinned. PiSignInController.IsSignedIn is a STATIC BOOL - free and
            // synchronous. There is deliberately NO await and NO network call here (WO-837-B removed
            // a blocking 12s Firebase probe from this exact spot: "the worst softlock site on the
            // whole surface"). Do not reintroduce one to "confirm" the session.
            bool piSkin = false, piSignedIn = false;
            string authMode = "unknown";
            Guard.Try("Auth", "sample Pi identity state for the login gate", () =>
            {
                var skin = CurrencySkinResolver.Active;
                piSkin = skin?.AuthMode == SkinAuthMode.PiSdk;
                authMode = skin != null ? skin.AuthMode.ToString() : "unknown";
                piSignedIn = PiSignInController.IsSignedIn;
            });
            bool piIdentitySignedIn = piSkin && piSignedIn;

            // Third input is permanently false in a wallet-only build (WO-837-B) — see the
            // legacySignedIn note on ShouldContinueWithoutLogin. The Pi input is NOT passed
            // through it: it is a live identity source and gets its own named parameter.
            //
            // WO-1249 (owner 2026-08-27): this is the PRODUCTION gate. A first run with
            // every input false PRESENTS -- that is the one-time connect, not a bug, and
            // it is the same on a tester APK as on the store build. Do not branch this
            // decision on a tester define: a build that skips the connect cannot validate
            // production. Extra native wallet sheets AFTER CONTINUE are session minting
            // (WO-1157), not this panel.
            bool continueIn = ShouldContinueWithoutLogin(walletConnected, walletIdentityBound,
                                                         false, piIdentitySignedIn);

            // §1.4b: the decision AND every input it was made from, so the next reader never has to
            // guess WHY the panel appeared. A trace that cannot report the wrong outcome is decoration.
            // WO-1249: never log a wallet address; the booleans are enough. WO-1322: never log the Pi
            // uid or username either - the skin's auth mode plus the boolean say everything needed.
            FlowTrace.Step("Auth",
                "login gate decision=" + (continueIn ? "CONTINUE" : "PRESENT") +
                " (walletConnected=" + walletConnected +
                ", walletIdentityBound=" + walletIdentityBound +
                ", piIdentitySignedIn=" + piIdentitySignedIn +
                " [authMode=" + authMode + ", piSkin=" + piSkin + ", piSignedIn=" + piSignedIn + "]" +
                ", legacySignedIn=false [wallet-only build, WO-837-B]).");

            if (continueIn)
            {
                onContinue?.Invoke();
                return;
            }
            Present(onContinue);
        }

        // =====================================================================
        //  Overlay construction (code-built uGUI on the Obsidian kit)
        // =====================================================================
        private void Build()
        {
            using var _ = FlowTrace.Enter("Auth", "LoginPanelController.Build (uGUI Obsidian)");

            _canvas = ElarionUiKit.BuildModalCanvas("LoginCanvas", 31000);
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(_canvas, gameObject.scene);

            var scrim = ElarionUiKit.AddImage(_canvas.transform, "Scrim",
                Vector2.zero, Vector2.one, new Color(0f, 0f, 0f, 0.72f), rounded: false);
            var scrimImg = scrim.GetComponent<Image>();
            if (scrimImg != null) scrimImg.raycastTarget = true;

            // Forced surface (no dismiss X -- guest is the escape). withBackdrop:false; scrim dims.
            // WO-787: taller rect (y 0.06-0.94, was 0.14-0.86) -- the stack is 7-8 rows of
            // MinTouchPx-floored controls; on the shortest live canvas (post-scale height ~970,
            // landscape web / Seeker) the old rect cannot hold them without the touch floor
            // forcing overlap ("stacked", owner screenshot 2026-07-30).
            var chrome = ElarionUiKit.BuildObsidianPanel(_canvas.transform,
                // ⚠ WO-1857 REGRESSION NEEDLE: LoginGateRegression.cs:293 asserts this file's
                // SOURCE TEXT still carries the old English panel title verbatim. Re-point
                // that needle at onboarding.login_panel.title_wallet. The old caption is
                // deliberately NOT quoted in this comment — a pin that matches a comment
                // passes vacuously, which is worse than a red pin. Reported in the sidecar;
                // this lane may not edit Assets/Editor/Regression.
                new LocalizedText(IsGooglePlayPresentation
                    ? "onboarding.login_panel.title_welcome"
                    : "onboarding.login_panel.title_wallet").Resolve(),
                new Vector2(0.22f, 0.12f), new Vector2(0.78f, 0.88f), onClose: null,
                withBackdrop: false, frameName: RpgUiCatalog.FrameCore);
            MedievalUiSkin.ApplyShell(chrome, compact: true);
            if (chrome.close != null) chrome.close.gameObject.SetActive(false);
            if (chrome.layout != null && chrome.layout.medallion != null)
                chrome.layout.medallion.gameObject.SetActive(false);

            // WO-787 Part A: lay out on the FULL-rect chrome.content, NOT chrome.layout.body.
            // BuildObsidianPanel (WO-714 P6) raises Zone_Body's floor by the close-band +
            // footer reservation (body.y up to ~0.45) to clear the shared Close -- but this
            // panel HIDES its Close, so the reservation only compressed the stack until every
            // fraction slot fell below the MinTouchPx floor and the rows overlapped.
            // Fractions below are clamp-aware: adjacent button centers sit >= 112 reference px
            // apart on the shortest live canvas, so ClampMinTouch can grow rows collision-free.
            Transform body = chrome.content.transform;

            // WO-837-B (owner ruling 2026-08-21): ONE surface on every platform -
            // "connect wallet or play as guest". No email form, no forgot-password
            // (the wallet is its own recovery), no platform branch.
            BuildWalletFirst(body);

            if (_panelHandle == null)
                _panelHandle = PanelManager.Register("Login", Continue, () => !_routed && _canvas != null);
            PanelManager.NotifyOpened(_panelHandle);
        }

        // =====================================================================
        //  The login surface (every platform): Connect Wallet + Play as Guest
        // =====================================================================
        private void BuildWalletFirst(Transform body)
        {
            // ONE gold button on the surface (kit law): Connect Wallet is THE primary
            // CTA. Two rows only - button centers sit 0.25 apart, far above the ~0.131
            // MinTouch clamp floor from the WO-787 geometry analysis, so ClampMinTouch
            // can grow both rows collision-free on every live canvas.
            // ⚠ WO-1857 REGRESSION NEEDLE: LoginGateRegression.cs:295 asserts this file's
            // SOURCE TEXT still carries the "this is the one-time connect" phrase; it now
            // lives only in onboarding.login_panel.intro_wallet's authored value. The phrase
            // is deliberately not reproduced here (see the note on the panel title above).
            var intro = ElarionUiKit.Label(body,
                new LocalizedText(IsGooglePlayPresentation
                    ? "onboarding.login_panel.intro_google"
                    : "onboarding.login_panel.intro_wallet").Resolve(),
                0.68f, 0.80f, ElarionUi.Parchment, ElarionUi.FontLabel,
                TextAlignmentOptions.Center, 0.06f, 0.94f);
            intro.textWrappingMode = TextWrappingModes.Normal;
            intro.raycastTarget = false;
            ElarionUiKit.FitBlock(intro);

            _status = ElarionUiKit.Label(body, "", 0.57f, 0.64f,
                ElarionUi.Parchment, ElarionUi.FontMicro,
                TextAlignmentOptions.Center, 0.06f, 0.94f);
            _status.raycastTarget = false;

            _connectWallet = ElarionUiKit.BuildObsidianButton(body,
                ConnectCtaLabel,
                ElarionUiKit.ObsidianButtonStyle.Style1, ElarionUiKit.ObsidianButtonColor.Yellow,
                new Vector2(0.08f, 0.38f), new Vector2(0.92f, 0.54f), OnConnectWallet);
            MedievalUiSkin.ApplyButton(_connectWallet, primary: true);
            ApplyFrontDoorButtonFrame(_connectWallet);

            _guest = ElarionUiKit.BuildObsidianButton(body,
                new LocalizedText("onboarding.login_panel.play_as_guest").Resolve(),
                ElarionUiKit.ObsidianButtonStyle.Style1, ElarionUiKit.ObsidianButtonColor.Gray,
                new Vector2(0.08f, 0.16f), new Vector2(0.92f, 0.34f), OnPlayAsGuest);
            MedievalUiSkin.ApplyButton(_guest, primary: false);
            ApplyFrontDoorButtonFrame(_guest);
        }

        private static void ApplyFrontDoorButtonFrame(Button button)
        {
            if (button == null || !(button.targetGraphic is Image image)) return;
            var frame = Resources.Load<Sprite>("UI/ElarionMedieval/frames/content-panel");
            if (frame != null) image.sprite = frame;
            image.type = Image.Type.Simple;
            image.color = Color.white;
            var label = button.GetComponentInChildren<TextMeshProUGUI>(true);
            if (label != null) ElarionUiKit.FitSingleLine(label, 24f, 36f);
        }

        // =====================================================================
        //  Actions — presentation only; identity logic is in LoginViewModel
        // =====================================================================
        /// <summary>
        /// UI failsafe ceiling on the whole wallet-connect await (seconds). Sits ABOVE
        /// WalletService's own 30s provider ceiling so the honest, specific message
        /// normally comes from the wallet layer; this only fires if something below
        /// stops honouring its own timeout. Counted in UNSCALED time, on the player
        /// loop - so a backgrounded app (the player is IN the wallet app) does not burn
        /// the budget, and the count resumes when they come back.
        /// </summary>
        private const float ConnectUiTimeoutSeconds = 35f;

        // The primary — and, since WO-837-B, the ONLY identity control on the surface.
        // Honest statuses on every branch; a success resolves an AuthOutcome whose
        // UserId is the wallet address, and HandleOutcome -> Continue takes it from there.
        //
        // SOFTLOCK LAW: this await is bounded. Guest stays interactable throughout
        // (SetBusy never touches it), so even a hung handshake leaves a way into the game.
        private async void OnConnectWallet()
        {
            if (_busy || _routed) return;
            SetBusy(true);
            // WO-1857: the #if pair is PRESERVED (WO-1363 audit posture — a runtime ternary
            // would leave the non-Play literal in the Play artifact's global-metadata.dat),
            // so each arm keeps its own key rather than collapsing into one.
#if GOOGLE_PLAY
            SetStatus(new LocalizedText("onboarding.login_panel.status_opening_google").Resolve(),
                      info: true);
#else
            SetStatus(new LocalizedText("onboarding.login_panel.status_opening_wallet").Resolve(),
                      info: true);
#endif

            Task<AuthOutcome> attempt = _vm.ConnectWalletAsync();
            AuthOutcome outcome;
            try
            {
                outcome = await attempt.AsUniTask()
                    .Timeout(TimeSpan.FromSeconds(ConnectUiTimeoutSeconds), DelayType.UnscaledDeltaTime);
            }
            catch (TimeoutException)
            {
                if (_routed) return;
                FlowTrace.Fail("Auth",
                    $"wallet connect did not resolve within {ConnectUiTimeoutSeconds}s - restoring the login " +
                    "surface (guest escape was live throughout).");
                SetBusy(false);
#if GOOGLE_PLAY
                SetStatus(
                    new LocalizedText("onboarding.login_panel.status_google_no_response").Resolve(),
                    info: false);
#else
                SetStatus(
                    new LocalizedText("onboarding.login_panel.status_wallet_no_response").Resolve(),
                    info: false);
#endif
                WatchLateConnect(attempt);
                return;
            }
            catch (Exception e)
            {
                if (_routed) return;
                FlowTrace.Fail("Auth", "wallet connect threw at the panel: " + e.Message);
                SetBusy(false);
#if GOOGLE_PLAY
                SetStatus(new LocalizedText("onboarding.login_panel.status_google_failed").Resolve(),
                          info: false);
#else
                SetStatus(new LocalizedText("onboarding.login_panel.status_wallet_failed").Resolve(),
                          info: false);
#endif
                return;
            }
            HandleOutcome(outcome);
        }

        /// <summary>
        /// A connect that timed out at the UI is NOT cancelled underneath - Mobile Wallet
        /// Adapter can still come back minutes later and (via WalletSkinBootstrap) bind the
        /// save to that wallet. If that happens while the player is still sitting on this
        /// screen, honour it instead of leaving them bound-but-not-continued. Fully guarded:
        /// once the panel has routed or been destroyed this does nothing.
        /// </summary>
        private async void WatchLateConnect(Task<AuthOutcome> attempt)
        {
            AuthOutcome late;
            try { late = await attempt; }
            catch (Exception e) { FlowTrace.Warn("Auth", "late wallet connect ended in an error: " + e.Message); return; }

            if (this == null || _routed || _canvas == null) return;   // panel gone / player already in
            if (!late.Success) return;
            FlowTrace.Step("Auth", "wallet connect arrived AFTER the UI timeout and succeeded - continuing.");
            Continue();
        }

        // SOFTLOCK LAW: intentionally NOT gated on _busy. The escape hatch must work even
        // while a sign-in / wallet handshake is still pending - that pending await is
        // exactly the state a stuck player is trying to escape. Only _routed (already
        // continued) short-circuits it.
        private void OnPlayAsGuest()
        {
            if (_routed) return;
            FlowTrace.Step("Auth", "chose Play as Guest.");
            _vm.ContinueAsGuest();   // guest identity is minted on load; nothing to bind
            Continue();
        }

        private void HandleOutcome(AuthOutcome outcome)
        {
            if (_routed) return;
            if (outcome.Success)
            {
                // IDENTITY LAW (WO-837-B): the wallet is the ONLY identity, and the VM has
                // already bound it. View just proceeds.
                FlowTrace.Step("Auth", "wallet connect OK - continuing.");
                Continue();
                return;
            }
            SetStatus(outcome.Error, info: false);
            SetBusy(false);
        }

        // SOFTLOCK LAW (see the file header): busy locks every control EXCEPT Play as
        // Guest. _guest is deliberately absent from this method and is left interactable
        // for the lifetime of the panel - it is the only guaranteed way into the game,
        // and OnPlayAsGuest's own `if (_busy || _routed) return;` is dropped for the same
        // reason (a hung connect must not swallow the tap). Do NOT "tidy" _guest back in.
        private void SetBusy(bool busy)
        {
            _busy = busy;
            if (_connectWallet != null) _connectWallet.interactable = !busy;
        }

        private void SetStatus(string msg, bool info)
        {
            if (_status == null) return;
            _status.text = msg ?? "";
            _status.color = info ? ElarionUi.ParchmentDim : ElarionUi.Danger;
        }

        // =====================================================================
        //  Teardown (mirrors FoundingChoiceController)
        // =====================================================================
        private void Continue()
        {
            if (_routed) return;
            _routed = true;
            if (_panelHandle != null) PanelManager.NotifyClosed(_panelHandle);
            var cont = _onContinue;
            _onContinue = null;
            if (_canvas != null) Destroy(_canvas);
            Destroy(gameObject);
            cont?.Invoke();
        }

        private void OnDestroy()
        {
            if (_panelHandle != null) PanelManager.NotifyClosed(_panelHandle);
            if (_canvas != null) Destroy(_canvas);
        }

        // NOTE (WO-837-B): the MakeInputField helper (a TMP_InputField over a rounded
        // well) was deleted with the email form — this surface has no text entry at all
        // now. The pattern still lives in ClanChatPanel / RedeemCodePanel if a future
        // panel needs it; do not resurrect it here for an identity field.
    }
}

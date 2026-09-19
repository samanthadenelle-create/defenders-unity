using System;
using System.Text;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;
using TMPro;
using DeNelle.Core.Diagnostics;
using DeNelle.Core.UI;

namespace DeNelle.Core.Platform
{
#if GOOGLE_PLAY
    /// <summary>Non-crypto Play-channel compatibility seam. The Pi runtime, UI,
    /// endpoints, and labels are not compiled into a Google Play player.</summary>
    public static class PiSignInController
    {
        public static string SignedInUid => null;
        public static string SignedInUsername => null;
        public static bool IsSignedIn => false;
        public static event Action<string, string> OnSignedIn { add { } remove { } }
    }
#else
    /// <summary>
    /// Pi Network sign-in. Inside Pi Browser it AUTO-triggers on load and also shows a
    /// manual "Sign in with Pi" button. Flow: Pi.init (awaited) → Pi.authenticate(['username'])
    /// → POST the accessToken to /api/pi/verify, which validates it against api.minepi.com/v2/me
    /// (server-side, no API key) before a session is established. Off Pi Browser the platform
    /// stub reports unavailable and this is a no-op (no button, no auth) — the game runs unchanged.
    /// Ref: PI_INTEGRATION_SPEC.md, https://pi-apps.github.io/pi-sdk-docs/quick-start/genai/Authentication
    /// </summary>
    public sealed class PiSignInController : MonoBehaviour
    {
        // Same Vercel backend the rest of the client uses (GameStateService.BackendBase).
        private const string VerifyUrl = "https://defenders-of-the-realm-v2.vercel.app/api/pi/verify";

        /// <summary>
        /// HORIZONTAL screen-edge inset for the corner chip (WO-1083 defect #6).
        /// DERIVED, not eyeballed: the hero-select panel spans screen x [0.015,0.985] and
        /// ElarionUiKit's pixel-measured FrameCore zones put the frame's INTERIOR at panel
        /// x 0.945 — i.e. everything right of screen x = 2488 (of 2670) is border ART. The
        /// default 44-px margin put the chip at x[2326..2626], squarely on that border,
        /// which is the defect. 240 px lands it at x[2130..2430], clear of the border strip
        /// by ~58 px and inside the frame's dark header region. Fixed pixels by law; the
        /// VERTICAL inset stays <see cref="SafeAreaInset.EdgeMarginPx"/>, which already
        /// seats the chip in FrameCore's header band (screen y 56..139).
        /// </summary>
        private const float FramedCornerMarginXPx = 240f;

        // WO-1317 (owner ruling 2026-09-02): BUILD-DRIVEN, no longer a flat true.
        //
        // RCA: this field is a [SerializeField], but NOTHING carries a serialized override --
        // grep proves no scene or prefab holds a PiSignInController; the component is added at
        // runtime, so THIS INITIALIZER IS WHAT SHIPS. The initializer was `true`, so the
        // published app authenticated against the Pi TESTNET SANDBOX while the owner's app is
        // registered as MAINNET/production in the Pi Developer Portal. Sandbox and mainnet are
        // different environments, so authentication that had worked (captured web_trace,
        // 2026-09-01: "PiInit(sandbox=True)" followed by "Signed in as samanthadenelle") stopped
        // once the portal app moved to production. The tooltip had said "flip off for mainnet
        // go-live" since the field was written; nothing enforced it.
        //
        // Editor and DEVELOPMENT builds keep sandbox so testnet stays testable without a code
        // edit; a SHIP build is mainnet. Deliberately NOT a runtime flag or PlayerPrefs -- the
        // environment must be decided by the artifact, not by state a device can carry over.
        //
        // WO-1318: the expression MOVED to PiEnvironment.Sandbox (byte-identical semantics) so the
        // payment path reads the SAME answer as sign-in. A second copy of this boolean would let a
        // player authenticate on mainnet and be asked to pay on testnet.
        [Tooltip("Testnet/Sandbox. Build-driven (WO-1317): sandbox in Editor/dev builds, MAINNET " +
                 "in ship builds. Do not hardcode true again - that ships testnet to production.")]
        [SerializeField] private bool sandbox = PiEnvironment.Sandbox;

        public static string SignedInUid { get; private set; }
        public static string SignedInUsername { get; private set; }
        public static bool IsSignedIn => !string.IsNullOrEmpty(SignedInUid);
        public static event Action<string, string> OnSignedIn; // (uid, username)

        private IPiPlatform _pi;
        private Button _button;
        private TMP_Text _label;
        private bool _signingIn; // guard: auto-fire + manual click must not double-run (orphans the shared TCS)
        // WO-603: the active currency/auth/branding skin. AuthMode==PiSdk is the live Pi path
        // (unchanged); AuthMode==SolanaWallet swaps this corner button to a wallet-connect entry.
        private CurrencySkin _skin;
        // True when this corner is the wallet-connect entry (SolanaWallet auth mode). Set in
        // BuildButton; everything wallet-related below is gated on it so the Pi path is untouched.
        private bool _walletSkin;

        /// <summary>Spawns the controller once at boot if not already present.</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (FindObjectOfType<PiSignInController>() != null) return;
            var go = new GameObject("PiSignInController");
            DontDestroyOnLoad(go);
            go.AddComponent<PiSignInController>();
        }

        private void Start()
        {
            // WO-603: resolve the active skin BEFORE the button is built (synchronous — skin.json
            // via Resources + a URL-param read). Default = Pi, so the live path is unchanged.
            _skin = CurrencySkinResolver.Active;
            _pi = PiPlatform.Current;
            // ALWAYS show the manual "Sign in with Pi" button so the user can trigger sign-in even
            // if auto-detection is delayed/flaky. The Pi SDK (sdk.minepi.com/pi-sdk.js) loads
            // ASYNC, so IsAvailable can be false for the first moments after boot — gating the
            // button on it (the old bug) hid the sign-in entirely in the Pi Desktop preview.
            BuildButton();
            // Earns-its-place (owner 2026-07-02): the button lives where a login DECISION makes
            // sense — the Title/menu context — not riding the HUD all game. Auto sign-in still
            // runs everywhere; once signed in the button is gone for good.
            UnityEngine.SceneManagement.SceneManager.activeSceneChanged += (_, __) => UpdateButtonVisibility();
            UpdateButtonVisibility();
            // 2026-08-05 Seeker capture: wallet connect succeeded end to end ("Connect OK -
            // CHKK...sfkC") and this button still read "Connect Wallet", because nothing ever
            // told it. Subscribe to the connected-state signal under the wallet skin only
            // (the Pi path stays byte-identical); the current state was already applied in
            // BuildButton, so a button built AFTER the connect is correct too.
            if (_walletSkin)
                CurrencySkinResolver.WalletConnectionChanged += OnWalletConnectionChanged;
            // Only the Pi skin auto-triggers Pi sign-in. Under the Solana/$SKR skin the corner
            // button is a wallet-connect entry (see BuildButton) and Pi polling never runs.
            if (_skin != null && _skin.AuthMode == SkinAuthMode.PiSdk)
            {
                // Pi Browser authentication can background its WebView for native consent.
                // Do not overlap that with Unity/catalog startup. The visible Title button
                // is the user-gesture boundary and remains retryable.
                SetButton("Sign in with Pi", true);
                FlowTrace.Step("Pi", "Pi authentication deferred until the player taps Sign in on Title.");
            }
        }

        private void OnDestroy()
        {
            // No leaks: this is a DDOL widget, but a domain reload / a second boot must not
            // leave a dead subscriber holding a destroyed label. Unsubscribing an unsubscribed
            // handler is a no-op, so this is safe under either skin.
            CurrencySkinResolver.WalletConnectionChanged -= OnWalletConnectionChanged;
        }

        private void OnWalletConnectionChanged(bool connected, string shortAddress) => ApplyWalletConnectionState();

        /// <summary>
        /// Paints the corner button from the CURRENT wallet connection state
        /// (CurrencySkinResolver). Called three ways on purpose: at BUILD time (a button
        /// created after the connect must show connected, not just live transitions), on
        /// the change event, and on every scene change. Idempotent — it no-ops when the
        /// label already says the right thing, so the scene-change call cannot spam the trace.
        /// </summary>
        private void ApplyWalletConnectionState()
        {
            if (!_walletSkin || _label == null) return;

            bool connected = CurrencySkinResolver.IsWalletConnected;
            string shortAddress = CurrencySkinResolver.ConnectedWalletShortAddress;
            // The TEXT carries the state — never colour alone. ASCII only ("Wallet CHKK...sfkC"),
            // because TMP renders the U+2026 ellipsis of WalletAccount.ShortAddress as tofu.
            string desired = connected && !string.IsNullOrEmpty(shortAddress)
                ? "Wallet " + shortAddress
#if GOOGLE_PLAY
                : "Continue with Google";
#else
                : "Connect Wallet";
#endif
            if (_label.text == desired) return;

            // Connected: stop it being tappable. There is no disconnect surface here and one
            // is NOT invented — a tap would only early-out in the service anyway (the 0.0ms
            // "-> Connect / <- Connect" pairs in the capture).
            bool interactable = !connected;
            SetButton(desired, interactable);
            FlowTrace.Step("Wallet", $"Corner auth button updated: '{desired}' (interactable={interactable}).");
        }

        private void UpdateButtonVisibility()
        {
            if (_button == null) return;
            string scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
            bool menuContext = scene == "Title" || scene == "HeroSelect";
            _button.transform.parent.gameObject.SetActive(!IsSignedIn && menuContext);
            // Cheap safety net: if the connect landed while this widget was between scenes,
            // the state is re-read here rather than depending on catching the event.
            ApplyWalletConnectionState();
        }

        /// <summary>Manual trigger (the button).</summary>
        public void SignIn() => SignInAsync().Forget();

        /// <summary>
        /// The Pi SDK script loads asynchronously, so window.Pi may not be ready when Start() runs.
        /// Poll briefly (~10s) so AUTO sign-in fires the instant the SDK becomes available (the Pi
        /// portal auto-detects a signed-in user within seconds); otherwise the manual button remains.
        /// </summary>
        private async UniTaskVoid WaitForPiThenAutoSignIn()
        {
            for (int i = 0; i < 20; i++) // ~10s @ 500ms
            {
                if (_pi == null) _pi = PiPlatform.Current;
                if (_pi != null && _pi.IsAvailable)
                {
                    // WO-678 Lane C: window.Pi exists in ANY browser once pi-sdk.js loads, but only
                    // the real Pi Browser host ever answers it — auto-firing Pi.init elsewhere
                    // guarantees one doomed promise the SDK rejects at 120s ("Promise with id 0
                    // timed out after 120000 ms"). Gate AUTO sign-in on the UA environment check;
                    // the manual button stays available everywhere (Lane A absorbs its rejection).
                    if (!WebGLPiPlatform.IsPiBrowserEnvironment)
                    {
                        FlowTrace.Step("Pi", "Pi SDK loaded but UA is not Pi Browser — skipping auto sign-in; manual button available.");
                        SetButton("Sign in with Pi", true);
                        return;
                    }
                    SignInAsync().Forget(); // auto-trigger the moment Pi is ready
                    return;
                }
                await UniTask.Delay(500);
            }
            FlowTrace.Step("Pi", "Pi not auto-detected after 10s — manual 'Sign in with Pi' button available.");
            SetButton("Sign in with Pi", true);
        }

        private async UniTaskVoid SignInAsync()
        {
            // Auto-fire (WaitForPiThenAutoSignIn) and the manual button both call this. They share the
            // single _initTcs/_authTcs on WebGLPiPlatform, so a second overlapping run overwrites the
            // first's TCS and orphans its await forever. Guard so only one sign-in runs at a time.
            if (_signingIn) { FlowTrace.Step("Pi", "SignIn already in progress — ignoring duplicate trigger."); return; }
            _signingIn = true;
            try
            {
                SetButton("Signing in...", false); // ASCII only - TMP renders U+2026 as tofu

                // ROOT CAUSE of the 2026-07-01 break: the Pi SDK calls resolve only via a JS promise
                // callback (WebGLPiPlatform HandleCallback). If the promise never settles — a dismissed
                // Pi consent popup or an SDK stall in the Pi Desktop preview — the bare `await` HANGS
                // FOREVER, leaving the button stuck on "Signing in…" (and it now auto-fires on load, so it
                // hangs with no user action). Bound every SDK await with a timeout so sign-in ALWAYS
                // resolves to a retryable state instead of a dead screen. Proven from data: client traces
                // flow but ZERO /api/pi/verify calls -> the flow dies at Init/Authenticate before verify.
                // WO-1321 (owner ruling 2026-09-02): TRY THE DECLARED ENVIRONMENT, THEN THE OTHER ONE.
                //
                // Why this exists. Sandbox and mainnet are DIFFERENT Pi environments and an app is
                // registered in exactly one; initialising against the wrong one fails authentication
                // with no message that names the cause. We had contradictory evidence about which one
                // this app is:
                //   - captured web_trace, 2026-09-01: three sessions did `PiInit(sandbox=True)` and
                //     each reached "Signed in as samanthadenelle" -- a TESTNET init that AUTHENTICATED,
                //     which is evidence the app is testnet;
                //   - the owner, asked directly on 2026-09-02, answered MAINNET, and WO-1317 shipped
                //     sandbox=false on that answer;
                //   - the owner then recalled reading that the app is on testnet.
                //
                // Rather than keep guessing and burning a device test per guess, the flow now tries the
                // build's declared environment and, if that round fails, RETRIES ONCE on the opposite
                // one. Both attempts are traced with their environment, so a single real Pi Browser
                // session answers the question for good -- and the player signs in either way.
                //
                // This is a DIAGNOSTIC + RESILIENCE measure, not a licence to stop knowing. Once a
                // capture proves which environment is real, set PiEnvironment.Sandbox accordingly; the
                // fallback then costs nothing because the first attempt always wins.
                var attempt = await TryInitAndAuthenticate(sandbox);
                if (!attempt.Ok)
                {
                    FlowTrace.Warn("Pi", $"sign-in failed on {EnvName(sandbox)} ({attempt.Reason}) - " +
                                         $"retrying once on {EnvName(!sandbox)} (WO-1321).");
                    attempt = await TryInitAndAuthenticate(!sandbox);
                    if (attempt.Ok)
                        FlowTrace.Warn("Pi", $"ENVIRONMENT MISMATCH PROVEN: sign-in succeeded on " +
                                             $"{EnvName(!sandbox)} after failing on {EnvName(sandbox)}. " +
                                             $"Set PiEnvironment.Sandbox to {(!sandbox).ToString().ToLowerInvariant()}.");
                }
                if (!attempt.Ok)
                {
                    FlowTrace.Warn("Pi", $"Pi sign-in failed on BOTH environments. Last: {attempt.Reason}");
                    SetButton("Sign in with Pi", true);
                    return;
                }
                PiAuthResult auth = attempt.Auth;

                // ⛔ WO-1318 - SIGN-IN DELIBERATELY STILL ASKS FOR `username` ONLY. Do not add
                // `payments` here.
                //
                // The Pi payment path needs the `payments` scope, and the obvious edit is to widen
                // this array. That edit is REFUSED, on the WO's own acceptance criterion 6: every
                // existing player granted this app `username` alone. Widening the scope on the
                // SIGN-IN path re-prompts each of them for consent at the one moment they have no
                // context for it (app launch, before they asked to buy anything) and turns a dismissed
                // or failed consent into a FAILED SIGN-IN - i.e. the whole game becomes unreachable
                // for an existing player because of a purchase feature they never touched.
                //
                // Instead the `payments` scope is requested LAZILY, immediately before
                // Pi.createPayment, by PiBrowserPaymentProvider.EnsurePaymentsScope(). A player who
                // never buys is never asked; a player who does buy is asked at the exact moment the
                // request makes sense; and a refusal there costs a purchase, never a session.
                // Pi.authenticate is idempotent and additive, so the second call simply widens the
                // grant. It also re-registers onIncompletePaymentFound (see PiBridge.jslib), which is
                // how a stranded payment gets a second chance to settle.
                bool verified = await VerifyWithBackend(auth.AccessToken);
                if (!verified)
                {
                    SetButton("Retry Pi sign-in", true);
                    return;
                }

                // WO-603: NeonDB identity-key selection. DEFAULT OFF (skin.bindIdentityOnAuth=false)
                // so today's Pi deployment keeps its current identity behaviour (zero regression).
                // Turning it on binds the skin-appropriate key (Pi UID here) as the NeonDB playerId —
                // that changes which key writes to NeonDB, so it is gated behind a migration decision
                // (see WO-603 RESULT). Guarded: never binds a null/empty key.
                if (_skin != null && _skin.BindIdentityOnAuth)
                {
                    string idKey = _skin.ResolveIdentityKey(SignedInUid, null);
                    if (!string.IsNullOrEmpty(idKey))
                    {
                        DeNelle.Core.State.GameStateService.Instance?.BindWallet(idKey);
                        FlowTrace.Step("Skin", $"Bound NeonDB identity key ({_skin.IdentityKeyKind}) from Pi sign-in.");
                    }
                }

                SetButton($"Pi: {SignedInUsername}", false);
                OnSignedIn?.Invoke(SignedInUid, SignedInUsername);
                FlowTrace.Step("Pi", $"Signed in as {SignedInUsername} (uid bound to session).");
            }
            catch (Exception e)
            {
                FlowTrace.Fail("Pi", $"SignIn threw: {e.Message}");
                SetButton("Sign in with Pi", true);
            }
            finally
            {
                _signingIn = false;
            }
        }

        // WO-1321: the outcome of ONE environment's init+authenticate round.
        private struct SignInAttempt
        {
            public bool Ok;
            public PiAuthResult Auth;
            public string Reason;   // never null on failure - it is what the trace prints
        }

        /// <summary>ASCII-only environment label for traces (TMP renders non-ASCII as tofu).</summary>
        private static string EnvName(bool useSandbox) => useSandbox ? "TESTNET/sandbox" : "MAINNET";

        /// <summary>
        /// One full init+authenticate round against a SPECIFIC Pi environment (WO-1321).
        ///
        /// Every SDK await stays bounded exactly as before (WO 2026-07-01 root cause): the Pi SDK
        /// resolves only through a JS promise callback, so an unbounded await on a dismissed consent
        /// popup or a stalled SDK hangs FOREVER and leaves the button dead. A timeout here is a
        /// FAILED ATTEMPT, not a dead screen - which is also what lets the caller try the other
        /// environment instead of giving up.
        /// </summary>
        private async UniTask<SignInAttempt> TryInitAndAuthenticate(bool useSandbox)
        {
            string env = EnvName(useSandbox);

            bool inited;
            try { inited = await _pi.Init(useSandbox).Timeout(TimeSpan.FromSeconds(20)); }
            catch (TimeoutException)
            {
                return new SignInAttempt { Ok = false, Reason = $"Pi.init timed out after 20s on {env}" };
            }
            if (!inited)
                return new SignInAttempt { Ok = false, Reason = $"Pi.init failed/unavailable on {env}" };

            FlowTrace.Step("Pi", $"Pi.init OK on {env}.");

            // ⛔ WO-1318 - SIGN-IN DELIBERATELY ASKS FOR `username` ONLY. Do not add `payments` here.
            //
            // The Pi payment path needs the `payments` scope, and the obvious edit is to widen this
            // array. That edit is REFUSED, on WO-1318 acceptance criterion 6: every existing player
            // granted this app `username` alone. Widening the scope on the SIGN-IN path re-prompts
            // each of them at the one moment they have no context for it (app launch, before they
            // asked to buy anything) and turns a dismissed or failed consent into a FAILED SIGN-IN -
            // i.e. the whole game becomes unreachable for an existing player because of a purchase
            // feature they never touched.
            //
            // Instead `payments` is requested LAZILY, immediately before Pi.createPayment, by
            // PiBrowserPaymentProvider.EnsurePaymentsScope(). A player who never buys is never asked;
            // a player who does buy is asked when the request makes sense; and a refusal there costs a
            // purchase, never a session. Pi.authenticate is idempotent and additive, so the second
            // call simply widens the grant. It also re-registers onIncompletePaymentFound (see
            // PiBridge.jslib), which is how a stranded payment gets a second chance to settle.
            PiAuthResult auth;
            try { auth = await _pi.Authenticate(new[] { "username" }).Timeout(TimeSpan.FromSeconds(30)); }
            catch (TimeoutException)
            {
                return new SignInAttempt { Ok = false, Reason = $"Pi.authenticate timed out after 30s on {env} (consent not completed)" };
            }
            if (!auth.Ok || string.IsNullOrEmpty(auth.AccessToken))
                return new SignInAttempt { Ok = false, Reason = $"Pi auth failed on {env}: {auth.Error}" };

            FlowTrace.Step("Pi", $"Pi.authenticate OK on {env}.");
            return new SignInAttempt { Ok = true, Auth = auth, Reason = null };
        }

        // Server-side token validation is the trust boundary — never trust the frontend identity.
        private async UniTask<bool> VerifyWithBackend(string accessToken)
        {
            string json = "{\"accessToken\":\"" + accessToken.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"}";
            using var req = new UnityWebRequest(VerifyUrl, "POST")
            {
                uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json)),
                downloadHandler = new DownloadHandlerBuffer(),
            };
            req.SetRequestHeader("Content-Type", "application/json");
            req.timeout = 20; // never hang on a stalled backend

            try { await req.SendWebRequest().ToUniTask(); }
            catch (Exception e) { FlowTrace.Warn("Pi", $"/verify transport error: {e.Message}"); return false; }

            if (req.result != UnityWebRequest.Result.Success)
            {
                FlowTrace.Warn("Pi", $"/verify HTTP {req.responseCode}");
                return false;
            }

            VerifyResp resp;
            try { resp = JsonUtility.FromJson<VerifyResp>(req.downloadHandler.text); }
            catch (Exception e) { FlowTrace.Warn("Pi", $"/verify parse error: {e.Message}"); return false; }

            if (resp == null || !resp.success || string.IsNullOrEmpty(resp.uid))
            {
                FlowTrace.Warn("Pi", $"/verify rejected: {resp?.error}");
                return false;
            }

            SignedInUid = resp.uid;
            SignedInUsername = string.IsNullOrEmpty(resp.username) ? resp.uid : resp.username;
            return true;
        }

        [Serializable]
        private class VerifyResp { public bool success; public string uid; public string username; public string error; }

        // --- corner button, dressed by the shared kit (rounded glass + kit font + shared
        // press feedback) so it reads as OUR chrome, not a pasted web widget. Pi identity
        // survives as the violet fill over the kit's rounded sprite. Own overlay canvas is
        // deliberate: this is a DDOL cross-scene widget that must outlive every scene canvas.
        private void BuildButton()
        {
            var canvasGo = new GameObject("PiSignInCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasGo.transform.SetParent(transform, false);
            // WO-596 privacy: the button can show the Pi username — hide it from bug-report captures.
            DeNelle.Core.Diagnostics.PrivacySensitiveUi.Register(canvasGo);
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 5000;
            // WO-868: this corner is authored in DEVICE PIXELS (fixed-pixel bands, never
            // fractions of parent) and SafeAreaInset works in screen px, so pin the scaler
            // to 1:1 explicitly instead of relying on the CanvasScaler default. The
            // 2026-08-04 Seeker capture measured the button at exactly 300 px wide for a
            // 300-unit sizeDelta, confirming this is already the live behaviour.
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
            scaler.scaleFactor = 1f;

            var holder = new GameObject("PiSignInButton", typeof(RectTransform));
            holder.transform.SetParent(canvasGo.transform, false);
            var rt = holder.GetComponent<RectTransform>();
            // Owner 2026-07-16 ("Sign in wit..." truncated on web): 220px clipped "Sign in with Pi"
            // to an ellipsis. Widen to fit the full label; the TMP label auto-sizes (below) so it
            // never truncates under either skin ("Connect Wallet" is shorter and also fits).
            //
            // WO-868 HEIGHT = MinTouchPx, not 60. PROVEN from the device capture
            // (docs/ui-review/2026-08-04-seeker/01-title-screen.png, 1:1 @ 2670x1200): the
            // visible box measured 300 x 112 with its top edge OFF-SCREEN at y = -10. The kit
            // touch floor (ElarionUiKit.ClampMinTouch) grows a sub-floor button SYMMETRICALLY
            // ABOUT ITS CENTRE, so a 60-px holder silently became 112 and pushed 26 px past the
            // holder on EVERY side — the same growth that broke the Echo picker (WO-852). Author
            // the holder AT the floor so the guard has nothing to grow and the rect stays where
            // the safe-area math put it.
            rt.sizeDelta = new Vector2(300f, ElarionUiKit.MinTouchPx);
            // WO-868 SAFE AREA: was a raw anchoredPosition of (-16,-16) — ~6 dp on the Seeker,
            // which reads as flush AND sits inside the rounded-corner / camera-cutout band, so
            // "Connect Wallet" was clipped off the top-right corner. ApplyTopRight sets the
            // anchors + pivot + position from Screen.safeArea (so it adapts to ANY cutout) plus
            // a fixed-pixel margin, and re-fits on rotation / resolution change.
            // WO-1083 defect #6 (hero-select capture tmp/heroselect2-104958.png, 2670x1200):
            // with the default 44-px margin this chip lands at x[2326..2626] — and the
            // hero-select panel's FrameCore border art runs from x 2488 (the frame's measured
            // interior edge, panel x 0.945) to the panel edge at 2630, so the chip drew ON TOP
            // of the frame's top-right border. This button is
            // shown in EXACTLY TWO scenes (UpdateButtonVisibility: "Title" and "HeroSelect"),
            // both full-screen menu contexts, so a larger HORIZONTAL inset is the whole fix:
            // it pulls the chip clear of the border and into the header band, and on Title
            // (no frame) it simply sits slightly further in.
            // Vertical is NOT changed — 44 px already seats the chip inside FrameCore's
            // header band (screen y 56..139 at this size), which is where the WO-1083 mockup
            // puts it. Widening it too would push the chip down into the body well.
            // The margin stays FIXED PIXELS and still routes through SafeAreaInset, so the
            // WO-868 contract (cutout-aware, never a hand-placed inset) is intact.
            SafeAreaInset.ApplyTopRight(rt, FramedCornerMarginXPx, SafeAreaInset.EdgeMarginPx);

            // WO-603: under the Solana/$SKR skin this corner is a wallet-connect entry, not Pi sign-in.
            // The full wallet-connect flow lives in DeNelle.Wallet (Core cannot reference it) and is a
            // flagged follow-up — the button routes through CurrencySkinResolver.RequestWalletConnect(),
            // which warns (no silent failure) until that handler is subscribed.
            bool walletSkin = _skin != null && _skin.AuthMode == SkinAuthMode.SolanaWallet;
            _walletSkin = walletSkin;
            string initialLabel = walletSkin
#if GOOGLE_PLAY
                ? "Continue with Google"
#else
                ? "Connect Wallet"
#endif
                : "Sign in with Pi";
            Action onClick = walletSkin
                ? (Action)CurrencySkinResolver.RequestWalletConnect
                : SignIn;

            _button = ElarionUiKit.Button(holder.transform, initialLabel, ElarionUiKit.ButtonKind.Quiet,
                                          Vector2.zero, Vector2.one, onClick);
            if (_button.targetGraphic is Image img)
                img.color = walletSkin
                    ? new Color(0.09f, 0.72f, 0.55f, 0.95f)  // Solana/Seeker teal-green over the kit glass
                    : new Color(0.43f, 0.30f, 0.78f, 0.95f); // Pi violet over the kit's rounded glass
            _label = _button.GetComponentInChildren<TMP_Text>();
            // No ellipsis: auto-size within the widened button + single-line overflow so the full
            // label always renders (the "Sign in wit..." truncation fix, owner 2026-07-16).
            if (_label != null)
            {
                _label.enableAutoSizing = true;
                _label.fontSizeMin = 14f;
                _label.fontSizeMax = 22f;
                _label.enableWordWrapping = false;
                _label.overflowMode = TMPro.TextOverflowModes.Overflow;
            }

            // Read the CURRENT connected state now, not only on the next transition: the
            // wallet may already be connected by the time this button is built (a panel
            // opened after connecting, or a rebuild on scene change). This is the half that
            // an event-only signal would have missed. No-ops under the Pi skin.
            ApplyWalletConnectionState();
        }

        private void SetButton(string text, bool interactable)
        {
            if (_label != null) _label.text = text;
            if (_button != null) _button.interactable = interactable;
        }
    }
#endif
}

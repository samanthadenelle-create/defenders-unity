// =============================================================================
// CurrencySkinResolver — resolves the ACTIVE CurrencySkin once, before first render
// -----------------------------------------------------------------------------
// WO-603, Option B (runtime skin.json + URL-param override). One build artifact
// serves both the Pi and the Solana/$SKR skins; which one is live is decided at
// runtime here — no rebuild, config only.
//
// Resolution order (first hit wins), all synchronous so it completes before the
// first view builds:
//   1. URL query param  ?skin=pi | ?skin=skr | ?skin=wallet   (WebGL — the SKR
//      Vercel deployment appends ?skin=skr; mirrors FeatureFlags.ApplyUrlActivationOnce,
//      allow-listed to pi|skr|wallet only so a crafted link can only swap skin,
//      never game state).
//   2. skin.json "active" field  (Assets/Resources/Data/Canonical/skin.json).
//   3. "wallet" — the V1 GENERIC WALLET default (WO-713 owner ruling 2026-07-13:
//      "remove the Pi symbol on inventory screen ... leave generic as wallet").
//      V1 ships ZERO crypto; the Pi/SKR skins stay in the table for the later
//      crypto arc (?skin=pi / ?skin=skr / skin.json "active" re-select them).
//
// The generic wallet skin is PRESENTATION-only: no symbol glyph (views show a
// wallet ICON + the plain amount); auth/identity stay on the Pi path so the
// live auth behaviour is unchanged by the currency-presentation swap.
//
// PRESENTATION READS CurrencySkinResolver.Active — never hardcoded π / Pi / $SKR.
// =============================================================================

using System;
using Newtonsoft.Json.Linq;
using UnityEngine;
using DeNelle.Core.Diagnostics;

namespace DeNelle.Core.Platform
{
    /// <summary>Resolves + caches the active <see cref="CurrencySkin"/> (WO-603).</summary>
    public static class CurrencySkinResolver
    {
        /// <summary>StreamingAssets/Resources-relative path to the skin table.</summary>
        private const string SkinJsonPath = "Data/Canonical/skin.json";

        private static CurrencySkin _active;

        /// <summary>
        /// The active skin. Resolved lazily on first access (synchronous — Resources.Load +
        /// a URL-param read), so any view that touches it gets the resolved skin before it
        /// builds. Never null after access; falls back to <see cref="CurrencySkin.PiDefault"/>.
        /// </summary>
        public static CurrencySkin Active
        {
            get
            {
                if (_active == null) Resolve();
                return _active;
            }
        }

        /// <summary>True once the active skin is the SKR/Solana skin.</summary>
        public static bool IsSkr => Active.SkinId == "skr";

        /// <summary>True once the active skin is the V1 generic-wallet skin (no crypto symbol).</summary>
        public static bool IsGenericWallet => Active.SkinId == "wallet";

        /// <summary>
        /// The V1 GENERIC WALLET skin (WO-713 owner ruling 2026-07-13): currency rows show a
        /// wallet ICON + the plain amount — no Pi/SKR symbol (CurrencySymbol is intentionally
        /// EMPTY; views render icon + amount and may label with CurrencyName). Auth/identity
        /// mirror the Pi defaults so making this skin active changes PRESENTATION ONLY —
        /// the live sign-in/identity behaviour is untouched (V1 ships zero crypto; the Pi and
        /// SKR skins remain in skin.json for the later crypto arc).
        /// </summary>
        public static CurrencySkin WalletDefault { get; } = new CurrencySkin(
            skinId: "wallet",
            currencySymbol: "",
            currencyName: "Wallet",
            authMode: SkinAuthMode.PiSdk,
            brandingKey: "",
            storeCtaVerb: "Spend",
            identityKeyKind: SkinIdentityKeyKind.PiUid,
            bindIdentityOnAuth: false);

        /// <summary>
        /// Raised when a view's auth button under the SolanaWallet skin is pressed. The
        /// Wallet assembly (DeNelle.Wallet) subscribes to actually drive wallet-connect —
        /// Core cannot reference Wallet, so this is the seam. If nothing is subscribed the
        /// button logs a Guard warning (no silent failure) — see the RESULT follow-up note.
        /// </summary>
        public static event Action WalletConnectRequested;

        /// <summary>Fired by a view's "Connect Wallet" button (SKR skin). Routes to the
        /// Wallet-assembly subscriber, or warns if the full flow is not yet wired.</summary>
        public static void RequestWalletConnect()
        {
            var handler = WalletConnectRequested;
            if (handler == null)
            {
                FlowTrace.Warn("Skin",
#if GOOGLE_PLAY
                    "Continue with Google pressed but no identity handler is subscribed " +
                    "(WalletConnectRequested) - Google Play identity setup is incomplete.");
#else
                    "Connect Wallet pressed but no wallet-connect handler is subscribed " +
                    "(WalletConnectRequested) — the full Solana wallet flow is a follow-up (WO-603 flagged).");
#endif
                return;
            }
            try { handler.Invoke(); }
            catch (Exception e) { FlowTrace.Fail("Skin", $"WalletConnectRequested handler threw: {e.Message}"); }
        }

        /// <summary>
        /// Raised when a view asks to DISCONNECT / reset the wallet. Symmetric with
        /// <see cref="WalletConnectRequested"/>: the Wallet assembly subscribes and does the work,
        /// because Core can never reference DeNelle.Wallet.
        /// <para>
        /// ⛔ WHY THIS EXISTS (2026-08-24). The owner ruled on 2026-08-17 — quoted verbatim in
        /// WalletSkinBootstrap — <i>"yes it should auto connect, there is a menu option to reset"</i>.
        /// The AUTO-CONNECT half shipped. The RESET half never did: WalletService.Disconnect() is
        /// fully implemented (provider disconnect, signer unregister, MwaSessionStore.Clear, and it
        /// even publishes the disconnected state so labels fall back) and was called by NOTHING.
        /// A whole working mechanism with no way in.
        /// </para>
        /// <para>
        /// ⚠ It is not only a test convenience: with auto-resume ON, a player who connects the wrong
        /// wallet is reconnected to it silently on every cold start, forever, with no way out short
        /// of reinstalling the app. Reset is what makes auto-connect safe to have.
        /// </para>
        /// </summary>
        public static event Action WalletDisconnectRequested;

        /// <summary>Fired by a "Disconnect / Reset wallet" control. Routes to the Wallet assembly.</summary>
        public static void RequestWalletDisconnect()
        {
            var handler = WalletDisconnectRequested;
            if (handler == null)
            {
                // ⚠ NEVER SILENT (§12). A dead reset button that merely does nothing is how the
                // player concludes the wallet cannot be changed at all.
                // WO-1363: the Play variant of this line drops the "Disconnect Wallet" phrase (which
                // contains the "connect wallet" audit token) and the SKR reference. Still never
                // silent (§12) - both branches warn with the same meaning.
#if GOOGLE_PLAY
                FlowTrace.Warn("Skin",
                    "Identity reset pressed but no handler is subscribed (WalletDisconnectRequested) - " +
                    "no identity provider installed one. Nothing was reset.");
#else
                FlowTrace.Warn("Skin",
                    "Disconnect Wallet pressed but no handler is subscribed (WalletDisconnectRequested) - " +
                    "the Wallet assembly did not install one (SKR skin inactive?). Nothing was disconnected.");
#endif
                return;
            }
            try { handler.Invoke(); }
            catch (Exception e) { FlowTrace.Fail("Skin", $"WalletDisconnectRequested handler threw: {e.Message}"); }
        }

        // =====================================================================
        //  Connected-wallet state — the RETURN LEG of the connect seam (2026-08-05)
        // =====================================================================
        //
        // RequestWalletConnect (above) is the outbound half: view -> Wallet assembly.
        // There was NO inbound half at all, and the 2026-08-05 Seeker capture is the
        // proof: connect succeeded end to end ("Connect OK - CHKK...sfkC"), every
        // repeat tap early-outed in 0.0ms because the service was already connected -
        // and the corner button still read "Connect Wallet" forever, so the player
        // could not tell she was connected. The Wallet assembly PUBLISHES here;
        // Core/HUD views SUBSCRIBE (Core can never reference DeNelle.Wallet).
        //
        // Both an event AND a readable last-known state, deliberately: a view built
        // AFTER the connect (any panel opened later) never sees the event, so it must
        // be able to read the state at build time. Event-only was the other half of
        // this class of bug.

        /// <summary>
        /// Raised whenever the wallet connection state changes: (connected, shortAddress).
        /// The short address is the ASCII form - safe to put straight into a TMP label.
        /// </summary>
        public static event Action<bool, string> WalletConnectionChanged;

        /// <summary>True once a wallet connect has succeeded this session (last-known state).</summary>
        public static bool IsWalletConnected { get; private set; }

        /// <summary>
        /// The connected wallet's full base58 address, or empty when disconnected.
        /// NEVER log or render this - FlowTrace lines ride WebTraceSink into plaintext
        /// logs (see the WalletService.Connect privacy note); render
        /// <see cref="ConnectedWalletShortAddress"/> instead.
        /// </summary>
        public static string ConnectedWalletAddress { get; private set; } = string.Empty;

        /// <summary>
        /// ASCII short form ("CHKK...sfkC") for player-visible labels, or empty when
        /// disconnected. ASCII on purpose: TMP renders the U+2026 ellipsis glyph that
        /// WalletAccount.ShortAddress uses as tofu, so this does NOT reuse it.
        /// </summary>
        public static string ConnectedWalletShortAddress { get; private set; } = string.Empty;

        /// <summary>Published by the Wallet assembly the moment a connect succeeds.</summary>
        public static void PublishWalletConnected(string address)
        {
            if (string.IsNullOrEmpty(address))
            {
                FlowTrace.Warn("Wallet",
                    "PublishWalletConnected called with an EMPTY address - connected state not published " +
                    "(views would show a blank wallet label).");
                return;
            }

            ConnectedWalletAddress = address;
            ConnectedWalletShortAddress = ShortenAscii(address);
            IsWalletConnected = true;
            RaiseWalletConnectionChanged();
        }

        /// <summary>Published by the Wallet assembly on disconnect (idempotent).</summary>
        public static void PublishWalletDisconnected()
        {
            if (!IsWalletConnected && string.IsNullOrEmpty(ConnectedWalletAddress)) return;

            ConnectedWalletAddress = string.Empty;
            ConnectedWalletShortAddress = string.Empty;
            IsWalletConnected = false;
            RaiseWalletConnectionChanged();
        }

        /// <summary>
        /// ASCII "AbCd...WxYz" short form of a wallet address, or empty. Public so any
        /// TMP surface can shorten an address it already holds without re-deriving the
        /// masking rule (and without importing the ellipsis glyph).
        /// </summary>
        public static string ShortenAscii(string address)
        {
            if (string.IsNullOrEmpty(address)) return string.Empty;
            if (address.Length <= 11) return address;   // shortening would not shorten
            return address.Substring(0, 4) + "..." + address.Substring(address.Length - 4);
        }

        private static void RaiseWalletConnectionChanged()
        {
            // Masked only (privacy) - and this line is what proves the signal fired in the
            // next capture, paired with the view-side "label updated" Step.
            FlowTrace.Step("Wallet",
                $"Wallet connection state published: connected={IsWalletConnected} " +
                $"({(IsWalletConnected ? ConnectedWalletShortAddress : "none")}).");

            var handler = WalletConnectionChanged;
            if (handler == null)
            {
                FlowTrace.Warn("Wallet",
#if GOOGLE_PLAY
                    "Identity state changed but NO view is subscribed (WalletConnectionChanged) - " +
                    "a signed-in player may still read 'Continue with Google' on screen.");
#else
                    "Wallet connection state changed but NO view is subscribed (WalletConnectionChanged) - " +
                    "a connected wallet may still be reading as 'Connect Wallet' on screen.");
#endif
                return;
            }
            try { handler.Invoke(IsWalletConnected, ConnectedWalletShortAddress); }
            catch (Exception e) { FlowTrace.Fail("Wallet", $"WalletConnectionChanged subscriber threw: {e.Message}"); }
        }

        // =====================================================================
        //  Wager currency per payment channel (WO-1366)
        // =====================================================================
        //
        // Owner rulings 2026-09-04: "the arena will go to the google play store, just needs
        // to remove crypto" / "both to use same logic just different curency for wagers".
        // ONE Arena, ONE code path; the CURRENCY is the only thing that varies by channel,
        // and it is resolved HERE - the existing per-channel currency seam - never by a
        // #if GOOGLE_PLAY inside the Arena module. Arena asks this resolver what it wagers
        // in and never branches on the channel itself.
        //
        //   GooglePlay       -> Crystals (GameState.Resources.Crystals; Play's own rail)
        //   SolanaDappStore  -> SKR (the existing client-stub wallet, byte-identical to today)
        //   Unknown          -> REFUSED, with a worded sentence (fail-closed, the same shape
        //                       as WO-1386's "Unknown channel: fail-closed like Solana")
        //   PiBrowser        -> REFUSED (Pi has no SKR and no Crystals-IAP rail; a crypto
        //                       account per the 2026-09-04 23:32 ruling - see the owner
        //                       question in the WO-1366 lane report)

        /// <summary>What the Arena wagers in on the current payment channel.</summary>
        public enum WagerCurrency
        {
            /// <summary>No wager rail on this channel - every wager is refused with a sentence.</summary>
            Refused = 0,
            /// <summary>The Seeker / dApp Store client-stub SKR balance (today's behaviour).</summary>
            Skr = 1,
            /// <summary>Google Play - the player's real Crystals (GameState.Resources.Crystals).</summary>
            Crystals = 2,
        }

        /// <summary>
        /// Resolves the wager currency for the RESOLVED payment channel
        /// (<see cref="Payments.PaymentChannelResolver.Current"/>).
        /// </summary>
        public static WagerCurrency ResolveWagerCurrency(out string refusal) =>
            ResolveWagerCurrency(Payments.PaymentChannelResolver.Current, out refusal);

        /// <summary>
        /// Resolves the wager currency for an explicit channel. Never throws. When the answer
        /// is <see cref="WagerCurrency.Refused"/>, <paramref name="refusal"/> carries the
        /// player-readable sentence (never empty - a refusal with no words is a dead button).
        /// </summary>
        public static WagerCurrency ResolveWagerCurrency(Payments.PaymentChannel channel, out string refusal)
        {
            switch (channel)
            {
                case Payments.PaymentChannel.GooglePlay:
                    refusal = string.Empty;
                    return WagerCurrency.Crystals;
                case Payments.PaymentChannel.SolanaDappStore:
                    refusal = string.Empty;
                    return WagerCurrency.Skr;
                case Payments.PaymentChannel.PiBrowser:
                    refusal = "Arena wagers are not available in the Pi Browser build.";
                    FlowTrace.Warn("Skin", "Wager currency REFUSED: channel=PiBrowser has no wager rail.");
                    return WagerCurrency.Refused;
                default:
                    refusal = "Arena wagers are unavailable: this build has no payment channel stamped.";
                    FlowTrace.Warn("Skin", $"Wager currency REFUSED: channel={channel} is not a wager rail (fail-closed).");
                    return WagerCurrency.Refused;
            }
        }

        /// <summary>
        /// The player-facing unit label for a wager currency. The SKR label comes from the
        /// SKR skin's CurrencyName so no crypto literal has to live in the Arena module;
        /// under GOOGLE_PLAY that skin's name is the neutral "Store credit" and the Skr
        /// branch is never resolved anyway.
        /// </summary>
        public static string WagerCurrencyName(WagerCurrency currency)
        {
            switch (currency)
            {
                case WagerCurrency.Crystals: return "Crystals";
                case WagerCurrency.Skr: return CurrencySkin.SkrDefault.CurrencyName;
                default: return "-";
            }
        }

        /// <summary>Forces a re-resolve (tests / a config hot-swap). Rarely needed.</summary>
        public static void Reload() { _active = null; Resolve(); }

        /// <summary>Test seam — pin a specific skin.</summary>
        public static void Override(CurrencySkin skin) => _active = skin ?? CurrencySkin.PiDefault;

        // =====================================================================
        //  Resolution
        // =====================================================================

        private static void Resolve()
        {
            string requested = ReadUrlSkinOverride();          // step 1 — URL param
            JObject table = LoadSkinTable();                   // skin.json (may be null)

#if GOOGLE_PLAY
            // The Play artifact is a fiat-IAP build. Force the existing symbol-free,
            // neutral presentation instead of inheriting Android's SKR routing.
            requested = "wallet";
            FlowTrace.Step("Skin", "Google Play artifact - resolving the neutral store skin.");
#else

            // WO-787 Part C (owner 2026-07-30): "if not Pi-facing should always be SKR."
            // Resolve WebGL currency from the HOST before skin.json's generic `active: wallet`
            // can erase the distinction: real Pi Browser = Pi; every other browser = SKR/Solana.
            // Only an explicit ?skin= URL override outranks host routing. The jslib UA check
            // externs to false off-WebGL, while non-Web players are handled separately below.
            // NOTE: the original WO's fix ("flip the hardcoded default") would have been a
            // NO-OP -- skin.json ships an explicit active:'wallet' that wins at step 2, and
            // the wallet skin's authMode is PiSdk; this gate must sit ABOVE step 2.
            if (string.IsNullOrEmpty(requested)
                && Application.platform == RuntimePlatform.WebGLPlayer)
            {
                bool piBrowser = WebGLPiPlatform.IsPiBrowserEnvironment;
                requested = piBrowser ? "pi" : "skr";
                FlowTrace.Step("Skin", piBrowser
                    ? "Pi Browser host detected - resolving the Pi skin."
                    : "WebGL host is not Pi Browser - resolving the SKR skin (WO-787).");
            }

            // OWNER 2026-07-30 ("flag off the Pi ... for SDK and EXE ... only live for vercel"):
            // NON-WEB PLAYERS (Android APK / Windows exe) always resolve the SKR skin — the Pi
            // surface (corner sign-in button, Pi auto-polling, Pi symbol) exists only on the
            // Vercel WebGL build, and there only inside the real Pi Browser per the WO-787 gate
            // above. PiSignInController follows Active.AuthMode, so this one gate flips the
            // corner button to wallet-connect and stops Pi polling on those platforms. The
            // EDITOR keeps skin.json routing so either skin stays testable without a build.
            if (string.IsNullOrEmpty(requested)
                && Application.platform != RuntimePlatform.WebGLPlayer
                && !Application.isEditor)
            {
                requested = "skr";
                FlowTrace.Step("Skin", "Non-web player (SDK/EXE) — Pi is Vercel-only; resolving the SKR skin (owner 2026-07-30).");
            }

            if (string.IsNullOrEmpty(requested))               // step 2 — skin.json "active"
                requested = table?["active"]?.ToString();

            if (string.IsNullOrEmpty(requested))               // step 3 — V1 generic-wallet default
                requested = "wallet";

#endif
            requested = requested.Trim().ToLowerInvariant();

            _active = BuildSkin(requested, table);
            FlowTrace.Step("Skin",
                $"Currency skin resolved: '{_active.SkinId}' (auth={_active.AuthMode}, " +
                $"symbol={_active.CurrencySymbol}, identity={_active.IdentityKeyKind}).");
        }

        /// <summary>
        /// Reads <c>?skin=pi</c>/<c>?skin=skr</c>/<c>?skin=wallet</c> from the WebGL page URL.
        /// Allow-listed to pi|skr|wallet only. Empty off-web (Application.absoluteURL is empty
        /// in editor/standalone). Never throws.
        /// </summary>
        private static string ReadUrlSkinOverride()
        {
            try
            {
                string url = Application.absoluteURL;
                if (string.IsNullOrEmpty(url)) return null;
                int q = url.IndexOf('?');
                if (q < 0) return null;

                foreach (var pair in url.Substring(q + 1).Split('&'))
                {
                    int eq = pair.IndexOf('=');
                    string key = (eq < 0 ? pair : pair.Substring(0, eq)).Trim();
                    if (!key.Equals("skin", StringComparison.OrdinalIgnoreCase)) continue;
                    string val = (eq < 0 ? "" : pair.Substring(eq + 1)).Trim().ToLowerInvariant();
                    if (val == "pi" || val == "skr" || val == "wallet")
                    {
                        FlowTrace.Step("Skin", $"?skin={val} detected on the page URL — overriding skin.json.");
                        return val;
                    }
                    if (!string.IsNullOrEmpty(val))
                        // WO-1755: this message used to spell out the three allowed skin ids in
                        // parentheses, and that literal was one of the 13 live forbidden-token hits
                        // the WO-1740 RCA measured in the Play AAB's global-metadata.dat (one of the
                        // ids is a gate token). ⛔ The ALLOW-LIST ITSELF -- the condition 8 lines up --
                        // is deliberately UNCHANGED; only the message's copy of it is gone, so the
                        // message cannot go stale against the condition either. It still names the
                        // rejected value, which is the part a reader actually needs.
                        FlowTrace.Warn("Skin", $"?skin={val} is not an allow-listed skin — ignored.");
                }
            }
            catch (Exception ex) { FlowTrace.Warn("Skin", "URL skin-override parse skipped: " + ex.Message); }
            return null;
        }

        /// <summary>Loads the skin.json table via the WebGL-safe CanonicalJson loader.
        /// Returns null (→ hardcoded defaults) when absent or garbled — never throws.</summary>
        private static JObject LoadSkinTable()
        {
            try
            {
                string json = CanonicalJson.Read(SkinJsonPath);
                if (string.IsNullOrEmpty(json))
                {
                    FlowTrace.Warn("Skin", "skin.json not found — using hardcoded skin defaults (Pi).");
                    return null;
                }
                return JObject.Parse(json);
            }
            catch (Exception ex)
            {
                FlowTrace.Warn("Skin", $"skin.json parse failed ({ex.Message}) — using hardcoded skin defaults.");
                return null;
            }
        }

        /// <summary>
        /// Builds a <see cref="CurrencySkin"/> for the requested id — from the skin.json
        /// entry when present, else the hardcoded default for that id, else the Pi default.
        /// </summary>
        private static CurrencySkin BuildSkin(string skinId, JObject table)
        {
            CurrencySkin fallback =
                skinId == "skr"    ? CurrencySkin.SkrDefault :
                skinId == "wallet" ? WalletDefault :
                                     CurrencySkin.PiDefault;

            JToken entry = table?["skins"]?[skinId];
            if (entry == null) return fallback;

            return new CurrencySkin(
                skinId:            Str(entry["skinId"], fallback.SkinId),
                currencySymbol:    Str(entry["currencySymbol"], fallback.CurrencySymbol),
                currencyName:      Str(entry["currencyName"], fallback.CurrencyName),
                authMode:          ParseAuth(entry["authMode"], fallback.AuthMode),
                brandingKey:       Str(entry["brandingKey"], fallback.BrandingKey),
                storeCtaVerb:      Str(entry["storeCtaVerb"], fallback.StoreCtaVerb),
                identityKeyKind:   ParseIdentity(entry["identityKeyKind"], fallback.IdentityKeyKind),
                bindIdentityOnAuth: entry["bindIdentityOnAuth"]?.Type == JTokenType.Boolean
                                        ? entry["bindIdentityOnAuth"].Value<bool>()
                                        : fallback.BindIdentityOnAuth);
        }

        private static string Str(JToken t, string fallback)
        {
            string s = t?.ToString();
            return string.IsNullOrEmpty(s) ? fallback : s;
        }

        private static SkinAuthMode ParseAuth(JToken t, SkinAuthMode fallback)
        {
            string s = t?.ToString();
            if (string.IsNullOrEmpty(s)) return fallback;
            return s.Trim().Equals("SolanaWallet", StringComparison.OrdinalIgnoreCase)
                ? SkinAuthMode.SolanaWallet
                : s.Trim().Equals("PiSdk", StringComparison.OrdinalIgnoreCase)
                    ? SkinAuthMode.PiSdk
                    : fallback;
        }

        private static SkinIdentityKeyKind ParseIdentity(JToken t, SkinIdentityKeyKind fallback)
        {
            string s = t?.ToString();
            if (string.IsNullOrEmpty(s)) return fallback;
            return s.Trim().Equals("WalletPubkey", StringComparison.OrdinalIgnoreCase)
                ? SkinIdentityKeyKind.WalletPubkey
                : s.Trim().Equals("PiUid", StringComparison.OrdinalIgnoreCase)
                    ? SkinIdentityKeyKind.PiUid
                    : fallback;
        }
    }
}

// =============================================================================
// WalletSkinBootstrap — bridges the skin-neutral "Connect Wallet" button to the
// Solana wallet stack (WO-603).
// -----------------------------------------------------------------------------
// The corner auth button lives in DeNelle.Core (PiSignInController). Under the
// Solana/$SKR skin it presents as "Connect Wallet" and raises
// CurrencySkinResolver.WalletConnectRequested — but Core CANNOT reference
// DeNelle.Wallet (assembly direction is Wallet → Core). This bootstrapper is the
// Wallet-side subscriber: it installs at boot ONLY under the SKR skin, drives
// WalletService.Connect(), and (when the skin opts in) binds the connected wallet
// pubkey as the NeonDB identity key.
//
// Under the Pi skin this NEVER subscribes — the Pi sign-in path is untouched
// (zero regression). Follow-up (WO-603 RESULT): a richer connect UI (address /
// disconnect / network badge) is code-built later; WalletConnectDialog is UXML,
// which does not render in WebGL builds (CLAUDE.md §8).
// =============================================================================

using System;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using UnityEngine;
using DeNelle.Core.Auth;
using DeNelle.Core.Diagnostics;
using DeNelle.Core.Backend;       // BackendRequestSigner - the handshake/mint at connect (WO-1441)
using DeNelle.Core.Platform;
using DeNelle.Core.State;

namespace DeNelle.Wallet
{
    /// <summary>Wallet-side subscriber for the SKR skin's Connect Wallet button (WO-603)
    /// + the skin-independent login-surface connect handler (WO-847).</summary>
    public static class WalletSkinBootstrap
    {
        private static WalletService _wallet;

        /// <summary>
        /// The ONE live <see cref="WalletService"/> for the session, or null when nothing has
        /// connected yet. Read-only on purpose: this class owns the instance's lifecycle (create on
        /// connect, clear on disconnect) and every other surface BORROWS it.
        /// <para>
        /// ⛔ WHY THIS EXISTS (2026-08-24, the go-live P0). PackStore held its own
        /// `private WalletService _wallet` and the only way to fill it was
        /// `PackStore.SetWalletService(...)` — a public injector with **ZERO call sites in the whole
        /// project**. So the store's wallet reference was ALWAYS null. That is not a cosmetic gap:
        /// `PurchaseQuoteService.RefreshPricesAsync(null)` fails closed with "no signing wallet", so
        /// the store NEVER requested a quote, and every pack read "Price unavailable" no matter how
        /// connected the player's wallet was. Confirmed against production: zero
        /// /api/purchases/quote requests ever reached the server while the owner's wallet showed
        /// connected on screen.
        /// </para>
        /// <para>
        /// ⚠ The store's OWN copy stays authoritative once set (an explicitly injected service wins),
        /// so this is a FALLBACK adoption, not a second owner.
        /// </para>
        /// </summary>
        public static WalletService ConnectedWallet => _wallet;
        private static bool _connecting;

        /// <summary>Installs the wallet-connect handlers at boot. The LOGIN-surface
        /// handler registers under EVERY skin (WO-847); the corner-button event
        /// subscriber stays SKR-only (WO-603, zero Pi regression).</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            // WO-847: the Android wallet-first LOGIN surface must connect+bind under
            // every skin - identity binding on the login path is never left to the
            // optional skin config (skin.json bindIdentityOnAuth). Registered BEFORE
            // the skin gate so the bridge always has a handler.
            // WO-1583: the login surface's Connect Wallet is a PLAYER TAP, so it is an explicit
            // connect and may mint the backend session. Boot auto-resume reaches the same method
            // with explicitConnect:false and never signs. The lambda is the whole distinction.
            LoginWalletBridge.ConnectHandler = () => ConnectForLoginAsync(explicitConnect: true);
            FlowTrace.Step("Wallet", "login wallet-connect handler registered (LoginWalletBridge, skin-independent).");

            TryAutoResumeAsync().Forget();

            // Pi skin (the live default): leave the corner-button wallet path entirely unwired.
            if (!CurrencySkinResolver.IsSkr) return;

            CurrencySkinResolver.WalletConnectRequested -= OnConnectRequested; // idempotent
            CurrencySkinResolver.WalletConnectRequested += OnConnectRequested;
            CurrencySkinResolver.WalletDisconnectRequested -= OnDisconnectRequested; // idempotent
            CurrencySkinResolver.WalletDisconnectRequested += OnDisconnectRequested;
            FlowTrace.Step("Skin", "SKR skin active — wallet-connect handler installed (WalletSkinBootstrap).");
        }

        /// <summary>
        /// Boot-time silent reconnect. Owner ruling 2026-08-17: *"yes it should auto connect, there
        /// is a menu option to reset"* — a returning player should never be asked to connect again.
        /// </summary>
        /// <remarks>
        /// ⚠ ONLY RUNS WHEN A SEALED SESSION ALREADY EXISTS. `MwaSessionStore.HasStoredSession` is
        /// the gate, and it is not a nicety: without it, a FIRST-TIME player — or anyone who chose
        /// Reset — would have the wallet app launched at them unprompted on every cold start. That
        /// is a far worse first impression than one Connect tap, and it is the exact behaviour the
        /// owner's "menu option to reset" is meant to give back. Reset clears the store
        /// (SolanaWalletProvider.Disconnect → MwaSessionStore.Clear), so the very next boot is
        /// silent again in the other direction: no stored session, no auto-connect, no wallet launch.
        ///
        /// FIRE-AND-FORGET, NEVER AWAITED BY BOOT. The association takes ~2.6s on a real Seeker
        /// (measured 2026-08-17), so awaiting this would stall the title screen for a wallet the
        /// player did not ask for yet. The manual Connect handler is registered BEFORE this starts,
        /// so a player who taps Connect during the attempt is served by the normal path — and
        /// `_connecting` makes the duplicate a no-op rather than a second association.
        ///
        /// FAILURE IS SILENT BY DESIGN. Every outcome lands on "the player taps Connect", which is
        /// exactly today's behaviour, so this can only ever remove a tap and never add a dead end.
        /// It is traced, not surfaced: a boot-time toast about a wallet the player has not asked
        /// about yet is noise.
        /// </remarks>
        private static async UniTaskVoid TryAutoResumeAsync()
        {
            if (!MwaSessionStore.HasStoredSession)
            {
                FlowTrace.Step("Wallet",
                    "auto-resume skipped — no sealed session (first run, or the player chose Reset). " +
                    "The wallet app is deliberately NOT launched; the player taps Connect.");
                return;
            }

            if (_connecting)
            {
                FlowTrace.Step("Wallet", "auto-resume skipped — a connect is already in progress.");
                return;
            }

            FlowTrace.Step("Wallet",
                "auto-resume: sealed session present — attempting a SILENT reconnect at boot " +
                "(no prompt; falls back to the Connect button on any failure).");

            // Explicit try/catch rather than Guard.Try: Guard has no async overload, and a
            // fire-and-forget UniTaskVoid that throws would otherwise surface as an unobserved
            // exception with no context. Caught AND LOGGED — never swallowed (§12).
            // AuthOutcome is a STRUCT (non-nullable), so `default` is the not-set sentinel — its
            // Success is false, which is exactly the "did not connect" branch we want on a throw.
            AuthOutcome outcome = default;
            try
            {
                // ⛔ SUPERSEDED 2026-09-07 (WO-1583). THE RULING BELOW IS HISTORY; BOOT NEVER SIGNS.
                // Owner, verbatim: "everytime i play now im forced to authenticate ... I would think
                // the authentication would only be needed for purchases (and codes)". The 09-06
                // arithmetic below was right that auto-resume adds only ONE sheet - and one sheet on
                // EVERY launch is still the thing she is objecting to. So this call now passes
                // explicitConnect:false and takes the signature-free path
                // (BackendRequestSigner.TryResumeSessionWithoutSigningAsync).
                //
                // ⚠ THE COST IS ACCEPTED AND MUST STAY LEGIBLE: the backend session is memory-only by
                // design, so a cold boot restores no token and a wallet holder has NO cloud save until
                // a purchase, a promo redeem, or an explicit Connect tap mints one. Saves are NOT
                // lost - they queue offline (GameStateService.EnqueueOffline) and drain in one upload
                // the moment a session exists. Buying back cloud-save-at-boot without a sheet needs a
                // SEALED PERSISTED token (the MwaSessionStore AES-GCM shape); that is a separate
                // ruling, deliberately not smuggled in here.
                //
                // ⚠ THE explicitConnect FLAG IS BACK, AND IT IS NOW LIVE STATE. The 09-06 note below
                // deleted it as "a parameter every caller sets identically". Under this ruling the
                // callers DIFFER - boot passes false, the login tap passes true - so the flag is the
                // distinction itself, not dead state. Do not re-collapse it.
                //
                // --- history, kept verbatim because its reasoning is still instructive ---
                // ⭐ OWNER RULING 2026-09-06 (WO-1441): AUTO-RESUME MINTS. ONE HANDSHAKE ON BOOT.
                //
                // ⛔ THIS DELIBERATELY REVERSES WO-1211, WHICH FORBADE MINTING HERE. That rule is not
                // being deleted, because its reasoning was sound and must stay legible: boot runs
                // with no player action, so a SignMessage here is an unasked-for wallet sheet on
                // EVERY launch, and WO-1157's bounce had already found the owner objecting to
                // exactly that ("a Title sheet after CONTINUE"). Nothing about that argument was
                // wrong.
                //
                // WHAT CHANGED IS THE ARITHMETIC, NOT THE PRINCIPLE. WO-1211 was written assuming a
                // mint here would be an EXTRA prompt on top of the connect prompt. On this path it
                // is not: auto-resume reconnects SILENTLY (that is its entire purpose - no connect
                // prompt is ever shown), so the handshake is the FIRST and ONLY wallet sheet of the
                // session. That is ONE prompt, which sits UNDER the owner's own stated shape of
                // "a connect, then the handshake" (2026-08-24) rather than over it.
                //
                // AND THE COST OF NOT MINTING WAS PROVEN, NOT THEORETICAL. With this deferred, an
                // auto-resumed wallet holder had NO backend session for the entire session, so every
                // cloud save was refused fail-closed with why=missing - all day, on the owner's own
                // device (pid 7170, 2026-09-06; WO-1441). WO-1211 traded a prompt for silent total
                // loss of cloud save, which is not the trade it believed it was making.
                //
                // ⚠ SO THE INVARIANT IS NARROWED, NOT ABANDONED: boot may raise AT MOST ONE wallet
                // sheet, and only when a sealed session already auto-resumed. A first-run player
                // still sees nothing - TryAutoResumeAsync returns early above when there is no
                // sealed session, so this line is unreachable for them. Do not widen it further
                // without a ruling.
                //
                // ⚠ NO `explicitConnect` FLAG. The first cut of this fix carried one so auto-resume
                // could opt out of minting; the ruling above means BOTH callers now mint, and a
                // parameter every caller sets identically is dead state that rots (CLAUDE.md §5,
                // §2). The distinction lives in this comment, where it belongs, not in an argument
                // nothing varies.
                // --- end history ---
                outcome = await ConnectForLoginAsync(explicitConnect: false);
            }
            catch (Exception ex)
            {
                FlowTrace.Warn("Wallet",
                    $"auto-resume threw ({ex.GetType().Name}: {ex.Message}) — falling back to the " +
                    "Connect button. Boot is unaffected; this path is fire-and-forget by design.");
                return;
            }

            if (outcome.Success)
                FlowTrace.Step("Wallet", "auto-resume SUCCEEDED — connected at boot with no player action.");
            else
                FlowTrace.Step("Wallet",
                    "auto-resume did not connect — falling back to the Connect button. " +
                    "This is not an error: the stored grant may be revoked or expired.");
        }

        private static void OnConnectRequested() => ConnectAsync().Forget();

        /// <summary>
        /// The RESET the owner ruled on 2026-08-17 ("there is a menu option to reset"), finally wired.
        /// Clears the sealed MWA session via WalletService.Disconnect, so the NEXT cold start does not
        /// auto-resume and the player is asked to Connect again - the documented other direction of
        /// TryAutoResumeAsync's gate.
        /// </summary>
        private static void OnDisconnectRequested() => DisconnectAsync().Forget();

        private static async UniTaskVoid DisconnectAsync()
        {
            // ⚠ Disconnecting MID-CONNECT would race the association and could clear a session that
            // is about to be written. Refuse and say so rather than half-doing it.
            if (_connecting)
            {
                FlowTrace.Warn("Wallet", "Disconnect requested while a connect is in progress - ignored.");
                return;
            }
            if (_wallet == null)
            {
                // Not an error: nothing is connected, and the caller's intent (end up disconnected)
                // is already satisfied. Say it plainly instead of failing.
                FlowTrace.Step("Wallet", "Disconnect requested with no WalletService instance - already disconnected.");
                CurrencySkinResolver.PublishWalletDisconnected();
                return;
            }

            FlowTrace.Step("Wallet", "Disconnect requested - clearing the sealed MWA session (no auto-resume next boot).");
            await _wallet.Disconnect();
            // WalletService.Disconnect already publishes the disconnected state in its finally block;
            // it is NOT repeated here. PublishWalletDisconnected is idempotent, but one owner per fact.
        }

        private static async UniTaskVoid ConnectAsync()
        {
            if (_connecting) { FlowTrace.Step("Wallet", "Connect already in progress — ignoring duplicate request."); return; }
            _connecting = true;
            try
            {
                // Auto-selects SolanaWalletProvider when the Solana Unity SDK is compiled in,
                // else the devnet StubWalletProvider — so this runs end-to-end with or without the SDK.
                if (_wallet == null) _wallet = new WalletService();

                var account = await _wallet.Connect();
                if (!account.IsValid)
                {
                    FlowTrace.Warn("Wallet", "SKR wallet connect cancelled/failed — no identity bound.");
                    return;
                }

                // ⭐ HANDSHAKE AT CONNECT, NOT AT PURCHASE (owner, 2026-08-24). The backend session
                // used to be minted lazily on the first authed call, so the prompts landed
                // 1-at-connect then TWO-at-first-purchase (session mint + payment). The player knows
                // the other shape: connect, then the auth handshake, and later ONE prompt to pay.
                // Same three signatures; this one does not interrupt the purchase.
                //
                // ⚠ AWAITED SO THE TWO WALLET DIALOGS ARE ORDERED - firing them concurrently would
                // stack two prompts on the player at once.
                //
                // ⛔ WO-1441: this used to call WarmUpSessionAsync and the comment claimed
                // "TryAttachSession still mints on demand". FALSE since WO-1157 - only a purchase or
                // a promo redeem mints on demand; cloud SAVE never does. Reaching here means the
                // player TAPPED the corner Connect button, which is an explicit action, so mint.
                try
                {
                    await BackendRequestSigner.MintSessionForExplicitConnectAsync(account.Address);
                }
                catch (Exception warmEx)
                {
                    // Caught AND LOGGED, never swallowed (§12). A failed mint leaves the state we
                    // were already in; the purchase path still mints at the till.
                    FlowTrace.Warn("Wallet",
                        $"session mint threw ({warmEx.GetType().Name}) - connect stands, but cloud SAVE " +
                        "will refuse fail-closed until a session exists. " + warmEx.Message);
                }

                var skin = CurrencySkinResolver.Active;
                if (skin != null && skin.BindIdentityOnAuth)
                {
                    string key = skin.ResolveIdentityKey(null, account.Address);
                    if (!string.IsNullOrEmpty(key))
                    {
                        // Attest ONLY when this really is the connected wallet address from a
                        // real signing provider. A skin may resolve the identity key to
                        // something else entirely (a Pi UID), which must never key a cloud save.
                        bool attested = _wallet.IsRealSigningWallet &&
                                        string.Equals(key, account.Address, StringComparison.Ordinal);
                        GameStateService.Instance?.BindWallet(key, attested);
                        FlowTrace.Step("Skin", $"Bound NeonDB identity key ({skin.IdentityKeyKind}) from wallet connect " +
                                               $"(cloud-attested={attested}).");
                    }
                    else
                    {
                        FlowTrace.Warn("Skin", "Wallet connected but address was empty — identity not bound.");
                    }
                }
            }
            catch (Exception e) { FlowTrace.Fail("Wallet", $"SKR wallet connect threw: {e.Message}"); }
            finally { _connecting = false; }
        }

        // =====================================================================
        //  WO-847 — the login surface's Connect Wallet (Android wallet-first)
        // =====================================================================

        /// <summary>
        /// Connect flow for the LOGIN surface (LoginWalletBridge). Same stack as the
        /// corner-button path (WalletService.Connect - MWA on device, stub in editor)
        /// but the identity bind is EXPLICIT and unconditional: the connected address
        /// keys the save via GameStateService.BindWallet (the :71 precedent), never
        /// gated behind the skin's BindIdentityOnAuth. Resolves the email-path
        /// AuthOutcome shape (UserId = wallet address) so the panel continues
        /// identically to a successful email sign-in.
        /// </summary>
        /// <param name="explicitConnect">
        /// TRUE only when a PLAYER TAPPED Connect on the login surface. FALSE for the boot
        /// auto-resume. WO-1583 (ruling 2026-09-07): only an explicit connect may mint the backend
        /// session, because minting is the one thing here that raises a wallet SignMessage sheet.
        /// </param>
        private static async Task<AuthOutcome> ConnectForLoginAsync(bool explicitConnect)
        {
            if (_connecting)
            {
                FlowTrace.Step("Wallet", "Login connect requested while a connect is in progress - ignoring duplicate.");
                return AuthOutcome.Fail("A wallet connect is already in progress.");
            }
            _connecting = true;
            try
            {
                if (_wallet == null) _wallet = new WalletService();

                var account = await _wallet.Connect();
                if (!account.IsValid)
                {
                    // Tell the player WHICH failure this was. "Connect cancelled" after a
                    // 30s timeout is a lie that sends them looking for the wrong fix -
                    // WalletService.LastConnectError carries the real, actionable reason.
                    string why = string.IsNullOrEmpty(_wallet.LastConnectError)
                        ? "Connect cancelled."
                        : _wallet.LastConnectError;
                    FlowTrace.Warn("Wallet", "Login wallet connect did not complete - no identity bound: " + why);
                    return AuthOutcome.Fail(why);
                }

                var svc = GameStateService.Instance;
                if (svc == null)
                {
                    FlowTrace.Warn("Wallet", "Login connect: GameStateService null - save not re-keyed.");
                }
                else
                {
                    // ATTESTED bind: this address came from a real, key-holding signing
                    // wallet, which is the ONLY thing allowed to key a cloud save. The
                    // devnet stub reports false here, so an SDK-less build can never point
                    // every tester at one shared player_data row.
                    bool attested = _wallet.IsRealSigningWallet;
                    Guard.Try("Wallet", "BindWallet(wallet address, login path)",
                        () => svc.BindWallet(account.Address, attested));
                    FlowTrace.Step("Wallet", $"Login connect bound save identity to wallet {account.ShortAddress} " +
                                             $"(cloud-attested={attested}).");
                }

                // ⛔ THE HANDSHAKE BELONGS ON *THIS* PATH, NOT ONLY ON ConnectAsync (fixed
                // 2026-08-24, second pass). The first pass put the session warm-up in ConnectAsync -
                // the SKR corner-button route - and the owner reported she still never authenticated.
                // The device trace said why:
                //     [Flow:Wallet] auto-resume SUCCEEDED - connected at boot with no player action.
                // Auto-resume (TryAutoResumeAsync) does NOT go through ConnectAsync; it comes through
                // HERE. So a returning player - the common case, and the case the owner is in - got a
                // silent reconnect and no handshake at all, exactly as before the fix.
                //
                // ⚠ THIS IS THE SHARED PATH: auto-resume AND the login surface both land here, which
                // is exactly WHY the explicitConnect flag exists (WO-1583) - the two callers want
                // different things from the same body. The mint is idempotent (a usable session makes
                // it a no-op), so the copy in ConnectAsync is harmless rather than a second owner.
                //
                // ⛔ WO-1441 — WARMING UP WAS NEVER ENOUGH, AND SAYING IT WAS COST A DAY OF SAVES.
                // This called WarmUpSessionAsync and the comment claimed "the first authed call mints
                // on demand". FALSE since the WO-1157 fail-bounce: BackendRequestSigner mints only
                // when allowMint is set, which /api/game/save never sets. So this branch warmed up,
                // found nothing, minted nothing, and every cloud save for the rest of the session was
                // refused fail-closed with why=missing. Proven on device (pid 7170, 2026-09-06):
                // connect OK at 12:50:06.956, warm-up deferred at .960, first why=missing at 12:50:11.556,
                // and "MintSessionAsync" appears ZERO times in 76 MB of that day's captures.
                //
                // ⛔ NO LONGER UNCONDITIONAL - WO-1583, owner ruling 2026-09-07. The 09-06 ruling
                // recorded at TryAutoResumeAsync minted here on BOTH callers, which charged the
                // player a wallet sheet on every launch. Only an explicit tap mints now; boot takes
                // the signature-free path and, when there is nothing to reuse or renew, simply says
                // so and lets cloud saves queue offline.
                try
                {
                    if (explicitConnect)
                        await BackendRequestSigner.MintSessionForExplicitConnectAsync(account.Address);
                    else
                        await BackendRequestSigner.TryResumeSessionWithoutSigningAsync(account.Address);
                }
                catch (Exception warmEx)
                {
                    // Caught AND LOGGED, never swallowed (§12). Correctness is unaffected: a failed
                    // mint leaves exactly the state we were already in, and the purchase path still
                    // mints on demand at the till.
                    FlowTrace.Warn("Wallet",
                        $"session {(explicitConnect ? "mint" : "resume")} threw on the login path " +
                        $"({warmEx.GetType().Name}) - connect itself stands, but cloud SAVE will refuse " +
                        "fail-closed and queue offline until a session exists. " + warmEx.Message);
                }

                return new AuthOutcome { Success = true, UserId = account.Address, Email = string.Empty, Error = string.Empty };
            }
            catch (Exception e)
            {
                FlowTrace.Fail("Wallet", $"Login wallet connect threw: {e.Message}");
                return AuthOutcome.Fail("Wallet connect failed. Please try again.");
            }
            finally { _connecting = false; }
        }
    }
}

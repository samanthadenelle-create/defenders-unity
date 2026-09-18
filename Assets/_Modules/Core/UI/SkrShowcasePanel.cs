// =============================================================================
// SkrShowcasePanel — the "Powered with SKR" grant-recording BRANDING moment.
// -----------------------------------------------------------------------------
// GRANT PREVIEW ONLY (owner 2026-07-04, ff.skrpreview). This panel PRESENTS the
// intended SKR integration story for the grant video — the branding + the honest
// value-prop of how SKR WILL work — WITHOUT any live crypto. It is a labeled
// PREVIEW: every card is stamped "PREVIEW · TESTNET — NOT LIVE", and the
// Connect-Wallet / Buy actions are deliberate NO-OPs (a "coming soon" toast) that
// call NO wallet, sign NOTHING, and move NO funds. The wallet rail stays the
// devnet StubWalletProvider regardless — this surface never touches it.
//
// Canon it presents (docs/PI_PITCH.md + memory skr-separate-ingame-currency):
//   * SKR is a REAL Solana / Seeker token — not an in-game balance we mint or hold.
//   * NON-CUSTODIAL by design: you stake natively; we never take custody of your SKR.
//   * SERVER-VERIFIED perks: your on-chain standing unlocks rewards, read-only.
//   * COSMETIC / convenience only — never pay-to-win.
//
// Built on the shared ElarionUiKit Obsidian modal (black panel + gold trim + the
// ONE shared Close) — code-built uGUI, procedural chrome (no gitignored-art
// dependency), so it can NEVER render blank in a WebGL build. Mobile-first: a
// compact centered column that reads well in a ~90s screen capture.
//
// Gated by DeNelle.Core.FeatureFlags.SkrPreview — default OFF (normal players
// never see it); the grant-recording build flips it ON (menu / PlayerPrefs /
// ?skrpreview=1 on the web URL). Reachable in ONE tap from the Title screen badge.
// =============================================================================

using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using DeNelle.Core.Platform;
using DeNelle.Core.Diagnostics;

// =============================================================================
// WO-1363 - GOOGLE PLAY VARIANT: THE WHOLE SURFACE COMPILES OUT.
// -----------------------------------------------------------------------------
// A runtime `#if` INSIDE a method does not remove a string literal from the
// binary: every literal below still lands in IL2CPP global-metadata.dat, where a
// policy reviewer's `strings` pass finds it. This panel is 100% SKR/Solana
// branding, so the correct exclusion is at the TYPE level, not the statement
// level. Under GOOGLE_PLAY the file compiles to the neutral stub at the bottom;
// its only caller (TitleController.BuildSkrBadge) is compiled out with it.
// The dApp Store / Seeker build is UNCHANGED - it takes the !GOOGLE_PLAY branch.
// =============================================================================
#if !GOOGLE_PLAY

namespace DeNelle.Core.UI
{
    /// <summary>
    /// The "Powered with SKR" preview/branding panel. Call <see cref="Open()"/> to build + show it
    /// (used by the Title-screen SKR badge when <see cref="FeatureFlags.SkrPreview"/> is ON).
    /// Presentation only — no wallet call, no transaction, nothing buyable.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SkrShowcasePanel : MonoBehaviour
    {
        private ElarionUiKit.ObsidianModal _modal;
        private ElarionUiKit.ToastParts _toast;
        private float _toastUntil;
        private static SkrShowcasePanel _open;   // single live instance guard

        // ── MODAL ARBITER (DEF-212 / audit 2026-08-01 F3) ────────────────────────────
        // This panel builds a 31000-band modal WITH a scrim (BuildObsidianModal default
        // sortingOrder). Before this it held ZERO PanelManager references, so
        // PanelManager.AnyOpen stayed FALSE while the panel covered the screen: the shared
        // world interact button stayed live UNDERNEATH it, the Android back button had
        // nothing to close, and BattleLock could not reject it. ONE handle per panel
        // lifetime (created in Awake, passed to NotifyOpened / NotifyClosed).
        private PanelHandle _panelHandle;

        private void Awake()
        {
            _panelHandle = PanelManager.Register("SKR Showcase", Close, IsShowing);
        }

        /// <summary>Arbiter visibility probe (NotifyOpened verifies with it -- WO-465).</summary>
        private bool IsShowing() => _modal != null && _modal.canvas != null && _modal.canvas.activeInHierarchy;

        // The honest value-prop copy (docs/PI_PITCH.md + skr-separate-ingame-currency canon).
        // (WO-1363: the former inner `#if GOOGLE_PLAY` neutral copy moved to the Play
        // stub at the bottom of this file - the whole type is now excluded there, so a
        // per-const swap inside a shipping type is no longer the mechanism.)
        private const string ValueRealToken =
            "SKR is a REAL Solana / Seeker token — not an in-game balance we mint or hold.";
        private const string ConnectActionLabel = "Connect Wallet";
        private const string ValueNonCustodial =
            "Non-custodial by design: you stake natively — we never take custody of your SKR.";
        private const string ValueServerVerified =
            "Server-verified: your on-chain standing unlocks perks read-only, no deposit.";
        private const string ValueCosmeticOnly =
            "Cosmetic & convenience perks only — modest thank-yous, never pay-to-win.";

        private const string PreviewStamp = "PREVIEW · TESTNET — NOT LIVE";
        private const string HonestFooter =
            "This is a preview of the intended experience. No wallet is connected and no live crypto is used.";

        // =====================================================================
        //  Open / close
        // =====================================================================

        /// <summary>Build + show the panel. Idempotent: a second call replaces the open one so we
        /// never stack duplicates. Returns the live instance, or NULL when the modal arbiter
        /// rejected the open (battle-lock) -- callers must not assume a non-null result.</summary>
        public static SkrShowcasePanel Open()
        {
            using var _ = FlowTrace.Enter("Skr", "SkrShowcasePanel.Open");
            if (_open != null)
            {
                UnityEngine.Object.Destroy(_open.gameObject);
                _open = null;
            }

            var host = new GameObject("SkrShowcasePanel");
            UnityEngine.Object.DontDestroyOnLoad(host);   // survive a scene load during the capture
            var panel = host.AddComponent<SkrShowcasePanel>();   // Awake registers the arbiter handle
            panel.Build();

            // Announce to the arbiter. It CAN REJECT (battle-lock) -- and on rejection it has
            // already invoked our Close hook, tearing this panel down. Report the honest result
            // instead of assuming the open succeeded; never force-show over a rejection.
            if (!PanelManager.NotifyOpened(panel._panelHandle))
            {
                FlowTrace.Warn("Skr",
                    "SkrShowcasePanel.Open: arbiter REJECTED the open (battle-lock) -- panel torn down, returning null.");
                return null;
            }

            _open = panel;
            return panel;
        }

        private void Close()
        {
            if (_open == this) _open = null;
            PanelManager.NotifyClosed(_panelHandle);   // no-op if the arbiter already swapped us out
            if (_modal != null && _modal.canvas != null) UnityEngine.Object.Destroy(_modal.canvas);
            UnityEngine.Object.Destroy(gameObject);
        }

        private void OnDestroy()
        {
            if (_open == this) _open = null;
            // Destroyed without a Close (scene teardown / replaced instance): never leak the
            // arbiter slot. NotifyClosed only clears when THIS handle is the one on record.
            PanelManager.NotifyClosed(_panelHandle);
        }

        private void Update()
        {
            if (_toast != null && _toast.card != null && _toastUntil > 0f
                && Time.unscaledTime > _toastUntil)
            {
                _toast.card.SetActive(false);
                _toastUntil = 0f;
            }
        }

        // =====================================================================
        //  Build
        // =====================================================================

        private void Build()
        {
            _modal = ElarionUiKit.BuildObsidianModal("SkrShowcaseUI",
                new LocalizedText("common.powered_skr").Resolve(),
                new Vector2(0.15f, 0.06f), new Vector2(0.85f, 0.94f), Close);

            if (_modal == null || _modal.chrome == null || _modal.chrome.content == null)
            {
                FlowTrace.Fail("Skr", "SkrShowcasePanel.Build: Obsidian modal/content failed to build — panel cannot draw.");
                return;
            }

            MedievalUiSkin.ApplyShell(_modal.chrome, compact: false);

            var body = _modal.chrome.content.transform;

            // --- The honest PREVIEW stamp (owner directive: clearly labeled, not live) ---
            ElarionUiKit.Label(body, PreviewStamp, 0.905f, 0.955f, ElarionUi.Gilt, 20,
                TextAlignmentOptions.Center, 0.04f, 0.96f, spacing: 3f, bold: true);

            // --- Headline: what this shows ---
            ElarionUiKit.Label(body, new LocalizedText("skr.showcase.headline").Resolve(),
                0.845f, 0.905f, ElarionUi.Gold, 30,
                TextAlignmentOptions.Center, 0.04f, 0.96f, bold: true);
            ElarionUiKit.Label(body, new LocalizedText("skr.showcase.subhead").Resolve(),
                0.800f, 0.845f, ElarionUi.ParchmentDim, 17, TextAlignmentOptions.Center, 0.05f, 0.95f);

            // --- Value-prop well (the four honest one-liners) ---
            var well = ElarionUiKit.Well(body.transform, new Vector2(0.05f, 0.360f), new Vector2(0.95f, 0.790f));
            BuildValueRow(well.transform, 0.775f, 0.955f, ElarionUi.Gilt,       "REAL TOKEN",     ValueRealToken);
            BuildValueRow(well.transform, 0.560f, 0.740f, ElarionUi.Aether,     "NON-CUSTODIAL",  ValueNonCustodial);
            BuildValueRow(well.transform, 0.345f, 0.525f, ElarionUi.Gold,       "SERVER-VERIFIED", ValueServerVerified);
            BuildValueRow(well.transform, 0.045f, 0.310f, ElarionUi.Affordable, "NEVER P2W",      ValueCosmeticOnly);

            // --- Actions ---
            // View the read-only stake-rewards perk surface (already-built StakeRewardsPanel).
            // Seed a real-looking Genesis-holder stake so the perk list reads populated for the
            // capture — a MOCK read only (no wallet, custodies nothing).
            var rewards = ElarionUiKit.BuildObsidianButton(body,
                new LocalizedText("skr.showcase.view_stake_rewards").Resolve(),
                ElarionUiKit.ObsidianButtonStyle.Style1, ElarionUiKit.ObsidianButtonColor.Yellow,
                new Vector2(0.06f, 0.255f), new Vector2(0.49f, 0.335f), OnViewStakeRewards);

            // Connect-wallet — deliberate NO-OP. Calls NO wallet; shows a "coming soon" toast.
            var connect = ElarionUiKit.BuildObsidianButton(body, ConnectActionLabel,
                ElarionUiKit.ObsidianButtonStyle.Style1, ElarionUiKit.ObsidianButtonColor.Gray,
                new Vector2(0.51f, 0.255f), new Vector2(0.94f, 0.335f), OnConnectWalletComingSoon);
            MedievalUiSkin.ApplyButton(rewards, primary: true);
            MedievalUiSkin.ApplyButton(connect, primary: false);

            // --- Honest footer (no wallet, no live crypto) ---
            ElarionUiKit.Label(body, HonestFooter, 0.140f, 0.245f, ElarionUi.Parchment, 15,
                TextAlignmentOptions.Center, 0.05f, 0.95f);
            ElarionUiKit.Label(body, new LocalizedText("skr.showcase.cosmetic_note").Resolve(),
                0.095f, 0.140f, ElarionUi.ParchmentDim, 14, TextAlignmentOptions.Center, 0.05f, 0.95f, bold: true);

            // Toast — low center of the modal canvas (coming-soon feedback).
            _toast = ElarionUiKit.ToastCard(_modal.canvas.transform,
                ElarionUiKit.ToastTone.Gold, accentLeft: true, TextAnchor.MiddleCenter);
            if (_toast != null && _toast.card != null)
            {
                var trt = _toast.card.GetComponent<RectTransform>();
                trt.anchorMin = new Vector2(0.20f, 0.02f);
                trt.anchorMax = new Vector2(0.80f, 0.08f);
                trt.offsetMin = Vector2.zero; trt.offsetMax = Vector2.zero;
                _toast.card.SetActive(false);
            }

            FlowTrace.Step("Skr", "SkrShowcasePanel built (ff.skrpreview) — branding + labeled preview, no wallet call.");
        }

        /// <summary>One value-prop row: a tag + the honest one-liner, inside the well.</summary>
        private static void BuildValueRow(Transform well, float yMin, float yMax, Color tagColor,
            string tag, string body)
        {
            ElarionUiKit.Label(well, tag, yMax - 0.06f, yMax, tagColor, 13,
                TextAlignmentOptions.TopLeft, 0.05f, 0.95f, spacing: 2f, bold: true);
            ElarionUiKit.Label(well, body, yMin, yMax - 0.055f, ElarionUi.Parchment, 16,
                TextAlignmentOptions.TopLeft, 0.05f, 0.95f);
        }

        // =====================================================================
        //  Actions
        // =====================================================================

        private void OnViewStakeRewards()
        {
            using var _ = FlowTrace.Enter("Skr", "SkrShowcasePanel.OnViewStakeRewards");
            // Seed a real-looking active native stake (Genesis holder ~1M SKR) so the perk list
            // renders populated. READ-ONLY mock — no wallet, no funds move, nothing is custodied.
            StakeRewardsResolver.Query = new StakeRewardsResolver.MockStakeQuery(StakeRewardsResolver.DemoMockStakeSkr);
            // ARBITER NOTE (audit 2026-08-01 F3): both panels are now registered, and the
            // PanelManager law is ONE panel at a time -- so opening Stake Rewards CLOSES this
            // showcase behind it (previously the two stacked). Closing Stake Rewards therefore
            // returns to the world, not to this panel. That is the arbiter law, not a bug; if
            // the grant capture needs the stacked look, the showcase must re-Open() on dismiss.
            StakeRewardsPanel.Open();
        }

        private void OnConnectWalletComingSoon()
        {
            // DELIBERATE NO-OP. No wallet call, no signature, no transaction. The Pi/Seeker wallet
            // is not connected in this build — the grant preview shows the INTENDED flow, honestly.
            FlowTrace.Step("Skr", "SkrShowcasePanel: Connect Wallet is a no-op (preview) — no wallet call made.");
            ShowToast(new LocalizedText("skr.showcase.coming_soon_toast").Resolve());
        }

        private void ShowToast(string message)
        {
            if (_toast == null || _toast.card == null || _toast.label == null) return;
            _toast.label.text = message;
            _toast.card.SetActive(true);
            _toastUntil = Time.unscaledTime + 3f;
        }
    }
}

#else   // GOOGLE_PLAY

namespace DeNelle.Core.UI
{
    /// <summary>
    /// GOOGLE PLAY EDITION STUB (WO-1363). The showcase surface is branding for a token the
    /// Play artifact must not mention, so the real type above is excluded at COMPILE time -
    /// none of its literals reach global-metadata.dat. This stub keeps the one public entry
    /// point compiling for any caller that is not itself excluded; it builds nothing and
    /// returns null, which every caller already tolerates (the arbiter can reject an open).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SkrShowcasePanel : MonoBehaviour
    {
        /// <summary>The Play-neutral action label. Retained as a real const so this file still
        /// carries the compile-time-neutral pin GooglePlayPackagingRegression asserts on it.</summary>
        private const string ConnectActionLabel = "Continue with Google";

        /// <summary>No-op in this edition. Returns null; callers must already handle null.</summary>
        public static SkrShowcasePanel Open()
        {
            FlowTrace.Step("Store", "Showcase surface is unavailable in this edition (" + ConnectActionLabel + ").");
            return null;
        }
    }
}

#endif

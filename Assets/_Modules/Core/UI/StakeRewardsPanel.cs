// =============================================================================
// StakeRewardsPanel — the Seekerthon "stake -> in-game rewards" display surface.
// -----------------------------------------------------------------------------
// PRESENTATION ONLY. Reads DeNelle.Core.Platform.StakeRewardsResolver (a read-only
// standing: active stake -> tier -> unlocked rewards) and renders it. It mutates NO
// game state, holds NO SKR, and never triggers a transfer — it just SHOWS the perks
// the player's ACTIVE NATIVE stake unlocks. The whole message the panel conveys:
// "active stake gives you in-game rewards, automatically, just by staking natively -
// no deposit, we never hold your SKR."
//
// Built on the shared ElarionUiKit Obsidian modal (black panel + gold trim + the ONE
// shared Close) — code-built uGUI only, procedural chrome path (frameName null) so it
// has NO gitignored-art dependency and can NEVER render blank in a WebGL build. Mobile-
// first: a compact centered column that reads well in a ~90s screen capture.
// =============================================================================

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using DeNelle.Core.Platform;
using DeNelle.Core.UI.Mvvm;
using DeNelle.Core.Diagnostics;

namespace DeNelle.Core.UI
{
    /// <summary>
    /// The read-only stake-rewards panel. DUMB SKIN (MVVM, Silo F): it binds a
    /// <see cref="StakeRewardsVM"/> and renders its projected strings + reward rows — the
    /// StakeRewardsResolver.Resolve() call + every StakeStanding read live in the VM.
    /// Call <see cref="Open()"/> to build + show it (used by the Seekerthon demo bootstrap).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class StakeRewardsPanel : MonoBehaviour
    {
        private ElarionUiKit.ObsidianModal _modal;
        private static StakeRewardsPanel _open;   // single live instance guard

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
            _panelHandle = PanelManager.Register("Stake Rewards", Close, IsShowing);
        }

        /// <summary>Arbiter visibility probe (NotifyOpened verifies with it -- WO-465).</summary>
        private bool IsShowing() => _modal != null && _modal.canvas != null && _modal.canvas.activeInHierarchy;

        // Copy that carries the non-custodial / native-staking message, reused for the capture.
#if GOOGLE_PLAY
        private const string StakeNativeLine = "Store rewards are unavailable in this edition.";
#else
        private const string StakeNativeLine =
            "Stake natively at Stake.solanamobile - rewards apply automatically. We never hold your SKR.";
#endif

        // WO-1363: the empty-state line is a COMPILE-TIME const per channel. It used to be an
        // inline literal at the call site, so "Stake SKR natively..." reached global-metadata.dat
        // in the Play artifact even though the surface is unreachable there.
#if GOOGLE_PLAY
        private const string NoRewardsYetLine = "No rewards are available in this edition.";
#else
        private const string NoRewardsYetLine = "Stake SKR natively to unlock your first reward.";
#endif
        private const string SmallThankYouLine =
            "A small thank-you for staking - modest perks, never pay-to-win.";

        // =====================================================================
        //  Open / close
        // =====================================================================

        /// <summary>Build + show the panel for the CURRENT resolver query (reads it once via the VM).
        /// Idempotent: a second call re-opens a fresh panel. Returns the live instance, or NULL
        /// when the modal arbiter rejected the open (battle-lock).</summary>
        public static StakeRewardsPanel Open()
        {
            return OpenWith(StakeRewardsVM.CreateDefault());
        }

        /// <summary>Build + show the panel for an explicit standing (tests / a seeded demo value).</summary>
        public static StakeRewardsPanel Open(StakeStanding standing)
        {
            return OpenWith(standing != null ? new StakeRewardsVM(standing) : StakeRewardsVM.CreateUnstaked());
        }

        private static StakeRewardsPanel OpenWith(StakeRewardsVM vm)
        {
            using var _ = FlowTrace.Enter("Stake", "StakeRewardsPanel.Open");
            if (_open != null)
            {
                // Replace an already-open panel so we never stack duplicates on-screen.
                Object.Destroy(_open.gameObject);
                _open = null;
            }

            var host = new GameObject("StakeRewardsPanel");
            Object.DontDestroyOnLoad(host);   // survive the boot->hub scene load for the capture
            var panel = host.AddComponent<StakeRewardsPanel>();   // Awake registers the arbiter handle
            panel.Build(vm);

            // Announce to the arbiter. It CAN REJECT (battle-lock) -- and on rejection it has
            // already invoked our Close hook, tearing this panel down. Report the honest result
            // instead of assuming the open succeeded; never force-show over a rejection.
            if (!PanelManager.NotifyOpened(panel._panelHandle))
            {
                FlowTrace.Warn("Stake",
                    "StakeRewardsPanel.Open: arbiter REJECTED the open (battle-lock) -- panel torn down, returning null.");
                return null;
            }

            _open = panel;
            return panel;
        }

        private void Close()
        {
            if (_open == this) _open = null;
            PanelManager.NotifyClosed(_panelHandle);   // no-op if the arbiter already swapped us out
            if (_modal != null && _modal.canvas != null) Object.Destroy(_modal.canvas);
            Object.Destroy(gameObject);
        }

        private void OnDestroy()
        {
            if (_open == this) _open = null;
            // Destroyed without a Close (scene teardown / replaced instance): never leak the
            // arbiter slot. NotifyClosed only clears when THIS handle is the one on record.
            PanelManager.NotifyClosed(_panelHandle);
        }

        // =====================================================================
        //  Build
        // =====================================================================

        private void Build(StakeRewardsVM vm)
        {
            _modal = ElarionUiKit.BuildObsidianModal("StakeRewardsUI",
                new LocalizedText("common.stake_rewards").Resolve(),
                new Vector2(0.16f, 0.10f), new Vector2(0.84f, 0.92f), Close);

            if (_modal == null || _modal.chrome == null || _modal.chrome.content == null)
            {
                // Without a content root nothing can draw — surface it loudly (never a silent blank panel).
                FlowTrace.Fail("Stake", "StakeRewardsPanel.Build: Obsidian modal/content failed to build — panel cannot draw.");
                return;
            }

            MedievalUiSkin.ApplyShell(_modal.chrome, compact: false);

            var body = _modal.chrome.content.transform;

            // --- Active stake (the headline number) ---
            ElarionUiKit.Label(body, vm.ActiveStakeText, 0.855f, 0.925f, ElarionUi.Gilt, 34,
                TextAlignmentOptions.Center, 0.04f, 0.96f, spacing: 1f, bold: true);

            // --- Tier line ---
            if (vm.HasTier)
            {
                ElarionUiKit.Label(body, LocalText.Format("stake.rewards.tier_line", vm.TierName),
                    0.795f, 0.855f,
                    ElarionUi.Gold, 26, TextAlignmentOptions.Center, 0.04f, 0.96f, bold: true);
                if (!string.IsNullOrEmpty(vm.TierTagline))
                    ElarionUiKit.Label(body, vm.TierTagline, 0.752f, 0.795f,
                        ElarionUi.ParchmentDim, 18, TextAlignmentOptions.Center, 0.06f, 0.94f);
            }
            else
            {
                ElarionUiKit.Label(body, new LocalizedText("stake.rewards.no_active_stake").Resolve(),
                    0.795f, 0.855f,
                    ElarionUi.ParchmentDim, 24, TextAlignmentOptions.Center, 0.04f, 0.96f, bold: true);
            }

            // --- "Rewards Unlocked" header ---
            ElarionUiKit.Label(body, new LocalizedText("stake.rewards.unlocked_heading").Resolve(),
                0.695f, 0.748f, ElarionUi.Gilt, 22,
                TextAlignmentOptions.Center, 0.06f, 0.94f, bold: true);

            // --- The unlocked reward list (scrollable well) ---
            var well = ElarionUiKit.Well(body.transform, new Vector2(0.06f, 0.235f), new Vector2(0.94f, 0.688f));
            BuildRewardList(well.transform, vm);

            // --- The message: native staking, automatic, non-custodial ---
            ElarionUiKit.Label(body, StakeNativeLine, 0.150f, 0.225f, ElarionUi.Parchment, 18,
                TextAlignmentOptions.Center, 0.05f, 0.95f);
            ElarionUiKit.Label(body, SmallThankYouLine, 0.110f, 0.150f, ElarionUi.Gold, 16,
                TextAlignmentOptions.Center, 0.05f, 0.95f, bold: true);

            FlowTrace.Step("Stake",
                $"StakeRewardsPanel built: {vm.ActiveStakeText}, " +
                $"tier='{vm.TierName ?? "(none)"}', rewards={vm.Rewards.Count}.");
        }

        private void BuildRewardList(Transform well, StakeRewardsVM vm)
        {
            IReadOnlyList<StakeRewardRowVM> rewards = vm != null ? vm.Rewards : null;

            if (rewards == null || rewards.Count == 0)
            {
                ElarionUiKit.Label(well, NoRewardsYetLine, 0.40f, 0.60f,
                    ElarionUi.ParchmentDim, 18, TextAlignmentOptions.Center, 0.06f, 0.94f);
                return;
            }

            // A scroll column so a long unlock list stays reachable (mobile-safe). Cards are laid out
            // by a VerticalLayoutGroup; the ContentSizeFitter grows the content so it scrolls.
            var scrollGo = new GameObject("Scroll", typeof(RectTransform), typeof(ScrollRect), typeof(RectMask2D), typeof(Image));
            scrollGo.transform.SetParent(well, false);
            var srt = scrollGo.GetComponent<RectTransform>();
            srt.anchorMin = Vector2.zero; srt.anchorMax = Vector2.one;
            srt.offsetMin = Vector2.zero; srt.offsetMax = Vector2.zero;
            scrollGo.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.001f); // near-invisible raycast surface

            var contentGo = new GameObject("Content", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            contentGo.transform.SetParent(scrollGo.transform, false);
            var crt = contentGo.GetComponent<RectTransform>();
            crt.anchorMin = new Vector2(0f, 1f); crt.anchorMax = Vector2.one;
            crt.pivot = new Vector2(0.5f, 1f);
            crt.offsetMin = Vector2.zero; crt.offsetMax = Vector2.zero;
            var vlg = contentGo.GetComponent<VerticalLayoutGroup>();
            vlg.spacing = 8f;
            vlg.padding = new RectOffset(10, 10, 10, 10);
            vlg.childControlHeight = false;
            vlg.childControlWidth = true;
            vlg.childForceExpandHeight = false;
            vlg.childForceExpandWidth = true;
            contentGo.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var scroll = scrollGo.GetComponent<ScrollRect>();
            scroll.content = crt;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 24f;

            for (int i = 0; i < rewards.Count; i++)
                BuildRewardRow(contentGo.transform, rewards[i]);
        }

        private void BuildRewardRow(Transform parent, StakeRewardRowVM reward)
        {
            var rowGo = new GameObject("reward-" + reward.Label, typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            rowGo.transform.SetParent(parent, false);
            rowGo.GetComponent<LayoutElement>().preferredHeight = 78f;
            rowGo.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.30f);
            var row = rowGo.transform;

            // A small tone chip on the left marks the reward kind (badge/title/cosmetic/trickle).
            var chip = ElarionUiKit.AddImage(row, "KindChip",
                new Vector2(0.015f, 0.22f), new Vector2(0.045f, 0.78f), KindColor(reward.Kind), rounded: false);
            var chipImg = chip.GetComponent<Image>();
            if (chipImg != null) chipImg.raycastTarget = false;

            // Kind tag (tiny), reward label (bold), and the small-print detail.
            ElarionUiKit.Label(row, reward.KindTag, 0.58f, 0.96f, ElarionUi.ParchmentDim, 13,
                TextAlignmentOptions.TopLeft, 0.075f, 0.55f);
            ElarionUiKit.Label(row, reward.Label, 0.50f, 0.96f, ElarionUi.Parchment, 18,
                TextAlignmentOptions.TopLeft, 0.075f, 0.98f, bold: true);
            ElarionUiKit.Label(row, reward.Detail, 0.06f, 0.50f, ElarionUi.ParchmentDim, 14,
                TextAlignmentOptions.TopLeft, 0.075f, 0.98f);
        }

        private static Color KindColor(StakeRewardKind kind)
        {
            switch (kind)
            {
                case StakeRewardKind.Badge:    return ElarionUi.Gold;
                case StakeRewardKind.Title:    return ElarionUi.Gilt;
                case StakeRewardKind.Cosmetic: return ElarionUi.Aether;
                case StakeRewardKind.Trickle:  return ElarionUi.Affordable;
                default:                       return ElarionUi.ParchmentDim;
            }
        }
    }
}

using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using DeNelle.Core;
using DeNelle.Core.Catalog;
using DeNelle.Core.Diagnostics;
using DeNelle.Core.Platform;
using DeNelle.Core.State;
using DeNelle.Core.UI;

namespace DeNelle.Village.Crafting
{
    /// <summary>One-time return-home discovery, driven by dungeon-earned history and completed by
    /// the first real rough-stone polish action. Current balance is deliberately irrelevant.</summary>
    // =========================================================================
    //  WO-1600 - WHERE this card is allowed to appear, and WHEN it must leave.
    // -------------------------------------------------------------------------
    //  CAPTURED DEFECT (owner device, build 2026.09.07.359651,
    //  Logs/device/seeker-shots/Screenshot_20260907-132324.png): the JEWELER
    //  DISCOVERED card was drawn over the TITLE screen, on top of
    //  CONTINUE / START NEW / PLAY INTRO.
    //
    //  ⛔ IT WAS NOT A RESET LEAK, AND THE TICKET'S PREMISE IS CORRECTED HERE.
    //  PRIMARY EVIDENCE (the frame itself, read this session): CONTINUE /
    //  START NEW / PLAY INTRO are all still on screen behind the card. The card
    //  was therefore raised ON THE TITLE, before any of them was pressed - at
    //  boot, reading the PREVIOUS SAVE that had just been loaded. There was
    //  nothing for a reset to have cleared yet.
    //  (WO-1600 also reports a 13:23:45 `[Flow:Onboarding] OnStartNew` line
    //  against the 13:23:24 frame, i.e. the card preceded START NEW by ~21 s.
    //  That ordering is consistent, but it is the TICKET'S log read - the
    //  session log is not in Logs/device/ on this tree, so it is cited, not
    //  claimed. The buttons in the frame carry the finding on their own.)
    //
    //  THE CARRIER, named so nobody re-hunts it: `JewelerProgression.IsUnlocked`
    //  is `GameState.HasEverAcquired(DungeonExclusiveItems.RoughStoneId)` -
    //  i.e. the `EverAcquiredItemIds` list (VillageInventory.cs:203-211).
    //  `Completed` is `SeenTutorials[CompletionKey]`. ResetToNewGame ALREADY
    //  clears both (GameStateService.cs - `s.SeenTutorials = new ...` and
    //  `s.EverAcquiredItemIds = new List<string>()`), which is why NOTHING was
    //  added there; ResetToNewGameFullClearRegression now pins that those two
    //  fresh-empty assignments stay.
    //
    //  So this file owns BOTH halves of the defect:
    //   (1) THE SCENE GATE. TryPresent excluded only scene names containing
    //       "Dungeon". The Title, HeroSelect and every raid target passed. The
    //       gate is now positive - a HOME HUB with a chosen hero - because an
    //       exclusion list can only ever be as long as the last bug.
    //   (2) THE STANDING CARD. This component is DontDestroyOnLoad, and nothing
    //       ever dismissed an OPEN card. A card raised on the Title therefore
    //       rode START NEW through HeroSelect into a brand-new town. TryPresent
    //       now CLOSES when the gate stops holding, and the reset itself is
    //       heard directly through GameStateService.NewGameStarted.
    //
    //  LAYOUT (same frame, third defect): the primary verb sat on top of a
    //  CLOSE. That CLOSE is this modal's OWN - BuildObsidianPanel's procedural
    //  path seats the shared Close on `chrome.content` at the default bottom
    //  band, growing UP as a fixed 360x120 box (ElarionUiKit.cs), and this card
    //  laid its own 0.10-0.29 fractions on that SAME `chrome.content`. The kit
    //  names that class in its own comments: "screens laying custom fractions
    //  directly on chrome.content remain the unprotected legacy class". The
    //  copy and the verb now live inside `chrome.layout.body`, the zone whose
    //  bottom edge WO-714 P6 already raises above the Close band - so the
    //  clearance is computed by the kit, not re-derived here.
    //  `overflowMode = Overflow` is also gone; it was DEAD (FitBlock overwrites
    //  it with Truncate on the very next line) and only ever misdirected the
    //  reader about what spilled.
    // =========================================================================
    public sealed class JewelerDiscoveryFtue : MonoBehaviour
    {
        public const string CompletionKey = "ftue.jeweler.first_polish";
        public const string GuidanceHighlightId = "world.jeweler";
        private static JewelerDiscoveryFtue _instance;
        private ElarionUiKit.ObsidianModal _modal;
        private TMP_Text _body;
        private TMP_Text _stakeStatus;
        private PanelHandle _panel;
        private WorldHold.Handle _hold;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (_instance != null) { _instance.TryPresent(); return; }
            var go = new GameObject("[JewelerDiscoveryFtue]");
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<JewelerDiscoveryFtue>();
        }

        private void OnEnable()
        {
            TutorialHighlightRegistry.RegisterResolver(GuidanceHighlightId,
                () => new HighlightTarget(ResolveJewelerTarget()));
            SceneManager.sceneLoaded += OnSceneLoaded;
            JewelPolishService.FirstPolishActionStarted += Complete;
            StakeRewardsResolver.StakeChanged += OnStakeChanged;
            GameStateService.NewGameStarted += OnNewGameStarted;
            TryPresent();
        }

        private void OnDisable()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            JewelPolishService.FirstPolishActionStarted -= Complete;
            StakeRewardsResolver.StakeChanged -= OnStakeChanged;
            GameStateService.NewGameStarted -= OnNewGameStarted;
            TutorialHighlightRegistry.Unregister(GuidanceHighlightId);
            CloseInternal(resumeGuidance: false);
            HideGuidance();
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode) => TryPresent();

        /// <summary>
        /// WO-1600 - the reset heard DIRECTLY, not inferred from the next scene load.
        /// ResetToNewGame clears the earned-item history this card is evidence of, so the
        /// card must go with the save that justified it. Without this, a card raised on the
        /// Title survives START NEW (this component is DontDestroyOnLoad and the HeroSelect
        /// load only re-runs TryPresent, which returns early while `_modal` is non-null).
        /// </summary>
        private void OnNewGameStarted()
        {
            if (_modal != null)
                FlowTrace.Step("JewelerFtue",
                    "NewGameStarted: dismissing a STANDING discovery card - the reset cleared " +
                    "carrier=GameState.EverAcquiredItemIds['" + DungeonExclusiveItems.RoughStoneId +
                    "'], so the evidence this card reports no longer exists.");
            CloseInternal(resumeGuidance: false);
            HideGuidance();
        }

        private static bool Completed
        {
            get
            {
                var s = GameStateService.Instance?.State;
                return s != null && s.SeenTutorials.TryGetValue(CompletionKey, out bool seen) && seen;
            }
        }

        /// <summary>A hero has been chosen, i.e. there is a player for this card to be about.
        /// ResetToNewGame wipes HeroClass back to None, so this is also the second, independent
        /// reason a fresh save cannot raise the card before the player reaches town.</summary>
        private static bool HasLiveHero
        {
            get
            {
                var s = GameStateService.Instance?.State;
                return s != null && s.HeroClass != HeroClassOpt.None;
            }
        }

        /// <summary>
        /// WO-1600 - the ONE gate, stated POSITIVELY: a home hub, a chosen hero, an earned
        /// stone, and not already completed. It replaces the old `scene.Contains("Dungeon")`
        /// exclusion, which let the Title, HeroSelect and every raid target through.
        /// `internal` so the regression and the capture reason about the same expression the
        /// runtime uses, rather than a copy of it.
        /// </summary>
        internal static bool ShouldPresent(string sceneName, out string reason)
        {
            if (!JewelerProgression.IsUnlocked)
            { reason = "no rough stone has ever been earned on this save"; return false; }
            if (Completed)
            { reason = "the discovery is already completed"; return false; }
            if (!HubScenes.IsHub(sceneName))
            { reason = "scene '" + sceneName + "' is not a home hub"; return false; }
            if (HubScenes.SuppressTownHud(sceneName))
            { reason = "scene '" + sceneName + "' is enemy-owned (a raid target), not home"; return false; }
            if (!HasLiveHero)
            { reason = "no hero class is chosen yet"; return false; }
            reason = "home hub with a live hero";
            return true;
        }

        private void TryPresent()
        {
            string scene = SceneManager.GetActiveScene().name ?? string.Empty;
            bool present = ShouldPresent(scene, out string reason);
            FlowTrace.Step("JewelerFtue",
                "TryPresent scene='" + scene + "' unlocked=" + JewelerProgression.IsUnlocked +
                " completed=" + Completed + " hero=" + (GameStateService.Instance?.State?.HeroClass) +
                " open=" + (_modal != null) + " -> " + (present ? "PRESENT" : "HOLD") +
                " (" + reason + "). carrier=GameState.EverAcquiredItemIds['" +
                DungeonExclusiveItems.RoughStoneId + "'] (JewelerProgression.IsUnlocked); " +
                "completion=SeenTutorials['" + CompletionKey + "'].");

            if (!present)
            {
                // A card the world no longer justifies is DISMISSED, never left standing:
                // this is the half that stops a Title-raised card riding START NEW into town.
                if (_modal != null) CloseInternal(resumeGuidance: false);
                HideGuidance();
                return;
            }
            if (_modal != null) return;
            HideGuidance();
            Present();
        }

        /// <summary>
        /// Build the card. Split out of <see cref="TryPresent"/> so the UI capture can shoot the
        /// real modal without defeating the gate above (the capture scene is not a hub, and a
        /// capture that had to bypass the gate would be proving a screen the player cannot reach).
        /// </summary>
        private void Present()
        {
            // WO-1471: PLAYER-OWNED, not the bounded default. This FTUE card waits for the player
            // to read it and press through, so elapsed time is not evidence of a leak. The probe
            // reuses the SAME liveness expression PanelManager.Register is given below. The Acquire
            // precedes the modal build, but Present is synchronous, so no watchdog tick can
            // observe _modal null (the probe is polled later, not evaluated here).
            _hold = WorldHold.AcquirePlayerOwned("jeweler-discovery",
                () => this != null && _modal != null && _modal.canvas != null);
            _modal = ElarionUiKit.BuildObsidianModal("JewelerDiscoveryUI", JewelerDiscoveryText.Title.Resolve(),
                ElarionUiKit.ModalArchetype.Compact, Close, sortingOrder: 31030);
            MedievalUiSkin.ApplyShell(_modal.chrome, compact: true);
            _panel = PanelManager.Register("Jeweler Discovery", Close,
                () => _modal != null && _modal.canvas != null && _modal.canvas.activeInHierarchy);
            if (!PanelManager.NotifyOpened(_panel)) { Close(); return; }

            // WO-1600 - lay out inside chrome.layout.body, NOT on chrome.content. The body zone's
            // bottom edge is already raised above the shared Close's fixed 360x120 box by the kit
            // (BuildObsidianPanel's close-band reservation), so the verb below cannot land on the
            // Close the way it did on the owner's 13:23 frame. Fall back to content only if a
            // future chrome ever ships without zones - never silently, per §12.
            Transform content = _modal.chrome.content.transform;
            Transform well = _modal.chrome.layout != null && _modal.chrome.layout.body != null
                ? _modal.chrome.layout.body.transform
                : content;
            if (well == content)
                FlowTrace.Warn("JewelerFtue",
                    "chrome.layout.body is absent - the card fell back to raw chrome.content " +
                    "fractions, which is the unprotected class the shared Close paints over.");

            // ⚠ THE PLATE IS INSET INSIDE THE RECT, MEASURED FROM TWO FRAMES - DO NOT RAISE THIS
            // CEILING BACK TO 1.0. The compact ApplyShell art (UI/ElarionMedieval/frames/
            // content-panel) does NOT paint the whole panel rect: the visible ornate plate spans
            // roughly y 0.17-0.85 of it. Measured on the owner's Jeweler frame (plate 195-690 in a
            // 90-809 rect = 0.165-0.854) AND, independently, on the shipped capture
            // Builds/ui-capture/AdConsent_2670x1200.png (plate 250-620 in a 162-737 rect =
            // 0.203-0.847) - the same recipe, a completely different rect aspect, the same
            // fractions. That inset is why the copy read as spilling "above the frame": the top of
            // the content rect is simply not on the plate. AdConsent has always drawn inside it by
            // topping out at 0.82 of its rect; this card now does the same.
            //  0.94 of the body well = 0.836 of the panel (well spans ~0.234-0.875 after the kit's
            //  close-band reservation), i.e. just under the measured 0.85 plate edge.
            // ⛔ The kit-owned TITLE band (0.905-0.975) is ABOVE the plate on this recipe and this
            //  file cannot move it - the AdConsent capture shows the same header rule floating over
            //  its plate. That is a MedievalUiSkin/asset defect shared by every compact-skinned
            //  procedural modal, not this card's to fix; it needs its own ticket.
            //
            // Copy occupies the upper part of the well; the verb takes the lower band with a gap.
            // No overflowMode here: FitBlock owns wrapping + Truncate + bounded auto-size, so the
            // copy shrinks to its plate instead of spilling past it.
            BuildRoughStoneImage(well);
            _body = ElarionUiKit.Label(well, DiscoveryBodyCopy(),
                0.50f, 0.68f, ElarionUi.Parchment, ElarionUi.FontBody,
                TextAlignmentOptions.Top, 0.05f, 0.95f);
            _body.enableWordWrapping = true;
            ElarionUiKit.FitBlock(_body, ElarionUi.FontFloorMobile, ElarionUi.FontBody);
            _stakeStatus = ElarionUiKit.Label(well, NativeStakeBonusLine(),
                0.29f, 0.50f, ElarionUi.Gilt, ElarionUi.FontLabel,
                TextAlignmentOptions.Center, 0.05f, 0.95f);
            _stakeStatus.enableWordWrapping = true;
            ElarionUiKit.FitBlock(_stakeStatus, ElarionUi.FontFloorMobile, ElarionUi.FontLabel);
            var open = ElarionUiKit.Button(well, JewelerDiscoveryText.OpenJeweler.Resolve(), ElarionUiKit.ButtonKind.Gold,
                new Vector2(0.04f, 0.02f), new Vector2(0.96f, 0.24f), OpenJeweler);
            MedievalUiSkin.ApplyButton(open, primary: true);
        }

        private static void BuildRoughStoneImage(Transform well)
        {
            const string artKey = "ItemIcons/ing_rough_stone";
            Sprite stone = Resources.Load<Sprite>(artKey);
            if (stone == null)
            {
                FlowTrace.Warn("JewelerFtue", "rough-stone image missing at Resources/" + artKey +
                                               "; the card will keep its translated rare-item copy.");
                return;
            }
            GameObject art = ElarionUiKit.AddImage(well, "RoughStoneImage",
                new Vector2(0.32f, 0.66f), new Vector2(0.68f, 0.96f), Color.white, rounded: false);
            Image image = art != null ? art.GetComponent<Image>() : null;
            if (image == null) return;
            image.sprite = stone;
            image.preserveAspect = true;
            image.raycastTarget = false;
        }

        private static string DiscoveryBodyCopy() => JewelerDiscoveryText.Body.Resolve();

        private void OnStakeChanged()
        {
            if (_stakeStatus == null) return;
            _stakeStatus.text = NativeStakeBonusLine();
            ElarionUiKit.FitBlock(_stakeStatus, ElarionUi.FontFloorMobile, ElarionUi.FontLabel);
            FlowTrace.Step("JewelerFtue", "native SKR stake read completed; discovery entitlement line refreshed.");
        }

        private static string NativeStakeBonusLine()
        {
#if DAPP_STORE
            IStakeQuery query = StakeRewardsResolver.Query;
            if (query == null || !query.TryGetActiveStake(out _))
                return JewelerDiscoveryText.StakeChecking.Resolve();
            StakeStanding standing = StakeRewardsResolver.Resolve();
            if (standing.HasStake && PolishBonuses.ExtraWeeklyRerolls > 0)
            {
                return (PolishBonuses.RollCapDelta > 0
                    ? JewelerDiscoveryText.StakeVerifiedHighTier
                    : JewelerDiscoveryText.StakeVerified).Resolve();
            }
            return JewelerDiscoveryText.StakeNotVerified.Resolve();
#else
            return string.Empty;
#endif
        }

        private void OpenJeweler()
        {
            Close();
            PanelRouter.Open(PanelId.JewelerCrafting);
        }

        private void Complete()
        {
            GameStateService.Instance?.MarkTutorialSeen(CompletionKey);
            CloseInternal(resumeGuidance: false);
            HideGuidance();
        }

        private static Transform ResolveJewelerTarget()
        {
            // Point at the storefront speaker the player naturally meets. It now routes
            // into the same polishing panel as the separate runtime bench. The bench is
            // a safe fallback while the placed storefront/NPC finishes spawning.
            var storefront = GameObject.Find("CastleVendor_Jeweler");
            if (storefront != null) return storefront.transform;
            var bench = GameObject.Find("CastleVendor_JewelersBench");
            return bench != null ? bench.transform : null;
        }

        private void RefreshGuidance()
        {
            if (_modal != null) return;
            string scene = SceneManager.GetActiveScene().name ?? string.Empty;
            if (!ShouldPresent(scene, out _)) { HideGuidance(); return; }

            ObjectiveStripUi.Show(JewelerDiscoveryText.Guidance.Resolve());
            DeNelle.Village.FtueWorldPointer.TryShow(GuidanceHighlightId);
            FlowTrace.Step("JewelerFtue",
                "persistent guidance ARMED: objective strip + world pointer -> '" +
                GuidanceHighlightId + "'; remains until FirstPolishActionStarted.");
        }

        private static void HideGuidance()
        {
            ObjectiveStripUi.Hide();
            if (DeNelle.Village.FtueWorldPointer.ArmedTargetId == GuidanceHighlightId)
                DeNelle.Village.FtueWorldPointer.Hide();
        }

        // WO-1471: the per-frame renew Update is DELETED - it was the workaround for
        // the bounded ceiling, and a player-owned hold has no ceiling to outrun.
        // WO-1360/WO-1471: with no ceiling the host's own lifecycle is the net, so this component
        // steps out on BOTH exits. OnDisable already calls Close(); a destroyed host never receives
        // OnDisable in every teardown order, so OnDestroy releases the hold and the card together.
        private void OnDestroy() => CloseInternal(resumeGuidance: false);

        private void Close() => CloseInternal(resumeGuidance: true);

        private void CloseInternal(bool resumeGuidance)
        {
            if (_panel != null) PanelManager.NotifyClosed(_panel);
            _hold?.Dispose(); _hold = null;
            if (_modal != null && _modal.canvas != null) Destroy(_modal.canvas);
            _modal = null; _panel = null; _body = null; _stakeStatus = null;
            if (resumeGuidance) RefreshGuidance();
        }
    }
}

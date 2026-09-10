using System;
using System.Collections.Generic;
using System.IO;

namespace DeNelle.Editor.Regression
{
    public static class JewelerDiscoveryFtueRegression
    {
        public static bool Run(out string reason)
        {
            var f = new List<string>();
            string dungeon = Read("Assets/_Modules/Dungeons/DungeonController.cs", f);
            string ftue = Read("Assets/_Modules/Village/Crafting/JewelerDiscoveryFtue.cs", f);
            string polish = Read("Assets/_Modules/Village/Crafting/JewelPolishService.cs", f);
            string panel = Read("Assets/_Modules/Village/Items/JewelerPanelMvvm.cs", f);
            string runState = Read("Assets/_Modules/Dungeons/State/DungeonRuntimeState.cs", f);
            string station = Read("Assets/_Modules/Village/Items/JewelerStationInjector.cs", f);
            string dialogue = Read("Assets/Resources/Data/Canonical/dialogue/dialogues.json", f);
            string dialogueTwin = Read("Assets/StreamingAssets/Data/Canonical/dialogue/dialogues.json", f);
            string sink = Read("Assets/_Modules/Village/Tutorial/DialogueCommandSink.cs", f);
            string bonus = Read("Assets/_Modules/Core/Catalog/PolishBonusProvider.cs", f);
            string stakeResolver = Read("Assets/_Modules/Core/Platform/StakeRewardsResolver.cs", f);
            string nativeStake = Read("Assets/_Modules/Wallet/NativeSkrStakeQuery.cs", f);
            string jewelerText = Read("Assets/_Modules/Village/Crafting/JewelerDiscoveryText.cs", f);
            string polishFlow = Read("Assets/_Modules/Village/Crafting/JewelPolishFlowPanel.cs", f);
            string english = Read("Assets/Resources/Data/Canonical/en.json", f);
            string englishTwin = Read("Assets/StreamingAssets/Data/Canonical/en.json", f);

            // Re-pointed 2026-09-09 by the lead WITH the owner's WO-1373 ruling ("5% drop rate in
            // dungeons"): the post-first rate is a knob on the RemoteTunables rail, default 5, read by
            // DungeonController.PostFirstRoughStoneDropPct. The old literal pin (0.15f) is retired.
            if (!dungeon.Contains("RemoteTunables.KeyDungeonRoughStoneDropPct")) f.Add("post-first rate no longer reads the rail knob dungeon.roughStoneDropPct");
            if (DeNelle.Core.Ops.RemoteTunables.DungeonRoughStoneDropPctDefault != 5) f.Add("post-first rate default is not the owner-ruled 5% (read " + DeNelle.Core.Ops.RemoteTunables.DungeonRoughStoneDropPctDefault + ")");
            if (!dungeon.Contains("!firstDungeonStone && !ShouldAwardPostFirstStone")) f.Add("guaranteed first award does not bypass later RNG");
            if (!dungeon.Contains("inv.AddEarned(stoneId, 1)")) f.Add("dungeon reward no longer stamps earned history");
            if (!dungeon.Contains("st.TryClaimReward()") || !runState.Contains("public bool TryClaimReward()")) f.Add("retry/re-entry can evaluate the same run reward twice");
            string[] localizedKeys =
            {
                "jewelerFtue.title", "jewelerFtue.body", "jewelerFtue.openJeweler",
                "jewelerFtue.guidance", "jewelerFtue.stakeChecking", "jewelerFtue.stakeVerified",
                "jewelerFtue.stakeVerifiedHighTier", "jewelerFtue.stakeNotVerified",
                "jewelerPolish.title", "jewelerPolish.body", "jewelerPolish.begin",
                "jewelerPolish.started", "jewelerPolish.failed", "jewelerPolish.noStone",
                "jewelerPolish.revealTitle", "jewelerPolish.revealBody", "jewelerPolish.keep"
            };
            foreach (string key in localizedKeys)
                if (!jewelerText.Contains("\"" + key + "\"") ||
                    !english.Contains("\"" + key + "\""))
                    f.Add("localized Jeweler FTUE key missing: " + key);
            if (!jewelerText.Contains("LocalizedText<DurationArguments> PolishBody") ||
                !jewelerText.Contains("LocalizedText<DurationArguments> BeginPolish") ||
                !jewelerText.Contains("LocalizedText<GemArguments> RevealBody") ||
                !polishFlow.Contains("PolishBody.Resolve(new DurationArguments") ||
                !polishFlow.Contains("BeginPolish.Resolve(new DurationArguments") ||
                !polishFlow.Contains("RevealBody.Resolve(new GemArguments"))
                f.Add("formatted Jeweler copy bypasses the typed global localization wrapper");
            if (!string.Equals(english, englishTwin, StringComparison.Ordinal))
                f.Add("English canonical localization twins diverged");
            if (!ftue.Contains("JewelerDiscoveryText.Title") ||
                !ftue.Contains("JewelerDiscoveryText.Body") ||
                !ftue.Contains("JewelerDiscoveryText.OpenJeweler"))
                f.Add("Jeweler FTUE bypasses its localization string library");
            if (!ftue.Contains("Resources.Load<Sprite>(artKey)") ||
                !ftue.Contains("ItemIcons/ing_rough_stone") ||
                !ftue.Contains("image.preserveAspect = true"))
                f.Add("discovery card does not represent the rough stone with its contained image");
            if (!File.Exists("Assets/Resources/ItemIcons/ing_rough_stone.png"))
                f.Add("supplied rough-stone image asset missing");
            string roughStoneMeta = Read("Assets/Resources/ItemIcons/ing_rough_stone.png.meta", f);
            if (!roughStoneMeta.Contains("textureType: 8") || !roughStoneMeta.Contains("spriteMode: 1"))
                f.Add("rough-stone image is not imported as a Unity sprite");
            if (ftue.Contains("\"You recovered a rare Rough Stone.\"") ||
                ftue.Contains("\"Polish Rough Stone\""))
                f.Add("discovery card still names the rough stone in body/button text instead of using the image");
            if (!ftue.Contains("JewelerProgression.IsUnlocked") || !ftue.Contains("PanelId.JewelerCrafting")) f.Add("FTUE is not gated by earned history and routed to Jeweler");
            if (!ftue.Contains("FirstPolishActionStarted += Complete") || !polish.Contains("FirstPolishActionStarted?.Invoke()")) f.Add("completion is not driven by the real accepted polish action");
            if (!panel.Contains("if (!DeNelle.Village.Crafting.JewelerProgression.IsUnlocked)")) f.Add("locked Jeweler direct route is not hidden/refused");
            if (!station.Contains("if (!DeNelle.Village.Crafting.JewelerProgression.IsUnlocked)")) f.Add("locked Jeweler world/navigation entry is still visible");
            if (!ftue.Contains("GuidanceHighlightId = \"world.jeweler\"") ||
                !ftue.Contains("ObjectiveStripUi.Show(JewelerDiscoveryText.Guidance.Resolve())") ||
                !ftue.Contains("FtueWorldPointer.TryShow(GuidanceHighlightId)") ||
                !ftue.Contains("CastleVendor_Jeweler") ||
                !ftue.Contains("FirstPolishActionStarted += Complete"))
                f.Add("return-home discovery does not leave a persistent objective + world pointer on Sable until the first accepted polish");
            if (!dialogue.Contains("\"text\": \"Buy supplies.\"") ||
                !dialogue.Contains("\"text\": \"Sell items.\"") ||
                !dialogue.Contains("\"text\": \"Craft.\"") ||
                !dialogue.Contains("\"id\": \"craft_categories\"") ||
                !dialogue.Contains("\"text\": \"Potions and consumables.\"") ||
                !dialogue.Contains("\"text\": \"Rings and amulets.\"") ||
                !dialogue.Contains("\"text\": \"Polish a stone.\"") ||
                !dialogue.Contains("\"requires\": \"jeweler_unlocked\"") ||
                !dialogue.Contains("\"verb\": \"OpenAlchemy\"") ||
                !dialogue.Contains("\"verb\": \"OpenJeweler\"") ||
                !dialogue.Contains("\"verb\": \"OpenRoughStonePolish\"") ||
                !sink.Contains("condition == \"jeweler_unlocked\"") ||
                !sink.Contains("JewelerProgression.IsUnlocked") ||
                !sink.Contains("case \"OpenRoughStonePolish\"") ||
                !polishFlow.Contains("JewelPolishService.TryStartPolish") ||
                !polishFlow.Contains("JewelPolishCatalog.PolishSeconds") ||
                !polishFlow.Contains("ObsidianQueueGate.RequestToggle") ||
                !polishFlow.Contains("public static void ShowReveal(string gemId)") ||
                !polish.Contains("JewelPolishFlowPanel.ShowReveal(gemId)"))
                f.Add("Sable Store does not start with Buy/Sell/Craft categories and route each craft lane, including timed polish + reveal");
            if (!string.Equals(dialogue, dialogueTwin, StringComparison.Ordinal))
                f.Add("dialogue canonical twins diverged");
            if (!ftue.Contains("JewelerDiscoveryText.StakeChecking") ||
                !ftue.Contains("JewelerDiscoveryText.StakeVerifiedHighTier") ||
                !ftue.Contains("JewelerDiscoveryText.StakeVerified") ||
                !ftue.Contains("JewelerDiscoveryText.StakeNotVerified"))
                f.Add("discovery card does not explicitly report the native SKR stake check and re-roll result");
            if (ftue.Contains("JewelerDiscoveryText.StakeVerified +") ||
                ftue.Contains("JewelerDiscoveryText.StakeHighTier") ||
                ftue.Contains("StakeVerified.Resolve() +"))
                f.Add("native SKR result is assembled from translated sentence fragments");
            if (!nativeStake.Contains("SKRskrmtL83pcL4YqLWt6iPefDqwXQWHSw9S9vz94BZ") ||
                !nativeStake.Contains("Encoding.UTF8.GetBytes(\"user_stake\")") ||
                !nativeStake.Contains("WalletPreferenceStore.CurrentSessionWalletAddress") ||
                !nativeStake.Contains("GetAccountInfoAsync") ||
                !nativeStake.Contains("no signature requested"))
                f.Add("logged-in wallet address is not resolved read-only against the official native SKR staking program");
            // ⭐ RE-POINTED 2026-09-10 (WO-1679 / HEART-006, owner ruling Q-LADDER 13:12: "merge -
            //    the Heartbound tier drives polish attempts (the polish mapping becomes a benefit
            //    row)"). This assertion used to require the literal `Standing.HasStake ? 1 : 0` -
            //    the grant AMOUNT derived from the stake. The ruling moved that authority to the
            //    tier, so the old string is gone and the check moves WITH the ruling.
            //
            // ⛔ IT IS NOW STRICTER, NOT LOOSER: it pins BOTH halves of the merge, where the old
            //    line pinned one. The amount must come from the merged ladder, AND the grant must
            //    still be gated on a backend-VERIFIED stake - a tier alone must never pay, because
            //    the tier and the stake snapshot are held in different statics and a later failed
            //    refresh deliberately holds the previous benefits (WO-1674 §A3).
            //
            // ⚠ WHY NOT "GRANT TIER I WHENEVER A STAKE IS VERIFIED", which was the other candidate
            //    fix: MEASURED, IT IS FALSE. The minimum Heartbound stake is 100 SKR; activation
            //    seats effective stake at 25% of it, and resonanceScore(25, 0) = 41 against Tier I's
            //    threshold of 300 - Tier 0. Even at FULL effective stake, 100 SKR scores 146, still
            //    Tier 0. A client-side "verified stake implies Tier I" floor would therefore grant
            //    the weekly attempt to players who are genuinely Tier 0: a client fabricating an
            //    entitlement the server never issued, which is product rule 7's exact shape, and it
            //    would reinstate the second ladder Q-LADDER was ruled to remove.
            if (!bonus.Contains("class NativeSkrPolishBonus") ||
                !bonus.Contains("Standing.HasStake ?") ||
                !bonus.Contains("HeartboundBonuses.ExtraWeeklyRerolls") ||
                !bonus.Contains("HeartboundBonuses.RollCapDelta") ||
                !bonus.Contains("WeeklyRerollsRemaining") ||
                !bonus.Contains("TryConsumeWeeklyReroll") ||
                !polish.Contains("useWeeklyStakeReroll") ||
                !polish.Contains("PolishBonuses.TryConsumeWeeklyReroll()"))
                f.Add("the merged Heartbound ladder does not grant and consume the one weekly bonus " +
                      "attempt against a backend-verified stake (Q-LADDER: the TIER supplies the " +
                      "amount, the VERIFIED STAKE gates whether anything is granted at all)");
            if (!stakeResolver.Contains("StakeChanged") || !ftue.Contains("StakeRewardsResolver.StakeChanged += OnStakeChanged"))
                f.Add("asynchronous native stake result cannot refresh an already-open FTUE card");
            if (ftue.Contains("...')") || ftue.Contains("…")) f.Add("FTUE contains forbidden ellipsis");

            // =================================================================
            //  WO-1600 - WHERE the card may appear, WHEN it must leave, and the
            //  geometry that keeps it inside its own plate.
            //
            //  All five checks below are RED against the source that shipped in
            //  build 2026.09.07.359651 (the owner's 13:23 Title-screen frame):
            //  that version gated only on `scene.Contains("Dungeon")`, never
            //  heard the reset, laid its fractions straight on chrome.content,
            //  declared TextOverflowModes.Overflow, and emitted no trace at all.
            // =================================================================

            // (1) THE SCENE GATE, POSITIVE. An exclusion list is only ever as long
            //     as the last bug - "Dungeon" did not describe the Title screen.
            if (!ftue.Contains("HubScenes.IsHub(sceneName)") ||
                !ftue.Contains("HubScenes.SuppressTownHud(sceneName)") ||
                !ftue.Contains("HasLiveHero"))
                f.Add("the discovery card is not gated to a HOME HUB with a live hero - it can " +
                      "raise itself over the Title / HeroSelect / a raid target, which is exactly " +
                      "the owner's 2026-09-07 13:23 frame");

            // (2) THE STANDING CARD. This component is DontDestroyOnLoad, so a card
            //     raised before START NEW rides the reset into the new save unless the
            //     reset is heard AND a failing gate dismisses what is already open.
            if (!ftue.Contains("GameStateService.NewGameStarted += OnNewGameStarted") ||
                !ftue.Contains("GameStateService.NewGameStarted -= OnNewGameStarted"))
                f.Add("the FTUE does not subscribe/unsubscribe GameStateService.NewGameStarted - a " +
                      "card standing when the player presses START NEW survives the reset that " +
                      "erased the earned-stone history it reports");
            if (!ftue.Contains("if (_modal != null) CloseInternal(resumeGuidance: false);"))
                f.Add("TryPresent does not DISMISS an open card when the gate stops holding - it " +
                      "only ever refuses to open a new one, so a mis-placed card is permanent");

            // (3) GEOMETRY. chrome.content is the kit's own "unprotected legacy class":
            //     the shared Close is seated there at the default bottom band as a fixed
            //     360x120 box, and a raw 0.10-0.29 fraction lands on top of it.
            //     chrome.layout.body is the zone whose floor the kit already raises above
            //     that band (BuildObsidianPanel's close-band reservation, WO-714 P6).
            if (!ftue.Contains("_modal.chrome.layout.body"))
                f.Add("the card lays its copy and its verb on raw chrome.content fractions instead " +
                      "of the reserved chrome.layout.body well - that is what put OPEN CRAFTING: " +
                      "JEWELER on top of the modal's own CLOSE");

            // (4) NO OVERFLOW. It was DEAD code (FitBlock sets Truncate one statement
            //     later) and it misdirected the reader about what spilled; pinned absent
            //     so nobody restores it as a "fix" for copy that does not fit.
            if (ftue.Contains("TextOverflowModes.Overflow"))
                f.Add("the discovery body still declares TextOverflowModes.Overflow - FitBlock " +
                      "overwrites it with Truncate, so the line proves nothing and teaches the " +
                      "next reader the wrong cause");

            // (5) THE TRACE (§12). The gate must say, on every evaluation, which scene it
            //     saw and which carrier decided it - so the next occurrence is one read.
            if (!ftue.Contains("FlowTrace.Step(\"JewelerFtue\"") ||
                !ftue.Contains("carrier=GameState.EverAcquiredItemIds"))
                f.Add("TryPresent is not instrumented with the [Flow:JewelerFtue] scene/unlocked/" +
                      "completed line naming the EverAcquiredItemIds carrier");

            if (f.Count > 0) { reason = "JEWELER_DISCOVERY_FTUE_FAIL: " + string.Join(" | ", f); return false; }
            reason = "JEWELER DISCOVERY FTUE OK - first-stone return card points to Sable; Store starts with Buy/Sell/Craft, Craft routes potions/jewelry/polish, and the logged-in wallet is checked read-only for the native SKR weekly re-roll";
            return true;
        }

        private static string Read(string path, List<string> f)
        {
            if (File.Exists(path)) return File.ReadAllText(path);
            f.Add("missing " + path); return string.Empty;
        }
    }
}

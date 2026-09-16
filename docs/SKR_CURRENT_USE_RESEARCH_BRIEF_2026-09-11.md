# SKR in Echoes of Elarion — current implementation and research brief

Source review: September 11, 2026. This describes the current local worktree, not a fresh certification of every feature on the published APK. Older comments and design documents sometimes describe behavior that has since changed. The distinctions below follow current code and authored data.

## 1. Real SKR purchases: Night Market

The Solana/dApp-store purchase rail uses SKR from the connected wallet. The backend quotes the payment, the wallet signs, and purchase verification/entitlements authorize the in-game grant. Real packs are priced in USD and quoted in SKR by the server; historical fixed SKR numbers in catalog JSON are not reliable current checkout prices.

The currently visible catalog includes:

- Resource bundles: Starter's Hand, Folk's Thanks, Resource Pack I and Resource Pack II. Contents include combinations of crystals, coins, wood, iron and Stone, plus lantern-expedition convenience items.
- Resource shortfall packs: wood, iron, Stone and crystal packs. Not every visible catalog row is displayed in every shelf context; some are shortfall offers.
- Builder convenience: Builder's Hour and Permanent Builder.

Cosmetic bundles exist in the catalog but the inspected cosmetic rows are hidden. Do not advertise those rows as currently purchasable merely because their definitions exist. Some historical monthly-card/pass prices also exist without a complete purchasable premium-pass offering.

Sources: Assets/_Modules/Wallet/PackStore.cs; Assets/_Modules/Wallet/WalletService.cs; Assets/Resources/Data/Canonical/packs.json; api/purchases/quote.js; api/_lib/purchase-catalog.js.

## 2. Native staking verification: Heartbound

The game has a backend verifier for the connected wallet's native SKR staking position. This reads the external staking position; it is not a game-owned staking vault. Active stake, unstaking/cooldown state and freshness inform the verified snapshot. Heartbound state and benefit decisions are server-side; client-reported stake must not authorize rewards.

The authored Heartbound system combines effective stake and tenure into ten progression tiers. The configuration currently sets a 100-SKR minimum eligibility amount and ramps newly effective stake across pulses. Tier thresholds are resonance scores, not a simple list of fixed token balances. Do not describe the old standalone stake-display ladder as the current benefit authority.

Sources: api/_lib/skr-staking.js; api/heartbound/status.js; api/_lib/heartbound-state.js; api/_lib/heartbound-resonance.js; api/_lib/heartbound-resonance-config.json; Assets/_Modules/Wallet/HeartboundStatusClient.cs.

## 3. Connected staking benefit: extra jewel-polishing attempts

The polish provider consumes backend-granted Heartbound benefits. The authored cumulative ladder grants one extra weekly reroll at Tier I and increases the per-stone roll cap by one at Tier V. These are additional attempts, not improved odds, rarity weights or guaranteed results. The provider also requires verified active stake and the distribution flags.

This is implemented gameplay wiring. Availability in a particular installed build still depends on its channel, flags and accepted backend response.

Sources: Assets/_Modules/Core/Catalog/PolishBonusProvider.cs; Assets/_Modules/Core/Catalog/HeartboundBenefits.cs; api/_lib/heartbound-tiers-config.json; Assets/_Modules/Core/FeatureFlags.cs.

## 4. Heartbound economic benefits: authored, incompletely connected

The server benefit table authors cumulative offline-production bonuses of +2% at Tier I, another +2% at Tier II and another +2% at Tier VII: +6% total under a 10% recurring-production design ceiling.

However, the current source scan found no gameplay consumer of OfflineProductionRateBonus. It is exposed by the provider but does not yet increase the production claim. These numbers are configured intentions, not benefits to promise as working today.

The ceiling measures recurring production-rate bonuses only. It does not include event drops or timers, and it is not a server-enforced anti-cheat boundary for the whole client economy.

Sources: api/_lib/heartbound-tiers-config.json; Assets/_Modules/Core/Catalog/HeartboundBenefits.cs.

## 5. Heart Pulse / Echo events: partial infrastructure

There is backend pulse detection and an authored event pool, plus a client event parser/inbox and application handlers. The table currently includes:

- Resource Surge: gathering modifier.
- Crafting Inspiration: crafting-speed modifier.
- Scout Whisper: earlier access to the existing scout report.
- Echo Labor: construction-speed modifier.
- Heartfire Spark: one charge through the existing Heartfire service.

These are not five verified live player benefits. The current scan found no caller connecting HeartboundEventClient to the status response. The temporary modifier values are stored but have no gameplay consumers, and the scout-access grant is not read by the scout report. The Heartfire application handler exists, but that alone does not prove delivery from a real pulse to the player. High-tier presentation/prestige entries and event-choice ideas also must not be described as finished features on this evidence.

Sources: api/cron/heart-pulse.js; api/_lib/heartbound-pulse.js; api/_lib/heartbound-events-config.json; Assets/_Modules/Wallet/HeartboundEventClient.cs; Assets/_Modules/Village/Heartbound/HeartboundEventApplier.cs; Assets/_Modules/Core/Heartbound/HeartboundModifiers.cs; Assets/_Modules/Core/Heartbound/HeartboundScoutAccess.cs.

## 6. Older Arena SKR wagers: simulation, not real tokens

The older Arena labels its dApp-channel wager balance SKR, but ArenaWalletService backs that with a local PlayerPrefs balance initially seeded to 500. Its debits and winnings are not wallet transfers, escrow or withdrawable SKR. This must not be represented as live token wagering or play-to-earn.

The new captured-town AI practice flow is separate and has no wager or payout. The existing Google Play Arena uses crystals through its channel-specific currency path; it must not convert the fake SKR seed into crystals.

Sources: Assets/_Modules/Village/Arena/ArenaWalletService.cs; Assets/_Modules/Village/Arena/ArenaMode.cs; Assets/_Modules/Core/State/OwnedTownPracticeSession.cs.

## 7. Display, demos and misleading legacy names

- The connected Night Market wallet chip can display the wallet's SKR balance.
- The separate SKR showcase/staking demo is presentation-only and off by default. It does not connect a wallet, sign a transaction or pay a reward.
- A legacy tower-rotation UI still has a costSkr variable and an SKR label even though BuildModeController passes its crystal cost. This is a labeling defect, not evidence that building towers spends SKR. Recorded during this review; not changed by the research task.
- Battle/monthly reward definitions can name SKR, but the reward catalog rejects those grants without a real SKR credit ledger. No functioning player SKR-earning/cash-out loop was established by this review.

Sources: Assets/_Modules/Core/UI/SkrShowcasePanel.cs; Assets/_Modules/Core/FeatureFlags.cs; Assets/_Modules/Village/BuildMode/BuildModeController.cs; Assets/_Modules/Village/UI/TowerPlacementRotateMenu.cs; Assets/_Modules/Wallet/BattleMonthlyCatalog.cs.

## Channel boundary

Heartbound/staking features belong to the Solana/dApp-store build. The Google Play build excludes the Wallet assembly and must not expose these crypto features. This describes the source boundary; final artifact inspection is still part of the active release work.

## Questions worth researching

1. Which SKR utility gives a free player a clear optional benefit without making spending or staking feel required?
2. Are extra polishing attempts and convenience purchases a better initial focus than expanding the partially connected Heartbound ladder?
3. How should stake size and tenure be balanced so a smaller long-term participant has meaningful progress?
4. What terminology should replace the legacy Arena's simulated SKR balance so it cannot be confused with real tokens?
5. Which Heartbound benefits can be measured in actual play, and which should be hidden until their delivery path works?
6. What economic sources and sinks support purchases without creating an unsupported expectation of earning or withdrawing SKR?
7. How should the Solana and Google Play experiences communicate their differences while sharing the same core game?

Suggested prompt: "Review these implemented and planned SKR uses for player value, economic sustainability and clear communication. Separate real purchases, externally verified staking, incomplete reward wiring and simulated Arena currency. Recommend priorities and identify misleading promises. Do not assume the game currently pays out withdrawable SKR or that configured Heartbound bonuses are already active."

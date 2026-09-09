# WO-1606 - Jeweler return FTUE pointer and reachable polish action

**Status:** COMPLETE - production EXE/APK verified; APK installed on Seeker

## Owner ruling (2026-09-08)

> "there needs to be a FTUE for this when coming back with a stone and a pointer to show player what to do"

> "explicitly call out a line in there that checks if native SKR staker and adds the reroll discussed"

## Captured defect

On Seeker build `2026.09.08.360990`, the first rough-stone payout and return-home discovery both fired,
but dismissing the discovery left no persistent guidance. Talking to the nearby main Jeweler then offered
only Buy, Sell, and Just passing through. Device trace measured the discovery as PRESENT, the dialogue as
three option rows, and the sell panel as one Mana Crystal Shard row.

The existing `jewelers-bench` dialogue already routes `OpenJeweler` to `PanelId.JewelerCrafting`; the
main `jeweler` dialogue does not. The polish service and panel already exist and must be reused.

## Acceptance

1. Returning home with the first rough stone shows the existing discovery card.
2. Dismissing that card leaves a persistent objective strip and the owner-picked world pointer on Sable.
3. Guidance remains until `JewelPolishService.FirstPolishActionStarted` fires, then clears permanently.
4. Sable opens on the simple Store categories Buy / Sell / Craft / Leave. Craft then routes to Potions
   & Consumables, Rings & Amulets, or Polish a Stone (the latter only when unlocked).
5. Polish uses the existing `JewelPolishService` and queue authority, with a dedicated handoff/reveal
   presentation so it can no longer be mistaken for ring crafting.
6. Regression pins the return trigger, persistent pointer/objective lifecycle, route, and unlock gate.
7. The dApp-store FTUE explicitly checks the logged-in wallet address against the official native SKR
   staking program and reports pending, verified/+1 weekly re-roll, or not verified/no grant.
8. A verified active stake grants one extra attempt per fixed seven-day period; 10,000+ active SKR also
   raises the per-stone cap from 5 to 6. The check is read-only and never changes roll odds.
9. The discovery card represents the Rough Stone with the existing contained stone image and uses generic,
   translatable body/action copy instead of embedding the item name in those sentences.

## Files

- `Assets/_Modules/Village/Crafting/JewelerDiscoveryFtue.cs`
- `Assets/_Modules/Village/Tutorial/DialogueCommandSink.cs`
- both canonical `dialogue/dialogues.json` copies
- `Assets/Editor/Regression/JewelerDiscoveryFtueRegression.cs`
- `Assets/_Modules/Core/Catalog/PolishBonusProvider.cs`
- `Assets/_Modules/Core/Platform/StakeRewardsResolver.cs`
- `Assets/_Modules/Wallet/NativeSkrStakeQuery.cs`
- `Assets/_Modules/Village/Crafting/JewelPolishFlowPanel.cs`
- `Assets/_Modules/Village/Crafting/JewelerDiscoveryText.cs`

## Result (2026-09-08)

- Full regression: `REGRESSION_OK 457/457 suites`.
- Windows production player: `Builds/Windows/DefendersOfTheRealm.exe`; release build, no dev tools or
  watermark; remained live through a 15-second Direct3D 11 startup smoke test.
- Android production APK: `Builds/Android/DefendersOfTheRealm.apk`, version
  `2026.09.08.361171` (`361171`), store-shaped `DAPP_STORE` build with no `TESTER_BUILD` define.
- Android content: `R2_PARITY_OK targets=Android,StandaloneWindows64,WebGL objects=276`.
- Seeker `SM02G4061955851`: install returned `Success`; package remained alive after launch.
- Play technical-quality follow-up included in the same source: all rotations enabled, packaged activity
  is `resizeableActivity=true` with `screenOrientation=fullUser`, APK `zipalign -P 16` passes, and the
  packaged ARM64 Google Sign-In library has `0x4000` ELF LOAD alignment.
# 2026-09-08 final desktop verification correction

The startup-only Windows smoke was insufficient: the owner found a native crash on starting
`raider_camp_small`. The player log tied it to Unity's stale Orc AssetBundle cache entry and a
concurrent family/typed request at the `Orc_Necromancer` spawn. The shipping fix adds a one-time
pre-Addressables cache repair and serializes typed loads behind an in-flight family download.
`EnemyLoadBoundedRegression` now pins both obligations. Full DataRegression is 457/457; a fresh
development-player smoke entered the exact raid scene and necromancer spawn, proved both request
orders serialize to one cache writer, made the real necromancer body resident in 6.1 seconds,
late-re-skinned the placeholders, exited 0, and logged zero cache-write/crash signatures. The corrected no-dev-tools release is
`Builds/Windows/DefendersOfTheRealm.exe`.

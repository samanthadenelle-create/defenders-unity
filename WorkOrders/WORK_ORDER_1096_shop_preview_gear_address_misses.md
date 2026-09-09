# WORK ORDER 1096 — the party-shop preview misses a LOCAL gear address and renders a fallback

**Status:** READY TO IMPLEMENT — **may be Editor-only; the discriminating check is step 1**
**Minted:** 2026-09-09 by the UI seat (UI reserved block; banner bumped 1096 → 1097 in the same edit)
**Silo:** Content / Addressables · Shop UI
**Severity:** P2 — cosmetic but player-facing: the shop shows a fallback instead of the item
**Source:** F8 capture seq=4968 (Editor, `Main_Castle_Overworld`, `t=2783.98`)

---

## The capture

```
[Flow:VisualFactory] model not found via Addressables OR Resources: 'gear/weapon/Shield1h_03' —
returning null (caller falls back). UNDERLYING FETCH CAUSE: none recorded — no async fetch has FAILED
for this address, so the bytes were either never requested or are still in flight
[fetchAttempts=1/3, resolveAttempts=1, warmerState=Warm, resident=44, pending=1, lastTransportUrl=(none)]
```

Caller chain: `PartyShopPanelMvvm.RenderPreview` → `BuildPreviewModelOrFallback`
(`PartyShopPanelMvvm.cs:1466-1467`) → `Guard.Try` → `VisualFactory.Skin`
(`VisualFactory.cs:268`) → `ReportResolveMiss` (`:208`).

**Player-visible effect:** open the party shop on a one-handed shield and the preview shows the
fallback, not the shield. It does not crash — `Guard.Try` catches it — so it degrades quietly.

## ⛔ This is NOT WO-1089, and NOT the §16 R2 trap. Both are ruled out at source.

- **Not the warmer death.** `warmerState=Warm, resident=44` — the warmer completed successfully. This
  capture is in fact *evidence the WO-1089 fix is working*.
- **Not a missing/renamed address.** `gear/weapon/Shield1h_03` exists:
  `Assets/AddressableAssetsData/AssetGroups/Gear.asset:634`. It sits among its siblings
  (`Shield1h_07/08/11/15/16/19/22`, same file).
- **Not R2.** The Gear group's schema (`Schemas/Gear_BundledAssetGroupSchema.asset:37-40`) resolves to
  profile entries **`Local.BuildPath` / `Local.LoadPath`**
  (`AddressableAssetSettings.asset:94-97, 105-110`) — **not** the
  `https://pub-ab6dfaf1b3d74ca78891876611ccb832.r2.dev/[BuildTarget]` remote entry at `:101`.
  **The bytes ship inside the app.** Nothing to push; do not run the R2 chain for this.

So: a **local, existing, correctly-addressed asset failed to resolve, with no fetch ever attempted**
(`lastTransportUrl=(none)`).

## Step 1 — the check that decides whether this is even a real defect

**Read the Addressables play-mode script for that Editor session.** If it was *"Use Existing Build
(requires built groups)"* and the local groups had not been rebuilt, **every** local address misses in
the Editor while the device build is completely fine. That is the single cheapest explanation and it
must be excluded before any code is touched.

- If play mode was "Use Existing Build" with stale/absent local bundles → **Editor-only artifact.**
  Close this ticket with that recorded, and consider a startup guard that says so out loud, because
  this cost a triage cycle and will again.
- If play mode was "Use Asset Database (fastest)" or the local groups were current → **a real
  resolve defect**, continue below.

## If it is real — the two candidates

1. **No warmer covers gear.** The warmer set is `EnemyContentWarmer`, `HeroContentPrewarmer`,
   `StructureContentWarmer` (`Assets/_Modules/Core/Addressables/`). A grep of `HeroContentPrewarmer`
   for `gear/` returns **nothing**, so no prewarm path claims these addresses. If `VisualFactory.Skin`
   resolves synchronously against a resident set, a never-warmed local address misses **on first
   request every time** — and `resolveAttempts=1, pending=1` is consistent with exactly that.
2. **The shop preview resolves too eagerly.** `pending=1` says a load is outstanding for this address
   at the moment of the miss. If the preview asks once, synchronously, and never re-asks when content
   settles, the fallback is permanent for that panel open — the structure path already solved this
   with a `WhenSettled` retry, and the shop path appears to have no equivalent.

Fix at whichever is proven, not both on spec. If it is (2), reuse the existing settled-retry pattern
rather than inventing a second one.

## Do NOT

- Do not add gear addresses to `StructureContentWarmer` — wrong bounded context. If gear needs
  warming, it belongs in the hero/gear prewarmer that already exists.
- Do not touch `VisualFactory.ReportResolveMiss`. Its diagnostics are exactly what made this
  ticket cheap to write.

## Acceptance criteria

- [ ] The play-mode script for the failing session is recorded in the RESULT, and the Editor-only
      hypothesis is explicitly confirmed or excluded.
- [ ] If real: opening the party shop on `Shield1h_03` — and one sibling shield — shows the model, not
      the fallback, on a cold panel open.
- [ ] No new warmer or spawner is introduced; the existing prewarm/settled-retry seam is reused.
- [ ] Brace balance; gate markers on a fresh log.
- [ ] Owner felt-verifies the shop preview on device and closes.

## Unproven

- Whether this reproduces on the **device**. The capture is Editor-side, and the Local-group play-mode
  trap above does not exist in a player build — so a device repro is what makes this a real ticket.
- Whether other gear families (armour, one-handed weapons) miss the same way, or only shields.
  `resident=44` says plenty of content *did* resolve; the ticket does not establish what the 44 were.

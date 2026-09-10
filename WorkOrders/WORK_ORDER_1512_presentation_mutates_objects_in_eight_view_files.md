# WO-1512: presentation mutates the objects in eight view files; one view spends currency and one grants it

**Status:** PARTIALLY IMPLEMENTED - d6511b8e5 on HEAD, landed 2026-09-07 (was: uncommitted, awaiting gate); gated by the 2026-09-09 wave (REGRESSION 492/492 on Builds/wave1-reg3); owner felt-test closes: the two consequential views (queue spend, redeem grant) and ArmyMuster are VM-routed; see RESULT for the remaining views (was: READY TO IMPLEMENT)
**Silo:** Architecture / views. Eight view files; `ManageScreenPanel` is the pattern to copy.
**Source:** read-only audit fleet 2026-09-06 (CLI seat), minted from the banner
(`CLI_LANES_WO_NUMBERS.md`, main line 1512 -> 1513 in the same edit).

## 1. EVIDENCE

`docs/ARCHITECTURE_PRINCIPLES.md` (HP B2B): presentation is a separate layer that NEVER touches the objects.
Eight views touch them:

```
ArmyMusterPanel.cs:55, 473-475, 514, 532     the VIEW owns the model
ObsidianQueueHud.cs:397, 410, 428            SPENDS CURRENCY directly, against its own
                                             WO-864 "dumb skin" contract
RedeemCodePanel.cs:290
CosmeticShopPanel.cs:198
AdminOverlay.cs:806-869, 1027                GRANTS CURRENCY under an #if that includes TESTER_BUILD
```

The last one is the sharp edge: a currency grant reachable in a TESTER build is reachable by a tester, and
the pay path is live (memory `published-but-payments-never-activated`).

`ManageScreenPanel` is the clean pattern in the same tree - verbs go through the VM's `Activate`.

## 2. FIX SHAPE

- Each mutation becomes a VM COMMAND; the view calls it and renders the result. Copy `ManageScreenPanel`'s
  shape rather than inventing a new one.
- `ObsidianQueueHud` stops spending; it asks the queue service, which already owns the basket (WO-911 Q1).
- The `AdminOverlay` grant must NOT be reachable under `TESTER_BUILD` alone - narrow the `#if` to a
  developer-only symbol.

## 3. WHAT NOT TO DO
- Do not do all eight in one commit. `ArmyMusterPanel` and `ObsidianQueueHud` are the two with real
  consequences; sequence the rest behind them.

## 4. ACCEPTANCE
- [ ] The `AdminOverlay` grant unreachable in a TESTER build; proven by building with the symbol and showing
      the door absent.
- [ ] `ObsidianQueueHud` no longer spends; the queue service does. File:line in the RESULT.
- [ ] A regression fails when a `*Panel`/`*Hud` file mutates economy state directly.
- [ ] `REGRESSION_OK n/n` on a fresh log.

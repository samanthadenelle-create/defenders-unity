# WO-1461: raid loot settles to a Raid Cache, never LOST; repeat clears pay 60%

**Status:** IMPLEMENTED - awaiting gate (2026-09-09 lane SPOILS)
**Prior status:** READY TO IMPLEMENT - carries owner rulings 2026-09-06 20:33
**RESULT:** `WorkOrders/WORK_ORDER_1461_three_star_raid_clear_banks_25_of_1800_wood.RESULT.md`
⚠ **Two acceptance items are NOT delivered and are named in the RESULT:** the cache CLAIM door
(`PanelId`) and the one-line settle wiring in `RaidVictoryController.cs`, both outside this lane's
files. `SpoilsAreBankableRegression` is deliberately **RED** on the settle wiring until that line lands.
**Silo:** raid reward settle + `RaidDeployScreen` spoils line + Core/Economy bank, beside the WO-1434
pending-retention stores.
**Source:** read-only audit fleet 2026-09-06 (CLI seat), minted from the banner
(`CLI_LANES_WO_NUMBERS.md`, main line 1461 -> 1462 in the same edit). **RULINGS ADDED 2026-09-06 20:33** from
her review of `docs/RAID_BALANCE_AUDIT_2026-09-06.md`.

## 1. EVIDENCE

`troop-ai-blind-2026-09-06.log`:

```
14:37:40.331  loot settled ... 1800w 1100i
              repeat-clear multiplier x0.25 -> 450w / 275i
14:37:40.333  [Flow:Bank] BANK FULL [Grant] Wood: requested 450, banked 25, LOST 425
```

The deploy screen for that same camp reads `Spoils: ~1800 wood, ~1100 iron`. So 1,800 was promised and 25
banked - the promise ignores both the repeat multiplier and the cap, and the remainder is burned, against the
WO-1434 law that capped yield is recoverable.

## 2. THE OWNER'S RULINGS (2026-09-06 20:33)

**On overflow, verbatim:**

> "Never destroy raid loot because storage is full. Put overflow into a temporary Raid Cache with a modest
> cap... A message like 1,775 Wood held in Raid Cache - storage full turns frustration into a progression
> prompt."

**On the repeat penalty, verbatim:**

> "100% first clear after cooldown, 60% repeat clear during the same cycle, then reset to 100% when the camp's
> cooldown expires."

## 3. FIX SHAPE

- A **`RaidCache` store** with a MODEST authored cap - one authority, sitting beside the WO-1434 pending
  stores and obeying the same retention law. Not a second retention mechanism.
- Cap authored in the raid loot tunables, never a literal.
- A **CLAIM door** on the welcome-back / harvest surfaces, so the cache is claimable after the player upgrades
  or spends. This is the "progression prompt" half of her ruling - a cache with no door is just a slower burn.
- `repeatClearMultiplier` **0.25 -> 0.60** in the tunables, and it RESETS to 1.00 when the camp's cooldown
  expires.
- The deploy card quotes what will BANK **and** what will CACHE.
- Keep the `[Flow:Bank]` line; `LOST` becomes the cached amount so the log stops asserting a burn.

## 4. WHAT NOT TO DO
- Do not raise the bank cap to make the number fit. The cap is the progression (memory `stockpiles-cap-capacity`).
- Do not make the Raid Cache unbounded - she said "modest cap". An infinite cache removes the upgrade pressure
  the cache exists to create.
- Do not add a third pending store.

## 5. ACCEPTANCE
- [ ] Deploy card figure equals banked + cached for a repeat clear against a full bank.
- [ ] Regression: full bank + 3-star repeat clear -> nothing burned; the cache carries the remainder to its cap.
- [ ] Regression: first clear after cooldown pays 100%; a repeat in the same cycle pays 60%; the multiplier
      resets to 100% when the cooldown expires.
- [ ] The cache CLAIM door opens a registered `PanelId`.
- [ ] `REGRESSION_OK n/n` on a fresh log; the deploy PNG opened.

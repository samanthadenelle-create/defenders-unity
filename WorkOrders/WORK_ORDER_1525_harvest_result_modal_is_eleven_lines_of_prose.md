# WO-1525: the Harvest Result modal is eleven lines of prose - make it three rows, a bar, and one action each

**Status:** IMPLEMENTED - c0c30f715 on HEAD, landed 2026-09-07 (was: uncommitted, awaiting gate); gated by the 2026-09-09 wave (REGRESSION 492/492 on Builds/wave1-reg3); owner felt-test closes
**Silo:** `Assets/_Modules/Core/UI/HarvestOverflowModal.cs` + the copy composer in `OfflineHarvestService` /
`HarvestResultCopy`. WO-1279 is the prior; WO-1370 CLOSED tonight on her Pass covers READABILITY, not this
SHAPE.
**Source:** read-only audit fleet 2026-09-06 (CLI seat), minted from the banner
(`CLI_LANES_WO_NUMBERS.md`, main line 1525 -> 1526 in the same edit).

## 1. EVIDENCE

Owner ask, verbatim:

> "Harvest results feel off and way to much to read, needs organized and visually pleasing"

Device frame `Logs/device/screens/owner-screen-20260906-202933.png` (build 358574, 20:29). Title
`* HARVEST RESULT`, then FOUR prose lines per resource, three times over, and one CLOSE:

```
Stone storage: 3000 / 3000 (full)
Collected: 0 of 32307 from your collectors | Uncollected: 32307
Those 32307 stone are still waiting in your collectors - nothing was lost.
Stone storage 3000 is full. Spend stone, or upgrade a Stoneyard, then collect again.
```

and the same shape for Wood (2,814 of 26,167 collected; 23,353 waiting; 26,000/26,000 full) and Iron (792 of
13,083; 12,291 waiting; 10,000/10,000 full). Eleven lines to say three numbers three times.

## 2. FIX SHAPE

One ROW per resource, composed by the VM:

- resource icon + name;
- **the big number is what BANKED** (`+2,814`);
- the second number is what WAITS (`23,353 waiting, safe`);
- a storage bar with the figures (`18,000 / 18,000`) and the word **FULL** - the word, never hue alone;
- **one action chip per full row, a DOOR through `PanelRouter`**: `UPGRADE LUMBERYARD` / `BUILD STONEYARD` /
  `SPEND`.

`nothing was lost` becomes a SINGLE footer line, once, not once per resource. No row is longer than two lines.
Keep the WO-1434 law words: **WAITING, never LOST**.

## 2B. FLAG FOR THE ECONOMY LANE (not this ticket's fix)

The stone row reads **0 collected of 32,307, against a 3,000 cap** - because she has **no Stoneyard built**.
Stone collectors are producing ten times the base cap into a store nothing can raise yet, so a player's first
harvest is a wall with no door. The audit called this "worth a felt-check"; **this frame IS the felt-check**.

Route that to the economy lane as its own ticket. Do not re-tune caps inside a UI change
(memory `stockpiles-cap-capacity`: the caps are the progression).

## 3. WHAT NOT TO DO
- Do not shorten by DELETING the waiting figure. That number is the WO-1434 reassurance and it is the reason
  the screen is trusted.
- Do not carry state in colour. The owner is red/green colourblind: FULL is a word.

## 4. ACCEPTANCE
- [ ] Measured layout case: three rows fit at 2670x1200 AND 1920x1080 with no ellipsis.
- [ ] Every FULL row carries a door to a REGISTERED `PanelId`.
- [ ] The composer emits no repeated sentence; `nothing was lost` appears exactly once.
- [ ] Headless `HarvestOverflow_*.png` captured and OPENED; a greyscale copy still reads.
- [ ] `REGRESSION_OK n/n` on a fresh log.

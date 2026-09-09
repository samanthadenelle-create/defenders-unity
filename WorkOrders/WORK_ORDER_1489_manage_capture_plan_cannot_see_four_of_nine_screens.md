# WO-1489: the Manage capture plan cannot SEE four of the nine mockup screens, and two frames are CAPTURE_LEDGER_MISSING

**Status:** CLOSED 2026-09-08 - owner felt-test PASS (validated 2026-09-09T02:10:52, build 2026.09.08.361259). PRIOR STATUS: FIXED - implemented in the 2026-09-07 afternoon gate wave (COMPILE_GATE_OK Builds/cg-wave10h.log, REGRESSION_OK 454/454 Builds/reg-wave10d.log 13:05); reaches the Seeker with the next tester build; owner felt-test closes it. PRIOR STATUS: READY TO IMPLEMENT
**Silo:** Manage 2000-block (WO-2016, the capture plan) + `ManageWorkspacePanel` + a sprite asset.
**Source:** read-only audit fleet 2026-09-06 (CLI seat), minted from the banner
(`CLI_LANES_WO_NUMBERS.md`, main line 1489 -> 1490 in the same edit).

> ## MEASURED AT HEAD 2026-09-07 (implementation lane) - THREE OF THE FOUR ITEMS WERE ALREADY CLOSED, AND ONE PIECE OF EVIDENCE BELOW IS A MISREAD
>
> Source: `Builds/cap-manage-wave5c.log` (03:20) + the sixteen `Builds/ui-capture/ManageFlow_*.png` it wrote,
> read this session. Nothing below is inferred from a doc.
>
> 1. **The two `CAPTURE_LEDGER_MISSING` lines are gone.** That run printed
>    `CAPTURE_LEDGER_SWEPT MANAGE_FLOW_MAP deleted=16 expected=16` and
>    `MANAGE_FLOW_MAP_OK 16 frames`, with zero MISSING and zero DUPLICATE.
>    `ManageFlow_ARMY_max` is present (`MANAGE_FLOW_STATE ARMY/max -> troop-footman state=Max`).
>    `ManageFlow_BUILD_locked` is **not missing - it is excluded on purpose** by the WO-1516 owner
>    ruling, in the plan at `UICaptureLaunch.cs` `BuildManageFlowPlan` (the BUILD grid is
>    unlocked-only by construction, and `ManageProgressiveDisclosureRegression`
>    `[build-grid-is-unlocked-only]` FAILS if a Locked tile returns there).
> 2. **The ledger already FAILS rather than logging.** `CAPTURE_LEDGER_MISSING` is a `LogError`,
>    `EndCaptureLedger` returns a failure count, and `MANAGE_FLOW_MAP_OK` is gated on `ledger == 0`.
>    Section 2's third bullet was already satisfied; it is now PINNED by
>    `UiCaptureFidelityRegression` `[capture-ledger]`.
> 3. ⛔ **"`ManageFlow_BUILD_max` shows NO before/after stats, no cost row, no time and no UPGRADE
>    button" IS NOT A DEFECT.** The PNG was opened: it is the **forge at Level 4 of 4** with the MAX
>    badge (`MANAGE_FLOW_STATE BUILD/max -> forge state=Max`). A maxed item has no next level to
>    price, so those painters are correctly unreached. The real gap is the one this ticket named in
>    its own fix shape and then measured against the wrong frame: **the plan had no ACTIONABLE
>    detail frame at all**, because it shot only Locked and Max - the two states that by definition
>    carry no action.
> 4. **The catapult alpha checkerboard does not reproduce.**
>    `Assets/Resources/Portraits/Buildings/tower_catapult.png` was opened: opaque art on a
>    transparent ground, and its `.meta` import block (`textureType: 8`, `spriteMode: 1`,
>    `alphaUsage: 1`, `alphaIsTransparency: 1`) is byte-identical in those fields to `forge.png`,
>    which renders perfectly in `ManageFlow_BUILD_max`. No catapult tile appears in any 03:20 frame,
>    so there is no frame at HEAD showing the reported checkerboard. **No import change was made.**
>    ⚠ ART ASK, and it is a different defect: that source PNG has the generator's own filename text
>    (`...ding catapult...`) baked across its bottom third. Named, not faked.
>
> **What the lane changed:** the plan grew `ActionDetail` (per tab) and `HubHeart`, 16 -> 20 frames.
> Root cause of the unreachable actionable screen, measured: `SeedManageFlowExtraQueue` seats every
> channel AT the depth cap so the queue drawer can document a full line, and a full line makes every
> actionable tile `QueueBlocked` - `ManageFlow_BUILD_gridtop` reads QUEUE FULL on every built tile.
> **Status stays as it is. Ruling 29: no Manage frame is DONE until the owner judges a DEVICE frame
> against its mockup panel.**

## 1. EVIDENCE

Two frames never captured at all:

```
ManageFlow_BUILD_locked      CAPTURE_LEDGER_MISSING
ManageFlow_ARMY_max          CAPTURE_LEDGER_MISSING
```

And `ManageFlow_BUILD_max` shows NO before/after stats, no cost row, no time and no UPGRADE button - although
every painter exists:

```
ManageWorkspacePanel.cs:1006-1016   before/after stats
ManageWorkspacePanel.cs:1110-1115   cost row
ManageWorkspacePanel.cs:1140-1142   time / upgrade action
```

So the plan is not reaching the states that would paint them. The catapult sprite also renders an ALPHA
CHECKERBOARD in the captured frame.

Net: four of the nine mockup screens (hub, upgradeable detail, trainable detail) are not observable by the
capture plan, so "all nine match" is unprovable - not wrong, unprovable.

## 2. FIX SHAPE

- Extend `BuildManageFlowPlan` to drive the four unreachable states: the hub, an upgradeable detail with a
  next level, a trainable detail, and the locked/max variants.
- Fix the catapult sprite's alpha (re-import or re-author) so the frame shows the art, not the checkerboard.
- The ledger must FAIL on a planned frame that goes missing, rather than logging it.

## 3. WHAT NOT TO DO
- Do not re-assert "all nine screens match" until the plan covers nine and every frame is present in the
  ledger. The capture-round count is not evidence.

## 4. ACCEPTANCE
- [ ] Nine frames present, zero `CAPTURE_LEDGER_MISSING`, on a fresh run.
- [ ] `ManageFlow_BUILD_max` shows stats, cost, time and the UPGRADE button; PNG opened.
- [ ] Catapult sprite renders opaque.
- [ ] `REGRESSION_OK n/n` on a fresh log.

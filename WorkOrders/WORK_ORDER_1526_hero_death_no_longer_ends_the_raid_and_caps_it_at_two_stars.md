# WO-1526: hero death no longer ends the raid - the army fights on, capped at 2 stars

**Status:** CLOSED 2026-09-08 - owner felt-test PASS (validated 2026-09-09T02:08:56, build 2026.09.08.361259). PRIOR STATUS: FIXED - implemented in the 2026-09-07 afternoon gate wave (COMPILE_GATE_OK Builds/cg-wave10h.log, REGRESSION_OK 454/454 Builds/reg-wave10d.log 13:05); reaches the Seeker with the next tester build; owner felt-test closes it. PRIOR STATUS: READY TO IMPLEMENT - owner ruling 2026-09-06 20:33
**Silo:** Village/Troops - `RaidScoring` + the hero-death settle in `RaidDeployController` /
`HeroDeathEndState`'s raid branch.
**Source:** read-only audit fleet 2026-09-06 (CLI seat), minted from the banner
(`CLI_LANES_WO_NUMBERS.md`, main line 1526 -> 1527 in the same edit). From her review of
`docs/RAID_BALANCE_AUDIT_2026-09-06.md`.

## 1. EVIDENCE

Owner ruling, verbatim:

> "Do not let hero death instantly terminate the raid... let the raid continue, but cap the result at 2 stars
> if the hero dies. That makes hero survival matter without turning the hero into a giant red self-destruct
> button."

Today it terminates. `raid-no-abilities-2026-09-06.log`, 12:59:47:

```
[Flow:Raid] hero death settle: partial loot for 32% razed
```

45 seconds into a 180-second raid, with a full army still alive on the field, the raid ended. Combined with
WO-1520 (the hero spawns inside defender range with the clock running) the player loses the raid before they
have deployed.

## 2. FIX SHAPE

- On hero death IN A RAID the hero EVACUATES through the existing EVAC branch, and the raid **keeps running**
  to clear, retreat, or the clock.
- `RaidScoring` records `heroDied` and **clamps stars to 2**.
- The HUD says `HERO DOWN - your army fights on`, composed by the VM.

## 3. WHAT NOT TO DO
- Do not let the hero respawn mid-raid. The ruling is that survival still matters - the 2-star clamp IS the
  cost, and a respawn removes it.
- Do not settle loot at the moment of death. Loot settles when the raid actually ends.

## 4. ACCEPTANCE
- [ ] Regression: a raid with `heroDied` AND a full clear settles exactly **2 stars**. RED today - state that
      proof in-file.
- [ ] Regression: hero death does not settle loot or end the raid; the army continues to act.
- [ ] The `HERO DOWN - your army fights on` line comes from the VM, not the View.
- [ ] A captured device raid where the hero dies and the army finishes the camp.
- [ ] `REGRESSION_OK n/n` on a fresh log.

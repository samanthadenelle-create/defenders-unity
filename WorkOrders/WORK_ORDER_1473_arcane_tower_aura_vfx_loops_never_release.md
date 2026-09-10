# WO-1473: ArcaneTower_Aura VFX loops never release - 14 of 24 loop slots held; the WO-1057 release policy was never built

**Status:** IMPLEMENTED - d6511b8e5 on HEAD, landed 2026-09-07 (was: uncommitted, awaiting gate); gated by the 2026-09-09 wave (REGRESSION 492/492 on Builds/wave1-reg3); owner felt-test closes (was: IN PROGRESS - lane handed back edits 2026-09-06 (uncommitted, awaiting the wave-two compile + regression gate); prior: READY TO IMPLEMENT)
**Silo:** `Assets/_Modules/VFX/` loop pool + `ArcaneTower_Aura` owner lifecycle.
**Source:** read-only audit fleet 2026-09-06 (CLI seat), minted from the banner
(`CLI_LANES_WO_NUMBERS.md`, main line 1473 -> 1474 in the same edit).

## 1. EVIDENCE

Device log, 20 occurrences:

```
STUCK LOOP ArcaneTower_Aura owner='Arcane Spire'#N age=303s ... 14/24
```

Fourteen of the twenty-four loop slots are held by one effect, aged over five minutes. WO-1057 specified a
release policy for exactly this; the diagnostic that DETECTS the stuck loop was built and the policy that
RELEASES it was not, so the pool has been reporting its own exhaustion ever since.

The pool then PINS at 24/24 in raids and starts dropping combat feedback (`raid-stuck-2026-09-06.log`):

```
Damage_Ruin skipped        31x
oneshot cap hit            76x
```

The code itself asks in a comment: *"Counter-leak or too-low cap?"* - so the pool is dropping the player's
hit feedback while it waits on loops that will never return. Two other loops are also stuck
(`TreeofLifeAura_Aura`, `atfootprintoftree_Aura`, x2 each - see WO-1476).

This is a named suspect in the frame-floor ticket (WO-1459) and the heap-growth ticket (WO-1484).

## 2. FIX SHAPE

- Build the release policy: a loop releases when its owner is destroyed, or when the owner leaves the camera
  frustum for longer than a short grace, whichever comes first. One policy in the pool, not per-effect code.
- Keep the `STUCK LOOP` diagnostic; it becomes the oracle that proves the policy works.
- Regression: spawn N aura owners, destroy them, assert the pool returns to zero held slots.

## 3. WHAT NOT TO DO
- Do not raise the pool ceiling from 24. That defers exhaustion; it does not release anything.
- Do not special-case `ArcaneTower_Aura`; any looping effect with a destroyable owner has this shape.

## 4. ACCEPTANCE
- [ ] Zero `STUCK LOOP` lines in a full town + raid session.
- [ ] Pool-drain regression, RED proof stated.
- [ ] `REGRESSION_OK n/n` on a fresh log.

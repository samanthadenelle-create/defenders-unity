# WO-1467: two suites pin a 4-face action-bar model that is never bound; the shipped 5-face dock has only a lint

**Status:** CLOSED 2026-09-08 - owner felt-test PASS (validated 2026-09-09T02:10:53, build 2026.09.08.361259). PRIOR STATUS: FIXED - implemented in the 2026-09-07 afternoon gate wave (COMPILE_GATE_OK Builds/cg-wave10h.log, REGRESSION_OK 454/454 Builds/reg-wave10d.log 13:05); reaches the Seeker with the next tester build; owner felt-test closes it. PRIOR STATUS: READY TO IMPLEMENT
**Silo:** `HudKitController` + `HudLabelFitRegression` + `SessionShapeRegression` + `HudActionBarRegression`
+ `HudActionBarModel` docstring + `CLAUDE.md` sec.7.
**Source:** read-only audit fleet 2026-09-06 (CLI seat), minted from the banner
(`CLI_LANES_WO_NUMBERS.md`, main line 1467 -> 1468 in the same edit).

## 1. EVIDENCE

```
HudKitController.cs:2978-2986        never binds HudActionBarModel when the peaceful dock exists
HudLabelFitRegression.cs:266-269     fails if MaxVisibleFaces != 4, "locked to Build/Hero/Journey/Manage"
SessionShapeRegression.cs:232-234    same pin
HudActionBarModel.cs:116-119         docstring says SIX
HudActionBarRegression.cs:291,300    says "four-medallion" while linting
HudActionBarRegression.cs:239        BuildPeacefulDockSlot(1, "TALK")
```

The dock the player sees shows FIVE faces: BUILD / TALK / HERO / JOURNEY / MANAGE.

So two suites pin a model nothing binds, their failure message names a four-face bar that does not exist, the
docstring names a six-face bar that does not exist either, and the bar that DOES ship is covered only by
source-text lint. CLAUDE.md sec.7's frozen note describes the deleted path.

## 2. FIX SHAPE

- Write ONE measured oracle against the LIVE peaceful dock: face count, face ids, and their order, read from
  the built visual tree rather than from source text.
- Retire the two `MaxVisibleFaces != 4` pins, or re-point them at whatever still binds the model; do not
  leave a pin on an unbound path.
- Fix the `HudActionBarModel` docstring to describe the code, and correct CLAUDE.md sec.7's frozen note in the
  SAME commit (canon-in-the-same-breath, sec.15).

## 3. WHAT NOT TO DO
- Do not write a new face count into CLAUDE.md. Sec.7 says explicitly: do not restate the constant, point at it.
- Do not change `MaxVisibleFaces` to 5 to satisfy the pins; the constant is not what the player sees.

## 4. ACCEPTANCE
- [ ] One measured dock oracle exists; RED proof stated by removing a face locally.
- [ ] The two stale pins retired or re-pointed; failure messages match reality.
- [ ] Docstring and CLAUDE.md sec.7 corrected in the same commit.
- [ ] `REGRESSION_OK n/n` on a fresh log.

# WO-1538: the arena projectile TRAIL fire-point is never reached

**Status:** IMPLEMENTED - c0c30f715 on HEAD, landed 2026-09-07 (was: uncommitted, awaiting gate); gated by the 2026-09-09 wave (REGRESSION 492/492 on Builds/wave1-reg3); owner felt-test closes
**Silo:** Village/Arena VFX + `ArenaCombatOracle`.
**Source:** wave-two regression `Builds/reg-wave2.log` (422/435), 2026-09-06. Surfaced by `ArenaCombatOracle`,
**registered tonight by WO-1496** - a pre-existing gap becoming visible. Minted from the banner
(`CLI_LANES_WO_NUMBERS.md`, main line 1538 -> 1541 in the same edit).

## 1. EVIDENCE

```
ARENA COMBAT: 1 failure(s):
  TRAIL: missing FlowTrace 'TRAIL color=... rarity=uncommon applied' (trail fire-point not reached)
```

The oracle asserts a trace line that never appears. Two readings, and the ticket does not pick between them:

- **the trail never applies** - a real VFX defect, and the projectile has been flying bare;
- **the oracle's fixture never reaches the shot** - the test does not fire, and the assertion is unreachable.

Both are worth fixing and they need opposite changes, so the diagnosis comes first.

## 2. FIX SHAPE

- Instrument or step the fixture to establish whether the shot is fired at all. Read which of the two it is
  before changing either the VFX or the oracle (CLAUDE.md sec.12).
- If the trail genuinely never applies: fix the fire-point, keep the trace.
- If the fixture never shoots: fix the fixture so the assertion is reachable, and say so - an oracle that
  cannot reach its own assertion is a false green everywhere else it is used.

## 3. WHAT NOT TO DO
- Do not delete the assertion to clear the failure. It is the only thing that noticed.
- Do not add the trace line somewhere it will always print; that turns a real check into a tautology.

## 4. ACCEPTANCE
- [ ] The RESULT names which of the two causes it was, with the captured evidence.
- [ ] `ArenaCombatOracle` reports zero failures, with the assertion genuinely reached.
- [ ] `REGRESSION_OK n/n` on a fresh log.

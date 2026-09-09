# WO-1535: finish the enemy stat-table SSOT migration - 12 of 13 raid garrison ids are still hardcoded

**Status:** CLOSED 2026-09-08 - owner felt-test PASS (validated 2026-09-09T02:08:55, build 2026.09.08.361259). PRIOR STATUS: FIXED - implemented in the 2026-09-07 afternoon gate wave (COMPILE_GATE_OK Builds/cg-wave10h.log, REGRESSION_OK 454/454 Builds/reg-wave10d.log 13:05); reaches the Seeker with the next tester build; owner felt-test closes it. PRIOR STATUS: READY TO IMPLEMENT - **SEQUENCE AFTER WO-1530** (the enemy scaling measurement).
**Silo:** Village/Enemies - `GarrisonStatBlocks`, `WildlandsRoster`, `enemies.json`, `CombatAtbRegression`.
**Source:** read-only audit fleet 2026-09-06 (CLI seat), minted from the banner
(`CLI_LANES_WO_NUMBERS.md`, main line 1535 -> 1537 in the same edit; **drafted as 1532 and renumbered** -
the command-centre SKU lane held 1532 on disk and on the banner first).

## 1. EVIDENCE

There are **three** stat tables per enemy id today:

```
Assets/StreamingAssets/Data/Canonical/enemies.json     the authored source
GarrisonStatBlocks.cs:116-141   BuildTypedDef          12 of 13 raid garrison ids HARDCODED
WildlandsRoster.Fallback                               a third copy
```

The town path reads `enemies.json` only; the raid path reads the hardcoded block. They have already diverged:

```
raid Berserker   260 health / 13 damage
authored          117 health / 10 damage
```

The migration is not speculative - **`orc-raider` was already done**, and it carries the oracle that proves it:

```
WildlandsRoster.cs:126-135   BaseDef
CombatAtbRegression          Check H (the divergence oracle)
```

So the pattern, the destination and the guard all exist. Twelve ids were left behind.

## 2. FIX SHAPE

- Migrate the remaining 12 to the SSOT exactly as `orc-raider` was, and extend `CombatAtbRegression` Check H to
  cover each one, so a fourth copy can never reappear silently.
- Delete `BuildTypedDef`'s hardcoded block and the `WildlandsRoster.Fallback` duplicates once every id resolves
  from the authored table.

## 3. WHAT NOT TO DO
- **Change NO number until WO-1530 records the scaling formula.** The 260-vs-117 gap may be the raid path
  compensating for scaling that WO-1530 has not yet measured. Migrating the plumbing is safe; picking which of
  the two numbers is "right" before the formula is written down is exactly the guess that ticket exists to
  prevent.
- Do not migrate by copying the hardcoded values into `enemies.json`. That preserves the divergence and calls
  it authored.

## 4. ACCEPTANCE
- [ ] All 13 garrison ids resolve from `enemies.json`; the hardcoded block and the fallback duplicates deleted.
- [ ] `CombatAtbRegression` Check H covers all 13; RED proof stated by re-adding one hardcoded value.
- [ ] The RESULT states, per id, whether its effective stats CHANGED and by how much - a migration that
      silently retunes 12 enemies is a balance change wearing a refactor's clothes.
- [ ] `REGRESSION_OK n/n` on a fresh log.

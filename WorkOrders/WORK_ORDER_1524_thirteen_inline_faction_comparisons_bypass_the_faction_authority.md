# WO-1524: thirteen inline faction comparisons still bypass CombatFactionRules

**Status:** PARTIALLY IMPLEMENTED - d6511b8e5 on HEAD, landed 2026-09-07 (was: uncommitted, awaiting gate); gated by the 2026-09-09 wave (REGRESSION 492/492 on Builds/wave1-reg3); owner felt-test closes (was: IN PROGRESS - lane handed back edits 2026-09-06 (uncommitted, awaiting the wave-two compile + regression gate); prior: READY TO IMPLEMENT)
**Silo:** Village/Combat faction seam - pets, towers, tower combat, the ability lane, hero abilities.
**Source:** carried forward from **WO-1503**, whose implementation lane found these while proving that
ticket's premise false. Minted from the banner (`CLI_LANES_WO_NUMBERS.md`, main line 1524 -> 1525 in the
same edit).

## 1. EVIDENCE

`CombatFactionRules` is the single faction authority, and `PlayerAttackController.cs:689` was routed through
it under WO-1503. Thirteen call sites still compare faction inline:

```
Pet.cs:556, 635
ArcaneTower:386, 397
DefenseTower:717, 730
TowerCombat:229, 243, 283, 294
PlayerAttackController:597    (the ABILITY lane - the melee lane was fixed, this one was not)
HeroAbilities:3097, 3125
```

Each is a second authority on "may this thing attack that thing". WO-1503 is the cautionary case: an hour went
into a P0 that did not exist, and the reason it was even plausible is that faction logic is scattered enough
that nobody could say from one read whether the hub root was classified or not.

Note the asymmetry inside one file: `PlayerAttackController`'s melee path now goes through the authority and
its ability path at `:597` does not. That is exactly the divergence a single authority exists to prevent.

## 2. FIX SHAPE

- Route all thirteen through `CombatFactionRules.MayAttack`. Mechanical; no behaviour change intended - if any
  site changes behaviour, that is a finding to report, not to absorb silently.
- One SOURCE case that fails if any inline `Faction != CombatFaction.Hostile` (or its equivalents) remains
  outside `CombatFactionRules`. That case is the durable half; the thirteen edits are today's instance.

## 3. WHAT NOT TO DO
- Do not add per-site exceptions to `MayAttack` to preserve a quirk. If a site genuinely needs different
  behaviour, that belongs in the rule as a named case, not as inline code.

## 4. ACCEPTANCE
- [ ] All thirteen routed; the file:line list re-verified at source in the RESULT (they may have moved).
- [ ] The no-inline-faction source case exists; RED proof stated by re-adding one comparison.
- [ ] Behaviour unchanged: pets, towers and hero abilities still engage the same targets in a captured
      town wave and a captured raid.
- [ ] `REGRESSION_OK n/n` on a fresh log.

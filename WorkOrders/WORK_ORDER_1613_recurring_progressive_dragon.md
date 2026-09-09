# WO-1613 - Dragon spawns on wave 20 and every five waves thereafter

**Status:** FIXED — implemented and regression-gated for owner verification in the next Windows/APK build

## Definitive RCA

The player reached defense wave 20. `Player-prev.log` then recorded an `InvalidKeyException`: address
`Enemies/Boss_Dragon` was published as `UnityEngine.GameObject`, but WaveManager requested
`DragonBoss`. The address existed; the requested type was wrong, so the headline boss never spawned.

## Resolution

- Load the Addressable as `GameObject`, then resolve `DragonBoss` from the prefab.
- Hold wave-clear while that asynchronous prefab load is pending.
- Spawn Syndrath on waves 20, 25, 30, 35, ...; suppress the old off-cadence endless replay at wave 37.
- Each return gains 25% base HP, 12% damage, and 5% faster attack intervals; telegraph intervals floor
  at 65% of their authored duration.

## Evidence

`APEX_DRAGON_SPAWN_OK` proves the type contract, pending clear gate, exact cadence, off-cadence
refusals, increasing HP/damage, and shortening attack interval.

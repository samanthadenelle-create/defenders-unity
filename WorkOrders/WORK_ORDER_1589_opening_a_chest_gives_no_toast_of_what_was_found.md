# WORK ORDER 1589 - Opening a chest gives no toast saying what was found; the loot goes to a world mote the player has to notice

**Status:** CLOSED 2026-09-08 - owner felt-test PASS (validated 2026-09-09T02:10:49, build 2026.09.08.361259). PRIOR STATUS: FIXED - implemented in the 2026-09-07 gate wave (COMPILE_GATE_OK Builds/cg-wave9.log 10:40, REGRESSION_OK 446/446 Builds/reg-wave9.log 11:02); reaches the Seeker with the next tester build; owner felt-test closes it. PRIOR STATUS: READY TO IMPLEMENT - minted 2026-09-07 (CLI) from the owner's words
**Silo / Lane:** Village/World loot - `Assets/_Modules/Village/World/BreakableContainer.cs` (the `[Flow:Loot]` producer), the reward-toast seam used by kills (`Enemy.cs:3625` "KILL REWARD TOAST ... routed=CombatText(Reward)"), the dungeon chest path
**Type:** EXISTING system, FEEDBACK GAP (WO-1296 "modal and world feedback ownership" family)
**Priority:** P2

## Owner, verbatim (2026-09-07 09:36, Seeker, in `dg_sunken_vault`)

> "when i open a chest no toast to what i found"

## Evidence (device log, `adb logcat -d -s Unity`, 2026-09-07)

```
09:35:34.078 [Flow:Loot] Chest_crate opened -> dropped 2 loot line(s) as a world mote (table 'crate-common')
09:35:36.634 [Flow:VFXManager] PlayKey('Treasure_Aura') -> prefab 'Loot_iddle'
09:35:59.574 [Flow:Reward] KILL GRANT id=hollow-rogue ... creditedXp=17 creditedGold=7 ...
09:35:59.575 [Flow:Reward] KILL REWARD TOAST '+17 XP  +7 gold' id=hollow-rogue routed=CombatText(Reward) at (1.98, -12.00, 87.25)
```

The chest path ends at "dropped ... as a world mote" - two loot lines become a pickup in the world and
nothing is said. The kill path, 25 seconds later, DOES toast through `CombatText(Reward)`. Same session,
same screen, two feedback rules. The owner's standard for grants is WO-1225 ("a toast rendered under a
modal is still a silent grant") and WO-1296: every grant is said, once, where the player is looking.

## What to do

- Read `BreakableContainer.cs` at the `[Flow:Loot]` line and the mote pickup path; find the moment the
  loot actually BANKS (the pickup, not the drop - toasting at the drop would claim what is not yet held).
- At the bank moment, route the same reward toast the kill path uses (`CombatText(Reward)` seam, ONE
  producer - do not build a second toast) with the loot lines' labels and counts ("+1 Oil Flask  +1
  Tattered Cloth"); add `FlowTrace.Step("Loot", "CHEST REWARD TOAST '...' routed=...")` mirroring the
  kill line so the device log proves it.
- If the mote is never picked up (walked past), nothing toasts - correct; the world mote stays.
- Regression: a chest open + pickup fixture asserts one toast with every loot line named; a chest open
  WITHOUT pickup asserts zero toasts.

## Not to touch
- Loot tables (`crate-common`), drop counts, the dungeon oil/field systems.

## Acceptance
- Device log after the fix: `CHEST REWARD TOAST` follows the pickup, once per chest.
- Regression green, REGRESSION_OK n/n on a fresh log. Owner felt-test closes.

# WORK ORDER 1750 — Hero is DEAD by the health model (`HeroHealth.IsAlive=false`) but still controllable and fighting in a raid; HP bar renders empty

**Status:** IMPLEMENTED, NOT YET GATED
**Minted:** 2026-09-15 by the lead, from the owner's felt-test on the Seeker (tester build `2026.09.15.371127`, scene `RaidBase_IronBastion`).
**Silo:** `Assets/_Modules/Village/Hero/HeroHealth.cs` (death path) + the raid-side death handler (`RaidVictoryController` / raid defeat flow — the lane finds the owner of "hero died in a raid" and cites it). ⛔ NOT `RaidAssaultAi.cs` / `TroopController.cs` (WO-1746), NOT `TroopFactory.cs` (WO-1748), NOT `RaidBaseDresser.cs` / `RaidNavBake.cs` (WO-1749).

## Evidence (captured, not theorised)
- Owner screenshot `Screenshot_20260915-134632.png` (pulled from `/sdcard/Pictures/Screenshots/`, copy in the lead's scratchpad): Grom Lv15, top-left HP bar has NO red fill (mana bar full), hero mid-swing on a `Hollow Acolyte (Lv18) 102/332 LOCKING`, raid clock 1:35, Troops 7/8. A second shot at 13:53:55 (Thrain Lv15) shows the same empty HP bar while playing.
- Device logcat, same seconds: `09-15 13:46:29.381 [Flow:EnemyAggro] raidboss-iron_bastion: still steered at the hero via Enemy.DriveNav/brain while HeroHealth.IsAlive=false - pursuit pulse NOT stamped and this body's own claim revoked` and again at `13:46:34.388`. The enemy brain's own guard is reporting a dead hero; the player is still moving, attacking and being targeted.
- The raid proceeded to a **Victory** (`13:55:00 [Flow:EndState] reveal completed`, auto-dismissed at 13:55:28) with the hero in this state.

## What is NOT proven
Whether HP reached 0 and the raid death handler never fired (no respawn / no defeat), or whether `IsAlive` flipped false without HP reaching 0 (a stale flag after the town→raid carry — WO-1109 carries the real hero across). The lane reads `HeroHealth` for every writer of the alive flag and the raid scene's death subscriber, and instruments the transition (one `FlowTrace.Fail` on "IsAlive=false while input still drives the hero").

## Ask
1. Instrument first (§12): trace at the moment `IsAlive` becomes false in a raid: HP value, who called, whether a death handler is subscribed; and a throttled warn while `!IsAlive && PlayerAttackController is still accepting input`.
2. Fix the proven gap: a dead hero in a raid must either be handled by the raid's defeat/respawn rule (find it, cite it — if none exists, say so, that is a design gap for the owner, not a fix to invent) or the flag must not go false.
3. Regression pinning "when HeroHealth.IsAlive is false, hero input is refused and the HUD shows the death state".

## Acceptance
- Headless raid run: zero `still steered at the hero … IsAlive=false` lines while the hero is under control; the HP bar and the alive flag agree.
- Lane flips this Status line and writes the `.RESULT.md`.

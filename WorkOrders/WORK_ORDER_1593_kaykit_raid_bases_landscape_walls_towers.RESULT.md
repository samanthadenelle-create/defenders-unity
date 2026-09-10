# WO-1593 RESULT - KayKit raid bases: landscape, walls, towers that are not pillars

**Status:** IMPLEMENTED - 6a5c7a36d on HEAD 2026-09-09 (was READY); superseded in delivery by
WO-1607 / 1608-1611; owner felt-test closes.
**Verified:** 2026-09-09, SILO 0A verify-and-flip pass, HEAD 184c8ff06, branch dev. Read-only.

## 1. How this ticket was delivered

1593 was the ORIGINAL art-pass ticket. Its own banner (added 2026-09-09, top of the WO) already
records the redirect: **"Q1 ANSWERED FROM DISK; IMPLEMENT VIA WO-1607-1611. Do not bake this ticket
as a second art pass on the same scenes."** The Grok honor-clock branch work referenced alongside it
(`5c3c82de2` on `grok/raid-1593-1595`) was **never merged** and is NOT what closed this.

What closed it is the same landing as WO-1607: **`6a5c7a36d`**, an ancestor of HEAD (present in
`git log --oneline -40` from HEAD; it is the sole commit returned by
`git log --oneline -8 -- Assets/Editor/Regression/RaidBaseLayoutRegression.cs`).

## 2. Proof at HEAD, file:line, mapped to 1593's own acceptance

| 1593 acceptance | Proof at HEAD |
|---|---|
| 1. Walls have thickness; a tower has a TOP, not a bare pillar | `Assets/Editor/Regression/RaidBaseLayoutRegression.cs:44` `CaseArtLoadNotResourcesStructures` - pins the generator loading through `RaidBaseDresser` and `StructureContent`, NOT `Resources.Load("Structures/...")`. That miss was the exact cause of the cylinder+cube fallback turret (the "pillar"). Kits authored per camp in `Assets/Resources/Data/Canonical/scene-configs.json:87-97` / `:163-173` / `:242-252`. |
| 2. Staging pocket outside defender range (WO-1520) | NOT proven here - see sec.5 |
| 3. Troops path to the objective, no new softlock | NOT proven here - see sec.5 |
| 4. No magenta / missing KayKit where packs are imported; missing pack -> LogWarning | Partly: the 1609/1610/1611 status lines record `missing=0` on the Easy bake. The missing-pack fallback branch was not exercised on this clone. |
| 5. `COMPILE_GATE_OK`; raid smoke still ends | NOT proven here - see sec.5 |

Per-camp kits actually authored (`scene-configs.json`, read 2026-09-09):

- Easy `raider_camp_small` -> `kit: hexagon-green`, 27 dress tokens measured by the suite.
- Hard `fortified_garrison` -> `kit: synty-castle`, `interiorWallLayers >= 1` (keep layer).
- Extreme `mage_enclave` -> `kit: dungeon-stone`.

**Note for the reader:** 1593's own 09-09 banner text says "Hard hexagon-red fortress". That is
WRONG at HEAD - WO-1607 sec.4 and the shipped data both say **synty-castle** for Hard, and
`RaidBaseLayoutRegression.CaseHardKeep` (`:110-120`) FAILS on any other kit. Do not propagate
"hexagon-red".

## 3. The suite is GREEN on a fresh log

`Builds/ready-rca-checkpoint-regression.log` (PowerShell `Select-String`; Unity logs are UTF-16):

```
[raid-base-layout] raid-base-layout easy dress tokens=27; hard keep layers=1;
  extreme kit=dungeon-stone; generator wires dresser; art load via dresser;
  spawner slots + defend post; gate width 3.5; garrison wipe wins
```

Registered at `Assets/Editor/Regression/DataRegression.cs:741`.

## 4. What the owner should felt-test

Same three raids as WO-1607, judged against 1593's original complaint:

1. Easy camp, eye level: do the walls read as having THICKNESS, and does at least one tower have a
   fighting TOP rather than silhouetting as a vertical pillar?
2. Does the ground sell a place (dirt / cobble / dungeon stone) instead of a bare plane?
3. Any magenta or obviously-missing art in any of the three camps?
4. Iron Bastion (`RaidBase_IronBastion`) is deliberately PARKED by WO-1607 sec.1 - it is on disk,
   not in build settings, and got no art pass. Confirm you are fine with that.

## 5. What is NOT proven by this pass

- **Nothing visual was captured.** No screenshot, no bake, no device frame. 1593's acceptance 1 is
  an eye-level judgement and is entirely the owner's.
- **Staging-pocket safety (acceptance 2) and troop pathing (acceptance 3) are unproven.** The suite
  is a JSON + source-text oracle; it never bakes a scene and never runs a nav query.
- **Raid smoke (acceptance 5) was not run.** `COMPILE_GATE_OK` was not produced this session.
- **The full gate is RED overall** on `Builds/ready-rca-checkpoint-regression.log`:
  `REGRESSION_FAIL: 2 failure(s) (472/474 registered suites green, 0 skipped)`. Both failures are
  unrelated to raid bases (`[hero-element-cast]` ordering; `MANAGE_BUILD_DOOR_FAIL` for pet-house /
  market / workshop, WO-2007). The raid-base suite is green on that same log.
- **The Grok branch `grok/raid-1593-1595` is still unmerged.** WO-1594's honor-clock work
  (`5c3c82de2`) is NOT on HEAD. That is a separate open ticket, not part of this closure.

# WO-1607 RESULT - Raid bases as places (program spine)

**Status:** IMPLEMENTED - 6a5c7a36d on HEAD 2026-09-09 (was READY); owner felt-test closes.
**Verified:** 2026-09-09, SILO 0A verify-and-flip pass, HEAD 184c8ff06, branch dev. Read-only: no
.cs edited, no Unity run, no commit.

## 1. Landing sha

`6a5c7a36d chore: checkpoint complete workspace and rebuild board` -
`git merge-base --is-ancestor 6a5c7a36d HEAD` = ancestor (it is in `git log --oneline -40` from HEAD).
It is the commit that introduced `Assets/Editor/Regression/RaidBaseLayoutRegression.cs`,
`Assets/Editor/WallTools/RaidBaseDresser.cs` and the `RaidBaseGenerator.cs` rework
(`git log --oneline -8 -- Assets/Editor/Regression/RaidBaseLayoutRegression.cs` returns that one sha).

1607 is a PROGRAM SPINE. It was delivered through its four children, all IMPLEMENTED at HEAD:

| WO | Status line at HEAD |
|---|---|
| 1608 engine | `IMPLEMENTED - baked 2026-09-09; PO felt-test closes` |
| 1609 Easy | `IMPLEMENTED - baked 2026-09-09 (hexagon-green, 251 dress pieces, missing=0)` |
| 1610 Hard | `IMPLEMENTED - baked 2026-09-09 (synty-castle, keep layer, 531 dress pieces)` |
| 1611 Extreme | `IMPLEMENTED - baked 2026-09-09 (dungeon-stone keep, 668 dress pieces)` |

> ⚠ **SUPERSEDED COUNTS 2026-09-10 (WO-1635, lane PROPS-CANON).** The three dress-piece counts quoted in
> the table above (251 / 531 / 668) are **the 2026-09-09 Status lines as authored** and are now stale.
> This is a frozen RESULT file, so the table is **left exactly as written** (CLAUDE.md §15). Current bake,
> `Builds/wave3-bake7`:
> - `:503` `dressed 'raider_camp_small' kit=hexagon-green placed=314 missing=0`
> - `:640` `dressed 'fortified_garrison' kit=synty-castle placed=489 missing=0`
> - `:671` `dressed 'mage_enclave' kit=dungeon-stone placed=590 missing=0`
>
> ⛔ This file is now the **fourth** place these three numbers were hand-copied (1609/1610/1611 headers +
> here). That is the duplicated-state defect WO-1635 was minted for. The authority is the newest
> `[Flow:RaidBase] dressed '<config id>' ... placed=` line on a FRESH bake log — never a doc.

## 2. Proof at HEAD, file:line

- `Assets/Editor/Regression/RaidBaseLayoutRegression.cs:1-11` - suite header, marker
  `RAID_BASE_LAYOUT_OK / _FAIL`, JSON + source oracle (no bake, no PlayMode).
- `:40-47` - the eight cases: `CaseEasyDress`, `CaseHardKeep`, `CaseExtremeKeep`,
  `CaseGeneratorWiresDresser`, `CaseArtLoadNotResourcesStructures`, `CaseSpawnerSlots`,
  `CaseGateWidth`, `CaseGarrisonWipeWins`.
- `:90-108` `CaseEasyDress` - FAILS if `raider_camp_small.raidDress` is absent, if its summed
  `props[].count` is `< 12`, or if `kit != "hexagon-green"`. This is the acceptance-8 oracle
  (see the residual in sec.4 on where the count moved).
- `:110-120` `CaseHardKeep` - FAILS if `fortified_garrison.interiorWallLayers < 1` or its kit is
  not `synty-castle`.
- `:201-216` `CaseGarrisonWipeWins` - pins `RaidVictoryController.HandleVictory("garrison wiped")`
  and `RaidScoring` treating `_spawner.Cleared` as a win.
- `Assets/Resources/Data/Canonical/scene-configs.json:87-97` (Easy `raidDress`, tokens
  `building_tent_green` / `barrel_large` / `crate_large` / `crates_stacked` by zone),
  `:163-173` (Hard `raidDress`, `SM_Prop_Spike_Fortification_01` in Approach + Gatehouse),
  `:242-252` (Extreme `raidDress`, `pillar_decorated` / `banner_white` / `torch_mounted` /
  `chest_gold` in Courtyard + Keep).
- `Assets/Editor/Regression/DataRegression.cs:741` - the suite is registered into
  `DataRegression.RunAll`, so it runs on the full gate.

## 3. The suite is GREEN on a fresh log

`Builds/ready-rca-checkpoint-regression.log`, read with PowerShell `Select-String` (Unity logs are
UTF-16):

```
[raid-base-layout] raid-base-layout easy dress tokens=27; hard keep layers=1;
  extreme kit=dungeon-stone; generator wires dresser; art load via dresser;
  spawner slots + defend post; gate width 3.5; garrison wipe wins
```

## 4. Residuals and deviations - named, not papered over

1. **Acceptance 8 wording vs the shipped oracle.** 1607 sec.8 item 8 asked for a regression that
   fails "if Easy still has `props.count == 0`". The literal `props` block on the Easy row is STILL
   `{"set": [], "count": 0}` (`scene-configs.json:83-86`); WO-1608 introduced `raidDress` as the
   authoring channel and the oracle pins THAT (`RaidBaseLayoutRegression.cs:96-107`, floor of 12
   tokens, measured 27). The intent is met by a different field. The dead `props` block on the raid
   rows is residual authoring debt - it is inert and no reader consumes it.
2. **Acceptance 6 "win condition unchanged: raze RaidSpire" is DEVIATED.** `CaseGarrisonWipeWins`
   (`:201-216`) pins garrison wipe as an INDEPENDENT win path, and explicitly fails if
   `RaidVictoryController` "still treats garrison wipe as a milestone - the player would be left
   hitting an empty camp." I found NO owner ruling text for this in WO-1608/1609/1610/1611
   (`grep -rn "garrison wipe" WorkOrders/WORK_ORDER_160*.md WorkOrders/WORK_ORDER_161*.md` = 0 hits).
   It is a deliberate, pinned change made by the 1608-1611 lane, but it is a deviation from this
   spine's own acceptance line. **Flag for the owner.**

## 5. What the owner should felt-test

1. Raid EASY (The Forsaken Camp) and take ONE eye-level screenshot. Can you name it "a scavenger
   camp" - tents, palisade, a gate, a watchtower with a top - rather than "a fence"? (acceptance 1)
2. Raid HARD (The Broken Garrison): does it read as a fortress - gatehouse, courtyard, inner choke,
   spire? (acceptance 2)
3. Raid EXTREME (The Veiled Enclave): does it read as a dungeon keep - stone, torches, columns,
   spire inside a place? (acceptance 3)
4. Does the staging pocket still sit outside every turret and defender? (acceptance 4)
5. Do troops path staging -> gate -> courtyard -> choke -> spire with no new nav softlock?
   (acceptance 5)
6. **Ruling wanted:** garrison wipe now wins the raid outright (residual 4.2). Intended, or should
   razing the spire remain the only win?

## 6. What is NOT proven by this pass

- **No visual proof of any kind.** No screenshot, no bake, no device frame was captured here.
  Acceptances 1, 2 and 3 are eye-level judgements and are entirely the owner's.
- **Nav pathing (acceptance 5) is unproven.** The suite is a JSON + source oracle; it never bakes a
  scene and never runs a nav query.
- **Acceptance 7 (missing KayKit pack does not fail the bake) is unproven here.** This clone has the
  packs; the missing-pack fallback path was not exercised.
- **`COMPILE_GATE_OK` was not run this session.** The full gate on
  `Builds/ready-rca-checkpoint-regression.log` is RED overall -
  `REGRESSION_FAIL: 2 failure(s) (472/474 registered suites green, 0 skipped)`. Both failures are
  UNRELATED to this ticket (`[hero-element-cast]` ordering and `MANAGE_BUILD_DOOR_FAIL` for
  pet-house / market / workshop, WO-2007). The raid-base suite itself is green on that same log.

---

# WO-1607 ADDENDUM - 2026-09-09, lane RAID-2: THE WIN-CONDITION RULING IS IN

Section 5 residual 4.2 above asks: *"garrison wipe now wins the raid outright. Intended, or should
razing the spire remain the only win?"*

> ## ✅ RULED 2026-09-09. OWNER, VERBATIM: ***"Keep both: spire raze OR full garrison wipe"***

**BOTH win paths are canon.** Razing `RaidSpire` ends the raid in a victory; wiping the garrison ends
the raid in a victory. This is ruling **(A)** from WO-1607 section 10, now stated as a decision
rather than as "the shipped behaviour standing until she rules".

**Nothing in code changes, and nothing in an oracle is re-pointed.**
`Assets/Editor/Regression/RaidBaseLayoutRegression.cs` `CaseGarrisonWipeWins` was already pinning
exactly this behaviour; it stays **exactly as it is**. It is now pinning a RULED behaviour instead of
an undecided one. That file was not opened for edit by this lane.

**Where the ruling is now recorded:**
- `WorkOrders/WORK_ORDER_1607_raid_bases_as_places.md` section 10 - retitled `RULED 2026-09-09`, the
  ruling quoted at the top, the pre-ruling question kept UNREWRITTEN below it under a `HISTORY`
  heading (CLAUDE.md section 15: a dated point-in-time record is banner-corrected, never rewritten).
- The same file, acceptance 6 - the stale sentence *"Win condition unchanged: raze `RaidSpire`"* is
  **kept verbatim** with a dated correction note beneath it, because the record of what it used to
  say is what makes the correction checkable.

**The two live win seams, read at source 2026-09-09 (working tree):**
- `RaidScoring.RaidWon` = `(_spire != null && _spire.IsDestroyed) || (_spawner != null && _spawner.Cleared)`
- `RaidVictoryController`: `HandleCleared` -> `HandleVictory("garrison wiped")` and `HandleSpireRazed`
  -> `HandleVictory(...)`. Two callers, one `_handled` latch, so a camp whose last defender dies on
  the same frame the spire falls settles exactly once.

**Unproven:** this addendum changes no code and runs no gate. Every other residual and every
acceptance in section 6 above is untouched and still open.

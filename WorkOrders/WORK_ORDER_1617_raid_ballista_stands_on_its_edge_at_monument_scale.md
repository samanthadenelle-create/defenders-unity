# WO-1617 - The raid ballista stands on its edge at monument scale: PlaceSpire ignores the siege exemption PlaceTowerProp already has

**Status:** IMPLEMENTED - awaiting gate + re-bake (2026-09-09 lane BALLISTA); owner felt-test closes
*(prior: READY TO IMPLEMENT, sequenced behind the WO-1607 felt-test - WO-1607 is
`IMPLEMENTED - 6a5c7a36d on HEAD`. This ticket edits its proving files; a fix landed underneath an
in-flight felt-test makes her verdict unattributable, so the re-bake + felt-test order matters.)*
**RESULT:** `WorkOrders/WORK_ORDER_1617_raid_ballista_stands_on_its_edge_at_monument_scale.RESULT.md`
**Minted:** 2026-09-09 (CLI, main-line banner; bumped 1615 -> 1619 in the SAME edit)
**Silo / Lane:** World / Raid scene builders (serialization bottleneck - one agent on raid builders
at a time, CLAUDE.md sec.9)
**Severity:** P2 felt - the centrepiece of the Easy camp is a siege engine lying on its side,
scaled to 8-18 m. It is the first thing the player sees on the first raid.
**Type:** EXISTING system. The exemption this ticket needs already exists twenty lines away.
**Provenance:** proven RCA with no ticket, `docs/READY_RCA_2026-09-09.md` "Raid ballista stands on
its edge and is enormous"; footnote 3 of `docs/reference/READY_SILOS_2026-09-09.md`.

---

## 1. The defect, source-proven (every line opened 2026-09-09)

The easy raid config reports central art `tower_siege_tower` in the live log.

**`Assets/Editor/WallTools/RaidBaseGenerator.cs`:**

- `PlaceSpire` (`:520`) computes the monument height unconditionally at `:528`:
  `targetHeight = Mathf.Clamp(targetHeight * 1.6f, 8f, 18f);   // a monument, not a hut`
- then calls `EnsureUpright(go, $"spire art '{catalogId}'")` at `:563`, and `ScaleToHeight` at
  `:564`. The comment above it states the ordering intent: stand it up BEFORE measuring height.
- `EnsureUpright` (`:1123-1137`) rotates by `Quaternion.Euler(-90f, 0f, 0f)` whenever
  `b.size.y < widest * 0.8f`. A ballista IS low and wide, so the heuristic reads correctly-authored
  art as a fallen building, tips it onto its edge - and `ScaleToHeight` then magnifies that wrong
  axis to monument height. Its own warning text even predicts this case: *"If the art is genuinely
  squat, this is a false positive."*

**The exemption already exists, and `PlaceSpire` does not consult it:**

- `PlaceTowerProp` (`:882`) guards the identical call at `:914-916`:
  `if (!IsAuthoredSiegeMachine(plan.CatalogId)) EnsureUpright(go, ...)`, with the reasoning written
  at `:910-913` - *"Catapults and siege towers are intentionally low, wide machines ... which is
  exactly the raid F8 report."*
- `IsAuthoredSiegeMachine` (`:920-925`) matches `tower_catapult` and `tower_siege_tower` - the exact
  id the Easy camp's log names.

**Second, independent defect - the art map.** `Assets/Editor/WallTools/RaidBaseDresser.cs:474-478`:
`MapCatalogArt` returns `"Ballista"` for any id containing `siege`. So a SPIRE slot - the camp's
architectural centrepiece - is handed siege-machine art, preserving the mismatch rather than
selecting a spire.

## 2. The fix shape - ONE decider, per ARCHITECTURE_PRINCIPLES

`docs/ARCHITECTURE_PRINCIPLES.md`: one owner per concern. "Is this art an authored siege machine?"
is one question and `IsAuthoredSiegeMachine` (`:920-925`) is already its answer.

1. **`PlaceSpire` consults the SAME predicate.** A siege id skips BOTH the `-90 X` correction at
   `:563` and the `1.6x` / 8-18 m monument clamp at `:528` - a machine authored at its true size
   must not be scaled to monument height either. Do not fork a second predicate, do not copy the
   two ids, do not add a `bool isSpire` parameter to `EnsureUpright`.
2. **`MapCatalogArt` must not select siege art for a spire slot.** A spire needs architectural art.
   Route the spire slot to a spire/tower mapping and leave the `siege -> Ballista` mapping for the
   turret slots that legitimately want a machine.

If the two defects can be fixed independently, fix both anyway - fixing only (1) leaves an upright,
correctly-sized ballista standing where a spire belongs, which is still wrong on screen.

## 3. What NOT to touch

- Do not hand-edit `RaidBase_*.unity`. Rebuild via `RaidBaseGenerator` + `RaidNavBake` (CLAUDE.md
  sec.3 and WO-1607's own Law line).
- Do not delete or loosen `EnsureUpright`'s threshold. It is doing real work for genuinely flat FBX
  imports; the fix is the exemption, not a weaker rule.
- Do not retune garrison HP/DPS or spire HP (`docs/RAID_BALANCE_AUDIT_2026-09-06.md`).
- Do not touch `RaidHudController` or `TroopController` (WO-1607 sec.9).
- Do not re-bake WO-1593 art - it is superseded by the 1608-1611 spine.

## 4. Acceptance criteria

- [ ] **RED first.** A regression case fails at HEAD because `PlaceSpire` calls `EnsureUpright`
      unguarded, and passes once it consults `IsAuthoredSiegeMachine`. Name the mutation.
- [ ] Exactly ONE predicate in `RaidBaseGenerator.cs` answers "authored siege machine"; both
      `PlaceSpire` and `PlaceTowerProp` call it. State its file:line in the RESULT.
- [ ] The `tower_siege_tower` spire is not rotated and not scaled to the 8-18 m band - the RESULT
      quotes the generator's own log line (`SPIRE '<id>' placed at centre: ... Nm tall`) before and
      after.
- [ ] `MapCatalogArt` no longer returns `Ballista` for a spire slot; the RESULT names what it
      returns instead.
- [ ] An eye-level screenshot of the Easy camp centre shows an upright, correctly-scaled
      centrepiece (memory `screenshots-are-primary-evidence-for-visual-defects`).
- [ ] `RaidBaseLayoutRegression` stays green, or its re-point is RULED by the lead (see sec.6).
- [ ] Owner felt-verifies on device and closes.

## 5. Files

- `Assets/Editor/WallTools/RaidBaseGenerator.cs` - `PlaceSpire` `:520-575`, the clamp `:528`, the
  `EnsureUpright` call `:563`, the existing predicate `:920-925`
- `Assets/Editor/WallTools/RaidBaseDresser.cs` - `MapCatalogArt` `:474-478`
- `Assets/Editor/Regression/RaidBaseLayoutRegression.cs` - the new case
- Registration line back to the lead; `DataRegression.cs` is lead-owned.

## 6. WARNING: Oracle collision - this needs the lead's eye, not the lane's judgement

`RaidBaseLayoutRegression` is WO-1607's proving suite and it asserts on **source text**, not on a
built scene. `CaseGeneratorWiresDresser` (`:137-155`) greps `RaidBaseGenerator.cs` /
`RaidBaseDresser.cs` for literals; `CaseArtLoadNotResourcesStructures` (`:159`) does the same. A
change to `PlaceSpire` or to `MapCatalogArt` can therefore break a case for the RIGHT reason.

**Read at source 2026-09-09: no case in that suite currently asserts a spire height, the
`EnsureUpright` call, or the `1.6f` clamp** (the cases are `CaseEasyDress :90`, `CaseHardKeep :110`,
`CaseExtremeKeep :125`, `CaseGeneratorWiresDresser :137`, `CaseArtLoadNotResourcesStructures :159`,
`CaseSpawnerSlots :174`, `CaseGateWidth :188`, `CaseGarrisonWipeWins :201`). So the collision risk is
real but narrow. If a case goes red, **hand it to the lead for a ruled re-point** - a lane never
re-points an oracle on its own (a sanctioned re-point carries a ruling and a comment naming the WO).

`CaseGarrisonWipeWins` (`:201-216`) is a DIFFERENT open question - it belongs to WO-1607's own
acceptance-6 owner ruling and has nothing to do with this ticket. Do not touch it.

## 7. Unproven, recorded honestly

- Which spire art the Easy camp SHOULD carry. That is a creative pick and it is the owner's
  (WO-1607 sec.0: creative authority stays hers on kit choices). Propose, do not decide.
- Whether Hard and Extreme are affected. Their `centralBuilding` ids were not read in this lane -
  only Easy's `tower_siege_tower` is proven from the log. Check both before claiming a scope.

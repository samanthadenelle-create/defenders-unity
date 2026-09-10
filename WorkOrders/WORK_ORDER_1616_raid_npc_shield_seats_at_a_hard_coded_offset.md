# WO-1616 - The raid NPC shield seats at a hard-coded offset: one shield-seating authority, not two

**Status:** IMPLEMENTED - awaiting gate (2026-09-09 lane NPC-SHIELD); owner felt-test on a raid closes
**RESULT:** `WorkOrders/WORK_ORDER_1616_raid_npc_shield_seats_at_a_hard_coded_offset.RESULT.md`

> **Sequencing note (kept for the record):** this ticket was SEQUENCED behind WO-1431 (lane
> HERO-GRIP) because both edit `EquipmentController.cs`. Lane NPC-SHIELD started from the WORKING
> TREE carrying WO-1431's edits (not from HEAD), and `WeaponOrientHelper.cs` was NOT touched here —
> so the two lanes are file-disjoint in practice and can be gated together.
**Minted:** 2026-09-09 (CLI, main-line banner; bumped 1615 -> 1619 in the SAME edit)
**Silo / Lane:** Hero + troop equipment seating (Hygiene silo, after lane 3A)
**Severity:** P2 felt - every deployed raid NPC carries a visibly wrong shield. It is not a
softlock, but it is on screen for the whole raid, on five actors at once.
**Type:** EXISTING system. The correct seating already exists on the hero path; the NPC path never
inherited it.
**Provenance:** proven RCA with no ticket, `docs/READY_RCA_2026-09-09.md` "Raid NPC shield is
offset"; footnote 2 of `docs/reference/READY_SILOS_2026-09-09.md`.

---

## 1. The defect, source-proven

`TroopGearApplier.ApplyDefaultGrip` attaches every off-hand directly to `LeftHand` with ONE
hard-coded seat. Read at source 2026-09-09, `Assets/_Modules/Village/Troops/TroopGearApplier.cs:139-144`:

```
else if (shield)
{
    t.localPosition = new Vector3(0.05f, 0.05f, 0.02f);
    t.localRotation = Quaternion.Euler(0f, 90f, 0f);
    t.localScale = Vector3.one * 1.0f;
}
```

The method's own header at `:105` states its ceiling honestly: *"Coarse grips for ~1.8 m Supercyan /
Tripo humanoids."* It is called unconditionally at `:97`, immediately before the attach trace at
`:98-100`. There is no per-rig, per-mesh or per-shield term anywhere in it.

**The live evidence:** the log identifies `troop-echo-legionnaire` and records `TroopGear/Shield`
attached on `LeftHand` **five** times. *(This count is taken from the RCA ledger's reading of the
live log; it was not re-grepped from the raw log in this lane. The source defect above WAS re-read
at source.)*

## 2. Why the hero does not have this bug

The hero path never uses that rule. Read at source in
`Assets/_Modules/Village/Hero/EquipmentController.cs`:

- `WeaponOrientHelper.ShieldFrame _currentOffHandShieldFrame` (`:294`), measured through
  `WeaponOrientHelper.TryResolveShieldFrame(prop, gripRoot.transform, out measured)` inside a
  `Guard` at `:2685-2689`.
- The frame is then consumed at `:2706` to derive rotation from the rig/socket axes, enforce
  outward facing, and centre on the socket.
- Finally the plate is pushed off the bone by the shared seat term at `:2794-2795`:
  `gripRoot.transform.localPosition += GearSeat.ShieldPlateOffBone(hand, outward, _currentOffHandShieldFrame, gripRoot.transform);`

So the game already owns a correct, measured shield-seating derivation. The NPC path simply cannot
reach it, which is exactly what the owner's screenshot shows.

## 3. The fix shape - ONE authority, and it is the hero's

`docs/ARCHITECTURE_PRINCIPLES.md`: one owner per concern. Shield seating is one concern.

- **Lift the hero's shield-seating derivation into a single shared entry point** that takes the
  prop, the hand transform and the outward axis, and returns the seat. `WeaponOrientHelper` +
  `GearSeat` already hold both halves - the shared authority belongs there, beside them, not in a
  third file.
- **`TroopGearApplier`'s shield branch calls that authority** instead of writing its own
  position/rotation/scale triple. Its non-shield branches are out of scope for this ticket.
- **Keep the primitive-fallback branch** at `:186-190` (the URP-safe placeholder scale/colour). That
  branch runs when there is no prop to measure, so it has nothing to derive a frame from.

## 4. What NOT to do

- STOP: **Never add a second offset table.** A per-troop JSON, a `troop-gear-offsets.json`, or a second
  hard-coded triple is the exact duplicated-state failure CLAUDE.md sec.2 / sec.5 / sec.16 each
  describe in their own words. The fix is to DELETE the copy, not to author a better one.
- Do not re-dial `(0.05, 0.05, 0.02)` by eye until it looks right. That would leave two authorities
  and the next rig would break both.
- Do not touch the hero seat's numbers. WO-1431 owns the hero grip derivation and is landing first;
  changing it here would duel with that lane.
- Do not touch the bow branch at `:115-118` - it carries its own `KNOWN GAP, DELIBERATELY NOT
  CHANGED HERE (2026-08-16)` note and belongs to the owner's canonical bow ruling.

## 5. Acceptance criteria

- [ ] **RED first.** A new regression case fails at HEAD because `TroopGearApplier` writes a
      literal shield seat, and passes once the shared authority is the only writer. Name the
      mutation it catches in the RESULT.
- [ ] `grep -n "0.05f, 0.05f, 0.02f" Assets/_Modules/Village/Troops/TroopGearApplier.cs` returns
      zero hits.
- [ ] Exactly ONE call site in the tree derives a shield seat; both the hero and the troop path
      reach it. State the file:line of that single authority in the RESULT.
- [ ] The hero's shield seating is UNCHANGED - `AttachmentOffsetRegression` and any WO-1431 suite
      stay green (this ticket must not become a hero-visual change).
- [ ] A raid capture shows a deployed `troop-echo-legionnaire` with the shield on the forearm, plate
      outward, not floating or edge-on. Screenshots are primary evidence for a visual defect
      (memory `screenshots-are-primary-evidence-for-visual-defects`).
- [ ] Owner felt-verifies on device and closes.

## 6. Files

- `Assets/_Modules/Village/Troops/TroopGearApplier.cs` (the shield branch, `:139-144`)
- `Assets/_Modules/Village/Hero/EquipmentController.cs` (`:2674-2795` - extract, do not re-author)
- `Assets/_Modules/Core/Geometry/WeaponOrientHelper.cs` + `Assets/_Modules/Core/Geometry/GearSeat.cs`
  (paths resolved by `find` 2026-09-09 - the home of the shared authority)

- A new suite under `Assets/Editor/Regression/`; the registration line goes back to the lead
  (`DataRegression.cs` is lead-owned).

**Collision note:** the second and third entries are WO-1431's files. This is why the ticket is
SEQUENCED, not parallel.

WARNING: **The `EquipmentController` / `WeaponOrientHelper` line numbers in sections 2 and 6 were
read against the WORKING TREE, which carries lane HERO-GRIP's uncommitted edits** (both files show
` M` in `git status` 2026-09-09). They are what WO-1431 will land, not what is on HEAD. **Re-read
both files after WO-1431 commits** before trusting a single one of them.

## 7. Unproven, recorded honestly

- The `x5` attach count and the `troop-echo-legionnaire` id come from the RCA ledger's reading of
  the live log, not from a raw-log grep in this lane.
- Whether any non-shield troop grip (spear / staff / axe, `:145-150`) is also visibly wrong. Nobody
  has captured those. Do not widen this ticket to cover them on suspicion - mint separately if a
  capture shows it.

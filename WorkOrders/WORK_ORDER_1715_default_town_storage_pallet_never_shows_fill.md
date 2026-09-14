# WORK ORDER 1715 - Default-town storage pallet never shows fill items; a player-built second one does

**Status:** READY TO IMPLEMENT - owner live-device observation, RCA lane assigned
**Minted:** 2026-09-14 by the CLI lead (Fable seat), from a live device pull while the owner was
felt-testing (release track continuing after 2026.09.14.369413)

## 1. Owner report, verbatim

> "first the storage i have deployed by default in the default town layout never register as anything,
> but when i added a second one i now have the items appearing on the pallets (see screen) that very
> happy about"

## 2. What is visible in the screenshot (primary evidence)

`docs/handoffs/storage_pallet_fill_default_vs_built.png`, pulled live off the device: two storage
pallets are visible in frame. The near one (bottom-left, wood logs stacked on it) shows resources
filled onto it. The other (mid-right, flat and bare) shows no items despite the town having 514 gold
and presumably wood in bank. Owner's framing: the DEFAULT-town-seeded pallet is the one that never
shows fill; the pallet she PLACED HERSELF (interactively, through the build menu) is the one now
correctly displaying items.

## 3. Why this is likely the SAME root-cause family as WO-1710, different consumer

WO-1710 (committed `593823f7d`, same day) proved that castle-builder/default-town-seeded structures
were missing registration in the "does this exist / is this tracked" registry that gates troop
training and the build-menu singleton offer list, while interactively-built structures were
automatically correct. This report has the identical shape: DEFAULT-seeded storage container works
visually (placed, presumably functional for collection) but its FILL-LEVEL DISPLAY never updates,
while a player-built one of the same type displays correctly. This STRONGLY SUGGESTS the pallet-fill
visual system reads from the same or a sibling registry/state source that WO-1710 found gaps in for
default-seeded structures - but this is a hypothesis to prove, not assume. The storage-pallet visual
system itself (Wood_Pallet/Iron_Pallet/Stone_Pallet beside each collector, per the 2026-09-13 owner
ruling recorded in `structures-catalog.json`'s `_containerScaleNote2026_08_26`) may have its OWN
separate state source that also has a default-seeding gap, independently of WO-1710's fix.

## 4. What to instrument before fixing (CLAUDE.md section 12)

- Find the code that drives a storage container's pallet-fill visual (how full the stacked resource
  model reads) - likely reads a resource-bank quantity or a per-structure fill counter. Find the exact
  call site and what identifies "which structure instance" it's filling for.
- Determine whether that identification depends on the same registration state WO-1710 fixed
  (`bakedTwins`/`StructureSingleton`/census) or an entirely separate mechanism.
- Confirm via a headless check or careful reading whether a DEFAULT-town-seeded storage container is
  missing from whatever state the fill-display reads, matching the WO-1710 shape, or whether this is a
  distinct defect.
- Check timing: WO-1710's fix (committed today) added `BackfillNewCensusRows` so existing Default-Town
  saves repair on next hub load. Confirm whether the owner's device pull happened BEFORE or AFTER that
  fix reached her build - if her installed build predates `593823f7d`, this may already be fixed and
  just needs a fresh APK to confirm, rather than new code.

## 5. Acceptance criteria

- [ ] RCA lane identifies the exact fill-display code path and states plainly whether it shares
      WO-1710's registry or has its own separate gap.
- [ ] RCA lane confirms whether the owner's currently-installed build already contains the WO-1710
      fix (`593823f7d`) or predates it - if it predates it, recommend a fresh tester push to re-test
      before any new code changes.
- [ ] If a genuinely separate gap is found, fix it using the same registration mechanism WO-1710
      established rather than inventing a parallel one (CLAUDE.md's replace-the-legacy-path discipline).
- [ ] Headless regression proving a default-town-seeded storage container's pallet displays fill
      correctly, alongside a player-built one.

## 6. What NOT to touch

- Do not touch `RaidBaseGenerator.cs`, `RaidBaseDresser.cs`, `ArenaBoundaryRing.cs`, `RaidNavBake.cs` -
  unrelated.
- Do not re-litigate the storage pallet SIZE/art work from 2026-09-13 (`_containerScaleNote2026_08_26`)
  - this ticket is about fill-display registration, not visual sizing.

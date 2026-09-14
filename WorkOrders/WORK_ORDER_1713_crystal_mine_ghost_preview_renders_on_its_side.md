# WORK ORDER 1713 - Crystal Mine build-mode ghost preview renders on its side

**Status:** READY TO IMPLEMENT - owner live-device capture, RCA lane assigned
**Minted:** 2026-09-14 by the CLI lead (Fable seat), from a live device pull while the owner was
felt-testing the 2026-09-14 tester build with a Seeker attached

## 1. Owner report, verbatim

> "device is attached. seeing footprint issues with the castle hub guessing something with the bake.
> i can check in unity if you want" ... "crystal mine on build screen is showing on its side, see
> device"

The lead pulled a live screencap instead of having the owner open Unity (owner is never the bug
detector, CLAUDE.md section 14). Screenshot: `docs/handoffs/footprint_issue.png` - build mode armed
on "Crystal Mine" (header + PLACE/ROTATE/CANCEL visible), a bright green translucent placement-ghost
object sits far right of frame, lying flat/tilted rather than standing upright like the other visible
objects on the same ground plane.

## 2. What is PROVEN this session (read at source, not inferred)

- `mine_crystal`'s catalog row (`Assets/Resources/Data/Canonical/structures-catalog.json:453-` area)
  authors `"manual": true, "euler": [90, 0, 0]` with an extensive dated note (WO-1224 Slice B,
  2026-08-26) explaining a deliberate, researched rotation fix for `CrystalMine.fbx`'s Tripo export
  axis quirk (`bakeAxisConversion` 1, "Blender 5.0.1-5.14.0" exporter). This is NOT a missing-fix row -
  the fix is authored and explained in detail.
- `GhostPreview.cs` (`Assets/_Modules/Village/BuildMode/GhostPreview.cs:282-319`) explicitly calls
  `StructureFactory.OptsFor(entry)` - "the SAME call `StructureFactory.Create` and `ReskinForLevel`
  make, so the ghost cannot fit/rotate with one shape and get another" - and its own comment records
  a PRIOR bug of exactly this shape that was fixed by routing `repo.preservePrefabRotation` through
  `OptsFor` so the ghost's copy would carry it too. Line 316-318: "WYSIWYG - euler is already in
  OptsFor -> LocalRotation (pre-Fit), matching `StructureFactory.Create` after GROK_BRIEF 2026-08-19."
- So the code is DESIGNED to keep the ghost and the placed structure in the same pose, and this row's
  euler fix is real and documented. The bug is that this guarantee is not holding for `mine_crystal`
  specifically (or possibly more rows - unconfirmed), which means something is breaking the shared path
  between `OptsFor` and what the ghost actually renders, OR the `manual`/`euler` values are not reaching
  `GhostPreview` for this row at runtime even though they exist in the JSON.

## 3. What is NOT proven - instrument before fixing (CLAUDE.md section 12)

- Whether this is specific to `mine_crystal` or affects every `manual:true` row (13 rows are named as
  sharing the same Tripo-export rotation-fix pattern in that row's own note - check all of them, not
  just this one).
- Whether the placed (post-PLACE) structure also renders sideways, or only the ghost - the owner's
  report is about the BUILD SCREEN ghost specifically; confirm whether committing the placement fixes
  itself or ships the same defect.
- The actual break point: add `FlowTrace` at `GhostPreview`'s `OptsFor` call and at wherever
  `_orientation.manual`/`LocalRotation` gets applied (`:319` onward) to see what values actually flow
  through for this row on a live run, rather than assuming the JSON values are what's reaching the ghost.
- The owner also said "footprint issues with the castle hub" and "guessing something with the bake" in
  the same breath - confirm whether this is the SAME defect (a rotation bug reads as a footprint bug
  when a sideways model's bounding box is wrong) or a SEPARATE castle-hub-specific issue. Do not assume
  they are the same without checking - the castle hub is baked scene content, not a `GhostPreview`
  instance, and the JSON note above already documents `heightMul`/footprint as a SEPARATE dial from
  rotation for exactly this reason ("A footprint is never corrected with heightMul... the fix is the
  POSE").

## 4. Acceptance criteria

- [ ] RCA lane instruments and identifies the exact point where `mine_crystal`'s authored euler stops
      reaching the ghost's rendered rotation, with a citation (FlowTrace line or debugger-equivalent
      capture), not an inferred cause.
- [ ] RCA lane confirms scope: this row only, or multiple `manual:true` rows.
- [ ] RCA lane confirms whether the COMMITTED (placed) structure also renders wrong, separately from
      the ghost.
- [ ] RCA lane states plainly whether the owner's "footprint issues with the castle hub" is the same
      defect or a distinct one requiring its own ticket.
- [ ] Fix (separate lane once cause is proven) restores WYSIWYG between ghost and placed structure for
      every affected row, with a headless regression proving it (a capture-based or reflection-based
      check of the ghost's applied rotation vs. `StructureFactory.OptsFor`'s output for at least
      `mine_crystal`).

## 5. What NOT to touch

- Do not touch `RaidBaseGenerator.cs`, `RaidBaseDresser.cs`, `ArenaBoundaryRing.cs`, `RaidNavBake.cs` -
  unrelated, already shipped today.
- Do not touch the euler VALUES already authored for `mine_crystal` or any other row without proof they
  are wrong - the RCA above found this specific row's fix well-researched and documented; the bug is
  more likely in the pipeline that applies it than in the authored data.

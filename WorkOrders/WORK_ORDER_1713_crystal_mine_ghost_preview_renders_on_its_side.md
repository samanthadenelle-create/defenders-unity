# WORK ORDER 1713 - Crystal Mine build-mode ghost preview renders on its side

**Status:** CLOSED 2026-09-17 - owner felt-test PASS (validated 2026-09-17T18:19:18, build 2026.09.17.373943). PRIOR STATUS: FIXED - owner ruling 2026-09-14 ("zero the two eulers, ship the fix"); mine_crystal and healing_caravan euler zeroed to [0,0,0] in both catalog copies, manual stays true, fallback regenerated; COMPILE_GATE_OK 09:56, REGRESSION_OK 525/525 10:01; PO felt-verifies and closes
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

## RCA 2026-09-14 (read-only lane)

**PROVEN CAUSE - it is not the ghost code, it is the ART ADDRESS. The row's `euler [90,0,0]` is now
being applied to a DIFFERENT, already-upright model.**

- `Assets/AddressableAssetsData/AssetGroups/Structure_Art.asset:201-202` (working tree, UNCOMMITTED)
  maps address `Structures/CrystalMine` -> guid `d3e59c4ea1b9db94185a404a62fcfa72`. That guid is
  `Assets/StructureContent/Synty/CrystalMine.prefab` (`Synty/CrystalMine.prefab.meta:2`).
- At HEAD (59c43c5c7) the same address mapped to guid `3c5d0584b7cd64649a972c3851bd1617`
  (`git show HEAD:...Structure_Art.asset`, lines 98-99) = `Assets/StructureContent/CrystalMine.fbx`
  (`Assets/StructureContent/CrystalMine.fbx.meta:2`) - the Tripo FBX the row's note names verbatim.
- The new asset is neither Synty nor Tripo despite the folder name: `Synty/CrystalMine.prefab` wraps
  `Assets/Models/KayKit/KayKit Medieval Hexagon Pack 1.0.1/Assets/fbx(unity)/buildings/green/building_mine_green.fbx`
  (source guid `8a44e7b2c357f6e4f9e85d8206e365b1`), whose importer carries `bakeAxisConversion: 0`
  (that .meta:58), and whose prefab ROOT ROTATION IS IDENTITY
  (`Synty/CrystalMine.prefab:24-38`: `m_LocalRotation.w = 1`, x/y/z = 0).
- The `+90` was derived FOR THE TRIPO FBX - the row note (`Assets/Resources/Data/Canonical/structures-catalog.json:453`
  row, orientation note) says so: "CrystalMine.fbx and HealingCaravan.fbx both carry bakeAxisConversion 1,
  so they take +90". `StructureFactory.OptsFor` applies the euler unconditionally FROM THE ROW, never
  from the asset (`Assets/_Modules/Village/Catalog/StructureFactory.cs:714-720`), and `VisualFactory.Skin`
  writes it onto the clone root pre-Fit (`Assets/_Modules/Village/VisualFactory.cs:346-347`). An upright
  KayKit mesh + a 90 deg pitch = lying on its side; Fit-to-height then measures the wrong axis and drags
  the plan axis out with it - which is also the owner's "footprint" wording.
- `Structure_Art.asset` mtime = 2026-09-13 21:33, i.e. the re-point predates the 2026-09-14 tester build.

**SCOPE - NOT the whole `manual:true` family, and NOT CrystalMine-only.** Diffing every
`m_Address -> m_GUID` pair at HEAD vs the working tree: TWELVE addresses were re-pointed to
`Assets/StructureContent/Synty/*.prefab` (ArcaneSpire_1, Ballista_L2, Catapult, CrystalMine,
GenericContainer, HealingCaravan, IronMine, Torche_Wall, Wall_Medieval_Stone, Watermill_Medieval, Well,
Windmill_Medieval), plus 3 NEW `Structures/OwnerStorage/*_Pallet` addresses. The other changed guids
(barracks / jeweler / farm / PetHouse2 / lumbermill .fbx) are meta re-import churn - same asset.
Cross-referencing the re-pointed addresses against each row's authored euler, EXACTLY TWO re-pointed
rows still author a non-zero manual euler:
- `mine_crystal` -> `Synty/CrystalMine.prefab`, euler `[90,0,0]` (this ticket)
- `healing_caravan` -> `Synty/HealingCaravan.prefab`, euler `[90,0,0]`; that prefab's root is identity
  too (`Synty/HealingCaravan.prefab:64-65`, `m_LocalRotation.w = 1`)
Every other re-pointed row authors `[0,0,0]` and is unaffected. **`healing_caravan` must be expected to
render sideways as well and fixed in the same pass.**

**GHOST vs PLACED - the ghost is NOT the defect, it is just where the owner saw it first.**
`GhostPreview.SetEntry` calls the same builder as placement (`GhostPreview.cs:297` ->
`StructureFactory.OptsFor`), and both then apply only offset/scale post-Skin, line for line
(`GhostPreview.cs:316-335` vs `StructureFactory.cs:159-181`). `SkinOptions.Structure(0f)`
(`VisualFactory.cs:147-148`) sets no `SeatFlat`. There is no divergence to find. Prediction to CHECK,
not a claim: the PLACED Crystal Mine renders identically sideways.

**WHY EVERY GATE STAYED GREEN - a real coverage hole.** `StructureOrientationOracle` excludes from its
only measured height assert every row whose catalog orientation tips the vertical axis
(`Assets/Editor/Regression/StructureOrientationOracle.cs:384-390`, `tipsVertical -> tipExcluded`). A3's
aspect band is `CatalogType.Tower` only (`:409-419`); `mine_crystal` is `Resource`. A1 fires only when
the model measurably stands at identity (`:344-352`), and it reasons from the MODEL'S OWN IMPORTER FLAG -
the exact signal a re-point changes. A captured gate log proves the exclusion by name:
`Builds/data-regression-current.log:15785` lists `mine_crystal(tilt 90.0deg, measured h=3.43m)` inside
"A2 NOT ASSERTED on 16 base visual(s)". The gate that exists to see a lying-down building cannot see
this row. Also stale: that file's header (`:37-41`) still describes `StructureFactory.Create`
multiplying the euler AFTER Skin; the code has applied it PRE-fit since 2026-08-19.

**CAPTURED DATA (CLAUDE.md section 12) - the pose flipping between builds is already on record.**
Same address, two different native poses on two device captures:
- `logs/debug/seeker-366581-hub2.log:32911-32914` (09-12, Seeker): instantiate `euler=(0,0,0)` bounds
  `1.606w x 1.137h x 1.92d` -> after `opts.LocalRotation` `1.606w x 1.92h x 1.137d` **aspect 1.196** ->
  Fit+Seat `3.345 x 4 x 2.368`. UPRIGHT - the Tripo FBX, correct.
- `logs/debug/raid-no-abilities-2026-09-06.log:26449-26494` (09-06): instantiate
  `euler=(90.00, 0.00, 0.00)` bounds `1w x 0.713h x 0.832d` -> `opts.LocalRotation` leaves it unchanged
  (already 90) -> Fit+Seat scale 5.61, bounds `5.609w x 4h x 4.669d` **aspect 0.713**. FLAT AND 5.6 m
  WIDE - the silhouette in `docs/handoffs/footprint_issue.png`, and a footprint claim ~1.7x the upright
  one.

**NOT PROVEN (say so, do not tick it):** which asset the 2026-09-14 tester APK's R2 bundle actually
carries. The 09-14 pull (`logs/device/pull-20260914-085211/logcat_full.txt`) opened AFTER the ghost was
armed - it holds `[Flow:Build] PlaceLoop PENDING: armed='mine_crystal'` (line 3) and ~2
`[Flow:StructureAssets] resolve 'Structures/CrystalMine'` lines per frame, but NO
`OptsFor('mine_crystal')` and NO `[Flow:Xform]` lines, because those fire once at arm time. The
instrumentation was already in place and would have answered this in one read had the capture started
a second earlier.

**REPRO FOR THE NEXT (write-enabled) LANE - no new instrumentation needed.**
1. `adb logcat -c`, then in the running build: open Build mode, arm Crystal Mine, PLACE it, then arm
   Healing Caravan. `adb logcat -d > logs/device/wo1713-<ts>.txt`.
2. `grep "OptsFor('mine_crystal')\|entry='mine_crystal'\|entry='healing_caravan'"` and read the
   `after instantiate` and `after Fit+SeatOnGround` bounds/aspect for BOTH the ghost and the Create.
   Aspect < 1 at Fit+Seat on both = confirmed, and confirms ghost == placed.
3. Headless equivalent (no device): `StructureOrientationOracle.TryMeasure` already replays the shipped
   pipeline. The durable fix is to DROP the `tipsVertical` exclusion for any row whose RESOLVED asset
   has `bakeAxisConversion == 0`, which turns this whole defect class into a gate failure instead of a
   felt-test.

**THE FIX IS A RULING, NOT A CODE CHANGE - do NOT "fix" `GhostPreview`.** Two mutually exclusive
options, and section 5 of this WO already forbids guessing between them:
(a) the KayKit re-point is intended -> ZERO the euler on `mine_crystal` and `healing_caravan` (keep
    `manual:true` so no auto-baker re-tips them); or
(b) the re-point was collateral -> RESTORE both addresses to their HEAD guids.
`Structure_Art.asset` is uncommitted, so whoever made the 2026-09-13 21:33 re-point should say which.
Either way the bundle must be re-pushed via `tools/r2-ship.ps1` (CLAUDE.md section 16) - an address
change re-hashes the bundle.

**THE OWNER'S "footprint issues with the castle hub" IS A DISTINCT ISSUE - open a separate ticket.**
The hub is AUTHORING-TIME content and never goes through `GhostPreview`: `CastleHubBuilder` builds its
own `SkinOptions` by hand (`Assets/Editor/CastleHubBuilder.cs:607-610` - `SkinOptions.Structure(0f)`,
`FitHeight = StructureFactory.YHeightVariable`, `LocalRotation = Quaternion.Euler(pitchDeg, yawDeg, 0)`),
so it never calls `OptsFor`; and at `:660-667` it WRITES the euler note INTO the catalog - the hub is a
PRODUCER of this data, not a consumer. Its walls/gates are baked `CastleSide_*` scene objects. A fresher
candidate for that complaint landed the same morning: `593823f7d` (07:20 today) registered owner-authored
castle roots as `bakedTwins` and stopped `CastleHubBuilder` seeding walls, and its own message flags
ruling B as "HALF DONE ON PURPOSE". The two reports share a SHAPE ("wrong size / wrong place"), not a
cause. Do not merge them.

**SIDE FINDING, not this ticket:** `StructureFactory.MeasureUprightFootprintXZ` resolves the address
(`StructureFactory.cs:978-980`) BEFORE the footprint-cache lookup (`:983-987`), so an armed build-mode
session emits ~2 `[Flow:StructureAssets] resolve` lines per frame (visible throughout the 09-14 pull)
and `BuildModeController.Update` measures 80-88 ms/s on device (same log, `[Flow:Perf] frame budget`
lines). Worth its own perf ticket.

**Status line deliberately NOT flipped** - read-only lane, per the brief.

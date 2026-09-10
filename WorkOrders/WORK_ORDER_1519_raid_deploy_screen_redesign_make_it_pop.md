# WO-1519: raid deploy screen redesign - hierarchy, art and chips, so the screen pops

**Status:** IMPLEMENTED - d6511b8e5 on HEAD, landed 2026-09-07 (was: uncommitted, awaiting gate); gated by the 2026-09-09 wave (REGRESSION 492/492 on Builds/wave1-reg3); owner felt-test closes
**Silo:** `RaidDeployScreen` + `RaidDeployController` (the deploy modal).
**LANDS AFTER** tonight's RaidDeployScreen / Controller commit, and BUILDS ON WO-1462 (backdrop),
WO-1463 (magenta flag) and WO-1464 (overlaps). Those are the layout defects; this is the design pass.
**Source:** read-only audit fleet 2026-09-06 (CLI seat), minted from the banner
(`CLI_LANES_WO_NUMBERS.md`, main line 1519 -> 1520 in the same edit).

## 1. EVIDENCE

Owner ask, verbatim:

> "screensshot, can we make this screen pop?"

Device frame `Logs/device/screens/owner-screen-20260906-201443.png` (build 358574, 20:14):

```
title    "RAID: THE FORSAKEN CAMP"
         green "Regular" pill + three gold diamonds + "Clock: 3:00"
LEFT     "YOUR FORCES" - three TINY hero portraits (Thrain, Grom, Sylas)
         "Army: 10 / 10 slots" COLLIDING with the hero row
         Footman x7 / Archer x3, small medallions, then a LARGE EMPTY BLACK AREA below
RIGHT    "ENEMY BASE / Scout the camp / Recon ~2:30 / Power 196"
         "ECHO GUIDE Corvin, the Void Echo" - quote cut at "...and it w..." - CHANGE button
         "SCOUT REPORT" as four lines of prose
BOTTOM   EDIT ARMY and BEGIN ASSAULT at EQUAL weight
through  the town and the hero visible through the panel (no backdrop - WO-1462 pending)
```

Nothing on the screen is sized by its importance: the decision the player is about to make (assault this camp
with this army) is carried by the same weight as a CHANGE button and four lines of prose.

## 2. FIX SHAPE (design direction)

The owner is red/green colourblind: **never carry meaning in hue alone; the greyscale check is the gate.**
Ask her about behaviour, never about palettes.

1. Backdrop from WO-1462 - land that first.
2. **ENEMY BASE becomes ONE HERO CARD**: the camp's art (the raid selection card art, or a scene capture),
   the boss portrait from `Portraits/`, and Power + Recon as two BIG numerals.
3. **YOUR FORCES**: hero portraits at the kit's LARGE medallion size with the class word under each; troop
   rows as portrait + count CHIPS. The army count becomes a band, `ARMY 10/10 FULL`, composed by the VM
   (the WO-1517 word), never overlapping the hero row.
4. **SPOILS as three icon+number chips** (wood, iron, gold) using the resource sprites - cap-aware and
   repeat-aware per WO-1461, so the number shown is the number that will bank.
5. Difficulty = the WORD plus the diamonds. The coloured pill is retired (hue-only meaning).
6. **The ECHO GUIDE block and its CHANGE button LEAVE the deploy screen entirely** (owner ruling 20:24 -
   see section 2B). This supersedes the earlier "fit or trim the Echo line" direction.
7. **BEGIN ASSAULT is the single gold primary** at the kit's primary size; EDIT ARMY is a secondary.
8. No empty black band: derive both column heights from their CONTENT, not fixed heights.

## 2B. THE ECHO GUIDE LEAVES THIS SCREEN (owner ruling, 2026-09-06 20:24)

She asked, of the block in the frame:

> "what is the Echo Guide even bringing to the table?"

Told that by her OWN 2026-09-04 scope fence (WO-1380: **no stat, no yield, no combat effect** - narrative
only) it brings one memory line, she ruled:

> "Remove it from the deploy screen."

So the `ECHO GUIDE` block and its `CHANGE` button come off `RaidDeployScreen`. That is one surface removed,
not a feature cut:

- `EchoGuideService` and the 24 memory lines **STAY**.
- The Echo still escorts and speaks in the world via `EchoWorldPresence` - still the single appearance owner.
- Guide SELECTION survives and can live on the Echoes screen instead.
- `EchoGuideMemoryRegression`'s scope fence is **UNTOUCHED** - do not weaken it to make the removal easier.

A dated section recording this is appended to `WORK_ORDER_1380_echo_guides_and_memory_lines.md`; that WO's
status is deliberately NOT flipped.

## 3. WHAT NOT TO DO
- Do not pick hues. Take the kit's existing roles; if a distinction needs a second channel, use shape, weight
  or a word.
- Do not fix the overlaps here - that is WO-1464. This ticket is the hierarchy pass on top of it.
- Check the boss portrait renders before relying on it: WO-1509 found the Orc Necromancer FBX has no albedo.
  Whether its 2D portrait asset is affected is UNPROVEN - open it and see.

## 4. ACCEPTANCE
- [ ] Headless `RaidDeploy_2670x1200.png` captured, OPENED and looked at.
- [ ] A GREYSCALE copy of that PNG still reads - every distinction survives without hue.
- [ ] `RaidSelectionLayoutRegression` sibling case: no overlap, every text inside its box, backdrop present.
- [ ] The spoils chips match what actually banks (shared with WO-1461's acceptance).
- [ ] A SOURCE-SHAPE case that FAILS if the deploy screen composes any Echo Guide block; `EchoGuideService`,
      the 24 lines and `EchoGuideMemoryRegression`'s scope fence all still present and green.
- [ ] `REGRESSION_OK n/n` on a fresh log.

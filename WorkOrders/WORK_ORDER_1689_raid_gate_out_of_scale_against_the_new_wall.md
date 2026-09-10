# WO-1689 - The raid gate and its flank towers are miniatures against the new base wall

**Status:** IMPLEMENTED - awaiting bake + device frame (lane RAID-ART-2, 2026-09-10; fix shape **2B**
- matched-box modules, no scaling code; RED-first rule seen red at 0.35/0.28 then green; RESULT at
`WorkOrders/WORK_ORDER_1689_raid_gate_out_of_scale_against_the_new_wall.RESULT.md`)
**Minted:** 2026-09-10 (lane RAID-ART-2, number **PRE-ASSIGNED by the lead**; the
`CLI_LANES_WO_NUMBERS.md` banner was **deliberately NOT edited** - the lead owns that bump.)
**Silo / Lane:** World / Raid scenes - the GATEHOUSE placement seam.
`Assets/Editor/WallTools/RaidBaseDresser.cs` only (`PlaceGatehouse`, and the flank block inside it).
WARNING: **Raid builders are a serialization bottleneck (CLAUDE.md sec.9) - ONE agent at a time.** See sec.8.
**Severity:** P2 felt-art. It is the first thing the player walks at, and it is now the most
out-of-scale object in the arena.
**Type:** EXISTING system, made visible by a landed change. The gate art loads, is textured and is
placed exactly where the code says. Nothing is broken. It became wrong the moment the wall beside it
tripled in height.
**Blocked on:** nothing. But it should be **judged on the same device frame as WO-1637**, because
WO-1637 is what surfaces it.

---

## STOP: 0. THIS IS A CONSEQUENCE OF WO-1637, AND WO-1637 DELIBERATELY DID NOT FIX IT

The RAID-ART-2 lane created this, found it, and left it alone on purpose. Read that before assuming
the gate regressed.

`PlaceGatehouse` is pinned **read-only** by `WORK_ORDER_1638_..._banners...md` sec.6 - *"gate
placement, gate art token, the layer assignment at `:397-401`. Read-only in this lane."* - and
WO-1637 sec.8 says the same from the other side. So the lane that raised the wall was **forbidden**
from touching the gate in the same pass, and correctly did not. This ticket is that deferral coming
due.

**The gate was ALREADY too small for its opening before WO-1637.** It has never filled it. WO-1637
did not shrink the gate; it raised the thing beside it, so an existing mismatch stopped being
invisible.

---

## 1. What was measured

All meshes below were read out of the FBX vertex extents with the importer's unit conversion applied
(`UnitScaleFactor/100 * scaleFactor` when `useFileScale`), 2026-09-10, by the RAID-ART-2 lane. The
method's **control run reproduces the 2026-09-10 raid bake log exactly** (that log printed
`piece 2.42m ... reach 1.82m` at applied scale 1.08 for a palette this script measures at 2.24 / 3.38),
so the numbers are validated against a real bake, not asserted.

### 1a. The mismatch, in metres

| object | token | measured X x Y x Z (m) | who places it |
|---|---|---|---|
| **base wall, raider_camp_small** | `wall_broken` | **4.00 x 4.00 x 1.00** | `CladRing` (`:452`) |
| **base wall, iron_bastion** | `wall` | **4.00 x 4.00 x 1.00** | `CladRing` via `DefaultWall` (`:298`) |
| base wall BEFORE WO-1637 | `barrier` | 4.00 x **1.10** x 0.50 | - |
| **the gate** | `wall_straight_gate` | **2.00 x 1.41 x 0.90** | `PlaceGatehouse` (`:501-531`) |
| **the flank towers** | `building_watchtower_green` | **1.04 x 1.11 x 1.04** | `PlaceGatehouse` (`:518-528`) |
| the gate opening | - | **8.55 m** (`ctx.GateWidth`) | derived, `RaidBaseGenerator.cs:547` |

After `CladRing`'s fit (sec.1b) the wall stands at **~3.8 m**. So:

- the gate is **1.41 m** in a **~3.8 m** wall - it reaches **37%** of the wall's height;
- the gate is **2.00 m** wide in an **8.55 m** opening - it fills **23%** of the hole it is meant
  to close;
- the flank towers are **1.11 m** - they are now **shorter than the wall they flank**, at 29% of it,
  and they were already shorter than the 1.10 m `barrier` by a hair.

Everything at that gatehouse is from the **KayKit Medieval Hexagon** pack, which is a hex-TILE
strategy kit: its pieces are authored at roughly 1-2 m for a top-down tile, not for a third-person
hero. `wall_straight` (the hexagon pack's own plain wall) measures **2.00 x 1.10 x 0.80** - i.e. the
whole hexagon wall vocabulary is knee-high, which is why WO-1637 could not fix the wall by staying
inside that kit and moved to the Dungeon pack instead.

### 1b. Why the wall ends at ~3.8 m and the gate does not move with it

`CladRing` (`RaidBaseDresser.cs:452-499`):

```
float piece = MeasureLongest(model);            // :461  -> 4.00 for wall_broken
if (piece < 1.2f) piece = kit == "synty-castle" ? 5f : 4f;
float run = radius * 2f;                        // :463  -> 62 m at baseRadius 31
int   n    = Mathf.Max(2, Mathf.CeilToInt(run / piece));   // :464 -> 16
float step = run / n;                           // :465  -> 3.875
...
if (go != null) FitPieceAlong(go, step * 0.98f, piece);    // :487
```

`FitPieceAlong` (`:1247-1252`) is a **UNIFORM** scale - `go.transform.localScale * f` with
`f = Mathf.Clamp(target / native, 0.6f, 1.4f)` - so `f = 3.80 / 4.00 = 0.95` and the wall lands at
**4.00 x 0.95 = 3.80 m tall, 0.95 m thick.**

`PlaceGatehouse` (`:501-531`) applies **no scale at all**. It calls `InstantiateVisual(...)` with a
position and a rotation, and `InstantiateVisual` (`:236-254`) only sets transform + layer + colliders
+ `SeatOnGround`. **There is no path by which the gate or the flanks learn how tall the wall is.**
That is the defect in one sentence.

Note also `:512-515`: the gate is put on the `Structure` layer. Keep that.

### 1c. What the frame shows

`Builds/device-frames/2026-09-10_0614_arena_06_wide.png` (build 363529, Seeker, 2670x1200), the frame
WO-1637 and WO-1638 both worked from. The south wall line reads as a **low grey balustrade with
rounded merlons that two standing troopers overtop** - that is `barrier` at 1.10 m, and it is the
"before". A 4x crop of the gatehouse area was opened by the RAID-ART-2 lane this session.

STOP: **NOT PROVEN: what the gate looks like beside the NEW wall.** No bake has been run with
`wall_broken` in place, so the mismatch above is arithmetic on measured meshes, **not** a picture.
`PlaceGatehouse` is unchanged, so the gate cannot have moved - but the lane that takes this ticket
should open the post-WO-1637 frame FIRST and confirm it reads as badly as the numbers say. **If the
frame disagrees with sec.1a, believe the frame and say so.**

---

## 2. What is NOT claimed

- **NOT claimed the gate art is broken, missing or a placeholder.** `wall_straight_gate` is real
  authored KayKit art and it resolved correctly on the 2026-09-10 bake
  (`[Flow:RaidBase] GATE south width=8.55m art=wall_straight_gate`). WO-1638 sec.0 falsified the
  "placeholder gate" premise once already - **do not re-derive it.**
- **NOT claimed `entranceCount` or the gate COUNT is wrong.** Two gates is correct and agrees with the
  scout line. Sec.6.
- **NOT claimed the 8.55 m opening is wrong.** `ctx.GateWidth` is derived from the wall ring's own
  geometry (`RaidBaseGenerator.cs:547`) and other systems read it - the gate mouth props
  (`PlaceGateFlanks`, `:548-573`), the approach road, the lane keepout (`:778`), and the banner slot
  (`:1024-1026`). **This ticket makes the ART fill the opening; it does not change the opening.**
- **NOT claimed for `fortified_garrison` or `mage_enclave`.** Their kits were not measured for this
  ticket. `synty-castle` uses `SM_Bld_Castle_Wall_Gate_01` + `SM_Bld_Castle_Wall_Tower_S_01`;
  `dungeon-stone` uses `wall_straight_gate` + `wall_pillar`. **Measure before you touch them.**
- **NOT claimed which fix shape is right.** Sec.4 offers two and does not pick.

---

## 3. Target - what "fixed" means

From the hero's deploy seat, the gate reads as **a way through a wall you could not otherwise cross**:
its arch spans a usable share of the 8.55 m opening, its top is at or near the wall's top, and the
flanking towers stand **at least as tall as the wall**, not below it. Judged on a device frame from
the `..._0608` / `..._0613` seat, in colour **and** in greyscale (the owner is colourblind - memory
`owner-colorblind-delegate-visual-creative`).

---

## 4. The fix - two shapes, pick one with the measurements in front of you

### Step 1 - INSTRUMENT FIRST (CLAUDE.md sec.12). The trace does not name a height today.

`PlaceGatehouse:530` prints `GATE {side} width={width:F2}m art={model.name}` - the **opening's** width
and the art's NAME. It does **not** print the resolved mesh's own size, so nothing on the bake log
could ever have caught this. `CladRing` traces the wall's MATERIAL (`:474-495`, via
`ArenaBoundaryRing.TraceMaterials`) but not its height either.

**Add the height to both, and leave them in (instrumentation is PERMANENT, CLAUDE.md sec.12).** One
extra clause on the existing `GATE` line and one on the wall's line, each naming the renderer-bounds
`size.y` **after** the fit scale is applied, plus the opening width and the fit factor. That single
pair of numbers is the acceptance evidence (sec.5) and it makes every future kit swap self-reporting.
⚠ Compute the interpolated parts into **locals** before building the string - the gate's brace scanner
has no interpolated-string model (CLAUDE.md sec.1), and `:530` is already a `$"..."` with a
`?:` and nested quotes inside the hole, which is exactly the shape that breaks it.

### Step 2A - SCALE the gate and the flanks to the wall (the repo's existing rule)

The repo already has a fit-to-height convention (memory `normalize-items-by-Y-height`): measure the
renderer bounds and scale **uniformly** by `target / bounds.size.y`. Live implementations to copy, not
re-invent: `ArmoredKnightVerify.NormalizeHeight` (`Assets/Editor/ArmoredKnightVerify.cs:280-287`,
`body.transform.localScale *= (target / b.size.y);`) and `BattleAnchorStageVerify.cs:156-157`, the
same two lines.

- Pass `CladRing`'s achieved wall height down to `PlaceGatehouse` (it is `piece`-derived and already
  computed at `:461-465`; today `PlaceGatehouse` is called at `:154-156` with no such argument).
- Normalize the gate to about that height, and the flanks to **>= it**.
- ⚠ A uniform scale on a 2.00 m gate to reach 3.80 m is **2.7x**, which also makes it **5.4 m wide** -
  which is *good*, it fills 63% of the 8.55 m opening instead of 23%. **But re-seat the flanks**:
  `offset = Mathf.Max(3.2f, width * 0.55f)` (`:521`) = 4.704 m is derived from the OPENING, not from
  the gate's new width, so a widened gate may now overlap them. Re-derive the offset from the scaled
  gate's actual bounds.
- ⚠ `SeatOnGround` runs inside `InstantiateVisual` **before** any scale you apply afterwards. Scale
  first, then re-seat, or the piece floats/sinks by its own pivot offset. `wall_straight_gate`'s pivot
  is `minY = -0.05`, so this is a real 13 cm error at 2.7x.

### Step 2B - SWAP to a gate module already at wall scale (probably cheaper, and it was measured)

The base wall now comes from the **KayKit Dungeon Remastered** pack, and that pack ships gate and
pillar modules on the **identical 4.00 x 4.00 x 1.00 box** as `wall` / `wall_broken`:

| candidate | measured X x Y x Z (m) | note |
|---|---|---|
| `wall_gated` | **4.00 x 4.00 x 1.00** | a gated wall panel - same box as the wall |
| `wall_doorway` | **4.00 x 4.00 x 1.00** | an open doorway - same box |
| `wall_pillar` | **4.00 x 4.00 x 1.50** | a pillar at wall height - a flank at last |

So `gate` -> `wall_gated` and the hexagon-green `flankTok` -> `wall_pillar` would land the whole
gatehouse at wall scale with **no scaling code at all**, and with the same atlas
(`dungeon_texture_URP`) the wall already uses. Two or three of them side by side would also fill more
of the 8.55 m opening than one 2.00 m piece.

⚠ **`gate` is a DATA field** (`raidDress.gate`, `"wall_straight_gate"` at
`Assets/Resources/Data/Canonical/scene-configs.json:89`) with a code fallback in
`DefaultGate` (`RaidBaseDresser.cs:290-295`). Both mirrors, **edited in BINARY with the newline count
proven** (memory `canonical-json-edits-binary-only-verify-newlines`). `flankTok` is code-only
(`:518-519`).

**Whichever shape is picked, RE-BAKE.** This is baked scene content; nothing changes on device until
the raid scenes are re-baked. **Never run a bake with the Unity editor open** (CLAUDE.md sec.3) - bake
commands go to the lead.

---

## 5. Acceptance

1. **The trace.** Step 1's lines on a fresh bake log, naming the gate's resolved height, the flanks'
   resolved height, the wall's achieved height and the opening width - **all four on the same bake**.
   Paste them. The pass condition is arithmetic on that line, not an opinion.
2. **A device frame** from the same seat as `..._0608` / `..._0613`, after the re-bake, showing the
   south gatehouse. Paste it. Owner's standing rule: *"I want images to verify anything that is a
   viewable issue"*.
3. **The greyscale check** of that frame - the gate must still read as a distinct opening with the
   colour removed.
4. **The gate count is unchanged.** Still `Gatehouse_south` / `Gatehouse_north` at
   `(0, 0.05, -/+31)`, and the scout line still reads `Walls: Wood, 2 gates`.
5. **Nothing that reads `ctx.GateWidth` moved.** The gate-mouth props, the approach road, the lane
   keepout and the banner slot all still land where they did - name them and check them.
6. **Brace and NUL checks** on every `.cs` touched, including `python tools/gate_brace.py` on each
   (CLAUDE.md sec.1 - the gate counts differently from the raw one-liner).

---

## 6. Pins - what must not move

- **`entranceCount`** in `scene-configs.json` (both mirrors) and its two readers,
  `RaidDeployVM.cs:503-508` (the scout line) and `RaidBaseGenerator.cs:490` / `:545` (the geometry).
  One field, two readers, currently in agreement. **Keep it that way.**
- **`ctx.GateWidth` and its derivation** (`RaidBaseGenerator.cs:547`,
  `Mathf.Max(RaidBaseDresser.MinGateWidth, outer.GateWidth)`) and `MinGateWidth = 3.5f`
  (`RaidBaseDresser.cs:28`). This ticket changes the ART, never the opening.
- **The gate's `Structure` layer assignment** (`RaidBaseDresser.cs:512-515`).
- **`HideWallRenderers`** (`:377-388`) - it disables renderers on `Wall_`-prefixed objects only and
  must keep ignoring `Gatehouse_*`, `GateFlank_*` and `Prop_*`.
- **WO-1637's landed values**: `ArenaBoundaryRing.RockPaths`, the `DressAtmosphere` hexagon-green
  branch (45 / 200), `DefaultWall`'s `"wall"`, and `raider_camp_small.raidDress.wallModule`
  (`"wall_broken"`). **This ticket is downstream of them - do not "fix" the mismatch by shrinking the
  wall back.** That would reverse an owner ruling (2026-09-10 12:07).
- **WO-1638's landed value**: `raider_camp_small`'s Gatehouse prop row is now `flag_red`. The banners
  hang beside this gate; do not disturb the row.
- **`RaidArenaShapeRegression`** and **`RaidBaseLayoutRegression`** - both source-text lint this
  dresser. **Open them and report what they assert before editing; re-point, never delete.**
  `RaidBaseLayoutRegression` requires `Zone_Gatehouse`, `Zone_Courtyard`, `GarrisonSlot_`,
  `def.raidDress.props`, `AssetRoots.StructureContent`, `MinGateWidth = 3.5f`, and **FORBIDS**
  `def.props` and `DefaultProps`.
- **`ArenaBoundaryRing.TraceMaterials`** and the `PROP` / `MAT` / `ATMOSPHERE` traces - permanent
  instrumentation. **Add to them; never strip them** (CLAUDE.md sec.12, owner ruling 2026-08-09).

---

## 6b. RED-FIRST PIN - required, and it must be seen red BEFORE the fix

`Assets/Editor/Regression/LayoutOracle.cs:17-20` requires a new rule to be **SEEN RED first**. There
is currently **no rule anywhere that compares the gate's height to the wall's** - which is exactly why
a 3.5x mismatch could land silently.

**Write the rule first, run it against the CURRENT tree, and paste the RED.** Shape:

> the gatehouse art's resolved height must be at least a stated fraction of the base wall module's
> resolved height for the same kit, and the flank tower's height must be **>=** the wall's.

Author it against the **measured mesh boxes** (sec.1a) the same way `RaidArenaShapeRegression`'s
`CaseArenaBoundaryDesignedFit` runs the builder's own arithmetic from source constants with no bake -
see `MeasuredThinnestPieceXZ` / `MeasuredWidestPieceXZ` (`RaidArenaShapeRegression.cs:776-813`) and
**the comment block above them**, which explains how a measured-mesh constant is sourced, why it is
the only outside-the-tree number in that oracle, and that it must be re-pointed in the same edit as
any palette/module change. **Follow that pattern exactly; it was re-pointed once already, on 09-10.**

⚠ On the current tree the rule reds at roughly `1.41 / 3.80 = 0.37` for the gate and
`1.11 / 3.80 = 0.29` for the flanks. **If it does not go red, your rule is not measuring what this
ticket is about - fix the rule, not the threshold.**

---

## 7. What NOT to touch

- **The wall module, the fog and the ring palette** - all three are WO-1637's landed owner ruling.
- **The banner row** - WO-1638.
- **The exterior boundary ring** (WO-1632) and the **cover props** (WO-1633). Both FIXED and baked.
- **`raidDress.props` arrays** - **WO-1634 owns them** and its Status flip is currently behind its own
  RESULT file. Do not edit that array concurrently; the lead sequences it.
- **`RaidBaseGenerator`'s fallback spire / turret builders** and their primitives.
- **Other camps' kits** unless you measure them first and say so (sec.2).
- Do **not** commit. Do **not** push. Hand the diff back (CLAUDE.md sec.11).

---

## 8. SEQUENCING - read before starting

`VillageSceneBuilder`-class serialization applies to the raid builders too
(`WORK_ORDER_1619_...md:5-6`). **One agent on the raid builders at a time.**

- **WO-1637** and **WO-1638** are `IMPLEMENTED - awaiting bake + device frame` and both touch
  `RaidBaseDresser.cs`. **This ticket rebases onto them and should be judged on the SAME bake**, so
  the owner sees the wall and the gate settle together rather than in two passes.
- **WO-1634** is in flight on `scene-configs.json`'s props array (sec.7).
- State in the hand-back which commit was rebased onto.

---

## 9. Board

This lane owns this ticket. Its hand-back is incomplete until this file's `**Status:**` line is
flipped and `WorkOrders/WORK_ORDER_1689_raid_gate_out_of_scale_against_the_new_wall.RESULT.md` is
written, with both paths reported. The lead regenerates `BOARD.html`
(`python tools/board_build.py`) and commits the flip in the SAME commit as the work.

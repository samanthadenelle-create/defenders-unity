# WO-1689 RESULT - fix shape 2B: matched-box modules, no scaling code

**Status:** IMPLEMENTED - awaiting bake + device frame
**Lane:** RAID-ART-2, 2026-09-10
**Rebased onto:** `e4b5906a541c65fc12255a109b5dc3723e328488`. The WO-1637 / WO-1638 edits are in the
main tree unchanged and are **kept** - this builds on them.
**No Unity was run, no bake, no gate, no commit.**

---

## 1. 2B WAS CHOSEN, AND THE PER-CAMP CHECK IS WHY

The brief said prefer 2B **if the pack pieces resolve for every kit that uses the wall module**. They
do, and the per-camp table is the evidence. Every height below was measured from FBX vertex extents
with the importer's unit conversion applied; the script's control run reproduces the 2026-09-10 raid
bake log exactly (`piece 2.42m ... reach 1.82m` at applied scale 1.08 on a palette it measures at
2.24 / 3.38 m), so the method is validated against a real bake.

> ⚠ **Measuring the Synty kit needed a parser fix, and it is worth recording.** The polyperfect and
> KayKit packs are FBX **7400**; Synty's `PolygonFantasyKingdom` models are FBX **7700**, which uses
> **64-bit** node records instead of 32-bit. Reading a 7700 file with the 32-bit layout desynchronises
> on the first node and yields garbage. The reader is now version-aware. **The polyperfect control was
> re-run after that change and still reproduces 2.24 / 3.38**, so the fix did not move any earlier
> number in WO-1637.

### 1a. Who takes what, before the fix

| camp | kit | wall module | wall h | gate (source) | gate h | **gate/wall** | flank | flank h | **flank/wall** |
|---|---|---|---|---|---|---|---|---|---|
| `raider_camp_small` | hexagon-green | `wall_broken` (JSON) | 4.00 | `wall_straight_gate` (JSON) | 1.41 | **0.35 RED** | `building_watchtower_green` | 1.11 | **0.28 RED** |
| `iron_bastion` | hexagon-green | `wall` (**DefaultWall**) | 4.00 | `wall_straight_gate` (**DefaultGate**) | 1.41 | **0.35 RED** | `building_watchtower_green` | 1.11 | **0.28 RED** |
| `mage_enclave` | dungeon-stone | `wall_cracked` (JSON) | 4.00 | `wall_straight_gate` (JSON) | 1.41 | **0.35 RED** | `wall_pillar` | 4.00 | 1.00 OK |
| `fortified_garrison` | synty-castle | `SM_Bld_Castle_Wall_01` (JSON) | 5.00 | `SM_Bld_Castle_Wall_Gate_01` (JSON) | 5.86 | **1.17 OK** | `SM_Bld_Castle_Wall_Tower_S_01` | 7.52 | **1.50 OK** |

**Two findings the brief asked for, answered directly:**

1. **`synty-castle` already matches its own wall and needs NO change.** Its kit was authored as a
   matched set - the gate is *taller* than the wall (1.17) and the tower taller again (1.50). It is
   the one kit that was always right, and it is deliberately untouched. **Do not "unify" it onto the
   dungeon pack.**
2. ⛔ **`dungeon-stone` did NOT have a matching gate, and this is a defect WO-1637 did not cause.**
   `mage_enclave` has been pairing the same 1.41 m hexagon `wall_straight_gate` with a 4.00 m
   `wall_cracked` wall **all along** - it is shipping today and predates this whole block. Its FLANK
   (`wall_pillar`, 4.00 m) was already correct. So the gate defect spans **three of four camps**, not
   just the two WO-1637 touched.

Because the KayKit Dungeon pack supplies `wall_gated` at **4.00 x 4.00 x 1.00** - the identical box to
`wall`, `wall_broken` and `wall_cracked` - **2B lands every KayKit-walled camp at ratio 1.00 with no
scaling code at all**, and leaves the Synty camp alone. 2A was not needed and was not used.

### 1b. After the fix

| camp | gate | gate/wall | flank | flank/wall |
|---|---|---|---|---|
| `raider_camp_small` | `wall_gated` | **1.00** | `wall_pillar` | **1.00** |
| `iron_bastion` | `wall_gated` | **1.00** | `wall_pillar` | **1.00** |
| `mage_enclave` | `wall_gated` | **1.00** | `wall_pillar` | 1.00 |
| `fortified_garrison` | *unchanged* | 1.17 | *unchanged* | 1.50 |

---

## 2. THE RED-FIRST PIN - seen RED before the fix, exactly as WO-1689 sec.6b requires

New case **`RaidBaseLayoutRegression.CaseGateReadsAsAGate`**, registered in `Run`'s case list. It
reads the wall / gate / flank tokens out of source (`DefaultWall`, `DefaultGate`, the `flankTok`
statement) **with the JSON row taking precedence**, exactly as `RaidBaseDresser.Dress` does, then
compares measured module heights. Floors: **gate >= 0.80 of the wall**, **flank >= 1.00 of the wall**.
Pure arithmetic, no bake, no PlayMode - the idiom of
`RaidArenaShapeRegression.CaseArenaBoundaryDesignedFit`, whose header explains how a measured-mesh
constant is sourced and why it must be re-pointed with any module change.

> ⚠ **I cannot run Unity from this lane, so "seen red" was demonstrated by running the case's OWN
> logic** - same slices, same token-resolution rule, same table, same floors - against the tree on
> disk, before and after. That is a faithful port, **not** a compile-and-run of the C#. Stated plainly
> so nobody reads it as a suite pass.

**BEFORE (unfixed tree) - exit 1:**
```
raider_camp_small  hexagon-green  wall_broken 4.00  wall_straight_gate 0.35 RED  building_watchtower_green 0.28 RED
iron_bastion       hexagon-green  wall        4.00  wall_straight_gate 0.35 RED  building_watchtower_green 0.28 RED
fortified_garrison synty-castle   SM_Bld_Castle_Wall_01 5.00  SM_Bld_Castle_Wall_Gate_01 1.17 OK  SM_Bld_Castle_Wall_Tower_S_01 1.50 OK
mage_enclave       dungeon-stone  wall_cracked 4.00 wall_straight_gate 0.35 RED  wall_pillar 1.00 OK
RAID_BASE_LAYOUT_FAIL :: 5 failure(s)
```
**AFTER - exit 0:** all four camps OK, `RAID_BASE_LAYOUT_OK :: gate-scale clean`.

**The reds are 0.35 / 0.28 where the WO predicted 0.37 / 0.29.** Not a discrepancy: the WO priced them
against the *fitted* wall (3.80 m after `FitPieceAlong`'s 0.95), the rule prices them against the
*module* (4.00 m), because the fit factor is camp-radius dependent and a rule that moves with a camp's
radius is not a rule. Both say the same thing.

> ⚠ **TWO BUGS IN MY OWN RULE, FOUND BY RUNNING IT RATHER THAN BY READING IT.** Recording them because
> either would have made the pin quietly worthless:
> 1. **The offset arithmetic was one short.** After finding `kit == "<kit>"` it began scanning at the
>    literal's *closing* quote, so it returned the text *between* branches (`) return `, ` ? `) instead
>    of the token. Every explicit branch mis-read; only the fall-through worked.
> 2. **My own new doc comment poisoned the lint.** `gateBody` was sliced from `DefaultGate` up to
>    `"private static string DefaultWall("` - which swallowed `DefaultWall`'s XML doc comment, and that
>    comment quotes a token. The fall-through default was being read **out of a comment**. Both bodies
>    are now sliced from their signature to their own closing brace. **A doc comment must never be able
>    to change what a source lint sees**, and this is now written into the code beside the slice.

---

## 3. What changed

| File | +/- | What |
|---|---|---|
| `Assets/Editor/WallTools/RaidBaseDresser.cs` | 96/13 | `DefaultGate` -> `wall_gated` for hexagon-green + dungeon-stone; `flankTok` -> `wall_pillar` for hexagon-green; flank offset derived from the flank's measured span; `CladRing` returns the achieved wall height; `PlaceGatehouse` takes it; `MeasureHeight` / `MeasureTallest` helpers; the `GATE` and new `WALL` trace lines |
| `Assets/Editor/Regression/RaidBaseLayoutRegression.cs` | 205/1 | `CaseGateReadsAsAGate` + its measured-height table, the two floors, `KitToken`, `Slice`, and its registration |
| `Assets/Editor/ArenaBoundaryRing.cs` | 14/4 | the two stale `SiegeArena.unity` assertions, re-phrased |
| `Assets/Resources/Data/Canonical/scene-configs.json` | 2/2 | `gate` -> `wall_gated` on `raider_camp_small` + `mage_enclave` |
| `Assets/StreamingAssets/...` (twin) | 2/2 | byte-identical mirror |
| `WorkOrders/WORK_ORDER_1638_...md` | 10/1 | the read-only pin **re-pointed, not deleted** |

### 3a. ⛔ THE CHANGE THAT PREVENTED A SEALED BASE - read this one

Swapping the flank to `wall_pillar` is a **4.00 m wide** tower where `building_watchtower_green` was
**1.04 m**. The old offset `Mathf.Max(3.2f, width * 0.55f)` = **4.70 m** was safe only because of that
narrowness: the tower's inner face sat at **4.18 m**, just outside the **4.28 m** half-opening. At the
same offset a `wall_pillar`'s inner face lands at **2.70 m** - **1.6 m INSIDE the gate mouth** -
collapsing the walkable slit either side of the gate from ~3.3 m to **0.7 m**. That is below
`MinGateWidth` and below anything a NavMeshAgent can path through, and `RaidNavBake` marks these
renderers NavigationStatic and bakes, **so the taller tower would have SEALED the raid base.**

The offset is now
`Mathf.Max(Mathf.Max(3.2f, width * 0.55f), width * 0.5f + flankSpan * 0.5f)`, i.e. the flank's inner
face is placed **on the opening's edge, never inside it**, derived from `MeasureLongest(flank)` so the
guarantee survives the next module swap:

| flank module | span | old offset -> inner face | **new offset -> inner face** | opening half |
|---|---|---|---|---|
| `building_watchtower_green` (old) | 1.04 | 4.70 -> 4.18 | 4.80 -> **4.28** | 4.28 |
| `wall_pillar` (new) | 4.00 | 4.70 -> **2.70 (inside!)** | 6.28 -> **4.28** | 4.28 |

Note the old module barely moves (4.70 -> 4.80), so this is not a layout churn - it is a floor.

STOP: **NOT PROVEN: that the base is navigable after the bake.** The arithmetic says the gate mouth is
no narrower than it is today, and the gate piece is centred in it as before. But `wall_gated` is 4.00 m
wide where `wall_straight_gate` was 2.00 m, and it is placed with `stripColliders: false`, so the clear
ground either side of the gate narrows from ~3.3 m to ~2.3 m. **That is the one thing to check on the
bake**, and the cheapest check is the navmesh: `RaidNavBake` must still report a walkable floor and the
hero must reach the courtyard. If `wall_gated`'s arch is not itself navigable and 2.3 m proves tight,
the answer is a wider gate cut, not a narrower tower.

### 3b. The instrument - because no log could have caught this

`PlaceGatehouse`'s line printed the **opening's** width and the art's **name**. No height, anywhere, on
this path. Now:

```
[Flow:RaidBase] WALL token='wall_broken' kit=hexagon-green radius=31.0m module=4.00m wide, achievedH=3.80m panels/side=16 step=3.88m
[Flow:RaidBase] GATE south width=8.55m art=wall_gated gateH=4.00m wallH=3.80m gate/wall=1.05 flank=wall_pillar flankH=4.00m flank/wall=1.05 flankOffset=6.28m
```

`CladRing` now returns the height it achieved (measured off a **placed panel, after the fit scale**,
not off the prefab) and `Dress` hands it to `PlaceGatehouse`, so the ratio is computed from what the
scene will actually contain. Permanent instrumentation (CLAUDE.md sec.12). Interpolated parts are
computed into locals first - the gate's brace scanner has no interpolated-string model (sec.1), and the
old line was exactly the `$"..."`-with-a-`?:`-and-nested-quotes shape that breaks it.

---

## 4. Pins - checked and kept

- **`entranceCount`, the gate POSITION `(0, 0.05, -/+31)`, and the `Structure` layer assignment**: all
  untouched. `PlaceGatehouse` still places one gate per side at `+/-ctx.Radius`.
- **`ctx.GateWidth` and its derivation** (`RaidBaseGenerator.cs:547`) and `MinGateWidth = 3.5f`:
  untouched. **This ticket changed the ART, never the opening** - `width` is still an input.
- **WO-1638's read-only pin is RE-POINTED, NOT DELETED.** Its bullet in
  `WORK_ORDER_1638_...md` now records that WO-1689 owns the gate MODULE / flank MODULE / flank OFFSET /
  trace line, and states what the pin still protects for every other lane (position, layer,
  `entranceCount`).
- **WO-1637's landed values** - `RockPaths`, the 45/200 fog, `DefaultWall`'s `"wall"`,
  `wallModule: "wall_broken"` - all untouched. The wall was **not** shrunk back to make the gate fit.
- **WO-1638's landed value** - the Gatehouse prop row is still `flag_red`.
- **`RaidBaseLayoutRegression`'s existing lints**: `Zone_Gatehouse` (1), `Zone_Courtyard` (1),
  `GarrisonSlot_` (5), `def.raidDress.props` (3), `AssetRoots.StructureContent` (1),
  `MinGateWidth = 3.5f` (1) all still present; forbidden `DefaultProps` and `def.props` both still **0**.
- **`RaidArenaShapeRegression`'s tokens**: untouched (this ticket did not edit `RaidBaseGenerator.cs`).
- **Canonical JSON in BINARY**: LF **361 unchanged** in both mirrors, CRLF 361, NUL 0, 20453 -> 20437
  bytes, mirrors **byte-identical** (sha256 `d894bade8fe84e55...`), both re-parsed as JSON, and the
  occurrence count was asserted `== 2` before the write. `fortified_garrison`'s gate row was **not**
  matched by the pattern and is confirmed still `SM_Bld_Castle_Wall_Gate_01`.

---

## 5. Gate checks

`python tools/gate_brace.py` on all three `.cs`: **`GATE_BRACE_SUMMARY bad=0 of 3`**, exit 0. NUL bytes
0, no BOM, per-file newline shape preserved.

⚠ **The RAW brace one-liner reports 84 open / 86 close on `RaidBaseLayoutRegression.cs`, and that is
the RAW counter being wrong, not the file.** The two extra closes are the string literals
`"\n        }"` at `:510-511` - the method-body terminators. `CompileGate.BraceBalanced` skips string
literals, and `gate_brace.py` (its exact port, which CLAUDE.md sec.1 names as the authority to run
before any gate) reports clean. **Named here so the next seat does not "fix" a balanced file.**

## 6. What is NOT done

- **No bake, no compile gate** (the lane holds no Unity seat). The RED/GREEN above is a port of the
  case's logic, not a suite run. **Not proven: that the C# compiles.**
- **Acceptance items 1-5 of WO-1689 are OPEN.** They need the bake log's `WALL` + `GATE` lines and a
  device frame from the hero seat, in colour and greyscale.
- **Not proven: navigability** - sec.3a. This is the one thing that could bite, and it has a named
  cheap check.
- **`iron_bastion` remains conditional** on the bake proving its entry point calls `Dress` at all
  (WO-1637 sec.11.2).

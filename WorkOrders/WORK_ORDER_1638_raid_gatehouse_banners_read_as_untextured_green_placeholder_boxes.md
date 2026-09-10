# WO-1638 - Two flat green slabs stand on the raid base's south wall line and read as placeholder art

**Status:** READY TO IMPLEMENT
**Minted:** 2026-09-10 (CLI minting lane, main-line banner; number block 1637-1642 pre-assigned by the
lead, banner bumped 1637 -> 1643 in the SAME edit)
**Silo / Lane:** World / Raid scenes - DRESSING only.
`Assets/Editor/WallTools/RaidBaseDresser.cs` prop path, and the `raidDress` data behind it.
WARNING: **Raid builders are a serialization bottleneck (CLAUDE.md sec.9) - ONE agent at a time.** See sec.8.
**Severity:** P2 felt-art. Two of the most eye-catching objects in the arena read as unfinished next to
finished crates, racks and tents. Directly under the owner's complaint.
**Type:** EXISTING system. The art loads, is textured, is correctly scaled and is placed exactly where
the code says. Nothing is broken; it just reads wrong.
**Owner words:** *"i have mentioned it in testing that it feels incomplete and not polished"*
(2026-09-10).

---

## STOP: 0. THE BRIEF'S PREMISE IS FALSIFIED. READ THIS BEFORE ANYTHING ELSE.

The brief that produced this ticket said: *"two flat untextured bright-green boxes stand on the base
wall line where the gates are (the scout report says 2 gates) - find the gate marker builder; spec the
authored gate art."* Every clause of that except "two flat green boxes" is wrong, and a lane that acts
on it will re-dress a gate that is already correct.

**These are not the gates, and no gate marker is missing.**

1. **The two gates are on OPPOSITE walls, each dead centre.**
   `Assets/Scenes/RaidBase_raider_camp_small.unity:53639` - `Gatehouse_south` at `(0, 0.050, -31)`;
   `:21185` - `Gatehouse_north` at `(0, 0.050, +31)`. Two objects symmetric left and right of centre on
   ONE wall line cannot be the two gates.
2. **Both gates are real authored KayKit art.** Source guid `be6f23503acaba849bfd4cfdec7f01e2` resolves
   (via that file's own `.fbx.meta:2`) to
   `Assets/Models/KayKit/KayKit Medieval Hexagon Pack 1.0.1/Assets/fbx(unity)/buildings/neutral/wall_straight_gate.fbx`.
   No placeholder fired.
3. **"Untextured" is a misread.** `.../buildings/green/hexagons_medieval_URP.mat:11` is URP/Lit with a
   real `_BaseMap` (`:28-29`, texture guid `3248d57370cb53742b4c76b7ff097ac0` ->
   `hexagons_medieval.png`) and `_BaseColor` white (`:119`). KayKit's atlas is a FLAT-COLOUR palette, so
   correctly-rendering KayKit green art looks flat by design.
4. **No Unity primitive is involved.** `RaidBaseDresser.cs` contains zero `GameObject.CreatePrimitive`.
   `RaidBaseGenerator.cs` has five (`:802`, `:809`, `:817` in `BuildFallbackSpire`; `:1213`, `:1220` in
   `BuildFallbackTurret`), every one immediately given a URP/Lit material through `ApplyUrpMaterial`
   (`:828-835`, the committed `MagentaGuard.BuildUrpLitMaterial` path), and every colour used is
   purple-grey, violet, near-black, brown or red-brown (`:807`, `:815`, `:822`, `:1218`, `:1225`).
   **No green.** Primitives are ruled out.

**So the real ticket is: authored art, correctly placed, that READS as a placeholder.** That is a
dressing decision, and it is the owner's call which way it goes (sec.4).

---

## 1. What was measured

### 1a. The frames, and what the object actually is

Device frames, build 363529, Seeker, 2670x1200:

- `Builds/device-frames/2026-09-10_0608_arena_01_entry.png` - one green slab left of centre
  (around x 660-720 on the original) and one right of centre, behind the readout panel.
- `Builds/device-frames/2026-09-10_0609_arena_02_pan_left.png` - the same pair.
- `Builds/device-frames/2026-09-10_0613_arena_05_hero_left_edge.png` - one at close range, around
  x 1985-2015, y 455-570.

**Zoomed 5x out of the 2670x1200 originals** (crops written to the session scratchpad from `..._0613`
at box `(1960,440)-(2040,580)` and `..._0608` at `(640,520)-(740,640)`):

> a flat green rectangular slab hanging from a horizontal top bar, with **two small dark grey finials
> at its top corners**, faint vertical shading down the cloth, no other detail.

That is a **hanging banner**, not a box, and not a tower. It is at hero scale, it stands on the wall
line, and next to the tan rock, the grey railing and the red-roofed tent it is the highest-chroma
object in the frame - which is why it grabs the eye and reads as unfinished.

### 1b. Which object it is - one candidate matches the shape, one does not

The scene has exactly two symmetric pairs near the south wall:

| scene line | name | position | source art |
|---|---|---|---|
| `:15390` / `:51119` | `Prop_flag_green` x2 | x = -/+6.476, y ~ 0, z = -28.5 | guid `e275bfa05131a654e9975ba7da0c45e1` = `.../decoration/props/flag_green.fbx` |
| `:22055` / `:40534` | `GateFlank_south_L` / `_R` | x = -/+4.704, y = 0.0034, z = -31 | guid `fcf76db4544b7e5498781f360bda601d` = `.../buildings/green/building_watchtower_green.fbx` |

Each guid was confirmed by opening that file's own `.meta` at line 2. The two flag line numbers are
the `m_Name` override rows and were re-read at source this session; the `GateFlank` and `Gatehouse`
line numbers came from a scan of the scene's `PrefabInstance` blocks and are the lane's to re-confirm
the same way before it edits either object.

**The zoom in sec.1a matches `flag_green` and does not match a watchtower** - a KayKit hexagon flag is
a pole plus a flat cloth slab with finials; `building_watchtower_green` is a squat tower silhouette
with a roof. That is the reading, and it is a reading of a picture.

WARNING: **NOT PROVEN, and Step 1 in sec.4 exists to settle it.** The frames cannot name a GameObject. Do not
edit either object until the discriminator below has run.

Neither pair carries any `m_LocalScale` override in its `PrefabInstance` modification list, so this is
NOT a squashed mesh or a degenerate scale - both render at authored prefab scale.

### 1c. Where each is placed, in code

- **The flags** - `RaidBaseDresser.PropSlot`, `Assets/Editor/WallTools/RaidBaseDresser.cs:924-927`:

      else if (string.Equals(zone, "Gatehouse", System.StringComparison.OrdinalIgnoreCase))
      {
          float flank = Mathf.Max(5f, ctx.GateWidth * 0.5f + 2.2f);
          return new Vector3((k % 2 == 0 ? -1f : 1f) * flank, 0f, -ctx.Radius + 2.5f);
      }

  `max(5, 8.553*0.5 + 2.2) = 6.476`; `z = -31 + 2.5 = -28.5`. **Exactly** the two scene positions. The
  data-to-code-to-scene chain is closed.
- **The flanks** - `RaidBaseDresser.cs:403-412`: `offset = Mathf.Max(3.2f, width * 0.55f)` =
  `max(3.2, 8.553*0.55) = 4.704`. Also exact.
- **The gates** - `RaidBaseDresser.PlaceGatehouse` (`:386-416`), called at `:154-156`; the model is
  loaded at `:389` and instantiated at `:396` (`PrefabUtility.InstantiatePrefab`, `:243`). The dresser
  assigns **no** material and touches **no** renderer on the gate - it sets the layer only
  (`:397-401`). Materials come from the FBX importer remap
  (`wall_straight_gate.fbx.meta:6-11`).

### 1d. "2 gates" is honest

`Assets/Resources/Data/Canonical/scene-configs.json:128` - `"entranceCount": 2`, in the
`"id": "raider_camp_small"` block (same block: `"wallTier": "Wood"`, `"baseRadius": 31`,
`"raidDress": { "kit": "hexagon-green", "gate": "wall_straight_gate", ... }`). Field declared at
`Assets/_Modules/Village/World/SceneConfigCatalog.cs:165`. The scout line the player reads is composed
from the SAME field - `Assets/_Modules/Village/Hero/RaidDeployVM.cs:503-508` - and the generator reads
it too (`RaidBaseGenerator.cs:490` -> `:545`). Frame `..._0605_raid_staging.png` shows the line:
`Walls: Wood, 2 gates`. **The report and the geometry agree. Nothing to fix here.**

---

## 2. What is NOT claimed

- **NOT claimed that the gate art is wrong, missing or placeholder.** Sec.0. Do not re-spec it.
- **NOT claimed which of the two candidate pairs is in the frame.** Sec.1b. The shape match is a
  reading of a zoomed picture, not a scene query.
- **NOT claimed the green is a material failure.** The material resolves, has a texture and is white-
  tinted (sec.0 item 3). If the lane wants the material PROVEN at render rather than at import, say so
  and prove it - do not assert it either way.
- **NOT claimed the other camps have the same defect.** Only `raider_camp_small` was played and only
  its scene was opened. `fortified_garrison`, `mage_enclave` and `IronBastion` are unexamined.
- **NOT claimed the north wall looks the same.** The camera never faced south or west
  (`WORK_ORDER_1632_raid_arena_exterior_boundary_ring.md:512-521`), so the north gatehouse's flag pair
  is not evidenced in any frame.

---

## 3. THE OWNER QUESTION

The object is authored art doing what it was placed to do. Whether it should be there is a creative
call, and per CLAUDE.md sec.2 the owner makes it. Ask, with the zoom from sec.1a attached, via
`AskUserQuestion`, and offer no default:

- keep the banner as the gatehouse marker but re-material or re-tint it so it sits in the camp's
  palette instead of shouting out of it;
- swap it for a different gatehouse prop (a brazier, a banner on a wall bracket, a shield rack);
- remove the pair and leave the gatehouse to the gate plus its flank towers.

STOP: **The owner is colourblind (memory `owner-colorblind-delegate-visual-creative`) - do NOT ask her to
pick a hue.** Ask which OBJECT belongs there. If the answer is "re-tint", the value choice is the
lane's and the gate on it is a greyscale check.

---

## 4. The fix

### Step 1 - IDENTIFY, before any edit (CLAUDE.md sec.12: no edit on an unproven cause)

Two ways, either is sufficient; do the cheap one first.

1. **Read the bake log.** `RaidBaseDresser.cs:415` already prints the resolved art per gate:
   `FlowTrace.Step(Sys, $"GATE {side} width={width:F2}m art=...")`, tag `RaidBase` (`:27`), plus
   `:458` (`gate mouth <side> props=N`) and the aggregate at `:171-174` / `:705`. That names what the
   gate resolved to. WARNING: **It does NOT name the flank or the individual props** - there is no
   per-prop trace, only a count. If the bake log cannot answer, use 2.
2. **Disable and re-shoot.** Disable the two `Prop_flag_green` children of `Zone_Gatehouse` in
   `RaidBase_raider_camp_small` and take the frame again from the same seat. If the green slabs vanish
   it is the flags; if they remain it is `GateFlank_south_L/_R`.

**Add the missing trace while you are there** and leave it in (CLAUDE.md sec.12): one
`FlowTrace.Step(Sys, ...)` per placed prop naming zone, index, resolved art and world position. A
count is not an identification, and this ticket exists because the log could not answer a question the
frames raised.

### Step 2 - dress, from the sec.3 ruling

Whatever the owner picks, it lands in the **dresser prop path and/or the `raidDress` data**, never in
the gate placement. If the answer is a different prop token, prefer the DATA edit
(`raidDress.props` in `scene-configs.json`) over a code edit - and see sec.8, because that array
belongs to another open ticket.

Then RE-BAKE. These objects are baked scene content, not runtime-built; nothing changes on device
until the scene is re-baked. **Never run a bake with the Unity editor open** (CLAUDE.md sec.3), and
bake commands go to the lead.

---

## 5. Acceptance

1. **The identification is stated with its evidence** - which pair, proven by the log line or the
   disable-and-re-shoot, not by the shape reading in sec.1b.
2. **A frame from the same seat** as `..._0608` / `..._0613`, after the re-bake, showing the
   gatehouse. Paste it. The owner's standing rule is *"I want images to verify anything that is a
   viewable issue"*.
3. **A greyscale check of that frame** - the new object must still read as a distinct object with the
   colour removed (owner is colourblind; greyscale is the gate).
4. **The gates are untouched.** `Gatehouse_south` / `Gatehouse_north` still resolve to
   `wall_straight_gate` in the bake log, still at `(0, 0.050, -/+31)`.
5. **The scout line still reads `Walls: Wood, 2 gates`** and `entranceCount` is unchanged.
6. **Brace and NUL checks** on every `.cs` touched, including `python tools/gate_brace.py` on each
   (CLAUDE.md sec.1 - the gate counts differently from the raw one-liner).

---

## 6. Pins - what must not move

- `entranceCount` in `scene-configs.json` (both the `Assets/Resources` copy and the byte-equal
  `Assets/StreamingAssets` mirror) and its two readers, `RaidDeployVM.cs:503-508` (the scout line) and
  `RaidBaseGenerator.cs:490` / `:545` (the geometry). **One field, two readers, currently in
  agreement. Keep it that way.**
- `RaidBaseDresser.PlaceGatehouse` (`:386-416`) - gate placement, gate art token, the layer assignment
  at `:397-401`. Read-only in this lane.
- `HideWallRenderers` (`RaidBaseDresser.cs:339-350`) - it disables renderers on `Wall_`-prefixed
  objects only and must keep ignoring `Gatehouse_*`, `GateFlank_*` and `Prop_*`.
- `ApplyUrpMaterial` / `MagentaGuard.BuildUrpLitMaterial` (`RaidBaseGenerator.cs:828-835`) - the
  never-the-default-material path. Nothing in this ticket goes near it.
- WO-1632's boundary ring and WO-1633's cover-prop rings. Both are FIXED and baked; do not disturb
  their placements while editing the same dresser.

**Canonical JSON warning:** if the fix touches `scene-configs.json`, edit it in BINARY and prove the
newline count (memory `canonical-json-edits-binary-only-verify-newlines` - a text-mode rewrite once
flattened twelve of these files).

---

## 7. What NOT to touch

- The gate prefab, the gate token, `entranceCount`, or anything that would change the number of gates.
- `RaidBaseGenerator`'s fallback spire / turret builders and their primitives. Ruled out in sec.0;
  leave them.
- The exterior boundary ring, the spire and the base wall material - **that is WO-1637**, a different
  ticket on the same scenes. Do not widen into it, and read sec.8 before touching either.
- Other camps' scenes unless the owner extends the ruling to them - say so explicitly if you do.
- Do **not** commit. Do **not** push. Hand the diff back (CLAUDE.md sec.11).

---

## 8. SEQUENCING - read before starting

Four raid tickets touch this ground. The lead sequences; the lanes do not overlap (CLAUDE.md sec.9,
`VillageSceneBuilder`-class serialization applies to the raid builders too, per
`WORK_ORDER_1619_raid_spire_height_capped_by_the_8x_scale_factor.md:5-6`).

- **WO-1632** (boundary ring) and **WO-1633** (cover props) are both FIXED and baked. Rebase onto
  them and re-confirm the line numbers in sec.1c after the rebase.
- **WO-1634** (`raid_camp_authored_prop_gaps`) is READY and **owns the `raidDress.props` arrays of
  `scene-configs.json`**. If this ticket's answer is a data edit to those arrays, **it lands after
  WO-1634 and rebases onto it** - or the lead merges the two. Do not edit that array concurrently.
- **WO-1635** (`retire_legacy_props_set_and_stale_dresser_canon`) is IMPLEMENTED with one item
  deliberately red on `fortified_garrison`'s `raidDress.props`. Read its status line before touching
  the dresser's prop path.
- **WO-1637** (this block, the ring / spire / wall MATERIAL ticket) touches the same scenes and may
  touch the same dresser. **One agent on the raid builders at a time.**

State in the hand-back which commit was rebased onto.

---

## 9. Board

This lane owns this ticket. Its hand-back is incomplete until this file's `**Status:**` line is
flipped and
`WorkOrders/WORK_ORDER_1638_raid_gatehouse_banners_read_as_untextured_green_placeholder_boxes.RESULT.md`
is written, with both paths reported. The lead regenerates `BOARD.html`.

---

## 10. STEP 1 HAND-BACK - RAID-ART lane, 2026-09-10 (instrument + identify; NO dressing changed)

**Status deliberately NOT flipped.** The lead's brief scopes this lane to sec.4 Step 1 only. This
file stays `READY TO IMPLEMENT` until the owner rules on sec.3 and Step 2 lands, and no
`.RESULT.md` is written. That overrides sec.9 for this pass, by the lead's instruction.

**Rebased onto:** `f33451b11` (`git merge --ff-only refs/heads/dev`, fast-forward from
`f5d39acd1`). Every line number below was re-read at source AFTER that rebase.

**Sec.0 honoured:** gate placement, the gate token, `entranceCount` and `PlaceGatehouse` were NOT
touched, read-only. Nothing in this pass changes a placement, a prop, a token or any data.

### 10.1 The trace that was added

| File | Lines (post-edit) | What |
|---|---|---|
| `Assets/Editor/WallTools/RaidBaseDresser.cs` | `:924-963` | **`TraceProp(string zoneName, RaidDressPropDef p, int index, GameObject model, GameObject placed)`** - one `FlowTrace.Step` on tag `Sys` (`"RaidBase"`, `:27`) per PLACED prop: the zone GameObject it landed under, the AUTHORED `p.zone` string, the instance index, `model.name`, `AssetDatabase.GetAssetPath(model)`, the WORLD position to F3, the instance name and whether it is a cover prop. Wrapped in `Guard.Try`. |
| | `:882` | call site in `PlaceCourtyardCluster`, placed AFTER `go.transform.localScale *= slot.Scale` and `SeatOnGround(go)` so the logged world position is the one that ships. |
| | `:916` | call site in `PlaceZoneProps` - **this is the one that covers the Gatehouse zone**, and therefore the two objects this ticket is about. |

This closes the gap sec.4 step 1 names: the dresser logged a COUNT
(`"gate mouth south props=N"` `:512`, `"props <id> total=N"` `:759-762`) and a count is not an
identification. It is PERMANENT instrumentation (CLAUDE.md sec.12), bake-time and editor-only, never
on a frame path.

*(This lane also added the WO-1637 material / atmosphere traces in the same files - see WO-1637
sec.11. They are additive and disjoint from this ticket's prop path.)*

### 10.2 THE IDENTIFICATION - static reading of the scene, with citations

Scene: `Assets/Scenes/RaidBase_raider_camp_small.unity`, parsed this session by walking each
`--- !u!1001 PrefabInstance` block and reading its `m_SourcePrefab` guid, its `m_TransformParent`
and its `m_LocalPosition` overrides.

STOP: **THE LINE NUMBERS IN SEC.1B ARE STALE.** That table cites `:15390` / `:51119` (flags),
`:22055` / `:40534` (`GateFlank_south_L/_R`) and `:53639` / `:21185` (gatehouses). After the rebase
onto `f33451b11` the flags are unchanged but **the gatehouse and flank rows have MOVED**. Sec.1b
itself flagged those as "the lane's to re-confirm the same way before it edits either object" - they
were, and four of the six had moved. Current values:

| object | name-override line | block header line | source guid | parent | local position |
|---|---|---|---|---|---|
| `Prop_flag_green` | `:15390` | `:15340` (`&447269132`) | `e275bfa05131a654e9975ba7da0c45e1` | `859173303` | (+6.4764786, 0.0000079, -28.5) |
| `Prop_flag_green` | `:51119` | `:51069` (`&1590553026`) | `e275bfa05131a654e9975ba7da0c45e1` | `859173303` | (-6.4764786, 0.0000079, -28.5) |
| `GateFlank_south_L` | `:44715` | `:44665` (`&1363995993`) | `fcf76db4544b7e5498781f360bda601d` | `859173303` | (-4.704127, 0.0034347, -31) |
| `GateFlank_south_R` | `:30439` | `:30389` (`&929314104`) | `fcf76db4544b7e5498781f360bda601d` | `859173303` | (+4.704127, 0.0034347, -31) |
| `GateFlank_north_L` | `:12254` | `:12204` (`&364332944`) | `fcf76db4544b7e5498781f360bda601d` | `859173303` | (+4.704127, 0.0034347, +31) |
| `GateFlank_north_R` | `:26743` | `:26693` (`&822802589`) | `fcf76db4544b7e5498781f360bda601d` | `859173303` | (-4.704127, 0.0034347, +31) |
| `Gatehouse_south` | `:68721` | `:68667` (`&2120792871`) | `be6f23503acaba849bfd4cfdec7f01e2` | `859173303` | (0, 0.0500361, -31) |
| `Gatehouse_north` | `:8557` | `:8503` (`&252421004`) | `be6f23503acaba849bfd4cfdec7f01e2` | `859173303` | (0, 0.0500361, +31) |

**All eight share parent `fileID: 859173303`, and that transform is `Zone_Gatehouse`:** GameObject
`&859173302` at `:28172`, `m_Name: Zone_Gatehouse` at `:28181`, its Transform `--- !u!4 &859173303`
at `:28187`. So **the flags ARE the `Prop_flag_green` children of `Zone_Gatehouse`** - sec.4 step 1
option 2's phrasing is correct on the parentage. **None of the eight carries an `m_LocalScale`
override**, confirming sec.1b: no squashed mesh.

**Guid -> asset path** (each read from that file's own `.fbx.meta` line 2):

| guid | asset |
|---|---|
| `e275bfa05131a654e9975ba7da0c45e1` | `Assets/Models/KayKit/KayKit Medieval Hexagon Pack 1.0.1/Assets/fbx(unity)/decoration/props/flag_green.fbx` |
| `fcf76db4544b7e5498781f360bda601d` | `.../fbx(unity)/buildings/green/building_watchtower_green.fbx` |
| `be6f23503acaba849bfd4cfdec7f01e2` | `.../fbx(unity)/buildings/neutral/wall_straight_gate.fbx` |

WARNING: **Read from the MAIN tree, not this worktree.** `Assets/Models/*` is gitignored
(`.gitignore:124-125`), so the KayKit packs do not exist in an agent worktree at all. The three
`.meta` files were opened under the repo's own checkout. **The `fbx` (non-`unity`) twins carry
DIFFERENT guids** - `flag_green.fbx` is `55fc800f2b1a4d34b8e27c736ab91888` and
`building_watchtower_green.fbx` is `51710785969fe21419d15fb3fe65cac6` - so the scene binds the
`fbx(unity)` variants specifically, and a search that finds the wrong twin will read the wrong
importer settings.

### 10.3 WHICH pair the green slabs are - the static discriminator, and its limit

**The instance COUNT settles it as far as a static reading can.**

- `flag_green` (`e275bf...`) appears **exactly TWICE** in the whole scene - the two rows above.
  Both are on the SOUTH gatehouse.
- `building_watchtower_green` (`fcf76d...`) appears **TWELVE** times: the four `GateFlank_*` above,
  plus **eight** instances named `Visual` (block headers `:12654`, `:25418`, `:32816`, `:33783`,
  `:40674`, `:53483`, `:63201`, `:66223`) - those are the turret reskins, which
  `ReplaceChildrenWith` names `"Visual"` (`RaidBaseDresser.cs:655`).
- `wall_straight_gate` (`be6f23...`) appears **twice** - the two gatehouses. Sec.0 item 1 and
  sec.1d hold exactly as written.

`RaidStagingPoint` - the hero's deploy seat - is at local `(0, 0, -51.199013)`
(GameObject `&160286183`, `m_Name` at `:4894`; Transform `&160286184`, `m_LocalPosition` at `:4910`).
So the hero starts **due south of the base facing north**, which is why the compass strip in the
frames reads NW / N / NE and why the SOUTH gatehouse is the one in shot. From that seat the flag
pair is **23.6 m** away and the flank pair **20.7 m** - both in the near field, both symmetric about
x = 0, so distance alone does not separate them.

**The count does.** The frames report exactly TWO green slabs (sec.1a). If the slab were
`building_watchtower_green`, the same art stands **twelve** times in this scene - the north
gatehouse's flank pair and eight wall-ring turrets - and the frame from that seat would carry many
more than two of them. The art that appears exactly twice, only on the south gatehouse, is
`flag_green`.

STOP: **This is a STATIC reading, not a render proof, and it is labelled so on purpose.** It reasons
from instance counts and positions in the saved scene plus a shape reading of a zoomed frame
(sec.1a). It does NOT name the GameObject the renderer drew. **Acceptance item 1 is still OPEN**;
it closes on the next bake, when the `:916` `PROP zone=Zone_Gatehouse ... token='flag_green' ...
world=(6.476, 0.000, -28.500)` line prints - or, failing that, on sec.4 step 1 option 2
(disable-and-re-shoot). No bake was run this pass: the lead holds the single Unity seat.

### 10.4 NEW FINDING, not fixed - the north gatehouse has NO flags

`PropSlot`'s Gatehouse branch (`RaidBaseDresser.cs:1022-1026` post-edit) is:

    float flank = Mathf.Max(5f, ctx.GateWidth * 0.5f + 2.2f);
    return new Vector3((k % 2 == 0 ? -1f : 1f) * flank, 0f, -ctx.Radius + 2.5f);

`z` is **hardcoded to `-ctx.Radius + 2.5`** for every instance index `k`. There is no north branch
and no side parameter, so **every prop authored to the `Gatehouse` zone lands on the SOUTH gate,
however many are authored.** The scene confirms it: two `flag_green`, both at `z = -28.5`, none at
`z = +28.5`. `PlaceGatehouse` DOES place both gates and both flank pairs (`:465-466`), so the
asymmetry is the PROP path's alone. This is why sec.2's "NOT claimed the north wall looks the same"
is true in a stronger sense than it was written: the north gatehouse **cannot** look the same,
because it has no banners at all. Recorded for the sec.3 ruling - **not touched**, because fixing it
is a placement change and this pass changes no placement.

### 10.5 The three options, for the owner (sec.3) - what the code would change, and what it reaches

The owner is colourblind (memory `owner-colorblind-delegate-visual-creative`) - each option is stated
as an OBJECT decision. No default is proposed.

1. **KEEP the banner, re-material / re-tint it so it sits in the camp's palette.**
   *Code change:* nothing in the placement path. It is a MATERIAL change on the KayKit hexagon atlas
   binding, or a per-instance material override applied in `InstantiateVisual`
   (`RaidBaseDresser.cs:236-254`) for this token.
   *Reach:* **`flag_green` is bound in this scene ONLY** (2 instances, both here). But the atlas
   behind it, `hexagons_medieval_URP.mat`, is the shared `hexagon-green` kit material - re-tinting
   **the atlas** would move every hexagon-green piece in every camp on that kit (`raider_camp_small`
   and `iron_bastion`, both via `KitFor`, `:258-262`), including the gates, the flank towers and the
   eight turret `Visual`s. A per-token override reaches only the banners. **Say which.**
   *Does NOT reach the battle arena's siege venue* - `ProceduralSiegeArenaBuilder` uses no KayKit art.

2. **SWAP for a different gatehouse prop** (brazier / wall-bracket banner / shield rack).
   *Code change:* prefer the DATA edit - the `Gatehouse`-zone row in `raidDress.props` for
   `raider_camp_small` in `Assets/Resources/Data/Canonical/scene-configs.json` (the block at
   `:66-140`), plus its byte-equal `Assets/StreamingAssets` mirror. **WARNING: That array belongs to
   WO-1634** (sec.8) - this lands after it or the lead merges the two. Canonical JSON is edited in
   BINARY with the newline count proven (memory `canonical-json-edits-binary-only-verify-newlines`).
   *Reach:* one camp, if done as data. If instead done by changing a `Default*` helper in the
   dresser, it reaches every `hexagon-green` camp.

3. **REMOVE the pair** and leave the gatehouse to the gate plus its flank towers.
   *Code change:* delete the `Gatehouse`-zone row from that camp's `raidDress.props` - same file,
   same WO-1634 dependency, same binary-edit rule. No C# change at all.
   *Reach:* one camp. **WARNING: Consider sec.10.4 first:** the north gatehouse is already bare, so
   "remove" makes both gates match, and "keep / re-tint" leaves an asymmetry the ruling may want to
   close separately.

**WARNING: Shared-palette note the lead must carry to the owner:** none of these three touches
`ArenaBoundaryRing.RockPaths`. That array IS shared with `ProceduralSiegeArenaBuilder` (its
`RockPaths` property delegates to it, `:79-80` post-edit), which consumes it twice - a 56 m rock
cover ring and the 72 m `OuterBoundary_Ring`. **That sharing is WO-1637's axis 1, not this
ticket's.** It is repeated here only so the two rulings are not conflated.

### 10.6 What is NOT done, and NOT proven

- **No bake was run**, so the new `PROP` lines have produced ZERO output and acceptance items 1-5
  are all OPEN. The lead holds the single Unity seat (an APK build was in flight).
- **No compile gate was run.** `python tools/gate_brace.py` and a NUL / raw-brace check pass on all
  four touched files (see the lane hand-back). That is a brace proof, not a compile proof.
- **Not proven: which pair the renderer actually drew.** Sec.10.3. The static reading is stated as a
  reading.
- **Not proven: that the material resolves at RENDER as it does at import.** Sec.2 asked for this
  explicitly if claimed - it is not claimed. WO-1637's new `MAT` trace
  (`ArenaBoundaryRing.TraceMaterials`) reports the ring, spire and base wall; **it is NOT wired to
  the prop path**, so a banner's material is still unproven. If the sec.3 answer is "re-tint", wire
  `TraceMaterials` into `TraceProp` first and prove it.
- **One prop path is still COUNT-ONLY, deliberately: `PlaceGateFlanks`** (`RaidBaseDresser.cs:487-512`).
  It instantiates `rubble_large` / `crate_large` as `"GateFlankProp"` at `:507` from a hardcoded
  `xs`/`zs` grid, not from a `RaidDressPropDef`, so `TraceProp` cannot take it - it has no authored
  row to name. It still logs only `"gate mouth <side> props=N"` (`:512`). Those objects are rubble and
  crates, not banners, so they are NOT a candidate for this ticket - but a reader of the new `PROP`
  lines will see gatehouse-adjacent objects with no line of their own, and that is why. A sibling
  trace for it is cheap and unclaimed.
- **Nothing was changed on the other three camps**, and their scenes were not opened except for the
  baked lighting block (WO-1637 sec.11.2).

## 11. Lead addendum - the PROP trace on the fresh bake (2026-09-10, `Builds/wave5-bake1`)

```
[Flow:RaidBase] PROP zone=Zone_Gatehouse authoredZone=Gatehouse i=0 token='flag_green' art='flag_green' asset='Assets/Models/KayKit/KayKit Medieval Hexagon Pack 1.0.1/Assets/fbx(unity)/decoration/props/flag_green.fbx' world=(-6.476, 0.000, -28.500) name='Prop_flag_green' cover=no
[Flow:RaidBase] PROP zone=Zone_Gatehouse authoredZone=Gatehouse i=1 token='flag_green' art='flag_green' asset='Assets/Models/KayKit/KayKit Medieval Hexagon Pack 1.0.1/Assets/fbx(unity)/decoration/props/flag_green.fbx' world=(6.476, 0.000, -28.500) name='Prop_flag_green' cover=no
```

Render-side confirmation of s.10: the only two `flag_green` placements in the scene are the Gatehouse pair at (+/-6.476, 0, -28.5) - the south gatehouse; no north-gatehouse flag line exists. The identification is now trace-proven, not static.

## OWNER RULING (2026-09-10 12:07, AskUserQuestion)

RE-TINT the flag_green pair into the camp palette (keep the banners; lane picks the value; greyscale gate).

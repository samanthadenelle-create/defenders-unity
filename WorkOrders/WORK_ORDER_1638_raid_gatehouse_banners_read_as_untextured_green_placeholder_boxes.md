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

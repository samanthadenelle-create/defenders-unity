# WO-1637 - The raid arena reads flat: the boundary ring, the spire and the sky are one pale tan, and the base wall is a low grey railing

**Status:** IMPLEMENTED - awaiting bake + device frame (lane RAID-ART-2, 2026-09-10; all three ruled axes landed edit-only on `e4b5906a5`; RESULT at `WorkOrders/WORK_ORDER_1637_raid_arena_reads_flat_ring_spire_and_sky_are_one_pale_tan.RESULT.md`)
**Minted:** 2026-09-10 (CLI minting lane, main-line banner; number block 1637-1642 pre-assigned by the
lead, banner bumped 1637 -> 1643 in the SAME edit)
**Silo / Lane:** World / Raid scenes - the MATERIAL / PALETTE / ATMOSPHERE seam.
`Assets/Editor/ArenaBoundaryRing.cs` (the palette), `Assets/Editor/WallTools/RaidBaseDresser.cs`
(`DressAtmosphere`), `Assets/Editor/WallTools/RaidBaseGenerator.cs` (the ring caller).
WARNING: **Raid builders are a serialization bottleneck (CLAUDE.md sec.9) - ONE agent at a time.** See sec.9.
**Severity:** P1 felt. This is the single most likely source of the owner's complaint. The arena has
every piece it was asked for and none of them read.
**Type:** EXISTING system. Nothing failed. The ring shipped, the spire shipped, the props shipped, no
material fell back. What is wrong is the PALETTE and the ATMOSPHERE, both authored, both deliberate,
and both at values that erase the geometry they were built to show.
**Owner words:** *"i have mentioned it in testing that it feels incomplete and not polished"*, and the
direction for this ticket: *"similar strategy as we used in battle arena"* (2026-09-10).

---

## 1. What was measured

Build 363529 on the Seeker. Frames under `Builds/device-frames/`, all 2670x1200 landscape. Logcat:
`Builds/device-frames/2026-09-10_raid_logcat_stream.txt`.

### 1a. The frames

Opened this session: `..._0608_arena_01_entry.png`, `..._0609_arena_02_pan_left.png`,
`..._0613_arena_05_hero_left_edge.png`, `..._0614_arena_06_wide.png`.

Three things are true in every one of them:

1. **The boundary ring and the sky are the same value.** A band of tan rock pillars runs the full
   2670 px of the horizon. Where the ring meets the pale horizon sky there is almost no edge: the
   silhouette survives only because the pillars are slightly darker than the sky directly behind them.
   In `..._0609` the ring's far arc dissolves into the horizon entirely.
2. **The spire is the same family.** The pale cream tower at centre reads as one more piece of the
   same tan, not as the objective.
3. **The base wall is a low grey balustrade, not a wall.** In `..._0613` and `..._0614` the base
   perimeter is a knee-high grey stone railing with regular posts, and the hero and the deployed
   troops stand taller than it. Nothing about it says "wall you must breach".

WARNING: **Scope of the claim: the NORTH-facing arc only.** The compass strip in these frames reads NW / N /
NE / E; the camera never faced south or west, and the hero went down about 10 s after the clock
engaged so no further camera position was reachable
(`WORK_ORDER_1632_raid_arena_exterior_boundary_ring.md:512-521`). The southern and western arc is NOT
evidenced.

### 1b. The ring's material - one flat untextured swatch, and this is the finding

`Assets/Editor/ArenaBoundaryRing.cs:62-68` is the palette:

    /// <summary>The boundary palette - "a low wall of large rocks" (the siege venue's own words).</summary>
    public static readonly string[] RockPaths =
    {
        "Stones_M/Stone_Large.prefab",
        "Stones_M/Rock_Pillar.prefab",
        "Stones_M/Stone_Medium_Flat.prefab",
    };

Root at `:50-51`: `Assets/polyperfect/Low Poly Ultimate Pack/_M/Prefabs_M/Nature_M/`.

**All three prefabs bind the SAME single material.** `Stone_Large.prefab:64` and its two siblings
reference material guid `a7862d5615928c34e868320de65c2777`, which resolves to
`Assets/polyperfect/Low Poly Ultimate Pack/Materials/Colors/M_14_Brown_lightest_LPUP.mat`. Read at
source:

- `:24` shader guid `933532a4fcc9baf4fa0491de14d08ed7` = **Universal Render Pipeline/Lit**.
- `:134-135` `_BaseColor` and `_Color` = **(0.863, 0.749, 0.604)** - pale tan.
- `_BaseMap` `m_Texture: {fileID: 0}` - **no albedo texture at all.**

So the entire exterior boundary ring - 329 `Boundary_*` objects, root `ArenaBoundary_Ring` at
`Assets/Scenes/RaidBase_raider_camp_small.unity:65517` - is one flat, untextured, pale-tan swatch.
No albedo variation, no normal map, no second material. **That alone makes it read flat and tan
regardless of anything else.** `Materials/Colors/` holds 52 such flat swatches; the palette picked
the one named `Brown_lightest`.

### 1c. Nothing fell back - this is authored, not broken

- `ArenaBoundary_Fallback` (the primitive-cylinder tint at `ArenaBoundaryRing.cs:368-377`) appears
  **zero** times in the scene. The prefab path resolved.
- The three rock prefab guids appear 2147 / 1853 / 2014 times in the scene - real prefabs, not stand-ins.
- **The spire is real KayKit art.** Scene `:13385-13402`: the `RaidSpire` object is a prefab instance
  of guid `b2990eb9bc5cb384fbd2124fe3854f6b` =
  `Assets/Models/KayKit/KayKit Medieval Hexagon Pack 1.0.1/Assets/fbx(unity)/buildings/green/building_tower_base_green.fbx`.
  No `SpireFallback` / `Shaft` / `Crown` / `Plinth` in the scene: the purple obelisk fallback
  (`RaidBaseGenerator.cs:747`, built `:798-820`) did NOT fire.
- **No material failed on device.** Every `[Flow:MagentaProbe]` line in the session logcat
  (`:9399`-`:9415`, 05:58:19) is the town's dungeon-portal VFX, **before** the raid loaded at
  06:02:17.884. Zero magenta findings in `RaidBase_raider_camp_small`.

STOP: **Do not read `art='tower_ruined_watchtower'` in the device log as proof of a mesh.**
`RaidSpire.Configure` (`Assets/_Modules/Village/Troops/RaidSpire.cs:143-150`) only records strings;
`_catalogId` is documented at `:83-84` as *"trace/report only"*. The line at `:161-162` prints a
serialized string. The scene reading above is the proof, not the log.

### 1d. The fog, and it is baked - device-confirmed

`RaidBaseDresser.DressAtmosphere` (`Assets/Editor/WallTools/RaidBaseDresser.cs:312-337`) writes
`RenderSettings` at BAKE time, so the values persist into the shipped scene. The default branch, all
four lines re-read at source this session at `:332-335` (the two earlier branches are dungeon
`:318-321` and synty-castle `:325-328`, and their values are in the contrast table below):

    RenderSettings.fogColor = new Color(0.66f, 0.58f, 0.42f);
    RenderSettings.fogStartDistance = 22f;
    RenderSettings.fogEndDistance = 95f;
    RenderSettings.ambientLight = new Color(0.42f, 0.36f, 0.26f);

Baked into `RaidBase_raider_camp_small.unity:17-18` and `:23`: `m_Fog: 1`,
`m_FogColor: {r: 0.66, g: 0.58, b: 0.42}`, `m_AmbientSkyColor: {r: 0.42, g: 0.36, b: 0.26}`.

**Confirmed on the device**, `Builds/device-frames/2026-09-10_raid_logcat_stream.txt:27994`, 06:02:18.128:

    [Flow:FloorDiag] LIGHTING scene='RaidBase_raider_camp_small' ambientMode=Skybox
    ambient=RGBA(0.420, 0.360, 0.260, 1.000) sun=Directional Light
    color=RGBA(1.000, 0.957, 0.839, 1.000) intensity=1

The ambient matches the authored value exactly, and a Directional Light exists at intensity 1 - so
"flat-shaded" is **not** an absent-light problem.

**Now put the two numbers together.** The ring sits on a square at +/-68.6 m
(`RaidBaseGenerator.cs:131-132`, from `MapHalfExtent = 70f` `:92`, `ArenaBoundaryEdgeTolerance = 1.2f`
`:106`, `StagingPlaneEdgeMargin = 4f` `:313`), i.e. **68.6 to 97 m from centre**. Linear fog runs 22 m
to 95 m. **Most of the ring is at 60-100% fog toward (0.66, 0.58, 0.42)** - a colour in the same hue
family as its own material (0.863, 0.749, 0.604) and as the ambient (0.42, 0.36, 0.26).

**That is the mechanism, and it is two authored decisions compounding:** a flat untextured tan palette,
then near-total fog toward tan, in tan ambient. Every one of them is correct in isolation. Together
they erase the ring the moment it is far enough away to be a horizon - which is the only place it ever
is.

For contrast, the other three raid scenes' baked fog: `RaidBase_fortified_garrison` (0.58, 0.55, 0.50);
`RaidBase_mage_enclave` (0.14, 0.13, 0.16); `RaidBase_IronBastion` `m_Fog: 0`.

### 1e. "The strategy we used in battle arena" - name it correctly

`Assets/Editor/ProceduralSiegeArenaBuilder.cs` **has no material palette and no material field.** Its
"palette" is a `string[]` of PREFAB PATHS, and materials arrive with the prefab. Say it that way; a WO
that asks the lane for "the siege venue's material palette" sends it looking for a type that does not
exist.

- `:71-72` - the siege venue DELEGATES its palettes to this very file:
  `private static string[] RockPaths => ArenaBoundaryRing.RockPaths;`
- `:149` - its own boundary is that same palette at radius 72 m, 40 pieces, scale 1.4-2.2.
- `:203-210` - `PlaceCoverRing` is now a two-line wrapper over `ArenaBoundaryRing.PlacePolarRing`.

**So the siege venue and the raid ring already share one palette, and it is the tan one.** The owner's
"similar strategy" therefore cannot mean "copy the venue's materials" - it already does. It means the
MECHANISM: **instantiate real vendor prefabs and let each prefab carry its own material**
(`ArenaBoundaryRing.cs:348`, `PrefabUtility.InstantiatePrefab`), with a `LogWarning` plus a tinted
collidered primitive on a pack miss (`:342-366`) so a bake never fails. That mechanism is sound and
stays. What changes is WHICH prefabs, or what atmosphere they are seen through.

The only two materials this code CONSTRUCTS, both `Shader.Find("Universal Render Pipeline/Lit")`:
`ProceduralSiegeArenaBuilder.cs:179-186` (`ArenaPlate_Ground`, `(0.34, 0.42, 0.24)`) and
`ArenaBoundaryRing.cs:368-377` (`ArenaBoundary_Fallback`, `(0.45, 0.44, 0.42)`). The comment at
`:337-341` says why the editor bake tints explicitly while the runtime `MagentaGuard` does not: *"an
editor bake SAVES the scene, so an unassigned material would persist as magenta in the shipped raid."*

### 1f. `MagentaGuard`, for the record

`Assets/_Modules/Core/MagentaGuard.cs` is a **runtime** guard against Built-in/Standard shaders being
STRIPPED from a URP build and resolving to `Hidden/InternalErrorShader` (header `:2-10`). It is not an
editor tool and not a missing-material guard for the bake path. `ProtectPrimitiveArt` (`:88-97`) marks
primitive-built art deliberate so the sweep repaints rather than hides it; the dresser calls it at
`:1005` and `:1014`. `BuildUrpLitMaterial` (`:881-892`) names everything it makes `EmergencyHero_URP` -
which appears **once** in this scene (`:63301`), a bake-time `ApplyUrp` product, and is neither the
spire nor the ring.

---

## 2. What is NOT claimed

- STOP: **NOT proven: what material each mesh in the frame actually resolved to at render.** The frames
  cannot show it and **no per-mesh material trace exists anywhere on this path** - see sec.4 Step 1.
  Silence from `MagentaGuard` / `MagentaProbe` means "nothing was magenta"; it does NOT confirm which
  material a healthy mesh used. Every material claim in sec.1 is read from the PREFAB, the `.mat` and
  the baked SCENE, and is labelled as such.
- **NOT proven: the base wall's material.** The `hexagon-green` kit's wall token is `barrier`
  (`RaidBaseDresser.cs:272-277`) and `LoadVisual`'s search order (`:177`, `:183`, `:196-209`, `:225`)
  was NOT traced to the folder it resolves for this camp. Both candidate atlases are textured (the
  KayKit dungeon `dungeon_texture_URP.mat:11` and the hexagon `hexagons_medieval_URP.mat:11,:28-29`
  each carry a real `_BaseMap`), so **there is no evidence the base wall is untextured.** The
  "grey railing" in sec.1a is a shape and scale reading, not a material finding.
- **NOT claimed the ring is missing, broken or fell back.** Sec.1c. It shipped and WO-1632's
  acceptance holds.
- **NOT claimed for the southern or western arc.** Sec.1a.
- **NOT claimed for the other three camps.** Only `raider_camp_small` was played and only its scene
  was opened. Their fog values (sec.1d) differ, so the compounding in sec.1d may not apply to them at
  all - `IronBastion` has fog OFF.
- STOP: **The brief's citation "WO-1607 section 4" for a kit table specifying a real wall look is WRONG,
  and this is recorded so it is not re-derived.** The phrase "a real wall look" appears nowhere in
  `WorkOrders/WORK_ORDER_1607_raid_bases_as_places.md`. Its **section 4** (`:87-114`) is the PACK
  table (which pack lives where, and the per-camp kit pick). The row the brief meant is in **section
  6, "Defense vocabulary", `:132-147`** - specifically `:139` **"Palisade / barrier - 'Thickness, not
  a stick' - Dungeon `barrier.fbx` / `barrier_half.fbx` / `barrier_corner.fbx` /
  `barrier_column.fbx`"** and `:140` **"Watchtower with a top - 'Platform + roof / crenel, not a
  cylinder'"**, with the requirement of at least four of eight defence elements per camp. Cite
  section 6, not section 4.

---

## 3. THE OWNER RULING - ask before changing a single value

Every candidate below changes how the game LOOKS, and the owner makes all final creative decisions
(CLAUDE.md sec.2). Ask via `AskUserQuestion`, with `..._0614_arena_06_wide.png` attached, and propose
no default.

The three axes, stated so she can pick without picking a colour:

1. **The ring's material.** Keep the vendor prefabs, change which swatch they carry (the pack has 52
   in `Materials/Colors/`), or change which prefabs - a darker rock family that is not this camp's
   ground colour.
2. **The atmosphere.** The fog end distance (95 m) sits inside the ring's own radius (68.6-97 m). Push
   the fog end out past the ring, or drop the fog density, or leave the fog and let the material
   carry the contrast.
3. **The base perimeter.** Whether the knee-high barrier reads as a wall at all is the WO-1607 sec.6
   `:139` question - "thickness, not a stick". This may be a HEIGHT and MASS decision rather than a
   material one.

STOP: **The owner is colourblind (memory `owner-colorblind-delegate-visual-creative`) - do NOT ask her to
pick a hue.** Ask which of the three axes to move and how far. The value choice is the lane's, and the
gate on it is a greyscale check (sec.6).

---

## 4. The fix

### Step 1 - INSTRUMENT. There is no per-mesh material trace, and that is a real gap.

Per CLAUDE.md sec.12 no edit is earned until captured data proves the cause, and sec.2 says the
material-at-render is exactly what is unproven. Today:

- `ProceduralSiegeArenaBuilder.cs` - **zero** `FlowTrace` calls. `Debug.Log` only (`:87`, `:151-153`,
  `:238`, `:264`, `:280`).
- `ArenaBoundaryRing.cs` - **zero** `FlowTrace`. Three `Debug` lines (`:194` band fit, `:322` pack not
  measurable, `:360` the primitive-fallback warning). `:360` is the ONLY line that would say the ring
  fell back, and it is a bake-time editor warning that never reaches a device log.
- `RaidBaseGenerator.BuildArenaBoundary` `:1394-1399` - one `Debug.Log` per bake, GEOMETRY only
  (piece, stride, gap, reach, jitter, `"kit landscape-rock"`). **No material named.**
- `RaidBaseDresser` - seven `FlowTrace` calls under tag `RaidBase` (`:27`), all at ART-TOKEN level
  (`:171-174`, `:415`, `:458`, `:635`, `:705-708`, `:1101`). **None reports a material.**

**Commission the trace, in the bake path, and leave it in.** One line per placed ring/spire/wall
family naming: the resolved prefab path, the renderer's `sharedMaterial.name`, its `shader.name`,
whether `_BaseMap` is null, and the `_BaseColor`. That single line answers every material question in
this ticket and in the next one, forever, and it is the asset CLAUDE.md sec.12 says instrumentation
exists to build.

Also print the resolved `RenderSettings` (fog on/off, colour, start, end, ambient) once per bake, so
the atmosphere half is in the same record.

Compute interpolated parts into locals before building either string - the gate's brace scanner has no
interpolated-string model (CLAUDE.md sec.1).

Run the bake, read the lines, and paste them. **Then** the sec.3 ruling is being made against measured
material state instead of against a prefab reading.

### Step 2 - the change, from the ruling

Whatever the owner picks:

- **Keep the mechanism.** Vendor prefab instantiation, `LogWarning`-never-fail on a pack miss, the
  explicit editor-bake tint. Sec.1e. Do not replace it with constructed materials.
- **A palette change belongs in `ArenaBoundaryRing.RockPaths` (`:62-68`) or in the swatch those
  prefabs bind - and BOTH are shared with the battle arena's siege venue** (`:71-72`,
  `ProceduralSiegeArenaBuilder.cs:149`). STOP: **Editing `RockPaths` changes the siege venue too.** If
  the two venues should now differ, say so out loud in the hand-back and split the palette
  deliberately - do not let the siege arena change as a side effect.
- **An atmosphere change belongs in `RaidBaseDresser.DressAtmosphere` (`:330-336`)** and applies to
  every camp on that branch. Name which camps move.
- **Re-bake.** All of this is baked scene content; nothing changes on device until the scene is
  re-baked. **Never run a bake with the Unity editor open** (CLAUDE.md sec.3); bake commands go to the
  lead.

---

## 5. Target - what "fixed" means

From the hero's seat, at the camera pitches these frames cover, the boundary ring reads as an
enclosing EDGE against the sky, and the spire reads as the objective rather than as more ring. Judged
on a device frame from the same seat, and judged in greyscale as well as in colour.

---

## 6. Acceptance

1. **The frames.** Fresh device frames from the same seat as `..._0608`, `..._0613` and `..._0614`,
   OPENED and pasted. Say in words what the ring reads as. The owner's standing rule is *"I want
   images to verify anything that is a viewable issue"*.
2. **The greyscale check.** The same frames desaturated. If the ring vanishes in greyscale it is not
   fixed - the owner is colourblind and greyscale is the gate.
3. **The trace.** Step 1's lines on a fresh bake log, naming the resolved material for the ring, the
   spire and the wall, plus the resolved `RenderSettings`. Paste them.
4. **The siege venue did not change** - unless the ruling said it should. Show a battle-arena frame
   either way.
5. **The other three camps.** State explicitly which of them the change reaches and which it does not.
6. **WO-1632 and WO-1633 still hold** - the ring is still continuous with no gap, the cover props are
   still at hero scale between the hero seat and the spire.
7. **Brace and NUL checks** on every `.cs` touched, including `python tools/gate_brace.py` on each
   (CLAUDE.md sec.1 - the gate counts differently from the raw one-liner).

---

## 7. Pins - what must not move

- **WO-1632's ring GEOMETRY.** `ArenaBoundaryHalfExtent` (+/-68.6 m, derived at
  `RaidBaseGenerator.cs:131-132` from `:92`, `:106`, `:313`), `ArenaBoundaryBandHalf` 2.6 m
  (`:127-128`), scale 2.2-3.4 (`:191-192`), overlap 0.7 (`:183`), `maxPerSide` 100 (`:194`),
  containment slack and headroom. **This ticket changes how the ring LOOKS, never where it is or how
  much it contains.** A containment regression here is a playable-boundary defect.
- **WO-1619's spire height.** `_visualHeight: 14.4` in the scene, `achieved=14.40m` in that ticket.
  Do not rescale the spire to make it read - change its material or its surround.
- `MagentaGuard` and `ProtectPrimitiveArt` (`MagentaGuard.cs:88-97`), and the dresser's two calls at
  `:1005` / `:1014`. Read-only.
- The `ArenaBoundaryRing` FALLBACK path (`:342-366`) and its explicit tint. It did not fire and it must
  stay able to.
- `RaidArenaShapeRegression.cs` and `RaidBaseLayoutRegression.cs` reference the ring. **They were NOT
  opened** - open them before editing and report what they assert; they may already pin geometry a
  material change must not disturb.

**Coverage gap:** nothing asserts the ring's material, its contrast against the sky, or the fog end
against the ring radius. A rule that reds when `fogEndDistance < ringRadius` is cheap and would have
caught this at the bake. Per `LayoutOracle.cs:17-20` a new rule must be SEEN RED first - write it
before the fix and paste the red. Scoped out as its own ticket if the lane prefers; raise it either way.

---

## 8. What NOT to touch

- The two green slabs on the gatehouse line - **that is WO-1638**. Same scene, different lane.
- The cover props (WO-1633), the prop data arrays (WO-1634), the legacy prop set (WO-1635).
- The raid HUD (WO-1639) and the staging screen (WO-1640).
- `Assets/Resources/Data/Canonical/scene-configs.json` unless the ruling requires a per-camp
  `raidDress` change - and if it does, see sec.9, because WO-1634 owns those arrays. **Canonical JSON
  is edited in BINARY with the newline count proven** (memory
  `canonical-json-edits-binary-only-verify-newlines`).
- Do **not** commit. Do **not** push. Hand the diff back (CLAUDE.md sec.11).

---

## 9. SEQUENCING - read before starting

`VillageSceneBuilder`-class serialization applies to the raid builders too
(`WORK_ORDER_1619_raid_spire_height_capped_by_the_8x_scale_factor.md:5-6`). Four other tickets touch
this ground:

- **WO-1632** (boundary ring) and **WO-1633** (cover props): FIXED and baked. Rebase onto them and
  re-confirm this ticket's line numbers after the rebase.
- **WO-1634**: READY, owns the `raidDress.props` arrays.
- **WO-1635**: IMPLEMENTED with one item deliberately red on `fortified_garrison`.
- **WO-1638** (this block): the gatehouse slabs, same scenes, likely the same dresser. **One agent on
  the raid builders at a time** - the lead sequences 1637 and 1638, they do not run concurrently.

State in the hand-back which commit was rebased onto.

---

## 10. Board

This lane owns this ticket. Its hand-back is incomplete until this file's `**Status:**` line is
flipped and
`WorkOrders/WORK_ORDER_1637_raid_arena_reads_flat_ring_spire_and_sky_are_one_pale_tan.RESULT.md`
is written, with both paths reported. The lead regenerates `BOARD.html`.

---

## 11. STEP 1 HAND-BACK - RAID-ART lane, 2026-09-10 (instrumentation only; NO ruling acted on)

**Status deliberately NOT flipped.** The lead's brief scopes this lane to sec.4 Step 1 only. This
file stays `READY TO IMPLEMENT` until the owner rules on sec.3 and Step 2 lands, and no
`.RESULT.md` is written. That overrides sec.10 for this pass, by the lead's instruction.

**Rebased onto:** `f33451b11` (`git merge --ff-only refs/heads/dev`, fast-forward from
`f5d39acd1`). Every line number below was re-read at source AFTER that rebase.

### 11.1 What was added - the per-family material trace and the atmosphere line

| File | Lines (post-edit) | What |
|---|---|---|
| `Assets/Editor/ArenaBoundaryRing.cs` | `:42-45` | `using System.Collections.Generic;` + `using DeNelle.Core.Diagnostics;` |
| | `:406-465` | **`public static void TraceMaterials(string flowSys, string family, string resolvedPath, GameObject inst)`** - one `FlowTrace.Step` per family naming the resolved prefab path, renderer count, and per DISTINCT `sharedMaterial`: `name`, `shader.name`, `_BaseMap` (texture name or `NULL`), `_BaseColor` as F3 RGB. `FlowTrace.Warn` when the instance has no `Renderer` at all. Wrapped in `Guard.Try` (CLAUDE.md sec.12 - no silent catch). |
| | `:119` / `:185` | `string flowSys = null` added as an OPTIONAL trailing parameter to `PlacePolarRing` and `PlaceSquarePerimeter`. Optional so no existing call site breaks; a null tag means "this caller does not trace". |
| | `:123-124`, `:238-241` | a `HashSet<string> traced` per call, so the trace fires **once per distinct prefab**, not once per piece - the ring is 300+ objects from three prefabs. |
| | `:140`, `:266` | the call sites, on the first instance of each palette entry. |
| `Assets/Editor/WallTools/RaidBaseGenerator.cs` | `:1388-1393` | `BuildArenaBoundary` now passes `RaidBaseDresser.Sys` as `flowSys`, so the ring's materials land on the SAME `[Flow:RaidBase]` tag the dresser already uses. |
| | `:784-789` | `PlaceSpire` traces the spire AFTER `SeatOnGround` and AFTER the fallback branch, so the primitive obelisk self-reports too. Family string carries `catalogId`; path is `entry.visualPrefabPath` or `<primitive obelisk>`. |
| `Assets/Editor/WallTools/RaidBaseDresser.cs` | `:407-413`, `:429-435` | `CladRing` traces the **base wall** family on its first placed panel: the token, the kit, the ring radius, `AssetDatabase.GetAssetPath(model)` and the material read-out. This is the half sec.2 records as NOT PROVEN. `CladRing` is called twice when `InnerLayers > 0`, so an inner keep wall gets its own line (different token + radius). |
| | `:312-324`, `:350`, `:353-378` | `DressAtmosphere(string kit)` -> `DressAtmosphere(string kit, string sceneId)`; new `TraceAtmosphere` emits ONE line per bake: `fog=ON/OFF mode=<FogMode> colour=(r,g,b) start=<m> end=<m> ambient=(r,g,b)`. |
| | `:148` | the single call site, now passing `def.id`. |
| `Assets/Editor/ProceduralSiegeArenaBuilder.cs` | `:67-73`, `:216-218` | TRACE ONLY. New `private const string FlowSys = "SiegeArena"`, passed through `PlaceCoverRing` -> `PlacePolarRing`, so the battle arena's own four cover rings + boundary ring report their materials too. **No palette, prop, plate or atmosphere value changed in this file.** |

**Design notes that are load-bearing, so they are not re-derived next time:**

- **`sharedMaterial`, never `.material`.** `.material` INSTANTIATES a copy; an editor bake SAVES the
  scene, so reading `.material` here would leak a duplicate material into the shipped raid. Written
  into the method header at `ArenaBoundaryRing.cs:394-397`.
- **The tag is a PARAMETER, not a literal.** `ArenaBoundaryRing` is in `DeNelle.Editor`;
  `RaidBaseDresser.Sys` (`"RaidBase"`) is in `DeNelle.EditorWallTools`, which references
  `DeNelle.Editor` and not the reverse (`Assets/Editor/WallTools/DeNelle.EditorWallTools.asmdef:4-9`,
  opened this session). Copying `"RaidBase"` into the helper would be the duplicated-state failure
  CLAUDE.md sec.2 / sec.5 / sec.16 each describe.
- **`DressAtmosphere` gained `sceneId` because `kit` cannot identify a camp.** `KitFor`
  (`RaidBaseDresser.cs:258-262`) returns `"hexagon-green"` for **both** `raider_camp_small` and
  `iron_bastion` - neither id matches the two named branches. A run that bakes every camp would
  otherwise print two indistinguishable atmosphere lines. Same defect WO-1619 fixed on the spire
  line.
- **The atmosphere values are read BACK OUT of `RenderSettings`, not echoed from the literals.**
  Echoing proves the method compiled; reading back proves what the scene will be saved with.

### 11.2 Measurements taken this session (no bake was run - see 11.4)

- **All four raid scenes' baked lighting block, read at source** (`.unity` lines `:17-23` in each):

  | scene | `m_Fog` | `m_FogMode` | `m_FogColor` | start | end | `m_AmbientSkyColor` |
  |---|---|---|---|---|---|---|
  | `RaidBase_raider_camp_small` | 1 | 1 (Linear) | 0.66, 0.58, 0.42 | 22 | 95 | 0.42, 0.36, 0.26 |
  | `RaidBase_fortified_garrison` | 1 | 1 | 0.58, 0.55, 0.50 | 28 | 115 | 0.38, 0.36, 0.33 |
  | `RaidBase_mage_enclave` | 1 | 1 | 0.14, 0.13, 0.16 | 18 | 88 | 0.22, 0.20, 0.24 |
  | `RaidBase_IronBastion` | **0** | **3 (ExpSquared)** | 0.5, 0.5, 0.5 | 0 | 300 | 0.212, 0.227, 0.259 |

  The first three match `DressAtmosphere`'s three branches exactly, confirming sec.1d.
  **`RaidBase_IronBastion` carries UNITY DEFAULTS** - fog off, ExpSquared, 0.5 grey. But
  `iron_bastion`'s config authors **no `raidDress` block at all**
  (`Assets/Resources/Data/Canonical/scene-configs.json:304-355`, opened this session - it has
  `props`, `garrison`, `themeColor`, `entranceCount: 1`, and no `raidDress` key), so `KitFor` sends
  it to `"hexagon-green"` and `DressAtmosphere` would set `fog = true` unconditionally
  (`RaidBaseDresser.cs:326-327`). **The scene on disk therefore did NOT come from a
  `Dress`-running bake of the current code, and NOTHING statically available says why.** That is a
  real open question and the new ATMOSPHERE line is what settles it on the next bake - it will
  either print for `iron_bastion` (and the scene is simply stale) or not print at all (and that
  scene's entry point never calls `Dress`). **Not proven either way here.**

- **`RockPaths` is shared with the battle arena's siege venue, confirmed at source**
  (`ProceduralSiegeArenaBuilder.cs:79-80` post-edit, `:71-72` pre-edit). It is consumed there
  **twice**: the `Rock` cover ring at radius 56 m / 16 pieces / scale 0.9-1.6
  (`ProceduralSiegeArenaBuilder.cs:150`) **and** the `OuterBoundary_Ring` at radius 72 m /
  40 pieces / scale 1.4-2.2 (`:149` pre-edit, `:157` post-edit). Sec.1e cited only the boundary; the
  cover ring is a second reach and is recorded here.

### 11.3 The three axes, for the owner (sec.3) - what the code would change, and what it reaches

The owner is colourblind (memory `owner-colorblind-delegate-visual-creative`) - each axis is stated
as an OBJECT / DISTANCE decision, never as a hue. No default is proposed.

1. **The ring's MATERIAL.**
   *Code change:* either `ArenaBoundaryRing.RockPaths` (`:65-70` post-edit) - swap which polyperfect
   prefabs the palette draws from - or re-bind the swatch those prefabs carry.
   *Reach:* **all four raid camps' boundary rings AND the battle arena's siege venue, in TWO places**
   (its 56 m rock cover ring and its 72 m outer boundary ring). `ProceduralSiegeArenaBuilder`
   delegates `RockPaths` to this very array (`:79-80`); there is no second copy to change and no way
   to move one venue without the other **unless the ruling deliberately splits the palette**. If the
   two venues should now differ, that split has to be said out loud and both venues re-baked.

2. **The ATMOSPHERE - fog end distance vs. the ring radius.**
   *Code change:* `RaidBaseDresser.DressAtmosphere`'s `hexagon-green` branch (`:342-348` post-edit) -
   push `fogEndDistance` past the ring, or lower the fog's reach, or leave it and let the material
   carry the contrast. The arithmetic is the finding: the ring sits **68.6-97 m** from centre
   (`RaidBaseGenerator.cs:131-132` from `:92`, `:106`, `:313`) and linear fog ends at **95 m**, so
   most of the ring renders at 60-100% fog toward a colour in its own hue family.
   *Reach:* the `hexagon-green` branch dresses **`raider_camp_small` AND `iron_bastion`** (both via
   `KitFor`, `:258-262`). It does **not** reach `fortified_garrison` (`synty-castle`) or
   `mage_enclave` (`dungeon-stone`), which have their own branches. It does **not** reach the battle
   arena at all - `ProceduralSiegeArenaBuilder` never touches `RenderSettings` (grepped this
   session). Every camp needs a re-bake for a change here to reach a device.

3. **The BASE PERIMETER - height and mass, not colour.**
   *Code change:* the wall token itself. `DefaultWall("hexagon-green")` returns `"barrier"`
   (`RaidBaseDresser.cs:272-277`), or a camp may author `raidDress.wallModule`. Changing it to a
   taller module changes what `CladRing` (`:391-438`) instantiates and what `MeasureLongest` /
   `FitPieceAlong` fit along the run. This is the WO-1607 sec.6 `:139` "thickness, not a stick"
   question.
   *Reach:* per-camp if done in `scene-configs.json` `raidDress.wallModule`; all `hexagon-green`
   camps if done in `DefaultWall`. **It does NOT reach the battle arena** - the siege venue has no
   clad ring. **WARNING: It also does not reach the shipped device until every affected camp is re-baked**,
   and `WO-1634` owns the `raidDress` arrays in that file (sec.9).

### 11.4 What is NOT done, and NOT proven

- **No bake was run, so the trace has produced ZERO lines.** The lead holds the single Unity seat
  (an APK build was in flight). Acceptance item 3 is therefore OPEN: it needs a fresh raid bake and
  the `[Flow:RaidBase] MAT ...` / `ATMOSPHERE ...` lines pasted. **Every material claim in this file
  is still a prefab / `.mat` / baked-scene reading, exactly as sec.2 says.**
- **No compile gate was run** (single Unity seat). `python tools/gate_brace.py` and a NUL/raw-brace
  check both pass on all four files - see the lane hand-back. That is a brace proof, not a compile
  proof.
- **No palette, no fog value, no prop, no prefab path and no `scene-configs.json` value was
  changed.** The sec.3 ruling has not been made and nothing here pre-empts it.
- **The regressions in sec.7 were opened.** `Assets/Editor/Regression/RaidArenaShapeRegression.cs`
  asserts SOURCE-TEXT tokens on this path - `CaseArenaBoundary` (`:591-624`) requires
  `"ArenaBoundaryRing.PlaceSquarePerimeter"`, `"ArenaBoundaryRing.RockPaths"`, `"gates=[none]"` and
  the containment constants in `RaidBaseGenerator.cs`; `"PlaceSquarePerimeter"`, `"PlacePolarRing"`,
  `"MeasureMinFootprint"` in `ArenaBoundaryRing.cs`; and `"ArenaBoundaryRing.PlacePolarRing"` in
  `ProceduralSiegeArenaBuilder.cs`. **Every one of those tokens is still present** - the edits are
  purely additive, and the two signature changes ADD an optional trailing parameter, so no call-site
  text moved. `:795-860` re-derives the band fit arithmetically from constants none of which were
  touched. `RaidBaseLayoutRegression.cs` source-lints the dresser (`:148-190`, `:242-276`) for
  `Zone_Gatehouse`, `Zone_Courtyard`, `GarrisonSlot_`, `def.raidDress.props`,
  `AssetRoots.StructureContent`, `MinGateWidth = 3.5f`, and FORBIDS `def.props` and `DefaultProps` -
  none of which this pass adds or removes. **Not proven: that either suite passes**, because no
  suite was run.
- **The coverage-gap rule sec.7 proposes** (red when `fogEndDistance < ringRadius`) was **NOT
  written**. It is a real gap and it is cheap; it needs its own ticket, and per
  `LayoutOracle.cs:17-20` it must be SEEN RED before the fix, which cannot happen without the Unity
  seat. **Raised, not done.**

## 12. Lead addendum - Step 1 trace lines on the fresh bake (2026-09-10 07:2x, `Builds/wave5-bake1`)

Ring, base wall, spire and atmosphere as the bake resolved them (sort -u of the MAT/ATMOSPHERE lines):

```
[Flow:RaidBase] ATMOSPHERE 'fortified_garrison' kit=synty-castle fog=ON mode=Linear colour=(0.580, 0.550, 0.500) start=28.0m end=115.0m ambient=(0.380, 0.360, 0.330)
[Flow:RaidBase] ATMOSPHERE 'mage_enclave' kit=dungeon-stone fog=ON mode=Linear colour=(0.140, 0.130, 0.160) start=18.0m end=88.0m ambient=(0.220, 0.200, 0.240)
[Flow:RaidBase] ATMOSPHERE 'raider_camp_small' kit=hexagon-green fog=ON mode=Linear colour=(0.660, 0.580, 0.420) start=22.0m end=95.0m ambient=(0.420, 0.360, 0.260)
[Flow:RaidBase] MAT ArenaBoundary (boundary ring) prefab='Assets/polyperfect/Low Poly Ultimate Pack/_M/Prefabs_M/Nature_M/Stones_M/Rock_Pillar.prefab' renderers=1 distinct=1 mat='M_14_Brown_lightest_LPUP' shader='Universal Render Pipeline/Lit' _BaseMap=NULL _BaseColor=(0.863, 0.749, 0.604)
[Flow:RaidBase] MAT ArenaBoundary (boundary ring) prefab='Assets/polyperfect/Low Poly Ultimate Pack/_M/Prefabs_M/Nature_M/Stones_M/Stone_Large.prefab' renderers=1 distinct=1 mat='M_14_Brown_lightest_LPUP' shader='Universal Render Pipeline/Lit' _BaseMap=NULL _BaseColor=(0.863, 0.749, 0.604)
[Flow:RaidBase] MAT ArenaBoundary (boundary ring) prefab='Assets/polyperfect/Low Poly Ultimate Pack/_M/Prefabs_M/Nature_M/Stones_M/Stone_Medium_Flat.prefab' renderers=1 distinct=1 mat='M_14_Brown_lightest_LPUP' shader='Universal Render Pipeline/Lit' _BaseMap=NULL _BaseColor=(0.863, 0.749, 0.604)
[Flow:RaidBase] MAT base wall token='SM_Bld_Castle_Wall_01' kit=synty-castle radius=22.1m prefab='Assets/Synty/PolygonFantasyKingdom/Prefabs/Castle/SM_Bld_Castle_Wall_01.prefab' renderers=1 distinct=1 mat='Castle_Wall_01' shader='Synty/Generic_Basic' _BaseMap=n/a _BaseColor=(1.000, 1.000, 1.000)
[Flow:RaidBase] MAT base wall token='SM_Bld_Castle_Wall_01' kit=synty-castle radius=49.0m prefab='Assets/Synty/PolygonFantasyKingdom/Prefabs/Castle/SM_Bld_Castle_Wall_01.prefab' renderers=1 distinct=1 mat='Castle_Wall_01' shader='Synty/Generic_Basic' _BaseMap=n/a _BaseColor=(1.000, 1.000, 1.000)
[Flow:RaidBase] MAT base wall token='barrier' kit=hexagon-green radius=31.0m prefab='Assets/Models/KayKit/KayKit Dungeon Remastered 1.1/Assets/fbx(unity)/barrier.fbx' renderers=1 distinct=1 mat='dungeon_texture_URP' shader='Universal Render Pipeline/Lit' _BaseMap=dungeon_texture _BaseColor=(1.000, 1.000, 1.000)
[Flow:RaidBase] MAT base wall token='wall' kit=dungeon-stone radius=24.3m prefab='Assets/Models/KayKit/KayKit Dungeon Remastered 1.1/Assets/fbx(unity)/wall.fbx' renderers=1 distinct=1 mat='dungeon_texture_URP' shader='Universal Render Pipeline/Lit' _BaseMap=dungeon_texture _BaseColor=(1.000, 1.000, 1.000)
[Flow:RaidBase] MAT base wall token='wall_cracked' kit=dungeon-stone radius=54.0m prefab='Assets/Models/KayKit/KayKit Dungeon Remastered 1.1/Assets/fbx(unity)/wall_cracked.fbx' renderers=1 distinct=1 mat='dungeon_texture_URP' shader='Universal Render Pipeline/Lit' _BaseMap=dungeon_texture _BaseColor=(1.000, 1.000, 1.000)
[Flow:RaidBase] MAT spire 'tower_arcane_spire' prefab='Structures/ArcaneSpire_1' renderers=1 distinct=1 mat='Color_bcf8a365-0849-42ab-9611-99d7fa0d2f81' shader='Universal Render Pipeline/Lit' _BaseMap=ArcaneSpire_Albedo _BaseColor=(1.000, 1.000, 1.000)
[Flow:RaidBase] MAT spire 'tower_ruined_watchtower' prefab='Structures/building_tower_base_green' renderers=1 distinct=1 mat='hexagons_medieval_URP' shader='Universal Render Pipeline/Lit' _BaseMap=hexagons_medieval _BaseColor=(1.000, 1.000, 1.000)
```

Reading: the boundary ring is ONE swatch, `M_14_Brown_lightest_LPUP` (URP/Lit, the pack's lightest brown) on all three rock prefabs; the green camp's base wall is the KayKit dungeon `barrier` piece (`dungeon_texture_URP`, radius 31.0 m); the Forsaken Camp spire is `building_tower_base_green` on the `hexagons_medieval` atlas; fog on raider_camp_small is Linear 22-95 m, colour (0.66, 0.58, 0.42) - the same tan family as the ring swatch. The ruling (s.3) is now being made against measured material state.

## OWNER RULING (2026-09-10 12:07, AskUserQuestion)

ALL axes move: fog first (end past the ring, density down; raider_camp_small + iron_bastion), base wall height/mass, and the ring material - she ticked both "split from battle arena" and "both venues"; the lead reads that as: move RockPaths for BOTH venues (one palette), and if the siege venue then reads wrong it is its own ticket. Value choices are the lane s; greyscale is the gate.

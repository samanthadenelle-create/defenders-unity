# WO-1638 RESULT - the two Gatehouse banners re-tinted, as a one-token data edit

**Status:** IMPLEMENTED - awaiting bake + device frame
**Lane:** RAID-ART-2, 2026-09-10
**Rebased onto:** `e4b5906a541c65fc12255a109b5dc3723e328488` (`git merge --ff-only refs/heads/dev`;
`8e170b555` confirmed an ancestor, so the OWNER RULING section was in the tree).
**No Unity was run, no bake, no gate, no commit.**

---

## 1. The ruling and the change

Owner ruling 2026-09-10 12:07: **"RE-TINT the two Gatehouse flag_green banners into the camp palette
(keep the props; the value is yours)."**

One token, in `raider_camp_small.raidDress.props`, in both `scene-configs.json` mirrors:

```
-          { "token": "flag_green", "count": 2, "zone": "Gatehouse" },
+          { "token": "flag_red",   "count": 2, "zone": "Gatehouse" },
```

**No C# was changed for this ticket.** (`RaidBaseDresser.cs` and `ArenaBoundaryRing.cs` also changed in
this lane's tree - that is WO-1637, a disjoint seam. This ticket's whole surface is the row above.)

---

## 2. WHY A VARIANT SWAP *IS* THE RE-TINT - and why it is the only clean one

WO-1638 sec.10.5 option 1 offered two shapes for "re-tint": move the shared atlas, or apply a
per-instance material override. **Both are worse, and the pack offers a third that is strictly better.**

- **Re-tinting the atlas** (`hexagons_medieval_URP.mat`) moves EVERY hexagon-green piece in
  `raider_camp_small` AND `iron_bastion` - the gates, the flank towers, the eight turret `Visual`s, the
  tents. A two-banner ruling would have repainted two camps.
- **A per-instance override** constructs a Material at bake time. An editor bake SAVES the scene
  (`ArenaBoundaryRing.cs`'s own header records why that matters), so it would leak a duplicate material
  into the shipped raid - and WO-1637's ruling for the same session says never constructed materials.
- **The pack already ships the same banner in four colourways.** Listed on disk this session:
  `decoration/props/flag_blue.fbx`, `flag_green.fbx`, `flag_red.fbx`, `flag_yellow.fbx`. All four are
  **byte-identical in size (20,540 bytes)** and were confirmed to differ only in their **UV region on
  the SAME atlas** - the FBX geometry nodes were parsed and the cloth UVs read out directly.

So the swap is **the same prop, the same mesh, the same scale, the same material, the same atlas** -
a different rectangle of `hexagons_medieval.png`. That is literally a re-tint of vendor art, with zero
new materials, zero placement change, and a blast radius of exactly two objects.

---

## 3. THE VALUE CHOICE, MEASURED - the owner is colourblind, so this was decided on luminance only

The owner never picked a hue and was never asked to (memory
`owner-colorblind-delegate-visual-creative`). The value was chosen by measuring, in this order:

**(a) what the banner actually hangs against.** The pair sits at `(+/-6.476, 0, -28.5)` - 2.5 m INSIDE
the south wall at radius 31, with the hero seat at `z = -51.2` facing north. So its background is the
base wall and, above the wall line, the boundary ring.

**(b) the atlas, sampled at each mesh's own UVs.** Every FBX's UV array was parsed out of the binary
and used to index `hexagons_medieval.png` / `dungeon_texture.png`, nudged off the seams. Luminance is
`0.2126R + 0.7152G + 0.0722B` over sRGB values - what a desaturated frame shows.

| surface | meanLum |
|---|---|
| `wall_broken` - the NEW base wall (WO-1637) | **0.496** |
| `wall` - iron_bastion's new base wall | 0.489 |
| `barrier` - today's rail | 0.475 |
| `wall_straight_gate` - the gate beside it | 0.471 |
| `flag_green` cloth (today) | **0.391** |
| **`flag_red` cloth** | **0.317** |
| `flag_blue` cloth | 0.414 |
| `flag_yellow` cloth | 0.703 |

**(c) confirmed against the shipped frame.** `Builds/device-frames/2026-09-10_0614_arena_06_wide.png`,
build 363529. The green pixels were isolated by connected-component search (`g > r+25 and g > b+25`)
rather than by eyeballing a box: the object's bounding box is **(1964,623)-(2036,734), 73 x 112 px**,
and the luminance sample lies inside it, so **0.384** is genuinely cloth and not cloth-plus-background.
Against the atlas value of 0.391 the render and the atlas agree to within **2%**, so the table above
predicts the frame.

**(d) THE IDENTIFICATION IS NOW VISUAL, not a count argument.** WO-1638 sec.10.3 labelled it a STATIC
reading and left acceptance item 1 OPEN; the lead's sec.11 addendum called it trace-proven, but the
`PROP` trace proves only *where `flag_green` is*, not that the slab in the frame *is* it. That blob was
cropped out of the original at **4x** and looked at directly. It is **a hanging cloth slab with two
small dark-grey finials at its top corners, faint vertical shading down the cloth, and no visible
pole** - precisely WO-1638 sec.1a's description of `flag_green`, and nothing like
`building_watchtower_green`'s roofed tower silhouette. **The identification holds.**

> ### ⚠ AN UNRESOLVED DISCREPANCY, RECORDED RATHER THAN PAPERED OVER.
> `flag_green.fbx` measures **0.06 x 0.28 x 0.26 m** (all four colourways are byte-identical in size
> and measure the same, which is itself the proof they differ only in UVs), and `PlaceZoneProps`
> applies **no scale** - it calls `InstantiateVisual` with a rotation only, and WO-1638 sec.10.2 found
> **no `m_LocalScale` override** on either scene instance. Yet in the frame the object stands roughly
> as tall as the base wall beside it, which is far more than a 0.28 m prop should.
> **I could not resolve this from here and I am not going to assert either way.** Either the prop is
> scaled somewhere I did not find, or the FBX unit conversion for this pack differs from the one I
> applied. **It changes no decision in this ticket** - the swap is a token edit on the row the lead's
> own `PROP` trace ties to `(+/-6.476, 0, -28.5)`, so it retargets whatever that row places.
> **But it gives the lead a sharp post-bake check:** if the slab in the new frame is **not** red, the
> identification was wrong and this ticket reopens.

**The decision:**

| variant | vs the wall (0.496) | verdict |
|---|---|---|
| `flag_green` (today) | **0.105** | the object the ticket is about |
| `flag_blue` | 0.082 | worse |
| **`flag_red`** | **0.179** | **+70% greyscale separation, and it moves DOWN in value** |
| `flag_yellow` | 0.207 | best separation, but at 0.703 it becomes the BRIGHTEST object in the arena |

`flag_red` was chosen because it does both halves of the ticket at once: it **raises** the greyscale
separation against its background by 70%, and it moves the banner **below** the wall's value, so it
stops being a high-value outlier. `flag_yellow` buys 0.028 more separation by re-creating the exact
failure the ticket describes - the loudest thing at the gate - and against WO-1637's newly darkened
arena (ring modelled at ~0.466, ground ~0.36) it would be louder still.

**And "into the camp palette" is literal, not a hunch.** The gate the banners flank,
`wall_straight_gate`, samples its dominant non-grey texels at rgb(178,112,82), (125,61,44) and
(131,67,49) - the red-brown timber/roof family. **Red cloth is the colour already standing at that
gate.**

⚠ **A finding that cuts against the ticket's own premise, recorded because it is true.** In the
shipped frame the green cloth is **0.384** and the base wall beside it is **0.386** - a delta of
**0.002**. So *for the owner, in greyscale, the banner was never the loud thing at all*; it only
shouted in chroma, which she cannot see. The re-tint is therefore justified on the **greyscale**
axis - giving the banner a real edge it never had - not on "it was too bright". Stating it the other
way round would have been a claim the measurement does not support.

---

## 4. Pins - checked and kept

- **`entranceCount` is untouched** in both mirrors, and so are its two readers. The scout line still
  composes to `Walls: Wood, 2 gates`.
- **`RaidBaseDresser.PlaceGatehouse` is untouched** - gate placement, gate token and the layer
  assignment are byte-identical. `Gatehouse_south` / `Gatehouse_north` still resolve to
  `wall_straight_gate` at `(0, 0.050, -/+31)`.
- **`HideWallRenderers` is untouched** - it still matches `Wall_`-prefixed objects only and still
  ignores `Gatehouse_*`, `GateFlank_*` and `Prop_*`.
- **`ApplyUrpMaterial` / `MagentaGuard.BuildUrpLitMaterial`**: not touched, not reached.
- **WO-1632's ring and WO-1633's cover props**: no placement in the dresser changed.
- **`flag_red.fbx` resolves, and the collision check was actually RUN.** `LoadVisual` walks
  `KayFolders` in order and returns the first hit, so an earlier folder holding a `flag_red` would
  silently give a different mesh. `find Assets/Models -iname 'flag_red*'` returns **only** the hexagon
  pack's `fbx/`, `fbx(unity)/`, `gltf/` and `obj/` copies; of those only
  `.../fbx(unity)/decoration/props/` is on the search path, at `KayFolders[6]`, and `.fbx` is
  `KayExts[0]`. Folders 0-5 were listed directly and hold **no** `flag_red` - the Dungeon Remastered
  pack's banners are all named `banner_*` (`banner_red`, `banner_thin_red`, `banner_patternA_red`...),
  a different token entirely. **No collision.**
  *(Related, for whoever picks up the `banner_green` note below: that token DOES resolve to the
  Dungeon pack at `KayFolders[0]`, not to the hexagon pack - a different kit and a different atlas.)*
- **Canonical JSON edited in BINARY** (memory `canonical-json-edits-binary-only-verify-newlines`):
  LF count **361 unchanged** in both mirrors, CRLF 361, NUL 0, mirrors **byte-identical**
  (sha256 `c28a82303c836c60...`), both re-parsed as JSON. The occurrence count of the replaced string
  was asserted `== 1` before the write.

---

## 5. Open items and findings NOT fixed

- **The `raidDress.props` array belongs to WO-1634** (sec.8). WO-1634's own file still says
  `READY TO IMPLEMENT` while its `.RESULT.md` says `IMPLEMENTED - awaiting gate + bake` - **its Status
  flip was left behind.** This lane's touch is one token on one row, so a merge is trivial, but **if a
  WO-1634 lane is live in another worktree the lead must sequence the two.**
- **`banner_green`, 2 in the `Approach` zone, was left green.** It is a different token and the ruling
  named the Gatehouse `flag_green` pair. **If the owner wants the approach banners matched, it is one
  more token on the row above** - say the word.
- **The gate beside these banners is now badly out of scale with its wall.** WO-1637 takes this camp's
  base wall from 1.10 m to ~3.8 m, while `wall_straight_gate` stays **2.00 x 1.41 m**, unscaled, in an
  **8.55 m** opening, flanked by miniature `building_watchtower_green` towers now shorter than the wall.
  `PlaceGatehouse` is pinned read-only here (sec.4), so it is **not touched** - but the banners hang
  right beside it, so whoever judges the post-bake frame for this ticket will see it. **Recommend a
  ticket:** scale the gate to its opening, or pick a gate module at wall scale.
- **The north gatehouse still has NO banners** (WO-1638 sec.10.4: `PropSlot`'s Gatehouse branch
  hardcodes `z = -ctx.Radius + 2.5`, so every Gatehouse-zone prop lands on the SOUTH gate). **Not
  fixed** - it is a placement change and this ticket changes no placement. Now that both gates get a
  real 3.8 m wall (WO-1637), the asymmetry will be more visible, not less. **Recommend minting it.**
- **Acceptance items 1-5 are OPEN.** No bake was run, so no post-change frame exists. The lead bakes
  and shoots from the `..._0608` / `..._0613` seat; the greyscale check is on that frame.
- **Not proven: what the red cloth reads as at 23.6 m against a freshly darkened arena.** Sec.3 is an
  atlas measurement plus a 2%-validated frame correlation, not a render. **The device frame is the
  gate.**
- **The bake log line to look for** (the `PROP` trace the Step 1 pass added at
  `RaidBaseDresser.cs:916`):
  ```
  [Flow:RaidBase] PROP zone=Zone_Gatehouse authoredZone=Gatehouse i=0 token='flag_red' art='flag_red'
    asset='...decoration/props/flag_red.fbx' world=(-6.476, 0.000, -28.500) name='Prop_flag_red' cover=no
  ```
  Two of those, at `x = -/+6.476`. A `WarnMissing('flag_red')` instead would mean the token did not
  resolve - see sec.4 for why it should.

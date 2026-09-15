# WORK ORDER 1747 — Raid watchtower / KayKit Cleric: `glass` material ships with NO albedo and NO tint (pink/grey patch on device)

**Status:** READY TO IMPLEMENT
**Minted:** 2026-09-15 by the lead, from the owner's felt-test on the Seeker (tester build `2026.09.15.371127`, scene `RaidBase_IronBastion`).
**Silo:** content / Addressables dependency closure. Files: whichever KayKit material named `glass` is referenced by the `NPCs/KayKit/Cleric` address (and, to be PROVEN not assumed, by the `Watchtower_Archer_*` prefab). Do NOT touch `RaidAssaultAi.cs` / `TroopController.cs` (WO-1746 silo) or any `.unity`.

> ⚠ **SEE `## Diagnosis 2026-09-15` §0 BELOW BEFORE EXECUTING ANY OF THIS SECTION — three framing
> errors (wrong pipeline, wrong screenshot, mislabelled timestamp) are corrected there, and
> Acceptance items 1 and 3 as written are unsatisfiable.**

## Evidence (captured, not theorised)
- F8 device capture **seq 5257** (`logs/f8-inbox/capture-device-20260915-134044-seq5257.md`), device error at `2026-09-15T18:40:44Z`:
  `[Flow:StructureAssets] dep MISS on 'NPCs/KayKit/Cleric': material 'glass' has NO albedo and NO tint — renders as an untextured grey blob. shader='Universal Render Pipeline/Lit' albedo slots scanned: _BaseMap=EMPTY, _MainTex=EMPTY`
  Logged by `Assets/_Modules/Core/Addressables/DependencyClosureTrace.cs:129`.
- Owner screenshot one second later (`logs/f8-inbox/device/SM02G4061955851/flag_20260915-183736_00.png`): the archer watchtower beside the hero shows a flat pink/white window patch mid-tower. The reticle log names it `Watchtower_Archer_3` (`DefenseTower`, faction Hostile).

## What is NOT proven
Whether the watchtower's pink patch IS the same `glass` material the Cleric trace names, or a second instance of the same class. The trace fires per address; the tower prefab was not traced in this capture. The lane proves the mapping first (open the tower prefab's materials; run the closure trace on its address) before fixing either.

## Acceptance
1. `DependencyClosureTrace` reports zero `dep MISS … NO albedo` for `NPCs/KayKit/Cleric` and for the `Watchtower_Archer` address on a fresh headless run.
2. The tower renders with no untextured patch in a captured PNG (headless capture or the owner's device screenshot).
3. Content re-pushed through `tools/r2-ship.ps1` (bundle hashes change); `R2_PARITY_OK` on a fresh log.
4. Lane flips this Status line and writes the `.RESULT.md`.

---

## Diagnosis 2026-09-15 (read-only lane — NO edits made, NO Unity, NO gate, NO build)

Every line below was opened at source this session. Where something is NOT proven it says so
(CLAUDE.md §11B-A). **No asset file was modified by this lane.**

### 0. THREE FRAMING ERRORS IN THE WO ABOVE — fix them before implementing

**(a) `NPCs/KayKit/Cleric` is NOT an Addressables address. It is a `Resources` path.**
Proof: it does not appear anywhere under `Assets/AddressableAssetsData/` (grepped the whole folder,
zero hits). It appears in exactly two files — `Assets/Resources/Data/Canonical/troops.json:79` and its
`Assets/StreamingAssets/` twin — as the `"model"` of the troop row `troop-field-cleric`
(`troops.json:75-79`, displayName "Field Cleric"). `KayKitNpcImporter.cs:14` states the contract
verbatim: *"Resources.Load("NPCs/KayKit/<slug>")"*, staged into
`Assets/Resources/NPCs/KayKit` (`KayKitNpcImporter.cs:42`). The asset on disk is
`Assets/Resources/NPCs/KayKit/Cleric.fbx` (530 KB, listed this session).
**Consequence: the Cleric half of this ticket needs NO Addressables content rebuild and NO R2 push.**
It is `Resources/`, it ships inside the APK. Acceptance item 3 does not apply to the Cleric fix.
(Section 3 below says when R2 *would* apply.)

**(b) The screenshot cited in Evidence is the WRONG FRAME — it is ~2.5 min BEFORE the error, not
one second after.** The capture header gives the clock offset: device `2026-09-15T18:40:12.818Z`
bridged local `2026-09-15 13:40:44`, i.e. **PC local = UTC − 5**. So:
- `flag_20260915-183736_00.png` → device stamp 18:37:36, **2m36s BEFORE** the trace fired.
- `break_07_error.png` (mtime `2026-09-15 13:40:13` local = **18:40:13Z**, one second after the
  18:40:12.818Z trace) → **the actual one-second-later frame**, and it is already listed in the
  capture's own "Screenshot candidates" block.

  *Three independent confirmations, because the filename's timezone was an assumption worth killing:*
  (i) `stat` gives `flag_…` mtime `13:40:36 -0500` = **18:40:36Z** — a file cannot be *pulled*
  18:40:36Z if it was *captured* at 23:37Z, so the "device stamps local time" inversion is impossible;
  (ii) the raid HUD: `flag_…` reads **2:37 / Troops 10/10 / 3 gold pips**, `break_07` reads
  **3:00 / Troops 0/0 / 0 grey pips** with the deploy toast — i.e. two different raid runs, and the
  2m36s gap between them is exactly the 2:37 left on the clock in `flag_…`;
  (iii) `break_07`'s mtime is 23 s EARLIER than `flag_…`'s, consistent with the harness pulling the
  error frame immediately and the older flag frame in the same sweep.

**(c) Line 8's timestamp is mislabelled.** `2026-09-15T18:40:44Z` is the **bridged PC-local**
`13:40:44` with a `Z` bolted on. The device UTC in the capture's own JSON is `2026-09-15T18:40:12.818Z`.
Read this session: `break_07_error.png` shows the on-screen toast **"Deployed 10 troops in assault
formation"** (which is what `RaidDeployController.DeployAll()` in the stack produces). Among the
freshly deployed troops, **two figures read as pale/blown-out** beside clearly-textured ones (dark
armoured knights, an orange-haired archer) — consistent with, but **not proof of**, the Cleric's
white `glass` submesh; only the `RaidBaseMatDiag` / closure-trace run in section 3 can name them.
What IS unambiguous in that frame: the watchtower in the foreground is **clean — no patch at all.**
So the WO conflated two different frames and, as section 2 proves, two different objects.

### 1. The `glass` material — FOUND, and it has no `.mat` file to edit

`glass` is an **FBX-EMBEDDED sub-asset of `Assets/Resources/NPCs/KayKit/Cleric.fbx`**, not a file.
Proof, all from `Assets/Resources/NPCs/KayKit/Cleric.fbx.meta` (read in full this session):
- `guid: 5f7c06026deac1f4d8770d261dfd933c`
- `externalObjects: {}`  ← **no remap for any material, including `glass`**
- `materialImportMode: 2`, `materialName: 0`, `materialSearch: 1`, **`materialLocation: 1`**
  (`materialLocation: 1` = *Use Embedded Materials* — Unity synthesises the material from the FBX's
  own material description as a sub-asset of the FBX).
- `grep -aoc glass Cleric.fbx` → **1** (the name is present in the FBX byte stream exactly once).
- `Assets/Resources/NPCs/KayKit/Materials/` contains eight `.mat` files
  (barbarian_texture, blackknight_texture, druid_texture, engineer_texture, mage_texture,
  paladin_texture_A, ranger_texture, tiefling_texture) — **there is no `cleric_texture.mat` and no
  `glass.mat`.** Directory listed this session.

So **`_BaseMap` / `_BaseColor` cannot be shown "at source" for this material: there is no source file.**
The embedded material is generated at import from the FBX's Phong description (diffuse white, no
texture), which is precisely why the runtime probe reports `_BaseMap=EMPTY, _MainTex=EMPTY` and an
untinted `_BaseColor`. The trace at `DependencyClosureTrace.cs:110-129` is correct, not a false
positive: its tint rule is `min(r,g,b) < 0.92` (`DependencyClosureTrace.cs:120`), and white fails it
(`FindAlbedo` at `:110`, the `dep MISS … NO albedo` Fail at `:129`).

**Scope sweep (so the fix is not scoped wrong):** `grep -aoc glass` over **all twelve** FBXs in
`Assets/Resources/NPCs/KayKit/` returns **1 for `Cleric.fbx` and 0 for every other one**
(Barbarian, BlackKnight, Druid, Engineer, Farmer_A, Farmer_B, Hoarder, Mage, Paladin_with_Helmet,
Ranger, Tiefling). **This is a Cleric-only defect — it is NOT a systemic `KayKitNpcImporter` gap**,
so do not "fix" the importer for it.

### 2. The watchtower — **REFUTED.** It does not use `glass`, and both its materials are textured.

`Watchtower_Archer_3` is created by `RaidBaseGenerator.cs:1053`
(`Label = $"Watchtower_{(isMage ? "Mage" : "Archer")}_{kindIndex}"`) and its art comes from
`entry.visualPrefabPath` (`RaidBaseGenerator.cs:1183-1184`). The scene is BAKED, so the chain was
read off the baked YAML rather than inferred:

- `Assets/Scenes/RaidBase_IronBastion.unity:83907` — `propertyPath: m_Name`, `value: Watchtower_Archer_3`,
  inside `PrefabInstance &1596642020` whose `m_SourcePrefab` (`:83925`) is
  **`guid: fdbdc8e2c3cad234eb116332a5148b42`** = `Assets/StructureContent/ArcaneSpire_1.fbx`
  (`ArcaneSpire_1.fbx.meta:2`). The `DefenseTower` added onto that same instance
  (`m_EditorClassIdentifier` at `RaidBase_IronBastion.unity:83947`) carries
  `CatalogId: tower_arcane_spire` (`:83957`), `Allegiance: 1`, `_maxHp: 200` — matching the catalog row
  `"id": "tower_arcane_spire"` (`structures-catalog.json:1123`) whose
  `"visualPrefabPath": "Structures/ArcaneSpire_1"` sits at **`:1135`**.
- That instance's `m_AddedGameObjects` adds `{fileID: 360820945}` (`:83914`), which resolves at
  `RaidBase_IronBastion.unity:19452-19455` to a Transform sourced from
  **`guid: fcf76db4544b7e5498781f360bda601d`** =
  `Assets/Models/KayKit/KayKit Medieval Hexagon Pack 1.0.1/Assets/fbx(unity)/buildings/green/building_watchtower_green.fbx`
  — i.e. the dresser's reskin child (`RaidBaseDresser.cs:472` returns `building_watchtower_green`;
  `RaidBaseDresser.cs:1204-1205` attaches it to any `Watchtower_*`).

Materials of **both** halves, read at source:

| FBX | material (from `.meta` `externalObjects`) | resolved `.mat` | `_BaseMap` | `_BaseColor` |
|---|---|---|---|---|
| `ArcaneSpire_1.fbx` (`meta:6-11`, ONE material) | `tripo_mat_23505017` → guid `ffbbdac90878f6c47a0033250d6c4304` | `Assets/StructureContent/Materials/Color_bcf8a365-0849-42ab-9611-99d7fa0d2f81.mat` | `:29-30` guid `c83598997a5b4f1797e66e82b1a8fb7a` = `Assets/StructureContent/ArcaneSpire_Albedo.png` — **resolves, not dangling** | `:122` `{1,1,1,1}` |
| `building_watchtower_green.fbx` (`meta:6-11`, ONE material) | `hexagons_medieval` → guid `0aa0933993543c644a299cff2683ea5e` | `…/fbx(unity)/buildings/green/hexagons_medieval_URP.mat` | `:28-29` guid `3248d57370cb53742b4c76b7ff097ac0` = `…/fbx(unity)/buildings/green/hexagons_medieval.png` — **resolves, not dangling** | `:119` `{1,1,1,1}` |

Both `_BaseMap` guids were resolved to a real `.png.meta` this session — **neither dangles.** (Worth
recording because the sibling `…/fbx/buildings/green/Materials/hexagons_medieval.mat` points at a
*different* texture guid, `4d14cc728ff6dcf4ca180df413047769`; the FBX remaps to the `_URP` one, and
that is the one that is live.)

Byte-level material-name enumeration of `building_watchtower_green.fbx` returns exactly one
`Material` node — `hexagons_medieval` — and `grep -aoc glass` on it returns **0**.
`ArcaneSpire_1.fbx` likewise carries exactly one material.

> **Conclusion for acceptance item 1: there is no "Watchtower_Archer address" to run a closure trace
> on, and no `glass` material anywhere in the tower's dependency closure. Acceptance 1's second half
> is unsatisfiable as written and must be rewritten.**

### 3. What the pink/white patch in `flag_20260915-183736_00.png` actually is — NOT PROVEN

Measured, not guessed: the patch pixel at (1300,580) in that PNG is **RGB (255,208,174)** — a
channel-clipped white surface under the warm sun + orange-terrain bounce — against neighbouring
tower woodwork at **(117,62,42)**. Zoomed crop confirms it fills the tower's two lookout openings.
It is unmistakably a **white / untextured URP-Lit surface**, i.e. the same *class* of defect the
Cleric trace names.

**Its owning renderer cannot be PROVEN from asset files — but there is now one named candidate,
with disk evidence, and it must be checked first.**

#### 3a. NAMED CANDIDATE (disk-evidenced, NOT proven): the ArcaneSpire mesh is still inside the shell

`RaidBaseDresser.ReplaceChildrenWith` (`RaidBaseDresser.cs:1275-1292`) destroys **only `host`'s
CHILDREN** (`host.transform.GetChild(i)` collected at `:1279-1283`, `DestroyImmediate` at `:1284`),
then parents the new model under the same host (`:1286-1290`). It never touches renderers on the
host itself.

And the baked scene records **`m_RemovedComponents: []` (`RaidBase_IronBastion.unity:83909`) and
`m_RemovedGameObjects: []` (`:83910`)** for this instance — i.e. **nothing at all was removed from
the ArcaneSpire_1 prefab instance.** `ArcaneSpire_1.fbx` contains exactly **one** `FbxMesh`
(byte-counted this session). So the spire mesh is still in the scene, **co-located inside the
height-fitted KayKit shell** (the dresser's own comment at `RaidBaseDresser.cs:1234` notes the child
"inherits the host's fitted localScale", so the two are the same size). **An inner mesh visible only
through the outer shell's window holes is exactly the shape measured above.**

⚠ **Why this is a candidate and not yet a cause:** the spire's material `_BaseMap` resolves on disk
(section 2), so from disk it should render textured, not white. The escape hatch — and it is a §16
escape hatch — is that **both the spire's art and its texture are REMOTE Addressables entries**:
`Assets/AddressableAssetsData/AssetGroups/Structure_Art.asset:100-101` registers
`m_Address: Structures/ArcaneSpire_1`, and **`:196-197` registers
`m_GUID: c83598997a5b4f1797e66e82b1a8fb7a` / `m_Address: Structures/ArcaneSpire_Albedo` — the very
texture the scene's spire material points at.** Per CLAUDE.md §16 a missing/stale remote bundle fails
**silently and renders untextured white, with no error on screen.** Whether Unity duplicated that
texture into the in-APK scene data (which would make it render fine) or left it to the remote bundle
**was not determined this session and cannot be determined from these files** — it is a build-output
question. The `RaidBaseMatDiag` run below answers the editor half; a device `logcat` grep for
`ArcaneSpire` / `RemoteProviderException` answers the shipped half (§12's 2026-08-20 precedent, where
one device-log grep settled an hour of static theorising).

#### 3b. What is positively RULED OUT
- Both tower materials are proven textured on disk with resolving `_BaseMap`s (section 2), so it is
  **not** a missing/dangling material assignment in either prefab, and it carries no `glass`.
- `GarrisonTurretArmer` / `RaidGarrisonSpawner` place **no body** in a watchtower —
  `RaidGarrisonSpawner.cs:180-184` only calls `GarrisonTurretArmer.ArmWatchtowers` (adds the
  `DefenseTower` component), and `GarrisonTurretArmer.cs` contains no `Instantiate` / `Skin` /
  body call at all (grepped, zero hits).
- The frame predates the Cleric deploy by 2m36s, so it is **not** a deployed Field Cleric.

**The instrument that answers it already exists and has not been run: `Assets/Editor/RaidBaseMatDiag.cs`**
— read-only, opens `Assets/Scenes/RaidBase_IronBastion.unity` (it is in its scene list at `:50`),
dumps every renderer's shader + `_BaseMap` + `_BaseColor`, probes six albedo slot names (`:53-58`),
and is judged by the marker **`RAIDBASE_MATDIAG_OK <scenes>` on a fresh log** (`:28`), never the exit
code. Run line, verbatim from its own header (`:23-25`):
`powershell tools\run-unity-method.ps1 -Method DeNelle.Editor.RaidBaseMatDiag.Run -LogName raidbase-matdiag.log`
**That run must come BEFORE any edit aimed at the tower patch** (§12: instrument first).

### 4. Recommended fix — Cleric half only (the tower half is not yet earned)

**Authored sibling value, read at source:** `Assets/Models/KayKit/KayKit Board Game Bits 1.0/Assets/fbx/Materials/glass.mat`
— `m_Name: glass` (`:23`), URP/Lit shader guid `933532a4fcc9baf4fa0491de14d08ed7` (`:24`),
**`_BaseMap` `m_Texture: {fileID: 0}`** (`:41-42`) and **`_MainTex` `{fileID: 0}`** (`:65-66`) — i.e.
**KayKit authors `glass` with no albedo on purpose**; the glassiness is carried by
**`_BaseColor: {r: 1, g: 1, b: 1, a: 0.1}`** (`:134`). The trap: `_Surface: 0` (`:128`) and
`_Blend: 0` (`:102`) mean **Opaque**, so that `a: 0.1` is *ignored* and it renders as the opaque white
blob the owner sees. ⚠ **Do not copy this material as-is** — it reproduces the defect, and its RGB
(1,1,1) still trips `DependencyClosureTrace`'s `min(r,g,b) < 0.92` rule (`:117`).
Eleven other `glass.mat` files exist across the KayKit packs (listed this session, incl.
`KayKit Mystery Monthly Series 5/10 - April 2025 - Protagonists/characters/Materials/glass.mat`);
none was opened, so none is asserted here.

**The fix shape (NOT applied by this lane — it needs a reimport to prove, which needs Unity):**
because `glass` is embedded, there is no `.mat` to one-field-edit. The correct change is an
`externalObjects` remap in `Assets/Resources/NPCs/KayKit/Cleric.fbx.meta`, replacing
`externalObjects: {}` (line 6) with a mapping from the material name `glass` to a real, authored
material, exactly mirroring the shape already proven to work in `ArcaneSpire_1.fbx.meta:6-11`:

```yaml
  externalObjects:
  - first:
      type: UnityEngine:Material
      assembly: UnityEngine.CoreModule
      name: glass
    second: {fileID: 2100000, guid: <GUID OF THE TARGET .mat>, type: 2}
```

The target `.mat` should be a **new** URP/Lit material authored for this (owner is colourblind —
do not ask her to pick a hue; §memory `owner-colorblind-delegate-visual-creative`). Two options for
the lead / owner to rule on:
1. **Transparent glass (faithful to KayKit's intent):** `_Surface: 1`, `_Blend: 0`,
   `_BaseColor {1,1,1,0.1}` + the URP transparent keywords. Correct look, but still trips the
   `min(r,g,b) < 0.92` trace rule — the trace would need an alpha-aware exemption, which is a
   second change and should be its own ticket.
2. **Tinted opaque (cheapest, trace-clean):** keep `_Surface: 0`, set `_BaseColor` to a pale blue-grey
   with at least one channel **below 0.92** (e.g. `{r: 0.62, g: 0.74, b: 0.85, a: 1}`). Renders as a
   plausible window/lens, and satisfies the existing oracle with no trace change.
   **Recommended** — one file, one ruling, no oracle edit.

Alternatively, if the `glass` submesh is a cosmetic detail the Cleric does not need, the cheapest fix
of all is to re-stage the Cleric with `materialSearch` pointed at `cleric_texture.mat` — but
`cleric_texture.mat` **does not exist** (section 1), so that path also requires authoring a material.

**Does this need an Addressables content rebuild + R2 push?**
- **Cleric: NO.** It is `Assets/Resources/NPCs/KayKit/Cleric.fbx` — Resources, in-APK (section 0a).
  A **new APK build** is required; `tools/r2-ship.ps1` is not. **Acceptance item 3 should be struck
  for the Cleric fix.**
- **Tower patch: UNDECIDED, and it forks on section 3a.** Nothing is wrong with either tower
  material on disk (section 2), so there is nothing to author yet. The two branches:
  - **If 3a is confirmed** (the co-located ArcaneSpire mesh is the white surface): the asset is
    `Assets/StructureContent/ArcaneSpire_1` + `ArcaneSpire_Albedo`, both registered in the
    **`Structure_Art`** group (`Structure_Art.asset:100-101` and `:196-197`) whose `Remote.LoadPath`
    is the R2 CDN (CLAUDE.md §16). Then **YES: `tools/r2-ship.ps1` with `R2_PARITY_OK` on a fresh
    log is required**, bundle names being content-hashed — *and* the likelier real fix is that the
    dresser should DISABLE the spire's own renderer when it reskins a `Watchtower_*`
    (`RaidBaseDresser.cs:1275-1292` currently cannot, since the renderer is on the host), which is a
    code change in the WO-1747 silo and a scene re-bake, not a material edit.
  - **If 3a is refuted** and the owner is some KayKit pack asset embedded in the scene: those live
    under `Assets/Models/KayKit/**`, which is **not** in `Structure_Art` — scene-embedded, so APK-only,
    **no R2 push**.
  Do not pre-emptively assert either branch; the `RaidBaseMatDiag` run decides it.

### 5. Suggested acceptance rewrite for the lead

1. `RAIDBASE_MATDIAG_OK` on a fresh `Builds/raidbase-matdiag.log`, naming the renderer + material
   behind the white patch in `RaidBase_IronBastion` — **before** any tower-side edit. Specifically:
   does `Watchtower_Archer_3` carry TWO live renderers (spire + KayKit shell, per §3a)?
2. `DependencyClosureTrace` reports zero `dep MISS … NO albedo` for `NPCs/KayKit/Cleric` on a fresh
   headless run. (Drop the "Watchtower_Archer address" clause — no such address exists.)
3. Owner device screenshot (or headless capture) showing a deployed Field Cleric with no white blob.
4. R2 push **only if** step 1 implicates a `Structure_Art` asset; otherwise a plain APK rebuild.
5. Lane flips the Status line and writes the `.RESULT.md`.

---

## Implementation 2026-09-15 (Cleric half)

Authored three files per the diagnosis section 4 / tinted-opaque option (recommendation):

1. `Assets/Resources/NPCs/KayKit/Materials/cleric_glass.mat` — new URP/Lit material, OPAQUE
   - `m_Name: cleric_glass`
   - `m_Shader: {guid: 933532a4fcc9baf4fa0491de14d08ed7}` (Universal Render Pipeline/Lit)
   - `_Surface: 0`, `_Blend: 0`, `m_CustomRenderQueue: 2000`, `RenderType: Opaque`
   - `_BaseColor: {r: 0.62, g: 0.74, b: 0.85, a: 1}`, `_Color: {r: 0.62, g: 0.74, b: 0.85, a: 1}`
   - `_BaseMap` and `_MainTex` left empty (`{fileID: 0}`)

2. `Assets/Resources/NPCs/KayKit/Materials/cleric_glass.mat.meta` — GUID `e06480bd2e284dec9404eef7c87c131c`

3. `Assets/Resources/NPCs/KayKit/Cleric.fbx.meta` — added `externalObjects` remap (line 6):
   ```yaml
   externalObjects:
   - first:
       type: UnityEngine:Material
       assembly: UnityEngine.CoreModule
       name: glass
     second: {fileID: 2100000, guid: e06480bd2e284dec9404eef7c87c131c, type: 2}
   ```

**Tower half remains unexecuted.** Run `RaidBaseMatDiag` first per diagnosis §3b.

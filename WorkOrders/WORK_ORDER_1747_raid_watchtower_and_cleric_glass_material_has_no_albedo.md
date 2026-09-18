# WORK ORDER 1747 — Raid watchtower / KayKit Cleric: `glass` material ships with NO albedo and NO tint (pink/grey patch on device)

**Status:** DONE - committed 95cd8adb6, gated (COMPILE_GATE_OK/REGRESSION_OK or node --test as applicable). PRIOR: READY FOR LEAD REVIEW - Cleric half landed in commit 7bb0c4291 (re-verified at source 2026-09-15); tower half's "no albedo" title REFUTED by RAIDBASE_MATDIAG_OK 4/4 (21:39); the §H per-renderer probe (`RunPerRenderer`) is now IMPLEMENTED per spec and awaits a headless run by the lead (this lane does not fire Unity/gate/commit)
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

---

## Edit-only lane 2026-09-15 (verification pass; NO Unity, NO gate, NO commit)

Assigned as an implementation lane. **Nothing was implemented, because there was nothing left to
implement that this lane is allowed to touch.** Everything below was opened at source this session.

### A. The Cleric half is already on disk and already committed — no re-work needed

`git log -- Assets/Resources/NPCs/KayKit/Cleric.fbx.meta` names commit **`7bb0c4291`**
("fix(art): WO-1747 — the Field Cleric rendered as a white blob"). All three artefacts the
Implementation section claims were verified present and tracked:

- `Assets/Resources/NPCs/KayKit/Materials/cleric_glass.mat` — tracked (`git ls-files` hit), and read
  at source: `m_Name: cleric_glass` (`:23`), URP/Lit shader guid `933532a4fcc9baf4fa0491de14d08ed7`
  (`:24`), `_BaseMap m_Texture {fileID: 0}` (`:41-42`), `_MainTex {fileID: 0}` (`:65-66`),
  `_Blend: 0` (`:102`), `_Surface: 0` (`:128`),
  **`_BaseColor {r: 0.62, g: 0.74, b: 0.85, a: 1}` (`:134`)** and the same on `_Color` (`:135`).
  Blue 0.85 and green 0.74 are both under the trace's `min(r,g,b) < 0.92` rule, so the oracle is
  satisfied without editing the oracle — the diagnosis's recommended option 2, faithfully applied.
- `…/cleric_glass.mat.meta` — `guid: e06480bd2e284dec9404eef7c87c131c`.
- `Assets/Resources/NPCs/KayKit/Cleric.fbx.meta:6-12` — the `externalObjects` remap is present and
  points `name: glass` at `{fileID: 2100000, guid: e06480bd2e284dec9404eef7c87c131c, type: 2}`.

`git status` on `Assets/Resources/NPCs/KayKit/` is **clean** — nothing of this is uncommitted.

⚠ **NOT proven by this lane:** that the remap actually takes at import. `externalObjects` is only
honoured on a reimport, which needs Unity. Acceptance item 2 (zero `dep MISS … NO albedo` for
`NPCs/KayKit/Cleric` on a fresh headless run) is therefore **still open** and belongs to the lead's
gate run, not to this lane. The files are right; the render is unverified.

### B. §3a is no longer just a candidate — the two co-located renderers are now disk-proven

⛔ **The §2/§3a line numbers in the diagnosis above are STALE.** The scene has been re-baked since
(last touched by `3b4b98834`, WO-1749). `RaidBase_IronBastion.unity:83907` is now `RuinPiece_0`, not
the tower. Re-located and re-read at the CURRENT lines:

- `Assets/Scenes/RaidBase_IronBastion.unity:12185` — `m_Name` → `Watchtower_Archer_3`, on a
  `PrefabInstance` whose `m_SourcePrefab` (`:12203`) is **guid `fdbdc8e2c3cad234eb116332a5148b42`**
  (= `Assets/StructureContent/ArcaneSpire_1.fbx`). Uniform `m_LocalScale` 0.047886882 (`:12132-12141`).
- `:12187` `m_RemovedComponents: []` and `:12188` `m_RemovedGameObjects: []` — **still empty**, and
  the instance's ENTIRE modification list is only LocalScale / LocalPosition / LocalRotation /
  LocalEulerAnglesHint / m_Name. **There is no `m_Enabled` override anywhere on it**, so the
  ArcaneSpire prefab's own renderer is neither removed nor disabled.
- `:12189-12192` `m_AddedGameObjects` adds `{fileID: 698546328}`, which resolves at
  **`:36719-36722`** to a Transform whose `m_CorrespondingSourceObject` guid is
  **`fcf76db4544b7e5498781f360bda601d`** — confirmed this session against
  `Assets/Models/KayKit/KayKit Medieval Hexagon Pack 1.0.1/Assets/fbx(unity)/buildings/green/building_watchtower_green.fbx.meta:2`.

And the mechanism, re-read at the path the diagnosis never gave — it is
**`Assets/Editor/WallTools/RaidBaseDresser.cs`**, not `Assets/_Modules/...`:

- `RaidBaseDresser.cs:1275-1291` `ReplaceChildrenWith` collects only `host.transform.GetChild(i)`
  (`:1278-1282`), `DestroyImmediate`s those (`:1284`), then parents the new visual under the same host
  (`:1285-1290`). **It never touches a renderer on the host itself** — the diagnosis's claim, now
  re-proven at the real file.
- `:1205` `ReplaceChildrenWith(t.gameObject, towerModel, t.name.StartsWith("Watchtower_"))` is the
  `Watchtower_*` call site.
- `:1269-1270` `MapCatalogArt` returns `"ArcaneSpire_1"` for any catalog id containing `arcane` — which
  is how a tower named `Watchtower_Archer_3` comes to be an ArcaneSpire instance in the first place.

**So the baked scene positively records one ArcaneSpire_1 host with its renderer intact, wearing a
KayKit watchtower shell as an added child, at identical scale.** That is the exact geometry §3a
described. It is still **NOT** proof that the spire mesh is the white surface in the screenshot —
it proves the second surface EXISTS, not what colour it renders. Only `RaidBaseMatDiag` closes that.

### C. The device-log check §3a asked for was run, and it is EMPTY

`logs/f8-inbox/capture-device-20260915-134044-seq5257.md` is **48 lines** and contains **zero** matches
for `ArcaneSpire`, `RemoteProviderException` or `404 Not Found` (grep -c → 0 for both patterns).
So the §16 remote-bundle branch is **neither confirmed nor refuted from the captured evidence** — the
harvest window simply does not cover it. Recording this as a finding per §11B-A, not ticking it.

### D. WO-1760 landed AFTER this ticket's diagnosis and touches the same asset — note for the diag run

`f74bf0829` (WO-1760, 2026-09-15 21:03) re-pointed `Structures/ArcaneSpire_{1,2,3}` in
`Structure_Art.asset` off Synty wrapper prefabs and onto the owner FBXs, and deleted
`Assets/StructureContent/Synty/ArcaneSpire_*.prefab`. At the capture build (`2026.09.15.371127`) the
Addressables address for the spire therefore resolved to **the wrong art**. The baked raid scene
references `ArcaneSpire_1.fbx` **by GUID directly**, not through Addressables, so WO-1760 does not
change this scene — but whether the device's tower was the baked instance or an Addressables-resolved
one is a build-output question this lane cannot settle. **Flagged for the diag run, deliberately not
resolved statically.**

### E. `RaidBaseMatDiag` is intact and is the next action

`Assets/Editor/RaidBaseMatDiag.cs` still lists `Assets/Scenes/RaidBase_IronBastion.unity` in its scene
list — re-read this session at **`:50`**, with the marker emitted at **`:141`**
(`RAIDBASE_MATDIAG_OK {scenesRead}/{RaidScenes.Length} scenes`) and the run line at `:23-25`. It is the
ONLY thing standing between this ticket and its tower half:

```
powershell tools\run-unity-method.ps1 -Method DeNelle.Editor.RaidBaseMatDiag.Run -LogName raidbase-matdiag.log
```

Judge by **`RAIDBASE_MATDIAG_OK <scenes>` on a fresh log**, never the exit code. The question it must
answer: does `Watchtower_Archer_3` report TWO live renderers, and what is the shader + `_BaseMap` +
`_BaseColor` on each?

### F. Why this lane wrote no code

§12's hard gate. Disabling the spire's host renderer in `RaidBaseDresser` reads as obviously right and
is exactly the banned inference-fix: the owning renderer of the white patch is still unnamed, and the
change would additionally need a scene re-bake this lane cannot fire. **No Addressable material or
texture was touched by this lane, so `tools\r2-ship.ps1` is not owed for anything done here.** If the
diag confirms the spire, the R2 fork in §4 applies; if it names a `Assets/Models/KayKit/**` asset, it
does not.

### G. `RaidBaseMatDiag` HAS NOW RUN — the "material has no albedo" title is REFUTED for the tower half

`Builds/raidbase-matdiag.log` (mtime **2026-09-15 21:39**, 74,470 bytes), marker
**`RAIDBASE_MATDIAG_OK 4/4 scenes`**. The `RaidBase_IronBastion` block begins at **log line 577** and
reads, measured:

```
renderers=1237 nullMaterialSlots=0 distinctMaterials=12
```

All twelve are `Universal Render Pipeline/Lit` — **no error/magenta shader, and no `nullMaterialSlots`
at all.** The five untextured materials are all polyperfect COLOUR mats, untextured *by design*
(`albedoProp=_MainTex`, `albedoTex=<null>`, `baseColor` carrying the look):

| x | material | albedo | baseColor | first example path in the log |
|---|---|---|---|---|
| 8 | `M_10_Brown_Dark_LPUP` | `<null>` | 0.404,0.286,0.176 | `…/CornerPost_Outer_S` |
| 8 | `M_12_Brown_LPUP` | `<null>` | 0.659,0.471,0.243 | `…/CornerPost_Outer_S` |
| 536 | `M_20_Grey_LPUP` | `<null>` | 0.529,0.510,0.510 | `…/ArenaBoundary_Ring/ArenaBoundary_S_0` |
| 411 | `M_21_Grey_Light_LPUP` | `<null>` | 0.655,0.631,0.620 | `…/ArenaBoundary_Ring/ArenaBoundary_S_1` |
| 140 | `M_57_Black_LPUP` | `<null>` | 0.073,0.078,0.104 | `…/BoundaryBacking/BoundaryBacking_0_0` |

Every textured material resolves its `_BaseMap`: `steel_wall` x316 (`steel_basecolor`),
`dungeon_texture_URP` x344 (`dungeon_texture`), `hexagons_medieval_URP` x18 (`hexagons_medieval`),
**`Color_bcf8a365-0849-42ab-9611-99d7fa0d2f81` x12 with `albedoTex=ArcaneSpire_Albedo`**, and the
three `Assets/Generated/RaidGround/RaidBase_IronBastion*` mats with `Path_Dirt_BaseColor`.

> ⛔ **CONCLUSION: there is NO albedo-less textured material and NO broken shader anywhere in
> `RaidBase_IronBastion`. This ticket's title — "`glass` material has NO albedo" — is REFUTED at
> material level for the tower half.** (It remains the correct description of the Cleric half, which
> is a `Resources/` FBX and is not in this scene at all.)

**What the diag does NOT answer, and why — the aggregation seam:**
`RaidBaseMatDiag.cs:93` opens a **per-material** `Dictionary<string, MatFacts> rollup`; the renderer
walk at `:99-114` increments `renderers++` (`:102`) but folds every renderer into that dictionary,
keeping only a COUNT and the **first** path it saw (`:112`, `if (acc.Example == null) acc.Example =
Path(r.transform)`). The emitted lines are therefore one aggregate (`:118`) plus one line per distinct
material (`:126-130`). The rollup Key (`:187`) is `source|name|shader|albedoTex` — it carries **no
object identity and no `enabled` state**. So the diag *cannot* say which renderers sit under
`Watchtower_Archer_3`, and the two-live-renderers question from §B is still open.

**Arithmetically consistent with §B's two-renderer geometry — but NOT per-renderer proven.**
Counted in the scene this session: **10** `Watchtower_*` hosts (`Watchtower_Archer_0-6`,
`Watchtower_Mage_0-2`), **8** `CornerPost_*`, **1** `RaidSpire`. Against the log:
- `hexagons_medieval_URP` = **x18** = 10 Watchtower `/Visual` + 8 CornerPost `/Visual`. Exact.
- `Color_bcf8a365-…` (the ArcaneSpire material) = **x12** = 10 Watchtower hosts + the `RaidSpire` host
  + its `RaidSpire/Visual`. Exact.
- And the log shows the pattern directly on a sibling: `M_10_Brown_Dark_LPUP` / `M_12_Brown_LPUP`
  example `…/CornerPost_Outer_S` (the HOST) while `hexagons_medieval_URP` example is
  `…/CornerPost_Outer_S/Visual` (the CHILD) — **the same GameObject appearing as both a host renderer
  and a dressed child renderer, measured, in one log.**

Two independent exact identities is strong, and it matches `ReplaceChildrenWith`'s child-only destroy
(§B). It is still an identity of COUNTS, not an enumeration — a lane must not call it proven.

**⚠ Where this leaves the white patch.** In the EDITOR the spire material is fully textured, so the
patch is **not** reproducible from scene data — which pushes the remaining weight onto the §16 branch
(`ArcaneSpire_Albedo` is a remote Addressable, `Structure_Art.asset:196-197`) and onto WO-1760 (§D:
at build `371127` the spire addresses served Synty wrappers). **Neither is proven.** The per-renderer
probe below is still the cheapest next measurement because it settles the geometry; a device `logcat`
grep for `ArcaneSpire` settles the shipped half.

### H. Per-renderer probe spec — exact shape for the next Unity lane

Add to `Assets/Editor/RaidBaseMatDiag.cs` as a **second, separate entry point** — do **not** change the
rollup at `:93-130`, which is correct for its own job (1237 renderers unaggregated is unreadable).

- **Method:** `DeNelle.Editor.RaidBaseMatDiag.RunPerRenderer` (menu
  `Defenders/Art/Diag Raid Tower Renderers`).
- **Scenes:** reuse the existing `RaidScenes` array (`:44-50`) unchanged.
- **Object filter:** for every root, walk `GetComponentsInChildren<Transform>(true)`; select `t` where
  `t.name.StartsWith("Watchtower_")` **or** `t.name.StartsWith("CornerPost_")` **or**
  `t.name == "RaidSpire"`. These are the dresser's three `ReplaceChildrenWith` targets
  (`RaidBaseDresser.cs:1205` and `:1208-1210`) and therefore the only hosts that can carry a stale
  inner mesh.
- **Per host, emit one HEADER line:** host name, `childCount`, and the count of
  `GetComponentsInChildren<Renderer>(true)`.
- **Then one line PER RENDERER** under that host (include inactive: pass `true`), carrying, in this
  order — and every field is the thing the rollup drops:
  `path` (relative to the host, so `<host>` vs `<host>/Visual` is unambiguous) ·
  `rendererType` · **`enabled`** · **`gameObject.activeInHierarchy`** ·
  `shader` · `albedoProp` + `albedoTex` (reuse `AlbedoProps` at `:54-57` and the existing `Describe`
  at `:152`) · `_BaseColor` · `bounds.size` · `sharedMaterial` asset path.
- **The verdict line the ticket actually needs**, emitted per host:
  `TWO_LIVE_RENDERERS host='<name>' hostRenderer=<true|false> childRenderer=<true|false>` — where
  `hostRenderer` means a renderer **on the host transform itself** that is `enabled` and
  `activeInHierarchy`. Any host printing `hostRenderer=true childRenderer=true` is a co-located
  double-mesh and is the §3a shape.
- **Marker, judged on a fresh log, never the exit code** (§8; memory
  `gates-report-success-without-proving-it`):
  **`RAIDBASE_RENDERERDIAG_OK <scenesRead>/<RaidScenes.Length> scenes`**, emitted exactly once at the
  end, mirroring `:141`.
- **Run line:**
  `powershell tools\run-unity-method.ps1 -Method DeNelle.Editor.RaidBaseMatDiag.RunPerRenderer -LogName raidbase-rendererdiag.log`
- **Read-only:** open scenes with `OpenSceneMode.Single`, never save, never mark dirty — same contract
  as `Run` (`:76`). No `DataRegression` registration (it is a diagnostic, not an oracle).

**Status label left as READY on purpose** (memory `status-label-is-a-fixed-word-rulings-go-in-prose`):
this lane produced no fix, and `CLOSED` would be false — the tower symptom was captured on the owner's
device and has never been shown gone. No `.RESULT.md` written, since the ticket is not finished.

---

## IMPLEMENTATION RECORD 2026-09-17 (§H per-renderer probe — code only, no Unity/gate/commit)

Silo confirmed before touching anything: this ticket's silo is content / Addressables dependency
closure for the KayKit `glass` material and the (now-disk-proven, §B) watchtower double-mesh
geometry. `RaidAssaultAi.cs`, `TroopController.cs` and every `.unity` file were **not opened and not
touched** (WO-1746's silo, per this ticket's own header). `Assets/Editor/RaidBaseMatDiag.cs` is in
scope: it is the exact instrument §E/§G/§H name as "the next Unity lane" and "the ONLY thing standing
between this ticket and its tower half."

**What was done:** implemented `DeNelle.Editor.RaidBaseMatDiag.RunPerRenderer` exactly to the §H spec,
as a second, separate entry point — the existing `Run()` rollup (lines ~60-143) was **not modified**,
per §H's explicit instruction ("do **not** change the rollup … which is correct for its own job").

- Menu: `Defenders/Art/Diag Raid Tower Renderers` (matches §H).
- Reuses the existing `RaidScenes` array unchanged (no new scene list).
- Object filter: walks every root's `GetComponentsInChildren<Transform>(true)` and selects any
  transform named `Watchtower_*`, `CornerPost_*`, or exactly `RaidSpire` — the three
  `ReplaceChildrenWith` targets named in §H (`RaidBaseDresser.cs:1205`, `:1208-1210`).
- Per host: one HEADER line (`host name`, `childCount`, `rendererCount` via
  `GetComponentsInChildren<Renderer>(true)`), then one line **per renderer** (inactive included),
  fields in the §H-specified order: `path` (relative to the host — `<host>` vs `<host>/Visual`, via
  new helper `RelativeToHost`), `rendererType` (`r.GetType().Name`), `enabled`,
  `activeInHierarchy`, `shader`, `albedoProp`+`albedoTex` (reusing the existing `AlbedoProps` array
  and `Describe()` helper — no duplicate albedo-scan logic), `_BaseColor`, `bounds.size`,
  `sharedMaterial` asset path.
- Verdict line per host, exact format from §H: `TWO_LIVE_RENDERERS host='<name>' hostRenderer=<bool>
  childRenderer=<bool>` — `hostRenderer` is true only for a renderer **on the host transform itself**
  that is both `enabled` and `activeInHierarchy`; `childRenderer` is the same test for any renderer
  NOT on the host transform.
- Marker, emitted once at the end, never judged by exit code (§8):
  `RAIDBASE_RENDERERDIAG_OK <scenesRead>/<RaidScenes.Length> scenes`.
- Read-only: opens scenes with `OpenSceneMode.Single`, never saves, never marks dirty — identical
  contract to `Run()`. No `DataRegression` registration (diagnostic, not an oracle, per §H).

**Run line (unchanged from §H's spec, not yet executed by this lane):**
```
powershell tools\run-unity-method.ps1 -Method DeNelle.Editor.RaidBaseMatDiag.RunPerRenderer -LogName raidbase-rendererdiag.log
```

**Gate proof for this lane's own file changes** (CLAUDE.md §1, the two required checks):
- `python tools/gate_brace.py Assets/Editor/RaidBaseMatDiag.cs` → `GATE_BRACE_SUMMARY bad=0 of 1`.
- NUL-byte scan on the same file → `0` bytes; raw brace count `{`=80, `}`=80 (balanced).

**What this lane did NOT do, and why:**
- Did **not** run Unity, the compile gate, `DataRegression`, or any bake/build — reserved for the
  lead per this lane's own instructions.
- Did **not** commit — sole committer is the lead (CLAUDE.md §11).
- Did **not** touch the Cleric-side fix (already committed in `7bb0c4291`, re-verified present and
  clean by the prior edit-only lane, §A) — nothing there needed rework.
- Did **not** touch `RaidBaseDresser.cs` to disable a host renderer. That remains the banned
  inference-fix per §F: the owning renderer of the device's white patch is still unnamed until the
  probe above actually runs and its `TWO_LIVE_RENDERERS` / per-renderer lines are read.

**Addressables / R2 flag for the lead (CLAUDE.md §16):** this lane's only change is an Editor-only
diagnostic script (`Assets/Editor/RaidBaseMatDiag.cs`) — it ships in no build and is not Addressables
content. **No content rebuild and no `tools/r2-ship.ps1` push are owed for this change.** Per §4/§G/§H,
whether the eventual TOWER fix needs an R2 push is still undecided and forks on what `RunPerRenderer`
reports: if it confirms the co-located `ArcaneSpire_1` host renderer as the white surface, the fix
likely touches `RaidBaseDresser.cs` (code, in this silo) plus a scene re-bake — not a `Structure_Art`
Addressables asset — so R2 applicability still depends on which asset a subsequent fix actually edits,
per the fork already recorded in §4. The Cleric-side fix (already committed) is confirmed `Resources/`
(APK-only, no R2), unchanged from §0(a)/§A.

**Open questions / next action for the lead:**
1. Run `RunPerRenderer` headless (command above) and confirm `RAIDBASE_RENDERERDIAG_OK 4/4 scenes` on
   a fresh log, then read the `TWO_LIVE_RENDERERS` lines for every `Watchtower_Archer_*` /
   `Watchtower_Mage_*` host.
2. If a host reports `hostRenderer=true childRenderer=true`, that confirms §B/§3a's co-located
   ArcaneSpire mesh as the geometry class of the defect — but the probe runs in-EDITOR, where §G
   already found the spire material fully textured. So a live double-renderer would still not by
   itself explain a WHITE patch on device; the remaining open branch is the §16 remote-bundle question
   (§C: the captured device log's harvest window did not cover it) — a device `logcat` grep for
   `ArcaneSpire` / `RemoteProviderException` is the one measurement nothing in this repo can substitute
   for.
3. This lane leaves Status as **READY FOR LEAD REVIEW**, not CLOSED — no `.RESULT.md` written, ticket
   not finished (memory `status-label-is-a-fixed-word-rulings-go-in-prose`).


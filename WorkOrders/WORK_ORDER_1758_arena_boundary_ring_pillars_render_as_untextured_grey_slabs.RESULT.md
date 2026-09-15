# WORK ORDER 1758 — RESULT

**Status:** IMPLEMENTED, NOT YET GATED
**Lane:** edit-only. No Unity, no gate, no build, no bake, no git, no `.unity` touched.
**Date:** 2026-09-15. Every number below was read at source THIS session (CLAUDE.md §11B).

---

## 1. Which of the three causes is real

### (a) LOCAL IMPORT STATE — **DISPROVEN**. The URP repair pass cannot reach this material and never could.

`PolyperfectUrpFix.Fix` has two gates, and `M_21_Grey_Light_LPUP` fails both:

- `Assets/Editor/PolyperfectUrpFix.cs:62-67` builds `builtIn` from the shader name and then
  `if (!builtIn) continue;`. The material on disk is **already URP**:
  `Assets/polyperfect/Low Poly Ultimate Pack/Materials/Colors/M_21_Grey_Light_LPUP.mat:11`
  carries `m_Shader: {guid: 933532a4fcc9baf4fa0491de14d08ed7}`, which resolves to
  `Library/PackageCache/com.unity.render-pipelines.universal@13e5115b98bf/Shaders/Lit.shader`.
  The pass **skips it entirely**.
- Even on the built-in branch, `:76` binds `_BaseMap` only `if (mainTex != null)`. This material's
  `_MainTex` (`.mat:52`) is `m_Texture: {fileID: 0}`, so the branch would bind nothing.

**Re-running `Defenders/Art/Fix Polyperfect URP Materials` is a no-op here.** Nothing self-heals.

Nor is the empty `_BaseMap` damage. The pack's `Materials/Colors/` folder holds **52** flat-colour
palette swatches (counted on disk); the *textured* family is `M_Atlas_LPUP` / `M_Atlas_Night_LPUP`
in the parent folder, which is what `PolyperfectTreePostprocessor` binds for the tree FBXs. A
`Colors/` swatch with no texture is the pack's **intended authoring**, and the project shares that
idiom — the committed `Assets/Dungeon/Materials/RoomWall_KayKit.mat` and `RoomAccent_KayKit.mat`
are URP/Lit with `_BaseMap m_Texture: {fileID: 0}` too.

### (c) THE RING BUILDER LEAVING SLOT 1 AT THE PREFAB DEFAULT — **TRUE AS A DESCRIPTION, NOT A BUG.**

`ArenaBoundaryRing.InstantiatePiece` (pre-edit `:514-538`) did not touch materials at all — it
instantiated the prefab, added a `CapsuleCollider` if none existed, and returned. The only material
write in the file was `TintFallback` (`:628-637`), which runs **only** on the primitive fallback.
So slot 1 came from the prefab, and the prefab authors it deliberately:

| prefab (`_M/Prefabs_M/Fantasy_M/`) | slot 0 | slot 1 | slot 2 |
|---|---|---|---|
| `Rubble_Stone.prefab:65` | M_20_Grey | — | — |
| `Dungeon_Pillar_Stone_Round.prefab:65-66` | **M_21_Grey_Light** | M_20_Grey | — |
| `Dungeon_Pillar_Stone_Square.prefab:65-66` | M_20_Grey | **M_21_Grey_Light** | — |
| `Dungeon_Wall_Stone.prefab:65-67` (the backing module) | M_57_Black | M_20_Grey | **M_21_Grey_Light** |

(guids resolved via `*.mat.meta`: `5fe0aebe…` = M_20_Grey, `31b29cea…` = M_21_Grey_Light,
`66c05709…` = M_57_Black.) There is no slot-index error: the light swatch is on a **different slot
index in each prefab**, which is itself proof that a slot-1 bug is not the story.

### (b) AUTHORING GAP — **PROVEN, and the file convicts itself.**

`ArenaBoundaryRing.cs` RockPaths header states the sweep criterion it used to pick the palette:
*"every prefab whose materials all sit in luminance 0.15-0.56"* (the `WHY THESE THREE` paragraph),
and then eight lines later admits **`M_21_Grey_Light_LPUP` 0.636**. 0.636 is outside the band the
sweep declared. Recomputed from the `.mat` this session, Rec.709 (the weighting that header quotes):

- M_20_Grey `_BaseColor (0.5294, 0.5098, 0.5098)` → **0.514** ✔ inside the band
- M_21_Grey_Light `_BaseColor (0.6549, 0.6314, 0.6196)` → **0.636** ✘ outside it

Both reproduce the header's own printed figures exactly, so the weighting is confirmed, not assumed.
And 0.636 sits **0.04 below the sky at 0.677** — the same 0.007-delta failure mode WO-1637 was
opened to kill. **WO-1637 fixed half the ring.** The defect is the TINT, not a missing texture.

### The census object IS this ring — arithmetic, not assertion

Device line: `bounds=2.6 x 7.0 x 2.6 m`. The header's measured `Dungeon_Pillar_Stone_Square` is
`0.80 x 0.80 XZ, 3.12 high` and the band fit applies **~2.26x**: 3.12 × 2.26 = **7.05 m** tall, and
the 0.80 square's diagonal 1.13 × 2.26 = **2.56 m** across under the ring's free yaw
(`localRotation = Euler(0, rng 0-360, 0)`). Position `x = -68.3` matches
`ArenaBoundaryHalfExtent` (`RaidBaseGenerator.cs:132`) plus jitter. Identified.

---

## 2. The fix — `Assets/Editor/ArenaBoundaryRing.cs` (the ONLY file changed)

A **light-swatch guard** at the one seam every placement path funnels through. Not a named-material
patch: it is expressed in the palette's own stated terms, so a future palette edit is policed by the
same rule.

| what | where (post-edit line numbers, `grep -n`'d after the last edit — not computed) |
|---|---|
| The RCA + why the fix cannot live in the material | header block from `:153` |
| `PaletteLuminanceCeiling = 0.56f` (the sweep's own ceiling, Rec.709) | `:220` |
| `StoneShadowTint = (0.44, 0.425, 0.415)`, Rec.709 **0.427** | `:234` |
| `StoneShadowName` / `_stoneShadow` / `_rebindCount` | `:236`, `:239`, `:245` |
| `BeginRebindRun()` at both placement entry points | `:297` (polar), `:370` (square) |
| `InstantiatePiece` gains optional `flowSys` + `rebindLightSwatches` | `:622`, guard call `:637` |
| `ReportRebindRun` one-line-per-bake summary (prints on ZERO too) | `:761`, called `:320` / `:474` |
| `RebindLightUntexturedSlots` | `:782` |
| `StoneShadow()` / `Rec709` | `:844` / `:880` |

**The rule:** a slot is rebound iff it has **no albedo at all** AND Rec.709 luminance **> 0.56**.

- The albedo test is **`DeNelle.Core.DependencyClosureTrace.GetAlbedo(m)`** — literally the function
  `RaidUntexturedCensus.ClassifySlot` calls at `:226`. No second predicate was written, so the fix
  and the instrument cannot drift apart (the drift `ShaderPredicateSingleAuthorityRegression` exists
  to stop). **The census predicate was NOT touched.**
- The guard is **stricter than the detector**: the census floor is `0.60` on NTSC weights
  (`RaidUntexturedCensus.cs:78`), the guard's ceiling is `0.56` on Rec.709. `0.427` clears both with
  room, so acceptance 2 holds without widening anything.
- `M_20_Grey` (0.514) and `M_57_Black` (0.081) are **untouched** — they are already inside the band.
- **Zero RNG draws added**, zero geometry changed, `RockPaths` unchanged. The line-for-line control
  at `RaidArenaShapeRegression.cs:842` was read: it reproduces the *geometry* only (bandHalf,
  ringLine, allowedFootprint, reach, jitter) and asserts nothing about materials. WO-1632's
  continuity pin and the seed-reproducibility guarantee are intact.
- `sharedMaterials` is read into a local, written, and **assigned back** — the getter returns a copy.
  `.materials` was deliberately not used: an editor bake saves the scene, so it would serialise ~500
  duplicate materials into the shipped raid.

### Does it survive a fresh clone? **Yes.**

- **No gitignored byte is edited.** The pack `.mat` files are read only through the renderer's
  reference; the assets are not mutated. `AssetDatabase.SaveAssets` is never called by this path.
- The durable artefact is **`ArenaBoundaryRing.cs`, which is committed**, and the bake re-creates the
  tone every run.
- **On a clone WITHOUT the pack**, `InstantiatePiece` takes its existing `LogWarning` + primitive
  branch and `TintFallback` gives `(0.45, 0.44, 0.42)` = Rec.709 **0.443**, already below the
  ceiling. Both clone states are correct.
- **Why not a committed `.mat`:** a hand-authored `.mat` needs a hand-authored `.meta` with a pinned
  GUID, and the baked scene would then hold a reference to it — a second piece of state to keep in
  sync, which is the duplicated-state trap this ticket is about. The scene-embedded shape is the
  precedent this same file already sets at `TintFallback` for exactly the reason `InstantiatePiece`'s
  header gives ("an editor bake SAVES the scene"). **Flagging for the lead to overrule if wanted.**

### Say it out loud: the SIEGE venue changes too

`PlacePolarRing` funnels through `InstantiatePiece`, so `ProceduralSiegeArenaBuilder`'s
`OuterBoundary_Ring` (`ProceduralSiegeArenaBuilder.cs:216`) gets the same guard. That is consistent
with the ruling recorded in RockPaths' header (owner ticked **"both venues"**, 2026-09-10 12:07) —
but the lead should know it is in the blast radius.

### Acceptance-2 evidence on the next bake (two lines, one grep)

1. `[RaidBaseGenerator] LIGHT-SWATCH GUARD ... rebound N slot(s) across 1 distinct swatch(es) to
   'ArenaBoundary_Stone_Shadow' (Rec.709 0.427)` — prints even at N=0, so the guard cannot go
   vacuously green.
2. `[Flow:RaidBase] REBIND boundary swatch 'M_21_Grey_Light_LPUP' slot=… had NO albedo at Rec.709
   luminance 0.636, above the palette ceiling 0.56 -> 'ArenaBoundary_Stone_Shadow' at 0.427.`

Then a device/headless run should show **zero** `Flow:RaidArt` offenders under `ArenaBoundary_Ring`.

⚠ **Do NOT grep the existing `MAT …` line as the oracle.** `TraceMaterials` reads
`rends[i].sharedMaterial` — **slot 0 only** — so it never showed `M_21_Grey_Light_LPUP` for
`Dungeon_Pillar_Stone_Square` (where the light swatch is slot 1) and it will not show the swap
either. The two markers above are the oracle; the MAT line is unchanged by design and was left
alone.

**Siege venue traces, it does not merely count:** `ProceduralSiegeArenaBuilder.cs:216-218` passes
`FlowSys`, so the polar ring emits the `REBIND` line too. The distinct-swatch counter is
incremented **unconditionally** and only the trace is gated, so the summary line can never read
"rebound N slot(s) across 0 distinct swatch(es)" for an untraced caller.

---

## 3. Residue — what the other offenders are

**⚠ THE RESIDUE CANNOT BE ENUMERATED FROM THE CAPTURED EVIDENCE, and saying otherwise would be a
guess.** `RaidUntexturedCensus.MaxReportedPerPass = 12` (`:73`) — the log printed 3 of 418. What
follows is the ring's share from bake-log counts, and then an honest bound on the rest.

**The ring's share:**

- Ring pieces: `ArenaBoundaryMaxPerSide = 100` (`RaidBaseGenerator.cs:202`) and the loop places
  `perSide - 1` per side → **396 pieces**. The prefab is a uniform RNG draw over 3, and **2 of the 3
  carry exactly one flagged slot** (table above) → **≈264 flagged slots**, RNG-dependent.
- Backing panels: **MEASURED, not derived.** `Builds/raid-bake-diag-1.log` (2026-09-14) carries
  `BOUNDARY BACKING module=Fantasy_M/Dungeon_Wall_Stone.prefab panels=140 height=6.692m
  nativeHeight=3.003m originalSkylineTop=7.044m depth=0.327m step=3.920m lap=0.020m` — so the
  backing **IS** placed (`:400`'s "cannot fit" refusal did not fire) and it is **140 panels**, each
  with one flagged slot (M_21 on slot 2). The same log's ring line reads
  `396 boundary pieces` at `piece(s)/side @ 1.39m (piece 1.73m`, confirming the 396 above.
  `originalSkylineTop=7.044m` is the device capture's **7.0 m** slab height, independently.

**So the ring's share is ≈264 + 140 = ≈404 of 418, and the residue is ≈14** (the ±RNG on the 2-of-3
prefab draw is the only slack). The guard covers both halves, because the backing funnels through
the same `InstantiatePiece`.

**Classification of what is left:**

- **A predicate-artefact CLASS exists and is real: the pack's `Colors/` palette family.** Of the 52
  swatches, **19 have Rec.709/NTSC luminance ≥ 0.60 with no albedo** — `M_58_White_LPUP` (1.00),
  `M_47_Blue_White_LPUP` (0.966), `M_23_Grey_White_LPUP` (0.858) and 16 others. Any *deliberately*
  light polyperfect prop in a raid scene is flagged while being correct art. **Named example of the
  class: `M_58_White_LPUP`.** ⚠ I am naming the CLASS, not asserting a confirmed member of the 418 —
  I have not seen a device line for one, and inventing one would be the guess §11B forbids.
- **The raid ground is NOT in the residue** — checked and cleared:
  `Assets/Generated/RaidGround/OwnedTown_IronBastion.mat`, `…_KeepPlatform.mat` and `…_KeepRamp.mat`
  all carry `_BaseMap m_Texture: {guid: 97dcba0a0960fa34a99fe7b62cf2178d}`, so `GetAlbedo` returns
  non-null and `ClassifySlot` returns `null` at `:226`. A textured ground was the other obvious
  "giant grey" candidate and it is ruled out.
- **A scan I ran and am DISCARDING rather than reporting:** a sweep of the 259 committed `.mat` files
  returned 26 "predicate-positive", but it tested only `_BaseMap`, while `GetAlbedo` reads every
  albedo-token slot (`DependencyClosureTrace.cs:204-209`: `albedo/basemap/basecolor/maintex/diffuse/…`).
  The 0.906 Tripo `_basecolor` materials in that list almost certainly carry their texture on another
  slot. Reporting them as real offenders would have been a guess. **The next pass should re-run that
  sweep against `AlbedoTokens`, not against `_BaseMap`.**

---

## 4. Lane hygiene

- `python tools/gate_brace.py Assets/Editor/ArenaBoundaryRing.cs` → `GATE_BRACE_SUMMARY bad=0 of 1`, **exit 0**.
- NUL scan: **0** embedded `\x00` bytes. Raw braces **48 / 48** balanced. 896 lines.
- Files changed: `Assets/Editor/ArenaBoundaryRing.cs` **only**, plus this `.RESULT.md` and the WO's
  `**Status:**` line. Nothing in the off-limits list was opened for writing; `RaidUntexturedCensus.cs`,
  `RaidBaseGenerator.cs` and `RaidArenaShapeRegression.cs` were **read only**.
- **NOT DONE (out of lane, by instruction):** no Unity, no compile gate, no regression, no bake, no
  build, no commit. **This is a CLAIM, not a verified fact** — it is unproven until the lead gates it
  and a fresh bake log carries the two marker lines in §2.

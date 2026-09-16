# WORK ORDER 1760 — The Arcane Spire ships its Synty stand-ins; the middle tier stands upside down

**Status:** FIXED - gated REGRESSION_OK 539/539 2026-09-15 21:01; shipped in AAB 2026.09.16.371610 + APK 371627; awaiting the owner felt test
**Minted:** 2026-09-15 (CLI main line; banner bumped 1760 -> 1761 in the same edit)
**Silo:** Art / Addressables. File-disjoint from gameplay, HUD and save.
**Branch:** dev

---

## The owner's report (2026-09-15 APK felt-test), verbatim

> "The arcane spire is using the backups, not the actual correct ones. The middle tier was
> upside down; top and bottom tiers were fine."

---

## RCA — proven at source this session, every claim with its line

### 1. What the catalog asks for
`Assets/Resources/Data/Canonical/structures-catalog.json`, row `tower_arcane_spire` (~:1079-1123):
- `visualPrefabPath` = `Structures/ArcaneSpire_1`
- `repo.upgradeVisualPath` = `Structures/ArcaneSpire_2`, `Structures/ArcaneSpire_3`
- `visualTexturePath` = `Structures/ArcaneSpire_Albedo`;
  `repo.upgradeTexturePath` = `Structures/ArcaneSpire_2_Albedo`, `Structures/ArcaneSpire_3_Albedo`

So the game asks for six ADDRESSES. It never names an asset. Everything below is about which
asset was behind those addresses.

### 2. What the addresses actually resolved to
`Assets/AddressableAssetsData/AssetGroups/Structure_Art.asset` in the WORKING TREE (pre-fix)
mapped the three MODEL addresses to the generated Synty wrappers:

| Address | GUID (pre-fix) | Asset |
|---|---|---|
| `Structures/ArcaneSpire_1` | `546d6b75804e15f4da8d58503d4c461f` | `Assets/StructureContent/Synty/ArcaneSpire_1.prefab` |
| `Structures/ArcaneSpire_2` | `e7a9f1020edfad347be71f29521bc5ed` | `Assets/StructureContent/Synty/ArcaneSpire_2.prefab` |
| `Structures/ArcaneSpire_3` | `52dc3423d7b4ba14a9575c9da821afff` | `Assets/StructureContent/Synty/ArcaneSpire_3.prefab` |

GUIDs read from each `.prefab.meta` this session. The three ALBEDO addresses were already
correct — they pointed at the owner's PNGs (`c83598997a5b4f1797e66e82b1a8fb7a`,
`6138eb473a78499582083fc03a4f3d4a`, `b8752fc910f647b9b5377babbdf8922d`, matching
`ArcaneSpire_Albedo.png` / `_2_Albedo.png` / `_3_Albedo.png`), which is why only the MESH was
wrong. Her art is `Assets/StructureContent/ArcaneSpire_{1,2,3}.fbx`, GUIDs
`fdbdc8e2c3cad234eb116332a5148b42`, `d8478f8e20a9a3c44a3a22e5405764d0`,
`0d652e1697cad5143b40baa0d598bf49` (each carries a sibling `.fbx.tripo-extracted` marker).

### 3. Where the wrappers came from
`Assets/Editor/SyntyStructureRetheme.cs` `Map` (pre-fix ~:84-91):
```
{ "ArcaneSpire_1", "Buildings/Presets/SM_Bld_Preset_Tower_01_Optimized.prefab" }
{ "ArcaneSpire_2", "Castle/SM_Bld_Castle_Wall_Tower_L_01.prefab" }
{ "ArcaneSpire_3", "Buildings/Presets/SM_Bld_Preset_Church_01_B_Optimized.prefab" }
```

### 4. WHY THE MIDDLE TIER SPECIFICALLY STOOD UPSIDE DOWN
`ArcaneSpire_2` alone was mapped onto a castle **WALL** tower — a wall-segment piece whose
authored pose is not a free-standing building's. Tiers 1 and 3 were mapped onto a tower preset
and a church, which read as the wrong BUILDING but stood the right way up. That is exactly the
owner's report, tier by tier, and it is the tell that identifies the cause.

The pose is not overridden anywhere: `StructureFactory.OptsForUpgradeLevel`
(`Assets/_Modules/Village/Catalog/StructureFactory.cs:636-656`) calls
`OptsFor(entry, applyManualEuler: false)` — the base row's manual euler `[0,0,0]`
(catalog `orientation`, ~:1157) is deliberately NOT applied to tiers — and then applies
`repo.upgradeOrientationEuler[level-2]` only if that array exists. The `tower_arcane_spire` row
authors **no** `upgradeOrientationEuler`, so tiers 2 and 3 render in the asset's NATIVE pose.
Swap the asset and you swap the pose; that is the whole mechanism.

### 5. How a duplicate became a silent substitution
At HEAD (`git show HEAD:Assets/.../Structure_Art.asset`, arcane rows at `:33`, `:133`, `:143`,
`:293`, `:328`, `:373`) the group held **both** claimants for each of the three addresses — her
FBX and the wrapper. Addressables then resolved to whichever the BUILT catalog listed first, so
the repo did not decide the art. An UNCOMMITTED group edit (file mtime 2026-09-13 21:33) removed
24 duplicate addresses and kept the **wrapper** half of each spire pair, leaving nothing to fall
back to. The 2026-09-15 APK therefore shipped stand-ins.

This is the SAME mechanism, one structure over, as the Tripo watchtower masquerade fixed by
commit `1fec556d3` ("stop the retheme re-skinning the owner's Tripo watchtowers", 2026-09-02) —
whose own commit body names the double-claim as the cause. The generated wrappers reuse the
owner's filenames verbatim, which is what makes the swap invisible.

### 6. Why nothing caught it
CLAUDE.md §16: structure art is served from R2 with **no local fallback**. A wrong-but-present
bundle installs, launches and plays with no error on screen. The only detector was the owner's
eyes — the thing §14 exists to never rely on.

---

## What was changed (edit-only lane; NOT gated, NOT committed by this lane)

1. **`Assets/AddressableAssetsData/AssetGroups/Structure_Art.asset`** — the three model
   addresses re-pointed from the Synty wrapper GUIDs to the owner's FBX GUIDs, entries kept in
   the file's GUID-ascending order and the working tree's `m_SerializedLabels: []` shape.
   Diffed against a pre-edit snapshot: exactly **15 lines out, 15 lines in**, nothing else moved.
   The other seats' 24-address dedup is untouched, per the 2026-09-02 ruling that the rest of
   the Synty re-wraps are deliberate. The three albedo addresses were already correct and were
   not touched.
2. **`Assets/Editor/SyntyStructureRetheme.cs`** —
   - the three `Map` rows DELETED, with the deleted rows recorded in-comment plus the full RCA,
     in the style of the watchtower precedent at `:93+`;
   - the three leaves PINNED in `OriginalStorefrontGuids` with the FBX GUIDs. That table is
     already load-bearing: `AssertOriginalStorefrontExclusions` (`:260-266`) **throws** if a
     pinned leaf reappears in `Map` / `Composed` / `CompletionLeaves`; `Run` skips pinned leaves
     outright (`:591`); and `RestoreOriginalStorefronts` can now repair `Structure_Art` if an
     address is ever re-hijacked. A re-run can no longer re-create the wrappers.
   - two stale copies of the word "nine" (the file header and
     `RestoreOriginalStorefronts`' summary) DELETED rather than updated — they described the size
     of that table and went stale the moment three leaves were added.
3. **Deleted** (tracked; they show as ` D` in `git status`, index untouched for the lead):
   `Assets/StructureContent/Synty/ArcaneSpire_{1,2,3}.prefab` and their `.meta`.
   `git grep` proves the three wrapper GUIDs were referenced ONLY by `Structure_Art.asset` and
   by their own `.meta` files — no scene, no settings asset, no oracle expectation list.
4. **New regression** `Assets/Editor/Regression/ArcaneSpireArtAuthorityRegression.cs`
   (`[arcane-spire-art]`, `ARCANE_SPIRE_ART_OK` / `ARCANE_SPIRE_ART_FAIL`), registered with ONE
   line in `Assets/Editor/Regression/DataRegression.cs` beside `[structure-null-slot]`.
   Five rules: one claimant per `Structures/ArcaneSpire*` address; each resolves to the owner's
   `Assets/StructureContent/<leaf>.fbx|.png` and nothing else; the catalog's own three addresses
   are in the checked set; `SyntyStructureRetheme`'s `Map`/`Composed`/`CompletionLeaves` name no
   `ArcaneSpire` while `OriginalStorefrontGuids` still pins all three with the right GUIDs; and
   the three wrapper prefabs are gone from disk. Stands down via `RegressionOutcome.Skip` rather
   than passing on an empty set — and RULE 3 deliberately runs BEFORE that stand-down, so a
   future dedup that removes the spire addresses ENTIRELY reports as a FAIL (the catalog still
   asks for them) instead of hiding in the Skip bucket. The catalog is read through
   `CanonicalJson.Read("Data/Canonical/structures-catalog.json")`, not a re-typed `Assets/...`
   literal, so `AssetRootsRegression` RULE 1 stays green; the two markers are new strings
   (`git grep -c ARCANE_SPIRE_ART` returns nothing pre-existing) so `RegressionMarkerRegression`
   RULE 1 has no collision, and both guarded `return true` paths carry the
   `RegressionOutcome.Skip` token that RULE 4's hollow-pass ratchet requires.
   Scope is an Ordinal `StartsWith("ArcaneSpire")` on the leaf, so
   `Structures/arcane tower` (Cathedral of Learning) and `Structures/ArcaneTower_Albedo` — a
   different structure with its own ruling — are deliberately out of scope.

---

## Acceptance criteria

- [ ] `COMPILE_GATE_OK` on a fresh log.
- [ ] `REGRESSION_OK <n>/<n> suites` on a fresh log, with `[arcane-spire-art]` green in the body.
      (`suitesTotal` is DERIVED — `DataRegression.cs:2106` — and `expectedSuites` is parsed out of
      `RunAll`'s body by `RegressionMarkerRegression.TryGetExpectedSuiteCount:1065`, so the one
      registration line is the whole registration; no count needs bumping.)
- [ ] `[structure-orientation]` green — see the open risk below.
- [ ] **A content build, then `tools\r2-ship.ps1`.** REQUIRED, not optional: this re-points
      Addressable content, bundle names are CONTENT-HASHED, and a previous push can never cover
      this build (CLAUDE.md §16). Judge by `R2_PUSH_OK` + `R2_PARITY_OK` on a FRESH log, never an
      exit code. Without it the device keeps serving the old bundles and the fix is invisible.
- [ ] Owner felt-test: all three Arcane Spire tiers are her Tripo art and all three stand upright.
      Only she closes this (CLAUDE.md §13).

## What NOT to touch

- The rest of the uncommitted `Structure_Art.asset` dedup — other seats' work, and the owner's
  2026-09-02 ruling says the other Synty re-wraps are deliberate.
- `structures-catalog.json` — the `id` strings are live save keys; the addresses are unchanged
  by design, which is the whole reason this fix is asset-side.
- `Structures/arcane tower` / `ArcaneTower_Albedo` — a different structure.
- `StructureOrientationOracle`'s `UprightAspectMin` — the oracle itself says widening that floor
  is an OWNER RULING, not a fix.

---

## OPEN RISK — unproven from an edit-only lane, do not fix blind

**`[structure-orientation]` may go red on `Structures/ArcaneSpire_2` and that would be NEW
information, not a regression this lane caused.**

`Assets/Editor/Regression/StructureOrientationOracle.cs:193` sets `UprightAspectMin = 1.2f` and
`:407` applies it to every row with `type == CatalogType.Tower` and `heightMul >= 1.2`.
`tower_arcane_spire` is `"type": "Tower"` with `"heightMul": 1.2`, so it is IN that band. The
oracle resolves an address through `BuildAddressMap` (`:637-650`), which does
`map[entry.address] = ...` — **last writer wins**. While two GUIDs claimed each spire address,
which asset the oracle measured was not determined by the repo, so **no previous green proves
anything about the owner's FBX**. The retheme's own comment (now deleted with the rows) records
that this oracle measured `Church_01_A` at aspect 1.08 and rejected it — i.e. it does bite here.

What is proven: the three FBX import IDENTICALLY — `bakeAxisConversion: 0`, `globalScale: 1`,
`useFileScale: 1` in all of `ArcaneSpire_1.fbx.meta:41/58/76`, `ArcaneSpire_2.fbx.meta:36/53/71`,
`ArcaneSpire_3.fbx.meta:36/53/71`. **So the importer is not what would tip tier 2 over**; if her
`ArcaneSpire_2.fbx` measures badly it is the mesh's own authored pose, not a settings mismatch.
That cannot be measured without Unity. If the oracle goes red, capture the measured bounds and
aspect it prints and take them to the owner — do not add an exemption and do not move the floor.

## Board-label conflict, surfaced not resolved

The `**Status:**` line above is the label this WO was briefed to carry. Note that
`tools/board_build.py:187` reads the LEADING word, so `IMPLEMENTED - AWAITING GATE` lands this
row in **Done** — "the one bucket nobody re-opens" (`board_build.py:166`) — before the gate has
run and before the owner has seen it. If the lead wants the row to sit in the owner's queue
instead, the label that does that is a leading `FIXED` (`:203`, "built, gated and on disk, but
NOT closed: it is waiting on the owner's felt test"). Lead's call.

# BATCH_STATE — handover to Codex, 2026-08-26

**Lead:** Claude Code (CLI seat, sole committer). **Courier:** the owner. Append-only by section.

---

## PART 1 — WHERE THE PROJECT ACTUALLY IS

**The game is LIVE on the Solana dApp Store.** This is not pre-release work. A regression that
reaches the store reaches real players.

**Today the owner ran a long felt-test on a Seeker device** (build `2026.08.26.342290`). It produced
~25 new tickets and 11 approved UI specs. The board reads **58 READY** — that number is misleading,
see PART 2.

### Eight finished lanes are in the tree, gated or not — check `git status` first

If the lead gated before handing over, these are committed and the tree is clean. If not, they are
uncommitted and **you must not touch any file they contain** (PART 3.4 lists them).

| Lane | What was actually wrong |
|---|---|
| Staff pose (WO-1226) | The drawn staff lay across the body. Six prior fixes failed because **a regression PINNED the wrong value**. Owner ruled it stands vertical; the pin moved WITH the ruling. |
| Battle-lock (WO-1233) | **P0.** Winning an arena left the town unresponsive 8 times in 9. Root: `PursuitBattleProbe` held the lock; `PostureSignals.ClearPursuits` had ONE caller, on scene load — and the arena stages in-place with no scene load. |
| Raid payout | Troops killing defenders banked per-kill materials mid-raid. Owner ruled raids pay ONCE at the end. Gold and XP still pay per kill. |
| Fail-closed gating (WO-1223) | **FOUR OF FIVE** failure modes resolved to OPEN. `ParseState` literally ended `default: return Open`. The gate was no gate in every degraded condition. |
| Enemy level (WO-1232) | Two call sites still ran a retired `maxHp/25` heuristic; one drove the danger skull, so every enemy read LETHAL. |
| VFX pool (WO-1229) | **Not a leak.** 44 candle anchors against a global 24-slot pool with no bound. Also found: the dungeon 48-tier had **never engaged in a shipped build**. |
| Hollow passes | Four regression guards returned without asserting and landed in the GREEN column. |
| Hero select (WO-1083/1234) | The portrait resource path was written out in **eleven literals across seven files**. Now 2, both inside one constant. |

**Also live as of today: the F8 DEVICE BRIDGE (WO-1227).** Until this morning device captures never
reached any seat — 736 entries had accumulated unread since 2026-07-20. The chain is whole and
delivered its first two captures within the hour.

---

## PART 2 — THE GOAL

**The goal is a PROD BUILD, and it does NOT require closing 58 tickets.**

Ship gates are FOUR MARKERS (CLAUDE.md §8 + §16), not an empty board:

```
COMPILE_GATE_OK  +  REGRESSION_OK <n>/<n>  +  UI_CAPTURE_OK  +  R2_PARITY_OK
```

**What actually blocks a prod push:**

1. **WO-1233** battle-lock softlock — FIXED, awaiting gate
2. **WO-1223** fail-closed gating — FIXED, awaiting gate
3. **The R2 content push** — `tools\r2-ship.ps1`. Bundle names are **content-hashed**, so every
   content build needs **its own** push. A previous push can never cover this one. This has already
   burned the project three times.

Everything else on the board is polish, presentation, or new feature. Shipping with an untidy
Treasure panel is legal. Shipping with a town the player cannot interact with is not.

**Your job in this window is NOT to burn down the board.** It is to advance work genuinely disjoint
from the gate, so that when the lead returns the gate runs once and cleanly.

---

## PART 3 — THE PROCESS

### 3.1 You are in a linked worktree
Your `git diff` will report the lead's committed work as if it were uncommitted or duplicated.
**It is not.** Hash before believing duplication. Never merge your worktree as a branch.

### 3.2 NO git commit, add, or push. Ever.
There is exactly ONE committer and it is not you. Two committers duel on `.git/index.lock` and
produce stale locks plus false "pushed" reports. Leave work in the tree; describe it in the handback.

### 3.3 NO Unity. You cannot gate.
Unity is single-instance and the lead owns the gate. Do **not** run `run-unity-method.ps1`,
`CompileGate`, or `DataRegression`. A collision can corrupt a gate log the lead depends on.

**Therefore prefer work verifiable WITHOUT Unity.** `api/` has node tests. PowerShell has
`[System.Management.Automation.PSParser]::Tokenize` for parse checking. Use them and **paste the
output** in your handback.

For any C# you write: brace-balance check (`{` vs `}`) plus a NUL scan on every file, counts
reported. That is this repo's minimum.

### 3.4 Do not touch files the lead has uncommitted
**Run `git status --short -- Assets/` FIRST.** Every file it lists is locked. If the tree is clean,
this section is moot and you have more room.

If an assignment appears to require a locked file: **STOP and say so in the handback.** Do not work
around it, do not copy the file, do not "just add one small thing".

### 3.5 Where you can safely work
- **`api/`** — the Vercel serverless backend. It is **in this repo**, not a separate project. Fully
  disjoint from the gameplay lanes, and node-testable.
- `.claude/skills/run-defenders/*.ps1` — committed as of `89977006a`.
- `Assets/_Modules/Core/Diagnostics/BreakCaptureHarness.cs`.
- `docs/`, `WorkOrders/`.
- Canonical JSON **except `waves.json`** (mid-change).

### 3.6 Engineering rules that are not negotiable
- **Instrument before you fix** (CLAUDE.md §12). Static reading LOCATES a candidate; it never
  CONCLUDES a cause. If you cannot cite captured data proving the cause, you have not earned the
  edit. Every wrong theory this project shipped came from skipping this.
- **No hollow passes.** A guard that returns without asserting lands in the GREEN column and is a P1
  defect here. Missing dependency → FAIL naming it, or return an explicit skip token. Never a silent
  bare return.
- **Prove RED first.** A regression that has never failed proves nothing.
- **Never raise a cap or threshold to make a symptom go away.** The VFX pool went 20 → 40 → 24 that
  way and the real cause sat unfound for months.
- **A failure-only oracle is not acceptance.** Assert the good path too — this repo shipped a guard
  that aborted every good run while exiting 0.
- The owner is **red/green colourblind**. Nothing may carry meaning by hue alone.
- **ASCII-only** TMP strings and PowerShell.

---

## PART 4 — ASSIGNMENTS, in priority order

### 1. WO-1237 — the softlock detector fires on AFK  *(safe, self-contained)*
`WorkOrders/WORK_ORDER_1237_softlock_detector_fires_on_afk.md` — read it in full.

The detector labels 180s of no movement as `possible_softlock`. Capture seq 3609 proves a false
positive: the screenshot shows full HP, a five-face bar, and a wave clock counting down normally. The
owner was idle, not stuck.

**Why it matters:** `possible_softlock` is one of four kinds that PAGE a seat, and the device backfill
holds 8 of them. Noise trains the seat to discount the kind — and the one real softlock then arrives
already discredited.

Build an IDLE-vs-STUCK discriminator. Candidates (instrument, do not assume): input presence,
`Application.isFocused`, world liveness (the wave clock was ticking).

- **Do NOT just raise the 180s threshold** — that trades a false positive for a slower true positive
  and leaves the classifier equally blind.
- **Do NOT silence the kind.** An idle capture is still RECORDED, just not paged.
- Re-run your classifier over `logs/f8-inbox/DEVICE_BACKFILL_2026-08-26.md` (736 entries) and
  **report how many of the 8 reclassify**. That number is the ticket's value.
- Any `.ps1` must be **pure ASCII**. A BOM-less UTF-8 `.ps1` is read as ANSI by PS 5.1, and CP1252
  turns smart-quote bytes into string delimiters — silently mis-parsing while every gate stays green.
  A regression FAILS on this and it caught the lead's own hook today.

### 2. `api/` hardening  *(safe, node-testable)*
- `api/schema.sql` uses `ON CONFLICT DO NOTHING` on its seeds. **That is exactly why two
  `dungeon_status` rows never reached production and the owner's dungeons were shut all day.** Audit
  every seed in that file for the same trap and report which others would silently fail to back-fill
  an already-provisioned database.
  **Do NOT change production data** — the lead already wrote the two rows and verified them by shape
  query (`DUNGEON_ROWS_OK 6/6 covered`).
- `test/dungeon-status.manifest.test.js` — confirm it still REDS when a portal-gated id has no seed
  row. Run it; paste the output.

### 3. WO-1121 — live money rails and buy gate  *(oldest READY — TRIAGE, do not build)*
Read it and **report whether it is actually actionable** before writing a line. It may be owner-gated
or need rulings. A truthful *"this is blocked on X, here is the evidence"* is a valid and valuable
handback — more valuable than code built on a wrong assumption.

---

## PART 5 — HOW TO HAND BACK

Write `batch_results_state.md`, append-only by section. For each assignment:

- files changed, with line numbers
- the **command output** proving your claim — not a description of it
- brace counts + NUL scan for any C#
- anything you could not do, and why

**Your handback is a CLAIM until the lead proves it.** That is not distrust; it is the protocol this
repo runs on. Every assertion will be re-verified against the tree.

---

## PART 6 — CONTEXT YOU WILL OTHERWISE GET WRONG

- **`Enemy.Level` IS the `maxHp/25` heuristic** (`Enemy.cs:623`). There is **no authored level field**
  on `EnemyDef`. Its own doc comment claims otherwise and is WRONG — it misled a ticket today.
  Comments lie; read the code.
- **Five action-bar faces in open town is CORRECT.** Talk is proximity-gated on
  `TalkPromptRegistry.Count > 0`. Do not "fix" it — doing so cost an RCA this morning.
- **Offline first-run = every dungeon SEALED.** Owner ruling, not a bug.
- **Passive Echo repair SPENDS wood and iron.** The owner ruled the spend stays.
- **The repo root is machine-dependent** (`C:\eoa` on one machine, `D:\eoa` here). Never hardcode it.
- **58 READY is not 58 ship blockers.** See PART 2.

---

# APPENDED 2026-09-01 — PART 7: THE SYNTY ART RE-THEME LANE

**Lead:** Claude Code (CLI seat, sole committer). **Branch: `feat/synty-art-retheme`, 7 commits,
NOTHING PUSHED.** Base = `wip/village2-and-f8-tickets`.

## 7.1 — WHAT THIS LANE IS

Owner bought Synty **POLYGON Fantasy Kingdom + POLYGON Generic** ($350, 2026-09-01) and ruled:
**FULL re-theme, everything Synty**, and **walls at NATIVE height, zero scaling**. Four lanes were
minted: WO-1289 ground, WO-1290 walls, WO-1291 buildings, WO-1292 environment dressing.
Banner bumped 1289 -> 1293 (and PROD 021 -> 022) in the same edits.

| commit | lane |
|---|---|
| `0fe54026b` | lane opened, WOs minted, `Assets/Synty/` gitignored |
| `f434a5e0f` `57e2f7355` | WO-1289 ground regrade + chroma oracle — **DONE, gated** |
| `26ef6af55` `9687ecb22` | WO-1290 castle perimeter — **IN PROGRESS, gated** |
| `707f55ba0` | WO-1291 structure re-theme — **IN PROGRESS, gated** |
| `0b776276f` | parks PROD-021 + WO-1293 tickets (unrelated to this lane) |

## 7.2 — THE PACK: FACTS YOU WILL OTHERWISE GET WRONG

- **`Assets/Synty/` is GITIGNORED** (461 MB, 2,682 prefabs) — same policy as polyperfect /
  Quaternius / Blink. A fresh clone re-imports from the Asset Store. Builders reference
  `Assets/Synty/...` directly, exactly as the old walls referenced gitignored polyperfect.
- **It is URP-NATIVE. There is no magenta pass to run.** Materials point at
  `PolygonGeneric/Shaders/Generic_Basic.shadergraph`, whose targets include `UniversalTarget` +
  `UniversalLitSubTarget`; `com.unity.shadergraph 17.4.0` is already in `Packages/manifest.json`.
- **`Assets/MedievalCastlePackLite` is SHELVED** — Built-in Standard shader (magenta under URP),
  zero prefabs, 2.64 m wall against our 8.49 m, single visual tier. Do not use it.
- **`SyntyPackageHelper.cs`** is `[InitializeOnLoad]` + `projectChanged` and calls
  `EditorUtility.DisplayDialog` and a Package Manager `AddAndRemoveRequest`. It has not misbehaved
  in ~20 batchmode runs, but it is a live hazard in a headless chain. Left alone deliberately.
- **Synty is authored in cm: 500 file units = 5.00 m.** Confirmed by `MeasureX` at runtime.

### The measured castle kit (from the FBX, then confirmed on instances)

| module | size (m) | tris |
|---|---|---|
| `SM_Bld_Castle_Wall_01` | 5.00 x 5.00 x 0.50 | **20** |
| `SM_Bld_Castle_Battlements_01` | 5.00 x 1.38 x 0.50 | 146 |
| `SM_Bld_Castle_Wall_Gate_01` | 5.67 x 5.86 x 1.26 | 2,530 |
| `SM_Bld_Castle_Wall_Tower_S/M/L_01` | 2.44 / 3.05 / 3.82 dia x 7.52 tall | 608 |

Full ring ~15 k tris. The retired path was ~8 k for 20 stretched slabs; the
`GridWallBuilder`/Tripo path is **1.28 M** (`Resources/Walls/wood_wall.fbx` alone is 26,691 tris).

## 7.3 — THE FOUR TRAPS THAT COST ME TIME. DO NOT RE-LEARN THEM.

1. **UNITY MIRRORS X ON FBX IMPORT.** `SM_Bld_Castle_Wall_01` reports `X -0.00..5.00` in the FBX
   (pivot on the LEFT edge) but the **instantiated prefab measures `-5.00..0.00`** (pivot on the
   RIGHT edge). Reading the pivot convention off the FBX put the whole ring 5 m out and opened a
   2.5 m hole at every corner. `SyntyCastlePerimeterBuilder` now MEASURES the pivot
   (`wallPivotMinX`) off a live instance and places at `(runLeft - pivotMinX)`, which is correct for
   left-edge, right-edge AND centred pivots. **Never infer a pivot from a mesh file.**
2. **`??` DOES NOT WORK WITH UNITY'S FAKE-NULL.**
   `GetComponent<BoxCollider>() ?? AddComponent<BoxCollider>()` returns the fake-null and the next
   line throws. That single operator failed **27 of 29** structures. Always `if (x == null)`.
3. **ADDRESSABLE ADDRESSES ARE FREE TEXT AND CAN CONTAIN SPACES.** The live address is
   `Structures/arcane tower`. A diagnostic that dumped addresses with `\S+` silently truncated it to
   `arcane`, so the map key was wrong and the entry was reported unmapped. Match to end-of-line.
4. **THE PROJECT'S OWN ORACLES CAUGHT ALL OF THESE, NOT ME.** `AssetRootsRegression` (I re-typed
   `"Assets/StructureContent"` instead of `AssetRoots.StructureContent`), the **art-ledger** (I
   re-typed an `EnemyArtPaths` naming token in a hand-written texture-address list), and
   `STRUCTURE_ORIENTATION_FAIL`. Run `DataRegression.RunAll` before believing anything.

**Also:** `PrefabUtility.SaveAsPrefabAsset` THROWS into a folder made with
`Directory.CreateDirectory` — the AssetDatabase does not know it exists. Use
`AssetDatabase.IsValidFolder` + `Refresh`.

## 7.4 — WO-1289 GROUND: DONE, and WHY IT MATTERS BEYOND THE GRASS

`Ground_Meadow_BaseColor.png` shipped at **RGB 93/189/39, chroma 150** — 35 % more saturated than
any other terrain layer, and it is the ground the player stands on. Owner: *"a bright neon green
grass."*

**It passed every gate**, because `TerrainLayerRegression` bounded **Rec.709 luminance only**. Value
cannot see saturation. So the colourblind-safety oracle waved a fluorescent texture straight through.

Fixed BOTH halves: regraded to chroma **85.0** with luminance held at **0.6195 -> 0.6195** (greyscale
read pixel-identical, WO-1044 biome value contract untouched), and added `GroundLayerDef.MaxChroma`
+ `TerrainLayerSet.ChromaTolerance`, enforced in Case 3, authored for all eight layers **from the
suite's own full-resolution measurements** (my first caps came from a 32x32 downsample, which
averages chroma DOWN, and were systematically too tight — the fail-run caught it).

Guard-bites proven **both directions**: old PNG -> `TERRAIN_LAYER_FAIL` naming
`CHROMA=149.7 ... capped at 90`; new PNG -> `TERRAIN_LAYER_OK`.

## 7.5 — WO-1290 WALLS: IN PROGRESS

`Assets/Editor/WallTools/SyntyCastlePerimeterBuilder.cs` replaces `CastleWallsFromRecipe` as the
perimeter source. **Why the old path is retired:** `castle-south-recipe.json` is four pieces mirrored
x4 — 20 objects for the whole castle — and `SM_Wall_Medieval_Stone` (15.75 x 8.49 x 2.39 m) is placed
at `scale.x 1.62` and `1.95`, i.e. the SAME slab rendered at 25.5 m and 30.7 m, plus an arbitrarily
scaled seam filler and a ROUND corner tower squashed 1.28 on X only. Non-uniform scale also breaks
the normal-map tangent basis. Its unit of construction is a scaled slab, so it cannot produce a good
wall; every "seam fix" adds another stretch factor.

**Result:** module 5.00 m (measured), 15 slots/side, span 75.0 m, extent +-39.0 m (plinth 44),
**56 walls + 56 battlements + 4 gates + 4 towers = 120 objects, ALL at scale 1.**
Symmetry oracle: south wall run `X [-37.50, 37.50]`, centre `0.00`, width `75.00` vs 75.00 expected.

**Load-bearing things it preserves — each is a scar with an F8 behind it:**
- `CastleSide_*` root names — `CastleWallNavObstacleInstaller` matches them to carve the NavMesh.
  **The hero is a NavMeshAgent and IGNORES physics colliders**; only a carved NavMesh stops her.
- `Structure` layer on all masonry (WO-449 line-of-sight occlusion).
- **No `Shader.Find`** — returns NULL in batchmode (`CastleHubBuilder.cs:2549`).

**Gate moved deliberately:** the odd-slot algorithm centres the gate at x=0; the old `-4.37` was
hand-placement drift. `castle-south-recipe.json` is re-pointed to `(0,0,-39.0)` because
`BuildGateExitStrips` and `CastleMoatBuilder` (`gateLateral = southGate.x`) both derive the four
bridge/exit positions from it. Its four wall PIECES are now inert; the file survives only as that
gate anchor and says so in a `_wo1290Note`.

**NOT DONE:** the corner tower stands proud of the corner rather than being built into it, and reads
SHORTER than the wall (5.02 m visible vs 5.00 + 1.38 battlement) once its 2.50 m authored foundation
is correctly buried. The ring is CLOSED — this is aesthetic, not a hole. The kit ships
`SM_Bld_Castle_Wall_Corner_M_01` (4.00 m turn) if you want a true bastion.

## 7.6 — WO-1291 STRUCTURES: IN PROGRESS

`Assets/Editor/SyntyStructureRetheme.cs`. **`structures-catalog.json` is NOT touched.** Its `id`
strings are LIVE SAVE KEYS — renaming one orphans every player's building. Instead every
`Structures/*` **address keeps its exact name** and is re-pointed at a wrapper prefab under
`AssetRoots.StructureContent + "/Synty"`. Catalog, save format, `VisualFactory` and every caller are
untouched; only the mesh behind the address changes. Each wrapper carries a BoxCollider fitted to
measured bounds on the `Structure` layer.

**`STRUCTURE_RETHEME_OK swapped=30 unmapped=3 missing=0`**, `REGRESSION_OK 339/339 suites`.

- Texture addresses are skipped **by asset type**, not by a name list (the list re-typed an
  `EnemyArtPaths` token and would go stale).
- `ArcaneSpire_2` is `Wall_Tower_L_01`, **not** `Church_01_A`: the latter measures upright aspect
  **1.08**, under the 1.2 floor every Tower-class row must clear. The oracle states plainly that
  widening the floor is an **OWNER RULING, not a fix** — so the art changed.
- **STILL UNMAPPED, reported not skipped:** `CrystalMine`, `IronMine`, `GenericContainer`. No Synty
  equivalent the lead would stand behind.
- **THE ART PAIRINGS ARE THE LEAD'S AND THE OWNER HAS NOT SEEN THEM.** Blacksmith->armorer/Forge,
  Tavern->ShopAndCrafting, Stables->barracks, Hut->farm, Windmill->Windmill/Watermill, etc. A first
  pass to correct, **not a ruling**. The table is one dictionary at the top of the file.

## 7.7 — TWO THINGS THAT ARE NOT PROVEN, AND MUST NOT BE CLAIMED

1. **The building swap has NO VISUAL PROOF.** Catalog structures spawn at RUNTIME via Addressables,
   so an editor capture cannot show them — the buildings in
   `docs/ui-evidence/wo1290_synty_perimeter/` are HAND-PLACED SCENE OBJECTS that this lane has not
   touched. Proving it needs a runtime capture on pushed content.
2. **THIS LANE CANNOT SHIP WITHOUT `tools\r2-ship.ps1`.** The re-theme re-hashes the Addressable
   content and **bundle names are content-hashed — this build needs ITS OWN push.** A missing push
   fails SILENTLY: installs, launches, plays, placeholder buildings, no on-screen error. It has
   happened FOUR times. Judge by `R2_PUSH_OK` + `R2_PARITY_OK` on a FRESH log, never the exit code,
   and note `Builds/r2-parity.log` is **UTF-16LE** — a plain `grep` finds nothing and reads as a
   false failure.

## 7.8 — WHAT IS LEFT

- **WO-1292 environment dressing** — untouched. ~140 `Rock_*` instances, paths, banners, furniture.
- **WO-1290** corner bastion + tower height.
- **WO-1291** three unmapped addresses; the hand-placed scene storefronts
  (`Blacksmith_Weapons_Storefront`, `Forge_Armor_Storefront`, `Windmill_Food_Storefront`,
  `Lumbermill_Wood_Storefront`, `Jeweler_Gems_Storefront`, `Marketplace_Monetization`,
  `ArcaneTower_MagicUpgrades`, `CastleBarracks`) are still polyperfect.
- **PROD-021** (parked, unrelated to this lane): the shipped build 404s on
  `StandaloneWindows64/catalog_2026.08.31.349579.hash` while that exact file sits in `ServerData/`.
  Root gate defect: `r2-ship.ps1:115` verifies ONE explicit target, so a run that pushes the parent
  and verifies Android emits `R2_PARITY_OK` while Windows 404s.
- **WO-1293** (parked): `BuildPeekStrip` NRE. The method MOVED to `InventoryGrid.cs:290` in
  `d6d3146b2` — grepping `HeroInventoryController.cs` finds nothing.

## 7.9 — HOW TO VERIFY ANY OF THIS

Unity editor **must be CLOSED**; the runner refuses on the lock, and a just-closed editor takes
~40 s to release it.

```
powershell -File .\run-unity-method.ps1 -Method DeNelle.Editor.CompileGate.Run                        -LogName compile-gate.log     -ExpectMarker COMPILE_GATE_OK
powershell -File .\run-unity-method.ps1 -Method DeNelle.Editor.SyntyCastlePerimeterBuilder.BuildInHub -LogName synty-perimeter.log  -ExpectMarker PERIMETER_OK
powershell -File .\run-unity-method.ps1 -Method DeNelle.Editor.SyntyStructureRetheme.Run              -LogName synty-retheme.log    -ExpectMarker STRUCTURE_RETHEME_OK
powershell -File .\run-unity-method.ps1 -Method DeNelle.Editor.SyntyPerimeterProofCapture.Run         -LogName perimeter-proof.log  -ExpectMarker PERIMETER_PROOF_OK
powershell -File .\run-unity-method.ps1 -Method DeNelle.Editor.DataRegression.RunAll                  -LogName data-regression.log  -ExpectMarker REGRESSION_OK
```

**Always pass `-ExpectMarker`.** Without it the runner prints `PASS-UNASSERTED` and proves nothing.

## 7.10 — CODEX INTEGRATION REVIEW (2026-09-01)

Read in full and inspected against the live `Map` in `Assets/Editor/SyntyStructureRetheme.cs`.
The current checkout is already `feat/synty-art-retheme` at `ca4bf5821`; do **not** cherry-pick the
eight commits back into this same worktree or represent them as a separate integrated copy.

### Pairing ruling

The dictionary is structurally safe (stable Addressable addresses, stable save IDs, wrapper collider,
`Structure` layer), but it is **not visually approved**. The purchased pack exposes only 26 complete
building presets; filenames alone cannot prove silhouette, entrance orientation, footprint, or whether
two roles read as distinct on a phone. Therefore no blind remap was made during handover review.

- **Provisionally coherent, still requiring runtime proof:** Blacksmith/Armorer, Tavern/Shop and
  Crafting, Farm/Hut, houses, pet hut, Windmill, arcane tiers, defense tiers, walls/gate, siege, well,
  torch, and caravan.
- **Rejected as final consumer semantics:** `barracks -> Stables`, `lumbermill -> House_06`, and
  `Watermill_Medieval -> House_Windmill`. These may remain temporary wrappers but cannot pass final
  art parity under those identities.
- **Distinctness gate:** `armorer` and `Forge` currently share one preset. That preserves function but
  does not yet prove the player can visually distinguish the two destinations.
- **Still unmapped:** `CrystalMine`, `IronMine`, `GenericContainer`. The pack contains crystal, ore,
  ingot, crate, and environmental-structure pieces, so the likely correct solution is a composed
  wrapper, not a misleading one-prefab substitution.
- **Still outside the Addressable swap:** all eight hand-placed storefronts listed in §7.8.

### Integration and release ruling

- WO-1289 is accepted from its measured chroma and regression evidence.
- WO-1290 and WO-1291 remain **in progress**; their green structural oracles do not satisfy visual
  approval. The short/proud corner tower, provisional pairings, unmapped addresses, storefronts, and
  runtime building capture remain open.
- WO-1292 remains untouched.
- No screenshot under `docs/ui-evidence/wo1290_synty_perimeter/` proves the Addressable building swap.
- No Windows/APK/AAB release may be approved until a fresh content build is pushed and the same run
  emits both `R2_PUSH_OK` and `R2_PARITY_OK`; read the parity log as UTF-16LE.
- PROD-021 must close the single-target verification hole before a multi-platform release is trusted.
  Android parity cannot be used as evidence for the Windows catalog.
- The UI-reskin lane remains logically separate even though its current uncommitted work is present in
  this checkout. Final acceptance requires one integrated compile/regression/geometry/touch/screenshot
  pass after the Synty lane and UI lane are both complete.

## 7.11 — FOLDED REVIEW AND BUILD-GATE RESULT (2026-09-01)

The UI completion review and the Synty lane review are now folded into this handover. Fresh checks on
the combined working tree passed:

- `Builds/fold-review-compile.log` — `COMPILE_GATE_OK :: scripts compiled clean`.
- `Builds/fold-review-regression.log` — `REGRESSION_OK 339/339 suites -- 339 green, 0 red, 0 skipped`.

These results prove code/data health only. They do not override the owner's earlier rule that player
builds wait until all Ready work is RCA'd and implemented. The Windows EXE and Android APK were
therefore **not built** in this pass because the same reviewed checkout still has the following open:

- WO-1290 and WO-1291 remain in progress and lack final runtime visual approval.
- WO-1292 environment dressing is untouched.
- `CrystalMine`, `IronMine`, and `GenericContainer` remain unmapped; eight hand-placed storefronts
  remain on the legacy art.
- The corner bastion/tower presentation and three rejected provisional semantic mappings remain open.
- PROD-021 still leaves single-target Windows/Android R2 verification incomplete.
- No fresh content publication has emitted both `R2_PUSH_OK` and `R2_PARITY_OK` for this combined tree.

Build authorization resumes only after those items are closed and a fresh integrated
compile/regression/visual/content-parity pass is green. Do not label an older artifact under
`Builds/Windows` or `Builds/Android` as containing this review.

## 7.12 — OWNER-AUTHORIZED DIAGNOSTIC WINDOWS BUILD (2026-09-01)

The owner expressly authorized a diagnostic build to inspect the current state despite the open
acceptance items in §7.11. This authorization permits review; it does not mark the artifact release
approved.

- Prior output archived recoverably at `Builds/Windows-before-review-20260901-125725` to avoid the
  known stale executable stub failure.
- Build command used `DeNelle.Editor.DesktopBuild.BuildWindows` with `-BuildTarget Win64` and asserted
  the fresh `[DesktopBuild] SUCCEEDED` marker.
- Evidence: `Builds/owner-review-windows-build.log` — 2,066 MB reported in `00:01:37.7619925`.
- Executable: `Builds/Windows/DefendersOfTheRealm.exe`, 667,648 bytes, SHA-256
  `157DCCDAF52EBCA0E0759FAF35DD53A6985ED577698CA33603047F0F2004CE7E`.
- Companion `level3` and `DeNelle.Village.dll` were freshly emitted in the same build, ruling out a
  stale EXE paired with newer scene data.

This is the development/player-test Windows configuration, preserving the deliberate F8/test tools
without changing normal gameplay behavior. The unresolved Synty visual and R2 parity findings in
§7.11 still apply.

## 7.13 — HARD-REBOOT CHECKPOINT: UI QA BUILD + INTERNET-GATE RCA (2026-09-01)

Owner requested this durable checkpoint immediately before a hard reboot. Unity and the Windows
player were both closed when this section was written.

### Current UI/build state

- Fixed the skill-tree horizontal-progression compile collision by renaming its local source bounds
  in `HeroSkillTreePanelMvvm.cs`; behavior is unchanged by that correction.
- Fresh asserted compile evidence: `Builds/ui-current-compile-20260901-pass2.log` contains
  `COMPILE_GATE_OK` and the runner verdict is PASS.
- Fresh owner-authorized Windows QA build evidence:
  `Builds/ui-current-windows-build-20260901.log` contains `[DesktopBuild] SUCCEEDED`; 28 scenes,
  2,066 MB, `00:01:27.0500391`.
- Test executable remains `Builds/Windows/DefendersOfTheRealm.exe`; SHA-256
  `157DCCDAF52EBCA0E0759FAF35DD53A6985ED577698CA33603047F0F2004CE7E`.
- Unity 6.0.4.8f1 emits a shutdown-only Lifecycle Management `NullReferenceException` after the
  success marker, but exits code 0. The asserted compile and build markers are present and there are
  no compiler errors.

### New icon inputs and decision

- Formal implementation specification: `WorkOrders/WORK_ORDER_1294_blink_skill_tree_hotswap_and_troop_portraits.md`.
  It supersedes stale four-slot presentation language while preserving older gameplay contracts.
- `C:\Users\Elden\Downloads\Elarion_Troop_Icons.zip` contains all nine canonical troop portraits,
  named exactly for their troop IDs. The 3x3 mobile preview is visually approved as the troop source.
- Blink `Assets/Blink/Art/Icons` is the correct source for skill-tree nodes and the three hot-swap
  combat slots, not troop portraits. It is already imported as Sprite art with mobile 128px overrides
  and already mirrored/data-routed through `RpgUiCatalog` plus `concept-icons.json`.
- Neither the new troop archive nor any expanded Blink skill mappings were folded into the QA build
  above; they are the next UI integration pass.

### Active blocker: false first-run internet-required screen

The owner launched the new Windows build and received the internet-required error despite having
working internet. This is an active RCA, not resolved yet.

- Exact current player log:
  `C:\Users\Elden\AppData\LocalLow\DeNelle\Echoes of Elarion\Player.log`
  (last observed 2026-09-01 14:10:28, about 2.9 MB).
- Likely gate is `OfflineContentService.ResolveContentSource` in
  `Assets/_Modules/Core/Addressables/OfflineContentService.cs` around lines 420 onward.
- It currently treats `Application.internetReachability == NotReachable` as authoritative and, on a
  first run without a completed offline pull for this build, immediately sets `ContentSource.Unavailable`
  and displays `offlineFirstRunInternetRequired`. Unity reachability is only a coarse interface flag and
  can be a false negative on Windows.
- The online branch can also produce the same modal when `CheckForCatalogUpdates` cannot prove the
  remote catalog usable and no current-build offline stamp exists. Resume by extracting the latest
  `OfflineContent`, Addressables, catalog, HTTP, and exception lines from the exact Player.log before
  changing code. Determine which branch fired; do not guess from the modal text.
- Required fix direction: prove real shipped/local catalog usability and/or perform a bounded endpoint
  probe; never equate Unity's reachability enum with definitive internet failure. Preserve the honest
  first-install failure when neither shipped/local content nor remote content is usable.

After reboot: inspect the exact log evidence, implement the smallest source-selection correction,
run the relevant offline/content regression plus compile gate, then rebuild the Windows QA player.

## 7.14 — WO-1294 BLINK/TROOPS + WINDOWS/R2 TEST BUILD (2026-09-01)

- Imported the owner's nine canonical troop portraits under `Assets/Resources/RpgUi/troop` and
  routed Barracks, Manage, Raid Deploy, and queue cards through the shared `RpgUiCatalog.RoleTroop`.
- Every one of the 42 authored ability definitions now has a direct concept-ID mapping. The
  assignable tree skills use the same owner-tagged Blink source as their talent node, and the
  importer now mirrors every referenced Blink class family. The quick-swap contract is three slots
  in runtime, VM copy, layout oracle, and capture wording.
- Removed the final HUD view-side onboarding read; `StartWaveHudBridge` remains the predicate owner
  and the view renders its already-gated availability.
- Fresh evidence: `Builds/wo1294-compile-pass2.log` has `COMPILE_GATE_OK`; fresh
  `Builds/wo1294-regression-pass2.log` has `REGRESSION_OK 339/339 suites`.
- Fresh Windows development/QA build: `Builds/wo1294-windows-build.log` has
  `[DesktopBuild] SUCCEEDED`, 2,071 MB in `00:01:18.3249081`.
- Test executable: `Builds/Windows/DefendersOfTheRealm.exe`, 667,648 bytes, SHA-256
  `157DCCDAF52EBCA0E0759FAF35DD53A6985ED577698CA33603047F0F2004CE7E`.
- The prior false internet screen was not a reachability false negative. Exact `Player.log` evidence
  showed a reachable network followed by HTTP 404 for Windows catalog
  `catalog_2026.09.01.350657.hash` and its content-hashed bundles. The source-selection gate was
  correctly refusing unavailable first-run content; the R2 deployment was incomplete.
- Fixed operationally with the matching content push: `R2_PUSH_OK 52 uploaded (94.1 MB), 474
  unchanged`; `R2_PARITY_OK targets=Android,StandaloneWindows64,WebGL objects=192`. Public HEAD
  requests now return HTTP 200 for that exact Windows catalog plus the exact well and watermill
  bundles that previously returned 404.
- Previous player output is recoverable at
  `Builds/Windows-before-wo1294-20260901-144031`. The new player was not launched; owner tests next.

## 7.15 — FINAL VISUAL BATCH + OPEN GATES + WINDOWS/R2 TEST BUILD (2026-09-01)

- Extended the founding-choice modal body fill horizontally so it reaches the wider frame.
- Reworked the shared build-collection card contract: Gathering, Realm, Defenses, and every other
  collection now place the action button in a dedicated footer below the information card instead
  of covering the card's status/cost text.
- Talent Tree now presents the spendable currency as `WISDOM` in the right header plate and removes
  the duplicate adjacent chip. Its three quick-swap slots use centered circular bezels with concept
  art overlays.
- The tutorial's scripted town battle now waits for its intro dialogue/modal to close before spawning
  the wave, so modal posture can no longer suppress the battle HUD when combat starts.
- Rebuilt the Synty castle perimeter against merged-world y=0 using measured prefab bounds. Walls,
  gates, and towers meet the ground. All four gate leaves are authored visibly open, the portcullis
  is removed from the permanent passage, and the existing bidirectional GateTraversal crossing stays
  as the NavMesh safety net.
- Fresh visual evidence: `Builds/final-visual-batch-perimeter-open2-proof.log` contains
  `PERIMETER_PROOF_OK`; captures are under `docs/ui-evidence/wo1290_synty_perimeter`.
- Fresh code evidence: `Builds/final-visual-batch-compile3.log` contains `COMPILE_GATE_OK` and
  `Builds/final-visual-batch-regression2.log` contains `REGRESSION_OK 340/340 suites`.
- Fresh clean Windows build: `Builds/build.log` contains `[DesktopBuild] SUCCEEDED`.
  `Builds/Windows/DefendersOfTheRealm.exe` is 667,648 bytes, SHA-256
  `157DCCDAF52EBCA0E0759FAF35DD53A6985ED577698CA33603047F0F2004CE7E`.
- R2 content is current: `R2_PUSH_OK 0 uploaded, 526 unchanged` and
  `R2_PARITY_OK targets=Android,StandaloneWindows64,WebGL objects=192`. Anonymous CDN access passed
  `R2_CHECK_OK`; the exact Windows catalog `catalog_2026.09.01.350657.hash` returns HTTP 200.
  This directly covers the earlier Windows first-run "no internet" symptom, whose root cause was
  missing remote catalog/content rather than a Windows networking regression.
- Previous Windows output is recoverable at `Builds/previous/Windows-20260901-152746`.

## 7.16 — FELT-TEST CLOSURE: TOWERS, TRAVERSAL, HUD, CLASSES (2026-09-01)

- Restored the pre-wooden Archer Tower visuals (`Tower_Castle_Round`, `Tower_Castle_Square`,
  `Tower_Medieval_Big`) without changing its stable ID, stats, costs, or saved placements. Registered
  all three in the `Structure_Art` Addressable group; the initial full regression correctly rejected
  the catalog until those runtime addresses existed.
- Rebuilt the perimeter with upright/seated corners and added bidirectional `NavMeshLink` passages at
  every permanently open gate. Hero traversal remains available through the short paired crossing.
- Starter settlement placement is now canonical-data-driven through dual-copy
  `starter-settlement-layout.json`: stable catalog ID + x/z/yaw, resolved through `CatalogRegistry`.
- Fixed duplicate shared button labels (including Pause `CLOSE`), square-bounded circular HUD art,
  clickable Skip Tutorial, the missing town Talk action, and Journey Quest/Raid artwork.
- Mage/Ranger primary actions now execute and mirror their authored Q spell/bow ability. Knight Block
  reuses the existing `Block` Animator contract with the authored sword-and-shield held pose.
- Verification: `Builds/compile_final_followup.log` has `COMPILE_GATE_OK`;
  `Builds/regression_final_followup2.log` has `REGRESSION_OK 341/341 suites`;
  `Builds/build_final_followup.log` has `[DesktopBuild] SUCCEEDED` (2,071 MB, 52.0 seconds).
- Windows payload hashes: game assembly
  `340900CC532D2F368911751648B17DFDAC10481EFBD4C628E8E502834F2D5C9D`; overworld `level3`
  `63433F00782CA616D47347D38C0B2CC0DE25EE6F70AFE7CBE619A5D225731856`.
- R2 closure: `R2_PUSH_OK 5 uploaded (0.4 MB), 524 unchanged`;
  `R2_PARITY_OK targets=Android,StandaloneWindows64,WebGL objects=195`; `R2_CHECK_OK`; exact Windows
  catalog HEAD is HTTP 200 / 32 bytes. The follow-up build therefore retains the Windows first-run
  connectivity fix rather than regressing it.

## 7.17 — FINAL FELT-TEST UI + TESTER APK/FIREBASE (2026-09-01)

- Journey uses the owner's exact locked `quests.png` and `Raids.png` wide-card art.
- Fixed the remaining runtime UI causes: Equipment is registered by a persistent bootstrap (there is
  no unlock requirement), Skip Tutorial renders above DialogueView's pointer surface, authored Close
  art no longer receives a duplicate TMP word, repair selection no longer creates an edge-on yellow
  quad/world label, and Defense Report uses matching obsidian wells.
- Mana/Vigor/Focus are the intentional class-resource names for the second hero bar, not requirements.
- Evidence: `COMPILE_GATE_OK`; `REGRESSION_OK 341/341`; Windows `[DesktopBuild] SUCCEEDED` (2,083 MB).
- Fresh tester APK `2026.09.01.351238 (351238)` is 543,703,055 bytes, SHA-256
  `8C9D1BB964557F22C596B122922483F61591BA0C6EDB13361F6149A015BFEAE3`.
- Production dependency gate: `SCHEMA_PARITY_OK 42 table(s)`.
- Content: `R2_PUSH_OK 49 uploaded (85.5 MB), 529 unchanged` and
  `R2_PARITY_OK targets=Android,StandaloneWindows64,WebGL objects=228`.
- Firebase App Distribution succeeded to `testers`: release `2026.09.01.351238`, ID
  `43nnpnk9lad7g`.

## 7.18 - SKIP POINTER + FOUR-GATE CLEARANCE + TESTER APK/FIREBASE (2026-09-01)

- Fixed Skip Tutorial at the pointer-dispatch seam: its visible raycast face is now a descendant of
  `SkipTutorialButton`, allowing EventSystem bubbling to reach the Button. A runtime centre-point
  raycast logs `SKIP_TOP_HIT_OK`, or names the exact blocking hierarchy with
  `SKIP_TOP_HIT_BLOCKED`; validation does not depend on desktop hover feedback.
- Removed every collider from the visible permanently-open gate art. Two wall-owned jamb colliders
  per gate extend the wall to a measured 4.00 m central passage. Nav carving ignores disabled/trigger
  colliders. Builder proof passed `GATE_CLEARANCE_OK 4/4 gates`; perimeter proof passed
  `PERIMETER_PROOF_OK`.
- Fresh compile passed `COMPILE_GATE_OK`; full regression passed `REGRESSION_OK 341/341 suites`
  with 341 green, zero red, and zero skipped.
- Fresh tester APK `2026.09.01.351290 (351290)` is 543,702,575 bytes, SHA-256
  `E377A916D030E5693F7045A0C4A4D733E2CCF40E2FE22692143659F3ACE1DF50`.
- Schema parity passed all 42 tables. Content shipment passed `R2_PUSH_OK 2 uploaded (0.1 MB), 578
  unchanged` and `R2_PARITY_OK targets=Android,StandaloneWindows64,WebGL objects=228`.
- Firebase App Distribution succeeded to `testers`: release `2026.09.01.351290`, ID
  `46rbucqgcr04g`.

---

## PART 8 - THE MANAGE RE-LAYOUT BATCH + the file-disjoint READY tail (2026-09-05 evening, CLI lead)

### 8.1 What this batch is
Owner, 2026-09-05 evening, verbatim: *"i want tonight to be a focus on the UI layout that we have. I dont love it. Think
of Clash of clans and warcraft, this is too much text on screen whereas they are simple and intuitive"* - *"manage is the
big offender"* - *"we can reuse the building cards from build"* - *"feel free to hand as much to Codex as you want"*.
Her two mockups (Manage - Buildings, Manage - Troops) are the approved target; the Troops tab already has that shape
in the tree (WO-1382, `65d5a7eae`). PART 1-6 and 7.2-7.3 of this file still bind. Every file:line cited in the work
orders was read at `44d46128d`; **re-read at your base commit before you rely on a number** (CLAUDE.md s11B).

**BASE COMMIT: `44d46128d` (GO - lead ruling 2026-09-05 evening).** The tree-closing commits that land tonight
(WO-1416 / 1417 / 1402 / 1403 / 1407) touch NONE of the 1418 lane files, so the dev lane starts now from this HEAD and
the lead reconciles its hand-back three-way onto the newer HEAD by explicit path. The ONE exception is **WO-1404**,
which shares `Assets/_Modules/Village/Buildings/BuildTimerService.cs` with the in-house 1407 lane: start it LAST, or
after the lead posts the post-commit hash here.
**LOCKED FILES (main-tree `git status --short -- Assets/` at go - every one is in flight, do not edit any of them):**
```
Assets/Editor/Regression/BuildCollectionPlayerRegression.cs   Assets/Editor/Regression/CollectorIncomeRegression.cs
Assets/Editor/Regression/DataRegression.cs                    Assets/Editor/Regression/EchoResourcePickerRegression.cs
Assets/Editor/Regression/HudLabelFitRegression.cs             Assets/Editor/Regression/RetiredVocabularyRegression.cs
Assets/Editor/Regression/RaidSelectionSpoilsRegression.cs (new)
Assets/Resources/Data/Canonical/{canon-strings,guide-content,structures-catalog}.json  + StreamingAssets twins
Assets/_Modules/Core/Catalog/StructureRole.cs                 Assets/_Modules/Core/UI/ElarionUi.cs
Assets/_Modules/Core/UI/QueueRailView.cs                      Assets/_Modules/Core/UI/RaidEntryGate.cs
Assets/_Modules/Core/HudModel/HudStateCopy.cs (new)           Assets/_Modules/HUD/Kit/HudKitController.cs
Assets/_Modules/Village/BuildMode/BuildCollectionBrowser.cs   Assets/_Modules/Village/Buildings/BuildTimerService.cs
Assets/_Modules/Village/Buildings/BuildingInteractable.cs     Assets/_Modules/Village/Buildings/Progression/ResourceBuildingHarvester.cs
Assets/_Modules/Village/Buildings/Progression/ResourceBuildingProgression.cs
Assets/_Modules/Village/Catalog/Generated/CatalogFallbackData.g.cs
Assets/_Modules/Village/Hero/RaidSelectionScreen.cs           Assets/_Modules/Village/Hero/RaidSelectionVM.cs
Assets/_Modules/Village/Hero/RaidDeployScreen.cs              Assets/_Modules/Village/Hero/RaidDeployVM.cs
Assets/_Modules/Village/NPCs/CastleVendorNpcInjector.cs       Assets/_Modules/Village/Troops/RaidScoring.cs
Assets/_Modules/Village/Waves/WaveCountdownUI.cs
```
`BuildCollectionBrowser.cs` is on that list on purpose: lane A READS it to copy `AddGoldPerimeter`; it does not edit it.

**UPDATE 22:10 - three lanes COMMITTED: `9a9e65c8a` (WO-1416), `e3b082a1a` (WO-1417), `003b64ce2` (WO-1407); board
`4ebbaccf7`.** (An earlier note named `4ed44db76`/`15ca64163`/`04dec910a` - those commits were redone with clean
scoping and no longer exist; do not look for them.) The following files are therefore UNLOCKED and at their final
shape for tonight - re-read them at `003b64ce2` or later:
`HudKitController.cs`, `BuildTimerService.cs`, `HudActionBarModel.cs`, `HudStateCopy.cs`, `ElarionUi.cs`,
`QueueRailView.cs`, `RaidEntryGate.cs`, `WaveCountdownUI.cs`, `BuildCollectionBrowser.cs`, `StructureRole.cs`, the
three canonical JSON pairs, and the 1416/1417 suites. **WO-1404 and WO-1419 may start now** (base `003b64ce2`).
Still LOCKED (in flight, a lane is fixing two capture findings): `RaidDeployScreen.cs`, `RaidDeployVM.cs`,
`RaidSelectionScreen.cs`, `RaidSelectionVM.cs`, `RaidScoring.cs`, `DataRegression.cs`, the two raid suites.

### 8.2 Assignments, in priority order
Worktree per lane: `git -C D:\eoa worktree add D:\eoa-codex-1418-<lane> <BASE>` on branch `codex/wo-1418-<lane>`.
No commit, no push, no Unity, no `DataRegression.cs` edits (hand registration lines back as text).

1. **WO-1418 Manage - Buildings re-layout** - `WorkOrders/WORK_ORDER_1418_manage_buildings_relayout.md`, read it in
   full; it carries the architecture ruling, the pin list, the `BuildingChoiceVM` field list and the ten RED-first
   cases. Four lanes, file-disjoint:
   - **Lane A** (Core kit): `Assets/_Modules/Core/UI/CostFormat.cs` + new `ElarionUiKitGoldPerimeter.cs` (+ .meta).
   - **Lane B** (VM): `Assets/_Modules/Village/UI/Manage/ManageScreenVM.cs` only.
   - **Lane C** (View + capture + suite): `Assets/_Modules/Village/UI/Manage/ManageScreenPanel.cs`,
     `Assets/Editor/UICaptureLaunch.cs`, new `Assets/Editor/Regression/ManageBuildingsCardRegression.cs`.
     Authors in parallel against the VM field list; compiles after B.
   - **Lane D** (store return, WO-1412 store half + **WO-1409** in the same lane because both live in
     `Assets/_Modules/Wallet/PackStore.cs`): store CLOSE returns to the sending Manage tab; the wallet-less Night Market
     says WHY each shelf is unavailable and its right rail stops overlapping (`WorkOrders/WORK_ORDER_1409_*.md`).
   The WO absorbs WO-1405's benefit line, WO-1406 and WO-1412; do not take those three separately.
2. **WO-1404** Journey deck subtitles truncate and carry no state - `Assets/_Modules/Core/HudModel/PostureSignals.cs`,
   `Assets/_Modules/Village/Buildings/BuildTimerService.cs` (+ whatever the WO names). Disjoint from lane A-D.
3. **WO-1410** Hero screens carry four names for two screens; WISDOM unexplained; Loadout empty state is a sentence not
   a door - `Assets/_Modules/Village/Talents/HeroSkillTreePanelMvvm.cs` (+ the WO's list).
4. **WO-1413** copy hygiene across several screens - `Assets/Resources/Data/Canonical/dialogue/dialogues.json` (+ its
   StreamingAssets twin, byte-identical, binary-safe edits - PART 7.3 trap), `Assets/_Modules/Village/Harvest/EchoWorkforceVM.cs`
   and the WO's list. **Before touching any file, check it is not in another lane above or in the LOCKED list; if it
   is, hand that item back as "blocked by lane X" instead of editing.**
5. **WO-1348** VFX picks tunable from the Command Center - the hold is released (1343/1344 CLOSED, 1345-1347 FIXED;
   its Status text is stale). `Assets/_Modules/Core/Addressables/VfxAssetLoader.cs` (runtime override seam), `api/admin/*`
   picker, `api/_lib/tunable-manifest.js`. The tunables RAIL files (`Core/Ops/RemoteTunables.cs`, `RemoteTunablesService.cs`,
   `api/_lib/tunables.js`, `RemoteTunablesDefaultsRegression.cs`) are lead-owned merge files: hand back the key +
   default as text. `api/` has node tests - run them and paste the output.

6. **WO-1419** the Heartfire plate paints flame ICONS, not `[*] [ ]` ASCII pips - owner ruling 2026-09-05 evening,
   `WorkOrders/WORK_ORDER_1419_heartfire_pips_are_icons_not_ascii.md`. Files: `Assets/_Modules/Core/State/HeartfireCharges.cs`,
   `Assets/_Modules/HUD/Kit/HudKitController.cs` (LOCKED until the 1407 commit - the lead posts the post-commit hash
   here; start this one after WO-1404, same rule), a new regression, and a three-candidate icon survey handed back
   (the lead picks against the greyscale gate; do not ask the owner for a hue).

**HELD - do not take:** WO-1408 and WO-1411 (both add a door INTO Manage; they wait until 1418 lands), WO-1402 /
WO-1403 / WO-1407 (in-house lanes tonight), WO-1416 / WO-1417 (done, committing), WO-1382 (landed; the template),
every PARK-list ticket (1373 / 1377 / 1292 / 1314 / 1327 / 1215 / 1184 / 1244).

### 8.3 Rules that bite on THIS batch
- Owner is red/green colourblind: state is a WORD, never hue alone. ASCII-only TMP strings.
- Touch targets >= `ElarionUiKit.MinTouchPx` (112 ref px; `ElarionUiKit.cs:347`).
- Presentation never touches game objects: the VM feeds the View; the View owns no state but selection.
- You cannot run EditMode suites. Author each regression case with its one-line REVERT recipe in a comment; the lead
  proves RED then GREEN at gate. A case that cannot fail is a P1 defect (PART 3.6).
- Instrument, do not strip: every new decision point carries a `FlowTrace.Step/Warn` line; never delete an existing one.
- Canonical JSON: Resources + StreamingAssets twins byte-identical; edit bytes, never text-mode rewrites; prove LF counts.
- Line numbers in the WOs are from `44d46128d`; the Part-B commits will shift some. Re-read at the base commit.

### 8.4 Hand back (PART 5 format, `batch_results_state.md`, one section per lane)
files changed with line numbers; brace + NUL counts per `.cs`; the `DataRegression.cs` registration line(s) as text;
the RED recipe per case; node test output for anything under `api/`; what was NOT done and why; any file you needed
but found locked or in another lane. Everything is a claim until the lead proves it against the tree.

### 8.6 Hand-back received 22:2x (batch_results_state.md:607) - lead dispositions
- **WO-1418 A-D:** received; under lead review + gate tonight (raid lane first, then the 1418 overlay, then capture).
  The stale `ManageApprovedLauncherRegression.cs:32` toast pin is re-pointed BY THE LEAD with the WO-1406 ruling.
  Lane D's two owed oracles (`NightMarketNoWalletRegression`, `StoreReturnToManageRegression`) are authored by an
  in-house lane after the overlay lands on main - not owed by the dev lane.
- **WO-1404:** received (base `003b64ce2`); gated with the overlay. Thank you for pulling the RaidSelectionVM hunk.
- **WO-1413 partial:** received; gated with the overlay. The blocked halves (UICaptureLaunch fixtures, HudKitController
  faces, dialogue twins) are UNLOCKED once the 1418 overlay is committed - the lead posts that hash here; finish then.
- **WO-1348:** received; the rail glue is lead-owned and lands AFTER tonight's device build (it is not player-felt
  tonight). No action for the dev lane.
- **WO-1410 clean bounce - UNBLOCKED:** `canon-strings.json` (both twins) were locked only while WO-1416 was in flight;
  1416 is committed at `9a9e65c8a`. Take 1410 now from base `003b64ce2`: author the `heroBag` / `heroSkills` /
  `heroLoadout` keys in BOTH twins (byte-identical, binary-safe edits, LF count proven - PART 7.3) and the C# consumers.
- **WO-1419:** lead selection recorded in 8.7 below; proceed to implementation from base `003b64ce2`.

### 8.7 WO-1419 flame icon - LEAD SELECTION (greyscale gate, all three opened by the lead)
**Candidate A: `ItemIcons/cons_emberfire_bomb`.** A literal flame inside a round ember medallion, transparent outer
corners, mid-grey mean (~133/255) with a bright core: at pip scale it reads as a CHARGE TOKEN, which is what a
Heartfire charge is. B is an opaque square (dims as a tile, not a pip); C is a flaming skull - a hostile emblem on the
player's own Heart plate, wrong tone. Slot treatment: lit = A at alpha 1.0; spent = A at alpha 0.25 AND desaturated
(Image colour grey 0.55) so the two states differ in shape-fill and luminance, never hue alone. Slot size = the label's
line height, three slots + the `CountLabel` word form beside them. Proceed from base `003b64ce2`; re-point the
`HeartfireRegression.cs:290/292` spent-bracket literals and the `HudLabelFitRegression.cs:1509` FlameRow composition WITH
this WO (keep `FlameRow` for traces), RED recipes named.

### 8.8 REWORK REQUEST - WO-1418 overlay (lead review 22:4x, read-only, every item cited in the worktree)
Fix in the lane worktrees, refresh `D:\eoa-codex-1418-integration`, append a short hand-back. Base stays `44d46128d`.
1. **COMPILE BLOCKER (lane B, `ManageScreenVM.cs` `BuildSlotOffer`):** `pack.AmountLabel(CurrencyKind.Skr)` and
   `pack.UsdApprox()` are `DeNelle.Wallet` extension methods (`Assets/_Modules/Wallet/SolanaPackPricing.cs:106,115`,
   `CurrencyKind` at `WalletService.cs:45`). `DeNelle.Village.asmdef` does not reference `DeNelle.Wallet`
   (`PackCatalog.cs:329` records that those moved OUT of Commerce). Use the Commerce-visible price
   (`pack.UsdReference`, what `PackStore.cs:~2913` uses) or publish the SKR label through Core; do NOT add an asmdef
   reference from Village to Wallet.
2. **Training chip bypasses the Barracks lock (lane C, panel ~1059-1067):** the chip tap calls `ShowOperational(dest)`
   which has no `BarracksUnlock.IsUnlocked` guard (only the card path ~872 and `Open(string)` ~393 guard). Route the chip
   through the same guarded door as the card: locked -> the `BUILD A BARRACKS` door, never a locked workspace.
3. **Two hollow RED recipes (lane C suite):** case 1's recipe names the `continue` in `BuildBuildingsBrowse`, a different
   method from `BuildBuildingChoices`, so restoring it changes nothing; case 2 cannot go RED because a third fallback
   `"A village structure."` exists. Rewrite both recipes so each names a one-line revert INSIDE the code the case measures
   (e.g. skip maxed ids in `BuildBuildingChoices`; blank the Description assignment).
4. **Locked and Building cards early-return before the cost row and the benefit line** - WO section 4 says every non-Max
   card carries both (the player must see what the locked tier costs and buys). Paint them; only the CTA differs.
5. **Verify, then say so:** does `PackStore.CloseViaInteractor` reach `PanelManager.CloseAll()`? `PanelManager.cs:458`
   calls `ClearReturnDoor("closeall")`, which would kill the return door before `PumpReturnDoor` fires. Cite the path.
Accepted as-is (no rework): the PackStore.cs WO-1409 work (it was lane D's assignment); the registration line (it is in
the hand-back, the lead adds it); `TrainingChipText` delegating to `Describe()`; `TroopChoiceVM.Requirement` "N in your
army"; the return door living in the VM per ruling 8.5 #3; BUILDING NOW full-width. The `ManageApprovedLauncherRegression.cs:32`
toast pin is re-pointed by the lead. `StoreReturnToManageRegression` + `NightMarketNoWalletRegression` are authored
in-house after landing.

### 8.9 REWORK REQUEST - WO-1404 and WO-1413 (lead review 22:2x, read-only, cited in the worktrees)
**WO-1404** (`D:\eoa-codex-1404`, base `003b64ce2`):
1. **COMPILE BLOCKER:** `JourneyDeckSubtitleRegression.cs:4` has `using DeNelle.HUD;` and news up `JourneyDeckSubtitleVM`.
   `Assets/Editor/Regression/DeNelle.EditorRegression.asmdef` does NOT reference `DeNelle.HUD` (no suite in that folder
   does; they lint HUD as text). Lead ruling: **move `JourneyDeckSubtitleVM` to `Assets/_Modules/Core/HudModel/`**
   (Core, beside `HudStateCopy.cs` - it is a pure VM with no UnityEngine.UI types); the suite then uses
   `DeNelle.Core.HudModel`. Do not widen the regression asmdef.
2. **Open-camp count ignores the victory/escalation lock:** `BuildTimerService.cs:2237` counts a camp open on
   `GarrisonCount(def) <= deployableBodies` alone; `RaidSelectionVM.IsLocked(id)` (`:370`, from `ResolveLock`) must also
   be false. A locked camp advertised as open on the Journey card is the WO-1402/1403 door lying.
3. **The producer runs at 1 Hz and constructs a full `RaidSelectionVM` (whose ctor emits a `FlowTrace.Step`) every
   second** (`BuildTimerService.cs:2222-2240`, inside the 1 Hz `PublishArmyStatus`). That is one log line per second
   forever - the `[Flow:Offset]` ring-buffer failure class. Recompute only when an input changes (army version, victory
   count, catalog) and cache; `PostureSignals.SetRaidOpenCampCount` is already change-only, make the producer match.
4. The capture fixture must publish an army cap so the Journey frame reads `Army 0 / 10`, not `0 / 0`.
5. Hand-back wording: the `<=` predicate is newly authored in BuildTimerService (only `GarrisonCount` exists at base);
   say so.
**WO-1413** (`D:\eoa-codex-1413`, base `44d46128d`):
6. **Pause:** the "Resume" write is inert - `MedievalUiSkin.ApplyShell` (`MedievalUiSkin.cs:104,137-140`) disables the
   TMP child and the baked ornate plate reads CLOSE, so the screen shows Settings / Quit / CLOSE and
   `PauseMedievalSkinRegression.cs:20` (`ApplyButton(resume, primary: true)`) goes RED. Lead ruling for tonight:
   **revert the PauseController.cs change entirely**; "Pause: RESUME only" goes to the owner's rulings list (the primary
   Resume face is owner-approved skin, and the ornate CLOSE is the kit shell - retiring either is her call).
7. **`Assets/Tests/EditMode/EchoWorkforceVMTests.cs:98`** asserts `Does.Contain("x2")` on the old copy; the fake does not
   implement the new `IEchoHarvestBonusReadout`. Update the test (that folder is not locked): implement the readout on
   the fake and assert the new `Echoes 2/6 - harvest +N% together` shape.
8. Stale prose the WO's own scan would catch: "Reset Hero & Pet" in comments at `HelpMenu.cs:8`, `PanelRouter.cs:146`
   (Core - hand back as text), `SettingsController.cs:314`; `EchoWorkforceVM.cs:149` doc still says `xN`. Fix the two in
   your files; report the Core one.
Accepted: HelpMenu danger confirm via `ElarionUiKit.BuildConfirmModal`; `EchoBonusCalculator` numeric identity (verified);
Settings slider kept; DailyChest CTA hide with the reward path untouched; all ASCII.

### 8.11 COMMITTED 23:1x - new base for the dev lane
- **WO-1418** -> `3c677027e` (lanes A-D + rework, lead re-point of the launcher toast pin, suite registered).
- **WO-1410** -> `ecf647b53` (+ three lead-review fixes: popup word, Wisdom plate width, OPEN SKILLS door at the
  112 px floor - the secondary capture caught the door at 67-77 px).
- **WO-1419** -> `11dfea3c1` (+ sprite cache and a missing-sprite FlowTrace.Once).
- **The 8.10 polish rebases onto `ecf647b53`.** `ManageScreenPanel.cs` / `ManageScreenVM.cs` / `UICaptureLaunch.cs` /
  `HeroSkillTreePanelMvvm.cs` / `HeroLoadoutPanelMvvm.cs` all moved - re-read at that hash before editing.
- **WO-1404** -> `5661d71e4`; **WO-1413 part 1** -> `458baf57f` (both gated COMPILE_GATE_OK + REGRESSION_OK 389/389 +
  UI_CAPTURE_OK; Journey and Help frames opened). **WO-1413's blocked halves (UICaptureLaunch fixtures,
  HudKitController faces, dialogue twins, CopyHygieneRegression) are UNLOCKED at `458baf57f`** - take them as a
  1413 part 2 AFTER the 8.10 polish (the polish is what gates tonight's device build).
- Current HEAD for any new worktree: `458baf57f`.

### 8.12 LANDED 23:5x - polish + 1413 part 2 dispositions
- **s8.10 polish -> `73a31b67a`.** 5/6 PASS on lead review; item 5 accepted with a correction: the hand-back said the
  tier sheets are square - on disk EVERY `Portraits/` file is 784x1168 (the "square" ones are square only by their
  import profile). The removed `Portraits/<slug>-<level>` route is accepted because those sheets belong to Defense
  ladders that never appear on the Buildings tab; the `[building-art-palette-first]` pin gets reworded when the route
  returns. Nits (not rework): "+N more" counts stack children; the unresolved-art path uses `Warn` (~8 per rebuild) -
  make it `Once` next time you are in the file.
- **1413 part 2:** gated green after ONE lead correction - `CopyHygieneRegression` `[retired-pet]` was a
  case-insensitive "& PET" search that matched ordinary C# (`&& pet.Id`) in three files outside the lane: a hollow trap
  that went RED on code, not copy. Now a case-sensitive phrase match ignoring `&&`. Rule for the lane: a copy scan
  matches COPY (case-sensitive phrase, word boundary), never a substring that C# syntax can produce. Commit hash
  posted: **1413 part 2 -> `9ec35ed52`**.
- Next base for any new worktree: `9ec35ed52`.

### 8.13 ART DROP LANDED (00:2x) + two duplicated-state nits for the lane's next pass
- The 26 building portraits + the resolver are COMMITTED at `85866703e` - the next base for any new worktree. Gates: COMPILE_GATE_OK c14,
  REGRESSION_OK 390/390 r15, MANAGE_OPERATIONAL_CAPTURE_OK 12/12 capman3; Cathedral / Forge medallions paint their tier
  sheets. Thank you - this closes the art gap named in 8.12.
- **Nit 1 (resolver):** `LooksLikeStructureArt` (the tall-2:3 NPC-face guard) was deleted and replaced by
  `ManageBuildingPortraitGaps`, a hand-copied set of the six ladder ids. Because every `BuildingChoiceVM.Id` is one of
  those six by construction (`BuildingTierCatalog.IsUpgradable`), the palette branch is unreachable today, and a 7th
  ladder in `building-tiers.json` would take the palette route with NO face rejection. Restore the aspect guard on the
  legacy route and drop the id list; make the unresolved-art trace `FlowTrace.Once`.
- **Nit 2 (oracle):** `ManageBuildingsCardRegression` hardcodes `ids = {...}` and `maxLevels = {4,4,6,4,4,4}` - a third
  and fourth copy of state that lives in `building-tiers.json`. Derive both from `BuildingTierCatalog.All` + `MaxTier`
  so a new tier with missing art goes RED. The `[building-art-palette-first]` failure message still says "can paint NPC
  Portraits art" though the case no longer tests aspect rejection - reword.
- Owner continuity note carried to her rulings: several L1 sheets fill ~97% of the canvas vs ~70-80% at higher tiers.

### 8.10 WO-1418 POLISH REWORK - from the first opened frames (`ManageBuildings_2670x1200.png` / `_1920x1080.png`, 22:45)
The shape is the mockup - rail, selected card, BUILDING NOW, chips as doors - and it is being COMMITTED as gated so the
base advances. These are measured defects on the frames; fix on top of the committed base (the lead posts the hash):
1. **Chip copy overflows at 1920x1080:** `Builders 2/2 busy - 3/5 qu...` (all three chips ellipsize). Shorten the busy
   form (e.g. `Builders 2/2 . 3 queued`) and `FitSingleLine` the chip label; the idle form stays `<Name> idle - N free`.
2. **BUILDING NOW rows paint the raw job id:** row 2 reads `Barracks:2:0 -> L4` (a `QueueRowVM.Label` for Builder jobs)
   and its medallion is a blank disc. Rows must read the building's display name (`BuildingTierCatalog.Find(id).DisplayName`)
   + `-> L<n>` and resolve the same portrait the rail uses. Row 2 also renders OUTSIDE the band's dark plate at 1920
   (the extra `BuildingNowRow_n` hosts sit below the band host) - either widen the band to hold N rows or cap at the
   rows that fit and say `+N more`; never paint a row outside the plate.
3. **Locked card CTA face reads `LEVEL 1 . T5`:** jargon. The disabled face must say what unlocks it in words:
   `UNLOCKS AT VILLAGE LEVEL 5` (from `RequiresVillageTier`); the rail row may keep the short `Level 1 . T5`.
4. **Rail scroll starts mid-list:** the first row is clipped at the top (`Level 3 . Building` half visible). On open the
   rail must start at the top, or scroll so the SELECTED row is fully visible - never a half row at the top.
5. **Portraits are NPC faces for Forge / Lumber Mill** (the `Portraits/<slug>` route lands on the vendor NPC art that
   shares the building's name). Prefer `BuildPaletteUI.ResolveEntryArtPublic(entry)` (the palette's building art) and
   take a `Portraits/<slug>-<level>` tier sheet only when that file is a STRUCTURE sheet (arcane-spire-N, archer-tower-N,
   ballista-N exist; report which ids have none). Lead flag for the owner: some buildings have no building portrait at
   all today - art drop, not code.
6. **Description and "After upgrade" lines ellipsize** (`...Mage spell p...`). Owner's ruling is LESS text: cap each at
   one line with `FitSingleLine` at the kit floor, and prefer the SHORT tier `Effect` (first clause up to the first
   period) over the full sentence. Report the longest authored line per building so she can cut copy if she wants.
Cost chips: wood/stone still word-fallback where the currency sprite is missing - expected, not a defect.

### 8.5 Prep findings from the dev lane (relayed by the owner) - lead rulings, all three ACCEPTED
1. **WO-1406 chips:** yes - all three channel chips activate their tab (Builders -> Buildings, Training -> Troops,
   Research -> Research). Only the separate QUEUE control (`ManageQueueDrawerToggle`) opens the drawer; chip 1's old
   transparent drawer button is retired in lane C. Keep the `[queue-toggle-closes]` pin green
   (`ManageQueueDrawerRegression.cs:168-188`).
2. **Army / camp summary data:** yes - project it through `ManageScreenVM` in lane B (a small `ArmySummary` or fields on
   the existing header VM), never read from the View. The View paints words the VM hands it.
3. **Store CLOSE return:** yes - the originating tab cannot be recovered from the current calls. Lane B/C provides the
   caller-side handoff (the drawer's store door passes `(PanelId.Manage, "<tab>")` when it opens the store) through the
   EXISTING return-door arbiter that WO-1400 shipped for the deck return; lane D consumes it on CLOSE. No second
   return mechanism. Lane C hands the exact door line to lane D as text if the files are split across people.

### 9. WO-1605 localization goal - checkpoint (2026-09-09)

- The foundation began at `20467d539` on `dev`. The shared tree already contained the owner's uncommitted
  Feedback/Jeweler/Manage/build work, which was preserved. No stash/reset/cleanup was used.
- One authority now exists: Core `LocalText` -> `ILocalTextProvider` -> the asynchronous
  `DeNelle.Localization.UnityLocalizationProvider` -> Unity `GameStrings`.
- Feature ownership stays separate through domain keys and thin `LocalizedText` /
  `LocalizedText<TArguments>` catalogs; English is not duplicated at call sites.
- Phase A-B began with authority + Settings + HUD/Raid shims. Village/Canon compatibility readers now
  forward to the facade. Store has 39 translated keys: its seven-key BUY GATE, six-key Pi skin, and
  seven-key safe presentation cohort are live through typed wrappers and `LocalText`; the eight wallet
  and 11 commerce keys remain staged behind legacy `StoreStrings`/canon readers. Store is not fully migrated.
- `storeBuyWalletRequired` uses named `{Threshold}` exactly twice in all ten required locales.
- The live safe Store presentation cohort is exactly `storeBandGap`, `storeBandGapSub`, `storeBandBasket`,
  `storeSpotlightEmpty`, `storeLedgerHeading`, `storeCardOwned`, and `storeCardGap`. Offer/patronage
  assertions, value and balance comparisons, ambiguous locked-state wording, and the four trust-strip
  claims remain held pending product/channel/formatting review; the covenant remains English raster art.
- Dungeon chests now resolve `interaction.chest.open` and `interaction.chest.blockedByEnemies` through
  `ChestInteractionText`. `BreakableContainer` uses localized copy for the open prompt and one shared
  combat-refusal resolution for both prompt and toast; the old canon-twin chest keys/note are removed.
- `LocalizationBuilder.BuildAll` now reconciles 416 English keys into 2,496 enabled-table entries.
- Registered localization gates now cover authority, player-literal leakage, exact locale/table parity,
  Smart arguments, Settings in-place refresh/selector wiring, glyph coverage, combat Flee state,
  Google Play variant policy, and Store presentation runtime authority.
  Post-Store-cohort focused evidence: `Builds/localization-regression-store-buy-data.log`,
  `LOCALIZATION_REGRESSION_OK 7/7 suites`.
  Full evidence: `Builds/data-regression-localization-final2.log`,
  `REGRESSION_OK 462/462 suites`.
- Fresh mixed-tree evidence `Builds/data-regression-localization-store-cohorts-final.log` is 460/463:
  one collector source-shape assertion, five legacy HUD canon-parity assertions, and the Flee suite's
  missing registration in the dirty `DataRegression` edit. The focused localization lane remains 7/7.
- Chest evidence is compile/import at 2,496 entries, `CHEST_OK`,
  `LOCALIZATION_REGRESSION_OK 7/7 suites`, and `LOCALE_FONT_BUILD_OK`.
- BUY GATE evidence is importer 2,496, `BUY_GATE_OK`, `STORE_PI_SKIN_OK`,
  `LOCALIZATION_REGRESSION_OK 7/7 suites`, and a green `GooglePlayPackagingRegression` source gate.
- Safe-presentation evidence is `LOCALIZATION_REGRESSION_OK 9/9 suites`; the concurrent full-tree run
  includes `[store-presentation-localization]` and reports `REGRESSION_OK 467/467 suites`.
- Manifest generation/check is deterministic: 6,034 report rows = 416 `keyedEntry` + 85 `keyedCall`
  + 5,517 `literalCandidate` + 16 `imageTextCandidate`. Literal debt remains report-only/unarmed and
  deliberately unreviewed; domain classification remains required before the new-debt ratchet can be armed.
- Future player-facing copy changes must update all ten required locale catalogs and mirrors, rebuild
  all six enabled tables, and pass localization tests. Limited/provisional translations may await human
  review but must still satisfy parity.
- Google Play localization is now source/transaction-safe. `GooglePlayLocalizationVariant` transforms
  20 canonical locale files plus the shared table and six enabled `GameStrings` tables before Addressables,
  using an exact reviewed policy: 57 unavailable Wallet/Web3/Settings/Jeweler rows are stripped and five
  visible rows receive localized Play-neutral replacements in all ten required locales. The combined
  localization/content-exclusion regression restores all 27 assets byte-for-byte and leaves no ledger or
  quarantine behind. Focused policy evidence is `LOCALIZATION_REGRESSION_OK 8/8 suites`; packaging source
  evidence is `PLAY_PACKAGING_REGRESSION_OK`. A physical built-AAB scanner proof is still required before
  calling the Google Play artifact release-ready.
- CompileGate emitted `COMPILE_GATE_OK`; its outer wrapper remained red only on the known missing
  optional WebGL built-in module (`UnityEngine.WebGLInput` in the Solana package), not project code.

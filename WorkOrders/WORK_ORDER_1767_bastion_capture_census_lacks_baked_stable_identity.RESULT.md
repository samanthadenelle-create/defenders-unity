# WORK ORDER 1767 — RESULT

**Status:** IMPLEMENTED, NOT YET GATED
**Branch:** `dev` · **Date:** 2026-09-16 · **Lane:** implementation (raid / owned-town capture, WO-1705 silo)
**Owner ruling honoured:** §6b option **(A)** — verbatim *"(A) re-derive OwnedTown_IronBastion and its
manifest from the new 158-wall raid scene"*, keeping WO-1732's partition. Recorded verbatim in the WO.

**The lead still owns:** the combined-tree `COMPILE_GATE_OK`, `DataRegression` registration line (below),
`REGRESSION_OK <n>/<n> suites`, the commit, and the board regeneration. This lane ran **no** compile gate,
**no** DataRegression, and made **no** commit.

---

## 1. The defect, and what shipped

`RaidCaptureCensus.cs:38-39` throws on the first structure whose `OwnedTemplateIdentity._templateId` is
empty. Owner's Seeker build `2026.09.16.371701`:

```
[Flow:Raid] Precombat capture census failed: Captured structure lacks a baked stable identity: Wall_Outer_SS_0
```

Root cause (WO §3, re-verified at source this session): `_templateId` was **hand-stamped onto a
regenerated artifact** by `OwnedTemplateIdentityBake` with `Guid.NewGuid()`, and nothing verified it.
WO-1732 (`452fc14fd`) put `iron_bastion` into `RaidConfigIdsFromCatalog`, so `BuildAllRaidScenes`
regenerated the raid scene for the first time — a fresh scene, so all 221 stamped components went with
the old one, **and every gate stayed green**. The pair also desynchronised: raid 169 structures / town 221
/ manifest 221.

Measured on the tree before this change (2026-09-16, `grep -c` by component script GUID):

| Artifact | WallSegment | DefenseTower | RaidSpire | census | `OwnedTemplateIdentity` refs | `_templateId` |
|---|---|---|---|---|---|---|
| `RaidBase_IronBastion.unity` | 158 | 10 | 1 | **169** | **0** | **0** |
| `OwnedTown_IronBastion.unity` | 210 | 10 | 1 | **221** | 221 | 221 |
| `IronBastionTemplate.json` | — | — | — | **221 entries** | — | 221 ids |

## 2. What was changed

### 2a. The identity is now the GENERATOR'S OUTPUT, deterministically

`Assets/Editor/WallTools/RaidBaseGenerator.cs`

- New `IdentityRoleWall/Tower/Spire` constants + `BeginIdentityScope` / `EndIdentityScope` /
  `StampIdentity`, with a long block naming the WO-1732 strip and the 371701 proving line.
- `BuildFromConfig` wraps `BuildConfigLayout` in `BeginIdentityScope(configId)` / `finally
  EndIdentityScope()` — a throw mid-build can never leak one config's role indices into the next scene.
- Stamped **at creation**, at the three (and only) sites that add the census components:
  `PlaceSegment` (after `ws.SetTier`), `ArmTower` (after the stats), `PlaceSpire` (after
  `spire.Configure`).
- Id = `<configId>.<role>.<index>` — e.g. `iron_bastion.wall.137`, `iron_bastion.tower.0`,
  `iron_bastion.spire.0`. **`.` not `:` deliberately:** the id is a plain YAML scalar and is also
  embedded in the saved `instanceId` (`owned:capture:<receipt>:<templateId>`), where a `:` would be
  ambiguous in both places.
- **No `Guid.NewGuid()` anywhere on the path.** Outside a scope `StampIdentity` is a no-op, so the legacy
  menu-only `Build()` flagship path invents nothing.
- Known non-invariant, written into the code rather than hidden: `PlaceSegment` returns 0 without
  creating anything when its tier prefab is missing, which would shift later wall indices. A bake with no
  wall art is already broken, and the new regression compares id **sets**, so it cannot ship silently.

### 2b. `RaidBaseGenerator.cs:528-533`'s false comment — corrected, not deleted

The old text (*"THAT SCENE IS OWNER-AUTHORED AND PROTECTED. It is NOT regenerated, NOT re-dressed and NOT
re-laid-out"*) is replaced with the truth: `Assets/Editor/OwnedTownSceneBuilder.cs` **derives**
`OwnedTown_IronBastion.unity` from the raid scene; believing the comment is what let the pair desync; the
owner's ruling (A) is quoted verbatim; and the ONE chain is named. The four refusals in
`ReseatOwnedTownSpire` are explicitly kept — they remain true and were not relaxed.

`BuildAllRaidScenes`' NEXT log line (`:516-519` before, `:597-607` now) now also warns that running it
alone for `iron_bastion` desynchronises the pair, and names the chain.

### 2c. `ReseatOwnedTownSpire` — "already seated" is a MEASURED pass, not a bypass

A town derived from a generated raid scene arrives already correct (`BuildConfigLayout:801` reseats the
raid spire), so the `lift < 0.001` branch would have reported `OWNED_TOWN_SPIRE_RESEAT_FAIL` on a
**correct** scene. New `AlreadySeatedOnPlatform` proves it instead: `TryMeasuredBounds(platform)` →
slab top, spire must be over the footprint and its lowest **rendered** point within 1 cm of that top.
Every other no-op reason (no measurable slab, off the footprint, no renderers) still FAILs, and the scene
is still never re-saved for a no-op. Measured on the run: `base y 1.500 vs KeepPlatform top y 1.500,
delta 0.0000m`.

### 2d. Both hardcoded `221`s are gone — DERIVED, not re-typed to 169

- `Assets/Editor/OwnedTemplateIdentityBake.cs` — **rewritten**. It no longer mints an id: a structure
  without one is a generator defect and it throws naming the chain. Split into
  `StampRaid()` (raid scene; verify + capture poses; marker `OWNED_TEMPLATE_IDS_OK`) and
  `VerifyTown(poses)` (town census count **==** raid count, id **sets** equal, every pose resolves by
  id, then the three original proofs: sibling reorder survives, duplicate id refused, changed coordinate
  frame refused; marker `OWNED_TOWN_IDS_VERIFIED_OK`). `Run()` = both, for the menu. New failure marker
  `OWNED_TEMPLATE_IDS_FAIL`. The old `poses.Count != 221` is replaced by the raid↔town equality.
- `Assets/Editor/OwnedTownManifestBake.cs:57` — `manifest.entries.Count != 221` → `!= property.structures.Count`
  (the census it was built FROM), plus an empty-census refusal. Marker now prints the derived count.
- `Assets/Editor/OwnedTownSceneBuilder.cs` — header added recording that this file is the evidence the
  town is derived, the owner ruling, and "do not call this alone". No count added, deliberately.

### 2e. NEW regression — `Assets/Editor/Regression/OwnedTownTemplateIdentityRegression.cs`

An **asset lint** (no Unity play, no scene load) over the three artifacts as text/JSON:

1. every `WallSegment`/`DefenseTower`/`RaidSpire` in **both** scenes carries a non-empty `_templateId`
   (structure count == stamped-id count, per scene);
2. ids unique within each scene;
3. the two scenes' id **SETS** are equal;
4. `IronBastionTemplate.json`'s `templateStructureId` set equals them and its entry count equals the
   scene structure count.

Every count is derived from the artifact; there is **no literal census in the file**. Script GUIDs are
resolved through `AssetDatabase`/`MonoScript` at run time, not pasted hex, so a script move cannot make
the lint count zero and pass. Its failure sentences name
`DeNelle.Editor.OwnedTownChain.RebuildFromRaid`. Public surface:
`Run(out string reason)` (reads disk) and `Check(raidText, townText, manifestText, out string reason)`
(text-in, so the RED leg is proven in memory — **never** by hand-editing a `.unity` file, CLAUDE.md §3).

**⚠ A REAL BUG I FOUND IN MY OWN LINT, AND THE FIX IS THE INTERESTING PART.** The first version used
`^\s*_templateId:\s*(.*)$` with `RegexOptions.Multiline`. `\s` **matches `\n`**, so on an EMPTY field the
pattern swallowed the newline and `(.*)` captured the *next* YAML line — an unstamped structure read as
stamped and the lint passed the exact defect it exists to catch. Measured against the planted mutation:
**0 blanks detected out of 1 planted.** Both the lint and the chain's mutator now use `[^\S\r\n]` /
`[^\r\n]`; re-measured: `mutated -> blank 1, nonblank 168`. This is why the red leg is run for real
rather than assumed.

### 2f. NEW batchmode chain — `Assets/Editor/WallTools/OwnedTownChain.cs`

`DeNelle.Editor.OwnedTownChain.RebuildFromRaid` (menu: *Defenders/Walls/Rebuild Owned Town From Raid
(WO-1767)*):

```
0. OwnedTownTemplateIdentityRegression.Run          -> expected RED before the rebuild
1. RaidBaseGenerator.BuildSceneFor("iron_bastion")  -> ids emitted at creation
2. OwnedTemplateIdentityBake.StampRaid()            -> OWNED_TEMPLATE_IDS_OK
3. OwnedTownSceneBuilder.Build()                    -> OWNED_TOWN_SCENE_OK   (town DERIVED from it)
4. OwnedTemplateIdentityBake.VerifyTown(poses)      -> OWNED_TOWN_IDS_VERIFIED_OK
5. OwnedTownManifestBake.Run()                      -> OWNED_TOWN_MANIFEST_OK
6. RaidBaseGenerator.ReseatOwnedTownSpire()         -> OWNED_TOWN_SPIRE_RESEAT_OK
7. RaidNavBake.BakeAll()                            -> RAID_NAV_REACH_OK + RAID_NAV_BAKE_OK
8. lint from disk (must be GREEN) + a blanked-id mutation in memory (must be RED)
```

It subscribes to `Application.logMessageReceived` for the duration and **withholds
`OWNED_TOWN_CHAIN_OK`** if any required step marker is absent or any failure marker
(`OWNED_TEMPLATE_IDS_FAIL`, `OWNED_TOWN_SPIRE_RESEAT_FAIL`, `RAID_NAV_REACH_FAIL`) appeared — because
several of those steps report failure with `Debug.LogError` and do **not** throw, so the exit code proves
nothing (CLAUDE.md §8).

**⚠ TWO DEVIATIONS FROM THE WO, BOTH DELIBERATE, RECORDED PER CLAUDE.md §11B.B:**

1. **Step order.** §6b put `OwnedTemplateIdentityBake` **before** `OwnedTownSceneBuilder.Build`. That
   order **cannot run**: the bake's town phase legacy-resolves every raid pose against the *existing*
   town scene, which carried the pre-WO-1723 210-wall partition, so it throws before the town is
   re-derived. Hence the `StampRaid` / `VerifyTown` split, with the verification after the derive.
2. **Step 1 is `BuildSceneFor("iron_bastion")`, not `BuildAllRaidScenes`** (as the brief specified), so
   the three lower tiers' scene files are not rewritten. `RaidNavBake.BakeAll` still covers all five
   scenes — that is its own hardcoded list (`RaidNavBake.cs:33-40`) and it re-saved the other three; see
   §5 for exactly which files changed.

### 2g. Assembly fix (measured, not guessed)

Run 1 failed to compile: `Assets\Editor\OwnedTownChain.cs(108,17): error CS0103: The name
'RaidBaseGenerator' does not exist in the current context`. `RaidBaseGenerator` lives in
**`DeNelle.EditorWallTools`**, which *references* `DeNelle.Editor` — so a chain in `DeNelle.Editor`
cannot see it and the reverse reference would be a cycle. The chain therefore lives in
`Assets/Editor/WallTools/`, and `DeNelle.EditorWallTools.asmdef` gained **one** reference,
`DeNelle.EditorRegression` (no cycle: that assembly references only runtime + package assemblies).
The namespace is unchanged (`DeNelle.Editor`), so the batchmode entry-point string is unaffected.

---

## 3. DataRegression registration — FOR THE LEAD (this lane did not touch `DataRegression.cs`)

Add one line to `DataRegression.RunAll`'s suite list, alongside the other `[…]` entries:

```csharp
if (!DeNelle.Editor.Regression.OwnedTownTemplateIdentityRegression.Run(out var ownedTownIdentityReason)) failures.Add(ownedTownIdentityReason); else log.AppendLine("[owned-town-template-identity] " + ownedTownIdentityReason);
```

It has no play-mode or scene-load cost (three file reads + regex/JSON), so it is safe anywhere in the list.
`DataRegression` lives in `DeNelle.EditorRegression`, the same assembly as the new suite — no asmdef change
needed for this line.

---

## 4. Markers — judged on FRESH logs, never the exit code

### 4.0 THREE LAUNCHES, NAMED ONCE. The brief said run the chain ONCE; I ran it three times, deliberately.

| Launch | Log | Outcome | Why |
|---|---|---|---|
| **run 1** | overwritten (was `Builds/owned-town-chain.log`) | **compile FAIL**, no bake | the assembly-boundary error in §2g. No scene was written. |
| **run 2** | `Builds/owned-town-chain.log` → copied to **`logs/debug/wo1767-chain-run2-redbefore.log`** | `OWNED_TOWN_CHAIN_OK` | the first real bake. **The ONLY place the BEFORE-RED line exists**, because run 2 itself repaired the tree. |
| **run 3** | `Builds/owned-town-chain2.log` → copied to **`logs/debug/wo1767-chain-run3-final.log`** | `OWNED_TOWN_CHAIN_OK` | the corrected red-proof mutator + the determinism comparison. **Run 3's output is what is on disk** — the SHA-256s in §4b prove run 2's bytes are gone. |

Both logs were copied out of `Builds/` on purpose: the next batchmode run in this repo can clobber that
directory, and the BEFORE-RED proof only exists in run 2's log.

### Chain run 2 — `Builds/owned-town-chain.log`, 296 819 bytes, mtime 2026-09-16T14:57:21

```
[OwnedTownChain] BEFORE lint: RED - [owned-town-template-identity] FAIL RaidBase_IronBastion has 169
    census structure(s) but 0 stamped identity/identities. …
OWNED_TEMPLATE_IDS_OK structures=169 (DERIVED from Assets/Scenes/RaidBase_IronBastion.unity, never a literal); …
OWNED_TOWN_SCENE_OK: separate template saved, garrison removed, towers friendly; source raid not saved
OWNED_TOWN_IDS_VERIFIED_OK structures=169; raid and town id SETS equal and census sizes equal (both DERIVED); …
OWNED_TOWN_MANIFEST_OK structures=169 (DERIVED from the raid census, never a literal); …
OWNED_TOWN_SPIRE_RESEAT_OK … spire y 1.52 lift=0.00m ALREADY SEATED (MEASURED: base y 1.500 vs
    KeepPlatform top y 1.500, delta 0.0000m)
RAID_NAV_REACH_OK  scenes=5; courtyard, ramp foot and platform top all reach the spire
RAID_NAV_BAKE_OK   scenes=5; wall and tower footprints use runtime carving
[OwnedTownChain] AFTER lint: GREEN - … 169 census structure(s) in both scenes, all stamped, ids unique,
    the three id SETS equal, manifest entries=169 — every count DERIVED from the artifact
OWNED_TOWN_CHAIN_OK config=iron_bastion; …
[run] VERDICT=PASS marker='OWNED_TOWN_CHAIN_OK' FOUND … errorCS=0 underAssets=0 elsewhere=0
```

### RED-then-GREEN proof of the new regression (CLAUDE.md §11B.A)

| Leg | How | Result |
|---|---|---|
| **RED, before the bake** | the chain's step 0 lint over the tree as WO-1732 left it | **FAIL** — *"RaidBase_IronBastion has 169 census structure(s) but 0 stamped identity/identities"* |
| **GREEN, after the bake** | step 8a, re-read off disk | **PASS** — 169/169 in both scenes, sets equal, manifest 169 |
| **RED, on a removed id** | step 8b — one `_templateId` value blanked **in memory** (no `.unity` hand edit) | **FAIL** (see run-3 line in §4b) |

### 4b. Chain run 3 — corrected mutator + DETERMINISM proof

`Builds/owned-town-chain2.log`, 251 305 bytes, mtime `2026-09-16T14:59:44`,
`[run] VERDICT=PASS marker='OWNED_TOWN_CHAIN_OK' FOUND … errorCS=0 underAssets=0 elsewhere=0`.

Why it was run a second time, stated plainly: **the first run's RED leg failed for the wrong reason.**
The mutator's own `\s*$` tail also spanned the newline, so the blanked line merged with the next one and
the lint refused it as a *set difference* (`'--- !u!114 &31189984' is in the first set only`) rather than
as a blank field. That exposed the identical `\s` bug in the lint itself (§2e). Both were fixed and the
chain re-run:

```
[OwnedTownChain] BEFORE lint: GREEN   (the tree was already repaired by run 2 - expected)
OWNED_TEMPLATE_IDS_OK structures=169 … OWNED_TOWN_SCENE_OK … OWNED_TOWN_IDS_VERIFIED_OK structures=169
OWNED_TOWN_MANIFEST_OK structures=169 … OWNED_TOWN_SPIRE_RESEAT_OK … delta 0.0000m
RAID_NAV_REACH_OK scenes=5 … RAID_NAV_BAKE_OK scenes=5
[OwnedTownChain] AFTER lint: GREEN - 169 census structure(s) in both scenes, …
[OwnedTownChain] RED proof: one blanked _templateId is refused -
    [owned-town-template-identity] FAIL RaidBase_IronBastion carries 1 EMPTY _templateId field(s).
OWNED_TOWN_CHAIN_OK config=iron_bastion; …
```

**AC6 — determinism, measured across the two independent bakes:**

| Compared | Result |
|---|---|
| the 169 `_templateId` values in `RaidBase_IronBastion.unity`, run 2 vs run 3 (`sort` + `diff -q`) | **IDENTICAL** |
| `OwnedTown_IronBastion.unity`'s id set vs that same set | **EQUAL** |
| id format | `iron_bastion.wall.0 … .137`, `iron_bastion.tower.0 … .9`, `iron_bastion.spire.0` |

⚠ **The scene/manifest BYTES are not identical between runs, and that is expected, not a determinism
failure.** Unity mints fresh `fileID`s when it writes a regenerated scene, and
`RaidCaptureCensus.cs:20` mints a fresh `captureReceiptId` per bake, which appears in all 169
`instanceId` values in the manifest (`owned:capture:1a6560377c814d98a2e7a8a0eb4a6dcf:<templateId>` on the
shipped file). **The thing AC6 asks about — the `_templateId` values — is byte-stable.** Corollary the
lead should know: every re-bake produces a genuinely different scene/manifest diff even when the layout
is unchanged, so do not read a large diff as evidence of a layout change.

---

## 5. Changed paths (every scene/asset change attributable to a chain marker)

`git status --short` scoped to the paths this lane's edits and bake could reach, 2026-09-16 after run 3.
The wider tree is dirty from other lanes; nothing below is theirs.

### Source I wrote (`.cs` / `.asmdef`)

| Path | State |
|---|---|
| `Assets/Editor/WallTools/RaidBaseGenerator.cs` | ` M` — deterministic emit, the corrected `:528-533` comment, the NEXT-line warning, `AlreadySeatedOnPlatform` |
| `Assets/Editor/WallTools/DeNelle.EditorWallTools.asmdef` | ` M` — **one** reference added: `DeNelle.EditorRegression` |
| `Assets/Editor/WallTools/OwnedTownChain.cs` (+ `.meta`) | `??` **NEW** — the chain |
| `Assets/Editor/Regression/OwnedTownTemplateIdentityRegression.cs` (+ `.meta`) | `??` **NEW** — the lint |
| `Assets/Editor/OwnedTemplateIdentityBake.cs` (+ `.meta`) | `??` — rewritten in place (was already untracked, WO §7) |
| `Assets/Editor/OwnedTownManifestBake.cs` (+ `.meta`) | `??` — derived count (already untracked) |
| `Assets/Editor/OwnedTownSceneBuilder.cs` (+ `.meta`) | `??` — header only (already untracked) |

### Bake output (the deliverable — every write attributable to a chain step, no hand edit)

| Path | State | Produced by |
|---|---|---|
| `Assets/Scenes/RaidBase_IronBastion.unity` | ` M` | step 1 `BuildSceneFor` (+ step 7 nav save) |
| `Assets/Scenes/OwnedTown_IronBastion.unity` | ` M` | step 3 `OwnedTownSceneBuilder.Build` (+ step 7) |
| `Assets/Scenes/OwnedTown_IronBastion/NavMesh.asset` | ` M` | step 7 `RaidNavBake.BakeAll`, mtime `14:59:40` |
| `Assets/Resources/OwnedTown/IronBastionTemplate.json` | `??` (dir untracked, WO §7) | step 5 `OwnedTownManifestBake.Run`, mtime `14:59:27` |

`Assets/Scenes/RaidBase_IronBastion/NavMesh.asset` was **re-written** (mtime `14:59:37`) but is
**byte-identical to HEAD** — the raid geometry did not change vs. WO-1732's committed scene, only its
identities did. Likewise the three lower-tier scenes (`RaidBase_raider_camp_small`,
`RaidBase_fortified_garrison`, `RaidBase_mage_enclave`) and their `NavMesh.asset`s were re-saved by the
nav bake and came out **byte-identical to HEAD** — they are *not* in the diff.

### ⚠ ONE SIDE EFFECT IN MY DIFF THAT IS NOT MINE BY INTENT — for the lead to accept or revert

| Path | State | Diff |
|---|---|---|
| `Assets/Generated/RaidGround/RaidBase_IronBastion_KeepPlatform.mat` | ` M` | 1 line |
| `…/RaidBase_IronBastion_KeepRamp.mat` | ` M` | 1 line |
| `…/RaidBase_fortified_garrison_KeepPlatform.mat` | ` M` | 1 line |
| `…/RaidBase_fortified_garrison_KeepRamp.mat` | ` M` | 1 line |
| `…/RaidBase_mage_enclave_KeepPlatform.mat` | ` M` | 1 line |
| `…/RaidBase_mage_enclave_KeepRamp.mat` | ` M` | 1 line |

All six are the same single change — a `_MainTex` tiling scale re-derived from the measured ground, e.g.
`m_Scale: {x: 2.97, y: 2.97}` → `{x: 15.894551, y: 15.895157}`. These are **generated** materials under
`Assets/Generated/`, written by the shared per-scene ground/keep material step inside
`RaidNavBake.BakeAll`, whose five-scene list (`RaidNavBake.cs:33-40`) I did **not** change. The two
non-IronBastion pairs therefore changed for scenes whose `.unity` files came out byte-identical.

**What I cannot prove:** whether those six were already dirty before this lane ran. Their mtimes are from
my run (14:59), and they differ from HEAD — but a file can be both already-dirty and re-written, and I did
not snapshot them beforehand. **Recommendation:** they are a pre-existing inconsistency between HEAD's
committed mats and what the current bake derives; either commit them with this fix or `git checkout` them
and expect the next nav bake to re-produce them. **Not a WO-1767 behaviour change either way.**

`ProjectSettings/ProjectSettings.asset` was touched by Unity at `14:59:24` but is **unchanged** vs HEAD.
`ProjectSettings/EditorBuildSettings.asset` is **unchanged** — `OwnedTown_IronBastion` was already
registered, so `OwnedTownSceneBuilder`'s build-settings step was a no-op.
`Assets/Scenes/MainCastle_Hall.unity` shows ` M` in the tree but its mtime is **2026-09-11T18:21:06** —
another lane's pre-existing dirt, untouched by this run.

---

## 6. Brace + NUL gate on every `.cs` touched

```
python tools/gate_brace.py <each file>   ->  GATE_BRACE_SUMMARY bad=0   (exit 0)
NUL scan (\x00 byte count)               ->  0 for every file
```

| File | braces | NULs |
|---|---|---|
| `Assets/Editor/WallTools/RaidBaseGenerator.cs` | bad=0 | 0 |
| `Assets/Editor/WallTools/OwnedTownChain.cs` (new) | bad=0 | 0 |
| `Assets/Editor/OwnedTemplateIdentityBake.cs` | bad=0 | 0 |
| `Assets/Editor/OwnedTownManifestBake.cs` | bad=0 | 0 |
| `Assets/Editor/OwnedTownSceneBuilder.cs` | bad=0 | 0 |
| `Assets/Editor/Regression/OwnedTownTemplateIdentityRegression.cs` (new) | bad=0 | 0 |

`Assets/Editor/WallTools/DeNelle.EditorWallTools.asmdef` — JSON, one reference added.

---

## 7. Acceptance criteria

| # | Criterion | Verdict |
|---|---|---|
| 1 | `_templateId` count == structure count in both scenes | **PASS** — 169 == 158+10+1 in both |
| 2 | the two scenes' id sets equal, and equal to the manifest's | **PASS** — `diff` EQUAL; manifest 169 ids |
| 3 | the regression FAILS when an id is removed (red proven) | **PASS** — see §4/§4b |
| 4 | `OWNED_TEMPLATE_IDS_OK`, `OWNED_TOWN_SCENE_OK`, `OWNED_TOWN_MANIFEST_OK`, `OWNED_TOWN_SPIRE_RESEAT_OK`, `RAID_NAV_BAKE_OK` | **PASS** (plus `OWNED_TOWN_IDS_VERIFIED_OK`, `RAID_NAV_REACH_OK`, `OWNED_TOWN_CHAIN_OK`). `COMPILE_GATE_OK` + `REGRESSION_OK` = **LEAD** |
| 5 | a run of Iron Bastion logs no `Precombat capture census failed`; a 3-star settle logs `OWNED_TOWN_CAPTURED` | **NOT PROVEN — needs a device 3-star clear** (§8) |
| 6 | re-running the bake leaves every `_templateId` unchanged | **PASS** — see §4b |
| 7 | no hand edit of any `.unity` in the diff | **PASS** — every scene write came from a chain step; `git diff` on the scenes is the chain's output only |

---

## 8. What is NOT proven, and what still has to happen

1. **AC5 needs the owner's device.** Nothing here runs a raid to a 3-star settle. What is proven is that
   the census's precondition now holds for all 169 structures, that the manifest census matches (so
   `OwnedTownLayoutSnapshot.Validate:41-43`'s `structures.Count` check — read at source this session —
   can pass), and that the spire is reachable. The remaining risk is downstream of the census and
   **cannot** be closed from this machine. **Close condition:** a device Iron Bastion run that logs no
   `Precombat capture census failed`, then a 3-star settle logging `OWNED_TOWN_CAPTURED`
   (`RaidVictoryController.cs:1031`) with the end-state primary button entering the town.
2. **The town's LOOK changed, by the owner's ruling.** It is now the WO-1723 4.0 m partition:
   **210 walls → 158 live walls + 158 ruin holders**, census 221 → **169**. That is ruling (A)'s
   explicit consequence, not a defect — but the owner has not yet *seen* it.
3. **⛔ `RaidCaptureCensus.templateVersion` is still the literal `"iron-bastion-20260911"`**
   (`RaidCaptureCensus.cs:59`), and `OwnedTownTemplateManifest.Version` must equal it. The building it
   describes has changed twice since that date. The version was deliberately **left alone**: the file is
   outside this lane's scope, and bumping it would invalidate any saved town. The owner's own ruling
   makes the churn free *today* — the census only runs when `GameStateService.State.OwnedBase == null`
   (`RaidVictoryController.cs:124`), i.e. no captured town exists in any save. **That window closes the
   day one player captures.** Flagged for the lead, not fixed here.
4. **§7's BLOCKER-CLASS finding still stands and is the lead's call.** The whole WO-1705 layer is
   untracked — `OwnedTemplateIdentity.cs`, `RaidCaptureCensus.cs`, the three bakes, and now
   `IronBastionTemplate.json` and my two new files. `OwnedTown_IronBastion.unity` references script GUID
   `a1e512946c94bcd40ae870198aa00b4e` 169 times and **neither that script nor its `.meta` is in git**, so
   a fresh clone gets 169 missing-script components. The 371701 device build shipped uncommitted census
   code. Commit the layer by explicit path with this fix.
5. **Whether the owner had ever hand-edited `OwnedTown_IronBastion.unity`** (WO §11's first open claim)
   was not resolved by this lane and is now moot for the artifact: the ruling re-derived it. If any hand
   edit existed there, it is gone — the owner chose (A) knowing the town is derived.
6. **I did not run the compile gate or `DataRegression`**, per the brief. `errorCS=0 underAssets=0` on
   the chain's own fresh log is evidence the tree compiles, **not** a `COMPILE_GATE_OK`.
7. **⛔ TWO ARTIFACTS OUTSIDE MY FILE SCOPE NOW CARRY THE OLD 221-STRUCTURE ASSUMPTION. Neither is
   fixed here; both were found by grepping for the pattern rather than assumed absent.**
   > **⚠ ITEM (b) BELOW IS SUPERSEDED — see §9, the addendum.** The lead ruled that ruling (A) covers
   > the practice arena; it is now rebuilt by the chain and linted, and its counts are in §9. Item (a)
   > (`OwnedTownMovePlayProof.cs:200`) still stands OPEN. The (b) paragraph is left exactly as written
   > because it is the evidence that produced the ruling.

   **(a) `Assets/Editor/OwnedTownMovePlayProof.cs:200` — a hardcoded `221`:**
   ```csharp
   Require(manifest != null && manifest.entries.Count == 221, "Shipped town manifest is missing.");
   ```
   The manifest is now **169**, so this proof will throw. **It does NOT affect the lead's gate:**
   `grep -rn 'OwnedTownMovePlayProof' Assets --include=*.cs` returns only three *comment* references
   (`KnightGearProofCapture.cs:117`, `RaidBreachTapLiveProof.cs:27`, `RaidWallTierProof.cs:21`) and it is
   **not** registered in `DataRegression.RunAll` — the two owned-town suites that ARE registered are
   `OwnedTownRepairPaymentProof` (`:389`) and `OwnedTownConstructionRulesProof` (`:392`), neither of which
   carries a census literal. It is a standalone play-proof entry point. Fix = the same derived treatment
   (`== OwnedTownTemplateManifest.Load().entries.Count` is circular; compare against the scene census, as
   `OwnedTemplateIdentityBake` now does). Untracked file, one line.

   **(b) `Assets/Scenes/ArenaPractice_IronBastion.unity` is TRACKED and now STALE — the more serious one.**
   Measured 2026-09-16 after the bake: **221** `OwnedTemplateIdentity` refs, **221** `_templateId`, **210**
   `WallSegment`, and its ids are the OLD 32-hex GUID format (`0118bfbe25dc466d88c54c1e14e8ae9f`, …), not
   `iron_bastion.wall.N`. `OwnedTownScenePose.TryResolve:59` explicitly accepts poses whose `sourceScene`
   is `RaidBase_IronBastion` inside `PracticeCombatPolicy.SceneName` (`= "ArenaPractice_IronBastion"`), so
   **a captured town's structures can no longer resolve in the practice arena.**
   `Assets/Editor/OwnedTownPracticeSceneBuilder.cs:16` derives that scene FROM
   `OwnedTown_IronBastion.unity`, so the repair is one insertion — `OwnedTownPracticeSceneBuilder.Build()`
   between the chain's step 6 and step 7 (it must run before the nav bake so the practice scene gets its
   own navmesh; note `RaidNavBake.cs:33-40`'s five-scene list does **not** include it today, which is a
   second question).
   **I did NOT add it**: the brief named the deliverable as "the two scenes + manifest + NavMesh assets",
   the WO's chain has no practice step, and widening a scene-writing chain past its spec without a ruling
   is the §11B.B deviation I am not allowed to make silently. **This needs the lead's word, and it is the
   one item I would raise before a device test of practice mode.**

8. **The three lower raid tiers were re-saved by the nav bake** (`RaidNavBake.BakeAll`'s own five-scene
   list), not by a layout change. Their geometry was not regenerated by this lane — see §5 for whether
   their files actually differ.

---

## 9. ADDENDUM — 2026-09-16, LEAD RULING: THE PRACTICE ARENA RE-DERIVES WITH THE TOWN

The lead ruled on §8 item 7(b): owner ruling **(A)** covers `ArenaPractice_IronBastion.unity` too,
because `Assets/Editor/OwnedTownPracticeSceneBuilder.cs:16` derives it **from the owned town**. Done, and
re-verified by a fourth chain run.

### 9a. What changed

- **`Assets/Editor/WallTools/OwnedTownChain.cs`** — new step: `OwnedTownPracticeSceneBuilder.Build()`,
  marker `OWNED_TOWN_PRACTICE_SCENE_OK` added to `RequiredMarkers` (so its absence withholds
  `OWNED_TOWN_CHAIN_OK`). The mutation proof now passes the practice text through as well.
- **`Assets/Editor/Regression/OwnedTownTemplateIdentityRegression.cs`** — the practice scene is a third
  linted scene. New public `PracticeScenePath`; `Check` takes a fourth text argument. Two new derived
  assertions: `practiceStructures == townStructures`, and `SetsEqual(townIds, practiceIds)`. The pass
  sentence now reads *"169 census structure(s) in all three scenes (raid, owned town, practice arena) …
  the four id SETS equal"*. `Run(out string reason)` is **unchanged**, so the lead's registration line
  already in `DataRegression.cs:2097` needs no edit.

### 9b. ⚠ ONE-STEP DEVIATION FROM THE LEAD'S PLACEMENT, WITH THE MEASUREMENT THAT FORCED IT

The brief said *"between the manifest/reseat step and `RaidNavBake`"*. **It runs after `RaidNavBake`
instead.** Read off the artifacts before touching anything:

| Fact | Evidence |
|---|---|
| the practice scene **shares** the town's baked navmesh | `ArenaPractice_IronBastion.unity:121` `m_NavMeshData: {… guid: 819cc32d36ca7ed42af4986002cb0e8d …}`, and `Assets/Scenes/OwnedTown_IronBastion/NavMesh.asset.meta:2` `guid: 819cc32d36ca7ed42af4986002cb0e8d` — the same asset. There is no `Assets/Scenes/ArenaPractice_IronBastion/` folder at all. |
| it carries the `RaidGround` object | `m_Name: RaidGround` × 1 — and only `RaidNavBake.EnsureGround` ever creates that |

Both are only possible for a Save-As taken **after** the town was nav-baked — which is also why
`RaidNavBake.cs:33-40`'s five-scene list does not, and need not, name it. Derived *before* the bake, the
practice arena would have had **no ground and no navmesh**. Named here rather than done silently
(CLAUDE.md §11B.B). Verified after the run: the shared guid and the single `RaidGround` are both intact.

### 9c. Practice-arena counts

| | WallSegment | DefenseTower | RaidSpire | census | `OwnedTemplateIdentity` | `_templateId` | unique |
|---|---|---|---|---|---|---|---|
| **before** (stale, 09-11 file) | **210** | 10 | 1 | **221** | **221** | 221 | — |
| **after** | **158** | 10 | 1 | **169** | **169** | **169** | **169** |

`diff` of the sorted practice id set vs the town's: **EQUAL**. Ids are the new format
(`iron_bastion.spire.0`, `iron_bastion.tower.0`, …), not the old 32-hex GUIDs.
`OwnedTownPracticeController` present ×1 (the controller swap `OwnedTownPracticeSceneBuilder` performs).
SHA-256 `5c1101a2…` → `d6e096c3…`. All four artifacts now read **169**.

### 9d. Chain run 4 — `Builds/owned-town-chain3.log`, 551 670 bytes, mtime `2026-09-16T15:10:57`

Copied to **`logs/debug/wo1767-chain-run4-practice.log`**.
`[run] VERDICT=PASS marker='OWNED_TOWN_CHAIN_OK' FOUND … errorCS=0 underAssets=0 elsewhere=0`

```
[OwnedTownChain] BEFORE lint: RED - FAIL the practice arena and the owned town describe DIFFERENT
    buildings: ArenaPractice_IronBastion holds 221 census structure(s), OwnedTown_IronBastion holds 169.
    The practice arena is DERIVED from the town (OwnedTownPracticeSceneBuilder.cs:16) - re-derive it with …
OWNED_TEMPLATE_IDS_OK structures=169 … OWNED_TOWN_SCENE_OK … OWNED_TOWN_IDS_VERIFIED_OK structures=169
OWNED_TOWN_MANIFEST_OK structures=169 … OWNED_TOWN_SPIRE_RESEAT_OK … delta 0.0000m
RAID_NAV_REACH_OK scenes=5 … RAID_NAV_BAKE_OK scenes=5
OWNED_TOWN_PRACTICE_SCENE_OK isolated template copy; original town and raid not saved
[OwnedTownChain] AFTER lint: GREEN - 169 census structure(s) in all three scenes …, the four id SETS equal
[OwnedTownChain] RED proof: … FAIL RaidBase_IronBastion carries 1 EMPTY _templateId field(s).
OWNED_TOWN_CHAIN_OK config=iron_bastion; …
```

**A third, independent RED leg landed for free** and it is the best one in this ticket: the BEFORE lint
refused the tree **naming the practice-arena desync in its own sentence**, before the step that fixes it
ran. That is the new assertion failing on the real defect, not on a planted mutation.

**Determinism held again:** the 169 `_templateId` values in `RaidBase_IronBastion.unity` are
**IDENTICAL** to run 3's (`sort` + `diff -q`) — three independent bakes, one id set.

### 9e. Changed paths after run 4 (delta vs §5)

**One new entry:** ` M` `Assets/Scenes/ArenaPractice_IronBastion.unity` (tracked; produced by the chain's
practice step — no hand edit).

Everything else in §5 is unchanged, and re-confirmed after run 4:
` M` `Assets/Scenes/RaidBase_IronBastion.unity`, ` M` `Assets/Scenes/OwnedTown_IronBastion.unity`,
` M` `Assets/Scenes/OwnedTown_IronBastion/NavMesh.asset`, `??` `Assets/Resources/OwnedTown/…json`,
` M` `Assets/Editor/WallTools/RaidBaseGenerator.cs`, ` M` `…/DeNelle.EditorWallTools.asmdef`,
`??` `…/OwnedTownChain.cs`(+meta), `??` `Assets/Editor/Regression/OwnedTownTemplateIdentityRegression.cs`,
`??` the three already-untracked bakes. `Assets/Scenes/RaidBase_IronBastion/NavMesh.asset` is **again**
byte-identical to HEAD. No new `Assets/Scenes/ArenaPractice_IronBastion/` folder was created (the shared
navmesh, §9b). `ProjectSettings/*` unchanged.

**Per the lead's instruction the six `Assets/Generated/RaidGround/*_Keep{Platform,Ramp}.mat` side effects
are left exactly as they are** — the lead is committing them with this lane.
`Assets/Editor/OwnedTownPracticeSceneBuilder.cs` shows `??` but I did **not** edit it; it was already
untracked (WO §7's layer).

### 9f. Brace + NUL after the addendum edits

```
python tools/gate_brace.py Assets/Editor/WallTools/OwnedTownChain.cs \
                           Assets/Editor/Regression/OwnedTownTemplateIdentityRegression.cs
  -> GATE_BRACE_SUMMARY bad=0 of 2   (exit 0)
NUL byte count -> 0 and 0
```
No other `.cs` was touched for this addendum. `RaidBaseGenerator.cs` re-checked after its header edit:
`bad=0`, NULs 0.

### 9g. Still open after the addendum

- `Assets/Editor/OwnedTownMovePlayProof.cs:200`'s hardcoded `entries.Count == 221` (§8 item 7a) — **still
  open**, still not registered in `DataRegression`, still out of this lane's file scope.
- `RaidCaptureCensus.cs:59`'s literal `templateVersion` (§8 item 4) — unchanged.
- **AC5 still needs a device 3-star clear** (§8 item 1). The practice arena's repair *widens* what a
  device pass should now cover: practice mode should be exercised too, since it was silently broken
  before this addendum and nothing here proves it plays.
- `RaidNavBake.cs:33-40` does not list `ArenaPractice_IronBastion.unity`. That is correct today because
  the scene shares the town's navmesh — **but it is a coupling nobody wrote down**, and if the practice
  scene ever stops being a Save-As of a nav-baked town it will silently have no navmesh. Flagged, not
  changed.

# WO-1732 RESULT — top raid tier never regenerated

**Status:** DONE (edit-only lane — no Unity run, no bake, no git; those are the lead's)
**Date:** 2026-09-15

---

## Files changed

| file | change |
|---|---|
| `Assets/Editor/WallTools/RaidBaseGenerator.cs` | hardcoded `RaidConfigIds` → derived `RaidConfigIdsFromCatalog()`; new `ScenePathFor(def, configId)`; `BuildSceneFor` resolves the config and saves to the authored path, throwing on unknown config / failed save; `BuildAllRaidScenes` throws on an empty derived set and logs the ids it baked; `BuildFinalRaidScene`'s hardcoded `"Assets/Scenes/RaidBase_IronBastion.unity"` literal replaced with the resolver |
| `Assets/Editor/Regression/RaidSceneCoverageRegression.cs` | **NEW** — the guard |
| `Assets/Editor/Regression/DataRegression.cs` | registration (one `Guard.Try` line + comment) |
| `WorkOrders/WORK_ORDER_1732_*.md` | the WO, Status DONE |
| `CLI_LANES_WO_NUMBERS.md` | minted 1732, bumped 1732 → 1733 in the same edit |

⚠ `Assets/Editor/Regression/RaidSceneCoverageRegression.cs.meta` does not exist — Unity will generate
it on next import. The lead must stage the generated `.meta` with the file.

---

## The id → scene-path resolution, proven at source

`Assets/_Modules/Village/Hero/RaidDeployVM.cs:443` — `DeNelle.Core.SceneRouter.GoRaid(_def.sceneName);`

The scene the game loads is the config's **authored `sceneName`** field
(`SceneConfigCatalog.cs:155`), not a name composed from the id. For `iron_bastion` that value is
`RaidBase_IronBastion`, matching `SceneRouter.cs:195` and the **enabled** entry at
`ProjectSettings/EditorBuildSettings.asset:53-54`.

**I chose derivation over an explicit id→path mapping**, against the letter of the brief but for its
stated intent ("so the mismatch can never silently recreate itself"). A hand-written map in the
generator would be a *second copy* of `sceneName` — the same duplicated-state failure CLAUDE.md
§2/§5/§8 each describe, and the brief itself warns the mismatch must not be able to recreate itself.
Reading the field makes it impossible by construction. The predicate is the twin of
`RaidVictoryController.cs:501`, and the comments point each at the other.

### Measured, so the lead is not taking it on faith

- Root-object count is **1 in all four** scenes (`grep -c "m_Name: RaidBase_"`). `BuildSceneFor`'s
  wrapper root never survives: `BuildFromConfig`'s `GameObject.Find(rootName)` matches the wrapper
  itself and destroys it, after which `parentRoot != null` is false (Unity's destroyed-object
  operator) and `SetParent` is skipped. **So routing IronBastion through `BuildSceneFor` does not
  change its hierarchy** versus today's `BuildFinalRaidScene` output. Measured, not assumed.
- `RaidSelectionScreen.cs:452`'s comment claiming IronBastion is "registered DISABLED" is **stale** —
  the asset reads `enabled: 1`. Not edited (other lane); flagged.
- `RaidNavBake.cs:347` resolving a def by stripping `RaidBase_` would miss `iron_bastion`, but the
  line above it (`FindBySceneName`) matches exactly, so **nav bake has no latent bug here**. Checked
  because the casing mismatch made it a plausible second victim; it is not one.

---

## Other configs — deliberate, not a latent bug

`village2_enemy_outpost` → `Village2`; `player_outpost` → `PlayerOutpost`. Neither `sceneName`
names a `RaidBase_*` scene, so the derived predicate excludes both. `Village2` is hand-built with
its own `Village2RaidController` (`:97` matches it by name), and this generator only ever writes
`RaidBase_*` levels. **Correctly absent. Do not add them.**

---

## The guard

**`RaidSceneCoverageRegression`** — marker `RAID_SCENE_COVERAGE_OK` / `RAID_SCENE_COVERAGE_FAIL`,
tag `[raid-scene-coverage]`.

Registration line, `Assets/Editor/Regression/DataRegression.cs:752`:

```csharp
DeNelle.Core.Diagnostics.Guard.Try("Regression", "raid-scene-coverage suite", () => { if (!DeNelle.Editor.Regression.RaidSceneCoverageRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[raid-scene-coverage] " + r); });
```

### Proving it can fail

I cannot run Unity in this lane, so I ported each case's **exact** predicate to Python and ran it
against today's on-disk bytes.

**Case 3 `[partition]`** — count `m_Name: Ruin_Wall_` first, else `m_Name: Wall_`, per line:

```
raider_camp_small      walls=58   ruins=58   OK
fortified_garrison     walls=118  ruins=118  OK
mage_enclave           walls=158  ruins=158  OK
iron_bastion           walls=210  ruins=0    FAIL

VERDICT: RAID_SCENE_COVERAGE_FAIL :: [partition] iron_bastion: 210 walls / 0 ruins
```

The guard **fails on exactly the stale scene and passes on the three current ones** — it reproduces
the owner's defect from the repository alone, and it is not noisy.

**Cases 1, 2, 4 and 5 pass today**, verified by the same porting method, so the FAIL above is
attributable to the partition alone — the guard is precise, not noisy:

- `[exists]` — all four `.unity` files present.
- `[registered]` — all four are `enabled: 1` entries (parser paired 31 enabled entries correctly).
- `[bake-list]` — `RaidNavBake.cs` names all four, and lists no orphan `RaidBase_*` path.
- `[orphan]` — 4 `RaidBase_*.unity` on disk, 0 unclaimed.

The `Ruin_` prefix is sourced from the builder (`RaidBaseDresser.cs:867`, `"Ruin_" + segName`), not
guessed.

**Case 5 `[orphan]` closes the trap the brief named.** Had the fix merely added `"iron_bastion"` to
the old array while `BuildSceneFor` still composed `RaidBase_{id}`, the bake would have written a
new `RaidBase_iron_bastion.unity` and left the live scene stale — and Cases 1–4 would all have gone
**green**. Case 5 fails on any `RaidBase_*.unity` no config claims.

### One thing I have NOT proven (CLAUDE.md §11B)

**"Expect `REGRESSION_OK` after the regen" is a prediction, not a measurement.** `iron_bastion` has
never once been built by the current WO-1723 Lane B Dresser: it is the largest ring, tier
`ReinforcedSteel`, and its 210 walls at the old 3.0 m partition will repartition to some other count
the clad module decides. If step 4 or 6 throws or fails on **geometry**, triage it as *the first real
build of this config* — not as the guard being wrong. The guard only asserts walls == ruins > 0,
whatever that count turns out to be.

### Case 4 carries a deliberate expiry

`[bake-list]` pins `RaidNavBake.cs` to **hardcoding** its scene list, because it reads source text
for literal paths. If a future seat correctly makes `RaidNavBake` catalog-derived the way
`RaidBaseGenerator` now is, that list will have no literals and Case 4 will fail **on a fix**. The
retirement instruction is written into the file's header so the next seat finds it there.

---

## Brace / NUL gate

```
python tools/gate_brace.py Assets/Editor/WallTools/RaidBaseGenerator.cs \
    Assets/Editor/Regression/RaidSceneCoverageRegression.cs \
    Assets/Editor/Regression/DataRegression.cs
GATE_BRACE_SUMMARY bad=0 of 3      (exit 0)
```

Raw counts (whole-file, CLAUDE.md §1 one-liner) — all balanced, zero NUL bytes:

| file | open | close |
|---|---|---|
| `RaidBaseGenerator.cs` | 368 | 368 |
| `RaidSceneCoverageRegression.cs` | 81 | 81 |
| `DataRegression.cs` | 1219 | 1219 |

---

## What the lead must re-run

1. Compile gate → `COMPILE_GATE_OK`.
2. **`DeNelle.Editor.DataRegression.RunAll` BEFORE regenerating** → expect `REGRESSION_FAIL` naming
   `[raid-scene-coverage]` / `[partition] 'iron_bastion' ... 210 Wall_* ... 0 Ruin_Wall_*`. This is
   the guard failing on the real tree.
3. `DeNelle.Editor.RaidBaseGenerator.BuildAllRaidScenes` → **four** scenes now; confirm the log names
   `iron_bastion` and that **`RaidBase_IronBastion.unity` is rewritten** and no
   `RaidBase_iron_bastion.unity` appears.
4. `DeNelle.Editor.RaidNavBake.BakeAll`.
5. `DataRegression.RunAll` again → `REGRESSION_OK <n>/<n>`, `[raid-scene-coverage]` green.
6. Stage the Unity-generated `RaidSceneCoverageRegression.cs.meta`.
7. Owner felt-test on the top tier.

---

## Follow-up left open

`Assets/Editor/WallTools/RaidWallContinuityRegression.cs:59` hardcodes the same three ids, so
`RAID_WALL_CONTINUITY_OK` has never exercised the top tier's geometry — **third copy of this list**.
Not touched here: it sits in the lane-fenced WallTools assembly beside `RaidBaseDresser.cs`, and
adding `iron_bastion` could fail the lead's gate for geometry reasons an edit-only lane cannot test.
Fix = derive from the catalog, as done in the generator.

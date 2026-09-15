# WORK ORDER 1731 — RESULT

**Completed:** 2026-09-14, edit-only lane. **Both halves landed (4a + 4b).**
⛔ **NOT VERIFIED BY THIS LANE — the lead runs the gate.** No Unity, no bake, no gate, no git was
run here. Every claim below is either a file edit that exists on disk or a measurement taken with
Python over the working tree, and each one says which.

---

## 1. WHAT WAS DONE

### 4b — the guard (the higher-leverage half)

**New suite: `Assets/Editor/Regression/NavMeshReferenceRegression.cs`**
Markers `NAVMESH_REF_OK` / `NAVMESH_REF_FAIL` (grepped `Assets/Editor`, `Assets/_Modules`, `tools/`,
`.claude/` — zero prior hits, so RULE 1 marker-uniqueness is satisfied).

Registration line, added ABOVE the END FENCE in `Assets/Editor/Regression/DataRegression.cs`:

```
DeNelle.Core.Diagnostics.Guard.Try("Regression", "navmesh-reference suite", () => { if (!DeNelle.Editor.Regression.NavMeshReferenceRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[navmesh-reference] " + r); });
```

`RegressionMarkerRegression.TryGetExpectedSuiteCount` (`:1065-1082`) DERIVES the expected suite count
by parsing `DataRegression.RunAll`'s body — read at source before registering, so adding this line
carries no second number to update.

**What it asserts.** For every scene enabled in `EditorBuildSettings`, every `m_NavMeshData:` reference
with a NON-ZERO fileID and a guid must resolve through `AssetDatabase.GUIDToAssetPath` **and** the
returned path must exist on disk. `{fileID: 0}` is not a failure. A non-zero fileID with no guid is an
in-scene reference with nothing to dangle — counted, not asserted.

**Deliberate superset of the WO's wording.** The WO says "every shipped scene containing a
`NavMeshSurface`". The suite scans every `m_NavMeshData:` token, which also covers the scene's own
legacy `NavMeshSettings` block — what `UnityEditor.AI.NavMeshBuilder.BuildNavMesh()` writes, the path
`CastleWalkable` / `RaidNavBake` / `CastleBuilderTester` use. Both dangle identically. Stated in the
file header so nobody later "fixes" it back down to the narrower rule.

**Two hollow spots closed on purpose:**
- **Zero assigned references across the whole sweep = FAIL**, not a quiet green. If no shipped scene
  carries a single navmesh reference, either the build list or the parse is broken.
- **Binary scenes are a NAMED `RegressionOutcome.PartialSkip`, never a silent pass.** Measured: all ten
  `Assets/Scenes/DungeonCompose/*.unity` start with `\0`, not `%YAML` (`head -c 5 | od -c`), and seven
  of them are `enabled: 1` in `EditorBuildSettings`. A text regex cannot read them, so the suite says
  so by name instead of counting them as covered. `IsYamlText` reads the first five bytes rather than
  guessing from the extension.

### ⛔ PROOF THAT THE GUARD ACTUALLY GOES RED

Two independent mechanisms, because this lane cannot run Unity:

**(a) A self-test inside the suite, which runs BEFORE the sweep and fails `Run` if it misbehaves.**
The detection rule lives in one pure function — `ScanSceneText(label, text, resolve, problems, out
assigned, out local)` — with no Unity and no filesystem dependency. `SelfTest()` drives it over four
string fixtures: a known-bad guid (must be reported, and the report must NAME the guid), a resolving
guid (must stay quiet), `{fileID: 0}` (must stay quiet), and text with no reference (must stay quiet).
If any fixture misbehaves, `Run` returns FALSE **before scanning a single scene** — a sweep by an
unproven detector is a hollow pass with the whole tree inside it. Same reasoning
`RegressionMarkerRegression` RULE 4 gives for `HollowPassFixtures.SelfTest`. This means the guard is
re-proven on **every** gate run, not once by a human.

**(b) Observed red on a SCRATCH COPY, run here with an exact Python port of the same regex and the
same branch rules.** ⛔ No `.unity` was hand-edited — the copy lives in the session scratchpad, outside
`Assets/`, and the real scene was left byte-untouched.

```
fixture dangling    problems=1 expected=1 assigned=1 PASS
      -> [fixture:dangling] line 1: guid 00000000000000000000000000000bad (fileID 23800000) resolves to NO FILE
fixture resolving   problems=0 expected=0 assigned=1 PASS
fixture unassigned  problems=0 expected=0 assigned=0 PASS
fixture none        problems=0 expected=0 assigned=0 PASS

--- SCRATCH COPY (outside Assets, real scene untouched) ---
assigned refs: 1 problems: 1
   RED -> scratch_Main_Castle_Overworld.unity line 16820: guid deadbeefdeadbeefdeadbeefdeadbeef (fileID 23800000) resolves to NO FILE

--- SAME SCAN over the UNMODIFIED real scene ---
assigned refs: 1 problems: 0 (green)
```

A full sweep with the same logic over all 30 text scenes carrying `m_NavMeshData`, resolving guids
against the 71,487 `.meta` files in the tree, returned **0 dangling** — the tree is GREEN today
because the WO §3 mitigation restored the asset. Which is exactly why the self-test, and not the
sweep, is what proves the guard can fail.

### 4a — every `BuildNavMesh()` caller saves, and throws if it cannot

Reference shape copied verbatim in intent from `Assets/Editor/RaidNavBake.cs:109-111`:
`MarkSceneDirty(scene)` then `if (!SaveScene(...)) throw new InvalidOperationException(...)`.
Every message names the scene path and cites WO-1731.

---

## 2. CALL-SITE LEDGER — every `BuildNavMesh()` hit in the tree

### FIXED — counted from the rows below, not asserted above them

**26 save sites across 20 files → 28 `throw` statements** (Village2Playable and Village3Builder each
carry TWO saves, and so two throws). 20 builder files + the new suite + `DataRegression.cs` = the 22
files brace-checked in §3, which is the number that has to reconcile.

| File | Site | What was wrong |
|---|---|---|
| `Assets/Editor/CastleWalkable.cs` | step-7 save after `BakeCastleNavMesh` | save unchecked |
| `Assets/Editor/CastleTroopWallNav.cs` | `BakeAndVerify` save after `BakeSurfaces` | save unchecked |
| `Assets/Editor/CastleWallsFromRecipe.cs` | `ApplyGateDoorJambsAndBake`, `SaveOpenScenes` after `LeanBakeNavMeshSurfaces` | save unchecked |
| `Assets/Editor/CastleHubBuilder.cs` | BATCH-BAKE save | save unchecked |
| `Assets/Editor/CastleHubBuilder.cs` | BATCH-RECIPE save | save unchecked |
| `Assets/Editor/CastleHubBuilder.cs` | REWIRE-REBAKE save | save unchecked |
| `Assets/Editor/CastleHubBuilder.cs` | BRIDGE-SEAM save (after `BakeAllCastleSurfacesAndPersist`) — a **fourth** site the WO's list did not name | save unchecked |
| `Assets/Editor/CastleBuilderTester.cs` | `ProScenePath` save after `BakeWalkable` | `bool saved` captured, only logged |
| `Assets/Editor/CastleBuilderTester.cs` | `OutpostScenePath` save after `BakeWalkable` | same |
| `Assets/Editor/CastleBuilderTester.cs` | `EnemyOutpostScenePath` save after `BakeWalkable` | same |
| `Assets/Editor/CastleBuilderTester.cs` | `DungeonScenePath` save after `BakeDungeonLevel0Walkable` | same |
| `Assets/Editor/DungeonChainBuilder.cs` | `SaveChainScene` after `BakeAndVerify` | `bool saved` only logged |
| `Assets/Editor/WorldMergeBuilder.cs` | merged-bake save — **not on the WO's list, and it is `Main_Castle_Overworld`'s own baker** (writes `Assets/Scenes/Main_Castle_Overworld/`) | save unchecked |
| `Assets/Editor/ProceduralSiegeArenaBuilder.cs` | save after `BakeVenueNavMesh` | save unchecked |
| `Assets/Editor/GarrisonSceneBuilder.cs` | `SaveSceneTo` | `else Err(...)` — logged and carried on |
| `Assets/Editor/EnemyStrongholdBuilder.cs` | `SaveScene` helper | `else Err(...)` — logged and carried on |
| `Assets/Editor/KayKitChallengeOutpostBuilder.cs` | save after `surface.BuildNavMesh()` | save unchecked |
| `Assets/Editor/DungeonComposer.cs` | save after legacy bake | `bool saved` only logged |
| `Assets/Editor/NavMeshBakeFinal.cs` | `SaveOpenScenes` after `BakeOpenScene` | save unchecked (the file's own comments already warn about exactly this class) |
| `Assets/Editor/Village2Playable.cs` | `C_BakeNavMesh` — both the Village2 and OuterWorld saves | both unchecked |
| `Assets/Editor/Village3Builder.cs` | Village3 + OuterWorld saves after the combined bake | both unchecked |
| `Assets/Editor/RoomForge/DungeonBaker.cs` | save after the navmesh bake | `bool saved` only logged |
| `Assets/Editor/Village2MakePlayable.cs` | save after bake | `bool saved` only logged |
| `Assets/Editor/Village2GroundFill.cs` | save after bake | `bool saved` only logged |
| `Assets/Editor/Village2FinalizeApproach.cs` | save after bake | `bool saved` only logged |
| `Assets/Editor/Village2PlaceGateCrossings.cs` | `SaveOpenScenes` after the bake at `:48` | `bool saved` only logged |

**Execution order was verified at every site, not inferred from line numbers.** The one that looked
wrong was `ProceduralSiegeArenaBuilder` (save at `:243`, bake text at `:303`) — read through:
`BakeVenueNavMesh(root)` is CALLED at `:241`, before the save. Order correct, only the check was
missing.

### JUDGED ALREADY SAFE — left alone, with the reason

- **`Assets/_Modules/Village/Arena/ArenaNavMeshBaker.cs:123`** — a RUNTIME component. There is no
  `EditorSceneManager` at play time and no scene to persist; a save would be meaningless. Also outside
  `Assets/Editor/` entirely.
- **`Assets/Editor/Regression/RoomForgeRegression.cs:431`** — bakes onto a throwaway `__rf_navmesh`
  GameObject it created and tracks in `s_spawned` for teardown. Nothing persisted, nothing to save.
- **`Assets/Editor/Village2NavRCA.cs:23`** — read-only RCA; its own comment at that line says "ensure a
  current bake to read". It never writes.
- **`Assets/Editor/Village2IslandMap.cs:22`** — read-only island mapper (header: "READ-ONLY"). It opens
  `Village2.unity` and bakes to sample, without saving — which is safe for a **second, structural**
  reason worth recording: this is the LEGACY baker, and the legacy baker always writes to the
  deterministic path `<SceneFolder>/NavMesh.asset`. The guid does not change, so the scene's reference
  cannot be orphaned. **The rename hazard is specific to the `NavMeshSurface` path**, which names its
  output `NavMesh-<GameObjectName>.asset` — which is exactly how WO-1731 §2's
  `NavMesh-OuterWorld_NavMeshSurface.asset` got its name.
- **`Assets/Editor/TroopTools/CatapultProofCapture.cs:29`** — `NewScene(EmptyScene)`, bakes a proof
  cube, captures a PNG. The scene has no path and is never saved.
- **`Assets/_Sandbox/ProceduralCastleBuilder.cs:266`** — a runtime `MonoBehaviour` in the
  `DeNelle.Sandbox` runtime asmdef, with the bake `#if UNITY_EDITOR`-guarded. No scene handle, no save
  path, not an editor tool.

### NOT TOUCHED — and why, explicitly

- **`Assets/Editor/VillageSceneBuilder.NavMesh.cs:70`** (and its two callers,
  `VillageSceneBuilder.cs:437` and `:524`). Both callers already save AFTER the bake — order correct,
  checks missing, so this is the same defect class. It was left alone for two reasons, and the lead
  should decide: (1) **CLAUDE.md §9 names `VillageSceneBuilder.cs` a serialization bottleneck that
  exactly one agent may touch at a time**, and this is a parallel lane; (2) its target,
  `Assets/Scenes/Village.unity`, **does not exist** — `ls` returned "No such file or directory",
  consistent with CLAUDE.md §7 recording it as deleted. It is a dead path guarded by a lane rule. One
  ticket, two lines, whenever that file is free.

---

## 3. BRACE / NUL GATE

`python tools/gate_brace.py` over all 22 touched `.cs` (the port of the gate's own rule):
**`GATE_BRACE_SUMMARY bad=0 of 22`, exit 0.**

Raw brace count + NUL scan, all 22 files — **balanced, zero NUL bytes**:

```
   34 /    34  Assets/Editor/Regression/NavMeshReferenceRegression.cs
 1218 /  1218  Assets/Editor/Regression/DataRegression.cs
   44 /    44  Assets/Editor/CastleWalkable.cs
  114 /   114  Assets/Editor/CastleTroopWallNav.cs
   62 /    62  Assets/Editor/CastleWallsFromRecipe.cs
  277 /   277  Assets/Editor/CastleHubBuilder.cs
  184 /   184  Assets/Editor/CastleBuilderTester.cs
   79 /    79  Assets/Editor/WorldMergeBuilder.cs
   82 /    82  Assets/Editor/DungeonChainBuilder.cs
   26 /    26  Assets/Editor/ProceduralSiegeArenaBuilder.cs
   27 /    27  Assets/Editor/GarrisonSceneBuilder.cs
  219 /   219  Assets/Editor/EnemyStrongholdBuilder.cs
  177 /   177  Assets/Editor/KayKitChallengeOutpostBuilder.cs
   75 /    75  Assets/Editor/DungeonComposer.cs
   80 /    80  Assets/Editor/NavMeshBakeFinal.cs
  265 /   265  Assets/Editor/Village2Playable.cs
   50 /    50  Assets/Editor/Village3Builder.cs
  351 /   351  Assets/Editor/RoomForge/DungeonBaker.cs
   24 /    24  Assets/Editor/Village2MakePlayable.cs
   14 /    14  Assets/Editor/Village2GroundFill.cs
   21 /    21  Assets/Editor/Village2FinalizeApproach.cs
   78 /    78  Assets/Editor/Village2PlaceGateCrossings.cs
```

⚠ Worth recording, because it is the §1 trap in the other direction: the new suite first read
**33 open / 32 close raw while the GATE read it clean**, because its regex literal carried an escaped
`\{` with no partner. The regex was rewritten to anchor on the closing brace with a two-sided negated
class (`[^{}]*\}`) — a strictly more precise parse that also balances the raw count. The tightened
pattern was re-proven against all four fixtures, the real scene, and the scratch copy after the change.

---

## 4. NOT DONE / OUT OF SCOPE

- **No gate, no bake, no Unity, no git** — the lead owns all four.
- **`NavMeshReferenceRegression.cs.meta` was not fabricated.** Unity generates it on the lead's first
  gate run.
- **WO §5 (Q1, the orphaned `NavMesh-OuterWorld_NavMeshSurface.asset`) is untouched** — it is an open
  owner ruling, not this lane's call. The file was not deleted, moved, or referenced.
- **The §2 culprit is still NOT PROVEN and is not named anywhere in this change.** No commit message,
  comment, or line here asserts which tool ran at 09:53.
- **`Assets/Scenes/Main_Castle_Overworld.unity` was not re-baked and not touched.** WO §7.
- The WO §6 acceptance item "hero reads `ownerAgent=on-mesh` in a captured `[Flow:HeroOwner]` line"
  needs a device or headless run — **this lane could not produce it. Unclosed.**

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>

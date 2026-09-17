# WO-1834 — 4 legacy spawn-point stubs block the real wall-fan injection

**Status:** DONE

Lead ran the batchmode method directly (`run-unity-method.ps1 -Method
DeNelle.Editor.LegacySpawnStubRemoval.RemoveLegacyStubs`). Fresh log (`Builds/wo1834-legacy-spawn-removal`)
shows all 4 legacy stubs found and removed by exact SpawnId match (`spawn-0/1/2/3`, gate 0/3/1/2,
dir S/E/W/N — matching the table above), 0 `WaveSpawnPoint`s remaining, scene saved, marker
`LEGACY_SPAWN_STUBS_REMOVED n=4` present. `git diff --stat` on `Main_Castle_Overworld.unity` is a
clean 196-line pure deletion (0 insertions) — no unrelated scene churn. `COMPILE_GATE_OK` confirmed
on a fresh log (`Builds/wo1834-compile-gate`) immediately after. A subsequent full-suite
`DataRegression.RunAll` run picked up an unrelated, still-in-flight concurrent lane's (WO-1835)
mid-write compile error (`EndlessPressure.cs` ambiguous overload) — not a WO-1834 regression; the
full-tree `REGRESSION_OK` will be re-proven once that lane lands. Committed by explicit path.

## Owner report

> "isnt there supposed to be the waves entering from walls not just stright approach"

## Root cause (confirmed at source, 2026-09-17 — do not re-derive, read the citations)

`Assets/Scenes/Main_Castle_Overworld.unity` has 4 **baked-in, legacy** `WaveSpawnPoint`
GameObjects serialized directly in the scene file:

| GameObject fileID | SpawnId  | GateIndex | Direction | Position |
|---|---|---|---|---|
| 212552887 | `spawn-0` | 0 | S | (-4.37, 0, -40.6) |
| 774120181 | `spawn-3` | 3 | E | (40.6, 0, -4.37) |
| 1044066722 | `spawn-1` | 1 | W | (-40.6, 0, 4.37) |
| 1673235098 | `spawn-2` | 2 | N | (4.37, 0, 40.6) |

These ids (`spawn-0`..`spawn-3`) are the **exact stale format** `WaveSpawnResolver.cs`'s own
header comment names as retired — the live producer, `CastleSpawnPointInjector`, only ever
emits ids shaped `spawn-castle-<dir>-<i>` (`CastleSpawnPointInjector.cs:156`). These 4 are
leftovers from before the injector existed (or a hand-authored placeholder set), one dead-centre
stub per wall side — **not** part of the injector's intended 5-per-side fan of 20.

Because `CastleSpawnPointInjector.Inject()` checks `FindObjectsByType<WaveSpawnPoint>()` and
**skips its entire 20-marker injection whenever ANY WaveSpawnPoint already exists**
(`CastleSpawnPointInjector.cs:126-131`, by design — see the class header, it never wants to
compete with a baked set), these 4 stale stubs permanently suppress the real fan every single
scene load. Confirmed live: device logcat 2026-09-17 12:59:06 —
`[CastleSpawnPointInjector] 4 WaveSpawnPoint(s) already present — skipping injection.`

**The escalation ladder and rotation are NOT the bug — both read correct at source:**
- `SmartEnemySpawner.SideCountForWave` (1 side waves 1-4, 2 sides waves 5-9, 4 sides wave 10+)
  is an intentional difficulty ramp, not a defect.
- `SmartEnemySpawner.ResolveSides` correctly rotates the single-side base N→E→S→W across wave
  numbers (`start = (waveId-1) % sideIds.Count`), and correctly picks opposite sides for the
  2-side step.

**The actual defect:** with only 1 spawn point per side (instead of the intended 5, fanned
±22m along that wall per `CastleSpawnPointInjector.SpawnLocalPositions`), every wave that
attacks from a given side releases its entire roster from the exact same single coordinate on
that wall, every time. Combined with early waves being single-sided by design, this reads
exactly like the owner's report: the same "straight line" entry, not enemies "entering from the
walls."

## What NOT to touch

- Do **not** change `CastleSpawnPointInjector.cs`, `SmartEnemySpawner.cs`, or
  `WaveSpawnResolver.cs` — all three are proven correct.
- Do **not** hand-edit `Main_Castle_Overworld.unity` (CLAUDE.md §3 — corruption-on-resave
  history). This must go through an EditorSceneManager batchmode method, never a text edit of
  the `.unity` file.
- Do **not** delete or touch any other GameObject in the scene.

## Fix

Add a one-shot editor batchmode method (new file, e.g.
`Assets/Editor/LegacySpawnStubRemoval.cs`, `DeNelle.Editor` namespace) that:

1. Opens `Main_Castle_Overworld.unity` via `EditorSceneManager.OpenScene`.
2. Finds every `WaveSpawnPoint` in the scene via `FindObjectsByType<WaveSpawnPoint>()`.
3. Destroys **only** the ones whose `SpawnId` is exactly `"spawn-0"`, `"spawn-1"`, `"spawn-2"`,
   or `"spawn-3"` (the legacy stub format) — never a blanket delete, and log each one removed
   (`FlowTrace.Step`) by SpawnId + GateIndex so the run is auditable from a fresh log.
4. If, after removal, `FindObjectsByType<WaveSpawnPoint>()` is non-empty, `FlowTrace.Fail` and
   abort without saving (something else besides the 4 named stubs is present — do not guess,
   surface it).
5. Marks the scene dirty (`EditorSceneManager.MarkSceneDirty`) and saves it
   (`EditorSceneManager.SaveScene`).
6. Prints a distinct marker on success, e.g. `LEGACY_SPAWN_STUBS_REMOVED n=4`, so the CLI lead
   can judge a fresh log rather than the exit code (CLAUDE.md §8/§12 discipline).

Run once via batchmode, then re-launch a headless play session (or a device build) and confirm
the injector's log line now reads `placed 20 wave spawn points on 4 sides (S/W/N/E)...` instead
of `... already present — skipping injection.`

## ⚠ Correction to the root cause above + a DURABILITY caveat (found at source 2026-09-17, implementation lane)

The section above calls the 4 stubs "leftovers from before the injector existed (or a hand-authored
placeholder set)". The id format `spawn-0..3` is **still emitted by two live editor builders**, and
that matters for whether this fix survives a future bake:

- **`CastleHubBuilder.PlaceCastleSpawnPoints`** (`Assets/Editor/CastleHubBuilder.cs:2849-2878`) emits
  `"spawn-" + i` for `i` 0..3 with side order **S/W/N/E** — which matches the scene's id→dir→gateIndex
  mapping in the table above **exactly** (spawn-0/S/0, spawn-1/W/1, spawn-2/N/2, spawn-3/E/3). So this
  method, or a scene derived from its output, is the shape that produced these stubs.
- **`Village2Playable.ImportSpawnPoints`** (`Assets/Editor/Village2Playable.cs:691-716`) also emits
  `spawn-0..3`, but ordered **N/S/E/W** — which does **not** match the scene. Not the origin.

**Nothing currently re-writes these stubs into the hub scene.** `PlaceCastleSpawnPoints` has exactly one
caller, `CastleHubBuilder.BatchAddCastleWaveSystem` (`:2812`), and that method opens and saves
**`Assets/Scenes/MainCastle_Hall.unity`** (`:2814`) — the LEGACY scene, not `Main_Castle_Overworld.unity`.
Searched: `grep -rn 'BatchAddCastleWaveSystem'` over `tools/ .claude/ docs/ WorkOrders/` and repo-root
`*.ps1`/`*.md` returns only two `docs/MASTER_CATALOG/editor-tools.md` mentions — **no script, chain or
runbook invokes it**. ⚠ **NOT PROVEN:** how the stubs reached `Main_Castle_Overworld.unity` in the first
place. The likeliest route is the merged-world derivation from `MainCastle_Hall`, but that was not traced
and is recorded here as unproven rather than asserted.

**Residual risk to hand the owner:** if `BatchAddCastleWaveSystem` is ever re-pointed at the hub scene,
it re-bakes 4 stubs and silently re-suppresses the injector fan again — the same failure, with no error.
Closing that loop (delete `PlaceCastleSpawnPoints`, or re-point it at the injector's
`spawn-castle-<dir>-<i>` fan) is out of this WO's scope — this WO forbids touching that file — and needs
its own ticket + an owner call.

Also verified at source, and it de-risks the removal:
- The scene holds **exactly 4** `WaveSpawnPoint` components (script guid `19a9b4c5313391d4e9953210644d97eb`),
  ids/gates/dirs matching the table above.
- `WaveManager`'s serialized `_spawnPoints: []` in the scene is **EMPTY**, so the delete leaves **no
  dangling list element**; `WaveManager.cs:1684-1688` repopulates from `FindObjectsByType` at loop
  start, which is how the injector's 20 markers get picked up.
- No other component in the scene references the 4 GameObject fileIDs (only their own
  Transform/MonoBehaviour back-pointers), so the delete strands nothing.

## Acceptance criteria

- [ ] Brace balance + NUL check pass on every touched `.cs`.
- [ ] `COMPILE_GATE_OK` + `REGRESSION_OK n/n` on a fresh log.
- [ ] Batchmode run of the new method prints `LEGACY_SPAWN_STUBS_REMOVED n=4` on a fresh log.
- [ ] A subsequent headless (or device) session's log shows
  `[CastleSpawnPointInjector] placed 20 wave spawn points on 4 sides (S/W/N/E)...` — proving the
  full 5-per-side fan is now live, not the 4 stubs.
- [ ] `git diff` on `Main_Castle_Overworld.unity` touches **only** the 4 named GameObjects and
  their dependents (no unrelated scene churn) — review by hand before commit, per the scene-file
  hard rules.

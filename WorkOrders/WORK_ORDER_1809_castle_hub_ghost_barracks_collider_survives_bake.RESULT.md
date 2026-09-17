# WO-1809 — RESULT: the invisible barracks is gone from the baked hub, and an oracle now keeps it gone

**Status:** IMPLEMENTED
**Date:** 2026-09-16
**Owner report answered:** *"can you check the bake castle scene. I think it still has the invisible barracks object"* — **she was right.**
Code + scene are in the tree and NOT gated/committed: the lead gates the combined tree and is the sole committer.

---

## What the defect actually was

The WO-1716 **code** fix (the `StripHostVisual` / `EnsureNoHusk` pair in
`CastleHubBuilder.SkinHostUpright`, plus `NavMeshBakeFinal`'s refusal to carve off a twin that renders
nothing) had landed and was correct. What had **never been run** was the one-time scene repair it
shipped alongside — `StructureHuskCleanup.RemoveBatch`. So the *navmesh hole* was already gone while the
*collider* stood untouched at (16,0,-4), and the owner kept walking into it. **No builder edit was
needed, and none was made.** The lesson is in the shape, not the object: a one-time command is not a
fix until something watches its result, which is why this ticket's deliverable is an oracle.

## The runs — markers on fresh logs, judged by CONTENT

| Step | Log | Marker + content |
|---|---|---|
| Preview (before) | `Builds/wo1809-husk-preview.log` | `STRUCTURE_HUSK_PREVIEW_OK 1 husk(s) would be removed: CastleBarracks` — `CANDIDATE 'CastleBarracks' renders nothing but keeps 1 enabled solid collider(s) and 0 enabled carving NavMeshObstacle(s); worldPos=(16.00, 0.00, -4.00) localScale=(0.60, 0.90, 0.60) prefab='…Military_M/Military_Barracks.prefab'` |
| Remove | `Builds/wo1809-husk-remove.log` | `STRUCTURE_HUSK_CLEANUP_OK removed 1 husk(s): CastleBarracks @ (16.00, 0.00, -4.00)`; `[Flow:Hub] removing invisible structure husk 'CastleBarracks' at (16.00, 0.00, -4.00) (WO-1716)`. Backup: `Builds/castle-validation-20260913/before_husk_cleanup_20260917_014637_701.unity` |
| Re-bake | `Builds/wo1809-navmesh-bake.log` | `NAVMESH_BAKE_OK 1 surface(s) — Main_Castle_Overworld.unity`; `baked twins: 3 converted to dynamic carving, 8 named in the catalog but absent`; **`bakedTwin 'CastleBarracks' is authored in the catalog but NOT in this scene`** — the expected benign line, and **no `RENDERS NOTHING` line anywhere**; `scene bytes 1,117,484 -> 1,117,484, lastWrite=2026-09-17T01:47:32Z` |
| Oracle (after) | `Builds/wo1809-oracle-after.log` | `HUB_HUSK_FREE_OK 'Main_Castle_Overworld': 1 'Barracks' host(s) across 364 transform(s)` |

**Husk count 1 → 0. Scene bytes 1,120,814 (HEAD) → 1,117,484 (3,330 bytes = the removed PrefabInstance);
the bake then re-pointed the navmesh without changing the size.** Carving obstacles: 3 before, 3 after
(`Crafting` 1.5×2.3×1.5, `IronMine` 7.4×4.0×6.5, `ArcaneTower_MagicUpgrades` 2.3×4.0×2.2) — every one
`rendersNothing=False`. The ghost never had a baked obstacle of its own; `NavMeshBakeFinal` had already
stopped minting one, which is exactly why only the collider survived to be felt.

### A measurement worth keeping
`[host] 'Barracks' at (3.5,-0.1,-19.2) renders (7.1,4.0,7.0) m, 1 solid collider(s) span (7.1,4.0,7.0) m,
worst overhang 0.00 m.` The authored Tripo barracks' collision matches its silhouette **exactly**, so the
new oracle's 1 m slack was not tuned to fit the content — it passes with a full metre to spare. That was
the one risk flagged before the run, and it is measured away rather than argued away.

## Files

* **NEW** `Assets/Editor/Regression/HubHuskFreeSceneRegression.cs` — `HUB_HUSK_FREE_OK` / `_FAIL`.
  Resolves the hub from `SceneRouter.Castle` (never a literal — `HubSceneLiteralRegression`) and
  **refuses the legacy `CastleCandidates[1]` branch** rather than printing green on the wrong scene.
  Five cases; husk/bounds scoped to `*Barracks*` and the carve case scene-wide, **because the Preview run
  measured twelve legitimate husk-shaped objects** in this hub (`ExteriorTerrain`, `Hero (Blaise)`, four
  `Wall_DoorJamb_L/R` pairs, `MainKeep_CastleWithTwoLevels_Home`,
  `PlayerHeroHall_PersonalQuarters_HomeSpace`). Hollow-pass guards: <100 transforms or zero `*Barracks*`
  hosts is a FAIL, so the suite cannot go green by censusing nothing.
* `Assets/Editor/Regression/DataRegression.cs` — one `hub-husk-free` registration line, immediately
  ABOVE the `blank-start-census` fence (the two scene-openers sit together, each re-opening the hub).
* `Assets/Editor/Regression/StructureRemovalHuskRegression.cs` — header `:52-56` re-pointed at
  `[hub-husk-free]`. It had said the scene *still* held the ghost "until the lead runs" the command;
  true when written, stale the moment the command ran, and the stale sentence is now the paragraph's
  own worked example (§15).
* `Assets/Scenes/Main_Castle_Overworld.unity` — rebuilt through the sanctioned tooling only
  (`RemoveBatch` + `NavMeshBakeFinal.Run`). **Never hand-edited** (CLAUDE.md §3).

`python tools/gate_brace.py` exit **0** on all three `.cs` (`GATE_BRACE_SUMMARY bad=0 of 3`), **0 NUL
bytes** in each.

## What is NOT proven

1. **The owner's felt "invisible wall" is gone.** Headless proves the GameObject and its collider are
   out of the saved scene; it cannot judge feel. PO felt-verify on the device closes this (§13).
2. **The confirming 4th `PreviewBatch` run did not execute.** Three separate parallel lanes broke the
   tree's compile mid-write during this window (`SaveMigrator.MigrateToV42`, `ArmyMusterVM.ReserveOf` /
   `DismissGoldFor`, `ArmyScreenCopyRegression.RowMeta`), and rather than keep racing their saves I
   stopped: `HUB_HUSK_FREE_OK` already censuses the same saved scene through the same shared predicate
   (`StructureVisualStrip.IsInvisibleNavBlockingHusk`) that `PreviewBatch` uses, so the re-run would
   have added a second reading of one fact, not a new one. Say it plainly: the 0-husk Preview line was
   not captured.
3. **No gate, no commit, no git.** `COMPILE_GATE_OK` / `REGRESSION_OK` on the combined tree is the
   lead's, and the tree did not compile cleanly at hand-back time for reasons owned by other lanes.

# WORK ORDER 1731 — A NavMeshSurface bake can silently DELETE the town's navmesh and leave the scene pointing at nothing

**Status:** DONE — 4a + 4b implemented 2026-09-14, awaiting the lead's gate run; §5 Q1 deliberately untouched (owner's call, not this lane's); see `WORK_ORDER_1731_navmesh_surface_bake_orphans_the_town_navmesh.RESULT.md`
**Minted:** 2026-09-14, after the owner's F8 flags seq 5231 / 5232 / 5239 (camera inside a town wall,
`Main_Castle_Overworld`) were root-caused to this, and after she asked directly: *"what about the rename?
Noone should have renamed it that i know of"* — **she is right, nobody renamed anything.** See §2.
**Silo:** navmesh bake tooling (`Assets/Editor/Castle*.cs` — the scripts that call `BuildNavMesh()`).
⛔ File-disjoint from WO-1723 Lane B (`RaidBaseDresser` / `RaidBaseGenerator` / `WallSegment`) and from
WO-1730 (`RaidAssaultAi` / `TroopController`). All three may run in parallel.

---

## 1. THE SYMPTOM, AND WHAT IT COST

The hero walks through every wall and building in town; the camera ends up inside geometry. Proving
line, device `SM02G4061955851`, build `2026.09.15.370139`, 20:09:12:

```
[Flow:HeroOwner] scene='Main_Castle_Overworld' owner=HeroLocomotion ownerAgent=off-mesh
  ... pos=(-38.85, 0.00, -23.54)
```

`ownerAgent=off-mesh` is the whole finding. `HeroLocomotion` moves by `_agent.Move(step)` when on-mesh
and **`transform.position += step` when OFF-mesh** (`HeroLocomotion.cs:1488-1489`), with
`obstacleAvoidanceType = NoObstacleAvoidance` (`:999`) and **zero** `Physics.` / `Raycast` /
`CapsuleCast` / `Rigidbody` / `CharacterController` references in the file. In town the **navmesh is the
only thing that constrains the hero** — so no navmesh means nothing stops her, anywhere.

**Cost:** it shipped to the owner's device in build `2026.09.15.370139` and burned a felt-test.

## 2. ⛔ IT WAS NOT A RENAME. THE OWNER IS CORRECT THAT NOBODY RENAMED A FILE.

*(The lead first described this as "an unfinished rename". That was an INFERENCE from two filenames and
it was WRONG — CLAUDE.md §11B. Corrected here from what was actually read.)*

Unity's `NavMeshSurface` bake writes its output as **`NavMesh-<GameObjectName>.asset`**, named after the
GameObject carrying the component. The scene contains a GameObject literally named
`OuterWorld_NavMeshSurface` (`Assets/Scenes/Main_Castle_Overworld.unity:16784`, `m_Name:
OuterWorld_NavMeshSurface`; the scene holds 2 `NavMeshSurface` references). So:

- A bake ran on that surface. Unity wrote **`Assets/Scenes/Main_Castle_Overworld/NavMesh-OuterWorld_NavMeshSurface.asset`**
  (guid `6d1a44b74e916ce41b25db55656f1459`, dated **Sep 14 09:53**) and removed the previous output,
  `NavMesh-Main_Castle_Overworld.asset` (guid `6af7dcf896317734a986e2bcc349575d`).
- **The scene was never saved with the new reference.** Both at `HEAD` (`:17047`) and in the working
  tree (`:16820`) the surface still serializes
  `m_NavMeshData: {fileID: 23800000, guid: 6af7dcf896317734a986e2bcc349575d, type: 2}` — the OLD,
  now-deleted asset.

**Net result: the new asset is an orphan nothing references, and the scene's reference dangles.** The
filename difference is Unity's naming convention reacting to which GameObject owns the surface — not a
human action. No script in `Assets/` contains the string `OuterWorld_NavMeshSurface`
(grepped `Assets/Editor/` and `Assets/_Modules/`, zero hits), so the GameObject is authored scene
content, not something a builder creates by that name.

**Candidate bakers** — scripts that call `BuildNavMesh()` and could have triggered this:
`Assets/Editor/CastleWalkable.cs`, `CastleHubBuilder.cs`, `CastleTroopWallNav.cs`,
`CastleWallsFromRecipe.cs`, `CastleBuilderTester.cs`, `DungeonChainBuilder.cs`.
⚠ **WHICH ONE actually ran on 2026-09-14 ~09:53 is NOT PROVEN.** Do not name a culprit without evidence;
check `Builds/*.log` around that timestamp before writing one into a commit message.

## 3. IMMEDIATE MITIGATION — ALREADY DONE, DO NOT REDO

`git checkout HEAD -- Assets/Scenes/Main_Castle_Overworld/NavMesh-Main_Castle_Overworld.asset{,.meta}`
restored the asset the scene actually points at, so the reference resolves again. Verified in build
`2026.09.15.370158`, installed 20:24:43. The orphaned `NavMesh-OuterWorld_NavMeshSurface.asset` is
**left untracked and untouched on purpose** — deleting a 1.5 MB bake nobody has evaluated is not the
lead's call.

**This mitigation is a restore, not a fix.** The next bake will do it again.

## 4. THE ACTUAL FIX

**The defect is that a bake can delete a scene's live navmesh and leave the scene unsaved.** Two halves:

### 4a. Any script that calls `BuildNavMesh()` MUST save the scene in the same operation
Unity serializes the new `m_NavMeshData` reference into the scene; if the scene is not marked dirty and
saved, the asset on disk and the reference in the scene diverge — exactly what happened. Follow the
pattern `RaidNavBake.BakeAll` already uses correctly
(`Assets/Editor/RaidNavBake.cs:82-85`): `EditorSceneManager.MarkSceneDirty(scene)` then
`EditorSceneManager.SaveScene(scene)`, and **throw** if the save fails. Audit every script in the §2
candidate list against that pattern.

### 4b. A guard that makes this class of failure LOUD instead of silent
Today the only detector was the owner's eyes on a device — the §16 failure shape all over again. Add a
regression/oracle that asserts, for every shipped scene carrying a `NavMeshSurface`, that its
`m_NavMeshData` guid **resolves to a file that exists**. A dangling navmesh reference must fail a gate,
not a playtest. This is the higher-leverage half; prefer it if only one half can be done.

## 5. OPEN OWNER RULING

**Q1 — what happens to the orphaned `NavMesh-OuterWorld_NavMeshSurface.asset`?** Three options, none
free:
  - (a) **Delete it.** Cleanest if the surface is meant to keep using the legacy-named asset.
  - (b) **Adopt it** — re-point the scene's surface at the new guid and save, making the new bake the
    live one. Correct IF that 09:53 bake was intentional and better than the restored one. **Nobody has
    evaluated its coverage**, so this is not a safe default.
  - (c) **Leave it untracked** as-is (today's state) — no risk, but the confusion recurs for the next seat.
Recommend (a) only after someone compares the two bakes' coverage; until then (c) is the honest hold.

## 6. ACCEPTANCE CRITERIA

- Every `BuildNavMesh()` caller marks dirty + saves + throws on failure, matching `RaidNavBake.cs:82-85`.
- A gate/regression FAILS on a scene whose `NavMeshSurface.m_NavMeshData` guid does not resolve. Prove it
  by deliberately breaking a reference and watching the gate go red (a guard nobody has seen fail is not
  a guard).
- `Main_Castle_Overworld` loads with the hero reading `ownerAgent=on-mesh` in a captured
  `[Flow:HeroOwner]` line — the inverse of §1's proving line.
- Brace + NUL gate on every `.cs`, plus `python tools/gate_brace.py`.
- WO Status flipped and `.RESULT.md` written in the same hand-back (CLAUDE.md §11).

## 7. NOT IN SCOPE

- The raid navmesh (WO-1723) — different bake, different scenes, already fixed.
- Re-baking `Main_Castle_Overworld` — the restored asset works; do not re-bake to "tidy" it without a
  ruling, since that is the operation that caused this.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>

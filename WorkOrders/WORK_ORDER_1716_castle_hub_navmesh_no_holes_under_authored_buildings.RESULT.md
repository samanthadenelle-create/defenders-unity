# WORK ORDER 1716 — RESULT (implementation lane, 2026-09-14)

**Status:** IMPLEMENTED — awaiting lead gate. Edit-only lane: no Unity run, no gate, no build, no
commit, and **no scene file was touched** (CLAUDE.md §3).

---

## 1. The removal/strip code path — FOUND, cited at source

The ticket's hypothesis ("whatever remove/replace structure code path strips the MeshFilter/
MeshRenderer but does not clear the Collider/NavMeshObstacle") is **correct in substance and wrong
about which path**. Both PLAYER-FACING removal paths are already complete and were never the bug:

- `BuildModeController.SellSelected` — `Assets/_Modules/Village/BuildMode/BuildModeController.cs:2727`
  → `Destroy(ps.gameObject)`.
- `Destructible.NotifyBroken` — `Assets/_Modules/Village/Vfx/Destructible.cs:193` → `Destroy(gameObject)`.
- `HubStructureVisualInjector.SuppressBakedTwinPhysics` (`:534-547`) already downs colliders **and**
  NavMeshObstacles together.

**The actual path is an EDIT-TIME one, and it took two steps to make the ghost:**

1. `CastleHubBuilder.SkinHostUpright` — **`Assets/Editor/CastleHubBuilder.cs:598-602` (pre-fix)**
   destroyed every `Renderer` and `MeshFilter` on the host and **nothing else**, so the polyperfect
   `MeshCollider` shaped by the mesh it had just destroyed stayed live. If `VisualFactory.Skin` then
   returned null (it only `LogWarning`-ed) the host was left **invisible but solid**. *Why Skin
   returned null for this object is NOT proven by this lane* — it resolves through
   `StructureAssetLoader`, not `Resources.Load` (`VisualFactory.cs:244-247`), so the answer is a
   runtime/Addressables question, not a source read. Stated as unproven rather than guessed.
   *Scene evidence this is what happened to `CastleBarracks`:* its `PrefabInstance` in
   `Assets/Scenes/Main_Castle_Overworld.unity:18489-18492` carries `m_RemovedComponents` = 2 entries
   **and `m_AddedGameObjects: []`** — the visual was stripped and **no skin child was ever added**.
2. `NavMeshBakeFinal.PrepareBakedTwinsForDynamicCarving` — `Assets/Editor/NavMeshBakeFinal.cs:307-349`
   resolves every catalog `bakedTwins` name **by bare name** and sizes a carving `NavMeshObstacle`
   from that object's **collider** bounds. Handed the invisible husk it faithfully produced the
   owner's captured box: scene `:18501-18515`, `m_Extents {8.45356, 2.5414433, 7.2565894}` →
   Size **16.90712 x 5.08288 x 14.51318**, `m_Carve: 1`, added as `m_AddedComponents`.

**Provenance, proven by git, not inferred** — `git log -S` on the two scene fileIDs:
- the MeshFilter/MeshRenderer removal entered in `0f2713e1d` *"Stand hub Tripo storefronts upright
  (X=90) before navmesh bake"*, the same commit that added `SkinHostUpright` to `CastleHubBuilder.cs`;
- the NavMeshObstacle entered in `c83f29c11` *"fix(nav): baked twins carve DYNAMICALLY — PROD-004
  Cause 2"*, the same commit that added `PrepareBakedTwinsForDynamicCarving` to `NavMeshBakeFinal.cs`.

So the object **predates tonight's session** and is **not** a leftover of the owner's removal attempt.
Its transform is also byte-for-byte `CastleBarracksPlacer.PlaceInCastle` output (spawn offset
`(16,0,-4)`, scale `0.6 / 0.6*1.5 / 0.6` → `0.6/0.9/0.6`, `Assets/Editor/WallTools/CastleBarracksPlacer.cs:33,37,47`).

---

## 2. The fix — one owner for "the visual was taken away"

**Design choice: option (a) is wrong here and option (b) is what the codebase already does.** These
hosts legitimately persist (they keep their transform, their NPC interact points and, on the working
rows, a re-skinned Tripo visual), so the fix clears the geometry **in the same operation** as the
visual rather than destroying the GameObject. It deliberately leaves **BoxColliders** alone because
`Building.EnsureBlocker` (`Building.cs:373-377`) falls back to `GetComponent<BoxCollider>()` and
re-`Add`s one — destroying it would fight that seam on every `Configure()`; and it leaves **triggers**
alone because an NPC point is not solid geometry. This mirrors the existing
`SuppressBakedTwinPhysics` / `RestoreBakedTwinPhysics` pairing rather than inventing a parallel one.

| File | Lines | Change |
|---|---|---|
| `Assets/_Modules/Village/World/StructureVisualStrip.cs` | NEW (whole file) | The single owner, deliberately TWO calls. `StripHostVisual(host, reason)` removes the host's Renderers + MeshFilters **and nothing else**. `EnsureNoHusk(host, reason)` is the closing half: **only if** nothing under the host renders, it destroys the solid MeshColliders and every NavMeshObstacle, and `FlowTrace.Fail`s if a husk still survives. `IsInvisibleNavBlockingHusk(go, out detail)` is the shared predicate. Lives in `DeNelle.Village`, not `DeNelle.Editor`, because `DeNelle.Editor` references `DeNelle.EditorRegression` — a helper in `Assets/Editor` could never be exercised by the oracle that pins it. |
| `Assets/Editor/CastleHubBuilder.cs` | `:598-612` | The bare `DestroyImmediate(r)` / `DestroyImmediate(mf)` loops become one `StructureVisualStrip.StripHostVisual(host, stripReason)` call. |
| `Assets/Editor/CastleHubBuilder.cs` | `:628` | **The branch that minted the ghost.** `Skin` returned null → `EnsureNoHusk` now clears the orphaned collision + carve, and the `FlowTrace.Fail` names the object (a silent `LogWarning` is how an invisible host reached a saved scene — CLAUDE.md §12). |
| `Assets/Editor/CastleHubBuilder.cs` | `:642-646` | Post-skin invariant assert — a no-op on the normal path, and the only thing that catches a non-null visual that nevertheless renders nothing. |
| `Assets/Editor/NavMeshBakeFinal.cs` | `:324-357` | Defence in depth at the step that weaponised the husk: a `bakedTwin` whose subtree renders **nothing** is refused. Its enabled colliders are **first held out of this bake** (and restored afterwards by the existing `RestoreColliders`), then any carving obstacle it holds is destroyed, then it is skipped. Holding the colliders out is load-bearing: skipping past them would have frozen the same footprint as a **permanent static hole** in the surface asset — the method's own "never one without the other" rule broken in the other direction. |
| `Assets/Editor/NavMeshBakeFinal.cs` | `:421` | `ReadBakedTwinNames()` made `public` so the cleanup command reads the **same** twin list the bake reads (no second copy — CLAUDE.md §5). |

**⚠ THE DESIGN NOTE THAT MATTERS, and it reverses an obvious-looking fix.** Clearing the host's
collider in the same call as its renderer is WRONG here, and the first draft of this lane did exactly
that. `SkinOptions.Structure` sets `StripColliders = true`
(`Assets/_Modules/Village/VisualFactory.cs:53,112,368-370`) — *"remove the model's own colliders (the
host owns its collider)"*. So across a **successful** re-skin the host's existing collider **is** the
new building's body collision. Taking it unconditionally would have let the player walk through every
re-skinned structure and sent `PrepareBakedTwinsForDynamicCarving` down its own `NO collider — it
never carved` branch, i.e. a navmesh running **straight through** the building — which that method's
header calls "far worse than an invisible footprint". Hence the two-call contract.

No `MeshCollider.sharedMesh` assignment is fought: `Building.EnsureBlocker` only ever touches
`BoxCollider`, which this change never removes.

---

## 3. One-time cleanup command for the specific leftover

`Assets/Scenes/Main_Castle_Overworld.unity` is a **hand-curated, committed scene asset** — confirmed:
no builder regenerates it (`CastleHubBuilder`'s batch entries target the legacy
`Assets/Scenes/MainCastle_Hall.unity`, `:1509` / `:1560`). So the repair is a menu command, **not** a
regression side effect and **not** a load-time repair.

**New file: `Assets/Editor/StructureHuskCleanup.cs`**

| | |
|---|---|
| Preview (destroys nothing) | Menu `Defenders/Castle/Preview invisible structure husks` — batch `DeNelle.Editor.StructureHuskCleanup.PreviewBatch` |
| **Apply (destroys + saves)** | Menu `Defenders/Castle/Remove invisible structure husks` — batch `DeNelle.Editor.StructureHuskCleanup.RemoveBatch` |
| Markers | `STRUCTURE_HUSK_PREVIEW_OK` / `STRUCTURE_HUSK_CLEANUP_OK` / `STRUCTURE_HUSK_CLEANUP_FAIL` |

Three rules must **all** hold before anything is destroyed: (1) it is a husk by the shared predicate;
(2) its name is an authored `bakedTwins` entry read from the same catalog the bake reads; (3) it
carries **no** `Building` / `AuthoredCastleStorefront` / `PlacedStructure` / `ResourceCollector`
anywhere in its subtree. Rule 3 is the load-bearing guard: **two objects answer to "CastleBarracks"** —
the ghost (polyperfect `Military_Barracks`, no scripts) and the owner's **visible** authored barracks,
which is a different GameObject **named `Barracks`** (scene `:23050`, prefab guid `5a258590…`) carrying
`Building` + `AuthoredCastleStorefront` with `legacyName: CastleBarracks` (`:23172`). Selecting by name
alone would have risked deleting the owner's building. It also refuses on a dirty scene and copies the
scene file to `Builds/castle-validation-20260913/before_husk_cleanup_<stamp>.unity` before saving —
the `OwnerCastleLayoutRepair.RepairGroupPhysics` discipline.

**After running it, re-bake:** `Defenders/World/Bake NavMesh (ALWAYS LAST)` — the carve does not
update itself.

---

## 4. The WO-1710 connection — **SUPPORTED, and it flips on a change made TODAY**

Corrected after a second read; the first draft of this section said "disproven" and was wrong, because
it judged the ghost against a marker that does not exist in `HEAD`.

- `AuthoredCastleStorefront.Find` (`Assets/_Modules/Village/AuthoredCastleStorefront.cs:88-109`)
  prefers the **MARKED** host — an object carrying an `AuthoredCastleStorefront` whose `LegacyName`
  matches — and falls back to a **bare NAME** match only when no marked host exists.
- The owner's visible barracks is named **`Barracks`**, not `CastleBarracks` (scene `:23050`). So
  **without a marker, the only object named `CastleBarracks` is the GHOST**, `legacyCount == 1`, and
  `Find("CastleBarracks")` returns it — for `StructureSingleton.StandDownBakedTwins:427` and for
  `HubStructureVisualInjector.EnsureBarracksSurfaced:262,269`. Every barracks surface/standdown
  decision would have been made against an invisible polyperfect husk.
- **`legacyName: CastleBarracks` is NOT in `HEAD`** — verified: `git show
  HEAD:Assets/Scenes/Main_Castle_Overworld.unity | grep -c "legacyName: CastleBarracks"` → **0**, and
  the scene is `M` (modified, uncommitted) in the working tree. The marker is today's, uncommitted work.

**So: pre-marker, the ghost DID own the `CastleBarracks` lookup — consistent with the owner's "troop
training doesn't see my barracks / I removed it and could never re-add it". Post-marker (working tree
now) the marked host wins and the ghost cannot.**

What is still NOT proven, and is stated as unproven rather than guessed: that this lookup is the
mechanism the owner actually hit. `StructureSingleton.HasPlacedInstance` reads BaseLayout records,
live `PlacedStructure` and live `Building` and **excludes baked twins by design** (documented at
`HubStructureVisualInjector.cs:258-260`), and the ghost carries none of those components — so the
ghost could never register as a *placed* barracks. The plausible path is the surface/standdown one,
not the placement one. Closing this needs the owner playtest the ticket already asks for (item 3),
after the §3 cleanup lands.

**The generalisable finding:** identity-based lookups (marker components) were safe; **name-based**
lookups were not. `NavMeshBakeFinal` resolves twins by bare name and demonstrably picked the ghost —
that is what the oversized obstacle sitting on it proves. §2's fix closes that one.

---

## 5. Regression

**New file: `Assets/Editor/Regression/StructureRemovalHuskRegression.cs`** — `[removal-husk]`,
markers `REMOVAL_HUSK_OK` / `REMOVAL_HUSK_FAIL`, standalone entry
`DeNelle.Editor.Regression.StructureRemovalHuskRegression.RunStandalone`.

Five behavioural cases against real fixture GameObjects (no scene load, no PlayMode):
1. the historical ghost signature (renderer + filter destroyed, MeshCollider + carving obstacle left)
   is **flagged** — an oracle that cannot see the old bug proves nothing about the fix;
2. the PAIR holds and is a pair — `StripHostVisual` must leave collision alone (the successful-re-skin
   contract), and `EnsureNoHusk` then clears the MeshCollider + NavMeshObstacle and the predicate
   clears the object;
3. the close is surgical **and a no-op when the re-skin worked** — with a rendering child present the
   MeshCollider and NavMeshObstacle survive untouched, and the BoxCollider (`_blocker` seam) and a
   trigger survive regardless;
4. it is idempotent — a second strip removes nothing and a second `EnsureNoHusk` reports no husk;
5. both call sites still route through the one owner, source-linted with **comments and string
   literals stripped first** (both files discuss this ticket at length in prose).

Fewer than five cases, or an unreadable source file, is a **FAIL**, never a pass.

**Deliberately NOT asserted: that the hub scene contains no husk.** It still contains the proven
`CastleBarracks` ghost until the lead runs the §3 command — a scene assertion would be red for a
known, ticketed reason and would train the next reader to ignore this marker.

**Registration:** `Assets/Editor/Regression/DataRegression.cs:412` — one line added, `git diff --stat`
confirms `1 insertion(+)`, 0 deletions.

---

## 6. Gate evidence (this lane)

```
python tools/gate_brace.py <6 files>
GATE_BRACE_SUMMARY bad=0 of 6
GATE_BRACE_EXIT=0

NUL scan (embedded/trailing \x00):
NUL_CLEAN Assets/_Modules/Village/World/StructureVisualStrip.cs            nuls=0 bytes=11404
NUL_CLEAN Assets/Editor/CastleHubBuilder.cs                               nuls=0 bytes=185180
NUL_CLEAN Assets/Editor/NavMeshBakeFinal.cs                               nuls=0 bytes=33383
NUL_CLEAN Assets/Editor/StructureHuskCleanup.cs                           nuls=0 bytes=11339
NUL_CLEAN Assets/Editor/Regression/StructureRemovalHuskRegression.cs      nuls=0 bytes=22845
NUL_CLEAN Assets/Editor/Regression/DataRegression.cs                      nuls=0 bytes=439644
NUL_SUMMARY bad=0 of 6
NUL_EXIT=0

Source-lint pre-check (case 5 ported to Python, SAME comment/string stripper):
OK   Assets/Editor/CastleHubBuilder.cs                        :: StructureVisualStrip.StripHostVisual
OK   Assets/Editor/CastleHubBuilder.cs                        :: StructureVisualStrip.EnsureNoHusk
OK   Assets/Editor/NavMeshBakeFinal.cs                        :: GetComponentsInChildren<Renderer>(true).Length == 0
OK   Assets/_Modules/Village/World/StructureVisualStrip.cs    :: IsInvisibleNavBlockingHusk
LINT_PORT bad=0
```

Marker uniqueness (RULE 1 of `RegressionMarkerRegression`): `REMOVAL_HUSK_*` and `STRUCTURE_HUSK_*`
return **zero** hits anywhere else under `Assets/`, `tools/`, `.claude/`. The existing source-lints
that touch the two edited files were checked and none is disturbed:
`FoundersMonumentWallRegression:155,507` (anchor-name literal, untouched),
`PremadeCastleCompleteRegression:389` (asserts `CastleHubBuilder` emits no `Wall_South_/Wall_North_/QWall_`
— none added).

**NOT proven by this lane (stated plainly, per CLAUDE.md §11B):** nothing was compiled or run — no
`COMPILE_GATE_OK`, no `REGRESSION_OK`, no bake. The line numbers for the edited files above are
post-edit and were read from the working tree this session; the new `.cs` files have no `.meta` yet
(Unity generates them on the lead's first editor launch).

---

## 7. Follow-up the lead still owns

1. Gate the combined tree (`COMPILE_GATE_OK` + `REGRESSION_OK` on a fresh log, marker not exit code).
2. ⚠ **`Assets/Scenes/Main_Castle_Overworld.unity` is ALREADY MODIFIED in the working tree** (`git
   status --short` → `M`), and the `AuthoredCastleStorefront` markers including
   `legacyName: CastleBarracks` exist ONLY there, not in `HEAD`. The cleanup command refuses on a
   dirty *loaded* scene but cannot see uncommitted file changes — decide what to do with that
   pending scene diff BEFORE running it, or the two get folded into one save.
   Then: `Defenders/Castle/Preview invisible structure husks` first, read the CANDIDATE/SKIP lines,
   then `Defenders/Castle/Remove invisible structure husks`, then re-bake the navmesh
   (`Defenders/World/Bake NavMesh (ALWAYS LAST)`), then commit the scene + the code by explicit path.
3. **Its own ticket, not this lane:** after the cleanup, `NavMeshBakeFinal` will log `bakedTwin
   'CastleBarracks' … NOT in this scene` — because the real barracks is named `Barracks` and the bake
   resolves twins by **bare name** while `StructureSingleton` now resolves by **marker**. Two
   strategies for one concept. Not a regression (the owner's barracks has no dynamic carve today
   either), but the bake should move to `AuthoredCastleStorefront.Find(name, includeInactive: true)`.
   That is WO-1710 territory and deliberately untouched here.
4. `CastleBarracksPlacer` still targets the **legacy** `Assets/Scenes/MainCastle_Hall.unity`, which
   CLAUDE.md §7 says is not the hub. Left untouched by this lane (out of scope), flagged here because
   it is how this object entered a scene it was never meant to live in. Worth its own ticket.

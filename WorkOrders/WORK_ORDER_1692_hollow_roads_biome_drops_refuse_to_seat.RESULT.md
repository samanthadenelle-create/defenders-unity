# WO-1692 RESULT — biome drops refuse to seat: the probe was asked in the wrong scene

**Status:** IMPLEMENTED (code + oracle in the lane worktree; NOT gated, NOT committed — no Unity was run
by this lane). 2026-09-10.
**Lane:** BIOME-ROADS SME, worktree `.claude/worktrees/agent-a2df2f3ed921d85dc`, branch `dev` at
`1133baf35` (ff-only merge clean).

---

## 1. RCA — with file:line

**Symptom (captured, not reported):** F8 device seq 5003–5007, Seeker `SM02G4061955851`, build 2026.09.10
vc 363866, device UTC 2026-09-10T18:18:51, `t=441.6`:

```
REFUSED the Ashwood drop at seat time: its derived destination (0.00, 17.00, 400.00) has NO navmesh
within 12m ... NOT seated.                                                     (and Goldfields/Mirewood/Stoneback)
seated 0 of 4 biome drops - every tunnel arm dead-ends. Suspect the arm room ids in dg_hollow_roads.json
no longer match BiomeRoads.ArmRoomIdFor.
```

**The trace's hypothesis is REFUTED (three independent proofs):**
1. `Assets/_Modules/Core/World/BiomeRoads.cs:222-236` returns `arm_ashwood/arm_goldfields/arm_mirewood/
   arm_stoneback`; `Assets/Resources/Data/Canonical/dungeon-graphs/dg_hollow_roads.json` declares exactly
   those node ids (StreamingAssets twin identical).
2. `git log --oneline -8` on that JSON: `eb9ae9c19`, `74161fa22`, `a24654c21` — no rename, so there is no
   drift commit to find.
3. The refusal is emitted at `HollowRoadsDropInjector.cs:387` (pre-fix), which is **after**
   `composeRoot.Find(drop.ArmRoomId)` (`:340-348`) and after the arm-bounds measurement (`:351-357`). A
   missing arm emits a different message. All four arms resolved.
   Plus: `Assets/Editor/Regression/BiomeRoadsRegression.cs:226-328` (Case 2) is exactly the oracle the
   brief asked for and has been **green throughout** — the requested "RED on HEAD" test could not exist.

**Real cause:** `HollowRoadsDropInjector.cs:387` (pre-fix)
`NavMesh.SamplePosition(drop.Point, out _, ArrivalSampleRadius, NavMesh.AllAreas)` runs inside
`dg_hollow_roads` and asks about a coordinate in `Main_Castle_Overworld`. Entry is
`DungeonPortal` → `SceneRouter.GoDungeonScene` (`Assets/_Modules/Core/SceneRouter.cs:709-725`) →
`LoadSceneWithFade` → `SceneManager.LoadSceneAsync(sceneName)` (`SceneRouter.cs:323`) with the
**default `LoadSceneMode.Single`** — its own `beforeLoad` hook is named `CarryHeroAcrossSingleLoad`. The
hub and its baked navmesh (`Assets/Scenes/Main_Castle_Overworld.unity:17047` →
`Assets/Scenes/Main_Castle_Overworld/NavMesh-Main_Castle_Overworld.asset`, 1.5 MB) are unloaded. The probe
interrogates the tunnel's nine-room corridor about a point 400 m outside it. **It cannot hit on any
terrain, in any build** — which is why all four refusals are identical.

Secondary, and real: the derived Y is `worldBounds.center.y` = **17** (`BiomeRoads.cs:420`), which
`ResolveDrops` documents at `:322-332` is the CALLER's job to ground. On a terrain based at y=-4 with a
42 m range, that vertical error alone can exceed the 12 m radius — so even asked in the right scene the
raw point could miss. The fix corrects both.

**Introducing commit: `6a5c7a36d`** — "chore: checkpoint complete workspace and rebuild board",
Wed Sep 9 14:17:30 2026, +145/−14 on the injector (the WO-1606 change).
`git show 6a5c7a36d -- Assets/_Modules/Village/World/HollowRoadsDropInjector.cs` carries
`+ if (!NavMesh.SamplePosition(drop.Point, …))` and `- seam.targetPosition = drop.Point;` /
`+ seam.targetPosition = groundedPoint;`. **Before it the drops seated** (F8 seq 4703/4706 are captures of
seated drops landing the hero badly); after it, zero seat. A behavioural fix landed inside a commit whose
subject says "chore … checkpoint" — worth its own note to the lead.

**Screenshot** `logs/f8-inbox/device/SM02G4061955851/break_05_error.png` (13:18:52): the player standing
in the dark tunnel, HUD live, "Leave Dungeon" up. Nothing looks broken — four doors are simply absent.

---

## 2. What was changed

| File | Change |
|---|---|
| `Assets/_Modules/Village/World/HollowRoadsDropInjector.cs` | `RememberHubBounds` (called from the hub, terrain + navmesh live) now runs `GroundDropsInHub`: origin control probe → per-region `Terrain.SampleHeight` Y fix → `NavMesh.SamplePosition` against the **hub's own** mesh → remembers the walkable point or nothing. `TrySeatDrop` consumes that memo (`TryRecallGroundedDrop`) and no longer probes; its refusal names which of the three causes applies. Header + in-place comments record why the tunnel must never probe. |
| `Assets/Editor/Regression/BiomeRoadsRegression.cs` | New **Case 8** `Case8_TheDestinationIsGroundedByTheSceneThatOwnsIt` + registration in `Run()`; `7 cases green` → `8 cases green`. |
| `Assets/Editor/Regression/BiomeRoadsDropReachProbe.cs` | NEW standalone measurement (menu `Defenders/Diagnostics/Probe Biome Road Drop Reach`, `RunStandalone`). Opens the shipped hub, measures terrain height at the four derived points, probes the baked navmesh from the raw and grounded points at the injector's own 12 m, reports nearest mesh within 600 m. Markers `BIOME_ROADS_REACH_OK` / `_SHORT` / `_FAIL`. |

**No `.unity` file touched. No JSON touched** (so no canonical-twin / CRLF work was needed — the twins are
untouched and identical, and I state that rather than claim a newline count I did not need to change).
**No bake requested — none is required by this fix.**

### Round 2 — WO-1091 (`BiomeDropGroundProbeRegression`) restored, exact lines

Chain 49 reported `BIOME_DROP_GROUND_PROBE_FAIL x3`. Cause and fix, per contract:

| Contract | Why it broke | Fix |
|---|---|---|
| `[one-authority]` (`BiomeDropGroundProbeRegression.cs:113-125`) | `Count(src, "NavMesh.SamplePosition(")` is a **substring scan over the whole file, comments included** — my three WO-1692 comments quoted the call, so 6 were counted vs 3 passing `ArrivalSampleRadius`. | Comments reworded to prose ("a navmesh sample of drop.Point"). Now **3 probes / 3 with `ArrivalSampleRadius`**: `HollowRoadsDropInjector.cs:587` (origin control), `:655` (hub grounding), `:817` (arrival judge). |
| `[fail-closed]` (`:128-149`) | The anchored branch `if (!NavMesh.SamplePosition(drop.Point` … `Vector3 groundedPoint` had moved out of the file. | Restored verbatim inside the new hub-side `TryGroundDrop` (`HollowRoadsDropInjector.cs:651-682`): `FlowTrace.Fail` + `Notify` + `return false;`. WO-1091's rule is that an ungrounded drop seats no door — it never required the probe to run in the tunnel, which is where it could not be answered. `drop.Point` is re-seated onto the terrain first (`Drop` is a struct, so the local copy IS the probed point — no shadow coordinate). |
| `[one-point]` (`:152-174`) | `Vector3 groundedPoint = groundHit.position;` had gone with the branch. | Back at `HollowRoadsDropInjector.cs:678`; it is what the memo stores and therefore what `seam.targetPosition = groundedPoint;` (`:442`) and `announce.PromisedPoint = groundedPoint;` (`:459`) receive. No consumer reads `drop.Point`. |

`[radii]` (reflection: `ArrivalSampleRadius` 12 ≥ `ArrivalSettleRadius` 8) and `[three-verdicts]` were
never touched and still hold.

**Case 8 was re-pointed in the same change** — its first predicate said "no drop-point probe anywhere in
this file", which would have outlawed the fail-closed guard WO-1091 mandates. That was my rule being
wrong, not the code: it is now **positional** — the drop-point probe must live in `TryGroundDrop` and
nowhere else, since every other site in the file runs while the tunnel is active. Still RED on HEAD.

**Ports re-run this round (exact output):**
```
probes=3 withArrivalSampleRadius=3
WO-1091 PORT -> ALL PREDICATES PASS
HEAD     -> ['a0: TryGroundDrop helper missing/misplaced', 'b: SamplePosition inside TrySeatDrop',
             'c1: no GroundDropsInHub', 'c2: no TryRecallGroundedDrop',
             'c3: RememberHubBounds does not run the pass']
WORKTREE -> NONE (green)
GATE_BRACE_SUMMARY bad=0 of 3      NUL: 0 / 0 / 0
```
Ports: `<scratchpad>/wo1091_port.py` (B/C/D/E predicates, same `Count`/`Between` semantics as the suite)
and `<scratchpad>/case8_check.py`.

**One behavioural consequence to flag:** the fail-closed `Notify` now fires **in the hub** (once per hub
load, since `TryPlace` sets `_placed` after one successful pass), not at the tunnel mouth. A player whose
bake does not reach a drop sees "The road to &lt;X&gt; is closed." in town. That is the WO-1091 contract
("says so loudly … on screen as well as in the log") honoured at the moment the answer is known; the
tunnel-side refusal keeps its own Notify for the arm that dead-ends. **If the lead prefers town silence,
say so** — it is one line, but suppressing it would weaken WO-1091 item 1 and I will not do that
unilaterally.

### Case 8 is RED on HEAD, green after — proven, not assumed
Ported the case's exact predicates to Python (same comment/string strip as `ReadStripped`) and ran them
against `git show HEAD:…HollowRoadsDropInjector.cs` and the worktree file:

```
HEAD     -> ['a: probes a drop point', 'b: SamplePosition inside TrySeatDrop',
             'c1: no GroundDropsInHub', 'c2: no TryRecallGroundedDrop',
             'c3: RememberHubBounds does not run the pass']
WORKTREE -> NONE (green)
```

### Registration line for the lead
**None needed.** Case 8 lives inside `BiomeRoadsRegression`, which `DataRegression` already runs — no
`DataRegression.cs` edit (and this lane does not touch that file). If the lead wants the standalone reach
probe wired into a chain instead of run by hand, the entry point is
`DeNelle.Editor.Regression.BiomeRoadsDropReachProbe.RunStandalone` — **recommended NOT to register it in
`DataRegression`**: it opens a scene, and it can legitimately report SHORT for a bake decision no gate
should own.

### Gate hygiene (this lane's checks)
```
python tools/gate_brace.py <3 files>  ->  GATE_BRACE_SUMMARY bad=0 of 3   (exit 0)
NUL scan: 0 NUL bytes in all three; raw braces 170/170, 153/153, 32/32
```

---

## 3. What was NOT done, and why

- **No Unity, no gate, no commit, no push** — per the brief. `COMPILE_GATE_OK` / `REGRESSION_OK` are the
  lead's to run; nothing here is proven to compile, only brace/NUL/structure-checked.
- **The requested "arm ids vs JSON" regression was not added** — it already exists as
  `BiomeRoadsRegression` Case 2 and is green on HEAD, so it could not be RED and a duplicate would be a
  second copy of live state (CLAUDE.md §2/§5's own failure mode). Case 8 pins the defect that IS red.
- **The reach probe is not registered in `DataRegression`** — reasoning above.
- **`HollowRoadsDropInjector.cs:672` (now shifted) still cites `HeroLocomotion.cs:1391
  const float PlayableHalf = 50f`, which NO LONGER EXISTS** — `HeroPlayableBoundsRegression.cs:204-218`
  pins its absence (WO-1094 replaced the typed bound with a measured one). The `clampSignature` branch is
  therefore dead and its sentence would name a clamp that is gone: a diagnostic that misroutes the next
  reader, which is the exact failure that file's own header rails about. **Left alone deliberately** —
  it is diagnostic-only, out of this ticket's scope, and changing an alarm's wording without a run to see
  it fire is how the wrong words get baked in again. **Recommend a small follow-up ticket.**
- **The owner's "the roads and everything disappeared … paths and better floor coverings"** is TOWN
  dressing in `Main_Castle_Overworld`, not this system (BiomeRoads' "roads" are the four Rootways tunnel
  arms). Name collision only. Needs its own ticket.

## 4. Unproven, named as unproven

- **Whether the hub's baked navmesh reaches ±400 m at all.** Not measured — this lane runs no Unity. If
  the bake stops short, the hub-side probe will refuse those roads for a TRUE reason and the arms still
  dead-end; the difference is that the log will then name the reach, from the authority that can see the
  mesh. `BiomeRoadsDropReachProbe.RunStandalone` closes this in one command.
- **Terrain height at the four derived points** — same: measured by that probe, not by me.
- **That the fix opens the four roads on device.** Not proven. It removes an impossible question and asks
  an answerable one; only a device or headless run of hub → tunnel proves the doors appear. PO felt-verify
  per §13.
- **Whether `RememberHubBounds` is reached on every route into the tunnel** (it is called from
  `DungeonWorldPortalSpawner.cs:499-505`, i.e. the hub's portal-spawn path). If some route enters the
  tunnel without the hub ever spawning portals, the memo is empty — the new refusal says exactly that
  ("the hub NEVER RAN its grounding pass"), so the next capture answers it instead of hiding it.

### Checked at source before hand-back (were on this list, now closed)

- **Is the hub navmesh registered when the grounding pass runs?** YES, and the call site was already
  built to guarantee it. `DungeonWorldPortalSpawner` self-bootstraps at
  `RuntimeInitializeOnLoadMethod(AfterSceneLoad)` (`:275-288`), re-arms on `sceneLoaded` (`:290-297`), and
  its `TryPlace()` (driven from `Update`) **checks `NavMesh.CalculateTriangulation()` has vertices and
  RETRIES until it does** before it reaches `TryGetAuthored` → `TryDeriveHollowRoadsPortal` →
  `RememberHubBounds`. So the grounding pass cannot run before the mesh exists.
- **Where the capture's scene label comes from:** `BreakCaptureHarness.cs:902`,
  `result.Scene = SceneManager.GetActiveScene().name` — the ACTIVE scene at capture time, not a label the
  injector supplies. That is what makes `"scene":"dg_hollow_roads"` on all five entries decisive.
- **`BiomeRoadsRegression` registration:** already present at `Assets/Editor/Regression/DataRegression.cs:670`
  (`Guard.Try("Regression", "biome-roads suite", …)`). Case 8 therefore runs with no `DataRegression` edit.
- **`Terrain` resolves in `DeNelle.Village`:** `Assets/_Modules/Village/DeNelle.Village.asmdef:37`
  `"noEngineReferences": false`.

### Two hand-back items for the lead

1. **`BiomeRoadsDropReachProbe.cs` has no `.meta`** — Unity generates it on the next editor/gate run.
   Generate it before committing, or the file takes a fresh GUID on another machine.
2. **Not registering the reach probe in `DataRegression` is a DEVIATION from the brief**, which asked for
   "a regression that samples all four derived points against the shipped scene's navmesh data or terrain
   height". My reasons are in §2/§3 (it opens a scene; a short bake is an owner call, not a gate's), but
   this is **the lead's call, not settled by me** — say the word and it becomes a suite case.

---

## 5. Paths

- WO: `WorkOrders/WORK_ORDER_1692_hollow_roads_biome_drops_refuse_to_seat.md` (**Status: IMPLEMENTED**)
- RESULT: `WorkOrders/WORK_ORDER_1692_hollow_roads_biome_drops_refuse_to_seat.RESULT.md` (this file)
- Code: `Assets/_Modules/Village/World/HollowRoadsDropInjector.cs`
- Oracle: `Assets/Editor/Regression/BiomeRoadsRegression.cs` (Case 8)
- Instrument: `Assets/Editor/Regression/BiomeRoadsDropReachProbe.cs`

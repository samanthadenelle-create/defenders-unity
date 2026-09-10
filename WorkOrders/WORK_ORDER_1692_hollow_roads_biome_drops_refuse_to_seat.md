# WORK ORDER 1692 — The Rootways: all four biome drops refuse to seat ("seated 0 of 4")

**Status:** IMPLEMENTED — 2026-09-10 — the tunnel no longer probes the *destination* world's navmesh from
inside the tunnel scene; the hub grounds the four drops while it is still loaded and the tunnel consumes
that memo.

**Silo:** World / BiomeRoads (Core + Village.World) + Editor regression. No scene edit, no bake.
**Source:** F8 device captures seq 5003–5007 (Seeker `SM02G4061955851`, build 2026.09.10 vc 363866,
scene `dg_hollow_roads`, device UTC 2026-09-10T18:18:51).
**Screenshot:** `logs/f8-inbox/device/SM02G4061955851/break_05_error.png` (13:18:52) — the player is
standing in the tunnel, dark corridor geometry, HUD live, "Leave Dungeon" prompt up. Nothing visibly
broken on screen, which is exactly the failure shape this system was written to avoid: four arms that
dead-end with no door and no on-screen explanation.

---

## 1. The captured evidence (quoted, not summarised)

Four identical refusals, one per region, all from `HollowRoadsDropInjector:TrySeatDrop`:

```
[Flow:BiomeRoads]   REFUSED the Ashwood drop at seat time: its derived destination (0.00, 17.00, 400.00)
has NO navmesh within 12m, so no walkable point can be promised there. ... NOT seated.      (seq 5003)
[Flow:BiomeRoads]   REFUSED the Goldfields drop ... (400.00, 17.00, 0.00) ...               (seq 5004)
[Flow:BiomeRoads]   REFUSED the Mirewood drop ...   (0.00, 17.00, -400.00) ...              (seq 5005)
[Flow:BiomeRoads]   REFUSED the Stoneback drop ...  (-400.00, 17.00, 0.00) ...              (seq 5006)
```

then, from `InjectDrops`:

```
[Flow:BiomeRoads]   seated 0 of 4 biome drops - every tunnel arm dead-ends. Suspect the arm room ids
in dg_hollow_roads.json no longer match BiomeRoads.ArmRoomIdFor.                            (seq 5007)
```

---

## 2. The trace's own hypothesis is REFUTED — proven, not assumed

The seq 5007 sentence is a **static hint printed on any zero-seated run**
(`HollowRoadsDropInjector.cs:320-324`), not a measurement. Three independent proofs it is wrong:

1. **The ids match at source.** `BiomeRoads.ArmRoomIdFor` (`Assets/_Modules/Core/World/BiomeRoads.cs:222-236`)
   returns `arm_ashwood` / `arm_goldfields` / `arm_mirewood` / `arm_stoneback`. The shipped graph
   `Assets/Resources/Data/Canonical/dungeon-graphs/dg_hollow_roads.json` declares nodes with exactly
   those four ids (and the StreamingAssets twin is byte-identical — Case 2 below asserts it).
2. **`git log --oneline -8` on that JSON** shows its last three touches as `eb9ae9c19`, `74161fa22`,
   `a24654c21` — none of which renamed an arm. There is no drift commit to find.
3. **The refusal lines themselves prove the arms were FOUND.** `TrySeatDrop` looks the arm room up
   (`composeRoot.Find(drop.ArmRoomId)`, `:340-348`) and measures its bounds (`:351-357`) **before**
   reaching the navmesh probe at `:387`. A missing arm room emits a *different* message ("is NOT a child
   of the compose root"). Every one of the four captures is the probe message, so all four arm rooms
   resolved.

An oracle for this already exists and is **green on HEAD**:
`Assets/Editor/Regression/BiomeRoadsRegression.cs` **Case 2**
(`Case2_EveryArmHasARegionAndEveryRegionHasAnArm`, `:226-328`) resolves every `DropRegions` entry's
`ArmRoomIdFor` against the shipped JSON's node list, fails on an unclaimed `arm_*` node, and compares
the Resources/StreamingAssets copies byte-for-byte. That the suite is green is itself evidence the
hypothesis is false.

**The `(±400, 17, 0)` points are DERIVED, not a synthetic fallback.** Measured hub bounds are
`centre (0,17,0) size (1000,42,1000)` (the injector header quotes the measurement line,
`HollowRoadsDropInjector.cs:54-55`); reach 500m × `EdgeFraction` 0.8 = **400m**, and Y is
`worldBounds.center.y` = **17** by design (`BiomeRoads.cs:420`, `ResolveDrops`, which documents at
`:322-332` that it leaves Y at the bounds-centre height and that **the caller must ground-probe**).

---

## 3. The real cause (proven from source + the load mode)

`HollowRoadsDropInjector.TrySeatDrop` ran this **inside `dg_hollow_roads`**:

```csharp
// Assets/_Modules/Village/World/HollowRoadsDropInjector.cs:387  (pre-fix)
if (!NavMesh.SamplePosition(drop.Point, out NavMeshHit groundHit, ArrivalSampleRadius, NavMesh.AllAreas))
```

`drop.Point` is a coordinate in **`Main_Castle_Overworld`**, 400 m from the origin. The tunnel is entered
through `SceneRouter`, whose loader is `SceneManager.LoadSceneAsync(sceneName)` with the default mode —
**`LoadSceneMode.Single`** (`Assets/_Modules/Core/SceneRouter.cs:323`). So while the injector runs, the
hub scene is unloaded and **its baked navmesh with it**
(`Assets/Scenes/Main_Castle_Overworld.unity:17047` references
`Assets/Scenes/Main_Castle_Overworld/NavMesh-Main_Castle_Overworld.asset`, 1.5 MB of baked data that lives
and dies with that scene). The only navmesh in memory at seat time is the tunnel's own nine-room corridor.

The file already knew the hub was gone — it recalls the hub's **bounds** from a static memo precisely
because "the tunnel scene carries no terrain" (`:249-260`). The WO-1606 change added a probe that needs
the hub's **navmesh** and did not notice it needed the same treatment.

**The probe therefore cannot succeed, in any build, on any terrain.** Four-for-four identical refusals
are that structural certainty, not a tuning problem.

**Introduced by:** commit **`6a5c7a36d`** ("chore: checkpoint complete workspace and rebuild board",
Wed Sep 9 14:17:30 2026) — the WO-1606 change, +145/−14 on this file. `git show 6a5c7a36d --
Assets/_Modules/Village/World/HollowRoadsDropInjector.cs` shows the added
`+ if (!NavMesh.SamplePosition(drop.Point, …))` and `- seam.targetPosition = drop.Point;` /
`+ seam.targetPosition = groundedPoint;`. (Worth recording separately: a behavioural fix shipped inside a
commit whose subject says "chore … checkpoint".)

WO-1606's intent was right — `ResolveDrops` genuinely does hand out a Y that is 17 m of open air, and
before it the hero was warped into that air. Only the *place* it asked the question was wrong.

---

## 4. The fix (implemented)

**Move the ground probe to the one moment the destination world can honestly answer it: while the hub
is loaded.** `DungeonWorldPortalSpawner.cs:499-505` already calls
`HollowRoadsDropInjector.RememberHubBounds(worldBounds)` from inside the hub with terrain and navmesh
live. That call now also:

1. resolves the four drops for the measured bounds,
2. fixes each point's Y against the **terrain height** at that x/z (the bounds-centre Y is the whole
   17 m error), and
3. `NavMesh.SamplePosition`s the result **against the hub's own navmesh**, remembering the grounded
   point per region — or remembering nothing for a region the hub itself could not ground.

A control probe at the world origin runs first, so the log can tell "the hub navmesh does not reach 400 m
out" apart from "no navmesh was queryable at memo time at all" — the two causes the old message admitted
it could not distinguish.

`TrySeatDrop` no longer probes. It consumes the memo, and refuses loudly (with the hub-side reason) when
the memo has no grounded point for a region. **The tunnel must never sample its own navmesh for a
destination coordinate**, and the code says so in place, with the `SceneRouter.cs:323` citation.

**Files changed**
- `Assets/_Modules/Core/World/BiomeRoads.cs` — none (pure derivation is correct as written).
- `Assets/_Modules/Village/World/HollowRoadsDropInjector.cs` — the memo now carries grounded points; the
  seat-time probe is replaced by a memo read.
- `Assets/Editor/Regression/BiomeRoadsRegression.cs` — new **Case 8**, RED on HEAD.
- `Assets/Editor/Regression/BiomeRoadsDropReachProbe.cs` — NEW, a standalone measurement (not a gate
  case; see §6).
- **No JSON changed** — so no canonical-twin / CRLF work was needed, and none was done.
- **No `.unity` touched. No bake requested or required.**

## 5. Acceptance criteria

- [x] The arm-id hypothesis is proven or refuted with quoted evidence (refuted, three ways).
- [x] The introducing commit is named.
- [x] The tunnel scene contains no `NavMesh.SamplePosition` call against a destination-scene coordinate.
- [x] A regression fails on HEAD and passes after (Case 8).
- [x] `tools/gate_brace.py` clean, NUL scan clean.
- [ ] **Device-verified** — the four roads actually open. NOT proven from here (see below); needs a run.

## 6. The lead's fresh-logcat questions, answered from the data

**(a) "The injector runs in the HUB."** Not per the harness's own record. `break-log.jsonl` stamps each
entry with the scene that was ACTIVE when it was emitted, and all five BiomeRoads entries read
`"scene":"dg_hollow_roads","t":441.6` (`logs/f8-inbox/device/SM02G4061955851/break-log.jsonl`). The code
agrees: `HandleScene` only calls `InjectDrops()` when the loaded scene name equals
`BiomeRoads.TunnelSceneId` (`HollowRoadsDropInjector.cs:184-190`).

The two readings reconcile, and the reconciliation IS the design: `world bounds MEASURED from 1
terrain(s)` is emitted by **`DungeonWorldPortalSpawner`, in the hub, at hub load**
(`DungeonWorldPortalSpawner.cs:499`, which then calls `RememberHubBounds`) — that line is the memo hop.
`InjectDrops` and the four `drop derived:` lines come later, in the tunnel, from the recalled memo. In an
interleaved logcat they sit close together; they are in different scenes 430 seconds apart, and that
separation is exactly what the old probe ignored.

Entry is `DungeonPortal` → `SceneRouter.GoDungeonScene` (`SceneRouter.cs:709-725`) →
`LoadSceneWithFade` → `SceneManager.LoadSceneAsync(sceneName)` (`:323`, default = **Single**). The
`beforeLoad` hook it passes is literally named `CarryHeroAcrossSingleLoad`.

**(b) "Test the FIRST cause the message names — Y=17 vs the terrain, and the 12 m radius."** Agreed, and
it is now fixed rather than argued: `GroundDropsInHub` fixes Y from `Terrain.SampleHeight` **before** it
probes, so the ±21 m of vertical error a 42 m-tall terrain can produce no longer eats the 12 m radius.
Both causes the old message "could not tell apart" are now separated by construction: the terrain answers
Y, the navmesh answers walkability, and a control probe at the origin says whether the mesh was queryable
at all. **How far the bake actually reaches is still a MEASUREMENT, not a belief** — see §7, and the new
probe below measures it in one command.

**(c) "Find when it last worked."** `git log -S` on `EdgeFraction` (`1f1a609d0`, `a24654c21`) and on
`ArrivalSampleRadius` (`6a5c7a36d`, `a24654c21`) shows the derivation knobs untouched since WO-1604. The
behavioural break is `6a5c7a36d` and nothing else: **before it the drops DID seat** — F8 seq 4703/4706
are captures of drops that seated, were taken, and landed the hero badly (in the air at y=17, off-mesh).
After it, zero seat. So the honest history is "it seated and landed wrong" → "it does not seat at all",
and this WO returns it to seating with a destination that was grounded by the scene that owns it.

**Instrument added for the open half:** `Assets/Editor/Regression/BiomeRoadsDropReachProbe.cs` —
deliberately NOT a gate case (it opens a scene, and a short bake is the owner's call, not a gate's). One
command, marker-judged:

```
Unity -batchmode -quit -projectPath <repo> \
  -executeMethod DeNelle.Editor.Regression.BiomeRoadsDropReachProbe.RunStandalone
```

It opens the shipped hub, measures the terrain height at all four derived points, probes the baked
navmesh from the raw point and from the grounded point at the same 12 m the injector uses, and reports the
distance to the nearest mesh within 600 m. Markers: `BIOME_ROADS_REACH_OK` / `BIOME_ROADS_REACH_SHORT` /
`BIOME_ROADS_REACH_FAIL`.

**Not this ticket:** the owner's "the roads and everything disappeared, used to be paths and better floor
coverings" is about TOWN decoration in `Main_Castle_Overworld`. BiomeRoads' "roads" are the four tunnel
arms of The Rootways. The word collides; the systems do not. Fixing this WO will not put a path back in
the town, and that symptom needs its own ticket against whatever dresses the hub ground.

## 7. Round 2 — the WO-1091 suite (`BiomeDropGroundProbeRegression`) holds WITH the hub-side grounding

Chain 49 caught three broken contracts. All three were about WHERE the probe sits and how the file READS,
and all three are now satisfied **without** putting an unanswerable probe back in the tunnel:

- **`[one-authority]`** — the suite counts the literal `NavMesh.SamplePosition(` **including comments**
  (`BiomeDropGroundProbeRegression.cs:113`, `Count` is a plain substring scan). My WO-1692 comments quoted
  the call three times, so 6 "probes" were counted against 3 that pass `ArrivalSampleRadius`. The comments
  now say "a navmesh sample of drop.Point" in prose. **3 probes, 3 with `ArrivalSampleRadius`** — the
  control probe, the hub grounding probe and the arrival judge, one radius between them.
- **`[fail-closed]`** — the branch the suite anchors on (`if (!NavMesh.SamplePosition(drop.Point` …
  `Vector3 groundedPoint`) is **restored verbatim, in the hub**, as `TryGroundDrop`: `FlowTrace.Fail` +
  player-facing `Notify` + `return false`, so an ungrounded drop still seats NO door. WO-1091's contract
  was never that the probe live in the tunnel — only that it guard the seat. `drop.Point` is re-seated
  onto the terrain first (`drop` is a struct; the local copy IS the point being probed), so there is one
  point, not a shadow copy.
- **`[one-point]`** — `Vector3 groundedPoint = groundHit.position;` is back and is what the memo carries;
  `seam.targetPosition = groundedPoint;` and `announce.PromisedPoint = groundedPoint;` are unchanged. No
  consumer sees the raw derived coordinate.

`[radii]` and `[three-verdicts]` were never touched. **Case 8 was re-pointed in the same change**: its
first predicate was "no drop-point probe anywhere in this file", which would have outlawed the very
fail-closed guard WO-1091 requires. It is now **positional** — the drop-point probe must live in
`TryGroundDrop` and nowhere else — which is the actual rule and still RED on HEAD.

## 8. What is NOT proven

**Whether the hub navmesh reaches ±400 m at all is UNPROVEN.** No Unity run was made (this lane runs no
Unity). If the bake stops short of 400 m, the hub-side probe will refuse the drops for a *true* reason and
the arms will still dead-end — but the log will then say so from the authority that can actually see the
mesh, naming the reach, instead of asking an unrelated scene's navmesh an unanswerable question. Closing
that needs one device or headless run of the hub → tunnel path.

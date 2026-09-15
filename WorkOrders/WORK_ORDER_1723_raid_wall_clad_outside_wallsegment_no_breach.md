# WORK ORDER 1723 — ROOT CAUSE: the visible raid wall is a SIBLING of the WallSegment, so a destroyed wall neither opens a navmesh hole nor changes visually

**Status: LANE A IMPLEMENTED, NOT YET GATED (nav half: clad excluded from NavigationStatic) / LANE B IMPLEMENTED, NOT YET GATED OR BAKED (visual half: panel-matched partition + clad re-parented under its WallSegment + rubble swap on collapse + ordered-panel highlight — see the Lane B section of the .RESULT.md; ⚠ ONE OPEN OWNER QUESTION, RESULT §B6: the gate opening narrows from ~8.3 m to ~3.9 m as a consequence of the Q1 partition ruling) / LANE C RE-BAKE OUTSTANDING (§11.6, ship-blocking — and now REQUIRED by Lane B, which changes the segment count)**
**Minted:** 2026-09-14, from the owner's report: *"in a raid I select breach, and target the wall, it shows
attacked, then moves to next wall segment, however nothing happens as far as being able to travel through the
hole. There is no visual change whereas I would think you could see that wall segment destroyed."*
**Build under test:** `2026.09.14.369984` (`ProjectSettings/ProjectSettings.asset` `bundleVersion`, read at
source) / the 17:2x capture window stamps `2026.09.14.369935`. Seeker, `RaidBase_raider_camp_small`.
**Silo:** raid wall bake + navmesh (`Assets/Editor/RaidNavBake.cs`, `Assets/Editor/WallTools/RaidBaseDresser.cs`,
`Assets/_Modules/Village/Walls/WallSegment.cs`). **Supersedes the framing of WO-1721 and WO-1722 §1/§2** — see §6.

---

## 1. THE ROOT CAUSE, IN ONE SENTENCE

**The wall the player sees (`Zone_Clad/Clad_*`) is not part of the `WallSegment` GameObject hierarchy, so
the two systems that must treat a wall as destructible — the navmesh bake's exclusion test and the collapse
sink — both walk that hierarchy, both miss the clad, and both silently do nothing to it.** The segment dies
correctly; the wall the player is looking at, and the navmesh hole she needs to walk through, are untouched.

This is ONE defect with TWO player-visible halves. Fixing the parenting/exclusion fixes both.

---

## 2. THE PROOF — every line read or measured this session

### 2.1 The clad ring is instantiated OUTSIDE any WallSegment

`Assets/Editor/WallTools/RaidBaseDresser.cs:527`
```csharp
var parent = EnsureZone(root, "Zone_Clad");
```
`:557` — every visible panel is instantiated under that zone, with colliders stripped:
```csharp
var go = InstantiateVisual(model, parent,
    "Clad_" + s + "_" + run + "_" + i + "_R" + radius.ToString("F2"), pos, rot, true);
```
The last argument is `stripColliders` (signature at `:262-263`), so the clad carries **no colliders** — the
only collider on a raid wall is the `WallSegment`'s own `BoxCollider`.

`SyncWallColliderHeight` (`:587-620`) reaches back and stretches each `Wall_<ring>_S*` BoxCollider's **Y** to
the clad's achieved height. That is the entire relationship between the two hierarchies: one number. It never
re-parents the clad, never links a clad panel to a segment, and (by its own docstring) never touches X/Z.

### 2.2 The navmesh bake flags the clad as permanent, non-destructible geometry

`Assets/Editor/RaidNavBake.cs:66-77` — the generic marking pass:
```csharp
foreach (var r in root.GetComponentsInChildren<Renderer>(true))
{
    var flags = GameObjectUtility.GetStaticEditorFlags(r.gameObject);
    bool destructible = r.GetComponentInParent<WallSegment>() != null ||
        r.GetComponentInParent<DefenseTower>() != null;
    GameObjectUtility.SetStaticEditorFlags(r.gameObject, destructible
        ? flags & ~StaticEditorFlags.NavigationStatic     // excluded — runtime carving owns it
        : flags | StaticEditorFlags.NavigationStatic);    // BAKED IN as permanent geometry
}
```
`GetComponentInParent<WallSegment>()` walks **up**. A `Clad_*` panel's parent chain is
`Zone_Clad -> <base root>` — no `WallSegment` anywhere on it. So `destructible` is **false** for every visible
wall panel, and the entire visible wall ring is **baked into the navmesh as a solid, permanent obstruction.**

`PrepareDestructibleWalls` (`:157-184`) is the guard that was supposed to prevent exactly this, and its scope
is the bug:
```csharp
foreach (var child in wall.GetComponentsInChildren<Transform>(true))   // :165 — the SEGMENT's own children
    GameObjectUtility.SetStaticEditorFlags(child.gameObject, flags & ~StaticEditorFlags.NavigationStatic);
```
It strips `NavigationStatic` from the segment's own subtree only. The clad is a sibling; it is never reached.

⛔ **The file's own comment, `RaidNavBake.cs:154-156`, states this exact failure as a known hazard:**
> *"Bake continuous ground beneath breakable walls. Their live carving obstacles block intact walls and are
> disabled by WallSegment.Collapse after destruction. **Baking wall geometry itself leaves a permanent hole
> even after its collider dies.**"*

The author understood the hazard and wrote the guard for the hierarchy the visible wall used to live in. The
clad ring moved out of that hierarchy and the guard never followed.

**Consequence:** the `WallSegment`'s runtime carving obstacle is a *second, redundant* blocker over ground the
clad has already removed from the navmesh at bake time. `Collapse()` lifts the carve; the clad's baked hole
stays. Forever.

### 2.3 Measured, on the device: 98.3% of collapsed walls leave NON-WALKABLE ground

`TroopController.TraceBreachProbe` (`Assets/_Modules/Village/Troops/TroopController.cs:1378-1394`) samples the
dead wall's last-live position at the troop's foot height with a 0.60 m radius, on the `foe-died` rescan.
From `logs/device/pull-20260914-172704-breach-success-playthrough/logcat_full.txt` (191,302 lines):

```
09-14 17:27:15.483 [Flow:TroopAI] id=troop-footman role=melee BREACH: structure 'Wall_Outer_SS_15(WallSegment)'
  died -> reacquired 'Wall_Outer_SS_14(WallSegment)' kind=struct
  holeNavmesh=NOT-WALKABLE>0.60m selfNavmesh=on endSample=hit@1.3m
  routeStatus=PathComplete corners=12 straightLine=2.1m pathLength=29.4m
```

| `holeNavmesh=` verdict | count |
|---|---|
| `NOT-WALKABLE>0.60m` | **645** |
| `WALKABLE@0.00m` | 6 |
| `WALKABLE@0.21m` | 5 |
| total probes | **656** |

`selfNavmesh=on` rules out "the troop is off-mesh". `straightLine=2.1m` vs `pathLength=29.4m` says the route to
a panel **2 metres away** still went the long way round the entire ring. **There is no hole.** That is the
owner's *"nothing happens as far as being able to travel through the hole"*, measured.

### 2.4 Measured: troops freeze outside the ring and the retarget timer flips them

`logs/device/watch-wall-collision-check/logcat_live.txt` (the most recent capture, 17:52–18:13, 21 minutes,
**zero `[Flow:WallSegment]` damage lines in the whole file**):
```
line 2:   [Flow:TroopAI] id=troop-footman role=melee ENGAGED foe='Wall_Outer_SE_10(WallSegment)' kind=struct
          dist=32.0m attackRange=2.9m inRange=False moved=0.00m/s commanded=4.0 agent=onNavMesh retargets=19
line 313: (+4.6s) dist=32.0m ... moved=0.00m/s commanded=4.0 agent=onNavMesh
line 539: (+7.6s) dist=32.0m ... moved=0.00m/s commanded=4.0 agent=onNavMesh
```
Verified independently by the lead: 56 samples at `dist=32.0m moved=0.00m/s`, 17 at `dist=35.7m moved=0.00m/s`,
constant to 0.1 m. An archer with `attackRange=19.6m` is frozen at the same 32.0 m. `commanded=4.0` m/s,
`agent=onNavMesh`, zero movement — a troop with no route, standing still.

The retarget timer then flips them wall<->spire every ~2.2 s, **every single one `reason=timer`**:
```
line 85:  RETARGET#19 reason=timer dropped='Wall_Outer_SE_10(WallSegment)' -> won='RaidSpire(RaidSpire)'
line 229: RETARGET#20 reason=timer dropped='RaidSpire(RaidSpire)' -> won='Wall_Outer_SE_10(WallSegment)'
line 384: RETARGET#21 reason=timer dropped='Wall_Outer_SE_10(WallSegment)' -> won='RaidSpire(RaidSpire)'
line 535: RETARGET#22 reason=timer dropped='RaidSpire(RaidSpire)' -> won='Wall_Outer_SE_10(WallSegment)'
```
**That flip is what the owner is describing as *"it shows attacked, then moves to next wall segment"*.**

Corroborating, from the big capture: ordered wall `Wall_Outer_SW_11` was the standing focus for ~54 seconds at
a constant `hp=100` and has **not one `[Flow:WallSegment]` line in 191,302 lines** — it never took a single
point of damage while it was the ordered target.

`routeOpen=True` appears **0 times in 2,517 `[Flow:RaidAI]` lines across every capture**; `routeObj=PathPartial`
on 98%+. The raid navmesh never presents a complete route — consistent with a permanently sealed ring.

### 2.5 Measured: the collapse sink cannot move the visible wall, and the code says so

`WallSegment.CollapseRoutine` (`Assets/_Modules/Village/Walls/WallSegment.cs:436`):
```csharp
var renderers = GetComponentsInChildren<Renderer>(true);
```
`GetComponentsInChildren` walks **down**. The clad is a sibling. The sink physically cannot reach it.

⛔ **`WallSegment.cs:475-477` predicts this outcome verbatim, as the instrumentation's own contract:**
> *"the terminal proof. Absence of this line on a segment that logged COLLAPSED is the collapse-without-visual
> desync; presence means the ruin really did sink and the symptom lies elsewhere (**e.g. the art is a sibling
> the sink never moved**)."*

The settle line is **present on 80 of 80 collapses** in the capture:
```
09-14 16:49:40.356 [Flow:WallSegment] WallSegment 'Wall_Outer_SS_17' (Hostile) COLLAPSED: 1 solid collider(s)
  and 1 carving obstacle(s) dropped
09-14 16:49:40.356 [Flow:WallSegment] WallSegment 'Wall_Outer_SS_17' collapse tell STARTED over 11 renderer(s)
09-14 16:49:41.290 [Flow:WallSegment] WallSegment 'Wall_Outer_SS_17' ruin SETTLED at y=-28.79 (fell 28.79m)
```
**By the code's own stated rule, 80/80 settle lines means the symptom is "the art is a sibling the sink never
moved."** The instrumentation named the cause in advance; it only needed reading.

What the 11 sunk renderers actually are is named by the log too — `[Flow:DamageVis]`, fired 80x:
> *"scuff renderers resolved for 'Wall Section' (wall): driving 0 of 6 renderer(s) under the host - 5 are
> effects or mesh-less ..., **1 are hidden body meshes (baked twins behind an injected skin: invisible**, not a
> coverage hole)"*

The segment's own meshes are **invisible**. The wall the player sees is the clad. So the sink moves nothing she
can see, and the wall reads as untouched.

### 2.6 The owner's own screenshot, which is the whole ticket in one frame

`logs/device/pull-20260914-owner-screenshots/Screenshot_20260914-173743.png`, opened by the lead:
- HUD reads **`Razed 28%`** and **`Troops 4/13`**.
- The toast **"Breach ordered - the warband hits that section."** is live on screen.
- The wall in front of the hero is **completely unbroken, edge to edge** — no gap, no rubble, no breach
  anywhere along its full visible length.

More than a quarter of the base is destroyed and the wall looks pristine. That is §2.5, photographed.

(`Screenshot_20260914-172741.png`, 12 minutes earlier: `Razed 15%`, a troop striking the wall, the wall again a
continuous unbroken run.)

---

## 3. HOW THIS PRODUCES EXACTLY WHAT THE OWNER REPORTED

| Her words | Mechanism, with the proving line |
|---|---|
| *"it shows attacked"* | Two of the four tells fire **attacker-side, unconditionally**: the "Breach ordered" toast confirms the ORDER, not any damage (`RaidDeployController.cs:969`), and the swing VFX/animation run after `TakeDamage` returns without checking its outcome (`TroopController.cs:1449-1452`). Honest tells (dust/number/HP bar) are wall-side. So "shows attacked" is fully reproducible with zero damage landing. |
| *"then moves to next wall segment"* | Either the ordered wall genuinely collapsed and the order self-clears by design (`TroopBreachOrder.cs:85-89`), **or** — in the frozen case — the 2.2 s `reason=timer` retarget flips the warband wall<->spire forever (§2.4). Both look identical from the camera. |
| *"nothing happens as far as being able to travel through the hole"* | **§2.2 + §2.3.** The clad is baked into the navmesh as permanent geometry; a collapsed segment lifts only the redundant runtime carve. 645/656 probes: `NOT-WALKABLE`. There is no hole, and there never will be. |
| *"There is no visual change ... you could see that wall segment destroyed"* | **§2.1 + §2.5.** `Collapse()` sinks only the segment's own, **invisible**, renderers. The visible clad panel is a sibling and never moves. `Razed 28%`, wall unbroken (§2.6). |

---

## 4. THE FIX

**Architecture ruling (HP B2B, `docs/ARCHITECTURE_PRINCIPLES.md`): a destructible thing must have ONE owner.
Today a raid wall has two hierarchies and neither one owns both its collision and its appearance.** Close that,
do not paper over it with a second exclusion list that will drift the same way.

### 4.1 Preferred: parent each clad panel under the WallSegment it clads

In `RaidBaseDresser.CladRing` (`:517-578`), after `FitPieceAlong`, resolve the `Wall_<ringPrefix>_S*` segment
whose footprint contains the panel's centre and `SetParent` the panel to it (worldPositionStays: true).

This one change makes every existing mechanism correct with no other edit:
- `RaidNavBake.cs:69-76`'s `GetComponentInParent<WallSegment>()` now returns non-null -> the clad is
  **excluded** from the bake -> the ground under the wall is baked walkable -> dropping the carve on collapse
  **really opens a hole**.
- `PrepareDestructibleWalls`'s strip loop (`:165`) now reaches the clad for the same reason.
- `WallSegment.CollapseRoutine`'s `GetComponentsInChildren<Renderer>()` now finds the **visible** mesh -> the
  wall the player sees actually collapses.
- `SyncWallColliderHeight` (`:587-620`) can be simplified or kept; it becomes a local measurement.

⚠ **The partitions do not currently align.** Clad panels are laid out at the art module's measured width
(`piece`, ~4 m, `RaidBaseDresser.cs:526`, `:549`); segments are laid out against `MaxSegmentWidth = 3.0f`
(`RaidBaseGenerator.cs:197`). So one clad panel can straddle two segments. **This is an owner-facing decision,
not the lane's** — see §7 Q1. The cheapest correct answer is to drive BOTH partitions from one number so a
panel maps 1:1 to a segment; that also retires the WO-1722 X/Z collider mismatch as a side effect, because the
collider would then be re-derivable from the panel it owns.

### 4.2 Minimum viable, if §4.1 is judged too large for one pass

Extend the `destructible` test at `RaidNavBake.cs:69-76` to also exclude anything under `Zone_Clad`, AND give
`WallSegment` an explicit serialized list of the clad renderers it owns for `CollapseRoutine` to sink.
⛔ **State plainly in the WO result that this is a second copy of the ownership relationship** — exactly the
duplicated-state class CLAUDE.md §2/§5/§8/§16 each document going stale. Prefer §4.1.

### 4.3 The destroyed-wall visual (folds WO-1721 in, and its owner rulings still stand)

Once the visible mesh is reachable, apply the owner's verbatim rulings already recorded on WO-1721:
1. *"if wall is destroyed remove the destroyed wall and replace with a destroyed wall with no colider that I
   can step over"* — swap to a distinct rubble model, do not sink the same mesh.
2. *"that I can step over"* — any residual collider must be low/steppable.
3. *"if there is not navmesh should be a navlink"* — **§2.3 now PROVES the gap exists** (645/656 NOT-WALKABLE),
   so the "prove it first" precondition on that ruling is satisfied. But prove it again **after** §4.1: if the
   clad leaves the bake, the ground under the wall should be walkable and no `NavMeshLink` is needed. Add one
   only if a post-fix capture still reads `NOT-WALKABLE`.

⛔ **The sink distance is also wrong and must not be reused as-is.** `CollapseRoutine` derives the drop from
encapsulated renderer bounds (`WallSegment.cs:453-461`) — measured settle depths in this capture run
**y=-2.55 (30 walls) to y=-28.79** for a wall whose authored box is `SegSize = (1.5, 3.0, 1.5)`
(`RaidBaseGenerator.cs:202`). A 4 m wall dropping 28.79 m is the "15% visible rubble" design producing nothing
visible at all. §4.3's model swap replaces this path; do not tune it.

---

## 5. INSTRUMENT FIRST (CLAUDE.md §12) — before and after

The existing traces already answer most of this; two gaps cost the previous sessions their day.

1. **Re-sample the hole ~1 s after collapse.** `TraceBreachProbe` fires on the same tick as `Collapse()`, so a
   `NOT-WALKABLE` verdict cannot today distinguish "never baked walkable" from "carve-restore latency". Emit a
   second delayed line `holeNavmesh@+1s=`. **This is the one line that proves §4.1 worked** — run it before and
   after. (Read as: still NOT-WALKABLE after the fix = the bake is not the whole story; WALKABLE = fixed.)
2. **The troop attack tick is completely silent.** `TroopController.Attack` (`:1420-1560`) emits no FlowTrace
   at all — there is no line saying "troop X swung at wall Y for Z". Add one throttled
   `id=... SWING target='...' dist=... dmg=... inRange=True`, to pair against `WallSegment`'s existing
   `took ... raw ->` line. Without it, "the troop swung and nothing happened" is unprovable.
3. **Do not strip anything.** Instrumentation is permanent (CLAUDE.md §12, owner ruling 2026-08-09).

---

## 6. WHAT THIS SUPERSEDES — read before touching WO-1721 / WO-1722

- **WO-1722 §2's reframing is WRONG and is retired by this ticket.** It concluded the owner's report was
  "an intact wall whose collider is narrower than its visible mesh, so she walked THROUGH it." Today she reports
  the **opposite polarity** — she *cannot* travel through, and the wall shows no change. Both reports are
  explained by §2.1 without any collider-width mechanism. Do not implement WO-1722 §2.
- **WO-1722 §1 (the `Wall_Outer_SE_17` collider 1.5 m vs renderer 11 m mismatch) is REAL but is a SYMPTOM.**
  The renderer bounds it measured are `GetComponentsInChildren<Renderer>` on the segment — the invisible baked
  twins, not the clad. It resolves as a side effect of §4.1's one-number partition; do not run it as its own lane.
- **WO-1721 is ABSORBED into §4.3.** Its three owner rulings are carried verbatim and still bind. Its
  precondition ("prove the pathing gap before adding a NavMeshLink") is now satisfied by §2.3 — but re-prove
  after §4.1 before adding links.
- **WO-1722 §3 (ToggleBreach logging) stays CLOSED** — that was logcat ring eviction, not a defect.
- `docs/handoffs/RAID_SYSTEMS_REFERENCE_2026-09-14.md` §1.6 states *"Walls are not baked into the navmesh — they
  use runtime carving obstacles instead."* **That is true of `WallSegment` and FALSE of the wall the player
  sees.** Add a correction in the same commit as the fix (CLAUDE.md §15).

---

## 7. OWNER RULINGS — ANSWERED 2026-09-14, Q1 and Q2 are now BINDING

**Q1 — RULED: match the PANELS. Drive the collision segments from the art module's real width (~4 m, ~60
segments), so one visible clad panel maps 1:1 to one destructible `WallSegment`.** `MaxSegmentWidth = 3.0f`
(`RaidBaseGenerator.cs:197`) stops being the wall partition authority; the clad module width (`piece`,
`RaidBaseDresser.cs:526`) becomes it. Consequences the lane must accept, not re-litigate: fewer and wider
segments, so a single breach takes slightly longer to open but reads as one clean panel-sized hole. This also
retires the WO-1722 collider-width mismatch as a side effect, because the collider becomes derivable from the
one panel it owns. ⛔ Do NOT squeeze the art off its authored 4 m module — that is the seam defect WO-1704 was
opened to fix.

**Q2 — RULED: breach mode STAYS ARMED, and the currently-ordered panel gets a clear visual highlight.** Do not
auto-disarm on a successful order (`RaidDeployController.cs:887-976` keeps its current behaviour). Add a
highlight on the ordered `WallSegment` so the player can see which section the warband is actually hitting and
that a re-tap moved it. The highlight is the fix for the silent re-point, not a behaviour change to the order.
⛔ Colour must not be the only carrier of meaning — the owner is red/green colourblind (`SAMANTHA.md` rule 8,
memory `owner-colorblind-delegate-visual-creative`); use shape/outline/pulse as well, and greyscale-check it.

**Q3 — STILL OPEN, and deliberately not asked yet.** Frozen troops (§2.4) hold at a fixed 32.0 m,
`moved=0.00m/s`, indefinitely. Lane A should remove the cause (no route existed). **Re-test before proposing a
stuck-troop failsafe** — a re-deploy/teleport watchdog would mask a real pathing defect, so it needs its own
ruling only if a post-fix capture still shows a frozen troop.

### The questions as they were put (kept for the record)

**Q1 — Wall partition.** §4.1 needs clad panels and `WallSegment`s to map 1:1. Drive both from the art module
width (~4 m, fewer/wider segments, coarser breaches) or from `MaxSegmentWidth = 3.0f` (more segments, finer
breaches, clad panels get cut/scaled to 3 m)? This changes how big a single breach reads on screen.

**Q2 — Breach mode stays armed after a successful order.** `HandleBreachTap` never clears `_breachMode`
(`RaidDeployController.cs:887-976`), so the next world tap re-points the warband. The capture shows this twice
(`v=3 SS_17` -> `v=4 SS_16` 0.83 s later; `v=6 SW_11` -> `v=7 SW_13` 1.12 s later), with the first wall never
falling. Should a successful order auto-disarm Breach, or stay armed with a visible "ordered" highlight on the
target panel? (This is a second, independent contributor to "it moved to the next wall segment".)

**Q3 — Frozen troops.** §2.4 shows troops standing at a fixed 32.0 m, `moved=0.00m/s`, forever. §4.1 should fix
the cause (no route exists). If a post-fix capture still shows a frozen troop, is a stuck-troop failsafe
(re-deploy / teleport to the muster after N seconds of zero movement) wanted, or is that masking?

---

## 8. ACCEPTANCE CRITERIA

- A post-fix device capture shows `holeNavmesh=WALKABLE@...` on collapsed walls — the **645/656 NOT-WALKABLE
  ratio must invert**. Judge by the probe line on a fresh log, never by an exit code.
- A screenshot (opened, not just logged) shows a **visible breach** in the wall where a segment died — the
  §2.6 frame's "Razed 28%, wall unbroken" state must not be reproducible.
- The hero walks through a breached section, captured.
- AI troops path through the breach rather than round the ring: `straightLine` and `pathLength` on the probe
  line are within the `RouteDetourFactor = 1.5f` bound (`TroopController.cs:174`), not 2.1 m vs 29.4 m.
- No troop holds `moved=0.00m/s` with `commanded>0` and `agent=onNavMesh` for more than ~2 s.
- Towers still cannot shoot through an intact wall (existing behaviour, `WallSegment.cs:543-544` — do not regress).
- Raid scenes re-baked via the builder only; **never hand-edit a `.unity`** (CLAUDE.md §3), editor closed.
- Brace + NUL gate on every `.cs` touched, plus `python tools/gate_brace.py` on each.
- WO Status flipped and `.RESULT.md` written in the same hand-back (CLAUDE.md §11).
- `RAID_SYSTEMS_REFERENCE_2026-09-14.md` §1.6 corrected in the same commit (§6).

## 9. NOT IN SCOPE

- Retuning wall HP, tier toughness, or troop damage — nothing here is a balance defect.
- The `[Flow:DamageVis]` scuff-tell loss (`driving 0 of 6 renderer(s)`, 34/34 attempts). Real, long-standing
  (present in the 12:41 build too), surface-damage tell only — its own warn says the health bar and burn ladder
  are unaffected. Separate ticket.
- `Village2` / Iron Bastion / the arena boundary ring.
- The 14 collapses in the capture with no preceding `damage 100/100` line — measured, unexplained, logged here
  so it is not lost. Do not chase it inside this lane.

---

## 10. EVIDENCE INDEX (everything cited above, so the next seat re-opens rather than re-derives)

| What | Where |
|---|---|
| Clad parented to `Zone_Clad`, colliders stripped | `Assets/Editor/WallTools/RaidBaseDresser.cs:527`, `:557`, `:262-263` |
| Navmesh bake's destructible test | `Assets/Editor/RaidNavBake.cs:66-77` |
| The guard that misses the clad + its own warning comment | `Assets/Editor/RaidNavBake.cs:154-156`, `:157-184` (strip loop `:165`) |
| Collapse drops colliders/obstacles; sinks only own children | `Assets/_Modules/Village/Walls/WallSegment.cs:387-404`, `:436`, `:453-461` |
| The comment that predicts "the art is a sibling the sink never moved" | `Assets/_Modules/Village/Walls/WallSegment.cs:475-477` |
| Breach probe (`holeNavmesh=`) | `Assets/_Modules/Village/Troops/TroopController.cs:1378-1394`, emitted at `:1406-1412` |
| 645/656 NOT-WALKABLE; ordered-wall-never-damaged (`SW_11`) | `logs/device/pull-20260914-172704-breach-success-playthrough/logcat_full.txt` |
| Frozen troops, `reason=timer` retarget flip | `logs/device/watch-wall-collision-check/logcat_live.txt` lines 2, 85, 229, 313, 384, 535, 539 |
| Razed 28% with an unbroken wall | `logs/device/pull-20260914-owner-screenshots/Screenshot_20260914-173743.png` |
| Razed 15%, troop striking the wall | `logs/device/pull-20260914-owner-screenshots/Screenshot_20260914-172741.png` |
| Segment/clad width constants | `RaidBaseGenerator.cs:197` (`MaxSegmentWidth`), `:202` (`SegSize`); `RaidBaseDresser.cs:526` (`piece`) |

---

## 11. SECOND SME LANE — three facts that MEASURE what §2 inferred, plus a ship-blocking tree hazard

### 11.1 The clad's `NavigationStatic` flag is MEASURED in the shipped scene, not inferred
Counted over `git show 0e656756e:Assets/Scenes/RaidBase_raider_camp_small.unity`:
```
PrefabInstance blocks naming Clad_ WITH NavigationStatic(8): 60
PrefabInstance blocks naming Clad_ WITHOUT it:                0
```
60 of 60 visible wall panels are baked in. §2.2 is therefore measured at the asset, not reasoned from the code.

### 11.2 The WallSegment is INVISIBLE BY DESIGN — so the sink can never show anything
`RaidBaseDresser.HideWallRenderers` (`:485-496`) switches off every renderer under any `Wall_*` object before
the clad is built. The `collapse tell STARTED over N renderer(s)` line counts them with
`GetComponentsInChildren<Renderer>(true)` (`WallSegment.cs:436`), which **returns disabled renderers** — so the
trace reports a visual tell over invisible geometry and reads as success. That is why 80/80 settle lines coexist
with a wall the owner sees unchanged.

### 11.3 ⛔ THE NAVMESH IS THE **ONLY** THING THAT BLOCKS THE HERO — colliders are irrelevant to her walking
`HeroLocomotion` is a kinematically-driven `NavMeshAgent`: `_agent.Move(step)` on-mesh, `transform.position +=
step` off-mesh (`HeroLocomotion.cs:1488-1489`), with `obstacleAvoidanceType = NoObstacleAvoidance` (`:999`).
A grep of that file for `Physics.` / `Raycast` / `CapsuleCast` / `SphereCast` / `Rigidbody` /
`CharacterController` returns **zero hits**.
*(The comment at `HeroControlEnsurer.cs:730-731` claiming HeroLocomotion CapsuleCasts against wall colliders is
**STALE** — CLAUDE.md §0, comments lie. Correct or banner it in the same commit.)*

**This retires WO-1722 §2 outright.** A collider never stopped the hero in the first place, so "her collider was
too narrow / too wide" cannot be the mechanism for either the walk-through report OR today's cannot-walk-through
report. Both are navmesh. Do not spend a lane on collider width.

### 11.4 Partition mismatch, measured: **78 `Wall_Outer_*` segments vs 60 `Clad_*` panels** on the same R31.00 ring
One collapse clears ~2.86 m of ring; one visible panel spans ~4.13 m. **Even after the nav fix, destroying one
segment can never cleanly clear one visible panel.** This is the hard constraint behind §7 Q1 and it is why the
VISUAL half of this ticket is held pending that ruling.

### 11.5 `Collapsed?.Invoke` (`WallSegment.cs:419`) has ZERO runtime subscribers
The only two in the repo are editor-only (`Assets/Editor/RaidBreachRuntimeProof.cs:44`,
`Assets/Editor/Regression/StructureTargetableRegression.cs:478`). Nothing in a shipped build swaps a visual,
spawns rubble, or adds a `NavMeshLink` when a wall dies. `grep -rn "Zone_Clad|\"Clad_\"" Assets/_Modules/`
returns **nothing** — no runtime code can see, move, hide or swap the visible wall at all.

### 11.6 ⚠⚠ SHIP-BLOCKING WORKING-TREE HAZARD — re-bake before ANY raid build
| Scene | `.unity` mtime | `!u!208` NavMeshObstacle in WORKING TREE | at HEAD |
|---|---|---|---|
| `RaidBase_raider_camp_small` | 18:27 | **0** | 82 |
| `RaidBase_fortified_garrison` | 18:27 | **0** | 193 |
| `RaidBase_mage_enclave` | 18:27 | **0** | 220 |
| `RaidBase_IronBastion` | 14:00 | 220 | 220 |

The three scenes regenerated at 18:27 (uncommitted, ` M` in `git status`) carry **zero** obstacles and **zero**
`m_StaticEditorFlags` modifications. A regeneration wiped `RaidNavBake`'s output and **`RaidNavBake.BakeAll` was
never re-run.** The device build (`0e656756e`) predates this and is unaffected — it carried 82 obstacles. **If
this tree ships, raid walls carve nothing at all.** Re-run `DeNelle.Editor.RaidNavBake.BakeAll` (editor CLOSED,
CLAUDE.md §3) before any raid build. This is independent of the fix and must not be bundled into judging it.

### 11.7 THE ACCEPTANCE ORACLE ALREADY EXISTS — do not build a new one
`Assets/Editor/RaidBreachRuntimeProof.cs` — menu `Defenders/Raids/Observe Next Combat Breach` (`:19`). Headed
Play in a `RaidBase_*` scene, let a wall die, and it emits:
- `RAID_BREACH_RUNTIME_OK` (`:67`) — a cross-wall route opened, **fix confirmed**
- `RAID_BREACH_RUNTIME_FAIL wall=... cross-wall route still blocked 2s after destruction` (`:72-73`) — **not fixed**

⚠ Editor-only and **headed** — it is not a device-log check and not a batchmode gate. Run it before and after.
It is the single line that settles §4.1/§4.2, and it should have been run today.

---

## 12. LANE SPLIT (set by the lead, 2026-09-14)

| Lane | Scope | Needs an owner ruling? |
|---|---|---|
| **A — NAVMESH (dispatched)** | §4.2 nav half only: exempt `Zone_Clad` descendants from the `NavigationStatic` marking pass in `RaidNavBake.cs:66-77`, so the ground under the visible wall bakes walkable and the existing carve-drop on collapse really opens a hole. Correct the stale `HeroControlEnsurer.cs:730-731` comment. No re-parenting, no partition change. | **No** |
| **B — VISUAL (UNBLOCKED 2026-09-14)** | §4.1 re-parenting on the **panel-matched (~4 m / 60-segment)** partition per the Q1 ruling, + §4.3 rubble swap, + the Q2 ordered-panel highlight. | **No — Q1 and Q2 are RULED (§7)** |
| **C — RE-BAKE (lead)** | §11.6. Editor closed, batchmode. | No |

Lane A is the half the owner actually asked about ("travel through the hole") and it is unblocked. Lane B is the
half she also asked about ("no visual change") and it is genuinely blocked on Q1 — do not guess it.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>

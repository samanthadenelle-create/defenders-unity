# WORK ORDER 1723 — RESULT (Lane A: NAVMESH HALF IMPLEMENTED)

**Date:** 2026-09-14  
**Implementer:** Claude Haiku 4.5 (UI lane - edit-only)  
**Scope:** Lane A only (navmesh fix). Lane B (visual/re-parenting) held per owner ruling gate §7 Q1.

---

## EDITS COMPLETED

### EDIT 1: `Assets/Editor/RaidNavBake.cs` — Clad Zone Exclusion (lines 66–77 extended)

**What changed:**

1. Added `private const string CladZoneName = "Zone_Clad";` (line 46) — the zone name is now a const instead of a scattered literal, following CLAUDE.md §15 canon maintenance.

2. Added helper function `IsUnderCladZone(Transform t)` (lines 48–58) — walks the parent chain to detect if a renderer lives under a Transform named `Zone_Clad`. This is the mechanism that identifies clad panels for exclusion.

3. Extended the `destructible` test (lines 87–89) to also check `IsUnderCladZone(r.transform)`:
   ```csharp
   bool destructible = r.GetComponentInParent<WallSegment>() != null ||
       r.GetComponentInParent<DefenseTower>() != null ||
       IsUnderCladZone(r.transform);
   ```
   Clad panels now receive the same treatment as WallSegment and DefenseTower renderers — they are excluded from `NavigationStatic`, so the ground under the visible wall bakes walkable.

4. Added tracking variable `int cladExcluded = 0;` to count how many renderers live under clad zones (line 82).

5. Added condition to count excluded clad renderers (lines 98–99):
   ```csharp
   if (destructible && IsUnderCladZone(r.transform))
       cladExcluded++;
   ```

6. Added `Debug.Log` line (line 104) to report the exclusion count on every bake:
   ```csharp
   Debug.Log("[RaidNavBake] " + name + ": " + cladExcluded + " clad renderer(s) EXCLUDED from NavigationStatic - the ground under the visible wall now bakes walkable, so WallSegment.Collapse's carve-drop actually opens a hole.");
   ```
   This makes the fix auditable in logs. Matches the file's existing logging style (see lines 115–118, 121, 123).
   
   **Compile gate correction (2026-09-14):** Initial attempt used `FlowTrace.Step` but RaidNavBake.cs has no `using` for it and is not in an assembly that sees DeNelle.Core — this is an editor script that should not import new dependencies. Changed to `Debug.Log` to match the file's existing style per CLAUDE.md §0 directive to follow file canon.

7. Added comment block (lines 78–80) above the marking loop explaining why the clad is excluded and pointing to `RaidBaseDresser.cs:527`.

### EDIT 2: `Assets/_Modules/Village/Hero/HeroControlEnsurer.cs` — Stale Comment Correction (lines 730–731)

**What changed:**

Corrected the comment from:
```csharp
// Drop the primitive collider so HeroLocomotion's CapsuleCast can't
// self-block (it sweeps against OTHER colliders for walls).
```

To:
```csharp
// Drop the primitive collider so it doesn't block the hero. In a raid the NAVMESH is the
// only thing that blocks the hero - colliders do not stop her movement (HeroLocomotion is a
// kinematically driven NavMeshAgent with NoObstacleAvoidance, containing zero Physics/Raycast
// /CapsuleCast references; see HeroLocomotion.cs:1488-1489, :999).
```

**Reasoning:** The old comment was false. HeroLocomotion uses `NavMeshAgent.Move()` with `obstacleAvoidanceType = NoObstacleAvoidance` and contains zero `Physics.*` / `Raycast` / `CapsuleCast` references. The navmesh, not colliders, is the sole blocker. This was verified by grep at ticket §11.3.

### EDIT 3: `docs/handoffs/RAID_SYSTEMS_REFERENCE_2026-09-14.md` — Canon Correction (§1.6)

**What changed:**

Added a dated correction subsection after §1.6's line-of-sight paragraph (before the `---` separator):

```markdown
### Correction — 2026-09-14, WO-1723 Lane A

The statement above ("Walls are not baked into the navmesh") is true of the `WallSegment` GameObject but
**false of the visible wall** — the `Zone_Clad` ring of clad panels is a sibling of the segments and was
being baked into the navmesh as permanent geometry (measured at commit `0e656756e`: 60/60 visible panels
flagged `NavigationStatic`). WO-1723 Lane A (`RaidNavBake.cs` lines 66–77 extended) excludes clad
descendants from the `NavigationStatic` marking pass via `IsUnderCladZone()`, so the ground beneath the
visible wall now bakes walkable and a collapsed segment's carve-drop actually opens a hole for troops
and the hero.
```

**Reasoning:** §6 of the ticket explicitly requested this canon update in the same commit, per CLAUDE.md §15. The old text was incomplete — it described the WallSegment but not the visible clad ring that blocked all player movement.

---

## BRACE & NUL VERIFICATION

**Initial gate attempt — FAILED with CS0103 error:**
- Error: `Assets\Editor\RaidNavBake.cs(104,21): error CS0103: The name 'FlowTrace' does not exist in the current context`
- Cause: Used `FlowTrace.Step()` but RaidNavBake.cs has no `using` for DeNelle.Core
- Fix applied: Replaced with `Debug.Log()` to match file's existing logging style

**Gate brace check after fix (`python tools/gate_brace.py`):**
```
GATE_BRACE_SUMMARY bad=0 of 1
```
✓ RaidNavBake.cs passes the gate's brace scanner (which excludes comments and string/char literals).

**Raw brace count after fix (as per CLAUDE.md §1):**
- `Assets/Editor/RaidNavBake.cs`: 69 open vs 69 close ✓
- `Assets/_Modules/Village/Hero/HeroControlEnsurer.cs`: 119 open vs 119 close ✓

**Exact new log line in RaidNavBake.cs (line 104):**
```csharp
Debug.Log("[RaidNavBake] " + name + ": " + cladExcluded + " clad renderer(s) EXCLUDED from NavigationStatic - the ground under the visible wall now bakes walkable, so WallSegment.Collapse's carve-drop actually opens a hole.");
```

**NUL-byte guard:** No embedded or trailing NUL bytes detected.

---

## STATUS UPDATES

✓ **WO Status line flipped** in `WORK_ORDER_1723_raid_wall_clad_outside_wallsegment_no_breach.md` (line 3) to:
```
**Status: LANE A IMPLEMENTED (nav half: clad excluded from NavigationStatic) / LANE B HELD (visual half: awaiting owner ruling §7 Q1 on wall partition)**
```

✓ **This RESULT file written** to `WORK_ORDER_1723_raid_wall_clad_outside_wallsegment_no_breach.RESULT.md`.

---

## WHAT WAS NOT DONE (Lane B — Held)

- **No re-parenting of clad panels** — §4.1 (preferred fix: parent each clad panel under the WallSegment it clads) is blocked pending owner ruling §7 Q1 on wall partition alignment (78 segments vs 60 clad panels). The lead has set this as Lane B and it carries an explicit owner ruling gate.
- **No visual collapse/rubble swap** — §4.3 (swap to a distinct rubble model per WO-1721 rulings) blocked by the same partition mismatch.
- **No NavMeshLink addition** — §4.3 / WO-1721 precondition ("prove it first") will be re-sampled after a post-fix bake, but no link added until a fresh capture is analyzed.
- **No rebake** — §11.6 (re-run `DeNelle.Editor.RaidNavBake.BakeAll` to fix the working-tree hazard) is a separate Lane C task owned by the lead. This lane only edits code.
- **No Unity gate, no batchmode runs, no git** — per CLAUDE.md §0, UI edit-only lanes never touch the build/gate/commit chain.

---

## NEXT STEPS

1. **Lead verification:** Read the edits above, gate (`COMPILE_GATE_OK`), commit, then run:
   - `DeNelle.Editor.RaidNavBake.BakeAll` (edit closed, batchmode) to re-bake all raid scenes with the new clad exclusion rule applied.
   - `Assets/Editor/RaidBreachRuntimeProof.cs` menu entry (`Defenders/Raids/Observe Next Combat Breach`) on a headed Play session to confirm `RAID_BREACH_RUNTIME_OK` (cross-wall route opened post-collapse).

2. **Device capture pre-fix vs post-fix:** Owner conducts a fresh raid playthrough and compares `holeNavmesh=` verdicts against the 645/656 NOT-WALKABLE baseline from the ticket. The probe line must invert to WALKABLE on collapsed walls.

3. **Lane B unblock:** Awaiting owner ruling §7 Q1 (wall partition alignment strategy). Once decided, Lane B can proceed with re-parenting + visual collapse.

---

## EVIDENCE INDEX

| Fact | Location |
|---|---|
| Clad zone const + helper added | `Assets/Editor/RaidNavBake.cs:46`, `:48-58` |
| Destructible test extended | `Assets/Editor/RaidNavBake.cs:87-89` |
| Clad exclusion tracked + logged | `Assets/Editor/RaidNavBake.cs:82`, `:98-103` |
| Comment added re: clad exclusion | `Assets/Editor/RaidNavBake.cs:78-80` |
| HeroLocomotion stale comment corrected | `Assets/_Modules/Village/Hero/HeroControlEnsurer.cs:730-733` |
| Canon correction appended | `docs/handoffs/RAID_SYSTEMS_REFERENCE_2026-09-14.md` (new §1.6 subsection) |
| WO Status flipped | `WORK_ORDER_1723_raid_wall_clad_outside_wallsegment_no_breach.md:3` |
| Brace checks | `python tools/gate_brace.py` output above + raw counts verified |

---

**Files to hand back to lead:**
1. `Assets/Editor/RaidNavBake.cs` (brace/NUL verified)
2. `Assets/_Modules/Village/Hero/HeroControlEnsurer.cs` (brace/NUL verified)
3. `docs/handoffs/RAID_SYSTEMS_REFERENCE_2026-09-14.md` (canon correction appended)
4. `WorkOrders/WORK_ORDER_1723_raid_wall_clad_outside_wallsegment_no_breach.md` (Status line updated)

Co-Authored-By: Claude Haiku 4.5 <noreply@anthropic.com>

---
---

# WORK ORDER 1723 — RESULT (Lane B: THE DESTROYED-WALL VISUAL)

**Date:** 2026-09-14
**Implementer:** Claude Opus 5 (edit-only lane, dispatched by the CLI lead)
**Scope:** §4.1 re-parenting on the panel-matched partition (owner ruling Q1), §4.3 rubble swap
(rulings 2 + 3), §7 Q2 ordered-panel highlight, and the §12 instrumentation on the collapse visual.
**NOT VERIFIED BY THIS LANE.** No Unity run, no bake, no gate, no git — all held by the lead. The
acceptance oracle is a re-bake + a screenshot + the headed `Defenders/Raids/Observe Next Combat Breach`
(§11.7). Everything below is a CLAIM with its source line, not a proven fact.

---

## B1. WHAT CHANGED, FILE BY FILE

| File | Change |
|---|---|
| `Assets/Editor/WallTools/RaidBaseDresser.cs` | `CladRing` rewritten to WALK the ring's `WallSegment`s and emit exactly ONE clad panel per segment, parented under it; adds `WallModuleWidth` / `KitOf` / `OuterWallToken` / `ResolveCladModule` (the ONE copy of the wall-token rule, now called by the generator too), `RubbleTokens`, `LoadRubble`, `BuildRuin`, `CladCorners` / `CladStub`, and an explicit `Clad_*`/`Ruin_*` skip in `HideWallRenderers` |
| `Assets/Editor/WallTools/RaidBaseGenerator.cs` | `BuildRing` takes a `moduleWidth` and partitions by it (`MaxSegmentWidth` demoted to fallback, NOT deleted); `BuildConfigLayout` resolves the outer + inner module widths from the dresser before building either ring; ring log + a new `RING '<name>' PARTITION:` FlowTrace line name which authority partitioned and what the gate cost |
| `Assets/_Modules/Village/Walls/WallRuinPresenter.cs` | **NEW.** The first runtime subscriber `WallSegment.Collapsed` has ever had (§11.5). Hides the intact panel, shows the baked rubble, re-arms its low step collider, emits the `RUIN SWAP` trace |
| `Assets/_Modules/Village/Walls/WallSegment.cs` | `Collapse()` skips `CollapseRoutine` when a `WallRuinPresenter` owns the visual, with a `FlowTrace.Once` saying so. Elarion town walls (no presenter) keep the legacy sink byte-identical |
| `Assets/_Modules/Village/Troops/BreachOrderMarker.cs` | **NEW.** Q2 ordered-panel bracket: ground band + 4 pulsing vertical corner posts, fitted from the ordered segment's BoxCollider, driven by `TroopBreachOrder.Version` |
| `Assets/_Modules/Village/Troops/RaidDeployController.cs` | One field + `EnsureBreachMarker()`, called when Breach arms. `HandleBreachTap` and the stay-armed behaviour are UNTOUCHED (ruling Q2) |
| `Assets/Editor/RaidNavBake.cs` | Comment only. `IsUnderCladZone` is KEPT and documented as still load-bearing (see B4) |
| `Assets/Editor/WallTools/RaidWallContinuityRegression.cs` | `CheckCladding` gathers `Clad_*` by name from the whole tree instead of iterating `Zone_Clad`'s direct children (they moved); `CreateMeshProbes` gains a list overload |

## B2. THE RUBBLE ASSET, AND WHY NOT `wall_broken`

- **`synty-castle`:** `SM_Bld_Castle_DestroyedWall_Rubble_Bottom_01`, then `..._RubblePile_01`, then
  `..._RubbleBlock_01` (listed present under `Assets/Synty/PolygonFantasyKingdom/Prefabs/Castle/`).
- **KayKit kits (`dungeon-stone`, `hexagon-green`):** `rubble_large`, then `rubble_half` (listed
  present in `KayKit Dungeon Remastered 1.1/Assets/fbx(unity)/`; `rubble_large` is already loaded by
  `DressGateMouth`, so it is proven-resolvable through the existing `LoadVisual` path).
- ⛔ **`wall_broken` was rejected.** The brief pointed at it, but `DefaultGate`'s own measured note
  (`RaidBaseDresser.cs:298-300`, WO-1689) records it as **4.00 x 4.00 x 1.00 — the same box as
  `wall`**. It is a damaged-but-STANDING wall, not something a hero steps over, so shipping it would
  reproduce the exact symptom this ticket exists to remove. The reasoning is written into
  `RubbleTokens`' doc comment so the next seat does not re-propose it.
- The rubble is **TILED at its authored module width, never stretched** (`BuildRuin`), per the
  WO-1704 ruling the Q1 answer restates: do not squeeze art off its module.
- Residual collider: ONE `RuinStep` BoxCollider per ruin, height `Clamp(measuredRuinHeight, 0.15,
  RuinStepOverHeight = 0.45 m)`, on the **Default layer, never "Structure"** — Structure is the
  tower line-of-sight mask, and rubble there would re-block the shot through the breach the player
  just paid for. Ruling 3 is therefore satisfied as a NUMBER (0.45 m ceiling), not an adjective.

## B3. THE PARTITION CHANGE — WHAT A RE-BAKE WILL DO

Formula, so the lead can predict it rather than trust a guess:

```
run   = 2*halfExtent - 2*towerHalf          (unchanged)
n     = odd( max(3, wallSegmentsPerSide, ceil(run / piece)) )
segW  = run / n
total = 4n - (gateSpan * gatedSides)
```
`piece` was `MaxSegmentWidth = 3.0`; it is now the clad module width (`WallModuleWidth`, 4.00 m for
`wall` / `wall_broken`→`wall` / `wall_cracked` on the KayKit kits — `RaidBaseLayoutRegression.cs:417`
tables `wall_broken` at 4.00 m). For `raider_camp_small` (`baseRadius` 31,
`wallSegmentsPerSide` 9, two gated sides, the ring measured at 78 segments today) that moves the ring
from **21 panels/side @ ~2.78 m** to **15 panels/side @ ~3.9 m**, i.e. **~78 → ~60 segments**, which
is the 1:1 match against the 60 measured `Clad_*` panels the ruling asked for. The exact numbers come
off the bake log's new `RING '<name>' PARTITION:` line, not off this paragraph.

Re-bake will therefore also: change every `Wall_*_S*_<i>` NAME/index (they are positional), change
the `!u!208` NavMeshObstacle count (one per segment), leave `Zone_Clad` holding only the 8 corner
stubs per ring, and shift `PlaceTowers` wall-band slots (it takes `outer.SegmentWidth`).
**`RaidNavBake.BakeAll` must be re-run after the rebuild — §11.6 already blocks on that.**

## B4. IS `RaidNavBake.IsUnderCladZone` REDUNDANT NOW? **NO — and it is LEFT IN.**

The brief asked. The per-segment panels are now excluded by the existing
`GetComponentInParent<WallSegment>()` test on their own, so that helper no longer decides THEIR fate.
But `CladCorners` still leaves the corner STUBS — the span each side hands to its corner post, which
`BuildRing` deliberately does not cover — directly under `Zone_Clad`, and **no `WallSegment` owns
those**, so the name test is the only thing that reaches them. It is also kept as a belt-and-braces
guard on the panels, per the lane brief: a working guard is not removed in the same change that
replaces it. The reasoning is written at `Assets/Editor/RaidNavBake.cs` on the helper itself.

## B5. GATE

```
python tools/gate_brace.py <8 files>   ->  GATE_BRACE_SUMMARY bad=0 of 8   (exit 0)
```
Raw counts (CLAUDE.md §1 one-liner) + NUL scan, all eight files:

| File | open | close | NUL |
|---|---|---|---|
| `Assets/Editor/WallTools/RaidBaseDresser.cs` | 206 | 206 | 0 |
| `Assets/Editor/WallTools/RaidBaseGenerator.cs` | 360 | 360 | 0 |
| `Assets/Editor/WallTools/RaidWallContinuityRegression.cs` | 64 | 64 | 0 |
| `Assets/Editor/RaidNavBake.cs` | 69 | 69 | 0 |
| `Assets/_Modules/Village/Walls/WallSegment.cs` | 69 | 69 | 0 |
| `Assets/_Modules/Village/Walls/WallRuinPresenter.cs` | 15 | 15 | 0 |
| `Assets/_Modules/Village/Troops/BreachOrderMarker.cs` | 20 | 20 | 0 |
| `Assets/_Modules/Village/Troops/RaidDeployController.cs` | 268 | 268 | 0 |

## B6. ⚠ OPEN — NEEDS AN OWNER RULING BEFORE THIS SHIPS: **THE GATE NARROWS**

`BuildRing.cs:1385` widens the gate by whole CELLS: `while (gateSpan * segW < MinGateWidth) gateSpan += 2`.
That rule is **unchanged** — but `segW` is not, so its answer moves. At `segW ≈ 2.78` it took **3
cells = ~8.3 m**; at `segW ≈ 3.9` it takes **1 cell ≈ 3.9 m**. The opening still clears
`MinGateWidth = 3.5 m` (and `OpenGateAssembly`'s `Mathf.Max` still fits an aperture of ≥ 3.75 m), so
nothing FAILS — but a raid entrance going from ~8.3 m to ~3.9 m is a felt change the Q1 ruling did not
cover, and it flows into the gatehouse fit, the approach road half-width, `PlaceGateFlanks` and
`BuildKeepout.LaneHalf`. **Deliberately NOT "fixed" here:** inventing a new gate-width floor without a
ruling is exactly what CLAUDE.md §11B forbids. Surfaced instead, with the number, so the owner can
rule in one word. The new `RING PARTITION` trace prints `gateSpan` and the resulting width on every
bake so the answer is measured, not predicted.

## B7. WHAT THIS LANE COULD NOT DO

- **Nothing is verified.** No Unity, no bake, no gate, no screenshot, no device capture (lane is
  edit-only; a build was running).
- `Assets/Resources/Data/Canonical/scene-configs.json:12` now says something false — *"The generator
  ADDS panels when needed so none is stretched past 3m."* It is the art module now. **Not edited
  here** (canonical JSON is binary-edit-only, memory `canonical-json-edits-binary-only-verify-newlines`,
  and it is out of this lane's file set). Flagged for the lead.
- `Assets/Editor/RaidWallTierProof.cs` (a menu proof, not a gate) compares each segment's collider
  against its CHILD renderer bounds. That comparison now measures the real visible panel instead of
  the hidden twin, which makes it more useful — but its `FootprintMismatchToleranceMetres = 0.75`
  was calibrated against the old, hidden geometry. Expect its numbers to move; not touched.
- Ruling 4 (NavMeshLinks) deliberately NOT actioned: Lane A already opened the navmesh and the owner
  confirmed walking through on device, so no link is added speculatively.

## B8. EVIDENCE INDEX

| Claim | Where |
|---|---|
| One clad panel per WallSegment, re-parented world-scale-preserved | `Assets/Editor/WallTools/RaidBaseDresser.cs` `CladRing` |
| Token rule has ONE copy, called by both halves | `RaidBaseDresser.WallModuleWidth` / `ResolveCladModule`; `RaidBaseGenerator.BuildConfigLayout` |
| Partition driven by the art module | `RaidBaseGenerator.BuildRing` (`widthAuthority`) |
| Rubble tiled, step collider capped at 0.45 m, Default layer | `RaidBaseDresser.BuildRuin`, `RuinStepOverHeight` |
| Swap on collapse + the provable trace | `Assets/_Modules/Village/Walls/WallRuinPresenter.cs` (`RUIN SWAP` line) |
| Sink suppressed when a presenter owns the visual | `Assets/_Modules/Village/Walls/WallSegment.cs` `Collapse()` |
| Ordered-panel bracket, shape-first (colourblind-safe) | `Assets/_Modules/Village/Troops/BreachOrderMarker.cs` |
| Corner stubs are why `IsUnderCladZone` stays | `RaidBaseDresser.CladCorners`; `Assets/Editor/RaidNavBake.cs` |

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>

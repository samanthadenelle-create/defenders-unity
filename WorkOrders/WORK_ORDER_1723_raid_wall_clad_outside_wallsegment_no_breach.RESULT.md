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

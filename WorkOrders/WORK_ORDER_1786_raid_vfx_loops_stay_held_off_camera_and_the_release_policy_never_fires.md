# WORK ORDER 1786 — Enemy-caster VFX loops stay held off-camera for 89 s inside the raid, and WO-1473's release policy says so itself

**Status:** READY TO IMPLEMENT

**Minted:** 2026-09-16 by the raid-polish audit lane (number PRE-ASSIGNED from the block 1777-1790; this lane did NOT touch `CLI_LANES_WO_NUMBERS.md`)

**Silo:** VFX loop budget — the `[Flow:Vfx]` loop registry / release policy (WO-1473's owner). **No camera code (WO-1785 lane), no perf probe (WO-1779 lane), no `.unity`, no bake.**

**Build under test:** `2026.09.16.371701` (`ProjectSettings/ProjectSettings.asset:148`), built 2026-09-15 22:06 from `f6653501f`.

**Video path:** act 2. This is a contributor to the 18-27 fps measured in WO-1779, and the held slots starve the effects the video actually needs. P1.

---

## 1. SYMPTOM

Looping VFX that should be released when they leave the camera are held indefinitely. In the raid this consumes over half the loop budget and the slots are spent on enemies and props nobody can see.

## 2. EVIDENCE — the trace line indicts itself

From `Logs/device/pull-20260916-143101-bastion-owner-run/logcat_full.txt` (grepped, 2026-09-16):

```
[Flow:Vfx] STUCK LOOP Aura_EnemyCaster owner='RaidGuard (orc-shaman-Lv7-8)'#-218406 age=89s
  — OFF CAMERA for 6s and STILL HELD: past the 6s release grace. Holding 1 of 24 loop slots (now 9/24).
  The policy exists (WO-1473) — if this line appears, the policy is not doing it[s job]
```

⛔ **That last clause is the finding.** The instrument was written to be an assertion: its author recorded that the line appearing *at all* means WO-1473's release policy failed. It appears **five times**, on four distinct owners:

| held loop | owner | age |
|---|---|---|
| `Aura_EnemyCaster` | `RaidGuard (orc-shaman-Lv7-8)` | **89 s** |
| `Harvest_Wood` | `LumberMill` | **231 s** |
| `Harvest_Iron` | `IronMine` | 17 s |
| `Collector_Ready` | `LumberMill` | 15 s |

**Budget pressure inside the raid window** (log lines 3038129-3062000): `LOOPS 3/24 held, 8 registered` → `LOOPS 11/24`, `13/24 held, 18 registered`, `13/24 held, 20 registered`. **More than half the 24-slot budget, in a raid.**

**And town/overworld loops are still resident during the raid.** In the same window:

```
[Flow:Vfx] Portal_Threshold_Aura  owner='DungeonWorldPortal_dg_hollow_roads'#-N  age=Ns  [SUSPENDED …]
[Flow:Vfx] Portal_Threshold_Aura  owner='DungeonWorldPortal_dg_ember_deep'#-N     [SUSPENDED …]
[Flow:Vfx] PP_GroundFog  owner='DungeonWorldPortal_dg_hollow_roads'   [SUSPENDED …]
[Flow:Vfx] PP_GroundFog  owner='DungeonWorldPortal_dg_healers_cottage'[SUSPENDED …]
[Flow:Vfx] Poi_NodeAura  owner='(unparented) [Hovl_Poi_NodeAura]'#-N  age=Ns
```

⚠ Note `owner='(unparented) [Hovl_Poi_NodeAura]'` — **a loop whose owner has no parent**, i.e. an orphan holding a slot. `Poi_NodeAura` also appears in the whole-capture `STUCK LOOP` set.

## 3. WHAT TO DO

1. **Find WO-1473's release policy and prove why the 6 s grace does not expire** for an off-camera owner. The line already tells you the grace was exceeded, so the defect is in the release path, not the detection — instrument the release decision (`FlowTrace.Step` on *why it chose to hold*), capture, then fix the step the data names (CLAUDE.md §12). ⛔ Do not "raise the budget" — that hides it.
2. **Release orphans unconditionally.** A loop whose owner is `(unparented)` or destroyed has no camera test to pass; it should never be able to hold a slot.
3. **Dungeon-portal and collector loops must not survive the raid scene handoff.** Either they are released on scene unload, or the registry is scene-scoped. This is the same class of leak as the town probe in WO-1779 (a town system alive inside a raid) — **but a different file, so keep the lanes separate.**
4. Leave every `FlowTrace` call in place — instrumentation is permanent (CLAUDE.md §12, owner ruling 2026-08-09). Flagging off is allowed later; stripping never is.

## 4. ACCEPTANCE

A fresh Seeker capture of one Bastion raid:

1. `grep -c "STUCK LOOP"` inside the raid window = **0**.
2. No `Portal_Threshold_Aura`, `PP_GroundFog`, `Harvest_*` or `Collector_*` loop appears in any `[Flow:Vfx] LOOPS`/held listing after the raid scene loads.
3. Peak `LOOPS n/24 held` in the raid is reported, and it is **below 8**.
4. `grep -c "(unparented)"` in the loop listings = **0**.
5. `python tools/gate_brace.py` exit 0, then `COMPILE_GATE_OK` + `REGRESSION_OK <n>/<n>` on fresh logs.

## 5. DO NOT TOUCH

The Mage ability VFX mappings (WO-1776 lane), ability SFX (WO-1781 lane), `TownActivityProbe` (WO-1779 lane), the camera (WO-1785 lane), any `.unity` file.

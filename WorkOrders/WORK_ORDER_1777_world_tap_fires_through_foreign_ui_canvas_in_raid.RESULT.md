# WORK ORDER 1777 — RESULT

**Outcome: IMPLEMENTED, NOT YET GATED.** Edit-only lane (lane A · raid input, run together with
WO-1790 because both live in one file): no Unity, no compile gate, no regression run, no commit, no
`.unity`, no bake. `python tools/gate_brace.py` exit 0 and zero NUL bytes on both touched `.cs`.

**Date:** 2026-09-16 · **Branch:** `dev` · **Committer:** the lead (this lane does not commit).

---

## 1. FILES TOUCHED (two, both `.cs`)

| File | What changed |
|---|---|
| `Assets/_Modules/Village/Troops/RaidDeployController.cs` | The UI guard is now component-level and cross-canvas, plus a reserved-band clause, plus a `HandleDeployTap IN` arrival trace. (The WO-1790 half of this file is described in its own RESULT.) |
| `Assets/Editor/Regression/RaidDeployUiRegression.cs` | Two new cases: `[raid-ui-guard]` pure-oracle band case + `[raid-ui-guard]` component-level source-lint. |

**Nothing else was opened for edit.** `TroopController.cs`, `RaidAssaultAi.cs`, `TroopBreachOrder.cs`,
`HudKitController.cs`, `PackStore.cs`, `BuildModeController.cs`, `TutorialFlow.cs`,
`SafeZoneRecovery.cs`, `WaveManager.cs`, `HeroAbilities.cs`, every `.unity`, `DataRegression.cs` and
`CLI_LANES_WO_NUMBERS.md` are untouched. **No `.asmdef` was touched** — CLAUDE.md §5 holds.

---

## 2. THE FIX, AND WHY IT DOES NOT NEED THE UNPROVEN FACT

`IsPointerOverUi` (`RaidDeployController.cs:785`) now delegates to `TryFindUiConsumer` (`:818`),
which decides consumption in four ordered clauses:

1. **Own canvas — KEPT.** Any hit under `_ui` consumes, exactly as before. Dropping this would have
   introduced a new bug: a press on this HUD's own non-interactable backing plate between two tray
   tiles would start falling through to the ground.
2. **Foreign canvas, by `Selectable`.** `go.GetComponentInParent<Selectable>(true)`. The kit's
   ability faces are `Image + Button` — read at source, `ElarionUiKitObsidian.BuildActionSlot:1157`
   (`go = new GameObject("ActionSlot", typeof(Image), typeof(Button))`, and every child sets
   `raycastTarget = false`) — so the press lands on the face root, which carries a `Selectable`.
   ⚠ `interactable` and `enabled` are deliberately **not** tested: a cooldown-locked face must still
   eat the press, or "press a greyed ability → a troop deploys" replaces the bug with a subtler one.
3. **Foreign canvas, by `IEventSystemHandler`.** Covers a pointer handler that carries no
   `Selectable` (a scroll rail, a drag surface, a toast with a tap-to-dismiss handler).
4. **The reserved ability-row band** (`IsInReservedThumbBand`, `:906` / `:921`).

`EventSystem.current.RaycastAll` already iterates **every** registered `GraphicRaycaster`, not just
this canvas' — the old narrowing was purely the `IsChildOf(_ui)` filter. When no `EventSystem`
exists (headless / a scene that never built one) the raycasters are resolved **by component**
(`FindObjectsByType<GraphicRaycaster>`) and asked directly, each inside `Guard.Try`.
`IsPointerOverGameObject()` was rejected on purpose and the reason is in the code: it needs a
per-touch pointerId under the new Input System and warns outside event processing, and it does not
hand back *which* object was hit — which the trace has to report.

**Clause 4 is what makes the fix independent of the fact nobody proved.** WO-1777 §4 acceptance 4
records that *which* ability face sits at x≈820 was never established. Clause 4 catches all forty
measured taps by arithmetic on shared band data, so the fix never has to know:

| measured point (of 2670x1200) | normalised | in band (x ≥ 0.270, y 0.015-0.150)? |
|---|---|---|
| (774, 78) | (0.290, 0.065) | YES |
| (876, 130) | (0.328, 0.108) | YES |
| (820, 105) — box centre, the unnamed face | (0.307, 0.088) | YES |
| **(1333, 573)** — the one success, `Wall_Keep1_SS_0` | (0.499, 0.478) | **NO** — still a world tap |

⛔ **The band is read, never typed.** `HudLayoutBands.ThumbActionRowBand(HudLayoutBands.MoveClusterMount.xMax, 1f)`
→ `Rect.MinMaxRect(0.270, 0.015, 1, 0.150)`. `ThumbActionRowMinY/MaxY` and `MoveClusterMount` were
read at source (`Assets/_Modules/Core/UI/HudLayoutBands.cs:225-227, :286`), and `MoveClusterMount.xMax`
is documented there as also the actionBar's left edge, pinned equal to it by
`RaidHudThumbBandRegression`. A literal here would be the duplicated-state failure CLAUDE.md §2/§5/§16
each describe.

**No over-rejection.** Clause 4 is x-bounded, so it never claims the bottom-left corner; a
non-interactable backing plate on a foreign canvas (`RaidHudController`'s tap-transparent readout) is
not consumed by clause 2 or 3; the three world-tap probes in the oracle all stay world taps.

**The rejection is traced, throttled, and states only what it measured** (`:797`):
`world tap REJECTED as UI: screenPoint=… screenNorm=… uiHits=… consumer='…' canvas='…' scope=own|foreign rule=…`
with `rule` one of `own-canvas-hit` / `foreign-selectable` / `foreign-event-handler` /
`reserved-thumb-band`. `Throttle` at 0.25 s is mandatory, not tidiness: the same capture shows 54 Mage
casts in ~100 s, and an un-throttled line on this path evicts the boot window out of the logcat ring
(memory `logcat-ring-buffer-destroys-evidence`).

**`HandleDeployTap IN`** (`:967`, trace at `:972`) is new. WO-1777 acceptance 1 greps that prefix and
**the line did not exist**, so the acceptance would have passed vacuously. It now carries
`screenPoint`, `screenNorm`, `inReservedThumbBand` and `armedDefId`, and says in its own text that a
true `inReservedThumbBand` there means this guard regressed — the trace is its own detector.

---

## 3. WO-1777 §4 ITEM 3 — `VirtualJoystick.IsInZone` IS **DEFERRED**, ON INSTRUCTION

The lead's brief for this lane says, verbatim, *"keep the joystick exclusion as it is"*, so no
joystick clause was added. It is also a no-op either way, and that is read at source, not assumed:
`VirtualJoystick.cs:12-13` states it polls `UnityEngine.Input` with **no EventSystem / GraphicRaycaster**,
and `:219` sets `img.raycastTarget = false` on its graphic. So the stick never appears in a graphic
raycast and this guard cannot change its behaviour. The WO's own §3a already ruled the stick out of
the forty taps by arithmetic. Recorded here so the item is visibly deferred, not silently dropped.

---

## 4. REGRESSION (two new cases in a suite that IS wired)

`Assets/Editor/Regression/RaidDeployUiRegression.cs` — registered at
`Assets/Editor/Regression/DataRegression.cs:630` (read there this session). ⚠ That file's own header
still says *"not yet wired into DataRegression.RunAll"*; **the header is stale, the wiring is live.**
`DataRegression.cs` was not touched, so no new registration was needed.

- **`CheckRaidUiGuardBand`** (`:587`) — a **pure oracle over the shipped function**, not a lint. It
  calls `RaidDeployController.IsInReservedThumbBand(nx, ny)` with the real capture coordinates: the
  four corners of the measured 100x50 px box **plus its centre** must all be consumed; the capture's
  one success point and two open-ground points must all NOT be; the bottom-left stick corner
  (0.05, 0.08) must NOT be; and (0.5, 0.08) MUST be, so a function that always returned false cannot
  pass. That last case is the non-vacuity guard this file's other cases also carry.
- **`CheckRaidUiGuardIsComponentLevel`** (`:650`) — source-lint: `GetComponentInParent<Selectable>`,
  `IEventSystemHandler`, `GraphicRaycaster`, `IsChildOf(_ui.transform)` (the own-canvas clause must
  SURVIVE), `HudLayoutBands.ThumbActionRowBand`, `world-tap-rejected-ui` under a `Throttle`, and
  `HandleDeployTap IN` are all present; `using DeNelle.HUD` / `DeNelle.HUD.` / `HudKitController` are
  all absent. ⚠ The §5 lint matches a **reference**, not the word — this controller's comments
  legitimately name `DeNelle.HUD` five times to record why the boundary exists.

**A live `EventSystem.RaycastAll` test is deliberately NOT built.** Overlay-canvas raycasting depends
on `Screen.*` and display state and is unreliable in EditMode; this file's own header settles on pure
oracles plus source-lint for that reason. The consequence is stated plainly in §6 below.

---

## 5. PROOFS RUN THIS SESSION

| Proof | Result |
|---|---|
| `python tools/gate_brace.py` on both `.cs` | `GATE_BRACE_SUMMARY bad=0 of 2`, exit 0 |
| NUL scan (`\x00`) on both `.cs` | 0 and 0 |
| Raw brace balance | controller 276/276, regression 81/81 |
| Every positive lint token, grepped in the controller | all present (counts captured) |
| Every negative lint token, grepped | `GetComponentsInChildren<Renderer>(true)` 0, the camera-distance expression 0, `Splits the remaining causes` 0, `using DeNelle.HUD` 0, `DeNelle.HUD.` 0, `HudKitController` 0 |
| The oracle's band arithmetic, recomputed by hand | table in §2 — 5 consumed, 3 not, stick corner not, non-vacuity point consumed |
| `ElarionUiKitObsidian.BuildActionSlot` builds `Image + Button` with children `raycastTarget=false` | read at `:1157-1240` |
| `RaidDeployUiRegression` is wired into `DataRegression.RunAll` | `DataRegression.cs:630` |
| **No sibling suite reads a trace string this lane renamed.** Grepped `Assets/Editor`, `tools`, `.claude` for every changed/removed token | zero consumers (detail in the WO-1790 RESULT §4) |
| `RaidRepeatClearRegression.cs:423-470` lints `HandleDeployTap` by ORDER; the new `HandleDeployTap IN` trace precedes all three audited indices | ordering relations unchanged, audited slice untouched |
| `GetComponentInParent<T>(bool includeInactive)` exists on this editor | `ProjectVersion.txt` = `6000.4.8f1` |
| ⚠ **Headless raid drivers do not synthesise a low-y tap.** No file under `Assets/Editor` / `tools` writes `_leanTapPoint` / `_leanTapLatched` or calls `HandleDeployTap`; the only references are source-lints. So the band clause refuses no automated deploy | grepped this session |

---

## 6. ⚠ NOT PROVEN BY THIS LANE — do not read these as done

1. **Which ability face sits at x≈820 is still unknown** (WO-1777 §4 acceptance 4). The fix is built
   so it does not depend on the answer (§2). One in-game screencap at 2670x1200 still closes it.
2. **Acceptance 1-3 are NOT met** — each needs a fresh Seeker capture of a Bastion raid with Deploy
   and then Breach armed. This lane took no device capture and fired no Unity.
3. **No compile gate and no regression run.** The code has never been through a C# compiler this
   session; every symbol was verified by grep at source instead. `COMPILE_GATE_OK` +
   `REGRESSION_OK <n>/<n>` on fresh logs are outstanding.
4. **That the raid scene has a live `EventSystem` at runtime was not read** — it is inferred from the
   tray/Rally/Retreat buttons working. The `GraphicRaycaster` fallback exists so the guard measures
   something either way, but the fallback path itself has not been executed.
5. **The `raycastTarget` state of foreign plates other than the joystick's was not swept.** The
   `Selectable` / `IEventSystemHandler` rule makes it moot for consumption, and the band clause covers
   the gaps between round medallions, but a raycast-target plate elsewhere on screen is untested.
6. **`BOARD.html` was not regenerated and nothing was committed** — the WO `**Status:**` line (the
   board's source of truth) is flipped; `python tools/board_build.py` and the commit are the lead's.

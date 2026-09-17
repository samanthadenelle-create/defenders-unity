# WORK ORDER 1779 — The Bastion raid runs at **18-27 fps**, and the TOWN activity probe is still ticking inside the raid scene

**Status:** IMPLEMENTED, NOT YET GATED — the probe's raid scene gate is in the tree with a headless regression case (`TownSuspendSceneFloorRegression` case (h), `raid-scene-gate`); see `WORK_ORDER_1779_raid_runs_eighteen_fps_and_the_town_probe_ticks_inside_it.RESULT.md`. Two items in §4/§5 are deliberately out of this lane and are re-laned there rather than claimed: the §4.2 `HeroLocomotion` sub-scopes (23 warns / 11.2 ms, the LARGER raid cost) and acceptance §3, plus the device fps delta in acceptance §2, which only a fresh Seeker capture can supply.

**Minted:** 2026-09-16 by the raid-polish audit lane (number PRE-ASSIGNED from the block 1777-1790; this lane did NOT touch `CLI_LANES_WO_NUMBERS.md`)

**Silo:** raid frame budget — `Assets/_Modules/Village/World/TownActivityProbe.cs`. **No `.unity`, no bake, no UI, no AI.**

**Build under test:** `2026.09.16.371701` (`ProjectSettings/ProjectSettings.asset:148`), built 2026-09-15 22:06 from `f6653501f`. Seeker.

**Video path:** every frame of act 2. A 18-fps stutter is visible in **every take**. P0.

---

## 1. SYMPTOM

The raid stutters. On the owner's Seeker the Iron Bastion raid holds **33 fps median and dips to 18**.

## 2. EVIDENCE — measured, not inferred

From `Logs/device/pull-20260916-143101-bastion-owner-run/logcat_full.txt` (grepped). The raid window opens at log line 3038129 / 13:24:28 (`scene=RaidBase_IronBastion`); of the 36 `scene=` samples in lines 3038000-3070000, **30 read `RaidBase_IronBastion`**.

**Every `LOW fps` sample the raid produced:**

```
LOW fps=18 ms=56.5 mem=523MB gc=35MB scene=RaidBase_IronBastion towers=0 enemies=16
LOW fps=21 ms=48.7 mem=522MB gc=36MB scene=RaidBase_IronBastion towers=0 enemies=18
LOW fps=22 ms=44.8 / 45.1   LOW fps=23 ms=42.7   LOW fps=24 ms=40.9
LOW fps=26 ms=37.8   LOW fps=27 ms=36.6
```

Full fps distribution inside the raid window: **18, 21, 22, 22, 23, 24, 26, 27, 32, 33×3, 34×2, 35, 36×3, 37, 38×2, 39×3, 40.**

**The `FlowTrace.Measure` scopes name the dominant costs INSIDE the raid window** (CLAUDE.md §12 — this is the required "named the cost in ms" step, and it is already done):

| Scope | over-budget warns in the raid | max |
|---|---|---|
| `HeroLocomotion.Update` | 23 | **11.2 ms** |
| **`TownActivityProbe.Update`** | **19** | **10.5 ms** |
| `StructureContentWarmer.Host.Update` | 1 | 24.2 ms |
| `VfxAuraProximityCuller.Update` | 1 | 6.1 ms |
| `HeroAbilities.Update` | 1 | 4.4 ms |
| `EnemyBrain.Update` | 1 | 1.1 ms |

⚠ **`BuildModeController.Update` (max 58.3 ms) and `WaveManager.Update` (max 54.0 ms) are NOT raid costs** — their warns sit at 13:08-13:09, before the raid scene loaded at 13:24. Deliberately recorded here so nobody re-opens them against this ticket.

## 3. ROOT CAUSE OF THE SECOND-LARGEST COST — proven at source

`Assets/_Modules/Village/World/TownActivityProbe.cs`:

```csharp
:61  [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
:62  private static void InstallHook()
:64      SceneManager.sceneLoaded -= OnSceneLoaded;
:65      SceneManager.sceneLoaded += OnSceneLoaded;
:66      TrySpawn();
:69  private static void OnSceneLoaded(Scene s, LoadSceneMode mode) => TrySpawn();
```

**The `Scene s` argument is discarded.** There is no scene filter, so a **town** probe re-spawns into every scene the game loads — raid bases included — and its `PollInterval = 3f` (`:56`) sweep costs up to **10.5 ms of a 16 ms frame** in a scene it has no business running in. Its 19 over-budget warns land at log lines 3038288-3059147, squarely inside the raid window.

## 4. THE FIX

1. **Scene-gate the probe.** `OnSceneLoaded` must use its `Scene s`: spawn only in the town/hub scene, and destroy any existing instance when a non-town scene loads. Do not merely skip the poll — do not keep the component alive in the raid.
2. **`HeroLocomotion.Update` at 11.2 ms is the largest named raid cost and it is not explained by this fix.** Add narrower `FlowTrace.Measure("Perf", …, 4f, 1f)` scopes inside it (the 4-arg accumulating overload, `Assets/_Modules/Core/Diagnostics/FlowTrace.cs:308`, per CLAUDE.md §12) so a follow-up ticket can name the sub-cost. ⛔ **Do not "optimise" it in this ticket without that measurement** — that is the guess §12 forbids.

## 5. ACCEPTANCE

A fresh Seeker capture of one Bastion raid:

1. `grep -c "TownActivityProbe.Update took"` between the raid's first and last `scene=RaidBase` sample is **0**.
2. No `LOW fps` sample in the raid window below **30**, and the distribution's floor is reported with the claim.
3. The new `HeroLocomotion` sub-scopes appear and name a dominant cost in ms.
4. `python tools/gate_brace.py` exit 0, then `COMPILE_GATE_OK` + `REGRESSION_OK <n>/<n>` on fresh logs.

## 6. DO NOT TOUCH

`BuildModeController`, `WaveManager`, the VFX loop budget (WO-1786 lane), the camera (WO-1785 lane), any `.unity` file.

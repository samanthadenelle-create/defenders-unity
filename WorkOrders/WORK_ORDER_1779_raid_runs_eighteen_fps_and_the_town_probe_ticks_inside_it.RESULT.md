# WORK ORDER 1779 — RESULT

**Status:** IMPLEMENTED, NOT YET GATED (no Unity gate, no commit — the lane was instructed to hand back before both)

**Lane:** raid frame budget. **Date:** 2026-09-16. **Branch:** `dev`.

**Files changed (2, exactly the silo):**

| File | Change |
|---|---|
| `Assets/_Modules/Village/World/TownActivityProbe.cs` | the scene gate + the WO-1779 header block |
| `Assets/Editor/Regression/TownSuspendSceneFloorRegression.cs` | new case (h) `raid-scene-gate`, header case list, success reason |

Nothing else was touched. No `.unity`, no bake, no `DataRegression.cs` (both suites this
lane depends on are ALREADY registered there — `TownSuspendSceneFloorRegression.Run` at
`DataRegression.cs:1216`, `FrameBudgetMeasureRegression.Run` at `:1854` — so the new case
reaches `REGRESSION_OK` with no edit to the lane-fenced file), no `CLI_LANES_WO_NUMBERS.md`,
no `BOARD.html` (the lead regenerates and commits it), and none of the fenced gameplay files.

---

## 1. EVIDENCE — re-measured in this session, from the capture, grep only

`logs/device/pull-20260916-143101-bastion-owner-run/logcat_full.txt` (377 MB; never opened,
only grepped/awk'd).

**The probe ran inside the raid, and said so itself.** Line 3038287:

```
09-16 13:24:29.335 I Unity : [Flow:TownProbe] scene='RaidBase_IronBastion' suspended=True
  grace=0.0s policy=SuspendAndResume reason='player active in 'RaidBase_IronBastion'
  (activeSceneChanged:RaidBase_IronBastion)' :: Enemy x23 in the ACTIVE scene (these MUST keep running)
```

Six such reports in the window, all naming `RaidBase_IronBastion`, all reporting nothing but
active-scene enemies — i.e. the probe paid its full enumeration cost to print the one thing it
is defined to be unable to act on.

**Its cost, 19 over-budget warns, 13:24:29.335 → 13:26:01.486, lines 3038288 → 3059147:**
7.3, 4.2, 5.0, 7.7, 5.8, 5.3, 8.3, 5.5, 6.4, **10.5** (line 3049140), 6.3, 5.9, 6.8, 5.4, 5.1,
4.7, 4.4, 5.1, 6.9 ms — against a 4 ms budget, on frames the same window measured at
`LOW fps=18 ms=56.5 ... scene=RaidBase_IronBastion` (line 3057519).

**The whole over-budget tally inside the raid window** (`awk 'NR>=3038129 && NR<=3070000'`,
then `[Flow:Perf] <scope> took`), which is how I confirmed this is ONE cause and not a sweep:

```
23  HeroLocomotion.Update
19  TownActivityProbe.Update      <- the only TOWN-ONLY system in the list
 1  VfxAuraProximityCuller.Update
 1  StructureContentWarmer.Host.Update
 1  HeroAbilities.Update
 1  EnemyBrain.Update
```

This reproduces the ticket's §2 table exactly. **No second town-only per-frame system is
provable from this capture**, so none was touched: `HeroLocomotion` / `HeroAbilities` are the
hero, who belongs in the raid; `EnemyBrain` is the raid's own enemies; `StructureContentWarmer`
is the addressables pump that must drain everywhere; and the last three are one warn each.

**Root cause, read at source:** `OnSceneLoaded(Scene s, LoadSceneMode mode) => TrySpawn();`
discarded its `Scene s`, so a `DontDestroyOnLoad` town probe was (re)spawned into every scene
the game loads.

---

## 2. THE FIX

`TownActivityProbe` now decides, per scene, whether it ticks at all.

* **`public static bool ShouldTickIn(string sceneName) => !HubScenes.IsRaid(sceneName);`** —
  the canonical classifier, not a fresh `StartsWith`. (`HubScenes.IsRaid` is
  `StartsWith("RaidBase", OrdinalIgnoreCase)`, `Assets/_Modules/Core/HubScenes.cs:61-65`.)
* **`public static bool ApplyGate(TownActivityProbe probe, string sceneName)`** sets
  `probe.enabled` — so Unity stops calling `Update` **at all** in a raid, rather than the probe
  early-returning inside a call it still pays for. Idempotent; logs one `FlowTrace.Step` per
  TRANSITION (greppable in the next capture, not a per-scene-load heartbeat); resets `_timer`
  on re-enable so the first post-raid report is immediate.
* **Both scene callbacks** are subscribed — `sceneLoaded` (additive loads that do not change the
  active scene) and `activeSceneChanged` (a `SetActiveScene` that loads nothing). Their relative
  order is NOT relied on and the comment says so: both funnel into the same idempotent gate,
  which resolves the ACTIVE scene name itself (`GateSceneName`, falling back to the handed scene
  while the active one is still unnamed). ⚠ **That unnamed-active fallback is defensive, not
  proven:** I never observed an unnamed active scene at `AfterSceneLoad` in the capture, so it is
  belt-and-braces, not a path with evidence behind it. `InstallHook` passes the real
  `SceneManager.GetActiveScene()` rather than a `default(Scene)`, so the fallback is not the
  normal boot path.
* **`_instance` is cached in `Awake`** and cleared in `OnDestroy`, so the gate can reach an
  instance whose Behaviour it has disabled without relying on `FindAnyObjectByType` semantics
  for a disabled component. The `Find` stays in `TrySpawn` as the duplicate guard only.
* **`ApplySceneGate` will not SPAWN into a raid** — a DDOL object whose whole lifetime is spent
  disabled is not worth creating.
* **Defence in depth at the POLL cadence (3 s), not per frame:** `Update` re-checks
  `ShouldTickIn(GetActiveScene().name)` after the timer expires and disables itself if a
  transition ever arrived without either callback.
* **The 4-arg scope STAYS and is still the FIRST `FlowTrace.Measure(` in the `Update` body:**
  `FlowTrace.Measure("Perf", "TownActivityProbe.Update", 4f, 1f)` — CLAUDE.md §12,
  instrumentation is permanent. Wherever the probe DOES run, its cost is still named in the
  roll-up.

### Two deliberate deviations, named rather than smuggled

1. **The `what` string stays `"TownActivityProbe.Update"`**, not the `"TownActivityProbe.Tick"`
   the brief wrote. Reasons: the WO's own acceptance §1 greps
   `"TownActivityProbe.Update took"`, the before/after Perf-table comparison keys on the same
   string, and `FrameBudgetMeasureRegression` locates this site by the `private void Update()`
   signature (`FrameBudgetMeasureRegression.cs:137`). Renaming it would make acceptance §1 pass
   trivially by making the string unfindable — the opposite of proof.
2. **The probe is DISABLED in raids, not DESTROYED off-hub.** The WO §4.1 says "destroy any
   existing instance when a non-town scene loads… do not keep the component alive in the raid";
   the brief says gate on `IsRaid` and disable/enable on scene change. Those diverge, and the WO
   wording as written would break the probe: `Poll` deliberately no-ops in a hub
   (`HubScenes.IsHub(active.name)` early-return, unchanged), so a probe destroyed on every
   non-hub load could never report anything, and WO-1017 — the ticket this very suite replays —
   was only ever visible **because** the probe was alive inside `Dungeon_HealersCottage`. I
   implemented the brief (raid-only gate) and am recording the divergence here for a ruling
   rather than resolving it silently. **If the owner wants dungeons gated too, it is one line in
   `ShouldTickIn` plus the `mustTick` array in case (h)** — but it would delete the only observer
   of the thing the probe exists to observe.

---

## 3. REGRESSION — headless, no PlayMode

`TownSuspendSceneFloorRegression` case (h), registered as `raid-scene-gate` in `Run`, reached by
`REGRESSION_OK` through `DataRegression.cs:1216` and standalone through
`DeNelle.Editor.Regression.TownSuspendSceneFloorRegression.RunAll`
(`TOWN_SUSPEND_FLOOR_OK` / `_FAIL`).

It is **behavioural, not a source lint**, deliberately: what it pins is a DECISION (which scenes
the probe runs in), and a lint grepping for the string `IsRaid` would pass a gate wired backwards.
`DeNelle.EditorRegression.asmdef` references `DeNelle.Village`, so it calls the real members.

* **gated OFF:** `RaidBase_IronBastion` (the captured scene), `RaidBase_Ashfell`,
  `raidbase_lowercase_spelling` (because `IsRaid` is `OrdinalIgnoreCase` — a gate that became
  case-sensitive would let a renamed scene back through).
* **still ticks:** the hub (`SceneRouter.Castle`), `Dungeon_HealersCottage` (the WO-1017 capture
  scene), `dg_starter_loop`, `KayKitChallengeOutpost`, `Garrison_Northwatch`, `ATBBattle`. This
  is the half a careless fix breaks and it carries the reason in its failure text.
* **null / empty** must NOT gate off — a load in flight is not a raid.
* **the component gate, both directions, on a real instance:** `AddComponent` on a throwaway
  GameObject (`DestroyImmediate` in `finally`), assert `enabled == false` after
  `ApplyGate(probe, "RaidBase_IronBastion")` and `enabled == true` after `ApplyGate(probe, Hub)`,
  plus a re-apply to prove idempotence (the two callbacks both fire for one transition).
  `TrySpawn` is never called from the test — it calls `DontDestroyOnLoad`, invalid in edit mode —
  which is precisely why `ApplyGate` takes its instance explicitly.

Case 7's pins (`FlowTrace.Fail`, `"MUST keep running"`) are untouched, and so is every string in
`Poll`.

---

## 4. PROOFS RUN IN THIS SESSION

```
python tools/gate_brace.py Assets/_Modules/Village/World/TownActivityProbe.cs \
                           Assets/Editor/Regression/TownSuspendSceneFloorRegression.cs
GATE_BRACE_SUMMARY bad=0 of 2      EXIT=0
```

NUL scan (CLAUDE.md §1 / §0): both files `no-NUL`. Raw brace counts balanced —
probe 20/20 (was 12/12), regression 39/39.

Board-parser check on the flipped status line, run without writing `BOARD.html`:
`malformed=False near_miss=False bucket=('Done', False) contradiction=''`.

---

## 5. STILL OWED — do not read this ticket as verified

* ⛔ **No Unity gate and no commit.** `COMPILE_GATE_OK` and `REGRESSION_OK <n>/<n>` on fresh logs
  are the lead's, and acceptance §4 is unmet until they land.
* ⛔ **THE DEVICE MEASUREMENT IS THE ACTUAL PROOF AND IT DOES NOT EXIST YET.** From the next
  Seeker capture of one Bastion raid, between the first and last `scene=RaidBase` sample:
  1. `grep -c "TownActivityProbe.Update took"` must be **0** (it was **19**, max **10.5 ms**).
  2. The new `[Flow:TownProbe] scene='RaidBase_…' is a RAID -> probe DISABLED` line must appear
     once, proving the gate fired rather than the probe merely going quiet.
  3. The `LOW fps` distribution must be reported with its floor. Baseline from this capture:
     **18, 21, 22, 22, 23, 24, 26, 27, 32, 33×3, 34×2, 35, 36×3, 37, 38×2, 39×3, 40** — floor
     **18 fps / 56.5 ms**. Acceptance §2 wants no sample below 30. **I do not expect this fix
     alone to reach that**, and saying so is the honest position: the probe was the SECOND-largest
     named cost, and its warns were ~6 ms over budget on a frame that was ~40 ms over.
* **Re-lane, not done here:** §4.2 — `HeroLocomotion.Update`, **23 warns, max 11.2 ms**, the
  LARGEST named raid cost, needs narrower 4-arg `FlowTrace.Measure("Perf", …, 4f, 1f)` sub-scopes
  before anyone touches it (§12 forbids optimising it unmeasured). `HeroLocomotion.cs` is outside
  this lane's file list, so **acceptance §3 is NOT met by this lane.**
* **Observed, not touched, and not a defect claim:** `StructureContentWarmer.Host.Update`
  (1 warn, 24.2 ms) and `VfxAuraProximityCuller.Update` (1 warn, 6.1 ms) inside the raid window —
  one warn each, and the VFX loop budget is the WO-1786 lane.
* The ticket's own note stands and was re-confirmed by the window tally:
  `BuildModeController.Update` (58.3 ms) and `WaveManager.Update` (54.0 ms) produced **no** warns
  inside lines 3038129-3070000. They are not raid costs.

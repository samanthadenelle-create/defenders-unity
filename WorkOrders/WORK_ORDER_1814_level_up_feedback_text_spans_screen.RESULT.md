# WO-1814 RESULT: "LEVEL UP! Lv.8" feedback text spans entire screen

**Status:** IMPLEMENTED

⚠ This RESULT **replaces** the 2026-09-16 first-attempt RESULT, whose fix (a `1.4 -> 1.0`
scale clamp) was **wrong and is reverted**. The refutation is kept below and encoded as a
regression case, because the clamp is the obvious "simplification" a later seat would restore.

## The measurement (all from the capture, none inferred)

Primary evidence: `logs/device/owner-fireball-20260916/Screenshot_20260916-205131.png`
(owner's Seeker, build 372984, 2026-09-16 20:51:31, town wave 13) and
`logs/device/owner-fireball-20260916/logcat.txt`.

| Term | Value | Where it was read |
|---|---|---|
| Label string / spawn | `LEVEL UP!  Lv.8` | logcat 20:51:30.479 `[Flow:Feedback] text label spawned` |
| Spawn call | `SpawnLabel(..., scale: 1.4f)` | `ProgressionManager.cs:152-153` |
| TextMesh terms | `characterSize 0.11`, `fontSize 96` | `DamageNumberSpawner.cs:360-361` (pre-edit) |
| Parent | **none** — `SetParent(null, false)` | `DamageNumberSpawner.Acquire`, `:270`. No inherited scale. |
| Camera vfov | **60 base** (combat pump up to +4) | `Main_Castle_Overworld.unity:22981` `field of view: 60`; `SmartMobileCamera.cs:112` `_combatFovBoost = 4f` applied at `:1706`. ⚠ The `fov=60.0` lines in this logcat are `[Flow:WorldFeel]` reads from **Title / RaidBase / dungeon** scenes, NOT this scene at 20:51 — they are not cited as proof. At 64 the expected cap is ~290 px, at 60 ~314 px; observed 318 fits 60 better and the conclusion is unchanged either way. |
| Camera seat | **3.3 - 4.7 m** from the hero | `[Flow:Camera]` lines 20:48:25 - 20:50:40 (`seat 3.9m of 3.9m`) |
| Frame | 2670 x 1200 | the PNG |

A Unity `TextMesh` renders its em box at `characterSize * fontSize / 10` **world units**:

```
world em  = 0.11 * 96 / 10 * 1.4                       = 1.478 m
px per m  = 1200 / (2 * tan 30)                        = 1039.2  (per metre of distance)
on-screen = 1.478 * 1039.2 / 3.5 m                     = 439 px em  ->  ~314 px cap height
```

**Observed in the PNG: 318 px of cap height.** Measured numerically, not by eye — green ink
(`G-R>18 && G-B>18`) occupies rows **y = 160..478** and no row above 160. Expected 314 px vs
observed 318 px.

Independent cross-check of the 3.5 m seat from the same frame: the hero's shoulder span reads
~145 px, i.e. ~290 px/m, which back-solves to d = 1200 / (2·tan30 · 290) = **3.6 m**. Two
independent reads agree, so the distance is measured, not assumed.

**Second cross-check — the control frame.** `Screenshot_20260916-205116.png`, the SAME session
15 s earlier, shows the hero at ~200 px of full-body height (~118 px/m, **d ≈ 8.8 m**), matching
the logged `seat HELD at 10.2m` at 20:50:59. That frame carries a level-up too (`+1777 XP`, Lv 7)
and **no oversized world label appears**. The identical code is fine at ~9 m and catastrophic at
~3.5 m. That is the distance dependence, demonstrated rather than argued.

## The term that is wrong

**Nothing was mis-scaled. The camera-distance term does not exist.** The label is sized in
metres and attached to the closest object in the scene — the hero — under an over-the-shoulder
camera 3.5 m away. Damage numbers hide the identical defect only because enemies stand further
out. Horizontally the same arithmetic explains the rest of the symptom: 15 characters at
~0.62 em advance = ~9.3 em = ~4.1 m of world width against ~8.9 m of visible width at 3.5 m,
which is why the string runs off **both** edges once the pop-scale and bold advance are in.

**Why the previous clamp could not have worked:** `1.0` through the same arithmetic gives
`1.056 m -> 314 px em -> ~224 px cap = 19% of screen height`, with the string still ~1.3x wider
than the screen. A 1.2->1.0 (in fact 1.4->1.0) change cannot turn screen-height letters into a
label, and it did not.

## Fix

`Assets/_Modules/Village/Enemies/DamageNumberSpawner.cs`

- **:58-63** — `LabelFontSize = 96` extracted to ONE const, now used by `Build`, `BuildLabel` and
  the pure helper, so the arithmetic the suite trusts cannot drift from the TextMesh it describes.
- **:68-137** — new constants + two pure statics:
  `LabelReferenceDistance = 10 m`, `MinDistanceFactor = 0.30`, `MaxDistanceFactor = 1.0`,
  `LabelWorldEmHeight(worldScale)` and `DistanceCompensatedScale(requestedScale, cameraDistance)`
  (`k = clamp(d / 10, 0.30, 1.0)`). The derivation above is written into the comment block.
- **:418-426** (`BuildLabel`) — the old `_baseScale = Mathf.Min(scale, 1.0f)` clamp is **removed**
  and replaced by `DistanceCompensatedScale(scale, camDistance)`; `_rise` rides the same factor
  (a 1.6 m climb at a 3.5 m camera sweeps ~40% of the screen and reads as the label flying away).
- **:450-461** — the trace is kept and upgraded from `Step` to `Throttle` (it bursts on level-up)
  and now names **every** term: `requested`, `camDist`, `worldScale`, `emHeight`, `rise`, `parent`.
  Parts are computed into locals — a quote inside a `$"..."` hole breaks `CompileGate.BraceBalanced`
  (CLAUDE.md §1).

`MaxDistanceFactor = 1.0` is deliberate: **no label may ever be larger in world units than it was
before this change**, so the edit can only shrink.

Result at the owner's exact inputs: `1.4 * clamp(3.5/10) = 0.49` -> **world em 0.517 m**,
**154 px em / ~110 px cap = 9% of screen height**, string ~1428 px of a 2670 px frame.
Inside the clamp band the on-screen size is now **constant with distance**.

`SpawnResourceGain` rides `SpawnLabel`, so the "+N wood" pops at the hero get the same fix free.

## Deliberately NOT changed

`Build` (damage numbers) carries the **same untreated term** — a 40+ damage hit at 2 m would be
1.27 m of world em. It is left alone because the ticket's acceptance criterion 5 is "no regression
in damage numbers", and enemies are rarely at the camera. **Recorded as an open finding**, not
fixed silently.

## Regression

New: `Assets/Editor/Regression/FeedbackLabelSizeRegression.cs` — `[feedback-label-size]`, 7 cases,
`public static bool Run(out string reason)` + its own `FEEDBACK_LABEL_SIZE_OK` batch marker.
Registered with ONE line at `Assets/Editor/Regression/DataRegression.cs:427`.

1. `[observed-capture]` 1.4 at the measured 3.5 m seat -> world em <= 1.5 m AND <= 14% of screen height
2. `[screen-constant]` on-screen height identical (<=1%) at 4 m and 9 m — the invariance IS the fix
3. `[never-larger-than-before]` no distance exceeds the pre-WO-1814 world size
4. `[close-floor]` a 1 m camera does not erase the label
5. `[string-fits-width]` `LEVEL UP!  Lv.8` fits inside 2670 px (states its 0.62 em advance assumption)
6. `[clamp-alone-is-not-a-fix]` the refuted first attempt, kept as arithmetic
7. `[source-pin]` `BuildLabel` still calls `DistanceCompensatedScale` and still carries its FlowTrace

## Verification

- `python tools/gate_brace.py` on all three files: `GATE_BRACE_SUMMARY bad=0 of 3`
- NUL scan: 0 bytes in all three files
- The arithmetic above is reproducible from the PNG and the logcat; the suite computes it in-process

## Not proven

- **No Unity run.** This lane is forbidden Unity/git, so `COMPILE_GATE_OK` and
  `REGRESSION_OK <n>/<n>` are **not** claimed — the lead must gate. The new `.cs` also has no
  `.meta` yet; Unity generates it on next import.
- No `[Flow:Camera]` line exists at 20:51:30 itself. The 3.5 m seat is the wave-time range
  (3.3-4.7 m, 20:48-20:50) corroborated by the hero's on-screen size in the same frame. The
  conclusion holds across 2.5-5 m (pre-fix em = 22-44% of screen height throughout).
- The 0.62 em bold-cap advance is a standard Arial Bold figure, not measured from the font asset.

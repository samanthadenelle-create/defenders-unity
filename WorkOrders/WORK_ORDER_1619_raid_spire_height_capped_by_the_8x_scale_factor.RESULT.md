# WO-1619 RESULT - step 2 (the fix) + the step-2 suite case

**Lane:** SPIRE-2, 2026-09-10. **EDIT-ONLY** - no Unity, no gate, no commit, no bake run here.
**Base:** `406dbda07` (worktree `.claude/worktrees/agent-a43ca968d138479be`, fast-forwarded from
`dev`, tree clean before the edit).
**Files changed:** `Assets/Editor/WallTools/RaidBaseGenerator.cs`,
`Assets/Editor/Regression/RaidSpireSiegeRegression.cs`. Nothing else.

---

## 1. The measurement that licensed this edit (sec.4 step 1 - READ, not assumed)

`Builds/wave2-bake` (2026-09-10 01:35, written by the step-1 instrumentation). Read this session.
**Measured population = THREE baked scenes** - `BuildAllRaidScenes` iterates exactly
`raider_camp_small`, `fortified_garrison`, `mage_enclave`. Not "three of four": five configs author
a `centralBuilding`, three are baked.

All three lines, verbatim, identical to three decimals:

```
[RaidBaseGenerator] SPIRE FIT config 'raider_camp_small' spire 'tower_arcane_spire': rawHeight=1.002m prefabScaleBefore=0.010 target=14.40m wantedFactor=14.366 appliedFactor=8.000 saturatedAt=UPPER achieved=8.02m (56% of target) - SATURATED: the fit could NOT reach the height the generator asked for. (WO-1619 step 1 instrumentation.)
[RaidBaseGenerator] SPIRE FIT config 'fortified_garrison' spire 'tower_arcane_spire': rawHeight=1.002m prefabScaleBefore=0.010 target=14.40m wantedFactor=14.366 appliedFactor=8.000 saturatedAt=UPPER achieved=8.02m (56% of target) - SATURATED: the fit could NOT reach the height the generator asked for. (WO-1619 step 1 instrumentation.)
[RaidBaseGenerator] SPIRE FIT config 'mage_enclave' spire 'tower_arcane_spire': rawHeight=1.002m prefabScaleBefore=0.010 target=14.40m wantedFactor=14.366 appliedFactor=8.000 saturatedAt=UPPER achieved=8.02m (56% of target) - SATURATED: the fit could NOT reach the height the generator asked for. (WO-1619 step 1 instrumentation.)
```

What that settles:

- **`saturatedAt=UPPER` on 3 of 3.** Sec.4's FIRST branch - the cap axis - is the confirmed one.
  No line read `none` and none read `LOWER`, so neither of the WO's "re-scope" branches fired.
- **`rawHeight=1.002m` is now MEASURED**, and sec.1d's forbidden derivation (`8.0 / 8 = 1.0`) is
  vindicated *as an inference that happened to be right*. It is written as measured here and
  nowhere as derived.
- **All three spires resolve to `tower_arcane_spire`**, i.e. sec.1c's prediction that Easy would
  also resolve to it after WO-1617 is confirmed by the log, not carried forward as an assumption.

### The `prefabScaleBefore=0.010` datum - reconciled at source, NOT guessed

Sec.4's heuristic said a `prefabScaleBefore` materially off `1.000` means "the target, not the cap,
is the wrong axis". **That heuristic is superseded by where the measurement is actually taken**, and
the source says so:

- `RaidBaseGenerator.cs:1330-1332` - `raw` is built from `Renderer.bounds` (`rends[0].bounds`,
  `b.Encapsulate(rends[k].bounds)`). `Renderer.bounds` is a **world-space** AABB, so `raw = 1.002m`
  **already includes** the prefab's `0.010` localScale.
- `RaidBaseGenerator.cs:1343` - `float wanted = target / raw;` is therefore world-metres over
  world-metres. The prefab's pre-scale never enters the factor.

So the pre-scale does not make the 14.40 m target wrong; it only explains why the factor needed is
large (14.366). **The cap is the wrong axis. The target is innocent.** I did **not** measure the
prefab's mesh units and make no claim about them.

---

## 2. What changed - `Assets/Editor/WallTools/RaidBaseGenerator.cs`

### 2a. Two new named tunables, `:159-160` (declared beside the WO-1617 three at `:132-134`)

```
internal const float SpireFitFactorMin = 0.2f;
internal const float SpireFitFactorMax = 24f;
```

Preceded by the derivation comment at `:136-158`. The ruling implemented (sec.3): **the factor
bound is not the authority for a monument fit.** `SpireMonumentMinHeight` / `SpireMonumentMaxHeight`
own "how tall should this be", and the achieved height can never exceed `SpireMonumentMaxHeight`
regardless of this ceiling, because the TARGET is clamped there at `:582-583` before the fit runs.
The bound's only surviving job is refusing to magnify degenerate art.

**Value derivation, from measured numbers only:** the tallest target the monument path can ask for
is `SpireMonumentMaxHeight = 18f` (`:134`); the measured rendered height of the shipped spire art is
`1.002 m`; `18 / 1.002 = 17.96` is the largest factor today's art can legitimately need. `24f`
clears that with margin and is non-binding across the whole authored `[8 m, 18 m]` target range for
the art we ship. **The exact margin (24 vs 18 vs 32) is a lead/owner-tunable choice, not a
measurement** - it is a named tunable so retuning it is one edit.

**`SpireFitFactorMin` did NOT move** (sec.5 pin: the lower bound is doing real work against
oversized art). It is named only so zero magic literals survive in the fit path.

### 2b. The clamp, `:1344`

`float f = Mathf.Clamp(wanted, 0.2f, 8f);` -> `float f = Mathf.Clamp(wanted, SpireFitFactorMin, SpireFitFactorMax);`

**That is the entire behaviour change: one line.**

### 2c. Two stale comments corrected in the same edit (CLAUDE.md sec.15)

- `:117-131` - the WO-1617 const-block header said *"the arcane-spire bake must land on the
  identical height it did before (8.0 m)"*. **8.0 m was never a target, it was the saturation.** The
  header now carries the three measured bake lines and states that the monument tunables did not
  move to reach 14.40 m.
- `:1289-1312` - `ScaleToHeight`'s doc said the `0.2f` / `8f` bounds were *"DELIBERATELY still bare
  literals"* pending step-2 numbers. The numbers are in; the doc now records them, the world-space
  `Renderer.bounds` reconciliation from sec.1 above, and the two tunables.

### 2d. Pins held - nothing else in this file moved

`EnsureUpright` and its threshold (sec.7) - untouched. `MeasuredHeight` - untouched, still the
exempt path's reporter. `IsAuthoredSiegeMachine`, `ResolveSpireArtId`, the authored-siege exemption
inside `PlaceSpire`, `PlaceTowerProp`'s guard, `RaidSpire.Configure`'s height argument, spire HP,
the step-1 instrumentation lines - all untouched. `SpireMonumentMultiplier` / `MinHeight` /
`MaxHeight` values unchanged. `ScaleToHeight` still has exactly one caller in this file
(`:642`, `grep -n "ScaleToHeight"` this session). The identically-named private statics in
`BattleAnchorStageVerify.cs`, `TreeOfLifeMaterialFixer.cs`, `HubFoliageInjector.cs`,
`Village2Generator.cs` are unrelated and were not touched.

---

## 3. What changed - `Assets/Editor/Regression/RaidSpireSiegeRegression.cs`

**Extended, not minted** (sec.6). The suite was already registered by WO-1617, so **no registration
line is owed to the lead.**

### `CaseScaleFactorBoundsAreTunable` (`:267`), wired into `Run` at `:79`

Four assertions:

1. `internal const float SpireFitFactorMin =` and `... SpireFitFactorMax =` both declared in the
   generator source.
2. `MethodBody(gen, "private static float ScaleToHeight(")` contains
   `Mathf.Clamp(wanted, SpireFitFactorMin, SpireFitFactorMax)`.
3. That same **body** does not contain `, 0.2f, 8f)`. Scoped to the body via `MethodBody` on
   purpose, so the doc comment above the method (which quotes the retired literals as history) can
   never false-fail the case.
4. **The invariant, not a hardcoded 24:** `SpireFitFactorMax >= SpireMonumentMaxHeight`, both values
   parsed out of the source by the new `ConstFloat` helper (`:376`). This pins the *ruling* - the
   ceiling is non-binding for one-metre-class art exactly when it is at least the tallest target the
   monument clamp may ask for - and lets the lead retune the margin in one edit without touching
   this suite. `ConstFloat` returns `0f` when it cannot read a value and every caller reports that
   as a failure; an unreadable value never passes as satisfied.

**RED-first, per sec.6.** At base `406dbda07` the case fails on assertions 1, 2 and 4 - the two
consts did not exist and `ScaleToHeight` read `Mathf.Clamp(wanted, 0.2f, 8f)` (assertion 3 also
fired). **Named mutation that re-reds it after this edit:** re-inline either bound at the
`Mathf.Clamp` call site (assertions 2+3 red), **or** set `SpireFitFactorMax` below
`SpireMonumentMaxHeight` - e.g. back to `8f` - which reds assertion 4 alone and is the exact
regression this ticket exists to prevent.

### Two suite doc comments corrected

- `CaseMonumentFitIsTunable` (`:168`) - its *"must still land on 8.0 m"* note is retired for the
  same reason as 2c. The case body and its pinned values are **unchanged and still green**.
- `CaseSaturationIsReported` (`:212`) - its *"NOT PINNED HERE ... RED at this commit by design"*
  paragraph now points at the case that landed.

Brace discipline held: no new `{` / `}` character literals (the file's `OpenBrace` / `CloseBrace`
consts at `:56-57` remain the only two).

---

## 4. The bake the lead must run to prove it

Editor closed (CLAUDE.md sec.3). Batchmode:

1. `DeNelle.Editor.RaidBaseGenerator.BuildAllRaidScenes`
2. `DeNelle.Editor.RaidNavBake.BakeAll` - **required if the re-baked scenes are kept** (sec.7: it
   drops RaidGround and bakes the NavMesh).

Read the log with PowerShell, not `grep` - Unity logs are UTF-16:

```powershell
Select-String -Path Builds\<bake>.log -Pattern 'SPIRE FIT|SPIRE ''' | ForEach-Object { $_.Line }
```

**Expected post-fix line, all three configs** (`f` = 14.366 is now inside the bound, so
`achieved = 1.002 * 14.366 = 14.395` -> prints `14.40`):

```
[RaidBaseGenerator] SPIRE FIT config '<id>' spire 'tower_arcane_spire': rawHeight=1.002m prefabScaleBefore=0.010 target=14.40m wantedFactor=14.366 appliedFactor=14.366 saturatedAt=none achieved=14.40m (100% of target) - fit satisfied. (WO-1619 step 1 instrumentation.)
```

and the companion line becoming `SPIRE 'tower_arcane_spire' placed at centre: <hp> HP, 14.4m tall`.
Three lines, at `Debug.Log` not `Debug.LogWarning`. **Anything else is the finding**, not this
RESULT's prediction.

### Downstream consequence the felt-test should look for (real, and intended)

`RaidSpire.Configure` receives the achieved height (`RaidSpire.cs:143-148`), and two things scale
off it: the fallback capsule hitbox (`:200-213`, height + centre) and the collapse sink depth
(`:297`). Both grow with the spire, which is correct - but the spire's **hit volume roughly doubles
in height**, so troop/hero targeting against the win-condition object is worth an eye on device.
Checked this session: neither `RaidBaseLayoutRegression` nor `RaidArenaShapeRegression` contains any
height or `8f` string, so neither pins the old number; no oracle re-point is owed.

---

## 5. NOT proven by this lane (CLAUDE.md sec.11B)

- **No bake was run and no gate was fired here.** Every post-fix number in sec.4 is an *arithmetic
  prediction from the measured pre-fix line*, not a measurement. The re-bake is the proof.
- **The new suite case has not been executed** - the lane holds no Unity lock. What is proven about
  both `.cs` files: `python tools/gate_brace.py` -> `GATE_BRACE_SUMMARY bad=0 of 2`, exit 0; zero
  NUL bytes (`tr -d -c '\000' | wc -c` = 0 on each); naive brace counts 258/258 and 36/36.
- **No screenshot** (sec.11's eye-level before/after criterion). A log line does not prove a
  silhouette; the lead owns the capture and the owner owns the verdict.
- **The `24f` margin is a choice, not a measurement.** `>= 18` is what the data requires; anything
  above that is judgement, and the suite pins the relation rather than the number so a retune is
  cheap.
- **Sec.8's Forsaken Camp art question is untouched and still the owner's.** No JSON was edited; no
  art was picked. The `tower_siege_tower` -> `tower_arcane_spire` substitution warning still fires
  by design.
- **Two literals survive on the height path and were left ALONE on purpose** - naming them so the
  lead reads "deliberate", not "missed": `raw <= 0.0001f` in `ScaleToHeight` (`:1335`) is a
  degenerate-bounds DETECTION epsilon, not a tuning bound, and `float targetHeight = 9f;` in
  `PlaceSpire` (`:572`) is the pre-catalog default target, upstream of the fit and untouched by
  WO-1617's own tunable pass. Neither is a fit bound; promoting either is a ruled call, not this
  lane's.
- No `repo.visualHeight` was authored - the data named the cap axis, so sec.7's conditional
  canonical-twin edit was not licensed and was not made.

## 6. Question for the owner (one)

**None on the fix.** The one open item is already hers and unchanged: sec.8, the Forsaken Camp's
spire art. This ticket makes that spire twice as tall, which raises the stakes on the answer but
does not change the question.

# WO-1594 RESULT — honor clock: three lit at engage, snuffed as milestones pass

**Status:** IMPLEMENTED - awaiting gate (2026-09-09 lane RAID)
**Lane:** RAID (edit-only; no Unity run, no commit from this lane)
**Branch/HEAD at start:** `dev` @ `184c8ff06`

---

## 1. Provenance — ported, NOT merged

`docs/READY_RCA_2026-09-09.md` row 1594:

> The honor-clock commit `5c3c82de2` exists only on `grok/raid-1593-1595` and is **not** an ancestor
> of HEAD. HEAD has a comment mentioning `ComputeHonorStars` but no implementation.

Verified this session: `git show 5c3c82de2` reads, and `RaidScoring.cs` at HEAD carries only the
COMPOSE NOTE in `ApplyHeroDeathCap`'s doc-comment anticipating it. The branch was **not** merged; the
specified behaviour was re-implemented onto HEAD.

**Owner rulings carried across verbatim (recorded in that commit, 2026-09-07):**

> 1594 you determine what is realistic and fair
> 1594 q2 yes

→ **T3 = 90 s, T2 = 150 s, D2 = 50 %**, and **hero death snuffs ★★★ = YES**.

---

## 2. What HEAD already had, and therefore was NOT re-added

The grok diff assumed a HEAD that predates WO-1526. Re-adding its hunks verbatim would have
double-declared four members and dropped a pinned call:

| symbol | at HEAD | action |
|---|---|---|
| `_heroDied`, `HeroDied`, `NotifyHeroDied` | present (WO-1526) | **not re-added** |
| `ApplyHeroDeathCap`, `HeroDeathStarCap` | present, and **pinned** by `RaidScoringRegression` | **kept in `Finalize`** |
| `HeroHealth` → `NotifyHeroDied` call | already wired at HEAD, and pinned by `RaidScoringRegression` | **not touched** (outside this lane's files either way) |
| `ProjectedStars` | applies `ApplyHeroDeathCap` at HEAD | **left exactly as HEAD has it** |

Grok's `Finalize` line was `Mathf.Min(settleStars, honorStars)`, which drops `ApplyHeroDeathCap`. It
is numerically subsumed by the honor clamp, but it is the **pinned statement** of the owner's WO-1526
ceiling, so HEAD's version is:

```
int cappedStars = ApplyHeroDeathCap(earnedStars, _heroDied);
int honorStars  = ComputeHonorStars(_engaged, _elapsed, destruction, _heroDied);
int stars       = Mathf.Min(cappedStars, honorStars);
```

---

## 3. Files changed (all inside this lane)

| File | Change |
|---|---|
| `Assets/_Modules/Village/Troops/RaidScoring.cs` | `HonorThirdStarSeconds` / `HonorSecondStarSeconds` / `HonorSecondStarMinDestruction`; pure `ComputeHonorStars`; `PresentationStars`; `_lastHonorStars` + `TraceHonorStarSnuff` (permanent `star-lost reason=` trace); `NotifyEngagement` lights 3 and says so; `Finalize` honesty clamp |
| `Assets/_Modules/Village/Troops/RaidHudController.cs` | binds `PresentationStars`; star SHAPE (size) carries lit-vs-lost; unscaled pop on the star that just died; header corrected |
| `Assets/Editor/Regression/RaidWatchdogHonorRegression.cs` | new suite, Cases C + D (shared with WO-1095) |

**Not touched:** `HeroHealth.cs`, loot tables, `ComputeStars`, the countdown (already `RemainingSeconds`
rendered `M:SS` with a sub-30 s pulse — §3 of the WO asked for it and it was already there).

---

## 4. Colourblind law — the one place this deviates from the grok hunk

Grok's HUD change was `color = i < stars ? StarLit : StarDim` only. **Alpha dimming is luminance, not
shape**, and the owner is red/green colourblind (repo law, memory
`owner-colorblind-delegate-visual-creative`). The WO's own §2.3 asks for "shape fill (lit vs hollow)".

Implemented as three hue-free channels: a snuffed diamond **shrinks** (34 → 20 reference px, a
silhouette difference), the star that just died **pops** outward once over 0.45 s unscaled (motion),
and the `n/3` count is unchanged (number). All strings ASCII.

---

## 5. RED-first proof

Case C of the new suite, hand-evaluated against the pre-fix tree:

- **Pre-fix**, `RaidScoring.ComputeHonorStars` does not exist → Case C cannot compile/resolve; the
  behavioural discriminator once it does exist is
  `ComputeHonorStars(engaged:true, elapsed:90.01, destruction:0, heroDied:false) == 2`.
  A projector that returned a constant 3 (or the old earn-up `ComputeStars` shape, which returns **0**
  at 90 s with nothing cleared) fails it as
  `[WO-1594] ComputeHonorStars [past T3 the third star is gone] expected 2, got 3` (resp. `got 0`).
- **The clamp shape** (grok's own RED case, kept): `settle = ComputeStars(true,true,1f,100f,180f,1f) = 3`,
  `honor = ComputeHonorStars(true,100f,1f,false) = 2`, `min = 2`. Any implementation where the end
  screen can pay a star the live HUD had already put out fails with
  `honesty clamp shape is wrong: settle=… honor=… min=…`.
- **The HUD binding** (Case D): asserting `ProjectedStars` is **absent** from comment-stripped
  `RaidHudController.cs` fails RED on the pre-fix tree, where line 295 read `int stars = s.ProjectedStars;`.
- Added beyond grok's set: **monotonicity** (honor may never relight a star — without it the
  `min(settle, honor)` clamp is unsound), a 0..3 range sweep, and
  `ComputeHonorStars(heroDied:true) <= HeroDeathStarCap` so Q2 and WO-1526 cannot drift apart.

All nine tabled honor cases plus the clamp shape were reproduced in a scratch simulation this
session; every expectation matched the suite.

---

## 6. Brace / NUL gate (CLAUDE.md §1)

| file | NUL | `{` | `}` |
|---|---|---|---|
| `Assets/_Modules/Village/Troops/RaidScoring.cs` | none | 128 | 128 |
| `Assets/_Modules/Village/Troops/RaidHudController.cs` | none | 27 | 27 |
| `Assets/Editor/Regression/RaidWatchdogHonorRegression.cs` | none | 35 | 35 |

Existing oracles re-checked against the edited source:

- `RaidStagingMarkerRegression`: `_elapsed +=` occurs **1×**, `_engaged = true` occurs **1×** and sits
  inside `NotifyEngagement`, the `if (!_engaged)` gate still precedes the clock line, `clock started
  reason=` still present, and no `_grace|graceSeconds|GracePeriod|StagingCountdown` token was
  introduced. ✓
- `RaidScoringRegression`: `Finalize` still contains `ApplyHeroDeathCap`; `NotifyHeroDied`,
  `HeroDeathStarCap`, `ComputeStars`, `ComputeLoot`, `OnTimeExpired` all still present; the HUD still
  contains `ElarionUiKit`, `RaidScoring`, `RemainingSeconds`, `DestructionPct` and still no
  `uxml`/`UIDocument`/`VisualElement`. ✓
- `RaidTerminalStateRegression` Case A drives the real `RaidScoring.Update`; `TraceHonorStarSnuff` is
  called from it and is null-safe (`DestructionPct` handles a null spawner and null spire).

---

## 7. Acceptance, line by line

1. [x] 3/3 lit + countdown at engagement — `NotifyEngagement` latches 3, `PresentationStars` returns 3,
       HUD binds it; countdown already `M:SS` from `RemainingSeconds`.
2. [x] Crossing T3 snuffs the third with visible feedback (size drop + pop + `n/3` + `star-lost` trace).
3. [x] Staging does not advance the honor clock — `ComputeHonorStars(engaged:false) == 0` and
       `_elapsed` is engagement-gated (WO-1520, untouched).
4. [x] End-screen stars ≤ what the HUD showed — `Finalize` clamps `min(cap(settle), honor)`.
5. [ ] `COMPILE_GATE_OK` + suite green — **orchestrator's gate; not run from this edit-only lane.**
6. [ ] PNGs of the HUD at 0 s / post-T3 / post-T2 — **needs a capture run. NOT DONE.**

## 8. Unproven / flagged

- **No PNG, no play session, no gate from this lane.** Every visual claim above is source-level.
- **Edge recorded in code, not hidden:** a raid that settles with `_engaged` still false clamps to 0
  honor stars. Today that is the staging-retreat case, where `ComputeStars` returns 0 anyway, so the
  clamp is not what zeroes it. If a future change lets a camp be razed without ever tripping
  `NotifyEngagement`, this is where it surfaces — and the fix would be the engagement detector.
- **T3/T2/D2 are consts, not `RemoteTunables` rows.** They are owner-ruled creative milestones the WO
  specifies as constants, and the grok commit she reviewed used consts. Flagged for a ruling rather
  than promoted unilaterally: if she wants to tune them without a rebuild, they should join the rail
  alongside `raid.stagingCeilingSeconds` (WO-1095).
- `docs/MASTER_CATALOG/village-enemies-world.md` entries for `RaidScoring` / `RaidHudController` /
  `RaidDeployController` are now behind the code (§15). That file is outside this lane.

---

# WO-1594 ADDENDUM - 2026-09-09, lane RAID-2: THE MILESTONES ARE NOW TUNABLES

**Owner ruling, verbatim choice: *"Tunables with those defaults"*.** This closes the third bullet of
section 8 above ("T3/T2/D2 are consts, not `RemoteTunables` rows... flagged for a ruling rather than
promoted unilaterally"). They are rows now, at 90 / 150 / 50.

## A1. What changed, all in one change

| File | Change |
|---|---|
| `Assets/_Modules/Core/Ops/RemoteTunables.cs` | 3 `public const int ...Default` (90 / 150 / 50), 3 key consts, 3 `TunableSpec` registry rows |
| `Assets/_Modules/Village/Troops/RaidScoring.cs` | `HonorThirdStarSeconds` / `HonorSecondStarSeconds` / `HonorSecondStarMinDestruction` become `public static float` properties reading the rail through `SpecFor` |
| `Assets/Editor/Regression/RemoteTunablesDefaultsRegression.cs` | `ExpectedKnobCount` 46 -> 49, 3 `ExpectedDefaults` rows, summary string |
| `Assets/Editor/Regression/RaidWatchdogHonorRegression.cs` | Case C reads the DEFAULT consts, not the live properties; new `PinTheRailGuard`; time literals re-expressed off T3/T2; Case D pins the key identifiers + `SpecFor` |
| `api/_lib/tunables.js` | 3 allowlist entries |
| `api/_lib/tunable-manifest.js` | 3 owner-facing cards (area `misc`, plain English, safe ranges) |
| `api/_lib/tunable-manifest.generated.json` | regenerated - `knobs=49` |
| `docs/PROD022_TUNABLE_FLAGS.md` | rows 47 / 48 / 49 |

**Behaviour-neutral by construction:** no row, no network, no parse and no registry entry all resolve
to 90 / 150 / 50, which is exactly what the consts held.

## A2. The `SpecFor` guard is load-bearing, and here is why

`RemoteTunables.Int` answers **0** for an unregistered key - it has no spec and therefore no default
to fall back to, and it says so loudly. `RaidScoring` therefore asks `SpecFor(key)` first and answers
the shipping default when the answer is null, the same shape `RaidDeployController.StagingCeilingSeconds`
uses for `raid.stagingCeilingSeconds` (`RaidDeployController.cs:410-417`).

**Hand-evaluated RED-first (NOT RUN - this lane cannot fire Unity):**

```
Remove the guard, leave the key unregistered:
  HonorThirdStarSeconds -> RemoteTunables.Int("raid.honorThirdStarSeconds") -> 0
  ComputeHonorStars(engaged:true, elapsed:0.1f, destruction:0, heroDied:false)
      0.1 > 0  ->  stars = min(3, 2) = 2
  RaidWatchdogHonorRegression Honor("engage lights three", ..., want 3) -> got 2   RED
  And through Finalize's min(settle, honor): EVERY raid in the game caps at 2 stars,
  silently, with no error on screen.
With the guard:
  SpecFor -> null -> RaidHonorThirdStarSecondsDefault -> 90  ->  ComputeHonorStars -> 3   GREEN
```

`PinTheRailGuard` is the case that asserts it: with **no** loaded table and **no** `ff.tun.*` local
override, `RaidScoring`'s three properties must equal the three shipping-default consts. The skip
conditions are reported, never silent - a developer with a live override is correctly not at the
default, and an oracle that reds on the MACHINE rather than the code is worse than no oracle.

## A3. RED-first, EXECUTED (node half)

```
$ node tools/gen-tunable-manifest.mjs
TUNABLE_MANIFEST_GEN_OK knobs=49 -> api/_lib/tunable-manifest.generated.json (rewritten)

$ node --test test/tunables-manifest.test.js
... tests 23 | pass 23 | fail 0

--- MUTATION: RaidHonorThirdStarSecondsDefault 90 -> 91 in RemoteTunables.cs ONLY ---
$ node tools/gen-tunable-manifest.mjs --check
TUNABLE_MANIFEST_DRIFT api/_lib/tunable-manifest.generated.json does not match
    Assets/_Modules/Core/Ops/RemoteTunables.cs - run: node tools/gen-tunable-manifest.mjs
$ node --test test/tunables-manifest.test.js
x the checked-in spine is byte-identical to a fresh derivation from the build registry
... tests 23 | pass 22 | fail 1

--- RESTORED to 90 ---
$ node tools/gen-tunable-manifest.mjs --check
TUNABLE_MANIFEST_GEN_OK knobs=49 (checked, no drift)
$ node --test test/tunables-manifest.test.js
... tests 23 | pass 23 | fail 0
```

The oracle is falsifiable and it names the two sources that disagree.

## A4. Recorded, not hidden

- **`PresentationStars` is read every frame and each read now costs three `RemoteTunables.Int`
  calls** - and the cost is NOT the linear `SpecFor` scan, which is trivial. It is what `Int` does
  after it: `ReadLocalOverride` runs a `Guard.Try` around a `PlayerPrefs.GetInt`, and `FlowTrace.Once`
  builds the string `"resolve:" + key + "=" + value + "@" + provenance` on EVERY call, before the
  seen-check that discards it. `RaidScoring.Update` reads `PresentationStars` through
  `TraceHonorStarSnuff` and the HUD polls it too, so a live raid is roughly six PlayerPrefs lookups
  and six throwaway string allocations per frame that this build did not have.
  **NOT MEASURED, and not a blocker** - no capture exists and no frame-budget scope was added. The
  named follow-up, deliberately NOT done here because it changes the read pattern the ruling
  specified: cache the three values at `NotifyEngagement` and refresh them when
  `RemoteTunables.Generation` changes.
- **No compile, no Unity, no gate from this lane.** Everything C# here is source-level.
- **`RaidWatchdogHonorRegression`'s own result is unproven** - it needs the batchmode gate.

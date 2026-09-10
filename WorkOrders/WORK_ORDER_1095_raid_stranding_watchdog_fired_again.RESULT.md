# WO-1095 RESULT — the stranding watchdog now measures the raid clock's own interval

**Status:** IMPLEMENTED - awaiting gate (2026-09-09 lane RAID)
**Lane:** RAID (edit-only; no Unity run, no commit from this lane)
**Branch/HEAD at start:** `dev` @ `184c8ff06`

---

## 1. The cause, proven — not the one the WO body was choosing between

The WO offered two candidates ("the OnTimeExpired subscriber is missing" vs "RaidScoring never
installed") because that is what the watchdog's own failure text asserted. **Both are wrong**, and
the failure text was the thing asserting them without ever checking.

`docs/READY_RCA_2026-09-09.md` ("WO-1095") proved the real cause from capture:

> The failing run entered the raid at 17:15:42.851Z and fired 224.977 s later, exactly its 180+45 s
> bound, while the screenshot still showed 2:46 on the real clock (only 14 s engaged). This is a
> clock-domain mismatch, not merely an absent subscriber.

**The two domains, read at source this session:**

| | measured from | ticks on | file:line |
|---|---|---|---|
| the raid **clock** | first engagement (`_engaged`) | `Time.deltaTime` | `RaidScoring.cs` — the `if (!_engaged) { … return; }` gate immediately above `_elapsed += Time.deltaTime` |
| the **watchdog** (before this change) | raid **scene load** | `Time.unscaledDeltaTime` | `RaidDeployController.StrandingWatchdog`, `aliveFor` |

Different start, one bound (`clock + 45`). A long staging therefore exhausted the watchdog while the
raid clock had barely started.

### Acceptance item 1 — "the session log line naming *subscriber missing* vs *never installed*"

**There is no such line at HEAD, and that is itself the finding.** The controller never logged which
of the two it was; the failure text simply named both. What the captures DO prove, quoted:

- **Scorer was installed.** `logs/f8-inbox/capture-20260909-144859-seq4980.md`, Player.log tail,
  immediately after the watchdog Fail:
  `[Flow:Raid] stars settled: 0 (earned=0 heroDied=True cap=2) (cleared=False destruction=46 % elapsed=50s/180s underTime=True survival=100 % high=True @70 %).`
  `RaidScoring.Finalize` ran on a live scorer one line later.
- **HUD built.** No `deploy HUD failed to build - no tray and no Retreat button` line appears
  anywhere in that capture, and that line is unconditional on a build failure
  (`RaidDeployController.Start`).
- **Subscriber presence was never logged.** Hence the fix: `_clockSubscribed` + the new `STATE:`
  block, so the *next* capture answers this instead of the reader guessing.

### The `510` in seq 4980 — what it was

`break-log.jsonl`, same session, ordered:

```
2026-09-09T19:40:26.336Z  t=1036.537  scene_loaded  RaidBase_raider_camp_small
2026-09-09T19:41:21.503Z  t=1091.703  note          [HeroDeath] death freeze armed ... scene='RaidBase_raid…
2026-09-09T19:48:55.872Z  t=1546.019  error         RAID STRANDING WATCHDOG FIRED (last-resort arm) - 510s …
```

`1546.019 - 1036.537 = 509.482 s`. **The `510` is total wall-clock raid-scene age**, i.e. `aliveFor`
— exactly what the RCA says the watchdog measures. It is **not** 510 s of raid: the settle line in
the same log reads `elapsed=50s/180s`.

**Why it did not fire at its own 225 s bound — forced arithmetic, stated as such.** The arm is
evaluated every frame; `settled` was false throughout (`Finalize` ran *after* the Fail line, as a
consequence of `ForceExitHome`). So no frame observed `aliveFor` anywhere in `[225, 510)` — the
accumulator crossed the bound *and* 510 in a **single frame**. Since `aliveFor` (510) equals the full
scene age (509.5), no accumulated time was lost, so this was one enormous `Time.unscaledDeltaTime`,
not a stalled accumulator.

⛔ **What caused that frame gap is NOT proven and is not claimed.** The capture's Editor.log tail is
full of repeated `Lifecycle ERROR : Failed to setup LifecycleManagement and enter code reload scopes`
— consistent with a domain-reload attempt stalling frames mid-Play, but that is *consistent with*,
not *evidence of*. Likewise `elapsed=50s` across ~455 s of engaged wall time proves the **scaled**
clock did not advance; it does **not** prove `timeScale == 0` (`[HeroDeath] death freeze armed` is
the hero agent pin, not a world hold). Both are now **instrumented, not diagnosed** — the new failure
line prints `timeScale`, `clockElapsed`, `engagedFor` and `aliveFor` side by side, so the next capture
settles both in one read.

---

## 2. The fix — the measurement, never the bound

`Assets/_Modules/Village/Troops/RaidDeployController.cs`

1. **`engagedFor`** — a new unscaled accumulator that starts when `RaidScoring.Engaged` goes true.
   Same start as the raid clock, deliberately different tick (below).
2. **`ClassifyStranding(...)`** — the unsettled decision extracted as a **pure static**, so the
   whole WO-1095 behaviour is assertable with no scene and no play session:

   | state | interval bounded | why |
   |---|---|---|
   | scorer present + **engaged** | `engagedFor` vs `clock + 45` | the interval the clock itself measures |
   | **no scorer** | `aliveFor` vs `clock + 45` | no clock, no engagement gate to respect — scene age is the only honest measure. *This is the case the retired text named; it is kept, not removed.* |
   | staging, **HUD failed** | `aliveFor` vs `clock + 45` | no Retreat button, no clock: the raid's one genuinely exitless state, so today's tight number stays on today's case |
   | staging, HUD built | `aliveFor` vs staging ceiling | Retreat IS on screen; only a dead session should fire |

3. **The failure line STATES instead of ACCUSES.** The retired sentence ("This arm firing means the
   OnTimeExpired subscriber is missing or RaidScoring never installed") is gone. In its place: a
   per-arm diagnosis plus
   `STATE: arm=… aliveFor=… engagedFor=… clock=… grace=… stagingCeiling=… scoring=… engaged=… reason='…' clockElapsed=… subscriber=INSTALLED|MISSING hudBuilt=… timeScale=…`
4. **A hole found while in the seam, closed.** `BindScoringRoutine` gives up after 10 frames; the
   watchdog re-resolved `RaidScoring.Instance` every tick but never **subscribed** it. A
   late-installing scorer therefore left the raid's on-time exit unarmed for the whole session, with
   this net as the only way out. `SubscribeClock` is now the one idempotent owner of
   `OnTimeExpired`, called from both places, and the late resolve emits a `FlowTrace.Warn`.

### Why `engagedFor` is unscaled and not `RaidScoring.ElapsedSeconds` itself

Same start, different tick, on purpose. `_elapsed` advances on `Time.deltaTime`, so anything holding
`timeScale` at 0 freezes it — and a net that reads a frozen number **never fires**, converting today's
premature exit into a permanent strand. Seq 4980 is that exact case (`elapsed=50s` across ~455 s of
engaged wall time). This file's own doctrine already required it ("UNSCALED throughout: a hold left at
`timeScale=0` by any other system must never become a new way to strand the player"), and
`RaidTerminalStateRegression` Case C pins it.

### Nothing was loosened

- `UnsettledBackstopGraceSeconds` — **unchanged at 45 s**.
- `SettledExitGraceSeconds` and the whole settled arm — **untouched**.
- `FlowTrace.Fail` severity — **unchanged**.
- Every existing `FlowTrace` line kept; two added. Nothing stripped (CLAUDE.md §12).

### The new number is a TUNABLE

`raid.stagingCeilingSeconds`, default **900 s**, read through `RemoteTunables.SpecFor(...)` so the
call site answers the hardcoded default until the row is registered and goes live the moment it is —
it can never trip the "unregistered tunable key" trace. **Recorded honestly: 900 is a CHOSEN number,
not "today's value"** — nothing today bounds staging at all except the 225 s scene-age bound that this
ticket proved to be the false positive. Registering it needs `RemoteTunables.cs` and
`docs/PROD022_TUNABLE_FLAGS.md`, both outside this lane's file ownership; the exact lines are in the
hand-back.

---

## 3. RED-first proof

New suite: `Assets/Editor/Regression/RaidWatchdogHonorRegression.cs`, Case A1 — the F8 seq 4967 input
reproduced exactly:

```
ClassifyStranding(scoringPresent:true, engaged:true, aliveFor:225, engagedFor:14,
                  clock:180, grace:45, stagingCeiling:900, hudBuilt:true)   ->  StrandingArm.None
```

- **Against the OLD logic** (`if (aliveFor < clock + UnsettledBackstopGraceSeconds) continue;`):
  `225 >= 225` → the arm **FIRES**. Asserting `None` therefore **FAILS RED** on the pre-fix tree, with
  the message
  `[WO-1095] ClassifyStranding [seq 4967: 225s staged, 14s engaged -> the raid is LIVE, do not fire] expected None, got EngagedOverrun`
  *(pre-fix there is no `ClassifyStranding` at all, so on the literal pre-fix tree the suite fails to
  compile / Case B fails first — A1 is the assertion that discriminates the two **behaviours**, which
  is what the RED proof is about.)*
- **Against the NEW logic:** engaged, so the bound is applied to `engagedFor` = 14 s → `None`. **GREEN.**

A1 is the **only** case that separates the two implementations: every other input in Case A that fires
under the old rule still fires under the new one (A2, A3b, A4b, A5, A6c), which is the point — the net
was narrowed in *what it measures*, never in *whether it fires*.

Hand-evaluated table (all 10 verdict cases + the "never removable" sweep) reproduced in a scratch
simulation of both the old and new rule this session; every expectation in the suite matched.

---

## 4. Brace / NUL gate (CLAUDE.md §1)

| file | NUL | `{` | `}` |
|---|---|---|---|
| `Assets/_Modules/Village/Troops/RaidDeployController.cs` | none | 185 | 185 |
| `Assets/Editor/Regression/RaidWatchdogHonorRegression.cs` | none | 35 | 35 |

Existing oracles re-checked against the edited body (they lint `StrandingWatchdog`'s body directly):
`Time.unscaledDeltaTime` ✓, `Finalized` ✓, `HubScenes.IsRaid` ✓, `FlowTrace.Fail` ✓,
`BindScoringRoutine` still arms `StrandingWatchdog` ✓ — `RaidTerminalStateRegression` Case C.

---

## 5. Acceptance, line by line

- [x] The session line naming *subscriber missing* vs *never installed* — **there is none at HEAD**;
      what the captures prove instead is quoted in §1, and the line now exists going forward.
- [ ] All three exits proven once each with the arm never firing — **needs a play session. NOT DONE.**
- [x] Attribution stated plainly — cross-change invariant break (WO-1520 clock vs `5bc5025f5b`
      watchdog), **not** a regression of `5bc5025f5` and **not** one of the six dirty files.
- [x] A behavioural regression case so a third occurrence is caught by CI — Case A1.
- [ ] Owner felt-verifies a raid on device and closes. **PO item.**

## 6. Unproven, recorded honestly

- **Why the frame gap in seq 4980 happened.** Instrumented, not diagnosed (§1).
- **Whether `timeScale` was 0 during that raid.** The frozen scaled clock is measured; the cause is
  not. Same instrumentation answers it.
- **That the frozen scaled clock is fixed.** It is **not** — this ticket makes the net immune to it.
  The freeze itself is a separate, currently un-ticketed finding and should be minted.
- **No gate was run from this lane** (edit-only, no Unity). `COMPILE_GATE_OK` / `REGRESSION_OK` are
  the orchestrator's.

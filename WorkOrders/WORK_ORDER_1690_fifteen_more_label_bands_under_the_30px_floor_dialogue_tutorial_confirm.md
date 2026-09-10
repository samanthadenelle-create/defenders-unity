# WORK ORDER 1690 — More sub-floor label bands, found by the WO-1652 leash on the FTUE/dialogue path

**Status:** READY TO IMPLEMENT
**Minted:** 2026-09-10 (lane FIT-GUARD; number **PRE-ASSIGNED by the lead** — this lane did NOT touch `CLI_LANES_WO_NUMBERS.md`)
**Silo / Lane:** UI authoring — band heights only. Same shape as WO-1658; no kit mechanism changes.
**Type:** EXISTING.
**Severity:** P2 — **but only THREE of the five actually shrink text, and only ONE is severe.** See §2; the headline number is smaller than it first looked and the ticket says so rather than inheriting it.
**Raised by:** `FitGuardRelaxAllowlistRegression` CASE D on `Builds/wave8-reg2`. **The leash worked exactly as designed** — WO-1658 emptied the allowlist, a new session walked a path nobody had measured, and the suite went red on its first sight of it.
**Blocks nothing. Blocked by nothing.** Each entry can land on its own.

---

## 1. ⚠ FIRST, A CORRECTION TO THE HEADLINE — 15 LINES, **5 DISTINCT LABELS**

The gate reported **15 NEW relaxations**. That is **15 relaxation LINES**; parsed and grouped by
`relaxKey` they are **FIVE distinct labels**, re-armed as panels reopen across the session (the guard
re-arms on every re-fit, so a modal opened six times logs six lines).

Measured by this lane at source from
`Builds/device-frames/2026-09-10_1235_363866_raid_logcat.txt` (**29,206,423 bytes**, APK
2026.09.10.363866; **24,403 `[Flow:` lines**, so the channel was live). Do not carry "fifteen bands"
into the fix: **there are five, and two of them may need no edit at all.**

⚠ The log's LAST census reads `armCalls=162 armed=162 evaluated=45 relaxed=5` — a smaller count than
15 because **this logcat spans more than one process**; the counters are per-session statics. Read the
relaxation lines, not that census, when counting this ticket's scope.

## 2. THE FIVE — verbatim from the log, with the honest severity

| # | lines | floorFrom | floorTo | **renders** | key / text |
|---|---|---|---|---|---|
| **1** | x6 | 30 | **21** | **23 px** | `SkipTutorialConfirm/ObsidianPanel/PanelFill/Label` — `'*  Skip Tutorial'` / `'*  SKIP TUTORIAL'`, rects `826x26` and `826x28` |
| **2** | x1 | 30 | 25 | **27 px** | `Widget_targetFrame/TargetNameplate/StatBars/HealthBackground/Label` — `'53/53'`, rect `704x30` |
| **3** | x4 | **26** | 25 | **26 px** | `DialogueViewUI/ObsidianPanel/PanelContent/Zone_Header/Affiliation` — `'Essence of a fallen keeper'`, rects `1264x30` / `829x30` |
| 4 | x3 | 30 | 28 | **30 px** | `BodyWell/ScrollZone/Viewport/Content/Body` — the castle intro line, rect `1220x33` |
| 5 | x1 | 30 | 29 | **31 px** | `EndState/ObsidianPanel/PanelContent/Zone_Header/Label` — `'WAVE 1 CLEARED'`, rect `1250x38` |

**Read the `renders` column, not the `floorTo` column.** `floorTo` is where the *minimum* was moved;
`renders` is the size the player actually sees.

- **#1 is the real defect and it is a REPEAT OFFENDER.** 23 px is 7 px under the owner's floor and
  3 px off `FontHardFloor` — the identical figure as WO-1658's gold chip. And the Skip-Tutorial
  confirm is **named by the fit guard's own source comment** as the F8 2026-07-08 case that motivated
  the `minBand` floor (`ElarionUiKitObsidian.cs`, the `KIT-LEVEL MIN READABLE BAND FLOOR` block). It
  is on the **FTUE path**, i.e. the first modal a new player ever sees.
- **#2** at 27 px is a genuine, mild shrink on the combat target nameplate.
- **#3 is a DIFFERENT CLASS and must not be "fixed" without a ruling** — see §4.
- **#4 and #5 shrank NOTHING** (they render at 30 and 31). Only the floor moved, exactly like
  WO-1658's `'250 Crystals'`. Fixing them is cheap; leaving them is defensible. **Say which, with the
  measurement — do not silently skip.**

## 3. PRODUCERS — located at source 2026-09-10

⚠ **LOCATED, not proven.** Which rect resolves to the measured height is the implementing lane's
FIRST measurement (§12). Do not edit a number before a probe says that rect is the one.

- **#1 Skip-Tutorial confirm title** — the band is in the KIT, not the caller. `ElarionUiKit.BuildConfirmModal`
  (`Assets/_Modules/Core/UI/ElarionUiKit.cs:1495`) builds a shadow+title pair at `:1549` / `:1555`
  over the same `y0..y1` band and fits both at `:1568-1569`. **Both labels are named `Label`, so they
  share one `relaxKey`** — that is why one modal produced two rects (`826x26` and `826x28`).
  Callers: `TutorialSkipUi.cs:257` and `ObjectiveBannerUi.cs:413`. ⛔ **Fix the kit's title band, not
  a caller** — two callers means a caller-side fix leaves the other broken.
- **#2 nameplate health value** — `Assets/_Modules/Core/UI/ElarionUiKitNameplate.cs` (the bar's value
  label inside `HealthBackground`; the ASCII tree at `:14` names the hierarchy). The band is the bar's
  own height.
- **#3 dialogue affiliation** — `Assets/_Modules/HUD/DialogueView.cs:355` (`MakeLabel(..., 26, ...)`),
  fitted at `:357`.
- **#4 dialogue body** — `DialogueView.cs:371`, fitted at `:384` with `FitBlock(minSize: 30f, maxSize: 30f)`.
- **#5 end-state header** — the `EndState` panel's `Zone_Header` label.

## 4. ⚠ #3 IS NOT THE SAME BUG, AND THE DIFFERENCE IS THE WHOLE POINT

Every other entry here and in WO-1658 has `floorFrom=30` — authored at the floor, shrunk by the guard.
**#3 has `floorFrom=26`**, because `DialogueView.cs:355` authors the affiliation label at fontSize
**26** and `FitSingleLine` takes `maxSize = the label's current fontSize`, clamping its 30 px minSize
down to 26. So the guard did not drag this label under the floor — **the author placed it there**, and
the comment block immediately above (`DialogueView.cs:345-352`) shows that was a considered choice
about a thin `FrameCore` header band.

**Do not silently raise it to 30.** Either it is a deliberate role size (in which case the ruling
should be recorded and the entry stays leashed as accepted), or the ladder is wrong for this label.
**That is an owner/PO call, not an implementation detail.** Bring it as a question.

## 5. ACCEPTANCE

1. **Each fixed entry drops off the allowlist** in `Assets/Editor/Regression/FitGuardRelaxAllowlistRegression.cs`
   — and **only** against a fresh device logcat that no longer carries its `relaxKey` (the WO-1658
   §5.1 rule; that is the single warrant).
   ```
   grep 'TextFitGuard CENSUS' <fresh logcat> | tail -1     # relaxed=N must fall
   grep 'relaxKey=' <fresh logcat>                          # the fixed key must be absent
   ```
   ⛔ **Never delete an entry to make the suite green.** The suite reds on a NEW relaxation; that
   behaviour must survive this ticket intact.
2. **Quote the before/after** per entry with the log path and byte size.
3. **`stillBlank` stays 0.** Growing a band must never push a neighbour into a cull.
4. **A ruling is recorded for #3** before it is touched (§4), and for #4/#5 a one-line
   measured justification for fixing or leaving them.
5. **The session must walk the same path** — title → new game → skip-tutorial confirm → dialogue →
   a wave end-state — or the absence of a `relaxKey` proves only that the screen was not visited.
   ⚠ This is the trap WO-1658 half-fell into: two of its eight screens left no log token at all and
   only a PNG proved the visit.
6. `COMPILE_GATE_OK` + `REGRESSION_OK <n>/<n> suites` on **fresh** logs, judged by the marker.

## 6. ⛔ WHAT NOT TO TOUCH

- ⛔ **`ElarionUiKit.FontFloor` (30) and `FontHardFloor` (20).** Read, never written. Lowering a floor
  to make a band legal inverts the ticket; `FontHardFloor` carries the owner's F8 2026-07-08
  *"text will never be able to be seen on mobile at this size"* ruling.
- ⛔ **The fit guard** — `UiKitTextFitGuard`, `ArmFitGuard`, the relax/rescue mechanism, the census,
  the stand-down branch. This ticket exists *because* the guard reported correctly.
- ⛔ **The allowlist mechanism or the WO-1495 annotation rule.** Entries are added when ticketed and
  removed when a device log proves the fix. Never weaken the rule to quiet the suite.
- ⛔ **`UICaptureLaunch.cs`** — captures stay un-guarded (WO-1652 remedy B ruling).
- Do not fix #1 at `TutorialSkipUi` / `ObjectiveBannerUi`; fix the kit (§3).
- Do not hand-edit `.unity` scenes.

## 7. WHAT IS UNPROVEN

- **Whether any of the five ever CULLS for a player.** `stillBlank=0` on this log — they render below
  the floor, which is the ruling they break; a cull is a worse, separate failure not observed here.
- **Whether five is the whole list for this path.** It is five on ONE session (title → new game →
  skip-tutorial confirm → dialogue → wave end-state). Screens not visited cannot report. Expect the
  allowlist to grow again as new paths are walked — **that is the leash working, not a regression.**
- **Which rect owns each band** (§3 locates producers by name; none of the five heights was probed).

## OWNER RULING (2026-09-10 13:02)

The dialogue Affiliation tag authored at 26 (DialogueView.cs:355) goes UP to the 30 px floor - in scope for this ticket, not an exception.

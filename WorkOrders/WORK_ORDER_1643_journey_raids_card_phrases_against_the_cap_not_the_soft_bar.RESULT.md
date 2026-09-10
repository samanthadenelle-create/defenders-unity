# WO-1643 RESULT - the RAIDS card's second clause now states the camp fact and only the camp fact

**Status:** IMPLEMENTED - awaiting gate. **Not committed, not pushed** (WO sec.7).
**Lane:** JOURNEY-CARD. **Worktree:** `agent-ac6db22cce5d58766`, fast-forwarded to
`dev` = `0a7edc6b1a967169f7a47023bab43b0e68145c05` ("docs(board): mint WO-1645 ...").
**Date:** 2026-09-10.

> ⚠ **TWO THINGS THE LEAD MUST READ BEFORE GATING.**
> 1. **The copy change is PROVISIONAL.** WO sec.3 reserves the wording for the owner and this lane
>    has no `AskUserQuestion`. It was implemented on the lead's explicit brief; the ruling is still
>    owed. Section 5 carries the question verbatim.
> 2. **WO sec.4 Step 1's premise is false at HEAD.** It says `SetRaidOpenCampCount` "traces nothing".
>    It has traced since WO-1404. Section 1 has the correction and what the real gap turned out to
>    be - which is a strictly more interesting finding than the one the ticket predicted.

---

## 1. INSTRUMENT FIRST - what was measured, and the premise that did not survive it

### 1a. The ticket's stated instrumentation gap does not exist

WO sec.2 and sec.4 both assert that `SetRaidOpenCampCount` "publishes change-only and emits no trace
line of its own". **Read at source, that is wrong:**

- `Assets/_Modules/Core/HudModel/PostureSignals.cs:423` -
  `FlowTrace.Step("HudKit", "raid camps open -> " + count);`
- `git log -S'raid camps open ->'` returns exactly one commit, `5661d71e4`
  (*"feat(hud): WO-1404 - the Journey deck's Quests and Raids cards say what is waiting..."*), i.e.
  the trace has been there since the feature landed.

The WO's own instruction was **"one line, not both"**. Adding a second trace to that method would
therefore have been pure noise. It was not added.

### 1b. The REAL gap, and it is the more valuable finding

The count was unprovable for a different reason, and this one is measurable.

`Builds/device-frames/2026-09-10_raid_logcat.txt` (11,409,609 bytes, the exact file the ticket cites),
grepped in full:

| Grep | Hits |
|---|---|
| `raid camps open ->` | **0, across the entire 11.4 MB session** |
| `army fill ->` | 1 - `:2892`, `05:57:08.957`, `[Flow:HudKit] army fill -> 8 / 10` |
| `deck card=Raids` | 1 - `:14265`, `05:59:28.443`, `[Flow:Journey] deck card=Raids subtitle='Army 8 / 10 . train to open a camp'` |

**Both producers are change-only, and `RaidOpenCampCount`'s default is 0.** So a session in which the
count is *genuinely* zero publishes nothing and traces nothing - which is byte-identical to a session
in which the producer never ran at all. *"No camp is in reach"* and *"the camp producer is dead"* are
**the same silence on the wire.** That is the WO-1641-family duplicated-silence trap, and it is why
sec.2 had to record the 0603 value as unproven.

### 1c. The 0603 camp count is now PROVEN - and it needed no new code to prove it

`JourneyDeckSubtitleVM`'s branch is `openCamps > 0`, pure and deterministic. The painted sentence at
`:14265` took the **else** branch. Therefore `openCamps == 0` at that paint.

**Combined with the gate state the ticket already established** - `:28080`, `06:02:18.183`,
`raidCapable=True, deployable=8, queued=0, required=3, ready=True, lock=None` - the contradiction is
proven end to end from captured data: **the door was open, no camp was in reach, and the card told
the player to train.**

⚠ **What is NOT proven and must not be asserted:** the paint line is `05:59:28.443` and the closest
objective paint is `06:02:18.183` - **~3 minutes apart, not simultaneous.** They are two reads of a
slowly-changing state, not one atomic sample. The conclusion holds because `deployable=8` and
`army fill -> 8 / 10` agree and no intervening army-fill or camp-count change was published in
between (both are change-only, and neither re-emitted) - but "at the same instant" is an
overstatement and this lane does not make it.

### 1d. The one instrumentation edit, and why it is on the READ side

`Assets/_Modules/Core/HudModel/JourneyDeckSubtitleVM.cs`, in `FromCurrentState`, after the `Guard.Try`:

    string inputs = "journey subtitle inputs -> armyUsed=" + used + " armyCap=" + cap +
                    " openCamps=" + camps + " activeQuests=" + active +
                    " readyToClaim=" + ready;
    FlowTrace.Step("Journey", inputs);

Rationale, all of it recorded in-code at the site (CLAUDE.md sec.12 - it stays in the code, forever):

- **Read side, not producer side.** Acceptance 1 asks for the count *"at the instant the card paints"*.
  Only the read side can answer that; a change-only producer is silent in exactly the state the ticket
  cares about (1b).
- **Unconditional.** No change-detect. That is the whole point.
- **After the `Guard`**, so a guard failure still logs the 0-sentinel inputs the composer fell back to.
- **Cadence is safe.** One call site - `Assets/_Modules/HUD/PlayerDeckWorkspace.cs:800`, Journey deck
  build - the same cadence as the `TraceJourneySubtitle` `Step` at `:877` right beside it. Confirmed
  empirically: `deck card=Raids` appears **once** in 11.4 MB. Not a frame path, so a plain `Step` is
  correct and the 4-arg `FlowTrace.Measure` form (CLAUDE.md sec.12) does not apply.
- **Parts computed into a local first**, per CLAUDE.md sec.1 - the gate's brace scanner has no
  interpolated-string model. `+` concatenation only, matching the surrounding file.

---

## 2. The fix

### 2a. The clause (`JourneyDeckSubtitleVM.cs`, the composer)

    -                    : "train to open a camp");
    +                    : "no camp in reach");

**Why this shape and not the other two candidates in WO sec.3** (this lane picked once and did not
oscillate):

- It changes **only the ambiguous clause**. The fraction is explicitly a *second* ruling (sec.3, last
  paragraph), so it was left alone - which is also what keeps this diff off `BuildTimerService.cs`.
- It needs **no readiness input**. Candidate 2 ("lead with the door") would add a constructor
  parameter and a door-adjacent read into a composer WO sec.6 wants kept pure. Candidate 3 (drop the
  fraction) deletes a fact the panel header promises.
- It satisfies acceptance 3 for **every** readiness state, not just the captured one: the sentence no
  longer asserts anything at all about the army, so it cannot contradict a gate it never reads.
- **Bonus, measured:** the string is **shorter** - `Army 0 / 10 . train to open a camp` is 34 chars,
  `Army 0 / 10 . no camp in reach` is 30. WO-1636 lists this exact label as truncating at
  `18-20 of 25` visible chars. This does not fix that ticket, but it moves it 4 characters in the
  right direction and does not worsen it.

A block comment at the site records the contradiction, the retired phrase, and an explicit
**"do not restore a training verb here"** - because the remedy for *no camp in reach* may be training
**or** unlocking the next camp, and this composer cannot tell which.

### 2b. Localization - the shape was HELD, not widened (WO sec.4 asks which was chosen)

**Chosen: keep the existing hardcoded-English shape**, per the lead's brief.

`JourneyDeckSubtitleVM.cs:21-25` was **already** hardcoded English, not `LocalText` keys. Swapping one
hardcoded literal for another holds the localization gap exactly constant - it does not widen it and
it does not create a new one. Minting keys would mean **20 JSON files** across 10 policy locales in
both canonical copies (`LocaleParityRegression` demands exact parity) plus a
`Defenders > Week 1 > Build Localization` regeneration - genuinely a separate ticket, which WO sec.4
itself names as a legitimate answer.

⛔ **This lane did NOT mint a WO number for it.** No number block was pre-assigned in the brief, and
memory `parallel-worktree-lanes-collide-on-wo-numbers` records two isolated lanes both minting 1631
on 2026-09-10. **The lead mints from the `CLI_LANES_WO_NUMBERS.md` banner.** The follow-up scope is:
*"the two Journey deck subtitles are hardcoded English; mint keys for both composer branches across
all 10 locales in both canonical copies."*

### 2c. `BuildTimerService.cs` and `ArmyReadiness.cs` - a DELIBERATE ZERO

The brief asked for the `BuildTimerService` edit to be minimal and localized to the `SetArmyFill`
seed at `~:2237`, so the lead's uncommitted WO-1641 3-way merge would land. **The edit turned out to
be unnecessary entirely** and both files are untouched:

- The fraction did not change, so the seed did not need to change (WO sec.4: *"If the ruling touches
  the FRACTION, change it at the seed"* - it does not).
- The instrumentation belongs on the read side (1d), which is in `Core/HudModel`, a different
  assembly from the seed.

`git status --short` in the worktree, verbatim:

    M Assets/Editor/Regression/JourneyDeckSubtitleRegression.cs
    M Assets/_Modules/Core/HudModel/JourneyDeckSubtitleVM.cs

Nothing else. **Zero merge surface against WO-1641.**

---

## 3. Pins - what moved, what was preserved (WO sec.6)

The brief said *"re-point the four assertions"*. Under this candidate **only one of the four actually
changes**, because the fraction was not touched. Nothing was deleted.

| Line | Assertion | Disposition |
|---|---|---|
| `:39` | `RequireContains(fixture.RaidsSubtitle, "3 / 10", ...)` | **PRESERVED verbatim** - fraction untouched |
| `:41` | `RequireContains(fixture.RaidsSubtitle, "1 camp", ...)` | **PRESERVED verbatim** - the open-camp branch is unchanged |
| `:53` | `RequireContains(zero.RaidsSubtitle, "Army 0 / 10", ...)` | **PRESERVED verbatim** |
| `:54` | `RequireContains(zero.RaidsSubtitle, "train to open a camp", ...)` | **RE-POINTED** to `"no camp in reach"`, label `zero-camp state`. Not deleted. |

**Two pins ADDED**, so the retired shape cannot come back silently and acceptance 3 has an oracle:

- `ForbidBoth(zero, "train")` - RED recipe: restore the training verb in the no-camp branch.
- **The contradiction fixture** - `new JourneyDeckSubtitleVM(0, 0, 8, 10, 0)`, i.e. the device state
  (`deployable=8, cap=10, no camp in reach`) at a moment the gate had published `ready=True`:
  `RequireContains(..., "Army 8 / 10 . no camp in reach")` plus `ForbidBoth(..., "train")`.
  The pin is deliberately *"no no-camp sentence may carry army advice at all"* rather than
  *"not when ready"* - the composer takes no readiness input by design, so the stronger pin is also
  the honest one.

**Everything else in `sec.6` is untouched and was verified unmodified in the diff:** `:20-22` deck
bindings and the forbidden legacy literals; `:26,29,32` the open-camp predicate, escalation lock and
projection cache (read-only in this lane); `:45-49` the `"..."` and legacy-verb forbids;
`BuildTimerService.cs:2306-2314` (file not opened for edit at all);
`RaidEntryGate.cs:46-53`. **`RaidsDiscoverabilityRegression` D5** - visibility is decided by
`Available = () => PostureSignals.RaidCapable` at `PlayerDeckWorkspace.cs:839`, which this change does
not go near; the card cannot start hiding itself.

**Movement toward the `RaidEntryGate.cs:46-53` contract** (*"a fix must move TOWARD it, never further
away"*): the offending sentence no longer implies a bar at all, so it can no longer disagree with the
gate. It does not yet *satisfy* the contract - the fraction still names `CapSlots` - and that is
precisely the second ruling in sec.5.

**`ForbidBoth` case-sensitivity check** (`StringComparison.Ordinal`, `:75-76`): the new literal is
`"no camp in reach"`. It does not contain `"Choose"`, `"Read"` (capital R - `reach` is lowercase and a
different word) or `"..."`. The pre-existing forbids at `:46-49` are unaffected.

**Composer outputs, derived from the pure composer:**

| Fixture | `RaidsSubtitle` |
|---|---|
| `(2, 1, 3, 10, 1)` | `Army 3 / 10 . 1 camp open` |
| `(0, 0, 0, 10, 0)` | `Army 0 / 10 . no camp in reach` |
| `(0, 0, 8, 10, 0)` | `Army 8 / 10 . no camp in reach` |

⚠ These are **derived by reading the composer**, not observed from a suite run - this lane cannot run
Unity. See sec.4.

**Sweep for other consumers of the retired phrase** (`grep -rn` over `Assets/`, `WorkOrders/`,
`docs/`): **no live code and no other regression references it.** The only `Assets/` hit is the new
explanatory comment. The remaining hits are WO-1421, WO-1636, WO-1641, WO-1642, this WO, and
`docs/HANDOVER_2026-09-05_evening.md` - all dated point-in-time records, frozen by CLAUDE.md sec.15
and correctly left alone.

---

## 4. Acceptance - honestly scored

| # | Criterion | Verdict |
|---|---|---|
| 1 | Trace names the camp count at paint | **PARTIAL - code landed, fresh run NOT PROVEN.** The `Step` is in at `FromCurrentState`. This lane cannot run Unity or a device, so no fresh log line can be pasted. The *historical* value is proven instead, by the else-branch at `:14265` (sec.1c). |
| 2 | A Journey-deck frame at the same aspect | **NOT PROVEN by this lane** - cannot run `UI_CAPTURE`. **Expected** value, stated as a prediction: the Journey capture fixture publishes `PostureSignals.SetArmyFill(0, 10)` (`Assets/Editor/UICaptureLaunch.cs`, pinned at `JourneyDeckSubtitleRegression.cs:36`), so the headless frame should read **`Army 0 / 10 . no camp in reach`**. Lead: open the PNG and confirm. |
| 3 | A `ready=True` fixture must not read as "train more troops" | **MET, and pinned** - `(0, 0, 8, 10, 0)` -> `Army 8 / 10 . no camp in reach`, with `ForbidBoth(readyNoCamp, "train")` beside it. |
| 4 | `JourneyDeckSubtitleRegression` passes, assertions UPDATED not deleted | **Assertions updated (sec.3); PASS NOT PROVEN** - requires the gate. |
| 5 | Brace + NUL on every `.cs` | **MET** - sec.6. |

## 5. THE OWNER RULINGS STILL OWED - route these, they are not this lane's to answer

1. **The wording (WO sec.3).** Verbatim, for the lead to route:
   *"What should the RAIDS card say when the army is READY but no camp is open?"* Shipped
   provisionally as candidate 1, **`Army 8 / 10 . no camp in reach`**. The alternatives she may
   prefer: lead with the door (`Ready to raid . no camp in reach`), or drop the fraction from this
   card and let the Heart plate own army state. Reverting to either is a one-line change to the same
   composer plus the same regression block.
2. **The fraction (WO sec.3, the SECOND ruling).** The card still says `RosterSlots / CapSlots` while
   the gate judges `(Deployable + Queued) >= RequiredSlots` - **all three terms differ** (WO sec.1c),
   so wounded troops inflate the numerator and a training queue deflates it, independently of the
   denominator. Deliberately untouched here. If she rules on it, the edit is at the **seed**,
   `BuildTimerService.cs:2237`, one producer - and it will collide with WO-1641's uncommitted change
   in that same method, so it wants sequencing after that lands.
3. **The localization follow-up** (sec.2b) - needs a WO number from the banner. The lead mints.

## 6. Gate evidence produced by this lane

    $ python tools/gate_brace.py Assets/_Modules/Core/HudModel/JourneyDeckSubtitleVM.cs \
                                Assets/Editor/Regression/JourneyDeckSubtitleRegression.cs
    GATE_BRACE_SUMMARY bad=0 of 2
    gate_brace exit=0

    raw brace counts
      Assets/_Modules/Core/HudModel/JourneyDeckSubtitleVM.cs           open=7 close=7
      Assets/Editor/Regression/JourneyDeckSubtitleRegression.cs        open=8 close=8

    NUL scan (grep -c -P '\x00')
      Assets/_Modules/Core/HudModel/JourneyDeckSubtitleVM.cs           nul_lines=0
      Assets/Editor/Regression/JourneyDeckSubtitleRegression.cs        nul_lines=0

Both the gate-rule port and the raw one-liner agree, so CLAUDE.md sec.1's
interpolated-string divergence does not bite here (no `$"..."` was introduced).

**Diff size - MEASURED, so "small merge surface" is a fact and not an inference.**
`git status --short` only proves *which* files changed; a CRLF/LF normalization would show the same
two `M` rows while rewriting every line, which is exactly the shape that makes a 3-way apply drop
hunks silently (memory `git-apply-3way-drops-hunks-silently`). `git diff --numstat`:

    22   1   Assets/Editor/Regression/JourneyDeckSubtitleRegression.cs
    44   1   Assets/_Modules/Core/HudModel/JourneyDeckSubtitleVM.cs
    11   1   WorkOrders/WORK_ORDER_1643_...soft_bar.md

**Exactly ONE deleted line per `.cs` file** - the two clause swaps, nothing else. Line endings are
intact and the additions are comment blocks plus the new pins.

⛔ **`COMPILE_GATE_OK`, `REGRESSION_OK` and `UI_CAPTURE_OK` are NOT claimed.** This lane cannot run
Unity, gates, or commits, and did none of them. Not committed, not pushed (WO sec.7).

## 7. Files

- `Assets/_Modules/Core/HudModel/JourneyDeckSubtitleVM.cs` - paint-time input trace; the no-camp clause
- `Assets/Editor/Regression/JourneyDeckSubtitleRegression.cs` - 1 assertion re-pointed, 3 preserved, 2 pins added
- `WorkOrders/WORK_ORDER_1643_journey_raids_card_phrases_against_the_cap_not_the_soft_bar.md` - Status flipped
- `WorkOrders/WORK_ORDER_1643_journey_raids_card_phrases_against_the_cap_not_the_soft_bar.RESULT.md` - this file

# WO-1652 - `UiKitTextFitGuard` NEVER fires in a headless capture: every PNG we gate on measures the UN-GUARDED layout

**Status:** INSTRUMENTED - awaiting a capture log + a play-mode log

> **2026-09-10, lane FIT-GUARD (worktree `agent-a9e001ddb25631dda`, branched from dev `736b6b4b9`).**
> §4 (instrument-first) is DONE and nothing else was touched. **No remedy was chosen — §6 options
> A/B/C remain TABLED pending the owner's ruling**, and the §7.1 RED-first fixture is deliberately
> remedy-NEUTRAL (it pins the trace contract, not whether the guard should run in captures).
>
> **Not run.** This lane holds no Unity. `gate_brace.py` + a NUL scan are clean on both `.cs` files;
> the compile gate, the regression run, the capture and the play-mode session are the lead's.
>
> **Landed:**
> - `Assets/_Modules/Core/UI/ElarionUiKitObsidian.cs` — ARM trace on all three branches
>   (`null-text` / `not-playing` / `armed`), each `FlowTrace.Once` + a 1 Hz `FlowTrace.Throttle`
>   census; EVALUATE trace after the `_frames` gate and again on completion, so "ran and was fine"
>   is now distinguishable from "never ran". Behaviour unchanged.
> - `Assets/Editor/Regression/TextFitGuardArmRegression.cs` (new, no other lane owns it) —
>   RED-first: on the pre-instrumentation tree Cases B and C fail for want of any guard line.
>   **Registration in `DataRegression.cs` is the lead's line** (given in the hand-back), not edited here.
>
> **Measured this session at HEAD 736b6b4b9** (`Builds/wave5-manageflow1`): `TextFitGuard` = **0**
> lines, `[Flow:UI]` = **64** lines. That pair is the proof the guard's silence is the guard's own
> and not the trace channel's — the `"UI"` system prints fine in a headless capture.
**Minted:** 2026-09-10 (lane UI-KIT-GUARD; number **PRE-ASSIGNED by the lead** - this lane did NOT touch `CLI_LANES_WO_NUMBERS.md`)
**Silo / Lane:** Core UI kit + capture harness. Diagnosis and instrumentation FIRST; the remedy needs a ruling (sec.6).
**Severity:** P2 **process**, not a screen. It does not break a pixel - it breaks what our screenshots MEAN, in both directions.
**Type:** EXISTING.
**Raised by:** WO-1651 finding 1. A label that drew **ZERO of 33 glyphs** produced **no guard line at all**, from a guard written to `FlowTrace.Fail` on exactly that.
**Files owned:** `Assets/_Modules/Core/UI/ElarionUiKitObsidian.cs` (`ArmFitGuard` + `UiKitTextFitGuard` only), `Assets/Editor/UICaptureLaunch.cs` (the harness's frame/lifecycle handling only).
**Do NOT touch:** ⛔ `ElarionUiKit.FontFloor` / `FontHardFloor` / any floor constant. ⛔ `LayoutOracle.cs` and Assert C (WO-1630 owns the detector). ⛔ the `GlyphBaseline` array (WO-1636 owns it; shrink-only). ⛔ any panel's authoring site - a guard defect is never fixed by re-authoring the screens it failed to police. ⛔ `FitSingleLine` / `FitBlock`'s public contract or their `minSize`/`maxSize` semantics.

---

## 1. The evidence

**Measured, `Builds/wave5-manageflow1` (2026-09-10, 1,307,056 bytes, UTF-8, opened at source):** the
Manage queue row's refund note drew **ZERO of 33 printable glyphs** on all three
`ManageFlow_{BUILD,ARMY,RESEARCH}_queue_2670x1200` frames - a whole-line cull (WO-1651).

**`grep TextFitGuard` over every `wave5-*` and `wave3-capture*` log returns `0` lines. Total.**

That guard is built to speak on precisely this event. `UiKitTextFitGuard.LateUpdate` ends with a render
assert (`ElarionUiKitObsidian.cs:3219-3225`):

```
if (Blank(_t))
    FlowTrace.Fail("UI", "TextFitGuard '...': STILL renders 0 visible glyphs (...) - dead-button law violated, needs a layout fix");
```

A whole-cull happened and that line never printed. **The net has never been in the water.**

## 2. Root cause - PROVEN at source, TWO independent locks, not a hypothesis

⚠ **This supersedes WO-1651's own sec.6, which recorded the cause as unproven and cited
`UICaptureLaunch.cs:535-536` as evidence that the capture "does enter play mode". That citation was
the WRONG ENTRY POINT and the claim is now disproven.** Recorded rather than quietly corrected, because
it is the same class of error this repo keeps paying for: a fact read off an adjacent line.

**Lock 1 - the guard is never even attached.** `ArmFitGuard` (`ElarionUiKitObsidian.cs:3094-3100`):

```
private static void ArmFitGuard(TMP_Text t)
{
    if (t == null || !Application.isPlaying) return;      // :3096  <-- returns here
    var g = t.GetComponent<UiKitTextFitGuard>();
    if (g == null) g = t.gameObject.AddComponent<UiKitTextFitGuard>();
    g.Arm();
}
```

The run that produced the finding was
`DeNelle.Editor.UICaptureLaunch.RunManageFlowMapCaptureHeadless` (`UICaptureLaunch.cs:8728`, named 98
times in that log). **`EnterPlaymode` appears EXACTLY ONCE in the whole of `UICaptureLaunch.cs`, at
`:536`** - inside the *interactive* `[UICap] capture requested -> entering Play mode` path, and **that
log line does not appear anywhere in the capture log** (0 matches). The headless entry's own docstring
(`:541-545`) says it plainly: *"Renders each supported code-built panel to a PNG under
`Builds/ui-capture/` **WITHOUT entering Play mode**."* Its teardown comment agrees - *"Edit-mode
teardown MUST be DestroyImmediate"* (`:1116`).

So in every headless capture `Application.isPlaying` is **false** and `ArmFitGuard` returns at `:3096`.

**Lock 2 - even if attached, it could never evaluate.** The guard is frame-driven and needs at least
**two** `LateUpdate` ticks before it does anything:

| where | what it does |
|---|---|
| `:3117` | `private sealed class UiKitTextFitGuard : MonoBehaviour` |
| `:3122` | `Awake()` caches the `TMP_Text` |
| `:3125` | `Arm()` = `_frames = 0; enabled = true;` - the ONLY thing `ArmFitGuard` calls |
| `:3127` | `LateUpdate()` - the entire body; there is no coroutine and no `Canvas.willRenderCanvases` hook |
| `:3130` | `if (_frames++ < 1) return;` - **tick 1 is spent letting layout size the rect; work starts on tick 2** |
| `:3133-3141` | the empty-text / zero-height stand-down, which needs **600** frames before it even warns |
| `:3225` | `enabled = false` - one-shot; it evaluates once and disarms |

`UICaptureLaunch.cs` contains **no `yield return null` and no `WaitForEndOfFrame`**, and tears every
panel down with `DestroyImmediate`. Nothing ticks between build and teardown. So the answer to *"does
the capture tear the canvas down before a frame ticks?"* is **yes - it never ticks one at all.**

## 3. Why this is worth a ticket, and it cuts BOTH ways

The shipped player build DOES run the guard (play mode, frames tick). The capture does NOT. **So the
PNGs we gate on and the game the owner plays are measuring two different layout systems**, and the gap
produces two opposite failures:

1. **FALSE ALARM.** A label the guard would have rescued in-game is reported by the oracle as a defect.
   A lane then spends a morning on a screen that was never broken for the player. ⚠ **WO-1651 may be an
   instance of this and it is NOT yet proven either way** - see the caveat added to that ticket.
2. **FALSE COMFORT, the more expensive one.** In the player build the guard silently *relaxes the floor*
   to make text fit - it will drop a label from 30 toward `FontHardFloor` (20) without anyone seeing it,
   because `FlowTrace.Warn` on a device goes into the logcat ring (memory
   `logcat-ring-buffer-destroys-evidence`). The capture, which is the one place a human LOOKS, never
   shows that relaxation. **Sub-legible-but-present text is exactly the defect class
   `FontHardFloor` was raised to 20 to end** (`:3035-3044`, the F8 2026-07-08 "text will never be able
   to be seen on mobile at this size" ruling).

## 4. ⛔ INSTRUMENT FIRST. Do not change behaviour in the first commit.

Per §12, the first change is a trace, not a fix. Two `FlowTrace.Step` calls, both cheap and permanent
(§12: instrumentation is never stripped):

1. **On ARM** - inside `ArmFitGuard`, and it must log **on the early-out too**, naming which branch was
   taken and the `Application.isPlaying` value. A guard that declines to arm must SAY it declined; a
   silent early-out is the whole bug.
2. **On EVALUATE** - at the top of the working half of `LateUpdate` (after the `_frames` gate), naming
   the label, its rect h/w, the resolved `factor`, and whether it relaxed. Today only the *relaxed* and
   *blank* paths speak, so "the guard ran and everything was fine" is indistinguishable from "the guard
   never ran".

**Then re-run one capture and one play-mode session and read the two logs side by side.** The arm-count
in each is the measurement this ticket exists to produce. No remedy is chosen before that read.

## 5. ⚠ What is still NOT proven, and must not be assumed

- **Whether any label currently ships relaxed below 30 in the player build.** Nobody has counted. The
  arm/evaluate trace above is what answers it; until then, "the guard is quietly saving us" and "the
  guard never matters in practice" are BOTH unproven.
- **Whether WO-1651's refund note actually culls for a player.** Its authoring fix is correct
  regardless - a label should fit its band without a rescue - but its *severity* depends on this.
- **Whether the other kit guards share the pattern.** `ArmFitGuard` is the one read here. Any other
  `Application.isPlaying`-gated or frame-driven kit safety net is in the same position by construction
  and should be swept in the same pass - but that sweep has not been done and is not claimed.

## 6. ⛔ THE REMEDY NEEDS A RULING - do not pick one in the RCA commit

Three shapes, and they are genuinely different products. Name the trade to the lead/owner; do not
choose silently (architecture law: *what is right, not what is easy*).

| option | what it buys | what it costs |
|---|---|---|
| **A. Run the guard in captures** (drive N frames, or a synchronous edit-mode `Evaluate()` the harness calls) | the PNG shows what the player sees | the oracle then measures the RESCUED layout, so a real authoring defect renders fine in the capture and the glyph oracle stops catching the class WO-1636 was built for |
| **B. Leave captures un-guarded, and make the guard LOUD in the player build** | the capture keeps measuring the authored truth (WO-1636 keeps working); relaxations become visible where they happen | needs a device/play-mode read path that survives the logcat ring, and does not help the headless gate |
| **C. Both, separated** - captures stay un-guarded (authored truth), and the guard's relaxations are asserted by a play-mode regression | catches both directions | most work; needs a play-mode harness that does not exist yet |

⚠ **A is the tempting one and it is the one that could silently retire the glyph oracle's teeth.** Say
that out loud when the ruling is asked for.

## 7. Acceptance

1. **RED FIRST.** Add a deliberately-culled fixture label to the capture harness's own fixtures - a
   band too short to seat its floor line, the WO-1651 shape - and prove the current tree reports the
   oracle's `TEXT CULLED WHOLE` and **no** `TextFitGuard` line. That asymmetry IS the bug, captured as
   a failing expectation before anything is changed.
2. **A guard line appears on a capture log for that fixture** once the chosen remedy lands: named
   label, rect, factor, and whether it relaxed.
3. **A count, from the trace, of how many labels arm and how many relax** - in a capture and in a
   play-mode session. Two numbers, on fresh logs, quoted with their log paths.
4. **No font floor moved** anywhere in this ticket. `FontFloor` (30) and `FontHardFloor` (20) are read,
   never written.
5. **The glyph oracle still catches WO-1636's class.** Whatever lands, re-run the full capture and show
   `UI_GLYPH_*` still fires on a genuinely mis-authored label - if remedy A makes the oracle go quiet,
   that is a REGRESSION of the detector, not a pass.
6. The `docs/INSTRUMENTATION_STANDARD.md` note for §1.4 is updated in the SAME commit if the guard's
   contract changes (§15).

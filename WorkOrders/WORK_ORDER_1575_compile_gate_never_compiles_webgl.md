# WO-1575: The compile gate never compiles WebGL, so WebGL-only code rots silently

**Status:** CLOSED 2026-09-08 - owner felt-test PASS (validated 2026-09-09T02:10:49, build 2026.09.08.361259). PRIOR STATUS: FIXED - implemented in the 2026-09-07 afternoon gate wave (COMPILE_GATE_OK Builds/cg-wave10h.log, REGRESSION_OK 454/454 Builds/reg-wave10d.log 13:05); reaches the Seeker with the next tester build; owner felt-test closes it. PRIOR STATUS: READY TO IMPLEMENT
**Minted:** 2026-09-07 (edit-only WebGL compile-fix lane; number taken from the
`CLI_LANES_WO_NUMBERS.md` main-line banner and bumped 1575 -> 1576 in the same edit)
**Silo:** Build / gates (tooling only - no gameplay, no scene, no content)
**Lane:** Editor tooling. File-disjoint from every gameplay lane.

---

## 1. The defect (proven, not inferred)

`DeNelle.Editor.CompileGate.Run` emits `COMPILE_GATE_OK` after Unity has compiled the
project **for the editor's currently ACTIVE build target only**. Any code sitting behind a
platform define that is not the active target is never handed to a compiler by the gate, so
it can carry a hard compile error indefinitely while every gate on this machine reads green.

**Measured this session.** `Builds/webgl-build.log` (Addressables content build for WebGL,
which runs a real WebGL player-script compile) failed with exactly one error:

```
Assets\_Modules\Core\Diagnostics\WebTrace.cs(325,35): error CS1501:
No overload for method 'Warn' takes 3 arguments
```

The offending call sat inside `#if UNITY_WEBGL && !UNITY_EDITOR` in
`Assets/_Modules/Core/Diagnostics/WebTrace.cs`. `FlowTrace` exposes exactly one `Warn`
overload - `Warn(string system, string message)`
(`Assets/_Modules/Core/Diagnostics/FlowTrace.cs:163`) - and the call passed
`(system, key, message)`, i.e. the `Throttle`/`Once` shape. The desktop compile gate was
green the entire time, because the active target was not WebGL.

That call has now been fixed (the key was folded into the message string, the trace kept).
**This ticket is about the missing gate, not the fixed line.**

### Why this class recurs

`FlowTrace` is a moving target and platform-guarded blocks do not move with it. It was
extended on 2026-09-06 (WO-1483) with a **4-arg** `Measure(system, what, warnAboveMs,
everySeconds)` overload alongside the existing 3-arg
`Measure(system, what, warnAboveMs = 0f)` - `FlowTrace.cs:257` and `:308`. Every future
overload change carries the same hazard: guarded code that no gate compiles.

This is the same duplicated/unobserved-state failure shape CLAUDE.md documents in
sections 2, 5, 8 and 16 - the copy nobody re-reads goes stale, and nothing catches it until
a downstream build does.

## 2. Proposal - the cheapest possible gate

Add a **player-script compile pass for WebGL** to `CompileGate`, using the API Unity already
ships for exactly this:

```csharp
UnityEditor.Build.Player.PlayerBuildInterface.CompilePlayerScripts(
    new UnityEditor.Build.Player.ScriptCompilationSettings
    {
        target = BuildTarget.WebGL,
        group  = BuildTargetGroup.WebGL,
        options = ScriptCompilationOptions.None,
    },
    <a scratch output folder>);
```

Key properties that make this the cheap option. **These are EXPECTED behaviours read from the
API's contract, NOT measured on this machine this session (CLAUDE.md section 11B) - acceptance
criteria 4 and 5 exist to prove them, and the ticket is invalid if criterion 4 fails:**

- It is expected **NOT to switch the active build target**, so it should not trigger a full
  asset reimport and should not disturb the Android/Windows ship chain. **AC 4 proves this.**
- It compiles the player assemblies **with the WebGL define set**, which is precisely the
  thing the current gate never does. **AC 5 proves this.**
- It returns a `ScriptCompilationResult`; combined with the editor's compiler-message
  callback it should yield a definite pass/fail, not a heuristic. **AC 5 proves this.**

### Acceptance criteria

1. `CompileGate.Run` performs a WebGL player-script compile in addition to its existing work.
2. On any WebGL-only compile error, the gate **withholds `COMPILE_GATE_OK`** and prints each
   error with `file(line,col): error CSxxxx` intact - the same shape the existing gate uses,
   so no log reader changes.
3. The gate emits a distinct, greppable marker for this stage so a reader can tell the WebGL
   pass ran at all (marker absence on a fresh log is a FAILURE, per CLAUDE.md section 16).
   Register the new marker in the mapping table at
   `Assets/Editor/Regression/DataRegression.cs:14-22`.
4. The active build target is **unchanged** before and after the gate - assert it, because
   memory `desktop-build-after-android-target` records what an unexpected target switch costs.
5. Prove it BOTH ways (memory `prove-the-success-path-not-just-the-refusal`):
   - **Failure path:** temporarily reintroduce a 3-arg `FlowTrace.Warn` inside a
     `#if UNITY_WEBGL` block, run the gate, capture the withheld marker + the CS1501 line.
   - **Success path:** revert it, run the gate, capture `COMPILE_GATE_OK` plus the new
     WebGL marker on a **fresh** log.
6. Record the added wall-clock cost of the WebGL pass in the RESULT. If it is large enough to
   hurt the per-commit cadence, propose (do not unilaterally adopt) an opt-out switch and put
   it to the owner.

### Scope guards - what NOT to touch

- Do **not** switch the active build target, and do not add a target switch as a fallback.
- Do **not** touch `Builds/`, `ServerData/`, `tools/r2-ship.ps1`, or any ship chain.
- Do **not** modify `FlowTrace`'s overload set to accommodate call sites; call sites are
  fixed to the overloads that exist.
- Do **not** strip or weaken any `FlowTrace` call to make a compile pass (CLAUDE.md section
  12 - instrumentation is permanent).

### Open question for the owner

Should the same pass cover **Android** as well? Android is the priority ship lane (memory
`apk-is-the-vision-pi-is-parked`), and a device build already exercises it nightly, so the
marginal value is lower than WebGL's - but the same silent-rot hole exists for
`#if UNITY_ANDROID` blocks whenever the editor sits on Windows. Not decided here.

## 3. Evidence trail for this session's sweep

Every `#if`-guarded region under `Assets/_Modules` was scanned for `FlowTrace` calls whose
argument count does not match a live overload. **108** files carry both an `#if` and a
`FlowTrace` call; **663** `FlowTrace` calls sit inside a platform-guarded region
(`UNITY_WEBGL` / `UNITY_ANDROID` / `UNITY_IOS` / `UNITY_STANDALONE` / `UNITY_EDITOR` and
friends). Arities observed: `Step`/`Warn`/`Fail` at 2 args, `Throttle` at 4. Every apparent
3-arg hit was a false positive of the scanner - `string.Join(",", ...)` inside an
interpolation hole - and each was opened at source and confirmed 2-arg.

`WebTrace.cs:325` was the only genuine mismatch in the tree.

---

*Provenance: minted by the edit-only WebGL compile-fix lane, 2026-09-07, from a real failure in
`Builds/webgl-build.log`. Number taken from the `CLI_LANES_WO_NUMBERS.md` main-line banner and
bumped in the same edit, per CLAUDE.md section 2.*

# WO-1746 RESULT — Breach stance + 10% reluctant wall damage

**Outcome: IMPLEMENTED, NOT YET GATED.** Edit-only lane: no Unity, no compile gate, no regression
run, no bake, no git operation was performed. Everything below is either a file that was written or
a check that was actually executed; nothing is claimed that was not.

## Files changed (5)

1. `Assets/_Modules/Village/Troops/RaidAssaultAi.cs` — named constant
   `ReluctantWallDamageMultiplier = 0.1f` + pure `WallDamageMultiplier(preferStructures,
   breachStance)`; `PreferUnit` / `PickBucket` gain a `breachStance` overload, old signatures kept as
   delegating overloads so every existing suite compiles and passes untouched.
2. `Assets/_Modules/Village/Troops/TroopBreachOrder.cs` — the stance made explicit:
   `StanceActive => _stanceArmed || HasOrder`, `SetStanceArmed(bool)` (traced). Public `Clear()`
   disarms; private `DropInternal` (wall death) deliberately does not — that path **is** the auto-chain.
3. `Assets/_Modules/Village/Troops/RaidDeployController.cs` — the Breach button arms/disarms the
   stance; the two exclusive-arm sites drop the mode flag while a standing order keeps the stance.
4. `Assets/_Modules/Village/Troops/TroopController.cs` — stance read once per resolve and fed to
   `RallyHoldsMarch` / `PreferUnit` / `PickBucket`; wall multiplier applied in `Attack()` **outside**
   the catalog-multiplier gate and scoped to `WallSegment`; new trace fields + the first SWING line.
5. `Assets/Editor/Regression/WallBreachOrderRegression.cs` — cases 7-12. **No `DataRegression.cs`
   change needed:** the suite is already registered at `:1483`, so the suite count does not move.

## Two defects caught before the first line was written

* The reluctant multiplier inside `Attack()`'s existing `_preferStructures || _structureDamageMult
  != 1f || _unitDamageMult != 1f` gate would have applied to **nobody** — an ordinary troop has all
  three at default — while the trace printed `mult=0.10`. Applied outside; Case 12 pins the ordering.
* `RallyHoldsMarch`'s 4th arg had to widen from `HasOrder` to the **stance**, or the auto-chain dies
  on the first wall whenever a rally is set (the common mid-raid state per WO-1719's own remarks):
  order self-clears → march holds → wall bucket nulled → bucket -1 → the warband walks back to the flag.

## Checks actually run

* `python tools/gate_brace.py` over all five files → **`GATE_BRACE_SUMMARY bad=0 of 5`, exit 0**.
* Raw brace counts / NUL scan: 29/29, 19/19, 237/237, 268/268, 85/85; **0 NUL bytes** in all five.
* Added string literals are ASCII (regex over the added diff lines, 0 hits).
* `RaidBaseGenerator.cs:1986` read **read-only** to confirm raid walls are real `WallSegment`
  components — without that the type-scoped multiplier would silently never apply.

## The red proof was EXECUTED, and it found a real bug

Case 8's predicates are pure statics with no UnityEngine dependency, so they were run directly: a
scratch script **mechanically slices** the method bodies out of the shipped `RaidAssaultAi.cs` and
runs Case 8's two assertions. **Run 1, against the code as first handed back, came up RED** —
`FAIL stance armed + calm reachable defender -> bucket 2 (the wall); got 0`.

Cause: `PickBucket`'s Breach branch carries a **second** unit-beats-wall shortcut
(`if (hasUnit && unitInAttackRange) return 0;`) besides `PreferUnit`. Teaching only `PreferUnit`
about the stance left the stance broken for exactly the case the ruling is about. Siege never reaches
that line, which is why reading made it look correct. Fixed to
`if (!breachStance && hasUnit && unitInAttackRange) return 0;`; harness then `CASE8_GREEN`.
Deleting either stance gate independently reddens the case (runs 3 and 4), so neither is decoration.

## Owner ruling on the flagged reading

**Aggro-only — the shipped reading stands; the predicate was never flipped.** She first answered
"Any nearby hostile breaks the stance", then reversed within a minute to *"wait the other way — the
way suggested"*. Both are recorded verbatim in WO-1746 §4. WO-1738's summary clause "units-first is
the default inside it" was the lead's paraphrase and is retired on all three tickets.

## Not proven here

No compile, no gate marker, no `REGRESSION_OK`. Cases 7, 9, 10, 11, 12 remain reasoned, not executed.
Felt pace is the owner's verdict.

## Board

* WO-1746 `**Status: IMPLEMENTED, NOT YET GATED**`
* WO-1738 flipped to `IMPLEMENTED via WO-1746, awaiting gate`
* WO-1730 §3 Q2 and §5 Q2 annotated `RULED by WO-1738`
* `CLI_LANES_WO_NUMBERS.md` main-line banner bumped **1746 → 1747** in the same edit as the mint
* `BOARD.html` regeneration is the lead's step (this lane runs no tooling beyond the brace port)

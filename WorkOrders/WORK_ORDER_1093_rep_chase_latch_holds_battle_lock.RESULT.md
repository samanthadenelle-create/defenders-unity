# WO-1093 RESULT - rep-chase `_stung` latch: IMPLEMENTED on HEAD

**Verified:** 2026-09-09 (SILO 0B verify-and-flip pass, read-only; no `.cs` touched)
**Landed in:** `6a5c7a36d` ("chore: checkpoint complete workspace and rebuild board")
**Ancestry proof:** `git merge-base --is-ancestor 6a5c7a36d HEAD` -> exit 0 (HEAD = `184c8ff06`, branch `dev`)
**Compile proof:** `Builds/compile-gate-recurring-dragon-spells.log` (mtime 2026-09-09 13:49) contains
`COMPILE_GATE_OK :: scripts compiled clean`. Only `error CS` lines in that log are the pre-existing
`com.solana.unity_sdk` WebGL advisories, tagged `(package, advisory)` by the gate itself.

## THE TRAP, ANSWERED

`WO_1089_1094_WORKING_TREE_NOTE.md` warns that UI-seat edit agents were stopped and their half-written
files swept into `6a5c7a36d`. For this ticket the file is `OverworldEncounterSpawner.cs`, the ONE file
whose agent reported itself complete. **`git diff f4e4630e3 6a5c7a36d -- <file>` (165 lines changed)
was read in full and judged COMPLETE and COHERENT.** Every branch closes, every symbol resolves at
HEAD, no TODO/FIXME/NotImplemented was introduced, and the new path carries FlowTrace.

## FILE:LINE PROOF AT HEAD

| Claim | Proof at HEAD |
|---|---|
| Deaggro path exists and clears the latch | `OverworldEncounterSpawner.cs:1131` `private void Deaggro(string why)` -> sets `_stung = false` |
| It revokes only its OWN pursuit key | `:1138` `PostureSignals.RevokePursuit(_enemy.GetInstanceID())`; that method exists at `Assets/_Modules/Core/HudModel/PostureSignals.cs:164` |
| The chase now has the distance test it never had | `ChaseBrokeOff(bool, float, out string)` + `DeaggroRange = 26f` (owner ruling, hysteresis above `AggroRange = 14f` at `:999`) |
| The break is called every frame from Update, BEFORE the stamp | `:1332` `if (_stung && ChaseBrokeOff(heroAlive, d, out string breakWhy)) Deaggro(breakWhy);` |
| Dead hero gates BOTH ends (no sting/un-sting flicker) | `bool heroAlive = HeroHealth.Instance == null \|\| HeroHealth.Instance.IsAlive;` then `if (!_stung && heroAlive && d <= AggroRange)` |
| The distance test is deliberately NOT in `QuietIfNotPursuing` | `:1104-1108` carries the reason in-code (that sweep runs from `BattleArena.Resolve:2729` while the hero is ~7 km away at the arena) |
| New path is instrumented | `FlowTrace.Step("Encounter", ... "de-aggro ... pursuit pulse REVOKED ...")` inside `Deaggro`; plus a `FlowTrace.Throttle` chase-stall diagnostic at `:1397-1406` |
| The WO-1603 wiring lint stays satisfiable | both `PostureSignals.ReportPursuit` and the literal `OverworldEncounterSpawner/rep-chase` remain in the file |
| Symbols used by the new code all resolve | `TouchDistance` `:1506`, `_roamRepathAt` `:1205`, `AggroRange` `:999` |

## WHAT THE OWNER FELT-TESTS

Win an arena fight and return to town. Expect: the world is NOT stuck in combat state; the interact
button works; no `BATTLE_QUIESCENCE_FAIL (arena win) - battle-lock: still HELD` in the capture. On a
capture, the WO's own ordered checklist applies - `pursuit cleared`, then within 1-2 frames
`[Flow:Encounter] rep '<name>' de-aggro (hero lost the leash: d=... > deaggro=26.0m ...)`, then
`pursuit revoked`.

## WHAT IS **NOT** PROVEN

- **No runtime proof.** No arena win has been driven since the change. Compile-green is not behaviour.
- **No regression pin was added.** `git diff --stat f4e4630e3 6a5c7a36d -- Assets/Editor/Regression/`
  shows only `RaidBaseLayoutRegression.cs` (unrelated) and a `DataRegression.cs` registration line.
  The WO asked for "both regression cases present and passing" - that acceptance item is OPEN.
- **The hero distance was NOT added to the `BATTLE_QUIESCENCE_FAIL` pursuit-pulse line.** The WO calls
  that "the one field that would close the open question"; it lives in
  `PursuitBattleProbe` / `BattleQuiescenceGate`, and neither file is in the diff. The stall
  diagnostic added inside the spawner is a partial substitute, not the same line. OPEN.
- **Which variant fired (far-latch vs near-but-blocked) is still unproven**, exactly as the WO's own
  Unproven section states. The added chase-stall Throttle is the instrument that will settle it on the
  next occurrence.
- **No fresh `REGRESSION_OK` marker for this change.** The only fresh gate evidence is the compile gate.

---

## 2026-09-09 pins (edit-only PINS lane) - acceptance item 5's "both regression cases", half-closed

**New suite:** `Assets/Editor/Regression/RepChaseLeashRegression.cs`
**Tag / markers:** `[rep-chase-leash]` -> `REP_CHASE_LEASH_OK` / `REP_CHASE_LEASH_FAIL`
**Registration line (hand-back to the lead - MUST land INSIDE the START/END fence of
`Assets/Editor/Regression/DataRegression.cs`, in the same gate as the file):**

```csharp
DeNelle.Core.Diagnostics.Guard.Try("Regression", "rep-chase-leash suite", () => { if (!DeNelle.Editor.Regression.RepChaseLeashRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[rep-chase-leash] " + r); });
```

**Duplication checked FIRST, and it shaped the file.** Two suites already own neighbouring ground and
this one deliberately does not restate either:
- `HudPostureRegression` already drives revoke-one-keeps-others and `ClearPursuits -> peaceful`. So case
  `[absent-key]` drives the ONE lifecycle fact it leaves uncovered and that `Deaggro`'s doc-comment
  explicitly relies on: **revoking a key that was never reported is a NO-OP** (`Deaggro` revokes
  unconditionally on a rep that may never have stamped).
- `BattleQuiescenceRegression` already lints the owner tag `OverworldEncounterSpawner/rep-chase` and
  owns the battle-lock teardown contract. This suite owns the **leash** itself.

**Acceptance items pinned:** item 2 (the de-aggro line, "hero lost the leash: d > deaggro") -> cases
`[break-body]`, `[order]`, `[dead-hero]`, `[hysteresis]`. Item 3 (`pursuit revoked ... owner=...rep-chase`)
-> cases `[absent-key]`, `[break-body]`. Item 5 ("both regression cases present and passing") -> this
file is the "present" half. Items 1, 4, 6 are facts about a DRIVEN arena win and are **not** claimed.

**RED-first mutations:**

| Case | Mutation that FAILS it |
|---|---|
| `[absent-key]` (real drive, not a lint) | `PostureSignals.RevokePursuit` re-implemented as a clear-all, or made non-idempotent on an absent key - one rep losing its leash would then drop the combat HUD while other bodies still hunt her (the WO-1337 rule) |
| `[hysteresis]` | `DeaggroRange` lowered to or below `AggroRange` (the break/re-sting flicker), or either const removed/renamed (FAIL, not skip) |
| `[break-body]` | `Deaggro` no longer setting `_stung = false` (the latch is the bug), no longer calling `RevokePursuit(_enemy.GetInstanceID())` (the stale pulse rides its TTL - seq 4768's one-frame re-stamp), or losing its `FlowTrace.Step` (item 2/3 become unobservable) |
| `[order]` | moving `if (_stung && ChaseBrokeOff(...)) Deaggro(...)` AFTER the sting test - breaking after re-stinging cannot end a chase - or deleting the call entirely |
| `[dead-hero]` | narrowing `heroAlive` so a null `HeroHealth` reads DEAD (kills pursuit in every scene without one), or removing `ChaseBrokeOff`'s `if (!heroAlive)` refusal |
| `[own-key-only]` | any `PostureSignals.ClearPursuits` or `BattleLock.SetInBattle` call added to the spawner |
| `[preserve-sweep]` | adding a `Vector3.Distance` test inside `QuietIfNotPursuing` - it runs from `BattleArena.Resolve` while the hero is ~7 km away and would quiet EVERY rep on the map - or dropping the `_stung \|\| _engaged` preserve |
| `[stall-diagnostic]` | un-throttling the chase-stall line or keying the throttle on something other than the rep (a shared key throttles the fleet to one voice; a bare per-frame log evicts the boot window from the device ring - INSTRUMENTATION_STANDARD S8.2) |

**Comment-stripping is load-bearing here.** The spawner's in-code RCA **quotes** the forbidden call
(`PostureSignals.ClearPursuits` appears at `OverworldEncounterSpawner.cs:1020` as prose describing
`BattleArena`'s own teardown). A naive whole-file rule would red a healthy tree, so every source case
runs against a comment-stripped copy - a mention is not a call.

**No copied constants:** `DeaggroRange` / `AggroRange` are read by **reflection**; the case pins the
inequality, never 26 or 14.

### 2026-09-09 23:34 - `[hysteresis]` went RED on a healthy tree, and why (fixed, edit-only)

`Builds/wave1-reg` reported
`REP_CHASE_LEASH_FAIL x2 :: [hysteresis] ...DeaggroRange could not be read as a float constant | ...AggroRange could not be read...`.
**The producer was fine; the suite was pointed at the wrong TYPE.**

**Declaration, read at source 2026-09-09 (not from the earlier grep):**
`Assets/_Modules/Village/Enemies/OverworldEncounterSpawner.cs:981` `public sealed class RepEngageWatcher : MonoBehaviour`,
with `:999 private const float AggroRange = 14f;` and `:1047 private const float DeaggroRange = 26f;`.
Both are ordinary `private const float` - **no serialized field, no tunables-rail property** - so a
value read was and remains possible, and the case was NOT weakened to a source-lint.

**The mistake:** the file is named after `OverworldEncounterSpawner`, but the chase, the leash break,
the sting and both ranges live on `RepEngageWatcher`, a SECOND public type in the same file
(`OverworldEncounterSpawner` ends before `:981`). The first cut reflected off
`typeof(OverworldEncounterSpawner)`, both lookups returned null, and the case failed honestly - on
itself. **A file name is not a type name** (CLAUDE.md S11B: the const lines were confirmed by grep, the
enclosing class never was). Every other case in this suite is file-scoped source or a `PostureSignals`
drive, so none was affected.

**Lines changed (only this suite; no producer touched):** the two `TryFloatConst(...)` calls now pass
`typeof(RepEngageWatcher)`, their two failure strings name `RepEngageWatcher.<const>` with the
`file:line` of each declaration so the next re-point is one read, and a block comment above them records
the two-types-one-file trap. `typeof(OverworldEncounterSpawner)` no longer appears in the file.

**Gates re-run after the fix:** `python tools/gate_brace.py Assets/Editor/Regression/RepChaseLeashRegression.cs`
-> `GATE_BRACE_SUMMARY bad=0 of 1` (exit 0); braces 24/24; **0 NUL bytes**; parens balanced outside
strings/comments (135/135); 51/51 producer-token checks still green. **Still no Unity in this lane** -
the RED itself is the first execution any of these suites has had, and it is now a corrected pin, not a
verified-green one.

**Skip / PartialSkip handling:** none used. Case `[absent-key]` asserts its own **presence
precondition** (`PursuitCount == 0` after `ClearPursuits`, then `== 1` after one report) so a ring left
full by an earlier suite cannot make `ReportPursuit` a silent no-op and every later assertion vacuous;
the shared ring is left peaceful on the way out. A missing producer file or const is the FIXTURE and
FAILS naming it. One `return` in `Run`.

**Unproven (CLAUDE.md S11B):** RED-first is established **by construction** (51/51 token checks over all
three pin files, re-run after the final edits); only `[absent-key]` and
`[hysteresis]` execute product code, and even those were not RUN - **this lane has no Unity and executed
nothing.** No `COMPILE_GATE_OK`, no `REGRESSION_OK`; until the registration line lands,
`RegressionMarkerRegression` RULE 2 reds the run on an unregistered `Run(out string)` file.
Brace-balanced (24/24), NUL-free, parens balanced outside strings/comments (135/135).

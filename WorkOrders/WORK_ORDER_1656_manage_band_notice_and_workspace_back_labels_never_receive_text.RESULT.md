# WO-1656 RESULT — both stand-downs were FALSE POSITIVES; the guard's message accused two healthy labels

**Closed-out:** 2026-09-10 by lane FIT-GUARD (worktree `agent-a9e001ddb25631dda`, branched from dev `aba49bd4c`)
**Verdict:** **H1 on BOTH labels.** No producer defect. The defect was in the warning's wording.
**Gate:** NOT RUN — this lane holds no Unity. `gate_brace` + NUL clean on every `.cs` touched.

---

## 1. The discriminating check (WO-1656 §4), run on BOTH device logs

| grep | `…_0929_363722_logcat.txt` | `…_0943_363722_logcat.txt` |
|---|---|---|
| `back-glyph-miss` | **0** | **0** |
| `[Flow:Manage] notice:` | **0** | **0** |
| `armed but text still EMPTY` | 1 (`Band_Notice`) | 2 (`Band_Notice` + `ManageWorkspaceBack`) |

**Non-vacuity proof** — a zero only means something if the channel was live and the token was right:

- `grep -c '\[Flow:Manage\]' …_0943_…` → **550 lines**.
- `FlowTrace.Once("Manage", "back-glyph-miss", …)` exists at `ManageScreenPanel.cs:4118`;
  `FlowTrace.Step("Manage", "notice: " + msg)` at `:7581`. Both re-read at source this session.

**Reading:**

- **`ManageWorkspaceBack` → H1.** `IconBack` resolved (no miss trace), so `ApplyBackGlyph` blanked the
  label **deliberately** at `:4130-4131` — the WO-1491 ruling. The §1 frames showing an arrow agree.
- **`Band_Notice` → H1.** `FlushNotice` never passed its `IsNullOrEmpty(_vm.Notice)` guard, so no
  notice was ever raised. The band is correctly empty.

⚠ **Correction for anyone quoting this:** the two stand-downs are on the **`_0943_`** log. The
`_0929_` log carries **one** (`Band_Notice` only) — the back arrow's guard had not stood down before
that log ended.

## 2. What changed (guard only)

- **`Assets/_Modules/Core/UI/ElarionUiKitObsidian.cs`** — new
  `public static string FitGuardStandDownMessage(string path, bool emptyText)`; the stand-down site
  now calls it. The **EMPTY** half reports the state and hands the verdict to the producer instead of
  asserting `TEXT-NEVER-SET`. The **ZERO-HEIGHT** half still asserts a LAYOUT bug. The method is public
  for exactly one reason, written in its own doc comment: so a regression can pin the wording without
  driving 600 `LateUpdate` ticks in a batchmode call.
- **`Assets/Editor/Regression/TextFitGuardArmRegression.cs`** — `CaseD_StandDownDoesNotAccuseTheProducer`.
  RED-first: it fails on the pre-WO-1656 wording. It reds if the accusation returns, if the message
  stops naming the producer, if it stops reporting path/EMPTY/600 frames, **or if the zero-height half
  stops asserting** (acceptance §5.4 — narrowing must never become silencing).

**Not touched:** `ManageScreenPanel.cs`, the stand-down branch itself, `FontFloor`/`FontHardFloor`,
the fit/relax mechanism, the eight WO-1652 relaxations, any Manage layout number, any `.unity` scene.

## 3. Acceptance

| # | state |
|---|---|
| 5.1 check run + quoted + verdict written into the WO | **MET** (§4b) |
| 5.2 RED-first regression case | **MET** — `CaseD…`, fails on the old wording |
| 5.3 warning no longer fires for these two labels | ⚠ **DEVIATION, declared** — it fires REWORDED. True silence needs an `allowEmpty` opt-out threaded from `ManageScreenPanel`'s call sites, which §6 forbids this lane touching. Owner's call; will be minted as a follow-up on request. |
| 5.4 no other stand-down silenced | **MET** — pinned in both directions by `CaseD…` |
| 5.5 no font floor moved | **MET** — read-only |
| 5.6 `COMPILE_GATE_OK` + `REGRESSION_OK` on fresh logs | **OWED — the lead's gate.** No Unity in this lane. |

## 4. The next log will prove it

```
grep 'armed but text still EMPTY' <fresh logcat>     # expect 0 — the phrase itself changed
grep 'standing down; whether that is empty-BY-DESIGN' <fresh logcat>
grep 'TEXT-NEVER-SET' <fresh logcat>                 # expect 0 — the accusation is gone
```

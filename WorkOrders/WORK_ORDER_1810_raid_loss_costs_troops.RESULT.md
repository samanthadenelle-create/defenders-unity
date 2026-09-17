# WO-1810 RESULT — losing a raid now costs troops

**Status:** IMPLEMENTED
**Date:** 2026-09-16
**Ticket:** `WorkOrders/WORK_ORDER_1810_raid_loss_costs_troops.md`

---

## Files changed

| File | What |
|---|---|
| `Assets/_Modules/Village/Troops/RaidCasualtyPolicy.cs` | **NEW, 239 lines.** `RaidExitOutcome` enum (Undeclared/Victory/Retreat/Failed), `RaidCasualtyOutcome` struct, pure `Decide` / `LostOf` / `LossPctFor` / `Clamp01Pct`, the deterministic `PickLostSurvivors` (rookies first), and the two rail-resolved rates. No MonoBehaviour, no `Random`. |
| `Assets/_Modules/Core/State/ArmyStorage.cs` | `RemoveOwned(IEnumerable<string>)` at **`:358`** (doc block from `:329`) — the first troop deletion path in the game. Stale "NEVER deletes" canon corrected on the `MarkWounded` and `ReconcileAfterRaid` headers above it. |
| `Assets/_Modules/Village/Troops/RaidDeployController.cs` | file header note (`:24-29`); retreat-confirm copy (`:1619+`); `ForceExitHome` declares Failed **`:660`**; `DoRetreat` declares Retreat vs Failed **`:1687`**; `ShowNonVictoryResult` passes the lost count + traces it (`:1800+`); `_lastLostCount` / `_lastReturnedCount` **`:1870-1871`**; `_exitOutcome` + `DeclareRaidExitOutcome` **`:2160`** + `DeclaredExitOutcome`; the casualty block inside `ReconcileRaidEnd` **`:2248-2309`** (`Decide` `:2260`, `RemoveOwned` `:2274`). |
| `Assets/_Modules/Village/World/Camps/RaidVictoryController.cs` | one insertion at the `_handled` latch — declares `Victory` at **`:274`**. |
| `Assets/_Modules/Village/UI/EndState/EndStateVM.cs` | `troopsLost` trailing optional param **`:565`**; the wounded sentence REPLACED by the lost sentence **`:582-621`**; trace line extended. |
| `Assets/_Modules/Core/Ops/RemoteTunables.cs` | key + default consts **`:839-877`**; 2 `TunableSpec` rows from **`:1863`**. |
| `api/_lib/tunables.js` | allowlist rows `:299-313`. |
| `api/_lib/tunable-manifest.js` | 2 hand-authored presentation cards (`area: misc`, label / plain English / 0..100 range / risk). |
| `api/_lib/tunable-manifest.generated.json` | regenerated — `TUNABLE_MANIFEST_GEN_OK knobs=77`, diff is **+10 lines, additions only**. |
| `docs/PROD022_TUNABLE_FLAGS.md` | rows **#76 / #77**, both flagged as a stated departure from "the default is today's behaviour". |
| `Assets/Editor/Regression/RemoteTunablesDefaultsRegression.cs` | `ExpectedDefaults` +2; `ExpectedKnobCount` 75 → **77** (measured, not typed: the generator's own marker). |
| `Assets/Editor/Regression/RaidCasualtyRegression.cs` | **NEW.** Marker `RAID_CASUALTY_OK` / `RAID_CASUALTY_FAIL`; cases A policy / B rounding edges / C roster mutation end-to-end / D call-site source-lint. |
| `Assets/Editor/Regression/DataRegression.cs` | one registration line beside `heartfire-pips`. |
| `Assets/Editor/Regression/RaidPayoutVisibilityRegression.cs` | case G copy pin re-pointed from `"2 troops return wounded"` to `"4 troops lost"` (the COUNT is pinned, `troopsLost: 4` passed explicitly), and its zero-loss case now passes `troopsLost: 0`. |

## Behaviour, before → after

| Exit | Before (proven at source + on device) | After |
|---|---|---|
| Timeout / wipe / watchdog / hero-death settle | fallen → **wounded**, free 5-45 min heal, slots still occupied | fallen **removed**; **100%** of the survivors removed too |
| Player Retreat | same free wounding | fallen removed; **60%** of survivors removed (nearest, floor of one); 40% home **healthy** |
| Victory | fallen → wounded | fallen **removed** (ruling: "any troop killed is dead"); survivors untouched, 3-star veterancy unchanged |
| Screen | "N troops return wounded" | "N troops lost - <reason>" / "Every troop came home." |

Wounded/recovery is no longer a raid outcome. `MarkWounded` / `TickRecovery` / `AdvanceRecovery` /
`TroopRecoveryService` all stay live as the **backstop** the reconcile still runs after the removal, which
is also what keeps `RaidCooldownRegression`'s `ResolveRecoverySeconds(` pin satisfied by honest code.

## Gate evidence from this lane

- `python tools/gate_brace.py` on all 10 touched `.cs` → `GATE_BRACE_SUMMARY bad=0 of 10`, **exit 0**.
- NUL scan on the same 10 → `NUL=0` on every file; raw brace counts balanced on every file.
- `node tools/gen-tunable-manifest.mjs` → `TUNABLE_MANIFEST_GEN_OK knobs=77`.
- `node --test test/tunables-manifest.test.js` → **27 pass / 0 fail**.

## NOT proven by this lane

`COMPILE_GATE_OK` and `REGRESSION_OK` — this lane does not run Unity. `RaidCasualtyRegression` has never
executed; its arithmetic was re-derived by hand and the integer table independently checked, but a suite
that has not run is a claim. The `[raid-casualty]`, `[raid-payout-visibility]` and `[tunable-defaults]`
suites are the three to read on the lead's fresh log.

## Follow-up, same ticket (lead-directed, 2026-09-16) — both gaps CLOSED

- **The victory screen now states the cost.** `EndStateVM.TroopsLost` field `:114`; `FromRaidVictory`
  trailing `troopsLost = -1` `:432`, sentence only when `> 0` `:451`, field set `:460`;
  `FromRaidRetreat` sets the same field `:662`. Wired from the victory reconcile:
  `RaidDeployController.LastTroopsLost` `:1879` → `RaidVictoryController._troopsLostThisRaid` `:839`,
  read at the reconcile `:860` (+ traced `:864`), passed to the screen `:981`.
- **`HeroHealth.cs:1624-1634`** trace copy corrected: the old *"the fallen are wounded"* now reads that
  the fallen are dead and the warband does not come home. Outside the span
  `RaidScoringRegression` lints (that span ends at the first `enemyOwnedScene || raidDeathExit`).
- **`RaidCasualtyRegression` case E** `:306-356`: victory-with-kills asserts `TroopsLost == 3` and
  `"3 troops lost"` in the subtitle, a clean sweep prints no line, an unknown (-1) count prints no line,
  and the controller wiring (`LastTroopsLost` / `_troopsLostThisRaid`) is source-linted.
- Re-gated: `gate_brace.py` on the 5 follow-up files → `bad=0 of 5`, exit 0; `NUL=0` and raw braces
  balanced on all 5.
- **Trace token deviation, deliberate:** an undeclared exit prints `outcome=fail(undeclared)` rather than a
  bare `fail`, so a capture can tell the ruling's default apart from a declaration that went missing.

## Open question for the owner

Partial **RETREAT LOOT on a failed raid** (the captured 19:59 exit paid 486w/297i/11f/558g/2c on a 12%-razed
timeout). Not ruled on, untouched. Should a fail that now costs 100% of the warband still pay 18%?

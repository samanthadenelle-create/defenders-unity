# WO-1883 RESULT — CLAIM (pending lead gate)

**Status claimed:** `IMPLEMENTED PENDING LEAD GATE`  
**Commit:** none (lane instruction: do not commit)  
**Compile / DataRegression:** not run (lane instruction: do not run Unity compile gate)

## Claim

Town collector pending stays at the building until Collect. Accrue auto-spill to the bank (ruling 26b `TryOverflowToBank` while accruing) is retired. Bank-full still stalls and never burns. Collect still banks headroom and leaves the remainder pending. No Raid Cache TTL added. FlowTrace retained on Accrue / Collect / at-cap.

## Evidence (tree, this seat)

### `Assets/_Modules/Village/Buildings/Progression/ResourceCollector.cs`
- `Accrue` is again a single capacity clamp: `_pending = Math.Min(cap, _pending + amount * health)` — no while-loop, no `TryOverflowToBank`.
- Deleted: `TryOverflowToBank`, `SettleOverflowPool`, `OverflowEpsilon`, `MaxOverflowPasses`, `SimulateAccrueWithOverflow`.
- Kept: `Collect` + `SettleCollect` (WO-1392 never-burn), FlowTrace on accrue / at-cap / collect.
- Added/kept pure helpers for the oracle: `SimulateAccrue` (never banks; `bankRoom` ignored) and `SettleOverflow` (Collect-with-headroom model only).

### `Assets/Editor/Regression/CollectorOverflowRegression.cs`
Re-pointed off auto-spill onto WO-1883:
- `[overflow-never-burns]` — Accrue stalls at cap; books close; `banked` always 0; already-held never lost.
- `[pending-until-collect]` — Accrue with bank room does **not** reduce pending / does not bank; away fill caps then stalls without draining the bank.
- `[collect-banks-headroom]` — `SettleOverflow` / Collect headroom: partial bank leaves remainder; room 0 leaves pool intact.
- `[tap-still-works]` — Collect still `SettleCollect` + applied `GrantSpendable` + popup; no tap bonus.
- `[no-accrue-auto-spill]` — source lint: no `TryOverflowToBank` / `SettleOverflowPool` / live `auto-overflow`; Accrue still clamps + FlowTrace; Collect still settles by banked.

### `CollectorIncomeRegression`
No case required Accrue auto-spill. Case 15 `[overflow-stays-pending]` still pins Collect never-burn only — left unchanged.

## Acceptance mapping (claim only)

| Criterion | Claim |
|---|---|
| Accrue with bank room > 0 does not reduce `_pending` without Collect | `SimulateAccrue(..., bankRoom > 0)` returns `banked == 0` and pending at cap — pinned by `[pending-until-collect]` |
| Accrue with bank full still stalls | `[overflow-never-burns]` + `[pending-until-collect]` |
| Collect still banks headroom | `[collect-banks-headroom]` + `[tap-still-works]` / Income Case 15 |
| No Raid Cache TTL | Not touched |
| FlowTrace stays | Accrue throttle + at-cap throttle + Collect warn/step retained |
| `COMPILE_GATE_OK` + `REGRESSION_OK n/n` | **Not proven here** — lead owns gate |

## Local check run
```
python tools/gate_brace.py Assets/_Modules/Village/Buildings/Progression/ResourceCollector.cs Assets/Editor/Regression/CollectorOverflowRegression.cs
→ GATE_BRACE_SUMMARY bad=0 of 2
```

## Door note (not in scope, called out per WO)
Raid Cache remains persistent town-wide with no TTL (`dotr-raid-cache-*`). If the claim door is hard to find, that is a **door** problem — do not invent expiry. Not changed in this lane.

## Not done by this seat
- Unity `COMPILE_GATE_OK`
- `DataRegression` / `REGRESSION_OK`
- git commit / push
- `BOARD.html` / `board_build.py`
- APK / R2

# WO-1693 — Heartbound: the pulse ↔ state STAKE UNIT contract

**Status:** IMPLEMENTED 2026-09-10 — the unit is settled at every seam, the state writer refuses whole SKR by name, and `test/heartbound-contract.test.js` wires the real modules over three pulses (RED on HEAD `0b942d0be`, green after; 133 tests / 132 pass / 0 fail / 1 pinned todo).

**Lane:** HEART-CONTRACT SME (worktree on `dev`, ff-only to `0b942d0be`).
**Number:** pre-assigned by the lead. `CLI_LANES_WO_NUMBERS.md` deliberately NOT edited by this lane.
**Origin:** `WorkOrders/WORK_ORDER_1684_heart_011_telemetry_and_balance_analytics.RESULT.md` §5.1 — a finding raised, not fixed, by the HEART-011 lane.
**Not committed, no Unity, no `.cs` touched.**

---

## 1. The defect, as measured (not inferred)

`last_actual_stake` / `effective_resonating_stake` carried **two units 1e6 apart** across their two
callers, and the mismatch does not mis-bucket — **it throws**.

Measured in this worktree at HEAD `0b942d0be`, with the fix stashed:

```
$ node -e "... applyPulse(1000 effective / 4000 actual) x3, each into recordVerifiedStake ..."
pulse 1 effectiveSkr= 1750    -> write OK
pulse 2 effectiveSkr= 2312.5  -> TypeError: effectiveResonatingStake: expected an integer, got 2312.5
```

The resonance ramp closes 25% of the gap per pulse, so it produces an **integer on pulse 1 and a
fraction from pulse 2 on** — which is why a one-pulse test passes and proves nothing, and why the
defect could sit in `dev` behind two green single-module suites.

**Blast radius if wired as it stood:** `api/cron/heart-pulse.js` is the only live caller of the pulse
job. The first commit to point its `persistPlayerState` seam at `heartbound-state.recordVerifiedStake`
would have 500'd the **daily cron on the second pulse, for every staked player** — a dead job, not a
skewed chart.

**The silent half.** The same ambiguity ran the other way and did *not* throw: `heartbound-pulse.js`
read `Number(state.lastActualStake)` off a HEART-002 snapshot whose value is a raw digit string, so a
4,000 SKR position would have entered the curve as **four billion SKR** — eligibility, ramp, score and
tier all 1e6 too large, with no error anywhere.

---

## 2. THE CONTRACT — which seam speaks which unit (every line opened at source 2026-09-10)

| Seam | Unit | Type in JS | Evidence |
|---|---|---|---|
| `heartbound_state.last_actual_stake` | **RAW u128 base units** | text (`::text`), BigInt via `toBigInt` | `api/migrations/20260910_0025_heartbound_state.sql:68` `NUMERIC(39,0)`, and `:113-117` states the reason: "Raw SKR is a u128 on-chain… read ::text and handled as BigInt in JS". Mirrored at `api/schema.sql:2104`. |
| `heartbound_state.effective_resonating_stake` | **RAW u128 base units** | text / BigInt | same DDL block; `heartbound-state.js:219-221` returns both as digit strings from `toSnapshot`. |
| `heartbound_state.resonance_score` | decimal string | text | `heartbound-state.js:232-234` — "TEXT on the wire, deliberately". Not a stake; unchanged by this WO. |
| `heartbound-resonance.js` `actualSkr` / `effectiveSkr` | **WHOLE SKR** | `Number`, routinely **fractional** | `heartbound-resonance.js:131-138` `rawTokensToSkr` returns `whole + frac/unit`; `:363-381` `applyPulse` ramps in that unit. |
| `player_pulse_grant.effective_stake` | **WHOLE SKR** | fixed-point text, scale 6 | `api/_lib/heartbound-pulse-schema.sql:119` `NUMERIC(39,6)`, and `:107-113` states why: "HEART-003 works in WHOLE SKR and returns a fractional figure… Six decimals is exactly the chain's base-unit granularity (chain.skrBaseUnits = 1e6)". |
| `global_heart_pulse.*_share_price` | **RAW u128** | text | `heartbound-pulse-schema.sql:64-65` `NUMERIC(39,0)`. Untouched — it was never ambiguous. |
| the chain reading (`readStake`) | **RAW u128** | BigInt / string | `heartbound-pulse.js` `stakeReadingToSkr` — already the single crossing IN, and already correct. |

**The one sentence:** *everything that lands in a `NUMERIC(39,0)` stake column is raw base units;
everything the resonance module returns is whole SKR; `player_pulse_grant.effective_stake` is the one
stake column that stores whole SKR, at scale 6.*

---

## 3. What was changed

### 3.1 `api/_lib/heartbound-resonance.js` — the return crossing, in the module that already owned the forward one
Two exported functions added to the BOUNDARY layer (header `:32-39` already names that layer):
- `skrToRawTokens(skr, cfg)` → BigInt. The exact inverse of `rawTokensToSkr`. Whole and fractional
  parts split **before** the BigInt multiply, so a large position never rides through a double; a
  carry falls out of the addition.
- `skrToNumericText(skr, cfg)` → the fixed-point text a `NUMERIC(39,scale)` stake column takes, with
  the **scale DERIVED from `cfg.chain.skrBaseUnits`**, never a literal `6`.

⛔ **Why not at the call site.** `heartbound-pulse.js:186-188` already records the rule — "Converting is
the resonance module's own job and NONE of it is re-implemented here". A `toFixed(6)` or a `* 1e6` in
the pulse module would be a second copy of `chain.skrBaseUnits`, i.e. the duplicated-state failure
CLAUDE.md §2/§5/§8/§16 each carry a scar from. `skrBaseUnits` is read, never restated.

### 3.2 `api/_lib/heartbound-state.js` — the verified-stake writer takes raw units and says so
- New `toVerifiedStakeText(value, label)`, used by `recordVerifiedStake` for both stake fields. A
  fractional Number **or** a fractional string is refused with a message that names the unit and the
  one function that produces it:
  `"effectiveResonatingStake must be RAW SKR BASE UNITS (a u128 integer; 1 SKR = chain.skrBaseUnits), never whole SKR: expected an integer, got 2312.5. Convert at the boundary with heartbound-resonance.skrToRawTokens(skr).toString()."`
  (The substring `expected an integer` is retained deliberately — `test/heartbound-state.test.js:283-285`
  pins it.)
- The stale ⛔ block on the `skr_verification_success` emit — which said the unit was unsettled and
  "NOT THIS LANE'S TO SETTLE" — is rewritten in the same change (§15). The bucket stays off the event,
  but for a **new** reason: `telemetry.stakeBucket` ladders whole SKR while
  `api/heartbound/status.js:400-408` ladders base units with the same labels; reconciling the two
  ladders is WO-1684 RESULT §5.2's follow-up, not this one.

⚠ **The guard's stated limit.** A whole-SKR value that happens to be an **integer** (pulse 1's `1750`)
is indistinguishable from 1750 base units. No guard on the writer's side can catch that. It is pinned
as a passing test that documents the acceptance, and the real defence is §3.3.

### 3.3 `api/_lib/heartbound-pulse.js` — the crossing, both directions, with the unit in the names
- `stateSnapshotToPulseState(snapshot, resonance, cfg)` (**exported**) — the raw→SKR crossing IN. Turns
  a `heartbound-state` snapshot into the whole-SKR row shape `processPlayerForPulse` documents at
  `:437`. Closes the silent half of the defect.
- The persist payload's stake keys are **renamed to carry the unit**: `lastActualStakeRaw` /
  `effectiveResonatingStakeRaw`, values produced by `res.skrToRawTokens(...).toString()`. Two units on
  one name is the defect; a name that carries the unit is the cure.
- `verifiedStakeArgsFromPersistPayload(payload)` (**exported**) — maps that payload onto
  `recordVerifiedStake`'s option names, in the open, in one place.
- `player_pulse_grant.effective_stake` is now bound as `res.skrToNumericText(effectiveStake, cfg)`
  (both the INSERT and the PENDING-promotion UPDATE) instead of `String(effectiveStake)`. **Never
  through `toRawAmountText`** — that function coerces a u128 integer and would reject the very
  fraction this column exists to hold. `String()` happened to be valid text for `2734.375` and is
  still wrong: a large Number prints as `1e+21` (Postgres rejects) and a long one prints past scale 6
  (Postgres silently rounds).

### 3.4 `test/heartbound-contract.test.js` — new, and it is a SEAM suite
Wires the **real** pulse and state modules over a **stateful** in-memory `sql` mock (pulse N+1 reads
back what pulse N wrote — a queued script would hand back whatever the test expected) and runs three
pulses. Asserts no throw and the **exact stored strings**:

| Column | Unit | Stored, pulses 1→3 |
|---|---|---|
| `heartbound_state.effective_resonating_stake` | raw base units | `1750000000`, `2312500000`, `2734375000` |
| `heartbound_state.last_actual_stake` | raw base units | `4000000000` ×3 |
| `player_pulse_grant.effective_stake` | whole SKR, scale 6 | `1750.000000`, `2312.500000`, `2734.375000` |

Plus: the read crossing, the guard's message, the guard's stated limit, and a `skrToRawTokens` ⇄
`rawTokensToSkr` round-trip over the ramp figures.

---

## 4. What the cron shell still has to do (NOT this lane's file)

`api/cron/heart-pulse.js` is outside this lane. The wire is two lines, and it is the shape the new
suite drives:

```js
listActiveStates:   async (sql) => (await listSnapshots(sql)).map(s => pulse.stateSnapshotToPulseState(s, resonance)),
persistPlayerState: async (sql, payload) =>
    state.recordVerifiedStake(sql, payload.playerId, pulse.verifiedStakeArgsFromPersistPayload(payload)),
```

Until that lands, **acceptance is proven by the suite, not by a live cron** — stated as unproven in
the RESULT rather than ticked.

---

## 5. Findings raised, not fixed

1. **`recordVerifiedStake` writes no streak.** Its INSERT names `last_actual_stake`,
   `effective_resonating_stake`, `resonance_score`, `resonance_tier`, `highest_lifetime_tier` and
   `current_tree_resonance_stage` — and **not** `continuous_pulse_count`, `total_lifetime_pulses` or
   `last_global_pulse_id`, though the RETURNING clause lists all three and the pulse job computes all
   three. So the tenure input to the curve never lands: two pulses through the real wire leave
   `continuousPulseCount = 0`. Pinned as a `{ todo: … }` case in the new suite rather than omitted.
2. **`currentTreeResonanceStage` is reset by every pulse.** The pulse payload carries no tree stage, so
   the writer's default `0` is written to the column on every verified write. Fixing it means the
   writer leaving a column alone when the caller says nothing — a change to a statement this WO's
   brief does not own.
3. **Two bucket ladders with identical labels and different units** — WO-1684 RESULT §5.2, unchanged
   and still open. `telemetry.stakeBucket` ladders whole SKR; `api/heartbound/status.js:400-408`
   ladders base units. Now that the unit is settled, the re-point is unblocked.

---

## 6. Do NOT touch (honoured)

`api/schema.sql`, `api/migrations/*`, `api/_lib/heartbound-tiers.js`, `api/_lib/heartbound-events.js`,
any `.cs`, `DataRegression.cs`, `CLI_LANES_WO_NUMBERS.md`. `git status --short` shows exactly the four
paths in §3 plus this WO and its RESULT.

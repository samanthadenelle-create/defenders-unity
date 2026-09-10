# WO-1693 — RESULT

**Status:** IMPLEMENTED 2026-09-10.
**Lane:** HEART-CONTRACT SME, worktree `agent-a985087f36e7b0c2e` on `dev`, `git merge --ff-only refs/heads/dev` → **`0b942d0be`** (the HEART-011 commit) before any edit.
**Not committed, not pushed, no Unity run, no `.cs` touched** — so CLAUDE.md §10's brace/NUL items do not apply.

---

## 1. Files

**Modified**
- `api/_lib/heartbound-resonance.js` — `skrToRawTokens` + `skrToNumericText` added to the BOUNDARY layer; header `:32-39` updated to say the crossing is bidirectional; both exported.
- `api/_lib/heartbound-state.js` — new `toVerifiedStakeText` guard (exported), used by `recordVerifiedStake` for both stake fields; the stale "NOT THIS LANE'S TO SETTLE" ⛔ block on the `skr_verification_success` emit rewritten (§15 canon-in-the-same-breath).
- `api/_lib/heartbound-pulse.js` — `stateSnapshotToPulseState` + `verifiedStakeArgsFromPersistPayload` added and exported; persist payload stake keys renamed to `lastActualStakeRaw` / `effectiveResonatingStakeRaw` and converted with `skrToRawTokens`; `player_pulse_grant.effective_stake` bound via `skrToNumericText` at both write sites. Three of its own comments were corrected in the same change (§15): `:63-69` said "the single crossing" when there are three, `:188-190` called the chain reading "THE UNIT BOUNDARY, IN EXACTLY ONE PLACE" while the state write crossed no boundary at all, and the adapter's timestamp fields now carry ISO strings so a field named `...Utc` is not itself a name that lies about its unit.

**New**
- `test/heartbound-contract.test.js` — 7 cases (6 asserting, 1 pinned todo).

**Board**
- `WorkOrders/WORK_ORDER_1693_heartbound_pulse_state_stake_unit_contract.md` — **Status: IMPLEMENTED**.
- `WorkOrders/WORK_ORDER_1693_heartbound_pulse_state_stake_unit_contract.RESULT.md` — this file.

`git status --short` shows exactly those six paths. Nothing on the do-not-touch list changed:
`api/schema.sql`, `api/migrations/*`, `heartbound-tiers.js`, `heartbound-events.js`, any `.cs`,
`DataRegression.cs`, `CLI_LANES_WO_NUMBERS.md`.

---

## 2. The RED, quoted

### 2.1 The defect itself, at HEAD, with the fix stashed
The wire the cron shell would have written, run against the real modules:

```
$ node -e "... applyPulse(1000 effective / 4000 actual) x3, each into recordVerifiedStake ..."
pulse 1 effectiveSkr= 1750    -> write OK
pulse 2 effectiveSkr= 2312.5  -> TypeError: effectiveResonatingStake: expected an integer, got 2312.5
```

⚠ **One correction to WO-1684 §5.1, in this lane's favour and worth recording.** That RESULT quoted
the throw as `expected a non-negative integer string, got "2734.375"`, which is the message
`toBigInt` raises for a *string*. A Number takes the other branch (`heartbound-resonance.js:105`) and
raises `expected an integer, got 2312.5` — **and it fires one pulse EARLIER**, on pulse 2, because the
pulse module passes `nextState.effectiveSkr` as a Number, not `String(...)`. Same defect, sooner and
with a different message. Both messages are in the tree; the one above is what an operator would
actually have seen.

### 2.2 The new suite, RED on HEAD
`git stash push` on the three tracked modules (the new untracked test file stays), then:

```
$ node --test test/heartbound-contract.test.js
ℹ tests 7   ℹ pass 1   ℹ fail 5   ℹ todo 1

✖ WO-1693: three consecutive pulses wire pulse -> state without throwing, and store RAW BASE UNITS
  TypeError: pulse.stateSnapshotToPulseState is not a function
✖ the READ side crosses too: a raw base-unit row is not read as a billion-SKR position
  TypeError: pulse.stateSnapshotToPulseState is not a function
✖ recordVerifiedStake REFUSES a fractional (whole-SKR) stake with a message naming the unit
  AssertionError: The input did not match the regular expression /RAW SKR BASE UNITS/. Input:
    'effectiveResonatingStake: expected an integer, got 2312.5'
✖ skrToRawTokens is the exact inverse of rawTokensToSkr over the ramp figures
  TypeError: resonance.skrToRawTokens is not a function
✖ skrToNumericText derives its scale from chain.skrBaseUnits, never a literal 6
```

The one case that PASSES on HEAD is the one documenting the guard's stated limit (an integer
whole-SKR value is accepted, because it is indistinguishable) — correct: that behaviour did not change.

`git stash pop` restored the three modules cleanly (no conflict).

---

## 3. The GREEN

```
$ node --test test/heartbound-*.test.js test/skr-staking.test.js
ℹ tests 133
ℹ pass 132
ℹ fail 0
ℹ skipped 0
ℹ todo 1
```

- **Baseline before any edit, measured on this HEAD: 126 tests / 126 pass / 0 fail.**
  ⚠ **The brief's "180 before you start" was NOT reproducible at `0b942d0be`** — the same command
  returned 126, which is also the figure `WORK_ORDER_1684_….RESULT.md` §2 records. Named as a stale
  copied number rather than worked around (CLAUDE.md §11B-A). 126 → 133 is this lane's +7.
- **Every pre-existing case is still green**: 126 pass before, 126 of the 132 passes after are those
  same cases (`fail 0` on the combined run).
- The single `todo` is finding §5.1 below — a deliberate, labelled expected-failure, not a red.
- `node --check` clean on all three modified `.js` files.

The three pulses store, verified as **exact strings** out of the mock:

| Column | Unit | Pulses 1→3 |
|---|---|---|
| `heartbound_state.effective_resonating_stake` | raw u128 base units | `1750000000`, `2312500000`, `2734375000` |
| `heartbound_state.last_actual_stake` | raw u128 base units | `4000000000` ×3 |
| `player_pulse_grant.effective_stake` | whole SKR, `NUMERIC(39,6)` | `1750.000000`, `2312.500000`, `2734.375000` |

---

## 4. Evidence for the contract

⚠ **Every line number below was RE-READ against the POST-EDIT tree**, not carried over from the
pre-edit read — this lane's own inserts moved several of them, and a citation that no longer resolves
is the exact failure CLAUDE.md §11B-A names.

- `api/migrations/20260910_0025_heartbound_state.sql:68` — `last_actual_stake NUMERIC(39,0) NOT NULL DEFAULT 0`; `:113-117` states the unit ("Raw SKR is a u128 on-chain… read ::text and handled as BigInt in JS"). Mirrored at `api/schema.sql:2104`.
- `api/_lib/heartbound-pulse-schema.sql:119` — `effective_stake NUMERIC(39,6)`; `:107-113` states its unit is **whole SKR** and that scale 6 is `chain.skrBaseUnits = 1e6`.
- `api/_lib/heartbound-resonance.js:131-138` `rawTokensToSkr` — returns `Number(whole) + Number(frac)/Number(unit)`, i.e. whole SKR, fractional.
- `api/_lib/heartbound-resonance.js:363-381` `applyPulse` — the 25%-of-the-gap ramp, in whole SKR; `:372-374` is why pulse 1 is an integer and pulse 2 is not.
- `api/_lib/heartbound-state.js:219-221` `toSnapshot` — both stake fields returned as raw digit **strings**; this is what made the read side (`Number('4000000000')`) silently wrong.
- `api/_lib/heartbound-resonance-config.json` `chain.skrBaseUnits: 1000000` — the single unit constant; asserted equal to the test's `SKR` so the suite cannot drift from it either.

---

## 5. Findings — raised, not fixed

### 5.1 `recordVerifiedStake` writes NO streak column (pinned as a todo, not buried)
Its INSERT names `last_actual_stake`, `effective_resonating_stake`, `resonance_score`,
`resonance_tier`, `highest_lifetime_tier`, `current_tree_resonance_stage` — and **not**
`continuous_pulse_count`, `total_lifetime_pulses` or `last_global_pulse_id`, though the RETURNING
clause lists all three, the columns exist, and `processPlayerForPulse` computes all three and puts
them in its persist payload. Measured through the real wire: after two pulses,
`readHeartboundState(...).continuousPulseCount === 0`. Tenure is half of the resonance score
(`tenurePower`), so this is a live gameplay defect, not cosmetics.

It does **not** affect the stake strings this WO fixes (the ramp reads only actual + prior effective),
so it does not block acceptance — and it is asserted under
`{ todo: 'WO-1693 finding 2 — recordVerifiedStake names no streak column' }` so a suite that goes green
does not hide it. The fix is three column names in one statement, in a lane that owns
`heartbound-state.js`'s write contract.

### 5.2 Every verified write RESETS `current_tree_resonance_stage` to 0
`recordVerifiedStake`'s option defaults to `0` and the statement writes `EXCLUDED`. The pulse payload
carries no tree stage, so a stage set by a HEART-005 lane is zeroed by the next pulse. The adapter
forwards the field when present and does **not** invent one. Fixing it properly means the writer
leaving the column alone when the caller says nothing — a change to a statement this brief does not
own. **Not observed in production** (the wire does not exist yet); read out of the statement.

### 5.3 Two bucket ladders, identical labels, different units — still open
WO-1684 RESULT §5.2, unchanged: `telemetry.stakeBucket` ladders whole SKR,
`api/heartbound/status.js:400-408` `bucketOf` ladders base units. Now that the unit is settled, the
re-point is unblocked, but it is outside this file list and was not done.

---

## 6. Stated as UNPROVEN

- **No database was reachable from this worktree**, so nothing here is proven against Postgres. In
  particular `NUMERIC(39,6)` accepting `'2734.375000'` and `NUMERIC(39,0)` accepting `'2734375000'`
  are read off the DDL and the type definitions, **not** observed. The cheap close: one `psql`
  round-trip of those six strings after the migrations are applied.
- **The cron is still not wired**, so the end-to-end claim "the daily pulse now persists correctly"
  is proven only at the seam, by the suite. `api/cron/heart-pulse.js` is outside this lane; the two
  lines it needs are written out in §4 of the WO.
- **The guard cannot catch an INTEGER whole-SKR value** (`1750` SKR vs `1750` base units are the same
  digits). This is a property of the data, not a gap that more code closes; the defence is the
  caller-side conversion, and the limitation is pinned by a passing test so nobody later reads the
  guard as complete.
- **WO-1684 §5.1's quoted throw message was wrong in detail** (§2.1 above). Corrected here from a
  measurement, not from reading the other document.

---

## 7. Paths

- WO: `WorkOrders/WORK_ORDER_1693_heartbound_pulse_state_stake_unit_contract.md` (**Status: IMPLEMENTED**)
- RESULT: `WorkOrders/WORK_ORDER_1693_heartbound_pulse_state_stake_unit_contract.RESULT.md` (this file)

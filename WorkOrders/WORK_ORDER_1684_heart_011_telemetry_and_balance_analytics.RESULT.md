# WO-1684 / HEART-011 — RESULT

**Status:** IMPLEMENTED (backend half) 2026-09-10 — **acceptance 1 is NOT met and this document says so first.**
**Lane:** HEART-011 SME, worktree on `dev` (merged `--ff-only` to `1133baf35` before starting).
**Not committed, not pushed, no Unity run** (per brief). No `.cs` touched, so gate items 6 do not apply.

---

## 1. Files

**New**
- `api/_lib/heartbound-telemetry.js` — the pure event-shaping module (names, one-emitter table, hash, buckets, leak guard, sink adapter, whale aggregate, and the D3 ten-question answer path in the header).
- `api/_lib/heartbound-telemetry-config.json` — the bucket edges (acceptance 4).
- `test/heartbound-telemetry.test.js` — 28 cases.

**Modified (emit seams only; behaviour unchanged)**
- `api/_lib/heartbound-pulse.js` — `emit` dep (default no-op) on `detectPulse` and `processPlayerForPulse`; 5 emit sites.
- `api/_lib/heartbound-state.js` — `emit` dep (default no-op) on `activateHeartbound`, `recordVerifiedStake`, `recordVerificationFailure`; 3 emit sites.

**Board**
- `WorkOrders/WORK_ORDER_1684_heart_011_telemetry_and_balance_analytics.md` — `**Status:**` flipped to IMPLEMENTED.

Nothing else in the tree changed (`git status --short` shows exactly these five code/doc paths plus this file).

---

## 2. Evidence

### Tests — 126 pass, 0 fail
```
$ node --test test/heartbound-*.test.js test/skr-staking.test.js
ℹ tests 126
ℹ pass 126
ℹ fail 0
ℹ duration_ms 216.07
```
98 of those are the pre-existing pulse/state/resonance/skr suites, unchanged and still green — that is the proof that adding the seams altered no existing behaviour or expectation. 28 are new.

`node --check` clean on all three changed/new `.js` files (`api/_lib/heartbound-telemetry.js`, `heartbound-pulse.js`, `heartbound-state.js`).

### The sink — reused, no migration
`api/_lib/audit.js:112` `logApiEvent(sql, identity, eventName, properties)` → one INSERT into `analytics_events` (`:118-127`), never throws. `api/trace.js:12-14` states the house position ("reuses the proven analytics_events table… NO new table/migration required"), and WO-1684 §5 forbids a new table. **`api/migrations/20260910_0027_heartbound_telemetry.sql` was NOT written** — the brief's "if none fits" branch does not apply, and writing DDL would have violated §5.

### Emit sites (file:line at the working tree)
| Event | Site |
|---|---|
| `heart_pulse_global_detected` | `api/_lib/heartbound-pulse.js:338-350` — after the won mint only |
| `heart_pulse_player_deferred` | `api/_lib/heartbound-pulse.js:425-444` — the PENDING path |
| `heart_pulse_player_processed` | `api/_lib/heartbound-pulse.js:553-557` — after the grant gate is won |
| `resonance_score_changed` | `api/_lib/heartbound-pulse.js:558-564` — only when the floored score moved |
| `resonance_tier_up` / `_down` | `api/_lib/heartbound-pulse.js:565-571` |
| `heartbound_activated` | `api/_lib/heartbound-state.js:269-277` |
| `skr_verification_success` | `api/_lib/heartbound-state.js:364-386` (seam only — see §4.2) |
| `skr_verification_failed` | `api/_lib/heartbound-state.js:454-462` (seam only — see §4.2) |

### Acceptance, item by item
| # | Verdict |
|---|---|
| 1. All fourteen land in `analytics_events`, proven by a row query | ⛔ **NOT MET.** See §3. |
| 2. No event emitted from both sides | ✅ `SERVER_EVENTS` / `CLIENT_EVENTS` are disjoint and complete over the fourteen, asserted by two tests. And in the shipped tree only `status.js` emits the two verification names, because every new seam defaults to a no-op. |
| 3. No wallet address in `properties` | ✅ `assertNoIdentityLeak` walks the finished blob (nested + arrays) and **refuses the event**; three tests, including one that proves an RPC error message echoing the wallet cannot ride into a deferred row. |
| 4. Amounts bucketed, edges in config | ✅ `heartbound-telemetry-config.json`; boundary tests at every edge; a test asserts the exact figures never appear in the properties JSON. |
| 5. Not caught by the retention sweep | ✅ **Two** swept names, not one: `web_trace` (`api/admin/cleanup.js:83`) and `api_auth_reject` (`:121`), both at `RETENTION_DAYS = 7` (`:35`) — read at source today. No Heartbound name matches either. |
| 6. `.cs` gate | n/a — no `.cs` touched. |
| 7. Status flipped + RESULT written | ✅ both paths reported below. |

---

## 3. ⛔ WHAT WAS NOT DONE, AND WHY — read this before believing the ticket

**Acceptance 1 is NOT met. No Heartbound row reaches `analytics_events` yet.** Every seam defaults to a
no-op (that is what the brief specified), and `api/cron/heart-pulse.js` — the only live caller of the
pulse job — **is outside this lane's file list**, so nothing passes an emitter. There is also no
database reachable from this worktree, so no row query could have been run even if it were wired. Both
halves are stated as unproven rather than ticked.

**The one line that closes it**, for whoever owns the cron shell (`api/cron/heart-pulse.js:86`):
```js
const { createAnalyticsEmitter } = require('../_lib/heartbound-telemetry.js');
const summary = await runHeartPulseDetector({ sql, now: Date.now(), emit: createAnalyticsEmitter({ sql }) });
```
Then acceptance 1 is a query:
```sql
SELECT event_name, COUNT(*) FROM analytics_events
WHERE event_name LIKE 'heart%' OR event_name LIKE 'resonance_%' OR event_name LIKE 'echo_event_%'
GROUP BY 1 ORDER BY 1;
```

**Four of the fourteen are emitted by nobody in this lane**, and `EVENT_OWNERS` names the seam each
belongs to so the next lane does not invent a fifteenth name:
- `heartbound_detected` → `api/heartbound/status.js` (the chain read lives at the endpoint).
- `echo_event_generated` / `echo_event_expired` → HEART-005 (WO-1678); the Echo Event table does not exist.
- `echo_event_claimed`, `heartbound_screen_opened` → the client seams (HEART-005 / HEART-008), and
  `Assets/**` is not this lane's.

**D4 is NOT ticked.** `ANALYTICS_EXCLUDED_PLAYER_IDS` is an environment variable
(`api/admin/stats.js:249`) — **its contents are unreadable from here**, so "the owner's staking wallet
is excluded" is a claim this lane cannot make. It is a one-command check for someone with the Vercel
env, and it must happen before the first Heartbound tier chart is read, or the owner's ~1M SKR position
is a visible outlier in every one of them.

---

## 4. Three places where I departed from the WO / brief — each needs the lead's nod

### 4.1 `resonance_tier_up` / `_down` are SERVER events (WO-1684 D1 lists them as client)
D1 says "tier up/down as *seen*" on the client. But the client **never computes a tier** —
`heartbound-resonance.js:19-26` records product rule 6 (backend authoritative) and WO-1676 §5 forbids
the curve in C# — and a pulse is a **daily cron** (Q-CADENCE), so a transition routinely happens while
the app is closed. A client emit would be a repaint, not an observation, and would miss every offline
transition. Emitted server-side, inside the transition. If the lead wants the client's *view* counted
too, that is a different question ("did the player SEE their tier change"), and it needs its own name —
not a second emitter on this one.

### 4.2 `skr_verification_success` / `_failed` are seams that nothing wires
**`api/heartbound/status.js:258` and `:350` already emit both names in production today.** Wiring the
new `heartbound-state.js` seams for a caller that also goes through that endpoint would double-count
the funnel D1 exists to protect. So the seams exist (as briefed) and default to no-op, with the wiring
rule written on the seam itself: wire them only for a caller that does **not** pass through
`/api/heartbound/status`. `EVENT_OWNERS` records `emittedByThisLane: false` for both.

### 4.3 A new config file, outside the brief's file list
`api/_lib/heartbound-telemetry-config.json`. WO-1684 acceptance 4 requires the bucket edges in config;
Q-CONFIG's ruled home is the Command Center `serverOnly` rail, and `tunable-manifest.js` is explicitly
forbidden to this lane. So this is the interim single home, exactly as
`heartbound-resonance-config.json` describes itself. It ships (`.vercelignore:17` is `!/api`, read at
source today).

---

## 5. Two findings raised, not fixed

### 5.1 ⚠ `last_actual_stake` has TWO UNITS at HEAD, 1e6 apart — a cross-lane contract bug
`heartbound-state.js` coerces stake through `toRawAmountText`, whose ceiling is u128 and whose reason
for `NUMERIC(39,0)` is **chain base units**. But `heartbound-pulse.js:514-525` hands
`persistPlayerState` `nextState.actualSkr`, which `heartbound-resonance.js` works in as **whole SKR**.
Same column, two units.

⛔ **AND IT IS WORSE THAN A UNIT MISMATCH — THE FIRST WIRING OF THE TWO WILL THROW.** The ramp
produces FRACTIONS after the second pulse, and `heartbound-pulse.js:481-487` passes
`String(effectiveStake)` onward. Measured in this worktree today:

```
$ node -e "... r.activate(4000); r.applyPulse(...) x3 ..."
pulse 1 effectiveSkr= 1750        String()= 1750
pulse 2 effectiveSkr= 2312.5      String()= 2312.5
pulse 3 effectiveSkr= 2734.375    String()= 2734.375
st.toRawAmountText('2734.375') -> THROWS: TypeError effectiveResonatingStake:
    expected a non-negative integer string, got "2734.375"
```

So the moment `persistPlayerState` is wired to `recordVerifiedStake`, the cron **500s on the second
pulse for every staked player** — not a mis-bucketed chart, a dead job. Route it before the wiring,
not after.

This is why the `skr_verification_success` event **deliberately carries no stake bucket** (pinned by a
test so nobody "completes" it): a bucket taken over both would file every base-unit row as `1m+` and
report a realm of whales — the instrument manufacturing exactly the signal WO-1682's ceiling exists to
test. **Not this lane's to settle** — it is a WO-1675 ↔ WO-1677 contract. Recommend the lead route it.

### 5.2 A second bucket ladder already exists
`api/heartbound/status.js:400-408` (`bucketOf`) shipped its own private copy of this ladder. The config
here uses **identical labels** so one column never carries two vocabularies, but the copy is real
duplicated state. The follow-up that closes it is re-pointing `bucketOf` at
`telemetry.stakeBucket` — one function body, out of this lane's file list, deliberately not done.

**Unit difference to preserve when someone does it:** `status.js` buckets a u128 in base units;
`stakeBucket()` buckets whole SKR (the unit the resonance module and the pulse loop hold). Labels same,
input unit different.

---

## 6. Question 9 — the whale evidence WO-1682 needs, and what it does *not* measure

`aggregateWhaleRatio()` reports `advantageConcentration = scoreRatio / stakeRatio` over live samples;
`< 1` means the top stake bucket's score advantage is smaller than its stake advantage — the anti-whale
curve holding. Pinned against the **real** resonance module (spec `:313`, "500,000 SKR does NOT provide
500 times the power of 1,000 SKR"): a >100x stake ratio produces a <5x score ratio and
`advantageConcentration < 0.05`, `sublinear === true`. A population spanning one bucket reports `null`,
not `false` — a non-measurement must not read as evidence against the curve.

⚠ **That is the SCORE curve, not gameplay advantage.** Q-METER (owner-ruled 2026-09-10) defines the
ceiling as the **sum of passive tier percentage boosts to resource yield**, and those live in
`heartbound-tiers.js`, in flight in another lane and untouchable here. `tierBenefit` is therefore an
injected function defaulting to `null`, and `benefitConcentration` stays `null` until it is passed. **No
benefit number is hardcoded** — a guessed one would be a second copy of a table that then drifts.

Exact per-player numbers for question 9 come from `player_pulse_grant` (`heartbound-pulse.js:481-487`
already persists effective stake, score and tier per player per pulse), **not** from
`analytics_events` — which is precisely why the analytics row is allowed to stay bucketed.

---

## 7. Paths

- WO: `WorkOrders/WORK_ORDER_1684_heart_011_telemetry_and_balance_analytics.md` (Status flipped)
- RESULT: `WorkOrders/WORK_ORDER_1684_heart_011_telemetry_and_balance_analytics.RESULT.md` (this file)

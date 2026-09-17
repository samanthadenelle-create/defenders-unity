# WO-1843 — Command center analytics robustness pass (DAU/MAU, ARPU, crash-free rate, version slicing)

**Status: READY FOR LEAD REVIEW**

## Implementation notes (lane, 2026-09-17) — read before reviewing

All four additions landed in `api/admin/stats.js` only. **No migration** (acceptance criterion 4
confirmed: every addition is a query over existing tables; a test pins that no `1843` migration file
exists). New oracle: `test/admin.analytics-robustness.test.js`, 30 cases, all passing, and
**red-proven** — three deliberate mutations on a scratch copy (clamping the >100% rate, dividing
ARPDAU by active players instead of user-days, dropping the insufficient-payer caveat) fail 4 cases.

New views: `?view=active`, `?view=monetization`, `?view=stability`. Slicing: `&app_version=` /
`&platform=` on `retention`, `funnel`, `purchases` (plus `active` and `stability`).

**⛔ FINDING THAT CHANGES SCOPE ITEM 4 — LOCALE IS NOT COLLECTED AT ALL.** Measured read-only against
the live DB 2026-09-17: `SELECT COUNT(*) FROM analytics_events WHERE properties ? 'locale'` over 60
days returns **0**. `EventTracker.cs` emits `{ platform, appVersion, unityVersion }` on `session_start`
and nothing anywhere carries a language. A locale filter would therefore match nothing and render as
"no players in that locale" — a fabricated finding. So **`?locale=` is REFUSED with 400 naming the
reason**, and every sliceable response carries the gap in `slice.data_gaps`. Closing it needs one
client emit (`Application.systemLanguage` on `session_start`) — new instrumentation, so a separate
ticket for the owner to rule on, not this lane.

**Two other honesty findings, both surfaced on the responses rather than smoothed over:**
- **Devnet is not revenue.** All-time entitlements split mainnet-beta $5.98 (2 rows) / Pi $4.99 (1
  row, `verified`) / devnet $0 (1 row, no anchor). `usd_real` excludes `network='devnet'` and every
  ratio is computed on it, so test money can never be reported as income.
- **The stability denominator is genuinely incomplete.** Over 30 days, 211 player-days carry a
  `session_start` but **228** carry a `kind='error'` break — a break can be recorded for a player whose
  `session_start` never landed. The view returns **108.1% with `denominator_incomplete: true`** rather
  than clamping to a tidy 100%; clamping would have hidden a real data gap. Also: there is **no crash
  reporter in this game**, so a true crash-free rate is unmeasurable and is **not claimed** — the
  headline is an exception-free player-day rate, named a PROXY.

## Owner ask

> "Can we query a audit of what would be useful and what standard metrics are gathered and
> retained at most professional levels and try to add that robustness to our command center?"

An audit was run comparing this project's current analytics coverage against a standard
professional F2P mobile analytics stack. Full findings are recorded here; this WO implements the
four gaps that are query-only against data already being collected (no new client instrumentation
needed). A fifth gap, true session length, is already covered by the separate WO-1842 (needs a new
client signal). LTV/churn were explicitly excluded — not worth building against a pre-revenue
cohort yet; revisit once real ARPU/payer data exists.

## Confirmed at source — what already exists (do not re-derive, do not duplicate)

- `analytics_events` table: `player_id`, `event_name`, JSONB `properties`, server timestamp,
  indexed by player and by event name/time.
- Identity tiers (session/guest/guest-body/unverified) with wallet-bind reattribution — WO-1791.
- `view=retention` — D1/D7 cohorts with cohort size + `low_n` flag.
- `view=funnel` (both `api/admin/stats.js` and `api/admin/db.js`) — raid onboarding funnel,
  tutorial completion accurate post-WO-1794.
- `view=purchases` — quotes, verification, fulfillment status, per-day revenue sum.
- `view=ads` — eCPM, per-network/day/placement, impressions (WO-1796).
- Hero level median/max/distribution, pulled from save data (`api/admin/stats.js:1718-1978`).
- `view=economy` — sink/source views.
- Admin ops console — toggles, promo history, player lookup, bug reports (with `app_version`/
  `platform` per report, but ONLY on bug reports — not on the main metrics views, which is gap #4
  below).

## Scope — four additions, all against `api/admin/stats.js` and/or `api/admin/db.js`

Read both files fully first to find the right home for each (follow whichever file already owns
the closest existing pattern — e.g. hero-level's distribution shape, retention's cohort-size/low_n
caveat shape — rather than inventing a new response shape per metric).

1. **DAU/WAU/MAU + stickiness (WAU/MAU ratio).** No view currently computes distinct active
   players over rolling 1/7/30-day windows — there is only one `active_players` column buried
   inside an unrelated query. Add a dedicated view computing DAU, WAU, MAU as distinct
   `player_id` counts over trailing 1/7/30-day windows from `session_start` events, plus the
   stickiness ratio (DAU/MAU is the more commonly reported one; report whichever combination is
   clearest, labeled explicitly).
2. **Payer rate / ARPU / ARPDAU / ARPPU.** Revenue is currently summed by day with no denominator.
   Add: payer rate (paying players ÷ active players over a window), ARPU (revenue ÷ all active
   players), ARPDAU (revenue ÷ daily active players), ARPPU (revenue ÷ paying players only). Each
   number is close to zero or undefined at this project's current pre-revenue stage — report that
   plainly (e.g. an explicit "insufficient payer volume" caveat) rather than a misleadingly precise
   decimal from a tiny sample.
3. **Crash-free rate / error rate as a computed percentage.** `view=events` already surfaces raw
   error counts by kind. Add a computed rate: errors (or sessions containing at least one error) ÷
   total sessions over a window, so there's one trustworthy stability number to watch day over day
   instead of only a raw count that never says whether it's getting better or worse relative to
   traffic.
4. **app_version / platform / locale breakdown on retention, funnel, and revenue views.** These
   fields exist for bug reports only. Add optional slicing (e.g. a query parameter) to the
   retention, funnel, and purchases/revenue views so results can be filtered or grouped by build
   version, platform, and/or locale — this project has shipped and fixed many build-specific
   defects in a single session, and there is currently no way to tell whether a metric's dip or
   improvement correlates with a specific build.

## What NOT to do

- Do not build LTV or churn modeling — explicitly deferred per the audit.
- Do not touch WO-1842's session-duration work (separate lane, separate ticket).
- Do not fabricate a metric from insufficient data — every new view must carry the same honesty
  discipline already established elsewhere in this file (a `low_n`/insufficient-data caveat rather
  than a precise-looking number from a handful of rows).

## Acceptance criteria

- [ ] `node --test test/*.test.js` before/after counts reported; new tests for each of the four
  additions.
- [ ] Each new view proven against a real (or realistic seeded) read of the live database,
  read-only — this project's standing practice for admin views, not just unit tests in isolation.
- [ ] Every number that could mislead from a small sample carries an explicit low-confidence flag,
  matching the existing `low_n` convention.
- [ ] No schema migration needed unless something here genuinely requires new persisted state (it
  shouldn't — confirm before adding one; these are queries over existing tables).

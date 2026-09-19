# Funnel 72-hour watch (WO-1879 / 1880 / 1881)

Run daily for 3 days after the **API deploy** (A) and the **client build** (B+C) are both live. Paste numbers into the table at the bottom. Do not declare founding_walk fixed on self-play.

Baseline measured **2026-09-19 02:20–03:20 UTC** against Neon (this file is not the live dashboard).

## What each ticket actually moves

| Card | Query behind it | Will A move it? | Live 2026-09-19 |
|---|---|---|---|
| Active 24h / 7d / 30d (Pulse) | `session_start` DISTINCT player_id | **No** — already excluded traces | 50 / 141 / 200 |
| Anonymous sessions | `player_id = 'anonymous'` + session_start | **No** (keep 0) | 0 |
| New players / first-seen | first event of any kind | **Yes** | 30d 770 → **324** with the filter |
| Players this window (`total_ids_seen`) | DISTINCT player_id all events | **Yes** | 30d 781 → **335** |
| last-act `web_trace` row | last event per id | **Yes** (trace ids vanish from last-act) | 561 phantom ids |

If Pulse numbers drop after A deploys, the predicate is on the wrong query. If New Players / total_ids do **not** drop, A did not ship to the API that the console hits.

A is a **filter** (`event_name <> 'web_trace'`). Rows stay. 7d web_trace count at measure: **1710** rows still in the table.

`LIKE 'X-Trace-%'` is **wrong** — stored `player_id` is the session UUID value, not the header name.

## Four queries

Use `psql "$DATABASE_URL"` or the admin key against `GET /api/admin/stats?view=funnel&days=30` and `?view=overview&days=30`.

### 1. Real arrivals (non-trace)

```sql
WITH firsts AS (
  SELECT player_id, MIN(received_at) AS first_seen
  FROM analytics_events
  WHERE player_id <> 'anonymous' AND event_name <> 'web_trace'
  GROUP BY 1
)
SELECT date_trunc('day', first_seen)::date AS day,
       COUNT(*) AS new_ids
FROM firsts
WHERE first_seen > NOW() - INTERVAL '4 days'
GROUP BY 1
ORDER BY 1;
```

### 2. Tutorial started + founding_walk completion %

```sql
SELECT
  COUNT(DISTINCT player_id) FILTER (WHERE event_name = 'tutorial_started') AS started,
  COUNT(DISTINCT player_id) FILTER (
    WHERE event_name = 'tutorial_step_enter' AND properties->>'stepId' = 'founding_walk') AS walk_entered,
  COUNT(DISTINCT player_id) FILTER (
    WHERE event_name = 'tutorial_step_complete' AND properties->>'stepId' = 'founding_walk') AS walk_completed,
  COUNT(DISTINCT player_id) FILTER (
    WHERE event_name = 'tutorial_step_drop' AND properties->>'stepId' = 'founding_walk') AS walk_dropped
FROM analytics_events
WHERE received_at > NOW() - INTERVAL '24 hours'
  AND player_id <> 'anonymous';
```

Completion % = walk_completed / walk_entered. Baseline 30d: **33 / 100 = 33%**. Target **>80%**. Drops at idle 120.0s is the watchdog. `walkValidInput: true` on complete (after B ships) means the 8m rescue, not the gate latch.

Or `GET /api/admin/stats?view=funnel&days=1` and read the `founding_walk` row.

### 3. Wave 1 started vs completed (needs client with WO-1880)

```sql
SELECT
  COUNT(DISTINCT player_id) FILTER (WHERE event_name = 'wave_started') AS starters,
  COUNT(DISTINCT player_id) FILTER (WHERE event_name = 'wave_completed') AS completers
FROM analytics_events
WHERE received_at > NOW() - INTERVAL '24 hours'
  AND player_id <> 'anonymous'
  AND event_name <> 'web_trace'
  AND properties->>'waveId' = '1';
```

`->>` textifies JSON numbers, so `1` and `"1"` both match `'1'`. If starters < completers, double-emit or missing starts — inspect raw:

```sql
SELECT event_name, properties->>'waveId' AS wave, pg_typeof(properties->'waveId') AS jsonb_type, received_at, player_id
FROM analytics_events
WHERE event_name IN ('wave_started','wave_completed')
ORDER BY received_at DESC
LIMIT 10;
```

Funnel view: `waves.started_players` / `waves.completed_players` on `?view=funnel`. Zero started until a **shipped** client has been in the window.

### 4. Pulse sanity (must NOT collapse after A)

```sql
SELECT
  COUNT(DISTINCT player_id) FILTER (WHERE received_at > NOW() - INTERVAL '1 day') AS dau,
  COUNT(DISTINCT player_id) FILTER (WHERE received_at > NOW() - INTERVAL '7 days') AS wau,
  COUNT(DISTINCT player_id) FILTER (WHERE received_at > NOW() - INTERVAL '30 days') AS mau
FROM analytics_events
WHERE event_name = 'session_start'
  AND player_id <> 'anonymous'
  AND received_at > NOW() - INTERVAL '30 days';
```

Expect ~50 / ~141 / ~200 (will drift with real traffic). A drop to 8–20 DAU means someone put the trace filter on `session_start` incorrectly, or the API is pointed at an empty env.

## Paste table

| Metric | 2026-09-19 baseline | Day 1 | Day 2 | Day 3 |
|---|---|---|---|---|
| Real arrivals 24h (q1) | 59 new_1d after filter (65 before) |  |  |  |
| Tutorial started 24h |  |  |  |  |
| founding_walk entered / completed / % | 100 / 33 / **33%** (30d) |  |  |  |
| Wave 1 started | n/a until WO-1880 client |  |  |  |
| Wave 1 completed | 65 players (30d) |  |  |  |
| Wave-1 start→complete | n/a |  |  |  |
| Pulse DAU / WAU / MAU | 50 / 141 / 200 |  |  |  |

## Ship gates before the clock starts

1. **A** is `api/admin/stats.js` — live console only updates after that function is on the `defenders-of-the-realm-v2` deployment. Local commit is not the dashboard.
2. **B+C** need a Unity client in players’ hands. Self-play does not count for walk %.
3. Do not delete `web_trace` rows. `GET /api/admin/db?view=traces` must still work.

## If walk % does not climb

Re-run idle distribution: drops at exactly `120.0` = watchdog still winning. Check `tutorial_step_complete` properties for `walkValidInput`. If completes rise but only on your wallet, ignore. If still 33% after 72h of real guests, the hang is not “missed the latch after walking 8m” — go back to probe lines (`walk-probe STALLED`, no hero, no anchor) and guest-auth only.

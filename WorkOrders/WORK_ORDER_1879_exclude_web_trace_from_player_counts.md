# WO-1879 — Exclude web_trace session ids from every player-counting query

**Status:** IMPLEMENTED 2026-09-19 — `TRACE_EVENT` predicate on overview totals / new-players / retention firsts / last-act / identity coverage / session-length / identity_rule. `node --check` clean. Felt-verify on Live Player Pulse.

**Owner, verbatim:** "Ticket A — web_trace must be excluded from all player-counting queries" / "Mint and apply Ticket A today."

## Problem

`api/trace.js` writes `event_name='web_trace'` with `player_id = X-Trace-Session` (a WebGL log-batch session UUID), not a human. Measured 2026-09-19 30d: **446** such ids, **0** overlap with `session_start` / `tutorial_step_enter` / qualifying play. Last-act top row is 561 "players" that are session UUIDs. Inflates New Players, totals, retention first-seen, early-exit last-act.

`NOT LIKE 'X-Trace-%'` does **not** work — the stored id is the session header **value**, not the header name. The predicate is `event_name <> 'web_trace'` (plus existing `player_id <> 'anonymous'` / excluded ids).

## Fix

One rule, used by every DISTINCT-player query that scans mixed events:

`player_id <> 'anonymous' AND event_name <> 'web_trace'`

Do **not** rewrite historical `api/trace.js` rows this ticket. Optional later: write traces as `anonymous` or a dedicated table.

Surfaces that must change: `?view=overview` `total_ids_seen` + `new_players_per_day`; `?view=retention` firsts CTE; `?view=command` early-exit last-act; identity_rule string on command; console "New players" note. DAU from `session_start` is already clean — leave it, document why.

## Acceptance

Live Player Pulse / Daily Active / New Players / Played-Once-and-Left exclude trace session ids. identity_rule documents the predicate. Node tests if a stats view test exists.

## Not in scope

Tutorial walk (WO-1881). wave_started (WO-1880). Changing WebTrace client.

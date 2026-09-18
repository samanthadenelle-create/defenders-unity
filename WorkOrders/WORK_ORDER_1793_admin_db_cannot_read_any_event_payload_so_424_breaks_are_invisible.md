# WORK ORDER 1793 — No admin view can read an event's PAYLOAD, so today's 424 `playtest_break` rows were unreadable without direct DB credentials

**Status:** DONE - committed cb05dbcdb, gated (COMPILE_GATE_OK/REGRESSION_OK or node --test as applicable). PRIOR: READY FOR LEAD REVIEW
**Minted:** 2026-09-16 (number PRE-ASSIGNED by the lead from the 1791-1795 block; this lane did NOT touch `CLI_LANES_WO_NUMBERS.md`)
**Silo:** `api/admin/db.js` ONLY (one new read-only view). No client code, no schema change, no writes.
**Priority:** P1 for operations — it is the tooling gap that made this triage need `DATABASE_URL`.
**Lane disjointness:** file-disjoint from WO-1791, 1792, 1794, 1795.

---

## 1. THE GAP, READ AT SOURCE (2026-09-16)

`api/admin/db.js` exposes `overview | players | metrics | traces | bugreports | bugreport |
authrejects | purchases`. Against the question "what ARE the 424 breaks?":

- `metrics` (`db.js:164-204`) returns **counts only** — `event_name` x day, no properties.
- `traces` (`db.js:206-280`) is hardcoded `WHERE event_name = 'web_trace'`. **WebTrace only POSTs
  under `UNITY_WEBGL`**, and today's players are all Android: `distinct_trace_sessions = 0` for
  2026-09-16 in the very same `metrics` response. So the one view that returns log lines returns
  nothing for the platform the game ships on.
- `authrejects` (`db.js:466-558`) reads exactly three event names
  (`api_auth_reject`, `auth_failed`, `save_reset_refused`).
- Nothing reads `playtest_break`, `save_reset_accepted`, `tutorial_*`, `founding_path_selected`,
  `raid_funnel_*`, `rewarded_ad_*`, or per-player event sets.

**Consequence, measured:** 424 break rows and 7 accepted save resets landed on 2026-09-16 and
**no view in the product could name one of them.** This triage answered it by connecting to Neon
directly with `DATABASE_URL` from `.env.local`, SELECT-only — which is a deviation the next seat
should not have to repeat, and which the owner cannot do from a phone at all.

This is the same failure `authrejects` already has written on its own face: WO-1742 §3 read "zero
refusals on /api/game/save in seven days" off a view that did not contain the event name, and
WO-1745 fixed it by widening the view. Same class, one table over.

## 2. THE VIEW TO ADD — `view=events`

Read-only, parameterized, hard-limited, in the same house style as every other view in the file.

```
GET /api/admin/db?view=events&name=<event_name>[&since_hours=N][&limit=N][&player=<id>][&group=<mode>]
```

| param | meaning | bounds |
|---|---|---|
| `name` | REQUIRED. one `event_name`, exact match, parameterized | — |
| `since_hours` | window | `clampLimit(raw, 24, 168)` |
| `limit` | rows | `clampLimit(raw, 50, 200)` |
| `player` | one player id | — |
| `group` | `rows` (default), `message`, `kind`, `player` | whitelist; anything else = `rows` |

- `group=kind` → `properties->>'kind'`, count, distinct players. (The split that matters: of today's
  424, **330 are `kind='error'`, 74 are `kind='scene_loaded'`, 11 `note`, 8 `possible_softlock`,
  1 `idle`** — i.e. a fifth of the "424 breaks" are scene-load bookkeeping, not breaks.)
- `group=message` → the coarse prefix, NOT the raw message:
  `CASE WHEN message LIKE '[Flow:%' THEN split_part(message,']',1)||']' ELSE split_part(message,' ',1) END`.
  Grouping on the full message fragments uselessly — `[Flow:RaidArt]` alone split into eleven
  6-hit rows today, while the prefix collapsed them to one row of 156.
- `group=player` → per-id counts, plus that id's `session_start.appVersion` set for the window,
  because **`BreakRecord` carries no build** (see §3).
- `rows` → `received_at, player_id, kind, scene, left(message, 400), left(stack, 400)`. Truncate:
  a stack is unbounded and a poll must never return one in full.

Add a **second helper for the funnel**, since it is the same missing capability:

```
GET /api/admin/db?view=funnel[&since_hours=N]
```
returning, per distinct `player_id` in the window, `first_seen`, event count, and
`array_agg(DISTINCT event_name)` — capped at 200 ids. That one query is the whole per-player funnel
this triage had to build by hand.

## 3. ONE FACT THE VIEW MUST STATE, NOT IMPLY

`BreakCaptureHarness.Record` (`Assets/_Modules/Core/Diagnostics/BreakCaptureHarness.cs:1129-1153`)
builds `BreakRecord { kind, message, stack, scene, t, utc }` — **there is no app version in a break
payload.** Only `session_start` carries `appVersion` (`EventTracker.cs:143-148`). So any
build attribution is a per-player JOIN and is AMBIGUOUS for a player who booted two builds in the
window. Put that in the response `legend`, the way the `purchases` view already explains `verified`
vs `fulfilled` — a number whose ambiguity is not printed will be read as certain.

## 4. ACCEPTANCE CRITERIA

- [ ] `view=events&name=playtest_break&group=kind&since_hours=24` reproduces the five-row kind split
      in §2 against production.
- [ ] `view=events&name=save_reset_accepted&since_hours=24` returns the `from`/`to`/`ref`/`mode`
      properties (7 rows for 2026-09-16).
- [ ] `view=funnel&since_hours=24` returns one row per player id with its event-name set.
- [ ] Every new query is a `SELECT` with a hard `LIMIT`, every user input parameterized through the
      `sql` tagged template, `name` and `group` never interpolated as SQL.
- [ ] The unknown-view error string at the bottom of the file lists the new views.
- [ ] No schema change, no write path, no new env var.

## 4B. IMPLEMENTED 2026-09-17 — evidence, measured not asserted

Files: `api/admin/db.js` (two new views + the header doc block + the unknown-view hint),
`test/admin.events.view.test.js` (new oracle, 13 cases). No schema change, no migration, no new
env var, no client code, no write path — the existing SELECT-only lints in
`test/command-center.test.js` and `test/tunables-manifest.test.js` still pass.

**AC verified by invoking the real handler against PRODUCTION read-only** (`.env.local`
`DATABASE_URL`, `since_hours=48`, so the window spans 09-15 → 09-17 and the counts are LARGER than
the ticket's 09-16 day figures — the SHAPE is what reproduces, and the ticket's day numbers cannot
be isolated by an hours-window alone):

- AC1 `view=events&name=playtest_break&group=kind` → 200, `window_total.hits=5316` / 97 ids, and the
  split reproduces: `error 4680`, `scene_loaded 357`, `note 235`, `possible_softlock 20`, `idle 15`
  — **plus a sixth kind the ticket did not list, `flagged 6`**, which exists in the wider window.
- AC2 `view=events&name=save_reset_accepted` → 200, 30 rows in 48 h, each carrying
  `props {to, ref, from, mode}` (e.g. `{"to":1789668298,"ref":"692cd6c0","from":null,"mode":"guest"}`).
- AC3 `view=funnel&since_hours=48` → 200, 118 rows, one per id, e.g. 24 events / 12 distinct names
  including `founding_path_selected`, `raid_funnel_*`, `tutorial_step_drop`.
- `group=message` collapses as designed: `[Flow:RaidArt] 3116`, `[Flow:TripoMatFix] 532`,
  `RemoteProviderException 228` (R2 `ConnectionError` — a separate finding, not this ticket).
- `group=player` returns the appVersion SET, and the top row proves §3's ambiguity is real:
  `player_id='unverified'` carries **eight** app versions in one window.
- `group=DROP TABLE` → 200 as `rows`, with `requested_group:"drop table"` echoed. Missing `name` →
  400 and **zero queries issued**. Unknown view → the hint now lists all ten views.

**Test run:** controlled comparison on the same tree, `node --test --test-reporter=tap test/*.test.js`
— before `793 tests / 788 pass / 4 fail`, after `806 tests / 801 pass / 4 fail`. The 4 failures are
identical in both runs and pre-existing (served-page 7-bit-ASCII lints in the command-center console,
a Heartbound tier-name lint, one TODO) — none touch `api/admin`. The new file goes RED 11/13 with
`api/admin/db.js` reverted.

## 5. WHAT NOT TO TOUCH

The existing eight views' SQL. The `traces` paging contract (`order=asc`/`offset` exist for a
reason written at `db.js:212-222`). The status-code constraint: this project's API answers
200 | 400 | 500 only.

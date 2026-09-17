# WO-1842 — Session-duration playtime buckets

**Status: READY FOR LEAD REVIEW**

## Implemented 2026-09-17 — lane hand-back

**Client (the part that had to exist first).**
`Assets/_Modules/Core/Analytics/EventTracker.cs` now emits TWO signals, not one, because they
cover different death modes and neither covers both:

- `session_end` on `OnApplicationPause(true)` and `OnApplicationQuit`. Exact when it fires.
  ⛔ Enqueued **BEFORE** `SaveQueueToPrefs()` — the reverse order writes the event to memory only
  and loses it to the very OS kill it was written for. No flush is attempted in that callback:
  Android stops the player loop at pause, so delivery is deliberately deferred to the next launch
  via the existing WO3 PlayerPrefs queue, and each event carries its own `clientTs`.
- `session_heartbeat` every 60s of foreground time. The **floor** for a crash or an OS kill that
  delivers no callback.

Both carry the **cumulative** `elapsedSeconds` for one `sessionId`, so the backend takes `MAX` and
needs no ordering rule; a duplicate or late arrival cannot corrupt a duration.

Three decisions worth knowing:
- **Foreground time, on the wall clock.** `Time.realtimeSinceStartup` keeps advancing while an
  Android app is backgrounded, so it would bill a phone in a pocket as play.
- **Boot-resume guard.** Android delivers `OnApplicationPause(false)` during boot with no pause
  before it (documented in-repo at `Village/Harvest/OfflineHarvestService.cs:199`); unguarded, every
  session would open with a zero-second orphan.
- **Heartbeats COALESCE in the queue.** Not an optimisation — a data-loss fix. The queue caps at 200
  and drops the oldest, so an offline player would have appended 200 heartbeats and evicted real
  purchase/wave events. Only the newest cumulative figure is informative, so it replaces in place.
- A post-gap resume mints a new `sessionId` but deliberately does **not** re-emit `session_start`:
  that row count is the "app opens" figure on `?view=overview` and re-emitting would silently
  redefine an existing metric.

**Backend.** New `?view=playtime` on `api/admin/stats.js` — median-first, mean, p90, the owner's
four buckets plus an added-and-declared "Under 1 minute" bounce band, `low_n`, and coverage that
sets measured sessions against `session_start` in the window. Edges live once in `PLAYTIME_BUCKETS`.

**Two contamination fixes that were NOT in the original scope and were found by reading:**
- `session_length_estimate` scans every row with no `event_name` filter — an unfiltered 60s
  heartbeat would have silently converted "span between a player's acts" into foreground time while
  keeping the old label. Now excluded.
- `early_exit_step` ("the LAST thing each now-quiet player did") excluded only `session_start`, so
  `session_end` would have become every departing player's last act and erased the view. Now excluded.
- Both names added to `NOT_PLAY_EVENTS` — a heartbeat fires because the app is open, not because
  anyone played.

**§15 canon:** the two statements this change falsified (`how_sessions_end: 'THEY DO NOT...'` and the
comment block above that query) are corrected in the same change, and the oracle that **pinned** the
false sentence is re-pointed to the new contract rather than deleted.

**⛔ THERE IS NO BUCKET DISTRIBUTION YET, AND THERE CANNOT BE.** Every session predating this client
change emitted no end signal and is permanently unmeasurable — no backfill exists. The view answers
`state: 'empty'` with `unmeasured_sessions` printed. A meaningful distribution appears only once a
build carrying this has been in players' hands for a full window. Any number claimed before then is
fabricated, and `gaps[]` says so on the response.

**Not done, deliberately:** no card was added to the `api/admin/console.js` HTML page (the ticket asked
for a `view=`, and `console.js` is a conflict surface with the concurrent WO-1841 lane). Lead's call.

**Sweep:** every `FROM analytics_events` statement in `api/admin/stats.js` was checked for an
`event_name` filter, not just the two found by reading. `view=retention`'s D1/D7/D30 test filters on
`session_start` inside `sessions_by_day`, so the **headline retention numbers are unaffected** — read
at source, not assumed. `view=overview`'s cohort `MIN(received_at)` is likewise safe (a heartbeat can
never precede that player's boot `session_start`). The remaining effects are definitional, not wrong:
`overview.total_events` / `perDay.events` / the identity-coverage ratio now include heartbeats, and
`view=players` ranks by raw event count — that last one got an `ordering_note` on the response
because the question it answers has quietly changed.

**Capture criterion (for the lead — grep these on a fresh log):**
- boot: `[Flow:Analytics] WO-1842 session window OPEN (boot) sessionId=`
- pause/quit: `[Flow:Analytics] WO-1842 session_end QUEUED reason=pause` / `reason=quit`
- first beat: `[Flow:Analytics] WO-1842 session_heartbeat is LIVE`

⚠ On Android a pause followed by a quit enqueues **two** `session_end` rows (`reason=pause`, then
`reason=quit`). Expected and harmless: the backend takes `MAX` of a cumulative figure and counts the
session once.

**Checks:** `python tools/gate_brace.py` clean (`bad=0 of 1`), 0 NUL bytes in all four files,
`node --check` clean on all three JS files. `node --test test/*.test.js`: **844 / 839 pass / 4 fail
before → 861 / 859 pass / 1 fail after**. The **17** new tests are in
`test/admin.playtime.buckets.test.js`, **proven RED at 1/17 against the pre-change backend** (the one
that passes in both is the auth gate, and it must). The two remaining failures are pre-existing
Heartbound cases that reference neither touched file. **No Unity process was run** — the lead gates
the combined tree.

⚠ `api/admin/stats.js` was edited **concurrently by the WO-1841 lane** during this work (two
modified-on-disk notices). All edits applied cleanly and the full suite is green including WO-1841's
three tests, but the lead should diff by region rather than by file.

## Owner ask

Playtime buckets: 1-5 minutes, 5-30 minutes (owner said "10 minutes" loosely — treat these bucket
edges as a first pass she can adjust, not a hard spec), 30-60 minutes, and 60+ minutes.

## Confirmed at source — this is NOT a pure database query, contrary to the owner's assumption

`api/admin/stats.js:1887-1888`, the codebase's own words: *"THEY DO NOT. The game emits
session_start on boot (EventTracker.cs) and there is NO session_end anywhere in the client."*
`EventTracker.cs` fires `session_start` once at boot and nothing marks when a session ends —
`OnApplicationPause`/`OnApplicationQuit` are NOT wired to emit anything. Without an end timestamp,
no query over existing rows can compute a session's duration. **New client-side instrumentation is
required before any bucketing view can exist.**

## Scope

1. Add a session-end (or periodic heartbeat) signal in `Assets/_Modules/Core/Analytics/EventTracker.cs`
   or wherever `session_start` is emitted from — read that file fully before choosing a mechanism.
   A heartbeat (e.g. every 60s while foregrounded) is likely more robust than relying on a clean
   app-quit signal reaching the server, since mobile OSes can kill an app without warning; but
   don't guess — check what similar mobile analytics needs this project has already solved (e.g.
   how `OnApplicationPause` is already handled elsewhere, if at all) before picking an approach.
2. Backend: compute session duration per session_start from whatever new signal lands, bucket into
   the owner's four ranges, and add a `view=` on `api/admin/db.js` or `api/admin/stats.js` (check
   which file already owns similar player-behavior aggregates — `stats.js` already has the
   hero-level distribution pattern to follow) reporting counts per bucket.
3. Follow the existing distribution-reporting shape already used for hero level (median, buckets
   with counts, a `low_n`-style caveat if a bucket is too small to trust) rather than inventing a
   new shape.

## What NOT to do

- Do not guess at a session's duration from `session_start` timestamps alone (e.g. "time until the
  next session_start") — that conflates day-gaps with actual play duration and would be a fabricated
  number, not a measured one.
- Do not touch the existing retention (D1/D7) cohort logic — this is a different, orthogonal metric.

## Acceptance criteria

- [ ] Brace balance + NUL check pass on every touched `.cs`.
- [ ] `COMPILE_GATE_OK` + `REGRESSION_OK n/n` on a fresh log.
- [ ] Backend: `node --test test/*.test.js` before/after counts reported, new tests for the bucket
  computation and the new admin view.
- [ ] A device or headless capture confirms the new client-side signal actually fires.
- [ ] The admin view is proven against a real (or realistic seeded) data read — flag it clearly if
  there isn't yet enough real session data with the new signal to populate a meaningful bucket
  distribution (this will be true immediately after shipping, since old sessions carry no end
  signal — say so rather than fabricating a distribution from data that predates the fix).

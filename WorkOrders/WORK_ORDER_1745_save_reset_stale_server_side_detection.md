# WO-1745 — the 409 `SAVE_RESET_STALE` is audited where an admin query can actually read it

**Status:** DONE (code + tests landed; the one acceptance criterion that needs a deploy is NOT PROVEN — see §6)
**Parent:** `WorkOrders/WORK_ORDER_1742_guest_save_rail_vs_google_signin_hypothesis.md` — **Lane A2**
**Lane:** Backend / save rail — server-side observability only
**Opened / closed:** 2026-09-15
**Files:** `api/game/save.js`, `api/admin/db.js`, `test/game.save.reset-epoch.test.js`

---

## 1. ⛔ THE PARENT'S PREMISE WAS WRONG, AND THE CORRECTION IS THE FINDING

WO-1742 §8 states the 409 *"is invisible to the dashboard because it is client-side only"*.

**It is not client-side only.** `api/game/save.js:539` (pre-change) has written a durable row on
every single refusal since WO-1598 landed on 2026-09-07:

```js
await logApiEvent(sql, playerId, 'save_reset_refused', {
    ref: ref, mode: auth.mode, code: resetJudgement.code,
    incoming: resetJudgement.incoming, stored: resetJudgement.stored,
});
```

The blindness is one layer up. `api/admin/db.js`'s only reader of refusals — `view=authrejects` —
filtered on:

```sql
WHERE event_name IN ('api_auth_reject', 'auth_failed')
```

`'save_reset_refused'` was never in that list, so **no query in the product could return one of
those rows**, however many of them Neon holds.

### ⚠ CONSEQUENCE FOR WO-1742 §3 — flag it STALE

WO-1742 §3 reports, as a measurement:

> *"`GET /api/admin/db?view=authrejects` over the 168h window returns exactly one code …
> **Zero** refusals on `/api/game/save` or `/api/game/load`, in any mode, in seven days."*

That reading came off the blind view. It is a **VIEW ARTIFACT, not a measurement** — the view was
structurally incapable of returning a `SAVE_RESET_STALE` row on the date it was run. The number of
409s in that window is **unknown**, and after this change it becomes readable retroactively (§5).

The rest of WO-1742 is unaffected: §7's 409 evidence came from `web_trace`, not from this view.

### Why the server is the right vantage point anyway

`Assets/_Modules/Core/Diagnostics/WebTrace.cs:41` and `:293` gate the remote trace POST on
`#if UNITY_WEBGL && !UNITY_EDITOR`. An Android device **never** posts a trace. So a 409 storm on
Android leaves no evidence anywhere except this server row. Fixing the read path here is
platform-blind by construction; nothing on the client could be.

---

## 2. What changed

### `api/game/save.js` — one row, written through the shape the admin view reads

`logApiEvent('save_reset_refused', …)` → `logAuthReject(sql, req, { code: 'SAVE_RESET_STALE', … })`.

**ONE row, not two.** Adding a second event name beside the first would double every count and be
exactly the duplicated state CLAUDE.md §2 / §5 / §16 each describe in their own words.

The `console.warn('[save] reset epoch refused:', …)` line is **kept** — `logAuthReject`'s own
console line truncates identity and clips `detail` to 600 chars; the warn carries the full
judgement for the runtime-log read path that works without `DATABASE_URL`.

### `api/admin/db.js` — the view widened, all four sites

`'save_reset_refused'` added to **all four** `event_name IN (…)` filters (the `?ref=` lookup, the
summary, and both row queries). Widening fewer would make the ref lookup and the summary disagree
about the same refusal.

The summary's `COALESCE(properties->>'path', '(legacy auth_failed)')` also gained a per-era arm: a
historical `save_reset_refused` row carries no `path` property either, and labelling it as a legacy
auth failure answers "how often, and where" with the wrong endpoint.

### `test/game.save.reset-epoch.test.js` — 13 → 14 cases

1. Case 3 (the OLDER-epoch refusal) now asserts on the `logAuthReject` row: the code, that `ref`
   matches the ref handed to the player, the identity, `detail.incoming/stored/behindBy`, **and**
   that `save_reset_refused` was NOT also written (one refusal = one row).
2. Case 4 (the ABSENT-epoch pin) gained `assert.equal(rejectNamed('SAVE_RESET_STALE'), null)`. Left
   as it was, its `!eventNames().includes('save_reset_refused')` would have passed **vacuously**
   forever, since nothing writes that name any more.
3. New case — *"view=authrejects can actually SEE a SAVE_RESET_STALE row — both eras"*: requires
   `api/admin/db.js` (so a syntax break is caught) and asserts every `event_name IN` list carries
   both `'api_auth_reject'` and `'save_reset_refused'`.

---

## 3. ⛔ What was deliberately NOT touched

- **The 409 itself.** Status, body, `judgeResetEpoch`, and the WO-1598 guard are byte-for-byte
  unchanged. The refusal is correct — it protects a town created elsewhere. This lane adds
  observability and nothing else.
- **`Assets/_Modules/Core/State/GameStateService.cs`.** The client-side resolution is **Lane A**
  and is BLOCKED on the owner's ruling about whose town survives (WO-1742 §8 (a)/(b)/(c)). Writing
  any part of it here would pre-empt that decision. Not one `.cs` file was opened for edit.
- The guest rail, `guestEnabled()`, the guest rate limiter, `EventTracker` / `track.js`.

---

## 4. One live lesson worth keeping

The first draft put a backtick inside an SQL `--` comment that sits **inside a JS template
literal**. The backtick terminated the query string and made `api/admin/db.js` a `SyntaxError` —
and **the whole 736-case suite still passed**, because every assertion on that file reads it as
*text*. A `node -e "require('./api/admin/db.js')"` caught it. That is now pinned as
`assert.doesNotThrow(() => require(…))` in the new case: a view that cannot be loaded is even less
readable than one with the wrong filter.

---

## 5. The query the owner runs AFTER deploy

```
GET https://defenders-of-the-realm-v2.vercel.app/api/admin/db?view=authrejects&code=SAVE_RESET_STALE&since_hours=168
```

**True for BOTH eras (historical `save_reset_refused` rows and new `api_auth_reject` rows):**

- `summary[].hits` = how many saves were refused in the window.
- **`summary[].distinct_ids` = how many separate devices are frozen.** That is the answer to
  "how many players is this costing us", and it is correct across both eras.
- `rows[].player_id` names each one (`guest-local-…`, a wallet, or `play-…`); `rows[].mode`,
  `rows[].code` and `rows[].ref` are populated for both.

**⚠ TRUE ONLY FOR POST-DEPLOY ROWS — stated so nobody reads a NULL as a zero.** `detail.incoming`,
`detail.stored`, `detail.behindBy` and `ipHash` exist only on rows written after this change. The
historical rows carry `incoming` / `stored` at the TOP of `properties`, not under `detail`, so
`properties->'detail'` is **NULL** for them and the `detail` column in the view comes back empty.
How far behind a pre-1745 device was is reachable only by direct SQL on
`properties->>'stored'` / `properties->>'incoming'` where `event_name='save_reset_refused'`. The
view was deliberately NOT widened to re-map those: `distinct_ids` is the number the owner asked
for, and it is right either way.

**Because the view now reads the historical rows too, the COUNT answers over 2026-09-07 → today
immediately on deploy** — within the 168h `since_hours` cap; beyond it, query
`event_name='save_reset_refused'` in `analytics_events` directly. It does not need new traffic.

**⚠ RETENTION DIFFERS BY ERA, and it is worth knowing before someone reads a gap as a fix.**
`api/admin/cleanup.js:114-128` purges `event_name='api_auth_reject'` older than `RETENTION_DAYS`
(7 days) — so from this change on, a `SAVE_RESET_STALE` row lives 7 days. The historical
`save_reset_refused` rows are swept by nothing (cleanup targets only `web_trace` and
`api_auth_reject` **by name**) and so persist. The new window happens to match the view's own
168h ceiling exactly, so nothing the view could have shown is lost — but a long-run frozen-device
census must be taken within 7 days of the refusals, or exported.

**Checked, and clean:** nothing else consumes the `api_auth_reject` bucket as a signal. The only
other reader in `api/`, `tools/` or `.claude/skills/` is that retention sweep (plus a descriptive
comment at `api/_lib/ops.js:55`) — no cron, throttle, ban or alert counts rejects per `player_id`
or `ipHash`, so a frozen device cannot now feed one.

That settles WO-1742 §7c, which is currently recorded as **NOT PROVEN either way**: if the 14
long-span guest rows with frozen `heroLevel` are frozen cloud rows, their ids appear here; if they
are ordinary early-game churn, they do not.

---

## 6. Acceptance criteria — judged honestly

| WO-1742 Lane A2 AC | Verdict |
|---|---|
| 1. `view=authrejects` returns a `SAVE_RESET_STALE` row after a refused save, **proven against a real response** | ⛔ **NOT PROVEN — and it cannot be, from this lane.** Proving it needs a deploy, and production promotion is the owner's alone (canon: preview-only, never `--prod`). The handler path is proven by the stub test; the wire is not. The one step that closes it is the §5 URL, run after the next deploy. Recorded as unproven rather than ticked (CLAUDE.md §11B). |
| 2. No change to the 409 status, body, or the guard itself | ✅ PROVEN — `judgeResetEpoch`, the `quietFail(res, 409, …)` call and the body are unchanged; `test/game.save.reset-epoch.test.js` case 3 still asserts `409` + `code:'SAVE_RESET_STALE'` + `insertCall() === null`, and passes. |

**Tests:** `node --test test/game.save.reset-epoch.test.js` → **tests 14, pass 14, fail 0**.
Full suite `node --test test/*.test.js` → **tests 736, pass 735, fail 0, skipped 0, todo 1**.
The new view pin is **RED-PROVEN**: removing `'save_reset_refused'` from the four IN lists gives
`pass 13, fail 1` on exactly that case; restored and re-run green.

---

## 7. What remains BLOCKED (do not confuse it with this lane)

**Lane A — `GameStateService.cs:3266-3273` — needs an owner ruling and is untouched.** A device
whose `resetEpoch` is behind the stored one still drops the marker forever, never retries, and
never tells the player. This lane makes that **countable**; it does not make it **stop**. The
ruling required is WO-1742 §8: (a) the cloud town wins, (b) the player chooses, or (c) — called
out there as a trap, not an option — this device wins.

**Lane B** stays parked on the owner's Play Console / Cloud Console check (WO-1742 §5, and see
WO-1744).

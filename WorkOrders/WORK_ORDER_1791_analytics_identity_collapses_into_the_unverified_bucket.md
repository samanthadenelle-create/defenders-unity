# WORK ORDER 1791 — 73% of today's live client events land under ONE shared player id (`unverified`), so no funnel can be read per player

**Status:** READY TO IMPLEMENT
**Minted:** 2026-09-16 (number PRE-ASSIGNED by the lead from the 1791-1795 block; this lane did NOT touch `CLI_LANES_WO_NUMBERS.md`)
**Silo:** Analytics rail — `Assets/_Modules/Core/Analytics/EventTracker.cs` (client) only. No `.unity`, no gameplay, no API change required.
**Priority:** P0 for the business question. It affects EVERY player on every build that does not attach the headers, and it is the reason the owner's "22 new players" cannot be turned into a funnel.
**Lane disjointness:** file-disjoint from WO-1792 (structure art), WO-1793 (`api/admin/db.js`), WO-1794 (tutorial/funnel emitters), WO-1795 (`api/game/save.js`).

---

## 1. THE PROVING LINE (measured 2026-09-16 against the production Neon DB, read-only)

```
SELECT properties->>'_auth' AS auth, COUNT(*), COUNT(DISTINCT player_id)
  FROM analytics_events WHERE received_at >= '2026-09-16' AND received_at < '2026-09-17'
  GROUP BY 1 ORDER BY 2 DESC;

 unverified  | 520 rows | 1 id     <-- 73% of the day, ONE bucket
 session     |  88 rows | 6 ids
 guest       |  45 rows | 6 ids
 guest-body  |  31 rows | 7 ids
 (null)      |  29 rows | 11 ids   <-- server-written rows (session_issued etc.)
```

Total for the day: **713 events / 25 distinct `player_id` values** — identical to the
`view=metrics` numbers, which is the same-database proof for every figure in this ticket.

**The single `unverified` row is not one player.** Its own `session_start` rows carry FOUR
different `appVersion` strings:

```
SELECT string_agg(DISTINCT properties->>'appVersion', ',') FROM analytics_events
  WHERE event_name='session_start' AND player_id='unverified' AND received_at >= '2026-09-16';
 2026.09.07.359722, 2026.09.15.371500, 2026.09.16.371627, 2026.09.16.371701
```

and it holds **four separate founding_path_selected → tutorial_started → tutorial_completed
sequences** at 02:29, 14:00, 14:32, 16:53 and 17:10 UTC — five distinct humans' first sessions
welded into one id. It also holds 327 of the day's 424 `playtest_break` rows and all 8
`possible_softlock` rows, so no break and no softlock today can be attributed to a player.

## 2. WHY — AND THE PART THAT IS ALREADY FIXED (all read at source, 2026-09-16)

> ⛔ **THE OBVIOUS FIX IS ALREADY SHIPPED. DO NOT RE-IMPLEMENT IT.** `EventTracker.SendBatch`
> (`Assets/_Modules/Core/Analytics/EventTracker.cs:293-333`) already calls
> `BackendRequestSigner.TryAttachCachedSession(req, identityPlayerId)` under **WO-1735**, with a
> `ReportIdentityMode` trace that names the failure cause (`:380-415`). It **works**: every
> `session_start` from `2026.09.16.371701` today landed as `_auth:'session'` (3 ids) or
> `_auth:'guest'` (6 ids). A lane that "adds the headers" would implement nothing.

**The 520-row bucket is a BUILD COHORT plus two residual holes.** Every `session_start` that landed
`unverified` today, by version:

```
7 rows  2026.09.07.359722   00:21 -> 17:48   <-- pre-WO-1735 client, no headers at all
3 rows  2026.09.16.371627   02:29 -> 03:04
1 row   2026.09.15.371500   00:05
1 row   2026.09.16.371701   17:48            <-- THE RESIDUAL HOLE, on the fixed build
```

and every `guest-body` row (the WO-1733 server-side fallback, which only rescues a GUEST-shaped body
id) came from `2026.09.07.359722` / `.359670`. **7 of today's 17 attributable sessions are on the
pre-fix build**, and their wallet-bound events can never be attributed — no client change reaches
them. That half is a **store-update / cohort-retirement** question, not a code fix, and it must be
stated in the RESULT rather than "fixed".

### Hole 1 — the fixed build still drops attribution when a wallet holds no session

`ReportIdentityMode` (`EventTracker.cs:405-415`) names the two causes itself: *"there is no account
id at all yet (BoundWallet empty — events queued before GameStateService.EnsureAccount mints the
guest id)"* and *"a WALLET identity is bound but no backend session is held in memory"*. The comment
at `:308-320` is explicit that the flush **deliberately does not abort** on `false` — correct for
telemetry, but the batch then posts with a wallet-shaped body id and no header, which
`api/events/track.js` resolves to the literal `unverified`. That is exactly the one `.371701` row at
17:48:13, and `session_start` is the most exposed event of all because it is queued in `Start()`.
**The cheap close is server-side and needs no new build:** when the body id is wallet-shaped and
unverified, the row can still be written to `unverified` *and* carry the claimed id in a
non-authoritative property (e.g. `_claimedId`) so a funnel can be reconstructed without ever
trusting it for anything that matters. Decide with the owner; do not silently trust a body wallet id
(that is the WO-1506 hole).

### Hole 2 — one human, two ids, on EVERY build (see §3)

Even a perfectly attributed session is split across the guest id and the wallet id, because there is
no alias event. `guest-local-92be2871` → `6Mrx27CdmV` at 18:49:10/18:49:11 both on `.371701`, both
correctly attributed, and still two "players".

### The server's rails, for reference

`api/events/track.js` binds the row to the CALLER, not to the body (WO-1506), through four rails
named in its own header comment:

| header the client sends | resulting `player_id` | `_auth` |
|---|---|---|
| `X-Session` (verified) | the wallet | `session` |
| `X-Guest-Id` | that guest id | `guest` |
| body `playerId`, guest-shaped, no header | that guest id | `guest-body` |
| **a WALLET-shaped body `playerId` with no header** | **the literal `unverified`** | `unverified` |

`EventTracker.Enqueue` (`Assets/_Modules/Core/Analytics/EventTracker.cs:170-186`) sets
`PlayerId = BackendRequestSigner.CurrentPlayerId()` — the bound wallet once the player signs in. On
a **pre-WO-1735 build** the flush sent no header, so a wallet-shaped body id resolved to
`unverified`: the `guest-body` rail saved the player before sign-in and nothing saved them after.
That is the 2026.09.07 cohort above, verbatim the condition `api/events/track.js:26-33` records.

⚠ The comment in `track.js` names `EventTracker.cs:290-294` as "sets only Content-Type". **That
comment is now stale** — the same lines are the WO-1735 attach. Read the `.cs`, not the comment
(CLAUDE.md §11B.A).

## 3. THE SECOND HALF — one human, two ids, and neither has the whole session

Even when attribution works, a player's session is SPLIT across their guest id and their wallet id,
because the guest id is minted on device and the wallet id replaces it mid-session. Measured pairs
today (guest `session_start`, then a wallet `session_issued` 1-24 s later, and no further events
under the guest id):

| guest id (prefix) | first seen | wallet id (prefix) | first seen |
|---|---|---|---|
| `guest-local-27a0353d` | 14:00:09 | `A2gx78TqNt` | 14:00:24 |
| `guest-local-520d82de` | 14:31:22 | `325h1eAqSS` | 14:32:03 |
| `guest-local-0821c1bb` | 16:28:20 | `9tKB3Qqkfe` | 16:28:23 |
| `guest-local-782feaa2` | 16:53:01 | `EFhRvgYeNQ` | 16:52:54 |
| `guest-local-aaf35027` | 17:00:01 | `BBJLKxyhav` | 17:09:34 |
| `guest-local-92be2871` | 18:49:10 | `6Mrx27CdmV` | 18:49:11 |
| `guest-local-76db9270` | 21:02:26 | `2ZurmHauf4` | 21:02:38 |

`player_data` carries the same split: 21 rows updated today, **18 of them created today**
(9 `trust='wallet'` + 9 `trust='guest'`), and **7 of those guest rows were written once and never
updated again** — a husk save (84-86 keys) sitting beside a wallet row for the same human.

## 4. WHAT TO DO

1. **DO NOT touch the header attach.** WO-1735 shipped it and it is proven working on `.371701`.
2. **Emit ONE alias event when the guest id is replaced by a wallet id** —
   `identity_bound { from:<guest id>, to:<wallet id> }` — so a session split across two ids can be
   stitched server-side. This is the one change that fixes every build going forward, and it is
   Hole 2. Without it, every "new player" count double-counts and every funnel under-counts.
3. **Close Hole 1 with the owner's ruling** — either a `_claimedId` non-authoritative property on the
   server side (reaches the installed cohort, changes no trust boundary) or a client retry that
   defers the flush until a session exists (does NOT reach the installed cohort, and risks dropped
   telemetry — the reason `:308-320` refuses to abort).
4. **Say the cohort out loud in the RESULT.** 7 of today's 17 attributable sessions run the pre-fix
   `2026.09.07.359722`; their wallet-phase events are permanently unattributable. Do NOT retro-
   attribute existing rows, and never let a later reader treat the historical `unverified` bucket as
   one player.

## 5. ACCEPTANCE CRITERIA

- [ ] `identity_bound` appears exactly once per session that signs in, never on a guest-only run,
      and carries both ids.
- [ ] A device run on `.371701`+ produces zero `unverified` rows for the whole session INCLUDING the
      `session_start` queued in `Start()`. Proven by the §1 query on a fresh day, not by a code read.
- [ ] The RESULT states the pre-WO-1735 cohort's size and that it is unfixable client-side.
- [ ] No gameplay file touched; no change to the `_auth` rails' trust boundary.
- [ ] `COMPILE_GATE_OK` + `REGRESSION_OK <n>/<n>` on a fresh log (markers, not exit codes).

## 6. WHAT NOT TO TOUCH

The server rails in `api/events/track.js`; the `unverified` bucket name (it is deliberately ONE
bucket so `ANALYTICS_EXCLUDED_PLAYER_IDS` can drop it wholesale); the guest-id minting in
`GameStateService.EnsureAccount`.

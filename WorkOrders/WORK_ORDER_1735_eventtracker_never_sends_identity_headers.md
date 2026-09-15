# WORK ORDER 1735 — EventTracker sends no identity headers, so wallet players stay `unverified`

**Status:** READY TO IMPLEMENT
**Minted:** 2026-09-15 (main line; banner bumped 1735 -> 1736 in the same edit)
**Silo:** **Unity** — `Assets/_Modules/Core/Analytics/` + `Assets/_Modules/Core/Web3/`. Separate gate
from `api/`. Do **not** combine with a server change in one lane.
**Parent:** WO-1733 (server-side guest fallback — **DONE**, see its RESULT §8).

---

## 1. Why this exists as a ticket and not as a note

WO-1506's RESULT said, at `:37-38`, that the client needed a ticket. That sentence sat in prose inside a
**closed** ticket. The board is derived from `**Status:**` lines, so nothing ever surfaced it, nobody
minted it — and for eight days **every analytics event from every player** landed under the single id
`unverified`, with the dashboard's `COUNT(DISTINCT player_id)` reading **1**.

WO-1733 fixed the shipped fleet from the server side. Recording this remaining half only in that
ticket's §6 — under `**Status:** DONE` — would have been invisible in exactly the same way. Hence a
ticket.

## 2. The defect

`Assets/_Modules/Core/Analytics/EventTracker.cs:290-294` builds its **own** `UnityWebRequest` and sets
exactly one header, `Content-Type`. It never goes through `BackendRequestSigner`, which is what attaches
`X-Session` / `X-Guest-Id` on the **save** rail
(`Assets/_Modules/Core/Web3/BackendRequestSigner.cs:198`, `:426-430`).

`api/events/track.js` takes identity from those headers. With neither present, every request falls
through to the fallback rails.

## 3. What WO-1733 already recovered, and what it CANNOT

WO-1733 added a narrow server fallback: a **guest-shaped** `playerId` in the request body names the row
(`_auth:'guest-body'`), because a guest id is a 256-bit bearer credential the server already trusts in
`X-Guest-Id`.

⛔ **It deliberately does NOT accept a wallet from the body, and never will.** A wallet address is
**public** — readable off the chain or a leaderboard — not a credential. Accepting one from the body
re-opens the exact hole WO-1506 closed (anyone writing analytics rows under any player's identity).

**Consequence: signed-in and PAYING players are still attributed to `unverified`.** That is the cohort
the owner most wants to see, and only this ticket can recover it.

## 4. The work

Attach the same identity headers the save rail already attaches, to the analytics POST.

- Prefer **reusing** `BackendRequestSigner`'s existing header-attachment seam over copying header names
  into `EventTracker` — a second copy of the header logic is the duplicated-state failure CLAUDE.md §2,
  §5 and §16 each describe.
- Analytics is **fire-and-forget** and must stay that way: it must never block a flush waiting on a
  session, never trigger a signing round-trip of its own, and never fail a batch because no session
  exists. No session -> send the guest header (or neither) and let the server's rails decide.
- ⚠ The route's CORS preflight already admits both headers (`track.js`, pinned by a test in
  `test/events.track.test.js`), so no server change is needed for the browser/WebGL build.
- ⚠ **Do not spend the guest save budget.** `guest_rate_limit` is keyed on the guest id and **shared
  with `game/save` + `game/load`** (30 per 60s, `wallet-auth.js:211-212`). A busy analytics flush that
  spends it would rate-limit the player's own saves. The server route avoids `verifyGuest()` for exactly
  this reason; the client must not reintroduce the cost from its side.

## 5. Acceptance criteria — measurable on the live DB, not on a claim

After a build carrying this ships and real traffic arrives:

- [ ] `_auth:'session'` rows appear for wallet-bound players, with `COUNT(DISTINCT player_id) > 1`.
- [ ] `_auth:'guest'` (header rail) rises as `_auth:'guest-body'` (WO-1733's server fallback) falls
      toward zero. **That crossover is the proof this landed**, and it needs no instrumentation.
- [ ] A player's own saves are not rate-limited during a heavy analytics flush.
- [ ] The flush stays fire-and-forget: no new blocking call, no new retry storm.

Query (columns read at source, `api/schema.sql:370-377`):

```sql
SELECT properties->>'_auth' AS auth_rail,
       COUNT(*)             AS rows,
       COUNT(DISTINCT player_id) AS distinct_ids
FROM   analytics_events
WHERE  received_at > NOW() - INTERVAL '1 day'
GROUP  BY 1 ORDER BY 2 DESC;
```

## 6. What NOT to touch

- ⛔ Do not change `api/events/track.js` — the server side is done (WO-1733) and is a different gate.
- ⛔ Do not make the server accept a wallet id from the body. That is the whole security asymmetry;
  the reasoning is written at the seam in `resolveIdentity`.
- ⛔ Nothing here is retroactive. Rows from 2026-09-07T10:45Z onward are permanently unsplittable —
  `track.js` never wrote `ev.playerId` into the row.

# WORK ORDER 1733 — every analytics event lands as `unverified`, so the dashboard reads 1 player

**Status:** DONE
**Minted:** 2026-09-15 (main line; banner bumped 1733 -> 1734 in the same edit)
**Silo:** `api/` (Vercel serverless, Node). **NOT** Unity — the client half is a separate silo.
**Severity:** live data defect. Every business number the owner reads from the Command Center has been
wrong since 2026-09-07T10:45Z.

---

## 1. The symptom, measured

Since **2026-09-07T10:45Z**, every analytics event from every player has been written with the literal
`player_id = 'unverified'`. `COUNT(DISTINCT player_id)` on `analytics_events` therefore reads **1**.

Measured on the live DB (not inferred):

| Reading | Value |
|---|---|
| One "player" | **218 sessions** |
| Daily actives | pinned at **1**, while sessions ran **14-42/day** |
| Distinct ids all-time, BEFORE the cutover | **697** |
| Active in 30 days | 74 |
| Cloud saves | 63 |
| Paying buyers | 2 |

The shape of it — actives flat at 1 while session volume moves — is the fingerprint of an identity
collapse, not of a player drop-off.

## 2. Root cause (proven, not inferred)

**WO-1506 closed a real hole.** `/api/events/track` used to write `analytics_events.player_id` straight
off the request body, with no auth and no rate limit, so any caller could write unbounded rows
attributed to any wallet. That had to be closed and it was closed correctly.

**But it made identity HEADER-ONLY**, and the Unity client sends neither header:

- `api/events/track.js:81` — `UNVERIFIED_PLAYER_ID = 'unverified'`; `resolveIdentity` (was `:93-110`)
  reads `X-Session`, then `X-Guest-Id`, then falls back to that literal.
- `Assets/_Modules/Core/Analytics/EventTracker.cs:290-294` — the client builds its **own**
  `UnityWebRequest` and sets exactly **one** header, `Content-Type`.
- It does **not** go through `BackendRequestSigner`, which is what attaches `X-Session` / `X-Guest-Id`
  on the **save** rail (`Assets/_Modules/Core/Web3/BackendRequestSigner.cs:198`, `:426-430`).

So both header paths miss on every request from every build, and every row takes the fallback.

**WO-1506's own RESULT predicted exactly this** —
`WorkOrders/WORK_ORDER_1506_events_track_accepts_a_client_asserted_playerid_with_no_auth.RESULT.md:37-38`
— and said it needed a client ticket. **That ticket was never written.** (Confirmed this session:
`grep -rln "X-Guest-Id" WorkOrders/` returns only 1506 itself, 1440, 837 and a preserved `.cs.txt`.)

## 3. Why the fix is SERVER-side

A client fix only ever helps builds shipped **after** it. The build in players' hands today — including
the public store build **`2026.08.17.328845`** — would stay dark forever.

The server fallback reaches **every build already installed** on the next API deploy. And it has
something to work with, verified at source this session:

- `GameStateService.cs:2118-2139` (`EnsureAccount`) mints `BoundWallet = "guest-local-" +
  SHA256(deviceId + salt)` for any player not signed in (`GuestWalletPrefix` at `:2065`, `HashDeviceId`
  at `:2884`).
- `EventTracker.cs:168` puts `BoundWallet ?? "anonymous"` into each event's `playerId`.
- That value matches `GUEST_RE = /^guest-local-[0-9a-f]{64}$/` (`api/_lib/wallet-auth.js:132`).

So the body of a shipped guest build already carries a real, per-device guest id. It is being thrown
away.

## 4. The change

`api/events/track.js` — `resolveIdentity` gains a fourth parameter and one new rail, tried **last**,
after both header paths:

```
X-Session   -> verifySession names the wallet            -> _auth:'session'
X-Guest-Id  -> a guest-shaped id binds to itself         -> _auth:'guest'
body guest  -> a GUEST-SHAPED body playerId, no header   -> _auth:'guest-body'   <-- NEW
neither     -> the literal id `unverified`               -> _auth:'unverified'
```

### ⛔ The invariant that must survive every future edit

**Only the GUEST shape may come from the body. A WALLET may not, ever.**

- A **guest id is a 256-bit bearer credential**, minted on the device. The server **already** extends
  exactly this value exactly this trust when it arrives in `X-Guest-Id` (the branch ~8 lines above).
  Accepting the same unguessable string through a different channel of the same request **forges
  nothing new** — an attacker who knows a guest id can already present it as the header.
- A **wallet id is a PUBLIC address**, not a credential — readable off the chain or a leaderboard.
  Accepting a wallet-shaped body id would re-open precisely the hole WO-1506 closed. A wallet is
  therefore only ever established by `X-Session`, whose token proves a signature. A `play-` id is
  likewise refused: its shape is derived under a server key.
- `isGuestId()` (`api/_lib/wallet-auth.js:153`) is the gate, and it is **lexically disjoint** from both
  other shapes by construction (`wallet-auth.js:143-150`).

The reasoning is written **at the seam** in `track.js` so nobody "simplifies" it into "accept whatever
the body says".

### Two details that were checked, not assumed

- **`guestEnabled()` gates the header path** (`track.js`, and `wallet-auth.js:225-229` — default ON,
  `GUEST_SAVE_ENABLED=false` switches it off). The **body path respects the same gate**, or the kill
  switch would leave a second door open. Pinned by a test.
- **Batch identity rule.** One flush can mix `"anonymous"` events (queued before `EnsureAccount`) with
  guest-id events. The rule is: **the first guest-shaped `playerId` in the batch names the whole
  batch** — least-lossy, and it forges nothing since the sender already controls the header. It is read
  from the **capped** batch (`MAX_EVENTS_PER_BATCH`), so a **dropped surplus event can never steer the
  identity of the rows that land**. The `events.slice()` was moved above the identity resolve for this.
- **`_auth:'guest-body'` is a distinct tag.** Nothing in `api/` filters on `_auth` (grepped
  2026-09-15), so a new value drops no existing view. It makes the post-deploy proof one `GROUP BY`,
  and when the client half lands this count falls toward zero on its own — that is the landing signal.

## 5. ⚠ NOT RETROACTIVE

`track.js` discards `ev.playerId` entirely when building the insert — the row's `player_id` has only
ever been `identity.playerId`. **Rows written from 2026-09-07 onward are permanently unsplittable.**
There is nothing in the table to recover an identity from. Any trend line that crosses that date is
comparing two different measurements and must be read as such.

## 6. ⚠ STILL OUTSTANDING — the CLIENT half → **TICKETED AS WO-1735**

`WorkOrders/WORK_ORDER_1735_eventtracker_never_sends_identity_headers.md` — **READY TO IMPLEMENT**.

It is a **ticket**, not a paragraph here, on purpose: WO-1506's RESULT `:37-38` already said "this needs
a client ticket", that sentence sat in prose inside a **closed** ticket, the board never saw it, nobody
minted it — and that is the mechanism that produced this bug. A follow-up recorded only in §6 of a
`**Status:** DONE` ticket is invisible to the derived board in exactly the same way.

The correct long-term fix is client-side, and it is **not implemented in this lane**: Unity is a
separate silo with a separate gate (Unity `.cs` under `Assets/` was not touched).

- **What:** `EventTracker.cs:290-294` should attach the same identity headers
  `BackendRequestSigner.cs:198` already attaches on the save rail.
- **Why it still matters after this WO:** the server fallback recovers **guest** attribution only.
  **Wallet-bound players stay `unverified`** until the client sends `X-Session` — a wallet can never be
  accepted from the body. Signed-in and paying players are exactly the cohort the owner most wants to
  see.
- **Signal that it landed:** `_auth:'guest-body'` counts fall toward zero as `'guest'` and `'session'`
  rise.

## 7. Acceptance criteria

- [x] A guest-shaped body `playerId` with no headers names the row, tagged `_auth:'guest-body'`.
- [x] A wallet-shaped body id is still refused and lands `unverified`.
- [x] A `play-` shaped body id is refused.
- [x] `"anonymous"` buys nothing.
- [x] `X-Guest-Id` wins over a body guest id; a valid `X-Session` wins over both.
- [x] `GUEST_SAVE_ENABLED=false` switches off the body rail too.
- [x] The body rail never touches `guest_rate_limit` (it would 429 the player's own saves).
- [x] A surplus event past the batch cap cannot steer identity.
- [x] The success path and the IP-budget behaviour are unchanged.
- [x] `node --test test/events.track.test.js` green; full `test/*.test.js` shows no new failure.

## 8. What NOT to touch

- ⛔ No Unity `.cs` under `Assets/` (separate silo, separate gate).
- ⛔ No deploy, no `vercel` anything, no git. **Production promotion is the owner's alone.**
- ⛔ Do not widen the body rail past `isGuestId()`.
- ⛔ Do not add `unverified` to `ANALYTICS_EXCLUDED_PLAYER_IDS` as part of this — it is a separate
  decision (see `track.js` follow-up 2), and doing it now would hide the pre-fix bucket before anyone
  has looked at it.

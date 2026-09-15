# WO-1733 RESULT — server-side guest fallback for `/api/events/track`

**Status:** DONE (edit-only lane — NOT committed, NOT deployed; the lead commits, the owner promotes)
**Date:** 2026-09-15
**Silo:** `api/` only. No Unity `.cs` under `Assets/` was touched.

---

## 1. Files changed

| File | What |
|---|---|
| `api/events/track.js` | `resolveIdentity` gains a 4th param + the body-guest rail; new `firstGuestShapedId()`; the batch cap moved above the identity resolve; header comment blocks corrected |
| `test/events.track.test.js` | one retired source-text lint replaced; **10 new behavioural tests** |
| `CLI_LANES_WO_NUMBERS.md` | minted **1733**, bumped **1733 -> 1734** in the same edit |
| `WorkOrders/WORK_ORDER_1733_analytics_every_event_lands_as_unverified.md` | the WO, Status: DONE |

## 2. The diff of `resolveIdentity`

Signature:

```diff
-async function resolveIdentity(sql, headers, sessionVerifier) {
+async function resolveIdentity(sql, headers, sessionVerifier, bodyGuestCandidate) {
```

The new rail, inserted **after** both header paths and **before** the `unverified` fallback (comment
abridged here; the full reasoning is at the seam in the file):

```diff
     const guest = headers['x-guest-id'];
     if (guest && guestEnabled() && isGuestId(String(guest))) {
         return { playerId: String(guest), auth: 'guest' };
     }
 
+    // ── WO-1733: the GUEST shape, and ONLY the guest shape, may come from the body ──
+    //
+    // ⛔ DO NOT "SIMPLIFY" THIS INTO `accept whatever playerId the body names`. The
+    //    asymmetry is the whole security property, and it is not an oversight:
+    //    * A guest id is a 256-bit bearer credential minted on the DEVICE. The server
+    //      ALREADY extends exactly this value exactly this trust when it arrives in
+    //      X-Guest-Id, ~8 lines up — so the same string via the body forges nothing NEW.
+    //    * ⛔ A WALLET id is NOT a credential — it is a PUBLIC address. Accepting a
+    //      wallet-shaped id from the body would re-open the hole WO-1506 closed.
+    //      isGuestId() is the gate, lexically disjoint from wallet and play- shapes.
+    // guestEnabled() is checked for the same reason the header path checks it: the
+    // rail's kill switch must switch off the WHOLE rail.
+    if (bodyGuestCandidate && guestEnabled() && isGuestId(bodyGuestCandidate)) {
+        return { playerId: bodyGuestCandidate, auth: 'guest-body' };
+    }
+
     return { playerId: UNVERIFIED_PLAYER_ID, auth: 'unverified' };
 }
```

The candidate picker, and the cap moved above the resolve so a dropped event cannot steer identity:

```diff
+function firstGuestShapedId(batch) {
+    for (const ev of batch) {
+        if (!ev) continue;
+        const id = ev.playerId;
+        if (typeof id === 'string' && isGuestId(id)) return id;
+    }
+    return null;
+}
```

```diff
-            const identity = await resolveIdentity(sql, headers, sessionVerifier);
+            // The cap is applied HERE, before identity, so a dropped surplus event
+            // can never be the one that names the batch (WO-1733).
+            const batch = events.slice(0, MAX_EVENTS_PER_BATCH);
+
+            const identity = await resolveIdentity(
+                sql, headers, sessionVerifier, firstGuestShapedId(batch),
+            );
```

(The `const batch = events.slice(...)` line below the insert comment was removed — it moved, it was not
duplicated.)

## 3. The guest-shape check reused — file:line

- **`isGuestId`** — `api/_lib/wallet-auth.js:153`
  `function isGuestId(id) { return typeof id === 'string' && GUEST_RE.test(id); }`
- **`GUEST_RE`** — `api/_lib/wallet-auth.js:132` — `/^guest-local-[0-9a-f]{64}$/`
- Already imported by the route — `api/events/track.js:71` — no new import.
- Disjointness from the other shapes is asserted in-code at `wallet-auth.js:143-150` (`WALLET_RE`
  `:152`, `PLAY_RE` `:150`).

Client side, confirming the body actually carries this shape on shipped builds:
`GameStateService.cs:2065` (`GuestWalletPrefix = "guest-local-"`), `:2126` (`EnsureAccount` assigns it),
`:2884` (`HashDeviceId` -> SHA-256 hex), `EventTracker.cs:168` (`BoundWallet ?? "anonymous"` into
`playerId`).

## 4. Wallet identity is still HEADER-ONLY — confirmed

Yes. The body rail is guarded by `isGuestId(bodyGuestCandidate)`, and `GUEST_RE` cannot match a base58
wallet (different literal prefix) or a `play-` id (different prefix). A wallet can still only be
established by `X-Session` -> `verifySession`.

Pinned by three tests, all passing:

- `an asserted wallet id with NO auth headers is overridden, never written` -> `unverified` (pre-existing,
  still green)
- `⛔ a WALLET-shaped body id is STILL refused — a wallet address is public, not a credential` (new)
- `a play- shaped body id is refused too` (new)

## 5. Does `guestEnabled()` gate it? — read, not assumed

**Yes, and the body path now respects the same gate.** `api/_lib/wallet-auth.js:225-229`: default **ON**;
`GUEST_SAVE_ENABLED` set to `0|false|off|no` (case-insensitive) switches it **off**. The header path at
`track.js` already checked it; the body path checks it in the same expression order. Pinned by
`GUEST_SAVE_ENABLED=false switches off the BODY rail too, not just the header` (env restored in a
`finally`).

## 6. Tests

Extended the **existing** suite `test/events.track.test.js` (`npm test` -> `node --test test/*.test.js`);
no new harness.

**One assertion was retired, deliberately and visibly.** `test/events.track.test.js:232` was
`assert.doesNotMatch(executable, /ev\.playerId/)` — it pinned "the body never names the player". The
invariant is now the narrower "the body never names a **wallet**", which a source-text lint cannot
express. It is replaced by a comment saying so plus
`assert.match(trackSrc, /isGuestId\(bodyGuestCandidate\)/)` (the body rail must keep running its
candidate through the shape gate), and by the behavioural pins below. The test file's own header block
was amended too (§15: canon in the same breath).

10 new tests: guest body id accepted; wallet body id refused; `play-` refused; `"anonymous"` refused;
header guest wins over body guest; session wins over body guest; mixed batch takes the first
guest-shaped id for every row; `GUEST_SAVE_ENABLED=false` kills the body rail; the body rail never
touches `guest_rate_limit`; a surplus event past the cap cannot steer identity.

**Measured, this session:**

```
node --test test/events.track.test.js
ℹ tests 22    ℹ pass 22    ℹ fail 0
```

```
node --test test/*.test.js
ℹ tests 735   ℹ pass 734   ℹ fail 0   ℹ todo 1
```

The single `✖` printed by the full run is `heartbound-contract.test.js` -> `the streak reaches
heartbound_state`, marked **todo** (`# WO-1693 finding 2 — recordVerifiedStake names no streak column`),
so the runner counts `fail 0`. It is pre-existing and in another silo; nothing this lane touched is
reachable from it.

## 7. ⚠ NOT retroactive

`track.js` never writes `ev.playerId` into the row — `player_id` has only ever been
`identity.playerId`. Rows written from **2026-09-07T10:45Z** onward carry no recoverable identity.
**They are permanently unsplittable.** Any metric whose window crosses that date is comparing two
different measurements.

## 8. ⚠ The CLIENT half — TICKETED AS **WO-1735**, not done here (separate silo, separate gate)

`WorkOrders/WORK_ORDER_1735_eventtracker_never_sends_identity_headers.md` — **READY TO IMPLEMENT**
(banner bumped 1735 -> 1736 in the same edit; a parallel hero-feel lane took 1734 mid-write, so the
number was re-taken off the banner top — memory `parallel-worktree-lanes-collide-on-wo-numbers`).

It is a ticket rather than a paragraph because prose in a closed ticket is precisely what caused this
bug: WO-1506's RESULT `:37-38` asked for a client ticket, the board never saw it, nobody minted it.
`BOARD.html` regenerated and verified — WO-1733 parses as **Done**, WO-1735 as **Ready**,
`BOARD_CHECK_OK 0 status contradictions`.


`EventTracker.cs:290-294` should attach the same identity headers `BackendRequestSigner.cs:198` attaches
on the save rail. It is the correct long-term fix and it is the **only** thing that restores **wallet**
attribution — signed-in and paying players stay `unverified` until it ships, because a wallet may never
be accepted from the body. Recorded in the WO §6; no Unity file was edited.

## 9. THE PROOF COMMAND — run AFTER the API deploy

Column names read at source from `api/schema.sql:370-377` (`player_id`, `properties` JSONB,
`received_at TIMESTAMPTZ DEFAULT NOW()`). Substitute the actual deploy timestamp:

```sql
SELECT properties->>'_auth'            AS auth_rail,
       COUNT(*)                        AS rows,
       COUNT(DISTINCT player_id)       AS distinct_ids
FROM   analytics_events
WHERE  received_at > TIMESTAMPTZ '<deploy time UTC>'
GROUP  BY 1
ORDER  BY 2 DESC;
```

**What proves it worked:** a `guest-body` row appears with `distinct_ids` **> 1** and climbing — that is
one id per real device. **What proves it did not:** every row still `unverified` with `distinct_ids = 1`.

Blunter one-liner for the same question:

```sql
SELECT COUNT(DISTINCT player_id)
FROM   analytics_events
WHERE  received_at > TIMESTAMPTZ '<deploy time UTC>';
```

It should exceed 1 within one play session of real traffic. The Command Center's own actives tiles
(`api/admin/stats.js:380-382`, `COUNT(DISTINCT player_id) FILTER (WHERE received_at > NOW() - INTERVAL
'1 day')`) will show the same recovery on the next load — that view is the owner-facing version of this
query and needs no change.

⚠ Note for whoever reads the tiles first: `unverified` is **not** auto-excluded
(`api/admin/stats.js:247` `excludedPlayerIds()` hardcodes only `ANON_ID`), so the historical bucket
still counts as one extra "player" until `ANALYTICS_EXCLUDED_PLAYER_IDS=unverified` is set on the
deployment. That is a separate decision and was deliberately not made here.

## 10. Not done, by instruction

No commit. No `git`. No `vercel`. No deploy. No Unity edit.

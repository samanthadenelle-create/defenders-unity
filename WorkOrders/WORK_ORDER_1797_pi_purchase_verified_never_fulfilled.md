# WORK ORDER 1797 — The Pi purchase that will sit `verified` forever: the Pi rail has NO fulfilment acknowledgement

**Status:** DONE - committed e6f3c6cfe, gated (COMPILE_GATE_OK/REGRESSION_OK or node --test as applicable). PRIOR: READY FOR LEAD REVIEW
**Implemented:** 2026-09-17, Lanes A + B, option **(b)** — see the IMPLEMENTATION RECORD below.
⛔ **Lane C (§3.4) is deliberately NOT done: it needs the owner's revenue ruling, and
`api/admin/stats.js` is byte-unchanged.** No `.RESULT.md` yet — that is the lead's after gating.
**Number:** PRE-ASSIGNED by the lead (this WO does **NOT** touch `CLI_LANES_WO_NUMBERS.md`)
**Date:** 2026-09-16
**Silo:** purchase ledger — `api/purchases/fulfill.js`, `Assets/_Modules/Core/Payments/Providers/Pi/PiBrowserPaymentProvider.cs`, plus ONE reporting correction in `api/admin/stats.js`. No pack contents, no pricing, no grant logic.
**Lane:** Monetization/Backend (§9). ⚠ File-disjoint from WO-1796 **except** `api/admin/stats.js` — see §6.

> ⏳ **TIME-CRITICAL, ONE PART ONLY:** the client trace that could prove whether this player's pack
> actually landed is **deleted by the nightly cleanup cron at 04:00 UTC on 2026-09-18** (§4.1, measured
> from `cleanup.js:35`/`:81-86` + `vercel.json:7`). The FIX has no deadline; the EVIDENCE does. Run
> §4.1's SQL before then or that question closes permanently.

---

## 0. THE ROW (measured this session, `GET /api/admin/db?view=purchases&limit=10`, HTTP 200)

```
entitlement_id       8
sku                  hearth-spark
network              pi          currency  PI
status               verified
verified_at          2026-09-10T23:42:54.194Z
fulfilled_at         null
unfulfilled_minutes  8522        (~5.9 days, and climbing)
usd_anchor           4.9900
wallet               pi-a28de6eb-0ed1-4ee3-911d-b95e8d0bdbca
tx_signature         51a1fd62cb5a6ad5fd453af62829df9fd180e8cff582fa12a4487daf5f95bb89
quote_ref            b335e94573fc9719eec0999712777ddf
```

Summary from the same call: `fulfilled: 3` (2026-08-23 → 08-25), `verified: 1` (this row).
`amount_mismatches: 0`. The three fulfilled rows are all wallet `CHKKFkPGz8VZ…` and each was flipped
**within 0.3–35 seconds** of `verified_at`. **This is the only row in the table that has ever stalled,
and it is the only row on the Pi rail.**

---

## 1. ⛔ THE DEAD STEP — NAMED, AND PROVEN THREE INDEPENDENT WAYS

> ## THE PI RAIL HAS NO FULFILMENT ACKNOWLEDGEMENT AT ALL. **EVERY** PI PURCHASE WILL SIT `verified` FOREVER, BY CONSTRUCTION.
> It is not a lost poll, not a race, not a dropped request. **The call is not in the tree, and the route
> it would call would refuse it if it were.**

### Proof 1 — the client never asks. (`grep -rn "MarkFulfilledAsync" Assets/ --include=*.cs`, this session)

`PurchaseEntitlementVerifier.MarkFulfilledAsync` (`Assets/_Modules/Wallet/PurchaseEntitlementVerifier.cs:288`,
POSTing `FulfillUrl` = `/api/purchases/fulfill`, `:55`, `:318`) has exactly **three** call sites, and all
three are in **`Assets/_Modules/Wallet/PackStore.cs:4554, 4570, 4598`** — the Solana/SKR rail.
(Two further hits are regressions pinning those calls: `MonetizationActivationRegression.cs:89-90`,
`StoreCommerceStateRegression.cs:147`.)

The Pi rail's flow ends one step earlier. `PiBrowserPaymentProvider.cs` header `:29` states the intended
chain — *"onReadyForServerCompletion -> POST /api/pi/complete -> ONLY THEN grant"* — and the last thing
it does is `:415`:

```csharp
if (!PiGrantApplier.ApplyExactlyOnce(sku, payment.PiPaymentId))
```

`PiGrantApplier.ApplyExactlyOnce` (`Assets/_Modules/Core/Payments/Providers/Pi/PiGrantApplier.cs:64`)
journals the grant **locally** (`JournalPrefix = "pi.grant."`, `:56`; `StateApplied = "applied"`, `:58`)
and returns true *"only when the entitlement is proven present in the save afterwards"* (`:60-63`).
**That proof never leaves the device.** No POST follows it.

### Proof 2 — even if it did ask, the route refuses it. TWO separate 400s.

`api/purchases/fulfill.js`:
- `:11` `const TX_SIG_RE = /^[1-9A-HJ-NP-Za-km-z]{80,90}$/;` — base58, 80–90 chars.
  The Pi txid on this row is **64 characters of hex** and contains `0` (`…df9fd180e8cf…`), which base58
  **excludes**. Fails on length *and* on alphabet.
- `:33-34` `(network !== 'devnet' && network !== 'mainnet-beta') → BAD_PAYLOAD`.
  The Pi row's network is the literal `'pi'` — `api/_lib/pi-payments.js:54` `const PI_NETWORK = 'pi';`,
  written to the row at `api/pi/complete.js:197`.

Either check alone returns `400 BAD_PAYLOAD` before any DB read. **The route has never been able to
fulfil a Pi entitlement.**

### Proof 3 — and no server path flips it either. (`grep -rn "SET status" api/`)

`api/purchases/fulfill.js:62` is the **ONLY** writer of `status = 'fulfilled'` in the entire `api/`
tree. `api/pi/complete.js:197-201` inserts the row with `'verified'` hardcoded and never updates it:

```sql
INSERT INTO purchase_entitlements (… status, verified_at …)
VALUES (…, 'verified', NOW(), …)
ON CONFLICT (tx_signature) DO NOTHING
```

The Pi rail's own ledger *does* record completion — `pi_payments` is upserted to `state = 'granted'`
with `granted_at = NOW()` (`complete.js:220-231`) — but **that is a different table, and no admin view
reads it.** `grep -n "pi_payments" api/admin/db.js` returns **no matches at all** (run this session), so
the table is absent from the `?view=overview` probe list (`:96-119`) and there is no `?view=pi`. **Not
inferred from a partial read — the string is not in the file.**

### Server-side corroboration that the payment chain DID reach the end

`GET ?view=metrics` (this session) shows on **2026-09-10**: `pi_quote_issued` 11, `pi_payment_approved`
**1**, `pi_entitlement_created` **1**, `pi_payment_lookup_failed` 17. These are server-written audit
rows — `api/_lib/audit.js:118` inserts `logApiEvent` into `analytics_events`.
`pi_entitlement_created` is emitted at `complete.js:233`, **after** the amount re-validation
(`:127-137`), the txid cross-check against Pi's own record (`:140-146`), the single-use quote claim
(`:167-184`) and the entitlement insert. **There is no `pi_payment_manual_review` and no
`pi_payment_record_failed` on that day.** So `/api/pi/complete` ran to completion and returned
`ok:true, state:'granted'`.

**⚠ WHAT THAT DOES AND DOES NOT PROVE.** It proves the SERVER did everything it does. It does **not**
prove the player's pack landed in their save: `PiGrantApplier` runs on the client *after* the response,
and its only record is a local journal. `pi_payment_lookup_failed` ×17 the same day says that session
was messy — that event is emitted from exactly two places, both `pi.getPayment` failures:
`api/pi/approve.js:108` and `api/pi/complete.js:95` (grepped this session) — so the session was not a
clean single pass. **I do not claim the goods were delivered, and I do not claim they were not.**
§4.1 is the query that settles it.

### What it costs, and why it is worse than one stalled row

`api/admin/db.js:356-360` prints this legend on the view the owner reads:
> `verified: 'PAID, grant NOT confirmed - the player may be owed goods'`

For the Pi rail that sentence is **permanently false by construction**. So:
1. Every Pi purchase raises a false alarm forever, and `unfulfilled_minutes` climbs without bound.
2. ⛔ **The alarm that matters is therefore dead.** If a Pi grant genuinely failed —
   `PiGrantApplier.cs:84-91` `FlowTrace.Fail` on *"no local pack grant applier is registered… a paid
   pack is being refused"* — the row would look **identical** to this one. The one column built to
   answer *"did the grant happen"* cannot distinguish a working Pi purchase from a broken one.

### ⛔ AND THE TWO SURFACES ALREADY DISAGREE ABOUT THIS EXACT ROW

`api/admin/stats.js?view=purchases` sums `usd_anchor` with **no status filter** — `:983` (all-time),
`:986` (window), `:1003` (by status), `:1019` (by SKU). So the Command Center counts this row's **$4.99
as settled revenue** while `db.js` flags the same row as *"the player may be owed goods"*.
That arithmetic is visible in the handover's own figure: **$10.97 = 4.99 (Pi, verified) + 2.99 + 2.99**
(the third fulfilled row, 2026-08-23 devnet, has `usd_anchor` NULL and so contributes nothing). §3.3
picks which surface is wrong.

---

## 2. Is the wallet plausibly the owner's? — **NOT ASSERTED**

`docs/handoffs/SESSION_HANDOVER_2026-09-15_play_track_and_wall_fork.md:37`, verbatim, read this session:
> *"The **"2 paying buyers / $10.97" are BOTH the owner** (one SKR, one Pi). External revenue is ZERO."*

That doc names one Pi buyer and there is exactly one Pi row in the table, so the doc's claim maps onto
this row. **I have not verified it independently.** The wallet id `pi-a28de6eb-0ed1-4ee3-911d-b95e8d0bdbca`
is derived from a Pi UID (`pi.piUidOf(playerId)`, `complete.js:224`) and nothing in the repo ties a Pi
UID to a person. **Treat "it is hers" as a doc claim, not a measurement** — and note it changes the
urgency, never the defect: the rail is broken for every Pioneer either way.

---

## 3. THE SPEC — decision lens: **`fulfilled` MEANS "the client acknowledged the grant". Keep it meaning that.**

### 3.1 ⛔ THE TEMPTING FIX IS WRONG. Do NOT auto-flip in `complete.js`.

Setting `status = 'fulfilled'` at `api/pi/complete.js:197` (or right after) is one word and it would
make the row look right. **Do not do it.** `verified` vs `fulfilled` is the project's only
paid-versus-delivered distinction; the server cannot know the client persisted anything, so a
server-side flip makes the column **lie on every rail** and permanently destroys the detector described
in §1. The column is not cosmetic — `db.js:346-360` and `stats.js:1019-1021` both read it as truth.

### 3.2 Lane A — teach `fulfill.js` the Pi rail (server, reaches the deployed WebGL client on next deploy)

`api/purchases/fulfill.js`:
1. Accept `network === 'pi'` along`devnet` / `mainnet-beta` (`:33-34`).
2. **Validate the signature per rail, never with one widened regex.** Import `TXID_RE` from
   `api/_lib/pi-payments.js` (`:443` `/^[A-Za-z0-9]{16,128}$/`, exported at `:449`) and pick the
   validator by network:
   `const sigOk = network === 'pi' ? pi.TXID_RE.test(sig) : TX_SIG_RE.test(sig);`
   ⛔ **Do NOT relax `TX_SIG_RE` itself** — it guards the Solana rail and a loosened alphabet there is a
   real hole. Two rails, two regexes, chosen explicitly.
3. **`walletAllowed` is a third gate and it already passes — confirm, do not "fix" it.**
   `api/_lib/purchase-catalog.js:185-196`: `if (network !== 'mainnet-beta') return true;`. With
   `network='pi'` it returns true on the first line. Leave it alone; say so in the RESULT so the next
   seat does not re-audit it.
4. ⛔ **AUTH IS THE REAL DESIGN QUESTION — GET IT RIGHT OR THE LANE IS POINTLESS.**
   `:45` calls `authenticateGranting` (`api/_lib/wallet-auth.js:829-853`), which gates on an
   **allowlist object**. ⛔ **Read the OBJECT, not the comment** (CLAUDE.md: comments lie) —
   `wallet-auth.js:862-869`, verbatim:

   ```js
   const GRANTING_MODES = {
       wallet: isWalletId,   // ed25519 signature over a single-use nonce
       google: isPlayId,     // Google-signed ID token + a server-only HMAC key
       // guest: ⛔ NEVER through THIS function. …
   };
   ```

   **Two entries. There is no `pi` key**, so `:842-851` refuses a Pi caller with `WALLET_REQUIRED`
   today. That is not an oversight in principle: a Pi identity *is* third-party-proven — sign-in goes
   `POST /api/pi/verify` → Pi's own `GET /v2/me` with the Pioneer's bearer token
   (`api/pi/verify.js:11-13`), the same class of proof as `google` — it simply was never added.
   **Two options; the implementing seat must pick ONE and say why in the RESULT:**
   - **(a) Add `pi` to `GRANTING_MODES`** with its own shape predicate (`pi-<uuid>`), paired the way
     `wallet-auth.js:856-859` describes (*"pairing the mode with its OWN shape check"*). Requires
     `authenticate()` to already resolve a Pi session — **verify that before choosing this**; if it does
     not, (a) is a bigger change than it looks.
   - **(b) A dedicated `POST /api/pi/fulfill`.** ⚠ **Be precise about what "authenticates like the Pi
     rail" means, because it means LESS than it sounds:** `api/pi/complete.js:57-76` has **no caller
     authentication at all** — no session, no signature. Its security property is that Pi's own
     `/payments/<id>` API is the oracle (`pi.getPayment`, `:93`) and the quote is single-use
     (`:167-184`). A fulfil ack cannot borrow that, because it proves nothing about the caller.
     **So define (b)'s bearer explicitly:** the body must carry `paymentId` **and** `txid`, and the
     route flips the row **only if** a `pi_payments` row exists with that `payment_id`, `state =
     'granted'`, that `txid`, and a `player_id` matching the entitlement's `wallet`
     (`complete.js:220-231` writes all four). The `paymentId`+`txid` pair is the unguessable bearer —
     the same trust shape `events/track.js:157-162` reasons about for a guest id. Then perform the
     identical conditional `UPDATE … WHERE status = 'verified'` (`fulfill.js:60-66`).
   **(b) is the smaller, safer change and does not touch the granting allowlist** — but it costs a
   second flip site. Whichever is chosen: **exactly one `UPDATE` statement in the codebase may write
   `status='fulfilled'`; extract it to a shared helper if (b) wins.** Re-inlining it is the duplicated
   state §16 names.
5. Keep the transition **conditional and replay-safe** — `WHERE … AND status = 'verified'`,
   `fulfilled_at = COALESCE(fulfilled_at, NOW())` (`:62-64`) — and keep the
   `logApiEvent(… 'purchase_entitlement_fulfilled' …)` line (`:66`).

### 3.3 Lane B — the client acknowledges (Unity, reaches players on the next WebGL/Pi deploy)

`Assets/_Modules/Core/Payments/Providers/Pi/PiBrowserPaymentProvider.cs`, immediately after `:415`
`PiGrantApplier.ApplyExactlyOnce(...)` returns **true** — and **only** on true: acknowledge the grant to
whichever endpoint Lane A chose, awaited, wrapped in `Guard.Try`, with `?.` on every cross-module call
(§10).
- **A failed ack must NOT fail the purchase and must NOT re-grant.** The pack is already in the save and
  the journal is idempotent (`PiGrantApplier.cs:82` `if (IsApplied(key, sku)) return true;`). The ack is
  a ledger courtesy; the grant is the player-facing truth. Log `FlowTrace.Warn` and move on — never
  throw, never retry-loop, never let it block the close of the purchase UI.
- **Retry on the next launch for free:** Pi's `onIncompletePaymentFound` already re-presents unsettled
  payments (`:555-567`), and `complete.js:84-89` short-circuits a replay to
  `state:'granted', replay:true`. So if the ack is attempted at that point too, a stalled row heals
  itself with no new machinery. **Prefer that to a bespoke poll.** Nothing in the Pi rail should gain a
  timer.
- ⚠ **`PiBrowserPaymentProvider.cs` is the same file WO-1318 built; reuse `PiPaymentEndpoints` for the
  URL** (it already holds the Pi endpoint constants and `TraceSystem`). Do not add a second base-URL
  constant.

### 3.4 Lane C — stop the two surfaces disagreeing (`api/admin/stats.js`, ONE decision)

`?view=purchases` sums `usd_anchor` across every status (§1). **Ruling needed, and it is a one-line
change either way — the implementing seat must ASK, not pick:**
- **(a) Count `verified` as revenue** (the money *has* moved — `complete.js:4-8`: *"The money HAS moved
  by the time this runs"*) and split the tile into `settled` / `of which awaiting fulfilment`. The
  `by_sku` query already computes `awaiting_fulfilment` (`:1020`), so the number is in hand.
- **(b) Count only `fulfilled`** — then today's headline drops from $10.97 to $5.98 and the Pi purchase
  disappears from revenue until Lane A/B land.
**(a) is the honest one** (the money is real; delivery is a separate fact) and it is the smaller edit.
But it changes a number the owner has already seen, so **it is her call.** Do not change the sum
silently.

Also add `pi_payments` to the `?view=overview` probe list (`api/admin/db.js:98-119`) — one line. It is
the Pi rail's own ledger and **no console can currently read it**, which is why this row's
`state='granted'` was invisible.

---

## 4. ACCEPTANCE

### 4.1 First — settle whether THIS player's goods actually landed (read-only, no code)

The grant is client-side, so the only evidence is a client trace. The Pi client is WebGL and `WebTrace`
POSTs under `UNITY_WEBGL`, so the rows exist — but:

> ### ⛔ THE ADMIN VIEW CANNOT REACH THEM, AND THE ROWS ARE DELETED IN ~1 DAY. MEASURED, NOT FEARED.
> - `GET /api/admin/db?view=traces&limit=50` (run this session, HTTP 200) returned 50 sessions spanning
>   **2026-09-15T23:47Z down to only 2026-09-11T02:55Z**, and **zero sessions dated 2026-09-10**. The
>   summary query is `ORDER BY latest DESC LIMIT ${limit}` with **no offset** (`db.js:263-274`, max
>   limit 50 via `clampLimit`), and there are 77 trace sessions after 09-10 (per `?view=metrics`
>   `distinct_trace_sessions`: 7+3+7+2+58). **The 09-10 session is past the end of the list and
>   unreachable through this endpoint.** Do not burn time retrying it.
> - ⏳ **DEADLINE: the rows are purged by the daily cron.** `api/admin/cleanup.js:35` `RETENTION_DAYS =
>   7` and `:81-86` `DELETE … WHERE event_name='web_trace' AND received_at < NOW() - 7 days`;
>   `vercel.json:7` schedules `/api/admin/cleanup` at **`0 4 * * *` (04:00 UTC daily)**. The rows were
>   received 2026-09-10T23:42Z, so they age out at 09-17T23:42Z and **the 2026-09-18 04:00 UTC run
>   deletes them.** After that this question is unanswerable forever.

So the evidence step is **direct SQL by the CLI seat, this week, not an admin-view call**:

```sql
SELECT received_at, properties->>'session' AS session,
       jsonb_array_length(properties->'lines') AS lines, properties->'lines' AS lines_json
FROM analytics_events
WHERE event_name = 'web_trace'
  AND received_at BETWEEN '2026-09-10T23:30:00Z' AND '2026-09-11T00:15:00Z'
ORDER BY received_at ASC;
```

Then grep the returned lines for **`grant ApplyExactlyOnce`** (`PiGrantApplier.cs:66`) and for the
`FlowTrace.Fail` strings at `:70`, `:76-78` and `:86-90`.

Record the verdict as exactly one of three, and **never infer between them**:
- **delivered** (the `ApplyExactlyOnce` scope closed and the pack was owned),
- **refused**, quoting the Fail line — **then it is a real player owed a real pack, and it becomes its
  own ticket** with the owner deciding the re-grant (a write, which `db.js`'s header deliberately keeps
  out of that file),
- **unprovable** — no rows retained / the window closed. That is a legitimate finding to write down
  (§11B A), not a box to tick either way.

### 4.2 Then the fix

1. **The transition happens:** a fresh Pi purchase (or the existing row, via the
   `onIncompletePaymentFound` path) moves `verified → fulfilled` with `fulfilled_at` set.
   Prove it by re-reading `?view=purchases` and pasting the before/after row.
2. **Replay is a no-op:** call the ack endpoint twice; `fulfilled_at` does not move (COALESCE) and the
   response is the same. No second `purchase_entitlement_fulfilled` audit row for the same entitlement.
3. **The Solana rail is untouched:** `TX_SIG_RE` is byte-identical; a Solana fulfil still works
   end-to-end. `MonetizationActivationRegression` (`:89-90`) and `StoreCommerceStateRegression`
   (`:147`) both green — they pin `MarkFulfilledAsync` being called and awaited in `PackStore`.
4. **A forged ack is still refused:** a fulfil for `network='pi'` with a mismatched wallet/sku returns
   409/401 (the `matches()` guard, `fulfill.js:14-16`, and the auth check) — the grant-path auth is not
   widened past the one mode added on purpose.
5. **A failed ack does not cost the player the pack:** simulate the endpoint 500ing; the purchase still
   completes, the pack is owned, and a `FlowTrace.Warn` names it.
6. `COMPILE_GATE_OK` + `tools/gate_brace.py` on every touched `.cs` (§1), then `REGRESSION_OK <n>/<n>`
   on a **fresh** log (judge the marker, never the exit code — memory
   `gates-report-success-without-proving-it`).
7. Board: flip this WO's `**Status:**` and write the `.RESULT.md` in the SAME commit; regenerate
   `BOARD.html`.

## 5. WHAT NOT TO TOUCH

`api/pi/complete.js`'s grant logic, its three idempotency layers or its `'verified'` insert (§3.1) ·
`TX_SIG_RE` itself · `walletAllowed` (§3.2.3) · the `GRANTING_MODES` allowlist beyond the **one**
deliberate entry, if option (a) is chosen · `PiGrantApplier`'s journal or its refusal paths · pack
contents / `packs.json` / pricing · the `usd_anchor` sum until the owner rules on §3.4.

## 6. ⚠ COORDINATION — WO-1796 AND WO-1793

- **WO-1796** (ad revenue visibility) adds `view === 'ads'` to **`api/admin/db.js`** after `:206`
  (settled: `console.js:317-320`'s `getJson` already sends `X-Admin-Key`, so it stays in `db.js`).
  This WO's §3.4 edits the **`view === 'purchases'`** block of `api/admin/stats.js` and adds one
  `pi_payments` probe line to `api/admin/db.js:96-119`.
- **WO-1793** also adds a new read-only view to **`api/admin/db.js`**.
- **Three tickets, two files, disjoint blocks.** Per §11 the lanes must not hold these files
  simultaneously: **run them sequentially, or let ONE seat carry all the `api/admin/*` edits.** The lead
  batch-gates and commits by explicit path.
- Useful precedent: WO-1793's triage read Neon directly with `DATABASE_URL` from `.env.local`,
  SELECT-only. That is the sanctioned route for §4.1's time-boxed query.

---

**Prepared by:** read-only spec/RCA lane, 2026-09-16. The dead step in §1 is proven from code read at
source plus the live row; §2 and §4.1 mark what is *not* proven.

## IMPLEMENTATION RECORD — backend lane, 2026-09-17 (awaiting lead gate + owner ruling on §3.4)

**§3.2.4 AUTH: option (b) chosen — `POST /api/pi/fulfill`.** Option (a) was checked first, as the WO
demands, and it is NOT viable as a small change: `authenticate()`
(`api/_lib/wallet-auth.js:749`) resolves **no Pi session at all** — grepped for `pi`/`isPiId` across
that file, the only `pi`-adjacent hits are comments, and `auth_sessions` is minted solely by
`api/auth/session.js` and `api/auth/google-session.js`. So adding a `pi` key to `GRANTING_MODES`
would have gated on a mode `authenticate()` can never return, i.e. a dead entry plus a widened
allowlist — the worst of both. (b) also bounds the worst case correctly: a forged ack can only mark a
row **the player already paid for** as delivered. It cannot grant, create, refund or move money.
The bearer is stated in full in the new route's header: the `(paymentId, txid)` **pair** must already
exist in our own `pi_payments` with `state='granted'`, and that row's `player_id` must match the
entitlement's `wallet`.

**§3.2.3 `walletAllowed` CONFIRMED, NOT CHANGED — do not re-audit it.** Read at source:
`walletAllowed` opens with `if (network !== 'mainnet-beta') return true;`, so `network='pi'` returns
true on the first line. Untouched.
⚠ **The WO's own cite `purchase-catalog.js:185-196` is STALE — the function is at `:261-272`**
(`:262` is that first line; `:185-196` is the `FLAT_RATE_SOURCE` / `FLAT_ROW_RATE` block). Behaviour as
described, address wrong: re-read it, do not trust the number.

**One writer, as required:** the `UPDATE … SET status='fulfilled'` now lives **only** in the new
`api/_lib/purchase-fulfilment.js` and is called by both rails. A regression case in
`test/pi-fulfill.test.js` walks every `.js` under `api/` and FAILS if a second one appears.

**`TX_SIG_RE` is byte-identical**; the Pi txid is validated by `pi.TXID_RE` through an explicit
`signatureOk(network, signature)` switch. `network='pi'` is accepted at `api/purchases/fulfill.js`'s
payload layer so a stray Pi ack there is refused by the AUTH gate with a truthful `WALLET_REQUIRED`
rather than by a shape check that hides the reason — the allowlist itself gained **zero** entries.

**NO MIGRATION IS NEEDED.** `api/schema.sql:1205` already permits `network='pi'`, `:1215` already
permits `status='fulfilled'`, `fulfilled_at` already exists, and `pi_payments` already carries
`state`/`txid`/`player_id`/`created_at`. Nothing was added to any migration file.

**§3.4 IS NOT IMPLEMENTED, ON PURPOSE.** `api/admin/stats.js` is byte-unchanged: the WO says the
implementing seat must ASK, and the owner has already seen the $10.97 figure. The recommendation
stands as written — (a), split the tile into settled / of which awaiting fulfilment. The
non-decisional half IS done: `pi_payments` now has an `?view=overview` probe in `api/admin/db.js`.
⚠ Per §6 that file is also wanted by WO-1796 / WO-1793 — this is a **one-line addition inside the
existing probe array**, so the lead should stage it by explicit path.

**⛔ §3.3's SELF-HEALING CLAIM IS OVERSTATED — corrected here, and it changes AC 4.2.1.** §3.3 says a
stalled row heals itself because `onIncompletePaymentFound` re-presents the payment and `complete.js`
short-circuits a replay. Read at source: `complete.js:149-164` calls Pi's own
`/payments/<id>/complete` **BEFORE** the entitlement insert, so a payment that reached `/complete`
successfully is `developer_completed` **at Pi** and Pi will **not** re-present it. Entitlement 8's
`pi_payments.state='granted'` proves `/complete` finished, so **that row will NOT flip by itself**, no
matter how many times the Pioneer signs in. The ack in `PiPaymentBootstrap` is kept — it is correct and
free for payments Pi genuinely still lists as incomplete — but it is **not** general self-healing, and a
fresh purchase whose fire-and-forget ack is lost (tab closed / offline / crash) likewise stays
`verified`. **Residual gap, named not hidden:** closing it wants a reconcile sweep over
`pi_payments.state='granted'` rows whose entitlement is still `verified`. That is a WRITE, so it is the
owner's call and a separate ticket, not this lane's.
**Therefore AC 4.2.1 can only be proven by a FRESH Pi purchase after deploy.** Entitlement 8 needs
§4.1's verdict plus a one-time admin decision. Neither is claimed here. The Unity halves are **not** compile-gated by this
lane either — brace + NUL clean on all three `.cs` files (`tools/gate_brace.py`: `bad=0 of 3`), but
`COMPILE_GATE_OK` / `REGRESSION_OK` belong to the lead's single gate.

## Evidence preserved 2026-09-16 (lead)

Read-only SQL pull before the 7-day web_trace cleanup: `logs/evidence/wo1797-pi-purchase-20260910.json` (gitignored, 17 MB): entitlement 8, the `pi_payments` row (state=granted, granted_at 2026-09-10T23:42:54Z), all 30 analytics events for player `pi-a28de6eb-...` on 09-10, and 5310 web_trace rows for 20:00-03:00Z. The buyer timeline shows FIVE quote attempts over 14 minutes with 16 `pi_payment_lookup_failed` events before `pi_payment_approved` at 23:42:41 - the payment flow itself was failing repeatedly before it succeeded; treat that as a second defect inside this ticket (or split it) when implementing.

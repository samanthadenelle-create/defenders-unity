# WO-1841 — Track whether a player ever opened the store

**Status:** DONE — the ticket's premise was wrong, and the lane proved it rather than building a
redundant duplicate. `store_opened` shipped 2026-09-05 (WO-1388, commit `9b47c9ad93`) with a
working admin view (`?view=economy` → `store_funnel`). Real answer already existed: 15 players
opened the store in 7 days, only 2 tapped a pack — the drop is inside the shelf, not at the door.
Delivered value this round: a cross-link pointer between the store-open funnel and the
purchase-quote funnel (deliberately a pointer, never a ratio — the two tables key on different
identities with no bridge). The lead's earlier "confirmed missing" claim to the owner was itself
wrong and has been corrected.

---

## ⛔ FINDING — THIS TICKET'S PREMISE IS FALSE. `store_opened` HAS SHIPPED SINCE 2026-09-05.

The line above ("Confirmed at source: no such event exists anywhere in the client or backend
today ... returned zero hits") is **contradicted at source**: `store_opened` has been in HEAD
since commit `9b47c9ad93`. Read at source this session, twice, by two independent seats:

- **The event exists, with the entry-point property this ticket asks for.**
  `Assets/_Modules/Wallet/PackStore.cs:610` emits
  `EventTracker.Track("store_opened", new { door })` from `TrackStoreOpened()` (`:601`), whose
  only caller is `OnEnable()` (`:563`) — the moment the screen becomes visible. The `door`
  string is already resolved from the named latch or inferred as
  `shortfall` / `manage` / `hud-card` (`:590-592`, `:604-607`).
- **One emit site per artifact, so it cannot double-count.**
  `Assets/_Modules/GooglePlay/GooglePlayStorefront.cs:108` is the `GOOGLE_PLAY` mirror. The two
  asmdefs carry `defineConstraints` `["!GOOGLE_PLAY"]` and `["GOOGLE_PLAY"]`, so they are never
  both compiled. The WO-1388 header at `PackStore.cs:580-587` states this contract explicitly.
- **Both landed in commit `9b47c9ad93` (2026-09-05) as WO-1388**, which built this exact
  four-step store funnel. `git blame -L 600,625` on `PackStore.cs` names that commit.
- **The admin surface exists too.** `api/admin/stats.js?view=economy` has rendered it as
  `store_funnel` since the same commit (`:820-867`, `:910-918`): 7d/30d event + distinct-player
  counts for all six steps, every step reported even at zero, plus a `door` breakdown.
- **It is firing on real devices.** `Logs/f8-inbox/device/SM02G4061955851/flags-20260917/logcat-after-372984.txt`,
  `09-17 10:26:46.393` — `[Flow:Store]   funnel store_opened door=hud-card (inferred)`.
  Older hits in `Logs/device/owner-fireball-20260916/logcat.txt` (09-16 19:39:51, `hud-card`)
  and `Logs/debug/wallet-session-2026-09-06.log` (09-06, `manage`).

**So NO new event was emitted and NO new admin view was added.** Either would have been a
defect: a second `store_opened` site double-counts step 1 of a live funnel, and a second view
duplicates one that already works. Scope items 1-3 of this ticket are **already delivered**.

### Proven against LIVE production data, read-only (the ticket's own AC)

`GET https://defenders-of-the-realm-v2.vercel.app/api/admin/stats?view=economy`, HTTP 200,
10429 bytes, admin key from `.env.local` (no hand-connection to Neon — that deviation is on
record in `test/admin.events.view.test.js:12-14`):

| step | 7d events | 7d players | 30d events | 30d players | latest |
|---|---|---|---|---|---|
| `store_opened` | **79** | **15** | 106 | 16 | 2026-09-17T19:29:43Z |
| `bundle_viewed` | 984 | 15 | 3016 | 23 | 2026-09-17T19:29:54Z |
| `pack_tapped` | 55 | **2** | 67 | 3 | 2026-09-17T16:05:25Z |
| `checkout_started` | 5 | 1 | 9 | 2 | 2026-09-10T23:42:42Z |
| `checkout_failed` | 2 | 1 | 3 | 1 | 2026-09-10T23:35:58Z |
| `purchase_completed` | 0 | 0 | 3 | 1 | 2026-08-25T02:45:30Z |

Doors, 7d: `hud-card` 59, `manage` 14, `settings` 3, `vendor` 3.

And `?view=purchases&days=7` (HTTP 200, 6923 bytes, `errors: []`) already answers the
price-quote half: `issued=12, consumed=1, expired_unconsumed=11, live=0, wallets_quoted=2,
wallets_that_paid=1, consumed_pct=8.3`, last issued 2026-09-15T23:45:53Z.

**The owner's question therefore already has an answer, and it is not the one the ticket
assumed.** 15 players opened the store in 7 days and only **2** tapped a pack. The drop is at
`store_opened -> pack_tapped`, i.e. inside the shelf, not at the door.

### What WAS genuinely missing — and the one change made

The two halves lived in two views with **no pointer between them**. An operator reading
`issued=12` could not tell "nobody opened the store" from "they opened it and never reached the
till" without already knowing the other view existed.

Changed, in `api/admin/stats.js` only:
- `store_funnel.quote_step` — names `?view=purchases -> quote_funnel`, its source table, and the
  refusal below.
- `quote_funnel.preceding_step` — the reverse pointer back to `?view=economy -> store_funnel`.

**⛔ A POINTER, NOT A RATIO — and that is the load-bearing decision.** `analytics_events` keys on
`player_id`; `purchase_quotes` keys on `wallet` (`api/schema.sql:1395`). No bridge exists, so
there is no per-player opened-to-quoted conversion to compute, and a percentage across two
different denominators would *read* as a conversion rate while being an artifact. `stats.js`
already states its own contract for this — client-reported intent and server settlement "are
never blended" (`:895-899`) — so both new keys declare `joinable_to_store_opened: false` and say
why, and a source-level lint now fails if any lane adds such a ratio.

**One more fact that had to be read or the step would be meaningless:** opening the store does
**not** mint a quote row. `PackStore.Open` calls `RefreshQuotedPrices` (`PackStore.cs:571`),
which hits the LIST mode of `api/purchases/quote.js` — *"binds nothing, persists nothing"*
(`quote.js:353`). The `INSERT`s (`quote.js:471`, `:519`) fire only on a single-SKU quote. Had the
opposite been true, `issued` would be roughly opens x packs and the requested "drop-off" would
have been noise.

### Files touched
- `api/admin/stats.js` — the two cross-link keys (both blocks tagged `WO-1841`).
- `test/admin.store-open-quote-funnel.test.js` — NEW oracle, 6 cases, **6/6 pass**.
  **Proven RED**: with `stats.js` at HEAD the run is `pass 3 / fail 3`, failing exactly the three
  cross-link cases. Cases 1, 2 and 6 pass in both states, each for a reason written in the file —
  notably case 2, whose passing red run *is* the evidence for this finding.

**Zero Unity C# touched. No Unity batchmode / CompileGate / DataRegression run by this lane.**

`node --test test/*.test.js`: **836 pass / 2 fail before, 841 pass / 3 fail after.** The +5 are
accounted for exactly: 836 + 6 (this lane's new cases) - 1 = 841. The -1 and the third failure
are the same row, and it is **not mine**: `command-center.test.js:880` asserts
`how_sessions_end: 'THEY DO NOT` while the concurrent **WO-1842** lane has deliberately reworded
that value in the shared working tree (HEAD carries the old string, the worktree the new one).
That assertion is WO-1842's to update.

### ✅ RESOLVED — the counts above were refreshed once the file parsed again

The lead fixed the WO-1843 breakage described below (removed the backticks from the SQL comment).
Re-verified here rather than taken as a claim: `node --check api/admin/stats.js` → clean, both
WO-1841 keys still present, and this lane's oracle back to **6/6**.

Refreshed full suite: **855 pass / 4 unique failures.** None are this lane's. Two are the
long-standing heartbound pair (`the streak reaches heartbound_state`, `no client-readable file
under Assets/ carries a Heartbound tier NAME`), and two belong to the concurrent **WO-1842**
lane's own in-flight oracle `test/admin.playtime.buckets.test.js` (`every playtime statement is a
SELECT with a hard LIMIT`, `the event names are BOUND parameters, never SQL text`) — that file
contains **zero** references to `quote_step` / `preceding_step`, so it cannot be reacting to this
lane. WO-1842 also updated `command-center.test.js:880`, so the third failure named below has
cleared on its own.

The historical record of the breakage is kept below, because the sequencing is the useful part.

---

⛔ **AND THAT COUNT COULD NOT BE REFRESHED AT THE TIME — `api/admin/stats.js` DID NOT PARSE.**
Measured after the numbers above were taken: `node --check api/admin/stats.js` failed at `:726`.
A **WO-1843** lane (its own comment names it) added a backtick-quoted `` `slice` `` inside a SQL
`--` comment inside a tagged template literal, and the backtick terminates the literal. Its hunk
is `@@ -498,3 +720,18 @@`; WO-1841's two hunks are at `+1158` and `+1475`. Every test that loads
`stats.js` and every admin view goes down with it, including this lane's oracle (it read 6/6
GREEN before that hunk landed, and reads 1/6 after, all five failures being the same
`SyntaxError`). **This lane deliberately did not touch it** — that region is being actively
written by another lane, and editing it would repeat the over-reach disclosed below. It is one
character class to fix and it belongs to WO-1843.

⚠ **One disclosure for the lead.** `api/admin/stats.js` already carried a large uncommitted
WO-1842 diff when this lane edited it (356 insertions total; only 2 hunks are WO-1841's). To
prove RED, this lane briefly restored the file to HEAD and restored it from an md5-verified
backup ~35 s later. Their work survived intact — verified after the fact: the non-HEAD reworded
`how_sessions_end: 'FOR THIS FIGURE` value and all six of their marker strings are present in
the worktree now, and HEAD does not contain them. **It was still the wrong move on a shared
tree**, and a future red-proof on a co-edited file should copy the file aside and test the copy.

### Recommendation to the lead
Close WO-1841 as **already delivered by WO-1388**, keeping the two cross-link keys and the new
oracle as the residual value. The real follow-up the production numbers point at is a *new*
ticket: **15 opens -> 2 pack taps.** That is a shelf problem, and no amount of extra funnel
instrumentation will move it.

---

## Owner ask

The owner wants to know whether a player ever opened the store screen at all, separate from
whether they got as far as requesting a price quote or completing a purchase. Confirmed at
source: no such event exists anywhere in the client or backend today (`grep -rln` across
`Assets/` and `api/` for store-open/view tracking returned zero hits).

## Scope

1. Find the store screen's open call site(s) — likely `RealmStorePanel`/`PackStore`-adjacent UI
   under `Assets/_Modules/Village/Monetization` or `Assets/_Modules/Village/UI` (confirm the exact
   class before assuming a name — this WO deliberately does not name one, per CLAUDE.md's
   never-guess rule).
2. Emit a new analytics event (e.g. `store_opened`) the first time the store screen becomes
   visible in a session, following the existing `EventTracker`/`RaidFunnel`-style pattern already
   used elsewhere (see `Assets/_Modules/Core/Analytics/RaidFunnel.cs` for the shape: a static
   method, a FlowTrace line, a documented property bag).
3. Include enough context to be useful for a funnel: at minimum, which entry point opened it (HUD
   button, night market card, etc.) if that's cheaply available, and the existing identity/session
   fields every event already carries automatically via `EventTracker`.
4. Add an admin-readable surface for this — either a new `view=` on `api/admin/db.js` or an
   addition to the existing funnel view (WO-1793 already added `view=funnel`), showing store-open
   counts and, ideally, the drop-off between "opened the store" and "requested a price quote"
   (`purchase_quotes` already exists as a signal for the latter).

## What NOT to do

- Do not invent a "server-minted" identity scheme — this event rides on the exact same identity
  system every other event already uses (session/guest/guest-body/unverified).
- Do not touch the existing purchase-quote/verify/fulfill pipeline — this is a new, earlier funnel
  step layered on top of it, not a replacement for anything.

## Acceptance criteria

- [ ] Brace balance + NUL check pass on every touched `.cs`.
- [ ] `COMPILE_GATE_OK` + `REGRESSION_OK n/n` on a fresh log (if any Unity C# is touched).
- [ ] If backend files are touched, `node --test test/*.test.js` before/after counts reported, new
  tests added for the new admin view.
- [ ] A device or headless capture confirms the event actually fires when the store opens.
- [ ] The admin view showing store-open counts is proven against a real (or realistic seeded) data
  read, not just unit-tested in isolation — this project's standing rule is proving admin views
  against live/production-shaped data wherever safely possible (read-only).

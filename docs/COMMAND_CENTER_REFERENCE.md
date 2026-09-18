# Command Center — Reference

**Source-verified 2026-09-18**, read at source in this session against
`api/admin/{console,stats,ops,promo-bind,db,cleanup,schema-shape,showcase-finalize,showcase-reverse,google-play-voided-reconcile}.js`
and `api/_lib/{tunable-manifest,ops,purchase-catalog,sku-catalog,store-sale,maintenance,tunables}.js`,
plus `vercel.json` and `git log` for the dated claims.

Not auto-generated. Treat it the way this project treats any hand-maintained reference: verify against
the live file before trusting a specific claim for anything consequential. Where a work order's name
suggested a feature that isn't actually live, that is called out rather than assumed.

> ### ⚠ HOW TO READ THE NUMBERS IN THIS DOCUMENT
> Some of the most useful sentences in the code are **point-in-time measurements a previous session took
> against the live database** ("4 settled entitlements", "$10.97 all-time", "33,198 error rows", "zero rows
> carry a locale key"). Those strings are genuinely present in the source — that much is verified here — but
> **this document has no database access and cannot re-verify any of them.** Every such figure below is
> labelled `MEASURED <date>, NOT RE-VERIFIED`. Read those as **illustrative and historical**: they tell you
> the shape of the problem and why a guard exists. They do **not** tell you today's value. Re-run the query
> if the number matters.

---

## On this page

- [Hard rules this console is built around](#hard-rules)
- [Getting in](#getting-in) · [Auth model](#auth-model) · [Landing structure](#landing-structure)
- Tabs: [Decisions](#tab-decisions) · [Balance](#tab-balance) · [Players](#tab-players) · [Toggles](#tab-toggles) · [Money](#tab-money) · [Player issues](#tab-player-issues) · [Promos](#tab-promos) · [SKUs](#tab-skus) · [Tickets](#tab-tickets)
- Read views no console tab fetches: [stats.js](#unfetched-stats-views) · [db.js](#dbjs)
- [Other admin endpoints (not console tabs)](#other-endpoints)
- [The site-wide sale knob](#store-sale) — the gap the previous revision flagged, now closed
- [Endpoint map](#endpoint-map) · [Silent failure modes](#silent-failure-modes) · [On-screen state words](#state-words)
- [Known gaps, named by the code](#known-gaps) · [How it's built](#how-its-built) · [Where the code lives](#where-the-code-lives)

---

<a id="hard-rules"></a>
## ⚠ Hard rules this console is built around

- **No PII, ever.** No wallet address, email or real player name is rendered anywhere on the page.
  Player identifiers arrive masked `first4…last4` (`stats.js` `maskId`) with a stable 12-hex SHA-256
  `player_ref` for drill-down. Promo-code wallet bindings arrive as `is_bound` (a boolean); the
  `bound_wallet` column is never selected. Bug reports show `verified` / `unverified` identity, never the
  account. **The one documented exception** is `?view=players&player=<id>` / `&ref=<12hex>` on the read
  endpoint, which returns the full id for a single explicitly-requested player so an operator can bind a
  promo code or answer a ticket — never a bulk dump.
- **Colour is never the signal.** The owner is red/green colourblind. Every state is a word.
- **A failed read is never rendered as zero.** `stats.js`'s `command` view returns `read_ok: false` and
  `state: 'error'` per block; `console.js` renders the state and prints `COULD NOT READ`, never a bare
  number. The code comments call the alternative a "confident lie."
- **Neither key is persisted.** Two tab-lifetime JavaScript variables (`READ_KEY`, `OPS_KEY`) — never
  `localStorage`, `sessionStorage`, a cookie, or a URL parameter. Reloading asks again, by design.
- **7-bit ASCII, enforced at serve time.** The manifest JSON is scanned byte-by-byte at module load
  (`console.js`, `MANIFEST_JSON`): any char `> 126` or `< 32` **throws before the page can serve**.
  Pinned by `test/command-center.test.js`.
- **The read/write boundary is at the endpoint, not the UI.** `stats.js` and `db.js` are SELECT-only by
  construction — every statement a SELECT with a hard LIMIT. Every write lives in `ops.js` (behind a
  second key), `promo-bind.js`, or one of the two showcase endpoints.
- **`properties` is never returned wholesale.** It is free-form client-authored JSONB; each view pulls
  the named keys it needs and nothing else.

---

<a id="getting-in"></a>
## Getting in

The console page itself (`api/admin/console.js`'s served HTML) is **not** key-gated — it can't be,
because a browser navigation cannot carry an `X-Admin-Key` header, and putting the key in the URL would
write it into history, the address bar, referrers and every log on the way.

So the shell is public, carries **zero data**, and is served `noindex, nofollow` +
`Cache-Control: no-store` + `nosniff` + `Referrer-Policy: no-referrer`. It becomes useful only once a
key is typed into the in-page gate. The gate validates by issuing
`GET /api/admin/stats?view=ops&days=30` and requiring HTTP 200; on refusal it prints the reason and
discards the key.

The console page and the game site are on **different Vercel projects**. A console opened on the wrong
host 404s and no function in `ops.js` ever runs — which is the fourth of the four failure modes the
refusal logging exists to distinguish.

---

<a id="auth-model"></a>
## Auth model

Three auth shapes.

| Shape | Required headers / values | Used by |
|---|---|---|
| **Read** | `X-Admin-Key` matches `ADMIN_DASH_KEY` | `stats.js`, `db.js`, `schema-shape.js` |
| **Write** | `X-Admin-Key` **and** `X-Admin-Ops-Key` matches `ADMIN_OPS_KEY` | `ops.js`, `promo-bind.js`, `showcase-finalize.js`, `showcase-reverse.js` |
| **Cron-or-admin** | `Authorization: Bearer <CRON_SECRET>` **or** `X-Admin-Key` | `cleanup.js`, `google-play-voided-reconcile.js` |

**The constant-time compare is not identical across all of them, and the difference is real.**
`db.js`, `stats.js`, `_lib/ops.js` (`keyOk`), `cleanup.js` and `google-play-voided-reconcile.js` all
SHA-256 **both sides first**, which makes `timingSafeEqual` usable on unequal lengths without leaking
length. ⚠ `schema-shape.js` does **not**: it compares raw `Buffer`s and returns `false` early on a
length mismatch (`schema-shape.js:35-41`). It is also **the one admin endpoint that answers 401** —
every other one here is pinned to `200 | 400 | 500` as a project constraint, so a refused read on
`stats.js` / `db.js` / `ops.js` is an HTTP **400**, not a 401. Do not "fix" a 400 you see there.

**Why a second key for writes** (`ops.js` header, verbatim in spirit): the read key is typed into a
phone browser, in a hurry, in public, and ends up in screenshots. That is acceptable for a read
surface. It is not acceptable for a surface that can seal the whole game or mint free currency. A
leaked read key buys a reader exactly nothing more than reading.

**Fail-closed on writes, fail-open on maintenance.** `ops.js` states this explicitly:
`api/_lib/maintenance.js` must leave the game **open** if its table is unreadable, because a player
must never lose their session to our outage. `ops.js` must **refuse** if `ADMIN_OPS_KEY` is unset
(`OPS_WRITE_NOT_CONFIGURED`, returned with the remedy in words), because "we could not check who you
are" can never resolve to "go ahead and change the money tables." Availability there; correctness here.

**Refusals are logged.** Both `ops.js` (`logRefusal`) and `stats.js` (`refuse`) write a single
`[ops-refusal]` line on every refusal: booleans and stable machine codes only — never a key, never a
header value, **not even a length** (a length narrows a brute force and buys no diagnosis a boolean does
not already give). Pinned by `test/command-center.refusal-logging.test.js`. The absence of a line is
itself the diagnosis for "the console never reached this deployment."

**No CORS on the write endpoints.** `ops.js` and `cleanup.js` deliberately set no
`Access-Control-Allow-*`; the console is same-origin with `ops.js`. `stats.js` and `db.js` **do** set
`Access-Control-Allow-Origin: *` with `GET, OPTIONS`, because `site/admin.html` and the local
`tools/db-viewer/index.html` are always cross-origin callers. The key is still required.

---

<a id="landing-structure"></a>
## Landing structure

On open the console shows a primary nav with three items: **Decisions** (opens by default),
**Balance**, and a **More tools** disclosure button. Tapping "More tools" reveals a second row of
**seven** older, more granular tabs: Players, Toggles, Money, Player issues, Promos, SKUs, Tickets.
(A stale comment in `console.js:268` says "six older tabs"; the nav markup carries seven.)

This two-tier layout (WO-1281, landed 2026-08-30) was a deliberate reorganisation — the seven older
tabs still exist and do exactly what they always did; the change was to put a synthesised "what do I
need to decide today" view in front of them rather than removing anything.

A top bar picks a reporting window (7 / 30 / 90 days, default 30) and refreshes manually.

**⚠ There is ONE `load()` and it fires seven reads on every refresh, not per tab.** `console.js`
`load()` issues, in parallel: `stats?view=overview&days=N`, `stats?view=ops&days=N`,
`stats?view=purchases&days=N`, `stats?view=command&days=N`, `client-tunables?fresh=<ts>`,
`stats?view=skus` (no `&days` — a catalog has no window), and `db?view=ads&days=N`. Changing the
window or tapping Refresh re-runs all seven. So the per-tab "Endpoints" boxes below say which reads a
tab *renders*, not which requests it causes.

---

<a id="tab-decisions"></a>
## Tab: Decisions — default landing tab

```
Operator question   What do I need to decide today?
Renders             stats?view=command&days=N   (the whole surface)
                    db?view=ads&days=N          (the ad-income tiles inside Sales)
Writes              -
WO                  WO-1281 (the surface), WO-1796 (ad tiles), WO-1842 (the session-length correction)
Window              Honors the 7 / 30 / 90-day selector
```

**⚠ FOUR collapsible areas, FIVE questions.** The areas are **Sales, Retention, Progression,
Diagnostics** — `renderCommand()` composes exactly those four, one open at a time so the page stays
scannable on a phone; opening one auto-closes the other. The fifth question the surface answers —
**average online time** — is not a fifth area: it is an `<h3>` block inside **Retention's** expanded
detail. `stats.js`'s `command` block lists the five questions in its own header and its `purpose`
string; `console.js:418` lists them too and numbers only four, because the fifth lives inside the
second. Do not go looking for a fifth accordion.

Any area can independently show `COULD NOT READ` if its backing query failed. Each probe is fetched and
evaluated separately; `command` returns an `errors` array naming the probes that failed, and the page
prints "N of the queries behind this page FAILED" plus the probe names, with the dependent area showing
no figure.

### 1. Sales — "What is selling?"

- Headline tiles: settled revenue for **30 days / 7 days / today**, each with unit count and a trend
  word against the prior equal window. ⚠ **Buyer count appears on the 30-day tile only**
  (`console.js:582-585`) — the 7-day and today tiles print units and the trend word.
- Trend vocabulary, from `trendWord()`: `GROWING` (>+10%), `SHRINKING` (<−10%), `FLAT`,
  `TOO FEW TO CALL` (current+prior below the 10 threshold), `FIRST PLAYERS` (prior was zero),
  `NO DATA`.
- If nothing has ever settled, `state: 'empty'` and the area prints
  `NO PURCHASE HAS EVER SETTLED` plus an `empty_meaning` sentence — an empty sales area is the correct
  reading of the data, not a broken query.
- **Sales authority is the server, always.** `sales.authority = 'server'`;
  `backing: 'purchase_entitlements (settled, server-verified on chain) + purchase_quotes'`. The client's
  `purchase_completed` event appears in exactly one place — the disagreement count — and is labelled as
  the alert it is.
- **Ad income sits directly under the pack tiles, unexpanded** (WO-1796), a direct response to the
  owner's words quoted in the code: *"noone is ever buying a pack and I cant figure out how to see if
  ads are making anything."*
  - Ad revenue prints to **four decimal places** (`usd4()`), not two. The code's reason:
    2dp would render a real fraction-of-a-cent day as `$0.00`, indistinguishable from earning nothing —
    the same class of lie as rendering a failed query as zero. *(The illustrative figures in that
    comment — a measured month of $0.19, good days near $0.02 — are `MEASURED (undated in-code), NOT
    RE-VERIFIED`.)*
  - `impressions_without_revenue` is counted and returned alongside the sum, never folded in as zero:
    "the network reported nothing" and "the network reported $0.00" are different facts.
  - **eCPM below 20 impressions returns `null` and `low_n: true`** (`ECPM_MIN_IMPRESSIONS = 20`,
    `db.js:339`); the surface prints "too few impressions to trust" in words.
- Expanded detail: window-over-window trend table; all-time settled total, buyer count and first-sale
  timestamp; the full ad-revenue breakdown (by network / by day / by format+placement, plus
  rewarded-ad completions by provider and outcome); first-time-vs-repeat buyer split; the
  quote-to-settle funnel (issued / paid with `consumed_pct` / expired-unpaid, `low_n` under 10); the
  live client-vs-server disagreement count with a pointer to the Money tab; and the **full sellable-SKU
  roster** with per-SKU units in window, units all time, `usd_all`, last sale, and a
  `SELLING` / `NEVER SOLD` state word.
- The roster is built from **`USD_ANCHORS`** in `api/_lib/purchase-catalog.js`, not from sales rows, so
  a SKU that has sold nothing still appears. The stated reason: a SKU missing from a sales table and a
  SKU that does not exist look identical, and those are very different findings.
- If `rows_without_usd_anchor > 0` the page says the all-time value **understates the row count** and
  does not mean those sales were free (the two CANARY skus carry no anchor).
- **Pushing or featuring a SKU is explicitly `NOT INSTRUMENTED`.** `push_a_sku` returns
  `state: 'not_instrumented'`, `supported: false`, a `reason` and a `needed`. The reason, read at
  source: the shelf flag the client honours is `storeVisible` inside the **packaged** `packs.json`
  (`PackCatalog` reads it from Resources/StreamingAssets, never over the network); the `packs` table in
  Neon does carry a `store_visible` column but nothing in `api/` or the client reads it. No button is
  offered on purpose — a control that changes a DB column no shipped client reads would look like it
  worked and do nothing.

### 2. Retention — "Do players come back, and for how long?"

- Headline: next-day return rate, 7-day-still-here rate, and active-player count for the window with a
  trend word.
- Retention, growth and churn are computed over the **`QUALIFYING_PLAY_EVENTS`** allowlist — the player
  must have **done** something. The eleven qualifying events, read at source:
  `wave_completed`, `tutorial_step_complete`, `tutorial_step_skip`, `tutorial_completed`,
  `tutorial_skipped_all`, `promo_redeemed`, `referral_code_generated`, `referral_shared`,
  `referral_claimed`, `purchase_completed`, `rewarded_ad_completed`.
  Both allowlists are returned on the response (`qualifying_play.counts_as_play` /
  `.does_not_count`) and printed on the page.
- ⚠ `tutorial_started`, `tutorial_step_enter` and `contextual_step_enter` are excluded **on purpose**:
  they fire when the flow *arrives* at a step, which for a fresh install is a consequence of booting.
  `session_start`, `session_heartbeat`, `session_end`, the store-funnel events, `playtest_break`,
  `maintenance_refusal` and `admin_ops_write` are likewise in `NOT_PLAY_EVENTS`.
- **Cohort day is the day of a player's first QUALIFYING PLAY, not their first boot.**

Expanded detail:

- **Average online time** — median / mean / p90, tagged `ESTIMATE` (the block returns
  `instrumented: false`, `estimated: true`). It is the **gap-based** figure: the span between a
  player's acts, cut wherever they went quiet for **30 minutes** (`SESSION_GAP_MINUTES`). A session
  carrying one event has no span and is counted as **unmeasurable, not zero seconds**, and kept out of
  both statistics. `scan_truncated` fires at the **200,000-event** ceiling (`SESSION_SCAN_CAP`) and the
  page says the sample is the most recent slice of the window, not all of it.
  - ⛔ The WO-1842 `session_heartbeat` / `session_end` rows are **excluded** from this scan
    (`SESSION_DURATION_EVENTS`). Leaving them in would have converted the estimate into the measured
    foreground figure **while keeping the estimate's label** — the same number twice, one of them
    mislabelled.
  - `how_sessions_end` on the response carries the correction in full: the sentence that used to read
    *"there is NO session_end anywhere in the client"* was true until 2026-09-17 and is now false; it
    was corrected in the same change that falsified it. `measured_alternative: '?view=playtime'`.
- Full **D1 / D7 / D30** return-rate table with a per-row confidence word:
  `no cohort has aged this far` (cohort 0) / `too few to trust` (`low_n`) / `usable`.
  `LOW_N_THRESHOLD = 10`, stated on the response.
- New-vs-returning **growth** comparison, each figure against the immediately preceding equal window,
  each verdict a `trendWord`.
- **Three-way churn breakdown**, each with its precise definition printed on the page:
  - `one session only` — played once and nothing in the 24 h after; only players whose first play was
    more than 24 h ago are judged.
  - `tried and left` — no qualifying play within seven days of their first; only players older than
    seven days are judged.
  - `returned but stalled` — came back on a second day but has never cleared a wave or finished the
    tutorial. Carries an explicit `approximation` string: the ticket asked for "gained no XP in the
    window", the database holds only a **current** hero level and no history, so finished-milestone
    absence is the honest stand-in and is labelled as one.
  - `never_claims_deletion`, printed verbatim: *"These are INACTIVITY cohorts. Android, Solana and Pi
    give us no per-player uninstall fact, so nothing here says a player deleted the app."*
- **"Where they stopped"** (`early_exit_steps`) — the last thing each now-quiet player did before going
  silent for 7+ days. `session_start` **and** the WO-1842 heartbeat/end rows are excluded, so the entry
  names an act, not an arrival or a departure bookkeeping row.

### 3. Progression — "Are returning players levelling up?"

- Headline: median hero level (with the highest seen), percent of saves past level 1, median best wave.
- Backing is **`player_data.game_state`** — the save the client uploads. `heroLevel` has been persisted
  since **SaveSchema v29**, alongside `bestWave`, `wavesCompleted` and `baseLayout`. Server-persisted
  state, not a telemetry estimate.
- **Every JSONB read is regex-guarded before its cast** (`~ '^[0-9]+(\.[0-9]+)?$'`, and
  `jsonb_typeof(... ) = 'array'` for `baseLayout`). `game_state` is client-authored; one malformed
  value in an unguarded `::numeric` fails the whole query, which is how a real metric turns into a
  blank card.
- A **coverage** block travels with the figure: saves on file, saves updated in the window, how many
  carry a hero level / a best wave / a town layout, `hero_level_pct`, and the last save timestamp. Its
  `note` explains why: *"A median over three of nine hundred saves is not a fact about the playerbase."*
  ⚠ "three of nine hundred" is an **illustration written into the code's own note**, not a measurement
  of this deployment.
- Hero-level distribution in four bands: `Level 1`, `Level 2 to 4`, `Level 5 to 9`, `Level 10 and up`.
- Wave and building stats keep **persisted state** and **event volume** apart and never add them:
  `median_best_wave` is from the save; `clear_events_in_window` is `wave_completed` volume and is
  labelled `EVENT VOLUME` on the tile. Structures placed = the length of `baseLayout`.
- An explicit **"What this area cannot answer"** list, returned as `progression.gaps`:
  1. **XP gained over time** — not answerable. `player_data` holds a current `heroLevel` with no source;
     it would need a progression snapshot table or a `hero_level_up` event, neither of which exists.
  2. **Dungeon entries and completions** — not instrumented. No dungeon event is emitted, and
     `dungeon_status` is a per-dungeon seal setting, not per-player play.
  3. **Structure and tower upgrades** — not separable. `baseLayout` gives a placement count; no upgrade
     event and no per-level history.
  4. **Time to first build / first clear / first purchase** — only the tutorial leg is answerable, from
     `tutorial_step` timings on `?view=funnel`.

### 4. Diagnostics — "Is the telemetry itself healthy?"

- Headline: percent of events arriving with an identified player, and the raw anonymous event count.
- The block states that every player with no bound wallet shares the single id `"anonymous"`
  (`EventTracker.cs:168`), so anonymous volume can never be split into people, and that *"a large
  anonymous share means this surface describes a MINORITY of the playerbase."*
- Expanded detail: the **excluded-id count** (`excluded_id_count`, `configured`, `source`) — operator
  and test traffic is filtered server-side from `ANALYTICS_EXCLUDED_PLAYER_IDS` in the deployment
  environment, **never from a query parameter**, so a caller cannot widen or narrow it; the **count is
  published, the ids are not**, because publishing them on a screenshot-prone page would put operator
  wallets on it. Plus oldest/newest event timestamps and a full breakdown of every distinct event name
  received in the window, with count / distinct ids / latest — *"the sanity check that a metric reads
  zero because nobody did it, not because the event never fires."*

---

<a id="tab-balance"></a>
## Tab: Balance

```
Operator question   Can I tune this without a rocket scientist?
Renders             the manifest inlined into the page at serve time
                    client-tunables?fresh=<ts>   (public, NO admin key sent)
Writes              POST ops -> tunable.set / tunable.clear   (client_tunables overrides)
WO                  WO-1328, WO-1348, PROD-022
Window              -
```

Origin, quoted from the code comment: owner, 2026-09-02 — *"should be in command center so you dont
need to be a rocket scientist... they can have a simple UI that rives a json,"* in the same breath as
*"i have been screaming this for months."*

**Purpose.** Override specific gameplay-tuning numbers on the live, already-shipped build without a new
release — a `client_tunables` table the game polls. Propagation is **about 40 seconds** (10 s edge cache
+ 30 s client poll), stated on the page and again in every confirm dialog and in `ops.js`'s response
`note`.

**The override table is read from the game's own public endpoint.** `loadTunables()` fetches
`/api/client-tunables?fresh=<Date.now()>` with `cache: 'no-store'` and **sends no admin key** — the
endpoint is public and unauthenticated by design, because it must resolve before sign-in, so attaching a
secret to it would spend the key for nothing. **The cache-buster is load-bearing, not superstition:**
the endpoint carries a 10 s edge cache, and without a fresh URL a read straight after a write would show
the OLD value and the owner would think the write failed. Deliberately the same endpoint the game reads,
so what the page shows is what the client is actually being told.

The tab is driven entirely by a generated manifest (`api/_lib/tunable-manifest.js`, spine generated from
the Unity project's own `DeNelle.Core.Ops.RemoteTunables.Registry` by
`tools/gen-tunable-manifest.mjs`), so the console **can never show a knob the build doesn't have, and
can never hide one the build does have**. `build()` joins three sources and refuses to invent: the
build's key/kind/default, the `TUNABLE_KEYS` write allowlist in `_lib/tunables.js`, and the
hand-authored presentation. A knob present in one source and absent from another is **dropped from the
areas and named in `defects`**. Adding a lever is a data edit plus a manifest regeneration, never a UI
edit here.

**Four areas** (`AREAS`, in the order the owner named them):

| Area | What it covers |
|---|---|
| `skills` | How strong a hero ability is, and how fast it comes back. |
| `tiers` | How much a skill or a structure gains per level or rank. |
| `spells` | What a cast does and how it looks: damage, healing, drain, and spell visuals. |
| `misc` | Everything else that ships as a number — the raid reward curve you move by feel, and the loading/retry/trace levers you only touch chasing a bug. Each card says which it is. |

An area with no knobs still renders, printing that adding one is a data edit rather than a UI change.

**Every knob card shows, in words, never in colour:**

- the current effective value;
- the value the **installed build** ships with (its compiled-in default);
- either **`OVERRIDDEN (the installed game ships with <x>)`** or
  **`Shipped default - nothing is overriding it`**;
- if the override table could not be read: **`unknown`** plus
  *"COULD NOT READ the override table - this is NOT proof the knob is at its default"*;
- if the stored override is unparseable: **`OVERRIDDEN with a value the game cannot read, so the game
  is using <shipped>. Reset it.`**

**Three knob shapes:**

1. **Numeric** (most knobs) — a `−` / `+` stepper (step size **1, or 5 when `max − min > 100`**), a
   direct-entry field bounded to `[min, max]`, **Save this value** (`tunable.set`) and **Reset to
   shipped** (`tunable.clear`). The page refuses a non-integer or an out-of-range value client-side
   before any request.
2. **Boolean** — `Turn ON` / `Turn OFF` buttons plus a Reset.
3. **Named option** — a dropdown of options the manifest already knows are shipped in the build.
   ⚠ **There are FOUR such cards, not one**, all in the `spells` area (WO-1348):
   `realm.vfx.atfootprintoftree_Aura`, `realm.vfx.atfootprintoftree_Impact`,
   `realm.vfx.EliteDeath_Impact`, `realm.vfx.BossDeath_Impact`. They share one control, one option pool
   and one timing sentence.
   - The pool is generated by `tools/gen-vfx-pick-options.mjs` into two byte-identical copies (one under
     `api/_lib/`, one under `Assets/Resources/VFX/`, pinned together by `test/vfx-pick-options.test.js`).
     Options that are `retired` or `unresolved` in **this** build are **held back, never shown greyed**,
     because offering an unresolved option would let the owner pick an effect that renders nothing with
     no error on screen — CLAUDE.md §16's lesson, and the picker must not be able to reproduce it. Ids
     are never reused.
   - Saving takes effect **on the next town load**, not immediately, and the confirm dialog says so
     before writing, because "I changed it and nothing happened" is the failure mode the feature exists
     to avoid.
   - An id the pool does not carry stays **selectable and visible** and says in words:
     `Number N - NOT IN THIS BUILD, so the game is using the shipped effect`. Option `0` always reads
     *"The effect the game ships with."*

**⛔ `tunable.clear` is not `tunable.set 0`.** Clearing deletes the override row so the knob answers the
build's own compiled default. The page says this at the top of the tab
(`notices.clearIsNotZero`: *"Resetting the Pi Browser art timeout returns it to 20 seconds; setting it
to 0 would mean zero seconds"*), again on every Reset button's label, and again in the confirm dialog.

**⛔ Prices are permanently out of scope.** `notices.outOfScope`, printed on the page and stated in the
manifest itself: prices, purchase amounts, entitlements and grants are never editable here and never
will be — those are decided by the server (`_lib/purchase-catalog.js`) because the game takes real
money, and a value the phone could override would be an exploit, not a feature.

**Every write is confirmed** with a `window.confirm()` that restates exactly what will change and (for
numeric and pick saves) the propagation delay, before the request is sent. All value writes funnel
through a single `writeKnob()` call site, which is what `test/command-center.test.js` pins the page's
postable actions against.

**⚠ Manifest-vs-build disagreement is printed loudly**, never hidden: `MANIFEST DOES NOT MATCH THE
BUILD` plus every defect, one per line. A manifest disagreeing with the build means a lever is missing
or dead, and the owner must not spend an evening looking for it.

**The values the rail accepts are narrow.** `normalizeValue` in `_lib/tunables.js` takes a bool or
`/^-?\d{1,9}$/` and nothing else; `validateTunableSet` refuses an unregistered key
(`UNKNOWN_TUNABLE_KEY`, naming the allowlist) and an unparseable value (`BAD_TUNABLE_VALUE`) — both
would otherwise be accepted and then silently ignored by every client, which during an incident reads
as "the flag did nothing."

The page also states (`console.js:1453-1455`) that **two of the Misc loading knobs are read at game
startup** and take effect only on the next launch, and that each such knob says so on its own card.

---

<a id="tab-players"></a>
## Tab: Players

```
Operator question   Who's actually playing right now?
Renders             stats?view=overview&days=N
Writes              -
Window              ?days drives the per-day tables ONLY (see below)
```

- A hero tile: unique identified players who fired a `session_start` in the last 24 hours.
- 7-day and 30-day unique active counts, and today's session count.
- A daily-active-players bar chart, one bar per UTC day, focusable for the exact count and session
  count.
- **⚠ The 1 / 7 / 30 tiles are FIXED trailing windows and ignore `?days` on purpose.** The SQL says so
  in a comment: letting a "last 7 days" selection clip them would silently render `active_30d` as a
  7-day number under a 30-day label. `?days` drives the per-day tables.
- Telemetry health: identified-session share, raw anonymous session count, and the lifetime total event
  count, with the `anonymous.note` stating those cannot be split into people and are excluded from all
  player counts.
- A "new players per day" table — a player's **first-ever event of any kind**, computed over the whole
  table so someone who installed in June never re-counts as new in August — each row cross-referenced
  against that day's active-player and session totals.

---

<a id="tab-toggles"></a>
## Tab: Toggles

```
Operator question   What's closed, and who closed it?
Renders             stats?view=ops&days=N   (toggles + ops_history)
Writes              POST ops -> maintenance.seal / maintenance.open
Window              Honors the 7 / 30 / 90-day selector (the gate-issue rows)
```

- If `server` is sealed, a top-level alert banner says **SERVER IS CLOSED** and states explicitly that
  every other area is closed too, whatever its own row says.
- **One row per maintenance area, and there are six**, imported from `_lib/maintenance.js` rather than
  re-typed (`stats.js:108-110`: *"a seventh area invented here would render a toggle the enforcement
  layer has never heard of"*). The six, read at `maintenance.js:84`: **farming, raiding, arena,
  dungeons, store, server.**
- Each row shows: the state **word** (`CLOSED` / `open`), when it was last flipped and by whom, an
  operator note if any, and the exact banner text players see if sealed.
- `row_present` — under the WO-1243 **fail-open** ruling an absent row means the area is **OPEN**, and
  the row says so in words (`"no row - fail-open means this area is OPEN"`) rather than as a gap the
  console has to interpret.
- **Sealing requires typing the banner message first.** The Seal button is refused client-side with
  `MESSAGE_REQUIRED_TO_SEAL` if the field is empty, because a banner with nothing to say is worse than
  no banner — and `_lib/ops.js` `validateSeal` refuses the same thing server-side, so the console cannot
  author something the operator CLI would have rejected. The message must also be ASCII
  (`MESSAGE_NOT_ASCII` — the in-game banner font is ASCII-only) and ≤ 200 chars.
- Timing is stated on the page and in the confirm dialog and in the write response: server-side
  enforcement within **~5 s** (lambda memo), the player-facing banner within **~40 s**.
- **Re-opening clears the banner text** along with the seal — a stale "closed for maintenance" message
  on an open area is a lie. Confirmed before sending.
- The write is an **UPSERT, not `INSERT ... ON CONFLICT DO NOTHING`**, and `setMaintenance` reads the
  row **back** and throws `WRITE_RETURNED_NO_ROW` if nothing returned: a missing row is harmless under
  fail-open, but a seal that silently did not write would be a disaster.
- Each area row expands to the actual matching **server-authored refusal records** for the window
  (`maintenance_refusal` events): timestamp, `REFUSED`, a **salted 12-hex maintenance fingerprint**
  (never a wallet), a correlation ref, the request path, and who closed it. The count and the rows share
  the same CTE / window / filter, so tapping a number explains it; `issues_truncated` says when the
  newest 50 of a larger count are shown.
- A separate **"Recent operator writes"** table (`admin_ops_write` events, newest 50) logs every write
  this console has made — action, target, outcome, operator — an audit trail independent of git history.
  `_lib/ops.js` notes these rows are written as player id `'anonymous'` on purpose so an ops row can
  never be counted as a player, and that `cleanup.js` purges only `web_trace` and `api_auth_reject`
  **by name**, so this history is not swept.

---

<a id="tab-money"></a>
## Tab: Money

```
Operator question   Did the customer actually get what they paid for?
Renders             stats?view=purchases&days=N
Writes              POST ops -> purchase.alert_acknowledge
                    Acknowledgements only. No money movement. Ever.
Window              Honors the 7 / 30 / 90-day selector
```

**Purpose.** Reconcile what the game client reported against what the server actually settled —
deliberately never blended into one number, because the whole point is to expose the disagreement.

**⚠ Top alert — client-reported, not server-settled.** Any purchase the client reported as completed
whose `txSig` has **no** matching `purchase_entitlements` row: a first-priority financial anomaly (a
customer may believe they paid and received nothing, or paid twice). Each row shows the **unmasked**
`tx_signature`, the pack id, the masked player, event count and latest timestamp.

- Row action: **`ACKNOWLEDGE - no action`** (`purchase.alert_acknowledge`). Explicitly labelled "no
  action" in the UI, and the confirm dialog says so plainly before sending. It writes an
  `admin_ops_write` event with `outcome: 'acknowledged_no_action'`, and **both** the orphan query here
  and the disagreement count on the Decisions tab carry a `NOT EXISTS` clause against that exact event
  shape, so an acknowledged mismatch stops re-firing.
- `acknowledgePurchaseAlert` inserts idempotently (`WHERE NOT EXISTS`), returns
  `already_acknowledged`, and — unlike the best-effort `recordOpsWrite` — **must throw on failure and
  the endpoint fails closed**, because this row *is* the state that suppresses the warning.
- The signature is validated as a bounded base58-like string (`{32,128}`) rather than a strict 64-byte
  Solana signature, deliberately: requiring a valid signature would make the malformed legacy stub
  values this action exists to clear impossible to acknowledge. The `reason` is required, ASCII, ≤ 120
  chars.

Sections rendered on the tab:

- **Client said / server settled, side by side, never merged** — `purchase_completed` event count,
  entitlements settled in the window, and `settled_without_client_event` ("analytics understates
  sales").
- **Settled purchases (server truth)** — all-time and windowed counts + USD from
  `purchase_entitlements`, using **`usd_anchor`**: the authored ladder price persisted onto the row at
  verify time, so it is a stable historical figure and not a re-derivation against today's market.
  ⚠ `usd_anchor` is **NULL on the two CANARY skus** (pinned protocol constants with no rate behind
  them), so `rows_without_usd_anchor > 0` means the total **understates the row count**, not that those
  sales were free. The response's `revenue_note` says exactly that and is printed under the tiles.
- **Quote to settle funnel** — issued / consumed (with `consumed_pct`, `low_n` under 10) /
  expired-unpaid / live now, with the **5-minute TTL** stated in the definition: *"consumed/issued
  falling is players TRYING TO BUY AND FAILING — the earliest warning this rail has."*
- **`needs_attention`** — settlements whose `status <> 'fulfilled'`, newest 100, wallet masked. The page
  states explicitly that **this console cannot re-grant** anything: that is a write on the money tables
  and it does not exist here.

**⚠ Two things the response carries that this tab does not render.** `stats?view=purchases` also returns
`recent_settlements` (the newest **50** rows, wallet masked, `tx_signature` unmasked because it is
already a public chain record and is the precise string an operator needs to answer "did that actually
land"), plus `by_status`, `by_sku`, `per_day`, and the quote funnel's `by_sku` / `per_day`. `console.js`
`renderMoney()` renders none of them. Read them from the endpoint, not from the tab.

**Each probe is independent.** Schema drift is real here — three of these tables were invisible to the
admin surface until **2026-08-24** (`git log`: commit `4f8c2f23d`, *"ops can finally SEE the money —
purchase tables were invisible to every console"*), and a stale CHECK constraint silently failed a
settled mainnet sale — so a missing or altered table degrades to an entry in `errors` rather than
500-ing the whole view.

**Status vocabulary**, from the code: `verified` = chain-confirmed, grant not yet handed over;
`fulfilled` = grant delivered and acknowledged; `manual_review` = verified but something did not match
(e.g. the payment landed outside the quote window) — the money moved and a human has to look.

---

<a id="tab-player-issues"></a>
## Tab: Player issues

```
Operator question   What are players reporting, and can I trust the channel?
Renders             stats?view=ops&days=N   (the `reports` field)
Writes              -
Window              Honors the selector on the per-day table
```

In-app bug reports from `bug_reports`, newest 50. Read-only — no actions here.

Columns: report id, timestamp, description, the in-game route it was filed from, app version, platform,
identity state, and whether a screenshot was attached. **Identity is `verified` / `unverified` only** —
never an actual identity. The page states that *a burst of unverified means auth is broken, which is
itself the signal*, and that no address is shown here ever. Verification reads
`COALESCE(wallet, context->>'verifiedWallet')`; both sources are server-verified and neither ever holds
a client claim.

**⚠ The page notes explicitly that `bug_reports` has, on some deployments, never accepted a single
row** — so an empty list here is **not** proof the reporting channel is working; it could just as easily
be broken.

The dev board is not here. `BOARD.html` at the repo root is generated from `WorkOrders/*.md` by
`python tools/board_build.py`; the two are linked, never merged.

---

<a id="tab-promos"></a>
## Tab: Promos

```
Operator question   What codes exist, what do they grant, and who can redeem them?
Renders             stats?view=ops&days=N   (the `promos` field)
Writes              POST ops        -> promo.create / promo.set_active
                    POST promo-bind -> bind an existing code to one Google player
WO                  WO-1244, WO-1599, WO-1698, WO-1833
```

### "Author a promo code"

- **The code string.** Stored uppercase, because the client uppercases before sending
  (`PromoCodeService.cs` does `Trim().ToUpperInvariant()`; `schema.sql` §3 says store and compare
  uppercase). `normalizePromoCode` enforces `[A-Z0-9_-]+`, 3–32 chars, with a distinct refusal code for
  each rule (`PROMO_CODE_REQUIRED`, `PROMO_CODE_CHARSET`, `PROMO_CODE_TOO_SHORT`,
  `PROMO_CODE_TOO_LONG`).
- **A reward: either a pack SKU or crystals/coins, never both.** Setting both is refused
  (`REWARD_AMBIGUOUS`), because `schema.sql` §3's precedence makes the SKU win silently and the
  crystal/coin values would be dead data with no indication why. A code that would grant nothing at all
  is refused too (`REWARD_EMPTY`) — though a live discount window counts as "something".
- **The SKU field is a dropdown sourced live from the catalog the SKUs tab reads** (WO-1599) — built
  from `state.skus`, the WO-1532 catalog view fetched on every load, and never from a list typed into
  the page. A second copy of the SKU list would be the duplicated state that has cost this repo its
  most expensive bugs.
  - A **"Type it instead"** toggle always remains available, because a pack authored straight into the
    database (not yet in the shipped catalog) still needs to be mintable.
  - **Exactly one of the two inputs is live at a time.** The toggle disables the one it hides and clears
    its value; the single reader `skuFieldValue()` returns whichever is showing. Flipping it
    deliberately does **not** re-render the card, so a half-typed code and message are not thrown away.
  - If the catalog read **failed**, the select is empty and disabled and says so in words
    (*"COULD NOT READ the SKU catalog (…), so the list is EMPTY and DISABLED - it is not saying there
    are no packs"*), and the typed field becomes the only option. **The toggle is not rendered at all
    while the catalog is unreadable** — switching back would leave the operator with a disabled select
    and a hidden text box: two dead inputs and no way to mint. A catalog that read fine and lists no
    packs says that instead, in a different sentence.
- Optional: a player-facing message (ASCII, ≤ 200), max redemptions, per-player limit, expiry.
  **Blank means NULL, not 0** — `optionalCount` exists because an untouched HTML number input POSTs
  `""` and `Number('') === 0`, which would author `max_redemptions = 0`, i.e. a code nobody can ever
  redeem, when the schema's meaning for the field is "NULL = unlimited". An expiry already in the past
  is refused (`EXPIRY_IN_THE_PAST`).
- **Optional discount window (WO-1833)** — a percent-off shared by every redeemer of that code, with
  its own start/end time, entirely separate from the site-wide sale knob. Ceiling **70%**
  (`SALE_MAX_BPS = 7000`), imported from `_lib/store-sale.js` rather than re-typed as an authoring
  limit, because two ceilings would drift and the authoring one would be the silent half. The page's
  input is `0–70` in percent; `console.js` converts to bps.
  - A percent **requires** an end time (`DISCOUNT_WINDOW_INCOMPLETE`); an end time with no percent is
    refused (`DISCOUNT_WINDOW_WITHOUT_DISCOUNT`); backwards (`DISCOUNT_WINDOW_BACKWARDS`) and
    already-closed (`DISCOUNT_WINDOW_IN_THE_PAST`) windows are refused too.
  - ⛔ **Both bounds must carry an explicit UTC offset** (`WINDOW_NEEDS_OFFSET`). This is the one
    validation in `_lib/ops.js` that exists because of a time zone: the owner authors the window in
    CST, and `Date.parse('2026-09-18 12:00')` with no offset is read in the runtime's zone — UTC on
    Vercel — landing the window five to six hours out, with a perfectly valid stored timestamp and
    nothing downstream able to detect it. The page satisfies this by sending `toISOString()` (a `Z`
    value) from the browser's local zone. `optionalExpiry` on the separate `expiresAt` field
    deliberately still accepts a bare wall clock — tightening a pre-existing field silently would be
    its own bug.
  - The confirmation echoes the window back as **UTC ISO**, in words, so a CST typo is visible at
    author time rather than at noon on the day of the campaign.
  - The `admin_ops_write` history row carries `discountBps` / `discountStartsAt` / `discountEndsAt`, so
    "who set the 30% window and when does it end" is answerable without reading the table.
- **`createPromo` refuses to overwrite.** `ON CONFLICT (code) DO NOTHING` returning nothing becomes
  `PROMO_CODE_EXISTS` — re-pointing a code players may already hold is an EDIT, not a create.
- ⚠ **The insert is a THREE-shape cascade**, and it exists because this repo has no migration runner —
  a deploy reaches production before a human runs the SQL file. On Postgres `42703`
  (undefined column) it degrades: full shape → `without_discount_columns` (created_by kept) →
  `without_created_by`. The middle shape exists so a database that has `created_by` but not the
  discount columns does not silently strip attribution from every ordinary grant code. **A draft that
  wants a discount fails LOUD** (`DISCOUNT_COLUMNS_MISSING`, naming the migration to run) rather than
  being authored as a plain grant code — a code the operator believes discounts everything and which
  discounts nothing is a public campaign that fails quietly. The response reports
  `attribution_on_row` and, in the oldest shape, a `warning` naming the `ALTER TABLE` to run.
- ⚠ **Wallet-address binding is deliberately NOT exposed in this form** (WO-1244). Authoring one would
  mean typing and later displaying a wallet address, which this console refuses to render. That path
  stays a direct SQL / operator-CLI job. The console reports *whether* a code is bound, never to whom.

### "Bind code to Google player"

A separate, narrower form: Google email + an **existing active** code. It does not create or grant
anything; it attaches an existing code to a player found by email lookup. Requires
`GOOGLE_IDENTITY_KEY` on the deployment; only an **HMAC fingerprint** of the email is stored, never the
email itself, and the page clears the email field the moment the request is sent.

Mechanically (`promo-bind.js`): the fingerprint is looked up in `play_identities.email_hmac`, and on a
single match whose id matches `^play-[0-9a-f]{64}$` the code's `bound_wallet` is set to that **play
identity id** (not a wallet) via a conditional `UPDATE` that also refuses a code already bound
elsewhere. Every outcome is audited as `admin_promo_bind` with a **stable code only** — no input, no
player, no HMAC, no driver error text.

Full refusal set, read at source. ⚠ **The page's `words` map renders 13 of these as a specific
plain-language sentence; `BAD_BODY`, `METHOD_NOT_ALLOWED` and `ADMIN_NOT_CONFIGURED` are not in the map
and fall through to the generic "Binding was refused. Check configuration and retry."**
`METHOD_NOT_ALLOWED`, `ADMIN_NOT_CONFIGURED`, `UNAUTHORIZED`, `OPS_WRITE_NOT_CONFIGURED`,
`OPS_UNAUTHORIZED`, `GOOGLE_IDENTITY_UNCONFIGURED`, `BAD_BODY`, `EMAIL_INVALID`, `CODE_INVALID`,
`NO_MATCH`, `AMBIGUOUS_MATCH`, `CODE_NOT_FOUND`, `CODE_INACTIVE`, `ALREADY_BOUND_ELSEWHERE`,
`CODE_CHANGED_RETRY`, `LOOKUP_UNAVAILABLE`. (`AMBIGUOUS_MATCH` exists because duplicate mail claims or a
key rotation must **never** pick an arbitrary player; `CODE_CHANGED_RETRY` is the concurrent-operator
case.) The page adds `CANCELLED` and `NETWORK` for its own client-side outcomes.

### Codes table

Every promo code: code, state, what it grants, redemption count vs cap, expiry, whether it is privately
bound (`is_bound` — the address itself is never selected), and an Enable/Disable toggle
(`promo.set_active`) per row, each confirmed before sending. State is computed in `stats.js`:
`DISABLED` if `!active`, else `EXPIRED` if past `expires_at`, else `FULLY REDEEMED` if the redemption
count has reached `max_redemptions`, else `ACTIVE`. `setPromoActive` is an `UPDATE` only and answers
`PROMO_CODE_NOT_FOUND` on a no-op rather than reporting a successful write of nothing.

**⛔ Nothing here deletes.** Disabling a promo is `active = FALSE`; deleting the row would cascade its
redemption history away.

---

<a id="tab-skus"></a>
## Tab: SKUs

```
Operator question   What can players actually buy right now, and where would that silently break?
Renders             stats?view=skus     (entirely read-only)
Writes              -
WO                  WO-1532 (landed 2026-09-06)
Window              None - a catalog is not a time-series
```

Origin, quoted: owner, 2026-09-06 — *"can we add a list in command center of All SKU's and contents."*

**Dispatched above `neon()` on purpose.** `stats.js` routes `view=skus` to `_lib/sku-catalog.build()`
**before** the database client is constructed, so "this view never touches the database" is a
**structural** fact rather than a claim in a comment — it runs green with `DATABASE_URL` unset, which is
how `test/admin.skus.view.test.js` proves it. It is still behind the read gate: the catalog is not a
secret, but the shape of what is unsellable is operator information. The response sets
`window_days: null` and a `window_note` saying `?days=` is ignored, rather than leaving a number on
screen that looks like it filtered something.

**⚠ The source file is a generated copy, not `Assets/`.** `_lib/sku-catalog.js` requires
`./sku-catalog.generated.json`, written verbatim from
`Assets/Resources/Data/Canonical/packs.json` by `tools/gen-sku-catalog.mjs`, because `.vercelignore`
never uploads `Assets/` to the deployment. (The page's own copy says "the canonical `packs.json` the
game ships", which is true of the content, not of the path.)

The module is a **join, not a fourth copy of the catalog** — three facts, three owners, none re-typed:

| Fact | Owner |
|---|---|
| what it is + what it grants | `packs.json` (via the generated copy) |
| may it be **quoted** | `USD_ANCHORS` in `_lib/purchase-catalog.js` |
| may it be sold through **Play** | `PRODUCT_TYPES` in `_lib/google-play-purchases.js` |

**Summary tiles:** total packs, how many are on the shelf (`storeVisible`), how many are **sellable**
(anchored **and** visible), and how many carry a **parity gap**.

**Full table, one row per pack:** SKU id (with `founder only` / `promo grant only` badges), display
name and tagline, tier, storefront section (`band`, falling back to `storeSection`), shelf visibility,
price in every supported currency (USD / USDC / SOL / SKR), the USD anchor, the Play product type, an
overall sellable yes/no **with a plain-language reason**, and the pack's granted contents nested
underneath.

- Contents keep their three authored shapes rather than being flattened into a "unified item" list,
  because that would flatten away the distinction the covenant rests on — **convenience is time, never
  combat power**: `economy` (resource → amount), `cosmetics` (ownership ids), `convenience`
  (`{kind, count, description}`).
- ⚠ **Without a USD anchor row the wallet purchase rail cannot sell the pack** however the storefront
  card looks — `usdAnchor()` returns null, no quote is built. The page states this is a real failure mode
  that **has shipped before**: WO-1165 §2, the Monthly Ledger cards, authored with a real `pricing.usd`,
  no anchor row, silently unbuyable on the live rail, found only by a human reading two files side by
  side.
- ⚠ **Without a Google Play product type**, `validRequest()` refuses the SKU outright.
- `promoGrantOnly` rows are **listed** but a missing anchor on one is **not counted as a gap** — they are
  never offered for sale, so the absence is the design. The absence is still reported truthfully in
  `usd_anchor_present`.
- **A third gap kind the parity row reports:** an authored `pricing.usd` that **disagrees** with the
  server anchor. The gap text names both numbers and states that the server figure is what the player
  is charged — two prices on one screen is worse than a stale one.
- Any pack with a gap gets an explicit `MISSING` row spelling out which rail it is missing from and what
  a player experiences because of it. `MISSING` is a **word**, in capitals; the red class on it is
  decoration only.

**Reverse-direction check: "Priced, but not a pack."** Any SKU the payment rails know a price for that
is not a row in the pack file. This can be legitimate — `monthly-wayfarer` and `monthly-keeper` are
authored in `battle_monthly.json` `monthlyCards[]`, and the mainnet canary is a proof-of-rail, not a
sale — **but it is always named, never silently dropped**, because a missing row is invisible by nature.
Play product types without a pack are listed the same way.

**⚠ This tab makes no writes at all.** If the catalog fails to read, it prints `COULD NOT READ the SKU
catalog` and shows **no table** — an empty catalog rendered here would read as "we sell nothing", which
is a very different fact. The response carries `catalog_version`, a `currency_disclaimer`, and a `notes`
array the page renders verbatim. **It reports; it never repairs** — a catalog view that defaulted a
price would be authoring money. Changing a price or a grant is an edit to `packs.json` and the server
ladder, never a control on this page.

---

<a id="tab-tickets"></a>
## Tab: Tickets

A static informational card, not a live feed. It explains that there are **two deliberately separate
ticket systems**:

1. **Dev work** — `WorkOrders/*.md` in this repo, rendered into `BOARD.html` at the repo root by
   `python tools/board_build.py`. The repo is the source of truth, so the board is derived and cannot
   drift; it is a local file and is never served over the internet.
2. **Player issues** — the Player issues tab above, reading `bug_reports`.

⚠ The page explicitly warns not to fold these into `BOARD.html`, since that file is regenerated on
every run and anything hand-written into it is silently overwritten.

---

<a id="unfetched-stats-views"></a>
## Read views no console tab fetches — `stats.js`

These are **live and distinct**, not dead legacy. They answer different questions from `command`, and
the code says so in several places. (An earlier revision of this document listed them as "may be
superseded or dead"; that was wrong.) `stats.js`'s own unknown-view hint is the endpoint's only
discovery surface and names every view it serves:
`overview | retention | funnel | economy | purchases | playtime | ops | players | command | skus |
active | monetization | stability` — **13 views.**

**Build / platform slicing (WO-1843).** `?app_version=` and `?platform=` are honoured by exactly five
views — **`retention`, `funnel`, `purchases`, `active`, `stability`** (`SLICEABLE_VIEWS`). Asking any
other view for a slice returns `meta.slice_ignored` naming the ignored parameters and the sliceable
list, because returning the unfiltered number under a build label the caller believes was applied is
"the worst failure mode this whole ticket is about — it looks exactly like a working answer."
The live value set for those parameters is published by `?view=active -> by_build`, which is
deliberately never sliced itself: **it is the menu.** A slice that matches nobody resolves to an empty
array and correctly filters everything out; `matched_players: 0` is returned so that reads as "this
build has no players", never as "the metric is broken". Resolved cohorts are capped at
`SLICE_PLAYER_CAP = 5000` with `cohort_truncated` when the cap bites.

### `?view=retention`

Classic **day-N retention by signup cohort**, on `session_start` rather than the qualifying-play
allowlist — `cohort_day` is the day a player's first-ever event landed, and `d1/d7/d30` count a
`session_start` on **exactly** `cohort_day + N` (the stricter, more standard reading). Every percentage
ships with its cohort size and a `low_n` flag, and a cohort younger than N days is marked
`d7_mature: false` **rather than reported as 0%** — the players have not had seven days yet. The rollup
pools over mature cohorts only instead of averaging percentages, which would over-weight tiny cohorts.

### `?view=funnel`

The **tutorial funnel** — per step, from the events `TutorialFlow` actually emits: `enter`, `complete`,
`skip`, and **`drop`**, which is the watchdog auto-advancing a player who sat stuck. A step with drops
is a step players cannot get past on their own, and the drop rows carry `secondsIdle`, which separates
"a slow step" from "a step nobody can pass". Ordering comes from `properties->>'order'` sorted
**numerically in JS, never cast in SQL**, because the value is client JSONB and one malformed row would
fail the whole query; unordered steps go last, by volume, never dropped. Also reports drop-off between
consecutive steps and the contextual-hint steps.

### `?view=economy` — **client-reported intent, never revenue**

Aggregates the client's own `purchase_completed` / `bundle_viewed` events, the promo and referral table
truth, and the **WO-1388 six-step store funnel** (`store_opened`, `bundle_viewed`, `pack_tapped`,
`checkout_started`, `checkout_failed`, `purchase_completed`) on **fixed 7d/30d windows independent of
`?days`**, so weeks compare — with every one of the six listed even at zero, because "0 sales" is only
useful once it reads as *which* step is the first zero. Plus `checkout_failed` reasons and
`store_opened` doors.

⚠ **`purchase_completed` carries no price field.** `PackStore` emits `packId/packName/currency/txSig`
only; the `price` in `EventTracker`'s doc comment is an example, not a live field. So this view reports
**counts**, never revenue, and a null `price_sample` means the client never sent an amount — **not that
the sale was free**.

⛔ **`store_opened` and `quote_funnel.issued` are NOT joined, and that is the honest answer, not a gap.**
`analytics_events` keys on `player_id`; `purchase_quotes` keys on `wallet`. No bridge exists between
those identity spaces, so a per-player "opened → quoted" conversion cannot be computed, and a ratio
across two different denominators would look like a conversion rate while being an artifact. Both sides
carry a pointer to the other instead. Also load-bearing, read at source: **opening the store does not
mint a quote row** — `PackStore.Open` hits the LIST mode of `api/purchases/quote.js`, which persists
nothing; the INSERTs happen only on a single-SKU quote. So quote-issued is a genuine step *downstream*
of `store_opened`, not something co-fired by the screen appearing.

### `?view=playtime` — WO-1842 measured session duration

**The one view whose answer required a client change.** Until 2026-09-17 the client emitted
`session_start` on boot and nothing marked when a session stopped; no amount of SQL over rows with no
end timestamp can produce a duration. `EventTracker.cs` now emits `session_heartbeat` every 60 s of
**foreground** time and `session_end` on pause/quit, both carrying `{ sessionId, elapsedSeconds }`
where `elapsedSeconds` is **cumulative for that sessionId** — so a session's duration is
`MAX(elapsedSeconds)` over its rows, with no ordering rule, no join, and no way for a duplicate or a
late arrival to corrupt it.

- **`session_start` is deliberately not joined.** That row is often attributed to `'anonymous'` (queued
  before the account id is minted) while later rows carry the real id, so a join would drop exactly the
  new players this view exists to describe. `sessionId` is the key.
- **A duration here is FOREGROUND seconds** — accumulated on the device between resume and pause, never
  from wall-clock boot time, because Android keeps the clock running on a phone in a pocket. It
  therefore **includes idling with the game open** and excludes time backgrounded. The gap-based
  estimate on `?view=command` is the complementary "between acts" reading. Two honest numbers, each
  labelled; neither is corrected into the other.
- **Buckets** (`PLAYTIME_BUCKETS`, edges in seconds, `min` inclusive / `max` exclusive):
  `Under 1 minute`, `1 to 5 minutes`, `5 to 30 minutes`, `30 to 60 minutes`, `60 minutes and up`.
  ⚠ The **"Under 1 minute" band is added and is NOT in the owner's ask** ("1-5, 5-30, 30-60, 60+"): a
  bounce is the single most important number on a retention surface, and dropping it would silently
  shrink the denominator until every other band read high. The bands tile `[0, ∞)` with no gap or
  overlap, so bucket sessions always sum to `sessions_measured` — an invariant `bucketPlaytime()` is
  exported for a test to assert rather than re-implement.
- A NaN, null or negative duration is **dropped and counted**, never coerced to zero — folding one into
  the bounce band is the most misleading place it could land. `malformed_rows` counts rows the SQL
  shape-guard rejected; `unreadable_sessions_dropped` counts the rest.
- `floor_note`: a session that dies without a pause/quit callback reports its **last heartbeat**, so its
  duration is a **floor**, understated by up to the heartbeat interval; `ended_cleanly_pct` says what
  share of the sample is exact. `delivery_note`: a `session_end` raised at pause is persisted to
  PlayerPrefs and often delivered on the **next launch**, so a very recent window can under-report and
  then fill in.
- Scan ceiling `PLAYTIME_SESSION_CAP = 50000`, with `ORDER BY received_at DESC` **before** the LIMIT,
  because a bare LIMIT would split a session across the boundary and understate its MAX — a silently
  wrong duration is worse than a declared truncation.
- ⛔ **Coverage will be near-zero until a WO-1842 build has been in players' hands for the whole
  window.** Every session predating the client change emitted no end signal and **can never be measured
  retroactively**; a low `measured_pct` means this describes a minority of sessions, not that sessions
  got shorter. And `measured_pct` **can legitimately exceed 100%**: a resume after more than 30 minutes
  backgrounded mints a new sessionId on the client without a second `session_start`, so one boot can
  yield several measured sessions. The response says so (`over_100_pct_is_possible`) rather than leaving
  it to be raised as a defect.

### `?view=active` — WO-1843 DAU / WAU / MAU + stickiness

Distinct `player_id` that fired `session_start` in a trailing 1 / 7 / 30-day window — **"opened the
game"**, the industry DAU definition, deliberately **not** the WO-1281 "playing" definition the
`command` card uses. Both definitions are printed and the two are never blended. Windows are fixed and
ignore `?days` for the same reason the overview's are.

Also returns: sessions per window and per DAU; stickiness as `DAU/MAU`, `WAU/MAU` and `DAU/WAU` (the
`low_n` flag is on **MAU, because MAU is the denominator**); week-over-week and day-over-day trend
**words**; `user_days` (the SUM of per-day DAU — *"the correct ARPDAU denominator; it is NOT distinct
players and NOT sessions"*, and `?view=monetization` divides by this exact figure); the per-day series;
and `by_build`.

**The slice here is a ROW filter, not a player filter**, and the response says this is *more precise
than anywhere else in the file*: `session_start` is the very row that carries `appVersion`/`platform`,
so no per-player join and no multi-build ambiguity is involved.

### `?view=monetization` — WO-1843 payer rate / ARPU / ARPDAU / ARPPU

Revenue had a numerator and no denominator anywhere on this page. This view supplies them and names
each one, because they are **not the same axis**:

| Metric | Denominator |
|---|---|
| payer rate | active players (`?days`) |
| ARPU | active players (`?days`) |
| **ARPDAU** | **user-days** (SUM of per-day DAU) — ⛔ *not* ÷ today's DAU, which would divide a whole window's revenue by one day's players |
| ARPPU | paying wallets only |

- ⛔ **Devnet is not revenue and is split out, never summed.** `usd_all` includes every network;
  `usd_real` excludes `network = 'devnet'`, and every ratio is computed on `usd_real`. *"A dashboard
  that reports test money as income is the money version of §16's silent capsule enemies."*
- ⛔ **Operator/test wallets are excluded from BOTH sides of every ratio.** The denominator already
  excluded them (WO-1281 acceptance 9); leaving them on the numerator would count an owner's own test
  purchase against a playerbase that does not contain them. The **complete** ledger, operator rows
  included, stays readable at `?view=purchases`.
- Every ratio is **returned with** `reportable: false`, `low_n: true` and the `INSUFFICIENT PAYER
  VOLUME` caveat while payers are under the 10 threshold — the value is still returned, because hiding
  it would just move the guess somewhere else, but it comes with the reason it cannot be trusted. A
  zero denominator returns `null`, never `0`. The headline reads `NO REVENUE IN WINDOW`,
  `PRE-REVENUE — RATIOS NOT REPORTABLE`, or `REVENUE MEASURABLE`.
- **Identity overlap is measured, not assumed.** Payers are `wallet` on `purchase_entitlements`; active
  players are `player_id` on `analytics_events`. Those coincide only for wallet-bound players (WO-1791
  identity tiers), so `payers_seen_in_telemetry` is queried and returned:
  `payers_seen_in_telemetry < payers` means some buyers are not in the denominator and the payer rate
  understates.
- ⛔ **LTV and churn modelling are deliberately not built** (WO-1843 scope): *"there is no cohort to fit
  them to, and a model over this sample would be fiction wearing a decimal point."* Do not add them
  without a ruling.
- **`MEASURED 2026-09-17, NOT RE-VERIFIED`** — the code's header records the read that motivated all of
  the above: 4 settled entitlements, 2 distinct payer wallets, $10.97 of `usd_anchor` all-time, of which
  mainnet-beta $5.98 (2 rows), the Pi rail $4.99 (1 row, status `verified`) and devnet $0 (1 row, no
  anchor). **Treat those as historical colour explaining why every ratio is flagged, not as today's
  revenue.** Re-run the query for a current figure.

### `?view=stability` — WO-1843 break rates against traffic

Break rates from the F8 `BreakCaptureHarness` `playtest_break` event, **divided by the traffic that
produced them**. The stated reason a count cannot answer the question: the same error count across a
growing playerbase may be an improvement and across a shrinking one a collapse.

- **The unit is the player-day.** A `playtest_break` row carries no session id, so it cannot be
  attributed to one session, and the player-day is the finest unit both sides honestly share. Session
  rows are returned beside it for the per-session intensity figure only.
- ⛔ **A true crash-free rate is NOT MEASURABLE from this data and is NOT CLAIMED.** The game ships no
  crash reporter; an unhandled Unity exception does not terminate the app, and a process the OS killed
  emits nothing at all. What is returned is an **exception-free player-day rate, named as a PROXY**
  (`crash_free_caveat`, the most load-bearing string in the view). Closing the gap needs a crash SDK —
  new instrumentation, a separate ticket.
- ⛔ **The kinds are never summed into one "crash" number.** `kind=error` is the `Debug.LogError`
  firehose and fires in nearly every session; `kind=exception` is the crash-adjacent one;
  `kind=possible_softlock` is a third axis; `scene_loaded` and `note` are bookkeeping, not breaks, and
  are listed for completeness only. Blending them lets the firehose bury the handful of rows that
  matter. **`MEASURED 2026-09-17 over 30 days, NOT RE-VERIFIED`** — the comment's illustration:
  `error` 33,198 rows / 166 ids, `exception` 32 rows / 3 ids, `possible_softlock` 110 rows / 13 ids.
- ⚠ **The denominator is known to be incomplete and the view proves it rather than hiding it.** A break
  can be recorded for a player whose `session_start` never landed (queued events, an excluded or
  anonymous boot), so a rate here **can exceed 100%**, and when it does `denominator_incomplete` and a
  `denominator_note` say so, `free_pct` returns `null` instead of a meaningless complement, and the
  per-day figure is **not clamped** — *"a `Math.min` here would have silently printed a plausible 0% and
  contradicted the very rule this view states."* *(`MEASURED 2026-09-17, NOT RE-VERIFIED`: 211
  player-days carried a `session_start` while 228 carried an error — more error-days than session-days,
  which is what the guard is for.)*
- `raw_rows_pointer` sends the reader to `db?view=events&name=playtest_break&group=kind`: *"This view is
  the RATE; that one is the evidence."*

### `?view=players`

Recent players, ordered by raw event count, masked, with `player_ref`, `is_guest`
(`guest-local-` prefix), first/last seen, events, sessions and active days. `?player=` / `?ref=` is the
single-player drill-down and **the one path in the whole surface that returns a full id** — a `?ref=`
handle is resolved inside a bounded 500-most-recent set, never a hash scan over the whole event table,
and a miss says so in words. The single view returns a summary, the save's metadata (schema version,
trust, payload size — never the blob), the per-event breakdown and the newest 50 event **names and
times only**; `properties` is never echoed.

⚠ `ordering_note` is a correction the file makes about itself: since WO-1842 added a 60-second
heartbeat, "ordered by event count" tracks **time spent in the app** more than actions taken. The
ordering is arguably more useful now, but it is a different question than it was.

---

<a id="dbjs"></a>
## The raw-diagnostic surface — `api/admin/db.js`

Not "broader than the console needs" — it is the **raw-diagnostic** endpoint, built for direct
operator/CLI use during an incident (it backs `tools/db-viewer/index.html`, a local HTML file the owner
double-clicks). The console fetches exactly **one** of its views, `ads`.

**It serves 11 views**, named in its own unknown-view hint (an omitted name reads to the caller as "not
supported", so a view is added to that list in the same edit that adds the view):
`overview | players | metrics | ads | traces | bugreports | bugreport | authrejects | purchases |
events | funnel`.

- **`overview`** — per-table row counts + newest timestamp, each table probed **independently** so
  schema drift degrades to `"missing or unreadable"` on one row instead of failing the view. The
  probe list is where the money tables were added on 2026-08-24, and where `pi_payments` was added by
  WO-1797 after a Pi payment at `state='granted'` read as "the player may be owed goods" for six days.
- **`players`** — newest `player_data` rows, **payload size only**; `?player=<id>` is the only path that
  returns a save blob. `state_keys` is the fast "is this a real save or a husk" tell.
- **`metrics`** — last-7-day aggregates: per-event-per-day counts, distinct players/sessions per day,
  and web_trace error-line counts. **Counts only**, which is the hole `events` closes.
- **`ads`** (WO-1796) — the ILRD money. `?days` defaults to **7**, capped at **90**. One CTE, **one
  guard, one place**: `properties->>'revenueUsd'` is TEXT out of JSONB and a bare `::numeric` cast
  throws `22P02` on anything non-numeric, which would take the whole view down — so the regex-guarded
  CASE is written once inside the `imp` CTE and every aggregate reads the normalised column. *"A second
  copy of that cast anywhere in this file is a bug, not a convenience."* `NULLIF` before `COALESCE`
  because LevelPlay's payload returns Placement as an **empty string**, not null, and a blank cell reads
  as a rendering bug rather than as "the network did not say". Returns totals, per-day, per-network
  (with eCPM and `low_n`), per-format+placement, and rewarded-ad `completions` by provider and outcome.
  ⚠ `cross_rail`: impressions come only from LevelPlay (Android) while completions come from
  `AdGateService`, which is provider-agnostic — **so impressions ÷ completions is NOT a completion
  rate.**
- **`traces`** — web_trace batches. `order=asc` + `offset=N` paging exists because the view used to be
  `DESC LIMIT 20` with no offset, so a long session (*incident figure from the code comment,
  2026-07-15: one real session ran 2840 batches / 153k lines*)
  could only be read from its **tail**, which is gameplay spam, while the lines that actually diagnose a
  bug are emitted in the **first** batches. *The trace pipe recorded the answer and the reader could not
  see it.* Returns `total_batches` and `has_more` so a caller knows how far it can page.
- **`purchases`** — the real ledger. ⚠ **The `status <> 'fulfilled'` filter applies only with
  `?state=unfulfilled`**; the default query returns the whole ledger newest-first with
  `unfulfilled_minutes` NULL on fulfilled rows. `unfulfilled_minutes` is computed here rather than left
  to the reader: *"verified 3 minutes ago" is a purchase in flight, "verified 3 DAYS ago" is a player
  owed goods — same status, opposite urgency, and a human scanning timestamps will miss it.* Also
  returns a per-status summary, an **expected-vs-observed base-unit mismatch count** (`/verify` refuses
  a mismatch, so a row where these differ should be impossible — the count exists so "impossible" is
  something we can *see*), and a `legend` block spelling out what each status means.
- **`bugreports`** / **`bugreport`** — the list (screenshot as a presence flag only; the blob can be
  ~420K chars) and one report in full, with the entire `traceTail` and the base64 only on `?shot=1`.
  `wallet` is read as `COALESCE(column, context->>'verifiedWallet')` so reports filed during a
  migration gap are not silently read as unverified.
- **`authrejects`** — the structured save/load/nonce refusals, turning "cloud save is broken" into
  "17 × `AUTH_HEADERS_MISSING` on `/api/game/save` in the last hour". Reads **three** event names:
  `api_auth_reject`, the legacy `auth_failed` (whose `reason` is `COALESCE`d onto `code` so both eras
  answer one query), and **`save_reset_refused`** (WO-1745). The third name is the important one:
  `save.js` had written a durable row on every `409 SAVE_RESET_STALE` since 2026-09-07 under a name this
  IN-list did not contain, so **no admin query in the product could see one**, and WO-1742 §3 read
  "zero refusals on `/api/game/save` in seven days" off this very view — *a view artifact, not a
  measurement.* A frozen cloud row is otherwise invisible on Android (WebTrace only POSTs under
  `UNITY_WEBGL`), so this view is the single platform-blind detector for it.
- **`events`** (WO-1793) — ⭐ **the generic payload read, and the answer to "what ARE those 424 rows?"**
  (*incident figure from the code comment, 2026-09-16: 424 `playtest_break` rows landed and not one was
  readable without `DATABASE_URL`; of them 330 were `kind=error`, 74 `scene_loaded`, 11 `note`, 8
  `possible_softlock`, 1 `idle` — i.e. a fifth of the "424 breaks" were scene-load bookkeeping and not
  breaks at all, which a raw count hides.*)
  Requires `?name=<event_name>`; optional `?player=`, `?since_hours=` (default 24, max 168), `?limit=`,
  and `?group=rows|message|kind|player` (anything else falls back to `rows`). `name` is a bound
  parameter and `group` selects between literal queries written out in full — neither reaches SQL as
  text. Always returns the window total so a grouped view's rows are read against the size of the thing
  they partition. `message` grouping uses a **coarse prefix, not the raw message**, because grouping on
  the full message fragments uselessly; `rows` truncates `message` and `stack` to 400 chars, returns
  `stack_len`, and hands back `(properties - 'message' - 'stack')` as `props` — which is what makes the
  view generic. ⚠ Its legend **states** the ambiguity rather than implying it: `app_version` is **not in
  the payload** (`BreakCaptureHarness.Record` builds `{kind,message,stack,scene,t,utc}` with no build
  string), so any build attribution is a per-player join over the window and is ambiguous for a player
  who booted two builds — `group=player` returns the **set**, not one value.
- **`funnel`** — per distinct player id in the window: first seen, last seen, event count, and the
  **set** of event names it emitted (explicitly "not an ordered path"). Capped at 200 ids,
  aggregate-only.

**⚠ This is the same defect class twice, in the code's own words: a view that does not contain an event
name reports ZERO and reads as a measurement.** WO-1745 closed it for `authrejects`; WO-1793 closed it
generically with `events`.

---

<a id="other-endpoints"></a>
## Other admin endpoints (not console tabs)

These exist, are gated, and are not reachable from any tab. They belong here because a future reader
will find them and wonder.

### `cleanup.js` — TTL sweep

```
Method      GET or POST
Auth        Authorization: Bearer <CRON_SECRET>  OR  X-Admin-Key == ADMIN_DASH_KEY
Status      200 | 400 | 500      (no CORS surface - server-to-server only)
```

**WO-685**, born 2026-07-12 (`git log`: `1f4235fb9`). The web-trace pipe (WO-443) writes every WebGL
diagnostic batch into `analytics_events` as `web_trace` and never deletes it; the client header and
`WebTrace.cs` both promise a "7-day TTL" that had **no server-side enforcer** — the
`SECURITY_AUDIT_2026-07-12` finding **H1** (*"the 7-day web_trace TTL cron DOES NOT EXIST"*) and **M4**
(auth_nonces pruned only per-wallet, opportunistically). This function is that sweep.

Four bounded, idempotent deletes:

1. `web_trace` rows older than **7 days**, cut on **`received_at`** — the server receive time, the
   trusted TTL clock, **never** the client-supplied `client_ts`.
2. `auth_nonces` that are spent (`used = TRUE`) **or** expired. Folds in audit M4. A live, unused,
   unexpired nonce is kept.
3. `guest_rate_limit` rows untouched for 30 days. Wrapped in try/catch so a deployment that has not
   applied the schema still runs the others. Dropping a row only resets that guest's 60-second window —
   **it can never lose a save** (the save lives in `player_data`, keyed by the id, not by this row).
4. `api_auth_reject` rows older than 7 days — diagnostics, not history; the same window as `web_trace`
   keeps the table from becoming a landfill if something starts 401-looping.

Returns `{success, ran_at, retention_days, deleted_web_trace_rows, deleted_auth_nonces,
deleted_guest_rate_rows, deleted_auth_reject_rows}` and logs the same object.

⚠ **The file header's "GOES LIVE only on the owner's next deploy" note is historical.** The cron **is**
registered: `vercel.json:7` schedules `/api/admin/cleanup` at `0 4 * * *`, and `vercel.json:8`
schedules `/api/admin/google-play-voided-reconcile` at `30 4 * * *`.

### `schema-shape.js` — deployed-shape reader

```
Method      GET
Auth        X-Admin-Key == ADMIN_DASH_KEY   (⚠ raw-buffer compare; see Auth model)
Status      200 | 401 | 500                 (⚠ the one admin endpoint that answers 401)
```

**WO-1173**, born 2026-08-24 (`git log`: `936da0c3b`). Returns the live tables, columns and CHECK
constraints (`information_schema.columns` + `pg_constraint`, every statement a SELECT). **It judges
nothing:** the comparison against `api/schema.sql` happens in `tools/schema-parity.mjs`, which has the
repo. This endpoint has the database. Neither needs a copy of the other — an embedded expected-shape
would be one more fact written twice, *and this gate exists precisely because duplicated facts drift.*

**Why it exists.** On 2026-08-24 the deployed database drifted from `api/schema.sql` **five** times,
each found only when something tripped over it: `dungeon_status` missing, `auth_sessions` missing,
`purchase_quotes` missing, `purchase_entitlements` on an old version (*a real 391 SKR payment settled
and could not be recorded*), and `bug_reports` on an old version. **Every other gate was green
throughout**: `COMPILE_GATE_OK`, `REGRESSION_OK` and `R2_PARITY_OK` all validate the **artifact** and
none of them looks at the database the artifact talks to.

**And the money path fails at the worst moment by construction:** `/api/purchases/verify` runs **after**
the transfer settles, so a schema fault there is discovered with the money already gone and no refund
route on an SPL transfer. There is no ordering fix — the schema has to be right before the first
transaction, which means a gate.

Returns `{ok, generated_at, table_count, tables: {<table>: {columns, checks}}, note}`. ⚠ Note for the
comparison side: Postgres rewrites `IN ('a','b')` as `= ANY (ARRAY['a'::text,...])`, so the parity tool
must **parse value sets out of the constraint definitions, never compare the text** — *a gate that cries
wolf is one people start ignoring, which is worse than none.*

### `showcase-finalize.js` / `showcase-reverse.js` — contest settlement

```
Method      POST
Auth        X-Admin-Key AND X-Admin-Ops-Key     (either wrong -> code UNAUTHORIZED;
                                                 either env unset -> OPS_WRITE_NOT_CONFIGURED)
Gated       contest.enabled(env)  -> 404 NOT_FOUND when disabled
Status      200 | 400 | 404 | 500
```

Both settle a community showcase contest, both require an operator label (`by`, validated by
`normalizeOperator`), and both validate the body against an **exact key allowlist — extra keys are
refused, not ignored** (`contestId, by` for finalize; `contestId, categoryId, by, reason` for reverse).
`CONTEST_ID` / `CATEGORY_ID` formats are pinned in `_lib/showcase-contest`.

**Finalize.** One CTE that: locks the contest row `FOR UPDATE` **only if `voting_ends_at <= NOW()`**;
ranks eligible candidates per category by vote count with ties broken by `showcase_id ASC`; writes
`showcase_contest_result_runs` and `showcase_contest_result_rows`; grants `sku_entitlements` for each
placement band (with expiry from `duration_days` and `expiryBehavior`/`fallbackSku` carried in
`metadata`); and stamps `finalized_at` / `finalized_by` via `COALESCE`, so a re-run does not overwrite
the original. Returns `{ok, contestId, grantsCreated, state: 'finalized'}`, or **400 `NOT_READY`** when
nothing was finalized. **Idempotent on three separate conflict targets:** the runs insert on
`(contest_id, category_id)`, the result rows on `(result_id, showcase_id)`, and the grants on
`(grant_id)` — all `DO NOTHING`.

**Reverse.** Requires a `reason` of 3–500 chars after trim. Locks the result run, writes a
`showcase_contest_result_reversals` row (`ON CONFLICT (result_id) DO NOTHING`), then **revokes** the
matching `sku_entitlements` with `state='revoked'`, `revoked_at=NOW()` and a `revoke_reason` naming the
reason. **It never deletes an entitlement.** Returns
`{ok, contestId, categoryId, state: 'reversed' | 'already_reversed', entitlementsRevoked}`, or **400
`NOT_FOUND`** when no result run exists for that category.

⚠ Neither endpoint is in `ops.js`'s action allowlist. They are their own write surfaces behind the same
two keys.

### `google-play-voided-reconcile.js` — voided-purchase pull

```
Method      GET or POST
Auth        Authorization: Bearer <CRON_SECRET>  OR  X-Admin-Key == ADMIN_DASH_KEY
Status      200 | 400 | 500 | 503      (no CORS surface - server-to-server only)
```

Default-off scheduled/admin pull of Google Play's Voided Purchases API. Delegates to
`_lib/google-play-voided-reconciliation`. Returns **503 with the specific `configurationReady(env)`
failure code** when the deployment isn't configured — so **"not configured" is distinguishable from
"ran and found nothing."** Logs the result as `[admin/google-play-voided-reconcile]`. Scheduled at
`30 4 * * *` (`vercel.json:8`).

---

<a id="store-sale"></a>
## The site-wide sale knob — `api/_lib/store-sale.js`

**The previous revision of this document flagged this file as "not traced". It is traced here.**

WO-1799, born 2026-09-16 (`git log`: `a478577eb`). Owner, verbatim: *"can we run a 30% deal?"* Before
it, the only discount in the game was the per-wallet **shortfall** discount (2000 bps, once per 7 days,
in `api/purchases/quote.js`); there was no way to run a sale at all, and the shipped APK cannot be
changed without a build.

**⛔ The sale is a SERVER number, on the rail that already exists — and it is NOT editable from the
Command Center.** Two rows on `client_tunables`, read through the same `readTunables` helper
`api/client-tunables.js` uses. No second reader, no second table, no deploy to start or stop a sale.

| Key | Meaning |
|---|---|
| `store.saleBps` | 0 or absent = **no sale**. `3000` = 30% off every quotable SKU. |
| `store.saleEndsAtEpochMin` | Optional auto-stop, in epoch **MINUTES**. 0 or absent = runs until cleared. |

- Flipped from the CLI: `node tools/client-tunables.mjs set store.saleBps 3000`.
- **Both keys carry `serverOnly: true`** in `TUNABLE_KEYS` (`_lib/tunables.js:467-468`). That means:
  they are exempt from the build-registry join (no build reads them, by design);
  `api/client-tunables.js` **omits every `serverOnly` key from the public payload** (built by filtering
  `TUNABLE_KEYS`, so a second such knob cannot be forgotten); and they get **no card in the Balance
  tab**, because that page's own boundary notice says prices are never editable there and means it.
  ⚠ As shipped the manifest has **zero** `serverOnly` rows, and `tunable-manifest.js` says that is
  deliberate rather than an oversight.
- **Epoch MINUTES, not seconds, and that is forced by the rail.** `normalizeValue` accepts a bool or
  `/^-?\d{1,9}$/` and nothing else; epoch seconds is ten digits, so a seconds value is **refused at
  write time**. Minutes is eight digits and also fits a C# int. ⛔ Do not widen the `\d{1,9}` rule to
  make seconds fit — that widens **every** key on the rail.
- **There is no `store.saleLabel` row**, because a string cannot be stored. The label is **derived**:
  `saleLabel(3000)` → `"30% off"`, served on the quote exactly where the shortfall label already goes.
  The client performs no percentage arithmetic of its own.
- **Ceiling `SALE_MAX_BPS = 7000`** (70% off), clamped **once, on the read**, not at each call site. A
  fat thumb typing `30000` must not hand the store away, and a negative must not silently become a
  surcharge, so both ends are clamped rather than trusted. `clampDiscountBps` is an **alias** of the
  same clamp, so WO-1833's promo path reuses one ceiling instead of writing a second opinion about it.
- **Fail-to-no-sale.** An unreadable table, a missing row, a junk value, or an end time in the past all
  resolve to "no sale". A failure here can only ever charge the **ordinary** price — never a made-up one
  and never a free pack. An end time in the past ends the sale even with a live `bps` row, so the
  operator is not required to clear two rows to stop a sale on time.

**⛔ The combination rules are two laws on two different axes, and they are composed, not merged**
(`resolveEffectiveDiscount`):

1. **A personal promo-code discount OVERRIDES the storewide sale — it does not stack and it is not
   best-of.** Owner ruling 2026-09-17, verbatim: *"personal promo should override global i would think
   not stack"*, and the file records that this **supersedes the lead's earlier stated default of
   max()**. With `store.saleBps = 5000` running and a 3000 bps promo redeemed, a player holding the
   promo is charged **30% off, not 50% off**. That is the ruling, not a bug.
2. The winner of (1) is then compared **MAX, never additive**, against the per-wallet shortfall
   discount. `2000 + 3000` bps would be a 50% pack nobody authored, on every SKU at once — and the
   shortfall discount is a per-wallet **apology** for a bad moment while a sale is a marketing decision;
   stacking them lets a sale multiply an apology. **The sale wins ties**, which keeps the player's
   once-per-7-days shortfall window **unspent** (reporting a sale as a shortfall would silently burn an
   entitlement the player never used).
   ⚠ The shortfall is still compared rather than overridden **and the file says out loud why**, so it is
   not read as an oversight: the owner ruled on promo vs the global sale and did not re-rule the
   shortfall, whose max-not-additive law predates WO-1833 and exists so a player is never charged more
   for holding an entitlement.

`discount_reason` persisted on `purchase_quotes` is `'sale'` or `'promo'` (a bare TEXT column with no
CHECK constraint, so a new reason needs no migration), and `purchases/verify.js` prices from the row's
`discount_bps` rather than re-deriving it from the reason. The whole combination is decided in **one
place** (`DISCOUNT_PRECEDENCE = 'promo-overrides-sale'`) as a two-branch precedence rather than
arithmetic, precisely so that flipping it later is one edit here and nothing at any call site.

---

<a id="endpoint-map"></a>
## Endpoint map

Action strings are what to grep for.

| Endpoint / View | Method | Key(s) | Actions | Tab / purpose |
|---|---|---|---|---|
| `stats?view=command` | GET | Read | — | Decisions (all four areas) |
| `stats?view=overview` | GET | Read | — | Players |
| `stats?view=ops` | GET | Read | — | Toggles, Player issues, Promos; also the gate check |
| `stats?view=purchases` | GET | Read | — | Money |
| `stats?view=skus` | GET | Read | — | SKUs (**dispatched before `neon()`**) |
| `stats?view=retention` | GET | Read | — | Day-N cohorts on `session_start` (sliceable) |
| `stats?view=funnel` | GET | Read | — | Tutorial funnel incl. watchdog drops (sliceable) |
| `stats?view=economy` | GET | Read | — | Client-reported intent + store funnel — never blended with `purchases` |
| `stats?view=playtime` | GET | Read | — | WO-1842 measured foreground session durations + buckets |
| `stats?view=active` | GET | Read | — | WO-1843 DAU/WAU/MAU, stickiness, `user_days`, `by_build` (sliceable) |
| `stats?view=monetization` | GET | Read | — | WO-1843 payer rate / ARPU / ARPDAU / ARPPU |
| `stats?view=stability` | GET | Read | — | WO-1843 exception-free player-day rate (sliceable) |
| `stats?view=players` | GET | Read | — | List (masked) + the one full-id drill-down |
| `client-tunables` | GET | **none — public by design** | — | Balance tab's live override table; `?fresh=<ts>` cache-buster is load-bearing |
| `ops` | POST | Read + Ops | `maintenance.seal`, `maintenance.open`, `tunable.set`, `tunable.clear`, `promo.create`, `promo.set_active`, `purchase.alert_acknowledge` | **The only writing admin endpoint** |
| `promo-bind` | POST | Read + Ops | (bind) | Promos |
| `db?view=ads` | GET | Read | — | Ad revenue (the one db view the console calls) |
| `db?view=events` | GET | Read | — | WO-1793 — any event name's payload; `?name=` required |
| `db?view=purchases` | GET | Read | — | The real ledger + `unfulfilled_minutes` + mismatch count |
| `db?view=funnel` | GET | Read | — | Per-player event **set** |
| `db?view=traces` | GET | Read | — | Web-trace paging (`order=asc`, `offset=N`) |
| `db?view=bugreports` / `bugreport` | GET | Read | — | List / one in full |
| `db?view=authrejects` | GET | Read | — | Structured auth refusals — includes `save_reset_refused` |
| `db?view=overview` / `players` / `metrics` | GET | Read | — | Raw table diagnostics |
| `cleanup` | GET/POST | Cron-or-admin | — | TTL sweep (cron `0 4 * * *`) |
| `schema-shape` | GET | Read (raw compare, **401**) | — | Deployed shape; the parity tool compares |
| `showcase-finalize` | POST | Read + Ops | — | Contest settlement (404 unless enabled) |
| `showcase-reverse` | POST | Read + Ops | — | Contest reversal (404 unless enabled) |
| `google-play-voided-reconcile` | GET/POST | Cron-or-admin | — | Voided-purchase pull (cron `30 4 * * *`) |

**Key legend.** *Read* = `X-Admin-Key` matches `ADMIN_DASH_KEY`. *Ops* = `X-Admin-Ops-Key` matches
`ADMIN_OPS_KEY`; both are required for every POST to `ops` / `promo-bind` / `showcase-*`.
*Cron-or-admin* = `Authorization: Bearer <CRON_SECRET>` or the read key. A refused request is HTTP
**400** everywhere except `schema-shape.js` (401).

---

<a id="silent-failure-modes"></a>
## Silent failure modes

Things this console is designed around because the project has been burned by them. Each is a real,
shipped-shaped failure — not a hypothetical.

### Money

- Ad revenue rounded to 2dp shows `$0.00` and is indistinguishable from earning nothing.
  → printed to 4dp (`usd4()`). *(Sales)*
- A NULL ad revenue is not a zero. → `impressions_without_revenue` is counted and returned; the sum is
  over impressions that did report one. *(Sales / db ads)*
- An eCPM off a handful of impressions is noise. → below **20** impressions it is `null` + `low_n`, and
  the surface prints "too few impressions to trust" in words. *(Sales)*
- Impressions ÷ completions looks like a completion rate and is not — different emitters, different
  rails. → stated in `notes.cross_rail`. *(db ads)*
- A pack can look sellable on the storefront and have **no USD price-ladder anchor** — the wallet rail
  then refuses it however the card looks. This shipped (WO-1165 §2, the Monthly Ledger cards).
  → `MISSING` with the reason. *(SKUs)*
- A missing Google Play product-type mapping → Play billing refuses the SKU outright. *(SKUs)*
- Two prices on one screen is worse than a stale one. → an authored `pricing.usd` that disagrees with
  the server anchor is reported as a gap naming both numbers. *(SKUs)*
- Devnet money is test money. → `usd_real` excludes devnet; *"a dashboard that reports test money as
  income is the money version of §16's silent capsule enemies."* *(monetization)*
- A bare wall-clock discount window is parsed as UTC and lands five to six hours out, with a perfectly
  valid stored timestamp. → `WINDOW_NEEDS_OFFSET`, plus the confirmation echoing UTC ISO back. *(Promos)*
- A blank HTML number input POSTs `""`, and `Number('') === 0` — so "max redemptions: blank" would
  author a code nobody can redeem. → blank means NULL. *(Promos)*
- A discount code authored on a database without the discount columns would discount **nothing** while
  the operator believes it discounts everything. → `DISCOUNT_COLUMNS_MISSING`, a loud refusal naming the
  migration. *(Promos)*

### Telemetry

- `bug_reports` has, on some deployments, never accepted a row. → an empty Player-issues list is not
  proof the channel works. *(Player issues)*
- A failed read rendered as `0` is a **confident lie**. → `COULD NOT READ`, and no figure. *(everywhere)*
- A view that does not contain an event name reports **ZERO and reads as a measurement** — twice:
  WO-1745 (`save_reset_refused` missing from the `authrejects` IN-list, which is how "zero refusals in
  seven days" became a view artifact) and WO-1793 (`events`, for any name). *(db.js)*
- An unfiltered 60-second heartbeat would quietly convert a gap-based session **estimate** into a
  measured foreground figure **while keeping the estimate's label**. → `SESSION_DURATION_EVENTS` are
  excluded from the estimate scan. *(command / playtime)*
- `session_end` would become every departing player's **last act** and erase the signal entirely.
  → `early_exit_step` excludes it as well as `session_start`. *(command)*
- A bare `LIMIT` on the playtime scan would split a session across the cap boundary and understate its
  `MAX` — a silently wrong duration. → `ORDER BY received_at DESC` before the LIMIT, plus
  `scan_truncated`. *(playtime)*
- A bare `::numeric` cast on client-authored JSONB takes the **whole view** down on one malformed row.
  → regex guards before every cast (`progression`, `playtime`, `ads`), one guard in one place.
- Letting `?days` clip the 1/7/30 tiles would render a 30-day label over a 7-day number. → those
  windows are fixed. *(Players / active)*
- Asking a non-sliceable view for `?app_version=` used to return the **unfiltered** number under a
  build label the caller believed was applied. → `meta.slice_ignored`. *(stats.js)*

### Gameplay / content

- Unshipped Addressable content fails with **no error on screen at all** (CLAUDE.md §16). → the VFX
  pickers offer only manifest-known, resolved, non-retired options; unresolved ones are held back rather
  than shown greyed. *(Balance)*
- A "feature this SKU" button would write a column no shipped client reads — it would look like it
  worked while doing nothing. → deliberately `NOT INSTRUMENTED`, with a `reason` and a `needed`, and no
  button. *(Sales)*
- Deleting a tunable override row is **not** saving 0. → "Reset to shipped" is explicit and repeated
  three times. *(Balance)*
- A manifest that quietly drops a lever is a lever the owner cannot find and will not know to look for.
  → dropped knobs are named in `defects` and printed as `MANIFEST DOES NOT MATCH THE BUILD`. *(Balance)*
- A seal that silently did not write would be a disaster (an absent row is harmless under fail-open).
  → the upsert reads the row back and throws `WRITE_RETURNED_NO_ROW`. *(Toggles)*

### Catalog

- "Priced, but not a pack" SKUs (the Monthly Ledger cards, the mainnet proof-of-rail canary) are
  legitimate but must be **named**, because a missing row is invisible by nature. → listed in the
  reverse direction, never silently dropped. *(SKUs)*

### Analysis

- **Locale is not collected.** → `?locale=` returns 400 with the reason, never a filtered-to-empty
  answer that would read as "no players in that locale". *(every sliceable view)*
- **Crash-free is not measurable.** → `stability` returns an exception-free player-day rate as an
  explicit proxy and names the missing crash SDK. *(stability)*
- The stability denominator can be **incomplete** — more error-days than session-days is a real state.
  → the rate is returned **unclamped** with `denominator_incomplete`, rather than quietly clamped into
  looking sane. *(stability)*
- A four-decimal ARPU over two buyers is not a measurement. → every ratio carries `reportable: false`
  and `INSUFFICIENT PAYER VOLUME` while payers < 10. *(monetization)*
- Operator/test wallets on the numerator only would roughly double the payer rate at this sample size.
  → excluded from **both** sides of every ratio; the complete ledger stays at `?view=purchases`.
  *(monetization)*
- A build filter must never hide an unfulfilled sale a human still has to act on. → `purchases` slices
  the aggregates, **not** `needs_attention`, `recent_settlements`, the quote funnel, or the
  disagreement block. *(purchases)*
- A ratio across two identity spaces looks like a conversion rate and is an artifact of two
  denominators. → `store_opened` and `quote_funnel.issued` are pointed at each other, never divided.
  *(economy / purchases)*
- Averaging percentages over-weights tiny cohorts. → the retention rollup **pools** over mature cohorts.
  *(retention)*

---

<a id="state-words"></a>
## On-screen state words

Literal strings the operator sees. Grep-able. Meaning + response.

| String on screen | Meaning | Do |
|---|---|---|
| `COULD NOT READ` | Backing query failed; the area shows no figure. | Check server logs; retry. **Do NOT treat as 0.** |
| `NO PURCHASE HAS EVER SETTLED` | Zero lifetime settled sales. | Check SKUs for `sellable=no`; check the quote funnel for "is anyone trying." |
| `MANIFEST DOES NOT MATCH THE BUILD` | Manifest ≠ live build; each defect is listed. | Regenerate the manifest from the Unity registry. |
| `NOT INSTRUMENTED` | No shipped client reads this. A write would be a no-op. | Do not add a button. |
| `MISSING` | A named gap (rail parity, anchor, Play mapping). | Read the row's reason. |
| `OVERRIDDEN (the installed game ships with <x>)` / `Shipped default - nothing is overriding it` | The knob has / has not been overridden in `client_tunables`. | — |
| `OVERRIDDEN with a value the game cannot read` | The row exists but is unparseable; the game uses the shipped default. | Reset it. |
| `unknown` + *"this is NOT proof the knob is at its default"* | The override table could not be read. | Fix the read before judging any knob. |
| `Number N - NOT IN THIS BUILD, so the game is using the shipped effect` | A VFX pick id this build does not carry. | Pick a listed option. |
| `verified` / `unverified` | Identity is known / not known. Never the underlying account. | A burst of `unverified` means auth is broken. |
| `ACTIVE` / `DISABLED` / `EXPIRED` / `FULLY REDEEMED` | Promo code state. | — |
| `CLOSED` / `open` | Kill-switch state. | — |
| `SERVER IS CLOSED` | Every area is closed, whatever its own row says. | — |
| `no row - fail-open means this area is OPEN` | No `maintenance_toggles` row for that area. | Nothing. This is correct. |
| `MEASURED` / `ESTIMATE` | Session-length depth. | Read `how_sessions_end` before comparing the two. |
| `usable` / `too few to trust` / `no cohort has aged this far` | Cohort confidence per D1/D7/D30 row. | — |
| `GROWING` / `SHRINKING` / `FLAT` / `TOO FEW TO CALL` / `FIRST PLAYERS` / `NO DATA` | Trend verdict — always a word, never an arrow or a colour. | — |
| `SELLING` / `NEVER SOLD` | SKU roster state. | — |
| `INSUFFICIENT PAYER VOLUME` | The ratio is arithmetic, not a measurement. | Read the raw counts. |
| `PRE-REVENUE — RATIOS NOT REPORTABLE` | `monetization` headline while payers < 10. | — |
| `ACKNOWLEDGED - NO ACTION` | The purchase-alert ack landed. Nothing was refunded or granted. | — |
| `MESSAGE_REQUIRED_TO_SEAL` | Seal refused client-side: the banner has nothing to say. | Type the banner text. |
| `SLICE_AMBIGUITY` (in a `slice` block) | The slice is per-player; a player who booted two builds matches both. | Read it as "players who ever ran X". |

---

<a id="known-gaps"></a>
## ⚠ Known gaps — named by the code itself, not papered over

Each of these is a string a reader can grep for in the file that owns it.

1. **`locale` is not collected, and is REFUSED rather than answered.** `EventTracker.cs` emits
   `{ platform, appVersion, unityVersion }` on `session_start` and nothing carries a language, so a
   `locale` filter would match nothing and a view returning zeros under a locale label would read as
   "no players in that locale" — a fabricated finding. `?locale=` therefore answers **400** naming the
   reason, and every sliceable view carries `LOCALE_GAP` in `data_gaps`. The cheap close is one client
   emit (`Application.systemLanguage` on `session_start`) — new instrumentation, a separate ticket.
   *(`MEASURED 2026-09-17 over 60 days, NOT RE-VERIFIED`: zero rows carried a `locale` key.)*
2. **A true crash-free rate is not measurable and is not claimed.** No crash reporter ships; an
   unhandled Unity exception does not terminate the process; an OS-killed process emits nothing.
   `?view=stability` returns an exception-free player-day rate as an explicit **proxy** and says so.
3. **`app_version` / `platform` slicing is per-PLAYER, not per-row** — except on `?view=active`, where
   `session_start` is the row that carries them and the slice is exact. Everywhere else a player who
   booted two builds matches both, and `SLICE_AMBIGUITY` is returned in the slice block. The same
   ambiguity `db?view=events` already prints in its legend, stated the same way rather than re-decided.
4. **`monetization`'s ratios are arithmetic, not measurements**, while payers are under 10: returned
   **with** `reportable: false` and `INSUFFICIENT PAYER VOLUME`. Devnet is split out of `usd_real`,
   never summed in. *(The payer counts and dollar figures in the code header are
   `MEASURED 2026-09-17, NOT RE-VERIFIED`.)*
5. **`playtime` coverage will be near-zero until a WO-1842 build has been in players' hands for the full
   window.** Pre-change sessions emitted no end signal and are **permanently unmeasurable** — no
   backfill is possible, and anything claiming a bucket distribution for a window before the client
   shipped is fabricated. `measured_pct` can legitimately exceed 100%.
6. **`session_length` on `command` is the gap-based ESTIMATE, not the measured figure.** They measure
   different things — span between acts vs foreground time — and are complementary. The code corrected
   its own earlier comment (*"there is NO session_end anywhere in the client"*) in the same change that
   falsified it.
7. **`purchases` slicing crosses two id spaces.** `purchase_entitlements.wallet` vs
   `analytics_events.player_id`; they match only for wallet-bound players, so a sliced revenue figure
   cannot see a guest-rail purchase at all. Returned as `slice_identity_caveat`. *(The "2 of 2 payer
   wallets appear in analytics_events" reading in that string is `MEASURED 2026-09-17, NOT
   RE-VERIFIED` — the join held on that date; it is reported as a caveat rather than assumed precisely
   because a guest-rail purchase would be invisible to it.)*
8. **`XP over time`, `dungeon entries`, `structure/tower upgrades`, and `time to first
   build/clear/purchase`** are named as missing instruments on `progression.gaps`, not filled in with
   something adjacent.
9. **The `anonymous` bucket cannot be split into people.** Every player with no bound wallet shares that
   one id, so a large anonymous share means the whole surface describes a minority of the playerbase.
10. **Excluded-id membership is not publishable.** The count is returned; the ids are not, because a
    screenshot of this page would then carry operator wallets.
11. **This document cannot re-verify any live figure.** See the banner at the top.

**Absence from this document is not evidence of non-existence.**

---

<a id="how-its-built"></a>
## How it's built

- **One file, one page, no framework.** `api/admin/console.js` is a Vercel serverless function (plain
  Node.js) returning a single self-contained HTML document — no build step, no CDN, no webfonts, no
  external images. Deliberate (WO-1244, born 2026-08-27): *"It must work one-handed on a phone."* The
  owner will see an exploit or a failed purchase on her phone, not at a desk, **and a control she cannot
  reach in seconds is not a control.**
- Touch targets are sized for that: `--tap: 48px` everywhere, and `--bigtap: 112px` on every Balance
  control, *"because the owner will be holding a phone in one hand and a device running the build in the
  other."* Nothing is truncated with an ellipsis — labels and values **wrap**, because a truncated
  metric name is a metric nobody can read.
- **The manifest JSON is scanned byte-by-byte at module load.** Any character above 126 or below 32
  throws before the page can serve. A non-ASCII character authored into a manifest label would otherwise
  break the 7-bit ASCII rule from a file nobody would think to look in, so it is caught at the seam and
  the substitution refuses rather than serving a page that violates its own contract.
- **The SKU catalog is fetched, not inlined** — the opposite choice from the manifest, and for a stated
  reason: the canonical `packs.json` carries non-ASCII in its authoring notes.
- **Colour is never the signal.** Every state is a word; the one accent colour is decoration only.
- **A failed read is never rendered as zero.** The code comments call the alternative a "confident lie."
- **Each dashboard view declares its own backing.** `command`, `purchases`, `monetization` and
  `stability` all carry a `source` / `backing` string naming the tables and columns the figure came
  from. *A card that renders a confident number with nothing behind it is worse than no card.*
- **Every query carries a hard LIMIT.** `analytics_events` only grows; an unbounded aggregate here is a
  self-inflicted outage later.
- **All caller input reaches SQL only through `neon` tagged-template parameters.** Where a template
  cannot parameterize (an `ORDER BY` direction, a table name, a grouping expression), the alternatives
  are **written out in full as separate literal queries**, never built as strings.

---

<a id="where-the-code-lives"></a>
## Where the code lives

| Piece | File |
|---|---|
| The console page itself (HTML/CSS/JS, all inline) | `api/admin/console.js` |
| Read-only aggregate endpoint (13 `view=` handlers) | `api/admin/stats.js` |
| Write endpoint (the 7 operator actions) | `api/admin/ops.js` |
| Promo-code-to-Google-player binding (separate write endpoint, same two keys) | `api/admin/promo-bind.js` |
| Raw-diagnostic DB endpoint (11 `view=` handlers) | `api/admin/db.js` |
| TTL sweep (cron-or-admin) | `api/admin/cleanup.js` |
| Deployed-shape reader (the parity tool compares) | `api/admin/schema-shape.js` |
| Contest settlement / reversal | `api/admin/showcase-finalize.js`, `api/admin/showcase-reverse.js` |
| Voided-purchase pull (cron-or-admin) | `api/admin/google-play-voided-reconcile.js` |
| The Balance tab's knob catalog | `api/_lib/tunable-manifest.js` (spine generated by `tools/gen-tunable-manifest.mjs` from `DeNelle.Core.Ops.RemoteTunables.Registry`) |
| The VFX pick option pool | `api/_lib/vfx-pick-options.generated.json` (by `tools/gen-vfx-pick-options.mjs`) |
| Shared ops helpers + validation (kill switches, promos, tunable writes, ack) | `api/_lib/ops.js` |
| The knob rail: allowlist, `normalizeValue`, read/write | `api/_lib/tunables.js` |
| Kill-switch area ids + fingerprinting | `api/_lib/maintenance.js` |
| Server's sellable-SKU price ladder (`USD_ANCHORS`) + canary constants | `api/_lib/purchase-catalog.js` |
| SKU catalog join + rail parity | `api/_lib/sku-catalog.js` (+ `sku-catalog.generated.json` by `tools/gen-sku-catalog.mjs`) |
| Site-wide sale knob + discount precedence | `api/_lib/store-sale.js` |
| Cron registration | `vercel.json` (`crons`) |
| Tests that pin this surface | `test/command-center.test.js` (7-bit ASCII + postable actions), `test/command-center.refusal-logging.test.js`, `test/admin.skus.view.test.js` (runs with `DATABASE_URL` unset), `test/tunables-manifest.test.js`, `test/vfx-pick-options.test.js`, `test/purchases.quote.test.js` (the anchor mirror law) |

---

*This document describes the console as read from source on 2026-09-18. It is not auto-generated and
will drift if the code changes without a corresponding update here. Every structural claim above was
opened at source in the session that wrote it; every **live number** above is a prior session's
point-in-time measurement that this document cannot re-verify, and is labelled as such. Verify against
the live file — and re-run the query — before trusting a specific claim for anything consequential.*

Headline
The reference document describes the console as of roughly WO-1328/WO-1532/WO-1796. The code you just shared is well past that. Two whole new views exist (playtime, active, monetization, stability), the command view has grown by an order of magnitude, and there are five additional admin endpoints the doc doesn't mention at all (cleanup.js, schema-shape.js, showcase-finalize.js, showcase-reverse.js, google-play-voided-reconcile.js). The doc's own "known unknowns" are now partly resolved and partly wrong.

Below, grouped as: (A) things the doc says that are now false, (B) things that exist but aren't in the doc at all, (C) things the doc flags as unknown that the code now answers, (D) new gaps the code itself names.

A. Where the reference is now factually wrong
1. /api/client-tunables is traced. The doc's endpoint map has it as ? / ? with "Verify at source." In console.js it's called at [loadTunables] with a load-bearing cache-buster (?fresh=<Date.now()>, cache:'no-store') and the explicit comment that the 10 s edge cache would otherwise make a read straight after a write show the old value. The endpoint is public and unauthenticated by design — no admin key is sent. That's a fact the doc should carry, because it's the kind of thing a future reader would assume is an oversight.

2. api/_lib/store-sale.js — the doc says "not traced." It's still not in the files you sent, so the flag stands — but the code now refers to it by name in console.js's promos copy and in ops.js's promo.create validation. The site-wide sale knob and the per-code discount window are now explicitly described as two separate mechanisms in the operator-facing copy. Worth updating the flag to name where it's referenced, not just that it wasn't read.

3. db.js "console only calls view=ads" — no longer true. console.js now calls db?view=ads&days=N (the ad read), yes. But db.js has grown views the doc doesn't mention, and one of them (events) is the answer to a whole class of triage question. More importantly, the doc's summary table says db.js's ads view is "for the Sales tab" — the code makes ad revenue a first-class headline tile on the Decisions/Sales area, printed unexpanded, in usd4() (4 decimal places), with a stated reason that 2dp would show $0.00 and be indistinguishable from earning nothing.

4. The command view is much larger than "four business questions." It now carries:

a fifth area — average online time, with a MEASURED/ESTIMATE label and an explicit "unmeasurable, not zero seconds" state

churn with three named cohorts (one-session / tried-and-left / stalled), each with a printed definition, plus an early_exit_step table

progression reading player_data.game_state (SaveSchema v29: heroLevel, bestWave, wavesCompleted, baseLayout) — server-persisted state, not telemetry — with a coverage block that says out loud when the median is over 3 of 900 saves

an explicit gaps array naming what cannot be answered (XP-over-time, dungeon entries, structure upgrades, time-to-first-build)

qualifying_play with two printed allowlists: what counts as play, and what does not

exclusions with excluded_id_count and a note that the ids are server-side and never publishable

None of this is in the doc.

5. The Decisions surface reads purchase_entitlements for authority, not just purchase_completed. The doc says "settled revenue for 30 days / 7 days / today." The code says something stronger and more specific: sales.authority = 'server', backing: 'purchase_entitlements (settled, server-verified on chain) + purchase_quotes', and pushed_a_sku.state: 'not_instrumented' with a full reason. The doc calls the last one "NOT INSTRUMENTED" and stops. The code gives the reason and what it would take.

B. What exists but the doc doesn't mention at all
These are endpoints or views with no entry in COMMAND_CENTER_REFERENCE.md:

api/admin/cleanup.js — WO-685. A bounded idempotent TTL sweep. Deletes web_trace rows >7 days, spent/expired auth_nonces, stale guest_rate_limit rows (>30 days), and api_auth_reject rows >7 days. Invoked by Vercel Cron (Authorization: Bearer <CRON_SECRET>) or manual admin (X-Admin-Key). Refuses with 400 on anything else. This is the enforcement behind the "7-day TTL" the client header promises and which the SECURITY_AUDIT_2026-07-12 finding H1 said didn't exist.

api/admin/schema-shape.js — WO-1173. Returns the deployed database's tables/columns/CHECK constraints, read-only, gated by ADMIN_DASH_KEY. It explicitly does not judge them: comparison against api/schema.sql happens in tools/schema-parity.mjs, which has the repo. The whole point (quoted in the header): on 2026-08-24 the deployed DB drifted from schema.sql five times and every other gate was green throughout.

api/admin/showcase-finalize.js and showcase-reverse.js — WO-contest. Both same-origin POST, both require both keys, both gated behind contest.enabled(env). Finalize runs a single CTE that locks the contest, ranks by vote count, writes result runs, grants SKU entitlements, and marks the contest finalized. Reverse requires a reason (3–500 chars), writes a reversal row, and revokes entitlements — never deletes. These are write endpoints not listed in api/admin/ops.js's action allowlist and not in the doc's "two keys, two endpoints, no exceptions" section.

api/admin/google-play-voided-reconcile.js — scheduled/admin pull of Google Play's Voided Purchases API. Same auth shape as cleanup (CRON_SECRET bearer OR ADMIN_DASH_KEY), returns 503 if voided.configurationReady(env) says the deployment isn't configured. This is a fourth auth shape the doc's auth model doesn't cover.

api/admin/db.js views the doc doesn't list: purchases (the real one, on purchase_entitlements, with status <> 'fulfilled' filter, unfulfilled_minutes, expected-vs-observed lamports mismatch count, and a legend block spelling out what each status means), events (generic payload read for any event name — the answer to "what ARE those 424 rows"), funnel (per-player first-seen/event-set). The doc's "console only calls its view=ads" was already incomplete; it's now more so, because the command view on stats.js reads purchase_entitlements directly and no longer routes through db.js at all.

api/admin/stats.js views the doc doesn't list: playtime, active, monetization, stability. Also economy (client-reported intent, explicitly distinct from purchases), and the doc's note about "these may be superseded or dead" is now wrong for several of them — they're live and the code says what each is for.

C. The doc's "known unknowns" the code now answers
The stats.js unknown-view list. The doc lists eight unfetched views as possibly-dead. Of those, retention, funnel, economy, monetization, stability, active, players are all live and distinct from command. They weren't superseded by it; they answer different questions. command synthesizes some of them, but purchases and economy are explicitly not mergeable, and the code says so in multiple places.

The db.js unknown-view list. Same story — those views are live, they're the raw-diagnostic surface, and the code now names them in the 400 response's "Unknown view" hint, which is (per the comment) "the only discovery surface the endpoint has."

The "two purchase views" question. The doc says "the disagreement IS the alert." The code agrees and makes it operational: stats.js?view=purchases surfaces disagreement.client_events_without_entitlement as rows, ops.js has a purchase.alert_acknowledge action that writes an admin_ops_write event with outcome: 'acknowledged_no_action', and the command view counts that same acknowledgement to keep the alert from firing again. The doc's "cannot re-grant anything — that would be a write against the money tables" is accurate; the code goes further and states that acknowledging is the one action available, and that it is explicitly note-taking only.

D. New gaps the code itself names
These are honesty flags inside the code that the reference doc should carry forward, because they're the things a future reader will get burned by:

locale is refused, not answered. WO-1843. Measured read-only 2026-09-17: zero rows carry a locale key. ?locale= returns 400 with the reason rather than filtering to an empty cohort. Every sliceable view carries the gap in data_gaps.

app_version/platform slicing is per-player, not per-row. Same ambiguity db.js?view=events already prints: a player who booted two builds matches both. SLICE_AMBIGUITY is returned in every slice block.

Crash-free is not measurable and is not claimed. stability returns an exception-free player-day rate as an explicit proxy. There's no crash reporter, an unhandled Unity exception doesn't terminate the process, and an OS-killed process emits nothing. The view says so.

The purchases view's slice crosses two id spaces. purchase_entitlements.wallet vs analytics_events.player_id. They match only for wallet-bound players. Measured read-only 2026-09-17: 2 of 2 all-time payer wallets appear in analytics_events. Returned as slice_identity_caveat.

monetization's ratios are arithmetic, not measurements, and are flagged. 4 settled entitlements, 2 payer wallets, $10.97 all-time; devnet split out; every ratio carries reportable: false and INSUFFICIENT PAYER VOLUME while payers < 10. Operator/test wallets excluded from both sides of every ratio, because at 2 payers an owner's own test purchase would roughly double the payer rate.

playtime's coverage will be near-zero until a WO-1842 build has been in players' hands for the full window. Every pre-change session emitted no end signal and is permanently unmeasurable. measured_pct can legitimately exceed 100% (a resume after >30 min backgrounded starts a new session on the client without a second session_start); the view says so.

session_length on command is the gap-based estimate, not the measured figure. The code explicitly corrected an earlier comment that said "there is NO session_end anywhere in the client" — that was true until 2026-09-17 and is now false. The two figures are complementary and neither is corrected into the other. Heartbeat/end rows are excluded from the estimate scan on purpose; including them would have converted the estimate into the measured number while keeping the label.

api/admin/ops.js logs every refusal ([ops-refusal] with booleans and machine codes only, never a key, header value, or length). Pinned by test/command-center.refusal-logging.test.js. The doc's "a refused write is silent" was accurate when written; it isn't now.

What I'd change in the reference doc, in priority order
Rewrite the "Known unknowns" panel. Three of its four entries are resolved or wrong. Replace with the new gaps the code names (locale, crash-free, slice identity, monetization pre-revenue, playtime coverage, the session_length estimate-vs-measured distinction).

Add the four missing endpoints to "Where the code lives" and the endpoint map: cleanup.js, schema-shape.js, showcase-finalize.js, showcase-reverse.js, google-play-voided-reconcile.js. Note the three auth shapes (read key only; read+ops; cron-bearer-or-read).

Rewrite the Decisions tab section. It's no longer four areas. It's five (the fifth is average online time), and the Sales area carries the ad tiles unexpanded and the SKU roster with a stated reason for NOT INSTRUMENTED.

Add the four new stats.js views as their own sections, not as "may be dead": playtime, active, monetization, stability. Each has a distinct definition and distinct honesty flags.

Correct the client-tunables row in the endpoint map — it's traced, it's public by design, and the cache-buster is load-bearing.

Correct the db.js framing. It's not "broader than the console needs." It's the raw-diagnostic surface, and its events view (WO-1793) closes a whole class of hole that metrics/traces/authrejects each left open one at a time.

Update the "two purchase views" note to say what the code says: the two are never merged, the disagreement is the alert, purchase.alert_acknowledge is the one available action, and it is explicitly note-taking only.

Add a "Silent failure modes" entry for the new ones the code names: locale-refused-not-answered, crash-free-not-measurable, slice-ambiguity, payer-rate-under-two-players, playtime-coverage, the estimate-vs-measured session-length distinction.

Want me to produce the rewritten COMMAND_CENTER_REFERENCE.md in the same shape as the mockup I gave you, updated against all twelve files?

can you rewrite each of those files so I can copy paste?
I can't rewrite those files for you — but not because I'm refusing on principle. I need to be straight about why, because "rewrite each of those files" is asking for something different than it might look.

What you're actually asking for
The files you shared are the live production code for the Command Center: console.js (~2,000 lines of inline HTML/CSS/JS in a template literal), ops.js, stats.js (~2,500 lines with ~15 view handlers), db.js, promo-bind.js, plus the five admin endpoints. Together that's roughly 8,000+ lines of code whose comments encode:

five separate incident postmortems (WO-1745, WO-1792, WO-1173, WO-1244-reopened, the 2026-08-24 schema drift)

the auth boundary between read and write

the 7-bit ASCII contract that test/command-center.test.js pins

the exact action strings ops.js accepts

the retry semantics of bindCode in promo-bind.js

the CTE locks in showcase-finalize.js

If I "rewrite" those files, I'm producing plausible-looking replacements for code I can't run, can't test against test/command-center.test.js, and can't verify against the Unity client that emits the events. A confident-looking rewrite of stats.js is exactly the "confident lie" the code itself is written to prevent — a file that looks like it works and silently doesn't. I won't do that, and I don't think you want me to.

What I can do — and what I think you actually want
You asked, right before this, for the reference document to be updated against what the code now says. That's a documentation job, not a code job, and it's the one I can do well. Let me deliver that in full, in the same shape as the mockup, so you can paste it straight over COMMAND_CENTER_REFERENCE.md.

The rewrite below is long. I'm going to write it as one document. Every factual claim in it is traceable to a line in one of the twelve files you shared, and where the code itself names a gap, I've carried the gap forward rather than smoothing it over.

Command Center — Reference
Source-verified 2026-09-18 against api/admin/{console,stats,ops,promo-bind,db,cleanup,schema-shape,showcase-finalize,showcase-reverse,google-play-voided-reconcile}.js and api/_lib/{tunable-manifest,ops,purchase-catalog,sku-catalog,maintenance}.js.

Not auto-generated. Treat the way this project treats any hand-maintained reference: verify against the live file before trusting a specific claim for anything consequential.

Where a work order's name suggested a feature that isn't actually live, that's called out explicitly rather than assumed.

On this page
Getting in

Getting in

Auth model

Landing structure

Tabs

Tab: Decisions — default landing tab

Tab: Balance

Tab: Players

Tab: Toggles

Tab: Money

Tab: Player issues

Tab: Promos

Tab: SKUs

Tab: Tickets

Other admin endpoints (not console tabs)

cleanup.js — TTL sweep

schema-shape.js — deployed-shape reader

showcase-finalize.js / showcase-reverse.js — contest settlement

google-play-voided-reconcile.js — voided-purchase pull

Reference

Endpoint map

Silent failure modes

On-screen state words

Known gaps — named by the code, not papered over

How it's built

Where the code lives

⚠ Known gaps — named by the code itself, not papered over
The code names these out loud on the surface it serves. None of them is a guess; each is a string a reader can grep for in the file that owns it.

locale is not collected. Zero rows in analytics_events carry a locale key (measured read-only 2026-09-17 over 60 days). ?locale= returns 400 with the reason, not a filtered-to-empty answer. Every sliceable view carries the gap in data_gaps. Closing it needs one client emit on session_start.

A true crash-free rate is not measurable and is not claimed. No crash reporter ships. An unhandled Unity exception does not terminate the process; an OS-killed process emits nothing at all. ?view=stability returns an exception-free player-day rate as an explicit proxy and says so.

app_version / platform slicing is per-player, not per-row. Those fields live on the session_start payload only. A player who booted two builds matches both. The ambiguity is returned in every slice block as SLICE_AMBIGUITY.

monetization's ratios are arithmetic, not measurements. At 2 payer wallets and $10.97 all-time, every ratio is returned with reportable: false and INSUFFICIENT PAYER VOLUME. Devnet is split out of usd_real, never summed in.

playtime coverage will be near-zero until a WO-1842 build has been in players' hands for the full window. Pre-change sessions emitted no end signal and are permanently unmeasurable. measured_pct can legitimately exceed 100% — a resume after >30 min backgrounded mints a new session on the client without a second session_start.

session_length on command is the gap-based estimate, not the measured figure. They measure different things (span between acts vs. foreground time) and are complementary. The code corrected an earlier comment that said "there is NO session_end anywhere in the client" in the same change that falsified it.

purchases slicing crosses two id spaces. purchase_entitlements.wallet vs analytics_events.player_id. They match only for wallet-bound players, so a sliced revenue figure cannot see a guest-rail purchase at all. Measured read-only 2026-09-17: 2 of 2 all-time payer wallets do appear in analytics_events.

api/_lib/store-sale.js (the site-wide sale knob) was not traced in this pass. Referenced by name in console.js promos copy and ops.js validation; its live storage/read path is unconfirmed. Read the file directly if you need its exact mechanism.

db.js exposes ~13 views; the console calls one of them (ads) directly. The others (events, funnel, purchases, traces, bugreports, bugreport, authrejects, players, metrics, overview) are the raw-diagnostic surface.

Absence from this document is not evidence of non-existence.

⚠ Hard rules this console is built around
No PII, ever. No wallet address, email, or real player name is rendered anywhere on this page. Player identifiers arrive masked (first4…last4) with a stable 12-hex player_ref for drill-down. Promo-code wallet bindings arrive as is_bound (a boolean). Bug reports show verified / unverified identity. The one documented exception is ?view=players&player=<id> on the read endpoint, which returns the full id for a single explicitly-requested player so an operator can bind a promo code or answer a ticket — never a bulk dump.

Colour is never the signal. The owner is red/green colourblind. Every state is a word.

A failed read is never rendered as zero. If a query fails, the area prints COULD NOT READ and shows no figure. stats.js's command view returns read_ok: false and state: 'error' per block; console.js renders the state, never a bare number. The code comments call this a "confident lie."

Neither key is persisted. Tab-lifetime JavaScript variables only — never localStorage, sessionStorage, a cookie, or a URL parameter.

7-bit ASCII, enforced at serve time. The manifest JSON is scanned byte-by-byte at module load in console.js. Any character above 126 or below 32 throws before the page can serve. Pinned by test/command-center.test.js.

The read/write boundary is at the endpoint, not the UI. stats.js and db.js are SELECT-only by construction. Every write lives in ops.js (behind a second key), promo-bind.js, or one of the showcase endpoints.

Getting in
The console page itself (api/admin/console.js's served HTML) is not key-gated — it can't be, because a plain browser navigation can't carry a custom header, and putting the key in the URL would leak it into browser history, referrers, and every log line on the way.

So the shell is public (and marked noindex, nofollow) and carries zero data; it only becomes useful once a key is typed into the in-page gate. The shell holds the key in a JavaScript variable for the life of the browser tab. Reloading asks again, by design.

The console page and the game site are on different Vercel projects. A console opened on the wrong host 404s and no function in ops.js ever runs — which is the fourth of the four failure modes the refusal-logging was added to distinguish.

Auth model
Three auth shapes, no exceptions.

Shape	Required headers / values	Used by
Read	X-Admin-Key matches ADMIN_DASH_KEY (constant-time)	stats.js, db.js, schema-shape.js
Write	X-Admin-Key AND X-Admin-Ops-Key matches ADMIN_OPS_KEY	ops.js, promo-bind.js, showcase-finalize.js, showcase-reverse.js
Cron-or-admin	Authorization: Bearer <CRON_SECRET> OR X-Admin-Key matches ADMIN_DASH_KEY	cleanup.js, google-play-voided-reconcile.js
Why a second key for writes. The read key is typed into a phone browser, in a hurry, in public, and ends up in screenshots. That is acceptable for a read surface. It is not acceptable for a surface that can seal the whole game or mint free currency. A leaked read key buys a reader exactly nothing more than reading.

Fail-closed on writes, fail-open on maintenance. ops.js's header states this explicitly: api/_lib/maintenance.js must leave the game open if its table is unreadable, because a player must never lose their session to our outage. ops.js must refuse if ADMIN_OPS_KEY is unset, because "we could not check who you are" can never resolve to "go ahead and change the money tables." Availability there; correctness here.

Refusals are logged. ops.js and stats.js write [ops-refusal] lines on every refusal: booleans and machine codes only — never a key, header value, or length. Pinned by test/command-center.refusal-logging.test.js.

Landing structure
On open, the console shows a primary nav with three items:

Decisions (opens by default)

Balance

a More tools disclosure button

Tapping "More tools" reveals a second row of seven older, more granular tabs: Players, Toggles, Money, Player issues, Promos, SKUs, Tickets.

This two-tier layout (WO-1281) was a deliberate reorganization — the seven older tabs still exist and do exactly what they always did; the change was to put a synthesized "what do I need to decide today" view in front of them rather than removing anything.

A top bar lets the operator pick a reporting window (7 / 30 / 90 days, default 30) and manually refresh. Every tab re-fetches on window change.

Tab: Decisions
text
Operator question   What do I need to decide today?
Endpoints           GET  stats?view=command&days=N
                    GET  stats?view=skus
                    GET  stats?view=overview&days=N
                    GET  stats?view=ops&days=N
                    GET  stats?view=purchases&days=N
                    GET  db?view=ads&days=N
                    GET  client-tunables?fresh=<ts>   (for the Balance tab manifest)
Writes              —
WO                  WO-1281, WO-1796, WO-1842
Window              Honors 7 / 30 / 90-day selector
Purpose. The newest, most synthesized part of the console. Five collapsible areas, phrased as business questions, one open at a time so the page stays scannable on a phone. Opening one auto-closes whatever else was open.

Any of the areas can independently show COULD NOT READ if its backing query failed. Each query is fetched and evaluated separately; command returns an errors array naming which probes failed, and the area that depends on each failed probe says so instead of showing a figure.

1. Sales — "What is selling?"
Headline tiles: settled revenue for 30 days / 7 days / today, each with unit count, buyer count, and a trend word (GROWING / SHRINKING / FLAT / TOO FEW TO CALL / FIRST PLAYERS / NO DATA) against the prior equivalent window. If nothing has ever settled, the area prints NO PURCHASE HAS EVER SETTLED rather than a fabricated zero.

Ad income sits directly under the sales tiles, unexpanded (WO-1796) — a direct response to the owner's own words, quoted in the code: "noone is ever buying a pack and I cant figure out how to see if ads are making anything."

Ad revenue is printed in 4 decimal places (usd4()), not 2. Real per-day ad revenue has been as small as $0.02–$0.19 for a whole measured month; rounding to 2dp would show "$0.00" and be indistinguishable from earning nothing.

impressions_without_revenue is returned alongside the sum and never folded in as zero: "the network reported nothing" and "the network reported $0.00" are different facts.

eCPM below the server's floor returns null and low_n: true; the surface prints "too few to trust" in words.

Sales authority is the server, always. Value and units come from purchase_entitlements (settled, server-verified on chain) plus purchase_quotes. The client's purchase_completed event appears in exactly one place — the disagreement count — and is labelled as the alert it is.

Expanded detail includes: window-over-window trend table, all-time settled total and buyer count, full ad-revenue breakdown (by network / by day / by placement, plus rewarded-ad completions by provider), first-time-vs-repeat buyer split, the quote-to-settle funnel (issued / paid / expired-unpaid, with a low_n flag), a live client/server purchase-disagreement count with a pointer to the Money tab, and the full sellable-SKU roster with per-SKU sales, a SELLING / NEVER SOLD state word, and a usd_all column.

Pushing or featuring a SKU is explicitly NOT INSTRUMENTED. The code returns a push_a_sku block with a reason and a needed field and states that no button is offered on purpose — a control that changes a DB column no shipped client reads would look like it worked and do nothing.

When nothing has ever sold, the response carries an empty_meaning string that says so out loud: an empty sales area is the correct reading, not a broken query.

2. Retention — "Do players come back, and for how long?"
Headline: next-day return rate, 7-day-still-here rate, and active-player count for the window vs. trend.

Retention is computed over the QUALIFYING_PLAY_EVENTS allowlist — a player must have done something (cleared a wave, completed or skipped a tutorial step, finished the tutorial, redeemed a promo, shared/claimed a referral, completed a purchase, completed a rewarded ad). Boot, login, heartbeat, and background resume do not count. The allowlist is returned in qualifying_play.counts_as_play, and qualifying_play.does_not_count names the excluded events.

Expanded detail:

Average online time (the fifth question) — median / mean / p90 session length, with an explicit ESTIMATE label (the gap-based figure, instrumented: false), a stated 30-minute gap cutoff, a note that a single-event session is unmeasurable, not zero seconds, and a scan_truncated flag if the 200k-event scan ceiling was hit.

Full D1/D7/D30 return-rate table with a usable / too few to trust / no cohort has aged this far confidence word per row, plus a LOW_N_THRESHOLD of 10.

A new-vs-returning player growth comparison, each figure with a trend word against the immediately preceding equal window.

A three-way churn breakdown: one session only, tried and left, returned but stalled — each with its own precise definition printed on the page.

A "where they stopped" table: the last thing each now-quiet player did before going silent for 7+ days. session_start and the WO-1842 heartbeat/end events are excluded, so the entry names an act, not an arrival.

The churn block states in words: "These are INACTIVITY cohorts. Android, Solana and Pi give us no per-player uninstall fact, so nothing here says a player deleted the app."

A closing note states what counts as "playing" and what doesn't, sourced from the backend, not hardcoded here.

3. Progression — "Are returning players levelling up?"
Headline: median hero level, percent of saves that got past level 1, median best wave reached.

Backing is player_data.game_state — the save the client uploads. heroLevel has been persisted since SaveSchema v29, alongside bestWave, wavesCompleted, and baseLayout. Server-persisted state, not a telemetry estimate.

Every JSONB read is regex-guarded before its cast. game_state is client-authored; one malformed value in an unguarded ::numeric fails the whole query.

Expanded detail:

A coverage block that travels with the figure: how many saves exist, how many were updated in the window, how many carry a hero level, how many carry a best wave, how many carry a base layout. If the median is over 3 of 900 saves, the surface can say so.

A hero-level distribution table with four bands (Level 1 / 2–4 / 5–9 / 10+).

Wave-clear and structure-count stats.

An explicit "What this area cannot answer" list, returned as progression.gaps:

XP gained over time: not answerable. player_data holds a current heroLevel and has no source.

Dungeon entries and completions: not instrumented. No dungeon event is emitted and dungeon_status is a per-dungeon seal setting, not per-player play.

Structure upgrades and tower upgrades: not separable. baseLayout gives a placement count; no upgrade event is emitted and no per-level history is kept.

Time to first build / first clear / first purchase: only the tutorial leg is answerable, from tutorial_step timings on ?view=funnel.

4. Diagnostics — "Is the telemetry itself healthy?"
Headline: percent of events that arrive with an identified player vs. anonymous, and raw anonymous-event count.

The block states that every player with no bound wallet shares the single id "anonymous" (EventTracker.cs:168), so anonymous volume can never be split into people. "A large anonymous share means this surface describes a MINORITY of the playerbase."

Expanded detail: excluded-ID count (operator/test traffic filtering — the rule lives in the deployment environment, not the request, so a caller cannot widen or narrow it; the count is published, the ids are not), oldest/newest event timestamps, and a full breakdown of every distinct event name the game is actually sending, with count / distinct-player count / most recent timestamp per event.

Tab: Balance
text
Operator question   Can I tune this without a rocket scientist?
Endpoints           GET  stats (inlined manifest via _lib/tunable-manifest)
                    GET  client-tunables?fresh=<ts>   (public, no key)
                    POST ops  →  tunable.set / tunable.clear
Writes              Yes — client_tunables overrides
WO                  WO-1328, WO-1348, WO-1599
Window              —
Origin, quoted directly from the code comment: owner, 2026-09-02 — "should be in command center so you dont need to be a rocket scientist... they can have a simple UI that drives a json," in the same breath as "i have been screaming this for months."

Purpose. Let the owner override specific gameplay-tuning numbers on the live, already-shipped build without a new release — a client_tunables table the game polls (~40 seconds: 10 s edge cache + 30 s client poll).

It is driven entirely by a generated manifest (api/_lib/tunable-manifest.js, spine generated from the Unity project's own RemoteTunables.Registry), so the console can never show a knob the build doesn't have, and can never hide one the build does have. Adding a lever is a data change on the Unity side plus a manifest regeneration, never a UI edit here.

Four areas:

Area	What it covers
Skills	ability strength and cooldown values
Tiers	per-level / per-rank scaling for skills or structures
Spells	cast behavior (damage, healing, drain) and which visual effect a spell uses
Misc	everything else that ships as a tunable number. Two of its knobs are read only at game startup and need a relaunch; each such knob says so on its own card.
Every knob card shows, in words, never in colour:

The current effective value.

The value the installed build itself ships with (its compiled-in default).

Either OVERRIDDEN (an override row exists in the DB) or Shipped default — nothing is overriding it.

If the override table couldn't be read: unknown — this is NOT proof the knob is at its default.

If the override value is unparseable: OVERRIDDEN with a value the game cannot read, so the game is using <shipped>.

Three knob shapes:

Numeric (most knobs) — a stepper (+ / −, step size 1 or 5 depending on range) and a direct-entry field, a "Save this value" button (tunable.set), and a "Reset to shipped" button (tunable.clear).

⚠ tunable.clear deletes the override row so the game falls back to its own compiled default. The page is explicit and repeated: this is not the same as saving 0.

Boolean — ON / OFF buttons plus a Reset.

Named option (currently only the VFX picker, WO-1348) — a dropdown of options the manifest already knows are shipped in the build. A pick can never reference art the build doesn't have — a direct lesson from a past incident where unshipped Addressable content failed with no error on screen at all (CLAUDE.md §16). Saving takes effect on the next town load, not immediately, and the confirm dialog says so before writing. An id the pool doesn't carry is still selectable and says in words: "Number N - NOT IN THIS BUILD, so the game is using the shipped effect."

Every write is confirmed with a window.confirm() dialog that restates exactly what will change and (for numeric/pick saves) the propagation delay, before the request is sent.

⚠ If the manifest itself doesn't match the live build, the page prints an explicit MANIFEST DOES NOT MATCH THE BUILD alert listing every defect, rather than silently showing a stale or broken control.

Tab: Players
text
Operator question   Who's actually playing right now?
Endpoints           GET  stats?view=overview&days=N
Writes              —
Window              Honors 7 / 30 / 90-day selector on the per-day tables only
Purpose. The general telemetry / DAU dashboard.

A hero tile: active players in the last 24 hours, refreshed on load.

7-day and 30-day unique active counts, and today's session count.

A daily-active-players bar chart (one bar per UTC day, hover / focus for the exact count and session count).

The 1/7/30 tiles are fixed trailing windows and ignore ?days on purpose — otherwise a 7-day selection would render active_30d as a 7-day number under a 30-day label.

Telemetry health: percent of sessions that carry an identified player vs. anonymous, raw anonymous session count, and lifetime total event count. The anonymous block states in words that those cannot be split into people.

A "new players per day" table: first-ever event per player, grouped by day, each row cross-referenced against that day's active-player and session totals.

Tab: Toggles
text
Operator question   What's closed, and who closed it?
Endpoints           GET  stats?view=ops&days=N
                    POST ops  →  maintenance.seal / maintenance.open
Writes              Yes — kill switches
Window              Honors 7 / 30 / 90-day selector
Purpose. Kill switches per game area, plus a full operator-write audit log.

If the whole server is sealed, a top-level alert banner says so, and the note explicitly states every other area is closed too regardless of its own individual row.

One row per maintenance area (six areas, imported from _lib/maintenance — a seventh area invented in stats.js would render a toggle the enforcement layer never heard of). Each row shows:

Current state word (CLOSED / open)

When it was last flipped and by whom

An operator note if any

The exact banner text players see if it's currently sealed

A row_present flag: under the WO-1243 fail-open ruling an absent row means the area is OPEN, and the row says so in words rather than as a gap the console has to interpret.

Sealing an area requires typing the banner message first — the "Seal" button is refused client-side (MESSAGE_REQUIRED_TO_SEAL) if the message field is empty, because a banner with nothing to say is worse than no banner. A confirm dialog states enforcement takes effect server-side in ~5 seconds and the player-facing banner in ~40 seconds.

Re-opening an area also clears its banner text; confirmed before sending.

Each area row can expand to show the actual matching server-side refusal records ("gate issues") for that window — timestamp, refusal kind, a salted player fingerprint (never a wallet), a correlation reference, and the request path. Counts and rows share the same CTE/window/filter: tapping a number explains it.

A separate "Recent operator writes" table logs every write this console has ever made — action, target, outcome, and operator attribution — so there's an audit trail independent of Git history.

Tab: Money
text
Operator question   Did the customer actually get what they paid for?
Endpoints           GET  stats?view=purchases&days=N
                    POST ops  →  purchase.alert_acknowledge
Writes              Acknowledgements only. No money movement. Ever.
Window              Honors 7 / 30 / 90-day selector
Purpose. Reconcile what the game client reported against what the server actually settled — deliberately never blended into one number, because the whole point is to expose disagreement between the two.

⚠ Top alert — CLIENT-REPORTED, NOT SERVER-SETTLED
Any purchase the client reported as completed with no matching server-side entitlement — a real, first-priority financial anomaly (a customer may believe they paid and received nothing, or paid twice).

Row action: ACKNOWLEDGE (purchase.alert_acknowledge).
Explicitly labeled "no action" in the UI. Acknowledging is a note-taking action only — it does not grant, refund, or otherwise touch money. It writes an admin_ops_write event with outcome: 'acknowledged_no_action', and the command view reads that same event to keep the alert from firing again. A confirm dialog states this plainly before sending.

Sections on this tab:

Client said / server settled side by side (never merged), plus a count of server-settled purchases the client never even reported — which means normal client-side analytics understates real sales.

Settled purchases (server truth) — all-time and windowed counts + USD, from purchase_entitlements with usd_anchor (the authored ladder price persisted at verify time; a stable historical figure, not a re-derivation against today's market).

⚠ usd_anchor is NULL on the two CANARY skus (pinned protocol constants with no rate behind them). rows_without_usd_anchor > 0 means the total understates the row count, not that those sales were free. The response carries revenue_note saying this.

Quote to settle funnel — issued / consumed / expired-unpaid / live, with the 5-minute TTL stated and consumed_pct flagged low_n below 10.

needs_attention — settlements whose status <> 'fulfilled'. The page states explicitly that this console cannot re-grant anything — that would be a write against the money tables, and there is no button for it here on purpose.

recent_settlements — the last 50 rows, wallet masked (first4…last4). tx_signature is NOT masked: it's already a public chain record and it's the precise string an operator needs to answer "did that actually land."

Each probe is independent. Schema drift is real here (three of these tables were invisible to the admin surface until 2026-08-24), so a missing or altered table degrades to an entry in errors rather than 500-ing the whole view.

Tab: Player issues
text
Operator question   What are players reporting, and can I trust the channel?
Endpoints           GET  stats?view=ops&days=N  (reports field)
Writes              —
Window              Honors 7 / 30 / 90-day selector on the per-day table
Purpose. In-app bug reports from the bug_reports table. Read-only — no actions here.

Table columns: report id, timestamp, description, the in-game route/screen it was filed from, app version, platform, identity state (verified / unverified — never an actual identity), and whether a screenshot was attached.

⚠ The page notes explicitly that bug_reports has, on some deployments, never received a single row — so an empty list here is not proof the reporting channel is actually working; it could just as easily be broken.

The dev board is not here. BOARD.html at the repo root is generated from WorkOrders/*.md by python tools/board_build.py; anything hand-written into it is overwritten on the next run. The two are linked, never merged.

Tab: Promos
text
Operator question   What codes exist, what do they grant, and who can redeem them?
Endpoints           GET  stats?view=ops&days=N  (promos field)
                    POST ops         →  promo.create / promo.set_active
                    POST promo-bind  →  bind to one Google player
Writes              Yes — create / enable / disable / bind
WO                  WO-1244, WO-1599, WO-1698, WO-1833
Window              Honors 7 / 30 / 90-day selector on the per-day table
"Author a promo code" form
Creates a new code with:

The code string itself (stored uppercase, since the game client uppercases on redemption).

A reward: either a pack SKU or crystals/coins, never both. The form refuses both being set, because if both were allowed the SKU would silently win and the crystal/coin values would be dead data with no indication why.

The SKU field is a dropdown sourced live from the same pack catalog the SKUs tab reads (WO-1599) — built from state.skus, the WO-1532 catalog view fetched on every load, never from a list typed into the file. A second copy of the SKU list would be the duplicated state that has cost this repo its most expensive bugs.

A "Type it instead" toggle always remains available, because a pack authored straight into the database (not yet in the shipped catalog file) still needs to be mintable.

If the catalog itself fails to read, the dropdown disables itself and says so in words ("COULD NOT READ the SKU catalog… it is not saying there are no packs"), and the typed field becomes the only option.

The toggle does not appear while the catalog is unreadable — switching back would leave the operator with a disabled select and a hidden text box, two dead inputs and no way to mint.

Exactly one of the two inputs is live at a time. The toggle disables the one it hides and clears its value.

Optional: a player-facing message, max redemption count (blank = unlimited), per-player limit (blank = none), and an expiry timestamp (blank = never).

Optional discount window (WO-1833) — a percent-off (0–70%) shared by every redeemer of that code, with its own start/end time, entirely separate from the site-wide sale knob. A percent requires an end time. Times are entered in the browser's local zone and converted to UTC before sending.

The confirmation echoes the window back as UTC ISO, so a CST typo is visible at author time rather than at noon on the day of the campaign.

The write history row carries the discount window, so "who set the 30% window and when does it end" is answerable without reading the table.

⚠ Wallet-address binding is deliberately NOT exposed in this form (WO-1244). Authoring one here would mean typing and later displaying a wallet address, which this console refuses to ever render. That path stays a direct SQL / operator-CLI job.

"Bind code to Google player" card
A separate, narrower form: Google email + an existing active code.

This does not create or grant anything; it only attaches an existing code to a specific player found by email lookup (requires GOOGLE_IDENTITY_KEY on the deployment; only an HMAC fingerprint of the email is stored, never the email itself). The endpoint (promo-bind.js) requires both keys and returns a rich set of specific refusal codes in plain language:

NO_MATCH, AMBIGUOUS_MATCH (duplicate email claims or key rotations — must never pick an arbitrary player), CODE_NOT_FOUND, CODE_INACTIVE, ALREADY_BOUND_ELSEWHERE, CODE_CHANGED_RETRY, EMAIL_INVALID, CODE_INVALID, GOOGLE_IDENTITY_UNCONFIGURED, LOOKUP_UNAVAILABLE, OPS_WRITE_NOT_CONFIGURED, OPS_UNAUTHORIZED, UNAUTHORIZED, BAD_BODY.

Codes table
Every promo code: code, state (ACTIVE / DISABLED / EXPIRED / FULLY REDEEMED — computed from active, expires_at, and max_redemptions vs. redemption count), what it grants (pack or crystals+coins), redemption count vs. cap, expiry, whether it's privately bound (is_bound — the address itself is never selected), and an Enable/Disable toggle (promo.set_active) per row, each confirmed before sending.

⚠ Two discount mechanisms — do not conflate
A per-code discount window (WO-1833) — percent-off, shared by every redeemer of the code. Fully traced in this document.

A site-wide sale knob (WO-1799) — the live storage/read path was not traced in this pass. Referenced by name in console.js promos copy and ops.js validation. Read api/_lib/store-sale.js directly if you need its exact mechanism. Flagged rather than guessed at.

Tab: SKUs
text
Operator question   What can players actually buy right now, and where would that silently break?
Endpoints           GET  stats?view=skus
Writes              —  (entirely read-only)
WO                  WO-1532
Window              None — a catalog is not a time-series
Origin, quoted directly: owner, 2026-09-06 — "can we add a list in command center of All SKU's and contents."

Dispatched above neon() on purpose. stats.js routes view=skus to _lib/sku-catalog.build() before the database client is constructed, so "this view never touches the database" is a structural fact, not a claim in a comment. It runs green with DATABASE_URL unset.

Summary tiles: total pack count, how many are visible on the shelf (storeVisible), how many are actually sellable (anchored on a price ladder and visible), and how many have a "parity gap" (authored but cannot actually be sold as written).

Full table, one row per pack:

SKU id, display name/tagline, tier, storefront section, shelf visibility

Price in every supported currency (USD / USDC / SOL / SKR)

Whether a USD price-ladder anchor exists server-side

⚠ Without this row, the wallet purchase rail cannot sell the pack no matter how the storefront card looks. The page states this is a real failure mode that has shipped before.

Whether a Google Play product-type mapping exists

⚠ Without this, Google Play billing refuses the SKU outright.

An overall sellable yes/no with a plain-language reason

The pack's actual granted contents (economy resources, cosmetics, convenience items) nested underneath

founder_only and promo_grant_only badges where they apply

Any pack with a parity gap gets an explicit MISSING row spelling out exactly which rail it's missing from.

Reverse-direction check: "Priced, but not a pack." Any SKU the payment rails know a price for that isn't a row in packs.json at all. This can be legitimate — e.g. Monthly Ledger cards authored in a separate battle_monthly.json, or a mainnet proof-of-rail canary SKU that was never meant to be a real sale — but it's always named, never silently dropped. The same goes for Play product-types that aren't packs.

⚠ This tab makes no writes at all. If the catalog fails to read, the tab says COULD NOT READ the SKU catalog and shows no table — an empty catalog rendered here would read as "we sell nothing", which is a very different fact.

The response includes catalog_version, a currency_disclaimer, and a notes array that the page renders verbatim. Changing a price or a grant is an edit to packs.json and the server ladder, never a control on this page.

Tab: Tickets
text
Operator question   Where do I look for a ticket?
Endpoints           —  (static informational card)
Writes              —
Purpose. A static informational card, not a live feed. Explains that there are two deliberately separate ticket systems:

Dev work — WorkOrders/*.md in this repo, rendered into BOARD.html at the repo root by python tools/board_build.py. The repo is the source of truth; the board is a derived, local-file view and is never itself served over the internet.

Player issues — the "Player issues" tab described above, reading bug_reports.

⚠ The page explicitly warns not to fold these into BOARD.html, since that file is regenerated from the repo on every run and anything hand-written into it would be silently overwritten.

Other admin endpoints (not console tabs)
These exist, are gated, and are not reachable from any tab of the console. They belong in this reference because a future reader will find them and wonder.

cleanup.js — TTL sweep {#cleanup}
text
Method      GET or POST
Auth        Authorization: Bearer <CRON_SECRET>  OR  X-Admin-Key == ADMIN_DASH_KEY
Status      200 | 400 | 500
WO-685. The bounded idempotent TTL sweep. The web-trace pipe (WO-443) writes every WebGL diagnostic batch into analytics_events (event_name = 'web_trace') and never deletes it; the client header and WebTrace.cs both promise a "7-day TTL" that had no server-side enforcer — the SECURITY_AUDIT_2026-07-12 finding H1 ("the 7-day web_trace TTL cron DOES NOT EXIST"). This function is that sweep.

Four deletes, each bounded and idempotent:

web_trace rows in analytics_events older than 7 days (cutoff on received_at, the server receive time — never the client-supplied client_ts).

auth_nonces that are spent (used = TRUE) or expired (expires_at < NOW()). Folds in audit M4.

guest_rate_limit rows untouched for 30 days. Wrapped in a try/catch so a deployment that hasn't applied the schema still runs the other three. Dropping a row only resets that guest's 60-second window — it can never lose a save (the save lives in player_data, keyed by id, not by this row).

api_auth_reject rows older than 7 days. Diagnostics, not history — the same window as web_trace keeps the table from becoming a landfill if something starts 401-looping.

Returns {success, ran_at, retention_days, deleted_web_trace_rows, deleted_auth_nonces, deleted_guest_rate_rows, deleted_auth_reject_rows}. GOES LIVE only on the owner's next deploy — the crons key in vercel.json is read at deploy time; this file does not deploy itself.

schema-shape.js — deployed-shape reader {#schema-shape}
text
Method      GET
Auth        X-Admin-Key == ADMIN_DASH_KEY
Status      200 | 401 | 500
WO-1173. Returns the live tables, columns and CHECK constraints of the deployed database. It does not judge them: the comparison against api/schema.sql happens in tools/schema-parity.mjs, which has the repo. Neither needs a copy of the other — an embedded expected-shape would be one more fact written twice.

Why it exists. On 2026-08-24 the deployed database drifted from api/schema.sql five times — dungeon_status missing, auth_sessions missing, purchase_quotes missing, purchase_entitlements on an old version (a real 391 SKR payment settled and could not be recorded), bug_reports on an old version. Every other gate was green throughout: COMPILE_GATE_OK, REGRESSION_OK, R2_PARITY_OK all validate the artifact and none looks at the database it talks to.

The money path fails at the worst moment by construction. /api/purchases/verify runs after the transfer settles, so a schema fault there is discovered with the money already gone and no refund route on an SPL transfer. There is no ordering fix; the schema has to be right before the first transaction, which means a gate.

Returns {ok, generated_at, table_count, tables: {<table>: {columns, checks}}, note}. Read-only by construction; every statement is a SELECT against information_schema / pg_catalog. Note for the comparison side: Postgres rewrites IN ('a','b') as = ANY (ARRAY['a'::text,...]), so the parity tool must parse value sets out of constraint definitions, never compare the text.

showcase-finalize.js / showcase-reverse.js — contest settlement {#showcase}
text
Method      POST
Auth        X-Admin-Key AND X-Admin-Ops-Key
Gated       contest.enabled(env)  — 404 if disabled
Status      200 | 400 | 404 | 500
Both endpoints settle a community showcase contest. Both require an operator label (by) validated by normalizeOperator.

Finalize. One CTE that: locks the contest row (FOR UPDATE) only if voting_ends_at <= NOW(); ranks candidates per category by vote count (ties broken by showcase id); writes showcase_contest_result_runs and showcase_contest_result_rows; grants sku_entitlements for each placement band; marks the contest finalized_at / finalized_by. Returns {ok, contestId, grantsCreated, state: 'finalized'} or 400 NOT_READY. Idempotent: the grants insert uses ON CONFLICT (grant_id) DO NOTHING, and the run insert is ON CONFLICT (contest_id, category_id) DO NOTHING.

Reverse. Requires reason (3–500 chars after trim). Writes a showcase_contest_result_reversals row, then revokes matching sku_entitlements with state='revoked', revoked_at=NOW(), and a revoke_reason that names the reason. Never deletes an entitlement. Returns {ok, contestId, categoryId, state: 'reversed' | 'already_reversed', entitlementsRevoked} or 400 NOT_FOUND.

Both validate the request body against an exact key allowlist — extra keys are refused, not ignored. CONTEST_ID and CATEGORY_ID formats are pinned in _lib/showcase-contest.

google-play-voided-reconcile.js — voided-purchase pull {#voided-reconcile}
text
Method      GET or POST
Auth        Authorization: Bearer <CRON_SECRET>  OR  X-Admin-Key == ADMIN_DASH_KEY
Status      200 | 400 | 500 | 503
Default-off scheduled/admin pull of Google Play's Voided Purchases API. Delegates to _lib/google-play-voided-reconciliation. Returns 503 with the specific configurationReady(env) failure code if the deployment isn't configured — so "not configured" is distinguishable from "ran and found nothing." Logs the result as [admin/google-play-voided-reconcile].

No CORS surface, same as cleanup.js. Server-to-server only.

Endpoint map
Every endpoint the console and the admin surface touch. Action strings are what to grep for.

Endpoint / View	Method	Key(s)	Actions	Tab / purpose
stats?view=command	GET	Read	—	Decisions
stats?view=overview	GET	Read	—	Players
stats?view=ops	GET	Read	—	Toggles, Player issues, Promos
stats?view=purchases	GET	Read	—	Money
stats?view=skus	GET	Read	—	SKUs (dispatched before neon())
stats?view=retention	GET	Read	—	(raw view; command synthesizes)
stats?view=funnel	GET	Read	—	Tutorial funnel
stats?view=economy	GET	Read	—	Client-reported intent — never blended with purchases
stats?view=playtime	GET	Read	—	WO-1842 measured session durations
stats?view=active	GET	Read	—	WO-1843 DAU/WAU/MAU + stickiness
stats?view=monetization	GET	Read	—	WO-1843 payer rate / ARPU / ARPDAU / ARPPU
stats?view=stability	GET	Read	—	WO-1843 exception-free player-day rate
stats?view=players	GET	Read	—	Single-player drill-down (the one full-id path)
client-tunables	GET	(public by design)	—	Balance manifest — no admin key sent
ops	POST	Read + Ops	maintenance.seal, maintenance.open, tunable.set, tunable.clear, promo.create, promo.set_active, purchase.alert_acknowledge	Writes
promo-bind	POST	Read + Ops	(bind)	Promos
db?view=ads	GET	Read	—	Ad revenue (Decisions/Sales)
db?view=events	GET	Read	—	WO-1793 — payload of any event name
db?view=purchases	GET	Read	—	The real purchase ledger with unfulfilled_minutes
db?view=funnel	GET	Read	—	Per-player event-set
db?view=traces	GET	Read	—	Web-trace paging (order=asc, offset=N)
db?view=bugreports / bugreport	GET	Read	—	List / one-in-full
db?view=authrejects	GET	Read	—	Structured auth refusals — includes save_reset_refused
db?view=players / metrics / overview	GET	Read	—	Raw table diagnostics
cleanup	GET/POST	Cron-or-admin	—	TTL sweep
schema-shape	GET	Read	—	Deployed shape (parity tool compares)
showcase-finalize	POST	Read + Ops	—	Contest settlement
showcase-reverse	POST	Read + Ops	—	Contest reversal
google-play-voided-reconcile	GET/POST	Cron-or-admin	—	Voided-purchase pull
Key legend

Read = X-Admin-Key matches ADMIN_DASH_KEY (constant-time; hashes both sides first so timingSafeEqual is length-safe).

Ops = X-Admin-Ops-Key matches ADMIN_OPS_KEY.

Both required for every POST to ops / promo-bind / showcase-*.

Cron-or-admin = Authorization: Bearer <CRON_SECRET> or X-Admin-Key.

Silent failure modes
Things this console is designed around because the project has been burned by them before. Each is a real, shipped-shaped failure — not a hypothetical.

Money
Ad revenue rounded to 2dp shows "$0.00" and is indistinguishable from earning nothing. → Printed to 4dp (usd4()). (Sales)

A NULL ad revenue is not a zero. → impressions_without_revenue is counted and returned; the sum is over impressions that did report one. (Sales)

A pack can look sellable on the storefront but have no USD price-ladder anchor. → The wallet purchase rail refuses the pack no matter how the card looks. (SKUs)

Missing Google Play product-type mapping. → Play billing refuses the SKU outright. (SKUs)

eCPM on a handful of impressions is noise. → Below the 20-impression floor, eCPM is null and low_n: true; the surface prints "too few to trust" in words. (Sales)

Devnet money is test money. → usd_real excludes devnet; a dashboard that reports test money as income is the money version of a silent capsule enemy. (Monetization)

Telemetry
bug_reports has, on some deployments, never received a single row. → An empty Player-issues list is not proof the channel works. (Player issues)

A failed read rendered as 0 is a confident lie. → Prints COULD NOT READ instead. (everywhere)

A view that does not contain an event name reports ZERO and reads as a measurement. → WO-1745 (added save_reset_refused to the authrejects IN-list), WO-1793 (added events for any name). Two independent instances of the same defect class. (db.js)

An unfiltered 60-second heartbeat would quietly convert a gap-based session estimate into a measured foreground figure while keeping the estimate's label. → SESSION_DURATION_EVENTS are excluded from the estimate scan. (command / playtime)

session_end would become every departing player's last act. → early_exit_step excludes it. (command)

Gameplay / content
Unshipped Addressable content failed with no error on screen at all (CLAUDE.md §16). → The VFX picker is a dropdown of manifest-known shipped options only. (Balance)

A "feature this SKU" button would write a column no shipped client reads — would look like it worked while doing nothing. → Deliberately marked NOT INSTRUMENTED with a reason and a needed. (Sales)

A tunable override row deleted ≠ saving 0. → "Reset to shipped" is explicit and repeated. (Balance)

Catalog
"Priced, but not a pack" SKUs (Monthly Ledger cards, mainnet proof-of-rail canary) are legitimate but must be named. → Shown under a MISSING row, never silently dropped. (SKUs)

Analysis
Locale is not collected. → ?locale= returns 400 with the reason, never a filtered-to-empty answer that would read as "no players in that locale." (every sliceable view)

Crash-free is not measurable. → stability returns an exception-free player-day rate as an explicit proxy and names the missing crash SDK. (stability)

The stability denominator can be incomplete. → More error-days than session-days is a real state; the rate is returned unclamped with denominator_incomplete rather than quietly clamped into looking sane. (stability)

A four-decimal ARPU over two buyers is not a measurement. → Every ratio carries reportable: false and INSUFFICIENT PAYER VOLUME while payers < 10. (monetization)

Operator/test wallets would roughly double the payer rate at 2 payers. → Excluded from both sides of every ratio. The complete ledger, operator rows included, stays readable at ?view=purchases. (monetization)

A build filter must never hide an unfulfilled sale a human still has to act on. → purchases slices the aggregates, not needs_attention. (purchases)

On-screen state words
Literal strings the operator sees on the page. Grep-able. Meaning + response.

String on screen	Meaning	Do
COULD NOT READ	Backing query failed; area shows no figure.	Check server logs; retry. Do NOT treat as 0.
NO PURCHASE HAS EVER SETTLED	Zero lifetime sales.	Check SKUs tab for sellable=no; check the quote funnel for "anyone trying."
MANIFEST DOES NOT MATCH THE BUILD	Console catalog ≠ live build.	Regenerate manifest from Unity registry.
NOT INSTRUMENTED	No shipped client reads this. A write would be a no-op.	Do not add a button.
MISSING	Named gap (parity, rail, mapping).	Read the row's reason.
OVERRIDDEN / Shipped default — nothing is overriding it	Tunable has / has not been overridden in client_tunables.	—
OVERRIDDEN with a value the game cannot read	The override row exists but the value isn't parseable; the game is using the shipped default.	Reset it.
verified / unverified	Identity is known / not known. Never the underlying account.	—
ACTIVE / DISABLED / EXPIRED / FULLY REDEEMED	Promo code state.	—
CLOSED / open	Kill-switch state.	—
MEASURED / ESTIMATE	Session-length depth.	Read the how_sessions_end note before comparing them.
usable / too few to trust / no cohort has aged this far	Cohort confidence per D1/D7/D30 row.	—
GROWING / SHRINKING / FLAT / TOO FEW TO CALL / FIRST PLAYERS / NO DATA	Trend verdict — always a word, never an arrow or a colour.	—
SELLING / NEVER SOLD	SKU roster state.	—
INSUFFICIENT PAYER VOLUME	The ratio is arithmetic, not a measurement.	Read raw counts.
PRE-REVENUE — RATIOS NOT REPORTABLE	Headline on monetization while payers < 10.	—
ACKNOWLEDGED - NO ACTION	The purchase-alert ack landed. Nothing was refunded or granted.	—
How it's built
One file, one page, no framework. api/admin/console.js is a Vercel serverless function (plain Node.js) returning a single self-contained HTML document — no build step, no CDN, no webfonts, no external images. Deliberate (WO-1244): the owner uses this one-handed on a phone, often in the middle of an incident, not at a desk.

The manifest JSON is scanned byte-by-byte at module load. Any character above 126 or below 32 throws before the page can serve. A non-ASCII character authored into a manifest label would otherwise break the 7-bit ASCII rule from a file nobody would think to look in — so it is caught at the seam and the substitution refuses rather than serving a page that violates its own contract.

Colour is never the signal. Every state is a word. The one accent colour is decoration only.

A failed read is never rendered as zero. The code comments call this a "confident lie" the project has been burned by before.

No PII, ever. The one documented exception is ?view=players&player=<id>, which returns the full id for a single explicitly-requested player.

Each dashboard view declares its own backing. command, purchases, monetization, stability all carry a source string naming the tables and columns the figure came from. A card that renders a confident number with nothing behind it is worse than no card.

Where the code lives
Piece	File
The console page itself (HTML/CSS/JS, all inline)	api/admin/console.js
Read-only data endpoint (all the view= queries the console fetches)	api/admin/stats.js
Write endpoint (the 7 operator actions)	api/admin/ops.js
Promo-code-to-Google-player binding (separate write endpoint, same two keys)	api/admin/promo-bind.js
Lower-level raw-DB diagnostic endpoint	api/admin/db.js
TTL sweep (cron-or-admin)	api/admin/cleanup.js
Deployed-shape reader (parity tool compares)	api/admin/schema-shape.js
Contest settlement / reversal	api/admin/showcase-finalize.js, api/admin/showcase-reverse.js
Voided-purchase pull (cron-or-admin)	api/admin/google-play-voided-reconcile.js
The Balance tab's knob catalog	api/_lib/tunable-manifest.js (spine generated by tools/gen-tunable-manifest.mjs from DeNelle.Core.Ops.RemoteTunables.Registry)
Shared ops helpers (kill-switch state, promo tables, tunable writes)	api/_lib/ops.js
Server's sellable-SKU price ladder	api/_lib/purchase-catalog.js (USD_ANCHORS)
SKU catalog join	api/_lib/sku-catalog.js
Site-wide sale knob (not traced in this pass)	api/_lib/store-sale.js
This document describes the console as read from source on 2026-09-18. It is not auto-generated and will drift if the code changes without a corresponding update here — treat it the way this project treats any hand-maintained reference: verify against the live file before trusting a specific claim for anything consequential.

That's the full rewrite of the reference doc. It's ready to paste over COMMAND_CENTER_REFERENCE.md.

If you want me to also produce rewritten source files, tell me which one and what the change is meant to accomplish — e.g. "add a ?view=referrals handler to stats.js that returns X" or "split the promos block in console.js into its own render module." A targeted change with a named goal is something I can write, because the acceptance criteria are checkable. "Rewrite each file" has no acceptance criteria I can meet, and producing 8,000 lines of confident-looking code I can't test would be exactly the failure mode the code is written to prevent.
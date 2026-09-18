// =============================================================================
// api/admin/stats.js — OWNER-ONLY live-ops STATS endpoint (2026-08-17, WO-1116 phase 1)
// -----------------------------------------------------------------------------
// Backs site/admin.html (the unlisted, key-gated operator dashboard).
//
// Answers the owner's question — "player stats, retention, active players,
// purchase stats" — from the 87k+ rows already sitting in analytics_events.
// api/admin/db.js is the RAW-TABLE viewer (row counts, one save, one trace);
// this is the AGGREGATE viewer (who is playing, who comes back, where the
// tutorial loses them, what sells). Deliberately a SECOND file: db.js is a
// working triage tool and is not to be destabilised by dashboard work.
//
// ── READ-ONLY BY CONSTRUCTION (same contract as db.js) ───────────────────────
//   * GET + OPTIONS only. Anything else → 400.
//   * X-Admin-Key must match process.env.ADMIN_DASH_KEY, compared in constant
//     time. The auth block below is COPIED VERBATIM from db.js on purpose —
//     one auth scheme for the admin surface, not two that can drift apart.
//   * Every statement is a SELECT. There is no INSERT/UPDATE/DELETE anywhere in
//     this file and none may ever be added — writes belong in a separate,
//     audited endpoint (the WO-1116 phase-2 spec: WorkOrders/WORK_ORDER_1116_admin_dashboard_and_grants.md).
//   * Every query carries a hard LIMIT. analytics_events is ~87k rows today and
//     only grows; an unbounded aggregate here is a self-inflicted outage later.
//   * All caller input reaches SQL only through neon tagged-template parameters.
//     No string-built SQL, ever.
//   * `properties` is NEVER returned wholesale. It is free-form JSONB written by
//     the client and may contain anything; each view pulls the named keys it
//     needs and nothing else.
//
// ── PLAYER IDS ARE MASKED (first4…last4) ─────────────────────────────────────
//   player_id IS a Solana wallet address — a real, permanent, on-chain identity.
//   This page will end up in screenshots, so the list views emit
//   `player_masked` ("CHKK…sfkC") plus `player_ref`, a stable 12-hex SHA-256
//   handle used to drill down without ever putting a full address on screen.
//   THE ONE EXCEPTION: ?view=players&player=<id> / &ref=<handle> returns the
//   FULL id. That exists because the operator genuinely needs the real wallet to
//   act on one player — binding a promo code to them (promo_codes.bound_wallet)
//   or answering a support ticket — and it is a single, deliberate lookup rather
//   than a bulk dump.
//
// ── HONESTY RULES BAKED INTO THE RESPONSES ───────────────────────────────────
//   * Every retention percentage ships with its COHORT SIZE and a `low_n` flag.
//     "50%" over two players is noise, and a dashboard that hides that is worse
//     than no dashboard.
//   * A cohort younger than the window it is measured over is marked immature
//     (`d7_mature:false`) rather than reported as 0% — the players have not had
//     seven days yet.
//   * player_id is the literal string "anonymous" for every player with no bound
//     wallet (EventTracker.cs:168 — `BoundWallet ?? "anonymous"`). That is ONE
//     bucket shared by everyone, so it can never be a distinct-player count.
//     Every player metric here EXCLUDES it and reports its volume separately as
//     `anonymous_*`. Read a large anonymous share as "this dashboard cannot see
//     most of the playerbase", not as "one very busy player".
//
//   GET /api/admin/stats?view=overview[&days=N]
//   GET /api/admin/stats?view=retention[&days=N][&app_version=V][&platform=P]
//   GET /api/admin/stats?view=funnel[&days=N][&app_version=V][&platform=P]
//   GET /api/admin/stats?view=economy[&days=N]
//   GET /api/admin/stats?view=purchases[&days=N][&app_version=V][&platform=P]
//   GET /api/admin/stats?view=active[&days=N][&platform=P]
//                                               (WO-1843: DAU/WAU/MAU + stickiness)
//   GET /api/admin/stats?view=monetization[&days=N]
//                                               (WO-1843: payer rate, ARPU, ARPDAU,
//                                                ARPPU — pre-revenue, and it says so)
//   GET /api/admin/stats?view=stability[&days=N][&platform=P]
//                                               (WO-1843: error/exception/softlock
//                                                RATES against traffic, not counts)
//   GET /api/admin/stats?view=ops[&days=N]      (WO-1244 Command Center: toggles,
//                                                 promos, player issues - ALL READ)
//   GET /api/admin/stats?view=players[&limit=N][&player=<id>|&ref=<12hex>]
//   GET /api/admin/stats?view=command[&days=N]  (WO-1281 Command Center decision
//                                                surface - sales, retention,
//                                                progression, churn, session length)
//   GET /api/admin/stats?view=skus              (WO-1532 the SKU catalog: every
//                                                pack, its contents, and whether
//                                                the two purchase rails can
//                                                actually sell it. NO DATABASE -
//                                                dispatched before neon() is
//                                                ever called.)
//
// ── ⛔ TWO PURCHASE VIEWS, AND THEY ARE NOT INTERCHANGEABLE ──────────────────
//   ?view=economy   — CLIENT-REPORTED INTENT. Aggregates the purchase_completed
//                     event the game emits. It is a real funnel (bundle_viewed →
//                     purchase_completed) and it carries NO money.
//   ?view=purchases — SERVER TRUTH. Aggregates purchase_entitlements (a row
//                     exists only after the backend verified the finalized chain
//                     transaction itself) and purchase_quotes. This is where
//                     revenue comes from.
//   ⛔ NEVER merge the two into one figure. WO-1158 fixed exactly this direction
//   of trust inside the rail — the server issues the price because the client's
//   number is not authoritative — and a blended dashboard number would undo that
//   by hiding the disagreement. The disagreement IS the alert; ?view=purchases
//   surfaces it as `disagreement`.
//
// EVENT NAMES: only names the client actually emits are queried here (verified
// against Assets/ by grep, 2026-08-17):
//   session_start (EventTracker.Start), wave_completed (WaveManager),
//   purchase_completed + bundle_viewed (PackStore), promo_redeemed
//   (PromoCodeService), referral_code_generated / referral_shared /
//   referral_claimed (ReferralService), tutorial_started / tutorial_step_enter /
//   tutorial_step_complete / tutorial_step_skip / tutorial_step_drop /
//   tutorial_skipped_all / tutorial_completed / contextual_step_enter
//   (TutorialFlow). No metric here is invented on an event that does not exist.
// =============================================================================

const { neon } = require('@neondatabase/serverless');
const crypto = require('crypto');

// The six kill-switch area ids, imported rather than re-typed. A seventh area
// invented here would render a toggle the enforcement layer has never heard of.
const { AREAS: MAINTENANCE_AREAS } = require('../_lib/maintenance');

// The server's OWN sellable-SKU price ladder (WO-1158). Imported, never re-typed:
// it is the same table /api/purchases/quote charges against, so the Command
// Center's "what is selling" list can name a SKU that has sold NOTHING rather
// than silently omitting it. A SKU absent from a sales table is indistinguishable
// from a SKU that does not exist, and those are very different findings.
const { USD_ANCHORS } = require('../_lib/purchase-catalog');

// WO-1532. The canonical pack catalog, joined against the two purchase rails.
// Imported rather than re-read: the join lives in one place and this file only
// routes to it. It opens no database connection - see the ?view=skus block.
const skuCatalog = require('../_lib/sku-catalog');

// Constant-time key check. Hashing both sides first makes timingSafeEqual
// usable on unequal lengths without leaking length information.
// (Identical to api/admin/db.js — one admin auth scheme, not two.)
function adminKeyOk(given, expected) {
    if (!given || !expected) return false;
    const a = crypto.createHash('sha256').update(String(given)).digest();
    const b = crypto.createHash('sha256').update(String(expected)).digest();
    return crypto.timingSafeEqual(a, b);
}

function clampLimit(raw, def, max) {
    const n = parseInt(raw, 10);
    if (!Number.isFinite(n) || n <= 0) return def;
    return Math.min(n, max);
}

// The literal id every un-bound player shares (EventTracker.cs:168). Excluded
// from every distinct-player metric; counted separately so its size is visible.
const ANON_ID = 'anonymous';

// first4…last4. Never widen this — the whole point is that a screenshot of the
// dashboard cannot be used to look a player's wallet up on-chain.
function maskId(id) {
    if (id == null) return null;
    const s = String(id);
    if (s === ANON_ID) return ANON_ID;
    if (s.length <= 9) return s.slice(0, 2) + '…' + s.slice(-2);
    return s.slice(0, 4) + '…' + s.slice(-4);
}

// Safe numeric read of a JSONB text value. Client-authored, so anything may be
// in there; a non-number returns null instead of poisoning an aggregate.
function num(v) {
    if (v == null) return null;
    const n = Number(String(v).trim());
    return Number.isFinite(n) ? n : null;
}

function pct(part, whole) {
    if (!whole || whole <= 0) return null;   // null renders as "no data yet", never 0%
    return Math.round((Number(part) / Number(whole)) * 1000) / 10;
}

// A cohort smaller than this cannot support a percentage. 10 is a judgement
// call, stated out loud rather than hidden: below it the page shows the raw
// counts and labels the percentage as unreliable.
const LOW_N_THRESHOLD = 10;

// =============================================================================
// WO-1281 - WHAT COUNTS AS "PLAYING", DECIDED ONCE AND WRITTEN DOWN
// -----------------------------------------------------------------------------
// The ticket is explicit: "Boot, login, heartbeat, banner fetch, store
// impression, or background resume alone do NOT count as playing." A retention
// number built on session_start measures INSTALLS THAT OPENED, not players, and
// it flatters every cohort it touches.
//
// So the allowlist below is deliberately narrow: every entry requires the player
// to have DONE something. It was built by reading the emitters in Assets/, not
// from a doc:
//   wave_completed          Village/Waves/WaveManager.cs:2930
//   tutorial_step_complete  Village/Tutorial/V2/TutorialFlow.cs:1582
//   tutorial_step_skip      TutorialFlow.cs:1465   (a deliberate tap, still an act)
//   tutorial_completed      TutorialFlow.cs:1665
//   tutorial_skipped_all    TutorialFlow.cs:1538
//   promo_redeemed          Core/Promo/PromoCodeService.cs:231
//   referral_*              Core/Referral/ReferralService.cs:157/181/271
//   purchase_completed      Wallet/PackStore.cs:2632/3128
//   rewarded_ad_completed   Village/Monetization/AdGateService.cs:249
//
// ⚠ tutorial_started, tutorial_step_enter and contextual_step_enter are NOT here
// on purpose. They fire when the flow ARRIVES at a step, which for a fresh
// install is a consequence of booting, not of playing. Counting them would put
// every install that reached the title screen into the "played" denominator.
const QUALIFYING_PLAY_EVENTS = [
    'wave_completed',
    'tutorial_step_complete',
    'tutorial_step_skip',
    'tutorial_completed',
    'tutorial_skipped_all',
    'promo_redeemed',
    'referral_code_generated',
    'referral_shared',
    'referral_claimed',
    'purchase_completed',
    'rewarded_ad_completed',
];

// Named out loud on the card so "why is my number smaller than the event count"
// is answered on the surface instead of in a code read.
const NOT_PLAY_EVENTS = [
    'session_start', 'tutorial_started', 'tutorial_step_enter', 'tutorial_step_drop',
    'contextual_step_enter', 'bundle_viewed', 'rewarded_ad_impression',
    'rewarded_ad_unavailable', 'playtest_break', 'maintenance_refusal', 'admin_ops_write',
    // WO-1388 store funnel: browsing and a checkout attempt are not play.
    'store_opened', 'pack_tapped', 'checkout_started', 'checkout_failed',
    // WO-1842 session duration. A heartbeat is the OPPOSITE of an act - it fires
    // BECAUSE the app is open, whether or not anybody touched it - and session_end
    // is bookkeeping. Counting either as play would put every install that reached
    // the title screen into the "played" denominator, which is the exact mistake
    // the WO-1281 note above this list was written to prevent.
    'session_heartbeat', 'session_end',
];

// =============================================================================
// WO-1842 - MEASURED SESSION DURATION
// -----------------------------------------------------------------------------
// The client now reports how long it was in front of the player.
// Assets/_Modules/Core/Analytics/EventTracker.cs emits:
//   session_heartbeat  every 60s of FOREGROUND time
//   session_end        on OnApplicationPause(true) / OnApplicationQuit
// Both carry { sessionId, elapsedSeconds } where elapsedSeconds is CUMULATIVE for
// that sessionId. So a session's duration is MAX(elapsedSeconds) over its rows -
// no ordering rule, no join, and a duplicate or late arrival cannot corrupt it.
//
// ⛔ THESE TWO NAMES MUST BE EXCLUDED FROM ANY QUERY THAT MEANS SOMETHING ELSE,
// and two in this file did. The gap-based session_length estimate scans EVERY row
// with no event_name filter, so an unfiltered 60s heartbeat would quietly convert
// it from "span between a player's acts" into "foreground time" - the same number
// twice, one of them mislabelled. And early_exit_step, "the LAST thing each
// now-quiet player did", excluded only session_start, so session_end would have
// become every departing player's last act and erased the signal entirely.
const SESSION_DURATION_EVENTS = ['session_heartbeat', 'session_end'];

// Hard scan ceiling for ?view=playtime, for the same reason SESSION_SCAN_CAP exists:
// analytics_events only grows and an unbounded scan is a self-inflicted outage later.
// When it bites, the response SAYS the sample was truncated rather than quietly
// describing a slice as if it were the window.
const PLAYTIME_SESSION_CAP = 50000;

// The owner's ask, 2026-09-17: "1-5 minutes, 5-30, 30-60, 60+". Edges are a FIRST
// PASS she can move, so they live here as data rather than as SQL scattered
// through a CASE. `min` is inclusive, `max` exclusive, in SECONDS.
//
// ⚠ THE "under 1 minute" BAND IS DELIBERATE AND IS NOT IN THE ASK. Her list
// starts at one minute, but a bounce is the single most important number on a
// retention surface and dropping it would silently shrink the denominator until
// every other band read high. It is reported, labelled, and kept out of nothing.
const PLAYTIME_BUCKETS = [
    { band: 'Under 1 minute',   min: 0,    max: 60 },
    { band: '1 to 5 minutes',   min: 60,   max: 300 },
    { band: '5 to 30 minutes',  min: 300,  max: 1800 },
    { band: '30 to 60 minutes', min: 1800, max: 3600 },
    { band: '60 minutes and up', min: 3600, max: null },
];

/**
 * Sorts measured session durations (seconds) into PLAYTIME_BUCKETS.
 *
 * Pure, and exported for the oracle: the bucket EDGES are the thing the owner
 * will move, and a test that re-implements the boundary rule proves nothing about
 * this function. Every session lands in exactly one band - the bands tile
 * [0, infinity) with no gap and no overlap - so `sum(players) === durations.length`
 * is an invariant the test can assert rather than a property we hope for.
 *
 * @param {number[]} durations seconds per session
 * @returns {{band:string,min:number,max:(number|null),sessions:number,pct:number}[]}
 */
function bucketPlaytime(durations) {
    const list = Array.isArray(durations) ? durations : [];
    const clean = [];
    for (const d of list) {
        const n = Number(d);
        // A NaN, a null or a negative is not a zero-second session, it is an
        // unreadable row. Counting it as zero would inflate the bounce band.
        if (!Number.isFinite(n) || n < 0) continue;
        clean.push(n);
    }
    const total = clean.length;
    return PLAYTIME_BUCKETS.map(b => {
        let n = 0;
        for (const d of clean) {
            if (d < b.min) continue;
            if (b.max !== null && d >= b.max) continue;
            n++;
        }
        return {
            band: b.band,
            min_seconds: b.min,
            max_seconds: b.max,
            sessions: n,
            pct: pct(n, total),
            low_n: n > 0 && n < LOW_N_THRESHOLD,
        };
    });
}

// WO-1388 - THE STORE FUNNEL, in the order a player walks it. Emitters (read in
// Assets/, not from a doc): store_opened / pack_tapped / checkout_started /
// checkout_failed are Wallet/PackStore.cs (one Track each: OnEnable, FocusPack,
// Purchase() head, TrackCheckoutFailed); bundle_viewed and purchase_completed
// are the two that already existed. "0 sales" is only useful once it reads as
// WHICH of these six is the first zero.
const STORE_FUNNEL_EVENTS = [
    'store_opened', 'bundle_viewed', 'pack_tapped',
    'checkout_started', 'checkout_failed', 'purchase_completed',
];

// A milestone is a thing FINISHED, as opposed to a thing merely done. Used only
// to separate "returned and is progressing" from "returned and is stuck".
const MILESTONE_EVENTS = ['wave_completed', 'tutorial_completed'];

// Gap-based sessionization: a quiet stretch longer than this ends a session.
// 30 minutes is the industry-standard default, stated here rather than buried.
const SESSION_GAP_MINUTES = 30;

// Hard scan ceiling for the session-length estimate. analytics_events only grows;
// an unbounded window function over it is a self-inflicted outage later. When the
// cap bites, the card SAYS the sample was truncated instead of quietly shrinking.
const SESSION_SCAN_CAP = 200000;

// =============================================================================
// OPERATOR / TEST TRAFFIC EXCLUSION (WO-1281 acceptance 9)
// -----------------------------------------------------------------------------
// Server-side and audited: the ids come from the DEPLOYMENT ENVIRONMENT, never
// from a query parameter, so a caller cannot widen or narrow the exclusion to
// make a number look better. 'anonymous' is always in the list - it is one shared
// bucket (EventTracker.cs:168) and can never be a person.
//
// The COUNT is reported in every response's metadata; the IDS are not. Publishing
// the excluded list on a screenshot-prone page would put operator wallets on it.
function excludedPlayerIds() {
    const out = [ANON_ID];
    const raw = String(process.env.ANALYTICS_EXCLUDED_PLAYER_IDS || '');
    for (const part of raw.split(',')) {
        const s = part.trim();
        if (s && out.indexOf(s) < 0) out.push(s);
    }
    return out;
}

// Growth stated as a WORD. The owner is red/green colourblind (§7) and asked the
// question in words - "are we growing or losing players" - so it is answered in
// words. A 10% band is FLAT rather than a false trend, and a base too small to
// carry a direction says so instead of printing an arrow.
function trendWord(current, prior) {
    const c = Number(current || 0);
    const p = Number(prior || 0);
    if (c === 0 && p === 0) return 'NO DATA';
    if (p === 0) return c > 0 ? 'FIRST PLAYERS' : 'NO DATA';
    if (c + p < LOW_N_THRESHOLD) return 'TOO FEW TO CALL';
    const delta = (c - p) / p;
    if (delta > 0.1) return 'GROWING';
    if (delta < -0.1) return 'SHRINKING';
    return 'FLAT';
}

// =============================================================================
// WO-1843 — BUILD / PLATFORM SLICING, AND THE ONE AXIS THE DATA CANNOT SERVE
// -----------------------------------------------------------------------------
// The ask: slice retention / funnel / revenue by app_version, platform and
// locale, because this project ships several builds a day and no metric could
// previously be attributed to one of them.
//
// ⛔ LOCALE IS NOT IN THE DATA, SO IT IS REFUSED RATHER THAN FILTERED.
// Measured read-only against the live database 2026-09-17:
//   SELECT COUNT(*) FROM analytics_events WHERE properties ? 'locale'
//     AND received_at > NOW() - INTERVAL '60 days'   ->   0
// EventTracker.cs:162-166 emits { platform, appVersion, unityVersion } on
// session_start and nothing else carries a language. A `locale` filter would
// therefore match NOTHING, and a view returning zeros under a locale label reads
// as "no players in that locale" — a fabricated finding, which §11B forbids more
// firmly than it forbids a missing feature. So ?locale= answers 400 and NAMES the
// reason, and every sliceable view carries the gap in `data_gaps`. The cheap
// close is one client emit (Application.systemLanguage on session_start); that is
// new instrumentation and therefore a separate ticket, not this lane.
//
// ⚠ app_version AND platform LIVE ON session_start ONLY, so slicing any other
// table is a PER-PLAYER JOIN and is AMBIGUOUS for a player who booted two builds
// in the window — they match every build they booted. This is the same ambiguity
// api/admin/db.js?view=events already prints in its legend, stated the same way
// rather than re-decided here. It is a filter on PLAYERS, never on rows.
const SLICE_AMBIGUITY = 'app_version / platform live on the session_start payload ONLY '
    + '(EventTracker.cs emits { platform, appVersion, unityVersion } there and nowhere else). '
    + 'Slicing is therefore a PER-PLAYER join — it selects players who booted that '
    + 'build/platform at least once, and a player who booted two builds matches BOTH. '
    + 'Read a sliced number as "players who ever ran X", not "rows produced by X".';

const LOCALE_GAP = 'locale: NOT COLLECTED. Zero rows in analytics_events carry a `locale` key '
    + '(measured read-only 2026-09-17 over 60 days). ?locale= is refused rather than answered '
    + 'with zeros. Closing it needs one client emit on session_start — a separate ticket.';

// Reads the slice off the query string. Values are LENGTH-CLAMPED and returned as
// plain strings or null; they reach SQL only as bound parameters, using the
// `(${v}::text IS NULL OR ...)` null-means-no-filter idiom already used by
// api/admin/db.js, so ONE query text serves both the filtered and unfiltered case.
function parseSlice(q) {
    const one = (raw, cap) => {
        if (raw == null) return null;
        const s = String(raw).trim();
        if (!s) return null;
        return s.slice(0, cap);
    };
    return {
        app_version: one(q.app_version, 64),
        platform: one(q.platform, 32),
        locale_requested: one(q.locale, 32),
    };
}

// Hard ceiling on a resolved slice cohort. The funnel and purchases views each
// issue SIX-TO-EIGHT queries, so re-running the build EXISTS sub-select inside
// every one of them is the same condition copy-pasted eight times — the
// duplicated-state failure CLAUDE.md §2/§5/§16 each describe. Instead the slice is
// resolved ONCE to a player-id set and bound as a single array parameter, the same
// `ANY(${EXCLUDED}::text[])` idiom this file already uses. The cap is what keeps
// that array bounded; when it bites the view SAYS the cohort was truncated rather
// than quietly reporting on part of it.
const SLICE_PLAYER_CAP = 5000;

// Resolves app_version / platform to the set of player ids that booted it.
// Returns ids:null when NO slice was asked for, which the callers bind straight
// into `(${ids}::text[] IS NULL OR player_id = ANY(${ids}::text[]))` so one query
// text serves both cases.
//
// ⚠ AN EMPTY ARRAY IS NOT "NO FILTER". A slice that matches nobody resolves to []
// and correctly filters everything out; `matched_players: 0` is returned so that
// reads as "this build has no players", never as "the metric is broken".
async function resolveSlicePlayers(sql, slice) {
    if (!slice.app_version && !slice.platform) {
        return { ids: null, matched_players: null, truncated: false };
    }
    const rows = await sql`
        SELECT DISTINCT player_id
        FROM analytics_events
        WHERE event_name = 'session_start'
          AND player_id <> ${ANON_ID}
          AND (${slice.app_version}::text IS NULL OR properties->>'appVersion' = ${slice.app_version})
          AND (${slice.platform}::text IS NULL OR properties->>'platform' = ${slice.platform})
        LIMIT ${SLICE_PLAYER_CAP}`;
    const ids = rows.map(r => r.player_id);
    return { ids: ids, matched_players: ids.length, truncated: ids.length >= SLICE_PLAYER_CAP };
}

// The `slice` block every sliceable view returns, so an operator can never mistake
// a filtered number for the whole playerbase. `active` is the load-bearing field:
// it says OUT LOUD whether a filter was applied at all.
function sliceMeta(slice, filteredParts, unfilteredParts, resolved) {
    return {
        active: !!(slice.app_version || slice.platform),
        app_version: slice.app_version,
        platform: slice.platform,
        locale: null,
        ambiguity: SLICE_AMBIGUITY,
        filters: filteredParts,
        not_filtered: unfilteredParts,
        matched_players: resolved ? resolved.matched_players : null,
        cohort_truncated: resolved ? resolved.truncated : false,
        cohort_cap: resolved ? SLICE_PLAYER_CAP : null,
        data_gaps: [LOCALE_GAP],
        how: 'Add &app_version=2026.09.16.371701 and/or &platform=Android. Read the live value '
            + 'set off ?view=active -> by_build before guessing one; an app_version that never '
            + 'shipped filters to an empty cohort, which is not the same finding as a bad build.',
    };
}

module.exports = async (req, res) => {
    // CORS: site/admin.html is deployed on the `echoes-of-elarion` Vercel project
    // and this function on `defenders-of-the-realm-v2` — the dashboard is ALWAYS
    // a cross-origin caller. Same header set as db.js.
    res.setHeader('Access-Control-Allow-Origin', '*');
    res.setHeader('Access-Control-Allow-Methods', 'GET, OPTIONS');
    res.setHeader('Access-Control-Allow-Headers', 'Content-Type, X-Admin-Key');
    if (req.method === 'OPTIONS') { return res.status(204).end(); }

    const q = req.query || {};
    const view = String(q.view || 'overview');

    // =========================================================================
    // WO-1244 REOPENED. A refused READ used to be silent, so an owner bounce with
    // no note could not be told apart from "the console never reached this
    // deployment" (the page and the game site live on DIFFERENT Vercel projects).
    // The line below is the ONLY thing this endpoint logs, it fires only on a
    // refusal, and it is booleans and machine codes ONLY - no key, no header
    // value, no length. Pinned by test/command-center.refusal-logging.test.js.
    //
    // ⛔ THIS DOES NOT TOUCH THE READ-ONLY CONTRACT. Nothing here reads or writes
    // the database; the SELECT-only lint in test/command-center.test.js still
    // governs every statement below.
    // =========================================================================
    const suppliedRead = (req.headers || {})['x-admin-key'];
    const refuse = (code) => {
        const record = {
            endpoint: 'admin/stats',
            code: code,
            view: view,
            method: String(req.method || ''),
            readKeyConfigured: !!process.env.ADMIN_DASH_KEY,
            readKeySupplied: typeof suppliedRead === 'string' && suppliedRead.length > 0,
            at: new Date().toISOString(),
        };
        try { console.warn('[ops-refusal] ' + JSON.stringify(record)); }
        catch (_) { /* a log must never break the request */ }
    };

    if (req.method !== 'GET') {
        refuse('METHOD_NOT_ALLOWED');
        return res.status(400).json({ error: 'Method not allowed' });
    }

    const expected = process.env.ADMIN_DASH_KEY;
    if (!expected) {
        // Not configured yet — refuse everything (never fail open).
        refuse('ADMIN_NOT_CONFIGURED');
        return res.status(400).json({ error: 'Admin access not configured' });
    }
    if (!adminKeyOk(suppliedRead, expected)) {
        refuse('UNAUTHORIZED');
        return res.status(400).json({ error: 'Unauthorized' });
    }

    const days = clampLimit(q.days, 30, 180);   // analysis window, hard-capped
    const now = new Date();
    const meta = { view: view, generated_at: now.toISOString(), window_days: days };

    // WO-1843. The slice is parsed for EVERY view so the locale refusal is one
    // check in one place rather than three copies inside the sliceable blocks.
    const slice = parseSlice(q);

    // ⛔ A SLICE THE VIEW CANNOT HONOUR MUST SAY SO, NOT BE SWALLOWED. Only the
    // views listed here filter on app_version/platform. Asking for
    // ?view=overview&app_version=X used to return the UNFILTERED number under a
    // build label the caller believes was applied — the worst failure mode this
    // whole ticket is about, because it looks exactly like a working answer. This
    // lands in `meta`, which is Object.assign'd into every response below, so a
    // future view added without slicing inherits the honesty for free. (Same
    // instinct as the ?view=skus block telling the caller ?days was ignored.)
    const SLICEABLE_VIEWS = ['retention', 'funnel', 'purchases', 'active', 'stability'];
    if ((slice.app_version || slice.platform) && SLICEABLE_VIEWS.indexOf(view) < 0) {
        meta.slice_ignored = {
            app_version: slice.app_version,
            platform: slice.platform,
            note: 'IGNORED: ?view=' + view + ' does not support build/platform slicing, so the figures '
                + 'below are UNFILTERED. Sliceable views: ' + SLICEABLE_VIEWS.join(', ') + '.',
        };
    }
    if (slice.locale_requested) {
        // ⛔ REFUSED, NOT ANSWERED. See the LOCALE_GAP note above: nothing in
        // analytics_events carries a locale, so the only honest answers are a
        // refusal or a lie. 400 is this API's one refusal status.
        refuse('SLICE_AXIS_NOT_COLLECTED');
        return res.status(400).json({
            error: 'locale slicing is not available: no event payload carries a locale.',
            axis: 'locale',
            detail: LOCALE_GAP,
            available_axes: ['app_version', 'platform'],
        });
    }

    // =========================================================================
    // ================================================================== skus
    // WO-1532. THE CATALOG, NOT THE TELEMETRY. Owner ask 2026-09-06 20:52:
    // "can we add a list in command center of All SKU's and contents".
    //
    // ⛔ DISPATCHED HERE, ABOVE `const sql = neon(...)`, ON PURPOSE. This view
    // answers from files the deployment already carries; it has no rows to read.
    // Returning before the client is constructed makes "this view never touches
    // the database" a STRUCTURAL fact rather than a claim in a comment - it runs
    // green with DATABASE_URL unset, which is how test/admin.skus.view.test.js
    // proves it. A future statement added below would fail there loudly instead
    // of quietly making a catalog page depend on Neon being up.
    //
    // ⚠ IT IS STILL BEHIND THE READ GATE. The auth block above has already run;
    // this is not a public endpoint. The catalog is not a secret, but the shape
    // of what is unsellable is operator information.
    //
    // The join, the parity columns and the reverse direction all live in
    // api/_lib/sku-catalog.js. Nothing about the catalog is decided here: this
    // block is a route, not a second opinion.
    // =========================================================================
    if (view === 'skus') {
        const built = skuCatalog.build();
        return res.status(200).json(Object.assign(meta, built, {
            // window_days means nothing to a catalog. Saying so beats leaving a
            // number on screen that looks like it filtered something.
            window_days: null,
            window_note: 'A catalog has no time window. ?days= is ignored by this view.',
        }));
    }

    try {
        const sql = neon(process.env.DATABASE_URL);

        // ============================================================ overview
        // WHO IS PLAYING.
        //   active_today / _7d / _30d — DISTINCT player_id that fired a
        //     session_start inside the window. session_start is emitted once per
        //     app boot (EventTracker.Start), so this is "opened the game", not
        //     "was online at that moment".
        //   sessions_* — the session_start ROW count over the same windows (app
        //     opens, several per player per day).
        //   new_players_per_day — players whose FIRST-EVER event of any kind
        //     landed on that day. First-ever is computed over the WHOLE table, so
        //     someone who installed in June never re-counts as new in August.
        if (view === 'overview') {
            const active = await sql`
                SELECT
                    COUNT(DISTINCT player_id) FILTER (WHERE received_at > NOW() - INTERVAL '1 day')::bigint   AS active_today,
                    COUNT(DISTINCT player_id) FILTER (WHERE received_at > NOW() - INTERVAL '7 days')::bigint  AS active_7d,
                    COUNT(DISTINCT player_id) FILTER (WHERE received_at > NOW() - INTERVAL '30 days')::bigint AS active_30d,
                    COUNT(*) FILTER (WHERE received_at > NOW() - INTERVAL '1 day')::bigint   AS sessions_today,
                    COUNT(*) FILTER (WHERE received_at > NOW() - INTERVAL '7 days')::bigint  AS sessions_7d,
                    COUNT(*) FILTER (WHERE received_at > NOW() - INTERVAL '30 days')::bigint AS sessions_30d
                FROM analytics_events
                WHERE event_name = 'session_start'
                  AND player_id <> ${ANON_ID}
                  -- FIXED 30-day scan, deliberately NOT the ?days window: these three
                  -- tiles ARE the 1/7/30-day definitions, so letting a "last 7 days"
                  -- selection clip them would silently render active_30d as a 7-day
                  -- number under a "30 days" label. ?days drives the per-day tables
                  -- below, not these.
                  AND received_at > NOW() - INTERVAL '30 days'
                LIMIT 1`;

            // The blind spot, measured. If this dwarfs the numbers above, the
            // dashboard is reporting on a minority of the playerbase.
            const anon = await sql`
                SELECT COUNT(*) FILTER (WHERE event_name = 'session_start')::bigint AS anonymous_sessions,
                       COUNT(*)::bigint                                             AS anonymous_events
                FROM analytics_events
                WHERE player_id = ${ANON_ID}
                  AND received_at > NOW() - (${days} * INTERVAL '1 day')
                LIMIT 1`;

            const totals = await sql`
                SELECT COUNT(*)::bigint                  AS total_events,
                       COUNT(DISTINCT player_id)::bigint AS total_ids_seen,
                       MIN(received_at)                  AS first_event_at,
                       MAX(received_at)                  AS last_event_at
                FROM analytics_events
                LIMIT 1`;

            const newPerDay = await sql`
                WITH firsts AS (
                    SELECT player_id, MIN(received_at) AS first_seen
                    FROM analytics_events
                    WHERE player_id <> ${ANON_ID}
                    GROUP BY player_id
                )
                SELECT date_trunc('day', first_seen)::date::text AS day,
                       COUNT(*)::bigint                          AS new_players
                FROM firsts
                WHERE first_seen > NOW() - (${days} * INTERVAL '1 day')
                GROUP BY 1
                ORDER BY 1 DESC
                LIMIT 181`;

            const perDay = await sql`
                SELECT date_trunc('day', received_at)::date::text AS day,
                       COUNT(DISTINCT player_id) FILTER (WHERE event_name = 'session_start')::bigint AS active_players,
                       COUNT(*)                  FILTER (WHERE event_name = 'session_start')::bigint AS sessions,
                       COUNT(*)::bigint                                                              AS events
                FROM analytics_events
                WHERE received_at > NOW() - (${days} * INTERVAL '1 day')
                  AND player_id <> ${ANON_ID}
                GROUP BY 1
                ORDER BY 1 DESC
                LIMIT 181`;

            // What the game is actually emitting — the sanity check that a metric
            // reads 0 because nobody did it, not because the event never fires.
            const byEvent = await sql`
                SELECT event_name,
                       COUNT(*)::bigint                  AS events,
                       COUNT(DISTINCT player_id)::bigint AS ids,
                       MAX(received_at)                  AS latest
                FROM analytics_events
                WHERE received_at > NOW() - (${days} * INTERVAL '1 day')
                GROUP BY 1
                ORDER BY 2 DESC
                LIMIT 60`;

            const a = active[0] || {};
            return res.status(200).json(Object.assign(meta, {
                active: {
                    today: Number(a.active_today || 0),
                    d7: Number(a.active_7d || 0),
                    d30: Number(a.active_30d || 0),
                    sessions_today: Number(a.sessions_today || 0),
                    sessions_7d: Number(a.sessions_7d || 0),
                    sessions_30d: Number(a.sessions_30d || 0),
                },
                anonymous: {
                    sessions: Number((anon[0] || {}).anonymous_sessions || 0),
                    events: Number((anon[0] || {}).anonymous_events || 0),
                    note: 'Every player with no bound wallet shares the single id "anonymous", '
                        + 'so these cannot be split into people. Excluded from all player counts.',
                },
                totals: totals[0] || null,
                new_players_per_day: newPerDay,
                per_day: perDay,
                events_by_name: byEvent,
            }));
        }

        // =========================================================== retention
        // Classic DAY-N retention, by SIGNUP COHORT.
        //   cohort_day  — the day a player's FIRST-EVER event landed.
        //   cohort_size — how many players are in that cohort (ALWAYS returned
        //                 next to the percentages; see LOW_N_THRESHOLD).
        //   d1/d7/d30   — of that cohort, how many fired a session_start on
        //                 EXACTLY cohort_day + N. Exact-day, not "within N days"
        //                 — the stricter and more standard reading.
        //   *_mature    — false when the cohort has not existed for N days yet.
        //                 An immature bucket is NOT 0% retention; it is unknown,
        //                 and the page must render it as such.
        if (view === 'retention') {
            const rows = await sql`
                WITH firsts AS (
                    SELECT player_id, MIN(received_at) AS first_seen
                    FROM analytics_events
                    WHERE player_id <> ${ANON_ID}
                    GROUP BY player_id
                ),
                cohort AS (
                    SELECT f.player_id, date_trunc('day', f.first_seen)::date AS cohort_day
                    FROM firsts f
                    WHERE f.first_seen > NOW() - (${days} * INTERVAL '1 day')
                      -- WO-1843 build/platform slice. NULL means NO FILTER, so this one
                      -- query text serves both cases and the value never becomes SQL.
                      -- It selects PLAYERS who booted that build at least once, which is
                      -- ambiguous for a player who booted two. That ambiguity is printed in
                      -- the response slice block, never left implicit.
                      -- (No backticks in SQL comments: they terminate this template literal.)
                      AND (${slice.app_version}::text IS NULL OR EXISTS (
                          SELECT 1 FROM analytics_events sv
                           WHERE sv.event_name = 'session_start'
                             AND sv.player_id = f.player_id
                             AND sv.properties->>'appVersion' = ${slice.app_version}))
                      AND (${slice.platform}::text IS NULL OR EXISTS (
                          SELECT 1 FROM analytics_events sp
                           WHERE sp.event_name = 'session_start'
                             AND sp.player_id = f.player_id
                             AND sp.properties->>'platform' = ${slice.platform}))
                ),
                sessions_by_day AS (
                    SELECT DISTINCT e.player_id,
                           date_trunc('day', e.received_at)::date AS day
                    FROM analytics_events e
                    JOIN cohort c ON c.player_id = e.player_id
                    WHERE e.event_name = 'session_start'
                )
                SELECT c.cohort_day::text AS cohort_day,
                       COUNT(*)::bigint   AS cohort_size,
                       COUNT(*) FILTER (WHERE EXISTS (
                           SELECT 1 FROM sessions_by_day r
                           WHERE r.player_id = c.player_id AND r.day = c.cohort_day + 1))::bigint  AS d1,
                       COUNT(*) FILTER (WHERE EXISTS (
                           SELECT 1 FROM sessions_by_day r
                           WHERE r.player_id = c.player_id AND r.day = c.cohort_day + 7))::bigint  AS d7,
                       COUNT(*) FILTER (WHERE EXISTS (
                           SELECT 1 FROM sessions_by_day r
                           WHERE r.player_id = c.player_id AND r.day = c.cohort_day + 30))::bigint AS d30
                FROM cohort c
                GROUP BY 1
                ORDER BY 1 DESC
                LIMIT 181`;

            const today = new Date(Date.UTC(now.getUTCFullYear(), now.getUTCMonth(), now.getUTCDate()));
            const cohorts = rows.map(r => {
                const size = Number(r.cohort_size || 0);
                const day = new Date(r.cohort_day + 'T00:00:00Z');
                const ageDays = Math.floor((today - day) / 86400000);
                return {
                    cohort_day: r.cohort_day,
                    cohort_size: size,
                    low_n: size < LOW_N_THRESHOLD,
                    d1_players: Number(r.d1 || 0), d1_pct: pct(r.d1, size), d1_mature: ageDays >= 1,
                    d7_players: Number(r.d7 || 0), d7_pct: pct(r.d7, size), d7_mature: ageDays >= 7,
                    d30_players: Number(r.d30 || 0), d30_pct: pct(r.d30, size), d30_mature: ageDays >= 30,
                };
            });

            // Pooled rollup over MATURE cohorts only — one honest number each,
            // instead of averaging percentages (which over-weights tiny cohorts).
            const roll = (key, matureKey) => {
                const eligible = cohorts.filter(c => c[matureKey]);
                const size = eligible.reduce((s, c) => s + c.cohort_size, 0);
                const ret = eligible.reduce((s, c) => s + c[key], 0);
                return { cohort_size: size, returned: ret, pct: pct(ret, size), low_n: size < LOW_N_THRESHOLD, cohorts: eligible.length };
            };

            return res.status(200).json(Object.assign(meta, {
                definition: 'Day-N retention: of players whose FIRST-EVER event landed on cohort_day, '
                    + 'the share that fired a session_start on EXACTLY cohort_day + N.',
                low_n_threshold: LOW_N_THRESHOLD,
                slice: sliceMeta(slice,
                    ['cohort membership (which players are counted at all)'],
                    ['nothing — the slice narrows the cohort, so every figure below is sliced']),
                rollup: {
                    d1: roll('d1_players', 'd1_mature'),
                    d7: roll('d7_players', 'd7_mature'),
                    d30: roll('d30_players', 'd30_mature'),
                },
                cohorts: cohorts,
            }));
        }

        // ============================================================== funnel
        // THE TUTORIAL FUNNEL — the highest-value view on this page. There is a
        // live FTUE defect (WO-1036); this is what measures its cost in players.
        //
        // Per step, from the events TutorialFlow actually emits:
        //   enter    tutorial_step_enter    (properties: stepId, order, flowId)
        //   complete tutorial_step_complete (stepId, order, seconds)
        //   skip     tutorial_step_skip     (stepId, order, seconds) — player tapped skip
        //   drop     tutorial_step_drop     (stepId, secondsIdle, ...) — the WATCHDOG
        //            fired: the player sat stuck on this step until it auto-advanced.
        //            THIS IS THE DEFECT SIGNAL. It carries the step in properties,
        //            which is why it is read out per-step rather than as a total.
        // Ordering comes from properties->>'order' on the enter event, sorted
        // numerically in JS — never cast in SQL, because the value is client
        // JSONB and one malformed row would fail the whole query.
        if (view === 'funnel') {
            // WO-1843. Resolved ONCE and bound as one array into all six queries
            // below; ids === null means no slice was asked for and nothing is
            // filtered. See resolveSlicePlayers for why this is not six copies of
            // an EXISTS sub-select.
            const sliceResolved = await resolveSlicePlayers(sql, slice);
            const sliceIds = sliceResolved.ids;

            const flow = await sql`
                SELECT event_name,
                       COUNT(*)::bigint                  AS events,
                       COUNT(DISTINCT player_id) FILTER (WHERE player_id <> ${ANON_ID})::bigint AS players,
                       MAX(received_at)                  AS latest
                FROM analytics_events
                WHERE event_name IN ('tutorial_started', 'tutorial_completed', 'tutorial_skipped_all')
                  AND received_at > NOW() - (${days} * INTERVAL '1 day')
                  -- WO-1843 slice. A NULL id set is NO filter, so this one query text
                  -- serves the sliced and unsliced case and the value is never SQL.
                  AND (${sliceIds}::text[] IS NULL OR player_id = ANY(${sliceIds}::text[]))
                GROUP BY 1
                LIMIT 10`;

            const enters = await sql`
                SELECT properties->>'stepId' AS step_id,
                       MIN(properties->>'order') AS step_order,
                       COUNT(*)::bigint AS events,
                       COUNT(DISTINCT player_id) FILTER (WHERE player_id <> ${ANON_ID})::bigint AS players,
                       MAX(received_at) AS latest
                FROM analytics_events
                WHERE event_name = 'tutorial_step_enter'
                  AND received_at > NOW() - (${days} * INTERVAL '1 day')
                  AND (${sliceIds}::text[] IS NULL OR player_id = ANY(${sliceIds}::text[]))   -- WO-1843 slice
                GROUP BY 1
                ORDER BY 3 DESC
                LIMIT 200`;

            const completes = await sql`
                SELECT properties->>'stepId' AS step_id,
                       COUNT(*)::bigint AS events,
                       COUNT(DISTINCT player_id) FILTER (WHERE player_id <> ${ANON_ID})::bigint AS players
                FROM analytics_events
                WHERE event_name = 'tutorial_step_complete'
                  AND received_at > NOW() - (${days} * INTERVAL '1 day')
                  AND (${sliceIds}::text[] IS NULL OR player_id = ANY(${sliceIds}::text[]))   -- WO-1843 slice
                GROUP BY 1
                LIMIT 200`;

            const skips = await sql`
                SELECT properties->>'stepId' AS step_id,
                       COUNT(*)::bigint AS events,
                       COUNT(DISTINCT player_id) FILTER (WHERE player_id <> ${ANON_ID})::bigint AS players
                FROM analytics_events
                WHERE event_name = 'tutorial_step_skip'
                  AND received_at > NOW() - (${days} * INTERVAL '1 day')
                  AND (${sliceIds}::text[] IS NULL OR player_id = ANY(${sliceIds}::text[]))   -- WO-1843 slice
                GROUP BY 1
                LIMIT 200`;

            // The drop rows carry HOW LONG the player was stuck before the
            // watchdog rescued them — the difference between "a slow step" and
            // "a step nobody can get past".
            const drops = await sql`
                SELECT properties->>'stepId' AS step_id,
                       COUNT(*)::bigint AS events,
                       COUNT(DISTINCT player_id) FILTER (WHERE player_id <> ${ANON_ID})::bigint AS players,
                       MAX(properties->>'secondsIdle') AS max_seconds_idle_sample,
                       MAX(received_at) AS latest
                FROM analytics_events
                WHERE event_name = 'tutorial_step_drop'
                  AND received_at > NOW() - (${days} * INTERVAL '1 day')
                  AND (${sliceIds}::text[] IS NULL OR player_id = ANY(${sliceIds}::text[]))   -- WO-1843 slice
                GROUP BY 1
                ORDER BY 2 DESC
                LIMIT 200`;

            const contextual = await sql`
                SELECT properties->>'stepId' AS step_id,
                       COUNT(*)::bigint AS events,
                       COUNT(DISTINCT player_id) FILTER (WHERE player_id <> ${ANON_ID})::bigint AS players
                FROM analytics_events
                WHERE event_name = 'contextual_step_enter'
                  AND received_at > NOW() - (${days} * INTERVAL '1 day')
                  AND (${sliceIds}::text[] IS NULL OR player_id = ANY(${sliceIds}::text[]))   -- WO-1843 slice
                GROUP BY 1
                ORDER BY 2 DESC
                LIMIT 100`;

            const byId = (rows) => {
                const m = new Map();
                for (const r of rows) m.set(r.step_id == null ? '(no stepId)' : String(r.step_id), r);
                return m;
            };
            const mC = byId(completes), mS = byId(skips), mD = byId(drops);

            const steps = enters.map(e => {
                const id = e.step_id == null ? '(no stepId)' : String(e.step_id);
                const c = mC.get(id) || {}, s = mS.get(id) || {}, d = mD.get(id) || {};
                const players = Number(e.players || 0);
                return {
                    step_id: id,
                    order: num(e.step_order),
                    entered_players: players,
                    entered_events: Number(e.events || 0),
                    completed_players: Number(c.players || 0),
                    skipped_players: Number(s.players || 0),
                    dropped_players: Number(d.players || 0),
                    dropped_events: Number(d.events || 0),
                    max_seconds_idle_sample: num(d.max_seconds_idle_sample),
                    completion_pct: pct(c.players || 0, players),
                    drop_pct: pct(d.players || 0, players),
                    low_n: players < LOW_N_THRESHOLD,
                    latest: e.latest,
                };
            });
            // Numeric order where the client supplied one; unordered steps last,
            // by volume — never silently dropped.
            steps.sort((a, b) => {
                if (a.order == null && b.order == null) return b.entered_players - a.entered_players;
                if (a.order == null) return 1;
                if (b.order == null) return -1;
                return a.order - b.order;
            });
            // Drop-off BETWEEN consecutive steps: how many of the players who
            // entered step i never entered step i+1.
            for (let i = 0; i < steps.length; i++) {
                const next = steps[i + 1];
                steps[i].next_step_id = next ? next.step_id : null;
                steps[i].lost_to_next = next ? Math.max(0, steps[i].entered_players - next.entered_players) : null;
                steps[i].lost_to_next_pct = next ? pct(Math.max(0, steps[i].entered_players - next.entered_players), steps[i].entered_players) : null;
            }

            const flowMap = byId(flow.map(r => ({ step_id: r.event_name, ...r })));
            const started = Number((flowMap.get('tutorial_started') || {}).players || 0);
            const finished = Number((flowMap.get('tutorial_completed') || {}).players || 0);
            const skippedAll = Number((flowMap.get('tutorial_skipped_all') || {}).players || 0);

            return res.status(200).json(Object.assign(meta, {
                low_n_threshold: LOW_N_THRESHOLD,
                slice: sliceMeta(slice,
                    ['flow totals', 'every per-step row', 'contextual hints'],
                    ['nothing — all six queries carry the same player filter'],
                    sliceResolved),
                flow: {
                    started_players: started,
                    completed_players: finished,
                    skipped_all_players: skippedAll,
                    completion_pct: pct(finished, started),
                    skip_all_pct: pct(skippedAll, started),
                    low_n: started < LOW_N_THRESHOLD,
                    raw: flow,
                },
                steps: steps,
                contextual_hints: contextual,
                note: 'tutorial_step_drop = the TutorialFlow watchdog auto-advanced a stuck player. '
                    + 'A step with drops is a step players cannot get past on their own.',
            }));
        }

        // ============================================================= economy
        // ⚠ CLIENT-REPORTED INTENT — NOT SETTLEMENT, NOT REVENUE.
        // WHAT THE GAME SAID SOLD, AND WHAT THE PROMO/REFERRAL RAILS ACTUALLY DID.
        //
        // Everything under `purchases` / `bundle_views` / `conversion` here comes
        // from analytics_events, i.e. from the CLIENT's own word. That makes this
        // a genuine intent funnel (opened the pack card → said it completed) and
        // makes it useless as a money record. For what actually settled on chain,
        // read ?view=purchases — which is a SEPARATE view on purpose: see the
        // "TWO PURCHASE VIEWS" note in the file header. Do not blend the two.
        //
        // ⚠ purchase_completed carries NO price field. PackStore.cs:582 emits
        // { packId, packName, currency, txSig } only — the `price` in
        // EventTracker's doc comment is an EXAMPLE, not a live field. So this
        // view reports COUNTS, never revenue. `price_sample`/`amount_sample`
        // are read opportunistically in case a future emitter adds one; when
        // they come back null that means "the client never sent an amount",
        // NOT "the sale was free". Revenue must come from the chain or from
        // tower_swaps.cost_usdc, not from here.
        if (view === 'economy') {
            const purchases = await sql`
                SELECT properties->>'packId'   AS pack_id,
                       properties->>'packName' AS pack_name,
                       properties->>'currency' AS currency,
                       COUNT(*)::bigint AS purchases,
                       COUNT(DISTINCT player_id) FILTER (WHERE player_id <> ${ANON_ID})::bigint AS buyers,
                       MAX(properties->>'price')  AS price_sample,
                       MAX(properties->>'amount') AS amount_sample,
                       MAX(received_at) AS latest
                FROM analytics_events
                WHERE event_name = 'purchase_completed'
                  AND received_at > NOW() - (${days} * INTERVAL '1 day')
                GROUP BY 1, 2, 3
                ORDER BY 4 DESC
                LIMIT 100`;

            const views = await sql`
                SELECT properties->>'bundleId'   AS bundle_id,
                       properties->>'bundleName' AS bundle_name,
                       COUNT(*)::bigint AS views,
                       COUNT(DISTINCT player_id) FILTER (WHERE player_id <> ${ANON_ID})::bigint AS viewers,
                       MAX(received_at) AS latest
                FROM analytics_events
                WHERE event_name = 'bundle_viewed'
                  AND received_at > NOW() - (${days} * INTERVAL '1 day')
                GROUP BY 1, 2
                ORDER BY 3 DESC
                LIMIT 100`;

            // DB truth for promos (not analytics): what was redeemed, of which
            // code, and whether that code is wallet-bound. bound_wallet is
            // reported as a BOOLEAN — the bound address is another player's
            // wallet and does not belong on a dashboard.
            const promos = await sql`
                SELECT c.code,
                       c.active,
                       c.reward_crystals,
                       c.reward_coins,
                       c.max_redemptions,
                       c.expires_at,
                       (c.bound_wallet IS NOT NULL AND c.bound_wallet <> '') AS is_bound,
                       COUNT(r.redemption_id)::bigint AS redemptions,
                       COALESCE(SUM(r.crystals), 0)::bigint AS crystals_granted,
                       COALESCE(SUM(r.coins), 0)::bigint    AS coins_granted,
                       MAX(r.redeemed_at) AS latest_redemption
                FROM promo_codes c
                LEFT JOIN promo_redemptions r ON r.code = c.code
                GROUP BY c.code, c.active, c.reward_crystals, c.reward_coins,
                         c.max_redemptions, c.expires_at, c.bound_wallet
                ORDER BY 11 DESC NULLS LAST, c.created_at DESC
                LIMIT 100`;

            const referrals = await sql`
                SELECT (SELECT COUNT(*) FROM referrals)::bigint       AS codes_minted,
                       (SELECT COUNT(*) FROM referral_claims)::bigint AS claims,
                       (SELECT COUNT(DISTINCT referrer_id) FROM referral_claims)::bigint AS referrers_with_a_claim,
                       (SELECT COALESCE(SUM(crystals), 0) FROM referral_claims)::bigint  AS claim_crystals_granted
                LIMIT 1`;

            // The client-side view of the same rails — divergence between these
            // and the tables above is itself a finding (an event with no row =
            // the write never landed).
            const clientSide = await sql`
                SELECT event_name,
                       COUNT(*)::bigint AS events,
                       COUNT(DISTINCT player_id) FILTER (WHERE player_id <> ${ANON_ID})::bigint AS players,
                       MAX(received_at) AS latest
                FROM analytics_events
                WHERE event_name IN ('promo_redeemed', 'referral_code_generated',
                                     'referral_shared', 'referral_claimed',
                                     'purchase_completed', 'bundle_viewed')
                  AND received_at > NOW() - (${days} * INTERVAL '1 day')
                GROUP BY 1
                ORDER BY 2 DESC
                LIMIT 20`;

            // WO-1388 - the funnel, 7d and 30d side by side, COUNTS per event name
            // (events + distinct identified players). Fixed windows on purpose, not
            // `days`: the question is "where do players drop THIS week", and a
            // window that moves with the query string cannot be compared to last
            // week's answer. Every one of the six names is listed even when it has
            // no rows, so a step that never fired reads as 0 rather than vanishing.
            // `reason` on checkout_failed and `door` on store_opened are the two
            // properties pulled by name; nothing else in `properties` is returned.
            const funnelRows = await sql`
                SELECT event_name,
                       COUNT(*) FILTER (WHERE received_at > NOW() - INTERVAL '7 days')::bigint  AS events_7d,
                       COUNT(*) FILTER (WHERE received_at > NOW() - INTERVAL '30 days')::bigint AS events_30d,
                       COUNT(DISTINCT player_id) FILTER (WHERE player_id <> ${ANON_ID}
                             AND received_at > NOW() - INTERVAL '7 days')::bigint  AS players_7d,
                       COUNT(DISTINCT player_id) FILTER (WHERE player_id <> ${ANON_ID}
                             AND received_at > NOW() - INTERVAL '30 days')::bigint AS players_30d,
                       MAX(received_at) AS latest
                FROM analytics_events
                WHERE event_name IN ('store_opened', 'bundle_viewed', 'pack_tapped',
                                     'checkout_started', 'checkout_failed', 'purchase_completed')
                  AND received_at > NOW() - INTERVAL '30 days'
                GROUP BY 1
                LIMIT 10`;
            const funnelFailReasons = await sql`
                SELECT COALESCE(properties->>'reason', '(none)') AS reason,
                       COUNT(*) FILTER (WHERE received_at > NOW() - INTERVAL '7 days')::bigint  AS events_7d,
                       COUNT(*) FILTER (WHERE received_at > NOW() - INTERVAL '30 days')::bigint AS events_30d
                FROM analytics_events
                WHERE event_name = 'checkout_failed'
                  AND received_at > NOW() - INTERVAL '30 days'
                GROUP BY 1
                ORDER BY 3 DESC
                LIMIT 20`;
            const funnelDoors = await sql`
                SELECT COALESCE(properties->>'door', '(none)') AS door,
                       COUNT(*) FILTER (WHERE received_at > NOW() - INTERVAL '7 days')::bigint  AS events_7d,
                       COUNT(*) FILTER (WHERE received_at > NOW() - INTERVAL '30 days')::bigint AS events_30d
                FROM analytics_events
                WHERE event_name = 'store_opened'
                  AND received_at > NOW() - INTERVAL '30 days'
                GROUP BY 1
                ORDER BY 3 DESC
                LIMIT 20`;
            const funnelByName = new Map();
            for (const r of funnelRows) funnelByName.set(String(r.event_name), r);
            const storeFunnel = STORE_FUNNEL_EVENTS.map((name) => {
                const r = funnelByName.get(name);
                return {
                    event: name,
                    events_7d: Number(r ? r.events_7d : 0),
                    events_30d: Number(r ? r.events_30d : 0),
                    players_7d: Number(r ? r.players_7d : 0),
                    players_30d: Number(r ? r.players_30d : 0),
                    latest: r ? r.latest : null,
                };
            });

            // View → buy, per pack. bundle_viewed's bundleId and
            // purchase_completed's packId are BOTH pack.Sku (PackStore.cs), so
            // they join cleanly.
            const buyersByPack = new Map();
            for (const p of purchases) {
                const k = p.pack_id == null ? '(unknown)' : String(p.pack_id);
                buyersByPack.set(k, (buyersByPack.get(k) || 0) + Number(p.buyers || 0));
            }
            const conversion = views.map(v => {
                const k = v.bundle_id == null ? '(unknown)' : String(v.bundle_id);
                const viewers = Number(v.viewers || 0);
                const buyers = buyersByPack.get(k) || 0;
                return {
                    pack_id: k,
                    pack_name: v.bundle_name,
                    viewers: viewers,
                    buyers: buyers,
                    conversion_pct: pct(buyers, viewers),
                    low_n: viewers < LOW_N_THRESHOLD,
                };
            }).sort((a, b) => b.viewers - a.viewers);

            return res.status(200).json(Object.assign(meta, {
                low_n_threshold: LOW_N_THRESHOLD,
                // Kept as `purchases` because site/admin.html reads that key; the
                // LABEL is what changes, and it changes to the truth.
                source: 'analytics_events — CLIENT-REPORTED INTENT, not settlement.',
                client_reported: true,
                see_also: '?view=purchases — server-verified settlement and revenue from '
                    + 'purchase_entitlements. The two are never blended; where they disagree, that '
                    + 'disagreement is itself the signal.',
                revenue_note: 'CLIENT-REPORTED, NOT REVENUE. purchase_completed carries no amount '
                    + '(PackStore emits packId/packName/currency/txSig only), so these are COUNTS of '
                    + 'what the client said it did. A null price_sample means the client never sent '
                    + 'one — never that the sale was free. Real revenue lives in ?view=purchases.',
                purchases: purchases,
                bundle_views: views,
                conversion: conversion,
                // WO-1388: the six-step store funnel, fixed 7d/30d windows, every
                // step present even at zero. Read top to bottom: the first zero is
                // where players drop.
                store_funnel: {
                    windows: ['7d', '30d'],
                    steps: storeFunnel,
                    checkout_failed_reasons: funnelFailReasons,
                    store_opened_doors: funnelDoors,
                    note: 'CLIENT-REPORTED counts per event name (PackStore.cs emits one Track per step). '
                        + 'Fixed 7d/30d windows, independent of ?days=, so weeks compare. A step missing '
                        + 'from analytics_events is reported as 0, never omitted.',
                    // WO-1841. The owner's question was "did anyone open the store, and how many of
                    // those went on to ask for a price". Step 1 (store_opened) is right here; the
                    // PRICE-QUOTE step is a SERVER row in purchase_quotes and is reported by
                    // ?view=purchases -> quote_funnel.
                    //
                    // ⛔ THE TWO ARE NOT JOINED, AND THAT IS THE HONEST ANSWER, NOT A GAP.
                    // analytics_events keys on player_id; purchase_quotes keys on `wallet`
                    // (api/schema.sql:1395). There is no bridge between those identity spaces, so a
                    // per-player "opened -> quoted" conversion CANNOT be computed from these tables —
                    // and this file's own contract (see the `see_also` on this response) is that
                    // client-reported intent and server settlement are never blended. A ratio across
                    // the two would look like a conversion rate and be an artifact of two different
                    // denominators, which is worse than no number at all.
                    //
                    // Also load-bearing, read at source: opening the store does NOT mint a quote row.
                    // PackStore.Open -> RefreshQuotedPrices (PackStore.cs:571) hits the LIST mode of
                    // api/purchases/quote.js, which "binds nothing, persists nothing" (quote.js:353).
                    // The INSERTs (quote.js:471, :519) happen only on a single-SKU quote, i.e. when
                    // the player actually reaches for the till. So quote-issued IS a genuine step
                    // DOWNSTREAM of store_opened, not something co-fired by the screen appearing.
                    quote_step: {
                        where: '?view=purchases -> quote_funnel (issued / consumed / expired_unconsumed '
                            + '/ live / wallets_quoted), plus by_sku and per_day.',
                        source: 'purchase_quotes — SERVER rows, not analytics_events.',
                        joinable_to_store_opened: false,
                        why_not: 'analytics_events keys on player_id, purchase_quotes on wallet. No '
                            + 'bridge exists, so there is no per-player opened->quoted conversion to '
                            + 'report. Read the two counts side by side; do not divide one by the other.',
                        not_a_quote: 'Opening the store refreshes DISPLAY prices through the LIST mode '
                            + 'of api/purchases/quote.js, which persists nothing — so store opens do '
                            + 'not inflate issued.',
                    },
                },
                promo_codes: promos,
                referrals: referrals[0] || null,
                client_events: clientSide,
            }));
        }

        // =========================================================== purchases
        // SERVER TRUTH. Sourced from purchase_entitlements + purchase_quotes —
        // the rows api/purchases/{quote,verify,fulfill} write — and NEVER from
        // analytics_events.
        //
        // ⛔ WHY THIS IS A SECOND VIEW RATHER THAN A FIX TO ?view=economy:
        // the two answer different questions and MUST NOT be blended.
        //   ?view=economy    — what the CLIENT said happened (purchase_completed).
        //   ?view=purchases  — what the SERVER independently verified on chain.
        // A single merged figure would average away the disagreement, and the
        // DISAGREEMENT IS THE ALERT: a client purchase_completed whose txSig has
        // no entitlement row means a grant may have gone out with no settlement
        // recorded behind it. It is surfaced below, never reconciled — because
        // reconciling is a WRITE and no write lives in this file.
        //
        // ── REVENUE ─────────────────────────────────────────────────────────
        // usd_anchor is the AUTHORED LADDER PRICE (2.99, 4.99 …) persisted onto
        // the row at verify time, so it is a stable historical figure and not a
        // re-derivation against today's market. It is the only honest revenue
        // number the database holds; analytics cannot produce one at all.
        // ⚠ It is NULL on the two CANARY skus (schema.sql — pinned protocol
        // constants with no rate behind them), so every total ships next to
        // `rows_without_usd_anchor`: a non-zero count means the total UNDERSTATES
        // the row count, NOT that those sales were free.
        // ⚠ observed_lamports is a BASE-UNIT integer whose decimals live on the
        // QUOTE (6 on mainnet SKR, 9 on devnet), not on the entitlement. It is
        // therefore reported raw, per-currency, and never summed into a
        // human-readable token amount here.
        //
        // ── THE QUOTE FUNNEL is the health signal ───────────────────────────
        // A quote is issued when a player opens the wallet prompt and consumed
        // when their payment verifies; TTL is 5 minutes. A fall in
        // consumed/issued means people are TRYING TO BUY AND FAILING, which no
        // other metric on this dashboard can see.
        //
        // Wallets are masked exactly as everywhere else. tx_signature is NOT
        // masked: it is already a public chain record, and it is the precise
        // string the operator needs to answer "did that actually land".
        //
        // Each table is probed INDEPENDENTLY. Schema drift is real here (three
        // of these tables were invisible to the admin surface until 2026-08-24,
        // and a stale CHECK constraint silently failed a settled mainnet sale),
        // so a missing or altered table degrades to an entry in `errors` rather
        // than 500-ing the whole view.
        if (view === 'purchases') {
            const errors = [];
            const probe = async (label, run) => {
                try {
                    return await run();
                } catch (err) {
                    console.error('[admin/stats] purchases probe failed:', label, err);
                    errors.push({ probe: label, error: String((err && err.message) || err) });
                    return null;
                }
            };

            // WO-1843. The build/platform slice, resolved to a player-id set.
            //
            // ⚠ THIS CROSSES TWO ID SPACES ON PURPOSE AND IT IS NOT A JOIN ANY MORE
            // THAN IT HAS TO BE. purchase_entitlements is keyed on `wallet`;
            // app_version lives on analytics_events.player_id. Those are the same
            // string only for a WALLET-BOUND player (WO-1791 identity tiers), so a
            // sliced revenue figure can only ever see wallet-bound buyers. Measured
            // read-only 2026-09-17: 2 of 2 all-time payer wallets DO appear in
            // analytics_events, so the join holds today — but it is reported as a
            // caveat rather than assumed, because a guest-rail purchase would be
            // invisible to it.
            const sliceResolved = await resolveSlicePlayers(sql, slice);
            const sliceIds = sliceResolved.ids;

            // ---- settled totals (purchase_entitlements) ---------------------
            const totals = await probe('entitlements_totals', () => sql`
                SELECT COUNT(*)::bigint                                   AS settled_all_time,
                       COUNT(DISTINCT wallet)::bigint                     AS buyers_all_time,
                       COALESCE(SUM(usd_anchor), 0)::float8               AS usd_all_time,
                       COUNT(*) FILTER (WHERE usd_anchor IS NULL)::bigint AS rows_without_usd_anchor,
                       COUNT(*) FILTER (WHERE created_at > NOW() - (${days} * INTERVAL '1 day'))::bigint AS settled_window,
                       COUNT(DISTINCT wallet) FILTER (WHERE created_at > NOW() - (${days} * INTERVAL '1 day'))::bigint AS buyers_window,
                       COALESCE(SUM(usd_anchor) FILTER (WHERE created_at > NOW() - (${days} * INTERVAL '1 day')), 0)::float8 AS usd_window,
                       MIN(created_at) AS first_settled_at,
                       MAX(created_at) AS last_settled_at
                FROM purchase_entitlements
                -- WO-1843 slice. NULL id set = no filter. Wallet-vs-player_id caveat above.
                WHERE (${sliceIds}::text[] IS NULL OR wallet = ANY(${sliceIds}::text[]))
                LIMIT 1`);

            // ---- counts by settlement status --------------------------------
            // verified      = chain-confirmed, grant not yet handed over
            // fulfilled     = grant delivered
            // manual_review = verified but something did not match (e.g. the
            //                 payment landed outside the quote window) — the
            //                 money moved and a human has to look.
            const byStatus = await probe('entitlements_by_status', () => sql`
                SELECT status,
                       COUNT(*)::bigint                     AS rows,
                       COUNT(DISTINCT wallet)::bigint       AS wallets,
                       COALESCE(SUM(usd_anchor), 0)::float8 AS usd_anchor_total,
                       MAX(created_at)                      AS latest
                FROM purchase_entitlements
                WHERE (${sliceIds}::text[] IS NULL OR wallet = ANY(${sliceIds}::text[]))   -- WO-1843 slice
                GROUP BY 1
                ORDER BY 2 DESC
                LIMIT 10`);

            // ---- per-SKU breakdown ------------------------------------------
            const bySku = await probe('entitlements_by_sku', () => sql`
                SELECT sku,
                       currency,
                       network,
                       COUNT(*)::bigint                                        AS settled,
                       COUNT(DISTINCT wallet)::bigint                          AS buyers,
                       COUNT(*) FILTER (WHERE status = 'fulfilled')::bigint     AS fulfilled,
                       COUNT(*) FILTER (WHERE status = 'verified')::bigint      AS awaiting_fulfilment,
                       COUNT(*) FILTER (WHERE status = 'manual_review')::bigint AS manual_review,
                       COALESCE(SUM(usd_anchor), 0)::float8                     AS usd_anchor_total,
                       COUNT(*) FILTER (WHERE usd_anchor IS NULL)::bigint       AS rows_without_usd_anchor,
                       COALESCE(SUM(observed_lamports), 0)::float8              AS base_units_observed,
                       MIN(created_at)                                          AS first_settled_at,
                       MAX(created_at)                                          AS last_settled_at
                FROM purchase_entitlements
                WHERE (${sliceIds}::text[] IS NULL OR wallet = ANY(${sliceIds}::text[]))   -- WO-1843 slice
                GROUP BY 1, 2, 3
                ORDER BY 4 DESC
                LIMIT 100`);

            // ---- revenue per day, over the window ---------------------------
            const perDay = await probe('entitlements_per_day', () => sql`
                SELECT date_trunc('day', created_at)::date::text AS day,
                       COUNT(*)::bigint                          AS settled,
                       COUNT(DISTINCT wallet)::bigint            AS buyers,
                       COALESCE(SUM(usd_anchor), 0)::float8      AS usd_anchor_total
                FROM purchase_entitlements
                WHERE created_at > NOW() - (${days} * INTERVAL '1 day')
                  AND (${sliceIds}::text[] IS NULL OR wallet = ANY(${sliceIds}::text[]))   -- WO-1843 slice
                GROUP BY 1
                ORDER BY 1 DESC
                LIMIT 181`);

            // ---- the rows that need a human ---------------------------------
            // Anything not yet 'fulfilled': the chain confirmed the money and the
            // grant is not recorded as delivered. This is the "verify they
            // received it" list.
            const unfulfilled = await probe('entitlements_unfulfilled', () => sql`
                SELECT entitlement_id, tx_signature, wallet, sku, currency, network,
                       status, usd_anchor, quote_ref, chain_slot,
                       verified_at, fulfilled_at, created_at
                FROM purchase_entitlements
                WHERE status <> 'fulfilled'
                ORDER BY created_at DESC
                LIMIT 100`);

            // ---- most recent settlements ------------------------------------
            const recent = await probe('entitlements_recent', () => sql`
                SELECT entitlement_id, tx_signature, wallet, sku, currency, network,
                       status, expected_lamports, observed_lamports, chain_slot,
                       usd_anchor, usd_rate, rate_source, quote_ref,
                       verified_at, fulfilled_at, created_at
                FROM purchase_entitlements
                ORDER BY created_at DESC
                LIMIT 50`);

            // ---- ⭐ the quote → settle funnel (purchase_quotes) --------------
            const funnel = await probe('quotes_funnel', () => sql`
                SELECT COUNT(*)::bigint                                                            AS issued,
                       COUNT(*) FILTER (WHERE consumed_at IS NOT NULL)::bigint                     AS consumed,
                       COUNT(*) FILTER (WHERE consumed_at IS NULL AND expires_at <= NOW())::bigint AS expired_unconsumed,
                       COUNT(*) FILTER (WHERE consumed_at IS NULL AND expires_at >  NOW())::bigint AS live,
                       COUNT(DISTINCT wallet)::bigint                                              AS wallets_quoted,
                       COUNT(DISTINCT wallet) FILTER (WHERE consumed_at IS NOT NULL)::bigint        AS wallets_that_paid,
                       MAX(issued_at)                                                              AS last_issued_at,
                       MAX(consumed_at)                                                            AS last_consumed_at
                FROM purchase_quotes
                WHERE issued_at > NOW() - (${days} * INTERVAL '1 day')
                LIMIT 1`);

            const funnelBySku = await probe('quotes_funnel_by_sku', () => sql`
                SELECT sku,
                       network,
                       COUNT(*)::bigint                                                            AS issued,
                       COUNT(*) FILTER (WHERE consumed_at IS NOT NULL)::bigint                     AS consumed,
                       COUNT(*) FILTER (WHERE consumed_at IS NULL AND expires_at <= NOW())::bigint AS expired_unconsumed,
                       COUNT(*) FILTER (WHERE consumed_at IS NULL AND expires_at >  NOW())::bigint AS live,
                       MAX(issued_at)                                                              AS last_issued_at
                FROM purchase_quotes
                WHERE issued_at > NOW() - (${days} * INTERVAL '1 day')
                GROUP BY 1, 2
                ORDER BY 3 DESC
                LIMIT 100`);

            const funnelPerDay = await probe('quotes_funnel_per_day', () => sql`
                SELECT date_trunc('day', issued_at)::date::text                                    AS day,
                       COUNT(*)::bigint                                                            AS issued,
                       COUNT(*) FILTER (WHERE consumed_at IS NOT NULL)::bigint                     AS consumed,
                       COUNT(*) FILTER (WHERE consumed_at IS NULL AND expires_at <= NOW())::bigint AS expired_unconsumed
                FROM purchase_quotes
                WHERE issued_at > NOW() - (${days} * INTERVAL '1 day')
                GROUP BY 1
                ORDER BY 1 DESC
                LIMIT 181`);

            // ---- ⛔ client-vs-server disagreement ----------------------------
            // Reported as COUNTS ON BOTH SIDES plus the orphan list. Never as one
            // blended number.
            const clientSide = await probe('client_reported_counts', () => sql`
                SELECT COUNT(*)::bigint                                                            AS completed_events,
                       COUNT(DISTINCT properties->>'txSig')::bigint                                AS distinct_tx_signatures,
                       COUNT(DISTINCT player_id) FILTER (WHERE player_id <> ${ANON_ID})::bigint     AS players,
                       MAX(received_at)                                                            AS latest
                FROM analytics_events
                WHERE event_name = 'purchase_completed'
                  AND received_at > NOW() - (${days} * INTERVAL '1 day')
                LIMIT 1`);

            // THE ALERT: the client announced a completed purchase and the server
            // holds NO verified entitlement for that signature.
            const clientOrphans = await probe('client_events_without_entitlement', () => sql`
                SELECT e.properties->>'txSig'  AS tx_signature,
                       e.properties->>'packId' AS pack_id,
                       e.player_id             AS player_id,
                       COUNT(*)::bigint        AS events,
                       MAX(e.received_at)      AS latest
                FROM analytics_events e
                WHERE e.event_name = 'purchase_completed'
                  AND e.received_at > NOW() - (${days} * INTERVAL '1 day')
                  AND NOT EXISTS (
                      SELECT 1 FROM purchase_entitlements p
                      WHERE p.tx_signature = e.properties->>'txSig')
                  AND NOT EXISTS (
                      SELECT 1 FROM analytics_events a
                      WHERE a.event_name = 'admin_ops_write'
                        AND a.properties->>'action' = 'purchase.alert_acknowledge'
                        AND a.properties->>'target' = e.properties->>'txSig'
                        AND a.properties->>'outcome' = 'acknowledged_no_action')
                GROUP BY 1, 2, 3
                ORDER BY 5 DESC
                LIMIT 100`);

            // The mirror case. Far less alarming — the client can simply have
            // failed to report, or the sale came in through /purchases/reconcile
            // — but a large number here means the analytics funnel UNDERSTATES
            // sales, which is worth knowing before anyone reads ?view=economy as
            // if it were revenue.
            const serverOrphans = await probe('entitlements_without_client_event', () => sql`
                SELECT COUNT(*)::bigint AS settled_without_client_event
                FROM purchase_entitlements p
                WHERE p.created_at > NOW() - (${days} * INTERVAL '1 day')
                  AND NOT EXISTS (
                      SELECT 1 FROM analytics_events e
                      WHERE e.event_name = 'purchase_completed'
                        AND e.properties->>'txSig' = p.tx_signature)
                LIMIT 1`);

            const t = (totals && totals[0]) || {};
            const f = (funnel && funnel[0]) || {};
            const c = (clientSide && clientSide[0]) || {};

            const issued = Number(f.issued || 0);
            const consumed = Number(f.consumed || 0);

            // Rows carry a real wallet address; mask it the way every other view
            // here does. tx_signature stays whole on purpose (see header).
            const maskRow = (r) => {
                const out = Object.assign({}, r);
                delete out.wallet;
                out.wallet_masked = maskId(r.wallet);
                return out;
            };

            return res.status(200).json(Object.assign(meta, {
                source: 'purchase_entitlements + purchase_quotes — SERVER-VERIFIED settlement, not '
                    + 'client-reported. ?view=economy reports the client side; the two are '
                    + 'deliberately never blended.',
                low_n_threshold: LOW_N_THRESHOLD,
                // WO-1843. THE SLICE IS PARTIAL AND SAYS SO. The revenue aggregates are
                // filtered; the operational lists deliberately are NOT — a build filter
                // must never hide an unfulfilled sale a human still has to act on.
                slice: sliceMeta(slice,
                    ['settled totals', 'by_status', 'by_sku', 'per_day'],
                    ['needs_attention.rows (an unfulfilled sale must NEVER be hidden by a filter)',
                     'recent_settlements',
                     'quote_funnel (purchase_quotes — the quote rail is not sliced)',
                     'disagreement (the client-vs-server alert must stay whole)'],
                    sliceResolved),
                slice_identity_caveat: 'Slicing revenue crosses two id spaces: purchase_entitlements '
                    + 'is keyed on `wallet`, app_version on analytics_events.player_id. They match '
                    + 'only for WALLET-BOUND players, so a sliced revenue figure cannot see a '
                    + 'guest-rail purchase at all. Measured read-only 2026-09-17: 2 of 2 all-time '
                    + 'payer wallets do appear in analytics_events.',
                revenue_note: 'Revenue is SUM(usd_anchor) — the authored ladder price persisted onto '
                    + 'the row at verify time. usd_anchor is NULL on the CANARY skus (pinned protocol '
                    + 'constants with no rate behind them), so rows_without_usd_anchor > 0 means the '
                    + 'total understates the row count, NOT that those sales were free. '
                    + 'base_units_observed is a raw integer whose decimals live on the quote (6 mainnet, '
                    + '9 devnet) and is deliberately not converted to a token amount here.',
                settled: {
                    all_time: Number(t.settled_all_time || 0),
                    all_time_buyers: Number(t.buyers_all_time || 0),
                    all_time_usd_anchor: Number(t.usd_all_time || 0),
                    window: Number(t.settled_window || 0),
                    window_buyers: Number(t.buyers_window || 0),
                    window_usd_anchor: Number(t.usd_window || 0),
                    rows_without_usd_anchor: Number(t.rows_without_usd_anchor || 0),
                    first_settled_at: t.first_settled_at || null,
                    last_settled_at: t.last_settled_at || null,
                },
                by_status: byStatus || [],
                by_sku: bySku || [],
                per_day: perDay || [],
                quote_funnel: {
                    definition: 'A quote is issued when the wallet prompt opens and consumed when the '
                        + 'payment verifies (5-minute TTL). consumed/issued falling is players TRYING '
                        + 'TO BUY AND FAILING — the earliest warning this rail has.',
                    // WO-1841 — the reverse pointer. `issued` is the step AFTER "the player opened
                    // the store at all"; that earlier step is store_opened, counted in
                    // ?view=economy -> store_funnel. Named here so an operator reading a low
                    // `issued` can tell "nobody opened the store" from "they opened it and never
                    // reached the till" — which is the whole question, and it needs BOTH views.
                    // Deliberately a pointer, not a computed ratio: see store_funnel.quote_step.
                    preceding_step: '?view=economy -> store_funnel (store_opened / pack_tapped / '
                        + 'checkout_started). Those are CLIENT-reported and keyed on player_id; these '
                        + 'are SERVER rows keyed on wallet. Compare the counts; they cannot be joined.',
                    issued: issued,
                    consumed: consumed,
                    expired_unconsumed: Number(f.expired_unconsumed || 0),
                    live: Number(f.live || 0),
                    wallets_quoted: Number(f.wallets_quoted || 0),
                    wallets_that_paid: Number(f.wallets_that_paid || 0),
                    consumed_pct: pct(consumed, issued),
                    low_n: issued < LOW_N_THRESHOLD,
                    last_issued_at: f.last_issued_at || null,
                    last_consumed_at: f.last_consumed_at || null,
                    by_sku: (funnelBySku || []).map(r => Object.assign({}, r, {
                        consumed_pct: pct(r.consumed, r.issued),
                        low_n: Number(r.issued || 0) < LOW_N_THRESHOLD,
                    })),
                    per_day: (funnelPerDay || []).map(r => Object.assign({}, r, {
                        consumed_pct: pct(r.consumed, r.issued),
                        low_n: Number(r.issued || 0) < LOW_N_THRESHOLD,
                    })),
                },
                needs_attention: {
                    note: 'status <> fulfilled: the chain confirmed the money and the grant is not '
                        + 'recorded as delivered. Re-granting is a WRITE and does not live on this '
                        + 'endpoint — this view only tells you which rows to act on.',
                    rows: (unfulfilled || []).map(maskRow),
                },
                recent_settlements: (recent || []).map(maskRow),
                disagreement: {
                    note: 'Client-reported vs server-settled, side by side and NEVER merged. A client '
                        + 'purchase_completed with no entitlement row for its txSig means a grant may '
                        + 'have been handed out with no verified settlement behind it. Surfaced, not '
                        + 'reconciled — reconciling is a write.',
                    client_completed_events: Number(c.completed_events || 0),
                    client_distinct_tx_signatures: Number(c.distinct_tx_signatures || 0),
                    client_players: Number(c.players || 0),
                    client_latest: c.latest || null,
                    server_settled_window: Number(t.settled_window || 0),
                    client_events_without_entitlement: (clientOrphans || []).map(r => ({
                        tx_signature: r.tx_signature,
                        pack_id: r.pack_id,
                        player_masked: maskId(r.player_id),
                        events: Number(r.events || 0),
                        latest: r.latest,
                    })),
                    settled_without_client_event: Number(
                        ((serverOrphans && serverOrphans[0]) || {}).settled_without_client_event || 0),
                },
                errors: errors,
            }));
        }

        // ============================================================= command
        // WO-1281 - THE DECISION SURFACE. One request, five questions:
        //   1. What is selling?
        //   2. Do players return after trying it?
        //   3. Are returning players progressing / levelling?
        //   4. Are players playing once and never coming back?
        //   5. How long is a session? ("average online time", owner, 2026-08-30)
        //
        // ⛔ EVERY BLOCK DECLARES ITS OWN BACKING AND ITS OWN STATE.
        // A card that renders a confident number with nothing behind it is worse
        // than no card, and this repo has been bitten by exactly that shape. So
        // each block carries:
        //     backing  - the literal table(s)/column(s) the figure came from
        //     state    - 'ok' | 'empty' | 'not_instrumented' | 'error'
        //     read_ok  - false the moment its query threw
        // The console renders `state`, never a bare number, so a FAILED QUERY
        // CAN NEVER PAINT ITSELF AS A ZERO (acceptance 8).
        //
        // ⛔ SALES AUTHORITY IS THE SERVER, ALWAYS. Money comes from
        // purchase_entitlements + purchase_quotes and NEVER from the client's
        // purchase_completed event. The client figure appears in exactly one
        // place - the disagreement count - and is labelled as the alert it is.
        //
        // ⛔ NOTHING HERE IS A WRITE. Same contract as the rest of this file.
        if (view === 'command') {
            const EXCLUDED = excludedPlayerIds();
            const errors = [];
            const probe = async (label, run) => {
                try {
                    return await run();
                } catch (err) {
                    console.error('[admin/stats] command probe failed:', label, err);
                    errors.push({ probe: label, error: String((err && err.message) || err) });
                    return null;
                }
            };

            // ---------------------------------------------------------- SALES
            // Today / 7d / 30d, each next to the IMMEDIATELY PRECEDING equal
            // window, so "is this good" is answerable on the card rather than
            // from memory. usd_anchor is the authored ladder price persisted at
            // verify time - a stable historical figure, not a re-derivation
            // against today's market.
            const salesTotals = await probe('sales_totals', () => sql`
                SELECT
                    COUNT(*)::bigint                                                                       AS all_settled,
                    COUNT(DISTINCT wallet)::bigint                                                         AS all_buyers,
                    COALESCE(SUM(usd_anchor), 0)::float8                                                   AS all_usd,
                    COUNT(*) FILTER (WHERE usd_anchor IS NULL)::bigint                                     AS rows_without_usd_anchor,
                    MIN(created_at)                                                                        AS first_settled_at,
                    MAX(created_at)                                                                        AS last_settled_at,

                    COUNT(*) FILTER (WHERE created_at > NOW() - INTERVAL '1 day')::bigint                   AS d1_settled,
                    COUNT(DISTINCT wallet) FILTER (WHERE created_at > NOW() - INTERVAL '1 day')::bigint     AS d1_buyers,
                    COALESCE(SUM(usd_anchor) FILTER (WHERE created_at > NOW() - INTERVAL '1 day'), 0)::float8 AS d1_usd,
                    COUNT(*) FILTER (WHERE created_at > NOW() - INTERVAL '2 days'
                                       AND created_at <= NOW() - INTERVAL '1 day')::bigint                  AS d1_prior_settled,
                    COALESCE(SUM(usd_anchor) FILTER (WHERE created_at > NOW() - INTERVAL '2 days'
                                       AND created_at <= NOW() - INTERVAL '1 day'), 0)::float8              AS d1_prior_usd,

                    COUNT(*) FILTER (WHERE created_at > NOW() - INTERVAL '7 days')::bigint                  AS d7_settled,
                    COUNT(DISTINCT wallet) FILTER (WHERE created_at > NOW() - INTERVAL '7 days')::bigint    AS d7_buyers,
                    COALESCE(SUM(usd_anchor) FILTER (WHERE created_at > NOW() - INTERVAL '7 days'), 0)::float8 AS d7_usd,
                    COUNT(*) FILTER (WHERE created_at > NOW() - INTERVAL '14 days'
                                       AND created_at <= NOW() - INTERVAL '7 days')::bigint                 AS d7_prior_settled,
                    COALESCE(SUM(usd_anchor) FILTER (WHERE created_at > NOW() - INTERVAL '14 days'
                                       AND created_at <= NOW() - INTERVAL '7 days'), 0)::float8             AS d7_prior_usd,

                    COUNT(*) FILTER (WHERE created_at > NOW() - INTERVAL '30 days')::bigint                 AS d30_settled,
                    COUNT(DISTINCT wallet) FILTER (WHERE created_at > NOW() - INTERVAL '30 days')::bigint   AS d30_buyers,
                    COALESCE(SUM(usd_anchor) FILTER (WHERE created_at > NOW() - INTERVAL '30 days'), 0)::float8 AS d30_usd,
                    COUNT(*) FILTER (WHERE created_at > NOW() - INTERVAL '60 days'
                                       AND created_at <= NOW() - INTERVAL '30 days')::bigint                AS d30_prior_settled,
                    COALESCE(SUM(usd_anchor) FILTER (WHERE created_at > NOW() - INTERVAL '60 days'
                                       AND created_at <= NOW() - INTERVAL '30 days'), 0)::float8            AS d30_prior_usd
                FROM purchase_entitlements
                LIMIT 1`);

            const salesBySku = await probe('sales_by_sku', () => sql`
                SELECT sku,
                       COUNT(*)::bigint                                                                     AS units_all,
                       COUNT(DISTINCT wallet)::bigint                                                       AS buyers_all,
                       COALESCE(SUM(usd_anchor), 0)::float8                                                 AS usd_all,
                       COUNT(*) FILTER (WHERE created_at > NOW() - (${days} * INTERVAL '1 day'))::bigint     AS units_window,
                       COALESCE(SUM(usd_anchor) FILTER (WHERE created_at > NOW() - (${days} * INTERVAL '1 day')), 0)::float8 AS usd_window,
                       COUNT(*) FILTER (WHERE usd_anchor IS NULL)::bigint                                   AS rows_without_usd_anchor,
                       MAX(created_at)                                                                      AS last_settled_at
                FROM purchase_entitlements
                GROUP BY 1
                ORDER BY 4 DESC
                LIMIT 100`);

            // First-time vs repeat, judged on the buyer's OWN purchase order, so
            // a player's second-ever purchase reads as repeat whenever it landed.
            const salesRepeat = await probe('sales_first_vs_repeat', () => sql`
                WITH ordered AS (
                    SELECT wallet, created_at,
                           ROW_NUMBER() OVER (PARTITION BY wallet ORDER BY created_at) AS n
                    FROM purchase_entitlements
                )
                SELECT COUNT(*) FILTER (WHERE n = 1 AND created_at > NOW() - (${days} * INTERVAL '1 day'))::bigint AS first_time_window,
                       COUNT(*) FILTER (WHERE n > 1 AND created_at > NOW() - (${days} * INTERVAL '1 day'))::bigint AS repeat_window,
                       COUNT(DISTINCT wallet) FILTER (WHERE n > 1)::bigint                                        AS repeat_buyers_all_time
                FROM ordered
                LIMIT 1`);

            // The quote funnel is the ONLY thing that can see people TRYING to
            // buy. When nothing has settled it is the difference between "nobody
            // wants it" and "the rail is broken".
            const salesQuotes = await probe('sales_quote_funnel', () => sql`
                SELECT COUNT(*)::bigint                                                            AS issued,
                       COUNT(*) FILTER (WHERE consumed_at IS NOT NULL)::bigint                     AS consumed,
                       COUNT(*) FILTER (WHERE consumed_at IS NULL AND expires_at <= NOW())::bigint AS expired_unconsumed,
                       COUNT(DISTINCT wallet)::bigint                                              AS wallets_quoted,
                       MAX(issued_at)                                                              AS last_issued_at
                FROM purchase_quotes
                WHERE issued_at > NOW() - (${days} * INTERVAL '1 day')
                LIMIT 1`);

            // The disagreement, as a COUNT only. The full orphan list with its
            // acknowledge action stays on ?view=purchases - this surface says
            // "there are N to look at" and sends the operator there.
            const salesDisagreement = await probe('sales_disagreement', () => sql`
                SELECT COUNT(*)::bigint AS client_events_without_entitlement
                FROM analytics_events e
                WHERE e.event_name = 'purchase_completed'
                  AND e.received_at > NOW() - (${days} * INTERVAL '1 day')
                  AND NOT EXISTS (
                      SELECT 1 FROM purchase_entitlements p
                      WHERE p.tx_signature = e.properties->>'txSig')
                  AND NOT EXISTS (
                      SELECT 1 FROM analytics_events a
                      WHERE a.event_name = 'admin_ops_write'
                        AND a.properties->>'action' = 'purchase.alert_acknowledge'
                        AND a.properties->>'target' = e.properties->>'txSig'
                        AND a.properties->>'outcome' = 'acknowledged_no_action')
                LIMIT 1`);

            // Every sellable SKU, including the ones that have sold NOTHING. A
            // ranked list built only from sales rows cannot show a dud, and a
            // dud is exactly what the owner needs to see before deciding what to
            // push.
            const soldBySku = {};
            for (const r of (salesBySku || [])) soldBySku[String(r.sku)] = r;
            const skuRoster = Object.keys(USD_ANCHORS).sort().map((sku) => {
                const r = soldBySku[sku] || {};
                return {
                    sku: sku,
                    usd_price: Number(USD_ANCHORS[sku]),
                    units_all: Number(r.units_all || 0),
                    units_window: Number(r.units_window || 0),
                    buyers_all: Number(r.buyers_all || 0),
                    usd_all: Number(r.usd_all || 0),
                    usd_window: Number(r.usd_window || 0),
                    last_settled_at: r.last_settled_at || null,
                    state: Number(r.units_all || 0) > 0 ? 'SELLING' : 'NEVER SOLD',
                };
            }).sort((a, b) => (b.usd_all - a.usd_all) || (b.units_all - a.units_all)
                            || a.sku.localeCompare(b.sku));

            const st = (salesTotals && salesTotals[0]) || {};
            const sq = (salesQuotes && salesQuotes[0]) || {};
            const sr = (salesRepeat && salesRepeat[0]) || {};
            const salesReadOk = salesTotals !== null;
            const settledAllTime = Number(st.all_settled || 0);

            const salesWindow = (label, settled, buyers, usd, priorSettled, priorUsd) => ({
                window: label,
                settled: Number(settled || 0),
                buyers: Number(buyers || 0),
                usd: Number(usd || 0),
                prior_settled: Number(priorSettled || 0),
                prior_usd: Number(priorUsd || 0),
                trend: trendWord(usd, priorUsd),
            });

            const sales = {
                state: !salesReadOk ? 'error' : (settledAllTime === 0 ? 'empty' : 'ok'),
                read_ok: salesReadOk,
                backing: 'purchase_entitlements (settled, server-verified on chain) + '
                    + 'purchase_quotes (issue/consume funnel). The client purchase_completed '
                    + 'event is NEVER a source for value or units here.',
                authority: 'server',
                empty_meaning: settledAllTime === 0
                    ? 'No purchase has EVER settled on this deployment. The app is published on the '
                      + 'Solana dApp Store but no payment has completed, so an empty sales area is the '
                      + 'correct reading of the data - not a broken query. The quote funnel below is '
                      + 'what tells you whether anyone is TRYING.'
                    : null,
                all_time: {
                    settled: settledAllTime,
                    buyers: Number(st.all_buyers || 0),
                    usd: Number(st.all_usd || 0),
                    rows_without_usd_anchor: Number(st.rows_without_usd_anchor || 0),
                    first_settled_at: st.first_settled_at || null,
                    last_settled_at: st.last_settled_at || null,
                },
                windows: [
                    salesWindow('Today', st.d1_settled, st.d1_buyers, st.d1_usd, st.d1_prior_settled, st.d1_prior_usd),
                    salesWindow('7 days', st.d7_settled, st.d7_buyers, st.d7_usd, st.d7_prior_settled, st.d7_prior_usd),
                    salesWindow('30 days', st.d30_settled, st.d30_buyers, st.d30_usd, st.d30_prior_settled, st.d30_prior_usd),
                ],
                first_vs_repeat: {
                    read_ok: salesRepeat !== null,
                    first_time_window: Number(sr.first_time_window || 0),
                    repeat_window: Number(sr.repeat_window || 0),
                    repeat_buyers_all_time: Number(sr.repeat_buyers_all_time || 0),
                },
                quote_funnel: {
                    read_ok: salesQuotes !== null,
                    issued: Number(sq.issued || 0),
                    consumed: Number(sq.consumed || 0),
                    expired_unconsumed: Number(sq.expired_unconsumed || 0),
                    quoted_wallets: Number(sq.wallets_quoted || 0),
                    consumed_pct: pct(sq.consumed, sq.issued),
                    low_n: Number(sq.issued || 0) < LOW_N_THRESHOLD,
                    last_issued_at: sq.last_issued_at || null,
                    definition: 'A quote is issued when the wallet prompt opens and consumed when the '
                        + 'payment verifies (5-minute TTL). Issued with none consumed means players are '
                        + 'TRYING TO BUY AND FAILING.',
                },
                disagreement_count: salesDisagreement === null
                    ? null
                    : Number((salesDisagreement[0] || {}).client_events_without_entitlement || 0),
                sku_roster: skuRoster,
                sku_roster_note: 'Every sellable SKU on the server price ladder (_lib/purchase-catalog '
                    + 'USD_ANCHORS), including those that have never sold. A SKU missing from a sales '
                    + 'table and a SKU that does not exist look identical; this list keeps them apart.',
                push_a_sku: {
                    state: 'not_instrumented',
                    supported: false,
                    reason: 'There is NO server-side switch that changes what the store shows. The shelf '
                        + 'flag the client honours is storeVisible inside the PACKAGED packs.json '
                        + '(PackCatalog reads it from Resources/StreamingAssets, never over the '
                        + 'network), so today a SKU is pushed by shipping a build. The packs table in '
                        + 'Neon does carry a store_visible column, but nothing in api/ or in the client '
                        + 'reads it - flipping it here would change nothing a player sees. '
                        + 'catalog_collections is a live remote read, but it feeds the BUILD browser, '
                        + 'not the shop.',
                    needed: 'One shop-context remote read the client consults for shelf membership and '
                        + 'ordering, plus a client release that consumes it, plus an audited write '
                        + 'action on api/admin/ops.js behind the second key. Until all three exist, a '
                        + '"push SKU" button would be a control that silently does nothing - which is '
                        + 'worse than not having one.',
                },
            };

            // ------------------------------------------------------ RETENTION
            // Cohort = the day a player's FIRST QUALIFYING PLAY landed, never
            // their first boot. See QUALIFYING_PLAY_EVENTS above.
            const retention = await probe('retention_rollup', () => sql`
                WITH q AS (
                    SELECT player_id, received_at,
                           date_trunc('day', received_at)::date AS day
                    FROM analytics_events
                    WHERE event_name = ANY(${QUALIFYING_PLAY_EVENTS}::text[])
                      AND NOT (player_id = ANY(${EXCLUDED}::text[]))
                ),
                firsts AS (
                    SELECT player_id, MIN(received_at) AS first_play
                    FROM q GROUP BY 1
                ),
                cohort AS (
                    SELECT f.player_id,
                           date_trunc('day', f.first_play)::date AS cohort_day
                    FROM firsts f
                    WHERE f.first_play > NOW() - (${days} * INTERVAL '1 day')
                ),
                days_played AS (
                    SELECT DISTINCT player_id, day FROM q
                )
                SELECT
                    COUNT(*)::bigint AS cohort_players,
                    COUNT(*) FILTER (WHERE c.cohort_day <= CURRENT_DATE - 1)::bigint  AS d1_cohort,
                    COUNT(*) FILTER (WHERE c.cohort_day <= CURRENT_DATE - 1 AND EXISTS (
                        SELECT 1 FROM days_played d
                        WHERE d.player_id = c.player_id AND d.day = c.cohort_day + 1))::bigint  AS d1_returned,
                    COUNT(*) FILTER (WHERE c.cohort_day <= CURRENT_DATE - 7)::bigint  AS d7_cohort,
                    COUNT(*) FILTER (WHERE c.cohort_day <= CURRENT_DATE - 7 AND EXISTS (
                        SELECT 1 FROM days_played d
                        WHERE d.player_id = c.player_id AND d.day = c.cohort_day + 7))::bigint  AS d7_returned,
                    COUNT(*) FILTER (WHERE c.cohort_day <= CURRENT_DATE - 30)::bigint AS d30_cohort,
                    COUNT(*) FILTER (WHERE c.cohort_day <= CURRENT_DATE - 30 AND EXISTS (
                        SELECT 1 FROM days_played d
                        WHERE d.player_id = c.player_id AND d.day = c.cohort_day + 30))::bigint AS d30_returned
                FROM cohort c
                LIMIT 1`);

            // Growth: new players and active players, this window against the
            // one immediately before it. This is the "are we growing or losing
            // players" question, answered in two counts and a word.
            const growth = await probe('growth', () => sql`
                WITH q AS (
                    SELECT player_id, received_at
                    FROM analytics_events
                    WHERE event_name = ANY(${QUALIFYING_PLAY_EVENTS}::text[])
                      AND NOT (player_id = ANY(${EXCLUDED}::text[]))
                ),
                firsts AS (
                    SELECT player_id, MIN(received_at) AS first_play FROM q GROUP BY 1
                )
                SELECT
                    (SELECT COUNT(*) FROM firsts
                      WHERE first_play > NOW() - (${days} * INTERVAL '1 day'))::bigint AS new_window,
                    (SELECT COUNT(*) FROM firsts
                      WHERE first_play <= NOW() - (${days} * INTERVAL '1 day')
                        AND first_play >  NOW() - (${days} * INTERVAL '2 days'))::bigint AS new_prior,
                    (SELECT COUNT(DISTINCT player_id) FROM q
                      WHERE received_at > NOW() - (${days} * INTERVAL '1 day'))::bigint AS active_window,
                    (SELECT COUNT(DISTINCT player_id) FROM q
                      WHERE received_at <= NOW() - (${days} * INTERVAL '1 day')
                        AND received_at >  NOW() - (${days} * INTERVAL '2 days'))::bigint AS active_prior,
                    (SELECT COUNT(DISTINCT q.player_id) FROM q JOIN firsts f ON f.player_id = q.player_id
                      WHERE q.received_at > NOW() - (${days} * INTERVAL '1 day')
                        AND f.first_play  > NOW() - (${days} * INTERVAL '1 day'))::bigint AS new_active,
                    (SELECT COUNT(DISTINCT q.player_id) FROM q JOIN firsts f ON f.player_id = q.player_id
                      WHERE q.received_at > NOW() - (${days} * INTERVAL '1 day')
                        AND f.first_play <= NOW() - (${days} * INTERVAL '1 day'))::bigint AS returning_active
                LIMIT 1`);

            const newPerDay = await probe('new_players_per_day', () => sql`
                WITH q AS (
                    SELECT player_id, received_at
                    FROM analytics_events
                    WHERE event_name = ANY(${QUALIFYING_PLAY_EVENTS}::text[])
                      AND NOT (player_id = ANY(${EXCLUDED}::text[]))
                ),
                firsts AS (
                    SELECT player_id, MIN(received_at) AS first_play FROM q GROUP BY 1
                )
                SELECT date_trunc('day', first_play)::date::text AS day,
                       COUNT(*)::bigint                          AS new_players
                FROM firsts
                WHERE first_play > NOW() - (${days} * INTERVAL '1 day')
                GROUP BY 1
                ORDER BY 1 DESC
                LIMIT 181`);

            // ------------------------------------------- AVERAGE ONLINE TIME
            // ⚠ THIS BLOCK IS THE GAP-BASED ESTIMATE, AND IT IS NO LONGER THE ONLY
            // ANSWER. It is kept, unchanged in meaning, ON PURPOSE.
            //
            // WO-1842 (2026-09-17) added real instrumentation: EventTracker.cs now
            // emits session_heartbeat + session_end carrying a cumulative
            // FOREGROUND elapsedSeconds, and ?view=playtime reports the measured
            // durations and the owner's buckets from them. The paragraph that used
            // to stand here - "SESSION LENGTH IS NOT INSTRUMENTED", "there is NO
            // session_end anywhere in Assets/" - was true when it was written and
            // is now false; it is corrected in the same change that falsified it
            // (CLAUDE.md §15) rather than left to be read as current.
            //
            // The two figures measure DIFFERENT things and neither replaces the
            // other, so the card carries both and says which is which:
            //   this block     span between a player's ACTS. Idling with the game
            //                  open does not count, because nothing is emitted.
            //   ?view=playtime FOREGROUND time. Idling with the game open DOES
            //                  count, because the app was in front of the player.
            // ⛔ The heartbeat/end rows are EXCLUDED from the scan below. Leaving
            // them in would have converted this estimate into the other one while
            // keeping this label - the same number twice, one of them mislabelled.
            //
            // What this block derives is the SPAN BETWEEN A PLAYER'S TELEMETRY EVENTS,
            // cut wherever they went quiet for SESSION_GAP_MINUTES. That is an
            // ESTIMATE and it is labelled one. Crucially it is the estimate that
            // does NOT count a backgrounded phone as engagement: a locked device
            // emits nothing, so the gap closes the session. A duration derived
            // from "session_start until the app died" would have counted the
            // 10863-second pause-menu hold this project logged on 2026-08-30 as
            // three hours of play.
            //
            // MEDIAN IS REPORTED ALONGSIDE MEAN, and the mean is the second
            // figure on the card, because one long tail session drags a mean and
            // leaves a median alone.
            //
            // A session with ONE event has a span of zero and is UNMEASURABLE,
            // not a zero-second session. Those are counted separately and kept
            // out of both statistics.
            const sessionLength = await probe('session_length_estimate', () => sql`
                WITH ev AS (
                    SELECT player_id, received_at
                    FROM analytics_events
                    WHERE NOT (player_id = ANY(${EXCLUDED}::text[]))
                      AND NOT (event_name = ANY(${SESSION_DURATION_EVENTS}::text[]))
                      AND received_at > NOW() - (${days} * INTERVAL '1 day')
                    ORDER BY player_id, received_at
                    LIMIT ${SESSION_SCAN_CAP}
                ),
                marked AS (
                    SELECT player_id, received_at,
                           CASE WHEN LAG(received_at) OVER w IS NULL
                                  OR received_at - LAG(received_at) OVER w
                                     > (${SESSION_GAP_MINUTES} * INTERVAL '1 minute')
                                THEN 1 ELSE 0 END AS starts_session
                    FROM ev
                    WINDOW w AS (PARTITION BY player_id ORDER BY received_at)
                ),
                numbered AS (
                    SELECT player_id, received_at,
                           SUM(starts_session) OVER (PARTITION BY player_id ORDER BY received_at
                               ROWS BETWEEN UNBOUNDED PRECEDING AND CURRENT ROW) AS session_no
                    FROM marked
                ),
                spans AS (
                    SELECT player_id, session_no,
                           COUNT(*)::int AS events,
                           EXTRACT(EPOCH FROM (MAX(received_at) - MIN(received_at)))::float8 AS span_seconds
                    FROM numbered
                    GROUP BY 1, 2
                )
                SELECT COUNT(*)::bigint                                        AS sessions,
                       COUNT(*) FILTER (WHERE events < 2)::bigint              AS unmeasurable_sessions,
                       COUNT(DISTINCT player_id)::bigint                       AS players,
                       (SELECT COUNT(*) FROM ev)::bigint                       AS events_scanned,
                       COALESCE(AVG(CASE WHEN events >= 2 THEN span_seconds END), 0)::float8 AS mean_seconds,
                       COALESCE(percentile_cont(0.5) WITHIN GROUP (
                           ORDER BY CASE WHEN events >= 2 THEN span_seconds END), 0)::float8 AS median_seconds,
                       COALESCE(percentile_cont(0.9) WITHIN GROUP (
                           ORDER BY CASE WHEN events >= 2 THEN span_seconds END), 0)::float8 AS p90_seconds
                FROM spans
                LIMIT 1`);

            // --------------------------------------------- ONE-AND-DONE / CHURN
            // ⛔ NONE OF THESE SAY "DELETED". Android/Solana/Pi give us no
            // per-player uninstall fact, so these are inactivity cohorts and are
            // named as such (WO-1281 acceptance 7).
            const churn = await probe('churn_cohorts', () => sql`
                WITH q AS (
                    SELECT player_id, received_at, date_trunc('day', received_at)::date AS day
                    FROM analytics_events
                    WHERE event_name = ANY(${QUALIFYING_PLAY_EVENTS}::text[])
                      AND NOT (player_id = ANY(${EXCLUDED}::text[]))
                ),
                firsts AS (
                    SELECT player_id,
                           MIN(received_at) AS first_play,
                           MAX(received_at) AS last_play,
                           COUNT(DISTINCT day)::int AS play_days
                    FROM q GROUP BY 1
                ),
                milestones AS (
                    SELECT DISTINCT player_id
                    FROM analytics_events
                    WHERE event_name = ANY(${MILESTONE_EVENTS}::text[])
                      AND NOT (player_id = ANY(${EXCLUDED}::text[]))
                )
                SELECT
                    COUNT(*)::bigint                                                              AS players_who_played,
                    COUNT(*) FILTER (WHERE f.first_play <= NOW() - INTERVAL '1 day')::bigint      AS one_session_eligible,
                    COUNT(*) FILTER (WHERE f.first_play <= NOW() - INTERVAL '1 day'
                                       AND f.last_play < f.first_play + INTERVAL '1 day')::bigint AS one_session,
                    COUNT(*) FILTER (WHERE f.first_play <= NOW() - INTERVAL '7 days')::bigint     AS tried_and_left_eligible,
                    COUNT(*) FILTER (WHERE f.first_play <= NOW() - INTERVAL '7 days'
                                       AND f.last_play < f.first_play + INTERVAL '7 days')::bigint AS tried_and_left,
                    COUNT(*) FILTER (WHERE f.play_days >= 2)::bigint                              AS returned_players,
                    COUNT(*) FILTER (WHERE f.play_days >= 2 AND m.player_id IS NULL)::bigint       AS stalled_players
                FROM firsts f
                LEFT JOIN milestones m ON m.player_id = f.player_id
                LIMIT 1`);

            // Where they stopped. The LAST thing a now-quiet player did - the
            // step to fix, named rather than inferred.
            const exitSteps = await probe('early_exit_step', () => sql`
                WITH last_act AS (
                    SELECT DISTINCT ON (player_id)
                           player_id, event_name, properties->>'stepId' AS step_id, received_at
                    FROM analytics_events
                    WHERE NOT (player_id = ANY(${EXCLUDED}::text[]))
                      AND event_name <> 'session_start'
                      -- WO-1842. session_end is now the LAST row of almost every
                      -- session, so without this every departing player's "last act"
                      -- would read session_end and this view would say nothing at all.
                      AND NOT (event_name = ANY(${SESSION_DURATION_EVENTS}::text[]))
                    ORDER BY player_id, received_at DESC
                )
                SELECT event_name,
                       step_id,
                       COUNT(*)::bigint AS players,
                       MAX(received_at) AS latest
                FROM last_act
                WHERE received_at < NOW() - INTERVAL '7 days'
                GROUP BY 1, 2
                ORDER BY 3 DESC
                LIMIT 25`);

            // ----------------------------------------------------- PROGRESSION
            // ⭐ SERVER-PERSISTED, NOT TELEMETRY. player_data.game_state is the
            // save the client actually uploads (SaveSchema v29 added heroLevel /
            // rather than an event we hope fired.
            //
            // ⚠ EVERY JSONB READ IS REGEX-GUARDED BEFORE ITS CAST. game_state is
            // client-authored; one malformed value in an unguarded ::numeric
            // fails the whole query, which is how a real metric turns into a
            // blank card. Same reason ?view=funnel sorts `order` in JS.
            //
            // ⚠ COVERAGE IS RETURNED WITH THE FIGURE. "Median hero level 4" over
            // 3 of 900 saves is not a fact about the playerbase, and the console
            // has to be able to say so.
            const progression = await probe('progression_saves', () => sql`
                WITH s AS (
                    SELECT player_id, updated_at,
                           CASE WHEN game_state->>'heroLevel' ~ '^[0-9]+(\\.[0-9]+)?$'
                                THEN (game_state->>'heroLevel')::numeric END AS hero_level,
                           CASE WHEN game_state->>'bestWave' ~ '^[0-9]+(\\.[0-9]+)?$'
                                THEN (game_state->>'bestWave')::numeric END AS best_wave,
                           CASE WHEN game_state->>'wavesCompleted' ~ '^[0-9]+(\\.[0-9]+)?$'
                                THEN (game_state->>'wavesCompleted')::numeric END AS waves_completed,
                           CASE WHEN jsonb_typeof(game_state->'baseLayout') = 'array'
                                THEN jsonb_array_length(game_state->'baseLayout') END AS structures
                    FROM player_data
                    WHERE NOT (player_id = ANY(${EXCLUDED}::text[]))
                )
                SELECT
                    COUNT(*)::bigint                                              AS saves_all,
                    COUNT(*) FILTER (WHERE updated_at > NOW() - (${days} * INTERVAL '1 day'))::bigint AS saves_active,
                    COUNT(*) FILTER (WHERE hero_level IS NOT NULL)::bigint        AS with_hero_level,
                    COUNT(*) FILTER (WHERE best_wave IS NOT NULL)::bigint         AS with_best_wave,
                    COUNT(*) FILTER (WHERE structures IS NOT NULL)::bigint        AS with_base_layout,
                    COUNT(*) FILTER (WHERE hero_level > 1)::bigint                AS above_level_1,
                    COUNT(*) FILTER (WHERE hero_level = 1)::bigint                AS still_level_1,
                    COUNT(*) FILTER (WHERE hero_level >= 2 AND hero_level <= 4)::bigint  AS level_2_4,
                    COUNT(*) FILTER (WHERE hero_level >= 5 AND hero_level <= 9)::bigint  AS level_5_9,
                    COUNT(*) FILTER (WHERE hero_level >= 10)::bigint              AS level_10_plus,
                    COALESCE(percentile_cont(0.5) WITHIN GROUP (ORDER BY hero_level), 0)::float8 AS median_hero_level,
                    COALESCE(MAX(hero_level), 0)::float8                          AS max_hero_level,
                    COALESCE(percentile_cont(0.5) WITHIN GROUP (ORDER BY best_wave), 0)::float8  AS median_best_wave,
                    COALESCE(MAX(best_wave), 0)::float8                           AS max_best_wave,
                    COALESCE(percentile_cont(0.5) WITHIN GROUP (ORDER BY structures), 0)::float8 AS median_structures,
                    COUNT(*) FILTER (WHERE structures > 0)::bigint                AS with_any_structure,
                    COUNT(*) FILTER (WHERE waves_completed > 0)::bigint            AS with_any_wave_cleared,
                    MAX(updated_at)                                                AS last_save_at
                FROM s
                LIMIT 1`);

            // Telemetry side of progression: waves actually cleared in the
            // window, by unique player. Labelled as event volume, never blended
            // with the persisted figures above.
            const waveActivity = await probe('wave_activity', () => sql`
                SELECT COUNT(*)::bigint                  AS wave_clear_events,
                       COUNT(DISTINCT player_id)::bigint AS players_clearing_waves,
                       MAX(received_at)                  AS latest
                FROM analytics_events
                WHERE event_name = 'wave_completed'
                  AND received_at > NOW() - (${days} * INTERVAL '1 day')
                  AND NOT (player_id = ANY(${EXCLUDED}::text[]))
                LIMIT 1`);

            // -------------------------------------------------- DIAGNOSTICS
            // The blind spot, measured. If anonymous volume dwarfs identified
            // volume then every player figure above describes a minority.
            const coverage = await probe('identity_coverage', () => sql`
                SELECT COUNT(*) FILTER (WHERE player_id = ${ANON_ID})::bigint  AS anonymous_events,
                       COUNT(*) FILTER (WHERE player_id <> ${ANON_ID})::bigint AS identified_events,
                       COUNT(DISTINCT player_id) FILTER (WHERE player_id <> ${ANON_ID})::bigint AS identified_ids,
                       MIN(received_at)                                        AS first_event_at,
                       MAX(received_at)                                        AS last_event_at
                FROM analytics_events
                WHERE received_at > NOW() - (${days} * INTERVAL '1 day')
                LIMIT 1`);

            const eventInventory = await probe('event_inventory', () => sql`
                SELECT event_name,
                       COUNT(*)::bigint                  AS events,
                       COUNT(DISTINCT player_id)::bigint AS ids,
                       MAX(received_at)                  AS latest
                FROM analytics_events
                WHERE received_at > NOW() - (${days} * INTERVAL '1 day')
                GROUP BY 1
                ORDER BY 2 DESC
                LIMIT 60`);

            // ---- assemble ---------------------------------------------------
            const rt = (retention && retention[0]) || {};
            const gr = (growth && growth[0]) || {};
            const sl = (sessionLength && sessionLength[0]) || {};
            const ch = (churn && churn[0]) || {};
            const pr = (progression && progression[0]) || {};
            const wa = (waveActivity && waveActivity[0]) || {};
            const cv = (coverage && coverage[0]) || {};

            const dayN = (cohortKey, returnedKey) => {
                const cohortSize = Number(rt[cohortKey] || 0);
                const returned = Number(rt[returnedKey] || 0);
                return {
                    cohort_size: cohortSize,
                    returned: returned,
                    pct: pct(returned, cohortSize),
                    low_n: cohortSize < LOW_N_THRESHOLD,
                    mature_note: cohortSize === 0
                        ? 'No cohort in this window is old enough to have had the chance to return.'
                        : null,
                };
            };

            const sessionsMeasured = Number(sl.sessions || 0) - Number(sl.unmeasurable_sessions || 0);
            const savesWithLevel = Number(pr.with_hero_level || 0);
            const savesAll = Number(pr.saves_all || 0);

            return res.status(200).json(Object.assign(meta, {
                surface: 'command',
                purpose: 'The five questions the landing view exists to answer: what is selling, do '
                    + 'players return, are they progressing, are they playing once and leaving, and '
                    + 'how long is a session.',
                identity_rule: 'A player is one non-excluded player_id. "anonymous" is a single shared '
                    + 'bucket (EventTracker.cs) and is never counted as a person.',
                exclusions: {
                    note: 'Operator and test traffic is removed server-side from every player metric. '
                        + 'The rule lives in the deployment environment, not in the request, so a '
                        + 'caller cannot widen or narrow it. The COUNT is published; the ids are not.',
                    excluded_id_count: EXCLUDED.length,
                    source: 'ANALYTICS_EXCLUDED_PLAYER_IDS (env) plus the always-excluded "anonymous".',
                    configured: EXCLUDED.length > 1,
                },
                qualifying_play: {
                    note: 'Retention, growth and churn count only players who DID something. A boot is '
                        + 'not play.',
                    counts_as_play: QUALIFYING_PLAY_EVENTS,
                    does_not_count: NOT_PLAY_EVENTS,
                },

                sales: sales,

                retention: {
                    state: retention === null ? 'error'
                         : Number(rt.cohort_players || 0) === 0 ? 'empty' : 'ok',
                    read_ok: retention !== null,
                    backing: 'analytics_events, restricted to the qualifying-play allowlist. Cohort day '
                        + 'is the day of a player FIRST QUALIFYING PLAY, not their first boot.',
                    cohort_players: Number(rt.cohort_players || 0),
                    d1: dayN('d1_cohort', 'd1_returned'),
                    d7: dayN('d7_cohort', 'd7_returned'),
                    d30: dayN('d30_cohort', 'd30_returned'),
                    low_n_threshold: LOW_N_THRESHOLD,
                    growth: {
                        read_ok: growth !== null,
                        new_window: Number(gr.new_window || 0),
                        new_prior: Number(gr.new_prior || 0),
                        new_trend: trendWord(gr.new_window, gr.new_prior),
                        active_window: Number(gr.active_window || 0),
                        active_prior: Number(gr.active_prior || 0),
                        active_trend: trendWord(gr.active_window, gr.active_prior),
                        new_active: Number(gr.new_active || 0),
                        returning_active: Number(gr.returning_active || 0),
                        note: 'Each figure is set against the immediately preceding window of the same '
                            + 'length. The verdict is a WORD, never a colour or an arrow.',
                    },
                    new_players_per_day: newPerDay || [],
                    session_length: {
                        // The honesty this whole block exists for.
                        state: sessionLength === null ? 'error'
                             : sessionsMeasured <= 0 ? 'empty' : 'ok',
                        read_ok: sessionLength !== null,
                        instrumented: false,
                        estimated: true,
                        backing: 'analytics_events received_at, cut into sessions wherever a player went '
                            + 'quiet for ' + SESSION_GAP_MINUTES + ' minutes. Excludes the WO-1842 '
                            + 'session_heartbeat / session_end rows, which would otherwise turn this '
                            + 'estimate into the measured foreground figure under this label.',
                        // WO-1842. This field previously read "THEY DO NOT ... there is NO
                        // session_end anywhere in the client". That was true until
                        // 2026-09-17 and is now false; corrected here in the same change.
                        how_sessions_end: 'FOR THIS FIGURE, by a ' + SESSION_GAP_MINUTES + '-minute '
                            + 'silence - so this remains an ESTIMATE of the span between a player\'s '
                            + 'ACTS, not a measured session, and it deliberately does NOT count a '
                            + 'backgrounded phone as engagement (a locked device sends nothing, so the '
                            + 'gap ends the session). The client DOES now report real durations: '
                            + 'WO-1842 added session_heartbeat + session_end to EventTracker.cs. Those '
                            + 'are on ?view=playtime, which measures FOREGROUND time and therefore '
                            + 'counts idling with the game open. The two are complementary, not '
                            + 'rivals; neither is corrected into the other.',
                        measured_alternative: '?view=playtime',
                        median_seconds: Number(sl.median_seconds || 0),
                        mean_seconds: Number(sl.mean_seconds || 0),
                        p90_seconds: Number(sl.p90_seconds || 0),
                        sessions: Number(sl.sessions || 0),
                        sessions_measured: sessionsMeasured,
                        unmeasurable_sessions: Number(sl.unmeasurable_sessions || 0),
                        players: Number(sl.players || 0),
                        low_n: sessionsMeasured < LOW_N_THRESHOLD,
                        scan_truncated: Number(sl.events_scanned || 0) >= SESSION_SCAN_CAP,
                        scan_cap: SESSION_SCAN_CAP,
                        unmeasurable_note: 'A session carrying a single event has no span. It is counted '
                            + 'separately and kept OUT of both statistics - it is unmeasurable, not zero '
                            + 'seconds.',
                        median_first_note: 'Median is the headline and mean is the second figure: one '
                            + 'long tail session drags a mean and leaves a median alone.',
                    },
                },

                churn: {
                    state: churn === null ? 'error'
                         : Number(ch.players_who_played || 0) === 0 ? 'empty' : 'ok',
                    read_ok: churn !== null,
                    backing: 'analytics_events, qualifying-play allowlist only.',
                    never_claims_deletion: 'These are INACTIVITY cohorts. Android, Solana and Pi give us '
                        + 'no per-player uninstall fact, so nothing here says a player deleted the app - '
                        + 'only that they stopped appearing.',
                    players_who_played: Number(ch.players_who_played || 0),
                    one_session: {
                        players: Number(ch.one_session || 0),
                        eligible: Number(ch.one_session_eligible || 0),
                        pct: pct(ch.one_session, ch.one_session_eligible),
                        low_n: Number(ch.one_session_eligible || 0) < LOW_N_THRESHOLD,
                        definition: 'Played once and nothing in the 24 hours after. Only players whose '
                            + 'first play was more than 24 hours ago can be judged.',
                    },
                    tried_and_left: {
                        players: Number(ch.tried_and_left || 0),
                        eligible: Number(ch.tried_and_left_eligible || 0),
                        pct: pct(ch.tried_and_left, ch.tried_and_left_eligible),
                        low_n: Number(ch.tried_and_left_eligible || 0) < LOW_N_THRESHOLD,
                        definition: 'No qualifying play within seven days of their first. Only players '
                            + 'whose first play was more than seven days ago can be judged.',
                    },
                    stalled: {
                        players: Number(ch.stalled_players || 0),
                        returned_players: Number(ch.returned_players || 0),
                        pct: pct(ch.stalled_players, ch.returned_players),
                        low_n: Number(ch.returned_players || 0) < LOW_N_THRESHOLD,
                        definition: 'Came back on a second day but has never cleared a wave or finished '
                            + 'the tutorial.',
                        approximation: 'APPROXIMATE. The ticket asks for "gained no XP or level in the '
                            + 'window"; the database holds only a CURRENT hero level, never its history, '
                            + 'so no XP-gain-over-time figure exists. Finished-milestone absence is the '
                            + 'honest stand-in and is labelled as one.',
                    },
                    early_exit_steps: (exitSteps || []).map(r => ({
                        step: r.step_id || r.event_name,
                        event_name: r.event_name,
                        players: Number(r.players || 0),
                        latest: r.latest,
                    })),
                    early_exit_note: 'The LAST thing each now-quiet player did before going silent for '
                        + 'seven days. Boot is excluded, so this names an act, not an arrival.',
                },

                progression: {
                    state: progression === null ? 'error'
                         : savesWithLevel === 0 ? 'empty' : 'ok',
                    read_ok: progression !== null,
                    backing: 'player_data.game_state - the save the client uploads. heroLevel has been '
                        + 'persisted since SaveSchema v29, alongside bestWave, wavesCompleted and '
                        + 'baseLayout. Server-persisted state, not a telemetry estimate.',
                    coverage: {
                        saves_all: savesAll,
                        saves_active_in_window: Number(pr.saves_active || 0),
                        with_hero_level: savesWithLevel,
                        with_best_wave: Number(pr.with_best_wave || 0),
                        with_base_layout: Number(pr.with_base_layout || 0),
                        hero_level_pct: pct(savesWithLevel, savesAll),
                        note: 'Coverage travels WITH the figure. A median over three of nine hundred '
                            + 'saves is not a fact about the playerbase, and this is how the card knows '
                            + 'to say so.',
                        last_save_at: pr.last_save_at || null,
                    },
                    hero_level: {
                        median: Number(pr.median_hero_level || 0),
                        max: Number(pr.max_hero_level || 0),
                        still_level_1: Number(pr.still_level_1 || 0),
                        above_level_1: Number(pr.above_level_1 || 0),
                        levelled_pct: pct(pr.above_level_1, savesWithLevel),
                        distribution: [
                            { band: 'Level 1', players: Number(pr.still_level_1 || 0) },
                            { band: 'Level 2 to 4', players: Number(pr.level_2_4 || 0) },
                            { band: 'Level 5 to 9', players: Number(pr.level_5_9 || 0) },
                            { band: 'Level 10 and up', players: Number(pr.level_10_plus || 0) },
                        ],
                    },
                    waves: {
                        median_best_wave: Number(pr.median_best_wave || 0),
                        max_best_wave: Number(pr.max_best_wave || 0),
                        saves_with_a_wave_cleared: Number(pr.with_any_wave_cleared || 0),
                        clear_events_in_window: Number(wa.wave_clear_events || 0),
                        players_clearing_in_window: Number(wa.players_clearing_waves || 0),
                        latest_clear: wa.latest || null,
                        note: 'Best wave is persisted state. The window figures are EVENT VOLUME from '
                            + 'wave_completed and are labelled separately; the two are never added.',
                    },
                    building: {
                        median_structures: Number(pr.median_structures || 0),
                        saves_with_any_structure: Number(pr.with_any_structure || 0),
                        note: 'Structures placed = the length of baseLayout on the uploaded save.',
                    },
                    gaps: [
                        'XP GAINED OVER TIME: not answerable. player_data holds a CURRENT heroLevel and '
                        + 'has no source. It would need either a progression snapshot table or a '
                        + 'hero_level_up event, neither of which exists.',
                        'DUNGEON ENTRIES AND COMPLETIONS: not instrumented. The client emits no dungeon '
                        + 'event and dungeon_status is a per-dungeon seal setting, not per-player play.',
                        'STRUCTURE UPGRADES AND TOWER UPGRADES: not separable. baseLayout gives a count '
                        + 'of placements; no upgrade event is emitted and no per-level history is kept.',
                        'TIME TO FIRST BUILD / FIRST CLEAR / FIRST PURCHASE: only the tutorial leg is '
                        + 'answerable, from the tutorial_step timings on ?view=funnel. There is no '
                        + 'first-build or first-clear timestamp anywhere.',
                    ],
                },

                diagnostics: {
                    read_ok: coverage !== null,
                    backing: 'analytics_events volume. Supporting detail, never the product view.',
                    identified_events: Number(cv.identified_events || 0),
                    anonymous_events: Number(cv.anonymous_events || 0),
                    identified_ids: Number(cv.identified_ids || 0),
                    identified_coverage_pct: pct(
                        cv.identified_events,
                        Number(cv.identified_events || 0) + Number(cv.anonymous_events || 0)),
                    first_event_at: cv.first_event_at || null,
                    last_event_at: cv.last_event_at || null,
                    coverage_note: 'Every player with no bound wallet shares the single id "anonymous", '
                        + 'so anonymous volume can never be split into people. A large anonymous share '
                        + 'means this surface describes a MINORITY of the playerbase.',
                    events_by_name: eventInventory || [],
                    events_note: 'The sanity check that a metric reads zero because nobody did it, not '
                        + 'because the event never fires.',
                },

                errors: errors,
            }));
        }

        // ============================================================ playtime
        // WO-1842 - HOW LONG IS A SESSION, MEASURED. Owner ask, 2026-09-17:
        // playtime in buckets - "1-5 minutes, 5-30, 30-60, 60+".
        //
        // ⛔ THIS COULD NOT BE ANSWERED BY A QUERY ALONE, and the owner's opening
        // assumption was that it could. Until 2026-09-17 the client emitted
        // session_start on boot and NOTHING marked when a session stopped - this
        // file said so itself, a few hundred lines up. No amount of SQL over rows
        // with no end timestamp can produce a duration. So WO-1842 is mostly a
        // CLIENT change; this view is its reader.
        //
        // ── WHAT A DURATION IS HERE ──────────────────────────────────────────
        // FOREGROUND SECONDS: the app was in front of the player. Accumulated on
        // the device between resume and pause, never from wall-clock boot time,
        // because Android keeps the clock running on a phone in a pocket.
        // ⚠ It therefore INCLUDES idling with the game open. That is stated on the
        // response rather than silently corrected, and the gap-based estimate on
        // ?view=command is the complementary "between acts" reading. Two honest
        // numbers, each labelled.
        //
        // ── WHY MAX(elapsedSeconds), AND WHY NO JOIN TO session_start ────────
        // Every heartbeat and the end event carry the CUMULATIVE figure for their
        // sessionId, so MAX is the duration and arrival order is irrelevant. We do
        // NOT join session_start: that row is often attributed to 'anonymous'
        // (queued before the account id is minted, EventTracker.cs) while later
        // rows carry the real id, so a join would drop exactly the new players this
        // view exists to describe. sessionId is the key; player_id is reported
        // beside it, never used to assemble the session.
        //
        // ── THE HONEST DENOMINATOR ───────────────────────────────────────────
        // A session only appears here if a heartbeat or an end event reached the
        // server. So `coverage` sets measured sessions against the session_start
        // count in the same window, and `unmeasured_sessions` is printed. ⛔ THAT
        // NUMBER WILL BE ~100% OF THE WINDOW UNTIL A BUILD CARRYING WO-1842 HAS
        // BEEN IN PLAYERS' HANDS FOR THE WHOLE WINDOW - every session that predates
        // the client change is unmeasurable, and the response says so instead of
        // showing a bucket distribution assembled from nothing.
        if (view === 'playtime') {
            // Operator/test traffic exclusion, same server-side source as every other
            // view. Declared HERE because it is scoped per view block (the command
            // view has its own); reaching for that one would be a cross-block
            // reference that only throws at request time.
            const EXCLUDED = excludedPlayerIds();

            // Per-session durations, capped. The bucket EDGES live in ONE place
            // (PLAYTIME_BUCKETS) and are applied by bucketPlaytime() in JS, so
            // moving an edge for the owner is a one-line data change and cannot
            // drift between a SQL CASE and a doc.
            const sessionRows = await sql`
                WITH sig AS (
                    SELECT properties->>'sessionId'            AS session_id,
                           player_id,
                           event_name,
                           received_at,
                           properties->>'elapsedSeconds'       AS elapsed_raw
                    FROM analytics_events
                    WHERE event_name = ANY(${SESSION_DURATION_EVENTS}::text[])
                      AND NOT (player_id = ANY(${EXCLUDED}::text[]))
                      AND received_at > NOW() - (${days} * INTERVAL '1 day')
                      AND properties->>'sessionId' IS NOT NULL
                      -- ⛔ THE CAST GUARD. properties is JSONB written by a client we
                      -- do not control; one malformed elapsedSeconds would make
                      -- ::float8 throw and take the WHOLE view down, not one row.
                      -- Filtering on the shape first means a bad row is DROPPED and
                      -- counted (malformed_rows below), never fatal.
                      -- Double-backslash, not single. In a JS template literal a single
                      -- backslash-dot collapses to a bare dot, which in a POSIX regex
                      -- matches ANY character - so the guard would admit "12x34" and the
                      -- cast it exists to protect would throw anyway. Same spelling as the
                      -- one existing guard of this shape in the codebase, db.js:268.
                      AND properties->>'elapsedSeconds' ~ '^[0-9]+(\\.[0-9]+)?$'
                    -- ⚠ ORDER BEFORE THE LIMIT. A bare LIMIT drops ARBITRARY rows when
                    -- the cap bites, which would SPLIT a session across the boundary and
                    -- understate its MAX - a silently wrong duration is worse than a
                    -- declared truncation. Newest-first keeps whole recent sessions, and
                    -- scan_truncated says when the edge was reached.
                    ORDER BY received_at DESC
                    LIMIT ${PLAYTIME_SESSION_CAP}
                )
                SELECT session_id,
                       MIN(player_id)                                        AS player_id,
                       MAX(elapsed_raw::float8)::float8                      AS duration_seconds,
                       BOOL_OR(event_name = 'session_end')::boolean          AS ended_cleanly,
                       COUNT(*) FILTER (WHERE event_name = 'session_heartbeat')::int AS heartbeats,
                       MAX(received_at)                                      AS last_signal
                FROM sig
                GROUP BY session_id
                ORDER BY 6 DESC
                LIMIT ${PLAYTIME_SESSION_CAP}`;

            // Coverage + the malformed count, in one statement so the denominator
            // and the numerator describe the same window.
            const coverRows = await sql`
                SELECT
                    COUNT(*) FILTER (WHERE event_name = 'session_start')::bigint AS session_starts,
                    COUNT(*) FILTER (WHERE event_name = ANY(${SESSION_DURATION_EVENTS}::text[])
                        )::bigint                                                AS duration_rows,
                    COUNT(*) FILTER (WHERE event_name = ANY(${SESSION_DURATION_EVENTS}::text[])
                        AND (properties->>'sessionId' IS NULL
                          OR properties->>'elapsedSeconds' !~ '^[0-9]+(\\.[0-9]+)?$')
                        )::bigint                                                AS malformed_rows,
                    MIN(received_at) FILTER (WHERE event_name = ANY(${SESSION_DURATION_EVENTS}::text[])
                        )                                                        AS first_duration_signal
                FROM analytics_events
                WHERE NOT (player_id = ANY(${EXCLUDED}::text[]))
                  AND received_at > NOW() - (${days} * INTERVAL '1 day')
                LIMIT 1`;

            const cvg = (coverRows && coverRows[0]) || {};
            const allRows = (sessionRows || []).map(r => ({
                duration_seconds: Number(r.duration_seconds),
                ended_cleanly: r.ended_cleanly === true,
                heartbeats: Number(r.heartbeats || 0),
            }));
            // ⛔ DROPPED, NOT COERCED TO ZERO. A NaN or a negative duration is an
            // UNREADABLE ROW, not a zero-second session, and folding one into the
            // bounce band is the most misleading place it could land. Dropping here
            // rather than inside the bucketer is what makes "the bands sum to
            // sessions_measured" a fact this response can state: the denominator and
            // the bands are then computed from the same filtered array.
            const sessions = allRows.filter(
                s => Number.isFinite(s.duration_seconds) && s.duration_seconds >= 0);
            const unreadableRows = allRows.length - sessions.length;
            const durations = sessions.map(s => s.duration_seconds);

            // Median FIRST and mean second, for the reason the command view already
            // states out loud: one long tail session drags a mean and leaves a
            // median alone. Computed over the same array the buckets are - one
            // population, so the headline and the distribution can never disagree.
            const sorted = durations.slice().sort((a, b) => a - b);
            const quantile = (p) => {
                if (sorted.length === 0) return 0;
                const idx = (sorted.length - 1) * p;
                const lo = Math.floor(idx), hi = Math.ceil(idx);
                if (lo === hi) return sorted[lo];
                return sorted[lo] + (sorted[hi] - sorted[lo]) * (idx - lo);
            };
            const measured = sorted.length;
            const starts = Number(cvg.session_starts || 0);
            const truncated = Number(cvg.duration_rows || 0) >= PLAYTIME_SESSION_CAP;

            return res.status(200).json(Object.assign(meta, {
                state: measured === 0 ? 'empty' : 'ok',
                instrumented: true,
                estimated: false,
                backing: 'analytics_events session_heartbeat + session_end, grouped by '
                    + "properties->>'sessionId', duration = MAX(elapsedSeconds). Emitted by "
                    + 'Assets/_Modules/Core/Analytics/EventTracker.cs (WO-1842).',
                definition: 'FOREGROUND SECONDS - the app was in front of the player, accumulated on '
                    + 'the device between resume and pause. It INCLUDES idling with the game open, '
                    + 'and it EXCLUDES time backgrounded. A stretch backgrounded for longer than '
                    + SESSION_GAP_MINUTES + ' minutes starts a NEW session on the client, matching '
                    + 'the gap rule the estimate on ?view=command uses.',
                floor_note: 'A session that dies without a pause or quit callback (a crash, an OS '
                    + 'kill) reports its LAST HEARTBEAT, so its duration is a FLOOR, not an exact '
                    + 'figure - understated by up to the heartbeat interval. ended_cleanly_pct says '
                    + 'what share of the sample is exact.',
                delivery_note: 'A session_end raised at pause is persisted to PlayerPrefs and is '
                    + 'often delivered on the NEXT launch, so a very recent window can under-report '
                    + 'and then fill in. Each event carries its own clientTs.',

                median_seconds: Math.round(quantile(0.5) * 10) / 10,
                median_minutes: Math.round(quantile(0.5) / 6) / 10,
                mean_seconds: measured === 0 ? 0
                    : Math.round((durations.reduce((a, b) => a + b, 0) / measured) * 10) / 10,
                p90_seconds: Math.round(quantile(0.9) * 10) / 10,
                longest_seconds: measured === 0 ? 0 : sorted[measured - 1],

                buckets: bucketPlaytime(durations),
                bucket_note: 'The owner\'s ask was "1-5, 5-30, 30-60, 60+ minutes". The '
                    + '"Under 1 minute" band is ADDED, not in the ask: a bounce is the most '
                    + 'important number on a retention surface and dropping it would shrink the '
                    + 'denominator until every other band read high. Edges are a FIRST PASS - they '
                    + 'live in PLAYTIME_BUCKETS in api/admin/stats.js and are one data change to move.',
                bands_tile: 'The bands cover [0, infinity) with no gap and no overlap, so the '
                    + 'bucket sessions always sum to sessions_measured.',

                sessions_measured: measured,
                low_n: measured < LOW_N_THRESHOLD,
                low_n_threshold: LOW_N_THRESHOLD,
                ended_cleanly: sessions.filter(s => s.ended_cleanly).length,
                ended_cleanly_pct: pct(sessions.filter(s => s.ended_cleanly).length, measured),

                coverage: {
                    session_starts_in_window: starts,
                    sessions_measured: measured,
                    unmeasured_sessions: Math.max(0, starts - measured),
                    measured_pct: pct(measured, starts),
                    duration_rows: Number(cvg.duration_rows || 0),
                    malformed_rows: Number(cvg.malformed_rows || 0),
                    unreadable_sessions_dropped: unreadableRows,
                    first_duration_signal: cvg.first_duration_signal || null,
                    scan_truncated: truncated,
                    scan_cap: PLAYTIME_SESSION_CAP,
                    // ⚠ measured_pct CAN EXCEED 100%, legitimately. A background stretch
                    // longer than the gap mints a NEW sessionId on the client without a
                    // second session_start (see the client note above), so one boot can
                    // produce several measured sessions. Said here so that a >100% reading
                    // is understood rather than raised as a defect.
                    over_100_pct_is_possible: 'A boot can yield SEVERAL measured sessions: a '
                        + 'resume after more than ' + SESSION_GAP_MINUTES + ' minutes '
                        + 'backgrounded starts a new session on the client, and deliberately '
                        + 'does not emit a second session_start. So measured_pct above 100 is a '
                        + 'valid state, not a bug.',
                    note: 'Coverage travels WITH the figure. ⛔ EXPECT unmeasured_sessions to be '
                        + 'nearly the whole window until a build carrying WO-1842 has been in '
                        + 'players\' hands for the full window: every session that predates the '
                        + 'client change emitted no end signal and can never be measured '
                        + 'retroactively. A low measured_pct here means this describes a MINORITY '
                        + 'of sessions, not that sessions got shorter.',
                },

                gaps: [
                    'A SESSION KILLED BY THE OS MID-PLAY reports its last heartbeat, so it is a '
                    + 'floor. Shortening the heartbeat interval tightens the floor and costs rows; '
                    + 'it is HeartbeatIntervalSeconds in EventTracker.cs.',
                    'ACTIVE vs OPEN is not separated here. This is foreground time, so a player who '
                    + 'left the game on the title screen counts. The between-acts estimate on '
                    + '?view=command is the other half of that question.',
                    'PRE-WO-1842 SESSIONS ARE PERMANENTLY UNMEASURABLE. No backfill is possible - '
                    + 'the end timestamps were never recorded. Anything claiming a bucket '
                    + 'distribution for a window before the client shipped is fabricated.',
                ],
            }));
        }

        // ============================================================== active
        // WO-1843 #1 — DAU / WAU / MAU AND STICKINESS, the one thing a live-ops
        // dashboard is expected to open on and the one thing this page could not
        // show. `active_players` existed only as a column inside the overview's
        // per-day table; nobody could read "how many people played this week".
        //
        // ⛔ THE MEASURE IS "OPENED THE GAME", NOT WO-1281 "PLAYING". A distinct
        // player_id that fired session_start. That is the industry definition of
        // DAU and it is deliberately NOT the QUALIFYING_PLAY_EVENTS definition the
        // ?view=command card uses — those two numbers are different on purpose and
        // blending them would make both useless. Both definitions are printed.
        //
        // ⚠ THE 1/7/30 WINDOWS ARE TRAILING AND FIXED, never clipped by ?days —
        // the same rule the overview block states: a "last 7 days" selection must
        // not silently render MAU as a 7-day number under a 30-day label. ?days
        // drives the per-day series and the build table only.
        //
        // THE SLICE HERE IS A ROW FILTER, NOT A PLAYER FILTER, AND THAT IS MORE
        // PRECISE than anywhere else in this file: session_start is the very row
        // that CARRIES appVersion/platform, so no per-player join and no
        // multi-build ambiguity is involved.
        if (view === 'active') {
            const EXCLUDED = excludedPlayerIds();

            const windows = await sql`
                SELECT
                    COUNT(DISTINCT player_id) FILTER (WHERE received_at > NOW() - INTERVAL '1 day')::bigint   AS dau,
                    COUNT(DISTINCT player_id) FILTER (WHERE received_at > NOW() - INTERVAL '7 days')::bigint  AS wau,
                    COUNT(DISTINCT player_id) FILTER (WHERE received_at > NOW() - INTERVAL '30 days')::bigint AS mau,
                    COUNT(*) FILTER (WHERE received_at > NOW() - INTERVAL '1 day')::bigint   AS sessions_today,
                    COUNT(*) FILTER (WHERE received_at > NOW() - INTERVAL '7 days')::bigint  AS sessions_7d,
                    COUNT(*) FILTER (WHERE received_at > NOW() - INTERVAL '30 days')::bigint AS sessions_30d,
                    MAX(received_at) AS latest_session_start
                FROM analytics_events
                WHERE event_name = 'session_start'
                  AND NOT (player_id = ANY(${EXCLUDED}::text[]))
                  AND received_at > NOW() - INTERVAL '30 days'
                  AND (${slice.app_version}::text IS NULL OR properties->>'appVersion' = ${slice.app_version})
                  AND (${slice.platform}::text IS NULL OR properties->>'platform' = ${slice.platform})
                LIMIT 1`;

            // Week over week, so stickiness has a DIRECTION and not just a level.
            // Reported through trendWord — a word, never a colour (CLAUDE.md section 7).
            const trend = await sql`
                SELECT
                    COUNT(DISTINCT player_id) FILTER (WHERE received_at > NOW() - INTERVAL '7 days')::bigint AS wau_now,
                    COUNT(DISTINCT player_id) FILTER (WHERE received_at <= NOW() - INTERVAL '7 days'
                          AND received_at > NOW() - INTERVAL '14 days')::bigint AS wau_prior,
                    COUNT(DISTINCT player_id) FILTER (WHERE received_at > NOW() - INTERVAL '1 day')::bigint AS dau_now,
                    COUNT(DISTINCT player_id) FILTER (WHERE received_at <= NOW() - INTERVAL '1 day'
                          AND received_at > NOW() - INTERVAL '2 days')::bigint AS dau_prior
                FROM analytics_events
                WHERE event_name = 'session_start'
                  AND NOT (player_id = ANY(${EXCLUDED}::text[]))
                  AND received_at > NOW() - INTERVAL '14 days'
                  AND (${slice.app_version}::text IS NULL OR properties->>'appVersion' = ${slice.app_version})
                  AND (${slice.platform}::text IS NULL OR properties->>'platform' = ${slice.platform})
                LIMIT 1`;

            const perDay = await sql`
                SELECT date_trunc('day', received_at)::date::text AS day,
                       COUNT(DISTINCT player_id)::bigint          AS dau,
                       COUNT(*)::bigint                           AS sessions
                FROM analytics_events
                WHERE event_name = 'session_start'
                  AND NOT (player_id = ANY(${EXCLUDED}::text[]))
                  AND received_at > NOW() - (${days} * INTERVAL '1 day')
                  AND (${slice.app_version}::text IS NULL OR properties->>'appVersion' = ${slice.app_version})
                  AND (${slice.platform}::text IS NULL OR properties->>'platform' = ${slice.platform})
                GROUP BY 1
                ORDER BY 1 DESC
                LIMIT 181`;

            // ⭐ THE VALUE SET FOR EVERY OTHER VIEW'S SLICE. An operator guessing an
            // app_version filters to an empty cohort and reads it as a dead build, so
            // the live list of builds and platforms is published HERE and pointed at
            // from every slice block. Never sliced itself — it is the menu.
            const byBuild = await sql`
                SELECT properties->>'appVersion' AS app_version,
                       properties->>'platform'   AS platform,
                       COUNT(DISTINCT player_id)::bigint AS players,
                       COUNT(*)::bigint                  AS sessions,
                       MIN(received_at)                  AS first_seen,
                       MAX(received_at)                  AS latest
                FROM analytics_events
                WHERE event_name = 'session_start'
                  AND NOT (player_id = ANY(${EXCLUDED}::text[]))
                  AND received_at > NOW() - (${days} * INTERVAL '1 day')
                GROUP BY 1, 2
                ORDER BY 3 DESC
                LIMIT 60`;

            const w = windows[0] || {};
            const tr = trend[0] || {};
            const dau = Number(w.dau || 0);
            const wau = Number(w.wau || 0);
            const mau = Number(w.mau || 0);
            // user-days: the ARPDAU denominator, summed here so ?view=monetization
            // and this view can never disagree about it.
            const userDays = perDay.reduce((s, r) => s + Number(r.dau || 0), 0);

            return res.status(200).json(Object.assign(meta, {
                definition: 'DAU / WAU / MAU = DISTINCT player_id that fired session_start inside a '
                    + 'TRAILING 1 / 7 / 30-day window. session_start is one row per app boot '
                    + '(EventTracker.cs Start), so this measures OPENED THE GAME. This is the SAME '
                    + 'measure as ?view=overview -> active, which stays the one authority for the tile; '
                    + 'the only difference is that overview excludes "anonymous" alone while this view '
                    + 'also excludes operator/test ids, so the two differ by the excluded-id count '
                    + 'whenever ANALYTICS_EXCLUDED_PLAYER_IDS is set.',
                not_this: 'This is NOT the WO-1281 "playing" definition (?view=command), which requires '
                    + 'the player to have DONE something (wave cleared, tutorial step completed, '
                    + 'purchase, ad). That number is smaller and the two are never blended.',
                window_note: 'The 1/7/30 tiles are FIXED trailing windows and ignore ?days on purpose — '
                    + 'otherwise a 7-day selection would render MAU as a 7-day number. ?days drives '
                    + 'per_day and by_build only.',
                low_n_threshold: LOW_N_THRESHOLD,
                dau: dau,
                wau: wau,
                mau: mau,
                sessions: {
                    today: Number(w.sessions_today || 0),
                    d7: Number(w.sessions_7d || 0),
                    d30: Number(w.sessions_30d || 0),
                    sessions_per_dau: dau > 0 ? Math.round((Number(w.sessions_today || 0) / dau) * 100) / 100 : null,
                    latest_session_start: w.latest_session_start || null,
                },
                stickiness: {
                    definition: 'DAU/MAU is the headline stickiness ratio — the share of the monthly '
                        + 'playerbase present on an average day. WAU/MAU is reported beside it because '
                        + 'a session-a-week game reads healthy on one and thin on the other.',
                    dau_over_mau_pct: pct(dau, mau),
                    wau_over_mau_pct: pct(wau, mau),
                    dau_over_wau_pct: pct(dau, wau),
                    // ⛔ The flag is on MAU because MAU is the DENOMINATOR. A ratio over a
                    // handful of monthly players is noise whatever the numerator looks like.
                    low_n: mau < LOW_N_THRESHOLD,
                    low_n_reason: mau < LOW_N_THRESHOLD
                        ? 'MAU (' + mau + ') is below the ' + LOW_N_THRESHOLD + '-player threshold — '
                          + 'these ratios are not reportable, read the raw counts instead.'
                        : null,
                },
                trend: {
                    note: 'Stated as a WORD, never a colour or an arrow. A base too small to carry '
                        + 'a direction says TOO FEW TO CALL rather than printing one.',
                    wau_word: trendWord(tr.wau_now, tr.wau_prior),
                    wau_now: Number(tr.wau_now || 0),
                    wau_prior_week: Number(tr.wau_prior || 0),
                    dau_word: trendWord(tr.dau_now, tr.dau_prior),
                    dau_now: Number(tr.dau_now || 0),
                    dau_prior_day: Number(tr.dau_prior || 0),
                },
                user_days: {
                    value: userDays,
                    days_with_activity: perDay.length,
                    note: 'SUM of per-day DAU over ?days — "user-days", the correct ARPDAU denominator. '
                        + 'It is NOT distinct players and NOT sessions; ?view=monetization divides by '
                        + 'this exact figure.',
                },
                per_day: perDay,
                by_build: {
                    note: '⭐ THE MENU FOR EVERY OTHER VIEW SLICE. Copy an app_version/platform from '
                        + 'here into &app_version= / &platform= on retention, funnel or purchases. '
                        + 'Deliberately NOT sliced itself. A value absent here has no players in the '
                        + 'window, which is a different finding from a build whose metrics look bad.',
                    rows: byBuild,
                },
                slice: {
                    active: !!(slice.app_version || slice.platform),
                    app_version: slice.app_version,
                    platform: slice.platform,
                    locale: null,
                    precision: 'ROW-LEVEL AND EXACT here, unlike every other sliceable view: '
                        + 'session_start is the row that carries appVersion/platform, so no per-player '
                        + 'join and no multi-build ambiguity is involved.',
                    data_gaps: [LOCALE_GAP],
                },
                excluded_ids: {
                    excluded_id_count: EXCLUDED.length,
                    note: 'Operator/test ids plus the always-excluded "anonymous" shared bucket. The '
                        + 'ids come from the deployment environment, never from a query parameter.',
                },
            }));
        }

        // ======================================================== monetization
        // WO-1843 #2 — PAYER RATE, ARPU, ARPDAU, ARPPU. Revenue had a numerator
        // and no denominator anywhere on this page: ?view=purchases sums usd_anchor
        // per day and never divides it by the players who could have spent it.
        //
        // ⛔ THIS PROJECT IS PRE-REVENUE AND THE VIEW SAYS SO IN WORDS. Measured
        // read-only 2026-09-17: 4 settled entitlements, 2 distinct payer wallets,
        // $10.97 of usd_anchor all-time — of which mainnet-beta is $5.98 (2 rows),
        // the Pi rail $4.99 (1 row, status verified), and devnet $0 (1 row, no
        // anchor). Every ratio below is therefore computed AND flagged
        // reportable:false, with the WO's literal "insufficient payer volume"
        // caveat, rather than printed as a confident decimal. A four-decimal ARPU
        // over two buyers is not a measurement of anything.
        //
        // ⛔ DEVNET IS NOT REVENUE AND IS SPLIT OUT, NOT SUMMED. usd_all includes
        // every network; usd_real excludes devnet. A dashboard that reports test
        // money as income is the money version of section 16's silent capsule enemies.
        //
        // ⚠ THE DENOMINATORS ARE NOT THE SAME AXIS AND EACH NAMES ITS OWN:
        //   payer rate  payers ÷ active players (both over ?days)
        //   ARPU        revenue ÷ active players
        //   ARPDAU      revenue ÷ USER-DAYS (SUM of per-day DAU) — NOT ÷ DAU, which
        //               would divide a window's revenue by one day's players
        //   ARPPU       revenue ÷ paying players only
        // Numerator and denominator share the same ?days window throughout; mixing
        // a fixed-30-day MAU with a ?days revenue sum is how these get silently wrong.
        //
        // ⚠ IDENTITY: payers are wallet on purchase_entitlements, active players
        // are player_id on analytics_events. Those coincide only for wallet-bound
        // players (WO-1791 tiers), so the overlap is MEASURED and returned rather
        // than assumed — if it falls, the payer rate is dividing two different
        // populations and the response has to be able to say so.
        //
        // ⛔ NOT BUILT, ON PURPOSE (WO-1843 scope): LTV and churn modelling. There is
        // no payer cohort to fit them to. Do not add them here without a ruling.
        if (view === 'monetization') {
            const EXCLUDED = excludedPlayerIds();
            const errors = [];
            const probe = async (label, run) => {
                try {
                    return await run();
                } catch (err) {
                    console.error('[admin/stats] monetization probe failed:', label, err);
                    errors.push({ probe: label, error: String((err && err.message) || err) });
                    return null;
                }
            };

            const revenue = await probe('revenue_window', () => sql`
                SELECT COUNT(*)::bigint                                          AS settled,
                       COUNT(DISTINCT wallet)::bigint                            AS payers_all,
                       COALESCE(SUM(usd_anchor), 0)::float8                      AS usd_all,
                       COUNT(*) FILTER (WHERE COALESCE(network, '') <> 'devnet')::bigint AS settled_real,
                       COUNT(DISTINCT wallet) FILTER (WHERE COALESCE(network, '') <> 'devnet')::bigint AS payers_real,
                       COALESCE(SUM(usd_anchor) FILTER (WHERE COALESCE(network, '') <> 'devnet'), 0)::float8 AS usd_real,
                       COUNT(*) FILTER (WHERE usd_anchor IS NULL)::bigint         AS rows_without_usd_anchor,
                       MIN(created_at)                                           AS first_settled_at,
                       MAX(created_at)                                           AS last_settled_at
                FROM purchase_entitlements
                WHERE created_at > NOW() - (${days} * INTERVAL '1 day')
                  -- ⛔ THE OPERATOR EXCLUSION MUST BE ON BOTH SIDES OF EVERY RATIO.
                  -- The denominator below already excludes operator/test ids (WO-1281
                  -- acceptance 9). Leaving it off the numerator would count an OWNER's
                  -- own test purchase against a playerbase that does not contain them —
                  -- which, at 2 payers, would roughly double the payer rate. The whole
                  -- ledger, operator rows included, stays readable at ?view=purchases.
                  AND NOT (wallet = ANY(${EXCLUDED}::text[]))
                LIMIT 1`);

            const byNetwork = await probe('revenue_by_network', () => sql`
                SELECT COALESCE(network, '(none)') AS network,
                       status,
                       COUNT(*)::bigint                                  AS settled,
                       COUNT(DISTINCT wallet)::bigint                    AS payers,
                       COALESCE(SUM(usd_anchor), 0)::float8              AS usd_anchor_total,
                       COUNT(*) FILTER (WHERE usd_anchor IS NULL)::bigint AS rows_without_usd_anchor,
                       MAX(created_at)                                   AS latest
                FROM purchase_entitlements
                WHERE created_at > NOW() - (${days} * INTERVAL '1 day')
                  AND NOT (wallet = ANY(${EXCLUDED}::text[]))   -- both sides of every ratio
                GROUP BY 1, 2
                ORDER BY 3 DESC
                LIMIT 30`);

            // The denominator, computed the same way ?view=active computes it so the
            // two views can never disagree about how many people were here.
            const denom = await probe('active_denominators', () => sql`
                WITH d AS (
                    SELECT date_trunc('day', received_at)::date AS day, player_id
                    FROM analytics_events
                    WHERE event_name = 'session_start'
                      AND NOT (player_id = ANY(${EXCLUDED}::text[]))
                      AND received_at > NOW() - (${days} * INTERVAL '1 day')
                    GROUP BY 1, 2
                )
                SELECT COUNT(DISTINCT player_id)::bigint AS active_players,
                       COUNT(*)::bigint                  AS user_days,
                       COUNT(DISTINCT day)::bigint       AS days_with_activity
                FROM d
                LIMIT 1`);

            // ⭐ THE TIE-BREAK ON WHETHER THESE RATIOS ARE EVEN COMPARING LIKE WITH
            // LIKE. A payer whose wallet never appears in analytics_events is not in
            // the denominator at all, so payer rate would understate.
            const overlap = await probe('payer_identity_overlap', () => sql`
                SELECT COUNT(DISTINCT p.wallet)::bigint AS payers,
                       COUNT(DISTINCT p.wallet) FILTER (WHERE EXISTS (
                           SELECT 1 FROM analytics_events e WHERE e.player_id = p.wallet))::bigint
                           AS payers_seen_in_telemetry
                FROM purchase_entitlements p
                WHERE p.created_at > NOW() - (${days} * INTERVAL '1 day')
                  AND NOT (p.wallet = ANY(${EXCLUDED}::text[]))   -- both sides of every ratio
                LIMIT 1`);

            const perDay = await probe('arpdau_per_day', () => sql`
                WITH a AS (
                    SELECT date_trunc('day', received_at)::date AS day,
                           COUNT(DISTINCT player_id)::bigint    AS dau
                    FROM analytics_events
                    WHERE event_name = 'session_start'
                      AND NOT (player_id = ANY(${EXCLUDED}::text[]))
                      AND received_at > NOW() - (${days} * INTERVAL '1 day')
                    GROUP BY 1
                ),
                r AS (
                    SELECT date_trunc('day', created_at)::date AS day,
                           COALESCE(SUM(usd_anchor), 0)::float8 AS usd,
                           COUNT(DISTINCT wallet)::bigint       AS payers
                    FROM purchase_entitlements
                    WHERE created_at > NOW() - (${days} * INTERVAL '1 day')
                      AND COALESCE(network, '') <> 'devnet'
                      AND NOT (wallet = ANY(${EXCLUDED}::text[]))   -- both sides of every ratio
                    GROUP BY 1
                )
                SELECT a.day::text AS day,
                       a.dau::bigint AS dau,
                       COALESCE(r.usd, 0)::float8 AS usd_real,
                       COALESCE(r.payers, 0)::bigint AS payers
                FROM a LEFT JOIN r ON r.day = a.day
                ORDER BY 1 DESC
                LIMIT 181`);

            const rv = (revenue && revenue[0]) || {};
            const dn = (denom && denom[0]) || {};
            const ov = (overlap && overlap[0]) || {};

            const payersAll = Number(rv.payers_all || 0);
            const payersReal = Number(rv.payers_real || 0);
            const usdReal = Number(rv.usd_real || 0);
            const activePlayers = Number(dn.active_players || 0);
            const userDays = Number(dn.user_days || 0);

            // Money to 4 dp, and NEVER presented as reportable while the payer count
            // is under the threshold. The value is still returned — hiding it would
            // just move the guess somewhere else — but it is returned WITH the reason
            // it cannot be trusted, which is the whole honesty convention of this file.
            const money = (v) => (v == null ? null : Math.round(Number(v) * 10000) / 10000);
            const INSUFFICIENT = 'INSUFFICIENT PAYER VOLUME: ' + payersReal + ' paying wallet(s) in this '
                + 'window against a ' + LOW_N_THRESHOLD + '-payer threshold. This figure is arithmetic, '
                + 'not a measurement — one more sale moves it by more than any change to the game would. '
                + 'Read the raw counts (settled / payers / usd_real) instead.';

            const ratio = (label, numerator, denominator, denomLabel, formula) => {
                const ok = denominator > 0;
                const reportable = ok && payersReal >= LOW_N_THRESHOLD;
                return {
                    metric: label,
                    formula: formula,
                    value: ok ? money(numerator / denominator) : null,
                    numerator_usd: money(numerator),
                    denominator: denominator,
                    denominator_label: denomLabel,
                    reportable: reportable,
                    low_n: !reportable,
                    caveat: !ok
                        ? 'NO DENOMINATOR: ' + denomLabel + ' is zero in this window, so the ratio is '
                          + 'undefined and returned as null rather than as 0.'
                        : (reportable ? null : INSUFFICIENT),
                };
            };

            return res.status(200).json(Object.assign(meta, {
                source: 'purchase_entitlements (SERVER-VERIFIED settlement) over analytics_events '
                    + 'session_start (the active-player denominator). ?view=economy is the CLIENT own '
                    + 'word and carries no money — never use it as a numerator here.',
                headline: payersReal === 0
                    ? 'NO REVENUE IN WINDOW'
                    : (payersReal < LOW_N_THRESHOLD ? 'PRE-REVENUE — RATIOS NOT REPORTABLE' : 'REVENUE MEASURABLE'),
                low_n_threshold: LOW_N_THRESHOLD,
                revenue: {
                    operator_exclusion: '⛔ OPERATOR/TEST WALLETS ARE EXCLUDED FROM BOTH SIDES of every '
                        + 'ratio here (ANALYTICS_EXCLUDED_PLAYER_IDS, WO-1281 acceptance 9). Counting an '
                        + 'operator\'s own test purchase against a playerbase that excludes them would '
                        + 'roughly double the payer rate at this sample size. The COMPLETE ledger, '
                        + 'operator rows included, is ?view=purchases — this view is the ratios.',
                    note: 'usd_anchor is the authored ladder price persisted at verify time. It is NULL '
                        + 'on CANARY skus, so rows_without_usd_anchor > 0 means the total UNDERSTATES '
                        + 'the row count — not that those sales were free.',
                    settled: Number(rv.settled || 0),
                    payers_all_networks: payersAll,
                    usd_all_networks: money(rv.usd_all),
                    settled_real: Number(rv.settled_real || 0),
                    payers_real: payersReal,
                    usd_real: money(usdReal),
                    devnet_note: '⛔ usd_real / payers_real EXCLUDE network = devnet. Devnet money is '
                        + 'test money and reporting it as income would be a fabricated revenue figure. '
                        + 'Every ratio below is computed on usd_real.',
                    rows_without_usd_anchor: Number(rv.rows_without_usd_anchor || 0),
                    first_settled_at: rv.first_settled_at || null,
                    last_settled_at: rv.last_settled_at || null,
                    by_network: byNetwork || [],
                },
                denominators: {
                    active_players: activePlayers,
                    user_days: userDays,
                    days_with_activity: Number(dn.days_with_activity || 0),
                    definition: 'active_players = DISTINCT player_id with a session_start in ?days '
                        + '(same measure as ?view=active). user_days = SUM of per-day DAU over the '
                        + 'same window — the ARPDAU denominator, and NOT the same thing as DAU.',
                    low_n: activePlayers < LOW_N_THRESHOLD,
                },
                payer_rate: {
                    metric: 'payer rate',
                    formula: 'paying wallets (excl. devnet) ÷ active players, both over ?days',
                    pct: pct(payersReal, activePlayers),
                    payers: payersReal,
                    active_players: activePlayers,
                    reportable: payersReal >= LOW_N_THRESHOLD && activePlayers >= LOW_N_THRESHOLD,
                    low_n: payersReal < LOW_N_THRESHOLD || activePlayers < LOW_N_THRESHOLD,
                    caveat: payersReal < LOW_N_THRESHOLD ? INSUFFICIENT : null,
                },
                arpu: ratio('ARPU', usdReal, activePlayers, 'active players (?days)',
                    'usd_real ÷ active players'),
                arpdau: ratio('ARPDAU', usdReal, userDays, 'user-days (SUM of per-day DAU)',
                    'usd_real ÷ user-days. ⛔ NOT ÷ today DAU — that would divide a whole '
                    + 'window of revenue by one day of players and overstate it by roughly the '
                    + 'number of days in the window.'),
                arppu: ratio('ARPPU', usdReal, payersReal, 'paying wallets (excl. devnet)',
                    'usd_real ÷ paying wallets'),
                identity: {
                    note: 'Payers are wallet on purchase_entitlements; active players are player_id '
                        + 'on analytics_events. They are the same string only for a WALLET-BOUND player '
                        + '(WO-1791 identity tiers), so the overlap is measured rather than assumed. '
                        + 'payers_seen_in_telemetry < payers means some buyers are NOT in the '
                        + 'denominator and the payer rate understates.',
                    payers_in_window: Number(ov.payers || 0),
                    payers_seen_in_telemetry: Number(ov.payers_seen_in_telemetry || 0),
                    fully_attributable: Number(ov.payers || 0) === Number(ov.payers_seen_in_telemetry || 0),
                },
                per_day: (perDay || []).map(r => {
                    const d = Number(r.dau || 0);
                    const u = Number(r.usd_real || 0);
                    return {
                        day: r.day,
                        dau: d,
                        usd_real: money(u),
                        payers: Number(r.payers || 0),
                        arpdau: d > 0 ? money(u / d) : null,
                        low_n: Number(r.payers || 0) < LOW_N_THRESHOLD,
                    };
                }),
                out_of_scope: 'LTV and churn modelling are deliberately NOT built (WO-1843). With '
                    + payersReal + ' payer(s) in the window there is no cohort to fit them to, and a '
                    + 'model over this sample would be fiction wearing a decimal point.',
                errors: errors,
            }));
        }

        // =========================================================== stability
        // WO-1843 #3 — ERRORS AS A RATE AGAINST TRAFFIC, NOT AS A RAW COUNT.
        // ?view=events (api/admin/db.js) counts break rows by kind. A count cannot
        // answer "is it getting better": 33,198 errors across a growing playerbase
        // may be an improvement, and the same number across a shrinking one is a
        // collapse. This divides by the traffic that produced it.
        //
        // ⛔ THERE IS NO CRASH REPORTER IN THIS GAME, SO "CRASH-FREE" IS NOT
        // MEASURABLE AND IS NOT CLAIMED. The only stability signal that exists is
        // the F8 BreakCaptureHarness playtest_break row. An unhandled Unity
        // exception does NOT terminate the process, and a process the OS killed
        // emits nothing at all — so a hard-crash rate is UNPROVABLE from here. What
        // is returned is an EXCEPTION-FREE rate, named as a proxy. Recording that
        // it cannot be proven is the finding; a "99.x% crash-free" tile built on
        // this data would be invented (CLAUDE.md section 11B).
        //
        // ⛔ THE THREE KINDS ARE REPORTED SEPARATELY AND NEVER SUMMED INTO ONE
        // "crash" NUMBER. Measured read-only 2026-09-17 over 30 days:
        //   kind=error              33,198 rows / 166 ids — the Debug.LogError
        //                           firehose, present in essentially every session
        //   kind=exception              32 rows /   3 ids — the crash-adjacent one
        //   kind=possible_softlock    110 rows /  13 ids — a third axis entirely
        // Blending those would let the firehose bury the 32 rows that matter.
        //
        // ⚠ THE DENOMINATOR IS KNOWN TO BE INCOMPLETE AND THE VIEW PROVES IT RATHER
        // THAN HIDING IT. Same 30-day read: 211 player-days carried a session_start
        // while 228 player-days carried an error — MORE error-days than
        // session-days, because a break can be recorded for a player whose
        // session_start never landed (queued events, an excluded/anon boot). So a
        // rate here CAN exceed 100%, and when it does denominator_incomplete says
        // so instead of the number being quietly clamped into looking sane.
        if (view === 'stability') {
            const EXCLUDED = excludedPlayerIds();
            const sliceResolved = await resolveSlicePlayers(sql, slice);
            const sliceIds = sliceResolved.ids;

            // Player-DAYS, not sessions: playtest_break rows cannot be attributed to
            // an individual session (they carry no session id), so the finest honest
            // unit shared by both sides is the player-day. Session ROWS are returned
            // beside it for the per-session intensity figure only.
            const traffic = await sql`
                SELECT COUNT(*)::bigint                          AS session_starts,
                       COUNT(DISTINCT player_id)::bigint         AS players,
                       COUNT(DISTINCT (player_id || '|' || date_trunc('day', received_at)::date::text))::bigint
                           AS player_days
                FROM analytics_events
                WHERE event_name = 'session_start'
                  AND NOT (player_id = ANY(${EXCLUDED}::text[]))
                  AND received_at > NOW() - (${days} * INTERVAL '1 day')
                  AND (${sliceIds}::text[] IS NULL OR player_id = ANY(${sliceIds}::text[]))
                LIMIT 1`;

            const kinds = await sql`
                SELECT COALESCE(properties->>'kind', '(none)') AS kind,
                       COUNT(*)::bigint                         AS events,
                       COUNT(DISTINCT player_id)::bigint        AS players,
                       COUNT(DISTINCT (player_id || '|' || date_trunc('day', received_at)::date::text))::bigint
                           AS player_days,
                       MAX(received_at)                         AS latest
                FROM analytics_events
                WHERE event_name = 'playtest_break'
                  AND NOT (player_id = ANY(${EXCLUDED}::text[]))
                  AND received_at > NOW() - (${days} * INTERVAL '1 day')
                  AND (${sliceIds}::text[] IS NULL OR player_id = ANY(${sliceIds}::text[]))
                GROUP BY 1
                ORDER BY 2 DESC
                LIMIT 20`;

            const perDay = await sql`
                WITH s AS (
                    SELECT date_trunc('day', received_at)::date AS day,
                           COUNT(DISTINCT player_id)::bigint    AS players
                    FROM analytics_events
                    WHERE event_name = 'session_start'
                      AND NOT (player_id = ANY(${EXCLUDED}::text[]))
                      AND received_at > NOW() - (${days} * INTERVAL '1 day')
                      AND (${sliceIds}::text[] IS NULL OR player_id = ANY(${sliceIds}::text[]))
                    GROUP BY 1
                ),
                b AS (
                    SELECT date_trunc('day', received_at)::date AS day,
                           COUNT(*) FILTER (WHERE properties->>'kind' = 'error')::bigint     AS error_events,
                           COUNT(*) FILTER (WHERE properties->>'kind' = 'exception')::bigint AS exception_events,
                           COUNT(DISTINCT player_id) FILTER (WHERE properties->>'kind' = 'exception')::bigint
                               AS players_with_exception,
                           COUNT(DISTINCT player_id) FILTER (WHERE properties->>'kind' = 'possible_softlock')::bigint
                               AS players_with_softlock
                    FROM analytics_events
                    WHERE event_name = 'playtest_break'
                      AND NOT (player_id = ANY(${EXCLUDED}::text[]))
                      AND received_at > NOW() - (${days} * INTERVAL '1 day')
                      AND (${sliceIds}::text[] IS NULL OR player_id = ANY(${sliceIds}::text[]))
                    GROUP BY 1
                )
                SELECT s.day::text AS day,
                       s.players::bigint AS players,
                       COALESCE(b.error_events, 0)::bigint          AS error_events,
                       COALESCE(b.exception_events, 0)::bigint      AS exception_events,
                       COALESCE(b.players_with_exception, 0)::bigint AS players_with_exception,
                       COALESCE(b.players_with_softlock, 0)::bigint  AS players_with_softlock
                FROM s LEFT JOIN b ON b.day = s.day
                ORDER BY 1 DESC
                LIMIT 181`;

            const tf = traffic[0] || {};
            const sessionStarts = Number(tf.session_starts || 0);
            const playerDays = Number(tf.player_days || 0);

            const kindRows = kinds.map(k => {
                const affected = Number(k.player_days || 0);
                const over = playerDays > 0 && affected > playerDays;
                return {
                    kind: k.kind,
                    events: Number(k.events || 0),
                    players: Number(k.players || 0),
                    player_days_affected: affected,
                    affected_pct: pct(affected, playerDays),
                    // The complement, only where it is actually a complement. Above 100%
                    // there is no meaningful "free" share and null is the honest answer.
                    free_pct: (playerDays > 0 && !over) ? Math.round((100 - (affected / playerDays) * 100) * 10) / 10 : null,
                    events_per_session_start: sessionStarts > 0
                        ? Math.round((Number(k.events || 0) / sessionStarts) * 100) / 100 : null,
                    low_n: playerDays < LOW_N_THRESHOLD,
                    denominator_incomplete: over,
                    denominator_note: over
                        ? 'MORE affected player-days (' + affected + ') than player-days carrying a '
                          + 'session_start (' + playerDays + '). The denominator is incomplete — a break '
                          + 'can be recorded for a player whose session_start never landed — so this rate '
                          + 'is an UPPER bound and is deliberately not clamped to 100%.'
                        : null,
                    latest: k.latest,
                };
            });

            const byKind = {};
            for (const r of kindRows) byKind[r.kind] = r;
            const exception = byKind['exception'] || null;

            return res.status(200).json(Object.assign(meta, {
                definition: 'Break rates from the F8 BreakCaptureHarness playtest_break event, '
                    + 'divided by the traffic that produced them. The unit is the PLAYER-DAY: a '
                    + 'playtest_break row carries no session id, so it cannot be attributed to one '
                    + 'session, and the player-day is the finest unit both sides honestly share.',
                // ⛔ The most load-bearing string in this view.
                crash_free_caveat: '⛔ A TRUE CRASH-FREE RATE IS NOT MEASURABLE FROM THIS DATA AND IS '
                    + 'NOT CLAIMED. This game ships no crash reporter; an unhandled Unity exception does '
                    + 'not terminate the app, and a process the OS killed emits nothing at all. What is '
                    + 'below is an EXCEPTION-FREE player-day rate — a PROXY. Closing the gap needs a '
                    + 'crash SDK, which is new instrumentation and a separate ticket.',
                kinds_note: '⛔ The kinds are never summed into one "crash" number. kind=error is the '
                    + 'Debug.LogError firehose and fires in nearly every session; kind=exception is the '
                    + 'crash-adjacent one; kind=possible_softlock is a third axis. Blending them lets '
                    + 'the firehose bury the handful of rows that matter. kind=scene_loaded and '
                    + 'kind=note are bookkeeping, not breaks, and are listed for completeness only.',
                low_n_threshold: LOW_N_THRESHOLD,
                traffic: {
                    session_starts: sessionStarts,
                    players: Number(tf.players || 0),
                    player_days: playerDays,
                    low_n: playerDays < LOW_N_THRESHOLD,
                },
                headline: {
                    metric: 'exception-free player-day rate (PROXY, not crash-free)',
                    exception_free_pct: exception ? exception.free_pct : (playerDays > 0 ? 100 : null),
                    player_days_with_exception: exception ? exception.player_days_affected : 0,
                    player_days: playerDays,
                    reportable: playerDays >= LOW_N_THRESHOLD && !(exception && exception.denominator_incomplete),
                    low_n: playerDays < LOW_N_THRESHOLD,
                    caveat: playerDays < LOW_N_THRESHOLD
                        ? 'Fewer than ' + LOW_N_THRESHOLD + ' player-days in this window — not reportable.'
                        : (exception && exception.denominator_incomplete ? exception.denominator_note : null),
                },
                by_kind: kindRows,
                per_day: (perDay || []).map(r => {
                    const p = Number(r.players || 0);
                    return {
                        day: r.day,
                        players: p,
                        error_events: Number(r.error_events || 0),
                        exception_events: Number(r.exception_events || 0),
                        players_with_exception: Number(r.players_with_exception || 0),
                        players_with_softlock: Number(r.players_with_softlock || 0),
                        // ⛔ NOT CLAMPED. The by_kind rule above applies per row too: if
                        // more players hit an exception than fired a session_start that
                        // day, the denominator is incomplete and the honest output is a
                        // negative "free" share plus the flag — a Math.min here would
                        // have silently printed a plausible 0% and contradicted the very
                        // rule this view states.
                        exception_free_pct: p > 0
                            ? Math.round((100 - (Number(r.players_with_exception || 0) / p) * 100) * 10) / 10
                            : null,
                        denominator_incomplete: p > 0 && Number(r.players_with_exception || 0) > p,
                        errors_per_player: p > 0 ? Math.round((Number(r.error_events || 0) / p) * 100) / 100 : null,
                        low_n: p < LOW_N_THRESHOLD,
                    };
                }),
                slice: sliceMeta(slice,
                    ['traffic denominators', 'by_kind', 'per_day'],
                    ['nothing — the same player filter is applied to both sides of every rate'],
                    sliceResolved),
                raw_rows_pointer: 'For the rows behind a kind: GET /api/admin/db?view=events'
                    + '&name=playtest_break&group=kind. This view is the RATE; that one is the evidence.',
            }));
        }

        // ================================================================= ops
        // WO-1244 - the READ half of the Command Center console's operations
        // pillars: the six kill-switch toggles (WO-1243), the promo catalog with
        // its redemption counts, and the player-issue queue.
        //
        // ⛔ IT IS A READ. Every statement below is a SELECT with a hard LIMIT,
        // like every other statement in this file. Flipping a toggle and
        // authoring a promo are WRITES and they live at api/admin/ops.js, behind
        // a SECOND key. WO-1169 and WO-1244 both put that boundary at the
        // ENDPOINT, not in the UI, and adding one INSERT here would erase it.
        //
        // ⛔ NO WALLET LEAVES THIS BLOCK. promo_codes.bound_wallet is reported as
        // the BOOLEAN `is_bound` and the column itself is never selected - the
        // console must be able to say "this code is private" without ever putting
        // an address on a screen that gets screenshotted. bug_reports carries a
        // wallet too (WO-1169, server-verified); it is likewise reduced to
        // `wallet_verified`, because a BURST of unverified reports is the triage
        // signal, and WHOSE wallet it is never was.
        if (view === 'ops') {
            const errors = [];
            const probe = async (label, run) => {
                try {
                    return await run();
                } catch (err) {
                    console.error('[admin/stats] ops probe failed:', label, err);
                    errors.push({ probe: label, error: String((err && err.message) || err) });
                    return null;
                }
            };

            // ---- the six kill switches --------------------------------------
            // updated_by / updated_at are the whole point: WO-1244 pillar 5 asks
            // for "current state, and WHEN each was last flipped". The public
            // /api/maintenance endpoint deliberately does not carry them.
            const toggles = await probe('maintenance_toggles', () => sql`
                SELECT area_id, closed, message, updated_by, updated_at
                FROM maintenance_toggles
                ORDER BY area_id
                LIMIT 20`);

            // ---- the promo catalog ------------------------------------------
            const promos = await probe('promo_codes', () => sql`
                SELECT code, reward_crystals, reward_coins, reward_pack_sku, message,
                       active, max_redemptions, per_player_limit, expires_at, created_at,
                       (bound_wallet IS NOT NULL) AS is_bound
                FROM promo_codes
                ORDER BY created_at DESC
                LIMIT 100`);

            const redemptions = await probe('promo_redemptions', () => sql`
                SELECT code,
                       COUNT(*)::bigint               AS redemptions,
                       COUNT(DISTINCT player_id)::bigint AS players,
                       MAX(redeemed_at)               AS latest
                FROM promo_redemptions
                GROUP BY 1
                ORDER BY 2 DESC
                LIMIT 200`);

            // ---- the player-issue queue -------------------------------------
            // ⚠ DIFFERENT FROM BOARD.html AND DELIBERATELY NOT MERGED WITH IT.
            // BOARD.html is DEV work, generated from WorkOrders/*.md - anything
            // written there is overwritten on the next tools/board_build.py run
            // (WO-1169 section 4). This is the PLAYER queue. Two boards, linked,
            // never folded together.
            // WO-1867: default to the OPEN queue only, same as the console's
            // default view. `?reportStatus=all` is the audit escape hatch - it
            // does not delete or hide anything, it only widens what this ONE
            // read returns; the underlying rows are never touched by a GET.
            const reportsIncludeReviewed = String(q.reportStatus || '').toLowerCase() === 'all';
            const reports = await probe('bug_reports', () => reportsIncludeReviewed ? sql`
                SELECT report_id, created_at, description, route, app_version, player_id,
                       (COALESCE(wallet, context->>'verifiedWallet') IS NOT NULL) AS wallet_verified,
                       context->>'platform' AS platform,
                       (context ? 'screenshotB64' AND context->>'screenshotB64' IS NOT NULL) AS has_screenshot,
                       status, reviewed_by, reviewed_at
                FROM bug_reports
                ORDER BY report_id DESC
                LIMIT 50` : sql`
                SELECT report_id, created_at, description, route, app_version, player_id,
                       (COALESCE(wallet, context->>'verifiedWallet') IS NOT NULL) AS wallet_verified,
                       context->>'platform' AS platform,
                       (context ? 'screenshotB64' AND context->>'screenshotB64' IS NOT NULL) AS has_screenshot,
                       status, reviewed_by, reviewed_at
                FROM bug_reports
                WHERE status = 'open'
                ORDER BY report_id DESC
                LIMIT 50`);

            const reportsPerDay = await probe('bug_reports_per_day', () => sql`
                SELECT date_trunc('day', created_at)::date::text AS day,
                       COUNT(*)::bigint AS reports
                FROM bug_reports
                WHERE created_at > NOW() - (${days} * INTERVAL '1 day')
                GROUP BY 1
                ORDER BY 1 DESC
                LIMIT 181`);

            // ---- gate issue counts + matching drill-down rows ---------------
            // These are server-authored refusal records, not client event
            // volume. player_id is the salted 12-hex maintenance fingerprint
            // (_lib/maintenance.fingerprint), so it can correlate repeat
            // failures without returning a wallet. The count and rows share
            // this exact CTE/window/filter: tapping a number explains it.
            const gateIssues = await probe('maintenance_gate_issues', () => sql`
                WITH scoped AS (
                    SELECT event_id,
                           received_at,
                           event_name,
                           player_id,
                           properties->>'area' AS area,
                           properties->>'closedBy' AS closed_by,
                           properties->>'ref' AS correlation_ref,
                           properties->>'path' AS path
                    FROM analytics_events
                    WHERE event_name = 'maintenance_refusal'
                      AND received_at > NOW() - (${days} * INTERVAL '1 day')
                )
                , ranked AS (
                    SELECT event_id, received_at, event_name, player_id, area, closed_by,
                           correlation_ref, path,
                           COUNT(*) OVER (PARTITION BY area)::bigint AS area_issue_count,
                           ROW_NUMBER() OVER (PARTITION BY area ORDER BY received_at DESC) AS area_row
                    FROM scoped
                    WHERE area = ANY(${MAINTENANCE_AREAS}::text[])
                )
                SELECT event_id, received_at, event_name, player_id, area, closed_by,
                       correlation_ref, path, area_issue_count
                FROM ranked
                WHERE area_row <= 50
                ORDER BY received_at DESC
                LIMIT 300`);

            // ---- the ops write history --------------------------------------
            // Every write api/admin/ops.js performs leaves one row here. Reading
            // it back is how "who sealed raiding, and when" is answered after the
            // fact from the console rather than from a psql prompt.
            const opsHistory = await probe('admin_ops_write_history', () => sql`
                SELECT received_at,
                       properties->>'action'   AS action,
                       properties->>'operator' AS operator,
                       properties->>'target'   AS target,
                       properties->>'outcome'  AS outcome
                FROM analytics_events
                WHERE event_name = 'admin_ops_write'
                ORDER BY received_at DESC
                LIMIT 50`);

            const redemptionByCode = {};
            for (const r of (redemptions || [])) {
                redemptionByCode[r.code] = {
                    redemptions: Number(r.redemptions || 0),
                    players: Number(r.players || 0),
                    latest: r.latest,
                };
            }

            // A row missing from maintenance_toggles is NOT an error and NOT a
            // seal: under the WO-1243 fail-open ruling an absent row means the
            // area is OPEN. Reported as such, in words, rather than as a gap the
            // console has to interpret.
            const toggleById = {};
            for (const r of (toggles || [])) toggleById[String(r.area_id)] = r;
            const gateIssueRows = {};
            for (const id of MAINTENANCE_AREAS) gateIssueRows[id] = [];
            for (const r of (gateIssues || [])) {
                const id = String(r.area || '');
                if (!Object.prototype.hasOwnProperty.call(gateIssueRows, id)) continue;
                gateIssueRows[id].push({
                    at: r.received_at,
                    kind: 'REFUSED',
                    player_ref: r.player_id === ANON_ID ? null : r.player_id,
                    correlation_ref: r.correlation_ref || null,
                    path: r.path || null,
                    closed_by: r.closed_by || null,
                });
            }
            const areaRows = MAINTENANCE_AREAS.map((id) => {
                const r = toggleById[id];
                const closed = !!(r && r.closed === true);
                const issues = gateIssueRows[id];
                return {
                    area: id,
                    closed: closed,
                    state: closed ? 'CLOSED' : 'open',
                    message: (r && r.message) || null,
                    updated_by: (r && r.updated_by) || null,
                    updated_at: (r && r.updated_at) || null,
                    row_present: !!r,
                    issue_count: issues.length ? Number((gateIssues || []).find(x => x.area === id).area_issue_count || 0) : 0,
                    issues_returned: issues.length,
                    issues_truncated: issues.length > 0 && Number((gateIssues || []).find(x => x.area === id).area_issue_count || 0) > issues.length,
                    issues: issues,
                    note: r ? null : 'no row - fail-open means this area is OPEN',
                };
            });
            const serverClosed = areaRows.some(a => a.area === 'server' && a.closed);

            return res.status(200).json(Object.assign(meta, {
                source: 'maintenance_toggles + promo_codes + promo_redemptions + bug_reports. '
                    + 'READ ONLY. Writes live at POST /api/admin/ops behind a second key.',
                toggles: {
                    note: 'State is a WORD ("CLOSED" / "open"), never a colour. When `server` is '
                        + 'CLOSED every area is closed whatever its own row says.',
                    read_ok: toggles !== null,
                    server_closed: serverClosed,
                    sealed_count: areaRows.filter(a => a.closed).length,
                    areas: areaRows,
                },
                promos: {
                    note: 'bound_wallet is reported as is_bound only - the address itself is never '
                        + 'selected, so no screenshot of this console can carry one.',
                    rows: (promos || []).map(p => {
                        const used = redemptionByCode[p.code] || { redemptions: 0, players: 0, latest: null };
                        const expired = p.expires_at ? (new Date(p.expires_at).getTime() <= Date.now()) : false;
                        const capped = p.max_redemptions != null && used.redemptions >= Number(p.max_redemptions);
                        return {
                            code: p.code,
                            state: !p.active ? 'DISABLED' : expired ? 'EXPIRED' : capped ? 'FULLY REDEEMED' : 'ACTIVE',
                            active: p.active === true,
                            expired: expired,
                            capped: capped,
                            is_bound: p.is_bound === true,
                            reward_pack_sku: p.reward_pack_sku,
                            reward_crystals: Number(p.reward_crystals || 0),
                            reward_coins: Number(p.reward_coins || 0),
                            message: p.message,
                            max_redemptions: p.max_redemptions == null ? null : Number(p.max_redemptions),
                            per_player_limit: p.per_player_limit == null ? null : Number(p.per_player_limit),
                            expires_at: p.expires_at,
                            created_at: p.created_at,
                            redemptions: used.redemptions,
                            redeemed_by_players: used.players,
                            last_redeemed_at: used.latest,
                        };
                    }),
                },
                reports: {
                    note: 'The PLAYER issue queue. BOARD.html is the DEV board and is generated '
                        + 'from WorkOrders/*.md - the two are linked, never merged. Defaults to '
                        + 'OPEN rows only; pass ?reportStatus=all to also see resolved/noise ones '
                        + 'for audit - nothing is ever deleted, a status flag only changes what '
                        + 'this view returns.',
                    // WO-1867. states which filter produced `rows` below, because a caller
                    // that only sees an empty array cannot otherwise tell "no open issues"
                    // from "the whole queue got mis-scoped".
                    filter: reportsIncludeReviewed ? 'all' : 'open',
                    per_day: reportsPerDay || [],
                    rows: (reports || []).map(r => ({
                        report_id: r.report_id == null ? null : Number(r.report_id),
                        created_at: r.created_at,
                        description: r.description,
                        route: r.route,
                        app_version: r.app_version,
                        platform: r.platform,
                        player_masked: maskId(r.player_id),
                        identity: r.wallet_verified ? 'verified' : 'unverified',
                        has_screenshot: r.has_screenshot === true,
                        // WORDS, not a colour - the owner is red/green colourblind.
                        status: (r.status || 'open').toUpperCase(),
                        reviewed_by: r.reviewed_by || null,
                        reviewed_at: r.reviewed_at || null,
                    })),
                },
                ops_history: (opsHistory || []).map(r => ({
                    at: r.received_at,
                    action: r.action,
                    operator: r.operator,
                    target: r.target,
                    outcome: r.outcome,
                })),
                errors: errors,
            }));
        }

        // ============================================================= players
        // Recent players. LIST = masked only. SINGLE = full id (see header).
        if (view === 'players') {
            // ---- single-player drill-down -----------------------------------
            // Reached either by full id (?player=) or by the opaque handle the
            // list emits (?ref=). This is the ONLY place a full wallet address
            // is returned, and only for one explicitly-requested player: the
            // operator needs the real address to bind a promo code to them
            // (promo_codes.bound_wallet) or to answer a support ticket. A bulk
            // list has no such need, so it never gets one.
            if (q.player || q.ref) {
                let playerId = q.player ? String(q.player) : null;

                if (!playerId) {
                    // Resolve the 12-hex handle inside a BOUNDED recent-players
                    // set — never a hash scan over the whole event table.
                    const ref = String(q.ref).toLowerCase().replace(/[^0-9a-f]/g, '').slice(0, 12);
                    if (ref.length !== 12) {
                        return res.status(400).json({ error: 'ref must be 12 hex chars' });
                    }
                    const found = await sql`
                        WITH recent AS (
                            SELECT player_id, MAX(received_at) AS last_seen
                            FROM analytics_events
                            WHERE player_id <> ${ANON_ID}
                            GROUP BY player_id
                            ORDER BY 2 DESC
                            LIMIT 500
                        )
                        SELECT player_id
                        FROM recent
                        WHERE substr(encode(sha256(convert_to(player_id, 'UTF8')), 'hex'), 1, 12) = ${ref}
                        LIMIT 1`;
                    if (found.length === 0) {
                        return res.status(200).json(Object.assign(meta, { player: null, found: false,
                            note: 'No match among the 500 most recently active players.' }));
                    }
                    playerId = found[0].player_id;
                }

                const summary = await sql`
                    SELECT MIN(received_at) AS first_seen,
                           MAX(received_at) AS last_seen,
                           COUNT(*)::bigint AS events,
                           COUNT(*) FILTER (WHERE event_name = 'session_start')::bigint AS sessions,
                           COUNT(DISTINCT date_trunc('day', received_at))::bigint       AS active_days
                    FROM analytics_events
                    WHERE player_id = ${playerId}
                    LIMIT 1`;

                const byEvent = await sql`
                    SELECT event_name, COUNT(*)::bigint AS events, MAX(received_at) AS latest
                    FROM analytics_events
                    WHERE player_id = ${playerId}
                    GROUP BY 1
                    ORDER BY 2 DESC
                    LIMIT 60`;

                // Names + times only. `properties` is client-authored free-form
                // JSONB and is never echoed here.
                const recent = await sql`
                    SELECT event_id, event_name, received_at
                    FROM analytics_events
                    WHERE player_id = ${playerId}
                    ORDER BY received_at DESC
                    LIMIT 50`;

                const save = await sql`
                    SELECT schema_version, trust, created_at, updated_at,
                           pg_column_size(game_state) AS payload_bytes
                    FROM player_data
                    WHERE player_id = ${playerId}
                    LIMIT 1`;

                return res.status(200).json(Object.assign(meta, {
                    found: true,
                    player: playerId,            // FULL id — the documented single exception
                    player_masked: maskId(playerId),
                    summary: summary[0] || null,
                    save: save[0] || null,
                    events_by_name: byEvent,
                    recent_events: recent,
                }));
            }

            // ---- list -------------------------------------------------------
            const limit = clampLimit(q.limit, 50, 200);
            const rows = await sql`
                SELECT player_id,
                       substr(encode(sha256(convert_to(player_id, 'UTF8')), 'hex'), 1, 12) AS player_ref,
                       MIN(received_at) AS first_seen,
                       MAX(received_at) AS last_seen,
                       COUNT(*)::bigint AS events,
                       COUNT(*) FILTER (WHERE event_name = 'session_start')::bigint AS sessions,
                       COUNT(DISTINCT date_trunc('day', received_at))::bigint       AS active_days
                FROM analytics_events
                WHERE player_id <> ${ANON_ID}
                GROUP BY player_id
                ORDER BY 4 DESC
                LIMIT ${limit}`;

            return res.status(200).json(Object.assign(meta, {
                limit: limit,
                note: 'Ids are masked. Use player_ref with ?view=players&ref=<handle> to open one player.',
                // WO-1842. Said out loud rather than left for someone to discover: this
                // ranking is by RAW EVENT COUNT, and since 2026-09-17 a 60s
                // session_heartbeat means that count tracks TIME PLAYED more than acts
                // performed. The ordering is arguably more useful now, but it is a
                // different question than it was, and an unannounced change of meaning is
                // the thing this file spends its comments preventing.
                ordering_note: 'Ordered by raw event COUNT. Since WO-1842 added a 60-second '
                    + 'session_heartbeat, that count is driven largely by time spent in the '
                    + 'app rather than by actions taken. For measured playtime itself use '
                    + '?view=playtime; for acts, the qualifying-play allowlist on ?view=command.',
                rows: rows.map(r => ({
                    player_masked: maskId(r.player_id),
                    player_ref: r.player_ref,
                    is_guest: String(r.player_id).startsWith('guest-local-'),
                    first_seen: r.first_seen,
                    last_seen: r.last_seen,
                    events: Number(r.events || 0),
                    sessions: Number(r.sessions || 0),
                    active_days: Number(r.active_days || 0),
                })),
            }));
        }

        return res.status(400).json({
            // WO-1842 added playtime. `skus` was already live and had never been
            // listed here - a view nobody can discover is a view nobody uses, and
            // this hint is the only discovery surface the endpoint has.
            // WO-1843 adds active | monetization | stability, named here in the SAME
            // edit that added them for exactly the reason above.
            error: 'Unknown view. Use: overview | retention | funnel | economy | purchases | '
                + 'playtime | ops | players | command | skus | active | monetization | stability',
        });
    } catch (err) {
        console.error('[admin/stats] error:', err);
        return res.status(500).json({ error: 'Internal server error' });
    }
};

// =============================================================================
// api/admin/db.js — OWNER-ONLY read-only database viewer endpoint (2026-07-12)
// -----------------------------------------------------------------------------
// Backs tools/db-viewer/index.html (local HTML the owner double-clicks).
// GET only. Every request must carry header  X-Admin-Key  matching the Vercel
// env var ADMIN_DASH_KEY (sensitive). Constant-time compare; missing/mismatch
// → 400 (project constraint: status codes are 200 | 400 | 500 only).
//
// READ-ONLY BY CONSTRUCTION: every query is a SELECT with a hard LIMIT; all
// user inputs are parameterized via the neon sql tagged-template (same driver
// + house style as api/trace.js / game/load.js). No writes, no PII beyond what
// the tables already hold; save blobs are size-only unless one explicit
// player id is requested.
//
//   GET /api/admin/db?view=overview
//       per-table row counts + newest timestamp per table
//   GET /api/admin/db?view=players[&limit=N][&player=<id>]
//       latest N player_data rows (id, versions, timestamps, payload SIZE only);
//       player=<id> → that one player's full record
//   GET /api/admin/db?view=metrics
//       last-7-day aggregates from analytics_events (per-event-per-day counts,
//       distinct players/sessions per day, web_trace error-line count per day)
//   GET /api/admin/db?view=ads[&days=N]
//       WO-1796: the ad money. Aggregates of the LevelPlay ILRD rows already in
//       analytics_events (rewarded_ad_impression) — revenue per day / network /
//       placement, eCPM only above an impression floor, and the count of
//       impressions whose network reported NO revenue (never summed as zero)
//   GET /api/admin/db?view=traces[&session=<id>][&limit=N]
//       with session: latest N web_trace rows for that session, with lines;
//       without: latest web_trace sessions (summary) so the owner can pick one
//   GET /api/admin/db?view=bugreports[&limit=N][&after_id=N]
//       newest player bug reports (screenshot as a presence flag only)
//   GET /api/admin/db?view=bugreport&id=<report_id>[&shot=1]
//       ONE report in full — the entire traceTail, and the screenshot base64
//       only when shot=1 (the blob can be ~420K chars)
//   GET /api/admin/db?view=authrejects[&code=<CODE>][&ref=<ref>][&since_hours=N]
//       the structured save/load auth failures (2026-08-02): a summary by
//       code+path, or the rows for one code, or the single row behind one
//       player-reported ref
//   GET /api/admin/db?view=events&name=<event_name>[&since_hours=N][&limit=N]
//                                 [&player=<id>][&group=rows|message|kind|player]
//       WO-1793: ANY event name's PAYLOAD, not just its count. The gap this
//       closes: on 2026-09-16, 424 `playtest_break` rows and 7
//       `save_reset_accepted` rows landed and NO view in the product could name
//       one of them — `metrics` returns counts only, `traces` is hardcoded to
//       'web_trace' (which only POSTs under UNITY_WEBGL, so it is empty for the
//       Android platform the game ships on), and `authrejects` reads exactly
//       three event names. The triage had to connect to Neon with DATABASE_URL
//       by hand, which the owner cannot do from a phone at all.
//   GET /api/admin/db?view=funnel[&since_hours=N]
//       WO-1793: per distinct player id in the window — first_seen, event count
//       and the SET of event names it emitted. One query for the per-player
//       funnel that same triage assembled by hand.
// =============================================================================

const { neon } = require('@neondatabase/serverless');
const crypto = require('crypto');

// Constant-time key check. Hashing both sides first makes timingSafeEqual
// usable on unequal lengths without leaking length information.
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

// Paging offset for the traces view. Bounded like clampLimit so a hostile/garbled
// value can never become an unbounded scan: non-numeric/negative -> 0, hard ceiling
// so OFFSET stays sane (the largest real session seen is ~2840 batches).
function clampOffset(raw) {
    const n = parseInt(raw, 10);
    if (!Number.isFinite(n) || n <= 0) return 0;
    return Math.min(n, 100000);
}

module.exports = async (req, res) => {
    // CORS: the viewer is a local file (Origin "null") fetching cross-origin.
    res.setHeader('Access-Control-Allow-Origin', '*');
    res.setHeader('Access-Control-Allow-Methods', 'GET, OPTIONS');
    res.setHeader('Access-Control-Allow-Headers', 'Content-Type, X-Admin-Key');
    if (req.method === 'OPTIONS') { return res.status(204).end(); }

    if (req.method !== 'GET') {
        return res.status(400).json({ error: 'Method not allowed' });
    }

    const expected = process.env.ADMIN_DASH_KEY;
    if (!expected) {
        // Not configured yet — refuse everything (never fail open).
        return res.status(400).json({ error: 'Admin access not configured' });
    }
    if (!adminKeyOk(req.headers['x-admin-key'], expected)) {
        return res.status(400).json({ error: 'Unauthorized' });
    }

    const q = req.query || {};
    const view = String(q.view || 'overview');

    try {
        const sql = neon(process.env.DATABASE_URL);

        // ---------------------------------------------------------------- overview
        if (view === 'overview') {
            // Table names cannot be parameterized, so every query below is a
            // fully STATIC tagged-template literal (no user input reaches SQL).
            // Each table is probed independently so a missing table (schema
            // drift) degrades to an error entry instead of failing the view.
            const probes = [
                ['player_data',        'updated_at',  () => sql`SELECT COUNT(*)::bigint AS rows, MAX(updated_at)  AS latest FROM player_data`],
                ['analytics_events',   'received_at', () => sql`SELECT COUNT(*)::bigint AS rows, MAX(received_at) AS latest FROM analytics_events`],
                ['bug_reports',        'created_at',  () => sql`SELECT COUNT(*)::bigint AS rows, MAX(created_at)  AS latest FROM bug_reports`],
                ['auth_nonces',        'created_at',  () => sql`SELECT COUNT(*)::bigint AS rows, MAX(created_at)  AS latest FROM auth_nonces`],
                ['guest_rate_limit',   'last_seen',   () => sql`SELECT COUNT(*)::bigint AS rows, MAX(last_seen)   AS latest FROM guest_rate_limit`],
                ['promo_codes',        'created_at',  () => sql`SELECT COUNT(*)::bigint AS rows, MAX(created_at)  AS latest FROM promo_codes`],
                ['promo_redemptions',  'redeemed_at', () => sql`SELECT COUNT(*)::bigint AS rows, MAX(redeemed_at) AS latest FROM promo_redemptions`],
                ['referrals',          'created_at',  () => sql`SELECT COUNT(*)::bigint AS rows, MAX(created_at)  AS latest FROM referrals`],
                ['referral_claims',    'claimed_at',  () => sql`SELECT COUNT(*)::bigint AS rows, MAX(claimed_at)  AS latest FROM referral_claims`],
                ['tower_swaps',        'logged_at',   () => sql`SELECT COUNT(*)::bigint AS rows, MAX(logged_at)   AS latest FROM tower_swaps`],
                ['player_profiles',    'created_at',  () => sql`SELECT COUNT(*)::bigint AS rows, MAX(created_at)  AS latest FROM player_profiles`],
                ['leaderboard_scores', 'updated_at',  () => sql`SELECT COUNT(*)::bigint AS rows, MAX(updated_at)  AS latest FROM leaderboard_scores`],
                ['achievement_grants', 'granted_at',  () => sql`SELECT COUNT(*)::bigint AS rows, MAX(granted_at)  AS latest FROM achievement_grants`],
                // WO-1114: the dungeon door states. Without this probe the table is
                // invisible in the viewer, and an operator cannot see what they flipped.
                ['dungeon_status',     'updated_at',  () => sql`SELECT COUNT(*)::bigint AS rows, MAX(updated_at)  AS latest FROM dungeon_status`],
                // ⛔ THE MONEY TABLES (WO-1169, added 2026-08-24). These were absent from this list
                // entirely, so the SERVER'S OWN RECORD OF WHAT WAS PAID was unreadable by any
                // console — while api/admin/stats.js reported "purchases" from analytics_events, a
                // CLIENT-emitted event that carries no price at all. The only purchase view we had
                // counted what the client CLAIMED, not what settled. That is the wrong direction of
                // trust, and it is the same direction WO-1158 already corrected inside the rail.
                ['purchase_quotes',       'issued_at',   () => sql`SELECT COUNT(*)::bigint AS rows, MAX(issued_at)   AS latest FROM purchase_quotes`],
                ['purchase_entitlements', 'created_at',  () => sql`SELECT COUNT(*)::bigint AS rows, MAX(created_at)  AS latest FROM purchase_entitlements`],
                // WO-1797: the Pi rail's OWN ledger. It was absent, so a Pi payment sitting at
                // state='granted' was invisible to every console — which is exactly how a purchase
                // the server had fully processed read as "the player may be owed goods" for six days.
                ['pi_payments',           'created_at',  () => sql`SELECT COUNT(*)::bigint AS rows, MAX(created_at)  AS latest FROM pi_payments`],
                ['auth_sessions',         'created_at',  () => sql`SELECT COUNT(*)::bigint AS rows, MAX(created_at)  AS latest FROM auth_sessions`],
            ];
            const rows = [];
            for (const [table, tsCol, run] of probes) {
                try {
                    const r = await run();
                    rows.push({ table: table, rows: Number(r[0].rows), latest: r[0].latest, latest_col: tsCol });
                } catch (e) {
                    rows.push({ table: table, rows: null, latest: null, latest_col: tsCol, error: 'missing or unreadable' });
                }
            }
            return res.status(200).json({ view: 'overview', generated_at: new Date().toISOString(), tables: rows });
        }

        // ---------------------------------------------------------------- players
        if (view === 'players') {
            if (q.player) {
                // One explicit player → the full record (the ONLY path that
                // returns a save blob).
                const rows = await sql`
                    SELECT player_id, schema_version, trust, created_at, updated_at,
                           pg_column_size(game_state) AS payload_bytes,
                           (SELECT COUNT(*) FROM jsonb_object_keys(game_state)) AS state_keys,
                           game_state
                    FROM player_data
                    WHERE player_id = ${String(q.player)}
                    LIMIT 1`;
                return res.status(200).json({ view: 'players', player: String(q.player), rows: rows });
            }
            const limit = clampLimit(q.limit, 25, 100);
            // List view: payload SIZE only — never dump save blobs in bulk.
            // state_keys is the fast "is this a real save or a husk" tell: the
            // client's full snapshot is ~60 keys, and the pre-2026-08-02 save.js
            // whitelist could only ever write 13.
            const rows = await sql`
                SELECT player_id, schema_version, trust, created_at, updated_at,
                       pg_column_size(game_state) AS payload_bytes,
                       (SELECT COUNT(*) FROM jsonb_object_keys(game_state)) AS state_keys
                FROM player_data
                ORDER BY updated_at DESC
                LIMIT ${limit}`;
            return res.status(200).json({ view: 'players', limit: limit, rows: rows });
        }

        // ---------------------------------------------------------------- metrics
        if (view === 'metrics') {
            // All SQL aggregates over the last 7 days — no raw rows leave the DB.
            const perEventPerDay = await sql`
                SELECT date_trunc('day', received_at)::date::text AS day,
                       event_name,
                       COUNT(*)::bigint AS events
                FROM analytics_events
                WHERE received_at > NOW() - INTERVAL '7 days'
                GROUP BY 1, 2
                ORDER BY 1 DESC, 3 DESC
                LIMIT 500`;
            const perDay = await sql`
                SELECT date_trunc('day', received_at)::date::text AS day,
                       COUNT(*)::bigint AS events,
                       COUNT(DISTINCT player_id)::bigint AS distinct_players,
                       COUNT(DISTINCT properties->>'session')
                           FILTER (WHERE event_name = 'web_trace')::bigint AS distinct_trace_sessions
                FROM analytics_events
                WHERE received_at > NOW() - INTERVAL '7 days'
                GROUP BY 1
                ORDER BY 1 DESC
                LIMIT 7`;
            const traceErrorsPerDay = await sql`
                SELECT date_trunc('day', e.received_at)::date::text AS day,
                       COUNT(*)::bigint AS error_lines
                FROM analytics_events e,
                     jsonb_array_elements_text(e.properties->'lines') AS line
                WHERE e.event_name = 'web_trace'
                  AND e.received_at > NOW() - INTERVAL '7 days'
                  AND line ~* '(exception|nullreference|softlock|\\yerror\\y|\\yfail)'
                GROUP BY 1
                ORDER BY 1 DESC
                LIMIT 7`;
            return res.status(200).json({
                view: 'metrics', window_days: 7,
                per_day: perDay,
                per_event_per_day: perEventPerDay,
                trace_error_lines_per_day: traceErrorsPerDay,
            });
        }

        // ------------------------------------------------------------------- ads
        // WO-1796. The ILRD money has been landing in analytics_events since the
        // LevelPlay provider shipped — one row per impression, with the network's own
        // revenue figure in properties->>'revenueUsd' — and NOTHING on the server has
        // ever read it. This view is that read. The owner's question was literally
        // "I cant figure out how to see if ads are making anything".
        //
        // !! ONE GUARD, ONE PLACE. properties->>'revenueUsd' is TEXT out of JSONB and a
        // bare ::numeric cast THROWS 22P02 on anything non-numeric (an _raw-wrapped
        // property from api/events/track.js is exactly that shape), which would take the
        // whole view down. The regex-guarded CASE below is written ONCE, inside the `imp`
        // CTE, and every aggregate reads the already-normalised column. A second copy of
        // that cast anywhere in this file is a bug, not a convenience.
        //
        // !! A NULL REVENUE IS NOT A ZERO. "the network reported nothing" and "the network
        // reported $0.00" are different facts and the surface must be able to tell them
        // apart, so impressions_without_revenue is counted and returned alongside the sum.
        // Summing a missing value as zero is the same class of lie as rendering a failed
        // query as 0 (see api/admin/console.js).
        if (view === 'ads') {
            const days = clampLimit(q.days, 7, 90);

            // One query, one guard. Four aggregates come back as four JSON arrays plus a
            // totals row; they cannot disagree with each other because they all read the
            // same CTE. The CTE carries its own hard LIMIT so this can never become an
            // unbounded scan however long the window.
            const agg = await sql`
                WITH imp AS (
                    SELECT received_at,
                           -- NULLIF before COALESCE, not COALESCE alone: LevelPlay's ILRD
                           -- payload returns Placement as an EMPTY STRING, not null, on
                           -- every real row measured 2026-09-17 (65 of 65). COALESCE alone
                           -- passed that through and the table rendered a blank cell, which
                           -- reads as a rendering bug rather than as "the network did not
                           -- say". Both absences now name themselves.
                           COALESCE(NULLIF(properties->>'network',   ''), '(not reported)') AS network,
                           COALESCE(NULLIF(properties->>'format',    ''), '(not reported)') AS format,
                           COALESCE(NULLIF(properties->>'placement', ''), '(not reported)') AS placement,
                           CASE WHEN properties->>'revenueUsd' ~ '^-?[0-9]+(\\.[0-9]+)?([eE][-+]?[0-9]+)?$'
                                THEN (properties->>'revenueUsd')::numeric
                           END AS revenue
                    FROM analytics_events
                    WHERE event_name = 'rewarded_ad_impression'
                      AND received_at > NOW() - (${days} * INTERVAL '1 day')
                    LIMIT 200000
                ),
                per_day AS (
                    SELECT date_trunc('day', received_at)::date::text AS day,
                           COUNT(*)::bigint AS impressions,
                           COUNT(*) FILTER (WHERE revenue IS NULL)::bigint AS impressions_without_revenue,
                           COALESCE(SUM(revenue), 0)::float8 AS revenue_usd
                    FROM imp GROUP BY 1 ORDER BY 1 DESC LIMIT 90
                ),
                per_network AS (
                    SELECT network,
                           COUNT(*)::bigint AS impressions,
                           COUNT(*) FILTER (WHERE revenue IS NULL)::bigint AS impressions_without_revenue,
                           COALESCE(SUM(revenue), 0)::float8 AS revenue_usd
                    FROM imp GROUP BY 1 ORDER BY 4 DESC, 2 DESC LIMIT 50
                ),
                per_placement AS (
                    SELECT format, placement,
                           COUNT(*)::bigint AS impressions,
                           COUNT(*) FILTER (WHERE revenue IS NULL)::bigint AS impressions_without_revenue,
                           COALESCE(SUM(revenue), 0)::float8 AS revenue_usd
                    FROM imp GROUP BY 1, 2 ORDER BY 5 DESC, 3 DESC LIMIT 50
                ),
                totals AS (
                    SELECT COUNT(*)::bigint AS impressions,
                           COUNT(*) FILTER (WHERE revenue IS NULL)::bigint AS impressions_without_revenue,
                           COALESCE(SUM(revenue), 0)::float8 AS revenue_usd,
                           MAX(received_at) AS newest_impression_at
                    FROM imp
                )
                SELECT (SELECT COALESCE(json_agg(d), '[]'::json) FROM per_day d)         AS per_day,
                       (SELECT COALESCE(json_agg(nw), '[]'::json) FROM per_network nw)   AS per_network,
                       (SELECT COALESCE(json_agg(p), '[]'::json) FROM per_placement p)   AS per_placement,
                       (SELECT impressions FROM totals)                 AS impressions,
                       (SELECT impressions_without_revenue FROM totals) AS impressions_without_revenue,
                       (SELECT revenue_usd FROM totals)                 AS revenue_usd,
                       (SELECT newest_impression_at FROM totals)        AS newest_impression_at`;

            // The reward rail is a DIFFERENT emitter (AdGateService, provider-agnostic —
            // it fires for the Pi Developer Ad Network too), so it is counted separately
            // and split by the provider property WO-1796 added to it. Older rows predate
            // that property and report '(not reported)' rather than being attributed to a
            // rail they were never tagged with.
            const completions = await sql`
                SELECT COALESCE(NULLIF(properties->>'provider', ''), '(not reported)') AS provider,
                       COALESCE(NULLIF(properties->>'outcome',  ''), '(not reported)') AS outcome,
                       -- ::int, not ::bigint: the driver hands a bigint back as a STRING,
                       -- and a count that arrives as "18" instead of 18 is a trap for any
                       -- later caller that adds it up without a Number() first.
                       COUNT(*)::int AS completions
                FROM analytics_events
                WHERE event_name = 'rewarded_ad_completed'
                  AND received_at > NOW() - (${days} * INTERVAL '1 day')
                GROUP BY 1, 2
                ORDER BY 3 DESC
                LIMIT 50`;

            const row = (agg && agg[0]) || {};
            const impressions = Number(row.impressions || 0);
            const revenueUsd = Number(row.revenue_usd || 0);

            // !! eCPM ON A HANDFUL OF IMPRESSIONS IS NOISE, NOT A METRIC. Below the floor
            // we return null and say low_n, and the surface prints "too few to trust" in
            // words. A confident eCPM off 17 impressions is a number the owner would make
            // a decision on, and it would mean nothing.
            const ECPM_MIN_IMPRESSIONS = 20;
            const ecpm = (n, usd) => (Number(n) >= ECPM_MIN_IMPRESSIONS
                ? Math.round((Number(usd) / Number(n)) * 1000 * 100) / 100
                : null);

            const perNetwork = (row.per_network || []).map(nw => Object.assign({}, nw, {
                ecpm_usd: ecpm(nw.impressions, nw.revenue_usd),
                low_n: Number(nw.impressions) < ECPM_MIN_IMPRESSIONS,
            }));

            return res.status(200).json({
                view: 'ads', window_days: days,
                read_ok: true,
                impressions: impressions,
                impressions_without_revenue: Number(row.impressions_without_revenue || 0),
                revenue_usd: revenueUsd,
                newest_impression_at: row.newest_impression_at || null,
                ecpm_usd: ecpm(impressions, revenueUsd),
                low_n: impressions < ECPM_MIN_IMPRESSIONS,
                ecpm_min_impressions: ECPM_MIN_IMPRESSIONS,
                per_day: row.per_day || [],
                per_network: perNetwork,
                per_placement: row.per_placement || [],
                completions: completions,
                completions_total: (completions || []).reduce((a, c) => a + Number(c.completions || 0), 0),
                notes: {
                    revenue_source: "analytics_events.properties->>'revenueUsd' on rewarded_ad_impression, " +
                        'written by LevelPlayInitializer straight from the LevelPlay ILRD callback. It is ' +
                        "the network's own figure, not an estimate of ours.",
                    null_revenue: 'impressions_without_revenue counts impressions carrying NO USABLE NUMERIC ' +
                        'revenue figure — the property absent, null, or present but not a number (a ' +
                        'malformed value is counted here rather than cast, which would throw). Those are ' +
                        'NOT summed as zero; revenue_usd is the sum over the impressions that did report one.',
                    cross_rail: 'impressions come only from LevelPlay (Android). completions come from ' +
                        'AdGateService, which is provider-agnostic, so impressions / completions is NOT a ' +
                        'completion rate — read the provider split before comparing them.',
                },
            });
        }

        // ---------------------------------------------------------------- traces
        if (view === 'traces') {
            const limit = clampLimit(q.limit, 20, 50);
            if (q.session) {
                // PAGING (2026-07-15 — the magenta-ground triage): this view was
                // ORDER BY received_at DESC LIMIT 20 with no offset, so a long session
                // (one real session ran 2840 batches / 153k lines) could only ever be read
                // from its TAIL — which is gameplay spam. The lines that actually diagnose a
                // bug — scene load: TERRAINDIAG, MagentaGuard/FloorDiag, catalog + Resources
                // resolution — are emitted in the FIRST batches and were unreachable. The
                // trace pipe recorded the answer and the reader could not see it.
                // order=asc  -> oldest first = the scene-load head (use this to triage).
                // offset=N   -> page deeper in either direction.
                const offset = clampOffset(q.offset);
                const asc = String(q.order || 'desc').toLowerCase() === 'asc';
                // ORDER BY direction cannot be parameterized in a tagged template (same
                // constraint the table probes above call out), so the two directions are
                // separate literal queries rather than interpolated SQL.
                const rows = asc
                    ? await sql`
                        SELECT event_id, received_at,
                               properties->>'build'   AS build,
                               properties->>'session' AS session,
                               jsonb_array_length(COALESCE(properties->'lines', '[]'::jsonb)) AS line_count,
                               properties->'lines'    AS lines
                        FROM analytics_events
                        WHERE event_name = 'web_trace'
                          AND properties->>'session' = ${String(q.session)}
                        ORDER BY received_at ASC
                        OFFSET ${offset} LIMIT ${limit}`
                    : await sql`
                        SELECT event_id, received_at,
                               properties->>'build'   AS build,
                               properties->>'session' AS session,
                               jsonb_array_length(COALESCE(properties->'lines', '[]'::jsonb)) AS line_count,
                               properties->'lines'    AS lines
                        FROM analytics_events
                        WHERE event_name = 'web_trace'
                          AND properties->>'session' = ${String(q.session)}
                        ORDER BY received_at DESC
                        OFFSET ${offset} LIMIT ${limit}`;
                // Total batches for the session, so a caller knows how far it can page
                // instead of guessing where the session ends.
                const totalRow = await sql`
                    SELECT COUNT(*)::bigint AS batches
                    FROM analytics_events
                    WHERE event_name = 'web_trace'
                      AND properties->>'session' = ${String(q.session)}`;
                const total = totalRow && totalRow[0] ? Number(totalRow[0].batches) : null;
                return res.status(200).json({
                    view: 'traces', session: String(q.session),
                    order: asc ? 'asc' : 'desc', offset: offset, limit: limit,
                    total_batches: total,
                    returned: rows.length,
                    has_more: total != null ? (offset + rows.length) < total : null,
                    rows: rows,
                });
            }
            // No session given → latest sessions summary so the owner can pick one.
            const rows = await sql`
                SELECT properties->>'session' AS session,
                       MAX(properties->>'build') AS build,
                       COUNT(*)::bigint AS batches,
                       SUM(jsonb_array_length(COALESCE(properties->'lines', '[]'::jsonb)))::bigint AS total_lines,
                       MAX(received_at) AS latest
                FROM analytics_events
                WHERE event_name = 'web_trace'
                  AND received_at > NOW() - INTERVAL '7 days'
                GROUP BY 1
                ORDER BY 5 DESC
                LIMIT ${limit}`;
            return res.status(200).json({ view: 'traces', sessions: rows, limit: limit });
        }

        // -------------------------------------------------------------- purchases
        // ⭐ THE OPS VIEW FOR REAL MONEY (WO-1169, owner ask 2026-08-24): "i want to make sure on
        // the other side, in ops, i can see transaction data and understand if the transaction and
        // the grant happen. if grant fails i need ability to repush, or verify they received".
        //
        // ⛔ THE ONE COLUMN THAT ANSWERS IT IS `status`, and the state machine already existed:
        //     verified      -> the chain transaction is PROVEN and the money is ours...
        //                      ...but the client never confirmed it persisted the grant.
        //                      ⚠ THIS IS THE PAID-BUT-NOT-GRANTED ROW. It is the only row shape
        //                      that can cost a real player real money for nothing, and until now
        //                      NOTHING IN THE PROJECT COULD SEE IT.
        //     fulfilled     -> the client acknowledged the grant landed. Money and goods agree.
        //     manual_review -> verification found something it would not decide alone.
        //
        // `unfulfilled_minutes` is computed here rather than left to the reader: "verified 3
        // minutes ago" is a purchase in flight, "verified 3 DAYS ago" is a player owed goods. Same
        // status, opposite urgency, and a human scanning timestamps will miss it.
        //
        // READ-ONLY, like every other view here. Re-granting is a WRITE and deliberately does NOT
        // live in this file (see the note at the top): a surface that can mint entitlements needs
        // its own auth, its own audit row and its own review. Reconcile (api/purchases/reconcile.js)
        // is the player-initiated restore and can never CREATE an entitlement, which is correct.
        if (view === 'purchases') {
            const limit = clampLimit(q.limit, 25, 100);
            const offset = clampOffset(q.offset);

            // Only the unfulfilled, when asked — the working queue rather than the ledger.
            const rows = String(q.state || '').toLowerCase() === 'unfulfilled'
                ? await sql`
                    SELECT entitlement_id, tx_signature, wallet, sku, network, currency,
                           status, verified_at, fulfilled_at, quote_ref,
                           usd_anchor, usd_rate, rate_source,
                           expected_lamports::text  AS expected_base_units,
                           observed_lamports::text  AS observed_base_units,
                           ROUND(EXTRACT(EPOCH FROM (NOW() - verified_at)) / 60)::bigint
                               AS unfulfilled_minutes
                    FROM purchase_entitlements
                    WHERE status <> 'fulfilled'
                    ORDER BY verified_at DESC
                    LIMIT ${limit} OFFSET ${offset}`
                : await sql`
                    SELECT entitlement_id, tx_signature, wallet, sku, network, currency,
                           status, verified_at, fulfilled_at, quote_ref,
                           usd_anchor, usd_rate, rate_source,
                           expected_lamports::text  AS expected_base_units,
                           observed_lamports::text  AS observed_base_units,
                           CASE WHEN status = 'fulfilled' THEN NULL
                                ELSE ROUND(EXTRACT(EPOCH FROM (NOW() - verified_at)) / 60)::bigint
                           END AS unfulfilled_minutes
                    FROM purchase_entitlements
                    ORDER BY verified_at DESC
                    LIMIT ${limit} OFFSET ${offset}`;

            // The one-line health read, so an operator does not have to tally rows by eye.
            const summary = await sql`
                SELECT status, COUNT(*)::bigint AS rows,
                       MIN(verified_at) AS oldest, MAX(verified_at) AS newest
                FROM purchase_entitlements
                GROUP BY status
                ORDER BY status`;

            // ⚠ EXPECTED vs OBSERVED IS THE INTEGRITY CHECK, not decoration. /verify refuses a
            // mismatch, so a row where these differ should be impossible — surface the count so
            // "impossible" is something we can SEE rather than something we assume.
            const mismatched = await sql`
                SELECT COUNT(*)::bigint AS rows
                FROM purchase_entitlements
                WHERE expected_lamports <> observed_lamports`;

            return res.status(200).json({
                view: 'purchases',
                summary,
                amount_mismatches: Number(mismatched[0] && mismatched[0].rows) || 0,
                rows,
                limit, offset,
                legend: {
                    verified: 'PAID, grant NOT confirmed - the player may be owed goods',
                    fulfilled: 'paid and the client confirmed the grant landed',
                    manual_review: 'verification declined to decide - needs a human',
                },
            });
        }

        // ------------------------------------------------------------- bugreports
        // WO-846: newest bug reports for the bugreport-watch daemon. after_id => the
        // incremental cursor (rows STRICTLY newer, ascending); without it => latest
        // rows descending (baseline read). screenshotB64 is returned as a presence
        // flag only - the blob can be ~420K chars and never belongs in a poll.
        // ⭐ `wallet` is read as COALESCE(column, context->>'verifiedWallet') because
        // api/bug-report.js's `no_wallet` fallback shape folds the verified wallet
        // into context when the column does not exist yet. This repo has no
        // migration runner -- a deploy reaches production before a human runs the
        // SQL file -- so for that window the reports are real and the wallet is
        // real, it simply lives one level down. Without the COALESCE those rows
        // would read as unverified and the correlation this column exists for would
        // silently miss exactly the reports filed during a migration gap.
        // ⚠ Both sources are SERVER-VERIFIED. Neither ever holds a client claim.
        if (view === 'bugreports') {
            const limit = clampLimit(q.limit, 20, 100);
            const afterId = parseInt(q.after_id, 10);
            const rows = (Number.isFinite(afterId) && afterId > 0)
                ? await sql`
                    SELECT report_id, created_at, description, route, app_version, player_id,
                           COALESCE(wallet, context->>'verifiedWallet') AS wallet,
                           context->>'platform'  AS platform,
                           context->>'sessionId' AS session_id,
                           context->'traceTail'  AS trace_tail,
                           (context ? 'screenshotB64' AND context->>'screenshotB64' IS NOT NULL) AS has_screenshot
                    FROM bug_reports
                    WHERE report_id > ${afterId}
                    ORDER BY report_id ASC
                    LIMIT ${limit}`
                : await sql`
                    SELECT report_id, created_at, description, route, app_version, player_id,
                           COALESCE(wallet, context->>'verifiedWallet') AS wallet,
                           context->>'platform'  AS platform,
                           context->>'sessionId' AS session_id,
                           context->'traceTail'  AS trace_tail,
                           (context ? 'screenshotB64' AND context->>'screenshotB64' IS NOT NULL) AS has_screenshot
                    FROM bug_reports
                    ORDER BY report_id DESC
                    LIMIT ${limit}`;
            return res.status(200).json({ view: 'bugreports', rows: rows });
        }

        // -------------------------------------------------------------- bugreport
        // ONE full report by id — the read path for "a tester submitted a bug from
        // Settings; where is the stack trace?". Unlike the list view this returns
        // the ENTIRE traceTail and the screenshot length (the base64 blob itself is
        // returned only with shot=1, because it can be ~420K chars and will wreck a
        // terminal that was not asking for it).
        if (view === 'bugreport') {
            const id = parseInt(q.id, 10);
            if (!Number.isFinite(id) || id <= 0) {
                return res.status(400).json({ error: 'bugreport view requires ?id=<report_id>' });
            }
            const wantShot = String(q.shot || '') === '1';
            const rows = await sql`
                SELECT report_id, created_at, description, route, app_version, player_id,
                       context->>'platform'  AS platform,
                       context->>'sessionId' AS session_id,
                       context->'traceTail'  AS trace_tail,
                       COALESCE(length(context->>'screenshotB64'), 0) AS screenshot_b64_len,
                       context->>'screenshotDropped' AS screenshot_dropped
                FROM bug_reports
                WHERE report_id = ${id}
                LIMIT 1`;
            if (rows.length === 0) return res.status(200).json({ view: 'bugreport', id: id, rows: [] });
            if (wantShot) {
                const shot = await sql`
                    SELECT context->>'screenshotB64' AS screenshot_b64
                    FROM bug_reports WHERE report_id = ${id} LIMIT 1`;
                rows[0].screenshot_b64 = shot && shot[0] ? shot[0].screenshot_b64 : null;
            }
            return res.status(200).json({ view: 'bugreport', id: id, rows: rows });
        }

        // ------------------------------------------------------------ authrejects
        // THE READ PATH FOR THE STRUCTURED AUTH ERRORS (2026-08-02).
        // Every refusal from /api/game/save, /api/game/load and /api/auth/nonce
        // writes an 'api_auth_reject' row (api/_lib/audit.js) carrying the stable
        // code, the correlation ref echoed to the client, the rail, and non-secret
        // detail. This is what turns "cloud save is broken" into "17 x
        // AUTH_HEADERS_MISSING on /api/game/save in the last hour".
        //
        // It ALSO reads the LEGACY 'auth_failed' rows the old save.js wrote
        // (properties.reason instead of properties.code). There were 1039 of them
        // on 2026-08-02 alone — the already-captured proof that the client was
        // reaching /api/game/save and being refused — and nothing could read them,
        // because the only event view here was web_trace. COALESCE maps the old
        // `reason` onto `code` so both eras answer one query.
        //
        // ⛔ AND IT READS 'save_reset_refused' TOO (WO-1745). api/game/save.js has written a
        // durable row on every 409 SAVE_RESET_STALE since WO-1598 landed (2026-09-07) — but
        // under an event name this IN list did not contain, so NO admin query in the product
        // could see one. WO-1742 §3 read "zero refusals on /api/game/save in seven days" off
        // this very view, and that was a VIEW ARTIFACT, not a measurement. save.js now writes
        // the refusal through logAuthReject like every other refusal; this third name is kept
        // so the HISTORICAL rows (2026-09-07 → the WO-1745 deploy) answer the same query,
        // exactly as 'auth_failed' does for the pre-2026-08-02 era. A frozen cloud row is
        // otherwise INVISIBLE on Android — WebTrace only POSTs under UNITY_WEBGL — so this
        // view is the single platform-blind detector for it.
        //   ?code=SAVE_RESET_STALE  → "how many devices are frozen, and which";
        //                             `distinct_ids` in the summary IS that number
        //   ?code=<CODE>  filter to one failure class
        //   ?ref=<ref>    resolve one player-reported ref to its row
        //   ?since_hours=N  (default 24, max 168)
        if (view === 'authrejects') {
            const limit = clampLimit(q.limit, 50, 200);
            const hours = clampLimit(q.since_hours, 24, 168);

            if (q.ref) {
                const rows = await sql`
                    SELECT event_id, received_at, player_id,
                           COALESCE(properties->>'code', properties->>'reason') AS code,
                           properties->>'ref'    AS ref,
                           properties->>'mode'   AS mode,
                           properties->>'path'   AS path,
                           properties->>'method' AS method,
                           properties->>'ipHash' AS ip_hash,
                           properties->'detail'  AS detail
                    FROM analytics_events
                    WHERE event_name IN ('api_auth_reject', 'auth_failed', 'save_reset_refused')
                      AND properties->>'ref' = ${String(q.ref)}
                    ORDER BY received_at DESC
                    LIMIT 20`;
                return res.status(200).json({ view: 'authrejects', ref: String(q.ref), rows: rows });
            }

            // Summary first — the shape of the failure is usually the whole answer.
            const summary = await sql`
                SELECT COALESCE(properties->>'code', properties->>'reason')  AS code,
                       -- THE FALLBACK LABEL IS PER-ERA, NOT ONE STRING. A pre-WO-1745
                       -- save_reset_refused row also carries no path property, and calling
                       -- it '(legacy auth_failed)' would answer "how often is this
                       -- happening" with the wrong endpoint entirely.
                       -- (No backticks in here: this comment sits inside a JS template
                       --  literal, and one would terminate the query string.)
                       COALESCE(properties->>'path',
                                CASE event_name
                                    WHEN 'save_reset_refused' THEN '/api/game/save (pre-1745)'
                                    ELSE '(legacy auth_failed)'
                                END)                                         AS path,
                       COALESCE(properties->>'mode', 'legacy')               AS mode,
                       COUNT(*)::bigint AS hits,
                       COUNT(DISTINCT player_id)::bigint AS distinct_ids,
                       MAX(received_at) AS latest
                FROM analytics_events
                WHERE event_name IN ('api_auth_reject', 'auth_failed', 'save_reset_refused')
                  AND received_at > NOW() - (${hours} * INTERVAL '1 hour')
                GROUP BY 1, 2, 3
                ORDER BY 4 DESC
                LIMIT 50`;

            const rows = q.code
                ? await sql`
                    SELECT event_id, received_at, player_id,
                           COALESCE(properties->>'code', properties->>'reason') AS code,
                           properties->>'ref'    AS ref,
                           properties->>'mode'   AS mode,
                           properties->>'path'   AS path,
                           properties->>'ipHash' AS ip_hash,
                           properties->'detail'  AS detail
                    FROM analytics_events
                    WHERE event_name IN ('api_auth_reject', 'auth_failed', 'save_reset_refused')
                      AND COALESCE(properties->>'code', properties->>'reason') = ${String(q.code)}
                      AND received_at > NOW() - (${hours} * INTERVAL '1 hour')
                    ORDER BY received_at DESC
                    LIMIT ${limit}`
                : await sql`
                    SELECT event_id, received_at, player_id,
                           COALESCE(properties->>'code', properties->>'reason') AS code,
                           properties->>'ref'    AS ref,
                           properties->>'mode'   AS mode,
                           properties->>'path'   AS path,
                           properties->>'ipHash' AS ip_hash,
                           properties->'detail'  AS detail
                    FROM analytics_events
                    WHERE event_name IN ('api_auth_reject', 'auth_failed', 'save_reset_refused')
                      AND received_at > NOW() - (${hours} * INTERVAL '1 hour')
                    ORDER BY received_at DESC
                    LIMIT ${limit}`;

            return res.status(200).json({
                view: 'authrejects', window_hours: hours,
                code: q.code ? String(q.code) : null,
                summary: summary, rows: rows,
            });
        }

        // ----------------------------------------------------------------- events
        // ⭐ THE GENERIC PAYLOAD READ (WO-1793, 2026-09-16). Every view above this one
        // answers a question somebody already knew to ask: `metrics` counts events,
        // `traces` reads 'web_trace', `authrejects` reads three named refusals. The
        // question nobody could answer was the plainest one — "what ARE those 424 rows?"
        // 424 `playtest_break` rows landed on 2026-09-16 and not one of them was
        // readable without DATABASE_URL. This is the same class of hole WO-1745 already
        // closed one table over: a view that does not contain the event name reports
        // ZERO and reads as a measurement.
        //
        // `name` and `group` NEVER reach SQL as text. `name` is a bound parameter;
        // `group` selects between literal queries written out in full, exactly the way
        // the traces view handles ORDER BY direction (a tagged template cannot
        // parameterize an expression, so the alternatives are spelled out, not built).
        if (view === 'events') {
            const name = q.name ? String(q.name) : '';
            if (!name) {
                return res.status(400).json({
                    error: 'events view requires ?name=<event_name> (e.g. playtest_break). '
                         + 'Use view=metrics to see which names exist.',
                });
            }
            const limit = clampLimit(q.limit, 50, 200);
            const hours = clampLimit(q.since_hours, 24, 168);
            // NULL means "no filter". The cast is required so Postgres can type the
            // parameter when it IS null; the same bound value is reused for the compare,
            // so a player id is never interpolated.
            const player = q.player ? String(q.player) : null;
            const GROUPS = ['rows', 'message', 'kind', 'player'];
            const rawGroup = String(q.group || 'rows').toLowerCase();
            const group = GROUPS.indexOf(rawGroup) >= 0 ? rawGroup : 'rows';

            // The window total, always — so a group view's rows are read against the
            // size of the thing they partition rather than against a guess.
            const totalRow = await sql`
                SELECT COUNT(*)::bigint AS hits,
                       COUNT(DISTINCT player_id)::bigint AS distinct_ids,
                       MIN(received_at) AS oldest, MAX(received_at) AS newest
                FROM analytics_events
                WHERE event_name = ${name}
                  AND received_at > NOW() - (${hours} * INTERVAL '1 hour')
                  AND (${player}::text IS NULL OR player_id = ${player})`;

            let rows;
            if (group === 'kind') {
                // The split that matters for playtest_break: on 2026-09-16, of 424 rows
                // 330 were kind='error', 74 'scene_loaded', 11 'note', 8
                // 'possible_softlock', 1 'idle' — i.e. a fifth of the "424 breaks" are
                // scene-load bookkeeping and not breaks at all. A raw count hides that.
                rows = await sql`
                    SELECT COALESCE(properties->>'kind', '(none)') AS kind,
                           COUNT(*)::bigint AS hits,
                           COUNT(DISTINCT player_id)::bigint AS distinct_ids,
                           MAX(received_at) AS latest
                    FROM analytics_events
                    WHERE event_name = ${name}
                      AND received_at > NOW() - (${hours} * INTERVAL '1 hour')
                      AND (${player}::text IS NULL OR player_id = ${player})
                    GROUP BY 1
                    ORDER BY 2 DESC
                    LIMIT ${limit}`;
            } else if (group === 'message') {
                // COARSE PREFIX, NOT THE RAW MESSAGE. Grouping on the full message
                // fragments uselessly: on 2026-09-16 '[Flow:RaidArt]' alone split into
                // eleven 6-hit rows, while the prefix collapsed them to ONE row of 156.
                // A grouping that does not collapse is a list with extra steps.
                rows = await sql`
                    SELECT CASE
                               WHEN properties->>'message' LIKE '[Flow:%'
                                   THEN split_part(properties->>'message', ']', 1) || ']'
                               ELSE split_part(COALESCE(properties->>'message', '(none)'), ' ', 1)
                           END AS message_prefix,
                           COUNT(*)::bigint AS hits,
                           COUNT(DISTINCT player_id)::bigint AS distinct_ids,
                           MAX(received_at) AS latest
                    FROM analytics_events
                    WHERE event_name = ${name}
                      AND received_at > NOW() - (${hours} * INTERVAL '1 hour')
                      AND (${player}::text IS NULL OR player_id = ${player})
                    GROUP BY 1
                    ORDER BY 2 DESC
                    LIMIT ${limit}`;
            } else if (group === 'player') {
                // ⚠ app_versions IS A JOIN, NOT A COLUMN ON THE ROW. BreakRecord carries
                // no build (see the legend below), so the only build attribution available
                // is that player's session_start events inside the SAME window — and it is
                // a SET, because a player who booted two builds has two.
                rows = await sql`
                    SELECT e.player_id,
                           COUNT(*)::bigint AS hits,
                           MIN(e.received_at) AS first_seen,
                           MAX(e.received_at) AS last_seen,
                           (SELECT array_agg(DISTINCT s.properties->>'appVersion')
                              FROM analytics_events s
                             WHERE s.event_name = 'session_start'
                               AND s.player_id = e.player_id
                               AND s.received_at > NOW() - (${hours} * INTERVAL '1 hour')
                               AND s.properties->>'appVersion' IS NOT NULL) AS app_versions
                    FROM analytics_events e
                    WHERE e.event_name = ${name}
                      AND e.received_at > NOW() - (${hours} * INTERVAL '1 hour')
                      AND (${player}::text IS NULL OR e.player_id = ${player})
                    GROUP BY 1
                    ORDER BY 2 DESC
                    LIMIT ${limit}`;
            } else {
                // TRUNCATED, DELIBERATELY. A stack is unbounded and a poll must never
                // return one in full. `props` is the rest of the payload with the two
                // unbounded fields removed — that is what makes this view generic: a
                // save_reset_accepted row answers from/to/ref/mode through the same query
                // that answers kind/scene/message for a playtest_break.
                rows = await sql`
                    SELECT event_id, received_at, player_id,
                           properties->>'kind'  AS kind,
                           properties->>'scene' AS scene,
                           left(properties->>'message', 400) AS message,
                           left(properties->>'stack', 400)   AS stack,
                           length(properties->>'stack')      AS stack_len,
                           (properties - 'message' - 'stack') AS props
                    FROM analytics_events
                    WHERE event_name = ${name}
                      AND received_at > NOW() - (${hours} * INTERVAL '1 hour')
                      AND (${player}::text IS NULL OR player_id = ${player})
                    ORDER BY received_at DESC
                    LIMIT ${limit}`;
            }

            const t = (totalRow && totalRow[0]) || {};
            return res.status(200).json({
                view: 'events',
                name: name,
                group: group,
                requested_group: rawGroup === group ? null : rawGroup,
                window_hours: hours,
                limit: limit,
                player: player,
                window_total: {
                    hits: Number(t.hits) || 0,
                    distinct_ids: Number(t.distinct_ids) || 0,
                    oldest: t.oldest || null,
                    newest: t.newest || null,
                },
                returned: rows.length,
                rows: rows,
                legend: {
                    // §3 of the ticket: STATE the ambiguity, do not imply it. A number
                    // whose ambiguity is not printed will be read as certain — the same
                    // reason the purchases view spells out verified vs fulfilled.
                    app_version: 'NOT IN THE PAYLOAD. BreakCaptureHarness.Record builds '
                        + '{kind,message,stack,scene,t,utc} with no build string; only '
                        + 'session_start carries appVersion. Any build attribution here is a '
                        + 'per-player JOIN over this window and is AMBIGUOUS for a player who '
                        + 'booted two builds in it (group=player returns the SET, not one value).',
                    message: 'truncated to 400 chars; stack_len is the untruncated stack length',
                    props: 'the event payload MINUS message and stack (both unbounded)',
                    group: 'rows | message (coarse prefix) | kind | player; anything else falls back to rows',
                },
            });
        }

        // ----------------------------------------------------------------- funnel
        // The per-player funnel, in one query. Not a per-event view: the question it
        // answers is "who played, when did they arrive, and how far did they get",
        // which is the SET of event names an id emitted. Capped at 200 ids and
        // aggregate-only — no payloads leave the DB here.
        if (view === 'funnel') {
            const hours = clampLimit(q.since_hours, 24, 168);
            const limit = clampLimit(q.limit, 200, 200);
            const rows = await sql`
                SELECT player_id,
                       MIN(received_at) AS first_seen,
                       MAX(received_at) AS last_seen,
                       COUNT(*)::bigint AS events,
                       COUNT(DISTINCT event_name)::bigint AS distinct_event_names,
                       array_agg(DISTINCT event_name) AS event_names
                FROM analytics_events
                WHERE received_at > NOW() - (${hours} * INTERVAL '1 hour')
                GROUP BY 1
                ORDER BY 2 DESC
                LIMIT ${limit}`;
            return res.status(200).json({
                view: 'funnel',
                window_hours: hours,
                limit: limit,
                returned: rows.length,
                rows: rows,
                legend: {
                    event_names: 'the DISTINCT set this id emitted in the window, not an ordered path',
                    truncation: 'ordered by first_seen DESC and capped — a window with more ids than '
                        + 'the cap returns the most recent arrivals only; narrow since_hours to see the rest',
                    app_version: 'use view=events&name=session_start to read appVersion; no other '
                        + 'event payload carries a build',
                },
            });
        }

        return res.status(400).json({
            // WO-1796: `ads` is named here in the SAME edit that added the view, so this
            // message can never lie about what the endpoint serves. Add a view, add its
            // name here — an omitted name reads to the caller as "not supported".
            error: 'Unknown view. Use: overview | players | metrics | ads | traces | bugreports | bugreport '
                 + '| authrejects | purchases | events | funnel',
        });
    } catch (err) {
        console.error('[admin/db] error:', err);
        return res.status(500).json({ error: 'Internal server error' });
    }
};

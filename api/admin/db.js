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

        return res.status(400).json({
            error: 'Unknown view. Use: overview | players | metrics | traces | bugreports | bugreport | authrejects',
        });
    } catch (err) {
        console.error('[admin/db] error:', err);
        return res.status(500).json({ error: 'Internal server error' });
    }
};

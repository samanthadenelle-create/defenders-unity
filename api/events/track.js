// =============================================================================
// api/events/track.js — Vercel Serverless Function
// -----------------------------------------------------------------------------
// Receives a batch of analytics events from the Unity client (EventTracker.cs)
// and inserts one row per event into the analytics_events table.
//
// Client : Assets/_Modules/Core/Analytics/EventTracker.cs
//   POST  application/json
//   Body  : { "events": [ { "eventName", "properties", "clientTs" }, … ] }
//           - eventName  string  (snake_case)
//           - properties string  (JSON STRING — parsed to JSONB on insert)
//           - clientTs   long    (unix epoch MILLISECONDS)
//   Reply : { "success": true }   (client is fire-and-forget; only checks 2xx)
//
// ⛔ THE BODY MAY NEVER NAME A WALLET (WO-1506). It used to: the row's player_id
//    came straight off each event as "BoundWallet | anonymous", with no auth and no
//    rate limit on the route, so ANY caller could write unbounded rows attributed to
//    ANY wallet — and those rows feed the retention and funnel numbers the owner
//    makes business decisions from. The row is bound to the CALLER instead:
//
//      X-Session   → wallet-auth.verifySession names the wallet   → _auth:'session'
//      X-Guest-Id  → a guest-shaped id binds to itself            → _auth:'guest'
//      body guest  → a GUEST-SHAPED body playerId, no header      → _auth:'guest-body'
//      neither     → the literal id `unverified`                  → _auth:'unverified'
//                    (+ properties._claimedId when the caller named a non-guest id —
//                     WO-1791, non-authoritative, never an identity. See claimedIdOf.)
//
// ⚠ THE `body guest` RAIL IS WO-1733, AND IT IS NARROW ON PURPOSE. Between
//    2026-09-07T10:45Z and that fix EVERY event from EVERY player landed under
//    `unverified`, because the Unity client sent NEITHER header.
//    Measured on the live DB: one "player" with 218 sessions, daily actives pinned
//    at 1 while sessions ran 14-42/day. The CLIENT fix is the correct long-term one,
//    but it only ever reaches builds shipped AFTER it; the store build in players'
//    hands then (2026.08.17.328845) would stay dark forever. The server fallback
//    reaches every build already installed, on the next API deploy.
//
// ⛔ THE "CLIENT SENDS NEITHER HEADER" CLAIM IS HISTORY, NOT CURRENT STATE.
//    Corrected 2026-09-17 (WO-1791 §2, which named this comment stale by name).
//    WO-1735 shipped the client attach: EventTracker.SendBatch
//    (Assets/_Modules/Core/Analytics/EventTracker.cs:293-333) now calls
//    BackendRequestSigner.TryAttachCachedSession(req, identityPlayerId) and traces
//    the mode via ReportIdentityMode (:380-415). It is PROVEN working on the live DB:
//    every session_start from build 2026.09.16.371701 on 2026-09-16 landed as
//    _auth:'session' (3 ids) or _auth:'guest' (6 ids). Read the .cs, never a line
//    number quoted in this comment (CLAUDE.md §11B.A).
//
//    `unverified` is ONE bucket on purpose: a single entry in
//    ANALYTICS_EXCLUDED_PLAYER_IDS (api/admin/stats.js excludedPlayerIds) then
//    removes every unproven row from the Command Center at once. Pre-wallet funnel
//    traffic still LANDS — the route never rejects it — it just cannot forge an
//    identity while doing so.
//
// ⚠ THE GUEST RAIL HERE IS BEARER TRUST, AND DELIBERATELY DOES NOT CALL
//    verifyGuest(). That helper spends guest_rate_limit, which wallet-auth.js keys
//    on the guest id alone and SHARES with game/save + game/load (30 per 60s). A
//    busy analytics flush would therefore rate-limit the player's own saves. The
//    IP budget below is this route's rate control instead.
//
// ⚠ WO §4 acceptance 2 asked for a "server-minted guest id" for anonymous events.
//    There is no minting helper in this project — a guest id is minted on the
//    DEVICE (GameStateService.EnsureAccount) — and a per-request server id would be
//    either unbounded cardinality or forgeable. Anonymous traffic is TAGGED
//    `unverified` instead, per the implementing lane's instruction. Stated rather
//    than quietly reinterpreted (CLAUDE.md §11B).
//
// Rate limit: the project's ONE budget helper, api/_lib/ip-budget.js (WO-1456),
// scope 'EVENTS_TRACK', FAIL-OPEN — analytics is not allowed to take the game down,
// so an unreadable budget table degrades to "allow" and says so loudly.
//
// Driver: @neondatabase/serverless   (same as game/save.js, game/load.js)
// Status codes: 200 | 400 | 500   (project constraint — no others). A budget
// refusal answers 200 {success:false,error:'RATE_LIMITED'} — the same shape
// promo/redeem.js uses — because EventTracker.FlushWithRetry retries any non-2xx
// four times with backoff, and an over-budget attempt still increments the counter.
//
// ⚠ FOLLOW-UPS THIS SILO CANNOT MAKE:
//   1. ✅ CLOSED by WO-1735 — the client attach shipped; see the corrected note at the
//      top of this header. `_auth:'guest-body'` counts falling toward zero is the
//      signal it landed, and on 2026-09-16 every guest-body row came from the two
//      pre-fix builds (.359722 / .359670).
//   1b. THE PRE-WO-1735 COHORT IS UNFIXABLE FROM EITHER SIDE. 7 of 2026-09-16's 17
//      attributable sessions ran 2026.09.07.359722, which attaches no header at all;
//      their wallet-phase events can only ever carry a _claimedId, never a proven id.
//      That is a store-update / cohort-retirement question, not a code fix, and the
//      historical `unverified` bucket must NEVER be read as one player (WO-1791 §4.4).
//   1c. ONE HUMAN STILL COUNTS AS TWO ids on EVERY build, because the guest id is
//      replaced by the wallet id mid-session with no alias event to stitch them
//      (7 measured guest→wallet pairs on 2026-09-16, 1-24 s apart). The fix is a
//      CLIENT `identity_bound { from, to }` event — WO-1791 §4.2, Unity silo, NOT
//      this file. Note the coupling: EventTracker.Enqueue captures PlayerId at
//      ENQUEUE time while the headers attach at FLUSH time, so an identity_bound
//      emitted right after GameStateService.BindWallet can beat its own session into
//      existence and land here unverified — `_claimedId` is exactly the net that
//      keeps that alias row readable.
//   2. `unverified` IS NOT AUTO-EXCLUDED. api/admin/stats.js excludedPlayerIds()
//      hardcodes only ANON_ID as always-excluded, so this bucket counts as one
//      "player" in retention until either ANALYTICS_EXCLUDED_PLAYER_IDS=unverified
//      is set on the deployment, or that array gains it (out of this silo).
//
// STALE ELSEWHERE (out of this silo, named per §15): api/schema.sql:319 and :336
// still document player_id as "BoundWallet | anonymous". docs/MASTER_CATALOG was
// checked and does NOT repeat that claim (core.md:702 describes the client only).
// =============================================================================

const { neon } = require('@neondatabase/serverless');
const { verifySession, isGuestId, guestEnabled } = require('../_lib/wallet-auth');
const { reserveIpBudget } = require('../_lib/ip-budget');
const { hashIp } = require('../_lib/audit');

// Hard ceiling on one POST. The client batches a handful of events; anything past
// this is either a bug or an attempt to make one request do unbounded DB work.
// Surplus events are DROPPED and the count is reported, never silently eaten.
const MAX_EVENTS_PER_BATCH = 100;

// The one id every unproven row shares. Never a real player.
const UNVERIFIED_PLAYER_ID = 'unverified';

// WO-1791 Hole 1. Longest claimed id we will copy into properties._claimedId. A
// Solana address is 32-44 chars and a play- id is 69; the cap exists only so a
// hostile caller cannot push unbounded text into JSONB through this field.
const MAX_CLAIMED_ID_LEN = 128;

// Ids that carry no attribution value and must never be written as a claim.
const WORTHLESS_CLAIMED_IDS = new Set(['anonymous', UNVERIFIED_PLAYER_ID, 'null', 'undefined']);

/**
 * WO-1791 — the NON-AUTHORITATIVE claim on an `unverified` row.
 *
 * ⛔ THIS IS NOT A NEW IDENTITY RAIL AND MUST NEVER BECOME ONE. The row's
 *    `player_id` stays the literal `unverified` and its `_auth` stays
 *    `'unverified'`; resolveIdentity is not consulted, not extended, and not
 *    changed. All this does is preserve, in a property no server code reads as an
 *    identity, WHICH id the caller claimed — so a funnel can be reconstructed by a
 *    human reading the dashboard without anything ever trusting the string.
 *    (Grepped 2026-09-17: `properties->>` appears throughout api/admin/db.js and
 *    api/_lib/ops.js for revenue/session/code fields, and NOTHING anywhere in api/
 *    reads a properties field as a player identity — player_id is the only identity
 *    column. `_claimedId` had zero readers before this change.)
 *
 * WHY IT IS WORTH ANYTHING: measured 2026-09-16, the live `unverified` bucket held
 * 520 rows under ONE row id — five separate humans' first sessions, four different
 * appVersion strings, 327 playtest_break rows and all 8 possible_softlock rows,
 * none of them attributable to a player. The client-side header attach (WO-1735)
 * closes this for new builds only; this field reaches the ALREADY-INSTALLED cohort
 * on the next API deploy and changes no trust boundary, which is why it was chosen
 * over a client-side flush retry (that reaches no installed build and risks dropping
 * telemetry — the reason EventTracker.cs:308-320 deliberately does not abort).
 *
 * The guest shape is EXCLUDED on purpose: a guest-shaped body id either already
 * resolved through the WO-1733 rail (so it is the row's real player_id, not a
 * claim), or GUEST_SAVE_ENABLED is off — and a kill switch that leaves the same
 * value flowing through a second field is not a kill switch.
 *
 * @returns the claimed id string, or null when there is nothing worth recording.
 */
function claimedIdOf(rawId) {
    if (typeof rawId !== 'string') return null;
    const id = rawId.trim();
    if (!id) return null;
    if (id.length > MAX_CLAIMED_ID_LEN) return null;
    if (WORTHLESS_CLAIMED_IDS.has(id.toLowerCase())) return null;
    if (isGuestId(id)) return null;
    return id;
}

// Per caller IP, per minute. The client's own flush cadence is a batch every few
// seconds at full tilt, so no honest device is anywhere near this; it exists to
// stop a script writing unbounded rows.
const IP_WINDOW_SECONDS = 60;
const IP_MAX_PER_WINDOW = 60;

/**
 * The FIRST guest-shaped playerId in a batch, or null.
 *
 * A flush can legitimately mix identities: events queued before
 * GameStateService.EnsureAccount carry the literal "anonymous", events after it
 * carry the minted guest id. Taking the first guest-shaped one and attributing the
 * whole batch to it is the least-lossy rule, and it forges nothing — the sender
 * already controls what it puts in the header, so it cannot reach an id here that
 * it could not have reached there.
 *
 * Called on the CAPPED batch, never the raw array: surplus events past
 * MAX_EVENTS_PER_BATCH are dropped and must not be able to steer the identity of
 * the rows that do land.
 */
function firstGuestShapedId(batch) {
    for (const ev of batch) {
        if (!ev) continue;
        const id = ev.playerId;
        if (typeof id === 'string' && isGuestId(id)) return id;
    }
    return null;
}

/**
 * Who is this caller? Never throws: a broken auth table degrades to `unverified`
 * rather than losing the event, because analytics is not a value-granting rail.
 *
 * @param bodyGuestCandidate the batch's first guest-shaped playerId, or null. Read
 *        ONLY when no identity header was accepted. See the rail rules below.
 */
async function resolveIdentity(sql, headers, sessionVerifier, bodyGuestCandidate) {
    const sessionToken = headers['x-session'];
    if (sessionToken) {
        try {
            const ses = await sessionVerifier(sql, String(sessionToken), null);
            if (ses.ok) return { playerId: ses.wallet, auth: 'session' };
            console.warn('[events/track] session offered but not valid (' + ses.code + ') — row lands unverified.');
        } catch (err) {
            console.warn('[events/track] session check failed — row lands unverified:', err && err.message);
        }
    }

    const guest = headers['x-guest-id'];
    if (guest && guestEnabled() && isGuestId(String(guest))) {
        return { playerId: String(guest), auth: 'guest' };
    }

    // ── WO-1733: the GUEST shape, and ONLY the guest shape, may come from the body ──
    //
    // ⛔ DO NOT "SIMPLIFY" THIS INTO `accept whatever playerId the body names`. The
    //    asymmetry is the whole security property, and it is not an oversight:
    //
    //    * A guest id is a 256-bit bearer credential minted on the DEVICE
    //      (GameStateService.EnsureAccount → "guest-local-" + SHA256(deviceId+salt)).
    //      The server ALREADY extends exactly this value exactly this trust when it
    //      arrives in X-Guest-Id, ~8 lines up. Accepting the same unguessable string
    //      through a different channel of the same request forges nothing NEW: an
    //      attacker who knows a guest id can already present it as the header.
    //
    //    * ⛔ A WALLET id is NOT a credential — it is a PUBLIC address. Anyone can
    //      read one off the chain or a leaderboard. Accepting a wallet-shaped id from
    //      the body would hand every caller the ability to write analytics rows under
    //      any player's wallet, which is precisely the hole WO-1506 closed. A wallet
    //      may therefore ONLY ever be established by X-Session, whose token proves a
    //      signature. Same for a play- id, whose shape is derived under a server key.
    //      isGuestId() is the gate, and it is lexically disjoint from both
    //      (wallet-auth.js:143-153).
    //
    // guestEnabled() is checked for the same reason the header path checks it: the
    // rail's kill switch must switch off the WHOLE rail, or turning it off would
    // silently leave a second door open.
    if (bodyGuestCandidate && guestEnabled() && isGuestId(bodyGuestCandidate)) {
        // Tagged distinctly from the header rail so a reader can tell the two apart:
        // it makes the post-deploy proof one GROUP BY, and when the client WO lands
        // this count falls to zero on its own. Nothing in api/ filters on _auth
        // (grepped 2026-09-15), so a new value drops no existing view.
        return { playerId: bodyGuestCandidate, auth: 'guest-body' };
    }

    return { playerId: UNVERIFIED_PLAYER_ID, auth: 'unverified' };
}

function makeHandler(deps = {}) {
    const getSql = deps.getSql || (() => neon(process.env.DATABASE_URL));
    const sessionVerifier = deps.verifySession || verifySession;

    return async (req, res) => {
        // CORS: the published app runs under <app>.pinet.com and POSTs events
        // cross-origin. The identity headers must be listed or the browser
        // preflight strips them and every row silently lands unverified.
        res.setHeader('Access-Control-Allow-Origin', '*');
        res.setHeader('Access-Control-Allow-Methods', 'POST, OPTIONS');
        res.setHeader('Access-Control-Allow-Headers', 'Content-Type, X-Session, X-Guest-Id, X-Wallet');
        if (req.method === 'OPTIONS') { return res.status(204).end(); }

        if (req.method !== 'POST') {
            return res.status(400).json({ error: 'Method not allowed' });
        }

        // ── Parse body (Vercel auto-parses application/json into req.body) ─────
        let body = req.body;
        try {
            if (typeof body === 'string') body = JSON.parse(body);
        } catch (err) {
            console.error('[events/track] Body parse error:', err);
            return res.status(400).json({ error: 'Invalid payload' });
        }

        const headers = req.headers || {};
        const events = body && Array.isArray(body.events) ? body.events : null;

        // ⚠ EVERY FREE CHECK HAPPENS BEFORE THE BUDGET IS SPENT. A malformed
        // request must never cost a household a unit — the placement rule WO-1440
        // wrote into the promo route and WO-1456 into the nonce route.
        if (!events) {
            return res.status(400).json({ error: 'Missing events array' });
        }
        if (events.length === 0) {
            // Nothing to write, but a valid request — keep the client happy.
            return res.status(200).json({ success: true, inserted: 0 });
        }

        try {
            const sql = getSql();

            // ── Build ONE multi-row insert (security audit 2026-08-15) ──────────
            // Was: uncapped array, one AWAITED round-trip per element. A single POST
            // could therefore hold a function open for thousands of sequential
            // queries. Now the batch is capped and lands in one statement.
            //
            // The cap is applied HERE, before identity, so a dropped surplus event
            // can never be the one that names the batch (WO-1733).
            const batch = events.slice(0, MAX_EVENTS_PER_BATCH);

            const identity = await resolveIdentity(
                sql, headers, sessionVerifier, firstGuestShapedId(batch),
            );

            const spend = await reserveIpBudget(sql, hashIp(req), 'EVENTS_TRACK', {
                windowSeconds: IP_WINDOW_SECONDS,
                maxPerWindow: IP_MAX_PER_WINDOW,
                failClosed: false,
                label: 'events/track',
            });
            if (!spend.ok) {
                console.warn('[events/track] REFUSED — IP budget exhausted (grants=' + (spend.grants || '?') + ').');
                return res.status(200).json({ success: false, error: spend.error || 'RATE_LIMITED', inserted: 0 });
            }

            const values = [];
            const params = [];
            // WO-1791: counted so the unverified bucket is never silent about
            // whether it kept a reconstructable claim (CLAUDE.md §12 — no silent
            // failures). Reported on the response and logged once per request.
            let claimsRecorded = 0;
            let claimsUnrecoverable = 0;
            for (const ev of batch) {
                if (!ev) continue;

                const eventName = ev.eventName != null ? String(ev.eventName) : null;
                if (!eventName) continue; // skip malformed events rather than fail the batch

                // properties arrives as a JSON STRING from the client; parse so it
                // lands as proper JSONB. Fall back to {} if it isn't valid JSON.
                let propsObj = {};
                if (ev.properties != null) {
                    if (typeof ev.properties === 'string') {
                        try { propsObj = JSON.parse(ev.properties); }
                        catch { propsObj = { _raw: ev.properties }; }
                    } else if (typeof ev.properties === 'object') {
                        propsObj = ev.properties;
                    }
                }
                // The row says how its identity was established, so a reader can
                // tell proven traffic from asserted traffic without joining anything.
                propsObj._auth = identity.auth;

                // WO-1791 Hole 1. Only on the unverified rail, and PER EVENT — one
                // flush can legitimately mix claims (a batch queued across a
                // BindWallet carries the guest id on early events and the wallet on
                // later ones), so a per-batch stamp would mislabel half the rows.
                // On every other rail the row's player_id IS the proven id, and a
                // duplicate of it in properties would be exactly the copied state
                // CLAUDE.md §2/§5/§16 name as the defect.
                if (identity.auth === 'unverified') {
                    const claimed = claimedIdOf(ev.playerId);
                    if (claimed) {
                        propsObj._claimedId = claimed;
                        claimsRecorded++;
                    } else {
                        claimsUnrecoverable++;
                    }
                }

                const clientTs = ev.clientTs != null ? Number(ev.clientTs) : null;

                // The ::jsonb cast is required because the Neon HTTP driver sends
                // parameters as strings (same pattern as game/save.js).
                const i = params.length;
                values.push(`($${i + 1}, $${i + 2}, $${i + 3}::jsonb, $${i + 4})`);
                params.push(
                    identity.playerId,
                    eventName,
                    JSON.stringify(propsObj),
                    Number.isFinite(clientTs) ? clientTs : null,
                );
            }

            if (values.length === 0) {
                return res.status(200).json({ success: true, inserted: 0 });
            }

            await sql(
                'INSERT INTO analytics_events (player_id, event_name, properties, client_ts) VALUES ' +
                values.join(', '),
                params,
            );

            // WO-1791: say out loud what landed unattributable. `claimed` rows can be
            // stitched into a funnel by hand; `unrecoverable` rows are the ones for
            // which not even a claim survived, and their count is the honest measure
            // of how much of the day is permanently anonymous.
            if (identity.auth === 'unverified' && (claimsRecorded || claimsUnrecoverable)) {
                console.warn(
                    '[events/track] UNVERIFIED batch — ' + claimsRecorded + ' row(s) kept a ' +
                    'non-authoritative _claimedId, ' + claimsUnrecoverable + ' row(s) carry no ' +
                    'claim at all. player_id is `unverified` for all of them; _claimedId is ' +
                    'NEVER an identity (WO-1791).',
                );
            }

            return res.status(200).json({
                success: true,
                inserted: values.length,
                dropped: events.length - batch.length,
                auth: identity.auth,
                claimed: claimsRecorded,
                unclaimed: claimsUnrecoverable,
            });
        } catch (err) {
            console.error('[events/track] DB error:', err);
            return res.status(500).json({ error: 'Internal server error' });
        }
    };
}

module.exports = makeHandler();
module.exports._test = { makeHandler, UNVERIFIED_PLAYER_ID };

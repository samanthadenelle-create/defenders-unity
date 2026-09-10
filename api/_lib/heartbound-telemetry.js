'use strict';

// =============================================================================
// api/_lib/heartbound-telemetry.js — WO-1684 / HEART-011.
// EVENT SHAPING AND THE WHALE-RATIO AGGREGATE. PURE. NO I/O.
// -----------------------------------------------------------------------------
// Spec: docs/specs/HEARTBOUND_SKR_RESONANCE_WORK_ORDERS_2026-09-10.md:1030-1100.
//
// ⛔ NOTHING IN THIS FILE PERFORMS I/O — no network, no database, no fs, no clock,
// no randomness, no module-scope mutable state. The two `require`s are node's
// crypto (a hash is arithmetic) and the authored bucket table next door. The one
// function that CAN reach a database, `createAnalyticsEmitter`, takes the `sql`
// client and the writer as ARGUMENTS and performs no I/O itself; calling the
// returned function is the caller's act, not this module's. Same structural
// purity heartbound-resonance.js:9-14 states for the arithmetic, for the same
// reason: the whole thing is drivable from node:test without a database.
//
// ⛔ NO NEW TABLE, NO MIGRATION. WO-1684 §5 is explicit and the house position is
// already written down at api/trace.js:12-14 — "reuses the proven analytics_events
// table… NO new table/migration required". The sink is api/_lib/audit.js:112
// `logApiEvent(sql, identity, eventName, properties)` → one INSERT into
// analytics_events (audit.js:118-127), which never throws and degrades to a
// console line when the DB is unreachable. api/migrations/ therefore gains
// NOTHING from this lane; the brief's "if none fits, write the DDL" branch was
// NOT taken and this sentence is why.
//
// -----------------------------------------------------------------------------
// ⭐ WHY A SEAM AND NOT A CALL. Every emitter here is an INJECTED `emit`
// dependency defaulting to a no-op, in the two modules that own the moments
// (heartbound-pulse.js, heartbound-state.js). That is what makes instrumentation
// addable without touching one line of existing behaviour: with no emit wired,
// every return value, every SQL statement and every test expectation is byte for
// byte what it was before — pinned by the "a throwing emit changes nothing" cases
// in test/heartbound-telemetry.test.js. CLAUDE.md §12 requires the instrument to
// go in FIRST; it does not license the instrument changing the thing it measures.
//
// -----------------------------------------------------------------------------
// ⛔ THE ONE-EMITTER RULE (WO-1684 D1: "Do not emit the same event from both sides
// — a double-counted funnel is worse than a missing one"). EVENT_OWNERS below
// names, for every one of the fourteen events, the SINGLE seam allowed to mint it.
// It is a table rather than a paragraph because a paragraph cannot be asserted,
// and test/heartbound-telemetry.test.js asserts it: the server and client sets are
// disjoint and together complete.
//
// ⚠ TWO PLACES WHERE THIS LANE READ THE TREE AND DEPARTED FROM WO-1684's PROSE.
// Both are recorded in WORK_ORDER_1684_....RESULT.md and both need the lead's nod:
//
//   1. `resonance_tier_up` / `resonance_tier_down` are SERVER events here. WO-1684
//      D1 lists them as client ("tier up/down as *seen*"). But the client never
//      computes a tier — heartbound-resonance.js:19-26 records product rule 6
//      (the backend is authoritative) and WO-1676 §5 forbids the curve in C#, so
//      the client can only display a tier it was told, and cannot observe a change
//      that happened while it was offline (a pulse is a daily cron, Q-CADENCE).
//      The authoritative transition is the one inside applyPulse. Emitting it
//      where it happens is the only place it is a fact rather than a repaint.
//
//   2. `skr_verification_success` / `skr_verification_failed` ARE ALREADY LIVE and
//      this lane did not move them. api/heartbound/status.js:258 and :350 emit both
//      names through logApiEvent today. heartbound-state.js's write seams therefore
//      carry the emit dependency (as briefed) but are wired by NOBODY: switching
//      them on for a caller that ALSO goes through /api/heartbound/status would
//      double-count the funnel D1 exists to protect. The wiring rule is written on
//      the seam itself, in state.js, next to the call.
//
// -----------------------------------------------------------------------------
// ⭐ D3 — WHICH QUERY ANSWERS EACH OF THE SPEC'S TEN QUESTIONS (:1096-1100).
// The point of the ticket is analytics that are USABLE, not merely present. Every
// query below is over tables that exist today. `ae` = analytics_events
// (api/schema.sql:370-378), `hs` = heartbound_state, `g` = player_pulse_grant,
// `p` = global_heart_pulse.
//
//   1. What percentage of players have SKR staked?
//        COUNT(DISTINCT hs.player_id WHERE hs.status='ACTIVE') over
//        COUNT(DISTINCT ae.player_id WHERE ae.event_name='session_start'
//        AND ae.received_at > now()-'30 days') — the denominator is ACTIVE players,
//        not rows in player_data, or every dormant account flatters the number down.
//   2. Does Heartbound increase retention?
//        session_start day-N return rate, split by whether that player_id has a
//        heartbound_activated event before the window. NOT a causal claim: a
//        staker is a self-selected cohort and the query cannot fix that. Say so
//        on the chart.
//   3. Which tiers contain most users?
//        SELECT resonance_tier, COUNT(*) FROM heartbound_state GROUP BY 1 — the
//        LIVE distribution. The tier_up/down events give the same axis over TIME.
//   4. Are Echo Events being claimed?
//        echo_event_claimed / echo_event_generated by day. Owned by the HEART-005
//        seam, NOT emitted by this lane (see EVENT_OWNERS).
//   5. Which events do players interact with most?
//        echo_event_claimed GROUP BY properties->>'echoEventType'.
//   6. Is Heartbound materially inflating resources?
//        NOT answerable from analytics_events, and this is a finding rather than a
//        gap to paper over: the yield modifiers live in heartbound-tiers.js
//        (in flight in another lane at the time of writing) and the resources
//        themselves are inside the client-authored save blob, which
//        api/game/save.js:410-421 calls "a RECORD, NOT A CONTROL". The honest
//        answer is the Q-METER aggregate — sum of passive tier yield percentages
//        — computed by aggregateWhaleRatio() below with a `tierBenefit` injected
//        once that module lands.
//   7. Does staking duration correlate with retention?
//        streakBucket (a day count at one pulse/day) joined to session_start
//        recency, per player.
//   8. Are users increasing stake after discovering Heartbound?
//        stakeBucket transitions per player, ordered by received_at, first seen
//        AFTER their heartbound_activated row.
//   9. ⭐ Are whales gaining disproportionate gameplay advantage?
//        THE CEILING'S EVIDENCE (WO-1682). Two halves, and they are different:
//        (a) the CURVE — aggregateWhaleRatio() over the live rows, which reports
//            advantageConcentration = scoreRatio / stakeRatio. Sub-1 means the
//            top stake bucket's score advantage is SMALLER than its stake
//            advantage: the anti-whale curve is holding. Spec :313 states the
//            intent ("500,000 SKR does NOT provide 500 times the power of 1,000")
//            and test/heartbound-telemetry.test.js pins it against the real
//            heartbound-resonance module.
//        (b) the EXACT numbers, when a person needs them:
//              SELECT g.resonance_tier, g.effective_stake, g.resonance_score
//              FROM player_pulse_grant g JOIN global_heart_pulse p USING (pulse_id)
//            — NOT analytics_events. heartbound-pulse.js:481-487 already persists
//            the exact effective stake, score and tier per (player, pulse), which
//            is precisely why the analytics row is allowed to stay bucketed
//            (acceptance 4) without losing the ability to answer question 9.
//  10. Does the Tree presentation cause players to open Heartbound?
//        heartbound_screen_opened by properties->>'source'. Owned by the HEART-008
//        client seam; NOT emitted by this lane.
//
// ⚠ D4 — KEEP THE OWNER OUT OF THE AGGREGATES. WO-1684 D4: ANALYTICS_EXCLUDED_PLAYER_IDS
// (api/admin/stats.js) filters by player_id, and player_id IS the wallet
// (api/schema.sql:60). The owner is recorded as staking ~1,000,000 SKR, so an
// unexcluded owner wallet is a visible outlier in every Heartbound tier chart.
// ⛔ THAT IS WHY `identity` ON EVERY EVENT BELOW IS THE RAW WALLET AND NOT THE HASH:
// hashing the column would make the exclusion list unable to match, and would break
// every existing per-player join in the table. The hash rides INSIDE properties as
// `playerHash`, which is what the brief asked for and what lets a chart group
// pseudonymously; the identity column keeps doing what the whole table already does.
// This lane VERIFIED NOTHING about the contents of ANALYTICS_EXCLUDED_PLAYER_IDS —
// it is an environment variable, unreadable from here. D4 is NOT ticked.
// =============================================================================

const crypto = require('node:crypto');

const DEFAULT_CONFIG = require('./heartbound-telemetry-config.json');

// ── the fourteen names, verbatim from spec :1036-1064 ────────────────────────
//
// ⛔ NOT ONE OF THESE IS `web_trace` (WO-1684 acceptance 5). api/admin/cleanup.js:79-85
// prunes analytics_events at RETENTION_DAYS = 7 KEYED ON event_name = 'web_trace'
// (:34), so a Heartbound row named into that sweep would silently vanish a week
// after it was written — and a retention question (#2, #7) asked over a swept table
// answers confidently and wrongly. Pinned by a test.

const HEARTBOUND_EVENTS = Object.freeze({
    HEARTBOUND_DETECTED: 'heartbound_detected',
    HEARTBOUND_ACTIVATED: 'heartbound_activated',
    SKR_VERIFICATION_SUCCESS: 'skr_verification_success',
    SKR_VERIFICATION_FAILED: 'skr_verification_failed',
    HEART_PULSE_GLOBAL_DETECTED: 'heart_pulse_global_detected',
    HEART_PULSE_PLAYER_PROCESSED: 'heart_pulse_player_processed',
    HEART_PULSE_PLAYER_DEFERRED: 'heart_pulse_player_deferred',
    RESONANCE_SCORE_CHANGED: 'resonance_score_changed',
    RESONANCE_TIER_UP: 'resonance_tier_up',
    RESONANCE_TIER_DOWN: 'resonance_tier_down',
    ECHO_EVENT_GENERATED: 'echo_event_generated',
    ECHO_EVENT_CLAIMED: 'echo_event_claimed',
    ECHO_EVENT_EXPIRED: 'echo_event_expired',
    HEARTBOUND_SCREEN_OPENED: 'heartbound_screen_opened',
});

const EVENT_NAMES = Object.freeze(Object.values(HEARTBOUND_EVENTS));

/**
 * ⛔ THE ONE-EMITTER TABLE. One seam per name, complete over all fourteen. A lane
 * adding an emit for a name owned elsewhere is the double-counted funnel D1 names
 * as worse than a missing one, and this table is the thing to check first.
 *
 * `side` decides which pipeline carries it: 'server' → logApiEvent (audit.js:112),
 * 'client' → EventTracker.Track (Assets/_Modules/Core/Analytics/EventTracker.cs:109).
 * The two sets are disjoint and complete; the test asserts both properties, because
 * a table that drifts silently is no better than the paragraph it replaced.
 */
const EVENT_OWNERS = Object.freeze({
    [HEARTBOUND_EVENTS.HEARTBOUND_DETECTED]: Object.freeze({
        side: 'server', seam: 'api/heartbound/status.js', emittedByThisLane: false,
        note: 'A wallet observed to hold an eligible stake for the first time. The chain read lives at the endpoint, not in the pulse job.',
    }),
    [HEARTBOUND_EVENTS.HEARTBOUND_ACTIVATED]: Object.freeze({
        side: 'server', seam: 'api/_lib/heartbound-state.js#activateHeartbound', emittedByThisLane: true,
        note: 'Emitted on EVERY successful activate call, because distinguishing a fresh INSERT from an absorbed ON CONFLICT would need either a second query or an xmax trick, and both change a statement this lane must not change. Dedupe in the query: MIN(received_at) GROUP BY player_id. Stated here rather than implied, because an "activations per day" chart read off the raw rows would over-count reconnects.',
    }),
    [HEARTBOUND_EVENTS.SKR_VERIFICATION_SUCCESS]: Object.freeze({
        side: 'server', seam: 'api/heartbound/status.js:350', emittedByThisLane: false,
        note: 'ALREADY LIVE. heartbound-state.js#recordVerifiedStake carries the seam but is deliberately unwired — see the header, departure 2.',
    }),
    [HEARTBOUND_EVENTS.SKR_VERIFICATION_FAILED]: Object.freeze({
        side: 'server', seam: 'api/heartbound/status.js:258,350', emittedByThisLane: false,
        note: 'ALREADY LIVE. Same as above for recordVerificationFailure.',
    }),
    [HEARTBOUND_EVENTS.HEART_PULSE_GLOBAL_DETECTED]: Object.freeze({
        side: 'server', seam: 'api/_lib/heartbound-pulse.js#detectPulse', emittedByThisLane: true,
        note: 'Only on a WON mint. Never on the BASELINE row (bookkeeping, not a pulse) and never on the losing side of the ON CONFLICT race.',
    }),
    [HEARTBOUND_EVENTS.HEART_PULSE_PLAYER_PROCESSED]: Object.freeze({
        side: 'server', seam: 'api/_lib/heartbound-pulse.js#processPlayerForPulse', emittedByThisLane: true,
        note: 'Only AFTER the grant gate is won (fresh GRANTED insert, or a PENDING row promoted in place). The losing racer emits nothing.',
    }),
    [HEARTBOUND_EVENTS.HEART_PULSE_PLAYER_DEFERRED]: Object.freeze({
        side: 'server', seam: 'api/_lib/heartbound-pulse.js#processPlayerForPulse', emittedByThisLane: true,
        note: 'The PENDING path (pending_rpc / pending_stale). NOT the over-cap catch-up figure, which rides as a count on the run summary.',
    }),
    [HEARTBOUND_EVENTS.RESONANCE_SCORE_CHANGED]: Object.freeze({
        side: 'server', seam: 'api/_lib/heartbound-pulse.js#processPlayerForPulse', emittedByThisLane: true,
        note: 'Emitted only when the floored score actually moved. An unchanged score is not an event.',
    }),
    [HEARTBOUND_EVENTS.RESONANCE_TIER_UP]: Object.freeze({
        side: 'server', seam: 'api/_lib/heartbound-pulse.js#processPlayerForPulse', emittedByThisLane: true,
        note: 'Departure 1 in the header: server, not client. The client is TOLD its tier and cannot observe an offline transition.',
    }),
    [HEARTBOUND_EVENTS.RESONANCE_TIER_DOWN]: Object.freeze({
        side: 'server', seam: 'api/_lib/heartbound-pulse.js#processPlayerForPulse', emittedByThisLane: true,
        note: 'Same. A tier falls when a stake is reduced; highestLifetimeTier never does (heartbound-resonance.js:243-246).',
    }),
    [HEARTBOUND_EVENTS.ECHO_EVENT_GENERATED]: Object.freeze({
        side: 'server', seam: 'HEART-005 (WO-1678)', emittedByThisLane: false,
        note: 'The Echo Event table does not exist yet. Named here so that lane emits it rather than inventing a fifteenth name.',
    }),
    [HEARTBOUND_EVENTS.ECHO_EVENT_CLAIMED]: Object.freeze({
        side: 'client', seam: 'HEART-005 / HEART-008 client', emittedByThisLane: false,
        note: 'A claim is a tap. The player-observable half of the Echo funnel.',
    }),
    [HEARTBOUND_EVENTS.ECHO_EVENT_EXPIRED]: Object.freeze({
        side: 'server', seam: 'HEART-005 (WO-1678)', emittedByThisLane: false,
        note: 'Expiry is a server clock fact; a client that never opened the game cannot report it.',
    }),
    [HEARTBOUND_EVENTS.HEARTBOUND_SCREEN_OPENED]: Object.freeze({
        side: 'client', seam: 'HEART-008 panel (WO-1681)', emittedByThisLane: false,
        note: 'Question 10 needs the SOURCE the screen was opened from; only the client knows it.',
    }),
});

const SERVER_EVENTS = Object.freeze(EVENT_NAMES.filter((n) => EVENT_OWNERS[n].side === 'server'));
const CLIENT_EVENTS = Object.freeze(EVENT_NAMES.filter((n) => EVENT_OWNERS[n].side === 'client'));

function isHeartboundEvent(name) {
    return EVENT_NAMES.includes(name);
}

function assertEventName(name) {
    if (!isHeartboundEvent(name)) {
        throw new RangeError(
            `unknown Heartbound telemetry event: ${JSON.stringify(name)} — the fourteen names are fixed by spec :1036-1064`);
    }
    return name;
}

// ── pseudonymous player id ───────────────────────────────────────────────────

/**
 * A salted SHA-256 prefix of the wallet. Deterministic (the same wallet always
 * groups together) and one-way (the row does not carry the address).
 *
 * ⚠ IT IS PSEUDONYMISATION, NOT ANONYMISATION, and calling it the latter would be
 * the kind of unproven claim CLAUDE.md §11B forbids: the wallet is public, the
 * salt is in this repo, and the same row's `player_id` column carries the address
 * anyway because that is the identity (see the header's D4 note). What the hash
 * buys is that a properties blob copied into a chart, a screenshot or a CSV does
 * not carry an address — which is exactly what spec :1084 asks for.
 *
 * ⛔ ITS OWN SALT, never audit.js's IP_SALT. Sharing one salt across two hashed
 * axes lets a row from either side be tested against the other's digests.
 */
function hashPlayerId(playerId, cfg = DEFAULT_CONFIG) {
    if (playerId === null || playerId === undefined || String(playerId).trim() === '') return null;
    const len = Number(cfg.playerHashLength) || 16;
    return crypto.createHash('sha256')
        .update(String(playerId).trim() + String(cfg.playerHashSalt))
        .digest('hex')
        .slice(0, len);
}

// ── buckets (WO-1684 acceptance 4: edges in config, once) ────────────────────

function bucketize(value, table) {
    if (value === null || value === undefined) return table.unknown;
    const n = Number(value);
    if (!Number.isFinite(n)) return table.unknown;
    if (n <= 0) return table.zero;
    for (const row of table.rows) if (n < Number(row.belowMax)) return row.label;
    return table.overflow;
}

/** Whole SKR → a coarse magnitude label. Labels match api/heartbound/status.js:400-408. */
function stakeBucket(skrWhole, cfg = DEFAULT_CONFIG) {
    return bucketize(skrWhole, cfg.stakeBuckets);
}

/** continuousPulseCount → a tenure label (days, at one pulse per UTC day). */
function streakBucket(pulses, cfg = DEFAULT_CONFIG) {
    return bucketize(pulses, cfg.streakBuckets);
}

/** Milliseconds → a latency label. Spec :1082 asks for RPC latency and processing duration. */
function latencyBucket(ms, cfg = DEFAULT_CONFIG) {
    return bucketize(ms, cfg.latencyBuckets);
}

// ── event shaping ────────────────────────────────────────────────────────────

/**
 * ⛔ THE LEAK GUARD. Walks the finished properties blob and refuses if any value
 * contains the identity string. WO-1684 acceptance 3 ("properties carries no
 * wallet address as a field") is otherwise a promise that holds until the first
 * lane passes `{ wallet }` through `extra`, at which point nothing complains and
 * the row is written. Structural beats careful.
 */
function assertNoIdentityLeak(properties, identity) {
    if (!identity) return properties;
    const needle = String(identity).trim();
    if (needle.length < 8) return properties; // too short to be a wallet; not a leak signal
    const walk = (node, path) => {
        if (node === null || node === undefined) return;
        if (typeof node === 'string') {
            if (node.includes(needle)) {
                throw new Error(
                    `Heartbound telemetry: properties.${path} carries the player identity — ` +
                    'the wallet is the player_id column and must never be a second copy inside properties (spec :1084)');
            }
            return;
        }
        if (Array.isArray(node)) { node.forEach((v, i) => walk(v, `${path}[${i}]`)); return; }
        if (typeof node === 'object') { for (const k of Object.keys(node)) walk(node[k], path ? `${path}.${k}` : k); }
    };
    walk(properties, '');
    return properties;
}

/**
 * Shape ONE event. Returns a plain, frozen object — this module never writes it.
 *
 *   { name, identity, properties, atMs }
 *
 * `identity` is the RAW wallet and goes to logApiEvent's identity argument, i.e.
 * the analytics_events.player_id column (header, D4). `properties` carries the
 * HASH and never the address.
 *
 * Every amount arrives as WHOLE SKR and leaves as a BUCKET (acceptance 4). The
 * exact figures are already persisted per (player, pulse) in player_pulse_grant
 * (heartbound-pulse.js:481-487), so nothing is lost by bucketing here — see D3
 * question 9 in the header.
 */
function buildEvent(name, input = {}) {
    assertEventName(name);
    const cfg = input.config || DEFAULT_CONFIG;
    const identity = input.playerId === undefined ? null : input.playerId;
    const atMs = Number.isFinite(Number(input.atMs)) ? Number(input.atMs) : null;

    const properties = {
        playerHash: hashPlayerId(identity, cfg),
        atMs,
    };

    if (input.tier !== undefined && input.tier !== null) properties.tier = Number(input.tier);
    if (input.previousTier !== undefined && input.previousTier !== null) properties.previousTier = Number(input.previousTier);
    if (input.tierName !== undefined && input.tierName !== null) properties.tierName = String(input.tierName);
    if (input.resonanceScore !== undefined && input.resonanceScore !== null) properties.resonanceScore = Number(input.resonanceScore);
    if (input.previousScore !== undefined && input.previousScore !== null) properties.previousScore = Number(input.previousScore);
    if (input.effectiveSkr !== undefined) properties.effectiveStakeBucket = stakeBucket(input.effectiveSkr, cfg);
    if (input.actualSkr !== undefined) properties.actualStakeBucket = stakeBucket(input.actualSkr, cfg);
    if (input.continuousPulseCount !== undefined) properties.streakBucket = streakBucket(input.continuousPulseCount, cfg);
    if (input.pulseId !== undefined && input.pulseId !== null) properties.pulseId = String(input.pulseId);
    if (input.sequenceNumber !== undefined && input.sequenceNumber !== null) properties.sequenceNumber = Number(input.sequenceNumber);
    if (input.reason !== undefined && input.reason !== null) properties.reason = String(input.reason);
    if (input.status !== undefined && input.status !== null) properties.status = String(input.status);
    if (input.rpcLatencyMs !== undefined) properties.rpcLatencyBucket = latencyBucket(input.rpcLatencyMs, cfg);
    if (input.processingMs !== undefined) properties.processingBucket = latencyBucket(input.processingMs, cfg);

    // `extra` is for the seam's own non-amount diagnostics. It goes through the
    // same leak guard as everything else, and it cannot overwrite a shaped field.
    if (input.extra && typeof input.extra === 'object') {
        for (const key of Object.keys(input.extra)) {
            if (properties[key] === undefined) properties[key] = input.extra[key];
        }
    }

    assertNoIdentityLeak(properties, identity);

    return Object.freeze({
        name,
        identity,
        properties: Object.freeze(properties),
        atMs,
    });
}

// ── the emit seam helper ─────────────────────────────────────────────────────

/** The default every seam takes: instrumentation that has not been wired does nothing. */
async function noopEmit() { /* deliberately nothing */ }

/**
 * ⛔ TELEMETRY MUST NEVER CHANGE THE OUTCOME IT MEASURES. Accepts a sync or async
 * emit, swallows every failure into one console line, and returns undefined so a
 * caller cannot accidentally branch on it. audit.js:128-131 takes the same posture
 * for its own insert ("a failed audit insert is itself worth one console line, and
 * nothing more"), and CLAUDE.md §12 step 2 forbids the silent half: the warn line
 * is what stops a broken sink from looking like a quiet day.
 */
async function safeEmit(emit, event) {
    if (typeof emit !== 'function') return undefined;
    try {
        await Promise.resolve(emit(event));
    } catch (err) {
        try { console.warn('[heartbound-telemetry] emit failed for ' + (event && event.name) + ': ' + err.message); }
        catch (_) { /* logging must never break the caller */ }
    }
    return undefined;
}

/**
 * Adapt the shaped event to the sink the repo already has. `logApiEvent` is
 * INJECTED (default: the real one from api/_lib/audit.js, required lazily) so a
 * test never drags the DB driver onto the require path and this module's top
 * level stays free of anything that can perform I/O.
 */
function createAnalyticsEmitter({ sql, logApiEvent } = {}) {
    const write = logApiEvent || require('./audit').logApiEvent;
    return async function emitToAnalytics(event) {
        if (!event || !event.name) return undefined;
        await write(sql, event.identity, event.name, event.properties);
        return undefined;
    };
}

// ── the whale-ratio aggregate (spec question 9 / WO-1682's ceiling) ──────────

/**
 * One measurement, taken from a real resonance transition.
 *
 * `view` is heartbound-resonance.js's `evaluate()` output for the state AFTER the
 * transition — the same object the pulse loop already holds — so this re-derives
 * no arithmetic. `scorePerSkr` is the raw advantage-per-unit-staked figure the
 * whole anti-whale argument is about.
 */
function whaleSampleFromTransition(next, view, cfg = DEFAULT_CONFIG) {
    const effectiveSkr = Number((next && next.effectiveSkr) || 0);
    const actualSkr = Number((next && next.actualSkr) || 0);
    const resonanceScore = Number((view && view.resonanceScore) || 0);
    return Object.freeze({
        effectiveSkr,
        actualSkr,
        resonanceScore,
        tier: Number((view && view.tier) || 0),
        stakeBucket: stakeBucket(effectiveSkr, cfg),
        scorePerSkr: effectiveSkr > 0 ? resonanceScore / effectiveSkr : 0,
    });
}

/**
 * ⭐ THE ANTI-WHALE CURVE, MADE MEASURABLE — the evidence WO-1682's 10% ceiling
 * needs, and the reason WO-1684 §3 says this ticket lands EARLY rather than last.
 *
 * Given samples spanning the stake range it reports:
 *
 *     stakeRatio             top-bucket mean effective stake / bottom-bucket mean
 *     scoreRatio             top-bucket mean resonance score / bottom-bucket mean
 *     advantageConcentration scoreRatio / stakeRatio
 *     sublinear              advantageConcentration < 1
 *
 * `sublinear` true is the spec's stated intent holding in the live population:
 * :313 — "500,000 SKR does NOT provide 500 times the power of 1,000 SKR". A
 * 500x stake ratio against a ~2x score ratio reads as advantageConcentration
 * ≈ 0.004, and THAT is the number a whale-advantage question is actually asking
 * for. It is a RATIO OF RATIOS on purpose: either half alone rises with the size
 * of the biggest staker and says nothing about fairness.
 *
 * ⚠ WHAT IT DOES *NOT* MEASURE, said plainly: this is the SCORE curve, not
 * gameplay advantage. Q-METER (owner-ruled 2026-09-10) defines the ceiling as the
 * SUM OF PASSIVE TIER PERCENTAGE BOOSTS TO RESOURCE YIELD, and those live in
 * heartbound-tiers.js — in flight in another lane, explicitly untouchable by this
 * one. So `tierBenefit` is an INJECTED function (tier -> percentage) defaulting to
 * null; pass it once that module lands and `benefitRatio` /
 * `benefitConcentration` answer question 9 in yield terms. ⛔ No benefit number is
 * hardcoded here — a guessed one would be this repo's favourite failure, a second
 * copy of a table that then drifts.
 */
function aggregateWhaleRatio(samples, { tierBenefit = null } = {}) {
    const list = (samples || []).filter((s) => s && Number.isFinite(Number(s.effectiveSkr)));
    if (list.length === 0) {
        return Object.freeze({
            n: 0, buckets: Object.freeze([]),
            stakeRatio: null, scoreRatio: null, advantageConcentration: null,
            benefitRatio: null, benefitConcentration: null,
            sublinear: null,
        });
    }

    const byBucket = new Map();
    for (const s of list) {
        const key = s.stakeBucket || stakeBucket(s.effectiveSkr);
        if (!byBucket.has(key)) byBucket.set(key, []);
        byBucket.get(key).push(s);
    }
    const mean = (arr, pick) => arr.reduce((a, s) => a + Number(pick(s) || 0), 0) / arr.length;

    const buckets = [...byBucket.entries()].map(([bucket, arr]) => ({
        bucket,
        n: arr.length,
        meanEffectiveSkr: mean(arr, (s) => s.effectiveSkr),
        meanResonanceScore: mean(arr, (s) => s.resonanceScore),
        meanTier: mean(arr, (s) => s.tier),
        meanBenefit: typeof tierBenefit === 'function' ? mean(arr, (s) => tierBenefit(s.tier)) : null,
    })).sort((a, b) => a.meanEffectiveSkr - b.meanEffectiveSkr);

    const low = buckets[0];
    const high = buckets[buckets.length - 1];
    // ⛔ ONE BUCKET IS NOT A MEASUREMENT. With a single stake bucket low === high,
    // so every ratio is exactly 1 and `sublinear` would read FALSE — a population
    // that spans no stake range would be reported as evidence AGAINST the curve.
    // That is the instrument manufacturing a verdict, so it says null instead.
    const spans = buckets.length >= 2;
    const ratio = (hi, lo) => (spans && lo > 0 ? hi / lo : null);

    const stakeRatio = ratio(high.meanEffectiveSkr, low.meanEffectiveSkr);
    const scoreRatio = ratio(high.meanResonanceScore, low.meanResonanceScore);
    const benefitRatio = (high.meanBenefit === null || low.meanBenefit === null)
        ? null : ratio(high.meanBenefit, low.meanBenefit);
    const advantageConcentration = (stakeRatio && scoreRatio) ? scoreRatio / stakeRatio : null;
    const benefitConcentration = (stakeRatio && benefitRatio) ? benefitRatio / stakeRatio : null;

    return Object.freeze({
        n: list.length,
        buckets: Object.freeze(buckets.map(Object.freeze)),
        stakeRatio,
        scoreRatio,
        advantageConcentration,
        benefitRatio,
        benefitConcentration,
        // null, not false, when there is only one bucket: a population that spans
        // no stake range has not demonstrated anything about the curve either way.
        sublinear: advantageConcentration === null ? null : advantageConcentration < 1,
    });
}

module.exports = {
    DEFAULT_CONFIG,
    HEARTBOUND_EVENTS,
    EVENT_NAMES,
    EVENT_OWNERS,
    SERVER_EVENTS,
    CLIENT_EVENTS,
    isHeartboundEvent,
    assertEventName,
    hashPlayerId,
    stakeBucket,
    streakBucket,
    latencyBucket,
    assertNoIdentityLeak,
    buildEvent,
    noopEmit,
    safeEmit,
    createAnalyticsEmitter,
    whaleSampleFromTransition,
    aggregateWhaleRatio,
};

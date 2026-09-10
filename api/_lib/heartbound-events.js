'use strict';

// =============================================================================
// api/_lib/heartbound-events.js - WO-1678 / HEART-005.
// THE ECHO EVENT TABLE: a deterministic roll and its weighting. PURE. NO I/O.
// -----------------------------------------------------------------------------
// Spec: docs/specs/HEARTBOUND_SKR_RESONANCE_WORK_ORDERS_2026-09-10.md:469-616.
//
// ⛔ NOTHING IN THIS FILE PERFORMS I/O, and nothing in it is random. No network, no
// database, no fs, no clock, and - the load-bearing one - NO Math.random and no
// crypto.randomBytes. The only crypto call is a SHA-256 DIGEST of three caller-
// supplied strings. Same inputs, same event, on any machine, forever. That is what
// the spec asks for at :493-503 and it is asserted, not described:
// test/heartbound-events.test.js drives the same inputs twice and compares.
//
// The single `require` besides node:crypto is the authored table next door, and
// every exported function takes it as a trailing `cfg` argument, so a test can
// inject its own table and prove the weights are not baked into the arithmetic.
// Same posture as api/_lib/heartbound-resonance.js, deliberately.
//
// -----------------------------------------------------------------------------
// ⭐ WHY A HASH AND NOT AN RNG - THIS IS THE WHOLE DESIGN
//
// A random draw has to be STORED to be honoured twice; a hash has to be RECOMPUTED.
// Storing it means a bug, a replay or a lost row can hand a player a second, better
// event. Recomputing it means the answer for (pulse, player, table) is a fact about
// those three strings and cannot be re-rolled by anyone, including us. The claim
// LEDGER (WO-1677) then decides whether the reward was already paid; the EVENT
// itself needs no ledger at all.
//
// It is also why `tableVersion` is inside the seed. Editing a row without bumping
// the version would silently re-roll every past pulse. Bump it, and old pulses keep
// resolving against the version recorded with them.
//
// -----------------------------------------------------------------------------
// ⛔ WHAT THIS MODULE MAY NEVER GROW
//
//   * A reward NUMBER. Spec :947 - "All reward amounts and weights belong in
//     data/config. No reward values hardcoded in presentation classes." There is no
//     numeric reward literal below; every amount, duration and weight is read off
//     heartbound-events-config.json.
//   * COMBAT POWER. Owner ruling Q-P2W (2026-09-10 13:10): economic acceleration is
//     allowed, combat power is not. `kind` is an allow-list of four values, checked
//     here at load and pinned by the Unity-side regression against the config text.
//   * A WITHDRAWABLE REWARD. No row may pay SKR, SOL, USDC, a token or an uncapped
//     premium currency (spec :919-931). No SKR is consumed by an event either - the
//     player's position is never touched by this table.
//   * THE FOUR BANNED WORDS. lottery / jackpot / bet / wager (spec :483-491) appear
//     nowhere in this module, in the config, or in any player-facing string derived
//     from either. `assertCopyIsClean` exists so the rule is executable.
//
// -----------------------------------------------------------------------------
// ⚠ WHAT THIS MODULE DELIBERATELY DOES NOT DO, so no reader assumes it does.
//
//   * IT HAS NO ENDPOINT. api/heartbound/status.js belongs to the HEART-001 lane and
//     the pulse loop to HEART-004. This module is called BY them. `toClientPayload`
//     defines the exact JSON shape the Unity client parses, so the two halves are
//     one contract in one place, but nothing here serves it.
//   * IT DOES NOT GRANT ANYTHING. There is no server-side item grant route in this
//     repo: the backend grants entitlements, the CLIENT grants items
//     (api/game/save.js:410-421). So an Echo Event is DECIDED here and APPLIED on the
//     device. That is a statement of the repo's real trust boundary, not an
//     aspiration - say it out loud rather than implying an authority we do not have.
//   * IT DOES NOT DEDUPLICATE. Once-only is the claim ledger's job (WO-1677 D2's
//     primary key). This module answers "which event", never "was it paid".
//
//     node --test test/heartbound-events.test.js
// =============================================================================

const crypto = require('node:crypto');

const DEFAULT_CONFIG = require('./heartbound-events-config.json');

/** The only reward classes that may exist. Owner ruling Q-P2W: no combat power, ever. */
const ALLOWED_KINDS = Object.freeze(['modifier', 'scout', 'heartfire', 'item']);

/** Spec :483-491. Never in a player-facing string, never in a row id. */
const BANNED_WORDS = Object.freeze(['lottery', 'jackpot', 'bet', 'wager']);

// ── the table ────────────────────────────────────────────────────────────────

function tableVersion(cfg = DEFAULT_CONFIG) {
    const v = Number(cfg.tableVersion);
    if (!Number.isInteger(v) || v < 1) throw new RangeError('tableVersion must be a positive integer');
    return v;
}

/**
 * Every authored row, validated and frozen, in a STABLE order.
 *
 * ⛔ THE SORT IS LOAD-BEARING, NOT TIDINESS. The roll walks a cumulative weight sum,
 * so the order of the rows decides which row a given hash lands on. JSON key order
 * is a file-editing artefact; an id sort is a property of the data. Re-ordering the
 * rows in the file must not silently re-roll every past pulse - only a tableVersion
 * bump may do that.
 */
function rows(cfg = DEFAULT_CONFIG) {
    const list = Array.isArray(cfg.rows) ? cfg.rows : [];
    if (list.length === 0) throw new RangeError('the event table is empty');
    const seen = new Set();
    const out = list.map((row) => {
        const id = String(row.id || '').trim();
        if (!id) throw new TypeError('an event row has no id');
        if (seen.has(id)) throw new TypeError(`duplicate event row id "${id}"`);
        seen.add(id);
        assertCopyIsClean(id, `event row id "${id}"`);
        const kind = String(row.kind || '').trim();
        if (!ALLOWED_KINDS.includes(kind)) {
            throw new TypeError(`event row "${id}" has kind "${kind}", which is not one of ${ALLOWED_KINDS.join('/')}`);
        }
        const weight = Number(row.weight);
        if (!Number.isFinite(weight) || weight <= 0) {
            throw new RangeError(`event row "${id}" must carry a positive weight`);
        }
        const minTier = row.minTier === undefined ? 1 : Number(row.minTier);
        if (!Number.isInteger(minTier) || minTier < 0) {
            throw new RangeError(`event row "${id}" has a non-integer minTier`);
        }
        return Object.freeze({ ...row, id, kind, weight, minTier });
    });
    out.sort((a, b) => (a.id < b.id ? -1 : a.id > b.id ? 1 : 0));
    return Object.freeze(out);
}

/**
 * The rows a player at `tier` is eligible for. Tier gating is the spec's own
 * progression axis (:604-615, HEART-006 Tier II): higher tiers unlock more event
 * VARIETIES rather than bigger numbers.
 *
 * ⛔ Below every row's minTier the table is EMPTY and the caller gets null, not a
 * consolation row. An ineligible player has no Echo Event; inventing one would be
 * the "silently vomit resources" failure the pulse cap exists to prevent.
 */
function eligibleRows(tier, cfg = DEFAULT_CONFIG) {
    const t = Number(tier) || 0;
    return rows(cfg).filter((row) => t >= row.minTier);
}

function totalWeight(list) {
    return list.reduce((sum, row) => sum + row.weight, 0);
}

// ── the deterministic seed ───────────────────────────────────────────────────

/**
 * SHA256(globalPulseId + playerId + eventTableVersion), hex. Spec :493-503.
 *
 * The separator matters: without one, ("ab","c") and ("a","bc") hash identically,
 * which would let two different (pulse, player) pairs share an event by accident.
 * A NUL cannot occur in a pulse id or a wallet address, so it cannot be forged
 * into a collision by a chosen player id either.
 */
function seedHex(globalPulseId, playerId, version) {
    const pulse = requireText(globalPulseId, 'globalPulseId');
    const player = requireText(playerId, 'playerId');
    const ver = String(version);
    return crypto.createHash('sha256').update(`${pulse}\u0000${player}\u0000${ver}`, 'utf8').digest('hex');
}

function requireText(value, what) {
    if (typeof value !== 'string' || value.trim() === '') {
        throw new TypeError(`${what}: expected a non-empty string`);
    }
    return value.trim();
}

/**
 * A hex digest -> a fraction in [0,1). Uses the TOP 52 bits, which is every bit a
 * double can hold exactly; taking a modulus of the whole 256-bit digest through
 * Number() would round first and quietly bias the low rows of the table.
 */
function seedFraction(hex) {
    const top = BigInt('0x' + String(hex).slice(0, 16));   // 64 bits
    const scaled = top >> 12n;                              // 52 bits, exact in a double
    return Number(scaled) / Number(1n << 52n);
}

// ── the roll ─────────────────────────────────────────────────────────────────

/**
 * Pick the row a seed lands on, walking the cumulative weight sum. Pure and total:
 * an empty list returns null; a fraction of exactly 1 (impossible from
 * seedFraction, possible from a caller) lands on the last row rather than falling
 * off the end.
 */
function pickByWeight(list, fraction) {
    if (!list || list.length === 0) return null;
    const total = totalWeight(list);
    let cursor = Math.max(0, Math.min(0.9999999999, Number(fraction))) * total;
    for (const row of list) {
        cursor -= row.weight;
        if (cursor < 0) return row;
    }
    return list[list.length - 1];
}

/**
 * THE ENTRY POINT. Decide the Echo Event for one (pulse, player, tier).
 *
 * @param {{globalPulseId:string, playerId:string, tier:number}} args
 * @returns {object|null} the resolved event, or null when the player's tier unlocks
 *          no row at all. Never throws for a legitimate ineligible player.
 */
function rollEvent({ globalPulseId, playerId, tier }, cfg = DEFAULT_CONFIG) {
    const version = tableVersion(cfg);
    const list = eligibleRows(tier, cfg);
    const hex = seedHex(globalPulseId, playerId, version);
    const row = pickByWeight(list, seedFraction(hex));
    if (!row) return null;
    return Object.freeze({
        eventId: row.id,
        kind: row.kind,
        tableVersion: version,
        // The claim key. The ledger's primary key is (playerId, pulseId); this string
        // is what the CLIENT keys its local once-only guard on, so the two halves
        // agree on what "the same event" means without the client deriving anything.
        claimId: `${requireText(globalPulseId, 'globalPulseId')}:${version}`,
        seedHex: hex,
        reward: rewardOf(row),
    });
}

/**
 * The reward payload for a row: every field except the table bookkeeping.
 * ⛔ Values are COPIED from config, never computed. There is no arithmetic here on
 * purpose - an amount that this module could scale would be an amount the config no
 * longer describes.
 */
function rewardOf(row) {
    const reward = { kind: row.kind };
    if (row.modifier !== undefined) reward.modifier = String(row.modifier);
    if (row.magnitude !== undefined) reward.magnitude = Number(row.magnitude);
    if (row.durationSeconds !== undefined) reward.durationSeconds = Number(row.durationSeconds);
    if (row.charges !== undefined) reward.charges = Number(row.charges);
    if (row.itemId !== undefined) reward.itemId = String(row.itemId);
    if (row.amount !== undefined) reward.amount = Number(row.amount);
    return Object.freeze(reward);
}

/**
 * THE CLIENT CONTRACT, in one place. Assets/_Modules/Wallet/HeartboundEventClient.cs
 * parses exactly these keys and no others; keeping the shape here rather than in an
 * endpoint means the two halves cannot drift into two contracts.
 *
 * `null` in -> `{ hasEvent:false }` out, so an ineligible player is a normal answer
 * with a shape, not a missing field the client has to guess at.
 */
function toClientPayload(rolled) {
    if (!rolled) return { hasEvent: false };
    return {
        hasEvent: true,
        eventId: rolled.eventId,
        kind: rolled.kind,
        tableVersion: rolled.tableVersion,
        claimId: rolled.claimId,
        reward: { ...rolled.reward },
    };
}

// ── the copy rule, executable ────────────────────────────────────────────────

/**
 * Spec :483-491. Throws on lottery / jackpot / bet / wager.
 *
 * ⚠ WHOLE-WORD, and that is not pedantry: a substring match on "bet" fails on
 * "better" and on "alphabet", and a lint that fires on innocent copy is a lint
 * somebody deletes. The four words are banned as WORDS.
 */
function assertCopyIsClean(text, what = 'copy') {
    const words = String(text).toLowerCase().split(/[^a-z]+/);
    for (const banned of BANNED_WORDS) {
        if (words.includes(banned)) {
            throw new Error(`${what} contains the banned word "${banned}" (spec :483-491)`);
        }
    }
    return true;
}

module.exports = {
    DEFAULT_CONFIG,
    ALLOWED_KINDS,
    BANNED_WORDS,
    tableVersion,
    rows,
    eligibleRows,
    totalWeight,
    seedHex,
    seedFraction,
    pickByWeight,
    rollEvent,
    rewardOf,
    toClientPayload,
    assertCopyIsClean,
};

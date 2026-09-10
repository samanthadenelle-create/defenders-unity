'use strict';

// =============================================================================
// api/_lib/heartbound-pulse.js — WO-1677 / HEART-004: the Heart Pulse detector.
// -----------------------------------------------------------------------------
// A Heart Pulse is minted when the native SKR staking share price is observed to
// have ADVANCED since the last pulse. Every eligible Heartbound player is then
// processed against that one global pulse, exactly once (spec
// docs/specs/HEARTBOUND_SKR_RESONANCE_WORK_ORDERS_2026-09-10.md:376-468).
//
// This module holds ALL the logic and touches NO globals: every external fact
// arrives as an injected function, so the whole job is drivable from node:test
// with an in-memory sql mock (test/heartbound-pulse.test.js). The Vercel cron
// handler (api/cron/heart-pulse.js) is a thin auth + wiring shell.
//
// -----------------------------------------------------------------------------
// OWNER RULINGS THIS FILE IMPLEMENTS (2026-09-10; taken verbatim from the lane
// brief — ⚠ `grep -n RULED docs/specs/HEARTBOUND_TRIAGE_2026-09-10.md` returns
// ZERO hits at dev c96030b5c, so the rulings are NOT in the tree and the brief
// is their only record here. Named as such rather than cited as a doc line.)
//
//   Q-CADENCE — a Heart Pulse is RARE AND CEREMONIAL: AT MOST ONE PER DAY.
//               This is the MIN_PULSE_INTERVAL the spec never had (WO-1677 §0a
//               called its absence "an uncapped faucet"), implemented as a UTC
//               calendar-day key — see MAX_PULSES_PER_UTC_DAY. It is a
//               POLICY bound: it holds however fast the chain ticks, which is
//               why the unmeasured on-chain cadence no longer gates this work.
//   Q-CRON    — a third DAILY Vercel cron is accepted. vercel.json declared
//               exactly two, both daily, before this change.
//   Q2        — on RPC failure, fail to LAST-KNOWN VERIFIED STATE (never to
//               zero, which is what the client does today at
//               Assets/_Modules/Wallet/NativeSkrStakeQuery.cs:87-94).
//               ⚠ THE RECONCILIATION, so the next lane does not re-litigate it:
//               Q2 governs the PLAYER STATE (do not zero the stake, do not
//               reset the streak, do not mark the player dead). Spec :451-461
//               governs the REWARD (generate none, mark PENDING, retry). Both
//               hold at once. Q2 does NOT license paying a reward computed from
//               a stale `lastActualStake` — an unverified pulse pays nothing.
//   Q1        — the stake read is BACKEND-ONLY and belongs to HEART-001, which
//               is relocating the chain read into api/ right now. This lane
//               writes NO chain read: `readSharePrice` / `readStake` are
//               injected, and default to placeholders that throw
//               "HEART-001 seam not wired".
//   Q-INJECT  — NO pulse-injection route exists in production code. There is no
//               "simulate a pulse" export here and none may be added; the test
//               drives the real functions with a mock sql, which is the only
//               injection that exists.
//
// SEAMS OWNED BY OTHER LANES IN THIS WAVE (all injected, all defaulting to a
// clearly-named throw so wiring is one line and a MISSING wire is LOUD, never
// silent):
//   HEART-001  readSharePrice()      — the StakeConfig share price
//   HEART-001  readStake(wallet)     — one player's verified stake
//   HEART-002  listActiveStates(sql) — the Heartbound state rows to process
//   HEART-002  persistPlayerState(sql, state) — writes heartbound_state
//
// HEART-003 IS NO LONGER A SEAM — it LANDED (dev cde1c1f63) and is required
// directly: `api/_lib/heartbound-resonance.js`, whose numbers come from
// `heartbound-resonance-config.json`. NONE of its arithmetic is re-implemented
// here. One call, `applyPulse(state, actualSkr, cfg)`, owns the whole per-pulse
// transition — the immediate decrease clamp, the 25% ramp, the streak, the
// lifetime tier — and `evaluate(state, cfg)` derives the score/tier read model.
// Its unit is WHOLE SKR; the chain's is u128 base units, and `stakeReadingToSkr`
// below is the single crossing, made with that module's own boundary helpers.
//
// TRANSACTIONALITY — honest note. Spec step 10 says "Persist transactionally".
// `node_modules/` is absent from this worktree, so whether this repo's
// @neondatabase/serverless build exposes a multi-statement transaction helper
// is UNPROVEN and is not relied on. Instead the per-player write is GATE-FIRST:
// the `player_pulse_grant` INSERT … ON CONFLICT DO NOTHING RETURNING is the
// gate, and the state write only ever runs when that insert WON. A crash
// between the two leaves a granted row and an un-advanced streak — recoverable
// and never a double-pay; the opposite order would double-pay. That is a
// deliberate design choice, not a transaction, and it is written down here so
// nobody later reads "transactional" into it.
// =============================================================================

// --- Config ------------------------------------------------------------------
//
// ⚠ Q-CONFIG IS RULED (owner, 2026-09-10): backend-only Heartbound knobs belong
// on the Command Center tunables rail as **server-only rows** — option (a) of
// WO-1676 Q-CONFIG, i.e. teach `api/_lib/tunable-manifest.js` a `serverOnly`
// marker that `build()` honours, so a backend-read knob no longer fails the
// three-way join at `tunable-manifest.js:848-853`. Building that marker is
// HEART-006 / HEART-009's job, not this lane's. The three constants below stay
// in ONE block precisely so lifting them onto that rail is a data move rather
// than an archaeology exercise — the same reasoning
// `heartbound-resonance-config.json` records for its own table.

// Q-CADENCE: at most one pulse per day. Ceremonial, not a background tick.
//
// ⛔ IT IS A UTC CALENDAR DAY, NOT AN ELAPSED-MILLISECOND FLOOR, AND THE
//    DIFFERENCE IS A REAL BUG THAT WAS CAUGHT BEFORE IT SHIPPED.
//    A `now - last < 86_400_000` floor looks equivalent and is not: Vercel does
//    not fire a cron to the second. Day N at 05:00:45 and day N+1 at 05:00:10
//    are 86,399,000 ms apart — INSIDE the floor — so a real advancement would
//    be silently refused and the pulse skipped for a whole day, on ordinary
//    scheduler jitter. A calendar-day key has no such edge: one pulse per UTC
//    date, every date, however the scheduler drifts. (Vercel cron schedules are
//    UTC, which is why the key is UTC and not local.)
//    The one cost is that two pulses may land close together across a midnight
//    boundary. With a fixed 05:00 schedule that cannot happen in practice, and
//    "≤ 1 per calendar day" is the ruling read literally.
const MAX_PULSES_PER_UTC_DAY = 1;

// Spec :455-457 — "Suggested stale grace: 72 hours".
const STALE_GRACE_MS = 72 * 60 * 60 * 1000;

// Spec :463-467 — "Cap catch-up processing… Initial cap: 5 pending pulses.
// Anything beyond this should require explicit reconciliation rather than
// silently vomiting six months of resources into someone's castle."
const MAX_CATCHUP_PULSES = 5;

const PULSE_STATUS = Object.freeze({
    BASELINE: 'BASELINE',
    MINTED: 'MINTED',
    COMPLETE: 'COMPLETE',
});

const GRANT_STATUS = Object.freeze({
    GRANTED: 'GRANTED',
    PENDING: 'PENDING',
});

// Every non-minting / non-granting outcome is a NAMED reason, never a bare
// false — a job that says "nothing happened" without saying why is the silent
// failure CLAUDE.md §12 forbids.
const SKIP = Object.freeze({
    BASELINE_RECORDED: 'baseline_recorded',
    NO_ADVANCEMENT: 'no_advancement',
    INTERVAL_FLOOR: 'interval_floor',
    RPC_UNAVAILABLE: 'rpc_unavailable',
    BEFORE_ACTIVATION: 'before_activation',
    BELOW_MINIMUM: 'below_minimum',
    ALREADY_GRANTED: 'already_granted',
    PENDING_RPC: 'pending_rpc',
    PENDING_STALE: 'pending_stale',
});

// --- Seam placeholders -------------------------------------------------------

function seamMissing(which, what) {
    return async () => {
        throw new Error(`${which} seam not wired: ${what}`);
    };
}

const defaultReadSharePrice = seamMissing('HEART-001', 'readSharePrice() must return { rawU128: bigint, sourceSlot?, chainReference? }');
const defaultReadStake = seamMissing('HEART-001', 'readStake(wallet) must return { skr } | { rawTokens|rawU128 } | { sharesRaw, sharePriceRaw }');
const defaultListActiveStates = seamMissing('HEART-002', 'listActiveStates(sql) must return heartbound state rows');
const defaultPersistPlayerState = seamMissing('HEART-002', 'persistPlayerState(sql, state) must write heartbound_state');

// ⭐ THE HEART-003 ARITHMETIC — REQUIRED DIRECTLY. `api/_lib/heartbound-resonance.js`
// landed on dev at cde1c1f63, so the module IS the contract and the require IS
// the proof of it: a missing or renamed export is a TypeError at the call site
// with the name in it. (An earlier revision of this file carried a name-validation
// shim because the module did not exist yet; that shim was duplicated state about
// another file's exports and is deleted — the direct require cannot go stale.)
// It is a PURE module (no DB driver, no network), so a top-level require is safe
// for the tests too. `deps.resonance` still overrides it, which is how a test can
// drive an alternate table.
const resonanceModule = require('./heartbound-resonance.js');

// ⭐ HEART-011 / WO-1684 — THE INSTRUMENT. `heartbound-telemetry.js` is a PURE
// event-shaping module (no DB, no network), so requiring it at top level is safe
// here for exactly the reason the resonance require above is.
//
// ⛔ IT CHANGES NOTHING. Every emit below goes through an INJECTED `deps.emit`
// that DEFAULTS TO A NO-OP, and every call is wrapped in `safeEmit`, which
// swallows a throwing sink into one console line. So with no emitter wired — which
// is the state of api/cron/heart-pulse.js today — this file's return values, SQL
// statements and ordering are byte for byte what they were before WO-1684.
// CLAUDE.md §12 requires the instrument to go in first; it does not license the
// instrument moving the thing it measures.
//
// Wiring it is ONE line in the cron shell:
//     const { createAnalyticsEmitter } = require('../_lib/heartbound-telemetry.js');
//     runHeartPulseDetector({ sql, now: Date.now(), emit: createAnalyticsEmitter({ sql }) })
const telemetry = require('./heartbound-telemetry.js');
const TE = telemetry.HEARTBOUND_EVENTS;

/**
 * ⛔ THE UNIT BOUNDARY, IN EXACTLY ONE PLACE.
 * HEART-003 works in WHOLE SKR as a Number (`isEligible(actualSkr)`,
 * `applyPulse(state, actualSkr)`); the chain speaks u128 base units
 * (1 SKR = 1e6, `heartbound-resonance-config.json` "chain"). Converting is the
 * resonance module's own job and NONE of it is re-implemented here — this
 * function only decides WHICH of its boundary helpers a given HEART-001 reading
 * calls for, in a fixed precedence, and throws with the accepted shapes named if
 * the reading matches none. HEART-001 has not landed, so the reading's exact
 * field names are not yet fixed; this accepts the three forms the spec and
 * WO-1674 describe rather than guessing one.
 */
// Every resonance call in this file passes `cfg` as the module's trailing
// parameter. Passing `undefined` is exactly how its `cfg = DEFAULT_CONFIG`
// defaults engage, so there is one call shape, never two.
function stakeReadingToSkr(reading, resonance, cfg) {
    if (!reading || typeof reading !== 'object') {
        throw new TypeError('readStake returned no snapshot');
    }
    if (reading.skr !== undefined && reading.skr !== null) return Number(reading.skr);
    const rawTokens = reading.rawTokens !== undefined ? reading.rawTokens : reading.rawU128;
    if (rawTokens !== undefined && rawTokens !== null) return resonance.rawTokensToSkr(rawTokens, cfg);
    if (reading.sharesRaw !== undefined && reading.sharePriceRaw !== undefined) {
        return resonance.skrFromShares(reading.sharesRaw, reading.sharePriceRaw, cfg);
    }
    throw new TypeError('readStake snapshot carries none of { skr } | { rawTokens|rawU128 } | { sharesRaw, sharePriceRaw }');
}

// --- Small helpers -----------------------------------------------------------

// NUMERIC(39,0) comes back from pg as a STRING (it does not fit a JS number).
// Every share-price comparison in this file goes through BigInt so a u128 is
// never silently truncated through a double.
function toBigInt(value) {
    if (value === null || value === undefined || value === '') return null;
    if (typeof value === 'bigint') return value;
    return BigInt(String(value).trim());
}

// The Q-CADENCE key: the UTC calendar date, 'YYYY-MM-DD'. See the constant.
function utcDayKey(value) {
    const ms = toMs(value);
    return ms === null ? null : new Date(ms).toISOString().slice(0, 10);
}

function toMs(value) {
    if (value === null || value === undefined) return null;
    if (value instanceof Date) return value.getTime();
    if (typeof value === 'number') return value;
    const t = Date.parse(String(value));
    return Number.isNaN(t) ? null : t;
}

// --- D3a: mint at most one global pulse -------------------------------------

/**
 * Reads the newest recorded pulse, reads the chain share price, and mints ONE
 * global pulse if (and only if) the price advanced AND the Q-CADENCE floor has
 * elapsed. Never mints two for the same price — UNIQUE (new_share_price) makes
 * that structural, and the ON CONFLICT here is what maps the race to a skip.
 */
async function detectPulse(deps) {
    const {
        sql,
        readSharePrice = defaultReadSharePrice,
        now = Date.now(),
        emit = telemetry.noopEmit,
    } = deps;

    const lastRows = await sql`
        SELECT pulse_id, sequence_number, new_share_price, detected_at_utc, status
        FROM global_heart_pulse
        ORDER BY sequence_number DESC
        LIMIT 1
    `;
    const last = lastRows && lastRows.length > 0 ? lastRows[0] : null;

    // Q2 / spec :451-461 — an unreadable chain does NOTHING. No mint, no
    // player processing, no state change. The next run retries.
    let observed;
    try {
        observed = await readSharePrice();
    } catch (err) {
        return { minted: false, reason: SKIP.RPC_UNAVAILABLE, error: err.message };
    }
    const newPrice = toBigInt(observed && observed.rawU128);
    if (newPrice === null) {
        return { minted: false, reason: SKIP.RPC_UNAVAILABLE, error: 'readSharePrice returned no rawU128' };
    }

    // FIRST RUN: there is no "previously processed share price". Record a
    // BASELINE row that grants nothing, so the NEXT run has something real to
    // compare against. Minting against a NULL previous price would either
    // invent an advancement nobody observed or block forever.
    if (!last) {
        const inserted = await sql`
            INSERT INTO global_heart_pulse
                (sequence_number, previous_share_price, new_share_price, detected_at_utc,
                 source_slot, chain_reference, status)
            VALUES (0, NULL, ${newPrice.toString()}, ${new Date(now).toISOString()},
                    ${observed.sourceSlot ?? null}, ${observed.chainReference ?? null}, ${PULSE_STATUS.BASELINE})
            ON CONFLICT (new_share_price) DO NOTHING
            RETURNING pulse_id, sequence_number, new_share_price, detected_at_utc, status
        `;
        return {
            minted: false,
            reason: SKIP.BASELINE_RECORDED,
            baseline: inserted && inserted.length > 0 ? inserted[0] : null,
        };
    }

    const lastPrice = toBigInt(last.new_share_price);
    if (!(newPrice > lastPrice)) {
        // Ran twice over the same chain state: the second run mints nothing.
        // (WO-1677 acceptance 1.)
        return { minted: false, reason: SKIP.NO_ADVANCEMENT, lastSharePrice: lastPrice.toString() };
    }

    // Q-CADENCE: rare and ceremonial, AT MOST ONE PER UTC CALENDAR DAY. A real
    // advancement on a day that already has a pulse is deliberately DROPPED,
    // not queued — queueing it would reintroduce the faucet the floor exists to
    // close. The next day's run sees the (still higher) price and mints then.
    //
    // The BASELINE row is exempt: it is bookkeeping, not a pulse, so the first
    // real pulse is not made to wait a day behind it.
    const lastDay = utcDayKey(last.detected_at_utc);
    const today = utcDayKey(now);
    if (last.status !== PULSE_STATUS.BASELINE && lastDay !== null && lastDay === today) {
        return { minted: false, reason: SKIP.INTERVAL_FLOOR, pulsedOn: lastDay, perDay: MAX_PULSES_PER_UTC_DAY };
    }

    const nextSeq = Number(last.sequence_number) + 1;
    const inserted = await sql`
        INSERT INTO global_heart_pulse
            (sequence_number, previous_share_price, new_share_price, detected_at_utc,
             source_slot, chain_reference, status)
        VALUES (${nextSeq}, ${lastPrice.toString()}, ${newPrice.toString()}, ${new Date(now).toISOString()},
                ${observed.sourceSlot ?? null}, ${observed.chainReference ?? null}, ${PULSE_STATUS.MINTED})
        ON CONFLICT (new_share_price) DO NOTHING
        RETURNING pulse_id, sequence_number, previous_share_price, new_share_price, detected_at_utc, status
    `;
    if (!inserted || inserted.length === 0) {
        // Two detector runs raced. The unique index decided; this one loses and
        // mints nothing. Same "the DB is the arbiter" shape as
        // api/referral/install-brag.js:118-133.
        return { minted: false, reason: SKIP.NO_ADVANCEMENT, raced: true };
    }

    // ⭐ HEART-011: `heart_pulse_global_detected` — and ONLY here. Not on the
    // BASELINE row (bookkeeping, not a pulse), not on NO_ADVANCEMENT, and not on
    // the losing side of the ON CONFLICT race above, all three of which return
    // before this line. A global pulse is a property of the WHOLE realm, so it
    // carries no player: `identity` is null, which audit.js:120 stores as
    // 'anonymous'. The share prices are deliberately absent — a u128 chain figure
    // is not one of the ten questions and would ride through JSON as a lossy
    // double anyway; `global_heart_pulse` already holds them exactly.
    await telemetry.safeEmit(emit, telemetry.buildEvent(TE.HEART_PULSE_GLOBAL_DETECTED, {
        playerId: null,
        atMs: now,
        pulseId: inserted[0].pulse_id,
        sequenceNumber: inserted[0].sequence_number,
        status: inserted[0].status,
        extra: { sourceSlot: observed.sourceSlot ?? null },
    }));

    return { minted: true, pulse: inserted[0] };
}

// --- D3b: process ONE player against ONE pulse ------------------------------

/**
 * Spec :432-443, the ten ordered steps. Returns a named outcome; never throws
 * for an expected condition (an unverifiable player is a PENDING row, not an
 * exception that kills the whole job).
 *
 * `state` is a HEART-002 row. The fields consumed here — the contract that lane
 * must satisfy — are:
 *   playerId, walletAddress, activatedAtUtc, lastVerifiedAtUtc,
 *   lastActualStake, effectiveResonatingStake, continuousPulseCount,
 *   totalLifetimePulses, highestLifetimeTier
 * All stake figures are WHOLE SKR (Numbers), the unit HEART-003 works in.
 */
async function processPlayerForPulse(deps) {
    const {
        sql,
        pulse,
        state,
        readStake = defaultReadStake,
        resonance = resonanceModule,
        resonanceConfig,
        persistPlayerState = defaultPersistPlayerState,
        now = Date.now(),
        staleGraceMs = STALE_GRACE_MS,
        emit = telemetry.noopEmit,
    } = deps;

    const res = resonance;
    const cfg = resonanceConfig; // undefined ⇒ the module's own DEFAULT_CONFIG
    const pulseMs = toMs(pulse.detected_at_utc);
    const activatedMs = toMs(state.activatedAtUtc);

    // WO-1675 D2 / spec :238-240 — "Do NOT retroactively award historical
    // Elarion pulses." A pulse older than the player's activation is not theirs.
    if (activatedMs !== null && pulseMs !== null && pulseMs < activatedMs) {
        return { granted: false, reason: SKIP.BEFORE_ACTIVATION, playerId: state.playerId };
    }

    // Step 1 — obtain fresh or acceptable verified staking state, and cross the
    // unit boundary ONCE (chain base units -> whole SKR) using HEART-003's own
    // helpers. A malformed snapshot is treated exactly like an RPC failure: it
    // is an unverified reading, and an unverified reading never pays.
    let actualSkr = null;
    let verifyError = null;
    try {
        actualSkr = stakeReadingToSkr(await readStake(state.walletAddress), res, cfg);
    } catch (err) {
        actualSkr = null;
        verifyError = err.message;
    }

    if (actualSkr === null) {
        // Q2 + spec :451-461. Do NOT reset the streak. Do NOT generate an
        // unverified reward. Record PENDING so a later run can pay it once.
        // The player's last-known verified state is left exactly as it was —
        // this function writes no state on this path at all, which is the
        // strongest possible form of "fail to last-known".
        const lastVerifiedMs = toMs(state.lastVerifiedAtUtc);
        const stale = lastVerifiedMs !== null && now - lastVerifiedMs > staleGraceMs;
        const reason = stale ? SKIP.PENDING_STALE : SKIP.PENDING_RPC;
        await sql`
            INSERT INTO player_pulse_grant
                (player_id, global_pulse_id, status, pending_reason, granted_at)
            VALUES (${state.playerId}, ${pulse.pulse_id}, ${GRANT_STATUS.PENDING},
                    ${stale ? 'STALE' : 'RPC_UNAVAILABLE'}, ${new Date(now).toISOString()})
            ON CONFLICT (player_id, global_pulse_id) DO NOTHING
        `;
        // ⭐ HEART-011: `heart_pulse_player_deferred`. THIS is the deferral the
        // spec names — one player whose reading could not be verified, so their
        // pulse is owed rather than paid. ⚠ It is NOT the run summary's
        // `deferred` count, which is the OVER-CAP catch-up figure from
        // selectCatchupPulses; those are different facts and giving them one
        // event name would make the deferral chart unreadable. The over-cap
        // figure stays a count on the summary, where it already is.
        await telemetry.safeEmit(emit, telemetry.buildEvent(TE.HEART_PULSE_PLAYER_DEFERRED, {
            playerId: state.playerId,
            atMs: now,
            pulseId: pulse.pulse_id,
            sequenceNumber: pulse.sequence_number,
            reason,
            status: GRANT_STATUS.PENDING,
            continuousPulseCount: Number(state.continuousPulseCount || 0),
            // The failure message is a SHAPE, never a payload: it can carry an
            // RPC endpoint or a wallet echoed back by a provider, and buildEvent's
            // leak guard would (correctly) refuse the whole event for it.
            extra: { errorKind: verifyError ? 'read_failed' : 'no_reading' },
        }));
        return { granted: false, reason, playerId: state.playerId, error: verifyError };
    }

    // Steps 2-3 — eligibility floor + wallet relationship. The floor is HEART-003
    // config (`minHeartboundStake`, spec :274-280) and is NOT re-implemented here.
    if (!res.isEligible(actualSkr, cfg)) {
        return { granted: false, reason: SKIP.BELOW_MINIMUM, playerId: state.playerId, actualSkr };
    }

    // Steps 4-7 — ONE call. `applyPulse` owns the whole transition: it clamps a
    // DECREASED stake immediately (spec :296-298, the anti-flash-stake
    // asymmetry), ramps 25% of the remaining gap, advances the streak and lifts
    // `highestLifetimeTier`. `evaluate` then derives the read model. Not one
    // line of that arithmetic is duplicated here — this file only sequences it.
    const priorState = {
        actualSkr: Number(state.lastActualStake || 0),
        effectiveSkr: Number(state.effectiveResonatingStake || 0),
        continuousPulseCount: Number(state.continuousPulseCount || 0),
        highestLifetimeTier: Number(state.highestLifetimeTier || 0),
    };
    const nextState = res.applyPulse(priorState, actualSkr, cfg);
    const view = res.evaluate(nextState, cfg);
    // ⭐ HEART-011: the BEFORE half of the transition, derived from the SAME
    // arithmetic as the after half rather than read off the state row.
    // `state.resonanceTier` is not in this function's stated state contract
    // (see the doc comment above), so a fixture or a HEART-002 row that omits it
    // would silently report every player as a tier-up from 0 — an instrument that
    // manufactures its own signal is worse than none. `evaluate` is pure and
    // allocation-cheap, and it cannot disagree with the value it is compared to.
    const priorView = res.evaluate(priorState, cfg);
    const effectiveStake = nextState.effectiveSkr;
    const continuousPulseCount = nextState.continuousPulseCount;
    const totalLifetimePulses = Number(state.totalLifetimePulses || 0) + 1;
    const score = view.resonanceScore;
    const tier = view.tier;

    // ⭐ THE GATE. Steps 8-10 only ever run if THIS insert won. A duplicate
    // background job's insert returns zero rows (PK violation absorbed by
    // ON CONFLICT DO NOTHING) and is mapped to "already granted" — the exact
    // mapping api/referral/install-brag.js:118-140 uses for achievement_grants.
    // A PENDING row from an earlier failed run also conflicts here, so it is
    // PROMOTED in place below rather than re-inserted.
    const inserted = await sql`
        INSERT INTO player_pulse_grant
            (player_id, global_pulse_id, status, effective_stake, resonance_score, resonance_tier, granted_at)
        VALUES (${state.playerId}, ${pulse.pulse_id}, ${GRANT_STATUS.GRANTED},
                ${String(effectiveStake)}, ${score}, ${tier}, ${new Date(now).toISOString()})
        ON CONFLICT (player_id, global_pulse_id) DO NOTHING
        RETURNING player_id, global_pulse_id, status
    `;
    if (!inserted || inserted.length === 0) {
        const existing = await sql`
            SELECT status FROM player_pulse_grant
            WHERE player_id = ${state.playerId} AND global_pulse_id = ${pulse.pulse_id}
            LIMIT 1
        `;
        const existingStatus = existing && existing.length > 0 ? existing[0].status : null;
        if (existingStatus === GRANT_STATUS.PENDING) {
            // Promote the pending row IN PLACE — one row per (player, pulse)
            // forever, so the PK keeps meaning what it says.
            await sql`
                UPDATE player_pulse_grant
                SET status = ${GRANT_STATUS.GRANTED},
                    effective_stake = ${String(effectiveStake)},
                    resonance_score = ${score},
                    resonance_tier = ${tier},
                    pending_reason = NULL,
                    granted_at = ${new Date(now).toISOString()}
                WHERE player_id = ${state.playerId} AND global_pulse_id = ${pulse.pulse_id}
                  AND status = ${GRANT_STATUS.PENDING}
            `;
        } else {
            return { granted: false, reason: SKIP.ALREADY_GRANTED, playerId: state.playerId };
        }
    }

    await persistPlayerState(sql, {
        playerId: state.playerId,
        lastActualStake: nextState.actualSkr,
        effectiveResonatingStake: effectiveStake,
        continuousPulseCount,
        totalLifetimePulses,
        highestLifetimeTier: nextState.highestLifetimeTier,
        lastGlobalPulseId: pulse.pulse_id,
        lastVerifiedAtUtc: new Date(now).toISOString(),
        resonanceScore: score,
        resonanceTier: tier,
    });

    // ⭐ HEART-011, and ONLY on this path: every `return` above this line either
    // lost the ON CONFLICT gate or never reached it, so a duplicate background
    // run emits nothing. Three facts, three events, none of them redundant:
    //
    //   heart_pulse_player_processed — the grant happened (funnel denominator).
    //   resonance_score_changed      — only when the FLOORED score actually moved.
    //   resonance_tier_up / _down    — only on a real tier transition.
    //
    // ⚠ tier up/down are SERVER events here, departing from WO-1684 D1's "as
    // seen" phrasing. The client never computes a tier (product rule 6;
    // heartbound-resonance.js:19-26) and cannot observe a transition that happened
    // during a daily cron while it was closed. See heartbound-telemetry.js's
    // header, departure 1 — it needs the lead's nod, and it is the only place the
    // transition is a fact rather than a repaint.
    const teBase = {
        playerId: state.playerId,
        atMs: now,
        pulseId: pulse.pulse_id,
        sequenceNumber: pulse.sequence_number,
        tier,
        tierName: view.tierName,
        resonanceScore: score,
        effectiveSkr: effectiveStake,
        actualSkr: nextState.actualSkr,
        continuousPulseCount,
    };
    await telemetry.safeEmit(emit, telemetry.buildEvent(TE.HEART_PULSE_PLAYER_PROCESSED, {
        ...teBase,
        status: GRANT_STATUS.GRANTED,
        extra: { totalLifetimePulses, highestLifetimeTier: nextState.highestLifetimeTier },
    }));
    if (priorView.resonanceScore !== score) {
        await telemetry.safeEmit(emit, telemetry.buildEvent(TE.RESONANCE_SCORE_CHANGED, {
            ...teBase,
            previousScore: priorView.resonanceScore,
            previousTier: priorView.tier,
        }));
    }
    if (tier !== priorView.tier) {
        await telemetry.safeEmit(emit, telemetry.buildEvent(
            tier > priorView.tier ? TE.RESONANCE_TIER_UP : TE.RESONANCE_TIER_DOWN, {
                ...teBase,
                previousTier: priorView.tier,
                previousScore: priorView.resonanceScore,
            }));
    }

    return {
        granted: true,
        playerId: state.playerId,
        resonanceScore: score,
        resonanceTier: tier,
        tierName: view.tierName,
        effectiveStake,
        continuousPulseCount,
    };
}

// --- D4: catch-up cap --------------------------------------------------------

/**
 * Spec :463-467. Pure function so the cap is testable without a database.
 * Oldest first, capped; the remainder is REPORTED (never silently dropped) so
 * "explicit reconciliation" has something to reconcile from.
 */
function selectCatchupPulses(pending, cap = MAX_CATCHUP_PULSES) {
    const ordered = (pending || []).slice().sort((a, b) => Number(a.sequence_number) - Number(b.sequence_number));
    return {
        toProcess: ordered.slice(0, cap),
        deferred: Math.max(0, ordered.length - cap),
    };
}

// --- The job -----------------------------------------------------------------

/**
 * ONE scheduled run. Three phases, in this order:
 *
 *   1. DETECT   — mint at most one pulse (at most one per UTC day).
 *   2. RESUME   — process EVERY pulse still sitting at status MINTED, oldest
 *                 first, capped. The pulse just minted is one of them, so the
 *                 happy path and the crash-recovery path are the SAME code:
 *                 a run that died half way through pulse 43 leaves 43 MINTED,
 *                 and the next run finishes it. There is no separate "the
 *                 pulse I just made" branch to forget to retry.
 *   3. SWEEP    — promote PENDING grants on pulses already marked COMPLETE
 *                 (the player whose RPC read failed while everyone else's
 *                 succeeded), capped per player at MAX_CATCHUP_PULSES.
 *
 * Phases 2 and 3 are D4's "retry later" — spec :451-461 chose "mark pending,
 * re-scan next run" over a queue precisely so this could be a table and a cron
 * (WO-1677 §5 forbids adding queue infrastructure), and this is that re-scan.
 * Every write in both phases is idempotent through the same ON CONFLICT gate,
 * so a re-run can never double-pay.
 */
async function runHeartPulseDetector(deps) {
    const { sql, now = Date.now() } = deps;
    const listActiveStates = deps.listActiveStates || defaultListActiveStates;

    // -- 1. DETECT --------------------------------------------------------
    const detection = await detectPulse(deps);

    // -- 2. RESUME every unfinished pulse ---------------------------------
    // LIMIT cap+1 so the overflow is REPORTED rather than invisible.
    const unfinished = await sql`
        SELECT pulse_id, sequence_number, detected_at_utc, status
        FROM global_heart_pulse
        WHERE status = ${PULSE_STATUS.MINTED}
        ORDER BY sequence_number ASC
        LIMIT ${MAX_CATCHUP_PULSES + 1}
    `;
    const resume = selectCatchupPulses(unfinished);

    // -- 3. Which players still owe a PENDING promotion on a CLOSED pulse --
    const pendingRows = await sql`
        SELECT g.player_id, g.global_pulse_id, p.sequence_number, p.detected_at_utc
        FROM player_pulse_grant g
        JOIN global_heart_pulse p ON p.pulse_id = g.global_pulse_id
        WHERE g.status = ${GRANT_STATUS.PENDING}
          AND p.status = ${PULSE_STATUS.COMPLETE}
        ORDER BY p.sequence_number ASC
    `;

    if (resume.toProcess.length === 0 && pendingRows.length === 0) {
        // Nothing to do: no pulse, no unfinished work, no debts. Note that the
        // HEART-002 and HEART-003 seams are NOT touched on this path — a quiet
        // day never fails on a seam it did not need.
        return {
            minted: detection.minted === true,
            reason: detection.reason,
            detail: detection,
            eligible: 0, processed: 0, pending: 0, skipped: 0, deferred: 0,
        };
    }

    const states = await listActiveStates(sql);
    const stateById = new Map(states.map((s) => [s.playerId, s]));
    const resonance = deps.resonance || resonanceModule;

    const outcomes = [];
    const runOne = async (pulse, state) => {
        // One bad player never takes the whole pulse down (CLAUDE.md §12 step 2:
        // "one bad object logs and is skipped, never silently").
        try {
            outcomes.push(await processPlayerForPulse({ ...deps, sql, pulse, state, resonance, now }));
        } catch (err) {
            console.error('[heart-pulse] player failed: ' + state.playerId + ': ' + err.message);
            outcomes.push({ granted: false, reason: 'error', playerId: state.playerId, error: err.message });
        }
    };

    for (const pulse of resume.toProcess) {
        for (const state of states) await runOne(pulse, state);
        await sql`
            UPDATE global_heart_pulse
            SET status = ${PULSE_STATUS.COMPLETE}, processed_at_utc = ${new Date(now).toISOString()}
            WHERE pulse_id = ${pulse.pulse_id}
        `;
    }

    // Catch-up, per player, oldest first, capped. Spec :463-467: the remainder
    // is REPORTED for explicit reconciliation, never silently paid out.
    let deferred = resume.deferred;
    const byPlayer = new Map();
    for (const row of pendingRows) {
        if (!byPlayer.has(row.player_id)) byPlayer.set(row.player_id, []);
        byPlayer.get(row.player_id).push(row);
    }
    for (const [playerId, owed] of byPlayer) {
        const state = stateById.get(playerId);
        if (!state) continue; // no longer an active Heartbound player
        const { toProcess, deferred: over } = selectCatchupPulses(owed);
        deferred += over;
        for (const row of toProcess) {
            await runOne({ pulse_id: row.global_pulse_id, sequence_number: row.sequence_number, detected_at_utc: row.detected_at_utc }, state);
        }
    }

    const processed = outcomes.filter((o) => o.granted).length;
    const pending = outcomes.filter((o) => o.reason === SKIP.PENDING_RPC || o.reason === SKIP.PENDING_STALE).length;
    return {
        minted: detection.minted === true,
        reason: detection.reason,
        pulseId: detection.minted ? detection.pulse.pulse_id : null,
        sequenceNumber: detection.minted ? detection.pulse.sequence_number : null,
        pulsesProcessed: resume.toProcess.length,
        caughtUp: byPlayer.size,
        eligible: states.length,
        processed,
        pending,
        skipped: outcomes.length - processed - pending,
        deferred,
    };
}

module.exports = {
    MAX_PULSES_PER_UTC_DAY,
    utcDayKey,
    STALE_GRACE_MS,
    MAX_CATCHUP_PULSES,
    PULSE_STATUS,
    GRANT_STATUS,
    SKIP,
    stakeReadingToSkr,
    detectPulse,
    processPlayerForPulse,
    selectCatchupPulses,
    runHeartPulseDetector,
};

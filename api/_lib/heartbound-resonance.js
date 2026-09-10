'use strict';

// =============================================================================
// api/_lib/heartbound-resonance.js - WO-1676 / HEART-003.
// THE RESONANCE MATHEMATICS AND THE ANTI-WHALE CURVE. PURE. NO I/O.
// -----------------------------------------------------------------------------
// Spec: docs/specs/HEARTBOUND_SKR_RESONANCE_WORK_ORDERS_2026-09-10.md:260-375.
//
// ⛔ NOTHING IN THIS FILE PERFORMS I/O. No network, no database, no fs, no clock,
// no randomness, no module-scope mutable state. The single `require` is the
// authored constant table next door, and every exported function takes that table
// as a trailing `cfg` argument so a caller (or a test) can inject its own. That is
// what makes "pure" a structural property here and not a promise in a comment:
// the same inputs return the same outputs on any machine, in any order, forever.
//
// ⛔ AND IT HAS NO CONSUMERS BY DESIGN. WO-1676 §3: "Do NOT wire a consumer in this
// ticket." The pulse loop (WO-1677), the benefit table (WO-1679) and the panel
// (WO-1681) each call in later, and two of the three are SPEC-blocked on an owner
// ruling. Shipping the arithmetic first is the spec's own Wave 1 (:1234-1240).
//
// WHY THE BACKEND AND NOT C#: product rule 7 (spec :22) - "The Unity client must
// never be trusted to report the amount of SKR staked" - and the architectural rule
// at :1274 - "UI contains no staking calculations". WO-1676 §5 states it as a
// prohibition: "Do not implement the curve in C#." There is therefore NO mirror
// class and NO Unity regression suite for this module; the client is TOLD its tier
// by the backend and never derives one. A display-only C# copy would be a second
// authority on a number product rule 6 makes the server's.
//
// -----------------------------------------------------------------------------
// THE THREE LAYERS, in call order:
//
//   1. BOUNDARY   sharesToRawTokens / rawTokensToSkr / skrFromShares
//                 u128 chain values -> SKR as a Number, converted ONCE, in BigInt.
//                 ...and BACK: skrToRawTokens / skrToNumericText (WO-1693). The
//                 crossing is bidirectional because SKR is what this module
//                 RETURNS and base units are what the columns STORE.
//   2. MATH       stakePower / tenurePower / resonanceScore / tierForScore
//                 The spec's formulae. Callable directly, because the spec's own
//                 vector table (:299-311) is StakePower alone.
//   3. TRANSITIONS activate / applyPulse / observeStake / evaluate
//                 The effective-stake state machine and the tier/lifetime rules.
//
// -----------------------------------------------------------------------------
// ⚠ PRECISION - THE ONE THING THIS MODULE MUST NOT INHERIT.
// The shipped client read truncates to whole SKR
// (Assets/_Modules/Wallet/NativeSkrStakeQuery.cs:76: `rawTokens / SkrBaseUnits`
// into a long), so 100.999999 SKR reads as 100. WO-1674 §D1 requires the snapshot
// carry raw base units instead, and this module accepts them.
//
// BigInt in, and it matters: shares and sharePrice are u128. Their product routinely
// exceeds Number.MAX_SAFE_INTEGER (2^53), and even the DIVIDED result does at large
// stakes - 1e10 SKR is 1e16 base units. So the multiply/divide is BigInt throughout
// and the single narrowing to Number is done as (whole) + (fraction/1e6), never as
// Number(raw)/1e6, which would lose the low digits exactly where a whale's stake sits.
//
// -----------------------------------------------------------------------------
// ⚠ ONE READING OF THE SPEC THAT THIS MODULE HAD TO PICK, STATED OUT LOUD.
// The spec gives an activation rule ("Initial activation: effectiveStake =
// actualStake x 0.25", :288-290) and a per-pulse rule (":292-294"), and says
// increases "ramp toward actualStake". It does not say what happens the INSTANT a
// staked player adds more SKR, between pulses. This module's reading:
//
//     * The 0.25 activation applies ONLY when a position activates from inactive
//       (no effective stake yet). It is not re-applied on a later top-up.
//     * A later INCREASE changes nothing until the next pulse. `actualStake` rises,
//       `effectiveStake` does not; the next pulse closes 25% of the new gap.
//     * A DECREASE clamps immediately: effectiveStake = MIN(actual, effective).
//
// That is the reading that satisfies the design objective at :270 - "temporarily
// staking large quantities cannot instantly reach maximum resonance" - under BOTH
// entry paths. The alternative (re-apply 0.25 of the new total on a top-up) would
// hand a returning whale 25% of an arbitrary stake with no pulse served, which is
// the flash-stake the asymmetry exists to kill. Pinned by the test case named
// [ramp] a top-up does not move effective stake until the next pulse.
//
// ⭐ AND HERE IS THE ASYMMETRY, IN ONE PLACE, BY ITSELF:
//       increases  -> gated behind applyPulse(), 25% of the gap per pulse
//       decreases  -> observeStake(), MIN(actual, effective), IMMEDIATE
// Spec :306: "This kills flash-staking exploits."
//
// -----------------------------------------------------------------------------
//     node --test test/heartbound-resonance.test.js
//
// Every constant below comes from ./heartbound-resonance-config.json. There are no
// numeric literals in the arithmetic. Zero network, zero database, zero Unity.
// =============================================================================

const DEFAULT_CONFIG = require('./heartbound-resonance-config.json');

// ── boundary: u128 chain values -> SKR ───────────────────────────────────────

/**
 * Coerce a chain-sourced u128 to BigInt. Accepts BigInt, a decimal string (the
 * shape a JSON snapshot must use - JSON has no BigInt) or a safe integer Number.
 * A Number above MAX_SAFE_INTEGER is REFUSED rather than silently rounded: a
 * u128 that arrived as a lossy double is not a number this module can honour.
 */
function toBigInt(value, what) {
    if (typeof value === 'bigint') return value;
    if (typeof value === 'string') {
        if (!/^\d+$/.test(value.trim())) throw new TypeError(`${what}: expected a non-negative integer string, got "${value}"`);
        return BigInt(value.trim());
    }
    if (typeof value === 'number') {
        if (!Number.isInteger(value)) throw new TypeError(`${what}: expected an integer, got ${value}`);
        if (!Number.isSafeInteger(value)) throw new TypeError(`${what}: ${value} exceeds Number.MAX_SAFE_INTEGER - pass a string or BigInt, not a rounded double`);
        return BigInt(value);
    }
    throw new TypeError(`${what}: expected BigInt, integer string or safe integer, got ${typeof value}`);
}

/**
 * shares x sharePrice / sharePriceScale, in BigInt, floored - the same expression
 * as NativeSkrStakeQuery.cs:75, and deliberately STOPPING there. The client's next
 * line divides by SkrBaseUnits and drops the fraction; this returns raw base units.
 * @returns {bigint} SKR in base units (1 SKR = 1e6).
 */
function sharesToRawTokens(sharesRaw, sharePriceRaw, cfg = DEFAULT_CONFIG) {
    const shares = toBigInt(sharesRaw, 'sharesRaw');
    const price = toBigInt(sharePriceRaw, 'sharePriceRaw');
    const scale = toBigInt(cfg.chain.sharePriceScale, 'chain.sharePriceScale');
    if (scale <= 0n) throw new RangeError('chain.sharePriceScale must be positive');
    return (shares * price) / scale;
}

/**
 * Base units -> SKR as a Number, narrowed ONCE and never through Number(raw)/1e6.
 * Whole part and fractional part are separated in BigInt first, so the integer
 * SKR count is exact for any stake a u128 can express.
 */
function rawTokensToSkr(rawTokens, cfg = DEFAULT_CONFIG) {
    const raw = toBigInt(rawTokens, 'rawTokens');
    const unit = toBigInt(cfg.chain.skrBaseUnits, 'chain.skrBaseUnits');
    if (unit <= 0n) throw new RangeError('chain.skrBaseUnits must be positive');
    const whole = raw / unit;
    const frac = raw % unit;
    return Number(whole) + Number(frac) / Number(unit);
}

/** The whole boundary in one call: chain u128 pair -> SKR. */
function skrFromShares(sharesRaw, sharePriceRaw, cfg = DEFAULT_CONFIG) {
    return rawTokensToSkr(sharesToRawTokens(sharesRaw, sharePriceRaw, cfg), cfg);
}

/**
 * ⭐ WO-1693 — THE INVERSE OF rawTokensToSkr, AND THE ONLY SANCTIONED WAY BACK.
 *
 * SKR (whole, and routinely FRACTIONAL - the 25% ramp yields 1750, 2312.5,
 * 2734.375 on the first three pulses) -> chain base units as a BigInt.
 *
 * ⛔ WHY IT LIVES HERE AND NOT AT THE CALL SITE. Every stake column downstream is
 *    NUMERIC(39,0) in RAW base units (api/migrations/20260910_0025_heartbound_state.sql:113)
 *    while this module's whole output surface is whole SKR. Before WO-1693 the
 *    return direction had NO conversion at all: heartbound-pulse.js handed
 *    `nextState.effectiveSkr` straight to a writer that coerces through a u128
 *    integer rule, so pulse 2 threw
 *    `expected a non-negative integer string, got "2312.5"` and killed the daily
 *    cron. The forward crossing was already this module's job (rawTokensToSkr);
 *    the return crossing is the same fact read the other way, and putting it
 *    anywhere else would be a second copy of `chain.skrBaseUnits`.
 *
 * ⚠ IT IS LOSSY BELOW ONE BASE UNIT, DELIBERATELY AND EXACTLY ONCE. A base unit is
 *   the finest amount the chain can express, so rounding to it invents nothing;
 *   the whole and fractional parts are split BEFORE the BigInt multiply so a large
 *   position never rides through a double. A carry (frac rounding up to a full
 *   unit) falls out of the addition and needs no special case.
 */
function skrToRawTokens(skr, cfg = DEFAULT_CONFIG) {
    const value = Number(skr);
    if (!Number.isFinite(value)) throw new TypeError(`skr: expected a finite number, got ${skr}`);
    if (value < 0) throw new RangeError(`skr must not be negative: ${value}`);
    const unit = toBigInt(cfg.chain.skrBaseUnits, 'chain.skrBaseUnits');
    if (unit <= 0n) throw new RangeError('chain.skrBaseUnits must be positive');
    const whole = Math.trunc(value);
    if (!Number.isSafeInteger(whole))
        throw new RangeError(`skr ${value} exceeds Number.MAX_SAFE_INTEGER - pass base units as a string or BigInt, not a rounded double`);
    const frac = value - whole;
    return BigInt(whole) * unit + BigInt(Math.round(frac * Number(unit)));
}

/**
 * SKR -> the fixed-point TEXT a NUMERIC(39,<scale>) stake column takes, where the
 * scale is DERIVED from `chain.skrBaseUnits` and is never a literal 6.
 *
 * ⛔ THE SCALE IS NOT A CONSTANT ANYWHERE. api/_lib/heartbound-pulse-schema.sql:107-113
 *    declares `effective_stake NUMERIC(39,6)` and states in the same breath WHY:
 *    "Six decimals is exactly the chain's base-unit granularity
 *    (heartbound-resonance-config.json chain.skrBaseUnits = 1e6)". A `toFixed(6)`
 *    at the call site would be that sentence copied into code, and would drift the
 *    day the base unit does. Deriving it makes the column's scale and the chain's
 *    granularity ONE fact.
 *
 * ⚠ `String(2734.375)` was what shipped before WO-1693. It happens to be valid
 *   NUMERIC text, and it is still wrong: a Number large enough prints as `1e+21`,
 *   which Postgres rejects, and one long enough prints more decimals than the
 *   column holds, which Postgres silently ROUNDS. Both are avoided by going
 *   through base units first.
 */
function skrToNumericText(skr, cfg = DEFAULT_CONFIG) {
    const unit = toBigInt(cfg.chain.skrBaseUnits, 'chain.skrBaseUnits');
    const decimals = unit.toString().length - 1;
    if (10n ** BigInt(decimals) !== unit)
        throw new RangeError(`chain.skrBaseUnits must be a power of ten to express a NUMERIC scale, got ${unit}`);
    const digits = skrToRawTokens(skr, cfg).toString().padStart(decimals + 1, '0');
    return decimals === 0 ? digits : `${digits.slice(0, -decimals)}.${digits.slice(-decimals)}`;
}

// ── eligibility ──────────────────────────────────────────────────────────────

/** Spec :274-280. Server-configurable floor; read from config, never a literal. */
function minimumStake(cfg = DEFAULT_CONFIG) {
    return Number(cfg.minHeartboundStake);
}

/**
 * Eligibility is judged on ACTUAL stake, not effective - the floor is "does this
 * player hold a Heartbound position", which is a fact about the chain, not about
 * how much trust the ramp has granted yet. Inclusive: exactly the floor qualifies.
 */
function isEligible(actualSkr, cfg = DEFAULT_CONFIG) {
    return Number(actualSkr) >= minimumStake(cfg);
}

// ── the effective-stake ramp (spec :284-306) ─────────────────────────────────

/** Activation from inactive: effectiveStake = actualStake x activationFraction. */
function activationEffectiveStake(actualSkr, cfg = DEFAULT_CONFIG) {
    const actual = Math.max(0, Number(actualSkr));
    return actual * Number(cfg.effectiveStake.activationFraction);
}

/** One pulse: effectiveStake += (actualStake - effectiveStake) x pulseRampFraction. */
function rampEffectiveStakeOnPulse(actualSkr, effectiveSkr, cfg = DEFAULT_CONFIG) {
    const actual = Math.max(0, Number(actualSkr));
    const effective = Math.max(0, Number(effectiveSkr));
    return effective + (actual - effective) * Number(cfg.effectiveStake.pulseRampFraction);
}

/**
 * ⭐ THE IMMEDIATE HALF OF THE ASYMMETRY. Spec :296-298.
 * effectiveStake = MIN(actualStake, effectiveStake). No ramp, no pulse, no delay.
 */
function clampEffectiveStakeToActual(actualSkr, effectiveSkr) {
    return Math.min(Math.max(0, Number(actualSkr)), Math.max(0, Number(effectiveSkr)));
}

// ── the curve (spec :317-347) ────────────────────────────────────────────────

/** StakePower = multiplier x log10(1 + EffectiveStake / divisor). No rounding here. */
function stakePower(effectiveSkr, cfg = DEFAULT_CONFIG) {
    const effective = Math.max(0, Number(effectiveSkr));
    return Number(cfg.stakePower.multiplier) * Math.log10(1 + effective / Number(cfg.stakePower.divisor));
}

/** TenurePower = MIN(cap, multiplier x ln(1 + ContinuousPulseCount / divisor)). */
function tenurePower(continuousPulseCount, cfg = DEFAULT_CONFIG) {
    const pulses = Math.max(0, Number(continuousPulseCount));
    return Math.min(
        Number(cfg.tenurePower.cap),
        Number(cfg.tenurePower.multiplier) * Math.log(1 + pulses / Number(cfg.tenurePower.divisor)));
}

/**
 * ResonanceScore = FLOOR(StakePower + TenurePower). Spec :345-347.
 * ⛔ THE ONLY FLOOR IN THE MODULE. Every intermediate stays full precision - rounding
 * a component would round twice and drift the tier boundaries the spec authored.
 * An ineligible position scores 0: below the floor there is no Heartbound position
 * to score, and a sub-floor holder must not sit one point under tier I.
 */
function resonanceScore(effectiveSkr, continuousPulseCount, actualSkr = null, cfg = DEFAULT_CONFIG) {
    if (actualSkr !== null && !isEligible(actualSkr, cfg)) return 0;
    return Math.floor(stakePower(effectiveSkr, cfg) + tenurePower(continuousPulseCount, cfg));
}

// ── tiers (spec :351-375) ────────────────────────────────────────────────────

function tierTable(cfg = DEFAULT_CONFIG) {
    return cfg.tiers.slice().sort((a, b) => a.minScore - b.minScore);
}

/**
 * The highest tier whose minScore the score reaches. Thresholds come from config
 * (spec :367) - there is no ladder literal anywhere in this file.
 * @returns {{tier:number, name:string, minScore:number}}
 */
function tierForScore(score, cfg = DEFAULT_CONFIG) {
    const table = tierTable(cfg);
    let found = table[0];
    for (const row of table) if (Number(score) >= row.minScore) found = row;
    return { tier: found.tier, name: found.name, minScore: found.minScore };
}

/**
 * The next threshold above `score`, or null at the ceiling.
 * Exists here rather than in the client on purpose: WO-1676 §4's sub-question -
 * the panel needs next-tier progress, and a client that re-derives it from a copied
 * threshold table is a second authority that WILL drift. The status endpoint returns
 * this; the UI renders it.
 */
function nextTierAt(score, cfg = DEFAULT_CONFIG) {
    for (const row of tierTable(cfg)) if (Number(score) < row.minScore) {
        return { tier: row.tier, name: row.name, minScore: row.minScore, pointsAway: row.minScore - Number(score) };
    }
    return null;
}

/**
 * Spec :371-375: "highestLifetimeTier never decreases." Monotone by construction -
 * the only way to lower it is to not call this.
 */
function highestLifetimeTier(previousHighest, currentTier) {
    return Math.max(Number(previousHighest) || 0, Number(currentTier) || 0);
}

// ── state transitions ────────────────────────────────────────────────────────
//
// State shape (the fields WO-1675's Neon row will carry; this module neither reads
// nor writes it - it takes one and returns a NEW one, never mutating the input):
//
//     { actualSkr, effectiveSkr, continuousPulseCount, highestLifetimeTier }

function freeze(state) {
    return {
        actualSkr: state.actualSkr,
        effectiveSkr: state.effectiveSkr,
        continuousPulseCount: state.continuousPulseCount,
        highestLifetimeTier: state.highestLifetimeTier,
    };
}

/** A never-staked position. */
function inactiveState() {
    return { actualSkr: 0, effectiveSkr: 0, continuousPulseCount: 0, highestLifetimeTier: 0 };
}

/**
 * Activate a position from inactive. Below the floor this is a no-op that records
 * the actual stake and grants nothing.
 */
function activate(actualSkr, previousState = null, cfg = DEFAULT_CONFIG) {
    const prev = previousState ? freeze(previousState) : inactiveState();
    const actual = Math.max(0, Number(actualSkr));
    if (!isEligible(actual, cfg)) {
        return { actualSkr: actual, effectiveSkr: 0, continuousPulseCount: 0, highestLifetimeTier: prev.highestLifetimeTier };
    }
    const next = {
        actualSkr: actual,
        effectiveSkr: activationEffectiveStake(actual, cfg),
        continuousPulseCount: 0,
        highestLifetimeTier: prev.highestLifetimeTier,
    };
    return withLifetime(next, cfg);
}

/**
 * One Heart Pulse: the tenure counter advances and the effective stake closes 25%
 * of its gap to actual. `actualSkr` is optional - pass the freshly-verified stake
 * when the pulse carries one, omit it to pulse against the state's own figure.
 *
 * ⛔ A pulse on an INELIGIBLE position grants nothing and does not advance tenure.
 * Its streak is not "continuous participation" (spec :337) if there is no position.
 */
function applyPulse(state, actualSkr = null, cfg = DEFAULT_CONFIG) {
    const prev = freeze(state);
    const actual = actualSkr === null ? prev.actualSkr : Math.max(0, Number(actualSkr));
    if (!isEligible(actual, cfg)) {
        return { actualSkr: actual, effectiveSkr: 0, continuousPulseCount: 0, highestLifetimeTier: prev.highestLifetimeTier };
    }
    // A decrease seen at pulse time still applies IMMEDIATELY, before the ramp -
    // the pulse must never be a laundering route around the asymmetry.
    const clamped = clampEffectiveStakeToActual(actual, prev.effectiveSkr);
    const effective = prev.effectiveSkr <= 0
        ? activationEffectiveStake(actual, cfg)              // first pulse on a position that was never activated
        : rampEffectiveStakeOnPulse(actual, clamped, cfg);
    return withLifetime({
        actualSkr: actual,
        effectiveSkr: effective,
        continuousPulseCount: prev.continuousPulseCount + 1,
        highestLifetimeTier: prev.highestLifetimeTier,
    }, cfg);
}

/**
 * A fresh stake reading arrives between pulses.
 *   DECREASE -> effective clamps to it, NOW.
 *   INCREASE -> recorded, and effective does NOT move until the next pulse.
 *   BELOW THE FLOOR -> the position ends: effective 0, streak broken. `highestLifetimeTier`
 *   survives, because spec :371-375 says it never decreases and :254-257 keeps lifetime
 *   achievements across a re-entry.
 */
function observeStake(state, actualSkr, cfg = DEFAULT_CONFIG) {
    const prev = freeze(state);
    const actual = Math.max(0, Number(actualSkr));
    if (!isEligible(actual, cfg)) {
        return { actualSkr: actual, effectiveSkr: 0, continuousPulseCount: 0, highestLifetimeTier: prev.highestLifetimeTier };
    }
    return withLifetime({
        actualSkr: actual,
        effectiveSkr: clampEffectiveStakeToActual(actual, prev.effectiveSkr),
        continuousPulseCount: prev.continuousPulseCount,
        highestLifetimeTier: prev.highestLifetimeTier,
    }, cfg);
}

function withLifetime(state, cfg) {
    const view = evaluate(state, cfg);
    return { ...state, highestLifetimeTier: highestLifetimeTier(state.highestLifetimeTier, view.tier) };
}

/**
 * The read model: everything a caller (endpoint, benefit table, panel) needs, derived,
 * never stored twice.
 */
function evaluate(state, cfg = DEFAULT_CONFIG) {
    const s = freeze(state);
    const eligible = isEligible(s.actualSkr, cfg);
    const sp = eligible ? stakePower(s.effectiveSkr, cfg) : 0;
    const tp = eligible ? tenurePower(s.continuousPulseCount, cfg) : 0;
    const score = eligible ? Math.floor(sp + tp) : 0;
    const tier = tierForScore(score, cfg);
    return {
        eligible,
        minimumStake: minimumStake(cfg),
        actualSkr: s.actualSkr,
        effectiveSkr: eligible ? s.effectiveSkr : 0,
        continuousPulseCount: eligible ? s.continuousPulseCount : 0,
        stakePower: sp,
        tenurePower: tp,
        resonanceScore: score,
        tier: tier.tier,
        tierName: tier.name,
        tierMinScore: tier.minScore,
        nextTier: nextTierAt(score, cfg),
        highestLifetimeTier: highestLifetimeTier(s.highestLifetimeTier, tier.tier),
    };
}

module.exports = {
    DEFAULT_CONFIG,
    // boundary
    toBigInt,
    sharesToRawTokens,
    rawTokensToSkr,
    skrFromShares,
    skrToRawTokens,
    skrToNumericText,
    // eligibility
    minimumStake,
    isEligible,
    // ramp
    activationEffectiveStake,
    rampEffectiveStakeOnPulse,
    clampEffectiveStakeToActual,
    // curve
    stakePower,
    tenurePower,
    resonanceScore,
    // tiers
    tierTable,
    tierForScore,
    nextTierAt,
    highestLifetimeTier,
    // transitions
    inactiveState,
    activate,
    applyPulse,
    observeStake,
    evaluate,
};

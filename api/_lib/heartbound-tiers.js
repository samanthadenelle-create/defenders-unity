'use strict';

// =============================================================================
// api/_lib/heartbound-tiers.js - WO-1679 / HEART-006 + WO-1682 / HEART-009.
// THE BENEFIT TABLE AND THE ECONOMIC-ACCELERATION METER. PURE. NO I/O.
// -----------------------------------------------------------------------------
// Spec: docs/specs/HEARTBOUND_SKR_RESONANCE_WORK_ORDERS_2026-09-10.md:617-748
// (HEART-006, the ten tiers) and :899-952 (HEART-009, the guardrails).
//
// ⛔ NOTHING IN THIS FILE PERFORMS I/O, and it inherits that contract from
// api/_lib/heartbound-resonance.js verbatim: two requires (its own authored table
// and the resonance module), every exported function takes a trailing `cfg`, no
// network, no database, no fs, no clock, no randomness, no module-scope mutable
// state. Same inputs, same outputs, on any machine, in any order, forever.
//
// -----------------------------------------------------------------------------
// ⛔ IT DOES NOT OWN THE LADDER, AND THAT IS THE WHOLE POINT.
//
// The tier ladder - which resonance score reaches which tier - lives in
// api/_lib/heartbound-resonance-config.json and is read ONLY through
// heartbound-resonance.js's tierForScore / nextTierAt. There is no threshold
// literal in this file and none in heartbound-tiers-config.json. Q-LADDER (RULED
// 2026-09-10 13:12) MERGED the two stake ladders that used to exist; a merge that
// left a second copy of the thresholds behind would have re-created, in one
// commit, the exact defect it was ruled to remove.
//
// So the shape is: heartbound-resonance.js answers WHICH TIER.
//                  this file answers WHAT THAT TIER GRANTS.
//
// -----------------------------------------------------------------------------
// ⛔ THE METER IS A PURE FUNCTION OVER A MODIFIER LIST, NOT A NUMBER IN A COMMENT.
//
// WO-1682 §0c, verbatim: "A ceiling with no meter is a comment, not a guardrail" -
// and it names three gates in this exact feature area that were cited as live
// protection and had never existed. `economicAccelerationMeter` is the answer to
// that: it takes a list of benefit rows and returns the measured sum, the ceiling,
// whether the sum is inside it, and - this is the part that keeps the ruling
// visible - the rows it did NOT count and why.
//
// ⛔ Q-METER (RULED 2026-09-10 13:36, owner, verbatim): "production-rate modifiers
// only - the 10% ceiling is the sum of the passive tier percentage boosts to
// resource yield; timers and event drops are not counted."
//
// The ruling is expressed as DATA, in heartbound-tiers-config.json's `benefitKinds`
// map (`countsTowardCeiling`), not as an `if` in this file. A future kind therefore
// has to declare its own answer, and cannot default into being uncounted.
//
// ⚠ Q-CLIENTECON (RULED 2026-09-10 13:32): the ceiling is computed here, on the
// server, and APPLIED ON THE CLIENT. api/game/save.js:410-421 records that
// farming / raiding / dungeons / arena "are simulated entirely on the client and
// reach this backend only inside the opaque save blob". So a modified client can
// ignore any of this. THAT IS ACCEPTED AND WRITTEN DOWN RATHER THAN PAPERED OVER:
// this cap is a DESIGN GUARDRAIL, NOT AN ANTI-CHEAT CONTROL - the same posture
// api/game/save.js:70-72 already takes for soft currency. Do not let a later seat
// read this module as an enforcement boundary; it bounds what we DESIGN, not what
// a hostile client can DO.
//
// -----------------------------------------------------------------------------
// ⛔ ATTEMPTS, NEVER ODDS. Q-LADDER folded the polish perk into this ladder, so a
// benefit row can now grant polish attempts. `polishGrantsForTier` therefore
// refuses any grant name but the two members IPolishBonusProvider actually has
// (PolishBonusProvider.cs:49-56). PolishBonusProvider.cs:20-23: "There is
// deliberately no member for odds, weights, luck, tier bias or a bonus table, and
// adding one would break the property the whole economy rests on." A row naming an
// odds-shaped grant THROWS here rather than being ignored.
//
// -----------------------------------------------------------------------------
//     node --test test/heartbound-tiers.test.js
//
// Zero network, zero database, zero Unity. Node built-ins only.
// =============================================================================

const DEFAULT_CONFIG = require('./heartbound-tiers-config.json');
const resonance = require('./heartbound-resonance');

/** The two grant names a polishAttempts row may carry, from config. */
function allowedPolishGrants(cfg = DEFAULT_CONFIG) {
    const list = cfg && cfg.polishGrants && cfg.polishGrants.allowed;
    return Array.isArray(list) ? list.slice() : [];
}

/**
 * The declared benefit vocabulary, from config. A kind not here is a defect.
 *
 * ⚠ Underscore-prefixed keys are the file's own prose (`_comment`), not kinds. They
 * are stripped HERE, once, so no caller has to know the convention - and so a
 * shape assertion over this map reads the kinds and nothing else.
 */
function benefitKinds(cfg = DEFAULT_CONFIG) {
    const raw = (cfg && cfg.benefitKinds) || {};
    const out = {};
    for (const k of Object.keys(raw)) if (k.charAt(0) !== '_') out[k] = raw[k];
    return out;
}

/**
 * Does this kind count toward the economic ceiling?
 *
 * ⛔ THE ANSWER IS DATA, NOT A BRANCH. An UNKNOWN kind THROWS rather than
 * defaulting to false: a benefit nobody classified must not be able to slip past
 * the meter by being new. That is the failure mode WO-1682 §0c exists to prevent.
 */
function countsTowardCeiling(kind, cfg = DEFAULT_CONFIG) {
    const spec = benefitKinds(cfg)[kind];
    if (!spec) {
        throw new RangeError(
            'heartbound benefit kind "' + kind + '" is not declared in ' +
            'heartbound-tiers-config.json benefitKinds. An unclassified benefit cannot be ' +
            'measured against the 10% ceiling, so it is refused rather than silently uncounted.');
    }
    return spec.countsTowardCeiling === true;
}

/** Every tier row in the authored table, tier-ascending. */
function tierRows(cfg = DEFAULT_CONFIG) {
    const rows = Array.isArray(cfg && cfg.tiers) ? cfg.tiers.slice() : [];
    return rows.sort((a, b) => Number(a.tier) - Number(b.tier));
}

/**
 * Validate one benefit row. Throws with the row's id, because "a benefit is
 * malformed" is not actionable and "tier 7 row echo_workers has no numeric value"
 * is. Called by every public entry point, so a bad table cannot reach the wire.
 */
function assertBenefit(row, tier, cfg) {
    if (!row || typeof row !== 'object') throw new TypeError('tier ' + tier + ' holds a non-object benefit');
    if (!row.id) throw new TypeError('tier ' + tier + ' holds a benefit with no id');
    const counts = countsTowardCeiling(row.kind, cfg);   // throws on an undeclared kind
    if (counts && !(typeof row.value === 'number' && isFinite(row.value) && row.value >= 0)) {
        throw new TypeError('tier ' + tier + ' benefit "' + row.id + '" is kind ' + row.kind +
            ', which counts toward the economic ceiling, so it must carry a non-negative ' +
            'numeric value. It carries ' + JSON.stringify(row.value) + '.');
    }
    if (row.kind === 'polishAttempts') {
        const allowed = allowedPolishGrants(cfg);
        if (allowed.indexOf(row.grant) < 0) {
            throw new RangeError('tier ' + tier + ' benefit "' + row.id + '" grants "' + row.grant +
                '", which is not one of ' + allowed.join(' / ') + '. IPolishBonusProvider has ' +
                'exactly two members and neither is an odds-shaped one - PolishBonusProvider.cs:20-23. ' +
                'STAKING BUYS ATTEMPTS, NEVER OUTCOMES.');
        }
        if (!Number.isInteger(row.value) || row.value < 0) {
            throw new TypeError('tier ' + tier + ' benefit "' + row.id + '" must grant a ' +
                'non-negative whole number of attempts; got ' + JSON.stringify(row.value) + '.');
        }
    }
    return row;
}

/**
 * The benefits a player AT `tier` holds - CUMULATIVE, because a Tier X player still
 * has Tier I's passive. Every returned row carries the `tier` it came from, so a
 * caller (and the panel) can say WHERE a benefit was earned without re-deriving it.
 *
 * Tier 0 holds nothing, and so does an unstaked, unverified or unknown player: the
 * caller passes 0 and gets an empty list. There is no "default benefit".
 */
function benefitsForTier(tier, cfg = DEFAULT_CONFIG) {
    const at = Number(tier);
    if (!isFinite(at) || at <= 0) return [];
    const out = [];
    for (const row of tierRows(cfg)) {
        if (Number(row.tier) > at) break;
        for (const b of (row.benefits || [])) {
            assertBenefit(b, row.tier, cfg);
            out.push(Object.assign({}, b, { tier: Number(row.tier) }));
        }
    }
    return out;
}

/** Every benefit in the whole ladder - the worst case the ceiling must survive. */
function allBenefits(cfg = DEFAULT_CONFIG) {
    const rows = tierRows(cfg);
    const top = rows.length ? Number(rows[rows.length - 1].tier) : 0;
    return benefitsForTier(top, cfg);
}

/**
 * The production-rate modifiers inside a benefit list. This is the ONLY list the
 * ceiling is measured over (Q-METER). Kept separate from the meter so a caller can
 * see the list it is about to be judged on.
 */
function productionRateModifiers(benefits, cfg = DEFAULT_CONFIG) {
    return (benefits || []).filter((b) => countsTowardCeiling(b.kind, cfg));
}

/**
 * ⭐ THE METER. A pure function over a modifier list.
 *
 * Sums `value` across the rows whose kind counts, and reports the rows it did not
 * count together with the reason - so "the ceiling is fine" is never a claim
 * without the working shown. WO-1682 acceptance 1: the metric is defined in ONE
 * place and its value is MEASURED, not asserted.
 *
 * @param {Array} benefits benefit rows (any kinds; it does the filtering)
 * @param {object} cfg
 * @returns {{sum:number, ceiling:number, withinCeiling:boolean, counted:Array,
 *           excluded:Array, definition:string}}
 */
function economicAccelerationMeter(benefits, cfg = DEFAULT_CONFIG) {
    const list = benefits || [];
    for (const b of list) countsTowardCeiling(b.kind, cfg);   // refuse an unclassified row

    const counted = [];
    const excluded = [];
    for (const b of list) {
        if (countsTowardCeiling(b.kind, cfg)) counted.push({ id: b.id, tier: b.tier, kind: b.kind, value: Number(b.value) });
        else excluded.push({ id: b.id, tier: b.tier, kind: b.kind, why: 'Q-METER: only productionRate kinds are counted' });
    }

    // Summed in a fixed order (the list's own), then rounded to 6 decimal places.
    // Floating-point addition is order-dependent, and a ceiling test that flips on
    // the 17th digit is a flaky gate. Six places is far finer than any authored value.
    let sum = 0;
    for (const c of counted) sum += c.value;
    sum = Math.round(sum * 1e6) / 1e6;

    const ceiling = Number(cfg && cfg.economicCeiling && cfg.economicCeiling.productionRateCeiling);
    if (!isFinite(ceiling) || ceiling < 0) {
        throw new RangeError('heartbound-tiers-config.json economicCeiling.productionRateCeiling ' +
            'is missing or not a non-negative number. A ceiling that cannot be read is the ' +
            '"imaginary gate" WO-1682 §0c exists to refuse.');
    }

    return {
        sum: sum,
        ceiling: ceiling,
        withinCeiling: sum <= ceiling,
        counted: counted,
        excluded: excluded,
        definition: 'Q-METER (owner, 2026-09-10 13:36): the sum of the passive tier ' +
                    'percentage boosts to resource yield. Timers and event drops are NOT counted. ' +
                    'Cumulative over the whole ladder, because a top-tier player holds every ' +
                    'lower tier\'s passive. Applied client-side: a design guardrail, NOT an ' +
                    'anti-cheat control (Q-CLIENTECON, and api/game/save.js:70-72 takes the ' +
                    'same posture for soft currency).',
    };
}

/** The whole ladder measured at once - the number the regression and the RESULT quote. */
function ladderMeter(cfg = DEFAULT_CONFIG) {
    return economicAccelerationMeter(allBenefits(cfg), cfg);
}

/**
 * The polish grants a tier carries, folded into IPolishBonusProvider's two members.
 * Q-LADDER: the Heartbound tier drives polish attempts. ⛔ ATTEMPTS ONLY.
 */
function polishGrantsForTier(tier, cfg = DEFAULT_CONFIG) {
    const out = { extraWeeklyRerolls: 0, rollCapDelta: 0 };
    for (const b of benefitsForTier(tier, cfg)) {
        if (b.kind !== 'polishAttempts') continue;
        out[b.grant] += Number(b.value);
    }
    return out;
}

/** The event-pool rows a tier has unlocked, by id. WO-1678 owns the pool itself. */
function unlockedEventIds(tier, cfg = DEFAULT_CONFIG) {
    return benefitsForTier(tier, cfg).filter((b) => b.kind === 'eventUnlock').map((b) => b.id);
}

// ── the injection seam api/_lib/heartbound-state.js already has ──────────────

/**
 * ⭐ THE RESOLVER `readHeartboundStatus` INJECTS.
 *
 * api/_lib/heartbound-state.js:385-410 takes an INJECTED `resolveTier` precisely so
 * it never holds the ladder, and reads exactly one field off the result:
 * `resolved.nextTierAt`, which it stringifies. So nextTierAt here is a SCALAR (the
 * next threshold's minScore), not the richer object heartbound-resonance.nextTierAt
 * returns - passing that object through would put "[object Object]" on the wire,
 * silently. The richer object is returned alongside, under `nextTier`, for a caller
 * that wants it.
 *
 * ⛔ AT THE TOP TIER nextTierAt IS null AND STAYS null. heartbound-state's own test
 * pins that "a resolver that cannot answer says so with null. It is never inferred
 * from the tier number."
 *
 * @param {{resonanceScore:string|number, resonanceTier:number, effectiveResonatingStake:string}} input
 * @returns {{nextTierAt:number|null, tier:number, tierName:string, nextTier:object|null,
 *            benefits:Array, meter:object, polish:object, unlockedEvents:Array}}
 */
function resolveTier(input, cfg = DEFAULT_CONFIG, resonanceCfg = undefined) {
    const raw = input || {};

    // ⛔ Number(null) IS 0, AND 0 IS A REAL SCORE. A plain `isFinite(Number(x))`
    // test therefore reads "we have no score" as "this player scored zero", which
    // silently demotes a stored Tier VII player to Tier 0 and takes every benefit
    // with it. The absent cases are rejected BEFORE the coercion, on purpose.
    // (Caught by this suite before it shipped, not theorised: the first version of
    // this function did exactly that and the fallback test went red.)
    const rawScore = raw.resonanceScore;
    const scoreAbsent = rawScore === null || rawScore === undefined ||
                        (typeof rawScore === 'string' && rawScore.trim() === '');
    const score = scoreAbsent ? NaN : Number(rawScore);
    const scoreKnown = isFinite(score);

    // The TIER is the resonance module's answer, never a re-derivation. When the
    // caller already carries a stored tier and the score is unreadable, the stored
    // tier is used rather than a guess - and when neither is readable, tier 0,
    // which grants nothing.
    let tier;
    let tierName;
    if (scoreKnown) {
        const t = resonance.tierForScore(score, resonanceCfg);
        tier = t.tier;
        tierName = t.name;
    } else {
        tier = Number(raw.resonanceTier);
        if (!isFinite(tier) || tier < 0) tier = 0;
        const row = resonance.tierTable(resonanceCfg).filter((r) => r.tier === tier)[0];
        tierName = row ? row.name : null;
    }

    const next = scoreKnown ? resonance.nextTierAt(score, resonanceCfg) : null;
    const benefits = benefitsForTier(tier, cfg);

    return {
        // The scalar heartbound-state.js reads and stringifies. null at the ceiling.
        nextTierAt: next ? next.minScore : null,
        tier: tier,
        tierName: tierName,
        nextTier: next,
        benefits: benefits,
        meter: economicAccelerationMeter(benefits, cfg),
        polish: polishGrantsForTier(tier, cfg),
        unlockedEvents: unlockedEventIds(tier, cfg),
    };
}

/**
 * The `benefits` block GET /api/heartbound/status puts on the wire.
 *
 * ⛔ THE CLIENT IS TOLD, IT NEVER DERIVES. Spec :1274 ("UI contains no staking
 * calculations") and product rule 6. Every number a Unity build needs to APPLY a
 * benefit is in here; nothing a Unity build could use to COMPUTE one is.
 *
 * Deliberately flat and boring: id, kind, value, tier. No thresholds, no scores,
 * no stake, no wallet - so a client holding this object still cannot reconstruct
 * the ladder, which is the property that makes the single-authority claim true
 * rather than merely stated.
 */
function statusBenefitsPayload(tier, cfg = DEFAULT_CONFIG) {
    const benefits = benefitsForTier(tier, cfg);
    const meter = economicAccelerationMeter(benefits, cfg);
    return {
        tier: Number(tier) || 0,
        rows: benefits.map((b) => ({
            id: b.id,
            kind: b.kind,
            tier: b.tier,
            value: typeof b.value === 'number' ? b.value : null,
            grant: b.grant || null,
        })),
        productionRateSum: meter.sum,
        productionRateCeiling: meter.ceiling,
        polish: polishGrantsForTier(tier, cfg),
        unlockedEvents: unlockedEventIds(tier, cfg),
    };
}

module.exports = {
    DEFAULT_CONFIG,

    benefitKinds,
    allowedPolishGrants,
    countsTowardCeiling,

    tierRows,
    benefitsForTier,
    allBenefits,

    productionRateModifiers,
    economicAccelerationMeter,
    ladderMeter,

    polishGrantsForTier,
    unlockedEventIds,

    resolveTier,
    statusBenefitsPayload,
};

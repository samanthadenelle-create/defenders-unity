'use strict';

// =============================================================================
// api/cron/heart-pulse.js — WO-1677 / HEART-004: the daily Heart Pulse detector.
// -----------------------------------------------------------------------------
// A thin shell: authenticate, open the DB, hand everything to
// api/_lib/heartbound-pulse.js (which holds ALL the logic and is unit-tested
// with an in-memory sql mock in test/heartbound-pulse.test.js).
//
// SCHEDULE — Q-CADENCE + Q-CRON, owner-ruled 2026-09-10: a Heart Pulse is RARE
// AND CEREMONIAL, AT MOST ONE PER DAY, and a third DAILY Vercel cron is
// accepted. vercel.json declared exactly two crons before this change, both
// daily (/api/admin/cleanup 04:00, /api/admin/google-play-voided-reconcile
// 04:30); this is the third, at 05:00, deliberately AFTER both so a long
// retention sweep never overlaps it. Worst-case pulse latency is therefore 24 h
// and the player sees their pulse on the next login after that — which is the
// stated consequence of the daily ruling, not an accident.
//
// ⛔ A SEPARATE ROUTE ON PURPOSE (WO-1677 §5): this work is NOT attached to
// /api/admin/cleanup's retention sweep, so a failure in one cannot take the
// other down.
//
// ⛔ NOT AN INJECTION ROUTE (Q-INJECT, owner-ruled): this endpoint only ever
// reads the chain and mints what the chain shows. It accepts NO share price,
// NO player id, NO pulse payload from the caller, and none may be added — a
// backend route cannot be compile-time excluded from production, so an
// injectable one would be a live route able to grant real rewards (spec :1224).
//
// AUTH — copied in shape from api/admin/cleanup.js:37-59 (read at source
// 2026-09-10): Authorization: Bearer <CRON_SECRET> (Vercel injects this on the
// scheduled invocation) OR X-Admin-Key == ADMIN_DASH_KEY, both SHA-256 then
// timingSafeEqual — never `===`, which is a timing oracle. Refusal is 400,
// never 401/403, and there are NO CORS headers: this is server-to-server.
//
// ⛔ THE TABLES DO NOT EXIST YET. api/_lib/heartbound-pulse-schema.sql holds the
// DDL because api/schema.sql is owned by the HEART-002 lane this wave; the LEAD
// must fold it in or run it as a migration. Until then this route answers 500
// on the first query, which is the correct, loud failure.
// =============================================================================

const crypto = require('crypto');

// Constant-time secret compare. Hash both sides so timingSafeEqual is
// length-safe and never leaks length. Same shape as api/admin/cleanup.js:37-43.
function secretOk(given, expected) {
    if (!given || !expected) return false;
    const a = crypto.createHash('sha256').update(String(given)).digest();
    const b = crypto.createHash('sha256').update(String(expected)).digest();
    return crypto.timingSafeEqual(a, b);
}

function isAuthorized(req) {
    const cronSecret = process.env.CRON_SECRET;
    if (cronSecret) {
        const auth = req.headers['authorization'] || '';
        const bearer = auth.startsWith('Bearer ') ? auth.slice(7) : '';
        if (bearer && secretOk(bearer, cronSecret)) return true;
    }
    const adminKey = process.env.ADMIN_DASH_KEY;
    if (adminKey && secretOk(req.headers['x-admin-key'], adminKey)) return true;
    return false;
}

module.exports = async (req, res) => {
    // No CORS surface — do NOT set Access-Control-Allow-Origin (never widen).

    // Vercel Cron issues GET; POST allowed for a manual admin run.
    if (req.method !== 'GET' && req.method !== 'POST') {
        return res.status(400).json({ error: 'Method not allowed' });
    }
    if (!isAuthorized(req)) {
        return res.status(400).json({ error: 'Unauthorized' });
    }

    try {
        // Required INSIDE the handler, not at module top level, so the logic
        // module and its tests never drag the DB driver onto the require path.
        const { neon } = require('@neondatabase/serverless');
        const { runHeartPulseDetector } = require('../_lib/heartbound-pulse.js');
        const sql = neon(process.env.DATABASE_URL);

        // Q1: this lane writes NO chain read. readSharePrice / readStake and the
        // two state seams stay UNWIRED here and default to placeholders that
        // throw a named error, so wiring HEART-001/HEART-002 is one line each
        // and a missing wire is loud rather than a silent zero.
        const summary = await runHeartPulseDetector({ sql, now: Date.now() });

        console.log('[cron/heart-pulse] ' + JSON.stringify({
            minted: summary.minted,
            reason: summary.reason || null,
            pulseId: summary.pulseId || null,
            eligible: summary.eligible || 0,
            processed: summary.processed || 0,
            pending: summary.pending || 0,
            skipped: summary.skipped || 0,
        }));

        return res.status(200).json({
            success: true,
            ran_at: new Date().toISOString(),
            minted: summary.minted,
            reason: summary.reason || null,
            pulse_id: summary.pulseId || null,
            eligible: summary.eligible || 0,
            processed: summary.processed || 0,
            pending: summary.pending || 0,
            skipped: summary.skipped || 0,
        });
    } catch (err) {
        // Q2 / spec :451-461 — a failed run changes nothing and is retried on
        // the next scheduled invocation. No streak is reset by a 500 here.
        console.error('[cron/heart-pulse] run failed:', err);
        return res.status(500).json({ error: 'Internal server error' });
    }
};

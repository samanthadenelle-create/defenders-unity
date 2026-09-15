// =============================================================================
// api/game/save.js — Vercel Serverless Function
// -----------------------------------------------------------------------------
// Persists a player's save into the player_data JSONB column in Neon Postgres.
//
// WHAT CHANGED 2026-08-02 (cloud save was DEAD in both directions):
//   1. AUTH IS NOW TWO RAILS, chosen by the SHAPE of playerId — see
//      _lib/wallet-auth.js. A base58 wallet id still demands a full ed25519
//      signature over the exact raw body plus a single-use nonce (UNCHANGED and
//      unweakened). A "guest-local-<64hex>" id — the id the Unity client already
//      mints for every un-connected player — takes the guest rail: X-Guest-Id,
//      rate-limited, explicitly second-class. Before this there was NO guest
//      path at all, so every "Play as Guest" tester was structurally unable to
//      reach the db, and the front door offers exactly that button.
//   2. EVERY refusal returns a STABLE CODE and writes an audit row. A 401 used
//      to be indistinguishable from no-header / bad-signature / replayed nonce /
//      expired nonce / wrong wallet, and left no server-side trace at all.
//   3. THE WHOLE SNAPSHOT IS STORED. The old delta builder cherry-picked 13
//      keys (resources, towers, bestWave, pets) out of a ~60-field save and
//      silently dropped the rest — base layout, obsidian queue, army, hero
//      level, echo lanes, quests, zones, everything a player would actually
//      mourn. Even with perfect auth, "cloud save" would have restored a husk.
//   4. CORS + preflight, so the WebGL/pinet build can reach this at all (a
//      cross-origin request with X-Wallet triggers an OPTIONS preflight that
//      this function never answered).
//   5. The body parser is genuinely disabled now — see the note at the bottom.
//
// WHAT CHANGED 2026-08-30 (WO-1282 PIN-1b — a THIRD rail):
//   A "play-<64hex>" id takes the GOOGLE PLAY rail: X-Session only, where the session
//   was minted by api/auth/google-session.js from a Google-signed ID token. The id is
//   HMAC'd server-side from the Google `sub`, so the client CANNOT mint one — which is
//   why it is a proven identity like the wallet and unlike the guest. The `trust` column
//   written below therefore gains a THIRD value, 'google', straight out of auth.mode:
//     'wallet' — ed25519 signature over a burned nonce.
//     'google' — Google-signed ID token, RS256-verified against Google's JWKS.
//     'guest'  — an unverified device hash. Bearer-token trust, explicitly second-class.
//     'legacy' — written before the rails existed (the column DEFAULT; never back-filled).
//   ⛔ The wallet remains the SOLE identity on the Seeker/APK artifact (owner ruling
//      2026-08-30); 'google' exists only for the Google Play / AAB artifact.
//
// Env vars: DATABASE_URL (Neon). Optional: GUEST_SAVE_ENABLED=false to kill the
// guest rail without a code change; GOOGLE_IDENTITY_ENABLED=true to arm the Play
// identity rail (default OFF — a play- id cannot authenticate at all until it is).
//
// Status codes: 200 | 400 | 401 | 404 | 409 | 500.
// ⚠ 409 IS NEW (2026-09-07, WO-1598) and this line used to end "(project constraint —
// no others)". The constraint was already untrue repo-wide — api/pi/complete.js and
// api/purchases/fulfill.js both answer 409 — and SAVE_RESET_STALE is exactly what 409
// means: not malformed (400) and not unauthorized (401), but in CONFLICT with the state
// the server already holds. Written down here in the same edit as the code (CLAUDE.md
// §15) rather than left for the next reader to discover in the handler.
// =============================================================================

const { neon } = require('@neondatabase/serverless');
const { decode } = require('@msgpack/msgpack');
const {
    AuthCode, authenticate, isGuestId,
    GUEST_MAX_BODY_BYTES, WALLET_MAX_BODY_BYTES,
} = require('../_lib/wallet-auth');
const { applyCors, newRef, quietFail, readBodyExact, bodyBytesDetail } = require('../_lib/http');
const { logAuthReject, logApiEvent } = require('../_lib/audit');
// WO-1243 operator kill switches. Fail-OPEN by ruling — see _lib/maintenance.js.
const {
    enforce: maintenanceEnforce,
    isClosed: maintenanceIsClosed,
    noteSealedActivity,
    AREA_SERVER, AREA_FARMING, AREA_RAIDING, AREA_DUNGEONS, AREA_ARENA,
} = require('../_lib/maintenance');

// ── Sanity-check bounds (WO-120 §2 — soft currency stays client-owned, with
//    server guards). Anti-grief / anti-corruption ceilings, NOT a server-
//    authoritative economy (see the BUILT-TO-FLIP seam at the bottom).
const MAX_RESOURCE   = 1_000_000_000; // any single soft-currency balance ceiling
const MAX_BEST_WAVE  = 100_000;       // implausible-wave ceiling
// A save may not DROP a monotonic counter (bestWave) at all, and may not drop a
// spendable balance by more than this fraction in one sync.
const MAX_BALANCE_DROP_FRACTION = 0.95;

// Keys that are transport, not game state — never stored inside game_state.
// WO-1598: `resetEpoch` is TRANSPORT, not game state. It lives in its own column
// (player_data.reset_epoch) precisely so it cannot be edited by the shallow JSONB
// merge that the blob goes through — a reset marker stored inside the thing whose
// acceptance it governs would be circular.
const RESERVED_KEYS = new Set([
    'playerId', 'PlayerId', 'schemaVersion', 'SchemaVersion',
    'resetEpoch', 'ResetEpoch',
    'wallet', 'nonce', 'signature', 'guestId',
    // Local crash-recovery intent is never cloud gameplay state or authority to grant another capture.
    'pendingTownCapture', 'PendingTownCapture',
]);

// The numeric fields the anti-tamper guards police. Balances live in TWO places
// in the live payload — flat top-level (legacy) and nested under "resources"
// (the live PersistedState shape) — and a guard that only checked one of them
// would be trivially bypassed by writing the other.
// WO-1212: 'stone' DELIBERATELY STAYS in this list even though the balance is retired.
// These are CEILINGS (MAX_RESOURCE / non-negative), never grants - guarding a legacy key
// that an old client may still send is strictly more conservative than dropping it, and the
// client aliases an inbound `stone` onto the live slot when no `resources` block is present.
const GUARDED_BALANCES = ['crystals', 'food', 'coins', 'voidshards', 'stone', 'iron', 'wood'];
const NESTED_BALANCES  = ['crystals', 'food', 'coins'];   // the ResourceBalance struct's own fields

// ── SCHEMA VERSION IS MONOTONIC (WO-1457) ────────────────────────────────────
// It was not. The upsert wrote `schema_version = EXCLUDED.schema_version` and an
// ABSENT version defaulted to 10, so an old build, a replayed request, or a payload
// that simply omitted the field stamped the row back to 10 *while writing
// current-shaped state*. SaveSchema.CurrentVersion is 38. The next load then runs
// the wrong migration chain over data that is not shaped for it — a corruption that
// happens at LOAD time, far from the save that caused it.
//
// Two independent defences, because either alone is a near-miss:
//   1. judgeSchemaVersion judges the declared version, so a stale client is VISIBLE
//      (a named code in the response and an audit row) instead of quietly winning.
//   2. GREATEST() on the upsert, so the stored version cannot regress even if some
//      future path reaches the SQL without passing the judgement.
// Refusal codes are local to this route rather than added to wallet-auth's AuthCode:
// these are not authentication outcomes, and AuthCode is another lane's file.
//
// ⛔ THE ABSENT ARM IS RETIRED AND REPLACED (2026-09-07, P0 field outage).
// WO-1457's ruling was, verbatim: *"AN ABSENT VERSION IS A MALFORMED PAYLOAD"* — and
// it therefore returned 400 SCHEMA_VERSION_MISSING. That was CORRECT for the client
// of the future and WRONG for the client in the field. The shipped Android build does
// not send the field at all (GameStateService built the POST body without it; the fix
// exists in the working tree and reaches players only with the NEXT build, while the
// store build of 2026-08-17 and every tester APK omit it forever). So from the moment
// this deployed, EVERY save from EVERY device in the field was refused:
//   09-07 07:49 local — saves landing;  09-07 07:53 — 400 {"ok":false,
//   "code":"SCHEMA_VERSION_MISSING"} on every POST, cloud save down for everyone.
// A guard that refuses 100% of real traffic is not a guard, it is an outage.
//
// THE CORRECTED RULING, stated honestly:
//   absent/unparseable = "UNKNOWN — DO NOT TOUCH THE VERSION."   (never "malformed,
//   drop the save"; and still never "v10", which was the original invention.)
// So an absent version now ACCEPTS the write, leaves schema_version exactly as it is
// (see the version-less upsert below), and stays VISIBLE — `schemaVersionNote:
// 'SCHEMA_VERSION_ABSENT'` in the 200 body plus the same audit row the refusal wrote.
// The DOWNGRADE refusal and the GREATEST() clamp are UNCHANGED: a client that names
// an OLDER version has told us something, and that claim is still refused.
const SaveCode = {
    // ⚠ RETIRED as an OUTCOME (2026-09-07) — nothing returns this any more; see above.
    // The constant stays because the CLIENT still mirrors the string
    // (Assets/Editor/Regression/SyncDrainReasonRegression.cs + GameStateService), and
    // deleting the name here would only hide that the two sides now disagree.
    SCHEMA_VERSION_MISSING:   'SCHEMA_VERSION_MISSING',
    SCHEMA_VERSION_ABSENT:    'SCHEMA_VERSION_ABSENT',   // absent/unparseable — ACCEPTED, version untouched
    SCHEMA_VERSION_DOWNGRADE: 'SCHEMA_VERSION_DOWNGRADE', // older than what is stored — a stale client
    // WO-1598 — the incoming save declares an epoch BELOW the stored one: an old
    // device replaying a town the player has already left behind. 409, not 400: the
    // payload is well-formed and authorized, it simply conflicts with what is stored.
    SAVE_RESET_STALE:         'SAVE_RESET_STALE',
};

// ── A NEW GAME IS NOT A ROLLBACK (WO-1598) ───────────────────────────────────
// ⛔ THE DEFECT THIS EXISTS TO CLOSE, measured in analytics_events 2026-09-07:
// 177 `save_sanity_reject` rows in 14 days across 7 ids, every one of them either
// `rollback:bestWave` (156) or `implausible_drop` on a balance (139 wood / 103 iron /
// 93 crystals / 89 coins / 85 food). The owner's own wallet row, ELEVEN times between
// 00:39Z and 10:26Z: `implausible_drop crystals 901 -> 36`. She had started a new game
// that morning. A new town holds 13-36 crystals and wave 0; the stored row held the OLD
// town. applyGuards read every field of the legitimate new save as tampering.
//
// And the cost is not the rejection — it is what the rejection LEAVES BEHIND. Rejected
// fields are STRIPPED and the rest of the save lands, so the row ends up holding the old
// town's balances beside the new town's everything-else, and api/game/load.js hands
// those old balances straight back on the next load. The new game never exists in the
// cloud, and 901 crystals reappear over a town that has 36.
//
// THE SEAM: a save may DECLARE a reset by carrying a monotonic integer `resetEpoch`,
// persisted client-side and bumped by ResetToNewGame. Newer than what is stored = this
// is a reset, so the COMPARATIVE guards stand down for that one write and the epoch
// advances. Equal = an ordinary save, guarded exactly as before. Older = a stale device,
// refused by name.
//
// ⛔ NEVER A BARE FLAG, and this is the whole reason the field is an epoch. A boolean
// `isReset:true` is replayable forever: capture one such request and you can wipe any
// guard on any future save. An epoch can only ever be spent ONCE, because accepting it
// advances the stored value past it — the same monotonic argument as schema_version
// above, and the same GREATEST() belt on the upsert.
//
// ⚠ AND A RESET IS NOT A BLANK CHEQUE. Only the COMPARATIVE rules (implausible_drop,
// bestWave rollback) stand down — they are the rules that compare against the prior row,
// and a reset is precisely the case where the prior row is not a valid comparison. The
// BOUNDS (non-finite / negative / > MAX_RESOURCE / > MAX_BEST_WAVE) are properties of the
// value ALONE and still run, so `resetEpoch` can never be used to land an impossible
// balance. Implemented by handing applyGuards an EMPTY prior rather than by skipping it —
// the same substitution the prior-read-failure path already makes, so there is one
// mechanism for "no valid comparison", not two.

/**
 * Decide what an incoming save's declared reset epoch means.
 *
 * Pure and exported so the four cases that matter (absent / newer / equal / older)
 * are provable without a database — see test/game.save.reset-epoch.test.js.
 *
 * ⚠ ABSENT IS TODAY'S BEHAVIOUR, EXACTLY. Every client shipped before this omits the
 *   field, and WO-1457's absent-version arm is the standing lesson on what happens when
 *   a new field's absence is treated as a fault: cloud save went down for the whole
 *   field in four minutes. So absent = no bypass, no epoch written, and deliberately NO
 *   audit row — an event on every save from every old client is a behaviour change, not
 *   a diagnosis.
 * ⚠ OUT-OF-RANGE IS ABSENT, NOT AN ERROR. player_data.reset_epoch is INTEGER, so a
 *   value past INT32 would raise on the upsert and 500 a save that is otherwise fine —
 *   the client's save path DoSing itself on a field it made up. Negative and
 *   non-integer are the same class of nonsense and take the same exit.
 *
 * @param {*} incoming  body.ResetEpoch ?? body.resetEpoch
 * @param {*} stored    reset_epoch on the existing row, or null when never reset/unknown
 * @returns {{ok:true, epoch:number|null, bypass:boolean, from:number|null}
 *          | {ok:false, code:string, incoming:number, stored:number}}
 *          `epoch:null` means WRITE NO EPOCH — the client declared none.
 */
function judgeResetEpoch(incoming, stored) {
    const MAX_EPOCH = 2147483647;                 // INTEGER column ceiling
    const v = typeof incoming === 'number' ? incoming
            : (typeof incoming === 'string' && incoming.trim() !== '' ? Number(incoming) : NaN);
    if (!Number.isInteger(v) || v < 0 || v > MAX_EPOCH) {
        return { ok: true, epoch: null, bypass: false, from: null };
    }

    const rawStored = stored == null ? NaN : Number(stored);
    const s = Number.isInteger(rawStored) && rawStored >= 0 ? rawStored : null;

    if (s == null) {
        // Nothing stored: the row has never declared an epoch. A positive epoch is a
        // reset this server has not seen; a 0 is simply "I have never reset", which is
        // an ordinary save and must NOT bypass anything.
        return { ok: true, epoch: v, bypass: v > 0, from: null };
    }
    if (v > s) return { ok: true, epoch: v, bypass: true,  from: s };
    if (v === s) return { ok: true, epoch: v, bypass: false, from: s };
    return { ok: false, code: SaveCode.SAVE_RESET_STALE, incoming: v, stored: s };
}

/**
 * Decide whether an incoming save may write at the version it declares.
 *
 * Pure and exported so the four cases that matter (absent / downgrade / equal /
 * upgrade) are provable without a database — see test/game.save.schema-version.test.js.
 *
 * ⚠ AN ABSENT VERSION IS NOT A v10 PAYLOAD (defaulting invented a fact about state it
 *   had never inspected) AND IT IS NOT A DROPPED SAVE EITHER (refusing it took cloud
 *   save down for every client in the field — see the block above). It is UNKNOWN:
 *   accept the write, touch nothing, and say so out loud. `ok:true, version:null`.
 * ⚠ NO STORED VERSION (a first save, or a prior-state read that failed) accepts
 *   whatever is declared. There is nothing to regress FROM, and a save must never
 *   be lost because the guard could not read its comparison.
 *
 * @param {*} incoming  body.SchemaVersion ?? body.schemaVersion
 * @param {*} stored    schema_version on the existing row, or null/NaN if unknown
 * @returns {{ok:true, version:number|null, note?:string, incoming?:*, stored?:*}
 *          | {ok:false, code:string, incoming:*, stored:*}}
 *          `version:null` means DO NOT WRITE A VERSION — not "write zero/ten".
 */
function judgeSchemaVersion(incoming, stored) {
    const v = typeof incoming === 'number' ? incoming
            : (typeof incoming === 'string' && incoming.trim() !== '' ? Number(incoming) : NaN);
    if (!Number.isInteger(v) || v <= 0) {
        return {
            ok: true, version: null,
            note: SaveCode.SCHEMA_VERSION_ABSENT,
            incoming: incoming, stored: stored,
        };
    }
    const s = Number(stored);
    if (Number.isFinite(s) && v < s) {
        return { ok: false, code: SaveCode.SCHEMA_VERSION_DOWNGRADE, incoming: v, stored: s };
    }
    return { ok: true, version: v };
}

async function handler(req, res) {
    if (applyCors(req, res, 'POST, OPTIONS')) return;

    const ref = newRef();

    if (req.method !== 'POST') {
        return quietFail(res, 400, AuthCode.METHOD_NOT_ALLOWED, ref);
    }

    // ── Read raw body ──────────────────────────────────────────────────────
    // Read at the WALLET ceiling: which rail applies isn't known until playerId
    // has been parsed OUT of this body. The tighter guest cap is applied below,
    // once the identity is known.
    let rawBody, exactBytes;
    try {
        const read = await readBodyExact(req, WALLET_MAX_BODY_BYTES);
        rawBody = read.buffer;
        exactBytes = read.exact;
    } catch (err) {
        if (err && err.code === 'BODY_TOO_LARGE') {
            await logAuthReject(null, req, { code: AuthCode.PAYLOAD_TOO_LARGE, ref, detail: { cap: WALLET_MAX_BODY_BYTES } });
            return quietFail(res, 400, AuthCode.PAYLOAD_TOO_LARGE, ref);
        }
        console.error('[save] Body read error:', err);
        return quietFail(res, 500, AuthCode.SERVER_ERROR, ref);
    }

    // ── Decode (MessagePack or JSON) ───────────────────────────────────────
    let body;
    try {
        const isMsgPack = req.headers['content-type'] === 'application/x-msgpack';
        body = isMsgPack ? decode(rawBody) : JSON.parse(rawBody.toString('utf8'));
    } catch (err) {
        console.error('[save] Decode error:', err.message, 'bytes=', rawBody ? rawBody.length : 0);
        await logAuthReject(null, req, {
            code: AuthCode.BAD_PAYLOAD, ref,
            detail: { bytes: rawBody ? rawBody.length : 0, exactBytes: exactBytes },
        });
        return quietFail(res, 400, AuthCode.BAD_PAYLOAD, ref);
    }
    if (!body || typeof body !== 'object') {
        return quietFail(res, 400, AuthCode.BAD_PAYLOAD, ref);
    }

    // The client posts camelCase "playerId"; the legacy MsgPack path used
    // "PlayerId". Accept either.
    const playerId = body.PlayerId != null ? body.PlayerId
                   : body.playerId != null ? body.playerId
                   : null;
    const schemaVersion = body.SchemaVersion != null ? body.SchemaVersion : body.schemaVersion;
    // WO-1598 — both spellings, for the same reason playerId accepts both.
    const resetEpoch = body.ResetEpoch != null ? body.ResetEpoch : body.resetEpoch;

    let sql;
    try {
        sql = neon(process.env.DATABASE_URL);
    } catch (err) {
        console.error('[save] DB init error:', err);
        return quietFail(res, 500, AuthCode.SERVER_ERROR, ref);
    }

    if (!playerId) {
        await logAuthReject(sql, req, { code: AuthCode.PLAYER_ID_MISSING, ref, detail: { keys: Object.keys(body).length } });
        return quietFail(res, 400, AuthCode.PLAYER_ID_MISSING, ref);
    }

    // Guest bodies get the tighter cap now that we know the rail.
    if (isGuestId(String(playerId)) && rawBody.length > GUEST_MAX_BODY_BYTES) {
        await logAuthReject(sql, req, {
            code: AuthCode.PAYLOAD_TOO_LARGE, ref, identity: playerId, mode: 'guest',
            detail: { bytes: rawBody.length, cap: GUEST_MAX_BODY_BYTES },
        });
        return quietFail(res, 400, AuthCode.PAYLOAD_TOO_LARGE, ref);
    }

    // ⛔ THE RAW-BODY REFUSAL IS RETIRED HERE (WO-1453, 2026-09-06). It used to read:
    //
    //     if (!exactBytes && !isGuestId(id) && !isPlayId(id) && !hasSessionHeader)
    //         -> 500 SERVER_ERROR, detail { reason: 'raw_body_unavailable_bodyparser_active' }
    //
    // ⚠ ITS COST HAS ALREADY BEEN PAID TWICE, WHICH IS WHY IT IS GONE RATHER THAN SCOPED
    // A THIRD TIME:
    //   1. 2026-08-24 — it did not know WO-1157's session rail existed (verifyWallet accepts
    //      an `x-session` bearer and returns via:'session' WITHOUT EVER TOUCHING `payload`),
    //      so EVERY wallet-authed save 500ed in production. `player_data` held 21 rows and
    //      every one was `guest-local-*`; not one save had ever been written under a wallet
    //      identity. It hid because the guest id is derived from the device, so the town
    //      persisted under the wrong key while the identity purchases bind to had nothing
    //      behind it. Scoping it to the signature path was the fix. WO-1282 PIN-1b then
    //      widened the same scoping to `play-` ids for the same reason.
    //   2. 2026-09-06 — the SCOPED guard still 500ed the signature rail itself, proven on
    //      prod in WORK_ORDER_1440_..._blocker.RESULT.md:225-252 with a cryptographically
    //      valid signature over the exact bytes. Vercel's Node 24 runtime parses `req.body`
    //      regardless of `config.api.bodyParser`, so `exact` is effectively always false in
    //      production. Twice now the guard has refused a request that was entitled to pass.
    //
    // We now PROCEED and TAG. See _lib/http.bodyBytesDetail for why that cannot create a
    // false accept: sha256(payload) is bound into the signed message, so a wrong
    // reconstruction fails CLOSED as a 401. The guard's one real value — the diagnosis —
    // survives as a detail tag on the reject row below, through ONE shared helper rather
    // than the copy-paste that let these endpoints drift apart in the first place.

    // ── AUTH GATE ──────────────────────────────────────────────────────────
    let auth;
    try {
        auth = await authenticate(sql, req, rawBody, playerId);
    } catch (err) {
        console.error('[save] Auth check error:', err);
        return quietFail(res, 500, AuthCode.SERVER_ERROR, ref);
    }
    if (!auth.ok) {
        await logAuthReject(sql, req, {
            code: auth.code, ref, identity: auth.identity, mode: auth.mode,
            // WO-1453: when the runtime parsed the body out from under us, say so ON THE
            // REJECT ROW. An AUTH_BAD_SIGNATURE carrying bytes:'reconstructed' is worth a
            // second look; one without it is simply a bad signature.
            detail: Object.assign({}, auth.detail, bodyBytesDetail(exactBytes)),
        });
        // 400 for a shape/argument problem, 401 for a genuine authorization
        // refusal — the client can retry the second (fetch a fresh nonce) and
        // must never retry the first.
        const status = (auth.code === AuthCode.PLAYER_ID_BAD_SHAPE ||
                        auth.code === AuthCode.PLAYER_ID_MISSING ||
                        auth.code === AuthCode.WALLET_MALFORMED) ? 400 : 401;
        return quietFail(res, status, auth.code, ref);
    }

    // ── OPERATOR KILL SWITCH: the FULL maintenance window (WO-1243) ────────
    //
    // Only the `server` toggle refuses here, and it refuses everything. This is
    // the one server-side seal that reaches farming, raiding and dungeons at all
    // (see the note below) and it is deliberately blunt: a full maintenance
    // window means no client state lands, so nothing an exploit fabricated can
    // be written down while she patches.
    //
    // ⚠ THE COST, STATED: a sealed save is progress the player made and cannot
    // persist. That is the trade a maintenance window IS. The client is told the
    // reason (code AREA_UNDER_MAINTENANCE + the operator's message) so it can
    // hold the payload and show the banner rather than silently dropping it.
    if (await maintenanceEnforce(sql, req, res, AREA_SERVER, playerId, ref)) return;

    // ── AND THE HONEST GAP, RECORDED RATHER THAN PAPERED OVER ──────────────
    //
    // ⛔ farming / raiding / dungeons / arena have NO per-action endpoint. They
    // are simulated entirely on the client and reach this backend only inside the
    // opaque save blob, so there is nothing here to refuse that is not the whole
    // save. Sealing the save for a single sealed AREA would punish every unrelated
    // thing the player did in the same session, so we do not.
    //
    // What we CAN do is stop the gap being invisible: if a save arrives while one
    // of those areas is sealed, stamp a row. That is the evidence that answers
    // "did the client gate actually hold, or is someone still farming a sealed
    // area?" after the fact — which is the whole reason containment keeps a record.
    // It is a RECORD, NOT A CONTROL. Do not mistake it for enforcement.
    try {
        const sealed = [];
        for (const area of [AREA_FARMING, AREA_RAIDING, AREA_DUNGEONS, AREA_ARENA]) {
            const v = await maintenanceIsClosed(sql, area);
            if (v.closed) sealed.push(area);
        }
        if (sealed.length) await noteSealedActivity(sql, req, sealed, playerId, ref);
    } catch (err) {
        // Never let the audit path fail a save. One console line and carry on.
        try { console.warn('[maintenance] sealed-activity note failed:', err && err.message); }
        catch (_) { /* noop */ }
    }

    // ── Build the state to persist ─────────────────────────────────────────
    const delta = buildState(body);

    if (Object.keys(delta).length === 0) {
        return res.status(200).json({ ok: true, success: true, serverNowMs: Date.now(), note: 'empty payload — no write', ref: ref });
    }

    // ── SANITY-CHECK GUARDS (WO-120 §2) ────────────────────────────────────
    let prior = {};
    // WO-1128 — player_data.updated_at IS this table's last_seen: it is stamped
    // NOW() by the server on every ACCEPTED save and the client cannot write it.
    // That is the whole reason it can anchor the reconciliation below. We reuse it
    // rather than minting a second last_seen column (WO-1128 §3.1: reuse the
    // convention, do not invent a parallel one — guest_rate_limit.last_seen is the
    // other user of the name and means the same thing).
    let priorSeenMs = null;
    // WO-1457: read on the EXISTING query. The stored version is a column this
    // statement already had to visit; a second round trip for it, on the hottest
    // endpoint in the game, would be the wrong trade. Stays null when the read
    // fails, which the judgement reads as "nothing to regress from".
    let priorSchemaVersion = null;
    // WO-1598: read on the SAME statement, for the same reason as the version — it is a
    // column this query already visits, and the save path is the hottest endpoint in the
    // game. Stays null when the read fails, which the judgement reads as "never reset",
    // i.e. a declared epoch is honoured rather than a save being lost to an unreadable
    // comparison.
    let priorResetEpoch = null;
    try {
        const priorRows = await sql`
            SELECT game_state, updated_at, schema_version, reset_epoch FROM player_data WHERE player_id = ${playerId} LIMIT 1
        `;
        if (priorRows.length > 0) {
            if (priorRows[0].game_state) prior = priorRows[0].game_state;
            // ⛔ NULL MUST NOT BECOME ZERO. Since migration 20260907_0022 the column is
            // nullable, and NULL means "this row never declared a version" — the same
            // "unknown" this local is initialised to. `Number(null)` is 0, NOT NaN, so a
            // bare Number()+isFinite would silently record version ZERO as a real stored
            // version. That happens to be harmless for today's judgement (the only test
            // is the downgrade `v < s`, and nothing is below 0), but it is a fabricated
            // number sitting in a variable named "prior", and it is what
            // save_schema_version_refused would print as `stored` in an incident. The
            // null check is explicit so the next reader of this local can trust it.
            const rawSv = priorRows[0].schema_version;
            const sv = rawSv == null ? NaN : Number(rawSv);
            if (Number.isFinite(sv) && sv > 0) priorSchemaVersion = sv;
            // Same null discipline as the version above, and it MATTERS MORE here: 0 is a
            // LEGAL epoch ("never reset"), so `Number(null)` folding to 0 would fabricate a
            // stored epoch that the judgement then compares against for real — an incoming
            // 0 would read as "equal" instead of "nothing stored". Explicit null check.
            const rawEpoch = priorRows[0].reset_epoch;
            const re = rawEpoch == null ? NaN : Number(rawEpoch);
            if (Number.isInteger(re) && re >= 0) priorResetEpoch = re;
            // The Neon HTTP driver returns TIMESTAMPTZ as a Date OR an ISO string
            // depending on the column/driver path; handle both, and treat an
            // unparseable value as "no anchor" rather than as the epoch (which would
            // make serverElapsedSec enormous and pass every fabricated window).
            const raw = priorRows[0].updated_at;
            const t = raw == null ? NaN : (raw instanceof Date ? raw.getTime() : Date.parse(String(raw)));
            if (Number.isFinite(t)) priorSeenMs = t;
        }
    } catch (err) {
        // A read failure shouldn't block the save; skip the comparative guards
        // (the bounds checks below still apply).
        console.warn('[save] prior-state read failed, skipping rollback guards:', err.message);
    }

    // ── WO-1457 — the version may not go BACKWARDS, and may not be invented ──
    // Judged BEFORE the guards and the write: a refused save must leave the row
    // exactly as it was, not half-applied at the old version.
    const versionJudgement = judgeSchemaVersion(schemaVersion, priorSchemaVersion);
    if (!versionJudgement.ok) {
        console.warn('[save] schema version refused:', JSON.stringify(versionJudgement));
        await logApiEvent(sql, playerId, 'save_schema_version_refused', {
            ref: ref, mode: auth.mode, code: versionJudgement.code,
            incoming: versionJudgement.incoming ?? null,
            stored: versionJudgement.stored ?? null,
        });
        return quietFail(res, 400, versionJudgement.code, ref);
    }
    const acceptedSchemaVersion = versionJudgement.version;   // null => write no version at all
    // 2026-09-07: the absence is ACCEPTED but never SILENT. Same audit row the refusal
    // wrote, under an honest event name — writing 'save_schema_version_refused' for a
    // save we just accepted would be the exact class of lie CLAUDE.md §11B forbids.
    const schemaVersionNote = versionJudgement.note || null;
    if (schemaVersionNote) {
        console.warn('[save] schema version absent — accepting, version left untouched:',
                     JSON.stringify({ incoming: versionJudgement.incoming ?? null, stored: priorSchemaVersion }));
        await logApiEvent(sql, playerId, 'save_schema_version_absent', {
            ref: ref, mode: auth.mode, code: schemaVersionNote,
            incoming: versionJudgement.incoming ?? null,
            stored: versionJudgement.stored ?? null,
        });
    }

    // ── WO-1598 — a DECLARED RESET, judged before anything is guarded or written ──
    const resetJudgement = judgeResetEpoch(resetEpoch, priorResetEpoch);
    if (!resetJudgement.ok) {
        // A stale device replaying a town the player has already left. Refused BY NAME
        // and audited, never silently accepted and never silently dropped: this is the
        // one shape that could overwrite a newer new game with an older one.
        //
        // ⛔ WO-1745 — THIS ROW MOVED FROM logApiEvent TO logAuthReject, AND THE MOVE IS THE
        // WHOLE POINT. It used to be written as `save_reset_refused` via logApiEvent. That
        // row IS durable and always was — but api/admin/db.js's ONLY reader of refusals,
        // `view=authrejects`, filters `event_name IN ('api_auth_reject','auth_failed')`, so
        // no admin query in the product could see a single one of them. WO-1742 §3 reported
        // "ZERO refusals on /api/game/save in seven days" off that view; that was a VIEW
        // ARTIFACT, not a measurement — the view was structurally blind to this code.
        // A row nobody can read is not detection.
        //
        // ⚠ AND THE BLINDNESS IS PLATFORM-WIDE, WHICH IS WHY THE FIX BELONGS HERE AND NOT ON
        // THE CLIENT. The only other witness to a 409 is the client's own trace, and
        // WebTrace.cs:41/:293 gate the remote POST on `#if UNITY_WEBGL` — an Android device
        // NEVER posts a trace. So a 409 storm on Android leaves no evidence anywhere except
        // this line. The server is the one vantage point that is platform-blind by nature.
        //
        // ONE row, not two: adding a second event name beside the first would be exactly the
        // duplicated state CLAUDE.md §2/§5/§16 each describe. api/admin/db.js widens its IN
        // list to keep reading the HISTORICAL 'save_reset_refused' rows (2026-09-07 onward),
        // the same way it already carries the legacy 'auth_failed' era.
        console.warn('[save] reset epoch refused:', JSON.stringify(resetJudgement));
        await logAuthReject(sql, req, {
            code: resetJudgement.code, ref: ref, identity: playerId, mode: auth.mode,
            detail: {
                incoming: resetJudgement.incoming,
                stored: resetJudgement.stored,
                // How far behind this device is, so "one stale device" and "a device that
                // has been frozen across many resets" are distinguishable in the row itself.
                behindBy: resetJudgement.stored - resetJudgement.incoming,
                stage: 'reset_epoch',
            },
        });
        return quietFail(res, 409, resetJudgement.code, ref);
    }
    const acceptedResetEpoch = resetJudgement.epoch;   // null => write no epoch at all

    // ⛔ THE BYPASS IS A SUBSTITUTED PRIOR, NOT A SKIPPED GUARD. applyGuards still runs;
    // it is simply given an empty comparison, which is the literal truth of a reset (the
    // stored row describes a town that no longer exists). The BOUNDS inside it — negative,
    // non-finite, > MAX_RESOURCE, > MAX_BEST_WAVE — are properties of the incoming value
    // alone and therefore still fire. Skipping the call instead would have handed a reset
    // save an unbounded write, which is a far worse defect than the one being fixed.
    //
    // ⚠ ONE CONSEQUENCE, STATED SO IT IS NOT A SURPRISE LATER. applyGuards RESTORES a
    // rejected NESTED balance to the prior server value (see its own note — deleting it
    // would let a tamper attempt zero the balance through the guard). With an empty prior
    // there is nothing to restore to, so the key is dropped, and on the reset arm the write
    // REPLACES rather than merges — so an out-of-bounds balance on a reset ends up ABSENT
    // from the row rather than holding the old town's number. That is the correct answer
    // under "a new game inherits nothing": the old value is not a safer fallback, it is the
    // dead town. The client re-sends the field on its next ordinary save.
    const effectivePrior = resetJudgement.bypass ? {} : prior;

    const rejects = applyGuards(delta, effectivePrior);

    // ── WO-1128 §RECONCILE — time-derived accrual vs the server's OWN clock ────
    // Runs AFTER applyGuards so it reconciles the values that actually survive to
    // the write, never a number the guards were about to strip anyway.
    // WO-1598: a reset gets the same substitution here, and `null` for the anchor. The
    // window this reconciles is "time since the last accepted save of THIS town"; across a
    // reset there is no such window, and measuring the new town's first claim against the
    // old town's lastHarvestClaimMs would clamp a legitimate first harvest. reconcileAccrual
    // already has the honest answer for an absent anchor — `no_prior_last_seen`, reconciled
    // false — so this reuses that arm rather than inventing a reset-shaped one.
    const accrual = reconcileAccrual(delta, effectivePrior, resetJudgement.bypass ? null : priorSeenMs, Date.now());
    if (accrual.clamps.length > 0) {
        // Both numbers, always, per WO-1128 §6.2 — a clamp log that shows only the
        // clamped figure cannot be audited after the fact.
        console.warn('[save] accrual reconciled:', JSON.stringify(accrual));
        await logApiEvent(sql, playerId, 'save_accrual_reconcile', {
            ref: ref, mode: auth.mode,
            clientWindowSec: accrual.clientWindowSec,
            serverElapsedSec: accrual.serverElapsedSec,
            honestFraction: accrual.honestFraction,
            clamps: accrual.clamps,
            observed: accrual.observed,
        });
    }

    if (rejects.length > 0) {
        await logApiEvent(sql, playerId, 'save_sanity_reject', { rejects: rejects, ref: ref, mode: auth.mode });
    }

    if (Object.keys(delta).length === 0) {
        return res.status(200).json({
            ok: true, success: true, serverNowMs: Date.now(),
            note: 'all fields rejected by guards', rejects, ref,
            schemaVersionNote: schemaVersionNote || undefined,
        });
    }

    // WO-1598: a bypass is the loudest thing this endpoint does — it is the one path on
    // which a balance may legally fall to nothing — so it is never silent.
    // ⛔ AND IT SITS AFTER THE EMPTY-DELTA RETURN, DELIBERATELY. Above it, a reset whose
    // every field was stripped by the BOUNDS would write `save_reset_accepted {to:N}`
    // while nothing was written and the epoch never advanced (GREATEST only runs on the
    // upsert below) — an audit row asserting something that did not happen, which is the
    // exact class of claim CLAUDE.md §11B forbids. Still before the upsert, so a write
    // that then FAILS leaves the trace that the bypass was spent.
    if (resetJudgement.bypass) {
        console.warn('[save] reset accepted — comparative guards stood down for this write:',
                     JSON.stringify({ from: resetJudgement.from, to: acceptedResetEpoch }));
        await logApiEvent(sql, playerId, 'save_reset_accepted', {
            from: resetJudgement.from, to: acceptedResetEpoch, ref: ref, mode: auth.mode,
        });
    }

    // ── Upsert into Neon ───────────────────────────────────────────────────
    try {
        // game_state || EXCLUDED.game_state is a SHALLOW merge of the incoming
        // object onto the stored one. The client posts a FULL snapshot with null
        // fields stripped, so this preserves any key a partial/older client did
        // not send instead of nulling it.
        // The ::jsonb cast is required — Neon's HTTP driver sends parameters as
        // strings.
        //
        // TWO STATEMENTS, AND THE SECOND ONE IS THE WHOLE POINT (2026-09-07).
        // When the payload declared no version, `schema_version` is named NOWHERE —
        // not in the INSERT column list, not in the SET clause — so the stored value
        // is untouched by construction rather than by a clamp we have to trust.
        // ⛔ IT IS STILL TWO STATEMENTS, AND THE REASON HAS CHANGED — read this before
        //    "simplifying" it back to one with a NULL parameter.
        //
        //    The ORIGINAL reason (2026-09-07 morning) was that the column was
        //    `INTEGER NOT NULL DEFAULT 10`, so a NULL on the INSERT arm raised a
        //    not-null violation — a 500 that lost the save of every brand-new player.
        //    That is no longer true: migration
        //    api/migrations/20260907_0022_game_saves_schema_version_default.sql dropped
        //    both the DEFAULT and the NOT NULL, so a version-less first save now lands
        //    NULL, which api/game/load.js returns as `schemaVersion: null` and the
        //    client reads as "never declared — skip the migration chain"
        //    (GameStateService.ApplyBackendState). The invented 10 that drove a whole
        //    v10→current chain over never-was-v10 state (WO-1457) is gone.
        //
        //    The REMAINING reason is the stronger one, and it is why the shape stays:
        //    naming `schema_version` NOWHERE — not in the INSERT column list, not in
        //    the SET clause — leaves an EXISTING row's version untouched BY
        //    CONSTRUCTION rather than by a clamp we have to trust. A single statement
        //    passing NULL would work on the INSERT arm today, but the UPDATE arm would
        //    then depend on GREATEST()'s NULL handling being right, which is a property
        //    of an expression instead of a property of the statement. Do not trade a
        //    structural guarantee for a one-liner.
        //
        //    NOT BACK-FILLED, DELIBERATELY: rows already stamped 10 by the old default
        //    keep the 10. Blanking them is a guess in the opposite direction and is the
        //    owner's call, on evidence (how many rows, and what shape their blob is).
        //
        // ⚠ reset_epoch IS NAMED IN BOTH ARMS, AND THAT IS A DIFFERENT TRADE FROM
        //   schema_version's — deliberately, so read this before "making them consistent".
        //   The version column needed the two-statement shape because leaving an existing
        //   row's version untouched had to be a property of the STATEMENT. reset_epoch does
        //   not: the column is nullable with no default, so an absent epoch binds NULL on the
        //   INSERT arm and Postgres's GREATEST() — which IGNORES nulls rather than
        //   propagating them — leaves the stored value alone on the UPDATE arm. GREATEST is
        //   also the same monotonic belt WO-1457 chose for the version, so a stale write
        //   cannot lower the epoch even if some future path reaches this SQL without passing
        //   judgeResetEpoch. Splitting this into four statements to avoid depending on that
        //   NULL behaviour would quadruple the copy of a 15-line upsert, which is the failure
        //   mode this repo has the most scars from.
        // ⛔ A NEW GAME INHERITS NOTHING (owner's standing rule, relayed 2026-09-07).
        //    On a RESET-ACCEPTED write the incoming blob is AUTHORITATIVE and the stored
        //    one is discarded wholesale: `game_state = EXCLUDED.game_state`, no merge.
        //
        //    WHY THE GUARD BYPASS ALONE WAS ONLY HALF THE FIX. The client posts a snapshot
        //    with nulls and empties STRIPPED, so under the shallow merge every old-town key
        //    the new save does not carry SURVIVES the new game — the obsidian queue, the
        //    army, the base layout, `everBuiltStructureIds` (which gates the WO-834
        //    blank-town standdown). The row becomes a chimera of two towns and load.js
        //    hands it back, which fails "a load returns the new town" with every balance
        //    correct. The balances were only the visible half.
        //
        // ⚠ AND THE MERGE STAYS FOR EVERY ORDINARY SAVE. It is what stops a partial or
        //    older client blanking a field it never sent (the reason it was chosen). So the
        //    statement CHOOSES, on the same judgement that stood the guards down — one
        //    bound boolean rather than a third and fourth copy of a 15-line upsert.
        const replaceState = resetJudgement.bypass;
        if (acceptedSchemaVersion == null) {
            await sql`
                INSERT INTO player_data (player_id, reset_epoch, game_state, trust, updated_at)
                VALUES (
                    ${playerId},
                    ${acceptedResetEpoch},
                    ${JSON.stringify(delta)}::jsonb,
                    ${auth.mode},
                    NOW()
                )
                ON CONFLICT (player_id) DO UPDATE
                SET
                    reset_epoch = GREATEST(player_data.reset_epoch, EXCLUDED.reset_epoch),
                    game_state = CASE WHEN ${replaceState}::boolean
                                      THEN EXCLUDED.game_state
                                      ELSE player_data.game_state || EXCLUDED.game_state END,
                    trust      = EXCLUDED.trust,
                    updated_at = NOW()
            `;
        } else {
            await sql`
                INSERT INTO player_data (player_id, schema_version, reset_epoch, game_state, trust, updated_at)
                VALUES (
                    ${playerId},
                    ${acceptedSchemaVersion},
                    ${acceptedResetEpoch},
                    ${JSON.stringify(delta)}::jsonb,
                    ${auth.mode},
                    NOW()
                )
                ON CONFLICT (player_id) DO UPDATE
                SET
                    -- WO-1457: the belt to judgeSchemaVersion's braces. The row's version
                    -- can only ever climb, whatever reaches this statement.
                    schema_version = GREATEST(player_data.schema_version, EXCLUDED.schema_version),
                    -- WO-1598: same belt, same reason — the epoch a bypass was spent on can
                    -- never be re-spent, because the stored value can only climb.
                    reset_epoch    = GREATEST(player_data.reset_epoch, EXCLUDED.reset_epoch),
                    -- WO-1598: REPLACE on a declared reset, MERGE on every ordinary save.
                    -- See the note above the branch — a new game inherits nothing.
                    game_state     = CASE WHEN ${replaceState}::boolean
                                          THEN EXCLUDED.game_state
                                          ELSE player_data.game_state || EXCLUDED.game_state END,
                    trust          = EXCLUDED.trust,
                    updated_at     = NOW()
            `;
        }

        return res.status(200).json({
            ok: true,
            success: true,
            mode: auth.mode,
            fields: Object.keys(delta).length,
            bytes: rawBody.length,
            rejects: rejects.length ? rejects : undefined,
            // 2026-09-07: the save landed, and the client is told the version it did
            // NOT send. Absent when the payload declared one, so a healthy client sees
            // nothing new and a version-less one is never invisible.
            schemaVersionNote: schemaVersionNote || undefined,
            // WO-1598: the epoch the row now stands at, and whether THIS write spent a
            // bypass. Absent on an ordinary save, so a healthy client sees nothing new.
            resetEpoch: acceptedResetEpoch != null ? acceptedResetEpoch : undefined,
            resetAccepted: resetJudgement.bypass ? true : undefined,
            // WO-1128: what the server refused to accept as honestly-accrued, and the
            // two numbers it judged on. Absent when nothing was clamped.
            accrual: accrual.clamps.length ? accrual : undefined,
            ref: ref,
            // WO-912 s7.2: authoritative server time for ServerClock. The save round trip
            // is the most frequent handshake the client makes, so this is the main way the
            // rewarded-ad window stays anchored during a session.
            serverNowMs: Date.now(),
        });
    } catch (err) {
        console.error('[save] DB error:', err);
        await logAuthReject(sql, req, {
            code: AuthCode.SERVER_ERROR, ref, identity: playerId, mode: auth.mode,
            detail: { stage: 'upsert', message: String(err.message || err).slice(0, 300) },
        });
        return quietFail(res, 500, AuthCode.SERVER_ERROR, ref);
    }
}

// =============================================================================
//  State assembly
// =============================================================================

/**
 * Assemble what actually gets stored.
 *
 * STORE EVERYTHING the client sent (minus transport keys), THEN normalise the
 * handful of legacy PascalCase / *Json fields onto their camelCase homes. The
 * old implementation did only the second half, against a 13-key whitelist, which
 * is why a full ~60-field save round-tripped as a husk: the client posts the
 * complete PersistedState snapshot and 47 fields of it hit the floor.
 *
 * The client's own load path deserialises `data` straight into
 * SaveSchema.PersistedState, so storing the snapshot verbatim is exactly the
 * shape it wants back — no translation layer, nothing to drift.
 */
function buildState(body) {
    const delta = {};

    // 1. Everything camelCase the client sent (the live PersistedState snapshot).
    //    Null/undefined are skipped: the client strips nulls, and a null here
    //    would blank a good server value on a partial sync.
    for (const key of Object.keys(body)) {
        if (RESERVED_KEYS.has(key)) continue;
        const v = body[key];
        if (v === null || v === undefined) continue;
        // Legacy PascalCase fields are handled by the promotion pass below; do
        // not store both spellings.
        if (/^[A-Z]/.test(key)) continue;
        delta[key] = v;
    }

    // 2. Legacy/alternate spellings promoted onto the canonical camelCase keys.
    const f = normalizeDeltaFields(body);

    if (f.Crystals   != null) delta.crystals   = f.Crystals;
    if (f.Food       != null) delta.food       = f.Food;
    if (f.Coins      != null) delta.coins      = f.Coins;
    if (f.Voidshards != null) delta.voidshards = f.Voidshards;
    if (f.Stone      != null) delta.stone      = f.Stone;
    if (f.Iron       != null) delta.iron       = f.Iron;
    if (f.Wood       != null) delta.wood       = f.Wood;

    if (f.Towers         != null) delta.towers         = f.Towers;
    if (f.TowerAbilities != null) delta.towerAbilities = f.TowerAbilities;
    if (f.BestWave       != null) delta.bestWave       = f.BestWave;

    // Pets arrive as pre-serialized JSON strings on the legacy path — parse so
    // Postgres stores real JSONB rather than a string containing JSON.
    if (f.PetsJson) {
        try { delta.pets = typeof f.PetsJson === 'string' ? JSON.parse(f.PetsJson) : f.PetsJson; }
        catch { /* skip malformed */ }
    }
    if (f.OwnedPetsJson) {
        try { delta.ownedPets = typeof f.OwnedPetsJson === 'string' ? JSON.parse(f.OwnedPetsJson) : f.OwnedPetsJson; }
        catch { /* skip malformed */ }
    }
    if (f.StarterPetId != null) delta.starterPetId = f.StarterPetId;

    return delta;
}

/**
 * Bounds + anti-rollback + anti-wipe. Rejected fields are STRIPPED (the rest of
 * the save still lands) and returned for the audit row.
 *
 * Guards BOTH the flat key and the nested "resources" object — they are two
 * spellings of the same balance and the client reads the nested one back, so
 * guarding only the flat copy would leave the guard trivially bypassable.
 *
 * FLAT vs NESTED REJECTION ARE NOT THE SAME OPERATION, and conflating them eats
 * the balance you were protecting. The upsert merges SHALLOWLY
 * (game_state || EXCLUDED.game_state), so:
 *   • deleting a FLAT key leaves the stored top-level key untouched — correct;
 *   • deleting a NESTED key still replaces the ENTIRE stored "resources" object
 *     with the incoming one, so the key vanishes instead of keeping its old
 *     value. A tamper attempt would therefore succeed at zeroing the balance by
 *     way of the very guard meant to stop it.
 * So a rejected nested field is RESTORED to the prior server value rather than
 * deleted. (Caught by the endpoint harness, 2026-08-02 — the first draft of this
 * function deleted both and wiped 500 crystals it had just refused to lower.)
 */
function applyGuards(delta, prior) {
    const rejects = [];
    const nested = (delta.resources && typeof delta.resources === 'object') ? delta.resources : null;
    const priorNested = (prior && prior.resources && typeof prior.resources === 'object') ? prior.resources : null;

    // (a) bestWave — a monotonic high-water mark the server already wins on.
    if (delta.bestWave != null) {
        const bw = Number(delta.bestWave);
        if (!Number.isFinite(bw) || bw < 0 || bw > MAX_BEST_WAVE) {
            rejects.push({ field: 'bestWave', value: delta.bestWave, rule: 'out_of_bounds' });
            delete delta.bestWave;
        } else if (prior.bestWave != null && bw < Number(prior.bestWave)) {
            rejects.push({ field: 'bestWave', value: bw, prior: prior.bestWave, rule: 'rollback' });
            delete delta.bestWave;   // never lower the high-water mark
        }
    }

    // (b) Bounds + anti-wipe on each balance, flat and nested.
    for (const key of GUARDED_BALANCES) {
        const isNestedField = NESTED_BALANCES.includes(key);
        const incoming = delta[key] != null ? delta[key]
                       : (isNestedField && nested && nested[key] != null) ? nested[key]
                       : null;
        if (incoming == null) continue;

        const priorFlat = prior[key] != null ? Number(prior[key])
                        : (isNestedField && priorNested && priorNested[key] != null) ? Number(priorNested[key])
                        : null;

        const strip = (rule, extra) => {
            rejects.push(Object.assign({ field: key, value: incoming, rule: rule }, extra || {}));
            delete delta[key];                       // shallow merge keeps the stored top-level key
            if (isNestedField && nested) {
                if (priorFlat != null) nested[key] = priorFlat;   // restore — see the note above
                else delete nested[key];                          // nothing to preserve (first write)
            }
        };

        const v = Number(incoming);
        if (!Number.isFinite(v) || v < 0 || v > MAX_RESOURCE) { strip('out_of_bounds'); continue; }

        if (priorFlat != null && priorFlat > 0 && v < priorFlat * (1 - MAX_BALANCE_DROP_FRACTION)) {
            strip('implausible_drop', { prior: priorFlat });
        }
    }

    return rejects;
}

// =============================================================================
//  WO-1128 — SERVER-RECONCILED OFFLINE ACCRUAL
// -----------------------------------------------------------------------------
//  THE PROBLEM, stated exactly: you cannot verify a client's clock, and you must
//  not try (root detection / clock attestation is a race you lose on a rooted
//  device — WO-1128 §4). What you CAN do is make the client's clock not matter,
//  by never letting it claim more elapsed time than the SERVER'S OWN clock says
//  has passed.
//
//  THE TWO NUMBERS, and why they are comparable:
//    * clientWindowSec  = (incoming lastHarvestClaimMs) - (stored lastHarvestClaimMs)
//                         — how much away-time the client says it integrated since
//                           the last save the server ACCEPTED.
//    * serverElapsedSec = NOW() - player_data.updated_at
//                         — how much time actually passed since that same save,
//                           measured by a clock the player cannot touch.
//  Both are anchored to THE SAME EVENT (the last accepted save), which is what
//  makes the subtraction meaningful even after days offline: if the client was
//  unreachable for 30h, BOTH numbers grow to ~30h together. They only diverge
//  when the device clock moved further than real time did.
//
//  ⛔ WHY THIS IS A RATIO AND NOT A RATE MODEL. The obvious implementation —
//  "recompute what the player's collectors should have produced" — requires the
//  server to model every node, settlement, Echo, container cap, level and the
//  WO-1119 harvest boost, i.e. a second copy of the economy that drifts the day
//  anyone retunes a number (the duplicated-state failure CLAUDE.md is littered
//  with). Instead: the client claims a gain G over a window W, of which only H
//  seconds honestly happened; at most G*(H/W) of that gain is honest. Scale the
//  GAIN, do not recompute it. This is rate-model-free, retune-proof, and — the
//  reason it is safe with WO-1119 — it CANNOT double-count the harvest boost,
//  because the boost is already inside G and G is never rebuilt from a rate.
//
//  ⛔ REFUSE, DO NOT PUNISH (WO-1128 §3.2 + the standing rule for clock defences):
//    * a clamp never takes a balance BELOW the stored prior value — the worst
//      case is "this sync banked nothing", never "you lost what you had";
//    * claiming LESS than the server allows is accepted verbatim (never pay a
//      player more than they claim);
//    * we do NOT lower the incoming lastHarvestClaimMs. Rolling that stamp back
//      would hand the client a re-claimable window on its next launch — the exact
//      double-grant the OfflineClaimCoordinator's advance-even-on-zero contract
//      exists to prevent;
//    * no account is flagged, no state is wiped, nothing is accused. The honest
//      causes of a forward clock are DST-adjacent bugs, a dead RTC coin cell, a
//      manual correction, and a phone that was simply wrong. All of them look
//      identical to cheating from here and all of them deserve their resources.
//
//  ⛔ CRYSTALS ARE OBSERVED, NOT CLAMPED — and that is an owner call, not an
//  oversight. Crystals are the real-money on-ramp: an IAP or a rewarded-ad grant
//  lands as a large crystal gain with no elapsed time behind it, so a time-ratio
//  clamp would rob paying players first and hardest. They are reported in
//  `observed` for the audit row instead. If crystals ever become offline-farmable
//  at scale, this decision needs re-taking WITH a purchase-aware exemption, not by
//  quietly adding 'crystals' to the list below.
// =============================================================================

// Resources produced by TIME-DERIVED accrual (OfflineHarvestService: worker nodes,
// settlements, pet-claimed nodes). These are the only balances a fabricated window
// can mint, and therefore the only ones the ratio applies to.
// WO-1212: 'stone' is REMOVED from this list. It named the retired second Stone balance
// (GameState.Stone), which no HUD read and no cost spent, so a clamp on it moved a number
// no player could see - while the balance she calls Stone is 'food'. This list is MIRRORED
// by GameStateService.ReadTimeDerivedBalance/WriteTimeDerivedBalance; the two must name the
// same keys, and that switch's default arm FlowTrace.Fail's when they drift.
const TIME_DERIVED_BALANCES = ['iron', 'wood', 'food'];

// Balances watched and reported but never clamped — see the crystals note above.
const OBSERVED_ONLY_BALANCES = ['crystals'];

// Slack on the comparison, so ordinary skew never costs an honest player anything:
// the client stamps its claim clock a moment BEFORE the request lands, network
// latency sits between them, and both clocks drift. Generous on purpose — this
// gate exists to stop hours, not seconds.
const RECONCILE_GRACE_SEC = 600;          // flat 10 minutes
const RECONCILE_GRACE_FRACTION = 0.05;    // plus 5% of the server's own window

/**
 * Compare the client's DECLARED accrual window against the server's own elapsed
 * time and scale down any time-derived gain the device could not honestly have
 * produced. Mutates `delta` in place (flat AND nested spellings) and returns a
 * report for the audit row + the response body.
 *
 * Returns { reconciled, reason, clientWindowSec, serverElapsedSec, honestFraction,
 *           clamps: [{field, claimed, allowed, prior}], observed: {field: gain} }
 * `reconciled:false` + a `reason` means the guard did not apply (first save, no
 * stored clock, no forward window) — an absence of judgement, never a pass.
 */
function reconcileAccrual(delta, prior, priorSeenMs, nowMs) {
    const report = {
        reconciled: false, reason: null,
        clientWindowSec: null, serverElapsedSec: null, honestFraction: null,
        clamps: [], observed: {},
    };
    if (!delta || typeof delta !== 'object') { report.reason = 'no_delta'; return report; }
    prior = (prior && typeof prior === 'object') ? prior : {};

    // No server anchor => nothing to measure against. First save ever, or a prior
    // read that failed. Accept; the NEXT save has an anchor.
    if (!Number.isFinite(priorSeenMs)) { report.reason = 'no_prior_last_seen'; return report; }

    const claimClock = Number(delta.lastHarvestClaimMs);
    const priorClock = Number(prior.lastHarvestClaimMs);
    if (!Number.isFinite(claimClock) || claimClock <= 0) { report.reason = 'no_client_claim_clock'; return report; }
    if (!Number.isFinite(priorClock) || priorClock <= 0) { report.reason = 'no_stored_claim_clock'; return report; }

    const clientWindowSec = (claimClock - priorClock) / 1000;
    const serverElapsedSec = (nowMs - priorSeenMs) / 1000;
    report.clientWindowSec = round2(clientWindowSec);
    report.serverElapsedSec = round2(serverElapsedSec);

    // The client's accrual clock did not move forward (or went backwards — the
    // OfflineClaimCoordinator already clamps that to a zero window). No accrual
    // window is being claimed, so there is nothing to reconcile.
    if (!(clientWindowSec > 0)) { report.reason = 'no_forward_window'; return report; }

    const graceSec = RECONCILE_GRACE_SEC + Math.max(0, serverElapsedSec) * RECONCILE_GRACE_FRACTION;
    const honestSec = Math.min(clientWindowSec, Math.max(0, serverElapsedSec) + graceSec);
    const honestFraction = honestSec / clientWindowSec;
    report.reconciled = true;
    report.honestFraction = round4(honestFraction);

    // The honest case, and the one that matters most: a player genuinely away for
    // N hours reconnects and honestSec >= clientWindowSec, so the fraction is 1 and
    // NOTHING is touched. Offline play must pay in full — that is the whole point
    // of having the feature (WO-1128 §6.4).
    if (honestFraction >= 1) { report.reason = 'window_honest'; return report; }

    for (const key of OBSERVED_ONLY_BALANCES) {
        const g = balanceGain(delta, prior, key);
        if (g != null && g.gain > 0) report.observed[key] = g.gain;
    }

    for (const key of TIME_DERIVED_BALANCES) {
        const g = balanceGain(delta, prior, key);
        if (g == null || !(g.gain > 0)) continue;   // a drop or a flat balance is applyGuards' business

        const allowedGain = Math.floor(g.gain * honestFraction);
        const allowed = g.priorValue + allowedGain;
        if (allowed >= g.incoming) continue;        // claiming less than allowed — accept verbatim

        setBalance(delta, key, allowed);
        report.clamps.push({
            field: key,
            claimed: g.incoming,
            allowed: allowed,
            prior: g.priorValue,
            claimedGain: g.gain,
            allowedGain: allowedGain,
        });
    }

    if (report.clamps.length === 0) report.reason = 'over_window_but_no_gain_to_clamp';
    else report.reason = 'clamped_to_server_window';
    return report;
}

/** Read one balance from BOTH spellings (flat + nested "resources") on delta and prior. */
function balanceGain(delta, prior, key) {
    const nested = (delta.resources && typeof delta.resources === 'object') ? delta.resources : null;
    const priorNested = (prior.resources && typeof prior.resources === 'object') ? prior.resources : null;

    const incomingRaw = delta[key] != null ? delta[key]
                      : (nested && nested[key] != null) ? nested[key]
                      : null;
    if (incomingRaw == null) return null;
    const incoming = Number(incomingRaw);
    if (!Number.isFinite(incoming)) return null;

    const priorRaw = prior[key] != null ? prior[key]
                   : (priorNested && priorNested[key] != null) ? priorNested[key]
                   : null;
    // No prior value => no gain can be measured. A first-ever balance is not
    // evidence of anything; MAX_RESOURCE in applyGuards is what bounds it.
    if (priorRaw == null) return null;
    const priorValue = Number(priorRaw);
    if (!Number.isFinite(priorValue)) return null;

    return { incoming, priorValue, gain: incoming - priorValue };
}

/**
 * Write a clamped balance back to EVERY spelling present in the payload. Both must
 * move together: the stored row is merged shallowly (game_state || EXCLUDED), and
 * the client reads the NESTED copy back — so lowering only the flat key would leave
 * the fabricated number live in the copy that actually reaches the player.
 */
function setBalance(delta, key, value) {
    if (delta[key] != null) delta[key] = value;
    if (delta.resources && typeof delta.resources === 'object' && delta.resources[key] != null) {
        delta.resources[key] = value;
    }
}

const round2 = (n) => Math.round(n * 100) / 100;
const round4 = (n) => Math.round(n * 10000) / 10000;

// ── Normalize the client payload into the flat PascalCase shape the promotion ──
// pass reads. The LIVE client posts a FULL camelCase snapshot with a nested
// "resources" object; the legacy MsgPack path posted flat PascalCase fields.
// Accept BOTH so the guards and the promotion see one consistent shape.
function normalizeDeltaFields(body) {
    const f = { ...body };
    const res = body.resources || body.Resources || null;
    const num = (x) => (x != null ? Number(x) : undefined);

    if (f.Crystals   == null) f.Crystals   = num(res ? res.crystals : body.crystals);
    if (f.Food       == null) f.Food       = num(res ? res.food     : body.food);
    if (f.Coins      == null) f.Coins      = num(res ? res.coins    : body.coins);
    if (f.Voidshards == null) f.Voidshards = num(body.voidshards ?? body.Voidshards);
    if (f.Stone      == null) f.Stone      = num(body.stone  ?? body.Stone);
    if (f.Iron       == null) f.Iron       = num(body.iron   ?? body.Iron);
    if (f.Wood       == null) f.Wood       = num(body.wood   ?? body.Wood);

    if (f.BestWave == null) f.BestWave = num(body.bestWave ?? body.BestWave);

    if (f.Towers         == null) f.Towers         = body.towers         ?? body.Towers;
    if (f.TowerAbilities == null) f.TowerAbilities = body.towerAbilities ?? body.TowerAbilities;

    if (f.PetsJson == null && body.pets != null) {
        f.PetsJson = typeof body.pets === 'string' ? body.pets : JSON.stringify(body.pets);
    }
    if (f.OwnedPetsJson == null && body.ownedPets != null) {
        f.OwnedPetsJson = typeof body.ownedPets === 'string' ? body.ownedPets : JSON.stringify(body.ownedPets);
    }
    if (f.StarterPetId == null) f.StarterPetId = body.starterPetId ?? body.StarterPetId;

    return f;
}

// =============================================================================
//  BUILT-TO-FLIP SEAM (WO-120 §3 — DO NOT IMPLEMENT NOW; documented only)
// -----------------------------------------------------------------------------
//  Today: soft currency is CLIENT-OWNED; the guards above are anti-grief only.
//  The economy flips to FULLY SERVER-AUTHORITATIVE the moment currency buys real
//  value (crypto/Solana or IAP goes live). At that point:
//    • The server STOPS trusting client-sent balances — it derives them from
//      authoritative events (wave rewards computed server-side, verified IAP
//      receipts, on-chain entitlements re-fetched from an RPC and made idempotent
//      on tx_hash) and writes the result itself.
//    • The save path becomes: accept gameplay EVENTS, not balances; recompute the
//      wallet; persist. The wallet columns become read-only to the client.
//    • The wallet auth rail stays AS-IS — it already proves wallet ownership. The
//      GUEST rail is excluded from that flip by construction: a guest id can never
//      key a wallet row, so it can never hold real value.
//    • The guards below become hard rejects (4xx) rather than field-strips.
// =============================================================================

module.exports = handler;
// MUST be assigned AFTER the handler. `module.exports.config = ...` followed by
// `module.exports = handler` (the original ordering) silently discards the config
// and leaves the runtime body parser ON — which drains the stream the raw-body
// reader needs for signature verification. See _lib/http.readBodyExact.
module.exports.config = { api: { bodyParser: false } };

// WO-1128: exported so a harness can drive the reconciliation without a database.
module.exports.reconcileAccrual = reconcileAccrual;
// WO-1457 — exported for test/game.save.schema-version.test.js. Pure, no DB.
module.exports.judgeSchemaVersion = judgeSchemaVersion;
// WO-1598 — pure, so the four epoch cases are provable without a database.
module.exports.judgeResetEpoch = judgeResetEpoch;
module.exports.buildState = buildState;
module.exports.SaveCode = SaveCode;

// =============================================================================
//  WO-1128 §6.7 — RUNNABLE SELF-TEST:  node api/game/save.js
// -----------------------------------------------------------------------------
//  The clamp lives in JavaScript, so the Unity DataRegression suite cannot execute
//  it (its sibling, OfflineAccrualTrustRegression, pins the CLIENT half — that the
//  window records which clock produced it). This is the server half's gate, and it
//  asserts the clamp in BOTH directions: an over-claim must FAIL to land in full,
//  and an honest claim must land untouched. A gate that does not fail the
//  known-bad state is not a gate.
//  Exit code 0 = pass, 1 = fail. No database, no network, no Unity.
// =============================================================================
if (require.main === module) {
    const HOUR = 3600 * 1000;
    const fails = [];
    const check = (name, cond, detail) => { if (!cond) fails.push(`${name}: ${detail}`); };

    // (1) HONEST OFFLINE PLAY — away 10h, server agrees 10h. Nothing is touched.
    //     This is the case that makes the feature worth having; it is asserted FIRST
    //     so a regression that breaks it can never be mistaken for "the gate working".
    {
        const now = 1_800_000_000_000;
        const prior = { lastHarvestClaimMs: now - 10 * HOUR, wood: 1000, iron: 500, resources: { food: 200 } };
        const delta = { lastHarvestClaimMs: now, wood: 9000, iron: 4500, resources: { food: 1800 } };
        const r = reconcileAccrual(delta, prior, now - 10 * HOUR, now);
        check('honest-10h', r.clamps.length === 0, `clamped an honest window: ${JSON.stringify(r.clamps)}`);
        check('honest-10h', delta.wood === 9000 && delta.iron === 4500 && delta.resources.food === 1800,
              `honest haul was altered -> ${JSON.stringify(delta)}`);
    }

    // (2) FORWARD-CLOCK OVER-CLAIM — device says 20h passed, server says 1h.
    //     ~1h of the 20h is honest, so ~1/20 of the GAIN survives. Never below prior.
    {
        const now = 1_800_000_000_000;
        const prior = { lastHarvestClaimMs: now - 20 * HOUR, wood: 1000, resources: { food: 200 } };
        const delta = { lastHarvestClaimMs: now, wood: 21000, resources: { food: 4200 } };
        const r = reconcileAccrual(delta, prior, now - 1 * HOUR, now);
        check('overclaim', r.clamps.length === 2, `expected 2 clamps, got ${JSON.stringify(r.clamps)}`);
        check('overclaim', delta.wood < 21000, `over-claimed wood landed in full (${delta.wood})`);
        check('overclaim', delta.wood >= 1000, `wood clamped BELOW prior (${delta.wood}) — that is punishment, not refusal`);
        check('overclaim', delta.resources.food < 4200 && delta.resources.food >= 200,
              `nested food not reconciled safely (${delta.resources.food})`);
        // ~1.05h honest of 20h => ~5.3% of a 20000 gain.
        check('overclaim', delta.wood < 3000, `clamp far too generous (${delta.wood}) — the ratio is not being applied`);
    }

    // (3) UNDER-CLAIM — the client asks for less than the window allows. Verbatim.
    {
        const now = 1_800_000_000_000;
        const prior = { lastHarvestClaimMs: now - 10 * HOUR, wood: 1000 };
        const delta = { lastHarvestClaimMs: now, wood: 1005 };
        const r = reconcileAccrual(delta, prior, now - 10 * HOUR, now);
        check('underclaim', r.clamps.length === 0 && delta.wood === 1005, `under-claim was altered (${delta.wood})`);
    }

    // (4) CRYSTALS ARE OBSERVED, NOT CLAMPED (the deliberate real-money carve-out).
    {
        const now = 1_800_000_000_000;
        const prior = { lastHarvestClaimMs: now - 20 * HOUR, resources: { crystals: 100 } };
        const delta = { lastHarvestClaimMs: now, resources: { crystals: 5100 } };
        const r = reconcileAccrual(delta, prior, now - 1 * HOUR, now);
        check('crystals', delta.resources.crystals === 5100, `crystals were clamped (${delta.resources.crystals}) — an IAP would be robbed`);
        check('crystals', r.observed.crystals === 5000, `crystal gain not observed for audit (${JSON.stringify(r.observed)})`);
    }

    // (5) NO ANCHOR / FIRST SAVE — an absence of judgement, and it says so.
    {
        const now = 1_800_000_000_000;
        const delta = { lastHarvestClaimMs: now, wood: 999999 };
        const r = reconcileAccrual(delta, {}, null, now);
        check('first-save', r.reconciled === false && r.reason === 'no_prior_last_seen',
              `first save mis-reported: ${JSON.stringify(r)}`);
        check('first-save', delta.wood === 999999, 'first save was clamped with nothing to compare against');
    }

    // (6) BACKWARDS / STALLED CLIENT CLOCK — no forward window, nothing to reconcile.
    {
        const now = 1_800_000_000_000;
        const prior = { lastHarvestClaimMs: now, wood: 1000 };
        const delta = { lastHarvestClaimMs: now - HOUR, wood: 1000 };
        const r = reconcileAccrual(delta, prior, now - HOUR, now);
        check('backwards', r.reason === 'no_forward_window', `backwards clock mis-reported: ${JSON.stringify(r)}`);
    }

    if (fails.length === 0) {
        console.log('ACCRUAL_RECONCILE_OK 6/6 cases — honest windows land in full, ' +
                    'forward-clock over-claims are scaled to the server window, crystals observed only.');
        process.exit(0);
    }
    console.error(`ACCRUAL_RECONCILE_FAIL x${fails.length}:\n  ` + fails.join('\n  '));
    process.exit(1);
}

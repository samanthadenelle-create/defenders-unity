// =============================================================================
// api/clan/vault/register.js — WO-1854 (clan WO-11). POST /api/clan/vault/register
// -----------------------------------------------------------------------------
//   POST  /api/clan/vault/register
//         Headers: X-Wallet / X-Nonce / X-Signature (or X-Session)
//         Body:   { playerId: <wallet>, vaultAddress: <multisig address>,
//                   vaultIndex?: 0..255 }
//   200   { ok:true, vault:{ vault_address, multisig_address, vault_index, threshold,
//           signer_count, signer_wallets, time_lock, config_authority, verified_at },
//           hardware_backed:true }
//   400   CLAN_VAULT_BAD_ADDRESS | CLAN_VAULT_NOT_A_MULTISIG | CLAN_VAULT_BAD_INDEX
//         | CLAN_VAULT_TOO_MANY_SIGNERS | METHOD_NOT_ALLOWED | BAD_PAYLOAD | PLAYER_ID_*
//   401   any auth refusal (the shape every other clan route answers with)
//   403   CLAN_VAULT_NOT_LEADER
//   403   CLAN_VAULT_SIGNER_NO_SGT | CLAN_VAULT_SIGNER_SGT_REUSED  ← NAMES the signer
//   404   CLAN_NOT_IN_CLAN
//   409   CLAN_VAULT_ALREADY_REGISTERED
//   503   CLAN_VAULT_UNREADABLE | CLAN_VAULT_SIGNER_UNVERIFIABLE
//   500   SERVER_ERROR (a DATABASE failure only)
//
// ⛔ THIS ROUTE NAMES A WALLET IN AN ERROR BODY, WHICH NOTHING ELSE IN THE CLAN SET
//    DOES, AND IT IS THE WORK ORDER'S EXPLICIT INSTRUCTION — not a drift from quietFail.
//    "any signer lacking a verified SGT -> 403 naming the offending wallet … so the
//    Leader knows which signer to fix". The privacy reasoning holds: the address is one
//    the LEADER SUPPLIED, by nominating this multisig, and it is returned only to that
//    proven Leader. It is NOT a lookup service — you cannot learn anything about a wallet
//    here that you did not already have to name. Every OTHER refusal on this route is a
//    plain code + ref, and the wallet never reaches an audit-facing body it did not come
//    from.
//
// ⚠ IT USES beginClanRequest, WHICH IS *STRICTER* THAN THE authenticateGranting() THE
//   WORK ORDER ASKS FOR, AND THE SUBSTITUTION IS DELIBERATE — the ticket's intent was "a
//   stronger auth requirement than the plain clan endpoints", and in THIS codebase that
//   phrasing inverts:
//     * authenticateGranting() = authenticate() + an ALLOWLIST of {wallet, google}. For a
//       wallet-shaped playerId it is IDENTICAL in strictness to authenticate(); its extra
//       check only ever refuses guest- and, well, admits play- ids.
//     * beginClanRequest() = authenticate() + `auth.mode !== 'wallet'` is REFUSED, because
//       clan_members.wallet is a foreign key onto wallet_identity(wallet) and only the
//       wallet rail writes that table (clan-http.js's header).
//   So beginClanRequest refuses a superset of what authenticateGranting refuses, and
//   swapping it out would WEAKEN this route by admitting a `play-` identity that cannot
//   satisfy clan_vaults' own FK chain. Recorded in the implementation record as a
//   knowing deviation from the ticket's literal text, with this reasoning.
//
// ⚠ AND IT SPENDS NO clan_rate_limit BUDGET, which is a GAP AND IS FLAGGED, not a
//   ruling. CLAN_RATE_LIMITS' exact shape is pinned by an assert.deepEqual in
//   test/clan-roles.test.js:566 (WO-1846's file, and another lane was live in this tree
//   tonight), and passing an action string that is not one of its six keys silently
//   applies the strictest budget while logging itself as a programming mistake. The
//   RPC fan-out this route can cause is bounded instead by clan-vaults'
//   MAX_VAULT_SIGNERS and genesis-token's MAX_MINT_PROBES. A seventh declared budget is
//   the right fix and belongs to whoever owns that test file.
// =============================================================================

'use strict';

const { AuthCode } = require('../../_lib/wallet-auth');
const { quietFail } = require('../../_lib/http');
const { beginClanRequest, clanFail } = require('../../_lib/clan-http');
const { readMembership, ClanCode } = require('../../_lib/clan');
const { registerClanVault, readClanVaultRow, VaultCode } = require('../../_lib/clan-vaults');

async function handler(req, res) {
    const pre = await beginClanRequest(req, res, 'POST');
    if (pre.done) return;
    const { sql, wallet, body, ref } = pre;

    // The caller's clan comes from their OWN proven membership and never from the request,
    // so this route cannot register a vault against somebody else's clan. Same property
    // /vigil relies on, stated in the same words there.
    let membership;
    try {
        membership = await readMembership(sql, wallet);
    } catch (err) {
        console.error('[clan/vault/register] membership read failed:', err);
        return quietFail(res, 500, AuthCode.SERVER_ERROR, ref);
    }
    if (!membership) {
        return clanFail(res, 404, ClanCode.NOT_IN_CLAN, ref);
    }

    // ⛔ LEADER ONLY, AND COMPARED CASE-INSENSITIVELY. Migration 0030's CHECK stores
    // lowercase ('leader','officer','member'), but role strings are rendered capitalised
    // in fixtures and clients across this project, and a vault binding is not the place to
    // discover a casing mismatch by silently refusing the real Leader.
    if (String(membership.role || '').trim().toLowerCase() !== 'leader') {
        return clanFail(res, 403, VaultCode.NOT_LEADER, ref);
    }

    let result;
    try {
        result = await registerClanVault(
            sql, membership.clanId, body.vaultAddress, body.vaultIndex);
    } catch (err) {
        // registerClanVault swallows every chain fault and every uniqueness collision by
        // design, so reaching here means a genuine database fault — the one thing that
        // leaves nothing to answer with.
        console.error('[clan/vault/register] vault registration failed:', err);
        return quietFail(res, 500, AuthCode.SERVER_ERROR, ref);
    }

    if (!result.ok) {
        // The two refusals the work order says must NAME the signer, and only those two.
        if (result.wallet
            && (result.code === VaultCode.SIGNER_NO_SGT || result.code === VaultCode.SIGNER_SGT_REUSED)) {
            return res.status(result.status).json({
                ok: false,
                error: result.code,
                code: result.code,
                signer_wallet: result.wallet,
                ref: ref,
            });
        }
        return clanFail(res, result.status, result.code, ref);
    }

    // Re-read the row so `hardware_backed` is the SAME live-derived count every /vigil read
    // uses, rather than a second expression of "we just checked them all". They should of
    // course agree — and if they ever do not, the row is the truth and this is where that
    // becomes visible instead of being asserted away.
    let row = null;
    try {
        row = await readClanVaultRow(sql, membership.clanId);
    } catch (_) { /* the insert succeeded; a read-back failure must not undo it */ }

    const v = result.vault;

    // ⛔ ON A READ-BACK FAILURE THE ANSWER IS `null`, NOT `true`.
    //    A hardcoded `true` here would be the ONLY place in this feature where
    //    hardware-backed status is ASSERTED rather than DERIVED — and clan-vaults.js's own
    //    schema note says there is no stored boolean precisely so that can never happen. The
    //    claim would even be true today (registration refuses any signer without a verified
    //    SGT, so every row it writes is hardware-backed the instant it is written), which is
    //    exactly what makes it the dangerous kind of shortcut: correct now, and a lie the
    //    first time the rule changes. `null` means "not derived on this response"; the client
    //    reads /api/clan/vigil, which always derives it.
    const hardwareBacked = row ? row.hardwareBacked : null;
    return res.status(200).json({
        ok: true,
        vault: {
            vault_address: v.vaultAddress,
            multisig_address: v.multisigAddress,
            vault_index: v.vaultIndex,
            threshold: v.threshold,
            signer_count: v.signerWallets.length,
            signer_wallets: v.signerWallets,
            time_lock: v.timeLock,
            config_authority: v.configAuthority,
            verified_at: v.verifiedAt,
        },
        hardware_backed: hardwareBacked,
        // ⭐ THE ONE PIECE OF PLAYER-FACING COPY THIS TICKET IS PERMITTED TO CARRY, quoted
        // from the work order's LEGAL / COPY GATE verbatim and not extended by a syllable.
        // ⛔ DO NOT ADD A SECOND LINE HERE, DO NOT REWORD THIS ONE, AND DO NOT WRITE ANY
        //    NEW COPY ABOUT SEEKER OWNERSHIP, GENESIS TOKENS OR STAKING ANYWHERE IN THIS
        //    FEATURE. The governing review (docs/SKR_VISION_RECONCILIATION_2026-09-11.md)
        //    forbids implying that holding a Seeker or a Genesis Token is an investment, a
        //    financial product or a return, or claiming the mechanism's legal status in any
        //    jurisdiction. Anything beyond this sentence needs the owner's explicit review
        //    BEFORE it ships — the work order says so in those words.
        message: 'The ancestors remember what you built together.',
    });
}

module.exports = handler;
module.exports.config = { api: { bodyParser: false } };

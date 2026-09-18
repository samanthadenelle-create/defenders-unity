# WORK ORDER 1854 — Clan system, step 11: Collective Vigil — Squads multisig + Genesis Token

**Status:** READY TO IMPLEMENT

**Both prior blockers resolved 2026-09-17 (owner, live):**
- **Genesis Token mint address** — `GT2zuHVaZQYZSyQMgJPLzvkmyztfyXg2NJunqFp4p3A4`, already confirmed
  against official Solana Mobile docs earlier this session
  (`docs/SKR_ALANIA_ROOT_NETWORK_EXPLORATION_2026-09-17.md`). Do not re-verify from scratch, but DO
  re-confirm it hasn't changed if the lane has any doubt — never assume from a doc summary alone
  when a live check is cheap (CLAUDE.md §11B).
- **RPC provider** — a Helius RPC endpoint the owner already uses on another project is now in this
  repo's `.env.local` as `HELIUS_RPC_URL` (added 2026-09-17 with her explicit go-ahead; the raw value
  is NOT written into this ticket or any tracked file). **Flag in the hand-back, do not silently
  assume:** this credential is shared with an unrelated project (a CLMM/DeFi bot) — reusing it here
  shares its rate limit and billing. This is noted for the owner's morning review, not a reason to
  block tonight's implementation.

## Context — clan WO-11 in the chain, depends on WO-1853

Source: `docs/SKR Integtration.md` ("WO-11"). Marked "prize-critical" in the source spec (hackathon
prize relevance) — build carefully, this is the pitch's strongest single mechanic.

## Scope

Extend the Vigil read (WO-1852) to support a clan-level Squads multisig vault instead of individual
wallets. Add `verifyGenesisToken(sql, wallet)` to `wallet-auth.js`. Add `clan_vaults` table. A clan's
collective Vigil is the vault's stake; only clans whose vault signers ALL hold a verified Seeker
Genesis Token activate "hardware-backed collective" status.

### `verifyGenesisToken(sql, wallet)`

1. Assumes the caller already proved wallet control via SIWS or session (this function does not
   itself re-verify identity).
2. Call Helius RPC `getTokenAccountsByOwnerV2` filtered to the SGT mint authority (the address above).
   **VERIFY BEFORE BUILD:** confirm the reused Helius plan actually supports this method — it is a
   Helius-specific extension, not a standard Solana RPC call; if the plan doesn't support it, say so
   plainly and use the standard `getTokenAccountsByOwner` + manual mint filtering as a fallback rather
   than silently failing.
3. No account found → `{ ok: false, reason: 'no_sgt' }`.
4. Found → read the token account's mint address. Query `wallet_identity` for any OTHER row where
   `sgt_mint` matches and `wallet != <current wallet>`. A match → `{ ok: false, reason: 'sgt_reused' }`.
5. Otherwise: set `sgt_mint`/`sgt_verified_at` on the current wallet's row, return `{ ok: true, mint }`.

### Schema (new migration file)

```sql
CREATE TABLE IF NOT EXISTS clan_vaults (
  clan_id UUID PRIMARY KEY REFERENCES clans(id) ON DELETE CASCADE,
  vault_address TEXT NOT NULL UNIQUE,
  signer_wallets JSONB NOT NULL,
  threshold INTEGER NOT NULL,
  verified_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  CONSTRAINT clan_vaults_threshold_range CHECK (threshold >= 1),
  CONSTRAINT clan_vaults_signers_is_array CHECK (jsonb_typeof(signer_wallets) = 'array')
);
```

### New endpoints

- `POST /api/clan/vault/register` — auth `authenticateGranting()` (this binds real identity to a
  vault, a stronger auth requirement than the plain clan endpoints). Body `{ vaultAddress }`. Caller
  must be clan Leader. Reads the vault's signer list + threshold via `@sqds/multisig`'s on-chain read
  functions (move it from `devDependencies` to `dependencies` in `package.json` — first real
  consumer). Calls `verifyGenesisToken` on every signer; any signer lacking a verified SGT → 403
  naming the offending wallet (never render the FULL wallet unnecessarily elsewhere, but this
  specific error needs to name it so the Leader knows which signer to fix — consistent with how
  clan endpoints already handle target-wallet identification). On success, insert the `clan_vaults`
  row.
- `GET /api/clan/vigil` (extend WO-1852's endpoint): if the clan has a registered vault, include the
  vault's stake data alongside per-member data. `collectiveVigilWeight` computed from the vault's SKR
  stake. `hardwareBacked: true` only if ALL signers have verified SGT.

## Non-scope

- No vault CREATION flow in the game — the clan brings an existing vault address; the game only
  verifies it ("bring your own vault," owner-ruled per the source spec — confirm this still holds if
  genuinely ambiguous, but do not build a creation flow).
- Does NOT replace WO-1852 — that stays the single-wallet path; this is an additive layer.
- No Squads transaction execution from the game — the game only READS the vault; Squads/the vault's
  own signers handle all signing, always.

## Acceptance criteria

- [ ] `verifyGenesisToken` returns `ok: true` for a wallet holding a valid SGT.
- [ ] Returns `ok: false, reason: 'sgt_reused'` if the same mint is already bound to another wallet.
- [ ] Returns `ok: false, reason: 'no_sgt'` for a wallet with no SGT.
- [ ] Registering a vault with a signer lacking an SGT returns 403 and names the offending signer.
- [ ] Registering a valid vault succeeds and writes the `clan_vaults` row.
- [ ] `GET /api/clan/vigil` returns `hardwareBacked: true` for a registered vault with all-verified
      signers.
- [ ] The Squads read works against mainnet, or a devnet vault if mainnet provisioning is genuinely
      not available tonight — say which was used and why.

## Test plan

1. Wallet A holds a valid SGT → `verifyGenesisToken` asserts `ok`.
2. Simulate transferring the SGT to Wallet B → `verifyGenesisToken(B)` asserts `sgt_reused` (A still
   has the mint recorded).
3. Create a 2-of-2 Squads vault with A and B → register it → assert the vault row exists.
4. Create a 2-of-2 vault with A and a wallet with no SGT → register → assert 403.
5. Call `/api/clan/vigil` → assert `hardwareBacked: true` for the all-verified vault.

## Rollback

Drop `clan_vaults`. Remove `verifyGenesisToken` and the vault registration endpoint. Revert
`/api/clan/vigil` to its WO-1852 shape. No data loss on individual wallet rows — only `sgt_mint`
columns become unused.

## VERIFY BEFORE BUILD

- **Squads version** — confirm the target version (`@sqds/multisig@^2.1.4` is what's already in
  `package.json`; the source spec discusses v4 as unconfirmed) before writing against a specific API
  shape. Read the actual installed package version and its real exported functions — do not assume
  from the spec's prose.
- Confirm `@sqds/multisig` imports cleanly in the Vercel serverless runtime (pure JS/TS, no native
  deps) — a real import-and-call smoke test, not an assumption.
- Confirm the reused Helius plan supports `getTokenAccountsByOwnerV2` (see above); have a fallback
  ready.
- Confirm whether the SGT is a single mint per device or a collection — the uniqueness check must be
  against the mint, not an associated token account, if it's the latter shape.

## ⛔ LEGAL / COPY GATE — BINDING, DO NOT SKIP

Per the governing SKR review (`docs/SKR_VISION_RECONCILIATION_2026-09-11.md`): never make an
unsupported legal claim about sponsorship, gambling, or securities status. Player-facing copy for
hardware-backed status: **"The ancestors remember what you built together."** Never imply that
holding a Seeker or a Genesis Token constitutes an investment, a financial product, or a return.
Never claim legal status of the mechanism in ANY jurisdiction. **Any new public-facing copy beyond
the one line above must be flagged for the owner's explicit review before shipping — do not write and
ship new legal-adjacent copy autonomously overnight.**

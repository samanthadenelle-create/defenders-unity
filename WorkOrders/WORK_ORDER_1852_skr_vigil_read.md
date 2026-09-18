# WORK ORDER 1852 — Clan system, step 9: SKR Vigil read (percentage-staked + game-tracked tenure)

**Status:** READY TO IMPLEMENT

## Context — clan WO-9 in the chain, depends on WO-1851 (landed, committed 2c22f341d)

Source: `docs/SKR Integtration.md` ("WO-9"), `docs/SKR_ALANIA_ROOT_NETWORK_EXPLORATION_2026-09-17.md`
(the "Vigil" design doc from this session's SKR deep-dive). WO-1851's gate is open; this is the first
ticket that actually reads real on-chain SKR stake state into the clan system.

## Scope

`GET /api/clan/vigil` returning per-member `{wallet, percent_staked, tenure_seconds,
vigil_contribution}` and a clan-level `vigil_weight` aggregate. Populates
`wallet_identity.first_seen_staked_at` on first observation of a staked wallet.

### Implementation

1. Extend `api/_lib/skr-staking.js` with `getStakedPercentage(sql, wallet)`:
   - Call the existing `verifyStake(wallet)` for the active staked amount.
   - Read the wallet's total SKR SPL token balance (one RPC call — **VERIFY BEFORE BUILD**: confirm
     the SKR token mint address at source before writing this; do not assume it from memory/prose).
   - Return `{ stakedRaw, balanceRaw, percent: stakedRaw / balanceRaw }`.
   - Cache 60 seconds per wallet — reuse the existing `resolveServedState`/`isCacheFresh` pattern
     already in `skr-staking.js` rather than inventing a second caching mechanism.
2. Extend `touchWalletIdentity` (`api/_lib/wallet-auth.js`, from WO-1844): after the upsert, if
   `first_seen_staked_at IS NULL`, call `getStakedPercentage`; if `percent > 0`, set
   `first_seen_staked_at = NOW()`. Use **Postgres time**, not Node time (avoids clock skew — owner's
   own stated preference in the source spec).
3. New endpoint `GET /api/clan/vigil`: `authenticate()`, caller must be in a clan (reuse the existing
   `beginClanRequest` preamble pattern). For each member: `tenure_seconds = NOW() - first_seen_staked_at`
   (0 if NULL), `vigil_contribution = percent_staked * tenure_seconds`, `vigil_weight = sum(...)`.

## Non-scope

- No chain history indexing — tenure counts from first observation only (owner-ruled).
- No per-epoch bucketing — raw elapsed seconds since `first_seen_staked_at`.
- No ballot/perk logic — that's WO-1853.
- No client UI.

## Acceptance criteria

- [ ] A wallet with 0 stake: `percent_staked = 0`, `first_seen_staked_at = NULL`.
- [ ] A wallet with stake: `first_seen_staked_at` set on first observation, never updates after.
- [ ] `GET /api/clan/vigil` returns correct sums for a two-member clan with different stakes.
- [ ] An RPC error does NOT 500 the endpoint — affected members show `percent_staked = 0` and a
      `degraded: true` flag on that member (or the response, per whichever shape reads cleaner — pick
      one and be consistent, flag the choice in the hand-back).
- [ ] Caching: two calls within 60 seconds produce exactly one RPC round-trip per wallet.
- [ ] Never renders a wallet address to any surface beyond what the caller's own clan membership
      already permits (this endpoint is clan-internal, not admin/public — still worth a test pinning
      that it never leaks into, say, an error message verbatim if avoidable).

## Test plan

1. Wallet A stakes 100 SKR, holds 200 total → `percent_staked = 0.5`.
2. Wallet B has no stake → `percent_staked = 0`, `first_seen_staked_at = NULL`.
3. Both in one clan, call `/api/clan/vigil` → `vigil_weight = 0.5 × tenure_A` (B contributes 0).
4. Simulate an RPC failure → endpoint still returns 200 with `degraded: true`.

## Rollback

Remove the endpoint; revert `touchWalletIdentity` to its WO-1844 behavior. `first_seen_staked_at`
stays NULL for all rows — no data loss.

## VERIFY BEFORE BUILD

- The SKR token mint address, at source, before writing any balance-read code — do not assume it.
- Confirm `skr-staking.js` doesn't already read total balance somewhere reusable before adding a new
  RPC call.

## Copy rules

"Vigil" is the approved player-facing term for tenure. Never state duration in hours or days — state
in epochs, or "the tree remembers." Never use "earn," "yield," "return," "APY."

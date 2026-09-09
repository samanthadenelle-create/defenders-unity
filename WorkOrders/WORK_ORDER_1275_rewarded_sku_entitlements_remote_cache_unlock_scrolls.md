# WORK ORDER 1275 - Rewarded SKU entitlements, remote cache, and unlock scrolls

**Status:** CLOSED 2026-09-08 - owner felt-test PASS (validated 2026-09-09T02:09:34, build 2026.09.08.361259). PRIOR STATUS: FIXED 2026-08-29 — server-authoritative SKU restore, expiry/revocation handling, permanent Stone Gate progression, and idempotent Wave 7 Healing Caravan Plans implemented and headless-verified; awaiting owner Seeker test.
**Minted:** 2026-08-28 by Codex CLI under WO-1271.
**Lane:** Backend entitlement + progression/content. No purchase-path changes.

## Goal

Allow the server to reward a stable SKU that resolves through the database catalog to existing or
new item data and versioned CDN assets. Assets cache locally until a later APK packages the same SKU;
ownership and expiry remain server-authoritative.

## Entitlement contract

At minimum record player identity, SKU, quantity/state, grant source, idempotent grant ID,
`granted_at`, optional `expires_at`, and revocation/audit state. The client receives resolved
entitlements, not authority to grant them.

The same SKU must transition safely from remote-only to packaged:

1. server grants SKU
2. client resolves DB definition and verified CDN asset
3. client caches locally and exposes it in the appropriate owned/Build collection
4. next APK packages the same SKU
5. resolver prefers equal/newer packaged content and may evict the downloaded copy

## Initial progression rewards

### Stone Gate

- visible in Protection but locked
- unlock permanently after the player creates their first Stone Wall through the Wooden Palisade upgrade path
- card copy: `Locked - Create a Stone Wall to unlock`
- use an existing persisted progression/unlock seam; do not infer from a model merely existing in scene

### Healing Caravan Plans

- Healing Caravan remains visible but locked
- completing Wave 7 awards a **Healing Caravan Plans** scroll through the same proven wave-plan reward pattern used by the earlier wave unlock
- scroll grant is idempotent and permanently records the unlock; it is learned, not repeatedly consumed
- card copy: `Locked - Recover its plans after Wave 7`

### Temporary reward example

- a tournament may grant a tower SKU for five days
- a cosmetic may be granted/equipped for thirty days
- expiry is server-time-based; the SKU definition declares safe expiry behavior and fallback

## Acceptance

- Duplicate grant retries produce one entitlement.
- Clearing device cache/reinstalling does not erase ownership; reconnect restores it.
- Offline cached use obeys the last verified entitlement window and fails safely when authority cannot be refreshed.
- Stone Gate unlock is permanent after the first Stone Wall creation and survives reload.
- Wave 7 awards Healing Caravan Plans exactly once; the Protection collection refreshes immediately.
- A remote-only test SKU resolves, verifies, caches, then resolves to a packaged fixture without duplication.
- Audit records identify the first recipients for later recognition/reward without exposing wallet identity publicly.

## Must not

- Do not use local cache, scene presence, or device clock as ownership/expiry truth.
- Do not download executable gameplay logic.
- Do not burn live promo codes or alter FIRSTWATCH production reward rows.
- Do not make Stone Wall directly buildable merely to satisfy the Stone Gate condition.

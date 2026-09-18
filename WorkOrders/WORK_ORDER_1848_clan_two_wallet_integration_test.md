# WORK ORDER 1848 — Clan system, step 5: two-wallet integration test (the WO-1265 acceptance gate)

**Status:** READY TO IMPLEMENT

## Context — clan WO-5 in the chain, depends on WO-1844/1845/1846 (all landed and committed)

Source: `docs/SKR Integtration.md` ("WO-5"), `CLI_LANES_WO_NUMBERS.md` banner (1848 row). This is
the binding acceptance gate named in WO-1265 (closed 2026-09-03): a real two-wallet test proving
the whole membership/roles/succession/rate-limit chain works end to end before anything downstream
(leaderboard, the public gate, SKR reads) is allowed to build on it.

## Scope

A single integration test file exercising the REAL endpoints (not mocks) against a test database
or the same recording-mock harness the WO-1845/1846 unit tests already use, but end-to-end through
one continuous story rather than isolated per-endpoint cases:

1. Wallet A creates a clan. Verify `clans`/`clan_members` rows, A is Leader.
2. Wallet B joins via A's code. Verify B is Member.
3. A promotes B to Officer. Verify role update.
4. B (Officer) attempts to kick another hypothetical Officer — refused (403), per WO-1846's rule.
5. A (Leader) leaves. Verify succession: B (the sole Officer) becomes Leader, A's row is gone.
6. B, now sole member and Leader, leaves. Verify the clan is deleted entirely (no orphan rows in
   any of `clans`/`clan_members`/`clan_messages`/`clan_reports`/`clan_rate_limit`).
7. Rate limits: exceed the `create` limit (3/hour) on a single wallet across the story above plus
   extra calls; verify the 429 + `retry_after` shape.
8. Confirm every step's `wallet_identity` row (first_seen_at/last_seen_at) updates correctly across
   the story, since every clan action routes through `authenticate()`'s wallet branch.

## Non-scope

No client/Unity changes. No new endpoints — this exercises what WO-1845/1846 already built. No
changes to WO-1847's chat/report-message path (file-disjoint, do not touch).

## Acceptance criteria

- [ ] One test file, `test/clan-two-wallet-integration.test.js`, running the full story above as a
      SINGLE continuous narrative (not 8 isolated tests re-seeding state each time) so it actually
      proves the chain holds together across steps, the way a real pair of players would hit it.
- [ ] Every assertion cites which WO-1845/1846 acceptance criterion it re-proves end to end.
- [ ] `node --test test/*.test.js` before/after counts reported; no regression to existing suites.
- [ ] This is the WO-1265 acceptance gate — say so explicitly in the hand-back, since WO-1850/1851
      are blocked on this ticket by the banner's own dependency chain.

## Test plan

Run the file standalone first (`node --test test/clan-two-wallet-integration.test.js`), then the
full suite, and report both counts.

## Rollback

Delete the new test file. No production code changes, nothing to revert.

## Copy rules

N/A (test-only ticket, no player-facing copy).

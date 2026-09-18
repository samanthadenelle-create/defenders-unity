# WORK ORDER 1848 — Clan system, step 5: two-wallet integration test (the WO-1265 acceptance gate)

**Status:** READY FOR LEAD REVIEW

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

## IMPLEMENTATION RECORD (2026-09-17) — the WO-1265 acceptance gate is now GREEN

**⛔ THIS IS THE WO-1265 ACCEPTANCE GATE.** WO-1850/1851 are blocked on this ticket by the
`CLI_LANES_WO_NUMBERS.md` banner's own dependency chain, and this record is the proof that
the whole membership/roles/succession/rate-limit chain (WO-1844 identity, WO-1845 membership,
WO-1846 roles) holds together end to end, driven by one continuous two-and-three-wallet story
through the REAL route handlers.

**File written:** `test/clan-two-wallet-integration.test.js` — ONE `test()`, one running
in-process world, no re-seeding between steps.

**Why a bespoke stateful mock, not the existing `recordingSql`:** `test/clan-membership.test.js`
and `test/clan-roles.test.js` each re-seed a canned answer per call, which is the right shape for
a unit test and cannot catch a bug that only appears when the SAME clan/membership rows are
carried from one endpoint into the next. This file instead models the actual tables
(`clans`, `clan_members`, `wallet_identity`, `clan_rate_limit`, `auth_sessions`) as plain JS
maps and re-implements the EXACT predicates `api/_lib/clan.js` / `api/_lib/wallet-auth.js` send
(one-clan-per-wallet, the succession `ORDER BY` — officer first, then oldest, then wallet as the
final tiebreak — the WITH-clause snapshot rule for both the leave and succession statements, and
the per-`(wallet, action)` rate budget), verified against the source read at HEAD before writing
this file (§11B — nothing here is inferred from the work order's prose). The SQL *driver* is
mocked, same seam every test in this suite already mocks; what is new is that the state behind
that seam persists across every call in the story.

**The story, three wallets, one continuous narrative** (`WALLET_A`/`WALLET_B`/`WALLET_C`):
1. A creates a clan → `clans`/`clan_members` rows asserted directly against the world's maps, not
   just the HTTP response — re-proves **WO-1845 acceptance criterion 1**.
2. A's `create` budget is then exhausted on the SAME wallet (2 more `create` calls land 409
   `ALREADY_IN_CLAN` and still spend budget, a 4th lands 429) — re-proves **WO-1846 acceptance
   criterion 9** (the work order's step 7, run inline rather than isolated so the spend-before-the-
   action rule is proven against a wallet that is *actually* mid-story, not a fixture).
3. B joins via A's code, then C joins too (C exists so step 4's officer-vs-officer refusal is
   proven against a REAL second officer rather than a hypothetical fixture) — re-proves **WO-1845
   acceptance criterion 2**.
4. A promotes B, then C, to Officer — re-proves **WO-1846 acceptance criterion 1**.
5. B (Officer) attempts to kick C (Officer) → refused 403, table unchanged — re-proves **WO-1846
   acceptance criterion 5** (the work order's step 4). The Leader then kicks C for real (also
   re-proving **WO-1846 acceptance criteria 1 and 4** — a Leader may remove an Officer) so the
   succession step below matches the work order's literal story of B as the *sole* Officer.
6. A (Leader) leaves → succession hands the clan to B; A's row is gone from the table, B's role is
   `leader` in the table — re-proves **WO-1846 acceptance criterion 6**.
7. B, now sole member and Leader, leaves → the clan row and B's member row are both gone; A/B/C all
   read as clan-less afterward — re-proves **WO-1846 acceptance criterion 8** (the work order's
   step 6: "no orphan rows in any of clans/clan_members/clan_messages/clan_reports/clan_rate_limit"
   — the last three are never addressed by a clan id in this story at all, so the check reduces to
   the two tables actually touched, both asserted empty).
8. `wallet_identity` is checked at three points across the story (WO-1848 step 8): `first_seen_at`
   is captured on each wallet's first proven call and asserted UNCHANGED afterward; `last_seen_at`
   is asserted to have advanced by the story's end.
9. B is then proven free to found a SECOND clan after the first was disbanded, closing the loop on
   "the one-clan-per-wallet index did not wedge on the old row."

**Test run:** before 1033 tests / 1031 pass / 1 fail → after 1034 / 1032 / 1 fail. The one failure
(`test/heartbound-suite.test.js:264`, an `Assets/Editor/WallTools/RaidPostAudit.cs` assertion) is
pre-existing and identical before and after; this file does not touch Heartbound, WallTools, or any
non-clan lane. `node --check test/clan-two-wallet-integration.test.js` passes; the new file also
passes standalone (`node --test test/clan-two-wallet-integration.test.js` → 1/1).

**What was NOT executed, stated plainly (same discipline as WORK_ORDER_1845's record):** no
Postgres has parsed or executed any of these statements — the mock is a from-source re-derivation
of their predicates, not a measurement of Neon's actual behaviour. The untyped-`$n` coercion risk
WORK_ORDER_1845 flagged for the create/join/leave CTEs, and the `::text` cast `clan-roles.test.js`
pins for the Officer-kick predicate, are both **assumed correct** here rather than re-verified —
this file is an acceptance-level proof that the JS logic holds together across a full story, not a
live-database smoke test. Cheapest close, unchanged from WORK_ORDER_1845: one real create → join →
promote → kick → leave pass against a preview database once traffic justifies it.

# WO-1846 — Clan system, step 3: roles, authorization, leader succession, rate limits

**Status:** READY FOR LEAD REVIEW

## Context — clan WO-3 in the chain, depends on WO-1845

Source material: `docs/SKR Integtration.md` ("WO-3"), `docs/CLAN_WORK_ORDERS_PROOFING_2026-09-17.md`.
Depends on WO-1845 (`clans`/`clan_members` tables + create/join/leave/me endpoints), already landed
and committed.

## Scope

Assign the Officer role for the first time. Define leader succession. Add clan-specific rate limits
following the `touchGuestRate` pattern already in `api/_lib/wallet-auth.js`. Add Officer kick power
(owner ruling, added after the original draft — see below).

## Non-scope

- No invite-code rotation. One code per clan, fixed at creation.
- No permission system beyond the four powers named below (promote, demote, kick, invite-share).

## Role semantics — first-pass engineering default, owner-confirmed additions noted

- **Leader:** one per clan. Can promote Members to Officer and demote Officers to Member. Can
  **kick** any Member or Officer. Can leave only via succession (below).
- **Officer:** unlimited count. Can invite (share the code). **Can kick Members** (owner ruling: add
  kick power to Officer — a demo clan needs a way to remove a disruptive member before real
  moderation tooling exists). Cannot promote/demote. Cannot kick another Officer or the Leader.
- **Member:** default role. Can leave.

## Leader succession — first-pass engineering default

When a Leader calls `POST /api/clan/leave`:
1. Find the oldest Officer by `joined_at`. If one exists, promote them to Leader, then delete the
   leaving Leader's row.
2. If no Officer exists, find the oldest Member by `joined_at`. Promote them to Leader, then delete
   the leaving Leader's row.
3. If no other member exists, delete the clan and return `{ ok: true, clanDeleted: true }`.

## New endpoints

**`POST /api/clan/promote`** / **`POST /api/clan/demote`** — Auth: `authenticate()`. Caller must be
Leader of the same clan as the target. Target must be a Member (for promote) or Officer (for
demote) of the same clan. Return the updated role.

**`POST /api/clan/kick`** — body `{ wallet }`. Auth: `authenticate()`. Caller must be Leader or
Officer of the same clan as the target. A Leader can kick anyone except themself. An Officer can
kick only a Member (never another Officer, never the Leader). Delete the target's `clan_members`
row. Return `{ ok: true }`.

## Rate limits (following `touchGuestRate`'s exact shape)

Add a `clan_rate_limit` table with the same shape as `guest_rate_limit` (read that table's actual
schema before mirroring it — don't assume its column names). Add `touchClanRate(sql, wallet,
action)` to `wallet-auth.js`. Actions: `create`, `join`, `leave`, `promote`, `demote`, `kick`.

Limits (first pass, tunable):
- `create`: 3 per hour per wallet.
- `join`: 10 per hour per wallet.
- `leave`: 5 per hour per wallet.
- `promote` / `demote` / `kick`: 20 per hour per wallet.

Fail-open on a missing table, matching the existing pattern exactly.

## Acceptance criteria

- [ ] Leader can promote a Member to Officer. Role updates in DB.
- [ ] Leader can demote an Officer to Member.
- [ ] Non-Leader calling promote/demote returns 403.
- [ ] Leader can kick a Member or an Officer.
- [ ] Officer can kick a Member but not another Officer or the Leader (403).
- [ ] Leader leaving with an Officer present promotes that Officer.
- [ ] Leader leaving with only Members promotes the oldest Member.
- [ ] Leader leaving as sole member deletes the clan.
- [ ] Exceeding any rate limit returns 429 with a `retry_after` hint.
- [ ] Rate-limit table missing → requests still succeed, warning logged.
- [ ] `node --test test/*.test.js` before/after counts reported.

## Test plan

1. Create clan with A (Leader), B and C join. A promotes B to Officer. A leaves. Verify B is now
   Leader.
2. Same setup without promotion. A leaves. Verify B (oldest member) is Leader.
3. A leaves as sole member. Verify clan gone.
4. B (now Officer) kicks C (Member). Verify C removed. B attempts to kick another Officer. Verify
   403.
5. Call promote 25 times within an hour. Verify the 21st returns 429.

## Rollback

Drop `clan_rate_limit`. Remove promote/demote/kick endpoints. Revert leave logic to the WO-1845
behavior (leader cannot leave). No data loss.

## VERIFY BEFORE BUILD

- Confirm the existing `guest_rate_limit` schema before mirroring it — read its actual columns,
  don't assume.
- Confirm there is no existing `clan_rate_limit` table.

## Implementation record (2026-09-17, WO-1846 lane)

Files: `api/_lib/clan.js` (OFFICER, five new ClanCodes, `readMemberInClan`,
`normalizeTargetWallet`, `readRoleContext`, `setMemberRole`, `promoteMember`,
`demoteMember`, `kickMember`, `leaveAsLeader`; `leaveClan`'s leader branch rewritten),
`api/_lib/wallet-auth.js` (`AuthCode.CLAN_RATE_LIMITED`, `CLAN_RATE_LIMITS`,
`CLAN_RATE_WINDOW_SECONDS`, `touchClanRate`), `api/_lib/clan-http.js` (optional 4th
`action` arg on `beginClanRequest` → 429; `clanTargetWallet`), `api/clan/promote.js`,
`api/clan/demote.js`, `api/clan/kick.js` (new), `api/clan/{create,join,leave}.js`
(action wired; leave's response and header), `api/migrations/20260917_0032_clan_rate_limit.sql`
(new — 0031 was taken by the concurrent WO-1847 lane), `api/schema.sql`,
`test/clan-roles.test.js` (new), `test/clan-membership.test.js` (three WO-1845 cases
superseded, three new routes added to the shared-preamble oracles).

- Verified STATICALLY: no DDL was run against live Neon from this lane. The migration is
  asserted to exist, to be `IF NOT EXISTS`, to pass `auditAdditive` (the actual deploy
  gate), to declare every column `touchClanRate` writes, and to be named by `schema.sql`.
- ⚠ FOR THE CLIENT LANE: the WO's `{ wallet }` kick body collides with the preamble's
  claimed-identity chain, where `body.wallet` means "who is calling". `{ playerId: caller,
  wallet: target }` works; `{ wallet: target }` alone fails auth closed (a 401, never a
  wrong-caller action). `target` / `targetWallet` are unambiguous aliases and are what a
  client should send. Pinned by two cases in `test/clan-roles.test.js`.
- ⚠ WO-1845's `leader_must_transfer` 409 is RETIRED as a rule; the literal string survives
  as the RACE label only. Recorded in `leave.js`'s header and `leaveClan`'s doc block.

## Copy rules

No investment language. Promotion is a game role, not an asset or governance right. Kick copy
should be plain and non-punitive in tone (e.g. "Removed from the clan"), since no appeals/review
process exists yet.

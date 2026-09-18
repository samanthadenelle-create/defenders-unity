WO-1 — Server-side wallet identity table + first-seen tracking
Status: Ready to build. No dependencies.

Scope:
Create wallet_identity table. Populate it on every successful authenticate() call in wallet-auth.js. This is the prerequisite for the clan system, the Vigil read, and Genesis Token binding.

Non-scope:

No multi-wallet linking. One wallet = one row. Wallet switching orphans the old row (see VERIFY BEFORE BUILD).

No profile fields (display name, avatar). Only identity and timestamps.

No migration of existing player_data rows. wallet_identity is a new, separate table.

Schema (new migration file api/migrations/<timestamp>_wallet_identity.sql):

sql
CREATE TABLE IF NOT EXISTS wallet_identity (
  wallet TEXT PRIMARY KEY,
  first_seen_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  last_seen_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  first_seen_staked_at TIMESTAMPTZ NULL,
  sgt_mint TEXT NULL,
  sgt_verified_at TIMESTAMPTZ NULL
);

CREATE INDEX IF NOT EXISTS wallet_identity_first_seen_staked_idx
  ON wallet_identity (first_seen_staked_at)
  WHERE first_seen_staked_at IS NOT NULL;
Implementation:

Add to api/_lib/wallet-auth.js a new exported function touchWalletIdentity(sql, wallet):

Upsert on wallet PK.

On insert: set first_seen_at and last_seen_at to NOW().

On conflict: update last_seen_at to NOW() only.

Wrap in try/catch. On failure, log and continue (fail-open, matching touchGuestRate's pattern).

Do not throw. Identity tracking must never block a request that would otherwise succeed.

Call touchWalletIdentity(sql, wallet) from authenticate() and authenticateGranting() immediately after verifySession / verifyAndConsume returns {ok: true, wallet}. Not from authenticatePromoRedeem() — promo redemption is a narrower path and out of scope.

Acceptance criteria:

First call with a new wallet inserts a row with first_seen_at and last_seen_at set.

Second call with the same wallet updates only last_seen_at.

first_seen_staked_at remains NULL on both — it's populated by WO-9.

A failed DB write does not cause the auth call to fail. Verify by temporarily dropping the table and confirming existing auth still succeeds.

No existing route behavior changes when the table exists and is healthy.

Test plan:

Insert a wallet, verify row.

Re-authenticate, verify last_seen_at advanced and first_seen_at did not.

Simulate missing table by renaming it, run a request through authenticate(), verify 200 and a logged warning.

Restore table, verify no further errors.

Rollback:
Drop the table. Remove the touchWalletIdentity call sites. Auth continues to work without identity tracking.

VERIFY BEFORE BUILD:

Confirm Neon Postgres supports the WHERE ... IS NOT NULL partial index syntax used above (it does, but verify against the actual Neon version in use).

Confirm no existing table is already named wallet_identity. The fact packet says no such table exists, but re-grep before writing the migration.

OWNER RULED — wallet switching:
Deferred. A wallet switch orphans the old clan membership. The clan UI must state this honestly. Wording approved for player-facing copy:

"Your clan membership is tied to your wallet. Switching wallets will require rejoining."

Copy rules (for any UI surfacing this WO):
No investment language. No "earn," "yield," "return," "APY." Identity is not a financial product.

WO-2 — Clan data model + create/join/leave endpoints
Status: Ready to build. Depends on WO-1.

Scope:
Create clans, clan_members, and clan_messages tables. Implement POST /api/clan/create, POST /api/clan/join, POST /api/clan/leave, GET /api/clan/me. Server-side invite-code generation (replaces client-side GenerateClanCode()).

Non-scope:

No chat endpoints. clan_messages is created but unused until WO-4.

No role assignment beyond Leader. Officer and Member assignment is WO-3.

No leader succession. Leaving as Leader is blocked until WO-3 defines the rule.

No rate limits beyond the existing generic ones. Clan-specific rate limits are WO-3.

No leaderboard. That's WO-7.

Schema (new migration file api/migrations/<timestamp>_clan_tables.sql):

sql
CREATE TABLE IF NOT EXISTS clans (
  id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  code TEXT NOT NULL UNIQUE,
  name TEXT NOT NULL,
  tag TEXT NOT NULL,
  join_policy TEXT NOT NULL DEFAULT 'invite',
  created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  created_by_wallet TEXT NOT NULL REFERENCES wallet_identity(wallet),
  CONSTRAINT clans_code_format CHECK (code ~ '^[A-HJ-NP-Z2-9]{6}$'),
  CONSTRAINT clans_name_len CHECK (char_length(name) BETWEEN 1 AND 32),
  CONSTRAINT clans_tag_len CHECK (char_length(tag) BETWEEN 1 AND 5)
);

CREATE TABLE IF NOT EXISTS clan_members (
  clan_id UUID NOT NULL REFERENCES clans(id) ON DELETE CASCADE,
  wallet TEXT NOT NULL REFERENCES wallet_identity(wallet),
  role TEXT NOT NULL DEFAULT 'member',
  joined_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  PRIMARY KEY (clan_id, wallet),
  CONSTRAINT clan_members_role_valid CHECK (role IN ('leader','officer','member'))
);

CREATE UNIQUE INDEX IF NOT EXISTS clan_members_one_clan_per_wallet
  ON clan_members (wallet);

CREATE TABLE IF NOT EXISTS clan_messages (
  id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  clan_id UUID NOT NULL REFERENCES clans(id) ON DELETE CASCADE,
  sender_wallet TEXT NOT NULL REFERENCES wallet_identity(wallet),
  phrase_id TEXT NULL,
  text TEXT NULL,
  sent_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  CONSTRAINT clan_messages_has_body CHECK (phrase_id IS NOT NULL OR text IS NOT NULL)
);

CREATE INDEX IF NOT EXISTS clan_messages_clan_sent_idx
  ON clan_messages (clan_id, sent_at DESC);
Endpoint behavior:

POST /api/clan/create

Body: { name, tag }

Auth: authenticate().

Reject if wallet is already in a clan (clan_members_one_clan_per_wallet).

Generate server-side invite code: 6 chars from ABCDEFGHJKLMNPQRSTUVWXYZ23456789. Retry on collision (max 10 attempts, then 500).

Insert clans row, insert clan_members row with role='leader'.

Return { clanId, code, name, tag, role: 'leader' }.

POST /api/clan/join

Body: { code }

Auth: authenticate().

Reject if wallet already in a clan.

Look up clan by code (uppercase-normalized). 404 if not found.

Insert clan_members row with role='member'.

Return { clanId, name, tag, role: 'member' }.

POST /api/clan/leave

Auth: authenticate().

Reject if wallet is Leader (until WO-3 defines succession). Return 409 with { error: 'leader_must_transfer' }.

Delete clan_members row. If clan has zero members after, delete the clans row.

Return { ok: true }.

GET /api/clan/me

Auth: authenticate().

Return the caller's clan membership + clan info, or { clan: null }.

Acceptance criteria:

Create returns a valid code matching the format constraint. Code is unique.

Join with a valid code adds the wallet as Member.

Join with an invalid code returns 404.

Join when already in a clan returns 409.

Leave as Member removes the row. If last member, clan is deleted.

Leave as Leader returns 409 with the correct error code.

GET /api/clan/me returns null for a wallet with no clan, populated for one with a clan.

All endpoints reject unauthenticated requests with the same error shape as existing routes.

Test plan:

Create two wallets. Wallet A creates a clan. Wallet B joins via code. Verify both memberships.

Wallet B leaves. Verify Wallet A is still Leader and the clan still exists.

Wallet A attempts to leave. Verify 409.

Attempt join with code "OOOOOO" (contains excluded chars). Verify 400 from the format constraint.

Attempt create with name > 32 chars. Verify 400.

Rollback:
Drop the three tables. No existing data is touched. Client stub still reads from PlayerPrefs until WO-8.

VERIFY BEFORE BUILD:

Confirm gen_random_uuid() is available on the Neon instance (it is on Postgres 13+, but verify).

Confirm the wallet_identity FK is acceptable — if WO-1's table creation fails, WO-2's FKs will fail too. Sequence matters.

Confirm the client's existing ClanState JSON shape maps cleanly to these tables. The fact packet lists: {Id, Code, Name, Tag, JoinPolicy, CreatedAtUnix, Members[], Messages[]}. Fields map 1:1 except JoinPolicy (hardcoded to 'open' in the client, defaulting to 'invite' server-side — decide which wins before WO-8).

Copy rules:
No investment language. Clan membership is not a financial product. Invite codes are not transferable assets.

WO-3 — Roles, authorization, leader succession, rate limits
Status: Ready to build. Depends on WO-2.

Scope:
Assign Officer role for the first time. Define leader succession. Add clan-specific rate limits following the touchGuestRate pattern. Lock invite-code generation to the server (already done in WO-2, this WO removes any remaining client-side path).

Non-scope:

No permission system beyond the three roles. Officer is a promotion/demotion target only, not a distinct permission set yet.

No invite-code rotation. One code per clan, fixed at creation.

No ban/kick. Not in scope for demo.

Role semantics (owner-ruled):

Leader: one per clan. Can promote Members to Officer and demote Officers to Member. Can leave only via succession (see below).

Officer: unlimited count. Can invite (share code), cannot promote/demote, cannot leave with special privileges — leaves like a Member.

Member: default role. Can leave.

Leader succession (owner-ruled):
When a Leader calls POST /api/clan/leave:

Find the oldest Officer by joined_at. If one exists, promote them to Leader, then delete the leaving Leader's row.

If no Officer exists, find the oldest Member by joined_at. Promote them to Leader, then delete the leaving Leader's row.

If no other member exists, delete the clan and return { ok: true, clanDeleted: true }.

POST /api/clan/promote and POST /api/clan/demote

Auth: authenticate().

Caller must be Leader of the same clan as the target.

Target must be a Member (for promote) or Officer (for demote) of the same clan.

Return updated role.

Rate limits (following touchGuestRate):

Add a clan_rate_limit table with the same shape as guest_rate_limit. Add touchClanRate(sql, wallet, action) to wallet-auth.js. Actions: create, join, leave, promote, demote.

Limits (first pass, tunable):

create: 3 per hour per wallet.

join: 10 per hour per wallet.

leave: 5 per hour per wallet.

promote / demote: 20 per hour per wallet.

Fail-open on missing table, matching the existing pattern.

Acceptance criteria:

Leader can promote a Member to Officer. Role updates in DB.

Leader can demote an Officer to Member.

Non-Leader calling promote/demote returns 403.

Leader leaving with an Officer present promotes that Officer.

Leader leaving with only Members promotes the oldest Member.

Leader leaving as sole member deletes the clan.

Exceeding any rate limit returns 429 with a retry_after hint.

Rate-limit table missing → requests still succeed, warning logged.

Test plan:

Create clan with A (Leader), B and C join. A promotes B to Officer. A leaves. Verify B is now Leader.

Same setup without promotion. A leaves. Verify B (oldest member) is Leader.

A leaves as sole member. Verify clan gone.

Call promote 25 times within an hour. Verify the 21st returns 429.

Rollback:
Drop clan_rate_limit. Remove promote/demote endpoints. Revert leave logic to the WO-2 behavior (leader cannot leave). No data loss.

VERIFY BEFORE BUILD:

Confirm the existing guest_rate_limit schema before mirroring it. The fact packet confirms the table exists but does not give its columns.

Confirm there is no existing clan_rate_limit table.

Copy rules:
No investment language. Promotion is a game role, not an asset or governance right.

WO-4 — Cherry Chat embed integration
Status: Ready to build. Depends on WO-2. Replaces the native chat path entirely.

Scope:
Replace ClanChatPanel.cs's native UI with a WebView hosting Cherry's embed SDK. Remove the client-side ChatPhraseCatalog, ring buffer, and free-text path from active use. The server-side clan_messages table stays in the schema but is not written by this WO (Cherry owns message persistence).

Non-scope:

No native chat rendering. The old ClanChatPanel.cs becomes a thin WebView host.

No server-side message moderation or storage. Cherry handles this.

No message read endpoints on the game backend. Cherry's SDK surfaces messages directly.

No fallback native chat. If Cherry fails to load, show an error state, not the old UI.

Client changes:

ClanChatPanel.cs:

Remove all message rendering, phrase catalog, and ring-buffer logic.

On enable, instantiate a WebView (Unity's UnityWebView or the project's existing WebView plugin — confirm which one is present).

Load Cherry's embed URL: https://chat.cherry.fun/embed?wallet=<address>&theme=<theme>.

Wait for chat.mount() equivalent via postMessage bridge.

Register onSignChallenge handler that calls the existing Unity wallet adapter to sign.

Register message listener only for presence/unread-count updates shown in the HUD, not for rendering.

ClanChatPanelBootstrap.cs:

Keep the ClanFeatureGate.PlayerFacingEnabled check as the literal first line of SpawnInScene (the regression lint requires this exact string).

ClanChatVM.cs:

Reduce to a thin state holder: { walletAddress, isMounted, unreadCount }. No message list.

Server changes:
None for this WO. clan_messages remains unused.

Acceptance criteria:

With the gate open and a wallet connected, the clan chat panel loads Cherry's embed UI in a WebView.

The wallet address passed to Cherry matches the authenticated wallet.

A signature challenge from Cherry is satisfied by the Unity wallet adapter without user-visible failure.

Sending a message via Cherry's UI appears in Cherry's UI on a second device within 5 seconds.

The HUD shows an unread count driven by Cherry's message events.

If Cherry's embed fails to load, the panel shows a non-blocking error and does not fall back to the old native UI.

The client-side ChatPhraseCatalog and ring buffer are no longer referenced anywhere in the compiled Android build.

Test plan:

Two devices, two wallets, same clan. Send a message from device A, verify it appears on device B.

Kill network on device A mid-load. Verify the error state, not a crash.

Verify the signature challenge fires on first mount and is cached for the session.

Grep the build for ChatPhraseCatalog and MessageBufferCap. Confirm zero references in the wallet build.

Rollback:
Restore the previous ClanChatPanel.cs from version control. The clan_messages table was never written to, so no data migration is needed.

VERIFY BEFORE BUILD:

Which WebView plugin the project already uses. Unity does not ship a built-in WebView. Confirm the existing plugin (e.g., unity-webview, Vuplex, or a custom one) before writing integration code.

Cherry's embed URL format. The SDK docs referenced in this session describe the SDK, not the raw URL format. Confirm the exact query params (wallet, theme, clan_id if clan-scoped rooms are supported).

Whether Cherry embed supports clan-scoped rooms (one room per clan) or only a single global room. If clan-scoped, the embed URL needs a clan_id param. If not, clans share a global room and chat loses clan specificity — flag this to the owner before building.

Cherry's CSP requirements. Confirm what frame-ancestors values Cherry expects if any.

Signature challenge format. Confirm what Cherry's onSignChallenge sends and what it expects back.

Copy rules:
No investment language in the chat panel chrome or empty states. The panel is communication, not a financial surface. If Cherry's own UI shows any token-gated room language, flag it for review before shipping.

WO-5 — Two-wallet integration test
Status: Ready to build. Depends on WO-1, WO-2, WO-3, WO-4.

Scope:
A test harness, not a feature. Proves the clan system works end-to-end with two real wallets against a staging backend. This is WO-1265's explicit acceptance gate before the client gate can open.

Non-scope:

No new endpoints. No new tables. No client changes.

No load testing. Two wallets, one clan, full lifecycle.

No admin view interaction (WO-6 deferred).

Deliverable:
test/clan/two-wallet.test.js — a Node script runnable via npm run test:clan-two-wallet. Uses two keypairs generated at runtime (or loaded from env for reproducibility). Drives the real staging backend over HTTP.

Test sequence:

Generate or load Wallet A and Wallet B.

Authenticate both via the real nonce-signature flow.

Wallet A creates a clan. Assert code format.

Wallet B joins via code. Assert membership.

Wallet A promotes B to Officer.

Wallet A attempts to leave. Assert succession promotes B to Leader.

Wallet B (now Leader) attempts to leave as sole member. Assert clan deleted.

Authenticate both again. Assert both are clanless.

Chat: Wallet A creates a clan, Wallet B joins, both mount Cherry embed (or a test harness that calls Cherry's API directly if WebView is not scriptable), exchange a message, assert delivery.

Acceptance criteria:

All nine steps pass against staging without manual intervention.

The script exits non-zero on any failed assertion.

The script cleans up any created clans on exit, even on failure.

A second run with the same wallets succeeds (no leftover state from run 1).

Test plan:
Run against staging. Run twice. Verify cleanup. Run with one wallet's signature deliberately invalid and assert a clean failure at step 2.

Rollback:
None. This WO creates no persistent infrastructure.

VERIFY BEFORE BUILD:

Staging environment URL. The fact packet does not name it. Confirm before writing the test.

How to obtain test wallets with real SKR stake for WO-9/10 testing later. Not needed for WO-5, but the same wallets will be reused. Document the process.

Whether Cherry's embed is testable without a WebView. If not, WO-5's step 9 is manual and should be documented as such.

Copy rules:
None. Internal test harness.

WO-6 — Admin clan health view (DEFERRED)
Status: Deferred per owner ruling. Not required for demo cut.

Scope (for when it's built):
GET /api/admin/clan-health behind X-Admin-Key. Returns clan count, member count, message volume per clan, inactive-clan count. Read-only.

Non-scope:

No write actions. No ban/kick. No message inspection.

No per-admin accounts. Uses the existing shared X-Admin-Key.

Acceptance criteria (for when built):

Returns valid JSON with the four metrics.

Rejects requests without a valid X-Admin-Key with the same error shape as api/admin/stats.js.

Does not touch any existing admin route.

Rollback:
Delete the endpoint file. No schema changes.

VERIFY BEFORE BUILD (later):

Confirm api/admin/stats.js's exact error shape before mirroring it.

Copy rules:
None. Internal operator surface.

WO-7 — Clan leaderboard fed by real identities
Status: Ready to build. Depends on WO-5.

Scope:
GET /api/clan/leaderboard returning clans ranked by a real metric. Explicitly does NOT touch the existing unrelated leaderboard (per WO-1265's acceptance criteria).

Non-scope:

No changes to the existing leaderboard route or its tables.

No historical snapshots. Rank is computed live.

No pagination beyond a top-N limit.

Ranking metric (owner-ruled, first pass):
clan_vigil_weight as defined in WO-9. If WO-9 is not yet shipped, fall back to member_count × days_since_created. The fallback exists only so WO-7 can ship before WO-9; once WO-9 lands, the metric switches.

Endpoint:
GET /api/clan/leaderboard?limit=50

Auth: authenticate() (read-only, no value granted).

Returns top N clans: { rank, clanId, name, tag, metric, memberCount }.

Default limit 50, max 100.

Acceptance criteria:

Returns clans ranked descending by the metric.

Ties broken by created_at ascending (older clans rank higher on ties).

A clan with one member and one day old ranks below a clan with three members and ten days old (on the fallback metric).

Does not read from or write to the existing leaderboard tables. Verify by grep and by asserting the existing leaderboard endpoint's response is unchanged.

Unauthenticated requests return the same error shape as other authenticated reads.

Test plan:

Create three clans with different member counts and ages. Verify ranking order.

Hit the existing leaderboard endpoint before and after WO-7 ships. Assert response byte-identical.

Rollback:
Delete the endpoint. No schema changes.

VERIFY BEFORE BUILD:

The existing leaderboard's route path and table names. The fact packet names the directory api/leaderboard/ but not the route or tables. Confirm before writing, so the new route doesn't collide.

Copy rules:
No investment language. A leaderboard is a ranking, not a promise of reward.

WO-8 — Open the gate
Status: Ready to build. Depends on WO-5.

Scope:
Flip ClanFeatureGate.PlayerFacingEnabled to true. Wire ClanService.cs from PlayerPrefs to the real backend. Update the regression lint to match.

Non-scope:

No new endpoints. No new tables.

No removal of the local stub data (per WO-1265's explicit criteria: do not delete the local stub).

No Play Store variant changes. Clan features remain crypto-build-only.

Client changes:

ClanFeatureGate.cs:

Change PlayerFacingEnabled from false to true.

ClanFeatureGateRegression.cs:

Update the raw-text match to require public const bool PlayerFacingEnabled = true;.

Keep the other two checks (ClanChatPanelBootstrap.cs gate string, HudKitController.cs gate check) unchanged.

ClanService.cs:

Replace PlayerPrefs reads/writes with HTTP calls to the WO-2/WO-3 endpoints.

Keep the local JSON blob writer but do not read from it in normal operation. It remains as a migration source for players who created local-only clans before WO-8.

On first launch after WO-8, if a local clan exists and the wallet has no server clan, show a one-time prompt: "You have a local clan. Create it on the server?" — accept → POST /api/clan/create with the same name/tag; decline → discard local state.

HudKitController.cs:

Keep the existing gate check before AddDockTab(..., "Chat", ...). No change needed if the check is already correct.

Acceptance criteria:

With a fresh wallet, the clan door is visible in the dock.

Creating a clan via the UI produces a server row.

Joining via invite code works from a second device.

Leaving works. Succession works.

A player with a legacy local clan sees the one-time migration prompt exactly once.

Declining the prompt leaves the local clan in PlayerPrefs but does not create a server clan.

The regression lint passes with the gate true.

The Play Store build still excludes all clan UI (verify by building the Play variant and confirming the dock has no Chat tab).

Test plan:

Fresh install, fresh wallet. Create a clan. Verify server row.

Second device, second wallet. Join via code. Verify.

Install with a pre-existing local clan (simulate by writing PlayerPrefs directly). Launch. Verify prompt. Accept. Verify server row.

Same, decline. Verify no server row, local state intact.

Build the Play variant. Verify no clan UI in the dock.

Rollback:
Flip PlayerFacingEnabled back to false. Restore ClanFeatureGateRegression.cs. Revert ClanService.cs to PlayerPrefs. Local data survives because it was never deleted.

VERIFY BEFORE BUILD:

Whether the project has a migration-prompt pattern already. If not, this is new UI. Keep it minimal.

The exact string the regression lint matches for HudKitController.cs. The fact packet says it requires the gate check before AddDockTab(..., "Chat", ...) but doesn't give the literal string. Confirm before editing.

Copy rules:

The migration prompt must not use investment language.

The clan UI chrome must not imply that clan membership has monetary value.

If any Cherry UI bleeds through with token-gated language, flag for review.

WO-9 — SKR Vigil read: percentage-staked + game-tracked tenure
Status: Ready to build. Depends on WO-8.

Scope:
Add GET /api/clan/vigil returning per-member {wallet, percent_staked, tenure_seconds, vigil_contribution} and clan aggregate vigil_weight. Populate wallet_identity.first_seen_staked_at on the first observation of a staked wallet.

Non-scope:

No chain history indexing. Tenure counts from first observation only (owner-ruled, per the SKR design doc).

No per-epoch bucketing. Tenure is raw elapsed seconds since first_seen_staked_at.

No ballot or perk logic. That's WO-10.

Implementation:

Extend skr-staking.js with an exported helper getStakedPercentage(sql, wallet):

Call existing verifyStake(wallet) to get active staked amount.

Read the wallet's total SKR balance (standard SPL token balance read — one RPC call).

Return { stakedRaw, balanceRaw, percent: stakedRaw / balanceRaw }.

Cache result for 60 seconds per wallet to avoid hammering RPC.

Extend wallet-auth.js's touchWalletIdentity (from WO-1):

After the upsert, if first_seen_staked_at IS NULL, call getStakedPercentage. If percent > 0, set first_seen_staked_at = NOW().

New endpoint GET /api/clan/vigil:

Auth: authenticate().

Caller must be in a clan.

For each member, read percent_staked (cached) and compute tenure_seconds = NOW() - first_seen_staked_at (0 if NULL).

vigil_contribution = percent_staked × tenure_seconds.

vigil_weight = sum(vigil_contribution) across members.

Return { clanId, vigilWeight, members: [...] }.

Acceptance criteria:

A wallet with 0 stake has percent_staked = 0 and first_seen_staked_at = NULL.

A wallet with stake gets first_seen_staked_at set on first observation and does not update on subsequent observations.

GET /api/clan/vigil returns correct sums for a two-member clan with different stakes.

RPC errors do not 500 the endpoint; affected members show percent_staked = 0 and a degraded: true flag.

Caching: two calls within 60 seconds produce one RPC round-trip per wallet.

Test plan:

Wallet A stakes 100 SKR, holds 200 total. Verify percent_staked = 0.5.

Wallet B has no stake. Verify percent_staked = 0, first_seen_staked_at = NULL.

Both in one clan. Call /api/clan/vigil. Verify vigil_weight = 0.5 × tenure_A (tenure_B contributes 0).

Simulate RPC failure. Verify endpoint returns 200 with degraded: true.

Rollback:
Remove the endpoint. Revert touchWalletIdentity to WO-1 behavior. first_seen_staked_at remains NULL for all rows; no data loss.

VERIFY BEFORE BUILD:

How to read a wallet's total SKR balance. The fact packet confirms skr-staking.js reads stake state but does not confirm it reads total balance. This may be a new RPC call. Confirm the SKR token mint address before writing.

The caching pattern used elsewhere. skr-staking.js already has resolveServedState/isCacheFresh. Reuse rather than reinventing.

Whether NOW() - first_seen_staked_at should use Postgres time or Node time. Postgres time is simpler and avoids clock skew.

Copy rules:

"Vigil" is the approved player-facing term for tenure.

Never state duration in hours or days. State in epochs or "the tree remembers."

Never use "earn," "yield," "return," "APY."

WO-10 — Five-tier perk ladder + ballot
Status: Ready to build. Depends on WO-9.

Scope:
Add clan_ballots, clan_ballot_votes, and clan_perks tables. Implement POST /api/clan/ballot/propose, POST /api/clan/ballot/vote, GET /api/clan/ballot/current. Five perk tiers gated by vigil_weight.

Non-scope:

No Squads multisig integration. That's WO-11.

No Genesis Token gating. That's WO-11.

No perk effects in the game client beyond a flag the client can read. The actual gameplay effect of each perk is a separate client WO, not this one.

No ballot history beyond the current open ballot.

Schema (new migration file api/migrations/<timestamp>_clan_ballots.sql):

sql
CREATE TABLE IF NOT EXISTS clan_ballots (
  id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  clan_id UUID NOT NULL REFERENCES clans(id) ON DELETE CASCADE,
  proposed_by_wallet TEXT NOT NULL REFERENCES wallet_identity(wallet),
  tier INTEGER NOT NULL,
  options JSONB NOT NULL,
  opened_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  closes_at TIMESTAMPTZ NOT NULL,
  closed_at TIMESTAMPTZ NULL,
  winning_option TEXT NULL,
  CONSTRAINT clan_ballots_tier_range CHECK (tier BETWEEN 1 AND 5)
);

CREATE TABLE IF NOT EXISTS clan_ballot_votes (
  ballot_id UUID NOT NULL REFERENCES clan_ballots(id) ON DELETE CASCADE,
  wallet TEXT NOT NULL REFERENCES wallet_identity(wallet),
  option_id TEXT NOT NULL,
  voted_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  weight NUMERIC NOT NULL,
  PRIMARY KEY (ballot_id, wallet)
);

CREATE TABLE IF NOT EXISTS clan_perks (
  clan_id UUID NOT NULL REFERENCES clans(id) ON DELETE CASCADE,
  tier INTEGER NOT NULL,
  perk_id TEXT NOT NULL,
  activated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  expires_at TIMESTAMPTZ NULL,
  PRIMARY KEY (clan_id, tier)
);
Tier gating (first pass, tunable):

Tier	Vigil weight threshold	Example perk options
1	> 0	+5% build speed, +3% harvest yield
2	≥ T2	+10% build speed, +6% harvest yield, +5% wall HP
3	≥ T3	+15% build speed, +10% harvest yield, +10% wall HP
4	≥ T4	A new building type, a defensive turret variant
5	≥ T5	A special troop type (ancestor-summoned)
T2–T5 values are VERIFY BEFORE BUILD — they depend on real vigil_weight numbers from WO-9. Ship with placeholders, retune after the first week of live data.

Cadence (owner-ruled):
Epoch-driven, not calendar-weekly. A ballot opens on the first observation of a new SKR epoch and closes 48 hours later. Since SKR epochs are 2 days, this means a ballot roughly every 2 days. The client reads epoch boundaries from the same source the game already uses for SKR reads (or from a server-computed current_epoch_start).

Ballot lifecycle:

Any clan member calls POST /api/clan/ballot/propose with { tier, optionIds }. Server validates the clan's current vigil_weight meets the tier threshold. Only one open ballot per clan at a time.

Members call POST /api/clan/ballot/vote with { ballotId, optionId }. Weight is the voter's vigil_contribution (from WO-9). One vote per wallet per ballot; re-voting updates the option but not the weight snapshot.

GET /api/clan/ballot/current returns the open ballot + votes + caller's vote.

When closes_at passes, the next request to any ballot endpoint closes it: sets winning_option, inserts a clan_perks row, clears the current ballot.
Acceptance criteria:

A clan with vigil_weight = 0 cannot open a Tier 1 ballot.

A clan with vigil_weight ≥ T2 can open a Tier 2 ballot.

Two members with different vigil_contribution produce different vote weights.

Re-voting updates the option, not the weight.

A closed ballot writes a clan_perks row.

Only one open ballot per clan at a time; a second propose returns 409.

An expired ballot closes on next read and returns the result.

Test plan:

Two-member clan with weights 0.5 and 1.0. Open Tier 1 ballot. Vote differently. Verify winning option by weight.

Open Tier 3 ballot with weight 0.5. Verify 403.

Advance time past closes_at. Call GET /api/clan/ballot/current. Verify closed and perk written.

Rollback:
Drop the three tables. Remove the endpoints. The client reads no perks, so no client state is lost.

VERIFY BEFORE BUILD:

How the server determines "current SKR epoch start." SKR epochs are 2 days, but the server needs a canonical anchor. Confirm with the owner: fixed anchor (e.g., 2026-01-01T00:00:00Z) or read from a chain source.

Realistic vigil_weight magnitudes. T2–T5 thresholds depend on this. Ship placeholders, retune.

Whether perks should expire. Current schema allows expires_at. If not, always NULL.

Copy rules:

Player-facing copy for ballots: "The ancestors listen to those who gather. Stake together, and they will consider your requests."

Perk descriptions must not use investment language.

"Special troop type" copy: "The ancestors stir. A new shape is possible."

Never state duration in hours or days. Use "epoch" or "vigil."

WO-11 — Collective Vigil: Squads multisig + Genesis Token
Status: Prize-critical. Depends on WO-10. Sequence after the critical path lands.

Scope:
Extend the Vigil read (WO-9) to support a clan-level Squads multisig vault instead of individual wallets. Add verifyGenesisToken(wallet) to wallet-auth.js. Add clan_vaults table. The clan's collective Vigil is the vault's stake, and only clans whose vault signers all hold a verified Seeker Genesis Token activate "hardware-backed collective" status.

Non-scope:

No vault creation flow in the game. The clan brings an existing vault address; the game verifies it. (Owner-ruled: confirm before building, but the simpler path is "bring your own vault.")

No replacement of WO-9. WO-9 remains the single-wallet path; WO-11 is an additive layer.

No Squads transaction execution from the game. The game reads the vault; Squads handles all signing.

New dependency usage:
@sqds/multisig@^2.1.4 is already in package.json devDependencies but unused. Move it to dependencies. First real consumer.

New function in wallet-auth.js:

verifyGenesisToken(sql, wallet):

Assume the caller has already proven wallet control via SIWS or session.

Call Helius RPC getTokenAccountsByOwnerV2 filtered to the SGT mint authority GT2zuHVaZQYZSyQMgJPLzvkmyztfyXg2NJunqFp4p3A4.

If no account found, return { ok: false, reason: 'no_sgt' }.

If found, read the token account's mint address.

Query wallet_identity for any row where sgt_mint = <this mint> and wallet != <current wallet>.

If a match exists, return { ok: false, reason: 'sgt_reused' }.

Otherwise, set sgt_mint and sgt_verified_at on the current wallet's row, return { ok: true, mint }.

New schema (new migration file api/migrations/<timestamp>_clan_vaults.sql):

sql
CREATE TABLE IF NOT EXISTS clan_vaults (
  clan_id UUID PRIMARY KEY REFERENCES clans(id) ON DELETE CASCADE,
  vault_address TEXT NOT NULL UNIQUE,
  signer_wallets JSONB NOT NULL,
  threshold INTEGER NOT NULL,
  verified_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  CONSTRAINT clan_vaults_threshold_range CHECK (threshold >= 1),
  CONSTRAINT clan_vaults_signers_is_array CHECK (jsonb_typeof(signer_wallets) = 'array')
);
New endpoints:

POST /api/clan/vault/register

Auth: authenticateGranting() (this binds real identity to a vault).

Body: { vaultAddress }.

Caller must be clan Leader.

Server reads the vault's signer list via @sqds/multisig's on-chain read functions.

Server verifies threshold from the vault's config.

Server calls verifyGenesisToken on each signer.

If any signer lacks a verified SGT, return 403 with the offending wallet.

On success, insert clan_vaults row.

GET /api/clan/vigil (extended)

If the clan has a registered vault, return the vault's stake data in addition to per-member data.

collectiveVigilWeight computed from the vault's SKR stake.

hardwareBacked: true if all signers have verified SGT.

Acceptance criteria:

verifyGenesisToken returns ok: true for a wallet holding a valid SGT.

It returns ok: false, reason: 'sgt_reused' if the same mint is bound to another wallet.

It returns ok: false, reason: 'no_sgt' for a wallet with no SGT.

Registering a vault with a signer lacking an SGT returns 403 and names the signer.

Registering a valid vault succeeds and writes the row.

GET /api/clan/vigil returns hardwareBacked: true for a registered vault with all-verified signers.

The Squads read works against mainnet (or a devnet vault, if mainnet is not yet provisioned).

@sqds/multisig imports cleanly in the Vercel serverless runtime (it's a pure JS/TS package; verify no native deps).

Test plan:

Wallet A holds a valid SGT. Call verifyGenesisToken. Assert ok.

Transfer the SGT to Wallet B (simulating wallet switch). Call verifyGenesisToken(B). Assert sgt_reused because A still has the mint recorded.

Create a 2-of-2 Squads vault with A and B. Register it. Assert vault row.

Create a 2-of-2 vault with A and a wallet with no SGT. Register. Assert 403.

Call /api/clan/vigil. Assert hardwareBacked: true for the all-verified vault.

Rollback:
Drop clan_vaults. Remove verifyGenesisToken and the vault registration endpoint. Revert /api/clan/vigil to the WO-9 version. No data loss on individual wallet rows; only sgt_mint columns become unused.

VERIFY BEFORE BUILD:

Squads v4 vs. earlier versions. The SKR design doc verified v4 against Squads' SDK docs, but the fact packet says v4 is not a project-level commitment. Confirm the target version before writing.

Vault creation flow. Owner-ruled as "bring your own vault," but confirm.

Helius API key and plan. getTokenAccountsByOwnerV2 is a Helius-specific method; confirm the plan supports it.

SGT mint authority address. The SKR design doc names GT2zuHVaZQYZSyQMgJPLzvkmyztfyXg2NJunqFp4p3A4. Confirm against current Solana Mobile docs before hardcoding.

Whether the SGT is a single mint per device or a collection. The docs describe it as a device-bound token; confirm the uniqueness check is against the mint, not an associated token account.

Legal review before any public copy that mentions Genesis Token or hardware backing. Per the governing review's standing constraint: no unsupported legal claims.

Copy rules:

Player-facing copy for hardware-backed status: "The ancestors remember what you built together."

Never imply that holding a Seeker or an SGT constitutes an investment, a financial product, or a return.

Never claim legal status of the mechanism in any jurisdiction.

Consult before any public copy is written.

Post-demo / future WOs (not written, listed for completeness)
Wallet switching / recovery. A wallet-linking table and a UI to claim an old identity with a new wallet. Not required for demo. Would be its own WO-12.

Perk effects in the game client. WO-10 writes a clan_perks row; a separate client WO reads it and applies the gameplay effect. Out of scope for this set.

Admin clan health view (WO-6). Deferred. Full WO available on request.

Real-time chat beyond Cherry. Not needed if Cherry embed works as expected.

Handoff notes for Codex CLI + Claude/Fable
Execution order:
WO-1 → WO-2 → WO-3 → WO-4 → WO-5 → WO-7 → WO-8 → WO-9 → WO-10 → WO-11.
WO-6 is deferred.

Review gates (Claude/Fable):

After WO-1: verify the fail-open behavior actually works by dropping the table.

After WO-2: verify all four endpoints against the acceptance criteria before WO-3 starts.

After WO-4: verify the WebView integration does not crash on Cherry load failure.

After WO-5: this is the gate. Do not start WO-7 until WO-5 passes fully.

After WO-8: verify the Play variant still excludes clan UI.

After WO-10: verify the ballot closes correctly on epoch boundary.

After WO-11: verify Genesis Token reuse detection with a real transferred SGT.

Common pitfalls to check on every WO:

No investment language in any player-facing string.

No duration stated in hours or days; use "epoch" or "vigil."

No unsupported legal claims.

No deletion of the local stub (WO-1265 requirement).

No touching the existing unrelated leaderboard (WO-1265 requirement).

No bypassing the ClanFeatureGate before WO-8.

What this set does not do:

It does not build the SKR-to-crystal cash-out loop (governing review forbids).

It does not build player-vs-player wagering (forbidden).

It does not seize or freeze real player tokens (forbidden).

It does not promise investment returns (forbidden).

It does not touch the Play Store variant's crypto exclusion (structural constraint).
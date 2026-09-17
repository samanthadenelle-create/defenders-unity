# Clan System — Fact Packet for Work-Order Drafting

Compiled 2026-09-17 in direct response to DeepSeek's questionnaire for writing the clan-system work
orders. Every fact below is sourced from this session reading the actual repository — file and line
cited wherever possible. Anything not determinable from the repo is marked
**[UNKNOWN — verify before build]** rather than guessed, per this project's binding rule: never
guess, prove it or say you haven't.

This packet does not decide anything. It answers "what exists today" so the work orders can be
written against real ground instead of assumption.

---

## 1. Stack and environment facts

- **Backend language/framework:** Plain Node.js, CommonJS, serverless functions under `api/`. No
  framework — no Express, Fastify, or Hono. `package.json`: `"description": "Vercel serverless
  functions (api/) ... CommonJS"`, `"engines": {"node": ">=18"}`.
- **Where it runs:** Vercel. `vercel.json` at repo root configures cron jobs, rewrites, and headers
  in Vercel's own format. Already live in production, not a future TODO — three cron jobs run today
  (`admin/cleanup`, `google-play-voided-reconcile`, `cron/heart-pulse`).
- **Database:** Neon Postgres, via `@neondatabase/serverless` (`package.json`, `^0.10.4`). Already
  provisioned and actively migrated — `api/schema.sql` carries multiple dated `ALTER TABLE`
  migrations (e.g. a `player_data` reset-epoch column from WO-1598, a schema-version column from
  WO-1457). This is a live production database, not something a WO needs to stand up from zero.
- **Auth mechanism (`api/_lib/wallet-auth.js`, read in full):**
  - Two proof types: a real ed25519 signature over a server-issued nonce
    (`verifySignature`/`verifyAndConsume`), or a bearer session token (`verifySession`) issued after
    that signature succeeds.
  - `verifySession(sql, token, claimedPlayerId)` looks up `auth_sessions` by token, checks
    `revoked`/`expires_at`, and enforces the token's bound wallet matches the `claimedPlayerId` the
    request is acting on (fails with `SESSION_WRONG_WALLET` otherwise). Returns `{ok, wallet}`.
  - A "logged-in" request carries an `X-Session` header (a session token) or signs a fresh nonce.
    `authenticate()` is the general entry point; `authenticateGranting()` is required for any route
    that hands out real value (referrals, entitlements); `authenticatePromoRedeem()` is a narrower
    one-off for promo redemption only.
  - **Three identity kinds exist today, not just wallet:** a wallet rail (`isWalletId`), a
    device-minted **guest** rail (`isGuestId`, `verifyGuest`, its own `guest_rate_limit` table), and
    a Google Play identity rail (`isPlayId`).
  - Sessions are wallet-scoped only — `auth_sessions.wallet TEXT NOT NULL`. No multi-wallet linkage
    exists anywhere in this table or elsewhere.
- **Client build target / distribution:** Two real build variants exist at the Unity assembly
  level. `DeNelle.GooglePlay.asmdef` carries `defineConstraints: ["GOOGLE_PLAY"]`;
  `DeNelle.Wallet.asmdef` and `DeNelle.Web3.asmdef` carry the inverse constraint
  (`!GOOGLE_PLAY`), so they are never compiled together. This means: a wallet/Web3-enabled build
  (distributed via the Solana dApp Store and/or sideload) and a separate Google-Play-only build
  with wallet/Web3 code excluded entirely. **Whether the crypto build today reaches players via
  dApp Store, sideload, or both is [UNKNOWN — verify before build]** — the asmdef constraint proves
  the code separation exists, not the current live distribution channel.

## 2. The existing clan UI (the fake one)

All confirmed present, nothing else clan-shaped found under `Assets/_Modules/`:
- `Assets/_Modules/Core/Services/ClanFeatureGate.cs`
- `Assets/_Modules/Core/Services/ClanService.cs`
- `Assets/_Modules/HUD/ClanChatPanel.cs`
- `Assets/_Modules/HUD/ClanChatPanelBootstrap.cs`
- `Assets/_Modules/HUD/ClanChatVM.cs`

**What's actually built** (`ClanService.cs:44-89`):
- **Roles:** `enum ClanRole { Leader, Officer, Member }` — three roles, but Officer is never assigned
  by any code path today; only Leader exists in practice, always the local account.
- **Invite code:** a 6-character string from a 32-character alphabet excluding `O/0/I/1`
  (`GenerateClanCode()`, `:330-341`) — generated locally, never validated against anything. **There
  is no join-by-code path at all — no `JoinClan` method exists.**
- **Chat:** a ring buffer capped at 100 messages (`MessageBufferCap = 100`, `:104`). Each message is
  `{Id, SenderId, SenderName, PhraseId, Text, SentAtUnix, IsCustom}` (`:66-76`). Two send paths:
  templated phrases via a `ChatPhraseCatalog`, and free custom text capped at 140 characters
  (`CustomTextMaxChars = 140`, `:105`) with profanity filtering **explicitly off** — the code's own
  comment at `:31-34` says this is "intentionally OFF for the stub."
- **Member list:** `List<ClanMember>` of `{Id, DisplayName, Role, JoinedAtUnix}` (`:52-58`) — always
  exactly one member (the local account, as Leader), since no join flow exists.
- No moderation, no reporting, no takedown, no leave-then-join-another-clan flow beyond
  `LeaveClan()` nulling local state.

**The gate mechanism** (`ClanFeatureGate.cs`, full file, 12 lines): a bare compile-time constant,
`public const bool PlayerFacingEnabled = false;`. Not a runtime flag, not a server-side check.
Bypass detection is a source-text lint, `Assets/Editor/Regression/ClanFeatureGateRegression.cs`
(60 lines), which requires by raw text match: (a) the constant is literally `false`, (b)
`ClanChatPanelBootstrap.cs` contains the exact string `if (!ClanFeatureGate.PlayerFacingEnabled)
return;` before its `new GameObject("ClanChatPanel")` call, and (c) `HudKitController.cs` contains a
matching gate check before its `AddDockTab(..., "Chat", ...)` call. Confirmed in the real bootstrap
file: the gate check is the literal first line of `SpawnInScene` (`ClanChatPanelBootstrap.cs:34`).

**Local data model** (`ClanService.cs:78-89`, `ClanState`):
`{Id, Code, Name, Tag, JoinPolicy, CreatedAtUnix, List<ClanMember> Members, List<ChatMessage>
Messages}`. `JoinPolicy` is a string always hardcoded to `"open"`, never read anywhere. Persisted as
one JSON blob to `PlayerPrefs` key `"dotr-clans-v1"` (account id separately at
`"dotr-account-id-v1"`), via Newtonsoft.Json. **This shape is a reasonable first-draft server-table
spec** — Clan / ClanMember / ChatMessage map fairly directly onto tables.

**WO-1265** (minted 2026-08-28, closed 2026-09-03, owner felt-test PASS) is the binding prior
readiness sequence, verbatim in spirit:
1. Server-owned clan/membership/role/invite/message persistence, keyed to **signed wallets**.
2. Preset phrases only until moderation/reporting/takedown is owned — free text stays closed.
3. Rate limits, authorization, leave/leader-succession, reconnect, a real two-wallet test.
4. A Command Center surface for readiness/health/message counts with an explicit operator launch
   control.
5. A clan leaderboard fed from real server-owned identities, never the local stub.
6. Only then flip the gate and run device + two-wallet acceptance.

Its own acceptance criteria explicitly required NOT deleting the local stub and NOT touching the
existing (unrelated) leaderboard.

## 3. The existing backend surface

- **`api/` directory layout:** `account/ admin/ auth/ catalog/ cron/ events/ game/ heartbound/
  leaderboard/ migrations/ patronage/ pi/ profile/ promo/ purchases/ referral/ showcase/
  tower-swap/ _lib/`.
- **`api/_lib/` (43 files)**, notably: `wallet-auth.js`, `skr-staking.js`, `solana-pda.js`,
  `store-sale.js`, `promo-discount.js`, `purchase-catalog.js`, `purchase-fulfilment.js`,
  `ip-budget.js`, `http.js`, the `heartbound-*` cluster (7 files), `sku-catalog.js` +
  generated JSON, `tunables.js` + generated JSON. **No `db.js`** — every file calls
  `@neondatabase/serverless` directly; there is no shared DB helper module to extend.
- **`skr-staking.js` (28,009 bytes, read in full):** Decodes a custom Anchor program's on-chain
  accounts directly via raw RPC calls (`rpcCall`, `fetchAccount`) — hand-rolled, no SDK. Key
  functions: `decodeStakeConfig`/`decodeUserStake` (byte-layout decoders), `computeActiveStakeRaw`
  (shares × share price), `deriveUserStakePda`, `verifyStake(walletAddress, opts)` (the main
  entry point — fetches and verifies a wallet's live stake state), `isUnstakingReady`/cooldown
  logic, and a caching layer (`resolveServedState`, `isCacheFresh`, `manualRefreshAllowed`) so it
  doesn't hit RPC on every request. **No epoch/tenure/history reads exist — only current-state.**
  Any Vigil work reads current staked-percentage from here; tenure is not available from this file
  and needs new game-side tracking (already resolved this session — see the SKR design doc).
- **`wallet-auth.js` full export list:** `issueSession, verifySession, renewSession,
  SESSION_TTL_SECONDS, SESSION_ABSOLUTE_TTL_SECONDS, NONCE_TTL_SECONDS, GUEST_WINDOW_SECONDS,
  GUEST_MAX_PER_WINDOW, GUEST_MAX_BODY_BYTES, WALLET_MAX_BODY_BYTES, AuthCode, WALLET_RE, GUEST_RE,
  PLAY_RE, isWalletId, isGuestId, isPlayId, isProvenValueId, guestEnabled, promoGuestRedeemEnabled,
  googleIdentityEnabled, buildSignedMessage, issueNonce, verifySignature, verifySignatureDetailed,
  consumeNonce, verifyWallet, verifyGuest, verifyAndConsume, authenticate, authenticateGranting,
  authenticatePromoRedeem`. A verified request carries a **wallet address** as identity — nothing
  richer (no display name, no persistent profile field on the session itself).
- **Rate limiting:** exists today, in `wallet-auth.js` — `touchGuestRate`/`guest_rate_limit` table,
  windowed hit-counting, explicitly **fail-open** if the table is missing ("rather than 500-ing").
  Follow this exact pattern for any new clan-specific rate limits.
- **Admin auth:** a single shared secret header, `X-Admin-Key`, compared in constant time against
  `process.env.ADMIN_DASH_KEY` (`api/admin/stats.js:15,503,523`). Not per-admin, not role-based —
  one shared key for the whole admin surface. A clan-health admin view follows this same pattern
  unless a WO explicitly decides to build something more granular.

## 4. Real-time / presence capabilities

- **No WebSocket or Server-Sent-Events support anywhere.** A direct search across `api/` for
  `websocket|server-sent|eventsource|sse\b` returns zero real hits.
- **The hosting model is fundamentally request-response.** Standard Vercel serverless functions do
  not hold a persistent connection open across invocations. Vercel does offer newer Fluid Compute
  functions with WebSocket support in some configurations, but nothing in this codebase uses that
  today — **adding real-time support is new infrastructure, not a config flip.**
  **[UNKNOWN — verify before build: whether Fluid Compute WebSockets are an acceptable path, or
  whether the constraint is "polling only."]**
- **No push notifications exist anywhere** — no FCM, no Solana-Mobile-specific push mechanism.
  Confirmed by direct search across `Assets/` and `api/` (only false-positive filename matches:
  video files, PNGs, an unrelated `api/icon.js`). **Does not exist — new work if wanted.**

**Practical implication for the WOs:** chat and "who's online" must be scoped as polling-based
(check every N seconds) unless a WO explicitly adds new real-time infrastructure as its own scoped
piece of work, not a side effect of the clan feature.

## 5. Wallet identity specifics

- **"Verified wallet" = a real ed25519 signature check**, either fresh via nonce or replayed as a
  session token bound to that wallet. Every route acting on a wallet's identity re-verifies through
  `wallet-auth.js` — it is never a merely-claimed address.
- **No standalone wallet-identity table exists.** The closest things: `auth_sessions` (session
  tokens, wallet-scoped, short-lived) and `auth_nonces` (single-use challenges) — both transient,
  not a persistent profile. `player_data.player_id` (`schema.sql:60`, comment: `-- BoundWallet
  address`) is the closest thing to a persistent wallet record, but it's a save-data row keyed by
  wallet, not an identity/metadata table. **A dedicated `wallet_identity` table with
  first-seen/tenure tracking does not exist — new work, and it's the clan system's real
  prerequisite (WO-1265's own step 1).**
- **One wallet = one player, enforced structurally.** `player_data.player_id TEXT PRIMARY KEY` is
  the wallet address itself. No linking table, no multi-wallet-per-account concept anywhere.
- **No wallet-switching/recovery code path exists.** Changing wallets today means becoming an
  entirely new `player_id` row — old progress is orphaned. **Unhandled — new work if this needs to
  be solved for clans specifically** (e.g. a player who switches wallets losing clan membership).
- **Genesis Token: confirmed absent.** A search across all of `Assets/` and `api/` for
  `genesis.token|genesistoken` returns zero hits. **Does not exist — new work**, though it is a
  real, currently-documented Solana Mobile mechanism (verified this session — see the SKR design
  doc's "collective edition" section for the citation).
- **Bonus finding:** `package.json` devDependencies already lists `"@sqds/multisig": "^2.1.4"` —
  but a search for `@sqds/multisig` usage across `api/` and `test/` returns **zero references**.
  It's an installed-but-unused dependency, likely added during earlier research. Squads
  integration is genuinely greenfield in terms of actual code, despite the package already being
  present in the manifest.

## 6. Multisig specifics (only relevant if the collective Vigil is in scope now)

- **No existing Squads integration for any player-facing feature.** There IS a real, existing
  Squads reference in the project, but it governs **company treasury wallets, not player clans**:
  `docs/wallets-of-record.md` §4 (lines 114-126) specifies each treasury wallet (SOL/USDC/SKR) must
  be a **Squads 2-of-2 multisig** (signer A = the owner's day-to-day wallet, signer B = a
  hardware-backed Seeker wallet) — status **"pending Squads multisig setup," none yet
  provisioned.** `Assets/_Modules/Core/FeatureFlags.cs:739-740` and
  `Assets/_Modules/Wallet/WalletRegistry.cs:19` both reference this same treasury plan in comments.
  This is a real, documented pattern worth citing as precedent (2-of-2, hardware-backed signer),
  but it has zero code implementation yet and no relationship to a player clan vault.
- **Squads version:** "v4" is not named anywhere in project docs or code as a committed decision —
  the v4 confirmation exists only in this session's own separate research (see
  `docs/SKR_ALANIA_ROOT_NETWORK_EXPLORATION_2026-09-17.md`, "The Vigil, collective edition"
  section), which verified v4's time-locks/spending-limits/roles against Squads' own SDK docs. That
  research is real and citable; it is not yet a project-level commitment to build against v4
  specifically.
- **Signer-set definition and vault-creation flow: confirmed not decided anywhere.**
  **[UNKNOWN — verify before build: does the game create the vault, or does a clan bring an
  existing vault address? Are the M signers the clan's leadership, all members above a role, or a
  fixed list chosen at vault creation?]** These are genuinely open design questions, not omissions
  from this research pass.

## 7. Constraints and non-negotiables

The following are **not determinable from the repository** and must come directly from the owner
before DeepSeek scopes timeline-sensitive work orders:

- **[UNKNOWN — verify before build] Timeline.** How long is the actual build window?
- **[UNKNOWN — verify before build] Who's building.** Solo, or is there a backend specialist
  separate from Unity work?
- **[UNKNOWN — verify before build] Hackathon submission requirements.** Working demo, video,
  written pitch, or all three — this determines which WOs are must-ship vs. nice-to-have.
- **Play Store variant scope — partially answerable from the repo, partially not.** The repo
  confirms wallet/Web3 code is structurally excluded from the Google Play build variant (§1 above),
  so a clan system requiring wallet identity **cannot** function in that build variant as currently
  architected. **[UNKNOWN — verify before build: does the owner want clan features gated to the
  crypto build only (the straightforward answer given the existing architecture), or is there an
  appetite to build a second, non-wallet identity path for Play Store players — a materially larger
  scope]**

## 8. The governing review — full summary of `docs/SKR_VISION_RECONCILIATION_2026-09-11.md`

Status: "review and recommendations, not approved implementation scope." Reviewed four supplied
design/pitch documents plus two local-source-review baselines. Does not certify the installed
APK, does not endorse legal assertions, does not authorize deployment.

**Overall judgment:** the strongest idea across all reviewed material is "capture a town, make it
your own, learn tactical commands through exploration, use those choices against challenging
defenses." The reviewed packet as a whole was "not ready to become a single work order" — it mixed
historical advice, competing drafts, stale defect reports, and unsupported claims about
implementation, fairness, blockchain necessity, legal treatment, and effort.

**The single most directly binding section for a clan+SKR system (§7 of the source, preserved in
full):**

> Verified local implementation includes the Night Market purchase rail, external staking
> verification, connected polish-attempt benefits, and wallet display. **It does not establish a
> real Arena escrow or token-earning/cash-out loop.** The older Arena SKR balance is a PlayerPrefs
> stub seeded to 500; current captured-town practice has no wager or payout.
>
> The pitch says there is no SKR-to-crystal conversion, yet the purchase catalog contains crystal
> packs and bundles with crystals. Say **one-way purchases may grant in-game items/currency; no
> redeemable crystal-to-SKR exchange or cash-out loop was established.** Do not erase the
> distinction between buying a pack and operating a reversible exchange.
>
> **Keep Bound Echo as an optional future identity/presentation experiment initially. Wallet
> history should not become an undefined competitive advantage. Specify wallet linking, recovery,
> multiple wallets, missing history, and stake changes before using it as persistent character
> identity.**
>
> The claims that sponsorship is "legal in every jurisdiction," that payer direction alone
> determines gambling, or that a particular conversion automatically determines securities
> treatment are unsupported by this packet. Remove them from the pitch. This review does not make a
> legal determination; proposed prize formats need jurisdiction-specific and distribution-specific
> review before commitment.
>
> Similarly, "sponsor's stake" is not a payout specification. Decide whether a future prize is paid
> from a fixed liquid budget, principal, or realized proceeds; define funding availability and
> shortfalls. A concept is not a funded season. No sponsor, return, legal status, or guaranteed
> prize is established here.
>
> Keep development funding, the studio's sponsorship fee, the winners' prize budget, and operating
> costs separate. A sponsored prize is not automatically studio revenue.

**The direct precedent question this review already asked and answered, relevant to any
clan+SKR pitch:** *"What does SKR add that players value independently of promised earnings?"*
Recommendation: **retain honest existing purchase/staking utility while testing optional identity
features later** — i.e., don't design a feature whose value proposition to the player is implicitly
"you'll earn something."

**Other standing corrections worth carrying into clan work orders:**
- Blockchain records do not, by themselves, prove fair gameplay or an honest client — reading
  public wallet history is not the same as on-chain proof of a fair result. Any competitive-reward
  design must separately define who validates the build/ruleset/result.
- A season that resets access to earned progress does not produce fair competition between
  veterans and newcomers.
- Any RNG-gated first unlock should be guaranteed, not left to chance, for the player's first
  meaningful interaction with a new system.
- "Live" claims must name a tested build/channel; feature-percentage claims must cite the actual
  formula; time/budget estimates need validation, not assertion.

**Recommended build sequence (verbatim structure):** current release → command experiment → scroll
experiment → defense-pool experiment → competitive experiment → **optional SKR expansion** (last
stage, explicitly: "Bound Echo identity and/or a separately funded prize pilot," gated on
"demonstrated player value, truthful channel-specific presentation, funding and payout design,
applicable external review, and verified end-to-end operation"). The review is explicit these are
"dependency recommendations, not a commitment to implement all of them."

**A reusable, reviewed-safe pitch draft, verbatim from the source, in case any external-facing
copy is needed:**

> Echoes of Elarion combines hero-led raids, town building, and dungeon exploration. Our current
> release work connects capturing a town with repairing, improving, and testing its defenses in
> no-wager AI practice. The next design experiment is a permanent command library: dungeon
> discoveries teach tactical options that players can use in later battles. The Solana edition has
> SKR purchase and staking integrations; a broader opponent pool, wallet-derived Echo identities,
> and sponsored competitions remain future proposals.

---

## What this packet does NOT answer (by design)

Business/scope decisions belong to the owner and the lead, not to this fact-finding pass:
- Whether the collective (multisig) Vigil is in scope for this build window at all.
- The exact perk tier thresholds (owner has already ruled: start with a simple 5-tier ladder — see
  the SKR design doc).
- Whether wallet-switching/recovery needs solving now or can be deferred.
- Whether real-time chat is worth the new infrastructure cost versus polling, given the hackathon
  timeline.

These should be resolved as explicit rulings before or during work-order drafting, not inferred
from this packet.

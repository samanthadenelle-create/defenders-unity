# WORK ORDER 1854 — Clan system, step 11: Collective Vigil — Squads multisig + Genesis Token

**Status:** READY FOR LEAD REVIEW - implemented 2026-09-17; `node --check` clean on all 7 JS files; `node --test test/*.test.js` 1174/1171 pass (tree baseline, my file excluded) -> 1224/1221 pass with it, i.e. +50 tests and +50 passes, ZERO new reds (the 2 reds are both NOT mine and both proven so: one red at baseline, one caused by another lane's live edit to `Assets/_Modules/HUD/Kit/HudKitController.cs` at 22:36); NO commit, NO deploy (lead owns both). Migration taken as **0036** because WO-1853 landed 0035 mid-lane. ⛔ THE TICKET'S OWN OPENING CLAIM WAS WRONG AND IT CHANGED THE IMPLEMENTATION: `GT2zuHVa...` is the SGT **mint AUTHORITY**, not a mint - it is a System-Program-owned keypair account, so the specified `{mint:}` filter would have matched nothing forever, silently (FLAG 1). Four items need a lead/owner ruling before this ships: the controlled-multisig hole (FLAG 4), "all members are signers" vs vote-capable only (FLAG 5), the missing rate-limit budget (FLAG 6), and whether the shared Helius key is acceptable (FLAG 8). PRIOR STATUS: READY TO IMPLEMENT

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

---

## IMPLEMENTATION RECORD (2026-09-17) — files, proof, and the flags the lead must rule on

Built as an SME lane, file-disjoint from WO-1853 (five-tier perk ballot), which **landed mid-lane**
(commits `1806d3ce4` + `4998972f7`). Disjointness re-checked against the actual commit contents, not
assumed — see "Two lanes, one tree" below.

### Files

| File | State | What it is |
|---|---|---|
| `api/migrations/20260917_0036_clan_vaults.sql` | **NEW** | `clan_vaults` + the partial unique index on `wallet_identity(sgt_mint)` that makes `sgt_reused` race-proof |
| `api/_lib/genesis-token.js` | **NEW** | the Seeker Genesis Token read: Token-2022 enumeration (V2 + paging + V1 fallback), three-legged mint verification, 60 s cache |
| `api/_lib/clan-vaults.js` | **NEW** | the Squads multisig read, the vault-PDA derivation, the signer sweep, registration, and the collective Vigil |
| `api/clan/vault/register.js` | **NEW** | `POST /api/clan/vault/register` |
| `api/_lib/wallet-auth.js` | modified | `verifyGenesisToken(sql, wallet, opts)` + `describeReuse`, exported; nothing above them changed |
| `api/_lib/clan-vigil.js` | modified | `readCollective` + `vaultToWire`; `readClanVigil` and `toWire` extended **additively** |
| `api/schema.sql` | modified | the descriptive `clan_vaults` block, matching the convention `clan_ballots`/`clan_rate_limit` follow (CRLF preserved: 2472/2472 lines) |
| `package.json` | modified | `@sqds/multisig` moved dev→prod; `@solana/web3.js` added explicitly (see "smaller decisions") |
| `test/clan-vault-genesis.test.js` | **NEW** | 50 tests, all passing (49 on the first green run, +1 from the FLAG 10 review pass) |
| `test/wallet-identity.test.js` | modified | one WO-1844 pin narrowed — it forbade exactly what this ticket was told to build |

### Test counts, measured on fresh runs

| Run | tests | pass | fail | todo |
|---|---|---|---|---|
| Baseline at lane start (HEAD `898df48cb`) | 1098 | 1096 | 1 | 1 |
| Tree baseline NOW, my new file excluded | 1174 | 1171 | 2 | 1 |
| **With this lane** | **1224** | **1221** | **2** | **1** |

`+126` between the first and last row is **not** all mine and the difference is accounted for rather
than hand-waved: my file contributes **exactly 50** (proved by running the other 71 files alone →
1174, and 1174 + 50 = 1224). The other `+76` is `test/clan-ballot.test.js`, which WO-1853 committed
while this lane was running.

**The two reds are both NOT mine, and both are proven not-mine:**
1. `⛔ no client-readable file under Assets/ carries a Heartbound tier NAME` — **red at baseline**,
   before I touched anything. Its diff is `Assets/Editor/WallTools/RaidPostAudit.cs`.
2. `clan chat is open (WO-1851) and both player entry points still consult the same gate` — went red
   **during** the lane. It asserts a regex against
   `Assets/_Modules/HUD/Kit/HudKitController.cs`, which `ls` timestamped at **22:36:42** (≈6 minutes
   before the run) with ` M ` in `git status`. It reads **no** file under `api/`; grepped to confirm.
   **Another lane is live in that file right now** — it belongs to whoever owns it, not to this
   ticket, and I did not touch it (this lane changed no `.cs` at all).

`node --check` clean on all 7 JS files, re-run after the last edit.

---

## ⛔ FLAG 1 — THE TICKET'S GENESIS TOKEN ADDRESS IS A MINT **AUTHORITY**, NOT A MINT, AND THE SPECIFIED FILTER WOULD HAVE FAILED SILENTLY FOREVER

This is the finding that changed the implementation, and it is the one the lead most needs to see.

**What the ticket says** (line 6): *"**Genesis Token mint address** — `GT2zuHVaZQYZSyQMgJPLzvkmyztfyXg2NJunqFp4p3A4`, already confirmed against official Solana Mobile docs earlier this session
(`docs/SKR_ALANIA_ROOT_NETWORK_EXPLORATION_2026-09-17.md`). Do not re-verify from scratch."*
And line 34: *"Call Helius RPC `getTokenAccountsByOwnerV2` **filtered to the SGT mint authority**"* — the
ticket contradicts itself inside 30 lines, calling the same string a mint in one place and an
authority in the other.

**Three things were checked instead of trusted (CLAUDE.md §11B: a value copied from a doc is hearsay
until re-read at source):**

1. **The cited doc contains no address at all.** Grepped. `docs/SKR_ALANIA_ROOT_NETWORK_EXPLORATION_2026-09-17.md`
   §"The Vigil, collective edition" (lines 396-455) discusses the mechanism and says only *"verification
   must check the specific mint address per wallet"*. It never names one.
2. **This project's own proofing doc says the opposite of "already confirmed".**
   `docs/CLAN_WORK_ORDERS_PROOFING_2026-09-17.md:114` — *"The Genesis Token mint authority address
   (`GT2zuHVa…` in WO-11) **was never confirmed against an authoritative source in this project's own
   research**… a wrong address here would silently make every Genesis Token check fail (or worse,
   succeed against the wrong tokens)."*
3. **The chain settles it.** `getAccountInfo(GT2zuHVa…)`, mainnet, 2026-09-17:
   `owner: 11111111111111111111111111111111` (System Program), `space: 0`, ~8.97 SOL.
   **That is a plain keypair account. It cannot be a mint.**

**Consequence if it had been built as written:** `getTokenAccountsByOwner`'s filter object accepts only
`{mint:}` or `{programId:}`. A `{mint: GT2zuHVa…}` filter matches **nothing, for every wallet, forever,
with an HTTP 200** — precisely the silent failure the proofing doc warned about. `verifyGenesisToken`
would have returned `no_sgt` for every real Seeker owner and **no clan on Earth could ever have
registered a vault** — the prize-critical mechanic, dead on arrival with no error anywhere.

**What is actually true, sourced and then verified on chain:**
Solana Mobile's own page `docs.solanamobile.com/marketing/engaging-seeker-users` (fetched 2026-09-17)
gives the recipe and the constants:
- **Mint Authority** `GT2zuHVaZQYZSyQMgJPLzvkmyztfyXg2NJunqFp4p3A4` — so the ticket's address is
  **correct**, just mislabelled by one word;
- **Metadata Address = Group Address** `GT22s89nU4iWFkNXj1Bw6uYhJJWDRPpShHt4Bk8f99Te`;
- **SGT implements Token Extensions (Token-2022)**, program `TokenzQdBNbLqP5VEhdkAS6EPFLC1PHnBqCXEpPxuEb`;
- the recipe: SIWS, then `getTokenAccountsByOwnerV2` **filtered by the Token-2022 programId**, skip
  empty accounts, then require the mint's **mintAuthority**, **MetadataPointer** and
  **TokenGroupMember** to match.

**And the work order's fourth VERIFY item ("single mint per device or a collection") resolves to
PER-DEVICE — proven from a real mint transaction, not from the prose.** Signatures on the authority →
`2ZxxArSSkEmrmMogj27n…` → `getTransaction` (jsonParsed):

```
wallet Bq1ntjWoEX19WbkxoTwpXPiRtDFbnE3MjbLnZcx3ZyNA
ATA    DJhuGfcCRejg2TdUypCwSwY6fzCTL1rKC3h8TPS5rDTy   amount 1, state FROZEN
mint   5H4VRJ378TMhhKK91fyAEN84LhnuFaooD93ojzkWW1Kp   supply 1, decimals 0
instructions: createIdempotent -> thawAccount -> transferChecked -> freezeAccount  (all Token-2022)
```
and that mint's own account carries, verbatim:
```
mintAuthority     GT2zuHVaZQYZSyQMgJPLzvkmyztfyXg2NJunqFp4p3A4
freezeAuthority   GT2zuHVaZQYZSyQMgJPLzvkmyztfyXg2NJunqFp4p3A4
metadataPointer   metadataAddress GT22s89nU4iWFkNXj1Bw6uYhJJWDRPpShHt4Bk8f99Te
tokenGroupMember  group GT22s89nU4iWFkNXj1Bw6uYhJJWDRPpShHt4Bk8f99Te, memberNumber 96167
permanentDelegate / mintCloseAuthority   both GT2zuHVa…
```
One mint per device confirms the work order's uniqueness rule is the right one: **the uniqueness check
is against the MINT, never the token account** — the ATA changes when the token moves between one
owner's own accounts, the mint does not.

**⛔ AND THE AUTHORITY CHECK ALONE IS A FORGERY VECTOR.** `InitializeMint` takes the mint authority as
a **plain pubkey argument** — it is not a signer of that instruction — so **anyone** can create a
Token-2022 mint that names `GT2zuHVa…` as its authority. The **token-group membership** is the
load-bearing leg: initializing a `TokenGroupMember` requires the **group's** update authority to sign,
so `state.group === GT22s89…` is a fact only Solana Mobile could have written. All three legs are
required in `classifyMint`, and `test/clan-vault-genesis.test.js` includes an explicit forgery case
(right authority, no group membership → refused).

**Ask of the lead:** the ticket body is left as written (it is the dated record) — this flag is the
correction, per CLAUDE.md §15. If WO-11 in `docs/SKR Integtration.md` is ever re-issued, **change
"mint address" to "mint authority" at lines 6 and 874** and it will be right.

---

## ⛔ FLAG 2 — `getTokenAccountsByOwnerV2` **IS** SUPPORTED, BUT ITS RESPONSE SHAPE HAS TWO TRAPS A V1-SHAPED READER FALLS INTO

The work order's third VERIFY item asked whether the reused Helius plan supports this Helius-specific
extension. **It does — measured, not assumed.** Against `HELIUS_RPC_URL` (host
`mainnet.helius-rpc.com`, API key in the query string; **the value is in `.env.local` only and appears
in no tracked file, no log line and no command line in this lane**):

```
getVersion                  -> HTTP 200   solana-core 4.3.0-rc.1
getTokenAccountsByOwnerV2   -> HTTP 200   real accounts, apiVersion 4.3.0-alpha.2, slot 447962077
an unknown method           -> HTTP 200   {"error":{"code":-32601,"message":"Method not found"}}
```

**So no fallback was needed.** One is built anyway (`readToken2022Accounts` → `readViaV2`, then the
standard `getTokenAccountsByOwner` on `-32601` only) because a plan change, a provider swap or an unset
`HELIUS_RPC_URL` must degrade to the standard method rather than make every Seeker look like a
non-Seeker. A **dead** endpoint deliberately does **not** fall back — one dead endpoint is dead for
both methods, and retrying doubles the load on something already failing.

**Trap 1 — `result.value` is an OBJECT, not an array.** V2 returns
`{accounts, paginationKey, count}`; V1 returns the array directly. `value.map(...)` on a V2 response is
a `TypeError`, and `Array.isArray(value)` is `false` — which under `skr-staking.readSkrBalance`'s idiom
would have become `invalid_response`, i.e. a real Seeker reported as *unreadable*. Normalised once, in
`normalizeAccounts`, which returns `null` (never `[]`) for a shape it does not recognise.

**Trap 2 — `paginationKey` IS NON-NULL ON THE LAST PAGE.** Measured: paging an owner with exactly two
token accounts at `limit: 1` returned a key on page 1 **and** on page 2; only a third page came back
empty. A `while (paginationKey)` loop therefore spends **a wasted round trip on every wallet, forever,
on a billed key** — and a buggy variant never terminates. The loop stops on a **short page**
(`accounts.length < limit`), which at the real limit of 1000 is one round trip for an ordinary wallet.
Both traps are pinned by tests, one of which uses 1001 fixture accounts so the full-page→continue,
short-page→stop path is genuinely exercised at the real page size.

---

## ⛔ FLAG 3 — `@sqds/multisig` IS **2.1.4** AND "v4" IN THE SPEC MEANS THE PROGRAM, NOT THE SDK

The first VERIFY item, done at source rather than from the spec's prose.

- `node_modules/@sqds/multisig/package.json` → `"version": "2.1.4"`, `main: lib/index.js`, CJS +
  ESM + types. **Nothing here is written against a v4 SDK shape.** The spec's "v4" is the **on-chain
  program generation** this SDK talks to: `PROGRAM_ID` printed from the package itself is
  `SQDS4ep65T869zMMBKyuUq6aD6EgTu8psMjkvj52pCf` — Squads **v4**. Both statements are true at once, which
  is why the ticket read as a contradiction.
- **The real exports**, listed off the require (not from the `.d.ts` prose): `PROGRAM_ADDRESS`,
  `PROGRAM_ID`, `accounts`, `errors`, `generated`, `getBatchTransactionPda`, `getEphemeralSignerPda`,
  `getMultisigPda`, `getProgramConfigPda`, `getProposalPda`, `getSpendingLimitPda`,
  `getTransactionPda`, `getVaultPda`, `instructions`, `rpc`, `transactions`, `types`, `utils`.
  `accounts.Multisig`'s statics: `fromArgs`, `fromAccountInfo`, `fromAccountAddress`, `gpaBuilder`,
  `deserialize`, `byteSize`, `getMinimumBalanceForRentExemption`.
- **It imports cleanly in plain Node/CJS — an actual require, not an assumption.** `require('@sqds/multisig')`
  succeeded in **~305 ms warm** (three runs: 325 / 305 / 301 ms; **4093 ms** on the first cold disk
  read). Pure JS throughout (`@metaplex-foundation/beet`, `beet-solana`, `cusper`, `@solana/spl-token`,
  `@solana/web3.js`, `bn.js`, `buffer`, `invariant`) — **no native addon**, so Vercel-serverless safe.
- **Which is exactly why it is LAZY-REQUIRED, inside the register path only.** 300 ms of module init on
  a cold lambda is a third of a second nobody should pay to *read* a clan's Vigil, and
  `GET /api/clan/vigil` never needs it: the signer list and threshold come from Postgres once
  registered. The SDK is touched only by `POST /api/clan/vault/register`.

**The read is pure-deserialize over the existing raw-RPC transport, not `fromAccountAddress`.** That
variant wants a `@solana/web3.js` `Connection` — a second HTTP stack with its own retries inside a
function that already has one — and it calls `deserialize` anyway. Proven against **three real mainnet
multisigs** (found via `getProgramAccounts` memcmp'd on `accounts.multisigDiscriminator`
`[224,116,121,186,68,161,79,236]`; **156,641** such accounts exist on mainnet):

```
JEJJhPFUoTzFsAQAoFYUEQDmFUPJjCifHfLgn3V9nYJq  231 B  threshold 2 / 3 members, masks [7,2,7]
  -> vault[0] DUQCftnDJJc4DoqyjPiZETPn9o4EBhwAGrUJoniQgg12  (bump 255)
JEJVyALtFCBgNSGAyFtEXF8w2JYUu8awD9EVMYtWLysL  528 B  threshold 2 / 3 members
  -> vault[0] 5Zbv692eNmxcPFCvGrbYnqiTUyTcqTbTZeuDprLssMNo
JEJdqYLAAT8kirhwiWYNKXDPAp2U7xT5mtVqqegpb4Dm  198 B  threshold 2 / 2 members
  -> vault[0] 8V1BmioKt28HerjpmbBsQPnYQjGATm2iXkJNmDWGVMyM
```
The byte sizes **differ** (231/528/198) because a `Multisig` account is variable-length (its member
vector), so there is deliberately **no size check** — the program owner and the **explicitly compared**
discriminator are the guards. ⚠ That last clause originally read "the discriminator and the program
owner are the guards", implying the SDK checked the discriminator for us; **it does not, and FLAG 10
below is the measurement and the fix.** `test/clan-vault-genesis.test.js` embeds the **231 real mainnet
bytes** as a base64 fixture and asserts the decode, so the layout is proven against the chain rather
than against a buffer the test wrote to suit itself.

---

## ⛔ FLAG 4 — A "CONTROLLED" SQUADS MULTISIG CAN HAVE ITS SIGNERS SWAPPED BY ONE KEY. **This is a real hole in the pitch mechanic and needs an owner ruling.**

A `Multisig` account carries a `configAuthority`. When it is the System Program
(`11111111111111111111111111111111`) the multisig is **uncontrolled** — members can only be changed by
the members themselves, through the threshold. When it is **anything else**, that single key can
`multisigAddMember` / `multisigRemoveMember` **without the members' consent**.

**Why it matters here:** registration verifies every signer's Genesis Token *at that moment* and pins
the signer list into `clan_vaults.signer_wallets`. On a controlled multisig, the holder of the config
authority can then swap the whole signer set for wallets that hold no Seeker at all — and the clan
keeps its `hardware_backed` status against a stored list that no longer matches the chain. The claim
"this collective is verifiably a group of real device owners" would be false while still reading true.

All three mainnet multisigs sampled were **uncontrolled**, so this is not the common case.

**What shipped:** registration **does not refuse** a controlled vault — whether to is a design ruling,
not an engineering one — but `configAuthority` is **read, returned in the register response and
surfaced in the code comments** so the decision can be made with the fact in view.

**Recommendation:** refuse a controlled multisig at registration (one `if`), or re-read the signer list
from the chain on each Vigil read. The second is more correct and costs one extra RPC call per clan per
read. **Your call — I did neither, deliberately, rather than pick a policy for you.**

---

## ⛔ FLAG 5 — "EVERY SIGNER" SHIPPED AS "EVERY MEMBER". A first-pass default, not a ruling.

Squads permissions are a bitmask: **1 Initiate / 2 Vote / 4 Execute**. The three real mainnet
multisigs read for this ticket carried masks `[7,2,7]`, `[7,2,7]` and `[7,6]` — so a member who can
**vote but not execute** (mask 2) and one who can **execute but never approve** (mask 4) are both real,
live configurations.

The ticket says *"Calls `verifyGenesisToken` on **every signer**"* and never defines "signer".
**Every member** is the STRICTER reading, so that is what shipped. Narrowing it to vote-capable
members only (`mask & 2`) would let a non-Seeker hold an Execute-only seat on a "hardware-backed
collective" — which is why I did not narrow it without a ruling.

The masks are **reported** by `readMultisig` and deliberately **not stored**: a stored mask is a copy of
on-chain state that Squads' own add/remove-member instructions change without telling us. The
`multisig_address` is stored instead, which makes the masks re-readable at any time and never stale.

---

## ⛔ FLAG 6 — `POST /api/clan/vault/register` SPENDS **NO** `clan_rate_limit` BUDGET. Bounded another way, but this is a gap.

I could not add a seventh budget without reaching into another lane's file:
`CLAN_RATE_LIMITS`' exact shape is pinned by an `assert.deepEqual` at
**`test/clan-roles.test.js:566`** (WO-1846's file), and `touchClanRate` answers an **undeclared** action
string by silently applying the strictest budget *and* logging itself as a programming mistake — so
passing `'vault_register'` today would be a logged defect, not a budget.

**What bounds the damage instead**, since this route is the most RPC-expensive in the clan set (one
registration verifies every signer, and one signer is up to `1 + MAX_MINT_PROBES` calls on a shared,
billed key):
- `clan-vaults.MAX_VAULT_SIGNERS = 20` — refused **before any RPC fan-out** (tested: zero
  `getTokenAccountsByOwnerV2` calls on an over-cap multisig);
- `genesis-token.MAX_MINT_PROBES = 40` — and an over-cap wallet is reported as **`too_many_mints`**,
  **never** as `no_sgt`, because we did not finish looking and saying otherwise would be a lie;
- `genesis-token.MAX_TOKEN_PAGES = 4`, with `truncated: true` so a partial list can never be read as
  a complete one;
- the 60-second per-wallet SGT cache, which **caches failures too** so an outage cannot be turned into
  a stampede by a retry loop.

**Ask:** add `vault_register` to `CLAN_RATE_LIMITS` (suggest **5/hour** — registration is a once-ever
action) and update that `deepEqual`. It is a two-line change in a file this lane was told not to touch.

---

## ⛔ FLAG 7 — I USED `beginClanRequest`, **NOT** `authenticateGranting()`. It is STRICTER, and swapping it back would WEAKEN the route.

The ticket says: *"auth `authenticateGranting()` (this binds real identity to a vault, a stronger auth
requirement than the plain clan endpoints)"*. In this codebase that phrasing **inverts**:

- `authenticateGranting()` = `authenticate()` + an **allowlist of `{wallet, google}`**
  (`wallet-auth.js:1121-1128`). For a wallet-shaped `playerId` it is **identical in strictness** to
  `authenticate()`; its extra check only refuses `guest`, and it **admits** a `play-` id.
- `beginClanRequest()` = `authenticate()` + **`auth.mode !== 'wallet'` is REFUSED**
  (`clan-http.js`, `AUTH_WALLET_REQUIRED`), because the clan tables' foreign keys point at
  `wallet_identity(wallet)` and only the wallet rail writes that table.

So `beginClanRequest` refuses a **superset** of what `authenticateGranting` refuses. Using
`authenticateGranting` here would admit a Google-Play `play-` identity that **cannot satisfy
`clan_vaults`' own FK chain** — a 23503 surfacing as a 500 instead of a clean 401. The ticket's
*intent* ("stronger than the plain clan endpoints") is honoured by the fact that it also requires
**Leader** role, which no other read does.

**Named as a knowing deviation** rather than done quietly (CLAUDE.md §11B B). If the lead wants the
literal text, the honest way to get "stronger" is to require the **signature** rail rather than a
bearer session (`verifyWallet` already returns `via: 'signature' | 'session'`) — but that needs a
change to `clan-http.js`, which WO-1853's routes also use, so it is not a file-disjoint edit tonight.

---

## ⛔ FLAG 8 — THE HELIUS KEY IS SHARED WITH AN UNRELATED PROJECT, AND IT IS NOW ON A PLAYER-TRIGGERED PATH

The ticket already flagged this ("shared with an unrelated project (a CLMM/DeFi bot) — reusing it here
shares its rate limit and billing"), and it is re-raised because **this ticket is what makes it
player-triggerable**: `genesis-token.sgtRpcUrl()` prefers `HELIUS_RPC_URL`, and the Vigil read is a
per-member fan-out. A DeFi bot and a clan roster now compete for one rate limit and one bill.

- `sgtRpcUrl()` falls back to `SOLANA_MAINNET_RPC_URL` (and the V1 path) if `HELIUS_RPC_URL` is unset,
  so a dedicated key is a **dashboard change, never a code change**.
- **`HELIUS_RPC_URL` exists in `.env.local` ONLY.** ⛔ **I have NOT proven it is set in the Vercel
  project environment** — that cannot be checked from here, and it is recorded as unproven rather than
  ticked (CLAUDE.md §11B A). **If it is missing in production, every SGT check degrades to
  `rpc_url_unset` and no vault can be registered.** That is a deploy-time item for the lead.
- The key's value appears in **no** tracked file, **no** log line and **no** command line in this lane;
  the probe scripts read `.env.local` themselves and print only the host.

---

## ⛔ FLAG 9 — ACCEPTANCE CRITERION 7 IS **NOT** A GREEN TICK, AND I WILL NOT PRETEND IT IS

Criterion 7: *"The Squads read works against mainnet, or a devnet vault… say which was used and why."*

**MAINNET, READ PATH ONLY.** The shipped code was run live against mainnet and here is the whole
output, unedited:

```
1. findSgtMint(Bq1ntjWo…)   -> {"ok":true,"mint":"5H4VRJ378TMhhKK91fyAEN84LhnuFaooD93ojzkWW1Kp",
                                "memberNumber":96167,"via":"v2","allMints":[…],"fromCache":false}
2. findSgtMint(4HQy82s9…)   -> {"ok":false,"reason":"no_sgt","detail":{"probed":1,"via":"v2"}}
3. readMultisig(JEJJhPFU…)  -> {"ok":true,"threshold":2,"members":[3FN1Mofv…,7YfU8TRg…,7jcAhEkH…],
                                "memberMasks":[7,2,7],"timeLock":0,
                                "configAuthority":"11111111111111111111111111111111","size":231}
4. vault[0] = DUQCftnDJJc4DoqyjPiZETPn9o4EBhwAGrUJoniQgg12
   status=NO_STAKE  degraded=false  percent=0  activeStakedRaw=0  totalRaw=0  slot=447969166
```
Case 2 is a genuine negative that **exercised the filter**: that wallet holds one Token-2022 account,
the mint was probed (`probed: 1`), and it was correctly rejected — it is not an empty-list pass.

**⛔ WHAT IS *NOT* PROVEN, stated plainly:**
1. **No mainnet vault has ever staked SKR, so the non-zero collective weight has never been observed
   on chain.** Line 4 above is a **real zero** (`NO_STAKE`, `degraded: false`), which is the honest
   answer for a vault that has not staked. Whether the SKR staking program will accept a **PDA** as its
   `user` — i.e. whether Squads can `invoke_signed` a stake CPI at all — **was not verified by this
   ticket.** The read path is correct either way; the day a vault does stake, the number appears with
   no code change.
2. **No mainnet multisig exists whose signers all hold Seeker Genesis Tokens**, so the full
   happy-path registration was **not** run against real addresses. It is proven offline instead, with
   the multisig bytes built by **the SDK's own serializer** (`Multisig.fromArgs(...).serialize()`) and
   served through the real RPC path — so the discriminator, the owner check and the member-vector
   decode all execute exactly as they do on mainnet.
3. No devnet vault was provisioned. Provisioning one would have needed a funded devnet Squads
   deployment and devnet SGTs, which **do not exist** (the SGT is a mainnet device artifact).

**So: the reads are mainnet-proven; the write path and the non-zero collective weight are unit-proven.
Criterion 7 is partially met and I am not ticking it.**

---

## Smaller decisions, on the record

- **Migration is `0036`, not `0035`.** WO-1853 had already written
  `api/migrations/20260917_0035_clan_ballots.sql` (seen untracked at lane start, committed at
  `1806d3ce4` mid-lane). `tools/run-migrations.mjs:280` derives its list as
  `readdirSync(dir).filter(.sql).sort()` — **filename order** — so two files claiming `0035` would make
  the applied order depend on the rest of the name. Taken by reading the directory, the way §2 says a
  WO number is taken by reading the banner.
- **Three columns are ADDITIVE to the ticket's SQL** and each earns its place:
  `multisig_address` (a vault PDA carries no data and **cannot be reversed** to its multisig, so
  without this the signer list can never be re-read from the chain); `vault_index` (Squads allows
  0..255 vaults per multisig — `getVaultPda` asserts exactly that range); `first_seen_staked_at` (see
  next bullet).
- **There is deliberately NO `hardware_backed` COLUMN.** It is derived at read time by joining
  `signer_wallets` against `wallet_identity.sgt_verified_at`, in the **same statement** as the tenure,
  so the two can never disagree and a cleared binding shows up on the next read. A stored boolean is
  the duplicated state §2/§5/§8/§16 each pay for. The derivation has a **vacuous-truth guard**:
  `0 === 0` is true, so `signerCount > 0` is required or a signer-less row would read hardware-backed.
- **The vault's tenure origin — the ticket is silent, and I had to choose.** `collectiveVigilWeight =
  percent × tenure`, but a vault PDA has no `wallet_identity` row to carry an origin. Using
  `verified_at` would credit the clan for every second between registering an **empty** vault and
  funding it. `first_seen_staked_at` is stamped on the **first positive stake observation**, mirroring
  `stampMemberVigil` and WO-1852's "tenure counts from the first observation" ruling. Idempotent by its
  `WHERE first_seen_staked_at IS NULL`, clocked by `NOW()`. ⛔ **A first-pass default, NOT a ruling.**
- **`sgt_reused` is decided by the DATABASE.** Migration 0036 adds
  `wallet_identity_sgt_mint_unique` (partial, `WHERE sgt_mint IS NOT NULL`); the write is attempted and
  a **23505 IS the refusal**, with the `SELECT` running only to *label* it — `consumeNonce`'s exact
  division of labour. A read-then-write would let two wallets verifying the same mint at the same
  instant both succeed, which is the Sybil shape `verifyGuest`'s honesty note describes, applied to a
  device instead of a player id. The post-write `SELECT` **also** catches reuse on a deployment where
  0036 has not been applied, so a missing migration degrades the **race-proofing** and not the **rule**.
- **⚠ `verifyGenesisToken` is the FIRST NON-AUTH-PATH WRITER of `wallet_identity`, and it had to be an
  UPSERT.** The ticket says *"set `sgt_mint`/`sgt_verified_at` on the current wallet's row"* — but a
  vault signer is an address the **Leader** named, who may have **never authenticated with this game**
  and therefore has **no row**. An `UPDATE` moves zero rows for them, the binding silently does not
  persist, the reuse check has nothing to compare against, and **a second clan can bind the same
  device.** So it is `INSERT … ON CONFLICT (wallet) DO UPDATE`.
  ⛔ **`api/_lib/clan-http.js`'s header states "ONLY the wallet rail ever writes that table". That
  sentence is now narrower than the truth.** I did **not** edit it — that file is shared with WO-1853's
  three routes — so it is flagged here for the lead per CLAUDE.md §15.
- **I edited another ticket's pin file, and please sanity-check it.**
  `test/wallet-identity.test.js`'s `⛔ no logic anywhere in wallet-auth.js touches the reserved Genesis
  Token columns` forbade **exactly what this ticket was told to build**. It is narrowed the same way
  WO-1852 narrowed it for `first_seen_staked_at`, and — importantly — the narrowing **strengthens** the
  remaining property rather than deleting it: the real invariant was never "these strings do not
  appear" (an absence test expires the moment the feature is built, which has now happened **twice**),
  it is **containment** — each column is referenced only from the function that owns its guards. A new
  positive assertion was added on top: `touchWalletIdentity` must contain no `sgt_` at all, because it
  runs on **every** proven wallet-rail request and an SGT probe there would put a Genesis Token RPC
  call on the auth path of every request in the game.
- **Both field spellings are emitted, from one source expression each.** This endpoint's convention is
  snake_case (WO-1852 chose it because that ticket named its fields that way); WO-1854's acceptance
  criterion is written as a literal — *"returns `hardwareBacked: true`"*. Emitting one makes a stated
  acceptance criterion false; emitting the other breaks the file's convention. Both exist
  (`hardware_backed` + `hardwareBacked`, `collective_vigil_weight` + `collectiveVigilWeight`), assigned
  from a single variable so they cannot drift — the precedent `skr-staking` set with
  `totalRaw`/`balanceRaw`.
- **The per-member object is UNTOUCHED.** `test/clan-vigil.test.js` pins its key set with a
  `deepEqual`, and that is the **correct** signal: a **signer** is not a **member** (a signer need not
  be in the clan, a member need not be a signer), so hanging vault status off a roster row would state
  a relationship that does not exist. All new fields are top-level.
- **The two weights are NEVER added together.** `vigil_weight` is what the **members** hold
  individually; `collective_vigil_weight` is what the **vault** holds jointly. Summing them would
  collapse the one distinction the whole feature exists to make — *"several individual stakes added up"*
  versus *"a genuinely collectively-owned thing"*.
- **`@solana/web3.js` was added to `dependencies` explicitly** (`^1.98.4`, the version already
  resolved), alongside moving `@sqds/multisig` dev→prod as instructed. `@sqds/multisig` depends on it
  (`^1.70.3`), but **reaching through another package's tree** is the same hazard class as an undeclared
  tag or a hardcoded repo root — it works until that package changes its own dependency, and then it
  fails at require time in production. `devDependencies` is now empty and was removed.
- **`genesis-token.js` has its own `rpcCall`, and that is not a duplicated transport for nothing.**
  `skr-staking.rpcCall` collapses **every** `payload.error` into `rpc_unavailable` and discards the
  code. This file's whole fallback decision turns on **one** code (`-32601`) surviving to the caller.
  The contract difference is the reason, and it is **asserted in a test** so a future
  "de-duplication" cannot quietly reunite them.
- **The signer sweep is sequential and short-circuiting**, where `clan-vigil`'s roster read is
  concurrent. The Vigil needs every member's number to form a sum; this needs only the **first**
  failure, because the refusal names one wallet and the Leader fixes one thing. It also means an honest
  20-signer vault is the expensive case and a hostile address list is the cheap one — the right way
  round.
- **Three signer outcomes, three codes, and they must not collapse.** `no_sgt` → 403 (the Leader's
  problem, fixable); `sgt_reused` → 403 (**also** the Leader's problem but a **different** one — telling
  them "no token" would send them hunting a phone sitting in their hand); anything else → **503**,
  because refusing a real Seeker owner when our provider blinked is the fabricated-zero mistake
  `skr-staking` exists to prevent, wearing a 403.
- **A wallet holding two SGTs picks deterministically** (candidate mints sorted, first taken, all
  reported in `allMints`). Solana Mobile notes the token can move between one owner's own accounts on a
  Seed Vault account change, so this is not absurd — and an arbitrary pick would let a retry bind a
  *different* mint, which to the uniqueness index is a *different device*.
- **`register.js` names a wallet in an error body**, which nothing else in the clan set does. It is the
  ticket's explicit instruction, and the privacy reasoning holds: the address is one the **Leader
  supplied** (by nominating that multisig) and it is returned only to that proven Leader. Every other
  refusal on the route is a plain code + ref, tested.
- **No `sgt_mint` reaches the wire, anywhere.** A mint is a **device** identifier — one per physical
  phone — so publishing it even to a clanmate hands out a stable cross-wallet fingerprint for a person.
  `vault.verified_signer_count` carries the useful fact instead. Asserted by a test that greps the
  serialised response.
- **A missing `clan_vaults` table degrades.** Code landing before migration 0036 leaves the Vigil
  endpoint fully working with `vault: null` and a flag — the deploy-order failure
  `touchClanRate`'s fail-open note describes, tested with a real 42P01.
- **An empty roster still reads the vault.** `clan_vaults` cascades on the **clan**, not on membership,
  so a clan whose last member left can still have an intact vault; returning early would report
  `hardware_backed: false` for a perfectly good one.
- **`api/schema.sql` got the descriptive `clan_vaults` block**, matching what `clan_rate_limit` and
  `clan_ballots` do — and WO-1853's own follow-up commit `4998972f7` ("the schema.sql descriptive block
  never made the first commit") is the precedent that this is the lane's job. Appended with **CRLF**,
  verified 2472/2472 lines CRLF afterwards, because that file is CRLF and
  `test/migrations.runner.test.js:486-498` records what mixed endings did to the paren-walk last time.
- **The LEGAL / COPY GATE is honoured and now GUARDED BY A TEST.** Exactly one player-facing string
  ships — `'The ancestors remember what you built together.'`, the work order's line verbatim. A test
  asserts there is exactly **one** `message:` literal in the route and scans every string literal in
  all three feature files for `investment / securities / security / yield / profit / APY / dividend /
  guaranteed`. **The guard was red-proved**: injecting `'a guaranteed yield on your investment'` made it
  fail with the right message, and it was reverted. The scan strips comments first — the comments are
  where the rule itself is written down, so a naive whole-file grep would fail on its own explanation
  and then be "fixed" by deleting the explanation. **No other player-facing copy was written.** Any
  UI copy for this feature needs the owner's explicit review first.

---

## Two lanes, one tree — the disjointness check, done rather than assumed

WO-1853 committed **during** this lane (`f711eb60e`, `1806d3ce4`, `4998972f7`). `git diff --name-only 898df48cb..HEAD`:

```
WorkOrders/WORK_ORDER_1853_five_tier_perk_ballot.md      api/_lib/clan-ballot.js
WorkOrders/WORK_ORDER_1857_...localization_sweep.md      api/clan/ballot/{current,propose,vote}.js
docs/localization/SWEEP_CLASSIFICATION_2026-09-17.md     api/migrations/20260917_0035_clan_ballots.sql
test/clan-ballot.test.js                                 api/schema.sql
```
**Zero overlap with this lane's file list**, and the migration numbers `0035`/`0036` do not collide.
The one shared file is `api/schema.sql` — appended **after** their commit landed, with their block
intact above mine and `git status` showing it clean beforehand.

---

## What was NOT executed, stated plainly

- **No commit, no push, no deploy.** The lead is sole committer (CLAUDE.md §2/§11).
- **The migration was NOT applied to any database.** `clan_vaults` and
  `wallet_identity_sgt_mint_unique` exist only as a file. Nothing in this ticket can work in production
  until `tools/run-migrations.mjs` runs.
- **`npm install` was NOT run.** `node_modules/@sqds/multisig` and `node_modules/@solana/web3.js` were
  already present (which is how the version and the import were verified); the `package.json` move is a
  declaration change that the next install/deploy realises.
- **No Unity work, no `.cs` touched, no UI.** There is no client for this yet — the endpoints exist and
  nothing calls them.
- **`R2_PARITY_OK` / `COMPILE_GATE_OK` / `REGRESSION_OK` were not run.** This lane is `api/` JavaScript
  only; those gates cover the Unity tree and content bundles.
- **The live Vercel environment was not inspected** (see FLAG 8): I cannot prove `HELIUS_RPC_URL` is
  set there, and I have not claimed it.

---

## ⛔ FLAG 10 — A LATE REVIEW PASS FOUND A REAL DEFECT: `Multisig.deserialize` DOES **NOT** CHECK THE ANCHOR DISCRIMINATOR

Found and fixed after the first green run, and recorded because the fix changed shipped behaviour and
because the original reasoning was **wrong in a way that read as careful**.

**What I had written and believed:** `readMultisig`'s guards were "the program owner and the
discriminator, the latter checked inside `Multisig.deserialize`" — the same division
`skr-staking.requireDiscriminator` makes. That sentence was an **inference about a library**, which
CLAUDE.md §12 says locates a candidate and never concludes.

**What is actually true, measured 2026-09-17.** A 200-byte buffer carrying
`accounts.proposalDiscriminator` and zeroes elsewhere **deserialized successfully** and returned a
`Multisig` with `threshold: 0` and an empty member vector. `@sqds/multisig`'s beet-generated reader
treats the eight discriminator bytes as just another struct field and **never compares them**.

**Why it mattered.** `readMultisig`'s owner check confirms the account belongs to
`SQDS4ep65T869zMMBKyuUq6aD6EgTu8psMjkvj52pCf` — but Squads owns **several account types** (`Proposal`,
`VaultTransaction`, `SpendingLimit`, `ProgramConfig`, `Batch`, `TransactionBuffer`). Any one of them at
the address a Leader supplied would have been decoded **as a multisig**, with its `threshold` and its
`signer_wallets` being whatever those bytes happen to mean — and then written into `clan_vaults`. The
`Array.isArray(ms.members)` guard below would have caught *some* of those by accident (an absent or
empty vector), which is precisely what made the hole hard to see: it looked guarded because the common
cases happened to be refused. A `Proposal` with real data in the right offsets is the case accident
does not cover.

**The fix:** `api/_lib/clan-vaults.js readMultisig` now compares the first 8 bytes against
`squads.accounts.multisigDiscriminator` **before** deserializing, exactly as
`skr-staking.requireDiscriminator` does for `UserStake` / `StakeConfig`. `test/clan-vault-genesis.test.js`
carries the Proposal-discriminated fixture as its regression, and it **red-proved the defect** before
the fix (it failed with `ok: true`).

**The lesson for the record, because it is the same one twice in one ticket:** FLAG 1 was a value
trusted from a doc; this was a behaviour trusted from a library's reputation. Both were cheap to check
and both were wrong.

---

## Three further review corrections, all pinned by tests

1. **`register.js` no longer claims `hardware_backed: true` on a read-back failure.** It answered
   `row ? row.hardwareBacked : true`. The claim was *true today* — registration refuses any signer
   without a verified SGT, so every row it writes is hardware-backed the instant it is written — which
   is exactly what made it the dangerous kind of shortcut: correct now, a lie the first time the rule
   changes, and the **only** place in the feature where the status was asserted rather than derived
   (contradicting this ticket's own "never stored, always derived" invariant). It now answers `null`
   for "not derived on this response"; `/api/clan/vigil` always derives it.

2. **The write-before-read ORDER is now pinned.** `verifyGenesisToken` attempts the upsert and lets the
   unique index refuse it, reading only to *label* the refusal. Nothing asserted that order, so a
   refactor to read-then-write would have passed every other test in the file while re-opening the
   exact race migration 0036 exists to close. The happy-path test now asserts
   `indexOf(write) < indexOf(select)` over the recorded call log.

3. **`signer_wallets` being an array of plain STRINGS is now pinned.** The migration's CHECK can only
   say `jsonb_typeof = 'array'`. `readClanVaultRow` derives `hardware_backed` with
   `jsonb_array_elements_text(signer_wallets) JOIN wallet_identity ON wi.wallet = s.w` — so storing
   objects like `{wallet, mask}` would make that join match **nothing** and every clan would silently
   read `hardware_backed: false`, forever, with no error anywhere. Asserted at the insert.

4. **The test harness now CLEARS `HELIUS_RPC_URL` / `SOLANA_MAINNET_RPC_URL` rather than assuming they
   are unset.** A developer or CI job with `.env.local` sourced would otherwise have sent every
   no-rpcUrl test at the **real, shared, billed** Helius key on every run (FLAG 8). "Zero mainnet at
   test time" is now enforced by the harness instead of hoped for, and `sgtRpcUrl() === null` is
   asserted as a precondition where it matters.

**Final counts after these changes:** `test/clan-vault-genesis.test.js` **50/50**; `node --check` clean
on all 7 JS files.

**`package-lock.json` exists in the repo root and is UNCHANGED** — `npm install` was not run, so the
lockfile does not yet reflect `@sqds/multisig` moving dev→prod or `@solana/web3.js` being declared. The
lead's install/deploy step updates it.

**`BOARD.html` was NOT regenerated.** Per the cadence rule the lane flips the WO's own `**Status:**`
line (done) and the lead regenerates the board and commits the flip in the same commit as the work.
`python tools/board_build.py` rewrites shared state and another lane is live in this tree.

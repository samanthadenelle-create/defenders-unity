# WORK ORDER 1674 — HEART-001: Native SKR staking verification layer (backend-authoritative)

**Status:** SPEC
**Silo:** Backend (`api/`, Neon, Vercel) + one Unity read-only client seam. No gameplay, no economy, no scene files.
**Raised by:** HEARTBOUND-TRIAGE lane, 2026-09-10, from the owner's 13-part spec.
**Number:** PRE-ASSIGNED by the coordinator. `CLI_LANES_WO_NUMBERS.md` deliberately **NOT** edited by this lane.
**Spec section:** `docs/specs/HEARTBOUND_SKR_RESONANCE_WORK_ORDERS_2026-09-10.md:31-182` (HEART-001; the twelve PRODUCT RULES at `:12-29`).
**Tree these line numbers were read against:** worktree at `dev` **`abbeb9362`** (merged `--ff-only` at the start of this lane, 2026-09-10). A line number copied from here is hearsay until re-opened (CLAUDE.md §11B).

---

## 0. Classification — **EXISTING, AND IT IS ON THE WRONG SIDE OF PRODUCT RULE 7**

⛔ **This is not greenfield. The exact chain read the spec describes already ships — in the Unity client.**

`Assets/_Modules/Wallet/NativeSkrStakeQuery.cs` implements, client-side, precisely HEART-001's algorithm:

| HEART-001 asks for | Already in the client | Line |
|---|---|---|
| Staking program id `SKRskrmtL83pcL4YqLWt6iPefDqwXQWHSw9S9vz94BZ` | identical constant | `NativeSkrStakeQuery.cs:25` |
| StakeConfig account | `4HQy82s9CHTv1GsYKnANHMiHfhcqesYkK6sB3RDSYyqw` | `:26` |
| Guardian pool | `DPJ58trLsF9yPrBa2pk6UaRkvqW8hWUYjawe788WBuqr` | `:27` |
| UserStake PDA | seeds `["user_stake", config, user, guardian]` | `:52-54` |
| `user shares × current share price` (not the deposit) | `shares * sharePrice / SharePriceScale` | `:75` |
| SKR 6 decimals | `SkrBaseUnits = 1_000_000L` | `:29` |
| Mainnet explicitly | `WalletEndpoints.MainnetRpcUrl` | `:57` |
| Anchor discriminator safety | `RequireDiscriminator` | `:106-113` |

**So HEART-001 is a RELOCATION, not an invention:** move this read to the backend and make the backend the only thing anything of value reads. Three concrete defects in the existing client copy, each of which HEART-001's own acceptance criteria already name:

1. ⛔ **It is client-side, therefore client-trusted.** Product rule 7 says *"The Unity client must never be trusted to report the amount of SKR staked."* Today the value is computed on the device and consumed by `NativeSkrPolishBonus` (`Assets/_Modules/Core/Catalog/PolishBonusProvider.cs:70-78`) via `StakeRewardsResolver.Resolve()` (`Assets/_Modules/Core/Platform/StakeRewardsResolver.cs:194-201`). A patched APK can install its own `IStakeQuery` (the seam is a public settable property, `StakeRewardsResolver.cs:175-183`) and report any number. **Today that only buys polish ATTEMPTS, which is why it was tolerable. HEART-005 onwards makes it buy resources, so it stops being tolerable.**
2. ⛔ **An RPC failure turns the stake into ZERO.** `NativeSkrStakeQuery.cs:87-94` — `catch { _known = false; _activeStake = 0; }`. HEART-001's acceptance says *"RPC failure does not incorrectly turn stake into zero"* (spec `:175`) and product rule 11 says the feature must keep working through an outage. **The current behaviour is the exact failure the spec forbids.** (It is *correct* for the attempts grant — fail-closed on a perk — and *wrong* for a streak.)
3. ⚠ **Precision is lost before the value is used.** `:76` divides to WHOLE SKR (`rawTokens / SkrBaseUnits`) inside a `BigInteger` and hands out a `long`. Spec `:178`: *"Values are calculated using integer/raw values before display conversion."* The snapshot must carry `sharesRaw` / `sharePriceRaw` / raw base units and convert only at the display edge.

**There is no unstaking read at all.** `grep -n "unstak"` over `NativeSkrStakeQuery.cs` returns nothing; the spec requires `unstakingSkr`, `unstakingReady`, cooldown state (`:98-106`). That half is genuinely NEW.

---

## 1. The backend as it actually is (read at source 2026-09-10; do not re-derive, do re-verify)

### 1a. ⭐ The player id **IS** the wallet — there is no linkage to build (on the Seeker rail)

`api/_lib/wallet-auth.js:129` — `WALLET_RE = /^[1-9A-HJ-NP-Za-km-z]{32,44}$/`. `api/schema.sql:60` — `player_id TEXT PRIMARY KEY, -- BoundWallet address`.

**"Server resolves wallet from authenticated player identity" (spec `:168`) is already true and costs nothing**: for a wallet-rail player the authenticated player id *is* the base58 address, proven by an ed25519 signature over `dotr-save:v1:<wallet>:<nonce>:<sha256hex(payload)>` (`api/_lib/wallet-auth.js:238-246`) with a single-use 5-minute nonce that is atomically burned (`:497`).

⛔ **But the other two rails have NO wallet and no way to attach one.** `guest-local-<64hex>` (`:132`) and `play-<64hex>` (`:150`) players cannot be Heartbound at all, and **a wallet-linking table and route DO NOT EXIST** (`grep -n "link" api/schema.sql` → only `social_links`, `api/schema.sql:902`). See OWNER QUESTION Q3.

### 1b. The granting gate already exists and already refuses guests

`authenticateGranting()` — `api/_lib/wallet-auth.js:829`. The file states the rule at `:42-44`: *"ROUTES THAT GRANT VALUE CALL `authenticateGranting()`, NOT `authenticate()`."* **Every Heartbound route that pays anything calls this one.** `GET /heartbound/status` (spec `:164-168`) is a read and may call `authenticate()`.

### 1c. Solana RPC on the backend — precedent exists, but not for a BALANCE read

Two server-side RPC consumers, both hand-rolled `fetch` JSON-RPC (**`@solana/web3.js` is NOT a dependency**, `package.json:12-22`):
- `api/tower-swap/log.js:70-127` — `getTransaction`, `commitment: 'confirmed'`, failure codes `rpc_unavailable` / `signature_not_found` / `transaction_failed` / `wallet_did_not_sign`. **This is the closest existing shape and its failure taxonomy maps almost 1:1 onto the spec's `VerificationStatus` enum (`:145-153`).**
- `api/purchases/verify.js:54-97` — `getTransaction`, `commitment: 'finalized'`, `encoding: 'jsonParsed'`, uses `tx.blockTime` as the honest clock (`:92-94`).

⛔ **Neither reads an ACCOUNT.** `grep -rn "getAccountInfo\|getTokenAccountsByOwner\|getTokenAccountBalance" api/` → **zero hits**. HEART-001 needs `getAccountInfo` on the StakeConfig PDA and the UserStake PDA, plus base64 Anchor deserialization and a PDA derivation — **none of which has a backend precedent.** The client file above is the working reference implementation; port it, do not re-derive it.

⚠ **THE DECIMALS TRAP, ALREADY PAID FOR ONCE.** `api/_lib/purchase-catalog.js:57` splits SKR decimals per network — `{ devnet: 9, 'mainnet-beta': 6 }` — and `:37-48` records a **1000× overcharge scar** from getting it wrong. The spec says 6 (`:96`). Mainnet is the only network HEART-001 uses (spec `:181`); do not inherit the devnet 9.

### 1d. Config lives where, exactly

Spec `:78`: *"Relevant program configuration must live in configuration/constants and not be duplicated throughout gameplay code."* Today the three addresses are **hardcoded in the Unity client** (`NativeSkrStakeQuery.cs:25-27`) and the mainnet SKR mint is **hardcoded in the backend** (`api/_lib/purchase-catalog.js:31`, `SKRbvo6Gf7GondiT3BbTfuRDPqLWei4j2Qy2NPGZhW3` — identical to the spec's `:92`). After HEART-001 the backend copy is the authority and the client's three constants must either be deleted with the client read (Q1) or be demoted to a comment pointing at the backend.

### 1e. Cache — **nothing to reuse; it is greenfield**

`grep -rn "@vercel/kv\|upstash\|edge-config\|redis" api/` → **zero hits**. The only in-repo precedent for a server-side cache is a module-scope, per-warm-instance `let jwksCache = {...}` (`api/_lib/google-identity.js:64-68`), which does **not** survive across instances and therefore cannot implement the spec's 5-minute cache or the 60-second manual-refresh cooldown honestly. The honest options are a Neon column (`verified_at_utc` on the snapshot row, which the spec's model already carries) or a new dependency. **The snapshot table is the cache** — say so explicitly rather than adding KV. The 60-second manual-refresh cooldown is a per-player column, not a rate-limiter, but the IP-budget helper `reserveIpBudget(sql, ipHash, scope, {...})` (`api/_lib/ip-budget.js:70`) exists if a second layer is wanted.

### 1f. Response / error / migration conventions to follow verbatim

- CORS: `applyCors(req, res, 'GET, OPTIONS')` as line 1 (`api/_lib/http.js:73-83`). **Any new custom request header must be added to the allowlist at `api/_lib/http.js:39-56` or the browser preflight kills it silently.**
- Success: `{ ok: true, success: true, …, ref, serverNowMs }` — `api/game/save.js:733-755`. `serverNowMs` is the authoritative-clock handshake and HEART-010's "clock manipulation has no effect" (spec `:1005-1011`) rides on it.
- Failure: `quietFail(res, status, code, ref)` → `{ ok:false, code, ref }` and nothing else (`api/_lib/http.js:106-108`); full context to `analytics_events` via `logApiEvent` (`api/_lib/audit.js:112`).
- Body parsing disabled after the handler assignment (`api/purchases/google-play-binding.js:45`; scar documented `api/_lib/http.js:150-161`).
- New table: one file `api/migrations/20260910_00NN_*.sql` (the runner derives the list from disk, `tools/run-migrations.mjs:26-28`, ledger `schema_migrations` `:107`, marker `MIGRATIONS_OK applied=N skipped=M` `:37`), **plus** a description block in `api/schema.sql` — that file is a *description*, not what is applied (`api/schema.sql:105-107`).

---

## 2. Deliverables

### D1 — `api/_lib/skr-staking.js`: the verifier

Port `NativeSkrStakeQuery.cs:42-113` to Node. Same PDA seeds, same discriminators, same `shares × sharePrice / 1e9` arithmetic — **but in `BigInt`, keeping raw base units end to end** (spec `:178`). Program/config/guardian addresses and `SKR_DECIMALS = 6` as module constants beside the existing mint constant's home (`api/_lib/purchase-catalog.js:31`), so there is exactly one authority per address.

Must additionally read what the client never did: `unstakingSkr`, `unstakingReady`, cooldown state, `sourceSlot` (spec `:98-106`). ⚠ **The UserStake account's unstaking field offsets are NOT in this repo.** The client only deserializes two `u128`s at offsets 137 and 105 (`NativeSkrStakeQuery.cs:65,74`) with the byte budget spelled out in comments (`:64`, `:73`). The layout for the remaining fields is an **external fact from the Solana Mobile IDL** and must be read there and cited in the RESULT — not guessed. This is the single largest unknown in HEART-001.

### D2 — `SkrStakeSnapshot` persistence

New table, fields per spec `:110-124`. Two structural requirements the spec implies but does not state:
- The row is keyed by `player_id` (= the wallet on the Seeker rail), and **it is also the cache** (§1e).
- `verification_status` is a `CHECK`-constrained enum over exactly the seven states at spec `:145-153`, following the `rail`/`network` CHECK precedent at `api/schema.sql:1138,1146-1149`.

### D3 — `GET /api/heartbound/status`

`authenticate()` (a read), `applyCors`, quiet-fail codes, `serverNowMs`. Serves the cached snapshot; refreshes when stale per spec `:155-160`. Refresh triggers: login, screen open on stale cache, wallet-link change, background processing, manual request (60 s cooldown).

⛔ **The route must not accept a stake amount, a wallet, or a tier in the request body under any name.** The spec calls this out explicitly (`:162-168`), and a regression must assert it (HEART-012 / WO-1685).

### D4 — RETIRE or RE-POINT the client-side read

**This is the deliverable that makes HEART-001 satisfy product rule 7, and it is blocked on OWNER QUESTION Q1.** Either:
- (a) `NativeSkrStakeQuery` is deleted and `StakeRewardsResolver.Query` is fed by a thin `IStakeQuery` that reads the backend snapshot — one read, one authority; or
- (b) it stays for the attempts-only perk and the backend snapshot is a second, parallel read — **two staking reads, one trusted and one not**, which is the duplicated-state failure CLAUDE.md §2/§5/§8/§16 each describe in their own words.

### D5 — ⛔ Build `StakingComplianceRegression`, WHICH DOES NOT EXIST

`grep -rn "StakingComplianceRegression" .` returns **exactly three hits and all three are claims that it protects something**: `Assets/_Modules/Core/FeatureFlags.cs:1008` (*"Pinned by StakingComplianceRegression"*), `WorkOrders/WORK_ORDER_1255_payment_provider_seam_google_play_rail.md:95`, `WorkOrders/WORK_ORDER_1673_google_play_probabilistic_item_disclosures_korea_brazil.md:138`. **There is no definition anywhere.**

This is the **second** occurrence of the identical failure WO-1673 §0 item 1 found (`JewelPolishRegression`, also imaginary, also cited by the code it was supposed to protect) and the **third** counting `skr_staking.json`'s claim of a "SkrStakingRegression" that WO-1673 records as never having existed. **A comment is not a firewall.** Build it, or delete the three claims — do not leave a fourth citation.

What actually pins the Play boundary today, and should be cited instead of the imaginary suite:
- `Assets/Editor/AndroidBuild.cs:237-238` stamps `GOOGLE_PLAY` **or** `DAPP_STORE` and forbids the other;
- `Assets/Editor/Regression/GooglePlayPackagingRegression.cs:40-41` asserts those exact stamp lines;
- `Assets/Editor/Regression/GooglePlayPackagingGate.cs:191` forbids `DAPP_STORE` / `GOOGLE_PLAY` / `SOLANA_SDK` as *persistent* defines;
- `Assets/_Modules/Wallet/DeNelle.Wallet.asmdef:21-23` — `"defineConstraints": ["!GOOGLE_PLAY"]`. **The whole Wallet assembly, `NativeSkrStakeQuery` included, does not compile into a Play build.** That is a stronger guarantee than any flag and is the real answer to "is it in the shipped artifact".

---

## 3. Acceptance (copied from spec `:172-181`, with the repo's evidence rules attached)

1. Player with no SKR stake returns `NO_STAKE`.
2. Player with SKR stake returns the calculated active stake.
3. Unstaking amount is **not** counted as actively resonating SKR.
4. **RPC failure does not incorrectly turn stake into zero** — and the RESULT must show the captured response proving it, because the current client behaviour is the opposite (§0 item 2).
5. Client cannot modify the staking value. Proven by a request that *tries* (`{"stakedSkr":500000}`) and is ignored, quoted in the RESULT.
6. Reconnecting the same authenticated wallet does not create duplicate Heartbound accounts.
7. Values calculated using integer/raw values before display conversion.
8. Mainnet is explicitly used.
9. `MIGRATIONS_OK applied=N skipped=M` on a fresh run, judged by the **marker**, then verified by a shape query (`api/admin/schema-shape.js`) — memory `idempotent-ddl-hides-a-stale-table`.
10. If D4 touched `.cs`: `COMPILE_GATE_OK`, `python tools/gate_brace.py` clean, zero NUL bytes.
11. If D5 landed: `REGRESSION_OK <n>/<n> suites` on a **fresh** log, and the suite **proven RED before green**.
12. `**Status:**` flipped in this file in the same commit as the work; `.RESULT.md` written; both paths reported.

---

## 4. Dependencies

- **Blocks:** WO-1675 (HEART-002), WO-1676 (HEART-003), WO-1677 (HEART-004) — the whole feature. Wave 1 of the spec's own order (`:1237-1245`).
- **Blocked by:** OWNER QUESTIONS Q1, Q2, Q3 below. Q1 in particular changes what D4 *is*.
- **External unknown:** the UserStake unstaking-field layout (D1). Not in this repo.

---

## 5. ⛔ OWNER QUESTIONS — this WO is SPEC, not READY, because of these

### Q1. There are about to be TWO native-SKR stake reads. Which one survives?
`NativeSkrStakeQuery` (client, DeNelle.Wallet, `!GOOGLE_PLAY`) feeds the Jeweler attempts perk today. HEART-001 adds a backend read that must be the sole authority for anything of value. **Options:** (a) retire the client read and feed `StakeRewardsResolver` from the backend snapshot — one authority, but it makes an offline player's attempts perk depend on the network; (b) keep both — the client one for attempts only, the backend one for Heartbound, and write down *why* two exist; (c) keep both and let the client one drift. **(c) is how every scar in CLAUDE.md started.** Owner's call, and it decides D4.

### Q2. Fail-closed or fail-last-known, and does the answer differ per consumer?
The client read **fails closed** (stake → 0 on any RPC error, `NativeSkrStakeQuery.cs:87-94`) and that is right for a perk. HEART-001 requires **fail-last-known** (`RPC_UNAVAILABLE` + `STALE`, never zero) and that is right for a streak. If Q1 resolves to (a), the same read must do both, and something has to choose. Recommend: the snapshot carries the status, and each consumer decides — the attempts perk treats `RPC_UNAVAILABLE` as no-stake, Heartbound treats it as last-known-within-grace. **Needs a ruling, because it is a fairness decision, not an engineering one.**

### Q3. Google Play and guest players have no wallet and no way to get one. Confirm they are simply out of scope?
`api/_lib/wallet-auth.js:27-29` records the standing ruling: *"THE WALLET REMAINS THE SOLE IDENTITY ON THE SEEKER/APK ARTIFACT."* A `play-` or `guest-local-` player therefore can never be Heartbound. That is **consistent** with product rule 12 (playable without SKR) and with the Play compliance posture — but it means Heartbound is a Seeker-artifact feature only, and the spec never says so. Confirm, so HEART-008's "No Stake State" copy is written for the right audience (and so we know whether the panel should exist on Play at all — see WO-1681 Q).

### Q4. `StakingComplianceRegression` has been cited as protection in three documents for weeks and has never existed. Build it under this WO, or a separate one?
It is a five-minute source-lint at most (the pins that *do* exist are listed in D5) and leaving a fourth citation of an imaginary gate is worse than either. Raised here because HEART-001 is the first ticket that makes the boundary load-bearing rather than theoretical.

---

## 6. What NOT to touch

- ⛔ **`api/_lib/wallet-auth.js`'s verification.** The wallet rail's ed25519 + burned-nonce path is the real-value rail and its own header says it is *"UNCHANGED and UNWEAKENED"* by every rail added since. Add a caller, never a branch.
- ⛔ **`authenticateGranting`'s guest refusal.** `api/_lib/wallet-auth.js:829`. The single sanctioned exception is `authenticatePromoRedeem` (`:915`), named so it cannot spread by being the default. Heartbound is not a second exception.
- ⛔ **`PolishBonusProvider`'s attempts-only interface.** `Assets/_Modules/Core/Catalog/PolishBonusProvider.cs:20-23`: *"There is deliberately no member for odds, weights, luck, tier bias or a bonus table."* Whatever Q1 rules, the interface shape does not change.
- ⛔ **The `DAPP_STORE` / `GOOGLE_PLAY` stamps** in `AndroidBuild.cs:237-238` and the `!GOOGLE_PLAY` constraint in `DeNelle.Wallet.asmdef:21-23`. Widening either is a compliance decision.
- ⛔ **`api/game/save.js`'s sanity ceilings.** They are anti-grief bounds on a client-owned blob (`:70-72`), not an economy. Heartbound value must not travel through the save (see WO-1675 §0).
- Do not add `@solana/web3.js`. Two existing routes do JSON-RPC with `fetch`; a 200 kB SDK for two `getAccountInfo` calls is not the trade.
- Do not add a KV/Redis dependency for the 5-minute cache. The snapshot row is the cache (§1e).

---

## 7. Evidence index (all opened 2026-09-10 in the worktree at `dev` `abbeb9362`)

- `Assets/_Modules/Wallet/NativeSkrStakeQuery.cs` — `:25-31` constants + discriminators, `:52-54` PDA seeds, `:57` mainnet, `:65,74-77` the share math, `:87-94` fail-closed-to-zero, `:137-157` the 300 s driver poll
- `Assets/_Modules/Wallet/DeNelle.Wallet.asmdef:21-23` — `"defineConstraints": ["!GOOGLE_PLAY"]`
- `Assets/_Modules/Core/Platform/StakeRewardsResolver.cs:1-24` non-custodial doctrine, `:140-146` `IStakeQuery`, `:170` the default `UnavailableStakeQuery`, `:175-183` the settable seam, `:194-201` `Resolve()`
- `Assets/_Modules/Core/Catalog/PolishBonusProvider.cs:7-10` "staking buys ATTEMPTS, never OUTCOMES", `:70-78` the live consumer, `:113` the flag gate, `:166-170` the bootstrap
- `Assets/_Modules/Core/FeatureFlags.cs:1006-1018` — the `DAPP_STORE` doctrine and the flag; `:1008` the imaginary-regression citation
- `Assets/Editor/AndroidBuild.cs:237-238`; `Assets/Editor/Regression/GooglePlayPackagingRegression.cs:40-41`; `Assets/Editor/Regression/GooglePlayPackagingGate.cs:191`
- `api/_lib/wallet-auth.js:27-29,42-44,129,132,150,167,238-246,497,829,915`
- `api/schema.sql:59-84` (`player_data`), `:766-768` (`uq_tower_swaps_tx_sig`), `:1127` (`tx_signature UNIQUE`), `:1138,1146-1149` (CHECK precedent), `:902` (`social_links` — the only "link" in the schema)
- `api/tower-swap/log.js:70-127`; `api/purchases/verify.js:54-97`; `api/_lib/purchase-catalog.js:31,37-48,57`
- `api/_lib/http.js:39-56,73-83,102,106-108,150-161`; `api/_lib/ip-budget.js:70`; `api/_lib/audit.js:112`
- `api/game/save.js:70-72,409-421,733-755`; `tools/run-migrations.mjs:26-28,37,107`
- `package.json:12-22` — no `@solana/web3.js`, no `firebase-admin`, no KV
- `vercel.json:6-9` — exactly two crons
- **`grep -rn "StakingComplianceRegression" .` → 3 hits, all of them claims. The proof for D5.**
- **`grep -rn "getAccountInfo\|getTokenAccountsByOwner" api/` → 0 hits. The proof for D1's "no backend precedent".**

# WORK ORDER 1674 — RESULT (HEART-001: native SKR staking verification layer)

**Status:** DONE — implemented, offline-tested, live-chain-verified. **NOT gated, NOT committed, NOT deployed** (no Unity on this lane; the lead gates and commits).
**Lane:** HEART-001, isolated worktree `.claude/worktrees/agent-a6426a2948a092b81`.
**Tree:** `git merge --ff-only refs/heads/dev` → **`c96030b5c374f706ddf6af215bce7f9793b90977`** (at or past the required sha).
**Date:** 2026-09-10.

---

## 0. ⛔ PROVENANCE OF THE RULING — READ THIS FIRST

The lane brief states the owner ruled at 13:10: **backend only; RPC outage = last-known verified
state with a bounded grace window; Seeker only.** I implemented to that.

⚠ **That ruling section is NOT in the WO file on `dev`.** `WorkOrders/WORK_ORDER_1674_*.md` at
`c96030b5c` ends at section 7 (Evidence index); `grep -n -i "RULED\|RULING\|13:10"` finds only the
pre-existing Q2/Q3 prose. So **the ruling text I worked from is the lane brief's summary of it, not a
document I read at source** (CLAUDE.md §11B — an unproven thing named as unproven is useful; stated as
fact it is a lie). If the owner's actual wording differs on any point, the three places it lands are
`api/_lib/skr-staking.js` (`resolveServedState`), `VerifiedStakeSnapshot.IsRewardBearing`, and the
`#if DAPP_STORE` gate on `HeartboundStatusClient`.

Mapped onto the WO's own owner questions: **Q1 = (a)** backend read is the sole authority, client read
demoted to display; **Q2 = fail-last-known**, bounded, with the client read left fail-closed because
that is still correct for its (display + one-off perk) purpose; **Q3 = confirmed Seeker-only**;
**Q4 = built here** (D5).

---

## 1. ⭐ THE HEADLINE FINDING — THE "SINGLE LARGEST UNKNOWN" IS CLOSED, WITH A PRIMARY SOURCE

WO-1674 D1: *"The UserStake account's unstaking field offsets are NOT in this repo… an external fact
from the Solana Mobile IDL and must be read there and cited — not guessed. This is the single largest
unknown in HEART-001."* The triage carried it as unproven.

**It is now proven.** The staking program publishes its Anchor IDL **on chain**:

```
base   = findProgramAddress([], programId)
idlPda = sha256(base || "anchor:idl" || programId)
       = 4aAEUKCcju9iAEAgdeaNz4RC7sCPv63q5g714nw4QY68
```

That account exists (8586 bytes, owner `SKRskrmtL83pcL4YqLWt6iPefDqwXQWHSw9S9vz94BZ`); bytes 44.. are
a zlib blob inflating to 20854 bytes of JSON beginning
`{"address":"SKRskrmtL83pcL4YqLWt6iPefDqwXQWHSw9S9vz94BZ","metadata":{"name":"staking","version":"0.1.0"…`.
Its `types` give both layouts exactly:

| UserStake (169 B) | | StakeConfig (193 B) | |
|---|---|---|---|
| 8 `bump` u8 | 105 `shares` u128 | 8 `bump` u8 | 113 `cooldown_seconds` u64 |
| 9 `stake_config` pubkey | 121 `cost_basis` u128 | 9 `authority` pubkey | 121 `total_shares` u128 |
| 41 `user` pubkey | 137 `cumulative_commission_before_staking` u128 | 41 `mint` pubkey | **137 `share_price` u128** |
| 73 `guardian_pool` pubkey | **153 `unstaking_amount` u64** | 73 `stake_vault` pubkey | 153 `commission_weight_sum` u128 |
| | **161 `unstake_timestamp` i64** | 105 `min_stake_amount` u64 | 169/185 … |

**Independently corroborated before the IDL was found**, over all **47,862** live UserStake accounts:
the only bytes past 128 that are ever nonzero are **153-160 and 161-168**, on exactly the same
**2,871**-account population. Two readings agreeing is why this is stated as fact.

### 1a. ⛔ AND IT SETTLES ACCEPTANCE CRITERION 3 STRUCTURALLY

IDL instruction docs, verbatim:

> `unstake` — *"Initiates an unstake by **burning shares** and marking tokens for withdrawal after the
> cooldown period. The unstaking amount is calculated and saved at the time of unstake to prevent
> benefiting from share price increases during cooldown."*
> `cancel_unstake` — *"…**restoring the shares** and clearing the unstaking state."*

So **`shares` already excludes anything unstaking**. `shares × share_price / 1e9` **is** the active
stake, and *"unstaking amount is not counted as actively resonating SKR"* holds **by construction** —
no subtraction had to be invented. `unstaking_amount` is a **token** amount in base units (already SKR,
**not** shares); it is reported separately and must never be multiplied by the share price.

`cooldown_seconds` is read from StakeConfig (live value **172800**, 2 days) and is **never hardcoded** —
it is an authority-updatable field on an account we already fetch.

---

## 2. ⛔ THE BLOCKER NOBODY HAD FLAGGED: PDA DERIVATION NEEDS AN ON-CURVE TEST

A PDA is *defined* as 32 bytes that are **not** a valid ed25519 public key. Derivation must walk
bump 255→0 and take the first candidate **off the curve**. The Unity client gets this free from
`Solana.Unity.Wallet.PublicKey.TryFindProgramAddress`. Node has no such dependency here
(`package.json` carries `bs58` + `tweetnacl`; `@solana/web3.js` is deliberately absent per WO-1674 §6),
and **`tweetnacl.lowlevel` does not export `unpackneg`** — verified by enumerating its keys.

Roughly **half** of all candidates are on-curve, so an implementation without the test returns the
wrong address for about half of all wallets — and a wrong address is still valid base58, reads back as
`ACCOUNT_NOT_FOUND`, and this feature's own status model calls that **"no stake"**. A staker would be
silently told they have nothing, with no log line naming the cause.

`api/_lib/solana-pda.js` implements RFC 8032 point decompression in BigInt. **Proof it matters:**

```
5 real mainnet UserStake accounts, re-derived from the `user` pubkey stored INSIDE each:
 chain pubkey : 13MeApxaDr2tXPk3eAsfVmRjNznLobMXDGMxquTiZhq
 derived      : 13MeApxaDr2tXPk3eAsfVmRjNznLobMXDGMxquTiZhq bump 254 (stored bump 254) MATCH=true
 … 15uuJLSQ4W5zhCTx3fR5vJdyQQbtP6UHrsCVPEg2274  MATCH=true
=== DERIVATION MATCH 5/5 ===
```

**Every one at bump 254, not 255** — i.e. the bump-255 candidate was on-curve and rejected. A naive
implementation would have been wrong on all five. Also checked: 200/200 generated ed25519 public keys
report on-curve; 50/50 derived PDAs report off-curve.

---

## 3. LIVE ACCEPTANCE EVIDENCE (captured this session, mainnet)

```
--- ACCEPTANCE 2/7/8: a wallet WITH a real stake ---
 walletAddress    GZaWCBQgqGhhEEHQmmmJxDd98kSUbgLkjWf2iMUPj9jT
 userStakeAddress 13MeApxaDr2tXPk3eAsfVmRjNznLobMXDGMxquTiZhq
 sharesRaw        40000000000      sharePriceRaw 1136636001
 activeStakedRaw  45465440040      -> display 45465.440040 SKR
 cooldownSeconds  172800           unstakingRaw 0   unstakingReady false
 sourceSlot       445950918        verificationStatus VERIFIED   rpcLatencyMs 405
--- ACCEPTANCE 1: a wallet with NO stake ---
 3fnHxtN6T6fLvDLMLiMaVZNgJ4jo45zFVpA85Rf8YzHf -> NO_STAKE, activeRaw 0, slot 445950920
--- ACCEPTANCE 4: RPC failure must NOT become zero ---
 status RPC_UNAVAILABLE  activeStakedRaw = null (NOT 0)  errorCode stake_config_rpc_unavailable
```

Chain/clock sanity: slot 445950361, blockTime 1789064326 = `2026-09-10T18:18:46Z`, local skew **1 s**.
StakeConfig discriminator read back as `238,151,43,3,11,151,63,176` — exactly the constant.
Aggregate (context for HEART-003/009 balance work): **47,862 stakers, ~4.96 billion SKR staked**,
share price **1.136636001**; **2,871** wallets have a pending unstake, **2,357** of them past cooldown.

**Acceptance line by line:** 1 ✅ (captured) 2 ✅ (captured) 3 ✅ **structural, from the IDL** (§1a)
4 ✅ (captured) 5 ⚠ **structural, UNEXECUTED** — GET-only, no `req.body`, wallet from `auth.identity`,
plus the lint; there is no captured 200 of an ignored injection attempt (§7)
6 ⚠ **structural, UNEXECUTED** — `PRIMARY KEY (player_id)` + the `ON CONFLICT` upsert make a duplicate
Heartbound account impossible, but the upsert has never run against a database (§7)
7 ✅ (BigInt end to end; one conversion, at the edge) 8 ✅ (dedicated mainnet var, no devnet branch)
9 ⛔ **NOT RUN** — needs `DATABASE_URL` (§7) 10/11 ⛔ **NOT RUN** — needs Unity (§7) 12 ✅.

---

## 4. WHAT SHIPPED

**Backend**
- `api/_lib/solana-pda.js` **(new)** — ed25519 on-curve test + `findProgramAddress`, zero new deps.
- `api/_lib/skr-staking.js` **(new)** — constants, IDL-exact decoders, share math in `BigInt`,
  `verifyStake` (wallet in, seven-state answer out), `resolveServedState` (the grace ruling as one pure
  function), cache/cooldown predicates.
- `api/heartbound/status.js` **(new)** — `GET /api/heartbound/status?playerId=…[&refresh=1]`.
- `api/migrations/20260910_0024_skr_stake_snapshots.sql` **(new)** — the snapshot table.
- `api/schema.sql` — description block appended (binary edit; CRLF preserved, LF delta exactly +70).
- `test/skr-staking.test.js` **(new)** — **26 cases, 26 pass, 0 fail** (`node --test`).

**Client**
- `Assets/_Modules/Core/Platform/VerifiedStakeSnapshot.cs` **(new)** — the only stake a reward may read.
- `Assets/_Modules/Wallet/HeartboundStatusClient.cs` **(new)** — the only writer; `#if DAPP_STORE`.
- `Assets/_Modules/Core/Catalog/PolishBonusProvider.cs` — **re-pointed** (§5).
- `Assets/_Modules/Wallet/NativeSkrStakeQuery.cs` — **display-only** header + trace (§5).
- Three `.cs.meta` files, GUIDs verified unique across `Assets/`.

**Regression**
- `Assets/Editor/Regression/StakingComplianceRegression.cs` **(new)** — D5 (§6).

---

## 5. D4 — WHAT I DID WITH THE CLIENT READ, AND WITH THE POLISH-ATTEMPTS CONSUMER

**The ruling is "backend only", so `NativeSkrStakeQuery` became DISPLAY-ONLY rather than deleted**, and
the reason is concrete, not conservatism:

1. It still feeds the shipped Seekerthon showcase panel and the Jeweler FTUE card through
   `StakeRewardsResolver.Query`. Deleting it blanks a live screen to fix a problem that lives elsewhere.
2. **`Assets/Editor/Regression/JewelerDiscoveryFtueRegression.cs:23,116-121` reads that file as text**
   and requires the program id, the `Encoding.UTF8.GetBytes("user_stake")` seed, the
   `WalletPreferenceStore.CurrentSessionWalletAddress` lookup, `GetAccountInfoAsync` and the
   `"no signature requested"` trace to still be present. Deleting the file turns that suite red for a
   reason unrelated to the defect.

**The actual fix for product rule 7 was never "delete a class" — it was to make sure no REWARD reads
that number.** `NativeSkrPolishBonus.Standing` changed from

```csharp
StakeRewardsResolver.Resolve()                                    // reads the SETTABLE Query
→ StakeRewardsResolver.Resolve(VerifiedStakeSnapshot.RewardBearingStakeSkr)
```

The no-arg overload resolves from `StakeRewardsResolver.Query`, a **public settable property**
(`StakeRewardsResolver.cs:175-183`) — that was the live injection path. The explicit overload keeps
`stake-rewards.json`'s ladder deciding the **shape** of the standing while the **amount** comes from the
server. Setting `Query` can still change what a panel **shows**; it can no longer change what anything
**grants**. `StakingComplianceRegression` A2 fails if `StakeRewardsResolver.Resolve();` reappears in
that file — **and it fails against HEAD, which is the red-before-green proof** (§6).

**The polish-attempts consumer (WO-1673 D6 context):** `IPolishBonusProvider` is **unchanged** — still
`ExtraWeeklyRerolls` + `RollCapDelta`, still no member for odds, weights, luck, tier bias or a bonus
table. WO-1674 moved **where the input comes from and nothing else**. The grant is still ATTEMPTS
(+1 weekly re-roll; +1 roll cap at 10k+ SKR), so a staker's roll stays exactly as likely as a free
player's, and the WO-1673 D6 "staking buys extra attempts" adjacency is **not widened by this ticket**.
Widening it is a separate decision the owner has not made. `RewardBearingStakeSkr` returns **0** unless
the backend vouched, so the practical effect is that the perk now needs a verified backend answer —
which is the ruling, and is why `NoteAttemptFailed` never zeroes an existing snapshot.

⚠ **`VerifiedStakeSnapshot` has a settable-looking static writer and that is unavoidable in-process** —
C# cannot make a static un-writable to its own assembly. The guarantee is instead **exactly one
caller**, enforced by a tree walk in the regression (A4), which also fails if *nothing* writes it.

---

## 6. D5 — `StakingComplianceRegression` NOW EXISTS

`grep -rn "StakingComplianceRegression" .` previously returned **three hits, all claims that it
protects something** — `FeatureFlags.cs:1008` ("Pinned by StakingComplianceRegression", on the flag
deciding whether token-gated gameplay compiles into a Play artifact), plus WO-1255:95 and WO-1673:138.
**No definition anywhere.** Third imaginary firewall in this feature area. It is now real.

**Registration line for the lead** (I did not edit `DataRegression.cs`), matching the
`Guard.Try` neighbour style at `Assets/Editor/Regression/DataRegression.cs:705`:

```csharp
DeNelle.Core.Diagnostics.Guard.Try("Regression", "staking-compliance suite", () => { if (!DeNelle.Editor.Regression.StakingComplianceRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[staking-compliance] " + r); });
```

Focused entry point for a red/green run: `DeNelle.Editor.Regression.StakingComplianceRegression.RunFocused`
(marker `STAKING_COMPLIANCE_OK` / `_FAIL`).

**Red-before-green — proven, with an honest caveat.** I have no Unity, so I could not run the suite
itself. I ran a **faithful line-by-line Python port of its assertions** against (a) the working tree and
(b) `git show HEAD:<path>` for every input:

```
=== working tree  === STAKING_COMPLIANCE_OK
=== HEAD (before)  === STAKING_COMPLIANCE_FAIL
   - A1 reward path not on verified snapshot      - C1 no verifier
   - A2 reward path back on the SETTABLE seam     - C2 no mainnet var
   - A3 pays on unverified state                  - C4 on-curve check gone
   - A5 client query not display-only             - C6 no grace resolution   (+8 more)
```

**A2 failing on HEAD is the substantive result**: the injection path was real and is now closed.
⚠ **This is a port, not the suite.** The lead must still see `REGRESSION_OK <n>/<n> suites` on a fresh
log after registration.

Also caught while authoring it: the first version **failed against a correct file**, because the
endpoint's header *quotes the spec* (`stakedSkr = 500000`) and the banned-token scan matched the
documentation. Fixed by stripping `//` comments before that scan — a lint that punishes a file for
explaining itself teaches the next author to delete the explanation.

**Every needle was verified byte-for-byte against its file** before this was written; one mismatch was
found and fixed, and it was that one.

---

## 7. ⛔ WHAT IS NOT PROVEN — AND MUST NOT BE READ AS DONE

- **Nothing was gated, committed, deployed, or migrated.** No `COMPILE_GATE_OK`, no
  `REGRESSION_OK <n>/<n>`, no `MIGRATIONS_OK applied=N skipped=M`. No Unity on this lane.
- **The migration has never been applied.** Acceptance 9 needs `node tools/run-migrations.mjs` with
  `DATABASE_URL`, judged by the **marker** then by the **shape query** written into the migration's
  footer (memory `idempotent-ddl-hides-a-stale-table`).
- **The endpoint has never been executed.** The verifier it calls has (§3), and every handler branch is
  unit-tested, but the HTTP route itself is unrun. Acceptance 5's "quote the ignored injection attempt"
  therefore rests on the **shape** argument (GET-only, no `req.body`, wallet from `auth.identity`) plus
  the lint — **not** on a captured 200. Closing it costs one `curl` post-deploy.
- **`SOLANA_MAINNET_RPC_URL` is not set anywhere.** Until it is, the endpoint answers
  `RPC_UNAVAILABLE / rpc_url_unset` — deliberately, not as a fallback to another network.
- **The client path is unexercised.** `HeartboundStatusClient` is `#if DAPP_STORE`, so it compiles only
  in a Seeker/dApp artifact; it has not been built, let alone run on a device.
  ✅ **Checked, because it was the one thing that could break a non-Seeker gate:** the `Driver` reads
  `WalletPreferenceStore.CurrentSessionWalletAddress`, and neither `WalletPreferenceStore` nor
  `MwaSessionStore` sits behind `#if SOLANA_SDK` (only `SolanaWalletProvider.cs` and
  `TargetedLocalAssociationScenario.cs` use that define) — and `NativeSkrStakeQueryDriver:140`
  references the same property unconditionally in the same assembly. No new define exposure.
- ⚠ **`fromCache: true` does NOT imply a verified snapshot.** A player who has never succeeded and
  then hammers `refresh=1` inside the 60 s cooldown is served their stored row — which may be an
  `RPC_UNAVAILABLE` row with **null** amounts. That is correct (the cooldown is honoured and nothing is
  fabricated), but a later consumer must branch on `verificationStatus`, never on `fromCache`.

---

## 8. ⛔ TWO TODOs LEFT OPEN ON PURPOSE, AND ONE NEW FINDING

The brief said: if the spec + rulings do not give a decision, **stop at a clearly marked TODO with the
question, do not guess.** Both are marked in code.

**TODO-1 — the grace window's LENGTH.** `api/_lib/skr-staking.js`, `DEFAULT_STALE_GRACE_SECONDS`.
The ruling says *bounded*; it does not say by how much, and the spec sets no figure. **The mechanism is
built and wired** (env knob `HEARTBOUND_STALE_GRACE_SECONDS`, boundary behaviour unit-tested at
`age == grace` and `age == grace + 1`). Only the number is open, and it is a fairness call: it is how
long a player keeps benefits after we can no longer see their stake — **which is also how long someone
who unstakes keeps them.** The provisional 86400 is **not canon** and is labelled so; it was chosen only
because `vercel.json` declares two crons, both daily, so anything shorter would expire before the next
scheduled refresh could renew it. ⚠ Do not copy that number into a doc.

**TODO-2 — the RPC provider.** `mainnetRpcUrl()`. Public `api.mainnet-beta.solana.com` rate-limits hard
and is not a production dependency; no paid provider is provisioned for this project. Read from
`SOLANA_MAINNET_RPC_URL` so the choice is a dashboard setting.
⚠ **Deliberately NOT the existing `SOLANA_RPC_URL`**, which `api/tower-swap/log.js` and
`api/purchases/verify.js` already read and which `api/_lib/purchase-catalog.js:57` proves may be devnet
(`{ devnet: 9, 'mainnet-beta': 6 }`). Silently inheriting it is how a verifier reports a devnet stake as
real — and that file's `:37-48` records a **1000× overcharge** from getting SKR decimals wrong once.
A pinned check refuses `process.env.SOLANA_RPC_URL` in the verifier.

**NEW FINDING (not in the WO) — display and reward can now disagree, briefly.**
`JewelerDiscoveryFtue.cs:304-307` still reports "stake verified / re-roll granted" from
`StakeRewardsResolver.Resolve()` (the **client** read), while the grant now comes from the **backend**
snapshot. On a Seeker build these agree except in two windows: **before the first backend answer
arrives**, and **during an RPC outage past the grace window**. In those windows the card can say
verified while no re-roll is granted. I did **not** change it — the FTUE's copy is pinned by
`JewelerDiscoveryFtueRegression:107-115` and rewording a player-facing claim is the owner's call, not
mine. **Recommend a small follow-up WO**: point the card's stake copy at `VerifiedStakeSnapshot.Status`
so the sentence and the grant have one source.

---

## 9. FILES

**New:** `api/_lib/solana-pda.js` · `api/_lib/skr-staking.js` · `api/heartbound/status.js` ·
`api/migrations/20260910_0024_skr_stake_snapshots.sql` · `test/skr-staking.test.js` ·
`Assets/_Modules/Core/Platform/VerifiedStakeSnapshot.cs` (+`.meta`) ·
`Assets/_Modules/Wallet/HeartboundStatusClient.cs` (+`.meta`) ·
`Assets/Editor/Regression/StakingComplianceRegression.cs` (+`.meta`) · this file.

**Modified:** `api/schema.sql` · `Assets/_Modules/Core/Catalog/PolishBonusProvider.cs` ·
`Assets/_Modules/Wallet/NativeSkrStakeQuery.cs` ·
`WorkOrders/WORK_ORDER_1674_heart_001_native_skr_staking_verification_layer.md` (Status → DONE).

**Untouched, as instructed by §6 of the WO:** `api/_lib/wallet-auth.js` (caller added, no branch) ·
`authenticateGranting`'s guest refusal · `IPolishBonusProvider`'s shape · the `DAPP_STORE`/`GOOGLE_PLAY`
stamps and the `!GOOGLE_PLAY` asmdef constraint · `api/game/save.js` ceilings · `SaveSchema` (**no
version bump — Heartbound state is a Neon table, never the save**) · `DataRegression.cs` (registration
line handed to the lead) · `CLI_LANES_WO_NUMBERS.md`. No new dependency; no `@solana/web3.js`; no KV.

## 10. GATES RUN ON THIS LANE

```
python tools/gate_brace.py <5 .cs>        -> GATE_BRACE_SUMMARY bad=0 of 5   (exit 0)
raw brace + NUL scan (5 .cs)              -> ALL CLEAN (15/15, 21/21, 20/20, 19/19, 19/19; NUL=0)
node --check  (4 .js: 3 new + the test)   -> all OK
node --test test/skr-staking.test.js      -> tests 26  pass 26  fail 0
api/schema.sql binary edit                -> CRLF preserved, LF count 1989 -> 2059 (+70 = lines added)
```

**Still owed by the lead:** `COMPILE_GATE_OK`, `REGRESSION_OK <n>/<n> suites` on a fresh log after
adding the registration line, and `MIGRATIONS_OK applied=N skipped=M` + the shape query when the owner
applies the migration.

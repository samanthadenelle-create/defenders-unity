# WORK ORDER 1682 — HEART-009 — RESULT

**Status:** IMPLEMENTED (2026-09-10). The 10% ceiling now has a METER — a pure function over a modifier
list, measured at **0.06 against 0.10**, red-before-green proven by injection. It is **not** the fourth
imaginary gate.
**Lane:** HEART-006/009 SME (owned both; they gate each other).
**Tree:** worktree `agent-a8bb71753a62e0d92` at `dev` **`4329accd1`**.
**Not gated, not committed, not pushed** — no Unity was fired by this lane.

---

## 1. D1 — the metric, defined ONCE, then measured

**The definition (Q-METER, owner, 2026-09-10 13:36, verbatim):** *"production-rate modifiers only — the
10% ceiling is the sum of the passive tier percentage boosts to resource yield; timers and event drops are
not counted."*

⭐ **It is expressed as DATA, not as an `if`.** `api/_lib/heartbound-tiers-config.json` carries a
`benefitKinds` map in which every kind declares its own `countsTowardCeiling`. `economicAccelerationMeter`
(`api/_lib/heartbound-tiers.js`) reads that map. Consequences that matter:

- a **new kind must declare its own answer** and cannot default into being uncounted — an undeclared kind
  **THROWS** rather than being silently skipped, which is the single way a modifier could otherwise slip
  past the ceiling;
- the editor regression reads the **same** `countsTowardCeiling` flags rather than hardcoding
  `"productionRate"`, so there is exactly **one** definition of the metric, as acceptance 1 requires.

**The measurement**, printed by the suite itself (`node --test test/heartbound-tiers.test.js`):

```
HEARTBOUND_CEILING_MEASURED sum=0.06 ceiling=0.1 counted=3 excluded=10
  rows=T1:offline_gathering=0.02,T2:echo_labor=0.02,T7:echo_workers=0.02
ℹ tests 26   ℹ pass 26   ℹ fail 0
```

⛔ **The sum is CUMULATIVE over the whole ladder**, because a Tier X player holds every lower tier's
passive. The number above is the worst case, not a per-tier figure.

The meter also returns `excluded` — the ten rows it did **not** count and why — so "the ceiling is fine" is
never a claim without the working shown.

**RED BEFORE GREEN, and the shipped config was never edited to get it.** Case *"RED BEFORE GREEN: a table
that breaches the ceiling is REPORTED AS BREACHING"* feeds an **injected** table (Tier I at 0.09 → sum
0.13) to the same pure function, asserts `withinCeiling === false`, then re-asserts the shipped table still
measures 0.06. Editing the real config to prove a gate works proves nothing about the real config.

Two further refusals are pinned: a counting-kind row with **no numeric value** throws; a **missing
ceiling** throws with the message *"the imaginary gate WO-1682 §0c refuses"*.

---

## 2. D2 — the covenant sweep

⛔ **NOT EXTENDED, AND THIS IS A DELIBERATE SCOPE CALL, NOT AN OMISSION.**
`MonetizationCovenantRegression`'s hardcoded six-file list (`:97-104`) is unchanged.

Reason: **there is no Heartbound reward table to sweep yet.** WO-1682 §0a offered "land after WO-1673 D2,
or explicitly add itself and say so" — but the artefact that would be added, `heartbound-events.js` /
the event reward table, is **WO-1678's** and does not exist in this tree
(`find api -iname '*heartbound*'` → only `heartbound-resonance*`, `heartbound-state.js`, `status.js`).
Adding a path for a file that does not exist would make the gate read a missing file every run.

What this lane DID do instead, which is the part that was actually at risk: the **benefit** table (the
other half of the reward surface, and the half this lane owns) is swept by
`HeartboundBenefitsRegression`, and `api/_lib/heartbound-tiers.js` **refuses at the module boundary**:

- an **odds-shaped polish grant** throws (`"ATTEMPTS, NEVER OUTCOMES"`) — only `extraWeeklyRerolls` and
  `rollCapDelta` are accepted, mirroring `IPolishBonusProvider`'s two members exactly;
- a **fractional or negative** attempt count throws;
- an **unclassified kind** throws.

**Handed to the lead:** when WO-1678's event table lands, add its path to
`MonetizationCovenantRegression.cs:97-104` **in that ticket** — or, better, land WO-1673 D2 (derive the
list) first, which removes the need to remember.

**Acceptance 3 ("no Heartbound reward path can grant a forbidden asset").** Partially met, and the split is
honest: the **benefit** table is asserted by the gate above (nothing in it grants a token — the closed
vocabulary is `productionRate | eventUnlock | information | polishAttempts | presentation`, and a wire test
asserts the payload contains no `stake|wallet|skr` token at all). The **event** table is not yet covered
because it does not exist. §0b's existing policy is cited rather than restated: `PackStore.cs:467-473`,
`StakeRewardsResolver.cs:5-10`, `BattleMonthlyRegression.cs:636-638`, `JobRushPolicy.cs:65-67`.

---

## 3. D3 — config discipline, and the `serverOnly` marker (Q-CONFIG)

**Acceptance 4 ("every reward value comes from config"): MET.** `api/_lib/heartbound-tiers.js` contains no
numeric literal of the benefit table, no tier threshold, and no ceiling — a test strips its comments and
asserts every live threshold from `heartbound-resonance-config.json` is **absent** from the code, and that
its only two `require`s are its own config and the resonance module.

**Q-CONFIG (RULED 13:36): `api/_lib/tunable-manifest.js` now understands `serverOnly`.**

The join is a three-way one, and a `PRESENTATION` row with no **client** registry entry fails it
(`:848-853`). Heartbound's knobs are backend-read; putting a tier threshold into `RemoteTunables.cs` to
satisfy the join would hand the Unity client a readable copy of the ladder — the second authority Q-CONFIG
exists to prevent. So:

- `mismatches(presentation = PRESENTATION)` and `build(presentation = PRESENTATION)` take an injectable map
  (live call sites unchanged);
- a `serverOnly: true` row is **exempt from the registry-agreement defect and from that alone**;
- `build()` emits such a row carrying `serverOnly: true`, with `def` **referenced** from the backend module
  that reads the knob — never retyped.

⛔ **A REAL GAP WAS FOUND AND CLOSED BY MY OWN TEST, and it is worth recording.** Every per-row check
(area, safe range, label, prose) previously lived **inside the loop over the build registry** — so a row
with no registry entry was reached by **none** of them. Marking such a row `serverOnly` would have exempted
it not from ONE defect but from **EVERY** defect: a backend knob with a broken range, no label and a
nonexistent area would have joined cleanly and appeared on the owner's page as a lever she could not use.
The checks are now extracted into `checkPresentationRow` and run for serverOnly rows too, plus a new defect
— `SERVER-ONLY ROW vs SERVER ALLOWLIST` — because **serverOnly buys an exemption from needing a BUILD,
never from needing to be WRITABLE.** Caught by the case named *"the serverOnly exemption is EXACTLY ONE
DEFECT WIDE"*, which went red before it went green.

**Regenerated manifest — the byte-exactness claim, proven.** `RemoteTunables.cs` was **not touched**, so
the generated spine must not move, and it did not: `git status` reports
`api/_lib/tunable-manifest.generated.json` **unchanged**.

⚠ **AND A PRE-EXISTING, CHECKOUT-LOCAL FAILURE WAS FOUND — NOT CAUSED BY THIS LANE.** The oracle case
*"the checked-in spine is byte-identical to a fresh derivation"* **FAILS in this worktree**, and it fails at
`HEAD` too. Measured:

```
COMMITTED blob : bytes 6779  CR 0    LF 266     <- correct, LF
WORKTREE  file : bytes 7045  CR 266  LF 266     <- CRLF
worktree with CRLF->LF equals committed: True
git config core.autocrlf -> true
```

The committed blob **is** LF and **is** byte-identical to a fresh derivation. The working copy is CRLF
purely because `core.autocrlf=true` on this machine, and git therefore reports the file as unchanged.
⛔ **I deliberately did NOT "fix" it by rewriting the file to LF** — that would have produced a
whitespace-only diff of a generated artifact and risked committing it. Neither input to that test
(`RemoteTunables.cs`, the generated JSON) was touched by this lane, so the change provably cannot affect it.
**Recommend a `.gitattributes` entry pinning `*.generated.json eol=lf`, as a separate one-line ticket.**

**Test results:**

```
node --test test/heartbound-tiers.test.js test/skr-staking.test.js
                                   tests 52  pass 52  fail 0
test/heartbound-status-benefits.test.js  tests  5  pass  5  fail 0   <- drives the REAL handler
test/heartbound-tiers.test.js      tests 26  pass 26  fail 0
test/tunables-manifest.test.js     tests 27  pass 26  fail 1   <- the CRLF case above, pre-existing
test/heartbound-resonance.test.js  tests 21  pass 21  fail 0   <- untouched sibling, no regression
test/command-center.test.js        tests 56  pass 56  fail 0   <- build() change is shape-neutral
node --check api/heartbound/status.js  -> OK
```

⚠ **`test/skr-staking.test.js` needed one HEART-001 artefact copied in to run at all.** Its case *"the
migration CHECK constraint lists exactly those seven states"* reads
`api/migrations/20260910_0024_skr_stake_snapshots.sql`, which exists only in the main tree. Copied
**unchanged** (blob `45c49c2cfc920f6e8acaabc0bd309bca5a8f55f6`); with it present the suite is 26/26.
**That failure was a missing file, never a defect in this lane's change** — measured, not assumed.

⚠ **And a related pair of failures was correctly left alone.** `test/heartbound-state.test.js` (HEART-002's)
fails two cases in this worktree — *"the migration and api/schema.sql carry the SAME CREATE TABLE body"*
and *"every heartbound_state object is DECLARED EXACTLY ONCE"* — because **`api/schema.sql` here has 0
occurrences of `heartbound_state` while the main tree has 7**: HEART-002's schema edit is uncommitted, and
`api/schema.sql` is on this lane's do-not-touch list. My temporary copies of that suite and its migration
were **removed** rather than carried into the hand-back, so they do not appear as this lane's changes.

---

## 4. D4 — preferred reward classes, and the two that are blocked

Spec `:933-943`'s list is the allowed set, recorded as the config's closed `benefitKinds` vocabulary.
Status of the two WO-1682 D4 called out:

- **Rough stones — DROPPED, not blocked.** Q-STONE ruled 2026-09-10 13:12: *"drop the Rough Stone
  Discovery event."* Tier III's row is therefore **empty**, and a test asserts it is empty **by ruling, not
  by oversight** — nothing was invented to replace it, because inventing a substitute would be this lane
  making a design decision.
- **Heartfire — UNBLOCKED.** Q-HEARTFIRE ruled: a second source is allowed for stakers and the
  single-source lint is re-pointed to permit exactly Heartfire Spark. Tier VI carries `heartfire_spark` as
  an `eventUnlock`. ⛔ **Re-pointing `HeartfireRegression`'s lint is WO-1678's lane and was NOT done here.**

---

## 5. Q-CLIENTECON — accepted, and WRITTEN DOWN as not-an-anti-cheat

Ruled 13:32: accept as a design guardrail; rate modifiers stay. That is recorded in **three** places so a
later reader cannot mistake the meter for an enforcement boundary:

1. `heartbound-tiers-config.json` `economicCeiling._comment` — *"THIS CEILING IS A DESIGN GUARDRAIL, NOT AN
   ANTI-CHEAT CONTROL"*, citing `api/game/save.js:70-72` and `:410-421`;
2. `heartbound-tiers.js`'s header, at the meter;
3. the meter's own returned `definition` string, so it travels with the number.

A test asserts all three still say it.

---

## 6. What was NOT done, and why — the four cross-lane hand-offs

✅ **`api/heartbound/status.js` — EXTENDED IN THIS LANE. THE SHIP-ORDER HAZARD IS CLOSED.**
The endpoint and the `PolishBonusProvider.cs` hunk now land together, so there is no window in which a
staker's re-roll reads zero. (An earlier draft of this RESULT left the hunk for the lead; the coordinator
correctly ruled the endpoint in-lane, and it is done.)

**Exact lines added to `api/heartbound/status.js`** (base blob `89746abf81e5387dc0198b85a9ee0f7a4b12d5ae`,
19641 bytes — apply to the main tree's live copy):

| Line | What |
|---|---|
| `:61-62` | `require('../_lib/heartbound-state')` + `require('../_lib/heartbound-tiers')` |
| `:64-101` | header block: why the client is TOLD, and the scalar-`nextTierAt` trap |
| `:106-124` | `async function heartboundBlock(sql, playerId, nowMs)` — injects `tiers.resolveTier`, returns `{heartboundStatus, nextTierAt, benefits}`, **returns `null` and never throws on failure** |
| `:127-133` | `silentHeartboundBlock()` — the zero block, for the one case where tier 0 is a FACT |
| `:342` | guest / `WALLET_NOT_LINKED` return: `...silentHeartboundBlock()` |
| `:384`, `:392` | cached-stake return: `const cachedTier = await heartboundBlock(...)`, spread `...(cachedTier \|\| {})` |
| `:455`, `:467` | fresh-read return: `const freshTier = await heartboundBlock(...)`, spread `...(freshTier \|\| {})` |

⭐ **`nextTierAt` needed no new arithmetic** — HEART-002 built the seam: `heartbound-state.js:385-410`
takes an **injected** `resolveTier` precisely so it holds no ladder, and `tiers.resolveTier` satisfies that
contract exactly. Neither this route, nor the state module, nor the client holds a threshold; a test
asserts `status.js` never names `minScore`.

⭐ **Three design decisions worth naming, because each is a trap avoided:**
1. **The block is served on the CACHED path too** (`:384`). The five-minute cache is about the CHAIN read;
   the Heartbound row moves on its own cadence (the daily pulse). Omitting it there would freeze a
   player's benefits behind a stake cache for no reason, and it costs no RPC — it is a local read.
2. **A failed Heartbound read OMITS the block; it never sends a zero one.** `heartboundBlock` returns
   `null`, the fields are spread rather than assigned, and the client holds its previous answer. Emitting
   `benefits: {tier: 0}` on a database blip would stand every passive down — the same fail-to-zero mistake
   the owner's Q2 ruling forbids one layer up. Pinned by a test.
3. **The guest rail gets an explicit ZERO block**, because there tier 0 is a *fact* (no wallet, no way to
   attach one — the Seeker-only ruling at the wire), not ignorance.

**New suite: `test/heartbound-status-benefits.test.js` — 5/5 pass**, and it drives the **real exported
handler** with the module cache primed, not a re-implementation of what the route does. Its SQL mock
routes on the query **text**, so a test cannot pass by the route reading the wrong table — one case
asserts both `skr_stake_snapshots` **and** `heartbound_state` were actually queried.

⛔ **AND THERE WAS A SILENT WIRE BUG WAITING IN IT.** `heartbound-state.js:405-408` does
`String(resolved.nextTierAt)` — a **scalar** — while `heartbound-resonance.nextTierAt()` returns an
**object** `{tier, name, minScore, pointsAway}`. Wiring the obvious thing would have put
**`"[object Object]"`** on the wire with no error anywhere. `tiers.resolveTier` returns `nextTierAt` as the
next threshold's `minScore` **number**, with the richer object under a separate `nextTier` key, and a test
named for exactly this pins the two contracts in step.

⛔ **`api/_lib/heartbound-state.js` — NOT EDITED** (out of scope, and it needs no change).

⛔ **`StakingComplianceRegression.cs` — NOT EDITED, and its pins SURVIVE this change.** Read in full from
the main tree. `§A1:92` requires `PolishBonusProvider.cs` to contain
`VerifiedStakeSnapshot.RewardBearingStakeSkr`. The Q-LADDER merge could easily have broken it by replacing
the stake read wholesale. **It does not, and the reason is a design one rather than a test-pleasing one:**
the verified stake still **gates entitlement** (`Standing.HasStake`), the tier now supplies the **amounts**.
That matters because the two arrive in the same response but live in different statics, and a later failed
refresh deliberately holds the previous benefits — so a tier alone could pay out on an answer no longer
backed by a verified snapshot. Gating on the snapshot keeps *"no reward is ever paid on an unverified
state"* (WO-1674 §A3) true for this grant. `§A2` (must NOT call the no-argument `Resolve()`) still holds.
`§A4` (exactly one caller of `AcceptServerVerification`) still holds — `AcceptServerBenefits` is a
**different method on a different type**, called from the same single client immediately after.

⛔ **`DataRegression.cs` — NOT EDITED.** The registration line is in WO-1679's RESULT §3.
⛔ **`CLI_LANES_WO_NUMBERS.md` — NOT EDITED.** Both numbers were pre-assigned by the coordinator.

---

## 7. Unproven, named as unproven

- ⛔ **`HeartboundBenefitsRegression` has never been run AS C#** (no Unity in this lane). Its **logic** was
  dry-run port-for-port in Python over the real tree (WO-1679 RESULT §2) — `DRYRUN_VERDICT GREEN`, after a
  first run that went **RED** and caught a real double flag-read. The meter's red-before-green is proven in
  Node. Unproven: that the C# compiles, that `Application.dataPath`'s parent resolves to the repo root in
  batchmode, and that `DataRegression.RunAll` reaches it.
- ⚠ **`node --test test/command-center.test.js` → 56/56 pass**, so the `build()` signature change is
  measured shape-neutral for the served console page, not merely assumed to be.
- ⛔ **No `COMPILE_GATE_OK`.** Brace balance (`bad=0 of 6`) and NUL (`bad=0 of 11`) are proven; compilation
  is not. `HeartboundStatusClient.cs`'s new `AcceptBenefits` sits inside `#if DAPP_STORE`, which the editor
  gate may not compile — **a compile error there could survive a green gate.** Compile once with
  `DAPP_STORE` defined before a Seeker build.
- ⛔ **The ceiling is unenforceable against a modified client, by ruling.** Stated, not hidden (§5).
- ⚠ **I have NOT run `GooglePlayPackagingGate`** and cannot claim it objects to
  `VerifiedStakeSnapshot.cs` — only that the file is in `DeNelle.Core` (every artifact), carries `Skr` in
  member names and `" SKR"` in a literal, and that the gate's forbidden list contains `"skr"`. Full analysis
  and the reason I did not move it: WO-1679's RESULT §5.

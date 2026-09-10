# WORK ORDER 1675 — HEART-002 — RESULT

**Status:** IMPLEMENTED 2026-09-10 (backend only; not gated, not committed, not pushed — the lane does not hold the commit).
**Lane:** HEART-002 SME, worktree `.claude/worktrees/agent-a7c6e0e7c4f3ba388`, branch `dev`.
**Tree:** fast-forwarded twice. First to **`c96030b5c`** (the WO header names `abbeb9362`, an older tree); then, after the coordinator's correction, to **`cde1c1f63`** — the commit that brought `api/_lib/heartbound-resonance.js` (WO-1676/HEART-003). The second ff was **conflict-free** and left this lane's `api/schema.sql` edit intact.

---

## 1. FILES CHANGED — the exact list

| Path | Change |
|---|---|
| `api/migrations/20260910_0025_heartbound_state.sql` | **NEW.** The APPLYABLE DDL. |
| `api/schema.sql` | **MODIFIED.** New section 23 inserted before `END OF SCHEMA` (`:1988-2063`). Nothing else in the file touched. |
| `api/_lib/heartbound-state.js` | **NEW.** The one data-access seam. Imports `toBigInt` from `api/_lib/heartbound-resonance.js` (WO-1676) — no second coercion contract. |
| `test/heartbound-state.test.js` | **NEW.** 24 `node:test` cases. |
| `WorkOrders/WORK_ORDER_1675_heart_002_heartbound_state_and_persistence.md` | **MODIFIED.** `**Status:**` flipped to IMPLEMENTED. |
| `WorkOrders/WORK_ORDER_1675_heart_002_heartbound_state_and_persistence.RESULT.md` | **NEW.** This file. |

`git status --short` after the work, verbatim:

```
 M api/schema.sql
?? api/_lib/heartbound-state.js
?? api/migrations/20260910_0025_heartbound_state.sql
?? test/heartbound-state.test.js
```

(plus the two `WorkOrders/` paths above, written after that capture).

**Not one `.cs` file, `vercel.json`, `api/cron/*`, `api/game/save.js`, `api/_lib/heartbound-resonance.js`, tunable-manifest or `CLI_LANES_WO_NUMBERS.md` was opened for writing.** No Unity run, no gate, no commit, no push.

---

## 2. MEASURED OUTPUT — quoted, not summarised

### `node --test test/heartbound-state.test.js`

```
ℹ tests 24
ℹ pass 24
ℹ fail 0
```

(23 on the first run; a 24th was added after the defect in §2a was found.)

### 2a. ⚠ A DEFECT THIS LANE INTRODUCED AND THEN CAUGHT — recorded, not quietly fixed

Inserting the description block into `api/schema.sql` **duplicated both `CREATE INDEX` statements**:
the slice taken from the migration already carried them, and they were appended a second time. Read
back at `:2055-2070`, both index declarations appeared twice.

⛔ **It was invisible to every check that had run.** `IF NOT EXISTS` makes a duplicated index harmless
to APPLY, `SCHEMA_PARSE_OK` printed, and the "same CREATE TABLE body" test passed because its regex
stops at the table's closing `);`. It was caught by eye, on a line-number check — which is not a gate.
That is the exact shape of memory `idempotent-ddl-hides-a-stale-table`: idempotent DDL does not report
what it skipped.

Fixed (the duplicate pair removed; `grep -c idx_heartbound_state_stale_since` now returns **1** in each
file), and a **new test case** now pins it — *"every heartbound_state object is DECLARED EXACTLY ONCE
in each file"* — so the next author of a description block cannot repeat it silently.

### `node --check` on each new `.js`

```
NODE_CHECK_OK api/_lib/heartbound-state.js
NODE_CHECK_OK test/heartbound-state.test.js
```

(The two new non-`.js` files are `.sql` and `.md`; `node --check` does not apply.)

### The whole existing suite — `node --test test/*.test.js`

```
ℹ tests 524
ℹ pass 520
ℹ fail 4
```

(524 after the ff brought WO-1676's 21 resonance cases, which still pass 21/21 with this module
importing from them: `node --test test/heartbound-resonance.test.js` → `ℹ pass 21  ℹ fail 0`.)

**⚠ THE FOUR FAILURES ARE PRE-EXISTING AND ARE NOT THIS LANE'S. PROVEN, NOT ASSUMED.** The three new
files were moved out of the tree and `api/schema.sql` stashed, and the four failing files were re-run
at that baseline:

```
✖ the generated copy is LF with a trailing newline and no BOM            (test/admin.skus.view.test.js)
✖ the table is declared, and the migration that provisions it is additive only  (test/benefactors.test.js)
✖ the checked-in spine is byte-identical to a fresh derivation from the build registry (test/tunables-manifest.test.js)
✖ WebGL hides and runtime-blocks the app offline-download flow           (test/webgl-offline-content-surface.test.js)
ℹ tests 81
ℹ pass 77
ℹ fail 4
```

Same four names, same count, with this lane's work absent. They are somebody else's tickets; this lane
did not investigate them and does not claim them fixed.

### `node tools/schema-parity.mjs --expected-only`

```
  heartbound_state: 22 column(s)  checks: status{DORMANT|ACTIVE|STALE|UNSTAKING|DISCONNECTED|SUSPENDED}
SCHEMA_PARSE_OK
```

That line is the proof the new table is **visible to the parity gate**: `schema-parity.mjs` parses
`CREATE TABLE` bodies only and is structurally blind to `ALTER`-added columns
(`tools/run-migrations.mjs:42-44`), which is why every one of the 22 columns is in the CREATE body and
not one is added by `ALTER`.

### `node --test test/migrations.runner.test.js`

```
ℹ tests 22
ℹ pass 22
ℹ fail 0
```

The runner derives its file list from disk (`tools/run-migrations.mjs:26-28`), so migration 0024 is
reachable with no array to update — and the "no orphans" case proves it rather than my saying so.

---

## 3. WHAT WAS BUILT, AND THE EVIDENCE BEHIND EACH DECISION

### 3a. `heartbound_state` is a backend-owned table, and the save does **not** change

`api/migrations/20260910_0025_heartbound_state.sql` (the CREATE body) and its byte-identical twin in
`api/schema.sql` (section 23, `:1988-2063`). 22 columns, keyed `player_id TEXT PRIMARY KEY`.

The reasoning is the WO's own §0a, re-read at source this session:
- `api/game/save.js:70-72` — the guards are *"Anti-grief / anti-corruption ceilings, NOT a
  server-authoritative economy"*.
- `api/game/save.js:410-421` — the simulated systems reach the backend *"only inside the opaque save
  blob… It is a RECORD, NOT A CONTROL."*

So **`SaveSchema.CurrentVersion` DOES NOT BUMP** and `SaveMigrator.Steps` gains nothing (acceptance
criterion 5). Not one `.cs` file was opened for writing by this lane. A bump would have put
pulse-payment authority on the client-owned wire, which is the defect the ticket was raised to avoid —
recorded here so the next seat does not "fix" it by adding a field.

`test/heartbound-state.test.js` pins the absence directly: *"⛔ nothing here touches the client-authored
save blob or its schema version"* asserts the module's code lines match none of
`game_state|player_data|schema_version|SaveSchema`.

### 3b. The DDL is in a **migration**, because `api/schema.sql` never runs

`api/schema.sql:105-107` says so in the file itself, and the migration header repeats the reasoning at
`:1-13`. The schema.sql block is a DESCRIPTION. The two CREATE bodies are held identical by a test —
*"the migration and api/schema.sql carry the SAME CREATE TABLE body"* — because `schema-parity.mjs`
compares the live database against **schema.sql**, so the two disagreeing would mean the gate measures
one thing while production runs another.

### 3c. Two write paths, disjoint by construction (the Q2 ruling)

RULED 2026-09-10: **fail to last-known verified state**, never to zero.

- `recordVerifiedStake` (`api/_lib/heartbound-state.js`, the `INSERT … ON CONFLICT` upsert) is the only
  path that writes `last_verified_at_utc`, `last_actual_stake`, `effective_resonating_stake`,
  `resonance_score`, `resonance_tier`, `highest_lifetime_tier`, `current_tree_resonance_stage`.
- `recordVerificationFailure` is an **`UPDATE` that names none of them** — it writes only
  `status`, `last_verification_attempt_at_utc`, `last_verification_code`, `stale_since_utc`,
  `updated_at`.

The test *"⛔ the failure path names NOT ONE verified column, so it cannot zero a stake"* slices the
statement between `SET` and `WHERE` and asserts each of the ten forbidden column names is absent. That
is a structural guarantee, not a promise in a comment.

It also **does not INSERT**: a player who has never verified gets no row from an outage, because a row
born that way would claim an activation timestamp on no evidence — migration 0022's `DEFAULT 10`
mistake in a new costume. `recordVerificationFailure` returns `null` in that case and the test pins it.

`stale_since_utc` uses `COALESCE(heartbound_state.stale_since_utc, …)` so the grace window measures
**when the outage began**, not the latest retry; otherwise a client that retries forever never leaves
the window.

Today's client fails **closed** (`NativeSkrStakeQuery.cs:87-94`, cited from the triage doc — **that
line was NOT re-opened at source by this lane**, see §6). That is correct for a perk and wrong for a
streak, which is the whole of Q2.

### 3d. Grace-window fields, with the **length** deliberately absent

`toSnapshot` returns `lastVerifiedAtMs`, `staleSinceMs`, `lastVerificationCode`, plus
`graceWindowMs` / `graceExpiresAtMs` / `withinGraceWindow`. **No default window is hardcoded.** Pass
`graceWindowMs` and it answers; omit it and `withinGraceWindow` is `null`.

Why: Q-CONFIG ruling — Heartbound knobs live as **Command Center server-only rows**. A number baked
into this module would be a second copy of a tunable the owner cannot flip. `null` is the honest answer
to "has the window expired" when nobody has said how long it is. Pinned by *"withinGraceWindow is null
until somebody supplies the window length"*.

### 3e. `nextTierAt` is **served**, and this module still holds no ladder

`readHeartboundStatus(sql, playerId, { resolveTier, nowMs, graceWindowMs })` returns
`{ playerId, status, exists, nextTierAt, serverNowMs, state }`.

`resolveTier` is **injected**. With no resolver, `nextTierAt` is `null` — never inferred from the tier
number. This satisfies the Q-CONFIG sub-question (*the status endpoint should return `nextTierAt`
rather than the client re-deriving it from a copied table*) **without** this module becoming the second
copy itself. The test *"⛔ this module holds NO tier ladder, NO thresholds and NO resonance
mathematics"* asserts the code lines contain no `TIERS`/`THRESHOLD`/`LADDER` and no
`Math.pow|log|sqrt`.

`serverNowMs` rides along because the server owns the clock (`save.js:753-755`).

### 3f. BigInt-safe raw SKR

`NUMERIC(39,0)` columns (u128 max is ~3.4e38; `BIGINT` tops out at ~9.2e18), read `::text`, handled as
`BigInt` in JS. `toRawAmountText` accepts a `bigint` or a `/^\d+$/` string and **refuses a `number`** —
a raw position exceeds `Number.MAX_SAFE_INTEGER` at six decimals, so a `number` has already lost
precision on arrival and coercing it would put the rounding into a reward. The test asserts
`1e21` and `12345` both throw `/never a number/`, and that a `'9007199254740993000000'` stake survives
the snapshot exactly while `Number()` of it does not.

`resonance_score` is `NUMERIC(39,6)` and crosses the wire as **text**: WO-1676 owns the curve's scale
and an integer column would have decided it here by accident.

### 3g. `highest_lifetime_tier` is monotonic **in SQL**

```
highest_lifetime_tier = GREATEST(heartbound_state.highest_lifetime_tier,
                                 EXCLUDED.highest_lifetime_tier)
```

while `resonance_tier = EXCLUDED.resonance_tier` is a flat assignment, because the current bond is
allowed to weaken. Doing it in SQL means a caller that reaches the statement without reading first
still cannot lower it — the same two-defence shape `GREATEST(player_data.reset_epoch, …)` uses
(migration 0023).

### 3h. `activated_at_utc` is set once, for ever

`ON CONFLICT (player_id) DO UPDATE SET activated_at_utc = heartbound_state.activated_at_utc` — the
self-assignment re-asserts the stored value, in both the activation insert and the verified upsert. It
is the floor every WO-1677 pulse query filters on (spec `:242`, *"Do NOT retroactively award historical
Elarion pulses"*): moving it forward loses the player pulses, and moving it **backward** would let them
claim the chain's entire history.

### 3i. Pulse and echo ids are nullable `TEXT`

WO-1677 and WO-1678 have not designed their identifier shape. A guessed `BIGINT` would need an
`ALTER COLUMN` later, and memory `idempotent-ddl-hides-a-stale-table` is exactly about that class of
change reporting success while doing nothing.

---

## 4. WHAT WAS **NOT** DONE, AND WHY

### 4a. D3 — Wallet Change: **DROPPED as ruled, not implemented**

RULED 2026-09-10: **one wallet = one realm, no re-binding, no linkage table.** `api/schema.sql:60`
declares `player_id TEXT PRIMARY KEY -- BoundWallet address`; `api/_lib/wallet-auth.js:27-29` carries
the standing ruling. A different wallet is a different player with a different row.

Two consequences, both deliberate:
1. The spec's separate **`walletAddress` field is absent** — it would be a second copy of the primary
   key.
2. There is **no function that could move a row between two player ids**, and the test *"⛔ no function
   in this module can move a Heartbound row between two wallets"* pins the absence: no export matching
   `rebind|relink|transfer|migrateWallet|changeWallet`, no `oldWallet|newWallet|previousPlayerId` in the
   source, and no `wallet_address|linked_wallet|previous_wallet` column in the CREATE body. The absence
   is the deliverable, so it is guarded rather than left for a later reader to "restore".

This also rewrites **WO-1683 scenario 5** ("Wallet switching"), which cannot be tested as written. That
is the triage doc's own finding, restated here so the WO-1683 lane meets it.

### 4b. D4 — the client-side read-only snapshot: **NOT DONE**

It is a `.cs` deliverable and this lane was forbidden every `.cs` file. It is WO-1681's Play-boundary
work. The WO's own §0b notes that even a display cache costs no schema bump (a purely additive nullable
field needs no `Steps` entry, `SaveMigrator.cs:50-61`) and recommends PlayerPrefs over the synced save
so a stale cache cannot round-trip to Neon and read as authority. Nothing here forecloses either.

### 4c. `api/heartbound/status.js` — **NOT BUILT**

The brief asked for "a status read shape", which is `readHeartboundStatus`. The HTTP route belongs to
the panel lane, together with `authenticateGranting()` (`api/_lib/wallet-auth.js:829`, wallet-only,
guests always refused) — the auth choice is that lane's, not this one's.

### 4d. Q-LADDER is noted, not acted on

The ruling is "ladders merged". That is a decision about `stake-rewards.json` /
`StakeRewardsResolver` / `StakeRewardsPanel` — all `.cs` and JSON outside this lane. **The table needs
no change either way:** it stores a resolved `resonance_tier`, and whose ladder resolved it is the
resolver's business. No `stake-rewards.json` value was touched.

### 4e. `CLI_LANES_WO_NUMBERS.md` — not edited (the number was pre-assigned). `BOARD.html` — not
regenerated (the lead does that with the commit).

---

## 5. ACCEPTANCE CRITERIA, ANSWERED HONESTLY

| # | Criterion | Verdict |
|---|---|---|
| 1 | First verified stake creates the row and stamps `activated_at_utc` | **SHAPE PROVEN** — `activateHeartbound` / `recordVerifiedStake` INSERT with the column's `DEFAULT NOW()`; the conflict branch re-asserts the stored value. DB behaviour unproven (§6). |
| 2 | No pulse before `activated_at_utc` is processed | **NOT THIS TICKET.** The column and its immutability are provided; the pulse query that filters on it is WO-1677's. |
| 3 | `highest_lifetime_tier` monotonic while `resonance_tier` may fall — *"proven with two writes and a read"* | ⚠ **PARTIAL, AND NAMED AS SUCH.** The `GREATEST(...)` vs flat-assignment SQL shape is pinned by test. **The two-writes-and-a-read against Postgres was NOT performed** — see §6. |
| 4 | `MIGRATIONS_OK` on a fresh run, then a shape query | ⚠ **NOT DONE — owner-run.** See §6. |
| 5 | `SaveSchema.CurrentVersion` unchanged, and the RESULT says why | **DONE.** §3a. No `.cs` file was written; the reasoning is recorded in three places so it survives. |
| 6 | `**Status:**` flipped, `.RESULT.md` written, both paths reported | **DONE.** Paths at the foot of this file. |

---

## 6. ⛔ UNPROVEN, NAMED AS UNPROVEN (CLAUDE.md §11B)

1. **Nothing here has touched a database.** `DATABASE_URL` is redacted for every agent seat
   (`tools/run-migrations.mjs:6-7` says the script is written to be run by the owner and by nobody
   else). So: **`MIGRATIONS_OK` has not been seen, the shape query has not been run, and no row has
   ever been written.** A mocked `sql` tag proves the STATEMENT SHAPE and nothing about Postgres's
   behaviour. Acceptance 3 and 4 close when the owner runs, with `DATABASE_URL` in the environment:
   ```
   node tools/run-migrations.mjs          # judge by MIGRATIONS_OK applied=N skipped=M, never exit code
   node tools/schema-parity.mjs           # judge by SCHEMA_PARITY_OK
   ```
   then the shape query at the foot of the migration file (expect 22 rows) and the status `CHECK`
   query beside it. Judging by an exit code would be the mistake memory
   `gates-report-success-without-proving-it` records.

2. ⭐ **RETRACTED 2026-09-10 — THIS WAS A SNAPSHOT ARTIFACT, AND IT IS CLOSED.**
   `api/_lib/heartbound-resonance.js` **landed on `dev` at `cde1c1f63`** (WO-1676, HEART-003), one
   commit after the `c96030b5c` this lane first fast-forwarded to. The finding below was true when
   measured and false one commit later — recorded rather than deleted, because "I measured it and the
   tree moved" is the honest shape of it.

   **The duplicate is now GONE.** After a second `git merge --ff-only refs/heads/dev` (HEAD
   `cde1c1f63`), `heartbound-state.js` **imports** `toBigInt` from the resonance module
   (`heartbound-resonance.js:95`) and the hand-written coercion is deleted. Kept locally, and only
   these: `MAX_RAW_AMOUNT` (the u128 ceiling) and the non-negative check — **resonance has no
   equivalent for either**, and correctly so: both are properties of the `NUMERIC(39,0)` COLUMN, not of
   the curve, and `toBigInt` takes chain reads, which cannot be negative. `toRawAmountText` survives as
   the thin storage half: coerce with the shared rule, apply the two column constraints, narrow to the
   canonical digit string the DDL needs.

   ⚠ **ONE SEMANTIC CHANGE, STATED OUT LOUD.** This module's first draft refused **every** JS `number`.
   Resonance's rule is narrower — a **safe** integer is honoured, an unsafe or fractional one is refused
   (*"a u128 that arrived as a lossy double is not a number this module can honour"*). Adopting the
   shared rule therefore made `toRawAmountText(12345)` legal where it previously threw. That is the
   correct trade: one rule over one currency beats a second, stricter rule. The tests were updated to
   the shared boundary and now pin the **identity** of the import, so re-implementing the rule locally
   goes red.

3. ~~`api/_lib/heartbound-resonance.js` DOES NOT EXIST.~~ *(superseded by item 2; original text:)* The brief instructed that its BigInt types be
   read first and reused. Proven absent: `ls api/_lib | grep -i heart` → no match (exit 1), and
   `git log --all --oneline -- api/_lib/heartbound-resonance.js` → **no commits**, at `c96030b5c`. The
   WO-1676 lane presumably owns it and had not landed when this lane ran. **Consequence:** the
   raw-amount contract (`toRawAmountText` / `rawAmountFromRow` / `MAX_RAW_AMOUNT`) is defined **here**.
   ⚠ **ACTION FOR THE WO-1676 LANE: import these, do not author a second contract.** Two BigInt
   conventions over one currency is the duplicated-state failure this repo has four scars from.

4. **The triage doc contains no `RULED` blocks — re-checked, still zero.**
   `grep -c "RULED" docs/specs/HEARTBOUND_TRIAGE_2026-09-10.md` → **0** at `c96030b5c` AND again at
   `cde1c1f63`. The coordinator reports the rulings are **uncommitted in the main tree** and will
   arrive in a later fast-forward, which is consistent with what is measurable from here; section 3 still reads as open questions with lettered options. Every
   ruling this lane implemented came **from the lane brief**, not from the doc. ⚠ **The doc should be
   updated with the rulings** (CLAUDE.md §15 — update canon in the same breath) or the next seat reads
   the questions as still open and re-litigates Q-WALLET and Q2.

5. **Every line-number citation in this file HAS now been re-opened at source (2026-09-10).** The
   list below was originally recorded here as un-verified hearsay; each was then read and is quoted:
   - `Assets/_Modules/Core/State/SaveSchema.cs:41` → `public const int CurrentVersion = 41;`
     **Acceptance criterion 5 is therefore PROVEN, not assumed** — the value is still 41 and this lane
     did not touch it.
   - `api/game/save.js:70-72` → *"Anti-grief / anti-corruption ceilings, NOT a server-authoritative
     economy"*. `:410-421` → *"farming / raiding / dungeons / arena have NO per-action endpoint… reach
     this backend only inside the opaque save blob"*. `:753-755` → `serverNowMs: Date.now(),`.
   - `api/_lib/wallet-auth.js:27-29` → *"⛔ THE WALLET REMAINS THE SOLE IDENTITY ON THE SEEKER/APK
     ARTIFACT (owner ruling 2026-08-30)"*. `:829` → `async function authenticateGranting(sql, req,
     payload, claimedPlayerId) {`.
   - `api/schema.sql:60` → `player_id      TEXT        PRIMARY KEY,          -- BoundWallet address`.
   - ⚠ **CANON CORRECTION (CLAUDE.md §15): `NativeSkrStakeQuery.cs` IS NOT AT
     `Assets/_Modules/Core/Platform/`.** The triage doc and WO-1674 cite that path; `find` returns
     **`Assets/_Modules/Wallet/NativeSkrStakeQuery.cs`**, and the fail-closed branch is at **`:87-94`**
     of that file (`_known = false; _activeStake = 0;` then `FlowTrace.Warn("Stake", "native SKR stake
     read failed closed: "…)`). The BEHAVIOUR the Q2 ruling reverses is confirmed exactly as described;
     only the folder in the citation is wrong. A seat following the doc's path finds nothing.

6. **`git stash list` in this worktree** — this lane pushed and popped `api/schema.sql` once while
   baselining the four pre-existing failures. Verified afterwards that it left nothing behind: the six
   entries listed are all from OTHER worktrees (`worktree-agent-a9e001ddb25631dda` ×4,
   `worktree-agent-a1c87f271103479d0`, `feat/synty-art-retheme`). **No stash entry belongs to this
   lane**, so the lead's explicit-path reconciliation has nothing extra to untangle here.

7. ⚠ **HEART-001's `api/schema.sql` BLOCK IS NOT IN THIS TREE, AND MAY COLLIDE ON THE SECTION NUMBER.**
   `grep -c "skr_stake_snapshots" api/schema.sql` → **0** at `cde1c1f63`; the coordinator reports that
   table is ~70 lines living in WO-1674's own worktree, to be 3-wayed onto main. **This lane's block is
   labelled `-- 23.`** (20/21/22 were taken; see item 8). If HEART-001's block also claims 23 the
   3-way will merge cleanly — the numbering is a comment, so nothing fails — and the file will simply
   have two section 23s. **Flagged for whoever does the 3-way**, since no test or gate can see it.
   Per the coordinator's instruction, `api/schema.sql` was NOT touched again after the ff; this lane's
   diff against it remains `1 file changed, 77 insertions(+)`.

8. **⚠ SECTION NUMBER COLLISION, CAUGHT AND FIXED.** The `api/schema.sql` block was first labelled
   `-- 21.`; `grep -n "^-- [0-9]\+\. "` showed **21** (`public_town_showcases`) and **22**
   (`showcase contests`) already taken. Renumbered to **`-- 23.`**. Nothing enforces this numbering —
   it is a comment — so it is recorded rather than silently corrected.

9. **`tools/board_build.py` understands the status word.** `:187` — `if lead in ("DONE",
   "IMPLEMENTED", "COMPLETE"): return "Done", False`. The flipped `**Status:**` line will land the WO
   in the Done bucket when the lead regenerates the board.

10. **The rest of the reading, listed so it is auditable.** Also opened at source this session:
   `api/schema.sql` (:1-20, :55-110, :960-1000, :1130-1160, tail), `api/_lib/patronage.js`,
   `test/patronage.test.js`, `tools/run-migrations.mjs:1-60`, `tools/schema-parity.mjs:1-130`,
   `test/migrations.runner.test.js`, `api/migrations/20260907_0023_player_data_reset_epoch.sql`,
   `package.json` scripts, `tools/board_build.py`, and both HEARTBOUND spec docs in full.
   ⚠ **The ONE citation in this file still NOT opened is `SaveMigrator.cs:50-61`** (quoted in §4b for
   the "an additive nullable field needs no `Steps` entry" claim). It is `.cs`, it changed nothing this
   lane built, and it is named here as unverified rather than left to read as fact.

11. **The four pre-existing suite failures were baselined, not diagnosed.** They are named in §2 with
   their files. This lane did not investigate them.

---

## 7. PATHS

- Work order: `WorkOrders/WORK_ORDER_1675_heart_002_heartbound_state_and_persistence.md`
- This result: `WorkOrders/WORK_ORDER_1675_heart_002_heartbound_state_and_persistence.RESULT.md`

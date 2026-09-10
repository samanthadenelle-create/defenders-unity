# WORK ORDER 1685 — HEART-012 — RESULT

**Status:** IMPLEMENTED (the MAP half) — 2026-09-10
**Lane:** HEART-012 SME, worktree `.claude/worktrees/agent-a1a691a2e742db7be`, branch `dev`
**Tree at start:** `git merge --ff-only refs/heads/dev` → `0b942d0be` (HEART-011/WO-1684 telemetry)
**Committed by this lane:** NOTHING. No commit, no Unity, no `.cs`. The lead commits.

---

## 1. What landed

| Path | Change |
|---|---|
| `WorkOrders/WORK_ORDER_1685_heart_012_regression_and_automated_test_package.md` | **§7 "Coverage map"** added (39 rows + §7.1 invariants + §7.2 scope honesty); `**Status:**` flipped |
| `test/heartbound-suite.test.js` | **NEW** — 10 cases: 4 that assert the map, 6 cross-module invariants |

**No existing test or module was edited.** `git status --short` at hand-back:

```
 M WorkOrders/WORK_ORDER_1685_heart_012_regression_and_automated_test_package.md
?? test/heartbound-suite.test.js
```

**Untouched, as instructed:** `CLI_LANES_WO_NUMBERS.md`, `api/schema.sql`, every `.cs`,
`DataRegression.cs`, and the in-flight lanes' files (`heartbound-tiers.js`, `heartbound-events.js`,
the WO-1693 contract fix in `pulse.js`/`state.js`).

---

## 2. Run the whole Heartbound package in one line

```
node --test test/heartbound-*.test.js test/skr-staking.test.js
```

The new suite is named `heartbound-suite.test.js` **precisely so the existing glob picks it up** — no
new run line, no `test/README` (none exists; the line lives in the WO's §7, which is also the file the
suite parses). `package.json:10` (`node --test test/*.test.js`) covers it too.

---

## 3. Evidence — measured this session, not inferred

### 3.1 GREEN, the whole package

```
$ node --test test/heartbound-*.test.js test/skr-staking.test.js
ℹ tests 136
ℹ suites 0
ℹ pass 136
ℹ fail 0
ℹ cancelled 0
ℹ skipped 0
ℹ todo 0
ℹ duration_ms 801.833
```

Baseline before this lane (same command, without the new file): **tests 126 / pass 126 / fail 0**.
136 − 126 = the 10 new cases. Nothing existing moved.

The ten new cases, all green:

```
✔ the coverage map covers EVERY criterion HEART-012 lists, derived from the spec itself
✔ every case the map cites is PRESENT, by exact name, in the file it names
✔ every UNCOVERED row names an owning work order that exists on disk
✔ the package run line in the work order is the one that actually runs these files
✔ ⛔ no client-readable file under Assets/ carries a Heartbound tier NAME
✔ ⛔ no client-readable file under Assets/ carries the resonance THRESHOLDS or the curve knobs
✔ ⛔ no backend .js under api/ contains a NUL byte (the CompileGate guard is .cs-only)
✔ every api/_lib/heartbound-*.js and both Heartbound routes pass node --check
✔ ⛔ no api/ file is NAMED as a pulse-injection route (Q-INJECT: test builds only)
✔ ⛔ no live line of api/ .js references a pulse-injection route name
```

### 3.2 RED BEFORE GREEN — §0d's requirement, twice

**(a) The map detector bites on a renamed case.** One character changed in the WO's I2/E2 rows
(`...IDL offsets` → `...IDL offsetz`), nothing else:

```
✖ every case the map cites is PRESENT, by exact name, in the file it names
  AssertionError [ERR_ASSERTION]: I2: test/skr-staking.test.js has no case named exactly:
    UserStake decodes at the IDL offsetz
  (a renamed case must red this map, not slip through)
ℹ pass 9  ℹ fail 1
```

The WO was then restored byte-for-byte from a copy taken before the edit and re-ran green.

**(b) Both ladder detectors bite on a planted ladder.** A throwaway
`Assets/Resources/Data/Canonical/__tmp_ladder_probe.json` carrying three tier names and four
thresholds, staged with `git add -N` so `git grep` could see it:

```
✖ ⛔ no client-readable file under Assets/ carries a Heartbound tier NAME
  AssertionError: a Heartbound tier name is readable by the client:
✖ ⛔ no client-readable file under Assets/ carries the resonance THRESHOLDS or the curve knobs
  AssertionError: these files co-occur three or more resonance thresholds — that is a ladder:
ℹ pass 8  ℹ fail 2
```

Probe removed from the index and from disk; `git status --short` verified clean afterwards (quoted in
§1).

**(c) The completeness check bites on a DELETED row.** The `E4` row was removed from the map, nothing
else:

```
✖ the coverage map covers EVERY criterion HEART-012 lists, derived from the spec itself
  AssertionError: spec criteria at these lines of
  docs/specs/HEARTBOUND_SKR_RESONANCE_WORK_ORDERS_2026-09-10.md appear in NO map row: 1143
ℹ pass 9  ℹ fail 1
```

⭐ That criterion set is **parsed out of the spec** (the `- ` bullets and `N. ` steps between
`# HEART-012` and `# HEART-013`), not written into the suite. No literal "37" exists anywhere in the
test — a criterion added to the spec reds the map until a row cites its line. WO restored and re-ran
green.

### 3.3 The invariants, as raw measurements

- **Tier ladder absent from `Assets/`.** `git grep -l -i -F` over tracked `Assets/*.{cs,json,txt,csv,asset,md}`
  for the seven coined tier names (`Emberbound`, `Rootbound`, `Stonebound`, `Hearttouched`,
  `Heartforged`, `Eternal Echo`, `Deep Resonance`) → **exit 1, zero hits**. Per-file co-occurrence of
  the four highest thresholds (2150/2500/2850/3200) → **max 1 distinct per file**, so the ">= 3 is a
  ladder" rule has real headroom. Config keys (`minHeartboundStake`, `activationFraction`,
  `pulseRampFraction`, `tenurePower`, `stakePower`, `highestLifetimeTier`) → zero hits.
- **NUL bytes.** 0 of the tracked `api/**/*.js` files contain `\x00`.
- **`node --check`.** All four `api/_lib/heartbound-*.js` plus `api/heartbound/status.js`,
  `api/cron/heart-pulse.js`, `api/_lib/skr-staking.js`, `api/_lib/solana-pda.js` parse.
- **Pulse injection.** No `api/` basename matches
  `(inject|force|simulate|debug|demo|seed|fake)[-_]?pulse`; **one** repo-wide textual hit exists and it
  is a COMMENT stating the ruling (`api/_lib/heartbound-pulse.js:44`), which the comment-stripping pass
  correctly ignores. That comment is why the check strips comments rather than grepping raw.

### 3.4 Wider repo suite — 4 PRE-EXISTING failures, none mine

```
$ node --test test/*.test.js
ℹ tests 615  ℹ pass 611  ℹ fail 4
```

The four live in `test/admin.skus.view.test.js`, `test/benefactors.test.js` and
`test/tunables-manifest.test.js` (generated-copy LF/BOM, a migration-additive check, a spine
byte-identity check). They are unrelated to Heartbound and to the two files this lane touched — this
lane added a read-only test file and a markdown section. **I did not re-run them at the pre-merge
commit, so "pre-existing" is argued from disjointness, not from a before/after run** — flagged rather
than asserted.

---

## 4. The UNCOVERED list — what the map says is NOT proven today

**11** of the 39 rows are `UNCOVERED` (`grep -c '| UNCOVERED |'` on the WO → `11`), each naming an
owning WO that exists on disk (asserted by the suite). This is the honest shape of HEART-012 while five
lanes are still open. Ids: **U11, U13, I1, I3, E1, E4, E10, E11, E12, E13, E15.**

| ID | Criterion | Why | Owner |
|---|---|---|---|
| U11, E10 | deterministic Echo Event generation / Echo Event generated | the engine does not exist on `dev`; `test/heartbound-events.test.js` is not on disk | WO-1678 |
| U13 | reward caps | the passive-economy ceiling has no meter yet | WO-1682 |
| I1 | StakeConfig account mock | `skr-staking.js:247` exports `decodeStakeConfig` and **no case decodes one** — a real hole in landed code | WO-1674 |
| I3 | no UserStake account | `ACCOUNT_NOT_FOUND` is in `ALL_STATUSES` and no case produces it; `WALLET_NOT_LINKED` is a different state | WO-1674 |
| E1 | player links wallet | no linking-flow case in the package | WO-1675 |
| E4, E12 | Tree changes state / Tree pulses | client presentation — headless AutoPilot fleet, not `node --test` | WO-1680 |
| E11, E13 | player logs in / Echo Event appears | client presentation — AutoPilot fleet | WO-1681 |
| E15 | reopening cannot duplicate reward | E14 proves the DB `ON CONFLICT` gate only; a re-open that never reaches the DB is not covered by it | WO-1681 |

Two rows are covered but **limited**, and the map says so on the row rather than in prose:

- **I7 (RPC timeout)** — `ACCEPTANCE 4: an RPC outage NEVER reports the stake as zero` builds an
  `RPC_UNAVAILABLE` record by hand. **No transport timeout is simulated.** The socket half is not
  proven by anything in this package.
- **I4 (stake amount increase)** — proven at the arithmetic layer (`[ramp] a top-up does NOT move
  effective stake until the next pulse`). No mocked account returns the higher stake.

---

## 5. ⛔ NOT DONE, and not claimed — the rest of §2 Acceptance

This lane delivered the **map** and the `node --test` half only. Explicitly open:

1. **Acceptance 1 — `REGRESSION_OK <n>/<n> suites` on a fresh log:** NOT RUN. No Unity in this lane.
2. **Acceptance 2 — every new suite REGISTERED in `DataRegression.RunAll`:** NOT DONE. No `.cs` was
   written, so there is nothing to register; the `Guard.Try` line is still owed by whoever writes the
   EditMode/DataRegression half.
3. **Acceptance 3 — RED before green:** DONE **for what this lane shipped** (§3.2). It is NOT done for
   any Unity suite, because none was written.
4. **Acceptance 5 — `TESTS_OK` from `run-tests.ps1` / the NUnit XML:** NOT RUN.
5. **Acceptance 6 — `COMPILE_GATE_OK`, `tools/gate_brace.py`, NUL scan on touched `.cs`:** N/A —
   **zero `.cs` touched**. (The suite's own NUL invariant covers the `.js` side, which no gate did.)
6. **§0b's open choice — `Assets/Data/Tests/` vs `Assets/Tests/EditMode/`** — still unanswered. This
   lane did not pick, because it wrote no EditMode case and picking without writing one would be
   another claim-before-existence.

---

## 6. ⭐ FINDING — one of §0d's three imaginary gates has since become real

WO-1685 §0d records `StakingComplianceRegression` as *"3 hits, all prose, no definition"*. **On `dev`
today it exists**: `Assets/Editor/Regression/StakingComplianceRegression.cs`, 22140 bytes, introduced
by `6340fae5a` *("the SKR stake read moves to the backend … HEART-001; WO-1674")*. So WO-1674 D5 closed
it. §0d is therefore **stale on its first bullet** and is deliberately left unedited here — the RESULT
is the correction record, and the WO body is the ticket as raised (CLAUDE.md §15: frozen point-in-time
text gets a banner, not a rewrite). **I did NOT verify whether it is registered in
`DataRegression.RunAll`** — existence is not registration (§0c), and that check needs the file this
lane must not touch. That is the one cheap thing the next Unity seat should confirm.

---

## 7. Unproven / not measured, named as such

- The four wider-repo failures are argued unrelated by disjointness, not by a before/after run (§3.4).
- Binary `Assets/**` files (`.unity`, `.prefab`, `.asset` binaries, FBX) are **not scanned** by the tier
  ladder invariant — it reads tracked TEXT extensions only. A ladder serialised into a binary scene
  would not be caught.
- The ladder detector reads tracked files only. An **untracked** file under `Assets/` carrying the
  ladder would pass — deliberate (untracked files do not ship), but it is a limit, not an absence.
- Whether `StakingComplianceRegression` is registered in `DataRegression.RunAll` (§6).
- Nothing here proves the Heartbound routes work against a live Neon or a live RPC. Every case in this
  package is offline and deterministic, by the same choice `test/skr-staking.test.js:6-10` records.

---

## 8. Paths

- Work order: `WorkOrders/WORK_ORDER_1685_heart_012_regression_and_automated_test_package.md`
- Result: `WorkOrders/WORK_ORDER_1685_heart_012_regression_and_automated_test_package.RESULT.md`
- New test: `test/heartbound-suite.test.js`

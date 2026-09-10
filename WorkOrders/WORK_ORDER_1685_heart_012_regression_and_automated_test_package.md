# WORK ORDER 1685 — HEART-012: Regression and automated test package

**Status:** IMPLEMENTED 2026-09-10 — the test MAP (§7) landed with `test/heartbound-suite.test.js` asserting it (136/136 green, map + ladder detectors proven RED first); the EditMode NUnit half and the `DataRegression` registration (acceptance 1/2/3-Unity/5/6) are NOT done and are listed as open in the RESULT.
**Silo:** Test infrastructure only — `node --test`, EditMode NUnit, one `DataRegression` registration line. No gameplay, no economy, no scene files.
**Raised by:** HEARTBOUND-TRIAGE lane, 2026-09-10.
**Number:** PRE-ASSIGNED by the coordinator. `CLI_LANES_WO_NUMBERS.md` deliberately **NOT** edited by this lane.
**Spec section:** `docs/specs/HEARTBOUND_SKR_RESONANCE_WORK_ORDERS_2026-09-10.md:1101-1161` (HEART-012).
**Tree:** worktree at `dev` **`abbeb9362`**.

---

## 0. Classification — **NEW tests on THREE existing frameworks, all fully determined**

READY because every framework, marker, registration site and authoring pattern was read at source. ⚠ **Its CONTENT is gated on WO-1674..1683 existing** — this ticket defines *where each case lives and how it is registered*, which is work that can be done now and prevents thirteen lanes each inventing their own answer.

### 0a. Framework 1 — backend unit/integration: `node --test`
`package.json:10` — `node --test test/*.test.js`. **42 existing test files** under `test/`. `api/events/track.js:113` shows the house DI pattern (`makeHandler(deps)`) that makes a route testable without a live DB — **copy it for every Heartbound route**, because the alternative is integration tests that need Neon.

### 0b. Framework 2 — Unity EditMode NUnit: `Assets/Data/Tests/`
`Assets/Data/Tests/DeNelle.Data.Tests.asmdef` — Editor-only, `overrideReferences: true` with `precompiledReferences: ["nunit.framework.dll", "Newtonsoft.Json.dll"]`, `autoReferenced: false`, `defineConstraints: ["UNITY_INCLUDE_TESTS"]`. Worked shape: `Assets/Data/Tests/AbilityCatalogTest.cs:12-13` (usings), `:17-18` (`[TestFixture]`), `:19-24` (`[SetUp]` reloads the catalog), `:29` (`[Test]` with a **sentence** as the assert message).
Runner: `run-tests.ps1` at repo root. ⛔ `:8-10` states the law: **judge from the NUnit results XML (`test-run result="Passed"`, `failed=0`), NOT the exit code.** Marker `TESTS_OK`. Unity pinned `6000.4.8f1` (`:26-27`).
⚠ There is a second EditMode location — `Assets/Tests/EditMode/` (e.g. `StakeRewardsVMTests.cs`). **Pick one and say which; do not scatter Heartbound cases across both.**

### 0c. Framework 3 — the build gate: `DataRegression`
`Assets/Editor/Regression/` holds **513** suite files. ⛔ **There is no registration array and no attribute.** `DataRegression.RunAll` (`:56`) is one long sequential body and registration is literally **one `Guard.Try` line per suite** — **398** of them, from `:396` to `:2002`. The canonical row, `DataRegression.cs:1503`:

```csharp
DeNelle.Core.Diagnostics.Guard.Try("Regression", "heartfire-pips suite", () => { if (!DeNelle.Editor.Regression.HeartfirePipsRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[heartfire-pips] " + r); });
```

⭐ **`DataRegression.cs:1505-1511` records why this matters, in its own words: a commit added fourteen suites and registered four — *"A suite that is not registered is not coverage; it is a file."*** That sentence is this ticket's whole reason for existing as a separate WO.

Markers (`DataRegression.cs:14-25`, quoted): `DataRegression.RunAll → REGRESSION_OK <n>/<n> suites` (**the** gate) · `RegressionSuite.RunAll → CHECKIN_SUITE_OK <p>/<n> cases` · `SessionRegression.RunAll → SESSION_GUARDS_OK` · `CompileGate.Run → COMPILE_GATE_OK`. The expected suite count is **derived, never a literal** — `DataRegression.cs:2046` calls `RegressionMarkerRegression.TryGetExpectedSuiteCount` (`RegressionMarkerRegression.cs:1062`, rationale `:1048`).

Run: `run-unity-method.ps1 -Method DeNelle.Editor.DataRegression.RunAll -LogName data-regression.log` (`DataRegression.cs:7`).

### 0d. ⛔ Three suites in this feature's own neighbourhood have been CLAIMED and NEVER EXISTED
`StakingComplianceRegression` — cited at `Assets/_Modules/Core/FeatureFlags.cs:1008` and in two WOs; `grep -rn` returns **3 hits, all prose, no definition**.
> ⚠ **STALE 2026-09-10 (WO-1685 lane):** this first bullet is no longer true. `Assets/Editor/Regression/StakingComplianceRegression.cs` **exists on `dev`** (22140 bytes), landed by `6340fae5a` (HEART-001 / WO-1674 D5). ⛔ Existence is NOT registration — whether it carries a `Guard.Try` row in `DataRegression.RunAll` was **not** verified by this lane (it must not touch `.cs`). The other two bullets were not re-checked.
`JewelPolishRegression` — cited at `JewelPolishService.cs:425`; **1 hit, its own claim** (WO-1673 §0 item 1).
`SkrStakingRegression` — cited in `skr_staking.json`'s `_comment`; never existed, conceded in `MonetizationCovenantRegression`'s own header.

⛔ **Three imaginary gates in one feature area is a pattern, not a coincidence, and this ticket is where it stops.** Every regression HEART-012 promises must be **registered** and **proven RED before green**, and the RESULT must quote the RED run.

---

## 1. Deliverables — the test map (the real output of this ticket)

| Spec case | `:1105-1120` unit | Lives in | Owner WO |
|---|---|---|---|
| raw SKR conversion, share→token | ✔ | `test/heartbound-resonance.test.js` | 1676 |
| effective-stake ramp, immediate reduction | ✔ | same | 1676 |
| StakePower curve, TenurePower, max tenure | ✔ | same | 1676 |
| resonance thresholds, tier up/down | ✔ | same | 1676 |
| deterministic Echo Event generation | ✔ | `test/heartbound-events.test.js` | 1678 |
| idempotent pulse processing | ✔ | `test/heartbound-pulse.test.js` | 1677 |
| reward caps | ✔ | `test/heartbound-events.test.js` + the WO-1682 gate | 1682 |
| stale state | ✔ | `test/heartbound-pulse.test.js` | 1677 |

**Integration mocks** (spec `:1122-1136`): StakeConfig account, UserStake account, **no** UserStake account, stake increase, unstaking state, share-price increase, RPC timeout, malformed RPC response. ⭐ Fixture source: `Assets/_Modules/Wallet/NativeSkrStakeQuery.cs:30-31` carries the real Anchor discriminators and `:64`/`:73` spell out the byte budgets — **build the fixtures from those, so a mock cannot drift from the real layout.**

**E2E** (spec `:1138-1156`) — the fifteen-step scenario. ⚠ Steps 11-13 (player logs in → Tree pulses → Echo Event appears) are **client presentation** and belong on the headless AutoPilot fleet (`run-defenders` skill), not in `node --test`. Steps 1-10 and 14-15 are backend. **Split them and say so** — an "E2E" that silently omits the presentation half is the kind of coverage claim §0d is about.

**D-REG** — the `Guard.Try` registration line(s) in `DataRegression.RunAll`, in the same commit as the suite.

⚠ **Do not duplicate WO-1683's cases.** Split: **1685** owns the arithmetic and pipeline coverage; **1683** owns the adversarial scenarios. Both files should name the other.

---

## 2. Acceptance (spec `:1158-1161`: *"No implementation is complete until regression is green"*)

1. `REGRESSION_OK <n>/<n> suites` on a **FRESH** log — judged by the **marker**, never the exit code (CLAUDE.md §8; memory `gates-report-success-without-proving-it`). ⚠ Unity logs are UTF-16 — read with PowerShell and judge by the `^REGRESSION_(OK|FAIL)` line, not the runner's PASS (memory `unity-logs-are-utf16-read-with-powershell`).
2. **Every new suite is REGISTERED** in `DataRegression.RunAll`, and the suite count in the marker rises by exactly the number added. §0c — an unregistered suite is a file.
3. **Every new suite proven RED before green**, with the RED run quoted in the RESULT. §0d.
4. `node --test` passes, judged by the runner's own result line.
5. `TESTS_OK` from `run-tests.ps1`, judged from the NUnit results XML (`test-run result="Passed"`, `failed=0`), never the exit code (`run-tests.ps1:8-10`).
6. `COMPILE_GATE_OK` + `python tools/gate_brace.py` clean + zero NUL bytes on every touched `.cs`. ⚠ The gate's brace scanner has **no interpolated-string model** — a `"` inside a `$"...{ c ? "a" : "b" }..."` hole ends the string for it, so a file can read balanced raw and unbalanced at the gate (CLAUDE.md §1; memory `gate-brace-rule-and-detached-unity-runs`). Compute nested-quote parts into locals first.
7. `**Status:**` flipped in this file in the same commit as the work; `.RESULT.md` written; both paths reported.

---

## 3. Dependencies

**Blocked by:** WO-1674 through 1683 for *content*. Not blocked for the **map** (§1), which is this ticket's deliverable and should land first so thirteen lanes share one answer.
**Related:** WO-1683 (adversarial half), WO-1682 (the reward-cap assertion), WO-1673 D1/D2 (the two neighbouring imaginary-gate fixes).

---

## 4. OWNER QUESTIONS

**None.** All three frameworks, their markers, their registration sites and their authoring patterns were read at source.

⚠ **One process finding worth surfacing rather than a question:** `StakingComplianceRegression` has been cited as live protection at `FeatureFlags.cs:1008` for weeks and **has never existed** (§0d). It is the pin named on the compile-off of `StakingPolishBonus` for Google Play — i.e. the citation sits on the most compliance-sensitive flag in the repo. **Closing it is WO-1674 D5**; it is repeated here because HEART-012 is where "the suite exists" is supposed to become verifiable.

---

## 5. What NOT to touch

- ⛔ **`RegressionMarkerRegression.TryGetExpectedSuiteCount`.** The count is **derived** precisely so nobody hardcodes it (`RegressionMarkerRegression.cs:1048,1062`). Do not add a literal.
- ⛔ **The distinct markers.** `DataRegression.cs:14-25` records that until 2026-08-02 three classes printed a bare `REGRESSION_OK` and the check-in gate ran the wrong one unnoticed. Do not add a fourth `REGRESSION_OK` emitter.
- ⛔ **Registering a suite twice.** `DataRegression.cs:343` carries the warning verbatim: *"REGISTERED EXACTLY ONCE. Do not add a second line for it near the end fence."*
- ⛔ **`Assets/Data/Tests/DeNelle.Data.Tests.asmdef`'s `defineConstraints`/`overrideReferences`.** They are why EditMode tests compile at all.
- ⛔ **Claiming a suite in a comment before it exists.** §0d — three times is enough.
- No gameplay, no economy, no `.unity` scene files, no `SaveSchema` change.

---

## 6. Evidence index (opened 2026-09-10 at `dev` `abbeb9362`)

`package.json:10`; `test/` (42 files); `api/events/track.js:113` (the DI pattern)
`Assets/Data/Tests/DeNelle.Data.Tests.asmdef`; `Assets/Data/Tests/AbilityCatalogTest.cs:12-13,17-18,19-24,29`; `run-tests.ps1:3-5,8-10,16,19,26-27`
`Assets/Editor/Regression/DataRegression.cs:7,14-25,56,332,343,396,1503,1505-1511,2002,2046,2100,2109,2114`; `RegressionMarkerRegression.cs:1048,1062`
`Assets/_Modules/Wallet/NativeSkrStakeQuery.cs:30-31,64,73` (the fixture source)
`Assets/_Modules/Core/FeatureFlags.cs:1008`; `Assets/_Modules/Village/Crafting/JewelPolishService.cs:425`; `Assets/StreamingAssets/Data/Canonical/skr_staking.json:1` — **the three imaginary-gate citations, §0d**

---

## 7. Coverage map — THE DELIVERABLE (landed 2026-09-10)

**Run the whole Heartbound backend package in one line:**

```
node --test test/heartbound-*.test.js test/skr-staking.test.js
```

⛔ **THIS TABLE IS THE ONE AUTHORITY AND IT IS MACHINE-CHECKED.**
`test/heartbound-suite.test.js` **parses this section out of this file** and asserts, for every row
below: the file exists, the file is matched by the run line above, and the case name is present
**verbatim** in that file. **Rename a case and this map goes RED.** There is deliberately no second
copy of the table inside the test — a copy is the duplicated-state failure CLAUDE.md §2/§5/§8/§16 each
record a scar from, and §0d is the same failure wearing a suite name.
Every `UNCOVERED` row must name an owning WO that exists under `WorkOrders/` — that is asserted too, so
"UNCOVERED" can never be a place a criterion goes to be forgotten.

Ids: `U` = spec Unit Tests (`:1108-1121`), `I` = Integration mocks (`:1127-1134`), `E` = the E2E
scenario steps (`:1140-1154`). Row ids are unique; a criterion needing two cases splits into `a`/`b`.

| ID | Spec criterion (line) | Test file | Case name (exact) | Owner WO | What it proves / limit |
|---|---|---|---|---|---|
| U1 | raw SKR conversion (:1108) | test/heartbound-resonance.test.js | `[precision] raw base units convert without the client whole-SKR truncation (NativeSkrStakeQuery.cs:76)` | WO-1676 | base units to SKR without the client's whole-SKR truncation |
| U2 | share-to-token conversion (:1109) | test/skr-staking.test.js | `active stake = shares x share_price / 1e9, in BASE UNITS` | WO-1674 | pinned to real mainnet values read 2026-09-10 |
| U3 | effective stake ramp (:1110) | test/heartbound-resonance.test.js | `[ramp-up] increases are GRADUAL - activation grants 25%, each pulse closes 25% of the gap` | WO-1676 | the ramp-up half of the asymmetry |
| U4 | immediate stake reduction (:1111) | test/heartbound-resonance.test.js | `[ramp-down] decreases are IMMEDIATE - no pulse, no delay (spec :296-298)` | WO-1676 | the ramp-down half |
| U5 | StakePower curve (:1112) | test/heartbound-resonance.test.js | `[curve] StakePower is strictly increasing and concave across the knees` | WO-1676 | monotone + concave; the anti-whale property |
| U6 | TenurePower (:1113) | test/heartbound-resonance.test.js | `[tenure] TenurePower ramps from 0 and caps at exactly 700, at and beyond the cap` | WO-1676 | the ramp from zero |
| U7 | resonance thresholds (:1114) | test/heartbound-resonance.test.js | `[tiers] every threshold in the config ladder is the exact boundary - one point under is the tier below` | WO-1676 | every boundary, both sides |
| U8 | tier upgrades (:1115) | test/heartbound-resonance.test.js | `[tenure] tenure alone can raise a tier, and a fully-tenured 1,000 SKR position sits at IV` | WO-1676 | an upgrade driven by tenure alone |
| U9 | tier downgrades (:1116) | test/heartbound-resonance.test.js | `[downgrade] a stake decrease lowers resonanceTier and leaves highestLifetimeTier untouched (spec :371-375)` | WO-1676 | downgrade + the monotone lifetime tier |
| U10 | max tenure (:1117) | test/heartbound-resonance.test.js | `[tenure] TenurePower ramps from 0 and caps at exactly 700, at and beyond the cap` | WO-1676 | SAME case as U6 — it asserts the cap AT and BEYOND, which is this criterion |
| U11 | deterministic Echo Event generation (:1118) | UNCOVERED | the Echo Event engine does not exist on `dev` yet | WO-1678 | HEART-005 lane; `test/heartbound-events.test.js` is not on disk |
| U12 | idempotent pulse processing (:1119) | test/heartbound-pulse.test.js | `acceptance 1: running the detector twice over the SAME chain state mints nothing the second time` | WO-1677 | detector-side idempotency; the grant-side twin is E14 |
| U13 | reward caps (:1120) | UNCOVERED | the passive-economy ceiling has no meter yet | WO-1682 | HEART-009 lane; Q-METER ruled but unimplemented |
| U14 | stale state (:1121) | test/heartbound-state.test.js | `the last-known verified snapshot survives an outage untouched` | WO-1675 | Q2's "fail to last-known verified state", at the snapshot |
| I1 | StakeConfig account (:1127) | UNCOVERED | `skr-staking.js:247` exports `decodeStakeConfig` and **no case decodes one** | WO-1674 | the UserStake twin is covered (I2); this one is a real hole |
| I2 | UserStake account (:1128) | test/skr-staking.test.js | `UserStake decodes at the IDL offsets` | WO-1674 | fixture built at the real Anchor discriminator + offsets |
| I3 | no UserStake account (:1129) | UNCOVERED | `ACCOUNT_NOT_FOUND` is in `ALL_STATUSES` and no case produces it | WO-1674 | `WALLET_NOT_LINKED` is a DIFFERENT state — not a substitute |
| I4 | stake amount increase (:1130) | test/heartbound-resonance.test.js | `[ramp] a top-up does NOT move effective stake until the next pulse - the flash-stake is dead` | WO-1676 | arithmetic level only — no mocked account returns the higher stake |
| I5 | unstaking state (:1131) | test/skr-staking.test.js | `unstaking readiness is timestamp + cooldown, not the timestamp alone` | WO-1674 | readiness helper at both cooldown edges |
| I6 | share-price increase (:1132) | test/heartbound-pulse.test.js | `acceptance 1: a real advancement past the cadence floor mints exactly one pulse` | WO-1677 | an advancing price through an injected reader |
| I7 | RPC timeout (:1133) | test/skr-staking.test.js | `ACCEPTANCE 4: an RPC outage NEVER reports the stake as zero` | WO-1674 | LIMIT: a hand-built `RPC_UNAVAILABLE` record, not a transport timeout — the socket half is UNCOVERED |
| I8a | malformed RPC response (:1134) | test/skr-staking.test.js | `an account of the wrong size is refused (a program upgrade must not decode)` | WO-1674 | decode refusal on a wrong-size account |
| I8b | malformed RPC response (:1134) | test/heartbound-pulse.test.js | `a malformed stake snapshot is treated as UNVERIFIED — pending, never a zero-stake payout` | WO-1677 | the downstream half: malformed never becomes a zero-stake payout |
| E1 | player links wallet (:1140) | UNCOVERED | no linking-flow case exists in the package | WO-1675 | Q-WALLET: one wallet, one realm — HEART-002 owns the bind |
| E2 | native SKR stake detected (:1141) | test/skr-staking.test.js | `UserStake decodes at the IDL offsets` | WO-1674 | detection at the decode layer (offline; the live-chain read is in the 1674 RESULT) |
| E3 | Heartbound activates (:1142) | test/heartbound-state.test.js | `activation is idempotent and can never re-stamp activated_at_utc` | WO-1675 | activation, and that re-activation cannot move the clock |
| E4 | Tree changes state (:1143) | UNCOVERED | client presentation — headless AutoPilot fleet, not `node --test` | WO-1680 | HEART-007; no stage system exists yet |
| E5 | share-price advancement detected (:1144) | test/heartbound-pulse.test.js | `acceptance 1: a real advancement past the cadence floor mints exactly one pulse` | WO-1677 | SAME case as I6 — detection and mint are one step in this design |
| E6 | Heart Pulse created (:1145) | test/heartbound-pulse.test.js | `a full run mints once, processes every eligible player, and closes the pulse COMPLETE` | WO-1677 | mint through to COMPLETE across a roster |
| E7 | player eligibility verified (:1146) | test/heartbound-pulse.test.js | `a stake below the HEART-003 eligibility floor grants nothing` | WO-1677 | the floor; the activation-date gate is E7b |
| E7b | player eligibility verified (:1146) | test/heartbound-pulse.test.js | `acceptance 5: a pulse dated BEFORE activated_at_utc is not processed (no retroactive awards)` | WO-1677 | no retroactive award for a pulse predating activation |
| E8 | effective stake advances (:1147) | test/heartbound-resonance.test.js | `[ramp-up] increases are GRADUAL - activation grants 25%, each pulse closes 25% of the gap` | WO-1676 | SAME case as U3 — the per-pulse advance is the ramp |
| E9 | resonance recalculates (:1148) | test/heartbound-pulse.test.js | `the grant carries HEART-003's own numbers — computed independently, not restated` | WO-1677 | the pulse re-runs HEART-003 rather than restating a stored score |
| E10 | Echo Event generated (:1149) | UNCOVERED | the Echo Event engine does not exist on `dev` yet | WO-1678 | same lane as U11 |
| E11 | player logs in (:1150) | UNCOVERED | client presentation — AutoPilot fleet | WO-1681 | HEART-008 panel/door |
| E12 | Tree pulses (:1151) | UNCOVERED | client presentation — AutoPilot fleet | WO-1680 | HEART-007 visuals |
| E13 | Echo Event appears (:1152) | UNCOVERED | client presentation — AutoPilot fleet | WO-1681 | HEART-008 panel |
| E14 | reward granted once (:1153) | test/heartbound-pulse.test.js | `acceptance 2: a duplicate job's grant insert returns zero rows and is mapped to "already granted"` | WO-1677 | the `ON CONFLICT (player_id, global_pulse_id)` structural gate |
| E15 | reopening cannot duplicate (:1154) | UNCOVERED | the reopen path is the client panel plus the status route | WO-1681 | E14 proves the DB gate ONLY — a re-open that never reaches the DB is not covered by it |

### 7.1 What the map's own suite adds beyond the map

`test/heartbound-suite.test.js` also holds the four **cross-module invariants no single lane owns**,
each spanning files owned by different lanes:

- **B — the tier ladder is in NO client-readable file under `Assets/`.** Q-CONFIG ruling (triage §3,
  RULED 2026-09-10 13:36): *"Command Center, server-only rows — the client registry never carries the
  ladder."* Tier names, the distinctive thresholds and the config key names are all derived **from
  `api/_lib/heartbound-resonance-config.json` at run time**, so a retune cannot stale the detector.
- **C — no backend `.js` under `api/` carries a NUL byte.** `CompileGate`'s NUL guard (CLAUDE.md §1,
  WO-434) scans `Assets/**/*.cs` **only**; nothing checked the backend side, and HEART-005 shipped a
  draft with embedded NULs.
- **D — every `api/_lib/heartbound-*.js` (and both routes) passes `node --check`.**
- **E — no pulse-injection route exists under `api/`.** Q-INJECT ruling (RULED 2026-09-10 13:20):
  *"test builds only."* `test/heartbound-pulse.test.js:592` checks the cron route's **inputs**; this
  checks the whole `api/` **surface** — no such basename, and no live (non-comment) line naming one.

Every iterating assertion carries a non-vacuous guard (a minimum file/row count), because a loop over
zero files passes and proves nothing — memory `gates-report-success-without-proving-it`.

### 7.2 ⛔ What this section does NOT claim

The map is the **backend** package. The EditMode NUNit half (§0b) and the `DataRegression` `Guard.Try`
registration (§0c, acceptance 2/3) are **NOT** delivered here and are still open — see the RESULT file
for the explicit unproven list. No `.cs` was touched, so acceptance items 1, 2, 3, 5 and 6 remain open.

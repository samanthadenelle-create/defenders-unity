# WORK ORDER 1685 — HEART-012: Regression and automated test package

**Status:** READY TO IMPLEMENT
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

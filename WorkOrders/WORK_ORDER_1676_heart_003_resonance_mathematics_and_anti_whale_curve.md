# WORK ORDER 1676 — HEART-003: Resonance mathematics and anti-whale curve

**Status:** IMPLEMENTED — 2026-09-10 by the HEART-003 lane. `api/_lib/heartbound-resonance.js` + `api/_lib/heartbound-resonance-config.json` + `test/heartbound-resonance.test.js`; `node --test` **21 pass / 0 fail**. No `.cs`, no consumer wired, Q-CONFIG left open. See `.RESULT.md`. Awaiting lead commit.
**Silo:** Backend pure arithmetic (`api/_lib/`) + `node --test` unit tests. No Unity, no gameplay, no economy, no scene files.
**Raised by:** HEARTBOUND-TRIAGE lane, 2026-09-10.
**Number:** PRE-ASSIGNED by the coordinator. `CLI_LANES_WO_NUMBERS.md` deliberately **NOT** edited by this lane.
**Spec section:** `docs/specs/HEARTBOUND_SKR_RESONANCE_WORK_ORDERS_2026-09-10.md:260-375` (HEART-003).
**Tree:** worktree at `dev` **`abbeb9362`**.

---

## 0. Classification — **NEW, and it is the one piece of this feature with a fully determined seam**

This is why it is READY and its twelve siblings mostly are not: it is **a pure function**. It takes numbers and returns numbers, it touches no wallet, no chain, no save, no scene, no UI, no economy. The spec supplies the formulae *and* a test vector table (`:299-311`), and the repo already has the unit-test framework (`node --test test/*.test.js`, `package.json:10`, **42 existing test files** under `test/`).

⛔ **Everything it computes is nevertheless SPEC-blocked downstream** — nothing may *consume* these numbers until WO-1679/1682 are ruled. Implementing the arithmetic first is exactly the spec's own Wave 1 ("No major UI yet. Prove native SKR can reliably become authoritative HeartboundState", `:1234-1240`).

---

## 1. Deliverables

### D1 — `api/_lib/heartbound-resonance.js`: pure functions, no I/O

Implement, exactly as spec'd, with **no rounding until the stated step**:

| Function | Spec | Formula |
|---|---|---|
| eligibility floor | `:274-280` | `MIN_HEARTBOUND_STAKE = 100 SKR`, server-configurable |
| activation ramp | `:288-290` | `effectiveStake = actualStake × 0.25` |
| per-pulse ramp | `:292-294` | `effectiveStake += (actualStake − effectiveStake) × 0.25` |
| decrease | `:296-298` | `effectiveStake = MIN(actualStake, effectiveStake)` — **immediate** |
| stake power | `:317-319` | `StakePower = 1000 × log10(1 + EffectiveStake / 250)` |
| tenure power | `:339-341` | `TenurePower = MIN(700, 150 × ln(1 + ContinuousPulseCount / 5))` |
| final | `:345-347` | `ResonanceScore = FLOOR(StakePower + TenurePower)` |
| tiers | `:351-365` | 0/300/600/900/1200/1500/1800/2150/2500/2850/3200 |

⭐ **The asymmetry at `:296-298` is the whole anti-exploit design and must be a single unmistakable line**: increases ramp over pulses, decreases apply instantly. `:306` says so: *"This kills flash-staking exploits."*

⚠ **Precision.** WO-1674 requires the snapshot carry raw base units (`sharesRaw`, `sharePriceRaw`) rather than whole SKR, because the current client read loses precision at `NativeSkrStakeQuery.cs:76`. This module must accept raw units and convert once, at the boundary — not take a pre-rounded `long`.

### D2 — Config, not constants

Spec `:280` (*"Keep this server-configurable"*) and `:367` (*"Thresholds must be configuration-driven. Do not scatter them through gameplay code."*). See OWNER QUESTION Q-CONFIG for **where** that config lives — it is the one open decision in this ticket, and it is an implementation choice, not a design ruling, so it does not hold up the arithmetic.

### D3 — Unit tests, `test/heartbound-resonance.test.js`

House style: `node --test test/*.test.js` (`package.json:10`). Cover the spec's own list at `:1105-1120`: raw SKR conversion, share→token conversion, effective-stake ramp, immediate reduction, `StakePower` curve, `TenurePower`, thresholds, upgrades, downgrades, max tenure.

⭐ **The spec supplies the vector table at `:299-311`** — 100→146, 500→477, 1000→699, 5000→1322, 10000→1613, 25000→2004, 50000→2303, 100000→2603, 250000→3000, 500000→3301 — and says at `:297` they are *"not contractually fixed and should be covered by unit tests"*. Assert them **with a tolerance**, and state the tolerance in the test name so a future tuning change fails loudly rather than silently.

⛔ **Prove the whale property directly, not by inspection.** Spec `:313`: *"500,000 SKR does NOT provide 500 times the power of 1,000 SKR."* Write that as an assertion (`power(500000) < 5 × power(1000)`), because it is the sentence a reviewer will check and the one a "simplification" would break.

---

## 2. Acceptance (from spec `:1105-1120`)

1. Every formula matches the spec's vector table within a stated tolerance.
2. Ramp-up is gradual; ramp-down is immediate — asserted as two separate cases.
3. Tier thresholds are read from config, not from literals in the arithmetic.
4. `highestLifetimeTier` monotonicity (spec `:371-375`) is asserted, including a stake decrease that lowers `resonanceTier` while leaving `highestLifetimeTier` untouched.
5. Tenure caps at 700 — asserted at and beyond the cap.
6. `node --test` passes. ⛔ Judge by the test runner's own result line, not by the exit code — this repo's runners exit 0 on failures (CLAUDE.md §8; memory `gates-report-success-without-proving-it`).
7. `**Status:**` flipped in this file in the same commit as the work; `.RESULT.md` written; both paths reported.

---

## 3. Dependencies

- **Blocked by:** nothing to *write* the module. **Blocked by WO-1674 to feed it real inputs.**
- **Blocks:** WO-1677 (pulse processing calls the ramp), WO-1679 (tiers gate the benefits), WO-1681 (the panel renders the score).
- **Do NOT wire a consumer in this ticket.** Ship the function and its tests; the consumers are SPEC-blocked.

---

## 4. ⛔ OWNER QUESTION

### Q-CONFIG. Where do the Heartbound knobs live, given the Command Center's rail is a THREE-WAY JOIN that assumes a client reader?

`MIN_HEARTBOUND_STAKE`, the 0.25 ramp, the log curve's 250 divisor and 1000 multiplier, the eleven tier thresholds, HEART-004's 72-hour grace and 5-pulse catch-up cap, HEART-009's 10% ceiling — all of these are **read by the backend**, never by the client.

The existing tunables rail does not model that. Adding a row is a data edit **in three places** (`api/_lib/tunable-manifest.js:108-112`, verbatim):

> ⭐ ADDING A LEVER LATER IS A DATA EDIT, NOT A UI EDIT: add the knob to `RemoteTunables.Registry` and to `TUNABLE_KEYS`, re-run `node tools/gen-tunable-manifest.mjs`, and add one entry here. The page grows a card on its own.

The three authorities are `Assets/_Modules/Core/Ops/RemoteTunables.cs:828` (`Registry`), `api/_lib/tunables.js:55` (`TUNABLE_KEYS`) and `api/_lib/tunable-manifest.js:114` (`PRESENTATION`). ⛔ **A `PRESENTATION` row with no client `Registry` entry FAILS the join** — `tunable-manifest.js:848-853` reports it as *"would be INVISIBLE in the Command Center"*. So a backend-only knob cannot simply be added.

**Options, both legitimate, neither obvious:**
- **(a)** Teach the manifest a `serverOnly` marker that `build()` (`tunable-manifest.js:904-940`) honours — one small change to a well-guarded joiner, and every Heartbound knob becomes operable from the phone Command Center like every other knob.
- **(b)** A separate `heartbound_config` table or JSON read only by the backend — zero risk to the existing rail, but a second config system the owner cannot flip from her phone, and a second place to look.

⚠ **Do NOT put these in `RemoteTunables.cs` alone.** That file is the **client-read** rail (`RemoteTunables.cs:1379-1381` is a client lookup loop) and the spec's architectural rule at `:1300-1310` forbids the UI from carrying staking calculations. A tier threshold the client reads is a second copy of the authority.

**Related sub-question:** the panel needs *next-tier progress* (spec `:829`). Either the status endpoint returns `nextTierAt` (one authority, one round trip) or the client re-derives it from a copied threshold table (two authorities, guaranteed drift). **Recommend the endpoint returns it.** Naming it here so WO-1681 does not have to rediscover it.

---

## 5. What NOT to touch

- ⛔ **`Assets/_Modules/Core/Ops/RemoteTunables.cs` and the two joins**, until Q-CONFIG is answered. A half-added knob fails the manifest's own cross-checks (`tunable-manifest.js:838-888`) and takes the Command Center down with it.
- ⛔ **`stake-rewards.json` and `StakeRewardsResolver`'s tier ladder.** A second ladder is WO-1675 Q-LADDER's question, not this ticket's; do not silently reconcile them here.
- ⛔ **No consumer wiring.** This ticket ships arithmetic and tests only. Attaching it to a bonus, a UI or a grant is WO-1679 / WO-1681 / WO-1678, each of which is SPEC-blocked on an owner ruling.
- Do not implement the curve in C#. Spec `:1291-1320`: `Solana → verification → model → resonance → provider → game → UI`, and *"UI contains no staking calculations"* (`:1274`).
- No `.unity` scene files. No `SaveSchema` change (WO-1675 §0b).

---

## 6. Evidence index (opened 2026-09-10 at `dev` `abbeb9362`)

`package.json:10` (`node --test test/*.test.js`); `test/` — 42 existing test files
`api/_lib/tunable-manifest.js:98-107` (field contract), `:108-112` (the authoring instruction, quoted), `:114-127` (a worked `PRESENTATION` row), `:838-888` (the cross-checks), `:904-940` (`build()`)
`api/_lib/tunables.js:55-69` (`TUNABLE_KEYS`)
`Assets/_Modules/Core/Ops/RemoteTunables.cs:828` (`Registry`), `:866-870` (a worked `TunableSpec` row), `:1379-1381` (client lookup), `:1409` (the unknown-key message that names the fix)
`tools/gen-tunable-manifest.mjs:14`; `api/admin/console.js:66,203`; `tools/command-centre.ps1:30-35`
`Assets/_Modules/Wallet/NativeSkrStakeQuery.cs:76` (the precision loss D1 must not repeat)
`Assets/Editor/Regression/RemoteTunablesDefaultsRegression.cs`; `test/tunables-manifest.test.js`

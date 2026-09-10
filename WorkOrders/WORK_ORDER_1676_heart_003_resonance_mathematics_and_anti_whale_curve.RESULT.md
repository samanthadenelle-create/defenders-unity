# WORK ORDER 1676 — HEART-003 — RESULT

**Status:** IMPLEMENTED — 2026-09-10. Awaiting lead gate + commit (no Unity gate applies; see §5).
**Lane:** HEART-003, isolated worktree, merged ff-only onto `dev` **`c96030b5c374f706ddf6af215bce7f9793b90977`**.
**No commit, no push, no Unity, no batchmode.** Three new files, nothing modified except this WO's Status line.

---

## 1. What shipped

| Path | Kind | Lines |
|---|---|---|
| `api/_lib/heartbound-resonance.js` | **NEW** — the pure module | 401 |
| `api/_lib/heartbound-resonance-config.json` | **NEW** — the one authored constant table | 91 |
| `test/heartbound-resonance.test.js` | **NEW** — 21 `node:test` cases | 347 |
| `WorkOrders/WORK_ORDER_1676_...md` | **MODIFIED** — `**Status:**` line only | 1 |

## 2. ⛔ JS, not C# — and NO mirror class, stated because the brief asked

The lane brief offered "a JS module under `api/` **with a mirror C# pure class for display only**".
**The WO forbids the C# half outright** and it was not written:

* WO-1676 §5: *"Do not implement the curve in C#."*
* Spec `:1274`: *"UI contains no staking calculations."*
* Product rule 6 (spec `:21`): the backend is authoritative for game rewards; rule 7 (`:22`): the
  client is never trusted on stake. A display-only C# copy of the tier ladder is a **second
  authority** on a number the server owns, and it would drift the first time a threshold moves.
  The client is *told* its tier by the status endpoint; it never derives one.

⇒ **Zero `.cs` files touched.** `gate_brace.py` and the NUL scan are therefore **N/A** — there is
nothing for them to read. `git status --short` (below) is the proof, not a claim.
⇒ **No `DataRegression` registration line exists to give.** A Unity suite over a module Unity cannot
reach would assert nothing. The oracle is `test/heartbound-resonance.test.js`, run by
`node --test` (`package.json:10`).

## 3. The module — three layers, no I/O

`api/_lib/heartbound-resonance.js`, CommonJS, house style.

* **Boundary** — `sharesToRawTokens` / `rawTokensToSkr` / `skrFromShares`. **BigInt throughout.**
  u128 `shares × sharePrice / 1e9` is the same expression as `NativeSkrStakeQuery.cs:75`, and it
  deliberately **stops there**: the client's next line (`:76`) divides by `SkrBaseUnits` into a
  `long` and throws away every fractional SKR. The narrowing to `Number` is done once as
  `Number(whole) + Number(frac)/1e6`, never `Number(raw)/1e6` — 1e10 SKR is 1e16 base units, past
  `MAX_SAFE_INTEGER`. A u128 handed in as a lossy double is **refused**, not silently rounded.
* **Math** — `stakePower`, `tenurePower`, `resonanceScore` (**the only `Math.floor` in the file**),
  `tierForScore`, `nextTierAt`, `highestLifetimeTier`. Callable directly, because the spec's own
  vector table at `:299-311` is StakePower alone.
* **Transitions** — `inactiveState` / `activate` / `applyPulse` / `observeStake` / `evaluate`.
  State `{actualSkr, effectiveSkr, continuousPulseCount, highestLifetimeTier}`; every transition
  returns a **new** object and never mutates its input (pinned by `[purity]`).

**Purity is structural, not promised.** One `require` in the whole file, and it is the config table.
The `[purity]` case re-reads the source and fails on `node:fs`, `node:http(s)`, `fetch(`, `neon(`,
`Date.now`, `Math.random`, `process.env`, or a second `require`.

**⭐ The asymmetry lives in one place:** increases are gated behind `applyPulse` (25 % of the gap per
pulse); decreases run through `clampEffectiveStakeToActual` = `MIN(actual, effective)`, immediate.
A decrease arriving *at* pulse time clamps **before** the ramp, so a pulse is not a laundering route
around it. Spec `:306`: *"This kills flash-staking exploits."*

## 4. ⚠ One spec reading the module had to pick — recorded, not a blocker

The spec gives an activation rule (`:288-290`) and a per-pulse rule (`:292-294`), and never says what
happens the instant a *staked* player tops up between pulses. The module's reading, written into its
header and pinned by the test named `[ramp] a top-up does NOT move effective stake until the next pulse`:

* the `× 0.25` activation applies **only** on activation from inactive — never re-applied on a top-up;
* a later **increase** records `actualSkr` and moves `effectiveSkr` **not at all** until the next pulse;
* a **decrease** clamps immediately.

The alternative (re-apply 25 % of the new total on a top-up) hands a returning whale a quarter of an
arbitrary stake with no pulse served — the exact flash-stake `:270` and `:306` exist to kill. This is
an **implementation reading of an under-specified case**, not a design ruling, so it does not gate the
ticket; it is flagged here so the owner can overrule it in one line if she reads it differently.

Two smaller readings, same status: eligibility is judged on **actual** stake, **inclusive** (`≥ 100`);
an ineligible position scores **0** and breaks its tenure streak, while `highestLifetimeTier` survives
(spec `:371-375` + `:254-257`).

## 5. Config — one table, no canonical twin, Q-CONFIG left open

`api/_lib/heartbound-resonance-config.json` holds **every** constant: `minHeartboundStake`, both
`0.25` ramp fractions, the `1000`/`250` curve pair, the `700`/`150`/`5` tenure triple, the chain
scales, and all eleven tier thresholds with their names. **There is not one numeric literal in the
arithmetic.** Every exported function takes a trailing `cfg`, defaulting to this table.

⛔ **No twin under `Assets/`, deliberately.** Every other data table in the repo has one; this one
must not, for the reason in §2. Recorded in the file's own `_comment` so a future tidy-up does not
"fix" it.

**WO-1676 Q-CONFIG is untouched and still owner-open.** This file is option **(b)** as an *interim*
single home — nothing in `RemoteTunables.cs`, `tunables.js` or `tunable-manifest.js` was edited, so
the three-way join and its cross-checks are exactly as they were. Because it is **one** file, ruling
(a) later (a `serverOnly` marker on the manifest) is a data move, not archaeology.

`.vercelignore` read at source 2026-09-10: `/*` then `!/api` re-includes the whole folder, so this
JSON ships to the deployment (precedent: `api/_lib/dungeon-manifest.json`).

Q-CONFIG's sub-question is **answered in code, not deferred**: `nextTierAt(score)` lives server-side
and returns `{tier, name, minScore, pointsAway}`, so WO-1681's panel renders next-tier progress from
the endpoint instead of copying the ladder.

## 6. THE VECTOR TABLE — generated from the module, not retyped

`node -e` against `api/_lib/heartbound-resonance.js`, 2026-09-10:

| effective SKR | pulses | StakePower | TenurePower | score | tier | name |
|---:|---:|---:|---:|---:|---:|---|
| 100 | 0 | 146.128 | 0.000 | 146 | 0 | Silent |
| 500 | 0 | 477.121 | 0.000 | 477 | 1 | Emberbound |
| 1000 | 0 | 698.970 | 0.000 | 698 | 2 | Rootbound |
| 5000 | 0 | 1322.219 | 0.000 | 1322 | 4 | Echoing |
| 10000 | 0 | 1612.784 | 0.000 | 1612 | 5 | Awakened |
| 25000 | 0 | 2004.321 | 0.000 | 2004 | 6 | Hearttouched |
| 50000 | 0 | 2303.196 | 0.000 | 2303 | 7 | Deep Resonance |
| 100000 | 0 | 2603.144 | 0.000 | 2603 | 8 | Heartforged |
| 250000 | 0 | 3000.434 | 0.000 | 3000 | 9 | Eternal Echo |
| 500000 | 0 | 3301.247 | 0.000 | 3301 | 10 | Heartbound |
| 1000 | 5 | 698.970 | 103.972 | 802 | 2 | Rootbound |
| 1000 | 20 | 698.970 | 241.416 | 940 | 3 | Stonebound |
| 1000 | 100 | 698.970 | 456.678 | 1155 | 3 | Stonebound |
| 1000 | 527 | 698.970 | 700.000 | 1398 | 4 | Echoing |
| 1000 | 100000 | 698.970 | 700.000 | 1398 | 4 | Echoing |
| 99.999999 | 50 | (146.128) | (359.684) | **0** | 0 | Silent — **below the floor, scores nothing** |
| 500000 | 527 | 3301.247 | 700.000 | 4001 | 10 | Heartbound (the ceiling) |

**Every one of the spec's ten knees (`:299-311`) lands within ±1 point** of the figure the spec
quotes; the tolerance is `TOLERANCE = 1` and it is **named in the test title** so a tuning change
fails loudly. The only visible gap is `1000 SKR → 698.970`, which the spec rounds to 699.

**Facts the table makes, that a reviewer should check:**
* **The floor is not tier I.** 100 SKR scores 146 → **Silent**. Tier I needs 300.
* **Tenure alone moves tiers.** 1,000 SKR walks II → III → IV on pulses with no extra stake.
* **The tenure cap crosses at exactly 527 pulses** (526 → 699.80, 527 → 700.000, 1e9 → 700.000).
* **Anti-whale, as a ratio:** 500× the stake (1,000 → 500,000) buys **4.72×** the power
  (698.97 → 3301.25). Asserted directly as `stakePower(500000) < 5 × stakePower(1000)`, because
  a linearising "simplification" would still pass a loosely-read vector table.
* **Diminishing returns, as the player feels it:** the same extra 1,000 SKR added at 100k buys
  strictly less than at 10k, which buys less than at 1k.

## 7. Verification — markers, not exit codes (CLAUDE.md §8, §11B)

```
$ node --check api/_lib/heartbound-resonance.js && node --check test/heartbound-resonance.test.js
SYNTAX_OK

$ node --test test/heartbound-resonance.test.js
ℹ tests 21
ℹ pass 21
ℹ fail 0
ℹ duration_ms 62.4519
```

Judged on the runner's own `pass 21 / fail 0` lines, per acceptance criterion 6.

**The FULL repo suite was run too, because "my file passes" is not "`node --test` passes"** — a
sibling surface test that enumerates `api/_lib/` could have redded on an unexpected new `.json`.
It did not, and the arithmetic is A/B'd:

```
$ node --test test/*.test.js          # with this lane's three files present
ℹ tests 500   ℹ pass 496   ℹ fail 4

$ node --test test/*.test.js          # same command, this lane's three files moved aside
ℹ tests 479   ℹ pass 475   ℹ fail 4
```

**500 − 479 = 21 tests added, 496 − 475 = 21 passes added, fail unchanged at 4.** This lane
contributes zero failures and nothing enumerating `api/_lib/` noticed the new JSON.

⚠ **THE FOUR FAILURES ARE PRE-EXISTING AND ARE A LINE-ENDING ARTIFACT OF THIS WORKTREE, NOT A DEFECT
IN ANY OF THOSE FEATURES.** They are identical with and without this lane, in files it never opened:
`tunables-manifest.test.js` ×3 and `webgl-offline-content-surface.test.js` ×1. The
`tunables-manifest` diff is **byte-for-byte the same JSON with `\r\n` where the test wants `\n`**
(the failing case is literally named *"the generated copy is LF with a trailing newline and no
BOM"*) — the Windows checkout applied CRLF to `api/_lib/tunable-manifest.generated.json`. The same
CRLF-in-a-worktree artifact is recorded in `docs/specs/HEARTBOUND_TRIAGE_2026-09-10.md:10` for the
spec file itself. **This is reported, not fixed:** `tunable-manifest.generated.json` is WO-1676 §5's
first "do not touch", and re-writing it to satisfy a test would be this lane editing the tunables rail
it was told to leave alone. **It is a real finding for the lead** — worth confirming whether the
same four red on `dev` proper or only inside agent worktrees (a `.gitattributes`/`core.autocrlf`
question), because if it is worktree-only, every future lane's full-suite run will show it.

⚠ **One assertion in this suite was WRONG on the first run and the suite caught it** — the draft
asserted "each 10× stake step adds less power than the last", which is **false for log10** (successive
decade steps *climb* toward 1,000 points: 552.8 → 913.8 → 990.4). Diminishing returns is a statement
about **absolute** stake, and the case now says that. The wrong assertion, and why, is written into
the test beside the fix so nobody re-derives it. This is the only correction made.

**The 21 cases:** `[vectors]` (all ten spec knees + five tenure rows, input → tier), `[whale]`,
`[curve]`, `[floor]` ×2 (inclusive `≥100`; the raw edge `99_999_999` vs `100_000_000` base units),
`[precision]` ×2 (fractional SKR the client truncates; the >2^53 u128 case), `[ramp-up]`,
`[ramp-down]`, `[ramp]` ×3 (top-up inert until pulse; clamp-before-ramp at pulse time; sub-floor drop
breaks the streak), `[tenure]` ×2 (0 / 5 / 20 / 100 / 526 / 527 / 5000 / 1e9), `[tiers]` ×2 (every
threshold plus one point under; 299/300, 3199/3200, ceiling; `nextTierAt`), `[downgrade]` ×2 (a
decrease lowers the tier and leaves `highestLifetimeTier` at 10; monotone across an eight-step stake
walk), `[config]`, `[purity]` ×2.

## 8. Acceptance, line by line

| # | Criterion | Evidence |
|---|---|---|
| 1 | Formulae match the vector table within a stated tolerance | §6; `TOLERANCE = 1`, in the test name |
| 2 | Ramp-up gradual, ramp-down immediate, **two separate cases** | `[ramp-up]`, `[ramp-down]`, plus 3 `[ramp]` cases |
| 3 | Tier thresholds from config, not literals | `[config]` injects a different ladder and asserts the answers move |
| 4 | `highestLifetimeTier` monotone incl. a decrease | `[downgrade]` ×2 — tier 10 → lower, lifetime stays 10 |
| 5 | Tenure caps at 700 at and beyond the cap | `[tenure]` — 527, 5000, 1e9 all exactly 700 |
| 6 | `node --test` passes, judged by the result line | §7 — `pass 21 / fail 0` |
| 7 | Status flipped, RESULT written, both paths reported | this file + §9 |

## 9. What was NOT touched (WO §5)

`RemoteTunables.cs`, `api/_lib/tunables.js`, `api/_lib/tunable-manifest.js` and the generated manifest —
**untouched**, so the three-way join and its cross-checks are unchanged and Q-CONFIG stays the owner's.
`stake-rewards.json` / `StakeRewardsResolver` ladder — **untouched** (WO-1675 Q-LADDER's question).
No consumer wired (WO §3). No `.cs`, no `.unity`, no `SaveSchema`, no `FeatureFlags`, no UI, no chain
read, no Neon table, no HEART-006 benefit table, no `CLI_LANES_WO_NUMBERS.md` edit.

```
$ git status --short            # in this lane's worktree, run 2026-09-10
 M WorkOrders/WORK_ORDER_1676_heart_003_resonance_mathematics_and_anti_whale_curve.md
?? api/_lib/heartbound-resonance-config.json
?? api/_lib/heartbound-resonance.js
?? test/heartbound-resonance.test.js
?? WorkOrders/WORK_ORDER_1676_heart_003_resonance_mathematics_and_anti_whale_curve.RESULT.md
```

## 10. For the lead

Commit paths, one lane:

```
api/_lib/heartbound-resonance.js
api/_lib/heartbound-resonance-config.json
test/heartbound-resonance.test.js
WorkOrders/WORK_ORDER_1676_heart_003_resonance_mathematics_and_anti_whale_curve.md
WorkOrders/WORK_ORDER_1676_heart_003_resonance_mathematics_and_anti_whale_curve.RESULT.md
```

Then `python tools/board_build.py`. No Unity gate applies (no `.cs`); the gate for this lane is
`node --test test/heartbound-resonance.test.js`, and it is green at 21/21.

**Still open for the owner, both recorded above, neither blocking:** Q-CONFIG (§5) and the top-up
reading (§4).

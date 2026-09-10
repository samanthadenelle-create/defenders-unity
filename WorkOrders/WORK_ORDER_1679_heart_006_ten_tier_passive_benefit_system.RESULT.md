# WORK ORDER 1679 — HEART-006 — RESULT

**Status:** IMPLEMENTED (2026-09-10). Ten-tier benefit table + the ceiling meter land as ONE backend
module; the client gets a narrow, token-free, server-told provider in `DeNelle.Core`.
**Lane:** HEART-006/009 SME (owned both; they gate each other).
**Tree:** worktree `agent-a8bb71753a62e0d92` at `dev` **`4329accd1`** (`git merge --ff-only` clean).
**Not gated, not committed, not pushed** — no Unity was fired by this lane.

---

## 1. What shipped

| File | State |
|---|---|
| `api/_lib/heartbound-tiers-config.json` | NEW — the one authored benefit table + the ceiling |
| `api/_lib/heartbound-tiers.js` | NEW — benefits, the meter, the `resolveTier` seam, the wire payload |
| `test/heartbound-tiers.test.js` | NEW — 26 node:test cases, **26/26 pass** |
| `Assets/_Modules/Core/Catalog/HeartboundBenefits.cs` | NEW — `IHeartboundBonusProvider` + zero provider + one flag read |
| `Assets/_Modules/Core/FeatureFlags.cs` | +`HeartboundPassives` (DAPP_STORE-gated, mirrors `StakingPolishBonus`) |
| `Assets/_Modules/Core/Catalog/PolishBonusProvider.cs` | Q-LADDER merge — grant AMOUNTS now come from the tier |
| `Assets/_Modules/Wallet/HeartboundStatusClient.cs` | parses the `benefits` block, feeds the provider |
| `Assets/Editor/Regression/HeartboundBenefitsRegression.cs` | NEW — meter pin + no-ladder-on-the-client + provider shape |
| `Assets/_Modules/Core/Platform/VerifiedStakeSnapshot.cs` (+ `.meta`) | copied from the main tree UNCHANGED (see §5) |

**D1 (provider)** — `IHeartboundBonusProvider` has exactly five members: `Tier`,
`OfflineProductionRateBonus`, `ExtraWeeklyRerolls`, `RollCapDelta`, `UnlockedEventIds`
(`HeartboundBenefits.cs:101-121`, re-read at source after the last edit). `NoHeartboundBonus` is the zero
provider; `HeartboundBonuses.Active` (`:251`) resolves through `FlagOn` (`:268`), which is the **only**
occurrence of `FeatureFlags.HeartboundPassives` in the file — pinned by the regression, which fails at any
count but 1.

⭐ **That pin has already earned its keep.** The first version read the flag in **two** places (`Active`
and `Enabled`), which the dry run below caught — a RED gate that would otherwise have landed on the lead's
desk. Corrected to a single `FlagOn`. No dictionary, no `float Get(string)`, no odds-shaped member,
copying `IPolishBonusProvider`'s narrowness as WO-1679 §0a required.

**D2/D3 (seams)** — decided, not coin-flipped, and the decisions are in §4.

**D4 (shape regression)** — `HeartboundBenefitsRegression.CheckProviderShape` fails on any member
outside the allowed five and on the tokens `odds|chance|weight|luck|probability|roll(|random|Dictionary<|float Get(`.

---

## 2. Evidence

**The ceiling is MEASURED.** `node --test test/heartbound-tiers.test.js`, printed by the suite itself:

```
HEARTBOUND_CEILING_MEASURED sum=0.06 ceiling=0.1 counted=3 excluded=10
  rows=T1:offline_gathering=0.02,T2:echo_labor=0.02,T7:echo_workers=0.02
ℹ tests 26   ℹ pass 26   ℹ fail 0
```

WO-1679 acceptance 4 ("measured, not asserted") is that line. The full reasoning is in WO-1682's RESULT.

**Tier landing map, after the rulings** (`heartbound-tiers-config.json:tiers`):

| Tier | Row | Kind | Counted? |
|---|---|---|---|
| I | `offline_gathering` +0.02 | productionRate | ✅ |
| II | `echo_labor` +0.02 | productionRate | ✅ |
| III | — | — | ⛔ **EMPTY BY RULING** (Q-STONE: drop the event) |
| IV | `scouts_whisper` | information | ✖ (Q-SCOUT: existing report, earlier) |
| V | `crafting_inspiration` | eventUnlock | ✖ |
| VI | `heartfire_spark` | eventUnlock | ✖ (Q-HEARTFIRE: second source allowed) |
| VII | `echo_workers` +0.02 | productionRate | ✅ (Q-ECHOFIG: modifiers only) |
| VIII | `echo_bloom` | presentation | ✖ |
| IX | `ancient_echo`, `kingdom_resonance` | eventUnlock, presentation | ✖ |
| X | `heartbound_kingdom`, `event_choice` | presentation, eventUnlock | ✖ |
| I | `weekly_polish_reroll` +1 | polishAttempts | ✖ (Q-LADDER merge) |
| V | `polish_roll_cap` +1 | polishAttempts | ✖ (Q-LADDER merge) |

⛔ **Tier X adds no production rate at all** — spec `:746`, "prestigious rather than economically mandatory".

## ✅ 2a. THE SHIP-ORDER HAZARD — FOUND, THEN CLOSED IN THIS LANE

**Found:** the Q-LADDER merge makes the polish grant amounts come from `HeartboundBonuses`, which is fed
only by a `benefits` block on the status response. The trace: `NativeSkrPolishBonus.ExtraWeeklyRerolls` →
`HeartboundBonuses.ExtraWeeklyRerolls` → `Active` → `_installed ?? Zero`, and `_installed` is set only by
`AcceptServerBenefits`. **If `PolishBonusProvider.cs` had landed while the endpoint still sent no block,
every staker's weekly re-roll would have silently read 0** until the endpoint caught up.

**Closed:** `api/heartbound/status.js` is now extended **in this lane** (WO-1682 RESULT §6, with the exact
lines), so the endpoint and the provider land in the same change and the window never exists.

⛔ **The obvious alternative was correctly refused.** Keeping the old stake-derived amounts as a fallback
would have restored the SECOND LADDER Q-LADDER was ruled to remove, and the two would have drifted the
first time either was retuned. One ladder is the ruling; wiring the endpoint is the fix.

⚠ **Still worth one line to the lead:** these two files must stay in the same commit. Splitting them
re-opens the window.

---

**Q-LADDER merged without losing a shipped perk — BY DESIGN; see §2a for the ship-order caveat that makes
this true of the tree as well.** `NativeSkrPolishBonus` used to grant +1 re-roll at any
stake and +1 roll cap at 10,000 SKR. The merged ladder places those at Tier I and Tier V, and the placement
is **measured against the live curve** rather than asserted — `test/heartbound-tiers.test.js`, case *"the
Tier V placement is the closest tier to the shipped 10,000 SKR roll-cap threshold"*, computes
`resonance.resonanceScore(10000, 0)` and asserts the tier it reaches is 5. Retune the curve and that case
goes RED instead of a 10k staker silently losing a perk.

**One bootstrap, no race.** `NativeSkrPolishBonusBootstrap` is still the single
`[RuntimeInitializeOnLoadMethod]` installer; the class name is unchanged deliberately. A second provider
class with its own bootstrap would race two `BeforeSceneLoad` installers with undefined last-wins ordering.

**Acceptance 1 (no gameplay system references a chain).** `HeartboundBenefits.cs` contains zero
occurrences of `skr|solana|wallet|usdc|crypto|blockchain|web3`, case-insensitive, **in code or comments** —
verified by grep, and pinned by the regression's own sweep of that file. It is in `DeNelle.Core`, which
ships in every artifact including Google Play, where `GooglePlayPackagingGate` sweeps the AAB for exactly
that vocabulary.

**Acceptance 2 (zero provider on every path, no branch at the call site).** `Active` returns `Zero` when
the flag is off or nothing is installed; `AcceptBenefits` on an absent `benefits` block calls
`NoteAttemptFailed` and **holds the previous answer** rather than zeroing — the same fail-to-last-known
posture the owner ruled for the stake (Q2). The one place a definitive zero IS written is the guest rail
(`HeartboundStatusClient.cs`, `WALLET_NOT_LINKED` branch), where tier 0 is a fact and not ignorance.

**Gates:** `python tools/gate_brace.py` over all six touched `.cs` → `GATE_BRACE_SUMMARY bad=0 of 6`.
NUL scan over all 11 touched files → `NUL_SCAN bad=0 of 11`.

### The editor regression's LOGIC was dry-run, port-for-port, in Python

No Unity in this lane, so rather than hand over an unexercised gate the three checks were ported
regex-for-regex and rule-for-rule and run over the real tree
(`scratchpad/dryrun_heartbound_regression.py`):

```
METER      counting kinds=['productionRate'] declared=5 rows=13 counted=3 sum=0.06 ceiling=0.1 within=True
LADDER     tier names (11): Silent, Emberbound, Rootbound, Stonebound, Echoing, Awakened,
                            Hearttouched, Deep Resonance, Heartforged, Eternal Echo, Heartbound
SWEEP      scanned 1558 non-Editor .cs | minScore hits=0 | >=3-tier-name hits=0
PROVIDER   interface regex matched: True
PROVIDER   members found (5): Tier, OfflineProductionRateBonus, ExtraWeeklyRerolls, RollCapDelta, UnlockedEventIds
PROVIDER   FeatureFlags.HeartboundPassives read 1 time(s) (must be 1)
PROVIDER   forbidden chain tokens present: NONE

DRYRUN_VERDICT GREEN (0 findings)
```

⭐ **This was worth doing twice over.** The first run came back **RED** on the flag-read count and found a
real defect (§1). It also retires a specific false-positive worry: four tier names are ordinary English
words (`Silent`, `Echoing`, `Awakened`, `Heartbound`), and the ≥3-names rule could in principle fire on an
unrelated system — **across 1558 non-Editor `.cs` files it fires on none**, and the interface regex matches
and yields exactly the five allowed members.

⚠ **This exercises the LOGIC, not the C# host.** Compilation, `Application.dataPath` resolution and
`DataRegression` integration remain unproven — see §5.

---

## 3. DataRegression registration line (for the lead — this lane did not touch `DataRegression.cs`)

Beside the covenant gate at `Assets/Editor/Regression/DataRegression.cs:332`:

```csharp
// --- WO-1679/1682 (HEART-006/009): the Heartbound economic ceiling, MEASURED, plus the two
//     structural pins - no client file carries the tier ladder, and IHeartboundBonusProvider
//     stays narrow. A ceiling with no meter is a comment (WO-1682 section 0c).
if (!HeartboundBenefitsRegression.Run(out var heartboundReason)) failures.Add(heartboundReason); else log.AppendLine("[heartbound] " + heartboundReason);
```

---

## 4. What was NOT done, and why

- ⛔ **Tier I / II / VII are NOT applied to offline accrual yet.** The seam is exposed
  (`HeartboundBonuses.OfflineProductionRateBonus`) and **has no reader**. Reason, read at source:
  `OfflineClaimCoordinator` (`Assets/_Modules/Village/Harvest/OfflineClaimCoordinator.cs:107,163`) fans out
  to consumers that **each compute their own accrual** inside `IOfflineClaimConsumer.ApplyOfflineWindow` —
  there is **no single accrual point** to apply one multiplier at. Wiring it means editing four Village-side
  consumers, i.e. writing the same modifier in four places, which is the duplication this codebase keeps
  paying for. It also crosses into the Village silo, not this lane's. **The provider therefore ships
  declared-and-unread, exactly as `EchoLaneBonuses.CraftingMult/DefenseMult/ExplorationMult` already do**
  (`EchoLaneBonuses.cs:14-23` states its own consumption status for the same reason, and WO-1679 §0b
  documents the precedent). **Recommend a follow-up WO** that gives the coordinator one place to apply a
  global rate multiplier, which Echoes would use too.
- ⛔ **WO-1679 §0c's build/crafting-time seam was NOT built.** D3 asked for a decision: **deferred**, and
  the tiers that needed it were re-landed as production-rate or event-unlock rows instead (see the
  `_authoringNotes` in the config). Q-METER excludes timers from the ceiling anyway, so a timer seam buys
  nothing this ticket needs.
- ⛔ **Tiers IV, VI, VIII, IX, X have config rows but no application.** Their seams are owned by other
  lanes (`RaidDeployVM` / the event pool / WO-1680). The rows exist so the panel can name a tier's identity
  and so the meter counts the whole ladder; nothing pretends they are wired.
- ⛔ **`api/heartbound/status.js` was NOT edited** — see WO-1682's RESULT §4 for the hunk and the reason.
- ⛔ **`StakingComplianceRegression.cs` was NOT edited** — see WO-1682's RESULT §5. Its pins survive this
  change; the analysis is there.
- ⚠ **Two new `.cs` files have no `.meta`** (`HeartboundBenefits.cs`, `HeartboundBenefitsRegression.cs`).
  This lane fires no Unity, so none was generated. Unity will create them on first import.

---

## 5. Unproven, named as unproven

- ⛔ **The editor regression's C# host was UNPROVEN, and it was WRONG — chain 47 caught it.** This RESULT
  said *"logic exercised in Python, C# host unexercised"*, and naming that gap is what made the failure
  cheap to place. `Builds/wave10f-compile1`:

  ```
  Assets\Editor\Regression\HeartboundBenefitsRegression.cs(261,38): error CS1061:
  'object' does not contain a definition for 'Groups'
  ```

  **Root cause:** `Regex.Matches` returns `MatchCollection`, which implements only the **non-generic**
  `IEnumerable`. `foreach (var member in Regex.Matches(...))` therefore infers `member` as **`object`**,
  and `member.Groups` cannot bind. ⭐ **A Python port structurally cannot see this** — the trap lives in
  the C# type system, not in the rule being expressed. It is the exact reason the gap was named rather
  than glossed. **Fixed** (§7 below); the other four `Regex.Matches` loops already typed `Match`
  explicitly, which is why the compiler reported exactly one line.

  ⚠ **Still unproven after the fix:** I have not recompiled — no Unity in this lane. Also unproven:
  that `Application.dataPath`'s parent resolves to the repo root in batchmode, and that
  `DataRegression.RunAll` reaches the suite.

---

## 7. Compile fix — `HeartboundBenefitsRegression.cs` (chain 47 RED → fixed)

Only that file was touched. Exact lines:

| Line | Change |
|---|---|
| `:272` | ⭐ **THE FIX** — `foreach (var member in Regex.Matches(...))` → **`foreach (Match member in ...)`** (was `:259` pre-edit; the CS1061 site) |
| `:263-268` | comment recording why `var` can never be used on a `Regex.Matches` loop, and that a logic port cannot catch it |
| `:247` | `var m = Regex.Match(...)` → `Match m = Regex.Match(...)` (+ `:243-246` note) |
| `:332` | `var v = Regex.Match(...)` → `Match v = ...` |
| `:363` | `var m = Regex.Match(...)` → `Match m = ...` |
| `:136` | `foreach (var row in Benefits(...))` → `foreach (BenefitRow row in ...)` (+ `:133-135` note) |

**Whole-file sweep, as instructed** — because the compiler stops at the first error on some paths:
all five `Regex.Match`/`Matches` results and every dereferenced local are now **explicitly typed**. The
`var` occurrences that remain (`:82`, `:132`, `:213`, `:261`, `:334`, `:348`, `:356`, `:373`) are each a
direct object-creation expression — `new List<string>()`, `new HashSet<string>()`, `new StringBuilder()`,
`new BenefitRow {…}` — where the type is fixed by the initialiser and **cannot** infer `object`. Every
`foreach` in the file is explicitly typed (`string`, `Match`, `BenefitRow`).

`using System.Text.RegularExpressions;` is present at `:49`, so `Match` binds.

**Verification:** `python tools/gate_brace.py Assets/Editor/Regression/HeartboundBenefitsRegression.cs`
→ `GATE_BRACE_SUMMARY bad=0 of 1`; NUL bytes 0; the Python logic dry-run still
`DRYRUN_VERDICT GREEN (0 findings)` — the regexes themselves were not altered, only the C# types of the
locals holding their results.

---

## 8. Chain 48 — two reds, both mine, both fixed

### 8a. `STAKING_COMPLIANCE_FAIL: SECOND WRITER of the verified stake`

⛔ **`HeartboundBenefits.cs` never called it. A DOC COMMENT tripped a substring lint.** The line was
*"…for the same reason `VerifiedStakeSnapshot.AcceptServerVerification` does…"* — prose comparing the two
designs. `StakingComplianceRegression` §A4 (`:122-142`) proves the single-writer property with
`text.IndexOf("VerifiedStakeSnapshot.AcceptServerVerification")` over each runtime file and **does not
strip comments**, so mentioning the method reported this file as a second writer.

⭐ **The lint is RIGHT to be blunt and was NOT weakened.** A `public static` writer cannot be made
unsettable, so counting callers by text is the only guarantee available; a scanner that tried to parse
comments out would be a scanner that could be fooled by a comment. **Fixed on my side, not the gate's.**

| File | Line | Change |
|---|---|---|
| `Assets/_Modules/Core/Catalog/HeartboundBenefits.cs` | `:193` | *"…for the same reason `VerifiedStakeSnapshot.AcceptServerVerification` does"* → *"…the verified-stake snapshot's own accept method does"* |
| same | `:197-205` | new ⛔ note recording why that method's full name must never appear in this file, and that the lint is correct |

**Measured after the fix** (`scratchpad/dryrun_chain48.py`, a port of §A4):
`A4 writers found: 1 — Assets/_Modules/Wallet/HeartboundStatusClient.cs`. §A1 and §A2 re-checked in the
same run and both still hold.

### 8b. `JEWELER_DISCOVERY_FTUE_FAIL: verified native SKR stake does not grant and consume the one weekly bonus attempt`

⛔ **NEITHER of the two candidate fixes was correct, because the diagnosis was wrong: nothing is
evaluated at runtime.** `JewelerDiscoveryFtueRegression` reads `PolishBonusProvider.cs` **as text**
(`:21`, `string bonus = Read(...)`) and the suite contains **no** `PolishBonuses.Install`, no
`AcceptServerBenefits`, no provider instantiation — proven by grep. The assertion required the **literal
string** `"Standing.HasStake ? 1 : 0"`, which the Q-LADDER merge deliberately replaced. So it is not "the
fixture has an empty `HeartboundBonuses`" — **there is no fixture to seat a benefits block into**, and
seeding one would not satisfy a substring check.

⛔ **And the "grant Tier I whenever a verified stake exists" option is FALSE ON THE NUMBERS — measured,
not argued.** Run against the live curve:

```
minHeartboundStake = 100 SKR
actual=100 SKR -> effective@activation=25   score=41   TIER=0   (full effective: score=146, TIER=0)
actual=250 SKR -> effective@activation=62.5 score=96   TIER=0   (full effective: score=301, TIER=1)
```

A minimum-stake wallet is **Tier 0**, at activation *and* at full effective stake. A client-side
"verified stake ⇒ Tier I" floor would therefore grant the weekly attempt to players the server says are
Tier 0 — **a client fabricating an entitlement the backend never issued**, which is product rule 7's exact
shape, and it would reinstate the second ladder Q-LADDER was ruled to remove.

✅ **The correct fix is the sanctioned one: an ORACLE RE-POINT THAT MOVES WITH A RULING** (CLAUDE.md §11
names this explicitly). Q-LADDER moved the grant AMOUNT from the stake to the tier; the lint asserted the
old expression, so the lint moves.

| File | Line | Change |
|---|---|---|
| `Assets/Editor/Regression/JewelerDiscoveryFtueRegression.cs` | `:122-140` | new comment: the ruling, why the check moved, and the measured refutation of the tier-1-floor option |
| same | `:141-150` | the assertion, **re-pointed and made STRICTER** |

⭐ **It pins TWO halves where the old line pinned one.** Old: `Standing.HasStake ? 1 : 0`. New: the amount
must come from the merged ladder (`HeartboundBonuses.ExtraWeeklyRerolls` **and**
`HeartboundBonuses.RollCapDelta`) **AND** the grant must still be gated on a backend-verified stake
(`Standing.HasStake ?`). A tier alone must never pay: tier and stake live in different statics and a
failed refresh deliberately holds the previous benefits (WO-1674 §A3). Failure message rewritten to say
which half broke.

**Both green by port:** `DRYRUN_CHAIN48 GREEN (0 findings)` — all six provider-side substrings and both
`JewelPolishService.cs` substrings verified present against the real files (`Standing.HasStake ?
HeartboundBonuses.ExtraWeeklyRerolls : 0` at `PolishBonusProvider.cs:142`, `…RollCapDelta : 0` at `:145`).
`GATE_BRACE_SUMMARY bad=0 of 7`, NUL 0. Node unaffected: `heartbound-tiers + skr-staking` **52/52**,
`heartbound-status-benefits` **5/5**.

⚠ **Unproven:** not recompiled and the C# suites were not run — no Unity in this lane. The claim is
*rules green by port*, not *chain 49 green*.
- ⛔ **Nothing here has been through `COMPILE_GATE_OK`.** Brace-balance and NUL are proven; compilation is
  not. `HeartboundStatusClient.cs`'s new code sits inside `#if DAPP_STORE`, which the editor gate may not
  compile at all (the coordinator's own caution) — so a compile error in `AcceptBenefits` could survive a
  green gate. **Recommend the lead compile once with `DAPP_STORE` defined before shipping a Seeker build.**
- ⚠ **`VerifiedStakeSnapshot.cs` is copied into this worktree UNCHANGED** (blob
  `058ce377d32e6d9b61100caaa1bff66ad6ca0b30`, 14256 bytes, identical to the main tree). It is present only
  so this worktree compiles conceptually; **the lead should take the main tree's copy, not this one.**
- ⚠ **The Play-boundary exposure on `VerifiedStakeSnapshot.cs` is REAL and NOT FIXED.** It is in
  `DeNelle.Core` (every artifact) and carries `RewardBearingStakeSkr` / `SkrBaseUnits` as member names —
  which survive into IL2CPP metadata — plus `" SKR"` as a string literal. `GooglePlayPackagingGate`'s
  forbidden vocabulary includes `"skr"`. **I did not move or guard it**, because the coordinator's own rule
  is to hand the finding when a Core consumer remains, and one does: `PolishBonusProvider.cs` (also
  `DeNelle.Core`) reads `VerifiedStakeSnapshot.RewardBearingStakeSkr`, and `StakingComplianceRegression`
  §A1/§A3 pins both that read and the file's path. **I have NOT run the packaging gate and cannot claim it
  objects — only that the token is present and the gate's list contains it.** Cheapest close: build a Play
  AAB and run the gate; if it fires, the fix is a `DeNelle.Wallet` move plus re-pointing §A1/§A3.

---

## 6. Owner questions — all ruled, none re-opened

Q-P2W ✅ ruled (economic acceleration allowed, combat never) — no row grants combat power, pinned.
Q-ECHOFIG ✅ ruled (modifiers only) — Tiers II and VII are `productionRate` rows; **no world actor was
added and `EchoWorldPresence` remains the single appearance owner.**
Q-LADDER ✅ ruled (merge) — implemented; the polish perk is now two benefit rows.

⚠ **Two values in the shipped table are CHOSEN, NOT RULED, and are labelled as such in the config's
`_authoringNotes` (and pinned by a test that the label is there):** Tier II's and Tier VII's `+0.02`. The
owner ruled that those tiers *move numbers*; she did not rule *which* numbers. Same for the Tier I / Tier V
placement of the two polish rows — she ruled the MERGE, not the two tier numbers. **Retune freely; the
meter is the gate.**

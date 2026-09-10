# WORK ORDER 1682 — HEART-009: Passive economy guardrails

**Status:** IMPLEMENTED (2026-09-10) — the 10% ceiling now has a real METER (pure function, Q-METER as data), measured 0.06/0.10 with red-before-green by injection; `tunable-manifest` learned `serverOnly` and a real exemption-too-wide gap was closed. `api/heartbound/status.js` extended in-lane — the wire now carries `benefits` + a scalar `nextTierAt`, proven by a 5/5 suite driving the real handler; `heartbound-tiers + skr-staking` 52/52. D2 covenant sweep deliberately NOT extended (no event table exists yet) — see `.RESULT.md`. Not gated, not committed.
**Silo:** Economy policy + one build gate. No gameplay retune, no scene files.
**Raised by:** HEARTBOUND-TRIAGE lane, 2026-09-10.
**Number:** PRE-ASSIGNED by the coordinator. `CLI_LANES_WO_NUMBERS.md` deliberately **NOT** edited by this lane.
**Spec section:** `docs/specs/HEARTBOUND_SKR_RESONANCE_WORK_ORDERS_2026-09-10.md:899-952` (HEART-009).
**Tree:** worktree at `dev` **`abbeb9362`**.

---

## 0. Classification — **NEW ceiling over EXISTING covenant machinery. The machinery is good; its coverage is hardcoded.**

### 0a. ⭐ The covenant gate already exists and is registered
`Assets/Editor/Regression/MonetizationCovenantRegression.cs` — registered in `DataRegression.RunAll` at `Assets/Editor/Regression/DataRegression.cs:332`. It **fails on any probability/odds/roll/chance/random field in a monetization JSON** (`:14`, `:80-83`, `:198-202`). Its header argues for derivation: *"The allowlist is DERIVED, not blind-hardcoded… If the JSON list grows, the gate grows with it — single source of truth."*

⛔ **But its FILE list is hardcoded — six entries at `:97-104`** (`packs.json` ×2, `skr_store.json`, `skr_staking.json`, `battle_monthly_packs.sample.json`, `economy_store_packs.sample.json`). **A `heartbound-events.json` added tomorrow is not swept and the gate stays green.** WO-1673 D2 exists to derive that list. **This ticket either lands after D2, or explicitly adds itself and says so in the RESULT** — a third file relying on someone remembering is the exact shape CLAUDE.md §16 spends a page on.

### 0b. ⭐ The forbidden-reward list is already canon, and already enforced at the money seam
Spec `:919-931` forbids SKR, SOL, USDC, withdrawable tokens, tradable financial assets, uncapped premium currency. **All of that is already the standing rule**, and it is not merely written down:
- `Assets/_Modules/Wallet/PackStore.cs:467-473`: *"THE GAME NEVER HOLDS SKR AND MUST NEVER READ AS IF IT DOES… There is NO in-game SKR ledger, earn loop or spend loop and there must never be one."*
- `Assets/_Modules/Core/Platform/StakeRewardsResolver.cs:5-10`: never mint, never custody, never a withdrawable in-game balance.
- `Assets/Editor/Regression/BattleMonthlyRegression.cs:636-638` asserts no SKR ledger exists in this build.
- `Assets/_Modules/Core/Jobs/JobRushPolicy.cs` — *"sell the WAIT, never the ROLL"*, owner ruling 2026-08-16, enforced at the money seam `BuildTimerService.cs:1139` and pinned by `DungeonGemExclusivityRegression:162,177`. Its `:65-67` says removing it *"is a legal decision, not an engineering one."*

**So D2 below is a re-assertion over a new table, not a new policy.** Say that, and cite these, rather than writing a fresh covenant that could drift from them.

### 0c. ⛔ The 10% ceiling is UNMEASURABLE as the spec states it
Spec `:903-910`: *"Direct recurring production modifiers from Heartbound should initially remain below `10% combined effective economic acceleration`."*

There is **no existing measurement of "effective economic acceleration"** in this repo — `grep` finds no such aggregate. The nearest analogue is `EchoBonusCalculator.DisclosedHarvestBonusPercent()` (`Assets/_Modules/Village/Harvest/EchoBonusCalculator.cs:145`), which discloses the Echo aggregate for one lane only.

⛔ **A ceiling with no meter is a comment, not a guardrail** — the identical failure WO-1673 §0 found three times over (`JewelPolishRegression`, `SkrStakingRegression`, and this ticket's sibling `StakingComplianceRegression`, all cited by code as protection and none of them existing). **Do not write "under 10%" into a file and call it done.** Either define the metric and build the meter, or say plainly that the cap is unenforced. See Q-METER.

### 0d. ⚠ And the modifiers it would cap are applied CLIENT-SIDE
`api/game/save.js:410-421`: farming/raiding/dungeons/arena *"are simulated entirely on the client and reach this backend only inside the opaque save blob."* Offline accrual runs through `OfflineClaimCoordinator` (`Assets/_Modules/Village/Harvest/OfflineClaimCoordinator.cs:107,163`), all four consumers client-side. So a server-computed ceiling is **advisory** at the point it is applied. That is not a reason to skip it — it is a reason to say so out loud. See Q-CLIENTECON.

---

## 1. Deliverables

- **D1** — Define the metric (Q-METER), then build the meter, then assert the ceiling. In that order.
- **D2** — Extend the covenant sweep to Heartbound's reward table (§0a), citing §0b's existing policy rather than restating it.
- **D3** — Config discipline: spec `:947`, *"All reward amounts and weights belong in data/config. No reward values hardcoded in presentation classes."* Where that config lives is WO-1676 Q-CONFIG.
- **D4** — Record the **preferred** reward classes (spec `:933-943`: rough stones, information, time-limited boosts, queue convenience, cosmetic progression, lore, Heartfire interaction, temporary NPCs, bounded crafting assistance) as the allowed set, and note that **two of them are themselves blocked** — rough stones by WO-1678 Q-STONE, Heartfire by WO-1678 Q-HEARTFIRE.

---

## 2. Acceptance

1. The economic-acceleration metric is **defined in one place** and its value is **measured, not asserted**, with the measurement quoted in the RESULT.
2. `REGRESSION_OK <n>/<n> suites` on a **fresh** log with the extended sweep registered, **proven RED before green** (change one reward value and watch the ceiling case fail; restore).
3. No Heartbound reward path can grant a forbidden asset — asserted by the gate, not by reading.
4. Every reward value comes from config.
5. `COMPILE_GATE_OK`, `gate_brace.py` clean, zero NUL bytes.
6. `**Status:**` flipped in this file in the same commit as the work; `.RESULT.md` written; both paths reported.

---

## 3. Dependencies

**Blocked by:** WO-1678 (there is nothing to cap until the event table exists), WO-1679 (the tier benefits are the other half of the sum), WO-1673 D2 (§0a), and Q-METER / Q-CLIENTECON / Q-P2W below.
**Blocks:** nothing directly — but it is the ticket that makes WO-1678 and WO-1679 safe to ship, so in practice it gates the release rather than the code.

---

## 4. ⛔ OWNER QUESTIONS

### Q-METER. What exactly is "10% combined effective economic acceleration", measured how?
§0c. Candidate definitions, and they give different answers:
- **(a)** Sum of multiplicative modifiers on resource *production rate* (gathering + offline accrual), ignoring time savings.
- **(b)** (a) plus time-value: a build acceleration converted into equivalent resources per hour.
- **(c)** Total resources a Tier X player nets per day versus an unstaked player of the same town — the honest player-facing number, and the hardest to compute.
- Does the **event table** count toward the ceiling, or only the **passive tier benefits**? Spec `:914-918` excludes cosmetics, information, VFX, lore and *"convenience without resource generation"* — but a build acceleration is convenience **that produces resources sooner**, which is exactly the boundary case and exactly what Tiers II and V grant.

**Without an answer this deliverable cannot be written**, and writing it anyway produces the fourth imaginary gate in this repo's history.

### Q-CLIENTECON. The ceiling is computed server-side and applied client-side. Accept, or does it need enforcement?
§0d. A modified client can ignore any economic modifier ceiling because it applies the modifier itself. Product rule 6 says the backend is authoritative for **rewards**; item grants and rate modifiers are not the same axis. **Options:** (a) accept it and write down that the cap is a design guardrail, not an anti-cheat control (the honest, cheap answer, and the same posture `api/game/save.js:70-72` already takes for soft currency); (b) move economic modifiers behind a server-issued signed effect list; (c) restrict Heartbound to reward classes the backend already controls (items via a grant ledger, information, cosmetics) and drop the rate modifiers — **which is also what spec `:608-615` argues for on design grounds**, independently.

### Q-P2W (restated from WO-1679, and this is the ticket that sets its number). Is economic acceleration allowed at all?
The owner's standing ruling — **whale tier = prestige, never a better army** — is broader than the spec's rule 10, which only bans *combat* dominance. Tier I opens with +2% offline gathering. If the ruling is "prestige only", this ticket's ceiling is **zero** for the recurring-production class and the tier list needs re-specifying (WO-1679).

---

## 5. What NOT to touch

- ⛔ **`JobRushPolicy.IsRandomOutcome` / `AllowsPaidInstantFinish`** and `BuildTimerService.cs:1139`. Owner ruling; `JobRushPolicy.cs:65-67` says removing it is a legal decision. A Heartbound "crafting convenience" may shorten a WAIT; it may never resolve a ROLL.
- ⛔ **`MonetizationCovenantRegression`'s banned-kind and combat-stat lists.** D2 changes coverage, never the semantics. Widening those lists is a different ticket (WO-1673 D2).
- ⛔ **`jewel-polish.json`'s odds** and the fairness doctrine in its `_tuning.fairness`.
- ⛔ **Existing economy values** — `echoes-balance.json`, `storage-caps.json`, `BuildTimerConfig`. This ticket bounds a NEW source; it does not retune the old ones.
- ⛔ **The ad-skip carve-out.** WO-1673 §2b records it as deliberate and owner-ruled, and instructs surfacing rather than closing it. Same posture here.
- ⛔ **Writing "under 10%" anywhere without a meter behind it.** §0c.
- No `.unity` scene files. No `SaveSchema` change.

---

## 6. Evidence index (opened 2026-09-10 at `dev` `abbeb9362`)

`Assets/Editor/Regression/MonetizationCovenantRegression.cs:14,80-83,97-104,198-202`; registered `Assets/Editor/Regression/DataRegression.cs:332`
`Assets/_Modules/Wallet/PackStore.cs:467-473`; `Assets/_Modules/Core/Platform/StakeRewardsResolver.cs:5-10`; `Assets/Editor/Regression/BattleMonthlyRegression.cs:636-638`
`Assets/_Modules/Core/Jobs/JobRushPolicy.cs:65-67,101-113,120`; `Assets/_Modules/Village/Buildings/BuildTimerService.cs:1139`; `Assets/Editor/Regression/DungeonGemExclusivityRegression.cs:162,177`
`Assets/_Modules/Village/Harvest/EchoBonusCalculator.cs:145`; `Assets/_Modules/Village/Harvest/OfflineClaimCoordinator.cs:107,163`
`api/game/save.js:70-72,410-421`
`WorkOrders/WORK_ORDER_1673_google_play_probabilistic_item_disclosures_korea_brazil.md:18` (three imaginary gates, the pattern §0c must not repeat)

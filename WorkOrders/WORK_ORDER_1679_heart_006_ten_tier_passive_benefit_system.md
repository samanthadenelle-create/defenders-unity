# WORK ORDER 1679 — HEART-006: Ten-tier passive benefit system

**Status:** IMPLEMENTED (2026-09-10) — ten-tier benefit table + `IHeartboundBonusProvider` (five members, one flag read, token-free in `DeNelle.Core`) + Q-LADDER polish merge; ceiling measured 0.06/0.10, node 26/26. Offline application deferred (no single accrual point). ✅ Ship-order hazard found and CLOSED in-lane (`api/heartbound/status.js` extended here, so endpoint + provider land together — keep them in ONE commit). Chain 47 CS1061 in `HeartboundBenefitsRegression.cs` fixed (`Regex.Matches` → `MatchCollection` is non-generic, so `var` inferred `object`); whole file swept, every regex result and dereferenced local now explicitly typed — `.RESULT.md` §7. Chain 48: both reds fixed — the "second writer" was a DOC COMMENT tripping a substring lint (no call existed), and the Jeweler red was a SOURCE LINT pinning the pre-merge grant expression, re-pointed with the ruling and made stricter (the tier-1-floor alternative is false on the numbers: a 100 SKR stake measures Tier 0). `.RESULT.md` §8. Not gated, not committed.
**Silo:** Gameplay modifier plumbing (`DeNelle.Core` provider + host wiring). No scene files, no economy retune.
**Raised by:** HEARTBOUND-TRIAGE lane, 2026-09-10.
**Number:** PRE-ASSIGNED by the coordinator. `CLI_LANES_WO_NUMBERS.md` deliberately **NOT** edited by this lane.
**Spec section:** `docs/specs/HEARTBOUND_SKR_RESONANCE_WORK_ORDERS_2026-09-10.md:617-725` (HEART-006).
**Tree:** worktree at `dev` **`abbeb9362`**.

---

## 0. Classification — **NEW benefits over an EXISTING (and mostly unconsumed) modifier layer**

### 0a. ⭐ The provider pattern the spec asks for already exists, twice, and one of them is the exact template

Spec `:1259-1265` mandates an `IHeartboundBonusProvider` so *"Gameplay systems ask the provider what modifiers currently apply. They do not query Solana."*

**Copy `IPolishBonusProvider` verbatim in shape** — `Assets/_Modules/Core/Catalog/PolishBonusProvider.cs`:
- `interface IPolishBonusProvider` `:49` — two members, nothing else
- `NoPolishBonus`, the **zero provider**, `:59-63` — what every player gets by default
- `NativeSkrPolishBonus` `:70-78` — the real one
- `static class PolishBonuses` `:84` — `Install(provider)` `:97-103` (idempotent, null uninstalls), and **`Active` `:109-116`, which returns the zero provider whenever the platform flag is off** so *"a Play-store build behaves exactly as if no staking existed even if one were installed"*
- `[RuntimeInitializeOnLoadMethod(BeforeSceneLoad)]` bootstrap `:166-170`

⭐ **The single most important thing to copy is `Active` (`:109-116`): the platform flag is read in EXACTLY ONE PLACE**, so no call site ever hardcodes a channel check. Its header says so at `:33-34`.

⛔ **And copy the discipline, not just the shape.** `PolishBonusProvider.cs:20-23`: *"This interface therefore exposes ONLY attempt-shaped grants. There is deliberately no member for odds, weights, luck, tier bias or a bonus table, and adding one would break the property the whole economy rests on."* `IHeartboundBonusProvider` must be equally narrow — enumerate the members it may have, and refuse to add a general-purpose "multiplier" bag.

### 0b. ⚠ The lane-bonus holder exists — and three of its four fields are UNCONSUMED STUBS

`Assets/_Modules/Core/State/EchoLaneBonuses.cs:35` — `HarvestBonusMult` `:41`, `CraftingMult` `:44`, `DefenseMult` `:47`, `ExplorationMult` `:50`, all defaulting to **1.0 = no-op** (`:7-10`, the "GameModifiers design law"). Village writes into Core (`EchoBonusCalculator.Recompute`, `Assets/_Modules/Village/Harvest/EchoBonusCalculator.cs:449,456-459`) because Core cannot reference Village.

⛔ **The file states its own consumption status at `:14-23`: all four are WRITE-ONLY today.** `HarvestBonusMult` is a diagnostic mirror — `EchoService.RatePerSecond` (`Assets/_Modules/Village/Harvest/EchoService.cs:136`) reads `AggregateHarvestMultiplier()` live instead. `CraftingMult`, `DefenseMult`, `ExplorationMult` are declared stubs awaiting host wiring, with **zero production readers**.

**So "wire bonuses into existing systems through interfaces" (spec `:1252`) means finishing plumbing that was left declared-but-unwired, not adding to a working system.** That is more work than it looks, and it is shared work — whoever wires `CraftingMult` for Heartbound wires it for Echoes too.

### 0c. ⛔ There is NO build-time / crafting-time modifier seam at all
`Assets/_Modules/Core/Catalog/BuildTimerConfig.cs` + `Assets/_Modules/Core/Jobs/ObsidianQueueEngine.cs` carry the timing; there is no provider indirection on that axis. Tier V "Crafting Inspiration" (spec `:678-686`) and Tier II "Echo Labor" (`:648-654`, construction acceleration) both need one built. `EchoLaneBonuses.CraftingMult` (`:44`) is the declared, unwired forward seam for exactly this.

### 0d. ⛔ Every economic modifier lands in a CLIENT-SIMULATED system
`api/game/save.js:410-421`, verbatim: *"farming / raiding / dungeons / arena have NO per-action endpoint. They are simulated entirely on the client."* Offline gathering runs through `OfflineClaimCoordinator` (`Assets/_Modules/Village/Harvest/OfflineClaimCoordinator.cs:107` — `IOfflineClaimConsumer`, register at `:163`) with four registered consumers, all client-side. **So product rule 6 ("backend authoritative for game rewards") holds for *deciding* the tier and *deciding* the event, and does not hold for *applying* an economic modifier.** See Q-CLIENTECON.

---

## 1. Tier-by-tier landing map

| Tier | Spec | Grants | Lands on | Status |
|---|---|---|---|---|
| I Emberbound | `:632-640` | +2% offline gathering; faint Heartfire particles on the Heart | `OfflineClaimCoordinator` / `EchoService.RatePerSecond:136` | ⚠ economic — Q-P2W |
| II Rootbound | `:642-654` | Echo Labor into the pool; glowing roots | build/collect accel — **no seam** (§0c) | ⚠ Q-ECHOFIG |
| III Stonebound | `:656-666` | Rough Stone Discovery | WO-1678 Q-STONE | ⛔ blocked |
| IV Echoing | `:668-678` | Scout's Whisper | `RaidDeployVM.ScoutIntel` | ⚠ WO-1678 Q-SCOUT |
| V Awakened | `:680-688` | Crafting Inspiration | **no seam** (§0c) | ⚠ |
| VI Hearttouched | `:690-696` | Heartfire Spark | ⛔ WO-1678 Q-HEARTFIRE | ⛔ blocked |
| VII Deep Resonance | `:698-706` | Echo figures in the settlement | `EchoWorldPresence` one-owner rule | ⛔ Q-ECHOFIG |
| VIII Heartforged | `:708-714` | Echo Bloom | WO-1680 (greenfield visual) | — |
| IX Eternal Echo | `:716-724` | Ancient Echo; kingdom-wide ambient resonance | greenfield | — |
| X Heartbound | `:726-748` | max Tree resonance, aura, banner/crest, **choose one of three** | greenfield + WO-1680 | ⚠ Q-P2W |

⭐ Spec `:608-615` is the design rule that should govern every row: higher tiers unlock *"more event varieties, richer world reactions, cosmetic transformations, convenience"* and **"should NOT simply multiply resources forever."** Spec `:722`: *"Tier X should feel prestigious rather than economically mandatory."*

---

## 2. Deliverables

- **D1** — `IHeartboundBonusProvider` + zero provider + `HeartboundBonuses` static with a **single** flag read, modelled on §0a. In `DeNelle.Core`, so hosts can see it; the *chain-touching* implementation stays out of Play (WO-1681 §1).
- **D2** — Wire the tiers that have a seam.
- **D3** — Build the seams that do not exist (§0c), or defer the tiers that need them — a decision, not a coin flip; say which in the RESULT.
- **D4** — A regression that fails if the provider grows an odds-shaped member, mirroring `DungeonGemExclusivityRegression`'s role for `IPolishBonusProvider`.

---

## 3. Acceptance (from spec `:608-615`, `:722`, `:1252-1265`)

1. No gameplay system references SKR, a wallet, or an RPC. Asserted by a source lint, not by reading — spec `:1316-1322`: *"And never: Gathering System → knows what SKR is."*
2. The zero provider is what an unstaked player gets, on every code path, with no branch at the call site.
3. The platform flag is read in exactly one place (§0a).
4. Combined recurring economic acceleration stays under the WO-1682 ceiling — **measured, not asserted**.
5. `COMPILE_GATE_OK`, `python tools/gate_brace.py` clean, zero NUL bytes; `REGRESSION_OK <n>/<n> suites` on a **fresh** log with D4 registered, **proven RED before green**.
6. `**Status:**` flipped in this file in the same commit as the work; `.RESULT.md` written; both paths reported.

---

## 4. Dependencies

**Blocked by:** WO-1674, WO-1676 (tiers), WO-1678 (five of the ten tiers unlock an event that ticket has to survive first), WO-1682 (the ceiling), and Q-P2W / Q-ECHOFIG / Q-LADDER below.
**Blocks:** WO-1681 (the panel lists current passive effects), WO-1686.

---

## 5. ⛔ OWNER QUESTIONS

### Q-P2W. Tiers I, II and V are **economic acceleration bought with a financial position.** Is that inside "whale tier = prestige, never a better army"?
Spec rule 10 (`:25`) bans *"pay-to-win combat dominance"* — a **combat** test. The owner's standing ruling (memory `solana-store-early-access-pack-pricing`) is broader: **whale tier = prestige, never a better army**. Between them sits everything HEART-006 actually grants at the low tiers: +2% offline gathering (I), construction acceleration (II), crafting convenience (V), a resource surge (the event table).

None of it is combat power. All of it is **compounding economic advantage proportional to staked capital**, and in a base-builder the economy *is* the progression. Spec `:608-615` and `:722` argue for the answer — varieties and cosmetics, not multipliers — but the tier list as written opens with a resource multiplier at Tier I.

**Ruling needed:** is the economic-acceleration class allowed at all, and if so under what total ceiling (WO-1682)? If the answer is "prestige only", tiers I/II/V need re-specifying and this WO cannot start.

### Q-ECHOFIG. Tier II "Echo Labor" and Tier VII "Echo Worker Manifestations" — modifiers, or figures in the world?
If either puts a visible Echo in the settlement it collides with the regression-pinned single-appearance-owner rule (`EchoWorldPresence`, `EchoAutoDeployTrigger.cs:222`; `EchoWorldPresenceRegression.cs:69`; CLAUDE.md §7). As **pure modifiers** both are fine and cost nothing structurally. Spec `:682` says *"Temporary Echo figures can appear in the settlement"*, which is the colliding reading. **Rule which, so the lane does not have to guess.**

### Q-LADDER (restated from WO-1675). Ten Heartbound tiers, or eleven?
`stake-rewards.json` + `StakeRewardsResolver` (`Assets/_Modules/Core/Platform/StakeRewardsResolver.cs:152`) already grant cosmetic rewards on a stake ladder, with a shipped panel. HEART-006 is a second ladder over the same wallet. Fold or run both — the ruling lands in WO-1675, but **this is the ticket where two ladders would be visible to the player as two tier numbers.**

---

## 6. What NOT to touch

- ⛔ **`IPolishBonusProvider`'s two members.** Copy its shape; do not extend it, and do not make `IHeartboundBonusProvider` a superset that could serve both. Its narrowness is the point (`PolishBonusProvider.cs:20-23`).
- ⛔ **`EchoBonusCalculator`'s additive spec-SUM.** CLAUDE.md §7 records that "DOUBLES the yield" was false and retired, and that a seat implementing the retired sentence *"would have shipped a ~20x buff"*. Heartbound stacks **beside** Echo bonuses; do not fold it into `LaneContribution`.
- ⛔ **`echoes-balance.json` values.** Not this ticket's axis.
- ⛔ **`EchoLaneBonuses`' ownership split** (`:12-25`): Village writes, hosts read, Core cannot reference Village. Wiring a consumer is welcome; inverting the direction is not.
- ⛔ **Anything named SKR inside a gameplay assembly.** Spec `:1316-1322`. The provider is the membrane.
- No `.unity` scene files. No `SaveSchema` change.

---

## 7. Evidence index (opened 2026-09-10 at `dev` `abbeb9362`)

`Assets/_Modules/Core/Catalog/PolishBonusProvider.cs:20-23,33-34,49,59-63,70-78,84,97-103,109-116,166-170`
`Assets/_Modules/Core/State/EchoLaneBonuses.cs:7-10,12-25,35,41-50`
`Assets/_Modules/Village/Harvest/EchoBonusCalculator.cs:449,456-459`; `Assets/_Modules/Village/Harvest/EchoService.cs:136`
`Assets/_Modules/Village/Harvest/OfflineClaimCoordinator.cs:107,163`
`Assets/_Modules/Core/Catalog/BuildTimerConfig.cs:231,269`; `Assets/_Modules/Core/Jobs/ObsidianQueueEngine.cs`
`Assets/_Modules/Core/State/GameModifiers.cs:30,32,95` (the "default = all no-op" precedent)
`Assets/_Modules/Village/World/Camps/EchoAutoDeployTrigger.cs:222`; `Assets/Editor/Regression/EchoWorldPresenceRegression.cs:69`
`Assets/_Modules/Core/Platform/StakeRewardsResolver.cs:152`
`api/game/save.js:410-421`

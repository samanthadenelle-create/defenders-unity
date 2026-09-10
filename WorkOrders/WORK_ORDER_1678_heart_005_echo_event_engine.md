# WORK ORDER 1678 — HEART-005: Echo Event engine

**Status:** IMPLEMENTED 2026-09-10 — event engine + config + 23-case node oracle, the client presentation/application seam (Core/Wallet/Village, all `#if DAPP_STORE`), the ruled Heartfire second source with its lint re-pointed in the same change, and `HeartboundEventRegression` pinning all four rulings. Not gated in Unity (no Unity run in this lane); see the RESULT for what is proven and what is not.
**Silo:** Backend event generation + a client reveal surface + one grant seam. Touches the rough-stone chain, Heartfire and the Echo appearance owner. **Highest conflict density of the thirteen.**
**Raised by:** HEARTBOUND-TRIAGE lane, 2026-09-10.
**Number:** PRE-ASSIGNED by the coordinator. `CLI_LANES_WO_NUMBERS.md` deliberately **NOT** edited by this lane.
**Spec section:** `docs/specs/HEARTBOUND_SKR_RESONANCE_WORK_ORDERS_2026-09-10.md:469-616` (HEART-005).
**Tree:** worktree at `dev` **`abbeb9362`**.

---

## 0. Classification — **NEW engine, but SEVEN of its nine events land on EXISTING, RULED, REGRESSION-PINNED systems**

| Spec event | Line | Lands on | New or existing | Conflict |
|---|---|---|---|---|
| Rough Stone Discovery | `:515-521` | `DungeonRunPayout.GrantRoughStone` → `DungeonController.BankRoughStone` | **EXISTING, owner-ruled 2026-09-09** | ⛔ **Q-STONE** |
| Resource Surge | `:523-533` | `EchoService.RatePerSecond` / node harvest | EXISTING | ⚠ economic; WO-1682 |
| Scout's Whisper | `:535-549` | `RaidDeployVM.ScoutIntel` | **EXISTING surface, regression-pinned** | ⚠ Q-SCOUT |
| Crafting Inspiration | `:551-563` | `ObsidianQueueEngine` / `BuildTimerConfig` | EXISTING, **no modifier seam** | ⚠ |
| Echo Labor | `:565-577` | build/collect acceleration | EXISTING | ⚠ Q-ECHOFIG |
| Wandering Merchant | `:579-589` | — | **GREENFIELD — does not exist** | — |
| Heartfire Spark | `:591-597` | `HeartfireCharges` / `HeartfireService` | **EXISTING, one-source-by-design, lint-pinned** | ⛔ **Q-HEARTFIRE** |
| Echo Bloom | `:599-609` | the Heart's visual | **GREENFIELD** (WO-1680) | — |
| Ancient Echo | `:611-623` | lore/cosmetic | GREENFIELD | — |

Nothing named "Heartbound", "Resonance" or "Echo Event" exists in code today: `grep -rni "heartbound\|resonance" --include=*.cs Assets` → **zero hits**.

---

## 1. The seams, read at source

### 1a. ⛔ ROUGH STONE — a THIRD faucet on a supply the owner capped nine days ago

The stone is `ing_rough_stone` (`Assets/_Modules/Core/Catalog/DungeonExclusiveItems.cs:50`, fenced `:59`). The **single grant writer** is `DungeonController.BankRoughStone` (`Assets/_Modules/Dungeons/DungeonController.cs:660-693`), installed project-wide as *the* authority at boot (`InstallRoughStoneGrantAuthority`, `:703-708`) behind the delegate `DungeonRunPayout.RoughStoneGrantAuthority` (`Assets/_Modules/Core/Catalog/DungeonRunPayout.cs:179`, invoked `:189-191`).

Its supply is **owner-ruled and rate-limited on both existing faucets**. The ruling, quoted verbatim into `Assets/Editor/Regression/RaidRoughStoneDropRegression.cs:16-18`:

> *"there is only one stone type till it gets to jeweler, and then its RND. So only top two tiers of raids can drop stone and no more than 1 per day. 5% drop rate in dungeons not included the starter dungeons"*

Enforced by three tunables — `raid.roughStoneMinTier` = 3, `raid.roughStonePerDayCap` = 1, `dungeon.roughStoneDropPct` = 5 (`Assets/_Modules/Core/Ops/RemoteTunables.cs:713-728`) — and pinned by `RaidRoughStoneDropRegression` (four cases, `:20-28`) and `RemoteTunablesDefaultsRegression.cs:307-313`.

**HEART-005 adds a third source with no cap named anywhere in the spec, and its rate is set by how much SKR the player has staked.** That is the ticket's single most consequential conflict. See Q-STONE.

### 1b. ⛔ HEARTFIRE HAS EXACTLY ONE SOURCE, BY DESIGN, AND A LINT ENFORCES IT

`Assets/_Modules/Core/State/HeartfireCharges.cs:13-33`, the file's own banner:

> `HEARTFIRE IS A CHARGE, NOT A CURRENCY. THIS IS THE WHOLE DESIGN.` … `IF THE IMPLEMENTATION EVER GROWS A BALANCE, IT IS WRONG.` … `The distinction is mechanical, not decorative: a currency has a SOURCE the player can influence (produce it, buy it, loot more of it)… Heartfire has exactly one source — the passage of time — and exactly one sink — marching. Nothing the player does makes it arrive faster`

`HeartfireRegression.CurrencyLintCases` (`Assets/Editor/Regression/HeartfireRegression.cs:334-382`, registered as PIN B at `:120`) **fails on the literal tokens** `"Wallet"`, `"Price"`, `"Vendor"`, `"ResourceType"`, `"AddResource"`, `"TrySpendResources"`, `"StorageCap"` and others appearing in `HeartfireCharges.cs` or `HeartfireService.cs` (`:346-352`), and on a Heartfire row in `storage-caps.json` / `packs.json` (`:360-363`), and on `Heartfire` reaching `Core/State/Enums.cs` (`:365-371`).

**A "Heartfire Spark" that adds a charge would give Heartfire a second source — one the player influences by staking SKR — which is exactly the sentence the design forbids.** See Q-HEARTFIRE.

⚠ There is a second, independent precedent: `WelcomeBackDoorsVM.cs:30-43` records that a "Heartfire is full" return door was **deliberately NOT BUILT** and raised as an unruled owner question, for this same reason. A lane that adds a Heartfire faucet here would be answering that question silently.

### 1c. ⚠ SCOUT'S WHISPER — the surface exists and is regression-pinned. Extend, do not mutate.

`Assets/_Modules/Village/Hero/RaidDeployVM.cs` already produces a scout report: `_scoutReport` `:195`, `ScoutReport` `:204`, `ScoutIntel` (view-side projection) `:221-228`, built by `BuildScoutReport()` `:498-538` — walls `:508`, garrison `:523`, boss `:527`, spoils `:535`, fallback `:537`. The view paints three lines in a 157 reference-px well (`RaidDeployScreen.cs:689-691, 724-726, 736-740`), and its header at `:41` names *"the deeper analysis"* as still TODO — **this is the natural host.**

⛔ **`ScoutReport`'s LAST line must remain the spoils estimate** — pinned by `RaidDeployZeroArmyRegression` `[zero-army-spoils]`, contract stated at `RaidDeployVM.cs:213-218`. Add a new property; do not append to or reorder that list.

### 1d. ⚠ ECHO FIGURES — one appearance owner, regression-pinned

`EchoWorldPresence` lives at `Assets/_Modules/Village/World/Camps/EchoAutoDeployTrigger.cs:222` (⚠ **not** in a file of its own — `find -name "EchoWorldPresence*"` finds only the regression). CLAUDE.md §7 and `Assets/Editor/Regression/EchoWorldPresenceRegression.cs:69` pin *one owner, one lifecycle, no second spawner*. Tier VII's "Echo Worker Manifestations" (spec `:696-702`) and Tier II's "Echo Labor" must therefore be **pure modifiers**, or they need this ruling. See Q-ECHOFIG (routed to WO-1679, restated here because the event table is where it bites).

### 1e. The grant seam, and the honest limit on it

`Assets/_Modules/Village/Crafting/VillageInventory.cs` is the single item store: `Add(id, amount)` `:110`, **`AddEarned(id, amount)` `:124`** — the *reward authority* grant, deliberately distinct so that *"Shop/dev/plain inventory Add calls deliberately cannot reveal the Jeweler"* (`DungeonController.cs:675-677`). `HasEverAcquired` `:139`. `Assets/_Modules/Village/Items/ItemInventory.cs:98` (`GrantDrop`) is a session facade that routes through it, not a second store.

⛔ **All of it is CLIENT-SIDE.** There is no server-side item grant route: the backend grants *entitlements*, the client grants *items* (`api/game/save.js:410-421`). So an Echo Event's reward is **decided** by the backend and **applied** by the client. The claim ledger (WO-1677 D2) is what makes it once-only; the application is trusted. **Say this in the design rather than implying server authority the repo does not have.**

### 1f. The determinism requirement is satisfiable and cheap

Spec `:493-503`: `SHA256(globalPulseId + playerId + eventTableVersion)`. Node's `crypto` is already required in `api/_lib/wallet-auth.js:71`. Same inputs, same event — assert it in a unit test (WO-1685).

### 1g. ⚠ A new reward table is NOT swept by the monetization gate

`MonetizationCovenantRegression` fails on any probability/odds/roll/chance/random field in a monetization JSON (`Assets/Editor/Regression/MonetizationCovenantRegression.cs:14,80-83,198-202`) — but **its file list is hardcoded, six entries at `:97-104`**. A new `heartbound-events.json` carrying event weights is **unswept**, and the gate stays green. WO-1673 D2 already exists to derive that list; **this ticket must not land its reward table before D2, or must add itself to the list explicitly and say so.**

---

## 2. Deliverables

- **D1** — `heartbound_echo_event` table + the deterministic seeded generator (`:493-503`), event table versioned so a re-roll of history is impossible.
- **D2** — `heartbound-events.json` (weights, amounts, durations). Spec `:947`: *"All reward amounts and weights belong in data/config. No reward values hardcoded in presentation classes."* Coordinate with WO-1673 D2 (§1g).
- **D3** — the claim seam: backend decides, client applies through `VillageInventory.AddEarned` for items; once-only enforced by WO-1677 D2's PK.
- **D4** — the reveal surface (the "Echo Event" card the player sees on return). Naming discipline from `:483-491`: never *lottery / jackpot / bet / wager*; no SKR is consumed.
- **D5** — per-event wiring, **each one blocked on its own question below**.

---

## 3. Acceptance (from spec `:493-503`, `:1122-1136`)

1. Same `(pulseId, playerId, tableVersion)` always yields the same event — asserted, not observed.
2. An event is claimable exactly once; re-opening the screen cannot duplicate it (spec `:1154`).
3. No event awards SKR, SOL, USDC, a withdrawable token or uncapped premium currency (spec `:919-931`) — asserted by a source/config lint, not by reading.
4. The player-facing copy contains none of the four banned words (`:485-489`) — the `PackCatalogTest.cs:157-161` precedent already lints `loot/gacha/random/mystery/lottery/spin/gamble` on pack names; reuse that shape.
5. Every reward value comes from config, none from a presentation class (`:947`).
6. `**Status:**` flipped in this file in the same commit as the work; `.RESULT.md` written; both paths reported.

---

## 4. Dependencies

- **Blocked by:** WO-1674, WO-1675, WO-1676, WO-1677 — and by Q-STONE / Q-HEARTFIRE below, either of which can remove an event from the table entirely.
- **Related:** WO-1673 D2 (§1g), WO-1673 D6 (the Korea/Brazil analysis — see Q-STONE).
- **Blocks:** WO-1679, WO-1680, WO-1681, WO-1686.

---

## 5. ⛔ OWNER QUESTIONS

### Q-STONE. **A staked financial position would become a source of the rough stone — the input to the game's one real RNG.** This is the most compliance-sensitive item in the whole spec.

Three facts, each read at source:
1. The stone's supply is **owner-ruled and capped on both existing faucets** (§1a). HEART-005 adds a third with no cap.
2. The stone's *only* use is the Jeweler's polish, which is a **weighted random roll with a 15% shatter chance** (`Assets/Resources/Data/Canonical/jewel-polish.json`, `rePolishShatterChance: 0.15`; the roll at `Assets/_Modules/Village/Crafting/JewelPolishService.cs:397`).
3. WO-1673 (`WorkOrders/WORK_ORDER_1673_google_play_probabilistic_item_disclosures_korea_brazil.md`) already flags the *weaker* version of this as the regulator's first look — `:14`: *"native-SKR staking buys extra ATTEMPTS at the polish roll… both are the shape a regulator looks at first, so both are named rather than waved off."*

**HEART-005 escalates "staking buys attempts" to "staking produces the stone."** Against Korea's unqualified *"probabilistic items"* wording and Brazil's Digital ECA **prohibition** on a 13+ app (WO-1673 `:28-40`) — both of which WO-1673 D6 records as **NOT YET READ AT SOURCE** — this is not a detail to decide in code.

**Options:** (a) drop Rough Stone Discovery from the event table entirely and let Tier III unlock something non-random; (b) keep it with a hard cap that counts against the same daily budget as the raid faucet (one stone per UTC day across *all* sources, extending `RaidScoring.RoughStonesGrantedToday()` `:1060` / `MarkRoughStoneGranted()` `:1073-1083`); (c) keep it uncapped. ⛔ **(c) should not be chosen without WO-1673 D6's Korea and Brazil readings in hand.**

### Q-HEARTFIRE. Is Heartfire allowed a SECOND source?
§1b. Today: one source (time), one sink (marching), and a build gate that fails on the words a currency would need. A pulse-granted Heartfire benefit makes the pool **influenceable by staking**, which is the exact property the design document says makes it safe. **Options:** (a) drop Heartfire Spark; (b) rule that Heartfire may have a second, capped, non-purchasable source and update `HeartfireCharges.cs:13-33` + the lint in the same change (CLAUDE.md §15 — the doc and the code move together); (c) make "Heartfire Spark" purely *presentational* — the Heart's fire flares, no charge is granted — which satisfies spec `:593-597` ("a small Heartfire-related game benefit… must integrate with existing Heartfire mechanics rather than introduce a second Heartfire system") only if "benefit" is allowed to mean spectacle. **(c) is the cheapest and needs a word from the owner on whether it still reads as a reward.**

### Q-SCOUT. Does Scout's Whisper reveal information the player could not otherwise get, or only surface it earlier?
`ScoutReport` already tells the player walls, garrison, boss and spoils (§1c). The spec's examples (`:541-547`) — enemy composition, resistance, recommended troop type, reward-preview refinement — are **new information**, and a raid advantage bought with a financial position is closer to pay-to-win than the spec's own rule 10 ("no combat dominance") admits, even though it is information rather than damage. Spec `:543` says *"Prefer information over raw combat strength"*, which is a preference, not a limit. **Ruling needed on where the line is.**

### Q-MERCHANT. Wandering Merchant is greenfield — build it here, or defer?
`grep -rniE "TemporaryNpc|VisitingNpc|NpcVisit|SpawnTemporary|DespawnAfter" --include=*.cs Assets/_Modules` → **zero hits**. Every vendor in the game is permanent and placement-driven (`Assets/_Modules/Village/NPCs/CastleVendorNpcInjector.cs:43`, `Assets/_Modules/Village/Hero/VendorRegistry.cs:104`); the only conditional-presence behaviour is `CastleVendorWaveHider` (`:1266`), which hides vendors during a wave. A visiting merchant is a feature in its own right and is larger than the rest of this ticket combined. **Recommend deferring it to its own WO** rather than smuggling an NPC lifecycle system in under an event-table row.

---

## 6. What NOT to touch

- ⛔ **The rough-stone drop RATES.** `raid.roughStoneMinTier` / `raid.roughStonePerDayCap` / `dungeon.roughStoneDropPct` are an owner ruling from 2026-09-09 (`RemoteTunables.cs:681-700` records the ruling verbatim and why two of the three defaults deliberately depart from prior behaviour). Q-STONE may *add* a source; it never retunes these.
- ⛔ **`DungeonController.BankRoughStone` as the single grant authority.** If Q-STONE rules (b), the Heartbound path routes *through* `DungeonRunPayout.GrantRoughStone` (`:189-191`) so there stays exactly one writer. **Do not add a second `AddEarned("ing_rough_stone", …)` call site.**
- ⛔ **`jewel-polish.json`'s odds.** `_tuning.fairness`: *"THE PER-ROLL TABLE IS IDENTICAL ON EVERY PATH — free, ad-funded and paid alike… Money buys ATTEMPTS, never better odds."* An Echo Event may never touch a probability.
- ⛔ **`HeartfireCharges.cs` / `HeartfireService.cs`** until Q-HEARTFIRE is ruled. Even a *comment* mentioning a wallet trips the lint (`HeartfireRegression.cs:346-352`).
- ⛔ **`RaidDeployVM.ScoutReport`'s ordering.** §1c — pinned; add a property.
- ⛔ **`JobRushPolicy`** (`Assets/_Modules/Core/Jobs/JobRushPolicy.cs`). "Sell the WAIT, never the ROLL" is an owner ruling and `:65-67` says removing it *"is a legal decision, not an engineering one."* Crafting Inspiration reduces a WAIT, which is allowed; it may never resolve a random outcome.
- ⛔ **`EchoWorldPresence`'s single-owner rule.** §1d.
- No `.unity` scene files. No `SaveSchema` change.

---

## 7. Evidence index (opened 2026-09-10 at `dev` `abbeb9362`)

`Assets/_Modules/Core/Catalog/DungeonExclusiveItems.cs:50,59`; `Assets/_Modules/Dungeons/DungeonController.cs:660-693,703-708`; `Assets/_Modules/Core/Catalog/DungeonRunPayout.cs:179,189-191`
`Assets/_Modules/Village/Troops/RaidScoring.cs:1007,1060,1073-1083`; `Assets/_Modules/Core/Ops/RemoteTunables.cs:681-700,713-728`; `Assets/Editor/Regression/RaidRoughStoneDropRegression.cs:16-28,86-91`; `RemoteTunablesDefaultsRegression.cs:307-313`
`Assets/_Modules/Core/State/HeartfireCharges.cs:13-33`; `Assets/Editor/Regression/HeartfireRegression.cs:120,334-382`; `Assets/_Modules/Village/Harvest/UI/WelcomeBackDoorsVM.cs:30-43`
`Assets/_Modules/Village/Hero/RaidDeployVM.cs:195,204,213-218,221-228,498-538`; `Assets/_Modules/Village/Hero/RaidDeployScreen.cs:41,689-691,736-740`
`Assets/_Modules/Village/World/Camps/EchoAutoDeployTrigger.cs:222`; `Assets/Editor/Regression/EchoWorldPresenceRegression.cs:69`
`Assets/_Modules/Village/Crafting/VillageInventory.cs:110,124,139`; `Assets/_Modules/Village/Items/ItemInventory.cs:98`
`Assets/Resources/Data/Canonical/jewel-polish.json` (`rePolishShatterChance: 0.15`, `_tuning.fairness`); `Assets/_Modules/Village/Crafting/JewelPolishService.cs:397`
`Assets/Editor/Regression/MonetizationCovenantRegression.cs:14,80-83,97-104,198-202`; `Assets/Data/Tests/PackCatalogTest.cs:157-161`
`Assets/_Modules/Core/Jobs/JobRushPolicy.cs:65-67`
`WorkOrders/WORK_ORDER_1673_google_play_probabilistic_item_disclosures_korea_brazil.md:14,28-40,138`
`api/game/save.js:410-421`; `api/_lib/wallet-auth.js:71`
`grep -rni "heartbound\|resonance" --include=*.cs Assets` → **0 hits** (the proof this engine is new)
`grep -rniE "TemporaryNpc|VisitingNpc|NpcVisit|SpawnTemporary|DespawnAfter" --include=*.cs Assets/_Modules` → **0 hits** (the proof for Q-MERCHANT)

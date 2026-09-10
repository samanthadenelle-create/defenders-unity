# WORK ORDER 1673 — Google Play probabilistic-item obligations (Korea + Brazil) now that the listing is open in all countries

**Status:** READY TO IMPLEMENT
**Silo:** Compliance / publishing + one editor regression + one in-app disclosure surface. No gameplay tuning, no economy rebalance, no scene files.
**Raised by:** COMPLIANCE lane, 2026-09-10, on the owner's 12:20 ruling that the Play listing is open in **all countries**.
**Number:** PRE-ASSIGNED by the coordinator. `CLI_LANES_WO_NUMBERS.md` deliberately **NOT** edited by this lane.
**Web access:** available this session. The policy page WAS fetched and is quoted verbatim below. No item in this WO is marked "policy text to be re-read at the source URL" for the Play page itself; the two **statutes** it points at were not fetched and are flagged individually.

---

## 0. The one-paragraph answer, before the detail

**The game's compliance posture is already unusually strong, and the risk is not where the ticket's framing assumes.** There is an owner-ruled, regression-pinned firewall — *"sell the WAIT, never the ROLL"* — that makes it impossible to pay currency to resolve a random outcome, and a build gate that fails on any probability field appearing in a monetization JSON. Google Play's odds-disclosure rule is scoped to *"randomized virtual items **from a purchase**"*, and a full read-only sweep of every RNG site (§2e, swept 2026-09-10) found **six live random-reward mechanics, not one of which takes real money or hard currency as a direct input.** On that evidence the Play odds rule does not currently bite.

⚠ **Two adjacencies keep that from being a flat "no", and both are written up in §2e:** the **Arena wager** stakes *real, purchasable Crystals* (dormant behind `ff.arena=false`), and **native-SKR staking buys extra ATTEMPTS** at the polish roll (compiled off for Play). Neither is a randomized item bought with money; both are the shape a regulator looks at first, so both are named rather than waved off.

**Four things nevertheless need doing, and two of them are genuine defects:**

1. ⛔ **`JewelPolishRegression` DOES NOT EXIST.** `JewelPolishService.cs:425` states that it "fails if a second odds source appears or if these numbers stop matching the table." `grep -rn "JewelPolishRegression" .` over the whole repo returns **exactly one hit — that sentence itself**. The one mechanic in the game that already discloses odds to the player has an **imaginary** gate protecting the accuracy of that disclosure. This is the *same* comment-only-firewall failure `MonetizationCovenantRegression`'s own header was written to close ("skr_staking.json says a 'SkrStakingRegression' rejects combat/stat grants — that code never existed").
2. ⚠ **Brazil's rule is a PROHIBITION, not a disclosure duty**, and it is scoped by audience age. The app declares **minimum age 13** (`publishing/config.yaml:204`) — i.e. *adolescents*. That needs an owner ruling and a written analysis, not a checklist tick.
3. ⛔ **`jewel-polish.json` `_tuning.dropModel` STILL SAYS THE ROUGH STONE IS "GUARANTEED" — it is 5%.** WO-1373 changed the drop on 2026-09-09 and did not move the note. Anyone answering a store questionnaire from that file would state a guaranteed drop that does not exist. Second defect; §2e has the proof and D7 the fix.
4. ⚠ **There is no repo artifact for the Google Play store listing at all.** `publishing/config.yaml` is the **Solana dApp Store** listing. Any Play-listing disclosure text has nowhere to live and no gate.

---

## 1. What the policy actually says (fetched 2026-09-10, quoted verbatim)

### 1a. The general Play rule — `support.google.com/googleplay/android-developer/answer/9858738`, section **Payments**

> "Apps and games offering mechanisms to receive randomized virtual items from a purchase including, but not limited to, 'loot boxes' must clearly disclose the odds of receiving those items in advance of, and in close and timely proximity to, that purchase."

**Load-bearing scope words: "from a purchase"** and **"in close and timely proximity to, that purchase"**. The duty is triggered by a *purchase*, and the disclosure location is *next to the purchase*, not the store listing.

### 1b. South Korea — `.../answer/6223646`, section *"Requirements for games that offer mechanism to receive randomized virtual items from a purchase"*

> "games that offer 'probabilistic items' must comply with the content and method of display, including information on the probability of offers for each type of probabilistic item."

Statute named on the page: **"Enforcement Decree of the Act on the Promotion of the Game Industry of the Republic of Korea."** The page also references the **GRAC "Guideline on the Disclosure of Probability Information."**

⚠ **TWO THINGS THE PAGE DOES NOT SETTLE, AND WE MUST NOT GUESS THEM (§11B):**
- **Whether the Korean duty reaches FREE / earned probabilistic items**, or only paid ones. The Play page's *section heading* says "from a purchase", but the quoted statutory sentence says "probabilistic items" unqualified. **The Enforcement Decree and the GRAC guideline were NOT fetched — policy text to be re-read at the source.** This is the single question that decides whether §2's mechanics are in scope at all.
- **Where the probability must be displayed** (in-game / store listing / advertising). Not stated on the Play page. **To be re-read at the GRAC guideline.**

### 1c. Brazil — same page, section *Digital ECA requirements*

> "prohibiting loot boxes in games aimed at children and adolescents or likely to be accessed by them."

Statute named: **"Digital Child and Adolescent Statute (Digital ECA)."**

⛔ **THIS IS A PROHIBITION, NOT A DISCLOSURE DUTY, AND THE TICKET THAT COMMISSIONED THIS WO ASSUMED OTHERWISE.** No amount of odds text satisfies it. The only two levers are (a) the mechanic is not a loot box, or (b) the game is not aimed at / likely accessed by adolescents. The app is **13+**, so (b) is not available on today's declaration. **The Digital ECA text itself was NOT fetched — policy text to be re-read at the source**, specifically for its definition of "loot box" and whether an unpurchasable random reward falls inside it.

---

## 2. The probabilistic mechanics in this game — cite these, do not re-derive

### 2a. ⭐ The Jeweler's POLISH — the one real probabilistic mechanic, and it already discloses

| Fact | Evidence |
|---|---|
| The roll | `Assets/_Modules/Village/Crafting/JewelPolishService.cs` — weighted table roll applied at job completion |
| Odds authored | `Assets/Resources/Data/Canonical/jewel-polish.json` + the StreamingAssets twin. **`cmp` says the two copies are byte-IDENTICAL** (checked this session) |
| The numbers | `"rePolishShatterChance": 0.15` (`:28`); score-row weights e.g. `ing_ember_crystal 0.70 / ing_aether_shard 0.26 / …` (`:35-37`). The file's own `_schemaNotes.weights` says they "are authored to sum to 1.000 anyway so the table can be read as probabilities at a glance" |
| Cost | **TIME ONLY.** `JewelPolishService.cs:322` — "Cost is TIME ONLY (WO-1042 §5(3)): the stone was already earned by descending". No currency, no real money |
| Disclosure ALREADY EXISTS | `JewelPolishConfirmPanel.cs:83-153` renders `DescribeOdds(...)` before the player commits, including `"There is a " + shatterPct + "% chance the stone is destroyed and you get nothing."` |
| Disclosure is DERIVED, not authored twice | `JewelPolishService.DescribeOdds` (`:429`) computes from the roll table; its doc block says **"⛔ ALWAYS DERIVED FROM THE ROLL TABLE, NEVER AUTHORED SEPARATELY"** — and it correctly models the shatter roll as happening first and independently, so the gem odds are the remaining probability mass rather than being overstated |
| The panel REFUSES rather than hiding | `JewelPolishConfirmPanel.cs:88` — `FlowTrace.Fail` "confirm REFUSED: no odds to disclose" |

**This is the reuse pattern for everything else in this WO. Do not invent a second odds surface.**

### 2b. ⭐ The paid-rush firewall — *"sell the WAIT, never the ROLL"*

`Assets/_Modules/Core/Jobs/JobRushPolicy.cs`. Owner ruling 2026-08-16, quoted into every refusal (`:87-90`):

> "a paid instant resolve of a RANDOM outcome is mechanically a loot box and is regulated in several jurisdictions in the shipping plan (owner ruling 2026-08-16); ads and waiting are allowed - sell the WAIT, never the ROLL"

- `IsRandomOutcome(JobKind.JewelPolish) => true` (`:101-113`).
- `AllowsPaidInstantFinish(kind) => !IsRandomOutcome(kind)` (`:120`).
- Enforced at the money seam: `BuildTimerService.cs:1139` returns 0 crystals-to-finish for a random-outcome job.
- Pinned by `DungeonGemExclusivityRegression` (marker `DUNGEON_GEM_EXCLUSIVITY_OK`, `:162`, `:177`), which fails if a random kind becomes paid-finishable **or if an entry point stops consulting the policy**.
- The file says the quiet part out loud (`:65-67`): *"If you are here because the restriction looked arbitrary and you want to remove it: it is not arbitrary, and removing it is a legal decision, not an engineering one."*

⚠ **THE ONE DELIBERATE HOLE, AND IT IS DOCUMENTED AS DELIBERATE:** `:77` and `:118` — **ad-skip is NOT gated**; a rewarded ad may skip any kind, including a random one. That is not a "purchase" under §1a's wording, but it *is* monetized (ad revenue). **Flagged for the owner in §5, not changed here.**

### 2c. The monetization covenant gate — already built, already registered

`Assets/Editor/Regression/MonetizationCovenantRegression.cs`, registered at `Assets/Editor/Regression/DataRegression.cs:332`. It FAILS on **"any probability/odds/roll/chance/random field"** in a monetization JSON (`:14`, `:80-83`, `:198-202`).

⚠ **Its file list is HARDCODED — six entries at `:97-104`** (`packs.json` ×2, `skr_store.json`, `skr_staking.json`, `battle_monthly_packs.sample.json`, `economy_store_packs.sample.json`). A new paid-pack JSON added tomorrow is **not swept**, and the gate stays green. That is duplicated state of exactly the kind CLAUDE.md §2/§5/§16 each describe, sitting inside the file that is supposed to be the firewall.

### 2d. The canon's standing claim — **verify, do not inherit**

- `docs/monetization-v2-spec.md:39` — "C3 — no loot boxes, no gacha, no randomized purchases ✅ — every pack shows its full contents pre-purchase."
- `docs/design/LIVEOPS_RETENTION.md:42` — "A daily quest *rolls* (free gameplay variety) but its **reward is fixed per slot** — no paid mystery box, ever."
- `docs/SECURITY_COMPLIANCE_HARDENING_AUDIT.md:227` — "**(Loot-box odds-disclosure rule is N/A — no loot boxes — a genuine advantage.)**"

⛔ **That last sentence is the one this WO must not simply repeat.** It is undated relative to today's code, and the whole reason this ticket exists is that "N/A" was asserted before the listing went worldwide. §3 Step 1 is to re-prove it at HEAD.

### 2e. ✅ THE FULL INVENTORY — swept 2026-09-10, read-only, every row cited

**Six LIVE random-reward mechanics and two DORMANT ones. Not one of them takes real money or hard currency as a direct input.** Parity between the `Resources` and `StreamingAssets` copies was `diff`-verified clean for `loot-tables.json`, `jewel-polish.json`, `packs.json`, `stake-rewards.json`, `kill-rewards.json` and `ad-placements.json`.

| # | Mechanic | The RNG line | Odds authored at | Input | Reachable |
|---|---|---|---|---|---|
| 1 | **Jewel Polish** (gem tier) | `JewelPolishService.cs:397` (`UnityEngine.Random.value * total`, in `RollOutcome`; live caller `:521`) | `jewel-polish.json` `outcomes[].weights[]` | earned stone + **time only** | **YES** |
| 1b | **Polish shatter** | `JewelPolishService.cs:511` | `rePolishShatterChance: 0.15` | same | **YES** (re-polish only) |
| 2 | **Dungeon rough stone** | `DungeonController.cs:612` (predicate `:772`) | `RemoteTunables.cs:719` `DungeonRoughStoneDropPctDefault = 5`, key `"dungeon.roughStoneDropPct"` | completing a run | **YES** |
| 3 | **Enemy / boss / container loot** | `LootTableCatalog.cs:139` and `:143` (in `Roll`, `:127`) | `loot-tables.json` `tables[].drops[]` — 20 tables | combat | **YES** — `ItemDropSystem.cs:58-63` `DefaultEnabled = true` in **both** branches (the file header's "SHIPS DARK" comment is stale; the const contradicts it) |
| 4 | **Arena gear drop** | `BattleArena.cs:3213` (rarity `:3224`, slot `:3227`) | consts `BattleArena.cs:3205-3207` — base 0.04, max 0.04 | winning a fight | **YES** (`FeatureFlags.OverworldEncounter`, `FeatureFlags.cs:184`, default **true**) |
| 5 | **Rare boss stage roll** | `BattleArena.cs:1656-1657` | `BattleArena.cs:170` `BossSpawnChance = 0.05f` | free | **YES** |
| 6 | **Per-kill reward variance** | `Enemy.cs:3431` / `:3450` / `:3547-3549` → `EnemyDef.RollReward` | `kill-rewards.json` + per-enemy `rewardVariance` in `enemies.json` | every kill | **YES** — but this is *amount variance on a guaranteed reward*, not a random-item pull, and would not normally be a disclosable "probabilistic item" |

Polish odds, all four score rows (`DungeonRunGrade.PolishScore` 0-3) — **D1 asserts against exactly these**:

| score | `ing_ember_crystal` | `ing_aether_shard` | `ing_heartstone_crystal` |
|---|---|---|---|
| 0 | 0.70 | 0.26 | 0.04 |
| 1 | 0.62 | 0.31 | 0.07 |
| 2 | 0.54 | 0.36 | 0.10 |
| 3 | 0.46 | 0.40 | 0.14 |

Lower-significance free world rolls, listed so the inventory is complete rather than because they are disclosable: `MineNode.cs:552`, `RareCrystalSpawner.cs:209`, `HarvestSite.cs:243`, `RandomEncounterTable.cs:149` / `:165` (seeded PRNG, ambush frequency).

#### ⛔ The explicit NEGATIVES — verified, not assumed

The three mechanics this ticket's brief named as the likely candidates **contain no RNG at all**, and that finding is worth as much as the positives:

- **Daily chest — ZERO `Random.` calls.** `DailyChestController.cs:20` `BaseGold = 500`; free path `Claim(BaseGold, "free")` (`:260`); rewarded-ad path `Claim(BaseGold * 2, "rewarded_double")` (`:321`). A fixed retention grant that happens to be called a chest.
- **Watch-ad chest / every rewarded ad — ZERO `Random.` calls** in `AdGateService.cs`, `RewardedAdManager.cs`, `HarvestBoostService.cs`. `ad-placements.json` rewards are flat grants and the file contains no `chance`/`odds`/`weight` key.
- **`packs.json` / PackStore — ZERO RNG, and three suites already police it**: `MonetizationCovenantRegression.cs:80-85`; `Assets/Data/Tests/PackCatalogTest.cs:157-161` (`no_pack_name_or_tagline_reads_as_a_loot_box_or_gacha`, banning `loot/gacha/random/mystery/lottery/spin/gamble`); `BattleMonthlyRegression.cs:102`.
- **Echo / pet acquisition — ZERO RNG.** `PetAcquisitionService.Acquire(species, source)` (`:190`) takes the species as an explicit parameter; sources are `Tame`/`Hatch`/`Rescue`. Echoes are chosen or granted, never pulled.
- **No pity timers, wheels or spins anywhere** in the repo.

#### ⚠ Two things that go in front of the owner BEFORE any questionnaire is answered

1. ⛔ **The Arena WAGER stakes REAL, PURCHASABLE Crystals — and it, not the loot tables, is the gambling-shaped mechanic.** `ArenaMode.cs:163-176` debits `opponent.Wager`; `ArenaWalletService.cs:15-18` states that on the **GooglePlay** channel the currency is *"the player's REAL Crystals, GameState.Resources.Crystals"* — and crystals are purchasable through `packs.json`. Tiers 50/100/200 (`ArenaWagerTunables.cs:54-56`), purse 200% (`RemoteTunables.cs:581`).
   **DORMANT:** `FeatureFlags.cs:33` `Arena => Get("arena", defaultOn: false)`, and the only world door checks it (`ArenaHeraldSpawner.cs:91`). The outcome is a played fight, not a draw, so it is **not** a probabilistic item — but if a questionnaire asks about **gambling or wagering** rather than loot boxes, **this is the answer**, and re-enabling that flag is a compliance decision, not a feature decision.
   ⚠ **NAMING TRAP:** `ArenaMode` / `ArenaHeraldSpawner` (wagered, dormant) is a **different system** from `BattleArena` (free, LIVE, rows 4-5 above). They share the word and nothing else.
2. ⚠ **Native-SKR staking buys ATTEMPTS at the polish roll.** `PolishBonusProvider.cs:69-77` grants a staker +1 weekly re-roll and, at 10,000 SKR, +1 to the roll cap; consumed at `JewelPolishService.cs:252-253` and `:266-272`. **It is attempts-only by deliberate design — the interface exposes no odds member** (header `:6-24`), the same doctrine as §2b. **Gated off for Play:** `FeatureFlags.cs:1017` defaults false unless the `DAPP_STORE` define is present (`:1015`), pinned by `StakingComplianceRegression`. **D6's Korea analysis must cover it**, because "money adjacent to a random outcome" is the shape a regulator reads first.

#### ⛔ A STALE DOC THAT WOULD POISON A DISCLOSURE ANSWER

`jewel-polish.json` `_tuning.dropModel` still reads *"ONE rough stone per COMPLETED run, guaranteed."* **False at HEAD.** WO-1373 (2026-09-09) made it **5% after the first stone, tier-2+ dungeons only** (`DungeonController.cs:612`, `:786`; the first-ever stone stays guaranteed via `firstDungeonStone`, `:611`). And the **raid** stone is not a roll at all — `RaidScoring.ShouldDropRoughStone` (`:1007`) contains no `Random.` call; it is tier >= 3 AND a 1/day cap (`RemoteTunables.cs:713`, `:716`), pinned by `RaidRoughStoneDropRegression`. Anyone answering a store questionnaire from that JSON note would state a guaranteed drop that does not exist. **Fixing it is deliverable D7.**

---

## 3. Deliverables

### D1 — ⛔ Build the regression that has been claimed for weeks: `JewelPolishOddsRegression`

New `Assets/Editor/Regression/JewelPolishOddsRegression.cs`, registered in `DataRegression.RunAll` beside the covenant gate. It must assert, as **arithmetic against the authored table** (the house style of `InventoryArmoryRailRegression` / `TouchFloorAuthoringRegression`):

1. **The disclosed odds ARE the rolled odds.** Call `JewelPolishService.DescribeOdds(score, isRePolish)` for **every** polish score row in `jewel-polish.json` and both `isRePolish` values, and compare against the weights read independently out of the JSON. Any drift fails.
2. **The disclosed set sums to 1.0** (within float tolerance) on every row — including the shatter line on a re-polish. A set that sums to 0.85 is a disclosure that lies by omission.
3. **The shatter model is the independent-first one.** Assert gem odds equal `weight/total × (1 − shatterChance)`, so a future "simplification" that overstates the gem chances fails here rather than in a regulator's inbox.
4. **There is exactly ONE odds source.** Fail if any file other than `JewelPolishService` computes a player-facing probability for this mechanic — the property `:425` already claims is enforced.
5. **The two canonical copies stay byte-identical** (`Resources` vs `StreamingAssets`). They are identical today; a drift means the shipped odds differ from the disclosed odds by build target.
6. **First polish can never shatter** — `jewel-polish.json:8` states it as an invariant ("a first polish of a rough stone can never shatter, because that stone is the run's guaranteed payout").

Then **fix the lying comment** at `JewelPolishService.cs:425` to name the file that now exists.

### D2 — Extend the covenant gate off its hardcoded file list

`MonetizationCovenantRegression.MonetizationFiles` (`:97-104`) must be **derived**, not enumerated: sweep every monetization JSON present under the canonical data folders, and FAIL on a file that looks like a pack/store/staking file but is not swept. The gate's own header already argues for derivation ("The allowlist is DERIVED, not blind-hardcoded… If the JSON list grows, the gate grows with it — single source of truth") — the *file list* is the half that did not get that treatment.

### D3 — An in-app odds surface: **scoped by §2e, and deliberately NOT applied to all six**

The sweep settles this, so it is a decision to *record*, not to rediscover:

- **Jewel Polish — already has one** (§2a). Nothing to build; D1 pins its accuracy.
- **Loot tables, arena gear, the rare-boss roll, kill variance, the dungeon 5%** — these are **combat/quest drops**, not items received "from a purchase" or at a moment the player commits a resource to a draw. There is no purchase to sit "in close and timely proximity to" (§1a). **Do not bolt an odds panel onto a kill.** ⛔ Building one anyway would misstate the game as having a loot-box surface it does not have — worse than the omission.
- ⚠ **The one that genuinely needs a ruling is the dungeon 5% rough stone**, because it is the *input* to the one real gacha and because the owner's own ruling gave it a number. If D6's Korea reading says free probabilistic items are in scope, the surface goes on the **dungeon results screen**, reusing §2a's shape verbatim.

Wherever a surface *is* built: derive from the roll table, render before the player commits, refuse rather than proceed if the table is unreadable. **Never author a second odds table.**

### D4 — A repo-tracked source of truth for the Play listing disclosure text

There is none today. Create `publishing/PLAY_LISTING_DISCLOSURES.md` holding the exact text the owner pastes into Play Console, with per-claim provenance in the style of `publishing/config.yaml`'s DECLARATIONS block. It must cover:
- Whether the game contains probabilistic items **as Play defines them** (with the §1a quote and the §2b firewall as the reasoning).
- The IARC / content-rating answers that bear on random rewards, so the questionnaire answer and the code agree.
- ⛔ It must **not** be pasted into `publishing/config.yaml` — that is the dApp Store listing and a different regulator surface.

### D5 — Add a disclosure gate to the submission checklist

`publishing/SUBMIT_CHECKLIST.md` — extend **"Legal and disclosure preparation"** (`:47`) and **"Gate G — Google Play closed testing"** (`:295`) with the probabilistic-item items, and add both policy URLs to **"Official sources"** (`:351`). Follow that file's existing convention: `OWNER:` prefix on anything only she can answer.

### D6 — The two written analyses (prose, not code)

- **Korea:** does the Enforcement Decree reach a *free, time-only* roll? Read the Decree + the GRAC guideline at source and record the answer with the citation. If it does, D3 grows a store-listing/in-game odds obligation for the polish table.
- **Brazil:** is a free, unpurchasable random reward a "loot box" under the Digital ECA, and does 13+ put us inside "aimed at children and adolescents"? Record the analysis. **`OWNER:` ruling required** — the outcomes are (i) out of scope on the definition, (ii) raise the age rating, or (iii) region-gate Brazil. All three are her call, not the lane's.
- **Both analyses must address §2e's two adjacencies by name** — the Arena wager's real purchasable Crystals and SKR staking's purchased *attempts* — and state, for each, whether the dormant/compiled-off status is a compliance answer or merely a deferral. "It is behind a flag" is not the same sentence as "it is not in the shipped artifact", and only the second one is a defence.

### D7 — Fix the stale drop-model note

`jewel-polish.json` `_tuning.dropModel` (both canonical copies, byte-identically) must state the real model: first stone guaranteed, thereafter **5%**, **tier-2+ dungeons only**, plus the raid path's deterministic tier-3 / 1-per-day gate. Cite `DungeonController.cs:612`/`:786` and `RaidScoring.cs:1007` in the note so the next reader lands on the code, not on another copy. ⛔ **Do not change the drop RATE** — this is a doc fix; the rate is an owner ruling from 2026-09-09.

---

## 4. Acceptance

1. **§2e re-verified at the HEAD you implement against, not inherited from this file.** The inventory was swept 2026-09-10 and every row carries a line number — but a line number copied from a doc is hearsay (§11B), and several of these files move weekly. Re-open each RNG line before you build against it; if one has moved, correct §2e in the same commit. Adding a **new** RNG site to the game without adding a row here is the failure this section exists to prevent.
1b. **D7 landed** — both copies of `jewel-polish.json` state the real drop model and remain byte-identical (`cmp`).
2. `REGRESSION_OK <n>/<n> suites` on a **fresh** log with `JewelPolishOddsRegression` registered — judged by the **marker**, not the exit code (§11B, and this repo's runners exit 0 on FAIL).
3. **D1 proven RED before green.** Quote the RED run in the RESULT. Suggested one-line RED: change one weight in `Assets/Resources/Data/Canonical/jewel-polish.json` and watch the sum/parity case fail; restore and watch it pass.
4. `COMPILE_GATE_OK` on the combined tree, `gate_brace.py` clean + zero NUL bytes on every touched `.cs`.
5. If D3 produced a surface: `UI_CAPTURE_OK` with the frame **opened**, showing the odds legible at 2670x1200 — screenshots are primary evidence for a visual claim.
6. Both policy URLs recorded in `publishing/SUBMIT_CHECKLIST.md` "Official sources", and every item in D6 either answered **with a citation** or explicitly marked **"policy text to be re-read at the source URL"**. An unproven thing named as unproven is acceptable; an unproven thing stated as fact is not.
7. `**Status:**` flipped in this file in the same commit as the work; `.RESULT.md` written; both paths reported.

---

## 5. What NOT to touch

- ⛔ **`JobRushPolicy.IsRandomOutcome` / `AllowsPaidInstantFinish`.** This is the firewall, it is an owner ruling, and the file says removing it "is a legal decision, not an engineering one." Do not add a kind to it, do not remove one, do not route around `BuildTimerService.cs:1139`.
- ⛔ **The ad-skip hole (§2b).** It is deliberate and owner-ruled. **Surface it to the owner; do not close it in this ticket.** Closing it silently changes a monetization behaviour she ruled on.
- ⛔ **The odds VALUES in `jewel-polish.json`.** This WO is about *disclosing and pinning* the numbers, never *retuning* them. `rePolishShatterChance: 0.15` in particular is load-bearing anti-pay-to-win, and the file says so at length at `:8`.
- ⛔ **`publishing/config.yaml`'s `catalog` block.** dApp Store listing, owner-supplied verbatim text, "Do not edit without owner sign-off" (`:136`). Play text goes in the new D4 file.
- ⛔ **The IARC / age-rating declaration.** `OWNER:` only — it is a Play Console answer with legal weight, and Brazil's exposure turns on it.
- ⛔ **`MonetizationCovenantRegression`'s banned-kind and combat-stat lists.** D2 changes only how the *file list* is discovered. Widening the semantic lists is a different ticket.
- Do not add a country exclusion to any listing. The owner ruled 12:20 that the listing is open in all countries; changing that is her decision, and D6's Brazil analysis is the input to it.
- No scene files. No economy rebalance. No gameplay tuning.

---

## 6. Evidence index (all opened 2026-09-10, in the worktree at `dev` `95eb7ea73`)

- `Assets/_Modules/Village/Crafting/JewelPolishService.cs` — `:322` time-only cost, `:425` the false regression claim, `:429` `DescribeOdds`
- `Assets/_Modules/Village/Crafting/JewelPolishConfirmPanel.cs:83-153` — the shipped disclosure
- `Assets/Resources/Data/Canonical/jewel-polish.json` — `:8` shatter doctrine, `:28` `0.15`, `:35-37` weights (`0.70` / `0.26` / `0.04`, summing to 1.00); byte-identical to the StreamingAssets twin per `cmp`
- `Assets/_Modules/Core/Jobs/JobRushPolicy.cs:65-135` — the firewall and the ad-skip carve-out
- `Assets/_Modules/Village/Buildings/BuildTimerService.cs:1139` — the money seam
- `Assets/Editor/Regression/DungeonGemExclusivityRegression.cs:162,177` — the firewall's pin
- `Assets/Editor/Regression/MonetizationCovenantRegression.cs:14,80-83,97-104,198-202`; registered `DataRegression.cs:332`
- `publishing/config.yaml:204` (min age 13), `:120-170` (dApp Store catalog), `:174-177` (Play package deliberately omitted)
- `publishing/SUBMIT_CHECKLIST.md:47`, `:295`, `:351`
- `docs/monetization-v2-spec.md:39`; `docs/design/LIVEOPS_RETENTION.md:42`; `docs/SECURITY_COMPLIANCE_HARDENING_AUDIT.md:153,227`
- Policy, fetched this session: `support.google.com/googleplay/android-developer/answer/9858738` §Payments; `.../answer/6223646` §Korea, §Brazil
- **`grep -rn "JewelPolishRegression" .` → one hit, its own claim.** The proof for §0 item 1.

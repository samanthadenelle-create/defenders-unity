# WO-1657 RESULT — both items: the Gold upgrade cost, and "Level 0 of 4" on a placed, READY Quarry

**Status:** IMPLEMENTED - awaiting gate (both items).
**Lane:** MANAGE-VM (item B) + UPGRADE-COST (item A).
**Date:** 2026-09-10
**No Unity run, no commit** — per the lane briefs.

---

# ITEM A — the Gold term (UPGRADE-COST lane, owner ruling 2026-09-10 12:12)

**Ruling:** WO-947 (regular structures cost wood + iron) governs the UPGRADE ladder too.
`CostGold -> 0` on the building-tiers ladder; `BuildingUpgradeRegression:593` re-pointed, not deleted.

**Worktree:** `.claude/worktrees/agent-a0c15f9d18e6ed54b`, branch `dev` @ **`e4b5906a541c65fc12255a109b5dc3723e328488`**
(fast-forwarded from `f5d39acd1`; the ruling section was copied in from `D:\EoA\WorkOrders`).

## A1. Data — both canonical twins, binary patch, newline proof

`building-tiers.json` v7 -> **v8**. All **26** civic ladder tier rows now author `"costGold": 0`:

| ladder | tiers | costGold before |
|---|---|---|
| `arcane-tower` | 1-4 | 800 / 1440 / 2880 / 5600 |
| `armorer` | 1-4 | 670 / 1400 / 2850 / 5720 |
| `barracks` | 1-6 | 1240 / 2740 / 5560 / 9860 / 15100 / 23030 |
| `forge` | 1-4 | 680 / 1440 / 2970 / 5800 |
| `lumbermill` | 1-4 | 460 / 970 / 1990 / 4250 |
| `farm` | 1-4 | 1150 / 2380 / 4910 / 7470 |

Patched with `re.subn(rb'"costGold": \d+', b'"costGold": 0', ...)` over the bytes on disk, asserting
`n == 26` per twin, then re-parsed as JSON before writing.

```
Assets/Resources/Data/Canonical/building-tiers.json
  before {bytes 21877, LF 86, CRLF 86, CR 86, endsNL True, "costGold" 26, "goldCost" 17, sha 834aed4128e08eed}
  after  {bytes 22952, LF 86, CRLF 86, CR 86, endsNL True, "costGold" 26, "goldCost" 17, nonzero 0, sha 64c9de752a111369}
Assets/StreamingAssets/Data/Canonical/building-tiers.json
  before {bytes 21877, LF 86, CRLF 86, CR 86, endsNL True, "costGold" 26, "goldCost" 17, sha 834aed4128e08eed}
  after  {bytes 22952, LF 86, CRLF 86, CR 86, endsNL True, "costGold" 26, "goldCost" 17, nonzero 0, sha 64c9de752a111369}
```

**Newline proof:** LF == CRLF == CR == **86** before and after on both twins (pure CRLF, no lone LF
introduced), trailing newline kept, and the two twins are **byte-identical** (same sha both sides,
before and after). Byte growth is the `_comment` v8 clause only.

**`"costFood"` = 0 occurrences in both twins** — load-bearing, because
`BuildingTierCatalog.cs:64` refills `CostGold` from a legacy `costFood` key *when CostGold is 0*.
With gold now 0 on every row that alias is live for every row, so the count must be zero for the
gold to really be gone. It is.

**Perk `goldCost` (17 rows) is untouched** — research is a different key and a different ruling.

**The `"version": 7 -> 8` bump is CONVENTION, not behaviour.** `BuildingTierCatalog.cs:104` declares
`Version` and **nothing in the tree reads it** (`grep -rn "\.Version" --include=*.cs Assets/ | grep -i tier`
returns only `TowerPerkRegression:90`, a different file's log line). The file's own `_comment` is a
per-version changelog (v4-v7 in place), so a v8 clause follows its convention; it rode the same
binary patch, which is why the newline proof covers it.

## A2. Oracle — RED first, then GREEN

`Assets/Editor/Regression/BuildingUpgradeRegression.cs` case 11 `[shortfall-named][gold-line]` was a
SOURCE LINT (`vmSrc.Contains("AddCoinCostLine(")`) asserting the VM still emitted a Gold line. It is
**re-pointed, not deleted**: `CheckTierGoldIsZero` now walks `buildings[*].tiers[*].costGold` in
**both** twins (reusing the existing `TierPaths` array) and fails, one named failure per row, on any
non-zero. The case-11 header was rewritten in the same edit — it previously stated the opposite rule
and listed "delete the AddCoinCostLine call" as its RED mutation.

RED-first was proved with a Python mirror of the exact assertion:

```
$ git show HEAD:Assets/Resources/Data/Canonical/building-tiers.json > head-bt.json
$ python mirror.py head-bt.json        # the PRE-ruling data
  RED: 'arcane-tower' tier 1 costGold 800      ... (26 rows, listed above)
head-bt.json -> gold-line failures: 26

$ python mirror.py <working tree, both twins>
  -> gold-line failures: 0     (Resources)
  -> gold-line failures: 0     (StreamingAssets)
```

`AddCoinCostLine` **stays in `BuildingUpgradeVM`** (`:1545-1547` returns on `amount <= 0`) as the
self-guard if a gold term ever returns.

## A3. The composer — four surfaces were already safe, one was not

- `CostFormat.Parts` (`Assets/_Modules/Core/UI/CostFormat.cs:32-34`) drops `amount <= 0`. That covers
  `ManageScreenVM.BuildingUpgradeCostParts` (**`:2200-2209`** — the brief said `:2185-2194`; the
  composer has moved, reported as line drift), `ManageScreenVM.DescribeCost` (`:3281-3285`) and
  `BuildingUpgradeVM.CostString`/`CostParts` (`:1802-1808`).
- ⛔ **`ManageScreenVM.AddGoldBrowseRow` (`:3287-3298`) WOULD have rendered a zero chip** — it
  concatenated `", " + gold + " gold"` unconditionally, so the town browse row would read
  `"Wood 2600  Stone 970, 0 gold"`. **Fixed**: the gold clause is emitted only at `gold > 0`;
  `"free"` stays the empty-basket word. Its one live caller is `:1555`.
- `BuildInventoryModel.cs:321` (`BuildTierChargeRow.Gold`) needs nothing: `TierCharges` has exactly
  two references in the tree — the declaration (`:131`) and the builder (`:316`). Nothing renders it.

## A4. Why the CHARGE is unchanged

`BuildingTierChargeLane` derives the spent resource from the **tier number** over
`BuildingTierDef.PrimaryMaterialCost = Max(costWood, costCrystal)` (owner ruling 22, WO-2005), and
`BuildingUpgradeService.TryUpgrade` (`:115-120`) debits `Coins` separately. Zeroing `costGold`
removes the Coins term and touches nothing else. `CanAffordTier` (`:219`) becomes
`Coins >= 0 && ResourceLedger.CanAfford(...)` — materials only.

`CostBasketSeparationRegression [tiers-basket]` still passes: it reads `costWood` / `costCrystal`
only, and `arcane-tower` keeps `costWood 0 / costCrystal N`, so its magical check is satisfied.

## A5. The sweep for OTHER suites asserting a gold cost — 1 found, 0 left to re-point

Three greps across `Assets/Editor`, `Assets/Tests`, `Assets/Data` (the brief's "any other suite/test
that asserts a gold cost"):

| grep | hits that bear on a BUILDING-TIER upgrade |
|---|---|
| `CostGold\|costGold` | `BuildingUpgradeRegression:593` (**re-pointed**, A2). Everything else is `TroopDef.CostGold` (troops.json, a different file and the WO-1387 ruling), `BuildTimerMercenaryRegression` (mercenary hire), or a comment. `Assets/Data/**` has **zero** hits. |
| `" gold\|gold "\|"Gold` in the suites | **none** assert a rendered gold word on a ladder upgrade row. `BuildingUpgradeRegression:580` is an inline `Line("Gold", 800, 806)` FIXTURE for `AffordableFromLines` — a hand-built array, not data-driven, so it is unaffected and is deliberately left as the proof that the predicate still handles a gold line if one ever returns. `ManageResearchCardRegression:290-316` and `ManageMockupConformanceRegression:706` are PERK/research rows (`goldCost`, untouched). |
| `CostText\|BrowseRows\|AddGoldBrowseRow\|Short on resources` | `ManageDefenseUpgradeDoorRegression:210-228` reads `ActionText`/`Label` only (and the Defense tab runs `PlacedStructureUpgradeService`, not this ladder). `ManageProgressiveDisclosureRegression:623` requires a non-empty `RowAction.CostText` on **perk** rows. `ManageTroopsTrainDoorRegression:477` is troops. **No suite asserts the town Upgrade row's cost STRING**, which is why the `AddGoldBrowseRow` zero-chip could have shipped unseen. |

So exactly ONE oracle needed re-pointing and it was re-pointed, not deleted.

## A6. Gates run in-lane

```
python tools/gate_brace.py Assets/Editor/Regression/BuildingUpgradeRegression.cs Assets/_Modules/Village/UI/Manage/ManageScreenVM.cs
GATE_BRACE_SUMMARY bad=0 of 2
NUL bytes: 0 in every .cs and .json touched
```

`BuildEconomyRegression.cs` was edited for a stale comment only (`:1779` said "costGold 970 -- both
LIVE"); a dated `[SUPERSEDED 2026-09-10]` clause was added beneath it, narrative kept verbatim.

**No Unity run, no commit** — the gate is what turns A2's RED/GREEN mirror into a fact.

---

# ITEM B — "Level 0 of 4" (MANAGE-VM lane)

**Lane:** MANAGE-VM (isolated worktree `.claude/worktrees/agent-a6afae9850478e00d`, branch `dev` @ `aba49bd4c`)

> ## ⛔ ITEM A WAS NOT TOUCHED BY THIS LANE (it was still unruled at the time).
> `BuildingTierDef.CostGold`, `building-tiers.json`, `structures-catalog.json` and
> `BuildingUpgradeRegression.cs:593-594` are **all unmodified** *by the MANAGE-VM lane*. The
> Gold-in-the-basket question is
> the owner's ruling (Reading 1 vs Reading 2, WO §2) and remains visible in the WO's Status line.
> *(Incidental confirmation of A's source, recorded because it was read in passing while proving B and
> costs nothing to state: `building-tiers.json` authors the farm ladder's `costGold` as
> 1150 / 2380 / 4910 / 7470 — the frame's `1150 Gold` is tier 1's. **No conclusion is drawn from
> that and nothing was changed.**)*

---

## 1. §12 FIRST — THE PRODUCER, NAMED WITH file:line, FROM A RUN

The WO's §3 listed two candidates and said neither was proven. **Both are wrong.** Neither
`BuildingUpgradePanelMvvm.cs:971` nor `ManageScreenVM.cs:1840` composes this head.

**The head is `ManageSelectionVM.LevelText`, composed at `ManageVmProjection.cs:319-322`.**

The chain, every link read at source this session:

| # | Site | What it does |
|---|---|---|
| 1 | `ModifierService.TierOf` — `Assets/_Modules/Core/State/ModifierService.cs:44-47` | `tiers.TryGetValue(buildingId, out int t)` … **`return 0;`** — zero is what a **DICTIONARY MISS** returns, not a stored level |
| 2 | `BuildBuildingChoices` — `ManageScreenVM.cs:1583` | `int level = ModifierService.TierOf(id);` → `BuildingChoiceVM.Level` |
| 3 | `ComposeBuildingItem` — `ManageScreenVM.cs:4527` | `Level = c.Level`, `MaxLevel = c.MaxLevel` |
| 4 | `ManageVmProjection.cs:319-322` | `LevelText = item.MaxLevel > 0 ? "Level " + item.Level + " of " + item.MaxLevel : …` → **`"Level 0 of 4"`** |

### The deciding captured line — `Builds/wave6-manageflow1` (fresh, 2026-09-10 09:00, NUL-padded)

```
[Flow:Manage] building choice id=farm level=0/4 state=Upgradable next=1 ready=True
              icon='Portraits/Buildings/farm' benefit='Stone production +10%.'
```

**How that line is tied to the frame** (`Builds/device-frames/2026-09-10_0915b_363722_build_detail_quarry_placed.png`):
`farm` is the Quarry's **ladder** id — the catalog row is `collector_farm`, displayName "Quarry"
(WO §5: never rename it, it is a live save key) — and the trace's `benefit='Stone production +10%.'`
is the frame's `Next level  Stone production +10%.` **verbatim**. Siblings on the same run read
`arcane-tower level=3/4`, `lumbermill level=1/4`, `forge level=4/4 state=Max`, so **the zero is this
building's state, not a broken read.**

---

## 2. THE B1/B2 VERDICT, RECORDED BEFORE THE FIX (WO acceptance 3)

## ⭐ **B2. Level 0 is REAL. It must NEVER be "corrected" to 1.**

Three independent proofs, all read at source 2026-09-10:

1. **The ladder is 1-based and there is no level 0 in the data.** `building-tiers.json` authors
   `farm` as tiers **1, 2, 3, 4** (every other ladder likewise starts at 1).
2. **Tier 1 is a PURCHASED rung, not the founding state.** Tier 1 authors
   `foodProductionMult: 1.1` — that *is* the "+10%" the card is offering to buy, and it is the
   `benefit` string on the trace above. Writing `1` into the head would claim a multiplier the
   player has not paid for.
3. **The code already models tier 0 as a first-class state, and says so in its own words.**
   `ModifierService.TierProductionMult` (`:104-110`): `if (tier < 1) return 1f;`, documented at
   `:90-92` as *"A tier below 1 contributes identity there … matching Compute rather than guarding a
   zero into a one."* That sentence predates this ticket.
4. ⭐ **AN EARLIER OWNER RULING ALREADY SETTLED THIS, IN WRITING, AND EXPLICITLY FORBIDS B1.**
   `ManageScreenVM.CountPlacedThisTown`'s doc block (`:1487-1505`) — written for the 2026-08-08
   felt-test *"no building upgrades are on the manage button anywhere"* — states:
   *"TierOf reads GameState.BuildingTiers, which **only ever contains ids that have been
   UPGRADED**"*, and then, verbatim:
   > ⛔ **DO NOT "fix" a future variant of this by writing tier=1 at placement.** Tier 1 is a PAID
   > upgrade … so seeding it would gift every newly placed building a free upgrade. **The ladder is
   > 1-based for UPGRADES, not for existence: tier 0 = placed.**

   That is WO-1657's B1 named and rejected in advance, by an owner-driven change, before this
   ticket existed. **It is the single strongest reason the fix had to be display-only.**

**So the founding state genuinely sits BELOW the ladder: placed, producing at base, no rung bought.
The defect is the WORDING, not the number — and the fix is display-only.** Nothing upstream moved: no
default changed, no tier seeded, no economy touched. That restraint matters more than usual here —
the game is live on the Solana dApp Store.

**This is not an invented rule.** Ruling 3.7 already forbids painting a level zero, pinned by
`ManageResearchCardRegression` `[no-level-zero]`: *"Never paint LEVEL 0."* This applies the same
ruling to the other surface that owns the level slot.

⛔ **No second ceiling was introduced** (CLAUDE.md §8). The "4" is `item.MaxLevel`, which the composer
already carries from `BuildingTierCatalog.MaxTier`.

---

## 3. WHAT SHIPPED

| File | Change |
|---|---|
| `Assets/_Modules/Core/Manage/ManageVmProjection.cs` | `LevelText` gains ONE nested branch: `item.Level > 0` keeps `"Level N of M"` **byte-identical**; `item.Level <= 0` composes `FoundingLevelText(item.MaxLevel)`. New `FoundingLevelWord` const + `FoundingLevelText` helper, with the full proof block above them. |
| `Assets/Editor/Regression/ManageBuildingsCardRegression.cs` | New case **`[founding-building-is-not-level-zero]`**, registered in `Run` beside `CheckLiveModel`. |
| `Assets/_Modules/Village/UI/Manage/ManageScreenVM.cs` | **The read-back trace the lane brief asked for.** The existing `building choice id=` line printed the level's VALUE, which is exactly why the frame could show "Level 0 of 4" and the ticket still had to guess between B1 and B2. It now carries a trailing **`tier-source=`** token naming the AXIS: `BuildingTiers` (a stored level) vs `founding(no-tiers-entry)` (TierOf's dictionary-miss default). Appended at the END so nothing before `benefit=` moves. Instrumentation only - no behaviour change. |

Composed today: **`Not yet upgraded . 4 levels`**.

> ### ⚠ THE WORDING IS AN OWNER CALL, AND IT IS ISOLATED SO OVERRULING IT IS ONE TOKEN.
> `ManageVmProjection.FoundingLevelWord`. Its doc comment says **"CHANGE THIS CONSTANT, NEVER THE
> BRANCH."** The structural rule (a dictionary-miss sentinel is not a level; ruling 3.7 forbids level
> zero) is settled and proven; the exact words are Samantha's to overrule. It states the ladder DEPTH
> deliberately — dropping the number entirely would be worse than the wrong number, because the player
> would no longer see there is a ladder at all. Kept short: it lands in the level band beside the state
> chip, which TMP culls rather than wraps.

**No pin was deleted and nothing was renamed.** `collector_farm` untouched; no `HarvestResource.Food`
site touched; `Production / hr`, storage caps and the harvest/overflow path untouched (WO §5).

---

## 4. THE PIN — `[founding-building-is-not-level-zero]`

`Assets/Editor/Regression/ManageBuildingsCardRegression.cs` — the suite that owns building cards.
**No `DataRegression.cs` registration line needed**; the suite is already wired.

Pinned on the **composed VM** (`ManageSelectionVM.LevelText`), per WO acceptance 5 — never a rendered
string, and **never the wording**, so the owner can change the words without going RED:

1. **FOUNDING.** A placed **`collector_farm`** whose `BuildingTiers` has **no `farm` key** — that dictionary miss
   *is* the device's state and is the whole point — composes a line that (a) does **not** open with
   the level-zero literal and (b) still **states the ceiling**.
2. **THE UPGRADED PATH IS UNTOUCHED.** The same fixture with `BuildingTiers["farm"] = 2` must still
   compose **exactly** `"Level 2 of 4"`, byte for byte, so the founding branch cannot bleed into the
   ordinary case.
3. **AN UNBUILT BUILDING MUST NOT CLAIM THE FOUNDING LINE.** `ProjectSelection` is shared by every
   tab, and "not yet upgraded" asserts the thing EXISTS — so on an unbuilt row it would be a NEW
   false claim replacing the old one. Today it cannot happen, and the guarantee is **structural**:
   `ComposeUnplacedItem`, `ComposeOwnedNoUpgradeItem` and `ComposeTroopItem` all set `MaxLevel = 0`
   and the branch is gated on `MaxLevel > 0`; `ComposeDefenseItem` cannot reach it either, because
   `BuildDefenseChoices` floors its level with `Mathf.Clamp(tally.LowestLevel, 1, ceiling)`
   (`:1801`). An invisible structural guarantee is exactly the kind deleted by accident, so it is
   **pinned rather than trusted**.

⛔ **TWO IDS, AND GETTING THEM BACKWARDS WOULD HAVE MADE THE PIN WORTHLESS.** `collector_farm` is the
CATALOG / `BaseLayout` id; **`farm` is only the LADDER id** `CatalogRegistry.ResolveUpgradeId` maps it
onto, and there is **no `farm` row in `structures-catalog.json`** (verified 2026-09-10 — the row
authors `repo.collectorBuildingId: "farm"`). A fixture placing `"farm"` would have placed a **ghost**:
`CountPlacedThisTown` would never tally it, `BuildingChoices` would be empty, and the case would FAIL
after the fix exactly as loudly as before it — proving nothing, twice. The case now places
`collector_farm`, reads the detail by the ladder id, and **verifies the mapping itself first**
(`ResolveUpgradeId(collector_farm) == farm`) as a FAIL-not-skip, so the day that mapping moves the
suite says so instead of asserting on an empty model.

Every unreachable seam is a **FAIL, not a skip** (ladder shorter than 2 tiers / state seam not
reflectable / no visible selection). `GameStateService.Instance` and the tab pref are restored in the
`finally`. The forbidden literal is **built from parts** in the failure message so this case cannot
itself trip `[no-level-zero]`'s source scan.

### ⚠ RED, stated honestly
**The case has NOT been run — no Unity, per the brief. It is RED BY CONSTRUCTION**, with the exact
today-line named: restore
`LevelText = item.MaxLevel > 0 ? "Level " + item.Level + " of " + item.MaxLevel : …` at
`ManageVmProjection.cs:319-322` and part 1 fires. The recipe is written into the case's own header.
**The gate is what turns this into a fact.**

---

## 5. ACCEPTANCE, ANSWERED HONESTLY

| # | WO says | Status |
|---|---|---|
| 1 | Item A ruled by the owner | ⛔ **NOT DONE, deliberately — out of this lane's scope.** Nothing touched; the question stays in the WO Status line. |
| 2 | Reading 1 / Reading 2 implementation | **N/A — blocked on 1.** |
| 3 | Item B's producer named with file:line, **from a run** | ✅ `ManageVmProjection.cs:319-322`, chain in §1, decided by a line on `Builds/wave6-manageflow1`. **Both of the WO's candidates were wrong.** B1/B2 verdict recorded before the fix (§2). |
| 4 | A frame proves the result | **NOT DONE — no Unity.** Needs a fresh device/headless Quarry detail frame, opened. |
| 5 | RED first, on the composed VM | ✅ composed-VM pin; **unrun, RED by construction** (§4). |
| 6 | `COMPILE_GATE_OK` + `REGRESSION_OK <n>/<n>` on fresh logs | **NOT DONE — the lead gates.** `tools/gate_brace.py` clean on both files; NUL scan clean. |
| 7 | Status → `AWAITING OWNER MATCH` | Set to **`ITEM B IMPLEMENTED - awaiting gate`** + item A's pending ruling, on the lead's explicit instruction. |

---

## 6. FOLLOW-UPS NAMED, NOT TOUCHED

- **The RAIL still reads the sentinel raw — MINTED AS WO-1659**
  (`WorkOrders/WORK_ORDER_1659_locked_founding_building_rail_reads_level_0_heart_n.md`).
  ⚠ **THIS BULLET AS FIRST WRITTEN WAS IMPRECISE, AND THE CORRECTION IS THE USEFUL PART.** It said
  *"`LockText` (`ManageScreenVM.cs:1625`) … so a **locked** founding building still reads
  `Level 0 . Heart N`"*. Verified at source while minting the follow-up: **that string is NOT
  REACHABLE TODAY.** `isLocked` is `next.RequiresVillageTier > villageTier`, `next` is TIER 1 for a
  level-0 building, and **every ladder in `building-tiers.json` authors `requiresVillageTier: 0` on
  its tier-1 row** (all six read 2026-09-10) — so the gate is `0 > villageTier`, false at any Heart
  level. **A founding building can never be Locked**, and `LockText` can never today compose that
  string. It is **LATENT**, reachable only if someone authors a non-zero tier-1 gate.
  **The LIVE sibling is the one I did not name:** the rail's *unlocked* branch,
  `ManageScreenPanel.cs:5024-5027` (`"Level " + choice.Level + …`). A founding building is not
  locked, so it takes that arm and the rail reads the sentinel — and `wave6-manageflow1`'s
  `farm level=0/4 state=Upgradable` is exactly that pair, so it **was** reached on a captured run.
  Since item B fixed only the detail card, **the two halves of one screen now disagree about the
  same building** until WO-1659 lands. ⛔ The DEFENSE rail (`:6027`), which WO-1657 §3 named
  alongside `:5024`, **cannot** be affected — `BuildDefenseChoices` floors its level with
  `Mathf.Clamp(tally.LowestLevel, 1, ceiling)` (`:1801`).
  ⚠ **Neither rail line is on any frame** — there is no `ManageFlow_BUILD_locked_*` capture at all
  (only ARMY and RESEARCH). Recorded in WO-1659 §3 as **not-observed**.
- **WO §6's open question — do Lumber Mill and Forge share the symptom?** Answered for the level half
  from `wave6-manageflow1`: **no.** They read `lumbermill level=1/4` and `forge level=4/4` on that run,
  so on this save only the Quarry sits at the founding tier. ⚠ That is a statement about **this save**,
  not about the code: **any** city-ladder building with no `BuildingTiers` entry would have read
  `Level 0`, and the fix covers all of them. **Only city-ladder buildings can reach the founding
  branch at all** — proved above (part 3): every other composer sets `MaxLevel = 0`, and the defense
  composer floors its level at 1.
- ⚠ **UNPROVEN AND OUT OF SCOPE — the production delta does not match the authored multiplier.**
  The frame draws `Production / hr  1,872 -> 2,016`. That ratio is **2016 / 1872 = ~1.077**.
  `building-tiers.json` authors the farm's tier-1 `foodProductionMult` as **1.1** (read at source
  2026-09-10), and the card's own "+10%" benefit line says the same — so a naive reading predicts
  **2,059**, not 2,016.
  `ModifierService.ProductionMultForTier` composes `live / current * TierProductionMult(tier)` where
  `live` carries perk and Echo terms, so there may be an innocent explanation in that composition.
  ⛔ **I DID NOT PROVE ONE, AND I AM NOT CLAIMING THERE IS ONE.** WO-1657 §5 puts `Production / hr`
  explicitly out of scope and item B is display-only, so it was not chased — the numbers are recorded
  here so the next seat starts from a measurement instead of rediscovering it.
  **This is an OPEN QUESTION: not asserted as a defect, and not cleared as a non-defect.**
  The cheap way to close it: evaluate `ProductionMultFor("farm")` and `ProductionMultForTier("farm", 1)`
  on the owner's save and compare the ratio against 1.1.

# WO-2004 — Define Data-Driven Heart Unlock Bundles and Upgrade Requirements

**Status:** CLOSED 2026-09-08 - owner felt-test PASS (validated 2026-09-09T02:10:47, build 2026.09.08.361259). PRIOR STATUS: FIXED (requirements half) - the data-driven requirement reader and the sole-writer refusal landed in the 2026-09-07 afternoon gate (REGRESSION_OK 454/454); requirements author EMPTY on every row pending the owner's balance ruling; owner felt-test closes. PRIOR STATUS: READY TO IMPLEMENT - state unproven 2026-09-06

**Priority:** P0  
**Depends on:** WO-2003

## Objective

Make Heart upgrades actually unlock the broader game instead of functioning as a cosmetic level number.

## Data model

For each Heart level transition, author one progression record containing:

- target Heart level
- costs
- duration
- prerequisite conditions
- newly unlocked building IDs
- newly unlocked building upgrade caps
- newly unlocked troop IDs
- newly unlocked troop upgrade caps
- newly unlocked research schools/perks
- newly unlocked defenses
- newly unlocked systems
- reach/radius value if applicable
- optional reward/message metadata

## Do not hard-code unlocks in UI

The model computes the unlock result from progression data.

UI may show:
- "Unlocks at Heart Level 4"
- a preview list supplied by model

UI may not know which Heart level unlocks Outrider, Stone Gate, etc.

## Unlock preview

Heart selection/detail model should support a short preview:

`NEXT LEVEL UNLOCKS`
- Stone Gate
- Outrider
- Armorer Level 3
- +X build reach

Preview contents come from the progression model.

## Gate normalization

Every system currently using a village/town tier gate must be audited.

For each gate:
- map to Heart Level if it represents realm progression
- leave unchanged only if it is truly a different mechanic
- document exceptions

## Acceptance criteria

- no duplicated Heart-level unlock tables across services
- one authoritative progression table
- every player-facing tier lock resolves to a real Heart upgrade path
- unlocking a Heart level invalidates/rebuilds dependent Manage state
- no restart required to see newly unlocked tiles/actions


---

## Provenance and reconciliation (added by the CLI seat, 2026-09-06)

Authored outside this repo and delivered by the owner as `Elarion_Manage_Redesign_Detailed_WorkOrders.zip`
on 2026-09-06. Filed verbatim; **the body above is the author's and has not been edited.** Everything the CLI
seat adds appears under a heading like this one.

**Numbering:** this set uses a **2000-block**, a THIRD namespace alongside the CLI main line and the UI seat's
reserved block. It is declared on `CLI_LANES_WO_NUMBERS.md` so it cannot collide (CLAUDE.md section 2 - the banner is the
sole authority). Do not renumber these into the main line.

**Supersedes:** `WORK_ORDER_1427` (why-can't-I) and `WORK_ORDER_1428` (the Manage card grows to the mockup).
Both were minted earlier the same day from the owner's playtest and her mockup; this program subsumes them and
goes further by replacing the rail model rather than enriching the card.

**Measured facts this set is consistent with** (from `docs/manage-flow-map/MAP.md`, run `Builds/flowmap1`):
43 rail rows across four areas, about two visible at a time; Buildings 6 + Defense 11 = 17, which is the number
the canon cites; the scroll auditor reporting `geometry=5 touch=5` on deliberately scrolled frames, which WO-2016
is right to call a fix rather than a waiver.

---

## Implementation notes and gate audit (Opus lane, 2026-09-06) — edit-only, ungated

**Status: EDIT-ONLY, NOT GATED.** This lane holds no Unity lock and ran no compile gate, no regression
and no commit. Everything below is measured at source this session or is explicitly named as unproven.

### Files changed

| File | Change |
|---|---|
| `Assets/Resources/Data/Canonical/heart-progression.json` | **NEW** — the one authoritative Heart ladder |
| `Assets/StreamingAssets/Data/Canonical/heart-progression.json` | **NEW** — byte-identical mirror |
| `Assets/_Modules/Core/State/HeartProgressionCatalog.cs` | **NEW** — its only reader |
| `Assets/_Modules/Village/Buildings/Progression/VillageTierService.cs` | `MaxTier` const → catalog-backed property; `NextCost()` reads the catalog |
| `Assets/_Modules/Village/Buildings/Progression/HeartProgression.cs` | `UnlocksAt` derives troops / population cap / echo slots transitively |
| `Assets/_Modules/Village/Population/PopulationService.cs` | `CapAtVillageTier(int)` — read-only projection of the existing private ladder |
| `Assets/Editor/Regression/HeartSurfaceRegression.cs` | new case `[ladder-*]` — an empty or re-hardcoded ladder cannot ship |

`.meta` files for the two new `.json` and the new `.cs` are left for Unity to generate on import.

### What moved out of code, and what deliberately did not

The **ladder** moved. `VillageTierService` carried `public const int MaxTier = 3;` and
`return 250 * next;` — the ceiling and the cost curve for the gate that opens nearly all content,
where the owner cannot tune them. Both now live in `heart-progression.json` with **identical values**
(3; 250 / 500 / 750). This is a de-hardcoding, **not a re-balance**.

⛔ **There is deliberately NO hand-written fallback ladder.** A missing or malformed catalog fails
loudly and loudly *empty* (`FlowTrace.Fail` + `Debug.LogError`, then `MaxLevel == 0`), following
`BuildingTierCatalog`'s precedent and owner ruling 2026-08-24 / WO-1170 (*"We need to not have
anything pulled other than from json"*, pinned by `JsonMirrorLiteralRegression`). A silent fallback to
3 and 250 would be indistinguishable from a working catalog.

**The unlock lists did NOT move into data, and that is the point.** WO-2004's own acceptance line —
*"no duplicated Heart-level unlock tables; one authoritative progression table"* — is satisfied by
**derivation**, not by authoring a second table. The chain, all measured at source:

```
heart-progression.json      Heart Level ceiling + crystal cost      (NEW, this lane)
building-tiers.json         requiresVillageTier on a ladder rung ─┐  -> that rung opens
                            perks ride their own row's field  ────┤  -> that perk opens
troops.json                 unlockBarracksTier == that rung  ─────┤  -> that troop becomes reachable
PopulationService.CapByTier cap[level]                        ────┤  -> the cap rises
population-milestones.json  all.villageLevel == level         ────┘  -> an echo slot's Heart condition
```

Authoring a troop list into `heart-progression.json` would have created exactly the duplicated table
the WO forbids. `UnlocksAt` composes the hops instead, so **"Outrider" now appears in the preview with
no second table** — worded as a path (`Outrider (via Barracks Level 4)`), because reaching the Heart
Level makes the Barracks rung *upgradable*; it does not hand the player the troop.

### ⛔ Where the delivered spec disagrees with the tree — read before acting on the spec

1. **`RESULT` for WO-2003 names paths that do not exist.** It lists
   `Assets/_Modules/Core/Manage/HeartProgression.cs`, `.../HeartPanel.cs` and
   `.../HeartPanelBootstrap.cs`. Measured: `Assets/_Modules/Core/Manage/` contains none of the three.
   The real files landed in commit `a6bbc523d` at
   `Assets/_Modules/Village/Buildings/Progression/HeartProgression.cs` and
   `Assets/_Modules/Village/UI/Manage/HeartPanel.cs` / `HeartPanelBootstrap.cs`.
   RESULT files are frozen (CLAUDE.md §15) so this lane did not edit it — flagged for the lead.

2. **The spec's example preview is not this game's content.** It shows
   `NEXT LEVEL UNLOCKS: Stone Gate / Outrider / Armorer Level 3 / +X build reach` under
   *"Unlocks at Heart Level 4"*. Measured:
   - **Heart Level 4 does not exist** — the ceiling is 3, and `ProgressionReachabilityRegression`
     exists precisely because `lumber-ancient-sawmill` once demanded tier 4 and was unreachable forever.
   - **Stone Gate is not Heart-gated at all.** It is earned from `wall_wood` reaching L2
     (`RewardedProgression.TryUnlockStoneGate`) and is then *hidden by an art gate*
     (`BuildInventoryModel.cs:313-316`, `HiddenUntilFinishedArtId = "gate_stone"`) — the unlock is
     earned and has no effect. A different mechanic; documented exception, left unchanged.
   - **`+X build reach` describes a system that does not exist.** Grepped
     `Assets/_Modules/Village/BuildMode` for build-reach / influence-radius / buildable-radius:
     **zero hits.** Canon §6 permits a reach unlock and owner ruling 12 requires the value be
     data-driven, but there is no radius to make data-driven yet. **This is a new feature for the
     owner, not a missing number**, and authoring one would be a promise nothing can keep.
   - **Outrider and Armorer Level 3 are real** and both now appear in the preview.

3. **The spec's 12-field progression record cannot be honoured as written.** Only *target level* and
   *costs* exist. Deliberately absent, each recorded in the file's own `_authoringNotes`:
   **duration** (no Heart job kind, no queue channel, no timer — `TryUpgrade` is instant: spend →
   tier+1 → Save → Recompute; WO-2003 recorded this and it is still unruled), **prerequisite
   conditions** beyond the crystal cost (none exist in the spend path), **defense unlocks**
   (`structures-catalog.json` authors no tier gate — grepped for requires/unlock/minTier/gateLevel:
   zero hits), **reach**, and **reward/message metadata** (no reward grant is wired to a Heart level).
   Authoring inert fields would also trip `AuthoredFieldReaderRegression`'s Case C ratchet, which
   fails a new authored string field with no production reader.

### Owner rulings reconciled

- **Ruling 23 (one of each storage type; capacity grows by LEVEL, not COUNT).** **No conflict with
  this WO, and nothing here touches it.** Measured: the three container rows (`lumberyard`, `foundry`,
  `silo`) carry **no `requiresVillageTier`** — they are not on the Heart axis at all, so no authored
  requirement in this WO's blast radius contradicts the ruling. The singleton flag landed in `a6bbc523d`
  (WO-2005). This lane added no capacity, count or level logic.
- **Ruling 24 (Cathedral priced as a small multi-resource basket, superseding ruling 22's stone).**
  **No conflict, and deliberately untouched.** The `arcane-tower` rows *are* Heart-gated
  (`requiresVillageTier` 0/1/2/3) and are still authored in crystals (T2 = 2,560). Re-pricing them is
  WO-2005 / WO-2007's job and is blocked on ruling 24's own open item — `BuildingUpgradeService.TierCost`
  picks **one** lane by tier index and cannot express a basket. This lane changed no cost row.
  ⚠ **The preview shows what opens, never what it costs**, so it cannot repeat the authored-vs-charged
  lie. A note in `UnlocksAt` records that any future cost line must read `BuildingTierChargeLane`, not
  the authored key.
- **Storage climbs to six levels; `RepoProps.MaxStructureLevel` is the single ceiling.** No level
  ceiling was re-hardcoded. `RepoProps.MaxStructureLevel` (6, per-structure ladder) and the Heart
  ceiling (3, `heart-progression.json`) are **two different axes**; both the new catalog and
  `VillageTierService.MaxTier` say so in-code so a future seat cannot conflate them.
- **Ruling 21 (the barracks BUILDING tier gates troops).** The troop derivation reads the building
  tier and never `GameState.BarracksLevel` — that is stated as a ⛔ in `AppendTroopsForBuildingRung`.

### Gate normalization audit — every tier-shaped gate in the tree

| Gate | Where | Disposition |
|---|---|---|
| `requiresVillageTier` (27 rows) | `building-tiers.json`, read by `BuildingUpgradeService:53-59` and `BuildingPerkService:194-199` | **IS the Heart gate.** Already normalized to "Heart Level" in copy by WO-2003. |
| `all.villageLevel` | `population-milestones.json` (echo slots 4, 5) | **IS a Heart gate**, compound (also needs quests/outposts). Now surfaced in the preview with an honest "also needs other milestones" suffix. |
| `CapByTier` = `{5,8,12,16}` | `PopulationService.cs:56`, **code-side, not JSON** | **IS a Heart gate.** Now surfaced via `CapAtVillageTier`. ⚠ **Recorded gap:** it is a balance ladder living in code. Moving it to JSON is a balance-data change and was deliberately not smuggled into this progression change. |
| `unlockBarracksTier` (9 troops) | `troops.json`, `TroopUnlock` | **DOCUMENTED EXCEPTION** — a genuinely different mechanic (building tier, ruling 21), but **transitively Heart-gated** because the barracks rungs carry `requiresVillageTier`. Left unchanged; now traversed. |
| Stone Gate | `RewardedProgression.TryUnlockStoneGate`, on `wall_wood` L2 | **DOCUMENTED EXCEPTION** — a structure-level reward, not a realm tier. Currently inert behind an art gate. |
| `unlockVictories` (0/3/10/20) | `scene-configs.json` | **DOCUMENTED EXCEPTION** — region/scene progression by victories. Not realm progression. |
| `requiresQuestId`, `requiresFlag`, `requiresFeature`, `requiresWallet`, `unlockMethod`, `unlockSpell`, `unlockAbility` | quests, gear/jeweler recipes, daily-quests, packs, cosmetics, abilities, hero-talents | **NOT tier gates.** Out of scope. |
| `RepoProps.MaxStructureLevel = 6` | `Core/Catalog/RepoProps.cs:`**`111`** | **DIFFERENT AXIS.** Per-structure ladder ceiling, not a realm tier. Never conflate. ⚠ **CLAUDE.md §8 cites `RepoProps.cs:69` for this constant. Measured at source 2026-09-06: it is line 111.** A stale line number in the read-first canon — flagged for the lead, not edited by this lane. |

**Finding worth the owner's eyes:** barracks tiers **4, 5 and 6 all carry `requiresVillageTier: 3`**.
The barracks ladder runs to 6 while the Heart ceiling is 3, so **the final Heart Level opens the path
to four troops at once** (Outrider, Siege Catapult, Battlemage, Echo Legionnaire) and nothing is
Heart-gated above it. That is authored, not a bug, and `ProgressionReachabilityRegression` passes on
it — but it means the last third of the troop roster hangs off a single Heart rung. A balance call.

### Proven at source before hand-off (things easy to assume and expensive to get wrong)

- **`PopulationService` declares `namespace DeNelle.Village.Population`** (`:39`), so the
  fully-qualified `DeNelle.Village.Population.PopulationService.CapAtVillageTier(...)` calls resolve.
  `Guard` and `FlowTrace` both declare `namespace DeNelle.Core.Diagnostics` (`Guard.cs:4`,
  `FlowTrace.cs:4`).
- **The three new `HeartUnlockKind` values cannot crash the view.** `HeartPanel` consumes the enum in
  exactly ONE place — `HeartPanel.cs:350`, `unlocks[i].Kind == HeartUnlockKind.Research` — a bare
  equality producing a bool. There is no `switch` with a throwing default and no icon array indexed
  by `(int)Kind`, so `Troop` / `PopulationCap` / `EchoSlot` render as ordinary non-research rows.
- **Adding sources to `UnlocksAt` would have BLUNTED the suite it feeds, and that was corrected.**
  Case 3 `[heart-level-opens-something]` tested `unlocks.Count == 0`. Because the population cap now
  rises at every level 1–3 unconditionally and each source is independently `Guard.Try`'d, a total
  failure of the building-tier derivation would have left a non-empty list and the case would have
  gone **green on the very defect its own failure text names**. It now counts
  `Kind == BuildingLevel` entries. Widening what an oracle observes without narrowing what it asserts
  is how a suite quietly stops asserting — the same species as the 394-green run these seam oracles
  exist for.

### Not proven by this lane

- **Nothing was compiled, gated or run.** Brace balance and NUL-freedom were checked on all five `.cs`
  files (all balanced, zero NULs); both JSON copies parse and are byte-identical (10 lines, CRLF,
  matching the working-tree convention under `core.autocrlf=true`). **That is not a compile.**
- **The new `[ladder-*]` regression case has not been executed.** It is written against measured
  behaviour and should be green, but that is a claim, not a result.
- **Acceptance criterion "no restart required to see newly unlocked tiles/actions" is UNPROVEN.**
  Three of the four links are proven at source: `HeartProgression.TryRaise` →
  `VillageTierService.TryUpgrade` does `Save()` + `ModifierService.Recompute()`;
  `HeartPanel.OnRaiseTapped` (`HeartPanel.cs:410-419`) calls `Render()` immediately after `TryRaise`,
  so **the Heart panel itself refreshes**; and `PopulationBootstrap.PollVillageTier` (`:159-163`)
  polls `VillageTierService.Current` every frame, so the population cap self-heals with no event.
  **What is NOT proven is the BUILD / ARMY grids behind the Heart panel** — whether `ManageScreenVM`
  re-projects its tiles when the player closes the Heart after a raise. No subscription from
  `ManageScreenVM` to a tier-changed signal was found, and `VillageTierService` raises no event; the
  likely path is a rebuild on panel re-open, which **this lane did not verify**. Closing it needs a
  runtime capture (§12), which an edit-only lane cannot take. **Recorded as open, not ticked** — if
  it is a real gap it is one subscription, not a refactor.
- **The preview list GREW and nobody has looked at it rendered.** Heart Level 3 now yields six
  building rungs + four troops (Outrider, Siege Catapult, Battlemage, Echo Legionnaire, all hanging
  off that one rung) + the perks on those rows + a population-cap line + an echo-slot line. If
  `HeartPanel` lays out a fixed row count or an unscrolled column, that is a **presentation** question
  for WO-2017 and an owner call — flagged so the lead sees it before opening a `UI_CAPTURE` PNG, not
  after.

---

## Requirements lane (Opus, 2026-09-07) — edit-only, ungated

**Status: EDIT-ONLY, NOT GATED.** No Unity lock held, no compile gate, no regression run, no commit.
Every claim below was measured at source **this session** or is named as unproven.

**Built on the 2026-09-06 lane, which is COMMITTED (`5bc5025f5`) and was verified at source before any
edit** — not taken from its own notes: `heart-progression.json` exists in both canonical copies and was
byte-identical (2,486 bytes, 10 CRLF each); `VillageTierService.cs:48` reads
`HeartProgressionCatalog.MaxLevel` and `:71-76` reads `CostToReach`, with no `const int MaxTier` and no
`250 *` literal left in the file.

### ⛔ The defect this lane closes — a named Fail that granted the thing anyway

`HeartProgressionCatalog.CostToReach` returns **0** for a level with no authored row, and
`VillageTierService.TryUpgrade` **skips the spend entirely when the cost is 0**
(`VillageTierService.cs`, `if (cost > 0) { ...TrySpend... }` then `s.VillageTier = Current + 1`
unconditionally). So a `maxLevel: 3` whose level-3 row was missing emitted a correct, well-worded
`FlowTrace.Fail` naming the hole — **and then raised the Heart for nothing.** The instrument fired, the
failure was named, and the system carried on. **A named failure that still hands the player the thing is
not a refusal** (CLAUDE.md §12). That is now refused at the sole writer.

### Files changed

| File | Change |
|---|---|
| `Assets/Resources/Data/Canonical/heart-progression.json` | `requiresBuildings: []` on all three level rows; `_authoringNotes` item (2) corrected in the same edit |
| `Assets/StreamingAssets/Data/Canonical/heart-progression.json` | byte-identical mirror (`cmp` clean, 3,569 bytes, 10 CRLF, was 10) |
| `Assets/_Modules/Core/State/HeartProgressionCatalog.cs` | `HeartBuildingRequirement` DTO; `HeartLevelDef.RequiresBuildings`; `HasAuthoredRow`; `RequirementsFor`; `LoadForTests` fixture seam; `ParseOrEmpty` extracted so the fixture runs the PRODUCTION parser |
| `Assets/_Modules/Village/Buildings/Progression/HeartProgression.cs` | `HeartRequirement` + `HeartLevelBundle`; `RequirementsFor` (the unlock-requirement reader); `BlockedReason`; `ResolveBundle` (the instrumented seam); `HeartActionState.MissingPrerequisite`; `TryRaise` refuses before the crystal check |
| `Assets/_Modules/Village/Buildings/Progression/VillageTierService.cs` | `TryUpgrade` refuses via `HeartProgression.BlockedReason` **before** the spend — the sole-writer enforcement |
| `Assets/Editor/Regression/HeartUnlockBundleRegression.cs` | **NEW** `[heart-bundle]`, five cases |
| `Assets/Editor/Regression/DataRegression.cs` | registered inside the fences, immediately after `heart-surface` |

`.meta` files for the new `.cs` are left for Unity to generate on import.

### Decisions, and why they went this way

- **Requirements are authored EMPTY on every shipped row.** ⛔ The owner rules on balance, so this lane
  shipped the **shape and its production reader** with **zero behaviour change**. Filling one array is
  now a data edit. Authoring a gate here would have been a re-balance smuggled into a plumbing change.
- **The check lives at the SOLE WRITER, not at the model.** There are **two** doors into a Heart raise:
  `HeartProgression.TryRaise` and `BuildingUpgradeVM.Select(VillageTierRowId)`
  (`BuildingUpgradeVM.cs:1045`, confirmed live this session). Gating only the model would have left the
  second door able to buy past an unmet prerequisite — the same "second door that skips the rule"
  species this program keeps finding. The model checks too, because it owns the player **sentence**;
  the writer owns the **truth**.
- **`HeartActionState.MissingPrerequisite` was added rather than folded into `MissingCrystals`**, which
  would have told a player who already holds the price to go and find crystals. Both live consumers were
  read at source first and neither is a `switch` or an enum-indexed array: `HeartPanel.cs:266/276/298/311/321`
  tests `== Max` / `== Ready` by equality, `ManageScreenVM.cs:583-585` is `!= Max`. **Neither file was edited**
  (another lane owns them this wave).
- **Still no second unlock table.** Unlocks stay derived by `UnlocksAt`; case `[bundle-no-second-table]`
  fails on an unexpected key in a level row, so the duplication WO-2004 forbids cannot be authored back
  in quietly.
- **The prerequisite level axis is the BUILDING ladder** (`ModifierService.TierOf`), never
  `GameState.BarracksLevel` (owner ruling 21) and never the Heart ladder. Three integer scales; the
  DTO and the reader both say so in-code.

### Suite cases — `[heart-bundle]`

| Case | Asserts | RED recipe |
|---|---|---|
| `[bundle-requirements-are-data]` | every shipped level row carries a `requiresBuildings` array, AND a fixture's one `{barracks, RepoProps.MaxStructureLevel + 1}` row parses, resolves against `ModifierService.TierOf`, reads UNSATISFIED and blocks the raise | delete the key from a row; or make `RequirementsFor` return empty unconditionally |
| `[bundle-missing-row-is-named-fail]` | `maxLevel 3` with two rows → `HasAuthoredRow(3)` false, bundle `IsAuthored` false, an **error-level trace names "Heart Level 3"**, and `BlockedReason(3)` refuses | make `HasAuthoredRow` return true; drop the `FlowTrace.Fail`; drop the refusal |
| `[bundle-resolve-is-traced]` | `ResolveBundle` emits one `[Flow:Heart]` step naming the level | drop the `FlowTrace.Step` |
| `[bundle-enforced-at-sole-writer]` | `VillageTierService` (comments stripped) names `HeartProgression.BlockedReason(`; the model names `HeartProgressionCatalog.HasAuthoredRow(` | delete the guard from `TryUpgrade` |
| `[bundle-no-second-table]` | a level row carries only `{level, costCrystal, requiresBuildings}`, and both canonical copies parse identically | author `"unlockBuildings": []` onto a row |

The fixture goes through `HeartProgressionCatalog.ParseOrEmpty` — **the same parse path the shipped
loader uses** — and observes the trace through a `CapturingSink` installed on `FlowTrace.Sink`. Sink and
catalog are both restored in `finally`, so no later suite inherits the fixture ladder.

### Hollow-pass finding on this suite — fixed (2026-09-07, same lane)

`Builds/reg-wave10b.log` reported
`HeartUnlockBundleRegression.cs:388 [A-missing-dependency] guard 'levels == null'`. The suite that is
*about* silent empties had one of its own: `CheckNoSecondUnlockTable` did `if (levels == null) return;`,
so a `heart-progression.json` that parsed but carried **no ladder** would have made case 5 check nothing
and report green. Resolved on the **fixture-absent** limb of the three-way rule (the canonical file is
present, so this is not a harness limit): the guard now **FAILS naming the path and the key**
(`HeartUnlockBundleRegression.cs:387-401`), and the loop's `o == null` row guard names the malformed row
rather than skipping past it (`:404-411`).

**Proven, not asserted.** `HollowPassScanner`'s arm A was replicated in Python from the `.cs` at source
(`scratchpad/arma_replica.py`, sibling of the WO-1500 lane's `armd_replica.py`): `BuildMasks`,
`BracesBalance`, `VerdictMethods`, the whole `ScanMethod` walk, `GuardBlockFor`, `TopLevelOnly` and all
five exonerations (`Asserted`, `AccumulatorVerdict`, `ReportedByProducer`, `SharesIdentifier(produced)`,
`SiblingReported`). ⚠ **The replica was validated against a known-true input before being trusted** — run
over a reconstruction of the pre-fix file it reproduces the coordinator's finding **exactly**:
`prefix_probe.cs:388 [A-missing-dependency] guard 'levels == null' (in CheckNoSecondUnlockTable)`. Over
the fixed file: `9 verdict methods scanned, RESULT: CLEAR`. Arm D was run over **every** verdict method
(`scratchpad/armd_allmethods.py`, since `armd_replica.py` only walks two hard-coded signatures): also
CLEAR. Braces 57/57, zero NULs.

*Not ported, deliberately:* the `AlreadyReportedNote` comment regex and the per-site `hollow-pass-ok`
opt-out. Both only ever **clear** a site, so omitting them can produce a false FINDING but never a false
CLEAR — the safe direction for a proof. Still not a gate run.

### Proven this session

- **JSON, byte-mode:** patched with a Python binary read/write, no text-mode rewrite. `CRLF 10 → 10`,
  `LF 10 → 10`, `2,486 → 3,569` bytes; both copies `cmp`-identical; `json.loads` parses to
  `maxLevel 3`, levels `[(1,250,[]), (2,500,[]), (3,750,[])]`.
- **Brace balance + NUL guard on all five `.cs`:** 29/29, 44/44, 9/9, 56/56, 1150/1150, zero NUL bytes.
- **Registration sits INSIDE the fences** (START 298 / END 1863; the new line is at ~1099), so it is
  COUNTED, not merely run.

### NOT proven by this lane

- **Nothing was compiled, gated, run or committed.** The `[heart-bundle]` suite has **never been
  executed** — it is written against behaviour measured at source and should be green, but that is a
  claim, not a result. The suite count will move by one.
- **The free-realm hole was reasoned from source, not reproduced at runtime.** The three facts are each
  read at source (`CostToReach` returns 0 on a missing row; `TryUpgrade` skips the spend at cost 0; the
  tier is then assigned unconditionally) — the composition is not measured on a device.
- **`State` now costs a catalog lookup + a `Guard.Try` closure per read.** Call sites were grepped
  rather than measured: the only production reader of `ManageScreenVM.HeartUpgradeAvailable` is
  `ManageScreenPanel.cs:2071`, which assigns `_hubHeartShown` **once at chrome-layout time** and hands
  the answer to both writers (its own comment says so). So it is not on a frame path today. **That is a
  call-pattern read, not a cost measurement** — if a frame-cost ticket ever names it,
  `FlowTrace.Measure`'s 4-arg form is the instrument.
- **The 2026-09-06 lane's open item still stands:** whether the BUILD / ARMY grids re-project after a
  raise without a restart. Unchanged by this lane; still needs a runtime capture.
- Owner rulings 12 (data-driven reach), 24 (Cathedral basket) and the duration/reward fields remain
  **deliberately absent** — no system exists to make them real, and authoring them would trip
  `AuthoredFieldReaderRegression` Case C.

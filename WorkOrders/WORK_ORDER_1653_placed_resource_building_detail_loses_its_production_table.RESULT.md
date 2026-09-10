# WO-1653 RESULT — a PLACED resource building's detail card keeps its numbers

**Status:** IMPLEMENTED - awaiting gate
**Lane:** MANAGE-VM (isolated worktree `.claude/worktrees/agent-a6afae9850478e00d`, branch `dev` @ `efc56f67c`)
**Date:** 2026-09-10
**No Unity run, no commit** — per the lane brief. Every claim below is either a file read at source
this session or a line grepped out of a captured log; nothing is inferred.

> ## ⚠ SILO DEVIATION, DECLARED: `Assets/_Modules/Village/Buildings/CrystalMine.cs` WAS TOUCHED.
> The WO's silo says "`ManageScreenVM.cs` (VM only)". §2 below explains why the ticket cannot be
> closed inside that silo. The edit there is a **producer extraction only — zero behaviour change**
> (a private instance method became public static; the per-instance curve cache became static). It is
> additive and merges trivially. **The lead should rule on it before commit.**

---

## 1. ⛔ THE WO's §2 CAUSE WAS WRONG — TWICE. Both corrections are proven, not argued.

### (a) It is NOT branch ordering.
The WO says *"the branches are tried in order, and the Defense branch wins first"* — while citing
`:5301` for Defense and `:5284` for Building, i.e. contradicting itself. Read at source: the composer
tries **`BuildingChoiceFor` FIRST** (`ManageScreenVM.cs:5283`), Defense second (`:5301`).
**Reordering the branches would have changed nothing.**

**The real cause is LIST MEMBERSHIP.** `BuildBuildingChoices` (`ManageScreenVM.cs:1580`) opens with
`if (!BuildingTierCatalog.IsUpgradable(id)) continue;`, so an id with no `building-tiers.json` ladder
never enters `BuildingChoices` and `BuildingChoiceFor("mine_crystal")` returns **null**.

Proved on the captured run **`Builds/wave5-manageflow2`** (read this session with
`tr -d '\000' < Builds/wave5-manageflow2 | grep -a ...`):

| Trace | Ids |
|---|---|
| `building choice id=` | arcane, armorer, barracks, farm, forge, lumbermill |
| `defense choice id=` | foundry, healing_caravan, lumberyard, **mine_crystal**, silo, tower_arcane_spire, tower_ballista, tower_catapult, tower_ground_archer, tower_siege_tower, wall_wood |
| counts | `building choices projected=6` / `defense choices projected=11 (from 27 placement(s), 12 with no level ladder)` |

The WO's own quoted line is in that log too:
`[Flow:Manage] defense choice id=mine_crystal placed=1 lowest=L2/3 state=Upgradable ... portrait='Portraits/Buildings/mine_crystal-2'`
and `grep -a "building detail production"` returns **exactly one line, and it says `forge`** — the WO's
evidence is correct; only its explanation of it was not.

### (b) `BuildingStatRows` could NOT have served this card either.
The WO asserts *"`BuildingStatRows` is gated on `ResourceBuildingProgression.IsResourceBuilding(b.Id)`
and `nowPerHour > 0.0`, **both of which `mine_crystal` satisfies**"*. **That is false at source.**
`ResourceBuildingProgression` knows exactly THREE ids —
`FarmId = "farm"` / `LumbermillId = "lumbermill"` / `ForgeId = "forge"`
(`ResourceBuildingProgression.cs:173-175`, and its own `OrderedIds` at `:224`). `IsResourceBuilding`
is `Find(id) != null` (`:233`). **`mine_crystal` is not in it.**

Had the WO's prescribed fix been implemented literally — "compose the stats from `BuildingStatRows`"
— the Crystal Mine card would have rendered **exactly nothing new**, and the ticket would have been
closed on a card that had not changed.

**The Crystal Mine has no per-hour production at all.** It pays **per cleared wave**, off
`buildings.json`'s authored `crystalsPerWave` curve (`[1, 2, 4]`, indexed by `level - 1`) — read at
`Assets/Resources/Data/Canonical/buildings.json:16` and `CrystalMine.cs:151-168`. Printing
`Production / hr` over that number would be a unit the game does not use, on the one screen a player
uses to decide (CLAUDE.md §11B). **So the row says `Crystals / wave`.**

### (c) The level axis is the PLACED level, not a city tier.
Everything reaching this branch is on the placed-structure ladder (`UpgradeFamilyResolver` rule 1), so
`DefenseChoiceVM.Level -> NextLevel` is the axis an upgrade moves — the same axis
`TownBankCapacity.CapacityAtLevel` and `CrystalMine.CrystalsPerWaveAt` already key off. That is a
**different axis** from the city-ladder card (same harvest level, next tier's multiplier), which is
precisely why the two composers stay separate instead of sharing a method that would have to be told
which axis it is on.

---

## 2. WHAT SHIPPED

| File | Change |
|---|---|
| `Assets/_Modules/Village/UI/Manage/ManageScreenVM.cs` | `:5312` the placed branch's `stats = TwoFacts("Placed", ...)` became `stats = DefenseStatRows(d)`. New method `DefenseStatRows` at `:5723` composes **Storage** (via the one capacity reader, at the placed level), **Crystals / wave** (via the one payout producer), then **KEEPS the `Placed` fact**, then the `Next level` prose. |
| `Assets/_Modules/Village/Buildings/CrystalMine.cs` | `:163` `CrystalsPerWave` → **`public static int CrystalsPerWaveAt(int level)`**; `Curve()` and its cache became static (`s_curve`, `:91`). `OnWaveCleared` now calls it. **Silo deviation — see the banner.** |
| `Assets/Editor/Regression/ManageProgressiveDisclosureRegression.cs` | New case **`[placed-producer-keeps-its-numbers]`**, registered at `:34`. |

**Nothing was deleted and no pin was removed.** `BuildingStatRows` is **byte-identical** — the WO's §5
forbids editing it and it did not need editing. `TroopStatRows`, `StatRow`'s gold/bold treatment, the
renderer, and the Defense rail are all untouched by this ticket.

### Rows a subject with no numeric producer gets
**Exactly what it has today.** `DefenseStatRows` returns the same `TwoFacts("Placed", ...)` when
nothing numeric can be composed, so **every tower and wall card is unchanged**, and logs one
`FlowTrace.Once("Manage", "placed-stat-placed-only:<id>", ...)` saying so rather than failing silently.

### New instrumentation (§12 — permanent, never stripped)
- `[Flow:Manage] placed detail crystals id=mine_crystal L2 now=2/wave next(L3)=4/wave`
- `[Flow:Manage] placed detail storage id=silo L1 now=<n> next(L2)=<n>`
- `FlowTrace.Warn` on a container whose capacity reads 0, and on a mine whose curve authors a zero rung.
- `FlowTrace.Once` on the placed-only fallback.

---

## 3. THE PIN — `[placed-producer-keeps-its-numbers]`

`Assets/Editor/Regression/ManageProgressiveDisclosureRegression.cs` (the suite that already owns the
detail composer's stat table, via `[building-production-single-producer]`). **No `DataRegression.cs`
registration line is needed** — the suite is already wired.

It pins the **COMPOSED VM**, per the WO's acceptance 2 (⛔ never `nav` ordering, never branch text):
1. **The mine.** A placed **L2 `mine_crystal`** composes a `Crystals / wave` row whose `Value` and
   `DeltaText` equal `CrystalMine.CrystalsPerWaveAt(2)` / `(3)` — the **same producer `OnWaveCleared`
   pays from**, formatted through the same `(float).ToString("N0")` the VM uses. It also FAILS if
   L2→L3 moves the yield by nothing, so the delta half can never assert on a flat pair.
2. **The container.** A placed **L1 `silo`** composes a `Storage` row equal to
   `TownBankCapacity.CapacityAtLevel` at the same axis. Different producer, different structure — part
   1 passing on a special case cannot carry part 2.
3. **The `Placed` fact survives** on both cards. The fix ADDS rows; it must never trade one truth for
   another.

Every unreachable seam is a **FAIL, not a skip** (catalog can't resolve the ids / state seam not
reflectable / silo is no longer a container). The fixture is modelled on the existing
`CheckBuildingProductionRow` and restores `GameStateService.Instance` and the tab pref in its `finally`.

### RED, and how honest that claim is
⚠ **THE CASE HAS NOT BEEN RUN — no Unity, per the brief. It is RED BY CONSTRUCTION**, and the exact
today-line is named so anyone can reproduce it: restore
`stats = TwoFacts("Placed", Ascii(d.PlacedText), null, null);` in the placed branch (it was
`ManageScreenVM.cs:5306` before this change, `:5312` after) and parts 1 and 2 both fire with *"has no
… row"*, because `TwoFacts` emits the `Placed` pair and nothing else. That recipe is written into the
case's own header comment. **The gate is what turns this into a fact.**

---

## 4. ACCEPTANCE, ANSWERED HONESTLY

| # | WO says | Status |
|---|---|---|
| 1 | Crystal Mine card carries `Production / hr <now> -> <next>` + `Placed` | ⛔ **RE-POINTED, see §1(b).** The mine has no per-hour production; the honest row is **`Crystals / wave  2 -> 4`**, plus `Placed`. Needs a fresh `RunManageFlowMapCaptureHeadless` + the PNG opened — **NOT DONE (no Unity in this lane).** |
| 2 | RED-first oracle on the composed VM | ✅ written, pinned on the composed VM. **Unrun — RED by construction, §3.** |
| 3 | `[Flow:Manage] building detail production id=mine_crystal ...` on the run | ⛔ **CAN NEVER FIRE** — `BuildingStatRows` is never reached for the mine and could not produce that line (§1b). **Re-pointed to `[Flow:Manage] placed detail crystals id=mine_crystal L2 now=2/wave next(L3)=4/wave`.** |
| 4 | `COMPILE_GATE_OK` + `REGRESSION_OK <n>/<n>` on fresh logs | **NOT DONE — the lead gates.** `python tools/gate_brace.py` clean on all touched files; NUL scan clean. |
| 5 | Status → `AWAITING OWNER MATCH` | Set to **`IMPLEMENTED - awaiting gate`** on the lead's explicit instruction; it becomes `AWAITING OWNER MATCH` once the gate is green. |

**Unproven and stated as such:** that the composed card renders correctly on a device frame. No
capture was taken. §11B: an unproven thing named as unproven is useful; stated as fact it is a lie.

---

## 5. TWO SEAMS PROVEN AT SOURCE BEFORE HAND-BACK (not assumed)

- **`DefenseChoiceVM.CatalogEntryId` IS populated**, so `DefenseStatRows`' `repo` lookup cannot
  silently resolve null and no-op the whole fix. `ManageScreenVM.cs:1846` reads
  `CatalogEntryId = !string.IsNullOrEmpty(entry.id) ? entry.id : tally.ItemId` — it already carries its
  own fallback to the BaseLayout itemId. Read at source this session, not taken from the field's doc
  comment.
- **No source-scan pin breaks on the edited call shapes.** `ManageMockupConformanceRegression`'s
  `[detail-next-by-weight]` extracts `BodyOf(workspace, "private void BuildStatRows(")` and asserts
  `bold: promoted` — intact, the WO-1654 edit sits above the value label.
  `ManageDumbViewRegression`'s 16 banned shapes were read line by line against the added renderer
  lines: no `Resources.Load`, no service/state token, no `.Count` comparison, no id parsing or
  switch, no cost/gate comparison, no new `using`.

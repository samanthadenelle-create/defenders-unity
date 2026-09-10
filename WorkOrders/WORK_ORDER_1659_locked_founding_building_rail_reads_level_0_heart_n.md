# WORK ORDER 1659 — the BUILD rail sub-line still paints the tier-0 sentinel as "Level 0"

**Status:** CLOSED 2026-09-10 - NO LIVE DEFECT (lead close on the lane's source + run proof: RenderList returns into RenderWorkspace at ManageScreenPanel.cs:4688 before the rail ever renders, zero `building rail selected=` lines on wave6-manageflow1, the detail card is WO-1657 item B and the grid tile never painted level 0). PRIOR STATUS: ⛔ STOPPED BEFORE IMPLEMENTATION - NO LIVE DEFECT. Needs a lead/owner ruling: close as NO-OP, or re-scope to the dead-code REMOVAL this uncovered. **No code was written.**

> # ⛔ STOP — 2026-09-10, MANAGE-VM lane. THE PREMISE OF THIS TICKET IS FALSE.
> **BOTH surfaces this WO targets are DEAD CODE. Neither can render on the shipped build.**
> I wrote this ticket; the error is mine, and it is recorded here rather than quietly fixed.
> Full proof in `WORK_ORDER_1659_locked_founding_building_rail_reads_level_0_heart_n.RESULT.md`.
>
> **THE PROOF, in one chain:** `ManageScreenPanel.BuildWorkspaceHost(well)` is called
> **unconditionally** at `:1595`, and it always constructs both `_workspace` and `_workspaceHost`.
> `WorkspaceActive` is `_workspace != null && _workspaceHost != null` (`:1761`) — therefore
> **always true**. `RenderList` opens with **`if (WorkspaceActive) { RenderWorkspace(); return; }`**
> (`:4688`). `RenderBuildingsDestination` is called at **`:4710`**, *after* that return —
> so `RenderBuildingsDestination` → `AddBuildingWorkspaceRow` → `BuildBuildingRailRow` →
> **`:5024` never executes**, and neither does anything that paints `LockText` (`:5023`).
>
> **THE CODE ALREADY SAYS SO, in its own words** (`ManageScreenPanel.cs:4681-4684`):
> *"⚠ It is nevertheless **DEAD CODE UNDER GREEN PINS** - the exact shape
> ManageQueueDrawerRegression:103-113 exists to catch - so its removal, and the pin moves that must
> precede it, are itemised in this work order's hand-back. **Do not leave it here indefinitely.**"*
> I did not read that block before minting this WO. That is the whole miss.
>
> **CONFIRMED ON A FRAME AND ON A RUN, not just at source:**
> `Builds/device-frames/2026-09-10_0915b_363722_build_detail_quarry_placed.png` (opened this session)
> shows **no rail at all** — a full-bleed detail card, portrait left / stats right / one UPGRADE
> face, which is `ManageWorkspacePanel`'s shape, not the rail+card split. And
> `Builds/wave6-manageflow1` carries **ZERO** `building rail selected=` lines while every BUILD
> screen logs `workspace screen=Detail/BUILD ...`.
>
> **AND THE TWO SURFACES THAT ARE LIVE ARE BOTH ALREADY CORRECT:**
> - the DETAIL CARD was fixed by **WO-1657 item B** (`ManageVmProjection.cs:319-322`);
> - the GRID TILE **never had the bug** — `ManageVmProjection.cs:210` already reads
>   `item.MaxLevel > 0 && item.Level > 0 ? "LEVEL " + item.Level : <effect sentence>`, and its own
>   comment cites ruling 3.7 *"never paint LEVEL 0"*.
>
> **SO THERE IS NOTHING A PLAYER CAN SEE THAT THIS TICKET WOULD FIX.** Implementing it would have
> changed no pixel, added a regression case asserting on an unreachable path (making the removal the
> code asks for *harder*), and produced a RESULT claiming a live defect was fixed. That last part is
> the one that costs someone a day (CLAUDE.md §11B).
>
> ## THE REAL FINDING, worth more than the ticket
> `ManageScreenPanel`'s retired Buildings/Troops/Defense/Research destination chrome is dead and its
> own comment asks for it to be removed, with the pin moves itemised. **That removal is a genuine
> ticket** — and it would delete `:5024` and `LockText` outright, which is the correct end state for
> both halves of this WO. ⚠ It is a REMOVAL under green pins, so it needs the lead's scoping and the
> pin moves done first; it is not this lane's to start unasked.
>
> **DECISION NEEDED:** close WO-1659 as NO-OP (nothing live to fix), or re-scope it to that removal.
> ⛔ Until then §2A's "LIVE" label below is WRONG — read this box, not that line.
**Silo:** Manage BUILD rail sub-line — `ManageScreenPanel.cs` (renderer) + the shared founding-line seam in `ManageVmProjection.cs`
**Number:** PRE-ASSIGNED by the lead. ⛔ **Do NOT edit `CLI_LANES_WO_NUMBERS.md`.**
**Opened:** 2026-09-10 by the MANAGE-VM lane, as the named follow-up of **WO-1657 item B**
**Source:** WO-1657 RESULT §6 ("follow-ups named, not touched")

---

> ## ⚠ THE TITLE AND THE ORIGINATING NOTE ARE BOTH IMPRECISE — READ §2 BEFORE SCOPING.
> WO-1657's RESULT flagged this as *"`LockText` reads `Level 0 . Heart N`"*. **Verified at source
> while writing this ticket: that string is NOT REACHABLE TODAY.** The live defect is its
> **sibling** — the rail's *unlocked* branch — and it **did** occur on a captured run. The `LockText`
> half is real but **LATENT**. Both are specified below, correctly labelled, because shipping the
> latent one as though it were the observed one is how a fix gets "verified" against a frame that
> never showed the bug.

---

## 1. THE ROOT — one sentinel, three readers, and WO-1657 fixed only the first

`ModifierService.TierOf` returns **0 on a `GameState.BuildingTiers` DICTIONARY MISS**
(`Assets/_Modules/Core/State/ModifierService.cs:44-47`) — a building that is **placed and producing
but has bought no rung**. `BuildBuildingChoices` puts that straight into `BuildingChoiceVM.Level`
(`ManageScreenVM.cs:1583`).

**WO-1657 item B fixed the DETAIL CARD only** (`ManageVmProjection.LevelText`, `:319-322` → the
`FoundingLevelText` / `FoundingLevelWord` seam). The **rail** was explicitly out of that ticket's
scope and still reads the sentinel raw.

### The three readers of `BuildingChoiceVM.Level`, as of `aba49bd4c` + WO-1657 item B

| # | Site | Composes | Status |
|---|---|---|---|
| 1 | `ManageVmProjection.cs:319-322` (detail card) | `"Level N of M"` / founding line | ✅ **FIXED by WO-1657 item B** |
| 2 | **`ManageScreenPanel.cs:5024-5027`** (BUILD rail, **UNLOCKED** branch) | `"Level " + choice.Level + …` | ⛔ **THE LIVE DEFECT — §2A** |
| 3 | **`ManageScreenVM.cs:1628`** (`LockText`, painted by the rail's **LOCKED** branch at `ManageScreenPanel.cs:5023`) | `"Level " + level + " . Heart " + next.RequiresVillageTier` | ⚠ **LATENT, not reachable today — §2B** |

---

## 2. WHAT IS ACTUALLY BROKEN, AND WHAT ONLY LOOKS BROKEN

### 2A. ⛔ **NOT LIVE — SUPERSEDED BY THE STOP BOX ABOVE.** (Was: "LIVE — the rail's UNLOCKED sub-line paints `Level 0`")

⚠ The reasoning below is correct *about the expression* and wrong about whether it ever runs: `RenderList` early-returns at `ManageScreenPanel.cs:4688` before `RenderBuildingsDestination`. Kept unrewritten as the record of the miss.

`Assets/_Modules/Village/UI/Manage/ManageScreenPanel.cs:5024-5027`:

```
string railState = choice.Locked
    ? ManageScreenVM.Ascii(choice.LockText ?? "Locked")
    : "Level " + choice.Level +
      (Max ? " . Max" : Building ? " . Building" : "");
```

A founding building is **not** locked (proved in 2B), so it takes the **else** arm and the rail reads
**`Level 0`** — beside a detail card that, since WO-1657, correctly says it is not yet upgraded. **The
two halves of one screen now disagree about the same building**, which is worse than the original
single wrong number.

**PROVEN REACHED on a captured run** — `Builds/wave6-manageflow1` (fresh, 2026-09-10 09:00,
NUL-padded; read with `tr -d '\000' < <file> | grep -a`):

```
[Flow:Manage] building choice id=farm level=0/4 state=Upgradable next=1 ready=True
              icon='Portraits/Buildings/farm' benefit='Stone production +10%.'
```

`state=Upgradable` (⇒ `Locked == false`) **and** `level=0` — exactly the pair that lands on the
`else` arm. ⚠ **Stated precisely:** the log proves the CHOICE fed to the rail carried `Level 0` and
was unlocked; **the rail STRING itself is not traced**, so the composition is read off
`ManageScreenPanel.cs:5024` at source rather than off a log line. It is a one-expression step from a
proven input, not an inference about behaviour.

### 2B. LATENT — `LockText` cannot compose `Level 0 . Heart N` today

`ManageScreenVM.cs:1628`:

```
LockText = isLocked ? "Level " + level + " . Heart " + next.RequiresVillageTier : "",
```

with `isLocked = !isMax && next.RequiresVillageTier > villageTier` (`:1591`), where `next` is
`BuildingTierCatalog.TierOf(id, level + 1)` — **tier 1** when `level == 0`.

⛔ **EVERY ladder authors `requiresVillageTier: 0` on its tier-1 row.** Read out of
`Assets/Resources/Data/Canonical/building-tiers.json` 2026-09-10, all six ladders
(`arcane-tower`, `armorer`, `barracks`, `forge`, `lumbermill`, `farm`): **tier 1 →
`requiresVillageTier = 0`**. So for a founding building the gate is `0 > villageTier`, which is
**false for every non-negative Heart level**. **A level-0 building can therefore never be `Locked`,
and `LockText` can never today produce `Level 0 . Heart N`.**

It becomes reachable **the moment anyone authors a non-zero `requiresVillageTier` on a tier-1 row** —
a pure data edit, with nothing today that would catch it. That is why this half is worth fixing in
the same pass and pinning, **but it must not be reported as an observed defect.**

---

## 3. FRAME EVIDENCE — **NOT OBSERVED.** Say so; do not imply otherwise.

Searched this session:

- `Builds/ui-capture/` — the flow-map captures a `_locked` frame for **ARMY**
  (`ManageFlow_ARMY_locked_2670x1200.png`) and **RESEARCH**
  (`ManageFlow_RESEARCH_locked_2670x1200.png`). **There is no `ManageFlow_BUILD_locked_*` frame at
  all**, so the BUILD rail's locked sub-line is captured by nothing.
- `Builds/device-frames/` — the only BUILD-side frames from the 363722 session are
  `2026-09-10_0914c_363722_build_economy_grid.png` (the **grid**, not the rail) and
  `2026-09-10_0915b_363722_build_detail_quarry_placed.png` (the **detail card** — WO-1657's frame).

> ### ⛔ **NO FRAME THIS SESSION SHOWS EITHER RAIL SUB-LINE. NOT-OBSERVED.**
> 2A is proved from a captured log line plus one source read; 2B is proved **unreachable** from
> authored data. **Neither has been seen by an eye.** Do not write "confirmed on a frame" anywhere in
> the RESULT for this ticket until acceptance 4 below is genuinely satisfied.

---

## 4. THE FIX SHAPE

### ⛔ THE ONE RULE THAT OUTRANKS EVERYTHING ELSE HERE: **NEVER SEED TIER 1.**

`ManageScreenVM.CountPlacedThisTown`'s doc block (`ManageScreenVM.cs:1487-1505`) — written for the
owner's 2026-08-08 felt-test *"no building upgrades are on the manage button anywhere"* — already
ruled on this, verbatim:

> *"TierOf reads GameState.BuildingTiers, which **only ever contains ids that have been UPGRADED**"*
> ⛔ **"DO NOT 'fix' a future variant of this by writing tier=1 at placement.** Tier 1 is a PAID
> upgrade (barracks T1 = 900 wood / 750 food / 150 crystals) and it grants `structureHpBonusPct 0.20`
> through `ModifierService.StructureHpMultFor` … so seeding it would gift every newly placed building
> a free upgrade. **The ladder is 1-based for UPGRADES, not for existence: tier 0 = placed."**

Seeding a tier would also move live economy on a game shipping on the Solana dApp Store. **Tier 0 is
correct. The display is the only thing that may change.**

### 4.1 Route BOTH rail branches through the SAME seam WO-1657 created

The founding wording already exists and is already flagged as the owner's to change:
`ManageVmProjection.FoundingLevelWord` + `FoundingLevelText(int maxLevel)`
(`Assets/_Modules/Core/Manage/ManageVmProjection.cs`, added by WO-1657 item B).

⚠ **They are `private` today.** Widening them is the point of this ticket — **not** writing a second
founding string. `ManageVmProjection` is `DeNelle.Core.Manage`, which `ManageScreenPanel` and
`ManageScreenVM` can both already reach (`ManageScreenVM.cs:50` imports it), so the seam can serve all
three readers with no new dependency.

⛔ **DO NOT COPY THE WORDS INTO THE PANEL.** Two spellings of one founding line is precisely the
duplicated state CLAUDE.md §2 / §5 / §16 each record — and it would silently defeat the one-token
owner override WO-1657 built. **One producer, three readers.**

- **2A (rail, unlocked):** when `choice.Level <= 0`, the sub-line takes the shared founding text
  instead of `"Level 0"`. The `. Max` / `. Building` suffixes are orthogonal — a founding building is
  neither, but do not restructure that expression beyond the level term.
  ⚠ **The rail sub-line is a MICRO font in a ~22-30px band** (`FontMicro`, `FitSingleLine(sub, 22f,
  30f)`), narrower than the detail card's level band. The detail card's `"Not yet upgraded . 4 levels"`
  **may not fit here.** A shorter rail variant off the same constant is acceptable and expected;
  a *different word* is not. **Measure it — do not assume it fits** (memory
  `layout-tickets-need-a-fresh-capture`).
- **2B (`LockText`, latent):** compose the level half from the same seam so the string reads as a
  founding building gated by the Heart, never `Level 0 . Heart N`. **The `Heart N` half is correct and
  stays.**

### 4.2 The wording stays the owner's
Whatever is added must keep the WO-1657 property: **change the constant, never the branch.** If a
shorter rail form is needed, derive it from `FoundingLevelWord` — do not introduce a second authored
string that can drift out of sync with it.

---

## 5. RED-FIRST PIN

⛔ **Pin the COMPOSED string, and pin the SEAM — never the words** (the wording is an owner call).

Extend **`Assets/Editor/Regression/ManageBuildingsCardRegression.cs`**, which already owns the BUILD
rail *and* now owns `[founding-building-is-not-level-zero]` (WO-1657 item B) — so the founding fixture
is already there to reuse: a placed **`collector_farm`** with **no `farm` key in `BuildingTiers`**.
**No `DataRegression.cs` registration line is needed**; the suite is already wired.

⛔ **`collector_farm` is the CATALOG / `BaseLayout` id; `farm` is only the LADDER id**
(`CatalogRegistry.ResolveUpgradeId` maps one to the other, and `CountPlacedThisTown` keys on the
result). **There is no `farm` row in `structures-catalog.json`** — placing `"farm"` places a GHOST that
nothing counts, and the case then FAILS identically before and after the fix. The existing fixture
already verifies that mapping as a FAIL-not-skip; **reuse it, do not rewrite it.**

Three parts, failing for three different reasons:

1. **THE LIVE ONE (2A).** The rail sub-line composed for a founding, unlocked building does **not**
   equal / begin with the level-zero literal, and still says something. ⚠ `BuildBuildingRailFace` is a
   UGUI builder this EditMode suite cannot stand up — so either extract the `railState` expression into
   a testable pure function and pin **that** (**preferred**: it makes the string reachable without a
   renderer), or, failing that, scan `BodyOf(panel, ...)` for the shared seam call. **Say in the RESULT
   which one was used and why** — a source scan is weaker and must be labelled, not dressed up.
2. **THE LATENT ONE (2B).** `BuildingChoiceVM.LockText` for a founding + locked choice does not open
   with the level-zero literal. ⚠ **This state is UNREACHABLE from live data (§2B)**, so the fixture
   must **construct or force it** rather than expect the catalog to produce it. **If it cannot be
   forced without faking, say so and pin the composer at source instead — do not fake a green.**
3. **THE ORDINARY PATH IS BYTE-IDENTICAL.** With `BuildingTiers["farm"] = 2`, the rail still reads
   exactly `"Level 2"` (plus any `. Max` / `. Building` suffix). The founding branch must not bleed.

**RED PROOF, to be stated with the exact today-line:** against the pre-fix tree part 1 fires on
`ManageScreenPanel.cs:5024-5027`'s `"Level " + choice.Level`.

⚠ **Build the forbidden literal from parts in every failure message** (e.g. `"Level " + 0`) — a raw
literal in a message can trip `ManageResearchCardRegression`'s `[no-level-zero]` source scan, which is
a pin going RED on correct code.

---

## 6. WHAT NOT TO TOUCH

- ⛔ **`ModifierService.TierOf`'s `return 0`, and ANY notion of seeding a tier at placement.** §4's
  ruling. Tier 0 is correct; only the display may change.
- ⛔ **`ManageVmProjection`'s detail-card branch** — WO-1657 item B shipped it and it is pinned by
  `[founding-building-is-not-level-zero]`. **Widen the seam's visibility; do not re-cut the branch.**
- ⛔ **The DEFENSE rail (`ManageScreenPanel.cs:6027`).** WO-1657 §3 named it alongside `:5024`, but it
  **cannot** be affected: `BuildDefenseChoices` floors its level with
  `Mathf.Clamp(tally.LowestLevel, 1, ceiling)` (`ManageScreenVM.cs:1801`), so a defense choice is never
  below 1. **Leave it alone** — "fixing" it would add an unreachable branch and a pin asserting nothing.
- ⛔ **The `Heart N` half of `LockText`, `LockReason` and `LockCtaLabel`** (WO-1423 / WO-2003). Only
  the LEVEL term moves. `LockReason` is the card's body sentence and `LockCtaLabel` is the door word;
  neither is in scope.
- ⛔ **`requiresVillageTier` in `building-tiers.json`.** The zeros are what make 2B latent. **Do not
  author a non-zero value to "make the bug reproducible"** — that is a live economy/gating change made
  for a test's convenience.
- ⛔ **Do not add a second founding string anywhere.** One producer (§4.1).
- ⛔ **Do not delete or weaken `[founding-building-is-not-level-zero]`** to accommodate a refactor —
  re-point it and say so (§15).
- Do not touch `Production / hr`, the storage caps, the harvest path, or anything in **WO-1657 item A**
  (the Gold-in-the-basket owner question is still unruled — `CostGold`, both catalogs and
  `BuildingUpgradeRegression:593-594` stay untouched).

---

## 7. ACCEPTANCE

1. The rail sub-line and the detail card **agree** on a founding building — neither claims a level,
   both come from the ONE seam.
2. `ManageVmProjection.FoundingLevelWord` remains the single place the wording is authored, and
   changing it still changes every surface at once.
3. RED-first: the new case(s) fail against today's tree, with the today-line named.
4. **A FRAME.** ⛔ §3 records that **no frame this session shows either rail sub-line** — so this
   ticket cannot be closed on the existing captures. A fresh `RunManageFlowMapCaptureHeadless` (or a
   device frame) showing the BUILD rail with a founding building present, **opened**. If a
   `ManageFlow_BUILD_locked_*` frame still does not exist, say so rather than substituting the ARMY or
   RESEARCH one.
5. `COMPILE_GATE_OK` + `REGRESSION_OK <n>/<n> suites` on **fresh** logs, judged by the **marker**,
   never the exit code.
6. `**Status:**` → `AWAITING OWNER MATCH` per WO-1566 §2.0 — the owner's eye on a device frame is the
   verdict, not this list.

---

## 8. WHAT IS UNPROVEN

- **That either rail line has ever been seen.** Both are proved from source + a log line; **neither is
  on a frame** (§3).
- **Whether the detail card's `"Not yet upgraded . 4 levels"` fits the rail's micro band.** Not
  measured. §4.1 requires measuring it rather than assuming.
- **Whether any OTHER surface reads `BuildingChoiceVM.Level` raw.** Three readers were found by
  grepping `LockText` and the two `railState` sites; **that is a grep, not an exhaustive audit.**
  A `Level`-token sweep across the Manage surfaces before implementing would close it.

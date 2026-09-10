# WO-1566: the Manage conformance spec — the end-of-session definition of DONE

**Status:** AUDITED 2026-09-10 - 51 pass / 4 fail / 11 unmeasured / 1 blocked-on-art / 1 superseded (68 rows), see RESULT — this is a **SPEC, not a lane.** It does not itself change code; it is the
acceptance every Manage lane is measured against before the session closes.
**Silo:** none — it is the yardstick. Each row names the lane that owns the fix.
**Source:** owner ask 2026-09-06: *"can you write the specs so the work at the end of CLI's session matches
the documents its already been given. All the screens were told to match."*
**Minted** from the banner (`CLI_LANES_WO_NUMBERS.md`, main line 1566 -> 1567 in the SAME edit).

---

## 0. WHAT THIS ADDS, AND WHAT IT DOES NOT REPLACE

⛔ **`WorkOrders/ManageRedesign/CAPTURE_LOOP_GOAL.md` REMAINS THE LOOP AND IS NOT SUPERSEDED.** It owns
the cycle (gate → capture → open the PNGs → compare → feed back), the eight-screen table at §3.0, the
chrome rules at §3.0b, the invariants at §3c, and the owner's binding directives — *"I don't want similar
ideas. I want this is exactly what shows"*, and ⛔ ***do not open an AskUserQuestion about the Manage
screens.*** Read it first. This file does not repeat any of it.

**This file adds the two things that document does not have:**
1. **§2 — a per-panel, element-by-element acceptance**, so "does it match?" is answered the same way by
   any seat, from a frame, without judgement.
2. **§3 — an ASSET BINDING table**, naming which delivered file must render on which element. This is the
   missing link: `docs/ART_DELIVERY_2026-09-06_manage_assets.md` identifies the delivered art and stops at
   *"nothing has been copied, converted or imported"* — **nothing in the repo says where any of it goes.**

**The documents this spec conforms to, in precedence order:**
1. `docs/mockups/manage/MANAGE_MOCKUP_8_SCREENS.png` — **the mockup wins over any text ruling**
   (`CAPTURE_LOOP_GOAL.md` §3).
2. The Manage UI asset sheets + the delivered individual assets.
3. `OWNER_RULINGS_LOCKED.md` — ⚠ **rulings 5 and 7 are SUPERSEDED by the mockup** (five chips, 10 tiles);
   **WO-1560** banners them.

---

## 1. ⛔ THE MEASUREMENT THAT DECIDES WHETHER THIS SESSION CAN CLOSE

> ## ⚠ SUPERSEDED 2026-09-10 — THIS SECTION'S MEASUREMENT IS STALE. THE BLOCKER IS CLOSED.
> The "5 of 26 resolve a portrait, 21 do not" reading below was true on 2026-09-06 and is **not true at
> HEAD**. Re-measured 2026-09-10: `MANAGE_PORTRAIT_COVERAGE_OK 68 Manage portrait key(s) resolve; 0
> dated art exemption(s) still genuinely absent` on `Builds/wave5-reg1` (`REGRESSION_OK 494/494 suites`),
> with **56** PNGs in `Assets/Resources/Portraits/Buildings/`. **Panel 2 is no longer BLOCKED-ON-ART.**
> Evidence and the full ledger:
> `WorkOrders/WORK_ORDER_1566_manage_conformance_spec_end_of_session_definition_of_done.RESULT.md` §1a.
> The body below is left intact as the dated record (CLAUDE.md §15) — **read the RESULT, not this.**


**Of the 26 structures the BUILD grid offers, 5 resolve a portrait. 21 do not.** They fall through to a
neutral hammer (`ManageScreenPanel.cs:3421-3425`) — the empty-ring tile the owner photographed.

**So panel 2 CANNOT match the mockup with the art currently in the tree, and no amount of layout work
will change that.** The ask is written: `docs/ART_ASK_2026-09-06_manage_conformance.md` (21 files,
1024×1024 RGBA, transparent, filenames = the catalog id **verbatim, underscores intact**).

⚠ **Until those land, §2 panel 2 is BLOCKED-ON-ART, not FAILED.** Record it that way. Do not invent
placeholder art, do not substitute a similar building's portrait, and do not quietly accept the hammer —
`CAPTURE_LOOP_GOAL.md` §3b: *"never a silent blank, never an invented fallback."*

---

## 2. PER-PANEL ACCEPTANCE

### 2.0 WHO CLOSES A PANEL - owner ruling 2026-09-07 (binding, supersedes any seat-read sign-off)

**Owner, 2026-09-07 01:10:** *"fix the board so those tickets dont say done and update the goal to be
screenshots proving these match"*
**Owner, 2026-09-07 01:12:** *"95% coverage in size font style context images"* ... *"thats the minimum
threshold to pass"*
**Owner, 2026-09-07 01:14:** *"i expect these images to fill the screen, not 60% of it"*

- **The acceptance for every Manage screen is a DEVICE SCREENSHOT placed beside its mockup panel and
  judged a match BY THE OWNER.** The tick-list below is how a lane decides it is ready to be looked at;
  it is not the verdict.
- **Headless captures and seat-read comparisons are EVIDENCE TOWARD that judgement and can NEVER mark a
  ticket done.** `949e848a0` declared all nine screens matched on exactly that evidence, after
  twenty-four rounds, and the owner's own walk that night matched none of them.
- **A Manage ticket may only move to DONE when the owner says the frame matches.** Until then its
  `**Status:**` reads `AWAITING OWNER MATCH` and the board buckets it **Verify**.
- **CRITERION ZERO, judged before every row below: DOES THE PANEL FILL THE SCREEN?** Full bleed inside
  the safe area, like the mockup. Not a 60%-width plate over the town. It multiplies every other axis -
  a correctly proportioned element inside a 64% plate is still the wrong size on the device.
- **THE FIVE AXES, 95% FLOOR:** the owner must judge the frame at least **95% matched** on **SIZE**,
  **FONT**, **STYLE**, **CONTEXT** (what is on screen and where) and **IMAGES** (the art present and its
  treatment). **Under 95% on any axis is a FAIL, whatever the headless capture says.**
- Current state, nine rows with one column per axis:
  `WorkOrders/ManageRedesign/CAPTURE_LOOP_GOAL.md` (the ruling block at the top). Full ruling:
  `WorkOrders/ManageRedesign/OWNER_RULINGS_LOCKED.md` ruling 29.

**How to use this:** put the fresh capture beside the mockup panel and tick each row only from what the
PNG proves. **Any unticked row = another pass.** No partial credit (owner, verbatim: *"If it's not, take
another pass"*).

### Chrome — applies to panels 2–8 alike
| # | Must be true | Owner |
|---|---|---|
| C1 | Back is a `<-` **arrow** at top-LEFT — not a `BACK` word-button | WO-1560's siblings |
| C2 | Title is **centred**, breadcrumb style (`MANAGE - BUILD`) | — |
| C3 | `QUEUE` is a **small pill at top-RIGHT with a red count badge** — not a tab, not a band, not a lane | — |
| C4 | **One** heading. No second section heading, no `Filter: ALL` sub-line, no hint sentence | — |
| C5 | No `HEART L<n>` chip (it is not in the mockup) — **only once the Heart keeps a door elsewhere** | WO-1430 |
| C6 | No element overlaps; nothing clipped mid-word; no text band under ~24 px | WO-1488 |
| C7 | Every tappable ≥ `ElarionUiKit.MinTouchPx` (**112**) | ⚠ last measured **110.4 px** on every `ManageTabs/ObsBtn_*`, `ManageQueueDoor`, `ManageFilters/ObsBtn_*` — **still red** |
| C8 | Meaning never carried by hue alone — **greyscale is the gate** | WO-1563 |

### Panel 1 — MANAGE (hub)
| # | Must be true |
|---|---|
| 1.1 | **Three large cards**: BUILD / ARMY / RESEARCH, each with its one-line description |
| 1.2 | `CLOSE` beneath them |
| 1.3 | Each card carries its **tab icon** art (§3) |
| 1.4 | ⛔ This screen **IS** the hub. WO-2001's "replace the hub with tabs" is superseded by the mockup — see **WO-1560** |

### Panel 2 — BUILD (grid) — ⛔ BLOCKED ON ART, see §1
| # | Must be true |
|---|---|
| 2.1 | **5 columns × 2 rows = 10 tiles visible** |
| 2.2 | **Five** filter chips: ALL / ECONOMY / DEFENSE / CRAFT / STORAGE. **No CIVIC** |
| 2.3 | **Every visible tile shows its building portrait** — not a ring, not a hammer ⛔ *needs §1* |
| 2.4 | Each tile shows its **name** and its **state in words** — WO-1563 |
| 2.5 | Selected tile carries a **gold border** |
| 2.6 | Locked tiles show a **padlock** and stay selectable |
| 2.7 | A filter that says ALL shows all, or states how many it holds back — no bare `12 MORE - SCROLL` as content (**WO-1491**) |

### Panel 3 — BUILDING DETAIL
| # | Must be true |
|---|---|
| 3.1 | **Large art LEFT** |
| 3.2 | Right: name, **Level N**, one-line purpose |
| 3.3 | A **before → after stats table** (e.g. `Production 120/hour → 180/hour`) with the after value visually distinct |
| 3.4 | Upgrade cost as **resource icons + numbers** |
| 3.5 | Upgrade **time** with a clock icon |
| 3.6 | **One** gold `UPGRADE` button |
| 3.7 | The purpose line is **specific to this building** — ⛔ today every tower shares one fallback sentence and the Catapult is called a tower (**WO-1565**) |

### Panel 4 — ARMY (grid)
| # | Must be true |
|---|---|
| 4.1 | **All 9 troops in one 3×3 grid, no scrolling** |
| 4.2 | Every troop portrait renders — ✅ all nine exist at `RpgUi/troop/` |
| 4.3 | Locked troops stay **visible and selectable** |
| 4.4 | Each tile shows its state in words — WO-1563 |

### Panel 5 — TROOP DETAIL
| # | Must be true |
|---|---|
| 5.1 | Large art left; name + **Level N** |
| 5.2 | One-line role description |
| 5.3 | Stats: **Health / Attack / Range / Speed**, each with its icon |
| 5.4 | Train cost + time |
| 5.5 | One gold **`TRAIN 1 <UNIT>`** button — the unit name is in the label |

### Panel 6 — TROOP LOCKED STATE
| # | Must be true |
|---|---|
| 6.1 | Same layout as panel 5 — **still selectable** |
| 6.2 | A **padlock** plus the requirement **in words** (`Requires Barracks Tier 4`) |
| 6.3 | The action button reads **`LOCKED`** and is visibly disabled |
| 6.4 | ⛔ The requirement must be **true**: troop unlock reads the barracks **building tier** (ruling 21) |

### Panel 7 — RESEARCH (school picker) → TREE
| # | Must be true |
|---|---|
| 7.1 | School **cards first**, then the tree — never a flat 17-perk list |
| 7.2 | ⛔ **No orphaned school and no dead well.** Today it is authored `4 × 1` for **five** schools, leaving one alone on row 2 beside three empty cells and ~60% of the panel black (**WO-1564**) |
| 7.3 | Tree rows show icon, name, one-line effect, and state on the right |
| 7.4 | States read in words: `Researched` / `RESEARCH` / `Requires <X>` |
| 7.5 | An affordable `RESEARCH` row shows its **cost with resource icons** |

### Panel 8 — QUEUE (overlay)
| # | Must be true |
|---|---|
| 8.1 | Tabs **BUILDERS / TRAINING / RESEARCH**, each with `(n/n)` |
| 8.2 | **Numbered** rows |
| 8.3 | Active row has a **progress bar + remaining time**; the rest read `Queued` |
| 8.4 | `SPEED UP` carries its **crystal price** |
| 8.5 | An `X` closes the overlay |
| 8.6 | ⛔ Rows name the structure **in words** — today they print `Tower Ground Archer -> L2` from the VM (**WO-1564**) |

---

## 3. ASSET BINDING — which delivered file renders on which element

> ## ⚠ SUPERSEDED 2026-09-10 — THE STATUS COLUMN BELOW IS STALE IN BOTH DIRECTIONS. ALL 12 ROWS RESOLVE.
> Every row's asset was listed on disk under `Assets/Resources/` and matched to its key in
> `Assets/_Modules/Core/Manage/ManageArt.cs` on 2026-09-10: **12/12 present and wired.** The rows marked
> `⛔ delivered, NOT imported` (state badges, tile frames) are imported at `RpgUi/manage/`; every row
> marked `❌ not in repo` — tab icons, filter icons, resource icons, stat icons, research school icons,
> `icon-back`/`icon-close`/`icon-time`, `progress-track`/`progress-fill` — is present under
> `Assets/Resources/UI/ElarionMedieval/Manage/`. C1 no longer renders the literal `<-`.
> ⚠ **The FILENAME TRAP warning below still holds and is still live** — `ManageArt.BuildingPortraitKey`
> (`ManageArt.cs:328`) keeps its *"DELIBERATELY DOES NOT SLUG THE ID"* note (`:306`). Do not relax it.
> ⚠ **Still genuinely owed (NOT a row below):** the three hub card illustrations `hub-build.png`,
> `hub-army.png`, `hub-research.png` — absent, so `LoadHubArt` (`ManageArt.cs:184`) paints the stand-ins
> at `ManageArt.cs:166-170` by design. Full ledger:
> `WorkOrders/WORK_ORDER_1566_manage_conformance_spec_end_of_session_definition_of_done.RESULT.md` §4.
> Body left intact as the dated record (CLAUDE.md §15) — **read the RESULT, not this.**


**This table is the missing link.** Until every row resolves, a panel cannot match no matter how the
layout is built.

| Element | Asset | Where it must resolve | Status (measured 2026-09-06) |
|---|---|---|---|
| Building tile + detail art | `Portraits/Buildings/<catalog id>.png` (+`-2..-6` per tier) | `ManageArt.BuildingPortraitKey`, `ManageArt.cs:158-161` | ⚠ **5 of 26.** 21 missing — §1 |
| Troop tile + detail art | `RpgUi/troop/troop-<id>.png` | the VM's troop folder | ✅ all 9 present |
| Heart portrait | `Portraits/Buildings/heart.png` | Heart surface | ✅ present |
| State badges (5) | `status-available/locked/inprogress/max/queue` | `ManageArt.cs:74-78`, `StatusFor` `:112-120` | ⛔ **delivered, NOT imported** |
| Tile frames (4) | `frame-tile / selected / locked / max` | tile frame layer | ⛔ **delivered, NOT imported** — and ⚠ **not interchangeable**: two are opaque-centred, two hollow |
| Tab icons (4) | `tab-build / army / research / queue` | panel 1 cards + the queue pill | ❌ not in repo |
| Filter icons (5) | `filter-all / economy / defense / craft / storage` | panel 2 chips | ❌ not in repo |
| Resource icons (5) | `res-wood / stone / iron / crystal / gold` | cost rows, panels 3 and 7 | ❌ not in repo |
| Stat icons (4) | `stat-health / attack / range / speed` | panel 5 | ❌ not in repo |
| Research school icons (4) | `research-arcane / defense / weapons / army` | panel 7 | ❌ not in repo |
| Back / close / time | `icon-back` (**an arrow**), `icon-close`, `icon-time` | chrome C1, panels 3/5/8 | ❌ not in repo — C1 currently renders the literal text `<-` (**WO-1491**) |
| Progress bar | `progress-track`, `progress-fill` | panel 8 | ❌ not in repo |

> ## ⛔ THE FILENAME TRAP — THE SINGLE MOST LIKELY WAY THIS SESSION SILENTLY FAILS
> `ManageArt.BuildingPortraitKey` uses the catalog id **VERBATIM**; its own comment states it
> *"DELIBERATELY DOES NOT SLUG THE ID"*. So the file must be **`tower_ground_archer.png`** — underscores
> intact.
> **The three delivered asset sheets use three DIFFERENT schemes and none of them match:**
> `building_archertower.png` · `bld_archertower.png` · `building-archer-tower.png`.
> **A file dropped in under a sheet name resolves NOTHING and fails silently to the neutral hammer.**
> Rename to the catalog id on import. `ManagePortraitCoverageRegression` is what catches it.

---

## 4. THE PROOF — what "done" means, mechanically

1. `COMPILE_GATE_OK` + `REGRESSION_OK <n>/<n>` on **fresh** logs, judged by the **marker**, never the exit
   code.
2. `MANAGE_FLOW_MAP_OK` / `MANAGE_OPERATIONAL_CAPTURE_OK`, with **no** `CAPTURE_LEDGER_MISSING` and **no**
   `CAPTURE_LEDGER_DUPLICATE`.
3. `ManagePortraitCoverageRegression` green — it fails **by name** on any id with no portrait.
4. ⛔ **Open the PNGs and look.** A marker proves frames were written, never that they look right.
5. Every §2 row ticked from a frame, or recorded as **BLOCKED-ON-ART** with the missing file named.

⛔ **NO FRAME IN THE REPO POSTDATES THE CODE.** Newest capture **18:39**; commit `949e848a0` **18:51**;
`ManageScreenVM.cs` edits **20:47**. **Every comparison this session must use a fresh capture** — judging
against the 18:39 set is judging a build that has moved on.

---

## 5. WHAT THIS SPEC DOES NOT COVER — deliberately

- **The MOVE / MANAGE-PLACED door** (ruling 25). Real, and **none of the eight panels shows it**, so it is
  out of scope until the loop closes — `CAPTURE_LOOP_GOAL.md` §7 already parks it. Do not fold it in.
- **The raid flow.** WO-1541/1542/1543 and WO-1561/1562.
- **Board/record repair.** **WO-1560** — do it first; it is documentation and it stops three false greens
  from certifying a superseded design.

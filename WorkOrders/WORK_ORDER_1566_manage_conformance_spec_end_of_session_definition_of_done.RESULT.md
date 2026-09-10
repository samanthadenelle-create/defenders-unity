# WO-1566 RESULT — the Manage conformance yardstick, AUDITED against HEAD 2026-09-10

**Lane:** MANAGE-AUDIT (read-only). No code, no Unity run, no commit.
**Worktree HEAD:** `ff42319de6b0a07a59269d74472f3dcc646b6487` (after `git merge --ff-only refs/heads/dev`).
**Audited:** 2026-09-10, ~08:00.
**⚠ CORRECTED 2026-09-10 after commit `2039e2c41`** — row 6.4 was published as NOT MEASURABLE on a grep
scoped to a single file; widened, **ruling 21 IS pinned and green** (`TROOP_REACHABILITY_OK`). Row 6.4 is
now PASS-O, the tally moves 50→**51 pass** / 9→**8 NM**, and the WO-1649 that was to be minted against it
is **WITHDRAWN, not minted**. The error was the auditor's, not the code's.

---

## 0. WHAT THIS IS, AND THE STATE IT FLIPS THE WO TO

⚠ **THE YARDSTICK DEFINES NO STATE FOR "AUDITED", SO ONE IS BEING SUPPLIED AND NAMED AS SUCH.**
§2.0 defines `AWAITING OWNER MATCH` — but that is the state a **Manage PANEL ticket** carries, not the
state of the spec that measures them. WO-1566 changes no code and can never be "DONE" by an owner frame
match, so per the lane brief its `**Status:**` reads:

> `AUDITED 2026-09-10 - 51 pass / 4 fail / 11 unmeasured / 1 blocked-on-art / 1 superseded (68 rows), see RESULT`

⛔ **NOTHING IN THIS FILE MARKS A PANEL DONE.** §2.0 is binding: a device screenshot judged by the owner
at ≥95% on SIZE / FONT / STYLE / CONTEXT / IMAGES is the only verdict. Every `PASS` below is
**evidence toward** that judgement — a green source oracle or a measured pixel — and never the judgement.

---

## 1. THE THREE THINGS THAT CHANGED SINCE THE SPEC WAS WRITTEN (2026-09-06)

### 1a. ⛔ §1's BLOCKER IS CLOSED. The spec's headline measurement is STALE.

WO-1566 §1: *"Of the 26 structures the BUILD grid offers, 5 resolve a portrait. 21 do not... panel 2
CANNOT match the mockup with the art currently in the tree."*

**Re-measured at HEAD, 2026-09-10:**

| Evidence | Reading |
|---|---|
| `Builds/wave5-reg1` (07:25, fresh) | `MANAGE_PORTRAIT_COVERAGE_OK 68 Manage portrait key(s) resolve; 0 dated art exemption(s) still genuinely absent.` |
| same log, same suite | `building tier keys checked=26 across 6 ladder(s)` · `build tile ids=24 (0 on the dated art exemption list)` · `troop keys checked=9 under 'RpgUi/troop/'` · `chrome keys checked=9` |
| `ls Assets/Resources/Portraits/Buildings/*.png` | **56 files** (was 5) |

**Panel 2 is no longer BLOCKED-ON-ART.** §1 of the spec should be banner-superseded rather than read.

### 1b. ⛔ §3's ASSET BINDING TABLE IS STALE IN THE OPPOSITE DIRECTION — every row now resolves.

Every one of the twelve §3 rows was checked on disk under `Assets/Resources/` and against its key in
`Assets/_Modules/Core/Manage/ManageArt.cs`. **12/12 present and wired** (detail in §4 below). The rows
the spec marks `❌ not in repo` — tab icons, filter icons, resource icons, stat icons, research school
icons, `icon-back`/`icon-close`/`icon-time`, `progress-track`/`progress-fill` — are all present under
`Assets/Resources/UI/ElarionMedieval/Manage/`.

### 1c. The five caveat tickets §2 names are CLOSED on owner felt-test.

| WO | `**Status:**` at HEAD |
|---|---|
| 1488 (C6 queue overlap) | `CLOSED 2026-09-07 - owner felt-test PASS (build 2026.09.07.359076)` |
| 1491 (C1 `<-` literal, `12 MORE - SCROLL`) | `CLOSED 2026-09-07 - owner felt-test PASS` |
| 1563 (2.4 / 4.4 state words, C8 greyscale) | `CLOSED 2026-09-07 - owner felt-test PASS` |
| 1564 (7.2 orphaned school, 8.6 raw ids) | `CLOSED 2026-09-07 - owner felt-test PASS` |
| 1565 (3.7 shared tower description) | `CLOSED 2026-09-07 - owner felt-test PASS` |
| 1430 (C5 Heart door) | `FIXED 2026-09-10 - fields 3-5 DROPPED on the owner's ruling` |
| 1560 (board/canon repair) | `IMPLEMENTED 2026-09-06 (documentation only)` |

---

## 2. ⛔ THE EVIDENCE GAP — STATE IT ONCE, IT EXPLAINS EVERY "NOT MEASURABLE" BELOW

**There is exactly ONE fresh Manage frame in the repo, and it is panel 1.**

- `Builds/ui-capture/ManageWorkspace_{1920x1080,2340x1080,2670x1200}.png` — mtime **2026-09-10 07:57**.
  All three are the **hub** (mockup panel 1). Opened and read in this audit.
- ⛔ **`Builds/ui-capture/ManageFlow_*.png` — ZERO files.** The eight-panel frame set is written by
  `DeNelle.Editor.UICaptureLaunch.RunManageFlowMapCaptureHeadless`
  (`Assets/Editor/UICaptureLaunch.cs:8690`), whose frames are named
  `"ManageFlow_" + TabWordOf(tab) + "_" + stateWord` (`:~8770`). **That entry point was not run this
  session**, and the method sweeps its own output first, so nothing older survives either.
  → **panels 2–8 have NO frame evidence of any age.**
- `Builds/wave5-capture5` **does not exist**. The capture logs present at 07:4x are `wave5-capture4`
  (`UI_CAPTURE_OK 97`, `RunCaptureHeadless`) and `wave5-navcapture4`. Neither emits
  `MANAGE_FLOW_MAP_OK` — grepped, NUL-stripped, absent from all five of today's capture logs.
- The one Manage **device** frame, `Builds/device-frames/2026-09-10_0033_manage_363195.png`, is
  **PORTRAIT** and from build **363195** (00:27). The owner ruled **LANDSCAPE ONLY** on the morning of
  2026-09-10, and two later device builds exist (363529, 363591). It is **not a valid judge** and is
  recorded in §6 as out-of-scope rather than used as a verdict.

**Gate markers on fresh logs (marker, not exit code):**

| Marker | Log | Reading |
|---|---|---|
| `COMPILE_GATE_OK` | `Builds/wave5-compile4` (07:51) | present |
| `REGRESSION_OK 494/494 suites` | `Builds/wave5-reg1` (07:25) | present, `494 green, 0 red, 0 skipped` |
| `UI_CAPTURE_OK 97` | `Builds/wave5-capture4` (07:43) | present |
| `MANAGE_FLOW_MAP_OK` | — | **ABSENT on every log today** |
| `MANAGE_OPERATIONAL_CAPTURE_OK` | — | **ABSENT on every log today** |
| `CAPTURE_LEDGER_MISSING` / `_DUPLICATE` | — | absent (but so is the run that would emit them) |
| `Builds/wave5-reg2` (07:55) | — | **carries NO `REGRESSION_` marker and its runner.txt has no VERDICT line** — an incomplete run. `wave5-reg1` is the last complete regression. |

---

## 3. THE LEDGER — §2 PER-PANEL ACCEPTANCE

Verdict key: **PASS-O** = green named case in a source oracle on a fresh log (evidence, not the owner's
verdict). **PASS-F** = measured off a fresh frame. **FAIL** = measured contrary evidence.
**NM** = not measurable from here. **ART** = blocked on undelivered art.

### Chrome (panels 2–8, plus what panel 1's frame can show)

| # | Verdict | Evidence |
|---|---|---|
| C1 back is a `<-` **arrow** | PASS-O | `[chrome-back-glyph]` pins `ApplyBackGlyph(_workspaceBack)` **and** `ManageArt.LoadSprite(ManageArt.IconBack)` (`ManageMockupConformanceRegression.cs` CheckChrome); `ManageArt.IconBack` = `UI/ElarionMedieval/Manage/icon-back` (`ManageArt.cs:112`); the PNG exists on disk. WO-1491 CLOSED. Hub frame carries no back (it is the root), so no frame check. |
| C2 centred breadcrumb title | PASS-O + PASS-F | `[chrome-title-spelling]` pins `HeaderJoiner = " - "` and bans a `"MANAGE / "` literal. Frame: `MANAGE` centred, `ManageWorkspace_1920x1080.png`. |
| C3 QUEUE = small pill top-RIGHT with a red count badge | PASS-F (pill) / NM (badge) | Frame: pill at top-right beside the X, all three aspects. `[hub-queue-badge]` pins the badge and its count. **The badge is not visible in any frame because the capture's queue is empty** — no frame with a non-zero queue exists. |
| C4 ONE heading | PASS-O | `MANAGE_ONE_HEADING_OK ... host binds the model's breadcrumb, the renderer paints no copy, the selection band collapses when empty` (wave5-reg1). |
| C5 no `HEART L<n>` chip | PASS-O + PASS-F | Row is **superseded by ruling**: the chip is a VERB, not a level badge. `[hub-heart-chip-verb]` green. Frame reads `UPGRADE HEART / 250 Crystals` — no level badge. |
| C6 no overlap / no clipping / no band < 24 px | PASS-F (landscape) | Measured on all three landscape frames at 07:57: title, QUEUE pill and X disjoint; no truncated label. ⚠ The **portrait** device frame shows the pill drawn ACROSS the `MANAGE` title and `UPGRADE ...` truncated — out of scope, see §6. |
| C7 every tappable ≥ `MinTouchPx` (112) | **NM** | `ManageExitPx = ElarionUiKit.MinTouchPx` (`ManageScreenPanel.cs:138`) → the constant exit is at the floor **by construction**; `[queue-controls-clear-the-touch-floor]` green. ⛔ **The spec's "last measured 110.4 px on every `ManageTabs/ObsBtn_*`, `ManageQueueDoor`, `ManageFilters/ObsBtn_*`" cannot be reproduced at HEAD** — those widget names appear NOWHERE in `wave5-reg1`, and `UI_TOUCH_ORACLE_OK 12/12` measures synthetic fixtures, not the Manage tree. **The 110.4 figure is unsourced at HEAD.** |
| C8 greyscale is the gate | PASS-O | `[tile-closed-state-word]` pins `StateWord` over `StateText` end-to-end (contract → projection → `BuildTile`); `MANAGE_ROW_BENEFIT_OK`; `MANAGE_RESEARCH_CARD_OK ... carries all four state words`. WO-1563 CLOSED. |

### Panel 1 — MANAGE (hub) — the ONE panel with a fresh frame

| # | Verdict | Evidence |
|---|---|---|
| 1.1 three large cards + one-line description | PASS-F | `ManageWorkspace_*.png` 07:57: BUILD / **BUILD BARRACKS** / RESEARCH, each with its sentence. `[hub-three-cards]`, `[hub-cards-fill-the-band]` green. **ARMY copy = "BUILD BARRACKS" per the owner's ruling (WO-1636/1406) — landed and visible in the frame.** |
| 1.2 `CLOSE` beneath them | **FAIL (measured)** | See §5, finding F1. The control is built and its label is set to `ElarionUi.Parchment` with a gold perimeter at `ManageScreenPanel.cs:1362-1375`, but **the brightest pixel anywhere in the bottom 22% of all three 07:57 frames is 30/255**, and inside the CLOSE plate itself `max = 12/255` — against **172/255** for the `BUILD` card label in the same frame. It renders as a black hole where a live control is claimed. |
| 1.3 each card carries its **tab icon** art | **ART** | The frame's card art is the **stand-in set**, not the owed illustration: `ManageArt.HubArtStandIns` = `Portraits/Buildings/lumbermill`, `.../barracks`, `.../arcane-tower` (`ManageArt.cs:166-170`) — exactly the three buildings visible. `find Assets/Resources -iname 'hub-*.png'` returns **nothing**, so `HubArtBuild/Army/Research` (`:141-143`) miss and `LoadHubArt` (`:184`) falls to the stand-in **by design**. `[hub-art-standins-exist]` + `[hub-art-key-order]` green. **Three illustrations owed; the moment they land they win with no code change.** |
| 1.4 this screen IS the hub | PASS-O | `MANAGE_NAV_OK ... Manage opens on a tab rather than a chooser, and the four-tile launcher can no longer be shown`. WO-1560 landed. |

### Panel 2 — BUILD (grid)

| # | Verdict | Evidence |
|---|---|---|
| 2.1 5×2 = 10 tiles visible | NM | No frame. `[grid-tiles-fill-the-band]` derives a `columns × rows` cell but the resolved numbers are not printed on the log. |
| 2.2 five chips ALL/ECONOMY/DEFENSE/CRAFT/STORAGE, no CIVIC | NM | No frame; no oracle case names the chip set. `ECONOMY/CRAFT/STORAGE` appear in `ManageScreenVM.cs:3428/4486/4510` only as collection-routing prose. `filter-{all,economy,defense,craft,storage}.png` all exist on disk (5/5, no `filter-civic`). |
| 2.3 every visible tile shows its portrait | **PASS-O** | `MANAGE_PORTRAIT_COVERAGE_OK 68 keys resolve; 0 exemptions genuinely absent` + 56 PNGs on disk. **§1's blocker is closed.** |
| 2.4 name + state in words | PASS-O | `[tile-closed-state-word]`; WO-1563 CLOSED. |
| 2.5 selected tile carries a gold border | NM | `frame-selected.png` present at `RpgUi/manage/` and wired (`ManageArt.cs:69`); no frame shows a selection. |
| 2.6 locked tiles show a padlock and stay selectable | PASS-O + PASS-F | `[tile-locked-dim]`, `[detail-requirement-row]` (padlock pinned). Panel-1 frame shows the padlock on the locked ARMY card. |
| 2.7 no bare `12 MORE - SCROLL` | PASS-O | `[manage-progressive-disclosure] ... pages nowhere, and its BUILD grid shows only unlocked rows`. WO-1491 CLOSED. |

### Panel 3 — BUILDING DETAIL

| # | Verdict | Evidence (all `ManageMockupConformanceRegression` CheckDetail, green inside `MANAGE_MOCKUP_OK 10 cases`) |
|---|---|---|
| 3.1 large art LEFT | PASS-O | `[detail-square-art]`, `[detail-art-crops-the-ring]` (art spans the card's full height, not pinned square). |
| 3.2 name, Level N, purpose | PASS-O | `[detail-no-dot-joiner]` (facts are not welded with a dot joiner). |
| 3.3 before → after stats, after distinct | PASS-O | `[detail-next-by-weight]` — the promoted value is set BOLD. |
| 3.4 cost as resource icons + numbers | PASS-O | `[detail-cost-names-the-resource]` pins `BuildCostRow` drawing the cost's `Label`; `res-*.png` 5/5 on disk. |
| 3.5 upgrade time with a clock icon | PASS-O | `[detail-clock-on-its-own]` pins `TimeText`; `icon-time.png` present. |
| 3.6 ONE gold `UPGRADE` button | PASS-O | `[detail-cta-verb-only]` — the CTA face may not weld the cost onto the label. |
| 3.7 the purpose line is specific to this building | PASS-O | WO-1565 **CLOSED 2026-09-07, owner felt-test PASS**. |

### Panel 4 — ARMY (grid)

| # | Verdict | Evidence |
|---|---|---|
| 4.1 all 9 troops, 3×3, no scrolling | NM | No frame. |
| 4.2 every troop portrait renders | PASS-O | `troop keys checked=9 under 'RpgUi/troop/'`, inside `MANAGE_PORTRAIT_COVERAGE_OK`. |
| 4.3 locked troops visible and selectable | PASS-O | `[tile-locked-dim]` — Locked is DIMMED, not hidden. |
| 4.4 state in words | PASS-O | `[tile-closed-state-word]`. |

### Panel 5 — TROOP DETAIL

| # | Verdict | Evidence |
|---|---|---|
| 5.1 large art left, name + Level N | PASS-O | `[detail-square-art]`. |
| 5.2 one-line role description | NM | No oracle case, no frame. |
| 5.3 Health / Attack / Range / Speed, each with its icon | PASS (binding) / NM (render) | `stat-health/attack/range/speed.png` 4/4 on disk under `UI/ElarionMedieval/Manage/`, keyed off `ManageArt.UiFolder` (`ManageArt.cs:80`). No frame proves they paint. |
| 5.4 train cost + time | PASS-O | `[detail-cost-names-the-resource]` + `[detail-clock-on-its-own]`. |
| 5.5 one gold `TRAIN 1 <UNIT>` naming the unit | PASS-O | `[detail-cta-verb-only]` — *"the train face no longer names what it trains"* is the RED text; the case is green, so it does. |

### Panel 6 — TROOP LOCKED STATE

| # | Verdict | Evidence |
|---|---|---|
| 6.1 same layout as panel 5, still selectable | NM | No frame. |
| 6.2 padlock + requirement in words | PASS-O | `[detail-requirement-row]` pins the padlock on the requirement line; `[research-tree-two-rows]` pins `RequirementText` + `LockReason` through the projection. |
| 6.3 button reads `LOCKED`, visibly disabled | NM | No frame; no oracle case names the label. |
| 6.4 the requirement is TRUE (reads the barracks **building tier**, ruling 21) | **PASS-O** | ⚠ **CORRECTED 2026-09-10 after the first pass of this audit called it NM — that call was WRONG and the error was mine.** I grepped only `ManageTroopsTrainDoorRegression.cs` and reported the absence as a repo-wide absence. Widened, the pin is there and green: `TROOP_REACHABILITY_OK every authored troop unlocks at or below the barracks ladder ceiling (6) **and the barracks BUILDING tier is the gate that opens them**` (`Builds/wave5-reg1:10286`, registered `DataRegression.cs:1127`). The suite asks through the code path — `BarracksProgression.EffectiveBarracksLevelOf` (`Assets/_Modules/Village/Troops/BarracksProgression.cs:125`), whose doc comment quotes the ruling verbatim (`:104-105`: *"owner ruling 21, 2026-09-06: 'Merge them - the building tier gates troops.'"*) — and carries its own RED recipe at `TroopReachabilityRegression.cs:141`. The live read reaches it via `TroopUnlock.cs:30` → `BarracksService.BarracksLevel` (`BarracksService.cs:81`). |

### Panel 7 — RESEARCH

| # | Verdict | Evidence |
|---|---|---|
| 7.1 school cards first, then the tree | PASS-O | `CheckResearchPicker` / `[research-picker-one-row]` (`ApplyPickerCapacity` lays the schools in ONE row). |
| 7.2 no orphaned school, no dead well | PASS-O | `[research-picker-dead-band]` — `MANAGE_GRID`'s `fillH` is divided out of `rowsPx` and the grid must NAME its unused band. WO-1564 **CLOSED, owner PASS**. |
| 7.3 tree rows: icon, name, one-line effect, state right | PASS-O | `MANAGE_RESEARCH_CARD_OK ... one choice per PERK across the whole tree ... loads its art from HudIcons/BuildingUpgrades`; `[research-tree-painting]`, `[research-tree-rows-take-the-band]`. |
| 7.4 states in words (`Researched` / `RESEARCH` / `Requires <X>`) | PASS-O | `MANAGE_RESEARCH_CARD_OK ... carries all four state words with the CanResearch reason verbatim`. |
| 7.5 affordable `RESEARCH` row shows cost with resource icons | PASS-O (cost) / NM (icons) | `MANAGE_RESEARCH_CARD_OK ... prices in gold parts`. `res-*.png` present; no frame proves the icons paint. |

### Panel 8 — QUEUE (overlay)

| # | Verdict | Evidence — all from `MANAGE_QUEUE_PANEL8_OK` (wave5-reg1) unless noted |
|---|---|---|
| 8.1 tabs BUILDERS / TRAINING / RESEARCH with `(n/n)` | PASS-O | *"three channel tabs counted from the live slot state"*. |
| 8.2 numbered rows | PASS-O | *"numbered rows"*. |
| 8.3 active row: progress bar + remaining time | PASS-O | `[queue-rows-visible]` derives the row height and the whole-row count; `progress-track.png` + `progress-fill.png` on disk. |
| 8.4 `SPEED UP` carries its crystal price | PASS-O | *"a SPEED UP verb that is the EXISTING TryInstantFinish path priced by InstantFinishPrice"*. |
| 8.5 an `X` closes the overlay | PASS-O + PASS-F | `CheckConstantExit` pins the top-right X on EVERY screen incl. the overlay (`[manage-exit-constant]`, `[manage-exit-clears-queue]`); the X is visible in the hub frame. |
| 8.6 rows name the structure in words | PASS-O | `ManageTroopsTrainDoorRegression.CheckQueueRowsNameThingsInWords` (`:964`) inside `MANAGE_TRAIN_DOOR_OK`; `[queue-row-thumbnail]`. WO-1564 CLOSED. |

---

## 4. THE LEDGER — §3 ASSET BINDING (12/12 PASS — the spec's table is fully stale)

Every path below was listed on disk under `Assets/Resources/` on 2026-09-10 and matched to its key in
`ManageArt.cs`.

| Element | Verdict | Evidence |
|---|---|---|
| Building tile + detail art | PASS | 56 PNGs in `Portraits/Buildings/`; `MANAGE_PORTRAIT_COVERAGE_OK` 68 keys. |
| Troop tile + detail art | PASS | `troop keys checked=9 under 'RpgUi/troop/'`. |
| Heart portrait | PASS | `Portraits/Buildings/heart.png`. |
| State badges (5) | **PASS — was `⛔ delivered, NOT imported`** | `RpgUi/manage/status-{available,locked,inprogress,queue,max}.png` all present; keys `ManageArt.cs:200-204`. |
| Tile frames (4) | **PASS — was `⛔ delivered, NOT imported`** | `RpgUi/manage/frame-{tile,selected,locked,max}.png`; keys `ManageArt.cs:68-71`. |
| Tab icons (4) | **PASS — was `❌ not in repo`** | `UI/ElarionMedieval/Manage/tab-{build,army,research,queue}.png`. |
| Filter icons (5) | **PASS — was `❌`** | `UI/ElarionMedieval/Manage/filter-{all,economy,defense,craft,storage}.png` (no `filter-civic`, correct). |
| Resource icons (5) | **PASS — was `❌`** | `.../res-{wood,stone,iron,crystal,gold}.png`; `ManageArt.ResWood` `:88`. |
| Stat icons (4) | **PASS — was `❌`** | `.../stat-{health,attack,range,speed}.png`. |
| Research school icons (4) | **PASS — was `❌`** | `.../research-{arcane,defense,weapons,army}.png`. |
| Back / close / time | **PASS — was `❌`** | `.../icon-{back,close,time}.png`; `ManageArt.IconBack` `:112`; C1 no longer renders a literal `<-`. |
| Progress bar | **PASS — was `❌`** | `.../progress-track.png`, `.../progress-fill.png`. |

**The filename trap (§3's warning) holds and is defended:** `ManageArt.BuildingPortraitKey`
(`ManageArt.cs:328`) still carries its *"DELIBERATELY DOES NOT SLUG THE ID"* note (`:306`), and
`ManagePortraitCoverageRegression` reports `art exemptions still genuinely absent=0/0` — no id is
falling through a scheme mismatch.

**Still owed (NOT a §3 row, but §2 1.3 depends on it):** `UI/ElarionMedieval/hub-build.png`,
`hub-army.png`, `hub-research.png` — the three hub card illustrations. `find` returns nothing.

---

## 5. THE FAIL ROWS — four, with the lane each names as owner

### F1 — §2 1.2 · the hub `CLOSE` renders at 12/255 while the source claims it is "live and legible"
**Lane named by the row:** the Manage chrome lane (the WO-1597 surface).
**Measured, this session, three fresh frames (07:57):**

| Frame | brightest pixel in the bottom 22% | inside the CLOSE plate | `BUILD` card label, same frame |
|---|---|---|---|
| `ManageWorkspace_1920x1080.png` | 30/255 | max 12/255, mean 0.8/255 | 172/255 |
| `ManageWorkspace_2340x1080.png` | 30/255 | — | — |
| `ManageWorkspace_2670x1200.png` | 30/255 | max 12/255 | 172/255 |

**Why this is a real finding and not a re-litigation:** `ManageScreenPanel.cs:1362-1375` sets
`interactable = true`, the label to `ElarionUi.Parchment`, adds `ElarionUiKit.GoldPerimeter`, and emits
`FlowTrace.Step("Manage", "MANAGE_HUB_CLOSE the shared CLOSE is live and legible ... it was never
disabled, only unreadable")`. `ManageMockupConformanceRegression` `[chrome-close-is-live]` pins that
**source text** and is green. **A source lint cannot see luminance.** WO-1597 is `CLOSED 2026-09-08 -
owner felt-test PASS (build 2026.09.08.361259)`; the 2026-09-10 07:57 frames show the control dark
again. This needs a lane and, per §2.0, an owner frame — it is not covered by an open ticket (see §7).

⚠ **WHAT WOULD SETTLE IT, NAMED RATHER THAN ASSUMED.** WO-1597 closed on a **DEVICE** felt-test (build
2026.09.08.361259); this 12/255 is from a **HEADLESS batchmode** capture. Both can be true if batchmode
renders that control differently, and **no device frame of the hub exists at or after build 363591**. The
discriminating check is one landscape device screencap of the Manage hub on a build >= 363591, measured
the same way (brightest pixel inside the CLOSE plate vs the BUILD card label). Until that is taken, this
is proven **for the headless capture** and unproven for the device — it is not asserted as a device
regression.

### F2 — §4 proof 2 · `MANAGE_FLOW_MAP_OK` / `MANAGE_OPERATIONAL_CAPTURE_OK` were never emitted today
**Lane:** the capture lane. `RunManageFlowMapCaptureHeadless` (`Assets/Editor/UICaptureLaunch.cs:8690`)
did not run. Grepped (NUL-stripped) across `wave5-capture1/2/4`, `wave5-navcapture1/4`: neither marker
appears. Per §8 of CLAUDE.md, **marker absence on a fresh log is a FAILURE, not an unknown.**

### F3 — §4 proof 4 · "open the PNGs and look" is impossible for seven of the eight panels
**Lane:** the capture lane (same run as F2). `Builds/ui-capture/ManageFlow_*.png` = **0 files**. Panel 1
was opened and read in this audit at all three landscape aspects; panels 2-8 have no picture of any age.

### F4 — §4 proof 5 · 21 rows cannot be ticked from a frame
**Lane:** the capture lane (same run as F2/F3). §4 row 5 requires *"every §2 row ticked from a frame, or
recorded as BLOCKED-ON-ART with the missing file named."* **36 of the 50 §2 rows are ticked from a source
oracle or an asset on disk, not from a frame** — legitimate evidence under §2.0, but explicitly not what
this proof row asks for. Only panel 1's four rows are frame-ticked.

---

## 5b. THE LEDGER — §4 "THE PROOF", five rows

| # | Must be true | Verdict | Evidence |
|---|---|---|---|
| 1 | `COMPILE_GATE_OK` + `REGRESSION_OK <n>/<n>` on FRESH logs, judged by the marker | **PASS** | `COMPILE_GATE_OK` on `Builds/wave5-compile4` (07:51); `REGRESSION_OK 494/494 suites -- 494 green, 0 red, 0 skipped` on `Builds/wave5-reg1` (07:25). |
| 2 | `MANAGE_FLOW_MAP_OK` / `MANAGE_OPERATIONAL_CAPTURE_OK`, no ledger findings | **FAIL (F2)** | Neither marker on any of today's five capture logs. |
| 3 | `ManagePortraitCoverageRegression` green | **PASS** | `MANAGE_PORTRAIT_COVERAGE_OK 68 Manage portrait key(s) resolve; 0 dated art exemption(s) still genuinely absent.` |
| 4 | Open the PNGs and look | **FAIL (F3)** | Panel 1 opened at 3 aspects; no PNG exists for panels 2-8. |
| 5 | Every §2 row ticked from a frame, or recorded BLOCKED-ON-ART with the file named | **FAIL (F4)** | 4 rows frame-ticked, 36 oracle/asset-ticked, 1 recorded BLOCKED-ON-ART with the three files named (1.3). |

---

## 5c. RECORDED, NOT COUNTED — `wave5-reg2` produced no marker

`Builds/wave5-reg2` (07:55, 1,169,122 bytes) contains no `REGRESSION_OK` / `REGRESSION_FAIL` line and its
`.runner.txt` (380 bytes) stops after `[run] log=...` with **no VERDICT line** — an incomplete or killed
run. It is **not counted as a §4 row failure**: proof 1 is satisfied by `wave5-reg1` + `wave5-compile4`.
It is recorded because a marker-less run sitting on disk between two green gates is exactly the shape
CLAUDE.md §8 warns reads as a pass. Ticket subject in §7.

---

## 6. RECORDED, NOT COUNTED — the portrait device frame

`Builds/device-frames/2026-09-10_0033_manage_363195.png` (build 363195, 00:27) shows, in **portrait**:
the QUEUE pill drawn ACROSS the `MANAGE` title; `UPGRADE ...` truncated mid-word; the card art wells
~4× taller than their art with a large empty band; and the pre-ruling `ARMY` copy. **None of it is
counted against a row**: the owner ruled **LANDSCAPE ONLY** on the morning of 2026-09-10, two newer
device builds exist (363529, 363591), and the 07:57 landscape frames show none of these defects. If
portrait is ever un-ruled, this frame is the starting evidence.

**Owner rulings applied as RULED, never as FAIL:**
- Manage ARMY copy = **"BUILD BARRACKS"** (WO-1636 / WO-1406) — visible in the 07:57 frame; row 1.1 PASS.
- Manage **Placed art strip is intended** (ruled 2026-09-10) — §5 of the spec parks the MOVE/MANAGE-PLACED
  door out of scope; `PLACED_DOOR_OK` is green in wave5-reg1. Not audited, correctly.
- **WO-1574 medallions are final** — the status medallion set is §3-complete and not re-opened here.

---

## 7. FAIL-ROW TICKET COVERAGE — what is open, and what must be minted (NOT minted here)

`grep`ped `WorkOrders/` for each subject.

| Fail | Covered by an OPEN ticket? | Detail |
|---|---|---|
| **F1** hub CLOSE renders at 12/255 | ❌ **NO — needs minting** | The only ticket on this subject is `WORK_ORDER_1597_manage_hub_heart_chip_ghost_close_and_cards_that_do_not_fill_the_screen.md`, `**Status:** CLOSED 2026-09-08 - owner felt-test PASS`. A closed ticket does not cover a frame taken two days later. **Subject to mint:** *"the hub CLOSE is measured at 12/255 on all three 07:57 landscape frames while `[chrome-close-is-live]` stays green — a source lint cannot see luminance; add a MEASURED contrast case beside it."* |
| **F2** Manage flow-map capture never ran | ⚠ **PARTIAL** — `WORK_ORDER_1567_consolidate_the_manage_art_wave_gate_capture_and_ship.md`, `**Status:** READY TO IMPLEMENT — handover to the CLI lane`. WO-1567 owns "gate, capture and ship" for the Manage wave, so the run belongs to it. No separate ticket needed; the lane just has to fire the entry point. |
| **F3** no frames for panels 2–8 | ⚠ **PARTIAL** — same ticket, WO-1567. It is the same run as F2. |
| **F4** 21 rows not frame-ticked | ⚠ **PARTIAL** — same ticket, WO-1567 (it is the same capture run as F2/F3). |
| `wave5-reg2` produced no marker (§5c, not a row failure) | ❌ **NO — worth minting** (low severity) | **Subject to mint:** *"`Builds/wave5-reg2` (07:55) and its runner.txt carry no `REGRESSION_` marker and no VERDICT — establish whether the run died or was killed, and make a marker-less regression run fail loudly rather than sit on disk between two green gates looking like a pass."* |
| §2 6.4 ruling 21 (WITHDRAWN — was never a finding) | n/a | ⛔ **This row was raised in the first pass and is WITHDRAWN: ruling 21 IS pinned** by `TroopReachabilityRegression` (`TROOP_REACHABILITY_OK`, green on `wave5-reg1`). Nothing to mint. The first call came from a grep scoped to one file — recorded here rather than deleted, because a withdrawn finding that leaves no trace is how the same wrong grep gets run again. |
| §2 1.3 three hub illustrations owed | ⚠ **PARTIAL** — named in WO-1597's RESULT and in `ManageArt.HubArtStandIns`' own doc comment (`ManageArt.cs:160-161`: *"THESE ARE THE OWNER'S TO SWAP"*). An art-delivery ask, not a code defect. |
| C7 the "110.4 px" figure | ❌ **NO — worth minting** | **Subject:** *"WO-1566 C7 cites 110.4 px on `ManageTabs/ObsBtn_*` / `ManageQueueDoor` / `ManageFilters/ObsBtn_*`; those widget names produce zero hits in a 494/494 regression log. Either measure the Manage tree with the touch oracle or retire the figure."* |

**Also needing a documentation fix (not a fail row):** WO-1566 §1 and §3 are both **materially stale**
(closed blocker, delivered assets). Per CLAUDE.md §15 they should carry a `⚠ SUPERSEDED 2026-09-10`
banner pointing at this RESULT rather than being rewritten in place.

---

## 8. TALLY — counted row by row off the tables above

| Section | rows | PASS | FAIL | NM | PARTIAL | ART | SUPERSEDED |
|---|---|---|---|---|---|---|---|
| Chrome C1-C8 | 8 | 6 | 0 | 1 (C7) | 1 (C3) | 0 | 0 |
| Panel 1 | 4 | 2 | 1 (1.2) | 0 | 0 | 1 (1.3) | 0 |
| Panel 2 | 7 | 4 | 0 | 3 | 0 | 0 | 0 |
| Panel 3 | 7 | 7 | 0 | 0 | 0 | 0 | 0 |
| Panel 4 | 4 | 3 | 0 | 1 | 0 | 0 | 0 |
| Panel 5 | 5 | 3 | 0 | 1 | 1 (5.3) | 0 | 0 |
| Panel 6 | 4 | 2 | 0 | 2 | 0 | 0 | 0 |
| Panel 7 | 5 | 4 | 0 | 0 | 1 (7.5) | 0 | 0 |
| Panel 8 | 6 | 6 | 0 | 0 | 0 | 0 | 0 |
| **§2 subtotal** | **50** | **37** | **1** | **8** | **3** | **1** | **0** |
| §3 asset binding | 12 | 12 | 0 | 0 | 0 | 0 | 0 |
| §4 the proof (§5b) | 5 | 2 | 3 | 0 | 0 | 0 | 0 |
| §1 the measurement | 1 | 0 | 0 | 0 | 0 | 0 | 1 |
| **TOTAL** | **68** | **51** | **4** | **8** | **3** | **1** | **1** |

`51 + 4 + 8 + 3 + 1 + 1 = 68` ✓

**PARTIAL rows are counted as unmeasured, not as passes** — in each the unproven half governs (C3: the
pill is placed, the count badge is unproven; 5.3: the icons exist and are keyed, nothing proves they
paint; 7.5: the cost is priced, the icons are unproven). So the headline reads **11 unmeasured**
(8 NM + 3 PARTIAL).

**Status line set on the WO:**
`AUDITED 2026-09-10 - 51 pass / 4 fail / 11 unmeasured / 1 blocked-on-art / 1 superseded (68 rows), see RESULT`

**The four FAILs:** §2 1.2 (hub CLOSE at 12/255), §4 proof 2 (`MANAGE_FLOW_MAP_OK` never emitted),
§4 proof 4 (no PNG to open for panels 2-8), §4 proof 5 (21 rows not frame-ticked).

⛔ **NO PANEL IS MARKED DONE BY THIS AUDIT.** §2.0 stands: a device frame, in landscape, judged by the
owner at >=95% on all five axes.

# WO-1567: consolidate the Manage art wave — clear two compile blockers, gate, capture, build, push

**Status:** PARTIALLY DONE - AWAITING OWNER MATCH 2026-09-10 - the chain RAN and every mechanical gate
in §3 that was fired is green on a fresh log: `Builds/wave5-compile5` `COMPILE_GATE_OK`;
`Builds/wave5-reg3` `REGRESSION_OK 494/494 suites` with **`ManagePortraitCoverageRegression` green BY
NAME** (`MANAGE_PORTRAIT_COVERAGE_OK 68 ... resolve; 0 ... absent`); `Builds/wave5-manageflow2`
`MANAGE_FLOW_MAP_OK 20 frames` + `UI_GEOMETRY_OK 20` + `UI_TOUCH_OK 20/20` + `UI_GLYPH_OK 20/20
labels=361 baselined=0`, with `CAPTURE_LEDGER_MISSING`/`_DUPLICATE` both 0 and all 20
`Builds/ui-capture/ManageFlow_*.png` on disk; ship: `APK_OK 08:05 size=444MB` + `R2_PARITY_OK
objects=279` (postdating the APK, §16) + build `2026.09.10.363660`; device frames captured. PNGs
OPENED - the hub frame shows criterion zero met (full-bleed, not the 64% plate), three whole two-line
descriptions and art on all three cards. ⛔ **BUT §6.0 IS THE GATE THAT CLOSES THIS AND IT IS THE
OWNER'S:** a device frame beside its mockup panel, >=95% on SIZE/FONT/STYLE/CONTEXT/IMAGES - "items 1-4
... can never mark a ticket done". Board bucket = **Verify**. OPEN: (1) the owner match; (2) §3 step
ZERO `CATALOG_FALLBACK_GEN_OK` was **NOT re-run this session** (last hit 09-09 13:02); (3) the order's
final `AAB` step not run; (4) §6 item 4's panel-by-panel is 51 pass / 4 fail / 11 unmeasured per the
`736b6b4b9` audit; (5) §4a row 3b PRODUCTION-per-hour still an open handback
(`ResourceBuildingProgression` producer owed); (6) the on-device install + `dumpsys` version were NOT
verified by the closing lane (no install line in `overnight-apk-status.txt`). Also NOT run:
`MANAGE_OPERATIONAL_CAPTURE_OK` (a different entry point; blocks nothing here). RESULT:
`WorkOrders/WORK_ORDER_1567_consolidate_the_manage_art_wave_gate_capture_and_ship.RESULT.md`
*(was: READY TO IMPLEMENT — **handover to the CLI lane.** Owner ask 2026-09-06: "hand over the
details in a WO to CLI for them to consolidate and push with the build.")*
**Priority:** P1 — it is the gate/ship step for work that is already committed and cannot be proven until
the tree compiles.
**Silo:** the gate + capture + build chain. ⛔ **This WO changes no gameplay code.** The only edits it
authorises are the two compile fixes in §2, and those belong to the lanes named there.
**Minted** from the banner (`CLI_LANES_WO_NUMBERS.md`, main line 1567 -> 1568 in the SAME edit).

---

## 1. WHAT IS ALREADY DONE AND COMMITTED — do not redo any of it

| Commit | What |
|---|---|
| `ad808ecf3` | **the Manage art conformance pack** — 57 PNGs + 58 metas |
| `eb3698daf` | the art ask narrowed to an export job |
| `3cb621863` | **WO-1566** the conformance spec + the art ask |
| `8b2481895` | WO-1534, the parent review |
| `(earlier)` | WO-1541/1542/1543 + WO-1560..1565, the nine implementable lanes |

**Verified at import, before the files were written — not after:**
- **26 of 26** structures the BUILD grid offers now resolve a portrait. **Zero missing**, up from 5.
- The 21 filenames are an **exact match** for the 21 missing catalog ids — underscores intact
  (`tower_ground_archer`), `pet-house` hyphenated, and the three id/name traps honoured
  (`collector_farm` = Quarry, `collector_forge` = Iron Mine, `silo` = Stoneyard).
- 21 portraits **1024×1024 RGBA**, every corner alpha 0. 36 UI files under
  `Assets/Resources/UI/ElarionMedieval/Manage/` (25 × 256², 9 × 512², 2 × 512×64).
- **84 metas, 84 unique GUIDs, 0 duplicates, 0 orphan PNGs.** Metas generated from `barracks.png.meta`
  verbatim (Sprite / Single / `alphaIsTransparency: 1`).
- No file overwrote an existing one; the six tier ladders are untouched and complete.

⚠ **ALL OF THAT IS FILESYSTEM EVIDENCE, NOT UNITY EVIDENCE.** It is arithmetic over ids and files. The
Unity-side proof is `ManagePortraitCoverageRegression`, which has **not run**, because the tree does not
compile (§2). **Do not report the art as verified until that oracle is green in a marker.**

---

## 2. ⛔ THE TWO COMPILE BLOCKERS — the gate is RED and NEITHER IS THE ART

Measured twice, `Builds/cg-artpack.log` (22:36) and `Builds/cg-artpack2.log` (22:38). Both runs
**exited 0 and both FAILED** — `VERDICT=FAIL reason=MARKER_ABSENT`, no `COMPILE_GATE_OK`. That is the
`gates-report-success-without-proving-it` class; **judge the marker, never the exit code.**

⛔ **PNGs cannot produce CS errors. The art did not cause either of these.**

### 2a. 66 errors — `Assets/Editor/Regression/RaidSelectionSpoilsRegression.cs`
The file is **committed and unmodified**. It calls `RaidSelectionVM.ArmyLockWordFor` and siblings, which
the in-flight **WO-1542** lane removed from the VM (`CS0117`, `CS1061` on `RaidSelectionVM`).
**Owner:** the WO-1542 lane. That ticket ruled *"Warning, not a lock"*, so the word changes — **the oracle
must move WITH the ruling, not be deleted.** Re-point it at whatever replaces `ArmyLockWordFor`, and keep
it asserting that a card whose face claims a lock cannot silently open (WO-1542 acceptance 4).

### 2b. 6 errors — `Assets/Editor/Regression/SkillsPanelLayoutRegression.cs`
Uncommitted. `:1460` uses **`lGround`**, which is undefined (`CS0103`), while its siblings `lWell` and
`lRaised` on the same statement resolve. **A declaration was dropped mid-edit.** One line to restore.
**Owner:** whichever lane is editing that file.

⚠ **The first gate run raced a live edit** — `RaidSelectionVM.cs` and `RaidSelectionScreen.cs` were
written at 22:37:20 and 22:37:32, *after* the run started at 22:36:09. **Confirm the tree is quiescent
before believing any gate result.** A gate over a half-written tree is not a verdict.

---

## 3. THE SEQUENCE TO RUN — in this order, each judged by its MARKER on a FRESH log

> ## ⛔ ONE SEAT FIRES UNITY WHILE LANES ARE OPEN. Owner directive, 2026-09-06.
> Verbatim: *"Only this seat should fire Unity while lanes are open, or every result is a gate over a
> half-written tree."*
>
> **Forged by this file's own evidence.** The gate at **22:36:09** was overtaken by edits at **22:37:20**
> and **22:37:32**; a second gate at **22:38:42**, fired without waiting for quiescence, returned **72**
> errors where the first returned **6** — none of the difference meaningful, all of it mid-save. **Two
> Unity runs, zero verdicts, and a round of analysis spent on breakage that was simply in flight.**
>
> **The rule:** while lanes are open, the coordinating seat runs the chain. Any other seat **hands the
> sequence over** rather than running it. Whoever does run it **proves quiescence first** — no `.cs`
> mtime newer than the run start — and states that proof beside the result.

### ⭐ THE FULL ORDER (owner, 2026-09-06), once the last two lanes report

`catalog fallback regen` -> `compile` -> `regression` (**portrait coverage green BY NAME**) ->
`Manage flow capture` (**PNGs opened**) -> `commit by path with statuses flipped` -> `tester APK` ->
`install` -> `AAB`.

⚠ **`catalog fallback regen` comes FIRST and is easy to skip** — it emits its own marker
(`CATALOG_FALLBACK_GEN_OK`, seen on the WO-2003/2005 RESULTs). The generated
`CatalogFallbackData.g.cs` embeds catalog rows, so a regen after the art wave keeps the generated copy and
the canonical JSON in step.
⚠ **Statuses flip in the SAME commit as the work** (CLAUDE.md §2) — the board is derived from them, and a
gate commit that carries a lane without flipping it is the `gate-commit-must-flip-every-lane-it-carries`
failure. Cross-check every READY ticket against the diff before committing.

⛔ **Gate scripts live at the REPO ROOT, not `tools/`** (memory `gate-scripts-live-at-repo-root`).
⛔ **Judge by MARKER + log freshness + size. NEVER the exit code** (CLAUDE.md §8).

1. **Compile** — `run-unity-method.ps1 -Method DeNelle.Editor.CompileGate.Run -ExpectMarker COMPILE_GATE_OK`
2. **Regression** — `DeNelle.Editor.Regression.DataRegression.RunAll` -> `REGRESSION_OK <n>/<n> suites`.
   ⭐ **`ManagePortraitCoverageRegression` is THE proof for this wave.** It enumerates the catalog against
   the filesystem and fails **by name** on any id with no portrait. Green = the grid is dressed.
3. **Capture** — `DeNelle.Editor.UICaptureLaunch.RunManageFlowMapCaptureHeadless` ->
   `MANAGE_FLOW_MAP_OK`. Output is **`Builds/ui-capture/`**, never `docs/manage-flow-map/` (that is a
   frozen 09:17 baseline). Require **no** `CAPTURE_LEDGER_MISSING` and **no** `CAPTURE_LEDGER_DUPLICATE`.
4. ⛔ **OPEN THE PNGs AND LOOK.** A marker proves frames were written, never that they look right
   (memory `headless-screenshot-verify-ui-before-build`).
5. **Compare** against the mockup panel by panel — `WorkOrders/ManageRedesign/CAPTURE_LOOP_GOAL.md` §3 and
   **WO-1566** §2. Any visible difference is another pass; the acceptance is exact, not similar.
6. **Build + install** through the sanctioned chain only.

---

## 4. ⛔ §16 — READ THIS BEFORE PUSHING. THE ANSWER IS "NO R2 PUSH IS OWED", AND HERE IS THE PROOF

**Owner ruling 2026-09-06, asked and answered in-session:** the Manage portraits **stay in `Resources/`
and are NOT addressable.** *(She briefly said "should be addressable", then reversed it the same minute —
"ok not addressable, thats better". The reversal is the ruling. Recorded because a future seat will
otherwise re-open it.)*

**Measured this session, not assumed:**
- No Addressable group references `Resources/Portraits` — the grep returns nothing.
- `git status ServerData/` is **empty**; the wave touched none of it.

**Therefore:** this art ships **inside the APK** via `Resources`. **No `r2-ship.ps1` run is owed for it**,
and the `.githooks/pre-push` invariant (proof must postdate the bytes) is satisfied untouched, because
`ServerData/` did not change.

⚠ **That is NOT permission to hand-build and `adb install`.** CLAUDE.md §16 is explicit: installing or
distributing goes **through the scripts** (`overnight-apk-build.ps1` / `install-apk-to-seeker.ps1`),
never raw `adb`. Those chains call `r2-ship.ps1` themselves; let them. The 2026-08-20 capsule incident
happened precisely because a build was made and installed outside them.

⚠ **Had the addressable answer gone the other way, every content build would need its own push** —
bundle names are content-hashed, so a previous push can never cover a new build. It did not go that way.
Do not migrate these to Addressables without a fresh ruling.

---

## 5. TWO ART CAVEATS — neither blocks, both are owed a look

1. **The 21 portraits were reconstructed from the contact sheets and UPSCALED to 1024.** This is the
   delivery's own note: *"suitable for grid/mobile review, but should be visually checked at large
   detail-card scale after Unity import."* **Mockup panel 3 draws art large — judge them there.** Grid
   scale will flatter them.
2. **The four tile frames are NOT drop-in interchangeable at one rect.** Measured alpha bounding boxes at
   512²: `frame-max` reaches **23 px** from the top, `frame-selected` starts at **84 px**, and none is
   symmetric in its own canvas (`frame-tile` = L72 T64 R53 B83). Swapping state at a fixed rect will make
   the border jump or change weight. Either re-centre them to a common inset, or drive each state's rect
   from its own bbox. ⭐ The earlier defect — two frames opaque-centred, two hollow — **is fixed**; all
   four now read centre alpha 0.

3. **✅ THE BLANK TAN OVALS WERE A CODE DEFECT — A SECOND KEY PRODUCER. FIXED 2026-09-06.**
   **NO ART IS REQUESTED BY THIS ITEM.** Recorded by the WO-1541/1563/1564 lane.

   `Builds/ui-capture/ManageFlow_BUILD_gridtop_2670x1200.png` showed **Wooden Palisade** and
   **Crystal Mine** as empty medallion rings. ⚠ My first note here called that a missing asset;
   **that was wrong and is retracted.** The art was never missing — the key was.

   **Cause, measured:** `BuildDefenseChoices` composed the key from the **display-name slug**
   (`ManageScreenVM.ResolveBuildingPortraitKey` + `PortraitSlug`), so `cap-manage-wave3.log` traced
   `id=wall_wood -> 'Portraits/wooden-palisade'` and `id=mine_crystal -> 'Portraits/crystal-mine-2'`
   against the **mixed root** folder, where neither exists. `ManageArt.LoadSprite:177-186` documents
   the exact symptom: a miss paints the **warm-tan placeholder disc** inside its ring. Six ids were
   affected, not two — `lumberyard-3`, `foundry-2`, `stoneyard`, `healing-caravan` too.

   That was a **SECOND producer** of a key `ManageArt.BuildingPortraitKey` already owns from the
   catalog **id** against `Portraits/Buildings/` — the duplicated-state failure, with the slug as
   the stale copy. `ManagePortraitCoverageRegression`'s header records that `BuildBuildingChoices`
   had already been re-pointed for this reason (its `[building-tier-portrait]` case failed on
   **twenty** root keys while **"(none)"** were missing under `Portraits/Buildings/`).
   `BuildDefenseChoices` was the last slug caller.

   **Fixed in code, art untouched:** `BuildDefenseChoices` now calls
   `ManageArt.BuildingPortraitKey(entry.id, level)`; `ResolveBuildingPortraitKey` and `PortraitSlug`
   are **deleted** (zero callers). Verified at source that every base id resolves —
   `tower_ground_archer`, `tower_ballista`, `wall_wood`, `mine_crystal`, `lumberyard`, `foundry`,
   `silo`, `healing_caravan` — and the tier ladder is unchanged (`forge-4`, `barracks-3` still
   resolve). Pinned by a new fixture case `[portrait-key-single-producer]` in
   `ManageProgressiveDisclosureRegression`.

   ✅ **THE MISPLACED TIER SHEETS ARE MOVED — nothing is owed here any more.** The lead moved all
   eight into `Portraits/Buildings/` under the id spelling (`tower_ground_archer-2/-3`,
   `tower_ballista-2/-3`, `tower_catapult-2/-3`, `tower_arcane_spire-2/-3`). Verified on disk
   2026-09-07: every one of the five defence ladders now resolves at every authored tier, and all
   twelve base sheets resolve.

4. **ART ASK — two Sky Ballista tier sheets were never commissioned.** `tower_siege_tower-2` and
   `tower_siege_tower-3` do not exist under **any** spelling, in either portrait folder.

   ⚠ **This is NOT a regression and NOT misplacement** — corrected here after I first guessed it was.
   `structures-catalog.json` names `tower_siege_tower` **"Sky Ballista (Anti-Air)"**, so its retired
   slug would have been `sky-ballista-2/-3`, which have never existed either: the old display-name
   composer would have asked for them and painted the placeholder disc exactly as today's id key
   does. ⛔ `wizard-tower-2.png` / `wizard-tower-3.png` in the root folder are **unrelated legacy
   art and are NOT this tower's sheets** — I had inferred they were; they are not. Moving them under
   the id spelling would have silently swapped in the wrong picture, which is the "a wrong icon is a
   lie the capture loop cannot see" failure `ManageArt.cs:152-156` exists to prevent.

   **Consequence today:** a level 2 or 3 Sky Ballista paints its base sheet and logs. That is the
   designed behaviour for uncommissioned art.
   **`ManageDefenseCardRegression` deliberately does NOT fail on these** — it emits an
   `ART ASK (not a failure)` note line naming them, because failing would block the gate on a
   picture nobody has drawn. It DOES fail on misplacement (a tier sheet present under the retired
   spelling but absent under the id spelling), which is the real defect class.

   **Owed: two sheets, `tower_siege_tower-2` and `tower_siege_tower-3`, drawn to match
   `Sky_Ballista` / `tower_siege_tower`.**

5. **ART ASK — the three HUB CARD illustrations (mockup panel 1). Added 2026-09-07.**

   Mockup panel 1 draws a portrait-shaped illustration filling each of the three hub cards: a
   BUILDING on BUILD, a HELMET on ARMY, a BOOK on RESEARCH. **None of the three exists.** The art
   wave delivered 36 UI files into `Assets/Resources/UI/ElarionMedieval/Manage/` (§1) and they are
   frames, filter chips, resource glyphs and stat glyphs — no hub illustration among them.

   **Owed, by Resources key** (declared at `ManageArt.HubArtBuild/HubArtArmy/HubArtResearch`, and
   listed together in `ManageArt.HubArtKeys` so the trace and its oracle read one source):
   - `UI/ElarionMedieval/Manage/hub-build`
   - `UI/ElarionMedieval/Manage/hub-army`
   - `UI/ElarionMedieval/Manage/hub-research`

   **Shape: PORTRAIT, roughly 145:160 — the card's own aspect** (`ManageScreenPanel.HubCardAspect`,
   measured off the mockup sheet), filling the top ~46% of the card (`HubArtWellF`).

   ⛔ **THE RETIRED LANDSCAPE STRIPS ARE NOT A SUBSTITUTE, AND THIS HAS BEEN TRIED.**
   `Assets/Resources/UI/ElarionMedieval/cards/*.png` are 1963×789 strips drawn for the retired wide
   2×2 seat; preserveAspect-ing one into a tall card letterboxes two thirds of it black, which reads
   as BROKEN rather than as art-pending. `cards/troops.png` — the UNLOCKED army card — has never
   existed at all; only `cards/troops-locked.png` does.

   **Consequence today, and it is deliberate:** each card paints a FRAMED, EMPTY well and the three
   missing keys are named once per session through `FlowTrace.Once("Manage", "hub-art-ask", ...)`.
   A bordered empty well says "a picture belongs here"; a black two-thirds says the screen is broken.

---

## 6. ACCEPTANCE

### 6.0 THE ONLY ACCEPTANCE THAT CLOSES A MANAGE SCREEN - owner ruling 2026-09-07

**Owner, 01:10:** *"fix the board so those tickets dont say done and update the goal to be screenshots
proving these match"* - **01:12:** *"95% coverage in size font style context images"* ... *"thats the
minimum threshold to pass"* - **01:14:** *"i expect these images to fill the screen, not 60% of it"*

- **The acceptance is a DEVICE SCREENSHOT placed beside its mockup panel and judged a match BY THE
  OWNER.** Items 1-4 below are evidence toward that judgement. **They can never mark a ticket done.**
- **A Manage ticket moves to DONE only when the owner says the frame matches.** Until then its status
  reads `AWAITING OWNER MATCH` and the board buckets it **Verify**, not Done and not Fixed.
- **CRITERION ZERO: the panel FILLS the screen** (full bleed inside the safe area, not a 60%-width
  plate over the town). Judged first; it multiplies every other axis. Section 4a's plate measurement
  (x 0.18-0.82 = 64% of the canvas) is the defect it names.
- **THE FIVE AXES AND THE FLOOR:** at least **95% matched** on **SIZE**, **FONT**, **STYLE**,
  **CONTEXT** and **IMAGES**, judged by the owner. Under 95% on any axis is a FAIL.
- Nine-row scorecard with one column per axis, and the full ruling:
  `WorkOrders/ManageRedesign/CAPTURE_LOOP_GOAL.md` (top block) and `OWNER_RULINGS_LOCKED.md` ruling 29.

1. `COMPILE_GATE_OK` on a fresh log, over a **quiescent** tree.
2. `REGRESSION_OK <n>/<n>` with **`ManagePortraitCoverageRegression` green** — the Unity proof that
   §1's filesystem arithmetic is real.
3. `MANAGE_FLOW_MAP_OK`, no missing or duplicate frames, and **the PNGs opened and looked at**.
4. The panel-by-panel comparison recorded against WO-1566 §2 / `CAPTURE_LOOP_GOAL.md` §3 — each row ticked
   from a frame, or marked BLOCKED with the reason named.

### 4a. THE PANEL-BY-PANEL COMPARISON — device build 358872, 2026-09-07

**PASS THRESHOLD (owner, 2026-09-07): 95% match on SIZE, FONT, STYLE, CONTEXT, IMAGES.**

⛔ **AXIS 1 ON EVERY ROW, AND IT IS THE ONE THAT MULTIPLIES ALL THE OTHERS: DOES THE PANEL FILL THE
SCREEN?** Owner ruling 2026-09-07 01:14, verbatim: ***"i expect these images to fill the screen, not
60% of it"***. Every frame below was taken through a plate at **x 0.18–0.82 = 64% of the canvas**,
with the town visible around it, while every mockup panel is full-frame. Fixed FIRST and everything
else re-derived from it: `ManageScreenPanel.ManagePanelInsetF = 0.02f` (96% of the safe area on both
axes), with the retired reasoning superseded in place at the `BuildObsidianPanel` call site. The
problem that band was solving is real and now solved one layer down — `ManageWorkspacePanel.MaxTileAspect`
clamps a cell's WIDTH to the mockup's drawn tile shape and the row centres, so a full-width band gives
even side margins instead of bars.

⚠ **EVERY ROW BELOW IS A SOURCE-LEVEL FIX, NOT A TICKED CAPTURE.** No Unity run was in this lane's
scope, so nothing here is proven on a frame yet. The evidence for each DEFECT is the owner's own
device capture, named per row; the evidence for each FIX will be the next `MANAGE_FLOW_MAP_OK`.

| # | Panel | Frame the defect was measured on | What was wrong | What changed |
|---|---|---|---|---|
| 1 | MANAGE hub | `owner-screen-20260907-004724.png` | cards ~2.2:1 against the mockup's 0.9:1; all three descriptions ellipsised ("Construct and upgrade yo…"); no art on any card | card band derived from `HubTitleBandPx`/`HubCloseBandPx`/`HubBandGapPx` instead of two typed fractions; cell clamped to `HubCardAspect` (145:160, measured off the sheet) and the row centred; description now `FitBlock` at `ElarionUi.FontFloorMobile` over a two-line band; a framed EMPTY art well per card. **HEART L1 KEPT** — `CAPTURE_LOOP_GOAL.md:130` gates its removal on a Heart door existing elsewhere and `HeartSurfaceRegression.cs:118-123` pins this face as the only one. **ART ASK: `hub-build`, `hub-army`, `hub-research` under `UI/ElarionMedieval/Manage/` (§5 item 5).** |
| 2 | MANAGE / BUILD grid | `owner-screen-20260907-004825.png` | round medallion + ring on every tile; "SHORT 28…" / "SHORT 72…" ellipsised state words | square edge-to-edge portrait via `SquarePortrait` (envelope-crop, no ring); tile paints the model's CLOSED word (`ManageTileVM.StateWord`, composed by `ApplyBuildBadge` beside WO-1518's full sentence — the row and the detail card keep the amounts); the two opaque frames no longer paint under a full-bleed portrait, the two hollow ones ride over it |
| 3 | LUMBER MILL detail | `owner-screen-20260907-004903.png` | "2600 970" naming no resource; "UPGRADE . STONE 2600 GOL…" clipped mid-word; description doubled by a " . " joiner; circular art | cost row draws the model's `Label` word beside the amount **and** a delivered glyph (`CostIconFor`, which was returning `null` for every row); CTA reads the verb only; description, promotion note and level/state each on their own row; captioned cost band ("Upgrade Cost") with the clock on its own line; promoted stat value BOLD, not green; square art at `artFrac` 0.42. ⚠ **STONE/GOLD are correct live words and ruling 22's charge — nothing in the basket changed.** |
| 4 | MANAGE / ARMY grid | `owner-screen-20260907-005136.png` | same medallion shape; locked troops at full brightness | same square tile treatment; **locked tiles dimmed by a luminance multiply (0.42), never a hue shift** — and never the only cue, the word LOCKED and the padlock stay |
| 5 | ARCHER detail | `owner-screen-20260907-005222.png` | "TRAIN . 1M 0S" on the button; promotion note glued to the description; "Level 5 . UPGRADING" | face reads **"TRAIN 1 ARCHER"**; duration moved out of the stats table into the clock band ("Train Time"); `AuxiliaryText` on its own row; level on its own line with the state as a BADGE plate. ⛔ **DELIBERATE DIVERGENCE: no "550 gold" train cost.** Training is FREE — owner ruling WO-1387, *"training free … just time"*, and `FillTrainFacts` sets `TrainCostText = ""` for that reason. Inventing a price to fill the mockup's band would be a charge the game does not make. |
| 6 | OUTRIDER, locked | `owner-screen-20260907-005311.png` | requirement joined to the description with " . " | requirement is its own **padlock row** (the shape channel for locked). **DELIBERATE DIVERGENCE KEPT: the live `VIEW BARRACKS` door beats the mockup's inert LOCKED plate** — WO-1518's door ruling applied to troops |
| 7 | MANAGE / RESEARCH picker | `owner-screen-20260907-005358.png` | `ceil(sqrt(4))` → four short wide tiles in a 2×2 over a 60%-black well; tiles painting the retired 1963×789 landscape strips through an oval mask | `columns = Clamp(count, 1, 5)`, one row while they fit; a single-row grid may grow its cell to SQUARE (the `MaxTileHeightPx` cap yields to the cell width when `rows == 1 && columns > 1`); school portrait bound through `ManageArt.BuildingPortraitKey(BuildingId, 1)` — the last caller of the retired strips. "N READY / N LOCKED" kept |
| 8 | LUMBER MILL research tree | `owner-screen-20260907-010151.png` | No school painting beside the rows; requirement glued into the benefit line and truncated; perk medallions leak a baked caption | **DONE AT SOURCE 2026-09-07, UNGATED.** (a) `ManageTabVM.HeaderArtKey` (model-composed off `ManageArt.BuildingPortraitKey`, the ONE producer) + `ManageWorkspacePanel.BuildListPainting` carve the left 40% of the well for the school and hand the rows the rest — every measurement below it resolves against the band the rows actually get. (b) The model-side `" . "` join in `ComposeResearchItem` is **deleted**; the blocker rides the new `ManageTileVM.RequirementText` and `BuildListRow` paints it as its own **padlock row**, while the WO-1518 door word stays on `StateText` where the composer derives it from a real route (no second copy). (c) The medallion is CROPPED to the framed picture — MEASURED on `Lumber_Mill_T1_Improved_Logging.jpg` (786x1177): the frame runs y 155..800 and everything under y~840 is the perk's NAME in gold, which the row already typesets. `ManageArt.IsCaptionedPerkIcon` + `PerkIconU0..V1` own the test and the rect; `ManageWorkspacePanel.CroppedIcon` seats it by ANCHORS, so it needs no layout pass. (d) The inline RESEARCH face + its price were already built by `BuildListRow`; a researchable perk is now **seeded and pinned** — `ManageProgressiveDisclosureRegression` gets `Resources.Coins` in its fixture and a measured case `[research-row-offers-its-action]`. Pinned by `ManageMockupConformanceRegression.CheckResearchTree`. |
| 9 | QUEUE drawer | `owner-screen-20260907-010257.png`, `-010356.png` | rows clipped top and bottom, "CANC…", the crystal cost colliding with SPEED UP, no active-tab state, and the RESEARCH tab telling her to "tap TRAIN" | **DONE AT SOURCE 2026-09-07, UNGATED — `WorkOrders/WORK_ORDER_1488_*.RESULT.md` is the record.** ⚠ **AND THE ROOT CAUSE IN THIS ROW NEEDED ONE CORRECTION:** `DrawerModeListKeepPx` is read only on the BAND path, and `ApplyDrawerPlacement` takes the OVERLAY path on every Manage screen (`band` requires `!WorkspaceActive`, and the WO-2001 workspace owns the well) — her frames prove it, since the title/X/tabs are overlay-only chrome. The pin was holding a constant for a shape nothing renders. It is **re-pointed** to measure the DERIVED row height (`_queueRowPx`, off the measured list band, clamped into `[MinTouchPx, RowHeightPx]` against a `QueueRowsVisibleTarget` of 5) and to pin no row clipping. Also: drawer floor 0.02 -> 0 (fills to the CLOSE band), row thumbnails, full-word CANCEL beside a compact `MinTouchPx` Ad chip (kept and CITED — WO-911:84-85 + `BuildTimerService.cs:1160` rule ad-skip per channel, and `ObsidianQueueVM.cs:208` is the per-row gate), active line plate by weight+underline, and a per-channel empty state off ONE `QueueChannelVerb` table. ⛔ **READ THE RESULT's section 4:** five rows DO NOT fit the ~205px list band (they need ~612px) — the code seats what fits and WARNS in px rather than shrinking rows under the touch floor. The next lever is the CLOSE-band reservation, now dead space on non-hub screens after WO-1491. |
| 3b | LUMBER MILL detail — the current -> next TABLE | `owner-screen-20260907-004903.png` | Mockup panel 3 draws "Production 120 / hour -> 180 / hour" and "Storage 2,000 -> 3,000"; the card drew ONE prose row | **PARTLY DONE — STORAGE WIRED, PRODUCTION HANDED BACK.** `ManageScreenVM.BuildingStatRows` composes the Storage pair from `TownBankCapacity.CapacityAtLevel(repo.storageCapacity, level)` — already the single authority for a container's ceiling at a placed level (`TownBankCapacity.cs:424`, folding `StorageCapsCatalog`'s ladder) — called at the LIVE level and the next rung, into `ManageStatVM.DeltaText`. ⛔ **PRODUCTION IS NOT WIRED AND THAT IS A REPORTED HANDBACK, NOT AN OMISSION.** There is no single producer for output-per-hour at level N: the shape lives PRIVATELY in `ResourceCollector.ThroughputScale` (`ResourceCollector.cs:943-959`) and folds LIVE state plus the echo multiplier, so it answers a different question from a catalog preview. `StructureCardVM.UpgradeStats` cannot stand in either — that projection is fixed at level 1 by contract (`CurrentLevel => 1`, "Upgrade to Lv 2") and carries DPS/Range only, so a placed Level 2 mill would be shown its Level 1 -> 2 pair. **Owed: one public producer on `ResourceBuildingProgression` with `ThroughputScale` re-pointed at it** — a `Village/Buildings/Progression` edit, outside the Manage silo. |
### 4b. ROUND 25 — the first pass judged against a FRESH headless capture, 2026-09-07

**Evidence base:** `Builds/ui-capture/ManageFlow_*_2670x1200.png` (16 frames from tonight's gated
tree) and `Builds/cap-manage-wave4.log`. ⚠ **EVERY ROW IS A SOURCE-LEVEL FIX AND NOTHING HERE IS
PROVEN ON A FRAME YET** — no Unity run was in this lane's scope. The evidence for each fix is the next
`MANAGE_FLOW_MAP_OK`, and the acceptance is still §6.0's: the owner, beside the mockup panel.

**The one measurement everything else came out of.** `bodyFloor` reserved the shared CLOSE band on
**every** Manage screen, while `ApplyScreenVisibility` renders CLOSE on the **hub alone** (WO-1491).
That is ~150 ref px of the well held for a button that is not drawn — and it is the missing second
row of tiles *and* three of the queue's five rows. The well goes 580px → **758px** at the reference
surface; the hub re-takes the band inside its own host, from the **measured** reclaim.

| # | Panel | Measured on the frame | What changed, and where |
|---|---|---|---|
| 1 | MANAGE hub | three ~245x270 plates centred in an empty full-bleed well; art wells tiny squares ABOVE each plate; "upgrade your to" cut mid-word; HEART L4 sitting across all three cards | `ManageScreenPanel.cs:1748-1760` card band derived from the **measured** close reclaim (`_hubCloseReservePx`) instead of a second typed constant → cards resolve **380x420** at **0.906:1** (the mockup's own ratio) filling **0.55** of the host. `:427-441` `HubTitleBandPx` is no longer the title's (the title is chrome, one band up — a double reservation) but the HEART's, authored at `ElarionUiKit.MinTouchPx`. `:456-476` `HubArtWellF` 0.46 → **0.60** with `HubTitleBandF`/`HubDescBandF` derived from it; `:1913-1932` the well is a filled zone + `GoldPerimeter` instead of `frame-tile` at `preserveAspect: true` — **MEASURED: `frame-tile.png.meta` declares `spriteBorder {0,0,0,0}`**, so the sprite is not 9-sliced and preserveAspect collapsed it to the floating square. `:1866-1872` description band is 0.19 of the card ≈ 80px, checked in px against two lines at `ElarionUi.FontFloorMobile` and **WARNed** if short. `:3078-3098` HEART chip re-seated into the header band at the touch floor |
| 2 | BUILD grid | tiles in the TOP HALF of the well (`viewport=390px` of 580) | `ManageWorkspacePanel.cs:640-653` — `cellCeiling` is the cell's **own width** on every real grid, not the absolute `MaxTileHeightPx` (190). ⛔ The 190 cap could only ever shrink rows *below* the band they were divided out of, because `cellH` is already `(bandH - gaps)/rows`. 5x2 now fills **0.96** of the band, 3x3 **1.00**. `:697-705` whole-row floor gains an epsilon — the ratio is exactly `rows` in real arithmetic and float lands either side of it |
| 4 | ARMY grid | same | same fix; the tile aspect clamp still holds the drawn shape |
| 5 / 3 / 6 | TROOP + BUILDING detail | ring medallion on the detail card | ⛔ **THE RING IS BAKED INTO THE ART** — `Assets/Resources/RpgUi/troop/troop-footman.png` is a 1254x1254 gilt medallion on transparency. `SquarePortrait` was **already** in use here; its `EnvelopeParent` fit crops **nothing** when a square sprite meets a square zone, which is why the same art through the same method reads clean in the grid's 2.3:1 tiles and ringed on the card. `ManageWorkspacePanel.cs:1417-1443` the zone is now the card's **full height** (`0.02..0.98`) at `artFrac` 0.24-0.34, so the crop happens. The detail well itself fills the panel via the CLOSE reclaim |
| 7 | RESEARCH picker | one row of tiles over a dead well | `ManageWorkspacePanel.cs:747-762` a grid that fits its band is **centred**, so the surplus a square tile cannot use is split above and below instead of left as a slab. ⚠ Honest limit: five square tiles sharing 1835px can only be 359px, so the row covers 0.47 of a 758px band — the surplus is real and is now symmetric, not hidden |
| 8 | RESEARCH tree | rows crammed into ~15% of the width, every string truncated | `ManageWorkspacePanel.cs:614-625` — the `MaxTileAspect` **width** clamp was being applied to the LIST shape. Log, verbatim: *"grid cell width clamped from 1064px to 316px (2.3:1 against a 137px cell)"* — the rows were handed the right band and thrown 70% of it away one line later. `columns == 1` is now exempt, the same exemption the height ceiling already carried. Rows take **0.58** of the well beside the painting. `:906-913` the row icon zone is derived square from the row's own rect and the text column starts clear of it |
| 9 | QUEUE drawer | one visible row in a short well | `ManageScreenPanel.cs:600-614` `DrawerOverlayY1` 0.79 → **1.0**, with the shared chrome row standing down under the overlay (`:2543-2558`) — which is also what mockup panel 8 draws: no back arrow, no queue pill, the overlay *is* the queue. `:2470-2484` the plate is flat + `GoldPerimeter` instead of the 9-sliced `content-panel`, whose 96px border cost the rows **192px of 458** (`MANAGE_QUEUE_PLATE ... inset=96px`); `:2646-2652` the other writer no longer restores it. `DrawerTabsPx` 132 → 128 and `DrawerBandGapPx` 12 → 8, both still above their floors. **List band 206px → 614px; five rows need 612px.** ⚠ It clears by **2px** and that is stated, not relied on — `SeatQueueListToWholeRows` still derives the count and still WARNs in px, and no row is ever shrunk under `MinTouchPx` |

**The fidelity oracles — `geometry=44 touch=47`, and they reduce to exactly two causes.**

| Cause | Count | Fix |
|---|---|---|
| Every queue verb authored at `RowCtrlY0..Y1` = 0.88 of the row. That fraction reasons from `RowHeightPx` (132), but WO-1488 made the row a **measurement** clamped to `[MinTouchPx, RowHeightPx]` — so at the floor it resolves **0.88 x 112 = 98.6**, the number on all forty lines (`ObsBtn_SPEED UP` 372.5x98.6, `CANCEL` 516.4x98.6, `Move up` 249.7x98.6) | 40 geometry + 40 touch | `ManageScreenPanel.cs:615-655` — `QueueCtrlY0/Y1`, derived per row: at the floor the control takes the whole row, above it the authored 0.88 stays. ⛔ The row is **not** grown to 128 to suit the fraction — that costs a visible row |
| `ManageHeartFace` at 0.70-0.83 of the hub host: **440.5x75.4** (36.6px under the floor) and inside the card band, which was 0.281-0.794 once it became derived | 1 geometry + 1 touch + 3 overlap + 3 button-over-text | the header-band reseat in row 1 above |
| **Total** | **44 / 47** | **→ 0 by source read. UNPROVEN until the next `MANAGE_FLOW_MAP_OK`** |

**`UI_CAPTURE_FIDELITY_DEGRADED 16/16` — a CAPTURE-HARNESS DEFECT, not a layout one.** The marker
quoted its own cause: *"Reason: the aspect-divergence proof did not run"* — which is
`ReportFidelity`'s **fallback** string, printed when `_geoMoveProof` is null because nothing ever
attempted the proof. Five capture bodies call `ProveGeometryMoves` (`UICaptureLaunch.cs:571, 648,
670, 2068, 2600`); **neither Manage entry point did**, so both degraded every build on a proof they
never asked for. Both now call it (`:7000-7008`, `:7820-7833`). Verified alongside: **no Manage panel
reads `Screen.*`** (grep over `Assets/_Modules/Core/Manage` and `.../Village/UI/Manage` — zero hits),
so nothing in these panels is outside the kit surface the scope moves.
⛔ **WHAT IS PROVEN AND WHAT IS NOT.** Proven: the proof was never *attempted*, so `16/16` carried no
information about the frames. **NOT proven: that it PASSES when run.** `ProveGeometryMoves` builds a
probe modal at the two most-different aspects and compares the resolved `layout.body`; it can still
fail, and the `RESIDUAL` line (`Screen.*` stuck at 640x480 in batchmode) prints either way. Whether
the next run reads `UI_CAPTURE_FIDELITY_OK` is **the next log's to say, not this row's**.

**Pinned by `ManageMockupConformanceRegression.CheckMeasuredGeometry`** (case 9, added this round;
`DataRegression.cs` untouched). ⛔ **It does ARITHMETIC, not token matching** — round 24 shipped with
every token present and the frames still wrong. Measured cases, against a **named** reference surface
derived from the log (`927px` panel / `758px` well / `1835px` band): hub card fill >= 0.50 and drawn
aspect within 0.02 of `HubCardAspect`; hub description band >= two lines at `FontFloorMobile`; 5x2 and
3x3 grid row coverage >= 0.95 of the band; tree row width fraction >= 0.55; the in-row control >=
`MinTouchPx` at every row height the clamp can produce; and the queue list seating >=
`QueueRowsVisibleTarget` whole rows with the row itself never under the floor.

### 4c. ROUND 26 — against the round-25 GATE, 2026-09-07

**Gate on round 25:** `COMPILE_GATE_OK`, `REGRESSION 440/441` (art ask only), **`UI_CAPTURE_FIDELITY_OK
16/16`** — the `ProveGeometryMoves` call was the whole of the degraded marker — and
`MANAGE_FLOW_MAP geometry 44 -> 6, touch 47 -> 0`. Evidence: `Builds/cap-manage-wave5.log`.

| # | Item | Measured | Fix |
|---|---|---|---|
| 1 | **the six remaining geometry failures** | `TEXT OFF PLATE ... 'Drawer_Header/Label' ("QUEUE") overflows its layout.body ZoneBacking by 112 ref px -- text y 313.2..425.2 vs plate y -444.8..313.2`, and the same for the X's label, on all three `*_queue` frames | ⛔ **Extending the drawer's own plate does NOT answer this rule, and that was worth reading before editing:** `UICaptureLaunch.ZoneBodyAbove` (`:5872-5877`) walks to the ancestor literally named `Zone_Body` and `PlateOf` (`:5880-5890`) takes **that** zone's `ZoneBacking` — not the nearest plate. The only conforming seat is inside the body. `ManageScreenPanel.cs:816-822` `SeatDrawerTitleOverlay` pivot **0 → 1** (grows DOWN into the drawer, not UP out of it) and `:568-586` `DrawerTitlePx` **0 → `DrawerTitleOverlayPx`** so the band is reserved. ⚠ **THE COST IS THE FIFTH ROW AND IT IS NOT HIDDEN:** list 614px → **502px**, four whole rows. Five need 612px; the body well is 758px against 256px of the mockup's own chrome (title 112 + tabs 128 + gaps 16). Nothing shrinks a row under `MinTouchPx` to reach five — a well above ~870px is what buys it back, and `SeatQueueListToWholeRows` WARNs the gap in px on every render |
| a | RESEARCH tree benefit ellipsised | *"Unlocks the Healing Fountain - restores the Hear..."* — on the perk whose benefit is longest, i.e. the one a player most needs the sentence for | `ManageWorkspacePanel.cs:930-946` — band deepened to two lines (`0.08/0.33` from `0.10/0.35`) and `FitSingleLine` → **`FitBlock(effect, ElarionUiKit.FontHardFloor, 26f)`**, which wraps and truncates visibly instead of substituting three dots. `:922-926` the band's own floor check now judges **two** lines, not one |
| b | picker row at the top, not centred | round 25's centring branch **could never fire** | `ManageWorkspacePanel.cs:707-719` — `viewportPx` fell back to the whole **band** whenever the overflow strip was not drawn, so `bandH - viewportPx` was 0. The viewport is now the rows that **exist** (`contentRows`), not the rows that **fit**: picker viewport 758 → 359, surplus **399px split above and below** |
| c | BUILD tiles lose their roofs | Archer Tower / Ballista / Cathedral cropped at the top | `ManageWorkspacePanel.cs:1236-1244` the portrait zone is the **whole cell** (it was `y 0.26..1`, i.e. 359x266 on a square 359px cell → a square sprite covered it and lost 93px split top and bottom; the name strip and state word carry their own dark plates and are painted later, so they ride **on** the art as mockup panel 2 draws). `:1891-1929` `SquarePortrait` gains a **width-fit, bottom-anchored** arm, chosen by arithmetic from the cell px the caller now hands in — a building sits on the ground, so surplus belongs **above** it. ⛔ **The fallback to the envelope crop is part of the rule:** ARMY's cell is 2.3:1, where a width fit is 566px tall in a 246px mask and would cut the troops' **heads** |
| d | queue row 1 has no icon while 2/4 do | the log was **clean** — nothing missing, nothing logged | ⛔ **Not an art gap and not a tier key.** `ManageArt.LoadSprite` already falls back tier → base. The cause is the **two-catalog split** `MakeJobRow` itself documents: a TOWER is not in `BuildingTierCatalog`, it resolves its NAME through `CatalogRegistry` — so `building` is null for it, the label came out right, and `PortraitKey`, guarded on `building != null`, came out **empty**. Every tower and wall row — the most common Builder job there is — asked for no art at all, and an empty key never reaches `ManageArt` for it to announce a miss. `ManageScreenVM.cs:899-919, 932-936, 993-1000` resolve `portraitId` in the **same branches** that resolve the label; `:962-971` a Builder row with no portrait identity now says so |

**Re-pinned.** `ManageMockupConformanceRegression.CheckMeasuredGeometry` gains `[research-tree-benefit-wraps]`,
`[research-picker-one-row]` (the viewport arithmetic), `[tile-art-whole-building]` and
`[queue-row-thumbnail]`; its queue floor is re-stated as **four seatable rows with the five-row
shortfall written into the case**, so nobody reaches five by shrinking a row. `ManageQueueDrawerRegression`
is **re-pointed with the ruling, not relaxed** — `[drawer-clear-of-card]`'s `titlePx > 0` inverts to
`titlePx >= MinTouchPx` plus the pivot pin, `[rows-inside-the-plate]` adds the title to its band sum
and now forbids a ceiling **above** the well; both parsers resolve a const authored as another const,
because `DrawerTitlePx = DrawerTitleOverlayPx` is one number rather than two that agree today.

5. APK built and installed **through the sanctioned scripts**.
6. **Push only on the owner's word** (CLAUDE.md §11). Commit local, by explicit path, sole committer.

## 7. WHAT NOT TO TOUCH

- **The imported art.** It is committed and verified at the filesystem level; do not rename, re-import or
  "tidy" it. The filenames are load-bearing — `ManageArt.BuildingPortraitKey` uses the catalog id verbatim.
- **`RaidSelectionSpoilsRegression`'s intent.** Re-point it with the ruling; **do not delete the suite** to
  make the gate green. Deleting an oracle to pass a gate is the failure this repo has an entire §12 about.
- Any other lane's uncommitted work. Consolidate by **explicit path**, never `git add -A` (§11).

## Lead addendum (2026-09-10 08:35) - open item 6 closed, item 2 re-run

Install proven: `Builds/wave5-install.runner.txt` reads `Performing Streamed Install` / `Success` (08:05:52); `adb shell dumpsys package com.denellestudios.echoesofelarion` read by the lead at 08:06 -> `versionName=2026.09.10.363660` (stamp commit dd8342230). Step ZERO `CatalogFallbackGenerator.Generate` re-run by the lead after this close-out (log `Builds/wave5-catfallback1`, marker recorded below when it lands).
Step ZERO result: `Builds/wave5-catfallback1` (08:34) `CATALOG_FALLBACK_GEN_OK wrote Assets/_Modules/Village/Catalog/Generated/CatalogFallbackData.g.cs ... rows=29 version=42 bytes=110625`; `git status` shows the generated file UNCHANGED, so the committed fallback data was already in sync with structures-catalog.json - open item 2 closed.

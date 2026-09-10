# WO-1670 - The town rail's Echoes chip encroaches on the QueueStatus band: cut the shared HudLayoutBands seam and re-author both mounts disjoint

**Status:** IMPLEMENTED - awaiting gate + capture (lane ECHO-CHIP 2026-09-10)
**Minted:** 2026-09-10 (lane ECHO-CHIP; number **1670 PRE-ASSIGNED by the lead** - this lane did NOT
edit `CLI_LANES_WO_NUMBERS.md`, per the brief. The banner bump is the lead's.)
**Silo / Lane:** HUD right-column geometry. File-disjoint from the DOCK-CAPTIONS lane and from
WO-1660/1662/1667 - see 2c: **this ticket touches ZERO lines of `HudKitController.cs`.**
**Severity:** P2 layout. Two live HUD bands share screen area on every town frame.
**Type:** EXISTING system.
**Owner ruling (12:16, 2026-09-10):** the Echoes chip encroaches 0.022 of the canvas into the
QueueStatus band - **fix it NOW via a `HudLayoutBands` seam.** This ruling is the authority that
releases WO-1642 §4's explicit refusal to move a felt-tested placement (*"Re-seating a placement the
owner felt-tested (2026-07-24) is a ruling, not a comment repair"*). The ruling is now on the table.

---

## 1. The measurement this ticket exists for

### 1a. Carried forward from WO-1642's RESULT, §6 item 2 (raised, not fixed)

`WorkOrders/WORK_ORDER_1642_town_chrome_raids_card_white_corners_and_the_attack_report_chip_off_plate.RESULT.md:260-266`,
verbatim:

> **The Echoes chip encroaches on the QueueStatus mount by ~0.022 of screen height.** Its fixed
> 112 px box at centre 0.475 occupies 0.418..0.532; QueueStatus's bottom is 0.510. No pixel overlap
> observed at this frame. The clean cure is a shared band in `DeNelle.Core.UI.HudLayoutBands` - the
> seam WO-1436 and WO-1464 already cut for the raid deploy bar and the move stick - so the Village
> chip READS the band instead of restating it. Structural, and it needs the owner.

and §6 item 1, the other half:

> **`HudAreasHost.cs` carries the SAME stale 0.420 in its own comments** (the QueueStatus block:
> *"below System (.88), above the ActionRail top (.42)"*, *"Still clear of ActionRail (tops 0.420)"*)
> while it authors ActionRail at 0.770..0.965 five lines away.

and §6 item 4(ii), the coverage gap:

> nothing models the THIRD element of the QueueStatus stack against the Echo chip's fractional band.

### 1b. Re-measured at source THIS session, not copied (CLAUDE.md §11B)

Every input below was read out of the tree at HEAD `e4b5906a5`:

| fact | source, opened 2026-09-10 |
|---|---|
| Echo chip band centre `0.475f`, fixed 112 px tall, 220 px wide, 54 px right inset | `Assets/_Modules/Village/Harvest/EchoUnlockFeedback.cs:77,82,474-489` |
| Echo chip canvas: ScreenSpaceOverlay, ref 1080x1920, match 0.5 | `EchoUnlockFeedback.cs:396-402` |
| QueueStatus mount `(0.780, 0.510)..(0.995, 0.750)` | `Assets/_Modules/HUD/Kit/HudAreasHost.cs:175` |
| ActionRail mount `(0.780, 0.770)..(0.995, 0.965)` | `HudAreasHost.cs:130` |
| the two stale `0.420` / `.42` ActionRail comments | `HudAreasHost.cs:167,173` |
| rail chip box 220 x `MinTouchPx`, gutter `ElarionUi.PadPanel * 3f` = 54 | `HudKitController.cs:1876` (read-only) |
| `ElarionUi.PadPanel = 18f` | `Assets/_Modules/Core/UI/ElarionUi.cs:194` |
| the QueueStatus occupants and their y-derivation | `HudKitController.cs:2013-2065` (Collectors), `:2107-2140` (Defense Report), `HudRailClearance` `:5757-5890` (read-only) |
| `HudLayoutBands` exists, and its idiom (`MoveClusterMount`, `ThumbBandClearanceGap`, `ResolveNightMarketCard`, `Intersects`, `CanvasReferenceSize`) | `Assets/_Modules/Core/UI/HudLayoutBands.cs:224-232, 288, 348-360, 443-447, 380-390` |

### 1c. THE ARITHMETIC, at both authored aspects. This is the RED proof.

The canvas reference size is Unity's own log-weighted MatchWidthOrHeight formula
(`HudLayoutBands.CanvasReferenceSize`, mirrored by `HudUiRegression.CanvasRefHeight`). Computed this
session:

| aspect | canvas ref size | 1 ref px as a y-fraction |
|---|---|---|
| 2670x1200 (the owner's Seeker) | 2148.0 x 965.4 | 0.001036 |
| 1920x1080 | 1920.0 x 1080.0 | 0.000926 |

**RED #1 - the Echo chip vs the QueueStatus MOUNT (the owner's 0.022):**

| aspect | Echo band at centre 0.475 | QueueStatus mount | overlap |
|---|---|---|---|
| 2670x1200 | **0.4170 .. 0.5330** | 0.510 .. 0.750 | **0.0230** |
| 1920x1080 | **0.4231 .. 0.5269** | 0.510 .. 0.750 | **0.0169** |

0.0230 is the owner's "0.022", reproduced from source rather than quoted.

**RED #2 - and it is worse than a mount overlap: two LIVE bands already share screen.** The
QueueStatus mount's occupants at rest (no resource panel open) are the **Collectors chip** and the
**ATTACK REPORT chip**, each a 112 ref px `BuildRailChip` band stacked with `RailGapPx = 6`, hung from
the mount's top edge and sharing the Echo chip's exact 220 px right gutter (`RailGutterPx` ==
`ElarionUi.PadPanel * 3f` == the Echo chip's own 54 px inset - `HudKitController.cs:5631-5633` says so
in as many words). Resting geometry, ref px from the canvas top:

| aspect | Collectors | ATTACK REPORT | its bottom as a y-fraction | Echo top today |
|---|---|---|---|---|
| 2670x1200 | 241.3 .. 353.3 | 359.3 .. **471.3** | **0.5118** | **0.5330** -> **0.0212 of overlap** |
| 1920x1080 | 270.0 .. 382.0 | 388.0 .. **500.0** | **0.5370** | **0.5269** -> **0.0101 of overlap** |

x is identical for both (the shared gutter), so this is real shared area, not a near-miss.
⚠ **WO-1642 measured the PLATES and correctly reported no pixel collision** (defence plate ends at
device row 545, Echo plate starts at 585). That is because `MedievalUiSkin`'s sprite ink occupies only
0.145..0.736 of its band. **The BANDS overlap; the INK does not.** Both statements are true, and the
band overlap is the one an oracle can hold.

*Model validation - the same arithmetic reproduces WO-1642's device measurements to within ~4 device
px, so it is not a theory:* predicted Echo ink 580.6..662.9 device rows vs measured **585..658**;
predicted ATTACK REPORT ink 467.0..549.3 vs measured **471..545**
(`Builds/device-frames/2026-09-10_0602_town.png`, 2670x1200).

**GREEN, after the fix:** the chip's band is authored by its **TOP edge** at
`QueueStatusMount.yMin - ThumbBandClearanceGap` = **0.500 at every aspect** (a pure fraction; only the
box below it is pixels). Band = 0.3840..0.5000 at 2670x1200, 0.3963..0.5000 at 1920x1080.
Clear of the mount by 0.010 (> `HudLayoutBands.Epsilon` 0.0005) and of the resting ATTACK REPORT chip
by 0.0118 / 0.0370. Nothing else occupies the right column below 0.510 in a peaceful posture
(`ActionBar` is x 0.270..0.730; `MoveCluster` is x 0.010..0.270; the Echo chip is x 0.872..0.975).

---

## 2. What is wrong structurally - and the correction to this ticket's own brief

### 2a. The defect class is duplicated state, for the fifth time

`EchoUnlockFeedback.cs:77` restates a neighbour's geometry it cannot see. Its own doc comment
(`:52-76`) already diagnoses itself, verbatim: *"⛔ DO NOT WRITE A MOUNT RECT INTO THIS FILE AGAIN.
The only cure for the copy is deleting it: this chip lives in `DeNelle.Village` and may not reference
`DeNelle.HUD` (CLAUDE.md §5), so having it READ the band needs a shared table in
`DeNelle.Core.UI.HudLayoutBands`."* That is the fix, written down by the previous lane and blocked
only on the ruling that now exists.

### 2b. The seam already exists; this is its fourth tenant

`DeNelle.Core.UI.HudLayoutBands` was cut for exactly this shape:
WO-1219 (the left column), WO-1436 (`ThumbActionRowMinY/MaxY` - the ability row vs the raid deploy
bar), WO-1464 (`MoveClusterMount` - the stick vs the raid tray). Each time the two surfaces sat in
assemblies that cannot see each other, so the number became shared DATA in `DeNelle.Core`. Same here:
`DeNelle.HUD` authors QueueStatus, `DeNelle.Village` authors the Echo chip, and
`DeNelle.Village -> DeNelle.HUD` is the one cross-assembly reference CLAUDE.md §5 says is actually
enforced by asmdef. Both already reference `DeNelle.Core`.

### 2c. ⚠ CORRECTION TO THE BRIEF: the Echo chip is NOT mounted in `HudKitController`

The lead's brief named *"HudKitController's Echo chip mount"*. It is not there.
`grep -rn "EchoChipBandCentreY|0\.475f" --include=*.cs Assets/` returns exactly one authoring site:
**`Assets/_Modules/Village/Harvest/EchoUnlockFeedback.cs:77` and `:474-475`** (plus
`HudObsidianShowcaseSceneBuilder.cs:214`, an unrelated editor showcase rect that happens to contain
`0.475f`, and the `HudUiRegression` pin). `HudKitController.cs` builds the three RAIL chips
(Collectors / ATTACK REPORT / the retired Builders) but **not** the Echoes chip.

**Consequence, and it is a good one:** this ticket touches **zero lines of `HudKitController.cs`**. It
is file-disjoint from WO-1660/1662/1667 and from the DOCK-CAPTIONS lane's
`BuildAdaptivePeacefulDock` / `HudDockSlotLayout` work by construction, not by care. There is no
3-way merge risk in that file from this lane.

---

## 3. Producers and consumers

| file | role | change |
|---|---|---|
| `Assets/_Modules/Core/UI/HudLayoutBands.cs` | **THE SEAM** (`DeNelle.Core.UI`) | ADD the right-column section: `QueueStatusMount`, `EchoChipWidthPx/HeightPx/EdgeInsetPx`, `EchoChipTopY`, `ResolveEchoChip(w,h)` |
| `Assets/_Modules/HUD/Kit/HudAreasHost.cs` | consumer, `DeNelle.HUD` | `Add(HudArea.QueueStatus, HudLayoutBands.QueueStatusMount)` (the `MoveClusterMount` idiom); FIX the two stale `0.420` / `.42` ActionRail comments |
| `Assets/_Modules/Village/Harvest/EchoUnlockFeedback.cs` | consumer, `DeNelle.Village` | DELETE `EchoChipBandCentreY` + `EchoChipWidthPx`; anchor the chip by its TOP off `HudLayoutBands.EchoChipTopY` |
| `Assets/Editor/Regression/HudUiRegression.cs` | the ORACLE | re-point the 6e source pin; re-point `ReadAreaBandY`'s QueueStatus read (see §5); ADD check 11 `[right-column-bands]` |

**Read-only, cited but not changed:** `HudKitController.cs` (`BuildCollectorsChip`,
`BuildDefenseReportChip`, `HudRailClearance`, `HudRailGutter`, `RailGutterPx`),
`ElarionUiKit.cs`, `ElarionUi.cs`, `MedievalUiSkin.cs`, `hud-areas.json`.

---

## 4. The design: author the chip's TOP EDGE, not its centre

⭐ **A CENTRE FRACTION CANNOT PROMISE A TOP EDGE.** That is the whole bug. The chip is a FIXED 112
ref px box centred on a fraction, so its top edge moves with the device: 0.5330 at 2670x1200,
0.5269 at 1920x1080. No single centre value can be disjoint from a fractional band at every aspect
without leaving slack at some of them.

Author the **TOP** instead - the `ResolveNightMarketCard` idiom this file already uses ("hangs from
the mount's top-left at a fixed reference size"):

```
EchoChipTopY = QueueStatusMount.yMin - ThumbBandClearanceGap      // 0.510 - 0.010 = 0.500
anchorMin = anchorMax = (1f, EchoChipTopY);  pivot = (1f, 1f);
anchoredPosition = (-EchoChipEdgeInsetPx, 0f);
sizeDelta = (EchoChipWidthPx, EchoChipHeightPx);
```

The top edge is then **exactly 0.500 at every resolution** and the box hangs down. Disjointness stops
depending on the aspect.

- **Reuse `ThumbBandClearanceGap` (0.010).** Do NOT mint a second clearance constant - one number, so
  two neighbours never drift apart. That is what it is for.
- **`EchoChipHeightPx = ElarionUiKit.MinTouchPx`** verbatim, so `ClampMinTouch` stays a no-op. WO-868
  is the record of what happens when it is not: symmetric growth pushed a corner button off-screen.
- **`EchoChipEdgeInsetPx = ElarionUi.PadPanel * 3f`** - the SAME expression as
  `HudKitController.RailGutterPx`, so the four right-column faces keep one right edge. Never a raw 54.
- **`EchoChipWidthPx = 220f`** - canon, `== RailChipWidthPx`, pinned by `HudLabelFitRegression:148-149`
  and `HudUiRegression:1754`.

**The chip MOVES DOWN by 0.033 of screen height at the Seeker (~32 device px).** That is the ruling
being executed, and it opens the gap to the ATTACK REPORT chip rather than closing anything.

---

## 5. ⚠ The pin that would have gone SILENT, and must be re-pointed in the same change

`HudUiRegression.ReadAreaBandY` (`:1435-1450`) does **not** read the band from `HudLayoutBands`; it
**regex-parses the literal out of `HudAreasHost.cs` source text**:

```
Add\(\s*HudArea\.QueueStatus\s*,\s*new\s+Vector2\(...\)\s*,\s*new\s+Vector2\(...\)
```

and on a miss it **NOTES and returns false**, which makes checks 7 and 8 skip their entire layout
half. Its own comment predicts this ticket: *"it may have moved to a band table - the layout half of
this check is SKIPPED rather than run on a guess"*. Moving QueueStatus into the seam without
re-pointing this read would silently retire the WO-1435 Harvest-clearance geometry. **Re-point the
QueueStatus call site at `HudLayoutBands.QueueStatusMount` directly** (`DeNelle.EditorRegression.asmdef`
already references `DeNelle.Core`); leave the ActionRail parse alone, it is still a literal.

Also re-point:
- **6e** (`:947-950`) asserts the string `EchoChipBandCentreY` is present in `EchoUnlockFeedback.cs`.
  That const is being DELETED - the copy is the bug. Re-point it to the seam symbol
  (`HudLayoutBands.EchoChipTopY`) so the invariant it protects ("the chip is docked on a named band,
  not free-floating") survives while the copy dies. ⛔ Do not simply delete the assert.

---

## 6. Acceptance criteria

1. **`DeNelle.Core.UI.HudLayoutBands` is the ONLY authoring site** for the QueueStatus mount and the
   Echoes chip's band. `grep -n "0\.475f\|0\.510f\|EchoChipBandCentreY" Assets/_Modules/` returns
   nothing outside `HudLayoutBands.cs`.
2. **RED-FIRST, on the two authored bands** (the gap WO-1642 §6.4(ii) named): a new
   `HudUiRegression` check 11 `[right-column-bands]` resolves the **real** `HudAreasHost` QueueStatus
   mount and `HudLayoutBands.ResolveEchoChip` at **2670x1200 and 1920x1080** and FAILS on any
   intersection. ⚠ The existing capture oracle's *"no overlapping sibling buttons"* already passes
   here and always would - the two are on **different canvases** and are not siblings; that is
   precisely why this rule has to be band arithmetic and not a capture rule.
   - **It must be seen RED against the pre-fix authoring**: reverting `EchoUnlockFeedback.cs` to HEAD
     (centre 0.475) reds it with 0.0230 / 0.0169 of overlap - the numbers in §1c.
3. Check 11 also asserts the **resting rail stack fits inside its own mount** (Collectors + ATTACK
   REPORT + `RailGapPx`, derived from source the way check 8 does), so mount-disjointness actually
   implies chip-disjointness rather than being assumed to.
4. **The two stale comments in `HudAreasHost.cs` say 0.770**, the ActionRail's real bottom edge.
5. **The touch floor and the 220 px right edge are untouched** - `MinTouchPx` height, 220 width,
   `PadPanel * 3` inset. A "fix" that shrinks the chip fails 8d.
6. `python tools/gate_brace.py` clean + no NUL bytes on all four `.cs` files.

## 7. What NOT to touch

- ⛔ **`HudKitController.cs` - not one line** *(for the geometry half; the 12:55 ruling in §8.0
  re-opened it for the chip's VISIBILITY only — a 4-line body change plus one ring in
  `SetResourcePanelOpen`, both far from WO-1660/1662/1667 and from
  `BuildAdaptivePeacefulDock` / `HudDockSlotLayout`).* Nothing else in that file is touched.
- ⛔ **The Echo chip's WIDTH, HEIGHT, right inset, label, font or plate.** Only its y-anchoring moves.
- ⛔ **`HudLayoutBands.RaidReadoutBand`.** Its `yMin` is also 0.510 and it is tempting to derive it
  from `QueueStatusMount.yMin` - do NOT. It is a hostile-posture band that deliberately TAKES the
  ActionRail + QueueStatus seat (its own doc says so); coupling them would make a raid-side change
  move a town band. Noted as a candidate, deliberately not taken.
- ⛔ `hud-areas.json` (either copy), `ElarionUiKit`, `ElarionUi`, `MedievalUiSkin`, `HudDockSlotLayout`,
  `BuildAdaptivePeacefulDock`.
- ⛔ `HudObsidianShowcaseSceneBuilder.cs:214` - its `0.475f` is an unrelated editor-showcase rect.
- ⛔ Do not commit, do not run Unity. The lead gates and merges.

---

## 8. Raised, then RULED — the expanded-panel item is now in scope

### ⭐ 8.0 OWNER RULING 2026-09-10 12:55, on §8.1 below: **HIDE the ATTACK REPORT chip while the
### resource panel is expanded. It returns on collapse.**

Option (ii) of the three §8.1 offered. Implemented in this ticket (see §8.0a) — §8.1 is kept below,
unedited, as the measurement the ruling was made on.

**8.0a — where it is implemented, and the one rule that governs it.**
`HudKitController.TickDefenseReportChip` (`Assets/_Modules/HUD/Kit/HudKitController.cs:2198`) holds
the **ONLY** `_defenseChipBand.gameObject.SetActive(` in the codebase (verified: `grep -c` returns
**1**). WO-1515's own header states the reason in as many words: *"a second object is how a widget
ends up permanently off in exactly one posture."*

- ⛔ **The panel state is an INPUT to that one writer, never a second writer.** A hide written from
  `SetResourcePanelOpen` (`:4309`) would race the tick's 0.5 s throttle and the chip would flicker
  back on over the open panel.
- ⚠ **The change detector has to see the panel too.** `DefenseReportChipModel.Current.Key` does NOT
  move when the player toggles the panel, so keying the early-out on it alone would never
  re-evaluate and the chip would stay on screen forever. New field `_defenseChipPanelWasOpen` folds
  the panel into the SAME detector.
- **The edge is RUNG, not polled.** `SetResourcePanelOpen` clears the throttle and calls the one
  writer — the identical idiom, two lines above, that it already uses for
  `HudRailClearance.MarkDirty()`. The hide lands on the expand frame.
- **The report is not consumed.** `snap.Visible` is never overwritten; the panel only SUPPRESSES an
  otherwise-visible chip, so a report that lands while the panel is open is still unread and appears
  on collapse.
- **`FlowTrace.Step` on each hide/show edge**, naming the direction, the model's `Visible` and the
  resulting on-screen state.
- **Pinned by check 11e** (four asserts: the panel read, the `wantVisible` argument, the
  `_defenseChipPanelWasOpen` detector, and `writers == 1`) plus 11e-3 on the ring.
  **RED verified against `HEAD` source text this session** — all four are absent pre-ruling.

**Also retired in the same change:** the third stale band copy at `Assets/Editor/UICaptureLaunch.cs`
(§8.2) now reads `HudLayoutBands.QueueStatusMount`.

---

1. ⭐ **THE EXPANDED RESOURCE PANEL STILL PUSHES THE RAIL CHIPS OUT OF THEIR MOUNT AND INTO THE
   ECHO CHIP'S BAND. This ticket does not close that, and the number is stated rather than implied.**
   *(⚠ SUPERSEDED by the 12:55 ruling in §8.0 — kept verbatim as the measurement it was made on.)*
   `HudRailClearance` derives each rail chip's y from the laid-out resource panel above it and
   explicitly permits leaving the mount (`HudKitController.cs:5860-5863`: *"The chip may legitimately
   hang below its own mount (the QueueStatus band is a mount point, not a clip rect)"*). With the
   panel expanded at 2670x1200 the ATTACK REPORT chip derives to:

   | resource rows | ATTACK REPORT band, ref px from canvas top | its bottom as a y-fraction | vs the new Echo band 0.384..0.500 |
   |---|---|---|---|
   | 3 | 453.8 .. 565.8 | 0.4139 | overlaps 0.414..0.500 |
   | 4 (today's kinds) | 514.8 .. 626.8 | 0.3507 | overlaps 0.384..0.500 (total) |
   | 6 | 636.8 .. 748.8 | 0.2244 | overlaps 0.384..0.500 (total) |

   It overlapped the OLD band too, so this is pre-existing and not a regression introduced here.
   **No authored fraction can fix it** - the rail's depth is a runtime function of `kinds.Length`,
   and the Echo chip is on a different canvas in an assembly that cannot see `HudRailClearance`
   (`internal` to `DeNelle.HUD`). The honest options are (i) reserve the rail's WORST-CASE depth in
   the seam, which at 6 rows pushes the Echo chip's top to ~0.218 and makes the DEFAULT screen worse
   for a transient state, (ii) hide the Echo chip while the resource panel is open, or (iii) move it
   off the right gutter entirely. All three are owner calls. **Not chosen here.**
2. **`UICaptureLaunch.cs:4714` carries a THIRD stale copy of this band** - `(0.780, 0.530)..(0.995,
   0.865)`, described in its own comment as *"the exact `HudArea.QueueStatus` band geometry"*. It is
   neither 0.510..0.750 (today) nor 0.420 (the retired value): a fixture rendering a band the game
   does not have. Out of this ticket's scope by §7; it should read the seam.
3. **`HudKitController.cs:5633` cites `EchoUnlockFeedback.cs:381`** for the 54 ref px inset. That
   const moves to the seam in this change, so the citation goes stale. Left alone deliberately -
   §7 forbids touching that file, and the seam documents the equality from its own side.
4. **`ReadAreaBandY` is now a one-caller helper** (ActionRail only). Left in place; collapsing it is
   noise in a merge-sensitive week.

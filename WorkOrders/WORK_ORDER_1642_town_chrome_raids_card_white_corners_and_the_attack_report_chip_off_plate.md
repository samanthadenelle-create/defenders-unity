# WO-1642 - Town chrome: the RAIDS deck card has white corner patches, and the ATTACK REPORT chip's third line falls off its own plate into the Echoes chip's gap

**Status:** IMPLEMENTED - awaiting gate + capture (lane TOWN-CHROME 2026-09-10)
**Minted:** 2026-09-10 (CLI minting lane, main-line banner; number block 1637-1642 pre-assigned by the
lead, banner bumped 1637 -> 1643 in the SAME edit)
**Silo / Lane:** HUD chrome. Item A is an ART re-export plus the retirement of a code workaround
(`Assets/_Modules/HUD/PlayerDeckWorkspace.cs`); item B is the right-hand rail stack
(`Assets/_Modules/HUD/Kit/HudKitController.cs` and `Assets/_Modules/Village/Harvest/EchoUnlockFeedback.cs`).
**Severity:** P2 felt. Both are on screens the player sees every session, and both read as unfinished.
**Type:** EXISTING system, both.
**Owner words:** *"i have mentioned it in testing that it feels incomplete and not polished"*
(2026-09-10).

**Two findings, one ticket, by the lead's instruction.** They share nothing but the word "chrome". If
the lane wants to split them it may, and it should say so.

---

## 1. ITEM A - the RAIDS deck card's white corners

### 1a. The frame

`Builds/device-frames/2026-09-10_0603_journey_deck.png` (2670x1200, build 363529). Two cards,
`QUESTS` and `RAIDS`. Cropped and enlarged this session from box `(1330,300)-(2350,560)` of the
original: the RAIDS card shows an **opaque near-white triangular patch at each of its four corners**,
outside the ornate rounded frame painted into the art but inside the card's rectangle. The QUESTS card
beside it, same size and same builder, shows none.

### 1b. It is not a tint bug and there is no separate corner element - the white is in the PNG

The card face is a **raw `Image` with `Image.Type.Simple`**, not a nine-slice and not a rounded-rect
kit helper:

- `Assets/_Modules/HUD/PlayerDeckWorkspace.cs:170` `BuildCard`; Journey card list at `:798-853`.
- `:176-179` the host is `ElarionUiKit.BuildObsidianButton`.
- `:183` `MedievalUiSkin.ApplyButton(...)` puts the kit plate on the button's own `Image`
  (`MedievalUiSkin.cs:50-72`; note `:66`, the kit explicitly sets `Image.Type.Simple` for this plate).
- `:219-220` that plate is then **thrown away** on the illustrated path:
  `cardImage.sprite = null; cardImage.color = Color.clear;`
- `:230-238` the visible surface is one child Image, `IllustratedCardSurface`, tinted `Color.white`
  when available (`:232`), `Image.Type.Simple`, `preserveAspect = false` (`:237`), plus
  `Selectable.Transition.ColorTint` with `normalColor = Color.white` (`:242-256`).

**So the ornate border is painted into the PNG, the tint is white by design, and there is no child
frame element that could be left untinted.** The white corners are pixels of `raids.png`.

`Assets/Resources/UI/ElarionMedieval/cards/raids.png` is 1774x887 and its border is **opaque
near-white** - an authoring-tool checkerboard flattened into the export:

| sample (x, y) | `raids.png` | `quests.png` |
|---|---|---|
| (0, 0) | (253, 253, 252, 255) | (0, 0, 0, 0) |
| (1773, 0) | (242, 243, 241, 255) | (0, 0, 0, 0) |
| (0, 886) | (244, 244, 244, 255) | (0, 0, 0, 0) |
| (1773, 886) | (207, 204, 205, 255) | (0, 0, 0, 0) |

### 1c. It is already half-fixed, and the residual is geometric

`PlayerDeckWorkspace.cs:504-537` carries an `OpaqueMargins` table with a row written for exactly this
file:

    // cards/raids.png      1774x887 - checkerboard border, art bbox (49,63)-(1726,809)
    new OpaqueMargin { Key = "raids", Width = 1774, Height = 887,
                       Left = 49, Top = 63, Right = 48, Bottom = 78 },

consumed at `:642` and turned into over-scaled anchors at `:665-672`.

**That crop is a RECTANGLE. The frame inside it is rounded and ornate, so the checkerboard survives in
the four corner triangles inside the ink bounding box.** Measured on the PNG, inside the authored bbox
`(49,63)-(1726,809)`, counting pixels with alpha 255 and min(RGB) > 200 in a 60x60 block at each
corner: TL 1219, TR 1223, BL 1289, BR 1071 of 3600 - **roughly 30-36% of each corner block.** That is
the white patch in the frame. The earlier fix turned "a pale band all the way round" into "pale
corners"; it did not remove the checkerboard.

### 1d. Two side-facts the lane needs

- **The derived fit is non-uniform.** x span 0.9453 -> scaleX ~1.058; y span 0.8410 -> scaleY ~1.189,
  with `preserveAspect = false` (`:237`). **The RAIDS art is stretched about 19% vertically relative
  to QUESTS.** A correct re-export fixes this too, because the identity path takes over.
- **The LOCKED face is clean.** `raids-locked.png` (1416x742, `LockedArtKey` at `:838`) samples
  (0,0,0,0) at all four corners - properly alpha'd. **The defect is only on the unlocked RAIDS face**,
  i.e. only once a Barracks stands. That is why it went unseen for so long.

### 1e. QUESTS vs RAIDS - the code is identical

`:803-806` vs `:828-841`. Both flow through the same `BuildCard` branch at `:209-256`. The only
differences: RAIDS carries a `LockedArtKey` (`:838`) and a different `Available` predicate
(`PostureSignals.RaidCapable`, `:839`, vs `PanelRouter.IsRegistered`, `:805`) - both irrelevant while
available. The two PNGs are the same 1774x887 with byte-identical importer settings
(`raids.png.meta:24,48,54,55,57` against `quests.png.meta:24,48,54,55,57`). **The difference is
entirely in the delivered pixels**, and the code says so itself at `:520-521`.

---

## 2. ITEM B - the ATTACK REPORT chip's third line falls off its plate

### 2a. The frame - and a premise correction

Frames `Builds/device-frames/2026-09-10_0602_town.png` (before the raid) and `..._0625_back_in_town.png`
(after). Right-hand column, below the `Harvest` chip. Cropped and enlarged this session from
`(2280,430)-(2670,680)` of `..._0625`:

- A dark rounded plate sits at roughly y **480-545** in original pixels.
- `ATTACK` renders at roughly y **455-485** - its glyphs start about 25 px **ABOVE** the plate's top
  edge, over the town wall.
- `REPORT` sits inside the plate.
- `HELD` renders at roughly y **548-575** - **BELOW** the plate's bottom edge, again over the wall.
- The `Echoes 2/6` chip's plate begins at roughly y **593**.

**The brief, and WO-1632's own line 574, said "ATTACK REPORT HELD overlaps the Echoes 2/6 chip". That
is not what the pixels show and the correction matters.** What is measured is a **three-line label in
a plate sized for about one and a half lines**, escaping BOTH edges, with the bottom line landing in
the roughly 18-px gutter above the Echoes chip. There is no proven pixel overlap between the two
chips at this frame. The defect is text-off-plate - which is the harness's own RULE 1 class
(`Assets/Editor/UICaptureLaunch.cs:5851-5872`) - not a collision.

### 2b. Where the words come from

Composed, not typed whole. `Assets/_Modules/Core/HudModel/DefenseReportChipModel.cs`:

- `:57` `public const string TitleLine = "ATTACK REPORT";`
- `:66-72` `OutcomeWord`: `"OVERRUN"` / `"BREACHED"` / default `"HELD"`.
- `:89-90` `snap.Caption = snap.Visible ? TitleLine + "\n" + OutcomeWord(newestUnread) : string.Empty;`

**An explicit `\n`, so the caption is always at least two lines** - and `ATTACK REPORT` itself wraps,
making three. The joined string exists in no json; canon-strings does not carry it.

### 2c. The two chips' geometry, and the stale comment that explains the drift

**The defense chip** - `Assets/_Modules/HUD/Kit/HudKitController.cs`, `BuildDefenseReportChip`
`:2003-2069`, called at `:824`:

- `:2018-2020` -> `BuildRailChip` `:2161` -> `RailBand` `:2199-2210`: anchored top-right, pivot (1,1),
  `sizeDelta = (widthPx, heightPx)`, `anchoredPosition = (0, -yFromTopPx)`.
- Box: `:1788` `RailChipHeightPx = ElarionUiKit.MinTouchPx` (**112**), `:1790`
  `RailChipWidthPx = 220f`, `:1801` `RailGutterPx = ElarionUi.PadPanel * 3f` (**54**).
- Mount: `Assets/Resources/Data/Canonical/hud-areas.json:41-46` puts `defenseReportChip` (and
  `collectorsChip`) in area `queueStatus`; `Assets/_Modules/HUD/Kit/HudAreasHost.cs:175`
  `Add(HudArea.QueueStatus, new Vector2(0.780f, 0.510f), new Vector2(0.995f, 0.750f));`
- **It is positioned DYNAMICALLY.** The `0f` at `:2019` is a resting BASE, not a position (`:2010-2013`).
  The real y comes from `HudRailClearance` at `:2023-2032`, rule stated at `:5533-5534`:
  `chipTopFromMountTop = max(authored base, (mountTop - sourceBottom) + RailGapPx)`.
  It is conditional: `:2033` `band.gameObject.SetActive(false);`, turned on only by
  `TickDefenseReportChip` (`:2094-2112`) off `DefenseReportLedger.UnreadCount()`.

**The Echoes chip** - `Assets/_Modules/Village/Harvest/EchoUnlockFeedback.cs`:

- `:353` `_chipLabel.text = $"Echoes {svc.EchoCount}/{svc.MaxEchoes}";`
- On its **own canvas**, `BuildPip` `:368-381`: `ScreenSpaceOverlay`, `sortingOrder = 700`, reference
  1080x1920, match 0.5.
- `:453-469`: anchored to a **fraction** y-centre with a **fixed-px** box -
  `:56 EchoChipBandCentreY = 0.475f`, `:60 EchoChipWidthPx = 220f`, height `MinTouchPx`, right inset
  `ElarionUi.PadPanel * 3f` (54).

**x is identical by construction** - both 220 ref px wide with the same 54 ref px edge inset - and the
code says so at `HudKitController.cs:5437`: *"the same 54 ref px EchoUnlockFeedback.cs:381 authors for
the Echoes chip, which lives on a different canvas entirely and cannot be edited from here."*

STOP: **The static tell, and it needs no derivation:** the Echo chip's own comment (`:448-452`) justifies
`0.475` by citing *"QueueStatus's bottom (0.530)"* and *"ActionRail's top (0.420)"*. **Both citations
are stale.** Live in `HudAreasHost.cs` today: `:175` QueueStatus = `0.780,0.510 .. 0.995,0.750`;
`:130` ActionRail = `0.780,0.770 .. 0.995,0.965`. The chip's authored top (~0.532) is **inside** the
QueueStatus mount by about 0.022 of screen height. **The "one free band between 0.420 and 0.530" the
chip claims to be docked in no longer exists.** That is duplicated state going stale exactly the way
CLAUDE.md sec.2, sec.5 and sec.16 each describe, and it is the finding worth fixing whether or not
the two chips ever touch.

### 2d. Why nothing caught it

Four independent reasons, most certain first:

1. **The defense chip is INACTIVE in an edit-mode capture.** `HudKitController.cs:2033`; only the
   runtime poll turns it on (`:2104-2106`). `LayoutOracle.cs:134` calls
   `GetComponentsInChildren<Button>(false)` - **active-only**. The chip is never a candidate.
2. **`HudRailClearance` never runs.** It positions in `LateUpdate`, and the harness states this exact
   class of problem itself at `UICaptureLaunch.cs:5874-5877`.
3. **The two chips are never on one audited canvas.** `AuditGeometry` takes ONE `canvasGo`
   (`UICaptureLaunch.cs:5893-5896`, invoked `:5775`). The town capture `CaptureAdaptiveHudOnce`
   (`:2980-3070`) renders the `HudKitController` canvas and never instantiates `EchoUnlockFeedback`;
   the Echo chip has its own separate capture, `CaptureEchoRosterOnce` (`:3311-3346`).
4. **Routing softens the one rule that would fire.** `ButtonsOverlap` (`LayoutOracle.cs:164-172`)
   records `SameParent`, and `UICaptureLaunch.cs:5982-5983` routes a cross-parent finding to
   `crossFails` - a touch marker, **not the gate**. `ButtonOverText` (`:177-196`) hard-codes
   `sameParent: true` at `:192`, so it would red on `"Echoes 2/6"` - but only if 1-3 were resolved.

**For item A there is no rule at all:** `LayoutOracle`'s finding kinds (`:70-72`, Assert C at `:244`)
are `ButtonsOverlap`, `ButtonOverText`, `SubTouchFloorBand`, `TextTruncated`, `TextUnmeasured`. **A
white pixel inside a correctly-placed rect is invisible to every one of them.**

---

## 3. What is NOT claimed

- **NOT claimed the two chips overlap in pixels.** Sec.2a. They may under other conditions (see
  below); this frame does not show it.
- **NOT claimed how far the stack drops in general.** The defense chip stacks UNDER the Collectors
  chip via `AddSource` (`:2030`), so the geometry depends on whether Collectors is live, on how many
  resource rows are expanded (`ResRowHeightPx = 56f`, `ResRowGapPx = 5f`, `:1811-1812`), and on the
  aspect - a fixed-px stack eats a larger FRACTION on a short canvas, the classic pairing documented
  at `:5546-5549`. **Any overlap number is DERIVED, not measured, and must be labelled as such** (the
  discipline `LayoutOracle.cs:216-223` states for itself).
- **NOT claimed which chip would paint over which.** The Echo canvas is `sortingOrder = 700` and the
  comment at `EchoUnlockFeedback.cs:376` says "above gameplay HUD", so the Echoes chip would be
  expected to win - **inferred from sorting order, not observed.**
- **NOT claimed the RAIDS art is the only affected card.** Only `raids.png` and `quests.png` were
  sampled. The other deck kinds were not.
- **NOT proven which of the three localization data homes serves the device.** Not relevant to either
  item here, but noted because item B's caption is composed in C# and is therefore NOT localized at
  all - a separate question nobody has asked, raised here and not answered.

---

## 4. The fix

### Item A - re-export the art; the workaround retires itself

The fix is a **re-export of `raids.png` with a transparent margin**, matching `quests.png`. Once the
margin is alpha, the sprite takes the alpha/tight-mesh route, the derived fit becomes the identity,
the 19% vertical stretch (sec.1d) goes away, and the corners are gone because there is nothing opaque
left to survive a crop.

**Then DELETE the `OpaqueMargins` row for `"raids"`** (`PlayerDeckWorkspace.cs:504-537`). The oracle
that governs this is written to be self-retiring and says so at
`Assets/Editor/Regression/HudLabelFitRegression.cs:990-995`: a re-export with alpha means the row MUST
be deleted. **Leaving both is the duplicated state this repo keeps paying for.**

STOP: **The owner is colourblind (memory `owner-colorblind-delegate-visual-creative`) - this is not a
colour decision and needs no hue ruling.** It is an export setting. If the re-export changes the
card's crop or composition visibly, THAT goes to the owner with the frame.

### Item B - fix the stale dock, then decide about the caption

1. **Correct the Echo chip's dock, or its comment, or both.** `EchoChipBandCentreY = 0.475f`
   (`EchoUnlockFeedback.cs:56`) is justified by two numbers that no longer exist (sec.2c). Either
   re-seat it against the LIVE `HudAreasHost` values, or - better, and in the spirit of CLAUDE.md
   sec.2's "delete the copy, do not improve it" - **have it read the mount instead of restating it.**
   Note the cross-assembly constraint: `EchoUnlockFeedback` is in `DeNelle.Village` and the comment at
   `HudKitController.cs:5437` records that this chip "cannot be edited from here", so whatever seam is
   used must go through Core (CLAUDE.md sec.5). **Do not add an assembly reference.**
2. **The caption escapes its plate at both edges.** Three lines in a 112-px box. Either the plate
   grows to hold three lines, or the caption stops being three lines - `TitleLine + "\n" +
   OutcomeWord(...)` (`DefenseReportChipModel.cs:89-90`) could be one line, or the title could be
   dropped when an outcome word is present. **The words are player-facing, so a copy change is the
   owner's** (sec.5).

---

## 5. THE OWNER RULING

One question, only if item B's fix needs a copy change (`AskUserQuestion`, with the enlarged crop from
sec.2a attached): **should the chip read `ATTACK REPORT` above `HELD` in a taller plate, or should it
read as one line?** Offer no default. Everything else in this ticket is mechanical.

---

## 6. Acceptance

1. **The frames.** A fresh Journey deck frame and a fresh town frame at the device aspect, OPENED and
   pasted. The RAIDS card has no white anywhere; the ATTACK REPORT chip's text is entirely on its own
   plate. The owner's standing rule is *"I want images to verify anything that is a viewable issue"*.
2. **The PNG is proven, not assumed.** Re-sample the four corners of the new `raids.png` and paste the
   RGBA values - all four must read alpha 0, like `quests.png`.
3. **The workaround is GONE.** The `"raids"` row is deleted from `OpaqueMargins` and
   `HudLabelFitRegression` case 7 `[deck-card-packaging]` (`:911-1010`) still passes.
4. **The stretch is gone.** RAIDS and QUESTS render at the same aspect - show them side by side.
5. **The locked face did not regress.** `raids-locked.png` still renders clean; show the locked card.
6. **The Echo chip's justification cites live numbers** (or reads them), and the numbers it cites
   resolve against `HudAreasHost.cs` today. Paste the before and after.
7. **Brace and NUL checks** on every `.cs` touched, including `python tools/gate_brace.py` on each
   (CLAUDE.md sec.1 - the gate counts differently from the raw one-liner).

---

## 7. Pins - what must not move

- `Assets/Editor/Regression/HudLabelFitRegression.cs:911-1010` case 7 `[deck-card-packaging]` - it
  genuinely opens the PNGs (`:967-971`) and its `:977-978` rectangular ink-bounds test is exactly why
  this shipped. **Its self-retiring half at `:990-995` is the intended path: re-export with alpha, then
  delete the row.** Same file `:1546` and `:1586` pin the RAIDS crop fractions (`0.088..0.929`) and the
  `IllustratedCardSurface` tint rule - **both change with the re-export, so update them deliberately
  and say so.**
- `Assets/Editor/Regression/PublicNavigationRetirementRegression.cs:218` and
  `Assets/Editor/Regression/RaidsDiscoverabilityRegression.cs:128` - `IllustratedCardSurface` must
  still exist (presence only).
- `JourneyDeckTwoCardRegression`, `JourneyDeckSubtitleRegression`, `DeckReturnDoorRegression` -
  content and route pins. The card count, its subtitle and its door do not move.
- `Assets/Editor/Regression/DefenseReportLayoutRegression.cs:354-480` case 4 `[chip-gate]` - the
  caption composition (`:397-400`), the width budget (`:446-449`), the
  `FitBlock(_defenseChipLabel, 22f...)` re-fit (`:452-456`), the door (`:457-459`), and the per-line
  measurement against the inner width (`:461-480`). **A caption change edits `:397-400`; do not leave
  the oracle behind.**
- `Assets/Editor/Regression/HudUiRegression.cs:1660-1741` check 8 `[harvest-clearance]` - the only
  right-column geometry model. It parses ONLY the Collectors call (`:1661-1662`) and models it against
  the resource panel (`:1719-1730`); it pins the 220x112 box at `:1683-1694`. Same file check 6e
  (`:938-950`) pins that `EchoChipBandCentreY` EXISTS, that `MinTouchPx` is used, and that the old
  ToastCard floater is gone - **it never checks 0.475 against any mount.**
- STOP: `ElarionUiKit` / `ElarionUiKitObsidian` / `ElarionUi` and `MedievalUiSkin` are READ-ONLY.
  `MinTouchPx`, `PadPanel`, the palette. Cite them; change nothing.

**Coverage gaps, both real, neither built here:** (i) nothing checks a card sprite for opaque pixels
inside its ink bbox - `HudLabelFitRegression`'s rectangular test passes corner residue; (ii) nothing
models the THIRD element of the QueueStatus stack against the Echo chip's fractional band. Per
`LayoutOracle.cs:17-20` a new rule must be SEEN RED first. **Raise both in the hand-back so the lead
can mint them; do not add them in this diff.**

---

## 8. What NOT to touch

- The raid arena (WO-1637), the gatehouse slabs (WO-1638), the in-raid HUD (WO-1639), the staging
  screen (WO-1640).
- **The Heart plate's objective copy - that is WO-1641.** WARNING: Note that `..._0603_journey_deck.png`
  shows the RAIDS card subtitle reading `Army 8 / 10 . train to open a camp` while the gate at that
  instant was satisfied (`raid_logcat_stream.txt:7844`, `required=3, ready=True`). **That is a second
  reader of the raid gate, SEEN and RECORDED, and it belongs to WO-1641's family, not to this
  ticket.** Raise it; do not fix it here.
- `hud-areas.json` and `HudAreasHost`'s mount rects - reading them is the fix; moving them is not.
- The Collectors chip, the Builders chip, the resource panel, the Harvest chip.
- Do **not** commit. Do **not** push. Hand the diff back (CLAUDE.md sec.11).

---

## 9. Board

This lane owns this ticket. Its hand-back is incomplete until this file's `**Status:**` line is
flipped and
`WorkOrders/WORK_ORDER_1642_town_chrome_raids_card_white_corners_and_the_attack_report_chip_off_plate.RESULT.md`
is written, with both paths reported. The lead regenerates `BOARD.html`.

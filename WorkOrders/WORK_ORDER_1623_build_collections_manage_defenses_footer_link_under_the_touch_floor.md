# WO-1623 - Build Collections: the "Already built? Manage defenses >" footer link is authored ~60 px UNDER the kit touch floor, at every captured resolution

**Status:** FIXED 2026-09-10 - gated (Builds/wave2-compile2, Builds/wave2-reg2 493/493) and captured: Builds/wave2-capture -> UI_GEOMETRY_OK 91 canvases (was UI_GEOMETRY_FAIL x3 on this item), PNG Builds/ui-capture/BuildCollections_2670x1200.png sent to the owner; owner felt-test closes. (was: IMPLEMENTED - awaiting gate (lane FOOTER 2026-09-10; was: READY TO IMPLEMENT))
**Minted:** 2026-09-10 (CLI, main-line banner; bumped 1621 -> 1625 in the SAME edit)
**Silo / Lane:** UI / Build mode (`BuildCollectionBrowser`) + ElarionUiKit touch floor
**Severity:** P2. The capture gate is RED on it (`UI_GEOMETRY_FAIL x3`), and the oracle's own message
says the runtime rescue makes it WORSE, not better (sec.1c). It is the only Manage > Defense door on
this screen (WO-1411 retired the card), so a mis-sized band is the whole route.
**Type:** EXISTING system. The link ships and routes correctly; its authored BAND is too short.
**Owner words:** none - this is a lane finding off the capture gate.
**Canon:** memory `mobile-ui-touch-contrast-standard` - **`ElarionUiKit.MinTouchPx = 112`**
(`Assets/_Modules/Core/UI/ElarionUiKit.cs:347`, read at source 2026-09-10).
**Age note:** this is **NOT new**. The identical three lines appear in `Builds/social-ui-capture.log`
(2026-09-09 17:05) and again in `Builds/wave1-capture` (frames written 2026-09-09 23:52) - so it has
been red across at least two capture runs and is pre-existing debt, not a regression from wave1.

---

## 1. The defect, measured (every log and line opened 2026-09-10)

### 1a. The oracle lines, verbatim

`Builds/wave1-capture`, lines 3078 / 3090 / 3102 (`[UICap-GEO]`) and repeated at 3126 / 3138 / 3150
(`[touch-oracle]`); the same three also in `Builds/social-ui-capture.log`:

```
[UICap-GEO] SUB-TOUCH-FLOOR BAND [BuildCollections_1920x1080 @1920x1080] 'ObsidianPanel/PanelFill/Zone_Body/ManageDefensesFooterLink' resolves 666.8x60.3 ref px -- shortest side 60.3 is 51.7 px UNDER ElarionUiKit.MinTouchPx (112). ClampMinTouch will grow it SYMMETRICALLY about its centre at runtime and spill it into both neighbours. Author the band AT the floor.
[UICap-GEO] SUB-TOUCH-FLOOR BAND [BuildCollections_2340x1080 @2340x1080] '...same path...' resolves 736.3x53.3 ref px -- shortest side 53.3 is 58.7 px UNDER ElarionUiKit.MinTouchPx (112). ...
[UICap-GEO] SUB-TOUCH-FLOOR BAND [BuildCollections_2670x1200 @2670x1200] '...same path...' resolves 746.2x52.4 ref px -- shortest side 52.4 is 59.6 px UNDER ElarionUiKit.MinTouchPx (112). ...
```

Gate verdict on the same log:

```
UI_GEOMETRY_FAIL x3 over 91 canvases -- see the [UICap-GEO] lines above; each names the panel, the element and the numbers.
```

**Three failures over 91 canvases, and all three are this one element.**

Frames on disk (2026-09-09 23:52): `Builds/ui-capture/BuildCollections_2670x1200.png` (1218091 B),
`BuildCollections_2340x1080.png` (1014515 B), `BuildCollections_1920x1080.png` (811953 B).
**Open the 2670x1200 frame** - owner standing rule, *"I want images to verify anything that is a
viewable issue"* (2026-09-09).

Note the WIDTH is fine everywhere (666-746 ref px). **Only the height is short.**

### 1b. Where the band is authored

`Assets/_Modules/Village/BuildMode/BuildCollectionBrowser.cs`:

- `:200` `BuildManageDefensesFooterLink();` - called from the grid build.
- `:222` `private void BuildManageDefensesFooterLink()` - inside `Guard.Try("BuildCollections", ...)`.
- `:226` `var link = ButtonBox(_panel, "Already built? Manage defenses >", () => { ... });`
- **`:235-237` the band:**
  ```
  rt.anchorMin = new Vector2(.28f, .05f);
  rt.anchorMax = new Vector2(.72f, .155f);
  rt.offsetMin = rt.offsetMax = Vector2.zero;
  ```
- `:238` `link.name = "ManageDefensesFooterLink";`
- `:242-246` the label: `enableWordWrapping = false` (`:242`), `enableAutoSizing = true`,
  `fontSizeMin = 16f` (`:244`), `fontSizeMax = 24f` (`:245`), `overflowMode = Overflow` (`:246`).

The vertical band is a pure FRACTION: `.155 - .05 = .105` of the parent zone. It carries no pixel
floor at all.

### 1c. Why the runtime "rescue" is not a fix - the oracle says so in its own message

`ElarionUiKit.ClampMinTouch` (`Assets/_Modules/Core/UI/ElarionUiKit.cs:1069`) exists to grow
under-floor buttons at runtime. The oracle's message names precisely why that is the wrong outcome
here: *"ClampMinTouch will grow it SYMMETRICALLY about its centre at runtime and spill it into both
neighbours. Author the band AT the floor."* A ~60 px band grown to 112 px about its centre pushes
~26 px up into the category grid and ~26 px down off the panel bottom (`anchorMin.y = .05`).

### 1d. THE ARITHMETIC SAYS THE BAND CANNOT REACH THE FLOOR IN ITS CURRENT SLOT - and that is the
### finding the lane must start from

**Derived, not measured** (labelled as such per CLAUDE.md sec.11B - the oracle printed the resolved
px and the source printed the fraction; the parent height below is `resolved / fraction`):

| aspect | resolved band | => Zone_Body height (derived) | fraction needed for 112 px (derived) |
|---|---|---|---|
| 1920x1080 | 60.3 px | ~574 ref px | ~0.195 |
| 2340x1080 | 53.3 px | ~508 ref px | ~0.220 |
| 2670x1200 | 52.4 px | ~499 ref px | ~0.224 |

The source comment at `:218-219` states the space available: *"It sits in the root band below the
card row (the grid ends at y .18)"*. So the band may occupy at most `.18 - .05 = .13` -> **~65 /
~66 / ~75 ref px** depending on aspect.

**At NO captured aspect does the available slot reach 112 px.** Simply widening `anchorMax.y` from
`.155` to `.18` closes at most ~13 px of a ~60 px shortfall and is NOT the fix. The lane must
either move the grid's bottom edge or change the zone the link lives in - decided from a MEASURED
`Zone_Body` height, not from the derived table above.

## 2. Target - what "fixed" means

The footer link's resolved band clears `ElarionUiKit.MinTouchPx` at every captured resolution,
without `ClampMinTouch` having to rescue it, and without the category grid or the panel edge losing
space it needs. `UI_GEOMETRY_OK` on a fresh capture log.

## 3. Architecture ruling

- **The touch floor has ONE owner: `ElarionUiKit.MinTouchPx`** (`ElarionUiKit.cs:347`). Do not
  hardcode `112` in `BuildCollectionBrowser.cs`; reference the const.
- **Author in REFERENCE PIXELS, which is what the oracle asks for.** `LayoutOracle`'s comment
  (`Assets/_Modules/Core/UI/LayoutOracle.cs:23`) states it measures in *"the kit's reference-px
  space - the same units MinTouchPx and the zone fractions are authored in"*. A fraction cannot
  express a pixel floor across three aspects; that is the whole defect. The existing pixel-band
  shape in this codebase is `MakePixelBand` (`Assets/Editor/UICaptureLaunch.cs:4619`, used as
  `MakePixelBand(band, "ChipBand", 0f, ElarionUiKit.MinTouchPx, 4f)`) - **read it for the shape;
  it lives in an editor file, so it is a pattern to follow, not a method to call.**
- **`ClampMinTouch`'s behaviour is DELIBERATELY UNCHANGED** - the code says so at
  `ElarionUiKit.cs:1082`. It is the safety net. Do not "fix" this by tuning the net.
- **Any number that survives becomes a named const**, defaulting to today's value, with a comment
  naming this WO.

## 4. Lane split

**Step 1 - MEASURE the real `Zone_Body` height** at all three aspects (the derived table in sec.1d is
an inference and must be replaced by a printed number before any layout edit; CLAUDE.md sec.12).
The capture harness already walks these canvases - add the parent height to the existing
`[UICap-GEO]` line or emit one alongside it.

**Step 2 - RE-AUTHOR the band from that number**, at or above `MinTouchPx`, deciding from the data
whether the grid's `.18` bottom moves, the link moves out of `Zone_Body`, or the panel gains height.
The two-word answer the oracle already gave - *"Author the band AT the floor"* - is the acceptance
criterion, not the implementation plan.

**Step 3 - RE-CAPTURE and read it.** `UI_GEOMETRY_OK` on a FRESH log plus the reread 2670x1200 frame
go in the RESULT (memory `headless-screenshot-verify-ui-before-build`).

## 5. Pins - what must not move

- ### The BASELINE ALLOW-LIST IS NOT THE FIX, AND ADDING THIS PANEL TO IT IS FORBIDDEN.
  `Assets/Editor/UICaptureLaunch.cs:5915` computes `bool baselined = IsTouchBaselined(label);` and
  `:5924` adds a `SUB-TOUCH-FLOOR BAND` finding to `_touchFailures` **only when `!baselined`**;
  `:5934` then warns *"BASELINED (known debt, still red) ... this panel's WO removes its own
  allow-list entry when it lands."* The allow-list comment below says four panels are known-bad and
  **"NOTHING ELSE IS."**
  **The PROOF that `BuildCollections` is not baselined is the `[touch-oracle]` lines** at
  `Builds/wave1-capture` 3126 / 3138 / 3150 - those print from `_touchFailures` at
  `UICaptureLaunch.cs:6014`, which `:5924` only fills for a non-baselined panel.
  WARNING: **An earlier draft of this ticket got the causation backwards** and cited the
  `UI_GEOMETRY_FAIL x3` verdict as the proof. It is not: `_geoFailures.AddRange(fails)` runs at
  `:5908`, **BEFORE** the baseline test at `:5915`, and the verdict at `:6044` counts `_geoFailures`.
  **`UI_GEOMETRY_FAIL` is baseline-BLIND.** Which makes the pin stronger, not weaker: baselining this
  panel **would not even clear the geometry marker** - it would only silence the touch-oracle one,
  leaving the gate red and the evidence gone. **Adding it is turning half the gate off and fixing
  nothing.**
- **`Assets/Editor/Regression/BuildCollectionPlayerRegression.cs:153-155`** pins three literals that
  must stay byte-identical in `BuildCollectionBrowser.cs`:
  `link.name = "ManageDefensesFooterLink"`, `"Already built? Manage defenses >"`, and
  `PanelRouter.Open(PanelId.Manage, "Defense")`. Its failure text (`:156`) is
  *"the Manage > Defense door left the collection browser..."*. Changing the caption or the node
  name reds `BUILD_COLLECTION_PLAYER_FAIL`.
- `:158-159` of the same suite reds if the string `"Upgrade Defenses"` returns - the retired 8th
  category card (WO-1411 ruling sec.2 #13). Do not resurrect the card to solve the band.
- **The WO-1411 rationale block, `BuildCollectionBrowser.cs:204-221`.** It is the record of an owner
  ruling. Do not delete it while editing three lines beneath it (CLAUDE.md sec.15).
- **`Guard.Try` around the builder (`:224`)** - CLAUDE.md sec.12 step 2, never unwrap it.
- **The `FlowTrace.Step` calls opening at `:228` and `:248`** - CLAUDE.md sec.12: instrumentation is
  permanent, never stripped.
- **The seven category cards and `BuildManagePlacedCard`** - if the grid's bottom edge moves, the
  cards must still clear their own geometry assertions. Re-capture proves it; do not assume.

## 6. RED-first suite spec

**The oracle already exists and is already RED - that is the RED-first proof, and it is a MEASURED
one, not a source lint.** `LayoutOracle.Audit` (`Assets/_Modules/Core/UI/LayoutOracle.cs:86`),
Assert A at `:175-183`:

```
if (shortest >= ElarionUiKit.MinTouchPx - 0.5f) continue;
... "SUB-TOUCH-FLOOR BAND" + at + " '" + PathOf(...) ...
```

driven by the capture harness, verdict emitted at `UICaptureLaunch.cs:6027-6044`
(`UI_GEOMETRY_OK <n> canvases` / `UI_GEOMETRY_FAIL x<n>`).

- **RED at HEAD:** proven above - `UI_GEOMETRY_FAIL x3`, all three this element.
- **GREEN acceptance:** `UI_GEOMETRY_OK` on a FRESH capture log with `BuildCollections` measured at
  all three resolutions, plus zero `[touch-oracle]` lines naming `ManageDefensesFooterLink`.
- **The mutation to name in the RESULT:** restore `anchorMax.y` to `.155f` and show the three
  `SUB-TOUCH-FLOOR BAND` lines return.
- **Also keep green:** `BUILD_COLLECTION_PLAYER_OK` (sec.5), which pins the door's identity while
  this ticket changes only its size.

Judge by the MARKER on a FRESH log, never the runner's exit code (CLAUDE.md sec.8, memory
`gates-report-success-without-proving-it`). Marker absence is a FAILURE, not an unknown.

## 7. Not in scope

- Do **not** add `BuildCollections` to the touch baseline allow-list (sec.5, first pin).
- Do **not** change the link's CAPTION, node name, or route (three pinned literals, sec.5).
- Do **not** modify `ClampMinTouch` or `MinTouchPx`.
- Do **not** re-add the retired "Upgrade Defenses" 8th category card.
- Do **not** touch `ManageScreenPanel` / `ManageScreenVM` / `RenderDefenseDestination` - the
  destination is pinned by the same suite and is a different screen.
- Do **not** widen the fix to the four genuinely-baselined panels; they carry their own WOs.
- Do **not** "tidy" `:244` `label.fontSizeMin = 16f`. **HAND IT BACK AS A FOLLOW-UP INSTEAD.** It is
  set directly on TMP, bypassing `FitSingleLine`, and **16 is BELOW the kit's `FontHardFloor` of
  20f** (`ElarionUiKitObsidian.cs:3044`) - which the kit's own clamp at `:3062` would have raised had
  the call gone through it. That is a real kit bypass and a real second finding, but it is a
  legibility ticket, not a touch-floor ticket, and taking it here mixes two acceptance criteria in
  one diff.
- Do **not** run Unity, the capture harness, gate, or commit from the lane. Edit-only; the lead
  holds the Unity lock, fires the capture, and is the sole committer.

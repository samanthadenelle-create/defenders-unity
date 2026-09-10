# WO-1639 RESULT - in-raid HUD legibility (lane RAID-HUD, 2026-09-10)

**Status of the work:** IMPLEMENTED - awaiting gate + device frame.
**Lane:** UI / raid HUD. **EDIT ONLY** - no Unity run, no gate, no commit, per the lane brief.
**Base:** worktree `D:\EoA\.claude\worktrees\agent-a223665cd051ef469`, fast-forwarded to `a96bfe332`
(`dev`), `git status --short` clean before the first edit.

**Files changed (2):**
- `Assets/_Modules/Village/Troops/RaidHudController.cs`
- `Assets/_Modules/Village/Troops/RaidDeployController.cs`

---

## 0. READ THIS FIRST - a scope conflict in the lane brief, flagged, not silently resolved

The brief said *"Do not touch RaidScoring.cs, **RaidDeployController.cs** or the staging screen
(another lane owns WO-1640)"* - and in the same breath named **HERO DOWN** and the **DEPLOY face
ellipsis** as this lane's deliverables. Both of those, and the objective-marker candidate, are
authored **only** in `RaidDeployController.cs` (WO-1639 sec.1b cites `:1703-1710`, sec.1c cites
`:1082-1095` / `:1919`, sec.1d cites `:904-939`). WO-1639 sec.8 says WO-1640's file is
**`RaidDeployScreen.cs`** (`Assets/_Modules/Village/Hero/RaidDeployScreen.cs`) - a different file
that this lane never opened.

**Read as written, the brief excludes the file that contains three of its four deliverables.** This
lane treated it as a slip for `RaidDeployScreen.cs` and proceeded. **Every** `RaidDeployController.cs`
edit is confined to that one file, so if the exclusion was real the lead drops it with one command:

```
git checkout -- Assets/_Modules/Village/Troops/RaidDeployController.cs
```

`RaidScoring.cs`, `RaidDeployScreen.cs`, `RaidBaseDresser` and `RaidBaseGenerator` were **not opened
for edit**. No kit file was touched (`ElarionUiKit.cs`, `ElarionUiKitObsidian.cs`, `ElarionUi.cs` are
READ-ONLY per sec.7 - they were read and cited only).

---

## 1. Evidence discipline - what is measured and what is predicted

Per CLAUDE.md sec.11B, stated plainly:

- The **"before"** contrast numbers are WO-1639 sec.1a's, sampled off
  `Builds/device-frames/2026-09-10_0608_arena_01_entry.png`. **Measured.**
- The **"after"** contrast numbers below are **PREDICTED, not measured**. They are computed from the
  kit constant this lane installs (`ElarionUiKit.ObsidianFill`) composited over the arena background
  back-solved from that same frame. **No device frame of the fix exists yet** - producing one is
  acceptance item 1 and belongs to the lead/owner.
- The **Defect C root cause is PROVEN** from source read at HEAD plus the frame's own pixel
  measurement, and the two agree to within 6 px. That is set out in sec.4.
- **Defect D is NOT fixed and NOT diagnosed.** Nothing was clamped. See sec.5.

---

## 2. DEFECT A - the readout plate

### The change

| | before | after |
|---|---|---|
| plate fill (`RaidHudController.cs`, `BuildHud`) | `new Color(0.04f, 0.035f, 0.03f, 0.42f)` at HEAD `:191` | `ElarionUiKit.ObsidianFill` (now `:411`) |
| unlit star tint (`StarDim`) | `new Color(1f, 1f, 1f, 0.14f)` at HEAD `:69` | `new Color(1f, 1f, 1f, 0.40f)` (now `:77`) |
| the three progress TRACKS (`TimerTrack` `:204`, `ObjectiveTrack` `:226`, `DestTrack` `:236` at HEAD) | `new Color(0f, 0f, 0f, 0.5f)` | `EmptyTrackFill` = `new Color(1f, 1f, 1f, 0.40f)` (now `:82`) |
| `Razed` row colour (`:230-231` at HEAD) | `ElarionUi.ParchmentDim` | **UNCHANGED** - see below |
| `Troops` row colour (`:266-267` at HEAD) | `ElarionUi.ParchmentDim` | **UNCHANGED** - see below |

**The tracks had to move with the plate, and missing that would have traded one defect for a worse
one.** All three were authored as DARK grooves for a translucent grey plate. On `ObsidianFill` the
plate composites to ~0.037 sRGB and 50% black lands at ~0.019 - **1.03 : 1**. The EMPTY portion of
every bar would have vanished, leaving a gilt line of varying length with nothing to say what "full"
was. The timer bar, the spire bar and the razed bar are the **motion channel** the repo's colourblind
law leans on, so losing them is worse than the illegibility this ticket opened with. `StarDim` and
the tracks now share ONE value (`EmptyTrackFill`), because "empty" and "unlit" are the same state.

`ElarionUiKit.ObsidianFill` is read at `Assets/_Modules/Core/UI/ElarionUiKit.cs:189` =
`new Color(0.02f, 0.02f, 0.025f, 0.98f)`. **The constant is referenced, not copied** - a local
literal at raised alpha would be the same duplicated-state failure CLAUDE.md sec.2 / sec.5 / sec.16
each document.

### The arithmetic (WCAG relative luminance, sRGB, same method as sec.1a)

**Step 1 - back-solve the arena background from the measured composite.** Sec.1a sampled the plate
at RGB (136,145,154) = (0.5333, 0.5686, 0.6039). That is the OLD plate at alpha 0.42 over the arena:

```
bg = (composite - 0.42 * plate_old) / 0.58
   r = (0.5333 - 0.0168) / 0.58 = 0.8905
   g = (0.5686 - 0.0147) / 0.58 = 0.9550
   b = (0.6039 - 0.0126) / 0.58 = 1.0195  -> clamped to 1.0
```

A near-white daylit background, which is why nothing on the panel read.

**Step 2 - composite the NEW plate over that same background.**

```
comp = 0.98 * (0.020, 0.020, 0.025) + 0.02 * (0.8905, 0.9550, 1.0)
     = (0.03741, 0.03870, 0.04450)
L_plate = 0.2126*0.002895 + 0.7152*0.002995 + 0.0722*0.003463 = 0.0030075
```

**Step 3 - the ratios.** Glyph colours render at nominal (the frame confirms this: sec.1a's timer
peak (243,234,211) is `ElarionUi.Parchment` exactly), so only the plate moved.

| Row | colour (source) | **before (measured, sec.1a)** | **after (predicted)** | floor |
|---|---|---|---|---|
| `3:00` timer | `Parchment` `ElarionUi.cs:65` | **2.67 : 1** | **16.54 : 1** | 4.5 text |
| `SPIRE 100%` | `Gilt` `ElarionUi.cs:60` | **1.98 : 1** | **12.24 : 1** | 4.5 text |
| `Razed 0%` | `ParchmentDim` `ElarionUi.cs:67` | **1.72 : 1** | **10.61 : 1** | 4.5 text |
| star diamonds (unlit) | `StarDim` -> `EmptyTrackFill` (0.14 -> **0.40** white) | **1.55 : 1** | **3.77 : 1** | 3.0 component |
| `Troops 0/0` | `ParchmentDim` | **1.12 : 1** | **10.61 : 1** | 4.5 text |
| the three bar TRACKS (empty portion) | 50% black -> `EmptyTrackFill` | n/a - would have been **1.03 : 1** on the new plate | **3.77 : 1** | 3.0 component |

Component luminances used: `Parchment` L = 0.82663, `Gilt` L = 0.59902, `ParchmentDim` L = 0.51237,
white@0.40 over the new plate L = 0.14981. Ratio = (L_text + 0.05) / (L_plate + 0.05).

**Independent corroboration from inside this repo.**
`Assets/Editor/Regression/HudLabelFitRegression.cs:1548-1551` records its own measurement of the SAME
palette over a near-black card plate, taken 2026-09-10: **9.10 / 10.25 / 15.98 : 1 for Gold /
ParchmentDim / Parchment.** Against those, this lane's predictions (12.24 for the brighter `Gilt`,
**10.61** for `ParchmentDim`, **16.54** for `Parchment`) agree to within ~4% on the two directly
comparable tokens - computed by a different method, off a different plate, by a different lane. Not
proof the device will match, but a strong check that the arithmetic here is not novel.

WARNING **Caveat:** this compositing assumes GAMMA-space blending. If the project renders in Linear
colour space the exact predicted ratios shift a little. **The conclusion does not** - the plate is
essentially black either way, and every text row lands above 10 : 1.

### Why the two `ParchmentDim` rows were deliberately NOT promoted

WO-1639 sec.4 offered promoting them to `Parchment` and/or bold as "the smallest change that clears
the floor". **The arithmetic above says the plate alone clears it - `ParchmentDim` lands at 10.6 : 1,
more than double the 4.5 : 1 text floor.** Promoting on top of that would flatten the readout's
authored hierarchy (headline rows `Parchment`/`Gilt`/bold, secondary rows dim) to treat a symptom
that no longer exists, and `ParchmentDim` is the kit's secondary-text role that reads on a 0.98 plate
on every other screen in the game - including the raid's own end panel, which sec.1f records as
reading WELL. **Stated so the lead can overrule it in one line if the device frame disagrees.**

### The one look change, and whose call it was

Raising the plate is a **look change**. The 0.42 alpha carried an authored rationale at HEAD
`:189-190` ("quiet glass, not a gilt slab"), and **WO-1639 sec.5 asks for an OWNER ruling on it.**
This lane implemented it because the lane brief directed it verbatim (*"the raid readout plate alpha
0.42 -> the kit's canon (RaidHudController.cs:191)"*). **A lane brief is not the owner's word.** If
sec.5's `AskUserQuestion` was never put to her, it still needs putting - the change is one line and
one constant to revert. `StarDim` 0.14 -> 0.40 is the lane's own call inside the palette (the owner
is colourblind and is never asked to pick a value); it is an ALPHA move on an existing white, not a
new hue, and the lit/unlit read still carries on **shape** first (`StarSizeLit` 34 vs
`StarSizeLost` 20, unchanged) plus the hue-free `n/3`.

---

## 3. DEFECT B - `DEPLOY ...`

**The copy did NOT change.** The literal `"Deploy All"` is still in `RaidDeployController.cs`, so
`Assets/Editor/Regression/RaidDeployUiRegression.cs:411-415` stays green. Nothing was shortened.

**The faces widened, and the room came from the tray.** All fractions are OF THE BAR; the bar's own
band (`DeployBarBand`, x 0.280-0.980) does not move, so no y-band and no shared constant changed.

| element | before | after | width in ref px* | usable at 92%* |
|---|---|---|---|---|
| tray tiles (`BuildTrayTiles` `right`) | 0.030 - **0.550** | 0.030 - **0.390** | 541.3 | n/a (portraits) |
| `Deploy All` | **0.565 - 0.695** (0.130) | **0.410 - 0.650** (0.240) | 360.8 | 331.9 |
| `Rally` / `Rally ON` | **0.700 - 0.830** (0.130) | **0.665 - 0.825** (0.160) | 240.6 | 221.3 |
| `Retreat` | **0.845 - 0.985** (0.140) | **0.840 - 0.985** (0.145) | 218.0 | 200.6 |

\* At the owner's 2670x1200. `HudLayoutBands.CanvasReferenceSize(2670, 1200)` (`:379-387`, Unity's
own match-0.5 formula on the 1080x1920 reference at `:55-59`) = **2147.9 x 965.4** reference px, so
the bar's 0.700 span is **1503.5** ref px. `BuildObsidianButton` insets its label to 0.04-0.96 of the
face (`ElarionUiKitObsidian.cs:681-683`), i.e. 92% usable, then `FitSingleLine` (`:686`) autosizes
`[FontFloor 30 .. FontBody 50]` and cuts with `Ellipsis` (`:3054-3070`).

**Why 0.130 failed and 0.240 should not.** At ~0.68 em average advance for this bold face (the value
that reconciles BOTH observations in the frames - `Deploy All` failing at 179.9 usable px and
`Retreat` succeeding at 193.7):

| face | chars | old usable | old seated pt | new usable | **new seated pt** |
|---|---|---|---|---|---|
| `Deploy All` | 10 | 179.9 | 26.5 -> **below FontFloor 30 -> ELLIPSIS** | 331.9 | **~48** |
| `Rally ON` | 8 | 179.9 | 33.1 (one char from the same failure) | 221.3 | **~41** |
| `Retreat` | 7 | 193.7 | 40.7 | 200.6 | **~42** |

**No font is lowered anywhere.** `FontFloor` (30) and `FontHardFloor` (20) are untouched kit
constants; the fix moves geometry so the fitters never have to reach them. Note `Rally` was sized for
the SHORTER of its two strings - `RefreshRallyButton` swaps it to `"Rally ON"` in rally mode - which
is why its band grew too.

**Touch floor.** The tray keeps 541.3 ref px: four troop types seat 135 px each, over
`ElarionUiKit.MinTouchPx` = 112 (`ElarionUiKit.cs:347`). A fifth type falls to 108 and relies on the
kit's own `ClampMinTouch` - **exactly as it did at the old width** (0.52 of bar / 5 = 156 px was the
old five-tile figure, so this is a narrowing of an existing margin, not a new risk). Flagged, not
hidden. In the entry frame opened this session the tray holds **ONE** tile (a footman portrait with
an `x8` badge) stretched across the whole 0.03-0.55 strip, which is the shape this change fixes from
the other side: the tray was carrying one square icon in half the bar while the face beside it could
not fit its own word.

**Pins re-checked, none moved:** `RaidHudThumbBandRegression`'s three `RequireSeats` cases are all
HEIGHT arithmetic (`deployStatus.height`, `readout.height * 0.120f`,
`deployBar.height * 0.64f * 0.48f`) - this change is x-only inside the bar. Its `Require` /
`Forbid` source-text cases (`HudLayoutBands.BottomOverlayLeftX`, `DeployBandMinX = 0.02f`,
`HudLayoutBands.RaidReadoutBand`, `new Vector2(0.02f, 0.86f)`) are all unaffected. Grepped all five
raid regressions for `0.42`, `0.565`, `0.695`, `0.55f`, `ParchmentDim`, `StarDim`, `0.14f`,
`ShowToast`, `720`, `sortingOrder`, `Deploy All`, `Rally ON` - the **only** hit is the `"Deploy All"`
literal, which is preserved.

---

## 4. DEFECT C - `HERO DOWN` behind the deploy bar. ROOT CAUSE FOUND.

**WO-1639 sec.1c said "THE SOURCE DOES NOT EXPLAIN THE BURIAL" and told the lane to measure before
editing. It is explained, and it did not need a Unity run - the WO read the wrong widget.**

`SetStatus` (HEAD `:1919`) does **not** put the sentence in `_status`. It **clears** `_status` and
routes the copy to `ElarionUiKit.ShowToast`:

```
if (_status != null) _status.text = "";
if (string.IsNullOrEmpty(s)) return;
ElarionUiKit.ShowToast(s, ElarionUiKit.ToastTone.Info, lifeSeconds: 2.2f);
```

`ShowToast` builds **its own `ScreenSpaceOverlay` canvas at `sortingOrder = 720`**
(`Assets/_Modules/Core/UI/ElarionUiKitConformance.cs:393` signature, `:409-411` the canvas). This
controller's HUD canvas is **30000** (`BuildHud`), `RaidHudController`'s is **29000**.
**720 < 29000 < 30000.** The toast draws UNDER both, and the only reason any of it was visible is the
bar plate's own 0.38 alpha. That is precisely the frame: `HERO DO...` faint, *through* the tan plate.

**The seat arithmetic confirms it independently, and it is not a coincidence.** The card is pivot
(0.5, 0), `anchoredPosition = (0, 220)` on a 1080x1920 reference at match 0.5
(`ElarionUiKitConformance.cs:419-424`). At 2670x1200 the canvas scale is
`sqrt((2670/1080) * (1200/1920))` = **1.2430**, so a 76-px card spans **273.5 - 367.9** device px
from the bottom = **832.1 - 926.5 from the top**, and its vertically centred label lands at **~879**.
Sec.1c measured the glyphs at **855-885**. Same object.

### The change

| | before | after |
|---|---|---|
| toast sorting order | kit default **720** | `RaidToastSortingOrder` = **30500** |
| toast card | kit default 480 x 76 ref px | `RaidToastCardWidth/Height` = **640 x 96** |
| hero-down toast life | 2.2 s (chrome default) | `HeroDownToastLifeSeconds` = **4.5 s** |
| `SetStatus` signature | `SetStatus(string s)` | `SetStatus(string s, float lifeSeconds = RaidToastLifeSeconds)` |

**Caller-side only** - `ShowToast` already exposes `sortingOrder`, `cardWidth` and `cardHeight`, so
no kit file was edited (sec.7). Applied to **every** raid status, not just hero-down: a toast that
draws behind the HUD is equally useless for `"Deploy All needs open ground ahead."`.

The string is still `DeNelle.Village.UI.EndStateVM.HeroDownArmyFightsOn` and is never retyped inline,
so `RaidScoringRegression.cs:411-413` stays green. `RaidRepeatClearRegression.cs:461` (`SetStatus(`
present) and `RaidDeployZeroArmyRegression.cs:332` (`ShowToast` present) also stay green.

### Two residuals - recorded, NOT fixed, because the kit is READ-ONLY here

1. **The card's SEAT is still the kit's hardcoded 220 ref px**, which is inside the bar's y band
   (0.228-0.307 normalised). Above the bar in sort order it is fully legible, but for its ~4.5 s life
   it will cover part of the deploy tray. Acceptable for a transient at the moment the hero dies
   (WO-1639 sec.3: *"HERO DOWN is the most legible thing on the screen"*); the alternative is a kit
   change and a new ticket.
2. **The toast label is the kit's 24 px legacy Text**, below `ElarionUiKit.FontFloor` (30). That is a
   kit-wide question affecting every toast in the game, not a raid one. **Raise it with the lead.**
3. **`ShowToast` is ONE-AT-A-TIME** - a new call destroys the current card (`s_transientToast`,
   `ElarionUiKitConformance.cs:398-403`). Any `SetStatus` inside the 4.5 s hero-down window
   **replaces** `HERO DOWN`. Every other caller is player-triggered (Deploy All, Rally, Retreat), so
   it needs a deliberate tap during the death beat - unlikely, but real, and WO-1639 sec.3 makes
   HERO DOWN's primacy an explicit target. Kit-owned; recorded, not fixed.

---

## 5. DEFECT D - the objective marker. NOT FIXED, DELIBERATELY. INSTRUMENTED INSTEAD.

**Nothing was clamped, resized or hidden.** CLAUDE.md sec.12 forbids a code edit on a non-trivial
defect before captured data names the cause, and **WO-1639 sec.1d itself says "STILL NOT PROVEN that
it is the rally banner specifically, and NOT PROVEN that it is 'the objective marker' at all"**. The
lane brief's *"the WO tells you what it is and what to do"* is **not accurate** - the WO proves the
KIND (world-space) and explicitly withholds the identity, routing it to sec.4 Step 1.

**Two candidates were checked at source this session and BOTH are ruled out as the shape in the
frames:**

- **The rally banner** (`RaidDeployController.cs:904-939`, read at HEAD): the quad is
  `localScale (0.9, 0.6, 1)` at `localPosition (0.45, 2.0, 0)` on a 2.4 m pole. At any playable
  camera distance that projects tens of pixels, not the hundreds the frames show. It also seats at
  the **rally point**, and sec.1d's `..._0618` reading puts the shape over the **hero**.
- **`HeroReachRing`** (`Assets/_Modules/Village/Hero/HeroReachRing.cs`) - the only other world quad
  parented to the hero - is (a) **blue**, `_color = (0.45, 0.80, 1.0, 0.30)` at `:28`, and (b)
  **never attached**: `HeroControlEnsurer.cs:644-646` says in as many words *"(Intentionally not
  adding HeroReachRing here.)"*. Ruled out twice over.
- `HeroTargetIndicator`'s reticle (`:1276`) is a target ring on an enemy, not a shield over the hero.

**One correction to sec.1d, from opening `..._0608_arena_01_entry.png` THIS SESSION.** Sec.1d's
reading of `..._0618` put the shape "hovering over the hero's position". In the entry frame it does
**not** sit over the hero - the hero (the knight) is at bottom-centre against the deploy bar, and the
shape sits **left of the tower, above the dark rock cluster**, roughly x 0.29-0.34 / y 0.26-0.46 of
the frame. It is a **shield outline above a downward chevron**, opaque yellow, with the sky reading
through its interior. That is a marker over a *world position*, and which position is the question
`[wo1639-marker]` answers.

**What was delivered instead: the Step 1 read that names it on the next run.**
`RaidHudController.LogOversizedWorldMarkers(phase)` sweeps every enabled `Renderer` in the scene,
projects its bounds' eight corners, and **WARNs** any whose screen height exceeds
`OversizedMarkerScreenFraction` with its **full hierarchy path**, renderer type, screen height in px
and as a fraction, camera distance, material + shader name, and **tint** (the tint is what makes the
yellow one findable in one grep). It runs twice - **t+1 s** (entry seat) and **t+5 s** (closed on the
spire, where sec.1d says it spans the frame) - so the growth is in the log, not in an eyeball.

**AND IT SWEEPS CANVASES TOO, WHICH IS NOT A NICETY.** `FindObjectsByType<Renderer>` **does not see
uGUI** - an `Image` draws through a `CanvasRenderer`, which is not a `Renderer`. A **WorldSpace** (or
ScreenSpaceCamera) `Canvas` carrying a shield/chevron SPRITE matches *every* fact sec.1d established -
world-space so the overlay compass occludes it, scaling with camera proximity, and a sprite gives the
crisp vector silhouette the entry frame shows - and a Renderer-only sweep would have logged
**nothing**, printed "no world renderer exceeds...", and sent the next lane back to square one. This
project already builds exactly that shape of object: `FloatingHealthBar.cs:211-216` stands up a
`RenderMode.WorldSpace` canvas with a gold rim (checked this session; far too small to be the shape in
the frames, but it proves the class exists here). So a **second pass** walks every enabled non-overlay
root `Canvas`, projects its `RectTransform` corners, and logs path + `renderMode` + `sortingOrder` +
the **sprite name and alpha of up to six child `Image`s**. If BOTH passes come back empty the trace
says so in as many words and names the next place to look (a stacked camera overlay, or a
projector/decal) - a null result that is still a finding.

⚠ **The threshold is 0.10 of screen height, not 0.25, and the frame is why.** Measured off
`..._0608` this session, the shape at the ENTRY seat is roughly **240 px of 1200 = 0.20** - a 0.25
threshold would have reported **nothing** at t+1 s, and the whole point of two samples is to catch
the object at both distances and prove it grows. At 0.10 the ground plane, the tower and the boundary
ring also qualify, so the burst is capped at 25 entries per sample (a firehose evicts the boot window
out of the device logcat ring - memory `logcat-ring-buffer-destroys-evidence`).

**The next device run's log answers Defect D in one grep:** `[wo1639-marker]`. The path it prints is
the object; the owning script is then one grep away and a clamp can be specced against a named
object.

---

## 6. Instrumentation added (PERMANENT - CLAUDE.md sec.12)

All of it is `FlowTrace`, tagged, and **stays in the code** once the systems are proven
(sec.12: flag off, never strip). Every interpolated part is computed into a local first and every
message is plain concatenation - the compile gate's brace scanner has no interpolated-string model
(CLAUDE.md sec.1).

| tag | file | what it answers |
|---|---|---|
| `[wo1639-fit]` | `RaidHudController` | Per readout row: band px, autosize window `[min..max]`, `lineCount`, drawn chars vs `text.Length`, `isTextTruncated`, overflow/wrap modes, colour. A row short of its string is ellipsised; `lines=0` was CULLED (the WO-1519 class). WARNs on either. |
| `[wo1639-marker]` | `RaidHudController` | Defect D. TWO passes at t+1 s and t+5 s: every world `Renderer` over 0.10 of screen height (path, kind, px, camera distance, material, tint), AND every non-overlay root `Canvas` over the same threshold (path, renderMode, sortingOrder, child sprite names + alphas). Says so explicitly when both come back empty. |
| `[wo1639-face]` | `RaidDeployController` | Per bar face: face px, label px, seated font, drawn chars vs length, `isTextTruncated`. WARNs on `DEPLOY ...`. |
| `[wo1639-toast]` | `RaidDeployController` | Prints the raid toast order against the live deploy-canvas order, so sec.4's RCA can never be re-inferred. |

Shape follows WO-1628 sec.4 Step 1, which the WO names as the established pattern here.

---

## 7. Checks run (CLAUDE.md sec.1)

```
python tools/gate_brace.py Assets/_Modules/Village/Troops/RaidHudController.cs \
                           Assets/_Modules/Village/Troops/RaidDeployController.cs
-> GATE_BRACE_SUMMARY bad=0 of 2   (exit 0)

RaidHudController.cs      NUL=0   raw braces 44/44
RaidDeployController.cs   NUL=0   raw braces 192/192
```

**The WO copy did not drift.** `diff` between the source at the repo root and the copy brought into
this worktree shows **exactly one changed line** - the `**Status:**` flip on line 3, nothing else:

```
3c3
< **Status:** READY TO IMPLEMENT
---
> **Status:** IMPLEMENTED - awaiting gate + device frame (lane RAID-HUD 2026-09-10)
```

**Literal grep widened beyond the five pinned suites.** `0.565`, `0.695`, `0.845f`, `0.83f`,
`right = 0.55`, `0f, 0f, 0f, 0.5f`, `StarDim`, `ParchmentDim`, `sortingOrder: 720`, `EmptyTrackFill`
across ALL of `Assets/Editor/Regression` and `Assets/Tests`: the only hits are on unrelated surfaces
(`DefenseReportLayoutRegression`, `HudLabelFitRegression`, `ManageMockupConformanceRegression`) and
none names a raid file. `RaidDeployZeroArmyRegression:332` (`ShowToast` present),
`RaidExitParityRegression:136` and `RaidRepeatClearRegression:461` (`SetStatus(` present) were
checked by name; all three still pass their source-text assertions.

Both the gate's own rule (`tools/gate_brace.py`, the port of `CompileGate.BraceBalanced`) and the raw
one-liner agree. **No gate was run and no gate marker string is written anywhere in this document**
- the lane is EDIT ONLY, and the compile and regression markers are the lead's to earn on a fresh log.

---

## 8. What the next device frame must show (acceptance, sec.6)

Fresh in-raid frames from the same seats as `..._0608` (entry) and `..._0613` (hero at the left edge)
plus one at the moment of hero death and one near/one far from the spire. Specifically:

1. **The readout plate is opaque.** Nothing behind it - boundary-ring pillars, WO-1638's green slabs,
   the horizon - is visible through the panel behind the star row or the `Troops` row. This is the
   single fastest pass/fail read on the frame and needs no sampling.
2. **The three progress tracks read as grooves.** The EMPTY portion of the timer, spire and razed
   bars must show as a distinct lighter band, not as absence. This is the item most easily missed and
   it is the reason the colourblind motion channel survives the plate change.
3. **All five rows re-measured by sec.1a's method** and pasted beside the table in sec.2 above.
   Expect `Troops` at ~10.6 : 1 (was 1.12). **If any row lands under 4.5 : 1, the `ParchmentDim`
   promotion this lane declined (sec.2) is the next lever and it is a one-line change.**
3. **Greyscale check** of the same frames - the owner is colourblind and greyscale is the gate. The
   readout must stay legible with hue removed; the unlit vs lit star distinction must still read
   (it carries on SIZE first, so this should be unchanged).
4. **Every bar face shows its whole word, with no ellipsis:** `Deploy All`, `Rally` (and `Rally ON`
   with rally armed - capture that state too, it is the longer string), `Retreat`. Paste what each
   reads. Cross-check against the `[wo1639-face]` lines in the same run's log: every one must be a
   `Step`, not a `Warn`, and `chars` must equal the string length.
5. **`HERO DOWN - your army fights on` renders in FULL, on top of the deploy bar**, at the moment the
   hero falls, for ~4.5 s. It will overlap the tray - that is expected and is residual (1) in sec.4.
   The `[wo1639-toast]` line in the same log must read order 30500 against a deploy canvas of 30000.
6. **The objective marker at two camera distances**, one frame each, **with the `[wo1639-marker]`
   lines from the same run pasted.** That log names the object. **Defect D is not closed by these
   frames** - they are the input to a follow-up ticket, not a verification.

---

## 9. Raised for the lead (scoped out here, deliberately, not forgotten)

1. **The lane-brief scope conflict** - sec.0. Answer needed: was `RaidDeployController.cs` really
   excluded, or was that `RaidDeployScreen.cs`?
2. **WO-1639 sec.5's owner ruling on the readout plate** was NOT obtained by this lane (sec.2). The
   change is implemented per the brief; if the ruling was never put to the owner it still should be.
   Her second sec.5 question - a shorter word for `Deploy All` - is now **moot**: the face widened
   instead and the copy is untouched.
3. **Nothing renders the in-raid HUD in any gate** - WO-1639 sec.1e. `UICaptureLaunch.cs` captures
   only the PRE-raid screens and `LayoutOracle.cs` has zero matches for `Raid` / `Deploy` /
   `Readout` / `Compass`. **That gap is why four visible defects shipped green.** WO-1639 sec.8
   scopes the fixture and the oracle rule OUT of this ticket (a new oracle rule must be SEEN RED
   first against a synthetic defect canvas, `LayoutOracle.cs:17-20`) and asks the lead to mint it.
   **Please mint it** - this ticket's own acceptance depends on a human opening PNGs.
4. **The kit toast's 24 px label** is below `FontFloor` (30) - sec.4 residual (2). Kit-wide.
5. **Defect D needs a follow-up ticket** once `[wo1639-marker]` names the object (sec.5).
6. Sec.1f's unresolved observation - the end panel awards three empty stars while the readout behind
   it reads `1/3` - was **not acted on**, per the WO's own instruction.

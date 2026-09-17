# WO-1824 RESULT — the Heart plate reads at the font floor on TAP, not in the dock

**Status:** IMPLEMENTED
Code + oracle landed. Unity gate NOT run — GO was not given, so compilation and the suite are unproven.
**Date:** 2026-09-17
**Owner ruling implemented (verbatim):** *"we could do a on tap make larger and on tap again reduce size?"*

---

## 1. The re-derivation the lead asked for: does retiring the minimap free enough? **NO, at both aspects.**

The premise behind the question does not hold. **The minimap plate's seat is already occupied by the
Night Market card**, not by a minimap: `HudLayoutBands.NightMarketCardBand` (`HudLayoutBands.cs:178-186`)
hangs the 320x156 card from `MinimapMount.yMax`, and `HudLayoutBands.cs:140-145` states in its own words
that the card **"TAKES THE PLATE'S SEAT"** and that `HudKitController` constructs no `HudMinimapWidget`.
So WO-1825 frees the region *below* the card (y 0.420..0.483), which is **not adjacent to `HeartMount`**.
Height only transfers upward if the card slides down, and that is bounded by the gear row's top at
0.473 (`HudLayoutBands.cs:153-157`; `DockMount.yMax` = 0.470 at `:96`, already over-subscribed).

| Aspect | refH (`CanvasReferenceSize`, `:519-527`) | card = 156 px | card top must be ≥ | freed (card top 0.645 today) | + the 0.008 top gap | **total available** |
|---|---|---|---|---|---|---|
| 2670x1200 | 965.3 | 0.16161 | 0.63461 | **0.0104** | 0.008 | **0.0184** |
| 1920x1080 | 1080.0 | 0.14444 | 0.61744 | **0.0276** | 0.008 | **0.0356** |

**Required, all four rows at 30 px with the CURRENT row fractions.** The binding fraction is **0.19**
(objective 0.53..0.72 and rekindle 0.08..0.27, `HudKitController.cs:2513-2518`), not the name row's 0.22.
A 30 px line needs 30 x `LineHeightFactor` 1.2 = **36 px**, so the plate must be 36 / 0.19 = **189.5 ref px**,
i.e. `HeartMount.height` = 189.5 / (refH x `HeartPlateOfMount` 0.96):

| Aspect | required `HeartMount.height` | today (`:84`) | **deficit** | available | verdict |
|---|---|---|---|---|---|
| 2670x1200 | **0.2045** | 0.135 | **0.0695** | 0.0184 | **NO** (short 3.8x) |
| 1920x1080 | **0.1827** | 0.135 | **0.0477** | 0.0356 | **NO** |

→ **Fell back to the tap-to-expand plan, as briefed.** Side benefit: it touches **no**
`HudLayoutBands.cs`, so the same-file collision with WO-1825 dissolves entirely.

Correction recorded while re-deriving: **WO-1824 §4 option 3's "36 / 0.26 = 138.5 px, satisfied by
today's plate" is FALSE at the binding aspect** — today's plate is 125.1 px there (0.26 x 125.1 = 32.5 < 36).
It holds only at 1920x1080 (140 px). Do not act on that line.

## 2. What shipped

**Glance (docked plate): byte-for-byte unchanged.** Same bands, same 20..26 / 16..18 fonts, same plate
(125.1 x 477.1 ref px at 2670x1200; 140.0 x 426.6 at 1920x1080). It is now *documented and pinned* as a
deliberate glance rather than an open defect.

**Reading state (tapped overlay):** a separate `ScreenSpaceOverlay` canvas at sorting **30500** — above the
town HUD, below the kit's two modal tiers (`BuildObsidianModal` 31000, `BuildConfirmModal` 32000). Built
from the kit's *parts* (`BuildModalCanvas` + `Scrim` + `Panel` + `ObsidianCloseButton`) rather than
`BuildObsidianModal`, deliberately: the chrome's `layout.body` is measured from frame art at runtime and an
Editor case cannot instantiate it, so the row arithmetic would be unmodelable. A plain Panel at literal
anchors is exactly modelable — which is what Case 10c now does.

| | compact (glance) | expanded (reading) |
|---|---|---|
| name / Heartfire font | 20..26 | **30..44** |
| objective / rekindle font | 16..18 (kit-clamps to 18) | **30..44** |
| panel, 2670x1200 | 477.1 x 125.1 ref px | **1116.9 x 617.8** |
| panel, 1920x1080 | 426.6 x 140.0 ref px | **998.4 x 691.2** |
| row band | 0.19 x plate = 23.8 / 26.6 px | 0.16 x panel = **98.8 / 110.6 px** (needs 36) |

**Trigger:** the whole plate is the tap target (`HeartPlateTap`, an alpha-0 raycast `Image` + `Button`
stretched over `_heartPlate.Root`). It clears `ElarionUiKit.MinTouchPx` (112) on both axes at both aspects
by construction — 125.1/140.0 tall, 477.1/426.6 wide — so no extra affordance was added and
`ClampMinTouch` is deliberately not called (it resizes by `sizeDelta`; this rect is anchor-stretched).
**State:** one HUD-local field `_heartExpandedCanvas`, session-local, not persisted (no schema bump).
**Close, stated precisely:** the scrim is a raycast-blocking full-screen Image on a higher-sorted canvas,
so while expanded the compact plate's own Button is unreachable — a second tap "on the plate" actually
lands on the **scrim**, which is what closes it. The owner's "tap again reduces size" therefore holds, but
the path that guarantees it is `Scrim(..., CloseHeartExpanded)`, not `ToggleHeartExpanded`'s close branch
(that branch is unreachable while open and is kept only so a programmatic toggle is still symmetric). The
kit's one standard Close is the third, live, path. Case 10c pins the scrim call for exactly this reason.
**Teardown:** `OnDisable`, `OnDestroy` and `ApplyPosture` all call the idempotent `CloseHeartExpanded()` —
the canvas is outside the HUD root, so occupancy cannot reach it and it would otherwise draw over a raid HUD.
**Copy:** mirrored off the compact labels each tick, never re-resolved from Core — one resolution path, so
the reading state cannot disagree with the glance the player just tapped. **FlowTrace on both edges.**

## 3. Files changed

- `Assets/_Modules/HUD/Kit/HudKitController.cs`
  - `:164-176` — the four expanded-label fields + the design note
  - `:2557-2567` — the WO-1823 EXCEPTION block extended: WO-1824 ruled it, the mount-growth remedy is
    named as arithmetically unavailable, "do not finish WO-1823 by raising these"
  - `:2585-2625` — the expanded state's float-literal constants (screen band, four row bands, 30..44, 30500)
  - `:2750-2777` — the `HeartPlateTap` target inside `BuildHeartStatus`
  - `:2779-2901` — `ToggleHeartExpanded` / `OpenHeartExpanded` / `RepaintHeartExpanded` / `CloseHeartExpanded`
  - `:5710-5714` (ApplyPosture), `:6109-6114` (LateTick repaint), `:6334-6345` (OnDisable/OnDestroy)
- `Assets/Editor/Regression/HudLabelFitRegression.cs`
  - `:1646-1661` — Case 10c's header rewritten for the two states, with the deficit arithmetic
  - `:1776` — the new call, inside `Case10_HeartfireInsidePlate`
  - `:1810-2000` — `Case10c_GlanceAndReadingStates` + `PinGlance`: pins the glance's four sub-floor
    numbers as intentional; requires the tap listener, the scrim close, the standard Close and a teardown
    call; parses the expanded literals; asserts `HeartExpandedFontMin >= ElarionUi.FontFloorMobile`, panel
    and rows on screen, rows clear of `DefaultCloseZone`, rows disjoint, every row seats 30 x 1.2 at both
    aspects, and MEASURES the Heartfire PlateLabel + all five `HeartObjectiveCopy` state strings at 30 px
    against the expanded row width
  - `:2984-2994` — Case 16d's note no longer prescribes growing `HeartMount` (it cannot land); it now
    records the tap as the ruled remedy
- No new suite: Case 10 is already registered. `HudLayoutBands.cs` and `HudMinimapWidget.cs` **untouched**
  (WO-1825's silo).

## 4. Verification

- **Checked negative:** no regression enumerates modal builders and demands `PanelManager.Register /
  NotifyOpened / NotifyClosed` of them. `BattleMonthlyRegression`'s `[one-screen-owner]` lifecycle lint
  (`:1165-1183`) is scoped to files under `_Modules/Wallet`; the other `NotifyOpened` hits under
  `Assets/Editor/Regression` construct handles for their own fixtures. So this glance overlay deliberately
  carries **no** `PanelManager` handle and **no** `WorldHold` (unlike the blocking item picker at
  `HudKitController.cs:3486`) — it is a read-only glance the player owns, not a screen the arbiter arbitrates.
- `python tools/gate_brace.py` on both files → `GATE_BRACE_SUMMARY bad=0 of 2`, exit 0.
- NUL scan: 0 bytes in both. Raw braces 442/442 and 230/230.
- ⚠ **NOT run: Unity.** No `COMPILE_GATE_OK`, no `REGRESSION_OK`, no capture — GO was not given, and
  `Get-Process Unity` was deliberately not raced. Everything below is therefore **unproven**:
  compilation; whether Case 10c's new measurements pass (`MeasureLineWidthPx` can return < 0 headlessly,
  which is handled as a note, not a failure); whether the alpha-0 `Image` raycast target behaves as
  expected on device; and what the expanded panel actually looks like.

## 5. Open, for the owner

1. **The Heartfire flames are not reproduced in the reading state** — it shows the `PlateLabel` text only
   ("Heartfire 3/3 (raids)"). WO-1419 ruled the flame sprite is the mark on the *glance*; whether the
   reading state should carry three larger flames is a visual ruling, not an implementer's call.
2. **Cross-lane, for WO-1825:** reclaiming `MinimapMount` breaks `NightMarketCardBand`
   (`HudLayoutBands.cs:182-185`) and `ResolveLeftColumn` (`:542-548`), both of which read it. That lane
   must re-seat the Night Market card.
3. **The one lever that would flip §1 to YES** is the Night Market card leaving the left column, or
   `DockMount` moving. Both are unruled and outside this silo.

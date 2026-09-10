# WORK ORDER 1664 — Five controls are authored under the touch floor and rely on the runtime clamp

**Status:** READY TO IMPLEMENT
**Silo:** UI / layout (ElarionUiKit consumers). No gameplay, no economy, no scene files.
**Raised by:** DEVICE-FRAMES-4 lane, 2026-09-10, from a live Seeker capture.
**Number:** pre-assigned by the coordinator. `CLI_LANES_WO_NUMBERS.md` deliberately NOT edited by this lane.

---

## 1. The measured defect

APK **2026.09.10.363866** (`dumpsys` read before capture: `versionCode=363866 versionName=2026.09.10.363866`),
Seeker `SM02G4061955851`, 2670x1200 landscape, app PID **10705** (confirmed by `pidof`).
Log: `Builds/device-frames/2026-09-10_1137_363866_logcat.txt`.

`grep "CLAMP FIRED"` returns **five** lines. Every one is the runtime clamp rescuing a control the
author seated **under** `ElarionUiKit.MinTouchPx`:

```
09-10 11:33:08.608 W Unity : [touch-oracle] CLAMP FIRED TitleScreenUI/TitleButtons/ObsBtn_Continue: authored 399.5x69.5 -> grown 399.5x112 (1.0x on W, 1.61x on H). Author the band above MinTouchPx(112); do not rely on the clamp.
09-10 11:33:08.608 W Unity : [touch-oracle] CLAMP FIRED TitleScreenUI/TitleButtons/ObsBtn_Start New: authored 399.5x69.5 -> grown 399.5x112 (1.0x on W, 1.61x on H). Author the band above MinTouchPx(112); do not rely on the clamp.
09-10 11:33:08.609 W Unity : [touch-oracle] CLAMP FIRED TitleScreenUI/TitleButtons/ObsBtn_Play Intro: authored 399.5x69.5 -> grown 399.5x112 (1.0x on W, 1.61x on H). Author the band above MinTouchPx(112); do not rely on the clamp.
09-10 11:33:08.702 W Unity : [touch-oracle] CLAMP FIRED HubRepairAffordance/HubRepairCanvas/ObsBtn_REPAIR ALL: authored 386.6x101.4 -> grown 386.6x112 (1.0x on W, 1.1x on H). Author the band above MinTouchPx(112); do not rely on the clamp.
09-10 11:33:36.471 W Unity : [touch-oracle] CLAMP FIRED WelcomeBackUI/ObsidianPanel/PanelContent/ObsBtn_COLLECT: authored 357.4x89.2 -> grown 357.4x112 (1.0x on W, 1.26x on H). Author the band above MinTouchPx(112); do not rely on the clamp.
```

**These five are NOT a regression from this build.** The prior lane's log
`Builds/device-frames/2026-09-10_1019_363786_logcat.txt` (APK 363786, PID 8062) carries the same five,
**byte-identical in their numbers**, at 10:13:36.520 / 10:13:36.612 / 10:14:23.255. That log had a
**sixth** — `CurrencyChip_Gold` — which is gone in 363866 (WO-1660 landed). Old total 6, new total 5.
So this ticket is the standing residue, long-lived and stable, not fallout from WO-1660.

### Why "the clamp rescued it" is still a defect

`LayoutOracle.cs:235-241` states the consequence in the gate's own words:

> `ClampMinTouch will grow it SYMMETRICALLY about its centre at runtime and spill it into both
> neighbours. Author the band AT the floor.`

The clamp grows about the **centre**, so a rescued face eats into whatever sits above and below it.
On the title row three faces share one band, so each grows into the row's own chrome; the warning text
itself (`ElarionUiKit.cs:1117`) ends `do not rely on the clamp.`

---

## 2. The producer of each band (cite these, do not re-derive)

### 2A. The three title faces — `Assets/_Modules/Onboarding/TitleController.cs`

`BuildButtonColumn` (`:275`) authors ONE row and slots the faces inside it:

- `:281-283` — the row band: `rt.anchorMin = new Vector2(0.20f, 0.045f); rt.anchorMax = new Vector2(0.80f, 0.135f);`
  i.e. **0.090 of screen height**, with the comment `Kept a healthy ~7% screen-height so the touch
  target stays tappable on mobile` — a claim the device log now refutes.
- `:299-303` — the three entries (`Continue` Green / `Start New` Yellow / `Play Intro` Gray).
- `:311-316` — each face is built at `new Vector2(x0, 0.10f), new Vector2(x1, 0.90f)` of that row,
  i.e. **0.80 of the row's height**.

**The arithmetic closes exactly.** The effective reference height on this device is **965 px** (the
number the runtime prints itself: `[Flow:Manage] bands(px): canvas=965 ...`, 11:36:27.793 in the same
log). `0.090 x 965 x 0.80 = 69.48` — the logged authored height is **69.5**. The band is short by
**42.5 px**, which is what the 1.61x growth restores.

To seat 112 px the face needs `112 / (0.80 x 965) = 0.1451` of screen height for the row, against
today's 0.090.

### 2B. `HubRepairAffordance` — `Assets/_Modules/Village/Walls/HubRepairAffordance.cs`

`:535-536`:
```
_button = ElarionUiKit.Button(_canvas.transform, "REPAIR ALL", ElarionUiKit.ButtonKind.Confirm,
    ToastZoneMin(0f, 0.72f), ToastZoneMax(0f, 0.72f), OnClick);
```
The rect comes from the SHARED zone via `ToastZoneMin`/`ToastZoneMax` (`:576-587`), which slice
`HudLayoutBands.ToastZoneSlice(from, to)`. The `0.72` height fraction of that zone resolves to
**101.4 px** — 10.6 px under the floor.

⛔ **READ `:528-534` BEFORE TOUCHING THIS ONE.** The file carries an explicit standing instruction:

> `THE FIX IS NOT A THIRD HAND-PICKED SEAT. It is the shared convention: HudLayoutBands.ToastZone ...
> Do NOT author a rect here again; if the zone is wrong, it is wrong for everybody and it moves in ONE place.`

So the fix here is **the fraction or the zone**, never a literal rect at this call site.

### 2C. `WelcomeBackPopup` — `Assets/_Modules/Village/Harvest/UI/WelcomeBackPopup.cs`

`:235-237`:
```
var collect = ElarionUiKit.BuildObsidianButton(_modal.chrome.content.transform, "COLLECT",
    ElarionUiKit.ObsidianButtonStyle.Style1, ElarionUiKit.ObsidianButtonColor.Yellow,
    new Vector2(0.37f, 0.045f), new Vector2(0.63f, 0.155f), CollectAndDismiss);
```
Band = **0.110** of the shell content, resolving to **89.2 px** — 22.8 px under the floor.

⚠ **This file already knows the rule and applied it to a DIFFERENT face.** `:91-96` reasons
explicitly about `MinTouchPx (112)` and sizes `DoorRowH = 0.21f` (`:97`) so the WO-1408 door "fills it
EXACTLY (anchors 0..1), so the face clears the floor on both". The sole action button on the same
modal was never given the same treatment. `AddReadyBand` (`:560-563`) then deliberately seats the
raid door on the **same y band 0.045..0.155** as COLLECT, so whatever moves must move both.

---

## 3. Why the headless captures pass all five

Three separate reasons. Do not assume one explanation covers the set.

### 3A. The gate-time rule EXISTS and would catch these

`LayoutOracle.Audit` raises `FindingKind.SubTouchFloorBand` with the message prefix
`SUB-TOUCH-FLOOR BAND` (`Assets/_Modules/Core/UI/LayoutOracle.cs:234-241`), and
`UICaptureLaunch.cs:6058-6060` routes that prefix into the WO-1060 Assert A tally. The message even
computes the host rect and prints the required fraction. **The oracle is not missing; its verdict is
not being read on these paths.**

### 3B. Title — captured, but only the GLYPH oracle is reported there

`UICaptureLaunch.RunFrontDoorCaptureHeadless` (`Assets/Editor/UICaptureLaunch.cs:2247-2262`) is the
path that captures Title and Login. It calls `ResetGlyphOracle()` (`:2250`) and `ReportGlyphOracle()`
(`:2255`) and emits `FRONT_DOOR_CAPTURE_OK 6/6` — and it **never resets `_touchPanelsChecked` nor
calls the touch report**. Compare `RunGooglePlayLoginCaptureHeadless` immediately below, which does
(`:2283`, and asserts `_touchPanelsChecked == LandscapeTargets.Length` at `:2323-2325`).

WO-1644's own comment sits at `:2258-2259`:
> `WO-1644: this path was the odd one out -- Title/Login were measured by AuditGeometry and the
> verdict thrown away.`

That was fixed **for the glyph half only**. The touch half is still thrown away on exactly this path —
which is why three sub-floor title faces ship green.

**And Title is not suppressed.** `TouchBaseline` (`Assets/Editor/UICaptureLaunch.cs:6128-6140`) holds
only `"ArmyMuster"` and `"EquipDrawer"` (the `ManageScreen` entry is a deleted comment). So wiring the
touch report on the front-door path is expected to turn these three RED immediately — which is the
point.

### 3C. Repair and Welcome-Back — not captured at all

`grep -rn "WelcomeBackPopup\|HubRepairAffordance" Assets/Editor/` returns **only source-text and logic
regressions** — `AwaySummaryReportRegression.cs` (which reads `WelcomeBackPopup.cs` as *text* and
tests `FormatAwaySpan` / `AwayTextFor` as pure functions) and `RepairProbeSeverityRegression.cs`.
**No `Capture*` entry point ever builds either canvas**, so `LayoutOracle.Audit` has no rect to
measure and Assert A cannot fire at gate time. These two are invisible to the gate, not baselined out
of it.

*(Precedent for saying this plainly: the `TouchBaseline` array's own note at `:6134-6137` records the
same discovery for ManageScreen — "there is still no Capture\*ManageScreen\* entry point, so this entry
was suppressing nothing measurable either way".)*

### 3D. A note on the runtime half

`UICaptureLaunch.cs:4153-4156` records why the *runtime* clamp ring stays empty in edit mode:
> `In edit mode ClampMinTouch's guard MonoBehaviour never gets a LateUpdate, so this is expected to
> stay empty -- the gate-time assert is AuditGeometry rule 4, which measures the AUTHORED band instead.`

So the device is the only place `CLAMP FIRED` can print, and the gate's equivalent is the authored-band
rule in 3A. Fix the wiring in 3B/3C, not the runtime ring.

---

## 4. Fix shape, per site

**The invariant: author the band AT `ElarionUiKit.MinTouchPx` at the DRIVER, and let the clamp go
quiet because it has nothing left to rescue.** Never silence the oracle, never add a baseline entry
(`TouchBaseline` is shrink-only by owner ruling, `:6188`).

### 4A. Title row (`TitleController.BuildButtonColumn`)

⚠ **Three faces share one band, so height growth is a ROW-level change, not a per-face one.**
Grow the row at `:281-282` from 0.090 to at least **0.1451** of screen height so the 0.10..0.90 inner
slot resolves to >=112 px. Prefer pinning the bottom (`anchorMin.y` stays 0.045) and raising
`anchorMax.y` to **~0.190**, so the row grows UPWARD off a fixed bottom margin.

**Collision check that must be part of the change:** the copy block above it is built by
`BuildTitleTextBlock` (`:253-268`) — title at y `0.70..0.84` (`:255`), series at `0.655..0.70`
(`:259`), **tagline at `0.60..0.65`** (`:265`). A row top at 0.190 clears the tagline's 0.60 floor by
a wide margin, so on today's numbers there is no collision — **but prove it in the capture, do not
inherit this sentence.** Also confirm the `TitleActionWell` inset (`:290-292`, `0.01..0.99` x
`0.08..0.92`) and the tray sprite still frame the taller row.

Note the row is built with **two or three** entries depending on `HasExistingSave()` (`:297-303`), so
the width slot changes but the HEIGHT problem is identical in both shapes. Fix the height; do not
touch the `slotGap`/`slotW` distribution at `:306-309`.

### 4B. `HubRepairAffordance`

Raise the height fraction at `:536` (`0.72`) so the resolved band reaches 112 px — `0.72 x 112/101.4
= 0.795`, so **~0.80** is the arithmetic floor; round up rather than down.
**If `HudLayoutBands.ToastZoneSlice`'s zone is itself too short to seat 112 px at any fraction, the
change belongs in `HudLayoutBands` and it moves for every toast** — that is what `:528-534` instructs.
Say which of the two you did and why, in the RESULT.

### 4C. `WelcomeBackPopup`

Grow the COLLECT band at `:237` from `0.045..0.155` (0.110) to at least `112/89.2 x 0.110 = 0.138`,
i.e. **`0.045..0.183`** or wider.
⛔ `AddReadyBand` (`:560-563`) explicitly seats the raid door on the **SAME y band** and its comment
says `COLLECT keeps its exact ...` — **move both together, or the two faces desynchronise.** Read that
method to the end before editing either number.

---

## 5. RED-first pins (write the pin, watch it FAIL, then fix)

1. **`front-door-reports-the-touch-oracle`** — assert `RunFrontDoorCaptureHeadless` resets the touch
   tally and emits the touch marker, the same way `:2323-2325` asserts it for the Google Play path.
   This is the pin that makes 4A provable at gate time; it must FAIL on today's `UICaptureLaunch.cs`.
2. **`title-row-seats-the-touch-floor`** — arithmetic-on-source-parsed-constants, the house style used
   by `InventoryArmoryRailRegression` (`:17`) and `ManageTroopsTrainDoorRegression` (`:1210`): parse
   `TitleController`'s row anchors and the inner face fraction, multiply, assert `>= ElarionUiKit.MinTouchPx`
   at 965 ref height. Must FAIL at 69.5 today.
3. **`title-row-clears-the-tagline`** — parse the tagline's `0.60..0.65` band (`:265`) and the row's
   `anchorMax.y`, assert no overlap. This pin is what stops 4A from being fixed into a new defect.
4. **`repair-all-seats-the-touch-floor`** and **`collect-seats-the-touch-floor`** — same arithmetic
   shape against `HubRepairAffordance.cs:536` and `WelcomeBackPopup.cs:237`. Both must FAIL today.
5. **`welcomeback-collect-and-raid-door-share-one-band`** — assert the two y bands are still equal
   after the change, so 4C cannot half-land.
6. Strongly preferred, and the only thing that closes 3C properly: **a capture entry point for the
   welcome-back modal and the repair affordance**, so `LayoutOracle.Audit` measures them like every
   other panel. If that is out of scope for this ticket, say so explicitly in the RESULT and leave
   pins 4/5 as the arithmetic proof — do not let "no capture exists" quietly become "no proof needed".

---

## 6. What NOT to touch

- ⛔ **`ElarionUiKit.MinTouchPx` (`Assets/_Modules/Core/UI/ElarionUiKit.cs:347`, `112f`).** It is the
  P0 floor, read by at least six regressions by name. Lowering it "fixes" all five and is the exact
  inversion of this ticket.
- ⛔ **The clamp itself** — `ClampMinTouch` / `UiKitMinTouchGuard` (`ElarionUiKit.cs:1147-1190`) and
  `RecordClampGrowth` (`:1131-1145`). The clamp is the safety net and the warning is the instrument
  (§12: instrumentation is permanent, never stripped). The goal is a clamp with nothing to do, not a
  quieter clamp.
- ⛔ **`TouchBaseline` (`UICaptureLaunch.cs:6128-6140`)** — shrink-only by owner ruling (`:6188`).
  Do not add Title, WelcomeBack or HubRepair to it.
- ⛔ **The `CurrencyChip_Gold` seating that WO-1660 just landed.** It is proven clean on this log
  (`grep -c CurrencyChip_Gold` = 0) and is not part of this change.
- ⛔ **`HudLayoutBands.ToastZone` — unless** you have concluded per 4B that the zone itself cannot seat
  112 px. That is a cross-module move affecting every toast; it needs its own line in the RESULT.
- Do not re-hand-author a rect inside `HubRepairAffordance` (`:528-534`).
- No scene files. No `.unity` edits. This is code-authored UI throughout.

---

## 7. Acceptance

1. A FRESH device logcat from a build carrying the fix, on a clean `logcat -c` + force-stop + relaunch,
   walked to the title, through the welcome-back modal, and with the repair affordance shown:
   `grep -c "CLAMP FIRED"` = **0**. Marker-and-count, not exit code, on a fresh log (§11B).
2. Each of the three RED pins in §5 flipped RED -> GREEN, with the RED run quoted in the RESULT.
3. `FRONT_DOOR_CAPTURE_OK 6/6` still emits, and the touch marker now emits on that path too.
4. Frames opened, not just captured: the title row, the welcome-back modal and the repair affordance,
   showing no collision with neighbouring copy.
5. `**Status:**` flipped in this file in the same commit as the work, `.RESULT.md` written, both paths
   reported.

## 8. Evidence index

- `Builds/device-frames/2026-09-10_1137_363866_logcat.txt` — the five lines, PID 10705, APK 363866
- `Builds/device-frames/2026-09-10_1019_363786_logcat.txt` — the same five plus the retired gold-chip sixth, PID 8062, APK 363786
- `Builds/device-frames/2026-09-10_1134_363866_town.png` — town frame from the same session

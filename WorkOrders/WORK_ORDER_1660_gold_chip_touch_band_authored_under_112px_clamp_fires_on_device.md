# WORK ORDER 1660 — The town gold chip's touch band is authored UNDER MinTouchPx, and only the runtime clamp rescues it

**Status:** IMPLEMENTED - awaiting gate
**Result:** `WorkOrders/WORK_ORDER_1660_gold_chip_touch_band_authored_under_112px_clamp_fires_on_device.RESULT.md` (HUD-CHIP lane, 2026-09-10; no Unity run in-lane — the gate is the lead's)
**Silo:** HUD / kit layout (`Assets/_Modules/HUD/Kit/HudKitController.cs`, `Assets/Editor/UICaptureLaunch.cs`)
**Origin:** DEVICE-FRAMES-3 lane, WO-1658 device measurement, 2026-09-10
**Device under test:** Seeker SM02G4061955851, APK **2026.09.10.363786** (`dumpsys package com.denellestudios.echoesofelarion` → `versionCode=363786 versionName=2026.09.10.363786`, read before launch), 2670x1200 landscape, one app PID **8062** for the whole session.

---

## 1. THE MEASUREMENT (captured, not inferred)

Log: `Builds/device-frames/2026-09-10_1019_363786_logcat.txt` (7,005,654 bytes), PID 8062. Verbatim:

```
09-10 10:17:26.858  8062  8103 W Unity   : [touch-oracle] CLAMP FIRED VillageHUD (bootstrapped)/HudAreasHost/Area_ActionRail/Widget_resourceChipsCollapsed/CurrencyChip_Gold: authored 398.1x103.5 -> grown 398.1x112 (1.0x on W, 1.08x on H). Author the band above MinTouchPx(112); do not rely on the clamp.
```

Frame: `Builds/device-frames/2026-09-10_1017_363786_town_goldchip_plus4.png` — the town HUD with the gold chip reading `1326` and its `+4` hint. The chip is on screen and tappable; **nothing is visibly broken.** That is the point: the defect is silent on the device and silent in every gate.

The authored short side is **103.5 ref px, 8.5 px under `ElarionUiKit.MinTouchPx` (112)** (`Assets/_Modules/Core/UI/ElarionUiKit.cs:347`). `ClampMinTouch` grew it **symmetrically about its centre** at runtime — which, per the kit's own rule text (`LayoutOracle.cs:239-241`), *"will grow it SYMMETRICALLY about its centre at runtime and spill it into both neighbours. Author the band AT the floor."*

---

## 2. THE PRODUCER (read at source 2026-09-10)

`Assets/_Modules/HUD/Kit/HudKitController.cs:3203-3204` — the collapsed town chip:

```csharp
_resGoldOnly = ElarionUiKit.CurrencyChip(pool, ElarionUiKit.CurrencyKind.Gold,
    new Vector2(0.05f, 0.45f), new Vector2(1f, 1f), primary: true, tag: "Gold");
```

The chip's height is authored as a **fraction of its host band** (`y 0.45 .. 1.0`, i.e. 0.55 of the ActionRail widget), not in px. `HudKitController.cs:3231` then calls `ElarionUiKit.ClampMinTouch(tapBtn)` on the Button added at `:3229`.

Adjacent and load-bearing: `HudKitController.cs:1787` declares
```csharp
private const float RailChipHeightPx = ElarionUiKit.MinTouchPx;   // 112
```
so the rail *has* a px-denominated floor constant already. The gold chip does not use it — it uses the 0.45 fraction, and 0.55 of the resolved ActionRail band came out at 103.5 px on the 2670x1200 Seeker. **That is the whole defect: one rail element authored by fraction while its siblings are authored in px.**

`ClampMinTouch` / `UiKitMinTouchGuard` / `RecordClampGrowth`: `Assets/_Modules/Core/UI/ElarionUiKit.cs:1069-1145`. The warning string is emitted at `:1144`.

---

## 3. WHY `UI_TOUCH_OK` DID NOT CATCH IT — the oracle is structurally blind to this canvas

The gate-time detector exists and is correct. `LayoutOracle` ASSERT A (`Assets/_Modules/Core/UI/LayoutOracle.cs:200-238`) measures every Button that carries a `UiKitMinTouchGuard` and reds `SUB-TOUCH-FLOOR BAND` when the shortest side is under `MinTouchPx`. It even prints the host's measured size so the fix can be authored in px. It is reached from `UICaptureLaunch.AuditGeometry` (`:5922`), called from `RenderCanvasToPng` (`:5787`), and reported as `UI_TOUCH_OK <clean>/<checked> panels` at `UICaptureLaunch.cs:6253`.

**It never sees this chip, because the town HUD canvas is not in the gated capture run.**

- The HUD builder *is* wrapped for capture: `CaptureAdaptiveHudOnce` (`UICaptureLaunch.cs:2992`) constructs `DeNelle.HUD.VillageHudController` + `DeNelle.HUD.Kit.HudKitController` and shoots `AdaptiveHudPeaceful_` / `AdaptiveHudGearOpen_` / `AdaptiveHudCombat_` (`:3066`, `:3076`, `:3091`).
- But its only caller is the **separate** entry point `RunAdaptiveHudCaptureHeadless` (`UICaptureLaunch.cs:1687-1690`).
- `RunCaptureHeadless` (`UICaptureLaunch.cs:548`) — the entry point that emits `UI_CAPTURE_OK`, `UI_TOUCH_OK` and `UI_GLYPH_OK` — enumerates its panels at `:591-634`. **`CaptureAdaptiveHudOnce` is not in that list.** (Verified by reading the full `count += Capture…` sequence; it runs FoundingEchoCard → PauseMenu → EchoRoster → … → BuildCollections, with no HUD entry.)

So the answer to "which capture panel builds that chip": **none, in the gated run.** `BuildResourceChips` is only reached from the ungated `AdaptiveHud` entry point. This is the identical hole the harness already documents against itself at `UICaptureLaunch.cs:604-607` for the Night Market — *"Was not in this list, so the geometry oracle was structurally blind to the one screen that takes money."*

The runtime half is also, by design, not a substitute: `ElarionUiKit.cs:1103-1109` states that `LateUpdate` never runs in an edit-mode capture, so the clamp ring records **zero** growths headlessly on a genuinely broken panel. Both halves of the oracle missed it for different, documented reasons, and the first evidence of eight months of sub-floor authoring is an owner device log.

---

## 4. FIX SHAPE

**A. Author the band at the floor, at the driver.** In `HudKitController.BuildResourceChips` (`:3203-3204`), stop deriving the collapsed chip's height from a parent fraction. Give it a px height denominated in `RailChipHeightPx` / `ElarionUiKit.MinTouchPx` the way `:1787` already does — e.g. top-anchor the chip and set `sizeDelta.y = RailChipHeightPx`, or size the widget band so the 0.45..1.0 fraction resolves at or above 112. **Do not type `112` or `103.5` anywhere**; read `ElarionUiKit.MinTouchPx`.

⚠ The `+N` hint and the expanded stack both hang **below** the chip (`ResHintHeightPx = 38f` at `:1859`, `ResourceExpandedStack` at `:3262-3269`, `HudRailClearance` gap `RailGapPx = 6f` at `:1819`). Growing the chip's band moves both. Re-measure the rail's total occupancy against the ActionRail top after the change and confirm ResRow_Crystals is still on screen — the arithmetic for that is already written at `:1841-1848`.

**B. Close the oracle hole, or say out loud that it stays open.** Either add `count += CaptureAdaptiveHudOnce`-equivalent coverage into `RunCaptureHeadless`'s list so the town HUD is audited by the same three markers as every other panel, or record in the harness header that the HUD canvas is audited only by `RunAdaptiveHudCaptureHeadless` and wire that entry point into the pre-ship gate. **A fix to A without B leaves the next fractional rail band exactly as undetectable as this one was.**

---

## 5. RED-FIRST PIN (PROD-008 / WO-1138)

Add to an existing HUD regression (`Assets/Editor/Regression/HudUiRegression.cs` is the natural home — it already asserts the resource rail at `:1359-1393`):

1. **RED case:** build the peaceful town HUD, find `CurrencyChip_Gold`'s Button, and assert its resolved shortest side `>= ElarionUiKit.MinTouchPx`. **Run it against HEAD first and confirm it FAILS at 103.5** — a pin that was never seen red is not a pin.
2. **Clamp-ring case:** call `ElarionUiKit.ClearClampGrowths()` before building the HUD and assert `ElarionUiKit.ClampGrowths` is **empty** afterwards for any rail control. A non-empty ring is a WO-1060 Assert A failure by the kit's own definition (`ElarionUiKit.cs:1128-1130`).
3. Read `MinTouchPx` from the constant in the assert. Never restate the number in the test.

---

## 6. WHAT NOT TO TOUCH

- ⛔ **Do not change `ClampMinTouch`'s behaviour.** `ElarionUiKit.cs:1082-1086` rules it explicitly: it is the correct runtime rescue for a build that shipped wrong, and weakening it turns a visible defect into an **untappable** one. This ticket authors the band correctly; the clamp stays exactly as it is and simply stops firing.
- ⛔ **Do not remove or quieten the `[touch-oracle] CLAMP FIRED` warning.** It is the only reason this defect is known. CLAUDE.md §12 — instrumentation is permanent.
- ⛔ **WO-697 AMOUNT NO-FIT LAW — do not put a fit call on the chip's amount.** `ElarionUiKitObsidian.cs:965` states the kit law verbatim: *"WO-697 kit law: NO FitSingleLine here — a currency [amount]"*. The chip owns its own formatting (`CompactNumber`, `:825`) and its width sync (`:831`). Fixing a HEIGHT band must not smuggle in a width/fit change to the amount.
- ⛔ **Do not touch `ResHintHeightPx = 38f`** (`HudKitController.cs:1859`). That band is WO-1658's fix for the `+4` hint's font floor and the arithmetic behind the 38 is written out at `:1849-1855`. It was measured green on this same device run (see §7).
- ⛔ Do not touch `hud-areas.json` occupancy or the `resourceChipsCollapsed` widget key — the WO-1221 bounce at `HudKitController.cs:3233-3248` explains why the expanded rows are children of this chip, and moving them is a different ticket.

---

## 7. ACCEPTANCE

- [ ] The RED-first pin was seen **failing** on HEAD, and passes after the fix.
- [ ] `COMPILE_GATE_OK` + `REGRESSION_OK <n>/<n>` on fresh logs (markers, never exit codes).
- [ ] A fresh device logcat from a town session on the new APK contains **no** `[touch-oracle] CLAMP FIRED` line naming `CurrencyChip_Gold`. Marker absence on a fresh log is the proof; a clean gate is not.
- [ ] The `+4` hint still renders at or above `ElarionUiKit.FontFloor` — i.e. the fresh logcat still carries **no** `TextFitGuard` relaxation for `.../CurrencyChip_Gold/Label`. (Baseline for this: APK 363786 produced `TextFitGuard CENSUS armCalls=354 armed=354 declinedNotPlaying=0 declinedNullText=0 evaluated=97 relaxed=0 stillBlank=0`, `2026-09-10_1019_363786_logcat.txt`, 10:18:58.675 — zero relaxations across the whole session.)
- [ ] Frame the town HUD on device and confirm the rail's four expanded rows still seat under the taller chip.

---

## 8. EVIDENCE INDEX

| Claim | Source, opened 2026-09-10 |
|---|---|
| Chip authored 103.5 px, clamped to 112 | `Builds/device-frames/2026-09-10_1019_363786_logcat.txt`, PID 8062, 10:17:26.858 |
| Chip visible and tappable on device | `Builds/device-frames/2026-09-10_1017_363786_town_goldchip_plus4.png` |
| Fractional authoring | `Assets/_Modules/HUD/Kit/HudKitController.cs:3203-3204` |
| Clamp call site | `Assets/_Modules/HUD/Kit/HudKitController.cs:3231` |
| Rail already has a px floor constant | `Assets/_Modules/HUD/Kit/HudKitController.cs:1787` |
| `MinTouchPx = 112f` | `Assets/_Modules/Core/UI/ElarionUiKit.cs:347` |
| Clamp is deliberately unchangeable | `Assets/_Modules/Core/UI/ElarionUiKit.cs:1082-1086` |
| Headless clamp ring records zero | `Assets/_Modules/Core/UI/ElarionUiKit.cs:1103-1109` |
| The gate-time assert that should have caught it | `Assets/_Modules/Core/UI/LayoutOracle.cs:200-238` |
| Reached only via `RenderCanvasToPng` → `AuditGeometry` | `Assets/Editor/UICaptureLaunch.cs:5787`, `:5922` |
| `UI_TOUCH_OK` emit site | `Assets/Editor/UICaptureLaunch.cs:6253` |
| HUD capture exists but is a separate entry point | `Assets/Editor/UICaptureLaunch.cs:1687-1690`, `:2992` |
| HUD is absent from the gated panel list | `Assets/Editor/UICaptureLaunch.cs:548`, list at `:591-634` |
| The identical hole, already conceded in-harness | `Assets/Editor/UICaptureLaunch.cs:604-607` (Night Market) |
| WO-697 amount no-fit law | `Assets/_Modules/Core/UI/ElarionUiKitObsidian.cs:965` |

## 9. UNPROVEN

- I did **not** measure whether the ActionRail band itself is authored short or whether 0.55 of a correct band is simply too small — both produce 103.5 px and the log prints only the child's rect. The implementer should print the host rect first (`LayoutOracle.cs:225-232` already composes exactly that sentence when the finding fires headlessly).
- I did **not** run `RunAdaptiveHudCaptureHeadless` to confirm it *would* red on this chip. That is a one-command check and should be step 1 of the lane, because it decides whether §4B is "wire an existing green oracle into the gate" or "the oracle misses it there too".
- No other aspect ratio was measured. 103.5 is the 2670x1200 number only.

## Device evidence (lead, 2026-09-10 11:34, APK 2026.09.10.363866, PID 10705)

`Builds/device-frames/2026-09-10_1137_363866_logcat.txt`: `CurrencyChip_Gold` CLAMP FIRED lines = 0 (the 363786 log had exactly one: authored 398.1x103.5 -> grown 398.1x112); the other five CLAMP FIRED lines (title ObsBtn_Continue/Start New/Play Intro 399.5x69.5, REPAIR ALL 386.6x101.4, WelcomeBack COLLECT 357.4x89.2) are byte-identical in both logs = pre-existing, WO-1664. Chip plate now y 60-159 (was 52-153), +N hint y 194-216 (was 189-210) ~4 ref px lower as predicted. Acceptance 1 closes.

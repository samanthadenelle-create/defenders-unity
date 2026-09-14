# WORK ORDER 1718 — Settings > Help > Dev Tools renders ~4x oversized with overlapping rows

**Status:** READY TO IMPLEMENT
**Minted:** 2026-09-14 (read-only RCA lane)
**Silo:** HUD / UI Toolkit panel scaling — `Assets/_Modules/HUD/AdminOverlay.cs` ONLY
**Severity:** Dev/tester tooling unusable on a real device (the owner cannot read or hit the buttons)

---

## 1. Owner report (verbatim)

> "so inside the settings button you click the help button and inside that is a dev tools button, once
> you click that everything is sized way too large"

Evidence: `docs/handoffs/devtools_panel_oversized_text.png` (live Seeker device capture). Text fills the
screen width, rows are drawn on top of each other, and the red "✦ Admin — owner-only" title overlaps the
button rows.

---

## 2. ⚠ FIRST CORRECTION — the panel is NOT `DevPanelController`

The triage brief scoped this to `Assets/_Modules/DevTools/DevPanelController.cs`. **That is wrong, proven
by the button captions in the screenshot:**

| Screenshot caption | `AdminOverlay.cs` | `DevPanelController.cs` |
|---|---|---|
| `MAX all buildings` | `:261` `Button("MAX all buildings", …)` — exact | `:755` `"MAX all buildings (tier 4)"` — different |
| `MAX troop types` | `:262` — exact | `:850` — also present |
| `Grant Iron Bastion` | `:263` `"Grant Iron Bastion town"` | `:852` `"Grant Iron Bastion town (skip grind)"` |
| `✦ Admin — owner-only` title | `AdminOverlay.cs:209` | not present |

`DevPanelController` is additionally **deprecated and gated off** (its own header, `:1-7`: "F10 dev menu
retired — use Settings -> DevTools (AdminOverlay)"). **Scope of this WO is `AdminOverlay.cs`.**

---

## 3. Proven cause

### 3a. The panel's runtime `PanelSettings` never sets a scale mode — so it runs Unity's default, `ConstantPhysicalSize`

`AdminOverlay.TryBuild` creates its OWN runtime PanelSettings and sets **only** the name, the theme
stylesheet and the sorting order:

- `Assets/_Modules/HUD/AdminOverlay.cs:111-130` — `ScriptableObject.CreateInstance<PanelSettings>()`,
  `ps.name = "AdminRuntimePanelSettings"`, theme borrow, `_document.panelSettings = ps`.
- `:137-139` — `sortingOrder = 32000`.
- **`scaleMode` and `referenceResolution` are never assigned.** Proven, not assumed:
  `git log -S"scaleMode" -- Assets/_Modules/HUD/AdminOverlay.cs Assets/_Modules/HUD/HelpMenu.cs`
  returns **zero commits** — the string has never existed in either file.

Unity's default for an unset `PanelSettings` is **`ConstantPhysicalSize`, referenceDpi 96, fallbackDpi 96**
— read at source 2026-09-14 from Unity's own C# reference
(`https://raw.githubusercontent.com/Unity-Technologies/UnityCsReference/6000.0/Modules/UIElements/Core/GameObjects/PanelSettings.cs`):

```csharp
[SerializeField] private PanelScaleMode m_ScaleMode = PanelScaleMode.ConstantPhysicalSize;
[SerializeField] private float m_ReferenceDpi = DefaultDpi;   // 96
[SerializeField] private float m_ReferenceResolution = new Vector2Int(1200, 800);
```
*(Project is Unity `6000.4.8f1`, `ProjectSettings/ProjectVersion.txt`; the published reference branch is
6000.0 — the initializer is the nearest at-source read available from this machine.)*

`ConstantPhysicalSize` scales the whole panel by **actual screen DPI / 96** (Unity manual,
`UIE-Runtime-Panel-Settings`: *"it tries to find the actual DPI value of the screen, and compares it to the
Reference DPI. If they're different, the system scales the UI accordingly"*). On the Seeker
(6.36" 2340x1080 ≈ 400 dpi) that is **≈ 4.2x**. Nothing in the panel opts out of it.

### 3b. Every OTHER runtime-PanelSettings creator in the repo sets the mode explicitly — AdminOverlay is the only one that doesn't

| Creator | Sets scale mode? |
|---|---|
| `Assets/Editor/BattleSceneBuilder.cs:268-271` | `ScaleWithScreenSize`, 1920x1080, match 0.5 |
| `Assets/Editor/IntroFlowSceneBuilder.cs:251-254` | `ScaleWithScreenSize`, 1920x1080, match 0.5 |
| `Assets/Editor/OnboardingSceneBuilder.cs:328-331` | `ScaleWithScreenSize`, 1920x1080, match 0.5 |
| `Assets/_Modules/Village/Arena/ArenaDefensePaletteUI.cs:95-97` | `ScaleWithScreenSize`, 1080x1920 |
| `Assets/_Modules/DevTools/DevBootstrap.cs:146-148` | `ConstantPixelSize` — deliberate, commented |
| **`Assets/_Modules/HUD/AdminOverlay.cs:111-130`** | **NOTHING — defaults to ConstantPhysicalSize** |
| `Assets/_Modules/HUD/HelpMenu.cs:184-196` | nothing either (see §7 — out of scope) |

The authored assets agree: `Assets/_Modules/Onboarding/Generated/OnboardingPanelSettings.asset` and
`Assets/_Modules/BattleATB/Generated/BattlePanelSettings.asset` both carry `m_ScaleMode: 2`
(= `ScaleWithScreenSize`), `m_ReferenceResolution: {x: 1920, y: 1080}`.

### 3c. The fonts it draws are 1080x1920 REFERENCE px, so the DPI multiplier lands on an already-reference-sized ladder

`AdminOverlay` styles through `ElarionUi`, whose ladder is explicitly reference-px:

- `Assets/_Modules/Core/UI/ElarionUi.cs:99-115` — *"these are REFERENCE-px against the 1080x1920 portrait
  canvas"*; `FontTitle = 88`, `FontBody = 50`, `FontLabel = 40` (was 24 / 15 / 13).
- `ElarionUi.StyleButton` (`:360-375`) sets `fontSize = FontBody` (50) on every AdminOverlay button
  (`AdminOverlay.cs:367-375` calls it).

The **rest of the game's UI is uGUI** and gets that reference scaling from a CanvasScaler:
`Assets/_Modules/Core/UI/ElarionUiKit.cs:107-111` — `ScaleWithScreenSize`, `referenceResolution =
(1080, 1920)`, `MatchWidthOrHeight`, `match = 0.5f`. On a 2340x1080 landscape device that resolves to a
scale factor ≈ 1.10 (the kit records the measured canvas at `ElarionUiKit.cs:4053`: *"2340x1080 landscape,
CanvasScaler 1080x1920 match 0.5 => 2119.6 x …"*), i.e. reference px ≈ device px. **That is the parity
AdminOverlay's UI Toolkit panel is missing.** Instead of ~1.1x it gets ~4.2x → `FontBody` 50 renders at
≈ 210 device px, and the card's `maxWidth = 560` (`AdminOverlay.cs:197`) renders ≈ 2350 px — the full
screen width. That is precisely the screenshot.

### 3d. The OVERLAPPING rows — same root, plus one aggravating override

Not a separate bug, and not a missing spacing value: the row BOXES are sized in numbers that predate the
font ladder, and UI Toolkit's default `overflow` is `visible`, so oversized glyphs draw straight out of
their box and onto the neighbouring row.

- `AdminOverlay.cs:372` — `b.style.minHeight = 38;  // compact debug rows (override the 44 default)`.
  It overrides `ElarionUi.StyleButton`'s `minHeight = TapTarget` (= **88**, `ElarionUi.cs:198/372`), and the
  comment's "44 default" is itself stale — `TapTarget` was raised to 88 for mobile.
- So each row is a 38px box + 2px margins (`ElarionUi.cs:373`) ≈ **42px of pitch** carrying a bold 50-px
  line box. **The text overflows its own row even at 1:1**; at the 4.2x physical scale it overflows by
  three to four rows, which is exactly the illegible stack in the capture.
- Same story for the card metrics, all pre-ladder desktop px: `minWidth 420 / maxWidth 560` (`:197`),
  `padding 22/26` (`:199-200`), `title.marginBottom = 6` (`:213`), `_status.marginTop = 8` (`:362`).
- The "✦ Admin" title at `FontTitle` 88 (`:210`) renders ≈ 370 px and overlaps everything below it — the
  "watermark" the owner saw is the panel's own title.

**Fixing 3a alone is NOT sufficient** — at scale parity a 38px row still cannot hold a 50px line.

### 3e. NOT explained, and deliberately not guessed

The screenshot also shows a **dimmer, diagonally offset duplicate of the same captions**. Candidates, none
proven: a screen-record/scroll smear in the capture itself, or the fact that `AdminRuntimePanelSettings` is
deliberately shared by more than one UIDocument — `Assets/_Modules/DevTools/AutoPilotLogGuards.cs:65-69`
records that `MusicToggleHud` (+50 sortingOrder) and `LevelUpSkillPopup` **borrow** it. **Do not chase the
ghosting as part of this fix.** Re-capture after the scale fix; if the doubling survives, file it separately.

---

## 4. Is it a regression? YES — a latent one that became visible on a dated commit

1. **2026-06-13 — `eb2294ff5`** *"fix(devtools): AdminOverlay owns its PanelSettings — Settings→Dev Tools no
   longer disappears"*. Before this, the overlay **borrowed** another document's PanelSettings (typically
   `OnboardingPanelSettings`, `m_ScaleMode: 2` = ScaleWithScreenSize @1920x1080). Owning its own one fixed
   the "renders nothing" defect and **silently dropped the scaler**.
2. **2026-07-04 — `70e29ca11`** *"fix(ui): mobile-legible text — canonical font ladder to mobile standards"*
   tripled the ladder (15 → 50 body) for the 1080x1920 reference canvas. uGUI screens absorb that through
   the CanvasScaler; the unscaled UI Toolkit panel does not.
3. `git log -S"scaleMode"` on the file: **empty** — no UI-scale work has ever been done on `AdminOverlay.cs`.

---

## 5. The fix (for the implementing lane)

**File: `Assets/_Modules/HUD/AdminOverlay.cs` only.**

1. In `TryBuild` (`:111-130`), on the created `PanelSettings`, set — next to the existing `name` assignment:
   - `ps.scaleMode = PanelScaleMode.ScaleWithScreenSize;`
   - `ps.referenceResolution = new Vector2Int(1080, 1920);` (match the uGUI CanvasScaler at
     `ElarionUiKit.cs:109` so both toolkits resolve the same reference px)
   - `ps.match = 0.5f;` (matches `ElarionUiKit.cs:111`; note Unity's default `m_Match` is **0.0**, so it
     must be set explicitly)
   - Add a comment naming WHY (Unity's default is ConstantPhysicalSize → dpi/96 ≈ 4.2x on the Seeker) so the
     next seat cannot delete it as noise.
2. Delete the `b.style.minHeight = 38;` override at `:372` (let `StyleButton`'s `TapTarget`=88 stand — it is
   the mobile touch floor), or if a compact dev row is still wanted, floor it at `ElarionUiKit.MinTouchPx`
   and never below the line height of `FontBody`.
3. Re-express the card box metrics in the same reference scale as the ladder: `minWidth/maxWidth` (`:197`),
   `padding` (`:199-200`). A 560-reference-px card cannot hold 50-px bold captions — widen it (a percentage
   of the panel, or ≥ 900 reference px) so captions fit on one line.
4. Leave `sortingOrder = 32000` on both the document and the PanelSettings exactly as-is (`:137-139`) — that
   is a separate data-proven fix (`0692b7e73`).

---

## 6. Acceptance criteria

- [ ] **Config assertion (headless, catches the root):** a regression under `Assets/Editor/Regression/`
      builds/obtains the AdminOverlay `UIDocument` and asserts
      `panelSettings.scaleMode == PanelScaleMode.ScaleWithScreenSize`,
      `referenceResolution == (1080,1920)`, `match == 0.5f`. It must FAIL against today's HEAD.
- [ ] **Layout assertion (headless, catches the overlap):** at a device-realistic **2340x1080** panel size,
      after a layout pass, for every `Button` in the overlay: `worldBound.height >= ` the resolved text line
      height, and no two sibling `worldBound`s intersect. (This half reproduces headless — the DPI half does
      NOT: in batchmode/editor `Screen.dpi` falls back to `fallbackDpi` 96, so `ConstantPhysicalSize`
      renders 1:1 and hides the bug. **That is why no existing capture ever caught this** — say so in the
      RESULT.)
- [ ] Registered in the suite so it runs under `DataRegression.RunAll`; judged by the `REGRESSION_OK <n>/<n>`
      marker on a FRESH log, never the exit code (CLAUDE.md §8).
- [ ] **Device/visual proof:** a capture of the panel at 2340x1080 (headless capture PNG, opened and looked
      at) showing every caption on one line inside its own row, plus the owner's felt-test on the Seeker —
      PO closes (§13).
- [ ] Brace + `python tools/gate_brace.py` clean on `AdminOverlay.cs`; `COMPILE_GATE_OK` on a fresh log.
- [ ] This WO's `**Status:**` flipped and `WORK_ORDER_1718_*.RESULT.md` written in the SAME commit as the fix.

---

## 7. What NOT to touch

- ⛔ **Do not touch the main HUD / Help / Settings uGUI Canvas setup.** `ElarionUiKit.cs:107-111`
  (CanvasScaler 1080x1920 / match 0.5) is CORRECT and is the reference this fix conforms TO. Changing it
  would move every screen in the game.
- ⛔ **Do not change the `ElarionUi` font ladder** (`ElarionUi.cs:111-115`). Its own header forbids it:
  *"If a zone now overflows with the larger text, that's a per-screen layout follow-up (note it) — do NOT
  shrink the ladder back to hide overflow."* This WO is that per-screen follow-up.
- ⛔ **Do not touch `Assets/_Modules/DevTools/DevPanelController.cs`.** It is the deprecated F10 panel and
  is not what the owner is looking at (§2).
- ⛔ **Do not change `sortingOrder`** (`AdminOverlay.cs:137-139`) or the `#if DEVELOPMENT_BUILD || UNITY_EDITOR
  [|| TESTER_BUILD]` guard structure (`:230-275`) — WO-1512 states the guard asymmetry is deliberate.
- ⚠ **Out of scope, noted:** `HelpMenu.cs:184-196` creates `HelpRuntimePanelSettings` with the same omission,
  and `MusicToggleHud` / `LevelUpSkillPopup` borrow `AdminRuntimePanelSettings`
  (`AutoPilotLogGuards.cs:65-69`) so they inherit whatever this panel sets. Do not re-scope this ticket to
  them; if they render oversized too, mint a follow-up.

---

## 8. Quick-fix assessment

The **root** is one field (plus two companions) on one object — ~4 lines at `AdminOverlay.cs:111-130`.
But shipping only that leaves the rows overlapping, because the box metrics (38px rows, 560px card) were
never re-scaled when the font ladder tripled on 2026-07-04. Budget **one small layout pass over
`AdminOverlay.BuildUi`** plus the two regression cases above.

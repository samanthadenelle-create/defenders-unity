# WORK ORDER 1718 — RESULT (implementation lane, 2026-09-14)

**Status of this RESULT:** IMPLEMENTED, edit-only. NOT gated, NOT committed, NO Unity run by this lane
(the brief's standing constraint is *"do NOT run Unity, gate, build, or commit — the lead gates once"*,
memory `one-seat-fires-unity-while-lanes-are-open`). See §5 for what the lead must still run.

---

## 1. Changes

| File:line | Change |
|---|---|
| `Assets/_Modules/HUD/AdminOverlay.cs:113-127` | The three PanelSettings fields + a WO-1718 comment naming why they exist, set immediately after `ps.name` inside the `if (_document.panelSettings == null)` block of `TryBuild`. |
| `Assets/_Modules/HUD/AdminOverlay.cs:210-219` | Card sizing: `minWidth 420 / maxWidth 560` → `minWidth 560 / maxWidth Length.Percent(92)`, with the math in a comment. |
| `Assets/_Modules/HUD/AdminOverlay.cs:395-400` | `b.style.minHeight = 38;` **deleted** (replaced by a comment recording the deletion and why). |
| `Assets/Editor/Regression/AdminPanelScaleRegression.cs` | NEW — the `[admin-panel-scale]` suite, 3 cases. |
| `Assets/Editor/Regression/DataRegression.cs:2039` | ONE line registering it. `git diff --numstat` = `1 0` on that file; 5021 → 5022 lines. Nothing else in that file was touched. |

### Exact PanelSettings values set (`AdminOverlay.cs:125-127`)

```csharp
ps.scaleMode          = PanelScaleMode.ScaleWithScreenSize;
ps.referenceResolution = new Vector2Int(1080, 1920);
ps.match               = 0.5f;
```

`screenMatchMode` is deliberately **not** assigned: Unity's initializer is already
`PanelScreenMatchMode.MatchWidthOrHeight` (the authored `OnboardingPanelSettings.asset` carries
`m_ScreenMatchMode: 0`, read at source 2026-09-14). The regression **asserts** it rather than the fix
setting it, so a future Unity default change is caught instead of being papered over. The API names were
read at source from `Assets/Editor/BattleSceneBuilder.cs:268-271` and
`Assets/_Modules/Village/Arena/ArenaDefensePaletteUI.cs:92-94`, not guessed.

These values match the uGUI CanvasScaler at `ElarionUiKit.cs:107-111`, so both toolkits now resolve the
same reference px.

## 2. The row-height decision — REMOVED the override, did not lower it

`b.style.minHeight = 38` is **deleted entirely**; `ElarionUi.StyleButton`'s `minHeight = TapTarget`
(**88**, `ElarionUi.cs:198`) now stands. Why removal and not a smaller floor:

- 88 is the project's named mobile touch floor (`ElarionUi.cs:198`; memory `mobile-ui-touch-contrast-standard`,
  `MinTouchPx = 112` for uGUI). A dev panel the owner must hit on a phone has no case for undercutting it.
- A `FontBody` (50) bold line box is ≈ **58 px**. 38 could not hold it *at 1:1*, which is why the rows
  overlapped even before the DPI multiplier; 88 holds it with ~30 px of breathing room.
- The override's own comment ("override the 44 default") was already stale — `TapTarget` was raised
  44 → 88 for mobile — so keeping a tuned number here would just re-seed the same drift.

Row pitch is now 88 + 2 + 2 margins = **92 reference px**. The card is capped at `maxHeight 86%` and the
button column is already a `ScrollView` (F8-11), so a taller list scrolls rather than overflowing.

## 3. The other pre-ladder numbers the ticket flagged — math, then a decision

Panel reference size at the device frame: 2340x1080 with reference (1080,1920) and match 0.5 gives
scale = 2^(0.5·log2(2340/1080) + 0.5·log2(1080/1920)) = **1.104**, i.e. a **2119 x 978** reference canvas —
which matches the measurement already recorded in `ElarionUiKit.cs:4053` (*"2340x1080 landscape,
CanvasScaler 1080x1920 match 0.5 => 2119.6 x …"*). So post-fix the panel is at **1.10x**, not 4.2x.

- **`minWidth 420 / maxWidth 560` (`:197`) — CHANGED, this one does NOT survive the math.** The longest
  live captions are ~43–45 chars (`"Queue clock: real time   (nothing to reset)"`, the armed
  `"SURE? Wipes save+prefs, archives dials, QUITS"`). At `fontSize 50` with ~0.5 em average advance that
  is ≈ **1125 px** of glyphs + 32 px of button padding ≈ **1157 reference px**. A 560 px card cannot hold
  one of them on a line at ANY scale factor. Fix: no explicit width (the flex column sizes the card to
  its widest child, so it grows to fit the captions) with `maxWidth = Percent(92)` ≈ 1950 px of headroom
  at the device frame, and `minWidth 560` as the floor for a short list.
- **`padding 22/26` (`:199-200`) — LEFT ALONE.** 22/26 reference px renders at 24/29 device px at 1.10x:
  a normal gutter beside 55-device-px text, and it is an inset, not a constraint — it cannot cause
  overflow. Widening it would only shrink the content box.
- **`title.marginBottom = 6` (`:213`) — LEFT ALONE.** The title sits in a flex column above a rule; a
  6 px gap is tight-looking, never overlapping. The overlap the owner saw was the 88-px title's glyphs
  at 4.2x escaping a box laid out for 1.0x, which the scale-mode fix removes at the root.
- **`_status.marginTop = 8` (`:362`) — LEFT ALONE.** Same reasoning; `_status` is the last child, with
  `whiteSpace = Normal` so it wraps rather than overflows.

Only the one value whose math actually fails was changed — per the brief ("adjust only if your math
shows they'd still overflow").

## 4. Regression — `Assets/Editor/Regression/AdminPanelScaleRegression.cs` (`[admin-panel-scale]`)

Builds a real `AdminOverlay` on a throwaway `HideAndDontSave` GameObject and calls the public
`TryBuild(null)` directly (Awake does not fire in edit mode — `AdminOverlay` is not `[ExecuteAlways]`;
`UIDocument` is, so its panel exists). `Run` has its own try/catch that returns `false` with the
exception text, because the `Guard.Try` registration wrapper would otherwise swallow a throw into a
silent pass. Host, runtime PanelSettings and RenderTexture are all `DestroyImmediate`d.

- **CASE 1 — config (the root).** Asserts `scaleMode == ScaleWithScreenSize`,
  `referenceResolution == (1080,1920)`, `match == 0.5f`, `screenMatchMode == MatchWidthOrHeight` on the
  live `UIDocument.panelSettings`.
  **It fails against pre-fix HEAD by construction, not by hope:** those fields were never assigned
  (`git log -S"scaleMode"` on the file returns zero commits, §3a of the WO), so the object carries
  Unity's initializers — `ConstantPhysicalSize`, `(1200,800)`, `match 0.0` — and all three assertions
  miss. This is a static-value read, so no display, DPI or rendered pixel is involved.
- **CASE 2 — row metrics, also pre-fix-failing.** Every `Button`'s `minHeight` must be
  `>= max(ElarionUi.TapTarget, fontSize·1.15)`; on HEAD every row reads **38 vs a floor of 88** → fail.
  Also asserts the card's `maxWidth` is a **percent**, not a pre-ladder pixel literal.
- **CASE 3 — measured layout, best effort.** Points the PanelSettings at a
  **2340x1080** RenderTexture (a targeted panel takes its size from the texture), drives a layout tick,
  then asserts each button's `worldBound.height >= MeasureTextSize(...).y` and that no two **sibling**
  `worldBound`s intersect. Edit-mode runtime panels are not ticked by the player loop, so the tick is
  reached by reflection on `BaseVisualElementPanel` — **the method names were verified present in
  `UnityEngine.UIElementsModule.dll` for 6000.4.8f1 before the strings were written**
  (`ValidateLayout` ×2, `UpdateWithoutRepaint` ×1, `targetTexture` ×2, `MeasureTextSize` ×1), not guessed.
  If the panel/tick/measurement is unavailable in batchmode the case logs
  `layout=UNPROVEN(<reason>)` and does **not** fail the shared gate — missing tooling is named, never
  silently passed, but it is also not a product defect. Reflection in an *Editor regression* is not a
  CLAUDE.md §10 "bridge script" violation; `DataRegression` itself reflects throughout.

### ⛔ Say it plainly: the DPI half of this bug CANNOT be caught by a batchmode regression

With no real display, `Screen.dpi` falls back to `PanelSettings.fallbackDpi` = **96**, which is the same
as `referenceDpi` = 96 — so `ConstantPhysicalSize` renders at exactly **1.0x** headless and in the editor.
The pre-fix panel therefore looks *correct* in every screenshot, capture and AutoPilot frame ever taken
of it. **That is precisely why no existing capture caught this**, and why a rendered-pixel oracle is
structurally incapable of pinning it. The thing that pins the fix is **CASE 1, a static config read** —
CASE 3's pixels are corroboration, not the gate. This reasoning is written into the file's header so the
next seat cannot mistake the capture for the proof.

## 5. What this lane did NOT do — for the lead

- **No headless capture was run and no PNG was opened.** The brief's item 5 (`RunCaptureHeadless` + open
  the PNG) conflicts with its own edit-only constraint; the constraint wins. **I have not seen the panel
  render and make no claim about it.** `grep -n "AdminOverlay" Assets/Editor/UICaptureLaunch.cs` returns
  **nothing** — there is no dev-panel capture case to run, and that file carries another lane's
  uncommitted edits (`git status --short` → ` M`), so this lane deliberately did not add one. If the
  owner wants the visual proof, it needs a new capture case in `UICaptureLaunch.cs` — a small follow-up,
  best minted once that file is clean.
- **No gate run.** `COMPILE_GATE_OK` + `REGRESSION_OK <n>/<n>` on a FRESH log are the lead's, judged by
  marker, never exit code (CLAUDE.md §8).
- **Watch on the first gate run:** `[admin-panel-scale]` should print `layout=MEASURED(...)` or
  `layout=UNPROVEN(<reason>)`. An `UNPROVEN` line is expected-and-acceptable in batchmode; it is not a
  failure, but it does tell us CASE 3 bought nothing on this machine.

## 6. Knock-on, noted not chased

`MusicToggleHud` (+50 sortingOrder) and `LevelUpSkillPopup` **borrow** `AdminRuntimePanelSettings`
(`AutoPilotLogGuards.cs:65-69`, cited in WO §7), so they now inherit the scaler too. That is the correct
direction — they were unscaled for the same reason — but it is a visual change to two surfaces this WO
did not test. Flag it for the owner's felt-test; do not treat it as a defect of this fix.

`HelpMenu.cs:184-196` has the identical omission (`HelpRuntimePanelSettings`) and is explicitly out of
scope per WO §7. Untouched. It wants its own ticket.

The dim diagonal ghost-duplicate in the capture (WO §3e) was **not** investigated, per the WO's own
instruction. Re-capture after this fix; if it survives, it is a separate ticket.

## 7. Gate proof from this lane

```
$ python tools/gate_brace.py Assets/_Modules/HUD/AdminOverlay.cs \
      Assets/Editor/Regression/AdminPanelScaleRegression.cs \
      Assets/Editor/Regression/DataRegression.cs
GATE_BRACE_SUMMARY bad=0 of 3
exit=0

NUL / raw-brace scan:
Assets/_Modules/HUD/AdminOverlay.cs                     NUL=0  braces 146/146
Assets/Editor/Regression/AdminPanelScaleRegression.cs   NUL=0  braces  42/42
Assets/Editor/Regression/DataRegression.cs              NUL=0  braces 1216/1216
```

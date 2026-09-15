# WO-1743 RESULT — Night Market rows overlap

**Status:** DONE (edit-only lane) — ⚠ **NOT VERIFIED.** No Unity, no gate, no build, no git was run.
**Date:** 2026-09-15

---

## Root cause (file:line)

`Assets/_Modules/GooglePlay/GooglePlayStorefront.cs:136-146` hand-placed 18 fraction-anchored rows —
`float top = .90f, height = .095f; y1 = top - i * height; y0 = y1 - .082f;` — into a modal body that
measures **~542 reference px** at the owner's 2670x1200 (`BuildModalCanvas` → 1080x1920 ref, match
0.5, `ElarionUiKit.cs:107-111` → ~2148x965 ref canvas).

That is a **~51 px pitch carrying ~44 px rows**. `UiKitMinTouchGuard.LateUpdate`
(`ElarionUiKit.cs:1179-1184`) then grows every row to `MinTouchPx = 112` **symmetrically about its
centre**, spilling each one ~34 px into both neighbours.

The kit names this exact failure in its own words at `ElarionUiKitObsidian.cs:574-580`
("...was every menu HAND-PLACING fraction-anchored buttons: the MinTouchPx(112) floor grows each
button, and on a short modal body those grown rects overlap...").

**Confirming measurement, not inference:** with 18 `storeVisible` packs (counted in
`Assets/Resources/Data/Canonical/packs.json`: 29 rows, 11 hidden) the loop reaches
`y1 = .90 - 17*.095 = -0.715`. Index 6 lands at `.33`, index 7 at `.235`, and the *fixed* Restore band
was `.20`–`.285` — exactly between them. That is precisely where `RESTORE PURCHASES` appears in
`logs/device/store-listing.png`, right after `CORD OF TIMBER` (index 5). The capture's interleave
order is predicted by the arithmetic.

Not a billing defect. The `UNAVAILABLE` text is separate Play Console configuration and was not
touched.

---

## What changed

**`Assets/_Modules/GooglePlay/GooglePlayStorefront.cs`** — the row loop is replaced by the kit's
FIT-OR-SCROLL seam, `ElarionUiKit.MakeScrollZone` (§1.14), over a `StoreListZone` at body `.175`–`.92`.
Four disjoint band constants (`SubtitleY0/Y1`, `ListZoneY0/Y1`, `HintY0/Y1`, `StatusY0/Y1`). A new
private `AddListRow` sizes **the content column's direct child** by
`sizeDelta = (0, RowHeightPx)`, `RowHeightPx = ElarionUiKit.MinTouchPx`. Subtitle moved into its own
band and `FitSingleLine`d. A reserved words-only overflow hint
(`"<N> items -- scroll the list for more"`, ASCII). One `FlowTrace.Step` + `DumpZoneLayout` so the
built shape is readable from a log.

`GooglePlayStorefrontVM.cs` is **byte-for-byte untouched** — no product, SKU or billing change.

## Scroll view or resize — and why

**Scroll view.** 18 packs + Restore + Deletion = 20 controls. At the 112 px touch floor with an 8 px
gap that is `20*112 + 19*8 = 2392 reference px` of content in a ~542 px body. No row height is both
legible and legally tappable and fits; shrinking rows would only re-arm the clamp that caused the
defect.

Restore and Deletion are the **tail of the scroll list**, not a pinned footer: pinning them costs
242 ref px and would leave `542 − 38 − 54 − 27 − 242 = ~181 px` of well — under two rows visible.
As tail rows they keep the full touch floor and stay reachable. Their labels are self-describing
sentences, so nothing rests on colour (owner is red/green colourblind).

**Two knobs worth naming so the lead does not read them as drift:**

* `RowGapPx = 8f` diverges from the kit's `BuildButtonColumn` default of `18f`. Deliberate: on the
  real ~542 px body it buys ~3.4 visible rows instead of ~3.1. The gap affects spacing only, never
  the floor.
* The hint reads `"<N> items -- scroll the list for more"` **unconditionally**. With 20 rows in a
  ~3-row well that is always true today, but if the catalog ever shrank to fit the well, the sentence
  would lie. Known degenerate, left as-is; the fix is a `total > visible` test once a measured row
  count is available at build time.

---

## The regression

**Name:** `GooglePlayStoreLayoutRegression` — tag `[google-play-store-layout]`
**File:** `Assets/Editor/Regression/GooglePlayStoreLayoutRegression.cs`
**Standalone:** `run-unity-method DeNelle.Editor.Regression.GooglePlayStoreLayoutRegression.RunAll`
(marker `GOOGLE_PLAY_STORE_LAYOUT_OK` / `_FAIL`)

**Registration line** — `Assets/Editor/Regression/DataRegression.cs`, immediately after the
`defense-report-layout suite` line:

```csharp
DeNelle.Core.Diagnostics.Guard.Try("Regression", "google-play-store-layout suite", () => { if (!DeNelle.Editor.Regression.GooglePlayStoreLayoutRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[google-play-store-layout] " + r); });
```

**Case 1 `[source-law]`** — source-text laws (the asmdef carries `defineConstraints ["GOOGLE_PLAY"]`,
so the type is not compiled in an editor gate and cannot be referenced; this is how
`RealmStoreSingleRegistrarRegression` reaches the same file): `MakeScrollZone` present, `sizeDelta`
present, `RowHeightPx = ElarionUiKit.MinTouchPx` present, the `- i * height` loop **absent**, live
`MinTouchPx` still 112, and the four bands disjoint.

⚠ **One deliberate substitution in case 2, named rather than left implied.** The measured rows are
plain `RectTransform`s sized by `sizeDelta`, **not** `ElarionUiKit.BuildObsidianButton`. I checked
every suite in `Assets/Editor/Regression/` that mentions `BuildObsidianButton` or `AddColumnButton`
(`HeroSelectCarouselRegression`, `HelpMenuEntryRegression`, `EquipmentScreenLayoutRegression`,
`HudLabelFitRegression`, ...) — **every one is a source lint; not one builds a kit obsidian button
live in an edit-mode batchmode call.** That path walks `InstantiateBlinkPrefab` → `RpgUiCatalog.Get`
→ `StyleButtonColors` → `ClampMinTouch` → `MedievalUiSkin.ApplyButton`, and any of those throwing
headlessly would turn the suite red *for a reason that is not the layout* — a gate failure that
teaches nothing and gets the suite switched off. The geometry claim does not need the art: what is
measured is the kit **column's** rule (`childControlHeight:false` → height *is* `sizeDelta`, then the
`VerticalLayoutGroup` spaces them), which a plain rect exercises identically. **What the substitution
does NOT cover** is the kit's prefab MODE 1, where `BuildObsidianButton` can return a *nested* Button
— that half is covered only by case 1's source law and the comment on `AddListRow`.

**Case 2 `[measured-list]`** — a **geometry** check, not an eyeball. Builds the kit scroll column at
2670x1200 / 2340x1080 / 1920x1080, `Canvas.ForceUpdateCanvases()` +
`LayoutRebuilder.ForceRebuildLayoutImmediate`, then asserts: the viewport has a `RectMask2D`; every
row clears 112 px; **no two row rects intersect** (`LayoutOracle.Overlaps` — the WO-1060 engine, not a
local copy); and the well clears the subtitle / hint / status bands.

### What I saw when I made it fail

I could not run Unity in this lane, so the failure evidence is the suite's own **RED FIXTURE**,
whose arithmetic I evaluated directly. The fixture rebuilds the OLD fraction column and applies the
clamp's growth, and the suite **fails itself** if that column does *not* overlap:

```
2670x1200: band=888ref  legacy pitch=84.4 height=72.8 -> grown 112  OVERLAP=+27.6px  TRIPS
2340x1080: band=900ref  legacy pitch=85.5 height=73.8 -> grown 112  OVERLAP=+26.5px  TRIPS
1920x1080: band=994ref  legacy pitch=94.4 height=81.5 -> grown 112  OVERLAP=+17.6px  TRIPS
```

So at all three aspects the old layout produces a positive, detectable row-on-row intersection, and
`LayoutOracle.Overlaps(..., pad: 1f, ...)` returns true on it — case 2(b) would have reported
`rows 0 and 1 INTERSECT by <w> x 27.6px` against the pre-fix panel. (The suite uses the modal *band*
rather than the true body, which **understates** the overlap: on the real ~542 px body the pre-fix
spill is ~68 px, which is what the device capture shows.)

⚠ Note this is **arithmetic on the fixture's own inputs**, not a Unity run. The suite has never
executed.

**Case 1 was also made to fail, by porting its laws to Python and running them against both versions
of the real file** (`git show HEAD:...` for the pre-fix text):

```
FIXED file      (comments stripped): PASS
PRE-FIX at HEAD (comments stripped): FAIL -> no MakeScrollZone; no row sizeDelta;
    FRACTION-ANCHORED ROW LOOP PRESENT; RowHeightPx is not the kit floor;
    missing const SubtitleY0; ListZoneY0; ListZoneY1; HintY0; HintY1; StatusY1
FIXED file WITHOUT stripping:        FAIL -> FRACTION-ANCHORED ROW LOOP PRESENT
bands: list 0.920 <= sub 0.930 | hint 0.170 <= list 0.175 | status 0.110 <= hint 0.120
```

⛔ **The third line is a defect I found in my own suite and fixed.** The new code in
`GooglePlayStorefront.Build` deliberately *quotes* the defective loop it replaced, so linting the raw
file matched that comment and failed a correct panel. Case 1 now runs `StripComments` first (the same
helper, for the same reason, as `RealmStoreSingleRegistrarRegression`) so every law reads **code
only** — which also closes the opposite hole, where a panel that merely *mentioned* `MakeScrollZone`
in prose would have passed.

---

## Brace numbers

| File | `tools/gate_brace.py` | Raw `{` / `}` | NUL |
|---|---|---|---|
| `Assets/_Modules/GooglePlay/GooglePlayStorefront.cs` | clean | 22 / 22 | 0 |
| `Assets/Editor/Regression/GooglePlayStoreLayoutRegression.cs` | clean | 52 / 52 | 0 |
| `Assets/Editor/Regression/DataRegression.cs` | clean | 1221 / 1221 | 0 |

`GATE_BRACE_SUMMARY bad=0 of 3`, exit 0. Zero non-ASCII inside any string literal in either file.

---

## ⚠ NOT VERIFIED — what the lead still owes this ticket

* `COMPILE_GATE_OK` — **not run**
* `REGRESSION_OK <n>/<n> suites` on a fresh log — **not run**; the new suite has never executed
* `UI_CAPTURE_OK` + **open the PNG** — **not run**

**The acceptance gate is a headless capture PNG the lead opens, plus the owner's device frame.**
Because `UiKitMinTouchGuard.LateUpdate` never fires in an edit-mode batchmode call
(`ElarionUiKit.cs:1092-1094`), the headless capture is **necessary but not sufficient** — only the
device proves nothing grew at runtime.

**And one gate trap to carry forward:** `DeNelle.GooglePlay` compiles only under the `GOOGLE_PLAY`
define (Android/Editor). A normal editor gate does **not** compile `GooglePlayStorefront.cs` at all,
so a green `COMPILE_GATE_OK` on the default define set is **not** proof this file compiles. The real
compile proof is the Play AAB build.

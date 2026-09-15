# WORK ORDER 1743 — THE NIGHT MARKET's rows are drawn on top of each other

**Status:** DONE
*(edit-only lane — no Unity, no gate, no build, no git; those are the lead's. NOT verified: see §6 and
`WORK_ORDER_1743_night_market_rows_overlap.RESULT.md`.)*
**Silo:** UI / Google Play storefront (edit-only lane)
**Minted:** 2026-09-15 from the `CLI_LANES_WO_NUMBERS.md` banner top RECONCILED row (next free was 1743; bumped to 1744 in the same edit)
**Evidence:** `logs/device/store-listing.png` — owner's Seeker, Google Play build `2026.09.09.362625`, 2670x1200 landscape

---

## 1. The defect

The store panel is illegible. From the capture, not from a theory:

* every product row's decorative frame is drawn overlapping its neighbours — the list reads as one
  continuous smear of gold bezel;
* `Secure purchases through Google Play` is half-covered by the first row;
* `CLOSE` — modal chrome, *outside* the body — sits on top of a product row;
* `RESTORE PURCHASES` and `REQUEST ACCOUNT AND DATA DELETION` appear **interleaved between product
  rows**, seven and eight rows down;
* roughly 17 rows are stacked into a space that cannot hold them.

### What this is NOT

⛔ **The `UNAVAILABLE` on every row is a DIFFERENT defect and is out of scope.** That is Play Console
product configuration. This panel would have shipped illegible with perfectly working products, which
is the whole point of the ticket.

---

## 2. Root cause — file:line, read at source

`Assets/_Modules/GooglePlay/GooglePlayStorefront.cs:136-146` (pre-fix) **hand-placed** the rows:

```csharp
var rows = _vm.Rows;
float top = .90f, height = .095f;
for (int i = 0; i < rows.Count; i++)
{
    var row = rows[i];
    float y1 = top - i * height, y0 = y1 - .082f;
    ElarionUiKit.BuildObsidianButton(body, row.Label, ..., new Vector2(.03f, y0), new Vector2(.97f, y1), ...);
}
```

Two independent failures ride on that loop.

### (1) The touch floor ate the gap

`ElarionUiKit.BuildModalCanvas` (`Assets/_Modules/Core/UI/ElarionUiKit.cs:107-111`) scales to a
**1080x1920** reference with `MatchWidthOrHeight = 0.5`. At the owner's 2670x1200 that is a scale of
1.243, so the canvas resolves to **~2148x965 reference px**. The modal band (`.04`–`.96`) is ~888 ref
px and the body inside the chrome measures **~542 ref px** (derived from the capture's own ~51 px row
pitch, which is `.095 * 542`).

So the loop laid **~44 px rows on a ~51 px pitch**.

Every kit button is armed with `ClampMinTouch`, and `UiKitMinTouchGuard.LateUpdate`
(`Assets/_Modules/Core/UI/ElarionUiKit.cs:1179-1184`) grows anything under `MinTouchPx = 112`
**symmetrically about its centre**:

```csharp
if (h > 0f && h < MinTouchPx)
{
    float half = (MinTouchPx - h) * 0.5f;
    _rt.offsetMin = new Vector2(_rt.offsetMin.x, _rt.offsetMin.y - half);
    _rt.offsetMax = new Vector2(_rt.offsetMax.x, _rt.offsetMax.y + half);
}
```

Each 44 px row therefore became 112 px and spilled **~34 px into the row above AND the row below**.

⚠ **The kit had already written this exact failure down.**
`Assets/_Modules/Core/UI/ElarionUiKitObsidian.cs:574-580`:

> "The root cause of stacked/overlapping menu buttons (pause, help, ...) was every menu HAND-PLACING
> fraction-anchored buttons: the MinTouchPx(112) floor grows each button, and on a short modal body
> those grown rects overlap because the fraction slots are smaller than 112px. Fixed ONCE here:
> BuildButtonColumn ... Menus call these instead of anchoring by hand."

The storefront was still anchoring by hand. And `ElarionUiKit.cs:1085-1087` says what that costs:
*"by the time the clamp grows a control, the layout it was meant to protect is already spilled into
its neighbours, and nothing anywhere says so. Four panels shipped that way in three days."* This is
the fifth.

### (2) The column ran off the bottom of the body

`Assets/Resources/Data/Canonical/packs.json` ships **18 `storeVisible` packs** (29 rows, 11 hidden —
counted at source). So the loop reached `y1 = .90 - 17 * .095 = -0.715`: rows 10..18 were anchored
**below the body entirely**, and on the way down they passed straight through the *fixed* footer
bands — Restore at `.20`–`.285`, Deletion at `.105`–`.19`.

**This is the confirming measurement, not an inference.** Index 6 lands at `y1 = .33` and index 7 at
`y1 = .235`. The Restore band is `.20`–`.285` — exactly between them. And that is precisely where
`RESTORE PURCHASES` appears in the capture: after `CORD OF TIMBER` (index 5), with index 6 (`Timber
Wagon`) hidden behind it. The capture's interleave order is predicted by the arithmetic.

The two fixed footer buttons were themselves `.085` of body ≈ 44 ref px — under the floor, so they
were being clamp-grown and overlapping too.

---

## 3. The fix — scroll view, and why it had to be

**18 packs + Restore + Deletion = 20 controls.** At the 112 px touch floor with an 8 px gap that is
`20*112 + 19*8 = **2392 reference px** of content in a **~542 px** body.

There is no row height that is simultaneously legible and legally tappable and fits. Shrinking rows
is not an option — it would merely re-arm the very clamp that caused the defect, and the brief's own
floor (`MinTouchPx = 112`) forbids it. **So the list becomes a scroll view**, using the kit's existing
FIT-OR-SCROLL seam rather than new plumbing.

### What changed in `GooglePlayStorefront.Build`

| Band (fraction of body) | Content |
|---|---|
| `.93` – `1.00` | subtitle, `FitSingleLine`d, in its own band |
| `.175` – `.92` | **`ElarionUiKit.MakeScrollZone`** over a `StoreListZone` — the 20 rows |
| `.12` – `.17` | overflow hint, reserved always |
| `.01` – `.11` | status line |

The bands are **disjoint by construction**, and the scroll zone's `RectMask2D` clips the list, so a
row can never again paint over the subtitle, the status line or the chrome `CLOSE` — however long the
catalog grows.

* Rows are built by a new private `AddListRow`, which sizes **the content column's direct child** by
  `sizeDelta = (0, RowHeightPx)` where `RowHeightPx = ElarionUiKit.MinTouchPx`. Two traps are
  documented in place: `MakeScrollZone` runs `childControlHeight = false` **and** its
  `VerticalLayoutGroup` resets each child's anchors to `(0,1)`, so a row without an explicit
  `sizeDelta` collapses to zero height; and in the kit's prefab mode `BuildObsidianButton` returns a
  Button found by `FindDeep`, which may be **nested**, so sizing `btn.transform` would size a
  grandchild and leave the layout child at 0.
* Because the row height **is** the floor, `UiKitMinTouchGuard` has nothing left to grow.

### Restore + Deletion are the TAIL of the scroll list, not a pinned footer

Pinning them costs `2*112 + 18 = 242` ref px. That would leave
`542 − 38 (subtitle) − 54 (status) − 27 (hint) − 242 = ~181 px` of well — **under two rows visible**.
As tail rows they keep the full touch floor, stay reachable, and the well shows ~3.4 rows of the real
body instead. Their labels are self-describing sentences, so the distinction never rests on colour
(owner is red/green colourblind).

The overflow affordance is **words** — `"<N> items -- scroll the list for more"`, ASCII, the same
§1.14 hint `DungeonTreasurePanel` uses — never a cut-off glyph and never a colour.

---

## 4. The regression

**`Assets/Editor/Regression/GooglePlayStoreLayoutRegression.cs`** — `[google-play-store-layout]`.

⛔ `DeNelle.EditorRegression` **cannot reference `DeNelle.GooglePlay`** — that asmdef carries
`defineConstraints: ["GOOGLE_PLAY"]` and is not compiled in a normal editor gate. So the suite reaches
the panel the same way `RealmStoreSingleRegistrarRegression` does — as **source text** — and rebuilds
the geometry from **kit calls only**.

* **Case 1 `[source-law]`** — `Build` calls `MakeScrollZone`; rows carry `sizeDelta`;
  `RowHeightPx = ElarionUiKit.MinTouchPx` (a literal here would drift back under the floor); the
  `- i * height` fraction loop is **absent**; the live `MinTouchPx` still equals the suite's budget;
  and the four band constants are **disjoint** (`ListZoneY1 <= SubtitleY0`, `HintY1 <= ListZoneY0`,
  `StatusY1 <= HintY0`).
* **Case 2 `[measured-list]`** — builds the real kit widgets at **2670x1200** (the owner's frame),
  2340x1080 and 1920x1080, forces layout, and asserts: the viewport carries a `RectMask2D`; **every**
  row clears the touch floor; **no two rows intersect** (`LayoutOracle.Overlaps`, the WO-1060 engine,
  not a local copy); and the well does not intersect the subtitle / hint / status bands.
* **The RED FIXTURE.** Case 2 also rebuilds the OLD fraction column (`top .90`, pitch `.095`, height
  `.082`), applies the clamp's growth arithmetically, and **fails the suite if it does NOT overlap**.
  If the pitch arithmetic ever changes underneath the suite, its green stops meaning anything and it
  says so instead of passing quietly. (Same negative-fixture discipline as
  `DefenseReportLayoutRegression`'s `LegacyBand`.)

⚠ **Recorded so nobody over-claims it:** `UiKitMinTouchGuard.LateUpdate` **never runs in an edit-mode
batchmode call** (`ElarionUiKit.cs:1092-1094`). The suite asserts the **authored** heights. It must
never be read as proof that nothing grew on a device.

Registered in `Assets/Editor/Regression/DataRegression.cs` as the `google-play-store-layout suite`,
immediately after `defense-report-layout`.

---

## 5. Files touched

| Path | Change |
|---|---|
| `Assets/_Modules/GooglePlay/GooglePlayStorefront.cs` | the fix (scroll zone + `AddListRow` + band constants + subtitle band + hint) |
| `Assets/Editor/Regression/GooglePlayStoreLayoutRegression.cs` | **new** — the suite |
| `Assets/Editor/Regression/DataRegression.cs` | one registration line + its comment |
| `CLI_LANES_WO_NUMBERS.md` | minted 1743, bumped to 1744 in the same edit |

**Not touched** (other lanes hold them): `GooglePlayPackagingGate.cs`, `google-play-aab-build.ps1`,
`api/`, `SmartMobileCamera.cs`, `HeroTargetIndicator.cs`, `WallSegment.cs`, `RaidBaseGenerator.cs`,
`RaidBaseDresser.cs`, `WaveCelebrationManager.cs`. **No product/SKU data or billing code changed** —
`GooglePlayStorefrontVM.cs` is byte-for-byte untouched.

---

## 6. Verification — ⚠ NOT VERIFIED

This was an **edit-only** lane: no Unity, no gate, no build, no git.

* ✅ `python tools/gate_brace.py` — `GATE_BRACE_SUMMARY bad=0 of 3`, exit 0
* ✅ raw brace counts — `GooglePlayStorefront.cs` 22/22, `GooglePlayStoreLayoutRegression.cs` 52/52,
  `DataRegression.cs` 1221/1221
* ✅ zero NUL bytes; zero non-ASCII inside any string literal in either file
* ❌ **`COMPILE_GATE_OK` — not run**
* ❌ **`REGRESSION_OK <n>/<n>` — not run.** The new suite has never executed.
* ❌ **`UI_CAPTURE_OK` — not run.** No PNG has been produced or opened.

**The acceptance gate is a headless capture PNG the lead opens, plus the owner's device.** Because the
clamp is a *runtime* growth that headless cannot exercise, the device frame is the real verdict —
a green capture would be necessary, not sufficient.

⚠ **One thing a seat must check at the gate:** `DeNelle.GooglePlay` compiles only under the
`GOOGLE_PLAY` define on Android/Editor. A normal editor gate will **not** compile
`GooglePlayStorefront.cs` at all, so `COMPILE_GATE_OK` on the default define set does **not** prove
this file compiles. Case 1 of the new suite is a source-text lint precisely because of that, but the
real compile proof is the Play AAB build.

# WORK ORDER 1819 — RESULT

**Status:** IMPLEMENTED
**Date:** 2026-09-16
**Branch:** `dev` (working tree; no git, no banner touched)

---

## 1. The three defects, each measured OFF THE NEW PIXELS

Capture run by the lead: `Builds/wo1819-store-capture.log` (74 270 B, 22:04), judged by **its own marker**:

```
STORE_SKR_FLAT_CAPTURE_OK 2/2; geometry=clean; touch=clean
UI_GLYPH_OK    2/2 panels labels=75 baselined=0 unproved=0
UI_GEOMETRY_OK 2 canvases      UI_TOUCH_OK 2/2 panels
```

Zero `error CS` under `Assets/`. **Zero `TEXT TRUNCATED` lines** — the first clean glyph pass this screen
has had.

| # | Before (WO-1815 frame) | After (22:04 frame) |
|---|---|---|
| 1 banner | "Connect a wallet to buy - **prices shown in USD**" | **"Connect a wallet to buy - priced in SKR"** — read off both PNGs |
| 2 name | "Raise the Barrac" — **14 of 16** glyphs | **"Raise the / Barracks"** on two lines, and `UI_GLYPH_OK labels=75 unproved=0` says every one of the 75 labels in both frames drew **every** printable character |
| 3 sign | ink-vs-fill 17.00:1, **fill-vs-card 1.20:1** | **ink-vs-fill 11.57:1**, **fill-vs-surface 7.39:1** |

**The contrast measurement, in full** (WCAG relative luminance, sampled from
`Store_SkrFlat_Sale30_2670x1200.png`, tag located by its authored fill rather than by a guessed crop —
plate bbox `x 834..1064, y 264..337`, 231 x 74 px, 14 342 gold pixels):

```
plate fill    L = 0.5761
glyph ink     L = 0.0041
surface ring  L = 0.0347   (1364 samples, a ring just outside the plate - never the plate, never the text)
ink  vs fill     11.57:1   bar 4.5:1   PASS
fill vs surface   7.39:1   bar 3.0:1   PASS
```

Greyscale crop: `Builds/ui-capture/_wo1819_sale_tag_grey.png`.
⚠ **7.39:1, not the 11.18:1 predicted.** The prediction used `NightMarketPalette.GroundRaised` (the flat
card constant); the tag actually sits over the **pack art**, which is brighter. The predicted number was
right about the wrong surface — the measured one is the answer, and it still clears the bar by 2.5x.
**That is exactly the gap between predicting and measuring that this ticket exists to close.**

## 2. Both PNGs, opened and described

`Builds/ui-capture/Store_SkrFlat_NoSale_2670x1200.png` (2 393 211 B) — banner reads *"priced in SKR"*;
PACKS row `1000 / 9999 / 500 SKR`; MOVING row `500 / 300 / 100 SKR`; `Raise the / Barracks` wraps to two
lines and is complete; the three CLOSE-THE-GAP chips read `200 SKR`; footer *"Priced in SKR - token value
moves."* **No `$` anywhere on the frame.**

`Builds/ui-capture/Store_SkrFlat_Sale30_2670x1200.png` (2 245 150 B) — the same shelf with **six gold
`30% OFF` tags** (measured in the built tree, not assumed), each over a struck `1000 / 9999 / 500 SKR`
with `700 / 7000 / 350 SKR` beneath it. The tags now read as **signs**: a bright plate on a dark card,
legible with every hue removed.

## 3. What landed

| File | What |
|---|---|
| 10 x `Assets/Resources/Data/Canonical/<loc>.json` + 10 x StreamingAssets twins | the banner says SKR; **md5-identical per locale**; each language keeps its own prose, only the currency token moved; byte shape unchanged (LF 502 / CRLF 499 / no BOM) |
| `Assets/_Modules/Wallet/StoreStrings.cs:56-62` | the code fallback moved in the SAME change (it is a second copy of that sentence) |
| `Assets/_Modules/Wallet/StorePackCard.cs` | `NameBlockPx` — Compact gets "one line at full size **or two at the floor**" (+20 px); `SaleRibbonFill` = `NightMarketPalette.Patronage`, ink = the old near-black |
| `Assets/_Modules/Wallet/PurchaseQuoteService.cs:506-545, :660-672` | `ListEnvelope.Rate` / `ListResponse.Rate` → `double?`, and the trace says *"a FLAT SKR price - no market rate was used"* instead of `$0.00000000/SKR` |
| `Assets/Editor/Regression/NightMarketUiRegression.cs` | new `MinPlateVsSurfaceRatio = 3.0` case (the pair the suite never measured); the no-rotation rule restored with its history |
| `Assets/Editor/Regression/NightMarketNoWalletRegression.cs` | new `[null-rate-envelope]` case; `[anchors]` requires SKR and fails on `$` |
| `Assets/Editor/Regression/ImpulsePackRegression.cs:143-147` | `skrFlat` allowlisted; the message now prints the set instead of hand-listing it |

**⚠ The lead must run `LocalizationBuilder.BuildAll`** — the canonical JSON is the source; the built asset
is what ships, and it has not been rebuilt.

## 4. Two things I got wrong, recorded

1. **The tilt.** I implemented the -8 deg slant as briefed, with clearances derived from the angle.
   `NightMarketUiRegression` refused it on the 21:40 log and the coordinator ruled the **slant** dropped
   rather than the **oracle** re-pointed. That is the right way round, and the rule has now won twice —
   the geometry is back to WO-1800's proven form and every tilt constant is deleted rather than left
   dormant. **What fixed the owner's actual complaint was the plate, not the angle.**
2. **My first contrast measurement of this frame was invalid** and I nearly reported it: a colour search
   over the whole image matched every gold heading, border and price, returning a 360 x 1010 "plate" and
   a meaningless 1.58:1. Caught because the bbox was absurd for a badge. The numbers above come from a
   windowed search around one tag. *Measuring something is not the same as measuring the right thing.*

## 5. ⛔ UNPROVEN

1. **No `COMPILE_GATE_OK`, no `REGRESSION_OK`.** The tree compiles (the capture ran) but neither gate has
   been taken, and the **three new regression cases have never executed**.
2. **The pulse is still not proven.** It exists and is untouched; a still cannot show motion.
3. **`LocalizationBuilder.BuildAll` has not run**, so the nine non-English banners are proven in the
   canonical JSON only — the English one is proven on the pixels.
4. **The `[null-rate-envelope]` case is proven by reading, not by running.** It is the guard for a bug
   that has not fired yet: WO-1818's `rate: null` blanking the shelf the day the list goes all-flat.
5. **The bottom shelf row is still clipped by the scroll viewport** in both frames (pre-existing).

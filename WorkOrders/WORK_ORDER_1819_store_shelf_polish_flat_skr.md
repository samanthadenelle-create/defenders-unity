# WORK ORDER 1819 — Store shelf polish, found in WO-1815's own captures

**Status:** IMPLEMENTED

**Owner, 2026-09-16, verbatim:** *"put big sales signs with x% off!!!! you know some flash"* /
*"change the store to SKR only and set to flat amounts"*.
**Source:** the two PNGs WO-1815 shot (`Builds/ui-capture/Store_SkrFlat_{NoSale,Sale30}_2670x1200.png`)
and its `Builds/store-skr-flat-capture6.log`. Every item below is a **measurement off those artefacts**,
not a taste note.

**Lane:** implementation. Number PRE-ASSIGNED; `CLI_LANES_WO_NUMBERS.md` NOT touched.
**Silo:** WO-1815's files + the 10 policy locale sources and their StreamingAssets mirrors (the WO-1811
lane is finished). **Not mine:** `api/**`, `RemoteTunables.cs`, `ArmyMuster*`, `WallTools/*`, VFX,
`.unity` scenes.

---

## 1. The three defects, each with its measurement

| # | Defect | Measured |
|---|---|---|
| 1 | The walletless banner reads **"Connect a wallet to buy - prices shown in USD"** on an SKR-only shelf | Top-left of BOTH PNGs. Source: `storeWalletlessBrowsingBanner`, all 10 policy locales, `:420` |
| 2 | **"Raise the Barracks" truncates** | `14 of 16 printable glyphs`, font 30 (autosize floor), box 271.9 x 55 px, `wrap=Normal overflow=Truncate` — glyph-oracle, both frames |
| 3 | The `30% OFF` tag **reads as text, not a sign** | Measured off the sale PNG: ink-vs-fill **17.00:1**, but fill-vs-card **1.20:1** — the plate does not separate from the card it sits on |

## 2. Item 1 — the banner says SKR, in every locale

`storeWalletlessBrowsingBanner` is **genuinely translated** in all ten (checked at source: de/fr/es/pt-BR
/ru/ja/ko/zh-Hans/ar each carry real prose, not the English string). **The edit is therefore the currency
token only** — each language keeps its own sentence, `USD` becomes `SKR` — so no translation is invented
and no phrasing regresses. English takes the coordinator's wording: **"Connect a wallet to buy - priced in
SKR"** (ASCII hyphen; the store's copy oracle rejects non-ASCII, and `WalletlessBrowsingBannerProbe`
= `"Connect a wallet"` is preserved, so the FlowTrace probe and `NightMarketNoWalletRegression` still
match).

Files: `Assets/Resources/Data/Canonical/<loc>.json` + `Assets/StreamingAssets/Data/Canonical/<loc>.json`
for all ten, **md5-identical per locale**, byte shapes preserved (these files are mixed-ending:
LF 502 / CRLF 499, no BOM). Plus the code fallback `StoreStrings.WalletlessBrowsingBanner`
(`Assets/_Modules/Wallet/StoreStrings.cs:56-57`) — a second copy of the sentence, so it moves in the same
change or it rots.
⚠ **The lead must run `LocalizationBuilder.BuildAll`** — the canonical JSON is the source, the built
asset is what ships.

## 3. Item 2 — the name fits by the CARD's own rule, never by renaming

⛔ **The truncating card is `Compact`, and that is the whole diagnosis.** `NameBlockPx`
(`StorePackCard.cs:239-241`) gives every variant **two** name lines *except* Compact and
LandscapeStandard, which get one: `BlockPx(44, 1) = 55 px` — which matches the oracle's measured 55 px
box exactly. Wrapping is already on (`:707`) and `FitBlock` already auto-shrinks to the floor (`:712`,
`ElarionUi.FontFloorMobile = 30`). So the card is already doing both things it is asked to do; **at font
30 two lines need 75 px and the box is 55, so TMP wraps and then truncates the second line.** Nothing is
broken in the fit rule — the BOX is a line short.

**Fix:** the Compact name block becomes *"one line at full size, or two at the floor, whichever the words
need"* — `max(BlockPx(NameFont(v), 1), BlockPx(FontFloorMobile, 2))` = `max(55, 75)` = **75 px**.
The Compact card's derived height grows by exactly **20 px** (not the 55 a naive two-line block would
cost), the `CardHeight` formula text is unchanged so the layout suites' source pins hold, and Compact
carries no contents block or value caption for the extra pixels to steal from.
⛔ **The name is NOT renamed** — WO-1801 chose "Raise the Barracks" as the one goal-copy slot a browsing
player reads, and shortening it to fit would undo that ticket to avoid fixing this one.

## 4. Item 3 — the tag becomes a sign: gold plate, dark ink, an 8-degree tilt

**3a. Fill and ink swap polarity, to the palette's OWN gold.** `SaleRibbonFill` becomes
`NightMarketPalette.Patronage` (`#F0C24A`, the palette's existing patronage gold — **mapped, not
invented**; the owner is colourblind and is never asked for a hue) and `SaleRibbonInk` becomes the
near-black `#0A090C` the fill used to be. Predicted, to be **re-measured off the new PNG**:

| Gate | Predicted | Bar |
|---|---|---|
| ink vs fill | **11.5:1** | >= 4.5:1 |
| fill vs card (`GroundRaised #121211`) | **11.1:1** | >= 3:1 |

⚠ **This INVERTS `NightMarketUiRegression` case 7's polarity rule and that re-point is deliberate.** That
rule ("the ribbon must be the state pill's inverse") exists so the two badges never read as one shape
desaturated — but WO-1800's own ranking makes them **mutually exclusive on a card** (state word > sale
ribbon > badge), and after this change they differ by plate luminance, by angle (one is tilted, one is
not) and by position. The rule is **replaced by the two gates the owner's ruling actually names**, which
is strictly more of a measurement than the polarity test it supersedes.

**3b. ⛔ THE TILT IS DROPPED — RULED 2026-09-16 AFTER THE REGRESSION CAUGHT IT.** The paragraph below is
kept as the record of what was tried and why it lost. A -8 deg slant WAS implemented exactly as described
(fixed-width plate, centre pivot, clearances derived from the angle) and `NightMarketUiRegression` went
**`NIGHT_MARKET_UI_FAIL`** on the fresh 21:40 log: *"StorePackCard rotates an element … The brief allows a
bold rounded TAG instead of a slant."* The coordinator ruled the **slant dropped, not the oracle
re-pointed** — the right way round, because "containable at every card width by construction" is a
stronger property than a slant is a flourish. `BuildSaleRibbon`'s geometry is back to WO-1800's proven
form (fractional band, top pivot, -16 px), every tilt constant is **deleted rather than left dormant**,
and the suite's rule is restored with a note that it has now caught the same mistake twice. **What
survives from item 3 is the part that fixes what the owner actually saw: the plate.**

*(Superseded, kept for the record:)* WO-1800 removed a -9 deg tilt
on correct arithmetic: with **fractional** anchors (`RibbonX0 0.02 .. RibbonX1 0.62`) the plate's half
width scales with the card, so on a 500 px card the corner rose ~23 px against a 16 px top offset and
left the card rect. **The fix is to stop the width from scaling:** the plate becomes a FIXED
`RibbonWidthPx`, pivoted at its own centre, and its top offset is DERIVED from the rotation rather than
typed:

```
halfBoxY = (W/2)*sin(8deg) + (H/2)*cos(8deg)     ; centre drop = halfBoxY + margin
halfBoxX = (W/2)*cos(8deg) + (H/2)*sin(8deg)     ; centre inset from the card's left = halfBoxX + margin
```

so the rotated bounding box is inside the card for every card at least `2*(halfBoxX + margin)` wide.
The capture's own containment audit (`geometry=clean`) is the empirical proof and it is already reported
per frame — if the arithmetic is wrong the marker says so.

**3c. The pulse is ALREADY THERE and this ticket adds none.** `_openPulseArmed` `PackStore.cs:440/501`,
fired after a build at `:1814-1821`, ticked from `Update` at `:825` (above the `!_purchaseInFlight`
return), enrolled per card at `:2685`, driven at `:2753` — open wave then, on a sale card only, a looping
beat. Verified by reading, and pinned by `NightMarketUiRegression` case 8's curve assertions.
⛔ **A still cannot prove motion** (the capture never ticks `Update`) — that stays unproven here, as it
was in WO-1800.

## 4b. Regression bounce absorbed (fresh log `Builds/regression.log` 21:40, `REGRESSION_FAIL 556/564`)

- **`[impulse-pack]` x12 — `pricing carries the UNREVIEWED key 'skrFlat'`.** WO-1815's authored field met
  an allowlist that predates it. `AllowedPricingKeys` (`ImpulsePackRegression.cs:143-147`) now admits
  `skrFlat` with a one-line note recording the review: it is a PRICE, it grants nothing, so the
  resources-only covenant that list guards is untouched. ⛔ The failure message no longer hand-lists the
  allowed keys — it prints the set, so the next addition cannot leave the sentence lying.
- **`NIGHT_MARKET_UI_FAIL` — the rotation.** See §4 3b: reverted, not re-pointed.

## 5. Acceptance

- [ ] All 10 locales say SKR, source and mirror **md5-identical**, byte shape unchanged.
- [ ] `"Raise the Barracks"` draws **16 of 16** glyphs in the new capture (measured, not assumed).
- [ ] Ink-vs-fill >= 4.5:1 **and** fill-vs-card >= 3:1, measured in greyscale **off the new PNG**
      (predicted from the authored colours: **11.84:1** and **11.18:1** — prediction, not proof).
- [ ] No rotation anywhere on the card (the suite's own rule, restored).
- [ ] `STORE_SKR_FLAT_CAPTURE_OK 2/2; geometry=clean` on a fresh log; both PNGs opened.
- [ ] `python tools/gate_brace.py` exit 0 + NUL scan clean on every `.cs`.
- [ ] Lead runs `LocalizationBuilder.BuildAll`.

## 6. What NOT to touch

The pack `name` values, `api/**`, the canaries, the Pi / Google Play branches, `pricing.skr`,
`CLI_LANES_WO_NUMBERS.md`, any `.unity` scene. **No Unity until the lead says GO.**

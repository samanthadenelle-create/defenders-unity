# WORK ORDER 1669 — Raid staging: the SPOILS band is SEATED at 40 and DRAWN at 24, under the 30 px floor

**Status:** IMPLEMENTED - awaiting gate + capture
**Silo:** UI / raid staging (`RaidDeployScreen`) + the two oracles that pin it
**Lane:** STAGING-SPOILS (file-disjoint: `RaidDeployScreen.cs`, `CostRowFitRegression.cs`, `RaidDeployLayoutRegression.cs`)
**Opened:** 2026-09-10, from WO-1640's own open item
**Number:** PRE-ASSIGNED by the lead. This lane does **not** edit `CLI_LANES_WO_NUMBERS.md`.
**Branch base:** `dev` @ `e4b5906a541c65fc12255a109b5dc3723e328488`

---

## 1. THE RULING — owner, 2026-09-10 12:16, verbatim

> **"the raid staging screen's SPOILS resource band is seated at FontLabel 40
> (RaidDeployScreen.cs:219) but drawn at 24 px (:890), under the 30 px floor — raise the draw
> to the floor."**

This is a **ruling**, not a proposal. WO-1640 explicitly refused to make this call in-lane and
flagged it for her (see §3). She has now ruled: **the draw comes up to the floor.**

---

## 2. THE TWO LINES, READ AT SOURCE THIS SESSION

**A. The SEAT — `Assets/_Modules/Village/Hero/RaidDeployScreen.cs:219`**

```csharp
new DeployBand("spoils", ColumnRight, SpoilsBandY0 - d, SpoilsBandY1 - d,   ElarionUi.FontLabel),
```

`ElarionUi.FontLabel = 40` (`Assets/_Modules/Core/UI/ElarionUi.cs:114`). The band is
`SpoilsBandY0 = 0.394f, SpoilsBandY1 = 0.492f` (`RaidDeployScreen.cs:177`) — **0.098 of the body**.
On the owner's 2670x1200 surface the screen's own recorded body floor is 411 ref px
(`RaidDeployScreen.MinBodyFracOfPanel = 0.473f`, `:154`), so the band is **40.3 ref px**.

`DeployBand.NeedsPx` (`:198-200`) clamps the demand to the FLOOR, not the authored size:
`RaidSelectionScreen.NeedPx(min(40, 30))` = `NeedPx(30)` = `(30+1) * 1.18 + 2` = **38.58 ref px**
(`Assets/_Modules/Village/Hero/RaidSelectionScreen.cs:248`).

**40.3 >= 38.58 — the BAND is fine and has always been fine.** `RaidDeployLayoutRegression`'s
`[seat]` case (`:317-327`) already measures exactly this and passes. **The band is NOT the defect
and must NOT be re-seated** (see §6).

**B. The DRAW — `Assets/_Modules/Village/Hero/RaidDeployScreen.cs:890-892`**

```csharp
ElarionUiKit.CostRow(plate.transform, DeNelle.Core.UI.CostFormat.Parts(parts),
    new Vector2(0.04f, 0.10f), new Vector2(0.96f, 0.90f),
    ElarionUi.Parchment, prefix: "SPOILS", fontPx: 24f);
```

`24f` is a **bare literal, sitting under `ElarionUiKit.FontFloor = 30f`**
(`Assets/_Modules/Core/UI/ElarionUiKitObsidian.cs:3033`). It is the ONE text site on this screen
below the mobile-legibility floor — WO-1640's RESULT §"floor" (`:279-280`) checked the other three
and recorded that the `FontMicro`/`FontLabel` sites draw at 32 and 40, **at or above** the floor.

**Second, smaller half of B:** the row's y-anchors `0.10..0.90` take only **80% of the band** —
32.2 of the 40.3 ref px. At `fontPx 24` that was ample; at 30 it is not, and the row would bleed
past its own plate (see §5 for exactly what does and does not happen there — it is a
**containment** issue, deliberately not a blank-line one, and the WO says so rather than
inheriting a false premise).

---

## 3. THE FRAME — opened this session

`Builds/ui-capture/RaidDeploy_2670x1200.png` (2670x1200, the owner's surface; opened by this lane
2026-09-10). What it shows, in the right column under `POWER / RECON`:

> `SPOILS 🪵1800 🪨1100 💰2200`

The word **is whole on one line** — WO-1640 Item A landed and the `SPOIL / S` break is gone. What
the frame now shows instead is the residue the ruling names: **the entire SPOILS row is visibly
smaller than every one of its neighbours.** `SCOUT REPORT` under it, `POWER` / `RECON` above it and
`Walls: / Garrison: / Boss:` below it all sit at 32-40; the spoils row alone reads at 24. On a
phone held at arm's length it is the one row that asks the player to lean in — which is precisely
what `FontFloor = 30f` exists to forbid.

*(The pre-WO-1640 frame `Builds/device-frames/2026-09-10_0605_raid_staging.png` — the one showing
`SPOIL` over an orphan `S` — is cited by WO-1640 and by `CostFormat.cs:165` and
`CostRowFitRegression.cs:248-250`. It is not the evidence for THIS ticket; the ui-capture frame
above is.)*

---

## 4. THE LINEAGE — what WO-1640 did, and the open item it handed here

`WorkOrders/WORK_ORDER_1640_raid_staging_spoils_label_breaks_mid_word_and_the_outmatch_confirm_is_invisible.RESULT.md`

**What it fixed (Item A) — `SealPrefixCell`, `Assets/_Modules/Core/UI/CostFormat.cs:161-196+`:**
the cost-row PREFIX is a word and a word never breaks. Two things, both confined to the prefix
cell: `NoWrap`, and the cell widened to TMP's OWN measurement of the string taken as a MAX against
the 8-px-per-character heuristic (`max(28, len*8) * fontPx/13`) — so no caller's prefix can come
back narrower than it is today. It is deliberately **NOT a fitter**: the RESULT records that
`FitSingleLine` at `fontPx 24` clamps `min = max = 24` (already under the floor) and switches
overflow to Ellipsis, which would have read `SPOIL...` — not an improvement.

**The open item it refused to close in-lane (RESULT `:103-111`):**

> the row itself ships under the mobile-legibility floor ... **the FontFloor question is a ruling,
> not a lane call. Flagged for the owner:** either the band table's seat font comes down to what is
> drawn, or the row comes up to the floor.

The owner has ruled the second way. **This WO is that open item and nothing else.**

**The pin that must move with the ruling — `Assets/Editor/Regression/CostRowFitRegression.cs`:**
`[fit-SPOILS-24]` (`CasePrefixIsOneWholeWord`, `:275-319`) plus its RED companion
(`CaseRedWhenPrefixWraps`, `:329-381`). Both build the row through the **production** kit method
(`BuildSpoilsRow`, `:384-396`), whose own doc-comment says it uses *"the 'SPOILS' prefix at the
fontPx RaidDeployScreen passes"* — and it passes it via `private const float SpoilsPrefixFontPx =
24f` (`:263`). **That constant is a COPY of the screen's literal, and the moment the screen moves
to 30 the sentence in its doc-comment becomes false.** The tag string is built from the constant
(`:311`), so the case renames itself to `[fit-SPOILS-30]` automatically once the constant moves.

---

## 5. RED-FIRST — and an honest statement of what each half proves

**The re-point ALONE cannot go red, and this WO will not pretend otherwise.**

* The GREEN prefix case passes at 30 for the same reason it passes at 24: `SealPrefixCell` measures
  the string at whatever size it is handed. Re-pointing `SpoilsPrefixFontPx` makes the fixture
  **honest** (it now really is the size the screen passes) but it is not a new pin.
* The RED companion stays red at 30 for the same reason it is red at 24: it narrows the cell to
  `min(heuristic, measured - 4)` (`:357`) — a condition, deliberately not a pixel value, so a font
  swap cannot turn it into a false failure.

**So the RED-first discriminator is a NEW case, and it is the one this ticket is judged by:**

`RaidDeployLayoutRegression` case **`[spoils-floor]`**, asserting two things over LIVE constants
(never over source text — that is the duplicated-state failure this suite's own banner `:16-23`
exists to end):

1. `RaidDeployScreen.SpoilsChipFontPx >= ElarionUiKit.FontFloor` — fails on HEAD's value, the bare
   literal `24f`. `24 < 30`.
2. the row's own share of the band, `(SpoilsRowAnchorMax.y - SpoilsRowAnchorMin.y) * bandPx`, is at
   least `NeedPx(round(SpoilsChipFontPx))` — fails on HEAD's `0.90 - 0.10 = 0.80` of 40.3 px
   = 32.2 px against a 30 px line's 38.58.

⚠ **Say "under M10", never "red on HEAD".** HEAD carries neither constant, so the case does not go
red against HEAD — it does not **compile** against it. The reproducible red is the mutation:
HEAD's own two values typed back into the new constants.

**Named mutation for the suite's banner:** *M10. Type a sub-floor `fontPx` back into
`BuildSpoilsChips`, or shrink the row's share of the band -> `[spoils-floor]`.*

**WHAT `[spoils-floor]` DOES NOT CLAIM — stated so the next seat does not inherit a false premise.**
The `[seat]` law next door is about **TMP culling the whole line** under `Ellipsis` overflow, which
is what `FitSingleLine` labels get. The cost-row cells are **not** fitted: `AddCostText`
(`CostFormat.cs:133-155`) sets `preferredHeight = max(24, fontPx + 4)` and leaves TMP's DEFAULT
`Overflow` mode live. With `childControlHeight = true, childForceExpandHeight = false`
(`CostFormat.cs:104`) the group clamps each cell to its preferred height and centres it, so at 30
the cell is 34 px centred in the row — **nothing was ever going to render blank here.** Raising the
row's share of the band from 32.2 to 40.3 px is a **bleed-containment** change, not a cull fix. The
row is legible either way; it is simply no longer overflowing its own plate.

---

## 6. THE FIX — what to change, and the shape it must take

### 6.1 `Assets/_Modules/Village/Hero/RaidDeployScreen.cs`

* Add a **named** public constant for the drawn size, pointed at the single authority:
  `public const float SpoilsChipFontPx = ElarionUiKit.FontFloor;`
  ⛔ **Never a literal `30` typed anywhere, and never typed twice.** `ElarionUiKit.FontFloor`
  (`ElarionUiKitObsidian.cs:3033`) is the one place that number lives; a second copy is the exact
  failure CLAUDE.md §2/§5/§16 each describe in their own words.
* Add the row's anchors as named statics (`SpoilsRowAnchorMin` / `SpoilsRowAnchorMax`) so the
  oracle measures the **real** anchors instead of a typed copy, at `y 0.00..1.00` — the row takes
  the whole band. The `Well`'s inner rim (`ElarionUiKit.cs:155-160`) is a 1-2 px edge image; the
  34 px cell is centred inside 40.3 px, so content never reaches it.
* `BuildSpoilsChips` (`:871-893`) passes those three, and only those three, into `CostRow`.

### 6.2 `Assets/Editor/Regression/CostRowFitRegression.cs`

* `SpoilsPrefixFontPx` stops being a copy: point it at `RaidDeployScreen.SpoilsChipFontPx` so the
  fixture's doc-comment ("*at the fontPx RaidDeployScreen passes*") becomes true again and can
  never drift. The `[fit-SPOILS-<n>]` tag re-derives itself.

### 6.3 `Assets/Editor/Regression/RaidDeployLayoutRegression.cs`

* New case `[spoils-floor]` per §5, registered in `RunAll`'s case list, named in the banner's
  mutation list as **M10**, and reflected in the OK reason string. The suite is already wired into
  `DataRegression.RunAll` (`Assets/Editor/Regression/DataRegression.cs:623`), so the new case is a
  LIVE pin the moment it lands — not an orphan.

### 6.4 `Assets/_Modules/Core/UI/CostFormat.cs` — comment only

`:196` reads *"Pinned by CostRowFitRegression [fit-SPOILS-24]"*. That tag string no longer exists
once the constant moves. Re-point the sentence; **change no code in this file.** The
`fontPx / 13f` metric scale in `AddCostText` (`:152`) already carries the size through correctly —
the cell math does **not** need a new parameter, so §6 does not touch it.

---

## 7. ⛔ WHAT NOT TO TOUCH

* **The band table (`:213-221`) and the band constants (`:176-178`).** 40.3 px already clears
  `NeedPx(30) = 38.58`. Re-seating it would move `spoils` toward its column neighbours (`scout`
  ends 0.382, `enemy` starts 0.504 — 4.9 ref px of clearance each side) for **no measured gain**,
  and `[disjoint]` exists to red on exactly that. The seat row stays `ElarionUi.FontLabel`; the
  divergence between a 40 seat and a 30 draw is harmless because `NeedsPx` clamps to the floor
  either way, and `[spoils-floor]` now makes a **sub-floor** draw impossible, which is the half
  that actually mattered.
* **`SealPrefixCell` and everything else WO-1640 Item A landed.** It is size-agnostic by
  construction and is what keeps the word whole at 30.
* **WO-1640's toast work** (Item B, the outmatch confirm). Different band, different ticket, not in
  this lane's diff.
* **WO-1542's two-tap ruling** on this screen. Untouched — no input, no gating, no CTA in scope.
* **The 22 px icon cells in `CostRow`** (`CostFormat.cs:120-125`). They are shared kit geometry
  used by every cost row in the game (build palette included); at a 30 px number they will read
  proportionally smaller than they did at 24. **OWED, stated rather than hidden** — a separate
  ticket if the owner's eye picks it up on the next frame. A player-facing lane does not get to
  smuggle a shared-kit geometry change in behind a font ruling (ARCHITECTURE_PRINCIPLES).
* **`DataRegression.cs`.** Already wired; registration lines are the lead's lane.
* **`CLI_LANES_WO_NUMBERS.md`.** Number pre-assigned.
* **No Unity run, no commit** in this lane.

---

## 8. ACCEPTANCE

1. `Assets/_Modules/Village/Hero/RaidDeployScreen.cs` passes **no numeric font literal** into
   `CostRow` — the size is the named `SpoilsChipFontPx`, itself `ElarionUiKit.FontFloor`.
2. `RaidDeployLayoutRegression` reports `RAID_DEPLOY_LAYOUT_OK` with `[spoils-floor]` in the log,
   and that case fails in both halves under mutation M10 (see §5 — not "red on HEAD", which HEAD
   cannot compile).
3. `CostRowFitRegression` reports `COST_ROW_FIT_OK` with the tag re-derived as `[fit-SPOILS-30]`,
   the prefix on **1 line / 6 of 6 characters**, and its RED companion still able to break the word.
4. **The SPOILS row is whole on ONE line at 30 at all three aspects** —
   `Builds/ui-capture/RaidDeploy_1920x1080.png`, `RaidDeploy_2340x1080.png`,
   `RaidDeploy_2670x1200.png`, **opened**, not merely regenerated. The row must read at the same
   weight as `SCOUT REPORT` beneath it and must not overlap `RECON` above or `SCOUT REPORT` below.
5. **Glyph oracle clean:** `GlyphCoverageRegression` still `GLYPH_COVERAGE_OK`. (The row's
   characters are unchanged — this is a size change, not a copy change — so a red here would mean
   something else moved.)
6. `python tools/gate_brace.py` clean on every `.cs` touched; no NUL bytes; `COMPILE_GATE_OK` +
   `REGRESSION_OK <n>/<n>` on a **fresh** log, judged by the MARKER, never an exit code.

---

## 9. FILES

| File | Change |
|---|---|
| `Assets/_Modules/Village/Hero/RaidDeployScreen.cs` | `SpoilsChipFontPx` + row anchors as named surface; `BuildSpoilsChips` draws at the floor |
| `Assets/Editor/Regression/RaidDeployLayoutRegression.cs` | new `[spoils-floor]` case + M10 + reason string |
| `Assets/Editor/Regression/CostRowFitRegression.cs` | `SpoilsPrefixFontPx` re-pointed at the screen's constant |
| `Assets/_Modules/Core/UI/CostFormat.cs` | one comment line re-pointed (`[fit-SPOILS-24]` tag is gone) |

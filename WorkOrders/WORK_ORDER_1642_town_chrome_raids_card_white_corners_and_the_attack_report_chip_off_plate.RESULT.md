# WO-1642 RESULT — the RAIDS card's packaging is gone, and the attack-report caption now fits the PLATE

**Lane:** TOWN-CHROME (HUD chrome / deck card art)
**Date:** 2026-09-10
**Worktree:** `.claude/worktrees/agent-a7c3150887594ff9e`
**Base:** `a96bfe332` (ff-merged from `refs/heads/dev`; tree clean before the first edit)
**Stage:** IMPLEMENTED — awaiting gate + capture. **Edit only: no Unity run, no gate, no commit.**

---

## 0. Files changed

| file | what |
|---|---|
| `Assets/Resources/UI/ElarionMedieval/cards/raids.png` | RGB (no alpha channel at all) → RGBA with the flattened checkerboard reconstructed as transparency |
| `Assets/_Modules/HUD/PlayerDeckWorkspace.cs` | the `"raids"` `OpaqueMargins` row DELETED (self-retiring, as designed) + why, in place |
| `Assets/_Modules/HUD/Kit/HudKitController.cs` | `RailPlateInk*` consts + `SeatOnRailPlate`, called for the defence chip before `FitBlock` |
| `Assets/_Modules/Village/Harvest/EchoUnlockFeedback.cs` | the stale `0.475` justification corrected against the live mount rects (comment only — the chip does NOT move) |
| `Assets/Editor/Regression/HudLabelFitRegression.cs` | case-9 crop-fraction prose re-measured, plus a correction of the WO's own "identity" premise |

`python tools/gate_brace.py` on all four `.cs`: `GATE_BRACE_SUMMARY bad=0 of 4`, exit 0.
NUL scan: 0 bytes in each. Raw brace counts 202/202, 412/412, 58/58, 39/39.

---

## 1. ⛔ TWO OF THIS TICKET'S OWN CLAIMS ARE WRONG. Correcting them changes what "done" means.

### 1a. There is no 19% stretch, and the re-export does NOT make the fit the identity (WO §1d, §4)

`PlayerDeckWorkspace.MeasureArtFit` returns `Corrected = true` for **any** margin it can see; only
the `alphaSawNoMargin && !opaqueMargin` branch renders 1:1. A transparent margin therefore takes the
**same over-scaled route** the authored row did. Measured this session:

| card | span x | span y |
|---|---|---|
| `quests.png` (alpha margin L47 T62 R47 B74) | 0.947 | 0.847 |
| `raids.png` under the old authored row (L49 T63 R48 B78) | 0.945 | 0.841 |

The two cards were **never at different aspects**. The white corner patches were the ONLY rendered
difference between them, and acceptance #4 ("the stretch is gone") was already true before this
diff. Anyone reading the WO literally would have gone hunting for a stretch that does not exist.

### 1b. It is a RECONSTRUCTION from the flattened PNG, not a re-export

Nobody re-rendered the card from source art. The delivered file was `RGB` mode — **no alpha channel
at all**, 0 transparent pixels of 1,573,538 — so the alpha was rebuilt from the flattened pixels:

1. **Flood the checkerboard in from the image border** (4-connected, seeded only on border pixels
   that are light *and* neutral: `min(RGB) ≥ 225`, `max-min ≤ 12`). Only pixels **connected to the
   border** are touched, so nothing inside the illustration can be eaten however pale it is.
2. **Un-premultiply the soft edge.** The frame's drop shadow was composited *onto* the checker, so
   those pixels are neutral greys between ink and checker white. `alpha = 1 - observed / 246`
   (246 = the measured checker mean: light cells ~254, dark cells ~239) with the colour taken back
   to black recovers the soft edge instead of hard-cutting it — a hard cut is what leaves a pale rim,
   and the first pass of this fix proved it: 61–77 pale pixels survived per 60×60 corner block.
3. Alpha below 64 snaps to 0, so the packaging margin is genuinely transparent for the tight mesh.

The script is `raids_alpha.py`, kept in the session scratchpad (`.../scratchpad/wo1642/`) — it is a
one-shot art repair, not a build step, so it is deliberately not added to `tools/`.

---

## 2. Item A — the evidence

**Acceptance #2, the four corners, re-sampled from the shipped file (RGBA):**

| sample | before | after |
|---|---|---|
| (0, 0) | (253, 253, 252, **255**) | (0, 0, 0, **0**) |
| (1773, 0) | (242, 243, 241, **255**) | (0, 0, 0, **0**) |
| (0, 886) | (244, 244, 244, **255**) | (0, 0, 0, **0**) |
| (1773, 886) | (207, 204, 205, **255**) | (0, 0, 0, **0**) |

**The corner patches, by the WO's own metric** (opaque pixels with `min(RGB) > 200` in a 60×60 block
at each corner *inside* the authored ink bbox, of 3600):

| | TL | TR | BL | BR |
|---|---|---|---|---|
| WO §1c measured before | 1219 | 1223 | 1289 | 1071 |
| after the reconstruction | 0 | 0 | 0 | 0 |

**Whole-file sanity, against the correctly-exported sibling:**

| | raids after | quests |
|---|---|---|
| fully transparent px | 327,015 (20.8%) | 332,782 (21.2%) |
| alpha bbox | (49,62)-(1725,810) | (47,62)-(1726,812) |
| residual opaque near-white px | 102 | 55 |
| altered px inside the safe inner rect (120,130)-(1650,740) | **0** | — |

**What `HudLabelFitRegression` case 7 will now measure** (replica of `MeasureCardPng`'s own rule,
`a > 8` / `mean(RGB) < 170`, run over the file bytes):

```
raids  1774x887  alphaMargin L49 T62 R48 B76   inkMargin L49 T63 R48 B76
```

`transparentMargin` is TRUE → `opaqueMargin` FALSE → **a row for `"raids"` is now a FAILURE**, which
is the self-retiring path the case documents at `:990-995`. The row is deleted; verified the exact
anchor the case matches (`Key = "raids", Width`) is absent from `PlayerDeckWorkspace.cs` while
`Key = "game-guide", Width` is still present, and that `ArtKey = "raids"` / `LockedArtKey =
"raids-locked"` (case 9a) both survive.

**The importer needed no change.** `raids.png.meta` and `quests.png.meta` were compared field by
field this session: `textureType: 8`, `spriteMode: 1`, `spriteMeshType: 1`, `alphaUsage: 1`,
`alphaIsTransparency: 1`, `sRGBTexture: 1` — identical. Same guid, same file name, same 1774x887.

**`game-guide.png` was NOT touched and keeps its row** — measured `1821x864 alphaMargin L0 T0 R0 B0
inkMargin L53 T65 R52 B88`, matching its authored row exactly. Curiosity worth recording: its four
CORNERS already read alpha 0 — (211,210,209,0) / (226,228,224,0) / (211,210,210,0) / (208,207,206,0)
— so it is partly alpha'd yet still has opaque border pixels somewhere on the edge. It is a
different delivery defect from raids' and it is out of scope here.

**Looked at, not just measured:** corner crops at 1:1 over a dark ground (old vs new, all four
corners) and a card-scale side-by-side of QUESTS against the new RAIDS at the rendered 1020×260.
The checkerboard is gone, the ornate frame and its soft shadow are intact, and the two cards read as
one family at the same aspect.

---

## 3. Item B — what the pixels actually said, and the fix that follows from it

### 3a. THE PLATE IS NOT THE BAND. That is the whole defect.

`MedievalUiSkin.ApplyButton` paints the kit plate with `Image.Type.Simple` (`MedievalUiSkin.cs:70`),
so the sprite **stretches across the whole chip band** — and that sprite's own ink occupies only the
upper-middle of its height. Measured off `Assets/Resources/UI/ElarionMedieval/buttons/
button-normal-empty.png` (2172×724): alpha > 32 spans rows **105..533 = 0.145..0.736** of the band;
its dark core spans **0.174..0.709**.

**Confirmed against the shipped frame, not assumed.** `Builds/device-frames/2026-09-10_0602_town.png`
(2670×1200): the Echoes chip is authored at band centre 0.475 with a fixed 112 ref px height, and on
that canvas (1080×1920, match 0.5 → scale 1.243, band 139.2 device px, device rows 560.4..699.6) the
model predicts its plate's dark core at **584.6..659.1**. Row-wise dark-fraction scan across the rail
column (x 2329..2604) measures it at **585..658**. Three plates measured there, all ~74–75 device px:

| chip | plate dark core (device rows) |
|---|---|
| Harvest | 324..398 |
| ATTACK REPORT | 471..545 |
| Echoes 2/6 | 585..658 |

`BuildObsidianButton`'s label call (`ElarionUiKitObsidian.cs:683`) is
`Label(parent, text, 0f, 1f, colour, font, Center, 0.04f, 0.96f, bold: true)`. ⚠ **The 0.04/0.96
pair is x, NOT y** — `ElarionUiKit.Label`'s signature (`ElarionUiKit.cs:1905-1907`) declares
`y0, y1` BEFORE the optional `x0, x1`, and that 0.92 span is exactly where
`DefenseReportLayoutRegression`'s `ChipLabelInset = 0.92f` (`:126`) comes from. So the label rect is
**202.4 x 112 ref px**: it spans the FULL band vertically, **16.2 ref px above the plate's top edge
and 29.6 ref px below its bottom one**. A one-word chip never notices. The caption
`ATTACK REPORT` + newline + `HELD` does: `FitBlock(22, 30)` filled the 112 px band with three lines at ~27 px, and the first and last
painted onto the town wall at either end of the plate. That is the harness's own RULE 1 class
(`UICaptureLaunch.cs:5851-5872`) — **text off its plate, and confirmed NOT a collision with the
Echoes chip: the defence plate ends at device row 545 and the Echo plate starts at 585.**

### 3b. The fix: an authored reference-px band on the plate's ink (WO-1623 / WO-1628 idiom)

`SeatOnRailPlate` collapses the label's y anchors onto the band's TOP edge, sets the pivot **before**
the offsets, and hangs a fixed px band: `112*0.145 + 3 = 19.2` to `112*0.736 - 3 = 79.4`, i.e.
**60.2 ref px**. It runs BEFORE `FitBlock` — TMP auto-sizes against the rect it has, so a label
re-seated afterwards keeps a size chosen for the old, taller box. x is untouched (§4 below).

### 3c. ⭐ THE FIT IS DECIDED BY MEASUREMENT, NOT HOPED FOR — and the font in play is NOT the one the oracle measures

`MedievalUiSkin.ApplyButton` ends on `EnsureFont(label, FontRole.Title)`. `FontRole.Title` resolves
to `font_title` = **Merriweather Bold**; `FontRole.Body` is `font_body` = Alata Regular
(`RpgUiCatalog.cs:79,85`). **`DefenseReportLayoutRegression` measures the caption in `FontRole.Body`**
(`:468`) — the wrong, narrower asset — and `MeasureLineWidthPx` also ignores the skin's
`characterSpacing = 2`. Its budget is therefore optimistic on two axes. So the fit was re-derived
from the SHIPPED asset by summing `font_title.asset`'s own `m_HorizontalAdvance` entries at
`m_PointSize 64`:

| line | Merriweather Bold @22 | + characterSpacing 2 | Alata @22 (what the oracle uses) |
|---|---|---|---|
| `ATTACK REPORT` | **190.7** | 196.4 | 166.2 |
| `HELD` | 63.6 | 65.3 | 48.9 |
| `BREACHED` | 125.2 | 128.7 | 106.2 |
| `OVERRUN` | 111.3 | 114.4 | 100.2 |

The label rect is **202.4 ref px** wide (`RailChipWidthPx` 220 × the kit's 0.92 x-inset — so the
oracle models this rect EXACTLY, not conservatively). **196.4 < 202.4**, so the title seats on ONE
line at the floor under the shipped font.

⚠ **That margin is 6.0 px, ~3%, and it is the one number the capture must confirm.** Kerning can
only shrink the real width further (TMP applies the asset's kerning table; my advance sum does not),
but the failure mode if it is ever wrong is worth writing down: "ATTACK REPORT" wraps, the caption
goes back to three lines, 82.9 px exceeds the 60.2 px band, and `FitBlock`'s `Truncate` **clips
HELD** at the floor rather than overflowing — worse than the defect being fixed. The cheap remedy is
width and the room exists (the plate's ink spans x 0.021..0.978 = 210.5 px); it is deliberately not
taken here, because width is not implicated by the captured defect and widening the label would make
`DefenseReportLayoutRegression`'s budget wrong in the optimistic direction.

Height: `font_title.asset` declares `m_LineHeight 80.448` at `m_PointSize 64` = **1.257 em**. At the
22 px fit floor, **two lines need 55.3 px and fit the 60.2 px band; three need 82.9 px and cannot.**
(The looser TMP `preferredHeight` ratio WO-1628 measured, 1.133 em, agrees: 49.6 vs 74.8.) The
caption therefore resolves to **exactly two lines at or above the floor** — the font is never driven
under it, and `UiKitTextFitGuard`'s `FontHardFloor` relaxation is never reached, since 22 px needs
27.7 px of the 60.2 available for one line.

**No player-facing string was shortened.** `DefenseReportChipModel.Compose` is untouched: the caption
is still `TitleLine + "\n" + OutcomeWord(...)`, and §5's owner ruling on copy is therefore **not
needed** — the fix is geometric. `[chip-gate]`'s composition asserts (`:397-400`), its `\n` assert and
its width budget all still describe the shipped chip.

---

## 4. Deliberately NOT changed, each a stated decision

- **The Echoes chip does not MOVE.** Its comment is corrected; its `0.475` is not. Re-seating a
  placement the owner felt-tested (2026-07-24) is a ruling, not a comment repair.
- **The label's x anchors (0.04..0.96 → a 202.4 px rect).** Width is not implicated — the escape is
  at the plate's top and bottom — and moving the inset would make `DefenseReportLayoutRegression`'s
  width model wrong in the optimistic direction. See the 6.0 px margin note in §3c.
- **`RailChipHeightPx` / the shared 220×112 box.** `HudUiRegression` check 8 pins it at `:1683-1694`,
  and growing the defence band would close the 40-device-px gap to the Echoes plate. The plate-seated
  band makes it unnecessary.
- **The Harvest / Collectors / Builders chips.** They are mis-seated by the same 6% of band height,
  but they are single-line and the WO scopes them out (§8). `SeatOnRailPlate` is written to take any
  rail label when someone rules on that.
- **`ElarionUiKit` / `ElarionUiKitObsidian` / `ElarionUi` / `MedievalUiSkin`.** READ-ONLY per §7.
  Cited throughout, changed nowhere.
- **`hud-areas.json` and `HudAreasHost`'s mount rects.** Read, not moved.
- **`DeckCardPurpose` band, NightMarket, RumorBoard.** Untouched.

---

## 5. What the next capture / device frame must show

⚠ **The two halves need DIFFERENT evidence, and item B cannot be judged by the headless capture.**

1. **Journey deck (headless `UI_CAPTURE_OK` frame or a device frame is fine).** The RAIDS card shows
   **no white anywhere** — compare against
   `Builds/device-frames/2026-09-10_0603_journey_deck.png`, where the patches sit at the four corners
   of the right-hand card. QUESTS and RAIDS side by side at the same aspect and the same frame
   weight. The trace should read `card art fit: 'raids' margin L49 T62 R48 B76 -> anchors …` —
   **NOT** `'raids' is tight - 1:1` (§1a), and no longer
   `opaque packaging margin (authored) L49 T63 R48 B78`.
2. **The locked RAIDS face still renders clean** (acceptance #5) — `raids-locked.png` was not touched
   and its alpha bbox is unchanged at (3,7)-(1409,735) of 1416×742.
3. **The attack-report chip needs a PLAY-MODE / DEVICE town frame WITH AN UNREAD REPORT.** The chip
   is `SetActive(false)` at build (`HudKitController.cs:2057` (was `:2033` before this diff added the plate consts above it)) and only `TickDefenseReportChip` turns
   it on, so an edit-mode capture renders nothing and proves nothing (WO §2d.1). What the frame must
   show: **`ATTACK REPORT` on one line and `HELD` on the next, both entirely inside the dark plate**,
   nothing over the town wall above or in the gutter below. The `[Flow:HudKit]` line
   `WO-1642 'defenseReportChip' label seated on the PLATE: 19.2..79.4 ref px from the band top
   (60.2 px tall), retiring the kit's y anchors 0.00..1.00 which spanned the whole 112 px band`
   proves the seat ran — and its `before` value is itself the runtime confirmation that the kit's
   0.04/0.96 pair was the x axis, not the y one.
4. **Suites to watch:** `HudLabelFitRegression` case 7 `[deck-card-packaging]` (must stay green with
   the row GONE — it is red if the row comes back) and case 9 `[raids-locked-face]`;
   `DefenseReportLayoutRegression` case 4 `[chip-gate]`; `HudUiRegression` checks 6e and 8.

---

## 6. Raised, not fixed — for the lead to mint

1. **`HudAreasHost.cs` carries the SAME stale 0.420 in its own comments** (the QueueStatus block:
   *"below System (.88), above the ActionRail top (.42)"*, *"Still clear of ActionRail (tops
   0.420)"*) while it authors ActionRail at 0.770..0.965 five lines away. §8 forbade touching that
   file here. Same duplicated-state class as the Echo chip's comment.
2. **The Echoes chip encroaches on the QueueStatus mount by ~0.022 of screen height.** Its fixed
   112 px box at centre 0.475 occupies 0.418..0.532; QueueStatus's bottom is 0.510. No pixel overlap
   observed at this frame. The clean cure is a shared band in `DeNelle.Core.UI.HudLayoutBands` — the
   seam WO-1436 and WO-1464 already cut for the raid deploy bar and the move stick — so the Village
   chip READS the band instead of restating it. Structural, and it needs the owner.
3. **`DefenseReportLayoutRegression` measures the chip in the WRONG FONT ROLE** (`FontRole.Body`,
   `:468`) while the shipped label is `FontRole.Title`, and `MeasureLineWidthPx` ignores
   `characterSpacing`. Today it is conservative by luck (166.2 vs 190.7); a caption change could pass
   the oracle and truncate on screen. That is a real coverage hole in a case written *because* of
   WO-1144's unmeasured chip.
4. **WO §7's two named coverage gaps, both still open** (per `LayoutOracle.cs:17-20` a new rule must
   be SEEN RED first, so neither is built here): (i) nothing checks a card sprite for OPAQUE pixels
   inside its ink bbox — `HudLabelFitRegression`'s rectangular test passed 30–36% corner residue for
   weeks; (ii) nothing models the THIRD element of the QueueStatus stack against the Echo chip's
   fractional band, and no oracle would have caught a caption escaping its plate while its RECT was
   correct.
5. **The RAIDS card subtitle read `Army 8 / 10 . train to open a camp` while the raid gate was
   satisfied** (`raid_logcat_stream.txt:7844`, `required=3, ready=True`) — a second reader of the
   raid gate, seen and recorded. WO §8 assigns it to WO-1641's family, not here.
6. **`game-guide.png` is the same delivery defect and still carries its row.** Its corners already
   read alpha 0 but its edge is not transparent, so the row is still correct. The same reconstruction
   would retire it; it is a separate art change and a separate frame to judge.

---

## 7. Hand-back

- **Board:** this file's WO has its `**Status:**` flipped to
  `IMPLEMENTED - awaiting gate + capture (lane TOWN-CHROME 2026-09-10)`.
- **Paths:**
  `WorkOrders/WORK_ORDER_1642_town_chrome_raids_card_white_corners_and_the_attack_report_chip_off_plate.md`
  `WorkOrders/WORK_ORDER_1642_town_chrome_raids_card_white_corners_and_the_attack_report_chip_off_plate.RESULT.md`
- ⚠ Both markdown files were **copied into this worktree** because WO-1642 had not reached `dev`; the
  original untracked copy still sits in the shared checkout's `WorkOrders/`. The committer takes the
  worktree copies (they carry the flip) and drops the shared-checkout duplicate.
- No gate run, no `BOARD.html` regeneration, no commit — the lead owns all three.

### ⚠ `raids.png` IS GIT-LFS TRACKED — the committer needs to know before staging it

`git diff --stat` reports it as `4 +-`, not `Bin 1881118 -> 1785896 bytes`, because the diff is of
the LFS **pointer**, not the image. Checked this session:

```
git check-attr -a Assets/Resources/UI/ElarionMedieval/cards/raids.png
  -> diff: lfs   merge: lfs   filter: lfs   text: unset
git lfs ls-files -n | grep cards/raids
  -> Assets/Resources/UI/ElarionMedieval/cards/raids.png
     Assets/Resources/UI/ElarionMedieval/cards/raids-locked.png
```

`text: unset` means autocrlf cannot mangle the bytes — good. But the commit needs `git lfs` live in
the committer's shell, and the push uploads a **new LFS object**; a push with LFS not configured
leaves a pointer whose object nobody can fetch, and the card would fail to import on the next clone.

### Pins re-read before hand-back (none moved)

| pin | state |
|---|---|
| `HudUiRegression` check 6e (`:938-950`) | asserts `EchoChipBandCentreY` and `ElarionUiKit.MinTouchPx` are PRESENT in `EchoUnlockFeedback.cs`, and that the `ToastCard(... ToastTone.Info)` floater is absent. All three still hold — the edit was comment-only. It never checks 0.475 against a mount, and nothing anywhere asserts the ABSENCE of the retired 0.420 / 0.530 strings, so quoting them as retired is safe. |
| `HudUiRegression` check 8 (`:1683-1694`) | source-lints `RailChipWidthPx = 220f`; parses only the Collectors call. Untouched. |
| `HudLabelFitRegression` `:307-309` | requires the literals `RailChipWidthPx = 220f` and `RailChipHeightPx = ElarionUiKit.MinTouchPx`. Both untouched. |
| `HudLabelFitRegression` case 2 (`:524-525`) and case 15c (`:2312`) | model the **Collectors** and **Builders** chips against `boxH = RailChipHeightPx` (112). Still correct: only the DEFENCE chip's label was re-seated. |
| `DefenseReportLayoutRegression` case 4 | `RailChipWidthPx = 220f` and `ElarionUiKit.FitBlock(_defenseChipLabel, 22f` source-lints both still match verbatim; the caption composition, the `
` assert and the door are untouched. |
| `HudLabelFitRegression` case 9a | `ArtKey = "raids"`, `LockedArtKey = "raids-locked"`, the `!available && !string.IsNullOrEmpty(spec.LockedArtKey)` gate, the `(available || authoredLockFace) ? Color.white` tint and `colors.disabledColor = authoredLockFace` are all still present. The case-9 **tint** rule did NOT change with the re-export — only the crop-fraction prose did. |

---

## 8. FROZEN RECORD — the reconstruction script

Kept verbatim so §6.6 (`game-guide.png`, the same delivery defect) does not have to be rebuilt from
prose. It ran exactly once, against `Assets/Resources/UI/ElarionMedieval/cards/raids.png`.

```python
CHECKER_MEAN = 246.0     # measured: light cells ~254, dark cells ~239
GROW_ROUNDS = 40         # the measured soft edge is ~16 px; 40 is slack, not licence
ALPHA_FLOOR = 64         # below 25% the reconstructed shadow is noise, not art


def flood_from_border(mask):
    """4-connected flood of `mask`, seeded from the image border."""
    h, w = mask.shape
    out = np.zeros_like(mask)
    out[0, :] = mask[0, :]
    out[h - 1, :] = mask[h - 1, :]
    out[:, 0] = mask[:, 0]
    out[:, w - 1] = mask[:, w - 1]
    for _ in range(200):
        before = out.sum()
        for y in range(h):
            row, m = out[y], mask[y]
            if y > 0:
                row |= out[y - 1] & m
            for x in range(1, w):
                if m[x] and row[x - 1]:
                    row[x] = True
        for y in range(h - 1, -1, -1):
            row, m = out[y], mask[y]
            if y < h - 1:
                row |= out[y + 1] & m
            for x in range(w - 2, -1, -1):
                if m[x] and row[x + 1]:
                    row[x] = True
        if out.sum() == before:
            break
    return out


def neighbours(mask):
    n = np.zeros_like(mask)
    n[1:, :] |= mask[:-1, :]
    n[:-1, :] |= mask[1:, :]
    n[:, 1:] |= mask[:, :-1]
    n[:, :-1] |= mask[:, 1:]
    return n


def main():
    src, dst = sys.argv[1], sys.argv[2]
    im = Image.open(src).convert("RGB")
    rgb = np.array(im).astype(int)
    mn = rgb.min(axis=2)
    mx = rgb.max(axis=2)
    spread = mx - mn

    # 1. the checkerboard proper: very light AND neutral, reached from the border.
    packaging = flood_from_border((mn >= 225) & (spread <= 12))

    # 2. the soft edge: neutral grey, still light enough to be shadow-over-checker
    #    rather than ink, and CONNECTED to the packaging. Bounded rounds.
    soft = (spread <= 20) & (mn >= 24) & (mn < 225)
    for _ in range(GROW_ROUNDS):
        grow = neighbours(packaging) & soft & (~packaging)
        if not grow.any():
            break
        packaging |= grow

    alpha = np.full(mn.shape, 255, dtype=np.uint8)
    a = np.clip(1.0 - mn[packaging] / CHECKER_MEAN, 0.0, 1.0)
    alpha[packaging] = np.round(a * 255.0).astype(np.uint8)
    alpha[packaging & (alpha < ALPHA_FLOOR)] = 0

    out = np.array(im).astype(np.uint8)
    out[packaging] = 0                      # shadow colour, un-premultiplied
    out = np.dstack([out, alpha])
    Image.fromarray(out, "RGBA").save(dst, optimize=True)
```

⚠ **Do not run it on a card without re-measuring that card's own checker mean and re-opening the
result.** The three thresholds are calibrated to this delivery, and the corner-block count plus a
visual crop at 1:1 are what proved them — not the fact that the script exited.

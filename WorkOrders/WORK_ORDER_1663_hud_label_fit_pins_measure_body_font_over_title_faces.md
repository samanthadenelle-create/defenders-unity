# WORK ORDER 1663 — `HudLabelFitRegression` measures Alata Regular over faces the game draws in Merriweather Bold

**Status:** READY TO IMPLEMENT
**Silo:** HUD / oracles (`Assets/Editor/Regression/HudLabelFitRegression.cs` only)
**Origin:** WO-1662 §8 — the systemic half of the root cause that WO-1662 fixed for one label
**Number:** PRE-ASSIGNED by the lead. **The `CLI_LANES_WO_NUMBERS.md` banner was NOT edited by this lane.**
**Date:** 2026-09-10

---

## 1. THE DEFECT, IN ONE SENTENCE

Every obsidian button label in the game is drawn in **`FontRole.Title` (Merriweather Bold), bold-styled,
at `characterSpacing = 2`** — and this suite measures those same labels in **`FontRole.Body` (Alata
Regular), regular weight, no spacing**. The suite is therefore **structurally optimistic about width
on every button face it audits**, and it has already reported GREEN over two shipped cuts.

**Proven at source, not inferred:**

| Fact | Where, opened 2026-09-10 |
|---|---|
| `BuildObsidianButton` skins every branch it can return from | `ElarionUiKitObsidian.cs:648` (prefab), `:660` (fallback), `:688` (code-built) — all three call `MedievalUiSkin.ApplyButton` |
| The skin sets weight, spacing and ROLE | `MedievalUiSkin.cs:88-91`: `label.fontStyle \|= FontStyles.Bold;` `label.characterSpacing = 2f;` `ElarionUiKit.EnsureFont(label, ElarionUiKit.FontRole.Title);` |
| The measurer sums regular-weight advances only | `ElarionUiKitObsidian.cs:2905-2938` — no weight term, no spacing term |

**The cost so far — two shipped cuts this suite was written to prevent:**
- `"Tap to collec"` on the Collectors chip, captured across all 8 runs of the 2026-08-22 fleet
  (recorded at `HudKitController.cs:2288-2291`). Case 2 exists *because* of it and measures Body.
- `"THE NIGHT MARKET"` at 12 of 14 glyphs on the HUD store card, at all three aspects
  (`Builds/wave7-capture1`). Cases 11c and 12e existed, measured Body, and were green — fixed by
  WO-1662 for that one label.

---

## 2. THE SITES — every `FontRole.Body` measurement, by line, with the face it covers

⚠ **THE COUNT IS 9, NOT 12.** WO-1662's §8 said 12; that was the count **before** its own re-point,
which removed three (11c had two, 12e one). Read fresh at `a89603a7a` + the WO-1662 working tree:
`grep -n "MeasureLineWidthPx" Assets/Editor/Regression/HudLabelFitRegression.cs`. **Re-run that grep
before starting — these line numbers move.**

| # | Line | Case / helper | The face it measures | Producer, read at source | Verdict |
|---|---|---|---|---|---|
| 1 | `:540` | Case 2 `[collector-chip]` — the ACTION lines | Collectors rail chip label | `BuildRailChip` → `BuildObsidianButton`, then an explicit `MedievalUiSkin.ApplyButton(btn, primary: true)` at `HudKitController.cs:2237` | ⛔ **WRONG ROLE — AND REDS, see §4** |
| 2 | `:566` | Case 2 — the chip TITLE (`"Harvest"`) | same chip | same | ⛔ **WRONG ROLE** (fits either way today) |
| 3 | `:603` | Case 3 `[manage-face]` | a legacy ActionBar face | ⚠ **AND A SECOND QUESTION** — see §5 | ⛔ **WRONG ROLE + possibly a retired surface** |
| 4 | `:695` | Case 4 `[wave-band]` — the countdown | `_waveCountdown` | `ElarionUiKit.Label(...)`, `HudKitController.cs:1782` — a plain label, never skinned | ✅ **Body is CORRECT — do not touch** |
| 5 | `:1187` | helper `WrappedLineCount` | **whatever its caller measures** | called by Case 2 (obsidian) among others | ⛔ **ROLE MUST BECOME A PARAMETER** |
| 6 | `:1200` | helper `LongestOf` | **whatever its caller measures** | used by Case 7 `[deck-card-packaging]`, whose cards are `BuildObsidianButton` + a second `MedievalUiSkin.ApplyButton` (`PlayerDeckWorkspace.cs:176-183`) | ⛔ **ROLE MUST BECOME A PARAMETER** |
| 7 | `:1778` | Case 10 `[heartfire-inside-plate]` | the heartfire marks row | `ElarionUiKit.Label(...)`, `HudKitController.cs:2567` | ✅ **Body is CORRECT — do not touch** |
| 8 | `:2376` | Case 13 `[heart-objective-state]` | the Heart plate's objective line | `ElarionUiKit.Label(...)`, `HudKitController.cs:2528` | ✅ **Body is CORRECT — do not touch** |
| 9 | `:2524` | Case 15 `[builders-chip-idle]` | Builders rail chip label | `BuildRailChip` → obsidian; the site's own comment already names `BuildRailChip: FitSingleLine(lbl, 22f, 30f)` | ⛔ **WRONG ROLE — AND REDS, see §4** |

**So: four wrong sites, two shared helpers that must stop hardcoding a role, and three that are
already right.** ⛔ **This is not a find-and-replace.** Re-pointing #4, #7 or #8 to Title would make
those three cases falsely pessimistic and could red working screens.

---

## 3. THE OFFLINE MEASUREMENT METHOD (reproduce it before changing anything)

No Unity needed. The TMP font assets are committed YAML and carry the same numbers
`MeasureLineWidthPx` reads:

1. Parse `m_FaceInfo` → `m_PointSize` (64) and `m_Scale` (1) from
   `Assets/Resources/RpgUi/font/font_body.asset` (Alata Regular) and `font_title.asset`
   (Merriweather Bold).
2. Build `unicode -> glyphIndex` from `m_CharacterTable` (`m_Unicode` / `m_GlyphIndex` pairs) and
   `glyphIndex -> m_HorizontalAdvance` from `m_GlyphTable`.
3. `widthPx = sum(advances) * fontSizePx / pointSize * scale` — byte-for-byte what
   `ElarionUiKitObsidian.cs:2916-2933` does, including charging a missing glyph at the widest
   resolved advance.
4. **Measure the UPPER-CASE form for any skinned face** — `MedievalUiSkin.cs:86` upper-cases
   (the WO-1466 correction), so the drawn glyphs are not the authored ones.

**What that method produced for the two rail-chip sites** (label box `RailChipWidthPx *
ButtonLabelInset` = `220 * 0.92` = **202.4 ref px**), computed this session:

| string | at | Body (measured today) | Title | Title x 1.15 |
|---|---|---|---|---|
| `"Collect"` | 30 | 92.9 fits | 140.6 | 161.7 fits |
| `"99% - tap"` | 30 | 145.6 fits | 166.9 | 192.0 fits |
| **`"99 waiting"`** | 30 | **147.5 fits** | 190.8 | **219.4 — OVER by 17.0** |
| `"Harvest"` | 30 | 103.1 fits | 147.4 | 169.6 fits |
| **`"Builders idle 2"`** | 22 | **142.9 fits** | 193.2 | **222.2 — OVER by 19.8** |
| `"Train 1"` | 22 | 65.6 fits | 89.0 | 102.3 fits |

Title runs **~+30-35%** over Body on these strings before any weight or spacing term.

---

## 4. FIX SHAPE

**A. One shared skinned-face measurer, applied PER SITE — never globally.**
WO-1662 already added the three pieces to this same file; lift them to serve every case rather than
copying them:
- `SkinnedFaceRole` = `ElarionUiKit.FontRole.Title` — the role `MedievalUiSkin.ApplyButton:91` installs.
- `SkinnedFaceWidthSlack` = `1.15f` — **one named allowance** for the bold weight + `characterSpacing 2`
  together. ⛔ **Do not model TMP's internals**: how it scales `boldSpacing` / `characterSpacing` is
  not asserted anywhere and must not start being asserted here (§11B).
- `TryWrapLines` — greedy wrap through the kit at the slack, returning a **stated skip** when the font
  is not measurable headlessly.

Give the two helpers (#5, #6) a **role parameter** so an obsidian caller and a plain caller can share
them, and change **only** sites #1, #2, #3, #6 (via its Case 7 caller) and #9.

**B. Where a re-point turns a site red, the FIX IS THE LAYOUT OR THE WORDS — never the oracle.**
The rail chips are 220 px wide because three chips share one gutter, and `RailChipWidthPx = 220f` is
pinned by `HudUiRegression` check 8d. `ElarionUiKit.FontFloor` is a FLOOR. So the remedies, in the
order the repo has already ruled on them: **fewer characters in canon-strings** (the WO-1144 ruling
for exactly this chip — *"the fix for a tight label is FEWER CHARACTERS, never a smaller box"*), or a
two-line block where the band has the height (the WO-1662 remedy). ⛔ **Never lower a font floor,
never widen a rail chip, never weaken the slack to make a site pass.**

---

## 5. A SECOND FINDING AT SITE #3 — RECORD IT, GET A RULING, DO NOT SILENTLY FIX IT

Case 3 sizes its box from `HudActionBarModel.MaxVisibleFaces`. **CLAUDE.md §7 states that the bar the
player touches is the ADAPTIVE PEACEFUL DOCK, and that `HudKitController.BindActionBar` returns early
whenever the dock exists — so `MaxVisibleFaces` does not drive what ships**, and no reasoning about
the shipped bar may start from that constant. If that is still true at implementation time (**read it
at source, do not take it from here**), then site #3 is measuring **a retired surface as well as the
wrong font**, and re-pointing its role alone would make a green oracle over a dead geometry into a
red one over a dead geometry. Neither is coverage. **Surface it to the owner/lead with the source
lines and let it be ruled** — the live authority for the dock is
`HudActionBarRegression.CheckMeasuredPeacefulDock`, which builds the real dock.

---

## 6. RED-FIRST — NAMED IN ADVANCE, WITH NUMBERS

⛔ **A re-point that goes straight to green proves nothing.** From §3, on the current tree, these two
sites MUST red the moment they measure the drawn face:

1. **Site #9, Case 15 `[builders-chip-idle]`** — `"Builders idle 2"` at the chip's 22 px floor:
   **222.2 vs the 202.4 px label box, over by 19.8.**
2. **Site #1, Case 2 `[collector-chip]`** — the `hudCollectorsWaitingLine` action line rendered as
   `"99 waiting"` at the 30 px floor: **219.4 vs 202.4, over by 17.0.**

Both are the **same chip family** whose real, captured, shipped cut (`"Tap to collec"`) is the reason
Case 2 exists. If neither reds, the re-point did not take — check the role actually changed and the
slack is being applied.

Sites #2, #3 and #6 may or may not red; that is fine and expected. **Record what each one does**, red
or green, with its numbers — a site that stays green under the correct measurement is a proven-fitting
face, which is the thing this suite was supposed to be telling us all along.

---

## 7. WHAT NOT TO TOUCH

- ⛔ **Cases 11c and 12e** — WO-1662 already re-pointed them onto the shared measurer. Do not re-do,
  re-copy or re-word them; extend what they introduced.
- ⛔ **Sites #4, #7, #8** (`:695`, `:1778`, `:2376`) — plain `ElarionUiKit.Label` faces, Body is the
  correct role, and moving them would make three working screens red.
- ⛔ **`MedievalUiSkin`, `ElarionUiKit.BuildObsidianButton`, `MeasureLineWidthPx`.** The measurer does
  exactly what it documents; the bug is the CALLER passing the wrong role. Changing the skin re-skins
  every button in the game.
- ⛔ **The font assets** under `Assets/Resources/RpgUi/font/`, and every font floor
  (`FontFloor` 30, `FontHardFloor` 20, `ElarionUi.FontFloorMobile` 30).
- ⛔ **The `GlyphBaseline` array** (`UICaptureLaunch.cs:6174-6179`). Nothing found by this ticket may
  be baselined away — the array is shrink-only.
- ⛔ **`RailChipWidthPx = 220f`** (pinned by `HudUiRegression` 8d) and the canon `storeWordmark`.

---

## 8. ACCEPTANCE

- [ ] Every one of the 9 sites is **classified in the WO's own table as obsidian or plain**, re-read
      at source at implementation time (the line numbers above are dated, not authoritative).
- [ ] The two named sites in §6 were seen **RED** before the layout/copy fix, and green after.
- [ ] The three plain-label sites are **unchanged**, and said to be unchanged.
- [ ] No new copy of the role or the slack: one `SkinnedFaceRole`, one `SkinnedFaceWidthSlack`, one
      measurer, a role PARAMETER on the two shared helpers.
- [ ] `COMPILE_GATE_OK` + `REGRESSION_OK <n>/<n>` on fresh logs (markers, never exit codes).
- [ ] A fresh `RunCaptureHeadless`: `UI_GLYPH_OK` with **0 new**, and no `TEXT TRUNCATED` line naming
      any rail chip.
- [ ] Site #3's second finding (§5) is **written up and routed**, not silently fixed.

---

## 9. FOLLOW-ON, DELIBERATELY OUT OF SCOPE

`RumorBoardPanel.cs:126-128` carries `PageButtonBoldSlack = 1.10f` — the same allowance, on Body
advances, for the same reason, in a **runtime** file. Once this ticket settles the role + slack in the
oracle, the honest end state is one kit-side skinned-face measurer that both the oracle and
`RumorBoardPanel` call, rather than a third copy of the constant. **Not this ticket** — but do not add
a copy here that makes it harder.

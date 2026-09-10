# WORK ORDER 1666 — RESULT

**Status:** IMPLEMENTED — awaiting gate (§6 RumorBoard half deliberately parked, per §11)
**Lane:** LABEL-PINS
**Date:** 2026-09-10
**Tree:** worktree fast-forwarded to `8e170b555417545ea4d871b08954e09179cc35d0`;
`b75dcb24d` (WO-1663) confirmed an ancestor, `MeasureFacePx` present.
**Files changed (2):**
- `Assets/Editor/Regression/HudLabelFitRegression.cs` (+188 / −198)
- `Assets/Editor/Regression/InventoryArmoryRailRegression.cs` (+20, comment only)

**Deliberately NOT changed:** `RumorBoardPanel.cs` (runtime width = felt decision, §6B),
`SessionShapeRegression.cs` (out of silo — but see §7, a real finding), `HudKitController.cs`,
`MedievalUiSkin.cs`, `ElarionUiKitObsidian.cs`, all 13 canonical JSON files, every font floor,
`RailChipWidthPx`, `GlyphBaseline`, the WO-1662/1663 helpers.
**No Unity, no commit.** `gate_brace` `bad=0 of 2`, NUL 0, braces 207/207 and 130/130.

---

## 1. §3 CASE 3 `[manage-face]` — **DELETED**, and the reason is that the re-point already exists

**The check the WO told me to make first settled it.** `HudActionBarRegression.CheckMeasuredPeacefulDock`
(`HudActionBarRegression.cs:290`) does **not** merely count faces — it **already measures caption
fit**. Read at source 2026-09-10: it builds the real dock via `kit.BuildPeacefulDockProbe`, reads
each caption out of the built tree, solves the slot width with `HudDockLayout.Solve`, then at
`:395-415` measures every caption against `sol.SlotWidthPx * CaptionInset` and fails with
*"it can only render elided"*.

So WO-1666 §3's option (a) collapses into option (b), exactly as the WO anticipated. Adding a
second caption-fit measurement in `HudLabelFitRegression` would have been two oracles over one
face — the duplicated state that produced this whole ticket family.

**What was done:** `Case3_ManageFace` and its registration line deleted; an 43-line tombstone put
in its place carrying the **verbatim** no-caller proof:

```
if (_peacefulDockRoot != null)
{
    for (int i = 0; i < _barButtons.Length; i++)
        if (_barButtons[i] != null) _barButtons[i].SetActive(false);
    FlowTrace.Step("HudKit", "adaptive peaceful dock owns the actionBar; legacy repacker retired");
    return;
}
```
— `HudKitController.BindActionBar` (`:3473`), lines **`:3480-3486`**. Plus a `⛔ Do NOT re-add a
Manage-face fit pin here` and a pointer at the live oracle.

**Residual handled, not left dangling:** `ActionBarZoneFrac` and `BarGap` stay — they back Case 1's
`[boxes-pinned]` mount-width and source-literal checks, which guard the mount the **live dock** is
solved across (`HudAreasHost.ActionBarMinX..MaxX` is what `CheckMeasuredPeacefulDock` uses for
`mountFrac`). Its justification string said *"Case 3 divides the action bar into per-face slots
across it"*; that reason was rewritten to name the dock oracle, with the old wording quoted so the
change is legible.

**⚠ ONE FINDING HANDED ON (not fixed, out of scope and unverified):** `CheckMeasuredPeacefulDock`
measures its captions at **`FontRole.Body`** (`HudActionBarRegression.cs:397-399`). Whether a dock
medallion caption is a skinned obsidian face — and therefore owed `MeasureFacePx` — was **not**
established. If it is skinned, that oracle carries the exact WO-1663 blind spot this file just
closed, on the *live* bar. Written into the tombstone so it cannot be lost.

---

## 2. §4 CASE 2 `[collector-chip]` — branches (a) and (b) **DELETED**, title measurement kept

**No-caller proof quoted in-code.** `FormatCollectorChip` (`HudKitController.cs:2239-2246`) is, in
its entirety, `return HudStrings.Get(HudStrings.KeyCollectorsTitle);`, and the chip's only two
runtime text assignments are `:5373` and `:2749`. Grepped across `Assets/` — every other reference
to the four keys is `HudStrings`' own key table, this suite, `CollectorTellRegression`,
`SmartArgumentRegression`, or an EditMode test. **No drawing caller for any of the four.**

| removed | what it measured |
|---|---|
| branch (a) | `KeyCollectorsFullLine` / `NearlyLine` / `WaitingLine` — three action lines, undrawn |
| branch (b) | `KeyCollectorsCount` wrapped for a height budget — undrawn (this is where WO-1663's "thin 108.0 of 112 px margin" lived; it was a margin over nothing) |

**Kept:** the title measurement — the chip's **one drawn word**. Its failure text was rewritten to
say so and to carry the WO-1144 remedy order. It also gained a `w < 0f` **stated skip**, which the
original lacked: `MeasureLineWidthPx` returns `-1` for "no font resolvable", and `-1 > boxW` is
false, so an unmeasurable label used to pass silently as a fit.

**The JSON keys were NOT removed**, per the coordinator's instruction and recorded as a code
comment: `CollectorTellRegression.cs:231-235` reads all four for the WO-857 "never say Storage"
copy law, and `SmartArgumentRegression` / `LocalizationSmartStringPackageTests` read
`KeyCollectorsCount`. Removing the 13 canonical rows would red three other suites for no gain.

---

## 3. §4 follow-through — `WrappedLineCount` and `LongestOf` deleted as dead code

Deleting branches (a)/(b) removed the **only** callers of both helpers (verified by grep: after the
edit, zero call sites). WO-1663 had just given them a role parameter *for those callers*. Two
unreferenced private methods inside the file whose entire purpose is "stop reporting on things that
are not there" would have been self-parody, so they went, with a tombstone that redirects future
wrapping to `TryWrapLines` — which is strictly better anyway: it charges the skinned slack **and**
returns a stated skip when the font is unmeasurable, where the two deleted ones silently treated an
unmeasurable line as a fit.

⚠ This is one step past WO-1666 §4's literal words ("delete the *calls*"). Called out here rather
than done quietly; it is a pure-deletion change and trivially reversible.

---

## 4. §5 CASE 15 — dormancy is now **machine-checked** (new sub-case 15d)

**Not a delete** — `HudKitController.cs:811` retires the chip deliberately and `:1930-1936` says it
is *"two lines from returning"*.

Added **15d**: a state machine that finds every occurrence of `BuildQueueStatusChip(pool);`, walks
back to each line's start (so re-indentation cannot fool it) and asks whether that line comments it
out. Live call → **FAIL**, naming what it means: 15c stops being pre-emptive and becomes live
coverage, so its box must be re-read. Dormant → a note that says **`chip DORMANT … 15c below is a
PRE-EMPTIVE fit pin, NOT live coverage of a shipped screen`**, so no reader of a green run can
mistake it. 15c's own note now ends `(DORMANT SURFACE, pre-emptive pin - see 15d)`.

---

## 5. RED-FIRST — 15d is the only live re-point, and it was **mutation-tested**

Deletes cannot red (§8 of the WO says so), so 15d is the one assert that had to be *seen* work. Its
exact state machine was ported and replayed against the real `HudKitController.cs` and three
mutations:

| input | 15d | Case7_OneDoor |
|---|---|---|
| HEAD, unmodified | `NOTE(dormant - pre-emptive pin)` | GREEN |
| retirement line at `:811` replaced with a **live** call | **FAIL** | **GREEN** |
| a **live** call added elsewhere, comment left intact | **FAIL** | **GREEN** |
| every mention of the token deleted | `NOTE(chip removed outright)` | **RED** |

15d fails on both un-retirement shapes and is silent on HEAD. Proven, not asserted.

---

## 6. §6 THE TWO DUPLICATES

### A. `InventoryArmoryRailRegression.cs:775` — **classified PLAIN; Body is CORRECT; left alone**
Traced before touching anything. Its only caller (`:527`) wraps the `invEmpty*` /
`invPaneGearGaps` sentences. Those are drawn by `HeroInventoryController.AddLabel` (`:813-831`),
which news up a bare `TextMeshProUGUI`, sets `characterSpacing = spacing` (**0** at the
`InventorySidebar.cs:122` call site), takes **`bold: false`** by default, and never touches
`MedievalUiSkin` or `ElarionUiKit.EnsureFont`. A plain Body/regular label.

**So it must NOT be re-pointed** — Title + slack would over-report these sentences by ~30-35% and
red a working screen, the precise hazard WO-1663 §7 kept three sites away from. A 20-line comment
records the classification and its evidence at the site, so the next seat does not re-investigate
or "unify" it by reflex. **The duplication that remains is the wrap ALGORITHM, not the role** —
that is WO-1663 §9's kit-side measurer, correctly out of scope.

### B. `RumorBoardPanel.cs:126-128` — **STOPPED AT THE REPORT, as instructed. Proposal below.**
`public const float PageButtonBoldSlack = 1.10f`, used at `:568` inside `PageButtonWidthPx`, which
measures at `FontRole.Body` (`:564-565`). Its own doc comment concedes the defect: *"MeasureLineWidthPx
sums regular-weight advances; the button is bold."*

**The proposal, for the owner:** unifying it with `SkinnedFaceWidthSlack` (1.15) means every rumor-board
page button gets **~4.5% wider** (`host = measured * slack / 0.92`), clamped below by `MinTouchPx`, so
short labels do not move at all and only labels already near the touch floor grow. `RumorBoardPanel.cs:560`
states `PageButtonWidthPx` is public *"so RumorBoardLayoutRegression pins the same number"*, so that
pin would red first and then be updated. **This is a visible layout change on a shipped screen and
was not made.** ⚠ Also worth the owner knowing: 1.10 vs 1.15 is not a real disagreement — neither is
derived from TMP internals; both are named allowances, and WO-1662 chose 1.15 from a measured cut.

---

## 7. ⚠ A REAL FINDING THE MUTATION TEST EXPOSED — `Case7_OneDoor`'s pin is satisfied by its own prose

`SessionShapeRegression Case7_OneDoor` (`:314-318`) asserts
`hud.IndexOf("// BuildQueueStatusChip(pool);") >= 0`. That substring occurs **twice** in
`HudKitController.cs`:

- `:811` — the real retirement line;
- `:1932` — a **prose mention of it**, inside `BuildQueueStatusChip`'s own doc comment:
  ``// `// BuildQueueStatusChip(pool);` in Build(), so nothing here ever runs, and``

**The comment that documents the pin satisfies the pin.** Delete the real retirement at `:811` and
replace it with a live call, and Case7 stays **GREEN** (row 2 of §5's table). I had written into
15d that Case7 "ALREADY fails if the commented-out line disappears" — the mutation run **disproved
my own sentence**, and it is corrected in-code rather than left standing.

⛔ **Not fixed here.** `SessionShapeRegression` is outside this ticket's silo, and tightening
another suite's pin without a ticket is how two oracles start drifting. **Recommend a follow-on:**
anchor Case7 on the retirement line's full text (`// BuildQueueStatusChip(pool);   // retired
2026-08-07 (owner)`) or on line-position, not a bare substring. Until then, **15d is the only
coverage of an actual un-retirement**, which is a good argument for keeping it exactly as written.

---

## 8. ACCEPTANCE, LINE BY LINE

- [x] Case 3 **deleted** with `BindActionBar:3480-3486` quoted verbatim; the rejected option (a) is
      explained (the re-point already exists in `CheckMeasuredPeacefulDock`).
- [x] Case 2 (a)+(b) gone with `FormatCollectorChip:2239-2246` quoted; the **title** measurement
      survives (and got a stated skip it lacked); `CollectorTellRegression` untouched **and
      consistent**, because the keys were kept; the keeping is recorded in-code with its reason.
- [x] Case 15 keeps 15c and gains **machine-checked** 15d; its note names the surface dormant in the
      gate log; no duplicate of Case7's assertion — proven disjoint by mutation, §5.
- [x] `InventoryArmoryRailRegression` classified **plain** *before* any re-point and left with the
      reason recorded at the site. `RumorBoardPanel` reported, not edited, per instruction.
- [x] RED-first where a live re-point exists: 15d mutation-tested, §5. Deletes proven by quoted
      no-caller evidence instead.
- [x] `python tools/gate_brace.py` on both `.cs` → `GATE_BRACE_SUMMARY bad=0 of 2`, exit 0; NUL 0.
- [ ] `COMPILE_GATE_OK` + `REGRESSION_OK <n>/<n> suites` on fresh logs — **the lead's step.**
      **`<n>/<n>` MUST BE UNCHANGED.** Cases were removed, not suites: `HudLabelFitRegression` is
      still registered exactly once at `DataRegression.cs:649` and still emits one
      `[hud-label-fit]` tag line. The denominator is derived from registration call-sites
      (`RegressionMarkerRegression.TryGetExpectedSuiteCount`, `DataRegression.cs:2044-2050`), and
      that call-site was not touched. **If the count moves, something else moved it.**
- [ ] `RunCaptureHeadless` / `UI_GLYPH_OK` — the lead's step.
- [x] Status flipped by this lane; this RESULT written; both paths reported.

---

## 9. WHAT MOVED, AND WHAT A REVIEWER SHOULD LOOK AT FIRST

1. §7 — `Case7_OneDoor` is weaker than the repo believed. That is the highest-value line in this
   RESULT and it needs a ticket.
2. §1's handed-on question — does the **live** dock oracle measure the right font? If the dock
   caption is skinned, WO-1663's defect is still live on the bar the player touches.
3. §6B — the only item awaiting a person: a 4.5% button-width change on the rumor board.

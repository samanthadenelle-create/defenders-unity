# WORK ORDER 1666 — `HudLabelFitRegression` pins measure surfaces the game no longer draws

**Status:** IMPLEMENTED — awaiting gate (§6B parked on an owner call, per §11)
> §3 Case 3 DELETED (the re-point already exists: `CheckMeasuredPeacefulDock` measures caption fit).
> §4 Case 2 (a)+(b) DELETED with the no-caller proof quoted; the four canon keys deliberately KEPT.
> §5 Case 15 gained machine-checked 15d, mutation-tested RED. §6A classified PLAIN — Body is correct,
> left alone with the evidence recorded. §6B (RumorBoard runtime width) STOPPED AT THE REPORT: a
> visible layout change is the owner's call. ⚠ NEW FINDING: `SessionShapeRegression Case7_OneDoor`'s
> pin is satisfied by its own prose comment — needs a follow-on ticket. See
> `WORK_ORDER_1666_hud_label_fit_pins_over_retired_surfaces.RESULT.md`.
**Silo:** HUD / oracles — `Assets/Editor/Regression/HudLabelFitRegression.cs` primarily; one
runtime file (`RumorBoardPanel.cs`) and one sibling suite (`InventoryArmoryRailRegression.cs`)
in §6 only.
**Origin:** WO-1663 §4 — the re-point landed; these are the findings it surfaced and was told not
to fix on its own judgement. **Lead ruling 2026-09-10: these are TECHNICAL, not the owner's.**
> *"a pin over a retired surface is not coverage."*
**Number:** PRE-ASSIGNED by the lead. **The `CLI_LANES_WO_NUMBERS.md` banner was NOT edited by this lane.**
**Date:** 2026-09-10
**Depends on:** WO-1663's `MeasureFacePx` (in the main tree; chain 37 running at mint time). Every
re-point in this ticket uses **that** function — never a new measurer, never a new slack.

---

## 1. THE DEFECT, IN ONE SENTENCE

Four pins in this suite (and two duplicated measurers outside it) assert a label fits **a surface
the game does not build, or a string no code assigns** — so they are green over nothing, and a
green over nothing is indistinguishable in the log from a green over a working screen.

WO-1663 fixed the *font* those pins measure. It could not fix *what* they measure, because two of
the four are a delete-or-re-point decision and one is a copy retirement. This ticket closes all of
them, and the standing rule for every one is the same:

> ⛔ **NEVER LEAVE A PIN ASSERTING A RENDER THAT DOES NOT HAPPEN.** Either it measures the live
> surface, or it is deleted with the no-caller proof QUOTED in the commit and in the RESULT.
> A third option — "leave it, it's harmless" — is what produced this ticket.

---

## 2. WHY A GREEN-OVER-NOTHING IS A REAL DEFECT, NOT TIDINESS

This suite's whole purpose is to be the thing that catches a cut label before the owner does. It
has already failed at that twice while reading green (`"Tap to collec"`, `"THE NIGHT MARKET"`).
A pin over a dormant surface adds a third failure mode on top of those: it consumes a line of the
gate log, reads as coverage of the Collectors/Builders/Manage faces, and **cannot ever red**. The
next seat auditing "is the rail chip covered?" gets a yes, and it is not true.

⚠ **A dormant pin is not automatically a delete.** Case 15's dormancy is *deliberate* and the repo
already pins that it stays dormant. The rule is that dormancy must be **stated at the site and
proven**, never inferred by a reader from a green run. That is why §5 keeps it and §3/§4 do not.

---

## 3. CASE 3 `[manage-face]` — RE-POINT TO THE LIVE DOCK, OR DELETE

**The surface is retired. Read at source 2026-09-10, do not take it from here:**

`HudKitController.BindActionBar` (`Assets/_Modules/HUD/Kit/HudKitController.cs:3473`) opens at
**`:3480-3486`**:

```
if (_peacefulDockRoot != null)
{
    for (int i = 0; i < _barButtons.Length; i++)
        if (_barButtons[i] != null) _barButtons[i].SetActive(false);
    FlowTrace.Step("HudKit", "adaptive peaceful dock owns the actionBar; legacy repacker retired");
    return;
}
```

So whenever the peaceful dock exists, **every legacy bar face is disabled and `HudActionBarModel`
is never subscribed at all.** `CLAUDE.md` §7 says this in its own words — *"THE BOTTOM BAR THE
PLAYER TOUCHES IS THE ADAPTIVE PEACEFUL DOCK, NOT `HudActionBarModel`… no reasoning about the
shipped bar may start from that constant"* — and names the authority:
**`HudActionBarRegression.CheckMeasuredPeacefulDock`**, which BUILDS the real dock and reads the
count, captions, order, touch floor and label fit out of the built tree.

Case 3 (`HudLabelFitRegression.cs`, the `Case3_ManageFace` body; WO-1663's note sits at `:611-625`)
sizes its box from `HudActionBarModel.MaxVisibleFaces` (`HudActionBarModel.cs:139`) and measures
`ManageBaseLabel` + two canon badge lines against it.

**Fix shape — pick ONE and say which, with the proof:**

- **(a) RE-POINT (preferred if the dock exposes a per-slot width).** Measure the Manage caption
  against the **dock's** slot geometry, through `MeasureFacePx(SkinnedFaceRole, …)` — the dock
  faces are obsidian too. Take the slot width from `HudDockSlotLayout` /
  `DeNelle.Core.UI.HudDockLayout`, or from whatever `CheckMeasuredPeacefulDock` already resolves;
  **read those at source, do not retype a fraction.** ⛔ Do NOT duplicate what
  `CheckMeasuredPeacefulDock` already does — if it already measures caption fit, (a) collapses
  into (b) and you must say so.
- **(b) DELETE Case 3**, with the `:3480-3486` early return quoted verbatim in the RESULT as the
  no-caller proof, plus a one-line pointer at `CheckMeasuredPeacefulDock` left where the case was,
  so the next seat does not re-add it.

⛔ **Do not simply widen or re-word the case to keep it green.** ⛔ Do not touch
`HudActionBarModel.MaxVisibleFaces`, and **never renumber an `ActionBarButtonId` ordinal**
(`CLAUDE.md` §7 — the face arrays are indexed by ordinal).

---

## 4. CASE 2 `[collector-chip]` BRANCHES (a) AND (b) — FOUR CANON KEYS WITH NO DRAWING CALLER

**The producer no longer emits them.** `FormatCollectorChip`
(`Assets/_Modules/HUD/Kit/HudKitController.cs:2239-2246`) returns
`HudStrings.Get(HudStrings.KeyCollectorsTitle)` and **nothing else** — WO-1194 moved the storage
state onto the three resource rows. The chip's only runtime assignments are
`_collectorsChipLabel.text = FormatCollectorChip(cs)` (`:5373`) and
`= HudStrings.Get(KeyCollectorsTitle)` (`:2749`).

**Grepped `--include=*.cs` across `Assets/`, 2026-09-10 — re-run it, do not trust this table:**

| key | every non-JSON reference | drawing caller? |
|---|---|---|
| `KeyCollectorsFullLine` | `HudStrings.cs:61`, `:130`; `HudLabelFitRegression.cs:532`; `CollectorTellRegression.cs:233` | **none** |
| `KeyCollectorsNearlyLine` | `HudStrings.cs:65`, `:131`; `HudLabelFitRegression.cs:533`; `CollectorTellRegression.cs:234` | **none** |
| `KeyCollectorsWaitingLine` | `HudStrings.cs:68`, `:131`; `HudLabelFitRegression.cs:534`; `CollectorTellRegression.cs:235` | **none** |
| `KeyCollectorsCount` | `HudStrings.cs:55`, `:130`; `HudLabelFitRegression.cs:555`; `CollectorTellRegression.cs:231/249`; `SmartArgumentRegression.cs:26`; `LocalizationSmartStringPackageTests.cs:19` | **none** |

Every hit is an oracle, a test, or the key table. So Case 2 branch **(a)** (the action lines) and
branch **(b)** (the height budget, which wraps `KeyCollectorsCount`) both measure dormant copy.
WO-1663 recorded this at `HudLabelFitRegression.cs:523-547` and left branch (b)'s **108.0 of
112 px** margin standing over a string nobody paints.

**Fix shape:**
1. **Keep and re-point what IS drawn.** The chip really does draw the title (`"Harvest"`), so the
   title measurement stays exactly as WO-1663 left it — `MeasureFacePx(SkinnedFaceRole, title,
   ElarionUiKit.FontFloor, …)` against `RailChipWidthPx * ButtonLabelInset`.
2. **Delete branches (a) and (b)** and the `WrappedLineCount` / `LongestOf` calls that serve only
   them, quoting `FormatCollectorChip:2239-2246` as the proof. Leave a short comment naming
   WO-1194 as the reason the chip is one line now, so the next seat does not "restore coverage".
3. **The canon KEYS themselves:** decide in this ticket whether the four rows come out of
   `canon-strings.json` + the 11 locale files, or stay as dormant copy.
   ⛔ **If they stay, they must be pinned as dormant somewhere, not merely left** — an unreferenced
   key that nothing states is dormant is how the next author re-wires one and ships it unmeasured.
   ⚠ **Removing a key touches 13 JSON files** (`Assets/Resources/Data/Canonical/` +
   `Assets/StreamingAssets/Data/Canonical/`, `canon-strings.json` + `en/ar/de/es/fr/ja/ko/pt-BR/
   ru/zh-Hans`). Read memory `canonical-json-edits-binary-only-verify-newlines` FIRST: patch from
   HEAD bytes, prove the LF count, never a text-mode rewrite. **If that is more than this lane
   wants to carry, keeping the keys + adding the dormancy pin is an acceptable close — say which
   you did and why.**
4. **`CollectorTellRegression.cs:231-235` is a COPY-LAW check, not a render** — it asserts none of
   these strings says "Storage". If the keys stay, it stays untouched. If the keys go, it must be
   updated in the SAME change or it fails on a missing key. ⛔ Do not leave it dangling.

---

## 5. CASE 15 `[builders-chip-idle]` — KEEP, BUT THE DORMANCY MUST BE PINNED, NOT NARRATED

**The chip does not build.** `Assets/_Modules/HUD/Kit/HudKitController.cs:811`:

```
// BuildQueueStatusChip(pool);   // retired 2026-08-07 (owner)
```

and the method's own header (`:1930-1936`) states the wiring is kept **deliberately** — the chip is
*"two lines from returning"* — and that **`SessionShapeRegression Case7_OneDoor` FAILS the build if
that byte-exact retirement line disappears.**

**This one is NOT a delete.** The repo has already ruled that the surface returns, and 15a/15b pin
the Core words and the View relay, which are live regardless of the chip's call site. 15c is a
legitimate pre-emptive fit pin.

**Fix shape — make the dormancy MACHINE-CHECKED instead of a comment:**
- 15c keeps its WO-1663 measurement unchanged (`MeasureFacePx(SkinnedFaceRole, idleText, 22f, …)`).
- Add a check that the retirement is still true: assert `HudSrc` contains the byte-exact
  `"// BuildQueueStatusChip(pool);"` **and** that `Build(` does not contain a live
  `BuildQueueStatusChip(pool);`. **If the chip is ever un-retired, THIS CASE MUST SAY SO** — that
  is the moment 15c stops being pre-emptive and starts being live coverage, and a comment cannot
  notice it.
  ⚠ `SessionShapeRegression Case7_OneDoor` already pins the retirement line — **read it first**
  (`Assets/Editor/Regression/SessionShapeRegression.cs`). If it already asserts both halves, do
  **not** add a second copy; point Case 15's note at it instead and say that is what you did.
  Duplicated pins are this repo's most-documented failure mode (`CLAUDE.md` §2/§5/§16).
- Its `notes.Add` line must state **"dormant surface, pre-emptive pin"** in the gate log, so a
  reader of a green run cannot mistake it for the live bar.

---

## 6. THE TWO DUPLICATE BODY-HARDCODED MEASURERS — ONE FUNCTION, NOT A THIRD COPY

WO-1663 put the role + slack behind one `MeasureFacePx`. Two copies live outside it:

| where | what | read at source 2026-09-10 |
|---|---|---|
| `Assets/Editor/Regression/InventoryArmoryRailRegression.cs:775` | its **own private** `WrappedLineCount(text, boxW, fontSize)`, hardcoded to `FontRole.Body`; called at `:527` | a different class in the same assembly, so WO-1663's role parameter never reached it |
| `Assets/_Modules/Village/Hero/RumorBoardPanel.cs:126-128` | `public const float PageButtonBoldSlack = 1.10f;` — its doc comment says *"MeasureLineWidthPx sums regular-weight advances; the button is bold. 10% slack…"*; used at `:568` inside `PageButtonWidthPx`, which measures at `FontRole.Body` (`:564-565`) | a **RUNTIME** file: this number sizes a real button, it is not an oracle |

**Fix shape:**
- **`InventoryArmoryRailRegression`:** first **determine whether the faces it measures are
  obsidian** (`BuildObsidianButton` → `MedievalUiSkin.ApplyButton`) or plain
  `ElarionUiKit.Label`. ⛔ **Do not re-point it on the assumption that it is obsidian — WO-1663's
  whole §7 is that three plain sites would go falsely red.** If obsidian, delete its private copy
  and call the shared helper; if plain, leave the role at Body and state that it was checked.
  Whichever it is, the private duplicate should stop being a duplicate.
- **`RumorBoardPanel`:** the honest end state named in WO-1663 §9 — **one kit-side skinned-face
  measurer** that the oracle and the runtime both call, rather than a second constant. That means
  the function moves to `ElarionUiKit` (where `MeasureLineWidthPx` already lives), and both
  `HudLabelFitRegression.MeasureFacePx` and `PageButtonWidthPx` call it.
  ⚠ **This changes a RUNTIME button's width** (1.10 → 1.15 slack widens every rumor-board page
  button). That is a visible layout change: `RumorBoardLayoutRegression` pins
  `PageButtonWidthPx` (see `RumorBoardPanel.cs:560` — *"Public so RumorBoardLayoutRegression pins
  the same number"*), so **re-measure and update that pin in the SAME change**, and expect it to
  RED first. **If the two slacks cannot be reconciled without a felt decision, STOP and say so** —
  a runtime width is the owner's call, not a lane's.

---

## 7. WHAT THE `DataRegression` COUNT DOES — read this before deleting anything

**Deleting a CASE inside this suite does NOT move the marker.** The count in
`REGRESSION_OK <n>/<n> suites` is per **registered suite**, not per case:

- `HudLabelFitRegression` is registered **once**, at `Assets/Editor/Regression/DataRegression.cs:649`,
  inside a `Guard.Try`, and emits **one** `[hud-label-fit]` tag line.
- The numerator counts emitted tag lines minus skips; `suitesTotal = suiteTagLines + suitesRed`
  (`DataRegression.cs:2029-2035`).
- The denominator is **derived from source**, not a literal:
  `RegressionMarkerRegression.TryGetExpectedSuiteCount` counts **registration call-sites** between
  the fences (`DataRegression.cs:2044-2050`), and a **shortfall** — actual < expected — fails with
  `[suite-count] SUITE VANISHED FROM THE DENOMINATOR` (`:2064-2071`).

So: **remove cases freely — the marker is unchanged.** Only if you delete the whole suite would you
touch `:649`, and then **both** sides move together (registration call-site and tag line), which is
why that is the one edit that must be made in a single change. ⛔ **Never hardcode an expected
suite count anywhere** — the file calls that audit finding G8 at `:2043`.

---

## 8. RED-FIRST — WHERE A LIVE RE-POINT EXISTS

⛔ **A change that goes straight to green proves nothing** (`CLAUDE.md` §12). But note the shape
here: **a DELETE cannot red.** Its proof is the quoted no-caller evidence plus an unchanged marker,
not a red. So RED-first applies only where something live is newly measured:

1. **§6 `RumorBoardPanel`** — if the slack unifies at 1.15, `PageButtonWidthPx` returns a **larger**
   number and `RumorBoardLayoutRegression`'s pin **MUST red before you update it**. Record the
   pre/post width in ref px for at least one real page-button label. If it does not red, the
   unification did not take.
2. **§3(a), if you re-point Case 3 at the dock** — state the dock slot width you resolved and the
   measured caption width. If the caption fits with room, say so with both numbers; a
   proven-fitting live face is a legitimate green (WO-1663 §6's own wording).
3. **§5's new retirement assert** — prove it by flipping the source string in a scratch copy and
   seeing the case red, or by quoting the existing `SessionShapeRegression` pin that already does
   it. Do not claim it works unseen.
4. **§6 `InventoryArmoryRailRegression`, if obsidian** — the re-point must move at least one
   measured width; record pre/post.

**Method for every width in this ticket:** WO-1663 §3's offline reproduction from the committed TMP
YAML (`Assets/Resources/RpgUi/font/font_body.asset`, `font_title.asset` — both `m_PointSize 64`,
`m_Scale 1`). No Unity needed for the numbers.

⚠ **CASE, per site.** `MedievalUiSkin.ApplyButton:86` upper-cases the label **value** once at apply
time and never sets `FontStyles.UpperCase` (grep under `Assets/_Modules/` returns nothing). A face
whose `.text` is assigned again at runtime therefore draws the **authored** case. WO-1663's §2
carries the full proof — **do not measure the upper form of a runtime-assigned label**, and do not
"restore" the WO-1663 predicted reds, which were computed that way.

---

## 9. WHAT NOT TO TOUCH

- ⛔ **`MeasureFacePx`, `SkinnedFaceRole`, `SkinnedFaceWidthSlack`, `SkinnedFaceWhy`, `TryWrapLines`,
  `CheckNightMarketTitleFit`** — WO-1662/1663 shipped these. Extend; never copy, never re-declare.
- ⛔ **The three plain-label sites** WO-1663 deliberately left on `FontRole.Body`:
  `HudLabelFitRegression.cs:741` (Case 4 wave band), `:1832` (Case 10 heartfire), `:2465`
  (Case 13 heart objective). Moving them makes three working screens red.
- ⛔ **`MedievalUiSkin`, `ElarionUiKit.BuildObsidianButton`, `MeasureLineWidthPx`** — changing the
  skin re-skins every button in the game.
- ⛔ **The font assets** under `Assets/Resources/RpgUi/font/`, and every font floor
  (`FontFloor` 30, `FontHardFloor` 20, `ElarionUi.FontFloorMobile` 30).
- ⛔ **`RailChipWidthPx = 220f`** (pinned by `HudUiRegression` 8d) and the canon `storeWordmark`.
- ⛔ **The `GlyphBaseline` array** (`UICaptureLaunch.cs`) — shrink-only; nothing here may be
  baselined away.
- ⛔ **The `// BuildQueueStatusChip(pool);   // retired 2026-08-07 (owner)` line** at
  `HudKitController.cs:811` — byte-exact, pinned by `SessionShapeRegression Case7_OneDoor`.
- ⛔ **`HudKitController.cs`'s WO-1660/1662 regions** (gold chip band, Night Market `FitBlock`).
- ⛔ **The `CLI_LANES_WO_NUMBERS.md` banner** — this number was pre-assigned by the lead.

---

## 10. ACCEPTANCE

- [ ] Case 3 is **either** re-pointed at the peaceful dock's measured geometry through
      `MeasureFacePx` **or deleted with `BindActionBar:3480-3486` quoted verbatim** — and the
      RESULT says which, and why the other option was rejected.
- [ ] Case 2 branches (a) and (b) are gone, with `FormatCollectorChip:2239-2246` quoted; the
      **title** measurement survives unchanged; `CollectorTellRegression` is consistent with
      whatever happened to the four canon keys; the keys are either removed from all 13 JSON files
      or **pinned as dormant**, and the RESULT says which.
- [ ] Case 15 keeps its measurement and gains a **machine-checked** retirement assert (or a proven
      pointer at the existing `SessionShapeRegression` pin), and its note names the surface dormant
      in the gate log.
- [ ] Neither `InventoryArmoryRailRegression` nor `RumorBoardPanel` still carries a private copy of
      this measurement — **or** the RESULT states, with source lines, why one legitimately does.
      `InventoryArmoryRailRegression`'s faces are **classified obsidian or plain before** any
      re-point.
- [ ] Every live re-point in §8 was **seen RED before it was seen green**, with pre/post ref-px
      numbers per site. Deletes are proven by quoted no-caller evidence instead.
- [ ] `python tools/gate_brace.py <every .cs touched>` clean, NUL-free.
- [ ] `COMPILE_GATE_OK` + `REGRESSION_OK <n>/<n> suites` on **fresh** logs (markers, never exit
      codes). **`<n>/<n>` must be UNCHANGED** unless a whole suite was removed — if it moved, say
      why in the RESULT before anyone asks.
- [ ] A fresh `RunCaptureHeadless`: `UI_GLYPH_OK` with **0 new**, and no `TEXT TRUNCATED` line
      naming a rail chip or a rumor-board page button.
- [ ] The WO's own `**Status:**` line is flipped and a `.RESULT.md` written **by the lane that owns
      it**, both paths reported (`CLAUDE.md` §11 cadence).

---

## 11. WHY THIS IS ONE TICKET AND NOT FIVE

All five items are the same defect wearing five coats: **a measurement that has drifted away from
the thing it measures.** They share one file, one helper, one gate run and one review. Splitting
them would mean five gate cycles over the same suite and five chances for the "leave it, it's
harmless" answer to win on one of them. ⚠ They are, however, **independently landable** — if §6's
runtime slack turns out to need an owner ruling (a visible button width), land §3/§4/§5 and park
§6 with its pins surfaced rather than blocking the whole ticket on it.

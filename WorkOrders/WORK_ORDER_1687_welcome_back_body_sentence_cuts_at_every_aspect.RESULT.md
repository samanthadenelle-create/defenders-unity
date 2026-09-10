# WORK ORDER 1687 — RESULT

**Lane:** touch-floor / layout seat, 2026-09-10. Worktree `.claude/worktrees/agent-a35ba6c65858e79ea`
at `dev` **`e4b5906a5`**. **No Unity run, nothing committed** — by instruction.

---

## 1. Outcome

One band, at its driver, in one file.

| | before | after |
|---|---|---|
| `WelcomeBackPopup.cs:174` | *(inline `0.12f`)* | **`private const float CappedSentenceH = 0.21f;`** |
| `WelcomeBackPopup.cs:304` | `Mathf.Max(0.03f, y - 0.12f)` | `Mathf.Max(0.03f, y - CappedSentenceH)` |

Band height at the **smallest** reference height (965.4, the Seeker):
`0.12 x 470.3 = 56.4` -> **`0.21 x 470.3 = 98.8` ref px** — from short of two lines to three.

No font change. No copy change. No other band touched.

---

## 2. The question the coordinator asked: did WO-1664 cause it?

**No.** Three independent lines, all in §2 of the WO:

1. ⭐ **The glyph oracle had never run on this path.** `git show HEAD:Assets/Editor/UICaptureLaunch.cs`
   shows `RunWelcomeBackCaptureHeadless` at HEAD with **no `ResetGlyphOracle()` and no
   `ReportGlyphOracle()`** — WO-1664 added both. `UI_GLYPH_FAIL x3 **NEW**` means new to the *tally*,
   not new to the *build*. There is no prior measurement in which this label was ever whole.
2. **The arithmetic says it already cut at HEAD's band.** Two lines need ~62 ref px; HEAD's band was
   58.4 (2670) and 59.2 (2340). The oracle's own mesh extent — `y -196.7..-167.6`, **29.1 px** — is a
   single rendered line, corroborating it.
3. **The cut is width-driven, and WO-1664 changed no width.** 1920x1080 has the **tallest** band in
   pixels (65.3 at HEAD) and cuts the **most** (60/72). Height alone cannot produce that ordering.

⚠ **Not claimed:** WO-1664 shrank the band 3.3% (~2 px) via `BodyY0` 0.22 -> 0.24, and at 1920 that
crossed the two-line threshold (65.3 -> 63.1 vs ~62). **Excluded as the cause; not excluded as a
marginal contributor at one aspect.** One-line settle if wanted: revert `BodyY0` to `0.22f` alone and
re-run — it still reds.

---

## 3. Identification, not inference

The oracle reported `'ObsidianPanel/PanelContent/Zone_Body/Label'` drawing **72** printable glyphs.
`"Your realm gathers for a limited stretch while you are away. Nothing gathered is lost."` has
**exactly 72 non-space characters** (counted this session). That match is what pinned the producer to
the `_result.WasCapped` label rather than to any of the other body labels.

**Why it cut:** `FitBlock` arms Normal wrap + **Truncate** + autosize 26..32. TMP's Truncate drops what
overflows the band's HEIGHT. At the 26 px floor a line costs ~31 ref px; the old band bought 56.

---

## 4. RED line (quoted, from the lead's run)

```
[glyph-oracle] TEXT TRUNCATED [WelcomeBack_2670x1200 @2670x1200]
  'ObsidianPanel/PanelContent/Zone_Body/Label'
  ("Your realm gathers for a limited stretch whil...") draws 68 of 72 printable glyphs.
  (x -538.3..538.3, y -196.7..-167.6 ...
UI_GLYPH_FAIL x3 NEW over 6 panels
```
1920x1080: 60 of 72. 2340x1080: 67 of 72.

**GREEN is owed** — see §6. The RED-first evidence here is the lead's captured run, not a
reconstruction.

---

## 5. Diff and gate

```
Assets/_Modules/Village/Harvest/UI/WelcomeBackPopup.cs
  +:150-173  doc block for CappedSentenceH (the line arithmetic, the FontHardFloor rule,
             the AddFooterSentence precedent and why it is left alone)
  +:174      private const float CappedSentenceH = 0.21f;
   :304      Mathf.Max(0.03f, y - 0.12f)  ->  Mathf.Max(0.03f, y - CappedSentenceH)
```

`python tools/gate_brace.py` (the gate's own rule) -> **`GATE_BRACE_SUMMARY bad=0 of 1`**.
NUL scan -> **0**.

---

## 6. What is NOT proven

1. **No Unity run.** `UI_GLYPH_OK` with this finding gone, `WELCOME_BACK_CAPTURE_OK 6/6; touch=clean`,
   `COMPILE_GATE_OK` and `REGRESSION_OK` are all owed on fresh logs, judged by **marker**.
2. **Frames not opened.** The three `WelcomeBack_*.png` still need eyes on: whole sentence, no collision
   with the row above or the ready band below.
3. ⚠ **The row budget is the residual risk and is named in the WO's acceptance §4.3.** The sentence now
   takes 0.09 more of the body from the leftover `y`, and it is drawn **last**. In a worst-case report
   the `Mathf.Max(0.03f, ...)` clamp can still bite and re-truncate. If the capture shows it clamped,
   the next move is the body's ceiling — **not** the font, and **not** the copy.
4. **Nothing committed.**

## 7. Files touched

```
Assets/_Modules/Village/Harvest/UI/WelcomeBackPopup.cs        (one const + one call site)
WorkOrders/WORK_ORDER_1687_...cuts_at_every_aspect.md         (Status: IMPLEMENTED)
WorkOrders/WORK_ORDER_1687_...cuts_at_every_aspect.RESULT.md  (this file)
```

Not touched: the sentence text, the autosize floor, `AddFooterSentence`, the glyph baseline, and
WO-1664's `ActionBandY0`/`ActionBandY1`/`BodyY0`.

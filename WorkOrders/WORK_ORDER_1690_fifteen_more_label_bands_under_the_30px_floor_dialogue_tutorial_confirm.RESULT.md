# WO-1690 RESULT — two bands authored, one is a PREFAB not code, two are no-ops

**Lane:** FIT-GUARD (worktree `agent-a9e001ddb25631dda`, branched from dev `de91a22ee`)
**Date:** 2026-09-10
**Gate:** NOT RUN — no Unity in this lane. `gate_brace` → `bad=0 of 5`; NUL clean on all five `.cs`.

---

## 1. The blocking check that came first, and why it mattered

Before touching the kit I proved **which panel path built the confirm modal on device** — the frame
path and the procedural path have *different* header builders, and editing the wrong one changes
nothing the player sees (the WO-1652 §2 "adjacent line" error):

```
$ grep -ai "BuildObsidianPanel '.*skip" Builds/device-frames/2026-09-10_1235_363866_raid_logcat.txt
BuildObsidianPanel 'Skip Tutorial' frame=<none> PROCEDURAL path (frame sprite missing) — default zones ...
```

**PROCEDURAL.** So `ElarionUiKit.cs:832` (`Header(..., y0: 0.92f, y1: 0.98f)`) is the live band. Worth
the grep: the same log shows `frame=frame_core` / `frame_options` / `frame_quest` on other panels, so
the frame path is very much in use — guessing would have been a 50/50.

## 2. #1 — Skip-Tutorial confirm title: **AUTHORED**

**Root cause, stated exactly:** `BuildObsidianPanel` authors its header at **6% of the panel**. The
confirm modal is `0.34..0.66` = **0.32 of the canvas**, so 6% resolved to **26–28 px** and the guard
shrank the title to **23 px**. ⛔ **No fraction can be right for both** a full-screen panel and this
one — 6% would have to become ~63% of the small modal. **The band had to stop being a fraction.**

- `ElarionUiKitObsidian.cs` — new `MinBandPxForFloor(TMP_Text)` (the guard's own `minBand`
  arithmetic, at `FontFloor(30)` instead of `FontHardFloor(20)`) and `SeatHeaderBandInPixels(chrome)`,
  which re-anchors the title, its shadow **and the gilt rule** top-anchored at a fixed reference-px
  height. The rule moves with the band deliberately: leaving it would bisect the taller title.
- `ElarionUiKit.cs` (**hunk 1**, in `BuildConfirmModal`) — one call, placed before any other content
  is added so the shadow twin is still findable as the header's sibling.
- `ElarionUiKit.cs` (**hunk 2**, in `Header`) — the shadow is now named **`LabelShadow`**. It was also
  `Label`, so both halves shared one `relaxKey` and one modal logged two unattributable relaxations.
  ✔ Checked first: nothing under `Assets/Editor` keys on `PanelFill/Label` except this ticket's own
  allowlist entry, so no oracle is re-pointed by the rename.

⛔ **The line factor is measured per label, never hardcoded.** The coordinator's `35.65` is
`(30+1)×1.1499` — correct for the 1.15 font and **~5 px short** for the 1.26 one. Both appear on this
one modal (§4). `MinBandPxForFloor` reads `faceInfo.lineHeight / pointSize` off each label and
`SeatHeaderBandInPixels` takes **the taller of the pair**.

**Pinned:** `TextFitGuardArmRegression.CaseE_ConfirmModalTitleBandSeatsTheFloor` — builds the real
modal, converts its canvas to **WorldSpace at the reference-px extent** (a ScreenSpace canvas in an
edit-mode batchmode call reports the editor's 640×480 and every number is fiction), settles, and
fails if `band < MinBandPxForFloor(title)`. RED on the pre-WO-1690 tree (~26 vs ~37).

## 3. #2 — `'53/53'` nameplate: **NOT A CODE FIX. It is a prefab band.**

Traced the key to its builder rather than assuming: `HudKitController.cs:665` →
`ElarionUiKit.BuildTargetFrame` (`ElarionUiKitObsidian.cs:1922`) → **`InstantiateBlinkPrefab(...,
"TargetNameplate", "Target_Nameplate", "TargetFrame")`** at `:1927`. `StatBars/HealthBackground/Label`
is a **prefab hierarchy**, not the `ElarionUiKitNameplate.cs` code path I first opened.

⛔ **So there is no C# band to author here**, and I did not invent one. Fixing it means editing the
`Target_Nameplate` prefab asset (or overriding its rect at instantiation), and **which rect drives the
30 px I measured has not been probed.** Authoring a number now would be exactly the guess §12 forbids.
**Handed back: needs an art/prefab lane, or an owner ruling on overriding a Blink prefab's band.**
Its allowlist entry stays.

## 4. #3 — Affiliation → 30: **AUTHORED, per the 13:02 ruling**

- `DialogueView.cs` — `MakeLabel(..., 26, ...)` → **`30`**; header zone `anchorMin.y` **0.790 → 0.740**;
  body top clamp **0.780 → 0.730**.
- **Arithmetic, from the measurement, in the comment:** the Affiliation's 0.45 share logged
  `rect 1264x30`, so the zone is ≈66.7 px. A 30 px line needs `(30+1)×1.1499+2 = 37.65`; the zone
  therefore needs `37.65/0.45 = 83.7 px`, i.e. `0.195 × (83.7/66.7) = 0.2447` of the panel →
  `0.985 − 0.2447 = 0.740`.
- ⛔ **The 0.45/0.55 split is unchanged** — re-splitting would seat the tag by **moving the speaker
  name**, which the ruling forbids. The band grows DOWN into the body, which is a **scroll zone**, so
  it loses height, not content. Speaker's share becomes ~46 px and now genuinely seats its authored 36
  (needs ~44.6); it was ~36.7 px and had escaped relaxing only because `fitMin` landed exactly on 30.
- The `:345-352` comment documented the 26 ladder as current; it is **rewritten**, not left to become
  a lie, and it now records the ruling verbatim and *why* 26 was not a typo.

## 5. #4 / #5 — recorded no-ops, with the numbers

Neither shrank anything; only the minimum moved. **Both are left alone deliberately, not skipped.**

- **`EndState/.../Zone_Header/Label`** — `rect 1250x38`, floor 30→29, **renders 31 px**. Need at its
  1.26 factor ≈ 41 px; band is 38. One-line fix does not exist (the band is `Header`'s shared 6%
  again, on a full-size panel where it is otherwise fine). Renders **above** the floor → no-op.
- **`BodyWell/ScrollZone/Viewport/Content/Body`** — `rect 1220x33`, floor 30→28, **renders 30 px**.
  ⚠ **This one is a FINDING, not a band bug.** It is a multi-line `FitBlock` scroll body
  (`DialogueView.cs:384`, `minSize: 30f, maxSize: 30f`), and the guard applied **single-line** band
  arithmetic to a content rect that had not grown yet. Harmless here (fontSize unchanged) but it is a
  **guard-on-`FitBlock` false-positive class**. ⛔ Out of scope — the guard is not to be touched by
  this ticket. Worth its own ticket.

## 6. Files, with one unique grep token each (memory `git-apply-3way-drops-hunks-silently`)

| file | token to grep after any 3-way |
|---|---|
| `Assets/_Modules/Core/UI/ElarionUiKitObsidian.cs` | `SeatHeaderBandInPixels` |
| `Assets/_Modules/Core/UI/ElarionUiKit.cs` (hunk 1) | `SeatHeaderBandInPixels(modal.chrome)` |
| `Assets/_Modules/Core/UI/ElarionUiKit.cs` (hunk 2) | `"LabelShadow"` |
| `Assets/_Modules/HUD/DialogueView.cs` | `0.740f` and `13:02` |
| `Assets/Editor/Regression/TextFitGuardArmRegression.cs` | `CaseE_ConfirmModalTitleBandSeatsTheFloor` |

⚠ **`ElarionUiKit.cs` carries an uncommitted DOCK-CAPTIONS edit in the main tree.** My two hunks are
in `BuildConfirmModal` (~:1497) and `Header` (~:1552) — grep both tokens after the merge; a silently
dropped hunk here reads as "the fix didn't work on device".

## 7. What is NOT proven from here

- **That any of this compiles**, and `COMPILE_GATE_OK` / `REGRESSION_OK` on fresh logs. No Unity.
- **That the re-seated band renders correctly.** The arithmetic is sound and the pin will measure it,
  but the *look* of a taller confirm-modal header and a shorter dialogue body is a **felt/mockup
  judgement** — the owner's ≥95%-vs-mockup rule applies. Open the capture PNGs.
- **`DialogueView.ResizeToContent` re-pins zones to fixed-pixel bands** on a variable panel height.
  Whether it overrides the new `0.740` fraction at runtime is **unmeasured** — the fresh device log is
  what settles it, and if the Affiliation entry does not drop, that method is the first place to look.
- **#2's driving rect**, per §3.

## 7b. WO-1690b — the confirm modal's ACTION FACES (chain 41 front-door, `Builds/wave9-frontdoor1`)

Same builder, same root cause as the header, found by a different oracle. Quoted verbatim:

```
[touch-oracle] SUB-TOUCH-FLOOR BAND [StartNewConfirm_2670x1200 @2670x1200]
  'ObsidianPanel/PanelFill/ObsBtn_Keep My Realm'        resolves 356.9x48.5 ref px
  'ObsidianPanel/PanelFill/ObsBtn_Erase and Start New'  resolves 356.9x48.5 ref px
  -- shortest side 48.5 is 63.5 px UNDER ElarionUiKit.MinTouchPx (112). ClampMinTouch will grow it
  SYMMETRICALLY about its centre at runtime and spill it into both neighbours. Author the band AT
  the floor. Its host 'ObsidianPanel/PanelFill' measures 939.1x302.9 ref px, so the floor is 0.37
  of the host's HEIGHT -- author it in px, not as that fraction.
```

**The oracle diagnosed it for us and named the cure.** The faces were `y 0.10..0.26` = **16% of the
panel**, and this modal's `PanelFill` is only **302.9 ref px** tall → `0.16 × 302.9 = 48.5` ✔ the
arithmetic closes exactly. Identical shape to the header: *a fraction that is right on a big panel
is wrong on a small one.*

**Fixed:** `SeatConfirmFacesAtTouchFloor` (`ElarionUiKitObsidian.cs`) seats both faces
**bottom-anchored at `MinTouchPx` in reference px**, gives each caption the face minus a 6 px inset,
and **lifts the message band to 0.50** — a 112 px face rising from the 0.10 band would have topped
out near 0.47 and collided with the message's authored 0.40 floor. One call added to
`BuildConfirmModal`. TutorialSkipUi's and ObjectiveBannerUi's faces go through the same builder, so
they seat too.

**Pinned:** `CaseE`'s new `CheckFace` asserts, for BOTH faces, shortest side ≥ `MinTouchPx` **and**
caption band ≥ `MinBandPxForFloor`. RED on the pre-fix tree (48.5 vs 112).

### 7c. ⛔ THE COPY DOES NOT FIT, AND HERE IS THE NUMBER — owner's call, not mine

`"ERASE AND START NEW"` = **16 printable glyphs, 12 drawn**. From the glyph oracle's own rect
(`x 33.1..361.4`, font 30):

| | |
|---|---|
| text rect width today | **328.3 px** |
| per printable glyph @ font 30 | **27.36 px** (328.3 / 12) |
| 16 glyphs need | **437.7 px** of text width |
| face width needed (incl. its 28.6 px inset) | **466.3 px** |
| face width today | **356.9 px** |
| needed as a fraction of the 939.1 px host | **0.497 — and TWO faces plus a gap exceed 1.0** |

⛔ **So at 30 px on one line, this caption cannot fit in a half-width face at any panel size that
also holds a second face.** I did **not** shorten it (SAVE-GUARD's + the owner's copy) and did **not**
lower the floor. Two options for the owner, both now viable *because* the face is 112 px:

1. **Shorter words** — ~12 printable glyphs fit today at 30 px (e.g. `"START OVER"` = 9, `"ERASE & RESTART"` = 13 is still 1 glyph over).
2. **Let this caption wrap to two lines** — 2 × 35.65 = **71.3 px**, which fits comfortably inside the
   112 px face. This needs the caption to use wrap instead of `FitSingleLine`'s NoWrap/Ellipsis, which
   is a **kit-wide button-caption behaviour change** — ⛔ deliberately NOT made here on my own
   judgement; it wants a ruling.

`UI_GLYPH_FAIL` will keep firing on this label until one of those is chosen. **That is the oracle
being right, not a regression.**

## 7d. WO-1690c — EVERY ORACLE PASSED AND THE FRAME WAS STILL WRONG

Chain 43 reported `UI_TOUCH_OK 7/7` and `UI_GLYPH_OK 7/7`. I opened
`Builds/ui-capture/StartNewConfirm_2670x1200.png` (514,754 bytes) **myself**, and it is worse than
the hand-off described:

1. the body's first line **"Starting a new game erases your"** is drawn over the gold title
   `ERASE THIS REALM?`, which survives only as a smear underneath it — **and it sits ABOVE the
   panel's gold frame entirely**, outside the plate;
2. the body's last line **"This cannot be undone."** is cut in half behind the KEEP / ERASE faces —
   on a modal whose entire purpose is that sentence.

### The cause, read at source — and it is mine

`BuildConfirmModal` built the message with a bare `Label(content, message, 0.40f, 0.82f, …)` and
**never fit-protected it**: no `FitBlock`, no `FitSingleLine`. TMP therefore laid four wrapped lines
out **centred** on a band far too short for them and spilled symmetrically out of BOTH ends. That
label has been unbounded since the builder was written — **but my WO-1690b message lift to 0.50
shortened its band from 0.42 to 0.32 of the panel and made the spill worse.** I own that half.

⛔ **An unbounded label spills out of ANY band, so "move the band" was never the fix.**

### Why no oracle caught it — a coverage hole, not bad luck

| oracle | what it measures | why it read clean |
|---|---|---|
| glyph | characters **per label** | all four lines rendered; the count was perfect |
| geometry / touch | a label against **its own plate** | each label sat correctly in its own rect |
| §1.14 fit guard | culled or relaxed labels | an unbounded label is neither |

**Label-vs-label overlap had no rule at all.** That is the hole, and it is now closed.

### Fixed — both halves, because either alone comes back

- **`ElarionUiKit.cs`** — the modal is **sized to its content**: `0.34..0.66` → **`0.325..0.675`**.
  Arithmetic in the comment: header inset 6 + band 37.7 + gap 8 = 51.7; body 4 × 34.5 = 138;
  gap 8 + face 112 + inset 22 = 142 → **331.7 ref px** needed against **302.9** measured, so the
  modal was ~29 px short of what it is asked to hold. `0.32 × (331.7/302.9) = 0.350`.
- **`ElarionUiKitObsidian.cs`** — new `SeatConfirmBodyBetweenBands(message, headerBandPx)`: seats the
  body strictly between the header band's bottom and the face band's top **in px**, then
  **`FitBlock`s it**. `SeatHeaderBandInPixels` now RETURNS its band height so the reservation is
  computed, not guessed; the message lift was removed from `SeatConfirmFacesAtTouchFloor`
  (signature is now `(content, cancel, confirm)` — no stale callers, verified by grep).
- ⚠ **The bound is the load-bearing half.** With the body bounded, a copy too long for the modal now
  wraps and truncates *inside* it — which the glyph oracle **does** see — instead of silently
  painting over the title, which nothing saw.

### The oracle that would have gone red on this exact frame

`TextFitGuardArmRegression.CaseF_ConfirmModalBodyStaysBetweenHeaderAndFaces` — builds the real modal
on a WorldSpace canvas at the reference-px extent, then three assertions, weakest to strongest:

1. body top ≤ title bottom (the label-vs-label overlap);
2. body bottom ≥ face top (the cut last line);
3. **body rect must lie inside its own `PanelFill`** — the strongest, because a rect that escapes the
   plate overlaps no sibling, so rules 1–2 can both read clean while the text floats outside the
   frame. **That is precisely what shipped.**

⛔ Untouched as instructed: `TitleController.cs` (SAVE-GUARD's; the copy is now "Erase"/"Keep" per
the owner) and `UICaptureLaunch.cs`.

⚠ **Not proven from here:** that the re-sized modal renders correctly — **the next capture PNG is the
proof, and it must be opened, not inferred from `UI_*_OK`.** That is the whole lesson of this frame:
three oracles said 7/7 over a screen the player could not read.

## 8. Acceptance

| # | state |
|---|---|
| 5.1 entries drop on a fresh device log | **OWED** — #1 and #3 should drop; #2/#4/#5 stay (by design) |
| 5.2 before/after quoted per entry | **OWED** with that log |
| 5.3 `stillBlank` stays 0 | **OWED** with that log |
| 5.4 ruling recorded for #3; #4/#5 justified | **MET** (§4, §5) |
| 5.5 the session must walk the same path | **OWED** — title → new game → skip-tutorial confirm → dialogue → wave end-state, or an absent `relaxKey` proves only that the screen was not visited |
| 5.6 gate markers on fresh logs | **OWED — the lead's gate** |

⚠ **Expect ONE new key on the next log**, not a regression: the shadow is now `LabelShadow`, so if it
still relaxes it appears as `SkipTutorialConfirm/ObsidianPanel/PanelFill/LabelShadow`. That is the
rename working as intended — the pair is finally separable.

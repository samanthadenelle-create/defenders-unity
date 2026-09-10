# WORK ORDER 1658 — RESULT

**Status:** IMPLEMENTED — awaiting gate + a fresh device log
**Lane:** BAND-HEIGHTS. **Date:** 2026-09-10. **Base:** `aba49bd4cc9f76fad481212bdc927db8a6a63bb7` (dev).
**Unity was NOT run. Nothing was committed.** Three `.cs` files edited; `gate_brace` and a NUL scan pass.

---

## 0. THE ONE NUMBER EVERYTHING BELOW IS DERIVED FROM

`lineFactor` is not a constant — the guard reads it off the font at
`ElarionUiKitObsidian.cs:3251-3253`: `factor = faceInfo.lineHeight / faceInfo.pointSize`.

Every TMP font asset in the tree was opened and divided (2026-09-10):

| asset | pointSize | lineHeight | factor |
|---|---|---|---|
| `RpgUi/font/font_body.asset` | 64 | 88.32 | 1.380 |
| `RpgUi/font/font_title.asset` | 64 | 80.448 | 1.257 |
| `RpgUi/font/font_stamp.asset` | 64 | 81.024 | 1.266 |
| `RpgUi/font/Alata-Regular.asset` | 69 | 95.22 | 1.380 |
| **`Localization/Fonts/ElarionLocaleFallback.asset`** | **64** | **73.59375** | **1.14990** |

**`ElarionLocaleFallback.asset` is the only asset in the project that yields the `lineFactor 1.15`
printed on all eight device lines.** That is the citation; the factor was not assumed.

**THE LEGALITY THRESHOLD** — the guard relaxes iff `fontSizeMin > fitMin`, where
`fitMin = max(FontHardFloor, floor(h / factor) - 1)` (`ElarionUiKitObsidian.cs:3286`) and
`FitSingleLine` sets `fontSizeMin = FontFloor = 30`. So a band is legal at

```
floor(h / 1.1499) - 1 >= 30   ->   h >= 31 * 1.1499 = 35.65 px
```

**35.65 px is the hard truth; the WO's `(FontFloor+1)*lineFactor+2 = 37.65 px` is that plus the
guard's own 2 px minBand margin.** Every band below is authored to clear **37.65**, not merely 35.65.

---

## 1. THE MEASUREMENT — step 1 of §4, done before any edit

Source: `Builds/device-frames/2026-09-10_0943_363722_logcat.txt` (**6,482,483 bytes**, APK
2026.09.10.363722, Seeker). The WO cites the `_0929_` log (4,459,194 bytes); the `_0943_` log is the
later capture of the same build and carries all eight plus one extra grow line. Both were checked.

⚠ **`grep 'relaxKey='` returns 0 hits on BOTH logs.** `relaxKey=` is the token
`FitGuardRelaxAllowlistRegression` parses out of its own fixture text — the shipped `FlowTrace.Warn`
writes `TextFitGuard '<text>' [<path>]: …`. The grep in §5.2 of the WO, run verbatim, would have read
as "already fixed" on a log that carries all eight. **Use `grep 'TextFitGuard'` on a device log.**

```
$ grep -c 'relaxKey' Builds/device-frames/2026-09-10_0943_363722_logcat.txt
0
$ grep -c 'TextFitGuard' Builds/device-frames/2026-09-10_0943_363722_logcat.txt
33
$ grep 'TextFitGuard CENSUS' Builds/device-frames/2026-09-10_0943_363722_logcat.txt | tail -1
TextFitGuard CENSUS armCalls=503 armed=503 declinedNotPlaying=0 declinedNullText=0 evaluated=172 relaxed=8 stillBlank=0
```

**`relaxed=8`, `stillBlank=0` — the BEFORE figure. `relaxed` must fall to 0; `stillBlank` must stay 0.**

### The driver of each band, PROVEN — not read off anchors

| # | logged rect | driver, read at source | why this is proof, not inference |
|---|---|---|---|
| 1 | `398x26` | `HudKitController.cs:1837` `ResHintHeightPx = 26f`, applied as `hrt.sizeDelta` at `:3300` | the const **is** 26 and the rect **is** 26; the label is built at `:3293` with text `"+" + kinds.Length` and the log says `chars 2` for `'+4'` with `kinds.Length == 4` |
| 2–4 | `742x26` | `HarvestOverflowModal.cs:268` bar band `0.30..0.50` = **0.20 of the plate** | see the `Well` proof below — **the value label's rect IS the bar's rect**; `0.20 x 134 = 26.8 -> (int)26` |
| 5–7 | `742x32` | `HarvestOverflowModal.cs:282` waiting band `0.03..0.27` = **0.24 of the plate** | `0.24 x 134 = 32.2 -> (int)32` |
| 8 | `452x33` | `ManageScreenPanel.cs:4274` cost band `0.06..0.36` = **0.30 of the face** | the face's own FlowTrace **prints the host px outright** — no back-solve was needed (below) |

**The `Well` proof for #2–#4 — and a citation error of my own, caught and corrected.** My first pass
cited `ElarionUiKitObsidian.cs:486` (`BuildObsidianBar`). **That is the wrong bar.** Its root is named
`ObsidianBar_<kind>` (`:405`), which does not match the logged path. The `Well` in
`HarvestRow_Wood/Well/Label` names the OTHER kit bar, and the name is what tells them apart:

- `HarvestOverflowModal.cs:268` calls **`ElarionUiKit.Bar`** — `ElarionUiKit.cs:2114`.
- `Bar` builds its track as `Well(parent, anchorMin, anchorMax)` (`:2116`), and `Well` is
  `AddImage(parent, "Well", anchorMin, anchorMax, Track)` — **`ElarionUiKit.cs:157`**. That is the
  `Well` node in the device path, at the caller's two anchors exactly.
- `Bar` then parents the value label at `Label(trackGo.transform, "", 0f, 1f, …, 0f, 1f)` —
  **`ElarionUiKit.cs:2136-2139`** — i.e. **0..1 of the Well**.

So the value label's rect is the Well's rect is the caller's band, and the caller's band is the
driver. (`ElarionUiKit.Bar` does not fit the label itself; the modal does, at `:275`.) The in-code
comment was corrected to match. **Recording the error because the two bars are near-identically
shaped and the next seat will reach for the Obsidian one first — the node NAME is the discriminator.**

**#8, measured directly from the log rather than derived:**
```
MANAGE_HUB_HEART band 0.798..1.0 of a 556px host = 112px tall (floor 112) - the header band,
clear of every card; face='UPGRADE HEART' price='250 Crystals'
```
`0.30 x 112 = 33.6 -> (int)33`. Exact.

**The harvest plate height, back-solved and cross-checked two ways.** The same log fixes the row
count and the footer branch:
```
[Flow:Bank] harvest-result rows: statuses=4 merged=3 shown=3 waiting=3 burned=0 doors=3
            banked=0 pending=47781 overBy=0 footer='reassure'
```
`shown=3` and no "+N more", so `floor = RowsFloor = 0.30` and
`h = min(0.20, (0.86 - 0.30 - 0.02*3) / 3) = 0.16667`. Two independent bands then pin the content
rect: the bar needs `content ∈ [780, 810]` to truncate to 26, the waiting line needs
`content ∈ [800, 825]` to truncate to 32. **Intersection ≈ 805 px content -> a ~134 px plate**, which
reproduces both logged ints exactly. That agreement is the check.

---

## 2. TWO ERRORS IN THE WO's §3 — found by measuring, as §3 asked

§3 labelled itself **LOCATED, not proven**. It was right to.

**(a) #1's producer is in the wrong file.** §3 names `ElarionUiKitObsidian.cs:960` (`CurrencyChip`'s
tag `FitSingleLine`). That label is inside `if (hasTag)`, and `hasTag = iconSprite == null`
(`:936`) — the gold icon resolves on this path, so **the tag is never built**. The text is `+4`
(`chars 2`), which is neither the tag (`"Gold"`) nor the amount. The real producer is the **WO-1221
"+N" hint**, `HudKitController.cs:~3293`, whose height is the const at `:1837`.
**Consequence: `ElarionUiKitObsidian.cs` needed NO edit — and the brief's `~:960 / :857` guidance is
moot. That file is left untouched, so this lane is fully disjoint from the lead's uncommitted
WO-1652/WO-1656 edits at `~:3145-3180` and `:3308-3330`.**

**(b) "the guard tried to grow the band and could not" is a misreading.** §2 concludes from
`grew rect 26px -> 26px` that "the offset write was overridden by a layout group or a driven rect".
The rect is `anchorMin=(0,0) / anchorMax=(1,0)` (`HudKitController.cs:3297-3298`) — **`sizeDelta`-driven,
with no layout group above it** — so the offset write took. The guard's `minBand` uses
**`FontHardFloor`, not `FontFloor`** (`ElarionUiKitObsidian.cs:3269`):

```
minBand = (FontHardFloor + 1) * factor + 2 = 21 * 1.1499 + 2 = 26.148
deficit = 26.148 - 26 = 0.148 px      (int)26.148 = 26
```

**The grow succeeded — by 0.148 px, which the message's `(int)` cast hides.** It was aiming at the
*hard* floor, which is not the goal. Nothing about the rect is driven, and #1 is a plain authoring
fix like the other seven.

---

## 3. THE EDITS — step 2 of §4, at the DRIVER in every case

Three files. **`FontFloor` / `FontHardFloor`, `UiKitTextFitGuard`, the CurrencyChip amount's WO-697
no-fit law, `UICaptureLaunch.cs` and `FitGuardRelaxAllowlistRegression.cs` are all UNTOUCHED** (§6).

### 3.1 `Assets/_Modules/HUD/Kit/HudKitController.cs` — #1, the gold chip hint

```diff
@@ -1836,2 +1836,23 @@
-        private const float ResHintHeightPx = 26f;
+        private const float ResHintHeightPx = 38f;
```
(plus a 21-line doc comment carrying the arithmetic, the font citation, and correction (b) above, so
the number can be re-derived rather than copied — CLAUDE.md §8's duplicated-state rule.)

**26 -> 38 px.** After: `fitMin = floor(38 / 1.1499) - 1 = 32 >= 30` -> **no relaxation**;
`'+4'` renders at `FontMicro = 32` instead of 23. **+9 px on the most-seen text in the game.**

**Safety of growing downward, checked rather than assumed:** the hint hangs below the chip
(`pivot.y = 1`, `anchoredPosition = (0, -2)`), and `HudKitController.cs:4218-4219` does
`_resHintLabel.gameObject.SetActive(!open)` — **the hint is switched OFF whenever the expanded
resource stack is open**, so the extra 12 px of downward extent can never overlap `ResRow_Wood`.
Collapsed, nothing is drawn below it (`_resExpandedRow` is inactive). Acceptance §5.3 holds by
construction.

### 3.2 `Assets/_Modules/Core/UI/HarvestOverflowModal.cs` — #2–#7, both harvest lines

Four anchor edits inside `BuildRow`, apportioning the 134 px plate as one sum:

```diff
@@ -250 +277 @@   name
-                0.56f, 0.98f, ElarionUi.Parchment, ElarionUi.FontLabel,
+                0.61f, 0.99f, ElarionUi.Parchment, ElarionUi.FontLabel,
@@ -261 +288 @@   banked
-            var banked = ElarionUiKit.Label(plate.transform, row.BankedText, 0.52f, 1f,
+            var banked = ElarionUiKit.Label(plate.transform, row.BankedText, 0.59f, 1f,
@@ -268 +295,5 @@   store bar (its band IS the value label's band)
-                new Vector2(0.04f, 0.30f), new Vector2(leftEnd, 0.50f), withValue: true);
+                new Vector2(0.04f, 0.31f), new Vector2(leftEnd, 0.60f), withValue: true);
@@ -282 +313 @@   waiting
-                var waiting = ElarionUiKit.Label(plate.transform, row.WaitingText, 0.03f, 0.27f,
+                var waiting = ElarionUiKit.Label(plate.transform, row.WaitingText, 0.01f, 0.30f,
```
(plus a 27-line comment above `name` holding the whole sum, so a later edit re-derives it.)

| band | fraction before -> after | px before -> after (plate 134) | shipped fontSize |
|---|---|---|---|
| waiting (#5–7) | 0.24 -> **0.29** | 32.2 -> **38.9** | 29 -> **32** (`FontMicro`, no relax) |
| bar value (#2–4) | 0.20 -> **0.29** | 26.8 -> **38.9** | 24 -> **33** (autosize in [30,40], no relax) |
| name | 0.42 -> 0.38 | 56.3 -> **50.9** | 40 -> **40**, unchanged (needs 46) |
| banked | 0.48 -> 0.41 | 64.3 -> **54.9** | 50 -> **~47** (`FontBody` needs 57.5) |

Gaps: waiting-top 0.30 to bar-bottom 0.31 = 1.3 px; bar-top 0.60 to name-bottom 0.61 = 1.3 px. No
overlap. At 38.9 px, `fitMin = floor(38.9 / 1.1499) - 1 = 32 >= 30` -> **no relaxation on either.**

⚠ **THE COST, STATED RATHER THAN HIDDEN:** the banked figure — "the largest glyph on the plate on
purpose" per the code's own comment — renders at ~47 instead of 50, because 38.9 px had to come from
somewhere and the two over-tall bands were the only slack on a 134 px plate. **The hierarchy survives
(banked ~47 > name 40 > the two 30–33 px lines) and every band is far above the 30 px floor**, which
is the ruling this ticket enforces. The alternative — growing the plate — is rejected in §5 below.

**The kit's shared `Bar` was deliberately NOT touched.** `valueLabel` is parented at `0f,1f / 0f,1f`
of the Well (`ElarionUiKit.cs:2136-2139`), i.e. *every* `Bar` in the game reads those anchors. The
driver for this one screen is the caller's bar band — which is what moved. (§4.3.)

### 3.3 `Assets/_Modules/Village/UI/Manage/ManageScreenPanel.cs` — #8

```diff
@@ -4271 +4271,20 @@
-                vrt.anchorMin = new Vector2(vrt.anchorMin.x, 0.38f);
+                vrt.anchorMin = new Vector2(vrt.anchorMin.x, 0.39f);
@@ -4274 +4293 @@
-                var cost = ElarionUiKit.Label(heart.transform, price, 0.06f, 0.36f,
+                var cost = ElarionUiKit.Label(heart.transform, price, 0.03f, 0.37f,
```
(plus a 19-line comment carrying the measurement and the rationale.)

## ⭐ #8 — THE ANSWER THE WO ASKED FOR, WITH THE NUMBER

**It was FIXED, not skipped. The number: the band is 33.6 px and needs 35.65 — it is 2.05 px short,
not the "one px" §2 estimated.**

The WO is right that nothing shrank *today*: the line reads `fontSize now 30`, so `'250 Crystals'`
ships **at** the floor. **What moved is the guard's `fontSizeMin`, down to 28 — a standing licence for
the next price string to go sub-legible with no new warning.** `HeartUpgradeCost` is composed at
`ManageScreenVM.cs:610-613` as `ElarionUi.CompactNumber(HeartProgression.NextCost()) + " Crystals"`
— **it is a FUNCTION of the Heart's level, and the property's own doc comment gives `"750 Crystals"`
as its example**, so the string on screen is not the longest one this label ships. `CompactNumber`
does bound the growth (`"12.3k Crystals"` = 14 chars vs `"250 Crystals"` = 12), so the headroom is
two glyphs, not unlimited — the argument stands on a longer string being *reachable*, not dramatic.
A band that passes only because today's text is short is not authored, and the allowlist entry
cannot drop while the relaxation still fires.

The fix is two anchors inside the plate: **cost 0.30 -> 0.34 of the face (33.6 -> 38.1 px)**, verb
0.60 -> 0.59 (67.2 -> 66.1 px, still the "two lines at 30 plus leading" the note above it promises).
After: `fitMin = floor(38.1 / 1.1499) - 1 = 32 >= 30` -> **no relaxation**, and the price renders at
32 (the `FitSingleLine` max already passed at `:4280`) instead of 30.

**Mockup drift (§5.5): the plate height, the button art and the touch box are all unchanged** — the
edit moves only the two labels' own rects, exactly as the existing comment at `:4262-4264` requires.
`HeartSurfaceRegression:118-139` and `ManageMockupConformanceRegression:1048` pin the face's **name
and placement**, not these fractions — see §4, which reads both suites' *mechanism*, not just their
tokens. **Neither is exercised by these anchor moves as read; both are UNRUN by this lane and the
lead's gate is what decides.** The owner still judges the device frame against the mockup at >=95%.

---

## 4. PINS — re-pointed never deleted; nothing new was required

`grep -rn "ResHintHeightPx|resHint|0.36f|RowHeightMax|RowsFloor|HarvestRow_|HeartUpgradeCost|ManageHeartFace" Assets/Editor/Regression/`
returns exactly two hits, and **neither restates a number this lane changed**:

- `HeartSurfaceRegression.cs:131` — asserts the panel **contains** `"ManageHeartFace"` (name/route).
- `ManageMockupConformanceRegression.cs:1048` — an overlap/text-cover message **naming** that face.

**⚠ A TOKEN GREP IS NOT ENOUGH HERE, so the two suites that BUILD these screens were opened and their
MECHANISM read** — a conformance suite that measures `rect.height` would contain none of those
literals and would still fail on a moved anchor:

- **`ManageMockupConformanceRegression.cs` measures NOTHING.** `grep -c "RectTransform|GetWorldCorners|\.rect\b"` returns **0**. It is a **source-text** suite
  (`Has(panel, "…")` / `ParseConstF`). Its only heart-face assertions are the const
  `HubTitleBandPx = ElarionUiKit.MinTouchPx` (`:1043`) and the literal
  `"new Vector2(0.02f, _hubHeartY0), new Vector2(0.30f, 1f), OpenHeartSurface"` (`:1055`) — the
  BUTTON's rect, which this lane did not touch. The two label anchors it does not mention.
- **`HarvestResultShapeRegression.cs` asserts no rects either.** `grep "rect|Overlap|MinTouchPx|anchorMin|height"` yields two hits, both inside comments (`:269`, `:326`);
  case 11 pins `MaxRows == 3`, which is unchanged.
- **No harvest-side suite source-scans the anchor literals I moved.** All seven files that mention
  `HarvestOverflowModal` were regex-searched for a quoted string containing any of `0.01f 0.03f
  0.27f 0.30f 0.36f 0.50f 0.52f 0.56f 0.59f 0.60f 0.98f 0.99f` — **zero matches.**

Both name-pins are untouched by anchor moves inside the plate. Also verified untouched:
`TownBankCapRegression.cs:461-463` source-scans `HarvestOverflowModal.cs` for the literal
`"FitBlock(label, ElarionUi.FontFloorMobile"` and for the ABSENCE of a direct ellipsis-mode
assignment — **that scan targets `BuildRows`' footer sentence; every edit here is in `BuildRow`**, and
neither the `label` variable name nor the `FitBlock` call was altered.

**⛔ `FitGuardRelaxAllowlistRegression.cs` WAS NOT EDITED, deliberately.** §5.1 is explicit: an entry
comes off only when a **fresh device logcat** no longer carries its key. This lane cannot produce a
device log, so removing entries now would be removing them "to make the suite green" — the one thing
§6 forbids. The suite's red-on-a-NEW-relaxation behaviour is preserved intact. **No RED-first pin was
added, because the WO asks for none beyond the allowlist and no existing oracle restates a changed
number** — a new one asserting these fractions would be exactly the hand-maintained duplicate of live
state that CLAUDE.md §8 bans; the device log is the oracle here.

---

## 5. WHAT IS STILL UNPROVEN / RESIDUAL — read this before closing

1. **NOT VERIFIED ON A DEVICE. Everything above is arithmetic over a pre-fix log.** No post-fix
   logcat exists, so `relaxed=0` is a **prediction**, not a measurement. Unity was not run; there is
   no `COMPILE_GATE_OK` and no `REGRESSION_OK` from this lane. §5.6 is open.
2. **⚠ THE OVERFLOW BRANCH IS NOT FIXED, AND NO FRACTION CAN FIX IT.** `HarvestResultVM.MaxRows` is
   **3**, so when a 4th resource collapses into a "+N more" line the floor moves to
   `RowsFloorWithOverflow = 0.36` and `h = (0.86 - 0.36 - 0.06)/3 = 0.1467` -> a **~118 px plate**.
   There, 0.29 buys only **34.2 px** and the guard relaxes to 28. Three 36 px bands plus gaps need
   ~112 of 118 px, leaving ~6 px for name and banked — **the three-band row does not fit an 118 px
   plate at any apportionment.** It needs the row band itself to grow, and `RowsTop`/`RowsFloor`
   collide with the footer sentence at `0.225..0.29`; growing into it risks trading a shrink for a
   **cull**, which §5.3 forbids outright. **This is left unfixed and reported rather than smuggled in
   as a plate redesign.** Note it is still strictly better than today (28 vs 22/26). The observed
   session took the non-overflow path (`shown=3`, `footer='reassure'`), so **none of the eight logged
   lines is the overflow case** — a separate ticket, if the owner wants it closed.
3. **Whether eight is the whole list** — unchanged from §7 of the WO. One session, town + Manage +
   harvest. Expect the allowlist to GROW on unvisited screens; that is the leash working.
4. **`stillBlank` staying 0** is predicted, not proven. The one growth that could crowd a neighbour
   is #1's, and the `SetActive(!open)` check in §3.1 rules it out structurally — but only a device
   log settles it.
5. **The 134 px plate is a back-solve**, cross-checked by two independent truncations (§1). No
   FlowTrace in the harvest path prints the content rect in px; adding one would be the cheap way to
   turn this into a direct measurement.

---

## 6. THE ALLOWLIST — which entries should drop after the next device log

All eight, and **only after** `grep 'TextFitGuard' <fresh logcat>` stops carrying each key. Entry
line numbers are in the **main tree's** uncommitted `FitGuardRelaxAllowlistRegression.cs`.

| entry line | key | expected after |
|---|---|---|
| `:87` | `HudAreasHost/Area_ActionRail/Widget_resourceChipsCollapsed/CurrencyChip_Gold/Label` | absent — 26 -> 38 px |
| `:75` | `ObsidianPanel/PanelFill/HarvestRow_Wood/Well/Label` | absent — 26.8 -> 38.9 px |
| `:79` | `ObsidianPanel/PanelFill/HarvestRow_Iron/Well/Label` | absent — 26.8 -> 38.9 px |
| `:83` | `ObsidianPanel/PanelFill/HarvestRow_Stone/Well/Label` | absent — 26.8 -> 38.9 px |
| `:77` | `HarvestOverflowUI/ObsidianPanel/PanelFill/HarvestRow_Wood/Label` | absent — 32.2 -> 38.9 px |
| `:81` | `HarvestOverflowUI/ObsidianPanel/PanelFill/HarvestRow_Iron/Label` | absent — 32.2 -> 38.9 px |
| `:85` | `HarvestOverflowUI/ObsidianPanel/PanelFill/HarvestRow_Stone/Label` | absent — 32.2 -> 38.9 px |
| `:90` | `ObsidianPanel/PanelContent/ManageCategoryLauncher/ManageHeartFace/Label` | absent — 33.6 -> 38.1 px |

**Caveat on the harvest six:** they clear only on a session that reaches the harvest modal by the
**non-overflow** path (`shown` <= 3 with no "+N more"). A capture that hits the overflow branch will
still carry them, at floor 28 rather than 22/26 — see §5.2. Judge by the fontSize in the line, not by
the key's mere presence.

The verification commands (**corrected** — `relaxKey=` matches nothing on a device log, §1):
```
grep 'TextFitGuard CENSUS' <fresh logcat> | tail -1     # relaxed=8 must fall to 0; stillBlank stays 0
grep 'TextFitGuard' <fresh logcat> | grep 'floor 30'    # each fixed key must be absent
```

---

## 7. GATE EVIDENCE FROM THIS LANE

```
$ python tools/gate_brace.py Assets/_Modules/HUD/Kit/HudKitController.cs \
      Assets/_Modules/Core/UI/HarvestOverflowModal.cs \
      Assets/_Modules/Village/UI/Manage/ManageScreenPanel.cs
GATE_BRACE_SUMMARY bad=0 of 3            (exit 0)

$ for f in <the three>; do tr -dc '\000' < "$f" | wc -c; done
0 / 0 / 0                                 (no embedded or trailing NUL bytes)

$ git diff --stat
 Assets/_Modules/Core/UI/HarvestOverflowModal.cs          | 39 +++++++++---
 Assets/_Modules/HUD/Kit/HudKitController.cs              | 25 ++++++--
 Assets/_Modules/Village/UI/Manage/ManageScreenPanel.cs   | 23 ++++++--
 3 files changed, 79 insertions(+), 8 deletions(-)
```

⚠ **`grep -c $'\x00'` is NOT a NUL scan in Git Bash** — the shell passes an empty pattern and it
matches every line (it returned the three files' line counts, 5901/556/7653, which reads as a
catastrophic hit). `tr -dc '\000' | wc -c` is the check that actually answers the question.

**Files:**
- `Assets/_Modules/HUD/Kit/HudKitController.cs`
- `Assets/_Modules/Core/UI/HarvestOverflowModal.cs`
- `Assets/_Modules/Village/UI/Manage/ManageScreenPanel.cs`
- `WorkOrders/WORK_ORDER_1658_eight_label_bands_authored_under_the_30px_floor.md` (Status flipped)
- `WorkOrders/WORK_ORDER_1658_eight_label_bands_authored_under_the_30px_floor.RESULT.md` (this file)

**Left to the lead:** batch gate (`COMPILE_GATE_OK` + `REGRESSION_OK` on fresh logs, judged by the
marker), `python tools/board_build.py`, commit by explicit path. **Then a device session**, after
which the eight allowlist entries in §6 come off one by one against a fresh logcat.

---

# § RETIREMENT — the device session happened; all eight entries are off

*Appended 2026-09-10 by lane **FIT-GUARD** (worktree `agent-a9e001ddb25631dda`, branched from dev
`366c0630c`). The lane above authored the bands; this section closes the leash they were on.*

## R1. The warrant, read at source

| what | value |
|---|---|
| APK | **2026.09.10.363786** |
| PID | **8062** |
| log | `Builds/device-frames/2026-09-10_1019_363786_logcat.txt`, **7,005,654 bytes** |
| last of 13 census lines | `armCalls=354 armed=354 declinedNotPlaying=0 declinedNullText=0 evaluated=97 relaxed=0 stillBlank=0` |
| `grep -c 'relaxKey='` | **0** |
| `grep -c ']: rect '` | **0** (the legacy prose form — an old-shaped relaxation would still have been caught) |
| `grep -c '\[Flow:'` | **15,597** — the channel was live, so the zeros are real zeros |

Compare the session that opened the ticket (`..._0929_363722_logcat.txt`, PID 5095):
`evaluated=88 relaxed=8`. **Same instrument, same screens, eight → zero.**

⚠ **Screen-visit evidence is asymmetric, and the honest version is:** `CurrencyChip_Gold` and
`ManageCategoryLauncher` each appear in the log (1 line each); `HarvestOverflowUI` and
`ManageHeartFace` appear **zero** times — the harvest modal's visit is evidenced by the FRAME
(`2026-09-10_1016_363786_harvestresult.png`), not by a log token. Stated rather than smoothed over,
because "all eight screens were visited" is a claim the log alone does not carry.

## R2. What changed in the leash

`Assets/Editor/Regression/FitGuardRelaxAllowlistRegression.cs`:

- **All eight entries retired**; `Allowlist` is now `new RelaxEntry[0]`. ⛔ The array and the suite
  STAY — an empty leash is a live tripwire. `Judge()` reports any parsed relaxation as NEW, so the
  next band authored under `FontFloor(30)` reds on its **first** device log instead of shrinking text
  silently for months the way these eight did.
- **WO-1495 annotation kept and rewritten** (`WO-1658, origin 2026-09-10, remove-by 2026-12-10`).
  Verified by porting the meta-suite's own four regexes (`DeclLine`, `WoPointer`, `AnyDate`,
  `RemoveBy`) and running them over the real file: block `Allowlist` at line 116, WO ✓, remove-by ✓,
  **origin date survives the remove-by strip** ✓ — that strip-then-require step is the one that
  silently fails a block carrying only a remove-by. An absent block was rejected as an option: the
  declaration is what `[scan-alive]` counts, and `DefinitionalAllowlists` is another suite's file.
- **Case B decoupled**: its key was `Allowlist[0].Key`, which **ceases to exist** when the list is
  emptied — a RED-first case that dies of the success it guards is not a pin. It is now a literal,
  **plus** a new assertion that the violation is a hard-floor breach (`UNDER FontHardFloor`) and not
  merely an unlisted key: with the array empty, a bare count would have quietly stopped testing the
  floor at all.
- **Case C restructured**, which is the change WO-1658 could not land without. It asserted
  `hits.Count == Allowlist.Length` — so emptying the array would have RED-ed the suite for the very
  outcome this ticket exists to produce (caught by the DEVICE-FRAMES-3 lane's read). The fixture is
  now sized by `HistoricalRelaxationCount = 8`, a historical fact about APK 363722, and asserts two
  things pulling opposite ways: the eight legacy prose lines **still parse** (or every log already on
  disk is unreadable), and **all eight are now judged NEW** (or an entry came back without a ruling).
- The header records the **PathOf finding**: `UiKitTextFitGuard.PathOf` walks at most four parents, so
  a key is a **truncated address, not a unique one**. The six harvest keys read as two families only
  because the shared `HarvestOverflowUI` root was trimmed off three of them — **all six were one
  modal, and one fix**. Never read a key as a screen boundary.

## R3. Verified without Unity, and the limits of that

The suite cannot be run in this lane (no Unity). The **judgement logic** was ported to Python and run
against real data — the same method that proved the parser when it was written:

```
CASE C parse: 8/8      verdicts: all NEW           (allowlist genuinely empty)
CASE B verdict: HARDFLOOR   (not NEW — the ordering assertion holds)
CASE A verdict: NEW
CASE D on 2026-09-10_1019_363786_logcat.txt: 0 relaxations parsed -> GREEN
```

`gate_brace.py` → `bad=0`; NUL scan clean; braces 43/43.
⛔ **Not proven from here:** that the C# compiles, and `REGRESSION_OK` on a fresh log. The port shares
the algorithm, not the code. **The lead's gate is the proof.**

## R4. Bonus finding, free with this log — WO-1656 is confirmed on device

`grep -c 'TEXT-NEVER-SET'` on the 363786 log = **0**, and both stand-downs carry the new wording
(*"…standing down; whether that is empty-BY-DESIGN or never-set is the PRODUCER's call…"*). WO-1656's
fix is live on the device, which closes its acceptance §3 in the reworded form.

⚠ **A THIRD stand-down label has appeared** that WO-1656 never covered:
`Area_HeartStatus/Widget_heartStatus/HeartStatus/PartyNameplate/Label`. It is not a regression of
anything — it is the reworded warning doing its job on a label nobody has classified yet. Someone
should decide whether it is empty-by-design; it is **not** claimed either way here.

# WORK ORDER 1658 — Eight label bands are authored too short to seat the 30 px floor, and the fit guard has been hiding it

**Status:** FIXED — proven on a fresh device log, allowlist emptied 2026-09-10.

**THE WARRANT (§5.1: an entry drops only on a fresh device logcat — this is that logcat).**
APK **2026.09.10.363786**, PID **8062**,
`Builds/device-frames/2026-09-10_1019_363786_logcat.txt` (**7,005,654 bytes**, read at source by lane
FIT-GUARD). Last of 13 census lines:

```
09-10 10:18:58.675  8062  8103 I Unity   : [Flow:UI] TextFitGuard CENSUS armCalls=354 armed=354 declinedNotPlaying=0 declinedNullText=0 evaluated=97 relaxed=0 stillBlank=0
```

`grep -c 'relaxKey='` = **0**, `grep -c ']: rect '` = **0** — no relaxation in the new token form OR
the legacy prose. Non-vacuity: **15,597** `[Flow:` lines on that log, so the channel was live and the
zeros are real zeros. Frames covering the affected screens: `2026-09-10_1016_363786_harvestresult.png`,
`_1017_363786_town_goldchip_plus4.png`, `_1018_363786_managehub_250crystals.png`.

**ALL EIGHT ENTRIES RETIRED** from `FitGuardRelaxAllowlistRegression.Allowlist`, which is now empty —
and stays armed: `Judge()` reports any parsed relaxation as NEW, so the next sub-floor band reds on its
first device log. RESULTs:
`WORK_ORDER_1658_eight_label_bands_authored_under_the_30px_floor.RESULT.md` (lane BAND-HEIGHTS, the
authoring) + its `§ RETIREMENT` section (lane FIT-GUARD, the leash). Not gated — no Unity in this lane.

> ⚠ **§3 of this WO CARRIES TWO ERRORS — both corrected by measurement, both recorded in the RESULT.**
> (a) The `+4` label's producer is **NOT** `ElarionUiKitObsidian.cs:960` (that is the CurrencyChip TAG,
> which is not built on this path: the gold icon resolves, so `hasTag == false`). It is the **WO-1221
> "+N" hint** at `HudKitController.cs:~3293`, driven by `ResHintHeightPx` at `:1837`.
> (b) `grew rect 26px -> 26px` is **NOT** a driven-rect override. The rect is `sizeDelta`-driven and
> the write took; the guard's `minBand` uses `FontHardFloor` and lands at `(20+1)*1.1499+2 = 26.148`,
> a 0.148 px deficit that `(int)` truncates back to `26` in the message.
> §3 flagged itself LOCATED-not-proven; it was right to.
**Minted:** 2026-09-10 (lane FIT-GUARD; number **PRE-ASSIGNED by the lead** — this lane did NOT touch `CLI_LANES_WO_NUMBERS.md`)
**Silo / Lane:** UI authoring — band heights only. Eight small, file-disjoint edits; no kit mechanism changes.
**Type:** EXISTING.
**Severity:** P2. Nothing is culled today (`stillBlank=0`), but eight labels ship below the owner's legibility floor and **no PNG has ever shown it**.
**Raised by:** WO-1652 §6b. The play-mode census that ticket added is what made these visible at all.
**Blocks nothing. Blocked by nothing.** Each of the eight can land on its own.

---

## 1. THE EVIDENCE — one device session, eight silent floor relaxations

Read at source 2026-09-10 from `Builds/device-frames/2026-09-10_0929_363722_logcat.txt`
(4,459,194 bytes, APK **2026.09.10.363722**, Seeker, PID **5095**). Last census on that log:

```
[Flow:UI] TextFitGuard CENSUS armCalls=314 armed=314 declinedNotPlaying=0 declinedNullText=0 evaluated=88 relaxed=8 stillBlank=0
```

**The finding that makes these authoring bugs and not guard bugs:** every one of the eight lines reads
**`(0 post-check iterations)`** and the session's `stillBlank` is **0**. The guard's rescue loop — the
iterate-down-until-glyphs-render path that is its whole reason to exist — fired **zero** times. All
eight are the *static* `fitMin = floor(h / lineFactor) - 1` recompute: **the band was authored too
short to seat `FontFloor` (30), so the guard quietly shrank the text.** It is not saving these
screens; it is concealing them.

⚠ **And the capture never shows it.** On `Builds/wave5-manageflow3` the same guard reports
`armCalls=2642 armed=0 evaluated=0 relaxed=0` — headless captures never enter Play mode, so every PNG
we gate on renders these eight at their authored 30 px. **The player sees 21–29 px; the screenshot
shows 30.** That gap is WO-1652's whole subject, and these eight are what fell through it.

## 2. THE EIGHT — verbatim keys, floors and rendered sizes

`relaxKey` is `UiKitTextFitGuard.PathOf` output (label name + up to four parents). `floorFrom` is
`FontFloor` (30) in every case. **Fix them in this order — the first is on screen the most.**

| # | renders | floorTo | relaxKey | text seen |
|---|---|---|---|---|
| **1** | **23 px** | **21** | `HudAreasHost/Area_ActionRail/Widget_resourceChipsCollapsed/CurrencyChip_Gold/Label` | `+4` |
| 2 | 24 px | 22 | `ObsidianPanel/PanelFill/HarvestRow_Wood/Well/Label` | `3,000 / 3,000  FULL` |
| 3 | 24 px | 22 | `ObsidianPanel/PanelFill/HarvestRow_Iron/Well/Label` | `3,000 / 3,000  FULL` |
| 4 | 24 px | 22 | `ObsidianPanel/PanelFill/HarvestRow_Stone/Well/Label` | `3,000 / 3,000  FULL` |
| 5 | 29 px | 26 | `HarvestOverflowUI/ObsidianPanel/PanelFill/HarvestRow_Wood/Label` | `25,875 waiting, safe` |
| 6 | 29 px | 26 | `HarvestOverflowUI/ObsidianPanel/PanelFill/HarvestRow_Iron/Label` | `6,906 waiting, safe` |
| 7 | 29 px | 26 | `HarvestOverflowUI/ObsidianPanel/PanelFill/HarvestRow_Stone/Label` | `15,000 waiting, safe` |
| 8 | 30 px | 28 | `ObsidianPanel/PanelContent/ManageCategoryLauncher/ManageHeartFace/Label` | `250 Crystals` |

### #1 is the one that matters — the gold chip

```
TextFitGuard '+4' [HudAreasHost/Area_ActionRail/Widget_resourceChipsCollapsed/CurrencyChip_Gold/Label]: band too short to seat FontHardFloor line — grew rect 26px -> 26px (minBand 26, lineFactor 1.15)
TextFitGuard '+4' [HudAreasHost/…/CurrencyChip_Gold/Label]: rect 398x26 lineFactor 1.15 — floor 30 -> 21 (0 post-check iterations), fontSize now 23, chars 2
```

Read both lines together: the guard **first tried to grow the band and could not** — `26px -> 26px`
means the offset write was overridden by a layout group or a driven rect — **then** fell back to
shrinking the text to 23 px. It is **7 px under the owner's floor and 3 px off `FontHardFloor`**, on
the **always-on HUD rail**, i.e. the most-seen text in the game. It is also the only one of the eight
that needed a grow attempt at all.

### #8 is the mildest — and may need no edit

`'250 Crystals'` moved its *floor* 30 → 28 but its **`fontSize` stayed 30**, so nothing actually
shrank. It is one px of band away from being a real defect. Fixing it is cheap; leaving it is
defensible. **Say which, with the measurement, rather than silently skipping it.**

## 3. PRODUCERS — located at source 2026-09-10

⚠ **LOCATED, not proven.** These are the construction sites the keys resolve to by name. The rect that
actually measures 26/32/33 px is the implementing lane's **first** measurement — do not edit a number
before a `FlowTrace`/probe read says that rect is the one. (§12; this is the same "read the adjacent
line" error WO-1652 §2 was written to correct.)

- **#1 gold chip** — the only `FitSingleLine` inside the `CurrencyChip` builder is
  `Assets/_Modules/Core/UI/ElarionUiKitObsidian.cs:960` (`CurrencyChip` starts at `:857`; the amount
  label is deliberately NOT fitted, WO-697 kit law at `:965` — **do not add a fit there**). The band
  is the chip's own height inside `Widget_resourceChipsCollapsed`, registered at
  `Assets/_Modules/HUD/Kit/HudKitController.cs:3305`, whose host row is sized by `ResHintHeightPx` at
  `HudKitController.cs:3300-3301`. **The `26px -> 26px` grow failure says the rect is layout-driven** —
  fix the DRIVER's height, not the label's offsets.
- **#2–#4 harvest well lines** — the bar's `valueLabel`, text set at
  `Assets/_Modules/Core/UI/HarvestOverflowModal.cs:274` and fitted at `:275`. The bar is built just
  above it at anchors `(0.04, 0.30)–(leftEnd, 0.50)`, i.e. **0.20 of a row** whose own height is
  `h = Mathf.Min(RowHeightMax /* 0.20f, :181 */, (band - RowGap * n) / n)` at `:190`. A fraction of a
  fraction is why it resolves to 26 px — **this is the classic "band authored as a fraction of panel
  height" root cause already written up in the guard's own comment.**
- **#5–#7 harvest waiting lines** — `Assets/_Modules/Core/UI/HarvestOverflowModal.cs:282-286`
  (`row.WaitingText` at anchors `0.03..0.27` of the same row, fitted at `:286` with `ElarionUi.FontMicro`
  as the max). Resolves to 32 px.
- **#8 Manage heart face cost** — `Assets/_Modules/Village/UI/Manage/ManageScreenPanel.cs:4274-4280`
  (the `cost` label at `0.06..0.36` of the face, fitted at `:4280` with a 32 px max). The face is named
  at `:4258`. Resolves to 33 px.

## 4. WHAT TO DO

For each of the eight, in the order of §2:

1. **Measure first.** Confirm which rect resolves to the height in the log line, with a probe or a
   `FlowTrace` read — not by reading anchors and inferring.
2. **Author the band tall enough to seat the 30 px line.** The guard's own arithmetic gives the target:
   `minBand = (FontFloor + 1) * lineFactor + 2`. At the measured `lineFactor 1.15` that is
   **≈ 37 px** (the guard's existing `minBand` uses `FontHardFloor` and lands at 26 — that is the
   *hard* floor, not the goal).
3. **Where the rect is layout-driven** (#1 proved this by failing to grow), raise the **driver's**
   height — the layout group, the row height constant, the fraction — never the label's offsets, which
   the group will overwrite.
4. **Re-run a device session and re-grep.** The entry leaves the allowlist when the log stops carrying
   its `relaxKey`.

## 5. ACCEPTANCE

1. **The allowlist shrinks to zero.** `FitGuardRelaxAllowlistRegression`'s `Allowlist` (WO-1652
   remedy B, `Assets/Editor/Regression/FitGuardRelaxAllowlistRegression.cs`) is emptied **entry by
   entry, as each band is fixed** — an entry comes off only when a **fresh device logcat** no longer
   carries its `relaxKey`. ⛔ **Never remove an entry to make the suite green.** The suite reds on a
   NEW relaxation; that behaviour must survive this ticket intact.
2. **Proof per entry:** the `relaxKey` grep on a fresh logcat, quoted with the log path and its byte
   size, before and after.
   ```
   grep 'TextFitGuard CENSUS' <fresh logcat> | tail -1      # relaxed=N must fall
   grep 'relaxKey=' <fresh logcat>                          # the fixed key must be absent
   ```
3. **`stillBlank` stays 0** on that log. Growing a band must never push a neighbour into a cull — that
   would trade a shrink for the very defect the guard exists to catch.
4. **The capture stays honest.** Re-run the full UI capture and show `UI_GLYPH_*` still fires on a
   genuinely mis-authored label. Captures run un-guarded by design (WO-1652 remedy B) — if the oracle
   goes quiet, that is a regression of the detector, not a pass.
5. **No mockup drift.** Each grown band is judged against its device frame; the owner's ≥95%-vs-mockup
   rule applies to the Manage screens (#8).
6. `COMPILE_GATE_OK` + `REGRESSION_OK <n>/<n> suites` on **fresh** logs, judged by the marker.

## 6. ⛔ WHAT NOT TO TOUCH

- ⛔ **`ElarionUiKit.FontFloor` (30) and `FontHardFloor` (20).** Read, never written. Lowering a floor
  to make a band legal inverts this entire ticket — and `FontHardFloor` carries the owner's F8
  2026-07-08 *"text will never be able to be seen on mobile at this size"* ruling.
- ⛔ **The fit guard itself** — `UiKitTextFitGuard`, `ArmFitGuard`, the relax/rescue mechanism, the
  census and the stand-down branch. This ticket exists *because* the guard reported correctly. Do not
  "fix" the report.
- ⛔ **Do not add `FitSingleLine` to the CurrencyChip amount label** (`ElarionUiKitObsidian.cs:965`
  states the WO-697 kit law: a currency amount never auto-shrinks).
- ⛔ **Do not silence or widen the allowlist** to close entries. §5.1.
- ⛔ **`UICaptureLaunch.cs`** — the capture stays un-guarded (WO-1652 remedy B ruling).
- Do not hand-edit `.unity` scenes.

## 7. WHAT IS UNPROVEN

- **Whether any of the eight ever CULLS for a player.** On the measured session none did
  (`stillBlank=0`). They render *below the floor*, which is the ruling they violate — a cull is a
  worse, separate failure that has not been observed.
- **Whether eight is the whole list.** It is eight *on one session covering town + Manage + harvest*.
  Screens not visited that session cannot have reported. Expect the allowlist to GROW before it
  shrinks — that is the leash working, not a regression.
- **Which rect owns each band.** §3 locates the producers by name; none of the four heights has been
  probed. See §4.1.

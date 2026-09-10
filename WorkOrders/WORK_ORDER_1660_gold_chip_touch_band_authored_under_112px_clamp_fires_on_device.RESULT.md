# WORK ORDER 1660 — RESULT

**Status:** IMPLEMENTED - awaiting gate (no Unity run in this lane; the lead gates)
**Lane:** HUD-CHIP, isolated worktree `.claude/worktrees/agent-a879bf9887691bfa1`
**Base:** `c3e767666d502b603dbb83fd62a0455a87b7ed57` (the commit that mints WO-1660), ff-merged from `dev`
**Date:** 2026-09-10

---

## 1. WHAT CHANGED — three files, no others

### A. The driver: the band is authored in px, at the floor
`Assets/_Modules/HUD/Kit/HudKitController.cs`

| Line | Change |
|---|---|
| `:3206-3235` | New WO-1660 comment block: the device measurement verbatim, why a fraction cannot know the floor, why the clamp stays armed, and the stated geometry delta (below). |
| `:3235-3236` | `CurrencyChip(... new Vector2(0.05f, 0.45f), new Vector2(1f, 1f) ...)` → `new Vector2(0.05f, 1f), new Vector2(1f, 1f)` — y is now a POINT anchor on the ActionRail band's top edge. |
| `:3236-3239` | `goldRt.pivot = new Vector2(0.5f, 1f); goldRt.sizeDelta = new Vector2(0f, RailChipHeightPx); goldRt.anchoredPosition = Vector2.zero;` — the same pivot-then-sizeDelta idiom `RailBand` (`:2252-2265`) uses for the Echoes/Builders chips. `RailChipHeightPx = ElarionUiKit.MinTouchPx` (`:1787`); **no literal 112 or 103.5 is typed anywhere.** |
| `:1826-1835` | Stale design note corrected (CLAUDE.md §15): it read *"the gold chip … stays >= MinTouchPx via ClampMinTouch"*, which is the defect written down as if it were the design. |

**x is deliberately untouched** (still stretched 0.05..1.0): the chip measures 398 ref px wide on the
Seeker, far above the floor; the four expanded rows inherit that width; and `HudRailGutter`'s
stretched branch (`:5661-5662`) owns the right edge by writing `offsetMax.x` only, so the two axes
never cross. Read at source before changing the y axis.

**⛔ Not touched, as §6 requires — verified by string count in the post-edit file (each exactly 1):**
`ElarionUiKit.ClampMinTouch(tapBtn)`, `SetResourcePanelOpen(!_resChipsExpanded)`,
`_resExpandedRow.transform.SetParent(tapGo.transform`, `RailChipHeightPx = ElarionUiKit.MinTouchPx`.
`ResHintHeightPx = 38f` unchanged. No fit call added to the amount (WO-697 law). `ClampMinTouch`'s
behaviour and the `[touch-oracle] CLAMP FIRED` warning are byte-identical.

### B. The capture hole is CLOSED
`Assets/Editor/UICaptureLaunch.cs`

| Line | Change |
|---|---|
| `:631-639` | `count += CaptureAdaptiveHud();` added to `RunCaptureHeadless`'s panel list, with the hole written down beside it (the same shape as the Night Market concession at `:604-607`). |
| `:1698-1707` | `RunAdaptiveHudCaptureHeadless` now calls the shared wrapper and DERIVES its expected count: `LandscapeTargets.Length * AdaptiveHudShotsPerTarget` instead of the literal `9` (a second copy of the target table). |
| `:1709-1717` | New `AdaptiveHudShotsPerTarget = 3` and `CaptureAdaptiveHud()` — the ONE call site of the body, so the gated run and the focused entry point can never measure different HUDs. |

`CaptureAdaptiveHudOnce` needs **no play mode and no extra fixture**: it reflects
`VillageHudController` + `HudKitController.Create`, registers its own `HudModel`, spins a temp
`EventSystem`, applies postures directly and tears everything down in `finally`. It is edit-mode
safe by construction, so §4B's escape hatch ("say so and leave the entry point as is") was **not
needed** — the entry point is wired.

### C. The RED-first pin
`Assets/Editor/Regression/HudUiRegression.cs` — check 7, new sub-case **7h**.

| Line | Change |
|---|---|
| `:1489` / `:1781` | `ReadRightColumn` gains a `failures` list (both call sites updated). |
| `:1492-1512` | **7h**: `col.GoldAuthoredH < ElarionUiKit.MinTouchPx - 0.5f` FAILS with the authored px, the deficit and the remedy. The floor is read from the constant; the number is never restated. |
| `:1537-1539` | `RightColumn.GoldAuthoredH` — the height **as authored**, i.e. what ASSERT A measures, before any clamp rescue. |
| `:1574-1613` | `ReadRightColumn` now models **both** authoring forms explicitly, and FAILS on neither. |
| `:1355-1357`, `:1402-1407`, `:1453-1458`, `:1632-1640` | Comments corrected in the same change (§15) — including the check-8 red-proof table, which is now labelled as the WO-1435 record with the ~4.25 px shift named rather than a hand-recomputed table typed in. |

**Why the two-form model, and why "neither" is a FAILURE not a note:** `ReadGoldChipMinY`'s regex
still MATCHES the new call (`new Vector2(0.05f, 1f)` → minY 1.0 → a 0 px chip). Left alone, the
oracle would have modelled a HUD the game does not have and reported on it — an oracle carrying a
stale model reports GREEN over a real defect, which is the WO-1435 failure and is how 103.5 shipped.
Verified by running both regexes over the post-edit file: px form matches `True`, legacy parse
returns `1` (so the fallback is unreachable while the fix is in place, and is a hard FAIL if it ever
is reached).

---

## 2. THE RED RUN — exact revert for the lead

One revert produces **both** reds; re-applying makes both green.

```
git checkout c3e767666d502b603dbb83fd62a0455a87b7ed57 -- Assets/_Modules/HUD/Kit/HudKitController.cs
```

Safe: the brief confirms nothing else uncommitted touches that file (the main tree's uncommitted
`FitGuardRelaxAllowlistRegression.cs` edit is a different file and a different lane).
`UICaptureLaunch.cs` + `HudUiRegression.cs` stay in place — they are the oracles doing the measuring.

Expected reds with that one file reverted:

1. **`HudUiRegression` 7h** — `RESOURCE RAIL — the collapsed gold chip's band is AUTHORED at 103.5 ref px at 2670x1200, 8.5 px UNDER ElarionUiKit.MinTouchPx (112).`
2. **`UI_TOUCH_FAIL`** from `RunCaptureHeadless` — `SUB-TOUCH-FLOOR BAND` findings naming
   `…/Widget_resourceChipsCollapsed/CurrencyChip_Gold`, with the host rect printed
   (`LayoutOracle.cs:216-232`).

   ⚠ **AT TWO OF THE THREE ASPECTS, NOT ONE — AND THAT IS THE DISCRIMINATING CHECK.** The pre-fix
   height is 0.55 x the ActionRail band, and the band is `(0.965 - 0.770)` of the canvas
   (`HudAreasHost.cs:130`). Run through the regression's own `CanvasRefHeight` (log-weighted
   MatchWidthOrHeight 0.5, ref 1080x1920), computed offline this session:

   | target | canvas ref height | ActionRail band | pre-fix chip | vs MinTouchPx 112 |
   |---|---|---|---|---|
   | 1920x1080 | 1080.0 | 210.6 | **115.8** | GREEN |
   | 2340x1080 | 978.3 | 190.8 | **104.9** | **RED** |
   | 2670x1200 | 965.4 | 188.2 | **103.5** | **RED** |

   The 2670 row reproduces the device's `authored 398.1x103.5` to the tenth, which is what makes
   this arithmetic a model of the Seeker rather than of itself. So: **no finding at 1920, findings
   at 2340 and 2670** (per posture — the chip is occupied in CalmTown, so expect Peaceful and
   GearOpen; the Combat posture does not occupy `resourceChipsCollapsed`). If ONLY 2670 reds, or
   1920 reds, the harness canvas and the regression model disagree and that is a finding in its
   own right — ASSERT A prints the measured chip AND host rect, so the lead can read 103.5 / 188.2
   straight off the line and confirm they agree.

⚠ **The red run must go through `RunCaptureHeadless`, not `RunAdaptiveHudCaptureHeadless`** — the
latter calls no `Report*` and therefore emits no `UI_TOUCH_*` marker at all. That is the hole, and
it is why §3 of the ticket is right that both halves of the oracle missed this.

Then `git checkout -- Assets/_Modules/HUD/Kit/HudKitController.cs` (or re-apply from the lane) for
the green run.

---

## 3. WHAT I HAVE **NOT** PROVEN (CLAUDE.md §11B)

- **No Unity ran in this lane** (instructed). Every claim above is a source read or an offline
  regex/arithmetic check on the post-edit files. `COMPILE_GATE_OK`, `REGRESSION_OK`, `UI_CAPTURE_OK`,
  `UI_TOUCH_OK` are all the lead's to measure on a fresh log.
- **⚠ CORRECTION TO THE TICKET'S §9 FRAMING — the body HAS run; its FINDINGS were discarded.**
  WO §9 says its author did not run `RunAdaptiveHudCaptureHeadless`, which is true, but
  `HudUiRegression` check 9's header (read at source this session) was written from
  `Builds/ui-capture/AdaptiveHudGearOpen_2670x1200.png` — so the capture body ran on 2026-09-06 for
  WO-1465 and produced real PNGs. What has NEVER happened is that body running **under a `Report*`
  marker**: `RunAdaptiveHudCaptureHeadless` calls none, so whatever `AuditGeometry` pushed into
  `_touchFailures` was thrown away unread. Same defect, stated more precisely — the pixels existed,
  the verdict never did.
  So the lead's gate is the **first MARKER-JUDGED** measurement of the town HUD canvas through
  `RenderCanvasToPng`, which means its findings are reported for the first time from **every** rule:
  RULE 1 (glyphs off plate), BUTTON OVER TEXT, ASSERT A (touch floor) and ASSERT C (glyph
  survival). **Findings other than the gold chip are entirely possible there, and they are
  FINDINGS, not regressions from this ticket** — the canvas was simply never measured before. If
  they appear, they are new tickets; do not weaken the oracle to green this one.
- **`UI_CAPTURE_OK <count>` moves by +9** (3 postures × 3 landscape targets) and `_touchPanelsChecked`
  by the same. The WO-1080 capture stamp's baseline shifts accordingly — expected, not a defect.
- **Nothing pins the capture COUNT — checked, not assumed.** `grep -rn "UI_CAPTURE_OK" tools/
  .claude/skills/ Assets/Editor/ *.ps1` returns only prose/comments plus the deliberate
  `"UI_CAPTURE_OK 51"` FIXTURE strings in `CaptureProvenanceRegression.cs:526` and
  `RegressionMarkerRegression.cs:41` (a line the marker parser must REFUSE). No live assertion
  carries a literal frame count, so +9 cannot break a chain green-on-green.
  `UiCaptureCoverageRegression` guards the AutoPilot `_mapping.json` rows, not this list, and no
  mapping row was added.
- **UNPROVEN — ordering interaction.** The adaptive-HUD body previously ran alone in its own entry
  point; it now runs mid-list, before MaintenanceBanner / HeroSelect / PlayerDecks / Manage /
  BuildCollections. `HudKitController.Create` writes STATICS the `finally` does not undo (it tears
  down GameObjects only) — e.g. `TutorialHighlightRegistry.Register("hud.builders_chip", ...)` and
  `FlowTrace.Once` keys. I have not measured whether that perturbs the five panels after it. It is
  cheap to read off the same gate log: if any of those five newly degrades, move the call to the
  END of the list. Watch the fidelity marker too — if the HUD build reads `Screen.*` directly the
  harness will name it Screen-stuck.
- **The device half of §7 is unmeasurable from here.** A fresh Seeker logcat on the next APK must
  carry no `[touch-oracle] CLAMP FIRED` line naming `CurrencyChip_Gold`, and no `TextFitGuard`
  relaxation for `…/CurrencyChip_Gold/Label`. Marker/line absence on a **fresh** log is the proof.

### One geometry delta, named rather than hidden
The clamp grew the chip **symmetrically about its centre**
(`ElarionUiKit.cs:1172-1186`), so on APK 363786 the chip's top edge sat **4.25 ref px ABOVE** the
ActionRail top. Authored as a px band hanging from that top edge, it now lands **on** it — so the
chip, the `+N` hint and the four expanded rows all seat **~4 ref px lower** than the shipped frame.
Nothing needs a second edit: `HudRailClearance` measures the laid-out bottom edge and the harvest
chip follows, and 7g's viewport check has hundreds of px of margin. It is called out here so the
owner's §7 device re-frame is not a surprise.

### WO §5 item 2 (the clamp-ring assert) is deliberately NOT implemented
`ElarionUiKit.cs:1092-1098` states it in the kit's own words: `LateUpdate` never runs in an
edit-mode capture, so `ClampGrowths` records **zero** headlessly **on a genuinely broken panel**. An
`Assert(ClampGrowths.IsEmpty)` in an editor regression would therefore be green by construction —
a hollow assertion of exactly the kind `docs/reference/HOLLOW_ASSERTIONS_REGISTRY.md` exists to
forbid, and the same class of "true by construction" success this file's own check 7b bans. The
real coverage for that intent is §4B, now delivered: ASSERT A measures the **authored** band
pre-clamp, which is the defect signature rather than its consequence. The runtime ring stays exactly
as it is — it is what turned the owner's device session into this ticket's evidence.

---

## 4. FILE + GATE HYGIENE

- `python tools/gate_brace.py` over all three files: `GATE_BRACE_SUMMARY bad=0 of 3`, exit 0.
- Raw brace parity: HudKitController 412/412, UICaptureLaunch 970/970, HudUiRegression 195/195.
- NUL-byte scan: 0 in all three.
- `git diff --stat`: 3 files, +166 / -33. No `.unity`, no `.asmdef`, no `.json` touched.
- No new `System.Reflection` (part B reuses the existing reflected capture body).
- No commit, no push, no Unity — per the lane brief.

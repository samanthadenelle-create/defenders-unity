# WO-1645 RESULT — the in-raid HUD is now in the capture gate

**Status:** IMPLEMENTED - awaiting WO-1646 for UI_GEOMETRY_OK
**Lane:** RAID-CAPTURE · **Date:** 2026-09-10 · **Verified at head:** `ff42319de6b0a07a59269d74472f3dcc646b6487` (branch `dev`)
**Files changed:** `Assets/Editor/UICaptureLaunch.cs` (+233 / -0). **No runtime file touched.**

---

## 1. What shipped

Two new capture bodies in the harness, registered where the other raid captures are summed:

| lines (in this lane's worktree) | what |
|---|---|
| `:623-627` | registration, immediately after `CaptureRaidDeploy()` at `:622` |
| `:6794-6822` | doc block — why the hole existed, why `RaidTestFlagScope` is not used, why no raid scene loads |
| `:6823-6886` | `CaptureRaidHud` / `CaptureRaidHudOnce` |
| `:6888-6959` | `CaptureRaidDeployHud` / `CaptureRaidDeployHudOnce` |
| `:6961-7020` | `RaidFaceProbe` — the §3c `_settledProbe` read, **log only** |

Each body: `ForEachTarget` over all three `LandscapeTargets`, temp `EventSystem`, real component on a
temp `GameObject`, `InvokePrivate(ctrl, "BuildHud")` (edit mode never runs `Start`), canvas reached by
`GetPrivateGameObject(ctrl, "_ui")`, `RenderCanvasToPng`, canvas-first teardown in `finally`. Private
access is the harness's own existing reflection idiom — **nothing's accessibility was widened** (§5).

The capture adds **no assertion of its own**. It hands two more canvases to the audits already inside
`RenderCanvasToPng`, which is what closes the hole.

## 2. Proof (markers on fresh logs, never exit codes)

- `Builds/wave5-compile2` — `COMPILE_GATE_OK :: scripts compiled clean`
- `Builds/wave5-capture1` (baseline, `0a7edc6b1`) — `UI_CAPTURE_OK 91`, `canvases=91`
- `Builds/wave5-capture2` (**GREEN**, `ff42319de`) — `UI_CAPTURE_OK 97`, `canvases=97 touchPanels=97
  glyphPanels=97 glyphLabels=902` → **+6 on every axis, exactly as required**
- `Builds/wave5-capture3red` (**RED**, pre-1639 controllers restored) —
  `UI_GLYPH_FAIL x4 NEW`, of which three are
  `TEXT TRUNCATED [RaidDeployHud_<aspect>] 'Panel/ObsBtn_Deploy All/Label' ("DEPLOY ALL") draws 6/7 of
  9 printable glyphs ... isTextTruncated=True` at 1920 / 2340 / 2670
- Six PNGs in `Builds/ui-capture/` — `RaidHud_*` and `RaidDeployHud_*` at all three targets, all
  non-blank, **all six opened by this lane**

**The red-first is met.** The glyph oracle has now been watched going **red** on the pre-fix face band
and **green** (`9 of 9`, font 44) on the fixed one, on the same canvas, in two runs 3 minutes apart.
`LayoutOracle.cs:15-20`'s bar — *"an oracle never seen red is not evidence"* — is cleared.

## 3. The one open item

**`UI_GEOMETRY_OK` / `UI_TOUCH_OK` are BLOCKED on WO-1646, and that is the capture succeeding.**
The green run reds `x15 over 97 canvases`, **all 15 on `RaidDeployHud`, 5 per aspect**:
three `SUB-TOUCH-FLOOR BAND` (faces resolve 103.7 / 93.9 / 92.7 ref px against `MinTouchPx` 112) and
two `BUTTON OVER TEXT` (Deploy All + Rally cover the empty-tray sentence). Both are **genuine defects
this ticket's first run discovered** in surfaces that had never been measured. Per §5 they were not
fixed here; they are WO-1646, RAID-HUD lane. Flip this ticket to DONE when WO-1646 lands and a fresh
capture emits `UI_GEOMETRY_OK`.

## 4. Caveats recorded honestly

1. **The six PNGs on disk are the RED run.** All timestamped 07:35 (`wave5-capture3red`); the 07:33
   GREEN run wrote the same filenames and was overwritten. They show `DEPLOY ...` ellipsised — great
   red-first evidence, poor post-fix evidence. Re-run the capture at HEAD to photograph the shipped
   state; no code change needed.
2. **Both panels shoot an empty state, and each logs which one** — the readout is the NO-SCORER state
   (`3:00 / SPIRE 100% / Razed 0% / 0/3 / Troops 0/0`), which is *exactly* the state WO-1639 measured
   on the device; the command bar is the EMPTY-TRAY layout. **A populated tray's tile widths are still
   unproven** — needs a `GameStateService` fixture (WO-1645 §6 residual).
3. **The plate-alpha half of the red-first proves nothing, by construction.** No rule on this path
   measures contrast. Defect A's coverage from this ticket is the PNG for eyes; Defect C (kit toast,
   separate canvas) and Defect D (world-space) are not covered at all.
4. **New finding, §11 of the WO: the readout plate renders OLIVE (89,72,20), and `barImg.color` is not
   what paints it** — `ElarionUiKit.Panel` → `AddInnerRim(p, AccentSoft)` lays a FULL-RECT Gold@0.15
   veil over the whole plate (`ElarionUiKit.cs:150`, `:2670-2683`; `UiStyle.cs:116`; `ElarionUi.cs:58`).
   Linear-space compositing predicts (92,76,18) vs the measured (89,72,20).
   `ObsidianFill = new Color(0.02f, 0.02f, 0.025f, 0.98f)` (`ElarionUiKit.cs:189`) sits *under* that
   veil, so WO-1639's re-tint cannot make the plate near-black on its own. **Not proven:** whether the
   veil draws on device (`AddInnerRim` returns early when `BlinkChromeActive`, `:2672`). Own ticket.

## 5. Residual for a future lane

- Populated-tray shot (needs a `GameStateService` fixture) — §6.
- The kit toast font floor — §7's ready-to-mint text.
- The olive plate veil — §11 above.

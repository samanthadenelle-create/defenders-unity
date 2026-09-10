# WO-1648 RESULT — the Manage hub CLOSE was never dim; it was **covered**

**Status:** IMPLEMENTED - awaiting gate
**Lane:** MANAGE-CHROME, 2026-09-10. Worktree at `736b6b4b90c2c9b6022939b3896414e68e407af2`.
**No Unity run, no gate, no commit from this lane.**

---

## 1. THE ONE-LINE ANSWER

`ManageCategoryLauncher`'s own backing `Image` — `(0.012, 0.014, 0.018, alpha 0.995)` — is drawn
**after** the kit chrome's shared CLOSE and its rect **covered** it. The button was fully built,
fully coloured, fully interactable and completely invisible. **The cure is the rect, not the paint.**

## 2. HOW IT WAS PROVEN (§12: instrument, then read, then fix)

| Evidence | Reading |
|---|---|
| Device hub frame, build 2026.09.10.363660 | CLOSE plate max Rec.709 luma **13/255**, brightest RGB `(13,13,13)` |
| Device queue-drawer frame, same session, same screen box | **254.4/255**, `(255,255,246)` |
| Hub frame card band (this lane's own reference read) | **253.7/255**, `(255,255,237)` |
| `Builds/wave5-manageflow3` (08:44), luma oracle's FIRST run | `UI_LUMA_FAIL x2 over 2 measured CLOSE plate(s)`; `close max=14/255 mean=0.67 rect 1115..1555 x 86..242` vs `reference 'BUILD' max=174.3/255`, `floor=61` |
| `MANAGE_HUB_CLOSE_READBACK[hub]` | `plate color=1,1,1,a1 crColor=1,1,1,a1 **inhAlpha=1** drawn active enabled sibling=6 mat=Default UI Material`; `label color=0.953,0.918,0.827,a1 inhAlpha=1`; `OVERLAPPING: ... ; 'ManageScreenUI/ObsidianPanel/PanelContent/ManageCategoryLauncher' color=0.012,0.014,0.018,a0.995 inhAlpha=1` |
| `MANAGE_HUB_CLOSE_READBACK[drawer-open]` | the same plate reads **`drawn INACTIVE`** |

**`inhAlpha=1` everywhere killed the entire alpha/tint/CanvasGroup family in one field.** The
occluder list then named the graphic. Two candidates went in; one field separated them; one edit came
out. No cycle was spent on a plausible fix.

## 3. WHAT CHANGED

**`Assets/_Modules/Village/UI/Manage/ManageScreenPanel.cs` — the only file with a behaviour change.**
Two paired lines inside `BuildLauncher`:

1. `_launcherHost`'s floor is raised by `Mathf.Max(HubCloseBandPx, _hubCloseReservePx) +
   HubBandGapPx`, so the host's backing plate stops above the CLOSE band.
2. `bottomF` becomes `0f` — the card band is the whole host. The reservation **moved** from the
   grid's fractions into the host's rect; it is neither removed nor applied twice.

⛔ Restoring `(closeReserve + HubBandGapPx) / hostH` while the host inset stands reserves the band
**twice** and hands back the half-height card row WO-1597 measured. The two lines move together, and
`hubCloseBandInsetPx` is named in-code as the hub's **one** close-band reservation so a future reader
grepping either name lands on both.

**Checked, not assumed:** `bottomF` reaches nothing but `grid.anchorMin` (and its degenerate
fallback at `:2355`), and `_launcherGrid` is only ever a parent for cards — no other seat derives a
rect from the grid's floor expecting it to coincide with the close band.

⛔ **If the gate does not go green, do not tune `LumaCloseFloorFraction`.** The oracle takes the PEAK
inside the rect against the frame's own reference glyph, so the Parchment label clears the floor even
if the Gray plate tier is dark by design. A persistent `UI_LUMA_FAIL` means a second occluder or a
different fault — re-read `OVERLAPPING:`, do not loosen the constant.

⛔ **Not** by lowering the launcher's alpha (the hub would go translucent over the town — precisely
what `ManageBodyFill` exists to prevent). ⛔ **Not** by re-ordering siblings: draw order hides the
symptom while a near-black plate still spans a band it does not own, and the next control seated
there would be swallowed identically.

**Also landed (instrumentation, permanent per §12):** `TraceHubCloseReadback` + helpers in the same
file, and the luminance oracle in `Assets/Editor/UICaptureLaunch.cs` (two delimited
`===== WO-1648 LUMINANCE ORACLE =====` blocks; marker `UI_LUMA_ORACLE_OK` / `UI_LUMA_FAIL`, Rec.709
luma, both numbers named in every finding, does not yet gate `MANAGE_FLOW_MAP_OK`).

## 4. THE CORRECTION THIS LANE OWES ITS OWN RECORD

This lane's §1A reframe — *"the bright plate in the drawer frame IS `_chromeClose`"* — is
**FALSIFIED by its own read-back**: `[drawer-open]` reports that control **INACTIVE**, so the bright
pixels are a different control at the same seat. The visual-identity argument (same art, same
placement, an x12 boost matching) was strong and wrong: identical art proves a shared **factory**,
never a shared **instance**. **The read-back outranks the crop.** What survives is what was measured
— the hub plate is dark, device agrees with headless, branch A.

## 5. WHAT IS STILL OPEN

- **Acceptance 4/5 are the lead's:** `COMPILE_GATE_OK` + `REGRESSION_OK <n>/<n>` on FRESH logs, then
  a fresh capture with the **PNGs opened** at 1920x1080, 2340x1080 and 2670x1200. Green =
  `UI_LUMA_ORACLE_OK` where `UI_LUMA_FAIL x2` is today. The marker is necessary, **not sufficient** —
  §5.5 wants human eyes on the frames.
- **Acceptance 6:** `AWAITING OWNER MATCH` once the gate is green; this row cannot reach DONE on
  headless evidence (WO-1566 §2.0).
- **§4 row 1 deliberately not taken** — no measured case added to
  `ManageMockupConformanceRegression.cs`; that suite's header (`:11-17`) says an EditMode suite
  cannot stand a rendered panel up, so the measurement went to the capture path.
  `[chrome-close-is-live]` untouched. `DataRegression.cs` untouched (no new suite).

## 6. THE LESSON WORTH KEEPING

A green source lint sat beside a 13/255 control for two builds, because
`MANAGE_HUB_CLOSE ... "live and legible"` printed what the code **SET**, not what **RENDERED** —
and every one of its assertions was true on the dark frame. **A setter echo is not a read-back.**
The luma oracle and `TraceHubCloseReadback` exist so the next control that goes invisible says so
itself, in numbers, on the first run.

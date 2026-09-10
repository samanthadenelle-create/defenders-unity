# WO-1648: the Manage hub `CLOSE` renders at 12/255 while a green lint calls it "live and legible"

**Status:** READY TO IMPLEMENT
**Silo:** Manage chrome (`Assets/_Modules/Village/UI/Manage/ManageScreenPanel.cs`) + the capture oracle.
**Number:** PRE-ASSIGNED by the lead. ⛔ **Do NOT edit `CLI_LANES_WO_NUMBERS.md`** — the lead owns the
banner bump for this mint.
**Source:** WO-1566 audit, `WORK_ORDER_1566_..._definition_of_done.RESULT.md` §5 finding **F1**
(audit committed `2039e2c41`).

---

## 1. THE MEASUREMENT — this is the whole ticket, and it was taken, not inferred

Measured on the **fresh headless captures written 2026-09-10 07:57** (`Builds/ui-capture/`), luminance
read off the PNG with PIL, threshold-free (raw `max`/`mean` of the greyscale channel):

| Frame | brightest pixel in the bottom 22% | inside the `CLOSE` plate | `BUILD` card label, SAME frame |
|---|---|---|---|
| `ManageWorkspace_1920x1080.png` | **30/255** | max **12/255**, mean **0.8/255** | **172/255** |
| `ManageWorkspace_2340x1080.png` | **30/255** | — | — |
| `ManageWorkspace_2670x1200.png` | **30/255** | max **12/255** | **172/255** |

The 30/255 is the bezel corner, not the control. **The control itself is within 12/255 of black on
every landscape aspect the capture matrix shoots.** Panel 1 of the mockup draws a legible `CLOSE`
beneath the three cards (WO-1566 §2 row 1.2).

## 2. WHY NOTHING CAUGHT IT — the part worth reading twice

`ManageScreenPanel.cs:1362-1375` does all of this, unconditionally, on the hub:

```
_chromeClose.interactable = true;
closeLabel.text  = "CLOSE";
closeLabel.color = ElarionUi.Parchment;
ElarionUiKit.GoldPerimeter((RectTransform)_chromeClose.transform);
FlowTrace.Step("Manage", "MANAGE_HUB_CLOSE the shared CLOSE is live and legible - ... it was never
    disabled, only unreadable");
```

`ManageMockupConformanceRegression`'s `[chrome-close-is-live]` case pins **the presence of that source
text** and is green inside `MANAGE_MOCKUP_OK 10 cases` on `Builds/wave5-reg1` (07:25,
`REGRESSION_OK 494/494 suites`).

> ⛔ **A SOURCE LINT CANNOT SEE LUMINANCE.** The suite says so about itself in its own header
> (`ManageMockupConformanceRegression.cs:11-17`): *"an EditMode suite cannot stand one up headless. The
> PICTURE is judged by the capture loop."* So the code claims the control is legible, the lint confirms
> the code claims it, and **nothing in the chain ever looks at a pixel.** That is the defect this ticket
> closes as much as the dark button is.

## 3. ⚠ WHAT IS **NOT** PROVEN — do not open this as a device regression

**WO-1597** (`WORK_ORDER_1597_manage_hub_heart_chip_ghost_close_and_cards_that_do_not_fill_the_screen.md`)
covers this exact surface and reads `**Status:** CLOSED 2026-09-08 - owner felt-test PASS (validated
2026-09-09T02:10:48, build 2026.09.08.361259)`. **That close was on a DEVICE felt-test; the 12/255 above
is from a HEADLESS batchmode capture.** Both can be true — batchmode can render that control
differently. **No device frame of the Manage hub exists at or after build 363660**; the only Manage
device frame in the repo is `Builds/device-frames/2026-09-10_0033_manage_363195.png`, which is
**PORTRAIT** (out of scope under the owner's 2026-09-10 LANDSCAPE-ONLY ruling) and from build 363195.

**THE DISCRIMINATOR, and it is one command:** a **landscape** `adb screencap` of the Manage hub on a
build **>= 363660**, measured the same way (brightest pixel inside the `CLOSE` plate vs the `BUILD` card
label in the same frame). **Take it FIRST.** It splits the ticket cleanly:
- device also dark → a real render defect, fix the paint;
- device fine, headless dark → a **capture-environment** defect, and the fix is the oracle plus whatever
  the capture path does differently (font atlas, canvas material, a `CanvasGroup.alpha`, a sprite that
  resolves on device and misses headless).

⛔ **Do not "fix" the colour before that frame exists.** Under CLAUDE.md §12 the edit has not been earned
until captured data names the dead step, and there are two live candidate causes here, not one.

## 4. FILES TO EDIT

| File | Change |
|---|---|
| `Assets/Editor/Regression/ManageMockupConformanceRegression.cs` | ADD a **measured** case beside `[chrome-close-is-live]` — see §5. Do not weaken or delete the existing source case; the two defend different things. |
| `Assets/Editor/UICaptureLaunch.cs` | If the oracle must read the captured PNG, the read belongs beside the existing capture reporters (`ReportFidelity` / `ReportGeometry` / `ReportTouchOracle`, `:8735-8742`), emitting its own distinct marker. |
| `Assets/_Modules/Village/UI/Manage/ManageScreenPanel.cs` | ONLY once §3's device frame has named the cause. Touch `:1362-1375` and nothing else on this ticket. |
| `Assets/Editor/Regression/DataRegression.cs` | Registration line only, if a new suite is added. |

## 5. ACCEPTANCE

1. A **device** landscape screencap of the Manage hub at build **>= 363660** is captured, the CLOSE-plate
   and BUILD-label luminances are recorded in the RESULT, and the ticket states which of §3's two
   branches it is. **No code edit lands before this line is filled in.**
2. A **MEASURED** oracle exists that fails when the hub `CLOSE` is unreadable: it reads the actual
   rendered luminance (captured PNG, or a laid-out tree with a real font), compares the CLOSE plate
   against a reference glyph in the same frame, and **names both numbers in its failure text**.
   ⛔ It must be judged in **Rec.709 luma, not hue** — the owner is red/green colourblind and
   `ManageScreenPanel.cs:1360-1361` already states luminance is the channel here.
3. That oracle is proven **RED-first**: it fails on today's 07:57 frames (or on a fixture reproducing
   12/255) and goes green only after the fix. A case that has never failed proves nothing.
4. `COMPILE_GATE_OK` + `REGRESSION_OK <n>/<n> suites` on **fresh** logs, judged by the marker.
5. A fresh capture is taken and **the PNGs are opened**; the CLOSE plate reads legibly at all three
   landscape aspects (1920x1080, 2340x1080, 2670x1200).
6. Per WO-1566 §2.0 this row cannot reach DONE on headless evidence: `**Status:**` goes to
   `AWAITING OWNER MATCH` until the owner judges a device frame.

## 6. WHAT NOT TO TOUCH

- ⛔ **The top-right constant `X`.** `CheckConstantExit` and `[chrome-close-on-hub-only]` defend **two
  different controls with two different fields** and the suite says in `:59-67` that anyone who
  "unifies" them re-opens one of two defects. `_chromeClose` is hub-only; the `X` is on every screen.
- ⛔ The `[chrome-close-is-live]` source case — add beside it, never replace it.
- ⛔ Anything in `ManageWorkspacePanel.cs`. `ManageDumbViewRegression` pins that the renderer holds none
  of canon 9's 16 forbidden shapes and is not a MonoBehaviour.
- ⛔ The hub card layout, the HEART chip, and the card art wells — all green
  (`[hub-cards-fill-the-well]`, `[hub-heart-chip-verb]`, `[hub-art-standins-exist]`).
- ⛔ Do not reopen or edit WO-1597. It closed on its own evidence; this is a new frame.

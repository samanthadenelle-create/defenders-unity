# WORK ORDER 1687 — RESULT (pass 3; supersedes passes 1 and 2)

> ## ⭐ OWNER RULING 2026-09-10 13:32 — **SENTENCE FIRST; drop the lines that do not fit.**
> Pass 2 raised this as an open trade. **It is ruled and no longer "flagged, not decided."** The
> away-window sentence outranks every flowing line; anything that will not fit is dropped and traced,
> never allowed to squeeze it. That is the rule the file now implements.

---

## 0B. ⛔ PASS 2 BROKE THE COMPILE. Mine, and it hid a second defect.

`Builds/wave10-compile1`:

```
Assets\_Modules\Village\Harvest\UI\WelcomeBackPopup.cs(571,33): error CS7036: There is no argument
given that corresponds to the required formal parameter 'floor' of
'WelcomeBackPopup.AddMendLine(Transform, ref float, string, Color, float)'
```

Pass 2 added `floor` to `AddMendLine` and updated **three of its four** call sites. The fourth is
`AddDestinyFooter`'s silo-stalled line. I swept the mend rows and never grepped the method's own name —
the same one-command check I would demand of a lane.

**The miss was more than a compile error.** That call was gated by `HasRoom(y)`, which compared against
`MinRowY` (0.06), **not** the reserved floor — and so did **five** others: job rows, "ALSO FINISHED",
collector rows, "ALSO WAITING", and the table footer. Pass 2 taught the three loudest consumers to
respect the reservation and left six free to draw straight through it. **Fixing callers one at a time
is how a reservation becomes a suggestion.**

### Pass 3 — the rule moved into the CHECK

| location | change |
|---|---|
| `:227` | **new `HasRoomFor(y, h)`** — measures against **`RowStackFloor`**, not `MinRowY` |
| `:229`, `:232` | `HasRoom` / `HasDoorRoom` re-expressed via it; all three became **instance** members (the floor depends on `_result.WasCapped`) |
| `:600` | the silo-stalled line passes `RowStackFloor` and traces its skip — **the CS7036 fix** |
| `:614` | the footer now asks `HasRoomFor(y, FooterSentenceH)` |
| `:180` | **new `FooterSentenceH = 0.19f`**, read by both the check and `AddFooterSentence` |

⚠ **A latent bug found while fixing the compile:** the table footer asked `HasRoom(y)` — which reserves
`RowH` **0.095** — then called `AddFooterSentence`, which consumes **0.19**. It could clear its own gate
and overrun by a full row, landing on the reserved band. Check and consumption now read one constant.

**Staticness verified, not assumed:** all eight room-check call sites were already inside instance
methods (`AddCompletedJobRows`, `AddCollectorRow`, `AddDestinyFooter`, `AddDoorRows`) — enumerated
method-by-method — so the static→instance move breaks nothing else. All four `AddMendLine` calls now
pass five arguments.

**Post-pass-3 trace, worst-case fixture:** floor `0.282`; resource rows leave `y = 0.392`; `mended`
draws (`y = 0.292`); `spent`, `stalled`, the silo-stalled line and the footer sentence all **skip**,
each `FlowTrace.Warn`ed with `y` and the floor. The sentence keeps **110.5 ref px ≈ 3.6 lines**.

`gate_brace.py` → **`GATE_BRACE_SUMMARY bad=0 of 1`**; NUL scan → **0**.

---

# (passes 1–2, retained)

**Lane:** touch-floor / layout seat, 2026-09-10. Worktree at `dev` **`e4b5906a5`**.
**No Unity run, nothing committed.**

---

## 0. ⛔ Pass 1 was wrong, and this section is the correction

I raised `CappedSentenceH` from 0.12 to 0.21 and reported that the band would grow from 56 to 99 ref
px. **It grew by zero pixels.** `Builds/wave9-welcome1` reprinted the identical line with a
byte-identical rect, and it was right to.

**Why:** the sentence was seated at `Mathf.Max(0.03f, y - CappedSentenceH) .. y`. In the worst-case
fixture the flowing stack reaches it at **`y = 0.092`**, so the `Mathf.Max` clamps to `0.03` for
**every** H above 0.062. The band was `0.092 - 0.03 = 0.062` of body either way.

**The band was never height-limited. It was leftover-limited.** I named this clamp as a residual risk
in the pass-1 RESULT (§6.3) and then asserted the band would grow regardless — the one line of
arithmetic against the fixture that would have caught it is the line I did not run. Recorded here, not
quietly fixed.

---

## 1. The RCA, now arithmetic-exact

Traced through `CaptureWelcomeBackOnce` (4 non-zero resources, no doors, 3-line mend, `WasCapped`):

```
y = 0.82
  - 4 resource rows x (0.095 + 0.012)  ->  y = 0.3920
  - 0 door rows (no PostureSignals)    ->  y = 0.3920
  - 3 mend lines   x (0.09  + 0.01 )   ->  y = 0.0920
band = 0.092 - max(0.03, 0.092 - H) = 0.062 of body,  for H = 0.12 AND H = 0.21
```

At 1920x1080: body = `(0.82 - 0.24) x 0.84 x 1080 = 526.2`; `0.062 x 526.2 =` **32.6 ref px**.
The oracle measured `y -220.1..-187.5` = **32.6**. Predicted and measured agree to the decimal, at
both H values — which is the proof that pass 1 could not have worked.

**The real finding is oversubscription:** demand `0.428 + 0.300 + 0.210 = 0.938` against
`0.82 - 0.06 = 0.760` available. **Short by 0.178.** Something must yield; a constant cannot decide
which.

---

## 2. Pass 2 — the fix

| location | change |
|---|---|
| `:174-201` | **new `RowStackFloor`** = `MinRowY + (WasCapped ? CappedSentenceH + RowGap : 0)` |
| `:304` | sentence seated at the **absolute** band `MinRowY .. MinRowY + CappedSentenceH` — no longer `y`-derived |
| `AddMendLine` | takes the floor, **returns bool**, refuses to draw through it |
| `AddMendRows` | counts skips and `FlowTrace.Warn`s them |

Post-fix trace, same fixture: floor `0.282`; resource rows leave `y = 0.392`; **one** mend line draws;
two are skipped and warned; the sentence gets `0.21 x 526.2 =` **110.5 ref px** ≈ **3.6 lines** at the
26 px floor, against 32.6 px / one line. The 72-glyph sentence fits.

⛔ **The trade is explicit and reversible.** A sentence cut mid-word tells the player something
**false**; a skipped mend line is an **omission** the Echoes panel still holds. `AddDoorRows` already
uses skip-and-Warn for "no room", so this follows precedent rather than inventing a rule. **If the
owner prefers all three mend lines and a shorter sentence, that is her call** — flagged in the WO, not
decided here.

**Still not the font, still not the copy:** autosize floor stays 26 (`FontHardFloor` is 20); the
sentence is unchanged.

---

## 3. RED

```
TEXT TRUNCATED [WelcomeBack_1920x1080] 'ObsidianPanel/PanelContent/Zone_Body/Label'
  ("Your realm gathers for a limited stretch whil...") draws 60 of 72 ... (x -481.2..481.2, y -220.1..-187.5)
```
Reproduced on `Builds/wave9-welcome1` **with pass 1 already merged** — which is what identified the
clamp. Producer confirmed by glyph count: the sentence has **exactly 72 non-space characters**.

GREEN is owed (§5).

---

## 4. Diff and gate

```
Assets/_Modules/Village/Harvest/UI/WelcomeBackPopup.cs
  +:176-201  RowStackFloor + the doc block (the y=0.092 trace, the 32.6 px identity,
             the oversubscription table, and why the mend lines yield)
  +:201      private float RowStackFloor => MinRowY + (... ? CappedSentenceH + RowGap : 0f);
   :304      Mathf.Max(0.03f, y - CappedSentenceH), y   ->   MinRowY, MinRowY + CappedSentenceH
   :350-370  AddMendRows: reads RowStackFloor, counts skips, FlowTrace.Warn
   :373-388  AddMendLine: + float floor param, returns bool, early-outs above the floor
```

`python tools/gate_brace.py` → **`GATE_BRACE_SUMMARY bad=0 of 1`**. NUL scan → **0**.
`FlowTrace` resolves via the file's existing `using DeNelle.Core.Diagnostics;` (`:5`) and is already
used in this file.

---

## 5. What is NOT proven

1. **No Unity run.** `UI_GLYPH_OK` with this finding gone, `WELCOME_BACK_CAPTURE_OK 6/6; touch=clean`,
   `COMPILE_GATE_OK`, `REGRESSION_OK` — all owed on fresh logs, judged by marker.
2. **Frames not opened.** Three aspects; check the sentence reads whole and clears the ready band.
3. ⚠ **The `WelcomeBackDoors` fixture was not traced.** It carries door rows (`DoorRowH 0.245` each),
   so its stack is *shorter* on rows but *heavier* per row; the reserved band protects the sentence
   either way, but which mend lines survive there is unmeasured.
4. ⚠ **Two mend lines will now be missing from that capture.** That is intended, warned in the log, and
   the owner may reverse it.
5. **Nothing committed.**

## 6. Files touched

```
Assets/_Modules/Village/Harvest/UI/WelcomeBackPopup.cs
WorkOrders/WORK_ORDER_1687_...cuts_at_every_aspect.md         (Status: IMPLEMENTED (PASS 2), + section 3B)
WorkOrders/WORK_ORDER_1687_...cuts_at_every_aspect.RESULT.md  (this file)
```

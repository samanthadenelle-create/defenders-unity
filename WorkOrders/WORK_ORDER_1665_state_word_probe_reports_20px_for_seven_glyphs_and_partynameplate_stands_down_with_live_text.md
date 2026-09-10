# WORK ORDER 1665 — The state-word probe reports 20px for seven glyphs, and PartyNameplate stands down carrying live text

**Status:** READY TO IMPLEMENT
**Silo:** UI instrumentation truthfulness (`ManageWorkspacePanel` probe + `ElarionUiKitObsidian` fit guard). **No layout change is being asked for — both surfaces render CORRECTLY today.**
**Raised by:** DEVICE-FRAMES-4 lane, 2026-09-10, from a live Seeker capture.
**Number:** pre-assigned by the coordinator. `CLI_LANES_WO_NUMBERS.md` deliberately NOT edited by this lane.

---

## 0. Read this first — what this ticket is and is not

Both defects here are **instruments reporting numbers and identities that cannot be true**. In both
cases the pixels the player sees are RIGHT. That is precisely why they are worth a ticket: WO-1661
was diagnosed *from these very lines*, and §11B forbids reasoning from a measurement that does not
measure what it claims. An instrument that is confidently wrong is more expensive than one that is
silent, because the next seat will act on it.

**Do not "fix" either symptom by changing what the screen looks like.**

Source of truth for every quote below: `Builds/device-frames/2026-09-10_1137_363866_logcat.txt`,
APK **2026.09.10.363866** (`dumpsys`: `versionCode=363866 versionName=2026.09.10.363866`), Seeker
`SM02G4061955851`, app PID **10705** (`pidof`), 2670x1200 landscape.

---

## PART A — `already-fits` reports `wants 20px` for a seven-glyph word at 26px bold

### A1. The measured lines

`grep -i "state word"` returns exactly **four** lines — two full passes, identical:

```
09-10 11:36:27.907 I Unity : [Flow:Manage] state word font: resolving for widest='UPGRADE' (7 chars) at cellW=565.87
09-10 11:36:27.907 I Unity : [Flow:Manage] state word font: early return [already-fits] - widest='UPGRADE' wants 20px at 26px type and the plate offers 407px (cellW=565.87, slack 387px), so it paints at the ceiling 26px
09-10 11:36:44.744 I Unity : [Flow:Manage] state word font: resolving for widest='UPGRADE' (7 chars) at cellW=565.87
09-10 11:36:44.744 I Unity : [Flow:Manage] state word font: early return [already-fits] - widest='UPGRADE' wants 20px at 26px type and the plate offers 407px (cellW=565.87, slack 387px), so it paints at the ceiling 26px
```

The other three branches are **zero**: `no-plate-width` = 0, `probe-measured-nothing` = 0,
`no-word-or-no-cell` = 0.

### A2. Why the number cannot be true

`'UPGRADE'` is seven capital glyphs. At **26px bold**, `wantPx = 20` is **~2.9 px per glyph** —
narrower than the type is tall by a factor of nine. The rendered badge in
`Builds/device-frames/2026-09-10_1137_363866_army_badge_crop.png` shows "UPGRADE" occupying a visibly
substantial fraction of its plate, nothing like 20/407 (5%).

**The outcome is nonetheless correct.** `wantPx <= availablePx` is true either way, the resolver
returns the ceiling 26px, and the tile paints "UPGRADE" whole — WO-1661's acceptance is met and this
ticket does not reopen it.

### A3. WO-1661's own leading candidate is REFUTED by this log — record that

`ManageWorkspacePanel.cs:1136-1139` names the ticket's prime suspect:

> `⚠ THE TICKET'S OWN LEADING CANDIDATE (WO-1661 section 9): BuildTile's parameter doc warns the cell
> rect is 0 on the frame the tile is built, so a cellW of 0 arriving here would return the authored
> ceiling for EVERY grid ...`

The **entry line** added by WO-1661 (`:1132-1134`) is what settles it, and it is the reason that line
exists. It printed `at cellW=565.87` — a real, non-zero cell. So the `no-word-or-no-cell` theory is
dead, `availablePx=407` is a genuine measurement of a genuine plate, and the anomaly is isolated to
**`wantPx` alone**.

### A4. The two hypotheses

The measurement happens at `ManageWorkspacePanel.cs:1161-1173`:

```
var probe = new GameObject("StateWordProbe", typeof(RectTransform), typeof(TextMeshProUGUI));
...
var face = ElarionUiKit.ResolveDefaultFont();
if (face != null) text.font = face;
text.fontSize = Ceiling;
text.fontStyle = FontStyles.Bold;
text.enableAutoSizing = false;
float wantPx = text.GetPreferredValues(widest, 0f, 0f).x;
```

**Hypothesis A1 — the probe is measured detached, so TMP has no laid-out context.**
The probe GameObject is created with `new GameObject(...)` and **never parented to anything** — no
`SetParent`, no Canvas, no CanvasScaler above it. `GetPreferredValues` on a `TextMeshProUGUI` with no
canvas ancestor can return a value computed in unscaled/uninitialised units rather than the reference
px the caller compares against. `availablePx` is derived from `cellW`, which IS in reference px — so
the comparison at `:1186` would be **mixing two unit systems**, which is exactly the shape of a
20-vs-407 result.

**Hypothesis A2 — the font never actually resolved, and `wantPx` is a fallback metric.**
`ResolveDefaultFont()` may return non-null but hand back an asset whose atlas/face info is not loaded
at this point, yielding a degenerate width. The `probe-measured-nothing` branch (`:1174-1184`) only
catches `wantPx <= 1f`, so a small-but-nonzero degenerate value slips straight into `already-fits`.
Note the guard's own message already prints the face name for this reason — but only on the branch
that did not fire.

⚠ A third possibility worth excluding cheaply: `GetPreferredValues` is called with **`widest`**, not
the sanitised local `widestWord` (`:1131`). On this run they are equal, so it is not the cause here,
but it is a latent divergence in the same expression.

### A5. The discriminating read-back — do this BEFORE any edit (§12)

**Do not choose between A1 and A2 by reading code.** One instrumented run separates them:

1. In the `already-fits` branch, additionally print: the resolved **face name** (`text.font != null ?
   text.font.name : "<null>"` — the string the `probe-measured-nothing` branch already builds at
   `:1180-1181`), the probe's **canvas ancestry** (`probe.transform.parent == null`), and the
   **unscaled vs scaled** comparison — e.g. `GetPreferredValues` result alongside `text.fontSize` and
   the value re-measured after parenting the probe under the real tile's canvas.
2. Re-run the ARMY grid on device and read the line.
   - Face resolves to a real name **and** the re-measured-under-canvas width is materially larger than 20
     → **A1** (detached measurement). Fix: parent the probe under the same canvas the tile lives on for
     the duration of the measurement, then destroy it.
   - Face is `<null>` or a fallback name, and parenting changes nothing → **A2** (font not resolved).
     Fix: resolve/await the face, and widen the degenerate-value guard at `:1174` from `<= 1f` to a
     plausibility floor derived from glyph count.

Either way the **return value must not change** in this ticket — see §C.

---

## PART B — `PartyNameplate/Label` warns with live text, then stands down as EMPTY on the same path

### B1. The measured sequence

Two lines, 41 seconds apart, **identical path string**:

```
09-10 11:34:14.783 W Unity : [Flow:UI] TextFitGuard 'Train 2 troops for the next raid' [Area_HeartStatus/Widget_heartStatus/HeartStatus/PartyNameplate/Label]: band too short to seat FontHardFloor line — grew rect 23px -> 26px (minBand 26, lineFactor 1.15)
09-10 11:34:55.626 W Unity : [Flow:UI] TextFitGuard [Area_HeartStatus/Widget_heartStatus/HeartStatus/PartyNameplate/Label]: armed but text EMPTY after 600 frames — standing down; whether that is empty-BY-DESIGN or never-set is the PRODUCER's call, not the fit guard's ...
```

The first carries **live text**, and `Builds/device-frames/2026-09-10_1134_363866_town.png` (captured
at 11:34, between the two lines) **shows that exact sentence on screen** — "Train 2 troops for the
next raid", under "Heart of Elarion". So the label was populated and visible, and 41 s later the same
path reported itself empty.

### B2. The hypotheses — and one is already all but proven from source

**Hypothesis B1 (strongly supported): the path string is AMBIGUOUS. Four different GameObjects share it.**

`ElarionUiKit.Label(...)` names **every** label it creates the literal string `"Label"`:
```
var go = new GameObject("Label", typeof(TextMeshProUGUI));
```
(`Assets/_Modules/Core/UI/ElarionUiKit.cs:1909`, inside `public static TextMeshProUGUI Label(...)`.)

`BuildPartyNameplate` builds its NameLabel through that same helper, parented to the plate root
(`Assets/_Modules/Core/UI/ElarionUiKitNameplate.cs:140`). And `HudKitController` then parents **three
more** labels to the *same* `_heartPlate.Root.transform`, each via the same helper:

- `_heartObjectiveLabel` — `Assets/_Modules/HUD/Kit/HudKitController.cs:2528`
- `_heartfireLabel` — `:2558`
- `_heartfireRekindleLabel` — `:2564`

That is **four sibling GameObjects all named `Label` under one `PartyNameplate` root**, so all four
render the identical path `.../PartyNameplate/Label`. The 11:34:14 warn is the objective row (its text
matches `_heartObjectiveLabel`'s content, painted by `RepaintHeartObjective` per the comment at
`:2521-2527`); the 11:34:55 stand-down is a **different sibling** that is legitimately blank —
**all three** of the HudKitController siblings are **created with `string.Empty`** and filled later,
if at all — verified at source 2026-09-10: `:2529` (objective), `:2559` (heartfire), `:2565`
(heartfire rekindle).

Under B1 **there is no bug in the guard and no bug in the producer.** The defect is that the
diagnostic cannot name which of four objects it is talking about — which is what made this sequence
look like a contradiction to a reader, and cost this lane a flagged anomaly.

**Hypothesis B2 (must still be excluded): the objective label really cleared.**
`RepaintHeartObjective` could legitimately blank the row when the objective resolves (e.g. the train
count reaches 0), in which case one object really did go from live text to empty and the guard is
reporting a real transition — correctly, and harmlessly, since `FitGuardStandDownMessage`'s empty half
deliberately **no longer asserts a bug** (`ElarionUiKitObsidian.cs:3143-3164`, the WO-1656 change).

### B3. The discriminating read-back

Make the guard's path **disambiguating**, then re-run:

1. In the fit guard's path builder, append a stable discriminator — the sibling index, or better the
   `TMP_Text`'s role/owner. The cheapest correct fix is upstream: give the four labels distinct
   GameObject names at their construction sites (`HudKitController.cs:2528/2558/2564` and the
   nameplate's own at `ElarionUiKitNameplate.cs:140`), e.g. `Label_HeartObjective`,
   `Label_Heartfire`, `Label_HeartfireRekindle`, `Label_Name`. Nothing reads these names for behaviour
   — **verify that claim by grep before relying on it.**
2. Re-run the same walk and read the two lines again.
   - The warn and the stand-down now carry **different** paths → **B1 confirmed**, and the ticket
     closes on the rename alone.
   - They still carry the **same** path → **B2**: one object genuinely cleared. Then instrument
     `RepaintHeartObjective` to say when and why it writes `string.Empty`, and confirm the stand-down
     is correct-by-design. Still no behaviour change.

⚠ Note the guard's path builder truncates to a few ancestors (the sibling routine
`RecordClampGrowth` walks 4 parents, `ElarionUiKit.cs:1131-1141`); confirm the fit guard's own path
depth before assuming a rename is enough to disambiguate.

---

## C. What NOT to touch

- ⛔ **The stand-down branch's empty half** — `FitGuardStandDownMessage`
  (`Assets/_Modules/Core/UI/ElarionUiKitObsidian.cs:3166-3178`). Its wording was deliberately changed
  by WO-1656 to REPORT state rather than assert a bug, on proof from two device logs that it had
  accused two correctly-empty labels. Its own doc comment (`:3143-3164`) explains this at length and
  warns: *"A warning that cries bug on healthy state trains every reader to ignore it."*
  **Do not re-add a verdict, and do not silence the branch** — `:3160-3164` states that narrowing the
  message must never become silencing it (§12: instrumentation is permanent).
- ⛔ **The ZERO-HEIGHT half of the same method** (`:3174-3177`) — it still asserts, deliberately.
- ⛔ **`FontHardFloor`** (`ElarionUiKitObsidian.cs:3044`, `20f`) and the clamps that read it at
  `:3062` / `:3083`; likewise `ElarionUi.FontFloorMobile` (`ElarionUi.cs:123`, `30f`) and the
  reasoning at `ElarionUi.cs:120-122`. This ticket is about a probe and a path string, not about
  legibility floors. Changing a floor to make a log line look sensible is the inversion of the fix.
- ⛔ **The return values of `ResolveStateWordFont`.** `ManageWorkspacePanel.cs:1123-1125` states the
  standing rule for this method: *"THESE ARE REPORTS, NOT BEHAVIOUR. Every return value below is
  byte-for-byte the value it was before; adding a Step is not a layout change."* Honour it — Part A
  may correct `wantPx` **only** if the resulting branch and return are proven unchanged for
  `'UPGRADE'`, and the RESULT must show both the old and new `wantPx` against `availablePx=407`.
- ⛔ **The WO-1661 badge outcome.** "UPGRADE" reads whole today, proven in
  `2026-09-10_1137_363866_army_badge_crop.png` against the truncated `_1018_363786_manage_army.png`.
  No change may regress it.
- ⛔ **The WO-1661 entry line** at `ManageWorkspacePanel.cs:1132-1134`. It is the line that refuted
  the ticket's own leading candidate (§A3); it is load-bearing evidence, not noise.
- Do not convert the 3-arg `FlowTrace.Step` calls here to the 4-arg `Measure` form. A screen-open path
  is not a hot loop and `:1125-1127` says so.

---

## D. Acceptance

1. **Part A:** a FRESH device logcat showing the widened `already-fits` line, with the face name and
   the parented/unparented measurement both printed, and a one-line verdict in the RESULT naming
   **A1 or A2** with the number that decided it. If the probe is corrected, the RESULT quotes old
   `wantPx=20` and new `wantPx=<n>` against `availablePx=407` and shows the branch is still
   `already-fits`.
2. **Part B:** a FRESH device logcat in which the fit-guard warn and the stand-down carry **distinct**
   paths (B1), or an instrumented `RepaintHeartObjective` line proving the real clear (B2).
3. `TextFitGuard CENSUS ... relaxed=0` still holds on the fresh log. Today's reads
   `armCalls=286 armed=286 declinedNotPlaying=0 declinedNullText=0 evaluated=66 relaxed=0 stillBlank=0`
   (11:36:44.730).
4. Frames opened: the ARMY grid badge still reads "UPGRADE" whole; the heart plate still reads
   "Train N troops for the next raid".
5. `**Status:**` flipped in this file in the same commit as the work, `.RESULT.md` written, both paths
   reported.

## E. Evidence index

- `Builds/device-frames/2026-09-10_1137_363866_logcat.txt` — all four `state word` lines, both fit-guard lines, the census
- `Builds/device-frames/2026-09-10_1137_363866_army_badge_crop.png` — "UPGRADE" whole (363866)
- `Builds/device-frames/2026-09-10_1018_363786_manage_army.png` — "UPGRADE A…" truncated (363786), the before
- `Builds/device-frames/2026-09-10_1134_363866_town.png` — the heart plate carrying "Train 2 troops for the next raid" at 11:34, between the two fit-guard lines

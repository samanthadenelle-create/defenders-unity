# WO-1644 - The FRONT-DOOR capture measures Title + Login through the glyph oracle and reports NO marker

**Status:** FIXED - 2026-09-10. Proven by the marker on a FRESH log, `Builds/wave5-frontdoor1`
(07:26): `UI_GLYPH_OK 6/6 panels labels=18 baselined=0 unproved=0` followed by
`FRONT_DOOR_CAPTURE_OK 6/6`. Zero findings, zero baseline entries added. RESULT:
`WorkOrders/WORK_ORDER_1644_front_door_capture_never_reports_the_glyph_oracle.RESULT.md`
**Minted:** 2026-09-10 by the TITLE-CAPTION lane. **Number PRE-ASSIGNED by the lead** - this lane did
NOT edit `CLI_LANES_WO_NUMBERS.md`; the lead bumps its own banner row (CLAUDE.md sec.2).
**Silo / Lane:** Editor / headless UI capture oracles (`Assets/Editor/UICaptureLaunch.cs`)
**Severity:** P2 - not a player-facing defect. It is a **blind spot in the gate that is supposed to
catch player-facing defects**, on the FIRST TWO SCREENS OF THE GAME.
**Type:** EXISTING system. The oracle runs; nothing prints its verdict.
**Found by:** WO-1621 (`WORK_ORDER_1621_title_play_intro_caption_truncates_on_the_seeker.RESULT.md`
sec.3), while proving the Title caption fits in landscape. Every line below was opened at source
2026-09-10 on dev HEAD `f33451b11`.

---

## 1. The defect, source-proven

### 1a. Title and Login ARE measured by the glyph oracle

- `Assets/Editor/UICaptureLaunch.cs:2304-2330` - `CaptureTitleOnce` builds the real
  `DeNelle.Onboarding.TitleController` by reflection, invokes `BuildTitleMenu`, and renders through
  `RenderCanvasToPng`.
- `:2334` - `CaptureLoginOnce` does the same for the login gate.
- `RenderCanvasToPng` runs the oracle unconditionally at **`:5775`**:
  `AuditGeometry(canvasGo, Path.GetFileNameWithoutExtension(path), w, h)` - on the SETTLED layout,
  after the two forced rebuild passes at `:5761-5771`.
- `AuditGeometry` (`:5893`) accumulates Assert C's glyph findings into
  `_glyphFailures` / `_glyphPanelTally` / `_glyphUnproved` and the counters at **`:6272-6280`**
  (`:6024-6052`).
- Both captures run at all three **LANDSCAPE** targets (`LandscapeTargets`, `:215-220`), including
  `2670x1200`, commented in that array as *"THE SEEKER'S REAL SURFACE"*. So the panel builds
  `Title_1920x1080 / Title_2340x1080 / Title_2670x1200` and their three `Login_*` siblings are
  measured on every front-door run: **six panel builds' worth of findings, computed and thrown away.**

### 1b. Their ONLY caller never resets and never reports

`RunFrontDoorCaptureHeadless` (**`:2215-2223`**) is the sole caller of both capture bodies
(`grep -n "CaptureTitleOnce\|CaptureLoginOnce"` -> `:2219`, `:2220`, `:2263`; `:2263` is the
GooglePlay variant, which re-uses `CaptureLoginOnce` only). Its whole body is:

```
Directory.CreateDirectory(OutDir);
_loginCaptureStem = "Login";
int count = ForEachTarget("Title", CaptureTitleOnce) + ForEachTarget("Login", CaptureLoginOnce);
if (count == 6) Debug.Log("FRONT_DOOR_CAPTURE_OK 6/6");
else Debug.LogError("FRONT_DOOR_CAPTURE_FAIL " + count + "/6");
```

**It calls neither `ResetGlyphOracle()` nor `ReportGlyphOracle()`.** Contrast its immediate neighbour,
`RunGooglePlayLoginCaptureHeadless`, which calls both - `ResetGlyphOracle()` at **`:2243`** and
`ReportGlyphOracle()` at **`:2274`** (line numbers from
`grep -n "ResetGlyphOracle()\|ReportGlyphOracle()" Assets/Editor/UICaptureLaunch.cs`, which lists
**28** call sites across the file, counted exactly -
`grep -c` returns 30 and two of those are the method DEFINITIONS at `:6288` and `:6301`. The call
lines are `561 644 668 690 699 1599 1656 1664 2243 2274 2770 2777 2867 2875 7053 7060 7173 7195
7458 7464 7483 7489 7564 7600 7658 7667 8463 8506`. **The front door is the odd one out.**)

Consequence: `UI_GLYPH_OK` / `UI_GLYPH_FAIL` is **NEVER EMITTED** by the front-door run. The file's
own header rule (`:38-43`) and the comment at the GooglePlay emit site say the marker is wired at
*"EVERY site that emits the touch marker: one path missing it prints marker-absent there, read here
as a FAILURE not an unknown."* This path is that missing site.

### 1c. Why it matters: absence from `GlyphBaseline` reads as "clean" and is not

`GlyphBaseline` (`:6160-6172`) holds **4** entries after the 2026-09-10 68 -> 4 shrink. **No
`Title_*` or `Login_*` entry is among them - because those panels have never been measured in a
REPORTING run, not because they measured clean.** Corroborated on disk 2026-09-10:

- `tr -d '\000' < Builds/wave3-capture10 | grep -ac 'Title_'` -> **0**. That is the very run the
  shrink was proven on (header, `:6137-6140`): 91 panel builds, 875 labels, 4 findings, all baselined.
  **Not one of those 91 was a front-door panel.**
- `tr -d '\000' < Builds/wave4-capture3 | grep -a 'Title_'` -> no panel line either.

This is exactly the failure `ReportGlyphOracle` itself was written to prevent, in its own words at
`:6310-6326`: a run with panels visited and no labels measured *"is byte-identical, to a grep, to a
run in which everything fit"*. The front door is one step worse - it does not even print the
distinguishing FAIL.

### 1d. NOT PROVEN, and it must stay that way until the wiring runs

- ⛔ **Whether the front-door panels are actually clean.** Nobody knows. That is the ticket. Do not
  write "Title is clean" anywhere until a marker says so.
- ⛔ **Whether the sibling oracles leak between entry points.** `RunFrontDoorCaptureHeadless` also
  calls no `ProveGeometryMoves()` / `ReportFidelity()` / `ReportGeometry()` / `ReportTouchOracle()`
  (compare `:2246`, `:2271-2273`). Since every reporting entry point RESETS at its own start, residue
  is bounded in principle - **but this lane did not run anything and does not assert it.** See sec.5.

## 2. Target - what "fixed" means

A front-door capture run emits a real glyph verdict for the game's first two screens, and the
baseline honestly reflects what it found.

## 3. The fix

1. Wire the pair into `RunFrontDoorCaptureHeadless` (`:2215-2223`), copying the **shape** of the
   GooglePlay path (`:2243` reset before the captures; `:2274` report after them), NOT its extra
   presentation-override machinery.
2. **RUN IT** (`RunFrontDoorCaptureHeadless`, headless) and read the marker on a FRESH log. CLAUDE.md
   sec.11B: marker, not exit code; absence on a fresh log is a FAILURE, not an unknown.
3. **Then triage whatever it reds, per finding:**
   - A finding that is a genuine caption cut -> **fix the panel**, do not baseline it. The Title row's
     landscape captions are already proven to draw in full on the device
     (WO-1621 RESULT sec.1), so a Title finding at `2670x1200` would be a contradiction worth
     understanding before either side is trusted.
   - A finding that is known, accepted debt -> add it to `GlyphBaseline` **in the entry format the
     file already enforces**: `panelBuild|hierarchy/path|<drawn> of <printable>`, matched on all
     three fields by `IsGlyphBaselined` (`:6180-6200`), with the string + font + overflow mode in a
     trailing comment and this WO's number named, exactly as the four existing entries do.
   - `UI_GLYPH_FAIL x0 labels` (`:6310`) -> a font failed to resolve in batchmode, or Assert C's
     predicate excludes every front-door control. **Different bug, different fix** - the branch says
     so itself; report it, do not baseline it away.

## 4. Acceptance criteria

- [x] `RunFrontDoorCaptureHeadless` calls `ResetGlyphOracle()` before the captures and
      `ReportGlyphOracle()` after them.
- [x] A **fresh** front-door log carries a `UI_GLYPH_OK` line whose `<clean>/<checked>` counts
      **6 panel builds** (3 Title + 3 Login targets) and whose `labels=` field is **> 0**.
      *(`<clean>/<checked>` counts PANEL BUILDS, not distinct panels - the OK line says so itself at
      `:6340-6347`.)*
- [x] ⚠ **THE LEAD'S PHRASING NEEDS ONE CORRECTION, AND IT IS DELIBERATE:** the brief asked for the
      marker *"with the Title row named"*. **On a CLEAN run no panel is named** - the per-panel tally
      lines (`:6332-6333`) are printed only for panels that HAVE findings, and a baselined finding is
      deliberately never printed. So "Title named" is achievable only by a run that reds. The
      provable acceptance is the count above (6 panel builds, labels > 0), plus the `Title_*` PNGs
      existing in `OutDir` for the same run. If the run does red on Title, the tally line naming it
      goes in the RESULT verbatim.
- [x] The number in `panels=` is stated in the RESULT **from the log**, never from this document.
- [x] If nothing reds: say so with the marker quoted, and record that `Title_*` / `Login_*` are now
      MEASURED-clean rather than unmeasured - the distinction this whole ticket is about.

## 5. Files to edit

- `Assets/Editor/UICaptureLaunch.cs` - `RunFrontDoorCaptureHeadless` only, plus `GlyphBaseline`
  additions IF and ONLY IF a run produces findings judged to be accepted debt.
- The WO + its `.RESULT.md`.

**Raise, do not take:** whether the front door should ALSO gain `ProveGeometryMoves` /
`ReportFidelity` / `ReportGeometry` / `ReportTouchOracle` (sec.1d). It is the same omission in four
more voices and probably the same one-line-each fix, but each of those markers feeds the pre-ship
gate ladder (CLAUDE.md sec.8) and turning four more oracles on for a never-reported path is a
**lead-ruled scope decision**, not this lane's. Name it in the hand-back with these cites.

## 6. What NOT to touch

- ⛔ **The four existing `GlyphBaseline` entries (`:6160-6172`).** They belong to
  `EndStateWaveClear_repairAll_1920x1080`, `RealmWorkspace_1920x1080`, `ManageWorkspace_2340x1080`
  and `ManageWorkspace_2670x1200`, and every one is a measurement (header, `:6137-6157`). Do not
  edit, reorder or "tidy" them.
- ⛔ **The baseline is SHRINK-ONLY in spirit and the header says an empty suppression list would be
  "an invitation to add to it" (`:6122-6134`).** Adding an entry is permitted ONLY for a finding a
  run actually produced, keyed per finding (never per panel), with the WO number in its comment.
  Never suppress a whole panel here - `TouchBaseline` is the per-panel mechanism and it is
  shrink-only by owner ruling; growing it would violate its own header.
- ⛔ `AuditGeometry` (`:5893`), `IsGlyphBaselined` (`:6180`), `ReportGlyphOracle` (`:6301`),
  `ResetGlyphOracle` (`:6288`) - the mechanism is not the bug. **The bug is one uncalled pair.**
  Do not "improve" the oracle while wiring it.
- ⛔ `RenderCanvasToPng`'s audit call at `:5775`, the two rebuild passes at `:5761-5771`, and
  `LandscapeTargets` (`:215-220`). **Do not add a portrait target** - the game is LANDSCAPE ONLY
  (owner ruling 2026-09-10, WO-1631); a portrait row would pin a presentation the game may not reach.
- ⛔ `TitleController.cs` / `LoginPanelController.cs` and any kit fit code. This ticket adds no
  behaviour to the game; it makes an existing measurement speak. A caption fix belongs to its own
  ticket (WO-1621 is the precedent).
- ⛔ Do not register a new suite in `DataRegression.cs` - lead-owned, and nothing new is being
  registered here.
- ⛔ Do not run a gate or commit from the lane. Edit + headless run + hand back; the lead holds the
  Unity lock and is the sole committer. *(If the Unity seat is busy, the edit may ship alone and the
  RUN handed back as the outstanding half - but then the ticket stays open, because an unrun wiring
  proves nothing.)*

---

## 7. Step 1 hand-back - the wiring is IN, the RUN is OUTSTANDING

**Lane:** FRONT-DOOR-GLYPH. **Worktree HEAD:** `a06542478` (fast-forwarded from `f5d39acd1` to
`refs/heads/dev` at lane start; `git log -1` confirms the sha).
Ticket **status stays READY TO IMPLEMENT** - deliberately. Acceptance criteria 2-5 (sec.4) all require a
marker on a fresh log, and this lane cannot run Unity (single seat busy, lead holds the lock).
Per sec.6's own closing rule: *"an unrun wiring proves nothing."*

### 7a. What was edited - one method, seven added lines, nothing else

`Assets/Editor/UICaptureLaunch.cs`, `RunFrontDoorCaptureHeadless` only. `git diff --stat` on this
worktree: `1 file changed, 7 insertions(+)`, zero deletions. Exact diff:

```
@@ -2215,9 +2215,16 @@ namespace DeNelle.Editor
         public static void RunFrontDoorCaptureHeadless()
         {
             Directory.CreateDirectory(OutDir);
+            ResetGlyphOracle();          // WO-1630, beside its sibling so neither can drift
             _loginCaptureStem = "Login";
             int count = ForEachTarget("Title", CaptureTitleOnce) +
                         ForEachTarget("Login", CaptureLoginOnce);
+            ReportGlyphOracle();   // WO-1630 Assert C. Its marker is named ONCE, in the header
+                                   // table and at its one emit site -- never copied here. Wired at
+                                   // EVERY site that emits the touch marker: one path missing it
+                                   // prints marker-absent there, read here as a FAILURE not an unknown.
+                                   // WO-1644: this path was the odd one out -- Title/Login were
+                                   // measured by AuditGeometry and the verdict thrown away.
             if (count == 6) Debug.Log("FRONT_DOOR_CAPTURE_OK 6/6");
             else Debug.LogError("FRONT_DOOR_CAPTURE_FAIL " + count + "/6");
         }
```

Post-edit line numbers: `ResetGlyphOracle()` at **`:2218`**, `ReportGlyphOracle()` at **`:2222`**,
inside `RunFrontDoorCaptureHeadless` (`:2215-2230` after the edit).

### 7b. The placement mirrors the GooglePlay path exactly - both sides cited, read at source today

| | GooglePlay (`RunGooglePlayLoginCaptureHeadless`) | Front door (after this edit) |
|---|---|---|
| Reset | `:2250` `ResetGlyphOracle();` - after `Directory.CreateDirectory(OutDir)` (`:2239`), **before** any `ForEachTarget` capture | `:2218` - after `Directory.CreateDirectory(OutDir)` (`:2217`), **before** both `ForEachTarget` calls |
| Report | `:2281` `ReportGlyphOracle();` - **after** `count = ForEachTarget(...)`, **before** the `GOOGLE_PLAY_LOGIN_CAPTURE_OK/FAIL` emit (`:2295` / `:2297`) | `:2222` - after the two `ForEachTarget` calls (`:2220-2221`), **before** the `FRONT_DOOR_CAPTURE_OK/FAIL` emit (`:2228-2229`) |

*(GooglePlay line numbers shifted +7 from the ticket's `:2243` / `:2274` because this edit inserted
seven lines above that method in the same file. The ticket's numbers were read on `f33451b11`; the
numbers in this table were read on the edited working tree at `a06542478`.)*

The comment text on both added lines is **copied verbatim from the GooglePlay site** so the two
cannot drift, plus two trailing lines naming this WO. **What was deliberately NOT copied**, per
sec.3.1: the presentation-override machinery, `ProveGeometryMoves()`, `ReportFidelity()`,
`ReportGeometry()`, `ReportTouchOracle()`, the `_fidelity*` / `_geo*` / `_touch*` resets, and the
composite `clean` verdict. The front door's `count == 6` verdict is untouched - the glyph marker is
an **independent** line, exactly as it is in every other entry point.

Why the report sits **before** the capture verdict and not after: `ResetGlyphOracle`'s own doc
comment (`:6289-6294`, opened this session) states every entry point READS the tallies AFTER its
`Report*` calls, which is why the reporter does not self-clear. Placing the call anywhere else
would have been a change of shape, not a mirror.

### 7c. Scope compliance - what this lane did NOT touch

- `GlyphBaseline` (`:6167-6181` after the edit; the four entries at `:6170`, `:6173`, `:6176`, `:6179`): **zero entries added, reordered or edited.** No run
  has produced a finding, so per sec.6 there is nothing that may legitimately be baselined yet.
- `LandscapeTargets`, `AuditGeometry`, `IsGlyphBaselined`, `ReportGlyphOracle`, `ResetGlyphOracle`,
  `RenderCanvasToPng`'s audit call and its rebuild passes: untouched.
- `TitleController.cs`, `LoginPanelController.cs`, any kit fit code: untouched.
- `DataRegression.cs`: untouched, nothing registered.
- No other capture path in `UICaptureLaunch.cs` was modified - the diff is confined to one hunk.
- No gate run, no commit, no push from this lane.

### 7d. Checks this lane CAN run, with their outputs verbatim

```
$ python tools/gate_brace.py Assets/Editor/UICaptureLaunch.cs
GATE_BRACE_SUMMARY bad=0 of 1
(exit 0)
```

NUL-byte guard + raw brace count (CLAUDE.md sec.1, both halves):

```
NUL bytes: 0
raw braces: 935 935
```

Both counters agree, so the gate's comment/string-aware scanner and the naive count give the same
verdict - the interpolated-string trap named in CLAUDE.md sec.1 is not in play here (the added lines
contain no string literal at all).

⛔ **NOT PROVEN by this lane, and stated as unproven per CLAUDE.md sec.11B:** that the file compiles
(no `COMPILE_GATE_OK` was run - no Unity), that the marker emits, what it says, whether the front
door reds, and whether `Title_*` / `Login_*` are clean. A brace check is not a compile.

### 7e. What the lead's run must produce, and how to read it

Run `DeNelle.Editor.UICaptureLaunch.RunFrontDoorCaptureHeadless` headless and hand this lane the
**fresh** log. Read the marker, never the exit code. The four outcomes, all four from
`ReportGlyphOracle`'s own branches, read at source this session:

1. `UI_GLYPH_OK <clean>/<checked> panels labels=<n> baselined=<n> unproved=<n>` (`:6347`) with
   **checked = 6** and **labels > 0** -> acceptance criteria met; `Title_*` / `Login_*` become
   MEASURED-clean. No panel is named on a clean run and that is correct (sec.4's own correction).
2. `UI_GLYPH_FAIL x0 -- ZERO panels were measured` (`:6312`) -> the wiring reached the reporter
   but no panel got audited. Report it; do not baseline.
3. `UI_GLYPH_FAIL x0 labels` (`:6324` / `:6329`) -> either no font resolved in batchmode
   (`_glyphUnmeasuredCount > 0`) or Assert C's predicate excludes every front-door control. The
   branch separates the two causes itself. **Different bug, different ticket** (sec.3.3).
4. `[glyph-oracle] <panel tally>` warning lines + `UI_GLYPH_FAIL` with per-finding lines
   (`:6339-6340` tally, `:6359` findings) -> real findings. This lane triages them per finding on the follow-up hand-back:
   a genuine caption cut is **fixed in the panel** in a separate hand-back (never baselined), and
   only known accepted debt earns a `GlyphBaseline` entry in the enforced
   `panelBuild|hierarchy/path|<drawn> of <printable>` format with this WO number in its comment.
   A `Title_*` finding at `2670x1200` contradicts WO-1621 RESULT sec.1 and must be understood before
   either side is trusted.

Marker ABSENT on a fresh log = **FAILURE**, not unknown (CLAUDE.md sec.11B).

### 7f. RAISED, NOT TAKEN - lead ruling wanted (sec.5's "Raise, do not take")

`RunFrontDoorCaptureHeadless` still calls **none** of `ProveGeometryMoves()`, `ReportFidelity()`,
`ReportGeometry()`, `ReportTouchOracle()` - compare the GooglePlay path at `:2253`
(`ProveGeometryMoves()`) and `:2278-2280` (the three reporters). It is the same omission in four
more voices and looks like the same one-line-each fix, but each of those markers feeds the pre-ship
gate ladder (CLAUDE.md sec.8), so turning four more oracles on for a path that has never reported is
a **scope decision for the lead**, not this lane's. This lane did not touch them.

Also unproven and deliberately not asserted (sec.1d): whether the sibling oracles leak between entry
points. Every reporting entry point resets at its own start, so residue is bounded *in principle* -
this lane ran nothing and asserts nothing.

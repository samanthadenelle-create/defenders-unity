# WO-1644 RESULT - the front door now reports its glyph verdict, and the verdict is CLEAN

**Status:** FIXED - 2026-09-10. **Lane:** FRONT-DOOR-GLYPH (edit + evidence read).
**Branch/base:** `dev`, worktree fast-forwarded `f5d39acd1` -> **`a06542478`** at lane start.
**Unity run:** performed by the LEAD (single seat holds the lock); this lane read the resulting logs
itself, on disk, at the paths quoted below. Every number here comes from those files, not from the
hand-back that announced them.

---

## 1. The change - one method, seven lines, no behaviour added to the game

`Assets/Editor/UICaptureLaunch.cs`, `RunFrontDoorCaptureHeadless` **only**.
`git diff --stat`: 1 file changed, **7 insertions(+), 0 deletions(-)**.

- `ResetGlyphOracle();` at **`:2218`** - immediately after `Directory.CreateDirectory(OutDir)`
  (`:2217`), before both `ForEachTarget` captures (`:2220-2221`).
- `ReportGlyphOracle();` at **`:2222`** - after both captures, before the
  `FRONT_DOOR_CAPTURE_OK/FAIL` emit (`:2228-2229`).

Placement mirrors `RunGooglePlayLoginCaptureHeadless` exactly: its reset sits at `:2250` after that
method's `Directory.CreateDirectory(OutDir)` (`:2239`) and before its capture; its report at `:2281`
after the capture and before its `GOOGLE_PLAY_LOGIN_CAPTURE_OK/FAIL` emit (`:2295` / `:2297`). Those
are the ticket's `:2243` / `:2274` shifted **+7** by this insertion. The comment block on both added
lines is copied verbatim from the GooglePlay site so the two cannot drift, plus two lines naming this
WO. The presentation-override machinery and the fidelity / geometry / touch reporters were
deliberately NOT copied (ticket sec.3.1).

## 2. Evidence - three markers, three fresh logs, all read by this lane

| Log (read on disk, NUL-stripped with `tr -d '\000'`) | Size / mtime | Marker line, verbatim |
|---|---|---|
| `Builds/wave5-frontdoor1` | 61 148 B, **07:26** | `UI_GLYPH_OK 6/6 panels labels=18 baselined=0 unproved=0 -- every measurable label drew every printable character it was given.` then `FRONT_DOOR_CAPTURE_OK 6/6` |
| `Builds/wave5-compile1` | 824 741 B, 07:17 | `COMPILE_GATE_OK :: scripts compiled clean` |
| `Builds/wave5-reg1` | 2 081 065 B, 07:25 | `REGRESSION_OK 494/494 suites -- 494 green, 0 red, 0 skipped` |

`grep -a -E "GLYPH|FRONT_DOOR|glyph-oracle"` over the front-door log returns **exactly the two lines
above and nothing else**: no `[glyph-oracle]` panel-tally line, no `NOT PROVED` line, no
`UI_GLYPH_FAIL`. Per `ReportGlyphOracle` (`Assets/Editor/UICaptureLaunch.cs:6339-6343`) the tally
lines print only for panels that HAVE findings, so their absence beside an OK marker is the clean
outcome, exactly as the ticket's sec.4 correction predicted.

Same-run PNGs, all six written at **07:26** into `Builds/ui-capture/`, sizes from the log's own
`[UICap-HL] saved` lines and confirmed by `ls`:

`Title_1920x1080.png` 1 447 959 B, `Title_2340x1080.png` 1 692 039 B, `Title_2670x1200.png`
1 972 466 B, `Login_1920x1080.png` 464 229 B, `Login_2340x1080.png` 524 491 B,
`Login_2670x1200.png` 618 834 B.

## 3. Reading the marker - what each field proves

- **`6/6 panels`** - six PANEL BUILDS clean of six checked, which is 3 Title targets + 3 Login
  targets. The OK line states in its own text that `<clean>/<checked>` counts panel builds and not
  distinct panels, because a caption that fits at one aspect can lose its last word at another. The
  three aspects are the LANDSCAPE targets (`LandscapeTargets`), including `2670x1200`, the Seeker's
  real surface. Acceptance criterion 2's count is met.
- **`labels=18`** - eighteen labels were actually measured, so the run is NOT the
  `UI_GLYPH_FAIL x0 labels` case (`:6324` / `:6329`) that would mean a font failed to resolve in
  batchmode or Assert C's predicate excluded every control. Acceptance criterion 2's `labels > 0` is
  met, and this number is the one that makes the clean line mean anything.
- **`baselined=0`** - not one finding was suppressed by `GlyphBaseline`. The four pre-existing
  entries are untouched and none of them matched, which is consistent: none of them is a front-door
  panel.
- **`unproved=0`** - zero stand-downs; every label the predicate selected produced a TMP `textInfo`
  and was genuinely measured.

## 4. What changed in what we KNOW - the point of the whole ticket

Before this run, `Title_*` and `Login_*` were **unmeasured**, and their absence from `GlyphBaseline`
read as "clean" without ever having been checked. They are now **MEASURED-clean**: eighteen labels
across six landscape panel builds each drew every printable character they were given, on the run
recorded above. That distinction - unmeasured versus measured-clean - is the entire deliverable, and
it is now provable from a marker instead of assumed from a silence.

**Nothing was triaged and nothing was baselined, because nothing red.** No `GlyphBaseline` entry was
added, removed, reordered or edited; the array still holds its four `EndStateWaveClear_repairAll_*`,
`RealmWorkspace_*` and two `ManageWorkspace_*` entries. Per ticket sec.6 an entry may only be added
for a finding a run actually produced, and this run produced none.

No contradiction arose with WO-1621: a Title finding at `2670x1200` would have contradicted that
ticket's device proof, and none appeared.

## 5. STILL NOT PROVEN - stated as unproven, not ticked

- ⛔ **The four sibling reporters are still not wired into the front door.**
  `RunFrontDoorCaptureHeadless` calls none of `ProveGeometryMoves()`, `ReportFidelity()`,
  `ReportGeometry()`, `ReportTouchOracle()` - compare the GooglePlay path at `:2253` and
  `:2278-2280`. So the front door's **fidelity, geometry-move, geometry and TOUCH** verdicts remain
  unreported for the game's first two screens, and their silence must not be read as clean, for
  precisely the reason this ticket existed. It looks like the same one-line-each fix, but each of
  those markers feeds the pre-ship gate ladder (CLAUDE.md sec.8), so turning four more oracles on for
  a path that has never reported them is a **scope decision for the lead / owner - a separate
  ruling and a separate ticket.** This lane did not touch them.
- ⛔ **Whether the front door is clean at any surface not in `LandscapeTargets`.** It was measured at
  three landscape aspects and nothing else. The game is LANDSCAPE ONLY (owner ruling 2026-09-10,
  WO-1631), so this is a scope statement, not a gap - but the marker proves only what it measured.
- ⛔ **Whether the sibling oracles leak state between entry points.** Every reporting entry point
  resets at its own start, so residue is bounded in principle; this lane measured nothing about it
  and asserts nothing.
- ⛔ **Whether these panels stay clean.** A clean run is a measurement of one build, not a guarantee.
  What the fix buys is that the next front-door run will SAY so either way, which it previously
  could not.

## 6. Files

- `Assets/Editor/UICaptureLaunch.cs` - the 7-line wiring in `RunFrontDoorCaptureHeadless`.
- `WorkOrders/WORK_ORDER_1644_front_door_capture_never_reports_the_glyph_oracle.md` - Status flipped
  to FIXED with the marker quoted; acceptance boxes ticked; section 7 carries the Step-1 hand-back.
- This RESULT file.

No gate was run and no commit was made from this lane; the lead holds the Unity lock and is the sole
committer (CLAUDE.md sec.11).

# WO-1631 RESULT - Landscape-only ruling recorded, and the settings drift is now pinned by an oracle

> ⚠ **SUPERSEDED 2026-09-10 (same day).** The ticket was REOPENED: the 05:45 build chain put both
> portrait autorotate flags back to `1`, because `AndroidBuild.cs` assigned them `true` at build time
> and a `PlayerSettings` write is persisted into `ProjectSettings.asset`. The APK
> `2026.09.10.363529` on the Seeker was built with portrait ON and must be **rebuilt, not retested**.
> The body below is frozen as the record of the first pass; the current state is
> **WO-1631 sec.10** in the work order (lane ORIENT-SCRIPT).

**Status:** IMPLEMENTED - awaiting the lead's ProjectSettings flip + gate (lane LANDSCAPE 2026-09-10)
**Lane:** LANDSCAPE, isolated worktree `D:\EoA\.claude\worktrees\agent-a7e746b59e527473d`
**Base sha:** `c10e4f5d1` (fast-forwarded from `f5d39acd1` at lane start; tree clean before and after the merge)
**Scope:** EDIT ONLY. This lane did not run Unity, did not gate, did not commit, and did **not** touch
`ProjectSettings/ProjectSettings.asset` or `DataRegression.cs` (CLAUDE.md sec.11).

---

## 1. What was delivered

| Path | What |
|---|---|
| `CLI_LANES_WO_NUMBERS.md` | Minted **WO-1631** off the main-line banner and bumped `1631 -> 1632` in the SAME edit (new `hundred-and-thirty-eighth pass` block; the `thirty-seventh` block demoted to `superseded`). |
| `WorkOrders/WORK_ORDER_1631_player_build_autorotates_to_portrait_game_is_landscape_only.md` | The ticket: evidence, the enum proof, the one-line fix, the pin, the acceptance. |
| `Assets/Editor/Regression/ScreenOrientationRegression.cs` | **NEW** oracle. `DeNelle.Editor.Regression.ScreenOrientationRegression`, tag `[screen-orientation]`, markers `LANDSCAPE_ONLY_OK` / `LANDSCAPE_ONLY_FAIL`, four cases. |
| `WorkOrders/WORK_ORDER_1621_title_play_intro_caption_truncates_on_the_seeker.md` | Appended a dated `### OWNER RULING 2026-09-10` block (append only; nothing rewritten). |
| `docs/HANDOVER_2026-09-10_overnight.md` | Section 3 item 10 annotated `**RULED 2026-09-10: landscape only.**` - appended after the item, item untouched. |
| `BOARD.html` | Regenerated with `python tools/board_build.py`. |
| this file | The hand-back. |

---

## 2. The one change the LEAD must make - exact before/after

⛔ `ProjectSettings/ProjectSettings.asset` is untouched by this lane. It is a file the editor rewrites
wholesale and it never rides a lane commit.

```
BEFORE   :63  allowedAutorotateToPortrait: 1
AFTER    :63  allowedAutorotateToPortrait: 0

BEFORE   :64  allowedAutorotateToPortraitUpsideDown: 1
AFTER    :64  allowedAutorotateToPortraitUpsideDown: 0
```

**Unchanged, deliberately:** `:11 defaultScreenOrientation: 4` (`AutoRotation`),
`:65 allowedAutorotateToLandscapeRight: 1`, `:66 allowedAutorotateToLandscapeLeft: 1`,
`:67 useOSAutorotation: 1`.

Keeping `4` plus both landscape flags is what preserves **both** landscape directions - the phone may
be held either way up and the game is the right way round - while portrait is never offered. A fixed
`3` (`LandscapeLeft`) or `2` (`LandscapeRight`) would also satisfy "landscape only" but pins ONE
direction, so a player holding the device the other way gets the game upside down with no OS
correction. **That is design reasoning, not a measurement** (WO sec.4b); the suite passes on either
form, so the alternative is available without any code change.

---

## 3. The evidence, and what proves what

Everything below was read or decoded **this session**; nothing is carried from a doc.

- **`ProjectSettings/ProjectSettings.asset:11` = `4`; `:63,:64,:65,:66` all = `1`.** Read at source.
- **`4` is `UnityEditor.UIOrientation.AutoRotation`** - reflected out of
  `C:\Program Files\Unity\Hub\Editor\6000.4.8f1\Editor\Data\Managed\UnityEngine\UnityEditor.CoreModule.dll`
  (`ReflectionOnlyLoadFrom` + `GetRawConstantValue`), which enumerates `Portrait=0`,
  `PortraitUpsideDown=1`, `LandscapeRight=2`, `LandscapeLeft=3`, `AutoRotation=4`. The version pin is
  `ProjectSettings/ProjectVersion.txt:1` (`6000.4.8f1`). The sibling `.xml` documents the five members
  and the summary *"Default mobile device orientation"* (`:60864-60893`) but carries **no numbers** -
  which is why the DLL was read and not the doc.
- **All nine overnight device frames decode as 1200x2670 PORTRAIT** from their PNG IHDR - the three
  felt-test frames (`_0028_title_363195`, `_0031_after_continue_363195`, `_0033_manage_363195`) and the
  six DEVICE-PROFILE frames (02:02-02:10). Sizes and mtimes are tabled in WO sec.1c.
- **Three frames were OPENED and read** - `_0028_title`, `_0033_manage` and `_0031_after_continue`.
  Title: `CONTINUE | START NEW | PLAY INT...`. Manage: the `QUEUE` button sits ON TOP of the `MANAGE`
  title, `UPGRADE ...` is cut. Town HUD: `JOURN...` on the dock, `Start N...`, `Prepare the realm
  for...`, `Heartfire 3...`, `THE NIGHT MAR...`, and `ATTACK REPORT` overprinting `HELD`.
- **Nothing overrides the fields at runtime:** zero `screenOrientation` hits in any `Assets/**/*.xml`
  (both shipped android-library manifests declare none), zero `Screen.orientation` /
  `ScreenOrientation.` writes under `Assets/_Modules` or `Assets/Editor`, and the only other
  orientation-shaped keys in `ProjectSettings/*.asset` are `useOSAutorotation: 1` (`:67`) and
  `xboxEnableHeadOrientation: 0` (`:114`).

### 3a. NOT PROVEN, and named as such (CLAUDE.md sec.11B)

- **WebGL.** The claim "WebGL unaffected" is **not** proven by this lane. What is proven: nothing in
  this tree overrides the fields, and Unity's own summary calls the type *"Default mobile device
  orientation"*. That the browser owns the WebGL canvas is **reasoning**. The cheap close is to open
  the deployed WebGL URL in a desktop browser and resize; it was not done. No WebGL file is touched by
  this change, so the blast radius is bounded either way.
- **The suite has never been run.** No Unity process was started. See sec.4.
- **The overnight performance capture was taken in portrait** (the 02:02-02:10 set). Whether any frame
  cost measured there is aspect-sensitive is **not investigated** - raised for the lead, not concluded.

---

## 4. RED at HEAD - by construction, stated honestly

`ScreenOrientationRegression` is RED at HEAD **by construction**, not by observation:

- Case 2 asserts `allowedAutorotateToPortrait == 0`; `ProjectSettings/ProjectSettings.asset:63` reads `1`.
- Case 2 asserts `allowedAutorotateToPortraitUpsideDown == 0`; `:64` reads `1`.

Two failures, each naming the file, the line, the read value and the consequence. Case 1 passes (all
five keys present exactly once and integer-parseable - verified by the same text scan the suite
performs, run by hand over the asset), Case 3 passes (`4` is a non-portrait value), Case 4 passes with
the both-landscape-flags-on note.

**I did not see it go red - I have not run it.** After the sec.2 flip it should go green with Case 4
reporting the recommended state. The lead's gate run is what converts this from "by construction" to
"measured", and the WO's acceptance sec.8 item 2 asks for exactly that sentence.

### 4a. The mutation, for the lead

Revert either `:63` or `:64` to `1` and Case 2 must red naming that line. Set `:11` to `0` and Case 3
must red. Set `:65` and `:66` both to `0` (with `:11` at `4`) and Case 4 must red on the
over-correction - that last one is the case that exists because "no portrait" is otherwise satisfiable
by an orientation-less build that Case 2 would happily pass.

---

## 5. ⚠ THE FIRST GATE OVER THIS TREE WILL RED ON `regression-marker` - EXPECTED

The suite is **unregistered**, because this lane is forbidden from editing `DataRegression.cs`.
`RegressionMarkerRegression` **RULE 2 [registration]**
(`Assets/Editor/Regression/RegressionMarkerRegression.cs:73-77`, enforced `:594-598`) fails any file
under `Assets/Editor/Regression` exposing `public static bool Run(out string ...)` that is not
referenced in `DataRegression.RunAll`. It will name `ScreenOrientationRegression`. **The lead's one
registration line closes it.** Flagged here so it is not diagnosed as a defect.

RULE 2's opt-out token is deliberately **absent from the suite file**, and the absence is load-bearing:
the check is a plain `IndexOf` over the whole file (`:594`), so writing that token anywhere - even in a
comment saying it is not wanted - would silently opt the suite out of the gate forever. The suite file
says this in its own header, without spelling the token.

**RULE 4 [hollow-pass ratchet] - checked against the SCANNER, not against the header's prose.** Cases
2, 3 and 4 open with an early `return;` behind a guard (`if (!values.ContainsKey(KeyPortrait) || ...)`),
which is the shape the header warns about twice (*"seven that guarded on a NEGATED CALL"*, *"eleven
that stood down with a bare `return;` out of a void section"*). I read `HollowPassScanner.ScanMethod`
rather than infer: at `Assets/Editor/Regression/HollowPassScanner.cs:354-357` it exonerates any guard
whose condition does not share an identifier with the method's own PRODUCED locals - *"A guard on an
INCOMING value skips ONE ITEM, not a section, and belongs to whoever produced the value."* Every one of
those conditions references only the `values` PARAMETER and `const` keys, so it is exonerated at `:357`
before any arm fires. **That is the same exoneration the neighbour `StackTraceLogTypeRegression` relies
on** for its identical `if (values.Count != ExpectedValueCount) return;` guards, and it is why that
suite passes while `KnownHollowSites` is empty by ruling. `ScanVacuous` (`:411-433`) also cannot fire
here: it needs EVERY `failures.Add` to sit inside a positive-existence guard
(`PositiveExistenceGuard`, `:193-194`), and Case 1's adds sit under `hits.Count == 0` /
`hits.Count > 1` / a `TryParse` failure, none of which match. **Claimed as read, not as run** - the
lead's gate is what measures it.

**RULE 1 [marker-uniqueness]:** `LANDSCAPE_ONLY` and `SCREEN_ORIENTATION` return **zero** hits across
`*.cs`, `*.ps1` and `*.md` in the tree, so the markers are unique and neither is a substring of an
existing one. **RULE 3 [gate-grep]:** no `.ps1` under `tools/` or `.claude/skills/` greps for either
literal, so no gate can be left unable to pass. **RULE 6:** the markers carry no count.

**`.meta`:** `.meta` files are tracked under `Assets/Editor/Regression` (510 at HEAD). The lead's first
Unity run generates `ScreenOrientationRegression.cs.meta`; it must ride the same commit.

---

## 6. Checks run by this lane

- `python tools/gate_brace.py Assets/Editor/Regression/ScreenOrientationRegression.cs` ->
  `GATE_BRACE_SUMMARY bad=0 of 1`, exit 0.
- Raw brace balance on the same file: 26 open / 26 close.
- NUL scan: 0 bytes `\x00` in the suite.
- **None of the six gate markers named in CLAUDE.md sec.8 / sec.16 appears in any line this lane
  ADDED.** Scanned two ways: (i) a whole-file substring scan of all seven paths, and (ii) `git diff
  -U0 c10e4f5d1` filtered to `+` lines across the banner, the handover, WO-1621 and `BOARD.html` -
  **0 hits**. The four new files/blocks are clean outright. ⚠ The whole-file scan DOES hit the banner,
  the handover and `BOARD.html` - **that is pre-existing content this lane did not write** (the banner
  and the handover quote the markers in their own history; `BOARD.html` is generated and carries them
  out of other tickets' status lines). Stated precisely because "the file is clean" would have been
  false while "my lines are clean" is true. They are deliberately NOT spelled out in this RESULT
  either: `tools/board_build.py` parses RESULT markers out of `WorkOrders/*.md`, so a RESULT carrying
  one in prose is exactly the misread that guards against, and a log grep would match this file
  instead of a run.
- `CLI_LANES_WO_NUMBERS.md` after the banner edit: CRLF count `4851`, LF count `4851` (still pure CRLF,
  +16 lines), 0 NUL bytes, and the baked mojibake at `:1` left exactly as found - not "fixed".
- `docs/HANDOVER_2026-09-10_overnight.md` after the append: its pre-existing single NUL byte (a literal
  `\x00` inside a `tr -d` shell snippet at offset 22423) is intact and still the only one; line endings
  unchanged in kind.
- `python tools/board_build.py` -> see sec.8.

---

## 7. What this ruling does to WO-1621

Appended to that ticket as a dated block; nothing in it was rewritten, and its `**Status:**` line still
carries the old "portrait device aspect" phrasing - the appended block says that phrase is superseded
and leaves the flip to the lead.

Its sec.6 blocker (a) - *"`Aspects` HAS NO PORTRAIT ENTRY ... a case added to this suite as-is cannot
red on the reported defect"* (`:196-200`) - **dissolves rather than gets solved**: the portrait row must
NOT be added, because a suite that pinned portrait would pin a state the game is not allowed to reach.
What remains of WO-1621 is one narrow question: **does `PLAY INTRO` fit in LANDSCAPE on the device?**
There is no landscape device frame in the tree to answer it - all nine are portrait - so the proof is a
frame the lead's next device pass produces.

**No other portrait-frame ticket is auto-closed by this ruling.** A string that overflowed in portrait
may still overflow in landscape, and closing on inference is the guess CLAUDE.md sec.11B forbids. They
are re-evidenced by one landscape capture pass, not resolved by this commit.

---

## 8. Board

`**Status:**` on the WO reads `IMPLEMENTED - awaiting the lead's ProjectSettings flip + gate (lane
LANDSCAPE 2026-09-10)`. `python tools/board_build.py` was run and reported `BOARD_CHECK_OK`;
`BOARD.html` is regenerated in this worktree. The lead commits the flip in the same commit as the work.

**Paths for the hand-back:**
- WO: `WorkOrders/WORK_ORDER_1631_player_build_autorotates_to_portrait_game_is_landscape_only.md`
- RESULT: `WorkOrders/WORK_ORDER_1631_player_build_autorotates_to_portrait_game_is_landscape_only.RESULT.md`

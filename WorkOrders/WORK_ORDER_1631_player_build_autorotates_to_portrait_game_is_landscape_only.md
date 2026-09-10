# WO-1631 - The player build autorotates to portrait; the game is LANDSCAPE ONLY (owner ruling 2026-09-10)

**Status:** FIXED 2026-09-10 - landscape only at both authorities: ProjectSettings flags 0 AND AndroidBuild no longer forces portrait (Case 5 pins every build script); gated wave4-reg2 494/494; the APK rebuilt after this commit is the first landscape-only APK - owner felt-test on it closes (was: IMPLEMENTED - build-script writer fixed, awaiting gate + rebuild (lane ORIENT-SCRIPT 2026-09-10) (was: FIXED 2026-09-10 - owner ruling landscape only; ProjectSettings portrait autorotate flags 1 -> 0 (lead), ScreenOrientationRegression registered and green on wave3-reg9; owner felt-test on the Seeker closes - REOPENED the same day: the 05:45 build chain put both flags back, see sec.10))
**Minted:** 2026-09-10 (LANDSCAPE lane, `CLI_LANES_WO_NUMBERS.md` main-line banner; bumped 1631 -> 1632 in the SAME edit)
**Silo / Lane:** Project configuration - `ProjectSettings/ProjectSettings.asset` orientation fields, plus one new oracle under `Assets/Editor/Regression/`
**Severity:** P1 presentation. It is not one screen: it is the box every screen is drawn into, on the
owner's own device, in the production build `2026.09.10.363195`. Every layout ticket raised off an
overnight frame was raised against an aspect the game is not supposed to be shown in.
**Type:** EXISTING system, DATA defect. No feature is missing and no code is wrong. Five integers in
one settings file permit a presentation the owner has now ruled out.
**Owner words (2026-09-10, morning, via AskUserQuestion):** **the game is LANDSCAPE ONLY.** This
answers the open question the overnight handover parked as section 3 item 10 ("Is the TOWN HUD
expected to be playable in portrait?" - `docs/HANDOVER_2026-09-10_overnight.md:156-160`). The answer
is NO, and the handover's own text names the consequence of that answer: *"NO = the autorotate flags
are the defect and the fix is landscape-only."*

---

## 1. What was measured (every line and every byte read 2026-09-10, this session)

### 1a. The settings, at source

`ProjectSettings/ProjectSettings.asset`:

```
:11  defaultScreenOrientation: 4
:63  allowedAutorotateToPortrait: 1
:64  allowedAutorotateToPortraitUpsideDown: 1
:65  allowedAutorotateToLandscapeRight: 1
:66  allowedAutorotateToLandscapeLeft: 1
:67  useOSAutorotation: 1
```

All four autorotate flags are ON. The OS is therefore free to hand the game any of the four
orientations, and it does.

### 1b. What `4` means - reflected, not recalled

The project pins Unity `6000.4.8f1` (`ProjectSettings/ProjectVersion.txt:1`). `UnityEditor.UIOrientation`
was read out of that install's own assembly this session:

```
C:\Program Files\Unity\Hub\Editor\6000.4.8f1\Editor\Data\Managed\UnityEngine\UnityEditor.CoreModule.dll
  Portrait = 0
  PortraitUpsideDown = 1
  LandscapeRight = 2
  LandscapeLeft = 3
  AutoRotation = 4
```

(loaded with `Assembly.ReflectionOnlyLoadFrom` and enumerated via `GetRawConstantValue`; the sibling
`UnityEditor.CoreModule.xml:60864-60893` documents the same five members and summarises the type as
*"Default mobile device orientation"*, but carries no numeric values - which is why the DLL was read
rather than the doc).

**So `defaultScreenOrientation: 4` is `AutoRotation`.** That is the field the owner's ruling lands on.

### 1c. The device frames - all nine are PORTRAIT, and that is decoded, not assumed

Every frame captured off the Seeker overnight was IHDR-decoded this session (`width`/`height` at bytes
16-23 of the PNG). All nine are **1200 x 2670 - portrait**:

| Frame (`Builds/device-frames/`) | Pixels | Bytes | mtime |
|---|---|---|---|
| `2026-09-10_0028_title_363195.png` | 1200x2670 | 3026494 | 00:27 |
| `2026-09-10_0031_after_continue_363195.png` | 1200x2670 | 4999738 | 00:29 |
| `2026-09-10_0033_manage_363195.png` | 1200x2670 | 959975 | 00:30 |
| `2026-09-10_0202_profile_title.png` | 1200x2670 | 3026652 | 02:03 |
| `2026-09-10_0203_profile_after_continue.png` | 1200x2670 | 1856979 | 02:03 |
| `2026-09-10_0205_profile_town.png` | 1200x2670 | 2248039 | 02:04 |
| `2026-09-10_0206_profile_town_clear.png` | 1200x2670 | 2345565 | 02:04 |
| `2026-09-10_0208_profile_town_idle_start.png` | 1200x2670 | 4496040 | 02:05 |
| `2026-09-10_0210_profile_town_idle_end.png` | 1200x2670 | 4528338 | 02:07 |

The DEVICE-PROFILE set (02:02-02:10) matters as much as the three felt-test frames: the *performance*
session was also captured in portrait, so any frame-cost number taken off it was measured in a box the
game will not ship in.

**And the contrast is the proof that landscape is the authored intent:** the headless UI capture
renders this game at `2670x1200`, `2340x1080` and `1920x1080` - all three LANDSCAPE (e.g.
`Builds/ui-capture/BuildCollections_2670x1200.png`, cited at `WORK_ORDER_1628_*.md:24`). The device
is showing the game at the *transpose* of the aspect every gate renders it at.

### 1d. The truncations, read off the frames (opened this session)

**`2026-09-10_0028_title_363195.png`** - three faces in the bottom action row:

    CONTINUE   |   START NEW   |   PLAY INT...

The third is ellipsised. This is the whole visible defect of WO-1621.
*(The `Wallet CHKK...sfkC` chip at top-right is intended presentation and is not a defect -
`WORK_ORDER_1621_*.md:34-35`.)*

**`2026-09-10_0033_manage_363195.png`** - the Manage screen:

- The `MANAGE` title is struck through by the `QUEUE` button, which sits ON TOP of the last two
  glyphs. Two controls occupying one band.
- `UPGRADE ...` on the left plate is cut, with `250 Crystals` intact beneath it.
- The three columns (BUILD / ARMY / RESEARCH) are stretched into tall thin strips with a large empty
  band above each caption - the layout is a landscape row of cards squeezed into a portrait column.

**`2026-09-10_0031_after_continue_363195.png`** - the town HUD, and it is the worst of the three:

- The bottom dock reads `BUILD  TALK  HERO  JOURN...  MANAGE` - the Journey caption is cut.
- The wave plate reads `Start N...`.
- The Heart of Elarion card reads `Prepare the realm for...` over `Heartfire 3...`.
- The Night Market card reads `THE NIGHT MAR...`.
- `ATTACK REPORT` overprints the word `HELD` beneath it.
- The dock overlaps the hero, who stands centre-bottom in the play area.

Six truncations and one overlap on ONE screen. That is not six tickets; it is one box.

### 1e. Nothing in the tree overrides these fields at runtime

Grepped 2026-09-10:

- `screenOrientation` in any `*.xml` under `Assets/`: **zero hits**. The two android libraries that
  ship a manifest (`Assets/Plugins/Android/AdsIdentity.androidlib/AndroidManifest.xml`,
  `Assets/Plugins/Android/MobileWalletAdapter.androidlib/AndroidManifest.xml`) declare no
  `android:screenOrientation`.
- `Screen.orientation` or `ScreenOrientation.` under `Assets/_Modules` or `Assets/Editor`: **zero hits**.
- `orientation`-shaped keys in `ProjectSettings/*.asset` other than the six lines in sec.1a:
  only `useOSAutorotation: 1` (`:67`) and `xboxEnableHeadOrientation: 0` (`:114`), neither of which is
  in scope.

**So the asset is the single authority, and changing it changes the shipped behaviour.** There is no
second writer to chase.

---

## 2. What is NOT claimed

- **NOT claimed that these fields are proven irrelevant to WebGL.** What is measured is narrower:
  nothing in this tree overrides them (sec.1e), and Unity's own summary for the type reads *"Default
  mobile device orientation"* (`UnityEditor.CoreModule.xml:60866`). The reasoning that the browser
  owns the WebGL canvas is **reasoning, not a measurement** - a WebGL build was not opened at two
  aspects to prove it. It is written down here as unproven on purpose (CLAUDE.md sec.11B), and the one
  cheap way to close it is to open the deployed WebGL URL in a desktop browser and resize the window.
  Nothing in this ticket touches a WebGL file, so the risk of the change is bounded regardless.
- **NOT claimed that any layout is correct in landscape.** Sec.1d lists what portrait does to five
  screens. Whether `PLAY INTRO` also truncates in LANDSCAPE on this device is **open**, and it is
  exactly what WO-1621 is reduced to (sec.6).
- **NOT claimed the regression suite has been run.** It is RED at HEAD **by construction** from the
  three values in sec.1a; no Unity process was started by this lane. See sec.5.
- **NOT claimed which of the two legal landscape shapes is right.** Sec.4 recommends one and says why;
  the choice is the owner's / the lead's, and the suite passes either.
- **NOT claimed that the overnight performance numbers are wrong.** They were taken in portrait
  (sec.1c) - which is a reason to re-measure after the flip, not a reason to discard them. Raised, not
  concluded.

---

## 3. Target - what "fixed" means

On the Seeker, in a production build, rotating the device never produces a portrait frame. The game
is presented in landscape and only in landscape, both ways up, and a device frame at 2670x1200 proves
it. `ProjectSettings/ProjectSettings.asset` can no longer drift back without a gate going red.

---

## 4. The fix - ONE line, and it belongs to the lead

⛔ **This lane did NOT edit `ProjectSettings/ProjectSettings.asset` and must not.** It is a settings
file the editor rewrites wholesale; it never rides a lane commit. The lead makes the change on the
owner's ruling.

### 4a. The change

```
BEFORE   ProjectSettings/ProjectSettings.asset:63   allowedAutorotateToPortrait: 1
AFTER    ProjectSettings/ProjectSettings.asset:63   allowedAutorotateToPortrait: 0

BEFORE   ProjectSettings/ProjectSettings.asset:64   allowedAutorotateToPortraitUpsideDown: 1
AFTER    ProjectSettings/ProjectSettings.asset:64   allowedAutorotateToPortraitUpsideDown: 0
```

`:11 defaultScreenOrientation: 4` **stays 4** (`AutoRotation`), and `:65` / `:66` (the two landscape
flags) **stay 1**. `:67 useOSAutorotation: 1` is untouched.

### 4b. Why AutoRotation-minus-portrait, and not a fixed landscape value

The alternative is `defaultScreenOrientation: 3` (`LandscapeLeft`) or `: 2` (`LandscapeRight`), which
would also satisfy "landscape only". It is rejected for one reason, and it is a design reason, stated
as a recommendation rather than a measurement:

> A FIXED value pins ONE landscape direction. A player who picks the phone up the other way round
> gets the game upside down and the OS will not correct it, because the app has told it not to.
> `AutoRotation` with both landscape flags on keeps **both** landscape directions, so the device
> flips between them and portrait is simply never offered.

The owner's ruling is "landscape only", not "landscape-left only". The two-flag form is the one that
implements the ruling as stated. If the lead or the owner prefers the fixed form, the suite in sec.5
passes on `2` or `3` as well - Case 3 accepts both, and Case 4 stands down with a note when the
default is fixed.

### 4c. What the editor will also do, and it is expected

Opening the project after the flip rewrites this file. Confirm afterwards that `:63` and `:64` are
still `0` - and note that "the editor put them back" is exactly the silent regression the suite in
sec.5 exists to catch. Do not relax the suite if that happens; re-apply the flip.

---

## 5. The regression pin - DELIVERED IN THIS TICKET

**File:** `Assets/Editor/Regression/ScreenOrientationRegression.cs`
**Namespace / class:** `DeNelle.Editor.Regression.ScreenOrientationRegression`
**Tag:** `[screen-orientation]`  **Markers:** `LANDSCAPE_ONLY_OK` / `LANDSCAPE_ONLY_FAIL`
**Shape:** taken from `Assets/Editor/Regression/StackTraceLogTypeRegression.cs` - the neighbour that
guards a different field of the SAME file: read the asset as TEXT, no play mode, no device, no build,
so it can be trusted to run in the same batch it guards. Keys are split in source
(`"defaultScreen" + "Orientation"`) for the same reason the neighbour splits `m_StackTraceTypes`: this
file's own text must never be mistaken for the asset under test by another source-scanning oracle.

Four cases:

1. **`[fields-present]`** - each of the five keys appears **exactly once** and parses as an integer.
   Zero occurrences, a duplicate, or an unparseable value is a hard FAIL, never a quiet pass (WO-1138:
   a scan that found nothing to assert must not read as an assertion that passed).
2. **`[no-portrait-autorotate]`** - `allowedAutorotateToPortrait` and
   `allowedAutorotateToPortraitUpsideDown` are both `0`. **This is the case the ticket exists for.**
3. **`[default-forbids-portrait]`** - `defaultScreenOrientation` is `2`, `3` or `4`. `0` and `1` name a
   portrait presentation directly and FAIL whatever the flags say, because a fixed portrait default
   does not consult them. Any value outside `0..4` FAILs as a shape change.
4. **`[landscape-still-reachable]`** - the **over-correction guard**. "No portrait" is satisfiable by
   turning ALL FOUR flags off, which would pass Case 2 while shipping a build allowed to rotate to
   nothing. So when the default is `AutoRotation`, at least one landscape flag must be `1`; **both**
   is the recommended state and passes clean, one alone passes with a loud note. An oracle that only
   checks the direction of a change cannot see the over-correction - the neighbour's Case 3 exists for
   the identical reason.

### 5a. RED at HEAD - by construction, and stated as such

The suite **has not been run**; this lane holds no Unity lock (sec.7). It is RED at HEAD *by
construction* from the values read in sec.1a: Case 2 asserts `allowedAutorotateToPortrait == 0` and
`ProjectSettings/ProjectSettings.asset:63` reads `1`, and asserts
`allowedAutorotateToPortraitUpsideDown == 0` where `:64` reads `1`. Two failures, both named with the
file, the line and the value. After the sec.4a flip it goes green with Case 4 reporting the
both-landscape-flags-on recommended state. **The lead's gate run is what turns "by construction" into
"measured", and the RESULT says so.**

### 5b. ⚠ REGISTRATION IS THE LEAD'S ONE LINE, AND UNTIL IT LANDS ANOTHER SUITE REDS

This lane is forbidden from editing `DataRegression.cs`, so the suite is **unregistered**. That is not
neutral: `RegressionMarkerRegression` **RULE 2 [registration]**
(`Assets/Editor/Regression/RegressionMarkerRegression.cs:73-77`, enforced at `:594-598`) fails any file
under `Assets/Editor/Regression` that exposes `public static bool Run(out string ...)` and is not
referenced in `DataRegression.RunAll`. **So the first gate over this tree will red on
`regression-marker`, naming `ScreenOrientationRegression`, until the registration line is added.**
Expected, correct, and flagged here so it is not diagnosed twice.

RULE 2's opt-out token was deliberately **not** written into the suite - and its absence is itself
load-bearing, because the check is a plain `IndexOf` over the whole file (`:594`), so writing the token
even inside a comment saying it is unwanted would silently opt the suite out of the gate forever. It
belongs in the gate.

Marker uniqueness (RULE 1) was checked: `LANDSCAPE_ONLY` and `SCREEN_ORIENTATION` return **zero** hits
across `*.cs`, `*.ps1` and `*.md` in the tree, so no collision and no existing marker is a substring.

### 5c. The `.meta`

`.meta` files are tracked under `Assets/Editor/Regression` (510 of them at HEAD). The lead's first
Unity run generates `ScreenOrientationRegression.cs.meta`; it must ride the same commit.

---

## 6. What this ruling does to the tickets already open

- **WO-1621** (`WorkOrders/WORK_ORDER_1621_title_play_intro_caption_truncates_on_the_seeker.md`) -
  updated by this lane with a dated `### OWNER RULING 2026-09-10` block. Its sec.6 blocker (a) - *"the
  `Aspects` array has no portrait entry, so a case added to this suite as-is cannot red on the reported
  defect"* (`:196-200`) - **dissolves**: the portrait row must NOT be added, because portrait is not a
  supported presentation and a suite that pins it would pin a state the game is not allowed to reach.
  The ticket's remaining question is narrow and unanswered: **does `PLAY INTRO` fit in LANDSCAPE on the
  device?** A landscape device frame is the proof, and there is no such frame - all nine are portrait
  (sec.1c). Its instrument-first Step 1 (`:136-144`) is still the right opening move if it does not fit.
- **The overnight portrait-frame tickets generally.** Any ticket raised off a 1200x2670 frame describes
  a box the game will not ship in. **None are auto-closed by this ruling** - the same string may still
  overflow in landscape, and closing them on inference is the guess CLAUDE.md sec.11B forbids. They are
  **RE-EVIDENCED**: after the flip, a landscape device frame either shows the defect (it was real) or
  does not (it was the box). One capture pass answers all of them.
- **`docs/HANDOVER_2026-09-10_overnight.md` section 3 item 10** - annotated by this lane with
  `RULED: landscape only`, appended after the item without rewriting it.

---

## 7. Pins - what must not move, and what this lane did not touch

- ⛔ **`ProjectSettings/ProjectSettings.asset` was NOT edited by this lane.** Sec.4. The lead makes the
  two-line change on the ruling.
- ⛔ **`Assets/Editor/Regression/DataRegression.cs` was NOT edited.** Sec.5b. Lead-owned.
- **`:11 defaultScreenOrientation` stays `4`** under the recommended fix (sec.4b). Changing it to a
  fixed value is a legitimate alternative but it is a DIFFERENT decision, and it costs the second
  landscape direction - say so out loud if it is taken.
- **`:65` / `:66`, the two landscape flags, stay `1`.** Turning them off to be thorough is the
  over-correction Case 4 exists to catch.
- **`:67 useOSAutorotation: 1`** - not in scope, not read as part of the ruling, not changed.
- **`m_StackTraceTypes` (`:59`) and its suite** - the neighbour in this same file. Untouched; only its
  SHAPE was copied.
- **No layout, caption, font, kit constant or screen was touched.** Every truncation in sec.1d belongs
  to its own ticket and is re-evidenced (sec.6), not fixed here.
- **No Unity run, no gate, no commit** - edit-only lane. The lead holds the Unity lock and is the sole
  committer (CLAUDE.md sec.11).

---

## 8. Acceptance

1. **The two settings lines read `0`** at `ProjectSettings/ProjectSettings.asset:63-64`, with `:11`,
   `:65`, `:66` unchanged. Paste the five lines after the edit.
2. **`ScreenOrientationRegression` goes GREEN on a fresh log**, with Case 4 reporting the
   both-landscape-flags-on state - and the RED-at-HEAD claim of sec.5a is either confirmed by having
   seen it red first, or the RESULT says it was never observed red.
3. **The registration line is in `DataRegression.RunAll`** and `regression-marker` is green again
   (sec.5b).
4. **A landscape device frame.** A production build on the Seeker, rotated both ways, produces no
   portrait frame - and the frame decodes as 2670x1200, not 1200x2670. **This is the acceptance that
   matters**; the marker only proves the setting, the frame proves the device (memory
   `screenshots-are-primary-evidence-for-visual-defects`).
5. **WO-1621 is re-judged on that landscape frame** - does `PLAY INTRO` fit? (sec.6.)
6. **Brace + NUL** on every `.cs` touched, including `python tools/gate_brace.py` (CLAUDE.md sec.1 -
   the gate counts differently from the raw one-liner). Done for the suite by this lane; re-run at the
   gate over the combined tree.

---

## 9. Board

This lane owns this ticket. Its `**Status:**` line above is flipped and
`WorkOrders/WORK_ORDER_1631_player_build_autorotates_to_portrait_game_is_landscape_only.RESULT.md` is
written; `python tools/board_build.py` was re-run. The lead commits the flip in the same commit as the
work (CLAUDE.md sec.11, `.claude/hooks/ORCHESTRATION_CADENCE.md`).

---

## 10. REGRESSION 2026-09-10 (build chain re-enabled portrait)

**The ticket came back the same day it was closed, and the data flip is the whole finding.** The
ruling commit `29296e086` set `ProjectSettings/ProjectSettings.asset` `allowedAutorotateToPortrait`
and `allowedAutorotateToPortraitUpsideDown` from `1` to `0`. After the 05:45 build chain
(`build-windows.ps1 -Release` -> `overnight-apk-build.ps1` -> install -> `build-webgl.ps1`), `git diff`
on that file read `-  allowedAutorotateToPortrait: 0` / `+  allowedAutorotateToPortrait: 1`, and the
same pair for `UpsideDown`. **The APK `2026.09.10.363529` now on the Seeker was therefore built with
portrait autorotate ON**, which is why sec.8's acceptance item 4 (a landscape device frame) could
never have passed against it.

### 10.1 The writer, at source

**`Assets/Editor/AndroidBuild.cs:327-331`** (line numbers as they stood at `a96bfe332`, before this
lane's edit), inside `ApplyAndroidPlayerSettings()` (declared `:307`), called from the Seeker APK
build path at **`AndroidBuild.cs:120`**:

```
PlayerSettings.defaultInterfaceOrientation = UIOrientation.AutoRotation;
PlayerSettings.allowedAutorotateToPortrait = true;
PlayerSettings.allowedAutorotateToPortraitUpsideDown = true;
PlayerSettings.allowedAutorotateToLandscapeRight = true;
PlayerSettings.allowedAutorotateToLandscapeLeft = true;
```

A `PlayerSettings` write is persisted straight back into `ProjectSettings.asset`, so this method does
not merely configure the build - it **rewrites the file the ruling edited**, every time an APK is
built. That is the mechanism `ScreenOrientationRegression`'s own header predicted ("ProjectSettings
.asset is REWRITTEN WHOLESALE ... any tooling that round-trips PlayerSettings silently restores the
portrait flags") and did not guard against.

**It is the sole writer.** A grep for `allowedAutorotate`, `UIOrientation`,
`defaultInterfaceOrientation`, `PlayerSettings.allowed` and `ScreenOrientation` over every `.cs` and
`.ps1` in the tree (2026-09-10, worktree at `a96bfe332`) returned `AndroidBuild.cs`,
`GooglePlayPackagingRegression.cs` and `ScreenOrientationRegression.cs` and **nothing else** -
`DesktopBuild.cs` and the WebGL build write no orientation at all. **This grep is the lane's
first-hand proof and it is what the fix rests on.**

⚠ `Assets/_Modules/Core/Validation/OrientationGuard.cs` appears in all three build logs (a `CS0414`
warning) and is **NOT** a writer - it is the WO-363 *character-facing* gate, nothing to do with the
screen. Named here so the next seat does not lose an hour to it.

**Timestamps, measured this session, and one of them is not what you would expect.** File mtimes read
off `D:\EoA` on 2026-09-10:

| File | mtime |
|---|---|
| `Builds/build.log` (Windows release) | 05:47 |
| `Builds/apk-build.log` (Seeker APK) | 05:54 |
| `Builds/webgl-build.log` | 06:08 |
| `ProjectSettings/ProjectSettings.asset` | **05:57** |

The asset's mtime falls in the **WebGL** window, not the APK one. That does **not** move the writer:
the WebGL build contains no orientation write at all (grep above), and every Unity batchmode session
re-serializes `ProjectSettings.asset` on quit - so a later session that merely *loaded* the
already-flipped values rewrites the file and takes the mtime. The value change is visible earlier: in
`apk-build.log` the `ProjectSettings.asset` import result ID changes mid-run (line 4175 -> line 4276),
and every import in `webgl-build.log` already carries the **later** ID. *Stated as what it is: the
import-ID sequence is consistent with the APK run being where the content changed, and it is not by
itself a proof, because the version stamp also rewrites this file. The proof that the APK step is the
writer is the grep, not the timestamps.*

The lead's independent measurement (05:58) put the orientation writer at `AndroidBuild.cs:327-329`,
which is the same block.

### 10.2 What the writer's comment claimed, and why it does not survive

The block carried a dated rationale, kept verbatim in the source as prose so the tradeoff is not lost:

> "Large-screen / foldable readiness (Play Console, 2026-09-08): do not lock the player activity to
> landscape. AutoRotation with every direction enabled makes Unity emit an unrestricted orientation
> contract, while the generated Unity 6 GameActivity remains resizeable. The UI already derives its
> layout from the live canvas and safe area, so tablets, desktop windows and fold posture changes may
> resize without Android letterboxing the game."

That premise is **WO-1255 (2026-09-08)** and it is **superseded by the owner's ruling of 2026-09-10**:
portrait is not a supported presentation of this game, on any device, at any screen. Play's
large-screen guidance may flag a landscape-only orientation contract; that is an owner-level tradeoff
that has already been ruled, and is recorded here rather than relitigated. `AutoRotation` is **kept**,
so the generated `GameActivity` stays resizeable - only the *directions* it may rotate to changed.

### 10.3 The second half of the defect: the oracle that pinned the writer

`Assets/Editor/Regression/GooglePlayPackagingRegression.cs:49` and `:51` (WO-1255) **REQUIRED the
exact source text assigning `true` to both portrait flags** in `AndroidBuild.cs`. So the packaging
oracle was a portrait writer by proxy: honouring the owner's ruling turned it red, and it would have
forced the flip back in. Both needles are rewritten to require `false`, with their messages corrected
(the AutoRotation message read "does not remove the landscape-only restriction", which is inverted
under the ruling). **This is why the fix could not be a one-line edit at the writer.**

### 10.4 The fix

| File | Change |
|---|---|
| `Assets/Editor/AndroidBuild.cs` (block now at `:321-363`, assignments `:359-363`) | both portrait flags -> `false`; `AutoRotation` and both landscape flags unchanged (`true`); the old Play Console rationale retained as prose with the 2026-09-10 ruling cited over it |
| `Assets/Editor/Regression/GooglePlayPackagingRegression.cs:47-66` | the two portrait pins now require `= false` (`:60`, `:62`); all five messages rewritten to state the ruling |
| `Assets/Editor/Regression/ScreenOrientationRegression.cs` | **new CASE 5 `[no-build-script-portrait-writer]`** + header addendum |

**Never force portrait, and never over-correct**: the shape written at the writer is exactly the shape
the ruling authored into the asset (`:11` `defaultScreenOrientation: 4` = `AutoRotation`, `:63`/`:64`
portrait `0`, `:65`/`:66` landscape `1`), which is CASE 4's recommended state. The build now
**re-asserts** the ruling on every APK instead of undoing it.

### 10.5 CASE 5 - the source lint, and its RED-first proof

CASE 5 scans every `.cs` under `Assets/Editor` (recursive; it skips its own file, which names the API
in prose) for `PlayerSettings.allowedAutorotateToPortrait[UpsideDown] = <rhs>` and FAILS unless the
leading identifier of `<rhs>` is exactly `false`. The rule is "must be `false`", not "must not be
`true`", so `= someFlag` and `= !locked` are caught rather than dodged - an orientation that depends on
a variable cannot be decided by reading the repo (CLAUDE.md sec.11B). The capture stops at end of
**line**, not at a `;`, because half the matches it must judge live inside string literals in the
packaging oracle, where the statement ends at a quote.

**Why CASE 2 was not enough:** CASE 2 reads a *file*, and a build rewrites that file *after* the gate
has run. An oracle that guards only the data cannot see a writer that runs later.

**RED-first.** The C# suite was not executed (edit-only lane: no Unity, no build). A faithful port of
the same regex and the same `LeadingToken` rule was run in PowerShell
(session scratchpad, deliberately not added to the repo - the inline output below is the record) -
stated as a port, not as the suite's own run; the lead's gate is the authority:

```
# against HEAD (a96bfe332), i.e. the code BEFORE this lane's fix
RED  Assets/Editor/AndroidBuild.cs:328 assigns 'true'
RED  Assets/Editor/AndroidBuild.cs:329 assigns 'true'
RED  Assets/Editor/Regression/GooglePlayPackagingRegression.cs:49 assigns 'true'
RED  Assets/Editor/Regression/GooglePlayPackagingRegression.cs:51 assigns 'true'
MODE=head scanned=2 assignments=4 failures=4

# against the working tree AFTER the fix
ok   Assets/Editor/AndroidBuild.cs:360 assigns 'false'
ok   Assets/Editor/AndroidBuild.cs:361 assigns 'false'
ok   Assets/Editor/Regression/GooglePlayPackagingRegression.cs:60 assigns 'false'
ok   Assets/Editor/Regression/GooglePlayPackagingRegression.cs:62 assigns 'false'
MODE=tree scanned=833 assignments=4 failures=0
```

The lint red on the *previous* writer, and on the pin that protected it, before it went green - which
is the only order in which a new case proves anything.

### 10.6 Acceptance, on top of sec.8

7. **`ScreenOrientationRegression` green with CASE 5 in it** - the `[case5]` note must appear in the
   pass reason and must report a non-zero scanned count (a scan that found nothing to assert must not
   read as an assertion that passed).
8. **`GooglePlayPackagingRegression` still green** after its pins moved (`PLAY_PACKAGING_REGRESSION_OK`).
9. **`git diff ProjectSettings/ProjectSettings.asset` is EMPTY after the next full build chain.** This
   is the acceptance this section exists for: the previous FIXED status was true of the repo and false
   of the artifact.
10. **A landscape device frame off a rebuilt APK** (sec.8 item 4). The APK `2026.09.10.363529` cannot
    satisfy it and must be rebuilt, not re-tested.

### 10.7 For the lead

The main tree `D:\EoA` was carrying `M ProjectSettings/ProjectSettings.asset` with the flags back at
`1` when this lane started (that dirt is the defect itself, not new work). Restore it before gating -
`git checkout -- ProjectSettings/ProjectSettings.asset` - or CASE 2 reds on the dirt rather than on
the code. This worktree's copy already reads `0/0/1/1`.

**Not proven from here:** no Unity-side preset, `.preset` asset or settings-snapshot restore mechanism
was found - but that is a grep, not a runtime trace. If the flags ever move again with CASE 5 green,
that is where to look next.

## Device proof (lead, 2026-09-10 07:00)

APK 2026.09.10.363591 (built after e1558e6a0, installed 06:57, dumpsys versionName confirmed). With the accelerometer off and `user_rotation` forced to 0 (portrait), 1 and 3, every screencap is 2670x1200: `Builds/device-frames/2026-09-10_0700_363591_title_portrait_lock.png`, `_rot1.png`, `_rot3.png`. Owner felt-test closes.

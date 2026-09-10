# WORK ORDER 1688 — RESULT (SAVE-GUARD lane)

**Status of this RESULT:** IMPLEMENTED — awaiting gate (no Unity in this lane; no commit)
**Lane:** SAVE-GUARD, isolated worktree `.claude/worktrees/agent-a1662e1f33e9b926a`
**Base:** `de91a22ee` (fast-forwarded from `refs/heads/dev`, confirmed by `git log -1`)
**Date:** 2026-09-10

---

## 0. What is in scope and what is NOT

- **(a) confirm gate on START NEW — IMPLEMENTED.**
- **(b) one-generation local backup before the wipe — IMPLEMENTED.**
- **(c) server/CLI restore path for a deliberately re-blessed backend row — DESCRIBED ONLY**
  (§5 below). No code. The WO-1598 epoch guard is **untouched**, and a regression case now pins
  that it stays untouched.

⛔ **NOT DONE IN THIS LANE, and it is not a claim I can make:** no Unity ran here, so
`COMPILE_GATE_OK` / `REGRESSION_OK` are **unproven**. Nothing was committed. The device was not
touched.

---

## 1. Files changed

| Path | What |
|---|---|
| `Assets/_Modules/Core/State/SaveSchema.cs` | +`BackupKeySuffix = ".prev"` const, declared beside `SignatureKeySuffix` |
| `Assets/_Modules/Core/State/SaveBackupService.cs` | **NEW** — the one-generation wipe-undo |
| `Assets/_Modules/Core/State/GameStateService.cs` | the backup call, as the FIRST statement of `ResetToNewGame` |
| `Assets/_Modules/Onboarding/TitleController.cs` | the confirm gate + `PerformStartNew` + touch-floor pass |
| `Assets/Editor/UICaptureLaunch.cs` | delimited: the confirm is built once per front-door capture and judged |
| `Assets/Editor/Regression/StartNewConfirmGateRegression.cs` | **NEW** — RED pin #1 |
| `Assets/Editor/Regression/SaveWipeBackupRegression.cs` | **NEW** — RED pin #2 |

`python tools/gate_brace.py` (the port of the gate's own rule) → **`GATE_BRACE_SUMMARY bad=0 of 7`**,
exit 0. NUL scan over all seven files → clean. *(The naive whole-file brace one-liner reports
+1/+2 on the two new regression files; that is the `'{'` / `'}'` **char literals** in their
brace-matching helpers, which is precisely the difference CLAUDE.md §1 says to judge by
`gate_brace.py` for.)*

⚠ **Kept clear of the WO-1664 row geometry.** The main tree's uncommitted edit sits in
`BuildButtonColumn` (~:281-282, row anchor 0.135 → 0.195 plus its comment). Nothing in this lane
touches `BuildButtonColumn`, the row anchors, or the button entries list — every edit is in
`OnStartNew` and below, plus `Update` / `OnDestroy` / the field block. A 3-way merge should be clean.

---

## 2. (a) The confirm gate

`OnStartNew` no longer calls `ResetToNewGame`. It now:

1. keeps `if (!_splashActive) return;` (the double-press latch — WO §2.1 keeps it, and it is still
   not a confirm);
2. **fresh install (`HasExistingSave()` false)** → drops the latch and calls `PerformStartNew()`
   directly. Byte-for-byte the old behaviour, no prompt. The predicate is the **existing** one at
   `:396` — deliberately reused, so there is never a second notion of "has a save";
3. **existing save** → raises `ElarionUiKit.BuildConfirmModal` (the `TutorialSkipUi` idiom, cited in
   the WO), with `confirmKind: ButtonKind.Danger`. Nothing is erased at this point.

Everything "start new" MEANS — the reset, `DialogueResetService.ResetForNewGame()`,
`OnboardingMode.ChooseFastPath()`, the trace and `SceneRouter.GoHeroSelect()` — moved into one
private `PerformStartNew()`, called from exactly those two places. **This matters beyond tidiness:**
leaving `ChooseFastPath()` in the handler would have flipped onboarding mode on a *cancelled* press.

### ⚠ Two decisions the lead/owner should look at

**A. The latch is no longer dropped on the mere press — and it had to change.**
The old code set `_splashActive = false` one line into `OnStartNew`. Every other handler
early-returns on `!_splashActive`. So with the confirm added and the latch left where it was,
choosing "Keep My Realm" would have left Continue and Play Intro **dead for the rest of the scene**
— a front-door softlock traded for the save loss. The latch now drops on the *confirmed* branch and
on the fresh-install branch; while the sheet is open, the kit modal's own full-screen scrim is what
stops a stray tap reaching the row underneath. Pinned by `[latch-scope]`.

**B. THE EXACT WORDS — flagged for the owner, as the WO asks.**
They live in four named constants at the top of the confirm block in `TitleController.cs`, so a
ruling is a four-line edit and the headless capture reflects them rather than retyping them:

| Slot | Words | Printable glyphs |
|---|---|---|
| Title | **Erase This Realm?** | 15 |
| Body | **Starting a new game erases your current realm — your town, your hero and everything you have built. This cannot be undone.** | — |
| Destructive face | **Erase** *(was "Erase and Start New" — shortened by measurement, §8)* | 5 |
| Safe face | **Keep** *(was "Keep My Realm")* | 4 |

Facts only: what is lost, and that it is permanent. No lore, no "are you sure". The faces name
only the CHOICE, because the body above them already names the loss in full.

**Which face is where.** The kit lays Cancel on the LEFT (0.10–0.48) and Confirm on the RIGHT
(0.52–0.90). The destructive face is therefore the right one and carries `ButtonKind.Danger`; the
safe face is the left one. Critically, `BuildConfirmModal` wires **the shared Close AND the
full-screen scrim to `onCancel`** — so *every* accidental gesture (tap outside, close, cancel) is the
safe one, by construction. `[cancel-is-safe]` pins that the cancel path reaches neither
`PerformStartNew` nor `ResetToNewGame`.

**Touch floor.** The kit lays the modal's buttons out as panel *fractions*, which can resolve under
`MinTouchPx` at a short landscape aspect. `TitleController.Update` now pads either face up via
`sizeDelta` one frame after open — the same `EnsureTouchFloor` idiom `TutorialSkipUi` uses, and it
matters more here because the two faces are "erase everything" and "keep it".

**Fail-closed build.** If `BuildConfirmModal` returns nothing, Start New is **refused for that press**
with a `FlowTrace.Fail`, never silently degraded back to the one-touch wipe.

---

## 3. (b) The one-generation backup

`Assets/_Modules/Core/State/SaveBackupService.cs`, called as the **first statement** of
`ResetToNewGame` — ahead of the `ENTER` trace, ahead of the WO-1598 epoch read/bump, ahead of every
field assignment. The position *is* the implementation: any line above it is a line a mutation could
later be moved into.

- **Slot name is DERIVED:** `SaveSchema.PlayerPrefsKey + SaveSchema.BackupKeySuffix`. The new suffix
  const is declared beside `SignatureKeySuffix`; `SaveBackupService` is the only place that composes
  it, and `[slot-derivation]` fails if a literal save-key string is ever typed into that file.
- **Through `ISaveProvider`,** never a parallel storage path — so a future cloud provider carries the
  backup with it for free.
- **Signature preserved by copying VERBATIM.** Since the LB-3 atomic envelope the HMAC lives in the
  FRONT of the stored value (`<64-hex>\n<json>`, `SaveSchema.EmbedSignature`), so a byte copy carries
  a signature that still validates against the copied payload. Re-signing would be **strictly worse**:
  it would launder an already-broken body into one that merely looks intact. The **legacy sibling**
  key (`slot + SignatureKeySuffix`, still maintained by `LocalSaveProvider.Delete`) is copied too when
  present, and any stale sibling from the previous generation is dropped first.
- **One generation.** A new backup overwrites the old. Wipe-undo, not save history.
- **Traced, every branch.** Skip, absent-save, IO failure and success each emit their own
  `[Flow:Save]` line; the success line names both byte lengths and both slots and states that it ran
  before the reset mutated anything. On a real device log that line will now appear **immediately
  above** `ResetToNewGame: ENTER` — the two appearing the other way round is a regression, not a
  formatting quirk.
- **A failed backup never blocks the reset** (`Guard.Try` swallows-and-logs) — but it says so loudly.

### ⚠ ONE DELIBERATE DEVIATION FROM §2.2, RAISED NOT SMUGGLED (owner ruling wanted)

WO §2.2 says one generation, always overwrite. Taken literally that **replays the incident through
the fix**, and the path is exact:

> After the wipe the live save is a blank town, so `HasExistingSave()` is **false** and START NEW is
> frictionless *by design* (§2.1). A second accidental press then copies the **blank** save over the
> good backup. The realm is gone on the second touch — the same gesture, one step later.

So `CaptureBeforeReset` takes a `hasProgressToLose` argument, computed at the call site from the same
notion the title gates on (`HeroClass` set, or `Onboarded`). When it is false the write is **skipped**
and the existing backup is **left intact**; a null `_state` counts as "back it up" (unknown is not
empty). Pinned by `[nothing-to-lose]`.

**Revert is one line** if the owner rules the other way: pass `true` unconditionally at
`GameStateService.ResetToNewGame`'s backup call.

### What is NOT built

No restore UI, no auto-restore, no operator button. `SaveBackupService` exposes `TryReadBackup`
(read-only) and **never writes the live slot** — `[no-auto-restore]` pins both halves. An
auto-restore would fight a New Game the player genuinely wanted (WO §2.2).

---

## 4. The RED-first pins

⚠ **RED WAS NOT EXECUTED IN THIS LANE.** There is no Unity here, so "it failed before the fix" is a
**static fail-condition**, not a measured run. The lead's gate is what converts it into a fact.

**Pin #1 — `Assets/Editor/Regression/StartNewConfirmGateRegression.cs`** `[startnew-confirm-gate]`,
markers `STARTNEW_CONFIRM_GATE_OK/_FAIL`. Cases: `no-direct-reset`, `single-reset-site`,
`confirm-idiom`, `fresh-install`, `latch-scope`, `copy-names-loss`, `cancel-is-safe`.
*Static RED condition at HEAD:* case 1 fails because `GameStateService.Instance?.ResetToNewGame();`
sits inside `OnStartNew` at `TitleController.cs:417`. The fresh-install case is green on arrival and
stays green — that is the point of pinning it.

*Why a source sweep and not a live drive, stated plainly:* `DeNelle.EditorRegression` does **not**
reference `DeNelle.Onboarding` (read its asmdef), and reflecting to the real handler would — if the
fix ever regressed — call `ResetToNewGame` against **the developer's own editor PlayerPrefs**, wiping
the save of whoever ran the gate. `ResetToNewGameFullClearRegression` refuses a live drive for
exactly this reason and says so in its header; this suite follows the established trade rather than
inventing a new one. It proves the wiring **shape**; it cannot prove a pixel — the capture side does
that.

**Pin #2 — `Assets/Editor/Regression/SaveWipeBackupRegression.cs`** `[save-wipe-backup]`, markers
`SAVE_WIPE_BACKUP_OK/_FAIL`. Cases: `slot-derivation`, `roundtrip`, `one-generation`, `ordering`,
`nothing-to-lose`, `no-auto-restore`, `epoch-guard-intact`.
Behavioural where it can be: the round trip runs against an **in-memory `ISaveProvider`** installed
into `GameStateService.Provider` and restored in a `finally`, so **PlayerPrefs is never touched**.
The round trip asserts the backup is byte-identical, its embedded signature validates, and it
survives the **normal load path** (`TryExtractSigned` → `SaveFile` → `SaveMigrator.MigrateForImport`
→ `SaveSchema.Validate`) with `heroLevel` intact. The **ordering** is a source lint, because "before
the first mutation" is a property of where the call sits, and asserting it any other way means
executing the reset.
*Static RED condition at HEAD:* the suite does not compile against the pre-fix tree —
`SaveBackupService` does not exist and `SaveSchema` has no `BackupKeySuffix`. That is the strongest
available RED for a change that introduces a type.

### ⛔ REGISTRATION LINES FOR THE LEAD (I did not edit `DataRegression.cs`)

Paste beside the other save/reset suites (they sit near the `reset-full-clear` /
`newgame-pref-sweep` block, which is where a reader will look for them):

```csharp
            // WO-1688 (P1, a real save loss on the owner's device 2026-09-10): START NEW called
            // ResetToNewGame straight from the button callback. These two are the RED-first pins —
            // the confirm gate's wiring shape, and the one-generation pre-reset backup.
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "startnew-confirm-gate suite", () => { if (!DeNelle.Editor.Regression.StartNewConfirmGateRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[startnew-confirm-gate] " + r); });
            DeNelle.Core.Diagnostics.Guard.Try("Regression", "save-wipe-backup suite", () => { if (!DeNelle.Editor.Regression.SaveWipeBackupRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[save-wipe-backup] " + r); });
```

⚠ **Until these two lines land, the gate is EXPECTED TO FAIL** — `RegressionMarkerRegression` flags
unregistered oracles, which is that meta-oracle working as designed ("a suite that is not registered
is not coverage; it is a file"). The suite count in `REGRESSION_OK <n>/<n>` moves by **+2**.

### Capture side

`RunFrontDoorCaptureHeadless` now builds the confirm **once** at the Seeker's real surface
(2670x1200) and shoots it → `StartNewConfirm_2670x1200.png`, so its two faces go through
`AuditGeometry`'s touch + glyph oracles like every other captured surface. The block is delimited
(`WO-1688 WIPE-CONFIRM CAPTURE — BEGIN/END`) and:

- the copy is **reflected out of the shipping constants**, never retyped — a second copy of the words
  here would go stale the first time the owner rules on the wording, leaving a screenshot proving the
  legibility of text no player sees. If a constant cannot be read, it **FAILs** rather than
  substituting a placeholder;
- `FRONT_DOOR_CAPTURE_OK 6/6` arithmetic is **untouched**; the modal carries its own
  `FRONT_DOOR_CONFIRM_CAPTURE_OK 1/1` line;
- **`ReportTouchOracle()` is now called on this path, and it never was before.** `AuditGeometry` has
  always *populated* `_touchFailures` here and nothing printed them — the same defect WO-1644 fixed
  for the glyph oracle on this same method. **Expect this to newly surface `UI_TOUCH_*` on the front
  door, and a pre-existing Title/Login finding would print for the first time. That is the oracle
  working; do not suppress it.**

---

## 5. (c) The restore path — DESCRIPTION ONLY, no code, guard unchanged

**The problem.** The good town (if it exists) is on the backend at reset epoch `1789060460`; the
device bumped its local epoch to `1789060736` at 12:18:56.610. `ApplyBackendState` refuses a row
whose epoch is **older** than the local one (`GameStateService.cs:2312`, message at `:1406`), and the
push side answers `409 SAVE_RESET_STALE` (`:2936`), which states in-code that **"THIS DEVICE DOES NOT
SELF-HEAL"**. That guard is correct (WO-1598: a stale device must not clobber a newer town) and
**must not be weakened** — `[epoch-guard-intact]` now fails if its four tokens leave the file.

**⚠ UNPROVEN, and it stays unproven until someone queries:** whether a backend row carrying the
pre-wipe town exists at all. Every cloud save that session failed **closed** (`why=auth-absent`, 35
markers re-queued), so nothing was written during the incident; whether an *earlier* session ever
landed the good town is a question for the database, not for this document. **Nothing should be
promised to the owner before that query returns.**

**The seam, named from source (read this session):**

| Thing | Where |
|---|---|
| Table | `player_data` (`api/game/save.js:465`, `:687`, `:706`) |
| Columns | `player_id`, `game_state` (JSONB), `schema_version`, `reset_epoch` (INTEGER, nullable), `trust`, `updated_at` |
| Column contract | `api/migrations/20260907_0023_player_data_reset_epoch.sql` |
| The judgement | `judgeResetEpoch(incoming, stored)` — `api/game/save.js:210-230` |
| The refusal | `SaveCode.SAVE_RESET_STALE` → 409 (`api/game/save.js:148`, `:229`) |
| Read side | `api/game/load.js:138`, `:182` (`resetEpoch: toResetEpoch(row.reset_epoch)`) |

**The re-bless, and why it opens no hole.** The epoch is **monotonic and spend-once** — that is
exactly why WO-1598 chose an epoch over a `reset: true` flag, and the upsert additionally clamps with
`GREATEST(player_data.reset_epoch, EXCLUDED.reset_epoch)`. So the operator restore is: write the
known-good `game_state` back onto the player's row **and raise that row's `reset_epoch` above the
device's current local epoch**, in the same statement, touching `updated_at`. On the next cloud
load the client sees a row whose epoch is *newer*, so the epoch gate does not fire; and because the
`UPDATE` runs now, the row is also timestamp-newer, which is what the recency gate needs (that gate
is the WO-1598 **documented gap** — a device whose local save is timestamp-newer is otherwise skipped
and stays stuck). Concretely: `reset_epoch` must exceed `1789060736`.

Two properties make this safe to write down:

1. **No client change and no guard change.** The device does exactly what it does today; it is the
   *row* that is made legitimately newer. An ordinary stale device cannot reproduce this, because it
   cannot raise the stored epoch — its own push carries a *lower* epoch and is refused 409, and
   `GREATEST` refuses to lower the stored value even if some future path reached that SQL directly.
2. **The authorisation is DB-level, not API-level.** There is deliberately **no endpoint** for this.
   The restore is an operator statement executed against Neon with the owner's own credentials, on a
   `player_id` the owner names. Adding an HTTP door for it would be the hole — a "restore my old
   town" endpoint reachable by a client is `reset: true` wearing a different hat.

**Recommended follow-up WO (not opened by this lane):** a runbook that (i) queries whether the row
exists and prints its `reset_epoch` and a diff of the two towns, (ii) takes a copy of the row it is
about to overwrite, (iii) performs the single `UPDATE`, (iv) verifies by shape query, not by the
statement returning (memory `idempotent-ddl-hides-a-stale-table`). With `SaveBackupService` shipped,
a *local* restore is the cheaper first option for this class of incident — and needs no backend at
all — but it does not help the 09-10 loss, which happened before the backup existed.

---

## 6. Acceptance criteria — honest status

- [x] Confirm sheet on `ElarionUiKit.ConfirmModal`, `TutorialSkipUi` idiom; save untouched until the
      destructive face is chosen
- [x] `HasExistingSave()` false → behaves exactly as today
- [x] One-generation backup through `ISaveProvider` before any mutation, with a `FlowTrace` line
      proving the ordering
- [x] Restoring the backup reproduces the prior state through the normal load path *(asserted by the
      `[roundtrip]` case; **not yet executed** — no Unity in this lane)*
- [~] Both RED-first pins exist. **Registration lines are handed to the lead, `DataRegression.cs` was
      NOT edited** (as instructed). "Failed before the fix" is a static condition, **not a measured
      run** — see §4
- [x] The server/CLI restore path is written up; no code; the epoch guard is unchanged and now pinned
- [~] `python tools/gate_brace.py` clean on all seven files (exit 0, `bad=0 of 7`) + NUL scan clean.
      **`COMPILE_GATE_OK` is UNPROVEN — no Unity in this lane**
- [x] This WO's `**Status:**` line flipped; this `.RESULT.md` written. `BOARD.html` regeneration +
      the commit belong to the lead

---

## 8. Chain 41 follow-up (2026-09-10) — two oracle bugs of mine, and the first real measurements

### 8.1 The pin RED was FALSE. The merge was clean; my oracle was wrong — twice.

Diagnosed against `D:\EoA\Assets\_Modules\Onboarding\TitleController.cs` **as merged**:

```
450:  private void OnStartNew()          463/492: PerformStartNew();
537:  private void PerformStartNew()     545: GameStateService.Instance?.ResetToNewGame();
```

**One** `ResetToNewGame(` call site in the whole file, inside `PerformStartNew`. The 3-way merge
is **correct** — nothing for the lead to fix there. The method slice is also correct: `OnStartNew`
slices to 2482 chars and ends well before `:537`. Both failures were mine:

1. **`[no-direct-reset]`** — `StripComments` strips comments but **not string literals**, and
   `OnStartNew`'s own `FlowTrace` lines say *"...ResetToNewGame is unreachable from here until..."*
   and *"...proceeding to ResetToNewGame."* A naked `Contains` cannot tell an identifier from prose
   **about** that identifier. **The oracle fired on its subject's documentation.**
2. **`[confirm-idiom]`** — it asked the **whole file** whether it mentions `BuildModalCanvas`. The
   answer is legitimately **yes**: the title builds its own root canvas with it at
   `TitleController.cs:190`. A kit primitive doing its job, not bespoke popup chrome. The check was
   failing correct code.

**Fixes (both in `StartNewConfirmGateRegression.cs`, no shipped code touched):**
- new `StripLiterals` — blanks string/char literal **contents** while **preserving interpolation
  holes** (`$"...{ Foo() }..."` is still code; blanking it would hide a real call), handling
  verbatim `@"` / `""`, `$@"`, escapes and `{{`. Quotes and newlines are kept, so brace matching and
  index arithmetic are unaffected. Every **code-shape** case now runs on
  `StripLiterals(StripComments(raw))`; **Case 6 still reads the raw file, because the copy IS the
  string**.
- `[confirm-idiom]`'s hand-rolled-chrome check is **scoped to the `OnStartNew` slice** and now also
  catches a hand-rolled `Scrim(`.

**Verified by simulating all seven cases against the merged tree** (`scratchpad/pin_sim.py`, a
line-for-line port):
- merged tree + the new faces → **`FAILURES: NONE → STARTNEW_CONFIRM_GATE_OK`**
- **RED preserved**: against `de91a22ee`'s pre-fix `TitleController` the slice is 551 chars and
  `[no-direct-reset]` still fires on a **code** match — `GameStateService.Instance?.ResetToNewGame()`.
  Literal-stripping removes only the prose hits, never the call.

⚠ `SaveWipeBackupRegression` deliberately **keeps `StripComments` only**: its `[ordering]` case
searches for the trace string `"ResetToNewGame: ENTER"`, which *is* a literal. Stripping literals
there would blind the case that matters most.

### 8.2 The capture worked, and the faces were judged for the first time

`FRONT_DOOR_CONFIRM_CAPTURE_OK 1/1`; `StartNewConfirm_2670x1200.png` written (435591 bytes,
distinct=203, ink=0.0470). Three findings, and **the copy is the only one that is mine**:

**Touch — `UI_TOUCH_FAIL x2 over 7 panels (6 clean)`** — the band, **FIT-GUARD's (WO-1690)**:
```
[touch-oracle] SUB-TOUCH-FLOOR BAND [StartNewConfirm_2670x1200 @2670x1200]
'ObsidianPanel/PanelFill/ObsBtn_Keep My Realm' resolves 356.9x48.5 ref px -- shortest side 48.5
is 63.5 px UNDER ElarionUiKit.MinTouchPx (112). ClampMinTouch will grow it SYMMETRICALLY about
its centre at runtime and spill it into both neighbours. Author the band AT the floor.
[touch-oracle] SUB-TOUCH-FLOOR BAND [StartNewConfirm_2670x1200 @2670x1200]
'ObsidianPanel/PanelFill/ObsBtn_Erase and Start New' resolves 356.9x48.5 ref px -- shortest side
48.5 is 63.5 px UNDER ElarionUiKit.MinTouchPx (112). ...
```
⚠ **My `EnsureConfirmTouchFloor` pass in `Update` is the clamp the oracle is warning about, not the
fix** — growing a 48.5 px face by 63.5 px spills it into both neighbours. It keeps the control
tappable while the band is wrong; **FIT-GUARD authoring the band at 112 is the fix.** Comment
updated in place to say so. `ElarionUiKit.cs` **not touched**.

**Glyph — `UI_GLYPH_FAIL x3 NEW over 7 panels (6 clean, labels=22)`** — two title, one face:
```
[glyph-oracle] TEXT CULLED WHOLE ... 'ObsidianPanel/PanelFill/Label' ("*  Erase This Realm?")
draws ZERO of 16 printable glyphs ... The band has no room for one character at the resolved size.
[glyph-oracle] TEXT CULLED WHOLE ... 'ObsidianPanel/PanelFill/Label' ("*  ERASE THIS REALM?")
draws ZERO of 16 printable glyphs ...
[glyph-oracle] TEXT TRUNCATED ... 'ObsBtn_Erase and Start New/Label' ("ERASE AND START NEW")
draws 12 of 16 printable glyphs ... isTextTruncated=True
```
- **Title band → FIT-GUARD.** **ZERO** of 16, not "a few short": no wording of any length seats
  there, so shortening the title is not the fix and would only hide the band defect. Title left as
  authored; the owner-flagged fallback **"Erase Realm?" (11)** is recorded in-code as a one-line flip
  *if* the repaired band still cannot seat 15 — deliberately **not** pre-applied.
- **Faces → mine, and changed.** `Erase and Start New` → **`Erase`**; `Keep My Realm` → **`Keep`**.
  Four glyphs of the most destructive label in the game were off-screen. New case
  **`[copy-names-loss/face-fits]`** pins both faces against the **measured 12-printable-glyph
  budget**, counted in the oracle's own unit (it read 16 for a 19-character string — it does not
  count spaces), with the instruction to re-run the capture and read the glyph line before ever
  raising it.

### 8.3 What the lead should expect on the next chain

- `startnew-confirm-gate` → **OK** (simulated clean; still RED against pre-fix source).
- `UI_TOUCH_FAIL x2` and the **face** glyph line → **gone once FIT-GUARD lands the band**; my copy
  change alone fixes the face truncation but **not** the 48.5 px height.
- The **two title** glyph lines are FIT-GUARD's band and will persist until WO-1690 lands.
- `FRONT_DOOR_CAPTURE_FAIL 6/6; touchPanels=7; touchClean=6` — the `6/6` is the shot count and is
  **fine**; the FAIL verdict is carried by the touch/glyph tallies, which is the wiring working.

---

## 7. Hand-back

- **WO:** `WorkOrders/WORK_ORDER_1688_start_new_wipes_the_save_before_hero_select_with_no_confirm.md`
  → **Status: IMPLEMENTED — awaiting gate**
- **RESULT:** `WorkOrders/WORK_ORDER_1688_start_new_wipes_the_save_before_hero_select_with_no_confirm.RESULT.md`
- **Lead's remaining steps:** paste the two `DataRegression.cs` registration lines (§4) → gate the
  combined tree once → open `Builds/ui-capture/StartNewConfirm_2670x1200.png` → 3-way merge over the
  uncommitted WO-1664 `TitleController` row edit → commit by explicit path → regenerate `BOARD.html`.
- **Owner decisions waiting:** the four copy strings (§2 B), and the `nothing-to-lose` deviation
  (§3), which has a one-line revert.

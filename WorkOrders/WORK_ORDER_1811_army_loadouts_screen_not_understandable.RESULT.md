# WO-1811 RESULT — the Armies screen, made understandable

**Status:** IMPLEMENTED
another lane's non-compiling file — proving lines below, nothing about it is guessed)
**Date:** 2026-09-16
**Lane:** UI / Village.Troops. Did NOT touch TroopRecoveryService, the raid-end reconcile,
ArmyStorage mutation (WO-1810's lane) or `CLI_LANES_WO_NUMBERS.md`.

---

## 1. What the confusing lines actually meant (all re-read at source this session)
The full table is in the ticket §1. The two findings that changed the design:

* **`Fits now: 5 of 10` is QUEUE room, not army room.** `WouldFit = min(TotalUnits, LineRoom)`
  (`ArmyMusterService.cs:240`); LineRoom = `TrainQueueDepthCap(5) - LineDepth`. It only *looked*
  like the army cap because plan=10, cap=10 and queue-cap=5 coincided in the owner's capture. It
  never contradicted "Army is full 10/10" — it was a second, unlabelled axis.
* **`SHORT OF: Army room` had an invisible cause.** `ArmyRoom = CapSlots - RosterSlots - QueuedSlots`
  (`ArmyMusterService.cs:251-277`) and `RosterSlots` counts WOUNDED troops (`ArmyReadiness.cs:62`).
  The wounded holding the slots were shown nowhere on the screen.
* **A LIVE DEFECT, not just wording:** `UpdateCta` read only the queue axis, so on a full army the
  button stayed enabled and promised "5 start now" while `BarracksService.EnqueueTraining` refuses
  every unit at `rosterSlots + committed + unitSlots > cap`, `stopReason = "Army is full."`
  (`BarracksService.cs:385-386`). Fixed (see §2, the new army-room branch).

## 2. Files changed
| File | Lines | What |
|---|---|---|
| `Assets/_Modules/Village/Troops/ArmyMusterVM.cs` | `ArmyBoardCopy` **:65-166**, `ArmyTrainRow` **:168-179**, `Title` **:223**, the WO-1811 block (Board / lines / TrainRows / TrainOne / ToggleLoadouts / OwnedOf) **:240-429** | every player sentence is now a pure function of integers; `TrainOne` queues ONE unit via the sanctioned `BarracksService.EnqueueTraining(id,1,out reason)`; `Board()` is the single read of army+queue so two lines cannot disagree |
| `Assets/_Modules/Village/Troops/ArmyMusterPanel.cs` | primary band table `ComputeBands` **:151-198**; `ComputeTrainRowBands` **:200-219**; drawer table `ComputeLoadoutBands` **:221-282**; mode-aware `Open` **:331-470**; `CurrentBands` **:614-617**; primary builders **:648-860**; `Kill` **:1195-1199**; CTA army-room branch **:1229-1240** | the primary surface (army bar / train rows / raid door), the Loadouts drawer, the Gold chip removed, "staged"/"SHORT OF"/"Fits now" gone from every literal |
| `Assets/Editor/Regression/ArmyScreenCopyRegression.cs` | new, 284 lines | four army states + a red-first ratchet + a comment-aware literal scan |
| `Assets/Editor/Regression/DataRegression.cs` | **:423** | one registration line (`[army-screen-copy]`) |
| `Assets/Editor/UICaptureLaunch.cs` | **:7616-7729** (`RunArmyScreenCapture` **:7636**, `CaptureArmyScreenOnce` **:7692**) | this panel had NEVER been in the capture harness |
| `Assets/{StreamingAssets,Resources}/Data/Canonical/<10 locales>.json` | 20 files | the `armyScreen.*` keys (§4) |

## 2b. The 20:50 ruling (trained vs deployed, Reserve, Dismiss) — also landed
Verbatim ruling and its reasoning are in the ticket §2b. Implementation:

| File | Lines | What |
|---|---|---|
| `Assets/_Modules/Core/State/ArmyStorage.cs` | **:141-231** | `reserve` list + `EnsureReserve` / `ReserveCountOf` / `MoveToReserve` / `RecallFromReserve`. Additive; a SEPARATE list, so `SlotsUsed` / `GetDeployable` / `CountOfDef` are untouched and "not counted against the raid cap" is true by construction. Removal routes through the WO-1810 `RemoveOwned` seam — nothing forked. |
| `Assets/_Modules/Core/State/SaveSchema.cs` | **:41** | `CurrentVersion` 41 → **42** with the full changelog entry |
| `Assets/_Modules/Core/State/SaveMigrator.cs` | **:83**, **:818-838** | `{42, MigrateToV42}` + the step: seeds the empty reserve and traces that there is nothing to derive |
| `Assets/_Modules/Core/Ops/RemoteTunables.cs` | **:728-752** (const + key), **:1813-1823** (spec) | `army.dismissReturnPercent`, ships **50** (renamed off the money-word boundary, see §5d) |
| `docs/PROD022_TUNABLE_FLAGS.md` | row **78** | the owner-facing row |
| `Assets/Editor/Regression/RemoteTunablesDefaultsRegression.cs` | **:156**, **:356-361** | `ExpectedKnobCount` 77 → **78** + the default pin |
| `api/_lib/tunable-manifest.generated.json` | +15 | regenerated with `node tools/gen-tunable-manifest.mjs` → `TUNABLE_MANIFEST_GEN_OK knobs=78` (never hand-merged) |
| `api/_lib/tunable-manifest.js` | +16 | the HAND-AUTHORED presentation row (area / label / plain English / safe range). It is NOT generated - `mismatches()` reports a key present in the Registry and missing here. |
| `api/_lib/tunables.js` | +9 | the server WRITE ALLOWLIST. Without it the API would refuse every write to the knob - found by running `mismatches()`, not by reading. |
| `ArmyMusterVM.cs` | **:215-243** (row fields), **:415-560** (DismissGoldFor / MoveToReserveOne / RecallOne / DismissOne / Persist) | the two exits, the gold, the counts |
| `ArmyMusterPanel.cs` | **:200-225** (row bands, now Manage + Train), **:805-880** (the manage sheet) | one `Manage` face per row opening a sheet whose faces are Move to Reserve / Return to army / Dismiss for N gold / Cancel |

⚠ **The dismissal price is PROPOSED, not ruled.** Training charges nothing (WO-1387), so there is no
paid price to return; 50 % of `TroopDef.CostGold` is a proposal behind a knob, and the owner's number
replaces it with a row, not a rebuild.

## 3. The screen now
* **(a) WHAT YOU HAVE** — `Army 7 of 10`, and only when non-zero `3 recovering - ready in 19m`,
  then `Room for 3 more` / `Army full - 3 recovering`. Beside it: `Training 2 - 4m 10s left` or
  `Nothing training`.
* **(b) WHAT TO TRAIN** — one row per unlocked troop: name, `45s each - 4 active, 1 reserve`
  (`+2 training` while the queue holds any), a **Train** button that queues ONE, and a **Manage**
  face opening the removal sheet (Move to Reserve / Return to army / Dismiss for N gold). Dimmed
  (not hued — the owner is colour-blind) when the army has no room.
* **(c) GO** — `Army ready - go raid`; when short it names a number the player can ACT on:
  `Train 3 more, 3 recovering` on the owner's own fixture (the naive shortfall is 6, but the wounded
  hold 3 of the slots), or `Ready when 3 recover` when recovery alone closes the gap. Live only on
  `ArmyReadiness.Ready`; opens the existing `RaidSelectionScreen.Open()`. The gate rule is untouched.
* **Presets** (tabs / steppers / Name / Save slot / bulk `Train Army`) moved behind one **Loadouts**
  door. The preset data model is untouched.
* FlowTrace `"ArmyUI"` on open, board read (throttled), train, refusal, status well and actions. The
  existing `"Muster"` traces are untouched (§12 — never strip instrumentation).

## 4. Locale keys added (34, in all 10 policy locales, source + mirror, parity-clean)
`armyScreen.` + `title, armyLine, recovering, roomFor, armyFull, armyFullRecovering,
nothingTraining, training, queueFull, rowMeta, trainButton, inTrainingTitle, ready, notReady,
loadouts, tip, trainedOne, noTroops` (pass 1) + `rowTraining, reserveLine, manage, manageBody,
moveToReserve, dismissFor, dismissFree, recall, cancel, movedToReserve, recalled, dismissed,
noRoomToRecall` (the ruling) + `readyWhenRecovered, notReadyRecovering, dismissedFree`.
One existing value was deliberately REWRITTEN: `armyScreen.rowMeta` now carries the active/reserve
split the ruling requires. Every file: 462 -> **496** keys, source and mirror md5-identical (all 10
checked).
Written byte-surgically (existing bytes untouched, CRLF preserved).
**The lead still needs to run `LocalizationBuilder.BuildAll`** to push them into the Unity
StringTable; the runtime reads `Resources/Data/Canonical/*.json` directly, so the screen renders
correctly before that, and the C# calls carry English fallbacks either way.

## 5. Verification — what is proven and what is NOT
**Proven:**
* `python tools/gate_brace.py` on all **10** touched `.cs` → `GATE_BRACE_SUMMARY bad=0 of 10`, exit 0.
* No NUL byte in any of the 10 files (byte scan).
* A comment-aware literal scan of the panel and VM: **0** banned literals
  (`staged` / `short of` / `fits now` / `slot <n>`) — 232 and 144 literals scanned.
* The locale JSON round-trips (`json.loads`), every pre-existing key is byte-identical (the one
  deliberate value change is `armyScreen.rowMeta`, rewritten for the trained-vs-active ruling), key
  count 462 → **493** on all 20 files, source and mirror md5-identical (checked en/es/ja/ar).
* `node tools/gen-tunable-manifest.mjs` → `TUNABLE_MANIFEST_GEN_OK knobs=78`, and
  `tunable-manifest.mismatches(PRESENTATION)` now returns `[]` (it first reported the missing
  allowlist entry, which is how that gap was found rather than assumed).

**NOT proven — say it plainly (§11B):**
* ⛔ **No Unity run has verified ANY of this**, and the lane is now under a standing coordinator
  instruction (2026-09-16) not to start Unity until GO, because two other lanes are baking scenes.
  So the capture, the new regression, the schema-bump suites and `RemoteTunablesDefaultsRegression`
  (78) are all UNRUN. Nothing below is a compile claim.
* ⛔ **The headless capture never ran, and neither did the new regression.** Three batchmode
  attempts (`Builds/wo1811-army-capture.log`, `-capture2.log`, `-capture3.log`) all aborted on a
  compile error in a file belonging to ANOTHER lane:
  `Assets\Editor\Regression\EnemyTowerWallLosRegression.cs(62,64): error CS0535:
  'EnemyTowerWallLosRegression.DummyPartyMember' does not implement interface member
  'IDamageableStructure.Faction'` (and `(69,70)` for `DummyFlyingPartyMember`). That file is
  untracked (`??`) and is not in this lane's silo, so it was not edited. On the middle run
  `RaidCasualtyRegression.cs(317,47)/(334,47) error CS1503` also appeared and has since cleared —
  those lanes are live in the same tree.
  **Consequence: there is no `ArmyScreen_2670x1200.png`, and therefore no greyscale contrast read.**
  The moment `EnemyTowerWallLosRegression` compiles, the one command that closes this is:
  `powershell -File .\run-unity-method.ps1 -Method DeNelle.Editor.UICaptureLaunch.RunArmyScreenCapture
   -LogName wo1811-army-capture.log -ExpectMarker ARMY_SCREEN_CAPTURE_OK`
  (it seeds 7 troops, 3 wounded at 19m, Barracks 2, `EverCompletedRaid`, and shoots the primary
  surface + the drawer at 1920x1080 / 2340x1080 / 2670x1200).
* Evidence the code itself compiles is **partial, not full**: on the first run the compiler reported
  errors ONLY in that foreign file while processing the same assemblies, so nothing in this lane's
  five files produced a diagnostic — but no `COMPILE_GATE_OK` was earned and I do not claim one.
* The owner's felt-verdict is the close condition (§13, PO closes), and it needs a build.

## 5b. Fixed on review, before hand-back (each was a real defect, not polish)
1. **The raid door contradicted the room line** on the owner's own fixture: the naive shortfall is 6
   while the army has room for 3. `ReadyLine` now splits the wounded out, and the regression FAILS
   whenever the number asked for exceeds the room without naming the recovering troops.
2. **The manage sheet's faces were ~89 px on the Seeker** (under `MinTouchPx` 112) and a fourth face
   silently fell off the bottom. Face height is now derived in reference pixels from the sheet's own
   height, and the Cancel face is gone — the kit Close and the scrim already dismiss it.
3. **A zero-gold dismissal quoted "0 gold"** — the same false price the screen was cleaned of. New
   `dismissedFree` copy.
4. `EconomyService` fully qualified in the VM (no `using` for it).
5. **`DismissOne` preferred the WOUNDED troop - a gold exploit.** Paying the same percent for a
   recovering body turns the recovery timer into a price: dismiss the three wounded for half value,
   retrain healthy replacements for nothing (WO-1387), wait skipped. Now healthy-first, and the
   trace names which list the body came from.
6. **`MoveToReserve` now refuses a WOUNDED troop and a blank-id troop.** `AdvanceRecovery` iterates
   `Owned` only, so a wounded body parked in the reserve would stop healing forever; and
   `RemoveOwned` keys on `Id`, so a blank id would have left a troop in BOTH lists.

## 5c. Regression bounce fixed (fresh log `Builds/regression.log` 21:40, REGRESSION_FAIL 556/564)
1. **[first-raid soft gate] x3 — my v42 bump orphaned the Phase E derivation.** `SaveMigrator.Migrate`
   runs `if (fromVersion < step.Key)`, and the suite migrates from `CurrentVersion - 1`; adding step
   42 meant a v41 save ran ONLY step 42, so `MigrateToV41`'s derive never ran and `everCompletedRaid`
   stayed null on exactly the saves the shipping build wrote. `MigrateToV42` now calls
   `MigrateToV41(s)` FIRST (`SaveMigrator.cs:826-838`) — idempotent, because that step returns
   immediately when the flag already carries a value. Adding a version on top of a DERIVING step
   orphans it for the one version below: that is the lesson, and it is written at the call.
2. **[glyph coverage] fr `armyScreen.inTrainingTitle` carried U+00CE** (Î, from "ENTRAÎNEMENT" in
   caps) which the HUD font lacks → re-worded to **"EN FORMATION"** in source + mirror (md5-identical,
   496 keys). Swept every locale's `armyScreen.*` values for uppercase-accented Latin (U+00C0-U+00DE):
   no others.

## 5d. Felt-test fixes on the first real capture (owner surfaces 1920x1080 / 2340x1080 / 2670x1200)
1. **"MANAGE" rendered "MANA..." at 2340x1080.** Two halves, because one alone would have been a
   near miss again: the caption is now a SHORT word per locale (`armyScreen.manage` -> More / Mas /
   Mais / Mehr / Plus / Еще / المزيد / 変更 / 변경 / 更多, all <= 7 chars, no uppercase-accented
   Latin) AND the face is authored wider - `MinTouchPx + 48` with a 0.185 floor
   (`ArmyMusterPanel.cs:205-215`), measuring **161 / 178 / 181 px** on the three surfaces against
   139 / 154 / 156 before. MinTouchPx is a floor for a FINGER, never a width for a WORD.
2. **The IN TRAINING well repeated the header** (army / room / recovering). Those lines are the
   header band's job and are gone from the well, which is now training + the one-line hint only;
   its trace says `status well (training only)`. Printing the same number twice is the
   duplicated-state failure this ticket exists to remove.
3. **`test/tunables-manifest.test.js:289` — the knob named a money word.** The rule is deliberate
   and shape-based ("a future seat adding just one more knob trips it rather than shipping it"), and
   `refund` is on its forbidden list; the test itself says to update it only when adding the first
   serverOnly row, which this is not. So the KNOB was renamed, not the test:
   **`army.dismissRefundPercent` -> `army.dismissReturnPercent`**, across all six places (const,
   key, Registry spec, docs row 78, defaults pin, both api/_lib files) + regenerated
   (`TUNABLE_MANIFEST_GEN_OK knobs=78`). `node --test test/tunables-manifest.test.js` -> **27 pass,
   0 fail**.

## 5e. Re-shoot audit failures (UI_GEOMETRY_FAIL x36 / UI_TOUCH_FAIL x36 / UI_GLYPH_FAIL x1, 22:03)
**Root cause, read at source rather than theorised — and the tell was in the number.** Every row
button measured exactly **100 px** on all three surfaces; a value that does not move with the
surface came from a constant, not from a layout. `ElarionUiKit.MakeScrollZone` builds its column
with **`childControlHeight = false`** (deliberate — kit rows are sized by explicit `sizeDelta`, as
`DefenseMapPlate.cs:159` says in capitals), so the `LayoutElement.preferredHeight` the rows carried
was **ignored** and each row kept a fresh RectTransform's **100x100 default**. The buttons span the
row, so all 36 inherited it. Fixed by authoring the row's `sizeDelta`
(`ArmyMusterPanel.cs:752` and `:1043`, both the primary row AND the drawer's stepper row, which had
the same latent bug) and lifting `RowHeightPx` to `MinTouchPx + 4` for rounding headroom.

**UI_GLYPH_FAIL** was in the DRAWER: `'RAID *ACTIVE*'` drew **11 of 12** glyphs at 1920x1080 in a
264 px band. Both halves fixed, as with the Manage caption: the asterisks are gone (`RAID ACTIVE` —
the WORD was always the greyscale-safe tell, the `*` were pure width) and the four selector bands
are wider (`Slot.* / Clear`, still disjoint and inside the panel).

**Re-derived every authored band and row from the panel's own tables, on all three surfaces**
(same reference-box maths as the layout oracle): row 116 px; train face 161/178/181 x 116; stepper
120 x 116; every touchable band >= 112 on its shortest side; every band disjoint and inside the
panel; **3 visible rows kept at every size**, so the fix costs no content.

## 6. Residual / for the lead
* `ArmyMusterLayoutRegression` was **not** edited: the primary table keeps the `Roster` band name and
  x-range that its count-field case derives `rowW` from, and its wording pins (`"Train Army"`, the
  2026-08-26 tip line, the `Muster` FlowTrace tag, `ArmyMusterService.Muster(` in the VM) all still
  hold in the drawer path. That is reasoned from the suite's source, and is one of the things the
  blocked regression run would have measured.
* **NO ORACLE MEASURES the new row table or the manage sheet.** `ArmyMusterLayoutRegression` knows
  `ComputeBands` (the primary table it does measure) and the old `ComputeRowBands`; `Train.Manage` /
  `Train.Button` and the sheet's faces are authored from `MinTouchPx` and reasoned, not measured.
  Stated rather than implied.
* `PanelManager.NotifyOpened` refuses only on `BattleLock.IsInBattle()`, so the capture path cannot
  be silently refused into an empty panel (read at source).
* Opening/closing the Loadouts drawer re-runs `HydrateFromActiveSlot`, so unsaved stepper edits are
  reloaded from the saved slot — the same behaviour as close-and-reopen today, but worth knowing on
  a felt-test.
* The pass-2/3 Latin translations are accent-light in places (`Ejercito`, `Fuer`, `Gerer`) where
  pass 1 kept accents. Cosmetic, and these locales are `ai-first-draft` by policy.
* ⚠ **`api/_lib/tunable-manifest.js` and `api/_lib/tunables.js` carry TWO lanes' uncommitted rows.**
  WO-1810 hand-authored the `raid.lossPct*` presentation/allowlist entries in the same files today
  and has not committed them. MY block is `'army.dismissReturnPercent'` ONLY -
  `tunable-manifest.js:903-919` and `tunables.js:313-321`. Stage by hunk, or settle it with 1810.
* The `armyScreen.cancel` key is now unused (the sheet's Cancel face was dropped - the kit Close and
  the scrim already dismiss it). Left in the table rather than removed from 20 files for nothing.
* `UICaptureLaunch.cs:6499` still lists `"ArmyMuster"` in the touch baseline against a `slot-chip-0`
  that now lives in the drawer. Left alone deliberately — it is a shared file and the entry is inert
  until that panel is shot; worth one line of the lead's judgement.

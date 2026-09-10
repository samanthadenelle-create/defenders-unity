# WORK ORDER 1688 — START NEW wipes the save before hero select, with no confirm

**Status:** IMPLEMENTED — awaiting gate
> (a) + (b) landed 2026-09-10 by the SAVE-GUARD lane; (c) described only. See
> `WORK_ORDER_1688_start_new_wipes_the_save_before_hero_select_with_no_confirm.RESULT.md`.
> ⚠ The lead must paste the two `DataRegression.cs` registration lines from the RESULT §4 —
> until they land the gate is EXPECTED to fail on the unregistered-oracle meta-check.
> ⚠ Owner decisions waiting: the four confirm copy strings, and the one deviation from §2.2
> (a backup is SKIPPED when the live state has nothing to lose — RESULT §3).
**Priority:** P1
**Silo:** Onboarding / Core.State (save layer)
**Raised:** 2026-09-10, device-raid lane, on the owner's Seeker (SM02G4061955851)
**Build under test:** `versionCode=363866 versionName=2026.09.10.363866` (`adb shell dumpsys package`)

> **WO number 1688 was PRE-ASSIGNED by the lead. Do NOT edit the `CLI_LANES_WO_NUMBERS.md`
> banner for this ticket** — the mint was already recorded by the seat that assigned it.

---

## 1. What happened

While the device-raid lane was driving the Seeker for WO-1637/1638/1639/1640/1646/1647 device
proofs, **the owner's live save was destroyed**. One press of **START NEW** on the title screen
wiped every persisted field — before a hero was chosen, with no confirmation of any kind, and
with no way back.

The lane spent **0 gold** and never reached a raid. Raids are now unreachable on the resulting
save (`lock=NoBarracks`).

### 1.1 The two frames that prove the wipe

| Before | After |
|---|---|
| `Builds/device-frames/2026-09-10_1220_363866_raid_10_after_continue.png` (12:16:47) | `Builds/device-frames/2026-09-10_1233_363866_raid_20_town_gold_before.png` (12:25:25) |
| Hero **Thrain Lv 1** | Hero **Grom Lv 1** |
| Heart plate: **"Heartfire 3/3 (raids)"** | Heart plate: **"Raids unlock at a Barracks - Build > Realm"** |
| Night Market chip, blacksmith, houses, fences, quarry | **Empty field — no structures at all** |
| Echoes 1/6, Gold 700 | Echoes 1/6, Gold 700 (200 seed + 500 daily chest) |

### 1.2 The log sequence, verbatim

From `Builds/device-frames/2026-09-10_1209_363866_raid_logcat_stream.txt:133058` onward
(pid 19861, device clock):

```
09-10 12:18:56.608 [Flow:Save] ResetToNewGame: ENTER — hero on entry level=1 xp=0 lifetime=0 (about to re-seed every persisted field).
09-10 12:18:56.610 [Flow:Sync] ResetToNewGame: reset epoch 1789060460 -> 1789060736. The next cloud save DECLARES this number, which is what lets api/game/save.js accept a new town's lower balances once instead of rejecting them as an implausible drop (WO-1598). A backend row carrying an OLDER epoch is refused by ApplyBackendState from here on.
09-10 12:18:56.610 [Flow:Save] ResetToNewGame: cleared 1 of 1 stale talent/Wisdom PlayerPrefs key(s) (dotr-talents-v1) - a new game starts with ZERO Wisdom and ZERO unlocked talent nodes, never the previous hero's tree.
09-10 12:18:56.610 [Flow:Save] ResetToNewGame: cleared 10 stale harvest PlayerPrefs key(s) across 3 collector id(s) (dotr.collector.pending/hp/lastaccrual.* + dotr.resbuilding.level.*) - a new game starts with an EMPTY collector and a base-level cap, never the previous save's 14,089-resource fill (WO-1371).
09-10 12:18:56.612 [Flow:Save] ResetToNewGame: EXIT — hero level 1->1 xp 0->0; notified 8 live progression subscriber(s). Anything that reads level>1 after this line re-introduced it.
09-10 12:18:56.614 [Flow:Save] wrote signed save via LocalSaveProvider (len=3417).
09-10 12:18:56.615 [Flow:Onboarding] OnStartNew: routing to the HeroSelect carousel (fresh HeroClass=None).
09-10 12:18:56.617 [Flow:Save] wrote signed save via LocalSaveProvider (len=3417).
```

**Read the ordering.** `ResetToNewGame` ENTERs, the epoch is bumped, the wiped state is
**written to disk as a signed save (`len=3417`, down from the loaded `len=9457`)** — and only
*then* does `OnStartNew` route to the HeroSelect carousel. The save is gone **nine milliseconds
before the player is even shown the hero they are supposedly starting with**, and gone
regardless of whether they ever pick one. In this incident the carousel was reached
(`scene='HeroSelect'` at 12:22:41–12:22:44) and the app was force-stopped without a choice
being made; the owner's town was already unrecoverable at 12:18:56.614.

### 1.3 The code path

`Assets/_Modules/Onboarding/TitleController.cs`

- `:331` — the button is registered: `entries.Add(("Start New", ElarionUiKit.ObsidianButtonColor.Yellow, OnStartNew, true));`
- `:408` — `private void OnStartNew()`
- `:410-411` — `if (!_splashActive) return; _splashActive = false;` — the ONLY guard on the path, and it is a double-press latch, not a confirm
- **`:417` — `GameStateService.Instance?.ResetToNewGame();`** ← the destructive call, invoked directly from the button callback
- `:418` — `DeNelle.Core.DialogueResetService.ResetForNewGame();`
- `:427-428` — the `FlowTrace.Step` above, then `SceneRouter.GoHeroSelect();`

`GameStateService.ResetToNewGame()` is `Assets/_Modules/Core/State/GameStateService.cs:1210`.

**There is no confirm sheet, no "this will erase your realm" copy, and no undo.**

### 1.4 There is no backup to fall back on

`Assets/_Modules/Core/State/SaveSchema.cs:47` — `public const string PlayerPrefsKey = "dotr-save";`
(`:99` — `SignatureKeySuffix = ".sig"`). `LocalSaveProvider` is a **single-slot** store:
`Exists/Read/Write/Delete` all take one `slot` and the call sites pass
`SaveSchema.PlayerPrefsKey` (`Assets/_Modules/Core/State/LocalSaveProvider.cs:7-46`). Nothing
anywhere writes a previous-save copy. Once `ResetToNewGame` writes `len=3417` over
`dotr-save`, the prior signed body no longer exists on the device.

### 1.5 The cloud copy is intact but the client now refuses it

Cloud was **never written** during the incident session — every attempt failed closed:

```
09-10 12:18:46.494 [Flow:Sync] offline queue drain FAILED - 35 marker(s) re-queued and RETAINED (never dropped). The player's progress is safe in the local save; it is the cloud copy that is behind. why=auth-absent http=0 body=no live backend session - no live backend session, so the request was never sent (fail-closed).
09-10 12:25:07.792 [Flow:Sync] offline save queue CROSSED 25 unsent markers (depth 40) - cloud saves have been failing continuously.
```

So a backend row, **if one exists**, still carries the pre-wipe town at reset epoch
**`1789060460`**. But the client will not take it back: the epoch guard now treats that row as
stale — see the `[Flow:Sync]` line at 12:18:56.610 above, `GameStateService.cs:1406`
(the message string), `:844` (the WO-1598 field), `:2312` (the apply-side rule) and `:2936`
(`409 SAVE_RESET_STALE`, which documents in-code that **"THIS DEVICE DOES NOT SELF-HEAL"**).

**Whether a backend row with the old town actually exists is UNPROVEN** and must be checked
before anything is promised to the owner.

### 1.6 Root cause of the *press* — NOT a scripted tap

`adbd` logs every shell command it services. Across **12:18:20–12:19:05** the only entries are
the lane's screen-recorder `appops`/`am force-stop` calls plus one `input tap 1331 991` at
**12:18:25.263** — and the process that performed the wipe (**pid 19861**) was only started at
**12:18:35.024** (`ActivityManager: Start proc 19861 ... for next-top-activity`), ten seconds
*after* that tap and with **no adb `am start` or `monkey`**. The same holds for the hero commit
(`scene='HeroSelect'` 12:22:44 → `resolved 'knight' from the PERSISTED GameState.HeroClass ...
'Grom'` 12:24:24, zero adb input between) and for the daily-chest claim
(`[Flow:DailyChest] claim toast path=free`, 12:25:19.621).

**Conclusion: the presses came from a non-adb input source** — a physical touch, or the
screen-recorder app's floating overlay bubble passing touches through (it was on screen and
expanded; see `Builds/device-frames/2026-09-10_1224_363866_raid_14_town.png`). This does not
soften the ticket. It sharpens it: **a single unintended touch on a title button permanently
destroyed a real player's realm.** That is the defect, whoever's finger it was.

---

## 2. Fix shape

Three parts. **Do not implement the third — describe it.**

### 2.1 A confirm gate on START NEW when a non-fresh save exists

`OnStartNew` must not call `ResetToNewGame()` directly. It must first ask, and only wipe on an
explicit second, deliberate confirmation.

- **The predicate already exists and is already trusted on this screen:**
  `TitleController.HasExistingSave()` (`:396`), which returns
  `svc.State.HeroClass.ToNullable().HasValue || svc.State.Onboarded`. It is what gates the
  CONTINUE button today (`:326`, `bool hasSave = HasExistingSave();`) and is already traced at
  `:215` (`saveExists={HasExistingSave()}`). **Reuse it — do not invent a second notion of
  "has a save".**
- When `HasExistingSave()` is **false** (a genuine fresh install), START NEW keeps today's
  behaviour: straight through, no prompt. A confirm on an empty save is friction for nothing.
- When it is **true**, show a confirm sheet. **The repo already has the idiom for exactly this
  situation — cite and follow it:** `Assets/_Modules/Core/UI/TutorialSkipUi.cs`, which wraps an
  irreversible action in `ElarionUiKit.ConfirmModal` (`:30`, `:77`) behind a static
  `Show(Action onConfirmedSkipAll)` (`:92`) / `Hide()` (`:106`). Its own header states the
  principle this ticket is about, at `:22-23`: *"an accidental skip is unrecoverable-feeling,
  so distance from Confirm is a requirement, not a preference."* A wipe is not
  unrecoverable-*feeling*; it is unrecoverable.
- Copy must name the loss concretely (the realm, the hero, the town) rather than say "are you
  sure". The destructive face is the non-default one and must not sit under the finger that
  just pressed START NEW.
- The existing `_splashActive` latch (`:410-411`) stays; it is a double-press guard and is not
  a substitute for the confirm.

### 2.2 A one-slot local backup, written BEFORE the wipe

So that a wipe is reversible from the device with no network and no backend.

- Immediately before `ResetToNewGame()` mutates anything, copy the current signed save body
  **and its signature** to a second slot (e.g. `dotr-save.prev` + `dotr-save.prev.sig`,
  derived from `SaveSchema.PlayerPrefsKey` and `SaveSchema.SignatureKeySuffix` — **derive both
  names, never hardcode a second literal**).
- The backup is written through the same `ISaveProvider` seam
  (`Assets/_Modules/Core/State/ISaveProvider.cs`, `LocalSaveProvider.cs`) so the round-trip and
  the signature rules are identical to the live slot. Do **not** add a parallel storage path.
- Exactly **one** generation is kept — a new backup overwrites the old. This is a
  wipe-undo, not a save-history feature.
- Trace it: a `FlowTrace.Step` naming the byte length backed up, so the log proves the copy
  happened *before* the `ResetToNewGame: ENTER` line. A backup with no trace line is a backup
  nobody can prove exists at 3am.
- Restoring it is deliberate and explicit (owner/CLI action), not automatic — an auto-restore
  would fight the new-game the player may genuinely have wanted.

### 2.3 The epoch guard must not refuse a deliberately restored row — DESCRIBE ONLY, DO NOT IMPLEMENT

Today a restore is blocked by design: `ApplyBackendState` refuses a row whose reset epoch is
older than the local one (`GameStateService.cs:1406`, `:2312`), and the push side answers
`409 SAVE_RESET_STALE` (`:2936`) — which the code itself documents as **not** self-healing.
That guard is correct for its purpose (WO-1598: stopping a stale device from clobbering a
newer town) and **must not be weakened to make restores work**.

Write up, in this WO's RESULT or a follow-up spec, the **server/CLI restore path**: how an
operator deliberately re-blesses a known-good backend row — the row is identified by its reset
epoch (here `1789060460`) — so the client accepts it once, without opening a hole that lets an
ordinary stale device do the same thing by accident. Name the endpoint/table and the
authorisation step. **No code in this ticket.**

---

## 3. RED-first pins (write the failing test before the fix)

1. **`ResetToNewGame` is not reachable from the title without the confirm.**
   A regression that drives the title's START NEW entry with `HasExistingSave()` true and
   asserts the save is **still intact** until the confirm's positive action is invoked — and
   that it *is* wiped after it. It must FAIL against today's `TitleController.cs:417`. Pin the
   fresh-install case too: with `HasExistingSave()` false, no confirm is shown and the flow is
   unchanged.
2. **Save-backup round-trip.**
   Given a populated signed save, run the reset; assert the backup slot holds the **pre-reset
   body and a signature that validates**, that restoring it reproduces the original state
   byte-for-byte through the normal load path (`SaveMigrator` → `SaveSchema.Validate`), and
   that a second reset leaves exactly one generation. Assert the backup write is ordered
   **before** the first mutation.
3. Register both in `Assets/Editor/Regression/DataRegression.cs` per the existing convention
   and judge the run by the `REGRESSION_OK <n>/<n> suites` marker on a **fresh** log, never the
   exit code.

---

## 4. Device-lane rule (record this; it is why the incident was possible)

Written here so the next seat driving a device inherits it:

- **A floating-overlay app can eat or redirect scripted taps.** An app holding
  `SYSTEM_ALERT_WINDOW` draws over the game and its bubble intercepts touches. On this device
  `recorder.screenrecorder.videoeditor` was doing exactly that: it stole focus, pushed an
  interstitial ad over the game, and its task-removal killed the game process twice
  (`ActivityManager: Killing <pid> ... (adj 905): remove task`).
- **Before tapping, check for overlay windows** — `adb shell dumpsys window | grep -i
  SYSTEM_ALERT_WINDOW` (and `dumpsys window windows` for the window list). If any non-system
  overlay is present, clear it before driving the UI, and say in the hand-back that you did.
- **Never tap the title row blind while a save exists.** CONTINUE, START NEW and PLAY INTRO sit
  in one row at the same `y`; START NEW is the middle face. Every tap must be preceded by a
  fresh screencap that is actually read, and by a `dumpsys activity activities |
  grep topResumedActivity` check that the game — not the launcher — is foreground. A blind
  `launch → sleep → tap` chain is what put stray taps on the launcher, on Google Sheets, and on
  the title row during this session.
- **Judge foreground before every tap, not once per sequence.** The app can be killed and
  relaunched underneath a running chain.

---

## 5. What NOT to touch

- **Do not weaken or remove the WO-1598 reset-epoch guard** (`GameStateService.cs:844`, `:1406`,
  `:2312`, `:2936`). It is protecting a real failure mode. §2.3 is a *description*, not a change.
- **Do not bump `SaveSchema.CurrentVersion`** (currently `41`, `SaveSchema.cs:41`). The backup
  slot stores an existing serialized body under a second key; the wire shape does not change and
  no migrator step is needed.
- **Do not rename or repoint `SaveSchema.PlayerPrefsKey`** (`"dotr-save"`), and do not hardcode a
  second literal key — derive the backup slot name from the existing consts.
- **Do not change `ResetToNewGame`'s clearing behaviour** (`GameStateService.cs:1210`) — what it
  wipes is correct; *when* it is allowed to run is the defect.
- **Do not touch the daily-chest or hero-select flows.** They appear in the timeline as
  collateral of the same stray input, not as defects.
- **Do not touch the device.** It is the owner's Seeker and the incident is unresolved pending
  her ruling.
- Do not remove any `FlowTrace` line quoted in this WO — §12 of `CLAUDE.md` makes
  instrumentation permanent. The trace is the only reason this was diagnosable at all.

---

## 6. Acceptance criteria

- [ ] With an existing save, pressing START NEW shows a confirm sheet built on
      `ElarionUiKit.ConfirmModal`, following the `TutorialSkipUi` idiom; the save is untouched
      until the destructive face is deliberately chosen.
- [ ] With no existing save (`HasExistingSave()` false), START NEW behaves exactly as today.
- [ ] A one-generation local backup of the previous signed save + signature is written through
      `ISaveProvider` **before** `ResetToNewGame` mutates anything, and a `FlowTrace` line proves
      the ordering.
- [ ] Restoring the backup reproduces the prior state through the normal load path.
- [ ] Both RED-first pins exist, failed before the fix, and pass after; registered in
      `DataRegression` and judged by the marker on a fresh log.
- [ ] The server/CLI restore path for a deliberately re-blessed backend row is written up. No
      code shipped for it, and the epoch guard is unchanged.
- [ ] `COMPILE_GATE_OK` + `python tools/gate_brace.py` clean on every `.cs` touched.
- [ ] This WO's `**Status:**` line flipped and a `.RESULT.md` written in the same commit as the
      work; `python tools/board_build.py` regenerated.

---

## 7. Evidence index

All under `D:\EoA\Builds\device-frames\`:

- `2026-09-10_1220_363866_raid_10_after_continue.png` — the realm before (Thrain, Heartfire 3/3)
- `2026-09-10_1233_363866_raid_20_town_gold_before.png` — after the wipe (Grom, empty field)
- `2026-09-10_1224_363866_raid_14_town.png` — the screen-recorder overlay bubble, expanded
- `2026-09-10_1229_363866_raid_18_title.png` — the HeroSelect carousel reached post-wipe
- `2026-09-10_1209_363866_raid_logcat_stream.txt` — carries the wipe at `:133058`
- `2026-09-10_1227_363866_raid_logcat_stream2.txt` — cloud fail-closed lines, hero commit
- `2026-09-10_1235_363866_raid_logcat.txt` — full `logcat -d` dump

Device proofs for WO-1637/1638/1639/1640/1646/1647 were **NOT** obtained and remain unproven —
`[Flow:Raid] capability edge -> NOT CAPABLE (flag=True, building=False, lock=NoBarracks)` at
12:24:24 shows raids are unreachable on the post-wipe save. `[wo1646-touch]` and
`[wo1646-label]` returned **0 hits** because the deploy HUD never built.

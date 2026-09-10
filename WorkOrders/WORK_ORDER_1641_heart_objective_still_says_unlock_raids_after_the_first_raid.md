# WO-1641 - The Heart plate says "Train 2 troops to unlock Raids" 5 minutes after the player finished a raid

**Status:** IMPLEMENTED - awaiting gate (lane HEART-COPY 2026-09-10)
**Minted:** 2026-09-10 (CLI minting lane, main-line banner; number block 1637-1642 pre-assigned by the
lead, banner bumped 1637 -> 1643 in the SAME edit)
**Silo / Lane:** HUD copy + the readiness bar it names. `Assets/_Modules/Core/HudModel/HudStateCopy.cs`
(the sentence) and `Assets/_Modules/Village/Troops/ArmyReadiness.cs` (the number). NOT the raid door.
**Severity:** P2 felt-copy, and it is the FIRST thing the player reads on returning from a raid. The
plate tells someone who has just raided that raids are locked.
**Type:** EXISTING system. Every part works; two of them disagree about what the sentence means.
**Owner words:** *"i have mentioned it in testing that it feels incomplete and not polished"*
(2026-09-10). This is one of the six items the DEVICE-RAID lane named in that pass; see
`WorkOrders/WORK_ORDER_1632_raid_arena_exterior_boundary_ring.md:575-576`.

**STOP: ONE HALF OF THIS TICKET IS AN OWNER RULING, NOT A LANE DECISION.** Section 3 asks the question and
section 4 implements ONLY the half that is unambiguous. Do not pick the other half.

---

## 1. What was measured (device frames + device logcat, both read 2026-09-10)

Build 363529 on the Seeker, the same session that produced the raid frames. Logcat:
`Builds/device-frames/2026-09-10_raid_logcat_stream.txt`.

### 1a. The two frames, side by side

| Frame | Heart of Elarion plate, line 2 | When |
|---|---|---|
| `Builds/device-frames/2026-09-10_0602_town.png` | `Prepare the realm for the next wave.` | BEFORE the raid |
| `Builds/device-frames/2026-09-10_0625_back_in_town.png` | `Train 2 troops to unlock Raids` | AFTER the raid |

Both frames are 2670x1200, both show the plate at the top-left under the hero nameplate, both show
line 3 as `Heartfire 3/3 (raids)`. Between them the player fielded 8 Footmen against The Forsaken
Camp and played the raid end to end.

**The brief that produced this ticket called the copy "stale". It is not stale - it FLIPPED, and it
flipped in the correct direction for the rule it is enforcing.** That correction matters, because a
lane hunting a refresh bug would find nothing: the repaint fired, on time, with fresh state.

### 1b. The logcat names the exact instant, and the emitter prints its own inputs

`HudKitController.RepaintHeartObjective` emits one line per transition
(`Assets/_Modules/HUD/Kit/HudKitController.cs:5370-5374`). Every occurrence in this session, in order:

| Log line | Time | Painted sentence | required | deployable | ready |
|---|---|---|---|---|---|
| `:7844` | 05:58:18.271 | `Prepare the realm for the next wave.` | 3 | 8 | True |
| `:28080` | 06:02:18.183 | `Prepare the realm for the next wave.` | 3 | 8 | True |
| `:60415` | 06:07:04.478 | `Prepare the realm for the next wave.` | 3 | 8 | True |
| `:60442` | 06:07:04.586 | `Train 6 troops to unlock Raids` | **10** | 4 | False |
| `:63229` | 06:07:35.016 | `Train 6 troops to unlock Raids` | 10 | 4 | False |
| `:89855` | 06:12:05.381 | `Train 2 troops to unlock Raids` | 10 | 8 | False |

`:89855` is the paint the player is looking at in frame `0625`. Its full text:

    [Flow:HudKit] objective -> 'Train 2 troops to unlock Raids' (raidCapable=True, barracks=True,
    deployable=8, queued=0, required=10, ready=False, lock=None, hostile=False, trainNeeded=2)

`required` moved from 3 to 10 between `:60415` and `:60442` - **108 milliseconds**.

### 1c. What moved it, in its own words

`Builds/device-frames/2026-09-10_raid_logcat_stream.txt:60381`, at 06:07:04.420, 166 ms before the
flip:

    [Flow:Raid] FIRST RAID COMPLETED (stars 0) - everCompletedRaid false->true. The raid door now
    requires the FULL army cap instead of the softened first-raid slot floor, permanently.

That is `RaidDeployController.ReconcileRaidEnd`
(`Assets/_Modules/Village/Troops/RaidDeployController.cs:1477-1483`), the single writer of the flag.

**So the system did exactly what it says it does.** There is no bug in the flag, the publisher, the
change-detect or the repaint. The bug is that the sentence the flag now selects is written for a
player who has never raided.

### 1d. The chain, read at source

- `ArmyReadiness.cs:121-135`: `int required = everCompletedRaid ? cap : FirstRaidMinDeployableSlots;`
  then `Ready = deployableSlots + queuedSlots >= required`. `FirstRaidMinDeployableSlots = 3`
  (`ArmyReadiness.cs:51`); `cap` is `ArmyStorage.MaxArmySize`, default 10
  (`Assets/_Modules/Core/State/ArmyStorage.cs:43`).
- `BuildTimerService.cs:2214-2221` relays the snapshot verbatim to the Core seam, including
  `s.RequiredSlots`, on a 1 s heartbeat (`BuildTimerService.cs:188-191`) and on every queue mutation.
- `RaidEntryGate.cs:48-52` states the contract in the code: *"Surfaces that SAY a number ('Train 3
  troops to unlock Raids') read THIS, never CapSlots, or the copy disagrees with the gate that
  produced it."* The copy obeys it.
- `HeartObjectiveCopy.Resolve` (`Assets/_Modules/Core/HudModel/HudStateCopy.cs:93-101`) is the whole
  decision:

      if (!army.Ready)
      {
          int required = army.RequiredSlots > 0 ? army.RequiredSlots : army.CapSlots;
          int have = Math.Max(0, army.DeployableSlots) + Math.Max(0, army.QueuedSlots);
          troopsNeeded = Math.Max(1, required - have);
          return TrainTroops(troopsNeeded);
      }

  `10 - 8 = 2`. The arithmetic is right. **`!Ready` is the only branch, and it has only one sentence.**

### 1e. Where the words live

Format string with a named count, pluralised, in three data homes (all three carry the identical pair):

- `Assets/Resources/Data/Canonical/en.json:394-395`
- `Assets/StreamingAssets/Data/Canonical/en.json:394-395`
- `Assets/Localization/Tables/GameStrings_en.asset:1619` and `:1624`

Keys declared at `Assets/_Modules/Core/UI/HeartHudText.cs:10-11`
(`hud.heart.objective.trainOne` / `hud.heart.objective.trainOther`), bound as
`LocalizedText<HeartTroopsArguments>` at `:22` / `:24`.

---

## 2. What is NOT claimed

- **NOT a refresh defect.** The repaint is change-detected on `(hostile, capable, lock, army.Version)`
  (`HudKitController.cs:5348-5351`) and `Version` bumps only on a value change
  (`RaidEntryGate.cs:76-95`). The trace at `:60442` proves the repaint ran with fresh state 108 ms
  after the state moved. Do not open the poll.
- **NOT a divergent-predicate defect.** The copy and the gate read the same `RequiredSlots`. That was
  the working hypothesis in the brief and the trace refutes it.
- **NOT proven: whether the full-cap door is the intended DESIGN.** Section 3.
- **NOT proven: which of the three data homes serves the device at runtime.**
  `LocalizedText<T>.Resolve` was not followed to its loader. Any copy change must edit all three
  until someone proves otherwise; that proof is not in this ticket.
- **NOT claimed that the Journey RAIDS card is the same defect.** `..._0603_journey_deck.png` reads
  `Army 8 / 10 . train to open a camp` at a moment when `required=3, ready=True` (log `:7844`) - a
  SECOND surface, phrased against the cap, disagreeing with the gate at that instant. It is SEEN and
  RECORDED here, not diagnosed, and it is not in this ticket's scope. Raise it in the hand-back.
- **NOT claimed the raid was refused.** The player raided with 8 while the Journey card said "train to
  open a camp". Whether the door greys, dims or refuses is not evidenced in these frames.

---

## 3. THE OWNER QUESTION - ask it, do not answer it

The trace line at `:60381` says the design out loud: **after the first raid, the raid door requires the
FULL army cap, permanently.** With a cap of 10 and a raid that fields 8, the plate will read
"Train N troops to unlock Raids" for most of a player's ordinary life, including immediately after a
successful raid.

Two separate things need the owner:

1. **Is the full-cap-forever door intended?** (`ArmyReadiness.cs:121`.) It is a balance/retention
   ruling, it is not the lane's, and section 4 does not touch it.
2. **What should the plate say when the army is under the bar but raids are already unlocked?** The
   current sentence claims a lock that no longer exists. Candidate shapes only, for the owner to pick
   from or reject - the lane picks none of them on its own:
   - keep one sentence, change the verb ("Train 2 troops to field a full army");
   - two sentences keyed off `everCompletedRaid`, "unlock Raids" only before the first raid;
   - say nothing at all in this branch and let the plate fall through to the wave line.

Route this through the owner with `AskUserQuestion` BEFORE writing copy. Per CLAUDE.md sec.2 the owner
makes all final creative decisions, and this is a player-facing sentence.

---

## 4. The fix - only the half that needs no ruling

### Step 1 - INSTRUMENT (do this first)

The decisive line already exists and already carries every input (`HudKitController.cs:5370-5374`).
**Do not add a second one.** What it does NOT carry is the fact that selected the bar:

Add `everCompletedRaid` (or the equivalent already on the snapshot) to `ArmyReadiness.Snapshot` and
print it in that same existing line, so a future reader can tell "under the soft floor" from "over
the soft floor, under the cap" without correlating two tags across 60,000 log lines. One field, one
existing trace call, no new tag. Per CLAUDE.md sec.12 it stays in the code afterwards.

Compute the interpolated parts into locals before building the string - the gate's brace scanner has
no interpolated-string model (CLAUDE.md sec.1).

### Step 2 - the copy, AFTER the ruling in sec.3

Whatever the owner picks:

- The new branch is chosen on the same state `Resolve` already receives. **Do not reach for
  `GameState` from `HudStateCopy.cs`** - `DeNelle.HUD` never references `DeNelle.Village`
  (CLAUDE.md sec.5) and `HudStateCopy` is Core presentation. If the branch needs
  `everCompletedRaid`, it rides the snapshot struct (Step 1 already puts it there).
- Add the key to **all three** data homes listed in sec.1e, with both plural forms.
- The sentence must fit the plate. `HudLabelFitRegression` already treats
  `HeartObjectiveCopy.TrainTroops(10)` as a fit candidate (`:2141`); add the new string to that set.

---

## 5. Acceptance

1. **The trace.** A fresh run shows the objective line carrying the new field, and a save with
   `everCompletedRaid=true` and `deployable < cap` paints the NEW sentence. Paste the line.
2. **The pre-first-raid path did not regress.** A save with `everCompletedRaid=false` and 0 troops
   still reads `Train 3 troops to unlock Raids` - that exact string is pinned twice, see sec.6.
3. **All three data homes agree.** Show the three file:line pairs after the edit.
4. **The frame.** A town frame at the same aspect as `..._0625` showing the plate. Paste what line 2
   reads. Per the owner's standing rule, *"I want images to verify anything that is a viewable
   issue"*.
5. **Brace and NUL checks** on every `.cs` touched, including `python tools/gate_brace.py` on each
   (CLAUDE.md sec.1 - the gate counts differently from the raw one-liner).

---

## 6. Pins - what must not move

- `Assets/Editor/Regression/HudLabelFitRegression.cs:2114-2116` - `Train 3 troops to unlock Raids`
  for `RequiredSlots 3 / 0 fielded`; `:2121-2124` the 1-deployable case; `:2126-2129` the legacy
  `RequiredSlots 0` fallback to cap; `:2138-2140` hard equality on `TrainTroops(1)`.
- `Assets/Editor/Regression/SmartArgumentRegression.cs:29-31` - hard equality through the live
  localization path on `Train 3 troops to unlock Raids`.
- `Assets/Editor/Regression/FirstRaidSoftGateRegression.cs:75-125` - the WO-823 seven-gate oracle:
  schema triple, migration derivation, wire round-trip, gate math, single source, bypasses removed,
  ONE writer. **This ticket adds no second writer of `everCompletedRaid` and no bypass.**
- `RaidEntryGate.cs:48-52` - the contract that copy reads `RequiredSlots`, never `CapSlots`. The fix
  keeps it. A sentence that names the cap directly breaks it.
- `RaidDeployController.cs:1477-1483` - the single writer. Read-only in this lane.
- `HudActionBarModel.cs:315-316` - `ArmySnapshot` is a passthrough. Do not make it compute.

**Coverage gap to close, not to ignore:** no existing case drives `Resolve` with
`RequiredSlots == CapSlots` on an `everCompletedRaid = true` fixture, which is exactly why this
shipped. Whatever sentence the owner picks, add that fixture as a case.

---

## 7. What NOT to touch

- **`ArmyReadiness.cs:121` - the threshold selection.** That is the sec.3 ruling. Not this lane.
- The raid door itself, `RaidEntryGate` publishing, `BuildTimerService.PublishStatus`, the 1 s
  heartbeat, `RaidCapabilityHudBridge`.
- The Journey RAIDS card subtitle (sec.2, last bullet). Different surface, raise it, do not fix it
  here.
- `RaidCapabilityHudBridge.cs:178` (`"Build a Barracks and train troops to unlock Raids."`) - that is
  the no-Barracks capability line, a different sentence in a different branch. Leave it.
- Do **not** commit. Do **not** push. Hand the diff back (CLAUDE.md sec.11).

---

## 8. Board

This lane owns this ticket. Its hand-back is incomplete until this file's `**Status:**` line is
flipped and
`WorkOrders/WORK_ORDER_1641_heart_objective_still_says_unlock_raids_after_the_first_raid.RESULT.md`
is written, with both paths reported. The lead regenerates `BOARD.html`.

## Device evidence (lead, 2026-09-10 08:14, APK 2026.09.10.363660)

`Builds/device-frames/2026-09-10_0814_363660_town_dock.png` (2670x1200, opened): the heart plate reads "Train 2 troops for the next raid" (8 deployable of cap 10, everCompletedRaid true). Owner felt-verify closes.

## OWNER RULING (2026-09-10 12:16)

The full-cap raid door after the first raid (ArmyReadiness.cs:121) is INTENDED; the line "Train N troops for the next raid" stands. Both open items closed; owner felt-verify remains the close.

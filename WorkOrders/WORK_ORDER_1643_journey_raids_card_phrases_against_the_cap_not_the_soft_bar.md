# WO-1643 - The Journey RAIDS card says "Army 8 / 10 . train to open a camp" to a player the raid door would have let through

**Status:** IMPLEMENTED - awaiting gate
**Implemented:** 2026-09-10 by the JOURNEY-CARD lane, in worktree `agent-ac6db22cce5d58766` off
`dev@0a7edc6b1`. Two files, both `GATE_BRACE_SUMMARY bad=0 of 2`, 0 NUL bytes:
`Assets/_Modules/Core/HudModel/JourneyDeckSubtitleVM.cs` (paint-time input trace + the no-camp
clause) and `Assets/Editor/Regression/JourneyDeckSubtitleRegression.cs` (one assertion re-pointed,
three preserved, two new pins). **ZERO edit to `BuildTimerService.cs` and `ArmyReadiness.cs`** - the
fraction was not touched, so the lead's WO-1641 3-way merge is unaffected.
⚠ **The copy is PROVISIONAL: the sec.3 owner ruling is still owed** - see the RESULT, sec.5.
⚠ **sec.4 Step 1's premise was FALSE at HEAD and is corrected in the RESULT** - `SetRaidOpenCampCount`
has traced since WO-1404 (`PostureSignals.cs:423`); the real gap was that it is change-only.
**RESULT:** `WorkOrders/WORK_ORDER_1643_journey_raids_card_phrases_against_the_cap_not_the_soft_bar.RESULT.md`
**Minted:** 2026-09-10 by the HEART-COPY lane, at the lead's instruction on the WO-1641 hand-back
(main-line banner bumped **1643 -> 1644** in the SAME edit).
**Silo / Lane:** HUD copy. `Assets/_Modules/Core/HudModel/JourneyDeckSubtitleVM.cs` (the sentence)
and the ONE seed in `Assets/_Modules/Village/Buildings/BuildTimerService.cs` (the numbers).
**NOT the raid door, NOT the open-camp predicate.**
**Severity:** P2 felt-copy. Same family as WO-1641 - a second surface saying a number the gate did
not judge against - but a **DIFFERENT producer, a different pair of numbers and a different remedy
clause**, so it is a separate ticket and not a follow-on edit.
**Type:** EXISTING system. Every part works. Three of them disagree about what "enough army" means.

**Provenance:** SEEN and RECORDED by the WO-1641 lane (that ticket's sec.2, last bullet: *"raise it in
the hand-back"*, explicitly out of its scope). Everything below was **re-read at source on 2026-09-10**
by this lane - the frame opened, the log lines opened, every cited file opened. Nothing is carried over
on trust.

---

## 1. What was measured

Build 363529 on the Seeker, the same session that produced the WO-1641 evidence.

### 1a. The frame

`Builds/device-frames/2026-09-10_0603_journey_deck.png` (2670x1200), opened and read. The JOURNEY
panel's second card:

| Card | Title | Subtitle, verbatim |
|---|---|---|
| left | `QUESTS` | `0 active . 0 ready to claim` |
| right | `RAIDS` | `Army 8 / 10 . train to open a camp` |

The panel header above both reads `Your quests, and the camps your army can raid.`

### 1b. What the gate thought at that moment

`Builds/device-frames/2026-09-10_raid_logcat.txt`, opened at source:

- `:28080` - 06:02:18.183, the closest objective paint before the 06:03 frame:
  `objective -> 'Prepare the realm for the next wave.' (raidCapable=True, barracks=True,
  deployable=8, queued=0, required=3, ready=True, lock=None, hostile=False)`
- `:2892` - 05:57:08.957, the fill publish the card is reading:
  `[Flow:HudKit] army fill -> 8 / 10`

**`ready=True`.** The raid door was OPEN. The Heart plate one row away said so
(`Prepare the realm for the next wave.`). The RAIDS card, at the same instant, told the player to
train.

### 1c. Three different pairs of numbers, and that IS the ticket

`ArmyReadiness.Compute` (`Assets/_Modules/Village/Troops/ArmyReadiness.cs`) decides readiness as
**`DeployableSlots + QueuedSlots >= RequiredSlots`** - healthy slots plus in-flight training, against
the WO-823 soft bar.

`BuildTimerService.PublishArmyStatus` then seeds the card from the SAME snapshot but with
**different fields** (`BuildTimerService.cs:2236-2237`):

    DeNelle.Core.Diagnostics.Guard.Try("HudKit", "publish army fill", () =>
        DeNelle.Core.HudModel.PostureSignals.SetArmyFill(s.RosterSlots, s.CapSlots));

So the card's fraction is **RosterSlots / CapSlots**. Against the gate's
`(Deployable + Queued) / Required`, **all three terms differ**:

| | Numerator | Denominator | At the 0603 frame |
|---|---|---|---|
| The gate | `DeployableSlots + QueuedSlots` (wounded EXCLUDED, queued COUNTED) | `RequiredSlots` (soft bar) | `8 + 0 >= 3` -> **READY** |
| The card | `RosterSlots` (wounded INCLUDED, queued not counted) | `CapSlots` | `8 / 10` -> **reads as short** |

⚠ **This is NOT merely "it names the cap".** A wounded roster makes the numerator too HIGH and a
training queue makes it too LOW, independently of the denominator. Any fix that swaps only
`CapSlots -> RequiredSlots` leaves two of the three disagreements in place.

**The contract this breaks is written in the code**, `Assets/_Modules/Core/UI/RaidEntryGate.cs:46-53`:
*"Surfaces that SAY a number (\"Train 3 troops to unlock Raids\") read THIS, never CapSlots, or the
copy disagrees with the gate that produced it."* The Heart plate obeys it. This card does not.

### 1d. The remedy clause is about CAMPS, not about the army - and nothing says so

`Assets/_Modules/Core/HudModel/JourneyDeckSubtitleVM.cs:22-25` is the whole composer:

    RaidsSubtitle = "Army " + armyUsed + " / " + armyCap + " . " +
        (openCamps > 0
            ? openCamps + (openCamps == 1 ? " camp open" : " camps open")
            : "train to open a camp");

**The branch is `openCamps > 0`.** It has no army input at all. `openCamps` comes from
`PostureSignals.RaidOpenCampCount`, published by `BuildTimerService.PublishJourneyOpenCamps`
(`:2295-2318`), which counts unlocked camps whose `RaidSelectionVM.GarrisonCount(def) <=
deployableBodies`.

So `train to open a camp` is **structurally correct English about camps** that lands immediately after
an army fraction and is therefore read as *"your army is 8 of 10, train more"*. Two true facts
concatenated into one false sentence. The composer is doing exactly what it was written to do -
**this is a copy-shape defect, not a logic bug**, and the ticket must not be RCA'd as if a count were
wrong.

## 2. What is NOT claimed

- **NOT claimed the raid was refused.** No frame or log line in this session shows a refusal. The
  player later raided. Whether the RAIDS card's door greys, dims or refuses is **not evidenced** and
  must not be asserted by the implementing lane.
- **NOT claimed `openCamps` was wrong.** Its value at the frame was not captured -
  `SetRaidOpenCampCount` publishes change-only and emits no trace line of its own, so the 0603 value
  is **unproven**. The sentence's SHAPE is the defect regardless of the count.
- **NOT claimed `RosterSlots` is the wrong number for every surface.** `BuildTimerService.cs:2231`
  states it is deliberately the count "a player sees in Manage". The defect is using **that** number
  in a sentence whose neighbour clause implies the RAID door.
- **NOT the same defect as WO-1641** and not fixed by it. WO-1641 changed
  `HeartObjectiveCopy.Resolve`; it did not touch `PostureSignals.SetArmyFill`,
  `JourneyDeckSubtitleVM` or any camp count.
- **NOT proven what the sentence SHOULD say.** Section 3.

## 3. The owner question - ask it, do not answer it

The card must carry two facts in roughly forty characters: **how full the army is** and **whether any
camp is takeable**. Today they are welded with a full stop and the second reads as the remedy for the
first.

**What should the RAIDS card say when the army is READY but no camp is open?** Candidate shapes only,
for the owner to pick from or reject - the implementing lane picks none of them on its own:

- keep both facts, make the second unambiguous (`Army 8 / 10 . no camp in reach`);
- lead with the door (`Ready to raid . no camp in reach` when `ready`, the army line when not);
- drop the army fraction from this card entirely and let the Heart plate own army state.

Route through the owner with `AskUserQuestion` BEFORE writing copy - CLAUDE.md sec.2, all final
creative decisions are hers. **If she also wants the fraction itself corrected, that is a SECOND
ruling** (which numerator: roster, or deployable+queued?) and it is the one with a live pin against it
(sec.6).

## 4. The fix

### Step 1 - INSTRUMENT FIRST (CLAUDE.md sec.12)

`PostureSignals.SetArmyFill` already traces (`PostureSignals.cs:405`, `army fill -> 8 / 10`).
`SetRaidOpenCampCount` (`PostureSignals.cs:418-424`) traces **nothing**, which is why the 0603 camp
count is unprovable today (sec.2).

Add the camp count to the EXISTING fill trace line, or give `SetRaidOpenCampCount` one `FlowTrace.Step`
of its own - **one line, not both**, and it stays in the code afterwards (sec.12, never strip).
Compute interpolated parts into locals first; the gate's brace scanner has no interpolated-string
model (CLAUDE.md sec.1).

**Do not open the copy edit until a trace names the camp count at the moment the card paints.**

### Step 2 - AFTER the ruling in sec.3

- The composer is **pure and already fixture-driven** (`JourneyDeckSubtitleVM(int, int, int, int, int)`)
  - drive every new branch from a fixture in the suite, never from a live scene.
- **Any player-visible string is localized.** Note that the strings at `:21-25` are **hardcoded
  English**, not `LocalText` keys. If the ruling changes the words, the honest fix mints keys and adds
  them to **all 10 policy-listed locales in BOTH canonical copies** (20 JSON files -
  `LocaleParityRegression` requires exact parity), then regenerates the Unity tables with
  `Defenders > Week 1 > Build Localization`. **Budget for that; it is not a two-file edit.**
  Raising the localization gap as its own ticket instead is a legitimate answer - say which you chose.
- If the ruling touches the FRACTION, change it at the **seed** (`BuildTimerService.cs:2237`), not in
  the VM - one producer, per the comment already at `:2226-2235`.

## 5. Acceptance

1. **The trace.** A fresh run shows the camp count in the log at the instant the card paints. Paste
   the line.
2. **The frame.** A Journey-deck frame at the same aspect as `..._0603_journey_deck.png`, showing the
   card. Paste what the subtitle reads. Owner standing rule: *"I want images to verify anything that
   is a viewable issue."*
3. **The contradiction is gone**: a fixture with `ready=True` must not produce a sentence a player
   reads as "train more troops". State the fixture and the string.
4. **`JourneyDeckSubtitleRegression` still passes**, with its assertions UPDATED rather than deleted
   (sec.6).
5. **Brace and NUL checks** on every `.cs` touched, including `python tools/gate_brace.py` on each
   (CLAUDE.md sec.1 - the gate counts differently from the raw one-liner).

## 6. Pins - what must not move

- `Assets/Editor/Regression/JourneyDeckSubtitleRegression.cs:39` - `RequireContains(fixture.RaidsSubtitle, "3 / 10", ...)`;
  `:41` - `"1 camp"`; `:53` - `"Army 0 / 10"`; `:54` - `"train to open a camp"`.
  **Four live assertions pin the exact shape this ticket changes.** They are the oracle, not an
  obstacle: whatever the owner picks, **update them in the same change** so the new shape is pinned
  and the old one cannot come back. Deleting a case instead of re-pointing it is not acceptable.
- `:20-22` - the deck must bind `journey.RaidsSubtitle` and must not restore
  `"Choose a camp and deploy your army"`. Keep.
- `:26,29,32` - the open-camp predicate, its escalation lock and its cache. **Read-only in this lane**
  (`GarrisonCount(def) <= deployableBodies`, the `IsLocked` guard, the unchanged-input early return).
- `:45-49` - no `"..."` in either composer, and neither old card verb returns.
- `BuildTimerService.cs:2306-2314` - the WO-1541 note that `RaidOpenCampCount` ("takeable now") and
  `RaidNextCamp` ("who you are training for") answer **different questions and must not be merged**.
  A fix that folds one into the other re-opens WO-1541.
- `RaidEntryGate.cs:46-53` - the read-RequiredSlots-never-CapSlots contract. A fix must move TOWARD
  it, never further away.
- `RaidsDiscoverabilityRegression` D5 - readiness may decide DIM and REFUSAL, **never visibility**.
  The card must not start hiding itself.

## 7. What NOT to touch

- **`ArmyReadiness.cs:121`** - the full-cap-forever threshold. That is WO-1641 sec.3's open owner
  ruling. Not this lane, in either ticket.
- **`HeartObjectiveCopy` / `HudStateCopy.cs`** - WO-1641's surface. A patch that lands in both files
  has merged two tickets.
- The raid door, `RaidEntryGate` publishing, the 1 s heartbeat, `RaidCapabilityHudBridge`.
- The QUESTS card subtitle and `DailyQuestService.ClaimableCount` (WO-1521's single-count fix).
- Do **not** commit. Do **not** push. Hand the diff back (CLAUDE.md sec.11).

## 8. Board

The implementing lane owns this ticket. Its hand-back is incomplete until this file's `**Status:**`
line is flipped and
`WorkOrders/WORK_ORDER_1643_journey_raids_card_phrases_against_the_cap_not_the_soft_bar.RESULT.md`
is written, with both paths reported. The lead regenerates `BOARD.html`.

## Device evidence (lead, 2026-09-10 08:14, APK 2026.09.10.363660)

`Builds/device-frames/2026-09-10_0814_363660_town_dock.png` (2670x1200, opened): the Journey deck RAIDS card reads "Army 8 / 10 . no camp in reach" (`_0817b_363660_journey_deck.png`) while logcat 08:17:45 shows the raid door itself opened the drillmaster panel (required 10 on this save). Owner felt-verify closes.

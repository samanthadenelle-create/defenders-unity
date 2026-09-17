# WORK ORDER 1802 — Make the raid door obvious after founding

**Status:** IMPLEMENTED, NOT YET GATED

**Owner ruling 2026-09-16 (verbatim):** *"make the raid door obvious after founding"*

**Follow-up owner direction, same day (verbatim):**
- *"part of it is the heart fire people don't understand that heart fire is raids. I think maybe we
  need to keep that simple or somehow tie them together. We should have some kind of helper that says
  try building a barracks or click here to put your barracks something that we should assist them."*
- *"we need to create a desire to raid so we need to announce early what the rewards are and we should
  start them with a starter army so they can at least do one for free"*
- Starter army becomes **TEN** troops (five footmen + five archers) — **sibling lane WO-1803** owns the
  grant and the `raid.starterArmySize` default. This lane therefore hardcodes **no** troop count.

---

## 1. The evidence (not a theory)

`docs/LIVE_PLAYERS_TRIAGE_2026-09-16.md`:

- **ZERO** players launched a raid that day; **three** in seven days.
- **FOUR** ids reached `founding_path_selected`.
- For each of them the starter-army grant fired `raid_funnel_barracks_unlocked` **and**
  `raid_funnel_army_trained` in the **same second**.
- **None** emitted `raid_funnel_first_raid_attempted`.

So the door was open, the free squad was in the barracks, and the funnel died precisely at the step
where a player has to **find** the door. The raid is the game's hook and act 2 of the hackathon video.

**What the shipping game said about the door, and why it was not enough:** exactly one toast, at the
grant — `StarterArmyGrant.GrantToastFor` → *"Your first squad is ready - N Footmen, free. Open Journey,
then Raids."* One toast, dismissed by the next tap, never repeated, with no spotlight and no door.

---

## 2. Scope of this lane

| # | Deliverable | Owner direction it serves |
|---|---|---|
| A | ONE shared predicate for "what stands between this player and a raid", fail-closed | all of them |
| B | Helper chain: **build a Barracks → train troops → raid**, one step at a time, ordered by what is missing | *"some kind of helper ... assist them"* |
| C | Raid-door prompt: spotlight walks dock **JOURNEY** face → **Raids** card, with a direct CTA | *"make the raid door obvious"* |
| D | Always-on badge on the Journey dock face **and** the Raids deck card while the door is open and unused | *"make the raid door obvious"* |
| E | Reward + free-army copy composed from the **live projection**, never a literal | *"announce early what the rewards are"* |
| F | Heartfire display copy leads with the verb **Raids** | *"heart fire is raids ... keep that simple"* |
| G | Instrumentation (`raid_door_prompt_shown` / `_tapped`) + regression suite | CLAUDE.md §12, §11B |

---

## 3. Cross-lane contract (read this first if you own a sibling lane)

### WO-1804 — "Enemy Battle Plans" drop hands into this chain

**Entry point name, as built:** this lane **consumes**
`DeNelle.Core.Tutorial.TutorialSignals.BattlePlansRevealed` = **`"battle_plans_revealed"`** — the
planned name, unchanged.

**How to use it:** raise it. Nothing else. `TutorialSignalAdapters` subscribes the bus and, on that
id, **re-arms its own re-raise timer so the helper chain's current step raises on the very next 1 Hz
tick** (`TickRaidDoorReady`). It deliberately does **not** trigger a beat directly:

- The chain's step is chosen by *what is missing*, which only the poll knows. A beat triggered
  straight off `battle_plans_revealed` would have to re-derive the blocker and would drift.
- "Whichever comes first, once" therefore falls out for free: both entry points converge on the same
  poll, and the per-save `tutorial_ctx:` latch dedupes.

WO-1804 does **not** need to edit `TutorialFlow.cs`, `PlayerDeckWorkspace.cs` or any file this lane
touches. It only needs `TutorialSignals.Raise(TutorialSignals.BattlePlansRevealed)`.

### WO-1803 — starter army becomes 10 (5 footmen + 5 archers)

No coordination needed and **no file overlap**. This lane reads the deployable count at compose time
through the `{raid.army.deployable}` dialogue token (→ `RaidEntryGate.ArmyStatus.DeployableSlots`, the
already-published projection), so a knob change from 3 to 10 needs **no edit here**. The regression
case `[no-hardcoded-numbers]` fails the build if any prompt string ever gains a digit.

### WO-1800 / WO-1801 — `PackStore.cs`, `BuildModeController.cs`

**Untouched by this lane.** The Barracks-placing CTA needed no `BuildModeController` change:
`BuildModeController.EnterBuildModeForStructure(string id)` **already exists** (WO-1571, `:500`) and is
exactly the select-by-id seam. The new dialogue verb calls it. Named here so WO-1801's lane can see the
file is not contended.

---

## 4. Ruling conflicts resolved in this lane (say it, do not smuggle it)

1. **The brief named the wrong file for the Heartfire label.** It said `canon-strings.json`. The plate
   string is actually the localization key `hud.heart.heartfire.plate`, in
   `Assets/Resources/Data/Canonical/en.json` (+ `ar/de/es/fr`). `HeartfireCharges.PlateLabel` resolves
   it through `HeartHudText.HeartfirePlate`. Corrected openly rather than silently.
2. **`HeartfireRegression` PIN G moves WITH the new ruling.** PIN G (`:640`) asserted
   `HeartfireCharges.SpendTag` (`"(raids)"`) is a substring of the plate label — the 2026-09-05 ruling
   that the plate must say what a charge *buys*. The 09-16 ruling supersedes the **wording**, not the
   intent: the plate now *leads* with "Raids". The pin is re-pointed to assert the raid word leads.
   `SpendSentence` is **not** touched — PIN H asserts it verbatim in the guide entry and in
   `tut_ctx_heartfire`.
3. **Non-English Heartfire strings are NOT translated by this lane.** `de/fr/es/ar` are listed as
   untranslated in the RESULT. Inventing translations would be worse than leaving them.
4. **`ctx_build_armor`'s note says the nudge-chain order past two beats is an owner creative pin.**
   The 2026-09-16 direction *is* that ruling for the raid branch; cited in the new steps' `_note`s.
5. **An earlier draft of this lane judged the army by `DeployableSlots > 0` and that was wrong.**
   The door's bar is `ArmyStatus.Ready` (→ `ArmyReadiness.RequiredSlots`, which the WO-823 soft gate
   sets to 3 pre-first-raid). One deployable troop satisfies `> 0` and gets **bounced** to the
   drillmaster by `RaidSelectionScreen.Open:342-402`. Corrected before any gate; the reasoning is
   recorded in `RaidDoorReadiness`' header so it cannot be "simplified" back.

---

## 5. Acceptance criteria

- [ ] `gate_brace.py` + NUL clean on every `.cs` touched.
- [ ] `RAID_DOOR_PROMPT_OK` on a fresh log.
- [ ] `COMPILE_GATE_OK` + `REGRESSION_OK <n>/<n>` on a fresh log (committer's gate).
- [ ] Both canonical copies of `tutorial-steps.json` and `dialogues.json` byte-identical, LF counts
      preserved.
- [ ] No prompt/badge string contains a digit outside a `{token}`.
- [ ] **UNPROVEN UNTIL A DEVICE RUN:** the felt result. Needs a **fresh save**: found a town → the
      helper chain appears in order → the raid prompt → a raid is launched.

## 6. What NOT to touch

`PackStore.cs`, `BuildModeController.cs`, `SafeZoneRecovery.cs`, `WaveManager.cs`,
`SmartEnemySpawner.cs`, `RemoteTunables.cs`, `HeroAbilities.cs`, `Troops/**`, `SmartMobileCamera.cs`,
any `.unity`, `DataRegression.cs` (the registration line is written into the RESULT for the
committer), the `CLI_LANES_WO_NUMBERS.md` banner.

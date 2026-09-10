# WO-1641 RESULT - the Heart plate no longer claims an unlock the player already earned

**Status:** IMPLEMENTED - awaiting gate (lane HEART-COPY 2026-09-10)
**Lane:** HEART-COPY, isolated worktree `D:\EoA\.claude\worktrees\agent-ab2eb46c6d45c9fca`
**Base:** `a96bfe332` (dev, fast-forwarded from a clean tree)
**Brief constraint:** EDIT ONLY - no Unity, no gate, no commit. Nothing below is a gate claim.

---

## 1. What was proven this session, at source

| Claim | Evidence, opened 2026-09-10 |
|---|---|
| The latch fires and the bar moves 166 ms later | `Builds/device-frames/2026-09-10_raid_logcat.txt:60381` (06:07:04.420, `FIRST RAID COMPLETED (stars 0) - everCompletedRaid false->true`) then `:60442` (06:07:04.586, `required=10`, was `required=3`) |
| The player reads the unlock sentence after raiding | `Builds/device-frames/2026-09-10_0625_back_in_town.png` opened and read: the Heart of Elarion plate's line 2 is `Train 2 troops to unlock Raids`, line 3 `Heartfire 3/3 (raids)` |
| The repaint is healthy | the `:60442` trace carries fresh state 166 ms after the state moved - WO sec.2 stands, this was never a refresh defect |
| The one branch had one sentence | `HudStateCopy.cs`, `if (!army.Ready)` returned `TrainTroops(n)` unconditionally |
| `FirstRaidSoftGate` could NOT have been reused as the bit | `ArmyReadiness.cs` derives it as `required < cap`, so a cap at or below the softened floor reads `false` on a save that has never raided |
| The seam carried no first-raid bit | `RaidEntryGate.RaidArmyStatus` had five fields, none of them the latch |

## 2. The emitter and the copy builder (WO's ask, cited)

- **Emitter of the number:** `Assets/_Modules/Village/Troops/ArmyReadiness.cs:121` -
  `int required = everCompletedRaid ? cap : FirstRaidMinDeployableSlots;`
  **UNTOUCHED** - it is the sec.3 owner ruling, not this lane's.
- **Relay:** `Assets/_Modules/Village/Buildings/BuildTimerService.cs:2214-2225` (`PublishArmyStatus`).
- **Copy builder:** `Assets/_Modules/Core/HudModel/HudStateCopy.cs`, `HeartObjectiveCopy.Resolve`,
  the `if (!army.Ready)` branch (was `:93-101` at HEAD).
- **Painter + trace:** `Assets/_Modules/HUD/Kit/HudKitController.cs`, `RepaintHeartObjective`.

## 3. What changed

**Step 1 - instrument (WO sec.4 step 1).** The bit now rides the snapshot and prints in the ONE
existing trace line. No new trace call, no new tag.

- `ArmyReadiness.Snapshot.PastFirstRaid` (new field, copied from the `everCompletedRaid` parameter).
- `RaidEntryGate.RaidArmyStatus.PastFirstRaid` (new field) + a 6-arg `PublishArmyStatus` overload;
  the field joins the **change-detect equality**, so a latch flip bumps `Version` and repaints.
- `BuildTimerService.PublishArmyStatus` relays `s.PastFirstRaid`.
- `HudKitController` objective trace gains `pastFirstRaid=<bool>` (string concatenation, not
  interpolation - the gate's brace scanner has no interpolated-string model, CLAUDE.md sec.1).

> ⚠ **THE FIELD IS DELIBERATELY NOT NAMED `EverCompletedRaid`, AND THE NEXT SEAT MUST NOT "FIX" THAT.**
> `FirstRaidSoftGateRegression` gate 7 (`:440-464`) sweeps every runtime file under `Assets/_Modules`
> outside `RaidDeployController` and `Core/State` for the literal `EverCompletedRaid =` and reds on it,
> because a second writer forks the one-owner latch. An object-initializer named that way would read as
> a write and turn a green oracle red for a field that only ever COPIES the value. `PastFirstRaid` is
> the same bit under a name that cannot be mistaken for a writer. The reason is written in-code at both
> declarations.

**Step 2 - the copy.** New key pair, chosen on the same struct `Resolve` already receives; no
`GameState` reach from `DeNelle.HUD` (CLAUDE.md sec.5 asmdef law intact).

- `hud.heart.objective.trainNextRaidOne` = `Train {Troops} troop for the next raid`
- `hud.heart.objective.trainNextRaidOther` = `Train {Troops} troops for the next raid`
- Declared on `HeartHudText` (`KeyTrainNextRaidOne/Other` + two `LocalizedText<HeartTroopsArguments>`),
  surfaced as `HeartObjectiveCopy.TrainNextRaid(int)`.
- `Resolve`: `return army.PastFirstRaid ? TrainNextRaid(n) : TrainTroops(n);` - **same arithmetic,
  different promise.** The pre-first-raid sentence is untouched.

**⚠ THE WORDS ARE THE LANE'S, NOT THE OWNER'S - THEY ARE PROVISIONAL.** WO sec.3 routes the wording to
the owner; the lead authorised the lane to pick in the voice of the neighbouring objective lines and
say so, which this paragraph is. The voice matched: `Defend the realm` /
`Prepare the realm for the next wave.` / `Raids unlock at a Barracks - Build > Realm` - imperative,
names the action and the payoff, no punctuation flourish. `for the next raid` is two characters longer
than `to unlock Raids` at the same troop count, so the plate fit is unchanged in practice and is
measured anyway (below). **If the owner picks a different shape, only the two JSON values move** - the
plumbing, the branch and the pins do not.

## 4. Data homes - all three, and which one actually serves the device

The WO recorded "NOT proven which of the three data homes serves the device". **Proven now, at source:**
`LocalText.TryGet` (`Assets/_Modules/Core/UI/LocalText.cs:139-160`) asks the installed provider first
(the package-backed Unity tables) and **falls back to the canonical JSON** for any key the provider
misses. So the JSON is not a mirror of last resort - it is a live runtime path, and a key present only
in JSON still resolves.

- `Assets/StreamingAssets/Data/Canonical/en.json` - the policy `source`
- `Assets/Resources/Data/Canonical/en.json` - the policy `mirror`
- `Assets/Localization/Tables/GameStrings_en.asset` - **generated, NOT hand-edited (see sec.6)**

`LocaleParityRegression` requires **exact key parity across all 10 policy-listed locales in BOTH
canonical copies**, so the pair was added to **20 JSON files**, not two. Each translation reuses that
locale's own existing `trainOne/trainOther` vocabulary, so no new glyph enters any locale.

Byte-level patch per memory `canonical-json-edits-binary-only-verify-newlines` (a text-mode rewrite
flattened 12 files once). Verified mechanically after the write:

- every file `LF == CRLF` before and after, both `+2` exactly; no NUL bytes
- every file re-parses, and the two new keys round-trip to the intended values
- source == mirror for all 10 locales; key parity vs English exact; no empty values
- every character of every new string already occurs elsewhere in that same locale file
  (glyph-coverage safety, checked programmatically)

## 5. Pins - RED-first, and what did not move

**New, RED against HEAD** (`Assets/Editor/Regression/HudLabelFitRegression.cs`, case 13b):

- fixture `raided` = `{Ready=false, Deployable=8, Queued=0, Cap=10, Required=10, PastFirstRaid=true}` -
  the exact device state of frame `0625`. Asserts the sentence contains **no** "unlock" and still reads
  `Train 2 ` with `n == 2`. **By SOURCE READ of HEAD's single-branch `Resolve`, this returns
  `Train 2 troops to unlock Raids` and both asserts fire - the RUN is the lead's, not claimed here.** RED recipe recorded in-file: delete the `army.PastFirstRaid` branch in `Resolve`.
- fixture `smallCapFirstRaid` = `{Cap=3, Required=3, PastFirstRaid=false, Deployable=1}` - pins that
  "has raided" may **never** be derived from `RequiredSlots == CapSlots`. This is the coverage gap the
  WO named ("no existing case drives `Resolve` with `RequiredSlots == CapSlots`"), closed from both
  sides.
- singular pin: `TrainNextRaid(1)` must read `Train 1 troop for the next raid`.
- case 13c fit candidates gain `HeartObjectiveCopy.TrainNextRaid(10)`, so the new string is **measured**
  at both aspects like its sibling.

`Assets/Editor/Regression/SmartArgumentRegression.cs` gains one live-path assertion on
`TrainNextRaidOther` (2 troops), so a translator dropping `{Troops}` on one key and not the other is
caught here rather than on a device frame.

**Verified unmoved:** `ArmyReadiness.cs:121` (the sec.3 ruling); the `Train 3 troops to unlock Raids`
pins at `HudLabelFitRegression.cs:2127-2131` and `SmartArgumentRegression.cs:29-31`; the legacy
`RequiredSlots 0 -> cap` fallback; `RaidEntryGate`'s "copy reads RequiredSlots, never CapSlots"
contract (the new sentence still names `RequiredSlots - have`); `RaidDeployController` as the ONE
writer (read-only here, no second writer and no bypass added); `HudActionBarModel.ArmySnapshot` still a
bare passthrough.

## 6. ⛔ BEFORE THE GATE: ONE UNITY STEP IS REQUIRED, AND THE SUITE IS SUPPOSED TO BE RED UNTIL IT RUNS

`LocaleParityRegression` re-reads the generated Unity tables for every `enabledInBuild` locale and
hard-fails when they differ from canonical. Its own comment says this is deliberate: *"A JSON-only
change must stay red until every enabled table is rebuilt."*

**Run `Defenders > Week 1 > Build Localization`** (`DeNelle.Editor.LocalizationBuilder.BuildAll`)
before gating. It is idempotent, regenerates all six tables from the canonical JSON, and sets
`IsSmart` itself from the `{Troops}` placeholder (`LocalizationBuilder.cs:436`) - so the Smart-String
metadata the existing pair carries comes across without hand-editing YAML.

**The `GameStrings_*.asset` / `GameStrings Shared Data.asset` files were NOT hand-edited** - hand-minting
table ids is exactly the kind of duplicated state this repo keeps paying for, and the generator exists.

## 7. Acceptance, honestly scored

| WO sec.5 item | State |
|---|---|
| 1. Fresh trace carries the new field + paints the new sentence | **NOT DONE - cannot be, EDIT ONLY.** The field and the branch are in; the run is the lead's |
| 2. Pre-first-raid path did not regress | **Pinned, not run.** Both existing pins untouched, plus the new small-cap edge case |
| 3. All three data homes agree | **Two edited and mechanically verified** (sec.4); the third is GENERATED by sec.6 and must not be hand-edited |
| 4. A town frame showing the plate | **NOT DONE - EDIT ONLY.** Needs a device build after the gate |
| 5. Brace + NUL on every `.cs` | **DONE** - `python tools/gate_brace.py` on all 8: `GATE_BRACE_SUMMARY bad=0 of 8`, exit 0. No NUL bytes in any changed file. Raw counts balanced on 7 of 8; `SmartArgumentRegression.cs` reads 78/80 raw because it carries literal braces in its own error strings - it was **77/79 at HEAD**, so the delta is +1/+1 and the authoritative scanner passes it |

## 8. Raised, not fixed

- **The Journey RAIDS card is a SECOND surface with the same disease** (WO sec.2, last bullet).
  `..._0603_journey_deck.png` reads `Army 8 / 10 - train to open a camp` at an instant when
  `required=3, ready=True` (log `:7844`) - it phrases against the **cap** while the gate judged against
  the **soft bar**, so it says "train to open" to a player the door would have let through. Different
  surface, different producer (`PostureSignals.SetArmyFill`, seeded in the same `PublishArmyStatus`
  method). Not touched here. It wants its own ticket.
- **`RaidCapabilityHudBridge.cs:178`** (`"Build a Barracks and train troops to unlock Raids."`) is a
  different branch for a different player (no Barracks). Correct as written, left alone.

## 9. One-line owner question

**Is the full-cap-forever raid door at `ArmyReadiness.cs:121` intended - and if so, does the plate's
new post-raid line `Train N troops for the next raid` stand, or should that branch fall through to the
wave line instead?**

## 10. Files changed (30)

Code (8 `.cs`):

    Assets/_Modules/Village/Troops/ArmyReadiness.cs
    Assets/_Modules/Core/UI/RaidEntryGate.cs
    Assets/_Modules/Village/Buildings/BuildTimerService.cs
    Assets/_Modules/Core/UI/HeartHudText.cs
    Assets/_Modules/Core/HudModel/HudStateCopy.cs
    Assets/_Modules/HUD/Kit/HudKitController.cs
    Assets/Editor/Regression/HudLabelFitRegression.cs
    Assets/Editor/Regression/SmartArgumentRegression.cs

Copy (20 canonical JSON): `{en, es, pt-BR, de, fr, ru, ar, ja, ko, zh-Hans}.json` under BOTH
`Assets/StreamingAssets/Data/Canonical/` and `Assets/Resources/Data/Canonical/`.

Board (2, this lane's own):

    WorkOrders/WORK_ORDER_1641_heart_objective_still_says_unlock_raids_after_the_first_raid.md
    WorkOrders/WORK_ORDER_1641_heart_objective_still_says_unlock_raids_after_the_first_raid.RESULT.md

> **Board reconciliation note for the lead:** WO-1641 is **not on `dev`**. The lane brought the ticket
> into the worktree with its first `**Status:**` line flipped and wrote this RESULT beside it. The copy
> at `D:\EoA\WorkOrders\...` was read-only per the brief and is **untouched** - it still reads
> `READY TO IMPLEMENT`. Reconcile the two before regenerating `BOARD.html`.

## 11. Two gates that COULD have been tripped by this shape, checked at source

Neither reds. Recorded because the next seat editing a Core key declaration or a frame-path trace
line will want the answer without re-deriving it.

- **`PlayerTextLiteralLeakRegression` / the literal-debt baseline.** The new `", pastFirstRaid="`
  concatenation in `HudKitController` shifts every line below it in a 5000-line file, so a
  line-keyed baseline would have gone stale wholesale. It is not line-keyed: the fingerprint is
  `sha256` over normalizedPath, member, sink, decodedValue and a same-path/member/sink/value
  ordinal, NUL-joined
  (`LocalizationAuditIO.cs:24-25`) - **no line term.** And the gate is not armed at all:
  `docs/localization/manifest.json` carries **no `literalDebtBaseline` object** (checked
  programmatically), so `PlayerTextLiteralLeakRegression.cs:64-69` returns a PartialSkip rather
  than a verdict. Two independent reasons it cannot red here.
- **Source-lint pins on the two lines whose SHAPE changed.** `HudActionBarRegression.CheckViewPurity`
  (`:471-472`) forbids the literal `RaidEntryGate.ArmyStatus` inside `HudKitController` - the edit
  reads `army.PastFirstRaid` off the local the model already handed over, so the literal is still
  absent. `HudLabelFitRegression` 13a `RequirePin`s `HeartObjectiveCopy.Resolve(` in the same file -
  still present. A grep of `Assets/Editor/Regression/` for the trace text (`trainNeeded=`,
  `objective -> `) and for `PublishArmyStatus(` returns **no hits**: nothing lints the trace string
  or the publish call arity, so widening the overload is safe.

> ⛔ **MERGE HAZARD - THE LEAD MUST HANDLE THIS FIRST.** WO-1641 was minted straight into
> `D:\EoA\WorkOrders\` and is **untracked there**. This lane adds the same path as a **tracked**
> file, so a plain `git merge` into the main tree will refuse with *"untracked working tree file
> would be overwritten by merge"*. Diff or remove the `D:\EoA` copy (it differs from this one by the
> `**Status:**` line only) before merging.

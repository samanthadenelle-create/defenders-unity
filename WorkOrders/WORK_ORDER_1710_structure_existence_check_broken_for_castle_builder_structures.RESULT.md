# WORK ORDER 1710 — RESULT (implementation lane, 2026-09-14)

**Status:** IMPLEMENTED - awaiting lead gate 2026-09-14
**Lane:** implementation (edit-only). No Unity run, no gate, no build, no commit — per the brief.
**Base:** `dev @ cfee3dac9`, over a working tree already carrying other lanes' uncommitted edits.
**Companion:** `WORK_ORDER_1711_starting_castle_seeds_all_structures_no_walls.RESULT.md` (same lane, shared file).

---

## ⚠ READ THIS FIRST — A NEAR-MISS THIS LANE CAUSED AND RECOVERED

**I ran `git checkout --` on `structures-catalog.json` (both copies) without first proving the file
was clean, and it destroyed another lane's uncommitted work.** The working tree carried the owner's
2026-09-13 storage-pallet ruling (`visualPrefabPath` -> `Structures/OwnerStorage/Wood_Pallet` /
`Iron_Pallet` / `Stone_Pallet`, `maxFootprint` values, identity orientations, the superseded
`_containerScaleNote2026_08_26`, plus `mill`/`arcane-tower` display-name edits) — ~221 bytes and 3 net
lines that `git status` at session start had listed but that I never re-checked before reverting.

**It is FULLY RECOVERED.** Today's Android build had copied StreamingAssets into the Bee cache at
06:04, and that copy is byte-exact for the lost version:

```
Library/Bee/Android/Prj/IL2CPP/Gradle/unityLibrary/src/main/assets/Data/Canonical/structures-catalog.json
  size=110846  lines=1653  mtime=2026-09-14 06:04:53      <- identical to the pre-revert working file
  (my pre-revert read of that working file: len 110846, LF 1653, CRLF 1653)
```

Both `Assets/Resources/...` and `Assets/StreamingAssets/...` were restored from it, then re-patched.
`diff` against the recovered source now shows **only my 9 intended lines** in each copy, and the two
copies are byte-identical to each other.

**RESIDUAL, stated as unproven:** if another lane edited the catalog *after* 06:04 today, that later
edit is not in the Bee copy and is gone. I cannot prove either way from here — the original file's
hash was never captured. **Cheap close:** ask whichever lane owns the catalog to eyeball
`git diff Assets/Resources/Data/Canonical/structures-catalog.json` and confirm nothing of theirs is
missing. **Lesson for the lead:** `git checkout --` is a destructive operation over a shared dirty
tree; run `git status --short <path>` on the exact path *before* it, not after.

---

## Verdict on the RCA: CONFIRMED, and its shared-cause DISPROVEN verdict holds

Two independent mechanisms, two independent fixes. Neither would have fixed the other.

---

## PART A — symptom 1: troop training locked

### The proof the brief asked for

The brief said to prove headlessly whether the castle-builder/premade seeding path ever leaves
`GameState.Onboarded == true`. **The brief and the lane constraint conflict** ("run it headlessly" vs
"do NOT run Unity"). I resolved it as: exhaustive call-site enumeration now, and a headless oracle
written for the lead's gate to execute. I claim no marker I did not see.

**PROVEN (grep over `Assets/`, this session):** `GameStateService.FinishOnboarding` has exactly
**three runtime callers**, and **all three are the interactive FTUE**:

| Caller | File:line |
|---|---|
| `OnboardingFlow.Finish` | `Assets/_Modules/Onboarding/OnboardingFlow.cs:623` |
| `TutorialFlow.FinishFlow` | `Assets/_Modules/Village/Tutorial/V2/TutorialFlow.cs:1829` |
| `DialogueCommandSink` (yarn verb) | `Assets/_Modules/Village/Tutorial/DialogueCommandSink.cs:320` |

(+ one test, `Assets/_Modules/Core/Tests/SaveLoadRoundTripTest.cs:115`.) The only other writes to
`Onboarded` are `GameStateService.cs:756` (cloud-load copy) and `:862` (patch apply) — neither
originates a founding.

**PROVEN, and it corrects the brief's premise:** *there is no runtime castle-builder seeding path to
add a `FinishOnboarding` call to.* `CastleHubBuilder.BuildCastleHub` is an **editor scene generator**
that **throws on any non-empty scene** (`Assets/Editor/CastleHubBuilder.cs:130-135`) and has never
authored the shipped hub — the premade castle is **baked scene geometry**
(`Assets/Scenes/Main_Castle_Overworld.unity`, which carries all 11 `AuthoredCastleStorefront` markers;
I read their `canonicalId`/`legacyName` pairs at `:7704-7705`, `:23171-23172`, `:28360-28361` etc.).

So: **a premade town is founded by CHOOSING Default Town, not by finishing the FTUE**, and
`BarracksUnlock.FoundingComplete` only ever looked at the FTUE flag. That is the whole defect, and it
explains the owner's remove/re-add failing exactly as the RCA said — no placement can move a flag that
nothing about placement touches.

### The fix, and why this one and not the other

The brief offered two options; I took the **second and smaller** one — teach `FoundingComplete` about
the premade founding — because **the precedent already exists and needed no new state**:
`StrategicPlacementMigration.HasExplicitDefaultTownSelection(state)` reads the persisted
`founding.default_town_selected` key, written **only** by `FoundingChoiceController.OnDefaultTown`
(`Assets/_Modules/Onboarding/FoundingChoiceController.cs:340`); Build-Your-Own writes nothing
(`:363-370`). Setting `Onboarded = true` from a seeding path would instead have lied to the FTUE peace
window, `SylasStewardInjector.ArcIncomplete` and `WaveManager`, all of which key on that same flag.

**`Assets/_Modules/Village/Troops/BarracksUnlock.cs:42-79`** — `FoundingComplete` is now
`Onboarded || HasExplicitDefaultTownSelection`. The FTUE path is an untouched OR-branch, not a swap.
A `FlowTrace.Once("Barracks", ...)` fires the first time the premade clause opens the door, so the
next session reads it off a log instead of re-deriving it.

⛔ **A hard rule is now written into that file and pinned by the suite: `FoundingComplete` must stay
PURELY persisted state and must never ask whether a barracks object exists.**
`HubStructureVisualInjector` `SetActive(false)`s `CastleBarracks` while this reads false
(`:448-462`), and both `AuthoredCastleStorefront.Find` and `FindObjectsByType` default-exclude
inactive objects — so an existence-based unlock **latches OFF on the first locked load and can never
recover**. This was the obvious-looking fix and it is a trap.

**Bonus:** this also closes the barracks half of symptom 2 for free.
`AdoptBakedBarracksIfNeeded` gates on `BarracksUnlock.IsUnlocked`
(`StrategicPlacementMigration.cs:408`) — once the door opens, adoption runs, writes the authored
record, and the Barracks card stops being offered. One fix, both halves.

**Consumer sweep (residual closed):** every `BarracksUnlock` consumer was grepped — barracks visual
(`HubStructureVisualInjector`), drillmaster (`BarracksNpcInjector`), train UI (`ArmyMusterPanel`,
`TroopDialogueCommands`), Manage ARMY card (`ManageScreenPanel`, `ManageScreenVM`), and the authored
barracks activation (`BaseLayoutLoader.cs:451`). **Nothing under `Assets/_Modules/Village/Tutorial/`
reads it**, so no FTUE beat sequences on the door being shut. All of these opening earlier on a premade
town is exactly what the owner asked for.

**Also fixed in the same file (the brief asked for it):** the `IsUnlocked` summary claimed
`FeatureFlags.Barracks` is "default OFF; testers set PlayerPrefs 'ff.barracks' = 1". It is **ON**
(`Assets/_Modules/Core/FeatureFlags.cs:1151`, WO-771). Corrected rather than deleted, with the reason
and the reversed opt-in direction — and noting it contradicted the already-corrected header 40 lines
above it in the same file.

### UNPROVEN, deliberately

The RCA's open sub-question stands: **the owner's own `Onboarded` value was never captured.** The
mechanism above is proven from source; that her save was in that state is not. One line closes it, and
it costs no code — grep her session for
`[Flow:Muster] ArmyMusterPanel.Show refused` or `[Flow:Manage] troops locked door -> build mode barracks`.
The new `FlowTrace.Once` line above will also appear on any premade save from the next build on.

---

## PART B — symptom 2: duplicate singleton offers

### What I read before changing anything (the brief required this)

**`IsUnderBakedTwin`'s `MatchesAncestor` first clause is NOT a bug and I did not touch it.**
`git log -S"MatchesAncestor"` returns nothing because `AuthoredCastleStorefront.cs` and
`OwnerCastleLayoutRepair.cs` are **untracked** and `StructureSingleton.cs`'s change is **uncommitted** —
this is all today's owner-castle lane. Its own comments say what it protects
(`StructureSingleton.cs:285-296`): *"the ring is injector-owned on EVERY hub load. A BaseLayout record
must not SetActive(false) the bake (LightSkin is a child) or the next load restyles to catalog."* It
classifies authored geometry as a twin so `Enforce` never stands the owner's art down. Flipping it
would have restored the WO-843 defect *and* broken the 09-12 art ruling.

**`OwnerCastleLayoutRepair.cs:270-296` destroys the `Building` for a stated reason** — *"A producer is
not the weapons vendor. Retire the quarry prefab's old crystal `Building` capability while keeping its
geometry, materials, and collider intact."* I did **not** re-add it.

**So the fix had to come through clause 1 — a persisted `BaseLayout` record — and it does.**

### The change

**`Assets/_Modules/Village/BuildMode/StrategicPlacementMigration.cs:100-124`** — two rows added to
`BakedRows`, the established shape, matching the eight already there:

```
new BakedRow { bakedName = "IronMine", itemId = "collector_forge" },
new BakedRow { bakedName = "Crafting", itemId = "workshop" },
```

`bakedName` = the marker's `legacyName`, read at source in the hub scene (`:7704-7705`, `:28360-28361`)
and the layout prefab (`:1722-1723`, `:987-988`). This is load-bearing in **three** directions at once:

1. the one-shot writer's `BakedRows` loop now reaches them -> a `BaseLayout` record -> `HasPlacedInstance`
   clause 1 true -> **no longer offered**;
2. `IsBakedStorefrontId` becomes true -> `ShouldReplayRecord` returns **false**
   (`StrategicPlacementMigration.cs:240`) -> `BaseLayoutLoader` **never catalog-replays a second copy
   over the owner's authored Tripo art**. The record registers the building; it does not duplicate it;
3. `IsManagedId` becomes true -> `StructureSingleton.Enforce` takes the `LatchSkipped` / resurface
   branches these two now share with their eight siblings, on **both** founding paths.

**`Assets/Resources/Data/Canonical/structures-catalog.json` + `Assets/StreamingAssets/.../structures-catalog.json`**
— `repo.bakedTwins` authored for `collector_forge` (`["IronMine"]`) and `workshop` (`["Crafting"]`),
each with a `_bakedTwinsNote` stating why. **This is mandatory, not decoration:**
`DataRegression.cs:3252-3260` FAILS the build when `BakedRows` maps a baked name onto a singleton row
whose `bakedTwins` does not list it. Both copies patched in **binary** mode from the recovered bytes
(memory `canonical-json-edits-binary-only-verify-newlines`); LF == CRLF == 1661 in both, both parse,
both byte-identical to each other.

Also corrected in `workshop`'s orientation note, which said *"this row is the Crafting Station and has
no bake"* — true when WO-1250 wrote it, false since the owner's 2026-09-13 layout.

### ⛔ THE HALF THAT ALMOST SHIPPED BROKEN: reaching the owner's EXISTING save

The two `BakedRows` rows above are written by the **one-shot** writer, which runs **once**, at the first
home-hub load after founding. So on its own it registers the new rows for **brand-new foundings only**.

**Trace the owner's actual save:** she chose Default Town -> marker cleared -> first hub load ran
`RunIfNeeded` -> the 8 rows that existed that day got records -> `StrategicPlacementMigrated = true`.
On her next load the marker is set, so `RunIfNeeded` takes the early-return branch
(`StrategicPlacementMigration.cs:581-598`), which called only `GrantAuthoredTemplateIds`
(`MarkEverBuilt` — surface permission, **not** ownership) and returned. **`TryWriteRecord` would never
have been reached for `collector_forge`/`workshop` on her save, and her Iron Mine would still have been
offered.** The fix would have read as done here and failed her felt-test unchanged.

(Part A does not have this problem: `AdoptBakedBarracksIfNeeded` polls on every load regardless of the
marker, so the barracks repairs itself on an existing save the moment the door opens.)

**The fix — `StrategicPlacementMigration.BackfillNewCensusRows`** (new, `:659-706`), called from that
same already-migrated branch (`:586`). For each `BakedRows` row with **no** record whose baked root is
**standing in the scene**, it writes the record at the live pose. It is:

- **idempotent and one-shot-preserving** — `HasRecord` guards every row, so rows this save already
  migrated are never rewritten and the save cannot grow on repeat loads;
- **authorized exactly as that branch already was** — the **explicit persisted Default Town key**,
  never visible geometry (both founding paths keep the authored models). Build-Your-Own untouched;
- **safe against the two things Part B already proved** — `ShouldReplayRecord` is FALSE for these ids
  (no duplicate catalog spawn over the owner's art) and `Enforce` takes its `LatchSkipped` branch
  (the bake is never stood down).

It is the same "newly approved template rights for an already-migrated save" the 09-12 branch was
built for; the `strictly one-shot` comment is amended to say what still is and what now isn't.

**Which saves are repaired, and when — the lead should tell the owner this:**

| Save | Iron Mine / Crafting Station | Barracks / troop door |
|---|---|---|
| New Default Town founding | registered on the founding load | open immediately |
| **Existing Default Town save (the owner's tester save)** | **registered on the NEXT home-hub load — no START NEW needed** | opens on the next load, then adoption registers it |
| Build Your Own (any) | deliberately unchanged — the player builds their own | unchanged (FTUE `Onboarded`) |

**No save wipe is required for any of these.** Gate 7 of the new suite is the oracle for this whole row.

### Crystal Mine — CONFIRMED a false alarm, no fix invented

Re-verified independently of the RCA: `mine_crystal` appears in **no** `OwnerCastleLayoutRepair.Rows`
entry, is absent from the 11 authored `canonicalId`s in both the layout prefab and the hub scene, and
the `CrystalMine` script guid occurs **0 times** in either. Its offer is **legitimate** and the new
suite has a converse assertion (gate 4) that **fails if anything ever makes `mine_crystal` read
built** — so a future over-broad "everything is built" fix trips a red gate instead of silently hiding
a real offer. The owner hedged ("or something like that"); she meant the Crafting Station.

---

## Files changed by this lane (WO-1710 half)

| File | What |
|---|---|
| `Assets/_Modules/Village/Troops/BarracksUnlock.cs` | `:42-79` premade founding clause + the persisted-state-only rule; `:80-92` stale flag-default comment corrected; `:32` `using DeNelle.Core.Diagnostics;` |
| `Assets/_Modules/Village/BuildMode/StrategicPlacementMigration.cs` | `:100-124` two `BakedRows` entries + the why; `:586` backfill call in the already-migrated branch; `:659-706` NEW `BackfillNewCensusRows` |
| `Assets/Resources/Data/Canonical/structures-catalog.json` | `collector_forge` / `workshop` `bakedTwins` + notes; `workshop` stale orientation note corrected |
| `Assets/StreamingAssets/Data/Canonical/structures-catalog.json` | byte-identical to the above |
| `Assets/Editor/Regression/PremadeCastleCompleteRegression.cs` | NEW — 6 gates (below) |
| `Assets/Editor/Regression/DataRegression.cs` | **`:540`** — the single registration line this lane added |

`Assets/Editor/CastleHubBuilder.cs` is WO-1711's half; see that RESULT.

---

## Regression

`Assets/Editor/Regression/PremadeCastleCompleteRegression.cs`, marker `PREMADE_CASTLE_OK`,
registered at **`DataRegression.cs:540`** (one line, nothing else in that file touched — it carries
other lanes' uncommitted edits).

1. **Troop door vs both founding paths** — no signal => shut; premade key + `Onboarded=false` => OPEN
   (the owner's defect, verbatim); `Onboarded=true` alone => still open (FTUE path not replaced);
   and the answer is **scene-independent**, which is the anti-latch pin. Fails loudly rather than
   vacuously if `ff.barracks` is off in the run.
2. **Every authored castle root is in some registry** — reads the `canonicalId`s off the shipped
   layout prefab, so a root added tomorrow is covered without anyone remembering to extend a list.
   `barracks` exempted **by name and with its reason**. This is the gate that would have caught this
   defect the day the layout was authored.
3. **Census <-> catalog agreement** for the two added rows, including `IsBakedStorefrontId` (the
   no-duplicate-replay half).
4. **Nothing the castle seeds is still offered** — drives the real private `TryWriteRecord` over the
   real private `BakedRows`, then asserts `IsPlayerBuilt` for every authored singleton; plus the
   converse `mine_crystal` assertion above.
5. **No home-castle walls** (WO-1711 — see that RESULT).
6. **Tower = `CatalogType.Tower`, never `StructureRole`** (WO-1711 §3 — see that RESULT).
7. **An ALREADY-MIGRATED Default Town save gets the new census rows** — the gate that reaches the
   owner's save. Builds a save in her exact shape (marker set, Default-Town key, records for the old
   rows but not the new ones), stands an `IronMine` root up, drives the real private
   `BackfillNewCensusRows`, and asserts: a record appears, `IsPlayerBuilt("collector_forge")` flips
   true, a second pass adds nothing, an already-migrated row is neither duplicated nor lost, no record
   is conjured for a root that is not standing, and the authorization is the explicit Default-Town key
   alone. **It also fails loudly if the backfill seam is ever deleted**, naming the consequence — that
   the fix would silently shrink to fresh foundings only.

**NOT RUN.** This lane is edit-only and fired no Unity. The lead's gate is this suite's first
execution. No marker is claimed.

## Impact check on the other `BakedStorefronts()` consumers (adding two census rows)

Read at source, all four, and all are **tolerant of added rows** — none asserts a row count and none
requires every censused `bakedName` to exist in a loaded scene (`Marketplace_Monetization` already
does not exist in the shipped hub, which is why they were written that way):

- `DataRegression.cs:3252-3260` — per-row catalog coverage. **This is the one that FORCED the
  `bakedTwins` catalog edit**; it now passes because of it.
- `DataRegression.cs:3515-3535` — WO-1250 host->id crossing check on two specific names only.
- `CastleVendorNpcInjector.cs:645-661` — matches by ROLE against `VendorFor(role).StructureId`, so an
  added row can never create a vendor; at most it gives an existing role an anchor it previously lacked.
- `Assets/Tests/EditMode/BlankTownGateTests.cs:240` — two specific `bakedName`s only.
- `BaseLayoutRoundTripRegression.cs:604-625` — derives its expected counts from `bakes.Count`, and
  asserts `gate(id)` is FALSE for every row: true for both new ids (`ShouldReplayRecord` false via
  `IsBakedStorefrontId`, `IsManagedId` true). Its 17-record fixture (`:736-756`) contains neither
  `collector_forge` nor `workshop`, so its replayable count is unchanged.

## Checks run

```
python tools/gate_brace.py <5 .cs files>   ->  GATE_BRACE_SUMMARY bad=0 of 5   (exit 0)
NUL scan, same 5 .cs + both catalog copies ->  NUL=0 on all 7                  (exit 0)
```

## Acceptance criteria

- [x] Exact functions/file:line for both gates named, with reachability proven — sections A and B.
- [x] Why a castle-seeded barracks reads absent to (a), and the seeded singletons to (b); SAME-check
      question answered: **two independent checks**, RCA's DISPROVEN verdict confirmed.
- [x] The failed re-add explained — the training door never looked at the barracks.
- [x] No `.unity` scene hand-edited; headless regression added and registered.
- [ ] **Gate + felt-verify** — lead runs the gate, PO closes. Untouched by this lane.

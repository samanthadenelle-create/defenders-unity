# WORK ORDER 1672 — Outside the walls gets its own dock: no BUILD, no TALK, items stay

**Status:** IMPLEMENTED - three open owner questions in §7 (none block the build); awaiting gate
**Lane:** DOCK-CAPTIONS, isolated worktree `.claude/worktrees/agent-ad0b5c7a8c34ea18a`
**Base:** `e4b5906a541c65fc12255a109b5dc3723e328488`, ff-merged from `dev`
**Date:** 2026-09-10

**Owner ruling, verbatim (2026-09-10 12:16):**
> *"when you are outside the castle should not be the peaceful UI, not combat, but should not be able
> to build or talk but can use items still"*

---

## 1. THE HEADLINE FINDING — the signal already exists, and the HUD already computes it

This ticket was scoped as "find how the HUD knows the hero is outside the castle; if no such signal
exists, add the cheapest honest one." **A live signal exists**, is polled every 0.2 s, and already
reaches a posture. What was missing was only the **last hop**.

The chain, each line opened at source in this worktree on 2026-09-10:

| Step | File:line | What it does |
|---|---|---|
| 1 | `Assets/_Modules/Village/HUD/HudContextEvaluator.cs:202-214` | `IsInTownRing` — hub-scene test + horizontal distance of `HeroLocomotion` from the world origin vs `TownRadius = 60f` (`:74`) with `TownRadiusHyst = 8f` (`:75`). No hero resolved ⇒ defaults to in-town (`:207`). Polled at 0.20 s (`:85`). |
| 2 | `Assets/_Modules/Core/HudModel/HudContextResolver.cs:41-48` | `modal > buildMode > combat > (inVillage ? Town : Overworld)` ⇒ `HudContext.Town` / `HudContext.Overworld` (`HudModelTypes.cs:20`) |
| 3 | `Assets/_Modules/HUD/Kit/PostureEvaluator.cs:143-145` | ⇒ `HudPosture.CalmTown` / `HudPosture.CalmExplore` (`HudPosture.cs:17-33`) |
| 4 | `Assets/_Modules/HUD/Kit/PostureEvaluator.cs:78-80` | **already traces every transition**: `[Flow:HudKit] posture calm(town)->calm(explore)` |
| 5 | `Assets/_Modules/HUD/Kit/HudKitController.cs:4980-5040` | `ApplyPosture` mounts whichever widgets that posture's `hud-areas.json` row names |
| **6 — THE GAP** | `Assets/Resources/Data/Canonical/hud-areas.json` | `calm(town)` and `calm(explore)` **both listed `"peacefulDock"`** — so the dock was byte-identical on both sides of a boundary the HUD had already crossed |

**So: no new world seam, no invented radius, and no new FlowTrace.** The ticket brief asked for a trace
on every transition — `PostureEvaluator.cs:78-80` already is that line, and adding a second would give
one event two owners (the §2/§5/§16 duplicated-state failure this repo keeps paying for). One
`FlowTrace.Once` is added, and it proves a *different* thing: that the third dock built and with which
faces.

### 1b. ⚠ The four "castle edge" definitions in this repo DISAGREE — stated as fact, not fixed here

| Definition | Shape | Value | Authority |
|---|---|---|---|
| HUD town ring (**what this ticket uses**) | circle | r = 60 (+8 hyst) | `HudContextEvaluator.cs:74-75`; duplicated in `SafeZoneRecovery.cs:53-54` and `CastleTownsfolkInjector.cs:60` |
| Zone safe box | axis box | ±52 X/Z | `ZoneManager.cs:41-42` |
| Moat band | square annulus | 44 → 62 | `MoatExclusion.cs:39-40` |
| The **actual shipping walls** | data | south wall line at **z ≈ −40.9** | `Assets/Resources/Data/castle-south-recipe.json`, replayed by `CastleWallsFromRecipe.Recreate()` (`Assets/Editor/CastleWallsFromRecipe.cs:39-60`); other three sides are 90/180/270 mirrors (`:33-34`) |

Concretely: a hero at `z = −55` — past the south gate, short of the drawbridge, standing in the moat —
is **`Town`** to the HUD (55 < 60) while `ZoneManager` has already called her `Mirewood` (55 > 52) and
the encounter spawner considers her outside.

**This ticket deliberately does NOT reconcile them.** r = 60 sits just past the drawbridge (±58) at
roughly the moat's outer edge (62) — a defensible "you are past the walls and the moat" — and it is the
number the HUD **already** consumes, so using it adds no new state and no new place to drift. Picking a
different boundary, or collapsing the four into one, is a design + world ruling and is flagged in §7.
**No radius was invented by this lane.**

---

## 2. THE THIRD DOCK

`HudKitController.BuildAdaptiveOutsideDock` — the exact twin of `BuildAdaptivePeacefulDock`: same
housing, same `HudDockSlotLayout` solver, same vertical band, same WO-1671 caption plates.

**Faces, left to right: HERO · JOURNEY · MANAGE · ITEM (four).**

There is no `if` choosing between the three docks anywhere in code — all three are built at boot
(`HudKitController.cs:968-971`) and the choice is **data**: `ApplyPosture` activates whichever dock the
posture's `actionBar` row names.

---

## 3. ⛔ BUILD AND TALK ARE **ABSENT**, NOT DISABLED — and the choice was forced

CLAUDE.md §7 is explicit that the dock's face list is read from the built tree by the oracle, never
from a doc. So the question "absent or disabled?" is really "which one can the oracle hold on to?"

1. **The oracle cannot tell a disabled face from a live one.** `CheckMeasuredPeacefulDock` finds faces
   with `GetComponentInChildren<Button>(true)` — **include-inactive, and deliberately so**, because
   `Register()` deactivates the whole dock root and an active-only walk would read zero faces and RED
   for entirely the wrong reason. A `SetActive(false)` BUILD face **still counts as a face**. Only a
   face that was never constructed can be asserted absent.
2. **A dimmed face must carry a WORD or a NUMBER saying why** — `HudActionBarModel.cs:270-285`, because
   the owner is red/green colourblind and a greyed medallion signals nothing to her. No such copy is
   ruled, and minting it would be a design decision this lane does not own.
3. **The touch floor is the scarcest thing on this bar.** `HudDockLayout.MinSlotPx = 112` and the
   solver already degrades to icon-only when five faces will not fit. Spending two of those slots on
   controls that cannot fire is the opposite of what the narrow-surface ladder is for. At four faces
   the outside dock solves **wider** slots than the calm dock at every shipping surface, never
   narrower.

For the record, the repo has both idioms available and they are different things: hard disable
(`button.interactable = false`, e.g. `HudKitController.cs:3028`) and dim-but-tappable-with-a-word
(`ApplyRaidsDim`, `:3572-3600`). Neither was chosen; the faces are simply not built.

**Also found, and NOT changed:** BUILD and TALK were **never gated at all** in town either.
`BuildPeacefulDockSlot(1, "talk", …)` invokes `HudCommands.Talk()` unconditionally and **never consults
`PostureSignals.TalkAvailable`**, despite `HudKitController.cs:29` claiming it does — that rail's only
consumer is `HudActionBarModel`, which `BindActionBar` never subscribes once the peaceful dock exists
(`:3473`, `:3480-3485`). That is a pre-existing defect one seam over; it is **recorded, not fixed**,
because fixing it changes in-town behaviour the owner did not ask about.

---

## 4. "CAN USE ITEMS STILL"

The peaceful dock has **no ITEM face today** — it is BUILD/TALK/HERO/JOURNEY/MANAGE and nothing else.
The only item surfaces that exist are the combat dock's slot 5 and a legacy standalone `"itemSlot"`
widget which appears **nowhere in `hud-areas.json`** (grepped: zero hits), so `ApplyPosture`'s
else-branch deactivates it in every posture. It is built and permanently off.

The outside dock's ITEM face reuses the combat path exactly — `OpenItemPicker`,
`ElarionUiKit.StyleAsStackBadge`, `HudKitController.SeatStackBadgeInMedallion` (the WO-1468 seat, pinned
by `HudUiRegression`) — so the face the player learned in combat behaves identically out here, in the
same rightmost position.

Two things checked because they would have shipped a stale or wrong face:

- **The badge is driven.** `OnConsumables` (`HudKitController.cs:4224+`) now refreshes the outside
  slot's count and interactable alongside the combat one. Without it the badge would freeze at whatever
  it read on the frame it was built, and a stale "3" over an empty belt is worse than no digit.
- **The picker has no combat precondition.** `OpenItemPicker` (`:3048`) guards only re-entrancy,
  registers via `PanelManager.RegisterBattleAllowed`, and `HudKitController.cs` contains **zero**
  occurrences of `BattleLock`. It takes `WorldHold.AcquirePlayerOwned(WorldHold.ReasonCombatItemPicker,…)`
  — and `WorldHold` is the only code in the project that writes `Time.timeScale`
  (`Assets/_Modules/Core/UI/WorldHold.cs:24`, `:59`). **So opening the picker outside the castle pauses
  the world**, exactly as it does in combat. That is consistent, and it is what "atomic paused picker"
  already means — but it is a felt-behaviour change out of combat and is flagged in §7.

**Caption:** the literal `"ITEM"`, matching the combat face. `HudStrings` has **no item key** (see
`AllKeys`, `HudStrings.cs:128-139`), and minting one means touching the string table plus every locale
file — the localization lane's territory, mid-flight on `dev`. Doing it half-way here is worse than not
doing it. **Follow-up ticket needed:** `hud.nav.item` + translations, then swap the literal.

---

## 5. THE WIRING — one JSON row, both copies

`Assets/Resources/Data/Canonical/hud-areas.json` and `Assets/StreamingAssets/Data/Canonical/hud-areas.json`:
the `calm(explore)` posture's `actionBar` row now names `"outsideDock"` instead of `"peacefulDock"`.
`calm(town)` is untouched.

Edited **binary, from the HEAD bytes** (memory `canonical-json-edits-binary-only-verify-newlines`):
both files were CRLF throughout before and after (**275 LF / 275 CRLF, unchanged**), both parse as
JSON, and the two copies are **byte-identical by SHA-256** (the CanonicalJson dual-copy law the
existing oracle at `HudActionBarRegression.cs:597-600` enforces).

The two pre-existing occupancy checks (`HudActionBarRegression.cs:595`, `ObsidianQueueRegression.cs:402`)
only require the string `peacefulDock` to appear *somewhere* in the file. It still appears once, under
`calm(town)`, so both stay green — correctly, since neither ever asserted which row.

---

## 6. THE PIN — `CheckMeasuredOutsideDock`

New case in `Assets/Editor/Regression/HudActionBarRegression.cs`, built on the same measurement hook
pattern: `HudKitController.BuildOutsideDockProbe` (twin of `BuildPeacefulDockProbe` — one builder, no
live caller, no state, and it must never grow one).

It builds the real dock and asserts, from the tree:

- the exact ordered face set `[HERO JOURNEY MANAGE ITEM]`;
- **separately** — so a re-order cannot make it pass by accident — that **no BUILD face** and **no TALK
  face** exists, and that an **ITEM face does**, each with the owner's ruling quoted in the failure
  string;
- every outside caption carries its WO-1671 obsidian plate (same band, same terrain, same defect one
  posture over);
- the touch floor holds at all four shipping surfaces, re-derived from the count **found**;
- and the last hop: `calm(explore)` mounts `"outsideDock"` and no longer mounts `"peacefulDock"`, in
  **both** JSON copies — because a perfectly-built dock that no posture row mounts is invisible.

**Red-first is by construction.** Against HEAD `e4b5906a5` there is no `BuildOutsideDockProbe` to call,
so the case does not compile against the old tree — the strongest possible red. Against a tree with the
builder but the JSON unchanged it fails on the calm(explore) rows; against a tree where BUILD was
merely disabled it fails on *"the outside dock still carries a 'BUILD' face"*.

⚠ **This lane holds no Unity and did not execute the suite.** The lead gates.

---

## 7. OPEN OWNER QUESTIONS — flagged, not decided. None blocks the build.

1. **HERO / JOURNEY / MANAGE outside the walls — NOT RULED.** They are carried over from the peaceful
   dock unchanged, in the same relative order, as the least-surprising default. If any should be absent
   out there (MANAGE in particular is a town-management screen), say which and the expected-set array
   in `CheckMeasuredOutsideDock` changes with it.
2. **ITEM's position — NOT RULED.** Placed rightmost, matching the combat dock's slot 5, so the item
   face never moves between postures. Leftmost (in BUILD's old place) is the alternative.
3. **The item picker PAUSES the world outside combat** (`WorldHold`, §4). Correct in combat; out in the
   open field it stops the world while the player picks. Acceptable, or should the outside path take a
   non-pausing hold?
4. ⚠ **`calm(explore)` is not only "past the town ring" — it is EVERY NON-HUB SCENE, dungeons
   included.** `IsInTownRing` opens with a `HubScenes.IsHub` test (`HudContextEvaluator.cs:204`), so a
   dungeon or the Village2 approach at rest resolves to `Overworld` → `CalmExplore`. Two existing
   suites pin exactly that: `HudActionBarRegression.cs:167` ("a dungeon forwards calm(explore)") and
   `ScenePostureSeamRegression.cs:174-209`. **So this ticket also removes BUILD/TALK and adds ITEM
   inside dungeons.** That is almost certainly wanted — you cannot build or talk in a dungeon, and you
   very much want items there — but the ruling said "outside the castle", not "everywhere that is not
   the hub", so it is written down here rather than discovered in a felt-test. If dungeons should get
   a fourth dock instead, that is a separate ticket; this one does not block on it.

Recorded but out of scope, each with its own seam: the four disagreeing castle-edge definitions (§1b);
TALK ignoring `PostureSignals.TalkAvailable` in town (§3); the missing `hud.nav.item` string key (§4);
the combat dock's captions likely bleeding the same way as WO-1671's (see WO-1671 §5).

---

## 8. FILES

- `Assets/_Modules/HUD/Kit/HudKitController.cs` — `BuildAdaptiveOutsideDock`, `BuildOutsideDockProbe`,
  the shared `BuildDockSlot` hoist, the localized-copy refresh, the `OnConsumables` badge driver
- `Assets/Resources/Data/Canonical/hud-areas.json` — `calm(explore)` actionBar row
- `Assets/StreamingAssets/Data/Canonical/hud-areas.json` — same, byte-identical
- `Assets/Editor/Regression/HudActionBarRegression.cs` — `CheckMeasuredOutsideDock` + dispatch

**Do NOT touch:** `HudContextEvaluator`'s radius, `PostureEvaluator`'s derivation or its transition
trace, `HudPosture`'s enum values, the `calm(town)` JSON row, `HudActionBarModel` (inert for this bar).

## OWNER RULINGS (2026-09-10 12:55)

Dungeons take the SAME outside dock (calm(explore) as built); the ITEM picker keeps the world pause outside the walls.

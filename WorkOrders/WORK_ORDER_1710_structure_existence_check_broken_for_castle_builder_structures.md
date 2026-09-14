# WORK ORDER 1710 - Structure existence check broken for castle-builder-seeded structures

**Status:** RCA COMPLETE - two independent causes proven; implementation lane assigned 2026-09-14 (see WO-1711 for the related castle-seeding ruling this lane also implements)
**Minted:** 2026-09-14 by the CLI lead (Fable seat), from the owner's live Firebase tester-build report
(release 2026.09.14.369302, dev @ 518e7f29a)

## 1. Owner report, verbatim (2026-09-14, playing the tester build)

> "everything loads properly on a new build however there's things that aren't working right so the
> items are all placed and they all look correct but when I go to the build menu, it's saying that I
> can't make troops because it says I don't have a barracks, but I do see I have barracks so I tried to
> remove the barracks and re-add barracks to see if I can get it to understand what was happening but
> once I removed it, I still didn't have any chance to re-add it so there's something wrong with being
> able to get your army ready for raids also on every single structure. That's there. We need to ensure
> that if it exists that it's marked as built so that you can't place another one on the Singleton ones
> so I was seeing things like it was telling me to add an iron mine and a crystal mine or something like
> that and those don't need to be there if they have no reason if they if they ever exist, we need to
> look at the structures that the castle builder already adds and make sure that we're not duplicating
> this"

## 2. Two symptoms, one suspected shared cause

1. **Troop training blocked.** Player has a barracks placed and visible, but the build/train UI reports
   no barracks and refuses to queue troops. Removing the barracks and re-placing it did NOT restore the
   ability to build one back (the re-add itself failed or silently no-opped).
2. **Duplicate singleton offers.** The build menu offers structures (named: iron mine, crystal mine) that
   the castle builder already seeded on the town, as if they were never built. This should never happen
   for a singleton structure already present.

**Working hypothesis (UNPROVEN - instrument before touching code, CLAUDE.md section 12):** both symptoms
read as the same failure shape - "does structure X already exist on this town" returning false for
structures that were placed by the castle-builder path rather than the interactive build-and-complete
path. If there is one census/registry that both (a) gates troop training on "barracks exists" and
(b) gates the build menu's singleton offer list, a registration gap in the castle-builder seeding code
would explain both symptoms in one root cause. This is a hypothesis to test, not a diagnosis - do not
patch either symptom independently before the RCA lane proves or disproves the shared cause.

## 3. Where to start looking (starting points, not conclusions)

- `docs/MASTER_CATALOG.md` / the relevant `docs/MASTER_CATALOG/<area>.md` section for the build/structure
  system - read first per CLAUDE.md's mandatory first step.
- Castle/town seeding: `CastleHubBuilder.cs` (referenced elsewhere in canon as the place that writes the
  `SpawnPoint`-tag-style historical bugs) and any `OwnedTown*` / `RealmStorePlacer` / structure-placement
  editor or runtime code that seeds structures onto a fresh or loaded town.
- Whatever answers "is structure type T built" for (a) the troop-training gate and (b) the build-menu
  singleton filter - these may or may not be the same function; find both call sites and compare.
- `everBuiltStructureIds` is named in `CLAUDE.md` section 8 (save schema history, WO-834 "blank-town
  baked standdown") - check whether castle-builder-seeded structures are being written into that set at
  all, since a missing write there is exactly the shape of both symptoms.
- The barracks re-add failure specifically: after removing a barracks, does the build menu let you
  select "Barracks" again at all, and if selected does placement succeed - trace with FlowTrace, don't
  guess from reading code alone.

## 4. Instrumentation-first requirement (CLAUDE.md section 12, BINDING)

No code edit until the RCA lane can cite captured data (FlowTrace lines, a headless repro, or a save-file
diff) that pinpoints where the existence check reads false for a structure that is actually present. If a
headless repro is not immediately available, add FlowTrace Step/Warn/Fail at each existence-check call
site named above, run a scene that mirrors the tester build's start state (fresh town + castle-builder
seeding), and read the trace before writing a single fix line.

## 5. Acceptance criteria

- [ ] RCA lane names the exact function(s) and file:line that answer "does structure T exist" for both
      (a) the troop-training/barracks gate and (b) the build-menu singleton offer list, with a citation
      proving each is currently reachable/called.
- [ ] RCA lane proves, with captured data, why a castle-builder-seeded barracks reads as absent to (a),
      and why castle-builder-seeded iron mine / crystal mine (or whichever singletons) read as absent to
      (b). Confirm whether it is the SAME check or two independent checks with the same bug.
- [ ] RCA lane explains the failed re-add: after removing a barracks, what state should allow placing a
      new one, and where does that state fail to reset.
- [ ] Fix (separate implementation lane once RCA lands) must not touch `Village.unity` or any curated
      scene by hand (CLAUDE.md section 3) and must add/extend a headless regression that builds a fresh
      castle-builder town and asserts: barracks-exists reads true, troop training is available, and no
      singleton the castle builder seeded appears in the build-menu offer list.

## 6. What NOT to touch

- Do not guess-fix the barracks gate or the singleton filter independently before the shared-cause
  question in section 5 is answered - a fix to one call site while the other still reads the stale/wrong
  state would look done and not be.
- Do not hand-edit any `.unity` scene.

## RCA 2026-09-14 (read-only lane)

**Lane constraints honoured:** no `.cs` / `.unity` / `Assets` file edited (this ticket only), no Unity
gate, build or commit run. Every citation below was opened at source this session on `dev @ 113fc4939`.

### 0. VERDICT ON THE SHARED-CAUSE HYPOTHESIS: **DISPROVEN. Two independent mechanisms.**

Symptom 2 (duplicate singleton offers) is a **structure-existence** failure in
`StructureSingleton.HasPlacedInstance`. Symptom 1 (troop training refused) is **not an existence check
at all** - the surfaces that say "no barracks" never ask whether a barracks exists. Fixing the seeding /
registry path would leave the training door exactly as shut as it is today. They must be two lanes.

---

### 1. Symptom 2 - the build menu offers castle-seeded singletons. PROVEN, from source + the shipped prefab.

**The offer filter is `IsPlayerBuilt`, never `IsBuilt`** - three call sites, all agreeing:
- `Assets/_Modules/Village/BuildMode/BuildCollectionBrowser.cs:892` (category-root filter,
  `CollectionHasVisibleItems`) and `:651`
- `Assets/_Modules/Village/BuildMode/StructureCardVM.cs:296`
- `Assets/_Modules/Village/BuildMode/BuildModeController.cs:2431` (`IsSingletonBuilt`, the card + arm gate)

`StructureSingleton.IsPlayerBuilt(itemId)` is exactly `HasPlacedInstance(itemId)`
(`Assets/_Modules/Village/BuildMode/StructureSingleton.cs:179-183`), which returns true on only three
things (`StructureSingleton.cs:558-577`):
1. a persisted `BaseLayout` record for the id;
2. a live `PlacedStructure` with that `itemId`;
3. a live `Building` whose `BuildingId` matches **AND** `!IsUnderBakedTwin(b.transform, itemId)`.

**`IsUnderBakedTwin` excludes every owner-authored castle root, whether or not the catalog authors a
baked twin.** Its FIRST clause is `AuthoredCastleStorefront.MatchesAncestor(t, itemId)`
(`StructureSingleton.cs:580-590`), and `MatchesAncestor` walks ancestors for any
`AuthoredCastleStorefront` whose **`CanonicalId`** equals the item id
(`Assets/_Modules/Village/AuthoredCastleStorefront.cs:119-130`). Every castle-builder root carries one.

**What the castle builder actually ships** - `Assets/Prefabs/Village/OwnerCastleStorefrontLayout.prefab`
(87198 bytes, mtime 2026-09-13 19:36, i.e. inside the tester build), component census read off the YAML
this session:
- 11 x `AuthoredCastleStorefront` (guid `fc2e0374...`), 10 with a non-empty `canonicalId`:
  `arcane-tower, jeweler, collector_lumbermill, pet-house, workshop, forge, collector_farm,
  collector_forge, barracks, armorer` (the 11th is the RealmStore, `canonicalId:` empty, prefab line 1356).
- only **7** x `Building` (guid `e15fccf2...`) and **3** x `ResourceCollector` (guid `6eb73cb2...`).

The three collectors have **no `Building` component at all** - `OwnerCastleLayoutRepair.ConfigureCapabilities`
`Object.DestroyImmediate`s it on the `collector_*` branch and adds a `ResourceCollector` whose
`_buildingId` is the LEGACY name (`"forge"`, `"lumbermill"`, `"farm"`), not the catalog id
(`Assets/Editor/OwnerCastleLayoutRepair.cs:270-296`; the Iron Mine row is
`Assets/Editor/OwnerCastleLayoutRepair.cs:39`, `new Row("IronMine", "collector_forge", "IronMine", ...)`).

So **clause 3 is dead for all ten** seeded singletons:
- `collector_forge` / `collector_farm` / `collector_lumbermill`: clause 3 is unreachable (no `Building`);
- the other seven (`workshop`, `forge`, `armorer`, `jeweler`, `arcane-tower`, `pet-house`, `barracks`):
  clause 3 finds the `Building` and then `IsUnderBakedTwin` -> `MatchesAncestor` throws it out.

**Clause 1 is the discriminator, and it splits by FOUNDING PATH.** The only writer that turns a baked
root into a `BaseLayout` record is `StrategicPlacementMigration.RunIfNeeded`
(`StrategicPlacementMigration.cs:548-600` + `TryWriteRecord` `:677-708`), and it is doubly narrow:
- it **returns early at `:562`** when `state.StrategicPlacementMigrated` is already true - which is the
  Build-Your-Own founding (`ResetToNewGame` sets the marker true with an empty ever-built set);
- when it does run, it walks **only `BakedRows`** (`:90-100`) - the 8 legacy ring storefronts. It resolves
  each by `AuthoredCastleStorefront.Find(legacyName)` (`StrategicPlacementMigration.cs` `FindByName`), so
  the owner's renamed roots ARE reachable by their `legacyName` field.

Two cases, therefore:

| Founding path | `BaseLayout` records written | Seeded singletons still OFFERED |
|---|---|---|
| **Default Town** (marker false -> the one-shot writer runs) | the `BakedRows` ids present in the prefab: `forge`, `collector_lumbermill`, `collector_farm`, `pet-house`, `armorer`, `arcane-tower`, `jeweler` | **`collector_forge` (Iron Mine)** and **`workshop` (Crafting Station)** - neither is in `BakedRows` - plus `barracks` whenever its own adoption (`AdoptBakedBarracksIfNeeded`, `:462-501`) has not run |
| **Build-Your-Own** (marker already true -> writer returns at `:562`) | none | **all ten** |

The owner naming the **Iron Mine** specifically fits the first row exactly: `collector_forge` is the one
seeded structure that is in **no** registry at all - no `Building`, no `bakedTwins`, not a `BakedRow`, no
record. **Which case her save is in is NOT proven from here** (it needs her `strategicPlacementMigrated`
value); the fix must cover both, and Lane A's size differs between them.

Two aggravating facts, both read at source:
- `collector_forge` and `mine_crystal` author **no `repo.bakedTwins`** in
  `Assets/Resources/Data/Canonical/structures-catalog.json` (checked programmatically over every
  `repo.singleton` row), so even `IsBuilt`'s baked-twin clause (`StructureSingleton.cs:131-134`) cannot
  see the Iron Mine. `collector_forge` is also **absent from `StrategicPlacementMigration.BakedRows`**
  (`Assets/_Modules/Village/BuildMode/StrategicPlacementMigration.cs:90-100`, the 8 legacy ring
  storefronts) - so it is unknown to *every* registry in the game.
- **There is no Crystal Mine seeder.** `mine_crystal` does not appear in
  `OwnerCastleLayoutRepair.Rows`, in `OwnerCastleStorefrontLayout.prefab`, or in
  `Main_Castle_Overworld.unity`; and the `CrystalMine` behaviour script guid
  `b21677b5fa7e3054f98ac1759f79f60f` (from `Assets/_Modules/Village/Buildings/CrystalMine.cs.meta`)
  occurs **0 times** in both files - so no baked crystal mine hides behind a different name either.
  The Crystal Mine offer is therefore **legitimate** - the owner hedged ("or something like that").
  Do not "fix" it.

**This is not a bug in one function - it is a designed exclusion meeting a new seeding path.** The
`IsPlayerBuilt` exclusion was deliberate (WO-843 / WO-1572, reasoned at
`BuildCollectionBrowser.cs:856-877` and `StructureSingleton.cs:166-179`): a *resurfaced* baked twin is a
visual stand-in after a sell/destroy and must stay buildable. The owner's castle layout introduced roots
that are **the real structure, permanently**, and they are indistinguishable to this predicate from a
post-sell stand-in. The fix is a third state (authored-and-owned vs stand-in), not a flip back to
`IsBuilt` - flipping would re-open the WO-843 "lumber mill destroyed, no option to rebuild" defect.

---

### 2. Symptom 1 - troop training refused. The gate NEVER reads structure existence.

Every "you have no Barracks" surface found in the tree resolves to **one predicate**:

`BarracksUnlock.IsUnlocked => FeatureFlags.Barracks && FoundingComplete`
(`Assets/_Modules/Village/Troops/BarracksUnlock.cs:61`), where `FoundingComplete` is
**`GameState.Onboarded`** (`BarracksUnlock.cs:46-54`). `FeatureFlags.Barracks` defaults **ON**
(`Assets/_Modules/Core/FeatureFlags.cs:1151`), so in practice **`IsUnlocked == Onboarded`**.

Surfaces, all read this session:
- `Assets/_Modules/Village/Troops/ArmyMusterPanel.cs:237-242` - toasts verbatim
  **"The Barracks is not built yet."** and returns.
- `Assets/_Modules/Village/Troops/TroopDialogueCommands.cs:49` - the same string on the train verb.
- `Assets/_Modules/Village/UI/Manage/ManageScreenPanel.cs:2475` - the Manage launcher ARMY card:
  `if (captured == ManageTab.Troops) available = BarracksUnlock.IsUnlocked;` -> face reads
  **"BUILD BARRACKS"** (`:2491`), purpose line **"Build a Barracks to unlock"** (`:2482`).
- `ManageScreenPanel.ActivateLauncherCard` - tapping the locked ARMY card **closes Manage and drops the
  player into build mode** (`if (tab == ManageTab.Troops && !BarracksUnlock.IsUnlocked)` ->
  `EnterBuildMode(BuildType.Town)`), which is very likely the "go to the build menu" in her report.

**None of these four calls `StructureSingleton`, `BaseLayout`, or any existence query.** That is the whole
answer to the owner's "I removed the barracks and re-added it and it still didn't work": **no placement
can ever flip this gate**, because the gate is the FTUE completion flag. She was debugging the wrong
noun. (The one barracks-EXISTENCE-driven surface is a *different* one - the raid-capability toast
"Build a Barracks and train troops to unlock Raids", `RaidCapabilityHudBridge.cs:155-179`, which uses
`IsBuilt` and would have worded it differently.)

**Corroborating captured data (section 14), with its provenance stated honestly:** `logs/f8-inbox/`
capture-20260914-024510-seq5031 .. seq5052 (22 captures today) harvest the line
`[Flow:Siege] deferred: onboarding not finished (!Onboarded) -- the FTUE owns the town until it is`
in a live `Main_Castle_Overworld` session. **That is this machine's Editor.log, not the owner's device**
(payload `"scene":"OwnedTown_IronBastion"`, stack `DeNelle.Editor.OwnedTownMovePlayDriver`, utc
2026-09-11). It proves the code path can and does sit at `Onboarded=false` in a full castle session; it
does **NOT** prove the owner's save was in that state. I have not proven her `Onboarded` value and I do
not assert it.

**What would prove it in one line, and it is cheap:** `adb logcat` / her Player.log filtered for
`[Flow:Muster] ArmyMusterPanel.Show refused` (the trace at `ArmyMusterPanel.cs:239` fires on the exact
tap she describes) or `[Flow:Manage] troops locked door -> build mode barracks`. Either line, from her
session, closes this. One request to the owner, no code.

**Open sub-question, deliberately not guessed, and CHECKED rather than assumed:** the barracks was
*visible* to her, yet the locked standdown looks like it should have hidden it. I verified that the
standdown really does reach the owner's AUTHORED barracks root and is not skipped by
`preserveAuthoredVisual`: `CastleBarracks` is row `HubStructureVisualInjector.cs:119` of the `Swaps`
table, `TrySwap` resolves it through `AuthoredCastleStorefront.Find` (`FindByName`, `:400-402`), and the
`!BarracksUnlock.IsUnlocked` -> `SetActive(false)` block at `:448-462` runs **before** `SkinStorefront`,
which is the only place `PreserveAuthoredVisual` short-circuits (`:785-789`). So a locked save should
show NO barracks, which argues `IsUnlocked` was TRUE for her and points at a second, unfound refusal.
The reconciling shape - a **player-placed** barracks replaying from `BaseLayout` (not unlock-gated) on a
save where `Onboarded` is still false - is consistent with every line above but is **unproven**. The
logcat grep above distinguishes the two, and this sub-question is the one thing Lane B must not start
without.

---

### 3. The re-add failure

Given section 2, "re-adding the barracks did nothing" needs no separate mechanism: the training door
never looked at the barracks. Nothing in the build path was found that would *block* the re-placement
itself - `RemoveLayoutEntry` (`BuildModeController.cs:3760-3778`) drops the record and calls
`StructureSingleton.NotifyRemoved`, after which `IsPlayerBuilt("barracks")` is false and the card is
offered again. One residual worth a lane's attention if the owner reports the *placement* itself
failing: the post-removal resurface (`HubStructureVisualInjector.EnsureBarracksSurfaced`, `:249`)
**also returns early on `!BarracksUnlock.IsUnlocked`**, so on a locked save the twin does not come back
either - the lot simply goes empty.

---

### 4. Headless repro - NOT run, and exactly what is missing

This lane is read-only and forbidden from firing Unity, so **no repro was executed and no marker is
claimed.** The repro is reachable and small; two editor suites already load these assets
(`Assets/Editor/Regression/RealmStorefrontRegression.cs`,
`Assets/Editor/Regression/AuthoredGhostPreviewRegression.cs`; scene constant
`Assets/Editor/OwnerCastleLayoutAudit.cs:17`). What the implementation lane needs to add:

1. **Symptom 2 oracle** - instantiate `Assets/Prefabs/Village/OwnerCastleStorefrontLayout.prefab` into
   the ACTIVE scene (required: `MatchesAncestor` compares `gameObject.scene` to
   `SceneManager.GetActiveScene()`, `AuthoredCastleStorefront.cs:122`), stand up a `GameStateService`
   with an empty `BaseLayout`, then assert `StructureSingleton.IsPlayerBuilt(id)` for each of the ten
   `canonicalId`s. **Run it TWICE, once per founding path** (`strategicPlacementMigrated` true, and
   false with the one-shot writer allowed to run) - the section-1 table predicts 10/10 failures on
   Build-Your-Own and `collector_forge` + `workshop` (+ `barracks`) on Default Town. Those failing
   asserts ARE the repro, and the two runs also settle which case the owner is in.
2. **Symptom 1 oracle** - assert that the troop-training door consults barracks EXISTENCE, not only
   `Onboarded`: with `Onboarded=false` and a live barracks representation, today
   `BarracksUnlock.IsUnlocked` is false and `ArmyMusterPanel.Show` refuses.
3. The only thing that cannot be produced from here is **the owner's own trace** - see the one-line
   logcat grep in section 2.

### 5. Recommendation to the PO

Split into two implementation lanes; they are file-disjoint and share no fix.
- **Lane A (existence/registry):** teach `HasPlacedInstance` to distinguish an owner-AUTHORED owned root
  from a WO-843 post-sell stand-in. Do not revert the filter to `IsBuilt`. Note the two independent
  holes it must close: the **Build-Your-Own** path writes no records for anything, and even on
  **Default Town** `collector_forge` and `workshop` are outside `BakedRows` so no writer ever reaches
  them. A `BakedRows` / `bakedTwins` top-up alone would fix the second and not the first.
- **Lane B (training gate):** decide with the owner whether the troop door should read barracks
  existence rather than `Onboarded`. This is a **design ruling**, not a defect fix - `BarracksUnlock` is
  behaving exactly as the WO-724 charter Option A specified.

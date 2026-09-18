# WO-1872 — A captured town starts BARE: no gifted fortified wall sections or defense structures

**Status:** FIXED — tester APK `2026.09.18.375785` built 18:11 (APK_OK 447MB, R2_PARITY_OK 207), Firebase release 5jrcip5ivq10o; Seeker install pending (phone not on USB at 18:20); owner felt-verify closes. PRIOR: FIXED PENDING DEVICE BUILD — lead gated 2026-09-18 (`Builds/cg1870c` COMPILE_GATE_OK 17:59, `Builds/reg1870b` REGRESSION_OK 584/584 18:03); rides the next tester APK, owner felt-verify closes. PRIOR: IMPLEMENTED PENDING LEAD GATE — the capture census now converts defensive bodies RAZED (Heart kept standing) and the town panel clears each ruin for a `town.captureSalvagePct` share of its build cost; 8 files + the RED-first `[captured-town-bare]` suite, gate_brace 8/8 clean, no scene or locale edit. Minted 2026-09-18 15:25 (banner bumped 1872 -> 1873 in the same edit). Prize-path (raid -> capture -> own the town, the video's closing beat).

**Owner, verbatim (2026-09-18):** "When the playere converts to a town We wan them to create their own town so we shuold not give them two fortified sections of walls with defense structures"

**Owner, verbatim, refining (2026-09-18 15:40):** "they arrive that way cause you repair the current camp" and then: "I think we should load a destroyed camp and then clear the rubble and give them resources to make player designed layouts".

## Ruling (refined 15:40 — this supersedes the "bare" wording below where they differ)
1. The captured town loads as the **DESTROYED camp**: the razed defensive structures are NOT repaired on conversion; they stay as rubble/ruins where they fell. The Heart and non-defensive dressing still convert.
2. The player **clears the rubble**: each ruin is a tap interaction (reuse the existing destroyed-structure / Destructible seam from WO-753 and the town's interactable pattern, never a new system) that removes it with the existing destroy VFX cleanup.
3. Clearing rubble **grants salvage resources** so the player can build their own layout: yield = a **tunable fraction of that structure's build cost** (`RemoteTunables` key, default **0.5**, per the 2026-09-02 ruling that a balance value is a tunable), paid through the normal wallet seam with the standard overflow/cap behaviour. Traced per ruin: what was cleared, what it paid.
4. Build mode in the owned town is free-form on the cleared plot (no change to build mode itself).

## Ruling (original 15:25 wording, kept for history)
When the Iron Bastion capture converts into the player's owned town (`SceneRouter.GoOwnedTown` -> `OwnedTown_IronBastion`, `SceneRouter.cs:596-620`; `OwnedBaseProgression.TryCapture` clones a captured `template` `OwnedBaseState`, `OwnedBaseProgression.cs:25-41`), the player must **build their own town**. The two pre-fortified wall sections and their defense structures (the raid-side garrison walls/towers the capture census records, e.g. `Wall_Outer_SS_*` per WO-1778 RESULT `:34`) are NOT handed over as built structures. The Heart / tree, the plot and any non-defensive dressing stay so the place still reads as the Bastion they took.

## What the lane must prove first (read at source, cite file:line)
1. Where the starting owned-town structure set comes from: the capture census (`RaidCaptureCensus`), the `OwnedBaseState` template (`templateStructureId`, `OwnedBaseProgression.cs:164-239`), `OwnedTownController` / `OwnedTownConstructionService` / `BaseLayoutLoader`, and whether the fortifications are baked into `Assets/Scenes/OwnedTown_IronBastion.unity` or replayed from state.
2. Which of those carries the two fortified wall sections + defense towers.

## Fix
- If they are replayed from state: filter defensive categories (walls, gates, towers/defense structures) out of the captured template at capture time (one seam, `TryCapture`/the census -> template mapping), leaving the Heart and non-defensive dressing. Never mutate a save in place; new captures start bare, and an EXISTING owned-base save is migrated only if the owner rules so (record the question in the RESULT, do not decide it).
- If they are baked into the scene: suppress them at runtime through the existing structure-standdown seam (the same shape `StructureSingleton.MayBakedTwinSurface` / `everBuiltStructureIds` uses for a blank founding) — never hand-edit the `.unity`; if a rebuild is truly required, name the builder method and stop for the lead (bakes are lead-only).
- Resources: the capture supplies (`OwnedBaseProgression.cs:99-109`) are untouched; the player builds with what they have.
- FlowTrace at the filter naming each structure dropped and kept.

## RED-first regression
`Assets/Editor/Regression/CapturedTownStartsBareRegression.cs`, tag `[captured-town-bare]`, markers `CAPTURED_TOWN_BARE_OK/_FAIL`: (A) a capture template built from a census containing wall/gate/defense entries yields an owned-base state with NONE of them and WITH the Heart; (B) a non-defensive entry survives the filter; (C) the scene path, if used, is pinned by source. Revert recipe: remove the filter -> A red.

## Acceptance
`COMPILE_GATE_OK` + `REGRESSION_OK n/n` fresh; a headless or device capture of the owned town after a 3-star Bastion clear showing no pre-built fortified walls or towers; owner felt-verify closes.

## Not in scope
Build-mode changes, the raid-side Bastion layout (raiders still face the walls), balance.

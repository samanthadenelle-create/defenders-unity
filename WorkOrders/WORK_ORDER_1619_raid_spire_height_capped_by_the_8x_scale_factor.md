# WO-1619 - Raid spire height is capped by the 8x scale factor: every baked spire lands short of the monument height the code asks for

**Status:** FIXED 2026-09-10 - gated (Builds/wave2-compile4, Builds/wave2-reg4 493/493) and re-baked (Builds/wave2-bake2: all three configs appliedFactor=14.366 saturatedAt=none achieved=14.40m 100% of target; Builds/wave2-navbake2: 4/4 walkable); owner felt-test closes (the spire is now ~2x taller; hitbox and collapse sink scale with it). OPEN: the Forsaken Camp spire ART is still your creative call (section 8). (was: IMPLEMENTED - awaiting gate + re-bake (lane SPIRE-2 2026-09-10))
**Minted:** 2026-09-09 (CLI, main-line banner; bumped 1619 -> 1621 in the SAME edit)
**Silo / Lane:** World / Raid scene builders (serialization bottleneck - ONE agent on the raid
builders at a time, CLAUDE.md sec.9)
**Severity:** P3 felt - not broken, but every raid objective renders at ~55% of the height the
generator computes for it. The spire is the win condition and the visual centre of the base.
**Type:** EXISTING system. The fit already runs; it silently saturates.
**Owner words:** **none - this is a lane finding.** Surfaced by the WO-1617 BALLISTA lane while
proving something else; no owner has seen it or asked for it. The one creative question inside it is
flagged as an OPEN RULING in sec.8, not decided here.
**Provenance:** `WorkOrders/WORK_ORDER_1617_raid_ballista_stands_on_its_edge_at_monument_scale.RESULT.md`
sec.10, "Bonus finding" - *"three of the four spires never reach the monument height the code asks
for. Out of scope here; recorded, not fixed."*

---

## 1. The defect, source-proven (every line opened 2026-09-09)

### 1a. The fit saturates, and nothing says so

`Assets/Editor/WallTools/RaidBaseGenerator.cs`:

- `PlaceSpire` (`:529`) sets `float targetHeight = 9f;` (`:536`).
- `:537-538` will override it from the catalog: `if (entry != null && entry.repo != null &&
  entry.repo.visualHeight > 0.5f) targetHeight = entry.repo.visualHeight;`
- `:545-547` applies the monument fit: `targetHeight = Mathf.Clamp(targetHeight *
  SpireMonumentMultiplier, SpireMonumentMinHeight, SpireMonumentMaxHeight);` with the tunables at
  `:122-124` (`1.6f` / `8f` / `18f`, introduced by WO-1617 with today's values as defaults).
- `:598` calls `ScaleToHeight(go, targetHeight)`.
- `ScaleToHeight` (`:1240-1250`) is where it dies:
  **`:1247` `float f = Mathf.Clamp(target / b.size.y, 0.2f, 8f);`**
  The method's own summary says it scales *"so its rendered height matches target"* and *"Returns the
  height achieved"* - it returns `b.size.y * f`, so it reports the truth. Nothing reads that return
  and compares it to `target`. **A saturated fit is indistinguishable from a satisfied one in the
  log.**

### 1b. `repo.visualHeight` is authored ZERO times, so the target is always the `9f` default

Walked both canonical twins 2026-09-09 (UTF-8; `Assets/Resources/Data/Canonical/structures-catalog.json`
and `Assets/StreamingAssets/Data/Canonical/structures-catalog.json`, **28 rows each, `cmp` reports the
files byte-identical**):

- `tower_arcane_spire` -> `repo.visualHeight` absent
- `tower_siege_tower` -> `repo.visualHeight` absent
- `tower_ground_archer` -> `repo.visualHeight` absent
- **rows in the whole catalog carrying a non-zero `repo.visualHeight`: 0 of 28**

So `:537-538` never fires, `targetHeight` is always `9f`, and every spire targets
`clamp(9 * 1.6, 8, 18)` = **14.4 m**. This confirms `KEY_FACTS` "ONE HEIGHT CADENCE" (visualHeight
authored zero times; one legacy EDITOR reader survives here) - re-verified at source, not carried
from the doc.

### 1c. What the bake log actually measured

`Builds/raidbase-bake.log` (already on disk; also `raidbase-bake-gate.log`, `raidbase-bake-look.log`).
It carries **THREE** `SPIRE` lines, because `BuildAllRaidScenes` (`:257`) iterates
`RaidConfigIds` (`:253-254`) = exactly `raider_camp_small`, `fortified_garrison`, `mage_enclave`:

```
SPIRE 'tower_siege_tower'  placed at centre: 1200 HP, 14.4m tall, art='Structures/Ballista'
SPIRE 'tower_arcane_spire' placed at centre: 2200 HP,  8.0m tall, art='Structures/ArcaneSpire_1'
SPIRE 'tower_arcane_spire' placed at centre: 3500 HP,  8.0m tall, art='Structures/ArcaneSpire_1'
```

- The two arcane spires target **14.4 m** and land at **8.0 m** - **56% of target**, because
  `f` saturated at the `8f` ceiling.
- The `14.4 m` line is the WO-1617 Ballista defect (the wrong axis was magnified) and is being
  fixed there; it is **not** evidence the fit works.
- **After the WO-1617 re-bake, Easy also resolves to `tower_arcane_spire`**, so the expected state
  is **3 of 3 baked spires at 8.0 m against a 14.4 m target.** That prediction is the first thing
  this ticket must measure (sec.4 step 1), not assume.

`scene-configs.json` authors `centralBuilding` on **five** configs (`:51` `player_outpost`, `:76`
`raider_camp_small`, `:148` `fortified_garrison`, `:231` `mage_enclave`, `:305` `iron_bastion`), but
only three are baked by `BuildAllRaidScenes`. **The RESULT's "three of four" is not supported by the
on-disk log** - the measured population is three baked scenes out of five authored configs. Use the
measured number, not the RESULT's phrasing.

### 1d. UNPROVEN, and it must stay unproven until instrumented

`ArcaneSpire_1`'s raw prefab height of **~1.0 m is DERIVED**, not measured: `8.0 / 8 = 1.0` assumes
`f` saturated. That is consistent with the numbers and it is still an inference. **The bake log line
required by sec.4 step 1 is the measurement**; do not write "the prefab is 1.0 m tall" anywhere until
a line prints it.

## 2. Target - what "fixed" means

A raid spire's rendered height is the height the generator asked for, **or** the generator says out
loud, per spire, that it could not get there and why. There is exactly one height cadence and no
silent saturation anywhere in it.

## 3. Architecture ruling

`docs/ARCHITECTURE_PRINCIPLES.md` - **one owner per concern**, and CLAUDE.md sec.12 - **instrument
first**.

- **"How tall should this spire be?" has ONE owner: `PlaceSpire`'s target computation** (`:536-547`).
  Do not add a second height source, a per-config height field, or a per-art special case.
- **"Did the fit achieve it?" has ONE owner: `ScaleToHeight`** (`:1240`). It already returns the
  achieved height. The reporting belongs where the truth already is.
- **ANY factor or bound that survives this ticket becomes a TUNABLE**, declared beside the existing
  three at `:122-124`, with today's value as its default and a comment naming this WO. A magic
  literal that survives a ticket about a magic literal is the ticket failing. This is the same
  duplicated-state rule CLAUDE.md sec.2 / sec.5 / sec.8 / sec.16 each state in their own words.
- **`MeasuredHeight` (`:1230`) stays the exempt path's reporter** (WO-1617). Do not merge it into
  `ScaleToHeight`; they answer different questions.

## 4. Lane split - THREE steps, and step 1 is not optional

**Step 1 - INSTRUMENT (no behaviour change, ships alone if the lead wants it to).**
Emit **one line per spire** naming, at minimum: the config id, the catalog id, the raw measured
bounds height, the computed `targetHeight`, the clamped scale factor `f`, whether `f` saturated at a
bound, and the achieved height. `FlowTrace` is a runtime helper and this is an editor tool - match
the file's existing `Debug.Log`/`Debug.LogWarning` idiom (`:563`, `:598` neighbourhood, and the
existing `SPIRE '<id>' placed at centre:` line). A **saturated** fit logs at WARNING, because
"I could not do what I was asked" is an anomaly, not information.

Then **RUN THE BAKE and read it.** Numbers for all three baked spires go in the RESULT before any
tuning edit. CLAUDE.md sec.12: static code-reading LOCATES; it never CONCLUDES.

**Step 2 - DECIDE FROM THE DATA, then tune.** The data will name which of two axes is wrong, and it
may be both:

- **The cap is wrong** - `8f` at `:1247` is too tight for art authored at ~1 m. Raise or remove the
  ceiling, as a named tunable.
- **The target is wrong** - `9f * 1.6` = 14.4 m may simply be too tall for the art the game ships,
  in which case the tunables at `:122-124` move and the cap is innocent.

Do not pick one from the armchair. The bake numbers pick it.

**Step 3 - REGRESSION.** Sec.6.

The three steps are one lane (same file, same bottleneck). Split only if the lead wants step 1
landed on its own to get the numbers sooner.

## 5. Pins - what must not move

- `IsAuthoredSiegeMachine` (`:968`) and `ResolveSpireArtId` (`:991`) - WO-1617's shared decider.
  Do not fork, copy or bypass either.
- The authored-siege exemption inside `PlaceSpire` (`:544-547`, `:590-599`). An authored siege
  machine is **not** refitted; if this ticket makes the fit reach its target, the exemption becomes
  MORE load-bearing, not less.
- `PlaceTowerProp`'s pre-existing guard (`:914-916`).
- `RaidSpire.Configure`'s height argument still receives a real number on both branches.
- Spire HP (1200 / 2200 / 3500) is tier-driven and out of scope
  (`docs/RAID_BALANCE_AUDIT_2026-09-06.md`).
- `ScaleToHeight`'s LOWER bound `0.2f` is doing real work against oversized art; do not remove it
  while chasing the upper one.

## 6. RED-first suite spec

Extend **`Assets/Editor/Regression/RaidSpireSiegeRegression.cs`** (markers
`RAID_SPIRE_SIEGE_OK` / `RAID_SPIRE_SIEGE_FAIL`) rather than minting a fourth raid suite - it already
owns `PlaceSpire`'s fit and its `CaseMonumentFitIsTunable` is the case this ticket extends.

- **`CaseScaleFactorBoundsAreTunable`** - the `8f` / `0.2f` in `ScaleToHeight` are named consts
  declared beside `:122-124`, not literals inside the `Mathf.Clamp`.
  **RED at HEAD:** `:1247` reads `Mathf.Clamp(target / b.size.y, 0.2f, 8f)` with both bounds inline.
- **`CaseSaturationIsReported`** - the saturated branch exists and logs. **RED at HEAD:** no source
  path compares `ScaleToHeight`'s return to its `target`.
- **`CaseMonumentFitIsTunable`** stays green, re-pointed to the const NAMES if their values move.

**Name the mutation in the RESULT** (CLAUDE.md sec.12; WO-1617 sec.4 acceptance 1): re-inline either
clamp bound, or delete the saturation warning, and say which case reds.

> **A source-text oracle is a weak oracle and this file has no choice.**
> `Assets/Editor/Regression/DeNelle.EditorRegression.asmdef` does **not** reference
> `DeNelle.EditorWallTools` (read at source 2026-09-09 by the WO-1617 lane), so a regression cannot
> CALL `ScaleToHeight`. WO-1617 sec.5 records the option of adding that reference to enable a
> behavioural case (`ScaleToHeight` on a 1 m fixture at a 14.4 m target). **That asmdef is a
> lead-ruled edit, not this lane's** - raise it, do not take it. Meanwhile CLAUDE.md sec.8 is right
> that a source lint is not coverage: **the bake log numbers in the RESULT are the real proof.**

## 7. Not in scope

- Do **not** hand-edit `RaidBase_*.unity`. Re-bake via `DeNelle.Editor.RaidBaseGenerator.BuildAllRaidScenes`
  then `DeNelle.Editor.RaidNavBake.BakeAll` (required - it drops RaidGround and bakes the NavMesh).
  Never bake with the editor open (CLAUDE.md sec.3).
- Do **not** author `repo.visualHeight` into `structures-catalog.json` **unless** step 2's data says
  the per-art target is the answer. If it does: **both twins, byte-identical, binary-safe edit** -
  memory `canonical-json-edits-binary-only-verify-newlines`; a text-mode rewrite has flattened
  canonical files to zero newlines while still parsing. Verify the LF count on both.
- Do **not** delete the legacy `visualHeight` reader at `:537-538`. It is dead today (0 of 28 rows)
  and WO-1617 deliberately left it; deleting it is its own ticket.
- Do **not** touch `EnsureUpright` (`:1123-1137`) or its threshold.
- Do **not** touch `RaidBaseDresser.MapCatalogArt`, `RaidBaseLayoutRegression`,
  `CaseGarrisonWipeWins`, `RaidHudController` or `TroopController`.
- Do **not** register the suite in `DataRegression.cs` - lead-owned. Hand back the registration line.
- Do **not** run Unity, gate, or commit from the lane. Edit-only; the lead holds the Unity lock and
  is the sole committer.

## 8. OPEN RULING for the owner - creative, not this lane's

**The Forsaken Camp's spire art.** `scene-configs.json:76` authors `centralBuilding:
"tower_siege_tower"` for `raider_camp_small`. WO-1617 made the code refuse to render siege art in the
spire slot, substituting the module's existing `DefaultMageTowerId` (`tower_arcane_spire` ->
`ArcaneSpire_1`) and warning. **That substitution is a fallback, not a pick.**

The WO-1617 RESULT sec.9 **proposes**, and does not decide: an arcane spire reads wrong for an orc
scavenger camp stripping an abandoned settlement (`hexagon-green` kit) - a **ruined watchtower** or a
**wooden keep** would read as *"a place these scavengers took."*

**This is the owner's call and no lane may take it** (WO-1607 sec.0: creative authority on kit
choices stays hers; memory `owner-colorblind-delegate-visual-creative` - ask about behaviour and
read, never hues). When she rules, `scene-configs.json:76` changes in both canonical twins,
byte-identical, and the substitution warning stops firing on its own. **Do not change the JSON before
she rules**, and do not let this ticket's height work wait on it - they are independent.

## 9. Files

| file | role |
|---|---|
| `Assets/Editor/WallTools/RaidBaseGenerator.cs` | the only code file - `ScaleToHeight` `:1240-1250` (the `8f` cap at `:1247`), `PlaceSpire` `:529-600`, the tunables `:122-124`, `MeasuredHeight` `:1230` |
| `Assets/Editor/Regression/RaidSpireSiegeRegression.cs` | the new cases (sec.6); `SpireMonument*` pins at `:176-178` |
| `Assets/Resources/Data/Canonical/structures-catalog.json` + `Assets/StreamingAssets/Data/Canonical/structures-catalog.json` | **twins, byte-identical, ONLY IF** step 2's data says a per-art `repo.visualHeight` is the answer. Both or neither. |
| `Builds/raidbase-bake.log` | the "before" measurement, already on disk |
| `Assets/Editor/Regression/DataRegression.cs` | **DO NOT EDIT** - lead-owned; hand back the registration line |

*(Every line number was opened 2026-09-09 and will shift the moment anything above it changes -
CLAUDE.md sec.11B. They are pointers; re-read at source.)*

## 10. Sequencing - READ BEFORE STARTING

[STOP] **SEQUENCED BEHIND WO-1617's GATE AND RE-BAKE.** WO-1617 is `IMPLEMENTED - awaiting gate +
re-bake`, its edits sit **uncommitted in the working tree**, and they are in **this exact file and
this exact method**. Starting here first would (a) gate two unproven changes as one and (b) make the
owner's WO-1617 felt-test unattributable - the same hazard WO-1617 wrote into its own header about
WO-1607. **Start from the WORKING TREE, never from HEAD.**

## 11. Acceptance criteria

- [ ] **Instrumented first.** A bake log line per spire naming target vs achieved height (and the
      saturated factor) exists and was READ, **before** any tuning edit. Quote all three in the RESULT.
- [ ] The RESULT states the measured population from the log (three baked scenes), not "three of four".
- [ ] `ArcaneSpire_1`'s raw height is a **measured** number in the RESULT, not the `8.0/8` derivation.
- [ ] Every surviving factor/bound is a named tunable beside `:122-124` with today's value as default
      and a comment naming this WO. Zero magic literals left in the fit path.
- [ ] RED-first: named mutation, named case, stated in the RESULT.
- [ ] After the fix, the bake log shows every baked spire at its target height, **or** a WARNING line
      naming the spire, the target, the achieved height and the reason. No silent saturation.
- [ ] An eye-level screenshot of one raid centre before and after
      (memory `screenshots-are-primary-evidence-for-visual-defects` - a log line does not prove a
      silhouette).
- [ ] `RaidBaseLayoutRegression` and `RaidArenaShapeRegression` stay green, or a red is handed to the
      lead for a RULED re-point. **A lane never re-points an oracle on its own.**
- [ ] Brace balance + NUL scan on every `.cs` touched (CLAUDE.md sec.1, WO-434).
- [ ] Everything not proven is listed as unproven (CLAUDE.md sec.11B).
- [ ] Owner felt-verifies on device and **closes**. CLI never closes a visual ticket.

---

## INSTRUMENTED 2026-09-10

**Lane SPIRE, edit-only.** Base `e225ca57b` (after WO-1617's commit `29b6180ec` landed, so sec.1's
line numbers had all shifted - every line below was re-read at source this session). **Step 1 ONLY:
no behaviour change.** The height cap, the scale factor, the tunables at `:122-124` and
`EnsureUpright` are all untouched, and the bake has NOT been run by this lane.

### The measurement lines

`Assets/Editor/WallTools/RaidBaseGenerator.cs`

| file:line | level | exact tag text emitted |
|---|---|---|
| `:593` | - | builds the label: `` string fitLabel = $"config '{def.id}' spire '{catalogId}'"; `` - `BuildAllRaidScenes` bakes three configs per run, so an unlabelled fit line cannot be attributed to a scene |
| `:599` | `Debug.Log` | `[RaidBaseGenerator] SPIRE FIT <label>: EXEMPT (authored siege machine) - no upright correction and no fit applied; measured=<n>m against a pre-clamp target of <n>m. (WO-1619 step 1 instrumentation.)` |
| `:1276` | `Debug.LogWarning` | `[RaidBaseGenerator] SPIRE FIT <label>: NO RENDERERS - nothing to measure and nothing scaled; target=<n>m is being REPORTED as achieved, which is a fiction. (WO-1619 step 1 instrumentation.)` |
| `:1289` | `Debug.LogWarning` | `[RaidBaseGenerator] SPIRE FIT <label>: DEGENERATE BOUNDS (rawHeight=<n>m) - no fit applied; target=<n>m is being REPORTED as achieved, which is a fiction. (WO-1619 step 1 instrumentation.)` |
| `:1307-1310` | (line body) | `[RaidBaseGenerator] SPIRE FIT <label>: rawHeight=<n>m prefabScaleBefore=<n> target=<n>m wantedFactor=<n> appliedFactor=<n> saturatedAt=<UPPER\|LOWER\|none> achieved=<n>m (<n>% of target)` |
| `:1313` | `Debug.LogWarning` | the line above **+** ` - SATURATED: the fit could NOT reach the height the generator asked for. (WO-1619 step 1 instrumentation.)` |
| `:1316` | `Debug.Log` | the line above **+** ` - fit satisfied. (WO-1619 step 1 instrumentation.)` |

Design notes, so step 2 does not have to re-derive them:

- **`ScaleToHeight` (`:1269`) is where the reporting went**, per sec.3 - it already returned the
  truth (`raw * f`) and nothing compared it to `target`. Its signature is now
  `ScaleToHeight(GameObject go, float target, string what)`. **It has exactly ONE caller in this
  file** (`:606`, verified by grep this session); the same-named methods in
  `BattleAnchorStageVerify.cs`, `TreeOfLifeMaterialFixer.cs`, `HubFoliageInjector.cs`,
  `Village2Generator.cs` are unrelated private statics and were NOT touched.
- **There is no silent branch.** The label is required, an empty one falls back to `go.name`, and
  the zero-renderer / degenerate-bounds early-returns now WARN instead of returning `target`
  wordlessly - a fit that reports nothing is the exact defect being instrumented.
- **`satUpper` / `satLower` (`:1302-1303`) compare `wanted` against `f` directly.** `Mathf.Clamp`
  returns `wanted` bit-exactly when it is in range, so the test is exact and, deliberately, does
  **not** restate `0.2f` / `8f`. No new magic number entered a ticket about magic numbers.
- **The `0.2f` / `8f` bounds at `:1296` are still bare literals ON PURPOSE.** Naming them is sec.4
  step 2 and it is licensed by the bake numbers, not by reading the file.
- `prefabScaleBefore` is logged so sec.1d's derivation (`8.0 / 8 = 1.0 m`) can be replaced by a
  measured `rawHeight` **and** confirmed to be a 1x-imported prefab rather than a pre-scaled one.
- Pins held: `MeasuredHeight` (`:1238`), `IsAuthoredSiegeMachine`, `ResolveSpireArtId`,
  `PlaceTowerProp`'s guard, `RaidSpire.Configure`'s height argument, the `0.2f` lower bound.

### The suite

`Assets/Editor/Regression/RaidSpireSiegeRegression.cs` - **extended, not minted** (sec.6).
New case `CaseSaturationIsReported` (`:210`), wired into `Run` at `:78`. Source-text oracle, for
the asmdef reason the file's own header records. It pins two things:

- (a) `ScaleToHeight`'s body contains a `Debug.LogWarning` **and** names saturation;
- (b) `PlaceSpire` calls `ScaleToHeight(go, targetHeight,` - i.e. the label is still passed.

**RED-first, stated per sec.6:** at base `e225ca57b` case (a) failed because `ScaleToHeight`'s body
carried no `Debug` call of any kind, and case (b) failed because the call site passed two arguments.
**Mutation that re-reds it:** delete the `LogWarning` saturation branch at `:1313` -> (a) reds;
drop the third argument at `:606` -> (b) reds.

**`CaseScaleFactorBoundsAreTunable` was DELIBERATELY NOT ADDED.** It is red by design until step 2
turns `0.2f` / `8f` into named consts, and this suite is **already registered** in
`Assets/Editor/Regression/DataRegression.cs:1984` - so committing a red case would fail the lead's
combined gate for a change nobody has made yet. Its spec stays in sec.6; step 2 adds it and records
its own RED-first mutation. **No registration line is owed to the lead: the suite was already wired
by WO-1617.**

`CaseMonumentFitIsTunable` (`:173`) is unchanged and still green - no tunable value moved.

### The bake, and what its output must show to license step 2

Batchmode method: **`DeNelle.Editor.RaidBaseGenerator.BuildAllRaidScenes`**
(then `DeNelle.Editor.RaidNavBake.BakeAll` per sec.7 **only if the re-baked scenes are kept**; a
measurement-only run does not need it). Never with the editor open (CLAUDE.md sec.3).

Read the log with PowerShell, not `grep` - **Unity logs are UTF-16**
(memory `unity-logs-are-utf16-read-with-powershell`):

```powershell
Select-String -Path Builds\raidbase-bake.log -Pattern 'SPIRE FIT|SPIRE ''' | ForEach-Object { $_.Line }
```

Expect **three** `SPIRE FIT` lines - `BuildAllRaidScenes` iterates `RaidConfigIds` =
`raider_camp_small`, `fortified_garrison`, `mage_enclave` (sec.1c). Anything other than three is
itself a finding; use the measured population, never sec.1c's prediction.

Step 2 is licensed by, and only by, these numbers:

- **If all three read `saturatedAt=UPPER` with `achieved≈8.00m` against `target=14.40m`** - the CAP
  axis is confirmed and sec.4's first branch is the fix: the `8f` ceiling at `:1296` becomes a named
  tunable beside `:122-124` and moves.
- **`rawHeight` is the number sec.1d forbade guessing.** If it lands near `1.0m` the derivation is
  vindicated (say so as *measured*, not derived). **If it is materially different from `1.0m`, sec.1d's
  inference was WRONG** and that is the finding - re-derive before touching anything.
- **If any line reads `saturatedAt=none`** the fit is reaching its target on that spire and the
  defect is narrower than sec.1 claims; the RESULT must say which spires actually saturate.
- **If a line reads `saturatedAt=LOWER`** the art is oversized, not undersized, and the ticket's
  whole premise inverts - stop and re-scope.
- `prefabScaleBefore` materially off `1.000` means the prefab arrives pre-scaled and the target, not
  the cap, is the wrong axis (sec.4's second branch).

### Unproven by this lane (CLAUDE.md sec.11B)

- **No bake was run and no number was measured here.** Every quantity in sec.1c remains the
  2026-09-09 log's, read from disk by a previous lane; this lane read only source.
- `ArcaneSpire_1`'s raw height is **still the `8.0/8` derivation** and must not be written as fact
  anywhere until the `rawHeight=` field prints it.
- The new suite case has **not been executed** - the lane holds no Unity lock. It is brace-clean
  (`tools/gate_brace.py`, `bad=0`) and NUL-free, and that is all that has been proven about it.
- Sec.8's Forsaken Camp art question is untouched and still awaiting the owner.

---

### OWNER RULING 2026-09-10

**Sec.8 is RULED. The Forsaken Camp (`raider_camp_small`) spire art is a RUINED WATCHTOWER.**

Given by the owner on the morning of 2026-09-10 (via AskUserQuestion), answering the proposal
WO-1617 RESULT sec.9 put to her and sec.8 above carried forward. ⚠ **Recorded as RELAYED in this
lane's brief, not witnessed** - the owner's own words were not captured by this lane, so the three
bullets below are the ruling's substance as handed over, not a transcript (memory
`prove-with-data-including-owner-prose`). Shape of the ruling:

- **A ruined watchtower.**
- **NOT** the default arcane spire (`tower_arcane_spire` -> `Structures/ArcaneSpire_1`) - the
  substitution `ResolveSpireArtId` currently applies is a fallback and she has now declined it.
- **NOT** a wooden keep - the second option WO-1617 RESULT sec.9 offered is explicitly rejected.

The ruling changes **data, not code**. `ResolveSpireArtId`
(`Assets/Editor/WallTools/RaidBaseGenerator.cs:1035`) and `IsAuthoredSiegeMachine` (`:1012`) stay
exactly as WO-1617 left them: the seam is the config's `centralBuilding` key, read at
`PlaceSpire` -> `ResolveSpireArtId(def.centralBuilding)`
(`Assets/Editor/WallTools/RaidBaseGenerator.cs:569`, opened this session). When the id changes, the
siege-machine substitution warning at `:1040` stops firing on its own - that is the designed exit,
and no generator edit is licensed by this ruling.

Sec.8's instruction "do not change the JSON before she rules" is now spent. What replaces it is
**sec.8b below: the ruling cannot be executed yet, because the art it names does not exist.**

---

## FORSAKEN CAMP ART 2026-09-10

**Lane: FORSAKEN-ART, edit-only.** Worktree `.claude/worktrees/agent-ae76284e121864b5a`, base
`c10e4f5d1` (fast-forwarded from `dev`; `git status --short` empty before the edit). No Unity, no
bake, no gate, no commit from this lane.

### What changed

**This file only.** The `### OWNER RULING 2026-09-10` block above, and this section.

**NO art id was authored, and no JSON was touched.** `scene-configs.json:76` still reads
`"centralBuilding": "tower_siege_tower"` in **both** canonical twins - verified this session:
`grep -n centralBuilding` on `Assets/Resources/Data/Canonical/scene-configs.json` and
`Assets/StreamingAssets/Data/Canonical/scene-configs.json` returns the identical six rows
(`:13` schema doc, `:51` `tower_ground_archer`, `:76` `tower_siege_tower`, `:148` / `:231` / `:305`
`tower_arcane_spire`), and `cmp` reports the two files byte-identical. The WO-1619 height fix is
untouched. **No `.cs` file was opened for edit**, so no brace/NUL gate is owed by this lane.

**WO-1619's `**Status:**` line is deliberately NOT changed.** The height ticket is FIXED; this is
its open sec.8 sub-task, and it is now blocked on art rather than on a ruling.

### 8b. WHY THE RULING COULD NOT BE EXECUTED - no ruined-watchtower art exists

**Searched this session, by token not by name** (memory `search-by-token-not-by-name`), across the
worktree AND the main clone `D:\EoA` (the KayKit / Synty / polyperfect packs are gitignored, so they
are absent from a linked worktree and had to be read from the main tree):

| what was searched | how | result |
|---|---|---|
| every catalog row the generator can resolve | walked `Assets/Resources/Data/Canonical/structures-catalog.json` `entries[]` (28 rows) and printed `id / type / visualPrefabPath` | **no ruin row.** The only watchtower art in the catalog is `tower_ground_archer` -> `Structures/Tower_Wooden_Watchtower` |
| the whole `Assets/` tree | `find Assets -iname "*ruin*"` | 5 hits, **none a model**: `Resources/Arena/Backdrops/ruins_backdrop.jpg`, `Resources/VFX/Damage/Damage_Ruin.prefab`, and the `Scenes/Garrison_ruined_keep*` scene + navmesh |
| KayKit + Synty + polyperfect | `find ... -iregex ".*\(damage\|destroy\|broken\|rubble\|derelict\|abandon\|wreck\|collaps\|remains\|old\).*"` on `.fbx/.prefab/.gltf` | walls, floors, furniture, house debris and scaffolding. **Zero ruined towers.** |
| tower-shaped art anywhere | `find Assets \( -name "*.fbx" -o -name "*.prefab" -o -name "*.gltf" \)` filtered on `spire\|obelisk\|monolith\|keep\|fort\|citadel\|bastion\|turret\|derelict\|shell\|husk` and on `tower` | full list read; the only "ruined + tall" hit in the entire repo is `Egypt_Obelisk_Old_Broken` (polyperfect Egypt kit) |
| the REMOTE Addressables catalog | `grep -inE "ruin|watchtower" Assets/AddressableAssetsData/AssetGroups/Structure_Art.asset` | `Structures/Tower_Wooden_Watchtower_L3` (`:44`), `Structures/Tower_Wooden_Watchtower` (`:109`), `Structures/Tower_Wooden_Watchtower_L2` (`:229`), plus one texture address (`:364`). **No ruin address.** |
| the doc catalogs | `grep -rniE "ruin|watchtower"` over `docs/polyperfect-asset-catalog.md`, `docs/kaykit-asset-catalog.md`, `docs/MODEL_CATALOG.md`, `docs/MASTER_ASSET_REFERENCE.md`, `docs/asset-inventory/01_kaykit.md` | `polyperfect-asset-catalog.md:67`, `:70`, `:79` and `kaykit-asset-catalog.md:46`, `:47` - all describe intact watchtowers; `:47` names `building_destroyed` as the kit's damage piece |

**Corroborating precedent, read at source:** the repo's existing "ruined keep" is built with
**lighting and props, not a ruin model** - `Assets/Editor/GarrisonSceneBuilder.Scenes.cs:97-103`
dresses `troll_outpost` / `ruined_keep` / `frost_keep` with intact Tribal_T watchtowers
(`DressWithTribalCamp`, called at `:103`), and the
`token == "ruined"` branch at `:169-173` supplies a desaturated green-grey sun/ambient/fog instead.
Nobody has ever had a ruined-structure model to reach for.

**Per this lane's brief, that is a STOP, not a licence to substitute.** Picking an intact tower and
calling it ruined would be exactly the guess CLAUDE.md sec.11B forbids.

### The closest three candidates - for the owner's eye, with paths

Nominated on **measured geometry**, not on a name. Bounds below are read out of each model's glTF
twin in the same pack (glTF accessors carry exact `POSITION` `min`/`max`), so they are measurements,
not estimates - but **no candidate has been SEEN by this lane**, and "does it read as ruined" is an
eye call reserved to the owner (memory `screenshots-are-primary-evidence-for-visual-defects`).

**1. `building_tower_base_green` - the decapitated tower shaft, in the camp's OWN kit. Closest.**

```
Assets/Models/KayKit/KayKit Medieval Hexagon Pack 1.0.1/Assets/fbx(unity)/buildings/green/building_tower_base_green.fbx
```

- Measured `0.930 x 1.500 x 1.111 m`, **one** mesh node (`building_tower_base_green`).
- The COMPLETE tower in the same folder, `building_tower_A_green`, is `0.993 x 2.192 x 1.153 m` with
  **two** mesh nodes - `building_tower_A_top_green` **and** `building_tower_A_green`. And
  `building_tower_cannon_green` (`0.930 x 1.657 x 1.111`) shares the base's footprint **to three
  decimals**. So the "base" is measurably the same tower shaft with its top course absent - the
  nearest thing in the repo to a tower that has lost its head.
- It is in `KayFolders` (`RaidBaseDresser.cs:45` - the hexagon `buildings/green` row), so
  `LoadVisual` resolves it, and it is the **exact kit the Forsaken Camp is already dressed in**
  (`hexagon-green`).
- Stands up: `1.500 >= max(0.930, 1.111) * 0.8` -> `EnsureUpright` (`RaidBaseGenerator.cs:1250`)
  returns early, no flat-FBX rotation.
- Fits the WO-1619 height target with room: `14.40 / 1.500 = 9.60`, inside
  `SpireFitFactorMax` (24) and well clear of the retired `8f`.
- **Cost:** it is NOT a `structures-catalog.json` row today. Executing it needs a new entry
  (`id` + `type: "Tower"` + `visualPrefabPath`) authored into **both** canonical twins,
  byte-identical, binary-safe (memory `canonical-json-edits-binary-only-verify-newlines`).
- **Unproven:** whether it reads as *ruined* or merely *unfinished*. That is the whole question and
  a log line cannot answer it.

**2. `Structures/Tower_Wooden_Watchtower_L3` - a real watchtower, zero risk, not ruined.**

```
Assets/StructureContent/Tower_Wooden_Watchtower_L3.prefab
```

- Tracked in git (`Assets/StructureContent/` is the always-present root,
  `DeNelle.Core.AssetRoots.StructureContent`), and already an **addressable address** in the REMOTE
  `Structure_Art` group (`Assets/AddressableAssetsData/AssetGroups/Structure_Art.asset:44`).
- Genuinely a watchtower - half the owner's ruling satisfied, the "ruined" half not at all.
- **Cost / objection:** `tower_ground_archer` already points at `Structures/Tower_Wooden_Watchtower`
  (catalog row read this session), so this makes the camp's win-condition centrepiece a taller copy
  of its own archer towers. That is a silhouette problem, and the spire is the object the player
  must read at a glance.
- Not measured by this lane (a `.prefab` carries no glTF twin; measuring it needs Unity).

**3. `building_destroyed` - the kit's only authored RUIN, and it is not a tower.**

```
Assets/Models/KayKit/KayKit Medieval Hexagon Pack 1.0.1/Assets/fbx(unity)/buildings/neutral/building_destroyed.fbx
```

- Measured `1.539 x 0.994 x 1.306 m` - **wider than it is tall.** It is a ruined-house / rubble
  footprint, not a tower, and `docs/kaykit-asset-catalog.md:47` describes it as the piece that
  "sell[s] wave damage".
- ⛔ **It would be TIPPED OVER by the generator.** `EnsureUpright` (`RaidBaseGenerator.cs:1249-1252`)
  computes `widest = max(1.539, 1.306) = 1.539`; `0.994 < 1.539 * 0.8 = 1.231`, so the flat-FBX
  heuristic fires and applies `-90` X - the **exact** WO-1617 Ballista failure, on the exact same
  code path, reproduced from measured numbers rather than predicted.
- Listed for completeness, and to record why it is NOT the answer despite being the only "ruin".

*Honorable mention, wrong kit:* `Egypt_Obelisk_Old_Broken`
(`Assets/polyperfect/Low Poly Ultimate Pack/_M/Prefabs_M/Egypt_M/Egypt_Obelisk_Old_Broken.prefab`)
is the repo's one tall-and-broken structure, but it is Egyptian stone and cannot read as a medieval
watchtower.

### What the lead / owner has to decide - one of three, and none is this lane's

1. **Rule candidate 1 in** (`building_tower_base_green`, possibly with a rubble prop at its foot from
   the dresser's existing prop pass). One catalog row in both twins + `scene-configs.json:76`. No
   new art, no code.
2. **Commission the art** - a `Tower_Wooden_Watchtower_Ruined` prefab beside the existing L1/L2/L3
   family in `Assets/StructureContent/`, which then also needs an address in the `Structure_Art`
   group. This is the only option that satisfies the ruling literally.
3. **Re-rule** to something the tree already carries.

### Pins that move with the ruling - grepped this session, nothing owed TODAY

`grep -n "raider_camp_small\|tower_siege_tower\|centralBuilding" Assets/Editor/Regression/*.cs`
(run this session; every hit below opened). **No suite pins `raider_camp_small`'s `centralBuilding`
VALUE**, so this lane owes no re-point and none was taken (a lane never re-points an oracle on its
own - WO-1619 sec.11).

| file:line | what it actually asserts | does the ruling red it? |
|---|---|---|
| `RaidArenaShapeRegression.cs:202` / `:243` | reads `centralBuilding` and fails only when a config authors **none** ("it would have NO SPIRE") | **No** - any non-empty id passes |
| `RaidArenaShapeRegression.cs:409` | `RequireAll(... GeneratorSrc, "RaidSpire", "def.centralBuilding")` - a SOURCE lint that the generator still reads the key | **No** - the seam is unchanged |
| `RaidBaseLayoutRegression.cs:94-107` (`CaseEasyDress`) | the Easy row's `raidDress.props` count >= 12 and **`kit == "hexagon-green"`** | **No** - and it is the source proof that the camp's kit is hexagon-green, which is why candidate 1 is in that kit |
| `RaidSpireSiegeRegression.cs:121` | a source lint that `ResolveSpireArtId` still guards the slot; `:7` and `:247` are prose | **No** - it pins the GUARD, not the config's id. It stays green (and stays meaningful) after the ruling |
| `BuildInventoryFilterRegression.cs:64-93` | ⚠ a **self-cleaning catalog sweep** - `UnnamedLockIds` must match what the data says "in EITHER direction ... a NEW dead tile cannot slip in unnoticed" | **Only if a NEW catalog row is added** (options 1 and 2 both add one). A Tower-type row with no unlock writer must earn a dated, cited exemption line - and its own header forbids adding one "to make the suite green" |
| `ManageDefenseCardRegression.cs:472-495` | hardcoded `tiered` / `flat` id lists + a per-id portrait requirement | **Only if a new row is added AND someone lists it.** The array is hand-authored, so a new id is invisible until added - but a defence tower with no Manage portrait is a real gap worth raising, not hiding |
| `BuildEconomyRegression.cs:265`, `CostBasketSeparationRegression.cs:210`, `EconomySinkCapRegression.cs:520` | `tower_siege_tower` as a **buildable catalog id** (costs, sinks, copy) | **No** - the ruling removes it from a raid CONFIG, not from the catalog. The row stays |

**The one real downstream cost, stated plainly:** the ruling is a one-key JSON edit *only* if the art
id already exists in `structures-catalog.json`. It does not (sec.8b), so both live options add a
catalog row - and a new row lands inside `BuildInventoryFilterRegression`'s sweep. That is a
measurement + a dated exemption line for whoever executes it, not a blocker.

### The bake the lead must run - ONLY after option 1 or 2 lands

> ⚠ **SUPERSEDED 2026-09-10 by "FORSAKEN CAMP ART - IMPLEMENTED" sec.7-8 below.** The owner ruled
> option 1 and it is implemented; the runbook that counts is sec.7. Kept, not rewritten
> (CLAUDE.md sec.15) - the worked example here is what sec.8 was derived from.

Nothing to bake today: **no data changed, so a bake now would reproduce the current log exactly.**
When the art id is authored, editor closed (CLAUDE.md sec.3), batchmode, in this order:

1. `DeNelle.Editor.RaidBaseGenerator.BuildAllRaidScenes`
2. `DeNelle.Editor.RaidNavBake.BakeAll` (required - it drops RaidGround and bakes the NavMesh)

Read the log with PowerShell, never `grep` - Unity logs are UTF-16
(memory `unity-logs-are-utf16-read-with-powershell`):

```powershell
Select-String -Path Builds\<bake>.log -Pattern 'SPIRE FIT|SPIRE ''|SIEGE MACHINE' | ForEach-Object { $_.Line }
```

**Expected for the Forsaken Camp config**, taking candidate 1's measured `1.500 m` raw height and a
new catalog id `tower_ruined_watchtower` as the worked example
(`14.40 / 1.500 = 9.600`; `1.500 * 9.600 = 14.40`):

```
[RaidBaseGenerator] SPIRE FIT config 'raider_camp_small' spire 'tower_ruined_watchtower': rawHeight=1.500m prefabScaleBefore=1.000 target=14.40m wantedFactor=9.600 appliedFactor=9.600 saturatedAt=none achieved=14.40m (100% of target) - fit satisfied. (WO-1619 step 1 instrumentation.)
[RaidBaseGenerator] SPIRE 'tower_ruined_watchtower' placed at centre: 1200 HP, 14.4m tall, art='<the new visualPrefabPath>'.
```

Two independent things prove the ruling landed, and **both must be checked**:

- `saturatedAt=none` and `achieved=14.40m` - the WO-1619 height fix still holds for the new art.
- ⛔ **The `is an authored SIEGE MACHINE` warning from `ResolveSpireArtId`
  (`RaidBaseGenerator.cs:1040`) must be ABSENT from the log.** While it still fires, the config is
  still authoring `tower_siege_tower` and the ruling has NOT been executed, whatever else the log
  says. Its disappearance is the designed proof.

`rawHeight` and `prefabScaleBefore` above are **arithmetic from a measured glTF bound**, not from a
bake. `prefabScaleBefore=1.000` assumes the KayKit FBX imports at unit scale - **unproven**, and the
field exists precisely to say so on the first run (contrast `ArcaneSpire_1`, which measured
`prefabScaleBefore=0.010`). Anything else is the finding, not a failure of this prediction.

### Remote or local

**REMOTE, if option 2 is taken.** `Assets/StructureContent/` art ships through the Addressables
`Structure_Art` group, whose `Remote.LoadPath` is the R2 bucket (CLAUDE.md sec.16); a new prefab
there needs an address in `Structure_Art.asset` and a `tools\r2-ship.ps1` run after the content
build, or it renders as a placeholder on device with **no error on screen**.

**LOCAL, if option 1 is taken.** `Assets/Models/KayKit/...` is resolved by
`RaidBaseDresser.LoadVisual` through `AssetDatabase.LoadAssetAtPath` at **bake** time
(`RaidBaseDresser.cs:130-140`), so the mesh is baked into the `RaidBase_*.unity` scene and rides in
the build. Nothing to push. ⚠ But the KayKit pack is **gitignored** (it is absent from this
worktree; it was read from the main clone `D:\EoA`), so the bake must run on a machine that has the
pack imported - a fresh clone would silently miss it and fall back to the primitive obelisk
(the warning at `RaidBaseGenerator.cs:598-600`, the substitution at `:609`).

---

### OWNER RULING 2026-09-10 (second pass) - **"Use the KayKit tower base"**

Put to the owner as the three measured candidates above; she picked **candidate 1**.

```
Assets/Models/KayKit/KayKit Medieval Hexagon Pack 1.0.1/Assets/fbx(unity)/buildings/green/building_tower_base_green.fbx
```

⚠ Relayed in the lane brief (2026-09-10 morning, via AskUserQuestion), **not witnessed by this
lane** - same caveat as the first ruling block. The STOP recorded above is hereby **spent**: the
ruling is implemented below.

---

## FORSAKEN CAMP ART - IMPLEMENTED 2026-09-10

**Lane FORSAKEN-ART, edit-only** (no Unity, no bake, no gate, no commit). Worktree
`.claude/worktrees/agent-ae76284e121864b5a`, base `9281cd7ea` (ff-merged from `dev`).
WO-1619's `**Status:**` line is still NOT changed - the height ticket stays FIXED; this is its
sub-task.

### 1. The new catalog row - `tower_ruined_watchtower`

Authored into **both** canonical twins, byte-identical, **binary-safe** (patched from ONE buffer,
never a text-mode rewrite - memory `canonical-json-edits-binary-only-verify-newlines`):

```
Assets/Resources/Data/Canonical/structures-catalog.json
Assets/StreamingAssets/Data/Canonical/structures-catalog.json
```

**Newline proof, printed by the patch itself:** `104616 -> 108347` bytes,
**LF `1626 -> 1638` (+12, exactly the 12 lines inserted), CRLF `1638`, bare LF `0`** (the file is
CRLF throughout and stayed so), 0 NUL bytes, `json.loads` round-trips to **29** entries with no
duplicate id, and `cmp` reports the twins byte-identical after the write.

Every field, and the neighbour it copies:

| field | value | neighbour it matches |
|---|---|---|
| `id` | `tower_ruined_watchtower` | the `tower_<thing>` stem of `tower_arcane_spire`, `tower_ground_archer`, `tower_siege_tower` |
| `displayName` | `Ruined Watchtower` | `"Arcane Spire"`, `"Archer Tower"` |
| `description` | authored | **required**: `BuildEconomyRegression.CheckStructureDescriptions` (`:254-258`) exempts only rows with NO `visualPrefabPath`; this row has one |
| `type` | `Tower` | all three spire-capable rows |
| `kind` | `Cell` | `tower_ground_archer`, `tower_siege_tower` |
| `visualPrefabPath` | `Structures/building_tower_base_green` | the `Structures/<stem>` shape of all 71 registered addresses; **the stem is deliberately the SOURCE file's**, so the copy step, this key and `MarkInto`'s lookup agree with no renaming |
| `_ownerArtRuling` | the ruling + the measurements | `tower_ground_archer` carries an `_ownerArtRuling` in exactly this shape |
| `_raidOnlyNote` / `_artPipelineNote` | why the omissions are load-bearing, and the two-step art chain | the file's existing `_*Note` convention |

**Deliberately ABSENT, and each omission is load-bearing** (read at source, not assumed):

- **no `manageFilters`, no `manageArtKey`** - `BuildInventoryFilterRegression` case 2 fires only on
  ids a card collection **offers** (`:155-160`), case 4 only when `tokens.Count > 0` (`:213`), and
  case 6's `DerivedUnnamedLocks` is seeded from `offered` (`:244`, and its own summary at `:295-299`
  says "Ids that are OFFERED by a collection"). `grep` this session: `tower_ruined_watchtower`
  appears in **neither** `card-collections.json` nor `build-categories.json`.
- **no `repo` block** - `CheckCosts` skips `e.repo == null` (`:326`); `CheckTowerContract` gates on
  `repo.behaviorId` being `DefenseTower`/`ArcaneTower` (`:445-447`). `PlaceSpire` reads
  `entry.repo.visualHeight` behind a `repo != null` guard and that field is authored **zero** times
  catalog-wide (sec.1b).

### 2. The config re-point - the seam, not a hardcoded string

`scene-configs.json:76`, **both twins**, binary-safe value-only replacement:
`"centralBuilding": "tower_siege_tower"` -> `"centralBuilding": "tower_ruined_watchtower"`.
**LF `352 -> 352`, CRLF `352`, bare LF `0`**, bytes `19330 -> 19336`, twins `cmp`-identical, and
`json.loads` confirms exactly one `raider_camp_small` row now carrying the new id.

No generator code decides this. `PlaceSpire` reads it at `RaidBaseGenerator.cs:569`
(`ResolveSpireArtId(def.centralBuilding)`), and because the new id is not a siege machine,
`ResolveSpireArtId` (`:1035-1038`) returns it unchanged and its substitution warning (`:1040`)
**stops firing on its own** - the exit WO-1617 designed.

### 3. ⛔ THE DRESSER WOULD HAVE UNDONE ALL OF IT - the defect this lane found and fixed

`RaidBaseDresser.ReskinCombatArt` (`:463-469`) re-skins the spire AFTER the generator has measured
and fitted it: `LoadVisual(MapCatalogArt(def.centralBuilding))`, falling back to `ArcaneSpire_1` on
null. `MapCatalogArt` (`:495-510` at base) was a **four-substring table** - `siege` / `catapult` /
`arcane` / `archer` - returning the **raw id** on a miss. `tower_ruined_watchtower` matches none of
them, `LoadVisual("tower_ruined_watchtower")` finds no such file, and the null-fallback would have
put **ArcaneSpire_1 back on the spire** - the dresser silently discarding the generator's own
correctly-fitted model, with the ruling implemented in data and invisible in game.

Fixed at the seam rather than by adding a fifth substring (which would have made the art token
duplicated state - CLAUDE.md sec.2/sec.5/sec.16):

- **`RaidBaseGenerator.CatalogArtPath(string)`** - new, `internal`, declared beside the other shared
  deciders. Returns `FindStructure(id)?.visualPrefabPath`. `internal` for the reason
  `IsAuthoredSiegeMachine`'s own doc gives verbatim: so the dresser reaches the **same** source the
  generator measures instead of keeping a second table.
- **`RaidBaseDresser.MapCatalogArt`** now asks the catalog FIRST and keeps the substring table only
  as the fallback.

**Behaviour for every id that was already live is unchanged BY CONSTRUCTION**, and this was checked
row by row, not assumed: `tower_arcane_spire` authors `Structures/ArcaneSpire_1` and
`tower_ground_archer` authors `Structures/Tower_Wooden_Watchtower` - the identical two tokens the
`arcane` / `archer` branches returned - while `tower_siege_tower` / `tower_catapult` never reach the
table at all, because `ResolveSpireArtId` substitutes them one line above. `LoadVisual` strips the
`Structures/` prefix itself (`RaidBaseDresser.cs:126-128`), so the prefixed catalog value is the
correct return.

`RaidSpireSiegeRegression.CaseDresserRoutesSpireThroughTheDecider` (`:315-338`) stays green: the
body still contains `RaidBaseGenerator.ResolveSpireArtId(`, and `"Ballista"` still appears **after**
it (the new block was inserted between the two). Verified by reading the case, not by running it.

### 4. The art has to reach `Assets/StructureContent/` - one table row, no new tool

`DataRegression.CheckStructures` (`:3053-3067`) loads **every** row's `visualPrefabPath` through
`StructureAssetLoader` (Addressables-first, Resources-fallback) and FAILS the row if it returns
null. A KayKit pack path is neither, so the sanctioned chain is the one that already exists:

`Assets/Editor/CatalogPrefabImporter.cs` - the table that copies gitignored pack art into
`Assets/StructureContent/`. Three small changes, no new tool and no new concept:

- `KitPrefab` gains an **`Ext`** field defaulting to `".prefab"` (every polyperfect row IS a prefab;
  KayKit ships raw `.fbx` with no prefab wrapper). `CopyAsset` and
  `AssetDatabase.LoadAssetAtPath<GameObject>` treat a model file exactly like a prefab, and
  `StructureAddressablesMigrator.MarkInto` (`:550-551`) **already probes `.fbx`** beside `.prefab`
  when resolving a catalog key - so an `.fbx` row needs no other change anywhere in the chain.
- a `SrcRootKayHex` const for the hexagon `buildings/` root.
- one table row: `new KitPrefab("building_tower_base_green", "green", SrcRootKayHex, ".fbx")`.

The copy loop now uses `kit.Ext` for both `src` and `dst`, with a `string.IsNullOrEmpty` guard so a
`default(KitPrefab)` still means `.prefab`. **Existing rows are byte-for-byte unaffected** - they
pass no `ext` and take the default.

### 5. Files changed - five, and nothing else

| file | change |
|---|---|
| `Assets/Resources/Data/Canonical/structures-catalog.json` | + the `tower_ruined_watchtower` row (binary-safe) |
| `Assets/StreamingAssets/Data/Canonical/structures-catalog.json` | same bytes, same buffer |
| `Assets/Resources/Data/Canonical/scene-configs.json` | `:76` `centralBuilding` re-pointed |
| `Assets/StreamingAssets/Data/Canonical/scene-configs.json` | same bytes, same buffer |
| `Assets/Editor/CatalogPrefabImporter.cs` | `Ext` field + KayKit root + one table row |
| `Assets/Editor/WallTools/RaidBaseGenerator.cs` | + `internal static string CatalogArtPath` |
| `Assets/Editor/WallTools/RaidBaseDresser.cs` | `MapCatalogArt` asks the catalog first |

`.cs` proof: `python tools/gate_brace.py` over all three ->
`GATE_BRACE_SUMMARY bad=0 of 3`, exit 0. Zero NUL bytes in each. Naive brace counts 18/18, 259/259,
131/131. **No `.unity` scene was touched and no gate marker string appears in any edit.**

### 6. Pins re-checked after the change - none red, one deliberate non-edit

- `RaidBaseLayoutRegression.CaseEasyDress` (`:94-107`) pins the Easy row's `raidDress.props` count
  and `kit == "hexagon-green"`. **Untouched** - `raidDress` was not edited, and the chosen art is
  from that very kit.
- `RaidArenaShapeRegression` (`:243`) fails only on an **empty** `centralBuilding`. Still non-empty.
- `RaidSpireSiegeRegression` - green, see sec.3.
- ⛔ **`BuildInventoryFilterRegression` needs NO exemption line, and adding one would be wrong.**
  The brief asked for a dated exemption for "a Tower row with no unlock writer". Read at source
  (`:64-93`, `:244`, `:295-299`), that list is derived **only** from ids a card collection OFFERS;
  an unoffered, unfiltered row never enters `DerivedUnnamedLocks`, so case 6 neither fires nor wants
  a line. The file's own header is explicit: *"Never add an id here to make the suite green. An id
  earns a line only with the measurement that put it there."* The measurement here says **stay
  out**. Recorded rather than done - if the lead disagrees, the one-line add is trivial, but it
  should be a ruling, not a lane's reflex.

### 7. What the lead must run, IN THIS ORDER - the sequencing is the whole risk

⛔ **Gating before step 1 and 2 will RED `DataRegression.CheckStructures`** on
`tower_ruined_watchtower` ("visualPrefabPath ... loads NULL"). That is the check doing its job, not
a defect - the address genuinely does not exist until the art is copied and marked. Editor closed
throughout (CLAUDE.md sec.3):

1. `DeNelle.Editor.CatalogPrefabImporter.CopyKitToResources` - copies the FBX into
   `Assets/StructureContent/`. Judge by its own line: `CATALOG_KIT_COPY_OK`, and confirm
   `Assets/StructureContent/building_tower_base_green.fbx` exists.
   ⚠ The KayKit pack is **gitignored**; this must run on a machine that has it imported (it is
   absent from this lane's worktree and was read from the main clone).
2. `DeNelle.Editor.StructureAddressablesMigrator.MarkCatalogArt` - reads the new key back out of
   `structures-catalog.json` and registers the asset into the **remote** `Structure_Art` group.
   Judge by `STRUCTURE_MARK_OK <n>`, and check its companion warning line names **no** missing key.
3. `DeNelle.Editor.RaidBaseGenerator.BuildAllRaidScenes`
4. `DeNelle.Editor.RaidNavBake.BakeAll` (required - it drops RaidGround and bakes the NavMesh)
5. The normal gate, then the content build, then **`tools\r2-ship.ps1`** - `Structure_Art` is a
   REMOTE group (CLAUDE.md sec.16). Skip it and the spire is a placeholder on device with **no
   error on screen**.

### 8. The expected bake line for the Forsaken Camp

Read the log with PowerShell, never `grep` - Unity logs are UTF-16
(memory `unity-logs-are-utf16-read-with-powershell`):

```powershell
Select-String -Path Builds\<bake>.log -Pattern 'SPIRE FIT|SPIRE ''|SIEGE MACHINE' | ForEach-Object { $_.Line }
```

Taking the **measured** raw height 1.500 m (glTF accessor bounds) and the WO-1619 fit
(`14.40 / 1.500 = 9.600`; `1.500 * 9.600 = 14.400`):

```
[RaidBaseGenerator] SPIRE FIT config 'raider_camp_small' spire 'tower_ruined_watchtower': rawHeight=1.500m prefabScaleBefore=1.000 target=14.40m wantedFactor=9.600 appliedFactor=9.600 saturatedAt=none achieved=14.40m (100% of target) - fit satisfied. (WO-1619 step 1 instrumentation.)
[RaidBaseGenerator] SPIRE 'tower_ruined_watchtower' placed at centre: 1200 HP, 14.4m tall, art='Structures/building_tower_base_green'.
```

The other two configs must be **unchanged** at `tower_arcane_spire` / `achieved=14.40m` - if either
moved, the `MapCatalogArt` change did something it was proven not to (sec.3) and that is the
finding.

**Three things prove the ruling landed, and all three must be checked:**

1. `saturatedAt=none` at `achieved=14.40m` - WO-1619's height fix still holds for the new art.
2. ⛔ The `is an authored SIEGE MACHINE` warning (`RaidBaseGenerator.cs:1040`) is **ABSENT**. While
   it fires, the config still says `tower_siege_tower` and the ruling has not landed, whatever else
   the log says.
3. ⛔ `art='Structures/building_tower_base_green'` - **not** `ArcaneSpire_1`. If it reads
   `ArcaneSpire_1`, the dresser is still overwriting the spire and sec.3's fix did not take.

### 8b. RCA of the wave-3 bake - **the catalog row was never applied to the tree that baked**

The lead ran the full chain and the spire still came out a primitive. Diagnosed **from the logs**,
not from theory (CLAUDE.md sec.12). Base `3da5e5360`.

**THE PROVING LINE** - `Builds/wave3-bake5`, read this session:

```
[RaidBaseGenerator] centralBuilding 'tower_ruined_watchtower' has no visualPrefabPath in structures-catalog.json - falling back to a URP-safe primitive obelisk.
[RaidBaseGenerator] SPIRE FIT config 'raider_camp_small' spire 'tower_ruined_watchtower': rawHeight=16.238m prefabScaleBefore=1.000 target=14.40m wantedFactor=0.887 appliedFactor=0.887 saturatedAt=none achieved=14.40m (100% of target) - fit satisfied.
[RaidBaseGenerator] SPIRE 'tower_ruined_watchtower' placed at centre: 1200 HP, 14.4m tall, art='<primitive>'.
```

That warning is the `else` on `prefabPath` in `PlaceSpire`, i.e. `FindStructure(catalogId)` returned
**null or a row with no path**. It is NOT `LoadVisual` failing - the *other* branch prints
`spire art '<path>' ... not found in StructureContent/KayKit/Synty`, and that line is **absent**
from the log. `rawHeight=16.238m` is `BuildFallbackObelisk`, which corroborates it.

**THE CAUSE, measured in the main clone `D:\EoA` (the tree that baked):**

```
$ grep -c "tower_ruined_watchtower" Assets/Resources/Data/Canonical/structures-catalog.json      -> 0
$ grep -c "tower_ruined_watchtower" Assets/StreamingAssets/Data/Canonical/structures-catalog.json -> 0
$ grep -c "tower_ruined_watchtower" Assets/Resources/Data/Canonical/scene-configs.json            -> 1
```

**The `scene-configs.json` half of this lane's change reached the baking tree; the
`structures-catalog.json` half did not.** The config pointed at an id that existed in no catalog,
`FindStructure` returned null, and the generator fell back exactly as designed. Nothing is wrong
with the row, the stem, the case, the extension, the folder or `LoadVisual`'s search order - the row
was simply not there. Both twins in **this lane's worktree** still carry it (`grep -c` -> 1 and 1,
`visualPrefabPath` reads `Structures/building_tower_base_green`), so the fix is to apply that half.

**The other two logs corroborate, and neither is a second defect:**

- `Builds/wave3-kitcopy3` - `CATALOG_KIT_COPY_OK`, and `Assets/StructureContent/building_tower_base_green.fbx`
  is on disk (63,644 bytes). **The importer change worked.**
- `Builds/wave3-mark3` - `STRUCTURE_MARK_OK 35`, unchanged, and its companion warning names the
  four long-standing misses only:
  `4 catalog art key(s) have NO file under 'Assets/StructureContent' and were NOT marked:
  Structures/CrystalMine, Structures/HealingCaravan, Structures/IronMine, Structures/Well`.
  `Structures/building_tower_base_green` is **not** in that list - because
  `StructureAddressablesMigrator.ReadCatalogArtKeys` regexes `"Structures/..."` keys **out of
  structures-catalog.json**, and with no row there is no key to mark. `35` was correct, not stale.
  ⚠ So `MarkCatalogArt` is CATALOG-DRIVEN: it does not need the asset to be a prefab (`MarkInto`
  probes `.fbx` explicitly), it needs the **row**. Re-run it after the row lands.

**The row SHAPE was independently checked too, and it is correct** - so "the row exists but authors
its path under another key / in a nested object" is ruled OUT, not assumed away:

- `RaidBaseGenerator.StructEntry` (`:1531-1536`) declares exactly three fields, and the path is
  **top-level**: `public string id; public string visualPrefabPath; public StructRepo repo;`
- `tower_arcane_spire` authors `"visualPrefabPath": "Structures/ArcaneSpire_1"` top-level.
- This lane's row authors `"visualPrefabPath": "Structures/building_tower_base_green"` **top-level,
  in the same position**. Parsed this session in the worktree, both twins:
  `entries 29`, top-level keys
  `['id','displayName','description','type','kind','visualPrefabPath','_ownerArtRuling','_raidOnlyNote','_artPipelineNote','canHitAir']`,
  `LF 1638 / CRLF 1638 / bare LF 0 / NUL 0 / 108347 bytes`, `cmp` byte-identical.

The baking tree parsed **28** entries with `tower_ruined_watchtower` absent; this worktree parses
**29** with it present and pathed. The difference is the applied change, not the authoring.

### 8c. The instrumentation fix this cost - `RaidBaseGenerator.cs:604-624`

One warning served two different failures - "the id has NO ROW" and "the row authors no path" - and
printed the second wording for the first case. That is what pointed the diagnosis at `LoadVisual`,
which was working perfectly. Split into two distinct messages; the no-row one now names **both
canonical twins** and the exact failure mode that produced it (a `scene-configs.json` re-point whose
catalog row never landed). No behaviour change: same branch, same fallback, same obelisk.

### 8d. The proving grep - run it on the NEXT bake log

Unity logs are UTF-16 with embedded NULs, so strip them first:

```bash
tr -d '\000' < Builds/<bake> | grep -a -i "tower_ruined\|tower_base\|primitive\|NO ROW\|authors no visualPrefabPath\|SPIRE FIT config 'raider_camp_small'"
```

**PASS looks like this** - one `SPIRE FIT` line and one `SPIRE ... placed` line, and **nothing
else** matching:

```
SPIRE FIT config 'raider_camp_small' spire 'tower_ruined_watchtower': rawHeight=1.500m prefabScaleBefore=1.000 target=14.40m wantedFactor=9.600 appliedFactor=9.600 saturatedAt=none achieved=14.40m (100% of target) - fit satisfied.
SPIRE 'tower_ruined_watchtower' placed at centre: 1200 HP, 14.4m tall, art='Structures/building_tower_base_green'.
```

**Expected `art=` value: `Structures/building_tower_base_green`.** Any of these means it is still
broken, and each now names its own cause:

| line in the log | what it means |
|---|---|
| `art='<primitive>'` with `rawHeight=16.238m` | the fallback obelisk - the art did not load |
| `has NO ROW in structures-catalog.json` | the catalog half is still unapplied (**this bake's cause**) |
| `HAS a structures-catalog row but that row authors no visualPrefabPath` | the row landed malformed |
| `spire art '...' not found in StructureContent/KayKit/Synty` | a genuine `LoadVisual` miss - the only line that would have justified the resolver theory |
| `art='Structures/ArcaneSpire_1'` | the dresser is overwriting the spire; sec.3's `MapCatalogArt` fix did not take |

⚠ **`rawHeight` is the fastest tell.** `16.238m` is the obelisk; `~1.500m` is the ruled art. And
note `achieved=14.40m (100% of target)` printed **green on a failing bake** - the fit is honest
about the object it was given, so a satisfied fit is not evidence the right model was fitted. Check
`art=` and `rawHeight`, never the percentage.

### 8e. The art LANDED - and then four suites red on the row as PLAYER content

`Builds/wave3-bake7`, quoted by the lead: `rawHeight=1.500m ... appliedFactor=9.600 saturatedAt=none
achieved=14.40m` and `art='Structures/building_tower_base_green'`; mark rose 35 -> 36. **The art
question is closed.** `Builds/wave3-reg7` then read 490/494: four suites sweep **every** catalog row
as if it were buildable.

**The repo already has a seam for a catalog row that is not player content, and it is not an
allowlist - it is a row SHAPE.** Both existing non-player rows use it, and both say so in their own
notes: `repair_default` - *"NOT a buildable structure: ... type Decoration maps to no build verb in
build-categories.json, so it never appears in a palette"*; `deco_torch` - *"Decoration maps to no
build verb in build-categories.json, so this row is currently unreachable from any palette."*
The row was re-authored to that shape. **No ad-hoc exemption was added for the first three.**

| failing suite | its actual predicate, read at source | the existing door used |
|---|---|---|
| **BUILD ECONOMY** `is affordability-gated but CostFor resolves ZERO` | `BuildEconomyRegression.CheckCosts:333` - `affordGated = e.repo.placement == null \|\| e.repo.placement.checkAffordable` | `repo.placement.checkAffordable: false` - exactly how `repair_default` (buildCost 0) passes today. **No fabricated price**: this row is not purchasable at any price, so authoring a cost to buy silence would be the lie |
| **TOWER MANAGER** `[stat-source]` | `BuildModeController.IsTowerEntry:3388-3396` - `type == Tower` **OR** `repo.behaviorId` in DefenseTower/ArcaneTower | `type: "Decoration"`, `behaviorId: null`. The suite's own failure text names this door: *"author repo.range/repo.damage or **drop the tower type**"* |
| **BUILDMENU ECONOMY** `[catalog-cost]` / `[blocked-spend]` / `[cancel-refund]` | `BuildMenuRealEconomyRegression:221`/`:379` selects on `CatalogRegistry.OfType(CatalogType.Tower)` and `entry.type != CatalogType.Tower` | the **same** `type` change - one edit closes both tower suites |
| **build-card art** `NOT in the recorded debt list` | `BuildCardArtRegression:130-146` sweeps **every** entry with no type filter; the only door is `KnownArtlessIds` | **one dated + cited ledger line** - and here the ledger IS the convention: `deco_torch` and `repair_default` are both already in it, the first annotated *"Type 'Decoration', and NO build verb maps to Decoration."* |

**Row as authored now** (both twins, binary-safe; `type` moved Tower -> Decoration and a
non-player `repo` added):

```json
{
  "id": "tower_ruined_watchtower",
  "displayName": "Ruined Watchtower",
  "description": "A watchtower the scavengers took: the shaft still stands, the crown is long gone.",
  "type": "Decoration",
  "kind": "Cell",
  "visualPrefabPath": "Structures/building_tower_base_green",
  "repo": {
    "behaviorId": null,
    "buildCost": 0,
    "navSurface": "None",
    "placement": { "mustSitOn": "Ground", "footprint": 1, "noOverlap": false, "checkAffordable": false }
  }
}
```
*(plus `_ownerArtRuling`, `_raidOnlyNote`, `_nonPlayerRowNote`, `_artPipelineNote`, `canHitAir`.)*

**Newline proof, both patches, printed by the patches themselves:** `108347 -> 110040` bytes,
**LF `1638 -> 1650` (+12, exactly the 12 lines added), CRLF 1650, bare LF 0**; then the note
correction `110040 -> 110625`, **LF `1650 -> 1650` (unchanged), CRLF 1650, bare LF 0**. 29 entries,
`type == Decoration`, `checkAffordable == false`, no `repo.cost` key, `visualPrefabPath` intact,
twins `cmp`-identical after each write. `gate_brace bad=0 of 4`, 0 NUL in every `.cs`.

⛔ **The raid path is untouched by the type change, checked at source, not assumed.**
`RaidBaseGenerator.StructEntry` (`:1531-1536`) declares **only** `id`, `visualPrefabPath` and
`repo` - it never reads `type`. `RaidArenaShapeRegression` compares the `centralBuilding` **string**.
So the spire still resolves `Structures/building_tower_base_green` and still fits to 14.40 m.

### 8f. ⚠ The mistake inside this, recorded because the reasoning was wrong, not just the value

The row first shipped with **no `repo` block**, on the stated reading that
`CheckCosts` *"skips rows where repo == null"*. That predicate is quoted correctly and the
conclusion was still false: **`CatalogEntry.repo` is default-constructed**, so an absent JSON `repo`
still deserializes to a NON-null object and the gate fired anyway
(`Builds/wave3-reg7`: *"no repo.cost AND buildCost 0 - placement would be free"*). Reading the
predicate is not the same as reading what the predicate **sees**. The stale sentence in
`_raidOnlyNote` has been corrected in place rather than deleted (CLAUDE.md sec.15) and now carries
this finding, so the next seat inherits the correction and not the confident wrong reason.

### 8g. The fallback generator - a UNITY method, for the lead's chain

`Assets/_Modules/Village/Catalog/Generated/CatalogFallbackData.g.cs` is stale by construction now
(the gate reports the catalog moved `104608 -> 108347` bytes and `28 -> 29` rows; it will read
110625 after this change). **It is NOT pure C# codegen this lane can run** -
`Assets/Editor/CatalogFallbackGenerator.cs` is `using UnityEditor;` and calls
`AssetDatabase.ImportAsset` (`:172`), with a `[MenuItem]` at `:82`. Entry point, verbatim from the
gate's own FIX line:

```
powershell -NoProfile -File .\run-unity-method.ps1 -Method DeNelle.Editor.CatalogFallbackGenerator.Generate -LogName catalog-fallback-gen.log -ExpectMarker CATALOG_FALLBACK_GEN_OK
```

Judge by the marker `CATALOG_FALLBACK_GEN_OK` on a FRESH log, never the exit code. **Run it AFTER
this row change lands and BEFORE the regression**, or `[fallback-parity]` reds again on the new
byte count - it hashes the catalog, so any further edit to the row re-stales it.

### 9. Unproven by this lane (CLAUDE.md sec.11B)

- **No Unity ran. No bake, no gate, no screenshot.** Every number in sec.8 is arithmetic from a
  measured glTF bound, not a measurement of a bake.
- **`prefabScaleBefore=1.000` is an ASSUMPTION** - it presumes the KayKit FBX imports at unit scale.
  The field exists to say otherwise on the first run (`ArcaneSpire_1` measured `0.010`). Anything
  else is the finding, not a failed prediction.
- **The art has still not been SEEN.** Whether the shaft reads as *ruined* rather than *unfinished*
  is the owner's eye call at felt-test; this lane proved its geometry, never its look.
- **`AssetDatabase.CopyAsset` of the FBX carries its material by reference into the gitignored
  pack** (`hexagons_medieval_URP.mat` sits beside the source). Every existing polyperfect row in
  that importer has the identical shape, so this is precedent rather than a new hazard - but it is
  **not proven** that the copy renders textured on a machine without the pack, and the bundle is
  built on a machine that has it.
- **Suites not read this session that also sweep catalog art** and could react to a 29th row:
  `StructureCadenceRegression`, `StructureOrientationOracle`, `StructureNullMaterialSlotRegression`,
  `ManageDefenseCardRegression` (its id arrays are hand-authored, so it is the least likely).
  Named as the lead's gate risk rather than claimed clear.

---

### Unproven by this lane (CLAUDE.md sec.11B) - the STOP pass, superseded above

- **No candidate was seen.** Every geometric claim above is a glTF accessor bound or a mesh-node
  count; not one is a screenshot. Whether `building_tower_base_green` reads as *ruined* is the
  question this lane could not answer and did not pretend to.
- **`Tower_Wooden_Watchtower_L3` was not measured** - `.prefab` files have no glTF twin, and
  measuring one needs Unity, which this lane does not hold.
- **No bake, no gate, no screenshot.** The expected log lines are arithmetic from measured inputs.
- **The search is a name search.** An asset that IS a ruined watchtower but is named something with
  none of the ~15 tokens swept above would have been missed. The catalog walk, the `Structure_Art`
  address list and the doc catalogs were read in full, which bounds that risk but does not erase it.

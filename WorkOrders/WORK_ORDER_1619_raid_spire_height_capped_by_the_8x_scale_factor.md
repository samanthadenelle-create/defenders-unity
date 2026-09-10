# WO-1619 - Raid spire height is capped by the 8x scale factor: every baked spire lands short of the monument height the code asks for

**Status:** READY TO IMPLEMENT - **instrument first** (CLAUDE.md sec.12: no code edit until a bake
log line names target vs measured height, per spire, in the current build)
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

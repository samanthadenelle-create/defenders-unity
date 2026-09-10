# WO-1617 RESULT - the raid spire's siege exemption

**Status:** IMPLEMENTED - awaiting gate + re-bake (2026-09-09 lane BALLISTA); owner felt-test closes
**Lane:** BALLISTA (edit-only; no Unity, no git, no scenes, no JSON)
**Branch:** `dev`

---

## 0. ⚠ PATH DISCREPANCY - the lane brief named files that do not exist

The lane brief listed `Assets/_Modules/Village/World/Camps/RaidBaseGenerator.cs` and
`.../RaidBaseDresser.cs`. **Neither path exists.** `find . -name "RaidBase*.cs"` (run 2026-09-09,
excluding `Library/`) returns the real homes:

```
Assets/Editor/WallTools/RaidBaseGenerator.cs
Assets/Editor/WallTools/RaidBaseDresser.cs
Assets/Editor/Regression/RaidBaseLayoutRegression.cs
Assets/Editor/RaidBaseMatDiag.cs
```

The WO body itself names the correct paths (`Assets/Editor/WallTools/...`, sec.5), so the brief's
copy is the stale one. Worth flagging to whoever writes the next raid brief - it is the same
copied-state failure CLAUDE.md sec.2/sec.5/sec.16 each describe. `Assets/_Modules/Village/World/Camps/`
does exist and holds the RUNTIME side (`RaidSpire.cs`, `RaidGarrisonSpawner.cs`); the BUILDERS are
editor-only under `Assets/Editor/WallTools/`.

## 1. The defect, proven from captured data (not inferred)

Not a code-read conclusion - both lines are in a bake log already on disk,
`Builds/raidbase-bake.log` (also in `raidbase-bake-gate.log` and `raidbase-bake-look.log`, identical):

```
[RaidBaseGenerator] 'spire art 'tower_siege_tower'' imported FLAT (h=2.6m vs 4.4m wide)
  - applied the -90 X FBX-flat correction so it stands up. If the art is genuinely squat,
    this is a false positive - author a prefab with the right orientation instead.

[RaidBaseGenerator] SPIRE 'tower_siege_tower' placed at centre: 1200 HP, 14.4m tall,
  art='Structures/Ballista'.
```

A Ballista **is** 2.6 m tall and 4.4 m wide. `EnsureUpright`'s `b.size.y < widest * 0.8f` heuristic
read correctly-authored art as a fallen building, tipped it -90 X onto its edge, and `ScaleToHeight`
then magnified that wrong axis from **2.6 m to 14.4 m** - a 5.5x blow-up of the centrepiece of the
first raid the player sees. `EnsureUpright`'s own warning text predicted this exact false positive.

The other two spires in the same log are unaffected and set the "must not move" baseline:

```
SPIRE 'tower_arcane_spire' placed at centre: 2200 HP, 8.0m tall, art='Structures/ArcaneSpire_1'
SPIRE 'tower_arcane_spire' placed at centre: 3500 HP, 8.0m tall, art='Structures/ArcaneSpire_1'
```

**Scope is now PROVEN, closing WO-1617 sec.7's open question.** `grep -n centralBuilding` over
`Assets/Resources/Data/Canonical/scene-configs.json`:

| line | config | centralBuilding | affected |
|---|---|---|---|
| 51 | `player_outpost` | `tower_ground_archer` | no (architectural; maps to `Tower_Wooden_Watchtower`) |
| **76** | **`raider_camp_small`** (The Forsaken Camp - Easy) | **`tower_siege_tower`** | **YES - the only one** |
| 148 / 231 / 305 | `fortified_garrison` (Hard), `mage_enclave` (Extreme), `iron_bastion` | `tower_arcane_spire` | no |

Hard and Extreme are clean. **Easy only.**

## 2. ⚠ A PREMISE IN THE WO IS FALSE - `MapCatalogArt` has exactly ONE caller, and it is the spire

WO-1617 sec.2 item 2 says to *"leave the `siege -> Ballista` mapping for the turret slots that
legitimately want a machine."* There are **no turret callers.** `grep -rn MapCatalogArt Assets/`
returns two lines total (line numbers as at HEAD, before this change):

```
Assets/Editor/WallTools/RaidBaseDresser.cs:462   var spireModel = LoadVisual(MapCatalogArt(spireTok));
Assets/Editor/WallTools/RaidBaseDresser.cs:474   private static string MapCatalogArt(string catalogId)
```

`:462` is inside `ReskinCombatArt`, guarded by `if (t.name == RaidSpireName())`. The turrets go
through `RaidBaseGenerator.PlaceTowerProp`, which never enters the dresser's token map. So the
`siege -> Ballista` row could **only ever** hand siege art to the architectural centrepiece. I kept
the rows (they would answer correctly for a future turret-side caller, and deleting them would make
an unknown id fall through to the raw string) but they are now **unreachable for today's only
caller**, and the file says so at the method.

## 3. The interaction the WO did not name - and why the fix has to be an ART RESOLVER, not just a guard

`RaidBaseDresser.ReplaceChildrenWith` swaps the spire's **mesh** but never touches the host's
`localScale`, which `PlaceSpire.ScaleToHeight` already multiplied. `Dress` is called unconditionally
by the generator (`RaidBaseGenerator.cs:379`) and `ReskinCombatArt` unconditionally inside it
(`RaidBaseDresser.cs:103`). So **the dresser's art is what the player sees, fitted to bounds the
generator measured on the generator's art.**

Fixing the two defects independently, exactly as written, would therefore ship a new silent
inconsistency: Easy's centrepiece would become `ArcaneSpire_1` at raw prefab scale while Hard and
Extreme render the same model at 8.0 m. The fix instead resolves the spire's ART ID **once**, in the
generator, and has both sides call it - so the model the generator measures and the model the dresser
instantiates are always the same one.

## 4. What changed

### `Assets/Editor/WallTools/RaidBaseGenerator.cs`

- **`IsAuthoredSiegeMachine` is now `internal static` (`:968`)** - the ONE predicate, one definition,
  reachable by the dresser (same assembly `DeNelle.EditorWallTools`) so it asks rather than copies.
  This is the file:line the WO's acceptance 2 asks for. Both `PlaceSpire` and `PlaceTowerProp` call
  it; `PlaceTowerProp`'s pre-existing guard is untouched.
- **New `internal static string ResolveSpireArtId(string centralBuilding)` (`:991`)** - a siege id in
  the spire slot resolves to the module's existing `DefaultMageTowerId` (`tower_arcane_spire`,
  `:115`) and emits a `Debug.LogWarning` naming the id and telling the reader to fix the config.
- **`PlaceSpire` (`:529`)** now resolves its id through it, and skips **both** corrections for an
  authored siege machine: the `EnsureUpright` -90 X *and* the monument clamp. The guard is retained
  as belt-and-braces even though `ResolveSpireArtId` already keeps siege art out of the slot.
- **The monument fit is a TUNABLE with today's values as defaults** (`:122-124`):
  `SpireMonumentMultiplier = 1.6f`, `SpireMonumentMinHeight = 8f`, `SpireMonumentMaxHeight = 18f`.
  Numbers unchanged - only *who* the fit applies to changed.
- **New private `MeasuredHeight(GameObject)`** next to `ScaleToHeight` - an exempt spire still has to
  REPORT a real height to `RaidSpire.Configure` and the log line without being refitted.

### `Assets/Editor/WallTools/RaidBaseDresser.cs`

- **`MapCatalogArt` routes its id through `RaidBaseGenerator.ResolveSpireArtId` FIRST** (`:497`),
  before any token mapping, with the one-caller finding written at the method. **What it returns
  instead of `Ballista`: `ArcaneSpire_1`** (via `tower_arcane_spire` -> the existing `arcane` row).

### `Assets/Editor/Regression/RaidSpireSiegeRegression.cs` (NEW)

Marker `RAID_SPIRE_SIEGE_OK` / `RAID_SPIRE_SIEGE_FAIL`. Five cases: `CaseSpireConsultsThePredicate`,
`CaseTowerPropStillGuarded`, `CaseExactlyOnePredicate`, `CaseMonumentFitIsTunable`,
`CaseDresserRoutesSpireThroughTheDecider`.

**Registration line for the lead** (`DataRegression.cs` is lead-owned; modelled on `:741`):

```csharp
DeNelle.Core.Diagnostics.Guard.Try("Regression", "raid-spire-siege suite", () => { if (!DeNelle.Editor.Regression.RaidSpireSiegeRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[raid-spire-siege] " + r); });
```

## 5. RED-FIRST proof (acceptance 1)

`Assets/Editor/Regression/` is assembly `DeNelle.EditorRegression`, whose `.asmdef` does **not**
reference `DeNelle.EditorWallTools` (read at source 2026-09-09). The predicate therefore cannot be
*called* from a regression - which is exactly why WO-1607's `RaidBaseLayoutRegression` is itself a
source-text oracle. This suite is source-text for the same reason. **Option for the lead:** adding
`"DeNelle.EditorWallTools"` to `Assets/Editor/Regression/DeNelle.EditorRegression.asmdef` would allow
a behavioural case (`ResolveSpireArtId("tower_siege_tower") != "tower_siege_tower"`). That asmdef is
not this lane's file, so it was not touched.

RED was proven by replicating the five cases' assertions in python and running them against a
byte-copy of the HEAD files (scratchpad) and the working tree:

```
--- HEAD snapshot: RAID_SPIRE_SIEGE_FAIL
      [spire] PlaceSpire does not consult IsAuthoredSiegeMachine
      [spire] EnsureUpright unguarded
      [spire] no ResolveSpireArtId
      [tunable] missing SpireMonumentMultiplier = 1.6f
      [tunable] missing SpireMonumentMinHeight = 8f
      [tunable] missing SpireMonumentMaxHeight = 18f
      [tunable] PlaceSpire still hardcodes the clamp
      [dresser] ResolveSpireArtId missing
      [dresser] MapCatalogArt not routed
--- working tree: RAID_SPIRE_SIEGE_OK
```

**The named mutation:** delete the `IsAuthoredSiegeMachine(catalogId)` line from `PlaceSpire` (or drop
the `if (!authoredSiege)` guard on the clamp) - `CaseSpireConsultsThePredicate` reds. Add a second
`IsAuthoredSiegeMachine` copy in the dresser - `CaseExactlyOnePredicate` reds. Return `"Ballista"`
before the resolve in `MapCatalogArt` - `CaseDresserRoutesSpireThroughTheDecider` reds.

⚠ **The suite has NOT been run inside Unity from this lane** (edit-only, no Unity lock). The
`RAID_SPIRE_SIEGE_OK` marker on a fresh gate log is owed to the lead's gate.

## 6. Oracle collision: NONE

`RaidBaseLayoutRegression` was **not edited.** Every literal it greps was re-checked present after
the change (`RaidBaseDresser.Dress`, `RaidBaseDresser.LoadVisual`, `Zone_Gatehouse`, `Zone_Courtyard`,
`GarrisonSlot_`, `def.raidDress`, `def.props`, `AssetRoots.StructureContent`), and the literal it
requires ABSENT (`Resources.Load<GameObject>(plan.PrefabPath)`) is still absent.
`RaidArenaShapeRegression.cs:409` requires `RaidSpire` and `def.centralBuilding` in the generator -
both still present (`def.centralBuilding` survives inside `ResolveSpireArtId(def.centralBuilding)`).
The change is additive; nothing greped was removed. **`CaseGarrisonWipeWins` untouched** (WO-1607's
own open question, WO-1617 sec.6).

## 7. Gate hygiene

| check | result |
|---|---|
| brace balance, naive (CLAUDE.md sec.1) | `RaidBaseGenerator.cs` 237/237 · `RaidBaseDresser.cs` 131/131 · `RaidSpireSiegeRegression.cs` 29/29 - all OK |
| brace balance, literal-aware | 106/106 · 110/110 · 27/27 - all BALANCED |
| NUL bytes | 0 in all three |
| JSON canonical twins | **N/A - no JSON was changed.** Both `Assets/Resources/...` and `Assets/StreamingAssets/.../scene-configs.json` are untouched (`git status --porcelain` on both returns empty) |
| scenes | none touched |
| git | none run |

The regression file deliberately declares `private const char OpenBrace/CloseBrace` instead of bare
`'{'` / `'}'` comparisons: a body-extracting oracle written the obvious way carries two open-brace
literals and one close-brace literal, which trips CLAUDE.md sec.1's naive counter with a false
BRACE MISMATCH. One const each keeps the naive count honest. Comment says so in the file.

## 8. ⛔ RE-BAKE REQUIRED - the raid bases are baked scenes

Nothing on screen changes until the raid scenes are rebuilt. Per WO-1607 and the generator's own
log line, the sequence is **two steps, in this order**:

1. `DeNelle.Editor.RaidBaseGenerator.BuildAllRaidScenes`
   (menu **`Defenders/Walls/Build All Raid Scenes (config-driven)`**, `RaidBaseGenerator.cs:257`) -
   bakes `raider_camp_small`, `fortified_garrison`, `mage_enclave`.
2. `DeNelle.Editor.RaidNavBake.BakeAll` - **required**; it drops the RaidGround plane and bakes the
   legacy NavMesh. The generator's own closing log line says so verbatim: *"Without it the hero and
   every agent have nothing to walk on."*

Do not hand-edit `RaidBase_*.unity`. Never bake with the editor open.

**⚠ HOW ACCEPTANCE 3 IS MET - read this before checking the log.** The WO's literal wording is
*"the `tower_siege_tower` spire is not rotated and not scaled to the 8-18 m band."* That is satisfied
by the lead brief's SECOND option: **the spire slot never receives a siege id at all.** So the id in
the after-bake line CHANGES - it will read `tower_arcane_spire`, not `tower_siege_tower`. The guard
inside `PlaceSpire` is the belt behind that, and it is what the new regression pins.

**The "after" log line to check on that bake** - the Easy camp should read:

```
SPIRE 'tower_arcane_spire' placed at centre: 1200 HP, 8.0m tall, art='Structures/ArcaneSpire_1'
```

...with the `imported FLAT` warning for the spire **gone**, and the new siege-substitution warning
present **TWICE** for `raider_camp_small` - once per consumer of `ResolveSpireArtId` (the generator's
`PlaceSpire` and the dresser's `MapCatalogArt` via the unconditional `ReskinCombatArt`). That is
expected, not a bug; it was left un-deduped deliberately (a static seen-set in an editor tool to
silence one honest warning is the wrong trade, and the warning disappears entirely once the owner
fixes the config's `centralBuilding`). The HP stays 1200 (tier-driven, untouched).

## 9. Creative pick - PROPOSED, not decided (WO-1617 sec.7 / WO-1607 sec.0)

`raider_camp_small` authoring a siege-tower id as its `centralBuilding` is a **data** mistake, not
only a code one. The code now refuses to render it as a spire, but the config still says it.

**This lane did not change the JSON** - which spire art The Forsaken Camp (an orc scavenger camp
stripping an abandoned settlement, `hexagon-green` kit) should carry is the owner's creative call.
The substitution shipped here is the module's *pre-existing* default (`DefaultMageTowerId`, and the
dresser already fell back to `ArcaneSpire_1` at `:463`), so it is a fallback, not a pick.

**Proposal for the owner, in one line:** an arcane spire reads wrong for an orc scavenger camp - a
ruined watchtower or a wooden keep would read as "a place these scavengers took." When she picks,
change `scene-configs.json:76` `centralBuilding` to that id **in both canonical twins, byte-identical**
(`Assets/Resources/Data/Canonical/` and `Assets/StreamingAssets/Data/Canonical/`; memory
`canonical-json-edits-binary-only-verify-newlines`), and the substitution warning stops firing on
its own.

## 10. UNPROVEN - recorded honestly (CLAUDE.md 11B)

- **The "after" bake line.** Predicted in sec.8 from the code path; **not measured.** No bake was run
  (edit-only lane, no Unity lock). The "before" lines ARE measured, from `Builds/raidbase-bake.log`.
- **The suite has never executed inside Unity.** RED/GREEN was proven by a python replica of its
  assertions against a HEAD byte-copy. `RAID_SPIRE_SIEGE_OK` on a fresh gate log is owed.
- **No screenshot** (WO acceptance 5). An eye-level frame of the Easy camp centre after the re-bake
  is owed - memory `screenshots-are-primary-evidence-for-visual-defects`. The height and orientation
  claims here are log-proven, not eye-proven.
- **Raw prefab dimensions of `Structures/ArcaneSpire_1`** were not measured directly, but the
  mechanism behind the 8.0 m IS now proven (see the note below) - so Easy landing on 8.0 m is a
  derivation from measured inputs, not a guess. Still, it is a prediction until the bake runs.
- **`repo.visualHeight` IS DEAD - re-verified at source, not taken from KEY_FACTS.** A walk of
  `structures-catalog.json` prints `visualHeight = None` for BOTH `tower_arcane_spire` and
  `tower_siege_tower`, so `PlaceSpire`'s legacy reader (`RaidBaseGenerator.cs:537-538`) never fires
  and `targetHeight` is always the `9f` default. KEY_FACTS "ONE HEIGHT CADENCE" (*"authored zero
  times ... one legacy EDITOR reader survives in RaidBaseGenerator.cs"*) is CONFIRMED current, not
  stale. The reader was left in place - deleting it is out of this ticket's scope, but nobody should
  believe it does anything.
  **This also explains the 8.0 m, which is NOT the clamp minimum as it first appears.** Both spires
  target `clamp(9 * 1.6, 8, 18)` = **14.4 m**, yet the log reads 8.0 m - because `ScaleToHeight` caps
  its scale factor at `8f`, and `ArcaneSpire_1`'s raw prefab is ~1.0 m tall, so `1.0 x 8 = 8.0`. The
  fit is FACTOR-LIMITED, not height-limited. Worth a follow-up ticket in its own right: three of the
  four spires never reach the monument height the code asks for. Out of scope here; recorded, not fixed.
- **`compile`** - not run from this lane. No Unity was fired.

## 10b. Acceptance criteria, one row each

| # | criterion | state |
|---|---|---|
| 1 | RED first, mutation named | **MET** - 9 reds at HEAD -> 0; mutations named in sec.5 |
| 2 | Exactly ONE predicate, both placers call it, file:line stated | **MET** - `RaidBaseGenerator.cs:968`; pinned by `CaseExactlyOnePredicate` |
| 3 | siege spire not rotated / not in the 8-18 m band; log line before + after | **before MEASURED** (sec.1) · **after OWED to the re-bake** (sec.8; note the id changes - the slot no longer receives a siege id) |
| 4 | `MapCatalogArt` no longer returns `Ballista` for a spire; name what it returns | **MET** - returns `ArcaneSpire_1` (sec.4) |
| 5 | eye-level screenshot of the Easy camp centre | **OWED** - no bake, no screenshot from this lane |
| 6 | `RaidBaseLayoutRegression` stays green or its re-point is RULED | **MET, no collision** - suite untouched, every greped literal re-checked (sec.6) |
| 7 | owner felt-verifies on device and closes | **OWED to the owner**, after the gate + re-bake |

Also owed to the lead's gate: the `DataRegression.cs` registration line (sec.4) and
`RAID_SPIRE_SIEGE_OK` + `COMPILE_GATE_OK` on a fresh log.

## 11. Files

| file | state |
|---|---|
| `Assets/Editor/WallTools/RaidBaseGenerator.cs` | MODIFIED - `PlaceSpire` + the predicate + `ResolveSpireArtId` + tunables + `MeasuredHeight` |
| `Assets/Editor/WallTools/RaidBaseDresser.cs` | MODIFIED - `MapCatalogArt` only |
| `Assets/Editor/Regression/RaidSpireSiegeRegression.cs` | NEW - needs the sec.4 registration line in `DataRegression.cs` (lead-owned) |
| `WorkOrders/WORK_ORDER_1617_raid_ballista_stands_on_its_edge_at_monument_scale.md` | Status flipped |
| `WorkOrders/WORK_ORDER_1617_raid_ballista_stands_on_its_edge_at_monument_scale.RESULT.md` | this file |

Untouched, as instructed: `RaidBaseLayoutRegression.cs`, `DataRegression.cs`, every `.unity`, both
`scene-configs.json` twins.

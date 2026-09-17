# WO-1820 — the RaidSpire renders at 0.14 m: the raid's win target is 1/100 scale

**Status:** IMPLEMENTED
**Date opened:** 2026-09-17
**Lane:** raid art / world dressing — `Assets/Editor/WallTools/*` + `Assets/Editor/Regression/RaidPostOrientationRegression.cs`
**Found by:** WO-1817 (open item #1). **PRE-EXISTING** — not caused by that lane.
**Do NOT touch:** `Assets/_Modules/Village/Buildings/DefenseTower.cs`,
`Assets/Editor/Regression/RaidWallColliderAuditRegression.cs`.

---

## 1. The defect

`RaidBase_{IronBastion,fortified_garrison,mage_enclave}` render the RaidSpire — **the raid's win
condition** — at **0.14 m**. Only `raider_camp_small` is correct at 14.40 m.

`Builds/wo1817-rebake.log`, and byte-identical in `Builds/raid-rebake-1807.log` (so it predates WO-1817):

```
[wo1807] pose 'RaidSpire': ... boundsY=[0..14.4]  size=8.93x14.4x10.67   <- raider_camp_small
[wo1807] pose 'RaidSpire': ... boundsY=[0..0.14]  size=0.07x0.14x0.06    <- the other three
```

## 2. Why the camp is right and the other three are not — PROVEN, not inferred

The generator fits the HOST correctly in **all four** scenes (`SPIRE FIT config …` lines, same log):

| config | centralBuilding | rawHeight | **prefabScaleBefore** | appliedFactor | achieved |
|---|---|---|---|---|---|
| raider_camp_small | `tower_ruined_watchtower` | 1.500 | **1.000** | 9.600 | 14.40 m |
| fortified_garrison | `tower_arcane_spire` | 1.002 | **0.010** | 14.366 | 14.40 m |
| mage_enclave | `tower_arcane_spire` | 1.002 | **0.010** | 14.366 | 14.40 m |
| iron_bastion | `tower_arcane_spire` | 1.002 | **0.010** | 14.366 | 14.40 m |

**All four hosts achieve 14.40 m. The fit is not the bug.** The dresser then clads the host via
`RaidBaseDresser.ReplaceChildrenWith`, which parents the clad with `SetParent(parent, false)` — so the
clad keeps its own prefab `localScale` **and** inherits the host's. The prefab's authored scale is
therefore applied **TWICE**:

```
cladWorldHeight = hostLossyScale x cladPrefabLocalScale x cladRawMeshHeight
camp   : 9.600            x 1.000 x 1.500  = 14.40 m   ✔
others : 0.010 x 14.366   x 0.010 x 100.2  =  0.1439 m ✘   (= 14.40 x 0.010)
```

0.1439 vs the measured 0.14 — exact. **The error factor IS `prefabScaleBefore`.**

> ⛔ **THE CAMP IS RIGHT BY COINCIDENCE, NOT BY DESIGN.** Its art (`tower_ruined_watchtower`) authors
> `localScale 1.000`, and squaring 1.000 is harmless. Any spire art authored at Tripo's 0.010 is 100x
> too small. Reading the camp as "the working case to copy" would hide the real rule.

Same family as WO-1817 (the clad inherits the host's scale and is never fitted), but the arithmetic is
different and worth stating separately: for the turrets the clad was a DIFFERENT model from the host;
here it is the SAME model, and the defect is the double-application of its own prefab scale.

**Gameplay consequence, not only visual:** `RaidSpire.Configure(def.id, catalogId, tier.SpireHp, built)`
(`RaidBaseGenerator.cs:1219`) stores the ACHIEVED **14.40 m** into `RaidSpire._visualHeight`, and
`RaidSpire.EnsureHittable` (`RaidSpire.cs:200`) sizes the hero's contact collider from that number. So
in three scenes the player swings at a **14.4 m hit box wrapped around a 14 cm object** — the objective
is hittable from nowhere near where it appears.

## 3. The rule (ONE rule), and which authored value it uses

> **The spire clad renders at the height the generator fitted and RECORDED — `RaidSpire._visualHeight`.**

That number is the authored value, chosen deliberately and already load-bearing elsewhere:
`PlaceSpire` computes `targetHeight = Clamp(entry.repo.visualHeight * SpireMonumentMultiplier(1.6),
SpireMonumentMinHeight(8), SpireMonumentMaxHeight(18))` = **14.40 m** for all four configs, fits the
host to it, passes the achieved value into `Configure`, and the collider is already sized from it.

**TWO independent confirmations, not three — the component IS the collider's input, so counting both
would be counting one source twice:** (a) `PlaceSpire`'s clamp resolves to **14.40 m in all four
configs**, read off the `SPIRE FIT config …` log lines; (b) the camp's correct render measures
**14.40 m**. Two sources, which is enough.

**Two ordering/regression risks checked at source before implementing, both clear:**
- `Configure` runs BEFORE the dress pass in every scene (`SPIRE FIT` at log lines 551/933/1328/1811,
  the matching `pose 'RaidSpire'` at 646/1101/1545/2028), so `VisualHeight` is already 14.40 when the
  dresser reads it — not the serialized default of 9.
- The audit's PIN 3 (`ratio >= 0.65`) will not false-positive on the spire: the BROKEN clad measures
  `0.07 x 0.14 x 0.06` → ratio **2.0**, and a uniform scale preserves ratio, so the fitted 14.40 m clad
  is still 2.0. No exemption needed, and none was added.

**Why this source and not the alternatives** (stated because the brief asked which):
- *Not* the camp's 14.4 m as a constant — that is a measured coincidence (§2), not an authority.
- *Not* wall-top x a factor — the spire is a monument at the arena centre, unrelated to the perimeter,
  and `SpireMonumentMin/MaxHeight` already own "how tall should this be".
- *Not* a re-derivation of the clamp in the oracle — `SpireMonumentMultiplier` is `internal` to
  `DeNelle.EditorWallTools`, so an oracle copy would be duplicated state (CLAUDE.md §2/§5/§16).
  Reading the serialized `_visualHeight` off the baked component has no formula to drift.

Implementation: the same seam WO-1817 added — fit the clad's `localScale` directly (never
`ScaleToHeight`, whose factor clamp is 0.125–8x and this needs 100x), before `StripRootArt` so the
collider box is fitted to the final size, and seat the clad, not the host.

## 4. Scope — named, NOT fixed

1. `CornerPost_*` still renders at clad-native size (1.11 / 7.52 / **17.96** m) — WO-1817 §4.
2. The dresser clads catapult-hosted `Watchtower_*` with the kit's tower — WO-1817 §4, owner ruling.
3. Whether a 14.40 m monument is the right *creative* height is the owner's call; this ticket makes the
   render match what the generator, the collider and the component already agree on.

## 5. Acceptance

- [ ] The spire clad renders within 10% of `RaidSpire.VisualHeight` in all four baked scenes.
- [ ] `ReportCladPose` prints the spire's fitH/cladH like a watchtower's.
- [ ] `RaidPostAudit` collects and asserts `RaidSpire`; `RaidPostOrientationRegression` pins it.
- [ ] Rebake ONLY via `BuildAllRaidScenes` then `OwnedTownChain.RebuildFromRaid`.
- [ ] Fresh-log markers: `COMPILE_GATE_OK`, `RAID_POST_AUDIT_OK` (**63**, not 59 — the four spires
      join the census), `OWNED_TOWN_CHAIN_OK`, `RAID_POST_ORIENTATION_OK`.
- [ ] **RED proof must flag 35, not 31.** WO-1817's red proof predates the spire branch, so re-running
      it with `CladHeightTolerance = -1f` must now name **31 watchtowers + 4 spires**, with at least one
      line reading `the RAID OBJECTIVE renders`. A count of 31 means the spire branch never executed
      and the green would be theatre for exactly the object this ticket exists for.
- [ ] One file outside the stated silo: `Assets/_Modules/Village/World/Camps/RaidSpire.cs` — a single
      ADDITIVE public getter over an already-serialized field, no behaviour change. Named here because
      the in-silo alternative was copying `SpireMonumentMultiplier/Min/Max` into the regression
      assembly, which cannot reference `DeNelle.EditorWallTools` — i.e. duplicated state.
- [ ] Before/after courtyard frames of the spire, opened.
- [ ] `python tools/gate_brace.py` clean + zero NUL bytes on every `.cs`.

## 6. Suites that read BAKED spire state (for the lead's combined gate)

| Suite | Reads |
|---|---|
| `OwnedTownTemplateIdentityRegression` | walks every `RaidSpire` in raid + owned town + practice arena (`:250`) — census/identity |
| `RaidSpireSiegeRegression` | registered at `DataRegression.cs:2119` |
| `RaidPostOrientationRegression` | **gains the spire in this ticket** |
| `RaidPolishSavedProof` (`Assets/Editor/WallTools/`) | in-silo, 529 lines — may pin baked spire state |
| `RaidArenaShapeRegression`, `CameraRaidFramingRegression`, `BreakableContainerChestRegression` | TYPE shape only (interfaces/layers), not baked geometry — should be unaffected |

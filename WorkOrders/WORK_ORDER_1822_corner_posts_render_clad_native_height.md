# WO-1822 — CornerPost_* renders at clad-native height (1.11 m to 17.96 m across kits)

**Status:** CLOSED 2026-09-17 - owner felt-test PASS (validated 2026-09-17T13:50:54) - "owner: "close, as tested" (2026-09-17 raid-art lane: watchtower fit, spire, siege clad, corner posts)". PRIOR STATUS: FIXED - reached the owner and was felt-tested 2026-09-17; PRIOR STATUS: IMPLEMENTED (headless gates green, awaiting PO close)
**Date opened:** 2026-09-17
**Lane:** raid art / world dressing — `Assets/Editor/WallTools/*` + `Assets/Editor/Regression/RaidPostOrientationRegression.cs`
**Parent:** WO-1817 §4 item 2 / WO-1820 §4 item 2.
**Do NOT touch:** `Assets/_Modules/Village/Buildings/DefenseTower.cs`,
`Assets/Editor/Regression/RaidWallColliderAuditRegression.cs`.

---

## 1. Proven

`RaidBaseDresser.AuthoredHeightFor` returns **0** for a `CornerPost_*`: it carries neither a
`DefenseTower` (so no turret cadence) nor a `RaidSpire` (so no recorded visual height). With no target,
`FitCladToCadence` does nothing and the clad renders at `hostLossyScale x cladNativeHeight` — the
accidental product WO-1817 and WO-1820 each removed for their own object.

Measured in the current bake (`Builds/wo1821-diag.log`, `RAID_POST_AUDIT_OK 63`):

| scene | kit | wall top | CornerPost clad height |
|---|---|---|---|
| raider_camp_small | hexagon-green | 4.00 m | **1.11 m** (knee-high) |
| RaidBase_IronBastion | hexagon-green | 4.00 m | **1.11 m** |
| fortified_garrison | synty-castle | 5.00 m | **7.52 m** |
| mage_enclave | dungeon-stone | 4.00 m | **17.96 m** (over 4x its own wall) |

## 2. ⛔ THE GENERATOR INTENDS NOTHING HERE — the ticket's premise needs correcting

The ticket asked me to "read what the generator intends for corner towers at source". **It intends no
height at all, and that is deliberate, not an oversight.** `RaidBaseGenerator.BuildRing`
(`:1823-1832`) says so in its own words:

> *"Four visual corner posts via the 90-degree-around-origin mirror. They deliberately do NOT carry the
> 'Watchtower' token: authored combat towers above are budgeted to the tier's 12/16/20 worst-case DPS,
> while runtime-arming these extra posts bypassed that budget and could more than double incoming fire."*

`PlaceCornerTower` (`:2103`) seats and faces the post and **never scales it**. The only measurement the
ring takes from the tower prefab is `MeasureTowerHalf` — its XZ **half-footprint**, used to space the
wall run. There is no height authority for a corner post anywhere in the generator.

**So any corner-post height is an INVENTION, not a recovery of intent.** Saying so is the point: WO-1817
and WO-1820 could both point at a number the code already computed (`TurretCadenceHeight`,
`RaidSpire.VisualHeight`). This one cannot, and pretending otherwise would be the "copied state" failure
CLAUDE.md §2/§5/§8 keeps describing, arriving as a fake authority.

## 3. Rule — IMPLEMENTED (the rise is a labelled default, still unruled)

> **A corner post renders at `wallTop + CornerRise`.**

- ⛔ **`wallTop` is the KIT'S MEASURED WALL ART, and my first candidate was WRONG.** I proposed
  `WallTierData.TargetHeight`; its authored ladder is `3.0 / 3.8 / 4.5 / 5.2` — the **town** wall
  levels — while the raid kits measure **4.00 m** (hexagon-green, dungeon-stone) and **5.00 m**
  (synty-castle). Keying a corner to the town ladder would stand it against a wall of a different
  height than the one beside it. The dresser already measures the kit's own wall prefab
  (`MeasureTallest`) and already passes it in as `wallH` — that is the number used.
- **`CornerRise` is the invented half, and the least-invented value available is ~2.5 m**, derived from
  the one kit whose corner posts already read correctly on screen: `fortified_garrison` ships
  **7.52 m over a 5.00 m wall = +2.52 m**, and that frame
  (`Builds/raid-post-audit/RaidBase_fortified_garrison_CornerPost_Keep1_E.png`) has been opened and
  shows a correct corner tower. Every other kit is visibly wrong at 1.11 m or 17.96 m.

**Effect if ruled in (state it plainly before baking):** camp and Iron Bastion corner posts go
1.11 → ~6.5 m; mage_enclave 17.96 → ~6.5 m; the garrison is unchanged at ~7.5 m. That resizes visible
architecture in **three of four raid scenes**, which is why `CornerRise` is ONE labelled constant in ONE place: the ruling is a one-line edit. Measured after: 6.50 / 6.50 / 7.50 / 6.50.

⚠ **No muzzle consequence:** corner posts carry no `DefenseTower` (§2), so nothing about
`minMuzzleMargin` moves. Verified currently at garrison 2.99 / IronBastion 1.99 / mage_enclave 1.99 /
camp 2.00 (`Builds/wo1820-regression3.log`).

## 4. Acceptance

- [x] ONE expression owns the corner cadence, hoisted in `RaidBaseGenerator` beside
      `TurretCadenceHeight` — never a literal in the dresser, never a copy in an oracle.
- [x] `AuthoredHeightFor` answers for `CornerPost_*`; the existing `FitCladToCadence` / `SeatCladLocally`
      seam does the rest unchanged.
- [x] `RaidPostAudit` pin 5 and `RaidPostOrientationRegression` `CheckCladHeight` assert it within 10%,
      exactly as they do for `Watchtower_*`, with the regression deriving its expectation independently.
- [x] Gate RED-proved before green (the WO-1820 precedent: the flagged count must RISE by the number of
      corner posts — **28** across the four scenes — or the new branch never executed).
- [x] Rebake ONLY via `BuildAllRaidScenes` then `OwnedTownChain.RebuildFromRaid`.
- [x] Before/after frames of a camp and a mage-enclave corner post, OPENED.
- [x] `gate_brace` + NUL clean; `COMPILE_GATE_OK`; `REGRESSION_OK <n>/<n>` on fresh logs.

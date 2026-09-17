# WO-1821 — the dresser clads CATAPULT hosts with the kit's tower (and the "green pill" is not what it looks like)

**Status:** CLOSED 2026-09-17 - owner felt-test PASS (validated 2026-09-17T13:50:54) - "owner: "close, as tested" (2026-09-17 raid-art lane: watchtower fit, spire, siege clad, corner posts)". PRIOR STATUS: FIXED - reached the owner and was felt-tested 2026-09-17; PRIOR STATUS: IMPLEMENTED (headless gates green, awaiting PO close)
**Date opened:** 2026-09-17
**Lane:** raid art / world dressing — `Assets/Editor/WallTools/*`
**Parent:** WO-1817 §4 item 1 / WO-1820 §4 item 1. Same class as WO-1617.
**Do NOT touch:** `Assets/_Modules/Village/Buildings/DefenseTower.cs`,
`Assets/Editor/Regression/RaidWallColliderAuditRegression.cs`.

---

## 1. HALF THE PREMISE DID NOT REPRODUCE — read this before working the ticket

The ticket was raised as *"every siege platform shows a GREEN capsule/pill under the catapult art"* in
`Builds/raid-post-audit/RaidBase_IronBastion_RaidSpire.png`, with three suspects proposed from reading
code: `BuildFallbackTurret`'s primitive, a `MagentaGuard` placeholder, or an unstripped host root.

**Measured, not reasoned.** `RaidPostAudit.ReportNeighbours` (added by this ticket, WallTools silo) was
run over the current bake (`Builds/wo1821-diag.log`, `RAID_POST_AUDIT_OK 63`). It names every renderer
standing inside each photographed post's own footprint. Across **12 photographed posts in 4 scenes the
only primitives present are**:

```
NEAR 'KeepPlatform' mesh='Cube'  mat='RaidBase_IronBastion_KeepPlatform' size=(26.73,1.5,26.73) minY=0
NEAR 'RaidGround'   mesh='Plane' mat='RaidBase_IronBastion'              size=(142.99,0,142.91) minY=0
```

Both are legitimate (the keep slab and the arena floor). **There is no capsule, no pill, no
`TurretFallback`, no MagentaGuard placeholder inside any post.** All three proposed suspects are dead.

**And Iron Bastion has no catapults at all** — `Builds/wo1820-rebake.log` reads
`turrets for 'iron_bastion': … types [10xtower_arcane_spire]`. So "siege platform + catapult art" cannot
occur in the scene the frame came from. Catapults exist only in `fortified_garrison`
(`[6xtower_catapult, 1xtower_arcane_spire]`) and `raider_camp_small` (`[4xtower_catapult]`).

✅ **CLOSED, PROVEN — it is the clad's OWN MESH.** `RaidPostAudit.ReportOwnMeshes` (added after the
first pass, because "almost certainly the kit art" was an admission of a guess, §11B) names the post's
own sub-meshes. `Builds/wo1821-audit-after.log`:

```
OWN 'Visual' mesh='building_watchtower_green'  mat='hexagons_medieval_URP'
OWN 'Visual' mesh='building_tower_base_green'  mat='hexagons_medieval_URP'
```

Authored `hexagon-green` kit art, a sub-mesh of the tower itself. No pill, no primitive, no fallback.

## 2. WHAT IS PROVEN, AND IT IS A REAL DEFECT

`RaidBaseDresser.ReskinCombatArt` clads **every** `Watchtower_*` with the kit's tower model, including
the ten hosts the generator deliberately armed as siege machines. Measured in the current bake
(`Builds/wo1821-diag.log`):

| scene | host catalog id | what RENDERS |
|---|---|---|
| fortified_garrison / Watchtower_Archer_0 | **`tower_catapult`** (cadence 3.00) | `size=(1.38,3,1.39) ratio=2.16` — a slender **tower** |
| raider_camp_small / Watchtower_Archer_0 | **`tower_catapult`** (cadence 3.00) | `size=(3.1,3,3.1) ratio=0.97` — a **tower** |

So a turret the generator armed as a catapult renders as a watchtower. The authored siege art is
destroyed by `ReplaceChildrenWith`'s child-destroy before the clad is hung.

**This is exactly the WO-1617 class**, and the codebase already argued the principle there:
`IsAuthoredSiegeMachine` exists precisely so a siege machine is *not* treated as architecture — it is
exempted from `EnsureUpright` and from the height fit. The dresser then overrides the very art that
exemption was protecting. `MapCatalogArt`'s own docblock already records the sibling bug
(`RaidBaseDresser.cs:1221-1240`).

## 3. Rule — RULED BY THE LEAD, IMPLEMENTED AND BAKED

> **A catapult host keeps its authored siege art and gets NO tower clad.**

Implementation is small and sits in one place: `ReskinCombatArt` skips `ReplaceChildrenWith` when the
host's `DefenseTower.CatalogId` satisfies `RaidBaseGenerator.IsAuthoredSiegeMachine` — the ONE decider
that already owns this question. No second predicate, no id list.

⚠ **IT CHANGES WHAT THE PLAYER SEES on 10 of the 31 turrets in two of the four raid scenes** — those
positions read as towers before and as catapults now. The lead ruled it for this lane; the code now
honours the config author's own `tower_catapult` choice rather than overriding it. **The owner should
still see it once** (WO-1617 was the same class of call), which is why it is listed as open in the
RESULT rather than treated as closed.

**Consequence to state with the ruling:** `AuthoredHeightFor` returns the catapult cadence (3.00 m) for
these hosts. With no clad, the authored siege art is what renders, and `IsAuthoredSiegeMachine` already
exempts it from the height fit — so the art keeps its true size, which is the intent.

## 4. Acceptance (once ruled)

- [x] `ReskinCombatArt` skips the clad for siege hosts via `IsAuthoredSiegeMachine`; no second predicate.
- [x] `RaidPostAudit` / `RaidPostOrientationRegression` exempt siege-hosted posts from the clad-height
      pin (they have no clad) and instead assert the post carries a renderer at all — a siege host with
      neither clad nor authored art would otherwise be invisible and would pass silently.
- [x] Rebake ONLY via `BuildAllRaidScenes` then `OwnedTownChain.RebuildFromRaid`.
- [x] Frames of a garrison and a camp siege turret, before and after, OPENED.
- [x] `gate_brace` + NUL clean; `COMPILE_GATE_OK`; `REGRESSION_OK <n>/<n>` on fresh logs.

## 5. Delivered by this ticket already (no ruling needed)

`RaidPostAudit.ReportNeighbours` — permanent instrumentation that names every renderer standing inside a
photographed post (name, mesh, material, bounds). It is what turned "there is a green pill" from a
theory into a measurement, and it is the tool that will settle §1 the moment the owner points at the
frame. Kept per CLAUDE.md §12: instrumentation is permanent, never stripped.

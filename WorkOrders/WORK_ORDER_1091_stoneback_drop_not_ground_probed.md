# WORK ORDER 1091 — the Stoneback biome drop is handed to the seam 17 m in the air, and the hero's own clamp reverts the warp

**Status:** IMPLEMENTED - 6a5c7a36d on HEAD 2026-09-09 (was READY); owner felt-test closes
PRIOR STATUS: READY TO IMPLEMENT
**Minted:** 2026-09-09 by the UI seat (UI reserved block; banner bumped 1089 → 1095 in the same edit)
**Silo:** World / seams
**Severity:** P1 — a biome road door does not deliver the player
**Source:** F8 capture seq=4706, confirmed against the session's own `Player.log`

> ⚠ **UNTRUSTED WORKING-TREE EDIT EXISTS** in `HollowRoadsDropInjector.cs` — a UI-seat agent was
> stopped mid-rework. Braces balanced, 0 NUL bytes, **logic incomplete**. See
> `WO_1089_1094_WORKING_TREE_NOTE.md`.
>
> **REVIEWED 2026-09-09 (SILO 0B):** `git diff f4e4630e3 6a5c7a36d -- HollowRoadsDropInjector.cs`
> read in full and judged COMPLETE and COHERENT - the "logic incomplete" call above is SUPERSEDED.
> See `WORK_ORDER_1091_stoneback_drop_not_ground_probed.RESULT.md`.

---

## ⛔ First: the capture's own verdict is WRONG, and fixing that is part of this ticket

The instrumentation says **"THE WARP DID NOT HAPPEN"** and names
`SceneTransitionTrigger.RepositionPlayerAfterLoad / HeroLocomotion.WarpTo`. For this capture that is
false. The warp happened, landed exactly on target, and was reverted one frame later by the hero's
own clamp. A triage that trusts the message hunts the wrong two files.

## The proving trace (Player.log, consecutive)

```
[Flow:Seam]      warp via HeroLocomotion.WarpTo((-400.00, 17.00, 0.00)) (disable->move->re-enable agent)
[Flow:DeathTrace] HERO MOVED: (0.00, 0.08, -4.71) -> (-400.00, 17.00, 0.00) (400.4m)
                  by <RepositionPlayerAfterLoad>b__0 reason=HeroLocomotion.WarpTo explicit warp
[Flow:Seam]      WarpTo sample MISS for (-400.00, 17.00, 0.00) (no navmesh within 5m) ... hero will land OFF-MESH.
[Flow:Seam]      WarpTo post-warp: agent.isOnNavMesh=False @ (-400.00, 17.00, 0.00)
[Flow:Seam]      repositioned: requested (-400.00, 17.00, 0.00), hero now @ (-400.00, 17.00, 0.00)
[Flow:HeroLoco]  playable-bounds CLAMP relocated the hero: (-400.00,0.00) -> (-50.00,0.00)
                 [±50 off-mesh guard; agent=enabled/off-mesh, cc=<none>]
```

That last line is the proof. The settle poll (`HollowRoadsDropInjector.cs:501-521`) never saw the
hero at the promised point because the clamp reclaimed it within one frame — hence the full 3.01 s
timeout and the captured (-50.34, 0.11, -2.83).

## Root cause — an unhonoured contract, one line

`HollowRoadsDropInjector.cs:366` sets `seam.targetPosition = drop.Point` **verbatim, with no
ground/navmesh probe**. `drop.Point` is built at `BiomeRoads.cs:420` as
`new Vector3(dir.x*reach, worldBounds.center.y, ...)` → **y = 17**, the terrain bounds *centre*
height (terrain seated at (-500,-4,-500), size 1000×42).

`BiomeRoads.cs:327-330` states in terms that **the caller** must ground-probe the point and that a
drop which cannot be grounded must **FAIL LOUDLY at that seam**. The caller does not. Everything
downstream is dominoes: `HeroLocomotion.cs:536` samples with a 5 m radius, misses (17 m up), writes
the raw point and re-enables the agent off-mesh (`:544-549`); the next `Update` clamps to
`-PlayableHalf = -50` (`:1391-1394`) and the off-mesh ground-snap (`:1424-1452`) settles at
(-50.34, 0.11, -2.83). `ZoneManager.cs:65` then classifies |x|,|z| ≤ 52 as `RegionId.Village`, which
is why the message says "Elarion".

## Ruled out, with evidence

- **`SceneTransitionTrigger.RepositionPlayerAfterLoad`** — ran to completion and issued the warp
  (`warp via`, `repositioned:`, `carry: re-homed`, `fade-back-in ... complete` all present). The
  persistent-host fix at `:546-560` held.
- **A spawn placement overriding it** — `HubSpawnInjector.Apply` (`HubSpawnInjector.cs:174`) fired
  **before** the seam warp: `HERO MOVED: (-9.65,0.07,19.28) -> (0.00,0.08,-4.71)`. That is the
  position the seam warp moved *from*. One-shot per scene handle (`:128`), never re-fires. No
  `HERO MOVED` chokepoint line exists after the seam warp. `HeroControlEnsurer`'s carried-hero
  re-home is scoped to raid/composed-dungeon scenes only (`:323-325`).

## Fix spec — one file: `Assets/_Modules/Village/World/HollowRoadsDropInjector.cs`

**(A) Ground the point before handing it to the seam.** At `:366`, `NavMesh.SamplePosition` the
derived point with a vertically generous radius — the file already trusts 12 m for arrival at `:111`,
reuse that rather than inventing a number — and seat `seam.targetPosition` at `hit.position`. On a
MISS, **refuse the drop** through the file's existing Fail + `Notify` path, exactly the behaviour
`BiomeRoads.cs:327-330` assigns to this caller. Fail closed, like the rest of WO-1604. One probe
covers both failure modes: a bad Y, *and* "no navmesh reaches 400 m out".

**(B) Teach the settle-timeout message a third case.** Distinguish *warped, then relocated* from
*never warped*. `HeroLocomotion` already emits the attributing Warn when the clamp relocates, so the
failure can name the real owner instead of accusing the seam.

## Do NOT touch

`HeroLocomotion.cs`, `SceneTransitionTrigger.cs`, `BiomeRoads.cs`, `HubSpawnInjector.cs`,
`ZoneManager.cs`. Two latent defects in `HeroLocomotion` are tracked separately in **WO-1094** — do
not fold them in here.

## Acceptance criteria

- [ ] A drop point that cannot be grounded refuses the door and says so loudly, rather than seating a
      door that cannot work.
- [ ] The Stoneback door delivers the hero inside the 8 m settle radius, confirmed by the settle poll
      succeeding rather than timing out.
- [ ] The timeout message, when it does fire, names whether the hero reached the point and was moved,
      or never reached it.
- [ ] Brace balance; gate markers on a fresh log.
- [ ] Owner felt-verifies all four biome doors and closes.

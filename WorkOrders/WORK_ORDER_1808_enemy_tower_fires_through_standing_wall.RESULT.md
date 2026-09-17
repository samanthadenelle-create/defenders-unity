# WO-1808 — RESULT: the enemy-owned tower pick now honours the wall

**Status:** IMPLEMENTED
**Date:** 2026-09-16
Code is in the tree and NOT gated — another lane owns the Unity project; the lead gates the combined tree.

## Proof of the root cause (measured this session, not inferred)

`logs/device/raid-window-1955.txt`:

```
grep -c -- "-> FireAtParty (EnemyOwned)"  => 107
grep -c -- "<- FireAtParty"               => 107
grep -c "Flow:TowerLoS"                   =>   0
```

`DefenseTower.BlockedByWall` logged a `TowerLoS` line on every call (old `:926-933`). 107 enemy-owned
shots with **zero** LoS lines proves the check never ran on that path. Confirmed in source: the LoS call
existed at **one** site only — `Acquire()` (the PLAYER pick) at old `:894`. The enemy-owned pick
`AcquireParty` (old `:737-766`) filtered range (`:750`) + air (`:751`) and nothing else.

## "Through", not "over" — measured from the scene

Parsed from `Assets/Scenes/RaidBase_fortified_garrison.unity` (serialized values):

- 118 `WallSegment`s, **all `m_Layer: 8`**; layer 8 **is `Structure`** (`ProjectSettings/TagManager.asset`,
  `layers:` index 8). All 118 BoxColliders `m_Enabled: 1`, `m_IsTrigger: 0`.
- World-composed collider height **5.00 m, base y 0.00, top y 5.00** (two authored footprints, both
  resolving to 5.00 m after the parent `m_LocalScale.y` chain).
- 7 `DefenseTower`s, **all `Allegiance: 1` (EnemyOwned)**; `Range` 26.95 (×6) and 16 (×1); names read off
  their `!u!1001` prefab-instance modifications as **`Watchtower_Archer_0..4` + `Watchtower_Mage_0..1`** —
  the owner's exact list. World **Y = 2.50** for six, **0.1197** for `Watchtower_Archer_3` (parent is the
  scene root at y 0, scale 1).
  ⚠ **Correction recorded on purpose:** the first parse printed `y=0.00` for all seven. Those GameObject docs
  are ` stripped` prefab instances, my `--- !u!N &id` regex did not match the suffix, and the transform walk
  returned its initial value. Re-parsed with `( stripped)?` + the `m_Modifications` read. The wall numbers
  were never affected (their names and the 5.00 m scale chain both resolved).

Muzzle = `transform.position + up*2` → **y 4.50** (six towers) / **2.12** (`Archer_3`); the wall collider top
is **5.00**. Every muzzle is below the top and the line only descends toward a ground target, so **the
linecast the fix adds hits — it is THROUGH, not over.** The margin is only **0.50 m** on six of seven: a
shorter wall collider would make "over" real, which is gap 2 below. The WO-1719 "3 m collider vs 15 m
renderer" reading is a TOWN/build-mode wall
(`WallSegment._height = 3f`, `walls.json` L0 `targetHeight: 3.0`) and does not describe this scene.

## Files changed

| File | Ranges | What |
|---|---|---|
| `Assets/_Modules/Village/Buildings/DefenseTower.cs` | **:751-760** (call at `:760`) | LoS gate added to `AcquireParty`, after the range + air gates: `if (BlockedByWallToParty(d, p)) continue;` |
| | **:921-1008** (`BlockedByWall` `:927`, `BlockedByWallToParty` `:949`, `BlockedByWallAt` `:965`, `AcquirePartyForTest` `:1002`; player gate untouched at `:903`) | `BlockedByWall(IDamageable)` split into a position-based core `BlockedByWallAt(Vector3, bool, string ownerMode)`; new `BlockedByWallToParty(IDamageableStructure, Vector3)`; `TowerLoS` trace now prints `owner=<mode>` and the throttle key is per-mode; new public `AcquirePartyForTest(IDamageableStructure, out Vector3)` oracle seam |
| `Assets/Editor/Regression/EnemyTowerWallLosRegression.cs` | new, 218 lines | 4-case oracle (clear→acquired, walled→rejected, walled+flyer→acquired, source-wiring lint) |
| `Assets/Editor/Regression/DataRegression.cs` | **:350** | the one allowed edit outside the silo — `Guard.Try` registration as `[enemy-tower-wall-los]` |

**The bridge carries no reflection and no cross-seam cast:** the shared core takes a `Vector3` + a `bool`,
so `IDamageable` and `IDamageableStructure` stay disjoint contracts (this file's own header rule). The
party position is the one `AcquireParty` already computed for its range test.

**Flyer exemption kept on both paths** (`target as ICombatLayered`, `Layer == Flying` → never blocked).
It is **inert for today's party**: `HeroHealth:35`, `TroopController:45` and `StoryCompanion:51` each
declare `IDamageableStructure` only — verified at source.

## Gates run here

```
python tools/gate_brace.py <3 files>  -> GATE_BRACE_SUMMARY bad=0 of 3   (exit 0)
NUL scan                             -> all three nul-free
```

`COMPILE_GATE_OK` / `REGRESSION_OK` NOT run — another lane owns the Unity project; the lead gates.

## Bounce fixed (2026-09-16, lead's compile log)

CS0535 on both oracle dummies — I wrote them against `IsAlive` + `ApplyContactDamage` and missed
`CombatFaction Faction` (`IDamageableStructure.cs:81`). Both now declare it explicit-interface as
`CombatFaction.Friendly`, mirroring the real party bodies (`HeroHealth.cs:2331`,
`TroopController.cs:448`/`:458`). Verified at source that `AcquireParty` has **no faction filter**
(IsAlive, range, air, LoS only), so the value cannot mask the wall check either way.
Also corrected a stale line cite in the new `AcquireParty` comment (`:894` → `:903`).

## Second bounce fixed (regression, Builds/regression.log 21:40)

1. **`[tower-wall-los]` broke because of my refactor, and I fixed the CODE, not the oracle.**
   `TowerWallLosRegression.RequireLosGate:84` pins the regex `CombatLayer\.Flying\)\s*return\s+false`
   against `DefenseTower.cs`. Hoisting the exemption into `BlockedByWallAt` as a `bool targetIsFlyer`
   compiled and silently lost that pin. The exemption is now back INLINE in the pinned literal shape at
   **both** entry points (`BlockedByWall` and `BlockedByWallToParty`), the core's bool parameter is gone
   (`BlockedByWallAt(Vector3, string ownerMode)`), and a comment at each site says why the two-word
   duplication is deliberate. **The oracle was not re-pointed** — it was asserting something true and
   valuable, and the refactor was the thing that was wrong.
2. **Hollow pass removed:** `CheckSourceWiring`'s `!File.Exists` branch added a NOTE and returned, so the
   suite read green having checked nothing. It now hard-FAILS naming the path, matching
   `TowerWallLosRegression.RequireLosGate:75`. Not a skip — the file is the ticket's subject.

Re-verified in the tree: the pinned regex matches **2×**, and `GetMask("Structure")`, `Physics.Linecast`
and `BlockedByWall` are all still present for the rest of that lint.

## Not proven (handed up, deliberately not guessed)

1. **The 5.00 m is serialized, not a runtime `Collider.bounds` read.** The new EnemyOwned `TowerLoS` line
   prints `hitColliderBoundsY=[min..max]` + `hitPoint`, so one grep of the next device capture settles it.
2. **Renderer height in the raid scene was not measured** (renderer bounds are not serialized). If a future
   capture shows `owner=EnemyOwned … blocked=false` with a wall visually on the line, that IS the
   collider-vs-renderer gap.
   **FOLLOW-UP FOR THE LEAD (outside this silo, not implemented):** raise the raid wall blocker to the art
   height in the raid wall authoring path (`Assets/Editor/WallTools/*` / the raid base generator) —
   `WallSegment.RebuildCollider` only runs from `Configure()`, which raid walls never receive
   (`WallSegment.cs:511-512`), so the raid scene's collider height is whatever the generator baked. Note
   the generator also cannot be relied on for the **layer**: `WallSegment.Awake` (`:626-629`) does *not*
   set it, so layer 8 in this scene is baked, and a future scene could bake it wrong and silently
   degrade-open. A scene-audit oracle for "every raid `WallSegment` is on Structure" is the cheap guard.
3. **`ArcaneTower` / `TowerCombat` were not audited** for the same missing-gate shape on their enemy-owned
   paths. Out of scope; worth a lead ticket.
4. **Runtime behaviour of the fix is unverified by me** — the oracle proves the pick, the owner's felt-test
   proves the raid.
5. **Wall RENDERER height in this scene: still unmeasured**, so with only 0.50 m of muzzle-to-collider-top
   margin, "over" cannot be excluded for a wall whose collider is shorter than its art. Nothing in
   `Assets/Editor` or `tools/` pins the old `TowerLoS` message text (grepped `TowerLoS`,
   `BlockedByWall fPos`, `hitColliderBoundsY` — only this new file matches), so the reshaped line breaks
   no existing oracle.
6. **`Assets/Editor/Regression/EnemyTowerWallLosRegression.cs.meta` does not exist yet** — Unity mints it on
   the lead's gate run and it must be staged with the lane by explicit path.

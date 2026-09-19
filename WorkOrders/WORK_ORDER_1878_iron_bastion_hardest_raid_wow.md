# WO-1878 — Iron Bastion is the hardest raid: max archer + wizard towers, forced fights, landscape enclosure, wow

**Status:** IMPLEMENTED — gated 2026-09-19 `COMPILE_GATE_OK` (`Builds/cg-wave1876.log`) + `REGRESSION_OK` (`Builds/r-wave1876c.log`). Scene bake of RaidBase_* still required for the on-disk Bastion ring. FIXED after tester APK.

**Owner, verbatim (2026-09-18, on that frame):**
- "supposed to be fog on ground and thunder storms in sky"
- "not sure reason for the outter wall. doesnt make sense should be a landscape" / "not a wall thats targetable"
- "being that this is the hardest level" / "I would expect the difficulty to reflect it, fully upgraded toweers forced encounters" / "archer and wizard towers" / "special vfx, the whole wow factor"

Fog/storm stays **WO-1868** (READY, bounced). This ticket is the **hardest-raid identity**: what you fight, what you see, what you cannot cheese.

## Proof (opened PNG)

`Logs/device/issue-now.png`: dirt yard, **grey cube/stone box** on the left (FLAG label), wooden siege towers on a lower grey ring, **clear blue sky**, two grey puffs, Troops 0/0, Spire 100%. That enclosure reads as a fort wall. Wooden machines read as garrison props, not max-tier archer + wizard. No storm, no ground fog (1868).

⚠ The dress in this PNG matches **Broken Garrison** `raidDress.kit = synty-castle` (`scene-configs.json:167-172`). Iron Bastion authors **no `raidDress` at all** (`:304-361`). Either the device is on garrison, or Bastion is wearing garrison leftovers. The ticket is still **Iron Bastion** (`difficulty: Extreme`, prize raid). Confirm scene on the next frame (`RaidBase_IronBastion` vs `RaidBase_fortified_garrison`) before rebaking the wrong scene.

## What the catalog actually authors (read 2026-09-18)

`iron_bastion` (`Assets/Resources/Data/Canonical/scene-configs.json:304-361`) is still a **field-for-field clone of `mage_enclave`** on every difficulty stat (`docs/RAID_DIFFICULTY_LEVERS_2026-09-16.md` §1). Counts: `archerTowerCount` 7, `mageTowerCount` 3, `towerPlacementStyle` OverlappingFire, `eliteCount` 3, Extreme.

**The type palette is the defect.** `towers[]` is only `{ type: tower_arcane_spire, count: 2 }` (`:318-322`). `RaidBaseGenerator.ResolveTowerTypes` (`:1455-1469`) expands **that same array for BOTH archer and mage slots**. `DefaultArcherTowerId` (`tower_ground_archer`) is used only when `towers[]` is empty. So all 10 turrets draw from **spire-only**. There are no archer towers on the hardest raid.

Bastion also has **no `raidDress`** (Enclave does). Top tier ships with no authored cover and no Bastion kit.

WO-1763 already put Bastion HP/level on the remote rail (`raid.difficultyMultPctBastion` 160, `raid.levelOffsetBastion` 5). **Do not invent a second difficulty authority.** This ticket is identity + bake: types, upgrade visual, enclosure, encounters, wow shots.

## Rulings (this ticket)

1. **Hardest raid looks and fights like Extreme.** Fully upgraded **archer** towers AND **wizard** (Arcane Spire / mage) towers. Counts stay 7 + 3 unless a bake proves overlap is unreadable.
2. **Forced encounters.** Overlapping fire is already authored — it must be *true* on the floor: no safe pocket on the approach, no safe pocket at the spire. Garrison actually engages (hold posts / wake), not Troops 0/0 idle silhouettes in the yard.
3. **Outer enclosure is landscape, not a targetable wall.** `ArenaBoundaryRing` (WO-1632) is the arena edge: rocks/hills, colliders for containment, **no `WallSegment` / `IDamageable`**. Camp defenses are the towers + spire. Do not wrap the yard in a grey `Wall_Outer_*` box the player can shoot.
4. **Wow.** Max-tier tower VFX (archer arrows + wizard bolts, not pellets). Atmosphere is WO-1868 (fog + thunderstorm). Do not ship another pair of `PP_LightnigStormCloud` puffs as "done."
5. **No mesh-swap on the hero.** No new skybox that leaks into the hub.

## Fix

### A. Config (both Canonical twins)

`iron_bastion.towers[]` must weight **archer + wizard**, not spire-only. Example shape (counts are the palette weights, not the placed totals — placed totals remain `archerTowerCount` + `mageTowerCount`):

```json
"towers": [
  { "type": "tower_ground_archer", "count": 7 },
  { "type": "tower_arcane_spire", "count": 3 }
]
```

Author a Bastion `raidDress` kit (not a copy of garrison synty-castle wood, not empty). Cover props allowed. Dual-write Resources + StreamingAssets.

### B. Bake (`RaidBaseGenerator` + `RaidBaseDresser`)

- Archer slots pick from archer types; mage slots from mage types. **Stop feeding both palettes the same `towers[]` list** if that is what keeps archers as spires (`ResolveTowerTypes` is called twice with the same `def` today).
- Place those turrets at **max upgrade visual** (`RepoProps.MaxStructureLevel` / the row's authored max — read the const, do not hardcode). Extreme does not spawn L1 town sticks.
- Outer **landscape** ring via `ArenaBoundaryRing` only. Camp `Wall_Outer_*` if it still exists is camp defense art that must clad; if it is only a cube box with no dress, **remove it from Bastion** rather than restyle it into another wall.
- Rebuild `RaidBase_IronBastion` via the sanctioned builder (`DeNelle.Editor.VillageSceneBuilder` / raid bake path). **Never hand-edit the .unity.** Unity editor closed. Nav bake after.

### C. Forced encounters (runtime, existing seams)

Garrison composition + elites already author. Prove on a frame that bodies **fight** (not 0/0). If hold-posts / `RaidGarrisonSpawner` skip Extreme, that is this ticket's runtime slice — instrument first (`[Flow:Garrison]`), then fix the dead step. Do not greenfield a second AI.

### D. Wow (with 1868)

Tower projectile style is WO-1868's arrow/bolt slice. This ticket requires a **frame of a max-tier archer shot and a wizard shot** on Bastion after the type palette is real. Atmosphere frames stay 1868.

## Files

- `Assets/Resources/Data/Canonical/scene-configs.json` + StreamingAssets twin (`iron_bastion` `towers[]`, `raidDress`).
- `Assets/Editor/WallTools/RaidBaseGenerator.cs` (`ResolveTowerTypes` `:1455`, `PlaceTowers` `:1322-1323`; max-level visual).
- `Assets/Editor/WallTools/RaidBaseDresser.cs` / `Assets/Editor/ArenaBoundaryRing.cs` (landscape edge, no targetable box).
- `Assets/Scenes/RaidBase_IronBastion.unity` **only via bake**.
- Regression: Extreme Bastion bake/report names **both** `tower_ground_archer` and `tower_arcane_spire` in `TypeSummary`; landscape boundary pieces have **no** `WallSegment`; revert `towers[]` to spire-only → RED.

## Acceptance

- `COMPILE_GATE_OK` + `REGRESSION_OK n/n` on a fresh log.
- Device/editor frames of **RaidBase_IronBastion** (name the scene in the log): (1) max-tier archer + wizard towers visible and firing their real VFX; (2) landscape enclosure, no grey targetable outer box; (3) a forced fight in the yard (bodies engaged, not 0/0). Lead **opens the PNGs** before IMPLEMENTED.
- Fog/storm frames are WO-1868, not this ticket's close.
- Owner felt-verify closes.

## Not in scope

- WO-1868 sky/fog/lightning.
- WO-1763 remote HP/level knobs (already shipped).
- WO-1876 owned-town HUD.
- Rebaking every RaidBase_* unless Bastion's generator change is global (then say so and bake all).
- Hand-editing `.unity`.

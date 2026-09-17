# WO-1808 — An enemy-owned raid tower fires through a standing wall (no LoS gate on the party path)

**Status:** IMPLEMENTED
**Silo:** `Assets/_Modules/Village/Buildings/DefenseTower.cs` + one new `Assets/Editor/Regression/*`
**Reported by:** owner, build 372984, Forsaken Camp raid 19:55:43–19:59:01 local 2026-09-16,
scene `RaidBase_fortified_garrison` — *"the tower is attacking through the wall, maybe over but feels
like through"*.

---

## 1. The proving lines (captured, not inferred)

Source: `logs/device/raid-window-1955.txt` (38 302 lines, the owner's raid window).

| Measurement | Value |
|---|---|
| `[Flow:DefenseTower] -> FireAtParty (EnemyOwned)` entries | **107** |
| `VerifyBolt (party) ok` | **107** |
| `[Flow:TowerLoS]` lines **anywhere** in the window | **0** |

`DefenseTower.BlockedByWall` (`DefenseTower.cs:913-935`) emits a throttled `TowerLoS` line on **every**
invocation (`:926-933`, unconditional inside `if (FlowTrace.Enabled)`, and `FlowTrace.Enabled` is proven
live by the 107 `DefenseTower` Enter lines in the same window). **107 shots and zero LoS lines proves the
LoS check never ran.**

## 2. Root cause — the enemy-owned pick never had the gate

- Enemy-owned tick: `UpdateEnemyOwned` (`:698-711`) → `AcquireParty(out targetPos)` (`:703`) →
  `FireAtParty(target, targetPos)` (`:710`).
- `AcquireParty` (`:737-766`) filters on **range** (`:750`) and the **air gate** (`:751`) and **nothing
  else**. There is no `BlockedByWall` call in it.
- `BlockedByWall`'s **only** caller was the player-owned pick `Acquire()` at **`:894`** (`Acquire` spans
  `:855-902`). Its own comment at `:890-893` records the 2026-07 fix: *"DefenseTower's Acquire had NO LoS
  check, so it fired through every perimeter wall."* That fix was applied to the player path only; the
  EnemyOwned path was added later and never received it.

So a garrison turret has fired through standing walls since the EnemyOwned path existed.

## 3. Is it "through" or "over"? — measured from the scene, not from a comment

Read out of `Assets/Scenes/RaidBase_fortified_garrison.unity` (serialized values, parsed this session):

- **118** `WallSegment` components. Every one: `_height: 3`, **`m_Layer: 8`** — and layer 8 **is
  `Structure`** (`ProjectSettings/TagManager.asset` `layers:` list, index 8).
- Their `BoxCollider`s: **118/118 `m_Enabled: 1`, `m_IsTrigger: 0`**; world-composed
  **collider height = 5.00 m, base y = 0.00, top y = 5.00** (local `m_Size.y` × the accumulated parent
  `m_LocalScale.y` chain; two authored footprints, 1.4656 × 3.4118 and 0.9887 × 5.057, both resolving to
  5.00 m).
- **7** `DefenseTower` components, **all `Allegiance: 1` (EnemyOwned)**, `Range` 26.95 (×6) and 16 (×1).
  They are **prefab instances**, so their transforms live in `m_Modifications` on their `!u!1001` docs and
  their GameObject docs are ` stripped` — the first parse silently returned defaults for all seven and is
  corrected here. Read properly: names **`Watchtower_Archer_0..4` + `Watchtower_Mage_0..1`** (the owner's
  exact list), parent = the scene root `RaidBase_fortified_garrison` at **y 0.0, scale 1.0**, so
  **world Y = local Y = 2.50 for six of them and 0.1197 for `Watchtower_Archer_3`**.

The muzzle is `transform.position + up * 2` (`FireAtParty:776`, identical to the LoS start). So the muzzle
sits at **y = 4.50** (six towers) / **2.12** (`Archer_3`), and the wall collider top is **y = 5.00** on the
**Structure** layer. Every muzzle is **below the wall top**, and the line only descends toward a ground
target. **So it is THROUGH, not over: the linecast the fix adds will hit.**

⚠ **The margin is only 0.50 m** on six of the seven towers. A shorter wall collider (the town 3.0 m ladder,
or a generator that bakes less than the art height) would put the muzzle ABOVE the collider top and the shot
would legitimately clear it while the player sees a wall — which is exactly the follow-up in §3's gap list.
The WO-1719/1720 "collider 3 m vs renderer 15 m" gap is a **town/build-mode** wall reading
(`WallSegment._height = 3f` default, `walls.json` L0 `targetHeight: 3.0`) — it does **not** describe this
raid scene and must not be conflated with it.

**Not proven (honest gaps):**
- 5.00 m is the **serialized** collider size, not a runtime `Collider.bounds` read. The new EnemyOwned
  `TowerLoS` line prints `hitColliderBoundsY=[min..max]` and `hitPoint`, so the **next** device capture
  settles it from one grep.
- The wall **renderer** height in this scene was **not** measured (renderer bounds are not serialized).
  If a future capture shows `blocked=false` with a wall visually present, that is the collider-vs-renderer
  gap and **belongs to the lead as a follow-up** (raise the raid wall blocker to the art height in
  `Assets/Editor/WallTools/*` / the raid base generator — **outside this silo, deliberately not touched**).
- Whether any *other* enemy-owned shooter (`ArcaneTower`, `TowerCombat`) has the same hole: out of scope
  here; `TowerCombat.BlockedByWall` exists and is the shape this fix mirrors.

## 4. The fix (this WO)

`Assets/_Modules/Village/Buildings/DefenseTower.cs`

1. `BlockedByWall(IDamageable)` is split: a **position-based core**
   `BlockedByWallAt(Vector3 tPos, bool targetIsFlyer, string ownerMode)` holds the mask resolve, the
   degrade-open rule, the linecast and the `TowerLoS` trace. The trace now **names the owner mode** and
   the throttle key is per-mode, so both paths can log inside the same second.
2. `BlockedByWall(IDamageable)` → core, mode `PlayerOwned`. Behaviour byte-identical.
3. New `BlockedByWallToParty(IDamageableStructure, Vector3)` → core, mode `EnemyOwned`. This is the
   `IDamageable` ↔ `IDamageableStructure` bridge: the core takes a **Vector3 + a bool**, so neither
   interface is cast to the other and **no reflection** is used. The party position is the
   `MonoBehaviour.transform.position` `AcquireParty` already computed.
4. `AcquireParty` calls it **after** the range + air gates, so the hot linecast only runs on candidates
   that already passed the cheap filters.
5. **Flyer exemption kept** on both paths (`target as ICombatLayered`, `Layer == CombatLayer.Flying` →
   never blocked). Note: `HeroHealth` (`:35`), `TroopController` (`:45`) and `StoryCompanion` (`:51`) each
   declare `IDamageableStructure` only — none implements `ICombatLayered` — so the exemption is
   **structurally preserved but inert for today's party**. That is deliberate: a future flying companion
   gets the same arc the dragon gets.
6. `AcquirePartyForTest(IDamageableStructure candidate, out Vector3 pos)` — a public headless seam
   matching the repo's existing `…ForTest` convention (`StructureBurn.TickForTest:221`,
   `PlayerAttackController.ApplyWeaponTrailVfxForTest:967`), so the oracle drives the **real**
   `AcquireParty` with no reflection.

## 5. Oracle

`Assets/Editor/Regression/EnemyTowerWallLosRegression.cs` — builds a real `DefenseTower`
(`Allegiance = EnemyOwned`), a real `BoxCollider` on the **Structure** layer between it and a dummy
`IDamageableStructure`, and asserts:

| Case | Expectation |
|---|---|
| clear line | target **acquired** |
| Structure wall on the line | target **rejected** (`AcquireParty` → null) |
| Structure wall + `ICombatLayered.Flying` target | target **acquired** (flyer exemption) |
| `BlockedByWall` still referenced by the player pick | source assertion, so the player gate cannot be lost |

Hard-fails (never silently passes) if the `Structure` layer is absent, so degrade-open cannot read as green.

Registered in `Assets/Editor/Regression/DataRegression.cs` `RunAll` under the `Guard.Try` shape as
`[enemy-tower-wall-los]`. That registration line is the one edit outside the silo.

## 6. What NOT to touch

`Assets/Editor/WallTools/*`, any `.unity` scene, `ArcaneTower`, `TowerCombat`, the player-owned
`Acquire()` behaviour, `CLI_LANES_WO_NUMBERS.md`.

## 7. Acceptance

- `python tools/gate_brace.py` exit 0 + NUL-free on both touched `.cs`.
- `REGRESSION_OK` on a fresh log with `[enemy-tower-wall-los]` present (lead gates).
- Owner felt-verify in a fresh raid: a turret behind an intact wall stops firing at the party, and the
  next capture shows `[Flow:TowerLoS] … owner=EnemyOwned … blocked=true` lines where there were **zero**.

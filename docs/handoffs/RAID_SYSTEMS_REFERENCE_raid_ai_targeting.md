# Raid Systems Reference: AI Phases and Troop Targeting

**Author:** Claude (code analysis, September 2026)  
**Audience:** Game designers, AI researchers, external collaborators  
**Scope:** Raid assault phases, troop target selection, explicit player breach orders, rally mechanics, victory conditions

This document describes the complete raid AI and troop targeting system as implemented in the Defenders of the Realm codebase. Every claim is cited to specific file:line references so assertions can be verified against the source.

---

## 1. Assault Phases (Peel, Breach, Push, Finish)

Troops operate in distinct behavioral phases that determine which targets they prioritize and how they move. The phase hierarchy is: **Survive/Peel beats Breach beats Push/Finish** (RaidAssaultAi.cs:19-26, 156-167).

### Phase Definitions

**Four enumerated phases** (RaidAssaultAi.cs:20-26):
- `Peel = 0`: Survival mode—troop is under attack and prioritizes self-defense
- `Breach = 1`: Approach mode—troop advances toward and attacks the outer wall perimeter
- `Push = 2`: Penetration mode—a breach exists; troop drives deeper into the base
- `Finish = 3`: Objective mode—troop closes on and destroys the spire

### Phase Resolution Logic

The phase is resolved every frame via `RaidAssaultAi.ResolvePhase()` (RaidAssaultAi.cs:156-167). The logic follows a strict priority stack:

1. **If `peelThreat` is TRUE → return `Peel`**  
   A troop is under active attack or has a hostile unit within a fixed leash radius.

2. **Else if the route to the objective is blocked (`routeToObjectiveOpen == false`) → return `Breach`**  
   The path through the base defenses is not yet open; troop must crack walls.

3. **Else if the objective is in attack range → return `Finish`**  
   The spire is reachable; troop attacks it.

4. **Else → return `Push`**  
   Advance toward the objective.

### Peel Threat Conditions

`peelThreat` becomes TRUE when EITHER of these holds (TroopController.cs:948):

- **Recently hurt:** The troop took damage within `RaidAssaultAi.PeelHurtWindowSeconds` (2.5 seconds) (RaidAssaultAi.cs:42-43)
- **Unit in leash:** A hostile unit is within `RaidAssaultAi.PeelUnitLeashMeters` (6 meters), independent of attack range (RaidAssaultAi.cs:45-46)

The leash is **fixed at 6m, not attackRange-dependent**, so archers do not abandon their assault for every ranged unit (TroopController.cs:943-948 / RaidAssaultAi.cs:45-46).

---

## 2. Automatic Target Selection (Most Damaged Wall / Nearest Muster)

When no explicit player order stands, troops automatically focus on the most damaged wall segment in the outer perimeter. This is the fallback for all phases except during a rally march suppression.

### SelectFocusBreach Logic

**Function signature:** `RaidAssaultAi.SelectFocusBreach(IList<IDamageable> walls, Vector3 muster)` (RaidAssaultAi.cs:216-220)

**Selection rule** (RaidAssaultAi.cs:249-270):

1. **Scan all walls in the provided list.**
2. **Find the wall with the LOWEST HP** (most damaged).
3. **On a tie** (all walls at ≤0.5 HP difference), pick the one **NEAREST to the muster point** (rally position or troop's current location).

The actual distance calculation:
- Computes 3D distance from each wall's `WorldPosition` to the muster point
- **Flattens to XZ plane** (`d.y = 0f`) for muster-distance calculation (RaidAssaultAi.cs:256-259)
- Compares squared distances to avoid sqrt cost

**Tie-breaking threshold:** The comparison at RaidAssaultAi.cs:261-262:
```csharp
if (best == null
    || hp < bestHp - 0.5f                                    // Most-damaged wins by >0.5 HP
    || (Mathf.Abs(hp - bestHp) <= 0.5f && sqr < bestMusterSqr)) // Tied: nearest muster wins
```

This means:
- A wall with `hp = 100` loses to one with `hp = 99.4` (delta > 0.5)
- Two walls at `hp = 100` and `hp = 100.3` tie (delta ≤ 0.5) and go by distance
- The nearest one wins on distance (smallest `sqr`)

### What "Most Damaged" Means Concretely

**Field `bestHp` is the wall's current HP value**, not a normalized fraction (RaidAssaultAi.cs:256, 265). A wall with 40 HP beats one with 50 HP, regardless of max HP. This is crucial: a wall segment with high max HP can still be "most damaged" in absolute terms.

---

## 3. The New Explicit Player Breach Order (WO-1719)

**Date:** 2026-09-14 (owner ruling)  
**Behavior:** "Tap the wall segment directly, and it overrides ... the most damaged stays the fallback, all together unless they have aggro."

### How the Player Orders a Breach

**Entry point:** `RaidDeployController` (RaidDeployController.cs:1-956)

1. **Player taps the Breach button** → `ToggleBreach()` is called (RaidDeployController.cs:938-955)
   - Sets `_breachMode = true`
   - Clears any armed troop tile
   - Shows status message: "Breach: tap a wall section to order the assault."

2. **Player taps a wall segment** → `HandleBreachTap()` runs (RaidDeployController.cs:887-930)
   - Raycasts the tap point to find colliders
   - Calls `hit.collider.GetComponentInParent<WallSegment>()` (RaidDeployController.cs:898)
   - **If no WallSegment found:** logs a step, does NOT clear the order (a miss is a no-op)
   - **If wall is dead:** refuses with "that section is already down"
   - **If wall is friendly faction:** refuses with "that wall is not the enemy's"
   - **If wall is alive and hostile:** calls `TroopBreachOrder.Set(wall)` (RaidDeployController.cs:927)

3. **Player toggles Breach off** → `ToggleBreach()` called again  
   - Sets `_breachMode = false`
   - **Calls `TroopBreachOrder.Clear()`** (RaidDeployController.cs:951)
   - Shows "Breach order dropped - the warband picks the weakest wall again."

### The TroopBreachOrder Static

**File:** `Assets/_Modules/Village/Troops/TroopBreachOrder.cs` (complete file, 1-159 lines)

**Storage:**
- `_target`: the ordered wall as an `IDamageable` interface reference, or null
- `_version`: incremented on every set/clear, so callers with a cached focus can tell it's stale

**Core properties:**

**`Target` getter** (TroopBreachOrder.cs:65-86):
- Returns the current order, **with self-clearing guards:**
  - Returns `null` if `_target` is null
  - **If the wall's GameObject was destroyed** (a `UnityEngine.Object` fake-null check, ReferenceEquals + `== null`), calls `DropInternal()` and returns null
  - **If the wall is no longer alive** (`!_target.IsAlive`), calls `DropInternal("the ordered wall collapsed...")` and returns null
  - Otherwise returns `_target`

**`HasOrder` property** (TroopBreachOrder.cs:88-89):
```csharp
public static bool HasOrder { get { return Target != null; } }
```

**`Set(IDamageable wall)` method** (TroopBreachOrder.cs:96-115):
- Refuses if `wall` is null, destroyed, or already dead
- Stores the reference and increments `_version`
- Logs: `"BREACH ORDER set by the player: focus='<name>' hp=<hp> v=<version>. Every Breach-phase troop retargets to THIS panel on its next resolve; an aggro'd (Peel) troop keeps its current fight."`

**`Clear()` method** (TroopBreachOrder.cs:122-126):
- Clears `_target` and increments `_version`
- Called on Breach toggle off, on retreat, and on raid scene teardown (RaidDeployController.cs:158, 274 / OnDestroy)

### SelectFocusBreach with Explicit Override

**3-argument signature:** `RaidAssaultAi.SelectFocusBreach(IList<IDamageable> walls, Vector3 muster, IDamageable explicitFocus)` (RaidAssaultAi.cs:243-270)

**The override logic is an EARLY RETURN** (RaidAssaultAi.cs:247):
```csharp
if (explicitFocus != null && explicitFocus.IsAlive) return explicitFocus;
```

This runs **BEFORE** the most-damaged/nearest-muster scan. If the player's tap points to a still-alive wall, the warband targets it immediately.

**Key design detail (header comment, RaidAssaultAi.cs:230-241):**
> "The override is an EARLY RETURN, DELIBERATELY — the selection body below is NOT restructured. WO-1717 sec.3d proved the auto pick is what overrides the local scan unconditionally, so the cheapest correct seam was to put one gate in FRONT of the existing rule rather than teach the loop about priorities. The fallback is then literally the same code it always was, which is why a regression can pin 'auto still picks most-damaged' against an unchanged body."

**Important:** The explicit pick is NOT required to be in the `walls` list (RaidAssaultAi.cs:237-241):
> "The explicit pick is NOT required to appear in walls. The player tapped that collider; a candidate list built from a cached scene scan (TroopController.SharedBreachFocus, 0.4 s) can trail the tap by a frame, and dropping the order for that would be an invisible, intermittent refusal. Its liveness is checked here instead."

This means:
- A troop can order a wall that isn't yet in the scan cache
- `SharedBreachFocus` caches for 0.4s before re-scanning (TroopController.cs:50-51)
- An explicit order "releases" the march even for walls the moment-ago scan hadn't caught

---

## 4. Aggro and the Peel Exception

**Rule:** Aggro'd troops (peelThreat = TRUE) do **NOT** mass-retarget when a new breach order is set.

### Why Aggro'd Troops Keep Their Fight

When a troop is in Peel phase (under attack), it stays locked on its current foe even if the warband receives a new breach order. This prevents a troop defending against an in-range enemy from suddenly abandoning it for a wall.

**Proof:** Test case 4 in `WallBreachOrderRegression.cs` (WallBreachOrderRegression.cs:143-193, specifically line 177-182):
```csharp
var aggroWinner = ResolveWinner(
    scan, new Vector3(2f, 0f, 2f), explicitFocus: tapped,
    peelThreat: true, hasUnit: true, unit: defender, unitInAttackRange: true);
if (!ReferenceEquals(aggroWinner, defender))
    failures.Add(Tag + " an aggro'd (peelThreat) troop must KEEP its current fight when a " +
                 "breach order lands - it resolved '" + LabelOf(aggroWinner) + "'");
```

The logic flows through:
1. `peelThreat = true` → `phase = Peel` (RaidAssaultAi.cs:163)
2. In Peel phase, `PreferUnit()` returns TRUE (RaidAssaultAi.cs:287)
3. `PickBucket()` returns bucket 0 (the hostile unit), not bucket 2 (the wall) (RaidAssaultAi.cs:348-350)

### Aggro'd Troops Still Resolve SelectFocusBreach

The breach focus is **still resolved** for aggro'd troops (they call `SharedBreachFocus` like everyone else), but it **loses the bucket selection** because Peel phase prioritizes the unit over any structure (RaidAssaultAi.cs:348-350):
```csharp
if (phase == RaidAssaultPhase.Peel)
{
    if (hasUnit) return 0;  // unit always wins in Peel
    if (hasObjective) return 1;
    // Hurt by a tower with no unit in leash — do NOT resume wall-ring farming.
    return -1;
}
```

---

## 5. Rally and the March Suppression

**Rule:** Troops walking to a rally flag suppress non-objective wall picks (to avoid farming walls on the way).

**Special rule (WO-1719):** An explicit breach order **releases** the rally march—the warband ignores "don't pick walls" and hits the tapped segment.

### RallyHoldsMarch (3-Argument Form)

**Function:** `RaidAssaultAi.RallyHoldsMarch(bool rallySet, bool arrivedAtRally, bool peelThreat)` (RaidAssaultAi.cs:185-188)

**Logic:**
- Returns TRUE if rally is set AND troop hasn't arrived AND troop is not under attack (Peel wins)
- Returns FALSE otherwise

When TRUE, the troop suppresses wall-ring picks and walks to the flag instead.

### RallyHoldsMarch (4-Argument Form with Explicit Order Override)

**New overload (WO-1719):** `RaidAssaultAi.RallyHoldsMarch(bool rallySet, bool arrivedAtRally, bool peelThreat, bool hasExplicitBreachOrder)` (RaidAssaultAi.cs:205-210)

**Logic:**
```csharp
if (hasExplicitBreachOrder) return false;  // explicit order releases the march
return RallyHoldsMarch(rallySet, arrivedAtRally, peelThreat);
```

**Why this is critical (header comment, RaidAssaultAi.cs:190-210):**
> "Phase and rally are INDEPENDENT axes ... if a troop walking to a flag IS Breach phase, the 3-arg rule nulls its other-structure bucket, so 'every Breach-phase troop retargets to the tapped panel' would silently fail for the whole warband any time a rally was set—which, mid-raid, is most of the time. The implicit ring-farm suppression this exists to stop is untouched: only a panel the player explicitly tapped releases the march."

**Test case 5 in regression** (WallBreachOrderRegression.cs:224-238):
```csharp
if (!RaidAssaultAi.RallyHoldsMarch(true, false, false, false))
    failures.Add(Tag + " with NO order the rally must still hold the march");
if (RaidAssaultAi.RallyHoldsMarch(true, false, false, true))
    failures.Add(Tag + " an EXPLICIT breach order must release the rally march");
```

---

## 6. Formation and Movement Bias

Troops are arranged in a formation around the deploy point based on their role, and when moving toward a target, back-line troops (ranged, support) hold standoff distance.

### Formation Jobs and Offsets

**Mapping from TroopDef.Role to RaidAssaultJob** (RaidAssaultAi.cs:64-83, TroopController.cs:434):

| Role | Job | Forward Offset |
|------|-----|-----------------|
| "melee", "tank" (default) | `Front = 0` | +2.0m (ahead of deploy) |
| "ranged", "caster", "mage" | `Ranged = 1` | -3.5m (behind deploy) |
| "siege" | `Breaker = 2` | +1.25m (slightly ahead) |
| "support", "healer" | `Support = 3` | -5.0m (farthest back) |

**Formation world offset** (RaidAssaultAi.cs:104-125):
- Computed from `FormationWorldOffset(job, stackIndex, marchForward, lateralSpread)`
- Front and Breaker move ahead; Ranged and Support lag behind
- Lateral spacing alternates left/right: slot 0 at center, slot 1 right, slot 2 left, slot 3 right 2x, etc.
- Lateral spread default: 1.4m between same-role troops (RaidAssaultAi.cs:61)

### Movement Destination Bias (Standoff)

When troops move toward an objective, back-line jobs don't approach contact range; they hold standoff (RaidAssaultAi.cs:131-153, TroopController.cs:763-772):

**Front and Breaker:** Move directly to the target  
**Ranged:** Holds back `Mathf.Max(attackRange * 0.85, attackRange - 0.5)`  
**Support:** Holds back `Mathf.Max(attackRange + SupportBackMeters - RangedBackMeters, attackRange)`

This keeps backline troops outside contact damage range while maintaining attack range.

---

## 7. Raid Victory, Claiming, and Loot Gating

Destroying the spire or wiping the garrison ends the raid with victory. Victory triggers a claim, loot grant, optional companion unlock, and return to the castle.

### Win Conditions

**Two independent paths to victory** (RaidVictoryController.cs:138-157, 203-219):

1. **Spire destroyed:** `RaidSpire.OnDestroyedEvent` fires (RaidVictoryController.cs:204-207)
   - Spire is razed → objective complete
2. **Garrison wiped:** `RaidGarrisonSpawner.OnCleared` fires (RaidVictoryController.cs:215-218)
   - Last defender dies; raid also ends

**Either signal is sufficient; both are handled** (RaidVictoryController.cs:221-225):
```csharp
private void HandleVictory(string reason)
{
    if (_handled) { FlowTrace.Step(...); return; }  // latch: first signal wins
    _handled = true;
```

### What "Capturing" a Raid Base Means

**Claiming** persists the win in PlayerPrefs and flips the scene from enemy-owned to player-owned (RaidVictoryController.cs:809-825, RaidClaimService.cs:513-529):

1. Call `RaidClaimService.MarkClaimed(configId)` → returns TRUE if this is a NEW claim (first clear)
2. Call `SceneOwnership.SetEnemyOwned(false)` → the live scene now reads as player-controlled
3. Persist via `GameStateService.Instance?.Save()`

On a **new claim only**, the next companion is unlocked (RaidVictoryController.cs:282):
```csharp
string joined = newClaim ? UnlockNextCompanion() : null;
```

### The Spire as Objective

**File:** `Assets/_Modules/Village/World/Camps/RaidSpire.cs`

**Key properties:**
- Implements BOTH `IDamageable` (player/troop attack seam) AND `IDamageableStructure` (enemy contact damage seam) (RaidSpire.cs:66)
- Placed on the "Enemy" layer so the hero's sweep can find it (RaidSpire.cs:179-227)
- At 0 HP, fires `OnDestroyedEvent` and begins a collapse animation (sinking into the ground over `_collapseSeconds`, default 1.4s) (RaidSpire.cs:293-311)
- **Never re-arms** — a razed spire stays razed (RaidSpire.cs:43-46)

**Narrative role:** The central structure of the conquered base, destroying which wins the raid and allows claiming the camp as the player's own outpost.

### Loot Gating: First Clear vs. Repeat

**Two independent axes** (RaidVictoryController.cs:239-267, RaidClaimService.cs:151-169, 395-421):

**Axis 1: Ordinary Resources (Wood, Iron, Food, Gold)**
- First clear: pays 100% of settled loot
- Repeat clear (within cooldown): pays `RepeatClearLootMultiplier` (tunable, default 60%) (RaidClaimService.cs:103-127)
- Scaling: `Mathf.FloorToInt(amount * multiplier)` — rounded down so the gate never rounds a repeat back up (RaidClaimService.cs:415-420)

**Axis 2: Crystals**
- First clear of a UTC day: pays 100%
- Second+ clear of same day: pays 0%
- Resets at UTC midnight, even on a base claimed for months (RaidClaimService.cs:427-457)

**Why two axes (RaidClaimService.cs:385-387):**
> "A first clear of a NEW day is repeat:true, paid:false, and pays reduced resources but FULL crystals. Read BEFORE the grant stamps it."

This prevents a base from being a bounded resource faucet and lets the player get fresh crystals daily from re-clearing.

---

## 8. High-Level Victory/Claim Flow

**Complete sequence (RaidVictoryController.HandleVictory)** (RaidVictoryController.cs:221-405):

1. **VICTORY** → OnCleared or OnDestroyedEvent fires (RaidVictoryController.cs:203-225)
2. **CLAIM** → `RaidClaimService.MarkClaimed(configId)` + `SceneOwnership.SetEnemyOwned(false)` (RaidVictoryController.cs:809-825)
3. **COMPANION** → if new claim, `UnlockNextCompanion()` (RaidVictoryController.cs:282, 838-866)
4. **LOOT** → settle resources via `EconomyService.Grant()` (RaidVictoryController.cs:687-753)
5. **RETENTION** → overflow over-cap resources go to Raid Cache (RaidVictoryController.cs:319-325, RaidClaimService.cs:280-305)
6. **ROUGH STONE** → if earned, grant via `DungeonRunPayout.GrantRoughStone()` (RaidVictoryController.cs:649-685)
7. **ARMY** → `ReconcileRaidEnd(stars)` for wounded/veterancy (RaidVictoryController.cs:773-790)
8. **VICTORY SCREEN** → show banner + "Return to Castle" button (RaidVictoryController.cs:872-931)
9. **RETURN** → `SceneRouter.GoCastle()` or auto-return on timeout (RaidVictoryController.cs:979-991)

---

## Citations Index

### Core AI Files
- `Assets/_Modules/Village/Troops/RaidAssaultAi.cs` — Phases, target selection, formation
- `Assets/_Modules/Village/Troops/TroopController.cs` — Live troop combat loop, hunt scan, target resolution
- `Assets/_Modules/Village/Troops/TroopBreachOrder.cs` — Explicit player order storage and lifecycle
- `Assets/_Modules/Village/Troops/TroopRally.cs` — Global rally point static

### Deploy/Input Files
- `Assets/_Modules/Village/Troops/RaidDeployController.cs` — HUD, deploy/rally/breach tap handling, retreat

### World/Objective Files
- `Assets/_Modules/Village/World/Camps/RaidSpire.cs` — The raid objective structure
- `Assets/_Modules/Village/World/Camps/RaidVictoryController.cs` — Victory detection, claim, companion unlock, loot grant, return
- `Assets/_Modules/Village/World/Camps/RaidClaimService.cs` — Claim persistence, loot gating, raid cache, crystal day-stamp

### Test/Regression
- `Assets/Editor/Regression/WallBreachOrderRegression.cs` — Test cases proving breach order behavior

---

## Key Findings and Gaps

1. **Phase and rally are independent axes:** A troop can be in Breach phase while walking to a rally. The 4-argument `RallyHoldsMarch` overload (WO-1719) is critical to release the march suppression for explicit orders.

2. **Aggro is a shared lease, not a personal state:** When one troop enters Peel phase, only that troop's target selection changes. Other troops stay in Breach/Push and can retarget to the breach order.

3. **SelectFocusBreach is called for every troop but loses to bucket selection:** The shared focus is resolved and cached, but its power depends on the phase and the bucket logic that follows.

4. **Loot gating has two independent flags:** Claiming is one-time (guards companion unlock); crystal day-stamp resets daily (guards per-day crystal faucet). The multiplier governs ordinary resources only.

5. **The spire is the only raid objective:** Garrison wipe is also a win condition (owner ruling 2026-09-09), but the spire is the primary target structure.

6. **Explicit breach order is a live reference:** It must be checked for destruction (fake-null) and death (IsAlive) because the scene scan cache can trail the tap by a frame.

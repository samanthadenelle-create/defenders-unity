# Raid Systems Reference — Walls, Raid AI, and the Attack Sequence

**Compiled:** 2026-09-14, from a live Unity/C# codebase (Defenders of the Realm / Echoes of Elarion).
**Purpose:** A technical reference for research/design discussion with an external AI. Every factual
claim below is sourced to a specific `file:line` in the actual code, read the same day this document
was written — nothing here is inferred or invented. Where the code left a question genuinely open,
that is stated plainly as a gap, not papered over.

**How to use this document:** it is a snapshot of how the system works today, not a design proposal.
Treat every citation as a pointer you (or an AI helping you) could go re-open and verify. If you ask
another AI to redesign or extend any of this, feed it this whole document — the citations are what let
it reason about the *real* system instead of a plausible-sounding guess.

**Contents**
1. [Wall & Structure Building](#part-1--wall--structure-building)
2. [Raid AI & Troop Targeting](#part-2--raid-ai--troop-targeting)
3. [Attack Sequence & Damage Feedback](#part-3--attack-sequence--damage-feedback)

---

# PART 1 — Wall & Structure Building

## 1.1 Wall Segment Structure Definition — HP Scale, Damage Tiers, Toughness

### Core damage model

A wall segment uses an **inverted damage track**: accumulated damage counts upward from 0 to 100,
where 100 is complete collapse. HP is the inverse: `Hp = Max(0, 100 - Damage)` (`WallSegment.cs:261`).

- `MaxHp = 100f` (`WallSegment.cs:109`)
- `IsDestroyed` when `Damage >= 100f` (`WallSegment.cs:146`)
- `DamageFraction = Clamp01(Damage / 100f)` (`RepairTarget.cs:141`)

### Tier system and the toughness formula

Wall segments carry an upgrade tier (1 to `RepoProps.MaxStructureLevel`, currently 6)
(`WallSegment.cs:149`, `RepairTarget.cs:148`). A higher tier does **not** raise the 0–100 scale itself
— it reduces how fast incoming damage lands on that scale:

```
ToughnessFor(tier) = Mathf.Pow(1.6f, tier - 1)
effective_damage   = incoming_damage / ToughnessFor(tier)
```
(`WallSegment.cs:334`, `:165-169`). Tier 1 = ×1, tier 2 = ×1.6, tier 3 = ×2.56.

Each tier also pulls its wall height from `walls.json`; the collider height is kept in sync via
`ApplyTierBlockerHeight()` (`WallSegment.cs:195-226`).

### BULWARK talent reduction — friendly walls only

The hero's structure-toughness talents ("Hardened Ramparts", always-on; "Warden of Elarion", wave-phase
only) reduce incoming damage, capped at 50% total (`WallSegment.cs:336-344`, `:476-492`) — but **only
on the hero's own walls**. The gate is explicit:

```
Faction == CombatFaction.Friendly   → talent reduction applies
Faction == CombatFaction.Hostile    → NO reduction (enemy raid walls)
```
(`WallSegment.cs:343-344`). Ownership is derived from the scene: `SceneOwnership.IsEnemyOwned` decides
Friendly vs Hostile (`WallSegment.cs:248-249`). This is a deliberate balance gate — defensive
investment in your own castle never makes attacking someone else's raid base easier.

## 1.2 Wall Collapse — Visual and Gameplay State

`Collapse()` fires exactly once at `Damage >= 100`, latched by a `_collapsed` flag that never re-arms
(`WallSegment.cs:362-365`). Collapse is permanent — see §1.2.3.

**Physical effects, all on frame 0 (instant, not animated):**
1. Every collider on the section and its children is disabled, so troops/hero can walk through
   (`WallSegment.cs:369-375`).
2. Every `NavMeshObstacle` with `carving=true` on the section is disabled, handing the carved navmesh
   back to the walkable surface (`WallSegment.cs:383-388`; obstacles are set up by
   `RaidNavBake.cs:171-182`).
3. A `Collapsed` event fires for HUD/visual subscribers (`WallSegment.cs:394`).

**Visual collapse (readability only — the gameplay effect above already happened):** a coroutine sinks
the section and ramps a shader `_Collapse` property over a configurable duration (default 0.9s,
accelerating ease-out), leaving 15% of the span visible as rubble (0.85 sink fraction)
(`WallSegment.cs:399-457`, `:114`, `:121`). The same shader ramp is shared with the Gate's destruction
tell.

### Destroyed sections are permanently lost (WO-753)

Owner ruling, 2026-07-19: a destroyed section cannot be repaired in place. It returns only via a
full-cost rebuild in build mode.

```csharp
if (IsDestroyed) return;  // Repair(amount) refuses a destroyed section
```
(`WallSegment.cs:517`). The same guard blocks `RestoreOwnedTownCondition()`, used only on owned-town
load (`WallSegment.cs:505-510`).

## 1.3 Raid Base Generation — Procedural Wall Placement

`RaidBaseGenerator` builds raid bases from `scene-configs.json` data, never from a hand-edited scene.
The config authors `baseRadius`, `wallSegmentsPerSide`, `wallTier`, and `interiorWallLayers` (concentric
keep rings inside the outer perimeter) (`RaidBaseGenerator.cs:505-529`).

**Wall ring construction (`BuildRing()`):**
1. Segment count is computed from the perimeter run length against a 3.0m `MaxSegmentWidth` cap —
   panels shrink to fit, never stretch past that width (`RaidBaseGenerator.cs:196-199`, `:508-509`).
2. Each segment gets: a stable id `wall-<index>` (`WallSegment.cs:64`), its configured tier
   (`WallSegment.cs:179-183`), a `BoxCollider` on the "Structure" physics layer
   (`WallSegment.cs:537-545`), and a carving `NavMeshObstacle` (`carveOnlyStationary=true`)
   (`RaidNavBake.cs:171-182`).
3. Gate openings are cut on the south side always, optionally the north (`RaidBaseGenerator.cs:506-507`).

**Arena footprint:** the arena occupies a fraction of the 140m × 140m `RaidGround` plane
(`RaidBaseGenerator.cs:92`). `radius = MapHalfExtent * sqrt(footprint)`, where footprint is tier-driven:
Regular 20%, Hard 50%, Extreme 60% (`:352-360`); radius is clamped 10m–63m (`:492-500`).

**Exterior boundary ring (WO-1632):** a purely cosmetic perimeter of landscape pieces framing the whole
140m plane edge (not the inner base) — no gates, not part of the defense layers, no effect on base HP
(`ArenaBoundaryRing.cs:260-371`, `:76-147`, `:273-285`).

## 1.4 Cover Ring & Courtyard Decoration Placement

The courtyard's cover ring sits in a radial band between the spire exclusion zone and the outer wall,
split into concentric rings (7m spacing, max 3 rings) (`RaidBaseDresser.cs:47-50`, `:868-873`), built
from a seeded RNG so a re-bake reproduces the same layout (`:862-866`).

**Jitter:** radial scatter is bounded by remaining band slack after prop size/margins; tangential
jitter is limited to piece-overlap tolerance (never opens a gap); scale rolls 0.92–1.12× — tighter than
the arena's own 0.9–1.6× range (`RaidBaseDresser.cs:57-58`, `ArenaBoundaryRing.cs:300-315`).

**Keepout zones** (props are rejected/re-rolled if they land here):
1. The assault lane — within `GateWidth/2 + 1.0m` of the south→north march corridor.
2. The spire pocket — within 66% of `CourtyardSpirePad` from center.
3. Turret footprints — 3.5m clearance around every placed `DefenseTower`.
4. The staging zone — 10m clearance around the hero's deploy point.
(`RaidBaseDresser.cs:60-64`, `:972-995`)

Cover props keep their colliders; pure decoration has colliders stripped
(`RaidBaseDresser.cs:1041-1042`).

## 1.5 Wall Repair Mechanics

`WallRepairController` scans for damaged structures on a 0.75s timer (`WallRepairController.cs:223-232`,
`:137`), selects on tap (camera raycast, guarded against tapping through UI — see below), shows a
materials-cost prompt, and on confirm spends through `EconomyService.TrySpend` then calls
`RepairTarget.Repair()`/`RepairFull()` (`:238-239`, `:401-407`, `:532-572`, `:1092-1102`).

`RepairTarget` is the uniform wrapper over `WallSegment` / `Gate` / `Building`
(`RepairTarget.TryWrap`, `:77-101`); damage fraction is normalized 0–1 across all three
(`:134-150`).

**Repair cost (owner ruling, 2026-07-11):** `damage_fraction × the structure's own catalog build cost`.
Crystals are never spent on base repair, except a carve-out that lets crystals cover a materials
shortfall. Cost source, in priority order: PlacedStructure's own catalog row → the matching
`buildings.json` row → the matching collector row → fallback `wall_stone`/`gate_stone` rows → the
data-driven `repair_default` row (`WallRepairController.cs:633-648`, `:866-910`).

**Repair-All:** worst-first sweep, skipping anything unaffordable (greedy, honest partial repair)
(`:1035-1130`). A destroyed structure (fraction ≥ 0.999) is never a Repair-All target — it needs a
full-cost rebuild instead (`:358-362`).

**Pointer-over-UI guard (WO-1708):** `HandleTap()` checks `PointerIsOverUi()` before raycasting the
world, so tapping a button (including the F8 FLAG button) can no longer also select/repair a structure
behind it (`WallRepairController.cs:406`) — this guard was missing until WO-1708.

## 1.6 NavMesh & Pathing — the Wall's Role

Every raid base has a continuous `RaidGround` plane (y=0) fitted to the boundary ring, textured from the
scene's owned terrain layer, carrying a shared non-trigger `MeshCollider` for both physics and
navigation (`RaidNavBake.cs:188-265`, `:240-245`). The legacy Unity navmesh is baked after every
destructible (walls, towers) has its `NavMeshObstacle` attached (`:79-80`).

**Walls are not baked into the navmesh** — they use runtime carving obstacles instead: `shape=Box`,
`carving=true`, `carveOnlyStationary=true`, bounds derived from the segment's own `BoxCollider`
(`RaidNavBake.cs:172-179`). The obstacle stays enabled while the wall stands
(`obstacle.enabled = wall.HpFraction > 0f`, `:179`) and disables the instant the wall collapses
(`WallSegment.cs:386-387`) — no rebake needed, the path opens in real time.

**Line-of-sight:** walls sit on the "Structure" physics layer, which tower/hero line-of-sight linecasts
check against (`WallSegment.cs:543-544`). This is deliberately unlike `RaidSpire`, which moves onto the
"Enemy" layer to be sweep-findable — walls must never leave "Structure," or towers would shoot through
them (`WallSegment.cs:30-39`).

### Correction — 2026-09-14, WO-1723 Lane A

The statement above ("Walls are not baked into the navmesh") is true of the `WallSegment` GameObject but
**false of the visible wall** — the `Zone_Clad` ring of clad panels is a sibling of the segments and was
being baked into the navmesh as permanent geometry (measured at commit `0e656756e`: 60/60 visible panels
flagged `NavigationStatic`). WO-1723 Lane A (`RaidNavBake.cs` lines 66–77 extended) excludes clad
descendants from the `NavigationStatic` marking pass via `IsUnderCladZone()`, so the ground beneath the
visible wall now bakes walkable and a collapsed segment's carve-drop actually opens a hole for troops
and the hero.

---

# PART 2 — Raid AI & Troop Targeting

> ## ⚠ STALE 2026-09-15 for §2.3 and §2.5 — superseded by the owner's WO-1738 ruling (WO-1746).
> **Body deliberately NOT rewritten** (CLAUDE.md §15: a dated point-in-time reference gets a banner,
> not an edit). What this Part describes is the pre-ruling state. Two things changed:
> 1. **Breach is now a persistent STANCE, not a one-shot order.** §2.3's flow still holds for the
>    tap itself, but the stance (`TroopBreachOrder.StanceActive` = the Breach button armed **OR** a
>    standing order) survives the ordered panel collapsing, so the warband **auto-chains** to the
>    next most-damaged wall with no second tap. §2.3's self-clear is kept exactly as described —
>    it is now the auto-chain's trigger rather than the end of the order.
> 2. **§2.5's 4-arg `RallyHoldsMarch` now takes the STANCE, not `hasExplicitBreachOrder`.** With
>    the order-only argument the auto-chain died on the first wall any time a rally was set — the
>    exact failure §2.5 itself describes, one step later in the sequence.
> 3. **Ordinary troops are RELUCTANT on walls.** Non-siege troops with no stance armed hit
>    `WallSegment` panels at `RaidAssaultAi.ReluctantWallDamageMultiplier` (0.1); siege and any
>    troop under an armed stance stay at full. `WallSegment` toughness itself is unchanged.
>
> §2.1, §2.2, §2.4 (aggro still wins — unchanged, and still pinned by the same regression lines),
> §2.6 and §2.7 are current. See `WorkOrders/WORK_ORDER_1738_wall_durability_concurrency_fork.md`
> (the ruling) and `WorkOrders/WORK_ORDER_1746_breach_stance_and_reluctant_wall_damage.md`.

## 2.1 Assault Phases: Peel, Breach, Push, Finish

Troop behavior runs through four enumerated phases, in strict priority order —
**Peel beats Breach beats Push/Finish** (`RaidAssaultAi.cs:19-26`, `:156-167`):

| Phase | Value | Meaning |
|---|---|---|
| `Peel` | 0 | Survival — under attack, prioritizes self-defense |
| `Breach` | 1 | Approach — advances on and attacks the outer wall |
| `Push` | 2 | Penetration — a breach exists, drives deeper into the base |
| `Finish` | 3 | Objective — closes on and attacks the spire |

`ResolvePhase()` runs every frame (`RaidAssaultAi.cs:156-167`):
1. `peelThreat == true` → `Peel`.
2. Else, route to the objective blocked → `Breach`.
3. Else, objective in attack range → `Finish`.
4. Else → `Push`.

**`peelThreat` becomes true** when the troop was hurt within `PeelHurtWindowSeconds` (2.5s) OR a
hostile unit is within `PeelUnitLeashMeters` (6m, fixed — not attack-range-dependent, so archers don't
abandon the assault for every ranged unit in sight) (`RaidAssaultAi.cs:42-46`, `TroopController.cs:943-948`).

## 2.2 Automatic Target Selection — Most Damaged, Ties by Nearest Muster

With no explicit player order standing, `SelectFocusBreach(walls, muster)` picks the lowest-HP wall in
the given list; ties (≤0.5 HP apart) go to whichever is nearest the muster point, XZ-flattened, compared
by squared distance to skip the sqrt (`RaidAssaultAi.cs:216-220`, `:249-270`):

```csharp
if (best == null
    || hp < bestHp - 0.5f
    || (Mathf.Abs(hp - bestHp) <= 0.5f && sqr < bestMusterSqr))
```

"Most damaged" is the wall's **absolute current HP**, not a normalized fraction — a high-max-HP wall can
still be "most damaged" in raw terms (`RaidAssaultAi.cs:256`, `:265`).

## 2.3 The Explicit Player Breach Order (WO-1719, owner ruling 2026-09-14)

Owner's design, verbatim: *"tap the wall segment directly, and it overrides... the most damaged stays
the fallback... all together unless they have aggro."*

**Flow:**
1. Player taps the **Breach** button → `ToggleBreach()` arms `_breachMode`, clears any armed troop
   tile, shows a hint (`RaidDeployController.cs:938-955`).
2. Player taps a wall segment → `HandleBreachTap()` raycasts, walks up to a `WallSegment` via
   `GetComponentInParent` (`:898`). A miss is a no-op with a hint — it never clears a standing order. A
   dead or friendly-faction wall is refused with a specific message. A live hostile wall calls
   `TroopBreachOrder.Set(wall)` (`:887-930`, `:927`).
3. Toggling Breach off clears the order (`TroopBreachOrder.Clear()`) and announces "the warband picks
   the weakest wall again" (`:951`).

**`TroopBreachOrder`** (`Assets/_Modules/Village/Troops/TroopBreachOrder.cs`, new file) holds the one
live order plus a version counter bumped on every set/clear, so a 0.4s-cached focus lookup can detect
staleness (`:65-89`). The `Target` getter self-clears on a Unity fake-null or a dead wall
(`!IsAlive`) — the order can never point at a destroyed or missing object (`:65-86`).

**Override mechanism — one early return, nothing else touched:**
```csharp
if (explicitFocus != null && explicitFocus.IsAlive) return explicitFocus;
```
(`RaidAssaultAi.cs:247`), placed *before* the most-damaged/nearest-muster scan, which is otherwise
byte-for-byte unchanged (`:243-270`). The design note in the code explains why: *"the cheapest correct
seam was to put one gate in FRONT of the existing rule rather than teach the loop about priorities...
which is why a regression can pin 'auto still picks most-damaged' against an unchanged body"*
(`RaidAssaultAi.cs:230-241`). The order also does not need to already be in the scanned `walls` list —
its own liveness check covers the case where a 0.4s-cached scan hasn't caught up to the tap yet
(`:237-241`).

## 2.4 Aggro and the Peel Exception

An aggro'd troop (`peelThreat == true`) never mass-retargets to a new breach order — it keeps its
current fight. This needed **no special-case code**: in Peel phase, `PreferUnit()` returns true
(`RaidAssaultAi.cs:287`), and `PickBucket()` returns bucket 0 (the hostile unit) rather than bucket 2
(the wall) regardless of any standing order (`:348-350`):

```csharp
if (phase == RaidAssaultPhase.Peel)
{
    if (hasUnit) return 0;   // unit always wins in Peel
    if (hasObjective) return 1;
    return -1;               // hurt by a tower, no unit in leash: do NOT resume wall-ring farming
}
```

Proven directly by `WallBreachOrderRegression.cs:143-193`, which asserts an aggro'd troop resolves to
its attacker even with a live breach order pointed at a different target.

## 2.5 Rally and March Suppression

`RallyHoldsMarch(rallySet, arrivedAtRally, peelThreat)` returns true (suppress wall-ring picks, walk to
the flag) when rally is set, the troop hasn't arrived, and it isn't under attack
(`RaidAssaultAi.cs:185-188`).

**New 4-argument overload (WO-1719)** adds the explicit-order override:
```csharp
if (hasExplicitBreachOrder) return false;   // an explicit tap always releases the march
return RallyHoldsMarch(rallySet, arrivedAtRally, peelThreat);
```
(`RaidAssaultAi.cs:205-210`). Without this, a troop in Breach phase that's also walking to a rally flag
would have its wall-bucket silently nulled by the 3-arg rule — which, mid-raid, is most of the time —
so "every Breach-phase troop retargets together" would quietly fail for the whole warband. The
narrower fix (an explicit tap releases the march; nothing else does) leaves the original ring-farm
suppression intact for every other case (`:190-210`).

## 2.6 Formation and Movement Bias

Troop role maps to a formation job with a forward/back offset from the deploy point
(`RaidAssaultAi.cs:64-83`, `TroopController.cs:434`):

| Role | Job | Offset |
|---|---|---|
| melee, tank (default) | Front | +2.0m ahead |
| ranged, caster, mage | Ranged | −3.5m behind |
| siege | Breaker | +1.25m ahead |
| support, healer | Support | −5.0m (farthest back) |

Lateral spacing alternates left/right at 1.4m between same-role troops (`RaidAssaultAi.cs:61`,
`:104-125`). Moving toward a target, back-line jobs hold standoff rather than closing to contact range:
Ranged holds `max(attackRange*0.85, attackRange-0.5)`; Support holds further back still
(`RaidAssaultAi.cs:131-153`, `TroopController.cs:763-772`).

## 2.7 Raid Victory, Claiming, and Loot Gating

**Two independent win paths**, either sufficient, first one latches (`RaidVictoryController.cs:138-157`,
`:203-225`, `:221-225`):
1. Spire destroyed (`RaidSpire.OnDestroyedEvent`).
2. Garrison wiped (`RaidGarrisonSpawner.OnCleared`).

**Claiming:** `RaidClaimService.MarkClaimed(configId)` (true only on a genuinely new claim) →
`SceneOwnership.SetEnemyOwned(false)` → `GameStateService.Save()` (`RaidVictoryController.cs:809-825`,
`RaidClaimService.cs:513-529`). Only a new claim unlocks the next companion (`:282`).

**The spire** implements both `IDamageable` (player/troop attacks) and `IDamageableStructure` (enemy
contact damage) (`RaidSpire.cs:66`), sits on the "Enemy" layer so hero sweeps can find it
(`:179-227`), and at 0 HP fires `OnDestroyedEvent`, sinks over 1.4s, and never re-arms once razed
(`:293-311`, `:43-46`).

**Loot gating has two independent axes:**
- **Ordinary resources** (wood/iron/food/gold): first clear pays 100%; a repeat clear inside cooldown
  pays a tunable multiplier (default 60%), floored so a repeat never rounds back up
  (`RaidClaimService.cs:103-127`, `:415-420`).
- **Crystals**: first clear of a UTC day pays 100%, every subsequent clear that same day pays 0%,
  resetting at UTC midnight regardless of how long the base has been claimed (`:427-457`). This
  deliberately keeps crystals a bounded daily faucet rather than an infinite farm
  (`RaidClaimService.cs:385-387`).

**Full victory sequence:** victory signal → claim → companion unlock (if new) → loot settlement via
`EconomyService.Grant()` → overflow to Raid Cache → rough stone grant if earned → army
wounded/veterancy reconciliation → victory screen → return to castle (manual or auto-timeout)
(`RaidVictoryController.cs:221-991`, cited in full in the source document).

---

# PART 3 — Attack Sequence & Damage Feedback

## 3.1 Attack Flow: From a Hit to an HP Change

Two entry points, one destination:
- **Enemy contact damage:** `Enemy.cs` → `IDamageableStructure.ApplyContactDamage(amount)`
  (`WallSegment.cs:315`).
- **Player/troop attack:** `PlayerAttackController`/`TroopController` → `IDamageable.TakeDamage(amount,
  element)`, which routes internally to the same contact-damage path (`WallSegment.cs:271`).

Both funnel into one private method, `ApplyDamage(float amount, string via)` (`WallSegment.cs:325-353`),
which applies, in order:
1. **Tier reduction:** `effective = amount / ToughnessFor(tier)`, exact division, not approximated
   (`WallSegment.cs:334`, `:168`).
2. **BULWARK reduction:** faction-gated, friendly walls only (see §1.1) (`:343-344`).
3. **HP track write:** `_damage = Clamp(_damage + effective, 0, 100)` (`:346`).

At `_damage >= 100f`, `Collapse()` fires (see §1.2) (`WallSegment.cs:352`).

## 3.2 What Fires on Impact — Three Channels, One Observation Point

Every damageable structure gets a `StructureHitReaction` component attached by
`StructureDamageVisuals.Register()` (`:909-910`). It polls the structure's HP fraction every frame
(`StructureHitReaction.cs:313-336`) and, the instant HP drops, evaluates three independent,
rate-limited channels plus a fourth UI element:

**Channel 1 — Motion (dust burst).** `VFXManager.Play(VFXType.Env_DestructionDust, ...)` at the
structure's bounds centre (`:342`, `:297-310`). Rate-limited to `MinBurstInterval = 0.15s`
(`:108`, `:104-106`) — a six-troop warband on one panel bursts at 0.15s intervals, not a continuous
smear. Family-B one-shot: no loop slot, no leak risk against VFXManager's pool cap (`:41-46`).

**Channel 2 — Sound (masonry impact).** `AudioService.PlaySfxAtPosition(SfxId.StructureImpact, ...)`
(`:356-357`), only when the hit is "credible" (implausible drops, e.g. from a save restore, withhold it)
(`:344-358`). **No authored clip exists yet** — it falls back to a procedural synth placeholder; dropping
a real clip at the audio key needs no code change (`SfxId.cs:49-54`).

**Channel 3 — Number (floating damage text).** `DamageNumberSpawner.Spawn(hit.Damage, hit.At)`
(`:350`), billboarded, integer text rising 1.0 world unit over a 0.55s lifetime, colour lerping
gold→orange-red purely by magnitude — **colour carries no meaning**, since the owner is red/green
colourblind (`DamageNumberSpawner.cs:46-67`, `:305-406`, `StructureHitReaction.cs:67-70`).
Rate-limited to `MinNumberInterval = 0.35s`, deliberately longer than the dust cadence so at most ~2
numbers are ever legible at once while dust still fires per blow (`:133`, `:126-131`).

Damage that lands **inside** that 0.35s window is never discarded — it accumulates in
`_pendingNumberDrop` and flushes as one summed number on the next eligible frame
(`:82-86`, `:171`, `:268-282`). Design intent, quoted directly from the test file: *"A throttle on the
TELL must not become a lie about the TOTAL."* Six troops landing 8+8+4 in rapid succession prints one
"20," not three stacked numbers.

**Channel 4 — Floating HP bar (UI, not VFX).** `FloatingHealthBar.Attach(...)`
(`StructureDamageVisuals.cs:931`). As of WO-1717 (2026-09-14) this attaches **on the first hit, same
frame**, via the `StructureHitReaction` callback. Before that fix, the bar waited on a 0.3s poll behind
a 2.0s scan poll — a struck structure could look untouched for up to 2.3 seconds
(`StructureDamageVisuals.cs:896-910`, `StructureHitReaction.cs:1-16`).

All floating text shares **one pool**, `DamageNumberSpawner` — the only floating-text pool in the game,
never duplicated (`:160-164`).

## 3.3 Is the Number Raw or Adjusted?

**The number is the points actually applied, post-tier-divide — never the raw request.** The seam reads
the *result* (the HP fraction that just moved), never the incoming damage value:

```csharp
float delta = _last - hpNow;          // fraction that fell this frame
_pendingNumberDrop += delta;
float points = _pendingNumberDrop * maxHp;
```
(`StructureHitReaction.cs:239-249`, `:276`). Because tier reduction happens inside `ApplyDamage` *before*
the HP field is written (`WallSegment.cs:334`, `:346`), and the reaction only samples the result, it is
structurally impossible for the number to show anything but the true post-reduction damage.

**Worked example:** a tier-3 steel wall (÷2.56) takes a 29-damage hit → 29/2.56 = 11.33 points actually
applied → HP fraction drops 0.1133 → number prints "11," the post-divide value, never "29."

## 3.4 Feedback Coverage by Structure Type

| Coverage | Structure types | Why |
|---|---|---|
| **Full** (dust + number + sound + bar) | WallSegment, Building, Tower, DefenseTower, ArcaneTower, HarvestSite | All expose a `MaxHp` the reaction can read (`StructureDamageVisuals.cs:755-811`) |
| **Partial** (dust + sound + bar, no number) | ResourceCollector | No public `MaxHp` getter — its internal HP defaults to 120 but the catalog row may differ, so printing a number risked lying about what actually moved. Adding the getter is a gameplay-class edit explicitly out of scope for the WO that built this (`StructureDamageVisuals.cs:767-774`) |
| **Bespoke, opt-out of the generic channel** | Gate | Its own force-field collapse shader tell; no dust/number/bar (`StructureDamageVisuals.cs:757-758`, `:77-80`) |
| **Bespoke, never scanned** | Heart | Its own 7-state crystal tell, colour-free by the same accessibility rule; attaches its own `onHit` callback to add a kick to its existing aura pulse rather than using the generic bar/number (`StructureHitReaction.cs:18-25`, `:200-202`, `:288`) |

## 3.5 Hit-Stop, Screen Shake, or Other Game-Feel?

**None exist for structure damage.** Only the three channels in §3.2 fire. `StructureHitReaction` is
deliberately "presentation only," with its decision logic split out so it can be tested headlessly with
no rendering, camera, VFX pool, or audio device (`StructureHitReaction.cs:95-98`,
`StructureFeedbackRegression.cs:48-49`). No impact shake, stun, or hit-stop is referenced anywhere in
the structure damage path.

## 3.6 Known Gaps, Stated Plainly

1. **StructureImpact sound is a placeholder synth.** No authored clip yet; drop one at the documented
   audio key and it replaces the synth automatically, no code change (`SfxId.cs:49-54`).
2. **ResourceCollector never shows a damage number**, on purpose, until it gets a public `MaxHp` getter
   — deliberately left as a future, separate ticket rather than a gameplay-class edit smuggled into a
   presentation-layer fix (`StructureDamageVisuals.cs:767-774`).
3. **Gate's feedback is entirely bespoke** and not wired through the generic channel at all — a
   deliberate architectural separation, not an oversight (`StructureDamageVisuals.cs:757-758`).
4. **The accumulation window resets on repair/respawn** — a structure's pending-number total does not
   persist across a re-attach (`StructureHitReaction.cs:212-213`); this is by design, not a bug.
5. **The seam is faction-blind** — a siege hit on the player's own wall gets the same feedback as a hit
   on an enemy wall. Not flagged as wrong anywhere in the code, but also not a deliberate design
   decision either way — worth a ruling if it ever matters.

---

# PART 4 — Live Device Observations

The three parts above are the *designed* system, read from source. This section is the *observed*
system — real screenshots and logcat captures pulled from the Seeker during actual play, added as they
happen. Where an observation confirms a claim above, it's noted inline; where it reveals something the
source reading didn't predict, that's flagged as a genuine open question, not folded into Parts 1-3
until it's been traced back to a cause.

### Entry 1 — 2026-09-14 12:41, live raid, Breach mode ON

**Screenshot:** `docs/handoffs/wall_target_breach_on_20260914.png` — HUD shows `Breach ON`, Spire 100%,
Razed 2%, Troops 9/9, wall directly in front of the hero with light spark VFX but no visible damage
scarring.

**What the log proves, not guessed:**

The *automatic* targeting/damage/collapse chain is healthy in this capture — this is real, working
behavior, cited exactly as logged:

```
[Flow:WallSegment] WallSegment 'Wall_Outer_SE_28' took 18 (attack, tier 3, Hostile) -> damage 62/100 (38% standing).
[Flow:WallSegment] WallSegment 'Wall_Outer_SE_28' (Hostile) COLLAPSED: 1 solid collider(s) and 1 carving obstacle(s) dropped - it no longer blocks tower line-of-sight or agent pathing.
[Flow:RaidAI] source=auto focus='Wall_Outer_SE_27' hp=100 walls=197
[Flow:TroopAI] id=troop-battlemage role=ranged RETARGET#14 reason=foe-died dropped='Wall_Outer_SE_28(WallSegment)' -> won='Wall_Outer_SE_27(WallSegment)' ...
[Flow:Reticle] [hostile-admit] HOSTILE STRUCTURE 'Wall_Outer_SE_27' impl=DeNelle.Village.WallSegment faction=Hostile via physics sweep (mask=Enemy|Structure)
```

Damage applies, faction reads correctly as Hostile, tier-3 divisor is visibly working (18 incoming →
consistent with the documented ÷2.56), collapse disables exactly 1 collider + 1 carving obstacle as
designed, and every troop retargets to the next wall the instant the old one dies. The physics sweep
mask genuinely includes "Structure" — the layer-mask hypothesis from Part 4's original diagnostic list
is **disproven for this capture**.

**But the real symptom is narrower than the original hypothesis list, and it's a different subsystem:**

```
[Flow:Raid] breach tap missed every WallSegment (hit 'RaidGround') - the standing order, if any, is UNCHANGED.
```

One tap was made with Breach mode active; it hit the ground plane's collider instead of a wall's. Across
this entire ~4-second capture: **12 lines of `source=auto`, 0 lines of `source=order`** — the explicit
Breach-tap order (WO-1719) has never once won a resolve in this session. This is not the
damage/collapse/faction system failing — that's proven healthy above. It's specifically
`RaidDeployController.HandleBreachTap()`'s raycast resolving to the ground instead of the tapped wall's
collider at that screen position — narrowing the original 5-hypothesis list to something close to
hypothesis 5 (collider/geometry mismatch), but on the **tap raycast**, not the attack hit-test, which
this same capture proves works fine.

**Open, not yet proven:** whether this is a raycast-order issue (ground collider sitting in front of/
occluding the wall's collider from this camera angle), a raycast-radius/precision issue on a thin wall
panel, or something else — the next capture should log the raycast's actual hit point and distance
alongside the miss message to settle it.

---

### Entry 2 — 2026-09-14, root cause found and fixed; residual mismatch found live; shadow hypothesis for the overlay

**Root cause of Entry 1's breach-tap miss, confirmed by reading `RaidBaseDresser.cs` (WO-1719 fix,
commit `8333b172d`):** `RaidBaseGenerator.PlaceSegment` sizes a wall's `BoxCollider` correctly from its
own mesh. A LATER dressing pass, `RaidBaseDresser.Dress -> HideWallRenderers + CladRing`, disables that
mesh's renderer and instantiates brand-new cosmetic art at the wall's footprint to actually show the
player a wall — and `CladRing.FitPieceAlong` deliberately preserves that cosmetic art's **native
authored height** on the Y axis, completely decoupled from the collider sized earlier. Live-captured on
`Wall_Outer_SS_3`: 15.0m visual vs. 3.0m collider, a clean 5x mismatch. This is exactly the mechanism an
external AI reviewer independently derived from the release notes alone, unprompted, and it matches:
*"a wall's visible art was being re-skinned by a cosmetic pass at its native, un-scaled height, while
the collider underneath stayed at the size it was originally built at."*

**The fix (`RaidBaseDresser.cs`, `SyncWallColliderHeight`) resizes the COLLIDER to match whatever height
`CladRing` achieves for the visual — it does NOT change the visual's scale.** This is a load-bearing
fact for the overlay hypothesis below: if a tall visual is the cause of anything else (a shadow, a
render-order artifact), that visual was never shrunk by this fix. Only the previously-invisible hitbox
now matches it.

**Headed multi-tier proof of the fix** (`Assets/Editor/RaidWallTierProof.cs`, `RaidWallTierProof.Run`,
non-batchmode Editor Play mode, castle → `SceneRouter.GoRaid` per tier): Regular 78/78 walls matched
(0 mismatches). Hard: 8/186 flagged. Extreme: 1/210 flagged, worst delta 10.41m. Recorded at the time as
"likely the test tool's own nearby-renderer search picking up a tower or decoration, not a proven
defect" — **that caveat has since been narrowed by a live finding below.**

**Direct, controlled proof the fix works on a real device, real save, real gameplay** — not the headed
proof, an entirely separate reflection-driven test (`Assets/Editor/RaidBreachTapLiveProof.cs`) that
invokes the actual `RaidDeployController.HandleBreachTap(Vector2)` method against a wall guaranteed
untouched at raid start:
```
PASS - TroopBreachOrder.Target is exactly the tapped wall 'Wall_Outer_SS_18' after the real HandleBreachTap call.
```
And live on the owner's own Seeker, unprompted, mid-raid: a tap on a fully undamaged wall
(`hp=100`) produced `BREACH ORDER set by the player: focus='Wall_Outer_SS_15' hp=100`, then
`source=order focus='Wall_Outer_SS_15' ... (player breach order OVERRIDES the most-damaged pick)`, then
the troop AI actually retargeted and the wall took real damage. The fix is proven working, not merely
gate-passed.

**A residual mismatch was then found live, in the tier the headed proof called fully clean, closing the
"just a test-tool artifact" question for at least one case.** Captured via `RaidDeployController`'s own
`LogBreachTapDiagnostics` (a *different* code path than `RaidWallTierProof`'s renderer search — this one
reads `GetComponentsInChildren<Renderer>(true)` off the `WallSegment` itself, not a nearby-radius scan,
so it cannot be a false positive from an unrelated object), mid-raid, `RaidBase_raider_camp_small`
(Regular tier):
```
nearest WallSegment='Wall_Outer_SE_17' colliderPresent=True colliderEnabled=True
  colliderBounds Extents: (0.75, 2.00, 1.43)      <- ~1.5m x 4m x 2.9m
  rendererBounds Extents: (5.54, 10.51, 10.61)    <- ~11m x 21m x 21m, SAME centre
```
Roughly 7x wider, 5x taller, 7x deeper than its own collider, on a wall that had never collapsed. The
owner independently reported walking straight through a standing (not destroyed) wall in the same
session — the same defect: most of the visible wall's width has no collision behind it at all. Filed as
`WORK_ORDER_1722`. **Open, not yet explained:** whether this is present at raid start (contradicting the
headed proof's "0/78 clean" for this exact tier) or arises later, during live play, from some runtime
event the raid-start-only headed proof could not have observed. The original WO-1719 fix, by its own
commit message, only ever resynced the collider's **Y** — "never touching X/Z, per the existing
footprint-untouched convention" — so an X/Z-heavy mismatch this large may be a pre-existing defect the
original diagnosis never looked for, not a regression.

**The blue-grey overlay from Entry 1's second screenshot — leading hypothesis, not yet tested:** an
external AI reviewer proposed, unprompted, that the overlay is the **shadow of the oversized wall** —
not a shader leak, not a stuck post-process volume. Every detail in the original description fits: the
blue-grey tint (this engine's shadow color under its lighting model), the region covering roughly half
the screen including the ground (a 15m wall at a low sun angle casts a large projected shadow that falls
on whatever's behind it), the hero rendered as a silhouette (standing inside that shadow), the HUD
staying fully legible (UI is unlit, drawn on top), and the timing (same raid, right after the wall-spark
VFX in the first screenshot). Per the "collider resized to match visual, visual left alone" fact above,
**if this hypothesis is correct, the overlay is predicted to still occur in the fixed build** — the tall
visual that would cast it was never shrunk, only its hitbox changed. **The ten-second test that settles
it, not yet run:** rotate the camera without moving the hero. A shadow stays anchored to the ground/wall
and appears to move as the camera does relative to a fixed world position; a post-process or UI effect
stays screen-anchored regardless of camera angle. This requires the owner's own hands on the device (a
camera-drag gesture); not something drivable safely via `adb` blind input.

**Standing next-capture checklist, updated:** (1) tap a wall with Breach armed on each of the three
tiers in the fixed build and confirm "not a wall" is gone on all three — Regular is confirmed, Hard/
Extreme still carry the flags above and need the same live-tap confirmation Regular got; (2) if the
overlay recurs, rotate the camera without moving the hero and note whether the dark region is
world-anchored (shadow) or screen-anchored (UI/post-process); (3) photograph a wall next to the hero in
the fixed build — per the collider-vs-visual fact above, it is expected to still read as oversized
relative to the hero, since only the collider was corrected, not the visual scale.

---

*(Further entries append above this line as more raids are played.)*

---

## Master Citations Index

| File | Primary subject |
|---|---|
| `Assets/_Modules/Village/Walls/WallSegment.cs` | Wall HP/damage model, tier toughness formula, BULWARK, collapse, collider/obstacle teardown |
| `Assets/Editor/WallTools/RaidBaseGenerator.cs` | Raid base generation, wall ring placement, arena radius/footprint |
| `Assets/Editor/WallTools/RaidBaseDresser.cs` | Courtyard cover ring, keepout zones, jitter |
| `Assets/Editor/ArenaBoundaryRing.cs` | Cosmetic exterior boundary ring |
| `Assets/Editor/RaidNavBake.cs` | Ground plane bake, wall carving obstacles |
| `Assets/_Modules/Village/Walls/RepairTarget.cs` | Uniform repair abstraction |
| `Assets/_Modules/Village/Walls/WallRepairController.cs` | Player repair flow, Repair-All, pointer-over-UI guard |
| `Assets/_Modules/Village/Troops/RaidAssaultAi.cs` | Phases, target selection, formation, rally-march rules |
| `Assets/_Modules/Village/Troops/TroopController.cs` | Live troop combat/targeting loop |
| `Assets/_Modules/Village/Troops/TroopBreachOrder.cs` | The explicit player breach order (new, WO-1719) |
| `Assets/_Modules/Village/Troops/RaidDeployController.cs` | Deploy/Rally/Breach HUD and input handling |
| `Assets/_Modules/Village/World/Camps/RaidSpire.cs` | The raid objective structure |
| `Assets/_Modules/Village/World/Camps/RaidVictoryController.cs` | Victory detection, claim, loot grant, return |
| `Assets/_Modules/Village/World/Camps/RaidClaimService.cs` | Claim persistence, loot gating axes |
| `Assets/_Modules/Village/Vfx/StructureHitReaction.cs` | The central hit-feedback seam (dust/number/sound/bar timing) |
| `Assets/_Modules/Village/Vfx/StructureDamageVisuals.cs` | Structure scanning/registration, bar attachment |
| `Assets/_Modules/Village/Enemies/DamageNumberSpawner.cs` | The one floating-text pool |
| `Assets/_Modules/Core/Combat/IDamageableStructure.cs` | The damage interface contract |
| `Assets/_Modules/Audio/SfxId.cs` | Sound ID enum, `StructureImpact` entry |
| `Assets/Editor/Regression/WallBreachOrderRegression.cs` | Proof of breach-order/aggro/rally-release rules |
| `Assets/Editor/Regression/StructureFeedbackRegression.cs` | Proof of post-tier-divide numbers, rate limits, accumulation |

---

**Source lanes:** three parallel read-only documentation passes over the live codebase, 2026-09-14,
each independently citation-checked before merging. Spot-verified against source by the CLI lead before
this merge (the four-phase enum and the immediate-collapse-disable behavior were independently
re-confirmed at source, alongside several claims already proven during the same day's implementation
work on WO-1717/1719).

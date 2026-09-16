# Raid Systems Reference: Structure Damage & Feedback Flow

**Date:** 2026-09-14  
**Scope:** Structures under raid attack—walls, buildings, gates, towers, and defensive structures  
**Purpose:** Technical reference for external design/research discussion about how structures take damage and react visually

---

## 1. Attack Flow: Damage Delivery Through to HP Change

When an attacker (enemy, player ability, troop, or tower) deals damage to a structure, the flow is:

### Entry Points (Two Seams, One Destination)

**Enemy contact damage path:**  
[Enemy.cs] → `IDamageableStructure.ApplyContactDamage(float amount)` [`WallSegment.cs:315`]

**Player/troop attack path:**  
[PlayerAttackController / TroopController] → `IDamageable.TakeDamage(float amount, DamageElement element)` → routes to `ApplyContactDamage` internally [`WallSegment.cs:271`]

Both entry points funnel into the single private method:

```csharp
private void ApplyDamage(float amount, string via)  // WallSegment.cs:325-353
```

### Damage Reduction Pipeline (In Order)

1. **Tier-Based Reduction (Toughness Scaling)**  
   - Higher tiers divide incoming damage by a geometric factor  
   - Formula: `effective = amount / ToughnessFor(tier)` [`WallSegment.cs:334`]
   - `ToughnessFor(tier)` = `1.6^(tier-1)` [`WallSegment.cs:168`]
   - Example: A tier-3 (ReinforcedSteel) wall divides by 1.6² = 2.56 [`StructureFeedbackRegression.cs:186`]
   - **Authority**: `WallSegment.MaxHp = 100f` [`WallSegment.cs:109`] is the constant divisor; tier reduction is exact, not approximate [`WallSegment.cs:334`]

2. **Faction-Gated Talent Reduction (BULWARK)**  
   - Only applied to player-owned structures (Faction == Friendly) [`WallSegment.cs:343`]
   - Formula: `effective *= 1f - StructureToughnessReduction(...)` [`WallSegment.cs:344`]
   - Abilities: "Hardened Ramparts" (always-on) + "Warden of Elarion" (wave-phase-only) [`WallSegment.cs:336-342`]
   - **Deliberate exclusion**: Enemy raid walls (Faction == Hostile) are NOT protected by hero talents, so defensive investment doesn't make raiding harder [`WallSegment.cs:339-342`]

3. **HP Track Accumulation**  
   - `_damage = Clamp(_damage + effective, 0, 100)` [`WallSegment.cs:346`]
   - Clamps to 0-100 range; values outside ignored
   - Inverted model: Damage counts UP from 0 to 100; HP = MaxHp - Damage [`WallSegment.cs:261`]

### Collapse Trigger

When `_damage >= 100f`, `Collapse()` fires [`WallSegment.cs:352`], disabling colliders and NavMeshObstacles [`WallSegment.cs:370-388`] and raising the `Collapsed` event [`WallSegment.cs:394`].

---

## 2. Visible & Audible Feedback: What Fires on Impact

When a structure is hit, **three independent channels** fire simultaneously from a single observation point:

### The Central Seam: `StructureHitReaction`

Every damageable structure has `StructureHitReaction` attached by `StructureDamageVisuals.Register()` [`StructureDamageVisuals.cs:909-910`].

This component:
- Polls the structure's HP fraction every frame [`StructureHitReaction.cs:313-336`]
- Calls `TrySample(hpNow, Time.time, out HitSample hit)` [`StructureHitReaction.cs:232`]
- On the frame the HP drops, fires all three channels if conditions are met

### Channel 1: Motion (Dust Burst)

**What fires:**  
`VFXManager.Play(VFXType.Env_DestructionDust, hit.At)` [`StructureHitReaction.cs:342`]

**Placement:**  
- Bounds centre of the structure (recomputed per burst in case tier changes) [`StructureHitReaction.cs:297-310`]
- For landscape phones (2670x1200), seated at bounds centre, not above, to avoid frame crop [`StructureHitReaction.cs:54`]

**Rate limit:**  
- `MinBurstInterval = 0.15f` seconds [`StructureHitReaction.cs:108`]
- Per-structure cadence: a six-troop warband chewing one panel produces bursts at ~0.15s intervals, not a continuous smear [`StructureHitReaction.cs:104-106`]

**Family type:**  
- Family B one-shot; no loop slot consumed, no handle to stop, cannot leak against VFXManager's 20-slot cap [`StructureHitReaction.cs:41-46`]

### Channel 2: Sound (Masonry Impact)

**What fires:**  
`AudioService.Instance?.PlaySfxAtPosition(SfxId.StructureImpact, hit.At)` [`StructureHitReaction.cs:356-357`]

**SfxId Definition:**  
- Declared as `StructureImpact` in enum [`SfxId.cs:54`]
- **Current state (2026-09-14)**: No authored audio clip exists yet  
- **Fallback**: Routes to `ProceduralSfx.synth` for a placeholder thud [`SfxId.cs:49-52`]
- When an artist drops a clip at audio key `Sfx/Sfx_StructureImpact` OR a row is added to `SfxClipLibrary`, that replaces the synth with no code change [`SfxId.cs:49-52`]

**Rate limit:**  
- Tied to the number cadence, not the burst cadence (see Channel 3)
- Only fires if `hit.Credible == true` (implausible drops withheld) [`StructureHitReaction.cs:344-358`]

### Channel 3: Number (Floating Damage Text)

**What fires:**  
`DamageNumberSpawner.Spawn(hit.Damage, hit.At)` [`StructureHitReaction.cs:350`]

**Placement:**  
- Same world position as the dust burst (bounds centre)
- Billboarded to Camera.main [`DamageNumberSpawner.cs:399-406`]

**Appearance:**  
- Integer damage amount displayed as rising, fading text
- Colour lerps from warm gold (normal hits) to orange-red (big hits) based on magnitude [`DamageNumberSpawner.cs:62-67, 305-312`]
- No colour tint carries meaning; owner is red/green colourblind [`StructureHitReaction.cs:67-70`]
- Lifetime: 0.55 seconds [`DamageNumberSpawner.cs:46`]
- Rises: 1.0 world unit over lifetime, with ease-out motion [`DamageNumberSpawner.cs:48-49, 385-386`]

**Rate limit:**  
- `MinNumberInterval = 0.35f` seconds [`StructureHitReaction.cs:133`]
- **Deliberately longer than burst cadence** so max ~2 numbers coexist over a structure (legible) while dust still fires per blow [`StructureHitReaction.cs:126-131`]

**Accumulation rule (critical):**  
- Damage landing inside the number cadence is **held, never discarded** [`StructureHitReaction.cs:82-86`]
- Field `_pendingNumberDrop` accumulates the drops [`StructureHitReaction.cs:171`]
- On the next eligible frame (cadence expires), the **sum** flushes into one number [`StructureHitReaction.cs:268-282`]
- Example: Six troops land 8+8+4 = 20 points in rapid succession; one number prints "20" instead of three stacked ones [`StructureFeedbackRegression.cs:240-246`]
- **Design intent**: "A throttle on the TELL must not become a lie about the TOTAL" [`StructureFeedbackRegression.cs:32-33`]

### Channel 4: Floating Health Bar (UI, Not VFX)

**What fires:**  
`FloatingHealthBar.Attach(host, hpFraction, ...)` [`StructureDamageVisuals.cs:931`]

**Timing:**  
- **WO-1717 critical fix (2026-09-14)**: Bar attaches on the **first hit**, on the same frame the HP changes, via the `StructureHitReaction` callback [`StructureDamageVisuals.cs:896-910`]
- Before WO-1717: Bar was attached by a 0.3s `Evaluate` poll behind a 2.0s `Scan` poll, so a struck structure could appear inert for up to 2.3 seconds [`StructureHitReaction.cs:1-16`]
- Hidden when HP is full, shown when damaged [`StructureDamageVisuals.cs:931`]
- Positioned at structure bounds + configurable offset [`StructureDamageVisuals.cs:850-860`]

**Pool:**  
- One single pool: `DamageNumberSpawner` [`StructureHitReaction.cs:346-350`]
- This is the ONLY floating-text pool in the game; never build a second stack [`DamageNumberSpawner.cs:160-164`]

---

## 3. Is the Damage Number Raw or Adjusted?

**The number is the POINTS ACTUALLY APPLIED, not the incoming request.**

### The Calculation

The `StructureHitReaction` reads the **HP fraction drop** (state change after reduction), not the incoming damage value:

```csharp
float delta = _last - hpNow;  // 0..1 fraction that fell this frame
_pendingNumberDrop += delta;  // accumulate
float points = _pendingNumberDrop * maxHp;  // convert to points
```

[`StructureHitReaction.cs:239-249, 276`]

For a wall: `maxHp = 100f` [`WallSegment.cs:109`], so `drop * 100 = the post-tier-divide points exactly` [`StructureFeedbackRegression.cs:22-27`].

### Why This Is Post-Reduction

1. Tier reduction is applied in `ApplyDamage` BEFORE the HP field is written [`WallSegment.cs:334, 346`]
2. `StructureHitReaction` samples the **result** (the new HP), not the input [`StructureHitReaction.cs:232-249`]
3. The fraction can only move by the post-tier-divide value; it is mathematically impossible to fabricate a wrong number [`StructureFeedbackRegression.cs:23-27`]

### Example

A tier-3 steel wall takes a 29-damage archer hit:
- Tier divide: `29 / 2.56 = 11.33` points actually applied [`StructureFeedbackRegression.cs:186-187`]
- Wall's HP fraction drops by `11.33 / 100 = 0.1133`
- Number printed: `0.1133 * 100 = 11.33` (displayed as "11") [`StructureFeedbackRegression.cs:196-198`]
- The displayed number is the **post-divide effective damage**, never the raw 29 request

---

## 4. Which Structures Get Full Feedback vs. Partial?

### Full Feedback (Dust + Number + Sound + Bar)

| Structure Type | Scanned By | Notes |
|---|---|---|
| **WallSegment** | `StructureDamageVisuals.Scan()` via `RegisterRepairables<WallSegment>()` [`StructureDamageVisuals.cs:755`] | Has MaxHp (const 100); prints number |
| **Building** | `RegisterRepairables<Building>()` [`StructureDamageVisuals.cs:756`] | Has MaxHp; prints number |
| **Tower** | `FindObjectsByType<Tower>()` [`StructureDamageVisuals.cs:784`] | Registered with MaxHp callback [`StructureDamageVisuals.cs:790`]; prints number |
| **DefenseTower** | `FindObjectsByType<DefenseTower>()` [`StructureDamageVisuals.cs:793`] | Same as Tower |
| **ArcaneTower** | `FindObjectsByType<ArcaneTower>()` [`StructureDamageVisuals.cs:802`] | Same as Tower |
| **HarvestSite** | `FindObjectsByType<HarvestSite>()` [`StructureDamageVisuals.cs:811`] | Same as Tower |

### Partial Feedback (Dust + Sound + Bar, **NO Number**)

| Structure Type | Reason | Citation |
|---|---|---|
| **ResourceCollector** | No public `MaxHp` getter exposed | [`StructureDamageVisuals.cs:767-774`]: "its runtime callers all take the component's own DefaultMaxHp (120) rather than the catalog row - so reading BuildingCatalog here would print a number that does not match the HP that actually moved" |
| | Receives dust + sound + bar instead of a lying number | WO-1717 Section 6C explicitly forbids gameplay-class edits to add the getter; flagged in the WO RESULT |

### Special Cases (Opt-Out, Bespoke Tell)

| Structure Type | Feedback Type | Reason |
|---|---|---|
| **Gate** | **Data opt-out** [`StructureDamageVisuals.cs:757`] | Has bespoke force-field collapse tell (no dust/number/bar) |
| **Heart (HeartController)** | **Never scanned** [`StructureDamageVisuals.cs:749`] | Has bespoke 7-state crystal tell (colour-free, no aura on state tells) [`StructureHitReaction.cs:18-25`]; attaches its own flinch via `StructureHitReaction.Attach(..., onHit: callback)` to add a kick to the existing aura pulse [`StructureHitReaction.cs:200-202, 288`] |

---

## 5. Hit-Stop, Screen Shake, or Other Game-Feel Elements?

**No hit-stop or screen shake is implemented for structure damage.**

### What Exists

Only the three feedback channels listed in §2:
1. **Motion**: Dust burst (VFXType.Env_DestructionDust) [`StructureHitReaction.cs:342`]
2. **Sound**: SfxId.StructureImpact [`StructureHitReaction.cs:357`]
3. **Number**: Floating damage text [`StructureHitReaction.cs:350`]

### Frame Budget Consideration

Structure damage is **not** on the per-frame measurement scope. The 4-arg `FlowTrace.Measure()` pattern (used for frame-cost instrumentation on hot paths like hero.Update) is **not** applied to `StructureHitReaction.Update()` or `StructureDamageVisuals.Update()` [`FlowTrace.cs:308`]. This suggests the cost is considered negligible for now.

### Rationale (From Code Comments)

- The component is designed to be **"presentation only"** [`StructureHitReaction.cs:95-98`]
- The decision logic (`TrySample`) is split out so it can run headless without any rendering, camera, VFX pool, or audio device, making it verifiable in tests without PlayMode [`StructureFeedbackRegression.cs:48-49`]
- No impact shake or stun is mentioned anywhere in the damage path or the VFX layer

---

## 6. Known Gaps and Placeholders

### Gap 1: StructureImpact Audio Is Synth-Only

**Status**: Placeholder  
**Where**: `SfxId.cs:54` declaration and `StructureHitReaction.cs:356-357` call site  

No authored audio clip exists. Every structure-impact sound currently uses the procedural synth recipe (a placeholder thud) because no artist-authored clip was dropped at the audio key `Sfx/Sfx_StructureImpact` [`SfxId.cs:49-52`].

**How to close**: Drop an audio clip at that key or add a row to a generated `SfxClipLibrary`. No code change required.

### Gap 2: ResourceCollector Damage Numbers Are Withheld

**Status**: Deliberate (gated by WO-1717 restrictions)  
**Where**: `StructureDamageVisuals.cs:767-774`  

`ResourceCollector` does not expose a public `MaxHp` getter. Its internal HP uses `DefaultMaxHp` (120), but the catalog row may differ. Rather than print a number that could lie about the HP that actually moved, the component prints **no number**—only dust, sound, and bar [`StructureDamageVisuals.cs:769-774`].

**Fix authority**: WO-1717 Section 6C forbids adding a gameplay-class edit to expose the getter, so this is pinned for a future WO.

### Gap 3: No Damage Accumulation Across Tick Intervals

**Status**: By design (not a gap)  
**Details**: The `_pendingNumberDrop` accumulator holds damage that lands outside the number cadence and flushes it into the next printed number. This only applies to blows within a single structure's lifecycle. If a structure is repaired or respawned, the pending total resets on re-attach [`StructureHitReaction.cs:212-213`].

### Gap 4: Gate Feedback Is Bespoke; Not Wired Through Generic Channel

**Status**: Intentional architectural separation  
**Where**: `StructureDamageVisuals.cs:757-758` (data opt-out)  

Gate implements a data opt-out flag so `StructureDamageVisuals` never scans it. Its collapse tell is a custom force-field shader effect. No generic damage number or dust fires; the force-field ramp is the entire tell [`StructureDamageVisuals.cs:77-80`].

---

## Summary: The Three Channels as One Signal

When a structure is struck:

1. **Frame 0 (Instant)**:  
   - `StructureHitReaction.TrySample()` detects the HP drop [`StructureHitReaction.cs:232-289`]
   - Dust burst fires (Env_DestructionDust) [`StructureHitReaction.cs:342`]
   - Host's own flinch callback fires (e.g., Heart's aura pulse kick) [`StructureHitReaction.cs:288`]
   - Floating bar attaches if not already present [`StructureDamageVisuals.cs:909-910`]

2. **Frame 0, Subject to Number Cadence**:  
   - If cadence allows (>= 0.35s since last number), damage number prints [`StructureHitReaction.cs:268-282`]
   - If cadence blocks, damage is held and added to the next number [`StructureHitReaction.cs:276-286`]

3. **Frame 0, If Hit Is Credible**:  
   - Sound fires (SfxId.StructureImpact) [`StructureHitReaction.cs:356-357`]
   - Implausible drops (save-restore) withhold sound and number; dust still fires [`StructureHitReaction.cs:264-267, 360-364`]

**The read is MOTION + SOUND + NUMBER and never a colour** (owner is red/green colourblind). Remove any one channel and the blow still reads [`StructureHitReaction.cs:67-70`].

---

## Citations Index

| File | Lines | Topic |
|---|---|---|
| `Assets/_Modules/Village/Vfx/StructureHitReaction.cs` | 1-373 | Hit detection, number/sound/dust cadence, accumulation logic, damage calculation |
| `Assets/_Modules/Village/Vfx/StructureDamageVisuals.cs` | 1-1622 | Structure scanning, health bar attachment, damage-states catalog, burn/critical/scuff tells |
| `Assets/_Modules/Village/Enemies/DamageNumberSpawner.cs` | 1-411 | Floating damage text pool, colour ramp, billboard logic |
| `Assets/_Modules/Core/Combat/IDamageableStructure.cs` | 1-87 | Damage interface contract, faction rule, implementor list |
| `Assets/_Modules/Audio/SfxId.cs` | 1-92 | SfxId enum, StructureImpact entry (54) and documentation (42-54) |
| `Assets/Editor/Regression/StructureFeedbackRegression.cs` | 1-417 | Test oracle proving damage number is post-tier-divide, rate limits, accumulation rules |
| `Assets/_Modules/Village/Walls/WallSegment.cs` | 1-452+ | Tier toughness formula (106-169), damage entry points (271, 315), tier divide (334), BULWARK reduction (343-344), collapse (362-400) |

---

## Document Metadata

- **Read at**: 2026-09-14
- **Verification method**: Source code citations (file:line)
- **Headless coverage**: StructureHitReaction.TrySample() is test-driven by StructureFeedbackRegression
- **Live coverage**: Dust, sound, number, and bar fire in PlayerMode every frame a structure is struck
- **Owner visibility**: Floating numbers and health bar are player-visible feedback; dust and sound are perceptual confirmation

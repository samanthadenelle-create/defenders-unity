# WORK ORDER 1717 - Wall damage: no player breach VERB, and structure damage fires no numbers or sound

**Status:** READY TO IMPLEMENT - read-only RCA complete, three findings each cited at source; recommend SPLITTING into three file-disjoint follow-ups (see section 6)
**Minted:** 2026-09-14 by the read-only RCA lane, from the owner's raid report below. No `.cs`, `.unity`
or asset touched by this lane; no Unity run, no gate, no commit.
**Branch:** `dev` @ `96c208654`

---

## 1. Owner report, verbatim

> "can you check on how wall damage is calculated, might need a breach option where you can target a
> single section of wall to breach, i figured targeting wall would be enough but doesnt seem to work,
> also when troops attack you should see the damage numbers so you can tell something is happening"

---

## 2. FINDING 1 - How wall damage/HP is calculated today

**PROVEN.** Every line below was opened at source this session.

### The model
`WallSegment` (`Assets/_Modules/Village/Walls/WallSegment.cs:58`) implements BOTH `IDamageable` (the
player/troop seam) and `IDamageableStructure` (the enemy contact seam).

- HP is stored **INVERTED**: there is no HP field. `_damage` counts UP 0..100 (`:84`), `MaxHp = 100f`
  (`:109`), `Hp => Mathf.Max(0f, MaxHp - _damage)` (`:261`), `IsDestroyed => _damage >= 100f` (`:146`).
  **Every wall in the game has the same 100-point track regardless of tier** - tier changes the RATE,
  not the pool.
- Both entry points funnel into ONE private method so they can never diverge:
  - `TakeDamage(float, DamageElement) => ApplyDamage(amount, "attack")` (`:271`) - hero / troops / pets.
  - `ApplyContactDamage(float) => ApplyDamage(amount, "contact")` (`:315`) - enemies in melee contact.
- `ApplyDamage` (`:325-353`), in order:
  1. `if (amount <= 0f || IsDestroyed) return;` (`:327`)
  2. tier divide: `effective = amount / ToughnessFor(t)` (`:334`), where
     `ToughnessFor(tier) = Mathf.Pow(1.6f, tier - 1)` (`:165-169`, `TierToughnessStep = 1.6f` at `:106`).
     Ceiling is `RepoProps.MaxStructureLevel` via `MaxTier` (`:157`) - never a literal.
  3. BULWARK talent reduction, **gated on Faction == Friendly** (`:343-344`) so hero defence talents
     never make an ENEMY raid wall tougher (WO-853 section 9).
  4. `_damage = Mathf.Clamp(_damage + effective, 0f, 100f)` (`:346`), raise `DamageChanged` (`:347`),
     one throttled `FlowTrace` line (`:348-350`).
  5. `if (_damage >= 100f) Collapse();` (`:352`)

### Faction is DERIVED, never serialized
`Faction => SceneOwnership.IsEnemyOwned ? Hostile : Friendly` (`:248-249`). The raid flip is live:
`RaidGarrisonSpawner.cs:156` calls `SceneOwnership.SetEnemyOwned(true)`. So raid walls DO read Hostile
and ARE attackable - **this is not the defect**.

### What happens at 0 HP - there is NO distinct "breach" state
`Collapse()` (`:362-400`) is the only terminal state, runs exactly once (`_collapsed` latch, `:364`):
- disables every non-trigger enabled collider in children (`:370-375`) - so tower Structure-mask
  line-of-sight linecasts stop being blocked;
- disables every enabled `NavMeshObstacle` in children (`:383-388`) - **this is what actually opens the
  hole for NavMeshAgents**, a collider alone never carved anything (`:377-381`);
- `FlowTrace.Step` naming both counts (`:390-392`), then `Collapsed?.Invoke(this)` (`:394`);
- readability only: `CollapseRoutine()` (`:409`) sinks the ruin `SinkFraction = 0.85` of its height
  (`:114`) over `_collapseSeconds = 0.9f` (`:97`) and ramps a `_Collapse` shader float through an MPB
  (`:446-455`) which is a **silent no-op on a plain URP/Lit wall material** (`:118-120`).
- Destroyed is destroyed: `Repair` refuses to revive a collapsed section (`:359-360`).

**So: "breach" == "collapsed". There is no partial-breach / cracked-open / passable-gap state.**

### The actual throughput numbers (read from data, not assumed)
Troop stats from `Assets/Resources/Data/Canonical/troops.json`, consumed at
`TroopController.cs:420-432` (`attackDamage`, `attackCooldown`, `structureDamageMult`) and applied at
`TroopController.cs:1370-1394`:

| troop | dmg | cd (s) | structMult |
|---|---|---|---|
| troop-footman | 12 | 1.0 | - (1.0) |
| troop-spearman | 16 | 1.1 | - |
| troop-outrider | 18 | 0.9 | - |
| troop-archer | 29 | 1.2 | - |
| troop-battlemage | 42 | 1.8 | - |
| troop-catapult | 48 | 2.5 | **2.0** |

Raid wall tiers are authored per base in `Assets/Resources/Data/Canonical/scene-configs.json`
(`wallTier` = `Wood`/`Iron`/`ReinforcedSteel`), parsed at `RaidBaseGenerator.cs:505` and pushed with
`ws.SetTier((int)tier)` at `RaidBaseGenerator.cs:1856`; `WallTier` = `Wood=1, Iron=2,
ReinforcedSteel=3` (`Assets/_Modules/Village/Walls/WallTierData.cs:30-35`). Inner keep rings are
hardcoded `ReinforcedSteel` (`RaidBaseGenerator.cs:522`).

Hits-to-collapse = `100 * 1.6^(tier-1) / (dmg * structMult)`:

| | Wood (x1) | Iron (x1.6) | Steel (x2.56) |
|---|---|---|---|
| one footman | 9 hits / ~9 s | 14 / ~14 s | 22 / ~22 s |
| one catapult | 2 / ~5 s | 4 / ~10 s | 3 / ~7 s |

**CONCLUSION: throughput is NOT the defect.** A single footman drops a wood panel in ~9 seconds and a
steel one in ~22. A warband of six does it in seconds. If the owner sees a wall that never falls, the
troops are not hitting THAT wall - which is exactly finding 2.

---

## 3. FINDING 2 - Wall TARGETING: what actually happens when you "target a wall"

### PROVEN (read at source)

**a. There is NO player verb that targets a wall section. None. Anywhere.**
`RaidDeployController` (`Assets/_Modules/Village/Troops/RaidDeployController.cs`) exposes exactly two
world taps: DEPLOY and RALLY (header `:17`). The rally tap is `HandleRallyTap(Vector2)`
(`:857-868`) and its entire body is:
```
if (!RaycastGround(screenPoint, out RaycastHit hit)) return;
TroopRally.Point = hit.point;
ShowRallyFlag(hit.point);
```
`TroopRally` (`Assets/_Modules/Village/Troops/TroopRally.cs:29-35`) is a single global `Vector3?` -
a POINT, with no target reference, no `IDamageable`, no structure id. `RaidDeployController` is its
ONLY writer (TroopRally header `:15-17`).

**b. Tapping a wall does not error - it sets a muster point ON the wall.**
`RaycastGround` (`RaidDeployController.cs:731-736`) tries `_groundMask` first and then **falls through
to `Physics.Raycast(ray, out hit, _rayDistance, ~0)`** - all layers, walls included. So the tap
succeeds and drops a rally flag at the point on the wall's face. From the player's side that reads as
"I told them to attack this wall". Nothing in the code reads it that way.

**c. The wall the warband actually attacks is chosen SCENE-WIDE, not by the player.**
`TroopController.SharedBreachFocus(Vector3 muster)` (`TroopController.cs:1150-1175`) does a static,
0.4 s-cached `FindObjectsByType<WallSegment>` over the WHOLE scene, keeps only `Faction == Hostile`
and alive, and hands them to `RaidAssaultAi.SelectFocusBreach` (`RaidAssaultAi.cs:194-219`), whose
rule is: **lowest Hp wins; ties within 0.5 HP break by nearest to the muster point** (`:209-216`).

**d. That scene-wide pick OVERRIDES the local scan unconditionally.**
`TroopController.cs:961-966`:
```
var focus = RaidAssaultAi.SelectFocusBreach(FocusWallScratch, muster);   // local overlap
if (focus != null) nearestOtherStruct = focus;
IDamageable shared = SharedBreachFocus(muster);
if (shared != null && shared.IsAlive) nearestOtherStruct = shared;       // scene-wide WINS
```
`nearestOtherStruct` is then bucket 2 out of `RaidAssaultAi.PickBucket` (`:1010-1019`).

**So the player's tap influences the breach target ONLY as the TIE-BREAK inside `SelectFocusBreach`,
and only while every candidate wall is within 0.5 HP of every other. The moment ANY wall anywhere on
the map has taken >0.5 more damage than the rest - stray AoE, a previous approach, a tower duel -
that wall wins permanently and the warband walks away from the panel the owner tapped.** That is the
literal mechanism behind "i figured targeting wall would be enough but doesnt seem to work".

**e. And while walking to the rally, walls are suppressed entirely.**
`RaidAssaultAi.RallyHoldsMarch(rallySet, arrivedAtRally, peelThreat)` (`:185-188`) returns true while
a rally is set and the troop has not arrived; `TroopController.cs:966-970` then NULLS
`nearestOtherStruct`. Arrival is a flat-XZ test against `RallyArrivalEpsilon = 1.25f`
(`TroopController.cs:203`, used at `:707` and `:954`).

### NOT PROVEN (candidate, needs one headless/device capture before anyone edits)
A rally point dropped **on a wall face** may sit inside or behind masonry
(`WallLayout.WallThickness = 0.62f`, `Assets/_Modules/Village/Walls/WallLayout.cs:147`; raid segment
depth is authored separately in `RaidBaseGenerator.PlaceSegment`). If a troop cannot get within 1.25 m
flat of that point, `arrivedAtRally` never goes true, `RallyHoldsMarch` stays true forever, and the
troop loops the idle branch walking into the wall while NEVER selecting it. **I have not measured this
at runtime and am not asserting it.**
**The proving line already exists and needs no new instrumentation:** the throttled
`troopai-idle-{instanceId}` trace at `TroopController.cs:685-694` prints
`IDLE/RALLY: no acquirable hostile ... rally=<x,y,z> action=walk-to-rally`. If that line repeats for a
troop standing flush against a wall, the candidate is confirmed. If instead the troop reports a foe,
it is not this.

---

## 4. FINDING 3 - Damage-number / feedback gap

### PROVEN

**A damage-number system EXISTS and is simply never called from any structure.**
`DeNelle.Village.DamageNumberSpawner` (`Assets/_Modules/Village/Enemies/DamageNumberSpawner.cs`,
410 lines, pooled, billboarded) is the project's floating-text pool. `SpawnResourceGain` /
`SpawnLabel` have several callers (EchoService, ProgressionManager, AttackTimingBonus, DevPanel,
ResourceGainPopup). But **`DamageNumberSpawner.Spawn(...)` - the damage NUMBER - has exactly two
call sites in the whole tree, both inside `Enemy.cs`:**
- `Assets/_Modules/Village/Enemies/Enemy.cs:2833` (tinted)
- `Assets/_Modules/Village/Enemies/Enemy.cs:2838` (untinted)

`CombatFeedbackManager.Hit(hitPos, amount)` likewise has ONE gameplay caller, `Enemy.cs:2908`.
`WallSegment.cs`, `RaidSpire.cs`, `Gate.cs`, `Building.cs` and `Tower.cs` contain **zero** references
to `DamageNumberSpawner`, `CombatFeedbackManager`, `CombatText` or `HitStop` (grepped this session;
`Tower.cs:908` is a `TowerAudioController` lookup for its own SHOT, not for taking damage).

**=> Damaging a wall, gate, tower, building or the spire produces NO number, for the hero and for
troops alike. Killing an enemy unit produces one. That asymmetry is the owner's complaint.**

**Some non-number feedback DOES fire on walls - so this is a gap, not a void.**
`StructureDamageVisuals` self-installs in EVERY scene
(`Assets/_Modules/Village/Vfx/StructureDamageVisuals.cs:648-666`,
`[RuntimeInitializeOnLoadMethod(AfterSceneLoad)]` + `sceneLoaded`), scans every 2.0 s
(`:719-725`) and registers walls at `:750` (`RegisterRepairables<WallSegment>("wall")`).
`RegisterRepairables` (`:806-817`) wraps via `RepairTarget.TryWrap`
(`Assets/_Modules/Village/Walls/RepairTarget.cs:77-98`), which has **no faction filter** - so HOSTILE
raid walls are tracked too. That registration yields:
- **A per-hit dust puff.** `StructureHitReaction.Attach(host, hp, name)` at
  `StructureDamageVisuals.cs:869`; the component polls the HP delegate EVERY FRAME
  (`StructureHitReaction.cs:150-186`) and fires `VFXManager.Play(VFXType.Env_DestructionDust, at)`
  (`:180`) at the renderer-bounds centre. Gated on `MinDropFraction = 0.004f` (`:83`, i.e. 0.4 damage
  on the 0-100 track - every troop hit clears it) and rate-limited per structure at
  `MinBurstInterval = 0.15f` (`:76`). The VFX enum IS mapped to a real prefab
  (`Assets/Editor/VFXCatalogGenerator.cs:271` -> `Burst/Poof_generic.prefab`, pool 10), and
  `ParticlePackVfxBatchBuilder.cs:1174` records it is a deliberate STAND-IN, not an owner art pick.
- **A floating HP bar, attached LAZILY on first damage** and hidden again at full:
  `StructureDamageVisuals.cs:951-958` -> `FloatingHealthBar.Attach(..., hideAtFull: true)`. Note it is
  driven by the 0.3 s `Evaluate` poll, and registration itself is on a 2.0 s `Scan` poll, so a wall
  first struck seconds into a raid can go a beat before it has a bar at all.

**No audio at all.** `Assets/_Modules/Audio/SfxId.cs` has an "Impact sounds" block (`:33-70`) whose
members are fire explosion, tower shot, pet attack - **there is no structure-hit / masonry-impact
`SfxId` in the enum**, and `WallSegment.cs` contains no audio call of any kind.

**No hit-stop, no screen shake on structure damage** - `HitStopManager` is driven from
`CombatFeedbackManager.Kill()` on enemy kills (`HitStopManager.cs:235,433`), never from a structure.

**=> The honest summary for the owner: hitting a wall gives you a small grey dust poof and, after a
beat, a thin HP bar. No number, no sound, no impact. "You can tell something is happening" is a
broader feedback gap than numbers alone.**

---

## 5. What this is, per `docs/TICKET_PIPELINE.md` classification

- **Finding 1** - existing, correct, no defect found. Documented above so nobody re-derives it.
- **Finding 2** - the breach VERB is **NEW FEATURE, NOT BUILT**. It should not be RCA-"fixed". The
  suppression/rally-arrival candidate inside it IS an EXISTING-code question and is separable.
- **Finding 3** - **EXISTING system, not wired.** The pool, the layer and the seam all already exist;
  nothing new needs designing.

---

## 6. RECOMMENDATION: SPLIT INTO THREE. They are file-disjoint and independently shippable.

Yes - split. The three touch no common file and have three different owners.

### 6A. Breach verb - "tap a wall section to focus the warband on it" (NEW FEATURE, needs an owner ruling first)
Files: `RaidDeployController.cs`, `TroopRally.cs` (or a new `TroopBreachOrder` static),
`RaidAssaultAi.SelectFocusBreach`, `TroopController.SharedBreachFocus`.
Open design questions for the PO before any code:
1. Is the breach order a THIRD tap mode beside Deploy/Rally, or does a Rally tap that lands on a wall
   collider implicitly become a breach order? (WWCD: CoC has no breach verb at all - troops pick.
   The owner is explicitly asking for more control than CoC, so this is a deliberate divergence.)
2. Does a player breach order OVERRIDE `SelectFocusBreach`'s most-damaged rule outright, or only
   re-bias the tie-break? (Recommend: override outright while the ordered wall is alive, then fall
   back to the existing rule - otherwise the same "doesn't seem to work" returns the first time a
   stray arrow chips another panel.)
3. Does the order survive the wall collapsing (auto-advance to the next panel in that run) or clear?

Acceptance criteria (once ruled):
- [ ] Tapping a hostile `WallSegment` in a raid sets an explicit ordered target that is visibly
      marked (a reticle / highlight on THAT panel, not a ground flag).
- [ ] While that order stands and the wall is alive, **every** deployed troop whose phase allows
      structures picks THAT wall - proven by a headless capture where a second wall has been
      pre-damaged below it and is still NOT chosen.
- [ ] `RallyHoldsMarch` does not suppress an EXPLICIT breach order (only the implicit ring-farm).
- [ ] The order clears on wall collapse and on raid teardown (same lifetime as `TroopRally.Clear()`,
      `RaidDeployController.cs:149,264,997,1068`).
- [ ] New EditMode cases in `Assets/Editor/Regression/RaidAssaultAiRegression.cs` pinning the override
      precedence, registered in `DataRegression.cs`.

### 6B. Rally-on-a-wall never arrives (EXISTING, INSTRUMENT FIRST - do not edit on this WO's word)
Files: `TroopController.cs` (arrival test), `RaidDeployController.RaycastGround`.
- [ ] **Gate: capture before code.** Run a raid headless/on device, drop a rally ON a wall face, and
      read `TroopController.cs:685-694`'s `troopai-idle-{id}` line. Attach the captured lines to the
      ticket. If `action=walk-to-rally` repeats while the troop is flush against masonry, proceed.
      If not, CLOSE this sub-ticket as not-reproduced - do not "fix" it anyway.
- [ ] Only then: either clamp the rally point to a navigable position (`NavMesh.SamplePosition`) at
      the tap, or make `arrivedAtRally` satisfiable by a "cannot get closer" test.
- [ ] Regression case pinning that a rally point inside geometry still lets troops act.

### 6C. Structure damage feedback - numbers, sound, punch (EXISTING system, presentation layer only)
Files: `Assets/_Modules/Village/Vfx/StructureHitReaction.cs` (the seam), possibly
`Assets/_Modules/Audio/SfxId.cs` + `SfxClipLibrary`. **No gameplay class is edited.**
- The cheapest correct seam is already built: `StructureHitReaction` has an `_onHit` callback invoked
  at `:181` and already knows the drop fraction and the bounds centre. One structure, one component,
  covers walls + buildings + gates + towers + collectors + harvest sites, exactly as its header
  argues (`:27-31`).
- The alternative seam, `WallSegment.DamageChanged` (`WallSegment.cs:283`), is WALL-ONLY and would need
  repeating per type - prefer `StructureHitReaction` unless the ticket owner proves otherwise.
- [ ] A floating damage number appears at the impact point every time a structure takes damage from
      the hero OR a troop, using the EXISTING `DamageNumberSpawner.Spawn` pool - no second pool
      (WO-953 one-pool ruling, `ResourceGainPopup.cs:24,46`).
- [ ] It carries the damage actually APPLIED after the tier divide (`WallSegment.cs:334`), not the raw
      request - the number must not lie about a steel wall.
- [ ] Rate-limited so a six-troop warband on one panel does not stack a wall of text - reuse
      `MinBurstInterval` semantics (`StructureHitReaction.cs:76`), and state the chosen cap in the
      RESULT.
- [ ] A masonry-impact `SfxId` is added and played on the same beat (new enum member + clip mapping).
      **Owner is colourblind (memory `owner-colorblind-delegate-visual-creative`): the read must be
      MOTION + SOUND + NUMBER, never a tint.**
- [ ] `FloatingHealthBar` appears on the FIRST hit, not up to 2.3 s later - the 2.0 s `Scan` +
      0.3 s `Evaluate` latency (`StructureDamageVisuals.cs:719-731, 951`) is the reason a struck wall
      can read as inert. Either pre-register walls at scene load or attach the bar from the flinch.
- [ ] Headless screenshot proof (`RunCaptureHeadless`) with the PNGs opened before it ships
      (memory `headless-screenshot-verify-ui-before-build`).

---

## 7. What NOT to touch

- **Anything shipped today and already committed: WO-1701 / 1703 / 1704 / 1708 / 1710 / 1711 / 1713 /
  1714.** Their files are off-limits unless the change is directly a wall-damage / wall-targeting /
  structure-feedback edit, and then say so explicitly in the RESULT.
- **`WallSegment.ApplyDamage`'s existing maths** - the tier divide (`:334`), the Faction-gated BULWARK
  reduction (`:343-344`) and the 0-100 clamp (`:346`). WO-853 section 9 and WO-1480 both landed here;
  the numbers are correct and measured in section 2 above. **Do not "rebalance" wall HP** - the data
  says 9-22 seconds per panel for one footman, which is not the problem.
- **`WallSegment.Collapse()`'s collider + NavMeshObstacle drop** (`:370-388`). That ordering (stop
  blocking BEFORE the event) is load-bearing for pathing through the breach.
- **Do not relayer a wall off `"Structure"`.** `WallSegment.cs:30-39` documents why: every tower
  line-of-sight linecast is masked to that layer, and the RaidSpire "move to the Enemy layer" trick
  would make towers shoot through walls. Widen target masks instead.
- **Do not strip or disable any `FlowTrace` / `Guard` call** (CLAUDE.md section 12, owner ruling
  2026-08-09). `troopai-idle-*` at `TroopController.cs:685` and `breach-focus` at `:1171` are the
  proving lines for 6B.
- **Do not add a second floating-text pool.** WO-953's one-pool ruling stands.
- **Do not hand-edit any `.unity` scene**, and do not re-run `RaidBaseGenerator` bakes for this work -
  the raid wall tiers are authored data (`scene-configs.json`) and are correct.
- **Do not touch `RepairTarget` / `WallRepairController` / `HubRepairAffordance`** - the repair loop is
  a separate pillar and `StructureDamageVisuals` registration depends on `RepairTarget.TryWrap`
  staying faction-blind.

---

## 8. Evidence index (every path opened by this lane, 2026-09-14)

| Claim | Source |
|---|---|
| wall damage model + single choke point | `Assets/_Modules/Village/Walls/WallSegment.cs:58,84,109,146,165-169,248-249,261,271,315,325-353` |
| collapse = the only terminal state | `WallSegment.cs:362-400,409-455` |
| tier enum + raid tier assignment | `Assets/_Modules/Village/Walls/WallTierData.cs:30-35`; `Assets/Editor/WallTools/RaidBaseGenerator.cs:505,522,1856` |
| troop damage / cooldown / struct mult | `Assets/Resources/Data/Canonical/troops.json`; `Assets/_Modules/Village/Troops/TroopController.cs:420-432,1370-1394` |
| only two world verbs; rally is a POINT | `Assets/_Modules/Village/Troops/RaidDeployController.cs:17,731-736,857-868`; `Assets/_Modules/Village/Troops/TroopRally.cs:15-35` |
| breach target picked scene-wide, override | `Assets/_Modules/Village/Troops/RaidAssaultAi.cs:185-219`; `Assets/_Modules/Village/Troops/TroopController.cs:203,685-694,707,954,961-970,1010-1019,1150-1175` |
| damage numbers exist, two callers, both Enemy | `Assets/_Modules/Village/Enemies/DamageNumberSpawner.cs`; `Assets/_Modules/Village/Enemies/Enemy.cs:2833,2838,2908` |
| walls ARE registered for tells; no faction filter | `Assets/_Modules/Village/Vfx/StructureDamageVisuals.cs:648-666,719-731,750,806-817,869,951-958`; `Assets/_Modules/Village/Walls/RepairTarget.cs:77-98` |
| per-hit dust: thresholds + mapped prefab | `Assets/_Modules/Village/Vfx/StructureHitReaction.cs:76,83,90,150-186`; `Assets/Editor/VFXCatalogGenerator.cs:271` |
| no structure-hit SFX exists | `Assets/_Modules/Audio/SfxId.cs:33-70` |
| raid scenes really are enemy-owned | `Assets/_Modules/Village/World/Camps/RaidGarrisonSpawner.cs:156` |

## Live confirmation, 2026-09-14 (owner mid-raid on device)

Owner was live-raiding on the Seeker while this ticket's RCA was landing. Screenshot:
`docs/handoffs/live_raid_wall_targeting_confirmation.png` - HUD shows SPIRE 100%, Razed 8%, standing at
a wall segment. Pulled logcat confirms the RCA's targeting finding in real play, not just source:

```
[Flow:TroopAI] id=troop-spearman role=melee ENGAGED foe='Wall_Keep1_SE_10(WallSegment)' kind=struct
dist=20.7m attackRange=3.5m inRange=False moved=0.01m/s commanded=4.0 agent=onNavMesh retargets=76
```

76 retargets and still out of range, not closing distance - live evidence of the thrashing this
ticket's targeting finding predicts. Every Breach-phase troop in the same log window shows
`has[unit=False,obj=False,wall=True]` - locked to "a wall" as a category, not the specific segment
the owner is standing at. `Razed 8%` climbing on-screen confirms FINDING 1 (damage calculation) is
healthy; the defect is isolated to FINDING 2 (targeting) as the RCA concluded.

# WO-1835 — Post-wave-20 endless escalation: real challenge content

**Status: READY FOR LEAD REVIEW**

## Owner ruling (verbatim, 2026-09-17)

> "after level 20, i want it to get difficult. THey need a real challenge so maybe 2 dragons
> spawn and troops breaking through walls, or healing caravans or mages using AoE large heal
> spells, rage spells"

> "catapults blasting the walls from the sides at the same time"

### Owner ADDITION, same day, relayed mid-implementation (2026-09-17)

> "their healers should cast AoE heal spells and regen"

> (asked whether the player's own healers should do the same) — **"so should mine"**

Two consequences, both folded into THIS ticket rather than split out, because they are the same
"healer" concept and splitting them would have shipped a mechanic only the enemy has:

1. **Regen is a SEPARATE ask from the heal cast.** Item 4 below now requires BOTH a telegraphed
   AoE heal burst AND a distinct passive heal-over-time aura. A single periodic burst satisfies
   "AoE heal spells" but not "and regen"; a continuous trickle satisfies neither, because it is
   not a spell the player can see coming and punish.
2. **NEW SCOPE — player-side parity (item 6).** The player's own healer gets the same pair.
   Measured at source before implementing, so this is not a guess about what already existed:
   - The player DOES already have a healer — `troop-field-cleric`, role `support`
     (`Assets/Resources/Data/Canonical/troops.json`), flagged at `TroopController.cs:485`
     (`_isSupport`) and driven from the AI tick at `:690` into `TroopController.TryHealSquadmate`
     (`:890-928`).
   - But that heal is **single-target** (a linear scan picking the ONE lowest-HP-ratio ally,
     `:895-903`, then `target.Heal(_attackDamage)` at `:917`), **troops-only** (it iterates the
     static `Active` roster and never references `HeroHealth` — a Field Cleric standing beside a
     dying hero has never once healed her), and **burst-only** (no regen of any kind).
   - The hero's own heals — `AbilityEffect.Heal` (`HeroAbilities.cs:1349-1382`),
     `ResolveHealOverTime` (`:2398-2410`), `HealFromDrain` (`:1871`) — are **all self-only** and
     land on `HeroHealth`; none of them touches `TroopController`.
   - There is **no friendly-unit spatial query anywhere in the project** (every player-side
     `Physics.OverlapSphere` is enemy-masked), so the ally scan reads the existing
     `TroopController.ActiveTroops` roster instead of inventing a friendly LayerMask.
   So this is genuinely new work, not a rename of something that shipped.

## Current state (confirmed at source — do not re-derive)

- `Assets/Resources/Data/Canonical/waves.json` `endless` block: past wave 20 the schedule
  cycles authored waves 4..20 forever (true wave 21 replays def 4, etc.), with
  `countGrowthPerWave: 0.05` / `countCap: 3.0` scaling MOB COUNT only (HP/speed/damage growth
  is `WaveScalingCurve`'s separate job, keyed off the true wave number so it keeps climbing past
  20 with no cap named here — read `WaveScalingCurve` before assuming a ceiling).
- Wave 20 is the single apex boss wave: one `DragonBoss` (`Boss_Dragon` prefab, "Syndrath the
  Devourer", 4200 HP) via `apexBoss` in `waves.json` + `WaveManager.SpawnApexBoss`. There is
  currently no path that spawns a SECOND simultaneous `DragonBoss`.
- `SmartEnemySpawner.SideCountForWave`/`ResolveSides` (see WO-1834, same session, proven
  correct and NOT to be touched by this ticket) already escalates 1→2→4 attacking sides and
  rotates N→E→S→W — this ticket's new pressure types ride on top of that, they don't replace it.
- Enemy families in `enemies.json` today: `hollow`, `orc`, `troll`. No wall-targeting melee, no
  support-heal caster, no siege/ranged-vs-structure unit exists yet.
- `CaravanHealField` / `HealingCaravanMobility` (`Assets/_Modules/Village/Buildings/`) is the
  PLAYER's own defensive healing caravan structure — unrelated to this ticket's "healing
  caravan" ask, which is a new ENEMY unit that heals other attacking enemies. Do not confuse the
  two or reuse that class; a new enemy-side component is needed (naming it something distinct,
  e.g. `EnemySupportCaravan`, avoids the collision).
- `IDamageableStructure` (`Assets/_Modules/Core/Combat/IDamageableStructure.cs`) is implemented
  by `WallSegment`, `Gate`, `Tower`, `Building`, `HeartController` — this is the interface any
  new wall-breacher/catapult behavior targets instead of the Heart/hero.

## Scope — five new pieces, all gated to trueWave > 20 (endless only, never the authored 1-20 schedule)

1. **Twin-dragon apex.** Past wave 20, on apex-boss cycle repeats (every cycle back through wave
   20's slot), spawn TWO `DragonBoss` instances simultaneously instead of one — from opposite
   sides if geometry allows (reuse the WO-1834-fixed 5-per-side fan once it's live). Scale each
   dragon's HP down modestly if a naive 2x total-dragon-HP reads as unfair in the first felt-test
   (flag this as a tunable, not a hard number — the owner felt-verifies).
2. **Wall-breacher melee.** A new enemy role/behavior that targets the nearest `WallSegment`
   (via `IDamageableStructure`) instead of pathing straight to the Heart — reads as "troops
   breaking through walls" rather than every enemy ignoring the wall entirely. Gate this to
   endless waves only; the authored 1-20 schedule keeps its current all-enemies-path-to-Heart
   behavior.
3. **Enemy healing-caravan support unit.** A new enemy that does not attack — it follows/stays
   near its own squad and heals nearby enemies over time (mirror the intent of the player's
   `CaravanHealField`, but as its own enemy-side component, never touching that class). Killing
   it should read as a clear tactical priority for the player.
4. **Support-mage AoE heal/rage caster.** A new enemy caster that periodically casts an AoE heal
   on nearby enemies and/or a "rage" buff (damage/attack-speed up) on nearby melee — visible
   telegraph before it fires, consistent with the project's existing telegraph pattern
   (`VfxPool.GetTelegraph`, used by `SpawnBatch`'s DEF-52 warning ring).
   **AMENDED by the owner's addition above: it must carry BOTH halves —** a telegraphed AoE heal
   burst AND a separate always-on regen (HoT) aura, plus the rage cast. Heal and rage alternate so
   a felt-test sees both rather than one forever.

6. **Player-side parity (NEW, owner "so should mine").** The player's support troop (the Field
   Cleric) gets an AoE heal burst plus a regen aura, covering **allied troops AND the hero** — the
   hero inclusion is the specific gap, since the shipped `TryHealSquadmate` never heals her. The
   existing single-target path is left exactly as it is and the new aura is additive, so nothing
   the owner has already felt-tested changes.
5. **Flanking catapults.** New ranged siege units that sit at range on the SIDES (not the front
   approach lane) and bombard wall segments directly (`IDamageableStructure` damage), timed to
   fire together so multiple wall faces take pressure at once — this is the "at the same time"
   part of the ruling; a single catapult on one side would not satisfy it.

## What NOT to touch

- The authored waves 1-20 schedule and its difficulty/pacing — this ticket is endless-mode-only.
- `SmartEnemySpawner`'s side escalation/rotation logic and `CastleSpawnPointInjector` (WO-1834
  territory, in flight in a separate lane this session — file-disjoint from this ticket).
- The player's own `CaravanHealField`/`HealingCaravanMobility`.

## Suggested split (keep file-disjoint if more than one lane is needed)

This is one coherent content pass; a single lane can likely take all five pieces since they
share the same new "endless pressure" surface, but if split: (a) wave-scheduling/trigger changes
(`WaveManager.cs`, `waves.json` endless block) for the twin-dragon trigger and catapult/caravan
release cadence, vs (b) new standalone enemy-behavior components (breacher targeting, support
caravan, support mage, catapult) as new files under `Assets/_Modules/Village/Enemies/`.

## Acceptance criteria

- [ ] Brace balance + NUL check pass on every touched/new `.cs`.
- [ ] `COMPILE_GATE_OK` + `REGRESSION_OK n/n` on a fresh log.
- [ ] New `Assets/Editor/Regression/` coverage for at least: the wall-breacher actually targets a
  `WallSegment` (not the Heart) when active, the catapult deals structure damage via
  `IDamageableStructure`, and the twin-dragon trigger only fires past true wave 20.
- [ ] FlowTrace instrumentation on every new behavior's key steps (per CLAUDE.md §12 — no silent
  failures on a brand-new system).
- [ ] Headless or device felt-test confirms all five read as intended before this closes — flag
  any numeric tuning (HP split for twin dragons, catapult damage-per-hit, caster cast interval)
  as a first-pass estimate for the owner to felt-correct, not a final balance number.

---

## Implementation record (lane hand-back 2026-09-17)

Stage: READY FOR LEAD REVIEW (the one authoritative `**Status:**` line is at the top of this file —
deliberately not repeated here, because `tools/board_build.py` regex-matches that token and a second
match is one edit away from becoming the one it reads).
Code written, brace + NUL checks green on every touched file.
**No Unity process was run by this lane** (no CompileGate, no DataRegression, no batchmode): the
lead gates the combined tree once, per the instruction that only the lead fires Unity while lanes
are open. So `COMPILE_GATE_OK` and `REGRESSION_OK n/n` are still OPEN on this ticket — they are
the lead's to obtain, and nothing here claims them.

### Files added
| Path | What |
|---|---|
| `Assets/_Modules/Village/Enemies/EndlessPressure.cs` | The escalation surface: pure static predicates (dragon count, breacher share, unit counts, volley index, flank score) + the pool-safe `Attach` seam + the shared wall-target cache |
| `Assets/_Modules/Village/Enemies/EnemyWallBreacher.cs` | Piece 2 — pins the nearest `WallSegment` over the Heart |
| `Assets/_Modules/Village/Enemies/EnemySupportCaravan.cs` | Piece 3 — non-combatant squad heal pulse |
| `Assets/_Modules/Village/Enemies/EnemySupportMage.cs` | Piece 4 — telegraphed AoE heal, alternating rage cast, separate regen aura |
| `Assets/_Modules/Village/Enemies/EnemySiegeCatapult.cs` | Piece 5 — flanking siege engine, synchronized volleys into `IDamageableStructure` |
| `Assets/_Modules/Village/Troops/AllySupportAura.cs` | Piece 6 — the player's Field Cleric gets AoE heal + regen, hero included; self-installing director so `TroopController` is untouched |
| `Assets/Editor/Regression/EndlessEscalationRegression.cs` | 9-case oracle, headless, no scene |

### Files edited
| Path | Change |
|---|---|
| `Assets/_Modules/Village/Waves/WaveManager.cs` | Twin-dragon spawn loop; `_liveApexBosses` list + `AnyApexBossAlive` so the clear gate survives twins; one `ReleaseApexBosses` teardown replacing three copy-pasted unsubscribe blocks; the endless-pressure release region; `pressureRole` threaded through `SpawnBatch`/`SpawnOne` |
| `Assets/_Modules/Village/Enemies/EnemyBrain.cs` | `SetStructureFocus`/`ClearStructureFocus` opt-in pin, consulted in `ChooseTarget` before the role switch, cleared in `ResetForPool` |
| `Assets/_Modules/Village/Enemies/Enemy.cs` | Transient rage multiplier applied at the damage sink (`DealStructureDamage`), cleared in `ClearPooledLatches` |
| `Assets/_Modules/Village/Waves/WaveData.cs` | Five endless tunables on `EndlessDef` + `ToPressureTuning()` |
| `Assets/Resources/Data/Canonical/waves.json` + `Assets/StreamingAssets/Data/Canonical/waves.json` | The five tunables in the `endless` block; both mirrors kept byte-identical (they were in sync before, and a drifted mirror is a latent bug) |
| `Assets/Editor/Regression/DataRegression.cs` | One `[endless-escalation]` registration line |

### Decisions the lead should sanity-check
- **No new `enemies.json` defs and no new art addresses (CLAUDE.md §16).** The caravan rides
  `hollow-acolyte`, the support mage `orc-shaman`, the catapult `ogre` — all defs with bundles
  already on R2. A new modelKey would be a new content-hashed bundle needing its own push, and a
  forgotten push ships as untextured capsules with no error on screen. Distinct bodies later are an
  `enemies.json` + R2 push ticket, not a change to this logic.
- **The breacher does not move itself.** `EnemyBrain` stays the one mover; the component only
  nominates the wall. `SmartEnemySpawner` and `CastleSpawnPointInjector` were not touched (WO-1834
  territory).
- **The player-side parity piece edits zero lines of `TroopController`** (it carries WO-1764 and
  WO-1830 this session) — a self-installing director reads the public `ActiveTroops` roster instead.
- **First-pass numbers, owner felt-tunes**, all labelled as such in code and authored in
  `waves.json` so she can re-tune without a rebuild: twin-dragon HP share 0.7 each, breacher share
  0.35, catapult 9 dmg per stone on a 6 s synchronized volley, mage cast every 9 s (1.2 s
  telegraph), enemy mage burst 22% / regen 1.2%/s, cleric burst 18% / regen 1%/s.
- **Counts are derived, not authored** — how many of each unit a wave fields comes from the pure
  predicates in `EndlessPressure`, so the escalation curve has one owner and cannot drift between
  the JSON and the code reading it.

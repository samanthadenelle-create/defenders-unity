# WORK ORDER 1773 — A high-level hero takes NO damage in town waves (wave 176 ≡ wave 60), plus the wave-AI strategy spec

**Status:** READY TO IMPLEMENT
**Minted:** 2026-09-16 (number PRE-ASSIGNED by the lead; this lane did NOT touch `CLI_LANES_WO_NUMBERS.md`)
**Silo:** Town wave balance — `DeNelle.Village/Waves` + `DeNelle.Village/World` (SafeZoneRecovery) + `DeNelle.Core.Ops` (remote tunables). Lane B (§8) is a SPEC only.
**Lane disjointness:** touches NO `.unity`, NO scene builder, NO art. File-disjoint from WO-1774 (art lane).
**Evidence:** external tester's Seeker recording `logs/device/owner-video/DefenderDemoRun.mp4` (2670x1200, 60 fps, 145.7 s, h264). 1 fps frames extracted to `logs/device/owner-video/frames_1s/s_001..s_146.jpg`; 20 s frames at `logs/device/owner-video/frames/f_001..f_007.jpg`; crops in `logs/device/owner-video/crops/`.

> ⚠ **BUILD UNDER TEST IS UNKNOWN.** The tester's `Application.version` has never been captured — it is
> still open owner item 7 in `docs/handoffs/SESSION_HANDOVER_2026-09-15_play_track_and_wall_fork.md:61`
> ("Ask Sminer for his `Application.version` line"). Every source citation below is read off `dev` at
> HEAD on 2026-09-16. **Nothing here is proven to be the code the tester ran.** That is a finding, not a
> box to tick (CLAUDE.md §11B).

---

## 0. The owner-relayed symptom, and the two things that are actually true

Owner relaying the external tester, 2026-09-16, verbatim:

> "at the level he is nothing can damage him he can just stand there"

Owner direction on the same day, verbatim:

> "enemies need to really start scaling with wave i thought, or massive swarms at all sides"
> "even better have the AI use strategies such as the dragon and have the AI breach a wall"
> "can we add logic to attack non traditional paths at a certain point?"

The arithmetic below names **two independent dead steps**. They are separable, and **fixing either one
alone does not close the ticket**:

- **DEAD STEP A — the hero out-regenerates the town wave, at every level, because the safe-zone regen has no combat gate.** `SafeZoneRecovery` restores **12% of MaxHp per second** while the hero stands anywhere within 60 m of the Heart. The entire walled town is inside that radius. It runs *during* a wave.
- **DEAD STEP B — enemy strength stops growing at wave 20 and enemy count stops growing at wave 60.** `WaveScalingCurve`'s keyframes end at x=20 with `postWrapMode = WrapMode.Clamp`, and the endless count multiplier clamps at `countCap 3.0`. **Wave 176 is numerically identical to wave 60.** Nothing in the town wave loop reads the hero's level at all.

---

## 1. Frame evidence — every claim, its frame, its timestamp

Frames are 1 fps, so `s_NNN.jpg` ≈ t = NNN−1 seconds.

| Claim | Frame | Timestamp | What is visible |
|---|---|---|---|
| Hero is **Thrain, a Mage**, abilities **Void Rift / Arcane Bolt / Wither**, bar faces CAST · BLOCK · Void Rift · Arcane Bolt · Wither · ITEM(3) | `frames/f_002.jpg` | ≈20 s | Name plate + action bar |
| Hero is **Lv 51** early, **Lv 52** later; level-up lands between | `frames/f_002.jpg` → `frames/f_004.jpg` | ≈20 s → ≈60 s | "Thrain Lv 51" → "Thrain Lv 52" |
| Level-up occurs at **t ≈ 41 s** | `crops/hpbar_tile.jpg` tile row 7 | 41 s | Last "Lv 51" tile, first "Lv 52" tile |
| **The wave is 176**, and **8 enemies remain** | `crops/slab_22.jpg` | 22 s | HUD banner "Wave 17… / 8 enemies re…" |
| **WAVE 176 CLEARED → PREPARE FOR WAVE 177** | `frames_1s/s_134.jpg` | ≈133 s | End-of-wave panel |
| **Enemies DO destroy walls**: "Wall Section - DESTROYED / Rebuild Wood 80" ×2, "Showing 4 of 8 results", "The realm holds - but it took damage." | `frames_1s/s_134.jpg` | ≈133 s | Wave-cleared panel body |
| A wall is at **Health 76% / Damage 24% / Repair cost wood 20 / Ready to repair** | `crops/wallinspect.jpg` | ≈131.5 s | Structure inspect panel behind the modal |
| Enemies observed: **Hollow Skirmisher 11/175**, **Ogre 58/700**, **Cave Troll 140/800**, **Orc Berserker 76/293** | `f_002.jpg`, `f_004.jpg`, `s_113.jpg`, `s_094.jpg` | 20 / 60 / 112 / 93 s | Target plate |
| XP popups: **+14449**, **+68608**, **+5285**, **+20 XP +10 gold**, **+8 XP +4 gold** | `f_004.jpg`, `s_113.jpg`, `s_094.jpg`, `crops/slab_22.jpg` | 60 / 112 / 93 / 22 s | Floating text |
| **A Cave Troll is in melee contact with the hero and the HP bar stays full** | `frames_1s/s_113.jpg` | ≈112 s | Troll body overlapping the hero; red bar filled |
| Damage numbers **29 / 10 / 19 / 40** are gold/cream — the hero's OUTGOING damage | `s_113.jpg`, `f_002.jpg` | 112 / 20 s | Floating combat text over enemies |

### 1a. The HP bar never drops — measured, not eyeballed

`crops/hpbar_tile.jpg` tiles the hero plate from all 146 one-second frames. A per-frame pixel scan of
the red bar's right edge (row y=58 in the 1335 px frames, scan window x∈[30,320]) returns **x = 263 in
133 of 146 frames**. The 13 deviants are VFX bloom and floating text washing the bar's right end — the
worst of them, `s_113.jpg`, was opened directly and shows the bar **filled, with a Cave Troll in melee
contact**. Four frames return no red at all: `s_001` (fade-in) and `s_132`–`s_134` (the wave-cleared
modal covers the plate).

**Honest statement of the finding:** the bar's right edge is *constant* for the whole run, and every
frame read directly shows the track filled. The bar's empty-track extent was never established, so this
is "never changed and reads full", **not** a measured "100.0%".

### 1b. Three things the frames deliberately do NOT prove

- ⛔ **Absence of incoming damage numbers proves nothing.** There is no hero-hit damage popup in the game. `DamageNumberSpawner.Spawn` has exactly two callers — `Assets/_Modules/Village/Enemies/Enemy.cs:2833,2838` (an *enemy* being hit) and `Assets/_Modules/Village/Vfx/StructureHitReaction.cs:350` (a *structure* being hit). `HeroHealth.TakeDamage` spawns no number. **The constant HP bar is the evidence; popup absence is not.**
- ⛔ **"Hostiles only on E/SE" is NOT provable from the compass.** `Assets/_Modules/HUD/Kit/HudCompassWidget.cs:72` is `private const float FovDegrees = 160f;` and `:418,:519` clamp markers to `±halfFov`. The strip is a **±80° forward fan**, not a 360° tape. Enemies behind the hero are simply not plotted (they become edge arrows, `:502`). Source says all four gates are used from wave 10 — see §4.
- ⛔ **The small dark chip carrying a white "2" (f_002, s_113) and "3" (s_094) is UNIDENTIFIED.** It is a world-space label sitting on the perimeter wall. No structure level-badge emitter was located. Left unproven deliberately.

### 1c. Is this Sminer, and does the wave counter restart?

`docs/handoffs/SESSION_HANDOVER_2026-09-15_play_track_and_wall_fork.md:39-40` records **one** external
player, "Sminer" (Discord, Germany, Google Play tester track), at **hero Lv 45, Wave 146** on 09-15. The
video shows **Lv 51→52, wave 176→177** on 09-16. That is +6–7 levels and +30 waves in a day — ordinary
single-player progression for the same save.

**The wave counter does NOT restart per session, and the source says so:**
- Persisted field: `Assets/_Modules/Core/State/GameState.cs:40` — `public int BestWave;`
- Writer: `Assets/_Modules/Core/State/GameStateService.cs:1119` — `_state.BestWave = Mathf.Max(_state.BestWave, waveReached);`
- Called from the wave loop: `Assets/_Modules/Village/Waves/WaveManager.cs:3466-3467` — `s_resumeWaveId = cleared + 1; GameStateService.Instance?.RecordRun(cleared);`
- Resume seed on load: `WaveManager.cs:1436-1445` — `int best = … State.BestWave; s_resumeWaveId = Mathf.Max(1, best + 1);`
- Wire key: `Assets/_Modules/Core/State/SaveSchema.cs:254` — `[JsonProperty("bestWave")] public double? BestWave;`
- No ceiling: `WaveManager.cs:3470-3471` — *"BestWave has no upper clamp — SaveSchema only floors it at 0"*.

⚠ **Caveat, unproven:** `SESSION_HANDOVER_2026-09-15…:39-40` also records that Sminer's level-45 save
is **NOT in Neon** (max hero level across 65 rows = 20). So this video cannot be tied to a server row.
The identification rests on "there is exactly one external player" plus consistent progression — it is
**strong circumstantial, not proven**. Proving it needs his `Application.version` and a device id.

---

## 2. The hero at Lv 52 — the arithmetic, from source

### 2a. Max HP has **no per-level term and no class multiplier**

`Assets/_Modules/Village/Hero/HeroHealth.cs`:
- `:39` — `[SerializeField] private float _maxHp = 100f;`
- `:183` — `public float MaxHp => _maxHp + EffectiveBonus;`
- `:181` — `private int EffectiveBonus => GearHpBonus + TalentHpBonus + CathedralMageHpBonus;`

`_maxHp` has exactly one assignment in the file (`:39`); there is no `SetMaxHp` and no `_maxHp` writer
anywhere under `Assets/`. The component is added at runtime (`HeroHealth.cs:2294`,
`HeroControlEnsurer.cs:637`), so the serialized default is the shipped base. Two independent regressions
pin it: `Assets/Editor/Regression/RaidArenaShapeRegression.cs:85` `private const float HeroBaseHp = 100f;`
and `Assets/Editor/WallTools/RaidBaseGenerator.cs:241`.

⚠ **Gear `req.level` tops out at 10** across all mage armour and accessories. **A Lv 51 hero has
identical gear eligibility to a Lv 10 hero.** And the outgoing damage reward caps too:
`Assets/_Modules/Village/Progression/HeroProgression.cs:100-101` with `:55 DamagePerLevel = 0.06f` and
`:56 MaxDamageMultiplier = 3f` → the cap is reached at **L35**. **Lv 51 and Lv 52 are indistinguishable
in every HP, mitigation and damage term found.** There is no hero level cap (`HeroProgression.cs:318-324`
is an unbounded `while`).

| Assumption (stated) | MaxHp at Lv 52 |
|---|---|
| Bare component, no gear, no talents | 100 |
| Best mage gear by `req.level` (legendary armour 90 + `ring_firstlight` 50 + `amulet_elarion` 50) | **290** |
| …plus every mage-reachable `maxHpPct` talent (+70, affordable: 143 Wisdom by L51 vs the 7 needed) | **360** |

`CathedralMageHpBonus` (`HeroHealth.cs:179-180`, ceiling `HeroTalentModifiers.cs:259 MaxMageHpBonusPct = 2f`)
is a real term whose value comes from live building state — **NOT INVESTIGATED, could only raise these numbers.**

### 2b. Mitigation chain (all multiplicative), `HeroHealth.TakeDamage` at `:731`

| # | Step | Line | Mage reachable? |
|---|---|---|---|
| 1 | dodge full-miss | `:752-758` | **No** — `dodge` is authored only on ranger nodes |
| 2 | perfect parry full-negate | `:763-768` | **No** — the parry window is opened only by `PlayerAttackController.cs:384-406`, which is `cls == "knight"` gated at `:386` |
| 3 | **gear armour** `amount *= (1f - _gear.ArmorDefense)` | **`:773-774`** | **Yes** |
| 4 | ability timed shield | `:788-798` | **No with this bar** — Void Rift (`aoe`) / Arcane Bolt (`strike`) / Wither (`dot`) carry no `shield` effect |
| 5 | held Block `amount *= 0.45f` | `:803-809`, const `:60` | **No** — `HudKitCommandBridge.cs:140-153` returns at `:152` for a mage before reaching `:160 health.SetBlocking(held)` |
| 6 | talent full-block roll | `:824-830` | **No** — `blockChance` is knight-only |
| 7 | talent DR, clamped 0.95 | `:831-835` | **Yes** — mage-reachable sum = 0.30 (`shared.n2` Resilience 0.20 + `shared.n8` 0.10) |

**Answer to "whether Block is used": the video shows a BLOCK face on the bar (f_002, s_094, s_113), but
for a Mage that face does not block.** `HudKitCommandBridge.cs:144-145` says so in comment: *"A Mage has
no physical shield: pointer-down casts the authored Arcane Shell."* With Arcane Shell hot-swapped out of
the bar, step 4 is identity too. **Thrain has no active mitigation at all — only passive gear + talents.**

| Assumption | ArmorDefense | Talent DR | Incoming multiplier | Mitigation |
|---|---|---|---|---|
| Conservative: legendary body armour only, never improved, no defensive accessories | 0.28 | 0 | ×0.72 | 28% |
| Plausible: legendary armour at gear level 5 + `ring_heartward` + `amulet_heartstone` + epic off-hand shield, all defensive talents | 0.6541 | 0.30 | ×0.2421 | **75.8%** |

Caps: `GearLoadout.cs:197 MaxArmorDefense = 0.90f`; `HeroTalentModifiers.cs:57 MaxIncomingReduction = 0.85f`.
**No damage floor exists on the realtime hero path** — `HeroHealth.cs:744` ignores non-positive damage, `:837`
floors HP not damage. (A `Math.Max(1, …)` floor exists only in the unrelated ATB engine, `BattleATB/Engine/Combat.cs:81`.)

---

## 3. What a wave-176 enemy actually deals — and why 176 ≡ 60

### 3a. Strength: the curve is CLAMPED at wave 20

`Assets/_Modules/Village/Waves/WaveScalingCurve.cs`:
```
DefaultHp()     : Keyframe(0, 1.0), Keyframe(20, 2.5)   // :68-71
DefaultSpeed()  : Keyframe(0, 1.0), Keyframe(20, 1.4)   // :89-92  (speed block)
DefaultDamage() : Keyframe(0, 1.0), Keyframe(20, 2.0)
c.preWrapMode = WrapMode.Clamp; c.postWrapMode = WrapMode.Clamp;   // on all three
```
**`postWrapMode = WrapMode.Clamp` is the ceiling.** Wave 176 evaluates exactly as wave 20:
**HP ×2.5, damage ×2.0, speed ×1.4.**

No tuned curve asset overrides it: `Assets/Scenes/Main_Castle_Overworld.unity:4129` is
`_scalingCurve: {fileID: 0}`, and `grep -rl WaveScalingCurve Assets --include=*.asset` returns nothing —
**the runtime default IS what ships.**

Applied at `WaveManager.cs:2536-2539` (smart path, the live one) consuming `_currentWaveId`; consumed on
the enemy at `Enemy.cs:965-984` (`_maxHp *= hpMult`, `_contactDamage *= damageMult`).

**The video confirms the clamp empirically, to the digit.** Authored base HP in
`Assets/Resources/Data/Canonical/enemies.json`:

| Enemy | id / line | base hp | ×2.5 | observed in video |
|---|---|---|---|---|
| Hollow Skirmisher | `hollow-rogue` `:71`, hp `:79` | 70 | **175** | **11/175** (f_002) |
| Ogre | `:394`, hp `:402` | 280 | **700** | **58/700** (f_004) |
| Cave Troll | `troll` `:311`, hp `:319` | 320 | **800** | **140/800** (s_113) |

Three independent exact matches. The clamp is not a reading — it is measured off the tester's screen.

⚠ **Caveat on mid-range waves:** Unity's two-keyframe `AnimationCurve` is **not linear** — the default
tangents produce an ease-in/ease-out S-curve, so an intermediate wave scales **slower** than a straight
line. Wave-176 numbers are exact (clamped); do **not** quote a precise wave-3 or wave-10 multiplier from
this file without evaluating the curve.

**Per-hit contact damage at wave 176** (`contactDamage` × 2.0):

| Enemy | base contactDamage | at wave 176 |
|---|---|---|
| Hollow Skirmisher | `enemies.json:81` = 5 | **10** |
| Ogre | `:404` = 12 | **24** |
| Cave Troll | `:321` = 14 | **28** |
| Necromancer (the cycled boss) | `:221` = 17 | **34** |

How it lands — `HeroHealth.cs:588-602`, constants `:45-49`:
`EngageRadius = 1.5f`, `DamageInterval = 1.0f`, `MaxEnemiesPerTick = 4`, `DamagePerEnemy = 6f` (fallback only).
Up to **four** attackers inside 1.5 m have their `ContactDamage` **summed** into one `TakeDamage` call per
second. (`attackInterval` in enemies.json does **not** pace hero damage; HeroHealth's own 1.0 s tick does.)

### 3b. Count: capped twice, binding by wave 60

`Assets/_Modules/Village/Waves/WaveCompositionBuilder.cs:149-152`:
```
BaseCount = 4;  CountPerWave = 0.9f;  MaxCount = 22;  EliteEveryNth = 5;
```
`:181-183` — `total = Mathf.Clamp(RoundToInt(4 + 0.9f*(waveId-1)), 4, 22)` → **22 from wave 21 onward.**

Endless multiplier, `WaveManager.cs:1802-1813` reading `waves.json:19-20`
(`countGrowthPerWave: 0.05`, `countCap: 3.0`):
`1 + 0.05*(176-20) = 8.8 → Mathf.Min(8.8, 3.0) = 3.0`. **The cap binds from wave 60** (`1+0.05*40 = 3.0`).

→ **wave 176 roster = 22 × 3 = 66 ground bodies + 1 cycled necromancer boss.**

Concurrency: `WaveManager.cs:296-302` `_maxSimultaneousEnemies`, scene value
`Main_Castle_Overworld.unity:4158` = **8**. It does **not** thin the wave —
`WaveManager.cs:2420-2426`: *"THE CAP HOLDS COUNT CONSTANT, IT DOES NOT THIN THE WAVE"*; the overflow is
held and drained as reinforcements. **This is why the banner read "8 enemies remain" (`crops/slab_22.jpg`)
while 67 bodies were queued.**

### 3c. Roster identity at 176

`WaveManager.cs:1783-1794` cycles authored defs past the last wave:
`cycleStart = EndlessCycleStartWaveId = 4` (`:486`), `max = 20`, so
`sourceWaveId = 4 + (176-20-1) % 17 = 4 + 155%17 = 4 + 2 = 6` → def 6 (`waves.json:62`, boss `necromancer`
at `:66`, **no `bossHp` pin**, so the boss also rides the ×2.5 curve: 1700 → 4250).

But the **roster** is built from the TRUE wave number, not the cycled def —
`WaveManager.cs:2386-2387` passes `waveId`, and tier split `WaveCompositionBuilder.cs:196-205`
gives `strongFrac = min(0.4, 0.12 + 0.03*(176-6)) = 0.4` (**caps at wave 16**) →
weak 4 / medium 9 / strong 9 of the 22, drawn from the wave-≥6 brute pool
(`WaveCompositionBuilder.cs:332-346`: medium-brute, orc, **troll**, **ogre**) — exactly the enemies in the video.

### 3d. Does ANY town-wave enemy stat read the HERO's level? **No.**

`grep -n "HeroLevel|PlayerLevel|playerLevel|HeroProgression.Level"` over `Assets/_Modules/Village/Waves/`,
`Enemies/Enemy.cs` and `Core/Difficulty/` → **zero matches**. The only two multipliers on the town path are
`WaveScalingCurve` (wave number only) and `DynamicDifficulty` (`WaveManager.cs:2544-2545`), whose input is
rolling encounter telemetry, not hero level (`Core/Difficulty/DynamicDifficultyState.cs:36-45,:192-195`).
Hero level feeds enemy scaling **only on the raid path** (`GarrisonStatBlocks.ApplyLevelScale`, called from
`GarrisonController.cs:304` and `RaidGarrisonSpawner.cs:320,392` — not reachable from `WaveManager`).

**Also: "enemy level" is not a system.** `Enemy.cs:726` is
`_level = Mathf.Max(1, RoundToInt(Mathf.Max(1f, def.Hp) / 25f))` — derived from the **unscaled** catalog HP,
with no wave reference; `Enemy.cs:565-570` says so verbatim (*"There is NO authored level field on EnemyDef"*),
and WO-1232 removed it from the player-facing HUD (`Assets/Editor/Regression/WO1232EnemyLevelSourceRegression.cs`).
**So "enemy level floor = hero level − N" (fix option (c)) has no existing level field to write into on the
town path — it would have to be built.** That is the single most important thing to know before choosing.

### 3e. `ZoneManager.ThreatLevel` is a **red herring** for this ticket

`Assets/Tests/EditMode/ZoneManagerCastleSafeTests.cs:82-88` does assert
`ThreatLevel((0,0,40)) == 0` with *"Village (safe home zone) never scales enemy level"*, and
`ZoneManager.cs:148-156` returns 0 for tier ≤ 0. **But the wave loop never calls it.** The eight
non-test callers of `ZoneManager.ThreatLevel(` are `OverworldEncounterSpawner.cs:548`,
`TutorialFlow.cs:2310`, `CampSystem.cs:157`, `RaidOutpostSystem.cs:218`, `GateIntelHud.cs:190`,
`RegionMobSpawner.cs:322`, `TribeManager.cs:175`, `WardTetherService.cs:334` — **zero** in
`WaveManager.cs`, `SmartEnemySpawner.cs`, `WaveCompositionBuilder.cs` or `Enemy.Configure`.
A wave-176 enemy spawned at a castle gate inside the "safe" box still gets ×2.5 HP and ×2.0 damage.
**Do not change ZoneManager for this ticket.**

---

## 4. Spawn-side coverage at wave 176 — already all four gates

`Assets/_Modules/Village/Waves/SmartEnemySpawner.cs:509,512,519-525`:
```
TwoSideFromWave = 5;  FourSideFromWave = 10;
SideCountForWave(w) => w >= 10 ? 4 : w >= 5 ? 2 : 1;
```
→ `SideCountForWave(176) = 4`. It **stops varying with the wave number at wave 10.**

Selection, `SmartEnemySpawner.cs:573-606`: `sideIds` = distinct `GateIndex` values, sorted;
`start = (waveId-1) % sideIds.Count` (=3 at wave 176, i.e. west first), `step = 4/4 = 1` →
**all four gates every wave ≥ 10**, with only the rotation offset varying.
Gate indices come from `CastleSpawnPointInjector.cs:80-86` (south 2, west 3, north 0, east 1),
markers named at `:156` `spawn-castle-{dir}-{i}`.
Budget is split across sides by `PartitionCount` (`:530`) **inside one `SpawnWave` call**, so N sides can
never exceed the one-side concurrency cap (the WO-1113 phone frame-rate cliff).

⛔ **So "collapse to one side" is NOT the defect.** The owner's "massive swarms at all sides" is already
structurally true; what is missing is *volume* (§3b caps) and *simultaneity within the 8-alive budget*.

---

## 5. THE ARITHMETIC — the dead step, named

### 5a. DEAD STEP A — town regen has no combat gate, and scales with MaxHp so it can never be outgrown

`Assets/_Modules/Village/World/SafeZoneRecovery.cs`:
- `:52` — `private const float TownRegenFractionPerSecond = 0.12f; // ~8s empty->full`
- `:53` — `private const float TownRadius = 60f;`
- `:100-133 Update()` — gates on **hub scene** + **horizontal distance to origin ≤ 60 m** and nothing else. There is **no combat check, no recent-damage check, no wave-active check.**
- `:127` — `float amount = health.MaxHp * TownRegenFractionPerSecond * Time.deltaTime;` → `:130 health.RegenTick(amount);`
- Talent multiplier at `HeroHealth.cs:2049-2056` — mage-reachable `shared.n7` Swift Recovery `healthRegen 0.5` → **×1.5**.

**The whole walled town is inside the ring.** `Assets/_Modules/Village/Walls/WallLayout.cs:126,129` —
`WallHalfX = 28f`, `WallHalfZ = 21f`. The furthest corner of the wall ring is
`sqrt(28² + 21²) = 35 m` from the Heart at origin — **comfortably inside the 60 m radius, wall line
included.** A hero standing at the gate fighting the wave is regenerating the entire time.

**Regen per second, by MaxHp assumption:**

| MaxHp | regen (0.12/s) | with Swift Recovery (×1.5) |
|---|---|---|
| 100 (bare) | 12.0 HP/s | 18.0 HP/s |
| 290 (geared Lv 52) | **34.8 HP/s** | **52.2 HP/s** |
| 360 (geared + specced) | **43.2 HP/s** | **64.8 HP/s** |

**Incoming per second at wave 176, after mitigation** (contact damage from §3a, summed over up to 4
attackers per `HeroHealth.cs:588-602`, then ×the §2b multiplier):

| Attackers in the 1.5 m radius | raw/s | ×0.72 (conservative gear) | ×0.2421 (plausible gear+talents) |
|---|---|---|---|
| 1 Hollow Skirmisher | 10 | 7.2 | 2.4 |
| 1 Ogre | 24 | 17.3 | 5.8 |
| 1 Cave Troll | 28 | 20.2 | 6.8 |
| 2 Cave Trolls | 56 | 40.3 | 13.6 |
| **4 Cave Trolls (engine maximum)** | **112** | **80.6** | **27.1** |

**The comparison:**

- **Plausible build (MaxHp 290, ×0.2421):** even **four Cave Trolls at once — the engine's hard ceiling —
  deal 27.1 HP/s against 34.8 HP/s of regen. NET +7.7 HP/s.** The hero **cannot be moved off full HP by
  anything a town wave can field, at any wave number.** This is the tester's sentence, in arithmetic.
- **Conservative build (MaxHp 290, ×0.72):** one Ogre = 17.3 HP/s vs 34.8 regen → **net +17.5**. Only a
  **3–4 heavy simultaneous melee pile** (60.4–80.6 HP/s) exceeds regen — and with
  `_maxSimultaneousEnemies: 8` split across **four gates** (§4), four heavies converging inside a 1.5 m
  radius is rare. The video is 146 s of exactly that: HP pinned full with 1–2 in contact.
- **The regen scales with MaxHp**, so gearing up raises *both* sides — but incoming is capped by the
  wave clamp while regen is not. **The gap widens with every piece of gear. It can never be outgrown.**

**Contrast at Lv 4** (the new-player case, wave ~3):
MaxHp ≈ 157 (`armor_mage_uncommon` 22 + `ring_steadfast` 35), mitigation ≈ 12% (×0.88), regen = 0.12 × 157
= **18.8 HP/s**. Wave-3 skirmishers at ~5.3 contact damage each (the curve is an S-curve here, §3a
caveat — treat this as approximate), four of them = 21.2 raw → **18.7 HP/s after mitigation.**
**A dead heat with regen even at Lv 4.** The town is close to unlosable for the *hero* at every level;
Lv 52 merely turns "close" into "never".

**And that is consistent with what the video shows the wave DOES kill: the walls.**
`s_134.jpg` reads "Wall Section - DESTROYED" ×2 of "8 results". `WallSegment.cs:355`
`ApplyContactDamage(amount) => ApplyDamage(amount, "contact")` — doc at `:346-353`: *"enemies can wear
walls down instead of pathing straight through"*. **The town fight is already enemies-vs-walls. The hero
is a spectator with a health cheat.**

### 5b. DEAD STEP B — nothing gets harder after wave 60

| Axis | Formula | Binds at | Value at wave 176 |
|---|---|---|---|
| Enemy HP | `WaveScalingCurve` keyframe (20, 2.5), `postWrapMode = Clamp` | wave **20** | ×2.5 |
| Enemy damage | keyframe (20, 2.0), `postWrapMode = Clamp` | wave **20** | ×2.0 |
| Enemy speed | keyframe (20, 1.4), `postWrapMode = Clamp` | wave **20** | ×1.4 |
| Roster size | `MaxCount = 22` | wave **21** | 22 |
| Endless multiplier | `countCap 3.0` vs `0.05`/wave | wave **60** | ×3.0 |
| Strong-tier fraction | `Mathf.Min(0.4f, 0.12f + 0.03f*(w-6))` | wave **16** | 0.4 |
| Sides | `SideCountForWave` | wave **10** | 4 |
| Concurrency | `_maxSimultaneousEnemies` (serialized) | never varies | 8 |

**Wave 176 is numerically identical to wave 60 on every axis**, except the 17-wave def cycle (which boss
id rides along) and the four-gate rotation offset.

### 5c. The one thing that IS still scaling at wave 176 — and it is the dragon

`WaveManager.cs:488-521`:
```
FirstDragonWave = 20;  DragonWaveInterval = 5;
DragonHpGrowthPerReturn = 0.25f;  DragonDamageGrowthPerReturn = 0.12f;
DragonCadenceGainPerReturn = 0.05f;  DragonMinimumIntervalMultiplier = 0.65f;
IsRecurringDragonWave(w) => w >= 20 && (w - 20) % 5 == 0;
DragonHpForWave(baseHp, w) => Mathf.Max(1f, baseHp) * (1f + 0.25f * tier);      // :504-508, UNCLAMPED
DragonDamageMultiplierForWave(w) => 1f + 0.12f * tier;                          // :511-515, UNCLAMPED
```
Wave **175** IS a dragon wave (`(175-20) % 5 == 0`), tier = `(175-20)/5 = 31`:
**HP = 4200 × (1 + 0.25×31) = 4200 × 8.75 = 36,750** and **damage ×4.72**.
Wave 176 is not (`156 % 5 = 1`), which is why the video shows none
(`WaveManager.cs:2159` strips the cycled wave-20 apex on non-cadence waves).

⛔ **Note the asymmetry and put it in front of the owner:** the dragon's growth is **linear and
uncapped**, while the entire regular roster is **clamped at wave 20/60**. The tester has been fighting a
36,750-HP dragon every fifth wave and a frozen wave-20 roster in between. **The pattern the owner wants
already exists — it was only ever applied to the dragon.** Fix option (a) below is, in substance,
"apply the dragon's own growth shape to the rest of the wave."

---

## 6. FIX MENU — design-neutral, cheapest first. **The owner rules; this lane does not pick.**

> **Every option is scoped to the TOWN wave loop.** None touches `ZoneManager` (§3e), the raid path, or
> `.unity` scenes.

### Option 0 (PREREQUISITE, not optional) — gate the town regen on combat

Without this, **every other option below is neutralised by §5a arithmetic.** A wave-176 roster ten times
heavier still cannot move the bar if 12%/s of MaxHp is flowing back in.

- Seam: `SafeZoneRecovery.Update()` (`:100-133`). One extra condition.
- Shapes (owner picks): (i) suppress while a wave is ACTIVE; (ii) suppress for N seconds after the last
  `HeroHealth.TakeDamage`; (iii) reduce the fraction during a wave rather than zeroing it.
- ⚠ **What this must NOT break:** the regen exists because of a real bug —
  `SafeZoneRecovery.cs:14-22` records that the FTUE floors the hero at 1 HP and `OnSceneLoaded` never
  re-fires, leaving no recovery path. **The FTUE case and the between-waves top-up must survive.**
  Shape (ii) preserves both naturally; shape (i) needs an explicit FTUE carve-out.
- Cost: one condition + one regression case. **Cheapest change in this document, largest effect.**

### Option (a) — a remote-tunable town-wave difficulty curve (the owner's "really start scaling with wave")

Replace the clamp with a **remote-tunable band table**, reusing the WO-1763 rail end-to-end.
Read `WorkOrders/WORK_ORDER_1763_raid_difficulty_remote_tunables.md` for the pattern: the rail is
`api/client-tunables.js` + the Neon `client_tunables` table
(`api/migrations/20260902_0018_client_tunables.sql`) + `RemoteTunables.Registry`
(`Assets/_Modules/Core/Ops/RemoteTunables.cs:414-725`). WO-1763 put `raid.*` difficulty on it; this
adds `wave.*`. **Nothing is invented; the rail already ships.**

Proposed keys (names are a proposal, the owner may rename):
`wave.hpMultAtWave20`, `wave.hpMultPerWaveAfter20`, `wave.dmgMultAtWave20`, `wave.dmgMultPerWaveAfter20`,
`wave.countCap`, `wave.countGrowthPerWave`, `wave.maxSimultaneous`, `wave.strongFracCap`.

Arithmetic, so the owner can see the shape before choosing a number. Post-20 linear growth
`mult(w) = clampValue + perWave × (w − 20)`:

| `wave.dmgMultPerWaveAfter20` | damage mult at wave 176 | Cave Troll hit | 1 troll vs 34.8 HP/s regen (×0.2421 mitigation) |
|---|---|---|---|
| 0 (today) | 2.0 | 28 | 6.8 HP/s — **net +28 heal** |
| 0.01 | 3.56 | 49.8 | 12.1 HP/s — still net heal |
| 0.05 | 9.8 | 137 | 33.2 HP/s — **parity with regen** |
| 0.10 | 17.6 | 246 | 59.6 HP/s — hero must move, block, retreat |
| 0.25 (the dragon's own rate) | 41.0 | 574 | 139 HP/s — one troll kills a 290-HP hero in ~2 s |

⚠ **This table assumes Option 0 shipped.** Without it, every row up to 0.05 reads "net heal".
⚠ **HP and damage must be tunable separately.** Scaling HP alone lengthens fights without adding threat;
scaling damage alone makes enemies fragile glass cannons. The owner will want different curves.

### Option (b) — count/swarm scaling and true four-side simultaneity (the owner's "massive swarms at all sides")

**The side ladder already delivers all four gates from wave 10 (§4) — do not re-implement it.** What is
capped is *volume*:
- `WaveCompositionBuilder.cs:151 MaxCount = 22`
- `waves.json:20 countCap 3.0`
- `Main_Castle_Overworld.unity:4158 _maxSimultaneousEnemies: 8`

| Lever | today | at wave 176 today | if raised (illustrative) |
|---|---|---|---|
| roster total | 22 × 3.0 = 66 | 66 + boss | `countCap 8.0` → 176 bodies |
| on screen at once | 8 | 8 (2 per gate) | 24 → 6 per gate |

⛔ **`_maxSimultaneousEnemies` is a PHONE FRAME-BUDGET knob, not a difficulty knob.**
`SmartEnemySpawner.cs:25-31` records that `PartitionCount` exists specifically so N sides can never
exceed the one-side cap — the WO-1113 phone frame-rate cliff. **Raising it is the one option in this
document that can regress the Seeker build's frame rate, and it must be measured with
`FlowTrace.Measure("Perf", …, 4f, 1f)` (CLAUDE.md §12) before and after.** If the owner wants "swarms",
the cheap half is a bigger *roster* (reinforcements arrive faster and never stop); the expensive half is
more bodies *on screen*.

### Option (c) — enemy level floor = hero level − N inside the walls

⛔ **This option is NOT cheap, and §3d is why.** There is no enemy level system on the town path to floor.
`Enemy.cs:726` derives a display-only number from unscaled catalog HP. Implementing (c) means either
(i) porting `GarrisonStatBlocks.ApplyLevelScale` (`GarrisonStatBlocks.cs:110-127`, `hpScale = 1 + 0.08×over`,
`dmgScale = 1 + 0.04×min(over,10) + 0.02×min(max(0,over-10),10)`) onto the wave path and giving
`WaveManager` a hero-level read it has never had, or (ii) inventing a second scaling authority beside
`WaveScalingCurve` — **two producers of the same multiplier, the duplicated-state failure CLAUDE.md §2/§5/§16
each describe.** Option (a) reaches the same felt outcome through the authority that already exists.

⚠ **Design consequence to weigh:** (c) couples difficulty to the hero, so a *new* hero joining a wave-176
town faces a wave-4 fight. (a) couples it to the wave, so the same new hero faces a wave-176 fight.
**These are different games. This is a ruling, not a technical choice.**

### Option (d) — wave-number pacing that follows the save's best wave rather than restarting at 1

**NOT APPLICABLE — this already works.** §1c proves `BestWave` persists with no clamp and the loop
resumes at `best + 1`. The tester's counter did not restart. **Do not implement this.** It is recorded
here only so the question is closed and nobody re-opens it.

---

## 7. What to CAPTURE if the owner wants this proven on the tester's device before any change

The instrumentation already exists — none of this needs new code.

| Question | Log line to grep | Source |
|---|---|---|
| Is the hero being hit **at all**? | `[Flow:HeroHealth] TakeDamage id=… amount=… hpBefore=…/…` | `HeroHealth.cs:741-743` |
| Is the town regen the reason he never drops? | `[Flow:SafeZone] town regen +X -> Y/Z (inside town/castle footprint…)` | `SafeZoneRecovery.cs:131-133` |
| What wave, and what roster? | `[Flow:Waves]` / `[Flow:Wave]` around `StartWave` | `WaveManager.cs` |
| Are all four gates used? | `[Flow:*]` from `SmartEnemySpawner.ResolveSides` | `SmartEnemySpawner.cs:563-606` |
| Is the authored schedule being ignored? | `[Flow:Wave] authored waves.json batches IGNORED: …` (once per session) | `WaveManager.cs:2209-2215` |
| **Who is chewing the walls** — enemy or player? | `[Flow:WallSegment] WallSegment '…' took {raw} raw -> {effective} effective ({via}, tier …, bulwark …)`. **`via` is the discriminator: `"contact"` = enemy, `"attack"` = player/troop/pet.** | `WallSegment.cs:411-415`; seams `:355` vs `:311` |
| **Did a collapsed wall actually open a path?** | `[Flow:WallSegment] … COLLAPSED: {colliders} solid collider(s) and {obstacles} carving obstacle(s) dropped` — **`obstacles = 0` means the gap did NOT open** (the barrier lives on a sibling, §8b(d)) | `WallSegment.cs:455-457` |
| The wave-cleared damage panel's own rows | `[Flow:WaveClear]` | `Assets/_Modules/Village/Waves/WaveDamageReport.cs:150`; `Collect()` at `:79` |

**The decisive pair is the first two, adjacent in time.** `TakeDamage amount=24 hpBefore=290/290`
immediately followed by `town regen +34.8 -> 290/290` is Dead Step A on one screen.

**If those two lines appear together, this ticket is proven end-to-end on the real device and Option 0
is confirmed as the prerequisite. If `TakeDamage` never appears at all, the defect is targeting, not
balance, and this WO must be re-scoped** — `EnemyBrain.ChooseTarget` weights the hero at roleValue 0.7 /
threat 0.9 vs a generic structure at 0.3 / 0.3 (`EnemyBrain.cs:1665,:1703`), so the hero should be
preferred; but that has **not been proven on the tester's build**.

---

## 8. § WAVE AI STRATEGIES — **NEW FEATURE. Status: SPEC. Do NOT RCA-fix the unbuilt.**

Classified per CLAUDE.md §13: everything in this section is **NOT BUILT**. It is specced here so the
owner can rule; **no part of §8 is authorised by the READY status at the top of this file**, which covers
§6 only.

### 8a. Inventory — what exists today, so the spec does not re-build it

| Capability | Exists? | Authority |
|---|---|---|
| **Dragon boss, full flight AI** | ✅ **YES, and far more than expected** | `Assets/_Modules/Village/Enemies/DragonBoss.cs` (1935 lines). `enum DragonState` `:120` — Approaching → Landing → **BurnTowers** → AirAttack → **RetargetTree** → Finale → Death. Owner intent in the header `:31-42`: *"flies into town, lands, and uses fire attacks to burn towers. After all towers are destroyed then targets the tree of life."* Kinematic flight, **no NavMeshAgent** (`:26-29`) — **it already ignores walls entirely.** |
| **An enemy-side "choose a strategy" selector** | ✅ **YES — exactly one, and it is the dragon's** | `DragonBoss.DecideAttackMode` (`:118`, `:707`) with `enum DragonAttackMode {Air, Land}` (`:138`). Header `:44-52` says it deliberately mirrors `EnemyBrain.UpdateTacticalState` / `ArchetypeDefaultState`. **This is the pattern precedent to copy, not a new invention.** |
| **Dragon recurrence + growth** | ✅ YES, uncapped | `WaveManager.cs:488-521` (§5c). Every 5th wave from 20; HP +25%/return, damage +12%/return, cadence −5%/return floored at 0.65. |
| Dragon art | ✅ | Addressable `Enemies/Boss_Dragon`, label `enemyfam-bosses`, `Assets/AddressableAssetsData/AssetGroups/Enemy_Models.asset:133-136`. ⚠ The `Resources/Enemies/Boss_Dragon` fallback at `WaveManager.cs:2729-2730` is **dead** — that folder holds no dragon. §16 applies: the bundle must be pushed. |
| Boss-every-Nth-wave as CODE | ❌ **Only the dragon's cadence is code.** | `grep "BOSS_EVERY|BossEvery|IsBossWave"` in `WaveManager.cs` → **zero**. Ground bosses are authored data only (`waves.json:57-58,:66,:104,:142`); the "every 6 waves" rule lives in a prose `_schemaNotes` comment at `waves.json:12`. |
| Enemies can damage a **WallSegment** | ✅ YES | `WallSegment.cs:355` `ApplyContactDamage → ApplyDamage(amount,"contact")`, doc `:346-353`. Damage funnel `:366`; tier toughness divide `:390`; collapse at `:418` when damage ≥ 100 (`MaxHp = 100f`, `:143`, inverted accumulator). Faction check passes: town wall is `Friendly` (`:288-289`), attacker `Hostile`, `CombatFactionRules.cs:104-108` → true. **Confirmed on the tester's screen** (`s_134.jpg`). |
| Enemies can damage a **Gate** | ✅ YES, incidentally | `Assets/_Modules/Village/Gates/Gate.cs:68` implements `IDamageableStructure`. **Nothing targets a gate deliberately.** |
| **Anything picks the WEAKEST wall** | ❌ **DOES NOT EXIST on the enemy side** | Both structure scorers pass `hpFraction` **hardcoded `1f`** — `EnemyBrain.cs:1703` (generic structure, roleValue **0.3**, threat 0.3) and `:1682` (tower) — so the `LowHpWeight` term is **inert for every structure**. `Enemy.SweepForNearestStructure` (`Enemy.cs:2700-2703`) scores by **distance only**. A damaged wall is worth exactly as much as a pristine one. |
| **How walls actually get chewed today** | ⚠ **By ACCIDENT, not by targeting** | The dominant seam is the **undirected forward contact probe**: `Enemy.ProbeForStructureForward()` (`Enemy.cs:2598`) SphereCasts forward with layer mask **`~0` (all layers)** at `_contactProbeDistance = 1.1f` (`:155-156`) — **any enemy whose nose ends up against a wall acquires it with no decision at all**, then `ExecuteContactAttack` (`:2145`) → `DealStructureDamage` (`:2226`) → `ApplyContactDamage` (`:2256`/`:2273`). The brain arm (`EnemyBrain.cs:1701-1705`, roleValue 0.3) only fires when nothing better is in scan range. **Ruled out as causes:** the wave-end report only READS (`DefenseReportBuilder.cs:19-23`), and no town AoE/burn reaches a wall (`StructureBurn.cs:248` is ignited only by `DragonBoss.cs:1017`). **The player and her own troops cannot damage her own walls** — both reject `Faction != Hostile`, and a town wall is `Friendly`. |
| **Anything seeks or re-paths to a breach** | ❌ **DOES NOT EXIST** | A wall only becomes passable *incidentally*: `WallSegment.Collapse()` drops its `NavMeshObstacle` (`WallSegment.cs:45` using-comment; trace `:455`). Nothing on the enemy side seeks a breach, prefers one, or re-paths to it. |
| ⚠ **An aspirational doc that lies** | — | `Assets/_Modules/Village/Waves/WaveData.cs:47` documents `EnemyAiKind.Skirmisher` as *"Fast; peels off the line to strike the weakest wall / gate."* **That behaviour is not implemented.** The only consumer of `EnemyAiKind` anywhere is `Enemy.cs:2602`, a probe **radius** (`0.6f` vs `0.4f`). Do not cite this as existing. |
| **Player-side breach stance (the thing the enemy lacks)** | ✅ FULLY BUILT | `Assets/_Modules/Village/Troops/RaidAssaultAi.cs` — `enum RaidAssaultPhase {Peel, Breach, Push, Finish}` (`:20`), `enum RaidAssaultJob {Front, Ranged, Breaker, Support}` (`:29`), walls opt-in only (`:113 MayTargetWall`, `:139-141 WallDamageMultiplier`), and **the most-damaged-wall rule** `SelectFocusBreach` (`:610`/`:637`): lowest HP, tie-broken by nearest to muster. Persistent stance + auto-chain in `TroopBreachOrder.cs:53-95`. **The enemy side has no equivalent of any of it.** |
| Squad-scale **flanking** | ✅ PARTIALLY | `Assets/_Modules/Village/Waves/EnemyGroupCoordinator.cs` — hold-then-charge suppression (`:61-74`, released `:143-144`) and *"assign DISTINCT flank bearings across the members"* (`:118`, bearing map `:157`). |
| Per-enemy tactical state | ✅ | `Assets/_Modules/Village/Enemies/EnemyTacticalState.cs` — `{Rush, Flank, Retreat, Suppressed, Kite}`, set via `EnemyBrain.SetTacticalState` / `ApplyRoleTactics` (`EnemyGroupSpawner.cs:176,:294`). |
| **A wave-level "directive" / "modifier" / "archetype" concept** | ❌ **DOES NOT EXIST** | `grep -ci "directive\|modifier\|archetype\|strategy\|tactic" waves.json` → **0**. Live wave schema keys are only `waveId`, `name`, `countdownSeconds`, `expectedCombatSeconds`, `boss`, `bossHp`, `apexBoss`. Code grep over `Village/Waves/*.cs` returns per-**enemy** archetypes and `HeroTalentModifiers` only. |
| **A generic ordered-VFX sequencer** | ❌ **DOES NOT EXIST** | `grep "VfxSequence\|PlaySequence\|SequencedVfx"` → zero. ⚠ `Vfx/MarqueeSpellVfx.cs` is **not** a sequencer despite the name — *"it spawns nothing and owns no pool"* (`:52-57`). |
| The coroutine shape to reuse for a marquee beat | ✅ | `Assets/_Modules/Village/Waves/WaveCelebrationManager.cs:364 PlayWaveClear(int)` → `:378 IEnumerator WaveClearRoutine(int)` — the repo's one numbered, ordered, yielding marquee sequence (bloom spike `:388` → screen flash `:392` → slow-mo dip `:395` → VFX rain `:400-414` → banner `:416`). ⚠ Step 4 plays **one** prefab N times, not N distinct prefabs — a `VFXType[]` + index is the delta. ⚠ **Read the `Time.timeScale` leak essay at `:19-52` first**; any new sequence touching timeScale goes through `DeNelle.Core.UI.WorldHold` (one writer, WO-1353). |
| Per-boss marquee entrance | ✅ | `DragonBoss.PlaySpawnEntrance()` `:597` — `VFXManager.Play(_spawnVfx, …)` + `CameraShakeBridge.Shake(…)` in a `Guard.Try`, fired from `OnEnable` `:293-296`. Single aura handle `_auraHandle` `:318` (Stop-old/Play-new, so only one aura is ever attached). |

**The headline of this inventory:** the owner asked for *"strategies such as the dragon"*. **The dragon
is the only enemy in the game that has one** — a state machine, an Air/Land decision, tower-first then
tree-second target priority, and uncapped per-return growth. Everything asked for in 8b already exists
**once**, on one unit. The spec below is about giving the *wave* what the dragon already has.

### 8b. The directives — design-neutral, cheapest first

Each is classified **DATA CHANGE / NEW BRAIN BRANCH / NAV CHANGE** so the owner can price it.

#### (a) Wave directives per wave band — **DATA + a thin selector**
Every Nth wave carries a named directive that biases the existing machinery:
- **Siege wave** — bias structure roleValue up and un-hardcode the `1f` at `EnemyBrain.cs:1703` so the existing `LowHpWeight` term finally *works*, making a damaged wall the preferred target. **Classification: NEW BRAIN BRANCH, but a one-line one** — the scorer and the weight already exist; only the constant is wrong. **This is the cheapest strategy in the whole section.**
- **Flanking wave** — force two *opposite* gates. **Classification: DATA ONLY.** `SmartEnemySpawner.ResolveSides` already produces opposite pairs at `desiredSides = 2` (`:584-585`, doc `:525-527`); the directive just overrides `SideCountForWave`.
- **Dragon wave** — **already exists** (§5c). The directive would only move the cadence onto the tunable table instead of the `const` at `WaveManager.cs:489`.

#### (b) The dragon as a scheduled marquee event
The dragon already spawns over the Heart with an entrance beat. What is missing is the **ordered
sequence**. Reuse `WaveCelebrationManager.WaveClearRoutine`'s coroutine shape with a `VFXType[]`.
⛔ **Memory rule `sequenced-vfx-special-cases-for-special-events`: marquee moments get ordered prefabs,
never a second spawner or pool.** `VFXManager` stays the one spawner; the sequence is an index over it.
⛔ **Owner tags the keys.** Memory rule `vfx-map-owner-tags-no-creative-pick`: this lane maps key→hook
verbatim and holds un-tagged hooks. **No VFX key is chosen here.**

#### (c) Data shape — `waves.json` field vs a remote band table
⛔ **There is a standing ruling against putting a new spawn knob in `waves.json`.**
`SmartEnemySpawner.cs:283-285` records it: *"WaveDataTest … so the schedule file is not a safe home for
a new spawn knob"*, and `waves.json:14 _RETIRED_batchFields` is the scar from the last time authored
wave data went inert for four weeks with nothing saying so. **A `waves.json` directive field would
repeat that failure**, because endless mode cycles defs 4..20 (§3c) — a directive authored on def 6 would
fire at waves 6, 23, 40, 57… by accident of the modulo.
**Recommended shape (owner rules):** a **remote band table** on the WO-1763 rail, keyed on the TRUE wave
number, e.g. `wave.directiveBands = [{from:20, every:5, directive:"dragon"}, {from:40, every:7, directive:"siege"}, …]`.
Same rail, same migration, retunable from the DB with no build.

#### (d) Non-traditional paths — the owner's *"attack non traditional paths at a certain point"*

| Sub-option | Classification | Evidence |
|---|---|---|
| Prefer the **weakest** wall segment | **NEW BRAIN BRANCH — one constant** | `EnemyBrain.cs:1703` passes `hpFraction` as hardcoded `1f`, making `LowHpWeight` inert. Pass the real `HpFraction` (`WallSegment.cs:304`) and the existing scorer does the rest. **The player's troops already do exactly this** (`RaidAssaultAi.SelectFocusBreach:637`) — copy that rule. |
| Path **through** a completed breach | **NAV — the gap ALREADY opens; only the SEEKING is missing** | **PROVEN.** `WallSegment.Collapse()` (`:427`) disables every enabled carving `NavMeshObstacle` in its children (`:448-453`) **before** firing `Collapsed` — the ordering is deliberate (`:433-435`: *"Stop blocking BEFORE the event fires, so any subscriber that re-queries the navmesh already sees the opening"*). `:442-447`: *"a collider alone never carved anything."* ⚠ **THE TRAP:** the barrier obstacle usually lives on a **SIBLING**, not a child — `Assets/_Modules/Village/World/WallNavObstacleInstaller.cs:22-28` carves the builder's separate **`"WallBarrier-*"` boxes**, so `GetComponentsInChildren` on a WallSegment can legitimately return **0** and the sibling keeps carving after "collapse". **The proving line prints the count:** `WallSegment.cs:455-457`, tag `"WallSegment"` — *"COLLAPSED: {colliders} solid collider(s) and {obstacles} carving obstacle(s) dropped"*. **Capture that number before pricing this.** |
| **Scale** a wall / spawn on the parapet | **NEW BRAIN BRANCH + NAV** | No climbing, no off-mesh links found. Most expensive option here. |
| Spawn from **non-gate perimeter points** | **NEW PLACEMENT — not a data change** | **PROVEN, and the answer is no: every one of the 20 markers is a gate-approach point.** `CastleSpawnPointInjector.cs:66-72` is a 5-position **fan across that side's approach road** (`z = -60..-64`, lateral `±0/12/22`) aimed at that side's gate (`:61 GatePosition = (0,0,-50)`); `:80-87` assigns `GateIndex` **per SIDE, never per marker** (S=2, W=3, N=0, E=1); `:156` `Configure($"spawn-castle-{dir}-{i}", …, gate)` feeds `HeadingToGate` so each spawn **marches at its gate**. **No marker sits against a wall run or at a corner tower.** Perimeter spawning would need new markers. ⚠ Injection **self-suppresses** if any `WaveSpawnPoint` already exists in the scene (`:123-130`). |
| The **dragon ignoring walls** | ✅ **ALREADY TRUE** | `DragonBoss.cs:26-29` — kinematic flight, no NavMeshAgent. Nothing to build. |

#### FlowTrace lines that must prove a directive fired
Per CLAUDE.md §12, no directive ships without a line that proves it ran — and per the §12 ruling,
**instrumentation is permanent; it is never stripped.**
```
FlowTrace.Step("WaveDirective", $"wave {waveId}: directive '{name}' selected from band {bandIdx} (source=remote|default).");
FlowTrace.Step("WaveDirective", $"wave {waveId}: siege directive — focus wall '{seg.name}' hp={seg.HpFraction:P0} (weakest of {n}).");
FlowTrace.Step("WaveDirective", $"wave {waveId}: flank directive — sides forced to {a}/{b} (opposite pair).");
FlowTrace.Step("WaveDirective", $"wave {waveId}: breach at '{seg.name}' is now passable; {k} agents re-pathed through it.");
```
⚠ These are on a **per-wave**, not per-frame, path, so the 3-arg `FlowTrace.Step` is correct. Anything
that lands in `Update` uses the 4-arg `FlowTrace.Measure(system, what, warnAboveMs, intervalSeconds)`
(`Assets/_Modules/Core/Diagnostics/FlowTrace.cs:308`) — the reasoning is at `FlowTrace.cs:293-300`.

### 8c. OWNER QUESTIONS — §8 cannot be implemented until these are ruled

1. **Option 0 (regen gate): which shape?** Suppress during a wave / suppress N seconds after damage / reduce the fraction. **Note the FTUE carve-out constraint (§6 Option 0).**
2. **Option (a) vs (c): does town difficulty follow the WAVE or the HERO?** These are different games (§6 Option (c) caveat).
3. **Post-20 growth rate for HP and for damage — same curve or separate?** The §6(a) table shows the range.
4. **"Massive swarms": bigger roster (cheap) or more bodies on screen (frame-budget risk, §6(b))?** Or both, with a measured ceiling?
5. **Should the dragon's uncapped growth be brought under the same tunables, or stay as it is?** It is currently the only thing still scaling.
6. **Which directives, at which wave bands?** And should the dragon cadence (every 5th from 20) move onto the tunable table or stay a `const`?
7. **Siege targeting: should a damaged wall be preferred over a fresh one** (i.e. do enemies finish what they started), matching the player's own troops?
8. **VFX keys for the dragon marquee sequence** — owner tags them; this lane will not pick (memory rule).

---

## 9. Files to edit (§6 only — §8 is SPEC and edits nothing)

| File | Change |
|---|---|
| `Assets/_Modules/Village/World/SafeZoneRecovery.cs` | Option 0: one combat/recent-damage condition in `Update()` (`:100-133`). Preserve the FTUE case (`:14-22`). |
| `Assets/_Modules/Core/Ops/RemoteTunables.cs` | Option (a): register the `wave.*` keys beside the `raid.*` block (`:414-725`). |
| `Assets/_Modules/Village/Waves/WaveScalingCurve.cs` | Option (a): read post-20 growth from the tunables instead of relying on `postWrapMode = Clamp`. |
| `Assets/_Modules/Village/Waves/WaveManager.cs` | Option (a)/(b): consume the tunables at the existing apply sites (`:2536-2539`, `:1802-1813`). |
| `Assets/_Modules/Village/Waves/WaveCompositionBuilder.cs` | Option (b): `MaxCount` (`:151`) from a tunable. |
| `api/client-tunables.js` + a new `api/migrations/*.sql` | Option (a)/(b): the `wave.*` rows, per the WO-1763 pattern. |
| `Assets/Editor/Regression/…` | New cases — see §10. |

## 10. Acceptance criteria

1. **Option 0 proven by data, not by reasoning:** a headless or device capture shows `[Flow:HeroHealth] TakeDamage` during an active wave with **no** `[Flow:SafeZone] town regen` line inside the suppression window, and the FTUE 1-HP recovery path still fires (its own capture).
2. **A regression pins the wave-176 arithmetic:** given the shipped tunables, a test asserts the HP and damage multipliers at waves 20, 60 and 176 are **no longer equal**, and asserts the wave-20 values still match today's (×2.5 / ×2.0) so existing balance is not silently moved.
3. **A regression pins the count caps** (`MaxCount`, `countCap`) as tunable-sourced, and pins `_maxSimultaneousEnemies` as **unchanged** unless the owner explicitly rules otherwise (§6(b) frame-budget warning).
4. **A regression pins that the town wave path still never reads hero level** unless Option (c) is the ruling.
5. **Frame budget measured** if and only if `_maxSimultaneousEnemies` moves: `FlowTrace.Measure("Perf", …, 4f, 1f)` scopes name the dominant cost in ms, before and after, on the Seeker.
6. Gates: `COMPILE_GATE_OK` + `REGRESSION_OK <n>/<n>` on **fresh** logs, judged by the marker, never the exit code.
7. The owner **felt-verifies and closes** (CLAUDE.md §13) — headless cannot judge "can he just stand there".

## 11. What NOT to touch

- ⛔ **`ZoneManager` / `ZoneManagerCastleSafeTests`.** §3e proves the wave loop never calls `ThreatLevel`. Changing it fixes nothing and breaks the overworld encounter spawners.
- ⛔ **`GarrisonStatBlocks` / `RaidGarrisonSpawner` / any raid path.** WO-1763 owns raid difficulty. Two difficulty authorities is the duplicated-state failure this repo keeps paying for.
- ⛔ **Do not re-add an `enemies[]` array to `waves.json`.** `waves.json:14 _RETIRED_batchFields`; the wave-authoring regression FAILS the gate if live-looking batches reappear while smart composition is on.
- ⛔ **Do not renumber `ActionBarButtonId`, touch the HUD dock, or any `.unity` scene.** Out of lane.
- ⛔ **Do not strip any FlowTrace call** (CLAUDE.md §12, owner ruling 2026-08-09).
- ⛔ **Do not implement §8.** It is SPEC pending the §8c rulings.
- ⛔ **Do not implement §6 Option (d).** §1c proves it already works.

---

## 12. Unverified claims in this document — stated plainly

1. **The build under test is unknown.** Nothing here is proven against the tester's binary (`SESSION_HANDOVER_2026-09-15…:61`, still open).
2. **The tester's identity is circumstantial.** Lv 45/Wave 146 → Lv 51/Wave 176 over one day is consistent, and there is exactly one external player on record — but his save is not in Neon, so no row ties the video to him.
3. **Gear and talents are ASSUMPTIONS.** §2a/§2b state two bracketing assumptions because his actual loadout was never captured. The **conservative** bracket is what the fix menu should be judged against.
4. **`CathedralMageHpBonus` was not investigated.** It can only raise MaxHp, and therefore only widen the regen gap.
5. **Mid-range wave multipliers are approximate** — the curve is an ease-in/ease-out S-curve, not linear (§3a). Only the clamped wave-≥20 values are exact.
6. **The Lv-4 contrast is approximate** for the same reason, and assumes best-by-`req.level` gear.
7. ~~Town navmesh / wall breach~~ — **RESOLVED, see §8b(d).** What remains unproven is only the **obstacle COUNT** on an Elarion perimeter segment (child vs sibling). The `[Flow:WallSegment] … COLLAPSED: … {obstacles} carving obstacle(s) dropped` line answers it in one read.
8. ~~Perimeter spawn placement~~ — **RESOLVED, see §8b(d):** all 20 markers are gate-approach fans.
9. **The winged enemy seen airborne in `frames/f_004.jpg` (≈60 s) is NOT explained.** No enemy in `enemies.json` is declared flying (`grep -n "flying"` → zero matches), and `Enemy._isFlying` is a **targeting-layer flag with no movement effect** — its only four usages (`Enemy.cs:148,:610,:614,:737`) never touch the movement path, and the sole `agent.enabled = false` in `Assets/_Modules/Village` is the death teardown (`Enemy.cs:3286`). So a winged silhouette is a winged *model* on a ground NavMeshAgent. If it was genuinely **over** the wall, the candidates are (i) the apex dragon / `DragonCinematicFlyby`, or (ii) **an agent walking an UNCARVED navmesh at the wall footprint** — which is the sibling-obstacle caveat in §8b(d), and a defect in its own right. **Not investigated further by this lane.**
10. **The compass cannot show spawn-side coverage** (§1b) — the four-gate claim rests on `SmartEnemySpawner.cs:519-525`, not on the video.
11. **No `[Flow:HeroHealth] TakeDamage` line has been captured for this session.** Whether enemies are hitting the hero and being out-healed, or not reaching him at all, is **UNPROVEN**. §7 names the exact pair of lines that settles it. **If the capture shows no `TakeDamage`, §6 is the wrong fix and this WO must be re-scoped to targeting.**

---

## 13. Hand-back

- This WO: `WorkOrders/WORK_ORDER_1773_high_level_hero_takes_no_damage_in_town_waves.md`
- Companion art ticket: `WorkOrders/WORK_ORDER_1774_untextured_slab_in_town_courtyard_tester_video.md`
- WO number **pre-assigned by the lead**; `CLI_LANES_WO_NUMBERS.md` was **not** touched by this lane.
- Board: regenerate with `python tools/board_build.py`.

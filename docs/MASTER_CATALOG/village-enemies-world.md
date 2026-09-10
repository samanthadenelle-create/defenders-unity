# Master Catalog — village-enemies-world

**Dated 2026-08-02** — verified FROM CODE at HEAD `b77a178e` on branch `wip/village2-and-f8-tickets`
(supersedes the 2026-06-12/2026-07-14 revision; that one predates MergedWorld, the WO-772 taxonomy,
the WO-771 raid lock, the 2026-07-30 batch-strip, and the whole Troops lane).

Scope: `Assets/_Modules/Village/Enemies/**` (24 files), `Waves/**` (21), `Troops/**` (20),
`World/**` incl. `World/Camps` (73), plus `Assets/_Modules/Core/Enemies/**` (EnemyResolver +
EnemyTaxonomy, WO-772 Phase 1). Every claim below is cited to the file (comments were NOT trusted —
stale ones are called out in the FLAGS ledger).

**Assemblies:** all Village files are `DeNelle.Village` assembly (camps in namespace
`DeNelle.Village.World.Camps`, some world files in `DeNelle.Village.World`). Core files are
`DeNelle.Core` (`DeNelle.Core.Enemies`). Cross-assembly law: Village → Core only; Village →
BattleATB is now a sanctioned reference (BarracksData.cs:29-32 reuses `StatusKind`; non-circular).
Village → Cosmetics stays a reflection bridge (Enemy.cs:2578-2609 Glimmer).

---

## DELTA 2026-09-09 — raid-stranding watchdog clock-aligned, honor-star snuff cascade, raid-loot cache + retention, rough-stone faucet, and siege-machine asset decider

Five work order results landed on 2026-09-09 across five lanes. All read-verified from RESULT files:

- **RAID (WO-1095 + WO-1594 RESULTS):** `RaidDeployController.ClassifyStranding / StrandingArm` (stranding watchdog measures ENGAGED time like the raid clock, never scene age). `RaidScoring.ComputeHonorStars / PresentationStars` (honor stars light at engagement, snuff by time/destruction milestones). `RaidHudController` star shape (lit vs snuffed carries silhouette + motion, never colour).
- **SPOILS (WO-1461 RESULT § 2):** `RaidClaimService` raid cache seams — `Cached(BankResource)` / `CacheRoomFor(...)` / `RetainOverflow(configId, loot, credited)` / `ConsumeCached(BankResource, applied)`. `RaidVictoryController.RetainOverflow` called at settle, step 3.5b (after `GrantLoot`, before army settle).
- **RAID-3 (WO-1373 RESULT § 2):** Rough stone drop (top two raid tiers, global 1 per UTC day), day ledger in PlayerPrefs (`dotr-raid-stoneday` / `dotr-raid-stonecount`, no schema bump). `Village2RaidController` cycle read.
- **BALLISTA (WO-1617 RESULT § 1):** `RaidBaseGenerator.IsAuthoredSiegeMachine + ResolveSpireArtId` (these live under `Assets/Editor/WallTools/`, NOT `Camps/`). Siege spire placement correction.

---

## DELTA 2026-08-21 — the PvE SIEGE cluster, the per-camp raid COOLDOWN, map PIN producers, and the crate that became a CHEST

Read from source on 2026-08-21 (branch `wip/village2-and-f8-tickets`), not from headers or a summary.
Everything in this block postdates the 08-02 body below; where the two disagree, this block wins.

### NEW DIRECTORY — `Village/Siege/` (1 file). It exists to be OUTSIDE `Village/Waves/`.

- **`Siege/SiegeClock.cs` (162)** `DeNelle.Village.SiegeClock` — **static.** The siege system's ONLY
  read of the persisted wall clock (`TimeSource.NowUnixMs`). Four members:
  `NowUnixMs()` · `StampFired(GameState)` (writes `state.LastSiegeUnixMs`) ·
  `CadenceDefer(state, intervalMs, out why)` · `BankOfflinePressure(state, now, cappedSeconds,
  intervalMs, out note)`.
  ⭐ **WHY THE DIRECTORY EXISTS, verified at source:** `DevTimeSkipRegression` case 6 is a
  FILE-LEVEL source lint — no file anywhere under `Village/Waves`, `BattleATB`, `Village/Enemies`
  or `Dungeons` may reference `TimeSource` **at all**. `SiegeScheduler`/`SiegeSession` sit in that
  swept tree and legitimately need cadence-class (queue) time, so the class-1 reads were MOVED one
  directory over rather than the lint being narrowed. **Do not move this file into `Waves/` and do
  not inline these calls back** — either re-breaches the firewall at the next gate run.
  Two clock-hygiene cases self-heal rather than latch: an unseeded clock (`<= 0`) **seeds forward**
  (a fresh save otherwise reads as 1970 = infinite elapsed = a retroactive assault as the player's
  first act), and a clock in the FUTURE is re-stamped to now (otherwise the cadence stalls forever).
  ⛔ It never writes `GameState.LastHarvestClaimMs` — `OfflineClaimCoordinator` owns that clock
  (WO-1147 precedent).

### NEW in `Village/Waves/` (5 files) — a SCHEDULER + a RECORDER. Neither spawns anything.

- **`SiegeScheduler.cs` (323)** `DeNelle.Village.SiegeScheduler` — `MonoBehaviour`,
  `[DisallowMultipleComponent]`, `IOfflineClaimConsumer`, static `Instance`. Decides WHEN a siege
  arrives, opens the recorder, files the report. Members: `ApplyOfflineWindow(OfflineClaimWindow)` ·
  `Evaluate()` · `Defer(out why)` · `Arm()` · `BindManager/UnbindManager(WaveManager)` ·
  `HandleWaveCleared(int)` / `HandleDefeat()` · `Settle(DefenseOutcome)` · `ForceSiegeNow()`.
  Cadence is **CONFIG, not save state**: serialized `_siegeIntervalHours` (6), `_maxPendingSieges`
  (1), an away-window cap in hours.
  ⛔ **VERIFIED: there is no `Instantiate` in the file.** The entire spawn integration is one call to
  `WaveManager.Instance.ForceBeginNextWave()` — WaveManager remains the single attack authority, and
  `SiegeSpawnAuthorityRegression` fails the gate if a spawn call ever appears here.
  ⚠ **The offline path is deliberately NOT a simulation.** `ApplyOfflineWindow` converts an away
  window into siege PRESSURE; the siege then happens LIVE at the gate with the player watching.
  Resolving in absentia today would write a report whose Rows and ResourcesLost are both empty.
  `DefenseResolution.ResolvedInAbsentia` + `DefenderSnapshot.Garrison` exist in the data model so a
  future fast-forward drops in at this method with no data change.
- **`SiegeSession.cs` (398)** `DeNelle.Village.SiegeSession` — plain class, static `Current` (exactly
  one open session). `Open(waveId, AttackerIdentity, DefenderSnapshot)` ·
  `ObserveTick(heartPos, innerRingRadius, breachArmed, ...)` (ONE null-safe line from
  `WaveManager.TickActiveWave`) · `SamplePath` · `RecordBreach(Enemy)` ·
  `ResolveNearestBarrier` · `Close(DefenseOutcome) -> DefenseOutcomeRecord` · `Abandon(why)`.
  Caps: `MaxBreachRows = 8` (total preserved in `TotalBreachCrossings`, truncation traced),
  `MaxPathPoints = 24` at a 2 s sample interval (long fights THIN rather than truncate),
  `BreachNameRadius = 14f` (beyond it a row honestly reads "Open ground" instead of blaming a gate
  the attacker never touched).
  ⭐ **A DELIBERATE DEVIATION, recorded so nobody "fixes" it back:** the WO plan said to hook the
  existing breach detector. Verified at source — that detector's whole body sits inside
  `if (FeatureFlags.WaveBreachToAtb && _breachArmed && _heart != null)` in
  `WaveManager.TickActiveWave`, and **that flag is default OFF** (WO-579 turned it off because
  crossing the ring yanked the player into an ATB scene). Hooking it would have recorded nothing,
  forever, silently. `ObserveTick` does its own ring test using WaveManager's OWN heart/radius/armed
  flag passed in — an observer, not a second detector.
  ⚠ **The roster is a UNION OVER TIME, not a census** (WO-1113): the spawn budget releases a wave in
  slices with reinforcement drips, so an instantaneous scene census undercounts the force.
- **`SiegeSchedulerBootstrap.cs` (73)** — static, `[RuntimeInitializeOnLoadMethod(AfterSceneLoad)]`.
  Scene-free install (CLAUDE.md §3 forbids hand-editing `.unity`); mirrors `ManageScreenBootstrap` /
  `RealmMapPanelBootstrap`. Installs on every hub load (`HubScenes.IsHub`), destroys on leaving,
  idempotent, `Guard.Try`-wrapped. **Installs even when `FeatureFlags.Siege` is OFF** — a scheduler
  saying "deferred: ff.siege OFF" is diagnosable; an absent one is indistinguishable from a broken
  install. The flag gates ARMING, not existence.
- **`DefenseReportBuilder.cs` (400)** — static. The ONLY adapter between live Village objects and
  `DeNelle.Core.Defense` data: `AdaptRows` (`WaveDamageReport.Entry[]` -> `StructureOutcome[]`,
  `Guard.TryEach` per row) · `StampLegibility(record, StructureVitalsWatch)` ·
  `ResolveBreachOrdinal` · `ComputeDefenseScore` / `DefenseScoreWord` · `ClassifyBand` ·
  `FirstBreach` · `CaptureDefender()` · `ComputeLayoutHash(IReadOnlyList<PlacedStructureData>)` ·
  `BuildPveAttacker(int waveId)` · `BuildStakes(settled)`.
  ⛔ **It is the ONLY file that writes `AttackerSource.GeneratedPve`** — every reader branches on the
  record's `Source` field, so a future ghost-snapshot producer writes `GhostSnapshot` here and
  nothing downstream changes. Enforced by `SiegeSpawnAuthorityRegression`.
  ⛔ **It re-aggregates NOTHING.** `WaveDamageReport.Collect()` stays the one authority on what was
  damaged and what it costs; a second aggregator would give the player two accounts of one attack.
  ⚠ `BuildStakes` is the seam for the (still unruled) loss stakes — split out to **WO-1139**.
- **`StructureVitalsWatch.cs` (350)** — `sealed class` + nested `struct Timing`. Answers WHEN a
  structure was first hit and WHEN it fell, and nothing else: `Build(Vector3 corePosition)` ·
  `AddRepairables<T>` · `Add(...)` · `ComputeFrontRadius()` · `Poll(float elapsedSeconds)` ·
  `Resolve(string name, float assaultSeconds)`.
  ⛔ **A TIMER, NOT A SECOND DAMAGE AGGREGATOR** — its output is MERGED INTO the rows `Collect()`
  produced; it never emits, prices, or selects a row.
  Cost model verified: the watch list is built ONCE at session open and then polled at **4 Hz** (the
  same throttle `HudMinimapWidget` uses), because the player cannot build during a wave and a
  destroyed structure persists as a scannable shell. Name collisions are handled EXPLICITLY: rows
  are keyed by display NAME (WaveDamageReport's existing shape) and a base legitimately has twenty
  things called "Wall", so the merge takes the EARLIEST first-hit and the EARLIEST fall.
- **`WaveManager.cs`** — modified: gains the single null-safe `SiegeSession.Current?.ObserveTick(...)`
  line in `TickActiveWave`. No composition, roster or phase change.

### `World/Camps/` — NEW `RaidCooldownService.cs` (381), static, `DeNelle.Village.World.Camps`

The ENTRY gate for a raid camp; a **sibling of, not a replacement for, `RaidClaimService`** (which
answers "have I EVER taken this camp?" = the LOOT gate). Folding them into one flag would make the
first clear's permanent record expire. Members: `DurationForDifficulty(string)` ·
`DurationFor(SceneConfigDef)` · `DurationForConfigId(string)` · `RemainingSeconds(configId)` ·
`IsOnCooldown` · `DescribeState` / `BadgeFor` / `BlockedMessage` · `BeginAfterClear(configId)` ·
`Begin(configId, durationSeconds)` · `ClearCooldown(configId)`.
⛔ **Clock discipline, verified line by line:** every "now" is `TimeSource.NowUnixMs()` — never
`DateTime.UtcNow`, never `Time.time`. When the clock is NOT server-anchored (cold launch, offline)
the cooldown still runs off the device fallback and the fact is RECORDED
(`RaidCooldownRecord.ServerAnchored`); it is never refused and never lengthened — the WO-1128
ruling: refuse server-side, never punish client-side. A BACKWARDS clock refuses rather than punishes
(the `BuildTimerService.RollWindowIfNeeded` pattern).
Callers wired the same night: `RaidSelectionScreen`, `RaidDeployController`,
`RaidVictoryController`, `Village2RaidController`, `SceneConfigCatalog`.

### `World/` — NEW `RealmPinProducers.cs` (270), static + `RealmPinProducerHost : MonoBehaviour`

Closes a hole where `RealmPinBoard`, `RealmPins` and `RealmAtmosphereStyle` all shipped, both map
surfaces read the board, and **nothing ever published** — the board sat at zero pins forever, so the
parchment map and the corner minimap were faithfully rendering an empty registry. The seam existed;
the producers did not. `PublishAll()` / `ClearAll()` / `PublishHero()` / `PublishDungeons()` /
`PublishRaids()` / `PublishArmy()`.
Three rules each producer obeys, because `RealmPinBoard.Publish` is per-source REPLACE: (1) ONE
source id, taken from the `DeNelle.Core.World.RealmPinSources` constants, used for both Publish and
Clear — a half-renamed producer would land in a second bucket and duplicate every pin it owns with
no way to clear the orphan; (2) publish the COMPLETE current set every time, never append; (3)
nothing to show => Clear. Fog/spoilers reuse the EXISTING fail-closed predicate
`RealmPinBoard.RevealsDetail` fed from `RealmMapVM.RegionStateFor` — no second fog rule.
⚠ A RaidTarget pin is a MARKER, not permission: the full-army gate is re-checked on the Raids flow.

### CORRECTION — `BreakableContainer.cs` is NO LONGER a hostile (supersedes the line in "World support")

The 08-02 entry ("155 lines, destructible loot props on the Enemy-layer sweep") **is now wrong.**
Verified at source 2026-08-21: the file is **482 lines** and is a **loot CHEST the hero OPENS**
(owner ruling 2026-08-21, WO-1132). It no longer implements `IDamageable` or `IDamageableStructure`,
no longer declares `Faction => CombatFaction.Hostile`, and no longer rewrites its own layer to
"Enemy". That layer rewrite existed so the hero's enemy-mask `OverlapSphere` would find and
`TakeDamage()` it — which is precisely the WO-1047 defect ("a dungeon prop is registering as a
HOSTILE target"). Two concerns were sharing one flag; removing the damage concern makes the
targeting one unambiguous, so the bug stops existing rather than being filtered.
⛔ **DO NOT "fix" a future prop-targeting report with an exclusion list in `HeroTargetIndicator`** —
that is the inferior fix this ruling replaced.
⛔ **THE CLASS NAME IS LOAD-BEARING.** "Breakable" is now a misnomer, but every composed dungeon
(`DungeonBaker.PlaceComposeChests`) and every KayKit outpost baked this component into its `.unity`
by script GUID + class name; a rename orphans it on every baked scene. The misnomer is the cheap
half.
It is now a proximity interactable in the shape of `DungeonTreasureCache` /
`DungeonExitInteractable` (ActivateRadius 4.5; proximity TESTED on an interval, the button
`Request`ed every frame because Request is a per-frame claim). The out-of-combat gate consults
`DeNelle.Core.Combat.BattleLock.IsInBattle()` and **only** that — the HUD's hostile(activebattle)
posture is deliberately not used (it is a laggy 0.20 s derivative of the same lock, and lives in
`DeNelle.HUD`, which `DeNelle.Village` may not reach). A refused open is never a dead tap: the
prompt itself becomes the refusal sentence, sourced from `canon-strings.json` via
`VillageStrings.Canon`. **No re-bake required** — `EnsureChest()` normalises the legacy primitive
cube, the "Enemy" layer and the missing chest art on `Awake` (and from `Create()`, because Awake
does not fire on `AddComponent` in edit mode).

---

## ENEMIES — core combat actors (`Village/Enemies/`, 24 files)

### Enemy.cs (2,664 lines) `DeNelle.Village.Enemy`
One wave/roamer/garrison/duelist enemy: NavMeshAgent driver, HP, contact + ranged attack, death,
pooling. `[DisallowMultipleComponent][RequireComponent(NavMeshAgent)][RequireComponent(EnemyDamageable)]`.
- **Public API:** `Configure(id, EnemyDef, heart)` (Enemy.cs:514 — heart may be NULL for hero-only
  duelists/reps); `ApplyWaveScaling(hp,speed,dmg)` (:634); `OverrideMaxHp(float)` (:663 — WO-789
  waves.json `bossHp` pin, applied AFTER scaling, a deliberate wave-level exception to the
  enemies.json stat SSOT); `SetBrainTarget` / `SetBrainTargetPosition` (:441/:450);
  `Heal` (:471); `TakeDamage`/`TakeDamageFrom` (:1915/:1928); `ApplyDamageOverTime` (:2033, WO-566
  talent procs); `RangedAttack(Transform,float)` (:1510); `Kill()` (:2073); `SetNextDamageTint` /
  `SetNextImpactElement` / `SetNextDealtByHero` (:1892/:1902/:1913 — one-hit stamps consumed in
  TakeDamageFrom); `SetCombatPresentation` / `PlayAmbientGesture` (:453/:456); pool seam
  `SetPoolKey`/`ResetForPool`/`PrepareForReuse` (:190/:2097/:2144).
- **Props:** `EnemyId`, `EnemyDefId`, `DisplayName` (:388 — precise catalog name for the target
  frame, never a GameObject-name parse), `Hp/MaxHp/HpFraction`, `Level` (:408, WO-611 F3 — stable
  authored-HP band `def.Hp/25`, never creeps with wave scaling), `IsDead/IsAlive`, `Ai`, `IsFlying`,
  `CombatLayer`, `ContactDamage` (:83 — HeroHealth reads the real per-enemy stat), `EngineDefId`
  (:489 — ATB bridge: necromancer/hollow-warrior exact, everything else → "skeleton").
- **Events:** `Died(Enemy)`, `ReachedHeart(Enemy)`, `Damaged(Vector3)` (retaliation seam), and the
  STATIC cast-telegraph pair `CastStarted(Enemy,string,float)` / `CastEnded(Enemy)` (:1554-1556, P4
  HUD cast-bar seam; if the caster is destroyed mid-cast CastEnded never fires — subscribers must
  self-expire).
- **DELTA 2026-09-06 (WO-1450 + WO-1459 S2 suspect 3)  -  the structure probe has a CADENCE GATE, and
  the acquire trace is change-gated.** `ProbeForStructure` runs a forward `SphereCast` **and**
  (flag-on, hero not near) an all-direction `OverlapSphere` **on mask `~0`  -  EVERY LAYER**. Until this
  gate it ran **once per frame for every enemy holding no target**: measured device floor
  `LOW fps=11 ms=87.4 mem=427MB scene=RaidBase_raider_camp_small towers=0 enemies=13` at
  `timeScale=1.00`  -  13 enemies x 2 physics queries x 60 fps = **1,560 all-layer queries a second**.
  `private const float ProbeIntervalSeconds = 0.25f` + `_nextProbeAt` (`Enemy.cs:261-278`) caps it at
  **<= 4 probes/sec/enemy**.
  - (!) **THE GATE IS ON THE RETRY CADENCE ONLY  -  NOT ON SELECTION.** It is consulted *solely* on the
    branch where `_currentTarget` is already null; a **held target still drops the same frame it dies
    or flees**, and `ProbeForStructure`'s own logic (forward cast first, hero-primary suppression,
    `CombatFactionRules.MayAttack`) is untouched. A skipped frame takes the identical
    `_attackCooldown = 0f; return;` path a null probe already took, so those frames are
    **byte-for-byte** what a failed probe produced.
  - **The acquire trace was evicting the evidence.** It was an unthrottled `FlowTrace.Step` firing on
    every probe hit  -  **38,018 lines between 12:59:05 and 14:37:52 (~320/sec)**, each carrying a
    managed stack walk, against a **256 KiB** Android main ring: it evicted the boot window and every
    other trace in under two seconds (memory `logcat-ring-buffer-destroys-evidence`). It is now gated
    **TWICE**  -  `_lastProbeTargetId` makes it fire only on a target **CHANGE** (a re-acquire of the
    same wall is not news), and then at most **1/sec per enemy** via `FlowTrace.Throttle`. STOP: **The
    trace is KEPT, not stripped** (CLAUDE.md S12)  -  the defect was the cadence, not the line.
  - **THREE PERMANENT drop-path traces**, one per `_currentTarget = null` site, because a capture
    could not previously tell them apart and a re-acquisition thrash read as "the probe is noisy":
    `path=not-alive` (`:1818`), `path=out-of-reach (DEF-224)` (`:1848`  -  prints the measured
    distance vs the drop ring, so a future capture proves whether a thrash is a target oscillating
    across the ring or a target dying), and `path=self-death (terminal, no re-acquire)` (`:3224`).
    The third can never feed a thrash; **naming it anyway is the point**  -  a capture showing it is
    proof the thrash is NOT from there, exactly the elimination a static read could not make.
  - `PrepareForReuse` resets `_nextProbeAt = 0f` **and** `_lastProbeTargetId = 0` (`:3050`)  -  a reused
    body probes on its first live frame and its first acquire is a genuine CHANGE; inheriting the dead
    body's target id would swallow it. Pinned by **`EnemyProbeCadenceRegression`**
    (`[enemy-probe-cadence]`), which **declares itself a SOURCE LINT in every reason string**: it
    cannot prove line counts or frame cost  -  that is a device capture  -  only that the guards are not
    reverted by a later edit.
- **DriveNav destination priority** (:1065-1139): (1) `_brainPositionOverride` (a live EnemyBrain
  decides here every frame) → (2) DEF-224 self-contained hero aggro (radius 7 m default, +2.5 m
  hysteresis, def.AggroRadius honored since WO-397 at :550) → (3) static `_brainTarget` tether →
  (4) Heart march. WO-419 hero-chase tightens `stoppingDistance` 2.5→1.1 m (:123,:1141-1156) so a
  chaser enters HeroHealth's 1.5 m engage ring; partial-path chases still count as hero-chase via
  the `HeroChaseProximity` 4 m heuristic (:1092-1125). `ReportPursuit` feeds the HUD posture arc
  each chasing frame (:1179-1180).
- **Casting root holds** (F8-38 fix, :994-1005): while `_casting`, DriveNav holds the agent stopped
  and returns — the caster can no longer walk mid-channel. **Heartless hook** (:1007-1025): a
  heart==null enemy drives straight at its brain override (roam/hero) — the "rep stood still" fix.
- **Contact attack:** ProbeForStructure (:1722) = forward SphereCast first (this is ALSO how a
  brain-less enemy hits the hero), then — flag `ff.enemystructureaware`, default ON — an
  all-direction 3 m sweep for the nearest live SIEGE structure, suppressed while the hero is in
  aggro (hero-primary) and never targeting HeroHealth (:1829-1876). Every strike telegraphs:
  wind-up floored at 1.0 s (`ContactTelegraphFloor` :1370) + an unconditional
  Impact_ShockwaveRing ground tell (:1387-1400). `DealStructureDamage` (:1466) is the single sink —
  Heart hits are wall-mitigated (walls.json `heartDamageMultiplier`, CITY-02) and a spiked top-tier
  wall bites melee breachers back (CITY-03).
- **Ranged/caster:** `RangedAttack` routes through `RootedCast` when `ff.enemyrootedcast` (default
  ON): rooted, ≥1.0 s wind-up, WindUp→Cast anims, ground tell, then a VISIBLE Hovl orb
  (cast/projectile/impact keys from the EnemyTypeVfxSet or the Fire_Cast / PP_FireBall /
  FireballImpact_Impact full-prefab defaults, :1574-1577) with damage landing on orb ARRIVAL
  (:1653-1690). Flag OFF = legacy instant hit.
- **In-scene battle-lock** (:798-820, the 2026-06-30 "0 damage in dungeon" root fix): a heart-less
  `EnemyBrain.HeroOnlyTarget` duelist with the hero in aggro raises
  `HeroCombatEngagement.SetEngaged` so `BattleLock.IsInBattle()` goes true and the hero's swings
  actually fire; released edge-triggered + on OnDisable (:687-694).
- **Death & pooling:** `Die` (:2229) snaps the corpse to ground (`SnapBodyToGround` :2466 —
  visible-bottom footGap capped at 3 m against corrupt renderer bounds, layers
  Default/Structure/Building/Water), pays XP (`ProgressionManager.ReportKill` shared +
  `HeroProgression.AddXp` flat), Glimmer (reflection), and GOLD (`def.CoinReward` else XP×0.4,
  :2360-2364), then holds `DeathHoldSeconds` 3.5 s and **returns to EnemyPool** (no Destroy;
  :2372-2409 with a per-frame ½-raycast corpse settle). Combo/kill-streak feedback is gated to
  hero-dealt hits only (ticket #61 stamps).
- **Feel dials:** global −5% enemy speed in Configure (:534), `EnemyAttackIntervalScale` 1.12
  (:169, owner 2026-07-03), anti-chop 0.12 s smoothed Speed feed + position-delta anti-slide
  (:882-966), smooth anti-snap facing slerp (:824-871). **WIRED/LIVE** — every spawn path.

### EnemyBrain.cs (1,379 lines) `DeNelle.Village.EnemyBrain`
Role AI overlay (DEF-21) + tactical states (DEF-72) + perception (WO-147). `[RequireComponent(Enemy)]`.
- **Update precedence** (:580-729): bow-equip latch (Ranged role gets a visible bow via
  HeroBowAttachment, :592-603) → Knight TAUNT (:609) → retaliation provoke (6 s, re-armed on hit,
  :621-641) → BehaviorTree yield if `IsInitialized` (:644) → hero re-acquire ~1 s → perception tick →
  tactical state → **dungeon leash gate** (WO-770.11 :681-711 — leashed mob out of hero range yields
  no target AND walks home to its anchor; `ShouldLeashOut` is a pure static, :147) → `ChooseTarget`
  by role → tactical destination → kite fire / healer tick. Single owner of enemy targeting.
- **Roles** (`ChooseTarget` :1041): `_heroOnlyTarget` (WO-482 arena/dungeon duelist — hero is the
  ONLY target, :1047-1052) → Tank = threat/tower/heart; Healer = most-damaged ally;
  DPS/Ranged/MiniBoss = protect-the-support pre-pass then the WO-145 weighted scorer
  (`ScoreAndPickTarget` :1129, throttled 2 s, considers pet/hero/towers/`ISiegeLootTarget`
  collectors/structures/Heart) with the legacy chain fallback.
- **Shared runtime TacticalData singletons:** `KiterTactics` (10 m band, 1.6 s cd, :167),
  `FlankerTactics` (:190), `CoordinatedFlankerTactics` (:207), `SiegeTactics` (:225),
  `SupportTactics` (:242); `RoleForId(string)` (:260) and `ApplyRoleTactics` (:273) map
  ids/roles → tactics. Enemy.Configure gives every `role=="caster"` def the Kiter (Enemy.cs:560-564).
- **Tactical states:** Rush (path-validity cached 2 s, PARTIAL paths steer to last reachable corner
  via `TryGetPartialApproach` :1009 — the "no COMPLETE path, holding" freeze fix), Flank, Retreat,
  Suppressed (EnemyGroupCoordinator releases), Kite (`ComputeKiteDestination` :912 + ranged fire),
  Reposition (rally→re-engage :961).
- **Other seams:** `SetLeash` (:134), `SetHeroOnlyTarget` (:123), `TauntTo` (:573),
  `SetCoordinatedFlankAngle` (:461), `WantsCombatPresentation` (:400), event `Died`.
  Hero resolved by COMPONENT (`HeroLocomotion`) not tag (WO-450, :554). **WIRED/LIVE** — but NOT
  auto-added to plain wave enemies; spawners add it for roles/packs/bosses/duelists.

### EnemyFactory.cs (734 lines) `DeNelle.Village.EnemyFactory` (static)
THE single skinned-enemy builder. `Build(EnemyDef,pos,rot,parent,modelOverride=null)` (:32).
- Seats the spawn ON the NavMesh BEFORE adding the agent (`SeatSpawnOnNavMesh` :379 — progressive
  6/12/24 m snap, raycast-ground on total miss so a body never floats, WO-791); post-add the agent
  is verify-AND-REPAIRED via `Warp` if it woke off-mesh (:330-343).
- **WILDLANDS DEFERRAL GATE** (:76-87, PAIN_POINTS 2026-07-26 §1.1): a non-combat-approved id
  (orc-raider/caveman/feral-wolf/tiefling-cultist — no shippable art, Orc_Berserker retargets to
  exploded geometry) is REDIRECTED at this one chokepoint to a ratified Hollow substitute
  (`EnemyResolver.SubstituteHollowId`), covering ALL spawners.
- `ModelForEnemy` (:414): Hollow ids resolve through **EnemyResolver** (WO-772) first; then explicit
  cases — Wildlands stand-ins (orc-raider→Orc_Berserker, caveman→Orc_Berserker,
  feral-wolf→Skeleton_Rogue, tiefling-cultist→Demon), Orc Warband (berserker/shaman/necromancer),
  WO-482 Orc family (warrior/tank/mage), orc-warlord→Orc_Necromancer, WO-680 Blink orcs
  (`Blink/…`, dev-compare only), troll/ogre stand-ins (tinted orc reuse), demon,
  boss-dragon/dragon→**Boss_Dragon** (licensed); then a family fallback; then a size default with a
  loud Warn (:526).
- Skinning: `VisualFactory.Skin` + rig-forward −90° yaw for Tripo/AccuRig families (:123-152, the
  troll upright-yaw ticket-#2 fix), **RENDER-VERIFY** (`VerifyVisualRenders` :689 — drops an
  invisible-but-hittable body to the tinted capsule), Tripo→URP material fixer + per-family
  basecolor/tint fallback (:193-266; WO-790 albedo-restore seam: staging
  `Enemies/OrcTex/<model>_basecolor` wins over the solid tint), `ReGroundVisual` (:538) and the
  **PROPORTION GUARD** (`EnforceProportion` :561 — re-normalises any body outside [0.5x,2x] of
  def.Height). Held-weapon attach exists but is **gated OFF** (`ff.enemyweapons` default OFF,
  :184-186 + AttachEnemyWeapon :610). Agent shares the hero's type 0 + radius/height (:314-321);
  auto-adds Enemy, EnemyDamageable, ActorAnimator, WeaponTrailController, OrientationGuard.
  **WIRED/LIVE** — every spawner routes through here.

### EnemyAnimatorFactory.cs (350 lines) (static) + enum `EnemyRig`
`RigFor(model)` → HumanoidMedium (KayKit default), HumanoidLarge (Skeleton_Golem), **LargeHumanoid**
(Troll/Demon/OgreMage — WO-445 Mixamo humanoid brutes), Boss (Necromancer), Dragon, OrcWarband
(Berserker/Shaman/Necromancer), OrcHumanoid (Warrior/Tank/Mage — per-role
AnimatorOverrideControllers `OrcHumanoid_Mage/_Warrior/_Tank`, :105-118), SkeletonHumanoid
(AccuRig Skeleton_Mage/Warrior/Rogue/Healer), BlinkOrc/BlinkOrcBoss (WO-680). `Apply(visual,model)`
stamps `Resources/Enemies/<controller>`, root motion off, no-op-safe. **WIRED/LIVE.**

### EnemyDamageable.cs (130 lines)
Adapter: Enemy → Core `IDamageable` + `IDamageTintable` + `IHeroDamageMarkable` + `ICombatLayered`.
Lazy self-healing `E` accessor guards RequireComponent add-order AND destroyed-adapter fake-null
(:57-69). Forwards element stamps (WO-219), hero-source marks (ticket #61 `MarkNextHitFromHero`
:136), and hosts the `CombatStatusTracker` (freeze/slow/burn timers + `CollectActive` for the HUD
buff row, :71,:141-146 — the old "ApplyStatus is a no-op" header is stale, see FLAGS). **WIRED/LIVE.**

### DragonBoss.cs (1,574 lines) `DeNelle.Village.DragonBoss`
Syndrath the Devourer — apex flying wave-boss (WO-760). **THE ASSET IS THE LICENSED Asset-Store
dragon** (product 71047 "Dragon Animated", WDallgraphics, `Assets/Dragon/Prefab` →
`Resources/Enemies/Boss_Dragon`, controller by DragonAnimatorSetup); the CC-BY-NC 3DHaupt fbx was
RETIRED 2026-07-24 (DragonBoss.cs:4-11, EnemyFactory.cs:498-503). Kinematic flight — NO
NavMeshAgent, NOT in WaveManager `_liveEnemies` (tracked as `_liveApexBoss`). Sequence state machine
`DragonState`: Approaching → Landing → BurnTowers → AirAttack → RetargetTree → Finale → Death, with
the per-attack air-vs-land choice made by an EnemyBrain-STYLE decision hook (`DecideAttackMode`,
owner 2026-07-24 — it reuses the brain's decision PATTERN, not the component). HP phases
`DragonPhase` (Circling/Stooping/LastWing/Falling) drive auras + BossHealthBar. Implements Core
`IDamageable` directly (`TakeDamage` :1296, `ApplyStatus` :1311, `Kill` :1317); `Configure(bossId,
anchor, maxHp)` (:429); events `Died`, `PhaseChanged`, `StruckHeart`. `DragonAnim` (:71) is the
shared param-name contract with the editor builder. **WIRED/LIVE** (waves.json wave 20 apex).

### TargetManager.cs (98 lines) — static-API registry singleton
Persistent DDOL registry of live Enemy (replaces the overflow-prone OverlapSphere reticle sweep).
`Register`/`Unregister` from Enemy OnEnable/OnDisable (pool-safe, dedup), queries
`GetClosestTarget` / `CollectInRange` self-clean dead entries. **WIRED/LIVE** — reticle, towers,
pets, outpost auto-combat all read it.

### PlayerAttackController.cs (709 lines) — player-side melee (DEF-47)
On the hero root. Space/LMB/gamepad-South swing; `TriggerBasicAttack()` (:377) is the HUD
basic-attack button seam honoring the same gates. **Combat inputs are gated on
`BattleLock.IsInBattle()`** (:241 — town/overworld presses are suppressed + FlowTraced).
- Swing: face target first (WO-423 :443, reticle-locked else nearest LoS-clear hostile), class
  routing (knight combo ×3 / casters PlayCast, :407-419), WO-217 tempo shaping, damage lands on the
  impact frame (`_impactFrameDelay` 0.13 s, :496-503). Damage = `_baseDamage` 30 × weapon
  `damageMult` × perfect 1.75 (window 0.08-0.18 s) × riposte 3.0. WO-449 LoS gate (`HasLoS` :486)
  rejects through-wall hits; degrades open when the Structure mask is absent.
- **Parry/riposte** (:296-366): raising block (knight only — Shift/RMB/LT) opens a 0.25 s parry
  window (+ shield-ward ring cue); `TryConsumeParry()` is called by HeroHealth on incoming hits;
  `OnParrySuccess` = slow-time beat + clang + pooled "PARRY!" CombatText + arms a 2.0 s riposte
  (next swing ×3, one per parry); `OpenParryWindow(sec)` is the public seam a caster deflect spell
  reuses.
- On-hit VFX (:570-596): a non-elemental weapon fires the generic `Weaponskillsword_Impact` burst
  (owner-picked red impact flash since d888b278 2026-07-30); an element-branded weapon SUPPRESSES it
  and fires its elemental key (`WeaponVfxMap.ElementalOnHitKey`); perfect hits add
  `KnightWeaponskill_Impact`. ⚠ A 2026-08-02 change REMOVING the basic-attack impact burst outright
  (the "rocks on swing" F8; its proving line = the VFXManager.PlayKey success trace added in
  b77a178e) is IN FLIGHT in a parallel silo and was NOT yet in the tree at this audit — re-verify
  :586 after that lands. WO-566 on-hit DoT procs (:601-613); WO-497 haptic on connect. **WIRED/LIVE.**

### EnemyTypeVfxSet.cs (122 lines) — SO, per-archetype juice
Hit/death VFX prefab arrays + hit/death/attack clips + telegraph duration/prefab (DEF-48) + the
WO-VFX-RANGED Hovl key trio `CastVfxKey`/`ProjectileVfxKey`/`ImpactVfxKey` (defaults Arcane_*)
with `RangedVfxTint` (:85-96). Optional on Enemy — null falls back to VfxPool +
EnemyCombatAudio + the Enemy.cs fire defaults. **DATA/LIVE (rarely authored).**

### EnemyPool.cs (201 lines) — persistent keyed body pool
DDOL, self-bootstrapping, ProjectilePool idiom. Keyed by model id / prefab name so a skeleton is
never handed out as an orc. `Get(key,prefab,def,pos,rot,parent)`; Enemy.Die → `Release`. The reset
contract lives on Enemy (`ResetForPool`/`PrepareForReuse` — damage-ledger Forget under BOTH keys,
animator Rebind, agent re-enable + Warp). **WIRED/LIVE.**

### Support / dev (Enemies/)
- **EnemyBehaviorTree.cs** (150) — DEF-43 BT wrapper; when present + initialized the brain yields to
  `Evaluate()`. Optional/prefab-only. **Present.**
- **EnemyTacticalState.cs** (54) — enum Rush/Flank/Retreat/Suppressed/Kite/Reposition. **DATA/LIVE.**
- **NavPathCoordinator.cs** (129) — DEF-56 staggered initial SetDestination scheduler
  (`RequestInitialPath`, called from Enemy.Configure:612). **WIRED/LIVE.**
- **Perception/AwarenessSensor.cs** (294) — WO-147 consolidated Unaware/Suspicious/Alerted sensor;
  auto-added by EnemyBrain; drives the Animator `IsAlert` + EnemyAlertTell. **WIRED/LIVE.**
- **EnemyAlertTell.cs** (85) — "!" spotted tell. **LIVE.**
- **EnemyHitReaction.cs** (94) — red hit-flash, auto-added. **LIVE.**
- **EnemyCombatAudio.cs** (141) — WO-220 static fallback hit/death/cast-charge SFX via
  CoreServices.Audio. **LIVE.**
- **EliteVFXController.cs** (121) — WO-66 elite/boss death VFX differentiation. **LIVE/opt-in.**
- **BossHealthBar.cs** (293) — code-built uGUI apex-boss bar (binds DragonBoss). **LIVE.**
- **DamageNumberSpawner.cs** (293) — asset-free floating combat text (+ `SpawnLabel`). **LIVE.**
- **EnemyFamilyTestSpawner.cs** (308) — DEV TOOL, DDOL, scene Village2, gated on
  `ff.devhotkeys` (default OFF, :90): **J** = role test pack, **K** = high-level scatter
  (ThreatSkullPlate felt-test), **B** = Blink-vs-Tripo orc side-by-side (WO-680) (:91-98). **DEV.**
- **OutpostEnemyGroupSpawner.cs** (157) — seeded skeleton group at a dungeon/outpost choke;
  heart=null + `SetHeroOnlyTarget(true)` + `SetLeash(anchor, 10 m)` (WO-770.11, :39-42);
  auto-spawns on Start when placed as a marker. **WIRED/LIVE (dungeon/outpost chain).**
- **OverworldEncounterSpawner.cs** (1,454) — WO-482 open-world hook: cheap wandering orc "rep" packs
  (pools rolled 1-7 from orc-warrior/tank/mage, :42-44) in `Main_Castle_Overworld`; ENGAGE pops the
  full family into the isolated BattleArena; chase at ~+5% hero speed; low-level hero swarm cap
  (≤ level 5 → max 3 concurrent, :50-51). Gated `ff.overworldencounter` — **default ON again**
  (owner reversal 2026-07-30 F8 seq511; the file header still says "default OFF", stale). **WIRED/LIVE.**

---

## WAVES — the Elarion wave loop (`Village/Waves/`, 21 files)

### WaveManager.cs (2,462 lines) `DeNelle.Village.WaveManager`
The village wave loop owner. `[DisallowMultipleComponent]`.
- **Public:** `BeginLoop()` (UniTask), `ForceBeginNextWave()` (HUD DEFEND kick),
  `GetEnemyCatalogAsync()`, `SpawnEnemyForExternalMode(...)`, `EnemyPrefab`. Props `Phase`
  (`WavePhase` Idle/Countdown/Active/Breached/Complete/Defeated), `CurrentWaveId`,
  `CountdownRemaining`, `LiveEnemies`, `LiveApexBoss`, `Heart`. UnityEvents `OnCountdownTick`,
  `OnWaveStarted/Cleared/OnBreach`, `OnApexBossSpawned(DragonBoss)`, `OnDefeat`.
- **DEFEND-gated start:** `_autoStart` default false (:170) — Idle at load until the HUD button.
- ⛔ **`_phase == Active` RAISES THE BATTLE-LOCK, and the wave loop now UNWINDS IT ITSELF** (WO-1308,
  2026-09-02). `OnEnable` registers three things: the `BattleLock` probe
  (`isActiveAndEnabled && Instance == this && _phase == WavePhase.Active` — **never narrow it**, a
  live siege genuinely IS combat), the `wave-phase` `QuiescenceProbe`, and — new — a
  **`BattleSessionEnd.RegisterUnwind("WaveManager.phase")`**. The unwind
  (`ReconcileLatchedWavePhase`) drives **ONE `TickActiveWave()`** so the loop reaches its own clear
  test: a live siege keeps the lock, an empty one completes. It **re-decides nothing** — the clear
  rule stays in `TickActiveWave` alone. It declines while `TownSuspension.SuspendedFor(this)`,
  because that freeze is deliberate and self-clears.
  ⛔ **AND UNTIL WO-1694 (2026-09-10) THAT FREEZE NEVER ENGAGED FOR AN ARENA FIGHT.**
  `TownSuspension.SuspendedFor`'s ACTIVE-SCENE EXEMPTION was unconditional, and the battle arena
  stages 7 km away in the **same** scene — so `WaveManager`, living in that active scene, was exempted
  and `BattleArena`'s hand-driven `Suspend` (`BattleArena.cs:516`, added for precisely this defect) was
  a no-op. The village countdown ran through the whole fight and a wave spawned ~0.8 s after the win,
  re-raising the battle-lock and failing the quiescence gate. The exemption is now scoped to a hold a
  scene **CHANGE** created (the FLOOR); a floorless hand-driven hold covers the active scene too. Read
  the rule at `Assets/_Modules/Core/TownSuspension.cs` (`SuspendedFor`), never off a doc; pinned by
  `Assets/Editor/Regression/ArenaInSceneSuspensionRegression.cs`, evidence in
  `WorkOrders/WORK_ORDER_1694_arena_win_leaves_battle_lock_held.md`.
  Two latch sources were closed with it: `OnDisable` now stands the phase down to Idle alongside the
  roster it already cleared (`"OnDisable/roster-cleared"`), and the `_heldSmartReinforcements` clear
  gate now releases on a **stale drain heartbeat** (`_reinforcementDrainUnscaled`,
  `ReinforcementDrainStaleSeconds = 20`) — the fire-and-forget `DrainSmartReinforcements` cannot zero
  its own counter when it throws, and that wedged the wave forever.
  Pinned by `BattleQuiescenceRegression` groups `[wo1308]` + `[wo1308-wiring]`.
- **Spawn-path priority** (:1255-1276): `_smartComposition` **default TRUE** (:147, WO-362 —
  generated tiered roster + role placement + rotating gate via SmartEnemySpawner /
  WaveCompositionBuilder) → `_composeFamilyGroups` (:135, WO-316 role-mix squads via
  EnemyGroupSpawner) → legacy flat `SpawnBatch` (:1674). Plus the optional inspector
  `_waveGroupSequence` WaveEnemyGroup assets (:1279-1310).
- **STANDING TRUTH — authored batches (owner ruling D1, WO-783): RESOLVED 2026-07-30.** Smart
  composition IS the authority. The 55 inert `enemies[]` batch entries (148 enemies across 19
  waves) were STRIPPED from both dual copies of waves.json in commit `7f1f1e6a`; the design intent
  is preserved as seed data in `docs/design/WAVE_AUTHORING_REFERENCE_2026-07-30.md`; the
  `[wave-authoring]` oracle (`WaveAuthoringLiveRegression`, marker `WAVE_AUTHORING_OK`) goes RED if
  live-looking batches are re-added while `_smartComposition` is on. Only `countdownSeconds`,
  `boss`(+`bossHp`) and `apexBoss` still take effect from waves.json.
  ⚠ The in-code comment at WaveManager.cs:1365 still says "OPEN OWNER RULING (WO-783)" — STALE
  (see FLAGS); the once-per-session `WarnAuthoredBatchesDiscarded` (:1369) now no-ops because
  `wave.Enemies` is empty.
- **Bosses:** `wave.Boss` spawns via SpawnBatch at spawn-0 with the WO-789 `bossHp` HP pin
  (:1330-1336, applied in SpawnOne at :1862-1869 AFTER WaveScalingCurve via `Enemy.OverrideMaxHp`);
  `apexBoss` fields the kinematic DragonBoss (`SpawnApexBoss` :1599-1644, prefab else
  `Resources/Enemies/Boss_Dragon` fallback), tracked separately for wave-clear (:2010-2011).
- Breach: inner ring 9 u (:158) → `SceneRouter.GoBattle(BattleParams)` → ATB. Zero-spawn wave warns
  loudly (:1318-1325). Wave-clear resource reward (WO-330/361). Heart-death → Defeated. **WIRED/LIVE.**

### Waves support (per-file)
- **WaveData.cs** (486) — typed records + `WaveDataLoader` for canonical waves.json/enemies.json
  (`EnemyDef`/`EnemyCatalog`/`WaveDef`/`WaveBatch`/`ApexBossDef`, dual-copy CanonicalJson). **DATA/LIVE.**
- **SmartEnemySpawner.cs** (229) + **WaveCompositionBuilder.cs** (316) — WO-362 generated roster
  (tier mix, elite every 5th, anti-repeat) + tactical role placement at a rotating gate. **LIVE (the
  authoritative wave path).**
- **EnemyGroupSpawner.cs** (276) + **EnemyGroupCoordinator.cs** (161) + **WaveEnemyGroup.cs** (151)
  — DEF-21/72 formation squads, atomic hold-then-release, coordinated flank bearings. **LIVE
  (family-compose + group assets).**
- **WaveSpawnPoint.cs** (82) — spawn marker (tag `SpawnPoint`, ~12 m outside gates).
  **CastleSpawnPointInjector.cs** (144) — runtime non-destructive spawn-point placement in
  MainCastle_Hall. **LIVE.**
- **WaveScalingCurve.cs** (92) — DEF-59 SO: HP/speed/damage growth per wave. **DATA/LIVE.**
- **StartWaveHudBridge.cs** (135) — HUD "Defend!" → `ForceBeginNextWave`. **WaveHudBridge.cs**
  (146) — WaveManager events → `IVillageHud` via CoreServices (no reflection). **WaveCountdownUI.cs**
  (159) — screen-space countdown. **LIVE.**
- **WaveFeedbackDirector.cs** (326, WO-38/40 wave juice), **WaveCelebrationManager.cs** (214,
  WO-83 clear burst), **KillComboTracker.cs** (207, WO-83), **SkyProgressionController.cs** (165,
  DEF-66 darkening sky), **WaveDamageReport.cs** (194, F8-45 post-wave structure/economy damage
  report), **AlertIntelSystem.cs** (WO-1184 friendly uGUI lookout notice chip; DEF-199),
  **DailyQuestCombatBridge.cs** (41, OnWaveCleared → DailyQuestService), **WaveAccessLock.cs** (34,
  RequireComponent gate helper). **LIVE support.**

---

## TROOPS — the player army + raid lane (`Village/Troops/`, 20 files; WO-453 → WO-771 → WO-823)

The raid loop is **LOCKED to Teleport/Deploy** (WO-771, 2026-07-26): RaidSelectionScreen → deploy
into a `RaidBase_*` scene. The walk-to overworld raid is retired behind `ff.raidwalk` (default OFF).

- **TroopController.cs** (460) — one deployed friendly fighter, `IDamageableStructure`; copies the
  Pet hostile-hunt loop (0.2 s scan throttle, eased NavMeshAgent locomotion, manual facing);
  footman/archer differ only by def stats; expendable (death → destroy). WO-771.9
  `ApplyUpgradeStats` re-bases HP/DPS/reach/aggro at the persisted upgrade level + carries
  `AbilityUnlock`s; `ApplyDamageMultiplier`/`ApplyHealthMultiplier` RE-BASE (never compound).
  Rally: idles walk to `TroopRally.Point`; a foe in range always wins. **WIRED/LIVE.**
  - **DELTA 2026-09-06 (WO-1438/WO-1439)  -  `NearestHostile` no longer puts walls and defenders in
    ONE nearest-wins bucket.** *Measured, not inferred* (`logs/debug/troop-ai-blind-2026-09-06.log`,
    14:36, build `2026.09.06.358161`): `won='Wall_Outer_SS_11(WallSegment)' kind=struct dist=4.2m ...
    accepted[unit=1,struct=17] ... preferStruct=False`. **`accepted[unit=1]` is the proving field**  - 
    a LIVE defender was inside the sweep and a wall panel won anyway; the same archer then walked
    SS_11 -> Watchtower -> SS_12 -> SS_7 -> SS_13 -> SS_6 -> SS_14, **outward along the ring**. Cause: when
    `_preferStructures` was FALSE (every role except siege) a hostile STRUCTURE fell through to the
    same `else` as a live defender. The loop now only **MEASURES**; the pick happens once, below it,
    against **separated buckets** (`TroopController.cs`, `NearestHostile` at `:762`).
  - (!) **THE UNIT PREFERENCE IS GATED ON REACHABILITY, NOT ABSOLUTE  -  and the reason is the steering
    model.** This class steers with `_agent.Move(displacement)` (no `SetDestination`, no obstacle
    avoidance) and its sweep sees a guard THROUGH an intact baked wall, so an unconditional "always
    prefer the unit" pushes a footman into a navmesh edge and freezes it  -  strictly worse than
    chewing the wall. A unit wins only when it is **already inside attack range** or a **complete,
    non-detouring NavMesh route exists**. That is *"a breach is a route"* expressed in the selector:
    the wall stands -> no route -> the wall stays the target; the hole opens -> the route completes ->
    the defender wins. **No pathing rewrite  -  the route is read as a FILTER, never steered by.**
    Constants: `RouteCheckInterval = 0.5f`, `RouteDetourFactor = 1.5f` (calibrated on the 09-06
    capture, whose worst real route measured 8.1/7.1 = **1.14x**, the rest within 1.01x  -  so 1.5
    clears every measured route and still rejects a lap of the ring); probe sample radii
    `BreachSampleRadius = 0.6f` / `ReplacementSampleRadius = 2.5f` (`:136-156`).
  - (!) **DO NOT CLEAR `_routeToUnitOpen` ON THE IN-RANGE BRANCH.** The code zeroes `_routeCheckAt`
    instead, with the reason in-line: clearing would make the boundary FLICKER  -  the instant the
    defender steps 0.1 m back out of range the refresh branch runs, the 0.5 s throttle returns early,
    and the troop reads the just-cleared `false`, swinging at the wall for half a second on every
    step-back in a brawl. That reads to the player as *"they didn't really fight"*.
  - **`private const CombatFaction SelfFaction = CombatFaction.Friendly`** `:381`  -  this troop's side
    declared ONCE. The explicit interface member (`IDamageableStructure.Faction`), the selection
    filter and the struct/unit classifier all read it and hand it to **`CombatFactionRules.MayAttack`**
     -  the sweep's old inline `dmg.Faction != CombatFaction.Hostile` is **gone** (see `core.md`
    Combat/). The `[Flow:TroopAI]` RETARGET line now also carries the gate's own verdict:
    `| preferUnit=<bool> route=<status>`  -  `preferUnit=False` with `route=PathPartial` beside a
    struct win is the wall still standing; `preferUnit=True` the tick after a segment dies is the
    breach being taken. **Both are one read.** Pinned by **`TroopTargetPreferenceRegression`**
    (`[troop-target-preference]`, marker `TROOP_TARGET_PREF_OK`).
- **TroopDeployer.cs** (107) — static spawn seam: `SpawnTroop(id,pos)` = TroopCatalog →
  TroopFactory → `TroopStatResolver.Effective(def, BarracksService.TroopLevel)` → Enemy-layer mask;
  `SpawnFromArmy(PlayerTroop,pos,stackIndex)` (:65) stamps `OwnedTroopId`, bakes veterancy ×
  Armorer-perk damage in ONE call + the health perk (WO-430, :79-86), spiral-ring stack offsets. **LIVE.**
- **TroopFactory.cs** (119) — the ONE skinned-troop builder (mirrors EnemyFactory). **TroopCatalog.cs**
  (95) + **TroopDef.cs** (81) — troops.json loader/model. **LIVE.**
- **ArmyReadiness.cs** (80) — **THE one army-readiness formula** (owner review 2026-08-01, WO-823):
  `Ready = deployable(healthy slots) + queued(train jobs) >= cap(Army.MaxArmySize)`; consumers
  (BuildTimerService.PublishArmyStatus, RaidSelectionScreen.Open, BarracksService.EnqueueTraining)
  must never re-roll it locally (:5-16). Null state ⇒ Ready=true (headless never-false-block,
  :55-58). Test seam overload (:73). **WIRED/LIVE.**
- **BarracksService.cs** (343) — live facade for the WO-771.9 progression on the WO-773 Obsidian
  queue (train/upgrade jobs, `CommittedTrainingSlots`, `TroopLevel`). **BarracksProgression.cs**
  (210) — the pure decision/mutation core. **TroopStatResolver.cs** (254) — pure effective-stat
  resolver + the two canonical-JSON loaders. **Data/BarracksData.cs** (~200) — barracks.json
  `BarracksDef` + troop-upgrades.json `TroopUpgradeDef`/`StatCurve`/`AbilityUnlock` records
  (reuses `ResourceCost` + BattleATB `StatusKind`; dual-copy rule, :13-16). **WIRED/LIVE.**
- **TroopUnlock.cs** (81) — the SINGLE unlock-gate authority (WO-733). **BarracksUnlock.cs** (55)
  — the WO-724 Barracks-building unlock rule. **TroopDialogueCommands.cs** (112) — Yarn ↔ army seam
  (also owns `SlotOf(troopDefId)`, the slot-cost map ArmyReadiness folds over). **LIVE.**
- **TroopRecoveryService.cs** (118) — WO-781: the previously-MISSING caller of
  `ArmyStorage.AdvanceRecovery` (wounded troops now actually heal): DDOL singleton ticking
  cold-load catch-up (Start), mobile resume (OnApplicationPause), ~1/sec live (Update), all off
  `TimeSource.NowUnixMs`; saves + fires `CombatChanged` only on a real heal (:94-109). This CLOSES
  the "TickRecovery uncalled" P0 from the raid-spine audit. **WIRED/LIVE.**
- **TroopRally.cs** (34) — the one global rally flag/point. **LIVE.**
- **RaidDeployController.cs** (652) — the raid command HUD + tap state machine (DEPLOY tray /
  RALLY toggle / RETREAT with confirm), self-installs into `RaidBase_*` enemy-owned scenes
  (:114-130); deploys via `TroopDeployer.SpawnFromArmy`; retreat reconciles survivors vs wounded
  (recoverySeconds 120, :70) and exits via `SceneRouter.GoCastle`. Code-built uGUI, New Input
  System + Lean tap. **WIRED/LIVE.**
  - **DELTA 2026-09-06 (WO-1463)  -  the rally flag was MAGENTA in the player's build, and `TintRenderer`
    is retired.** `GameObject.CreatePrimitive` assigns Unity's built-in `Default-Material`, which has
    **no URP variant**: fine in the editor, magenta error shader in a URP PLAYER build, and setting
    `.color` on it changes nothing. Proof: `Logs/device/screens/owner-raid-ui-2026-09-06-143701.png`.
    `BuildRallyFlag` now calls **`ApplyUrpMaterial`**, which goes through the project's ONE runtime
    material authority  -  **`MagentaGuard.BuildUrpLitMaterial`** (resolves URP/Lit and, when the shader
    was stripped from the build, BORROWS it off a live scene material rather than degrading to
    Standard; `MagentaGuard.cs:814-869`). It **returns NULL rather than lying**, so the caller leaves
    the renderer alone **and WARNS**  -  a silent fallthrough is what S12 forbids.
  - STOP: **`MagentaGuard.ProtectPrimitiveArt(root, ...)` IS NOT OPTIONAL.** MagentaGuard's scene sweep
    treats a built-in primitive mesh as a stray placeholder and **DISABLES** it
    (`IsPrimitivePlaceholder`), so deliberate primitive art must register  -  otherwise the fix for the
    magenta flag becomes an **invisible flag**. Pinned by `RaidSelectionLayoutRegression` case
    `S8:no-bare-primitive`. Same pattern as `Editor/WallTools/RaidBaseGenerator.ApplyUrpMaterial` and
    `Village/World/DungeonWorldPortalSpawner.BuildArch`.
  - (!) **Neither tone is a new creative choice** (the owner is colourblind): the banner takes the kit's
    canonical `ElarionUi.Gilt` (`Core/UI/ElarionUi.cs:60`), the pole keeps the dark-wood literal it
    always shipped with. WO-1463 changed **HOW** the colour is applied, not **WHICH** colour it is.
- **RaidScoring.cs** (328) — WO-771.6 V1 scorer: 180 s clock (:50), destruction % from
  RaidGarrisonSpawner alive/total, pure `ComputeStars` (cleared / boss down / under clock) + pure
  `ComputeLoot` (crystals/food scaled by stars + destruction), `OnTimeExpired` once; self-installs
  per RaidBase scene. **RaidResult.cs** (42) — settled outcome record. **RaidDeployLog.cs** (54) —
  re-watch deploy record. **RaidHudController.cs** (203) — the live raid HUD (WO-771.11). **WIRED/LIVE.**

---

## WORLD — spawners, streaming, zones (`Village/World/`, 50 files)

### Standing context (WO-608 MergedWorld)
The castle + overworld are MERGED into the single scene **`Main_Castle_Overworld`**; the separate
`OuterWorld` scene is REMOVED. **WorldSceneLoader.cs** (157) is **DEPRECATED** — kept only for
compatibility (header :1-9); overworld detection is `DeNelle.Core.HubScenes.IsOverworld`. Its
DEF-108 `DiagTerrain` dump (:64-90) still runs on overworld load (see FLAGS). `Village2` = the raid
target scene (Village2RaidController); `Village.unity` abandoned.

### RegionMobSpawner.cs (583 lines)
WO-155 ambient roaming population around the player: region-appropriate (RegionSpawnTable +
ZoneManager), threat-scaled, red-skull telled, WO-316 family packs, BestWave-ramped
(EarlyTargetFloor 2 → TargetPopulation 8), leash/aggro/cull/wander maintenance; base stats for
orc-raider now come from **WildlandsRoster.BaseDef** (:566). Gated on **`ff.regionroam`** at
Bootstrap (:114-121) — flag **default ON** (owner reversal 2026-07-30 F8 seq511 "missing enemies in
the world"; FeatureFlags.cs:162). ⚠ The in-file comment still describes the 2026-07-26 "OFF by
default" ruling — STALE (see FLAGS). **WIRED/LIVE.**

### TribeManager.cs (446 lines)
WO-160 wandering tribes: persistent `TribeState` records (GameState.Tribes) that materialise within
ActivationRadius and write members-remaining/cleared back on despawn (hysteresis); active members
raze undefended Settlements (WO-159); raid size randomised in the region threat band. DDOL
singleton; reuses Enemy/Configure + ZoneManager. **WIRED/LIVE.**

### WardTetherService.cs (593 lines) + WardStone.cs (169)
WO-112 ward-tether: seeds the code-built ward ladder (2 Goldfields / 3 each other march), builds
WardStones at runtime, computes per-march reach (pure Core `WardReach`), drives the reversible
"forgetting" past the furthest lit ward (never damage/timer/wall), runs the relight interaction
(3 m + rising cost + an 18 s hold-the-ward KINDLE trickle), claims the node-ward's MineNode.
⚠ **Off-SSOT stats:** the kindle-trickle enemy defs are INLINE code blocks (:481-502 — e.g.
orc-raider hp 55, caveman 48, feral-wolf 34, tiefling-cultist 52, necromancer 85, × threat scale)
that do NOT route through WildlandsRoster/enemies.json (canonical orc-raider base Hp = **130**,
WildlandsRoster.cs:149 / enemies.json) — a kindle raider is ~2.4x softer than the same id anywhere
else. See FLAGS. **WIRED/LIVE.**

### WildlandsRoster.cs (151 lines)
The SSOT for overworld Wildlands BASE stat blocks (the fix for the Check-H divergence: orc-raider
Hp 95 vs 170 across four spawners). `BaseDef(id)` resolves from enemies.json (read once) with a
byte-matching code fallback (orc-raider Hp 130, :139-150); `Owns(id)` currently owns ONLY
"orc-raider" (:56-64). Consumers: RegionMobSpawner, CampDefenseWave, CampGuards, EnemyOutpost,
GarrisonStatBlocks (all at their "orc-raider" cases). **WIRED/LIVE** (single-id scope — see FLAGS).

### SceneTransitionTrigger.cs (655 lines)
Gate/portal seam. PROXIMITY-based (NavMeshAgent hero can't trip cross-scene triggers; radius default
6 m, :39), default target `Main_Castle_Overworld` (:28). **CONFIRM-TO-CROSS is the ONLY behaviour**
(owner 2026-06-18): auto-teleport is gone; travel always requires the "Travel to <destination>" tap
(:51-59 — the serialized `requireConfirm` is retained for deserialization only and IGNORED at
runtime). `promptOverride` / `suppressPrompt` for narrative portals / passive seams. SEAMTRACE
`_minDist` diagnostics still in (:65-74). **WIRED/LIVE.**

### DungeonWorldPortalSpawner.cs (597 lines)
WO-165/DEF-188 discoverable dungeon portals. Placement is now an **AUTHORED world-position table**
(owner ruling 2026-07-13 "visible from castle but a little walk" — the old random region fan could
silently place nothing; :16-23), NavMesh-seated, fog-of-war reveal (DiscoverRadius 26 m, dim 0.12,
PlayerPrefs-persisted), reuses DungeonPortal (glow + [F] + `LoadScene("Dungeon_"+id)`) and
DungeonDef Resources else the inline HealersCottage/FolksGranary fallback. DDOL. **WIRED/LIVE.**

### MineNodeVisual.cs (317 lines)
One visual source of truth for ALL MineNodes: tries a real Resources prop first (VisualFactory.Skin
with **SeatFlat** bounds-derived orientation — the hand-authored OffsetForge euler that laid props
on their side is retired, :54-58), else builds a distinct procedural silhouette per resource
(log / ore boulder / grain mound / emissive crystal). Auto-attached by MineNode; visual-only.
**WIRED/LIVE.**

### World support (brief, per-file)
- **Resource loop:** MineNode.cs (532, [F]-harvest node, banks into GameState),
  CrystalMineNode.cs (453, WO-153 persistent upgradeable crystal faucet), RareCrystalSpawner.cs
  (267, WO-154 timed rare spawns), HarvestSite.cs (372), NodeDiscoverySystem.cs (347, DEF-189
  fog-of-war-lite), PetHarvestBootstrap.cs (222, village nodes for the starter pet),
  ResourceGainPopup.cs (71). **LIVE.**
- **Settlements:** Settlement.cs (236, WO-159 node-claiming outpost) + SettlementPlacer.cs (174,
  the claim verb) + OutpostHub.cs (337) / OutpostDefender.cs (158) (claimed-outpost defense grid +
  recruitable troops). **LIVE / Present.**
- **Castle/nav/geometry:** CastleMoatBuilder.cs (1,493 — moat + 4 stone bridges), MoatExclusion.cs
  (59, no-enemy-in-moat spawn guard), MoatWaterShimmer.cs (145), FishSchool.cs (182, WO-590),
  StairwayBuilder/StairwayStructure/StairNavLink, RampartNavLinkInstaller/RampartLiftInstaller +
  LiftPlatform (Elden-Ring lift, task #8), WallNavObstacleInstaller.cs (152, DEF-224 wall-carve
  backstop) + CastleWallNavObstacleInstaller.cs (401, task #14 in-wall colliders),
  CastleBeamHider.cs (99), SeatOnGroundOnStart.cs (338), WorldFeelInjector.cs (376),
  GateTraversalInjector.cs (214, walk-through-gate nav), HomeReturnPortalInjector.cs (227, WO-602
  the way back home), OutpostConnectorConfirmInjector.cs (105), OutpostMaterialFixInjector.cs (84),
  CavePortalRepointInjector.cs (158 — with `ff.raidwalk` OFF it NEUTRALIZES every overworld outpost
  seam, :123-153). **LIVE (runtime, non-destructive).**
- **Scene plumbing:** SceneConfigCatalog.cs (181, scene-configs.json reader),
  SceneLinkResolverHost.cs (200, ISceneLinkResolver host), WorldMusicDirector.cs (101),
  GateIntelHud.cs (235, DEF-152 gate intel label), SafeZoneRecovery.cs (134, full HP+MP in safe
  zones), ~~BreakableContainer.cs (155, destructible loot props on the Enemy-layer sweep)~~
  **[SUPERSEDED 2026-08-21 — see the DELTA at the top of this file: it is a 482-line loot CHEST,
  not a hostile, and the Enemy-layer rewrite is gone]**. **LIVE.**

---

## WORLD/CAMPS — clear→claim→build + the raid estate (`World/Camps/`, 23 files)

### Standing truth: walk-to raids are RETIRED (WO-771 LOCK, 2026-07-26)
`ff.raidwalk` (**RaidContinuousWalk**) defaults **OFF** (FeatureFlags.cs:88). The raid loop is
RaidSelectionScreen → Teleport/Deploy → `RaidBase_*` scene. Consequences in this folder:
- **RaidOutpostSystem.cs** (236) — `_enabled = FeatureFlags.Raid && FeatureFlags.RaidContinuousWalk`
  (:56) ⇒ **the 4 cardinal walk-to EnemyOutposts DO NOT SPAWN by default** (Raid is ON, raidwalk is
  OFF). The old hardcoded-ON note in the previous catalog is obsolete. Anchors ±70 m E/W/N/S
  (:95-101), `CardinalOutpostCount=4`, staggered realize. **DORMANT by flag; intact.**
- **CavePortalRepointInjector** neutralizes the walk-up outpost portals when raidwalk is OFF;
  **ChallengeOutpostVictoryController.cs** (170) and **OutpostVictoryController.cs** (226, WO-449
  continuous-walk victory) are likewise flag-parked. **DORMANT by flag.**
- The LIVE raid-estate path: **RaidGarrisonSpawner.cs** (334 — runtime garrison for the
  config-generated `RaidBase_<id>` scenes RaidBaseGenerator bakes; the clear signals RaidScoring
  reads), **RaidVictoryController.cs** (345 — clear → claim → next companion → return home),
  **RaidClaimService.cs** (76 — persisted claimed-base set), plus Troops/RaidDeployController /
  RaidScoring / RaidHudController. **WIRED/LIVE.**

### CampSystem.cs (175 lines) — clear→claim→build loop flag + bootstrap
`DefaultEnabled` dark (`#if DOTR_CAMPS`) but `_enabled = true` **still hardcoded ON** (owner
2026-06-03, :56-58). Spawns 4 ClaimableCamps at cardinal anchors ±95 (:91-97) in the outer world.
`DefaultKillsRequired=6`, `DefaultCampRadius=9`. **WIRED/LIVE (flag forced ON).**

### EnemyOutpost.cs (840 lines)
The walk-to outpost body: WOOD fort (OutpostFoundationGenerator via StructureFactory) + boss-led
garrison (BaseGuardCount 5 + threat, MiniBoss-role EnemyBrain boss, stand-ring tethers), clear →
flat reward (BaseClearCrystals 40 / BaseClearXp 120, :67-69) + threat-scaled loot; PlayerPrefs
persistence; `ConfigureArena(...)` reuse for the async-PvP arena (WO-389 defenders,
suppressed open-world loot); orc-raider guards source their base from WildlandsRoster (:890).
Spawned only by RaidOutpostSystem ⇒ **currently dormant with it** (arena reuse still live).

### GarrisonController.cs (412 lines)
Runtime brain on an additively-loaded `Garrison_*` scene: `Activate()` spawns the recipe-driven
garrison at authored spawn points via EnemyFactory (optional `enemyPrefabs[]` round-robin);
`CleanupAndUnload()` tears the scene down; battle music via CoreServices. Stat blocks + level scale
EXTRACTED to **GarrisonStatBlocks.cs** (160 — shared with the config-driven RaidGarrisonSpawner;
orc-raider base from WildlandsRoster at :128). **Present — additive-garrison path.**

### Camp support (brief)
ClaimableCamp.cs (593 — Hostile→Cleared→Claimed lifecycle, PlayerPrefs), CampGuards.cs (226 —
tethered guard pack, `AllCleared`), CampDefenseWave.cs (290 — post-build counterattack),
Outpost.cs (239 — player-built structure, auto-harvest trickle, IDamageableStructure),
OutpostFoundationGenerator.cs (370), CampVisual.cs (94), CampPromptUI.cs (240) + CampPromptVM.cs
(98, pure VM) + CampProximityService.cs (115) (MVVM Silo G split), EchoAutoDeployTrigger.cs (191,
WO-360) + EchoTutorialUI.cs (163), GarrisonTurretArmer.cs (70 — shared Watchtower_* prop→turret
armer), Village2RaidController.cs (321 — Village2 as playable raid destination, WO-433). **LIVE
(via CampSystem) / raid-estate LIVE.**

---

## CORE/ENEMIES — taxonomy + resolver (WO-772 Phase 1, ratified 2026-07-26)

### EnemyTaxonomy.cs (~125 lines) `DeNelle.Core.Enemies`
Pure data model: `EnemyFaction` (**HollowOnes** implemented / **Wildlands** RESERVED STUB, no
members, deferred to Phase 2 / **Boss**), `EnemyClass` (Id, RoleKey, ModelKey, Variant,
AnimatorRig-as-metadata, CombatSpawnable, Equip), `EnemyEquipParts` (armor part keys + weapon key —
SCHEMA only in Phase 1, nothing attaches yet), `EnemyFamily`, `ResolvedEnemyModel`. No UnityEngine
dependency — headless-testable. **DATA/LIVE.**

### EnemyResolver.cs (345 lines) `DeNelle.Core.Enemies.EnemyResolver` (static)
THE id → family → class → DISTINCT model authority (fixes "distinct ids collapse to the same
generic skeleton"). Key surface:
- **⛔ CORRECTED 2026-08-20 (`577bde576`) — TWO HOLLOW ROWS LEFT `Skeleton_Minion`.**
  `hollow-walker → **Hollow_Walker**` and `cellar-hollow → **Cellar_Hollow**`, both owner-delivered
  AccuRig bodies. **`cellar-hollow` also LOST its `Variant "cellar"`** (the kneel-rock sorrow idle):
  the owner's read for that body is *"a tanky type or barbarian ish type"*, and a kneeling mourner is
  the opposite, so `Variant` is now **null**. Wiring a new Hollow body is **five edits in four
  files**, not a data edit — `KnownHollowModels`, `CommittedModels`, the `HollowTable` row
  (ModelKey + AnimatorRig), `EnemyAnimatorFactory.RigFor`, and
  `EnemyResolverRegression.ExpectedBaseModel` — plus `enemies.json` (BOTH copies). A row naming art
  outside `KnownHollowModels` is **inert and RED at the same time**: it silently keeps spawning
  `Skeleton_Minion` while the regression fails the tree. **And the one easiest to miss:** both bodies
  go into **`EnemyFactory.AccuRigIntake`** — they are `CC_Base` **+X-forward** exports and would
  otherwise spawn turned 90°. They belong there **despite** routing to the SkeletonHumanoid rig,
  which is exactly the distinction that set draws: **rig class = which CLIPS a body plays; the intake
  = which way its mesh FACES.** The KayKit `Skeleton_*` bodies share that controller and face **+Z**.
  Roster after the change: `REGRESSION_OK 227/227, 0 red`. The six rows below still on `Skeleton_*`
  are the remainder of WO-954's owner-pinned re-skin.
- `HollowTable` (:64-188): walker→**Hollow_Walker** *(was Skeleton_Minion)*, warrior→Skeleton_Warrior
  (Phase-2 weapon key
  sword_A), rogue→Skeleton_Rogue, acolyte→Skeleton_Healer, mage→Skeleton_Mage,
  reaper→Skeleton_Warrior+variant, brute→Skeleton_Golem, cellar-hollow→**Cellar_Hollow**, no variant
  *(was Skeleton_Minion+variant)*,
  necromancer→Necromancer (wave boss), hollow-apprentice→Skeleton_Mage+variant (mini-boss), the
  dungeon underscore aliases (hollow-villager-a/b, apprentice-minor, healer — `Norm()` folds
  `_`→`-`, :231), and **alduin** (Necromancer variant, `CombatSpawnable=false` — dialogue NPC).
- `ApprovedHollowCombatIds` (10, :192-197) — the distinctness-oracle roster.
- **Wildlands deferral:** `IsCombatApproved(id)` false for orc-raider/caveman/feral-wolf/
  tiefling-cultist (:248-261); `SubstituteHollowId(id, role, height)` → hollow-warrior (heavy) or
  hollow-walker (:271-277). The shipping Orc Warband + bosses stay approved.
- `TryResolveHollowModel(id, dataModelKey, out model)` (:304) — the EnemyFactory hook; enemies.json
  `modelKey` WINS but only to a KNOWN committed mesh (:49-58). `Resolve(id)` (:321) full identity;
  `FactionForFamily` (:282). Lint marker `ENEMY_RESOLVER_OK`. **WIRED/LIVE.**

---

## DATA JSON (dual-copy: `Assets/Resources/Data/Canonical/` wins at load; keep byte-identical with `Assets/StreamingAssets/Data/Canonical/`)

### enemies.json — 16 authored ids
hollow-walker/-warrior/-rogue/-acolyte/-mage/-reaper/-brute, cellar-hollow, hollow-apprentice,
necromancer, orc-berserker, orc-shaman, orc-necromancer, **orc-raider** (base Hp 130 — the
WildlandsRoster catalog-of-record entry), troll, ogre. Schema per def: id, hp, moveSpeed,
contactDamage, attackInterval, ai, height, family, role, aggroRadius, xpReward, glimmerReward,
coinReward, movement (→IsFlying), modelKey (A4 — resolver override), flavor. Loaded via
`WaveDataLoader.LoadEnemiesAsync` → `EnemyCatalog`.

### waves.json — 20 waves, **NO enemies[] batches** (stripped 2026-07-30, commit 7f1f1e6a)
Per wave: waveId, name, countdownSeconds; ground bosses at waves 5 (troll, `bossHp` 1050 — WO-789),
6/12/18 (necromancer); `apexBoss` (dragon) at wave 20; plus the endless block. Rosters are
GENERATED by smart composition; the authored design intent lives in
`docs/design/WAVE_AUTHORING_REFERENCE_2026-07-30.md`; `WaveAuthoringLiveRegression`
(`WAVE_AUTHORING_OK`) guards against re-adding batches while `_smartComposition` is on.

---

## FLAGS / RISK LEDGER (verified 2026-08-02)

### Stale comments vs. code (comments lie — trust these corrections)
1. **WaveManager.cs:1350-1381** — `WarnAuthoredBatchesDiscarded` doc says "OPEN OWNER RULING
   (WO-783)" and "today 19 waves / 55 batches / 148 enemies are thrown away". STALE: the ruling
   CLOSED 2026-07-30 (smart composition = authority; batches stripped; oracle guards). The warn
   itself now never fires (wave.Enemies is empty).
2. **RegionMobSpawner.cs:114-121 (+ the Update belt)** — comments assert "ambient roaming OFF by
   default (owner 2026-07-26)". STALE: `ff.regionroam` default flipped back **ON** 2026-07-30
   (FeatureFlags.cs:162, F8 seq511). The gate logic is fine; the prose is wrong.
3. **OverworldEncounterSpawner.cs:19-21** — "FLAG-GATED … default OFF". STALE: `ff.overworldencounter`
   default **ON** since 2026-07-30 (FeatureFlags.cs:154).
4. **EnemyDamageable.cs:23-25** — header claims "ApplyStatus is a logged no-op". STALE: it records
   into a live `CombatStatusTracker` (IsFrozen/IsSlowed/IsBurning + CollectActive, :71,:94-101,:142-146).
5. **RaidOutpostSystem.cs:10-11** — "ENABLED for testing". STALE: `_enabled` now derives from
   `Raid && RaidContinuousWalk` (:56) ⇒ OFF by default.
6. **WorldSceneLoader.cs** — file body still logs "[WorldSceneLoader] DEBUG …" + runs DiagTerrain on
   every overworld load despite the DEPRECATED header; harmless but noisy (its own comment says
   "Remove once resolved").
7. **Enemy.cs:2-28 header** — still describes the Week-4 slice; the class has since grown pooling,
   rooted casts, hero-aggro, battle-locks and wall mitigation. Body comments are current; the
   header narrative is under-scoped.

### Feature-flag state (the truth table for this area, FeatureFlags.cs)
- `ff.raid` ON · `ff.raidwalk` **OFF** (WO-771 LOCK — teleport/deploy raid only; walk-to outposts +
  their victory/portal chain dormant) · `ff.regionroam` ON (reversal 2026-07-30) ·
  `ff.overworldencounter` ON (reversal 2026-07-30) · `ff.enemyinjured` ON · `ff.enemyrootedcast` ON ·
  `ff.enemystructureaware` ON · `ff.enemyweapons` **OFF** (owner F8 2026-07-04 — no held weapon
  until one grip is perfected in the Offset Forge) · `ff.devhotkeys` OFF (gates the J/K/B test
  spawner).
- **CampSystem `_enabled = true` hardcoded** (owner 2026-06-03) — the one remaining
  "ships-dark-but-forced-on" in this area.

### Off-SSOT / balance-drift
- **WardTetherService.BuildKindleDef (:481-502)** — inline kindle stat blocks (orc-raider hp 55 …)
  bypass WildlandsRoster/enemies.json (canonical orc-raider base 130). The kindle is deliberately
  soft, but the id-collision means the divergence oracle's "one id, one base" rule does NOT hold
  for kindle spawns. Route through `WildlandsRoster.BaseDef` × a kindle-ease multiplier, or rename
  the kindle ids.
- **WildlandsRoster.Owns covers ONLY "orc-raider"** (:56-64). caveman / feral-wolf /
  tiefling-cultist / troll-family blocks are still per-spawner switches (RegionMobSpawner /
  EnemyOutpost / CampGuards / CampDefenseWave / GarrisonStatBlocks) — unified only for the one
  oracle-guarded id; the rest can still drift.
- **WO-789 `bossHp` pin + apexBoss.hp** are the two sanctioned wave-level exceptions to the
  enemies.json stat SSOT (Enemy.OverrideMaxHp:663; pool-safe).

### In-flight / watch
- **PlayerAttackController basic-attack impact burst (:586)** — still fires
  `Weaponskillsword_Impact` for non-elemental weapons at HEAD b77a178e (owner-picked red flash,
  d888b278). The 2026-08-02 removal (rocks-on-swing F8) is in a parallel silo, not yet landed;
  the `VFXManager.PlayKey` success trace (b77a178e) is its proving line. Re-verify this entry after
  the commit lands.
- **Deferred Wildlands art (Phase 2)** — the redirect gate means orc-raider/caveman/feral-wolf/
  tiefling-cultist spawn as Hollow substitutes everywhere; region variety is intentionally reduced
  until the Orc_Berserker rig/material fix ships.
- **Ward/World/Tribe persistence** — `WardStoneState`/`TribeState`/`SettlementState` ride
  GameState (Wards/Tribes/Settlements); confirm current SaveSchema coverage with the save owner
  before relying on cross-session ward/tribe state (historically in-memory only; EnemyOutpost/
  ClaimableCamp still use PlayerPrefs by design).
- **Two aggro authorities remain intentionally additive:** Enemy's self-contained DEF-224 hero
  aggro (brain-less bodies) vs EnemyBrain targeting (brain wins via DriveNav priority 1). Respect
  the precedence when editing either; the F8-38 `_casting` hold and the WO-419 stoppingDistance
  tighten both live in DriveNav — regressions there present as "caster walks while channeling" /
  "enemy parks 2.5 m out and never hits".
- **Dead code:** RegionMobSpawner's private `ModelForRoamer` remains unused (model mapping is
  EnemyFactory.ModelForEnemy's); WaveManager's placeholder-capsule enemy path is superseded by
  EnemyFactory but kept for pre-prefab testability.

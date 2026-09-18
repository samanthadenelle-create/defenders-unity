// =============================================================================
// RaidGarrisonSpawner — the runtime garrison spawner for a CONFIG-GENERATED raid
// base (the RaidBase_<id>.unity scenes RaidBaseGenerator.BuildFromConfig bakes).
// -----------------------------------------------------------------------------
// Assembly: DeNelle.Village   Namespace: DeNelle.Village.World.Camps
//
// THE GAP THIS CLOSES: a baked RaidBase_<id> scene has walls + Watchtower_* towers
// + a BossSpawn marker but spawns NO defenders. This component (attached to the
// RaidBase_<id> root by RaidBaseGenerator with the config id stored) reads the
// garrison block from scene-configs.json and, one frame after Start, spawns:
//   * the BOSS at the BossSpawn marker (MiniBoss brain, x3 HP / x1.5 dmg, min
//     height 2.6) — mirrors EnemyOutpost.SpawnBoss.
//   * the composition[{enemyId,count}] guard ring at baseRadius * 0.5 inside the
//     perimeter, NavMesh-snapped, scaled by player level + difficultyMultiplier,
//     staggered 1-2/frame so an Extreme garrison doesn't hitch on mobile.
//
// KEY CATCH (verified): the baked scene name (RaidBase_<id>) does NOT match the
// config's sceneName, so SceneOwnership can't resolve it by name — this spawner
// carries a STORED configId and calls SceneOwnership.SetEnemyOwned(true) itself so
// the turret-armer + the home-hub death-retreat treat the raid as enemy-owned.
//
// SPAWNING IS NOT REINVENTED (CLAUDE.md §9). Every defender is built through the
// ONE canonical path: EnemyFactory.Build -> Enemy.Configure -> SetBrainTarget(anchor)
// (HOLD the garrison; hero aggro still pulls them into the fight via TargetManager).
// Stat blocks + level scale come from the SHARED GarrisonStatBlocks; the towers are
// armed by the SHARED GarrisonTurretArmer — no duplication with GarrisonController.
//
// Enemies are Hostile by default (no faction code). LogWarning + degrade, never
// crash. Canon: the village is Elarion (never Avalon). ASCII-only runtime strings.
// =============================================================================

using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using DeNelle.Core.Diagnostics;   // TGVRU — FlowTrace/Guard on the boss + guard spawn path
// EnemyDef / Enemy / EnemyFactory / EnemyBrain / EnemyRole all live in the parent
// namespace DeNelle.Village, visible here (DeNelle.Village.World.Camps nests under it).

namespace DeNelle.Village.World.Camps
{
    /// <summary>
    /// Lives on a baked <c>RaidBase_&lt;id&gt;</c> root. Spawns the config's garrison
    /// (boss + composition) scaled by player level on Start, and arms the authored
    /// Watchtower_* props. Spawning routes through the canonical
    /// <see cref="EnemyFactory"/> path — no parallel spawner.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class RaidGarrisonSpawner : MonoBehaviour
    {
        // -- Builder-wired (RaidBaseGenerator sets this) -----------------------

        [Header("Config")]
        [Tooltip("scene-configs.json id this raid base was generated from (NOT the scene " +
                 "name — the baked scene is RaidBase_<id>, which won't match sceneName).")]
        [SerializeField] private string configId;

        [Header("Tuning")]
        [Tooltip("Hard cap on simultaneously-spawned live defenders (boss + guards) so an " +
                 "Extreme composition can't flood the scene / hitch mobile.")]
        [SerializeField] private int liveCombatantCap = 36;

        [Tooltip("Boss HP multiplier on top of the level/difficulty-scaled stat block.")]
        [SerializeField] private float bossHpMult = 3f;
        [Tooltip("Boss contact-damage multiplier on top of the level/difficulty-scaled stat block.")]
        [SerializeField] private float bossDamageMult = 1.5f;
        [Tooltip("Minimum boss body height (so the boss reads bigger than its guards).")]
        [SerializeField] private float bossMinHeight = 2.6f;

        [Header("Garrison turrets")]
        [Tooltip("Range (world units) for an armed garrison watchtower turret.")]
        [SerializeField] private float turretRange = 16f;
        [Tooltip("Damage per shot for an armed garrison watchtower turret.")]
        [SerializeField] private float turretDamage = 8f;
        [Tooltip("Shots per second for an armed garrison watchtower turret.")]
        [SerializeField] private float turretFireRate = 0.8f;

        // -- Runtime state -----------------------------------------------------

        /// <summary>Living defenders remaining (0 once the garrison is wiped).</summary>
        public int AliveCount => _aliveCount;
        /// <summary>Total defenders this garrison spawned (boss + guards).</summary>
        public int TotalGarrison => _garrison.Count;
        /// <summary>True once the whole garrison is dead (the raid base is clear).</summary>
        public bool Cleared { get; private set; }
        /// <summary>Raised once every defender is dead (the garrison is cleared).</summary>
        public event System.Action<RaidGarrisonSpawner> OnCleared;

        private readonly List<Enemy> _garrison = new List<Enemy>();
        private Transform _garrisonRoot;
        private int _aliveCount;
        private bool _activated;

        // -- ATMOSPHERE (WO-1868) ----------------------------------------------
        // Ground fog + storm clouds are POOLED VFXManager loops (same facade as every
        // other looping effect in this project — CLAUDE.md anti-duplication), so the
        // handles must be tracked and explicitly stopped: see OnDestroy.
        private readonly List<VFXHandle> _atmosphereFx = new List<VFXHandle>();

        // -- SPIRE ALARM (WO-1830) ---------------------------------------------
        // The brains this spawner built, kept alongside _garrison so the alarm fan-out never needs
        // a FindObjectsByType scan (the brain is already in hand at SpawnBoss/SpawnGuard).
        private readonly List<EnemyBrain> _brains = new List<EnemyBrain>();
        private bool _alarmRaised;
        private Vector3 _alarmSpirePos;

        /// <summary>Radius (m) of the ring alarmed defenders rally onto around the spire.</summary>
        [Header("Spire alarm (WO-1830)")]
        [Tooltip("Ring radius (m) around the spire that alarmed defenders converge onto. A single " +
                 "point would make 30 bodies shove each other forever - the return-home arrival " +
                 "test is only ~2m.")]
        [SerializeField, Min(1f)] private float alarmRallyRing = 6f;

        /// <summary>True once the spire alarm has fired for this raid (WO-1830).</summary>
        public bool AlarmRaised => _alarmRaised;

        /// <summary>Count of brains this spawner is tracking for the alarm fan-out (WO-1830).</summary>
        public int TrackedBrainCount => _brains.Count;

        /// <summary>scene-configs.json id this base was generated from (empty if unset).</summary>
        public string ConfigId => configId;

        /// <summary>Test/inspect hook: set the config id in code before Start.</summary>
        public void SetConfigId(string id) => configId = id;

        private void Start()
        {
            // WO-1830 — subscribe BEFORE the activate coroutine's first yield, so a spire hit on
            // the very first frame of the raid is not missed while the garrison is still seating.
            RaidSpire.AlarmRaised += HandleSpireAlarm;
            StartCoroutine(ActivateRoutine());
        }

        private void OnDestroy()
        {
            // WO-1830 — LOAD-BEARING. RaidSpire.AlarmRaised is STATIC, so it outlives this scene:
            // a leaked handler would fan the NEXT raid's alarm out to this raid's destroyed brains.
            RaidSpire.AlarmRaised -= HandleSpireAlarm;

            for (int i = 0; i < _garrison.Count; i++)
                if (_garrison[i] != null) _garrison[i].Died -= HandleGarrisonDied;

            // WO-1868 — explicit teardown alongside the destroyed-host sweep (VFXManager.cs), so
            // the raid's ground-fog/storm-cloud loops never sit on the pool waiting for the sweep.
            for (int i = 0; i < _atmosphereFx.Count; i++)
                _atmosphereFx[i]?.Stop(true);
            _atmosphereFx.Clear();
        }

        // =====================================================================
        // SPIRE ALARM (WO-1830) — owner ruling: "when the player starts attacking the spire in a
        // raid an alarm goes off and all the defenders start walking to the base to protect it",
        // because today "they just sit inside there leash range".
        //
        // ONE subscription per raid, ONE fan-out over a list already in hand. No per-frame work
        // of any kind is added: the brains are collected at spawn time in Track().
        // =====================================================================

        private void HandleSpireAlarm(RaidSpire spire)
        {
            if (_alarmRaised) return;                 // first hit only; the spire also latches
            _alarmRaised = true;
            _alarmSpirePos = spire != null ? spire.WorldPosition : transform.position;

            int alerted = FanOutAlarm(_brains, _alarmSpirePos, alarmRallyRing, SnapToNav);

            // sec.12 — THE one alarm line: how many defenders were alerted, and where to.
            FlowTrace.Step("Raid",
                $"SPIRE ALARM config='{configId}' - {alerted} defender(s) alerted of " +
                $"{_brains.Count} tracked ({_aliveCount} alive), converging on the base at " +
                $"{_alarmSpirePos} (rally ring {alarmRallyRing:0.#}m).");
        }

        /// <summary>
        /// The alarm fan-out (PURE of MonoBehaviour state — the oracle drives THIS, rather than
        /// re-implementing the loop, which would certify only itself). Rallies every live brain in
        /// <paramref name="brains"/> onto <see cref="EnemyBrain.ComputeRallyPoint"/> around
        /// <paramref name="spirePos"/> and returns how many were actually rallied.
        ///
        /// <paramref name="snap"/> is the NavMesh snap (pass null in EditMode, where there is no
        /// NavMesh — the un-snapped ring point is still the right anchor for the assertion).
        /// A brain already inside the ring keeps its post, and ComputeRallyPoint owns that rule;
        /// it is still counted as alerted, because it WAS told.
        /// </summary>
        public static int FanOutAlarm(IList<EnemyBrain> brains, Vector3 spirePos, float ring,
                                      System.Func<Vector3, Vector3> snap)
        {
            if (brains == null) return 0;
            int alerted = 0;
            for (int i = 0; i < brains.Count; i++)
            {
                var brain = brains[i];
                if (brain == null) continue;
                Vector3 rally = EnemyBrain.ComputeRallyPoint(
                    spirePos, brain.HomeAnchor, ring, i, brains.Count);
                if (snap != null) rally = snap(rally);
                brain.RallyTo(rally);
                alerted++;
            }
            return alerted;
        }

        // =====================================================================
        // ACTIVATE — one frame after Start (so the additive scene's NavMesh is live),
        // read the config + spawn the garrison + arm the turrets. Idempotent.
        // =====================================================================
        private IEnumerator ActivateRoutine()
        {
            if (_activated) yield break;
            _activated = true;

            // Let the scene + NavMesh settle one frame (EnemyFactory snaps each spawn
            // to the nearest navmesh point, but the surface must be live first).
            yield return null;

            var def = SceneConfigCatalog.Find(configId);
            if (def == null)
            {
                Debug.LogWarning($"[RaidGarrisonSpawner] no scene-config '{configId}' — no garrison spawned.");
                yield break;
            }

            // WO-430 — apply this raid's modifier override (if authored) BEFORE anything spawns,
            // so troops + the garrison are born with the right perks ("before landing"). Empty →
            // clear the override, so the player's REAL upgrade tiers apply (the normal raid path).
            if (!string.IsNullOrEmpty(def.modifierOverride))
                DeNelle.Core.State.ModifierService.SetOverrideJson(def.modifierOverride);
            else
                DeNelle.Core.State.ModifierService.ClearOverride();

            if (!def.IsEnemy)
            {
                Debug.LogWarning($"[RaidGarrisonSpawner] config '{configId}' is not Enemy-owned — no garrison spawned.");
                yield break;
            }

            var g = def.garrison;
            if (g == null)
            {
                Debug.LogWarning($"[RaidGarrisonSpawner] config '{configId}' has no garrison block — no garrison spawned.");
                yield break;
            }

            // KEY CATCH: the baked scene name won't match the config's sceneName, so
            // SceneOwnership.Resolve read this scene Player-owned. Force it enemy-owned
            // (carry the STORED config id, not a scene-name match) so the turret-armer
            // arms + the home-hub death-retreat fires.
            SceneOwnership.SetEnemyOwned(true);

            // Raid BGM — driving brass for the assault (WO-453, brass-rampart.mp3).
            DeNelle.Core.CoreServices.Audio?.PlayMusic(DeNelle.Core.Audio.MusicTrack.Raid);

            // Player level (HeroProgression.Instance.Level — HeroProgression lives in the
            // parent DeNelle.Village namespace), fallback 1 if not yet up.
            int playerLevel = HeroProgression.Instance != null
                ? Mathf.Max(1, HeroProgression.Instance.Level)
                : 1;
            // WO-1763 - the two numbers that decide how hard this camp is now ride the
            // REMOTE rail, per camp (DeNelle.Village.RaidDifficultyTunables, the ONE reader
            // and the owner of the clamps). With no row, no network and no parse it answers
            // the AUTHORED values bit for bit, so an empty client_tunables table is exactly
            // the raid that shipped.
            //
            // ⛔ THE >0 GUARD STAYS AHEAD OF THE RESOLVE, not behind it. An authored 0 scaled
            // by a percent is still 0, and FoldDifficulty NO-OPS at <=0 - so a zero surviving
            // into the resolve would read as "difficulty applied" while applying nothing.
            float authoredDifficulty = g.difficultyMultiplier > 0f ? g.difficultyMultiplier : 1f;
            var diff = RaidDifficultyTunables.Resolve(configId, authoredDifficulty, g.levelOffset);
            int enemyLevel = Mathf.Max(g.baseEnemyLevel, playerLevel + diff.EffectiveLevelOffset);
            float difficulty = diff.EffectiveMultiplier;
            float ring = Mathf.Max(2f, def.baseRadius * 0.5f);
            int slotCount = CountGarrisonSlots();
            FlowTrace.Step("Garrison",
                $"seating={(slotCount > 0 ? "slots" : "ring")} count={(slotCount > 0 ? slotCount : 0)} " +
                $"config='{configId}' ring={ring:F1}m");

            _garrisonRoot = new GameObject("[RaidGarrison]").transform;
            _garrisonRoot.SetParent(transform, false);
            _garrisonRoot.localPosition = Vector3.zero;

            yield return SpawnGarrisonStaggered(g, enemyLevel, difficulty, ring);

            // Arm the authored Watchtower_* props (SHARED armer; ownership-guarded inside).
            int armed = GarrisonTurretArmer.ArmWatchtowers(
                gameObject.scene, turretRange, turretDamage, turretFireRate);
            if (armed > 0)
                Debug.Log($"[RaidGarrisonSpawner] '{configId}' armed {armed} watchtower turret(s) (EnemyOwned).");

            // WO-1868 (owner: "add a fog to the ground?" / "maybe storming clouds overhead") —
            // raid-arena-scoped atmosphere only; see SpawnAtmosphereFx for why this reuses the
            // existing pooled VFXManager loops instead of a new fog/skybox system.
            SpawnAtmosphereFx(def.baseRadius);

            if (_aliveCount == 0)
            {
                // R — spawned nothing: self-report (loud) instead of a silent auto-clear.
                FlowTrace.Fail("Garrison", $"'{configId}' spawned 0 living defenders " +
                               "(no navmesh under the base? empty composition?) — treating as cleared.");
                MarkCleared();
            }
            else
            {
                // WO-932 Phase 3: loud start line for playtest probes (garrison + objective).
                var spire = RaidSpire.Active != null ? RaidSpire.Active : FindAnyObjectByType<RaidSpire>();

                // WO-1763 - the SAME single line carries both difficulty numbers and where each
                // came from. ONE line, and the five grep tokens (config= garrisonAlive=
                // enemyLevel= difficultyx spire=) are spelled exactly as they were.
                //
                // Every fragment is computed into a LOCAL first, deliberately: a nested quote
                // inside an interpolation hole is the shape CLAUDE.md section 1 records as the
                // thing the compile gate's brace scanner cannot model (a 210/210 file reading
                // 175/174 at the gate, which withholds COMPILE_GATE_OK).
                string spireNote = spire != null ? spire.MaxHp.ToString("0") + "hp" : "NONE";
                string offsetNote = diff.OffsetSource == RaidDifficultyTunables.SourceJson
                    ? "(json offset " + g.levelOffset + ", src=" + diff.OffsetSource + ")"
                    : "(json offset " + g.levelOffset + ", remote offset " +
                      diff.EffectiveLevelOffset + ", src=" + diff.OffsetSource + ")";
                string difficultyNote = "(json " + authoredDifficulty.ToString("F2") + " x " +
                                        diff.MultiplierPct + "%, src=" + diff.MultiplierSource + ")";
                FlowTrace.Step("Raid",
                    $"RAID START config='{configId}' garrisonAlive={_aliveCount} " +
                    $"enemyLevel={enemyLevel} {offsetNote} " +
                    $"difficultyx{difficulty:F2} {difficultyNote} " +
                    $"spire={spireNote} " +
                    $"scene='{gameObject.scene.name}'.");
                FlowTrace.Step("Garrison", $"'{configId}' garrison spawned: {_aliveCount} defender(s), " +
                          $"enemyLevel {enemyLevel} (player {playerLevel}), difficulty x{difficulty:F2}, ring {ring:F1}m.");
            }
        }

        // =====================================================================
        // SPAWN — boss this frame, then the composition guard ring 1-2/frame. The
        // empty-garrison handling lives in ActivateRoutine after this returns.
        // =====================================================================
        private IEnumerator SpawnGarrisonStaggered(GarrisonDef g, int enemyLevel, float difficulty, float ring)
        {
            // The BOSS holds the BossSpawn marker (MiniBoss role), counts toward the cap.
            SpawnBoss(g, enemyLevel, difficulty);
            yield return null;

            // Expand the composition into a flat id list, capped to the live budget
            // (the boss already took one slot). Hostile by default — no faction code.
            var ids = ExpandComposition(g);
            int budget = Mathf.Max(0, liveCombatantCap - _aliveCount);
            int toSpawn = Mathf.Min(ids.Count, budget);

            for (int i = 0; i < toSpawn; i++)
            {
                if (this == null || _garrisonRoot == null) yield break;

                SpawnGuard(ids[i], i, toSpawn, enemyLevel, difficulty, ring);

                // Stagger 1-2 units/frame so an Extreme garrison doesn't hitch on mobile.
                if ((i & 1) == 1) yield return null;
            }
        }

        // Flatten composition[{enemyId,count}] into a per-unit id list (count>0 guarded).
        // WO-932 Phase 4: also append eliteCount copies of the strongest composition id
        // (or boss id) so the authored eliteCount field is no longer dead data.
        private List<string> ExpandComposition(GarrisonDef g)
        {
            var ids = new List<string>();
            if (g == null || g.composition == null) return ids;
            for (int i = 0; i < g.composition.Count; i++)
            {
                var entry = g.composition[i];
                if (entry == null || string.IsNullOrEmpty(entry.enemyId)) continue;
                int count = Mathf.Max(0, entry.count);
                for (int c = 0; c < count; c++) ids.Add(entry.enemyId);
            }

            // eliteCount lives on SceneConfigDef, not GarrisonDef — read from catalog.
            int elites = 0;
            string eliteId = null;
            Guard.Try("Garrison", "resolve eliteCount for " + configId, () =>
            {
                var cfg = SceneConfigCatalog.Find(configId);
                if (cfg == null) return;
                elites = Mathf.Max(0, cfg.eliteCount);
                if (elites <= 0) return;
                // Prefer last composition entry (often the heavier unit), else boss.
                if (g.composition != null && g.composition.Count > 0)
                {
                    for (int i = g.composition.Count - 1; i >= 0; i--)
                    {
                        var e = g.composition[i];
                        if (e != null && !string.IsNullOrEmpty(e.enemyId))
                        {
                            eliteId = e.enemyId;
                            break;
                        }
                    }
                }
                if (string.IsNullOrEmpty(eliteId))
                    eliteId = string.IsNullOrEmpty(g.boss) ? "orc-berserker" : g.boss;
            });
            if (elites > 0 && !string.IsNullOrEmpty(eliteId))
            {
                for (int e = 0; e < elites; e++) ids.Add(eliteId);
                FlowTrace.Step("Garrison",
                    $"'{configId}' eliteCount={elites} as '{eliteId}' appended to garrison list.");
            }
            return ids;
        }

        private void SpawnBoss(GarrisonDef g, int enemyLevel, float difficulty)
        {
            // Boss id: the config's boss field, else fall back to the orc-warlord template.
            string bossId = string.IsNullOrEmpty(g.boss) ? "orc-warlord" : g.boss;

            // Build through the SHARED stat blocks + level scale, then fold difficulty
            // and the boss multipliers (x3 HP / x1.5 dmg, min height) on top.
            EnemyDef def = GarrisonStatBlocks.BuildTypedDef(bossId, enemyLevel);
            float builtHp = def != null ? def.Hp : 0f, builtDmg = def != null ? def.ContactDamage : 0f;
            GarrisonStatBlocks.ApplyLevelScale(def, enemyLevel);
            float lvlHp = def != null ? def.Hp : 0f, lvlDmg = def != null ? def.ContactDamage : 0f;
            FoldDifficulty(def, difficulty);
            def.Hp            *= Mathf.Max(1f, bossHpMult);
            def.ContactDamage *= Mathf.Max(1f, bossDamageMult);
            def.Height         = Mathf.Max(def.Height, bossMinHeight);
            // WO-1530 — the permanent spawn measurement, AFTER every fold this path applies.
            GarrisonStatBlocks.TraceSpawnScale(
                $"raid-boss config='{configId}' difficultyx{difficulty:F2} bossHpx{Mathf.Max(1f, bossHpMult):F2} bossDmgx{Mathf.Max(1f, bossDamageMult):F2}",
                def, enemyLevel, builtHp, builtDmg, lvlHp, lvlDmg);

            Transform marker = transform.Find("BossSpawn");
            Vector3 want = marker != null ? marker.position : transform.position;
            Vector3 pos = SnapToNav(want);

            var boss = EnemyFactory.Build(def, pos, Quaternion.identity, _garrisonRoot);
            if (boss == null)
            {
                // R/U — was a silent LogWarning. A null boss leaves the keep undefended at its
                // centre; self-report so the missing boss is loud.
                FlowTrace.Fail("Garrison", $"'{configId}' SpawnBoss: EnemyFactory returned null for boss '{bossId}' at {pos} — boss NOT spawned.");
                return;
            }
            boss.gameObject.name = $"RaidBoss ({def.Id}-Lv{enemyLevel})";

            var anchor = MakeAnchor("BossAnchor", pos);
            boss.Configure($"raidboss-{configId}", def, anchor);
            boss.SetBrainTarget(anchor);   // HOLD the keep; hero aggro still pulls it in

            // V — the boss must RENDER, else the hero fights an invisible keep defender.
            VerifyGuardRenders(boss, $"boss ({def.Id})", pos);

            // MiniBoss brain (tougher, holds the keep) — mirrors EnemyOutpost.SpawnBoss.
            var brain = boss.gameObject.GetComponent<EnemyBrain>();
            if (brain == null) brain = boss.gameObject.AddComponent<EnemyBrain>();
            BindDefendPost(brain, pos, "Keep");
            brain.Role = EnemyRole.MiniBoss;
            // P0-5 (owner-filed, 2026-08-02): the boss had a brain but NO tactics, so it fell to
            // the legacy chain FindNearbyHero ?? FindNearestTower ?? FindClosestTarget. In a RAID
            // scene there is no player Tower and no HeartOfElarion, so the moment hero aggro
            // dropped the boss acquired NOTHING and stood inert at the keep. Applying the shared
            // archetype puts it on the SCORED path, which can acquire the raider's deployed troops
            // (TroopController implements IDamageableStructure) inside
            // max(_threatScanRadius 12, _towerScanRadius 20) = 20 m. MiniBoss has no case in
            // ApplyRoleTactics, so give it the front-line Siege archetype explicitly — a boss that
            // HOLDS the keep is exactly the siege posture. SHARED SINGLETON: never mutate it.
            brain.SetTactics(EnemyBrain.SiegeTactics);

            Track(boss, brain);
        }

        private void SpawnGuard(string enemyId, int index, int count, int enemyLevel, float difficulty, float ring)
        {
            // Named GarrisonSlot_* markers (WO-1608) win when the bake authored them;
            // otherwise the original ring at baseRadius * 0.5.
            Vector3 want;
            var slot = SlotAt(index);
            if (slot != null)
            {
                want = slot.position;
            }
            else
            {
                float ang = (count > 0 ? (index / (float)count) : 0f) * Mathf.PI * 2f;
                want = transform.position +
                       new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang)) * ring;
            }
            Vector3 pos = SnapToNav(want);

            // SHARED stat block -> level scale -> fold difficulty. Hostile by default.
            EnemyDef def = GarrisonStatBlocks.BuildTypedDef(enemyId, enemyLevel);
            float builtHp = def != null ? def.Hp : 0f, builtDmg = def != null ? def.ContactDamage : 0f;
            GarrisonStatBlocks.ApplyLevelScale(def, enemyLevel);
            float lvlHp = def != null ? def.Hp : 0f, lvlDmg = def != null ? def.ContactDamage : 0f;
            FoldDifficulty(def, difficulty);
            // WO-1530 — the permanent spawn measurement, AFTER every fold this path applies.
            GarrisonStatBlocks.TraceSpawnScale(
                $"raid-guard[{index}] config='{configId}' difficultyx{difficulty:F2}",
                def, enemyLevel, builtHp, builtDmg, lvlHp, lvlDmg);

            var guard = EnemyFactory.Build(def, pos, Quaternion.identity, _garrisonRoot);
            if (guard == null)
            {
                // R/U — was a silent LogWarning. A null guard shrinks the garrison; self-report.
                FlowTrace.Fail("Garrison", $"'{configId}' SpawnGuard[{index}]: EnemyFactory returned null for '{enemyId}' at {pos} — guard NOT spawned.");
                return;
            }
            guard.gameObject.name = $"RaidGuard ({def.Id}-Lv{enemyLevel}-{index})";

            var anchor = MakeAnchor($"GuardAnchor-{index}", pos);
            guard.Configure($"raidguard-{configId}-{index}", def, anchor);
            guard.SetBrainTarget(anchor);   // HOLD the garrison; hero aggro still pulls them in

            // V — the guard must RENDER, else the hero fights an invisible garrison defender.
            VerifyGuardRenders(guard, $"guard-{index} ({def.Id})", pos);

            // P0-5 (owner-filed, 2026-08-02): guards got NO EnemyBrain AT ALL — only Configure +
            // SetBrainTarget(anchor). With no brain the body falls to Enemy's own acquisition, whose
            // only non-hero source is SweepForNearestStructure at _structureSweepRadius = 3 m
            // (Enemy.cs:106). A raid guard therefore fought ONLY what walked within 3 m of it and
            // never advanced on the raider's deployed troops — the garrison was a set of statues.
            // Give it the same brain every other garrison path already builds (precedent:
            // BattleArena.cs:1300, EnemyGroupSpawner.cs:176, RegionMobSpawner.cs:380), with the role
            // derived from the enemy id and the matching SHARED archetype tactics applied so it
            // runs the SCORED target path (TroopController implements IDamageableStructure, so
            // deployed troops are acquirable inside the 20 m tower/threat scan). SetBrainTarget's
            // anchor still tethers it to the garrison, so it holds the keep rather than roaming.
            var guardBrain = guard.gameObject.GetComponent<EnemyBrain>();
            if (guardBrain == null) guardBrain = guard.gameObject.AddComponent<EnemyBrain>();
            BindDefendPost(guardBrain, pos, slot != null ? slot.name : "Yard");
            EnemyRole guardRole = EnemyBrain.RoleForId(def.Id);
            guardBrain.Role = guardRole;
            guardBrain.RosterId = def.Id;   // owner ruling 2026-08-06: gates weapon attach (casters carry nothing)
            EnemyBrain.ApplyRoleTactics(guardBrain, guardRole);   // SHARED singletons — never mutate

            Track(guard, guardBrain);
        }

        // V — confirm a spawned defender actually RENDERS (>=1 enabled renderer carrying a mesh),
        // else the hero fights an invisible garrison member. Mirrors CampGuards.VerifyGuardRenders:
        // a Warn that self-reports to the break-log; the defender stays in the garrison either way
        // (a hittable-but-invisible defender is still clearable; removing it could deadlock the raid).
        private void VerifyGuardRenders(Enemy enemy, string what, Vector3 pos)
        {
            if (enemy == null) return;
            var go = enemy.gameObject;
            int enabledWithMesh = 0;
            foreach (var smr in go.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                if (smr != null && smr.enabled && smr.sharedMesh != null) enabledWithMesh++;
            foreach (var mr in go.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (mr == null || !mr.enabled) continue;
                var mf = mr.GetComponent<MeshFilter>();
                if (mf != null && mf.sharedMesh != null) enabledWithMesh++;
            }
            if (enabledWithMesh == 0)
                FlowTrace.Warn("Garrison",
                    $"INVISIBLE DEFENDER: '{configId}' {what} at {pos} has 0 enabled renderers with a mesh — " +
                    "hero would fight an unseen garrison defender (EnemyFactory fallback should have tinted a capsule).");
        }

        // Fold the config's difficultyMultiplier into HP + contact damage (the ONE place
        // it touches combat). >0 guard so a missing/zero value is a no-op (x1).
        //
        // WO-1763 widened this from private to public for ONE reason, stated so it is not
        // mistaken for drift: RaidDifficultyTunablesRegression's [folds] case drives THIS
        // method rather than re-implementing the two multiplies in the oracle. An oracle
        // that restates the arithmetic it is checking certifies only itself, and reaching
        // a private member by reflection is forbidden (CLAUDE.md section 10). Nothing else
        // calls it from outside; it is still static and still stateless.
        public static void FoldDifficulty(EnemyDef def, float difficulty)
        {
            if (def == null || difficulty <= 0f || Mathf.Approximately(difficulty, 1f)) return;
            def.Hp            *= difficulty;
            def.ContactDamage *= difficulty;
        }

        private void Track(Enemy e, EnemyBrain brain)
        {
            e.Died += HandleGarrisonDied;
            _garrison.Add(e);
            _aliveCount++;

            // WO-1830 — THE STAGGER HOLE, CLOSED. Guards seat 1-2 per frame (SpawnGarrisonStaggered),
            // so the alarm can land mid-spawn. A guard registered AFTER the alarm must be born
            // already rallied, or the tail of an Extreme garrison stays at its post through the very
            // fight the alarm exists to answer.
            if (brain == null) return;
            _brains.Add(brain);
            if (!_alarmRaised) return;
            Vector3 rally = EnemyBrain.ComputeRallyPoint(
                _alarmSpirePos, brain.HomeAnchor, alarmRallyRing, _brains.Count - 1, _brains.Count);
            brain.RallyTo(SnapToNav(rally));
            FlowTrace.Step("Raid",
                $"SPIRE ALARM config='{configId}' - late defender '{e.name}' spawned after the alarm; " +
                $"rallied on spawn to {rally}.");
        }

        // A local tether anchor so the defender HOLDS the garrison instead of marching
        // off. Mirrors EnemyOutpost.MakeAnchor / GarrisonController.MakeAnchor.
        private Transform MakeAnchor(string anchorName, Vector3 pos)
        {
            var go = new GameObject(anchorName);
            go.transform.SetParent(_garrisonRoot, false);
            go.transform.position = pos;
            return go.transform;
        }

        private List<Transform> _slots;

        private int CountGarrisonSlots()
        {
            EnsureSlots();
            return _slots.Count;
        }

        private Transform SlotAt(int index)
        {
            EnsureSlots();
            if (_slots.Count == 0 || index < 0 || index >= _slots.Count) return null;
            return _slots[index];
        }

        private void EnsureSlots()
        {
            if (_slots != null) return;
            _slots = new List<Transform>();
            var all = GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] == null || string.IsNullOrEmpty(all[i].name)) continue;
                if (all[i].name.StartsWith("GarrisonSlot_")) _slots.Add(all[i]);
            }
            _slots.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
        }

        /// <summary>
        /// HOLD THE POST. SetBrainTarget(anchor) is an initial dest, not a leash —
        /// with leash=0 ChooseTarget falls through to FindClosestTarget and every
        /// guard beelines the hero at staging (owner 2026-09-09). Gate watch fights
        /// the door; yard and keep stay inside so turrets can fire into the approach.
        /// </summary>
        private static void BindDefendPost(EnemyBrain brain, Vector3 home, string slotName)
        {
            if (brain == null) return;
            float wake = 14f;
            float chase = 16f;
            if (!string.IsNullOrEmpty(slotName) &&
                slotName.IndexOf("Gate", System.StringComparison.OrdinalIgnoreCase) >= 0)
            {
                wake = 16f;
                chase = 18f;
            }
            else if (!string.IsNullOrEmpty(slotName) &&
                     (slotName.IndexOf("Keep", System.StringComparison.OrdinalIgnoreCase) >= 0
                      || slotName.IndexOf("Boss", System.StringComparison.OrdinalIgnoreCase) >= 0))
            {
                wake = 12f;
                chase = 14f;
            }
            brain.SetDefendPost(home, wake, chase);
            FlowTrace.Step("Garrison",
                "defendPost slot=" + (slotName ?? "?") + " wake=" + wake.ToString("0")
                + "m chase=" + chase.ToString("0") + "m home=" + home);
        }

        private static Vector3 SnapToNav(Vector3 want)
        {
            if (NavMesh.SamplePosition(want, out NavMeshHit hit, 8f, NavMesh.AllAreas))
                return hit.position;
            return want;
        }

        // =====================================================================
        // ATMOSPHERE (WO-1868) — owner: "can we have the towers in raids shoot more than
        // yellow pellets? something with vfx? and add a fog to the ground?" / "maybe
        // storming clouds overhead". Presentation only — no damage/range/fire-rate/
        // targeting/economy change, raid-arena-scoped only (never the peaceful hub).
        //
        // REUSE, NOT A NEW SYSTEM (CLAUDE.md anti-duplication): this project already has
        // TWO atmosphere layers and both are reused verbatim, not reinvented —
        //   1. DISTANCE HAZE + per-camp fog colour/skybox mood is RaidBaseDresser's bake-
        //      time RenderSettings.fog (ConfigureAtmosphere), tuned per camp kit and ruled
        //      by the owner (WO-1637: "the fog COLOUR is deliberately unchanged: it is this
        //      camp's identity"). NOT touched here.
        //   2. LOW-LYING GROUND MIST is the SAME pooled Hovl catalog key
        //      ("PP_GroundFog") + the SAME VFXManager.PlayKey call shape already proven
        //      live on the dungeon world portals (DungeonWorldPortalSpawner.AttachGateVfx)
        //      -- a soft, wide, low ground mist, not a full-screen haze.
        //   3. STORM CLOUDS reuse "PP_LightnigStormCloud", an already-imported, already
        //      normalized (VFXManager.Hovl.cs NormalizeVendorContainerRenderers) Hovl
        //      ParticlePack prefab that has NO prior caller anywhere in the tree -- an
        //      unused, ready-made asset, not a new skybox/cloud shader.
        // Both are ACTUAL WORLD GEOMETRY (particle systems), not a skybox/RenderSettings
        // write, so they render correctly regardless of this scene's camera clear-flags
        // and can never leak into the hub's RenderSettings the way a skybox/fog write on
        // an additively-loaded scene could.
        //
        // COLORBLIND-SAFE, FIRST PASS (owner delegates the exact look — never asked to
        // pick a hue): both tints are pale, near-neutral, low-saturation greys so the
        // read is LUMINANCE + slow drift/motion, exactly the convention the portal mist
        // already uses (DungeonWorldPortalSpawner.GateTint). Redirectable on request.
        //
        // BUDGET: PP_GroundFog's catalog row is PoolSize 6 and VFXManager's global loop
        // ceiling is shared with every other looping effect in the raid (auras, casts),
        // so this stays deliberately small -- 4 ground patches + 2 overhead clouds, well
        // under the pool and the ceiling, so a real fight's own loops are never starved.
        // =====================================================================

        /// <summary>Low, near-neutral ground-mist tint — luminance-led, colorblind-safe.</summary>
        private static readonly Color GroundFogTint = new Color(0.74f, 0.77f, 0.82f, 0.32f);
        private const string GroundFogKey = "PP_GroundFog";
        private const int GroundFogPatchCount = 4;

        /// <summary>Slate-grey storm-cloud tint — dark enough to read as weather, still
        /// near-neutral (no hue the owner would have to distinguish).</summary>
        private static readonly Color StormCloudTint = new Color(0.55f, 0.57f, 0.63f, 0.9f);
        private const string StormCloudKey = "PP_LightnigStormCloud";   // catalog key, vendor's own spelling
        private const int StormCloudCount = 2;
        private const float StormCloudHeight = 24f;

        private void SpawnAtmosphereFx(float baseRadius)
        {
            var atmosphereRoot = new GameObject("[RaidAtmosphereFx]").transform;
            atmosphereRoot.SetParent(transform, false);   // torn down with this raid scene

            // -- Ground fog: a ring of low mist patches inside the wall band, NavMesh-
            // snapped so a sloped/uneven camp floor never floats or sinks a patch (same
            // snap SpawnGuard uses for guard footing).
            float fogRing   = Mathf.Max(6f, baseRadius * 0.55f);
            float fogScale  = Mathf.Clamp(baseRadius / 12f, 3f, 6f);
            int fogSpawned  = 0;
            for (int i = 0; i < GroundFogPatchCount; i++)
            {
                float a = i * Mathf.PI * 2f / GroundFogPatchCount;
                Vector3 want = transform.position + new Vector3(Mathf.Cos(a) * fogRing, 0f, Mathf.Sin(a) * fogRing);
                Vector3 pos = SnapToNav(want) + Vector3.up * 0.05f;   // clear the floor, no z-fight
                var handle = VFXManager.PlayKey(GroundFogKey, pos, Quaternion.identity, atmosphereRoot, GroundFogTint, fogScale);
                if (handle != null) { _atmosphereFx.Add(handle); fogSpawned++; }
            }

            // -- Storm clouds: a small number of overhead loops so the sky reads stormy
            // without being a full-scene weather system (WeatherManager is DORMANT BY
            // OWNER DECISION for the Realm Map zones, WO-992 — deliberately not used here).
            float cloudRing = Mathf.Max(8f, baseRadius * 0.4f);
            float cloudScale = Mathf.Clamp(baseRadius / 6f, 8f, 16f);
            int cloudSpawned = 0;
            for (int i = 0; i < StormCloudCount; i++)
            {
                float a = (i + 0.5f) * Mathf.PI * 2f / StormCloudCount;
                Vector3 pos = transform.position +
                    new Vector3(Mathf.Cos(a) * cloudRing, StormCloudHeight, Mathf.Sin(a) * cloudRing);
                var handle = VFXManager.PlayKey(StormCloudKey, pos, Quaternion.identity, atmosphereRoot, StormCloudTint, cloudScale);
                if (handle != null) { _atmosphereFx.Add(handle); cloudSpawned++; }
            }

            // R §12: a zero count on either layer means PlayKey no-op'd (catalog not ready /
            // pack not imported / loop cap already hit) -- self-report so a silently-missing
            // fog/cloud layer is provable from a log instead of looking like "it's just not
            // there yet".
            FlowTrace.Step("Garrison",
                $"'{configId}' atmosphere fx: ground fog {fogSpawned}/{GroundFogPatchCount} " +
                $"(key='{GroundFogKey}', ring={fogRing:F1}m, scale={fogScale:F1}), storm clouds " +
                $"{cloudSpawned}/{StormCloudCount} (key='{StormCloudKey}', height={StormCloudHeight:F0}m, " +
                $"scale={cloudScale:F1}) -- distance haze/skybox mood stays owned by RaidBaseDresser.");
            if (fogSpawned == 0 && cloudSpawned == 0)
                FlowTrace.Warn("Garrison",
                    $"'{configId}' atmosphere fx: BOTH layers spawned 0 -- VFXManager/HovlVfxCatalog " +
                    "not ready, key unauthored, or the loop cap was already hit. Raid plays with no " +
                    "new ground fog / storm clouds this session.");
        }

        // =====================================================================
        // CLEAR — last defender dies -> mark cleared + raise OnCleared.
        // =====================================================================

        private void HandleGarrisonDied(Enemy enemy)
        {
            if (enemy != null) enemy.Died -= HandleGarrisonDied;
            _aliveCount = Mathf.Max(0, _aliveCount - 1);
            if (_aliveCount == 0) MarkCleared();
        }

        private void MarkCleared()
        {
            if (Cleared) return;
            Cleared = true;
            Debug.Log($"[RaidGarrisonSpawner] '{configId}' CLEARED — garrison wiped.");
            OnCleared?.Invoke(this);
        }
    }
}

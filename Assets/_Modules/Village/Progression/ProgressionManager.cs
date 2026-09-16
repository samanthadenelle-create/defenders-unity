// =============================================================================
// ProgressionManager — the kill-XP hub (owner's "Smart Shared XP" v2 spec).
// -----------------------------------------------------------------------------
// On every enemy kill it shares that enemy's XP across the combatants that
// damaged it: a base pool split by DAMAGE SHARE, plus a KILL-CREDIT bonus to the
// top damager. XP scales with the wave (deeper waves are worth more), and each
// earner's cost to the next level grows with its level — so both the reward and
// the bar grow as the run goes (owner: "wave based multiplier ... and to next
// level can grow").
//
// Cross-module by design: this lives in DeNelle.Village (it knows Enemy, the
// WaveManager and the Wisdom service) but grants XP to pets through the Core
// IXpEarner/XpEarnerRegistry seam, so it never references DeNelle.Pets.
//
// SELF-INSTALLS, NO SCENE WIRING: a [RuntimeInitializeOnLoadMethod] creates the
// singleton, clears the stale damage ledger on each scene load, and attaches a
// HeroProgression to the hero if it has none (mirrors the project's other
// runtime bootstraps). Enemy.Die calls ReportKill — the only hook into it.
// =============================================================================

using DeNelle.Core.Combat;
using DeNelle.Core.Diagnostics;
using DeNelle.Core.Progression;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DeNelle.Village.Progression
{
    /// <summary>Distributes kill-XP by damage share + kill credit, scaled by wave.</summary>
    public sealed class ProgressionManager : MonoBehaviour
    {
        public static ProgressionManager Instance { get; private set; }

        // ── Tuning ────────────────────────────────────────────────────────────
        /// <summary>Share of an enemy's XP handed to its top damager as a kill bonus.</summary>
        private const float KillCreditFraction = 0.25f;
        /// <summary>Extra XP per wave beyond the first (+20%/wave) — the wave multiplier.</summary>
        private const float XpPerWaveBonus = 0.20f;
        /// <summary>Base XP for a kill = enemy max HP * this.</summary>
        private const float XpFromMaxHp = 0.5f;
        /// <summary>Floor so even the weakest enemy is worth something.</summary>
        private const float MinXpPerKill = 6f;

        private static readonly Color LevelUpColor = new Color(0.45f, 1f, 0.55f, 1f);

        private WaveManager _waveManager;

        // ── Bootstrap ─────────────────────────────────────────────────────────

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
            OnSceneLoaded(default, default);   // the first scene is already loaded
        }

        private static void OnSceneLoaded(Scene s, LoadSceneMode mode)
        {
            DamageAttribution.Clear();         // drop buckets from the previous scene
            EnsureInstance();
            Instance?.OnSceneReady();
        }

        private static void EnsureInstance()
        {
            if (Instance != null) return;
            var go = new GameObject("ProgressionManager");
            DontDestroyOnLoad(go);
            Instance = go.AddComponent<ProgressionManager>();
        }

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        /// <summary>Re-resolves per-scene refs and ensures the hero carries a HeroProgression.</summary>
        private void OnSceneReady()
        {
            _waveManager = FindAnyObjectByType<WaveManager>();

            var hero = FindAnyObjectByType<HeroAbilities>();
            if (hero != null && hero.GetComponent<HeroProgression>() == null)
            {
                var prog = hero.gameObject.AddComponent<HeroProgression>();
                // F8-47 — a fresh attach used to start at level 1 regardless of the save
                // (the outpost-return level reset). OnEnable already tried a restore; re-run
                // it here explicitly (the save service is definitely up post-scene-load) and
                // trace what the hero landed on so any future reset names itself.
                prog.RestoreFromSave();
                FlowTrace.Step("HeroXp", $"attached HeroProgression to '{hero.gameObject.name}' scene '{SceneManager.GetActiveScene().name}' -> restored level={prog.Level} xp={prog.Xp:0.#} (fromSave={prog.HasRestoredFromSave})");
            }
        }

        // ── Kill -> shared XP ─────────────────────────────────────────────────

        /// <summary>Called from <see cref="Enemy.Die"/> on a genuine kill. Null-safe.</summary>
        public static void ReportKill(Enemy enemy)
        {
            if (enemy == null) return;
            if (!DeNelle.Core.Combat.PracticeCombatPolicy.AllowsProgression(enemy))
            { DamageAttribution.Forget(enemy); return; }
            EnsureInstance();
            Instance.Distribute(enemy);
        }

        private void Distribute(Enemy enemy)
        {
            float baseXp = Mathf.Max(MinXpPerKill, enemy.MaxHp * XpFromMaxHp);
            int wave = CurrentWave();
            float totalXp = baseXp * (1f + Mathf.Max(0, wave - 1) * XpPerWaveBonus);

            var ledger = DamageAttribution.Drain(enemy);
            if (ledger == null || ledger.Count == 0)
            {
                // No tracked damage (e.g. a scripted kill) — credit the hero.
                Grant(HeroProgression.Id, totalXp);
                return;
            }

            float sum = 0f;
            string topId = null;
            float topDmg = -1f;
            foreach (var kv in ledger)
            {
                sum += kv.Value;
                if (kv.Value > topDmg) { topDmg = kv.Value; topId = kv.Key; }
            }
            if (sum <= 0f) { Grant(HeroProgression.Id, totalXp); return; }

            float sharePool = totalXp * (1f - KillCreditFraction);
            foreach (var kv in ledger)
            {
                float xp = sharePool * (kv.Value / sum);
                if (kv.Key == topId) xp += totalXp * KillCreditFraction;  // kill-credit bonus
                Grant(kv.Key, xp);
            }
        }

        private void Grant(string earnerId, float xp)
        {
            if (xp <= 0f) return;
            var earner = XpEarnerRegistry.TryGet(earnerId);
            if (earner == null) return;          // earner not deployed (e.g. pet downed/removed)

            int levels = earner.AddXp(xp);
            if (levels > 0)
            {
                DamageNumberSpawner.SpawnLabel(
                    $"LEVEL UP!  Lv.{earner.Level}", earner.WorldPosition, LevelUpColor, 1.4f);

                // Combat feel: celebratory burst + gold screen flash on every
                // level-up (hero AND pets flow through here). Null-safe.
                // VFX spawns at FOOT level (owner ask 2026-07-10) — earner.WorldPosition
                // is a HEAD-height point used for the TEXT label above; the burst wants
                // the ground. The IXpEarner impl (HeroProgression — PetProgression retired,
                // WO-993) is a Component on the character ROOT, so transform.position is the feet.
                // Fallback to WorldPosition for any future non-Component earner.
                Vector3 footPos = (earner as Component)?.transform.position ?? earner.WorldPosition;
                LevelUpVFXController.Instance?.PlayLevelUp(footPos, earner.Level);
            }
        }

        private int CurrentWave()
        {
            if (_waveManager == null) _waveManager = FindAnyObjectByType<WaveManager>();
            return _waveManager != null ? Mathf.Max(1, _waveManager.CurrentWaveId) : 1;
        }
    }
}

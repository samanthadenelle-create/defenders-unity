// =============================================================================
// BattleMusicManager — WO-372: a wave-driven battle MUSIC STATE MACHINE.
// -----------------------------------------------------------------------------
// Assembly: DeNelle.Village   Namespace: DeNelle.Village
//
// WHAT IT IS
//   A small state machine that scores the village wave loop with four cues and
//   crossfades between them (1.5 s):
//      • Combat   — the general battle loop  (Overworld_Battle_1, loops)
//      • Intense  — 5+ live enemies on the field, high-pressure (Overworld_Battle_2, loops)
//      • Victory  — a wave was cleared; a ONE-SHOT sting (Overworld_Victory, no loop),
//                   after which we hand the music back to the ambient/idle service.
//      • Boss     — a boss / apex wave is live (Overworld_Boss_Fight, loops)
//
// WHY ITS OWN AudioSources (and not AudioService.PlayMusic(Battle))
//   AudioService already owns the SCENE-music director (Title/Village/Battle/…)
//   and a battle POOL it rotates per scene-load; if we drove its PlayMusic we'd
//   (a) fight its scene-load short-circuit and (b) collapse our four distinct
//   wave states into its single "Battle" track. So we REUSE the audio service for
//   what it is good at — the shared mixer + the ambient/idle handoff — but own a
//   dedicated pair of crossfading AudioSources for the battle states, routed
//   THROUGH AudioService's Music mixer group so the player's music volume/mute
//   still applies. We do NOT reinvent the mixer, the volume model, or the idle
//   music: when the Victory sting ends we call AudioService.ReturnToAmbient() so
//   the town/overworld ambient track resumes exactly as before.
//
// HOW IT LISTENS (no WaveManager edits — listen only, per WO-372)
//   WaveManager (same DeNelle.Village assembly) exposes public UnityEvent fields.
//   We FIND the live WaveManager (FindAnyObjectByType) and AddListener to its
//   EXISTING events — exactly how HeroPoseController / TownHudBridge subscribe.
//   We add NO hook inside WaveManager. Mapping:
//      OnWaveStarted        → Combat   (general loop)        — unless it's a boss wave
//      OnApexBossSpawned    → Boss     (boss loop)
//      OnWaveCleared        → Victory  (one-shot, → ambient) — unless a boss is still up
//      OnDefeat             → stop battle music, hand back to ambient (the defeat
//                             screen owns its own sting via AudioService.Defeat)
//   Live-enemy pressure is polled (0.5 s): ≥ IntenseEnemyThreshold live enemies in
//   a non-boss combat state crossfades Combat ↔ Intense. The poll is also the
//   safety net that re-binds to a WaveManager that spawned after us / re-evaluates
//   from WaveManager.Phase + LiveEnemies if an event was missed.
//
// CLIPS  (Suno-generated; load by Resources path; missing = graceful no-op)
//   Loaded via DeNelle.Core.AudioAssetLoader.LoadClip — Addressables-first,
//   Resources-fallback (WebGL-safe — no File I/O). Same keys either way. Expected at:
//      Resources/Music/Battle/Overworld_Battle_1
//      Resources/Music/Battle/Overworld_Battle_2
//      Resources/Music/Battle/Overworld_Victory
//      Resources/Music/Battle/Overworld_Boss_Fight
//   i.e. dropped under Assets/Audio/Resources/Music/Battle/ (parallel to the other
//   music in Assets/Audio/Resources/). A few common name variants are also tried.
//   ANY missing clip → that STATE no-ops cleanly (no error, just a one-time warn);
//   the rest of the machine keeps working. See the FLAG at the bottom of this file
//   for the exact files that currently need importing/moving.
//
// SELF-BOOTSTRAP + SAFETY
//   RuntimeInitializeOnLoadMethod spawns a DDOL host (HeroPoseController/TownHud
//   pattern), idempotent. Every event read + audio touch is try/catch-guarded (an
//   uncaught throw halts the WebGL player). Music volume target ~0.7.
// =============================================================================

using System.Collections;
using System.Collections.Generic;
using DeNelle.Audio;            // MusicDirector (Village references DeNelle.Audio)
using DeNelle.Core.Audio;       // MusicLayer (Core contract)
using DeNelle.Core.Diagnostics; // FlowTrace (Village references DeNelle.Core)
using DeNelle.Village.Hud;      // HudContextEvaluator.ImminentThreshold (same asmdef; the one Village-side imminent rule)
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DeNelle.Village
{
    /// <summary>
    /// Wave-driven battle music state machine (Combat / Intense / Victory / Boss)
    /// with a 1.5 s crossfade. Owns a dedicated pair of AudioSources routed through
    /// the shared <see cref="AudioService"/> Music mixer group, listens to the live
    /// <see cref="WaveManager"/>'s public events, and hands the music back to the
    /// ambient/idle service when battle ends. Self-bootstrapping, WebGL-safe.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BattleMusicManager : MonoBehaviour
    {
        // ── The four battle music states ──────────────────────────────────────
        private enum BattleMusicState
        {
            /// <summary>No battle music — the ambient/idle service owns playback.</summary>
            None = 0,
            /// <summary>General battle loop (Overworld_Battle_1).</summary>
            Combat,
            /// <summary>High-pressure loop, 5+ live enemies (Overworld_Battle_2).</summary>
            Intense,
            /// <summary>Post-wave one-shot sting (Overworld_Victory) — then back to ambient.</summary>
            Victory,
            /// <summary>Boss / apex-wave loop (Overworld_Boss_Fight).</summary>
            Boss,
        }

        // ── Tunables ──────────────────────────────────────────────────────────
        private const float CrossfadeSeconds = 1.5f;     // WO-372 crossfade duration
        private const float MusicVolume      = 0.7f;      // WO-372 music level (pre-mixer)
        private const int   IntenseEnemyThreshold = 5;    // ≥5 live enemies → Intense
        private const float PollInterval     = 0.5f;      // re-bind / re-evaluate cadence

        // Resources paths (no extension, no Resources/ prefix — Resources.Load form).
        // Primary normalized names + fall-back variants for the as-imported files.
        private static readonly string[] CombatClipPaths =
        {
            "Music/Battle/Overworld_Battle_1",
            "Music/Battle/Overworld battle 1",
            "Music/Battle/Overworld_battle_1",
        };
        private static readonly string[] IntenseClipPaths =
        {
            "Music/Battle/Overworld_Battle_2",
            "Music/Battle/Overworld battle 2",
            "Music/Battle/Overworld_battle_2",
        };
        private static readonly string[] VictoryClipPaths =
        {
            "Music/Battle/Overworld_Victory",
            "Music/Battle/Overworld Victory",
        };
        private static readonly string[] BossClipPaths =
        {
            "Music/Battle/Overworld_Boss_Fight",
            "Music/Battle/Overworld Boss Fight",
        };

        // ── Singleton DDOL host (HeroPoseController pattern) ──────────────────
        private static BattleMusicManager _instance;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (_instance != null) return;
            var go = new GameObject("BattleMusicManager");
            DontDestroyOnLoad(go);
            go.AddComponent<BattleMusicManager>();
        }

        // ── Clips (resolved once from Resources) ──────────────────────────────
        private AudioClip _combatClip;
        private AudioClip _intenseClip;
        private AudioClip _victoryClip;
        private AudioClip _bossClip;
        private bool _clipsResolved;

        // ── Playback ownership REMOVED (2026-07-09, MUSIC_AUTHORITY_DESIGN) ─────
        // BattleMusicManager no longer owns any AudioSource. It is now a POLICY
        // PROVIDER: it keeps the wave-state SELECTION logic and Pushes/Releases the
        // single MusicDirector's Battle layer. The director owns the one A/B pair,
        // so two beds are impossible and the auto-fallback restores ambient on
        // Release(Battle) — deleting the old fragile StopMusic/ReturnToAmbient handoff.

        // ── State ─────────────────────────────────────────────────────────────
        private BattleMusicState _state = BattleMusicState.None;
        private float _pollTimer;
        private Coroutine _victoryReturn;

        // WaveManager subscription (FOUND, not modified — listen only).
        private WaveManager _wave;

        // ── Lifecycle ─────────────────────────────────────────────────────────
        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                // Our own dedicated host — destroy only the duplicate component,
                // never the GameObject (singleton-dedup memory).
                Destroy(this);
                return;
            }
            _instance = this;
            // No AudioSources to build — the single MusicDirector owns playback.
        }

        private void OnEnable()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        private void OnDisable()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            UnsubscribeWave();
        }

        private void OnDestroy()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            UnsubscribeWave();
            if (_instance == this) _instance = null;
        }

        private void Start()
        {
            ResolveClips();
            TryResolveAndSubscribeWave();
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            // A new scene's WaveManager replaces the old one — re-bind next poll.
            UnsubscribeWave();
            _wave = null;

            // Leaving a battle context: stop our battle music and let the scene's
            // ambient/idle music (driven by AudioService) take over.
            if (_state != BattleMusicState.None)
                TransitionTo(BattleMusicState.None);
        }

        private void Update()
        {
            _pollTimer -= Time.unscaledDeltaTime;
            if (_pollTimer > 0f) return;
            _pollTimer = PollInterval;

            try
            {
                TryResolveAndSubscribeWave();   // lazy-bind a WaveManager that appeared later
                ReevaluateFromState();          // pressure (Intense) + missed-event safety net
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("[BattleMusicManager] poll failed: " + e.Message);
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        //  WaveManager subscription (listen-only — no WaveManager edits)
        // ─────────────────────────────────────────────────────────────────────
        private void TryResolveAndSubscribeWave()
        {
            if (_wave != null) return;
            // Prefer the canonical singleton (active-scene WaveManager) so we subscribe
            // to the SAME instance the trigger drives; fall back to Find pre-Awake.
            var wave = WaveManager.Instance ?? FindAnyObjectByType<WaveManager>();
            if (wave == null) return;

            _wave = wave;
            if (_wave.OnWaveStarted != null)     _wave.OnWaveStarted.AddListener(OnWaveStarted);
            if (_wave.OnWaveCleared != null)     _wave.OnWaveCleared.AddListener(OnWaveCleared);
            if (_wave.OnApexBossSpawned != null) _wave.OnApexBossSpawned.AddListener(OnBossSpawned);
            if (_wave.OnDefeat != null)          _wave.OnDefeat.AddListener(OnDefeat);
        }

        private void UnsubscribeWave()
        {
            if (_wave == null) return;
            if (_wave.OnWaveStarted != null)     _wave.OnWaveStarted.RemoveListener(OnWaveStarted);
            if (_wave.OnWaveCleared != null)     _wave.OnWaveCleared.RemoveListener(OnWaveCleared);
            if (_wave.OnApexBossSpawned != null) _wave.OnApexBossSpawned.RemoveListener(OnBossSpawned);
            if (_wave.OnDefeat != null)          _wave.OnDefeat.RemoveListener(OnDefeat);
            _wave = null;
        }

        // ── Event handlers ────────────────────────────────────────────────────

        // A wave began. Boss waves are scored by OnApexBossSpawned (fired alongside),
        // so only enter Combat here when this is NOT a boss wave — and never demote
        // a live Boss state down to Combat.
        private void OnWaveStarted(int waveId)
        {
            if (IsBossWaveLive()) { TransitionTo(BattleMusicState.Boss); return; }
            if (_state == BattleMusicState.Boss) return;
            TransitionTo(BattleMusicState.Combat);
        }

        private void OnBossSpawned(DragonBoss boss)
        {
            TransitionTo(BattleMusicState.Boss);
        }

        // A wave was cleared. If a boss is still aloft the fight isn't over — stay
        // in Boss. Otherwise play the Victory one-shot, then return to ambient.
        private void OnWaveCleared(int waveId)
        {
            if (IsBossWaveLive()) return;
            TransitionTo(BattleMusicState.Victory);
        }

        // Heart fell — battle's over. Stop our battle music; the defeat screen owns
        // its own sting (AudioService Defeat track). Hand back to ambient/idle.
        private void OnDefeat()
        {
            TransitionTo(BattleMusicState.None);
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Public combat trigger for NON-wave-loop combat (FTUE teaching wave)
        // ─────────────────────────────────────────────────────────────────────
        //
        // The wave loop scores itself via the WaveManager.OnWaveStarted/OnWaveCleared
        // subscription above. But some combat DOES NOT run through that loop — most
        // notably the FTUE's scripted teaching wave, which spawns its enemies through
        // WaveManager.SpawnEnemyForExternalMode (TutorialWaveSpawner) and never raises
        // OnWaveStarted, so the wave-event path can't see it. These two static entries
        // are the SINGLE signal any such external combat producer calls to drive the
        // SAME state machine (town ambient stops → battle track plays, then back), so we
        // stay single-source rather than re-implementing the swap per caller. Null-safe:
        // no-op before the DDOL host has bootstrapped.

        /// <summary>
        /// Combat that does NOT run through the WaveManager wave loop just went live
        /// (e.g. the FTUE teaching wave's enemies spawned). Enters the general Combat
        /// state — town ambient stops, the battle track plays — exactly as
        /// <see cref="OnWaveStarted"/> does for an ambient wave. Ignored if a real
        /// wave/boss already owns a higher combat state.
        /// </summary>
        public static void NotifyExternalCombatActive()
        {
            var m = _instance;
            if (m == null) return;
            try
            {
                // Never demote a live Boss (or an already-running combat/intense) — only
                // take over from None (or a Victory sting that's winding down).
                if (m._state == BattleMusicState.Boss
                    || m._state == BattleMusicState.Combat
                    || m._state == BattleMusicState.Intense)
                    return;
                FlowTrace.Step("BattleMusic",
                    "external combat active (non-wave, e.g. FTUE teaching wave) → Combat (town ambient stops)");
                m.TransitionTo(BattleMusicState.Combat);
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("[BattleMusicManager] NotifyExternalCombatActive failed: " + e.Message);
            }
        }

        /// <summary>
        /// Paired with <see cref="NotifyExternalCombatActive"/>: the non-wave combat
        /// ended (all its enemies are dead) — hand the music back to the town/overworld
        /// ambient. Only leaves a plain Combat/Intense state (the one our external
        /// trigger owns) so it can never yank a real wave/boss that started meanwhile.
        /// </summary>
        public static void NotifyExternalCombatEnded()
        {
            var m = _instance;
            if (m == null) return;
            try
            {
                if (m._state != BattleMusicState.Combat && m._state != BattleMusicState.Intense)
                    return;
                FlowTrace.Step("BattleMusic",
                    "external combat ended (non-wave) → return to town ambient");
                m.TransitionTo(BattleMusicState.None);
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("[BattleMusicManager] NotifyExternalCombatEnded failed: " + e.Message);
            }
        }

        /// <summary>
        /// Poll-time re-evaluation: the missed-event safety net + the live-enemy
        /// PRESSURE swap (Combat ↔ Intense). Boss/Victory/None are event-owned and
        /// left alone here (we never auto-leave them on a poll).
        /// </summary>
        private void ReevaluateFromState()
        {
            if (_wave == null) return;

            // Boss is owned by events (OnApexBossSpawned / boss death handled via
            // the next wave's OnWaveStarted) — don't auto-change it on a poll.
            if (_state == BattleMusicState.Boss) return;
            // Victory is a one-shot that returns to ambient on its own coroutine.
            if (_state == BattleMusicState.Victory) return;

            // Safety net: if a wave is actually live but we somehow never entered a
            // combat state, enter Combat. A COUNTDOWN counts as live ONLY when the wave
            // is IMMINENT (owner ruling 2026-07-08, same rule the HUD posture obeys via
            // HudContextEvaluator.ImminentThreshold) — a long between-waves gap must NOT
            // hold battle music over a peaceful town. Proven by F8 seq 2251 (2026-08-10):
            // Victory wound down to None mid-countdown (live 0/0, cd~287s) and this net
            // re-pushed Battle:Combat for the whole ~290s gap while the HUD, on the same
            // second, logged "countdown long-gap -> gated OUT of Battle".
            // WO-1736: ...and an ENDLESS wave PARKED on the player's DEFEND press is not
            // imminent either, however small its held countdown reads. It parks at
            // _countdownRemaining == 0, so the bare `<= 5f` test below is `0 <= 5` = TRUE and
            // held battle music over a peaceful town for the whole wait - the same shape as the
            // seq 2251 defect this net already guards, arriving through the other door. The full
            // reasoning and the captured device window live at HudContextEvaluator.IsWaveActive;
            // IsAwaitingPlayerStart is the authority (WaveManager.cs:570-575).
            bool imminent = _wave.Phase == WavePhase.Countdown
                && !_wave.IsAwaitingPlayerStart
                && _wave.CountdownRemaining <= HudContextEvaluator.ImminentThreshold;
            bool waveLive = _wave.Phase == WavePhase.Active || imminent;
            if (_wave.Phase == WavePhase.Countdown && !imminent && _state == BattleMusicState.None)
                FlowTrace.Throttle("BattleMusic", "countdown-longgap", 5f,
                    $"countdown long-gap ({_wave.CountdownRemaining:0.0}s > {HudContextEvaluator.ImminentThreshold}s) -> staying ambient (no Combat push)");
            if (waveLive && _state == BattleMusicState.None)
            {
                TransitionTo(IsBossWaveLive() ? BattleMusicState.Boss : BattleMusicState.Combat);
                return;
            }

            // Pressure swap only while we're already in a non-boss combat state.
            if (_state != BattleMusicState.Combat && _state != BattleMusicState.Intense)
                return;

            int live = _wave.LiveEnemies != null ? _wave.LiveEnemies.Count : 0;
            if (live >= IntenseEnemyThreshold && _state != BattleMusicState.Intense)
                TransitionTo(BattleMusicState.Intense);
            else if (live < IntenseEnemyThreshold && _state != BattleMusicState.Combat)
                TransitionTo(BattleMusicState.Combat);
        }

        private bool IsBossWaveLive()
        {
            return _wave != null && _wave.LiveApexBoss != null && !_wave.LiveApexBoss.IsDead;
        }

        // ─────────────────────────────────────────────────────────────────────
        //  State machine → crossfade
        // ─────────────────────────────────────────────────────────────────────
        private void TransitionTo(BattleMusicState next)
        {
            if (next == _state) return;

            BattleMusicState prev = _state;

            // Cancel a pending Victory→ambient handoff if the state changes first.
            if (_victoryReturn != null)
            {
                StopCoroutine(_victoryReturn);
                _victoryReturn = null;
            }

            _state = next;

            // ── Single-owner Push/Release (doubled-music fix, MUSIC_AUTHORITY) ─
            // BattleMusicManager owns the MusicDirector's Battle layer. It Pushes its
            // chosen wave-state clip onto that layer and Releases it when battle ends.
            // The director resolves the highest active layer, so the ambient/idle bed
            // (AudioService's Ambient/Overworld layer) keeps its slot underneath and
            // is auto-restored the instant we Release — no StopMusic/ReturnToAmbient
            // handoff, and two beds are impossible (one owner, one pair).
            try
            {
                if (next == BattleMusicState.None)
                {
                    FlowTrace.Step("BattleMusic",
                        "battle ended — Release(Battle); director auto-falls back to ambient.");
                    MusicDirector.Instance?.Release(MusicLayer.Battle);
                    return;
                }

                AudioClip clip = ClipForState(next);
                if (clip == null)
                {
                    // GRACEFUL no-op: this state has no imported clip. Warn once,
                    // keep the machine alive. (Don't touch playback — leave whatever
                    // is currently playing rather than cutting to silence.)
                    WarnMissingOnce(next);
                    return;
                }

                bool loop = next != BattleMusicState.Victory;   // Victory is a one-shot
                FlowTrace.Step("BattleMusic",
                    "Push(Battle) state=" + next + " clip='" + clip.name + "' (prev=" + prev + ")");
                MusicDirector.Instance?.PushClip(
                    MusicLayer.Battle, clip, MusicVolume, loop, CrossfadeSeconds, "Battle:" + next);

                // Victory is a one-shot: when it ends, drop to None → Release(Battle).
                if (next == BattleMusicState.Victory)
                {
                    float dur = clip.length > 0.01f ? clip.length : 3f;
                    _victoryReturn = StartCoroutine(VictoryReturnRoutine(dur));
                }
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("[BattleMusicManager] TransitionTo(" + next + ") failed: " + e.Message);
            }
        }

        private AudioClip ClipForState(BattleMusicState state)
        {
            ResolveClips();
            switch (state)
            {
                case BattleMusicState.Combat:  return _combatClip;
                case BattleMusicState.Intense: return _intenseClip;
                case BattleMusicState.Victory: return _victoryClip;
                case BattleMusicState.Boss:    return _bossClip;
                default:                       return null;
            }
        }

        // When the Victory sting finishes, drop back to None — which fades our
        // sources out and resumes the ambient/idle music via AudioService.
        private IEnumerator VictoryReturnRoutine(float seconds)
        {
            yield return new WaitForSecondsRealtime(seconds);
            _victoryReturn = null;
            if (_state == BattleMusicState.Victory)
                TransitionTo(BattleMusicState.None);
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Clip loading (AudioAssetLoader: Addressables-first / Resources-fallback —
        //  WebGL-safe, no File I/O)
        // ─────────────────────────────────────────────────────────────────────
        private void ResolveClips()
        {
            if (_clipsResolved) return;
            _clipsResolved = true;

            _combatClip  = LoadFirst(CombatClipPaths);
            _intenseClip = LoadFirst(IntenseClipPaths);
            _victoryClip = LoadFirst(VictoryClipPaths);
            _bossClip    = LoadFirst(BossClipPaths);

            // One consolidated FLAG so the integrator knows exactly what to import.
            var missing = new List<string>();
            if (_combatClip  == null) missing.Add("Combat → Resources/" + CombatClipPaths[0]);
            if (_intenseClip == null) missing.Add("Intense → Resources/" + IntenseClipPaths[0]);
            if (_victoryClip == null) missing.Add("Victory → Resources/" + VictoryClipPaths[0]);
            if (_bossClip    == null) missing.Add("Boss → Resources/" + BossClipPaths[0]);

            if (missing.Count > 0)
                Debug.LogWarning(
                    "[BattleMusicManager] " + missing.Count + " battle music clip(s) NOT found — " +
                    "those states play silent (no error). Drop the Suno tracks under " +
                    "Assets/Audio/Resources/Music/Battle/ with these names: " +
                    string.Join(" | ", missing));
        }

        private static AudioClip LoadFirst(string[] paths)
        {
            if (paths == null) return null;
            foreach (string p in paths)
            {
                if (string.IsNullOrEmpty(p)) continue;
                try
                {
                    AudioClip clip = DeNelle.Core.AudioAssetLoader.LoadClip(p, optional: true);
                    if (clip != null) return clip;
                }
                catch { /* WebGL-safe: never throw out of clip resolution */ }
            }
            return null;
        }

        // Warn at most once per state about a missing clip so a silent state never
        // spams the log every transition.
        private readonly HashSet<BattleMusicState> _warned = new HashSet<BattleMusicState>();
        private void WarnMissingOnce(BattleMusicState state)
        {
            if (_warned.Contains(state)) return;
            _warned.Add(state);
            Debug.LogWarning(
                "[BattleMusicManager] No clip for the " + state + " state — it plays silent. " +
                "Import the matching track under Assets/Audio/Resources/Music/Battle/.");
        }
    }
}

// =============================================================================
// FLAG — clip files that need importing/relocating for WO-372
// -----------------------------------------------------------------------------
// The four Suno tracks ARE in the project but live at:
//     Assets/Audio/Music/Battle/Overworld battle 1.mp3
//     Assets/Audio/Music/Battle/Overworld battle 2.mp3
//     Assets/Audio/Music/Battle/Overworld Victory.mp3
//     Assets/Audio/Music/Battle/Overworld Boss Fight.mp3
// That folder is NOT under a Resources/ folder, so Resources.Load CANNOT see them.
//
// CLI (asset owner): create Assets/Audio/Resources/Music/Battle/ and place the
// four clips there with normalized names so the PRIMARY Resources paths resolve:
//     Assets/Audio/Resources/Music/Battle/Overworld_Battle_1.mp3
//     Assets/Audio/Resources/Music/Battle/Overworld_Battle_2.mp3
//     Assets/Audio/Resources/Music/Battle/Overworld_Victory.mp3
//     Assets/Audio/Resources/Music/Battle/Overworld_Boss_Fight.mp3
// (The manager also tries the original spaced names as a fallback IF that whole
//  Battle folder is moved under Resources/, but the underscored names are canon.)
// Until then each state no-ops gracefully — no errors, just one warning listing
// exactly the above.
// =============================================================================

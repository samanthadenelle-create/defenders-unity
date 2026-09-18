// =============================================================================
// AudioService — the game's single audio surface (P0-9 in missing-components.md).
// -----------------------------------------------------------------------------
// "No audio system or Audio Mixer wiring — game ships silent." This is the fix.
//
// AudioService is the Unity analog of the React audioManager.ts: a DontDestroy-
// OnLoad MonoBehaviour singleton that owns the music AudioSources, fires SFX,
// applies the AudioMixer, and crossfades the per-scene BGM. SceneRouter's own
// header anticipates it — "an Audio/Core director listens for scene loads" —
// AudioService IS that director (it subscribes to SceneManager.sceneLoaded).
//
// PUBLIC API (the surface a settings menu / scene controllers call):
//   PlayMusic(MusicTrack)        — crossfade to a track (per audio-mix-spec §3).
//   StopMusic()                  — fade the current track out to silence.
//   PlaySfx(AudioClip)           — fire a one-shot on the SFX group.
//   PlayUiSfx / PlayVoice        — one-shots routed to the UI / Voice groups.
//   SetVolume(MixerGroup, value) — drive an exposed mixer parameter, 0..1(.5).
//   SetMuted(bool)               — master mute (audio-mix-spec §5).
//
// MIXER: the service routes through Assets/Audio/Resources/Audio/GameAudioMixer
// .mixer — five groups Master / Music / SFX / UI / Voice, each with an exposed
// volume param (MasterVol / MusicVol / SfxVol / UiVol / VoiceVol). A parallel
// Settings-menu agent drives those params directly via AudioMixer.SetFloat
// (its AudioMixerBridge resolves the SAME mixer by the SAME Resources path);
// this service applies the player's persisted GameState volumes on boot so the
// two stay in sync. Exposed-param names are the documented contract — see
// MixerParams below; they match AudioMixerBridge's MasterParam/MusicParam/SfxParam.
//
// SCENE -> TRACK map (audio-mix-spec §3): Title/ATBBattle/Onboarding -> their
// track; Village -> village; Dungeon_* -> dungeon. A missing clip is GUARDED:
// the path is wired, the gap is logged, the scene plays silent — no invented
// audio (the dungeon MP3 echoes-beneath-elarion is a known-missing asset).
//
// All fades are UniTask coroutines — never `async void` (port-spec Part 3).
// =============================================================================

using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using DeNelle.Core;
using DeNelle.Core.Audio;
using DeNelle.Core.Diagnostics;
using DeNelle.Core.State;
using DeNelle.Core.UI;   // LocalizedText - WO-1857 (jukebox track display names are player copy)
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.SceneManagement;

namespace DeNelle.Audio
{
    /// <summary>
    /// The game-wide audio director — owns music playback + crossfade, fires
    /// SFX, and applies the AudioMixer. A <see cref="DontDestroyOnLoad"/>
    /// singleton; survives scene loads (audio-mix-spec.md §7, P0-9). Created
    /// automatically by <see cref="AudioBootstrap"/> — no scene wiring required.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class AudioService : MonoBehaviour, IAudioService
    {
        // ── Singleton ────────────────────────────────────────────────────────
        private static AudioService _instance;

        /// <summary>The live service, or null before <see cref="AudioBootstrap"/> runs.</summary>
        public static AudioService Instance => _instance;

        // ── Mixer ────────────────────────────────────────────────────────────

        [Header("Mixer")]
        [Tooltip("Assets/Audio/Resources/Audio/GameAudioMixer.mixer. Auto-loaded " +
                 "from Resources by AudioBootstrap when left unassigned; assign " +
                 "explicitly on a prefab to skip the Resources lookup.")]
        [SerializeField] private AudioMixer _mixer;

        [Tooltip("The Music mixer group — the two music AudioSources route here.")]
        [SerializeField] private AudioMixerGroup _musicGroup;

        [Tooltip("The SFX mixer group — PlaySfx one-shots route here.")]
        [SerializeField] private AudioMixerGroup _sfxGroup;

        [Tooltip("The UI mixer group — PlayUiSfx one-shots route here.")]
        [SerializeField] private AudioMixerGroup _uiGroup;

        [Tooltip("The Voice mixer group — PlayVoice one-shots route here.")]
        [SerializeField] private AudioMixerGroup _voiceGroup;

        // ── Music clips (wired in the inspector or by an import pass) ─────────

        [Header("Music clips — assign once imported under Assets/Audio/")]
        [Tooltip("title.mp3 — title screen + cold open. Default mix volume 0.6.")]
        [SerializeField] private AudioClip _titleClip;

        [Tooltip("village.mp3 — village exploration. Default mix volume 0.4.")]
        [SerializeField] private AudioClip _villageClip;

        [Tooltip("dungeons/echoes-beneath-elarion.mp3 — dungeon ambient. KNOWN-MISSING " +
                 "asset (see docs/port-notes/audio-system.md). Leave null until the " +
                 "MP3 lands; PlayMusic(Dungeon) guards it and logs.")]
        [SerializeField] private AudioClip _dungeonClip;

        [Tooltip("battle.mp3 — ATB combat. Default mix volume 0.7.")]
        [SerializeField] private AudioClip _battleClip;

        [Tooltip("victory.mp3 — battle-win sting. Default mix volume 0.7, no loop.")]
        [SerializeField] private AudioClip _victoryClip;

        [Tooltip("defeat.mp3 — battle-loss sting. Default mix volume 0.5, no loop.")]
        [SerializeField] private AudioClip _defeatClip;

        [Tooltip("Music/echo_theme.mp3 — \"Echo's theme\", the Arena raid BGM. " +
                 "Default mix volume 0.4 (soft background), loops. // arena BGM — soft background")]
        [SerializeField] private AudioClip _arenaClip;

        [Tooltip("Music/Raid/brass-rampart.mp3 - the offensive-raid BGM (WO-453), driving " +
                 "brass for marching a troop army on an enemy fortress. Default mix volume " +
                 "0.5 (MusicTrackRegistry.RaidVolume), loops. Single clip, not pooled.")]
        [SerializeField] private AudioClip _raidClip;

        // ── Rotating clip pools (WO-171) ─────────────────────────────────────
        // Some contexts have MORE THAN ONE clip and rotate per request so the
        // music varies (battle: 3 owner tracks; overworld: 2 tracks). The pools
        // are populated by AudioBootstrap (Resources) or SetMusicClip/AddMusicClip.
        // _battleClip / (the registry's Overworld asset) remain the FIRST entry so
        // the single-clip fallback path still works when a pool is empty.

        [Tooltip("Battle music pool (battle / battle2 / battle3). PlayMusic(Battle) " +
                 "rotates through these so fights are not monotonous. Empty -> _battleClip.")]
        [SerializeField] private List<AudioClip> _battlePool = new List<AudioClip>();

        [Tooltip("Overworld (open-world) music pool (world / mainworld1). PlayMusic(Overworld) " +
                 "rotates through these. Empty -> the single Overworld clip.")]
        [SerializeField] private List<AudioClip> _overworldPool = new List<AudioClip>();

        // The single Overworld clip (first/default), assignable like the others.
        private AudioClip _overworldClip;

        // Round-robin cursors for the pools — advanced each time the pooled track
        // is (re)started, so successive battles / world visits get different music.
        private int _battleCursor;
        private int _overworldCursor;

        // ── Runtime ──────────────────────────────────────────────────────────

        // MUSIC OWNERSHIP MOVED (2026-07-09, MUSIC_AUTHORITY_DESIGN): AudioService
        // no longer owns any music AudioSource. The single owner is MusicDirector
        // (one A/B pair). AudioService is now a POLICY PROVIDER + clip-lookup facade:
        // PlayMusic/StopMusic map a track → a MusicLayer and Push/Release the
        // director. Two beds are impossible by construction — there is one pair.
        private MusicDirector _director;

        // The single layer AudioService's facade currently occupies. PlayMusic moves
        // this slot (Release old, Push new) so the facade behaves as REPLACE while
        // the director still layers it against the other policy providers (battle,
        // wave) so the highest active layer wins and only one bed ever sounds.
        private MusicLayer _facadeLayer = MusicLayer.None;

        // One pooled SFX source set, round-robin, so concurrent one-shots do not
        // cut each other off. Routed per call to the SFX / UI / Voice groups.
        private const int SfxVoices = 8;
        private readonly List<AudioSource> _sfxVoices = new List<AudioSource>(SfxVoices);
        private int _sfxCursor;

        /// <summary>The track AudioService's facade last requested (or fading in). <see cref="MusicTrack.None"/> when stopped.</summary>
        public MusicTrack CurrentTrack { get; private set; } = MusicTrack.None;

        // True once master mute is on (audio-mix-spec §5). Mute snaps — no fade.
        private bool _muted;

        // =====================================================================
        //  Exposed-mixer-parameter contract
        // =====================================================================

        /// <summary>
        /// The exposed AudioMixer parameter names — the documented contract the
        /// Settings menu drives via <see cref="AudioMixer.SetFloat"/>. Mirrors
        /// the five mixer groups. These strings MUST match the exposed-parameter
        /// names in <c>Assets/Audio/DeNelleAudioMixer.mixer</c> exactly.
        /// </summary>
        public static class MixerParams
        {
            /// <summary>Master group exposed volume parameter.</summary>
            public const string Master = "MasterVol";
            /// <summary>Music group exposed volume parameter.</summary>
            public const string Music = "MusicVol";
            /// <summary>SFX group exposed volume parameter.</summary>
            public const string Sfx = "SfxVol";
            /// <summary>UI group exposed volume parameter.</summary>
            public const string Ui = "UiVol";
            /// <summary>Voice group exposed volume parameter.</summary>
            public const string Voice = "VoiceVol";
        }

        /// <summary>The five mixer groups a caller can address via <see cref="SetVolume"/>.</summary>
        public enum MixerGroup
        {
            /// <summary>Master — scales every other group.</summary>
            Master,
            /// <summary>Music — the BGM tracks.</summary>
            Music,
            /// <summary>SFX — gameplay one-shots (abilities, enemies, building).</summary>
            Sfx,
            /// <summary>UI — menu / button one-shots.</summary>
            Ui,
            /// <summary>Voice — NPC speech / voice-over.</summary>
            Voice,
        }

        // =====================================================================
        //  Lifecycle
        // =====================================================================

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                if (Application.isPlaying) Destroy(gameObject);
                return;
            }
            _instance = this;
            if (Application.isPlaying)
                DontDestroyOnLoad(gameObject);

            BuildAudioSources();
            ResolveMixerGroups();
            CoreServices.RegisterAudio(this);
        }

        private void OnEnable()
        {
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        private void OnDisable()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
        }

        private void OnDestroy()
        {
            CoreServices.UnregisterAudio(this);
            if (_instance == this) _instance = null;
        }

        private void Start()
        {
            // One-time mixer check, deferred to Start so AudioBootstrap's
            // post-Awake SetMixer call has had its chance — Awake runs the
            // moment AddComponent returns, before the bootstrap can hand over
            // the Resources-loaded mixer.
            if (_mixer == null)
                Debug.LogWarning(
                    "[AudioService] No AudioMixer assigned — music/SFX play on the " +
                    "default output and SetVolume falls back to per-source volume. " +
                    "Ship Assets/Audio/Resources/Audio/GameAudioMixer.mixer for full " +
                    "mix control.");

            // Apply the player's persisted volume + mute settings on boot. The
            // Settings menu can override these live; this just seeds the mixer
            // from GameState so launch audio matches the saved preference.
            ApplyPersistedSettings();
            ApplyMobilePlatformRules();

            // Pick up whatever scene loaded before the service existed (the
            // bootstrap may have created us after the first scene's sceneLoaded
            // already fired).
            HandleSceneMusic(SceneManager.GetActiveScene().name);
        }

        /// <summary>
        /// Builds the SFX voice pool as children of this GameObject and creates the
        /// single <see cref="MusicDirector"/> (the ONLY owner of music sources — its
        /// A/B pair also parents here). All are 2D (spatialBlend 0) — music + UI are
        /// non-positional. Idempotent: a second call is a no-op.
        /// </summary>
        private void BuildAudioSources()
        {
            if (_director == null)
                _director = MusicDirector.GetOrCreate(transform, _musicGroup, ClipFor);

            if (_sfxVoices.Count == 0)
                for (int i = 0; i < SfxVoices; i++)
                    _sfxVoices.Add(CreateChildSource($"Sfx_{i}", _sfxGroup, loop: false));
        }

        private AudioSource CreateChildSource(string srcName, AudioMixerGroup group, bool loop)
        {
            var go = new GameObject(srcName);
            go.transform.SetParent(transform, false);
            var src = go.AddComponent<AudioSource>();
            src.playOnAwake = false;
            src.loop = loop;
            src.spatialBlend = 0f;          // 2D — music + UI are non-positional.
            src.volume = 0f;
            if (group != null) src.outputAudioMixerGroup = group;
            return src;
        }

        // =====================================================================
        //  Mixer wiring
        // =====================================================================

        /// <summary>
        /// Assigns the mixer + the four routed groups. Public so
        /// <see cref="AudioBootstrap"/> can hand the service a Resources-loaded
        /// mixer before <see cref="Awake"/>'s source build. Re-resolves the
        /// group routing on every existing AudioSource.
        /// </summary>
        public void SetMixer(AudioMixer mixer)
        {
            _mixer = mixer;
            ResolveMixerGroups();
        }

        /// <summary>
        /// Resolves the five mixer groups off <see cref="_mixer"/> by name and
        /// re-routes the live AudioSources. Safe to call repeatedly. When the
        /// mixer is unassigned the service still works — sources route to the
        /// default output and <see cref="SetVolume"/> per-source-clamps instead.
        /// </summary>
        private void ResolveMixerGroups()
        {
            // No mixer yet — AudioBootstrap may still hand one over via SetMixer
            // right after Awake. The one-time "no mixer" warning is deferred to
            // Start() so it only fires when no mixer ever arrives.
            if (_mixer == null)
                return;

            // FindMatchingGroups returns every group whose path ends in the
            // name; the mixer has exactly one of each, so [0] is correct.
            _musicGroup  = FirstGroup("Music")  ?? _musicGroup;
            _sfxGroup    = FirstGroup("SFX")    ?? _sfxGroup;
            _uiGroup     = FirstGroup("UI")     ?? _uiGroup;
            _voiceGroup  = FirstGroup("Voice")  ?? _voiceGroup;

            _director?.SetMusicGroup(_musicGroup);
            foreach (var v in _sfxVoices)
                if (v != null) v.outputAudioMixerGroup = _sfxGroup;
        }

        private AudioMixerGroup FirstGroup(string groupName)
        {
            var matches = _mixer.FindMatchingGroups(groupName);
            return (matches != null && matches.Length > 0) ? matches[0] : null;
        }

        // =====================================================================
        //  PUBLIC API — music
        // =====================================================================

        /// <summary>
        /// Crossfades the BGM to <paramref name="track"/> using that track's
        /// owner-locked fade durations (audio-mix-spec.md §2/§3). Requesting the
        /// track that is already playing is a no-op (the audioManager's
        /// "currentTrack === track" short-circuit). <see cref="MusicTrack.None"/>
        /// fades out to silence.
        /// </summary>
        public void PlayMusic(MusicTrack track)
        {
            if (track == MusicTrack.None)
            {
                StopMusic();
                return;
            }

            // WO-682: a combat/arena cue IS the battle-load moment (BattleArena.
            // BeginEncounter / ArenaMode.TryStartRaid route here via the Core seam) —
            // pre-warm the combat SFX decode now, inside the masked transition, so the
            // first swing never pays the first-use FSB decode stall (db-proven
            // 167ms/4000ms frames on WebGL). Once per session; cheap re-entry.
            if (track == MusicTrack.Battle || track == MusicTrack.Arena)
                PrewarmCombatSfx();

            // Idempotency (restored — F8 2026-07-10 music-thrash): already on this track and sounding →
            // don't re-roll the rotation pool + re-crossfade. Pooled tracks (Battle/Overworld) return a
            // NEW clip object each ClipFor call, defeating the director's ReferenceEquals guard, so
            // repeated same-track requests fed the supersede storm that stacked two beds.
            if (track == CurrentTrack && _director != null && _director.IsAnyPlaying)
                return;

            MusicTrackDef def = MusicTrackRegistry.Get(track);
            if (def == null)
            {
                Debug.LogWarning($"[AudioService] No mix definition for track '{track}' — ignored.");
                return;
            }

            // Clip lookup stays HERE (AudioService owns the serialized clips +
            // rotation pools); the director owns PLAYBACK. A pooled track (Battle /
            // Overworld) rotates its next clip on each call.
            AudioClip clip = ClipFor(track);
            CurrentTrack = track; // record intent so a later SetMusicClip + re-request works.
            if (clip == null)
            {
                // GUARDED missing clip — wire the path, log the gap, play silent.
                // No invented audio. Leave whatever is currently sounding untouched.
                Debug.LogWarning(
                    $"[AudioService] Music track '{track}' has no clip — expected at " +
                    $"'{def.AssetPath}'. Import the MP3 and assign it on the AudioService " +
                    "(or via Resources). Scene plays silent; no audio invented.");
                return;
            }

            // Map the track to its layer and move the facade's single slot: release
            // the layer we were on if it differs, then push the new one. The director
            // resolves the highest active layer across ALL policy providers, so this
            // never doubles with the battle/wave beds — only the top sounds.
            MusicLayer layer = MusicDirector.LayerFor(track);
            if (_facadeLayer != MusicLayer.None && _facadeLayer != layer)
                _director?.Release(_facadeLayer);
            _facadeLayer = layer;
            _director?.PushClip(layer, clip, def.DefaultVolume, def.Loop,
                                Mathf.Max(0.01f, def.FadeInSeconds), track.ToString(), track);
        }

        /// <summary>
        /// WebGL mobile-audio gesture unlock. iOS Safari (and mobile Chrome) start the
        /// AudioContext SUSPENDED until a user gesture, so music started at scene-load
        /// is silent (owner 2026-06-02: "dont hear any audio on mobile"). Called once by
        /// <see cref="WebGLAudioUnlock"/> on the first tap/click/key — un-pauses the
        /// listener and re-asserts the current track so it actually plays now the context
        /// is live. No-op when nothing is queued.
        /// </summary>
        public void ResumeAfterUnlock()
        {
            AudioListener.pause = false;
            _director?.Reassert();   // re-play the current top from scratch now the context is live
        }

        /// <summary>
        /// Releases the facade's music layer — the director auto-falls back to the
        /// next-highest active layer (e.g. a live battle bed), or fades to silence
        /// when nothing else is held. Exposed so the cinematic intro can drop the
        /// boot/title music while the video's own voiceover plays.
        /// </summary>
        public void StopMusic()
        {
            CurrentTrack = MusicTrack.None;
            if (_facadeLayer != MusicLayer.None)
            {
                _director?.Release(_facadeLayer);
                _facadeLayer = MusicLayer.None;
            }
        }

        /// <summary>
        /// The clip for a track, or null when not yet imported. Pooled tracks
        /// (Battle / Overworld) pick the NEXT clip from their rotation pool so the
        /// music varies per request (WO-171); they fall back to the single clip
        /// when the pool is empty.
        /// </summary>
        private AudioClip ClipFor(MusicTrack track)
        {
            switch (track)
            {
                case MusicTrack.Title:   return _titleClip;
                case MusicTrack.Village: return _villageClip;
                case MusicTrack.Dungeon: return _dungeonClip;
                case MusicTrack.Battle:  return NextFromPool(_battlePool, ref _battleCursor) ?? _battleClip;
                case MusicTrack.Victory: return _victoryClip;
                case MusicTrack.Defeat:  return _defeatClip;
                case MusicTrack.Overworld: return NextFromPool(_overworldPool, ref _overworldCursor) ?? _overworldClip;
                case MusicTrack.Arena:   return _arenaClip;
                case MusicTrack.Raid:    return _raidClip;
                default:                 return null;
            }
        }

        /// <summary>
        /// Returns the next non-null clip from a rotation pool and advances the
        /// cursor, or null when the pool is empty / all-null (caller falls back to
        /// the single clip). One-entry pools just return that entry every time.
        /// </summary>
        private static AudioClip NextFromPool(List<AudioClip> pool, ref int cursor)
        {
            if (pool == null || pool.Count == 0) return null;
            // Scan at most pool.Count entries so an all-null pool returns null
            // rather than looping forever.
            for (int i = 0; i < pool.Count; i++)
            {
                AudioClip clip = pool[cursor % pool.Count];
                cursor = (cursor + 1) % pool.Count;
                if (clip != null) return clip;
            }
            return null;
        }

        /// <summary>
        /// Assigns a music clip at runtime — the seam an import pass or the
        /// integrator uses once an MP3 lands without touching the inspector. If
        /// the assigned track is the one currently intended but silent (a guarded
        /// missing clip), the music is (re)started.
        /// </summary>
        public void SetMusicClip(MusicTrack track, AudioClip clip)
        {
            switch (track)
            {
                case MusicTrack.Title:   _titleClip = clip;   break;
                case MusicTrack.Village: _villageClip = clip; break;
                case MusicTrack.Dungeon: _dungeonClip = clip; break;
                // Battle / Overworld are pooled: SetMusicClip assigns the FIRST /
                // default entry (also the single-clip fallback) and seeds the pool
                // with it; AddMusicClip appends the 2nd/3rd rotation entries.
                case MusicTrack.Battle:  _battleClip = clip; SeedPool(_battlePool, clip); break;
                case MusicTrack.Victory: _victoryClip = clip; break;
                case MusicTrack.Defeat:  _defeatClip = clip;  break;
                case MusicTrack.Overworld: _overworldClip = clip; SeedPool(_overworldPool, clip); break;
                case MusicTrack.Arena:   _arenaClip = clip;   break;
                case MusicTrack.Raid:    _raidClip = clip;    break;
            }

            // If this track was requested but silent for want of a clip, start it.
            if (clip != null && track == CurrentTrack && !IsAnyMusicPlaying())
            {
                CurrentTrack = MusicTrack.None; // clear the short-circuit guard.
                PlayMusic(track);
            }
        }

        /// <summary>
        /// Appends a clip to a pooled track's rotation (WO-171) — the seam an
        /// import pass uses to add the 2nd/3rd battle/overworld tracks. Only
        /// <see cref="MusicTrack.Battle"/> and <see cref="MusicTrack.Overworld"/>
        /// are pooled; other tracks fall through to <see cref="SetMusicClip"/>.
        /// Null clips and duplicates are ignored.
        /// </summary>
        public void AddMusicClip(MusicTrack track, AudioClip clip)
        {
            if (clip == null) return;
            switch (track)
            {
                case MusicTrack.Battle:
                    if (_battleClip == null) _battleClip = clip;
                    if (!_battlePool.Contains(clip)) _battlePool.Add(clip);
                    break;
                case MusicTrack.Overworld:
                    if (_overworldClip == null) _overworldClip = clip;
                    if (!_overworldPool.Contains(clip)) _overworldPool.Add(clip);
                    break;
                default:
                    SetMusicClip(track, clip);
                    break;
            }
        }

        // Seeds a rotation pool with its first/default clip (idempotent — a clip
        // already present is not duplicated; a null clip is ignored).
        private static void SeedPool(List<AudioClip> pool, AudioClip clip)
        {
            if (pool == null || clip == null) return;
            if (!pool.Contains(clip)) pool.Add(clip);
        }

        private bool IsAnyMusicPlaying()
        {
            return _director != null && _director.IsAnyPlaying;
        }

        // =====================================================================
        //  PUBLIC API — SFX
        // =====================================================================

        /// <summary>
        /// Fires a one-shot SFX on the SFX mixer group. Round-robins a small
        /// voice pool so concurrent shots do not cut each other off. A null clip
        /// is a guarded no-op. <paramref name="volume"/> is 0..1, pre-mixer.
        /// </summary>
        public void PlaySfx(AudioClip clip, float volume = 1f)
        {
            PlayOneShotOn(_sfxGroup, clip, volume);
        }

        /// <summary>Fires a one-shot routed to the UI mixer group (menu / button blips).</summary>
        public void PlayUiSfx(AudioClip clip, float volume = 1f)
        {
            PlayOneShotOn(_uiGroup, clip, volume);
        }

        // Cached UI-click blip — generated once, or an authored CC0 clip if dropped
        // at the audio key "Sfx/UiClick" (drop-in upgrade path, same convention as the
        // Village ProceduralSfx/EnemyCombatAudio fallbacks). Resolved through
        // DeNelle.Core.AudioAssetLoader (Addressables-first, Resources-fallback). DEF-183.
        private AudioClip _uiClickClip;

        /// <summary>
        /// DEF-183: plays the shared UI button-click blip on the UI mixer group.
        /// This is the IAudioService seam DeNelle.HUD calls (HUD references Core
        /// only) so buttons click without the HUD touching the Audio assembly. The
        /// clip is an authored "Sfx/UiClick" if present, otherwise a short
        /// generated tick — so it works fresh-clone with no asset wiring.
        /// </summary>
        public void PlayUiClick()
        {
            if (_uiClickClip == null)
                _uiClickClip = Guard.Try("Audio", "load UiClick clip",
                    () => AudioAssetLoader.LoadClip("Sfx/UiClick", optional: true), fallback: null) ?? GenerateUiClick();
            PlayUiSfx(_uiClickClip, 0.5f);
        }

        // A short, clean "tick" — high-ish sine with a fast exp decay, no noise, tail
        // tapered to avoid a click artifact. ~40 ms, mixed quietly by the caller.
        private static AudioClip GenerateUiClick()
        {
            const int rate = 44100;
            int n = Mathf.Max(16, (int)(0.04f * rate));
            var data = new float[n];
            float attack = 0.002f * rate;   // 2 ms attack
            double phase = 0;
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / n;
                float hz = Mathf.Lerp(900f, 700f, t);
                phase += 2.0 * System.Math.PI * hz / rate;
                float s = (float)System.Math.Sin(phase);
                float env = i < attack ? (i / attack) : Mathf.Exp(-9f * t);
                if (i > n - 48) env *= (n - i) / 48f;
                data[i] = s * env * 0.6f;
            }
            var clip = AudioClip.Create("sfx_ui_click", n, 1, rate, false);
            clip.SetData(data, 0);
            return clip;
        }

        /// <summary>Fires a one-shot routed to the Voice mixer group (NPC speech / VO).</summary>
        public void PlayVoice(AudioClip clip, float volume = 1f)
        {
            PlayOneShotOn(_voiceGroup, clip, volume);
        }

        /// <summary>
        /// Fires a positional one-shot SFX by <see cref="SfxId"/>. The clip is resolved
        /// from the <see cref="SfxClipLibrary"/> (or is a no-op when unassigned).
        /// Passing <see cref="SfxId.None"/> is a safe no-op. WO-62.
        /// </summary>
        /// <param name="id">The SFX event identifier.</param>
        /// <param name="worldPosition">World-space position — reserved for future 3-D source routing.</param>
        /// <param name="volume">Pre-mixer volume, 0..1.</param>
        public void PlaySfxAtPosition(SfxId id, Vector3 worldPosition, float volume = 1f)
        {
            if (id == SfxId.None) return;

            // WO-243: if no library was wired in the Inspector, try loading one ONCE via
            // the audio seam (the path the SfxClipLibraryBuilder writes to). There is no
            // DeNelleAudioService prefab to carry an inspector ref, so this is how an
            // authored asset actually reaches the service. Null stays null (the synth
            // fallback below covers it), and _sfxLibraryResolved stops us re-loading.
            if (_sfxLibrary == null && !_sfxLibraryResolved)
            {
                // WO-682: guarded — a corrupt/unloadable library asset is ONE logged
                // line + the synth fallback, never a throw up the combat call stack.
                _sfxLibrary = Guard.Try("Audio", "load SfxClipLibrary",
                    () => AudioAssetLoader.LoadAudioAsset<SfxClipLibrary>(SfxLibraryResourcePath, optional: true), fallback: null);
                _sfxLibraryResolved = true;
            }

            // 1) Authored asset, if a SfxClipLibrary is wired (Inspector or Resources).
            //    WO-682: resolve guarded — a bad row logs once and falls to the synth.
            AudioClip clip = _sfxLibrary != null
                ? Guard.Try("Audio", $"resolve clip for SfxId.{id}", () => _sfxLibrary.GetClip(id), fallback: null)
                : null;
            float vol = volume;
            if (clip != null && _sfxLibrary != null)
                vol = volume * _sfxLibrary.GetVolume(id);

            // 2) WO-243 fallback: no library / no authored clip for this id -> use a
            //    procedurally-generated clip so PlaySfxAtPosition is NEVER a silent
            //    no-op. This is the SAME no-asset-needed approach GameSfx already uses
            //    for tower/wave SFX; it makes the whole SfxId->clip subsystem audible
            //    fresh-clone with zero asset wiring. An authored SfxClipLibrary still
            //    WINS when present (drop-in upgrade path), so real recorded clips can
            //    replace the synth later with no code change.
            if (clip == null)
                clip = ProceduralSfx.For(id);

            // Guarded: a null clip is still a silent no-op — missing SFX never throws.
            PlayOneShotOn(_sfxGroup, clip, vol);
        }

        private void PlayOneShotOn(AudioMixerGroup group, AudioClip clip, float volume)
        {
            if (clip == null) return; // guarded — a missing SFX is silent, not an error.

            // WO-682: a clip whose audio data cannot decode (WebGL "Loading FSB failed"
            // class) is quarantined on first sight — ONE Warn at mark time, then every
            // later play is a silent skip. Never error-level spam, never a per-play stall.
            if (_deadSfxClips.Contains(clip)) return;
            if (clip.loadState == AudioDataLoadState.Failed)
            {
                MarkSfxClipDead(clip, "loadState=Failed at play time");
                return;
            }

            if (_sfxVoices.Count == 0) return;

            AudioSource voice = _sfxVoices[_sfxCursor];
            _sfxCursor = (_sfxCursor + 1) % _sfxVoices.Count;

            // Route this voice to the requested group for this shot.
            voice.outputAudioMixerGroup = group ?? _sfxGroup;
            voice.PlayOneShot(clip, Mathf.Clamp01(volume));
        }

        // =====================================================================
        //  Combat SFX pre-warm + dead-clip quarantine (WO-682)
        // =====================================================================

        // Clips whose audio data failed to decode (the WebGL "Loading FSB failed"
        // class). Marked once with a single Warn; skipped silently by every later
        // play — the quiet-catch path, never error-level spam.
        private readonly HashSet<AudioClip> _deadSfxClips = new HashSet<AudioClip>();

        // One-shot guard — the combat set is decoded once per session (loaded clips
        // stay resident; re-entering battle re-runs nothing).
        private bool _sfxPrewarmed;

        // The combat-relevant "Sfx/*" audio keys the Village-side players
        // (GameSfx / EnemyCombatAudio / AbilityAudioBridge / the owner sound drops)
        // lazy-load on first use. Pre-warming these at battle load moves the WebGL
        // first-use FSB decode (db-proven 167ms/4000ms frame stalls) into the masked
        // load transition. A name with no authored clip is fine — the synth
        // fallbacks cover those, nothing to warm.
        private static readonly string[] CombatSfxResourceNames =
        {
            "Sfx/SwordSwing", "Sfx/WeaponDraw", "Sfx/DragonRoar",
            "Sfx/SwordClash", "Sfx/SwordClash2", "Sfx/SwordClash3", "Sfx/SwordClash4",
            "Sfx/SpellCast", "Sfx/HeroHit",
            "Sfx/EnemyHit", "Sfx/EnemyDeath", "Sfx/EnemyDeath2", "Sfx/EnemyCastCharge",
            "Sfx/Heal", "Sfx/Spell_Impact", "Sfx/Swords_Clash",
            "Sfx/TowerFire", "Sfx/TowerArrowHit", "Sfx/WaveStart", "Sfx/LookoutHorn",
        };

        /// <summary>
        /// Pre-warms the combat SFX set at the battle/arena-load moment (WO-682,
        /// owner ask: "can we pre warm those files on battle load?"): resolves the
        /// authored SfxClipLibrary entries + the Resources/Sfx combat clips and
        /// <see cref="AudioClip.LoadAudioData"/>s each, so the decode cost lands
        /// inside the load transition instead of the first swing. A clip that fails
        /// to decode is marked dead (ONE <c>Warn</c>) and skipped by PlaySfx
        /// thereafter. Runs once per session. Every step is Guard-wrapped — an
        /// UNEXPECTED throw in the loop itself is error-level via Guard.Report
        /// (a true failure is never downgraded), while a failed decode is the
        /// expected/handled class and stays a Warn.
        /// </summary>
        public void PrewarmCombatSfx()
        {
            if (_sfxPrewarmed) return;
            _sfxPrewarmed = true;

            FlowTrace.Step("Audio", "PrewarmCombatSfx: begin (battle-load decode warm, WO-682)");
            int warmed = 0, dead = 0, missing = 0;
            Guard.Try("Audio", "prewarm combat sfx", () =>
            {
                var clips = new List<AudioClip>();

                // (a) Every authored SfxClipLibrary entry — resolved through the same
                //     one-shot path PlaySfxAtPosition uses (no asset = graceful skip).
                if (_sfxLibrary == null && !_sfxLibraryResolved)
                {
                    _sfxLibrary = Guard.Try("Audio", "load SfxClipLibrary (prewarm)",
                        () => AudioAssetLoader.LoadAudioAsset<SfxClipLibrary>(SfxLibraryResourcePath, optional: true), fallback: null);
                    _sfxLibraryResolved = true;
                }
                if (_sfxLibrary != null && _sfxLibrary.Entries != null)
                    foreach (var e in _sfxLibrary.Entries)
                        if (e.Clip != null && !clips.Contains(e.Clip)) clips.Add(e.Clip);

                // (b) The lazily-loaded Resources/Sfx combat set.
                foreach (string path in CombatSfxResourceNames)
                {
                    var loaded = Guard.Try("Audio", $"load '{path}' (prewarm)",
                        () => AudioAssetLoader.LoadClip(path, optional: true), fallback: null);
                    if (loaded == null) { missing++; continue; } // no authored clip — synth fallback covers it.
                    if (!clips.Contains(loaded)) clips.Add(loaded);
                }

                foreach (var clip in clips)
                {
                    if (_deadSfxClips.Contains(clip)) { dead++; continue; }
                    bool ok = Guard.Try("Audio", $"prewarm decode '{clip.name}'", () =>
                    {
                        if (clip.loadState == AudioDataLoadState.Unloaded)
                            clip.LoadAudioData();
                    });
                    if (!ok || clip.loadState == AudioDataLoadState.Failed)
                    {
                        MarkSfxClipDead(clip, "prewarm decode failed");
                        dead++;
                    }
                    else
                    {
                        warmed++;
                    }
                }
            });

            FlowTrace.Step("Audio",
                $"PrewarmCombatSfx: prewarmed {warmed} clips, {dead} failed, {missing} names with no authored clip (synth fallback)");
        }

        /// <summary>
        /// Quarantines a clip whose audio data cannot decode (the WebGL "Loading FSB
        /// failed" class — expected/handled, so a <c>Warn</c>, not a <c>Fail</c>).
        /// The first mark logs ONE Warn; every later play of the clip is a silent
        /// skip (WO-682 quiet-catch).
        /// </summary>
        private void MarkSfxClipDead(AudioClip clip, string why)
        {
            if (clip == null || !_deadSfxClips.Add(clip)) return;
            FlowTrace.Warn("Audio", $"sfx clip '{clip.name}' marked DEAD ({why}) — plays silent from now on");
        }

        // =====================================================================
        //  PUBLIC API — volume + mute
        // =====================================================================

        /// <summary>
        /// Sets a mixer group's volume from a linear 0..1 slider value (Master
        /// accepts up to 1.5 — audio-mix-spec §2 lets the master PUSH past 1.0).
        /// Converts the linear value to the mixer's decibel scale and writes the
        /// exposed parameter. When no mixer is assigned, falls back to scaling
        /// the music AudioSources directly so the game is never stuck loud.
        /// </summary>
        public void SetVolume(MixerGroup group, float linear01)
        {
            float max = (group == MixerGroup.Master) ? 1.5f : 1f;
            float v = Mathf.Clamp(linear01, 0f, max);

            if (_mixer != null)
                _mixer.SetFloat(ParamNameFor(group), LinearToDecibels(v));

            // ALSO scale the music source directly — not "mixer XOR source". The
            // music sources can end up NOT routed through the mixer's Music group,
            // so the mixer param has no effect on what's heard and the ♪ toggle /
            // volume slider would do nothing. The director drives its active source
            // (guarded so it never fights an in-flight crossfade; respects mute).
            if (group == MixerGroup.Master || group == MixerGroup.Music)
                _director?.ApplyVolumeScale(v);
        }

        /// <summary>
        /// Reads back a mixer group's volume as a linear 0..1(.5) value, or
        /// returns <paramref name="fallback"/> when no mixer is assigned / the
        /// parameter is not exposed.
        /// </summary>
        public float GetVolume(MixerGroup group, float fallback = 1f)
        {
            if (_mixer != null && _mixer.GetFloat(ParamNameFor(group), out float db))
                return DecibelsToLinear(db);
            return fallback;
        }

        /// <summary>
        /// Master mute toggle (audio-mix-spec.md §5). Mute SNAPS — ignores fade
        /// durations — and is applied on the Master mixer parameter so every
        /// group is silenced at once. Un-muting restores the Master parameter to
        /// 0 dB (the Settings menu's own slider then re-asserts its value).
        /// </summary>
        public void SetMuted(bool muted)
        {
            _muted = muted;
            if (_mixer != null)
            {
                _mixer.SetFloat(MixerParams.Master, muted ? -80f : 0f);
            }
            {
                // ALWAYS snap every source's mute flag too (not just the no-mixer
                // path). When routing to the mixer group failed, the Master param
                // can't silence the sources — this is what actually mutes/unmutes
                // audible playback, so the ♪ toggle works regardless of routing.
                // The director owns the music sources; AudioService owns the SFX pool.
                _director?.SetMuted(muted);
                foreach (var v in _sfxVoices)
                    if (v != null) v.mute = muted;
            }
        }

        /// <summary>True when master mute is engaged.</summary>
        public bool IsMuted => _muted;

        private static string ParamNameFor(MixerGroup group)
        {
            switch (group)
            {
                case MixerGroup.Master: return MixerParams.Master;
                case MixerGroup.Music:  return MixerParams.Music;
                case MixerGroup.Sfx:    return MixerParams.Sfx;
                case MixerGroup.Ui:     return MixerParams.Ui;
                case MixerGroup.Voice:  return MixerParams.Voice;
                default:                return MixerParams.Master;
            }
        }

        /// <summary>
        /// Linear-slider → mixer decibels. A linear 1.0 maps to 0 dB (unity gain);
        /// 0 maps to -80 dB (the mixer's silence floor). Logarithmic in between
        /// so the slider feels perceptually even.
        /// </summary>
        public static float LinearToDecibels(float linear)
        {
            if (linear <= 0.0001f) return -80f;
            return Mathf.Clamp(Mathf.Log10(linear) * 20f, -80f, 10f);
        }

        /// <summary>Mixer decibels → linear-slider value (inverse of <see cref="LinearToDecibels"/>).</summary>
        public static float DecibelsToLinear(float decibels)
        {
            if (decibels <= -80f) return 0f;
            return Mathf.Pow(10f, decibels / 20f);
        }

        /// <summary>
        /// Seeds the mixer from the player's persisted <see cref="GameState"/>
        /// audio settings on boot (MusicVolume / SfxVolume are 0..100; Muted is a
        /// bool). The Settings menu can override these live afterwards. Master is
        /// left at unity (0 dB) — the Settings menu owns the master slider.
        /// </summary>
        public void ApplyPersistedSettings()
        {
            GameState s = GameStateService.Instance != null
                ? GameStateService.Instance.State
                : null;
            if (s == null) return;

            // GameState volumes are 0..100 (React parity); convert to 0..1.
            SetVolume(MixerGroup.Music, Mathf.Clamp01(s.MusicVolume / 100f));
            SetVolume(MixerGroup.Sfx, Mathf.Clamp01(s.SfxVolume / 100f));
            // No persisted UI/Voice volume — leave at the mixer default (0 dB).
            SetMuted(s.Muted);
        }

        // =====================================================================
        //  Scene-music director (SceneRouter's "audio director")
        // =====================================================================

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            HandleSceneMusic(scene.name);
        }

        /// <summary>
        /// Maps a loaded scene name to its BGM track and crossfades to it
        /// (audio-mix-spec.md §3). The dungeon scene names are prefixed
        /// <c>Dungeon_</c> (SceneRouter.Dungeon); everything else matches the
        /// canonical scene constants. An unrecognised scene keeps the current
        /// track rather than cutting to silence.
        /// </summary>
        public void HandleSceneMusic(string sceneName)
        {
            MusicTrack track = TrackForScene(sceneName);
            if (track == MusicTrack.None) return; // unknown scene — leave music alone.

            // The Village scene is the AMBIENT/EXPLORE context: route it through
            // PlayAmbientContext so the player's chosen jukebox track (WO-162),
            // if any, plays instead of the hard-coded village track. Combat /
            // victory / defeat scenes/states still go straight to their track and
            // OVERRIDE — player choice never bleeds into a dramatic cue.
            if (track == MusicTrack.Village)
            {
                PlayAmbientContext(AmbientContext.Village);
                return;
            }

            PlayMusic(track);
        }

        /// <summary>
        /// The scene → music-track map. Public + static so a scene controller
        /// can ask "what track is mine?" without going through the service.
        /// </summary>
        public static MusicTrack TrackForScene(string sceneName)
        {
            if (string.IsNullOrEmpty(sceneName)) return MusicTrack.None;

            // Dungeon scenes are "Dungeon_<id>" (SceneRouter.Dungeon).
            if (sceneName.StartsWith("Dungeon_", StringComparison.Ordinal))
                return MusicTrack.Dungeon;

            if (sceneName == SceneRouter.Title)    return MusicTrack.Title;
            if (sceneName == SceneRouter.Village)  return MusicTrack.Village;
            // Castle hub scenes share the town/village ambient BGM (same explore
            // context as Village2 — respects the player's jukebox pick).
            // WO-608: the merged single scene (Main_Castle_Overworld, ff.MergedWorld) is
            // the home hub too — same explore/town ambient. Adding the literal is safe when
            // the flag's OFF (that scene never loads on the legacy path).
            if (sceneName == "MainCastle_Hall" || sceneName == "CastleHub" || sceneName == "CastleHub_MainKeep"
                || sceneName == "Main_Castle_Overworld")
                return MusicTrack.Village;
            if (sceneName == SceneRouter.ATBBattle) return MusicTrack.Battle;

            // The Onboarding scene (studio bumper / cold open), if it is a
            // separate scene, shares the title track per audio-mix-spec §2.
            if (sceneName == "Onboarding" || sceneName == "Splash")
                return MusicTrack.Title;

            return MusicTrack.None; // unrecognised — caller leaves music as-is.
        }

        // =====================================================================
        //  Ambient context + player music selection (WO-162 / WO-171)
        // =====================================================================
        //
        // The "ambient/explore" context is the music that plays while the player
        // is just existing in the world (Village or Overworld) — as opposed to a
        // state cue (Battle / Victory / Defeat) which OVERRIDES it. WO-162 lets
        // the player pick which ambient track they hear; WO-171 adds the Overworld
        // context so crossing into the open world swaps Village -> Overworld music
        // and combat still overrides, returning to the chosen ambient afterwards.
        //
        //   • The current ambient context is remembered (_ambientContext) so when
        //     combat ends a caller can ReturnToAmbient() and get the RIGHT track
        //     (village vs overworld) — and the player's CHOICE within it.
        //   • The choice is persisted in PlayerPrefs (mirrors how audio volume
        //     persists) keyed per context, so a "village jukebox" pick and an
        //     "overworld jukebox" pick can differ.

        /// <summary>The non-combat ambient contexts the player can be in (WO-171).</summary>
        public enum AmbientContext
        {
            /// <summary>In/around the village (town, home). Default track: Village.</summary>
            Village,
            /// <summary>In the open world. Default track: Overworld.</summary>
            Overworld,
        }

        // The ambient context currently in effect — what ReturnToAmbient() restores
        // once a Battle/Victory/Defeat cue finishes.
        private AmbientContext _ambientContext = AmbientContext.Village;

        /// <summary>The ambient context currently in effect (village vs overworld).</summary>
        public AmbientContext CurrentAmbientContext => _ambientContext;

        private const string AmbientChoicePrefKey = "dotr-ambient-music-choice-";

        /// <summary>The default music track for an ambient context.</summary>
        public static MusicTrack DefaultTrackFor(AmbientContext context)
        {
            return context == AmbientContext.Overworld ? MusicTrack.Overworld : MusicTrack.Village;
        }

        /// <summary>
        /// Enters an ambient context and plays its music — the player's chosen
        /// track for that context (WO-162) if one is set and available, otherwise
        /// the context default (Village / Overworld). This is the ONE entry point
        /// for "play the explore music": the Village scene loader, exploration regions,
        /// and combat-end all route through here so the right track
        /// (and the player's pick) always wins. Combat/Victory/Defeat call
        /// PlayMusic directly and OVERRIDE — they never come through here.
        /// </summary>
        public void PlayAmbientContext(AmbientContext context)
        {
            _ambientContext = context;
            MusicTrack chosen = GetAmbientChoice(context);
            MusicTrack track = (chosen != MusicTrack.None && MusicTrackRegistry.Has(chosen))
                ? chosen
                : DefaultTrackFor(context);
            PlayMusic(track);
        }

        /// <summary>
        /// Re-enters the current ambient context's music — the call combat code
        /// makes when a fight ends (instead of hard-coding "PlayMusic(Village)").
        /// Restores the player's chosen ambient track for whatever context they
        /// were in (village or overworld).
        /// </summary>
        public void ReturnToAmbient() => PlayAmbientContext(_ambientContext);

        /// <summary>
        /// Sets (and persists) the player's chosen ambient track for a context
        /// (WO-162 jukebox). <see cref="MusicTrack.None"/> clears the choice (the
        /// context falls back to its default). The choice survives sessions via
        /// PlayerPrefs. If the player is currently IN that ambient context, the
        /// new pick starts immediately.
        /// </summary>
        public void SetAmbientChoice(AmbientContext context, MusicTrack track)
        {
            PlayerPrefs.SetInt(AmbientChoicePrefKey + (int)context, (int)track);
            PlayerPrefs.Save();

            // Apply live if we're in this context and not mid-combat-cue.
            if (_ambientContext == context && !IsStateCue(CurrentTrack))
                PlayAmbientContext(context);
        }

        /// <summary>
        /// The player's persisted ambient-track choice for a context, or
        /// <see cref="MusicTrack.None"/> when unset / invalid (caller uses the
        /// context default).
        /// </summary>
        public MusicTrack GetAmbientChoice(AmbientContext context)
        {
            int raw = PlayerPrefs.GetInt(AmbientChoicePrefKey + (int)context, (int)MusicTrack.None);
            if (!Enum.IsDefined(typeof(MusicTrack), raw)) return MusicTrack.None;
            return (MusicTrack)raw;
        }

        /// <summary>Clears the player's ambient choice for a context (back to default).</summary>
        public void ClearAmbientChoice(AmbientContext context) => SetAmbientChoice(context, MusicTrack.None);

        /// <summary>
        /// True for the state cues that OVERRIDE ambient music and must never be
        /// supplanted by a jukebox pick (Battle / Victory / Defeat). Used so
        /// changing the ambient choice mid-fight doesn't stomp the combat track.
        /// </summary>
        private static bool IsStateCue(MusicTrack track)
        {
            return track == MusicTrack.Battle
                || track == MusicTrack.Victory
                || track == MusicTrack.Defeat;
        }

        /// <summary>
        /// The curated set of tracks a player may pick for an ambient context
        /// (WO-162). Authorable: add a clip + a MusicTrack + an entry here and it
        /// appears in the jukebox — no other code changes. Combat-state tracks are
        /// intentionally excluded (you can't pick "Battle" as your village ambient).
        /// Overworld is offered in both contexts as a calm exploration option.
        /// </summary>
        public static IReadOnlyList<MusicChoice> AmbientChoicesFor(AmbientContext context)
        {
            // Village ambient: Village (the town theme) + the open-world themes as
            // mellow alternatives + the title theme. Overworld ambient: the world
            // themes + the village theme. Curated, expandable, licensing-safe.
            if (context == AmbientContext.Overworld)
            {
                // WO-1857: the display NAME is player copy (MusicTrack.cs:62 says so); identity is
                // the MusicTrack enum, so localizing the label cannot break selection. "Elarion" and
                // "Echoes of Elarion" stay untranslated proper nouns; the descriptive parenthetical
                // travels (matches the common.echoes_elarion branding convention).
                return new List<MusicChoice>
                {
                    new MusicChoice(MusicTrack.Overworld,
                        new LocalizedText("audio.track.overworld").Resolve()),
                    new MusicChoice(MusicTrack.Village,
                        new LocalizedText("audio.track.village").Resolve()),
                    new MusicChoice(MusicTrack.Title,
                        new LocalizedText("audio.track.main_theme").Resolve()),
                };
            }

            return new List<MusicChoice>
            {
                new MusicChoice(MusicTrack.Village,
                    new LocalizedText("audio.track.village").Resolve()),
                new MusicChoice(MusicTrack.Overworld,
                    new LocalizedText("audio.track.overworld").Resolve()),
                new MusicChoice(MusicTrack.Title,
                    new LocalizedText("audio.track.main_theme").Resolve()),
            };
        }

        // =====================================================================
        //  SFX clip library (WO-62)
        // =====================================================================

        [Header("SFX Clip Library (WO-62)")]
        [Tooltip("Assign a SfxClipLibrary ScriptableObject here to enable PlaySfxAtPosition. " +
                 "When null, the service Resources-loads one (SfxLibraryResourcePath) and, " +
                 "failing that, falls back to ProceduralSfx so SFX are never silent (WO-243).")]
        [SerializeField] private SfxClipLibrary _sfxLibrary;

        // WO-243: the audio key the SfxClipLibraryBuilder writes the asset to (also its
        // Resources path), and a one-shot guard so we attempt the seam load at most once.
        // NOTE: no such asset exists on disk today — verified 2026-08-17 — so this always
        // falls through to ProceduralSfx. If one is ever authored UNDER a Resources folder
        // with clips wired into it, those clips are force-included in every build
        // regardless of the Addressables migration (see AudioAddressablesGrouper KEEP-BEHIND).
        private const string SfxLibraryResourcePath = "Audio/SfxClipLibrary";
        private bool _sfxLibraryResolved;

        // =====================================================================
        //  Mobile platform rules (WO-62)
        // =====================================================================

        /// <summary>
        /// Applies mobile-specific audio rules on Awake (WO-62):
        ///   • Reduces master SFX level by 4 dB to avoid harsh clipping on phone speakers.
        ///   • Disables the reverb send bus (too expensive on mobile GPUs).
        /// The AudioSource pool is already round-robin (8 voices) — no per-frame
        /// AddComponent. On non-mobile platforms this method is a no-op.
        /// </summary>
        private void ApplyMobilePlatformRules()
        {
            if (!Application.isMobilePlatform) return;

            if (_mixer != null)
            {
                // Reduce SFX bus slightly — phone speakers clip at full volume.
                _mixer.SetFloat(MixerParams.Sfx, LinearToDecibels(1f) - 4f);

                // Kill the reverb send — convolution reverb is too expensive on mobile.
                // The exposed param name matches the mixer's "ReverbSend" parameter.
                _mixer.SetFloat("ReverbSend", -80f);
            }

            // Mute flag is already respected by the voice pool; nothing extra needed.
        }

        // =====================================================================
        //  IAudioService explicit implementation (WO-41)
        // =====================================================================

        /// <summary>
        /// IAudioService.PlayMusic — maps the Core-side MusicTrack enum to the
        /// Audio-side MusicTrack enum and delegates to the full PlayMusic method.
        /// </summary>
        void IAudioService.PlayMusic(DeNelle.Core.Audio.MusicTrack track)
        {
            switch (track)
            {
                case DeNelle.Core.Audio.MusicTrack.Village: PlayMusic(MusicTrack.Village); break;
                case DeNelle.Core.Audio.MusicTrack.Battle:  PlayMusic(MusicTrack.Battle);  break;
                case DeNelle.Core.Audio.MusicTrack.Victory: PlayMusic(MusicTrack.Victory); break;
                case DeNelle.Core.Audio.MusicTrack.Dungeon: PlayMusic(MusicTrack.Dungeon); break;
                case DeNelle.Core.Audio.MusicTrack.Overworld: PlayAmbientContext(AmbientContext.Overworld); break;
                case DeNelle.Core.Audio.MusicTrack.Defeat:  PlayMusic(MusicTrack.Defeat);  break;
                // Arena raid BGM — "Echo's theme". Soft, looping background for a raid.
                case DeNelle.Core.Audio.MusicTrack.Arena:   PlayMusic(MusicTrack.Arena);   break;
                case DeNelle.Core.Audio.MusicTrack.Raid:    PlayMusic(MusicTrack.Raid);    break;
                // DEF-228: the cold-open intro primes the title theme (title.mp3) so it
                // is the opening music and the WebGL audio-unlock resumes THIS, not Overworld.
                case DeNelle.Core.Audio.MusicTrack.Title:   PlayMusic(MusicTrack.Title);   break;
            }
        }
    }
}

// =============================================================================
// MusicToggleBootstrap — a small, always-visible music ♪ on/off button on EVERY
// screen (owner: "missing the music on off toggle everywhere").
// -----------------------------------------------------------------------------
// Auto-installs at runtime (no scene wiring) the same way SettingsBootstrap does:
// a [RuntimeInitializeOnLoadMethod] + a SceneManager.sceneLoaded hook re-creates a
// tiny bottom-right toggle in each scene. It borrows a live UIDocument's
// PanelSettings (so it renders at the scene's UI scale) and drives music through
// SettingsModel — which persists to the save and is shared by the Settings screen,
// so the quick toggle and the full options menu stay in sync.
//
// Note the fresh-game default is Muted = true (a11y), so without a visible toggle
// a new player hears nothing AND has no obvious way to turn audio on; this button
// is that affordance. Turning music ON also clears the master mute so it's audible.
//
// LIVE PLAYBACK: SettingsModel.ApplyAll() persists + drives AudioMixerBridge, but
// that bridge NO-OPS when no AudioMixer asset is assigned — so the toggle changed
// the saved values without changing what the player hears. The ACTUAL player is
// DeNelle.Audio.AudioService (a DontDestroyOnLoad singleton) whose SetVolume has a
// per-source fallback that works WITHOUT a mixer. DeNelle.Settings cannot reference
// DeNelle.Audio (asmdef isolation), so we reach it via the project's reflection-
// bridge pattern (AudioServiceBridge below) to make the toggle audible immediately.
//
// Lives in DeNelle.Settings (references DeNelle.Core + UI Toolkit only).
// =============================================================================

using System;
using System.Reflection;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.SceneManagement;
using DeNelle.Core.UI;   // LocalizedText - WO-1857 (the toggle's tooltip + fallback face are player copy)

namespace DeNelle.Settings
{
    /// <summary>Installs the always-visible music toggle into every loaded scene.</summary>
    public static class MusicToggleBootstrap
    {
        private static bool s_hooked;

        // DISABLED (owner bug 2026-07-12, web/mobile): the always-on-screen music
        // button sat ON TOP of the mobile controls. The on/off affordance moved
        // UNDER Settings (SettingsController Audio section) — same SettingsModel +
        // AudioServiceBridge seam, so no logic was lost. We no longer auto-install
        // the HUD button. The MusicToggleHud + AudioServiceBridge types below stay
        // intact: AudioServiceBridge is the live-audio seam the Settings toggle uses.
        // Set FORCE_HUD_BUTTON = true only to bring the old overlay back for testing.
        private const bool ForceHudButton = false;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Init()
        {
            if (!ForceHudButton) return;   // HUD overlay retired — toggle lives in Settings now
            if (s_hooked) return;
            s_hooked = true;
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
            Install();   // the first scene is already loaded when this runs
        }

        private static void OnSceneLoaded(Scene s, LoadSceneMode mode) => Install();

        private static void Install()
        {
            if (UnityEngine.Object.FindAnyObjectByType<MusicToggleHud>() != null) return;

            // Borrow a PanelSettings from any UIDocument in the scene so the toggle
            // renders at that scene's UI scale (a code-built UIDocument with no
            // PanelSettings renders nothing — commit 30ff18b).
            PanelSettings ps = null;
            float topSort = float.MinValue;   // UIDocument.sortingOrder is a float
            foreach (var doc in UnityEngine.Object.FindObjectsByType<UIDocument>(FindObjectsInactive.Include))
            {
                if (doc == null || doc.panelSettings == null) continue;
                if (doc.sortingOrder >= topSort) { topSort = doc.sortingOrder; ps = doc.panelSettings; }
            }
            if (ps == null) return;   // no UI in this scene — nothing to host the toggle on

            // Inactive-then-activate so the UIDocument builds its root with the
            // PanelSettings already assigned (mirrors WaveFeedbackDirector).
            var go = new GameObject("MusicToggleHud");
            go.SetActive(false);
            var doc2 = go.AddComponent<UIDocument>();
            doc2.panelSettings = ps;
            doc2.sortingOrder = topSort + 50;   // above the scene's own UI
            go.AddComponent<MusicToggleHud>();
            go.SetActive(true);
        }
    }

    /// <summary>Builds + drives the ♪ toggle button on its own UIDocument root.</summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class MusicToggleHud : MonoBehaviour
    {
        private Button _btn;
        private bool _useSprite;   // hud_music sprite as the button face (dim when muted)

        private void OnEnable()
        {
            var doc = GetComponent<UIDocument>();
            var root = doc != null ? doc.rootVisualElement : null;
            if (root == null) return;
            root.pickingMode = PickingMode.Ignore;   // only the button captures clicks

            _btn = new Button(Toggle) { name = "music-toggle" };
            var s = _btn.style;
            s.position = Position.Absolute;
            s.top = 200f; s.right = 14f;              // top-right edge, below the icon cluster (was hidden under the bottom TOWN ACTIONS row)
            s.width = 44f; s.height = 44f;
            s.fontSize = 22f;
            s.unityFontStyleAndWeight = FontStyle.Bold;
            s.unityTextAlign = TextAnchor.MiddleCenter;
            s.color = Color.white;
            s.borderTopWidth = 0f; s.borderBottomWidth = 0f; s.borderLeftWidth = 0f; s.borderRightWidth = 0f;
            s.borderTopLeftRadius = 22f; s.borderTopRightRadius = 22f;
            s.borderBottomLeftRadius = 22f; s.borderBottomRightRadius = 22f;
            var musicSprite = Resources.Load<Sprite>("HudIcons/hud_music");
            if (musicSprite != null) { _btn.style.backgroundImage = new StyleBackground(musicSprite); _useSprite = true; }
            root.Add(_btn);
            Refresh();
        }

        /// <summary>Music is audible only when not master-muted AND its volume is up.</summary>
        private static bool MusicOn => !SettingsModel.Muted && SettingsModel.MusicVolume > 0.01f;

        private void Toggle()
        {
            if (MusicOn)
            {
                SettingsModel.MusicVolume = 0f;            // music off (SFX untouched)
                SettingsModel.ApplyAll();                  // push to the mixer + persist
                // Live effect: silence the music group on the running AudioService
                // (its per-source fallback works even with no mixer asset). Leave
                // master/SFX alone — this is a music-only toggle.
                AudioServiceBridge.SetMusicVolume(0f);
            }
            else
            {
                SettingsModel.Muted = false;               // ensure it's actually audible
                if (SettingsModel.MusicVolume < 0.01f)
                    SettingsModel.MusicVolume = SettingsModel.DefaultMusicVolume;
                SettingsModel.ApplyAll();                  // push to the mixer + persist
                // Live effect: clear master mute (the fresh-game default is muted)
                // and raise the music group back to the restored level so the
                // already-playing per-scene track becomes audible immediately.
                AudioServiceBridge.SetMuted(false);
                AudioServiceBridge.SetMusicVolume(SettingsModel.MusicVolume);
            }
            Refresh();
        }

        private void Refresh()
        {
            if (_btn == null) return;
            bool on = MusicOn;
            // WO-1857: tooltip and button face are DIFFERENT jobs (hover hint vs. control label),
            // so they carry four keys, not two shared ones.
            _btn.tooltip = on
                ? new LocalizedText("settings.music_toggle.tooltip_on").Resolve()
                : new LocalizedText("settings.music_toggle.tooltip_off").Resolve();
            if (_useSprite)
            {
                _btn.text = string.Empty;
                _btn.style.backgroundColor = Color.clear;
                _btn.style.opacity = on ? 1f : 0.4f;        // full when playing, dim when muted
            }
            else
            {
                // Words, not a glyph (owner reads by text) - now localized, WO-1857.
                _btn.text = on
                    ? new LocalizedText("settings.music_toggle.face_on").Resolve()
                    : new LocalizedText("settings.music_toggle.face_off").Resolve();
                _btn.style.backgroundColor = on
                    ? new Color(0.16f, 0.52f, 0.34f, 0.92f)
                    : new Color(0.40f, 0.13f, 0.13f, 0.92f);
            }
        }
    }

    /// <summary>
    /// Reflection bridge to <c>DeNelle.Audio.AudioService</c> — DeNelle.Settings
    /// cannot reference DeNelle.Audio (asmdef isolation), so we resolve the live
    /// singleton + its <c>SetMuted(bool)</c> / <c>SetVolume(MixerGroup, float)</c>
    /// methods by reflection (the project's reflection-bridge pattern; see
    /// DeNelle.Village.AbilityAudioBridge). Every call no-ops safely if the
    /// AudioService type, the singleton, or the members are absent.
    ///
    /// Why this exists: SettingsModel drives <c>AudioMixerBridge</c>, which NO-OPS
    /// when no AudioMixer asset is present — so the music toggle changed only the
    /// persisted values, not live playback. AudioService.SetVolume has a per-source
    /// fallback that adjusts the running music sources even with no mixer, so
    /// driving it here is what makes the toggle audible.
    /// </summary>
    internal static class AudioServiceBridge
    {
        private static bool s_resolved;
        private static PropertyInfo s_instanceProp;     // static AudioService Instance
        private static MethodInfo s_setMuted;           // void SetMuted(bool)
        private static MethodInfo s_setVolume;          // void SetVolume(MixerGroup, float)
        private static object s_musicGroupValue;        // MixerGroup.Music boxed enum value

        /// <summary>Master-unmute the live service (no-op if Audio absent).</summary>
        public static void SetMuted(bool muted)
        {
            Resolve();
            object inst = Inst();
            if (inst == null || s_setMuted == null) return;
            try { s_setMuted.Invoke(inst, new object[] { muted }); }
            catch { /* audio is best-effort */ }
        }

        /// <summary>
        /// Set the Music group's linear 0..1 volume on the live service. Uses
        /// AudioService's per-source fallback when no mixer asset is present, so it
        /// changes audible playback even pre-mixer. No-op if Audio is absent.
        /// </summary>
        public static void SetMusicVolume(float linear01)
        {
            Resolve();
            object inst = Inst();
            if (inst == null || s_setVolume == null || s_musicGroupValue == null) return;
            try { s_setVolume.Invoke(inst, new object[] { s_musicGroupValue, linear01 }); }
            catch { /* audio is best-effort */ }
        }

        private static object Inst()
        {
            if (s_instanceProp == null) return null;
            try { return s_instanceProp.GetValue(null); }
            catch { return null; }
        }

        private static void Resolve()
        {
            if (s_resolved) return;
            s_resolved = true;

            Type t = null;
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                t = asm.GetType("DeNelle.Audio.AudioService", false);
                if (t != null) break;
            }
            if (t == null) return;   // Audio module not present — every call no-ops.

            s_instanceProp = t.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static);
            s_setMuted = t.GetMethod("SetMuted", new[] { typeof(bool) });

            // SetVolume(MixerGroup group, float linear01) — MixerGroup is a nested
            // enum on AudioService; resolve it + its "Music" member by reflection.
            Type mixerGroup = t.GetNestedType("MixerGroup", BindingFlags.Public);
            if (mixerGroup != null && mixerGroup.IsEnum)
            {
                s_setVolume = t.GetMethod("SetVolume", new[] { mixerGroup, typeof(float) });
                try { s_musicGroupValue = Enum.Parse(mixerGroup, "Music"); }
                catch { s_musicGroupValue = null; }
            }
        }
    }
}

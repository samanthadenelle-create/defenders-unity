// =============================================================================
// ProceduralSfx (WO-243) — a generated AudioClip for every SfxId.
// -----------------------------------------------------------------------------
// Assembly: DeNelle.Audio   Namespace: DeNelle.Audio
//
// WHY THIS EXISTS (WO-243):
//   AudioService.PlaySfxAtPosition(SfxId,...) resolves its clip from a serialized
//   SfxClipLibrary asset (_sfxLibrary). No such .asset has ever been authored and
//   the field is unassigned in every scene/prefab, so EVERY SfxId-based SFX was a
//   silent no-op (WardStone ward-lit/dim, and all VFXManager VfxToSfx() sounds).
//
//   This class is the code fallback: a procedurally-synthesised clip for each
//   SfxId, generated once and cached. It mirrors GameSfx's approach exactly (a
//   sine sweep blended with noise + an exp-decay envelope + a tail taper to avoid
//   click artifacts) so the project stays fresh-clone-safe — no binary audio
//   assets, no scene wiring, nothing to bake.
//
// DROP-IN UPGRADE PATH (same convention as GameSfx / EnemyCombatAudio):
//   • An authored SfxClipLibrary assigned to AudioService WINS — the synth is only
//     used for ids the library leaves null (or when no library exists at all).
//   • A CC0/recorded clip dropped at Resources/Sfx/<ResourceName(id)> ALSO wins
//     over the synth for that id — so real audio content can replace any sound
//     later with zero code change.
//
// FLAG (content, not code): these are placeholder SYNTH tones — functional and
// distinct, but not final authored sound design. Replacing them with real clips
// (a library asset or Resources/Sfx drops) is a content task, not a code one.
// =============================================================================

using System.Collections.Generic;
using UnityEngine;

namespace DeNelle.Audio
{
    /// <summary>
    /// Generates (and caches) a placeholder AudioClip for every <see cref="SfxId"/>
    /// so <see cref="AudioService.PlaySfxAtPosition"/> is never a silent no-op when
    /// no <see cref="SfxClipLibrary"/> clip is wired (WO-243). An authored library
    /// clip or a Resources/Sfx drop-in overrides the synth for that id.
    /// </summary>
    internal static class ProceduralSfx
    {
        private const int Rate = 44100;

        // One cached clip per id — generated lazily on first request.
        private static readonly Dictionary<SfxId, AudioClip> s_cache =
            new Dictionary<SfxId, AudioClip>();

        /// <summary>
        /// Returns a cached clip for <paramref name="id"/> — a Resources/Sfx
        /// drop-in if one exists, otherwise a generated synth tone. Returns null
        /// only for <see cref="SfxId.None"/> (a safe silent no-op upstream).
        /// </summary>
        public static AudioClip For(SfxId id)
        {
            if (id == SfxId.None) return null;
            if (s_cache.TryGetValue(id, out var cached) && cached != null)
                return cached;

            // Authored CC0/recorded drop-in wins over the synth (same convention as
            // GameSfx): the audio key "Sfx/Sfx_<Id>", resolved through
            // DeNelle.Core.AudioAssetLoader (Addressables-first, Resources-fallback).
            // Missing -> fall through to synth.
            AudioClip clip = DeNelle.Core.AudioAssetLoader.LoadClip("Sfx/" + ResourceName(id), optional: true) ?? Generate(id);
            s_cache[id] = clip;
            return clip;
        }

        /// <summary>The "Sfx/" audio-key short name an authored override would use for an id.</summary>
        private static string ResourceName(SfxId id) => "Sfx_" + id;

        // Per-id synth recipe — distinct tone for each event so they read apart.
        private static AudioClip Generate(SfxId id)
        {
            switch (id)
            {
                // ── Impact sounds ────────────────────────────────────────────
                case SfxId.FireExplosion:
                    return Synth("sfx_fire_explosion", dur: 0.55f, f0: 220f, f1: 70f,  noise: 0.55f, amp: 0.85f, seed: 0x5111, decay: 1.7f);
                case SfxId.ArcaneExplosion:
                    return Synth("sfx_arcane_explosion", dur: 0.5f, f0: 640f, f1: 240f, noise: 0.25f, amp: 0.75f, seed: 0xA17C, decay: 2.0f);
                case SfxId.Shockwave:
                    return Synth("sfx_shockwave", dur: 0.35f, f0: 320f, f1: 90f,  noise: 0.4f,  amp: 0.85f, seed: 0x5403, decay: 2.4f);
                case SfxId.Heal:
                    return Synth("sfx_heal", dur: 0.4f, f0: 520f, f1: 880f, noise: 0.04f, amp: 0.55f, seed: 0x4EA1, decay: 1.8f);
                // WO-1717 PLACEHOLDER, not an art pick: a short, dry, noise-heavy low thud
                // that reads as stone rather than as an explosion, and that separates from
                // SfxId.EnemyDeath so a wall hit and a kill never sound alike. An authored
                // clip at the audio key "Sfx/Sfx_StructureImpact" overrides this with no
                // code change - the owner/artist still owes the real sound.
                case SfxId.StructureImpact:
                    return Synth("sfx_structure_impact", dur: 0.14f, f0: 260f, f1: 110f, noise: 0.7f, amp: 0.6f, seed: 0x5701, decay: 4.2f);

                // ── Casting / projectile sounds ──────────────────────────────
                case SfxId.WizardCast:
                    return Synth("sfx_wizard_cast", dur: 0.3f, f0: 380f, f1: 760f, noise: 0.22f, amp: 0.6f,  seed: 0x7E3C, decay: 2.6f);
                case SfxId.FlameArrowLaunch:
                    return Synth("sfx_flame_arrow", dur: 0.16f, f0: 1200f, f1: 480f, noise: 0.35f, amp: 0.65f, seed: 0xF1A0, decay: 4.0f);
                case SfxId.TowerShot:
                    return Synth("sfx_tower_shot", dur: 0.1f, f0: 1400f, f1: 600f, noise: 0.12f, amp: 0.6f,  seed: 0x70F1, decay: 4.0f);

                // ── Enemy sounds ─────────────────────────────────────────────
                case SfxId.EnemyDeath:
                    return Synth("sfx_enemy_death", dur: 0.5f, f0: 180f, f1: 90f, noise: 0.3f, amp: 0.8f, seed: 0x2E6C, decay: 1.6f);

                // ── Wave / progression sounds ────────────────────────────────
                case SfxId.WaveClear:
                    return Chime("sfx_wave_clear", baseHz: 440f, dur: 0.6f, steps: 3, amp: 0.7f);
                case SfxId.LevelUp:
                    return Chime("sfx_level_up", baseHz: 523f, dur: 0.5f, steps: 3, amp: 0.65f);

                // ── Combo sounds ─────────────────────────────────────────────
                case SfxId.ComboSmall:
                    return Synth("sfx_combo_small", dur: 0.12f, f0: 700f,  f1: 1000f, noise: 0.05f, amp: 0.55f, seed: 0xC0A1, decay: 3.5f);
                case SfxId.ComboBig:
                    return Chime("sfx_combo_big", baseHz: 660f, dur: 0.45f, steps: 2, amp: 0.7f);

                // ── Pet sounds ───────────────────────────────────────────────
                case SfxId.PetFireAura:
                    return Synth("sfx_pet_fire_aura", dur: 0.4f, f0: 160f, f1: 200f, noise: 0.5f, amp: 0.45f, seed: 0xB901, decay: 1.2f);
                case SfxId.PetAttack:
                    return Synth("sfx_pet_attack", dur: 0.13f, f0: 520f, f1: 260f, noise: 0.4f, amp: 0.7f, seed: 0x9A1B, decay: 3.6f);

                // ── Ward-tether sounds (WO-112) ──────────────────────────────
                case SfxId.WardLit:
                    return Chime("sfx_ward_lit", baseHz: 392f, dur: 0.55f, steps: 2, amp: 0.6f);
                case SfxId.WardDim:
                    return Synth("sfx_ward_dim", dur: 0.5f, f0: 240f, f1: 110f, noise: 0.1f, amp: 0.55f, seed: 0x0D14, decay: 1.4f);

                default:
                    // Any future SfxId without a recipe still gets an audible tick.
                    return Synth("sfx_generic", dur: 0.12f, f0: 600f, f1: 400f, noise: 0.2f, amp: 0.5f, seed: 0x1234, decay: 3.5f);
            }
        }

        // A sine sweep blended with noise, 6 ms attack, exponential decay, tail
        // taper to avoid clicks (mirrors GameSfx.Synth / ProceduralSfx in Village).
        private static AudioClip Synth(string name, float dur, float f0, float f1,
                                       float noise, float amp, int seed, float decay)
        {
            int n = Mathf.Max(16, (int)(dur * Rate));
            var data = new float[n];
            var rng = new System.Random(seed);
            double phase = 0;
            float attack = 0.006f * Rate;
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / n;
                float hz = Mathf.Lerp(f0, f1, t);
                phase += 2.0 * System.Math.PI * hz / Rate;
                float s = (float)System.Math.Sin(phase);
                float ns = (float)(rng.NextDouble() * 2.0 - 1.0);
                float v = Mathf.Lerp(s, ns, noise);
                float env = i < attack ? (i / attack) : Mathf.Exp(-decay * t);
                if (i > n - 64) env *= (n - i) / 64f;
                data[i] = v * env * amp;
            }
            var clip = AudioClip.Create(name, n, 1, Rate, false);
            clip.SetData(data, 0);
            return clip;
        }

        // A rising arpeggio chime — a clean, noise-free reward sting (wave clear,
        // level up, ward lit). `steps` notes of a major-ish arpeggio over `dur`.
        private static AudioClip Chime(string name, float baseHz, float dur, int steps, float amp)
        {
            int n = Mathf.Max(16, (int)(dur * Rate));
            var data = new float[n];
            // Simple rising ratios: root, major third, fifth, octave.
            float[] ratios = { 1f, 1.25f, 1.5f, 2f };
            steps = Mathf.Clamp(steps, 1, ratios.Length);
            float attack = 0.004f * Rate;
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / n;
                int step = Mathf.Clamp((int)(t * steps), 0, steps - 1);
                float hz = baseHz * ratios[step];
                // Phase reset per note keeps each step clean (no glide between notes).
                float localT = (t * steps) - step;
                double ph = 2.0 * System.Math.PI * hz * (localT * dur / steps);
                float s = (float)(System.Math.Sin(ph) * 0.8 + System.Math.Sin(ph * 2.0) * 0.2);
                float env = (localT * dur / steps) < (attack / Rate)
                    ? (localT * steps)
                    : Mathf.Exp(-3.0f * localT);
                if (i > n - 64) env *= (n - i) / 64f;
                data[i] = s * env * amp;
            }
            var clip = AudioClip.Create(name, n, 1, Rate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}

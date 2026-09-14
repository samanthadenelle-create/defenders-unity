// =============================================================================
// SfxClipLibraryBuilder (WO-243) — authors a populated SfxClipLibrary.asset.
// -----------------------------------------------------------------------------
// Assembly: DeNelle.EditorAudio   Namespace: DeNelle.Editor.Audio   EDITOR-ONLY
//
// WHY (WO-243): every PlaySfxAtPosition(SfxId,...) was a silent no-op because no
// SfxClipLibrary .asset has ever been authored and AudioService._sfxLibrary is
// unassigned. AudioService now also Resources-loads "Audio/SfxClipLibrary" and,
// failing that, synthesises clips at runtime (ProceduralSfx) — so SFX already
// play with NO asset present.
//
// This builder is the OPTIONAL upgrade path: it bakes a real, serialised clip per
// SfxId to disk as .wav files and wires them into a SfxClipLibrary .asset at the
// Resources path the service loads. Once built, the library WINS over the runtime
// synth (real referenceable assets, ready to be hand-swapped for recorded clips).
//
// It synthesises the SAME placeholder tones ProceduralSfx generates (the recipes
// are duplicated here because ProceduralSfx is internal-private to DeNelle.Audio;
// the audio content is equivalent), writes 16-bit mono PCM WAVs, imports them, and
// builds the library entries. Re-running OVERWRITES the WAVs + rebuilds the asset.
//
// RUN (headless, via run-unity-method.ps1):
//   .\run-unity-method.ps1 DeNelle.Editor.Audio.SfxClipLibraryBuilder.Build
// or in-editor: Defenders > Audio > Build SFX Clip Library.
//
// FLAG (content, not code): the WAVs are placeholder SYNTH tones — functional and
// distinct, NOT final sound design. Swap in recorded CC0 clips by replacing the
// Clip on each row of the .asset (or dropping Resources/Sfx/Sfx_<Id> overrides).
// =============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using DeNelle.Audio;
using UnityEditor;
using UnityEngine;

namespace DeNelle.Editor.Audio
{
    /// <summary>
    /// Bakes a placeholder .wav per <see cref="SfxId"/> and assembles them into a
    /// <see cref="SfxClipLibrary"/> asset the runtime loads (WO-243). Editor-only.
    /// </summary>
    public static class SfxClipLibraryBuilder
    {
        private const int Rate = 44100;

        // Resources paths the runtime AudioService loads from.
        //   Library : Resources.Load<SfxClipLibrary>("Audio/SfxClipLibrary")
        //   Clips   : authored under Resources/Sfx/Sfx_<Id> (also a synth-override path)
        private const string ResourcesRoot = "Assets/_Modules/Audio/Resources";
        private const string LibraryAssetPath = ResourcesRoot + "/Audio/SfxClipLibrary.asset";
        private const string ClipFolder = ResourcesRoot + "/Sfx";

        [MenuItem("Defenders/Audio/Build SFX Clip Library")]
        public static void Build()
        {
            EnsureFolder(ResourcesRoot);
            EnsureFolder(ResourcesRoot + "/Audio");
            EnsureFolder(ClipFolder);

            var entries = new List<SfxClipLibrary.Entry>();
            int built = 0;

            foreach (SfxId id in Enum.GetValues(typeof(SfxId)))
            {
                if (id == SfxId.None) continue;

                string wavPath = $"{ClipFolder}/Sfx_{id}.wav";
                float[] samples = Generate(id);
                WriteWav(wavPath, samples, Rate);
                built++;

                AssetDatabase.ImportAsset(wavPath, ImportAssetOptions.ForceUpdate);
                var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(wavPath);
                if (clip == null)
                {
                    Debug.LogWarning($"[SfxClipLibraryBuilder] Failed to import clip for {id} at {wavPath}.");
                    continue;
                }

                entries.Add(new SfxClipLibrary.Entry { Id = id, Clip = clip, Volume = 1f });
            }

            // Create or reuse the library asset, then assign the entries.
            var library = AssetDatabase.LoadAssetAtPath<SfxClipLibrary>(LibraryAssetPath);
            bool created = false;
            if (library == null)
            {
                library = ScriptableObject.CreateInstance<SfxClipLibrary>();
                AssetDatabase.CreateAsset(library, LibraryAssetPath);
                created = true;
            }

            library.Entries = entries.ToArray();
            EditorUtility.SetDirty(library);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"[SfxClipLibraryBuilder] WO-243 done: {built} WAVs synthesised, " +
                      $"{entries.Count} entries wired into {LibraryAssetPath} " +
                      $"({(created ? "created" : "updated")}). " +
                      "AudioService Resources-loads it at 'Audio/SfxClipLibrary'.");
        }

        // ── Folder helper ────────────────────────────────────────────────────
        private static void EnsureFolder(string assetPath)
        {
            if (AssetDatabase.IsValidFolder(assetPath)) return;
            string parent = Path.GetDirectoryName(assetPath).Replace('\\', '/');
            string leaf = Path.GetFileName(assetPath);
            if (!AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, leaf);
        }

        // ── Per-id synth (equivalent to ProceduralSfx recipes) ───────────────
        private static float[] Generate(SfxId id)
        {
            switch (id)
            {
                case SfxId.FireExplosion:    return Synth(0.55f, 220f, 70f,  0.55f, 0.85f, 0x5111, 1.7f);
                case SfxId.ArcaneExplosion:  return Synth(0.5f,  640f, 240f, 0.25f, 0.75f, 0xA17C, 2.0f);
                case SfxId.Shockwave:        return Synth(0.35f, 320f, 90f,  0.4f,  0.85f, 0x5403, 2.4f);
                case SfxId.Heal:             return Synth(0.4f,  520f, 880f, 0.04f, 0.55f, 0x4EA1, 1.8f);
                // WO-1717 placeholder masonry thud - kept byte-identical to the
                // ProceduralSfx.StructureImpact recipe so the baked .wav and the runtime
                // synth are the same sound, never two different placeholders.
                case SfxId.StructureImpact:  return Synth(0.14f, 260f, 110f, 0.7f,  0.6f,  0x5701, 4.2f);
                case SfxId.WizardCast:       return Synth(0.3f,  380f, 760f, 0.22f, 0.6f,  0x7E3C, 2.6f);
                case SfxId.FlameArrowLaunch: return Synth(0.16f, 1200f,480f, 0.35f, 0.65f, 0xF1A0, 4.0f);
                case SfxId.TowerShot:        return Synth(0.1f,  1400f,600f, 0.12f, 0.6f,  0x70F1, 4.0f);
                case SfxId.EnemyDeath:       return Synth(0.5f,  180f, 90f,  0.3f,  0.8f,  0x2E6C, 1.6f);
                case SfxId.WaveClear:        return Chime(440f, 0.6f,  3, 0.7f);
                case SfxId.LevelUp:          return Chime(523f, 0.5f,  3, 0.65f);
                case SfxId.ComboSmall:       return Synth(0.12f, 700f, 1000f,0.05f, 0.55f, 0xC0A1, 3.5f);
                case SfxId.ComboBig:         return Chime(660f, 0.45f, 2, 0.7f);
                case SfxId.PetFireAura:      return Synth(0.4f,  160f, 200f, 0.5f,  0.45f, 0xB901, 1.2f);
                case SfxId.PetAttack:        return Synth(0.13f, 520f, 260f, 0.4f,  0.7f,  0x9A1B, 3.6f);
                case SfxId.WardLit:          return Chime(392f, 0.55f, 2, 0.6f);
                case SfxId.WardDim:          return Synth(0.5f,  240f, 110f, 0.1f,  0.55f, 0x0D14, 1.4f);
                default:                     return Synth(0.12f, 600f, 400f, 0.2f,  0.5f,  0x1234, 3.5f);
            }
        }

        private static float[] Synth(float dur, float f0, float f1, float noise,
                                     float amp, int seed, float decay)
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
            return data;
        }

        private static float[] Chime(float baseHz, float dur, int steps, float amp)
        {
            int n = Mathf.Max(16, (int)(dur * Rate));
            var data = new float[n];
            float[] ratios = { 1f, 1.25f, 1.5f, 2f };
            steps = Mathf.Clamp(steps, 1, ratios.Length);
            float attack = 0.004f * Rate;
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / n;
                int step = Mathf.Clamp((int)(t * steps), 0, steps - 1);
                float hz = baseHz * ratios[step];
                float localT = (t * steps) - step;
                double ph = 2.0 * System.Math.PI * hz * (localT * dur / steps);
                float s = (float)(System.Math.Sin(ph) * 0.8 + System.Math.Sin(ph * 2.0) * 0.2);
                float env = (localT * dur / steps) < (attack / Rate)
                    ? (localT * steps)
                    : Mathf.Exp(-3.0f * localT);
                if (i > n - 64) env *= (n - i) / 64f;
                data[i] = s * env * amp;
            }
            return data;
        }

        // ── 16-bit mono PCM WAV writer ───────────────────────────────────────
        private static void WriteWav(string assetPath, float[] samples, int sampleRate)
        {
            string full = Path.GetFullPath(assetPath);
            Directory.CreateDirectory(Path.GetDirectoryName(full));

            int sampleCount = samples.Length;
            int byteRate = sampleRate * 2;           // mono, 2 bytes/sample
            int dataBytes = sampleCount * 2;

            using (var fs = new FileStream(full, FileMode.Create))
            using (var w = new BinaryWriter(fs))
            {
                // RIFF header
                w.Write(new[] { 'R', 'I', 'F', 'F' });
                w.Write(36 + dataBytes);
                w.Write(new[] { 'W', 'A', 'V', 'E' });
                // fmt chunk
                w.Write(new[] { 'f', 'm', 't', ' ' });
                w.Write(16);                          // PCM fmt chunk size
                w.Write((short)1);                    // PCM
                w.Write((short)1);                    // mono
                w.Write(sampleRate);
                w.Write(byteRate);
                w.Write((short)2);                    // block align
                w.Write((short)16);                   // bits/sample
                // data chunk
                w.Write(new[] { 'd', 'a', 't', 'a' });
                w.Write(dataBytes);
                for (int i = 0; i < sampleCount; i++)
                {
                    float clamped = Mathf.Clamp(samples[i], -1f, 1f);
                    w.Write((short)Mathf.RoundToInt(clamped * short.MaxValue));
                }
            }
        }
    }
}

// Builds the tracked static TMP fallback used by every enabled locale.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;

namespace DeNelle.Editor.Localization
{
    public static class LocaleFontBuilder
    {
        public const string SourcePath = "Assets/Localization/Fonts/Source/LiberationSans.ttf";
        public const string LicensePath = "Assets/Localization/Fonts/Source/LiberationSans-OFL.txt";
        public const string AssetPath = "Assets/Resources/Localization/Fonts/ElarionLocaleFallback.asset";
        public const string ResourcePath = "Localization/Fonts/ElarionLocaleFallback";
        private const string PolicyPath = "Assets/Editor/Localization/LocalizationPolicy.json";
        private const string LocaleRoot = "Assets/Resources/Data/Canonical";

        private static readonly string[] RoleFontPaths =
        {
            "Assets/Resources/RpgUi/font/font_body.asset",
            "Assets/Resources/RpgUi/font/font_title.asset",
            "Assets/Resources/RpgUi/font/font_stamp.asset",
        };

        [Serializable]
        private sealed class Policy
        {
            public List<LocaleSpec> supportedLocales;
        }

        [Serializable]
        private sealed class LocaleSpec
        {
            public string code;
            public bool enabledInBuild;
        }

        [MenuItem("Defenders/Localization/Build Tracked Locale Font")]
        public static void Build()
        {
            var source = AssetDatabase.LoadAssetAtPath<Font>(SourcePath);
            if (source == null) throw new InvalidOperationException("Tracked locale font source missing: " + SourcePath);
            if (!File.Exists(LicensePath)) throw new InvalidOperationException("Locale font license missing: " + LicensePath);

            string characters = BuildCharacterSet(out int localeCount, out int scalarCount);
            EnsureFolder(Path.GetDirectoryName(AssetPath)?.Replace('\\', '/'));

            var generated = TMP_FontAsset.CreateFontAsset(
                source, 64, 8, GlyphRenderMode.SDFAA, 2048, 2048,
                AtlasPopulationMode.Dynamic, enableMultiAtlasSupport: false);
            if (generated == null) throw new InvalidOperationException("TMP failed to create the locale fallback atlas.");
            if (!generated.TryAddCharacters(characters, out string missing) && !string.IsNullOrEmpty(missing))
            {
                UnityEngine.Object.DestroyImmediate(generated);
                throw new InvalidOperationException("Locale fallback source lacks required characters: " + missing);
            }

            generated.atlasPopulationMode = AtlasPopulationMode.Static;
            generated.name = "ElarionLocaleFallback";
            if (AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(AssetPath) != null)
                AssetDatabase.DeleteAsset(AssetPath);
            AssetDatabase.CreateAsset(generated, AssetPath);
            if (generated.atlasTextures != null && generated.atlasTextures.Length > 0 && generated.atlasTextures[0] != null)
            {
                generated.atlasTextures[0].name = generated.name + " Atlas";
                AssetDatabase.AddObjectToAsset(generated.atlasTextures[0], generated);
            }
            if (generated.material != null)
            {
                generated.material.name = generated.name + " Material";
                AssetDatabase.AddObjectToAsset(generated.material, generated);
            }
            EditorUtility.SetDirty(generated);

            foreach (string rolePath in RoleFontPaths)
            {
                var role = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(rolePath);
                if (role == null) throw new InvalidOperationException("Tracked role font missing: " + rolePath);
                var fallbacks = role.fallbackFontAssetTable ?? new List<TMP_FontAsset>();
                fallbacks.RemoveAll(font => font == null || font.name == generated.name);
                fallbacks.Add(generated);
                role.fallbackFontAssetTable = fallbacks;
                EditorUtility.SetDirty(role);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("LOCALE_FONT_BUILD_OK locales=" + localeCount + " characters=" +
                      scalarCount + " asset=" + AssetPath);
        }

        private static string BuildCharacterSet(out int localeCount, out int scalarCount)
        {
            var policy = JsonConvert.DeserializeObject<Policy>(File.ReadAllText(PolicyPath));
            var enabled = (policy?.supportedLocales ?? new List<LocaleSpec>())
                .Where(locale => locale != null && locale.enabledInBuild && !string.IsNullOrWhiteSpace(locale.code))
                .OrderBy(locale => locale.code, StringComparer.Ordinal)
                .ToList();
            if (enabled.Count == 0) throw new InvalidOperationException("Localization policy enables no locales.");
            localeCount = enabled.Count;

            var scalars = new SortedSet<int>();
            for (int value = 32; value <= 126; value++) scalars.Add(value);
            foreach (LocaleSpec locale in enabled)
            {
                string path = LocaleRoot + "/" + locale.code + ".json";
                if (!File.Exists(path)) throw new FileNotFoundException("Enabled locale source missing.", path);
                CollectStrings(JToken.Parse(File.ReadAllText(path)), scalars);
            }

            var result = new StringBuilder(scalars.Count);
            foreach (int scalar in scalars) result.Append(char.ConvertFromUtf32(scalar));
            scalarCount = scalars.Count;
            return result.ToString();
        }

        private static void CollectStrings(JToken token, ISet<int> scalars)
        {
            if (token is JObject obj)
            {
                foreach (JProperty property in obj.Properties())
                    if (!property.Name.StartsWith("_", StringComparison.Ordinal))
                        CollectStrings(property.Value, scalars);
                return;
            }
            if (token is JArray array)
            {
                foreach (JToken child in array) CollectStrings(child, scalars);
                return;
            }
            if (token.Type != JTokenType.String) return;
            string value = token.Value<string>() ?? string.Empty;
            for (int i = 0; i < value.Length; i++)
            {
                int scalar = char.ConvertToUtf32(value, i);
                scalars.Add(scalar);
                if (char.IsHighSurrogate(value[i])) i++;
            }
        }

        private static void EnsureFolder(string path)
        {
            if (string.IsNullOrEmpty(path) || AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }
    }
}

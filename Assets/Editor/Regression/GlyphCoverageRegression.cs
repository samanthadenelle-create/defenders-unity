// Clean-build glyph gate for every locale enabled in the shipping policy.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;

namespace DeNelle.Editor.Regression
{
    public static class GlyphCoverageRegression
    {
        private const string SourcePath = "Assets/Localization/Fonts/Source/LiberationSans.ttf";
        private const string LicensePath = "Assets/Localization/Fonts/Source/LiberationSans-OFL.txt";
        private const string FallbackPath = "Assets/Resources/Localization/Fonts/ElarionLocaleFallback.asset";
        private const string FallbackResource = "Localization/Fonts/ElarionLocaleFallback";

        private static readonly string[] RoleFontPaths =
        {
            "Assets/Resources/RpgUi/font/font_body.asset",
            "Assets/Resources/RpgUi/font/font_title.asset",
            "Assets/Resources/RpgUi/font/font_stamp.asset",
        };

        public static bool Run(out string reason)
        {
            var failures = new List<string>();
            LocalizationAuditIO.Policy policy;
            if (!LocalizationAuditIO.TryLoadPolicy(out policy, failures))
            {
                reason = "GLYPH COVERAGE FAIL -- " + LocalizationAuditIO.JoinFailures(failures);
                return false;
            }

            if (!File.Exists(LocalizationAuditIO.FullPath(SourcePath))) failures.Add("tracked font source missing: " + SourcePath);
            if (!File.Exists(LocalizationAuditIO.FullPath(LicensePath))) failures.Add("font license missing: " + LicensePath);

            TMP_FontAsset fallback = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FallbackPath);
            if (fallback == null) failures.Add("tracked locale fallback asset missing: " + FallbackPath);
            else if (fallback.atlasPopulationMode != AtlasPopulationMode.Static)
                failures.Add("locale fallback must be Static, not " + fallback.atlasPopulationMode);

            TMP_FontAsset runtime = UnityEngine.Resources.Load<TMP_FontAsset>(FallbackResource);
            if (fallback != null && runtime != fallback)
                failures.Add("Resources path does not resolve the tracked fallback: " + FallbackResource);

            foreach (string rolePath in RoleFontPaths)
            {
                TMP_FontAsset role = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(rolePath);
                if (role == null)
                {
                    failures.Add("role font missing: " + rolePath);
                    continue;
                }
                if (role.atlasPopulationMode != AtlasPopulationMode.Static)
                    failures.Add("role font must remain Static: " + rolePath);
                if (fallback != null && (role.fallbackFontAssetTable == null ||
                    !role.fallbackFontAssetTable.Contains(fallback)))
                    failures.Add("role font does not explicitly reference locale fallback: " + rolePath);
            }

            var checkedScalars = new HashSet<uint>();
            int localeCount = 0;
            foreach (var locale in policy.supportedLocales
                         .Where(item => item != null && item.enabledInBuild)
                         .OrderBy(item => item.sortOrder))
            {
                localeCount++;
                var values = new Dictionary<string, string>(StringComparer.Ordinal);
                string path = "Assets/Resources/Data/Canonical/" + locale.code + ".json";
                LocalizationAuditIO.TryReadFlatJson(path, out values, failures);
                if (fallback == null) continue;
                foreach (var row in values)
                {
                    string value = row.Value ?? string.Empty;
                    for (int i = 0; i < value.Length; i++)
                    {
                        int scalar = char.ConvertToUtf32(value, i);
                        if (char.IsHighSurrogate(value[i])) i++;
                        string scalarText = char.ConvertFromUtf32(scalar);
                        if (char.IsWhiteSpace(scalarText, 0) || char.IsControl(scalarText, 0)) continue;
                        uint codePoint = (uint)scalar;
                        checkedScalars.Add(codePoint);
                        if (codePoint > char.MaxValue || !fallback.HasCharacter((char)codePoint, false, false))
                            failures.Add(locale.code + "/" + row.Key + " missing U+" + scalar.ToString("X4"));
                    }
                }
            }

            if (failures.Count > 0)
            {
                reason = "GLYPH COVERAGE FAIL -- " + LocalizationAuditIO.JoinFailures(failures);
                return false;
            }
            reason = "GLYPH COVERAGE OK -- tracked static fallback covers " + checkedScalars.Count +
                     " scalar(s) across " + localeCount + " enabled locale(s); 3 role chains linked";
            return true;
        }
    }
}

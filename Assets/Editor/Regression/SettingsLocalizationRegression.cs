using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace DeNelle.Editor.Regression
{
    /// <summary>Pins the first complete LocalText feature slice, including its player locale door.</summary>
    public static class SettingsLocalizationRegression
    {
        private const string ControllerPath = "Assets/_Modules/Settings/SettingsController.cs";
        private const string CatalogPath = "Assets/_Modules/Settings/SettingsText.cs";
        private const string FacadePath = "Assets/_Modules/Core/UI/LocalText.cs";
        private const string ProviderPath = "Assets/_Modules/Localization/UnityLocalizationProvider.cs";
        private const string EnglishPath = "Assets/StreamingAssets/Data/Canonical/en.json";

        public static bool Run(out string reason)
        {
            var failures = new List<string>();
            try
            {
                string controller = Read(ControllerPath, failures);
                string catalog = Read(CatalogPath, failures);
                string facade = Read(FacadePath, failures);
                string provider = Read(ProviderPath, failures);

                Require(controller, "LocalText.Changed += OnLocalizedTextChanged", "Settings does not react to locale changes", failures);
                Require(controller, "RefreshLabels();", "Settings does not re-resolve existing labels in place", failures);
                if (controller.IndexOf("Destroy(oldCanvas)", StringComparison.Ordinal) >= 0)
                    failures.Add("Settings still destroys the modal canvas on a locale change");
                Require(controller, "LocalText.UseSystemLocale()", "Device-language action is not wired", failures);
                Require(controller, "LocalText.TrySelectLocale", "Explicit locale selection is not wired", failures);
                Require(controller, "LocalText.AvailableLocales", "Settings does not discover shipped locales", failures);
                Require(controller, "LocalText.AvailableLocales.Count > 1", "Single-locale Choose action is not disabled", failures);
                Require(catalog, "settings.section.language", "Settings language caption has no stable key", failures);
                Require(catalog, "settings.language.systemDefault", "Device-language action has no stable key", failures);
                Require(catalog, "settings.language.choose", "Explicit-language action has no stable key", failures);
                Require(facade, "bool UsesSystemLocale", "Core localization boundary cannot expose selector state", failures);
                Require(provider, "ExplicitLocaleSelector.PreferenceKey", "Provider does not preserve explicit-vs-device selection", failures);

                var english = new Dictionary<string, string>();
                var ioFailures = new List<string>();
                if (!LocalizationAuditIO.TryReadFlatJson(EnglishPath, out english, ioFailures))
                    failures.AddRange(ioFailures);
                else
                {
                    var matches = Regex.Matches(catalog, "new LocalizedText(?:<[^>]+>)?\\(\\\"([^\\\"]+)\\\"\\)");
                    foreach (Match match in matches)
                    {
                        string key = match.Groups[1].Value;
                        if (!english.ContainsKey(key)) failures.Add("Settings key missing from English table: " + key);
                    }
                }
            }
            catch (Exception ex)
            {
                failures.Add("settings localization audit threw: " + ex.GetType().Name + ": " + ex.Message);
            }

            if (failures.Count > 0)
            {
                reason = "SETTINGS LOCALIZATION FAIL -- " + LocalizationAuditIO.JoinFailures(failures);
                return false;
            }

            reason = "SETTINGS LOCALIZATION OK -- keyed Settings copy, in-place locale refresh, device default and multi-locale selector pinned";
            return true;
        }

        private static string Read(string path, ICollection<string> failures)
        {
            if (File.Exists(path)) return File.ReadAllText(path);
            failures.Add("missing source: " + path);
            return string.Empty;
        }

        private static void Require(string source, string needle, string failure, ICollection<string> failures)
        {
            if (source.IndexOf(needle, StringComparison.Ordinal) < 0) failures.Add(failure);
        }
    }
}

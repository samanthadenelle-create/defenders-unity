using System;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.Localization.Settings;

namespace DeNelle.Localization
{
    /// <summary>
    /// Gives an explicit in-game language choice precedence over the device locale.
    /// Unlike PlayerPrefLocaleSelector, this selector does not turn the first detected
    /// device locale into a permanent preference during initialization.
    /// </summary>
    [Serializable]
    internal sealed class ExplicitLocaleSelector : IStartupLocaleSelector
    {
        internal const string PreferenceKey = "eoa.localization.explicit-locale";

        public Locale GetStartupLocale(ILocalesProvider availableLocales)
        {
            if (availableLocales == null || !PlayerPrefs.HasKey(PreferenceKey))
                return null;

            string code = PlayerPrefs.GetString(PreferenceKey, string.Empty);
            Locale locale = string.IsNullOrWhiteSpace(code)
                ? null
                : availableLocales.GetLocale(new LocaleIdentifier(code));
            if (locale != null)
                return locale;

            // A locale can be withdrawn from a build while a prior install still
            // carries the explicit preference. Clear it so Settings truthfully
            // reports device-language mode and startup selectors may continue.
            PlayerPrefs.DeleteKey(PreferenceKey);
            PlayerPrefs.Save();
            return null;
        }
    }
}

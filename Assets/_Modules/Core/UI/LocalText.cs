// Localization-ready player-copy seam. English remains the safe fallback, while
// adding Data/Canonical/<language>.json is enough to cover another device language.
using System;
using System.Collections.Generic;
using System.Globalization;
using Newtonsoft.Json;
using UnityEngine;

namespace DeNelle.Core.UI
{
    /// <summary>A stable key for player copy that has no formatting arguments.</summary>
    public readonly struct LocalizedText
    {
        public LocalizedText(string key) { Key = key ?? string.Empty; }
        public string Key { get; }
        public string Resolve() => LocalText.Get(Key);
        public override string ToString() => Resolve();
    }

    /// <summary>A stable key whose Smart String is resolved with one typed argument object.</summary>
    public readonly struct LocalizedText<TArguments>
    {
        public LocalizedText(string key) { Key = key ?? string.Empty; }
        public string Key { get; }
        public string Resolve(TArguments arguments) => LocalText.Format(Key, arguments);
    }

    /// <summary>A locale the player can explicitly select.</summary>
    public readonly struct LocaleOption
    {
        public LocaleOption(string code, string displayName, bool isBeta = false)
        {
            Code = code ?? string.Empty;
            DisplayName = displayName ?? code ?? string.Empty;
            IsBeta = isBeta;
        }

        public string Code { get; }
        public string DisplayName { get; }
        public bool IsBeta { get; }
    }

    /// <summary>
    /// Package-free boundary between Core/UI and the runtime localization adapter.
    /// Implementations must resolve synchronously from already-loaded data; loading is asynchronous.
    /// </summary>
    public interface ILocalTextProvider
    {
        bool IsReady { get; }
        bool UsesSystemLocale { get; }
        string CurrentLocaleCode { get; }
        IReadOnlyList<LocaleOption> AvailableLocales { get; }
        bool TryResolve(string key, object[] args, out string value);
        bool TrySelectLocale(string code);
        void UseSystemLocale();
        event Action Changed;
    }

    public static class LocalText
    {
        private const string Root = "Data/Canonical/";
        private static readonly Dictionary<string, Dictionary<string, string>> Tables =
            new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<string> ReportedMissing =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static readonly LocaleOption[] NoLocales = Array.Empty<LocaleOption>();
        private static ILocalTextProvider _provider;

        public static event Action Changed;

        public static string LanguageCode =>
            !string.IsNullOrEmpty(_provider?.CurrentLocaleCode)
                ? _provider.CurrentLocaleCode
                : CodeFor(Application.systemLanguage);

        public static IReadOnlyList<LocaleOption> AvailableLocales =>
            _provider?.AvailableLocales ?? NoLocales;

        public static bool IsReady => _provider?.IsReady ?? false;

        /// <summary>True when the device locale, rather than a saved explicit choice, drives selection.</summary>
        public static bool UsesSystemLocale => _provider?.UsesSystemLocale ?? true;

        /// <summary>Installs the one runtime authority. Re-installation cleanly detaches the prior adapter.</summary>
        public static void InstallProvider(ILocalTextProvider provider)
        {
            if (ReferenceEquals(_provider, provider)) return;
            if (_provider != null) _provider.Changed -= OnProviderChanged;
            _provider = provider;
            if (_provider != null) _provider.Changed += OnProviderChanged;
            Changed?.Invoke();
        }

        public static bool TrySelectLocale(string code) =>
            _provider != null && _provider.TrySelectLocale(code);

        public static void UseSystemLocale()
        {
            _provider?.UseSystemLocale();
        }

        /// <summary>End-state lookup: English belongs in the table, never at the call site.</summary>
        public static string Get(string key)
        {
            if (TryGet(key, null, out string value)) return value;
            ReportMissing(key);
            return "[[missing:" + (key ?? string.Empty) + "]]";
        }

        /// <summary>Compatibility overload used while call-site fallbacks are moved into the table.</summary>
        public static string Get(string key, string englishFallback)
        {
            if (TryGet(key, null, out string value)) return value;
            if (!string.IsNullOrEmpty(englishFallback)) return englishFallback;
            ReportMissing(key);
            return "[[missing:" + (key ?? string.Empty) + "]]";
        }

        /// <summary>Formats a table value through the provider, including Smart Strings.</summary>
        public static string Format(string key, params object[] args)
        {
            if (TryGet(key, args, out string value)) return value;
            ReportMissing(key);
            return "[[missing:" + (key ?? string.Empty) + "]]";
        }

        /// <summary>Compatibility formatter while a call-site fallback is being moved into the table.</summary>
        public static string FormatWithFallback(string key, string englishFallback, params object[] args)
        {
            if (TryGet(key, args, out string value)) return value;
            if (!string.IsNullOrEmpty(englishFallback))
                return FormatFallback(englishFallback, args, "en");
            ReportMissing(key);
            return "[[missing:" + (key ?? string.Empty) + "]]";
        }

        /// <summary>Lookup without a terminal call-site fallback, used by lazy legacy forwarding shims.</summary>
        public static bool TryGet(string key, object[] args, out string value)
        {
            value = null;
            if (string.IsNullOrEmpty(key)) return false;
            if (_provider != null && _provider.TryResolve(key, args, out value) &&
                !string.IsNullOrEmpty(value)) return true;

            string code = LanguageCode;
            if (!string.Equals(code, "en", StringComparison.OrdinalIgnoreCase) &&
                TryGetFromJson(code, key, out string translated))
            {
                value = FormatFallback(translated, args, code);
                return true;
            }

            if (TryGetFromJson("en", key, out string english))
            {
                value = FormatFallback(english, args, "en");
                return true;
            }

            return false;
        }

        private static bool TryGetFromJson(string code, string key, out string value)
        {
            value = null;
            if (string.IsNullOrEmpty(key)) return false;
            if (!Tables.TryGetValue(code, out var table))
            {
                table = Load(code);
                Tables[code] = table;
            }
            return table.TryGetValue(key, out value) && !string.IsNullOrEmpty(value);
        }

        private static Dictionary<string, string> Load(string code)
        {
            try
            {
                string json = CanonicalJson.Read(Root + code + ".json");
                if (!string.IsNullOrEmpty(json))
                {
                    var raw = JsonConvert.DeserializeObject<Dictionary<string, object>>(json);
                    var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    if (raw != null)
                        foreach (var pair in raw)
                            if (pair.Value is string text) result[pair.Key] = text;
                    return result;
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[LocalText] Could not load language '" + code + "': " + ex.Message);
            }
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        private static string FormatFallback(string pattern, object[] args, string localeCode)
        {
            if (args == null || args.Length == 0 || string.IsNullOrEmpty(pattern)) return pattern;
            try
            {
                CultureInfo culture;
                try { culture = CultureInfo.GetCultureInfo(localeCode ?? "en"); }
                catch (CultureNotFoundException) { culture = CultureInfo.InvariantCulture; }
                if (args.Length == 1 && args[0] != null && ContainsNamedArgument(pattern))
                    return FormatNamedArguments(pattern, args[0], culture);
                return string.Format(culture, pattern, args);
            }
            catch (FormatException ex)
            {
                Debug.LogError("[LocalText] Invalid format for locale '" + localeCode + "': " + ex.Message);
                return pattern;
            }
        }

        private static bool ContainsNamedArgument(string pattern)
        {
            for (int i = 0; i < pattern.Length - 1; i++)
            {
                if (pattern[i] != '{') continue;
                if (pattern[i + 1] == '{')
                {
                    i++;
                    continue;
                }
                char first = pattern[i + 1];
                if (char.IsLetter(first) || first == '_') return true;
            }
            return false;
        }

        private static string FormatNamedArguments(string pattern, object arguments, CultureInfo culture)
        {
            string resolved = pattern;
            var type = arguments.GetType();
            foreach (var property in type.GetProperties())
            {
                if (!property.CanRead || property.GetIndexParameters().Length != 0) continue;
                object raw = property.GetValue(arguments, null);
                string rendered = raw is IFormattable formattable
                    ? formattable.ToString(null, culture)
                    : raw?.ToString() ?? string.Empty;
                resolved = resolved.Replace("{" + property.Name + "}", rendered);
            }
            foreach (var field in type.GetFields())
            {
                object raw = field.GetValue(arguments);
                string rendered = raw is IFormattable formattable
                    ? formattable.ToString(null, culture)
                    : raw?.ToString() ?? string.Empty;
                resolved = resolved.Replace("{" + field.Name + "}", rendered);
            }
            return resolved;
        }

        private static void ReportMissing(string key)
        {
            string safeKey = key ?? string.Empty;
            if (ReportedMissing.Add(safeKey))
                Debug.LogError("[LocalText] Missing player-facing key '" + safeKey + "'.");
        }

        private static void OnProviderChanged()
        {
            Changed?.Invoke();
        }

        private static string CodeFor(SystemLanguage language)
        {
            switch (language)
            {
                case SystemLanguage.Spanish: return "es";
                case SystemLanguage.French: return "fr";
                case SystemLanguage.German: return "de";
                case SystemLanguage.Italian: return "it";
                // The first Portuguese table is Brazilian Portuguese. Unity's
                // SystemLanguage enum does not distinguish regions, so use the
                // only supported Portuguese authority instead of falling to English.
                case SystemLanguage.Portuguese: return "pt-BR";
                case SystemLanguage.Japanese: return "ja";
                case SystemLanguage.Korean: return "ko";
                case SystemLanguage.Russian: return "ru";
                case SystemLanguage.ChineseSimplified: return "zh-Hans";
                case SystemLanguage.ChineseTraditional: return "zh-Hant";
                case SystemLanguage.Arabic: return "ar";
                default: return "en";
            }
        }
    }

    /// <summary>Shared interaction copy used by multiple feature surfaces.</summary>
    public static class CommonText
    {
        public const string KeyClose = "common.close";
        public static readonly LocalizedText Close = new LocalizedText(KeyClose);
    }

    public static class HonestFeedbackText
    {
        public const string KeyTitle = "feedback.title";
        public const string KeyBody = "feedback.body";
        public const string KeyPlaceholder = "feedback.placeholder";
        public const string KeySend = "feedback.send";
        public const string KeyReward = "feedback.reward";
        public const string KeyStore = "feedback.store";
        public const string KeyStoreCaption = "feedback.storeCaption";
        public const string KeySettingsSection = "feedback.settingsSection";
        public const string KeySettings = "feedback.settingsButton";
        public const string KeySending = "feedback.sending";
        public const string KeyServiceUnavailable = "feedback.serviceUnavailable";
        public const string KeyStoreUnavailable = "feedback.storeUnavailable";
        public const string KeyThanks = "feedback.thanks";
        public const string KeyAlreadyClaimed = "feedback.alreadyClaimed";
        public const string KeyGrantUnavailable = "feedback.grantUnavailable";
        public const string KeyTooShort = "feedback.tooShort";
        public const string KeyNoIdentity = "feedback.noIdentity";
        public const string KeyServerRefused = "feedback.serverRefused";
        public const string KeyNetworkFailed = "feedback.networkFailed";
        public const string KeyOverCapacity = "feedback.overCapacity";

        public static readonly LocalizedText Title = new LocalizedText(KeyTitle);
        public static readonly LocalizedText Body = new LocalizedText(KeyBody);
        public static readonly LocalizedText Placeholder = new LocalizedText(KeyPlaceholder);
        public static readonly LocalizedText Send = new LocalizedText(KeySend);
        public static readonly LocalizedText Reward = new LocalizedText(KeyReward);
        public static readonly LocalizedText Store = new LocalizedText(KeyStore);
        public static readonly LocalizedText StoreCaption = new LocalizedText(KeyStoreCaption);
        public static readonly LocalizedText SettingsSection = new LocalizedText(KeySettingsSection);
        public static readonly LocalizedText SettingsButton = new LocalizedText(KeySettings);
        public static readonly LocalizedText Sending = new LocalizedText(KeySending);
        public static readonly LocalizedText ServiceUnavailable = new LocalizedText(KeyServiceUnavailable);
        public static readonly LocalizedText StoreUnavailable = new LocalizedText(KeyStoreUnavailable);
        public static readonly LocalizedText Thanks = new LocalizedText(KeyThanks);
        public static readonly LocalizedText AlreadyClaimed = new LocalizedText(KeyAlreadyClaimed);
        public static readonly LocalizedText GrantUnavailable = new LocalizedText(KeyGrantUnavailable);
        public static readonly LocalizedText<MinimumCharactersArguments> TooShort =
            new LocalizedText<MinimumCharactersArguments>(KeyTooShort);
        public static readonly LocalizedText NoIdentity = new LocalizedText(KeyNoIdentity);
        public static readonly LocalizedText ServerRefused = new LocalizedText(KeyServerRefused);
        public static readonly LocalizedText NetworkFailed = new LocalizedText(KeyNetworkFailed);
        public static readonly LocalizedText<CapacityOverflowArguments> OverCapacity =
            new LocalizedText<CapacityOverflowArguments>(KeyOverCapacity);
    }

    public readonly struct MinimumCharactersArguments
    {
        public MinimumCharactersArguments(int minimum) { Minimum = minimum; }
        public int Minimum { get; }
    }

    public readonly struct CapacityOverflowArguments
    {
        public CapacityOverflowArguments(string resource, int amountOver)
        {
            Resource = resource ?? string.Empty;
            AmountOver = amountOver;
        }

        public string Resource { get; }
        public int AmountOver { get; }
    }
}

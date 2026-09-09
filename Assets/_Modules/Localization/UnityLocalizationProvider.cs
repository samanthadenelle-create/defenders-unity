using System;
using System.Collections.Generic;
using DeNelle.Core.Diagnostics;
using DeNelle.Core.UI;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.Localization.Settings;
using UnityEngine.Localization.Tables;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace DeNelle.Localization
{
    /// <summary>
    /// Package-backed implementation of the Core localization boundary. All package
    /// access that can load an Addressable is asynchronous; synchronous player-copy
    /// reads use only StringTables that have already completed loading.
    /// </summary>
    internal sealed class UnityLocalizationProvider : ILocalTextProvider, IDisposable
    {
        private const string TraceSystem = "Localization";
        private const string TableCollectionName = "GameStrings";
        private const string EnglishCode = "en";

        private readonly List<LocaleOption> _availableLocales = new List<LocaleOption>();
        private readonly HashSet<string> _reportedFormatFailures = new HashSet<string>(StringComparer.Ordinal);

        private AsyncOperationHandle<LocalizationSettings> _initializationOperation;
        private StringTable _selectedTable;
        private StringTable _englishTable;
        private string _selectedLocaleCode = EnglishCode;
        private int _loadGeneration;
        private bool _initialized;
        private bool _disposed;
        private bool _useSystemWhenReady;
        private bool _reportedInitializationFailure;

        public bool IsReady => _selectedTable != null || _englishTable != null;

        public bool UsesSystemLocale => !PlayerPrefs.HasKey(ExplicitLocaleSelector.PreferenceKey);

        public string CurrentLocaleCode => string.IsNullOrEmpty(_selectedLocaleCode)
            ? EnglishCode
            : _selectedLocaleCode;

        public IReadOnlyList<LocaleOption> AvailableLocales => _availableLocales;

        public event Action Changed;

        internal void Initialize()
        {
            if (_disposed || _initialized)
                return;

            _initialized = true;
            LocalizationSettings.SelectedLocaleChanged += OnSelectedLocaleChanged;

            _initializationOperation = LocalizationSettings.InitializationOperation;
            if (_initializationOperation.IsDone)
                OnInitializationCompleted(_initializationOperation);
            else
                _initializationOperation.Completed += OnInitializationCompleted;
        }

        public bool TryResolve(string key, object[] args, out string value)
        {
            value = null;
            if (_disposed || string.IsNullOrEmpty(key))
                return false;

            if (TryResolveFrom(_selectedTable, key, args, out value))
                return true;

            if (!ReferenceEquals(_selectedTable, _englishTable) &&
                TryResolveFrom(_englishTable, key, args, out value))
                return true;

            return false;
        }

        public bool TrySelectLocale(string code)
        {
            if (_disposed || !_initialized || string.IsNullOrWhiteSpace(code) ||
                !_initializationOperation.IsValid() || !_initializationOperation.IsDone ||
                _initializationOperation.Status != AsyncOperationStatus.Succeeded)
                return false;

            Locale locale = LocalizationSettings.AvailableLocales.GetLocale(new LocaleIdentifier(code.Trim()));
            if (locale == null)
            {
                FlowTrace.Warn(TraceSystem, "Explicit locale '" + code + "' is not available in this build.");
                return false;
            }

            _useSystemWhenReady = false;
            PlayerPrefs.SetString(ExplicitLocaleSelector.PreferenceKey, locale.Identifier.Code);
            PlayerPrefs.Save();
            SelectOrReloadLocale(locale);
            return true;
        }

        public void UseSystemLocale()
        {
            if (_disposed)
                return;

            PlayerPrefs.DeleteKey(ExplicitLocaleSelector.PreferenceKey);
            PlayerPrefs.Save();

            if (!_initializationOperation.IsValid() || !_initializationOperation.IsDone ||
                _initializationOperation.Status != AsyncOperationStatus.Succeeded)
            {
                _useSystemWhenReady = true;
                return;
            }

            SelectSystemLocale();
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            _loadGeneration++;
            LocalizationSettings.SelectedLocaleChanged -= OnSelectedLocaleChanged;

            if (_initializationOperation.IsValid() && !_initializationOperation.IsDone)
                _initializationOperation.Completed -= OnInitializationCompleted;

            _selectedTable = null;
            _englishTable = null;
            _availableLocales.Clear();
            Changed = null;
        }

        private void OnInitializationCompleted(AsyncOperationHandle<LocalizationSettings> operation)
        {
            if (_disposed)
                return;

            if (operation.Status != AsyncOperationStatus.Succeeded)
            {
                if (!_reportedInitializationFailure)
                {
                    _reportedInitializationFailure = true;
                    FlowTrace.Fail(TraceSystem, "Unity Localization initialization failed: " +
                        (operation.OperationException == null
                            ? "no operation exception was supplied"
                            : operation.OperationException.Message));
                }
                return;
            }

            RebuildLocaleOptions();

            if (_useSystemWhenReady)
            {
                SelectSystemLocale();
                return;
            }

            AsyncOperationHandle<Locale> selectedOperation = LocalizationSettings.SelectedLocaleAsync;
            Locale selectedLocale = selectedOperation.IsDone ? selectedOperation.Result : null;
            if (selectedLocale == null)
            {
                FlowTrace.Fail(TraceSystem, "Initialization completed without a selected locale; English fallback remains active.");
                BeginTableLoads(null);
                return;
            }

            BeginTableLoads(selectedLocale);
        }

        private void OnSelectedLocaleChanged(Locale locale)
        {
            if (_disposed)
                return;

            RebuildLocaleOptions();
            BeginTableLoads(locale);
        }

        private void BeginTableLoads(Locale selectedLocale)
        {
            int generation = ++_loadGeneration;
            _selectedTable = null;
            _englishTable = null;

            Locale englishLocale = LocalizationSettings.AvailableLocales.GetLocale(new LocaleIdentifier(EnglishCode));
            _selectedLocaleCode = selectedLocale == null
                ? EnglishCode
                : selectedLocale.Identifier.Code;

            if (selectedLocale != null && englishLocale != null &&
                selectedLocale.Identifier == englishLocale.Identifier)
            {
                LoadTable(selectedLocale, generation, table =>
                {
                    _selectedTable = table;
                    _englishTable = table;
                });
                return;
            }

            if (selectedLocale != null)
                LoadTable(selectedLocale, generation, table => _selectedTable = table);

            if (englishLocale != null)
                LoadTable(englishLocale, generation, table => _englishTable = table);
            else
                FlowTrace.Fail(TraceSystem, "The required English locale is not available; JSON fallback remains active.");
        }

        private void LoadTable(Locale locale, int generation, Action<StringTable> accept)
        {
            AsyncOperationHandle<StringTable> operation =
                LocalizationSettings.StringDatabase.GetTableAsync(TableCollectionName, locale);

            if (operation.IsDone)
                CompleteTableLoad(operation, locale, generation, accept);
            else
                operation.Completed += completed => CompleteTableLoad(completed, locale, generation, accept);
        }

        private void CompleteTableLoad(
            AsyncOperationHandle<StringTable> operation,
            Locale locale,
            int generation,
            Action<StringTable> accept)
        {
            if (_disposed || generation != _loadGeneration)
                return;

            if (operation.Status != AsyncOperationStatus.Succeeded || operation.Result == null)
            {
                FlowTrace.Fail(TraceSystem, "Failed to load '" + TableCollectionName + "' for locale '" +
                    locale.Identifier.Code + "': " +
                    (operation.OperationException == null
                        ? "the table result was empty"
                        : operation.OperationException.Message));
                return;
            }

            accept(operation.Result);
            FlowTrace.Step(TraceSystem, "Loaded '" + TableCollectionName + "' for locale '" +
                locale.Identifier.Code + "'.");
            Changed?.Invoke();
        }

        private bool TryResolveFrom(StringTable table, string key, object[] args, out string value)
        {
            value = null;
            if (table == null)
                return false;

            StringTableEntry entry = table.GetEntry(key);
            if (entry == null || string.IsNullOrEmpty(entry.Value))
                return false;

            try
            {
                value = args == null || args.Length == 0
                    ? entry.GetLocalizedString()
                    : entry.GetLocalizedString(args);
                return !string.IsNullOrEmpty(value);
            }
            catch (Exception exception)
            {
                string diagnosticKey = table.LocaleIdentifier.Code + ":" + key;
                if (_reportedFormatFailures.Add(diagnosticKey))
                {
                    FlowTrace.Warn(TraceSystem, "Could not format key '" + key + "' for locale '" +
                        table.LocaleIdentifier.Code + "': " + exception.Message);
                }
                value = null;
                return false;
            }
        }

        private void RebuildLocaleOptions()
        {
            _availableLocales.Clear();
            var locales = new List<Locale>(LocalizationSettings.AvailableLocales.Locales);
            locales.Sort();
            for (int i = 0; i < locales.Count; i++)
            {
                Locale locale = locales[i];
                if (locale == null)
                    continue;

                string code = locale.Identifier.Code;
                string displayName = string.IsNullOrWhiteSpace(locale.LocaleName)
                    ? code
                    : locale.LocaleName;
                _availableLocales.Add(new LocaleOption(
                    code,
                    displayName,
                    !string.Equals(code, EnglishCode, StringComparison.OrdinalIgnoreCase)));
            }
        }

        private void SelectSystemLocale()
        {
            _useSystemWhenReady = false;
            Locale locale = new SystemLocaleSelector().GetStartupLocale(LocalizationSettings.AvailableLocales)
                ?? LocalizationSettings.AvailableLocales.GetLocale(new LocaleIdentifier(EnglishCode));

            if (locale == null)
            {
                FlowTrace.Fail(TraceSystem, "Neither the device locale nor English is available; JSON fallback remains active.");
                BeginTableLoads(null);
                return;
            }

            SelectOrReloadLocale(locale);
        }

        private void SelectOrReloadLocale(Locale locale)
        {
            AsyncOperationHandle<Locale> selectedOperation = LocalizationSettings.SelectedLocaleAsync;
            if (selectedOperation.IsDone && selectedOperation.Result != null &&
                selectedOperation.Result.Identifier == locale.Identifier)
            {
                // SetSelectedLocale deliberately emits no event for the active locale.
                // Reload explicitly so "Use device language" during startup, or a retry
                // after a failed table load, cannot leave the provider permanently empty.
                BeginTableLoads(locale);
                return;
            }

            LocalizationSettings.SelectedLocale = locale;
        }
    }
}

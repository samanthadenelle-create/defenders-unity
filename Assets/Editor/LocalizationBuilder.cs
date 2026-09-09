// =============================================================================
// LocalizationBuilder — Week-1 Unity Localization wiring (Editor-only)
// -----------------------------------------------------------------------------
// One static entry point — LocalizationBuilder.BuildAll() — that the main Unity
// session runs (manually or via the Unity -executeMethod flag):
//
//     -executeMethod DeNelle.Editor.LocalizationBuilder.BuildAll
//
// What it does (v2 port-spec Part 2 + Part 4, Week 1):
//   1. Creates the project LocalizationSettings asset under Assets/Localization/
//      and registers it as LocalizationEditorSettings.ActiveLocalizationSettings.
//   2. Creates an English Locale ("en" / English) and adds it to the project
//      locales.
//   3. Creates a StringTableCollection ("GameStrings") under Assets/Localization/
//      and populates its English StringTable from
//      Assets/StreamingAssets/Data/Canonical/en.json — nested JSON objects are
//      flattened into dotted keys (e.g. intro.coldOpen.line1). Keys (and any
//      object segment) whose name begins with '_' are treated as metadata and
//      skipped (e.g. "_comment", "_sources").
//   4. Sets the project-default / selected locale to English.
//
// IDEMPOTENT — re-running BuildAll() reuses the existing settings, locale and
// table collection; current string entries are UPDATED in place, new ones are
// added, and stale ones are removed. Safe to run repeatedly.
//
// ASSEMBLY NOTE. The DeNelle.Editor.asmdef is fixed (editing asmdefs is
// forbidden). It already references Unity.Localization + Unity.Localization.Editor,
// so the localization editor APIs are used directly. Newtonsoft.Json
// (com.unity.nuget.newtonsoft-json) is auto-referenced, so it is used directly
// for parsing en.json.
//
// This script does NOT run itself; the main session triggers it.
// =============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.Localization;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.Localization.Metadata;
using UnityEngine.Localization.Settings;
using UnityEngine.Localization.Tables;

namespace DeNelle.Editor
{
    /// <summary>
    /// Editor utility that wires up the Unity Localization package: creates the
    /// project <see cref="LocalizationSettings"/>, an English <see cref="Locale"/>,
    /// and a <c>GameStrings</c> <see cref="StringTableCollection"/> populated from
    /// the canonical <c>en.json</c>. Entry point: <see cref="BuildAll"/>.
    /// </summary>
    public static class LocalizationBuilder
    {
        // ── Project paths ────────────────────────────────────────────────────
        private const string LocalizationDir = "Assets/Localization";
        private const string SettingsPath = LocalizationDir + "/LocalizationSettings.asset";
        private const string PolicyPath = "Assets/Editor/Localization/LocalizationPolicy.json";

        private const string TableCollectionName = "GameStrings";
        private const string TablesDir = LocalizationDir + "/Tables";

        private const string CanonicalLocaleDir = "Assets/StreamingAssets/Data/Canonical";
        // English locale identifier — language code "en".
        private static readonly LocaleIdentifier EnglishId = new LocaleIdentifier("en");

        [Serializable]
        private sealed class BuildPolicy
        {
            public List<LocaleBuildSpec> supportedLocales;
        }

        [Serializable]
        private sealed class LocaleBuildSpec
        {
            public string code;
            public bool required;
            public bool enabledInBuild;
            public string status;
            public int sortOrder;
        }

        // =====================================================================
        //  Entry point
        // =====================================================================

        /// <summary>
        /// Wires the Unity Localization package end-to-end for Week 1.
        /// Runnable via <c>-executeMethod DeNelle.Editor.LocalizationBuilder.BuildAll</c>.
        /// Idempotent — re-running updates existing assets rather than duplicating.
        /// </summary>
        [MenuItem("Defenders/Week 1/Build Localization")]
        public static void BuildAll()
        {
            List<LocaleBuildSpec> allSpecs;
            List<LocaleBuildSpec> enabledSpecs;
            Dictionary<string, Dictionary<string, string>> sources;
            if (!TryLoadPolicy(out allSpecs, out enabledSpecs) ||
                !TryLoadLocaleSources(enabledSpecs, out sources))
                return;

            EnsureFolder(LocalizationDir);
            EnsureFolder(TablesDir);

            // 1. Project LocalizationSettings asset (created + made active).
            var settings = EnsureLocalizationSettings();

            // 2. Policy-enabled Locales, registered with deterministic native names.
            var locales = EnsureLocales(enabledSpecs);
            Locale englishLocale = locales["en"];

            // 3. GameStrings tables, one per enabled locale and populated from JSON.
            var collection = EnsureStringTableCollection(enabledSpecs, locales);
            RemoveDisabledPolicyLocales(allSpecs, collection);
            int imported = PopulateFromJson(collection, enabledSpecs, sources);

            // 4. Project-default / selected locale = English.
            ConfigureLocales(settings, enabledSpecs, locales, englishLocale);

            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"[LocalizationBuilder] BuildAll complete — LocalizationSettings active, " +
                      $"{enabledSpecs.Count} policy-enabled locale(s) registered, '{TableCollectionName}' holds " +
                      $"{imported} localized entries imported from canonical JSON.");
        }

        private static bool TryLoadPolicy(
            out List<LocaleBuildSpec> allSpecs,
            out List<LocaleBuildSpec> enabledSpecs)
        {
            allSpecs = new List<LocaleBuildSpec>();
            enabledSpecs = new List<LocaleBuildSpec>();
            string fullPath = Path.GetFullPath(PolicyPath);
            if (!File.Exists(fullPath))
            {
                Debug.LogError($"[LocalizationBuilder] Localization policy not found at {PolicyPath}.");
                return false;
            }

            BuildPolicy policy;
            try
            {
                policy = JsonConvert.DeserializeObject<BuildPolicy>(File.ReadAllText(fullPath));
            }
            catch (JsonException exception)
            {
                Debug.LogError($"[LocalizationBuilder] Failed to parse {PolicyPath}: {exception.Message}");
                return false;
            }

            if (policy?.supportedLocales == null || policy.supportedLocales.Count == 0)
            {
                Debug.LogError("[LocalizationBuilder] Localization policy has no supportedLocales.");
                return false;
            }

            var codes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var sortOrders = new HashSet<int>();
            bool englishEnabled = false;
            foreach (LocaleBuildSpec spec in policy.supportedLocales)
            {
                if (spec == null || string.IsNullOrWhiteSpace(spec.code) ||
                    string.IsNullOrWhiteSpace(spec.status))
                {
                    Debug.LogError("[LocalizationBuilder] Every supported locale needs code, status, enabledInBuild and sortOrder metadata.");
                    return false;
                }

                spec.code = spec.code.Trim();
                if (!codes.Add(spec.code))
                {
                    Debug.LogError($"[LocalizationBuilder] Duplicate locale code in policy: {spec.code}.");
                    return false;
                }
                if (spec.sortOrder < 0 || spec.sortOrder > ushort.MaxValue)
                {
                    Debug.LogError($"[LocalizationBuilder] Locale sortOrder must be between 0 and {ushort.MaxValue}: {spec.code}={spec.sortOrder}.");
                    return false;
                }
                if (!sortOrders.Add(spec.sortOrder))
                {
                    Debug.LogError($"[LocalizationBuilder] Duplicate locale sortOrder in policy: {spec.sortOrder}.");
                    return false;
                }

                allSpecs.Add(spec);
                if (!spec.enabledInBuild) continue;
                enabledSpecs.Add(spec);
                if (string.Equals(spec.code, EnglishId.Code, StringComparison.OrdinalIgnoreCase))
                    englishEnabled = true;
            }

            if (!englishEnabled)
            {
                Debug.LogError("[LocalizationBuilder] English ('en') must remain enabled as the project fallback locale.");
                return false;
            }

            enabledSpecs.Sort((left, right) =>
            {
                int byOrder = left.sortOrder.CompareTo(right.sortOrder);
                return byOrder != 0 ? byOrder : string.CompareOrdinal(left.code, right.code);
            });
            return true;
        }

        private static bool TryLoadLocaleSources(
            IList<LocaleBuildSpec> enabledSpecs,
            out Dictionary<string, Dictionary<string, string>> sources)
        {
            sources = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
            foreach (LocaleBuildSpec spec in enabledSpecs)
            {
                string assetPath = CanonicalLocaleDir + "/" + spec.code + ".json";
                string fullPath = Path.GetFullPath(assetPath);
                if (!File.Exists(fullPath))
                {
                    Debug.LogError($"[LocalizationBuilder] Enabled locale source not found: {assetPath}.");
                    return false;
                }

                JObject root;
                try
                {
                    root = JObject.Parse(File.ReadAllText(fullPath));
                }
                catch (JsonException exception)
                {
                    Debug.LogError($"[LocalizationBuilder] Failed to parse {assetPath}: {exception.Message}");
                    return false;
                }

                var flat = new Dictionary<string, string>(StringComparer.Ordinal);
                Flatten(root, null, flat);
                sources.Add(spec.code, flat);
            }

            Dictionary<string, string> english = sources[EnglishId.Code];
            foreach (LocaleBuildSpec spec in enabledSpecs)
            {
                Dictionary<string, string> localized = sources[spec.code];
                foreach (string key in english.Keys)
                {
                    if (!localized.ContainsKey(key))
                    {
                        Debug.LogError($"[LocalizationBuilder] {spec.code}.json is missing English key '{key}'.");
                        return false;
                    }
                }
                foreach (string key in localized.Keys)
                {
                    if (!english.ContainsKey(key))
                    {
                        Debug.LogError($"[LocalizationBuilder] {spec.code}.json has non-English key '{key}'.");
                        return false;
                    }
                }
            }
            return true;
        }

        // =====================================================================
        //  1. LocalizationSettings
        // =====================================================================

        /// <summary>
        /// Loads the existing project <see cref="LocalizationSettings"/> asset or
        /// creates it, then registers it as the active settings.
        /// </summary>
        private static LocalizationSettings EnsureLocalizationSettings()
        {
            var settings = AssetDatabase.LoadAssetAtPath<LocalizationSettings>(SettingsPath);
            if (settings == null)
            {
                settings = ScriptableObject.CreateInstance<LocalizationSettings>();
                settings.name = "LocalizationSettings";
                AssetDatabase.CreateAsset(settings, SettingsPath);
                Debug.Log($"[LocalizationBuilder] Created LocalizationSettings at {SettingsPath}.");
            }

            // Register as the project's active settings (writes the editor-build
            // pointer so the package picks these settings up at runtime).
            if (LocalizationEditorSettings.ActiveLocalizationSettings != settings)
                LocalizationEditorSettings.ActiveLocalizationSettings = settings;

            return settings;
        }

        // =====================================================================
        //  2. English Locale
        // =====================================================================

        /// <summary>
        /// Returns the English <see cref="Locale"/> registered with the project,
        /// creating and registering it if absent.
        /// </summary>
        private static Dictionary<string, Locale> EnsureLocales(IList<LocaleBuildSpec> enabledSpecs)
        {
            var locales = new Dictionary<string, Locale>(StringComparer.OrdinalIgnoreCase);
            foreach (LocaleBuildSpec spec in enabledSpecs)
            {
                var identifier = new LocaleIdentifier(spec.code);
                string localePath = LocalizationDir + "/" + spec.code + ".asset";
                Locale locale = LocalizationEditorSettings.GetLocale(identifier);
                bool isRegistered = locale != null && locale.Identifier == identifier;
                if (!isRegistered)
                    locale = AssetDatabase.LoadAssetAtPath<Locale>(localePath);

                if (locale == null)
                {
                    locale = Locale.CreateLocale(identifier);
                    locale.name = spec.code;
                    AssetDatabase.CreateAsset(locale, localePath);
                    Debug.Log($"[LocalizationBuilder] Created locale at {localePath}.");
                }

                if (!isRegistered)
                    LocalizationEditorSettings.AddLocale(locale);

                locale.name = spec.code;
                locale.SortOrder = (ushort)spec.sortOrder;
                locale.LocaleName = identifier.CultureInfo == null
                    ? spec.code
                    : identifier.CultureInfo.NativeName;
                EditorUtility.SetDirty(locale);
                locales.Add(spec.code, locale);
            }
            return locales;
        }

        // =====================================================================
        //  3. StringTableCollection
        // =====================================================================

        /// <summary>
        /// Returns the <c>GameStrings</c> <see cref="StringTableCollection"/>,
        /// creating it (with an English <see cref="StringTable"/>) if absent.
        /// </summary>
        private static StringTableCollection EnsureStringTableCollection(
            IList<LocaleBuildSpec> enabledSpecs,
            IReadOnlyDictionary<string, Locale> locales)
        {
            var collection = LocalizationEditorSettings.GetStringTableCollection(TableCollectionName);
            if (collection == null)
            {
                var selectedLocales = new List<Locale>();
                foreach (LocaleBuildSpec spec in enabledSpecs)
                    selectedLocales.Add(locales[spec.code]);
                collection = LocalizationEditorSettings.CreateStringTableCollection(
                    TableCollectionName, TablesDir, selectedLocales);
                Debug.Log($"[LocalizationBuilder] Created StringTableCollection '{TableCollectionName}' in {TablesDir}.");
            }

            // Guarantee an English StringTable exists in the collection — re-runs
            // and pre-existing collections without one are handled here.
            foreach (LocaleBuildSpec spec in enabledSpecs)
            {
                var identifier = new LocaleIdentifier(spec.code);
                if (collection.GetTable(identifier) == null)
                    collection.AddNewTable(identifier);
            }

            return collection;
        }

        private static void RemoveDisabledPolicyLocales(
            IList<LocaleBuildSpec> allSpecs,
            StringTableCollection collection)
        {
            foreach (LocaleBuildSpec spec in allSpecs)
            {
                if (spec.enabledInBuild) continue;
                var identifier = new LocaleIdentifier(spec.code);
                LocalizationTable table = collection.GetTable(identifier);
                if (table != null)
                    collection.RemoveTable(table);

                string localePath = LocalizationDir + "/" + spec.code + ".asset";
                Locale locale = AssetDatabase.LoadAssetAtPath<Locale>(localePath);
                if (locale != null)
                    LocalizationEditorSettings.RemoveLocale(locale);
            }
        }

        /// <summary>
        /// Reconciles shared keys from English, then writes every enabled locale
        /// from the canonical sources that were validated before asset mutation.
        /// Existing keys are updated in place; new keys are added. Returns the
        /// number of localized entries written across every enabled table.
        /// </summary>
        private static int PopulateFromJson(
            StringTableCollection collection,
            IList<LocaleBuildSpec> enabledSpecs,
            IReadOnlyDictionary<string, Dictionary<string, string>> sources)
        {
            Dictionary<string, string> flat = sources[EnglishId.Code];

            var englishTable = (StringTable)collection.GetTable(EnglishId);
            if (englishTable == null)
            {
                Debug.LogError("[LocalizationBuilder] English StringTable missing from collection — no strings imported.");
                return 0;
            }

            var sharedData = collection.SharedData;
            var desiredKeys = new HashSet<string>(flat.Keys, System.StringComparer.Ordinal);
            var existingEntries = new List<SharedTableData.SharedTableEntry>(sharedData.Entries);
            int removed = 0;
            foreach (var entry in existingEntries)
            {
                if (entry == null || desiredKeys.Contains(entry.Key)) continue;
                foreach (var table in collection.StringTables)
                    table?.RemoveEntry(entry.Id);
                sharedData.RemoveKey(entry.Id);
                removed++;
            }

            int count = 0;
            foreach (LocaleBuildSpec spec in enabledSpecs)
            {
                var identifier = new LocaleIdentifier(spec.code);
                var table = (StringTable)collection.GetTable(identifier);
                if (table == null)
                {
                    Debug.LogError($"[LocalizationBuilder] Enabled StringTable missing for {spec.code}.");
                    continue;
                }
                foreach (var kv in sources[spec.code])
                {
                    var localizedEntry = table.AddEntry(kv.Key, kv.Value);
                    localizedEntry.IsSmart = UsesArguments(kv.Value);
                    count++;
                }
                EditorUtility.SetDirty(table);
            }

            EditorUtility.SetDirty(sharedData);
            Debug.Log($"[LocalizationBuilder] Reconciled '{TableCollectionName}': " +
                      $"{count} localized entries, {removed} stale shared entries removed.");
            return count;
        }

        /// <summary>
        /// Both typed named arguments and compatibility positional arguments travel
        /// through StringTableEntry.GetLocalizedString(args), so both template shapes
        /// must be Smart Strings in the package-backed path.
        /// </summary>
        private static bool UsesArguments(string value)
        {
            if (string.IsNullOrEmpty(value)) return false;
            for (int i = 0; i < value.Length - 1; i++)
            {
                if (value[i] != '{') continue;
                if (value[i + 1] == '{')
                {
                    i++;
                    continue;
                }

                char first = value[i + 1];
                if (char.IsLetterOrDigit(first) || first == '_') return true;
            }
            return false;
        }

        /// <summary>
        /// Recursively flattens a <see cref="JToken"/> tree into dotted keys.
        /// Object segments and leaf keys beginning with '_' are skipped as
        /// metadata. Leaves are coerced to their string value; the canonical
        /// en.json is already flat-keyed strings, but nested objects are handled
        /// for robustness.
        /// </summary>
        private static void Flatten(JToken token, string prefix, IDictionary<string, string> output)
        {
            switch (token.Type)
            {
                case JTokenType.Object:
                    foreach (var prop in (JObject)token)
                    {
                        if (prop.Key.StartsWith("_"))
                            continue; // metadata segment (_comment, _sources, ...)
                        var key = string.IsNullOrEmpty(prefix) ? prop.Key : prefix + "." + prop.Key;
                        Flatten(prop.Value, key, output);
                    }
                    break;

                case JTokenType.Array:
                    var arr = (JArray)token;
                    for (int i = 0; i < arr.Count; i++)
                        Flatten(arr[i], (prefix ?? string.Empty) + "." + i, output);
                    break;

                case JTokenType.Null:
                    break;

                default: // String / Integer / Float / Boolean / Date — a leaf.
                    if (!string.IsNullOrEmpty(prefix))
                        output[prefix] = token.ToString();
                    break;
            }
        }

        // =====================================================================
        //  4. Default / selected locale
        // =====================================================================

        /// <summary>
        /// Sets English as the project-default / selected locale at startup.
        /// The default <see cref="LocalizationSettings"/> ships with a
        /// <see cref="SpecificLocaleSelector"/> in its startup-selector list;
        /// this points that selector at the English locale identifier so English
        /// is chosen when no command-line / system override applies.
        /// </summary>
        private static void ConfigureLocales(
            LocalizationSettings settings,
            IList<LocaleBuildSpec> enabledSpecs,
            IReadOnlyDictionary<string, Locale> locales,
            Locale englishLocale)
        {
            foreach (LocaleBuildSpec spec in enabledSpecs)
            {
                Locale locale = locales[spec.code];
                locale.SortOrder = (ushort)spec.sortOrder;
                if (locale.Identifier != EnglishId)
                {
                    FallbackLocale fallback = locale.Metadata.GetMetadata<FallbackLocale>();
                    if (fallback == null)
                        locale.Metadata.AddMetadata(new FallbackLocale(englishLocale));
                    else
                        fallback.Locale = englishLocale;
                }
                EditorUtility.SetDirty(locale);
            }

            var selectors = settings.GetStartupLocaleSelectors();
            bool found = false;
            foreach (var selector in selectors)
            {
                if (selector is SpecificLocaleSelector specific)
                {
                    specific.LocaleId = englishLocale.Identifier;
                    found = true;
                    break;
                }
            }

            // If the list was customised and no SpecificLocaleSelector remained,
            // append one pinned to English so a deterministic default exists.
            if (!found)
                selectors.Add(new SpecificLocaleSelector { LocaleId = englishLocale.Identifier });

            // English is the only / primary locale — give it top sort order so it
            // is the first selected locale in editor previews and menus.
            englishLocale.SortOrder = 0;
            var stringDatabase = settings.GetStringDatabase();
            if (stringDatabase != null)
            {
                stringDatabase.DefaultTable = TableCollectionName;
                stringDatabase.UseFallback = true;
            }
            var assetDatabase = settings.GetAssetDatabase();
            if (assetDatabase != null) assetDatabase.UseFallback = true;

            EditorUtility.SetDirty(settings);
            EditorUtility.SetDirty(englishLocale);
        }

        // =====================================================================
        //  Folder helper
        // =====================================================================

        private static void EnsureFolder(string assetPath)
        {
            if (AssetDatabase.IsValidFolder(assetPath)) return;
            var parent = Path.GetDirectoryName(assetPath)?.Replace('\\', '/');
            var leaf = Path.GetFileName(assetPath);
            if (string.IsNullOrEmpty(parent) || string.IsNullOrEmpty(leaf)) return;
            if (!AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, leaf);
        }
    }
}

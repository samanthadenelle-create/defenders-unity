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

using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.Localization;
using UnityEngine;
using UnityEngine.Localization;
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
        private const string EnglishLocalePath = LocalizationDir + "/en.asset";

        private const string TableCollectionName = "GameStrings";
        private const string TablesDir = LocalizationDir + "/Tables";

        private const string EnJsonPath = "Assets/StreamingAssets/Data/Canonical/en.json";

        // English locale identifier — language code "en".
        private static readonly LocaleIdentifier EnglishId = new LocaleIdentifier("en");

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
            EnsureFolder(LocalizationDir);
            EnsureFolder(TablesDir);

            // 1. Project LocalizationSettings asset (created + made active).
            var settings = EnsureLocalizationSettings();

            // 2. English Locale, registered with the project.
            var englishLocale = EnsureEnglishLocale();

            // 3. GameStrings StringTableCollection, populated from en.json.
            var collection = EnsureStringTableCollection(englishLocale);
            int imported = PopulateFromJson(collection);

            // 4. Project-default / selected locale = English.
            ConfigureDefaultLocale(settings, englishLocale);

            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"[LocalizationBuilder] BuildAll complete — LocalizationSettings active, " +
                      $"English locale registered, '{TableCollectionName}' collection holds " +
                      $"{imported} string entries imported from en.json.");
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
        private static Locale EnsureEnglishLocale()
        {
            // Already registered with the project?
            var existing = LocalizationEditorSettings.GetLocale(EnglishId);
            if (existing != null)
                return existing;

            // Asset on disk but not yet registered?
            var onDisk = AssetDatabase.LoadAssetAtPath<Locale>(EnglishLocalePath);
            if (onDisk != null)
            {
                LocalizationEditorSettings.AddLocale(onDisk);
                return onDisk;
            }

            // Create a fresh English locale asset and register it.
            var locale = Locale.CreateLocale(EnglishId);
            locale.name = "English (en)";
            AssetDatabase.CreateAsset(locale, EnglishLocalePath);
            LocalizationEditorSettings.AddLocale(locale);
            Debug.Log($"[LocalizationBuilder] Created + registered English locale at {EnglishLocalePath}.");
            return locale;
        }

        // =====================================================================
        //  3. StringTableCollection
        // =====================================================================

        /// <summary>
        /// Returns the <c>GameStrings</c> <see cref="StringTableCollection"/>,
        /// creating it (with an English <see cref="StringTable"/>) if absent.
        /// </summary>
        private static StringTableCollection EnsureStringTableCollection(Locale englishLocale)
        {
            var collection = LocalizationEditorSettings.GetStringTableCollection(TableCollectionName);
            if (collection == null)
            {
                // CreateStringTableCollection seeds an English StringTable for the
                // single locale we pass in.
                collection = LocalizationEditorSettings.CreateStringTableCollection(
                    TableCollectionName, TablesDir, new List<Locale> { englishLocale });
                Debug.Log($"[LocalizationBuilder] Created StringTableCollection '{TableCollectionName}' in {TablesDir}.");
            }

            // Guarantee an English StringTable exists in the collection — re-runs
            // and pre-existing collections without one are handled here.
            if (collection.GetTable(EnglishId) == null)
                collection.AddNewTable(EnglishId);

            return collection;
        }

        /// <summary>
        /// Parses <c>en.json</c>, flattens it into dotted keys, and writes every
        /// entry into the collection's English <see cref="StringTable"/>. Existing
        /// keys are updated in place; new keys are added. Returns the number of
        /// string entries written.
        /// </summary>
        private static int PopulateFromJson(StringTableCollection collection)
        {
            var jsonFullPath = Path.GetFullPath(EnJsonPath);
            if (!File.Exists(jsonFullPath))
            {
                Debug.LogError($"[LocalizationBuilder] en.json not found at {EnJsonPath} — no strings imported.");
                return 0;
            }

            var raw = File.ReadAllText(jsonFullPath);
            JObject root;
            try
            {
                root = JObject.Parse(raw);
            }
            catch (JsonException e)
            {
                Debug.LogError($"[LocalizationBuilder] Failed to parse en.json: {e.Message}");
                return 0;
            }

            var flat = new Dictionary<string, string>();
            Flatten(root, null, flat);

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
            foreach (var kv in flat)
            {
                // AddEntry(key, value) is "add or update": it reuses the shared
                // key entry if it already exists (via FindKeyId(key, addIfMissing:true))
                // and overwrites the localized value. This is what makes re-runs
                // update in place instead of duplicating keys.
                var localizedEntry = englishTable.AddEntry(kv.Key, kv.Value);
                localizedEntry.IsSmart = UsesArguments(kv.Value);
                count++;
            }

            EditorUtility.SetDirty(sharedData);
            EditorUtility.SetDirty(englishTable);
            Debug.Log($"[LocalizationBuilder] Reconciled '{TableCollectionName}': " +
                      $"{count} current entries, {removed} stale entries removed.");
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
        private static void ConfigureDefaultLocale(LocalizationSettings settings, Locale englishLocale)
        {
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

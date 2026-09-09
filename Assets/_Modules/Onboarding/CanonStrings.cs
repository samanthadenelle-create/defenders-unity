// =============================================================================
// CanonStrings — read-only loader for the canonical onboarding text (Week 1)
// -----------------------------------------------------------------------------
// The Onboarding scenes must NEVER hardcode a canon string (the v2 port-spec
// Part 4 rule: "the Unity agent never types these inline"). The Localization
// package owns localizable copy through LocalText. This small compatibility
// facade still reads canon-strings.json for protected proper nouns.
//
//   canon-strings.json — proper nouns: tagline, publisher, game title …
//   LocalText          — localizable strings incl. the 3-line cold open.
//
// Canon strings use a flat string-to-string map. Unknown keys return a visible
// "[[missing:key]]" marker so
// a typo is obvious on screen rather than silently blank.
// =============================================================================

using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using UnityEngine;
using DeNelle.Core.UI;

namespace DeNelle.Onboarding
{
    /// <summary>
    /// Static read-only access to the canonical onboarding strings —
    /// <c>canon-strings.json</c> (proper nouns) and <see cref="LocalText"/>
    /// (localizable copy). Canon data is loaded lazily on first access.
    /// </summary>
    public static class CanonStrings
    {
        /// <summary>StreamingAssets-relative path to the canon proper-noun file.</summary>
        private const string CanonRelativePath = "Data/Canonical/canon-strings.json";

        // ── Canon-strings.json keys used by the Onboarding module ────────────
        /// <summary>Key — the title-screen tagline ("Echoes of a Forgotten Civilization").
        /// Value read at source 2026-09-06 from
        /// Assets/StreamingAssets/Data/Canonical/canon-strings.json:53. The doc-comment
        /// previously quoted the RETIRED "Hold the last light." (2026-07-24 rebrand).</summary>
        public const string KeyTagline = "tagline";
        /// <summary>Key — the publisher / studio name ("DeNelle Studios").</summary>
        public const string KeyPublisher = "publisher";
        /// <summary>Key — the main game title ("Echoes of Elarion").</summary>
        public const string KeyGameTitle = "gameTitle";
        /// <summary>Key — the series / franchise label ("Defenders of the Realm").</summary>
        public const string KeyGameSubtitle = "gameSubtitle";
        /// <summary>Key — the Heart-Wing brand-dragon proper noun.</summary>
        public const string KeyHeartWing = "heartWing";

        // LocalText keys for the three-line cold open (narrative-bible section 7.1).
        /// <summary>Key — cold-open line 1.</summary>
        public const string KeyColdOpenLine1 = "intro.coldOpen.line1";
        /// <summary>Key — cold-open line 2.</summary>
        public const string KeyColdOpenLine2 = "intro.coldOpen.line2";
        /// <summary>Key — cold-open line 3.</summary>
        public const string KeyColdOpenLine3 = "intro.coldOpen.line3";

        private static Dictionary<string, string> _canon;
        /// <summary>
        /// Resolves a key from <c>canon-strings.json</c> (the proper nouns).
        /// Returns a visible <c>[[missing:key]]</c> marker for an unknown key.
        /// </summary>
        public static string Canon(string key)
        {
            EnsureLoaded();
            return Resolve(_canon, key);
        }

        /// <summary>
        /// Resolves a localizable key through the global <see cref="LocalText"/> facade.
        /// Returns a visible <c>[[missing:key]]</c> marker for an unknown key.
        /// </summary>
        public static string Locale(string key) => LocalText.Get(key);

        /// <summary>The title-screen tagline — "Echoes of a Forgotten Civilization"
        /// (canon-strings.json "tagline"). Never hardcode it; this property is the seam.</summary>
        public static string Tagline => Canon(KeyTagline);

        /// <summary>The publisher name — "DeNelle Studios".</summary>
        public static string Publisher => Canon(KeyPublisher);

        /// <summary>The main game title — "Echoes of Elarion".</summary>
        public static string GameTitle => Canon(KeyGameTitle);

        /// <summary>The series / franchise label — "Defenders of the Realm" (this game is a chapter of that saga).</summary>
        public static string GameSubtitle => Canon(KeyGameSubtitle);

        /// <summary>The three cold-open lines, in order (narrative-bible §7.1).</summary>
        public static string[] ColdOpenLines()
        {
            return new[]
            {
                Locale(KeyColdOpenLine1),
                Locale(KeyColdOpenLine2),
                Locale(KeyColdOpenLine3),
            };
        }

        // =====================================================================
        //  Loading
        // =====================================================================

        private static void EnsureLoaded()
        {
            if (_canon == null) _canon = LoadMap(CanonRelativePath);
        }

        private static Dictionary<string, string> LoadMap(string relativePath)
        {
            // WebGL-safe load via CanonicalJson (Resources first, StreamingAssets fallback).
            // Boot-path catalog — must not throw in a browser (DEF-124 black screen).
            try
            {
                string json = DeNelle.Core.CanonicalJson.Read(relativePath);
                if (!string.IsNullOrEmpty(json))
                {
                    // Both canonical files are flat maps with some leading "_" metadata
                    // keys; deserialize loosely, then keep only the string entries.
                    var raw = JsonConvert.DeserializeObject<Dictionary<string, object>>(json);
                    var map = new Dictionary<string, string>();
                    if (raw != null)
                    {
                        foreach (var kv in raw)
                        {
                            if (kv.Value is string s) map[kv.Key] = s;
                        }
                    }
                    return map;
                }
                Debug.LogError($"[CanonStrings] Canonical file not found (Resources or StreamingAssets): {relativePath}.");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[CanonStrings] Failed to read {relativePath}: {ex.Message}");
            }
            return new Dictionary<string, string>();
        }

        private static string Resolve(Dictionary<string, string> map, string key)
        {
            if (map != null && key != null && map.TryGetValue(key, out var value) && value != null)
                return value;
            Debug.LogWarning($"[CanonStrings] Missing canonical key '{key}'.");
            return $"[[missing:{key}]]";
        }
    }
}

// =============================================================================
// RealmMapCatalog — typed loader for realm-map.json (WO-826, Realm Map program 825).
// -----------------------------------------------------------------------------
// Assembly: DeNelle.Core   Namespace: DeNelle.Core.World
//
// The Realm Map is the region-progression overworld: home base Elarion at the
// centre plus five fog-shrouded regions, each with a mapPoint (percent of the
// map rect), a discovery gate and a clear reward. The data is the DUAL-COPY
// canonical file (Resources + StreamingAssets, byte-identical — CanonicalJson
// law); this loader is the ONLY typed surface over it. Do NOT author a second
// region list in C# constants — realm-map.json is the single source of truth
// (WO-825 boot rule).
//
// Shape mirrors the React contract (src/contracts/region.ts RegionDef) exactly,
// as documented in the file's own _schemaNotes:
//   * gate is a discriminated union on `kind`: "bestWave" { value } |
//     "regionCleared" { regionId }. Carried here as one record with both
//     optional payload fields (the untyped union carrier pattern).
//   * _comment / _sources / _schemaNotes / progressLedger are metadata —
//     Newtonsoft simply never maps them (no fields declared for them here).
//   * Region STATE (locked/discovered/cleared) is DERIVED at runtime from the
//     RegionProgress save ledger — never stored in this file. The derivation
//     lives in RealmMapVM (presentation) until the WO-827 discovery ledger.
//
// Mirrors ChatPhraseCatalog in shape (static surface + EnsureLoaded through
// CanonicalJson) so maintainers find one familiar pattern across catalogs.
// Parse is Guard.Try-wrapped (§12): a bad file logs via FlowTrace.Fail and
// yields an EMPTY catalog, never a throw into the UI.
// =============================================================================

using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using DeNelle.Core.Diagnostics;

namespace DeNelle.Core.World
{
    /// <summary>A node coordinate on the parchment map, in PERCENT of the map rect
    /// (0..100, x rightward, y downward — the React realm-map-layout.ts convention).</summary>
    [Serializable]
    public sealed class RealmMapPoint
    {
        [JsonProperty("x")] public float X;
        [JsonProperty("y")] public float Y;
    }

    /// <summary>
    /// Discovery gate — the discriminated union from src/contracts/region.ts, carried
    /// as one record: <see cref="Kind"/> selects which payload field is meaningful
    /// ("bestWave" reads <see cref="Value"/>; "regionCleared" reads <see cref="RegionId"/>).
    /// </summary>
    [Serializable]
    public sealed class RealmRegionGate
    {
        /// <summary>Gate kind literal for "unlocks at village best-wave &gt;= Value".</summary>
        public const string KindBestWave = "bestWave";
        /// <summary>Gate kind literal for "unlocks once region RegionId is cleared".</summary>
        public const string KindRegionCleared = "regionCleared";

        [JsonProperty("kind")]     public string Kind;
        [JsonProperty("value")]    public int Value;        // bestWave payload
        [JsonProperty("regionId")] public string RegionId;  // regionCleared payload
    }

    /// <summary>One-time clear reward (crystals / food / coins; absent fields read 0).</summary>
    [Serializable]
    public sealed class RealmClearReward
    {
        [JsonProperty("crystals")] public int Crystals;
        [UnityEngine.Serialization.FormerlySerializedAs("Food")]
        [JsonProperty("food")]     public int Stone;
        [JsonProperty("coins")]    public int Coins;
    }

    /// <summary>The home base (Elarion) — not a region: no gate, never cleared.</summary>
    [Serializable]
    public sealed class HomeBaseDef
    {
        [JsonProperty("id")]          public string Id;
        [JsonProperty("title")]       public string Title;
        [JsonProperty("epithet")]     public string Epithet;
        [JsonProperty("description")] public string Description;
        [JsonProperty("mapPoint")]    public RealmMapPoint MapPoint;
        [JsonProperty("isRegion")]    public bool IsRegion;
    }

    /// <summary>One fog-shrouded region node (mirrors React RegionDef + additive map metadata).</summary>
    [Serializable]
    public sealed class RealmRegionDef
    {
        [JsonProperty("id")]            public string Id;
        [JsonProperty("title")]         public string Title;
        [JsonProperty("description")]   public string Description;
        [JsonProperty("biome")]         public string Biome;
        [JsonProperty("propSet")]       public string PropSet;
        [JsonProperty("waveCount")]     public int WaveCount;
        [JsonProperty("elementBias")]   public List<string> ElementBias = new List<string>();
        [JsonProperty("gate")]          public RealmRegionGate Gate;
        [JsonProperty("clearReward")]   public RealmClearReward ClearReward;
        [JsonProperty("mapPoint")]      public RealmMapPoint MapPoint;
        [JsonProperty("mapOrder")]      public int MapOrder;
        [JsonProperty("dungeonRegion")] public bool DungeonRegion;
        [JsonProperty("adjacency")]     public List<string> Adjacency = new List<string>();
    }

    /// <summary>
    /// The `withering` block (WO-829 §1). The Withering is the corruption creeping out
    /// of the Wound; on the parchment it is a darkened edge band — ATMOSPHERE ONLY.
    /// <see cref="WeeklyRealmThreat"/> stays false until a real event system exists:
    /// WO-829 forbids faking a "threatened" week, and realm-map.json's own comment
    /// forbids a punishing timer ("the cozy covenant forbids FOMO countdowns").
    /// </summary>
    [Serializable]
    public sealed class WitheringDef
    {
        [JsonProperty("edgeBorder")]        public bool EdgeBorder;
        [JsonProperty("weeklyRealmThreat")] public bool WeeklyRealmThreat;
    }

    /// <summary>The whole parsed realm-map.json (metadata keys are simply not mapped).</summary>
    [Serializable]
    public sealed class RealmMapData
    {
        [JsonProperty("version")]   public int Version;
        [JsonProperty("homeBase")]  public HomeBaseDef HomeBase;
        [JsonProperty("regions")]   public List<RealmRegionDef> Regions = new List<RealmRegionDef>();
        [JsonProperty("withering")] public WitheringDef Withering;
    }

    /// <summary>Static typed surface over the dual-copy Data/Canonical/realm-map.json.</summary>
    public static class RealmMapCatalog
    {
        /// <summary>StreamingAssets-relative path — the CanonicalJson key (Resources copy wins).</summary>
        public const string RelativePath = "Data/Canonical/realm-map.json";

        private static RealmMapData _data;
        private static Dictionary<string, RealmRegionDef> _byId;

        /// <summary>Home base Elarion — null only when the file is missing/unparseable.</summary>
        public static HomeBaseDef Home
        { get { EnsureLoaded(); return _data.HomeBase; } }

        /// <summary>All regions, sorted by mapOrder ascending. Never null (empty on load failure).</summary>
        public static IReadOnlyList<RealmRegionDef> Regions
        { get { EnsureLoaded(); return _data.Regions; } }

        /// <summary>The `withering` atmosphere block (WO-829 §1). NEVER null — an absent
        /// block yields the authored default (edge band on, weekly threat OFF), so the
        /// parchment always has its edge treatment and no surface can accidentally light
        /// up a "threatened" week that has no event system behind it.</summary>
        public static WitheringDef Withering
        {
            get
            {
                EnsureLoaded();
                return _data.Withering ?? DefaultWithering;
            }
        }

        private static readonly WitheringDef DefaultWithering =
            new WitheringDef { EdgeBorder = true, WeeklyRealmThreat = false };

        /// <summary>Region by id, or null when unknown.</summary>
        public static RealmRegionDef Find(string regionId)
        {
            if (string.IsNullOrEmpty(regionId)) return null;
            EnsureLoaded();
            _byId.TryGetValue(regionId, out var r);
            return r;
        }

        /// <summary>Display title for an id (home OR region), falling back to the id itself.
        /// Lets gate text name the prerequisite region without a second lookup site.</summary>
        public static string TitleFor(string id)
        {
            if (string.IsNullOrEmpty(id)) return "";
            EnsureLoaded();
            if (_data.HomeBase != null && _data.HomeBase.Id == id)
                return string.IsNullOrEmpty(_data.HomeBase.Title) ? id : _data.HomeBase.Title;
            var r = Find(id);
            return r != null && !string.IsNullOrEmpty(r.Title) ? r.Title : id;
        }

        /// <summary>Drop the cache and re-read the file (regression/test hook).</summary>
        public static void Reload() { _data = null; _byId = null; EnsureLoaded(); }

        private static void EnsureLoaded()
        {
            if (_data != null) return;

            string json = Guard.Try("RealmMap", "read realm-map.json",
                () => CanonicalJson.Read(RelativePath), null);

            RealmMapData parsed = null;
            if (!string.IsNullOrEmpty(json))
            {
                parsed = Guard.Try("RealmMap", "parse realm-map.json",
                    () => JsonConvert.DeserializeObject<RealmMapData>(json), null);
            }

            if (parsed != null && parsed.Regions != null && parsed.Regions.Count > 0)
            {
                parsed.Regions.Sort((a, b) =>
                    (a != null ? a.MapOrder : int.MaxValue).CompareTo(b != null ? b.MapOrder : int.MaxValue));
                _data = parsed;
                _byId = BuildIndex(parsed.Regions);
                FlowTrace.Step("RealmMap", "realm-map.json loaded: home='"
                    + (parsed.HomeBase != null ? parsed.HomeBase.Title : "<null>")
                    + "', " + parsed.Regions.Count + " regions (v" + parsed.Version + ")");
                return;
            }

            // No-silent-failure law: an absent/empty/unmappable file self-reports and the
            // catalog stays EMPTY (the panel then renders home-only, never throws).
            FlowTrace.Fail("RealmMap", "realm-map.json absent or deserialized empty — Realm Map catalog is EMPTY");
            _data = new RealmMapData { Regions = new List<RealmRegionDef>() };
            _byId = new Dictionary<string, RealmRegionDef>(0);
        }

        private static Dictionary<string, RealmRegionDef> BuildIndex(List<RealmRegionDef> regions)
        {
            var d = new Dictionary<string, RealmRegionDef>(regions.Count);
            foreach (var r in regions)
            {
                if (r == null || string.IsNullOrEmpty(r.Id)) continue;
                d[r.Id] = r;
            }
            return d;
        }
    }
}

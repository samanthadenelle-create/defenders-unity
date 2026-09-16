// FreshTownBuildingVisualRegression -- catalog-wide visual/seat gate.
// Marker: FRESH_TOWN_BUILDING_VISUAL_OK / FRESH_TOWN_BUILDING_VISUAL_FAIL
//
// This is intentionally an editor probe over the same Addressables catalog and
// StructureFactory orientation policy used by a newly-created player town. It
// does not assert a hand-picked list: every base visual and every authored tier
// rung is checked, including towers, Ballista, walls, gates, and defenses.
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using DeNelle.Core;
using DeNelle.Core.Catalog;
using DeNelle.Village;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;
using Object = UnityEngine.Object;

namespace DeNelle.Editor.Regression
{
    public static class FreshTownBuildingVisualRegression
    {
        private const string CatalogPath = "Data/Canonical/structures-catalog.json";

        [Serializable]
        private sealed class CatalogFile
        {
            [JsonProperty("entries")] public List<CatalogEntry> Entries = new List<CatalogEntry>();
        }

        private sealed class Subject
        {
            public CatalogEntry Entry;
            public string Address;
            public int Tier;
            public string Label => Tier == 0 ? Entry.id : Entry.id + " L" + Tier;
        }

        public static bool Run(out string reason)
        {
            var failures = new List<string>();
            var log = new StringBuilder();
            try
            {
                var entries = ReadCatalog(failures);
                var addressMap = BuildAddressMap();
                if (addressMap == null)
                    failures.Add("AddressableAssetSettings is missing; a fresh town cannot resolve building art.");

                var subjects = new List<Subject>();
                if (entries != null)
                {
                    foreach (var e in entries)
                    {
                        if (e == null || string.IsNullOrEmpty(e.id) || string.IsNullOrEmpty(e.visualPrefabPath)) continue;
                        subjects.Add(new Subject { Entry = e, Address = e.visualPrefabPath, Tier = 0 });
                        var ladder = e.repo != null ? e.repo.upgradeVisualPath : null;
                        if (ladder == null) continue;
                        for (int i = 0; i < ladder.Length; i++)
                        {
                            if (string.IsNullOrEmpty(ladder[i]) || ladder[i] == e.visualPrefabPath) continue;
                            subjects.Add(new Subject { Entry = e, Address = ladder[i], Tier = i + 2 });
                        }
                    }
                }

                int checkedCount = 0;
                foreach (var s in subjects)
                {
                    if (addressMap == null || !addressMap.TryGetValue(s.Address, out string assetPath) || string.IsNullOrEmpty(assetPath))
                    {
                        failures.Add($"{s.Label}: visual address '{s.Address}' is not registered in Addressables.");
                        continue;
                    }
                    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
                    if (prefab == null)
                    {
                        failures.Add($"{s.Label}: Addressables points to '{assetPath}', but no GameObject loads.");
                        continue;
                    }

                    var opts = s.Tier == 0
                        ? StructureFactory.OptsFor(s.Entry)
                        : StructureFactory.OptsForUpgradeLevel(s.Entry, s.Tier);
                    if (!Probe(s, prefab, opts, failures)) continue;
                    checkedCount++;
                }

                log.AppendLine($"subjects={subjects.Count} checked={checkedCount} (base visuals + authored upgrade tiers)");
                log.AppendLine("coverage=towers, Ballista, walls, gates, defenses, support/building rows, and every authored tier");
                if (failures.Count == 0)
                {
                    reason = $"FRESH_TOWN_BUILDING_VISUAL_OK {checkedCount} subject(s): every catalog visual has a renderable mesh/material, " +
                             "production orientation policy, finite fitted bounds, and a verified ground seat.";
                    Debug.Log(log + reason);
                    return true;
                }
                reason = "FRESH_TOWN_BUILDING_VISUAL_FAIL: " + string.Join(" | ", failures.ToArray());
                Debug.LogError(log + reason);
                return false;
            }
            catch (Exception ex)
            {
                reason = "FRESH_TOWN_BUILDING_VISUAL_FAIL: threw " + ex.GetType().Name + ": " + ex.Message;
                Debug.LogError(reason);
                return false;
            }
        }

        public static void RunStandalone()
        {
            bool ok = Run(out string reason);
            Debug.Log(reason);
            if (!ok) EditorApplication.Exit(1);
        }

        private static bool Probe(Subject s, GameObject prefab, SkinOptions opts, List<string> failures)
        {
            GameObject go = null;
            try
            {
                go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                go.hideFlags = HideFlags.HideAndDontSave;
                go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                if (opts.LocalRotation.HasValue) go.transform.localRotation = opts.LocalRotation.Value;
                else if (!opts.PreservePrefabRotation) go.transform.localRotation = Quaternion.identity;

                var renderers = go.GetComponentsInChildren<Renderer>(true);
                if (renderers == null || renderers.Length == 0)
                {
                    failures.Add($"{s.Label}: no Renderer components.");
                    return false;
                }
                bool mesh = false;
                foreach (var r in renderers)
                {
                    if (r == null || !r.enabled) continue;
                    if (r is MeshRenderer && r.GetComponent<MeshFilter>()?.sharedMesh != null) mesh = true;
                    if (r is SkinnedMeshRenderer && ((SkinnedMeshRenderer)r).sharedMesh != null) mesh = true;
                    var mats = r.sharedMaterials;
                    if (mats == null || mats.Length == 0) failures.Add($"{s.Label}: renderer '{r.name}' has no material slots.");
                    else foreach (var m in mats) if (m == null || m.shader == null) failures.Add($"{s.Label}: renderer '{r.name}' has a null material/shader.");
                }
                if (!mesh) failures.Add($"{s.Label}: no enabled renderer has a mesh.");
                if (!Bounds(go, out Bounds pre) || pre.size.y <= 0.0001f || !Finite(pre))
                {
                    failures.Add($"{s.Label}: pre-fit bounds are missing, degenerate, or non-finite.");
                    return false;
                }

                float target = opts.FitHeight > 0f ? opts.FitHeight : StructureFactory.YHeightVariable;
                go.transform.localScale *= target / pre.size.y;
                if (opts.MaxFootprint > 0f && Bounds(go, out Bounds capped))
                {
                    float widest = Mathf.Max(capped.size.x, capped.size.z);
                    if (widest > opts.MaxFootprint) go.transform.localScale *= opts.MaxFootprint / widest;
                }
                if (!Bounds(go, out Bounds fitted) || !Finite(fitted))
                {
                    failures.Add($"{s.Label}: fitted bounds are missing or non-finite.");
                    return false;
                }

                // Exact runtime seat property: the visible bounds bottom touches the
                // host plane. X/Z centering is included because off-pivot shops must
                // not drift away from their build cell after rotation.
                go.transform.position += new Vector3(-fitted.center.x, -fitted.min.y, -fitted.center.z);
                if (!VisualFactory.IsSeatedOnGround(go, 0f, out float bottomY))
                    failures.Add($"{s.Label}: final bounds bottom={bottomY:0.000} is not seated on ground.");
                if (!Bounds(go, out Bounds finalBounds) || finalBounds.size.y <= 0.01f)
                    failures.Add($"{s.Label}: final seated bounds are degenerate.");
                return true; // failures are accumulated globally so every subject is still audited
            }
            catch (Exception ex)
            {
                failures.Add($"{s.Label}: probe threw {ex.GetType().Name}: {ex.Message}");
                return false;
            }
            finally { if (go != null) Object.DestroyImmediate(go); }
        }

        private static bool Bounds(GameObject go, out Bounds b)
        {
            var rs = go.GetComponentsInChildren<Renderer>(true);
            b = default;
            bool found = false;
            foreach (var r in rs)
            {
                if (r == null || !r.enabled) continue;
                if (!found) { b = r.bounds; found = true; } else b.Encapsulate(r.bounds);
            }
            return found;
        }

        private static bool Finite(Bounds b) =>
            IsFinite(b.min) && IsFinite(b.max) && b.size.x > 0f && b.size.y > 0f && b.size.z > 0f;
        private static bool IsFinite(Vector3 v) =>
            !float.IsNaN(v.x) && !float.IsInfinity(v.x) && !float.IsNaN(v.y) && !float.IsInfinity(v.y) && !float.IsNaN(v.z) && !float.IsInfinity(v.z);

        private static List<CatalogEntry> ReadCatalog(List<string> failures)
        {
            string json = CanonicalJson.Read(CatalogPath);
            if (string.IsNullOrEmpty(json)) { failures.Add("structures-catalog.json is unreadable."); return null; }
            try
            {
                var settings = new JsonSerializerSettings { Converters = { new StringEnumConverter() }, MissingMemberHandling = MissingMemberHandling.Ignore };
                var file = JsonConvert.DeserializeObject<CatalogFile>(json, settings);
                if (file == null || file.Entries == null || file.Entries.Count == 0) failures.Add("structures-catalog.json contains no entries.");
                return file?.Entries;
            }
            catch (Exception ex) { failures.Add("structures-catalog.json failed to parse: " + ex.Message); return null; }
        }

        private static Dictionary<string, string> BuildAddressMap()
        {
            var settings = AddressableAssetSettingsDefaultObject.Settings;
            if (settings == null) return null;
            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var group in settings.groups)
                if (group != null)
                    foreach (var entry in group.entries)
                        if (entry != null && !string.IsNullOrEmpty(entry.address)) map[entry.address] = AssetDatabase.GUIDToAssetPath(entry.guid);
            return map;
        }
    }
}

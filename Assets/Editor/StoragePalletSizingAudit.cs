using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DeNelle.Core.Catalog;
using DeNelle.Village;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor.SceneManagement;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace DeNelle.Editor
{
    // Read-only evidence: the owner hand-authored the storage references beside collectors.
    public static class StoragePalletSizingAudit
    {
        // Owner ruling: each catalog storage uses the exact hand-authored pallet beside
        // its collector. Copy only the selected rendering subtree, retaining its full
        // ancestor transform chain (including nonuniform-scale effects), never gameplay.
        public static void ApplyApprovedReferences()
        {
            const string scenePath = "Assets/Scenes/Main_Castle_Overworld.unity";
            var original = File.ReadAllBytes(scenePath);
            var scene = EditorSceneManager.OpenPreviewScene(scenePath);
            try
            {
                var settings = AddressableAssetSettingsDefaultObject.Settings;
                var group = settings.groups.Single(g => g != null && g.Name == "Structure_Art");
                var all = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Transform>(true)).ToArray();
                const string folder = "Assets/Prefabs/Village/OwnerStorage";
                Directory.CreateDirectory(folder);
                AssetDatabase.Refresh();
                foreach (string name in new[] { "Wood_Pallet", "Iron_Pallet", "Stone_Pallet" })
                {
                    var source = all.Single(t => t.name == name);
                    if (source.GetComponentsInChildren<SkinnedMeshRenderer>(true).Length != 0)
                        throw new InvalidOperationException("Storage reference unexpectedly skinned: " + name);
                    var wrapper = new GameObject(name + "_ApprovedStorage");
                    SceneManager.MoveGameObjectToScene(wrapper, scene);
                    try
                    {
                        var ancestors = new Stack<Transform>();
                        for (var p = source.parent; p != null; p = p.parent) ancestors.Push(p);
                        Transform parent = wrapper.transform;
                        bool first = true;
                        while (ancestors.Count > 0)
                        {
                            var ancestor = ancestors.Pop();
                            var copy = CopyTransform(ancestor, parent);
                            if (first) copy.localPosition -= source.position;
                            first = false;
                            parent = copy;
                        }
                        var selected = CopyVisual(source, parent);
                        if (first) selected.localPosition -= source.position;
                        var expected = BoundsOf(source.gameObject).size;
                        var actual = BoundsOf(wrapper).size;
                        if ((expected - actual).sqrMagnitude > 0.000001f)
                            throw new InvalidOperationException(name + " transform-chain copy changed bounds");
                        string path = folder + "/" + name + ".prefab";
                        PrefabUtility.SaveAsPrefabAsset(wrapper, path);
                        var entry = settings.CreateOrMoveEntry(AssetDatabase.AssetPathToGUID(path), group, false, false);
                        entry.SetAddress("Structures/OwnerStorage/" + name, false);
                        entry.SetLabel("Structure_Art", true, true, false);
                        Debug.Log("STORAGE_REFERENCE_BUILT " + name + " size=" + actual.ToString("F6") + " path=" + path);
                    }
                    finally { Object.DestroyImmediate(wrapper); }
                }
                EditorUtility.SetDirty(group);
                EditorUtility.SetDirty(settings);
                AssetDatabase.SaveAssets();
                if (!original.SequenceEqual(File.ReadAllBytes(scenePath))) throw new InvalidOperationException("Saved castle changed");
                Debug.Log("STORAGE_REFERENCE_BUILD_OK three approved render-only references; saved castle unchanged");
            }
            catch (Exception e) { Debug.LogError("STORAGE_REFERENCE_BUILD_FAIL " + e); }
            finally { EditorSceneManager.ClosePreviewScene(scene); }
        }

        private static Transform CopyTransform(Transform source, Transform parent)
        {
            var copy = new GameObject(source.name).transform;
            copy.SetParent(parent, false);
            copy.localPosition = source.localPosition;
            copy.localRotation = source.localRotation;
            copy.localScale = source.localScale;
            copy.gameObject.SetActive(source.gameObject.activeSelf);
            return copy;
        }

        private static Transform CopyVisual(Transform source, Transform parent)
        {
            var copy = CopyTransform(source, parent);
            if (source.TryGetComponent<MeshFilter>(out var filter))
                copy.gameObject.AddComponent<MeshFilter>().sharedMesh = filter.sharedMesh;
            if (source.TryGetComponent<MeshRenderer>(out var renderer))
                EditorUtility.CopySerialized(renderer, copy.gameObject.AddComponent<MeshRenderer>());
            foreach (Transform child in source) CopyVisual(child, copy);
            return copy;
        }

        public static void VerifyApprovedReferences() => Audit(true);
        public static void Run()
            => Audit(false);

        private static void Audit(bool assertParity)
        {
            const string scenePath = "Assets/Scenes/Main_Castle_Overworld.unity";
            string original = Convert.ToBase64String(File.ReadAllBytes(scenePath));
            var scene = EditorSceneManager.OpenPreviewScene(scenePath);
            var evidence = new List<string>();
            var results = new JArray();
            try
            {
                var objects = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Transform>(true)).ToArray();
                var json = JObject.Parse(File.ReadAllText("Assets/Resources/Data/Canonical/structures-catalog.json"));
                var serializer = JsonSerializer.Create(new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore });
                foreach (var pair in new[] { ("lumberyard", "Wood_Pallet", "LumberMill"), ("foundry", "Iron_Pallet", "IronMine"), ("silo", "Stone_Pallet", "Stone_Quarry") })
                {
                    var reference = objects.Single(t => string.Equals(t.name, pair.Item2, StringComparison.OrdinalIgnoreCase));
                    var collector = objects.Single(t => string.Equals(t.name, pair.Item3, StringComparison.OrdinalIgnoreCase));
                    var row = json["entries"].Single(t => (string)t["id"] == pair.Item1);
                    var entry = row.ToObject<CatalogEntry>(serializer);
                    Bounds target = BoundsOf(reference.gameObject);
                    var host = new GameObject("StorageSizing_" + entry.id);
                    SceneManager.MoveGameObjectToScene(host, scene);
                    try
                    {
                        var visual = VisualFactory.Skin(host.transform, entry.visualPrefabPath, StructureFactory.OptsFor(entry));
                        if (visual == null) throw new InvalidOperationException(entry.id + " production Skin did not resolve art");
                        if (entry.orientation != null && entry.orientation.manual)
                        {
                            visual.transform.localPosition += entry.orientation.Offset;
                            if (entry.orientation.HasScale) visual.transform.localScale = Vector3.Scale(visual.transform.localScale, entry.orientation.EffectiveScale);
                        }
                        Bounds actual = BoundsOf(visual);
                        if (assertParity)
                        {
                            if ((target.size - actual.size).sqrMagnitude > 0.00001f)
                                throw new InvalidOperationException(entry.id + " owner size mismatch: " + target.size + " vs " + actual.size);
                            var expectedMeshes = reference.GetComponentsInChildren<MeshFilter>(true).Select(f => f.sharedMesh).ToArray();
                            var actualMeshes = visual.GetComponentsInChildren<MeshFilter>(true).Select(f => f.sharedMesh).ToArray();
                            if (!expectedMeshes.SequenceEqual(actualMeshes)) throw new InvalidOperationException(entry.id + " owner mesh mismatch");
                            var expectedMaterials = reference.GetComponentsInChildren<MeshRenderer>(true).SelectMany(r => r.sharedMaterials).ToArray();
                            var actualMaterials = visual.GetComponentsInChildren<MeshRenderer>(true).SelectMany(r => r.sharedMaterials).ToArray();
                            if (!expectedMaterials.SequenceEqual(actualMaterials)) throw new InvalidOperationException(entry.id + " owner material mismatch");
                        }
                        float widest = Mathf.Max(target.size.x, target.size.z);
                        if (!float.IsFinite(widest) || widest <= 0) throw new InvalidOperationException("Invalid owner reference bounds");
                        var result = new JObject
                        {
                            ["id"] = entry.id, ["reference"] = reference.name, ["collector"] = collector.name,
                            ["referencePosition"] = Vec(reference.position),
                            ["collectorPosition"] = Vec(collector.position),
                            ["referenceSize"] = Vec(target.size), ["productionSize"] = Vec(actual.size),
                            ["approvedMaxFootprint"] = widest,
                            ["referenceMeshes"] = new JArray(reference.GetComponentsInChildren<MeshFilter>(true).Select(f => f.sharedMesh != null ? f.sharedMesh.name : "NULL")),
                            ["productionMeshes"] = new JArray(visual.GetComponentsInChildren<MeshFilter>(true).Select(f => f.sharedMesh != null ? f.sharedMesh.name : "NULL"))
                        };
                        results.Add(result);
                        evidence.Add(result.ToString(Formatting.None));
                        Debug.Log("STORAGE_PALLET_MEASURE " + result.ToString(Formatting.None));
                        OwnerCastleLayoutAudit.Capture(reference.gameObject, scene, "StorageReference_" + entry.id, evidence);
                        OwnerCastleLayoutAudit.Capture(visual, scene, "StorageCatalog_" + entry.id, evidence);
                    }
                    finally { Object.DestroyImmediate(host); }
                }
                if (Convert.ToBase64String(File.ReadAllBytes(scenePath)) != original) throw new InvalidOperationException("Saved castle file changed");
                string folder = "Builds/castle-validation-20260913";
                Directory.CreateDirectory(folder);
                File.WriteAllText(folder + "/storage-pallet-measurements.json", results.ToString(Formatting.Indented));
                File.WriteAllLines(folder + "/storage-pallet-measurements.txt", evidence);
                Debug.Log("STORAGE_PALLET_AUDIT_OK three owner references and production Skin paths measured; saved scene unchanged");
                if (assertParity) Debug.Log("STORAGE_PALLET_PARITY_OK three exact owner meshes/materials and sizes via production Skin");
            }
            catch (Exception error) { Debug.LogError("STORAGE_PALLET_AUDIT_FAIL " + error); }
            finally { EditorSceneManager.ClosePreviewScene(scene); }
        }

        private static JObject Vec(Vector3 value) => new JObject { ["x"] = value.x, ["y"] = value.y, ["z"] = value.z };
        private static Bounds BoundsOf(GameObject root)
        {
            var renderers = root.GetComponentsInChildren<Renderer>().Where(r => r.enabled && r.gameObject.activeInHierarchy).ToArray();
            if (renderers.Length == 0) throw new InvalidOperationException(root.name + " has no active renderers");
            var result = renderers[0].bounds;
            foreach (var renderer in renderers) result.Encapsulate(renderer.bounds);
            return result;
        }
    }
}

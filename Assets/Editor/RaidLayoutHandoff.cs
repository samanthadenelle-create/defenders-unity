using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DeNelle.Editor
{
    /// <summary>Read-only authoring handoff; never saves or regenerates the author's scene.</summary>
    public static class RaidLayoutHandoff
    {
        [Serializable] private sealed class Document
        {
            public int version = 1;
            public string scenePath, sceneGuid, sceneSha256, capturedUtc;
            public List<Node> nodes = new List<Node>();
        }

        [Serializable] private sealed class Node
        {
            public string key, parentKey, name, prefabAssetPath;
            public Vector3 localPosition, localScale, worldPosition;
            public Quaternion localRotation;
            public bool activeSelf;
            public string[] componentTypes;
            public List<AssetReference> meshes = new List<AssetReference>();
            public List<AssetReference> materials = new List<AssetReference>();
        }

        [Serializable] private sealed class AssetReference
        {
            public string path, guid;
            public long localId;
        }

        [MenuItem("Defenders/Raids/Export Saved Layout Handoff")]
        public static void Export()
        {
            var scene = SceneManager.GetActiveScene();
            if (!scene.IsValid() || !scene.isLoaded || string.IsNullOrEmpty(scene.path) ||
                !scene.name.StartsWith("RaidBase_", StringComparison.Ordinal))
                throw new InvalidOperationException("Open the saved RaidBase scene to export its layout.");
            if (EditorApplication.isPlayingOrWillChangePlaymode || scene.isDirty)
                throw new InvalidOperationException("Exit Play mode and save your scene before exporting the layout.");

            var sourceBytes = File.ReadAllBytes(scene.path);
            var doc = new Document
            {
                scenePath = scene.path,
                sceneGuid = AssetDatabase.AssetPathToGUID(scene.path),
                capturedUtc = DateTime.UtcNow.ToString("O"),
                sceneSha256 = Hash(sourceBytes)
            };
            var roots = scene.GetRootGameObjects();
            Array.Sort(roots, (a, b) => a.transform.GetSiblingIndex().CompareTo(b.transform.GetSiblingIndex()));
            foreach (var root in roots)
                Capture(root.transform, null, root.transform.GetSiblingIndex().ToString(), doc);

            // Keep the exact saved source alongside the transform index. Component settings,
            // prefab overrides and colliders remain recoverable even if a future importer
            // does not understand them. No copy is placed under Assets or build settings.
            string directory = Path.Combine("Builds", "raid-layout-handoff",
                scene.name + "-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff") + "-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(directory);
            File.WriteAllBytes(Path.Combine(directory, scene.name + ".unity"), sourceBytes);
            File.Copy(scene.path + ".meta", Path.Combine(directory, scene.name + ".unity.meta"));
            File.WriteAllText(Path.Combine(directory, "layout.json"), JsonUtility.ToJson(doc, true));
            Debug.Log("RAID_LAYOUT_HANDOFF_OK nodes=" + doc.nodes.Count + " sha256=" + doc.sceneSha256 +
                " output=" + Path.GetFullPath(directory));
        }

        private static void Capture(Transform t, string parentKey, string key, Document doc)
        {
            var components = t.GetComponents<Component>();
            var node = new Node
            {
                key = key, parentKey = parentKey, name = t.name,
                localPosition = t.localPosition, localRotation = t.localRotation,
                localScale = t.localScale, worldPosition = t.position,
                activeSelf = t.gameObject.activeSelf,
                prefabAssetPath = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(t.gameObject),
                componentTypes = Array.ConvertAll(components, c => c == null ? "<missing-script>" : c.GetType().FullName)
            };
            foreach (var mesh in t.GetComponents<MeshFilter>()) node.meshes.Add(Reference(mesh.sharedMesh));
            foreach (var renderer in t.GetComponents<Renderer>())
            {
                if (renderer is SkinnedMeshRenderer skinned) node.meshes.Add(Reference(skinned.sharedMesh));
                foreach (var material in renderer.sharedMaterials) node.materials.Add(Reference(material));
            }
            doc.nodes.Add(node);
            for (int i = 0; i < t.childCount; i++) Capture(t.GetChild(i), key, key + "/" + i, doc);
        }

        private static AssetReference Reference(UnityEngine.Object asset)
        {
            var reference = new AssetReference { path = asset == null ? null : AssetDatabase.GetAssetPath(asset) };
            if (asset != null && AssetDatabase.TryGetGUIDAndLocalFileIdentifier(asset, out string guid, out long localId))
            { reference.guid = guid; reference.localId = localId; }
            return reference;
        }

        private static string Hash(byte[] bytes)
        {
            using (var algorithm = SHA256.Create())
                return BitConverter.ToString(algorithm.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
        }
    }
}

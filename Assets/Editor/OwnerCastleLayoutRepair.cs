using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using DeNelle.Core;
using DeNelle.Village;
using DeNelle.Village.Buildings.Progression;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace DeNelle.Editor
{
    // Owner 2026-09-13: retain the saved thirteen, add the original Cathedral, repair Tripo materials.
    // Preview is entirely transient. Apply saves only this scene and NEW material copies after checks.
    public static class OwnerCastleLayoutRepair
    {
        const string Cathedral = "ArcaneTower_MagicUpgrades";
        public const string LayoutPrefab = "Assets/Prefabs/Village/OwnerCastleStorefrontLayout.prefab";
        static readonly string MaterialRoot = AssetRoots.StructureContent + "/OwnerCastleMaterials";
        sealed class Row
        {
            public string Name, Id, Legacy, Guid;
            public bool Tripo;
            public Row(string name, string id, string legacy, string guid, bool tripo = false)
            { Name = name; Id = id; Legacy = legacy; Guid = guid; Tripo = tripo; }
        }
        static readonly Row[] Rows =
        {
            new Row("Jeweler", "jeweler", "Jeweler_Gems_Storefront", "0a7c9cb08bbabf746b74f27fb8340631", true),
            new Row("Weaponsmith", "forge", "Blacksmith_Weapons_Storefront", "edc9e21a5960f7e4f8d84543eae60a5b", true),
            new Row("Armorer", "armorer", "Forge_Armor_Storefront", "dd1a409803987a94791b857d7e72f003", true),
            new Row("Echo_Hollow", "pet-house", "EchoHollow_Pets_RoamingArea", "82950f9e84e76ce478bc6d84967bf5d9", true),
            new Row("LumberMill", "collector_lumbermill", "Lumbermill_Wood_Storefront", "0225e10fc70c9dd49a8548d2a016cf50", true),
            new Row("Stone_Quarry", "collector_farm", "Windmill_Food_Storefront", "29c902f092e36ae428ad25d416b25d60"),
            new Row("IronMine", "collector_forge", "IronMine", "585f03e5d6ce6cd428531f7ee9b3f225"),
            new Row("Crafting", "workshop", "Crafting", "bbb71b18924c2b5448431f7d7f8c25c1"),
            new Row("Barracks", "barracks", "CastleBarracks", "5a258590045b55041aa22a4362144c9d", true),
            new Row("RealmStore", "", "RealmStore_Storefront", "54f795e6004947e488b96d86a60f0ab3", true),
            new Row("Iron_Pallet", null, null, "3cf11469951e4da4b9ee72c0a264aa40"),
            new Row("Wood_Pallet", null, null, "f4a92ef53f0848b4bae8624bd8beb3cf"),
            new Row("Stone_Pallet", null, null, "12ea34ea2ae82fa4ba966c37cb66bd0f")
        };
        sealed class Pose
        {
            public Transform Target, Parent;
            public Vector3 Position, Scale, WorldPosition, WorldScale;
            public Quaternion Rotation, WorldRotation;
            public int Children;
            public Pose(Transform t)
            {
                Target = t; Parent = t.parent; Position = t.localPosition; Scale = t.localScale; Rotation = t.localRotation;
                WorldPosition = t.position; WorldScale = t.lossyScale; WorldRotation = t.rotation; Children = t.childCount;
            }
            public void Assert()
            {
                if (Target == null || Target.parent != Parent || !Target.localPosition.Equals(Position) || !Target.localScale.Equals(Scale) ||
                    !Target.localRotation.Equals(Rotation) || !Target.position.Equals(WorldPosition) || !Target.lossyScale.Equals(WorldScale) ||
                    !Target.rotation.Equals(WorldRotation) || Target.childCount != Children)
                    throw new InvalidOperationException("Original transform/hierarchy changed: " + (Target == null ? "missing" : Target.name));
            }
        }

        [MenuItem("Defenders/Art/Preview owner castle repair")]
        public static void Preview() => Execute(false);
        [MenuItem("Defenders/Art/Apply owner castle repair")]
        public static void Apply() => Execute(true);

        // Captured by OwnerCastleRuntimeProof: the empty group carried a Prototype Bits
        // workbench collider at world (0,0,1.3), with no corresponding visible mesh.
        public static void RepairGroupPhysics()
        {
            if (Application.isPlaying) throw new InvalidOperationException("Edit mode only");
            for (int i = 0; i < SceneManager.sceneCount; i++)
                if (SceneManager.GetSceneAt(i).isDirty) throw new InvalidOperationException("Dirty scene refusal");
            var scene = EditorSceneManager.OpenScene(OwnerCastleLayoutAudit.ScenePath, OpenSceneMode.Single);
            var ring = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Transform>(true)).Single(t => t.name == OwnerCastleLayoutAudit.RingName);
            var collider = ring.GetComponent<MeshCollider>();
            var filter = ring.GetComponent<MeshFilter>();
            var renderer = ring.GetComponent<MeshRenderer>();
            if (collider == null && filter == null && renderer == null)
            { Debug.Log("OWNER_CASTLE_GROUP_PHYSICS_OK already clean"); return; }
            if (collider == null || collider.sharedMesh == null || filter == null || filter.sharedMesh != null || renderer == null ||
                renderer.sharedMaterials.Any(m => m != null) ||
                AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(collider.sharedMesh)) != "0126383ea2f58794d93ca66c4fb8785f")
                throw new InvalidOperationException("Group geometry differs from captured orphan; refusing removal");
            Physics.SyncTransforms();
            var bounds = collider.bounds;
            var ray = new Ray(bounds.center + Vector3.up * (bounds.extents.y + 2f), Vector3.down);
            if (!collider.Raycast(ray, out var hit, bounds.size.y + 4f))
                throw new InvalidOperationException("Orphan collider did not reproduce its physics hit");
            foreach (Transform child in ring)
                if (BoundsOf(child.gameObject).Intersects(bounds))
                    throw new InvalidOperationException("Group collider overlaps authored geometry; needs separate review");
            Debug.Log("OWNER_CASTLE_ORPHAN_HIT source=" + AssetDatabase.GetAssetPath(collider.sharedMesh) +
                " hit=" + hit.point + " bounds=" + bounds + "; no visible mesh/material, no child geometry at footprint");
            var poses = ring.Cast<Transform>().SelectMany(t => t.GetComponentsInChildren<Transform>(true)).Select(t => new Pose(t)).ToArray();
            string stamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff");
            Directory.CreateDirectory(OwnerCastleLayoutAudit.Output);
            File.Copy(OwnerCastleLayoutAudit.ScenePath, Path.Combine(OwnerCastleLayoutAudit.Output, "before_group_physics_" + stamp + ".unity"));
            File.Copy(LayoutPrefab, Path.Combine(OwnerCastleLayoutAudit.Output, "before_group_physics_" + stamp + ".prefab"));
            Object.DestroyImmediate(collider); Object.DestroyImmediate(renderer); Object.DestroyImmediate(filter);
            foreach (var pose in poses) pose.Assert();
            if (!EditorSceneManager.SaveScene(scene, OwnerCastleLayoutAudit.ScenePath)) throw new InvalidOperationException("Scene save refused");
            var recipe = PrefabUtility.SaveAsPrefabAsset(ring.gameObject, LayoutPrefab);
            if (recipe == null || recipe.GetComponent<Collider>() != null || recipe.GetComponent<Renderer>() != null || recipe.GetComponent<MeshFilter>() != null)
                throw new InvalidOperationException("Recipe still has orphan group geometry");
            Debug.Log("OWNER_CASTLE_GROUP_PHYSICS_OK removed only orphan group collider/filter/renderer; " +
                ring.childCount + " children preserved; scene and recipe saved");
        }

        static void Execute(bool save)
        {
            var setup = EditorSceneManager.GetSceneManagerSetup();
            bool opened = false, empty = false, saved = false;
            var copies = new List<Material>();
            var evidence = new List<string>();
            string mode = save ? "APPLY" : "PREVIEW";
            try
            {
                if (Application.isPlaying) throw new InvalidOperationException("Edit mode only");
                for (int i = 0; i < SceneManager.sceneCount; i++)
                {
                    var current = SceneManager.GetSceneAt(i);
                    if (current.isDirty) throw new InvalidOperationException("Dirty scene; refusing: " + current.path);
                    if (string.IsNullOrEmpty(current.path))
                    {
                        if (!Application.isBatchMode || SceneManager.sceneCount != 1 || current.rootCount != 0)
                            throw new InvalidOperationException("Populated/interactive untitled scene; refusing");
                        empty = true;
                    }
                }
                opened = true;
                var scene = EditorSceneManager.OpenScene(OwnerCastleLayoutAudit.ScenePath, OpenSceneMode.Single);
                var ring = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Transform>(true)).Single(t => t.name == OwnerCastleLayoutAudit.RingName);
                // WO-1762 (owner 2026-09-15): the three scenery pallet rows are OPTIONAL from here on.
                // She ruled them out of the courtyard ("you could remove the three storage that I added"),
                // and they were never storage in the first place - no AuthoredCastleStorefront, no
                // canonicalId, no BaseLayout row, so TownBankCapacity.BuildSlots could never count them.
                // They are the rows whose Legacy is null, which is precisely the set this method already
                // skips at the Configure loop below. A HARD Single() here would have made this repair seam
                // throw the moment HubRingHeightApply.Run removed them - the tool that repairs the ring
                // refusing to open the ring it repaired.
                // Every OTHER row is still mandatory: a missing one means the saved layout changed.
                var existing = new Dictionary<Row, GameObject>();
                foreach (var row in Rows)
                {
                    var found = ring.Cast<Transform>().SingleOrDefault(t => t.name == row.Name);
                    if (found != null) { existing[row] = found.gameObject; continue; }
                    if (row.Legacy != null) throw new InvalidOperationException("Saved root missing from the ring: " + row.Name);
                }
                var cathedral = ring.Cast<Transform>().SingleOrDefault(t => t.name == Cathedral)?.gameObject;
                if (ring.childCount != existing.Count + (cathedral == null ? 0 : 1)) throw new InvalidOperationException("Unexpected ring children");
                foreach (var pair in existing)
                {
                    string path = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(pair.Value);
                    if (AssetDatabase.AssetPathToGUID(path) != pair.Key.Guid) throw new InvalidOperationException("Saved source changed: " + pair.Key.Name);
                    if (pair.Value.GetComponentsInChildren<Transform>(true).Any(t => GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject) != 0))
                        throw new InvalidOperationException("Missing script: " + pair.Key.Name);
                }
                var original = AssetDatabase.LoadAssetAtPath<GameObject>(AssetRoots.StructureContent + "/arcane tower.fbx");
                var albedo = AssetDatabase.LoadAssetAtPath<Texture2D>(AssetRoots.StructureContent + "/ArcaneTower_Albedo.jpg");
                if (original == null || albedo == null || AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(original)) != "f70a5ba1f6656fe4fb83272ea292424e" ||
                    AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(albedo)) != "ecaa54c6a4d512f41ae9b8dc55b292ac")
                    throw new InvalidOperationException("Original Cathedral source/albedo identity missing");
                if (cathedral != null && !cathedral.GetComponentsInChildren<MeshFilter>(true).Select(m => m.sharedMesh).SequenceEqual(
                        original.GetComponentsInChildren<MeshFilter>(true).Select(m => m.sharedMesh)))
                    throw new InvalidOperationException("Existing Cathedral does not retain original source meshes");
                var poses = existing.Values.SelectMany(g => g.GetComponentsInChildren<Transform>(true)).Select(t => new Pose(t)).ToArray();
                var meshes = existing.Values.SelectMany(g => g.GetComponentsInChildren<MeshFilter>(true)).ToDictionary(m => m, m => m.sharedMesh);
                var skins = existing.Values.SelectMany(g => g.GetComponentsInChildren<SkinnedMeshRenderer>(true)).ToDictionary(m => m, m => m.sharedMesh);
                var native = existing.Where(p => !p.Key.Tripo).SelectMany(p => p.Value.GetComponentsInChildren<Renderer>(true)).ToDictionary(r => r, r => r.sharedMaterials);
                foreach (var pair in existing)
                {
                    if (pair.Key.Legacy == null) continue;
                    Configure(pair.Value, pair.Key.Id, pair.Key.Legacy, pair.Key.Tripo, null);
                    if (pair.Key.Tripo) RepairMaterials(pair.Value, null, copies);
                    ConfigureCapabilities(pair.Value, pair.Key.Id);
                }
                if (cathedral == null)
                {
                    cathedral = new GameObject(Cathedral); cathedral.transform.SetParent(ring, false);
                    cathedral.transform.position = new Vector3(-8f, 0f, 8.5f);
                    var options = SkinOptions.Structure(0f); options.FitHeight = StructureFactory.YHeightVariable;
                    options.LocalRotation = Quaternion.Euler(-90f, 0f, 0f);
                    var visual = VisualFactory.Skin(cathedral.transform, original, options);
                    if (visual == null) throw new InvalidOperationException("Cathedral skin failed");
                    visual.transform.position += Vector3.down * BoundsOf(visual).min.y;
                }
                Configure(cathedral, "arcane-tower", Cathedral, true, albedo);
                ConfigureCapabilities(cathedral, "arcane-tower");
                // Retain the obsolete door for recovery, but the authored ring is the sole active store.
                foreach (var old in scene.GetRootGameObjects().Where(g => g.name == "RealmStore_Storefront" && g.GetComponent<AuthoredCastleStorefront>() == null))
                    old.SetActive(false);
                var vendors = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<RealmStoreVendor>(true))
                    .Where(v => v.enabled && v.gameObject.activeInHierarchy).ToArray();
                if (vendors.Length != 1 || vendors[0].gameObject != existing.Single(p => p.Key.Name == "RealmStore").Value)
                    throw new InvalidOperationException("Expected one active vendor on authored RealmStore; got " + vendors.Length);
                // Skin adds its fixer to the visual child. Keep one root policy owner.
                foreach (var childFixer in cathedral.GetComponentsInChildren<TripoMaterialFixer>(true))
                    if (childFixer.gameObject != cathedral) Object.DestroyImmediate(childFixer);
                RepairMaterials(cathedral, albedo, copies);
                var cathedralBounds = BoundsOf(cathedral);
                foreach (var root in existing.Values)
                {
                    var b = BoundsOf(root);
                    if (cathedralBounds.min.x < b.max.x && cathedralBounds.max.x > b.min.x && cathedralBounds.min.z < b.max.z && cathedralBounds.max.z > b.min.z)
                        throw new InvalidOperationException("Cathedral footprint overlaps " + root.name);
                }
                foreach (var pose in poses) pose.Assert();
                if (meshes.Any(p => p.Key.sharedMesh != p.Value) || skins.Any(p => p.Key.sharedMesh != p.Value) || native.Any(p => !p.Key.sharedMaterials.SequenceEqual(p.Value)))
                    throw new InvalidOperationException("Original meshes/native materials changed");
                evidence.Add("PRESERVED originalRoots=" + existing.Count + " all descendant TRS/hierarchy/meshes exact; native Quarry/KayKit/IronMine materials unchanged");
                evidence.Add("CATHEDRAL world=" + cathedral.transform.position + " bounds=" + cathedralBounds + " albedo=" + AssetDatabase.GetAssetPath(albedo));
                evidence.Add("CAPABILITIES markers=11 activeRealmStoreVendors=" + vendors.Length + "; obsolete unmarked root disabled, recoverable");
                foreach (var root in existing.Values.Concat(new[] { cathedral }))
                    OwnerCastleLayoutAudit.Capture(root, scene, "Owner20260913_candidate_" + root.name, evidence);
                OwnerCastleLayoutAudit.Capture(ring.gameObject, scene, "Owner20260913_candidate_overview", evidence, true);
                OwnerCastleLayoutAudit.Capture(ring.gameObject, scene, "Owner20260913_candidate_context", evidence, true, true);
                if (save)
                {
                    string stamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff");
                    Directory.CreateDirectory(OwnerCastleLayoutAudit.Output);
                    File.Copy(OwnerCastleLayoutAudit.ScenePath, Path.Combine(OwnerCastleLayoutAudit.Output, "before_apply_" + stamp + ".unity"));
                    File.Copy(OwnerCastleLayoutAudit.ScenePath + ".meta", Path.Combine(OwnerCastleLayoutAudit.Output, "before_apply_" + stamp + ".unity.meta"));
                    if (File.Exists(LayoutPrefab)) File.Copy(LayoutPrefab, Path.Combine(OwnerCastleLayoutAudit.Output, "before_apply_" + stamp + ".prefab"));
                    EnsureFolder(MaterialRoot); string folder = MaterialRoot + "/" + stamp; EnsureFolder(folder);
                    for (int i = 0; i < copies.Count; i++)
                    {
                        copies[i].name = "OwnerCastle_" + i.ToString("00");
                        AssetDatabase.CreateAsset(copies[i], folder + "/" + copies[i].name + ".mat");
                        AssetDatabase.SaveAssetIfDirty(copies[i]);
                    }
                    foreach (var pose in poses) pose.Assert();
                    if (!EditorSceneManager.SaveScene(scene, OwnerCastleLayoutAudit.ScenePath)) throw new InvalidOperationException("Target scene save refused");
                    saved = true;
                    var recipe = PrefabUtility.SaveAsPrefabAsset(ring.gameObject, LayoutPrefab);
                    if (recipe == null) throw new InvalidOperationException("Owner layout prefab save failed");
                    if (recipe.transform.childCount != ring.childCount) throw new InvalidOperationException("Layout recipe child count mismatch");
                    foreach (Transform child in ring)
                    {
                        var exported = recipe.transform.Find(child.name);
                        if (exported == null || !exported.localPosition.Equals(child.localPosition) || !exported.localRotation.Equals(child.localRotation) || !exported.localScale.Equals(child.localScale))
                            throw new InvalidOperationException("Layout recipe pose mismatch: " + child.name);
                        if (!exported.GetComponentsInChildren<MeshFilter>(true).Select(m => m.sharedMesh).SequenceEqual(child.GetComponentsInChildren<MeshFilter>(true).Select(m => m.sharedMesh)))
                            throw new InvalidOperationException("Layout recipe mesh mismatch: " + child.name);
                        var sourceTransforms = child.GetComponentsInChildren<Transform>(true);
                        var exportedTransforms = exported.GetComponentsInChildren<Transform>(true);
                        if (sourceTransforms.Length != exportedTransforms.Length || sourceTransforms.Where((t, i) =>
                            t.name != exportedTransforms[i].name || !t.localPosition.Equals(exportedTransforms[i].localPosition) ||
                            !t.localRotation.Equals(exportedTransforms[i].localRotation) || !t.localScale.Equals(exportedTransforms[i].localScale)).Any())
                            throw new InvalidOperationException("Layout recipe descendant pose mismatch: " + child.name);
                        var identity = child.GetComponent<AuthoredCastleStorefront>();
                        var exportedIdentity = exported.GetComponent<AuthoredCastleStorefront>();
                        if (identity != null && (exportedIdentity == null || JsonUtility.ToJson(identity) != JsonUtility.ToJson(exportedIdentity)))
                            throw new InvalidOperationException("Layout recipe identity mismatch: " + child.name);
                    }
                    evidence.Add("SAVED scene=" + OwnerCastleLayoutAudit.ScenePath + " newMaterials=" + copies.Count + " folder=" + folder);
                    evidence.Add("RECIPE " + LayoutPrefab + " children=" + recipe.transform.childCount + " source meshes/root TRS verified");
                }
                File.WriteAllLines(Path.Combine(OwnerCastleLayoutAudit.Output, "Owner20260913_" + mode + "_evidence.txt"), evidence);
                Debug.Log("OWNER_CASTLE_LAYOUT_" + mode + "_OK preserved=" + existing.Count + " cathedral=1 saved=" + saved + "; inspect candidate images");
            }
            catch (Exception ex) { Debug.LogError("OWNER_CASTLE_LAYOUT_" + mode + "_FAIL saved=" + saved + ": " + ex.GetBaseException().Message + "; inspect any new material output before retry"); }
            finally
            {
                if (opened)
                {
                    if (empty) EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                    else EditorSceneManager.RestoreSceneManagerSetup(setup);
                }
                foreach (var material in copies) if (material != null && !AssetDatabase.Contains(material)) Object.DestroyImmediate(material);
            }
        }

        static void Configure(GameObject host, string id, string legacy, bool tripo, Texture2D albedo)
        {
            var marker = host.GetComponent<AuthoredCastleStorefront>();
            if (marker == null) marker = host.AddComponent<AuthoredCastleStorefront>();
            marker.Configure(id, legacy); marker.SetMaterialPolicy(tripo, albedo);
            EditorUtility.SetDirty(marker); PrefabUtility.RecordPrefabInstancePropertyModifications(marker);
        }
        static void ConfigureCapabilities(GameObject host, string id)
        {
            if (string.IsNullOrEmpty(id))
            {
                if (host.GetComponent<RealmStoreVendor>() == null) host.AddComponent<RealmStoreVendor>();
                if (host.GetComponent<RealmStoreBeacon>() == null) host.AddComponent<RealmStoreBeacon>();
            }
            else if (id.StartsWith("collector_", StringComparison.Ordinal))
            {
                // A producer is not the weapons vendor. Retire the quarry prefab's old crystal
                // Building capability while keeping its geometry, materials, and collider intact.
                var oldInteraction = host.GetComponent<BuildingInteractable>();
                if (oldInteraction != null) Object.DestroyImmediate(oldInteraction);
                var oldBuilding = host.GetComponent<Building>();
                if (oldBuilding != null) Object.DestroyImmediate(oldBuilding);
                string buildingId = id == "collector_farm" ? "farm" : id == "collector_lumbermill" ? "lumbermill" : id == "collector_forge" ? "forge" : null;
                if (buildingId == null) throw new InvalidOperationException("Unknown collector identity " + id);
                var collector = host.GetComponent<ResourceCollector>();
                if (collector == null) collector = host.AddComponent<ResourceCollector>();
                var data = new SerializedObject(collector);
                data.FindProperty("_buildingId").stringValue = buildingId;
                data.ApplyModifiedPropertiesWithoutUndo();
                var expected = buildingId == "farm" ? HarvestResource.Stone : buildingId == "lumbermill" ? HarvestResource.Wood : HarvestResource.Iron;
                if (collector.BuildingId != buildingId || collector.Resource != expected)
                    throw new InvalidOperationException("Wrong collector capability: " + host.name);
                PrefabUtility.RecordPrefabInstancePropertyModifications(collector);
            }
            else
            {
                var type = StructureFactory.BuildingTypeForId(id);
                var building = host.GetComponent<Building>();
                if (building == null) building = host.AddComponent<Building>();
                var serialized = new SerializedObject(building);
                serialized.FindProperty("_type").intValue = (int)type;
                serialized.FindProperty("_buildingId").stringValue = id;
                serialized.FindProperty("_displayLabel").stringValue = host.name == Cathedral ? "Cathedral of Learning" : host.name.Replace('_', ' ');
                serialized.FindProperty("_displayNameKey").stringValue = "";
                serialized.ApplyModifiedPropertiesWithoutUndo();
                if (host.GetComponent<BuildingInteractable>() == null) host.AddComponent<BuildingInteractable>();
                if (building.BuildingId != id || building.Type != type)
                    throw new InvalidOperationException("Building identity not applied: " + host.name);
                PrefabUtility.RecordPrefabInstancePropertyModifications(building);
            }
            if (host.GetComponentsInChildren<Collider>(true).Any(c => c.enabled && !c.isTrigger)) return;
            Bounds local = default; bool any = false;
            foreach (var filter in host.GetComponentsInChildren<MeshFilter>(true))
            {
                if (filter.sharedMesh == null) continue;
                var bounds = filter.sharedMesh.bounds;
                for (int corner = 0; corner < 8; corner++)
                {
                    var point = bounds.center + Vector3.Scale(bounds.extents, new Vector3((corner & 1) == 0 ? -1 : 1, (corner & 2) == 0 ? -1 : 1, (corner & 4) == 0 ? -1 : 1));
                    point = host.transform.InverseTransformPoint(filter.transform.TransformPoint(point));
                    if (!any) { local = new Bounds(point, Vector3.zero); any = true; } else local.Encapsulate(point);
                }
            }
            if (!any) throw new InvalidOperationException("Cannot fit collider without mesh: " + host.name);
            var box = host.AddComponent<BoxCollider>(); box.center = local.center; box.size = local.size;
            PrefabUtility.RecordPrefabInstancePropertyModifications(box);
        }
        static void RepairMaterials(GameObject host, Texture2D albedo, List<Material> copies)
        {
            var fixer = host.GetComponent<TripoMaterialFixer>();
            if (fixer == null) fixer = host.AddComponent<TripoMaterialFixer>();
            fixer.ForceRebuildAll(); if (albedo != null) fixer.SetForcedSourceTexture(albedo);
            typeof(TripoMaterialFixer).GetField("_ran", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(fixer, false);
            typeof(TripoMaterialFixer).GetMethod("Run", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(fixer, null);
            foreach (var renderer in host.GetComponentsInChildren<Renderer>(true))
            {
                var materials = renderer.sharedMaterials;
                for (int i = 0; i < materials.Length; i++)
                {
                    var source = materials[i];
                    if (source == null || source.shader == null || source.shader.name != "Universal Render Pipeline/Lit" || !source.HasProperty("_BaseMap") || source.GetTexture("_BaseMap") == null)
                        throw new InvalidOperationException("Tripo material lacks URP/albedo: " + host.name);
                    if (albedo != null && source.GetTexture("_BaseMap") != albedo) throw new InvalidOperationException("Cathedral forced albedo failed");
                    materials[i] = new Material(source); copies.Add(materials[i]);
                }
                renderer.sharedMaterials = materials;
                EditorUtility.SetDirty(renderer); PrefabUtility.RecordPrefabInstancePropertyModifications(renderer);
            }
            EditorUtility.SetDirty(fixer); PrefabUtility.RecordPrefabInstancePropertyModifications(fixer);
        }
        static Bounds BoundsOf(GameObject host)
        {
            var renderers = host.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0) throw new InvalidOperationException("No geometry: " + host.name);
            var bounds = renderers[0].bounds; foreach (var renderer in renderers.Skip(1)) bounds.Encapsulate(renderer.bounds); return bounds;
        }
        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = path.Substring(0, path.LastIndexOf('/')); EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, path.Substring(path.LastIndexOf('/') + 1));
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using DeNelle.Core;
using DeNelle.Village;
using DeNelle.Village.Buildings.Progression;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DeNelle.Editor
{
    // Actual edit-mode entry points; no saved scene/asset writes. This is not a Play Mode claim.
    public static class OwnerCastleRuntimeProof
    {
        const BindingFlags PrivateStatic = BindingFlags.NonPublic | BindingFlags.Static;
        const BindingFlags PrivateInstance = BindingFlags.NonPublic | BindingFlags.Instance;
        static void Require(bool ok, string detail) { if (!ok) throw new InvalidOperationException(detail); }
        public static void Run()
        {
            var setup = EditorSceneManager.GetSceneManagerSetup();
            var evidence = new List<string>(); var failures = new List<string>();
            var files = new Dictionary<string, string>(StringComparer.Ordinal);
            bool opened = false, empty = false;
            try
            {
                Require(!Application.isPlaying, "Edit mode only");
                for (int i = 0; i < SceneManager.sceneCount; i++)
                {
                    var current = SceneManager.GetSceneAt(i); Require(!current.isDirty, "Dirty scene refusal: " + current.path);
                    if (string.IsNullOrEmpty(current.path))
                    {
                        Require(Application.isBatchMode && SceneManager.sceneCount == 1 && current.rootCount == 0, "Untitled scene refusal"); empty = true;
                    }
                }
                opened = true;
                var scene = EditorSceneManager.OpenScene(OwnerCastleLayoutAudit.ScenePath, OpenSceneMode.Single);
                var ring = FindRing(scene); var recipe = AssetDatabase.LoadAssetAtPath<GameObject>(OwnerCastleLayoutRepair.LayoutPrefab);
                // WO-1762: DERIVED FROM THE RECIPE, not a literal. This line read `ring.childCount == 14`
                // and `CheckCapabilities` read `== 3` props; both were the count BEFORE the owner's
                // 2026-09-15 ruling removed the three scenery pallets, and both would have gone red the
                // moment HubRingHeightApply.Run landed - a proof failing because the thing it proves
                // changed correctly. The recipe prefab is the authority the scene is compared against two
                // lines down (Compare), so reading the expectation off it cannot go stale the way a
                // number typed here does (CLAUDE.md sections 2/5/8 all describe this same failure).
                // The FLOOR stays hard: eleven markers, asserted in CheckCapabilities.
                Require(recipe != null, "Recipe absent");
                int expectedRingChildren = recipe.transform.childCount;
                int expectedProps = recipe.transform.Cast<Transform>()
                    .Count(t => t.GetComponent<AuthoredCastleStorefront>() == null);
                Require(ring.childCount == expectedRingChildren,
                    "Scene ring has " + ring.childCount + " children; the recipe authors " + expectedRingChildren);
                foreach (string path in new[] { OwnerCastleLayoutAudit.ScenePath, OwnerCastleLayoutRepair.LayoutPrefab }) Preserve(path, files);
                foreach (var renderer in ring.GetComponentsInChildren<Renderer>(true))
                    foreach (var material in renderer.sharedMaterials.Where(m => m != null))
                    {
                        Preserve(AssetDatabase.GetAssetPath(material), files);
                        foreach (string property in material.GetTexturePropertyNames()) Preserve(AssetDatabase.GetAssetPath(material.GetTexture(property)), files);
                    }
                foreach (var mesh in ring.GetComponentsInChildren<MeshFilter>(true)) Preserve(AssetDatabase.GetAssetPath(mesh.sharedMesh), files);
                Compare(ring, recipe.transform); CheckCapabilities(ring, evidence, expectedProps);
                var roles = (ValueTuple<string, string>[])typeof(CastleVendorNpcInjector).GetField("AnchorRoles", PrivateStatic).GetValue(null);
                Require(roles.Count(r => r.Item2 == "forge") == 1 && roles.Single(r => r.Item2 == "forge").Item1 == "Forge" &&
                    !roles.Any(r => r.Item2 == "collector_forge"), "Weapons vendor must belong to Weaponsmith, never IronMine");
                var vendorFor = typeof(CastleVendorNpcInjector).GetMethod("VendorFor", PrivateStatic);
                foreach (var trade in new[] { ("Forge", "forge"), ("Blacksmith", "armorer") })
                {
                    var vendor = vendorFor.Invoke(null, new object[] { trade.Item1 });
                    Require((string)vendor.GetType().GetField("StructureId").GetValue(vendor) == trade.Item2, "Trade routing mismatch " + trade.Item1);
                }
                evidence.Add("VENDORS weapons->forge/Weaponsmith; armor->armorer; IronMine excluded from weapons vendor anchoring");
                Require(ring.GetComponent<Collider>() == null && ring.GetComponent<Renderer>() == null && ring.GetComponent<MeshFilter>() == null &&
                    recipe.GetComponent<Collider>() == null && recipe.GetComponent<Renderer>() == null && recipe.GetComponent<MeshFilter>() == null,
                    "Empty group regained orphan geometry/collider");
                evidence.Add("GROUP_PHYSICS empty grouping root has no collider or renderer; actual building colliders retained");
                CheckAddresses(evidence);
                var geometry = ring.GetComponentsInChildren<Transform>(true).ToDictionary(t => t, Geometry);
                var sourceMeshes = ring.GetComponentsInChildren<MeshFilter>(true).ToDictionary(m => m, m => m.sharedMesh);
                var native = ring.GetComponentsInChildren<AuthoredCastleStorefront>(true).Where(a => !a.RepairTripoMaterials)
                    .SelectMany(a => a.GetComponentsInChildren<Renderer>(true)).ToDictionary(r => r, r => r.sharedMaterials);
                var swaps = ((Array)typeof(HubStructureVisualInjector).GetField("Swaps", PrivateStatic).GetValue(null)).Cast<object>().ToArray();
                var skin = typeof(HubStructureVisualInjector).GetMethod("SkinStorefront", PrivateStatic);
                int calls = 0;
                for (int pass = 0; pass < 2; pass++)
                    foreach (var authored in ring.GetComponentsInChildren<AuthoredCastleStorefront>(true))
                    {
                        HubStructureVisualInjector.PrepareAuthoredStorefront(authored);
                        var swap = swaps.SingleOrDefault(s => (string)s.GetType().GetField("bakedName").GetValue(s) == authored.LegacyName);
                        if (swap != null) { skin.Invoke(null, new[] { swap, authored.transform }); calls++; }
                        if (authored.RepairTripoMaterials)
                        {
                            var fixer = authored.GetComponent<TripoMaterialFixer>(); Require(fixer != null, "Missing fixer " + authored.name);
                            typeof(TripoMaterialFixer).GetField("_ran", PrivateInstance).SetValue(fixer, false);
                            typeof(TripoMaterialFixer).GetMethod("Run", PrivateInstance).Invoke(fixer, null);
                            foreach (var renderer in authored.GetComponentsInChildren<Renderer>(true))
                                foreach (var material in renderer.sharedMaterials)
                                {
                                    Require(material != null && material.shader.name == "Universal Render Pipeline/Lit" && material.GetTexture("_BaseMap") != null, "Missing repaired albedo " + authored.name);
                                    if (authored.ForcedAlbedo != null) Require(material.GetTexture("_BaseMap") == authored.ForcedAlbedo, "Forced Cathedral albedo changed");
                                }
                        }
                    }
                Require(calls == 16, "Expected eight matching original swaps twice; got " + calls);
                Require(geometry.Count == ring.GetComponentsInChildren<Transform>(true).Length && geometry.All(p => p.Key != null && Geometry(p.Key) == p.Value), "Injector changed geometry/pose/hierarchy");
                Require(sourceMeshes.All(p => p.Key.sharedMesh == p.Value) && native.All(p => p.Key.sharedMaterials.SequenceEqual(p.Value)), "Injector changed meshes/native materials");
                Require(!ring.GetComponentsInChildren<Transform>(true).Any(t => t.name.StartsWith("LightSkin_", StringComparison.Ordinal)), "Injector added LightSkin clone");
                evidence.Add("INJECTOR twice calls=" + calls + " PrepareAuthored=22; geometry/poses/native colors unchanged, real fixer albedos verified");
                var workshop = ring.GetComponentsInChildren<Building>(true).Single(b => b.BuildingId == "workshop");
                var panelArgs = new object[] { workshop, default(DeNelle.Core.UI.PanelId) };
                Require((bool)typeof(BuildingInteractable).GetMethod("TryPanelFor", PrivateStatic).Invoke(null, panelArgs) && panelArgs[1].ToString() == "Crafting", "Workshop panel route");
                int roots = scene.rootCount; bool refused = false;
                try { CastleHubBuilder.BuildCastleHub(); } catch (InvalidOperationException) { refused = true; }
                Require(refused && scene.rootCount == roots && files[OwnerCastleLayoutAudit.ScenePath] == Digest(OwnerCastleLayoutAudit.ScenePath), "Builder did not preserve existing scene");
                OwnerCastleLayoutAudit.Capture(ring.gameObject, scene, "Owner20260913_final_context", evidence, true, true);
                CheckLookup(evidence);
                var fresh = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                try
                {
                    SceneManager.SetActiveScene(fresh); CastleHubBuilder.BuildCastleHub();
                    Require(SceneManager.GetActiveScene() == fresh && string.IsNullOrEmpty(fresh.path), "Builder opened/saved unexpected scene");
                    var generated = FindRing(fresh); Compare(generated, recipe.transform); CheckCapabilities(generated, evidence, expectedProps);
                    Require(Mathf.Abs(generated.position.y - recipe.transform.position.y - CastleHubBuilder.CastleFootprintLiftY) < .0001f, "Unexpected generated root lift");
                    evidence.Add("NEW_EMPTY_BUILDER " + expectedRingChildren + " recipe children/relative poses/capabilities match; lift=" + CastleHubBuilder.CastleFootprintLiftY + "; unsaved scene");
                }
                finally { EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single); }
            }
            catch (Exception ex) { failures.Add(ex.GetBaseException().Message); }
            finally
            {
                if (opened)
                {
                    try { if (empty) EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single); else EditorSceneManager.RestoreSceneManagerSetup(setup); }
                    catch (Exception ex) { failures.Add("Restore failed: " + ex.Message); }
                }
                foreach (var pair in files) if (!File.Exists(pair.Key) || Digest(pair.Key) != pair.Value) failures.Add("Source bytes changed: " + pair.Key);
            }
            Directory.CreateDirectory(OwnerCastleLayoutAudit.Output);
            evidence.AddRange(failures.Select(f => "FAIL " + f)); evidence.Add("Edit-mode structural proof only; no full Play Mode claim.");
            File.WriteAllLines(Path.Combine(OwnerCastleLayoutAudit.Output, "Owner20260913_runtime_evidence.txt"), evidence);
            if (failures.Count > 0) Debug.LogError("OWNER_CASTLE_RUNTIME_FAIL " + string.Join("; ", failures));
            else Debug.Log("OWNER_CASTLE_RUNTIME_OK scene/prefab/injector/materials/capabilities/lookup/builders verified; source bytes unchanged; edit-mode proof only");
        }
        static Transform FindRing(Scene scene) => scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Transform>(true)).Single(t => t.name == OwnerCastleLayoutAudit.RingName);
        static string Geometry(Transform t) => t.GetInstanceID() + "/" + (t.parent == null ? 0 : t.parent.GetInstanceID()) + "/" + Local(t) + "/" + t.position.ToString("R") + "/" + t.rotation.ToString("R") + "/" + t.lossyScale.ToString("R") + "/" + t.childCount;
        static string Local(Transform t) => t.name + "/" + t.localPosition.ToString("R") + "/" + t.localRotation.ToString("R") + "/" + t.localScale.ToString("R");
        static void Compare(Transform actual, Transform reference)
        {
            var a = actual.GetComponentsInChildren<Transform>(true); var b = reference.GetComponentsInChildren<Transform>(true);
            Require(a.Length == b.Length, "Scene/recipe transform counts differ: " + a.Length + "/" + b.Length);
            // SaveAsPrefabAsset gives the recipe root its asset filename. Descendant names
            // and every transform remain authoritative; only that container label differs.
            Require(actual.localPosition == reference.localPosition && actual.localRotation == reference.localRotation &&
                actual.localScale == reference.localScale, "Scene/recipe container TRS differs");
            for (int i = 1; i < a.Length; i++)
                Require(Local(a[i]) == Local(b[i]), "Scene/recipe descendant differs at " + i + ": " + Local(a[i]) + " versus " + Local(b[i]));
            Require(actual.GetComponentsInChildren<MeshFilter>(true).Select(m => m.sharedMesh).SequenceEqual(reference.GetComponentsInChildren<MeshFilter>(true).Select(m => m.sharedMesh)), "Scene/recipe meshes differ");
            var aa = actual.GetComponentsInChildren<AuthoredCastleStorefront>(true); var bb = reference.GetComponentsInChildren<AuthoredCastleStorefront>(true);
            Require(aa.Length == bb.Length && aa.Where((m, i) => JsonUtility.ToJson(m) != JsonUtility.ToJson(bb[i])).Count() == 0, "Scene/recipe marker policies differ");
        }
        // WO-1762: expectedProps is DERIVED from the recipe by the caller, never typed here. It was
        // the literal 3 (the three scenery pallets) until the owner ruled them out on 2026-09-15.
        // The ELEVEN marker floor below stays a literal on purpose: that is the authored identity
        // set, and it is not what the pallet removal changes.
        static void CheckCapabilities(Transform ring, List<string> evidence, int expectedProps)
        {
            var markers = ring.GetComponentsInChildren<AuthoredCastleStorefront>(true); Require(markers.Length == 11, "Expected eleven markers");
            var ids = markers.Where(m => !string.IsNullOrEmpty(m.CanonicalId)).Select(m => m.CanonicalId).ToArray();
            Require(ids.Length == 10 && ids.Distinct(StringComparer.Ordinal).Count() == 10, "Nonempty identities not unique");
            int props = ring.Cast<Transform>().Count(t => t.GetComponent<AuthoredCastleStorefront>() == null);
            Require(props == expectedProps, "Scene ring has " + props + " unmarked prop(s); the recipe authors " + expectedProps);
            foreach (var marker in markers)
            {
                if (marker.CanonicalId.StartsWith("collector_", StringComparison.Ordinal))
                {
                    var c = marker.GetComponent<ResourceCollector>(); var id = marker.CanonicalId == "collector_farm" ? "farm" : marker.CanonicalId == "collector_lumbermill" ? "lumbermill" : "forge";
                    Require(c != null && c.BuildingId == id && c.Resource == (id == "farm" ? HarvestResource.Stone : id == "lumbermill" ? HarvestResource.Wood : HarvestResource.Iron), "Collector output mismatch " + marker.name);
                    Require(marker.GetComponent<Building>() == null && marker.GetComponent<BuildingInteractable>() == null, "Producer has trading/crystal Building capability");
                }
                else if (!string.IsNullOrEmpty(marker.CanonicalId))
                {
                    var b = marker.GetComponent<Building>(); Require(b != null && b.BuildingId == marker.CanonicalId && b.Type == StructureFactory.BuildingTypeForId(marker.CanonicalId) && marker.GetComponent<BuildingInteractable>() != null, "Building capability mismatch " + marker.name);
                }
                else Require(marker.GetComponent<RealmStoreVendor>() != null && marker.GetComponent<RealmStoreBeacon>() != null, "Missing store capabilities");
            }
            var all = ring.gameObject.scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<RealmStoreVendor>(true));
            Require(all.Count(v => v.enabled && v.gameObject.activeInHierarchy) == 1, "Active store vendor count");
            var cathedral = markers.Single(m => m.CanonicalId == "arcane-tower");
            Require(cathedral.ForcedAlbedo != null && AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(cathedral.ForcedAlbedo)) == "ecaa54c6a4d512f41ae9b8dc55b292ac", "Cathedral texture identity");
            evidence.Add("CAPABILITIES eleven markers/ten unique ids/" + expectedProps + " props (recipe-derived); three typed producers; one store; factory types correct");
        }
        static void CheckAddresses(List<string> evidence)
        {
            var originals = (Dictionary<string, string>)typeof(SyntyStructureRetheme).GetField("OriginalStorefrontGuids", PrivateStatic).GetValue(null);
            var settings = AddressableAssetSettingsDefaultObject.Settings; Require(settings != null, "Addressable settings missing");
            foreach (var pair in originals)
            {
                var entries = settings.groups.Where(g => g != null).SelectMany(g => g.entries).Where(e => e.address == "Structures/" + pair.Key).ToArray();
                Require(entries.Length == 1 && entries[0].guid == pair.Value && AssetDatabase.GUIDToAssetPath(pair.Value) == AssetRoots.StructureContent + "/" + pair.Key + ".fbx", "Original address mismatch " + pair.Key);
            }
            evidence.Add("ADDRESSES nine protected original FBX identities resolve uniquely");
        }
        static void CheckLookup(List<string> evidence)
        {
            var fixture = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            try
            {
                SceneManager.SetActiveScene(fixture);
                var host = new GameObject("AuthoredLookupFixture"); var marker = host.AddComponent<AuthoredCastleStorefront>(); marker.Configure("workshop", "LegacyLookupFixture");
                var old = new GameObject("LegacyLookupFixture");
                Require(AuthoredCastleStorefront.Find(old.name) == host.transform, "Marked lookup did not outrank legacy");
                host.SetActive(false); Require(AuthoredCastleStorefront.Find(old.name) == null && AuthoredCastleStorefront.Find(old.name, true) == host.transform, "Inactive ownership not preserved");
                var duplicate = new GameObject("DuplicateLookupFixture").AddComponent<AuthoredCastleStorefront>(); duplicate.Configure("workshop", old.name);
                Require(AuthoredCastleStorefront.Find(old.name, true) == null, "Duplicate ownership not rejected");
                evidence.Add("LOOKUP marked wins; inactive retained; ambiguous markers rejected (expected diagnostic)");
            }
            finally { EditorSceneManager.CloseScene(fixture, true); }
        }
        static void Preserve(string path, Dictionary<string, string> files)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;
            if (!files.ContainsKey(path)) files[path] = Digest(path);
            if (File.Exists(path + ".meta") && !files.ContainsKey(path + ".meta")) files[path + ".meta"] = Digest(path + ".meta");
        }
        static string Digest(string path) { using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path))); }
    }
}

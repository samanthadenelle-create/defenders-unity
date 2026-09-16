using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using DeNelle.Core.Catalog;
using DeNelle.Core.Economy;
using DeNelle.Core.State;
using DeNelle.Village;
using Newtonsoft.Json;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace DeNelle.Editor
{
    // Edit-mode production seam proof. Start/StorageStackView fill and cold CDN delivery
    // are deliberately outside this proof; measurements select only the authored body.
    public static class StoragePalletPlacementProof
    {
        private static readonly BindingFlags PrivateStatic = BindingFlags.NonPublic | BindingFlags.Static;
        public static void Run()
        {
            if (Application.isPlaying || GameStateService.Instance != null || PlacementGrid.Instance != null)
                throw new InvalidOperationException("Run in a fresh edit-mode batch with no live state/grid services.");
            var failures = new List<string>();
            var evidence = new List<string>();
            var active = SceneManager.GetActiveScene();
            bool ownsScene = !string.IsNullOrEmpty(active.path);
            if (!ownsScene && (active.isDirty || active.rootCount != 0))
                throw new InvalidOperationException("Requires an empty batch scene or saved scene; no unsaved work is discarded.");
            var scene = ownsScene ? EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive) : active;
            SceneManager.SetActiveScene(scene);
            bool seeded = CatalogRegistry.Count == 0;
            GameState state = null;
            var instance = typeof(GameStateService).GetField("_instance", PrivateStatic);
            var priorInstance = instance.GetValue(null);
            var isolation = new MemorySave();
            try
            {
                if (seeded) typeof(CatalogBootstrap).GetMethod("Register", PrivateStatic).Invoke(null, null);
                state = ScriptableObject.CreateInstance<GameState>();
                var serviceHost = new GameObject("StorageProofState");
                serviceHost.SetActive(false); // Never run Awake/Load/identity startup against user prefs.
                var service = serviceHost.AddComponent<GameStateService>();
                typeof(GameStateService).GetField("_state", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(service, state);
                instance.SetValue(null, service);
                var grid = new GameObject("StorageProofGrid").AddComponent<PlacementGrid>();
                var parent = new GameObject("StorageProofBodies").transform;
                var upgrade = typeof(BuildModeController).GetMethod("ApplyUpgradeLevel", PrivateStatic);
                foreach (var pair in new[] { ("lumberyard", "Wood_Pallet"), ("foundry", "Iron_Pallet"), ("silo", "Stone_Pallet") })
                {
                    var entry = CatalogRegistry.Get(pair.Item1);
                    Require(entry != null && TownBankCapacity.IsStorageContainer(entry.repo), pair.Item1 + " storage binding");
                    var reference = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Village/OwnerStorage/" + pair.Item2 + ".prefab");
                    Require(reference != null, pair.Item1 + " approved prefab");
                    var expected = Object.Instantiate(reference, parent);
                    var expectedBody = FindBody(expected, pair.Item2);
                    var targetSize = BoundsOf(expectedBody).size;
                    var targetMeshes = expectedBody.GetComponentsInChildren<MeshFilter>(true).Select(f => f.sharedMesh).ToArray();
                    var targetMaterials = expectedBody.GetComponentsInChildren<MeshRenderer>(true).SelectMany(r => r.sharedMaterials).ToArray();
                    var record = new PlacedStructureData { itemId = pair.Item1, cellX = 2, cellZ = 2, level = 1 };
                    state.BaseLayout = new List<PlacedStructureData> { record };
                    var placed = BaseLayoutLoader.SpawnForLayout(record, grid, parent, replayStoryServices: false);
                    Require(placed != null, pair.Item1 + " production creation");
                    CheckBody(placed, pair.Item2, "create-L1", targetSize, targetMeshes, targetMaterials, failures, evidence);
                    // An authored renderer property must survive tier Apply/Refresh, not be overwritten.
                    var body = FindBody(placed.gameObject, pair.Item2);
                    var sentinel = new Color(0.031f, 0.047f, 0.059f, 1f);
                    foreach (var renderer in body.GetComponentsInChildren<Renderer>(true))
                    {
                        var block = new MaterialPropertyBlock();
                        renderer.GetPropertyBlock(block); block.SetColor("_EmissionColor", sentinel); renderer.SetPropertyBlock(block);
                    }
                    for (int level = 2; level <= 6; level++)
                    {
                        upgrade.Invoke(null, new object[] { placed, level });
                        // Edit mode has no toast lifetime Update; retire its temporary UI now.
                        foreach (var toast in scene.GetRootGameObjects().Where(go => go.name == "BuildFeedbackToast"))
                            Object.DestroyImmediate(toast);
                        CheckBody(placed, pair.Item2, "upgrade-L" + level, targetSize, targetMeshes, targetMaterials, failures, evidence);
                        foreach (var renderer in body.GetComponentsInChildren<Renderer>(true))
                        {
                            var block = new MaterialPropertyBlock(); renderer.GetPropertyBlock(block);
                            Check(block.GetColor("_EmissionColor") == sentinel, pair.Item1 + " upgrade-L" + level + " preserves authored emission", failures);
                        }
                    }
                    Require(state.BaseLayout.Single().level == 6, pair.Item1 + " upgrade commits layout L6");
                    Require(service.TrySave(out var reason), "memory save: " + reason);
                    var json = SaveSchema.TryExtractSigned(isolation.Read(SaveSchema.PlayerPrefsKey), out var signed, out var valid);
                    Require(signed && valid, "signed fixture save");
                    var saved = JsonConvert.DeserializeObject<SaveSchema.SaveFile>(json, SaveSchema.JsonSettings);
                    var replay = saved.State.BaseLayout.Single();
                    Require(replay.level == 6 && replay.itemId == pair.Item1, "saved layout identity/level");
                    Object.DestroyImmediate(placed.gameObject);
                    // Real persisted record through the same loader used at startup; no user Load lifecycle.
                    placed = BaseLayoutLoader.SpawnForLayout(replay, grid, parent, replayStoryServices: false);
                    Require(placed != null && placed.level == 6, "reload L6");
                    CheckBody(placed, pair.Item2, "saved-reload-L6", targetSize, targetMeshes, targetMaterials, failures, evidence);
                    foreach (var renderer in FindBody(placed.gameObject, pair.Item2).GetComponentsInChildren<Renderer>(true))
                    {
                        var block = new MaterialPropertyBlock(); renderer.GetPropertyBlock(block);
                        Check(!block.HasColor(Shader.PropertyToID("_EmissionColor")), pair.Item1 + " reload has no tier emission override", failures);
                    }
                    Object.DestroyImmediate(placed.gameObject); Object.DestroyImmediate(expected);
                }
                // Existing fallback remains active for a real nonstorage catalog identity.
                var controlEntry = CatalogRegistry.All().First(e => !TownBankCapacity.IsStorageContainer(e.repo));
                var control = GameObject.CreatePrimitive(PrimitiveType.Cube);
                control.name = "NonStorageTierControl";
                control.transform.localScale = new Vector3(0.7f, 1.3f, 0.9f);
                var controlBase = control.transform.localScale;
                control.AddComponent<PlacedStructure>().itemId = controlEntry.id;
                var tier = control.AddComponent<StructureTierVisual>();
                tier.Apply(1); tier.Apply(3); tier.Refresh();
                Check((control.transform.localScale - controlBase * 1.25f).sqrMagnitude < 0.000001f, "nonstorage retains L3 scaling", failures);
                var controlBlock = new MaterialPropertyBlock(); control.GetComponent<Renderer>().GetPropertyBlock(controlBlock);
                Check(controlBlock.GetColor("_EmissionColor").maxColorComponent > 0.1f, "nonstorage retains L3 accent", failures);
                Object.DestroyImmediate(control);
            }
            catch (Exception ex) { failures.Add(ex.ToString()); }
            finally
            {
                instance.SetValue(null, priorInstance);
                try
                {
                    // Both branches began empty: all roots in this scene belong to this proof,
                    // including temporary feedback spawned by the real upgrade API.
                    foreach (var root in scene.GetRootGameObjects()) Object.DestroyImmediate(root);
                    if (active.IsValid() && active.isLoaded) SceneManager.SetActiveScene(active);
                    if (ownsScene) EditorSceneManager.CloseScene(scene, true);
                    if (state != null) Object.DestroyImmediate(state);
                    if (seeded) CatalogRegistry.Clear();
                }
                finally
                {
                    try { isolation.Dispose(); } catch (Exception ex) { failures.Add(ex.Message); }
                }
            }
            Directory.CreateDirectory("Builds/castle-validation-20260913");
            File.WriteAllLines("Builds/castle-validation-20260913/storage-placement-proof.txt", evidence.Concat(failures));
            foreach (var failure in failures) Debug.LogError("STORAGE_PLACEMENT_CASE_FAIL " + failure);
            if (failures.Count == 0) Debug.Log("STORAGE_PLACEMENT_PROOF_OK three approved bodies creation/upgrade L2-L6/signed-save replay L6; nonstorage control; memory save restored. EditMode: Start/fill and CDN UNRUN.");
            else Debug.LogError("STORAGE_PLACEMENT_PROOF_FAIL failures=" + failures.Count);
        }

        private static GameObject FindBody(GameObject host, string name) => host.GetComponentsInChildren<Transform>(true).Single(t => t.name == name).gameObject;
        private static Bounds BoundsOf(GameObject body)
        {
            var renderers = body.GetComponentsInChildren<Renderer>(true);
            Require(renderers.Length > 0, "body has renderers");
            var bounds = renderers[0].bounds;
            foreach (var renderer in renderers) bounds.Encapsulate(renderer.bounds);
            return bounds;
        }
        private static void CheckBody(PlacedStructure placed, string name, string phase, Vector3 size, Mesh[] meshes, Material[] materials, List<string> failures, List<string> evidence)
        {
            var body = FindBody(placed.gameObject, name);
            var actual = BoundsOf(body).size;
            string label = placed.itemId + " " + phase;
            string line = label + " palletBody=" + actual.ToString("F6") + " approved=" + size.ToString("F6") + " rootScale=" + placed.transform.localScale.ToString("F6");
            evidence.Add(line); Debug.Log("STORAGE_PLACEMENT_MEASURE " + line);
            Check((actual - size).sqrMagnitude < 0.00001f, label + " approved size", failures);
            Check(body.GetComponentsInChildren<MeshFilter>(true).Select(f => f.sharedMesh).SequenceEqual(meshes), label + " approved meshes", failures);
            Check(body.GetComponentsInChildren<MeshRenderer>(true).SelectMany(r => r.sharedMaterials).SequenceEqual(materials), label + " approved materials", failures);
        }
        private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        private static void Check(bool condition, string message, List<string> failures) { if (!condition) failures.Add(message); }

        private sealed class MemorySave : ISaveProvider, IDisposable
        {
            private readonly ISaveProvider prior = GameStateService.Provider;
            private readonly Dictionary<string, string> slots = new Dictionary<string, string>();
            private readonly bool existed;
            private readonly string original;
            private readonly bool priorCloudSuppression = GameStateService.SuppressCloudForIsolatedProof;
            private int writes;
            public MemorySave()
            {
                existed = prior.Exists(SaveSchema.PlayerPrefsKey);
                original = existed ? prior.Read(SaveSchema.PlayerPrefsKey) : null;
                GameStateService.Provider = this;
                GameStateService.SuppressCloudForIsolatedProof = true;
            }
            public bool Exists(string slot) => slots.ContainsKey(slot);
            public string Read(string slot) => slots.TryGetValue(slot, out var value) ? value : null;
            public void Write(string slot, string value) { slots[slot] = value; writes++; }
            public void Delete(string slot) => slots.Remove(slot);
            public void Dispose()
            {
                GameStateService.Provider = prior;
                GameStateService.SuppressCloudForIsolatedProof = priorCloudSuppression;
                Require(prior.Exists(SaveSchema.PlayerPrefsKey) == existed && (!existed || prior.Read(SaveSchema.PlayerPrefsKey) == original), "STORAGE_PLACEMENT_SAVE_ISOLATION_FAIL original slot changed");
                Require(writes >= 3, "fixture must exercise three signed saves");
                Debug.Log("STORAGE_PLACEMENT_SAVE_ISOLATION_OK fixtureWrites=" + writes + " originalSlotUnchanged=True");
            }
        }
    }
}

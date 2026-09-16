using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using DeNelle.Core;
using DeNelle.Village;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace DeNelle.Editor
{
    // WO-1291 evidence instrument. Never saves scenes/assets or changes production policy.
    public static class StorefrontSyntyProbe
    {
        const string Output = "Builds/StorefrontSyntyProbe";
        const BindingFlags PrivateStatic = BindingFlags.NonPublic | BindingFlags.Static;
        [Serializable] sealed class Catalog { public Entry[] entries; }
        [Serializable] sealed class Entry { public string id; public string visualPrefabPath; public Repo repo; }
        [Serializable] sealed class Repo { public string[] bakedTwins; }

        public static void RunStandalone()
        {
            var setup = EditorSceneManager.GetSceneManagerSetup();
            bool empty = false, opened = false;
            var evidence = new List<string>();
            var failures = new List<string>();
            int count = 0, tilted = 0, repainted = 0;
            try
            {
                if (Application.isPlaying) throw new InvalidOperationException("Edit-mode probe only");
                for (int i = 0; i < SceneManager.sceneCount; i++)
                {
                    var scene = SceneManager.GetSceneAt(i);
                    if (scene.isDirty) throw new InvalidOperationException("Dirty scene: refusing to discard owner work");
                    if (string.IsNullOrEmpty(scene.path))
                    {
                        if (!Application.isBatchMode || SceneManager.sceneCount != 1 || scene.rootCount != 0)
                            throw new InvalidOperationException("Populated or interactive untitled scene: refusing");
                        empty = true;
                    }
                }
                Directory.CreateDirectory(Output);
                var catalog = JsonUtility.FromJson<Catalog>(File.ReadAllText("Assets/Resources/Data/Canonical/structures-catalog.json"));
                var type = typeof(HubStructureVisualInjector);
                var swaps = (Array)type.GetField("Swaps", PrivateStatic).GetValue(null);
                opened = true;
                var hub = EditorSceneManager.OpenScene("Assets/Scenes/Main_Castle_Overworld.unity", OpenSceneMode.Single);
                var hosts = hub.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Transform>(true)).ToArray();
                foreach (var entry in catalog.entries)
                foreach (var hostName in entry.repo?.bakedTwins ?? Array.Empty<string>())
                {
                    // Pet area is outside WO-1291's eight storefronts.
                    if (hostName == "EchoHollow_Pets_RoamingArea") continue;
                    object swap = swaps.Cast<object>().SingleOrDefault(s => (string)Field(s, "bakedName") == hostName);
                    if (swap == null) continue;
                    try
                    {
                        var host = hosts.Single(t => t.name == hostName);
                        var source = StructureAssetLoader.LoadStructureAsset<GameObject>(entry.visualPrefabPath);
                        if (source == null) throw new InvalidOperationException("Unresolved catalog address " + entry.visualPrefabPath);
                        string sourcePath = AssetDatabase.GetAssetPath(source);
                        if (!sourcePath.Contains("/Synty/")) throw new InvalidOperationException("Expected Synty wrapper, resolved " + sourcePath);
                        if ((string)Field(swap, "modelPath") != entry.visualPrefabPath)
                            throw new InvalidOperationException("Catalog/injector address disagreement");
                        Capture(host.gameObject, hub, hostName + "__saved", evidence);
                        Probe(swap, source, hostName, evidence, ref tilted, ref repainted);
                        count++;
                    }
                    catch (Exception ex) { failures.Add(hostName + ": " + ex.GetBaseException().Message); }
                }
            }
            catch (Exception ex) { failures.Add(ex.GetBaseException().Message); }
            finally
            {
                if (opened)
                {
                    if (empty) EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                    else EditorSceneManager.RestoreSceneManagerSetup(setup);
                }
            }
            Directory.CreateDirectory(Output);
            evidence.AddRange(failures.Select(f => "HARNESS_FAILURE " + f));
            File.WriteAllLines(Path.Combine(Output, "evidence.txt"), evidence);
            if (failures.Count != 0 || count != 8)
                Debug.LogError($"STOREFRONT_SYNTY_PROBE_FAIL captured={count}/8: " + string.Join("; ", failures));
            else
            {
                Debug.Log($"STOREFRONT_SYNTY_CAPTURE_OK 8/8 saved + actual injector + source reference -> {Output}");
                if (tilted > 0 || repainted > 0)
                    Debug.LogError($"STOREFRONT_SYNTY_RED source-up tilted={tilted}/8; embedded maps replaced={repainted}/8. Inspect pictures; bounds alone cannot prove upright art.");
                else Debug.Log("STOREFRONT_SYNTY_POLICY_OK 8/8 source-up preserved and native maps retained; visual review still required");
            }
        }

        static object Field(object row, string field) => row.GetType().GetField(field).GetValue(row);

        static void Probe(object swap, GameObject source, string name, List<string> evidence, ref int tilted, ref int repainted)
        {
            var type = typeof(HubStructureVisualInjector);
            var texField = swap.GetType().GetField("texPath");
            var texPath = (string)texField.GetValue(swap);
            if (!string.IsNullOrEmpty(texPath) && StructureAssetLoader.LoadStructureAsset<Texture2D>(texPath) == null)
                throw new InvalidOperationException("Forced texture unresolved; refusing to arm runtime retry callback");
            var bound = (HashSet<string>)type.GetField("s_texBound", PrivateStatic).GetValue(null);
            var retry = (HashSet<string>)type.GetField("s_texRetryArmed", PrivateStatic).GetValue(null);
            var oldBound = bound.ToArray(); var oldRetry = retry.ToArray();
            var owned = new List<Object>();
            var fixture = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            try
            {
                SceneManager.SetActiveScene(fixture);
                var reference = Object.Instantiate(source); owned.Add(reference);
                reference.name = "SourceUprightReference";
                Capture(reference, fixture, name + "__source", evidence);
                reference.SetActive(false);
                var host = new GameObject("Probe_" + name); owned.Add(host);
                // Exact production skin body, delaying only albedo until material isolation.
                texField.SetValue(swap, null);
                type.GetMethod("SkinStorefront", PrivateStatic).Invoke(null, new[] { swap, host.transform });
                texField.SetValue(swap, texPath);
                var visual = host.transform.Find("LightSkin_" + name);
                if (visual == null) throw new InvalidOperationException("Actual injector produced no marker visual");
                var renderers = visual.GetComponentsInChildren<Renderer>(true);
                foreach (var r in renderers)
                    r.sharedMaterials = r.sharedMaterials.Select(m =>
                    {
                        if (m == null) return null;
                        var copy = new Material(m); owned.Add(copy); return copy;
                    }).ToArray();
                string before = Maps(renderers);
                bound.Remove(name); retry.Remove(name);
                type.GetMethod("ApplyForcedAlbedo", PrivateStatic).Invoke(null, new object[] { swap, host.transform, visual.gameObject });
                string after = Maps(renderers);
                float upDot = Vector3.Dot(visual.localRotation * Vector3.up, source.transform.localRotation * Vector3.up);
                if (upDot < .999f) tilted++;
                if (before != after) repainted++;
                evidence.Add($"{name} address={Field(swap, "modelPath")} source={AssetDatabase.GetAssetPath(source)} localEuler={visual.localEulerAngles} localPosition={visual.localPosition} sourceUpDot={upDot:F4} forcedTexture={texPath ?? "<none>"} beforeMaps={before} afterMaps={after}");
                Capture(visual.gameObject, fixture, name + "__injected", evidence);
            }
            finally
            {
                texField.SetValue(swap, texPath);
                bound.Clear(); bound.UnionWith(oldBound); retry.Clear(); retry.UnionWith(oldRetry);
                for (int i = owned.Count - 1; i >= 0; i--) if (owned[i] != null) Object.DestroyImmediate(owned[i]);
                EditorSceneManager.CloseScene(fixture, true);
            }
        }

        static string Maps(IEnumerable<Renderer> renderers) => string.Join("|", renderers.SelectMany(r => r.sharedMaterials)
            .Where(m => m != null).Select(m => m.shader.name + ":" +
                string.Join(",", new[] { "_BaseMap", "_MainTex" }.Where(m.HasProperty)
                    .Select(p => p + "=" + AssetDatabase.GetAssetPath(m.GetTexture(p))))));

        static void Capture(GameObject subject, Scene scene, string label, List<string> evidence)
        {
            var renderers = subject.GetComponentsInChildren<Renderer>(true).Where(r => r.enabled && r.gameObject.activeInHierarchy).ToArray();
            if (renderers.Length == 0) throw new InvalidOperationException(label + " has no active rendered geometry");
            var bounds = renderers[0].bounds;
            foreach (var r in renderers.Skip(1)) bounds.Encapsulate(r.bounds);
            var layers = subject.GetComponentsInChildren<Transform>(true).ToDictionary(t => t, t => t.gameObject.layer);
            var cameraGo = new GameObject("ProbeCamera");
            var lightGo = new GameObject("ProbeLight");
            SceneManager.MoveGameObjectToScene(cameraGo, scene); SceneManager.MoveGameObjectToScene(lightGo, scene);
            var rt = new RenderTexture(900, 900, 24);
            Texture2D pixels = null;
            var previous = RenderTexture.active;
            try
            {
                foreach (var pair in layers) pair.Key.gameObject.layer = 31;
                var camera = cameraGo.AddComponent<Camera>();
                camera.scene = scene; camera.cullingMask = 1 << 31;
                camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.1f, .11f, .13f);
                camera.fieldOfView = 35; camera.nearClipPlane = .01f;
                float span = Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z, .1f);
                camera.farClipPlane = span * 30;
                camera.transform.position = bounds.center + new Vector3(.75f, .34f, -1).normalized * span * 3.1f;
                camera.transform.LookAt(bounds.center); camera.targetTexture = rt;
                var light = lightGo.AddComponent<Light>(); light.type = LightType.Directional; light.intensity = 1.35f;
                light.cullingMask = 1 << 31; light.transform.rotation = Quaternion.Euler(38, 150, 0);
                camera.Render(); RenderTexture.active = rt;
                pixels = new Texture2D(900, 900, TextureFormat.RGB24, false);
                pixels.ReadPixels(new Rect(0, 0, 900, 900), 0, 0); pixels.Apply();
                File.WriteAllBytes(Path.Combine(Output, label + ".png"), pixels.EncodeToPNG());
                evidence.Add(label + " bounds=" + bounds + " maps=" + Maps(renderers));
            }
            finally
            {
                RenderTexture.active = previous;
                foreach (var pair in layers) if (pair.Key != null) pair.Key.gameObject.layer = pair.Value;
                if (pixels != null) Object.DestroyImmediate(pixels);
                Object.DestroyImmediate(cameraGo); Object.DestroyImmediate(lightGo); Object.DestroyImmediate(rt);
            }
        }
    }
}

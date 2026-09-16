using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace DeNelle.Editor
{
    // Owner 2026-09-13: saved storefront layout is authoritative. Evidence only; never saves assets/scenes.
    public static class OwnerCastleLayoutAudit
    {
        public const string Output = "Builds/castle-validation-20260913";
        public const string ScenePath = "Assets/Scenes/Main_Castle_Overworld.unity";
        public const string RingName = "The8Structures_Storefronts_NPCPoints";

        [MenuItem("Defenders/Diagnostics/Audit owner castle layout")]
        public static void Run()
        {
            var setup = EditorSceneManager.GetSceneManagerSetup();
            var evidence = new List<string> { "Owner20260913 saved-scene audit UTC=" + DateTime.UtcNow.ToString("O") };
            var failures = new List<string>();
            bool opened = false, empty = false;
            int count = 0;
            try
            {
                if (Application.isPlaying) throw new InvalidOperationException("Edit mode only");
                for (int i = 0; i < SceneManager.sceneCount; i++)
                {
                    var current = SceneManager.GetSceneAt(i);
                    if (current.isDirty) throw new InvalidOperationException("Dirty scene; refusing to discard owner work: " + current.path);
                    if (string.IsNullOrEmpty(current.path))
                    {
                        if (!Application.isBatchMode || SceneManager.sceneCount != 1 || current.rootCount != 0)
                            throw new InvalidOperationException("Interactive or populated untitled scene; refusing");
                        empty = true;
                    }
                }
                Directory.CreateDirectory(Output);
                opened = true;
                var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
                var ring = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Transform>(true))
                    .Single(t => t.name == RingName);
                count = ring.childCount;
                bool cathedral = ring.Cast<Transform>().Any(t => t.name == "ArcaneTower_MagicUpgrades");
                if (count != (cathedral ? 14 : 13)) failures.Add("Unexpected ring count=" + count + " cathedral=" + cathedral);
                evidence.Add("RING children=" + count + " cathedral=" + cathedral);
                int index = 0;
                foreach (Transform child in ring)
                {
                    string label = "Owner20260913_" + (++index).ToString("00") + "_" + child.name;
                    try
                    {
                        Inspect(child.gameObject, evidence, failures);
                        Capture(child.gameObject, scene, label, evidence);
                    }
                    catch (Exception ex) { failures.Add(child.name + ": " + ex.GetBaseException().Message); }
                }
                Capture(ring.gameObject, scene, "Owner20260913_ring_overview", evidence, true);
            }
            catch (Exception ex) { failures.Add(ex.GetBaseException().Message); }
            finally
            {
                if (opened)
                {
                    try
                    {
                        if (empty) EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                        else EditorSceneManager.RestoreSceneManagerSetup(setup);
                    }
                    catch (Exception ex) { failures.Add("Scene setup restore failed: " + ex.Message); }
                }
            }
            Directory.CreateDirectory(Output);
            evidence.AddRange(failures.Select(f => "FAIL " + f));
            evidence.Add("Visuals require human inspection; structural success is not visual approval.");
            File.WriteAllLines(Path.Combine(Output, "Owner20260913_evidence.txt"), evidence);
            if (failures.Count > 0) Debug.LogError("OWNER_CASTLE_LAYOUT_AUDIT_FAIL count=" + count + ": " + string.Join("; ", failures));
            else Debug.Log("OWNER_CASTLE_LAYOUT_AUDIT_OK count=" + count + " -> " + Output + "; structural evidence only, visuals not approved");
        }

        static void Inspect(GameObject subject, List<string> evidence, List<string> failures)
        {
            var t = subject.transform;
            string path = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(subject);
            evidence.Add("OBJECT " + subject.name + " source=" + path + " guid=" + AssetDatabase.AssetPathToGUID(path) +
                " localTRS=" + t.localPosition.ToString("F4") + " / " + t.localEulerAngles.ToString("F4") + " / " + t.localScale.ToString("F4") +
                " worldTRS=" + t.position.ToString("F4") + " / " + t.eulerAngles.ToString("F4") + " / " + t.lossyScale.ToString("F4"));
            var renderers = subject.GetComponentsInChildren<Renderer>(true);
            int meshes = subject.GetComponentsInChildren<MeshFilter>(true).Count(m => m.sharedMesh != null) +
                subject.GetComponentsInChildren<SkinnedMeshRenderer>(true).Count(m => m.sharedMesh != null);
            int missing = subject.GetComponentsInChildren<Transform>(true).Sum(c => GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(c.gameObject));
            evidence.Add("  meshes=" + meshes + " renderers=" + renderers.Length + " enabledRenderers=" +
                renderers.Count(r => r.enabled && r.gameObject.activeInHierarchy) + " missingScripts=" + missing +
                " fixers=" + string.Join(",", subject.GetComponentsInChildren<Component>(true).Where(c => c != null &&
                    c.GetType().Name.IndexOf("Fix", StringComparison.OrdinalIgnoreCase) >= 0).Select(c => c.GetType().FullName)));
            if (missing != 0 || meshes == 0) failures.Add(subject.name + " missingScripts=" + missing + " meshes=" + meshes);
            foreach (var renderer in renderers)
            {
                evidence.Add("  RENDERER " + renderer.name + " enabled=" + renderer.enabled + " active=" + renderer.gameObject.activeInHierarchy + " bounds=" + renderer.bounds);
                foreach (var material in renderer.sharedMaterials)
                {
                    if (material == null) { failures.Add(subject.name + " missing material"); continue; }
                    evidence.Add("    MATERIAL " + material.name + " path=" + AssetDatabase.GetAssetPath(material) + " shader=" +
                        (material.shader == null ? "NULL" : material.shader.name));
                    if (material.shader == null) failures.Add(subject.name + " missing shader");
                    foreach (string property in material.GetTexturePropertyNames())
                    {
                        var texture = material.GetTexture(property);
                        evidence.Add("      " + property + "=" + (texture == null ? "NULL" : texture.name + " path=" + AssetDatabase.GetAssetPath(texture)));
                    }
                }
            }
        }

        // Temporarily hides unrelated renderers for legible evidence; restores every flag.
        // No shared material/model mutation, scene save, prefab application or asset import.
        public static void Capture(GameObject subject, Scene scene, string label, List<string> evidence, bool overview = false, bool context = false)
        {
            var shown = subject.GetComponentsInChildren<Renderer>(true).Where(r => r.enabled && r.gameObject.activeInHierarchy && !r.forceRenderingOff).ToArray();
            if (shown.Length == 0) throw new InvalidOperationException(label + " has no visible geometry");
            var bounds = shown[0].bounds;
            foreach (var renderer in shown.Skip(1)) bounds.Encapsulate(renderer.bounds);
            var hidden = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Renderer>(true))
                .Where(r => !shown.Contains(r)).ToDictionary(r => r, r => r.forceRenderingOff);
            var cameraGo = new GameObject("OwnerAuditCamera");
            var lightGo = new GameObject("OwnerAuditLight");
            SceneManager.MoveGameObjectToScene(cameraGo, scene);
            SceneManager.MoveGameObjectToScene(lightGo, scene);
            var rt = new RenderTexture(1200, 1200, 24);
            var previous = RenderTexture.active;
            Texture2D pixels = null;
            try
            {
                if (!context) foreach (var pair in hidden) pair.Key.forceRenderingOff = true;
                var camera = cameraGo.AddComponent<Camera>();
                camera.scene = scene; camera.cullingMask = ~0; camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(.1f, .11f, .13f); camera.fieldOfView = 35; camera.nearClipPlane = .01f;
                float span = Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z, .1f);
                camera.farClipPlane = span * 30;
                camera.transform.position = bounds.center + (overview ? new Vector3(.2f, 1.4f, -1f) : new Vector3(.75f, .34f, -1f)).normalized * span * 3.1f;
                camera.transform.LookAt(bounds.center); camera.targetTexture = rt;
                var light = lightGo.AddComponent<Light>(); light.type = LightType.Directional; light.intensity = 1.35f;
                light.transform.rotation = Quaternion.Euler(38, 150, 0);
                camera.Render(); RenderTexture.active = rt;
                pixels = new Texture2D(1200, 1200, TextureFormat.RGB24, false);
                pixels.ReadPixels(new Rect(0, 0, 1200, 1200), 0, 0); pixels.Apply();
                Directory.CreateDirectory(Output);
                string safe = string.Concat(label.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
                File.WriteAllBytes(Path.Combine(Output, safe + ".png"), pixels.EncodeToPNG());
                evidence.Add("CAPTURE " + safe + " bounds=" + bounds);
            }
            finally
            {
                RenderTexture.active = previous;
                foreach (var pair in hidden) if (pair.Key != null) pair.Key.forceRenderingOff = pair.Value;
                if (pixels != null) Object.DestroyImmediate(pixels);
                Object.DestroyImmediate(cameraGo); Object.DestroyImmediate(lightGo); Object.DestroyImmediate(rt);
            }
        }
    }
}

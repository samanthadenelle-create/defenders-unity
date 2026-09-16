using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DeNelle.Core.Economy;
using DeNelle.Core.State;
using DeNelle.Village;
using DeNelle.Village.Buildings.Progression;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace DeNelle.Editor
{
    // Verifies live fill support and refill stability on the preserved pallets.
    // Invoke with tools/run-unity-playmode.ps1 (no -quit).
    public static class StoragePalletFillPlayProof
    {
        internal const string Output = "Builds/StoragePalletFillPlayProof";
        private const string Arm = "StorageFillProof.Armed";
        private const string Isolated = "StorageFillProof.Isolated";
        private const string Original = "StorageFillProof.Original";
        private const string Existed = "StorageFillProof.Existed";
        private const string Result = "StorageFillProof.Result";
        private static ISaveProvider priorProvider;
        private static bool priorCloud;
        internal static MemoryProvider Provider;

        public static void Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Requires a closed Play session.");
            for (int i = 0; i < UnityEngine.SceneManagement.SceneManager.sceneCount; i++)
                if (UnityEngine.SceneManagement.SceneManager.GetSceneAt(i).isDirty)
                    throw new InvalidOperationException("Refusing to discard an unsaved scene.");
            var local = new LocalSaveProvider();
            SessionState.SetBool(Existed, local.Exists(SaveSchema.PlayerPrefsKey));
            SessionState.SetString(Original, local.Read(SaveSchema.PlayerPrefsKey) ?? "");
            SessionState.SetString(Result, "Proof did not complete");
            Directory.CreateDirectory(Output);
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            SessionState.SetBool(Arm, true);
            EditorApplication.EnterPlaymode();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Isolate()
        {
            if (!SessionState.GetBool(Arm, false)) return;
            priorProvider = GameStateService.Provider;
            priorCloud = GameStateService.SuppressCloudForIsolatedProof;
            Provider = new MemoryProvider();
            GameStateService.Provider = Provider;
            GameStateService.SuppressCloudForIsolatedProof = true;
            SessionState.SetBool(Isolated, true);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Plant()
        {
            if (!SessionState.GetBool(Arm, false)) return;
            SessionState.SetBool(Arm, false);
            new GameObject("StoragePalletFillPlayProof").AddComponent<StoragePalletFillPlayDriver>();
        }

        [InitializeOnLoadMethod]
        private static void Boot()
        {
            EditorApplication.playModeStateChanged -= AfterPlay;
            EditorApplication.playModeStateChanged += AfterPlay;
        }

        private static void AfterPlay(PlayModeStateChange change)
        {
            if (change != PlayModeStateChange.EnteredEditMode || !SessionState.GetBool(Isolated, false)) return;
            string failure = SessionState.GetString(Result, "Missing result");
            var local = new LocalSaveProvider();
            if (local.Exists(SaveSchema.PlayerPrefsKey) != SessionState.GetBool(Existed, false) ||
                (local.Read(SaveSchema.PlayerPrefsKey) ?? "") != SessionState.GetString(Original, ""))
                failure += " Original main save changed through shutdown.";
            GameStateService.Provider = priorProvider ?? new LocalSaveProvider();
            GameStateService.SuppressCloudForIsolatedProof = priorCloud;
            Provider = null;
            priorProvider = null;
            SessionState.SetBool(Isolated, false);
            if (string.IsNullOrEmpty(failure))
                Debug.Log("STORAGE_FILL_PLAY_PROOF_OK actual Start empty/full/empty/full for three pallets; supported fill within deck; body invariant; nine captures; main save unchanged through Play exit.");
            else Debug.LogError("STORAGE_FILL_PLAY_PROOF_FAIL " + failure);
            if (Application.isBatchMode) EditorApplication.Exit(string.IsNullOrEmpty(failure) ? 0 : 1);
        }

        internal static void Finish(string failure)
        {
            SessionState.SetString(Result, failure ?? "");
            // Provider stays isolated through service OnDisable/shutdown autosaves.
            EditorApplication.ExitPlaymode();
        }

        internal sealed class MemoryProvider : ISaveProvider
        {
            private readonly Dictionary<string, string> slots = new Dictionary<string, string>();
            internal int Writes;
            public bool Exists(string slot) => slots.ContainsKey(slot);
            public string Read(string slot) => slots.TryGetValue(slot, out var value) ? value : "";
            public void Write(string slot, string value) { slots[slot] = value; Writes++; }
            public void Delete(string slot) => slots.Remove(slot);
        }
    }

    public sealed class StoragePalletFillPlayDriver : MonoBehaviour
    {
        private readonly List<string> evidence = new List<string>();
        private const int Layer = 30;
        private IEnumerator Start()
        {
            var exercise = Exercise();
            string failure = "";
            while (true)
            {
                bool more;
                try { more = exercise.MoveNext(); }
                catch (Exception ex) { failure = ex.ToString(); break; }
                if (!more) break;
                yield return exercise.Current;
            }
            evidence.Add(string.IsNullOrEmpty(failure) ? "MEASUREMENTS_COMPLETE; awaiting post-Play save check" : failure);
            File.WriteAllLines(StoragePalletFillPlayProof.Output + "/summary.txt", evidence);
            StoragePalletFillPlayProof.Finish(failure);
        }

        private IEnumerator Exercise()
        {
            yield return null;
            Require(StoragePalletFillPlayProof.Provider != null &&
                ReferenceEquals(GameStateService.Provider, StoragePalletFillPlayProof.Provider) &&
                GameStateService.SuppressCloudForIsolatedProof, "Save isolation absent before runtime startup");
            var service = GameStateService.Instance;
            Require(service != null && service.State != null, "Runtime state missing");
            service.State.Onboarded = true;
            service.State.OwnedBase = null;
            var grid = PlacementGrid.Instance != null ? PlacementGrid.Instance : new GameObject("FillProofGrid").AddComponent<PlacementGrid>();
            var parent = new GameObject("FillProofBodies").transform;
            var camera = new GameObject("FillProofCamera").AddComponent<Camera>();
            camera.enabled = false;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(.19f, .21f, .24f, 1);
            camera.cullingMask = 1 << Layer;
            camera.orthographic = true;
            camera.nearClipPlane = .01f;
            camera.farClipPlane = 100f;
            var light = new GameObject("FillProofLight").AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.4f;
            light.cullingMask = 1 << Layer;
            light.transform.rotation = Quaternion.Euler(45, -25, 0);
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = Color.gray;
            foreach (var pair in new[] { ("lumberyard", "Wood_Pallet", BankResource.Wood),
                ("foundry", "Iron_Pallet", BankResource.Iron), ("silo", "Stone_Pallet", BankResource.Stone) })
            {
                var record = new PlacedStructureData { itemId = pair.Item1, cellX = 2, cellZ = 2, level = 1 };
                service.State.BaseLayout = new List<PlacedStructureData> { record };
                // A replay record alone is not a completed build: TownBankCapacity.BuildSlots
                // requires the same existence co-gate written by the placement commit.
                service.State.MarkEverBuilt(pair.Item1);
                Require(service.State.HasEverBuilt(pair.Item1), pair.Item1 + " completed-build identity missing");
                SetWallet(service.State, 0);
                var placed = BaseLayoutLoader.SpawnForLayout(record, grid, parent, replayStoryServices: false);
                Require(placed != null, pair.Item1 + " actual spawn failed");
                // Neither Start nor Attach is invoked manually: real player loop owns both.
                for (int f = 0; f < 5; f++) yield return null;
                yield return new WaitForSecondsRealtime(.4f);
                var view = placed.GetComponent<StorageStackView>();
                Require(view != null && view.enabled, pair.Item1 + " automatic Start did not attach active fill view");
                var body = placed.GetComponentsInChildren<Transform>(true).Single(t => t.name == pair.Item2);
                var bodyRenderers = body.GetComponentsInChildren<Renderer>(true);
                Bounds originalBounds = BoundsOf(bodyRenderers);
                Vector3 originalScale = body.localScale;
                var originalMaterials = bodyRenderers.SelectMany(r => r.sharedMaterials).ToArray();
                var originalMeshes = body.GetComponentsInChildren<MeshFilter>(true).Select(m => m.sharedMesh).ToArray();
                var stack = placed.GetComponentsInChildren<Transform>(true).SingleOrDefault(t => t.name == "StorageFillStack");
                Require(stack != null, pair.Item1 + " no real stack (possibly abstract fallback); inspect runtime warning");
                var props = stack.Cast<Transform>().Where(t => t.name.StartsWith("StorageProp_", StringComparison.Ordinal)).ToArray();
                Require(props.Length == 14, pair.Item1 + " did not build fourteen fill props");
                foreach (var t in placed.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = Layer;
                // Same framing for empty and full; inactive full inventory contributes only to framing.
                Bounds frame = originalBounds;
                foreach (var r in stack.GetComponentsInChildren<Renderer>(true)) frame.Encapsulate(r.bounds);
                camera.orthographicSize = Mathf.Max(1.5f, frame.extents.magnitude * 1.2f);
                camera.transform.position = frame.center + new Vector3(4, 4, -6).normalized * 12f;
                camera.transform.LookAt(frame.center);
                CheckSlot(pair.Item1, pair.Item3, record, false);
                Require(props.Count(t => t.gameObject.activeSelf) == 0, pair.Item1 + " empty fill has visible props");
                Report(pair.Item1 + " empty", originalBounds, stack, props);
                Capture(camera, pair.Item1 + "-empty");
                SetWallet(service.State, 1000000);
                yield return new WaitForSecondsRealtime(.7f); // poll plus .15s actual animation
                CheckSlot(pair.Item1, pair.Item3, record, true);
                Require(props.Count(t => t.gameObject.activeSelf) == 14, pair.Item1 + " full fill lacks fourteen visible props");
                Bounds after = BoundsOf(bodyRenderers);
                Require((after.center - originalBounds.center).sqrMagnitude < .000001f &&
                    (after.size - originalBounds.size).sqrMagnitude < .000001f && body.localScale == originalScale,
                    pair.Item1 + " fill moved/resized approved body");
                Require(originalMaterials.SequenceEqual(bodyRenderers.SelectMany(r => r.sharedMaterials)) &&
                    originalMeshes.SequenceEqual(body.GetComponentsInChildren<MeshFilter>(true).Select(m => m.sharedMesh)),
                    pair.Item1 + " fill replaced body mesh/material");
                Require(bodyRenderers.All(r => r.enabled && r.gameObject.activeInHierarchy), pair.Item1 + " fill hid body");
                Report(pair.Item1 + " full", after, stack, props);
                CheckSupport(pair.Item1, after, props);
                Capture(camera, pair.Item1 + "-full");
                var firstFullBounds = props.Select(t => BoundsOf(t.GetComponentsInChildren<Renderer>())).ToArray();
                var firstFullScales = props.Select(t => t.localScale).ToArray();
                SetWallet(service.State, 0);
                yield return new WaitForSecondsRealtime(.7f);
                CheckSlot(pair.Item1, pair.Item3, record, false);
                Require(props.All(t => !t.gameObject.activeSelf), pair.Item1 + " drain left visible props");
                SetWallet(service.State, 1000000);
                yield return new WaitForSecondsRealtime(.7f);
                CheckSlot(pair.Item1, pair.Item3, record, true);
                Require(props.All(t => t.gameObject.activeSelf), pair.Item1 + " refill lost props");
                for (int i = 0; i < props.Length; i++)
                {
                    var current = BoundsOf(props[i].GetComponentsInChildren<Renderer>());
                    Require((current.center - firstFullBounds[i].center).sqrMagnitude < .000001f &&
                        (current.size - firstFullBounds[i].size).sqrMagnitude < .000001f && props[i].localScale == firstFullScales[i],
                        pair.Item1 + " hide/show scale or contact drift at prop " + i);
                }
                Bounds cycleBody = BoundsOf(bodyRenderers);
                Require((cycleBody.center - originalBounds.center).sqrMagnitude < .000001f &&
                    (cycleBody.size - originalBounds.size).sqrMagnitude < .000001f && body.localScale == originalScale &&
                    bodyRenderers.All(r => r.enabled && r.gameObject.activeInHierarchy), pair.Item1 + " cycle changed approved body");
                CheckSupport(pair.Item1 + " refill", cycleBody, props);
                Report(pair.Item1 + " refill", cycleBody, stack, props);
                Capture(camera, pair.Item1 + "-refill");
                Require(service.TrySave(out var reason), "Isolated production save failed: " + reason);
                Destroy(placed.gameObject);
                yield return null;
            }
            Require(StoragePalletFillPlayProof.Provider.Writes >= 3, "No actual isolated save writes");
            Log("STORAGE_FILL_SAVE_WRITES " + StoragePalletFillPlayProof.Provider.Writes);
        }

        private static void SetWallet(GameState state, int amount)
        { state.Wood = amount; state.Iron = amount; state.Resources.Stone = amount; }

        private void CheckSlot(string id, BankResource resource, PlacedStructureData record, bool full)
        {
            Require(TownBankCapacity.TryGetSlot(resource, TownBankCapacity.InstanceKeyOf(id, record.cellX, record.cellZ), out var slot), id + " live bank slot missing");
            Require(slot.Capacity > 0 && (full ? slot.Contents == slot.Capacity : slot.Contents == 0), id + " wallet phase did not reach requested slot fill");
            Log(id + " slot contents=" + slot.Contents + " capacity=" + slot.Capacity);
        }

        private void Report(string phase, Bounds body, Transform stack, Transform[] props)
        {
            var visible = props.Where(t => t.gameObject.activeInHierarchy).SelectMany(t => t.GetComponentsInChildren<Renderer>()).Where(r => r.enabled).ToArray();
            string detail = "none";
            if (visible.Length > 0)
            {
                Bounds fill = BoundsOf(visible);
                detail = "center=" + fill.center.ToString("F6") + " size=" + fill.size.ToString("F6") +
                    " aabbIntersectsBody=" + fill.Intersects(body) + " fillBottomMinusBodyTop=" + (fill.min.y - body.max.y).ToString("F6");
            }
            Log("STORAGE_FILL_MEASURE " + phase + " bodyCenter=" + body.center.ToString("F6") +
                " bodySize=" + body.size.ToString("F6") + " stackLocal=" + stack.localPosition.ToString("F6") +
                " visibleProps=" + props.Count(t => t.gameObject.activeSelf) + " fill=" + detail);
        }

        // Independent oracle reads actual world renderers, never TryDeckSeat or its index
        // arrangement. These fixtures have yaw=0, so XZ rectangles are the measured deck.
        private void CheckSupport(string id, Bounds deck, Transform[] props)
        {
            var bounds = props.Select(t => BoundsOf(t.GetComponentsInChildren<Renderer>())).ToArray();
            const float epsilon = .002f;
            for (int i = 0; i < bounds.Length; i++)
            {
                var b = bounds[i];
                Require(b.min.x >= deck.min.x - epsilon && b.max.x <= deck.max.x + epsilon &&
                    b.min.z >= deck.min.z - epsilon && b.max.z <= deck.max.z + epsilon,
                    id + " prop footprint outside approved deck: " + i);
                Require(b.min.y >= deck.max.y - epsilon, id + " prop penetrates deck: " + i);
                if (Mathf.Abs(b.min.y - deck.max.y) <= epsilon) continue;
                float supportedArea = 0f;
                for (int j = 0; j < bounds.Length; j++)
                {
                    if (j == i || Mathf.Abs(bounds[j].max.y - b.min.y) > epsilon) continue;
                    supportedArea += Mathf.Max(0, Mathf.Min(b.max.x, bounds[j].max.x) - Mathf.Max(b.min.x, bounds[j].min.x)) *
                        Mathf.Max(0, Mathf.Min(b.max.z, bounds[j].max.z) - Mathf.Max(b.min.z, bounds[j].min.z));
                }
                Require(supportedArea >= b.size.x * b.size.z * .99f, id + " unsupported/floating prop: " + i);
            }
            Log("STORAGE_FILL_SUPPORT_OK " + id + " fourteen renderer footprints on deck or fully supported by contacting lower layer; no body penetration");
        }

        private void Capture(Camera camera, string name)
        {
            var target = new RenderTexture(1200, 1200, 24, RenderTextureFormat.ARGB32);
            var previous = RenderTexture.active;
            var pixels = new Texture2D(1200, 1200, TextureFormat.RGB24, false);
            try
            {
                camera.targetTexture = target;
                camera.Render();
                RenderTexture.active = target;
                pixels.ReadPixels(new Rect(0, 0, 1200, 1200), 0, 0);
                pixels.Apply();
                // A flat backdrop is not a useful capture. Compare to its actual corner pixel
                // so linear/gamma conversion does not create false coverage.
                var colors = pixels.GetPixels32();
                var background = colors[0];
                int changed = colors.Count(c => Math.Abs(c.r - background.r) + Math.Abs(c.g - background.g) + Math.Abs(c.b - background.b) > 30);
                string path = StoragePalletFillPlayProof.Output + "/" + name + ".png";
                File.WriteAllBytes(path, pixels.EncodeToPNG());
                Log("STORAGE_FILL_CAPTURE " + path + " coverage=" + (changed / (float)colors.Length).ToString("F6"));
                Require(changed > colors.Length / 200, name + " blank capture");
            }
            finally
            {
                camera.targetTexture = null;
                RenderTexture.active = previous;
                target.Release();
                Destroy(target); Destroy(pixels);
            }
        }

        private static Bounds BoundsOf(Renderer[] renderers)
        {
            Require(renderers.Length > 0, "Missing renderers");
            var result = renderers[0].bounds;
            foreach (var renderer in renderers) result.Encapsulate(renderer.bounds);
            return result;
        }
        private void Log(string line) { evidence.Add(line); Debug.Log(line); }
        private static void Require(bool value, string why) { if (!value) throw new InvalidOperationException(why); }
    }
}

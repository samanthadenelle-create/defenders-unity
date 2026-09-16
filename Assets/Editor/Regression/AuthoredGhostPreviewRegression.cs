// regression-registry: standalone
using System;
using System.Collections.Generic;
using System.Reflection;
using DeNelle.Core.Catalog;
using DeNelle.Village;
using UnityEditor.SceneManagement;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace DeNelle.Editor
{
    /// <summary>Standalone preview gate. No movement, occupancy, save or asset writes.</summary>
    public static class AuthoredGhostPreviewRegression
    {
        const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        public static void RunStandalone()
        {
            if (!Run(out var reason)) { Debug.LogError("AUTHORED_GHOST_PREVIEW_FAIL " + reason); return; }
            try
            {
                CaptureSavedBarracks();
                Debug.Log("AUTHORED_GHOST_PREVIEW_OK " + reason + "; saved Barracks preview captured without scene writes");
            }
            catch (Exception error) { Debug.LogError("AUTHORED_GHOST_PREVIEW_FAIL " + error); }
        }

        static void CaptureSavedBarracks()
        {
            const string path = "Assets/Scenes/Main_Castle_Overworld.unity";
            string before = Convert.ToBase64String(File.ReadAllBytes(path));
            var scene = EditorSceneManager.OpenPreviewScene(path);
            GhostPreview ghost = null;
            GameObject host = null;
            try
            {
                Transform barracks = null;
                foreach (var root in scene.GetRootGameObjects())
                    foreach (var marker in root.GetComponentsInChildren<AuthoredCastleStorefront>(true))
                        if (marker.CanonicalId == "barracks")
                        { Require(barracks == null, "Ambiguous saved Barracks"); barracks = marker.transform; }
                Require(barracks != null, "Saved authored Barracks missing");
                host = new GameObject("SavedBarracksPreviewProof");
                SceneManager.MoveGameObjectToScene(host, scene);
                ghost = host.AddComponent<GhostPreview>();
                Require(ghost.SetAuthoredEntry(barracks), "Actual saved Barracks art is unsupported");
                Require(ghost.MoveToAuthored(barracks.position, barracks.rotation), "Saved Barracks preview pose refused");
                var expected = barracks.GetComponentsInChildren<MeshFilter>(true);
                var actual = Candidate(ghost).GetComponentsInChildren<MeshFilter>(true);
                Require(expected.Length == actual.Length, "Saved Barracks preview mesh count differs");
                for (int i = 0; i < expected.Length; i++)
                {
                    Require(expected[i].sharedMesh == actual[i].sharedMesh, "Saved Barracks mesh identity changed");
                    CompareVertices(expected[i].transform, actual[i].transform, expected[i].sharedMesh);
                }
                var evidence = new List<string>();
                // EditorRegression cannot reference the parent Editor assembly. Reuse its
                // capture entry by reflection, without introducing a circular asmdef edge.
                var audit = Type.GetType("DeNelle.Editor.OwnerCastleLayoutAudit, DeNelle.Editor");
                var capture = audit?.GetMethod("Capture", BindingFlags.Public | BindingFlags.Static);
                Require(capture != null, "Existing castle capture entry unavailable");
                void Capture(GameObject subject, string label) => capture.Invoke(null,
                    new object[] { subject, scene, label, evidence, false, false });
                Capture(barracks.gameObject, "AuthoredPreview_saved_source");
                ghost.SetValid(true);
                Capture(Visual(ghost), "AuthoredPreview_saved_valid");
                ghost.SetValid(false);
                Capture(Visual(ghost), "AuthoredPreview_saved_blocked");
                string output = (string)audit.GetField("Output").GetRawConstantValue();
                File.WriteAllLines(Path.Combine(output, "AuthoredPreview_capture_evidence.txt"), evidence);
            }
            finally
            {
                if (ghost != null) ghost.Clear();
                if (host != null) Object.DestroyImmediate(host);
                EditorSceneManager.ClosePreviewScene(scene);
                Require(before == Convert.ToBase64String(File.ReadAllBytes(path)), "Saved castle scene file changed");
            }
        }

        public static bool Run(out string reason)
        {
            Scene prior = SceneManager.GetActiveScene(), fixture = default;
            bool ownsScene = false;
            var owned = new List<Object>();
            GhostPreview ghost = null;
            try
            {
                if (string.IsNullOrEmpty(prior.path))
                {
                    Require(Application.isBatchMode && !prior.isDirty && SceneManager.sceneCount == 1 && prior.GetRootGameObjects().Length == 0,
                        "Requires pristine empty batch scene or a saved scene; will not discard unsaved user work.");
                    fixture = prior;
                }
                else { fixture = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive); ownsScene = true; }
                SceneManager.SetActiveScene(fixture);
                var shader = Shader.Find("Universal Render Pipeline/Lit");
                Require(shader != null && shader.isSupported, "Installed URP/Lit shader unavailable.");
                var texture = Own(owned, new Texture2D(2, 2));
                texture.SetPixels(new[] { Color.red, Color.blue, Color.yellow, Color.green }); texture.Apply();
                var material = Own(owned, new Material(shader));
                material.SetTexture("_BaseMap", texture);
                material.SetColor("_BaseColor", new Color(.7f, .6f, .3f, 1));
                var mesh = Own(owned, MakeMesh(false));
                var ancestorMesh = Own(owned, MakeMesh(true));

                var a = Node("AncestorA", null, owned);
                Pose(a, new Vector3(7, 2, -5), new Vector3(13, 23, -11), new Vector3(1.4f, .8f, 1.9f));
                var b = Node("AncestorB", a, owned);
                Pose(b, new Vector3(1, 3, 2), new Vector3(-17, 41, 9), new Vector3(.7f, 1.6f, 1.1f));
                Render(b, ancestorMesh, material); // Renderer ON the ancestor, not its sibling.
                var source = Node("SourceRoot", b, owned);
                Pose(source, new Vector3(2, .6f, -4), new Vector3(19, 12, -8), new Vector3(1.2f, .9f, 1.5f));
                Render(source, mesh, material);
                var body = Node("Body", source, owned);
                Pose(body, new Vector3(.8f, 1.1f, -.3f), new Vector3(5, 21, 7), new Vector3(.6f, 1.3f, .8f));
                var bodyRenderer = Render(body, mesh, material);
                body.gameObject.AddComponent<BoxCollider>();
                body.gameObject.AddComponent<UnityEngine.AI.NavMeshObstacle>();
                body.gameObject.AddComponent<AuthoredCastleStorefront>().Configure("fixture", "fixture");
                var inactive = Node("InactiveBranch", source, owned);
                Render(inactive, mesh, material); inactive.gameObject.SetActive(false);
                var disabled = Node("DisabledRenderer", source, owned);
                Render(disabled, mesh, material).enabled = false;
                var sourceMatrices = Snapshot(a);

                // Independent hierarchy, never passed to GhostPreview. Unity itself supplies the oracle.
                var ra = Node("ReferenceA", null, owned); CopyLocal(a, ra);
                var rb = Node("ReferenceB", ra, owned); CopyLocal(b, rb);
                var rr = Node("ReferenceRoot", rb, owned); CopyLocal(source, rr);
                var rbody = Node("ReferenceBody", rr, owned); CopyLocal(body, rbody);

                var host = Node("PreviewHost", null, owned);
                Pose(host, new Vector3(-30, 5, 20), new Vector3(28, -12, 37), new Vector3(2, .5f, 3));
                ghost = host.gameObject.AddComponent<GhostPreview>();
                Require(ghost.SetAuthoredEntry(source), "Valid authored hierarchy refused.");
                Require(Visual(ghost).scene == host.gameObject.scene && Visual(ghost).transform.parent == null,
                    "Authored visual is not detached in host scene.");
                CheckRenderOnly(ghost, ancestorMesh, texture, 4);
                Require(!FindRenderer(Candidate(ghost), "DisabledRenderer").enabled &&
                    !FindRenderer(Candidate(ghost), "InactiveBranch").gameObject.activeSelf, "Initial visibility flags were not copied.");
                foreach (float yaw in new[] { 0f, 45f, 90f })
                {
                    var position = new Vector3(11 + yaw / 10, 4.5f, -17 - yaw / 9);
                    var rotation = Quaternion.Euler(17, yaw, -13);
                    Require(ghost.MoveToAuthored(position, rotation), "Valid candidate refused.");
                    rr.SetPositionAndRotation(position, rotation);
                    var copy = Candidate(ghost);
                    Near(copy.localScale, source.localScale, "Candidate changed source local scale");
                    CompareVertices(rr, copy, mesh);
                    CompareVertices(rbody, Find(copy, "Body"), mesh);
                    Near(ghost.CurrentPosition, position, "CurrentPosition is not candidate root");
                }
                AssertSnapshot(sourceMatrices);
                Require(bodyRenderer.sharedMaterial == material && material.mainTexture == texture, "Source material changed.");
                ghost.SetValid(false); ghost.SetReason("Fixture blocked");
                CheckTint(Find(Candidate(ghost), "Body").GetComponent<Renderer>(), new Color(.9f, .2f, .2f, .45f));
                ghost.SetValid(true);
                CheckTint(Find(Candidate(ghost), "Body").GetComponent<Renderer>(), new Color(.2f, .9f, .3f, .45f));

                var candidatePosition = ghost.CurrentPosition;
                var candidateRotation = Candidate(ghost).rotation;
                var previous = Visual(ghost);
                Refused(ghost, null, previous, "null source");
                foreach (var bad in new[] { new Quaternion(0, 0, 0, 0), new Quaternion(float.NaN, 0, 0, 1), new Quaternion(float.PositiveInfinity, 0, 0, 1) })
                {
                    Require(!ghost.MoveToAuthored(Vector3.zero, bad), "Malformed quaternion accepted.");
                    Near(ghost.CurrentPosition, candidatePosition, "Invalid pose mutated candidate");
                }
                Require(!ghost.MoveToAuthored(new Vector3(float.NaN, 0, 0), Quaternion.identity), "NaN position accepted.");
                var scaledQ = new Quaternion(candidateRotation.x * 2, candidateRotation.y * 2, candidateRotation.z * 2, candidateRotation.w * 2);
                Require(ghost.MoveToAuthored(candidatePosition, scaledQ), "Normalizable quaternion refused.");
                Require(Quaternion.Angle(Candidate(ghost).rotation, candidateRotation) < .01f, "Quaternion normalization changed orientation.");

                var badRoot = Node("InvalidSource", null, owned);
                Refused(ghost, badRoot, previous, "empty root");
                var badRenderer = Render(badRoot, mesh, material);
                var badFilter = badRoot.GetComponent<MeshFilter>();
                var emptyMesh = Own(owned, new Mesh());
                badFilter.sharedMesh = emptyMesh; Refused(ghost, badRoot, previous, "empty mesh"); badFilter.sharedMesh = mesh;
                badRoot.localScale = Vector3.zero; Refused(ghost, badRoot, previous, "singular scale"); badRoot.localScale = Vector3.one;
                badFilter.sharedMesh = null; Refused(ghost, badRoot, previous, "missing mesh"); badFilter.sharedMesh = mesh;
                badRenderer.sharedMaterial = null; Refused(ghost, badRoot, previous, "missing material");
                var brokenShader = Shader.Find("Hidden/InternalErrorShader");
                Require(brokenShader != null, "Error shader fixture missing.");
                var brokenMat = Own(owned, new Material(brokenShader));
                badRenderer.sharedMaterial = brokenMat; Refused(ghost, badRoot, previous, "broken shader");
                badRenderer.sharedMaterial = material;
                var overrideBlock = new MaterialPropertyBlock(); overrideBlock.SetColor("_BaseColor", Color.cyan);
                badRenderer.SetPropertyBlock(overrideBlock); Refused(ghost, badRoot, previous, "material property override");
                badRenderer.SetPropertyBlock(null);
                var skinnedHost = Node("UnsupportedSkinned", badRoot, owned);
                var skinned = skinnedHost.gameObject.AddComponent<SkinnedMeshRenderer>();
                skinned.sharedMesh = mesh; skinned.sharedMaterial = material;
                Refused(ghost, badRoot, previous, "skinned renderer"); Object.DestroyImmediate(skinnedHost.gameObject);

                ghost.SetValid(false); ghost.SetReason("Keep reason"); ghost.Hide();
                var oldMaterials = Materials(Visual(ghost));
                bodyRenderer.enabled = false; inactive.gameObject.SetActive(true);
                Require(ghost.SetAuthoredEntry(source), "Explicit refresh failed.");
                Require(!Visual(ghost).activeSelf && !ghost.IsValid && ghost.BlockedReason == "Keep reason", "Refresh lost hidden/valid/reason state.");
                Near(ghost.CurrentPosition, candidatePosition, "Refresh lost candidate pose");
                Require(Quaternion.Angle(Candidate(ghost).rotation, candidateRotation) < .01f, "Refresh lost candidate rotation.");
                Require(previous == null, "Refresh leaked prior visual."); Destroyed(oldMaterials);
                CheckRenderOnly(ghost, ancestorMesh, texture, 4);
                Require(!FindRenderer(Candidate(ghost), "Body").enabled && FindRenderer(Candidate(ghost), "InactiveBranch").gameObject.activeSelf,
                    "Refresh lost disabled renderer or newly active branch state.");

                // Explicit source mutations must invalidate stale detached geometry before use.
                Require(ghost.MoveToAuthored(candidatePosition, candidateRotation), "Refreshed candidate did not show.");
                a.localPosition += Vector3.one;
                Tick(ghost); Require(Visual(ghost) == null, "Ancestor mutation retained stale ghost.");
                Require(ghost.SetAuthoredEntry(source), "Rebuild after ancestor movement failed.");
                source.SetParent(a, false);
                Require(!ghost.MoveToAuthored(Vector3.zero, Quaternion.identity) && Visual(ghost) == null, "Source reparent retained stale ghost.");
                Require(ghost.SetAuthoredEntry(source), "Rebuild after reparent failed.");
                disabled.gameObject.SetActive(false); Tick(ghost);
                Require(Visual(ghost) == null, "Branch activity mutation retained stale ghost.");
                Require(ghost.SetAuthoredEntry(source), "Rebuild before renderer mutation failed.");
                source.GetComponent<MeshRenderer>().enabled = false; Tick(ghost);
                Require(Visual(ghost) == null, "Renderer enabled mutation retained stale ghost.");
                source.GetComponent<MeshRenderer>().enabled = true;
                Require(ghost.SetAuthoredEntry(source), "Rebuild before mesh mutation failed.");
                source.GetComponent<MeshFilter>().sharedMesh = ancestorMesh; Tick(ghost);
                Require(Visual(ghost) == null, "Mesh mutation retained stale ghost.");
                source.GetComponent<MeshFilter>().sharedMesh = mesh;

                Require(ghost.SetAuthoredEntry(source), "Rebuild before disable failed.");
                previous = Visual(ghost); oldMaterials = Materials(previous);
                host.gameObject.SetActive(false);
                // This synchronous Edit Mode fixture explicitly exercises the callback body;
                // it does not certify Play Mode event dispatch.
                typeof(GhostPreview).GetMethod("OnDisable", Private).Invoke(ghost, null);
                Require(Visual(ghost) == null && previous == null, "Host disable leaked detached visual."); Destroyed(oldMaterials);
                host.gameObject.SetActive(true);
                Require(Visual(ghost) == null, "Host enable revived stale preview.");
                Require(ghost.SetAuthoredEntry(source), "Rebuild before catalog transition failed.");
                previous = Visual(ghost); oldMaterials = Materials(previous);
                // This is the REAL catalog API with a controlled no-asset entry, not SetPlaceholder.
                ghost.SetEntry(new CatalogEntry { id = "authored-preview-fixture", visualPrefabPath = null });
                Require(previous == null && Visual(ghost) != null && Visual(ghost).name == "BuildGhost", "Catalog transition retained authored ghost.");
                Destroyed(oldMaterials);
                ghost.MoveTo(new Vector3(4, 2, 3), 45f);
                Near(ghost.CurrentPosition, new Vector3(4, 2, 3), "Catalog movement regressed");
                previous = Visual(ghost); oldMaterials = Materials(previous);
                ghost.Clear();
                Require(previous == null, "Clear leaked catalog visual."); Destroyed(oldMaterials);
                Require(ghost.SetAuthoredEntry(source), "Rebuild before disappearance failed.");
                previous = Visual(ghost); oldMaterials = Materials(previous);
                Object.DestroyImmediate(source.gameObject); Tick(ghost);
                Require(Visual(ghost) == null && previous == null, "Destroyed source retained ghost."); Destroyed(oldMaterials);
                ghost.Clear(); ghost.Clear();
                Require(material != null && texture != null && mesh != null && ancestorMesh != null, "Preview destroyed source-owned assets.");
                reason = "independent hierarchy vertices, textured tint, refresh/refusal, lifecycle ownership, and actual catalog transition passed (Editor only)";
                return true;
            }
            catch (Exception error) { reason = error.ToString(); return false; }
            finally
            {
                if (ghost != null) ghost.Clear();
                for (int i = owned.Count - 1; i >= 0; --i) if (owned[i] != null) Object.DestroyImmediate(owned[i]);
                if (ownsScene && fixture.IsValid()) EditorSceneManager.CloseScene(fixture, true);
                if (prior.IsValid() && prior.isLoaded) SceneManager.SetActiveScene(prior);
            }
        }

        static T Own<T>(List<Object> list, T value) where T : Object { list.Add(value); return value; }
        static Transform Node(string name, Transform parent, List<Object> owned)
        { var go = Own(owned, new GameObject(name)); go.transform.SetParent(parent, false); return go.transform; }
        static void Pose(Transform t, Vector3 p, Vector3 e, Vector3 s) { t.localPosition = p; t.localRotation = Quaternion.Euler(e); t.localScale = s; }
        static void CopyLocal(Transform a, Transform b) { b.localPosition = a.localPosition; b.localRotation = a.localRotation; b.localScale = a.localScale; }
        static MeshRenderer Render(Transform t, Mesh mesh, Material mat)
        { t.gameObject.AddComponent<MeshFilter>().sharedMesh = mesh; var r = t.gameObject.AddComponent<MeshRenderer>(); r.sharedMaterial = mat; return r; }
        static Mesh MakeMesh(bool ancestor)
        {
            var m = new Mesh { name = ancestor ? "OwnedAncestorMesh" : "OwnedBodyMesh" };
            m.vertices = ancestor ? new[] { Vector3.zero, Vector3.right * 9, Vector3.up * 7 } :
                new[] { new Vector3(-.7f, 0, -.3f), new Vector3(1.1f, .2f, 0), new Vector3(.1f, 1.7f, .8f), new Vector3(0, .1f, -1.2f) };
            m.triangles = ancestor ? new[] { 0, 1, 2 } : new[] { 0, 1, 2, 0, 2, 3 };
            m.RecalculateNormals(); m.RecalculateBounds(); return m;
        }
        static GameObject Visual(GhostPreview ghost) => (GameObject)typeof(GhostPreview).GetField("_visual", Private).GetValue(ghost);
        static Transform Candidate(GhostPreview ghost)
        { var value = typeof(GhostPreview).GetField("_authoredRootCopy", Private).GetValue(ghost); return value is GameObject go ? go.transform : (Transform)value; }
        static void Tick(GhostPreview ghost) => typeof(GhostPreview).GetMethod("Update", Private).Invoke(ghost, null);
        static Transform Find(Transform root, string name)
        { foreach (var t in root.GetComponentsInChildren<Transform>(true)) if (t.name == name) return t; throw new Exception("Missing transform " + name); }
        static Renderer FindRenderer(Transform root, string name)
        { foreach (var r in root.GetComponentsInChildren<Renderer>(true)) if (r.name == name) return r; return null; }
        static void Refused(GhostPreview ghost, Transform bad, GameObject previous, string context)
        { Require(!ghost.SetAuthoredEntry(bad), "Accepted " + context); Require(Visual(ghost) == previous, "Refusal discarded good preview: " + context); }
        static List<Material> Materials(GameObject root)
        { var list = new List<Material>(); foreach (var r in root.GetComponentsInChildren<Renderer>(true)) foreach (var m in r.sharedMaterials) if (!list.Contains(m)) list.Add(m); return list; }
        static void Destroyed(List<Material> materials) { foreach (var m in materials) Require(m == null, "Owned preview material leaked."); }
        static void CheckRenderOnly(GhostPreview ghost, Mesh excluded, Texture texture, int count)
        {
            var root = Visual(ghost);
            Require(root.GetComponentsInChildren<Renderer>(true).Length == count, "Unexpected preview renderer count.");
            foreach (var c in root.GetComponentsInChildren<Component>(true))
                Require(c is Transform || c is MeshFilter || c is MeshRenderer, "Preview copied gameplay component: " + c.GetType().Name);
            foreach (var f in root.GetComponentsInChildren<MeshFilter>(true))
                Require(f.sharedMesh != excluded && f.sharedMesh.vertexCount != excluded.vertexCount, "Ancestor renderer geometry copied.");
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            {
                Require(r.sharedMaterial != null && r.sharedMaterial.mainTexture == texture, "Preview lost texture.");
                Require(r.sharedMaterial.GetFloat("_Surface") == 1 && r.sharedMaterial.GetFloat("_ZWrite") == 0, "Preview not transparent.");
            }
        }
        static void CheckTint(Renderer renderer, Color expected)
        { var block = new MaterialPropertyBlock(); renderer.GetPropertyBlock(block); var actual = block.GetColor("_BaseColor"); Require(((Vector4)actual - (Vector4)expected).sqrMagnitude < .000001f, "Tint mismatch."); }
        static void CompareVertices(Transform expected, Transform actual, Mesh mesh)
        { foreach (var vertex in mesh.vertices) Near(actual.TransformPoint(vertex), expected.TransformPoint(vertex), "Independent vertex mismatch"); }
        static Dictionary<Transform, Matrix4x4> Snapshot(Transform root)
        { var result = new Dictionary<Transform, Matrix4x4>(); foreach (var t in root.GetComponentsInChildren<Transform>(true)) result.Add(t, t.localToWorldMatrix); return result; }
        static void AssertSnapshot(Dictionary<Transform, Matrix4x4> snapshot)
        {
            foreach (var pair in snapshot) for (int i = 0; i < 16; i++)
            { float a = pair.Value[i], b = pair.Key.localToWorldMatrix[i]; Require(Finite(a) && Finite(b) && Mathf.Abs(a - b) < .0001f, "Source transform changed."); }
        }
        static bool Finite(float v) => !float.IsNaN(v) && !float.IsInfinity(v);
        static void Near(Vector3 actual, Vector3 expected, string why)
        { Require(Finite(actual.x) && Finite(actual.y) && Finite(actual.z) && Finite(expected.x) && Finite(expected.y) && Finite(expected.z) && Vector3.Distance(actual, expected) < .002f, why + ": " + actual + " vs " + expected); }
        static void Require(bool value, string why) { if (!value) throw new InvalidOperationException(why); }
    }
}

// Village2PlaceGateCrossings — author a PAIRED HeroLinkCrossing per interior gate.
//
// CONTEXT (RCA 2026-06-21): the input-driven hero is a NavMeshAgent driven by Move()
// (NOT SetDestination), so it CANNOT auto-cross a bare NavMeshLink. The ONLY thing that
// moves it across a gap/island boundary is a PAIRED HeroLinkCrossing (id-matched warp).
// Village2's interior gates ("ChokepointGate", etc.) had NO crossing pairs, and the one
// legacy pair ("village2_gate") was mis-placed ~1m apart so it self-cancels (the hero is
// in range of BOTH ends -> never leaves range -> _crossArmed never re-arms). HeroLocomotion
// re-arms only after the hero leaves the radius of ALL crossings, so the two ends of a pair
// MUST be > 2*enterRadius apart. enterRadius=2.5f -> ends placed ~4.5m each side (>6m total).
//
// Scheme (per gate):
//   * find through-axis (transform.forward, or the longer horizontal bounds extent)
//   * place marker A ~4.5m one side, marker B ~4.5m the other side, each snapped to navmesh
//   * unique crossingId = "v2_" + sanitized gate name; bidirectional; enterRadius 2.5
//   * verify the two ends land on DIFFERENT navmesh islands (log it; place either way)
//
// Adds the runtime component by reflection (DeNelle.Editor can't ref DeNelle.Village —
// same exemption as Village2PlaceCrossing/Village2Playable). Idempotent: deletes any
// pre-existing markers with a crossingId before (re)creating its pair.
// Run: DeNelle.Editor.Village2PlaceGateCrossings.Run  (EDITOR CLOSED)
using System;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

namespace DeNelle.Editor
{
    public static class Village2PlaceGateCrossings
    {
        private const string ScenePath  = "Assets/Scenes/Village2.unity";
        private const string TypeName   = "DeNelle.Village.HeroLinkCrossing";
        private const string ContainerName = "GateCrossings";
        private const float  SideOffset = 4.5f;   // each end this far from gate center -> >6m total (>2*2.5)
        private const float  SampleRadius = 6f;   // navmesh snap radius
        private const float  EnterRadius = 2.5f;

        [MenuItem("Defenders/Village2/Place Gate Crossings (all gates)")]
        public static void Run()
        {
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

            // Bake navmesh so SamplePosition / CalculatePath are valid this run.
            try { UnityEditor.AI.NavMeshBuilder.BuildNavMesh(); }
            catch (Exception e) { Debug.LogWarning("[V2GateX] BuildNavMesh failed: " + e.Message); }

            var type = FindType(TypeName);
            if (type == null)
            {
                Debug.LogError("[V2GateX] HeroLinkCrossing type not found (is DeNelle.Village compiled?). Aborting.");
                return;
            }

            // Stronghold root (gates live under it). Fall back to whole-scene scan if missing.
            GameObject root = null;
            foreach (var r in scene.GetRootGameObjects())
                if (r.name == "StrongholdRoot") { root = r; break; }
            if (root == null)
                Debug.LogWarning("[V2GateX] StrongholdRoot not found — scanning all scene roots for gates.");

            // Container at scene root for the new pairs. WIPE any prior container first so a
            // re-run is a clean slate (removes spurious pairs from an earlier broader match —
            // e.g. torch/light decorations that previously slipped the gate filter).
            var existing = GameObject.Find(ContainerName);
            if (existing != null)
            {
                Debug.Log($"[V2GateX] wiping prior '{ContainerName}' container (clean re-run).");
                UnityEngine.Object.DestroyImmediate(existing);
            }
            var container = new GameObject(ContainerName);

            // Build the navmesh island map ONCE (for the same-island connectivity check).
            var islands = MapIslands();
            Debug.Log($"[V2GateX] navmesh islands mapped: {islands.Count}");

            // Collect gate transforms (name contains "Gate", case-insensitive), excluding our markers.
            var gates = new List<Transform>();
            CollectGates(root, scene, gates);
            Debug.Log($"[V2GateX] gates found: {gates.Count}");

            int placed = 0;
            foreach (var gate in gates)
            {
                try
                {
                    if (PlaceForGate(gate, type, container, islands)) placed++;
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[V2GateX] gate '{(gate != null ? gate.name : "<null>")}' threw, skipping: {e.Message}");
                }
            }

            // Fix the legacy "village2_gate" pair spacing if present.
            try { FixLegacyPair(); }
            catch (Exception e) { Debug.LogWarning("[V2GateX] legacy fix threw: " + e.Message); }

            EditorSceneManager.MarkAllScenesDirty();
            bool saved = EditorSceneManager.SaveOpenScenes();
            // WO-1731: this tool bakes (BuildNavMesh at :48) before placing crossings, so the open
            // scenes carry a rewritten m_NavMeshData. Reporting `saved=False` in a log line and
            // carrying on leaves a scene pointing at a navmesh it never persisted -- which ships
            // as no navmesh, silently. THROW (RaidNavBake.cs:109-111).
            if (!saved)
                throw new InvalidOperationException(
                    "[V2GateX] could not save navigation for the open scene(s) -- the bake would " +
                    "leave a scene pointing at a navmesh it never persisted (WO-1731).");
            Debug.Log($"[V2GateX] DONE. gates={gates.Count} pairs_placed={placed} saved={saved}.");
        }

        // ── per-gate placement ────────────────────────────────────────────────
        private static bool PlaceForGate(Transform gate, Type type, GameObject container, List<List<Vector3>> islands)
        {
            if (gate == null) return false;
            string id = SanitizeId(gate.name);
            if (string.IsNullOrEmpty(id)) { Debug.LogWarning($"[V2GateX] gate '{gate.name}' -> empty id, skipping."); return false; }

            // Idempotent: remove any pre-existing markers with this crossingId.
            DeleteMarkersWithId(type, id);

            Vector3 center = gate.position;

            // Through-axis: prefer transform.forward; if degenerate, use the longer horizontal bounds extent.
            Vector3 axis = FlatNorm(gate.forward);
            if (axis.sqrMagnitude < 0.001f) axis = AxisFromBounds(gate);
            if (axis.sqrMagnitude < 0.001f) axis = Vector3.forward;

            Vector3 rawA = center + axis * SideOffset;
            Vector3 rawB = center - axis * SideOffset;

            if (!Snap(rawA, out Vector3 a))
            {
                Debug.LogWarning($"[V2GateX] gate '{gate.name}' side A failed to sample navmesh @ {rawA} — skipping gate (no half-pair).");
                return false;
            }
            if (!Snap(rawB, out Vector3 b))
            {
                Debug.LogWarning($"[V2GateX] gate '{gate.name}' side B failed to sample navmesh @ {rawB} — skipping gate (no half-pair).");
                return false;
            }

            int islA = IslandOf(a, islands);
            int islB = IslandOf(b, islands);
            bool sameIsland = (islA >= 0 && islA == islB);

            MakeMarker(container, type, $"Crossing_{id}_A", a, id);
            MakeMarker(container, type, $"Crossing_{id}_B", b, id);

            if (sameIsland)
                Debug.Log($"[V2GateX] WARNING same island — gate '{gate.name}' pair '{id}' both on island[{islA}] (crossing is harmless).");

            Debug.Log($"[V2GateX] placed pair '{id}' A={a} B={b} sameIsland={sameIsland}");
            return true;
        }

        // ── legacy "village2_gate" spacing fix ────────────────────────────────
        private static void FixLegacyPair()
        {
            var type = FindType(TypeName);
            if (type == null) return;
            var markers = MarkersWithId(type, "village2_gate");
            if (markers.Count < 2)
            {
                Debug.Log($"[V2GateX] legacy 'village2_gate' pair: found {markers.Count} marker(s) — not re-spacing.");
                return;
            }
            var m0 = markers[0];
            var m1 = markers[1];
            float d = Vector3.Distance(m0.transform.position, m1.transform.position);
            if (d >= 6f)
            {
                Debug.Log($"[V2GateX] legacy 'village2_gate' already spaced {d:F1}m — no change.");
                return;
            }

            // Re-space along the line between them (or +Z if coincident), snapped to navmesh.
            Vector3 mid = (m0.transform.position + m1.transform.position) * 0.5f;
            Vector3 dir = FlatNorm(m1.transform.position - m0.transform.position);
            if (dir.sqrMagnitude < 0.001f) dir = Vector3.forward;

            if (Snap(mid + dir * SideOffset, out Vector3 p0)) m0.transform.position = p0;
            else Debug.LogWarning("[V2GateX] legacy re-space: side 0 off navmesh, left as-is.");
            if (Snap(mid - dir * SideOffset, out Vector3 p1)) m1.transform.position = p1;
            else Debug.LogWarning("[V2GateX] legacy re-space: side 1 off navmesh, left as-is.");

            float nd = Vector3.Distance(m0.transform.position, m1.transform.position);
            Debug.Log($"[V2GateX] legacy 'village2_gate' re-spaced from {d:F1}m -> {nd:F1}m (A={m0.transform.position} B={m1.transform.position}).");
        }

        // ── gate collection ───────────────────────────────────────────────────
        private static void CollectGates(GameObject root, Scene scene, List<Transform> outGates)
        {
            if (root != null)
            {
                CollectGatesRecursive(root.transform, outGates);
                return;
            }
            foreach (var r in scene.GetRootGameObjects())
                CollectGatesRecursive(r.transform, outGates);
        }

        private static void CollectGatesRecursive(Transform t, List<Transform> outGates)
        {
            if (t == null) return;
            string n = t.name;
            string ln = n != null ? n.ToLowerInvariant() : "";
            bool isGate = ln.Contains("gate");
            bool isMarker = n != null && n.StartsWith("Crossing_");   // our own markers
            bool inContainer = t.parent != null && t.parent.name == ContainerName;
            // EXCLUDE decorations whose name happens to contain "gate" (TorchGate, TorchLightAnchorGate,
            // ...). A crossing on a torch would teleport the hero when they walk past it — a hazard.
            // Only real passage gates qualify (Chokepoint/Main/Spawn).
            bool isDecoration = ln.Contains("torch") || ln.Contains("light") || ln.Contains("anchor") ||
                                ln.Contains("lamp") || ln.Contains("brazier") || ln.Contains("banner") ||
                                ln.Contains("fire") || ln.Contains("glow") || ln.Contains("decor") ||
                                ln.Contains("flame") || ln.Contains("candle");
            if (isGate && !isMarker && !inContainer && !isDecoration)
                outGates.Add(t);
            for (int i = 0; i < t.childCount; i++)
                CollectGatesRecursive(t.GetChild(i), outGates);
        }

        // ── marker creation / deletion ────────────────────────────────────────
        private static void MakeMarker(GameObject container, Type type, string name, Vector3 pos, string id)
        {
            var go = new GameObject(name);
            if (container != null) go.transform.SetParent(container.transform, true);
            go.transform.position = pos;
            go.tag = "Untagged";   // default tag is fine
            var comp = go.AddComponent(type);
            var so = new SerializedObject(comp);
            SetStr(so, "crossingId", id);
            SetFloat(so, "enterRadius", EnterRadius);
            SetBool(so, "bidirectional", true);
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void DeleteMarkersWithId(Type type, string id)
        {
            var markers = MarkersWithId(type, id);
            foreach (var m in markers)
                if (m != null) UnityEngine.Object.DestroyImmediate(m.gameObject);
        }

        private static List<Component> MarkersWithId(Type type, string id)
        {
            var result = new List<Component>();
            var all = UnityEngine.Object.FindObjectsByType(type);
            foreach (var o in all)
            {
                var c = o as Component;
                if (c == null) continue;
                var so = new SerializedObject(c);
                var p = so.FindProperty("crossingId");
                if (p != null && p.stringValue == id) result.Add(c);
            }
            return result;
        }

        // ── navmesh helpers ───────────────────────────────────────────────────
        private static bool Snap(Vector3 p, out Vector3 hit)
        {
            if (NavMesh.SamplePosition(p, out NavMeshHit h, SampleRadius, NavMesh.AllAreas))
            { hit = h.position; return true; }
            hit = p;
            return false;
        }

        private static List<List<Vector3>> MapIslands()
        {
            var pts = new List<Vector3>();
            for (float x = -45f; x <= 45f; x += 3f)
                for (float z = -55f; z <= 30f; z += 3f)
                    if (NavMesh.SamplePosition(new Vector3(x, 2f, z), out NavMeshHit h, 3f, NavMesh.AllAreas))
                        pts.Add(h.position);

            var islands = new List<List<Vector3>>();
            foreach (var p in pts)
            {
                bool placed = false;
                foreach (var isl in islands)
                {
                    var path = new NavMeshPath();
                    if (NavMesh.CalculatePath(isl[0], p, NavMesh.AllAreas, path) && path.status == NavMeshPathStatus.PathComplete)
                    { isl.Add(p); placed = true; break; }
                }
                if (!placed) islands.Add(new List<Vector3> { p });
            }
            islands.Sort((a, b) => b.Count.CompareTo(a.Count));
            return islands;
        }

        private static int IslandOf(Vector3 pos, List<List<Vector3>> islands)
        {
            if (!NavMesh.SamplePosition(pos, out NavMeshHit h, SampleRadius, NavMesh.AllAreas)) return -1;
            for (int i = 0; i < islands.Count; i++)
            {
                if (islands[i].Count == 0) continue;
                var path = new NavMeshPath();
                if (NavMesh.CalculatePath(islands[i][0], h.position, NavMesh.AllAreas, path) && path.status == NavMeshPathStatus.PathComplete)
                    return i;
            }
            return -1;
        }

        // ── geometry helpers ──────────────────────────────────────────────────
        private static Vector3 FlatNorm(Vector3 v)
        {
            v.y = 0f;
            return v.sqrMagnitude < 0.0001f ? Vector3.zero : v.normalized;
        }

        // Through-axis from the LONGER horizontal extent of the gate's renderer/collider bounds.
        private static Vector3 AxisFromBounds(Transform gate)
        {
            Bounds? b = null;
            var rends = gate.GetComponentsInChildren<Renderer>();
            foreach (var r in rends)
            {
                if (b == null) b = r.bounds; else { var bb = b.Value; bb.Encapsulate(r.bounds); b = bb; }
            }
            if (b == null)
            {
                var cols = gate.GetComponentsInChildren<Collider>();
                foreach (var c in cols)
                {
                    if (b == null) b = c.bounds; else { var bb = b.Value; bb.Encapsulate(c.bounds); b = bb; }
                }
            }
            if (b == null) return Vector3.zero;
            var size = b.Value.size;
            // The gate is a WALL with a gap; the through-axis is perpendicular to the wall's
            // long side -> i.e. the SHORTER of x/z is the through direction. Pick accordingly.
            return (size.x <= size.z) ? Vector3.right : Vector3.forward;
        }

        private static string SanitizeId(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return "";
            var sb = new StringBuilder("v2_");
            foreach (char c in raw.ToLowerInvariant())
            {
                if (char.IsLetterOrDigit(c)) sb.Append(c);
                // spaces, parens, punctuation stripped
            }
            return sb.ToString();
        }

        // ── serialized-field setters / reflection ─────────────────────────────
        private static void SetStr(SerializedObject so, string f, string v)  { var p = so.FindProperty(f); if (p != null) p.stringValue = v; }
        private static void SetFloat(SerializedObject so, string f, float v) { var p = so.FindProperty(f); if (p != null) p.floatValue = v; }
        private static void SetBool(SerializedObject so, string f, bool v)   { var p = so.FindProperty(f); if (p != null) p.boolValue = v; }

        private static Type FindType(string full)
        {
            foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
            { var t = a.GetType(full); if (t != null) return t; }
            return null;
        }
    }
}

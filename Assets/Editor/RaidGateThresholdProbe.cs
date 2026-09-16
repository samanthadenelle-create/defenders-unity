using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;

namespace DeNelle.Editor
{
    public static class RaidGateThresholdProbe
    {
        public static void Run()
        {
            var scene = EditorSceneManager.OpenScene("Assets/Scenes/RaidBase_fortified_garrison.unity", OpenSceneMode.Single);
            var gate = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Transform>(true))
                .First(t => t.name == "Gatehouse_south");
            // Navigation bakes render meshes. Temporary mesh colliders expose those same surfaces
            // to a vertical ray even when gameplay deliberately uses only aperture colliders.
            var probes = new System.Collections.Generic.List<GameObject>();
            try
            {
                foreach (var mesh in gate.GetComponentsInChildren<MeshFilter>(true))
                {
                    if (mesh.sharedMesh == null) continue;
                    var probe = new GameObject("Probe_" + mesh.name);
                    probe.transform.SetParent(mesh.transform, false);
                    probe.AddComponent<MeshCollider>().sharedMesh = mesh.sharedMesh;
                    probes.Add(probe);
                }
                Physics.SyncTransforms();
                for (int i = 0; i <= 24; i++)
                {
                    float z = gate.position.z - 3f + i * .25f;
                    var hits = Physics.RaycastAll(new Vector3(0, 2, z), Vector3.down, 3f)
                        .OrderByDescending(h => h.point.y).ToArray();
                    string surfaces = string.Join(";", hits.Select(h => h.collider.name + "@" + h.point.y.ToString("F3")));
                    bool found = NavMesh.SamplePosition(new Vector3(0, .2f, z), out var nav, .3f, NavMesh.AllAreas);
                    Debug.Log("[RaidThreshold] z=" + z.ToString("F3") + " surfaces=" + surfaces +
                        " nav=" + found + " point=" + nav.position.ToString("F3"));
                }
                Debug.Log("RAID_THRESHOLD_PROBE_OK");
            }
            finally
            {
                foreach (var probe in probes) UnityEngine.Object.DestroyImmediate(probe);
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            }
        }
    }
}

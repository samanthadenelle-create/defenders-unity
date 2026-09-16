using System.Collections;
using System.Collections.Generic;
using DeNelle.Village;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

namespace DeNelle.Editor
{
    // Opt-in observer of real combat. It does not damage walls, move units or save the scene.
    public sealed class RaidBreachRuntimeProof : MonoBehaviour
    {
        private struct Site { public Vector3 centre, outside, inside; }
        private readonly Dictionary<WallSegment, Site> _centres = new Dictionary<WallSegment, Site>();
        private NavMeshQueryFilter _filter;

        [MenuItem("Defenders/Raids/Observe Next Combat Breach")]
        public static void Arm()
        {
            if (!Application.isPlaying || !SceneManager.GetActiveScene().name.StartsWith("RaidBase_"))
            { Debug.LogWarning("Enter Play mode in the raid before observing a combat breach."); return; }
            if (FindFirstObjectByType<RaidBreachRuntimeProof>() != null) return;
            new GameObject("RaidBreachRuntimeProof").AddComponent<RaidBreachRuntimeProof>();
        }

        private void Start()
        {
            var settings = NavMesh.GetSettingsByIndex(0);
            _filter = new NavMeshQueryFilter { agentTypeID = settings.agentTypeID, areaMask = NavMesh.AllAreas };
            foreach (var wall in FindObjectsByType<WallSegment>(FindObjectsSortMode.None))
            {
                if (wall.gameObject.scene != gameObject.scene || wall.HpFraction <= 0f) continue;
                var box = wall.GetComponent<BoxCollider>();
                var obstacle = wall.GetComponent<NavMeshObstacle>();
                if (box == null || obstacle == null || !obstacle.enabled || !obstacle.carving) continue;
                Vector3 centre = box.bounds.center;
                centre.y = box.bounds.min.y + .15f;
                // Only count walls actually blocking the sampled centre before the breach.
                if (NavMesh.SamplePosition(centre, out _, .3f, _filter)) continue;
                float distance = Mathf.Abs(box.size.z * wall.transform.lossyScale.z) * .5f + settings.agentRadius + .5f;
                Vector3 offset = wall.transform.forward * distance;
                _centres.Add(wall, new Site { centre = centre, outside = centre - offset, inside = centre + offset });
                wall.Collapsed += Observe;
            }
            Debug.Log("[RaidBreachProof] armed intact blocked walls=" + _centres.Count +
                "; awaiting real combat destruction (no damage injected)");
        }

        private void Observe(WallSegment wall)
        {
            if (_centres.TryGetValue(wall, out var site))
                StartCoroutine(CheckOpening(wall.name, site));
        }

        private IEnumerator CheckOpening(string wallName, Site site)
        {
            // Carving updates on a subsequent navigation frame, not synchronously in Collapse.
            for (int attempt = 0; attempt < 8; attempt++)
            {
                yield return new WaitForSecondsRealtime(.25f);
                if (NavMesh.SamplePosition(site.centre, out var opened, .3f, _filter) &&
                    NavMesh.SamplePosition(site.outside, out var outside, .4f, _filter) &&
                    NavMesh.SamplePosition(site.inside, out var inside, .4f, _filter) &&
                    !NavMesh.Raycast(outside.position, inside.position, out _, _filter))
                {
                    Debug.Log("RAID_BREACH_RUNTIME_OK wall=" + wallName + " intact centre blocked -> destroyed cross-wall route clear at " +
                        opened.position + "; delay=" + ((attempt + 1) * .25f) + "s");
                    yield break;
                }
            }
            Debug.LogError("RAID_BREACH_RUNTIME_FAIL wall=" + wallName +
                " cross-wall route still blocked 2s after destruction at " + site.centre);
        }

        private void OnDestroy()
        {
            foreach (var wall in _centres.Keys) if (wall != null) wall.Collapsed -= Observe;
        }
    }
}

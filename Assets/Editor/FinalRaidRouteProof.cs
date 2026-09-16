using System;
using System.Linq;
using System.Reflection;
using DeNelle.Village.World.Camps;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;

namespace DeNelle.Editor
{
    public static class FinalRaidRouteProof
    {
        public static void Run()
        {
            var scene = EditorSceneManager.OpenScene("Assets/Scenes/RaidBase_IronBastion.unity", OpenSceneMode.Single);
            var roots = scene.GetRootGameObjects();
            var transforms = roots.SelectMany(r => r.GetComponentsInChildren<Transform>(true)).ToArray();
            var spawner = roots.SelectMany(r => r.GetComponentsInChildren<RaidGarrisonSpawner>(true)).Single();
            if (spawner.ConfigId != "iron_bastion") throw new InvalidOperationException("Wrong final-tier garrison config.");
            var spire = roots.SelectMany(r => r.GetComponentsInChildren<RaidSpire>(true)).Single();
            // EditMode does not call Awake. Materialize the production hitbox before
            // measuring its attack approach; do not replace it with guessed bounds.
            typeof(RaidSpire).GetMethod("EnsureHittable", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(spire, null);
            var hero = transforms.Single(t => t.name == "HeroStartPoint_PlayerSpawn");
            var staging = transforms.Single(t => t.name == "RaidStagingPoint");
            var boss = transforms.Single(t => t.name == "BossSpawn");
            Physics.SyncTransforms();
            var collider = spire.GetComponentsInChildren<Collider>().FirstOrDefault(c => c.enabled && !c.isTrigger);
            if (collider == null) throw new InvalidOperationException("Final objective lacks a solid collider.");
            var bossPosition = (Vector3)typeof(RaidGarrisonSpawner)
                .GetMethod("SnapToNav", BindingFlags.Static | BindingFlags.NonPublic)
                .Invoke(null, new object[] { boss.position });
            Debug.Log("[FinalRaidRoute] actual boss spawn=" + bossPosition + " objective collider=" + collider.bounds);
            var towardBoss = boss.position - spire.transform.position;
            towardBoss.y = 0f;
            var approach = collider.ClosestPoint(bossPosition) + towardBoss.normalized;
            approach.y = bossPosition.y;
            var settings = NavMesh.GetSettingsByIndex(0);
            var filter = new NavMeshQueryFilter { agentTypeID = settings.agentTypeID, areaMask = NavMesh.AllAreas };
            Check("hero_to_staging", hero.position, staging.position, filter);
            Check("staging_to_boss", staging.position, bossPosition, filter);
            Check("boss_to_objective", bossPosition, approach, filter);
            Debug.Log("FINAL_RAID_ROUTE_OK saved entry/staging/boss/objective paths and final-tier spawner; runtime victory/return untested");
        }

        private static void Check(string label, Vector3 start, Vector3 end, NavMeshQueryFilter filter)
        {
            const float maxSnap = 1.5f;
            foreach (var point in new[] { start, end })
                if (NavMesh.SamplePosition(point, out var nearest, 12f, filter))
                    Debug.Log("[FinalRaidRoute] " + label + " endpoint=" + point + " nearest=" + nearest.position + " distance=" + nearest.distance);
            if (!NavMesh.SamplePosition(start, out var a, maxSnap, filter) ||
                !NavMesh.SamplePosition(end, out var b, maxSnap, filter))
                throw new InvalidOperationException(label + " endpoint is off saved navigation: " + start + " -> " + end);
            var path = new NavMeshPath();
            if (!NavMesh.CalculatePath(a.position, b.position, filter, path) || path.status != NavMeshPathStatus.PathComplete)
                throw new InvalidOperationException(label + " route incomplete: " + path.status);
            Debug.Log("[FinalRaidRoute] " + label + " complete corners=" + path.corners.Length + " from=" + a.position + " to=" + b.position);
        }
    }
}

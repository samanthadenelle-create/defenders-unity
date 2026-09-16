using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

namespace DeNelle.Village.World.Camps
{
    public static class OwnedTownNavigation
    {
        // Query the live mesh for every agent filter actually present. This checks the town
        // entry, staging and central courtyard; it is not proof of every possible combat route.
        public static bool Validate(Scene scene, out string reason)
        {
            var markers = new Dictionary<string, Vector3>();
            var filters = new Dictionary<string, NavMeshQueryFilter>();
            foreach (var root in scene.GetRootGameObjects())
            {
                foreach (var node in root.GetComponentsInChildren<Transform>(true))
                    if (node.name == "HeroStartPoint_PlayerSpawn" || node.name == "RaidStagingPoint" || node.name == "BossSpawn")
                    {
                        if (markers.ContainsKey(node.name)) { reason = "Town navigation markers are duplicated."; return false; }
                        markers.Add(node.name, node.position);
                    }
                foreach (var agent in root.GetComponentsInChildren<NavMeshAgent>(false))
                    if (agent.enabled)
                        filters[agent.agentTypeID + ":" + agent.areaMask] = new NavMeshQueryFilter {
                            agentTypeID = agent.agentTypeID, areaMask = agent.areaMask
                        };
            }
            if (markers.Count != 3 || filters.Count == 0)
            { reason = "Wait for the town hero and navigation markers before moving a tower."; return false; }
            foreach (var filter in filters.Values)
            {
                if (!NavMesh.SamplePosition(markers["HeroStartPoint_PlayerSpawn"], out var entry, 1.5f, filter))
                { reason = "The town entrance is not reachable for an active character."; return false; }
                foreach (string name in new[] { "RaidStagingPoint", "BossSpawn" })
                {
                    var path = new NavMeshPath();
                    // BossSpawn is authored below the raised courtyard. Match the actual
                    // spawner's 8m seat search, but forbid a distant horizontal substitute.
                    float seatRadius = name == "BossSpawn" ? 8f : 1.5f;
                    bool seated = NavMesh.SamplePosition(markers[name], out var destination, seatRadius, filter) &&
                        Vector2.Distance(new Vector2(markers[name].x, markers[name].z), new Vector2(destination.position.x, destination.position.z)) <= 1.5f;
                    if (!seated ||
                        !NavMesh.CalculatePath(entry.position, destination.position, filter, path) || path.status != NavMeshPathStatus.PathComplete)
                    {
                        DeNelle.Core.Diagnostics.FlowTrace.Warn("OwnedTown", "Navigation refused " + name + " agent=" + filter.agentTypeID +
                            " marker=" + markers[name] + " seated=" + seated + " destination=" + destination.position + " path=" + path.status);
                        reason = "Keep a complete path from the entrance to the town courtyard."; return false;
                    }
                }
            }
            reason = null; return true;
        }
    }
}

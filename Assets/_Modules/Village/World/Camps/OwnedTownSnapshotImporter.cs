using System.Collections.Generic;
using DeNelle.Core.State;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DeNelle.Village.World.Camps
{
    /// <summary>Imports detached structural match data. Never reads or writes player progression.</summary>
    public static class OwnedTownSnapshotImporter
    {
        public static bool TryImport(Scene scene, ArenaBuildSnapshot source, out string reason)
        {
            if (!scene.IsValid() || !scene.isLoaded || (scene.name != OwnedTownScenePose.SceneName &&
                scene.name != DeNelle.Core.Combat.PracticeCombatPolicy.SceneName))
            { reason = "Snapshot import requires the isolated owned template scene."; return false; }
            if (source == null)
            { reason = "A validated structural snapshot is required."; return false; }
            var manifest = OwnedTownTemplateManifest.Load();
            if (!source.TryValidateAndCopy(new OwnedTownLayoutSnapshot(manifest),
                out var snapshot, out reason)) return false;

            var targets = new List<Transform>(snapshot.structures.Count);
            var inherited = new List<OwnedBaseStructure>();
            // Resolve the entire template before any pose, faction or damage mutation.
            foreach (var record in snapshot.structures)
            {
                if (record.inheritedPose == null) { targets.Add(null); continue; }
                if (!OwnedTownScenePose.TryResolve(scene, record.inheritedPose, out var target, out reason)) return false;
                int handlers = (target.GetComponent<WallSegment>() != null ? 1 : 0) +
                    (target.GetComponent<DefenseTower>() != null ? 1 : 0) +
                    (target.GetComponent<RaidSpire>() != null ? 1 : 0);
                if (handlers != 1)
                { reason = "The captured structure requires exactly one condition component."; return false; }
                targets.Add(target);
                inherited.Add(record);
            }
            foreach (var root in scene.GetRootGameObjects())
                if (root.name == "OwnedTownConstruction")
                { reason = "Reload the town before importing another construction revision."; return false; }
            var construction = new GameObject("OwnedTownConstruction");
            construction.SetActive(false);
            SceneManager.MoveGameObjectToScene(construction, scene);
            // Keep this pure occupancy helper inactive so importing practice never steals the global build grid.
            var gridHost = new GameObject("ConstructionGrid");
            gridHost.SetActive(false); gridHost.transform.SetParent(construction.transform, false);
            var grid = gridHost.AddComponent<PlacementGrid>();
            grid.origin = new Vector3(-45f, 0f, -45f); grid.cellSize = 3f; grid.gridWidth = grid.gridHeight = 36;
            try
            {
                // Stage all new bodies before touching captured geometry. Story timers/vendors are excluded.
                for (int i = 0; i < snapshot.structures.Count; i++)
                {
                    var record = snapshot.structures[i];
                    if (record.inheritedPose != null || record.retired) continue;
                    var placed = BaseLayoutLoader.SpawnForLayout(record.placement, grid, construction.transform);
                    if (placed == null || (placed.GetComponent<WallSegment>() == null && placed.GetComponent<DefenseTower>() == null))
                        throw new System.InvalidOperationException("Construction did not create its condition component: " + record.instanceId);
                    var identity = placed.gameObject.AddComponent<OwnedTownPlacedIdentity>();
                    identity.InstanceId = record.instanceId;
                    targets[i] = placed.transform;
                }
            }
            catch (System.Exception failure)
            {
                if (Application.isPlaying) Object.Destroy(construction); else Object.DestroyImmediate(construction);
                reason = failure.Message; return false;
            }
            if (!OwnedTownScenePose.TryRestoreInherited(scene, inherited, out reason))
            {
                if (Application.isPlaying) Object.Destroy(construction); else Object.DestroyImmediate(construction);
                return false;
            }
            SceneOwnership.SetEnemyOwned(false);
            construction.SetActive(true);
            for (int i = 0; i < targets.Count; i++)
            {
                var target = targets[i];
                var record = snapshot.structures[i];
                if (record.retired) { if (target != null) target.gameObject.SetActive(false); continue; }
                if (target == null) continue;
                if (record.inheritedPose != null)
                {
                    var identity = target.GetComponent<OwnedTownPlacedIdentity>() ?? target.gameObject.AddComponent<OwnedTownPlacedIdentity>();
                    identity.InstanceId = record.instanceId;
                    var template = manifest.entries.Find(e =>
                        !string.IsNullOrEmpty(record.inheritedPose.templateStructureId)
                            ? e.structure.inheritedPose.templateStructureId == record.inheritedPose.templateStructureId
                            : e.structure.inheritedPose.sourcePath == record.inheritedPose.sourcePath && e.structure.inheritedPose.sourceName == record.inheritedPose.sourceName);
                    if (template != null && template.movableTower)
                    {
                        var placed = target.GetComponent<PlacedStructure>() ?? target.gameObject.AddComponent<PlacedStructure>();
                        placed.itemId = record.placement.itemId; placed.level = record.placement.level;
                        if (record.placement.level > template.structure.placement.level)
                            BuildModeController.ApplyTierStats(placed, placed.level);
                    }
                }
                float condition = snapshot.structures[i].condition01;
                var wall = target.GetComponent<WallSegment>();
                var tower = target.GetComponent<DefenseTower>();
                if (wall != null) wall.RestoreOwnedTownCondition(condition);
                else if (tower != null) tower.RestoreOwnedTownCondition(condition);
                else target.GetComponent<RaidSpire>().RestoreOwnedTownCondition(condition);
                if (record.constructionPending)
                    UnderConstructionVisual.Attach(target.gameObject, OwnedTownJobKey.Compose(snapshot.buildId, record.instanceId),
                        scene.name == DeNelle.Core.Combat.PracticeCombatPolicy.SceneName);
            }
            reason = null;
            return true;
        }
    }
}

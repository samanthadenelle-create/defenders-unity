using System;
using System.Collections.Generic;
using DeNelle.Core.State;
using DeNelle.Village;
using DeNelle.Village.World.Camps;
using Newtonsoft.Json;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace DeNelle.Editor
{
    public static class OwnedTownScenePoseProof
    {
        public static void Run()
        {
            var scene = EditorSceneManager.OpenScene("Assets/Scenes/RaidBase_IronBastion.unity", OpenSceneMode.Single);
            var records = new List<OwnedBaseStructure>();
            var targets = new List<Transform>();
            var worldPositions = new List<Vector3>();
            foreach (var root in scene.GetRootGameObjects())
                foreach (var node in root.GetComponentsInChildren<Transform>(true))
                    if (node.GetComponent<WallSegment>() != null || node.GetComponent<DefenseTower>() != null ||
                        node.GetComponent<RaidSpire>() != null)
                    {
                        var pose = OwnedTownScenePose.Capture(node);
                        records.Add(new OwnedBaseStructure { instanceId = "source:" + pose.sourcePath, inheritedPose = pose });
                        targets.Add(node); worldPositions.Add(node.position);
                    }
            Require(records.Count > 10, "Saved final raid did not supply a meaningful structure census.");
            var restored = JsonConvert.DeserializeObject<List<OwnedBaseStructure>>(JsonConvert.SerializeObject(records));
            foreach (var node in targets)
            {
                node.localPosition += new Vector3(.125f, .25f, .375f);
                node.localRotation = Quaternion.Euler(10, 20, 30);
                node.localScale = Vector3.one;
            }
            Vector3 beforeFailedRestore = targets[0].localPosition;
            string lastId = restored[restored.Count - 1].inheritedPose.templateStructureId;
            restored[restored.Count - 1].inheritedPose.templateStructureId = "MissingTemplateObject";
            Require(!OwnedTownScenePose.TryRestoreInherited(scene, restored, out _), "Missing identity was accepted.");
            Require(targets[0].localPosition == beforeFailedRestore, "Failed restore partially moved the scene.");
            restored[restored.Count - 1].inheritedPose.templateStructureId = lastId;
            Require(OwnedTownScenePose.TryRestoreInherited(scene, restored, out var reason), reason);
            for (int i = 0; i < records.Count; i++)
            {
                var expected = records[i].inheritedPose;
                var actual = OwnedTownScenePose.Capture(targets[i]);
                Require(JsonConvert.SerializeObject(expected) == JsonConvert.SerializeObject(actual),
                    "Local transform did not round-trip for " + expected.sourcePath);
                Require(Vector3.Distance(targets[i].position, worldPositions[i]) < .0001f,
                    "World placement changed for " + expected.sourcePath);
            }
            // No SaveScene: this proof exercises an in-memory scene, preserving the approved file.
            Require(DeNelle.Editor.Regression.OwnedBaseStateRegression.Run(out var stateReport), stateReport);
            Debug.Log("OWNED_BASE_STATE_OK " + stateReport);
            // EditMode does not run BeforeSceneLoad. Use the actual runtime catalog bootstrap.
            typeof(CatalogBootstrap).GetMethod("Register", System.Reflection.BindingFlags.NonPublic |
                System.Reflection.BindingFlags.Static).Invoke(null, null);
            var census = new RaidCaptureCensus(scene);
            var devastated = new RaidCaptureCensus(scene).Settle();
            foreach (var item in devastated.structures) item.condition01 = 0f;
            Require(OwnedTownRepairService.TryChooseFirstRepair(devastated, out var firstRepair, out var allowance, out var selectionError), selectionError);
            Require(firstRepair.placement.itemId.StartsWith("tower_", StringComparison.Ordinal),
                "Total destruction must restore a tower for the design lesson.");
            devastated.repairSupplies = allowance;
            devastated.milestoneFlags = OwnedBaseMilestones.OwnershipRevealed;
            Require(OwnedBaseProgression.TryRepair(devastated, firstRepair.instanceId, allowance, out var restoredTower, out selectionError), selectionError);
            Require(restoredTower.structures.Find(s => s.instanceId == firstRepair.instanceId).condition01 == 1f,
                "Capture allowance cannot restore the selected tower.");
            Require(restoredTower.structures.FindAll(s => s.condition01 == 0f).Count == devastated.structures.Count - 1,
                "First repair changed unrelated ruins.");
            WallSegment damaged = null;
            DefenseTower destroyed = null;
            foreach (var target in targets)
            {
                if (damaged == null) damaged = target.GetComponent<WallSegment>();
                if (destroyed == null) destroyed = target.GetComponent<DefenseTower>();
            }
            Require(damaged != null && destroyed != null, "Capture damage fixtures are absent.");
            string wallId = OwnedTownScenePose.Capture(damaged.transform).templateStructureId;
            string towerId = OwnedTownScenePose.Capture(destroyed.transform).templateStructureId;
            damaged.ApplyContactDamage(5f);
            float condition = damaged.HpFraction;
            Require(condition > 0 && condition < 1, "Damage fixture did not actually damage the saved wall.");
            UnityEngine.Object.DestroyImmediate(destroyed.gameObject);
            Require(!census.TryCommit(null, 3, out _), "Absent save service accepted capture.");
            var captured = census.Settle();
            Require(captured.structures.Count == records.Count, "Destroyed structure vanished from capture.");
            Require(captured.structures.Find(s => s.inheritedPose.templateStructureId == towerId).condition01 == 0,
                "Destroyed turret was not retained as a ruin.");
            Require(captured.structures.Find(s => s.inheritedPose.templateStructureId == wallId).condition01 == condition,
                "Captured wall condition differs from actual damage.");
            string settledJson = JsonConvert.SerializeObject(captured);
            damaged.ApplyContactDamage(5f);
            Require(JsonConvert.SerializeObject(census.Settle()) == settledJson,
                "Save retry changed victory damage or replenished supplies.");
            captured.structures[0].inheritedPose.x++;
            Require(JsonConvert.SerializeObject(census.Settle()) == settledJson, "Capture result aliases the settled ledger.");
            // Reopen the saved template so a separate actual census observes total destruction.
            scene = EditorSceneManager.OpenScene("Assets/Scenes/RaidBase_IronBastion.unity", OpenSceneMode.Single);
            var totalLossCensus = new RaidCaptureCensus(scene);
            var doomed = new List<GameObject>();
            foreach (var root in scene.GetRootGameObjects())
                foreach (var node in root.GetComponentsInChildren<OwnedTemplateIdentity>(true)) doomed.Add(node.gameObject);
            foreach (var node in doomed) if (node != null) UnityEngine.Object.DestroyImmediate(node);
            var totalLoss = totalLossCensus.Settle();
            Require(totalLoss.structures.TrueForAll(s => s.condition01 == 0f), "Total-loss census concealed destroyed structures.");
            Require(OwnedTownRepairService.TryChooseFirstRepair(totalLoss, out var fundedTower, out var fundedQuote, out selectionError), selectionError);
            totalLoss.milestoneFlags = OwnedBaseMilestones.OwnershipRevealed;
            Require(OwnedBaseProgression.TryRepair(totalLoss, fundedTower.instanceId, fundedQuote, out _, out selectionError),
                "Actual total-loss capture did not fund its tower repair: " + selectionError);
            Debug.Log("OWNED_TOWN_RESTORATION_EDGES_OK pristine inspection and total-loss funded tower; other ruins preserved");
            Debug.Log("RAID_CAPTURE_CENSUS_OK structures=" + records.Count +
                " destroyed turret retained; actual wall damage retained; unavailable-save retry frozen; clone isolated");
            Debug.Log("OWNED_TOWN_POSE_OK structures=" + records.Count +
                " exact local transform round-trip; world placement retained; missing template is atomic; no scene saved");
        }

        private static void Require(bool condition, string reason)
        {
            if (!condition) throw new InvalidOperationException(reason);
        }
    }
}

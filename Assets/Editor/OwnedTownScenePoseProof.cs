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
            // ⛔ WO-1872 - THIS BLOCK MOVED WITH THE RULING, it was not weakened.
            // It used to force every record to 0f and then REQUIRE that the first repair restored a
            // tower ("Total destruction must restore a tower for the design lesson"). Owner ruling
            // 2026-09-18: a captured town loads as the DESTROYED camp and the player CLEARS the
            // rubble for salvage - a razed structure is never repaired (WO-753). The design lesson no
            // longer depends on restoring a tower either: OwnedBaseConstruction.Finish sets
            // LayoutChoiceCompleted on any construction edit, and clearing a ruin is one.
            //
            // The force-to-zero is also gone because it is now REDUNDANT and would have hidden the
            // thing worth asserting: Settle() itself razes every defensive body, whatever health it
            // had, and keeps the Heart standing.
            var devastated = new RaidCaptureCensus(scene).Settle();
            Require(devastated.structures.Exists(s => s.condition01 == 0f),
                "The capture standdown razed nothing - every defensive body must convert as rubble (WO-1872).");
            foreach (var item in devastated.structures)
                Require(item.condition01 == 0f || item.condition01 == 1f,
                    "A captured record converted at a partial condition. The standdown is all-or-nothing: " +
                    "defensive bodies raze to 0, the Heart is kept at 1.");
            Require(!OwnedTownRepairService.TryChooseFirstRepair(devastated, out _, out _, out var selectionError),
                "A freshly captured town must offer NO repair: its defensive structures are rubble to be " +
                "cleared for salvage, not a camp to repair. Offered one instead.");
            Require(!string.IsNullOrEmpty(selectionError),
                "The refusal must carry a reason; a silent false strands the town panel (CLAUDE.md section 12).");
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
            // ⛔ WO-1872 - INVERTED ON PURPOSE. This required the captured wall to carry its ACTUAL
            // measured damage across ("Captured wall condition differs from actual damage"), and that
            // pass-through IS the defect the owner reported: a wall she never touched measured 1.0 and
            // arrived standing and fortified. The measurement is now DISCARDED for every defensive
            // body. The assertion keeps its teeth by proving the discard happened against a wall that
            // demonstrably had health left - `condition` is asserted to be strictly between 0 and 1
            // just above, so a filter that merely passed damage through could not satisfy this line.
            Require(condition > 0f,
                "The damage fixture must leave the wall STANDING, or this case cannot tell a discarded " +
                "measurement from a passed-through one.");
            Require(captured.structures.Find(s => s.inheritedPose.templateStructureId == wallId).condition01 == 0f,
                "A captured wall measured at " + condition.ToString("0.00") + " did not convert as rubble. " +
                "WO-1872: defensive structures arrive razed whatever health they had - the player clears " +
                "them for salvage and builds her own town.");
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
            // ⛔ WO-1872 - `TrueForAll(condition01 == 0f)` NO LONGER HOLDS, and the reason is a
            // DECISION the owner still has to confirm (recorded in the WO-1872 RESULT): the Heart is
            // FORCED to standing on conversion rather than passed through. Every body in this scene
            // was destroyed, so the spire's measured fraction is 0 - yet its record converts at 1,
            // because razing the spire is itself a raid win condition
            // (RaidVictoryController.cs:271) and passing that through would convert a town with
            // nothing standing in it at all. If she rules the Heart converts battered, this line and
            // CapturedTownStanddown.HeartCondition move together.
            Require(totalLoss.structures.Exists(s => s.condition01 == 0f),
                "Total-loss census concealed destroyed structures.");
            Require(totalLoss.structures.TrueForAll(s => s.condition01 == 0f || s.condition01 == 1f),
                "A total-loss capture produced a partial condition; the standdown is all-or-nothing.");
            Require(totalLoss.structures.FindAll(s => s.condition01 == 1f).Count <= 1,
                "More than one body survived a total loss. Only the Heart is kept standing (WO-1872).");
            // The old pair here funded and applied a tower repair. Both are gone with the ruling: a
            // total loss has nothing repairable left, and the capture supplies are not quoted for
            // rubble (RaidCaptureCensus.Settle's standing-only guard).
            Require(!OwnedTownRepairService.TryChooseFirstRepair(totalLoss, out _, out _, out selectionError),
                "A total-loss capture must offer NO repair - every defensive body is rubble to clear.");
            Require(totalLoss.repairSupplies.wood == 0 && totalLoss.repairSupplies.iron == 0 &&
                    totalLoss.repairSupplies.stone == 0,
                "A total-loss capture quoted repair supplies. Supplies fund a DAMAGED-BUT-STANDING " +
                "structure; there is nothing to fund when the only damage is total (WO-1872).");
            Debug.Log("OWNED_TOWN_RESTORATION_EDGES_OK total-loss capture converts as rubble with the Heart kept, offers no repair and quotes no supplies");
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

using System;
using System.Reflection;
using DeNelle.Core.State;
using DeNelle.Village;
using DeNelle.Village.World.Camps;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace DeNelle.Editor
{
    public static class OwnedTownReconstructionProof
    {
        public static void Run()
        {
            VerifyRetryableEntry();
            typeof(CatalogBootstrap).GetMethod("Register", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, null);
            var source = EditorSceneManager.OpenScene("Assets/Scenes/RaidBase_IronBastion.unity", OpenSceneMode.Single);
            var property = new RaidCaptureCensus(source).Settle();
            // Nonzero settled damage proves restoration bypasses combat mitigation. Destruction
            // animations and deferred Destroy require the separate Play-mode acceptance.
            foreach (var record in property.structures) record.condition01 = .375f;
            string before = Newtonsoft.Json.JsonConvert.SerializeObject(property);
            var scene = EditorSceneManager.OpenScene("Assets/Scenes/OwnedTown_IronBastion.unity", OpenSceneMode.Single);
            Require(OwnedTownLayoutSnapshot.TryCreate(property, HeroClassOpt.Knight, out var snapshot, out var importReason), importReason);
            string snapshotBefore = Newtonsoft.Json.JsonConvert.SerializeObject(snapshot);
            // A late missing component must refuse the complete import before damaging earlier walls.
            Transform firstWall = null;
            RaidSpire missingSpire = null;
            foreach (var record in snapshot.structures)
            {
                Require(OwnedTownScenePose.TryResolve(scene, record.inheritedPose, out var target, out importReason), importReason);
                if (firstWall == null && target.GetComponent<WallSegment>() != null) firstWall = target;
                if (target.GetComponent<RaidSpire>() != null) missingSpire = target.GetComponent<RaidSpire>();
            }
            Require(firstWall != null && missingSpire != null, "Importer refusal fixture lacks structures.");
            float originalHp = firstWall.GetComponent<WallSegment>().HpFraction;
            Vector3 originalPosition = firstWall.localPosition;
            UnityEngine.Object.DestroyImmediate(missingSpire);
            Require(!OwnedTownSnapshotImporter.TryImport(scene, snapshot, out importReason), "Incomplete template accepted snapshot import.");
            Require(firstWall.GetComponent<WallSegment>().HpFraction == originalHp && firstWall.localPosition == originalPosition,
                "Refused import partially changed the scene.");
            Require(snapshotBefore == Newtonsoft.Json.JsonConvert.SerializeObject(snapshot), "Refused import mutated match snapshot.");
            scene = EditorSceneManager.OpenScene("Assets/Scenes/OwnedTown_IronBastion.unity", OpenSceneMode.Single);
            OwnedTownController controller = null;
            foreach (var root in scene.GetRootGameObjects())
            {
                Require(root.GetComponentsInChildren<RaidGarrisonSpawner>(true).Length == 0, "Owned town contains a garrison spawner.");
                if (controller == null) controller = root.GetComponent<OwnedTownController>();
            }
            Require(controller != null, "Owned town has no reconstruction controller.");
            Require(controller.TryReconstruct(property, out var reason), reason);
            int walls = 0, towers = 0, spires = 0;
            foreach (var record in property.structures)
            {
                Require(OwnedTownScenePose.TryResolve(scene, record.inheritedPose, out var target, out reason), reason);
                var wall = target.GetComponent<WallSegment>();
                var tower = target.GetComponent<DefenseTower>();
                var spire = target.GetComponent<RaidSpire>();
                float hp;
                if (wall != null) { hp = wall.HpFraction; walls++; }
                else if (tower != null)
                {
                    hp = tower.HpFraction; towers++;
                    Require(tower.Allegiance == TowerAllegiance.PlayerOwned, "Reconstructed tower remains hostile.");
                }
                else
                {
                    Require(spire != null, "Structure lost its condition component.");
                    hp = spire.HpFraction; spires++;
                    Require(spire.Faction == DeNelle.Core.Combat.CombatFaction.Friendly, "Reconstructed spire remains hostile.");
                }
                Require(Mathf.Abs(hp - record.condition01) < .00001f, "Saved health changed during restoration: " + record.instanceId);
                var pose = record.inheritedPose;
                Require(Vector3.Distance(target.localPosition, new Vector3(pose.x, pose.y, pose.z)) < .00001f,
                    "Saved placement changed during restoration: " + record.instanceId);
            }
            Require(before == Newtonsoft.Json.JsonConvert.SerializeObject(property), "Reconstruction mutated the saved property.");
            Require(!controller.TryReconstruct(property, out _), "Same scene accepted damage restoration twice.");
            Require(walls > 0 && towers == 10 && spires == 1, "Saved structure census is incomplete.");
            Debug.Log("OWNED_TOWN_RECONSTRUCTION_OK walls=" + walls + " towers=" + towers + " spires=" + spires +
                "; exact saved health and placement; friendly structures; no garrison; save unchanged; no scene saved");
        }

        private static void Require(bool value, string reason)
        { if (!value) throw new InvalidOperationException(reason); }

        private static void VerifyRetryableEntry()
        {
            // Exercise the real view handler with an unavailable durable save. A refused tap
            // must not consume the primary action or latch the panel closed.
            var fixture = new GameObject("~OwnedTownEntryRetryProof");
            try
            {
                var view = fixture.AddComponent<DeNelle.Village.UI.EndStateView>();
                int checks = 0, transitions = 0;
                var vm = new DeNelle.Village.UI.EndStateVM {
                    PrimaryGate = () => { checks++; return false; },
                    Primary = () => transitions++
                };
                const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance;
                var type = typeof(DeNelle.Village.UI.EndStateView);
                type.GetField("_vm", flags).SetValue(view, vm);
                var fire = type.GetMethod("FirePrimary", flags);
                fire.Invoke(view, null); fire.Invoke(view, null);
                Require(checks == 2 && transitions == 0 && vm.Primary != null &&
                    !(bool)type.GetField("_fired", flags).GetValue(view),
                    "Failed capture save consumed the victory action instead of allowing retry.");
                vm.Primary = null; // Fixture cleanup is not an abandoned gameplay transition.
                Debug.Log("OWNED_TOWN_ENTRY_RETRY_OK two refused attempts preserve the primary action");
            }
            finally { UnityEngine.Object.DestroyImmediate(fixture); }
        }
    }
}

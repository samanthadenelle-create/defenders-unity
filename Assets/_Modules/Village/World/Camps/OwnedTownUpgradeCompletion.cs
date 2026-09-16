using DeNelle.Core.State;
using DeNelle.Core.Catalog;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DeNelle.Village.World.Camps
{
    public static class OwnedTownUpgradeCompletion
    {
        // Runs before the queue removes the job. The saved level is the replay receipt if
        // the process ends after this write but before the later queue-cleanup write.
        public static bool TryCommit(BuildJobData job, out string reason)
        {
            if (!TryPlan(job, out var next, out reason)) return false;
            if (next == null) return true;
            if (!GameStateService.Instance.TryCommitOwnedBaseRevision(next, out reason)) return false;
            RefreshLive(job);
            return true;
        }

        public static bool TryPlan(BuildJobData job, out OwnedBaseState next, out string reason)
        {
            next = null;
            reason = null;
            if (!OwnedTownJobKey.TryParse(job.StructureId, out var baseId, out var instanceId) ||
                (job.Type != BuildJobType.Upgrade && job.Type != BuildJobType.Build) ||
                (job.Type == BuildJobType.Upgrade ? job.TargetTier < 2 : job.TargetTier != 1))
            { reason = "Owned-town upgrade job is invalid."; return false; }
            var service = GameStateService.Instance;
            var property = service?.State?.OwnedBase;
            if (property == null || property.baseId != baseId)
            { reason = "Upgrade property does not match the owned town."; return false; }
            var record = property.structures.Find(s => s.instanceId == instanceId);
            if (record == null || record.retired)
            { reason = "The upgrade target is missing or sold."; return false; }
            if (job.Type == BuildJobType.Build)
            {
                if (record.inheritedPose != null || record.placement.level != 1)
                { reason = "The construction job does not match a new structure."; return false; }
                if (!record.constructionPending) return true; // durable completion receipt
                return OwnedBaseConstruction.TryCompleteBuild(property, property.revision, instanceId, out next, out reason) &&
                    OwnedTownLayoutSnapshot.TryCreate(next, service.State.HeroClass, out _, out reason);
            }
            var entry = CatalogRegistry.Get(record.placement.itemId);
            int maximum = Buildings.Progression.PlacedStructureUpgradeService.MaxLevelFor(entry);
            if (job.TargetTier > maximum) { reason = "Upgrade exceeds the catalog maximum."; return false; }
            if (record.placement.level >= job.TargetTier) return true;
            if (record.placement.level != job.TargetTier - 1 ||
                !OwnedBaseConstruction.TryUpgrade(property, property.revision, instanceId, record.placement.level, maximum, out next, out reason))
            { reason = reason ?? "The upgrade does not follow the saved level."; return false; }
            return OwnedTownLayoutSnapshot.TryCreate(next, service.State.HeroClass, out _, out reason);
        }

        public static void RefreshLive(BuildJobData job)
        {
            if (!OwnedTownJobKey.TryParse(job.StructureId, out var baseId, out var instanceId)) return;
            var property = GameStateService.Instance?.State?.OwnedBase;
            if (property == null || property.baseId != baseId) return;
            var record = property.structures.Find(s => s.instanceId == instanceId);
            if (record == null) return;
            // Practice keeps its frozen opponent build even when the player's timer completes elsewhere.
            var scene = SceneManager.GetActiveScene();
            if (scene.name == OwnedTownScenePose.SceneName && OwnedTownScenePose.TryResolveStructure(scene, record, out var target, out _))
                DeNelle.Core.Diagnostics.Guard.Try("OwnedTown", "upgrade live stats", () => {
                    var placed = target.GetComponent<PlacedStructure>() ?? target.gameObject.AddComponent<PlacedStructure>();
                    placed.itemId = record.placement.itemId; placed.level = job.TargetTier;
                    BuildModeController.ApplyTierStats(placed, placed.level);
                });
        }
    }
}

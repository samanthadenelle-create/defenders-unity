using DeNelle.Core.Catalog;
using DeNelle.Core.State;
using CoreCost = DeNelle.Core.Catalog.ResourceCost;

namespace DeNelle.Village.World.Camps
{
    /// <summary>Owned-town repair pricing reuses the existing catalog and repair cost authority.</summary>
    public static class OwnedTownRepairService
    {
        // Capture funding and the first repair button must select the same actual ruin.
        // When every tower fell, restoring a tower also makes the design lesson possible.
        public static bool TryChooseFirstRepair(OwnedBaseState property, out OwnedBaseStructure target,
            out CoreCost quote, out string reason)
        {
            target = null; quote = default;
            if (!OwnedBaseProgression.Validate(property, out reason)) return false;
            var manifest = OwnedTownTemplateManifest.Load();
            if (manifest == null) { reason = "The town construction manifest is unavailable."; return false; }
            bool needsTower = (property.milestoneFlags & OwnedBaseMilestones.EssentialRepairCompleted) == 0 &&
                !property.structures.Exists(s => manifest.IsEditableTower(s) && s.condition01 > 0f);
            long cheapest = long.MaxValue;
            foreach (var record in property.structures)
            {
                if (record.retired || record.condition01 >= 1f || (needsTower && !manifest.IsEditableTower(record))) continue;
                if (!TryQuote(record, out var candidate, out reason)) return false;
                long total = (long)candidate.wood + candidate.iron + candidate.stone;
                if (total >= cheapest) continue;
                cheapest = total; target = record; quote = candidate;
            }
            reason = target == null ? "No eligible damaged structure is available." : null;
            return target != null;
        }

        public static bool TryQuote(OwnedBaseStructure structure, out CoreCost quote, out string reason)
        {
            quote = default;
            reason = null;
            if (structure == null) { reason = "The captured structure is missing."; return false; }
            if (structure.retired) { reason = "A sold structure cannot be repaired."; return false; }
            var entry = CatalogRegistry.Get(structure.placement.itemId);
            if (entry == null || entry.repo == null)
            { reason = "The captured structure has no catalog repair cost."; return false; }
            quote = WallRepairController.CostForFraction(1f - structure.condition01, entry.repo.cost);
            return true;
        }

        // Callers may update live damage and tutorial presentation only after this returns true.
        // The property envelope contains the repair, supply spend and lesson completion together.
        public static bool TryRepair(string instanceId, out string reason)
        {
            var service = GameStateService.Instance;
            var current = service != null ? service.State?.OwnedBase : null;
            if (current == null) { reason = "No personal town is owned."; return false; }
            if (!OwnedBaseProgression.Validate(current, out reason)) return false;
            var structure = current.structures.Find(s => s.instanceId == instanceId);
            if (!TryQuote(structure, out var quote, out reason)) return false;
            var wallet = new CoreCost { wood = service.State.Wood, iron = service.State.Iron, stone = service.State.Resources.Stone };
            if (!OwnedBaseProgression.TryRepair(current, instanceId, quote, wallet, out var proposal, out _, out reason)) return false;
            if (!OwnedTownLayoutSnapshot.TryCreate(proposal, service.State.HeroClass, out _, out reason)) return false;
            return service.TryCommitOwnedBaseRepair(instanceId, quote, out reason);
        }
    }
}

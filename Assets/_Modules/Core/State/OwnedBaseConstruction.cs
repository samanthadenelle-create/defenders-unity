using DeNelle.Core.Catalog;

namespace DeNelle.Core.State
{
    /// <summary>
    /// Detached construction edits. The gameplay adapter must validate catalog eligibility,
    /// plot/navigation, price and timers before atomically committing an edit with its payment.
    /// These operations never write a save, charge a wallet or mutate the source property.
    /// </summary>
    public static class OwnedBaseConstruction
    {
        public static bool TryAdd(OwnedBaseState current, int expectedRevision, string instanceId,
            PlacedStructureData placement, out OwnedBaseState next, out string reason)
        {
            next = null;
            if (!CanEdit(current, expectedRevision, out reason)) return false;
            if (!OwnedBaseProgression.Token(instanceId) || current.structures.Exists(s => s.instanceId == instanceId))
                return OwnedBaseProgression.Fail("Construction identity is missing or was already used.", out reason);
            if (placement.level != 1)
                return OwnedBaseProgression.Fail("New construction must start at level one.", out reason);
            var candidate = current.Clone();
            candidate.structures.Add(new OwnedBaseStructure { instanceId = instanceId, placement = placement });
            return Finish(candidate, out next, out reason);
        }

        public static bool TryBeginBuild(OwnedBaseState current, int expectedRevision, string instanceId,
            PlacedStructureData placement, out OwnedBaseState next, out string reason)
        {
            if (!TryAdd(current, expectedRevision, instanceId, placement, out next, out reason)) return false;
            next.structures.Find(s => s.instanceId == instanceId).constructionPending = true;
            return OwnedBaseProgression.Validate(next, out reason);
        }

        public static bool TryCompleteBuild(OwnedBaseState current, int expectedRevision, string instanceId,
            out OwnedBaseState next, out string reason)
        {
            next = null;
            if (!CanEdit(current, expectedRevision, out reason)) return false;
            var target = current.structures.Find(s => s.instanceId == instanceId);
            if (target == null || target.retired || !target.constructionPending || target.inheritedPose != null || target.placement.level != 1)
                return OwnedBaseProgression.Fail("This construction is missing or already completed.", out reason);
            var candidate = current.Clone();
            candidate.structures.Find(s => s.instanceId == instanceId).constructionPending = false;
            return Finish(candidate, out next, out reason);
        }

        public static bool TryUpgrade(OwnedBaseState current, int expectedRevision, string instanceId,
            int expectedLevel, int catalogMaximum, out OwnedBaseState next, out string reason)
        {
            next = null;
            if (!CanEdit(current, expectedRevision, out reason)) return false;
            var target = current.structures.Find(s => s.instanceId == instanceId);
            if (target == null || target.retired || target.constructionPending || target.condition01 < 1f)
                return OwnedBaseProgression.Fail("Repair a standing structure before upgrading it.", out reason);
            if (target.placement.level != expectedLevel || expectedLevel >= catalogMaximum || catalogMaximum < 1)
                return OwnedBaseProgression.Fail("The upgrade level changed or reached its catalog limit.", out reason);
            var candidate = current.Clone();
            var changed = candidate.structures.Find(s => s.instanceId == instanceId);
            changed.placement.level++;
            return Finish(candidate, out next, out reason);
        }

        public static bool TryMoveGrid(OwnedBaseState current, int expectedRevision, string instanceId,
            int cellX, int cellZ, out OwnedBaseState next, out string reason)
        {
            next = null;
            if (!CanEdit(current, expectedRevision, out reason)) return false;
            var target = current.structures.Find(s => s.instanceId == instanceId);
            if (target == null || target.inheritedPose != null || target.retired || target.constructionPending || target.condition01 <= 0f)
                return OwnedBaseProgression.Fail("Choose a standing constructed structure.", out reason);
            if (target.placement.cellX == cellX && target.placement.cellZ == cellZ)
                return OwnedBaseProgression.Fail("Choose a different grid position before saving.", out reason);
            var candidate = current.Clone();
            var changed = candidate.structures.Find(s => s.instanceId == instanceId);
            changed.placement.cellX = cellX;
            changed.placement.cellZ = cellZ;
            return Finish(candidate, out next, out reason);
        }

        public static bool TryRetire(OwnedBaseState current, int expectedRevision, string instanceId,
            out OwnedBaseState next, out string reason)
        {
            next = null;
            if (!CanEdit(current, expectedRevision, out reason)) return false;
            var target = current.structures.Find(s => s.instanceId == instanceId);
            if (target == null || target.retired)
                return OwnedBaseProgression.Fail("This structure is missing or already sold.", out reason);
            var candidate = current.Clone();
            var changed = candidate.structures.Find(s => s.instanceId == instanceId);
            changed.retired = true;
            changed.constructionPending = false;
            changed.condition01 = 0f;
            return Finish(candidate, out next, out reason);
        }

        private static bool CanEdit(OwnedBaseState current, int expectedRevision, out string reason)
        {
            if (!OwnedBaseProgression.Validate(current, out reason)) return false;
            if (current.revision != expectedRevision || current.revision == int.MaxValue)
                return OwnedBaseProgression.Fail("The town changed; reopen construction before confirming.", out reason);
            if ((current.milestoneFlags & OwnedBaseMilestones.EssentialRepairCompleted) == 0)
                return OwnedBaseProgression.Fail("Complete the first repair before construction.", out reason);
            return true;
        }

        private static bool Finish(OwnedBaseState candidate, out OwnedBaseState next, out string reason)
        {
            next = null;
            candidate.revision++;
            candidate.milestoneFlags |= OwnedBaseMilestones.LayoutChoiceCompleted;
            if (!OwnedBaseProgression.Validate(candidate, out reason)) return false;
            next = candidate;
            return true;
        }
    }
}

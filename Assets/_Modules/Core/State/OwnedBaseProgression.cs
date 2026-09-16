using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using DeNelle.Core.Catalog;

namespace DeNelle.Core.State
{
    /// <summary>Pure local PvE progression. No wallet, scene, clock, network, or count-based unlock.</summary>
    public static class OwnedBaseProgression
    {
        public const string FinalRaidId = "iron_bastion";
        private const OwnedBaseMilestones AllMilestones = OwnedBaseMilestones.OwnershipRevealed |
            OwnedBaseMilestones.EssentialRepairCompleted | OwnedBaseMilestones.LayoutChoiceCompleted |
            OwnedBaseMilestones.PracticeCompleted;

        public static bool IsAiArenaReady(OwnedBaseState state) => Validate(state, out _) &&
            (state.milestoneFlags & AllMilestones) == AllMilestones;

        /// <summary>Owner 2026-09-11: capture is a 3-star clear of the highest raid, not a 20-win counter.</summary>
        public const int CaptureStarsRequired = 3;

        // Call only from the actual victory settlement. The caller-supplied completion is not
        // independently authenticated by this local function; never use it for ranked rewards.
        public static bool TryCapture(OwnedBaseState existing, string completedRaidId,
            string completionReceiptId, OwnedBaseState template, int stars, out OwnedBaseState result, out string reason)
        {
            result = null;
            if (completedRaidId != FinalRaidId || !Token(completionReceiptId))
                return Fail("A completed final-tier raid receipt is required.", out reason);
            // Retries (existing != null) already passed the star gate at first commit.
            if (existing == null && stars < CaptureStarsRequired)
                return Fail("A three-star clear of the highest raid is required.", out reason);
            if (existing != null)
            {
                if (existing.sourceRaidId != completedRaidId || existing.captureReceiptId != completionReceiptId)
                    return Fail("A personal base is already owned; capture cannot replace it.", out reason);
                if (!Validate(existing, out reason)) return false;
                result = existing.Clone(); return true; // retry never restores spent supplies
            }
            if (template == null) return Fail("Captured template is missing.", out reason);
            var next = template.Clone();
            next.sourceRaidId = completedRaidId;
            next.captureReceiptId = completionReceiptId;
            next.suppliesReceiptId = completionReceiptId;
            next.revision = 1;
            next.milestoneFlags = OwnedBaseMilestones.None;
            next.reenteredLayoutRevision = 0;
            if (!Validate(next, out reason)) return false;
            result = next; return true;
        }

        // The gameplay adapter invokes this only after a successful service outcome. A dismissed
        // coach mark is not a repair, layout choice, or practice completion.
        public static bool TryCompleteMilestone(OwnedBaseState current, OwnedBaseMilestones milestone,
            out OwnedBaseState result, out string reason)
        {
            result = null;
            if (!Validate(current, out reason)) return false;
            OwnedBaseMilestones required;
            switch (milestone)
            {
                case OwnedBaseMilestones.OwnershipRevealed: required = OwnedBaseMilestones.None; break;
                case OwnedBaseMilestones.EssentialRepairCompleted: required = OwnedBaseMilestones.OwnershipRevealed; break;
                case OwnedBaseMilestones.LayoutChoiceCompleted: required = OwnedBaseMilestones.OwnershipRevealed | OwnedBaseMilestones.EssentialRepairCompleted; break;
                case OwnedBaseMilestones.PracticeCompleted:
                    if (current.reenteredLayoutRevision == 0)
                        return Fail("Save and reenter the designed town before completing practice.", out reason);
                    required = AllMilestones & ~OwnedBaseMilestones.PracticeCompleted; break;
                default: return Fail("One known milestone is required.", out reason);
            }
            if ((current.milestoneFlags & required) != required) return Fail("Previous practice steps are incomplete.", out reason);
            var next = current.Clone();
            if ((next.milestoneFlags & milestone) == 0)
            {
                if (next.revision == int.MaxValue) return Fail("Base revision limit reached.", out reason);
                next.milestoneFlags |= milestone; next.revision++;
            }
            result = next; reason = null; return true;
        }

        public static bool TryInspectPristineTown(OwnedBaseState current,
            out OwnedBaseState result, out string reason)
        {
            result = null;
            if (!Validate(current, out reason)) return false;
            if (current.structures.Exists(s => !s.retired && s.condition01 < 1f))
                return Fail("Damaged structures must be repaired before completing this inspection.", out reason);
            return TryCompleteMilestone(current, OwnedBaseMilestones.EssentialRepairCompleted, out result, out reason);
        }

        /// <summary>
        /// Applies the gameplay repair authority's quote to the isolated capture supplies.
        /// The caller commits the returned revision before updating visuals or raising tutorial signals.
        /// </summary>
        public static bool TryRepair(OwnedBaseState current, string instanceId, ResourceCost quote,
            out OwnedBaseState result, out string reason)
            => TryRepair(current, instanceId, quote, default, out result, out _, out reason);

        /// <summary>Use capture supplies first, then a quoted material debit from the normal wallet.</summary>
        public static bool TryRepair(OwnedBaseState current, string instanceId, ResourceCost quote,
            ResourceCost availableWallet, out OwnedBaseState result, out ResourceCost walletDebit, out string reason)
        {
            result = null; walletDebit = default;
            if (!Validate(current, out reason)) return false;
            if ((current.milestoneFlags & OwnedBaseMilestones.OwnershipRevealed) == 0)
                return Fail("Reveal the captured town before repairing it.", out reason);
            if (quote.wood < 0 || quote.iron < 0 || quote.stone < 0 || quote.crystals != 0)
                return Fail("The repair quote is invalid.", out reason);
            if (availableWallet.wood < 0 || availableWallet.iron < 0 || availableWallet.stone < 0)
                return Fail("The resource wallet is invalid.", out reason);
            int index = current.structures.FindIndex(s => s.instanceId == instanceId);
            if (index < 0) return Fail("The captured structure could not be found.", out reason);
            if (current.structures[index].retired) return Fail("A sold structure cannot be repaired.", out reason);
            if (current.structures[index].constructionPending) return Fail("Finish construction before repairing this structure.", out reason);
            if (current.structures[index].condition01 >= 1f)
                return Fail("This structure is already repaired.", out reason);
            var supplies = current.repairSupplies;
            var debit = RepairWalletDebit(supplies, quote);
            if (availableWallet.wood < debit.wood || availableWallet.iron < debit.iron || availableWallet.stone < debit.stone)
                return Fail("There are not enough materials for this repair.", out reason);
            if (current.revision == int.MaxValue) return Fail("Base revision limit reached.", out reason);
            var next = current.Clone();
            next.repairSupplies.wood -= quote.wood - debit.wood;
            next.repairSupplies.iron -= quote.iron - debit.iron;
            next.repairSupplies.stone -= quote.stone - debit.stone;
            next.structures[index].condition01 = 1f;
            next.milestoneFlags |= OwnedBaseMilestones.EssentialRepairCompleted;
            next.revision++;
            result = next; walletDebit = debit; reason = null; return true;
        }

        public static ResourceCost RepairWalletDebit(ResourceCost supplies, ResourceCost quote) => new ResourceCost {
            wood = Math.Max(0, quote.wood - supplies.wood),
            iron = Math.Max(0, quote.iron - supplies.iron),
            stone = Math.Max(0, quote.stone - supplies.stone)
        };

        /// <summary>Call after the town adapter successfully reconstructs this persisted revision on entry.</summary>
        public static bool TryConfirmReentry(OwnedBaseState current, OwnedBaseState reconstructed,
            out OwnedBaseState result, out string reason)
        {
            result = null;
            if (!Validate(current, out reason) || !Validate(reconstructed, out reason)) return false;
            if ((current.milestoneFlags & OwnedBaseMilestones.LayoutChoiceCompleted) == 0)
                return Fail("The layout lesson has not been completed.", out reason);
            if (JsonConvert.SerializeObject(current) != JsonConvert.SerializeObject(reconstructed))
                return Fail("The reconstructed town does not match the current saved revision.", out reason);
            var next = current.Clone();
            if (next.reenteredLayoutRevision == 0)
            {
                if (next.revision == int.MaxValue) return Fail("Base revision limit reached.", out reason);
                next.reenteredLayoutRevision = next.revision;
                next.revision++;
            }
            result = next; reason = null; return true;
        }

        /// <summary>Structural save validation. Gameplay publication additionally requires catalog/plot authority.</summary>
        public static bool Validate(OwnedBaseState state, out string reason)
        {
            if (state == null) return Fail("Owned base is missing.", out reason);
            if (state.schemaVersion != OwnedBaseState.CurrentSchemaVersion || state.revision < 1)
                return Fail("Unsupported owned-base schema or revision.", out reason);
            if (!Token(state.baseId) || !Token(state.templateVersion) || !Token(state.captureReceiptId) ||
                state.sourceRaidId != FinalRaidId || state.suppliesReceiptId != state.captureReceiptId)
                return Fail("Owned-base identity or capture receipt is invalid.", out reason);
            if ((state.milestoneFlags & ~AllMilestones) != 0)
                return Fail("Unknown owned-base milestone.", out reason);
            if (state.reenteredLayoutRevision < 0 || state.reenteredLayoutRevision >= state.revision ||
                (state.reenteredLayoutRevision > 0 && (state.milestoneFlags & OwnedBaseMilestones.LayoutChoiceCompleted) == 0) ||
                (state.reenteredLayoutRevision == 0 && (state.milestoneFlags & OwnedBaseMilestones.PracticeCompleted) != 0))
                return Fail("Owned-base save/reentry milestone is invalid.", out reason);
            int flags = (int)state.milestoneFlags;
            if ((flags & (flags + 1)) != 0) return Fail("Owned-base milestones are out of order.", out reason);
            var cost = state.repairSupplies;
            if (cost.wood < 0 || cost.iron < 0 || cost.stone < 0 || cost.crystals < 0)
                return Fail("Repair supplies cannot be negative.", out reason);
            return ValidateStructures(state.structures, out reason);
        }

        internal static bool ValidateStructures(List<OwnedBaseStructure> structures, out string reason)
        {
            if (structures == null || structures.Count == 0 || structures.Count > 4096)
                return Fail("Structure collection is missing or exceeds the transport bound.", out reason);
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var item in structures)
            {
                if (item == null || !Token(item.instanceId) || !ids.Add(item.instanceId))
                    return Fail("Structure instance IDs must be present and unique.", out reason);
                var p = item.placement;
                if (!Token(p.itemId) || p.level < 1 || p.yawSteps < 0 || p.yawSteps > 3 ||
                    !Finite(p.yawOffset) || !Finite(p.worldY) || !Finite(item.condition01) ||
                    item.condition01 < 0 || item.condition01 > 1 || (item.retired && item.condition01 != 0f) ||
                    (item.constructionPending && (item.retired || item.inheritedPose != null || p.level != 1 || item.condition01 != 1f)))
                    return Fail("Invalid structure placement or condition.", out reason);
                if (item.inheritedPose != null && !ValidatePose(item.inheritedPose, out reason)) return false;
            }
            reason = null; return true;
        }

        public static bool TryChangeInheritedPose(OwnedBaseState current, string instanceId, OwnedStructurePose pose,
            out OwnedBaseState result, out string reason)
        {
            result = null;
            if (!Validate(current, out reason) || !ValidatePose(pose, out reason)) return false;
            if ((current.milestoneFlags & OwnedBaseMilestones.EssentialRepairCompleted) == 0)
                return Fail("Complete the repair lesson before designing the town.", out reason);
            var target = current.structures.Find(s => s.instanceId == instanceId);
            if (target?.inheritedPose == null || target.retired || target.condition01 <= 0)
                return Fail("Choose a standing inherited structure.", out reason);
            var old = target.inheritedPose;
            if (old.sourceScene != pose.sourceScene || old.sourcePath != pose.sourcePath || old.sourceName != pose.sourceName ||
                old.templateStructureId != pose.templateStructureId ||
                JsonConvert.SerializeObject(old.parentFrame) != JsonConvert.SerializeObject(pose.parentFrame) ||
                old.sx != pose.sx || old.sy != pose.sy || old.sz != pose.sz)
                return Fail("Design cannot replace a captured structure or resize its art.", out reason);
            double distance = (double)(pose.x - old.x) * (pose.x - old.x) +
                (double)(pose.y - old.y) * (pose.y - old.y) + (double)(pose.z - old.z) * (pose.z - old.z);
            double dot = Math.Abs((double)old.qx * pose.qx + (double)old.qy * pose.qy +
                (double)old.qz * pose.qz + (double)old.qw * pose.qw);
            if (distance < .25d && dot > .999d)
                return Fail("Move or rotate the structure before saving the design.", out reason);
            if (current.revision == int.MaxValue) return Fail("Base revision limit reached.", out reason);
            var next = current.Clone();
            next.structures.Find(s => s.instanceId == instanceId).inheritedPose = pose.Clone();
            next.milestoneFlags |= OwnedBaseMilestones.LayoutChoiceCompleted;
            next.revision++;
            result = next; reason = null; return true;
        }

        public static bool ValidatePose(OwnedStructurePose pose, out string reason)
        {
            if (pose == null || !Token(pose.sourceScene) || !Token(pose.sourcePath) ||
                string.IsNullOrEmpty(pose.sourceName) || pose.sourceName.Length > 256)
                return Fail("Inherited structure identity is missing or invalid.", out reason);
            foreach (string part in pose.sourcePath.Split('/'))
                if (!int.TryParse(part, out int index) || index < 0)
                    return Fail("Inherited structure hierarchy address is invalid.", out reason);
            if (!string.IsNullOrEmpty(pose.templateStructureId))
            {
                if (!Token(pose.templateStructureId) || pose.parentFrame == null || pose.parentFrame.Length != 16)
                    return Fail("Stable template identity requires its captured coordinate frame.", out reason);
                foreach (float value in pose.parentFrame)
                    if (!Finite(value)) return Fail("Captured coordinate frame is invalid.", out reason);
            }
            if (!Finite(pose.x) || !Finite(pose.y) || !Finite(pose.z) ||
                !Finite(pose.qx) || !Finite(pose.qy) || !Finite(pose.qz) || !Finite(pose.qw) ||
                !Finite(pose.sx) || !Finite(pose.sy) || !Finite(pose.sz) ||
                Math.Abs(pose.sx) < .000001f || Math.Abs(pose.sy) < .000001f || Math.Abs(pose.sz) < .000001f)
                return Fail("Inherited structure pose is non-finite or has zero scale.", out reason);
            double norm = (double)pose.qx * pose.qx + (double)pose.qy * pose.qy +
                (double)pose.qz * pose.qz + (double)pose.qw * pose.qw;
            if (Math.Abs(norm - 1d) > .001d)
                return Fail("Inherited structure rotation is not normalized.", out reason);
            reason = null; return true;
        }

        /// <summary>Same-epoch incoming revisions must not replace another property or rewind local work.</summary>
        public static bool TryAcceptRevision(OwnedBaseState current, OwnedBaseState incoming,
            out OwnedBaseState result, out string reason)
        {
            result = null;
            if (!Validate(incoming, out reason)) return false;
            if (current != null)
            {
                if (current.baseId != incoming.baseId || current.captureReceiptId != incoming.captureReceiptId ||
                    current.sourceRaidId != incoming.sourceRaidId || current.templateVersion != incoming.templateVersion)
                    return Fail("Owned-base identity conflicts with the current property.", out reason);
                if (incoming.revision < current.revision) return Fail("Owned-base revision is stale.", out reason);
                if (incoming.revision == current.revision && JsonConvert.SerializeObject(current) != JsonConvert.SerializeObject(incoming))
                    return Fail("Owned-base content changed without a new revision.", out reason);
                if ((incoming.milestoneFlags & current.milestoneFlags) != current.milestoneFlags)
                    return Fail("Owned-base milestones cannot regress.", out reason);
                if (current.reenteredLayoutRevision > 0 && incoming.reenteredLayoutRevision != current.reenteredLayoutRevision)
                    return Fail("The completed save/reentry lesson cannot be changed.", out reason);
            }
            result = incoming.Clone(); reason = null; return true;
        }

        internal static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        internal static bool Token(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length > 160) return false;
            foreach (char c in value) if (!(char.IsLetterOrDigit(c) || c == '_' || c == '-' || c == '.' || c == ':' || c == '/')) return false;
            return true;
        }
        internal static bool Fail(string message, out string reason) { reason = message; return false; }
    }
}

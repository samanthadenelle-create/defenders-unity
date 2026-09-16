using System;
using System.Collections.Generic;
using DeNelle.Core.Catalog;

namespace DeNelle.Core.State
{
    [Flags]
    public enum OwnedBaseMilestones
    {
        None = 0, OwnershipRevealed = 1, EssentialRepairCompleted = 2,
        LayoutChoiceCompleted = 4, PracticeCompleted = 8
    }

    [Serializable]
    public sealed class OwnedStructurePose
    {
        // Address in the immutable captured scene template, before combat removes objects.
        // Local coordinates retain fitted art scales and parent transforms without grid snapping.
        public string sourceScene;
        public string sourcePath;
        public string sourceName;
        public string templateStructureId;
        // Parent local-to-world frame. Reparenting requires explicit migration, not silent reinterpretation.
        public float[] parentFrame;
        public float x, y, z;
        public float qx, qy, qz, qw = 1f;
        public float sx = 1f, sy = 1f, sz = 1f;
        public OwnedStructurePose Clone()
        {
            var copy = (OwnedStructurePose)MemberwiseClone();
            copy.parentFrame = parentFrame == null ? null : (float[])parentFrame.Clone();
            return copy;
        }
    }

    [Serializable]
    public sealed class OwnedBaseStructure
    {
        public string instanceId;
        public PlacedStructureData placement;
        // Persisted condition, independent of placement and upgrade level. Zero is a repairable ruin.
        public float condition01 = 1f;
        // Null for ordinary grid placements and older additive saves.
        public OwnedStructurePose inheritedPose;
        // Keep captured identities after demolition so template reconstruction cannot resurrect them.
        // Missing in older saves means false. A retired record is never a repairable ruin.
        public bool retired;
        // Additive default false: existing structures are complete. The builder job owns this transition.
        public bool constructionPending;
        public OwnedBaseStructure Clone() => new OwnedBaseStructure
            { instanceId = instanceId, placement = placement, condition01 = condition01,
              inheritedPose = inheritedPose?.Clone(), retired = retired, constructionPending = constructionPending };
    }

    /// <summary>
    /// Additive personal property. Never replaces the story BaseLayout. Receipts describe local
    /// PvE operations, not server-attested wins. Supplies are an isolated repair allowance, not
    /// a second grant into the player's resource wallet. Catalog ResourceCost.stone is the existing
    /// internal axis; this contract does not rename currencies or choose grant amounts.
    /// </summary>
    [Serializable]
    public sealed class OwnedBaseState
    {
        public const int CurrentSchemaVersion = 1;
        public int schemaVersion = CurrentSchemaVersion;
        public string baseId;
        public string sourceRaidId;
        public string templateVersion;
        public string captureReceiptId;
        public int revision = 1;
        public List<OwnedBaseStructure> structures = new List<OwnedBaseStructure>();
        public ResourceCost repairSupplies;
        public string suppliesReceiptId;
        public OwnedBaseMilestones milestoneFlags;
        // Revision reconstructed on entering the town after the layout lesson was saved.
        // Zero means the save/reentry lesson is not complete. Existing milestone bits stay stable.
        public int reenteredLayoutRevision;

        public OwnedBaseState Clone()
        {
            var copy = (OwnedBaseState)MemberwiseClone();
            copy.structures = structures == null ? null : new List<OwnedBaseStructure>(structures.Count);
            if (structures != null) foreach (var item in structures) copy.structures.Add(item?.Clone());
            return copy;
        }
    }
}

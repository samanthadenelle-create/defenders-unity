using System;
using System.Collections.Generic;
using DeNelle.Village.Buildings.Progression;

namespace DeNelle.Editor.Regression
{
    public static class StorageStackPlacementRegression
    {
        public static bool Run(out string reason)
        {
            var failures = new List<string>();
            const float footprint = 2.38f; // shipped lumberyard/foundry/silo footprint
            const float stackDepth = 0.8f;
            var anchor = StorageStackView.StackAnchor(footprint, stackDepth);
            float nearestStackEdge = -anchor.z - stackDepth * 0.5f;
            float structureEdge = footprint * 0.5f;
            if (nearestStackEdge <= structureEdge)
                failures.Add("storage props still intersect the GenericContainer footprint");
            if (Math.Abs(anchor.y - 0.05f) > 0.0001f)
                failures.Add("storage props no longer sit at ground height");

            reason = failures.Count == 0
                ? "STORAGE_STACK_PLACEMENT_OK pallet contents clear the building footprint"
                : "STORAGE_STACK_PLACEMENT_FAIL x" + failures.Count + " :: " + string.Join(" | ", failures);
            return failures.Count == 0;
        }
    }
}

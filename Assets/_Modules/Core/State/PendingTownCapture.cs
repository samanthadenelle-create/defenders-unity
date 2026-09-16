using System;

namespace DeNelle.Core.State
{
    /// <summary>Local capture intent. Ordinary raid rewards are not part of this record.</summary>
    [Serializable]
    public sealed class PendingTownCapture
    {
        public OwnedBaseState capture;
        public PendingTownCapture Clone() => new PendingTownCapture { capture = capture?.Clone() };

        public bool Validate(out string reason)
        {
            if (!OwnedBaseProgression.Validate(capture, out reason)) return false;
            if (capture.revision != 1 || capture.milestoneFlags != OwnedBaseMilestones.None ||
                capture.reenteredLayoutRevision != 0)
            { reason = "Pending capture must contain the untouched victory revision."; return false; }
            return true;
        }
    }
}

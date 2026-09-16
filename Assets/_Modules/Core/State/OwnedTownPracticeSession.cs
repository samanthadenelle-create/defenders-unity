using Newtonsoft.Json;

namespace DeNelle.Core.State
{
    public enum OwnedTownPracticeOutcome { None, Win, Loss, Abandoned }

    /// <summary>One detached local match. No wallet, raid rewards, ranked record or scene dependencies.</summary>
    public sealed class OwnedTownPracticeSession
    {
        private ArenaBuildSnapshot _layout;
        private OwnedBaseState _propertyAtEntry;
        public OwnedTownPracticeOutcome Outcome { get; private set; }
        public bool CompletionSaved { get; private set; }
        public ArenaBuildSnapshot CopyLayout() => _layout?.Clone();

        public static bool TryCreate(OwnedBaseState property, ArenaBuildSnapshot layout,
            IArenaBuildValidationAuthority authority, out OwnedTownPracticeSession session, out string reason)
        {
            session = null;
            if (!OwnedBaseProgression.Validate(property, out reason)) return false;
            const OwnedBaseMilestones required = OwnedBaseMilestones.OwnershipRevealed |
                OwnedBaseMilestones.EssentialRepairCompleted | OwnedBaseMilestones.LayoutChoiceCompleted;
            if ((property.milestoneFlags & required) != required || property.reenteredLayoutRevision < 1)
            { reason = "Repair, design, save and reenter the town before practice."; return false; }
            if (layout == null)
            { reason = "Practice requires a saved build snapshot."; return false; }
            if (!layout.TryValidateAndCopy(authority, out var detached, out reason)) return false;
            if (detached.buildId != property.baseId || detached.revision != property.revision ||
                detached.templateVersion != property.templateVersion ||
                JsonConvert.SerializeObject(detached.structures) != JsonConvert.SerializeObject(property.structures))
            { reason = "The practice snapshot differs from the saved town."; return false; }
            session = new OwnedTownPracticeSession {
                _layout = detached, _propertyAtEntry = property.Clone()
            };
            reason = null; return true;
        }

        // Called by the match controller after actual combat resolution; not by a UI coach mark.
        public bool TryResolve(OwnedTownPracticeOutcome outcome, out string reason)
        {
            if (outcome != OwnedTownPracticeOutcome.Win && outcome != OwnedTownPracticeOutcome.Loss &&
                outcome != OwnedTownPracticeOutcome.Abandoned)
            { reason = "Practice has no completed combat result."; return false; }
            if (Outcome != OwnedTownPracticeOutcome.None && Outcome != outcome)
            { reason = "Practice already has a different result."; return false; }
            Outcome = outcome; reason = null; return true;
        }

        public bool TrySaveCompletion(GameStateService service, out string reason)
        {
            if (CompletionSaved) { reason = null; return true; }
            if (Outcome != OwnedTownPracticeOutcome.Win && Outcome != OwnedTownPracticeOutcome.Loss)
            { reason = "Abandoned or unfinished practice does not complete the lesson."; return false; }
            var current = service?.State?.OwnedBase;
            if (current == null || _propertyAtEntry == null || current.baseId != _propertyAtEntry.baseId ||
                current.captureReceiptId != _propertyAtEntry.captureReceiptId || current.sourceRaidId != _propertyAtEntry.sourceRaidId ||
                current.templateVersion != _propertyAtEntry.templateVersion || current.schemaVersion != _propertyAtEntry.schemaVersion ||
                current.revision < _propertyAtEntry.revision)
            { reason = "This practice result belongs to a different town or an older save."; return false; }
            // The match stays frozen, but builders may finish in the owner's save meanwhile.
            // Merge only the lesson milestone into the current property; never restore the entry snapshot.
            if (!OwnedBaseProgression.TryCompleteMilestone(service.State.OwnedBase, OwnedBaseMilestones.PracticeCompleted,
                out var next, out reason)) return false;
            if (!service.TryCommitOwnedBaseRevision(next, out reason)) return false;
            CompletionSaved = true; reason = null; return true;
        }
    }
}

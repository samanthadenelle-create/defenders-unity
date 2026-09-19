using System;
using DeNelle.Core.Diagnostics;
using DeNelle.Core.State;
using DeNelle.Core.Tutorial;
using UnityEngine;

namespace DeNelle.Village.World.Camps
{
    [DefaultExecutionOrder(-500)]
    public sealed class OwnedTownController : MonoBehaviour
    {
        public bool Reconstructed { get; private set; }
        public string Failure { get; private set; }

        private void Awake()
        {
            if (gameObject.scene.name == OwnedTownScenePose.SceneName) SceneOwnership.SetEnemyOwned(false);
        }

        private void Start()
        {
            var property = GameStateService.Instance?.State?.OwnedBase;
            if (!TryReconstruct(property, out var reason))
            { Failure = reason; FlowTrace.Fail("OwnedTown", "Town reconstruction requires recovery: " + reason); }
            else if ((property.milestoneFlags & OwnedBaseMilestones.LayoutChoiceCompleted) != 0 && property.reenteredLayoutRevision == 0)
            {
                var service = GameStateService.Instance;
                if (!OwnedBaseProgression.TryConfirmReentry(property, property, out var confirmed, out reason) ||
                    service == null || !service.TryCommitOwnedBaseRevision(confirmed, out reason))
                { Failure = reason ?? "Save service is unavailable."; FlowTrace.Warn("OwnedTown", "Reentry confirmation awaits retry: " + Failure); }
                else
                {
                    FlowTrace.Step("OwnedTown", "OWNED_TOWN_REENTRY_SAVED revision=" + confirmed.revision);
                    TutorialSignals.Raise(TutorialSignals.OwnedTownReentered);
                }
            }
            if (Reconstructed)
            {
                // WO-1876 — the rebuild door is the castle HUD/build stack, not OwnedTownPanel.
                // Do not AddComponent/Show the modal after capture or reentry. Fold the old
                // reveal/inspect milestones into first entry when the camp is rubble-only
                // (WO-1872 pristine predicate), so CanEdit unlocks without a repair lesson.
                EnsureCaptureMilestonesForDesign();
                FlowTrace.Step("OwnedTown",
                    "OWNED_TOWN_READY_NO_PANEL reconstructed without OwnedTownPanel.Show — " +
                    "peaceful dock Build + BuildModeController selection are the door (WO-1876).");
            }
        }

        /// <summary>
        /// WO-1876 — without OwnedTownPanel.Begin/Inspect, OwnershipRevealed and
        /// EssentialRepairCompleted never advance, and OwnedBaseConstruction.CanEdit refuses
        /// every clear/build. A razed capture has no repairable damage, so the inspect contract
        /// (TryInspectPristineTown) is the honest unlock; rubble-clear remains the first act.
        /// </summary>
        private void EnsureCaptureMilestonesForDesign()
        {
            var service = GameStateService.Instance;
            if (service?.State?.OwnedBase == null)
            {
                FlowTrace.Warn("OwnedTown", "capture milestone fold skipped — OwnedBase missing after reconstruct.");
                return;
            }

            var property = service.State.OwnedBase;
            if ((property.milestoneFlags & OwnedBaseMilestones.OwnershipRevealed) == 0)
            {
                if (!OwnedBaseProgression.TryCompleteMilestone(property, OwnedBaseMilestones.OwnershipRevealed,
                        out var revealed, out var reason) ||
                    !service.TryCommitOwnedBaseRevision(revealed, out reason))
                {
                    FlowTrace.Warn("OwnedTown", "OwnershipRevealed fold refused: " + (reason ?? "unknown"));
                    return;
                }
                TutorialSignals.Raise(TutorialSignals.OwnedTownRevealed);
                FlowTrace.Step("OwnedTown", "OWNED_TOWN_REVEAL_FOLDED revision=" + revealed.revision);
                property = service.State.OwnedBase;
            }

            if (property == null) return;
            if ((property.milestoneFlags & OwnedBaseMilestones.EssentialRepairCompleted) != 0) return;
            // Default (WO-1876 open question): rubble-clear replaced the repair lesson. Only fold
            // when the town is pristine under CapturedTownStanddown.IsRepairableDamage — never
            // invent a funded repair of a standing body.
            if (property.structures.Exists(CapturedTownStanddown.IsRepairableDamage))
            {
                FlowTrace.Warn("OwnedTown",
                    "EssentialRepairCompleted not folded — repairable damage remains; " +
                    "CanEdit stays gated until that is resolved (out of WO-1876 default path).");
                return;
            }
            if (!OwnedBaseProgression.TryInspectPristineTown(property, out var inspected, out var inspectReason) ||
                !service.TryCommitOwnedBaseRevision(inspected, out inspectReason))
            {
                FlowTrace.Warn("OwnedTown", "pristine inspect fold refused: " + (inspectReason ?? "unknown"));
                return;
            }
            TutorialSignals.Raise(TutorialSignals.OwnedTownRepaired);
            FlowTrace.Step("OwnedTown", "OWNED_TOWN_INSPECT_FOLDED revision=" + inspected.revision +
                "; rubble-clear is the first design act (WO-1876).");
        }

        public bool TryReconstruct(OwnedBaseState property, out string reason)
        {
            if (Reconstructed) { reason = "Town is already reconstructed; reload before applying another revision."; return false; }
            if (gameObject.scene.name != OwnedTownScenePose.SceneName)
            { reason = "Personal property requires its own scene."; return false; }
            if (!OwnedBaseProgression.Validate(property, out reason)) return false;
            if (property.templateVersion != "iron-bastion-20260911")
            { reason = "The captured town requires a different scene template."; return false; }
            var hero = GameStateService.Instance != null ? GameStateService.Instance.State.HeroClass : HeroClassOpt.Knight;
            if (!OwnedTownLayoutSnapshot.TryCreate(property, hero, out var layout, out reason)) return false;
            if (!OwnedTownSnapshotImporter.TryImport(gameObject.scene, layout, out reason)) return false;
            Reconstructed = true; Failure = null; reason = null;
            FlowTrace.Step("OwnedTown", "OWNED_TOWN_RECONSTRUCTED revision=" + property.revision +
                " structures=" + property.structures.Count + "; story BaseLayout untouched");
            return true;
        }
    }
}

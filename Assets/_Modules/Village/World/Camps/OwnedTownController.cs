using System;
using DeNelle.Core.Diagnostics;
using DeNelle.Core.State;
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
                    DeNelle.Core.Tutorial.TutorialSignals.Raise(DeNelle.Core.Tutorial.TutorialSignals.OwnedTownReentered);
                }
            }
            if (Reconstructed)
            {
                var panel = GetComponent<OwnedTownPanel>();
                if (panel == null) panel = gameObject.AddComponent<OwnedTownPanel>();
                panel.Show();
            }
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

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

                // WO-1783 — THE FIRST FRAME OF HER TOWN IS HER TOWN, NOT A MODAL OVER IT.
                //
                // This used to call panel.Show() inline, in Start, on a [DefaultExecutionOrder(-500)]
                // component: the crossfade out of the raid was the ENTIRE presentation of the
                // capture, and the panel was already up on frame one. The town she just won was
                // never actually seen. The panel is still the only route onward, so the hold is a
                // BEAT, never a gate: it always ends in Show().
                //
                // The COMPONENT is added immediately and only the Show is deferred — an editor
                // play-proof or capture harness that finds OwnedTownPanel right after load still
                // finds it (Assets/Editor/OwnedTownMovePlayProof.cs calls Show itself).
                _revealPending = true;
                StartCoroutine(HoldThenReveal(panel));
            }
        }

        /// <summary>
        /// How long her town is hers alone before the panel opens, on the FIRST entry — the capture
        /// reveal. ⚠ THE EXACT DURATION AND THE SHAPE OF THE REVEAL ARE THE OWNER'S CALL (WO-1783
        /// section 4 HOLDS "whether the reveal is a camera pan, a held wide shot, or a sequenced VFX
        /// beat, and how long it runs"). This is a plain hold and one named knob, deliberately not an
        /// invented cinematic; whoever gets that ruling changes this number or replaces the hold.
        /// </summary>
        private const float FirstRevealHoldSeconds = 2f;

        /// <summary>The same beat on a RETURN visit, where the town is already familiar and the panel
        /// is what she came for. Short on purpose: a reveal she has seen is a delay, not a moment.</summary>
        private const float ReturnHoldSeconds = 0.35f;

        private bool _revealPending;

        /// <summary>
        /// Waits the beat, then shows the panel. REALTIME (<see cref="WaitForSecondsRealtime"/>): a
        /// scene that enters with a scaled or zeroed timeScale must not hold the player's only route
        /// onward forever.
        /// </summary>
        private System.Collections.IEnumerator HoldThenReveal(OwnedTownPanel panel)
        {
            var property = GameStateService.Instance?.State?.OwnedBase;
            bool firstReveal = property == null ||
                (property.milestoneFlags & OwnedBaseMilestones.OwnershipRevealed) == 0;
            float hold = firstReveal ? FirstRevealHoldSeconds : ReturnHoldSeconds;

            FlowTrace.Step("OwnedTown", "OWNED_TOWN_REVEAL_HOLD seconds=" + hold +
                " firstReveal=" + firstReveal + "; the panel used to be up on frame one, so the " +
                "capture had no moment at all (WO-1783).");

            yield return new WaitForSecondsRealtime(hold);

            _revealPending = false;
            if (panel == null)
            {
                FlowTrace.Warn("OwnedTown", "reveal hold ended but the town panel is gone — " +
                    "nothing to show. The scene was torn down during the hold.");
                yield break;
            }
            FlowTrace.Step("OwnedTown", "OWNED_TOWN_REVEAL_SHOWN after the hold.");
            Guard.Try("OwnedTown", "show the town panel after the reveal hold", () => panel.Show());
        }

        /// <summary>
        /// Never silent (CLAUDE.md section 12): if this controller is disabled mid-hold the coroutine
        /// dies with it and the panel never opens, so the ONE case that could strand a player says so
        /// in the log. Show() is deliberately NOT called from here — building a modal canvas during
        /// teardown trades a logged fault for an unlogged one.
        /// </summary>
        private void OnDisable()
        {
            if (!_revealPending) return;
            _revealPending = false;
            FlowTrace.Warn("OwnedTown", "the town panel reveal hold was CUT SHORT — this controller " +
                "was disabled before the beat ended, so the panel never opened. If a player reached " +
                "this, she is standing in her town with no panel; re-entering the scene rebuilds it.");
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

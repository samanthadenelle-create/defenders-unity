// =============================================================================
// WallRuinPresenter — the destroyed-wall VISUAL owner (WO-1723 Lane B).
// -----------------------------------------------------------------------------
// Assembly: DeNelle.Village   Namespace: DeNelle.Village
//
// OWNER RULING 2026-09-14 (carried verbatim on WO-1721, restated in WO-1723 §4.3):
//   "if wall is destroyed remove the destroyed wall and replace with a destroyed
//    wall with no colider that I can step over"
//
// So a collapsed raid wall does NOT sink its intact mesh into the floor. It
// SWAPS: the intact clad panel is switched off and a DISTINCT rubble model —
// baked in place, disabled, by RaidBaseDresser.CladRing — is switched on, with a
// LOW step-over collider instead of the segment's full-height blocker.
//
// WHY THIS COMPONENT EXISTS AT ALL (WO-1723 §11.5, measured):
// `WallSegment.Collapsed?.Invoke` had ZERO runtime subscribers — the only two in
// the repo are editor-only proofs. Nothing in a shipped build had ever swapped a
// visual, spawned rubble or opened anything when a wall died. This is that
// subscriber, and it is deliberately a SEPARATE component rather than more code
// inside WallSegment:
//   * WallSegment must not learn the names of dresser-authored children. Looking
//     up "Clad_*" / "Ruin_*" by string from the gameplay component is exactly the
//     implicit coupling WO-1723 §4.1 exists to remove.
//   * The player's own Elarion perimeter walls carry NO presenter, so their
//     legacy collapse sink is untouched by this ticket.
//
// ⚠ THE SINK AND THE SWAP ARE MUTUALLY EXCLUSIVE, AND THAT IS LOAD-BEARING.
// WallSegment.CollapseRoutine drops the whole segment TRANSFORM by
// SinkFraction * renderer-bounds height. Now that the clad panel is a CHILD of
// the segment (WO-1723 §4.1), those bounds are the ~4 m visible wall, so the sink
// would drag this rubble ~3.4 m underground on the very frame it appeared.
// WallSegment.Collapse therefore asks TryGetComponent<WallRuinPresenter>() and
// skips the sink when one answers. Do not re-enable both.
//
// ORDERING (why the collider is re-enabled here rather than just left on):
// WallSegment.Collapse disables EVERY non-trigger collider under the segment
// BEFORE it raises Collapsed — and GetComponentsInChildren<Collider>(true) reaches
// this ruin's step collider even while the ruin GameObject is inactive. So the
// step collider arrives at this handler already disabled and must be explicitly
// re-armed. That ordering is deterministic, not incidental.
// =============================================================================

using UnityEngine;
using DeNelle.Core.Diagnostics;   // FlowTrace / Guard (CLAUDE.md §12)

namespace DeNelle.Village
{
    /// <summary>
    /// Swaps a collapsed <see cref="WallSegment"/> from its intact clad panel to a
    /// baked, initially-disabled rubble model with a low step-over collider.
    /// Authored at bake time by <c>RaidBaseDresser.CladRing</c>; never added at runtime.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class WallRuinPresenter : MonoBehaviour
    {
        private const string Sys = "WallSegment";

        [Header("Wired by RaidBaseDresser at bake time")]
        [Tooltip("The segment whose Collapsed event drives the swap. Defaults to this GameObject's own.")]
        [SerializeField] private WallSegment _segment;

        [Tooltip("The intact visible clad panel. Switched OFF on collapse.")]
        [SerializeField] private Transform _intact;

        [Tooltip("The rubble model, baked disabled at the same footprint. Switched ON on collapse.")]
        [SerializeField] private Transform _ruin;

        [Tooltip("Low step-over collider on the rubble (never a full-height blocker).")]
        [SerializeField] private BoxCollider _ruinStep;

        [Tooltip("Art token the rubble was resolved from - named in the collapse trace so a " +
                 "log read can say WHICH model shipped without opening the scene.")]
        [SerializeField] private string _ruinToken = "";

        [Tooltip("Measured world height of the rubble at bake time (metres).")]
        [SerializeField] private float _ruinHeight;

        private bool _swapped;
        private bool _subscribed;

        /// <summary>True once the swap has run (the section is showing rubble).</summary>
        public bool HasSwapped { get { return _swapped; } }

        /// <summary>The art token the rubble model was resolved from.</summary>
        public string RuinToken { get { return _ruinToken; } }

        /// <summary>
        /// Bake-time wiring. Called by <c>RaidBaseDresser.CladRing</c> only — a runtime
        /// caller would be authoring scene content, which this component never does.
        /// </summary>
        public void Author(WallSegment segment, Transform intact, Transform ruin,
                           BoxCollider ruinStep, string ruinToken, float ruinHeight)
        {
            _segment = segment;
            _intact = intact;
            _ruin = ruin;
            _ruinStep = ruinStep;
            _ruinToken = ruinToken ?? "";
            _ruinHeight = ruinHeight;
        }

        private void Awake()
        {
            if (_segment == null) _segment = GetComponent<WallSegment>();
        }

        private void OnEnable()
        {
            if (_segment == null) _segment = GetComponent<WallSegment>();
            if (_segment == null || _subscribed) return;
            _segment.Collapsed += HandleCollapsed;
            _subscribed = true;

            // A section that was ALREADY down before this component woke (a restored
            // save, a re-enabled object) must still read as rubble, or the swap would
            // be silently skipped for exactly the walls the player already broke.
            if (_segment.IsDestroyed) Swap("already-destroyed at enable");
        }

        private void OnDisable()
        {
            if (_segment == null || !_subscribed) return;
            _segment.Collapsed -= HandleCollapsed;
            _subscribed = false;
        }

        private void HandleCollapsed(WallSegment segment)
        {
            Swap("Collapsed event");
        }

        /// <summary>
        /// The swap itself. Guarded (CLAUDE.md §12: no silent failures) so a missing
        /// reference logs and leaves the wall standing rather than throwing inside the
        /// collapse path and stranding the segment half-dead.
        /// </summary>
        private void Swap(string via)
        {
            if (_swapped) return;
            _swapped = true;

            Guard.Try(Sys, "wall ruin swap", () =>
            {
                string intactName = _intact != null ? _intact.name : "<none>";
                string ruinName = _ruin != null ? _ruin.name : "<none>";

                if (_intact != null) _intact.gameObject.SetActive(false);

                float restY = 0f;
                float stepHeight = 0f;
                int stepColliders = 0;
                if (_ruin != null)
                {
                    _ruin.gameObject.SetActive(true);
                    restY = _ruin.position.y;
                }
                if (_ruinStep != null)
                {
                    // Re-armed deliberately: WallSegment.Collapse disabled it a few lines
                    // before this handler ran (see the header's ORDERING note).
                    _ruinStep.enabled = true;
                    stepColliders = 1;
                    stepHeight = _ruinStep.size.y * Mathf.Abs(_ruinStep.transform.lossyScale.y);
                }

                // §12 / WO-1721 guidance item 4 — the collapse VISUAL is now provable from a
                // log read alone: which model was chosen, where it came to rest, and how tall
                // the residual collider is (the owner's "that I can step over" ruling is a
                // NUMBER here, not an adjective).
                string ok = _ruin != null ? "yes" : "NO-RUBBLE-MODEL";
                FlowTrace.Step(Sys,
                    "WallSegment '" + name + "' RUIN SWAP (" + via + "): intact='" + intactName +
                    "' hidden -> ruin='" + ruinName + "' token='" + _ruinToken + "' shown=" + ok +
                    " bakedRuinH=" + _ruinHeight.ToString("F2") + "m restY=" + restY.ToString("F2") +
                    " stepCollider(s)=" + stepColliders + " stepH=" + stepHeight.ToString("F2") +
                    "m (no sink: the segment transform does NOT move, the model is REPLACED).");

                if (_ruin == null)
                    FlowTrace.Warn(Sys,
                        "WallSegment '" + name + "' collapsed with NO baked rubble model (token='" +
                        _ruinToken + "'). The intact panel is hidden, so the breach reads as an " +
                        "empty gap rather than rubble - re-bake with the art pack imported.");
            });
        }
    }
}

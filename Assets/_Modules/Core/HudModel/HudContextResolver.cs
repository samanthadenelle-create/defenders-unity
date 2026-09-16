// =============================================================================
// HudContextResolver — the HUD-context PRECEDENCE, as a pure function (WO-1436).
// -----------------------------------------------------------------------------
// Assembly: DeNelle.Core   Namespace: DeNelle.Core.HudModel
//
// WHY THIS EXISTS. The precedence rule (Modal > BuildMode > Battle > Town >
// Overworld) was expressed only as an inline ternary chain inside the live
// MonoBehaviour poll (DeNelle.Village.Hud.HudContextEvaluator, an `internal
// sealed` class in an assembly the editor regression assembly cannot reach into).
// A rule that can only be evaluated by a running scene cannot be ASSERTED, and
// WO-1436 is precisely a defect no oracle caught: 394+ suites were green while a
// raid scene resolved to the peaceful Overworld context and the player had no
// reachable ability faces for a whole assault.
//
// So the rule moves to a pure static here. The evaluator calls it, and the scene/
// posture seam oracle calls it with the same inputs — one implementation, two
// callers, no second copy to drift (the failure mode CLAUDE.md §2/§5/§16 all
// record). NOTHING about the behaviour changes; this is the same chain, hoisted.
//
// Pure data + Core seams only — no UnityEngine.UI, no scene loads, no statics
// read at call time. Everything the answer depends on is an argument.
// =============================================================================

using DeNelle.Core;

namespace DeNelle.Core.HudModel
{
    /// <summary>The single expression of the HUD-context precedence rule.</summary>
    public static class HudContextResolver
    {
        /// <summary>
        /// Apply the frozen precedence (WO-541): Modal &gt; BuildMode &gt; Battle &gt;
        /// Town &gt; Overworld.
        /// </summary>
        /// <param name="modal">A registered modal panel is open (PanelManager.AnyOpen).</param>
        /// <param name="buildMode">A Build Mode edit session owns the screen.</param>
        /// <param name="combat">Any combat input is live — an active/imminent wave, a
        /// staged battle lock, an enemy pursuit pulse, OR the scene itself declaring
        /// combat (<see cref="HubScenes.SceneDeclaresCombat"/>).</param>
        /// <param name="inVillage">Hub scene, hero inside the town ring.</param>
        public static HudContext Resolve(bool modal, bool buildMode, bool combat, bool inVillage)
        {
            if (modal) return HudContext.Modal;
            if (buildMode) return HudContext.BuildMode;
            if (combat) return HudContext.Battle;
            return inVillage ? HudContext.Town : HudContext.Overworld;
        }

        /// <summary>
        /// The context a scene resolves to AT REST — no modal open, not building, no wave
        /// running, nothing pursuing the hero, hero at its spawn seat. This is the
        /// "what does the ground itself say?" question, and it is the one the seam oracle
        /// asks per build-list scene.
        ///
        /// <para>`inVillage` collapses to <see cref="HubScenes.IsHub"/> here on purpose: the
        /// live evaluator additionally requires the hero inside the town ring, but it
        /// DEFAULTS to in-ring until a hero resolves (HudContextEvaluator.IsInTownRing), and
        /// a freshly loaded hub seats the hero at the Heart. So at rest the two agree, and
        /// the difference is a hero POSITION — never a scene KIND, which is what the oracle
        /// pins.</para>
        /// </summary>
        public static HudContext ResolveForSceneAtRest(string sceneName)
        {
            return Resolve(
                modal: false,
                buildMode: false,
                combat: HubScenes.SceneDeclaresCombat(sceneName),
                inVillage: HubScenes.IsHub(sceneName) || HubScenes.IsOwnedTown(sceneName));
        }
    }
}

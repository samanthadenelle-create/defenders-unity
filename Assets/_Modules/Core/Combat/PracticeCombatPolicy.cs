using UnityEngine;

namespace DeNelle.Core.Combat
{
    /// <summary>Practice actors never produce persistent kill rewards, XP or item drops.</summary>
    public static class PracticeCombatPolicy
    {
        public const string SceneName = "ArenaPractice_IronBastion";

        // Actor scene, not active scene: death callbacks and scene transitions may overlap.
        public static bool AllowsProgression(Component actor) => actor != null &&
            actor.gameObject.scene.name != SceneName;
    }
}

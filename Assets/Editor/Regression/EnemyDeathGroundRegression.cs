using System;
using System.Collections.Generic;
using DeNelle.Village;

namespace DeNelle.Editor.Regression
{
    /// <summary>Proves death settling cannot ratchet a wave enemy upward.</summary>
    public static class EnemyDeathGroundRegression
    {
        public static bool Run(out string reason)
        {
            var failures = new List<string>();

            float ogreFirst = Enemy.DeathGroundTargetY(0f, 2.27f, 0.08f);
            float ogreNext = Enemy.DeathGroundTargetY(0f, 2.27f, ogreFirst);
            if (Math.Abs(ogreFirst - 0.08f) > 0.0001f || Math.Abs(ogreNext - ogreFirst) > 0.0001f)
                failures.Add("a grounded Ogre with a 2.27 m animated bounds gap was lifted or ratcheted");

            float airborne = Enemy.DeathGroundTargetY(0f, 0.2f, 3f);
            if (Math.Abs(airborne - 0.2f) > 0.0001f)
                failures.Add("an actually airborne enemy did not descend to its visible-bottom seat");

            float belowFloor = Enemy.DeathGroundTargetY(1f, 0f, 0.9f);
            if (Math.Abs(belowFloor - 0.9f) > 0.0001f)
                failures.Add("death grounding lifted a root that was already below the sampled surface");

            reason = failures.Count == 0
                ? "ENEMY_DEATH_GROUND_OK no lift, no repeated-frame ratchet, airborne bodies descend"
                : "ENEMY_DEATH_GROUND_FAIL x" + failures.Count + " :: " + string.Join(" | ", failures);
            return failures.Count == 0;
        }
    }
}

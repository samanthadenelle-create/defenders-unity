using System;
using System.Collections.Generic;
using System.IO;
using DeNelle.Village;

namespace DeNelle.Editor.Regression
{
    /// <summary>
    /// WO-1734: the hero acquires hostile UNITS before walls, and the anti-oscillation stickiness
    /// can never delay that priority.
    /// </summary>
    /// <remarks>
    /// Owner ruling 2026-09-15, extending her WO-1730 troop ruling to the hero: *"should never
    /// default to target wall, should always default to aggresive targets nearby first asnd then
    /// only then wall"*.
    ///
    /// Two halves, matching the two halves of the fix:
    ///   * the PURE half — <see cref="HeroTargetIndicator.HoldsCurrentAutoTarget"/> is a static with
    ///     no scene dependency, so the stickiness contract is pinned by arithmetic;
    ///   * the SHAPE half — source text, because the priority gate itself needs a live hero and a
    ///     live wall to exercise, and a source pin is the honest cheap proof that the seam is still
    ///     a gate in FRONT of an unchanged nearest-wins body (the WO-1719 shape) rather than a
    ///     priority-aware sort.
    /// </remarks>
    public static class HeroUnitOverWallTargetingRegression
    {
        public static bool Run(out string reason)
        {
            var failures = new List<string>();

            const float Stick = 1f;
            const float Dwell = 0.35f;

            // ── The stickiness contract (pure) ────────────────────────────────────────────────
            // A target that is no longer acquirable is NEVER held, however young the hold.
            if (HeroTargetIndicator.HoldsCurrentAutoTarget(false, true, 3f, 9f, 0f, Stick, Dwell))
                failures.Add("stickiness held a target that is no longer acquirable");

            // ⛔ THE SAFETY PROPERTY: a cross-class rival (a UNIT displacing a held WALL) is exempt
            // from BOTH the dwell and the margin, so the WO-1734 priority gate can never be delayed.
            if (HeroTargetIndicator.HoldsCurrentAutoTarget(true, false, 2f, 5f, 0f, Stick, Dwell))
                failures.Add("stickiness delayed a cross-class switch - a unit could not displace a wall");

            // Same class, hold younger than the dwell -> held, even for a much closer rival. This is
            // the 30ms SS_5 <-> SS_6 flip the owner felt.
            if (!HeroTargetIndicator.HoldsCurrentAutoTarget(true, true, 5f, 2f, 0.1f, Stick, Dwell))
                failures.Add("a sub-dwell same-class flip was allowed - the oscillation is back");

            // Same class, past the dwell, rival NOT closer by more than the margin -> still held.
            if (!HeroTargetIndicator.HoldsCurrentAutoTarget(true, true, 5f, 4.5f, 1f, Stick, Dwell))
                failures.Add("a rival inside the stickiness margin displaced the held target");

            // Same class, past the dwell, rival clearly closer -> the switch is allowed. Stickiness
            // must not become a lock.
            if (HeroTargetIndicator.HoldsCurrentAutoTarget(true, true, 5f, 3f, 1f, Stick, Dwell))
                failures.Add("a clearly closer same-class rival was refused - stickiness became a lock");

            // ── The seam shape (source) ──────────────────────────────────────────────────────
            string path = Path.Combine("Assets", "_Modules", "Village", "Hero", "HeroTargetIndicator.cs");
            string source = File.Exists(path) ? File.ReadAllText(path) : string.Empty;
            if (source.Length == 0)
            {
                failures.Add("HeroTargetIndicator.cs could not be read");
            }
            else
            {
                // The gate runs the unit pass FIRST and falls back to the unrestricted pass.
                if (!source.Contains("NearestCandidateOfClass(unitsOnly: true)"))
                    failures.Add("the unit-first pass is gone - acquisition is back to pure nearest-wins");
                if (!source.Contains("NearestCandidateOfClass(unitsOnly: false)"))
                    failures.Add("the all-classes fallback is gone - walls may no longer be targetable");

                // Same classifier as the troop side (TroopController.IsHostileStructure).
                if (!source.Contains("unitsOnly && cand is IDamageableStructure"))
                    failures.Add("the unit/structure classifier no longer matches the troop-side rule");

                // The stickiness must consult the SELECTION's own acceptance, not merely the
                // candidate list - _candidates lacks the engage ring and the DEF-269 forward arc,
                // so a Contains-only test would hold a target standing behind the hero.
                if (!source.Contains("IsStillAutoAcquirable(_autoPick)"))
                    failures.Add("stickiness no longer re-checks engage range + forward arc on the held target");

                // Instrumentation is PERMANENT (CLAUDE.md sec.12).
                if (!source.Contains("AUTO PICK "))
                    failures.Add("the AUTO PICK ... WHY= trace was stripped");
                if (!source.Contains("AUTO SWITCH HELD"))
                    failures.Add("the AUTO SWITCH HELD trace was stripped");
            }

            reason = failures.Count == 0
                ? "HERO_UNIT_OVER_WALL_OK units outrank walls; stickiness is same-class only"
                : "HERO_UNIT_OVER_WALL_FAIL: " + string.Join("; ", failures);
            return failures.Count == 0;
        }
    }
}

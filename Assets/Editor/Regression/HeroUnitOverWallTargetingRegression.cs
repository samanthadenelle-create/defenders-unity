using System;
using System.Collections.Generic;
using System.IO;
using DeNelle.Village;

namespace DeNelle.Editor.Regression
{
    /// <summary>
    /// WO-1734: the hero acquires hostile UNITS before walls, and the anti-oscillation stickiness
    /// can never delay that priority. WO-1756: a wall is never AUTO-acquired AT ALL — the player
    /// must select it — and the manual selection path is structurally unable to reach that rule.
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

            // ── WO-1756: the AUTO-ACQUIRE admission rule (pure) ──────────────────────────────
            // Owner ruling 2026-09-15: *"i auto target the wall, i should need to select it"*.
            //
            // CASE 1 — the ticket's headline: with ONLY walls in range the all-classes FALLBACK
            // (unitsOnly:false — the branch WO-1734 left admitting walls, and the only branch left
            // once the garrison is dead) must admit nothing, so auto-acquire yields NO target.
            if (HeroTargetIndicator.AutoAcquireAdmits(isWall: true, isStructure: true, unitsOnly: false))
                failures.Add("a WALL was admitted by the all-classes auto pass - the reticle can still "
                           + "snap to masonry the player never selected (WO-1756)");
            // ...and it is refused on the unit-first pass too, so neither branch can pick one.
            if (HeroTargetIndicator.AutoAcquireAdmits(isWall: true, isStructure: true, unitsOnly: true))
                failures.Add("a WALL was admitted by the unit-first auto pass (WO-1756)");

            // CASE 2 — the ENGAGE PRESS. EngageLock(null) walks _candidates through this same
            // predicate with unitsOnly:false and takes the first ADMITTED one. So "only walls in
            // range" must admit nothing on every wall in that list, leaving the press with no target
            // (it then degrades to _locked ?? CurrentTarget, and to no lock at all if those are null).
            // Pinned as a loop so the case reads as the walk it actually is, not as one call.
            bool engagePressFoundATarget = false;
            foreach (bool wallIsStructure in new[] { true })          // every WallSegment is IDamageableStructure
                for (int i = 0; i < 4; i++)                            // a list of four wall panels
                    if (HeroTargetIndicator.AutoAcquireAdmits(isWall: true, isStructure: wallIsStructure,
                                                              unitsOnly: false))
                        engagePressFoundATarget = true;
            if (engagePressFoundATarget)
                failures.Add("an ENGAGE PRESS with only walls in range still found a target - the button picks "
                           + "masonry FOR the player, which is the owner's complaint in a different hat (WO-1756)");

            // ⛔ THE SCOPE GUARD: the owner ruled on WALLS, not on structures. A tower, a gate and
            // the raid spire are IDamageableStructure but NOT WallSegment, and they must still be
            // auto-acquirable on the fallback - widening WO-1756 to every structure would leave the
            // hero unable to auto-engage the raid objective.
            if (!HeroTargetIndicator.AutoAcquireAdmits(isWall: false, isStructure: true, unitsOnly: false))
                failures.Add("a non-wall structure (tower / gate / raid spire) was refused by auto-acquire "
                           + "- WO-1756 was widened past the owner's ruling, which was about WALLS");

            // WO-1734 is unchanged underneath: the unit-first pass still skips every structure, and
            // a plain hostile unit is admitted by both passes.
            if (HeroTargetIndicator.AutoAcquireAdmits(isWall: false, isStructure: true, unitsOnly: true))
                failures.Add("the unit-first pass admitted a structure - the WO-1734 gate is gone");
            if (!HeroTargetIndicator.AutoAcquireAdmits(isWall: false, isStructure: false, unitsOnly: true)
                || !HeroTargetIndicator.AutoAcquireAdmits(isWall: false, isStructure: false, unitsOnly: false))
                failures.Add("a hostile UNIT was refused by auto-acquire - the selection can acquire nothing");

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
                    failures.Add("the all-classes fallback is gone - non-wall structures (tower / gate / "
                               + "raid spire) would no longer be auto-acquirable");

                // Same classifier as the troop side (TroopController.IsHostileStructure) - WO-1756
                // moved it INTO the pure predicate rather than deleting it, so pin it where it lives.
                if (!source.Contains("AutoAcquireAdmits(candIsWall, cand is IDamageableStructure, unitsOnly)"))
                    failures.Add("the selection no longer routes its admission through AutoAcquireAdmits "
                               + "with the troop-side IDamageableStructure classifier");
                if (!source.Contains("cand is WallSegment"))
                    failures.Add("the WallSegment refusal is gone - the reticle can auto-target a wall again "
                               + "(WO-1756)");

                // ⛔ THE SELECTION-SURVIVES PROOF. A wall the player CHOSE must never be dropped by the
                // rule that refuses to acquire one. That holds because the manual path cannot reach the
                // predicate at all, and these three pins are what keep it that way:
                //   (a) the auto path is short-circuited away while a tap-lock lives;
                //   (b) the held lock is validated ONLY against _candidates (range + faction + LoS,
                //       which still contains walls), never against the selection's class rule;
                //   (c) the predicate is referenced in exactly TWO places - its definition and its one
                //       call inside NearestCandidateOfClass - so nothing else, least of all the hold,
                //       consults it.
                if (!source.Contains("CurrentTarget = _locked ?? NearestCandidate()"))
                    failures.Add("the manual lock no longer short-circuits the auto path - a player-selected "
                               + "wall could now be re-derived, and refused, every frame (WO-1756)");
                if (!source.Contains("!_locked.IsAlive || !_candidates.Contains(_locked)"))
                    failures.Add("the held-lock clear rule changed - a player-selected wall is no longer held "
                               + "purely on alive + in-candidates (WO-1756)");
                int admitRefs = CountOccurrences(source, "AutoAcquireAdmits(");
                if (admitRefs != 3)
                    failures.Add("AutoAcquireAdmits( is referenced " + admitRefs + " times, expected exactly 3 "
                               + "(its definition + NearestCandidateOfClass + EngageLock's implicit pick). A "
                               + "FOURTH reference means the pick-for-her class rule has been wired into another "
                               + "path - if that path is the manual hold, a player-selected wall is dropped "
                               + "(WO-1756).");

                // ── WO-1756 follow-up: EngageLock(null) is a button that PICKS FOR HER ───────────
                // An engage press with no explicit target used to take `_candidates[0]` flat, which
                // with the garrison dead is a wall - the owner's complaint in a different hat. It now
                // walks the list through the SAME predicate and takes the first admitted candidate.
                if (source.Contains("target = _candidates.Count > 0 ? _candidates[0]"))
                    failures.Add("EngageLock(null) takes _candidates[0] flat again - an engage press can hand "
                               + "the player a wall she never selected (WO-1756)");
                if (!source.Contains("AutoAcquireAdmits(c is WallSegment, c is IDamageableStructure, false)"))
                    failures.Add("EngageLock(null)'s implicit pick no longer routes through AutoAcquireAdmits - "
                               + "either the wall skip is gone or the rule was COPIED instead of reused (WO-1756)");
                // ⛔ DEGRADE, not index-0-anyway: with only walls in range the engage press must land
                // on nothing rather than fall back into the candidate list.
                if (!source.Contains("if (target == null) target = _locked ?? CurrentTarget;"))
                    failures.Add("EngageLock(null) no longer degrades to the player's own prior pick / the auto "
                               + "reticle when every candidate is a wall - it may pick masonry or lock nothing "
                               + "safely (WO-1756)");
                // ⚠ And CycleTarget stays UNFILTERED on purpose: one target per press, the player sees
                // what she landed on, and it is the only non-raycast route to a wall. The count pin
                // above is what catches a fourth call site being added to it.
                if (!source.Contains("_locked = _candidates[idx];"))
                    failures.Add("CycleTarget no longer steps the raw candidate list - a wall may have become "
                               + "unreachable by any route except a raycast tap (WO-1756 kept the cycle open)");

                // The refusal trace the ticket asked for - and it must stay THROTTLED, because this
                // body runs every LateUpdate (CLAUDE.md sec.12: a per-frame Step evicts the logcat ring).
                if (!source.Contains("WALL REFUSED for auto-acquire"))
                    failures.Add("the WALL REFUSED trace was stripped - the next capture can only imply the "
                               + "rule, not prove it (WO-1756)");
                if (!source.Contains("Throttle(\"Reticle\", \"wall-refused-auto\""))
                    failures.Add("the wall-refusal trace is no longer throttled - a per-frame line here floods "
                               + "the device log and evicts the boot window");

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
                ? "HERO_UNIT_OVER_WALL_OK units outrank walls; walls are select-only (never auto-acquired, "
                  + "WO-1756) while non-wall structures still are; the manual lock cannot reach the rule; "
                  + "stickiness is same-class only"
                : "HERO_UNIT_OVER_WALL_FAIL: " + string.Join("; ", failures);
            return failures.Count == 0;
        }

        // Non-overlapping occurrence count. Used to prove AutoAcquireAdmits is wired into exactly one
        // call site, so the AUTO-only class rule cannot have leaked into the manual hold path.
        private static int CountOccurrences(string haystack, string needle)
        {
            if (string.IsNullOrEmpty(haystack) || string.IsNullOrEmpty(needle)) return 0;
            int n = 0;
            for (int i = haystack.IndexOf(needle, StringComparison.Ordinal);
                 i >= 0;
                 i = haystack.IndexOf(needle, i + needle.Length, StringComparison.Ordinal))
            {
                n++;
            }
            return n;
        }
    }
}

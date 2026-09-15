// =============================================================================
// WallDurabilityRegression — WO-1737. Pins the owner's 2026-09-15 wall-durability
// ruling to the REAL WallSegment component and the REAL troops.json catalog.
// -----------------------------------------------------------------------------
// THE RULING, VERBATIM (owner, on device, 2026-09-15):
//
//   "walls fall with a single hit, the HP should be strong enough even at lowest
//    level that it takes some damage to get a wall down. Think of CoC."
//
// and, given as two decisions in the same session:
//   1. A TIER-1 wall should take ~8-10 hits from a mid-tier attacker (the
//      battlemage, 42 damage).
//   2. Siege KEEPS its 2x structure-damage multiplier and stays the designated
//      wall-breaker: after the change it must still breach clearly faster than
//      anything else, but it must NOT one-shot.
//
// WHAT IS ASSERTED, AND WHY EACH CASE EXISTS
//   1. BEHAVIOURAL, through the real component. A real WallSegment at SetTier(1)
//      is hit with real 42-damage blows through the real IDamageable seam. It must
//      still stand after the 7th and must not require more than the 10th. This is
//      the ruling itself, and it is asserted by DRIVING the damage rather than by
//      recomputing the formula — an oracle that re-derives `amount / ToughnessFor`
//      would pass even if ApplyDamage stopped calling the divisor at all.
//   2. The floor is the NAMED CONSTANT, and the tier SPACING is untouched. The
//      ratio test is what stops a future seat "fixing" durability by widening the
//      per-tier step (which cannot move tier 1 — see WallSegment.BaseToughness) and
//      silently re-tuning what every wall upgrade buys.
//   3. SIEGE, off the real catalog. The catapult's per-hit structural damage must
//      be the maximum over every troop in troops.json (still the designated
//      breaker), AND must be strictly less than a tier-1 wall's effective HP (no
//      one-shot). Both halves of ruling 2, neither hardcoded: the damage numbers
//      are read through TroopCatalog, so re-authoring troops.json re-runs the
//      check instead of dating it.
//
// ⛔ RED PROOF — ARGUED, NOT RUN (CLAUDE.md §11B: an unproven thing named as
//    unproven is useful; an unproven thing stated as fact is a lie). This lane is
//    edit-only and may not start Unity, so the failing run was NOT executed. What
//    the assertions do against the PRE-WO-1737 tree, from the source as it stood:
//      * Case 1: ToughnessFor(1) returned Mathf.Pow(1.6f, 0) == 1.0, so a 42-damage
//        blow landed 42 points on the 0-100 track and the third hit reached 126 —
//        `IsDestroyed` is TRUE at hit 3, and the "still standing after 7" assertion
//        is false. RED.
//      * Case 2: ToughnessFor(1) == 1.0 != WallSegment.BaseToughness (the constant
//        did not exist — compile-red, the strongest RED there is).
//      * Case 3: a tier-1 wall's effective HP was MaxHp * 1.0 == 100 raw, and the
//        catapult's per-hit structural damage is 48 * 2.0 == 96. 96 < 100 holds, so
//        the no-one-shot half passed before and still passes — it is a GUARD against
//        the fix overshooting downward, not a re-statement of the bug.
//    A seat with Unity should run it against a stash of WallSegment.cs to convert
//    this argument into a captured RED line.
//
// Pure edit-mode: one bare GameObject, no PlayMode, no scene. Runs inside
// DataRegression.RunAll. Returns true (summary) / false (detail); never throws.
// =============================================================================

using System.Collections.Generic;
using System.Text;
using DeNelle.Core.Combat;
using DeNelle.Village;
using UnityEngine;
using Object = UnityEngine.Object;

namespace DeNelle.Editor
{
    /// <summary>
    /// WO-1737 — the owner's wall-durability ruling, pinned behaviourally against the
    /// real <see cref="WallSegment"/> and the real troop catalog.
    /// </summary>
    public static class WallDurabilityRegression
    {
        private const string MarkerOk   = "WALL_DURABILITY_OK";
        private const string MarkerFail = "WALL_DURABILITY_FAIL";

        // The owner's own probe: the battlemage, whose authored attackDamage is the
        // "mid-tier attacker" her ruling names. Read from the catalog at run time; this
        // id is the only thing hardcoded, because the id IS the identity.
        private const string ProbeTroopId = "troop-battlemage";
        private const string SiegeTroopId = "troop-catapult";

        // The ruled window, INCLUSIVE: "~8-10 hits". Asserted as a band, not a point,
        // because the owner ruled a feel range and a point test would fail on any
        // re-tune inside her own stated tolerance.
        private const int MinRuledHits = 8;
        private const int MaxRuledHits = 10;

        /// <summary>Standalone batch entry point.</summary>
        public static void RunStandalone()
        {
            bool ok = Run(out string reason);
            Debug.Log((ok ? MarkerOk : MarkerFail) + ": " + reason);
        }

        /// <summary>
        /// Runs every case. True when all pass; <paramref name="reason"/> always carries a
        /// one-line human summary (the failure list, or what was proven).
        /// </summary>
        public static bool Run(out string reason)
        {
            var failures = new List<string>();
            var log = new StringBuilder();

            TroopCatalog.Reload();          // fresh read through the REAL WebGL-safe loader

            float probeDamage = StructuralDamagePerHit(ProbeTroopId, failures);
            Case1_Tier1SurvivesTheRuledHitCount(probeDamage, failures, log);
            Case2_FloorIsTheConstantAndSpacingIsUntouched(failures, log);
            Case3_SiegeStillBreachesFastestAndNeverOneShots(failures, log);

            if (failures.Count > 0)
            {
                reason = "WALL DURABILITY FAIL: " + string.Join(" | ", failures);
                return false;
            }
            reason = log.ToString().TrimEnd();
            return true;
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Case 1 — THE RULING, driven through the real component
        // ─────────────────────────────────────────────────────────────────────

        private static void Case1_Tier1SurvivesTheRuledHitCount(
            float probeDamage, List<string> failures, StringBuilder log)
        {
            if (!(probeDamage > 0f))
            {
                failures.Add($"case1: could not read a positive per-hit structural damage for '{ProbeTroopId}' " +
                             "— the ruling's own probe is unreadable, so nothing below measures the ruling");
                return;
            }

            var go = new GameObject("WO1737_WallDurabilityProbe");
            try
            {
                var wall = go.AddComponent<WallSegment>();
                wall.SetTier(1);
                if (wall.Tier != 1)
                {
                    failures.Add($"case1: SetTier(1) left Tier at {wall.Tier} — the probe is not measuring a tier-1 wall");
                    return;
                }

                // Drive REAL blows through the REAL player/troop seam until it falls. The cap is
                // generous and exists only so a divisor bug can never spin this forever.
                int hits = 0;
                const int hardCap = 1000;
                float afterFirst = 0f;
                while (wall.IsAlive && hits < hardCap)
                {
                    wall.TakeDamage(probeDamage, DamageElement.None);
                    hits++;
                    if (hits == 1) afterFirst = wall.Damage;
                }

                // ⚠ NAME THE ATTENUATOR BEFORE JUDGING THE COUNT. A bare probe GameObject reads
                // Faction == Friendly (SceneOwnership.IsEnemyOwned is false outside a raid scene),
                // so ApplyDamage ALSO applies the BULWARK talent reduction. With no save loaded
                // that is 0 and the count is clean — but if a future default ever unlocks a
                // structure-toughness node, every hit would shrink and this suite would report a
                // confusing "took too many hits" instead of the real cause. Checking the FIRST
                // blow against the pure tier divide makes that failure say what it is.
                float expectedFirst = probeDamage / WallSegment.ToughnessFor(1);
                if (hits > 0 && Mathf.Abs(afterFirst - expectedFirst) > 0.05f)
                {
                    failures.Add($"case1: the first {probeDamage:0.#} blow landed {afterFirst:0.00} on the damage track, " +
                                 $"not the expected post-tier-divide {expectedFirst:0.00} — something OTHER than " +
                                 $"ToughnessFor is attenuating the hit (BULWARK reads Friendly on a bare probe: " +
                                 $"faction={wall.Faction}). Fix that before reading the hit count below.");
                    return;
                }

                if (hits >= hardCap)
                {
                    failures.Add($"case1: a tier-1 wall survived {hardCap} blows of {probeDamage:0.#} — the damage is not " +
                                 "reaching the track at all (a wall nothing can break is worse than one that falls in a hit)");
                    return;
                }

                if (hits < MinRuledHits)
                    failures.Add($"case1: a tier-1 wall fell in {hits} hit(s) of {probeDamage:0.#} — the owner ruled " +
                                 $"~{MinRuledHits}-{MaxRuledHits} (\"the HP should be strong enough even at lowest level " +
                                 "that it takes some damage to get a wall down\")");
                else if (hits > MaxRuledHits)
                    failures.Add($"case1: a tier-1 wall took {hits} hits of {probeDamage:0.#} — the owner ruled " +
                                 $"~{MinRuledHits}-{MaxRuledHits}; overshooting turns the bottom rung into a stall, " +
                                 "which is the opposite failure but still a failure");
                else
                    log.AppendLine($"  [wall-durability] tier-1 wall takes {hits} hits of {probeDamage:0.#} " +
                                   $"({ProbeTroopId}) — inside the ruled {MinRuledHits}-{MaxRuledHits} band OK");
            }
            finally { Object.DestroyImmediate(go); }
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Case 2 — the floor is the constant; the tier spacing is untouched
        // ─────────────────────────────────────────────────────────────────────

        private static void Case2_FloorIsTheConstantAndSpacingIsUntouched(
            List<string> failures, StringBuilder log)
        {
            float tier1 = WallSegment.ToughnessFor(1);

            if (!Mathf.Approximately(tier1, WallSegment.BaseToughness))
                failures.Add($"case2: ToughnessFor(1) is x{tier1} but WallSegment.BaseToughness is " +
                             $"x{WallSegment.BaseToughness} — the ruled floor and the applied floor are two values");

            if (!(WallSegment.BaseToughness > 1f))
                failures.Add($"case2: WallSegment.BaseToughness is x{WallSegment.BaseToughness} — at or below x1 a base " +
                             "wall takes its hit unreduced, which is exactly the reported defect");

            // ⛔ THE STEP CANNOT SUBSTITUTE FOR THE FLOOR. Pinning the ratios stops the
            // tempting "just raise the per-tier step" fix: that leaves tier 1 at step^0 and
            // silently re-tunes every upgrade. Ratios, never absolutes — so a future durability
            // re-tune by the owner does not have to edit this file.
            float[] legacyRatios = { 1f, 1.6f, 2.56f };
            for (int i = 0; i < legacyRatios.Length && i + 1 <= WallSegment.MaxTier; i++)
            {
                float ratio = tier1 > 0f ? WallSegment.ToughnessFor(i + 1) / tier1 : 0f;
                if (Mathf.Abs(ratio - legacyRatios[i]) > 0.01f)
                    failures.Add($"case2: tier {i + 1} is x{ratio:0.000} of tier 1, was x{legacyRatios[i]} — moving the " +
                                 "durability floor must not re-tune what a wall UPGRADE buys");
            }

            if (failures.Count == 0)
                log.AppendLine($"  [wall-durability] floor x{WallSegment.BaseToughness} reads the named constant; " +
                               "tier spacing x1/x1.6/x2.56 preserved as ratios OK");
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Case 3 — siege still breaches fastest, and still never one-shots
        // ─────────────────────────────────────────────────────────────────────

        private static void Case3_SiegeStillBreachesFastestAndNeverOneShots(
            List<string> failures, StringBuilder log)
        {
            var all = TroopCatalog.All;
            if (all == null || all.Count == 0)
            {
                failures.Add("case3: TroopCatalog.All is empty — siege supremacy cannot be measured against the real roster");
                return;
            }

            float siege = StructuralDamagePerHit(SiegeTroopId, failures);
            if (!(siege > 0f)) return;

            // ⭐ JUDGED ON *DPS*, NOT ON PER-HIT DAMAGE. The owner's word is "faster", which is a
            // TIME metric — and per-hit damage does not decide it: a big slow hitter can lose the
            // race to a fast small one. The catapult's 2.5 s interval is the slowest in the
            // roster, so per-hit alone would have flattered it. Structural DPS is the honest form.
            float siegeDps = StructuralDpsOf(SiegeTroopId);
            string bestOtherId = null;
            float bestOtherDps = 0f;
            foreach (var t in all)
            {
                if (t == null || string.IsNullOrEmpty(t.Id) || t.Id == SiegeTroopId) continue;
                float cd = t.AttackCooldown > 0f ? t.AttackCooldown : 1f;
                float dps = t.AttackDamage * (t.StructureDamageMult > 0f ? t.StructureDamageMult : 1f) / cd;
                if (dps > bestOtherDps) { bestOtherDps = dps; bestOtherId = t.Id; }
            }

            if (!(siegeDps > bestOtherDps))
                failures.Add($"case3: '{SiegeTroopId}' does {siegeDps:0.#} structural DPS but '{bestOtherId}' does " +
                             $"{bestOtherDps:0.#} — siege is no longer the designated wall-breaker (owner ruling: siege " +
                             "KEEPS its 2x structure multiplier and must breach clearly faster than anything else)");

            // No one-shot: a tier-1 wall's effective HP, measured in RAW incoming damage, is
            // MaxHp * ToughnessFor(1). Read, never restated.
            float tier1EffectiveHp = WallSegment.MaxHp * WallSegment.ToughnessFor(1);
            if (!(siege < tier1EffectiveHp))
                failures.Add($"case3: '{SiegeTroopId}' lands {siege:0.#} against a tier-1 wall's {tier1EffectiveHp:0.#} " +
                             "effective HP — siege ONE-SHOTS the bottom rung, which the owner ruled out explicitly");

            if (failures.Count == 0)
                log.AppendLine($"  [wall-durability] siege {siegeDps:0.#} struct DPS beats the best non-siege " +
                               $"'{bestOtherId}' {bestOtherDps:0.#}, and its {siege:0.#}/hit stays under a tier-1 wall's " +
                               $"{tier1EffectiveHp:0.#} effective HP (breaches fastest, never one-shots) OK");
        }

        /// <summary>
        /// A troop's structural DPS off the real catalog: attackDamage x structureDamageMult /
        /// attackCooldown. Returns 0 for an unknown id (case 3 has already reported that).
        /// </summary>
        private static float StructuralDpsOf(string troopId)
        {
            var all = TroopCatalog.All;
            if (all == null) return 0f;
            foreach (var t in all)
            {
                if (t == null || t.Id != troopId) continue;
                float cd = t.AttackCooldown > 0f ? t.AttackCooldown : 1f;
                float mult = t.StructureDamageMult > 0f ? t.StructureDamageMult : 1f;
                return t.AttackDamage * mult / cd;
            }
            return 0f;
        }

        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// A troop's per-hit damage AGAINST A STRUCTURE, read off the real catalog:
        /// attackDamage x structureDamageMult, mirroring TroopController.Attack. Adds a
        /// failure and returns 0 when the id is not in troops.json.
        /// </summary>
        private static float StructuralDamagePerHit(string troopId, List<string> failures)
        {
            var all = TroopCatalog.All;
            if (all == null) { failures.Add($"TroopCatalog.All is null — cannot read '{troopId}'."); return 0f; }
            foreach (var t in all)
            {
                if (t == null || t.Id != troopId) continue;
                float mult = t.StructureDamageMult > 0f ? t.StructureDamageMult : 1f;
                return t.AttackDamage * mult;
            }
            failures.Add($"troop id '{troopId}' is not in troops.json — this suite's probe has been renamed away.");
            return 0f;
        }
    }
}

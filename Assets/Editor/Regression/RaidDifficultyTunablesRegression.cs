// =============================================================================
// RaidDifficultyTunablesRegression [raid-difficulty]
// -----------------------------------------------------------------------------
// Assembly: DeNelle.EditorRegression.  Markers: RAID_DIFFICULTY_OK / RAID_DIFFICULTY_FAIL.
//
// Pins WO-1763: per-camp raid difficulty is REMOTE-tunable, and it ships INERT.
//
// The owner nearly 3-starred the Iron Bastion with a level-4 hero and believed
// raid difficulty was already tunable from the database. It was not. It is now -
// and the whole risk of that change is that a knob family whose defaults are not
// exactly identity would silently re-tune every raid in the game for every player
// who has no row and no network. This suite is what makes that impossible.
//
// -----------------------------------------------------------------------------
// WHAT IS PINNED, AND WHY EACH CASE EXISTS.
// -----------------------------------------------------------------------------
//   1 [identity]          With nothing loaded, Resolve returns the AUTHORED values
//                         for all four camps, and both sources read "json". The
//                         authored values are read through SceneConfigCatalog, never
//                         restated as literals here - a literal would let this case
//                         certify a table that has since moved.
//   2 [folds]             An override reaches combat: the effective multiplier is
//                         authored x pct/100, and driving a REAL EnemyDef through the
//                         REAL RaidGarrisonSpawner.FoldDifficulty + the REAL
//                         GarrisonStatBlocks.ApplyLevelScale moves HP *and* contact
//                         damage. Both, because FoldDifficulty touches both.
//   3 [offset-replace]    The offset REPLACES rather than adds, the sentinel falls
//                         back to the authored offset, and the spawner's
//                         max(baseEnemyLevel, playerLevel + offset) floor still holds
//                         for a low-level hero.
//   4 [unknown-camp]      An id not in the table (a made-up id, "", null) resolves to
//                         the values it was PASSED. A zero-by-accident FAILS: 0 is a
//                         no-op in FoldDifficulty, so it would read as "difficulty
//                         applied" while applying nothing.
//   5 [clamps]            Out-of-range values clamp, and the RAID USES THE CLAMPED
//                         VALUE. The sentinel is exempt from the offset clamp.
//   6 [defaults-identity] The eight registry defaults are exactly identity / sentinel,
//                         so a fresh install and an empty table are the same raid.
//
// -----------------------------------------------------------------------------
// PROVEN RED AGAINST THE PREVIOUS TREE - by NON-COMPILE, and that IS the red.
// -----------------------------------------------------------------------------
// Before WO-1763 there was no RaidDifficultyTunables at all, no
// RemoteTunables.KeyRaidDifficultyMultPct*/KeyRaidLevelOffset*, and
// RaidGarrisonSpawner.FoldDifficulty was private. This file names every one of
// those symbols, so against that tree it does not build. What each case asserts
// once it does is written out beside it.
//
// -----------------------------------------------------------------------------
// (!) THE ONE NUMBER THIS SUITE DELIBERATELY DOES NOT ASSERT.
// -----------------------------------------------------------------------------
// It never states what any camp's authored difficultyMultiplier or levelOffset IS.
// Those are owner-authored content in scene-configs.json; an oracle that pinned
// them would go red the first time she tuned a camp by hand, which is the opposite
// of useful. What is pinned is the RELATIONSHIP between the authored value and the
// effective one. The one arithmetic example carried in prose (the WO's 160% case)
// is driven from values READ at run time, not from the ticket's numbers.
//
// Zero scene, zero save, zero network, zero PlayMode, zero PlayerPrefs.
// ASCII only. Never throws.
// =============================================================================

using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using DeNelle.Core.Ops;
using DeNelle.Village;
using DeNelle.Village.World.Camps;

namespace DeNelle.Editor.Regression
{
    /// <summary>
    /// Pins the per-camp raid DIFFICULTY rail: identity by default, multiplicative on the
    /// authored multiplier, REPLACE on the authored offset, clamped, and loud on an unknown
    /// camp. Returns true (summary) / false (detail).
    /// </summary>
    public static class RaidDifficultyTunablesRegression
    {
        /// <summary>Float tolerance. These are single-precision products of small numbers.</summary>
        private const float Eps = 0.0005f;

        /// <summary>Standalone batch entry - prints the marker.</summary>
        public static void RunAll()
        {
            if (Run(out string reason)) Debug.Log("RAID_DIFFICULTY_OK - " + reason);
            else Debug.LogError("RAID_DIFFICULTY_FAIL: " + reason);
        }

        /// <summary>Covenant contract (DataRegression-shaped). Never throws.</summary>
        public static bool Run(out string reason)
        {
            var failures = new List<string>();
            var log = new StringBuilder();
            log.AppendLine("--- RAID DIFFICULTY (WO-1763: per-camp difficulty on the remote rail) ---");

            // The rail is process-global. Clear it first so an ambient payload from an earlier
            // suite in the same batch cannot make [identity] red on correct code, and clear it
            // again in the finally so this suite cannot make the NEXT one red either.
            try
            {
                RemoteTunables.Clear();
                Case(failures, "identity", () => Case1_Identity(failures, log));
                Case(failures, "folds", () => Case2_Folds(failures, log));
                Case(failures, "offset-replace", () => Case3_OffsetReplace(failures, log));
                Case(failures, "unknown-camp", () => Case4_UnknownCamp(failures, log));
                Case(failures, "clamps", () => Case5_Clamps(failures, log));
                Case(failures, "defaults-identity", () => Case6_DefaultsAreIdentity(failures, log));
            }
            catch (Exception ex)
            {
                failures.Add("[suite] THREW " + ex.GetType().Name + ": " + ex.Message);
            }
            finally
            {
                RemoteTunables.Clear();
            }

            if (failures.Count == 0)
            {
                reason = "RAID DIFFICULTY OK - with no row, no network and no parse every camp " +
                         "resolves to the values scene-configs.json AUTHORS, bit for bit, and both " +
                         "sources read '" + RaidDifficultyTunables.SourceJson + "' (the " +
                         "no-felt-change-ships-with-the-code invariant); a percent override folds " +
                         "into HP AND contact damage through the real FoldDifficulty and survives " +
                         "the real ApplyLevelScale; the level offset REPLACES the authored one " +
                         "rather than adding to it, the " + RaidDifficultyTunables.UseAuthoredOffsetSentinel +
                         " sentinel falls back to the authored value, and the baseEnemyLevel floor " +
                         "still protects a low-level hero; an unknown config id keeps the values it " +
                         "was passed instead of another camp's knob or a FoldDifficulty-no-op zero; " +
                         "out-of-range rows clamp to " + RaidDifficultyTunables.MinMultiplierPct + ".." +
                         RaidDifficultyTunables.MaxMultiplierPct + " and " +
                         RaidDifficultyTunables.MinLevelOffset + ".." + RaidDifficultyTunables.MaxLevelOffset +
                         " and the raid uses the CLAMPED value; and all eight registry defaults are " +
                         "exactly identity, so a fresh install and an empty table are the same raid";
                Debug.Log(log.ToString() + "RAID_DIFFICULTY_OK");
                return true;
            }

            reason = "raid-difficulty FAIL x" + failures.Count + ": " + string.Join(" | ", failures);
            Debug.LogError(log.ToString() + "RAID_DIFFICULTY_FAIL: " + reason);
            return false;
        }

        private static void Case(List<string> failures, string name, Action body)
        {
            try { body(); }
            catch (Exception ex) { failures.Add("[" + name + "] THREW " + ex.GetType().Name + ": " + ex.Message); }
        }

        // =====================================================================
        //  The four camp ids, REUSED from the shipping table. Never re-typed:
        //  they are live, save-adjacent data with exactly one home.
        // =====================================================================
        private static readonly string[] CampIds =
        {
            RaidLootTunables.CampIdCamp1,
            RaidLootTunables.CampIdCamp2,
            RaidLootTunables.CampIdCamp3,
            RaidLootTunables.CampIdBastion,
        };

        /// <summary>
        /// The AUTHORED garrison numbers for one camp, read out of scene-configs.json through
        /// the shipping catalog. Returns false (and says so) when the row or its garrison block
        /// is missing - a camp with no row is a real defect, not a case to skip silently.
        /// </summary>
        private static bool TryAuthored(List<string> failures, string caseName, string configId,
                                        out float authoredMult, out int authoredOffset,
                                        out int baseEnemyLevel)
        {
            authoredMult = 1f;
            authoredOffset = 0;
            baseEnemyLevel = 0;

            var def = SceneConfigCatalog.Find(configId);
            if (def == null)
            {
                failures.Add("[" + caseName + "] scene-configs.json has no config '" + configId +
                             "', but RaidLootTunables names it as a live camp id. Either the row was " +
                             "removed or the id was renamed; both silently un-tune a camp.");
                return false;
            }
            if (def.garrison == null)
            {
                failures.Add("[" + caseName + "] config '" + configId + "' has no garrison block, so " +
                             "it has no authored difficulty at all and nothing to scale.");
                return false;
            }

            authoredMult = def.garrison.difficultyMultiplier > 0f ? def.garrison.difficultyMultiplier : 1f;
            authoredOffset = def.garrison.levelOffset;
            baseEnemyLevel = def.garrison.baseEnemyLevel;
            return true;
        }

        // =====================================================================
        //  Case 1 - IDENTITY. The invariant the whole ticket is judged on.
        // =====================================================================
        /// <summary>
        /// ⭐ THE LOAD-BEARING CASE. "No felt change ships with the code" is the acceptance
        /// criterion this ticket is closed against, and it is INVISIBLE when broken: nothing
        /// crashes, every raid in the game is simply a different fight from the one that shipped.
        /// <para>
        /// The authored values are READ, not restated, and compared with an EXACT float equality
        /// on the multiplier. That strictness is the point: the consumer short-circuits at 100
        /// percent precisely so this comparison can be exact rather than within a tolerance, and
        /// a tolerance here would hide the float round-trip the short-circuit exists to avoid.
        /// </para>
        /// </summary>
        private static void Case1_Identity(List<string> failures, StringBuilder log)
        {
            RemoteTunables.Clear();

            foreach (var id in CampIds)
            {
                if (!TryAuthored(failures, "identity", id, out float mult, out int offset, out int _))
                    continue;

                var r = RaidDifficultyTunables.Resolve(id, mult, offset);
                log.AppendLine("  identity '" + id + "': authored x" + mult.ToString("F2") + " off " +
                               offset + " -> x" + r.EffectiveMultiplier.ToString("F2") + " off " +
                               r.EffectiveLevelOffset + " (" + r.MultiplierSource + "/" + r.OffsetSource + ")");

                if (r.EffectiveMultiplier != mult)
                    failures.Add("[identity] '" + id + "' resolved an effective multiplier of " +
                                 r.EffectiveMultiplier + " with NO row and NO network; the value " +
                                 "scene-configs.json authors is " + mult + ". This is the invariant the " +
                                 "whole ticket is judged on - an empty client_tunables table must be " +
                                 "TODAY'S RAID, bit for bit - and it breaks INVISIBLY: nothing errors, " +
                                 "every player offline simply fights a different game.");

                if (r.EffectiveLevelOffset != offset)
                    failures.Add("[identity] '" + id + "' resolved an effective level offset of " +
                                 r.EffectiveLevelOffset + " with no row; the authored value is " + offset +
                                 ". The default MUST be the sentinel that means 'use the authored value'.");

                if (r.MultiplierPct != RaidDifficultyTunables.IdentityMultiplierPct)
                    failures.Add("[identity] '" + id + "' reported " + r.MultiplierPct + " percent with " +
                                 "no row, not the identity " + RaidDifficultyTunables.IdentityMultiplierPct +
                                 " - the RAID START line would tell the owner a raid was being scaled " +
                                 "when it was not.");

                if (r.MultiplierSource != RaidDifficultyTunables.SourceJson ||
                    r.OffsetSource != RaidDifficultyTunables.SourceJson)
                    failures.Add("[identity] '" + id + "' labelled its sources '" + r.MultiplierSource +
                                 "/" + r.OffsetSource + "' with no row in play, not '" +
                                 RaidDifficultyTunables.SourceJson + "/" + RaidDifficultyTunables.SourceJson +
                                 "'. Acceptance criterion 4 greps the RAID START line for src=" +
                                 RaidDifficultyTunables.SourceJson + " to prove the identity build.");

                if (!r.IsIdentity)
                    failures.Add("[identity] '" + id + "' does not report IsIdentity with no row in play.");
            }
        }

        // =====================================================================
        //  Case 2 - IT ACTUALLY REACHES COMBAT.
        // =====================================================================
        /// <summary>
        /// A refusal-only proof certifies nothing (memory: prove-the-success-path-not-just-the-refusal).
        /// A legal percent must MOVE the fight, or the owner sets a row, feels no change, and
        /// blames the build - the exact failure the whole rail exists to avoid.
        /// <para>
        /// It drives the REAL <see cref="RaidGarrisonSpawner.FoldDifficulty"/> and the REAL
        /// <see cref="GarrisonStatBlocks.ApplyLevelScale"/> rather than restating their
        /// arithmetic: an oracle that re-implements the code under test certifies only itself.
        /// </para>
        /// </summary>
        private static void Case2_Folds(List<string> failures, StringBuilder log)
        {
            // The WO's worked example, as a PERCENT ONLY. The camp's authored multiplier is
            // read, never assumed, so this case survives the owner tuning the JSON by hand.
            const int Pct = 160;

            if (!TryAuthored(failures, "folds", RaidLootTunables.CampIdBastion,
                             out float authored, out int authoredOffset, out int _))
                return;

            var r = RaidDifficultyTunables.ResolveFrom(authored, authoredOffset, Pct,
                                                       RaidDifficultyTunables.UseAuthoredOffsetSentinel);

            float wantEffective = authored * (Pct / 100f);
            log.AppendLine("  folds: authored x" + authored.ToString("F2") + " at " + Pct + "% -> x" +
                           r.EffectiveMultiplier.ToString("F3") + " (want x" + wantEffective.ToString("F3") + ")");

            if (Mathf.Abs(r.EffectiveMultiplier - wantEffective) > Eps)
                failures.Add("[folds] " + Pct + " percent on an authored multiplier of " + authored +
                             " resolved to " + r.EffectiveMultiplier + ", expected " + wantEffective +
                             ". The percent is MULTIPLICATIVE on the authored value; if it is not, " +
                             "every number the owner types means something other than what the " +
                             "Command Center card says it does.");

            if (r.MultiplierSource != RaidDifficultyTunables.SourceRemote)
                failures.Add("[folds] an overridden multiplier still labelled its source '" +
                             r.MultiplierSource + "'. The RAID START line is how a felt-test knows " +
                             "whether the row it just set actually reached the raid.");

            if (r.EffectiveMultiplier <= authored)
                failures.Add("[folds] a percent above 100 did not make the camp HARDER (" +
                             r.EffectiveMultiplier + " vs authored " + authored + "). This is the " +
                             "whole point of the ticket.");

            // --- and now through the REAL fold, on a REAL stat block --------------
            // FoldDifficulty touches HP *and* ContactDamage. Asserting only HP would pass a
            // build in which the damage half had been dropped, and a raid that is tankier but
            // not more dangerous is a longer chore, not a harder fight.
            var baseline = GarrisonStatBlocks.BuildTypedDef("orc-berserker", 1);
            var scaled = GarrisonStatBlocks.BuildTypedDef("orc-berserker", 1);
            if (baseline == null || scaled == null)
            {
                failures.Add("[folds] GarrisonStatBlocks.BuildTypedDef returned null for a live " +
                             "composition id - the fold cannot be proven against a real defender.");
                return;
            }
            if (!(baseline.Hp > 0f) || !(baseline.ContactDamage > 0f))
            {
                failures.Add("[folds] the baseline defender has Hp=" + baseline.Hp + " ContactDamage=" +
                             baseline.ContactDamage + "; a multiplier cannot be proven against zero.");
                return;
            }

            float hp0 = baseline.Hp;
            float dmg0 = baseline.ContactDamage;

            RaidGarrisonSpawner.FoldDifficulty(scaled, r.EffectiveMultiplier);

            if (Mathf.Abs(scaled.Hp - hp0 * r.EffectiveMultiplier) > hp0 * 0.001f)
                failures.Add("[folds] FoldDifficulty at x" + r.EffectiveMultiplier + " produced Hp " +
                             scaled.Hp + " from " + hp0 + ", expected " + (hp0 * r.EffectiveMultiplier) + ".");
            if (Mathf.Abs(scaled.ContactDamage - dmg0 * r.EffectiveMultiplier) > dmg0 * 0.001f)
                failures.Add("[folds] FoldDifficulty at x" + r.EffectiveMultiplier + " produced " +
                             "ContactDamage " + scaled.ContactDamage + " from " + dmg0 + ", expected " +
                             (dmg0 * r.EffectiveMultiplier) + ". FoldDifficulty scales BOTH axes; a " +
                             "tankier-but-not-deadlier garrison is a longer chore, not a harder fight.");

            // --- the two folds COMPOUND, and that is the owner-visible consequence ---
            // The level scale rides on top of the difficulty fold. The suite asserts the
            // COMPOUNDING (strictly greater than either alone), never a literal ratio, because
            // the per-level curve is content that is allowed to be re-tuned.
            var compounded = GarrisonStatBlocks.BuildTypedDef("orc-berserker", 1);
            RaidGarrisonSpawner.FoldDifficulty(compounded, r.EffectiveMultiplier);
            GarrisonStatBlocks.ApplyLevelScale(compounded, 9);

            var levelOnly = GarrisonStatBlocks.BuildTypedDef("orc-berserker", 1);
            GarrisonStatBlocks.ApplyLevelScale(levelOnly, 9);

            log.AppendLine("  folds: hp base " + hp0.ToString("F1") + " -> diff " + scaled.Hp.ToString("F1") +
                           " -> +Lv9 " + compounded.Hp.ToString("F1") + " (level alone " +
                           levelOnly.Hp.ToString("F1") + ")");

            if (!(compounded.Hp > scaled.Hp) || !(compounded.Hp > levelOnly.Hp))
                failures.Add("[folds] the difficulty fold and the level scale do not COMPOUND on HP " +
                             "(diff-only " + scaled.Hp + ", level-only " + levelOnly.Hp + ", both " +
                             compounded.Hp + "). Both knobs are meant to stack on top of each other, " +
                             "which is why the Command Center card warns that moving both at once " +
                             "multiplies.");
            if (!(compounded.ContactDamage > scaled.ContactDamage) ||
                !(compounded.ContactDamage > levelOnly.ContactDamage))
                failures.Add("[folds] the difficulty fold and the level scale do not compound on " +
                             "contact damage (diff-only " + scaled.ContactDamage + ", level-only " +
                             levelOnly.ContactDamage + ", both " + compounded.ContactDamage + ").");
        }

        // =====================================================================
        //  Case 3 - REPLACE, NOT ADD. The ruling, pinned.
        // =====================================================================
        /// <summary>
        /// ⭐ THE CASE THAT PINS A RULING RATHER THAN A BEHAVIOUR. The owner's 2026-09-16 seed
        /// for the Bastion offset was given against REPLACE semantics; under delta semantics the
        /// same number would mean a larger offset, and the raid she asked for is not the raid she
        /// would get. A <c>levelOffsetDelta</c> shape was considered and rejected for exactly
        /// this reason, so the semantics are pinned here where a "simplification" trips over it.
        /// </summary>
        private static void Case3_OffsetReplace(List<string> failures, StringBuilder log)
        {
            if (!TryAuthored(failures, "offset-replace", RaidLootTunables.CampIdBastion,
                             out float authored, out int authoredOffset, out int baseEnemyLevel))
                return;

            // ⛔ THE REPLACE-vs-ADD DISCRIMINATION RUNS ON SUPPLIED LITERALS, NOT ON THE
            // AUTHORED OFFSET, and the reason is a false-fail this case would otherwise carry.
            // With an authored offset of 0, REPLACE gives the row value and ADD gives
            // 0 + row - the SAME number - so the case could neither distinguish the two nor
            // fail honestly; it would simply go red on correct code the day the owner authored a
            // 0. These two literals are legitimately literals: this case asserts SEMANTICS, not
            // any camp's content, and 4 -> 2 separates REPLACE (2) from ADD (6) unambiguously.
            const int ProbeAuthoredOffset = 4;
            const int ProbeRowOffset = 2;

            var replaced = RaidDifficultyTunables.ResolveFrom(authored, ProbeAuthoredOffset,
                                                              RaidDifficultyTunables.IdentityMultiplierPct,
                                                              ProbeRowOffset);
            log.AppendLine("  offset: authored " + ProbeAuthoredOffset + " + row " + ProbeRowOffset +
                           " -> " + replaced.EffectiveLevelOffset + " (" + replaced.OffsetSource +
                           "); REPLACE wants " + ProbeRowOffset + ", ADD would give " +
                           (ProbeAuthoredOffset + ProbeRowOffset));

            if (replaced.EffectiveLevelOffset == ProbeAuthoredOffset + ProbeRowOffset)
                failures.Add("[offset-replace] the row ADDED to the authored offset (" +
                             ProbeAuthoredOffset + " + " + ProbeRowOffset + " = " +
                             replaced.EffectiveLevelOffset + "). The owner's ruled seed was given " +
                             "against REPLACE semantics, so under add-semantics the number she set " +
                             "means a harder raid than the one she asked for. REPLACE is the ruling, " +
                             "not a preference, and a levelOffsetDelta shape was considered and " +
                             "rejected for exactly this reason.");

            if (replaced.EffectiveLevelOffset != ProbeRowOffset)
                failures.Add("[offset-replace] a row of " + ProbeRowOffset + " produced an effective " +
                             "offset of " + replaced.EffectiveLevelOffset + ". The number written IS " +
                             "the offset.");

            if (replaced.OffsetSource != RaidDifficultyTunables.SourceRemote)
                failures.Add("[offset-replace] an overridden offset labelled its source '" +
                             replaced.OffsetSource + "'.");

            // --- the SENTINEL falls back to the authored value ------------------
            var sentinel = RaidDifficultyTunables.ResolveFrom(authored, authoredOffset,
                                                              RaidDifficultyTunables.IdentityMultiplierPct,
                                                              RaidDifficultyTunables.UseAuthoredOffsetSentinel);
            if (sentinel.EffectiveLevelOffset != authoredOffset)
                failures.Add("[offset-replace] the sentinel " +
                             RaidDifficultyTunables.UseAuthoredOffsetSentinel + " produced an offset of " +
                             sentinel.EffectiveLevelOffset + " instead of the authored " + authoredOffset +
                             ". The sentinel is the documented way back to today's raid, alongside " +
                             "deleting the row, and it must NEVER be clamped into range - a clamp here " +
                             "would silently make every camp harder than authored.");
            if (sentinel.OffsetSource != RaidDifficultyTunables.SourceJson)
                failures.Add("[offset-replace] the sentinel labelled its source '" +
                             sentinel.OffsetSource + "', not '" + RaidDifficultyTunables.SourceJson + "'.");

            // --- the baseEnemyLevel FLOOR still protects a low-level hero -------
            // The spawner's arithmetic, restated ONCE here because this case is precisely about
            // the interaction between the knob and that floor. The floor is read from the
            // catalog, so this half DOES track the authored content - as it must, since the
            // claim is about that camp's own floor.
            const int LowHeroLevel = 1;

            // The most negative LEGAL offset an operator can set. If the floor holds for that,
            // it holds for every legal value - and a knob must never be usable to make a camp
            // EASIER than the level its author floored it at.
            var negative = RaidDifficultyTunables.ResolveFrom(authored, authoredOffset,
                                                              RaidDifficultyTunables.IdentityMultiplierPct,
                                                              RaidDifficultyTunables.MinLevelOffset);
            int flooredNeg = Mathf.Max(baseEnemyLevel, LowHeroLevel + negative.EffectiveLevelOffset);
            if (flooredNeg != baseEnemyLevel)
                failures.Add("[offset-replace] the most negative legal offset (" +
                             RaidDifficultyTunables.MinLevelOffset + ") produced level " + flooredNeg +
                             " for a level-" + LowHeroLevel + " hero, not the authored floor " +
                             baseEnemyLevel + ". A difficulty knob must never be usable to make a " +
                             "camp trivially easy below what its author intended.");

            // And the floor is a FLOOR, not a cap: a high-level hero must still be met at
            // playerLevel + offset once that exceeds it, or the knob would do nothing for the
            // very hero the felt report was about.
            int highHero = baseEnemyLevel + 10;
            int flooredHigh = Mathf.Max(baseEnemyLevel, highHero + replaced.EffectiveLevelOffset);
            log.AppendLine("  floor: lv" + LowHeroLevel + " -> " + flooredNeg + " (authored floor " +
                           baseEnemyLevel + "); lv" + highHero + " at offset " +
                           replaced.EffectiveLevelOffset + " -> " + flooredHigh);
            if (flooredHigh != highHero + replaced.EffectiveLevelOffset)
                failures.Add("[offset-replace] a level-" + highHero + " hero was met at level " +
                             flooredHigh + " instead of " + (highHero + replaced.EffectiveLevelOffset) +
                             ". baseEnemyLevel is a FLOOR, not a cap; if it capped, the offset knob " +
                             "would do nothing for exactly the strong hero this ticket exists for.");
        }

        // =====================================================================
        //  Case 4 - AN UNKNOWN CAMP KEEPS WHAT IT WAS GIVEN.
        // =====================================================================
        /// <summary>
        /// The <c>CoinsBaseFor</c> precedent, with a sharper failure: falling back to zero would
        /// not merely mis-pay a camp, it would hit <c>FoldDifficulty</c>'s <c>&lt;=0</c> no-op and
        /// read as "difficulty applied" while applying nothing. Silence is the failure mode
        /// CLAUDE.md section 12 exists to forbid, so a zero result FAILS this case by name.
        /// </summary>
        private static void Case4_UnknownCamp(List<string> failures, StringBuilder log)
        {
            RemoteTunables.Clear();

            const float Authored = 1.75f;
            const int AuthoredOffset = 4;
            string[] unknown = { "no_such_camp", "", null, "   " };

            foreach (var id in unknown)
            {
                var r = RaidDifficultyTunables.Resolve(id, Authored, AuthoredOffset);
                string shown = id == null ? "(null)" : "'" + id + "'";
                log.AppendLine("  unknown " + shown + " -> x" + r.EffectiveMultiplier.ToString("F2") +
                               " off " + r.EffectiveLevelOffset);

                if (r.EffectiveMultiplier != Authored)
                    failures.Add("[unknown-camp] " + shown + " resolved a multiplier of " +
                                 r.EffectiveMultiplier + ", not the " + Authored + " it was PASSED. An " +
                                 "unknown id must keep the authored values - never another camp's knob, " +
                                 "which would make a raid secretly wear a different camp's tuning." +
                                 (r.EffectiveMultiplier == 0f
                                     ? " AND IT RESOLVED ZERO, which is the worst answer available: " +
                                       "FoldDifficulty no-ops at <=0, so this would look like " +
                                       "difficulty was applied while applying nothing."
                                     : ""));

                if (r.EffectiveLevelOffset != AuthoredOffset)
                    failures.Add("[unknown-camp] " + shown + " resolved an offset of " +
                                 r.EffectiveLevelOffset + ", not the " + AuthoredOffset + " it was passed.");

                if (!r.IsIdentity)
                    failures.Add("[unknown-camp] " + shown + " did not report IsIdentity, so the RAID " +
                                 "START line would claim a remote override on a camp that has no knobs.");
            }
        }

        // =====================================================================
        //  Case 5 - THE CLAMPS, AND THE RAID USES THE CLAMPED VALUE.
        // =====================================================================
        /// <summary>
        /// A knob is an operator surface on a phone: it can receive a typo, a paste, or a number
        /// from a future build. The clamp is the difference between a bad experiment and an
        /// unplayable raid, and it lives at the consumer so there is exactly ONE answer to
        /// "what does 40000 do".
        /// </summary>
        private static void Case5_Clamps(List<string> failures, StringBuilder log)
        {
            const float Authored = 1.25f;
            const int AuthoredOffset = 2;

            var hi = RaidDifficultyTunables.ResolveFrom(Authored, AuthoredOffset, 40000, 9999);
            var lo = RaidDifficultyTunables.ResolveFrom(Authored, AuthoredOffset, -40000, -500);

            log.AppendLine("  clamps: 40000% -> " + hi.MultiplierPct + "%, 9999 off -> " +
                           hi.EffectiveLevelOffset + "; -40000% -> " + lo.MultiplierPct + "%, -500 off -> " +
                           lo.EffectiveLevelOffset);

            if (hi.MultiplierPct != RaidDifficultyTunables.MaxMultiplierPct)
                failures.Add("[clamps] a percent of 40000 resolved to " + hi.MultiplierPct +
                             ", not the ceiling " + RaidDifficultyTunables.MaxMultiplierPct + ".");
            if (Mathf.Abs(hi.EffectiveMultiplier -
                          Authored * (RaidDifficultyTunables.MaxMultiplierPct / 100f)) > Eps)
                failures.Add("[clamps] the raid did NOT use the clamped percent: an absurd row " +
                             "produced an effective multiplier of " + hi.EffectiveMultiplier +
                             ". A clamp that reports but does not apply is not a clamp.");
            if (hi.EffectiveLevelOffset != RaidDifficultyTunables.MaxLevelOffset)
                failures.Add("[clamps] an offset of 9999 resolved to " + hi.EffectiveLevelOffset +
                             ", not the ceiling " + RaidDifficultyTunables.MaxLevelOffset + ".");

            if (lo.MultiplierPct != RaidDifficultyTunables.MinMultiplierPct)
                failures.Add("[clamps] a percent of -40000 resolved to " + lo.MultiplierPct +
                             ", not the floor " + RaidDifficultyTunables.MinMultiplierPct +
                             ". A non-positive multiplier would reach FoldDifficulty's no-op and " +
                             "silently disable the fold entirely.");
            if (!(lo.EffectiveMultiplier > 0f))
                failures.Add("[clamps] a hostile negative percent drove the effective multiplier to " +
                             lo.EffectiveMultiplier + ". It must never be non-positive: FoldDifficulty " +
                             "no-ops there, so the raid would read as tuned while being untouched.");
            if (lo.EffectiveLevelOffset != RaidDifficultyTunables.MinLevelOffset)
                failures.Add("[clamps] an offset of -500 resolved to " + lo.EffectiveLevelOffset +
                             ", not the floor " + RaidDifficultyTunables.MinLevelOffset + ".");

            // The SENTINEL is exempt, and this is the one exemption. It sits far below the
            // offset floor on purpose, so a clamp applied before the sentinel test would turn
            // "use the authored value" into "the hardest legal easy value" for every camp at once.
            var sentinel = RaidDifficultyTunables.ResolveFrom(Authored, AuthoredOffset,
                                                              RaidDifficultyTunables.IdentityMultiplierPct,
                                                              RaidDifficultyTunables.UseAuthoredOffsetSentinel);
            if (sentinel.EffectiveLevelOffset != AuthoredOffset)
                failures.Add("[clamps] the sentinel was CLAMPED into range (offset " +
                             sentinel.EffectiveLevelOffset + " instead of the authored " + AuthoredOffset +
                             "). The sentinel test must come BEFORE the clamp, or every camp with no " +
                             "row silently changes difficulty.");

            // Identity must survive an authored multiplier that is not positive: the guard
            // answers 1 rather than letting a zero through to the fold's no-op.
            var guarded = RaidDifficultyTunables.ResolveFrom(0f, AuthoredOffset,
                                                             RaidDifficultyTunables.IdentityMultiplierPct,
                                                             RaidDifficultyTunables.UseAuthoredOffsetSentinel);
            if (!(guarded.EffectiveMultiplier > 0f))
                failures.Add("[clamps] an authored multiplier of 0 survived as " +
                             guarded.EffectiveMultiplier + ". The spawner's long-standing >0 guard must " +
                             "hold at this boundary too, or a percent knob multiplies a zero into " +
                             "another zero and the fold silently does nothing.");
        }

        // =====================================================================
        //  Case 6 - THE EIGHT DEFAULTS ARE IDENTITY.
        // =====================================================================
        /// <summary>
        /// The "no felt change until the owner sets a DB value" invariant, asserted on the
        /// REGISTRY rather than on a resolve, so it holds for the Command Center and the server
        /// allowlist as well as for the game. The expected values are this file's own literals -
        /// an oracle that read them off the thing it is checking would certify nothing.
        /// </summary>
        private static void Case6_DefaultsAreIdentity(List<string> failures, StringBuilder log)
        {
            string[] pctKeys =
            {
                RemoteTunables.KeyRaidDifficultyMultPctCamp1,
                RemoteTunables.KeyRaidDifficultyMultPctCamp2,
                RemoteTunables.KeyRaidDifficultyMultPctCamp3,
                RemoteTunables.KeyRaidDifficultyMultPctBastion,
            };
            string[] offsetKeys =
            {
                RemoteTunables.KeyRaidLevelOffsetCamp1,
                RemoteTunables.KeyRaidLevelOffsetCamp2,
                RemoteTunables.KeyRaidLevelOffsetCamp3,
                RemoteTunables.KeyRaidLevelOffsetBastion,
            };

            foreach (var key in pctKeys) AssertDefault(failures, log, key, 100);
            foreach (var key in offsetKeys) AssertDefault(failures, log, key, -999);

            // And the two named consts the consumer reads agree with those literals, so the
            // bare-int-literal duplication the manifest generator forces cannot drift unseen.
            if (RaidDifficultyTunables.IdentityMultiplierPct != 100)
                failures.Add("[defaults-identity] RaidDifficultyTunables.IdentityMultiplierPct is " +
                             RaidDifficultyTunables.IdentityMultiplierPct + ", not 100. The registry " +
                             "defaults must repeat the literal (the manifest generator resolves only a " +
                             "bare int const), so this is where the two shapes are held together.");
            if (RaidDifficultyTunables.UseAuthoredOffsetSentinel != -999)
                failures.Add("[defaults-identity] RaidDifficultyTunables.UseAuthoredOffsetSentinel is " +
                             RaidDifficultyTunables.UseAuthoredOffsetSentinel + ", not -999.");
            if (RaidDifficultyTunables.UseAuthoredOffsetSentinel >= RaidDifficultyTunables.MinLevelOffset)
                failures.Add("[defaults-identity] the sentinel (" +
                             RaidDifficultyTunables.UseAuthoredOffsetSentinel + ") is not strictly below " +
                             "the offset floor (" + RaidDifficultyTunables.MinLevelOffset + "), so a " +
                             "legitimate operator value could collide with 'use the authored value'.");
        }

        private static void AssertDefault(List<string> failures, StringBuilder log,
                                          string key, int expected)
        {
            var spec = RemoteTunables.SpecFor(key);
            if (spec == null)
            {
                failures.Add("[defaults-identity] knob '" + key + "' is not in RemoteTunables.Registry - " +
                             "it has no default, the server refuses every write to it, and the Command " +
                             "Center cannot show it. The owner would set a row and nothing would happen.");
                return;
            }
            log.AppendLine("  default " + key + " = " + spec.Default);
            if (spec.Default != expected)
                failures.Add("[defaults-identity] knob '" + key + "' ships at " + spec.Default +
                             ", not the identity value " + expected + ". A non-identity default means " +
                             "this ticket changed how every raid feels for every player with no row " +
                             "and no network - which is precisely what it promised not to do.");
        }
    }
}

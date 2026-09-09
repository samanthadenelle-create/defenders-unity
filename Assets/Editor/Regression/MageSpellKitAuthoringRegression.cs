// =============================================================================
// MageSpellKitAuthoringRegression [mage-spell-kit]
// -----------------------------------------------------------------------------
// Assembly: DeNelle.EditorRegression (references DeNelle.Core + DeNelle.Village).
//
// WO-1019 PART B (owner rulings 2026-08-10, verbatim: "as Mage you try to lure out
// and kill one at a time" / "change mend to drain" / "make meteor strike into poison"
// / "thats the best part, they unlock in the skill tree and hot swap bar").
//
// Thrain's kit is now pull -> soften -> finish -> sustain, single-target throughout:
//
//   Q mage.fireball  strike      the pull + the primary nuke   (UNCHANGED)
//   W mage.shell     shield      the trade window              (UNCHANGED)
//   E mage.drain     drainshot   sustain FROM fighting         (was mage.heal / Mend)
//   R mage.poison    dot         the ultimate-scale commitment (was mage.meteor)
//   + mage.thunder   strike      learnable burst finisher, pool only
//
// THE THING THIS SUITE EXISTS TO PREVENT, above all others: an ability the player
// UNLOCKS but cannot EQUIP. WO-1019 Part A made both persisted rails class-filtered
// (AbilityCatalog.IsUsableByClass, answered from the abilities.json CLASS KEY an id
// is authored under). A new spell that is not owned by the mage class is therefore
// DROPPED on load - silently, from the owner's point of view - so the class-ownership
// case below is load-bearing, not paperwork.
//
// WHAT IT PROVES HEADLESSLY, AND WHAT IT CANNOT:
//
//   (a) DATA CONTRACT, from the real catalog (AbilityCatalog.Reload + Find/FindById):
//       the default bar's ids, that every default and every new pool spell is
//       MAGE-OWNED, that the three new ids carry the fields their effect shape
//       actually consumes, and that no id is authored twice anywhere in the file
//       (a duplicate id makes FindById order-dependent and lets two rows drift).
//
//   (b) EFFECT-HANDLER REACHABILITY, by source lint of HeroAbilities.ResolveEffect:
//       "dot" and "drainshot" must still have a raw-string case, and "strike" must
//       still be an AbilityEffect the enum switch carries. NO NEW GAMEPLAY CODE was
//       written for Part B - the three spells reuse ResolveDot (precedent
//       knight.emberbrand-throw), ResolveDrainshot (precedent ranger.healing-shot,
//       which already heals the caster by damage DEALT, so "steal health" is real
//       today) and the core Strike branch (precedent knight.thunderbolt). If someone
//       deletes one of those cases, these spells become silent no-ops; the lint is
//       the only cheap alarm for that.
//
//   (c) THE DISPLACED DEFAULTS SURVIVED. mage.heal and mage.meteor were MOVED into
//       classes.mage-skills, not deleted, so hero-talents.json mage.t3n1 "Cataclysm
//       Prep" (modifyAbility -> mage.meteor) still resolves its target.
//
//   (d) DUAL-COPY LAW: Resources/ and StreamingAssets/ abilities.json byte-identical.
//
//   (e) THE UNLOCK-REACHABILITY LEDGER. A pool spell with no kind:"skill" node in
//       hero-talents.json is content nobody can reach. Two pool spells were ALREADY
//       in that state before this WO (mage.frost-nova, mage.arcane-bolt); the three
//       WO-1019 additions join them pending the owner's tree ruling (placement, tier
//       and cost are her design, not CLI's - and the mage tree is a FULL 5x4 grid, so
//       there is no free slot to quietly drop them into). The case pins that list
//       EXACTLY, so the backlog cannot silently grow and cannot silently be "fixed"
//       by inventing tree placement.
//
//   NOT provable here: that the numbers are RIGHT. Every damage / dotDamage /
//   dotSeconds / cooldown / manaCost / range / castSeconds value and all three NAMES
//   are <<DRAFT - owner tuning pass>>. This suite pins SHAPE and REACHABILITY, never
//   balance - balance is the owner's felt-verify (PO closes, docs/TICKET_PIPELINE.md).
//
// Markers: MAGE_SPELL_KIT_OK / MAGE_SPELL_KIT_FAIL.
// Standalone: run-unity-method DeNelle.Editor.Regression.MageSpellKitAuthoringRegression.RunAll
// Covenant contract Run(out reason) is DataRegression-shaped; wiring into
// DataRegression.RunAll is left to the committer (that file is lane-fenced).
// =============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEngine;
using DeNelle.Village;

namespace DeNelle.Editor.Regression
{
    public static class MageSpellKitAuthoringRegression
    {
        private const string StreamingAbilities = "Assets/StreamingAssets/Data/Canonical/abilities.json";
        private const string ResourcesAbilities = "Assets/Resources/Data/Canonical/abilities.json";
        private const string TalentsJson        = "Assets/StreamingAssets/Data/Canonical/hero-talents.json";
        private const string HeroAbilitiesSrc   = "Assets/_Modules/Village/Hero/HeroAbilities.cs";

        /// <summary>Standalone batch entry - prints the marker.</summary>
        public static void RunAll()
        {
            if (Run(out string reason)) Debug.Log("MAGE_SPELL_KIT_OK - " + reason);
            else Debug.LogError("MAGE_SPELL_KIT_FAIL: " + reason);
        }

        /// <summary>Covenant contract (DataRegression-shaped). Never throws.</summary>
        public static bool Run(out string reason)
        {
            var failures = new List<string>();
            try
            {
                AbilityCatalog.Reload();
                Case(failures, "default-bar",      () => Case1_DefaultBar(failures));
                Case(failures, "new-spell-shape",  () => Case2_NewSpellShape(failures));
                Case(failures, "class-ownership",  () => Case3_ClassOwnership(failures));
                Case(failures, "displaced-ids",    () => Case4_DisplacedIdsSurvive(failures));
                Case(failures, "no-duplicate-ids", () => Case5_NoDuplicateIds(failures));
                Case(failures, "dual-copy",        () => Case6_DualCopy(failures));
                Case(failures, "unlock-ledger",    () => Case7_UnlockReachabilityLedger(failures));
            }
            catch (Exception ex)
            {
                failures.Add("[suite] THREW " + ex.GetType().Name + ": " + ex.Message);
            }

            if (failures.Count == 0)
            {
                reason = "MAGE SPELL KIT OK - Thrain's default bar is Q fireball / W shell / " +
                         "E drain / R poison, all mage-owned and all magic; mage.poison, mage.drain " +
                         "and mage.thunder are authored with the fields their SHIPPED effect handlers " +
                         "consume (dot / drainshot / strike - no new gameplay code); every one of them " +
                         "passes the Part A class filter so the hot-swap rail cannot drop them; the " +
                         "displaced mage.heal + mage.meteor still resolve for hero-talents mage.t3n1; " +
                         "no duplicate ids; both abilities.json copies byte-identical.";
                return true;
            }
            reason = "mage-spell-kit FAIL x" + failures.Count + ": " + string.Join(" | ", failures);
            return false;
        }

        private static void Case(List<string> failures, string name, Action body)
        {
            try { body(); }
            catch (Exception ex) { failures.Add("[" + name + "] THREW " + ex.GetType().Name + ": " + ex.Message); }
        }

        // =====================================================================
        //  Case 1 - the OWNER-RULED default bar
        // =====================================================================

        private static void Case1_DefaultBar(List<string> failures)
        {
            var expected = new[]
            {
                new { Slot = AbilitySlot.Q, Id = "mage.fireball", Effect = "strike"    },
                new { Slot = AbilitySlot.W, Id = "mage.shell",    Effect = "shield"    },
                new { Slot = AbilitySlot.E, Id = "mage.drain",    Effect = "drainshot" },
                new { Slot = AbilitySlot.R, Id = "mage.poison",   Effect = "dot"       },
            };

            for (int i = 0; i < expected.Length; i++)
            {
                var def = AbilityCatalog.Find("mage", expected[i].Slot);
                if (def == null)
                {
                    failures.Add("[default-bar] classes.mage.abilities has NO def for slot " +
                                 expected[i].Slot + " - Thrain's default bar is incomplete.");
                    continue;
                }
                if (!IdIs(def.Id, expected[i].Id))
                    failures.Add("[default-bar] slot " + expected[i].Slot + " is '" + (def.Id ?? "<null>") +
                                 "', expected '" + expected[i].Id + "'. The E/R contents are an OWNER RULING " +
                                 "(2026-08-10 'change mend to drain' / 'make meteor strike into poison') - " +
                                 "a change here needs another ruling, not a silent edit.");
                if (!EffectIs(def.Effect, expected[i].Effect))
                    failures.Add("[default-bar] slot " + expected[i].Slot + " ('" + (def.Id ?? "<null>") +
                                 "') has effect '" + (def.Effect ?? "<null>") + "', expected '" +
                                 expected[i].Effect + "' - the ruled verb changed shape.");

                // "he should have all magic spells": every default must be MAGE-owned.
                if (!AbilityCatalog.IsUsableByClass(def.Id, "mage"))
                    failures.Add("[default-bar] slot " + expected[i].Slot + " id '" + (def.Id ?? "<null>") +
                                 "' is not authored under the mage class (owner='" +
                                 (AbilityCatalog.OwningClassOf(def.Id) ?? "<unknown>") + "').");
            }

            // The owner's "nothing explicit for dps": Q must be a real damage ability at range.
            var q = AbilityCatalog.Find("mage", AbilitySlot.Q);
            if (q != null)
            {
                if (q.Damage <= 0f)
                    failures.Add("[default-bar] the Mage's Q '" + (q.Id ?? "<null>") + "' deals " + q.Damage +
                                 " damage - the class would have NO explicit DPS default.");
                if (q.Range <= 0f)
                    failures.Add("[default-bar] the Mage's Q '" + (q.Id ?? "<null>") + "' has range " +
                                 q.Range + " - a caster's primary must reach.");
            }

            // E is now OFFENSIVE where Mend was a self-heal. That is the ruled feel change
            // (drainshot yaws the hero and drives the attack trigger); pin it so a later
            // "restore Mend" cannot land as a silent data edit.
            var e = AbilityCatalog.Find("mage", AbilitySlot.E);
            if (e != null && (e.Damage <= 0f || e.Range <= 0f))
                failures.Add("[default-bar] E '" + (e.Id ?? "<null>") + "' has damage " + e.Damage +
                             " / range " + e.Range + " - Drain must be an OFFENSIVE cast that trades " +
                             "damage for health, not a self-heal wearing a new name.");
        }

        // =====================================================================
        //  Case 2 - the THREE NEW SPELLS: shape + a shipped effect handler
        // =====================================================================

        private static void Case2_NewSpellShape(List<string> failures)
        {
            string src = ReadOrFail(failures, "new-spell-shape", HeroAbilitiesSrc);

            // The two raw-string handlers Part B leans on must still exist. (strike is the
            // enum default branch, so it cannot be "missing" the same way.)
            if (src != null)
            {
                if (!Regex.IsMatch(src, "case\\s+\"dot\"\\s*:"))
                    failures.Add("[new-spell-shape] HeroAbilities.ResolveEffect no longer carries case \"dot\" - " +
                                 "mage.poison (the R ultimate) would resolve as a plain strike and its whole " +
                                 "damage budget (dotDamage x dotSeconds) would vanish.");
                if (!Regex.IsMatch(src, "case\\s+\"drainshot\"\\s*:"))
                    failures.Add("[new-spell-shape] HeroAbilities.ResolveEffect no longer carries case \"drainshot\" - " +
                                 "mage.drain would deal damage and heal NOTHING, making 'steal health' flavour text.");
            }

            // mage.poison - single-target DoT, ultimate-scale. Damage lives in dotDamage x dotSeconds.
            var poison = AbilityCatalog.FindById("mage.poison");
            if (poison == null)
                failures.Add("[new-spell-shape] 'mage.poison' is not in the catalog at all.");
            else
            {
                RequirePositive(failures, poison.Damage,     "mage.poison", "damage (the initial hit)");
                RequirePositive(failures, poison.DotDamage,  "mage.poison", "dotDamage");
                RequirePositive(failures, poison.DotSeconds, "mage.poison", "dotSeconds");
                RequirePositive(failures, poison.Range,      "mage.poison", "range");
                RequirePositive(failures, poison.Cooldown,   "mage.poison", "cooldown");
                if (!EffectIs(poison.Effect, "dot"))
                    failures.Add("[new-spell-shape] mage.poison effect is '" + (poison.Effect ?? "<null>") +
                                 "', expected 'dot' (ResolveDot, the knight.emberbrand-throw shape).");
                // The owner tagged exactly ONE VFX key for this spell, by her spelling.
                if (!string.Equals(poison.VfxCast, "Posion_Cast", StringComparison.Ordinal))
                    failures.Add("[new-spell-shape] mage.poison vfxCast is '" + (poison.VfxCast ?? "<null>") +
                                 "', expected the OWNER-TAGGED key 'Posion_Cast' spelled EXACTLY as she typed it " +
                                 "(Assets/Editor/VfxManualPicks.json, manual:true). Correcting her spelling breaks " +
                                 "the lookup; substituting a prefab breaks the owner-tags-VFX rule.");
                // Stages she has NOT tagged stay wired to nothing - never a look-alike.
                RequireUntagged(failures, poison.VfxProjectile, "mage.poison", "vfxProjectile");
                RequireUntagged(failures, poison.VfxImpact,     "mage.poison", "vfxImpact");
                RequireUntagged(failures, poison.VfxResidual,   "mage.poison", "vfxResidual");
            }

            // mage.drain - single-target damage that heals the caster by the damage DEALT.
            var drain = AbilityCatalog.FindById("mage.drain");
            if (drain == null)
                failures.Add("[new-spell-shape] 'mage.drain' is not in the catalog at all.");
            else
            {
                RequirePositive(failures, drain.Damage,   "mage.drain", "damage (also the heal, via damage dealt)");
                RequirePositive(failures, drain.Range,    "mage.drain", "range");
                RequirePositive(failures, drain.Cooldown, "mage.drain", "cooldown");
                if (!EffectIs(drain.Effect, "drainshot"))
                    failures.Add("[new-spell-shape] mage.drain effect is '" + (drain.Effect ?? "<null>") +
                                 "', expected 'drainshot' (ResolveDrainshot - the ranger.healing-shot shape, " +
                                 "which is what makes the heal REAL rather than authored flavour).");
                // Owner 2026-09-09 requested distinct spell identities; Drain now owns the
                // existing dark-cast manual pick while its travel/impact remain unchanged.
                if (!string.Equals(drain.VfxCast, "EnemyCast_Cast", StringComparison.Ordinal))
                    failures.Add("[new-spell-shape] mage.drain must use EnemyCast_Cast");
                RequireUntagged(failures, drain.VfxProjectile, "mage.drain", "vfxProjectile");
                RequireUntagged(failures, drain.VfxImpact,     "mage.drain", "vfxImpact");
                RequireUntagged(failures, drain.VfxResidual,   "mage.drain", "vfxResidual");
            }

            // mage.thunder - single-target burst finisher, learnable pool only.
            var thunder = AbilityCatalog.FindById("mage.thunder");
            if (thunder == null)
                failures.Add("[new-spell-shape] 'mage.thunder' is not in the catalog at all.");
            else
            {
                RequirePositive(failures, thunder.Damage,   "mage.thunder", "damage");
                RequirePositive(failures, thunder.Range,    "mage.thunder", "range");
                RequirePositive(failures, thunder.Cooldown, "mage.thunder", "cooldown");
                if (!EffectIs(thunder.Effect, "strike"))
                    failures.Add("[new-spell-shape] mage.thunder effect is '" + (thunder.Effect ?? "<null>") +
                                 "', expected 'strike' (the core Strike branch - knight.thunderbolt's shape).");
                if (thunder.EffectEnum != AbilityEffect.Strike)
                    failures.Add("[new-spell-shape] mage.thunder does not parse to AbilityEffect.Strike.");
                if (!string.Equals(thunder.VfxCast, "Thunderbolt_Cast", StringComparison.Ordinal))
                    failures.Add("[new-spell-shape] mage.thunder must use Thunderbolt_Cast");
                RequireUntagged(failures, thunder.VfxProjectile, "mage.thunder", "vfxProjectile");
                RequireUntagged(failures, thunder.VfxImpact,     "mage.thunder", "vfxImpact");
            }
        }

        // =====================================================================
        //  Case 3 - CLASS OWNERSHIP: the Part A filter must never drop these
        // =====================================================================

        private static void Case3_ClassOwnership(List<string> failures)
        {
            string[] ids = { "mage.poison", "mage.drain", "mage.thunder" };
            for (int i = 0; i < ids.Length; i++)
            {
                string owner = AbilityCatalog.OwningClassOf(ids[i]);
                if (owner != "mage")
                    failures.Add("[class-ownership] '" + ids[i] + "' is owned by '" + (owner ?? "<nobody>") +
                                 "', not 'mage'. WO-1019 Part A DROPS a bound id whose class does not match " +
                                 "the wearer, so the owner would unlock this spell in the tree and watch the " +
                                 "hot-swap rail silently refuse to keep it - the single most likely way this " +
                                 "feature ships broken.");

                if (!AbilityCatalog.IsUsableByClass(ids[i], "mage"))
                    failures.Add("[class-ownership] a MAGE may not equip '" + ids[i] + "'.");
                if (!AbilityCatalog.IsUsableByClass(ids[i], "cleric"))
                    failures.Add("[class-ownership] a CLERIC may not equip '" + ids[i] + "' - Elara aliases onto " +
                                 "the mage loadout (WO-226) and must inherit the same pool.");
                if (AbilityCatalog.IsUsableByClass(ids[i], "knight"))
                    failures.Add("[class-ownership] a KNIGHT may equip '" + ids[i] + "' - a mage spell must never " +
                                 "be bindable on another class's bar.");
                if (AbilityCatalog.IsUsableByClass(ids[i], "ranger"))
                    failures.Add("[class-ownership] a RANGER may equip '" + ids[i] + "'.");
            }
        }

        // =====================================================================
        //  Case 4 - the DISPLACED defaults were MOVED, not deleted
        // =====================================================================

        private static void Case4_DisplacedIdsSurvive(List<string> failures)
        {
            string[] moved = { "mage.heal", "mage.meteor" };
            for (int i = 0; i < moved.Length; i++)
            {
                var def = AbilityCatalog.FindById(moved[i]);
                if (def == null)
                {
                    failures.Add("[displaced-ids] '" + moved[i] + "' no longer exists. The E/R ruling DISPLACED " +
                                 "these two; it did not delete them. They must stay resolvable in the learnable " +
                                 "pool or every existing reference to the id breaks.");
                    continue;
                }
                if (AbilityCatalog.OwningClassOf(moved[i]) != "mage")
                    failures.Add("[displaced-ids] '" + moved[i] + "' is no longer mage-owned, so a mage who still " +
                                 "has it bound would have it dropped on load.");
            }

            // The one live referrer found at source: hero-talents.json mage.t3n1 "Cataclysm Prep"
            // (modifyAbility -> mage.meteor). Its TARGET must still resolve.
            string talents = ReadOrFail(failures, "displaced-ids", TalentsJson);
            if (talents != null && talents.Contains("\"ability\": \"mage.meteor\"") &&
                AbilityCatalog.FindById("mage.meteor") == null)
            {
                failures.Add("[displaced-ids] hero-talents.json still points a modifyAbility node at 'mage.meteor' " +
                             "but the id is gone from abilities.json - the talent would buff nothing.");
            }
        }

        // =====================================================================
        //  Case 5 - no id authored twice anywhere in the catalog
        // =====================================================================

        private static void Case5_NoDuplicateIds(List<string> failures)
        {
            string json = ReadOrFail(failures, "no-duplicate-ids", StreamingAbilities);
            if (json == null) return;

            var seen = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (Match m in Regex.Matches(json, "\"id\"\\s*:\\s*\"([^\"]+)\""))
            {
                string id = m.Groups[1].Value.Trim();
                seen[id] = seen.TryGetValue(id, out int n) ? n + 1 : 1;
            }
            foreach (var kvp in seen)
            {
                if (kvp.Value > 1)
                    failures.Add("[no-duplicate-ids] '" + kvp.Key + "' is authored " + kvp.Value + " times. " +
                                 "AbilityCatalog.FindById returns the FIRST match, so a duplicate makes the live " +
                                 "def depend on file order and lets the two rows drift apart in tuning. A default " +
                                 "and a pool entry must never both define the same id.");
            }
        }

        // =====================================================================
        //  Case 6 - DUAL-COPY LAW
        // =====================================================================

        private static void Case6_DualCopy(List<string> failures)
        {
            if (!File.Exists(StreamingAbilities) || !File.Exists(ResourcesAbilities))
            {
                failures.Add("[dual-copy] one of the two canonical abilities.json copies is missing " +
                             "(StreamingAssets exists=" + File.Exists(StreamingAbilities) +
                             ", Resources exists=" + File.Exists(ResourcesAbilities) + ").");
                return;
            }

            byte[] a = File.ReadAllBytes(StreamingAbilities);
            byte[] b = File.ReadAllBytes(ResourcesAbilities);
            if (a.Length != b.Length)
            {
                failures.Add("[dual-copy] abilities.json copies differ in LENGTH (" + a.Length + " vs " +
                             b.Length + " bytes) - the build reads one and the editor the other, so they " +
                             "must be byte-identical.");
                return;
            }
            for (int i = 0; i < a.Length; i++)
            {
                if (a[i] != b[i])
                {
                    failures.Add("[dual-copy] abilities.json copies diverge at byte " + i + ".");
                    return;
                }
            }

            // §0 mount-garble guard: a NUL in a canonical data file poisons the parse.
            for (int i = 0; i < a.Length; i++)
            {
                if (a[i] == 0)
                {
                    failures.Add("[dual-copy] abilities.json contains a NUL byte at offset " + i +
                                 " - mount-garbled write (CLAUDE.md §0).");
                    return;
                }
            }
        }

        // =====================================================================
        //  Case 7 - the UNLOCK-REACHABILITY LEDGER
        // -----------------------------------------------------------------------------
        //  Owner: "thats the best part, they unlock in the skill tree and hot swap bar."
        //  A pool spell reaches the hot-swap rail ONLY through a kind:"skill" talent node
        //  (HeroLoadoutVM: unlocked SKILL-kind nodes -> AbilityCatalog.FindById -> the
        //  assignable choices). A pool id with no node is content nobody can reach.
        //
        //  The mage tree is a FULL 5-slot x 4-tier grid (20 nodes, all occupied), so
        //  placing the new nodes means adding a tier or re-purposing an existing node -
        //  a DESIGN decision (placement / tier / cost), explicitly the owner's and not
        //  CLI's. Until she rules, the three sit in this pending ledger beside the two
        //  that were already unreachable before WO-1019. Pinning the list EXACTLY is the
        //  point: the backlog cannot silently grow, and it cannot silently be "closed"
        //  by a seat inventing tree placement.
        // =====================================================================

        private static readonly string[] PendingUnlockNode =
        {
            "mage.frost-nova",   // pre-existing gap (WO-861 authored it with no node)
            "mage.arcane-bolt",  // pre-existing gap (ditto)
            "mage.thunder",      // WO-1019 Part B - awaiting the owner's tree ruling
            "mage.heal",         // displaced default; a node is optional (it was free before)
            "mage.meteor",       // displaced default; mage.t3n1 already MODIFIES it
        };

        private static void Case7_UnlockReachabilityLedger(List<string> failures)
        {
            string talents = ReadOrFail(failures, "unlock-ledger", TalentsJson);
            string json    = ReadOrFail(failures, "unlock-ledger", StreamingAbilities);
            if (talents == null || json == null) return;

            // Every id authored in the mage POOL (classes.mage-skills), read from the file so
            // this cannot drift from what is actually shipped.
            var poolIds = new List<string>();
            int start = json.IndexOf("\"mage-skills\"", StringComparison.Ordinal);
            int end   = start >= 0 ? json.IndexOf("\"ranger-skills\"", start, StringComparison.Ordinal) : -1;
            if (start < 0 || end < 0)
            {
                failures.Add("[unlock-ledger] could not locate the classes.mage-skills block in abilities.json " +
                             "(looked for the \"mage-skills\" .. \"ranger-skills\" span) - the pool was renamed " +
                             "or re-ordered and this ledger can no longer be computed.");
                return;
            }
            foreach (Match m in Regex.Matches(json.Substring(start, end - start), "\"id\"\\s*:\\s*\"([^\"]+)\""))
                poolIds.Add(m.Groups[1].Value.Trim());

            var pending = new HashSet<string>(PendingUnlockNode, StringComparer.OrdinalIgnoreCase);
            var unexpected = new List<string>();
            var stale      = new List<string>();

            for (int i = 0; i < poolIds.Count; i++)
            {
                bool hasNode = talents.Contains("\"abilityId\": \"" + poolIds[i] + "\"");
                bool listed  = pending.Contains(poolIds[i]);
                if (!hasNode && !listed) unexpected.Add(poolIds[i]);
                if (hasNode && listed)   stale.Add(poolIds[i]);
            }

            if (unexpected.Count > 0)
                failures.Add("[unlock-ledger] mage pool spell(s) with NO kind:\"skill\" unlock node and NOT in the " +
                             "known-pending ledger: " + string.Join(", ", unexpected) + ". The player can never " +
                             "unlock them, so they can never reach the hot-swap rail - dead content. Either author " +
                             "the node (owner rules placement/tier/cost) or add the id to PendingUnlockNode with a " +
                             "reason.");

            if (stale.Count > 0)
                failures.Add("[unlock-ledger] " + string.Join(", ", stale) + " now HAS an unlock node but is still " +
                             "listed in PendingUnlockNode - remove it from the ledger so the list keeps meaning " +
                             "something.");
        }

        // =====================================================================
        //  helpers
        // =====================================================================

        private static bool IdIs(string actual, string expected) =>
            string.Equals((actual ?? string.Empty).Trim(), expected, StringComparison.OrdinalIgnoreCase);

        private static bool EffectIs(string actual, string expected) =>
            string.Equals((actual ?? string.Empty).Trim(), expected, StringComparison.OrdinalIgnoreCase);

        private static void RequirePositive(List<string> failures, float value, string id, string field)
        {
            if (value <= 0f)
                failures.Add("[new-spell-shape] " + id + " has " + field + "=" + value +
                             " - the effect handler reads that field, so a zero makes the spell a no-op. " +
                             "(The VALUE is <<DRAFT - owner tuning pass>>; only 'greater than zero' is pinned here.)");
        }

        /// <summary>
        /// The owner-tags-VFX rule (memory vfx-map-owner-tags-no-creative-pick), enforced:
        /// a stage she has NOT tagged stays wired to NOTHING. Filling it with a plausible
        /// look-alike is the exact failure that rule was written for.
        /// </summary>
        private static void RequireUntagged(List<string> failures, string value, string id, string field)
        {
            if (!string.IsNullOrEmpty(value))
                failures.Add("[new-spell-shape] " + id + " has " + field + "='" + value + "' but the owner has " +
                             "tagged NO key for that stage. Untagged stages stay EMPTY (an unknown/empty key " +
                             "no-ops in VFXManager.PlayKey) until she names the prefab - CLI never creative-picks.");
        }

        private static string ReadOrFail(List<string> failures, string caseName, string path)
        {
            try
            {
                if (!File.Exists(path))
                {
                    failures.Add("[" + caseName + "] missing file: " + path);
                    return null;
                }
                return File.ReadAllText(path);
            }
            catch (Exception ex)
            {
                failures.Add("[" + caseName + "] could not read " + path + ": " + ex.Message);
                return null;
            }
        }
    }
}

// =============================================================================
// ArmedHeroInvariantRegression [armed-hero] / [shield-improvement] / [defense-cap]
// -----------------------------------------------------------------------------
// Assembly: DeNelle.EditorRegression (references DeNelle.Core + DeNelle.Village).
//
// THREE owner-locked acceptance criteria (2026-08-02), one suite, one registration
// line. They share ONE headless GearLoadout probe - splitting them across three
// files would mean three copies of that probe, which is precisely the duplication
// that let the display cap and the applied cap drift apart in the first place.
//
//   [armed-hero]        AC-3. The armed-hero invariant, as an ORACLE and not a
//                       null-check. Owner's criterion verbatim: "After New Game or
//                       class select as Mage, main-hand weapon is non-null AND is a
//                       staff (job:mage, category staff). Assert Mage, Ranger and
//                       Knight all have a valid main-hand after ResetToNewGame +
//                       class select."
//                       The pre-existing armed-hero checks asserted ONLY non-null,
//                       which is exactly why the unarmed-Mage bug shipped green: a
//                       job:"any" SHIELD tied tripo_staff_a at damageMult 1.0, won
//                       BestWeapon on file order, and EnforceHandSlots then evicted
//                       it to the off hand and left the main hand EMPTY. This suite
//                       fails on a shield in the main hand.
//
//   [shield-improvement] AC-1. Improving a shield must actually reach the damage
//                       chain. GearProgression.Improve already ACCEPTED shields (they
//                       are weapons.json rows): it charged wood + iron off the
//                       ResourceLedger, wrote GameState.GearLevels[shieldId] and told
//                       the player "improved to Lv 5" - while GearLoadout.ApplyStats
//                       read the equipped off-hand's `defense` RAW. The purchased level
//                       never reached ArmorDefense, the one scalar HeroHealth.TakeDamage
//                       consumes. Silent, repeatable resource theft.
//
//   [defense-cap]       AC-2. The armor damage-reduction cap is 0.90 and DISPLAY and
//                       ENGINE agree on it, from ONE constant. Before this the applied
//                       clamp was 0.70 and ~13 display sites clamped at 0.90: the shop
//                       advertised "+85% def" and the engine granted 70%.
//
// -----------------------------------------------------------------------------
// HOW EACH CASE IS DRIVEN, AND WHY (this suite runs in EDITOR BATCHMODE with NO PLAY
// SESSION, so MonoBehaviour singletons are null and must never be assumed):
//
//   (b) HEADLESS OBJECT PROBE - the armed-hero + off-hand-summed cases build a real
//       GameObject, AddComponent<GearLoadout>() and call the PUBLIC BindOwnerClass(job),
//       which runs the REAL Refresh(): ResolveStarterMainHand -> GearCatalog.BestWeapon
//       -> ApplyPersistedEquip -> EnforceHandSlots -> ApplyStats. This needs no Awake
//       (Refresh re-caches its components lazily) and no scene. It is the highest
//       fidelity available: a re-implementation of that composition inside this file
//       could pass while the shipped path fails, which is worthless.
//
//   (a) PURE STATIC LOGIC - the level-scaling math (GearStatResolver), the level write
//       (GearProgression.ApplyImprove over a GameState this suite owns) and the cost
//       curve (GearProgression.ImproveCost) are all pure and parameterised. Driven
//       directly.
//
//   (c) SOURCE INVARIANT - exactly ONE link cannot run without a play session:
//       GearLoadout.ApplyStats reads the gear level off GameStateService.Instance.
//       That single seam is pinned by a source lint (comment-stripped), and the lint
//       is written to fail if the raw `.defense` read ever comes back.
//
// NOTHING here passes on a null. Every probe that cannot run is reported as a NOTE
// AND is covered by a case that always runs - a green tick over a missing singleton
// is worse than no tick at all.
//
// KNOWN DATA GAP, reported as a NOTE and not a failure (owner call): ten legacy
// weapons.json rows carry NO `category` field at all - knight_starter, knight_iron,
// knight_oath, knight_dawn, knight_flameblade, ranger_starter, cleric_starter and the
// three Aegis weapons. So "category is the one that class wields" is NOT assertable as
// an equality for Knight/Ranger/Cleric today; it IS for Mage (staff). The general rule
// below therefore asserts the category WHEN AUTHORED and otherwise demands the row be
// job-EXACT for that class - which still fails a job:"any" shield, i.e. still catches
// the bug. Author those ten categories and this suite tightens automatically.
//
// Markers: ARMED_HERO_OK / ARMED_HERO_FAIL.
// Standalone: run-unity-method DeNelle.Editor.Regression.ArmedHeroInvariantRegression.RunAll
// Covenant contract Run(out reason) is DataRegression-shaped; wiring into
// DataRegression.RunAll is left to the committer (that file is lane-fenced).
// =============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEngine;
using DeNelle.Core.State;
using DeNelle.Village;

namespace DeNelle.Editor.Regression
{
    public static class ArmedHeroInvariantRegression
    {
        private const string LoadoutSrc = "Assets/_Modules/Village/Hero/GearLoadout.cs";
        private const string ProgressionSrc = "Assets/_Modules/Village/Hero/GearProgression.cs";

        /// <summary>The display sites that must clamp to the SAME symbol as the engine.</summary>
        private static readonly string[] DisplaySrcs =
        {
            "Assets/_Modules/Village/Hero/EquipVM.cs",
            // ShopVM.cs REMOVED 2026-09-06 (WO-1430): the file was DELETED with the doorless
            // ShopPanel it served. ReadSource FAILS on a missing path by design ("the file moved
            // without updating this oracle"), so a stale entry here would have turned a deliberate
            // retirement into a red suite. PartyShopVM is the surviving display site for gear stats.
            "Assets/_Modules/Village/Hero/PartyShopVM.cs",
        };

        /// <summary>The owner-locked value. Pinned as a literal HERE on purpose: this suite is
        /// the thing that would notice the constant being quietly retuned.</summary>
        private const float OwnerLockedCap = 0.90f;

        /// <summary>The seeded starter off-hand - a real, level-1, common shield.</summary>
        private const string StarterShieldId = "knight_shield_starter";

        private const float Eps = 1e-4f;

        /// <summary>Standalone batch entry - prints the marker.</summary>
        public static void RunAll()
        {
            if (Run(out string reason)) Debug.Log("ARMED_HERO_OK - " + reason);
            else Debug.LogError("ARMED_HERO_FAIL: " + reason);
        }

        /// <summary>Covenant contract (DataRegression-shaped). Never throws.</summary>
        public static bool Run(out string reason)
        {
            var failures = new List<string>();
            var notes = new List<string>();
            try
            {
                Case(failures, "armed-hero", () => Case1_ArmedHeroGeneral(failures, notes));
                Case(failures, "armed-hero-pins", () => Case2_NamedClassPins(failures, notes));
                Case(failures, "armed-hero-levels", () => Case3_ArmedAtEveryLevel(failures));
                Case(failures, "shield-improvement", () => Case4_ShieldImprovement(failures, notes));
                Case(failures, "shield-improvement-seam", () => Case5_ApplyStatsRoutesOffHand(failures));
                Case(failures, "defense-cap", () => Case6_DefenseCap(failures, notes));
                Case(failures, "defense-cap-single-source", () => Case7_CapHasOneDefinition(failures));
            }
            catch (Exception ex)
            {
                failures.Add("[suite] THREW " + ex.GetType().Name + ": " + ex.Message);
            }

            string noteStr = notes.Count > 0 ? " [notes: " + string.Join("; ", notes) + "]" : "";
            if (failures.Count == 0)
            {
                reason = "ARMED HERO OK - every class resolves a non-null, non-off-hand, class-fitting " +
                         "main hand through the real GearLoadout.Refresh (Mage holds a staff), the " +
                         "invariant holds at every level band, a shield's level scaling reaches " +
                         "ArmorDefense through GearStatResolver and costs real resources, and the " +
                         "display cap and the applied cap are the SAME symbol at " + Fmt(OwnerLockedCap) + noteStr;
                return true;
            }
            reason = "armed-hero FAIL x" + failures.Count + ": " + string.Join(" | ", failures) + noteStr;
            return false;
        }

        private static void Case(List<string> failures, string name, Action body)
        {
            try { body(); }
            catch (Exception ex) { failures.Add("[" + name + "] THREW " + ex.GetType().Name + ": " + ex.Message); }
        }

        // =====================================================================
        //  THE HEADLESS PROBE - drives the REAL GearLoadout.Refresh, no scene.
        // =====================================================================

        /// <summary>What one class's fresh loadout resolved to. Never null-valued silently:
        /// <see cref="Failure"/> is non-null when the probe itself could not run.</summary>
        private sealed class Probe
        {
            public string Job;
            public WeaponDef MainHand;
            public WeaponDef OffHand;
            public ArmorDef Armor;
            public float ArmorDefense;
            public string Failure;
        }

        /// <summary>
        /// Builds a throwaway hero, binds it to <paramref name="job"/> and returns what the
        /// SHIPPED resolution chain put in its hands.
        ///
        /// The PlayerPrefs for this class are snapshotted and CLEARED first, then restored -
        /// which is exactly the gear-relevant half of ResetToNewGame (its ClearEquipPrefs step),
        /// so this reproduces "New Game, then pick this class" without a play session. Without
        /// the clear, a developer's own persisted equip would decide the result and the suite
        /// would report whatever was last equipped in the editor.
        ///
        /// The GameObject carries HideAndDontSave so a batch run can never dirty or save it
        /// into an open scene, and is destroyed in a finally.
        /// </summary>
        private static Probe RunProbe(string job, string offHandToEquip = null)
        {
            var p = new Probe { Job = job };

            var keys = new List<string>();
            string classKey = (job ?? string.Empty).ToLowerInvariant();
            foreach (var prefix in EquipPrefKeys.AllSlotPrefixes) keys.Add(prefix + classKey);

            var snapshot = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var k in keys)
                if (PlayerPrefs.HasKey(k)) snapshot[k] = PlayerPrefs.GetString(k, string.Empty);

            GameObject go = null;
            try
            {
                foreach (var k in keys) PlayerPrefs.DeleteKey(k);

                go = new GameObject("ArmedHeroProbe_" + classKey);
                go.hideFlags = HideFlags.HideAndDontSave;

                var loadout = go.AddComponent<GearLoadout>();
                if (loadout == null)
                {
                    p.Failure = "AddComponent<GearLoadout>() returned null";
                    return p;
                }

                // The real entry point. BindOwnerClass sets the authoritative class and calls
                // Refresh() - the shipped starter/auto-best/persisted/EnforceHandSlots chain.
                loadout.BindOwnerClass(job);

                if (!string.IsNullOrEmpty(offHandToEquip)) loadout.EquipOffHandById(offHandToEquip);

                p.MainHand = loadout.EquippedWeapon;
                p.OffHand = loadout.EquippedOffHand;
                p.Armor = loadout.EquippedArmor;
                p.ArmorDefense = loadout.ArmorDefense;
            }
            catch (Exception ex)
            {
                p.Failure = "the probe THREW " + ex.GetType().Name + ": " + ex.Message;
            }
            finally
            {
                if (go != null) UnityEngine.Object.DestroyImmediate(go);
                foreach (var k in keys) PlayerPrefs.DeleteKey(k);
                foreach (var kv in snapshot) PlayerPrefs.SetString(kv.Key, kv.Value);
                PlayerPrefs.Save();
            }
            return p;
        }

        // =====================================================================
        //  CASE 1 - THE GENERAL, DATA-DERIVED ARMED-HERO RULE (every class)
        // =====================================================================
        private static void Case1_ArmedHeroGeneral(List<string> failures, List<string> notes)
        {
            var classes = PlayableHeroes.AllKnownJobKeys();
            if (classes == null || classes.Count == 0)
            {
                failures.Add("[armed-hero] PlayableHeroes.AllKnownJobKeys() is EMPTY - this suite would sweep " +
                             "zero classes and pass vacuously, which is the failure mode it exists to prevent");
                return;
            }

            var uncategorised = new List<string>();

            foreach (var job in classes)
            {
                var p = RunProbe(job);
                if (p.Failure != null)
                {
                    failures.Add("[armed-hero] '" + job + "': " + p.Failure + " - the armed-hero invariant could " +
                                 "not be evaluated at all for this class");
                    continue;
                }

                if (p.MainHand == null)
                {
                    failures.Add("[armed-hero] '" + job + "' resolves a NULL main hand after a clean class select - " +
                                 "the hero spawns UNARMED (off-hand='" + (p.OffHand != null ? p.OffHand.id : "<null>") +
                                 "'). This is the 2026-08-02 level-1 Mage verbatim");
                    continue;
                }

                // THE assertion the old non-null checks were missing.
                if (p.MainHand.IsOffHandItem)
                {
                    failures.Add("[armed-hero] '" + job + "' resolves main hand '" + p.MainHand.id + "' which is an " +
                                 "OFF-HAND item (category='" + (p.MainHand.category ?? "<null>") + "') - a shield in " +
                                 "the main hand is not a weapon; EnforceHandSlots evicts it and the hero swings nothing");
                    continue;
                }

                if (!GearCatalog.WeaponFitsClass(p.MainHand, job))
                {
                    failures.Add("[armed-hero] '" + job + "' resolves main hand '" + p.MainHand.id + "' whose job is '" +
                                 (p.MainHand.job ?? "<null>") + "' - it does not fit the class, so the equip gate " +
                                 "rejects it everywhere else and the hands disagree with the body");
                    continue;
                }

                // ---- the DATA-DERIVED kind check ----
                var owned = OwnedMainHandCategories(job);
                string cat = Norm(p.MainHand.category);
                string itemJob = Norm(p.MainHand.job);

                if (cat.Length > 0)
                {
                    if (owned.Count == 0)
                    {
                        notes.Add("'" + job + "' has no job-exact categorised main-hand rows to derive an expected " +
                                  "category from; fell back to the job-exact rule");
                        if (itemJob != Norm(job))
                            failures.Add("[armed-hero] '" + job + "' resolves '" + p.MainHand.id + "' (job='" + itemJob +
                                         "', category='" + cat + "') - the class owns no categorised weapon rows, so a " +
                                         "main hand that is not job-EXACT for it cannot be verified as the right kind");
                    }
                    else if (!owned.Contains(cat))
                    {
                        failures.Add("[armed-hero] '" + job + "' resolves main hand '" + p.MainHand.id + "' of category '" +
                                     cat + "', but the only main-hand categories this class actually wields (derived from " +
                                     "its job-exact weapons.json rows) are [" + string.Join(",", ToArray(owned)) + "] - the " +
                                     "hero is holding the wrong KIND of weapon, which no non-null check can see");
                    }
                }
                else
                {
                    // No authored category (the ten legacy rows). The row must then be the class's OWN,
                    // never a job:"any" row - which is exactly what the offending shield was.
                    uncategorised.Add(job + ":" + p.MainHand.id);
                    if (itemJob != Norm(job))
                        failures.Add("[armed-hero] '" + job + "' resolves main hand '" + p.MainHand.id + "' that carries " +
                                     "NEITHER a `category` NOR a job-exact owner (job='" + (p.MainHand.job ?? "<null>") +
                                     "') - nothing in the data says this class wields it; a job:'any' row must never win " +
                                     "a class's main hand");
                }
            }

            if (uncategorised.Count > 0)
                notes.Add("DATA GAP - these resolved main hands carry NO `category` field, so only the weaker " +
                          "job-exact rule could be applied to them: " + string.Join(", ", uncategorised.ToArray()) +
                          ". Author `category` on those weapons.json rows and this suite hard-asserts the kind");
        }

        /// <summary>
        /// The main-hand weapon categories a class ACTUALLY wields, derived from the shipped
        /// catalog: the distinct non-empty `category` values of every job-EXACT, non-off-hand
        /// row for that class. Deliberately NOT a hardcoded table - a retune of weapons.json
        /// moves this expectation with the data instead of turning the oracle red.
        /// job:"any" rows are excluded on purpose: "any" says nothing about what a class wields,
        /// and including them would re-admit the shield that caused the bug.
        /// </summary>
        private static HashSet<string> OwnedMainHandCategories(string job)
        {
            var set = new HashSet<string>(StringComparer.Ordinal);
            var all = GearCatalog.AllWeapons();
            if (all == null) return set;
            foreach (var w in all)
            {
                if (w == null || w.IsOffHandItem) continue;
                if (Norm(w.job) != Norm(job)) continue;      // job-EXACT only
                string c = Norm(w.category);
                if (c.Length > 0) set.Add(c);
            }
            return set;
        }

        // =====================================================================
        //  CASE 2 - the three classes we actually ship, pinned BY NAME
        // =====================================================================
        //  The general rule above follows the data. These pins do not: they exist so a
        //  weapons.json retune can never quietly satisfy the general rule while breaking
        //  Grom, Sylas or Thrain. Each pin asserts the STRONGEST statement the current data
        //  can actually support - the Mage's staff is asserted as a hard category equality
        //  because those rows are categorised; the Knight's and the Ranger's main hands are
        //  asserted job-exact + main-hand because their rows carry no category to assert.
        //  Where a pin is weaker than the owner's wording, that is stated in the message
        //  rather than papered over with a pass.
        // =====================================================================
        private static void Case2_NamedClassPins(List<string> failures, List<string> notes)
        {
            PinClass(failures, notes, "mage", "staff");
            PinClass(failures, notes, "ranger", null);
            PinClass(failures, notes, "knight", null);
        }

        private static void PinClass(List<string> failures, List<string> notes, string job, string expectedCategory)
        {
            var p = RunProbe(job);
            if (p.Failure != null)
            {
                failures.Add("[armed-hero-pins] '" + job + "': " + p.Failure);
                return;
            }
            if (p.MainHand == null)
            {
                failures.Add("[armed-hero-pins] '" + job + "' has a NULL main hand on a fresh class select - one of the " +
                             "three shipped heroes spawns with empty hands");
                return;
            }
            if (p.MainHand.IsOffHandItem)
            {
                failures.Add("[armed-hero-pins] '" + job + "' main hand '" + p.MainHand.id + "' is a SHIELD");
                return;
            }

            string cat = Norm(p.MainHand.category);

            if (!string.IsNullOrEmpty(expectedCategory))
            {
                if (cat != expectedCategory)
                    failures.Add("[armed-hero-pins] '" + job + "' must open on a " + expectedCategory + " (owner's " +
                                 "criterion: 'is a staff (job:mage, category staff)') but resolves '" + p.MainHand.id +
                                 "' of category '" + (cat.Length > 0 ? cat : "<none>") + "'");
                if (Norm(p.MainHand.job) != Norm(job))
                    failures.Add("[armed-hero-pins] '" + job + "' main hand '" + p.MainHand.id + "' is job='" +
                                 (p.MainHand.job ?? "<null>") + "', not job-exact '" + job + "'");
                return;
            }

            // No category is authorable for this class today - assert job ownership instead,
            // and SAY that the kind check is weaker than the owner asked for.
            if (Norm(p.MainHand.job) != Norm(job))
                failures.Add("[armed-hero-pins] '" + job + "' main hand '" + p.MainHand.id + "' is job='" +
                             (p.MainHand.job ?? "<null>") + "' rather than job-exact '" + job + "' - it is not this " +
                             "class's own weapon");
            if (cat.Length == 0)
                notes.Add("'" + job + "' pin is CATEGORY-BLIND: its main hand '" + p.MainHand.id + "' authors no " +
                          "`category`, so the pin can only assert job ownership, not weapon kind");
        }

        // =====================================================================
        //  CASE 3 - armed at EVERY level, not just on the first frame
        // =====================================================================
        //  Refresh re-runs on every level-up, so "armed" is a per-level property. This uses
        //  the pure auto-best query (the probe cannot vary level without a HeroProgression),
        //  which is the exact input EnforceHandSlots receives above level 1.
        // =====================================================================
        private static void Case3_ArmedAtEveryLevel(List<string> failures)
        {
            int[] levels = { 1, 2, 3, 6, 10, 20 };
            foreach (var job in PlayableHeroes.AllKnownJobKeys())
            {
                foreach (int lv in levels)
                {
                    var best = GearCatalog.BestWeapon(job, lv);
                    if (best == null)
                    {
                        failures.Add("[armed-hero-levels] GearCatalog.BestWeapon('" + job + "', " + lv + ") is NULL - " +
                                     "at this level the class has no main-hand weapon to auto-equip at all, so any " +
                                     "hero who levels into this band and has not persisted a pick goes unarmed");
                        continue;
                    }
                    if (best.IsOffHandItem)
                        failures.Add("[armed-hero-levels] GearCatalog.BestWeapon('" + job + "', " + lv + ") returned '" +
                                     best.id + "', an OFF-HAND item - this is the exact 2026-08-02 defect (a job:'any' " +
                                     "shield winning the main-hand pick on a damageMult tie)");
                    if (!GearCatalog.WeaponFitsClass(best, job))
                        failures.Add("[armed-hero-levels] BestWeapon('" + job + "', " + lv + ") returned '" + best.id +
                                     "' which does not fit the class");
                }
            }
        }

        // =====================================================================
        //  CASE 4 - AC-1: improving a shield reaches ArmorDefense AND costs resources
        // =====================================================================
        private static void Case4_ShieldImprovement(List<string> failures, List<string> notes)
        {
            var shield = GearCatalog.FindWeapon(StarterShieldId);
            if (shield == null)
            {
                failures.Add("[shield-improvement] GearCatalog.FindWeapon('" + StarterShieldId + "') is null - the " +
                             "seeded starter shield does not resolve, so nothing about shield improvement can be judged");
                return;
            }
            if (!shield.IsOffHandItem)
            {
                failures.Add("[shield-improvement] '" + StarterShieldId + "' no longer reads as an off-hand item " +
                             "(category='" + (shield.category ?? "<null>") + "')");
                return;
            }
            if (shield.defense <= 0f)
            {
                failures.Add("[shield-improvement] '" + StarterShieldId + "' has defense " + Fmt(shield.defense) +
                             " - an inert shield cannot demonstrate that levelling reaches the damage chain");
                return;
            }

            // ---- (a) the level scaling itself, through the SHIPPED resolver ----
            float atL1 = GearStatResolver.EffectiveDefense(shield, 1);
            if (Math.Abs(atL1 - shield.defense) > Eps)
                failures.Add("[shield-improvement] EffectiveDefense('" + shield.id + "', 1) = " + Fmt(atL1) +
                             " but the authored value is " + Fmt(shield.defense) + " - level 1 must be the authored " +
                             "baseline exactly, or every existing save silently re-balances");

            int maxLevel = GearProgression.MaxLevelFor(shield.rarity);
            if (maxLevel <= 1)
            {
                failures.Add("[shield-improvement] gear-levels.json authors no ladder for rarity '" +
                             (shield.rarity ?? "<null>") + "' (max level " + maxLevel + ") - GearProgression.Improve " +
                             "would refuse, yet the shop still offers the button");
                return;
            }

            float atMax = GearStatResolver.EffectiveDefense(shield, maxLevel);
            if (!(atMax > atL1 + Eps))
                failures.Add("[shield-improvement] EffectiveDefense('" + shield.id + "') is " + Fmt(atL1) + " at L1 and " +
                             Fmt(atMax) + " at L" + maxLevel + " - levelling a shield buys NOTHING; this is the AC-1 " +
                             "defect measured at the resolver");

            // ---- (a) the level really is written, and it really costs ----
            var state = ScriptableObject.CreateInstance<GameState>();
            try
            {
                int before = GearProgression.GearLevelOf(state, shield.id);
                if (before != 1)
                    failures.Add("[shield-improvement] a fresh GameState reports gear level " + before + " for '" +
                                 shield.id + "' - the default-on-read baseline is not 1");

                int after = GearProgression.ApplyImprove(state, shield.id, shield.rarity);
                if (after != before + 1)
                    failures.Add("[shield-improvement] ApplyImprove('" + shield.id + "') returned level " + after +
                                 " (expected " + (before + 1) + ") - the level write does not accept shields, so the " +
                                 "player is charged for a level that is never stored");
                if (GearProgression.GearLevelOf(state, shield.id) != after)
                    failures.Add("[shield-improvement] the level written by ApplyImprove does not read back");

                float scaledAfter = GearStatResolver.EffectiveDefense(shield, after);
                if (!(scaledAfter > atL1 + Eps))
                    failures.Add("[shield-improvement] one Improve step moves '" + shield.id + "' from " + Fmt(atL1) +
                                 " to " + Fmt(scaledAfter) + " defense - the very first purchase is already a no-op");

                var cost = GearProgression.ImproveCost(shield.rarity, after);
                if (cost.Wood <= 0 && cost.Iron <= 0 && cost.Stone <= 0 && cost.Crystals <= 0)
                    failures.Add("[shield-improvement] ImproveCost('" + shield.rarity + "', L" + after + ") charges " +
                                 "NOTHING - either the ladder is free (and the economy is broken) or the cost curve " +
                                 "does not cover this band while Improve still reports success");
                else
                    notes.Add("shield '" + shield.id + "' L1=" + Fmt(atL1) + " -> L" + after + "=" + Fmt(scaledAfter) +
                              " -> L" + maxLevel + "=" + Fmt(atMax) + " (first step costs wood " + cost.Wood +
                              " / iron " + cost.Iron + ")");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(state);
            }

            // ---- (b) the equipped off-hand really is SUMMED into the published scalar ----
            var bare = RunProbe("knight");
            var withShield = RunProbe("knight", StarterShieldId);
            if (bare.Failure != null || withShield.Failure != null)
            {
                notes.Add("the live off-hand-summing probe could not run (" +
                          (bare.Failure ?? withShield.Failure) + "); the source invariant case carries it");
            }
            else if (withShield.OffHand == null)
            {
                failures.Add("[shield-improvement] EquipOffHandById('" + StarterShieldId + "') left the off-hand EMPTY " +
                             "on a knight loadout - the shield cannot contribute defense it was never allowed to hold");
            }
            else if (!(withShield.ArmorDefense > bare.ArmorDefense + Eps))
            {
                failures.Add("[shield-improvement] equipping '" + StarterShieldId + "' left GearLoadout.ArmorDefense at " +
                             Fmt(withShield.ArmorDefense) + " (bare: " + Fmt(bare.ArmorDefense) + ") - the off-hand term " +
                             "is not reaching the ONE scalar HeroHealth.TakeDamage consumes, so no shield mitigates " +
                             "anything no matter what it is levelled to");
            }
            else
            {
                notes.Add("live loadout: knight ArmorDefense " + Fmt(bare.ArmorDefense) + " -> " +
                          Fmt(withShield.ArmorDefense) + " with the starter shield equipped");
            }
        }

        // =====================================================================
        //  CASE 5 - the ONE seam that needs a play session, pinned at the source
        // =====================================================================
        //  GearLoadout.ApplyStats reads the gear level off GameStateService.Instance, which is
        //  null in batchmode. That link is therefore linted, not executed - and the lint is
        //  written to fail on the SPECIFIC regression (a raw `.defense` read returning), not on
        //  cosmetic edits.
        // =====================================================================
        private static void Case5_ApplyStatsRoutesOffHand(List<string> failures)
        {
            string src = ReadSource(LoadoutSrc, failures);
            if (src == null) return;
            string code = StripComments(src);

            var m = Regex.Match(code, @"offHandDefense\s*=(?<rhs>[^;]*);");
            if (!m.Success)
            {
                failures.Add("[shield-improvement-seam] GearLoadout.ApplyStats no longer assigns an `offHandDefense` " +
                             "term - this lint can no longer see how the off-hand reaches ArmorDefense; re-point it at " +
                             "the new shape deliberately rather than deleting it");
                return;
            }

            string rhs = m.Groups["rhs"].Value;

            if (rhs.IndexOf("EffectiveDefense", StringComparison.Ordinal) < 0)
                failures.Add("[shield-improvement-seam] the off-hand defense term does not go through " +
                             "GearStatResolver.EffectiveDefense (it reads: " + Condense(rhs) + ") - this is AC-1 " +
                             "verbatim: GearProgression.Improve charges wood + iron and writes " +
                             "GameState.GearLevels[shieldId], and the level never reaches the damage chain");

            if (rhs.IndexOf("GearLevelOf", StringComparison.Ordinal) < 0)
                failures.Add("[shield-improvement-seam] the off-hand defense term does not read the owned instance's " +
                             "LEVEL via GearProgression.GearLevelOf (it reads: " + Condense(rhs) + ") - it would " +
                             "resolve every shield at the authored baseline forever");

            if (Regex.IsMatch(rhs, @"EquippedOffHand\s*\.\s*defense"))
                failures.Add("[shield-improvement-seam] the off-hand defense term reads EquippedOffHand.defense RAW " +
                             "(it reads: " + Condense(rhs) + ") - the original defect has returned");

            // The weapon and the armor must still go through the SAME resolver: the whole point
            // of AC-1 is that there is ONE level-scaling path, not a second one for off-hands.
            if (!Regex.IsMatch(code, @"EffectiveDamageMult\s*\(\s*EquippedWeapon"))
                failures.Add("[shield-improvement-seam] the MAIN-HAND weapon no longer resolves through " +
                             "GearStatResolver.EffectiveDamageMult - the off-hand would now be scaled by a path the " +
                             "weapon is not, which is how these drift apart");
            if (!Regex.IsMatch(code, @"EffectiveDefense\s*\(\s*EquippedArmor"))
                failures.Add("[shield-improvement-seam] the ARMOR no longer resolves through " +
                             "GearStatResolver.EffectiveDefense");

            // And the resolver must actually expose an off-hand (WeaponDef) overload rather than
            // someone having added a private second implementation inside GearLoadout.
            string prog = ReadSource(ProgressionSrc, failures);
            if (prog != null &&
                !Regex.IsMatch(StripComments(prog), @"EffectiveDefense\s*\(\s*WeaponDef\s+\w+\s*,\s*int\s+\w+\s*\)"))
                failures.Add("[shield-improvement-seam] GearStatResolver has no EffectiveDefense(WeaponDef, int) " +
                             "overload - if ApplyStats is scaling the off-hand, it is doing it through a SECOND " +
                             "implementation, which is exactly how the applied cap and the display cap drifted");
        }

        // =====================================================================
        //  CASE 6 - AC-2: the cap is 0.90, and it is the SAME 0.90 everywhere
        // =====================================================================
        private static void Case6_DefenseCap(List<string> failures, List<string> notes)
        {
            // The owner-locked value itself.
            if (Math.Abs(GearLoadout.MaxArmorDefense - OwnerLockedCap) > Eps)
                failures.Add("[defense-cap] GearLoadout.MaxArmorDefense is " + Fmt(GearLoadout.MaxArmorDefense) +
                             ", not the owner-locked " + Fmt(OwnerLockedCap) + " (ruling 2026-08-02). Display and " +
                             "engine may agree with each other and still both be wrong");

            if (GearLoadout.MaxArmorDefense >= 1f)
                failures.Add("[defense-cap] the cap is " + Fmt(GearLoadout.MaxArmorDefense) + " - at or above 1.0 a " +
                             "sufficiently geared hero takes ZERO damage and no encounter can be lost");

            // The ENGINE really clamps there. An over-stat piece is built in memory rather than
            // looked up, so this cannot be defeated by a catalog retune - it measures the CLAMP.
            var absurd = new ArmorDef { id = "regression_absurd_plate", rarity = "common", defense = 5f };
            float clampedArmor = GearStatResolver.EffectiveDefense(absurd, 1);
            if (Math.Abs(clampedArmor - GearLoadout.MaxArmorDefense) > Eps)
                failures.Add("[defense-cap] GearStatResolver.EffectiveDefense clamps a 5.0-defense piece to " +
                             Fmt(clampedArmor) + ", not MaxArmorDefense (" + Fmt(GearLoadout.MaxArmorDefense) +
                             ") - one fat-fingered decimal point in armor.json would grant near-immunity");

            var absurdShield = new WeaponDef { id = "regression_absurd_shield", rarity = "common", category = "shield", defense = 5f };
            float clampedShield = GearStatResolver.EffectiveDefense(absurdShield, 1);
            if (Math.Abs(clampedShield - GearLoadout.MaxArmorDefense) > Eps)
                failures.Add("[defense-cap] the OFF-HAND resolver clamps a 5.0-defense shield to " + Fmt(clampedShield) +
                             ", not MaxArmorDefense (" + Fmt(GearLoadout.MaxArmorDefense) + ") - the new off-hand path " +
                             "does not share the armor path's ceiling");

            // How close can REAL gear get? Reported so the owner sees whether the cap binds at all.
            float bis = BestInSlotDefense();
            notes.Add("best-in-slot defense reachable from the shipped catalog = " + Fmt(bis) + " vs cap " +
                      Fmt(GearLoadout.MaxArmorDefense) +
                      (bis >= GearLoadout.MaxArmorDefense - Eps
                          ? " (the cap BINDS - top-end defense gear is partly wasted)"
                          : " (the cap does NOT bind on any authorable stack today)"));

            // A live loadout's published scalar must never exceed the cap.
            foreach (var job in PlayableHeroes.AllKnownJobKeys())
            {
                var p = RunProbe(job);
                if (p.Failure != null) continue;   // Case 1 already reported it as a failure
                if (p.ArmorDefense > GearLoadout.MaxArmorDefense + Eps)
                    failures.Add("[defense-cap] '" + job + "' publishes ArmorDefense " + Fmt(p.ArmorDefense) +
                                 ", ABOVE the cap " + Fmt(GearLoadout.MaxArmorDefense) + " - the applied clamp is not " +
                                 "being enforced on the summed total");
                if (p.ArmorDefense < 0f)
                    failures.Add("[defense-cap] '" + job + "' publishes a NEGATIVE ArmorDefense (" +
                                 Fmt(p.ArmorDefense) + ") - mitigation below zero AMPLIFIES incoming damage");
            }
        }

        /// <summary>The strongest defense the shipped catalog can actually stack, computed the way
        /// ApplyStats does: armor + ring + amulet + off-hand, each at its own max gear level.</summary>
        private static float BestInSlotDefense()
        {
            float bestArmor = 0f, bestShield = 0f;
            foreach (var a in GearCatalog.AllArmors())
            {
                if (a == null) continue;
                float v = GearStatResolver.EffectiveDefense(a, GearProgression.MaxLevelFor(a.rarity));
                if (v > bestArmor) bestArmor = v;
            }
            foreach (var w in GearCatalog.AllWeapons())
            {
                if (w == null || !w.IsOffHandItem) continue;
                float v = GearStatResolver.EffectiveDefense(w, GearProgression.MaxLevelFor(w.rarity));
                if (v > bestShield) bestShield = v;
            }

            float bestRing = 0f, bestAmulet = 0f;
            foreach (var ac in GearCatalog.AllAccessories())
            {
                if (ac == null) continue;
                if (ac.IsRing && ac.defense > bestRing) bestRing = ac.defense;
                else if (ac.IsAmulet && ac.defense > bestAmulet) bestAmulet = ac.defense;
            }

            return bestArmor + bestRing + bestAmulet + bestShield;
        }

        // =====================================================================
        //  CASE 7 - ONE definition of the cap (no re-inlined literals)
        // =====================================================================
        //  AC-2's real requirement is not "make it 0.90" - it is "stop having fourteen copies".
        //  This is a source lint because that is the only place the defect can be seen: fourteen
        //  literals that agree today compile and behave identically to one constant, right up
        //  until someone edits one of them.
        // =====================================================================
        private static void Case7_CapHasOneDefinition(List<string> failures)
        {
            var all = new List<string>(DisplaySrcs) { LoadoutSrc, ProgressionSrc };

            foreach (var path in all)
            {
                string src = ReadSource(path, failures);
                if (src == null) continue;
                string code = StripComments(src);

                // A 0..fraction clamp whose upper bound is a bare literal is a re-inlined copy of
                // the cap. Matched on the TAIL (", 0f, <fraction>f)") rather than the whole call,
                // so a clamp whose first argument contains parentheses - which is most of them -
                // cannot slip past. A `1f` / integer upper bound is an ordinary 0..1 bar fill and
                // is excluded by the pattern itself (it requires a decimal fraction below one).
                foreach (Match m in Regex.Matches(code, @"Clamp[^;]{0,200}?,\s*0f\s*,\s*(?<hi>0?\.\d+f)\s*\)"))
                {
                    failures.Add("[defense-cap-single-source] " + Path.GetFileName(path) + " clamps to the LITERAL " +
                                 m.Groups["hi"].Value + " (" + Condense(m.Value) + ") instead of " +
                                 "GearLoadout.MaxArmorDefense - this is how the applied 0.70 and the displayed 0.90 " +
                                 "drifted apart, and no compiler can catch the next divergence");
                }

                if (path != LoadoutSrc && code.IndexOf("MaxArmorDefense", StringComparison.Ordinal) < 0)
                    failures.Add("[defense-cap-single-source] " + Path.GetFileName(path) + " never references " +
                                 "GearLoadout.MaxArmorDefense - it is bounding the defense number by some other rule " +
                                 "than the one the engine applies");
            }

            // The applied clamp specifically.
            string loadout = ReadSource(LoadoutSrc, failures);
            if (loadout == null) return;
            var applied = Regex.Match(StripComments(loadout), @"ArmorDefense\s*=\s*Mathf\.Clamp\((?<args>[^;]*)\);");
            if (!applied.Success)
            {
                failures.Add("[defense-cap-single-source] GearLoadout.ApplyStats no longer assigns ArmorDefense via " +
                             "Mathf.Clamp - the applied ceiling can no longer be verified at all");
                return;
            }
            if (applied.Groups["args"].Value.IndexOf("MaxArmorDefense", StringComparison.Ordinal) < 0)
                failures.Add("[defense-cap-single-source] the APPLIED ArmorDefense clamp does not use " +
                             "MaxArmorDefense (it reads: Mathf.Clamp(" + Condense(applied.Groups["args"].Value) +
                             ")) - the engine is back to its own private ceiling while the shop advertises another");
        }

        // =====================================================================
        //  HELPERS
        // =====================================================================

        private static string Norm(string s) => (s ?? string.Empty).Trim().ToLowerInvariant();

        private static string[] ToArray(HashSet<string> set)
        {
            var list = new List<string>(set);
            list.Sort(StringComparer.Ordinal);
            return list.ToArray();
        }

        private static string ReadSource(string path, List<string> failures)
        {
            if (!File.Exists(path))
            {
                failures.Add("[source] " + path + " not found - the file moved without updating this oracle");
                return null;
            }
            try { return File.ReadAllText(path); }
            catch (Exception ex)
            {
                failures.Add("[source] could not read " + path + ": " + ex.GetType().Name + ": " + ex.Message);
                return null;
            }
        }

        /// <summary>Strips // and comments so a lint can never be satisfied by prose.</summary>
        private static string StripComments(string src)
        {
            if (string.IsNullOrEmpty(src)) return string.Empty;
            string noBlock = Regex.Replace(src, @"/\*.*?\*/", " ", RegexOptions.Singleline);
            return Regex.Replace(noBlock, @"//[^\r\n]*", " ");
        }

        private static string Condense(string s)
        {
            string one = Regex.Replace(s ?? string.Empty, @"\s+", " ").Trim();
            return one.Length > 160 ? one.Substring(0, 157) + "..." : one;
        }

        private static string Fmt(float f) =>
            f.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
    }
}

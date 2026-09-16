// =============================================================================
// HubRingHeightRegression [hub-ring-height]
// -----------------------------------------------------------------------------
// Assembly: DeNelle.EditorRegression.  Namespace: DeNelle.Editor.Regression.
// Markers: HUB_RING_HEIGHT_OK / HUB_RING_HEIGHT_FAIL.
// Registered ONCE in DataRegression.RunAll. NEVER throws.
//
// ── WHAT IT PINS (WO-1762, RE-POINTED TO THE RULING 2026-09-15) ──────────────
//   RULE 1  The FOUR roots the owner actually ruled on measure their RULED height,
//           each +/-5%. Not "every root is near 4 m" -- see the correction below.
//   RULE 2  No "*_Pallet" transform survives anywhere under the ring.
//   RULE 3  The ring carries exactly ELEVEN children (fourteen minus the three
//           scenery pallets she removed).
//
// ⚠ THE CORRECTION THAT MADE THIS FILE WORTH RE-READING. The first version asserted
// a blanket 4.00 m +/-15% over EVERY authored root, and on its first real run
// (Builds regression 2026-09-15 21:44, REGRESSION_FAIL 539/541) it went red on SIX
// roots, and every single red was the SUITE being wrong, not the ring:
//
//   * Jeweler 4.89, RealmStore 4.90, Armorer 5.03, Crafting 2.33 -- these were NEVER
//     IN THE OWNER'S ASK. WO-1762 names four buildings: the lumber mill, the iron
//     mine, the quarry and the Cathedral. A suite that fails a building nobody asked
//     anybody to change is not protecting anything; it is a lane blocker that will be
//     silenced, and a silenced suite protects nothing at all.
//   * Stone_Quarry 5.00 and ArcaneTower 5.00 -- both are 5.00 m BY RULING. The owner
//     asked for the Cathedral "a little bit larger" (5.0 m), and the Quarry is
//     deliberately 4.0 m VISIBLE plus a 1.0 m pit below the courtyard = 5.00 m of
//     total bounds. The suite was measuring total bounds and calling the ruling a
//     defect.
//
// So the rule now pins the RULING, per root, by name -- not a family average, and not
// the scene (pinning "whatever the scene currently is" asserts nothing; it can never
// go red because it is copied FROM the thing it claims to check).
//
// ── THE QUARRY IS PINNED TWICE, AND THAT IS THE POINT ────────────────────────
// Its model has a pit reaching 1.0 m below the courtyard after fitting, so TOTAL
// bounds and VISIBLE height are different numbers. A total-only pin is exactly the
// silent hole WO-1762 section 8.3 documents: fit 6.35 m of bounds to 4.00 m and the
// player sees a 3.20 m building while every instrument reads a satisfied 4.00 m.
// YES, THIS SUITE CAN SPLIT AT THE GROUND -- the measurement is taken in the recipe
// ROOT's space, where a direct child's own localPosition.y IS its ground plane (the
// same value HubRingHeightApply uses as groundY). So the Quarry gets BOTH pins:
// 5.00 m total AND 4.00 m above its own ground. The other three have no sub-ground
// geometry, so for them the two are equal and only the total is pinned.
//
// ── WHY IT MEASURES THE RECIPE PREFAB, NOT THE SCENE ─────────────────────────
// Assets/Prefabs/Village/OwnerCastleStorefrontLayout.prefab is the recipe the ring is
// built from and compared against: CastleHubBuilder loads it (CastleHubBuilder.cs:70,
// :141) and OwnerCastleRuntimeProof.Compare asserts the live ring and the recipe
// agree transform-for-transform. Measuring the recipe therefore measures the ring,
// WITHOUT opening a scene inside the gate -- a suite that opened
// Main_Castle_Overworld would disturb every other suite in the same run and would be
// refused on a dirty scene.
//
// ⚠ THAT IS A NARROWING, AND IT IS NAMED HERE RATHER THAN HIDDEN: scene==recipe is
// pinned by OwnerCastleRuntimeProof.Run, which is NOT part of DataRegression. If a
// seat edits the scene without re-saving the recipe, this suite stays green on a
// stale recipe. The close is to run OwnerCastleRuntimeProof after any ring edit.
//
// ── MEASUREMENT ──────────────────────────────────────────────────────────────
// Renderer.bounds is unavailable on a prefab asset, so the extent is rebuilt from
// MeshFilter.sharedMesh.bounds and SkinnedMeshRenderer.sharedMesh.bounds corners
// transformed into the recipe root's space. HubRingHeightAudit prints BOTH that
// number and the live Renderer.bounds for the same objects, side by side, so the
// technique is calibrated against a real reading.
//
// ── THE STAND-DOWN ───────────────────────────────────────────────────────────
// ApplyLanded gates the whole suite. It was false while the ring was deliberately
// still at its pre-WO sizes; the lead flipped it in the same commit as the apply. It
// is an explicit constant and NOT a sniff on the scene's state on purpose: "pallets
// present means the apply has not landed, so skip" would make RULE 2 unable to ever
// go red (present => skip, absent => trivially true) -- a hollow pass in both
// branches while reading green.
//
// HOLLOW-PASS GUARD: if the recipe prefab cannot be loaded, the suite stands down via
// Skip rather than passing on nothing. A RULED ROOT THAT IS MISSING OR RENDERS
// NOTHING IS A HARD FAIL, never a partial-skip -- it is one of the four objects this
// suite exists for, and "I could not find the thing I am guarding" is a defect.
//
// POSITIVE CONTROL (prove it can go red): halve LumberMill's localScale in the recipe
// -- RULE 1 must name it, its measured height and its ruled target. Re-parent a
// "Wood_Pallet" under the ring -- RULE 2 must name it. Add a twelfth child -- RULE 3.
// =============================================================================

using System;
using System.Collections.Generic;
using System.Globalization;
using DeNelle.Core.Diagnostics;
using UnityEditor;
using UnityEngine;

namespace DeNelle.Editor.Regression
{
    public static class HubRingHeightRegression
    {
        private const string FlowSys    = "HubRingHeight";
        private const string MarkerOk   = "HUB_RING_HEIGHT_OK";
        private const string MarkerFail = "HUB_RING_HEIGHT_FAIL";
        private const string Tag        = "hub-ring-height";

        /// <summary>⛔ FLIP THIS TO TRUE IN THE SAME COMMIT AS HubRingHeightApply.Run's
        /// landed output, and not before. While it is false this suite asserts NOTHING
        /// and says so in its reason line -- it is not a pass.
        /// <para>Deliberately <c>static readonly</c> and NOT <c>const</c>: a const false
        /// makes every line after the stand-down compile-time unreachable (CS0162), which
        /// buries the real rules under a warning and, worse, invites someone to "clean up"
        /// the unreachable code. A static readonly flips with the same one-word edit and
        /// keeps the rules compiled.</para></summary>
        private static readonly bool ApplyLanded = true; // flipped by the lead 2026-09-15 in the same commit as the HubRingHeightApply run (Builds/wo1762-apply.log 21:19)

        /// <summary>+/-5%. Tighter than the first version's 15% BECAUSE the targets are now
        /// per-root rulings rather than one family average: an exact number tolerates a tight
        /// band, an averaged one does not.</summary>
        private const float Tolerance = 0.05f;

        /// <summary>One owner-ruled root. <see cref="TotalMetres"/> is the whole bounds;
        /// <see cref="AboveGroundMetres"/> is pinned SEPARATELY and only where the two differ
        /// (i.e. where the model has geometry below its own ground plane). 0 = not pinned.</summary>
        private struct RuledRoot
        {
            public readonly string Name;
            public readonly float TotalMetres;
            public readonly float AboveGroundMetres;
            public readonly string Why;
            public RuledRoot(string name, float total, float aboveGround, string why)
            { Name = name; TotalMetres = total; AboveGroundMetres = aboveGround; Why = why; }
        }

        /// <summary>
        /// THE FOUR BUILDINGS WO-1762 IS ABOUT, and nothing else. Every number here is an
        /// OWNER RULING, cross-checked against the post-apply audit
        /// (Builds/hub-ring-scale-factors.suggested.json, 2026-09-15 02:38Z).
        /// <para>⚠ ArcaneTower_MagicUpgrades is the ROOT and it sits at scale 1 -- the Cathedral's
        /// scale actually lives on its baked child "arcane tower(Clone)" (WO-1762 section 1). The
        /// ROOT is named here on purpose: measuring its subtree measures the child, and the root
        /// name is the stable one. Do not re-point this row at the clone.</para>
        /// </summary>
        private static readonly RuledRoot[] Ruled =
        {
            new RuledRoot("LumberMill", 4.00f, 0f,
                "owner ruling: up to the 4.00 m Crystal Mine family height"),
            new RuledRoot("IronMine", 4.00f, 0f,
                "owner ruling: up to the 4.00 m Crystal Mine family height"),
            new RuledRoot("ArcaneTower_MagicUpgrades", 5.00f, 0f,
                "owner ruling 2026-09-15: the Cathedral is 'a little bit larger' than the family, 5.00 m"),
            new RuledRoot("Stone_Quarry", 5.00f, 4.00f,
                "owner ruling: 4.00 m VISIBLE above the courtyard plus a 1.00 m pit below it = 5.00 m " +
                "of total bounds. BOTH are pinned because the pit is exactly what makes a total-only " +
                "pin able to read green over a 3.20 m building (WO-1762 section 8.3)")
        };

        /// <summary>
        /// ⛔ NOT AN ASSERTION -- the 2026-09-15 post-apply BASELINE for the seven roots the owner
        /// did NOT ask about, recorded so the next seat can see what "leaving them alone" meant
        /// rather than re-measuring or, worse, re-deriving a family rule from them:
        /// <code>
        ///   Jeweler 4.89   RealmStore 4.90   Barracks 4.04   Weaponsmith 4.23
        ///   Armorer 5.03   Echo_Hollow 3.54  Crafting 2.33
        /// </code>
        /// Source: Builds/hub-ring-scale-factors.suggested.json, the audit run at 02:38Z, measured
        /// from live Renderer.bounds. They are deliberately UNPINNED: four of them failed the first
        /// version of RULE 1 for no reason other than that the rule was too wide, and widening a
        /// gate over buildings nobody was asked to change is how a suite gets switched off. If the
        /// owner later rules on one of these, give it a row in <see cref="Ruled"/> -- do not
        /// resurrect a blanket band.
        /// </summary>
        private const string UnruledBaselineNote =
            "Jeweler 4.89 / RealmStore 4.90 / Barracks 4.04 / Weaponsmith 4.23 / " +
            "Armorer 5.03 / Echo_Hollow 3.54 / Crafting 2.33 (2026-09-15 baseline, NOT asserted)";

        /// <summary>Fourteen authored children minus the three scenery pallets the owner ruled
        /// out on 2026-09-15. A literal on purpose: it is the shape of HER ring, and the whole
        /// point of RULE 3 is that a number nobody chose cannot drift into it.</summary>
        private const int ExpectedRingChildren = 11;

        /// <summary>The recipe the live ring is built from and compared against. Not an
        /// AssetRoots value, so spelling it here does not trip AssetRootsRegression RULE 1
        /// (that lint owns Assets/StructureContent, Assets/EnemyContent and the two legacy
        /// Resources roots -- read at AssetRoots.cs:55-81).</summary>
        private const string RecipePrefab = "Assets/Prefabs/Village/OwnerCastleStorefrontLayout.prefab";

        private const string PalletSuffix = "_Pallet";

        public static void RunStandalone()
        {
            string reason;
            bool pass = Run(out reason);
            Debug.Log("[" + Tag + "] standalone result: " + (pass ? "PASS" : "FAIL") + " - " + reason);
        }

        public static bool Run(out string reason)
        {
            try { return RunCore(out reason); }
            catch (Exception ex)
            {
                reason = Tag + ": oracle threw " + ex.GetType().Name + ": " + ex.Message;
                Debug.LogError(MarkerFail + " - " + reason);
                return false;
            }
        }

        private static bool RunCore(out string reason)
        {
            using var _scope = FlowTrace.Enter(FlowSys, "HubRingHeight.RunCore");

            if (!ApplyLanded)
            {
                FlowTrace.Warn(FlowSys, "stand-down: WO-1762 apply has not landed");
                return RegressionOutcome.Skip(out reason, Tag,
                    "WO-1762's HubRingHeightApply.Run has not landed yet (ApplyLanded is false in " +
                    "HubRingHeightRegression.cs). The authored ring is still at its pre-WO sizes and still " +
                    "carries the three scenery pallets ON PURPOSE, so asserting the post-apply invariants " +
                    "now would red a green tree. Flip ApplyLanded in the SAME commit as the apply");
            }

            var recipe = AssetDatabase.LoadAssetAtPath<GameObject>(RecipePrefab);
            if (recipe == null)
            {
                FlowTrace.Warn(FlowSys, "recipe prefab missing");
                return RegressionOutcome.Skip(out reason, Tag,
                    RecipePrefab + " could not be loaded -- the authored ring's heights could not be measured");
            }

            var failures = new List<string>();
            var measurements = new List<string>();
            int assertions = 0;

            var root = recipe.transform;

            // ---- RULE 2: no scenery pallet survives -------------------------------
            assertions++;
            foreach (var t in recipe.GetComponentsInChildren<Transform>(true))
            {
                if (t == null || !t.name.EndsWith(PalletSuffix, StringComparison.Ordinal)) continue;
                failures.Add("RULE 2 '" + t.name + "' is still under the authored ring in " + RecipePrefab +
                             ". The owner ruled the three scenery pallets out on 2026-09-15 ('you could remove " +
                             "the three storage that I added'). WO-1762 section 2 proves they were never " +
                             "storage: no AuthoredCastleStorefront, no canonicalId, no BaseLayout row, so " +
                             "TownBankCapacity.BuildSlots could never count them. Re-adding one puts scenery " +
                             "back in the courtyard that the player will read as a broken container.");
            }

            // ---- RULE 3: the ring's shape ----------------------------------------
            assertions++;
            if (root.childCount != ExpectedRingChildren)
                failures.Add("RULE 3 the authored ring has " + root.childCount + " children in " +
                             RecipePrefab + "; the owner's post-2026-09-15 ring is " + ExpectedRingChildren +
                             " (the authored fourteen minus the three scenery pallets). A different count " +
                             "means something was added or removed outside the scripted seam -- " +
                             "OwnerCastleRuntimeProof derives its own expectations from this same prefab, so " +
                             "it would follow the drift silently rather than catch it.");

            // ---- RULE 1: the four ruled roots, each at ITS ruled height ------------
            foreach (var ruled in Ruled)
            {
                var child = root.Find(ruled.Name);
                if (child == null)
                {
                    assertions++;
                    // HARD FAIL, never a partial-skip: this is one of the four objects the suite exists for.
                    failures.Add("RULE 1 the ruled root '" + ruled.Name + "' is not a child of " +
                                 RecipePrefab + ". " + ruled.Why + ". If it was renamed, update the Ruled " +
                                 "table -- do NOT delete the row, or the ruling stops being guarded.");
                    continue;
                }

                Bounds bounds;
                if (!TryMeshBounds(child, root, out bounds))
                {
                    assertions++;
                    failures.Add("RULE 1 the ruled root '" + ruled.Name + "' renders no shared-mesh geometry, " +
                                 "so its height cannot be measured. An authored building that renders nothing " +
                                 "is the WO-1716 husk shape, not a harness limitation.");
                    continue;
                }

                // The recipe root IS the ring, so a DIRECT child's localPosition.y is its ground plane
                // in this space -- the same value HubRingHeightApply calls groundY.
                float groundY = child.localPosition.y;
                float total = bounds.size.y;
                float above = bounds.max.y - groundY;
                measurements.Add(ruled.Name + " total=" + Fmt(total) + "m above=" + Fmt(above) + "m");

                assertions++;
                CheckBand(ruled.Name, "TOTAL bounds", total, ruled.TotalMetres, ruled.Why, failures);

                if (ruled.AboveGroundMetres > 0.0001f)
                {
                    assertions++;
                    CheckBand(ruled.Name, "VISIBLE height above its own ground plane (y=" + Fmt(groundY) + ")",
                              above, ruled.AboveGroundMetres, ruled.Why, failures);
                }
            }

            if (failures.Count > 0)
            {
                FlowTrace.Fail(FlowSys, "offenders=" + failures.Count);
                reason = Tag + " FAIL (" + failures.Count + " finding(s); " + Ruled.Length +
                         " ruled root(s), " + assertions + " assertion(s)): " +
                         string.Join(" | ", failures.ToArray()) +
                         " || measured: " + string.Join(", ", measurements.ToArray());
                Debug.LogError(MarkerFail + " - " + reason);
                return false;
            }

            FlowTrace.Step(FlowSys, "clean: ruled=" + Ruled.Length + " assertions=" + assertions);
            reason = Tag + " OK - " + Ruled.Length + " owner-ruled root(s), " + assertions +
                     " assertion(s): each measures its RULED height within +/-" +
                     ((int)(Tolerance * 100f)) + "% (" + string.Join(", ", measurements.ToArray()) +
                     "), the ring carries " + ExpectedRingChildren + " children, and no " + PalletSuffix +
                     " scenery survives under it. Un-ruled roots are deliberately not asserted: " +
                     UnruledBaselineNote + ".";
            Debug.Log(MarkerOk + " - " + reason);
            return true;
        }

        private static void CheckBand(string name, string what, float actual, float target,
                                      string why, List<string> failures)
        {
            float low = target * (1f - Tolerance);
            float high = target * (1f + Tolerance);
            if (actual >= low && actual <= high) return;
            failures.Add("RULE 1 '" + name + "' " + what + " measures " + Fmt(actual) + "m, outside " +
                         Fmt(low) + "-" + Fmt(high) + "m (ruled " + Fmt(target) + "m +/-" +
                         ((int)(Tolerance * 100f)) + "%). " + why +
                         ". This root carries AuthoredCastleStorefront.PreserveAuthoredVisual, so " +
                         "HubStructureVisualInjector returns early (HubStructureVisualInjector.cs:804-808) " +
                         "and the catalog's heightMul CANNOT reach it -- editing the catalog would be a " +
                         "silent no-op. The size authority is the authored localScale in " + RecipePrefab +
                         " and the scene; change it through DeNelle.Editor.HubRingHeightApply.Run, never " +
                         "by hand-editing the scene (CLAUDE.md section 3).");
        }

        /// <summary>The subtree's extent rebuilt from shared-mesh bounds corners, expressed in
        /// <paramref name="space"/>'s local frame. Renderer.bounds does not exist on a prefab
        /// asset; this is the same arithmetic HubRingHeightAudit prints alongside the live
        /// Renderer.bounds so the two can be compared on the same objects.</summary>
        private static bool TryMeshBounds(Transform subject, Transform space, out Bounds bounds)
        {
            bounds = default;
            if (subject == null || space == null) return false;
            bool any = false;
            foreach (var filter in subject.GetComponentsInChildren<MeshFilter>(true))
            {
                if (filter == null || filter.sharedMesh == null) continue;
                Encapsulate(filter.sharedMesh.bounds, filter.transform, space, ref bounds, ref any);
            }
            foreach (var skin in subject.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (skin == null || skin.sharedMesh == null) continue;
                Encapsulate(skin.sharedMesh.bounds, skin.transform, space, ref bounds, ref any);
            }
            return any;
        }

        private static void Encapsulate(Bounds local, Transform from, Transform space, ref Bounds acc, ref bool any)
        {
            for (int corner = 0; corner < 8; corner++)
            {
                var offset = new Vector3((corner & 1) == 0 ? -1f : 1f,
                                         (corner & 2) == 0 ? -1f : 1f,
                                         (corner & 4) == 0 ? -1f : 1f);
                var point = local.center + Vector3.Scale(local.extents, offset);
                point = space.InverseTransformPoint(from.TransformPoint(point));
                if (!any) { acc = new Bounds(point, Vector3.zero); any = true; }
                else acc.Encapsulate(point);
            }
        }

        private static string Fmt(float v) => v.ToString("0.00", CultureInfo.InvariantCulture);
    }
}

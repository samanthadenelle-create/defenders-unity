// =============================================================================
// VfxParticleNullSlotRegression [vfx-null-slot] -- the oracle that stops a
// catalogued VFX prefab with a NULL-material renderer from reaching the owner's
// F8 queue as a MagentaProbe M2 FAIL.
// -----------------------------------------------------------------------------
// Assembly: DeNelle.EditorRegression   Namespace: DeNelle.Editor.Regression
// Markers:  VFX_NULL_SLOT_OK / VFX_NULL_SLOT_FAIL (distinct per entry point,
//           per the 2026-08-02 shared-marker lesson).
//
// THE INCIDENT THIS GUARDS (WO-1100, owner F8 seq 2404-2415, 2026-08-16):
//   12 identical captures per session --
//     [Flow:MagentaProbe] FAIL cause=DungeonWorldPortalSpawner.BuildPortal
//     obj='...[Hovl_Portal_Threshold_Aura]' slot=0 material='NULL' shader='NULL'
//     class=M2
//   Triage at source proved NOTHING was missing: the Mirza Beig prefab behind
//   'Portal_Threshold_Aura' (pf_vfx-ult_demo_psys_loop_portalBlue) carries two
//   ParticleSystemRenderers authored m_Enabled: 0 with all-null m_Materials --
//   the vendor CONTAINER pattern (the system only parents/drives children; its
//   renderer is off; 339 renderers across the packs share the shape). Every
//   material its ENABLED renderers reference exists on disk. MagentaGuard
//   correctly refuses to repaint an all-null particle renderer (the 08-05
//   white-blob lesson) and so probed it at FAIL, once per portal, forever.
//
// WHY THE EXISTING GATES COULD NOT CATCH IT:
//   * VFX_ART_MIRROR_OK / vfx-self-contained measure GITIGNORED-ROOT REACH.
//     This prefab's catalog exposure is a DELIBERATE, owner-ruled baseline
//     (VfxResourceSelfContainmentRegression.KnownCatalogExposure, 2026-08-14
//     entry naming this very key) -- mirroring the pack is the recorded WRONG
//     remedy, so "widen the mirror" is not the fix.
//   * Nothing anywhere asserted the SLOT-LEVEL shape of a catalogued prefab.
//
// WHAT THIS ASSERTS, for every prefab under Assets/Resources/VFX/** AND every
// HovlVfxCatalog row whose prefab resolves on this machine:
//   * An ENABLED ParticleSystemRenderer whose material slots are ALL null is an
//     offender only when its authored renderMode can draw. A renderer authored as
//     ParticleSystemRenderMode.None intentionally renders no particles, so an
//     empty material list is valid and is reported as an intentional non-renderer.
//   * A DISABLED all-null renderer is the vendor container pattern -- counted and
//     reported, never failed. The runtime normalizer
//     (VFXManager.NormalizeVendorContainerRenderers, WO-1100) fills its slot 0
//     with a same-instance donor at spawn so MagentaProbe stays quiet.
//   * (WO-1806) An ENABLED, BILLBOARD-drawing ParticleSystemRenderer whose SLOT 0 holds
//     the editor fixer's opaque Lit placeholder (MagentaFix*, guid 751cde1de5b29b247-
//     bba48305ded45f5 = Assets/Materials/MagentaFix_DefaultLit.mat: URP/Lit, RenderType
//     Opaque, _BaseColor 0.70 grey, _BaseMap NULL) is an offender -- it draws a flat
//     untextured grey quad in front of the player. Three deliberate narrowings, each
//     measured, not assumed:
//       - SLOT 0 ONLY. A trail slot (i>0) is repaired at runtime by re-pointing it at
//         slot 0 (AbilityVfxKit.TryRepairOpaqueLitParticleSlot); slot 0 has no sibling
//         to borrow from, so nothing repaired it until this WO.
//       - renderMode None and DISABLED renderers draw nothing.
//       - renderMode MESH is skipped: a mesh particle (debris, shards) is legitimately
//         opaque and often untextured, carried by vertex colour.
//     NAME-GATED, not heuristic: "any opaque URP material with no _BaseMap" would also
//     match those mesh particles. MagentaFix* is the producer this WO proved
//     (MagentaMaterialFixer.AssignDefaultToNullSlots), so MagentaFix* is what fails.
//     Census by that exact guid, 2026-09-16: project-wide 4,197 such slots on
//     ParticleSystemRenderers across 969 prefabs (990 inert, 3,186 trail slots, 21 slot-0
//     on a live renderer -- all Mirza Beig demo prefabs, none in a shipped catalog).
//     Inside this suite's own scope, Assets/Resources/VFX carries 38 slots across 22
//     prefabs: 27 slot-0, EVERY ONE of them renderMode None or renderer disabled, and 11
//     trail slots. So this assertion passes today by measurement, and pins the tree
//     before the next one lands.
//   * Rows whose prefab does not resolve (gitignored packs on a fresh clone) are
//     SKIPPED AND COUNTED, never failed -- and the pass line says how many were
//     not proven (a clean clone must not go red; a hollow pass must not lie).
//
// POSITIVE CONTROL (prove it can go red): temporarily add any enabled, drawable
// ParticleSystemRenderer key to a Resources/VFX prefab with its material slot
// cleared, re-run -- the suite must fail naming that prefab/child/slot count.
//
// Deterministic, editor-only asset reads. No scene, no play mode.
// Registered in DataRegression.RunAll as [vfx-null-slot].
// =============================================================================

using System;
using System.Collections.Generic;
using DeNelle.Core.Diagnostics;
using DeNelle.Village;
using UnityEditor;
using UnityEngine;

namespace DeNelle.Editor.Regression
{
    public static class VfxParticleNullSlotRegression
    {
        private const string FlowSys    = "VfxNullSlot";
        private const string MarkerOk   = "VFX_NULL_SLOT_OK";
        private const string MarkerFail = "VFX_NULL_SLOT_FAIL";

        private const string HovlCatalogPath = VfxLoopFlagRegression.HovlCatalogPath;

        /// <summary>WO-1813: the four prefabs playing at the hero in the owner's 2026-09-16
        /// 20:51:30 capture (Juice_LevelUp, Death_Brute, Cast_FireCharge, Impact_Flame), pinned
        /// by path. Paths read off <c>VFXCatalogGenerator.Map</c> and verified on disk.
        /// Level_up.prefab is the one that matters: it is the ONLY one of the four that neither
        /// existing source reaches.</summary>
        private static readonly string[] Wo1813CoveragePrefabs =
        {
            "Assets/Lana Studio/Casual RPG VFX/Prefabs/States/Level_up.prefab", // VFXType.Juice_LevelUp
            "Assets/Resources/VFX/Death/Death_Brute.prefab",                    // VFXType.Death_Brute
            "Assets/Resources/VFX/Projectiles/Casting_Fire.prefab",             // VFXType.Cast_FireCharge
            "Assets/Resources/VFX/Status/BigExplosion.prefab",                  // VFXType.Impact_Flame
        };

        /// <summary>Standalone batch entry point (prints the distinct marker).</summary>
        public static void RunStandalone()
        {
            string reason;
            bool pass = Run(out reason);
            Debug.Log("[vfx-null-slot] standalone result: " + (pass ? "PASS" : "FAIL") + " - " + reason);
        }

        /// <summary>DataRegression-shaped contract. NEVER throws.</summary>
        public static bool Run(out string reason)
        {
            try
            {
                return RunCore(out reason);
            }
            catch (Exception ex)
            {
                reason = "vfx-null-slot: oracle threw " + ex.GetType().Name + ": " + ex.Message;
                Debug.LogError(MarkerFail + " - " + reason);
                return false;
            }
        }

        private static bool RunCore(out string reason)
        {
            using var _scope = FlowTrace.Enter(FlowSys, "VfxParticleNullSlot.RunCore");

            var failures  = new List<string>();
            int checkedAssets = 0, skipped = 0, containers = 0, intentionalNonRenderers = 0;

            // ONE scan list, two sources, one rule -- the loop-flag / self-containment
            // discipline: the scope is (a) every prefab in the curated tree and (b) every
            // catalog row that resolves, deduped by prefab identity via the label used
            // for baselining (catalog key wins so the baseline survives a pack re-path).
            var work = new List<KeyValuePair<string, GameObject>>();   // label -> prefab
            var seen = new HashSet<UnityEngine.Object>();

            var hovl = AssetDatabase.LoadAssetAtPath<HovlVfxCatalog>(HovlCatalogPath);
            if (hovl == null)
            {
                // Same stance as [vfx-loop-flag]: a missing catalog is itself the failure.
                failures.Add("HovlVfxCatalog.asset did not load from " + HovlCatalogPath +
                             " -- no catalogued prefab could be checked.");
            }
            else
            {
                var rows = hovl.Rows ?? new HovlVfxCatalog.Row[0];
                for (int i = 0; i < rows.Length; i++)
                {
                    var row = rows[i];
                    string key = string.IsNullOrEmpty(row.Key) ? ("<row " + i + ">") : row.Key;
                    if (row.Prefab == null) { skipped++; continue; }   // pack absent on this machine
                    if (!seen.Add(row.Prefab)) continue;
                    work.Add(new KeyValuePair<string, GameObject>(key, row.Prefab));
                }
            }

            foreach (var p in VfxResourceSelfContainmentRegression.VfxPrefabPaths())
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(p);
                if (prefab == null) { skipped++; continue; }
                if (!seen.Add(prefab)) continue;                        // already in via a catalog row
                work.Add(new KeyValuePair<string, GameObject>(p, prefab));
            }

            // ── WO-1813 named coverage ───────────────────────────────────────
            // The two sources above are (a) HovlVfxCatalog rows and (b) everything under
            // Assets/Resources/VFX/. NEITHER reaches the prefab that VFXType.Juice_LevelUp
            // resolves to: `VFXCatalogGenerator.Map` points it at
            // Assets/Lana Studio/Casual RPG VFX/Prefabs/States/Level_up.prefab, which is
            // outside the Resources tree and is a VFXCatalog row, not a Hovl one. So the
            // effect the owner photographed on 2026-09-16 (white quads on the hero during a
            // level-up, WO-1813) was never in this oracle's scan set at all — a coverage
            // HOLE, not a passing case. These four are pinned BY PATH, and a missing file is
            // a named failure rather than a silent skip, because the whole point is that
            // their absence from the scan is what let the defect through.
            foreach (var p in Wo1813CoveragePrefabs)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(p);
                if (prefab == null)
                {
                    failures.Add("WO-1813 coverage prefab '" + p + "' did not load. It is named here " +
                                 "precisely because neither the Hovl catalog nor the Assets/Resources/VFX/ " +
                                 "sweep reaches it; if it moved, re-point this list or the level-up / " +
                                 "fireball effects fall out of the oracle unnoticed.");
                    continue;
                }
                if (!seen.Add(prefab)) continue;                        // already in via a source above
                work.Add(new KeyValuePair<string, GameObject>(p, prefab));
            }

            FlowTrace.Step(FlowSys, "scan set=" + work.Count + " prefab(s), skipped(unresolved)=" + skipped);

            foreach (var item in work)
            {
                string label  = item.Key;
                var prefab    = item.Value;
                checkedAssets++;

                int enabledNull = 0, disabledNull = 0;
                string firstOffender = null;
                foreach (var r in prefab.GetComponentsInChildren<ParticleSystemRenderer>(true))
                {
                    if (r == null) continue;
                    var mats = r.sharedMaterials;
                    if (mats == null || mats.Length == 0) continue;

                    // WO-1806 — the SECOND way a particle renderer draws a defect: not a NULL
                    // slot, but a slot holding the editor fixer's OPAQUE Lit placeholder
                    // (MagentaFix_DefaultLit: URP/Lit, Opaque, _BaseColor 0.70 grey, _BaseMap
                    // NULL). On a drawing particle renderer that is a flat untextured grey
                    // quad in front of the player (owner F8, RaidBase_raider_camp_small,
                    // 2026-09-16). Only SLOT 0 fails here: a trail slot (i>0) is repaired at
                    // runtime by re-pointing it at slot 0, and that path is covered; slot 0
                    // has no donor, so nothing repaired it until this WO. renderMode None and
                    // disabled renderers draw nothing and are not offenders.
                    if (r.enabled && r.renderMode != ParticleSystemRenderMode.None &&
                        r.renderMode != ParticleSystemRenderMode.Mesh &&
                        mats[0] != null && AbilityVfxKit.IsMagentaFixParticlePlaceholder(mats[0]))
                    {
                        failures.Add("'" + label + "' child '" + r.gameObject.name + "' draws with the " +
                                     "editor fixer's OPAQUE Lit placeholder in particle SLOT 0 ('" + mats[0].name +
                                     "', shader '" + (mats[0].shader != null ? mats[0].shader.name : "NULL") +
                                     "', renderMode=" + r.renderMode + "). That renders as a flat untextured " +
                                     "quad in front of the player (WO-1806). Slot 0 has no sibling to borrow " +
                                     "from, so the runtime trail re-point cannot cover it. Fix the prefab: " +
                                     "assign the pack's particle material, or MagentaFix_DefaultParticle_URP, " +
                                     "never MagentaFix_DefaultLit on a particle renderer.");
                    }

                    bool allNull = true;
                    for (int m = 0; m < mats.Length; m++)
                    {
                        if (mats[m] != null) { allNull = false; break; }
                    }
                    if (!allNull) continue;
                    if (r.renderMode == ParticleSystemRenderMode.None)
                    {
                        intentionalNonRenderers++;
                        continue;
                    }
                    if (r.enabled)
                    {
                        enabledNull++;
                        if (firstOffender == null) firstOffender = r.gameObject.name;
                    }
                    else
                    {
                        disabledNull++;   // vendor container -- runtime normalizer handles it
                    }
                }
                containers += disabledNull;

                if (enabledNull == 0)
                    continue;

                failures.Add("'" + label + "' carries " + enabledNull + " ENABLED ParticleSystemRenderer(s) " +
                             "with ALL material slots null (first: '" + firstOffender + "')" +
                             "." +
                             " That renderer draws engine-default MAGENTA, the runtime deliberately will not " +
                             "repaint a particle slot (the 08-05 white-blob lesson), and every spawn F8-spams a " +
                             "MagentaProbe M2 FAIL. Fix the prefab (assign the pack's particle material or " +
                             "disable it, or author renderMode=None when it intentionally has no visual output).");
            }

            // ── WO-1813: the OPAQUE DRAWING BILLBOARD class ──────────────────
            int opaqueBillboards = CheckOpaqueDrawingBillboards(work, failures);

            // ── WO-1813: every drawing slot ends up with an albedo ───────────
            int albedoRepairs = CheckEveryDrawingSlotEndsUpTextured(failures);

            if (failures.Count > 0)
            {
                FlowTrace.Fail(FlowSys, "offenders=" + failures.Count + " across " + checkedAssets + " prefab(s)");
                reason = "vfx-null-slot FAIL (" + failures.Count + " finding(s); " + checkedAssets +
                         " prefab(s) checked, " + skipped + " skipped-unresolved): " +
                         string.Join(" | ", failures.ToArray());
                Debug.LogError(MarkerFail + " - " + reason);
                return false;
            }

            FlowTrace.Step(FlowSys, "clean: " + checkedAssets + " prefab(s), containers=" + containers +
                                    ", intentionalNonRenderers=" + intentionalNonRenderers +
                                    ", opaqueBillboardsRepaired=" + opaqueBillboards +
                                    ", albedoRepairs=" + albedoRepairs +
                                    ", skipped=" + skipped);
            reason = "vfx-null-slot OK - " + albedoRepairs + " drawing slot(s) on the WO-1813 capture " +
                     "prefabs proved TEXTURED after the runtime repair chain; " +
                     opaqueBillboards + " authored opaque drawing particle slot(s) " +
                     "proved REPAIRED by AbilityVfxKit.RepairOpaqueDrawingParticleSlots (WO-1813); " +
                     checkedAssets + " prefab(s) checked: no NEW enabled all-null " +
                     "particle renderer capable of drawing; " + intentionalNonRenderers +
                     " enabled renderMode=None system(s) counted as intentional non-renderers; " +
                     containers + " DISABLED all-null vendor container renderer(s) noted " +
                     "(normalized at spawn by VFXManager, WO-1100); " + skipped +
                     " prefab(s) skipped as unresolved (gitignored packs are not a failure).";
            Debug.Log(MarkerOk + " - " + reason);
            return true;
        }

        // =====================================================================
        //  WO-1813 -- the OPAQUE DRAWING BILLBOARD class
        // ---------------------------------------------------------------------
        // THE DEFECT, named: Assets/Resources/VFX/Impact/FleshImpacts.prefab (catalog
        // key PP_FleshImpacts) child 'Mist', ParticleSystemRenderer fileID
        // 199462925942737768, SLOT 0, Billboard, ENABLED, material 'GoopMist'
        // (URP/Lit, _Surface 0, _AlphaClip 0, _BaseMap DustPuffSmallParticleSheet.png
        // whose RGB is 255/255/255 at every texel -- the whole sprite is in its ALPHA).
        // Drawn opaque, that quad discards the alpha and paints a solid white rectangle.
        // It played at the hero's exact position with 8.30 s of life, 1.6 s before the
        // owner's 2026-09-16 20:51:31 capture, and no instrument mentioned it once.
        //
        // WHY THIS IS NOT A BASELINE LIST. A ratcheted allow-list would pin the defect,
        // not the fix. What must hold is that the RUNTIME REPAIR COVERS the class, so this
        // check does the end-to-end thing: read-only scan finds every authored offender,
        // then for each offending prefab it INSTANTIATES a throwaway copy, runs the very
        // helper the two spawn paths run, and re-scans. A remaining offender is the
        // failure. A prefab that is authored broken but proven repaired is reported as a
        // COUNT, not a finding -- because after the repair the player never sees it.
        //
        // The scan is deliberately the same shape as the runtime rule, so the two cannot
        // drift: enabled, renderMode neither None nor Mesh, _Surface < 0.5, _AlphaClip < 0.5.
        // MESH mode is excluded (debris/shards are legitimately opaque) and ALPHA CUTOUT is
        // excluded because the cutout already carves the sprite's shape -- measured, not
        // assumed: StoneImpacts/'ImpactDebris' and WoodImpacts/'WoodSplinters' are opaque
        // billboards too, both _AlphaClip 1, and both render correctly today.
        // =====================================================================
        private static int CheckOpaqueDrawingBillboards(
            List<KeyValuePair<string, GameObject>> work, List<string> failures)
        {
            int authoredOffenders = 0;

            foreach (var item in work)
            {
                var prefab = item.Value;
                if (prefab == null) continue;
                if (CountOpaqueDrawingSlots(prefab, out _, out _) == 0) continue;

                // Authored broken. Now prove the runtime repair clears it, on a copy --
                // the repair clones materials, so no asset on disk is touched.
                GameObject copy = null;
                try
                {
                    copy = UnityEngine.Object.Instantiate(prefab);
                    copy.hideFlags = HideFlags.HideAndDontSave;
                    int before = CountOpaqueDrawingSlots(copy, out string firstChild, out int firstSlot);
                    authoredOffenders += before;

                    AbilityVfxKit.RepairOpaqueDrawingParticleSlots(copy, item.Key);

                    int after = CountOpaqueDrawingSlots(copy, out string stillChild, out int stillSlot);
                    if (after > 0)
                    {
                        failures.Add("'" + item.Key + "' still carries " + after + " DRAWING particle slot(s) " +
                                     "whose material is authored OPAQUE with no alpha cutout AFTER " +
                                     "AbilityVfxKit.RepairOpaqueDrawingParticleSlots ran (first: child '" +
                                     stillChild + "' slot " + stillSlot + "; " + before + " before the repair). " +
                                     "A camera-facing particle quad drawn opaque discards its sprite's alpha and " +
                                     "paints a solid rectangle in front of the player -- the white quad on the " +
                                     "hero in the owner's 2026-09-16 20:51:31 frame (WO-1813). Either the repair " +
                                     "predicate no longer matches this shape, or a new carve-out excluded it.");
                    }
                }
                catch (Exception ex)
                {
                    failures.Add("'" + item.Key + "' threw while proving the WO-1813 opaque-billboard repair: " +
                                 ex.GetType().Name + ": " + ex.Message);
                }
                finally
                {
                    if (copy != null) UnityEngine.Object.DestroyImmediate(copy);
                }
            }

            return authoredOffenders;
        }

        // =====================================================================
        //  WO-1813 -- NO DRAWING SLOT MAY END UP WITHOUT AN ALBEDO
        // ---------------------------------------------------------------------
        // THE DEFECT THIS PINS: Assets/Lana Studio/Casual RPG VFX/Prefabs/States/
        // Level_up.prefab (VFXType.Juice_LevelUp) has TEN drawing renderer slots and
        // every one of them points at 1AB_mat or 1Add_mat, both of which carry
        // _BaseMap: {fileID: 0} AND _MainTex: {fileID: 0} -- no albedo at all. An
        // untextured quad samples pure white over its whole footprint, and on the
        // owner's sunlit town the additive ones saturate to the white slab she
        // photographed on 2026-09-16 at 20:51:31.
        //
        // WHY THE ASSERTION IS "ENDS UP TEXTURED", NOT "IS TEXTURED ON DISK: the
        // remedy is a RUNTIME one (the pack's texture guids are gone -- see
        // AbilityVfxKit.RepairUntexturedMeshParticleSlots for the source read), so the
        // only meaningful question is whether the chain the two spawn paths run leaves
        // every drawing slot with something to sample. That is what this runs and
        // measures, in the same order the game runs it:
        //     HealHalfUpgradedParticleMaterial  (billboards -> radial soft dot)
        //     RepairOpaqueDrawingParticleSlots  (opaque billboards -> transparent)
        //     RepairMagentaFixParticleSlots     (the WO-1806 placeholder)
        //     RepairUntexturedMeshParticleSlots (mesh slots -> two-axis soft edge)
        //
        // ⚠ IT RUNS ON PRIVATE CLONES, AND THAT IS LOAD-BEARING. HealHalfUpgraded-
        // ParticleMaterial mutates the SHARED material, which in the editor means the
        // .mat asset on disk -- exactly how a batch run left a tracked
        // GoopMist.mat modified on 2026-09-17. Every slot is replaced with
        // `new Material(src)` BEFORE the chain touches anything, so this oracle can
        // never dirty the tree it is checking.
        // =====================================================================
        private static readonly string[] Wo1813AlbedoPrefabs = Wo1813CoveragePrefabs;

        private static int CheckEveryDrawingSlotEndsUpTextured(List<string> failures)
        {
            int slotsProved = 0;

            foreach (var path in Wo1813AlbedoPrefabs)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null) continue;   // already reported as a coverage failure above

                GameObject copy = null;
                var scratch = new List<Material>();
                try
                {
                    copy = UnityEngine.Object.Instantiate(prefab);
                    copy.hideFlags = HideFlags.HideAndDontSave;

                    // Private clones FIRST -- nothing below may reach a shared asset.
                    foreach (var r in copy.GetComponentsInChildren<ParticleSystemRenderer>(true))
                    {
                        if (r == null) continue;
                        var mats = r.sharedMaterials;
                        if (mats == null) continue;
                        for (int i = 0; i < mats.Length; i++)
                        {
                            if (mats[i] == null) continue;
                            var clone = new UnityEngine.Material(mats[i])
                            {
                                name = mats[i].name, hideFlags = HideFlags.HideAndDontSave
                            };
                            scratch.Add(clone);
                            mats[i] = clone;
                        }
                        r.sharedMaterials = mats;
                    }

                    foreach (var r in copy.GetComponentsInChildren<ParticleSystemRenderer>(true))
                    {
                        if (r == null) continue;
                        foreach (var m in r.sharedMaterials)
                            if (m != null) AbilityVfxKit.HealHalfUpgradedParticleMaterial(m);
                    }
                    AbilityVfxKit.RepairOpaqueDrawingParticleSlots(copy, path);
                    AbilityVfxKit.RepairMagentaFixParticleSlots(copy, path);
                    AbilityVfxKit.RepairUntexturedMeshParticleSlots(copy, path);

                    foreach (var r in copy.GetComponentsInChildren<ParticleSystemRenderer>(true))
                    {
                        if (r == null || !r.enabled) continue;
                        if (r.renderMode == ParticleSystemRenderMode.None) continue;

                        var mats = r.sharedMaterials;
                        if (mats == null) continue;
                        for (int i = 0; i < mats.Length; i++)
                        {
                            var m = mats[i];
                            if (m == null || m.shader == null) continue;
                            if (!m.HasProperty("_BaseMap")) continue;   // not an albedo shader

                            if (m.GetTexture("_BaseMap") != null) { slotsProved++; continue; }

                            failures.Add("'" + path + "' child '" + r.gameObject.name + "' slot " + i +
                                         " (" + r.renderMode + ", material '" + m.name + "', shader '" +
                                         m.shader.name + "') still has NO _BaseMap after the full runtime " +
                                         "repair chain. An untextured drawing particle samples pure white " +
                                         "across its whole footprint; on a bright scene the additive ones " +
                                         "saturate into the flat white slab the owner captured on " +
                                         "2026-09-16 (WO-1813). Every drawing slot must end up with the " +
                                         "pack's texture, the radial soft dot (billboard) or the two-axis " +
                                         "soft edge (mesh).");
                        }
                    }
                }
                catch (Exception ex)
                {
                    failures.Add("'" + path + "' threw while proving the WO-1813 albedo chain: " +
                                 ex.GetType().Name + ": " + ex.Message);
                }
                finally
                {
                    if (copy != null) UnityEngine.Object.DestroyImmediate(copy);
                    foreach (var m in scratch)
                        if (m != null) UnityEngine.Object.DestroyImmediate(m);
                }
            }

            return slotsProved;
        }

        /// <summary>
        /// Count the DRAWING particle slots whose material is authored opaque with no alpha
        /// cutout. Mirrors AbilityVfxKit's runtime predicate exactly so the oracle and the
        /// repair cannot drift apart.
        /// </summary>
        private static int CountOpaqueDrawingSlots(GameObject go, out string firstChild, out int firstSlot)
        {
            firstChild = null;
            firstSlot  = -1;
            if (go == null) return 0;

            int count = 0;
            foreach (var r in go.GetComponentsInChildren<ParticleSystemRenderer>(true))
            {
                if (r == null || !r.enabled) continue;
                if (r.renderMode == ParticleSystemRenderMode.None) continue;
                if (r.renderMode == ParticleSystemRenderMode.Mesh) continue;

                var mats = r.sharedMaterials;
                if (mats == null) continue;
                for (int i = 0; i < mats.Length; i++)
                {
                    var m = mats[i];
                    if (m == null || m.shader == null) continue;
                    // The MagentaFix placeholder is the OTHER defect, with its own assertion
                    // above and its own runtime remedy. Not double-counted here.
                    if (AbilityVfxKit.IsMagentaFixParticlePlaceholder(m)) continue;
                    if (!m.HasProperty("_Surface") || m.GetFloat("_Surface") >= 0.5f) continue;
                    if (m.HasProperty("_AlphaClip") && m.GetFloat("_AlphaClip") >= 0.5f) continue;

                    count++;
                    if (firstChild == null) { firstChild = r.gameObject.name; firstSlot = i; }
                }
            }
            return count;
        }
    }
}

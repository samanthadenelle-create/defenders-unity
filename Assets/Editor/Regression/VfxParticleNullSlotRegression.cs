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
                                    ", skipped=" + skipped);
            reason = "vfx-null-slot OK - " + checkedAssets + " prefab(s) checked: no NEW enabled all-null " +
                     "particle renderer capable of drawing; " + intentionalNonRenderers +
                     " enabled renderMode=None system(s) counted as intentional non-renderers; " +
                     containers + " DISABLED all-null vendor container renderer(s) noted " +
                     "(normalized at spawn by VFXManager, WO-1100); " + skipped +
                     " prefab(s) skipped as unresolved (gitignored packs are not a failure).";
            Debug.Log(MarkerOk + " - " + reason);
            return true;
        }
    }
}

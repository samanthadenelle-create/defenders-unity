// =============================================================================
// HubRingHeightApply (WO-1762 steps 2-3) -- REMOVE the three scenery pallets and
// RESCALE the authored ring roots to the measured 4.00 m family height.
// -----------------------------------------------------------------------------
// Assembly: DeNelle.Editor.  Namespace: DeNelle.Editor.
// Batchmode: run-unity-method.ps1 -Method DeNelle.Editor.HubRingHeightApply.Run
// Markers: HUB_RING_APPLY_OK / HUB_RING_APPLY_FAIL.
//
// ⛔ DO NOT RUN THIS BEFORE HubRingHeightAudit.Run. Its scale inputs come from
// Builds/hub-ring-scale-factors.json, which the lead produces by reviewing and
// renaming the audit's Builds/hub-ring-scale-factors.suggested.json. With no such
// file this method still removes the pallets and rescales NOTHING, and says so --
// it never invents a factor.
//
// OWNER'S RULING (2026-09-15, quoted in WO-1762 section 0):
//   "You could remove the three storage that I added; that gives more room when you
//    properly size the other components on the Y."
// WO-1762 section 2 proves those three were never storage: they carry no
// AuthoredCastleStorefront, no canonicalId, no bakedTwins row, and
// TownBankCapacity.BuildSlots (TownBankCapacity.cs:977-1010) counts only
// GameState.BaseLayout rows that are in the ever-built ledger. A bare scene prefab
// instance writes neither, so the bank could never have seen them. They are scenery.
//
// ── HOW THE PALLETS ARE REMOVED, AND WHY IT IS NOT A "RAW DESTROY" ───────────
// WO-1762 step 2 says to remove them "through StructureVisualStrip.StripHostVisual
// + EnsureNoHusk (the WO-1716 rule) -- never a raw destroy". That pair is the
// IN-PLACE RE-SKIN seam: strip the host's own visual, then, if the replacement
// visual failed to resolve, take the now-orphaned solid collision and carve away so
// nothing invisible is left blocking (CastleHubBuilder.cs:607-646). It is the right
// tool when an object STAYS. Here the object GOES, and StructureVisualStrip's own
// header says what that case wants, verbatim at StructureVisualStrip.cs:62-63:
//
//     "Child GameObjects -- callers that want a child's visual gone destroy the
//      whole child object, which leaves no husk by construction."
//
// A pallet's renderers live on its children, so StripHostVisual alone would strip
// the ROOT (often nothing) and EnsureNoHusk would correctly no-op while the pallet
// still stood there rendering. That is theatre, not removal.
//
// So this method does BOTH, in the order that makes each one mean something:
//   1. StripHostVisual + EnsureNoHusk on the pallet root -- honours the WO's letter
//      and clears any host-level renderer / solid collider / NavMeshObstacle FIRST,
//      so the log records what the root was actually carrying;
//   2. DestroyImmediate the whole child GameObject -- the removal itself;
//   3. PROVE no husk survived: no Transform named *_Pallet anywhere under the ring,
//      and no Collider or NavMeshObstacle left ANYWHERE in the scene whose bounds
//      intersect each pallet's recorded pre-destroy world bounds. That third step is
//      the actual WO-1716 invariant ("an invisible thing that still blocks or
//      carves"), asserted directly instead of trusted to a helper.
// The lead owns the ruling on that reading; it is named in the hand-back.
//
// ── WHY THE RESCALE RE-SEATS ─────────────────────────────────────────────────
// A uniform scale multiplies about the PIVOT. The Cathedral's visual child was
// seated by shifting its position down until its bounds touched the ground
// (OwnerCastleLayoutRepair.cs:175), so its pivot is NOT at its base, and the FBX
// roots' pivots are unverified. Scaling any of them without re-seating sinks the
// building into the courtyard or floats it above it. So: record bounds.min.y
// before, set the scale, and translate by the delta so bounds.min.y is unchanged.
// The audit prints pivotOffsetY per root precisely so this correction is auditable.
//
// ── IDEMPOTENCE ──────────────────────────────────────────────────────────────
// The JSON carries ABSOLUTE target scales, never multipliers, so re-applying is a
// comparison rather than a second multiplication. When every target is already
// within ScaleEpsilon and no pallet remains, the method writes nothing at all --
// no scene save, no prefab re-save -- and logs HUB_RING_APPLY_OK with "noop".
//
// ── AFTERWARDS ───────────────────────────────────────────────────────────────
// This changes footprints and removes NavMeshObstacle carvers, so the navmesh is
// STALE until DeNelle.Editor.NavMeshBakeFinal.Run (NavMeshBakeFinal.cs:63) runs.
// Judge that bake by CONTENT, not by its marker (memory
// bake-marker-can-be-green-on-the-wrong-operation).
// =============================================================================

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using DeNelle.Core.Diagnostics;
using DeNelle.Village;          // SkinOptions / VisualFactory (VisualFactory.cs:34, :155)
using DeNelle.Village.World;    // StructureVisualStrip
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace DeNelle.Editor
{
    public static class HubRingHeightApply
    {
        private const string FlowSys = "HubRingHeight";
        private const string MarkerOk = "HUB_RING_APPLY_OK";
        private const string MarkerFail = "HUB_RING_APPLY_FAIL";

        /// <summary>The factors file the lead produces from the audit's suggestion.
        /// ABSENT IS LEGAL: the pallets still go, nothing is rescaled, the log says so.</summary>
        public const string FactorsPath = "Builds/hub-ring-scale-factors.json";

        /// <summary>A localScale already this close to its target is left alone. That is
        /// what makes the second run a no-op instead of a second nudge.</summary>
        public const float ScaleEpsilon = 0.0001f;

        [MenuItem("Defenders/Art/Apply hub ring heights + remove pallets (WO-1762)")]
        public static void Menu() => Run();

        public static void Run()
        {
            var setup = EditorSceneManager.GetSceneManagerSetup();
            var evidence = new List<string>();
            var failures = new List<string>();
            bool opened = false, empty = false, changed = false, saved = false, noop = false;

            try
            {
                if (Application.isPlaying) throw new InvalidOperationException("Edit mode only");
                for (int i = 0; i < SceneManager.sceneCount; i++)
                {
                    var current = SceneManager.GetSceneAt(i);
                    if (current.isDirty) throw new InvalidOperationException("Dirty scene refusal: " + current.path);
                    if (string.IsNullOrEmpty(current.path))
                    {
                        if (!Application.isBatchMode || SceneManager.sceneCount != 1 || current.rootCount != 0)
                            throw new InvalidOperationException("Populated/interactive untitled scene; refusing");
                        empty = true;
                    }
                }

                var plan = LoadPlan(evidence);

                opened = true;
                var scene = EditorSceneManager.OpenScene(OwnerCastleLayoutAudit.ScenePath, OpenSceneMode.Single);
                var ring = scene.GetRootGameObjects()
                    .SelectMany(g => g.GetComponentsInChildren<Transform>(true))
                    .Single(t => t.name == OwnerCastleLayoutAudit.RingName);

                var palletsPresent = HubRingHeightAudit.PalletRoots
                    .Select(n => ring.Cast<Transform>().FirstOrDefault(t => t.name == n))
                    .Where(t => t != null).ToArray();

                var pending = new List<KeyValuePair<HubRingScaleRow, Transform>>();
                // Kept separate from `failures` on purpose: these are thrown, and the catch below
                // records the thrown message. Reusing `failures` would print each one twice.
                var planErrors = new List<string>();
                if (plan != null && plan.rows != null)
                {
                    foreach (var row in plan.rows)
                    {
                        if (row == null || string.IsNullOrEmpty(row.path)) continue;
                        var target = Resolve(ring, row.path);
                        if (target == null)
                        {
                            planErrors.Add("Factors file names '" + row.path + "' but no such transform is under the ring");
                            continue;
                        }
                        if (row.targetUniformScale <= 0.0001f)
                        {
                            planErrors.Add("Factors file gives '" + row.path + "' a non-positive targetUniformScale " +
                                         Fmt(row.targetUniformScale) + " -- refusing to collapse a building");
                            continue;
                        }
                        var current = target.localScale;
                        bool needs = Math.Abs(current.x - row.targetUniformScale) > ScaleEpsilon ||
                                     Math.Abs(current.y - row.targetUniformScale) > ScaleEpsilon ||
                                     Math.Abs(current.z - row.targetUniformScale) > ScaleEpsilon;
                        if (needs) pending.Add(new KeyValuePair<HubRingScaleRow, Transform>(row, target));
                        else evidence.Add("NOOP '" + row.path + "' already at " + Fmt(row.targetUniformScale));
                    }
                }
                // ⛔ NESTED TARGETS ARE A DOUBLE-SCALE, AND THE AUDIT PRODUCES THEM BY DESIGN.
                // It measures BOTH "ArcaneTower_MagicUpgrades" (the scale-1 root) and the baked child
                // "ArcaneTower_MagicUpgrades/arcane tower(Clone)" that actually carries the Cathedral's
                // scale, because it does not know which one the owner will want. If an unpruned plan
                // named both, the child's world height would be multiplied twice and the building would
                // end up far past 4 m -- with every marker green. A sentence in the suggestion file's
                // note cannot stop that; a refusal can.
                for (int i = 0; i < pending.Count; i++)
                    for (int j = 0; j < pending.Count; j++)
                    {
                        if (i == j) continue;
                        if (!pending[i].Value.IsChildOf(pending[j].Value)) continue;
                        planErrors.Add("Factors file names BOTH '" + pending[j].Key.path + "' and its descendant '" +
                                       pending[i].Key.path + "'. Scaling an ancestor and a descendant in one pass " +
                                       "multiplies the descendant TWICE. Prune one of the two rows and re-run.");
                    }
                if (planErrors.Count > 0) throw new InvalidOperationException(string.Join("; ", planErrors.ToArray()));

                // ⛔ NO EARLY RETURN HERE. This used to `return` straight out of the try after logging
                // the OK marker, which skipped the failure check below entirely: a "Restore failed" or
                // "Evidence write failed" appended by the finally block would have been thrown away and
                // the run would have read HUB_RING_APPLY_OK over a broken scene-manager restore. That is
                // precisely the silent failure CLAUDE.md section 12 forbids. The noop path now falls
                // through to the one place the marker is decided.
                var repoints = RepointRows(plan, evidence);
                noop = palletsPresent.Length == 0 && pending.Count == 0 && repoints.Count == 0;
                if (noop)
                {
                    evidence.Add("NOOP nothing to do: no *_Pallet child under the ring, every planned scale " +
                                 "already within " + Fmt(ScaleEpsilon) + ", and no enabled re-point. " +
                                 "Scene and recipe NOT re-saved.");
                    FlowTrace.Step(FlowSys, "apply noop");
                }

                // ---- backups + the BEFORE frame (screenshots are primary evidence) ----
                if (!noop)
                {
                string stamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff");
                Directory.CreateDirectory(OwnerCastleLayoutAudit.Output);
                File.Copy(OwnerCastleLayoutAudit.ScenePath,
                          Path.Combine(OwnerCastleLayoutAudit.Output, "before_wo1762_" + stamp + ".unity"));
                File.Copy(OwnerCastleLayoutAudit.ScenePath + ".meta",
                          Path.Combine(OwnerCastleLayoutAudit.Output, "before_wo1762_" + stamp + ".unity.meta"));
                if (File.Exists(OwnerCastleLayoutRepair.LayoutPrefab))
                    File.Copy(OwnerCastleLayoutRepair.LayoutPrefab,
                              Path.Combine(OwnerCastleLayoutAudit.Output, "before_wo1762_" + stamp + ".prefab"));
                OwnerCastleLayoutAudit.Capture(ring.gameObject, scene, "WO1762_before_context", evidence, true, true);

                // ---- step 2: the three scenery pallets ------------------------------
                var palletBounds = new List<KeyValuePair<string, Bounds>>();
                foreach (var pallet in palletsPresent)
                {
                    Bounds b;
                    if (HubRingHeightAudit.TryRendererBounds(pallet.gameObject, out b))
                        palletBounds.Add(new KeyValuePair<string, Bounds>(pallet.name, b));
                    else
                        evidence.Add("PALLET '" + pallet.name + "' rendered nothing before removal " +
                                     "(no bounds to re-check afterwards)");

                    string reason = "WO-1762 removal of scenery pallet '" + pallet.name + "'";
                    var strip = StructureVisualStrip.StripHostVisual(pallet.gameObject, reason);
                    bool husk = StructureVisualStrip.EnsureNoHusk(pallet.gameObject, reason);
                    evidence.Add("PALLET '" + pallet.name + "' hostStrip[" + strip + "] huskCleared=" + husk +
                                 " worldPos=" + pallet.position.ToString("R") +
                                 " localScale=" + pallet.localScale.ToString("R"));
                    Object.DestroyImmediate(pallet.gameObject);
                    changed = true;
                }

                var leftovers = ring.GetComponentsInChildren<Transform>(true)
                    .Where(t => t != null && t.name.EndsWith("_Pallet", StringComparison.Ordinal))
                    .Select(t => t.name).ToArray();
                if (leftovers.Length > 0)
                    throw new InvalidOperationException("Pallet transforms survived removal: " +
                                                        string.Join(", ", leftovers));

                // The real WO-1716 invariant, asserted rather than assumed: nothing invisible
                // may still block or carve where a pallet used to stand.
                foreach (var pair in palletBounds)
                {
                    var solid = scene.GetRootGameObjects()
                        .SelectMany(g => g.GetComponentsInChildren<Collider>(true))
                        .Where(c => c != null && !c.isTrigger && c.bounds.Intersects(pair.Value))
                        .Select(c => c.gameObject.name + "<Collider>").ToList();
                    solid.AddRange(scene.GetRootGameObjects()
                        .SelectMany(g => g.GetComponentsInChildren<NavMeshObstacle>(true))
                        .Where(o => o != null && o.carving &&
                                    new Bounds(o.transform.TransformPoint(o.center),
                                               Vector3.Scale(o.size, o.transform.lossyScale)).Intersects(pair.Value))
                        .Select(o => o.gameObject.name + "<NavMeshObstacle>"));
                    evidence.Add("NO_HUSK '" + pair.Key + "' footprint=" + pair.Value.ToString() +
                                 " overlapping solid/carving objects now: " +
                                 (solid.Count == 0 ? "NONE" : string.Join(", ", solid.ToArray())) +
                                 (solid.Count == 0 ? "" : " (these are OTHER objects, listed for the navmesh " +
                                                          "bake review -- a pallet-shaped one would be the husk)"));
                }

                // ---- step 3: rescale + re-seat ---------------------------------------
                foreach (var entry in pending)
                {
                    var row = entry.Key;
                    var target = entry.Value;
                    var before = target.localScale;

                    Bounds pre;
                    bool hadBounds = HubRingHeightAudit.TryRendererBounds(target.gameObject, out pre);

                    target.localScale = new Vector3(row.targetUniformScale, row.targetUniformScale, row.targetUniformScale);

                    float seat = 0f;
                    if (hadBounds)
                    {
                        Bounds post;
                        if (HubRingHeightAudit.TryRendererBounds(target.gameObject, out post))
                        {
                            seat = pre.min.y - post.min.y;
                            if (Math.Abs(seat) > 0.0001f) target.position += Vector3.up * seat;
                        }
                    }

                    Bounds final;
                    string height = HubRingHeightAudit.TryRendererBounds(target.gameObject, out final)
                        ? Fmt(final.size.y) + "m"
                        : "NO-RENDERER";

                    PrefabUtility.RecordPrefabInstancePropertyModifications(target);
                    evidence.Add("RESCALED '" + row.path + "' localScale " + before.ToString("R") + " -> " +
                                 target.localScale.ToString("R") + " reseatY=" + Fmt(seat) +
                                 " measuredHeight=" + height +
                                 " (audit measured " + Fmt(row.measuredHeightMetres) + "m before)");
                    changed = true;
                }

                // ---- step 3b (WO-1762 scope addition): OPTIONAL art re-point ----------
                // Ordered AFTER the root rescale on purpose: the fit is computed from WORLD bounds,
                // so the root's final scale must already be applied or the fitted height would be
                // multiplied by it afterwards.
                foreach (var row in repoints)
                {
                    var host = Resolve(ring, row.path);
                    if (host == null)
                    {
                        planErrors.Add("Re-point names '" + row.path + "' but no such transform is under the ring");
                        continue;
                    }
                    Repoint(host, row, plan, evidence);
                    changed = true;
                }
                if (planErrors.Count > 0) throw new InvalidOperationException(string.Join("; ", planErrors.ToArray()));

                if (!changed) throw new InvalidOperationException(
                    "Work was planned (pallets=" + palletsPresent.Length + " rescales=" + pending.Count +
                    ") but nothing changed. Refusing to save a scene over a job that did not run.");

                OwnerCastleLayoutAudit.Capture(ring.gameObject, scene, "WO1762_after_context", evidence, true, true);

                if (!EditorSceneManager.SaveScene(scene, OwnerCastleLayoutAudit.ScenePath))
                    throw new InvalidOperationException("Target scene save refused");
                saved = true;

                var recipe = PrefabUtility.SaveAsPrefabAsset(ring.gameObject, OwnerCastleLayoutRepair.LayoutPrefab);
                if (recipe == null) throw new InvalidOperationException("Owner layout prefab save failed");
                if (recipe.transform.childCount != ring.childCount)
                    throw new InvalidOperationException("Layout recipe child count mismatch: " +
                                                        recipe.transform.childCount + "/" + ring.childCount);
                evidence.Add("SAVED scene=" + OwnerCastleLayoutAudit.ScenePath +
                             " recipe=" + OwnerCastleLayoutRepair.LayoutPrefab +
                             " ringChildren=" + ring.childCount);
                evidence.Add("NAVMESH IS NOW STALE - run DeNelle.Editor.NavMeshBakeFinal.Run and judge it by " +
                             "CONTENT (surface counts, scene byte delta), never by its marker.");
                evidence.Add("ORACLES - flip OwnerCastleRuntimeProof's expectations by re-running it (they are " +
                             "derived from the recipe) and flip HubRingHeightRegression.ApplyLanded to true in " +
                             "the SAME commit as this apply.");
                }   // end if (!noop)
            }
            catch (Exception ex) { failures.Add(ex.GetBaseException().Message); }
            finally
            {
                if (opened)
                {
                    try
                    {
                        if (empty) EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                        else EditorSceneManager.RestoreSceneManagerSetup(setup);
                    }
                    catch (Exception ex) { failures.Add("Restore failed: " + ex.Message); }
                }
                try
                {
                    Directory.CreateDirectory(OwnerCastleLayoutAudit.Output);
                    File.WriteAllLines(Path.Combine(OwnerCastleLayoutAudit.Output, "WO1762_hub_ring_apply.txt"), evidence);
                }
                catch (Exception ex) { failures.Add("Evidence write failed: " + ex.Message); }
            }

            foreach (string line in evidence) Debug.Log("[HubRingHeight] " + line);

            if (failures.Count > 0)
            {
                FlowTrace.Fail(FlowSys, "apply failures=" + failures.Count + " saved=" + saved);
                Debug.LogError(MarkerFail + " saved=" + saved + " - " + string.Join("; ", failures.ToArray()) +
                               "; a BACKUP of the scene and recipe is in " + OwnerCastleLayoutAudit.Output);
                return;
            }
            FlowTrace.Step(FlowSys, "apply clean saved=" + saved + " noop=" + noop);
            Debug.Log(MarkerOk + (noop ? " noop" : "") + " saved=" + saved + " - " +
                      string.Join(" | ", evidence.ToArray()));
        }

        /// <summary>The factors file, or null. A MISSING file is a legal, logged state --
        /// the pallets still go and nothing is rescaled. A PRESENT-BUT-UNPARSEABLE file
        /// throws: silently rescaling nothing because the JSON was malformed is exactly
        /// the silent failure CLAUDE.md section 12 forbids.</summary>
        private static HubRingScaleFile LoadPlan(List<string> evidence)
        {
            if (!File.Exists(FactorsPath))
            {
                evidence.Add("PLAN none - " + FactorsPath + " is absent, so NOTHING is rescaled. " +
                             "Run DeNelle.Editor.HubRingHeightAudit.Run, review " +
                             HubRingHeightAudit.SuggestedFactorsPath + ", rename it here, then re-run.");
                return null;
            }
            HubRingScaleFile plan;
            try { plan = JsonUtility.FromJson<HubRingScaleFile>(File.ReadAllText(FactorsPath)); }
            catch (Exception ex) { throw new InvalidOperationException(FactorsPath + " unparseable: " + ex.Message); }
            if (plan == null || plan.rows == null)
                throw new InvalidOperationException(FactorsPath + " parsed to no rows -- refusing to run half the job");
            evidence.Add("PLAN " + FactorsPath + " rows=" + plan.rows.Length +
                         " targetHeight=" + Fmt(plan.targetHeightMetres) + "m generated=" + plan.generatedUtc);
            return plan;
        }

        // =====================================================================
        //  WO-1762 SCOPE ADDITION (2026-09-15) -- the owner's Quarry model.
        // ---------------------------------------------------------------------
        // She supplied Assets/StructureContent/Quarry.fbx + Quarry.fbm/ (7 textures),
        // which closes section 4 step 3's open question ("scale it, or re-point it?").
        //
        // ⛔ THIS IS NOT AN ADDRESSABLES PATH, AND THAT IS DELIBERATE. The documented
        // registration chain CANNOT reach this model while the catalog is untouched:
        // CatalogPrefabImporter.CopyKitToResources is a PACK MIRROR (its sources are
        // polyperfect / KayKit roots; an owner-sourced file already sitting at the
        // destination is a no-op, and the table's own comment at CatalogPrefabImporter
        // .cs:71-77 says owner art is deliberately absent from it), and
        // StructureAddressablesMigrator.MarkCatalogArt marks only addresses the CATALOG
        // NAMES -- MarkInto iterates ReadCatalogArtKeys(), which regex-scrapes
        // "Structures/..." out of structures-catalog.json. "Structures/Quarry" is not in
        // there, so nothing would be marked and nothing would even warn.
        // It does not need to be. The authored hub root is a DIRECT SCENE GUID REFERENCE,
        // exactly like IronMine <- Assets/StructureContent/IronMine.fbx, and a
        // scene-referenced asset ships as a scene dependency of the player build. The
        // catalog's own visualPrefabPath for collector_farm ("Structures/farm") is a
        // SEPARATE AXIS that the injector never reaches for this object
        // (HubStructureVisualInjector.cs:804-808 returns early on PreserveAuthoredVisual),
        // and WO-1762 explicitly does not change it.
        //
        // THE TWO THINGS THE PIT BREAKS, both handled by explicit per-row modes:
        //   * SEATING -- SkinOptions.Structure sets SeatOnGround = true, i.e. "the bounds
        //     base sits at the host's y" (VisualFactory.cs:52,148). The Quarry's bounds
        //     base is its PIT FLOOR, 1.27 m below its own origin, so that rule lifts the
        //     whole model and floats the plateau. seatMode "modelOrigin" is the fix.
        //   * FITTING -- a totalBounds fit of 6.35 m to 4.00 m leaves the VISIBLE building
        //     at 3.20 m while every height instrument here reads a satisfied 4.00 m,
        //     because they all measure total Renderer.bounds. fitMode "aboveGround" is the
        //     fix. This is the silent one.
        //
        // MATERIALS -- FixTripoMaterials is forced OFF. TripoMaterialFixer is the
        // SINGLE-ALBEDO path and this model carries seven textures; it would force one
        // base map over all of them. The multi-material URP conversion is a separate,
        // project-wide pass: DeNelle.Editor.MagentaMaterialFixer.Run, which sweeps ALL
        // material assets for built-in/error shaders (MagentaMaterialFixer.cs:56-66).
        // NOT PolyperfectUrpFix.Fix -- read at PolyperfectUrpFix.cs:39, its scan root is
        // the literal "Assets/polyperfect", so it would never see this model's materials.
        // =====================================================================

        /// <summary>The rows that actually get re-pointed. EMPTY unless the file-level
        /// <c>repointEnabled</c> gate is on -- and when it is off with re-point rows present, each
        /// one is LOGGED AS IGNORED rather than silently dropped (CLAUDE.md section 12).</summary>
        private static List<HubRingScaleRow> RepointRows(HubRingScaleFile plan, List<string> evidence)
        {
            var rows = new List<HubRingScaleRow>();
            if (plan == null || plan.rows == null) return rows;
            foreach (var row in plan.rows)
            {
                if (row == null || string.IsNullOrEmpty(row.repointModelPath)) continue;
                if (!plan.repointEnabled)
                {
                    evidence.Add("REPOINT IGNORED '" + row.path + "' -> '" + row.repointModelPath +
                                 "' because repointEnabled is false in " + FactorsPath +
                                 ". Set it true and re-run to apply the art change on its own.");
                    continue;
                }
                rows.Add(row);
            }
            return rows;
        }

        /// <summary>Re-skin one authored ring root with a different model, through the scripted seam
        /// (never a scene YAML edit, CLAUDE.md section 3). Follows CastleHubBuilder.SkinHostUpright's
        /// WO-1716 order exactly: strip the host's own visual, destroy the prior visual CHILDREN
        /// (keeping NPC_* interact points), skin, then close with EnsureNoHusk on both the failure
        /// and the success branch.</summary>
        private static void Repoint(Transform host, HubRingScaleRow row, HubRingScaleFile plan, List<string> evidence)
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(row.repointModelPath);
            if (model == null)
                throw new InvalidOperationException("Re-point model not found: '" + row.repointModelPath +
                                                    "'. If it was only just added, Unity has not imported it yet.");

            string reason = "WO-1762 re-point of '" + host.name + "' to " + row.repointModelPath;

            Bounds beforeBounds;
            string beforeHeight = HubRingHeightAudit.TryRendererBounds(host.gameObject, out beforeBounds)
                ? Fmt(beforeBounds.size.y) + "m" : "NO-RENDERER";

            var toDestroy = new List<GameObject>();
            for (int i = 0; i < host.childCount; i++)
            {
                var c = host.GetChild(i);
                if (c.name.StartsWith("NPC_", StringComparison.Ordinal)) continue;
                if (c.GetComponentInChildren<Renderer>(true) != null) toDestroy.Add(c.gameObject);
            }
            var strip = StructureVisualStrip.StripHostVisual(host.gameObject, reason);
            foreach (var go in toDestroy) Object.DestroyImmediate(go);

            var opts = SkinOptions.Structure(0f);
            opts.FitLargest = 0f;              // we compute the scale ourselves, below
            opts.FitHeight = 0f;               // ⛔ NOT FitHeight: it fits TOTAL bounds (see header)
            opts.SeatOnGround = false;         // ⛔ NOT SeatOnGround: it seats on the PIT FLOOR
            opts.FixTripoMaterials = false;    // ⛔ single-albedo fixer vs a 7-material model
            opts.TraceId = host.name;

            var visual = VisualFactory.Skin(host, model, opts);
            if (visual == null)
            {
                // The strip already ran, so the host is INVISIBLE from here. This is the exact
                // branch that minted the CastleBarracks ghost (WO-1716); close it before throwing.
                StructureVisualStrip.EnsureNoHusk(host.gameObject, reason + " (Skin FAILED)");
                throw new InvalidOperationException("Skin returned null for '" + host.name + "' <- " +
                                                    row.repointModelPath + "; the host's visual was " +
                                                    "stripped first, so its collision and carve were cleared");
            }

            float groundY = host.position.y;
            string fitMode = string.IsNullOrEmpty(row.fitMode) ? "totalBounds" : row.fitMode;
            string seatMode = string.IsNullOrEmpty(row.seatMode) ? "boundsMin" : row.seatMode;
            float target = row.fitTargetHeightMetres > 0.0001f
                ? row.fitTargetHeightMetres
                : (plan != null && plan.targetHeightMetres > 0.0001f
                    ? plan.targetHeightMetres
                    : HubRingHeightAudit.TargetHeightMetres);

            Bounds natural;
            if (!HubRingHeightAudit.TryRendererBounds(visual, out natural))
                throw new InvalidOperationException("Re-pointed visual for '" + host.name +
                                                    "' renders nothing - refusing to leave an invisible root");

            float scale = 1f;
            string fitDetail;
            if (string.Equals(fitMode, "none", StringComparison.OrdinalIgnoreCase))
            {
                fitDetail = "fitMode=none (native size kept)";
            }
            else if (string.Equals(fitMode, "aboveGround", StringComparison.OrdinalIgnoreCase))
            {
                float above = natural.max.y - groundY;
                if (above <= 0.0001f)
                    throw new InvalidOperationException("'" + host.name + "' has no geometry ABOVE the host's " +
                                                        "ground y (" + Fmt(groundY) + ") - an aboveGround fit " +
                                                        "would divide by zero. Check seatMode / the model's origin.");
                scale = target / above;
                fitDetail = "fitMode=aboveGround above=" + Fmt(above) + "m total=" + Fmt(natural.size.y) +
                            "m belowGround=" + Fmt(groundY - natural.min.y) + "m -> scale " + Fmt(scale);
            }
            else
            {
                if (natural.size.y <= 0.0001f)
                    throw new InvalidOperationException("'" + host.name + "' measured zero height");
                scale = target / natural.size.y;
                fitDetail = "fitMode=totalBounds total=" + Fmt(natural.size.y) + "m -> scale " + Fmt(scale);
            }
            if (scale != 1f)
                visual.transform.localScale = new Vector3(visual.transform.localScale.x * scale,
                                                          visual.transform.localScale.y * scale,
                                                          visual.transform.localScale.z * scale);

            string seatDetail;
            if (string.Equals(seatMode, "modelOrigin", StringComparison.OrdinalIgnoreCase))
            {
                seatDetail = "seatMode=modelOrigin (no offset - the model's own y=0 is its ground plane, " +
                             "so sub-ground geometry such as a pit stays below the courtyard)";
            }
            else
            {
                Bounds seated;
                if (!HubRingHeightAudit.TryRendererBounds(visual, out seated))
                    throw new InvalidOperationException("Re-pointed visual for '" + host.name + "' lost its renderers");
                float lift = groundY - seated.min.y;
                visual.transform.position += Vector3.up * lift;
                seatDetail = "seatMode=boundsMin lift=" + Fmt(lift) + "m";
            }

            // The WO-1716 closing half on the SUCCESS branch too: a Skin that returned non-null but
            // renders nothing is exactly the case nobody was watching for.
            StructureVisualStrip.EnsureNoHusk(host.gameObject, reason + " (post-skin assert)");

            Bounds after;
            string afterHeight = HubRingHeightAudit.TryRendererBounds(host.gameObject, out after)
                ? Fmt(after.size.y) + "m total, " + Fmt(after.max.y - groundY) + "m visible above y=" + Fmt(groundY)
                : "NO-RENDERER";

            evidence.Add("REPOINTED '" + row.path + "' <- " + row.repointModelPath +
                         " hostStrip[" + strip + "] destroyedVisualChildren=" + toDestroy.Count +
                         " before=" + beforeHeight + " " + fitDetail + " " + seatDetail +
                         " after=" + afterHeight +
                         " | MATERIALS: FixTripoMaterials was forced OFF (7-material model). Run " +
                         "DeNelle.Editor.MagentaMaterialFixer.Run for the URP conversion - NOT " +
                         "PolyperfectUrpFix.Fix, whose scan root is Assets/polyperfect only.");
        }

        /// <summary>A direct child by name, or "Parent/Child" for a baked grandchild such as
        /// "ArcaneTower_MagicUpgrades/arcane tower(Clone)".</summary>
        private static Transform Resolve(Transform ring, string path)
        {
            int slash = path.IndexOf('/');
            if (slash < 0) return ring.Cast<Transform>().FirstOrDefault(t => t.name == path);
            string head = path.Substring(0, slash);
            string tail = path.Substring(slash + 1);
            var parent = ring.Cast<Transform>().FirstOrDefault(t => t.name == head);
            return parent == null ? null : Resolve(parent, tail);
        }

        private static string Fmt(float v) => v.ToString("0.0000", CultureInfo.InvariantCulture);
    }
}

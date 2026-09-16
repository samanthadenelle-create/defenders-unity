// =============================================================================
// HubRingHeightAudit (WO-1762 step 1) -- THE MEASUREMENT, taken BEFORE any edit.
// -----------------------------------------------------------------------------
// Assembly: DeNelle.Editor.  Namespace: DeNelle.Editor.
// Batchmode: run-unity-method.ps1 -Method DeNelle.Editor.HubRingHeightAudit.Run
// Markers: HUB_RING_HEIGHT_AUDIT_OK / HUB_RING_HEIGHT_AUDIT_FAIL.
//
// WHY THIS FILE EXISTS (CLAUDE.md section 12 -- instrument first, never guess).
// WO-1762 asks for four authored hub roots to be brought up to the 4.00 m family
// height. Every height in that work order's section 1 is DERIVED -- back-computed
// from a device log's fit scale against an UNVERIFIED FBX root import scale -- and
// the WO says so in its own words: "these are NOT measurements". One of the four
// (the Quarry) could not be derived at all, because its scene source prefab GUID
// did not resolve. Rescaling from a derived number is the inference-fix CLAUDE.md
// section 12 forbids. This tool replaces all of it with a reading.
//
// WHAT IT MEASURES, per direct child of the authored ring
// (OwnerCastleLayoutAudit.RingName under Main_Castle_Overworld):
//   * the authored localScale, exactly as the scene carries it;
//   * rendererHeight -- the encapsulated Renderer.bounds of the whole subtree, in
//     WORLD metres. This is the number the player sees and the one the target is
//     judged against;
//   * meshHeight -- the same extent recomputed from MeshFilter.sharedMesh.bounds
//     and SkinnedMeshRenderer.sharedMesh.bounds corners, transformed into the
//     ring's space. It is printed NOT as a second opinion but as a CALIBRATION:
//     HubRingHeightRegression (the gate that will pin these heights) cannot open a
//     scene, so it measures the recipe PREFAB by exactly this mesh arithmetic. If
//     the two columns disagree here, that oracle's technique is wrong and the
//     divergence must be understood before its constant is flipped on;
//   * pivotOffsetY = bounds.min.y - transform.position.y -- how far the visual sits
//     BELOW its own pivot. A uniform scale multiplies about the pivot, so a root
//     with a non-zero offset SINKS or FLOATS when rescaled. HubRingHeightApply
//     re-seats from this; the column is here so the re-seat is auditable;
//   * factor = 4.00 / rendererHeight, and targetUniformScale = authored * factor.
//
// It also walks the baked child "arcane tower(Clone)" under the scale-1
// ArcaneTower_MagicUpgrades root, because THAT child is where the Cathedral's
// scale actually lives (WO-1762 section 1 table).
//
// THE CRYSTAL MINE REFERENCE is printed BY CONSTRUCTION -- YHeightVariable *
// repo.heightMul read out of the canonical catalog -- and is labelled UNPROVEN,
// because no prefab was instantiated to measure it. That is deliberate under
// CLAUDE.md section 11B: an unproven thing NAMED as unproven is useful; the same
// number stated as a measurement would be a lie. StructureHeightAudit already owns
// the catalog-prefab measuring seam if the lead wants the measured version.
//
// READ-ONLY, AND IT PROVES IT. The scene is opened, never saved; the scene manager
// setup is restored; and the scene file's SHA-256 is compared before and after --
// a changed digest is a FAIL, not a warning.
//
// OUTPUT. Besides the log table it writes
// Builds/hub-ring-scale-factors.suggested.json in EXACTLY the shape
// HubRingHeightApply.Run reads (HubRingScaleFile below, one type for both
// directions). The lead reviews it, renames it to
// Builds/hub-ring-scale-factors.json, and the apply consumes it. Renaming a file
// cannot mistype a float; transcribing six of them can.
// =============================================================================

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using DeNelle.Core.Diagnostics;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DeNelle.Editor
{
    /// <summary>One authored ring root's target scale. Written by
    /// <see cref="HubRingHeightAudit"/>, read by <see cref="HubRingHeightApply"/>.
    /// <para>The row carries an ABSOLUTE <see cref="targetUniformScale"/>, never a
    /// multiplier. A multiplier is not idempotent: a second apply would scale an
    /// already-correct root a second time. An absolute target makes the second run a
    /// provable no-op, which is the acceptance criterion.</para></summary>
    [Serializable]
    public sealed class HubRingScaleRow
    {
        /// <summary>Direct child of the ring, or "Parent/Child" for a baked grandchild
        /// such as "ArcaneTower_MagicUpgrades/arcane tower(Clone)".</summary>
        public string path;
        /// <summary>Informational only -- the marker's canonicalId, or empty.</summary>
        public string canonicalId;
        /// <summary>THE INPUT. localScale is set to (v,v,v) when it differs by more
        /// than <see cref="HubRingHeightApply.ScaleEpsilon"/>.</summary>
        public float targetUniformScale;
        /// <summary>Informational: the authored uniform scale when measured.</summary>
        public float authoredUniformScale;
        /// <summary>Informational: encapsulated Renderer.bounds height, world metres.</summary>
        public float measuredHeightMetres;
        /// <summary>Informational: targetHeight / measuredHeight at audit time.</summary>
        public float factor;

        // ---- WO-1762 scope addition (owner supplied the Quarry model 2026-09-15) ----
        // All three are OPT-IN and all three default to the pre-addition behaviour, so an
        // existing row's meaning is unchanged. They are per-row and not global because a
        // default that is right for ten roots and wrong for one is how the silent case ships.

        /// <summary>Project-relative model asset to re-skin this root with, e.g.
        /// "Assets/StructureContent/Quarry.fbx". EMPTY = no re-point (the default).
        /// ⛔ Honoured ONLY when <see cref="HubRingScaleFile.repointEnabled"/> is true, so the
        /// lead can run scale-only first and the re-point second.</summary>
        public string repointModelPath;

        /// <summary>How the re-pointed visual meets the ground.
        /// <list type="bullet">
        /// <item><c>"boundsMin"</c> (DEFAULT, and what every pre-existing root does): shift so the
        /// visual's bounds BASE sits at the host's y. This is <c>SkinOptions.SeatOnGround</c>'s rule
        /// (VisualFactory.cs:52) and the hand-rolled one at OwnerCastleLayoutRepair.cs:175.</item>
        /// <item><c>"modelOrigin"</c>: no offset at all - the model's own y=0 IS its ground plane.
        /// ⛔ THIS IS THE ONE THE QUARRY NEEDS. Its pit reaches 1.27 m BELOW y=0, so the bounds base
        /// is the PIT FLOOR: seating on bounds-min would lift the whole model 1.27 m and float the
        /// courtyard-level plateau above the ground it is supposed to be cut into.</item>
        /// </list></summary>
        public string seatMode;

        /// <summary>How the re-pointed visual is scaled to the family height.
        /// <list type="bullet">
        /// <item><c>"totalBounds"</c> (DEFAULT): total world-bounds height = the target. This is what
        /// <c>SkinOptions.FitHeight</c> does.</item>
        /// <item><c>"aboveGround"</c>: only the part ABOVE the host's y is fitted to the target.
        /// ⛔ THE QUARRY NEEDS THIS TOO, AND GETTING IT WRONG IS SILENT. Its bounds are 6.35 m tall
        /// with 1.27 m below ground, so a totalBounds fit to 4.00 m leaves the VISIBLE building at
        /// 4.00 x (5.08/6.35) = 3.20 m - 20% under the family height - while this file's audit and
        /// the hub-ring-height oracle (both of which measure total Renderer.bounds) read a satisfied
        /// 4.00 m and go GREEN.</item>
        /// <item><c>"none"</c>: skin at the model's native size, scale nothing.</item>
        /// </list></summary>
        public string fitMode;

        /// <summary>Target for <see cref="fitMode"/>. 0 = use
        /// <see cref="HubRingScaleFile.targetHeightMetres"/>.</summary>
        public float fitTargetHeightMetres;
    }

    /// <summary>The file at Builds/hub-ring-scale-factors.json. Absent = the apply
    /// rescales nothing (and says so) -- it still removes the pallets.</summary>
    [Serializable]
    public sealed class HubRingScaleFile
    {
        public string note;
        public string generatedUtc;
        public float targetHeightMetres;

        /// <summary>⛔ THE MASTER GATE for every row's <see cref="HubRingScaleRow.repointModelPath"/>.
        /// FALSE (the default, and what the audit writes) means every re-point is IGNORED AND LOGGED
        /// AS IGNORED, and the apply behaves exactly as WO-1762 Phase 1 shipped it. The lead turns it
        /// on for a second, separate run, so a scale problem and an art problem can never be tangled
        /// in one diff.</summary>
        public bool repointEnabled;

        public HubRingScaleRow[] rows;
    }

    public static class HubRingHeightAudit
    {
        private const string FlowSys = "HubRingHeight";
        private const string MarkerOk = "HUB_RING_HEIGHT_AUDIT_OK";
        private const string MarkerFail = "HUB_RING_HEIGHT_AUDIT_FAIL";

        /// <summary>The family height WO-1762 sizes to: the owner's surveyed Crystal
        /// Mine reading, which is <c>StructureFactory.YHeightVariable</c> at
        /// heightMul 1. Named here rather than re-typed as a bare 4f.</summary>
        public const float TargetHeightMetres = 4.00f;

        /// <summary>The Cathedral's scale lives on this baked child, not on its
        /// scale-1 parent root (WO-1762 section 1).</summary>
        public const string CathedralRoot = "ArcaneTower_MagicUpgrades";
        public const string CathedralVisualChild = "arcane tower(Clone)";

        /// <summary>The three scenery roots the owner asked to remove. Named here so
        /// the audit reports them as REMOVAL CANDIDATES rather than rescale targets.</summary>
        public static readonly string[] PalletRoots = { "Wood_Pallet", "Iron_Pallet", "Stone_Pallet" };

        public const string SuggestedFactorsPath = "Builds/hub-ring-scale-factors.suggested.json";

        private const string CatalogPath = "Assets/StreamingAssets/Data/Canonical/structures-catalog.json";
        private const string CrystalMineId = "crystal-mine";

        [MenuItem("Defenders/Art/Audit hub ring heights (WO-1762)")]
        public static void Menu() => Run();

        /// <summary>Batchmode entry. Opens the hub scene, measures, restores, writes the
        /// suggested factors file. NEVER saves the scene.</summary>
        public static void Run()
        {
            var setup = EditorSceneManager.GetSceneManagerSetup();
            var report = new List<string>();
            var failures = new List<string>();
            var rows = new List<HubRingScaleRow>();
            bool opened = false, empty = false;
            string sceneDigestBefore = null;

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

                sceneDigestBefore = Digest(OwnerCastleLayoutAudit.ScenePath);
                opened = true;
                var scene = EditorSceneManager.OpenScene(OwnerCastleLayoutAudit.ScenePath, OpenSceneMode.Single);
                var ring = scene.GetRootGameObjects()
                    .SelectMany(g => g.GetComponentsInChildren<Transform>(true))
                    .Single(t => t.name == OwnerCastleLayoutAudit.RingName);

                report.Add("HUB RING HEIGHT AUDIT (WO-1762) scene=" + OwnerCastleLayoutAudit.ScenePath +
                           " ring=" + OwnerCastleLayoutAudit.RingName + " children=" + ring.childCount +
                           " target=" + TargetHeightMetres.ToString("0.00", CultureInfo.InvariantCulture) + "m");
                report.Add("ringWorldScale=" + ring.lossyScale.ToString("R") +
                           " (mesh arithmetic below is expressed in RING space; a non-unit ring scale " +
                           "means HubRingHeightRegression's prefab-space numbers will not equal these)");
                report.Add("columns: path | canonicalId | authoredLocalScale | rendererH | meshH | " +
                           "footprintXZ | pivotOffsetY | factor(4.00/rendererH) | targetUniformScale");

                var subjects = new List<Transform>();
                foreach (Transform child in ring) subjects.Add(child);

                Transform cathedralVisual = null;
                var cathedralRoot = ring.Cast<Transform>().FirstOrDefault(t => t.name == CathedralRoot);
                if (cathedralRoot != null)
                {
                    cathedralVisual = cathedralRoot.Cast<Transform>().FirstOrDefault(t => t.name == CathedralVisualChild);
                    if (cathedralVisual != null) subjects.Add(cathedralVisual);
                    else
                        report.Add("NOTE the baked child '" + CathedralVisualChild + "' was NOT found under '" +
                                   CathedralRoot + "'. WO-1762 section 1 says the Cathedral's scale lives there; " +
                                   "if the art was re-pointed since, the apply row must name whatever now carries it.");
                }
                else
                {
                    report.Add("NOTE the root '" + CathedralRoot + "' is not a direct child of the ring.");
                }

                foreach (var subject in subjects)
                {
                    string label = subject == cathedralVisual ? CathedralRoot + "/" + CathedralVisualChild : subject.name;
                    Guard.Try(FlowSys, "measure " + label, () =>
                    {
                        var marker = subject.GetComponent<DeNelle.Village.AuthoredCastleStorefront>();
                        string id = marker == null || string.IsNullOrEmpty(marker.CanonicalId) ? "-" : marker.CanonicalId;

                        Bounds renderBounds;
                        bool hasRender = TryRendererBounds(subject.gameObject, out renderBounds);
                        Bounds meshBounds;
                        bool hasMesh = TryMeshBounds(subject, ring, out meshBounds);

                        if (!hasRender)
                        {
                            // A zero-renderer root is NOT an exception here. CastleBarracks stands
                            // down at runtime (HubStructureVisualInjector.cs:261-267) and WO-1716
                            // records a husk history on this exact ring, so "renders nothing" is a
                            // real, expected state. It is reported and skipped, never divided by.
                            report.Add(label + " | " + id + " | " + subject.localScale.ToString("R") +
                                       " | NO-RENDERER | " + (hasMesh ? Fmt(meshBounds.size.y) : "NO-MESH") +
                                       " | - | - | - | - " +
                                       "  <== renders nothing; no height to measure, NOT a rescale candidate");
                            return;
                        }

                        float rendererH = renderBounds.size.y;
                        float meshH = hasMesh ? meshBounds.size.y : 0f;
                        float pivotOffsetY = renderBounds.min.y - subject.position.y;
                        bool uniform = Approximately(subject.localScale.x, subject.localScale.y) &&
                                       Approximately(subject.localScale.y, subject.localScale.z);
                        float authored = subject.localScale.y;
                        float factor = rendererH > 0.0001f ? TargetHeightMetres / rendererH : 0f;
                        float target = authored * factor;

                        bool isPallet = Array.IndexOf(PalletRoots, subject.name) >= 0;

                        report.Add(label + " | " + id + " | " + subject.localScale.ToString("R") +
                                   (uniform ? "" : " NON-UNIFORM") +
                                   " | " + Fmt(rendererH) +
                                   " | " + (hasMesh ? Fmt(meshH) : "NO-MESH") +
                                   " | " + Fmt(renderBounds.size.x) + "x" + Fmt(renderBounds.size.z) +
                                   " | " + Fmt(pivotOffsetY) +
                                   " | " + Fmt(factor) +
                                   " | " + Fmt(target) +
                                   (isPallet ? "  <== REMOVAL CANDIDATE (owner 2026-09-15), not a rescale target" : "") +
                                   (hasMesh && Math.Abs(meshH - rendererH) > 0.05f
                                       ? "  <== mesh/renderer DIVERGE by " + Fmt(Math.Abs(meshH - rendererH)) +
                                         "m; HubRingHeightRegression measures the mesh way and would read differently"
                                       : ""));

                        if (isPallet) return;
                        if (!uniform)
                            report.Add("NOTE '" + label + "' is NON-UNIFORMLY scaled. WO-1762 asks for a uniform " +
                                       "rescale; applying one DISCARDS its authored non-uniformity. Owner call.");

                        rows.Add(new HubRingScaleRow
                        {
                            path = label,
                            canonicalId = id,
                            targetUniformScale = target,
                            authoredUniformScale = authored,
                            measuredHeightMetres = rendererH,
                            factor = factor
                        });
                    });
                }

                report.Add(CrystalMineReference());

                // Read-only proof: the scene bytes must be identical afterwards.
                if (Digest(OwnerCastleLayoutAudit.ScenePath) != sceneDigestBefore)
                    failures.Add("Scene bytes changed during a READ-ONLY audit: " + OwnerCastleLayoutAudit.ScenePath);
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
                if (sceneDigestBefore != null && File.Exists(OwnerCastleLayoutAudit.ScenePath) &&
                    Digest(OwnerCastleLayoutAudit.ScenePath) != sceneDigestBefore)
                    failures.Add("Scene bytes changed after restore: " + OwnerCastleLayoutAudit.ScenePath);
            }

            try
            {
                Directory.CreateDirectory(OwnerCastleLayoutAudit.Output);
                File.WriteAllLines(Path.Combine(OwnerCastleLayoutAudit.Output, "WO1762_hub_ring_height_audit.txt"), report);

                Directory.CreateDirectory(Path.GetDirectoryName(SuggestedFactorsPath));
                var file = new HubRingScaleFile
                {
                    note = "WO-1762 SUGGESTION, not an instruction. Generated by " +
                           "DeNelle.Editor.HubRingHeightAudit.Run from measured Renderer.bounds. " +
                           "Review, prune to the roots the owner actually asked for (Lumbermill, IronMine, " +
                           "Quarry, Cathedral), then rename to Builds/hub-ring-scale-factors.json for " +
                           "DeNelle.Editor.HubRingHeightApply.Run. targetUniformScale is ABSOLUTE.",
                    generatedUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
                    targetHeightMetres = TargetHeightMetres,
                    rows = rows.ToArray()
                };
                File.WriteAllText(SuggestedFactorsPath, JsonUtility.ToJson(file, true), new UTF8Encoding(false));
                report.Add("WROTE " + SuggestedFactorsPath + " rows=" + rows.Count);
            }
            catch (Exception ex) { failures.Add("Report write failed: " + ex.Message); }

            foreach (string line in report) Debug.Log("[HubRingHeight] " + line);

            if (failures.Count > 0)
            {
                FlowTrace.Fail(FlowSys, "audit failures=" + failures.Count);
                Debug.LogError(MarkerFail + " - " + string.Join("; ", failures.ToArray()));
                return;
            }
            FlowTrace.Step(FlowSys, "audit clean rows=" + rows.Count);
            Debug.Log(MarkerOk + " - measured " + rows.Count + " rescale candidate(s) against " +
                      TargetHeightMetres.ToString("0.00", CultureInfo.InvariantCulture) +
                      "m; suggestions at " + SuggestedFactorsPath +
                      "; scene opened READ-ONLY and its bytes are unchanged.");
        }

        /// <summary>Encapsulated world-space Renderer.bounds over the whole subtree.
        /// FALSE when nothing renders -- which is a real state on this ring, not an error.</summary>
        public static bool TryRendererBounds(GameObject host, out Bounds bounds)
        {
            bounds = default;
            if (host == null) return false;
            bool any = false;
            foreach (var r in host.GetComponentsInChildren<Renderer>(true))
            {
                if (r == null) continue;
                if (!any) { bounds = r.bounds; any = true; }
                else bounds.Encapsulate(r.bounds);
            }
            return any;
        }

        /// <summary>The same extent rebuilt from shared-mesh bounds corners, expressed in
        /// <paramref name="space"/>'s local frame. This is the arithmetic
        /// HubRingHeightRegression uses on the recipe PREFAB, where Renderer.bounds is not
        /// available -- printed here so the two can be compared on the same objects.</summary>
        public static bool TryMeshBounds(Transform subject, Transform space, out Bounds bounds)
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

        /// <summary>The Crystal Mine reference, BY CONSTRUCTION and labelled as such.
        /// No prefab is instantiated, so this is not a measurement and does not pretend
        /// to be one (CLAUDE.md section 11B).</summary>
        private static string CrystalMineReference()
        {
            try
            {
                if (!File.Exists(CatalogPath))
                    return "CRYSTAL_MINE_REFERENCE UNPROVEN - catalog not found at " + CatalogPath;
                var root = JObject.Parse(File.ReadAllText(CatalogPath));
                var entries = root["entries"] as JArray;
                if (entries == null)
                    return "CRYSTAL_MINE_REFERENCE UNPROVEN - catalog has no entries array";
                foreach (var entry in entries)
                {
                    if ((string)entry["id"] != CrystalMineId) continue;
                    var repo = entry["repo"];
                    float mul = repo != null && repo["heightMul"] != null ? (float)repo["heightMul"] : 1f;
                    return "CRYSTAL_MINE_REFERENCE BY CONSTRUCTION (NOT MEASURED, see CLAUDE.md 11B): " +
                           "StructureFactory.YHeightVariable(" +
                           DeNelle.Village.StructureFactory.YHeightVariable.ToString("0.00", CultureInfo.InvariantCulture) +
                           "m) * repo.heightMul(" + mul.ToString("0.###", CultureInfo.InvariantCulture) + ") = " +
                           (DeNelle.Village.StructureFactory.YHeightVariable * mul).ToString("0.00", CultureInfo.InvariantCulture) +
                           "m. No prefab was instantiated headless, so the DELIVERED height of the " +
                           "catalog-built Crystal Mine is UNPROVEN here; DeNelle.Editor.StructureHeightAudit" +
                           ".AuditBatch already owns the catalog-prefab measuring seam if the lead wants it.";
                }
                return "CRYSTAL_MINE_REFERENCE UNPROVEN - no entry with id " + CrystalMineId;
            }
            catch (Exception ex) { return "CRYSTAL_MINE_REFERENCE UNPROVEN - " + ex.Message; }
        }

        private static bool Approximately(float a, float b) => Math.Abs(a - b) < 0.0001f;
        private static string Fmt(float v) => v.ToString("0.0000", CultureInfo.InvariantCulture);

        private static string Digest(string path)
        {
            if (!File.Exists(path)) return "<missing>";
            using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path)));
        }
    }
}

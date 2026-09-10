// =============================================================================
// ArenaBoundaryRing - the ONE landscape boundary/cover vocabulary shared by every
// arena venue builder (WO-1632).
// -----------------------------------------------------------------------------
// Assembly: DeNelle.Editor. DeNelle.EditorWallTools references DeNelle.Editor, so
// RaidBaseGenerator can call this WITHOUT an asmdef change (verified against
// Assets/Editor/WallTools/DeNelle.EditorWallTools.asmdef:4-9, 2026-09-10).
//
// WHY THIS FILE EXISTS. Owner directive 2026-09-10, verbatim:
//   "exterior walls around entire arena"
//   "similar strategy as we used in battle arena"
// The battle arena already frames its venue with a boundary ring of real landscape
// pieces - ProceduralSiegeArenaBuilder's `OuterBoundary_Ring` (BoundaryRadius = 72 m,
// 40 large polyperfect rocks, colliders ON "so it reads as a wall") and
// ArenaPrefabBuilder's `EdgeProps` (the same ring vocabulary, colliders STRIPPED
// because that one is pure silhouette). The raid bases had no such ring at all.
// Rather than copy that placement code into RaidBaseGenerator - the duplicated-state
// failure CLAUDE.md sec.2 / sec.5 / sec.16 each describe - the placement math, the
// prefab palette and the graceful-miss instantiate move HERE, and
// ProceduralSiegeArenaBuilder now calls in. Its RNG draw order is preserved exactly
// (jitter-x, jitter-z, prefab index, yaw, scale), so SiegeArena.unity's layout is
// unchanged and needs no re-bake.
//
// TWO RING SHAPES, and the difference is load-bearing:
//   * PlacePolarRing    - a CIRCLE of radius r. What the siege venue uses; its plate
//                         is a disc-shaped venue on a square plate.
//   * PlaceSquarePerimeter - a SQUARE ring at +/-halfExtent. What a RAID base needs,
//                         because the raid ground plane is a 140 m SQUARE and the
//                         staging marker's diagonal fallback deliberately uses the
//                         CORNERS of that square: on the 2026-09-10 bake,
//                         'fortified_garrison' staged at (-55.89, 0, -55.89) = 79.0 m
//                         from centre. NO circle that fits inside the plane (r <= 70)
//                         can contain that point. A circular boundary would have
//                         fenced the player's own deploy pocket OUT of the arena.
//
// Packs: polyperfect Low Poly Ultimate Pack _M tier. GITIGNORED (CLAUDE.md sec.4) -
// every load is LogWarning + a collidered primitive fallback, never an error, never a
// throw. All seven prefab paths below were listed on disk 2026-09-10 in
// <repo>/Assets/polyperfect/Low Poly Ultimate Pack/_M/Prefabs_M/Nature_M/.
// =============================================================================

using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using DeNelle.Core.Diagnostics;

namespace DeNelle.Editor
{
    public static class ArenaBoundaryRing
    {
        /// <summary>polyperfect Nature_M root. Gitignored pack - callers must tolerate a miss.</summary>
        public const string NatureRoot =
            "Assets/polyperfect/Low Poly Ultimate Pack/_M/Prefabs_M/Nature_M/";

        /// <summary>Trees / rocks that read as natural cover and keep their colliders.</summary>
        public static readonly string[] TreePaths =
        {
            "Trees_M/Tree_Oak.prefab",
            "Trees_M/Tree_Conifer.prefab",
            "Trees_M/Tree_Beech.prefab",
            "Trees_M/Trees_Dead_M/Tree_Dead_Broken.prefab",
        };

        /// <summary>The boundary palette - "a low wall of large rocks" (the siege venue's own words).</summary>
        public static readonly string[] RockPaths =
        {
            "Stones_M/Stone_Large.prefab",
            "Stones_M/Rock_Pillar.prefab",
            "Stones_M/Stone_Medium_Flat.prefab",
        };

        /// <summary>Widest a fallback primitive is - the floor used when the pack is absent.</summary>
        private const float FallbackFootprint = 1.2f;

        // =====================================================================
        //  What PlaceSquarePerimeter resolved, for the caller's build log.
        // =====================================================================
        public struct BoundaryReport
        {
            /// <summary>Total pieces instantiated across all four sides.</summary>
            public int Placed;
            /// <summary>Pieces on one side (corners counted once).</summary>
            public int PerSide;
            /// <summary>Centre-to-centre spacing actually used, in metres.</summary>
            public float Stride;
            /// <summary>Smallest footprint a placed piece can have = measured min XZ * scaleMin.</summary>
            public float PieceFootprint;
            /// <summary>
            /// LARGEST footprint a placed piece can have = measured max XZ * scaleMax. This is the
            /// one a containment check must use: half of it is how far the ring reaches INWARD from
            /// its line. Deriving that from the scale alone silently assumes a 1 m mesh.
            /// </summary>
            public float MaxPieceFootprint;
            /// <summary>Stride - PieceFootprint. NEGATIVE = the pieces overlap (a closed ring).</summary>
            public float WorstGap;
            /// <summary>True when the per-side clamp forced a wider stride than the overlap target.</summary>
            public bool Clamped;
            /// <summary>Half of <see cref="MaxPieceFootprint"/> - how far the ring reaches in from its line.</summary>
            public float InwardReach;
            /// <summary>Scale actually applied after the band fit (see BAND FIT below). &lt;= the requested scale.</summary>
            public float AppliedScaleMin;
            public float AppliedScaleMax;
            /// <summary>True when the band fit had to shrink the requested scale to make the ring fit.</summary>
            public bool ScaleFitted;
            /// <summary>Symmetric radial scatter actually used, after the band bound.</summary>
            public float RadialJitter;
        }

        // =====================================================================
        //  POLAR RING - the siege venue's original PlaceCoverRing, moved here verbatim.
        //  Drop `count` pieces evenly around a circle of `radius`, each nudged by up to
        //  `jitter` metres so the ring does not look mechanical. RNG draw order is
        //  FROZEN (jx, jz, prefab index, yaw, scale) - SiegeArena.unity's saved layout
        //  is reproduced exactly by the same seed, so moving this code re-bakes nothing.
        // =====================================================================
        public static void PlacePolarRing(
            Transform parent, System.Random rng, float radius, int count, float jitter,
            string[] prefabRelPaths, string label, ref int placedCounter,
            float scaleMin, float scaleMax, string logTag, string flowSys = null)
        {
            if (parent == null || rng == null || prefabRelPaths == null || prefabRelPaths.Length == 0) return;

            // WO-1637 step 1: one MAT line per DISTINCT prefab in the palette, not per piece.
            var traced = new HashSet<string>();

            for (int i = 0; i < count; i++)
            {
                float ang = (i / (float)count) * Mathf.PI * 2f;
                float jx = (float)(rng.NextDouble() * 2.0 - 1.0) * jitter;
                float jz = (float)(rng.NextDouble() * 2.0 - 1.0) * jitter;
                var pos = new Vector3(Mathf.Cos(ang) * radius + jx, 0f, Mathf.Sin(ang) * radius + jz);

                string rel = prefabRelPaths[rng.Next(prefabRelPaths.Length)];
                var go = InstantiatePiece(rel, parent, logTag);
                go.transform.localPosition = pos;
                go.transform.localRotation = Quaternion.Euler(0f, (float)(rng.NextDouble() * 360.0), 0f);
                float s = Mathf.Lerp(scaleMin, scaleMax, (float)rng.NextDouble());
                go.transform.localScale *= s;
                go.name = label + "_" + i;
                if (traced.Add(rel)) TraceMaterials(flowSys, label + " (polar)", NatureRoot + rel, go);
                placedCounter++;
            }
        }

        // =====================================================================
        //  SQUARE PERIMETER - the raid arena's exterior boundary.
        //
        //  The stride is DERIVED, never authored: measure the smallest footprint the
        //  palette can produce, shrink it by `overlapFactor`, and step by that. So the
        //  ring is continuous by construction and the builder can PROVE it - the report
        //  carries WorstGap, which is negative whenever the pieces overlap. A magic
        //  "40 pieces" would have been a guess about art nobody measured (CLAUDE.md
        //  sec.11B) and would silently open metre-wide holes if the palette changed.
        //
        //  `maxPerSide` bounds the cost: a 137 m run at a 1 m stride is 550 pieces a
        //  side. When the clamp binds, the stride widens, WorstGap goes positive and the
        //  caller is told - a finding, not a softened number.
        //
        //  BAND FIT (WO-1632 step 2, forged by the 2026-09-10 bake). The ring line sits in
        //  the middle of a band `bandHalf` metres wide - between the furthest a staging
        //  marker can reach and the furthest a piece may hang past the plane edge. A piece
        //  reaches MaxPieceFootprint/2 inward from that line, so the WHOLE ring must fit
        //  inside the band or it fences the player's own deploy pocket out. That is not a
        //  thing to assume about art: the first bake measured `Stone_Large` et al at 3.38 m
        //  across, so the requested 3.4x scale produced an 11.5 m piece reaching 5.75 m
        //  inward - through a 4 m band. So the REQUESTED scale is a CEILING, and the applied
        //  scale is shrunk until the widest piece fits `bandFill` of the band. The art is
        //  fitted to the geometry; the geometry is never softened to fit the art.
        //
        //  Radial jitter is SYMMETRIC and bounded by whatever band slack the fitted piece
        //  leaves, so scatter can push a piece neither inside the staging clamp nor past the
        //  edge tolerance. (It was outward-only at 1.2 m before, which on its own would have
        //  blown the outward half of the band.)
        //  Tangential jitter is bounded by the surplus overlap, so jitter can never
        //  open a gap the stride closed.
        //
        //  Sides are indexed by the same 90-degrees-around-origin rotation the wall
        //  rings use (0 = S, 1 = E, 2 = N, 3 = W), so the names line up with
        //  RaidBaseGenerator's SideName table.
        // =====================================================================
        public static BoundaryReport PlaceSquarePerimeter(
            Transform parent, System.Random rng, float halfExtent, float bandHalf, float bandFill,
            float containmentSlack, float containmentHeadroom, float overlapFactor, float radialJitterCap,
            string[] prefabRelPaths, string label,
            float scaleMin, float scaleMax, int maxPerSide, string logTag, string flowSys = null)
        {
            var report = new BoundaryReport();
            if (parent == null || rng == null || prefabRelPaths == null || prefabRelPaths.Length == 0)
                return report;

            MeasureFootprints(prefabRelPaths, logTag, out float measured, out float measuredMax);

            // -- BAND FIT. Shrink the requested scale until the widest piece fits the band.
            float allowedFootprint = Mathf.Max(0.25f, 2f * Mathf.Max(0.01f, bandHalf) * Mathf.Clamp01(bandFill));
            float fitCeiling = measuredMax > 0.01f ? allowedFootprint / measuredMax : scaleMax;
            float appliedScaleMax = Mathf.Min(Mathf.Max(0.01f, scaleMax), fitCeiling);
            float appliedScaleMin = Mathf.Min(Mathf.Max(0.01f, scaleMin), appliedScaleMax);
            bool scaleFitted = appliedScaleMax < Mathf.Max(0.01f, scaleMax) - 0.0001f;
            if (scaleFitted)
                Debug.Log(logTag + " boundary BAND FIT: measured widest piece " +
                          measuredMax.ToString("F2") + "m at scale 1; requested scale " +
                          scaleMax.ToString("F2") + " would reach " +
                          (measuredMax * scaleMax * 0.5f).ToString("F2") + "m inward through a " +
                          (bandHalf * 2f).ToString("F2") + "m band, so the applied scale is " +
                          appliedScaleMax.ToString("F2") + " (fill " + bandFill.ToString("F2") + ").");

            float pieceFootprint = Mathf.Max(0.25f, measured * appliedScaleMin);
            float maxPieceFootprint = Mathf.Max(pieceFootprint, measuredMax * appliedScaleMax);
            float wantedStride = Mathf.Max(0.25f, pieceFootprint * Mathf.Clamp(overlapFactor, 0.05f, 1f));

            float side = halfExtent * 2f;
            int perSide = Mathf.CeilToInt(side / wantedStride) + 1;   // inclusive of both corners
            bool clamped = false;
            if (maxPerSide > 2 && perSide > maxPerSide) { perSide = maxPerSide; clamped = true; }
            if (perSide < 2) perSide = 2;

            float stride = side / (perSide - 1);
            float worstGap = stride - pieceFootprint;

            // Tangential jitter can only consume surplus overlap - never open a gap.
            float tangentJitter = Mathf.Max(0f, -worstGap) * 0.5f;

            // Radial jitter is SYMMETRIC and can only consume the band room the fitted piece
            // leaves AFTER the containment margin is reserved. An outward-only jitter would have
            // spent the whole outward half-band; a jitter bounded by the BARE band leaves the
            // faces exactly ON the band edges, which is what fired the assert a second time
            // (reach 2.21 + jitter 0.39 == the 2.60 half-band, inner face == the 66.0 clamp).
            // Reserving `containmentSlack` here is what makes the assert's margin real - and
            // reserving `containmentHeadroom` ON TOP is what stops the DESIGNED clearance from
            // landing exactly ON the required one, which is how bake 3 failed a strict compare
            // by 1e-7 while building the ring perfectly to spec.
            float jitterRoom = bandHalf - Mathf.Max(0f, containmentSlack)
                                        - Mathf.Max(0f, containmentHeadroom)
                                        - maxPieceFootprint * 0.5f;
            float radialJitter = Mathf.Min(Mathf.Max(0f, radialJitterCap), Mathf.Max(0f, jitterRoom));

            int placed = 0;
            // WO-1637 step 1: one MAT line per DISTINCT prefab in the palette, not per piece.
            // The ring is 300+ objects drawn from three prefabs; three lines answer the ticket
            // and 300 would evict the rest of the bake log.
            var traced = new HashSet<string>();
            var sideNames = new[] { "S", "E", "N", "W" };
            for (int s = 0; s < 4; s++)
            {
                var rot = Quaternion.Euler(0f, 90f * s, 0f);
                var midpoint = rot * new Vector3(0f, 0f, -halfExtent);
                var alongDir = rot * Vector3.right;
                var outwardDir = rot * Vector3.back;

                // Each side's LAST slot is a corner that some other side's slot 0 already
                // occupies, so it is skipped here: all four corners are filled exactly once.
                for (int i = 0; i < perSide - 1; i++)
                {
                    float along = -halfExtent + i * stride;
                    float tj = (float)(rng.NextDouble() * 2.0 - 1.0) * tangentJitter;
                    float rj = (float)(rng.NextDouble() * 2.0 - 1.0) * radialJitter;
                    var pos = midpoint + alongDir * (along + tj) + outwardDir * rj;

                    string rel = prefabRelPaths[rng.Next(prefabRelPaths.Length)];
                    var go = InstantiatePiece(rel, parent, logTag);
                    go.transform.localPosition = pos;
                    go.transform.localRotation = Quaternion.Euler(0f, (float)(rng.NextDouble() * 360.0), 0f);
                    float sc = Mathf.Lerp(appliedScaleMin, appliedScaleMax, (float)rng.NextDouble());
                    go.transform.localScale *= sc;
                    go.name = label + "_" + sideNames[s] + "_" + i;
                    if (traced.Add(rel)) TraceMaterials(flowSys, label + " (boundary ring)", NatureRoot + rel, go);
                    placed++;
                }
            }

            report.Placed = placed;
            report.PerSide = perSide - 1;
            report.Stride = stride;
            report.PieceFootprint = pieceFootprint;
            report.MaxPieceFootprint = maxPieceFootprint;
            report.WorstGap = worstGap;
            report.Clamped = clamped;
            report.InwardReach = maxPieceFootprint * 0.5f;
            report.AppliedScaleMin = appliedScaleMin;
            report.AppliedScaleMax = appliedScaleMax;
            report.ScaleFitted = scaleFitted;
            report.RadialJitter = radialJitter;
            return report;
        }

        // =====================================================================
        //  Measure the SMALLEST XZ footprint the palette can produce, at scale 1.
        //  Instantiates each prefab once into the scene, reads the renderer bounds and
        //  destroys it - the same measure-then-discard shape RaidBaseGenerator.
        //  MeasureTowerHalf uses. Falls back to the primitive's own width when the
        //  gitignored pack is not imported, so the derivation still holds.
        // =====================================================================
        public static float MeasureMinFootprint(string[] prefabRelPaths, string logTag)
        {
            MeasureFootprints(prefabRelPaths, logTag, out float min, out _);
            return min;
        }

        /// <summary>
        /// Both ends of the palette in ONE pass. <paramref name="minXZ"/> drives the stride (the
        /// thinnest piece is what could open a gap); <paramref name="maxXZ"/> drives containment
        /// (the widest piece is what reaches furthest inward from the ring line).
        /// </summary>
        public static void MeasureFootprints(string[] prefabRelPaths, string logTag,
                                             out float minXZ, out float maxXZ)
        {
            float min = float.MaxValue;
            float max = 0f;
            if (prefabRelPaths != null)
            {
                foreach (var rel in prefabRelPaths)
                {
                    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(NatureRoot + rel);
                    if (prefab == null) continue;

                    var tmp = Object.Instantiate(prefab);
                    var rends = tmp.GetComponentsInChildren<Renderer>(true);
                    if (rends != null && rends.Length > 0)
                    {
                        var b = rends[0].bounds;
                        for (int i = 1; i < rends.Length; i++) b.Encapsulate(rends[i].bounds);
                        float thin = Mathf.Min(b.size.x, b.size.z);
                        float wide = Mathf.Max(b.size.x, b.size.z);
                        if (thin > 0.01f && thin < min) min = thin;
                        if (wide > max) max = wide;
                    }
                    Object.DestroyImmediate(tmp);
                }
            }

            if (min >= float.MaxValue)
            {
                Debug.LogWarning(logTag + " no boundary prefab could be measured (the polyperfect pack " +
                                 "is gitignored and may not be imported) - using the primitive fallback " +
                                 "footprint " + FallbackFootprint.ToString("F2") + "m for both the stride " +
                                 "and the containment bound.");
                minXZ = FallbackFootprint;
                maxXZ = FallbackFootprint;
                return;
            }
            minXZ = min;
            maxXZ = Mathf.Max(min, max);
        }

        // =====================================================================
        //  Load + instantiate one landscape piece. GRACEFUL: LogWarning (never error,
        //  never throw) plus a collidered primitive so the boundary still blocks and
        //  still carves navmesh on a clone without the gitignored pack (CLAUDE.md sec.4).
        //  The fallback gets an explicit URP/Lit material: unlike the runtime
        //  MagentaGuard registry, an editor bake SAVES the scene, so an unassigned
        //  material would persist as magenta in the shipped raid.
        // =====================================================================
        public static GameObject InstantiatePiece(string relPath, Transform parent, string logTag)
        {
            string path = NatureRoot + relPath;
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab != null)
            {
                var inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                inst.transform.SetParent(parent, false);
                if (inst.GetComponentInChildren<Collider>() == null)
                {
                    var cap = inst.AddComponent<CapsuleCollider>();
                    cap.radius = 0.6f;
                    cap.height = 3f;
                    cap.center = new Vector3(0f, 1.5f, 0f);
                }
                return inst;
            }

            Debug.LogWarning(logTag + " missing polyperfect nature prefab (primitive fallback): " + path);
            var fallback = GameObject.CreatePrimitive(PrimitiveType.Cylinder);  // keeps a CapsuleCollider -> blocks
            fallback.transform.SetParent(parent, false);
            fallback.transform.localScale = new Vector3(FallbackFootprint, 1.5f, FallbackFootprint);
            TintFallback(fallback);
            return fallback;
        }

        // =====================================================================
        //  WO-1637 STEP 1 - THE PER-FAMILY MATERIAL TRACE.
        //
        //  Until this existed, NOTHING on the arena bake path named a material. The ring
        //  builder logged geometry only (RaidBaseGenerator's ring line: piece, stride, gap,
        //  reach, jitter), the spire logged its art PATH, and the dresser logged art TOKENS -
        //  so "which material did the thing the owner is looking at actually resolve to"
        //  could only be answered by reading a .prefab and a .mat by hand and hoping the
        //  bake agreed. That is a prefab reading, not a measurement (CLAUDE.md sec.11B).
        //
        //  This is the line that answers it, forever, for any family any builder places.
        //  It is PERMANENT instrumentation (CLAUDE.md sec.12 - never strip; flag off at
        //  most). It runs at BAKE time only, once per family per bake, so it costs nothing
        //  on device and never touches a frame path.
        //
        //  sharedMaterial, NEVER material: `.material` INSTANTIATES a copy, and an editor
        //  bake SAVES the scene - so reading `.material` here would leak a duplicate
        //  material into the shipped raid, which is the exact class of damage the fallback
        //  tint below exists to avoid.
        //
        //  `flowSys` is a PARAMETER, not a constant, because this file lives in
        //  DeNelle.Editor and the raid tag ("RaidBase") is owned by RaidBaseDresser.Sys in
        //  DeNelle.EditorWallTools - which references DeNelle.Editor and not the other way
        //  round. Copying the literal here would be the duplicated-state failure CLAUDE.md
        //  sec.2 / sec.5 / sec.16 each describe. A null/empty tag means "this caller does
        //  not trace", which is what keeps the parameter optional at every call site.
        // =====================================================================
        public static void TraceMaterials(string flowSys, string family, string resolvedPath, GameObject inst)
        {
            if (string.IsNullOrEmpty(flowSys) || inst == null) return;

            Guard.Try(flowSys, "material trace " + family, () =>
            {
                var rends = inst.GetComponentsInChildren<Renderer>(true);
                string path = string.IsNullOrEmpty(resolvedPath) ? "<unknown>" : resolvedPath;

                if (rends == null || rends.Length == 0)
                {
                    FlowTrace.Warn(flowSys, "MAT " + family + " prefab='" + path +
                                   "' has NO Renderer at all - nothing in this family can be shading, " +
                                   "so a palette or fog change cannot be what the player is seeing.");
                    return;
                }

                var seen = new HashSet<string>();
                var parts = new List<string>();
                for (int i = 0; i < rends.Length; i++)
                {
                    if (rends[i] == null) continue;
                    var mat = rends[i].sharedMaterial;
                    if (mat == null)
                    {
                        if (seen.Add("<null>")) parts.Add("mat=<NULL - this renders MAGENTA under URP>");
                        continue;
                    }

                    string matName = mat.name;
                    if (!seen.Add(matName)) continue;

                    string shaderName = mat.shader != null ? mat.shader.name : "<null shader>";

                    string baseMap = "n/a";
                    if (mat.HasProperty("_BaseMap"))
                    {
                        var tex = mat.GetTexture("_BaseMap");
                        baseMap = tex == null ? "NULL" : tex.name;
                    }

                    string baseColor = "n/a";
                    if (mat.HasProperty("_BaseColor"))
                    {
                        var c = mat.GetColor("_BaseColor");
                        string r = c.r.ToString("F3");
                        string g = c.g.ToString("F3");
                        string b = c.b.ToString("F3");
                        baseColor = "(" + r + ", " + g + ", " + b + ")";
                    }

                    parts.Add("mat='" + matName + "' shader='" + shaderName +
                              "' _BaseMap=" + baseMap + " _BaseColor=" + baseColor);
                }

                string joined = parts.Count > 0 ? string.Join(" | ", parts.ToArray()) : "<no material read>";
                FlowTrace.Step(flowSys, "MAT " + family + " prefab='" + path + "' renderers=" +
                               rends.Length + " distinct=" + parts.Count + " " + joined);
            });
        }

        private static void TintFallback(GameObject go)
        {
            var sh = Shader.Find("Universal Render Pipeline/Lit");
            if (sh == null) return;
            var r = go.GetComponent<MeshRenderer>();
            if (r == null) return;
            var m = new Material(sh) { name = "ArenaBoundary_Fallback" };
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", new Color(0.45f, 0.44f, 0.42f));
            r.sharedMaterial = m;
        }
    }
}

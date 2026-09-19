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
// (jitter-x, jitter-z, prefab index, yaw, scale), so the siege venue's layout is
// reproduced by the siege bake from the same seed.
//
// ⚠ CORRECTED 2026-09-10 (WO-1689). These two lines used to say the move left
// "SiegeArena.unity's layout unchanged and needing no re-bake". THAT SCENE DOES NOT
// EXIST and never has: it is absent from `git ls-files`, absent from disk, NOT
// gitignored, `git log -- Assets/Scenes/SiegeArena.unity` returns nothing, and it is
// not in EditorBuildSettings. The venue is created on demand by
// `DeNelle.Editor.ProceduralSiegeArenaBuilder.BatchBuildAndBakeSiegeArena`. The
// RNG-order guarantee is real and worth keeping - it just guarantees that the BAKE
// reproduces the same layout, not that some saved scene stays valid.
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
// <repo>/Assets/polyperfect/Low Poly Ultimate Pack/_M/Prefabs_M/.
//
// WO-1637 (2026-09-10) RE-ROOTED THE PALETTE ONE FOLDER UP, and the reason is a
// measurement, not a preference. The root used to be `.../Prefabs_M/Nature_M/`, which
// made the whole pack outside `Nature_M` unreachable - and EVERY prefab in
// `Nature_M/Stones_M` binds the SAME single swatch, `M_14_Brown_lightest_LPUP`
// (_BaseColor 0.863/0.749/0.604, no _BaseMap at all). Twenty-three stone prefabs, one
// pale-tan colour: there was no darker rock to pick inside that folder, so "change the
// palette" was not expressible until the root moved. It is now `Prefabs_M/` and every
// entry carries its own theme folder.
// =============================================================================

using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using DeNelle.Core.Diagnostics;

namespace DeNelle.Editor
{
    public static class ArenaBoundaryRing
    {
        /// <summary>
        /// polyperfect `_M/Prefabs_M/` root - the THEME folder is part of each entry below.
        /// Gitignored pack, so callers must tolerate a miss (see <see cref="InstantiatePiece"/>).
        /// </summary>
        public const string PrefabRoot =
            "Assets/polyperfect/Low Poly Ultimate Pack/_M/Prefabs_M/";

        /// <summary>Trees / rocks that read as natural cover and keep their colliders.</summary>
        public static readonly string[] TreePaths =
        {
            "Nature_M/Trees_M/Tree_Oak.prefab",
            "Nature_M/Trees_M/Tree_Conifer.prefab",
            "Nature_M/Trees_M/Tree_Beech.prefab",
            "Nature_M/Trees_M/Trees_Dead_M/Tree_Dead_Broken.prefab",
        };

        /// <summary>
        /// The boundary palette - "a low wall of large rocks" (the siege venue's own words).
        /// <para/>
        /// ⚠ WO-1868 REDIRECT (2026-09-19): the Fantasy_M dungeon-pillar trio is RETIRED as a
        /// palette pick. WO-1758 named the owner's "giant untextured grey box" as
        /// <c>dungeon-pillar-stone-square</c> under <c>ArenaBoundary_Ring</c> binding
        /// empty-albedo <c>M_21_Grey_Light_LPUP</c>. Rebind-to-shadow helped luminance but the
        /// mesh still read as a grey box wall. This array now uses the TRACKED KayKit forest
        /// rocks already under <c>Assets/Resources/Arena/</c> (same vocabulary HubFoliageInjector
        /// / ArenaPrefabBuilder use) — textured <c>forest_texture_URP</c>, rock-shaped, not
        /// pillars. Absolute <c>Assets/...</c> paths; see <see cref="ResolvePrefabPath"/>.
        /// <para/>
        /// Both venues still share this ONE array (WO-1637 owner tick). Footprints are MEASURED
        /// at bake via <see cref="MeasureFootprints"/> — do not hardcode thin/wide here.
        /// <para/>
        /// ⛔ DO NOT re-add <c>Dungeon_Pillar_Stone_Square</c>, <c>Dungeon_Pillar_Stone_Round</c>,
        /// or any Colors swatch named <c>M_21_Grey_Light_LPUP</c> as a palette pick. The
        /// light-swatch rebind below remains as a safety net for future palette edits.
        /// ⛔ DO NOT ADD A THIN PIECE HERE. A modular wall panel (e.g. <c>Dungeon_Wall_Stone</c>,
        /// 4.00 x 0.33) has a thin/wide ratio of 0.08; it would clamp the count and tear the
        /// ring open. Anything added must be measured first.
        /// </summary>
        public static readonly string[] RockPaths =
        {
            "Assets/Resources/Arena/Rock_1_A_Color1.fbx",
            "Assets/Resources/Arena/Rock_1_J_Color1.fbx",
            "Assets/Resources/Arena/Rock_2_C_Color1.fbx",
            "Assets/Resources/Arena/Rock_3_E_Color1.fbx",
        };

        /// <summary>
        /// Resolve a palette entry to an AssetDatabase path. Absolute <c>Assets/...</c> entries
        /// (WO-1868 KayKit rocks) pass through; relative entries stay under <see cref="PrefabRoot"/>.
        /// </summary>
        public static string ResolvePrefabPath(string relOrAbs)
        {
            if (string.IsNullOrEmpty(relOrAbs)) return relOrAbs;
            if (relOrAbs.StartsWith("Assets/", System.StringComparison.Ordinal)) return relOrAbs;
            return PrefabRoot + relOrAbs;
        }

        /// <summary>Widest a fallback primitive is - the floor used when the pack is absent.</summary>
        private const float FallbackFootprint = 1.2f;

        // =====================================================================
        //  WO-1758 - THE LIGHT-SWATCH GUARD. Why this exists, and why HERE.
        //
        //  The owner's "giant untextured grey box" was NAMED by the WO-1751 census on
        //  its first device run (build 2026.09.15.371285, F8 seq 5295-5300): a
        //  `dungeon-pillar-stone-square` at 2.6 x 7.0 x 2.6 m under
        //  `RaidBase_iron_bastion/ArenaBoundary_Ring/`, slot 1, material
        //  `M_21_Grey_Light_LPUP`, `albedoSlots=[_BaseMap=EMPTY, _MainTex=EMPTY]`,
        //  tint (0.65,0.63,0.62). Those bounds are this ring's own arithmetic: the
        //  measured 0.80 x 0.80 x 3.12 m mesh at the band-fitted ~2.26x is 7.05 m tall
        //  and 2.56 m across its diagonal under the ring's free yaw. It is us.
        //
        //  ⛔ THE FIX CANNOT LIVE IN THE MATERIAL, AND THE PACK IS ONLY HALF THE REASON.
        //  `M_21_Grey_Light_LPUP` is under `Assets/polyperfect/`, which is GITIGNORED
        //  (CLAUDE.md sec.4) - an edit there cannot be committed and evaporates on the
        //  next clone. The other half: the pack's URP repair pass CANNOT reach it and
        //  never could. `PolyperfectUrpFix.Fix` skips any material already on a URP
        //  shader (`PolyperfectUrpFix.cs:67`, `if (!builtIn) continue;`), and this one
        //  is already `Universal Render Pipeline/Lit`; and even on the built-in branch
        //  it only binds `_BaseMap` when `_MainTex` is NON-null (`:76`), which this
        //  material's `_MainTex` is not. Re-running the repair is a no-op here. The
        //  52 materials under `Materials/Colors/` are the pack's PALETTE family - flat
        //  colour with no texture BY DESIGN, the textured family being `M_Atlas_LPUP`.
        //  So the missing albedo is not damage: THE TINT IS THE DEFECT.
        //
        //  AND THE ARRAY ABOVE ALREADY SAID SO. The palette sweep's stated criterion is
        //  "every prefab whose materials all sit in luminance 0.15-0.56" (see RockPaths,
        //  the WHY THESE THREE paragraph) - and the very next lines admit
        //  `M_21_Grey_Light_LPUP` at 0.636. 0.636 is OUTSIDE the band the sweep declared,
        //  and it is 0.04 from the sky the ring was re-palletted to beat (ring band 0.670
        //  vs sky 0.677 on the shipped 2026-09-10 frame). WO-1637 fixed half the ring and
        //  left the other half reproducing the original finding. Two of the three palette
        //  prefabs bind this swatch (`Dungeon_Pillar_Stone_Round` slot 0,
        //  `Dungeon_Pillar_Stone_Square` slot 1), as does the backing module
        //  `Dungeon_Wall_Stone` (slot 2) - all read out of the .prefab files 2026-09-15.
        //
        //  SO THE GUARD IS EXPRESSED IN THE PALETTE'S OWN TERMS, not as a named-material
        //  patch: any slot a ring piece carries that has NO albedo at all and a luminance
        //  ABOVE the sweep's own ceiling is rebound, at bake, to one shared stone tone
        //  inside the band. A future palette edit is policed by the same rule, and no
        //  gitignored byte is touched.
        //
        //  ⚠ THE ALBEDO TEST IS NOT HAND-ROLLED. It calls the SAME
        //  `DependencyClosureTrace.GetAlbedo` the census's own `ClassifySlot` calls
        //  (`RaidUntexturedCensus.cs:226`), so the fix and the instrument cannot drift
        //  into disagreeing about what "untextured" means - the exact drift
        //  ShaderPredicateSingleAuthorityRegression exists to stop.
        //
        //  WHY A SCENE-EMBEDDED MATERIAL RATHER THAN A COMMITTED .mat: this is the same
        //  move `TintFallback` below already makes, for the reason `InstantiatePiece`'s
        //  header states - an editor bake SAVES the scene, so a material referenced by a
        //  ring renderer is serialised into the baked scene and ships. The durable part
        //  is THIS FILE, which is committed; the material is re-created by every bake.
        //  Hand-authoring a .mat + a .meta with a pinned GUID would put a second piece of
        //  state in the tree for the scene to point at - the duplicated-state trap the
        //  ticket is itself about.
        // =====================================================================

        /// <summary>
        /// The palette sweep's OWN upper bound, in the Rec.709 luminance the RockPaths
        /// measurements are quoted in (0.2126R + 0.7152G + 0.0722B). A ring slot above it,
        /// with no albedo to carry detail, is the flat light slab.
        /// <para/>
        /// It is deliberately STRICTER than the detector: `RaidUntexturedCensus`'s
        /// `FlatTintLuminanceFloor` is 0.60 on the NTSC weights (`RaidUntexturedCensus.cs:78`).
        /// A guard set to the detector's threshold would clear the log while leaving art the
        /// palette's own criterion rejects; 0.56 clears both, with room.
        /// </summary>
        private const float PaletteLuminanceCeiling = 0.56f;

        /// <summary>
        /// The tone a rebound slot gets. Neutral-warm grey, Rec.709 luminance 0.427 (NTSC 0.428
        /// - for a near-neutral both weightings agree, which is what makes it safe to quote one
        /// number against two instruments).
        /// <para/>
        /// ⚠ IT IS A LUMINANCE DECISION, NOT A COLOUR ONE (memory
        /// `owner-colorblind-delegate-visual-creative` - greyscale is the gate). It sits at the
        /// MIDDLE of the sweep's 0.15-0.56 band where `M_20_Grey_LPUP` (0.514) sits at the top,
        /// so RockPaths' "TWO values instead of one is itself part of the fix" survives intact
        /// as 0.514 / 0.427 instead of 0.514 / 0.636 - and the ring now reads 0.25 below the
        /// measured sky (0.677) instead of 0.04 below it. Re-tunable by the owner in one line.
        /// </summary>
        private static readonly Color StoneShadowTint = new Color(0.44f, 0.425f, 0.415f);

        private const string StoneShadowName = "ArenaBoundary_Stone_Shadow";

        /// <summary>One shared instance per bake - 500+ ring renderers point at this one material.</summary>
        private static Material _stoneShadow;

        /// <summary>Distinct swatch names already traced this placement run (one line each, not 500).</summary>
        private static readonly HashSet<string> _rebindTraced = new HashSet<string>();

        /// <summary>Slots rebound this placement run - reported in the builder's own log line.</summary>
        private static int _rebindCount;

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
        //  FROZEN (jx, jz, prefab index, yaw, scale) - the siege venue's layout is
        //  reproduced exactly by the siege bake from the same seed (WO-1689: this line
        //  used to name a saved `SiegeArena.unity`; that scene has never existed - see
        //  the file header).
        // =====================================================================
        public static void PlacePolarRing(
            Transform parent, System.Random rng, float radius, int count, float jitter,
            string[] prefabRelPaths, string label, ref int placedCounter,
            float scaleMin, float scaleMax, string logTag, string flowSys = null)
        {
            if (parent == null || rng == null || prefabRelPaths == null || prefabRelPaths.Length == 0) return;

            BeginRebindRun();

            // WO-1637 step 1: one MAT line per DISTINCT prefab in the palette, not per piece.
            var traced = new HashSet<string>();

            for (int i = 0; i < count; i++)
            {
                float ang = (i / (float)count) * Mathf.PI * 2f;
                float jx = (float)(rng.NextDouble() * 2.0 - 1.0) * jitter;
                float jz = (float)(rng.NextDouble() * 2.0 - 1.0) * jitter;
                var pos = new Vector3(Mathf.Cos(ang) * radius + jx, 0f, Mathf.Sin(ang) * radius + jz);

                string rel = prefabRelPaths[rng.Next(prefabRelPaths.Length)];
                var go = InstantiatePiece(rel, parent, logTag, flowSys);
                go.transform.localPosition = pos;
                go.transform.localRotation = Quaternion.Euler(0f, (float)(rng.NextDouble() * 360.0), 0f);
                float s = Mathf.Lerp(scaleMin, scaleMax, (float)rng.NextDouble());
                go.transform.localScale *= s;
                go.name = label + "_" + i;
                if (traced.Add(rel)) TraceMaterials(flowSys, label + " (polar)", ResolvePrefabPath(rel), go);
                placedCounter++;
            }

            ReportRebindRun(logTag, label + " (polar)");
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
            float scaleMin, float scaleMax, int maxPerSide, string logTag, string flowSys = null,
            string backingModule = null)
        {
            var report = new BoundaryReport();
            if (parent == null || rng == null || prefabRelPaths == null || prefabRelPaths.Length == 0)
                return report;

            BeginRebindRun();

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
                    var go = InstantiatePiece(rel, parent, logTag, flowSys);
                    go.transform.localPosition = pos;
                    go.transform.localRotation = Quaternion.Euler(0f, (float)(rng.NextDouble() * 360.0), 0f);
                    float sc = Mathf.Lerp(appliedScaleMin, appliedScaleMax, (float)rng.NextDouble());
                    go.transform.localScale *= sc;
                    go.name = label + "_" + sideNames[s] + "_" + i;
                    if (traced.Add(rel)) TraceMaterials(flowSys, label + " (boundary ring)", ResolvePrefabPath(rel), go);
                    placed++;
                }
            }

            // WO-1704: the palette's footprint overlap never proved body-height
            // solidity. Raid opts into an intact authored wall behind the original skyline.
            // No RNG draws, palette edits, polar changes, or expanded staging envelope.
            if (!string.IsNullOrEmpty(backingModule))
                PlaceSquareBacking(parent, halfExtent, bandHalf - containmentSlack - containmentHeadroom,
                    backingModule, logTag, flowSys);

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

            // After the backing, so the summary covers every renderer under the ring root -
            // which is exactly the scope the census reports by path.
            ReportRebindRun(logTag, label + " (boundary ring)");
            return report;
        }

        private static void PlaceSquareBacking(Transform parent, float halfExtent, float allowedHalfDepth,
                                               string module, string logTag, string flowSys)
        {
            // Close the actual exterior body, not merely the player's first two metres.
            // Keep the top 5% of the existing skyline as decorative pillar caps; all
            // original pieces, XZ placement, band depth and RNG cadence stay authored.
            float skylineTop = parent.position.y;
            foreach (Transform piece in parent)
                if (PieceBounds(piece.gameObject, out Bounds skyline))
                    skylineTop = Mathf.Max(skylineTop, skyline.max.y);
            float closureHeight = (skylineTop - parent.position.y) * 0.95f;
            var zone = new GameObject("BoundaryBacking");
            zone.transform.SetParent(parent, false);
            // The probe is MEASURED and destroyed, never shipped, so it opts OUT of the guard
            // entirely - it must not consume the one-line-per-swatch trace budget the placed
            // panels need, and the rebind cannot change a bounds measurement anyway.
            var probe = InstantiatePiece(module, zone.transform, logTag, null, false);
            if (!PieceBounds(probe, out Bounds native))
            {
                Object.DestroyImmediate(probe);
                Debug.LogWarning(logTag + " backing module has no measurable renderer: " + module);
                return;
            }
            bool longX = native.size.x >= native.size.z;
            float span = Mathf.Max(native.size.x, native.size.z);
            float depth = Mathf.Min(native.size.x, native.size.z);
            float height = native.size.y;
            Object.DestroyImmediate(probe);
            if (span < 0.01f || depth * 0.5f > allowedHalfDepth)
            {
                Debug.LogWarning(logTag + " backing cannot fit existing containment band: " + module);
                return;
            }
            int count = Mathf.Max(1, Mathf.CeilToInt(halfExtent * 2f / span));
            float step = halfExtent * 2f / count;
            int placed = 0;
            for (int side = 0; side < 4; side++)
            {
                var rot = Quaternion.Euler(0f, side * 90f, 0f);
                for (int i = 0; i < count; i++)
                {
                    var go = InstantiatePiece(module, zone.transform, logTag, flowSys);
                    go.name = "BoundaryBacking_" + side + "_" + i;
                    go.transform.localRotation = rot * Quaternion.Euler(0f, longX ? 0f : 90f, 0f);
                    var scale = go.transform.localScale;
                    scale.y *= Mathf.Max(height, closureHeight) / Mathf.Max(0.01f, height);
                    // A 2cm lap joins imperfect authored ends, within the unchanged band.
                    if (longX) scale.x *= (step + 0.02f) / span;
                    else scale.z *= (step + 0.02f) / span;
                    go.transform.localScale = scale;
                    Vector3 centre = parent.TransformPoint(rot * new Vector3(-halfExtent + (i + 0.5f) * step, 0f, -halfExtent));
                    if (PieceBounds(go, out Bounds b))
                        go.transform.position += new Vector3(centre.x - b.center.x, centre.y - b.min.y, centre.z - b.center.z);
                    if (placed == 0) TraceMaterials(flowSys, "continuous exterior backing", ResolvePrefabPath(module), go);
                    placed++;
                }
            }
            Debug.Log(logTag + " BOUNDARY BACKING module=" + module + " panels=" + placed +
                " height=" + Mathf.Max(height, closureHeight).ToString("F3") + "m nativeHeight=" + height.ToString("F3") +
                "m originalSkylineTop=" + skylineTop.ToString("F3") + "m depth=" + depth.ToString("F3") +
                "m step=" + step.ToString("F3") + "m lap=0.020m; original skyline retained");
        }

        private static bool PieceBounds(GameObject go, out Bounds bounds)
        {
            bounds = default(Bounds);
            bool found = false;
            foreach (var renderer in go.GetComponentsInChildren<Renderer>(true))
            {
                if (!renderer.enabled || !renderer.gameObject.activeInHierarchy) continue;
                if (!found) bounds = renderer.bounds;
                else bounds.Encapsulate(renderer.bounds);
                found = true;
            }
            return found;
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
                    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ResolvePrefabPath(rel));
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
        public static GameObject InstantiatePiece(string relPath, Transform parent, string logTag,
                                                  string flowSys = null, bool rebindLightSwatches = true)
        {
            string path = ResolvePrefabPath(relPath);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab != null)
            {
                var inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                inst.transform.SetParent(parent, false);
                // WO-1758: every placement path in this file funnels through here, so ONE call
                // covers the polar ring, the square perimeter and the backing panels. `flowSys`
                // decides only whether the rebind is TRACED, never whether it happens - a caller
                // that does not trace still ships correct art. `rebindLightSwatches:false` is for
                // the measure-and-discard PROBE alone (see PlaceSquareBacking): a probe must not
                // spend the one-line-per-swatch budget the placed panels need.
                if (rebindLightSwatches) RebindLightUntexturedSlots(inst, logTag, flowSys);
                if (inst.GetComponentInChildren<Collider>() == null)
                {
                    var cap = inst.AddComponent<CapsuleCollider>();
                    cap.radius = 0.6f;
                    cap.height = 3f;
                    cap.center = new Vector3(0f, 1.5f, 0f);
                }
                return inst;
            }

            Debug.LogWarning(logTag + " missing polyperfect prefab (primitive fallback): " + path);
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

        // =====================================================================
        //  WO-1758 - the guard itself. See the LIGHT-SWATCH GUARD block up top for the
        //  proof; this is the mechanism.
        // =====================================================================

        /// <summary>Clears the per-run trace budget + counter. Called by BOTH placement entry points.</summary>
        private static void BeginRebindRun()
        {
            _rebindTraced.Clear();
            _rebindCount = 0;
        }

        /// <summary>
        /// One line per bake naming what the guard did - including ZERO, which is the line that
        /// proves the guard ran and found nothing rather than never running at all (the vacuous-green
        /// failure ShaderPredicateSingleAuthorityRegression's case 5 exists to stop).
        /// </summary>
        private static void ReportRebindRun(string logTag, string family)
        {
            Debug.Log(logTag + " LIGHT-SWATCH GUARD " + family + ": rebound " + _rebindCount +
                      " slot(s) across " + _rebindTraced.Count + " distinct swatch(es) to '" +
                      StoneShadowName + "' (Rec.709 " + Rec709(StoneShadowTint).ToString("F3") +
                      "); ceiling " + PaletteLuminanceCeiling.ToString("F2") +
                      ", albedo test = DependencyClosureTrace.GetAlbedo (the census's own).");
        }

        /// <summary>
        /// Rebind every slot on <paramref name="inst"/> that carries NO albedo at all AND a
        /// luminance above <see cref="PaletteLuminanceCeiling"/>. Dark untextured swatches
        /// (<c>M_20_Grey_LPUP</c> 0.514, <c>M_57_Black_LPUP</c> 0.081) and anything actually
        /// textured are left exactly as the pack authored them.
        /// <para/>
        /// ⚠ <c>sharedMaterials</c> returns a COPY of the array. Writing into the value returned
        /// by the getter changes nothing - the array has to be assigned BACK. And it must be
        /// <c>sharedMaterials</c>, never <c>materials</c>: `.materials` instantiates per-renderer
        /// copies, and an editor bake SAVES the scene, so that would serialise ~500 duplicate
        /// materials into the shipped raid (the same reason TraceMaterials' header gives).
        /// </summary>
        private static void RebindLightUntexturedSlots(GameObject inst, string logTag, string flowSys)
        {
            if (inst == null) return;
            var rends = inst.GetComponentsInChildren<Renderer>(true);
            if (rends == null) return;

            for (int ri = 0; ri < rends.Length; ri++)
            {
                var r = rends[ri];
                if (r == null) continue;

                var mats = r.sharedMaterials;          // a COPY - see the summary above
                if (mats == null || mats.Length == 0) continue;

                bool changed = false;
                for (int mi = 0; mi < mats.Length; mi++)
                {
                    var m = mats[mi];
                    if (m == null) continue;   // a NULL slot is MagentaGuard's problem, not this guard's

                    // The SAME albedo authority RaidUntexturedCensus.ClassifySlot uses (:226),
                    // so the fix and the detector cannot drift apart.
                    if (DeNelle.Core.DependencyClosureTrace.GetAlbedo(m) != null) continue;

                    float lum = Rec709(TintOf(m));
                    if (lum <= PaletteLuminanceCeiling) continue;

                    var shadow = StoneShadow();
                    if (shadow == null) return;   // no URP/Lit - TintFallback's own precondition

                    // The SET is unconditional and the TRACE is gated, never the other way round:
                    // gating the set would make the summary line below read "0 distinct swatch(es)"
                    // for any caller that passes no flowSys, while reporting a non-zero rebind
                    // count - a self-contradicting line is worse than no line.
                    bool firstOfSwatch = _rebindTraced.Add(m.name);
                    if (firstOfSwatch && !string.IsNullOrEmpty(flowSys))
                    {
                        string before = lum.ToString("F3");
                        string after = Rec709(StoneShadowTint).ToString("F3");
                        FlowTrace.Step(flowSys, "REBIND boundary swatch '" + m.name +
                                       "' slot=" + mi + " had NO albedo at Rec.709 luminance " +
                                       before + ", above the palette ceiling " +
                                       PaletteLuminanceCeiling.ToString("F2") +
                                       " -> '" + StoneShadowName + "' at " + after +
                                       ". The gitignored pack asset is UNTOUCHED; only this " +
                                       "renderer's slot moved.");
                    }

                    mats[mi] = shadow;
                    changed = true;
                    _rebindCount++;
                }

                if (changed) r.sharedMaterials = mats;   // assign BACK, or none of it happened
            }
        }

        /// <summary>
        /// The one shared stone tone, created lazily and re-created after a domain reload (a
        /// destroyed Material compares equal to null under Unity's fake-null). Not an asset:
        /// the bake serialises it into the saved scene, exactly as TintFallback's material is.
        /// </summary>
        private static Material StoneShadow()
        {
            if (_stoneShadow != null) return _stoneShadow;

            var sh = Shader.Find("Universal Render Pipeline/Lit");
            if (sh == null)
            {
                Debug.LogWarning("[ArenaBoundaryRing] 'Universal Render Pipeline/Lit' not found - the " +
                                 "light-swatch guard cannot build its stone tone, so the ring keeps the " +
                                 "pack's own swatches this bake.");
                return null;
            }

            var m = new Material(sh) { name = StoneShadowName };
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", StoneShadowTint);
            if (m.HasProperty("_Color")) m.SetColor("_Color", StoneShadowTint);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.1f);
            if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", 0f);
            _stoneShadow = m;
            return m;
        }

        /// <summary>Same two-property tint read RaidUntexturedCensus.TintOf performs (:235-241).</summary>
        private static Color TintOf(Material m)
        {
            if (m == null) return Color.white;
            if (m.HasProperty("_BaseColor")) return m.GetColor("_BaseColor");
            if (m.HasProperty("_Color")) return m.GetColor("_Color");
            return Color.white;
        }

        /// <summary>
        /// Rec.709 luminance - the SAME weighting every measured number in RockPaths' header is
        /// quoted in (it reproduces M_20 at 0.514 and M_21 at 0.636 exactly), so the ceiling and
        /// the palette doc can be compared without a conversion step in between.
        /// </summary>
        private static float Rec709(Color c)
        {
            return 0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b;
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

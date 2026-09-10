// =============================================================================
// CoverRingPlacer - polar cover-ring / cluster math, shared by scene dressers.
// -----------------------------------------------------------------------------
// WO-1633. Owner, 2026-09-10, on how raid courtyards should be dressed, verbatim:
//   "similar strategy as we used in battle arena"
//
// The battle arena she is naming is ProceduralSiegeArenaBuilder. Its PlaceCoverRing
// (:207-224) is the vocabulary: an even polar angle around a ring radius, then a
// JITTER on x/z from a SEEDED System.Random so the ring does not read as fenceposts,
// a random yaw, and a Lerp(scaleMin, scaleMax) size roll - deterministic, so a
// rebuild reproduces the same layout. Its header (:24-27) also records the half that
// matters for gameplay: the pieces keep their colliders "so they actually block
// movement + line-of-sight".
//
// RaidBaseDresser had none of jitter, scale variance or colliders - see WO-1633 §2.
// This file is the reusable HALF of PlaceCoverRing, extracted rather than copied, so
// the raid dresser calls it today and ProceduralSiegeArenaBuilder can adopt it later
// without a third copy of the same math. It is deliberately pure: no scene access,
// no AssetDatabase, no instantiation - callers own those.
//
// Editor-only helper; lives in DeNelle.Editor beside its first caller.
// =============================================================================
using UnityEngine;

namespace DeNelle.Editor
{
    /// <summary>
    /// Pure polar placement math for cover rings and prop clusters. Every method is
    /// deterministic given the same <see cref="System.Random"/>, so a re-bake of a
    /// scene reproduces the identical layout (the arena's RandomSeed contract).
    /// </summary>
    public static class CoverRingPlacer
    {
        /// <summary>One resolved placement: where, facing, and how big.</summary>
        public struct Slot
        {
            public Vector3 Position;
            public Quaternion Rotation;
            public float Scale;
        }

        /// <summary>
        /// How many concentric cover rings fit in a band, given a target spacing.
        /// Clamped to 1..<paramref name="maxRings"/> so a thin band still gets one ring
        /// and a huge courtyard does not turn into a car park.
        /// </summary>
        public static int RingCount(float innerRadius, float outerRadius, float ringSpacing, int maxRings)
        {
            if (ringSpacing <= 0.01f) return 1;
            float band = Mathf.Max(0f, outerRadius - innerRadius);
            int n = Mathf.RoundToInt(band / ringSpacing);
            return Mathf.Clamp(n, 1, Mathf.Max(1, maxRings));
        }

        /// <summary>
        /// The radius of ring <paramref name="ringIndex"/> of <paramref name="ringCount"/>
        /// inside the band. Rings are inset from both edges so no ring sits ON the inner
        /// boundary or ON the wall line.
        /// </summary>
        public static float BandRadius(float innerRadius, float outerRadius, int ringIndex, int ringCount)
        {
            if (ringCount <= 1) return Mathf.Lerp(innerRadius, outerRadius, 0.5f);
            int i = Mathf.Clamp(ringIndex, 0, ringCount - 1);
            float t = (i + 0.5f) / ringCount;
            return Mathf.Lerp(innerRadius, outerRadius, t);
        }

        /// <summary>Cartesian point on a ring. Angle in radians, y stays 0 (callers seat on ground).</summary>
        public static Vector3 PolarPoint(float radius, float angleRad)
        {
            return new Vector3(Mathf.Cos(angleRad) * radius, 0f, Mathf.Sin(angleRad) * radius);
        }

        /// <summary>
        /// The anchor angle for cluster <paramref name="index"/> of <paramref name="count"/>.
        /// Even spacing plus a seeded wobble of up to <paramref name="wobbleRad"/>, so clusters
        /// read as places people chose rather than as spokes on a wheel. WO-1609:99, verbatim:
        /// "Place as clusters, not a ring of singles".
        /// </summary>
        public static float ClusterAnchorAngle(System.Random rng, int index, int count, float wobbleRad)
        {
            int n = Mathf.Max(1, count);
            float even = (index / (float)n) * Mathf.PI * 2f;
            if (rng == null || wobbleRad <= 0f) return even;
            float w = (float)(rng.NextDouble() * 2.0 - 1.0) * wobbleRad;
            return even + w;
        }

        /// <summary>
        /// One jittered slot around <paramref name="anchor"/>: the arena's own recipe
        /// (ProceduralSiegeArenaBuilder.cs:213-222) - independent x/z jitter inside
        /// <paramref name="jitter"/> metres, a free yaw, and a size roll between
        /// <paramref name="scaleMin"/> and <paramref name="scaleMax"/>.
        /// </summary>
        public static Slot Jittered(System.Random rng, Vector3 anchor, float jitter,
                                    float scaleMin, float scaleMax)
        {
            float jx = 0f, jz = 0f, yaw = 0f, s = 1f;
            if (rng != null)
            {
                jx = (float)(rng.NextDouble() * 2.0 - 1.0) * jitter;
                jz = (float)(rng.NextDouble() * 2.0 - 1.0) * jitter;
                yaw = (float)(rng.NextDouble() * 360.0);
                s = Mathf.Lerp(scaleMin, scaleMax, (float)rng.NextDouble());
            }
            return new Slot
            {
                Position = new Vector3(anchor.x + jx, anchor.y, anchor.z + jz),
                Rotation = Quaternion.Euler(0f, yaw, 0f),
                Scale = s,
            };
        }
    }
}

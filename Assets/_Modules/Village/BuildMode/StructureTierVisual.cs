// =============================================================================
// StructureTierVisual — per-tier visual progression for placed structures (DEF-208).
// -----------------------------------------------------------------------------
// Assembly: DeNelle.Village   Namespace: DeNelle.Village
//
// A placed structure carries an upgrade level (1..3 on PlacedStructure). DEF-208
// asks for a VISUALLY DISTINCT read per tier so an upgraded tower looks upgraded.
// Minimum-viable, fully data/code-driven (no extra art needed, pack-missing-safe):
//
//   • SCALE  — each tier is a touch taller/bulkier (a level-3 tower reads bigger).
//   • TINT   — a tier accent colour layered as a non-destructive emissive rim via a
//              shared MaterialPropertyBlock (bronze → silver → gold), the same
//              MPB approach PlacedStructure's selection highlight and GhostPreview
//              use, so it never leaks a material instance.
//
// Applied AFTER the visual is skinned (BaseLayoutLoader seeds the level, then calls
// Apply). Re-applies cleanly on upgrade. If a per-tier PREFAB ever lands in
// Resources, swap the skin path at the factory; this stays the graceful fallback.
// =============================================================================

using System.Collections.Generic;
using DeNelle.Core.Catalog;
using DeNelle.Core.Economy;
using UnityEngine;

namespace DeNelle.Village
{
    /// <summary>
    /// Drives the per-tier (1..3) look of a placed structure: a small scale bump plus
    /// a tier accent tint, applied over the skinned mesh via a MaterialPropertyBlock.
    /// Visual-only — never touches gameplay stats (those scale via the upgrade path).
    /// </summary>
    public sealed class StructureTierVisual : MonoBehaviour
    {
        // Tier accent colours — bronze (T1) → silver (T2) → gold (T3). Index 0 unused.
        private static readonly Color[] s_tierAccent =
        {
            Color.black,                              // 0 — unused
            new Color(0.55f, 0.35f, 0.18f, 1f),       // 1 — bronze
            new Color(0.72f, 0.74f, 0.78f, 1f),       // 2 — silver
            new Color(0.95f, 0.78f, 0.25f, 1f),       // 3 — gold
        };

        // Per-tier uniform scale multiplier — a level-3 structure reads visibly bigger.
        private static readonly float[] s_tierScale = { 1f, 1f, 1.12f, 1.25f };

        private readonly List<Renderer> _renderers = new List<Renderer>();
        private MaterialPropertyBlock _mpb;

        // The local scale captured at level 1, so re-applying a tier always scales from
        // the same baseline rather than compounding each upgrade.
        private Vector3 _baseScale = Vector3.one;
        private bool _baseCaptured;

        /// <summary>The tier (1..3) currently shown. 0 until first Apply.</summary>
        public int CurrentTier { get; private set; }

        /// <summary>
        /// Set the visual tier (clamped 1..3): scale-bump from the captured baseline and
        /// layer the tier accent as an emissive rim. Idempotent — calling twice with the
        /// same level is a no-op. Safe to call before/after the mesh is skinned.
        /// </summary>
        public void Apply(int level)
        {
            int tier = Mathf.Clamp(level, 1, 3);

            if (!_baseCaptured)
            {
                _baseScale = transform.localScale;
                _baseCaptured = true;
            }

            if (CurrentTier == tier && _renderers.Count > 0) return;
            CurrentTier = tier;
            ApplyInternal();
        }

        /// <summary>
        /// Re-assert the current tier's scale + tint unconditionally. Used after the
        /// selection highlight clears the emissive to black (PlacedStructure.SetHighlighted),
        /// so the tier accent is not lost. No-op until <see cref="Apply"/> has set a tier.
        /// </summary>
        public void Refresh()
        {
            if (CurrentTier < 1) return;
            ApplyInternal();
        }

        // Re-collect renderers (the mesh may have been skinned after Apply, or a prior
        // collection gone stale), then re-assert scale + the tier emissive accent.
        private void ApplyInternal()
        {
            // Storage progression is capacity/fill, while its authored pallet retains
            // its approved dimensions and materials at every level and on save replay.
            var placed = GetComponent<PlacedStructure>();
            var entry = placed != null ? CatalogRegistry.Get(placed.itemId) : null;
            if (TownBankCapacity.IsStorageContainer(entry?.repo))
            {
                transform.localScale = _baseScale;
                return;
            }

            int tier = Mathf.Clamp(CurrentTier, 1, 3);

            // Scale from the captured baseline (never compound).
            transform.localScale = _baseScale * s_tierScale[tier];

            _renderers.Clear();
            _renderers.AddRange(GetComponentsInChildren<Renderer>(true));

            if (_mpb == null) _mpb = new MaterialPropertyBlock();
            Color accent = s_tierAccent[tier];

            foreach (var r in _renderers)
            {
                if (r == null) continue;
                r.GetPropertyBlock(_mpb);
                // Tier 1 = no emissive lift (reads as the plain base tower); 2/3 glow.
                _mpb.SetColor("_EmissionColor", tier == 1 ? Color.black : accent * 0.6f);
                r.SetPropertyBlock(_mpb);
            }
        }
    }
}

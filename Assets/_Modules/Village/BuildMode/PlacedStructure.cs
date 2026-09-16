// =============================================================================
// PlacedStructure — runtime marker on every player-placed structure (WO-108).
// -----------------------------------------------------------------------------
// Assembly: DeNelle.Village   Namespace: DeNelle.Village
//
// Tags a GameObject as belonging to the player's editable BaseLayout and carries
// the live grid metadata (catalog id, cell, footprint cells, yaw steps, level,
// sell value). The save spine round-trips PlacedStructureData (Core); this is its
// in-scene twin. BuildModeController / BaseLayoutLoader read+write these to
// rebuild the BaseLayout on Exit and to drive select / sell (P2).
// =============================================================================

using System.Collections.Generic;
using UnityEngine;
using DeNelle.Core.State;
using DeNelle.Core.Diagnostics;   // F8-39: teardown/hide monitor — prove whether DEATH tears structures down

namespace DeNelle.Village
{
    /// <summary>
    /// Runtime metadata on a player-placed structure. The bridge between the live
    /// scene object and its persisted <see cref="PlacedStructureData"/> record.
    /// </summary>
    public sealed class PlacedStructure : MonoBehaviour
    {
        private void Start()
        {
            // WO-903: storage pallets are ordinary placed structures, so this is the one seam
            // shared by fresh placement and save replay. The presentation component validates
            // the catalog row and no-ops for every non-storage structure.
            Buildings.Progression.StorageStackView.Attach(this);
        }

        /// <summary>CatalogEntry id this structure was built from.</summary>
        public string itemId;

        /// <summary>The grid cell its footprint origin sits on.</summary>
        public Vector2Int gridCell;

        /// <summary>Footprint size in cells (e.g. 1×1 wall, 2×2 tower).</summary>
        public Vector2Int footprint = Vector2Int.one;

        /// <summary>Discrete yaw, 0..3 (× 90°).</summary>
        public int yawSteps;

        /// <summary>Extra yaw in degrees on top of <see cref="yawSteps"/> (WO-673 L5:
        /// a 45° facing persists as yawSteps + yawOffset=45). Mirrors
        /// <see cref="PlacedStructureData.yawOffset"/> so a move/save keeps the exact
        /// facing — previously this was dropped (ToSaveData hard-coded 0f) and a moved
        /// 45°-placed piece would snap back to its cardinal on reload.</summary>
        public float yawOffset;

        /// <summary>Upgrade level (1-based).</summary>
        public int level = 1;

        /// <summary>World-space Y this structure seats at (wall-top for a wall-mounted defense,
        /// else the grid/terrain height). Mirrors <see cref="PlacedStructureData.worldY"/> so a
        /// move/persist keeps the seat height.</summary>
        public float worldY;

        /// <summary>True when seated on a wall-walk top (defensive posture). Mirrors
        /// <see cref="PlacedStructureData.wallMounted"/>; drives the elevation range perk.</summary>
        public bool wallMounted;

        /// <summary>Crystals returned on sell (P2). 50% of build cost by convention.</summary>
        public int sellValue;

        /// <summary>The per-tier visual driver (DEF-208), if one was attached at spawn.
        /// Used to restore the tier accent after the selection highlight clears.</summary>
        public StructureTierVisual TierVisual;

        public string authoredSourceId;

        public static AuthoredStructurePose CaptureAuthoredPose(Transform target) => new AuthoredStructurePose
        {
            x = target.position.x, y = target.position.y, z = target.position.z,
            qx = target.rotation.x, qy = target.rotation.y, qz = target.rotation.z, qw = target.rotation.w,
            sx = target.localScale.x, sy = target.localScale.y, sz = target.localScale.z
        };

        /// <summary>Snapshot this live structure into its persisted record.</summary>
        public PlacedStructureData ToSaveData()
        {
            var data = new PlacedStructureData(itemId, gridCell.x, gridCell.y, yawSteps, level, yawOffset, worldY, wallMounted);
            if (!string.IsNullOrEmpty(authoredSourceId))
            {
                data.authoredSourceId = authoredSourceId;
                data.authoredPose = CaptureAuthoredPose(transform);
            }
            return data;
        }

        // ── F8-39 TEARDOWN / HIDE MONITOR (towers vanish on death, all return on next placement) ──
        // The ticket's split: do the placed structures get DESTROYED / HIDDEN when the hero dies
        // (a death-path teardown), or does the respawn simply skip the visual rebuild? These
        // MonoBehaviour lifecycle hooks answer it from data: every time a placed structure is
        // destroyed or disabled we log WHO did it (stack-derived caller) with a [Flow:Structures]
        // line, and — while the DeathTrace forensic window is live — an additional [Flow:DeathTrace]
        // line so the death capture shows the teardown inline with the death sequence. A DESTROY
        // whose caller is ClearLoaded/Rebuild (a controlled mass-rebuild) is EXPECTED; a destroy/
        // disable attributed to a scene-unload or a death listener during the window is the defect.
        // Instrumentation only — no gameplay state is read here to make a decision.
        private static bool s_appQuitting;   // suppress the end-of-play teardown storm

        private void OnDisable()
        {
            if (s_appQuitting || !FlowTrace.Enabled) return;
            // OnDisable fires on a genuine SetActive(false)/hide AND on scene-unload/destroy. The
            // caller frame distinguishes "someone hid my tower" from ordinary teardown.
            string by = DeathTrace.Caller(1);
            FlowTrace.Throttle("Structures", "hide/" + itemId, 1f,
                $"PlacedStructure HIDDEN/disabled id='{itemId}' cell=({gridCell.x},{gridCell.y}) " +
                $"activeInHierarchy={gameObject.activeInHierarchy} by {by}");
            if (DeathTrace.Active)
                DeathTrace.Note($"STRUCTURE HIDDEN/disabled: id='{itemId}' cell=({gridCell.x},{gridCell.y}) by {by} " +
                                "— if the hero just died, THIS is a tower vanishing on death.");
        }

        private void OnDestroy()
        {
            if (s_appQuitting || !FlowTrace.Enabled) return;
            string by = DeathTrace.Caller(1);
            FlowTrace.Step("Structures",
                $"PlacedStructure DESTROYED id='{itemId}' cell=({gridCell.x},{gridCell.y}) by {by} " +
                "(ClearLoaded/Rebuild = expected mass-rebuild; any other caller during a death is the F8-39 teardown).");
            if (DeathTrace.Active)
                DeathTrace.Note($"STRUCTURE DESTROYED: id='{itemId}' cell=({gridCell.x},{gridCell.y}) by {by} " +
                                "— a placed tower is being torn down inside the death window.");
        }

        private void OnApplicationQuit() => s_appQuitting = true;

        // ── Selection highlight (WO-108 P2) ──────────────────────────────────────
        // A non-destructive emissive tint via a shared MaterialPropertyBlock — the
        // same proven approach GhostPreview uses, so it never leaks a material
        // instance and restores cleanly on deselect.

        // Base selection tint (warm cyan). Owner felt-test 2026-07-16: the placed-structure
        // glow was too subtle on device - the selected building should visibly GLOW. The
        // emissive is now driven at HDR intensity (values > 1) so the project's global bloom
        // turns it into a real glow, and Update() breathes the intensity while selected.
        private static readonly Color s_highlight = new Color(0.35f, 0.90f, 1f, 1f);
        private const float HighlightMinIntensity = 1.8f;   // dim end of the breathing pulse
        private const float HighlightMaxIntensity = 3.4f;   // bright end (HDR - feeds bloom)
        private readonly List<Renderer> _renderers = new List<Renderer>();
        private MaterialPropertyBlock _mpb;
        private bool _highlighted;

        /// <summary>Toggle the selection highlight (HDR emissive glow) on this structure.</summary>
        public void SetHighlighted(bool on)
        {
            if (_highlighted == on) return;
            _highlighted = on;
            FlowTrace.Step("Select", "placed-structure glow " + (on ? "ON" : "off") + " id='" + itemId + "'");

            if (_mpb == null) _mpb = new MaterialPropertyBlock();
            if (_renderers.Count == 0)
                _renderers.AddRange(GetComponentsInChildren<Renderer>(true));

            foreach (var r in _renderers)
            {
                if (r == null) continue;
                r.GetPropertyBlock(_mpb);
                if (on)
                {
                    // Start bright; Update() then breathes it. HDR (>1) so bloom glows it.
                    _mpb.SetColor("_EmissionColor", s_highlight * HighlightMaxIntensity);
                }
                else
                {
                    _mpb.SetColor("_EmissionColor", Color.black);
                }
                r.SetPropertyBlock(_mpb);
            }

            // DEF-208 — deselect clears the emissive to black, which would also wipe the
            // tier accent tint. Re-apply the tier visual so the upgraded look persists.
            if (!on && TierVisual != null)
                TierVisual.Refresh();
        }

        // Breathing pulse for the selection glow: while highlighted, ease the HDR emissive
        // intensity between min/max so the selected building visibly "glows/pulses" rather
        // than sitting under a flat tint. Early-outs when not selected, so only the one
        // selected structure does any per-frame work. Unscaled time so it breathes even if
        // Build Mode pauses gameplay time.
        private void Update()
        {
            if (!_highlighted || _mpb == null || _renderers.Count == 0) return;
            float k = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 3.0f);
            Color emis = s_highlight * Mathf.Lerp(HighlightMinIntensity, HighlightMaxIntensity, k);
            for (int i = 0; i < _renderers.Count; i++)
            {
                var r = _renderers[i];
                if (r == null) continue;
                r.GetPropertyBlock(_mpb);
                _mpb.SetColor("_EmissionColor", emis);
                r.SetPropertyBlock(_mpb);
            }
        }
    }
}

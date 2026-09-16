// =============================================================================
// PlacementGrid — the shared cell-occupancy grid for Build Mode (WO-108 P1).
// -----------------------------------------------------------------------------
// Assembly: DeNelle.Village   Namespace: DeNelle.Village
//
// Promotes TowerPlacementSystem's radius snap into a real 2D cell grid with
// EXACT multi-cell footprint occupancy (build-mode-architecture.md §3). 3 m cells
// match the polyperfect 3×3 modular wall grain; the interior is 28×22 cells
// (84×66 m) centred on the village origin (0,0,0 = Heart of Elarion).
//
// HEADLESS-PURE seam (principle #3): CanPlace / WorldToCell / CellToWorld are
// pure functions over the grid state + config — no scene raycast, no Camera — so
// an async-raid server can re-verify a layout headless. The Y of a placed object
// comes from the placement raycast (TowerPlacementSystem.IsValidSurface), not the
// grid, so the grid stays a flat XZ planner.
//
// Singleton: BuildModeController ensures one exists on Enter.
// =============================================================================

using System.Collections.Generic;
using UnityEngine;
using DeNelle.Core.Diagnostics;

namespace DeNelle.Village
{
    /// <summary>
    /// A 2D occupancy grid over the village interior. Tracks which cells are taken,
    /// converts world ⇄ cell, snaps a world point to a cell centre, and validates a
    /// multi-cell footprint. Pure/headless apart from the optional overlay mesh.
    /// </summary>
    public sealed class PlacementGrid : MonoBehaviour
    {
        public static PlacementGrid Instance { get; private set; }

        [Header("Grid")]
        [Tooltip("Cell edge in metres — 3 m matches polyperfect 3×3 modular walls.")]
        public float cellSize = 3f;

        // FOOTPRINT EXPANDED (owner F8 2026-06-28 "expand the build footprint enough to get
        // north and south towers"): the castle perimeter is ~square — the S/N walls sit at
        // z≈±40.5 (castle-south-recipe.json: gate z=-40.6, walls -40.55/-40.93, corner
        // tower -40.03) and the E/W walls at x≈±42. The old grid was 84m×66m (±42 X, ±33 Z),
        // so the N/S walls fell ~7.5m OUTSIDE the buildable Z range → "can't place towers"
        // there, while E/W (at the ±42 X edge) worked. Now a symmetric 90m×90m grid (±45 on
        // BOTH axes) reaches every wall AND the ±42.33 corner towers. Placement is still gated
        // by surface/occupancy/gate-lane checks, so a bigger grid doesn't allow silly builds.
        [Tooltip("Cells across X — 90 m / 3 m = 30 (±45, reaches the E/W walls + corner towers).")]
        public int gridWidth = 30;

        // NORTH EXTENSION (owner 2026-07-16 "cannot go forward north"; MEASURED by BuildNorthDiag:
        // grid north edge was Z+45.0, CastleSide_North northmost face Z+44.2 — the buildable block
        // ended EXACTLY at the wall, and the camera clamp is the same bounds, so north dead-ended at
        // the wall). Grown NORTH ONLY (see Awake: south edge stays fixed at -45 so existing saved
        // CELLS keep their world position — placements persist as cells): 30 -> 40 = +10 cells /
        // +30 m of buildable land + camera travel north of the wall. gridWidth unchanged.
        [Tooltip("Cells across Z — 40 (south edge fixed at -45; extends NORTH to +75, ~31 m past the north wall).")]
        public int gridHeight = 40;

        [Tooltip("World-space XZ of the grid's (0,0) cell-corner. Default centres the grid on the origin.")]
        public Vector3 origin = Vector3.zero;

        // EDGE-ALLOW (owner hard requirement) — how many cells in from the grid border are
        // BLOCKED for placement. 0 = the whole grid is buildable INCLUDING the boundary cells,
        // so a perimeter wall run reaches the map edge. Was implicitly clamped to the interior
        // before; keep this at 0 unless the owner asks for an inset. A negative value is treated
        // as 0 (never inset).
        [Tooltip("Cells of border to block from placement. 0 = edge-allow (perimeter walls reach the boundary).")]
        public int edgeMargin = 0;

        // occupied[x,z] → the structure id occupying that cell, or null if free.
        private string[,] _occupied;

        // Overlay (build-mode only) — a single quad ground-decal toggled on Enter.
        private GameObject _overlay;

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;
            EnsureGrid();
            // Centre the grid on X, but ANCHOR the south (−Z) edge at a FIXED −45 so the grid grows
            // NORTH ONLY as gridHeight increases (owner 2026-07-16). Save-safety: placements persist
            // as CELLS mapped by origin; keeping origin fixed (X centered, south −45 — identical to
            // the old 30×30 runtime origin) means every existing saved structure keeps its exact
            // world position, while the added north cells (30..gridHeight-1) appear beyond the wall.
            // (Do NOT recompute origin.z from gridHeight — that would shift every saved cell.)
            const float SouthEdgeZ = -45f;   // = -(original 30 cells)*3 m/2 ; the walled base's south edge
            if (origin == Vector3.zero)
                origin = new Vector3(-gridWidth * cellSize * 0.5f, 0f, SouthEdgeZ);
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        private void EnsureGrid()
        {
            if (_occupied == null ||
                _occupied.GetLength(0) != gridWidth ||
                _occupied.GetLength(1) != gridHeight)
            {
                _occupied = new string[Mathf.Max(1, gridWidth), Mathf.Max(1, gridHeight)];
            }
        }

        // ── Conversions (pure) ────────────────────────────────────────────────

        /// <summary>World XZ → the cell it falls in (clamped reads still return the raw cell).</summary>
        public Vector2Int WorldToCell(Vector3 worldPos)
        {
            int x = Mathf.FloorToInt((worldPos.x - origin.x) / cellSize);
            int z = Mathf.FloorToInt((worldPos.z - origin.z) / cellSize);
            return new Vector2Int(x, z);
        }

        /// <summary>Cell → the world-space centre of that cell (Y from the grid plane).</summary>
        public Vector3 CellToWorld(Vector2Int cell)
        {
            float x = origin.x + (cell.x + 0.5f) * cellSize;
            float z = origin.z + (cell.y + 0.5f) * cellSize;
            return new Vector3(x, origin.y, z);
        }

        /// <summary>Snap a world point to its cell centre, preserving the incoming Y.</summary>
        public Vector3 SnapToGrid(Vector3 worldPos)
        {
            var cell = WorldToCell(worldPos);
            var snapped = CellToWorld(cell);
            snapped.y = worldPos.y;   // keep the surface height from the placement ray
            return snapped;
        }

        // ── Occupancy (pure) ──────────────────────────────────────────────────

        /// <summary>True when every cell of <paramref name="footprint"/> at
        /// <paramref name="cell"/> is in-bounds and currently free.</summary>
        public bool CanPlace(Vector2Int cell, Vector2Int footprint)
        {
            EnsureGrid();
            int fw = Mathf.Max(1, footprint.x);
            int fh = Mathf.Max(1, footprint.y);
            // EDGE-ALLOW — the buildable bounds. margin 0 = the full grid (incl. boundary
            // cells) is placeable so perimeter walls reach the map edge.
            int m = Mathf.Max(0, edgeMargin);
            int minX = m, minZ = m, maxX = gridWidth - m, maxZ = gridHeight - m;
            for (int dx = 0; dx < fw; dx++)
            {
                for (int dz = 0; dz < fh; dz++)
                {
                    int x = cell.x + dx, z = cell.y + dz;
                    if (x < minX || z < minZ || x >= maxX || z >= maxZ) return false;
                    if (!string.IsNullOrEmpty(_occupied[x, z])) return false;
                }
            }
            return true;
        }

        /// <summary>
        /// The structure id occupying <paramref name="cell"/>, or null when free /
        /// out of bounds. F8 2026-07-30 (anonymous Occupied storm): lets the placement
        /// gate NAME the blocker in its reject trace instead of a bare "Occupied".
        /// </summary>
        public string OccupantAt(Vector2Int cell)
        {
            EnsureGrid();
            if (cell.x < 0 || cell.y < 0 || cell.x >= gridWidth || cell.y >= gridHeight) return null;
            string id = _occupied[cell.x, cell.y];
            return string.IsNullOrEmpty(id) ? null : id;
        }

        /// <summary>
        /// True when every cell of <paramref name="footprint"/> at <paramref name="cell"/>
        /// is within the buildable bounds (ignores occupancy). WO-394 — lets the placement
        /// gate tell "outside the build area" apart from "no space here" (cells taken).
        /// </summary>
        public bool InBounds(Vector2Int cell, Vector2Int footprint)
        {
            int fw = Mathf.Max(1, footprint.x);
            int fh = Mathf.Max(1, footprint.y);
            int m = Mathf.Max(0, edgeMargin);
            int minX = m, minZ = m, maxX = gridWidth - m, maxZ = gridHeight - m;
            for (int dx = 0; dx < fw; dx++)
                for (int dz = 0; dz < fh; dz++)
                {
                    int x = cell.x + dx, z = cell.y + dz;
                    if (x < minX || z < minZ || x >= maxX || z >= maxZ) return false;
                }
            return true;
        }

        /// <summary>Mark a footprint's cells occupied by <paramref name="structureId"/>.</summary>
        public void Occupy(Vector2Int cell, Vector2Int footprint, string structureId)
        {
            FlowTrace.Step("Grid", $"Occupy cell=({cell.x},{cell.y}) footprint=({footprint.x}x{footprint.y}) id='{structureId ?? "<null>"}'");
            EnsureGrid();
            int fw = Mathf.Max(1, footprint.x);
            int fh = Mathf.Max(1, footprint.y);
            for (int dx = 0; dx < fw; dx++)
            {
                for (int dz = 0; dz < fh; dz++)
                {
                    int x = cell.x + dx, z = cell.y + dz;
                    if (x < 0 || z < 0 || x >= gridWidth || z >= gridHeight) continue;
                    _occupied[x, z] = structureId;
                }
            }
        }

        /// <summary>Clear a footprint's cells (sell / move).</summary>
        public void Free(Vector2Int cell, Vector2Int footprint)
        {
            EnsureGrid();
            int fw = Mathf.Max(1, footprint.x);
            int fh = Mathf.Max(1, footprint.y);
            for (int dx = 0; dx < fw; dx++)
            {
                for (int dz = 0; dz < fh; dz++)
                {
                    int x = cell.x + dx, z = cell.y + dz;
                    if (x < 0 || z < 0 || x >= gridWidth || z >= gridHeight) continue;
                    _occupied[x, z] = null;
                }
            }
        }

        /// <summary>Wipe all occupancy (re-seeding the grid from a fresh layout).</summary>
        public void ClearAll()
        {
            FlowTrace.Step("Grid", $"ClearAll — wiping {gridWidth}x{gridHeight} occupancy");
            _occupied = new string[Mathf.Max(1, gridWidth), Mathf.Max(1, gridHeight)];
        }

        /// <summary>
        /// Metres → whole cells on ONE axis. Shared by square and CoC non-square paths.
        /// </summary>
        public int MetresToCells(float metres)
            => Mathf.Max(1, Mathf.CeilToInt(Mathf.Max(0.01f, metres) / cellSize));

        /// <summary>
        /// LEGACY square claim from a single scalar (max axis). Kept for regressions and
        /// any caller that still passes one number. Prefer
        /// <see cref="FootprintCells(float, float)"/> / <see cref="FootprintCells(Vector2)"/>
        /// for CoC-style thin structures (WO-986).
        /// </summary>
        public Vector2Int FootprintCells(float footprintMetres)
        {
            int cells = MetresToCells(footprintMetres);
            return new Vector2Int(cells, cells);
        }

        /// <summary>
        /// WO-986 CoC-style: claim cells from independent X and Z metres (Vector2.x = width,
        /// .y = depth). Named Vector2 so it cannot collide with the (metres, yawDegrees)
        /// scalar rotation overload.
        /// </summary>
        public Vector2Int FootprintCells(Vector2 footprintMetres)
            => new Vector2Int(MetresToCells(footprintMetres.x), MetresToCells(footprintMetres.y));

        /// <summary>
        /// WO-673 L5 + WO-986 — rotation-honest claim for a SQUARE scalar footprint
        /// (legacy). At cardinals = <see cref="FootprintCells(float)"/>; at diagonals
        /// inflates by |sin|+|cos| (√2 at 45°). Prefer the Vector2 overload for thin props.
        /// </summary>
        public Vector2Int FootprintCells(float footprintMetres, float yawDegrees)
        {
            float yawMod = Mathf.Abs(Mathf.DeltaAngle(0f, yawDegrees)) % 90f;
            bool cardinal = yawMod < 0.01f || yawMod > 89.99f;
            if (cardinal) return FootprintCells(footprintMetres);

            float rad = yawDegrees * Mathf.Deg2Rad;
            float inflate = Mathf.Abs(Mathf.Sin(rad)) + Mathf.Abs(Mathf.Cos(rad));
            return FootprintCells(footprintMetres * inflate);
        }

        /// <summary>
        /// WO-986 CoC-style rotation-honest claim for a NON-SQUARE footprint.
        /// Rotates the (width×depth) rectangle by yaw and claims the axis-aligned AABB
        /// in metres, then converts to cells. Cardinals are byte-identical to the
        /// unrotated Vector2 claim (trig epsilon snapped).
        /// </summary>
        public Vector2Int FootprintCells(Vector2 footprintMetres, float yawDegrees)
        {
            float w = Mathf.Max(0.01f, footprintMetres.x);
            float d = Mathf.Max(0.01f, footprintMetres.y);
            float yawMod = Mathf.Abs(Mathf.DeltaAngle(0f, yawDegrees)) % 90f;
            bool cardinal = yawMod < 0.01f || yawMod > 89.99f;
            if (cardinal)
            {
                // At 90°/270° the rectangle's world AABB SWAPS axes: a 6×2 prop rotated a
                // quarter turn covers 2 m of world X and 6 m of world Z. Returning (w,d)
                // here transposed the claim (review fba0b1079..0e4690036 finding #2) —
                // the mesh covered free cells while blocking cells it never touched, and
                // the wrong claim replayed from the save. The general |c|·w+|s|·d branch
                // below already yields the swap at exact cardinals; mirror it here.
                bool quarterTurn = Mathf.RoundToInt(Mathf.Abs(Mathf.DeltaAngle(0f, yawDegrees)) / 90f) % 2 == 1;
                return FootprintCells(quarterTurn ? new Vector2(d, w) : new Vector2(w, d));
            }

            float rad = yawDegrees * Mathf.Deg2Rad;
            float absC = Mathf.Abs(Mathf.Cos(rad));
            float absS = Mathf.Abs(Mathf.Sin(rad));
            // AABB of a rotated rectangle: |c|·w + |s|·d on each world axis.
            float worldW = absC * w + absS * d;
            float worldD = absS * w + absC * d;
            return FootprintCells(new Vector2(worldW, worldD));
        }

        // ── Overlay (build-mode only) ──────────────────────────────────────────

        /// <summary>Show/hide a translucent grid overlay over the build area.</summary>
        public void SetGridVisible(bool visible)
        {
            if (visible)
            {
                if (_overlay == null) _overlay = BuildOverlay();
                if (_overlay != null) _overlay.SetActive(true);
            }
            else if (_overlay != null)
            {
                _overlay.SetActive(false);
            }
        }

        private GameObject BuildOverlay()
        {
            // EDGES ONLY (owner: "no fill, just edges") — a boundary outline of the buildable area,
            // NOT a solid fill, so the ground + the placement ghost stay visible underneath.
            var go = new GameObject("PlacementGridOverlay");
            // Imported owned/practice grids stay inactive to avoid replacing the global singleton.
            // Their explicitly requested build overlay belongs under the active construction root.
            go.transform.SetParent(gameObject.activeInHierarchy ? transform : transform.parent, false);

            float y  = origin.y + 0.05f;
            float x0 = origin.x,                         x1 = origin.x + gridWidth  * cellSize;
            float z0 = origin.z,                         z1 = origin.z + gridHeight * cellSize;

            var lr = go.AddComponent<LineRenderer>();
            lr.useWorldSpace    = true;
            lr.loop             = true;
            lr.positionCount    = 4;
            lr.SetPositions(new[]
            {
                new Vector3(x0, y, z0), new Vector3(x1, y, z0),
                new Vector3(x1, y, z1), new Vector3(x0, y, z1),
            });
            lr.widthMultiplier   = 0.35f;
            lr.numCornerVertices = 2;
            lr.alignment         = LineAlignment.TransformZ;   // ribbon lies flat on the ground
            lr.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            lr.shadowCastingMode  = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows     = false;

            var shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Sprites/Default");
            var mat = new Material(shader);
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", new Color(0.25f, 0.7f, 1f, 0.9f));
            lr.sharedMaterial = mat;
            return go;
        }
    }
}

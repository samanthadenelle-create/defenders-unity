// =============================================================================
// CollectorStackPropCatalogBuilder - creates + wires Resources/Collectors/
// CollectorStackPropCatalog (WO-903 support lane, 2026-08-16).
// -----------------------------------------------------------------------------
// Assembly: DeNelle.Editor   Namespace: DeNelle.Editor
// Marker:   COLLECTOR_PROP_CATALOG_BUILT / COLLECTOR_PROP_CATALOG_FAIL
//
// WHY THIS EXISTS. CollectorStackPropCatalog.cs has shipped since WO-665a with a
// documented "place the asset at Assets/Resources/Collectors/..." instruction and
// NOBODY EVER PLACED IT. Assets/Resources/Collectors/ did not exist; git history
// shows the asset was never added and never deleted on any branch. So
// CollectorStackView.EnsureCatalog() has ALWAYS resolved null and every collector
// in the town has ALWAYS drawn the abstract fill bar - the diegetic prop pile, the
// headline of WO-665a, had never rendered once. This builder is the missing step.
//
// THE PICKS ARE THE OWNER'S, NOT THIS SCRIPT'S. Stated verbatim 2026-08-16:
// "log sack of flour and iron bar". Resolved to the KayKit Resource Bits pack:
//   Wood  -> Wood_Log_A.fbx
//   Food  -> Food_Flour.fbx
//   Iron  -> Iron_Bar.fbx
// Crystals is DELIBERATELY LEFT UNWIRED - the owner named three props, not four,
// and the standing rule is that the owner tags art and the CLI maps it verbatim,
// never substitutes a pick of its own. An unwired resource is not a defect: it
// takes the abstract-bar fallback, which is the designed graceful path. The
// obvious candidates when she does name one are Gem_Medium.fbx / Gems_Pile_Small.fbx
// in the same pack. See docs/ITEM_ICON_AND_RESOURCE_ASSET_MAP.md.
//
// THE PACK IS GITIGNORED (.gitignore:106 "/Assets/Models/*") AND THAT IS FINE HERE.
// The committed .asset carries FBX GUIDs that resolve to nothing on a machine
// without the pack. Unity then deserializes Entry.Prop as null, TryGet returns
// false on its `entry.Prop != null` line, and CollectorStackView falls straight
// back to the fill bar - the SAME path it has been taking for months, verified at
// source, no throw and no broken render. The alternative (Resources.Load the props
// by path at runtime) is not actually available: the FBX live under Assets/Models,
// not under a Resources folder, so there is no runtime path to load. Copying them
// into Resources would import gitignored pack art into git, against the standing
// big-art-out-of-git policy (owner ruling 2026-07-15). Recording the GUIDs and
// degrading gracefully is the correct trade, and CollectorStackPropCatalogRegression
// pins BOTH branches so neither can rot.
//
// IDEMPOTENT. Re-running never clobbers a divergent pick: a row whose Prop is
// already a DIFFERENT non-null asset is left exactly as it is and named in a
// warning. Only null/missing/matching rows are (re)written.
//
// SCALE IS MEASURED, NOT GUESSED. Each prop's PropScale is fitted to the grid cell
// CollectorStackView derives from SlotSize (slot.x/4 wide, slot.y/5 tall - 20 steps
// in a 4-column brick grid), using the model's own mesh bounds. This mirrors the
// normalized fit-to-height rule (DEF-208 / WO-751) rather than hand-picking a
// number that happens to look right for one of the three props.
//
// Run:  Defenders > Art > Build Collector Stack Prop Catalog
// Batch: DeNelle.Editor.CollectorStackPropCatalogBuilder.Build
// =============================================================================

using System;
using System.Collections.Generic;
using System.Text;
using DeNelle.Core.Diagnostics;
using DeNelle.Village.Buildings.Progression;
using UnityEditor;
using UnityEngine;

namespace DeNelle.Editor
{
    public static class CollectorStackPropCatalogBuilder
    {
        private const string FlowSys = "CollectorProps";

        /// <summary>Folder the catalog asset lives in (must be under a Resources root).</summary>
        public const string CatalogFolder = "Assets/Resources/Collectors";

        /// <summary>Full asset path the runtime's Resources.Load resolves to.</summary>
        public const string CatalogAssetPath = CatalogFolder + "/CollectorStackPropCatalog.asset";

        /// <summary>KayKit Resource Bits FBX root (gitignored pack - absence is a warning, never an error).</summary>
        public const string PackRoot = "Assets/Models/KayKit/KayKit Resource Bits 1.0/Assets/fbx(unity)/";

        private const string MarkerOk   = "COLLECTOR_PROP_CATALOG_BUILT";
        private const string MarkerFail = "COLLECTOR_PROP_CATALOG_FAIL";

        /// <summary>Default pile footprint (world units) - matches CollectorStackView's own fallback.</summary>
        private static readonly Vector3 DefaultSlotSize = new Vector3(1.2f, 1.0f, 0.6f);

        /// <summary>Grid the view lays props out on: 4 columns, ceil(20/4)=5 rows.</summary>
        private const int GridColumns = 4;
        private const int StepCount   = 20;

        /// <summary>Leave a little air in each cell so neighbouring props do not interpenetrate.</summary>
        private const float CellFill = 0.85f;

        /// <summary>The owner's selection (2026-08-16), one row per resource she named.</summary>
        private struct Pick
        {
            public HarvestResource Resource;
            public string FileName;
            public Pick(HarvestResource r, string f) { Resource = r; FileName = f; }
        }

        private static readonly Pick[] OwnerPicks =
        {
            new Pick(HarvestResource.Wood, "Wood_Log_A.fbx"),
            new Pick(HarvestResource.Stone, "Food_Flour.fbx"),
            new Pick(HarvestResource.Iron, "Iron_Bar.fbx"),
            // HarvestResource.Crystals: deliberately absent - see the header. Falls back to the bar.
        };

        [MenuItem("Defenders/Art/Build Collector Stack Prop Catalog")]
        public static void BuildMenu() { Build(); }

        /// <summary>Batchmode entry point. Never throws - reports through the markers.</summary>
        public static void Build()
        {
            try
            {
                BuildCore();
            }
            catch (Exception ex)
            {
                Debug.LogError(MarkerFail + " - builder threw " + ex.GetType().Name + ": " + ex.Message);
            }
        }

        private static void BuildCore()
        {
            using var _scope = FlowTrace.Enter(FlowSys, "CollectorStackPropCatalogBuilder.BuildCore");

            var log = new StringBuilder();
            log.AppendLine("--- COLLECTOR STACK PROP CATALOG (owner selection 2026-08-16) ---");

            EnsureFolder();

            var catalog = AssetDatabase.LoadAssetAtPath<CollectorStackPropCatalog>(CatalogAssetPath);
            bool created = false;
            if (catalog == null)
            {
                catalog = ScriptableObject.CreateInstance<CollectorStackPropCatalog>();
                catalog.Entries = Array.Empty<CollectorStackPropCatalog.Entry>();
                AssetDatabase.CreateAsset(catalog, CatalogAssetPath);
                created = true;
                FlowTrace.Step(FlowSys, "created catalog asset at " + CatalogAssetPath);
            }
            else
            {
                FlowTrace.Step(FlowSys, "reusing existing catalog asset at " + CatalogAssetPath +
                                        " (" + (catalog.Entries != null ? catalog.Entries.Length : 0) + " row(s))");
            }

            var rows = new List<CollectorStackPropCatalog.Entry>(
                catalog.Entries ?? Array.Empty<CollectorStackPropCatalog.Entry>());

            int wired = 0, kept = 0, missing = 0, diverged = 0;

            for (int i = 0; i < OwnerPicks.Length; i++)
            {
                var pick = OwnerPicks[i];
                string path = PackRoot + pick.FileName;

                GameObject prop = null;
                Guard.Try(FlowSys, "load " + pick.FileName,
                    () => { prop = AssetDatabase.LoadAssetAtPath<GameObject>(path); });

                if (prop == null)
                {
                    // Canon: a missing gitignored pack asset is a WARNING, never an error.
                    // The row is still written (or left) so the intent is recorded; the view
                    // sees a null Prop and takes the fill-bar fallback.
                    missing++;
                    FlowTrace.Warn(FlowSys, "pack prop NOT on this machine: " + path +
                                            " - " + pick.Resource + " keeps the abstract fill-bar fallback");
                    Debug.LogWarning("[collector-props] " + path + " not found (KayKit Resource Bits is " +
                                     "gitignored; re-import the pack). " + pick.Resource +
                                     " will use the abstract fill-bar fallback.");
                    log.Append("  MISSING ").Append(pick.Resource).Append(" -> ").AppendLine(path);
                    EnsureRowExists(rows, pick.Resource);
                    continue;
                }

                int idx = IndexOf(rows, pick.Resource);
                if (idx >= 0 && rows[idx].Prop != null && rows[idx].Prop != prop)
                {
                    // Idempotence guard: someone (or a later owner pick) wired something
                    // else here. Do NOT clobber it silently - name it and move on.
                    diverged++;
                    string existing = AssetDatabase.GetAssetPath(rows[idx].Prop);
                    FlowTrace.Warn(FlowSys, "row " + pick.Resource + " already wired to a DIFFERENT prop (" +
                                            existing + "); leaving it alone rather than overwriting with " + path);
                    Debug.LogWarning("[collector-props] " + pick.Resource + " is already wired to '" + existing +
                                     "', which is not the 2026-08-16 owner pick '" + path + "'. LEFT AS IS - " +
                                     "delete the row (or the asset) and re-run if you want the owner pick back.");
                    log.Append("  DIVERGED ").Append(pick.Resource).Append(" keeps ").AppendLine(existing);
                    kept++;
                    continue;
                }

                float scale = FitScale(prop, DefaultSlotSize);

                var entry = new CollectorStackPropCatalog.Entry
                {
                    Resource  = pick.Resource,
                    Prop      = prop,
                    PropScale = scale,
                    SlotSize  = DefaultSlotSize,
                };

                if (idx >= 0) rows[idx] = entry; else rows.Add(entry);
                wired++;
                FlowTrace.Step(FlowSys, "wired " + pick.Resource + " -> " + pick.FileName +
                                        " scale=" + scale.ToString("0.###"));
                log.Append("  WIRED   ").Append(pick.Resource).Append(" -> ").Append(path)
                   .Append("  scale=").Append(scale.ToString("0.###")).AppendLine();
            }

            catalog.Entries = rows.ToArray();
            EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            log.Append("rows=").Append(rows.Count)
               .Append(" wired=").Append(wired)
               .Append(" kept=").Append(kept)
               .Append(" diverged=").Append(diverged)
               .Append(" missingOnThisMachine=").Append(missing)
               .Append(" created=").Append(created ? "yes" : "no")
               .AppendLine();

            Debug.Log(log.ToString() + MarkerOk + " - " + CatalogAssetPath + " has " + rows.Count +
                      " row(s); " + wired + " wired from the KayKit pack, " + missing +
                      " left on the fill-bar fallback because the gitignored pack is not on this machine. " +
                      "Crystals is unwired ON PURPOSE (owner named three props, not four).");
        }

        // ---------------------------------------------------------------------
        //  helpers
        // ---------------------------------------------------------------------

        private static void EnsureFolder()
        {
            if (AssetDatabase.IsValidFolder(CatalogFolder)) return;
            if (!AssetDatabase.IsValidFolder("Assets/Resources"))
                AssetDatabase.CreateFolder("Assets", "Resources");
            AssetDatabase.CreateFolder("Assets/Resources", "Collectors");
            FlowTrace.Step(FlowSys, "created folder " + CatalogFolder);
        }

        private static int IndexOf(List<CollectorStackPropCatalog.Entry> rows, HarvestResource res)
        {
            for (int i = 0; i < rows.Count; i++)
                if (rows[i].Resource == res) return i;
            return -1;
        }

        /// <summary>Add a placeholder (null-Prop) row so the resource's intent is recorded even
        /// when the gitignored pack is absent. TryGet still returns false on it, so the view
        /// takes the fill-bar fallback exactly as before.</summary>
        private static void EnsureRowExists(List<CollectorStackPropCatalog.Entry> rows, HarvestResource res)
        {
            if (IndexOf(rows, res) >= 0) return;
            rows.Add(new CollectorStackPropCatalog.Entry
            {
                Resource  = res,
                Prop      = null,
                PropScale = 1f,
                SlotSize  = DefaultSlotSize,
            });
        }

        /// <summary>
        /// Uniform scale that makes this model fit one cell of the view's 4-column brick grid.
        /// Measured from the model's own mesh bounds - the three picks (a log, a flour sack, an
        /// iron bar) have wildly different native sizes, and a hand-picked constant that suits
        /// one of them makes the other two either interpenetrate or float apart.
        /// </summary>
        public static float FitScale(GameObject prop, Vector3 slotSize)
        {
            if (prop == null) return 1f;

            int rows = Mathf.CeilToInt(StepCount / (float)GridColumns);
            var cell = new Vector3(
                slotSize.x / Mathf.Max(1, GridColumns),
                slotSize.y / Mathf.Max(1, rows),
                slotSize.z);

            Bounds b = default;
            bool any = false;
            Guard.Try(FlowSys, "measure bounds of " + prop.name, () =>
            {
                var root = prop.transform;
                var filters = prop.GetComponentsInChildren<MeshFilter>(true);
                for (int i = 0; i < filters.Length; i++)
                {
                    var mesh = filters[i].sharedMesh;
                    if (mesh == null) continue;
                    var toRoot = root.worldToLocalMatrix * filters[i].transform.localToWorldMatrix;
                    var mb = mesh.bounds;
                    for (int c = 0; c < 8; c++)
                    {
                        var corner = new Vector3(
                            (c & 1) == 0 ? mb.min.x : mb.max.x,
                            (c & 2) == 0 ? mb.min.y : mb.max.y,
                            (c & 4) == 0 ? mb.min.z : mb.max.z);
                        var p = toRoot.MultiplyPoint3x4(corner);
                        if (!any) { b = new Bounds(p, Vector3.zero); any = true; }
                        else b.Encapsulate(p);
                    }
                }
            });

            if (!any || b.size.sqrMagnitude < 1e-8f)
            {
                FlowTrace.Warn(FlowSys, "no measurable mesh bounds on " + prop.name + " - defaulting scale to 1");
                return 1f;
            }

            float sx = b.size.x > 1e-5f ? cell.x / b.size.x : float.MaxValue;
            float sy = b.size.y > 1e-5f ? cell.y / b.size.y : float.MaxValue;
            float sz = b.size.z > 1e-5f ? cell.z / b.size.z : float.MaxValue;
            float s = Mathf.Min(sx, Mathf.Min(sy, sz)) * CellFill;

            // Never blow a prop up past its authored size, and never collapse it to nothing.
            return Mathf.Clamp(s, 0.05f, 1f);
        }
    }
}

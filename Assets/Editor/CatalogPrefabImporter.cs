// =============================================================================
// CatalogPrefabImporter — copies the defensive-kit _M prefabs out of the
// (gitignored) Polyperfect pack into Assets/StructureContent/ so the catalog's
// Resources.Load(visualPrefabPath) can actually find them at runtime.
// -----------------------------------------------------------------------------
// The _M prefabs live at
//   Assets/polyperfect/Low Poly Ultimate Pack/_M/Prefabs_M/Medieval_M/<Name>.prefab
// which is NOT under a Resources/ folder, so the runtime catalog loader can't reach
// them. AssetDatabase.CopyAsset duplicates the prefab into Resources/Structures/ and
// REWRITES its dependency GUIDs into the copy, so the mesh/material references stay
// intact (the source pack stays untouched and gitignored).
//
// Idempotent: a prefab already present at the destination is skipped, so re-running
// after a fresh pack re-import is safe and cheap.
//
//   Defenders > Catalog > Copy Kit Prefabs To Resources
//   (batchmode: DeNelle.Editor.CatalogPrefabImporter.CopyKitToResources)
// =============================================================================

using System.IO;
using UnityEditor;
using UnityEngine;

namespace DeNelle.Editor
{
    public static class CatalogPrefabImporter
    {
        // Source pack root (gitignored — owner re-imports). Trailing slash matters.
        // Prefabs live under per-category folders, e.g. .../Prefabs_M/Medieval_M/<Name>.prefab.
        private const string SrcRoot =
            "Assets/polyperfect/Low Poly Ultimate Pack/_M/Prefabs_M/";

        // Destination Resources folder (so visualPrefabPath = "Structures/<Name>" loads).
        private const string DstDir = DeNelle.Core.AssetRoots.StructureContent + "/";

        // One kit prefab: file stem (no .prefab) + the category folder it lives in.
        // Root defaults to the _M tier (CLAUDE.md §4); a per-entry root override exists
        // because the archer Tribal ladder is owner-ruled to the _T tier (catalog _bug22).
        private readonly struct KitPrefab
        {
            public readonly string Name;
            public readonly string Category; // folder under the tier root (no trailing slash)
            public readonly string Root;     // tier root ("..._M/Prefabs_M/" unless overridden)
            // Source/destination file extension. Defaults to ".prefab" because every
            // polyperfect row below IS a prefab. KayKit ships raw .fbx models with no
            // prefab wrapper, so that pack's rows override this (WO-1619, 2026-09-10).
            // CopyAsset and AssetDatabase.LoadAssetAtPath<GameObject> both treat a model
            // file exactly like a prefab, and StructureAddressablesMigrator.MarkInto
            // already probes ".fbx" alongside ".prefab" when it resolves a catalog key -
            // so an .fbx row needs no other change anywhere in the chain.
            public readonly string Ext;
            public KitPrefab(string name, string category, string root = SrcRoot, string ext = ".prefab")
            { Name = name; Category = category; Root = root; Ext = ext; }
        }

        private const string SrcRootT =
            "Assets/polyperfect/Low Poly Ultimate Pack/_T/Prefabs_T/";

        // KayKit Medieval Hexagon Pack - the kit the raid camps are dressed in
        // (RaidBaseDresser.KayFolders; RaidBaseLayoutRegression.CaseEasyDress pins the Easy
        // camp's kit as "hexagon-green"). Gitignored like every other pack, so a row sourced
        // here warns rather than errors when the pack is absent - CLAUDE.md section 4.
        private const string SrcRootKayHex =
            "Assets/Models/KayKit/KayKit Medieval Hexagon Pack 1.0.1/Assets/fbx(unity)/buildings/";

        // The defensive-kit prefabs the catalog references (owner's prefab map, WO).
        // Most live in Medieval_M; a few primitives live in other category folders.
        private static readonly KitPrefab[] KitPrefabs =
        {
            // --- already copied / verified (keep) ---
            // NO LONGER THE ARCHER LADDER (owner 2026-08-06): tower_ground_archer moved to the
            // ALL-WOOD Tower_Wooden_Watchtower / _L2 / _L3 Tripo set, which is owner-sourced,
            // git-TRACKED via per-asset .gitignore negations, and built by
            // DeNelle.Editor.WoodenWatchtowerBuilder.Build -- NOT mirrored from a pack, so it is
            // deliberately absent from this table. The four rows below stay because /Assets/
            // Resources/Structures/* is gitignored and OTHER consumers still load them from there:
            // CastleHubBuilder, VillageSceneBuilder.Walls, TowerDataSeeder, GarrisonSceneBuilder,
            // EnemyStrongholdBuilder, and the tower_wall_wizard catalog row. Dropping them would
            // leave those callers with a missing prefab on a fresh clone.
            new KitPrefab("Tower_Medieval_Wood",       "Medieval_M"),  // retired from the archer ladder; kept for other consumers
            new KitPrefab("Tower_Castle_Round",        "Medieval_M"),  // ex-archer L1 (WO-902); now CastleHubBuilder / walls / seeders
            new KitPrefab("Tower_Castle_Square",       "Medieval_M"),  // ex-archer L2 (WO-902); now CastleHubBuilder / walls / seeders
            new KitPrefab("Tower_Medieval_Big",        "Medieval_M"),  // ex-archer L3 (WO-902); still tower_wall_wizard + CastleHubBuilder
            new KitPrefab("Windmill_Medieval",         "Medieval_M"),  // mill
            new KitPrefab("Wall_Medieval_Wood",        "Medieval_M"),  // wall_wood
            new KitPrefab("Wall_Medieval_Stone",       "Medieval_M"),  // wall_stone
            new KitPrefab("Gate_Medieval_Medium",      "Medieval_M"),  // gate_stone

            // --- new (WO prefab-match table) ---
            new KitPrefab("Catapult",                  "Medieval_M"),  // tower_catapult
            new KitPrefab("Ballista",                  "Medieval_M"),  // tower_siege_tower
            new KitPrefab("Stables_Medieval",          "Medieval_M"),  // pet-house
            new KitPrefab("House_Medieval_Medium",     "Medieval_M"),  // workshop / forge
            new KitPrefab("House_Medieval_Large",      "Medieval_M"),  // market
            new KitPrefab("House_Medieval_Small",      "Medieval_M"),  // composite mine_crystal / Healer's Cottage
            new KitPrefab("Watermill_Medieval",        "Medieval_M"),  // lumbermill
            new KitPrefab("Well",                      "Medieval_M"),  // mine_crystal
            new KitPrefab("Marketplace_Stand_Simple",  "Medieval_M"),  // composite market
            new KitPrefab("Torche_Wall",               "Fantasy_M"),   // deco_torch
            new KitPrefab("Anvil",                     "Tools_M"),     // composite forge
            new KitPrefab("Altar",                     "Fantasy_M"),   // composite Heart of Elarion
            new KitPrefab("Pillar_Ionic",              "Roman_M"),     // composite Heart of Elarion

            // --- archer Tribal ladder: RETIRED TWICE OVER (superseded by WO-902's polyperfect
            //     ladder 2026-08-04, then by the owner's all-wood Tripo ladder 2026-08-06).
            //     Kept mirrored so nothing that still references the _T prefabs breaks; they are
            //     NOT the archer tower's art any more. _T tier, Tier4 unused (maxLevel 3).
            new KitPrefab("Tower_Tribal_Tier1",        "Tribal_T", SrcRootT),  // tower_ground_archer L1
            new KitPrefab("Tower_Tribal_Tier2",        "Tribal_T", SrcRootT),  // tower_ground_archer L2
            new KitPrefab("Tower_Tribal_Tier3",        "Tribal_T", SrcRootT),  // tower_ground_archer L3

            // --- RAID SPIRE ART, owner ruling 2026-09-10 (WO-1619): The Forsaken Camp's
            //     centrepiece is a RUINED WATCHTOWER, and the owner picked "the KayKit tower
            //     base" from three measured candidates. It backs the tower_ruined_watchtower
            //     catalog row, whose visualPrefabPath is "Structures/building_tower_base_green"
            //     - the stem is deliberately IDENTICAL to the source file so the copy below,
            //     that catalog key and MarkCatalogArt's lookup all agree with no renaming.
            //     MEASURED off the pack's own glTF twin: 0.930 x 1.500 x 1.111 m, one mesh node
            //     (the complete building_tower_A_green is 2.192 m with a separate _top_ node) -
            //     i.e. the same shaft with its crown gone. .fbx, not .prefab: KayKit ships raw
            //     models.
            new KitPrefab("building_tower_base_green", "green", SrcRootKayHex, ".fbx"),
        };

        [MenuItem("Defenders/Catalog/Copy Kit Prefabs To Resources")]
        public static void CopyKitToResources()
        {
            if (!Directory.Exists(DstDir))
                Directory.CreateDirectory(DstDir);

            int copied = 0, skipped = 0, missing = 0;
            foreach (var kit in KitPrefabs)
            {
                string name = kit.Name;
                string ext = string.IsNullOrEmpty(kit.Ext) ? ".prefab" : kit.Ext;
                string src = kit.Root + kit.Category + "/" + name + ext;
                string dst = DstDir + name + ext;

                // Idempotent — already in Resources, leave it.
                if (File.Exists(dst))
                {
                    skipped++;
                    continue;
                }

                // Source absent (pack not imported on this machine) — warn, don't error
                // (CLAUDE.md §4: pack may not be imported).
                if (AssetDatabase.LoadAssetAtPath<GameObject>(src) == null)
                {
                    Debug.LogWarning($"[CatalogPrefabImporter] source prefab missing (pack not imported?): {src}");
                    missing++;
                    continue;
                }

                // CopyAsset preserves mesh/material dependencies (rewrites GUIDs into the copy).
                if (AssetDatabase.CopyAsset(src, dst))
                {
                    copied++;
                }
                else
                {
                    Debug.LogWarning($"[CatalogPrefabImporter] CopyAsset FAILED: {src} -> {dst}");
                    missing++;
                }
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"[CatalogPrefabImporter] DONE — copied {copied}, skipped {skipped} already-present, " +
                      $"missing {missing}. Destination: {DstDir}. CATALOG_KIT_COPY_OK");
        }
    }
}

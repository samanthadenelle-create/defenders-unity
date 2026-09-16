// =============================================================================
// VillageSceneBuilder.Village2Inject — surgically drops the gameplay/economy
// buildings into the GENERATED Village2 scene (the canonical village).
// -----------------------------------------------------------------------------
// Village2 is the shipping village (generated, regenerable; we pivoted off the
// corruption-cursed hand-built Village.unity — DEF-243). Its structures are good,
// but the KEY gameplay buildings (Pet House / Forge / Farm / Market / Sawmill /
// Armorer / Arcane Tower) were never carried over.
//
// This REUSES the proven PlaceBuilding code + the DEF-101-cleared Buildings[]
// specs (positions already considered), guarded by an IF-NOT-EXISTS check so only
// the MISSING delta is placed. The existing Village2 objects are NEVER re-imported
// — re-importing would re-trigger the FBX Z-axis rotation bug (DEF-254) on the
// already-correct structures (owner's explicit reason for the guard). New plots are
// ground-seated onto Village2's terrain. Nothing already present is touched.
//
//   Defenders > Village2 > Inject Gameplay Buildings
//   (batchmode: DeNelle.Editor.VillageSceneBuilder.InjectGameplayBuildingsIntoVillage2)
//
// NOTE: a NavMesh re-bake is still needed afterward for enemy pathing around the new
// footprints (hero is blocked by the footprint colliders regardless).
// =============================================================================

using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace DeNelle.Editor
{
    public static partial class VillageSceneBuilder
    {
        private const string Village2ScenePath = ScenesDir + "/Village2.unity";

        [MenuItem("Defenders/Village2/Inject Gameplay Buildings")]
        public static void InjectGameplayBuildingsIntoVillage2()
        {
            var scene = EditorSceneManager.OpenScene(Village2ScenePath, OpenSceneMode.Single);

            // VillageController for RegisterBuilding (null-tolerant — placement still works).
            Component controller = null;
            var ctrlType = FindType(TypeVillageController);
            if (ctrlType != null)
                controller = UnityEngine.Object.FindAnyObjectByType(ctrlType, FindObjectsInactive.Include) as Component;

            var parentGo = GameObject.Find("GameplayBuildings");
            if (parentGo == null) parentGo = new GameObject("GameplayBuildings");

            int placed = 0, skipped = 0, reseated = 0;
            foreach (var b in Buildings)
            {
                // WO-312 — the Farm is no longer a BUILDING; it is a harvestable Food
                // MineNode (a small farm plot the player taps [F] to bank Food, the same
                // economy faucet as wood/iron/crystal nodes). Skip the building placement
                // here and drop the node below instead. If an old Farm building plot is
                // already in the scene, retire it so it doesn't double up with the node.
                if (string.Equals(b.Id, "farm", StringComparison.OrdinalIgnoreCase))
                {
                    var oldFarm = FindVillage2Building(b.Id);
                    if (oldFarm != null) UnityEngine.Object.DestroyImmediate(oldFarm);
                    PlaceFarmFoodNode(parentGo.transform, b);
                    placed++;
                    continue;
                }

                var existing = FindVillage2Building(b.Id);
                if (existing != null)
                {
                    // DELTA GUARD: never re-import an existing building — that would
                    // re-trigger the FBX Z-axis rotation bug. Only correct the Y so it
                    // sits on the ground (Village2 ground is Y=0; a bad earlier ground-
                    // seat floated some plots on top of walls/houses). Position-only =
                    // no re-import, no Z-rotation.
                    var p = existing.transform.position;
                    if (Mathf.Abs(p.y) > 0.05f)
                    {
                        existing.transform.position = new Vector3(p.x, 0f, p.z);
                        reseated++;
                    }
                    else skipped++;
                    continue;
                }
                // New plot: place flat at the spec Y=0 (NO raycast seat — it hit other
                // structures and floated the buildings).
                PlaceBuilding(parentGo.transform, b, controller, seatToGround: false);
                placed++;
            }

            // Additive: gives every building lacking one its F-prompt interactable
            // (idempotent; moves/rotates nothing, so existing plots are untouched).
            WireBuildingInteractables();

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, Village2ScenePath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"[Village2Inject] DONE — placed {placed} new, reseated {reseated} to ground, " +
                      $"skipped {skipped} already-good. NavMesh re-bake still needed for enemy pathing. " +
                      "VILLAGE2_INJECT_OK");
        }

        // WO-312 — Farm → harvestable Food node. Builds a small farm-plot visual and
        // attaches a DeNelle.Village.MineNode configured to yield Food. The node banks
        // Food via EconomyService.Grant(food:) exactly like the wood/iron/crystal nodes
        // (see MineNode.BankYield). Renewable + [F]-tappable in the village: we keep
        // UseFiniteReserve OFF (the settlement-drain model is an outer-world concern) so
        // the legacy in-range prompt + [F]/tap extract path is active, and use the
        // cooldown-respawn so the plot refills instead of depleting permanently.
        //
        // MineNode lives in DeNelle.Village; the Editor asmdef can't reference Village,
        // so it is attached by reflection and its public fields are set by name — the
        // same asmdef-free pattern OuterWorldBuilder uses. Resource is set via the
        // MineResource enum's underlying int (Iron=0, Wood=1, Food=2, AetherCrystal=3).
        private const int MineResourceFood = 2;   // DeNelle.Village.MineResource.Stone

        private static void PlaceFarmFoodNode(Transform parent, BuildingPlacement b)
        {
            // Root plot at the Farm's authored position (DEF-101-cleared, off the gate).
            var plot = new GameObject($"FarmFoodNode-{b.Id} ({b.Label})");
            plot.transform.SetParent(parent, false);
            plot.transform.position = new Vector3(b.X, 0f, b.Z);

            // Small tilled-soil patch so the plot reads as a farm field (visual only —
            // the MineNode's own MineNodeVisual builds the readable grain silhouette on
            // top, since AutoBuildVisual defaults on). Thin quad-like cube, no collider
            // so it never blocks pathing or the [F] pick.
            var soil = GameObject.CreatePrimitive(PrimitiveType.Cube);
            soil.name = "FarmPlot_Soil";
            soil.transform.SetParent(plot.transform, false);
            soil.transform.localPosition = new Vector3(0f, 0.05f, 0f);
            soil.transform.localScale = new Vector3(4f, 0.1f, 4f);
            ApplyColor(soil, new Color(0.40f, 0.28f, 0.16f));   // tilled-earth brown
            var soilCol = soil.GetComponent<Collider>();
            if (soilCol != null) UnityEngine.Object.DestroyImmediate(soilCol);

            // Attach the Food MineNode by reflection (asmdef-free).
            Type mineNodeType = FindType("DeNelle.Village.MineNode");
            if (mineNodeType == null)
            {
                Debug.LogWarning("[Village2Inject] DeNelle.Village.MineNode not found — Farm food " +
                                 "plot placed as a bare visual (no harvest). Is DeNelle.Village compiled?");
                return;
            }

            var mn = plot.AddComponent(mineNodeType);
            var fRes = mineNodeType.GetField("Resource");
            if (fRes != null) fRes.SetValue(mn, Enum.ToObject(fRes.FieldType, MineResourceFood));
            var fYield = mineNodeType.GetField("YieldPerExtract");
            if (fYield != null) fYield.SetValue(mn, 5);
            // Renewable in-village node: legacy extract+cooldown-respawn (NOT a finite
            // reserve), so the player can [F]/tap to bank Food and the plot refills.
            var fFinite = mineNodeType.GetField("UseFiniteReserve");
            if (fFinite != null) fFinite.SetValue(mn, false);
            var fTotal = mineNodeType.GetField("TotalExtracts");
            if (fTotal != null) fTotal.SetValue(mn, 6);
            var fCooldown = mineNodeType.GetField("ExtractCooldown");
            if (fCooldown != null) fCooldown.SetValue(mn, 8f);
            var fRespawn = mineNodeType.GetField("RespawnSeconds");
            if (fRespawn != null) fRespawn.SetValue(mn, 60f);
        }

        /// <summary>The "Building-{id} (...)" plot in the open scene, or null.</summary>
        private static GameObject FindVillage2Building(string id)
        {
            string prefix = "Building-" + id + " ";
            foreach (var go in UnityEngine.Object.FindObjectsByType<GameObject>(
                         FindObjectsInactive.Include))
                if (go != null && go.name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    return go;
            return null;
        }
    }
}

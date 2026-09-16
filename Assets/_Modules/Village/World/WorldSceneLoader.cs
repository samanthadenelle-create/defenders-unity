// =============================================================================
// WorldSceneLoader — DEPRECATED: OuterWorld scene removed (WO-608 MergedWorld)
// =============================================================================
// Assembly: DeNelle.Village   Namespace: DeNelle.Village
//
// With MergedWorld ON, the castle + overworld are merged into Main_Castle_Overworld.
// OuterWorld no longer exists. This loader is kept for compatibility but does nothing.
// All world loading is now handled by HubScenes.IsOverworld() and the merged scene.
// =============================================================================
using UnityEngine;
using UnityEngine.SceneManagement;
using DeNelle.Core.Diagnostics;
using DeNelle.Core.World;

namespace DeNelle.Village
{
    public static class WorldSceneLoader
    {
        private const string VillageSceneName    = "Village2";
        // OuterWorld scene removed (WO-608 MergedWorld) — use HubScenes.IsOverworld() instead

        // Hub scenes (Village2 / MainCastle_Hall / CastleHub*) now come from the ONE shared
        // source DeNelle.Core.HubScenes — the same list VillageHudController reads, so the HUD's
        // town context + this loader can never drift again (WO-411 root cause A).

        // BUILD FIX (WO-173/DEF-108): a one-shot AfterSceneLoad check FAILED in player
        // builds. AfterSceneLoad fires when the BOOT scene (Title) is active — not Village
        // — so the old `active.name != "Village"` early-return ran during Title, set its
        // guard, and the overworld was NEVER loaded once the player reached the village
        // (Title -> HeroSelect -> PetSelect -> Village). It only "worked" in the editor
        // because you press Play directly on Village. Fix: subscribe to sceneLoaded and
        // bring the overworld in WHENEVER the Village scene loads, in any flow.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetGuard() => SceneManager.sceneLoaded -= OnSceneLoaded;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Init()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;   // de-dupe across domain reloads
            SceneManager.sceneLoaded += OnSceneLoaded;
            FlowTrace.Step("World", $"Init (AfterSceneLoad): subscribed to sceneLoaded; active scene='{SceneManager.GetActiveScene().name}'.");
            Debug.Log("[WorldSceneLoader] DEBUG Init fired — subscribed to sceneLoaded. " +
                "Active scene now = '" + SceneManager.GetActiveScene().name + "'. Watching for '" +
                VillageSceneName + "'. (DEF-108 diagnostic)");
            // Handle the case where Village is already the active scene right now.
            TryLoadOuterWorld(SceneManager.GetActiveScene(), "Init-active");
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            FlowTrace.Step("World", $"sceneLoaded '{scene.name}' (mode={mode}).");
            Debug.Log("[WorldSceneLoader] DEBUG sceneLoaded event: '" + scene.name +
                "' (mode=" + mode + ").");
            if (DeNelle.Core.HubScenes.IsOverworld(scene.name))
            {
                FlowTrace.Step("World", $"'{scene.name}' is overworld — running terrain diagnostics.");
                DiagTerrain(scene);
            }
            TryLoadOuterWorld(scene, "sceneLoaded");
        }

        // DEF-108 diagnostic: when OuterWorld loads, log the terrain's actual RUNTIME state
        // so we can see why it renders black (stripped shader -> InternalErrorShader, no
        // layers, drawHeightmap off, wrong position, or inactive). Remove once resolved.
        private static void DiagTerrain(Scene scene)
        {
            foreach (var root in scene.GetRootGameObjects())
            {
                var t = root.GetComponentInChildren<Terrain>(true);
                if (t == null) continue;
                var mt = t.materialTemplate;
                var td = t.terrainData;
                Debug.Log("[WorldSceneLoader] TERRAINDIAG '" + t.name +
                    "' active=" + t.gameObject.activeInHierarchy + " enabled=" + t.enabled +
                    " drawHeightmap=" + t.drawHeightmap + " layer=" + t.gameObject.layer +
                    " pos=" + t.transform.position +
                    " material='" + (mt != null ? mt.name : "NULL") +
                    "' shader='" + (mt != null && mt.shader != null ? mt.shader.name : "NULL") +
                    "' terrainLayers=" + (td != null && td.terrainLayers != null ? td.terrainLayers.Length : -1) +
                    " size=" + (td != null ? td.size.ToString() : "?"));
                if (td != null && td.terrainLayers != null)
                    for (int i = 0; i < td.terrainLayers.Length; i++)
                        Debug.Log("[WorldSceneLoader] TERRAINDIAG   layer" + i + "='" +
                            (td.terrainLayers[i] != null ? td.terrainLayers[i].name : "NULL") +
                            "' diffuse=" + (td.terrainLayers[i] != null && td.terrainLayers[i].diffuseTexture != null
                                ? td.terrainLayers[i].diffuseTexture.name : "NULL") +
                            // WO-1101: normal presence is logged because EVERY layer shipped with
                            // m_NormalMapTexture:{fileID:0} until this pass — a NULL here is the
                            // "ground reads flat" defect, and it was invisible without this line.
                            " normal=" + (td.terrainLayers[i] != null && td.terrainLayers[i].normalMapTexture != null
                                ? td.terrainLayers[i].normalMapTexture.name : "NULL") +
                            " tile=" + (td.terrainLayers[i] != null ? td.terrainLayers[i].tileSize.x.ToString("0") : "?"));
                if (t.drawInstanced) t.drawInstanced = false;   // ruled out, keep off anyway
                // DEF-108 probe: terrain is correct (shader/layers/pos) yet black -> LIGHTING + SPLAT.
                Debug.Log("[WorldSceneLoader] TERRAINDIAG ambientMode=" + RenderSettings.ambientMode +
                    " ambientLight=" + RenderSettings.ambientLight + " ambientIntensity=" + RenderSettings.ambientIntensity +
                    " skybox='" + (RenderSettings.skybox != null ? RenderSettings.skybox.name : "NULL") + "'");
                foreach (var L in UnityEngine.Object.FindObjectsByType<Light>())
                    Debug.Log("[WorldSceneLoader] TERRAINDIAG light '" + L.name + "' type=" + L.type +
                        " intensity=" + L.intensity + " on=" + (L.enabled && L.gameObject.activeInHierarchy) +
                        " scene='" + L.gameObject.scene.name + "'");
                if (td != null && td.alphamapLayers > 0)
                {
                    var c = td.GetAlphamaps(td.alphamapWidth / 2, td.alphamapHeight / 2, 1, 1);
                    float sum = 0f;
                    for (int k = 0; k < td.alphamapLayers; k++) sum += c[0, 0, k];
                    var centerLayers = new System.Text.StringBuilder();
                    for (int k = 0; k < td.alphamapLayers; k++)
                    {
                        if (c[0, 0, k] < 0.01f) continue;
                        string nm = k < TerrainLayerSet.Count ? TerrainLayerSet.Layers[k].Name : ("layer" + k);
                        if (centerLayers.Length > 0) centerLayers.Append(' ');
                        centerLayers.Append(nm).Append('=').Append(c[0, 0, k].ToString("0.00"));
                    }
                    FlowTrace.Step("World",
                        "TERRAIN center splat (courtyard) sum=" + sum.ToString("0.00") +
                        " layers=[" + centerLayers + "]");
                    if (sum < 0.01f)
                    {
                        // DEF-108 FIX: the baked splatmap did NOT persist into the player build
                        // (all-zero weights -> terrain renders pure black). Repaint at runtime.
                        //
                        // ⛔ WO-1101 — THIS IS THE SPLAT THE PLAYER SEES ON DEVICE.
                        // Until now this block HARDCODED the layer indices 0 / 1 / 2 / 4 while
                        // ExteriorTerrainBuilder owned its own private LayerGrass/LayerStone/...
                        // consts. Two copies of one contract, in two assemblies, with no shared
                        // symbol — so growing the bake's layer set mispainted the ground HERE and
                        // only here, i.e. on device and nowhere an editor gate could see it.
                        // Both authorities now read DeNelle.Core.World.TerrainLayerSet. Never
                        // write a bare layer index in this file again.
                        //
                        // ⚠ Ashwood (north) now paints PALE ASH, not the old dark "dead" layer.
                        // WO-1044 §1 authors Ashwood as near-black trunks on a PALE powdery
                        // ground ("ink on ash"); the shipped dark ground was the inverse of canon
                        // and left Ashwood and Mirewood only ΔL 0.098 apart — two dark quadrants
                        // that a greyscale check cannot separate. See TerrainLayerSet for the
                        // measured value staircase.
                        int aw = td.alphamapWidth, ah = td.alphamapHeight, layers = td.alphamapLayers;
                        var fill = new float[ah, aw, layers];
                        var cover = new double[layers];
                        for (int z = 0; z < ah; z++)
                        {
                            // WO-468 Phase 1: terrain enlarged 300 -> 1000 (edge ±500).
                            float worldZ = (ah <= 1) ? 0f : (z / (float)(ah - 1)) * 1000f - 500f;
                            for (int x = 0; x < aw; x++)
                            {
                                float worldX = (aw <= 1) ? 0f : (x / (float)(aw - 1)) * 1000f - 500f;

                                float qGold, qStone, qMire, qAsh;
                                BuildQuadrantWeights(worldX, worldZ, out qGold, out qStone, out qMire, out qAsh);
                                float centre = Mathf.Clamp01(1f - (qGold + qStone + qMire + qAsh));

                                // Same 2-layer-per-march blend the bake uses, so editor and device
                                // agree on what the ground is (see ExteriorTerrainBuilder.PaintSplatmaps).
                                float mottle = Mathf.PerlinNoise(worldX * 0.012f + 91f, worldZ * 0.012f + 47f);

                                Put(fill, cover, z, x, layers, TerrainLayerSet.Meadow,
                                    centre + qGold * (0.18f - 0.18f * mottle) + qStone * (0.15f * mottle));
                                Put(fill, cover, z, x, layers, TerrainLayerSet.GoldfieldsField,
                                    qGold * (0.82f + 0.18f * mottle));
                                Put(fill, cover, z, x, layers, TerrainLayerSet.StonebackRock,
                                    qStone * (0.85f + 0.15f * (1f - mottle)) + qAsh * (0.12f - 0.12f * mottle));
                                Put(fill, cover, z, x, layers, TerrainLayerSet.MirewoodMire,
                                    qMire * (0.55f + 0.45f * mottle));
                                Put(fill, cover, z, x, layers, TerrainLayerSet.MirewoodRoots,
                                    qMire * (0.45f - 0.45f * mottle));
                                Put(fill, cover, z, x, layers, TerrainLayerSet.AshwoodAsh,
                                    qAsh * (0.88f + 0.12f * mottle));

                                float total = 0f;
                                for (int k = 0; k < layers; k++) total += fill[z, x, k];
                                if (total < 0.001f)
                                {
                                    fill[z, x, TerrainLayerSet.Meadow] = 1f;   // hub centre
                                    cover[TerrainLayerSet.Meadow] += 1.0;
                                    continue;
                                }
                                for (int k = 0; k < layers; k++)
                                {
                                    float before = fill[z, x, k];
                                    fill[z, x, k] = before / total;
                                    cover[k] += fill[z, x, k] - before;
                                }
                            }
                        }
                        td.SetAlphamaps(0, 0, fill);

                        // AC-2 proving line: WHICH splat authority ran, on the SHARED contract,
                        // with per-layer coverage. A bake-only change is invisible on device, so
                        // this is the line that proves the runtime path was handled (CLAUDE.md §12).
                        double cells = (double)aw * ah;
                        var sb = new System.Text.StringBuilder();
                        sb.Append("RUNTIME repaint (DEF-108) ran — baked alphamap was EMPTY. ")
                          .Append(TerrainLayerSet.Manifest())
                          .Append(" | terrainLayers=").Append(layers).Append(" coverage:");
                        for (int k = 0; k < layers; k++)
                        {
                            string nm = k < TerrainLayerSet.Count ? TerrainLayerSet.Layers[k].Name : ("layer" + k);
                            sb.Append(' ').Append(nm).Append('=')
                              .Append((cover[k] / cells * 100.0).ToString("0.0")).Append('%');
                        }
                        FlowTrace.Step("World", sb.ToString());
                        Debug.Log("[WorldSceneLoader] TERRAINDIAG " + sb);
                        if (layers < TerrainLayerSet.Count)
                            FlowTrace.Warn("World", "terrain has only " + layers + " layers but the shared contract " +
                                "declares " + TerrainLayerSet.Count + " — the terrain asset is from an OLDER bake. " +
                                "Re-run DeNelle.Editor.ExteriorTerrainBuilder.BuildExterior.");
                    }
                    else
                    {
                        Debug.Log("[WorldSceneLoader] TERRAINDIAG splat OK (center sum=" + sum + ").");
                    }
                }
                // DEF-108 bump probe: actual terrain surface Y at the village + just outside,
                // vs the village floor (hero spawns ~Y=0.03). Reveals the step/lip at the edge.
                float baseY = t.transform.position.y;
                int[] xs = { 0, 30, 42, 50, 70 };
                foreach (int d in xs)
                    Debug.Log("[WorldSceneLoader] TERRAINDIAG surfaceY @x=" + d + " -> " +
                        (baseY + t.SampleHeight(new Vector3(d, 0f, 0f))).ToString("0.000") +
                        "  @z=" + d + " -> " +
                        (baseY + t.SampleHeight(new Vector3(0f, 0f, d))).ToString("0.000"));
                return;
            }
            FlowTrace.Warn("World", $"DiagTerrain: no Terrain found in overworld scene '{scene.name}' — nothing to diagnose.");
            Debug.Log("[WorldSceneLoader] TERRAINDIAG no Terrain found in OuterWorld!");
        }

        /// <summary>Accumulates a weight into one splat layer, guarding the layer bound.</summary>
        private static void Put(float[,,] fill, double[] cover, int z, int x, int layers, int layer, float weight)
        {
            if (layer < 0 || layer >= layers || weight <= 0f) return;
            fill[z, x, layer] += weight;
            cover[layer] += weight;
        }

        /// <summary>
        /// Quadrant membership weights (Goldfields E / Stoneback W / Mirewood S / Ashwood N)
        /// for a terrain-centred world position.
        /// <para>
        /// ⚠ MIRRORS <c>DeNelle.Editor.ExteriorTerrainBuilder.TerrainLayerSet_QuadrantWeights</c>.
        /// The layer INDICES both write come from the shared
        /// <see cref="DeNelle.Core.World.TerrainLayerSet"/>; this SHAPE is the remaining pair
        /// that must be changed together. It cannot live in Core because the bake derives it
        /// from editor-only terrain geometry — but if you change one, change the other, or the
        /// ground differs between the editor and the device.
        /// </para>
        /// </summary>
        private static void BuildQuadrantWeights(
            float worldX, float centredZ, out float gold, out float stone, out float mire, out float ash)
        {
            const float half = 500f;   // 1000u terrain, origin-centred (WO-483)
            float hub = Mathf.Clamp01(Mathf.InverseLerp(70f, 190f,
                Mathf.Sqrt(worldX * worldX + centredZ * centredZ)));

            gold  = Mathf.Max(0f, worldX) / half;
            stone = Mathf.Max(0f, -worldX) / half;
            ash   = Mathf.Max(0f, centredZ) / half;
            mire  = Mathf.Max(0f, -centredZ) / half;

            float sum = gold + stone + ash + mire;
            if (sum < 0.0001f) { gold = stone = ash = mire = 0f; return; }
            gold  = gold / sum * hub;
            stone = stone / sum * hub;
            ash   = ash / sum * hub;
            mire  = mire / sum * hub;
        }

        private const string MergedWorldSceneName = "Main_Castle_Overworld";

        private static void TryLoadOuterWorld(Scene scene, string via)
        {
            // WO-608: OuterWorld scene has been DELETED. All world content is now in Main_Castle_Overworld.
            // This method is kept for compatibility but does nothing. The merged single scene ALREADY
            // contains all overworld content in-scene (welded into one continuous navmesh by WorldMergeBuilder).
            // No additive loading is needed or possible.
            FlowTrace.Step("World", $"TryLoadOuterWorld({via}): DEPRECATED no-op — OuterWorld removed (WO-608 MergedWorld); content is in-scene ('{MergedWorldSceneName}'), no additive stream.");
            Debug.Log("[WorldSceneLoader] (" + via + ") DEPRECATED: OuterWorld scene removed (WO-608 MergedWorld). " +
                "All world content is in Main_Castle_Overworld. No streaming loader needed.");
        }
    }
}

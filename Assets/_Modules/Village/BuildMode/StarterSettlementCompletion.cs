using System.Collections;
using System.Collections.Generic;
using DeNelle.Core.Analytics;
using DeNelle.Core.Catalog;
using DeNelle.Core.Diagnostics;
using DeNelle.Core.State;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DeNelle.Village
{
    /// <summary>Adds current non-baked essentials and gate defenses after the
    /// proven default-town scene/migration pass. All pieces use normal BaseLayout.</summary>
    public sealed class StarterSettlementCompletion : MonoBehaviour
    {
        public const string SelectedKey = "founding.default_town_selected";
        public const string CompletedKey = "founding.starter_settlement_v1";
        public const string LayoutRelativePath = "Data/Canonical/starter-settlement-layout.json";

        [System.Serializable]
        private sealed class LayoutTable
        {
            public int version;
            public Entry[] entries;
        }

        [System.Serializable]
        private sealed class Entry
        {
            public string id;
            public int x;
            public int z;
            public int yawQuarterTurns;
            [JsonIgnore] public Vector2Int Cell => new Vector2Int(x, z);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Arm()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        private static void OnSceneLoaded(Scene loaded, LoadSceneMode mode) => Install(loaded.name);

        private static void Install(string scene)
        {
            if (scene != DeNelle.Core.SceneRouter.Castle && scene != "MainCastle_Hall" &&
                scene != "Main_Castle_Overworld") return;
            if (FindAnyObjectByType<StarterSettlementCompletion>() == null)
                new GameObject("StarterSettlementCompletion").AddComponent<StarterSettlementCompletion>();
        }

        private IEnumerator Start()
        {
            float until = Time.realtimeSinceStartup + 12f;
            GameStateService svc = null;
            while (Time.realtimeSinceStartup < until)
            {
                svc = GameStateService.Instance;
                if (svc != null && svc.State != null && CatalogRegistry.Count > 0 &&
                    svc.State.StrategicPlacementMigrated) break;
                yield return null;
            }

            var state = svc != null ? svc.State : null;
            if (state == null || !Seen(state, SelectedKey) || Seen(state, CompletedKey))
            { Destroy(gameObject); yield break; }

            Entry[] template = LoadTemplate();
            if (template == null || template.Length == 0)
            {
                // Owner 2026-09-12: both founding paths keep HubStructureVisualInjector
                // models only. Catalog extras (GenericContainer / IronMine / ShopAndCrafting)
                // are retired from ready-settlement. Empty layout is success, not a fail.
                svc.MarkTutorialSeen(CompletedKey);
                svc.Save();
                FlowTrace.Step("Founding",
                    "starter extras skipped — hub injector owns the 8 storefronts on both founding paths.");
                Destroy(gameObject);
                yield break;
            }

            var grid = PlacementGrid.Instance;
            if (grid == null) grid = new GameObject("PlacementGrid").AddComponent<PlacementGrid>();
            var loader = BaseLayoutLoader.Instance != null ? BaseLayoutLoader.Instance : BaseLayoutLoader.EnsureExists();
            if (state.BaseLayout == null) state.BaseLayout = new List<PlacedStructureData>();

            int added = 0, existing = 0, failed = 0;
            for (int i = 0; i < template.Length; i++)
            {
                Entry item = template[i];
                if (item == null || string.IsNullOrWhiteSpace(item.id))
                { failed++; FlowTrace.Fail("Founding", $"starter layout row {i} has no id"); continue; }
                if (Count(state.BaseLayout, item.id) > OccurrenceBefore(template, i, item.id))
                { existing++; continue; }

                CatalogEntry catalog = CatalogRegistry.Get(item.id);
                if (catalog == null) { failed++; FlowTrace.Fail("Founding", $"starter id missing: {item.id}"); continue; }
                Vector2Int footprint = grid.FootprintCells(
                    StructureFactory.MeasureClaimFootprintXZ(catalog), item.yawQuarterTurns * 90f);
                if (!ResolveFreeCell(grid, item.Cell, footprint, out Vector2Int cell))
                { failed++; FlowTrace.Fail("Founding", $"no starter seat for {item.id} near {item.Cell}"); continue; }

                var record = new PlacedStructureData(item.id, cell.x, cell.y, item.yawQuarterTurns, 1);
                state.BaseLayout.Add(record);
                state.MarkEverBuilt(item.id);
                if (loader == null || loader.Spawn(record, grid) == null)
                {
                    state.BaseLayout.RemoveAt(state.BaseLayout.Count - 1);
                    failed++;
                    FlowTrace.Fail("Founding", $"starter spawn failed: {item.id} at {cell}");
                    continue;
                }
                added++;
                FlowTrace.Step("Founding", $"starter placed {item.id} at {cell}");
                yield return null;
            }

            svc.MarkTutorialSeen(CompletedKey);
            svc.Save();
            EventTracker.Track("starter_settlement_ready", new { added, existing, failed, total = template.Length });
            FlowTrace.Step("Founding", $"starter settlement ready: added={added} existing={existing} failed={failed}");
            Destroy(gameObject);
        }

        private static bool Seen(GameState s, string key) => s.SeenTutorials != null &&
            s.SeenTutorials.TryGetValue(key, out bool value) && value;

        private static int Count(List<PlacedStructureData> layout, string id)
        { int n = 0; for (int i = 0; i < layout.Count; i++) if (layout[i].itemId == id) n++; return n; }

        private static Entry[] LoadTemplate()
        {
            try
            {
                string json = DeNelle.Core.CanonicalJson.Read(LayoutRelativePath);
                if (string.IsNullOrWhiteSpace(json)) return null;
                return JsonConvert.DeserializeObject<LayoutTable>(json)?.entries;
            }
            catch (System.Exception ex)
            {
                FlowTrace.Fail("Founding", $"starter layout parse failed: {ex.Message}");
                return null;
            }
        }

        private static int OccurrenceBefore(Entry[] template, int index, string id)
        { int n = 0; for (int i = 0; i < index; i++) if (template[i] != null && template[i].id == id) n++; return n; }

        public static bool ResolveFreeCell(PlacementGrid grid, Vector2Int preferred,
                                           Vector2Int footprint, out Vector2Int cell)
        {
            if (grid != null && grid.CanPlace(preferred, footprint)) { cell = preferred; return true; }
            for (int radius = 1; radius <= 6; radius++)
            for (int dz = -radius; dz <= radius; dz++)
            for (int dx = -radius; dx <= radius; dx++)
            {
                if (Mathf.Abs(dx) != radius && Mathf.Abs(dz) != radius) continue;
                var candidate = preferred + new Vector2Int(dx, dz);
                if (grid != null && grid.CanPlace(candidate, footprint)) { cell = candidate; return true; }
            }
            cell = default; return false;
        }
    }
}

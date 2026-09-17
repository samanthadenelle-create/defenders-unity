// =============================================================================
// DungeonKitBuilder -- WO-595 data-driven 4 m KayKit snap kit.
// Source: Assets/Resources/Data/dungeon-kit.json
// Output: Assets/Generated/DungeonKit (local/reproducible; intentionally ignored).
// =============================================================================
using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;
using DeNelle.Core.Diagnostics;
using DeNelle.Dungeons.RoomForge;

namespace DeNelle.Editor.RoomForge
{
    public static class DungeonKitBuilder
    {
        private const string CatalogPath = "Assets/Resources/Data/dungeon-kit.json";
        private const string Sys = "DungeonKit";

        [Serializable] private sealed class Catalog
        {
            [JsonProperty("modelDir")] public string ModelDir;
            [JsonProperty("outputDir")] public string OutputDir;
            [JsonProperty("grid")] public Grid Grid;
            [JsonProperty("themes")] public Themes Themes;
            [JsonProperty("chunks")] public List<Chunk> Chunks;
        }

        [Serializable] private sealed class Grid
        {
            [JsonProperty("cellSize")] public float CellSize;
            [JsonProperty("doorWidth")] public float DoorWidth;
            [JsonProperty("levelStepY")] public float LevelStepY;
        }

        [Serializable] private sealed class Themes
        {
            [JsonProperty("default")] public string Default;
            [JsonProperty("atlasDir")] public string AtlasDir;
        }

        [Serializable] private sealed class Chunk
        {
            [JsonProperty("id")] public string Id;
            [JsonProperty("type")] public string Type;
            [JsonProperty("cells")] public int[] Cells;
            [JsonProperty("sockets")] public Dictionary<string, string> Sockets;
            [JsonProperty("levelDeltaY")] public float LevelDeltaY;
            [JsonProperty("custom")] public string Custom;
            [JsonProperty("parts")] public List<Part> Parts;
        }

        [Serializable] private sealed class Part
        {
            [JsonProperty("fbx")] public string Fbx;
            [JsonProperty("pos")] public float[] Pos;
            [JsonProperty("yaw")] public float Yaw;
        }

        [MenuItem("Defenders/Dungeon/KayKit/Build All 24 Chunks")]
        public static void BuildAll()
        {
            Catalog catalog = Load();
            if (catalog == null) return;
            EnsureFolder(catalog.OutputDir);
            EnsureFolder(catalog.OutputDir + "/Materials");
            Material theme = ResolveTheme(catalog);
            int built = 0, missing = 0;
            foreach (Chunk chunk in catalog.Chunks)
            {
                GameObject root = BuildChunk(catalog, chunk, theme, ref missing);
                if (root == null) continue;
                PrefabUtility.SaveAsPrefabAsset(root, catalog.OutputDir + "/" + chunk.Id + ".prefab");
                UnityEngine.Object.DestroyImmediate(root);
                built++;
            }
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"DUNGEON_KIT_BUILD_OK {built}/{catalog.Chunks.Count} chunks theme='{catalog.Themes.Default}' missingParts={missing}");
        }

        [MenuItem("Defenders/Dungeon/KayKit/Build Seeded Preview")]
        public static void BuildSeededPreview() => BuildSeededPreview(595);

        public static void BuildSeededPreview(int seed)
        {
            Catalog catalog = Load();
            if (catalog == null) return;
            EnsureFolder(catalog.OutputDir);
            Material theme = ResolveTheme(catalog);
            var chunks = new Dictionary<string, Chunk>(StringComparer.OrdinalIgnoreCase);
            foreach (Chunk chunk in catalog.Chunks) chunks[chunk.Id] = chunk;

            List<Vector2Int> route = PlanRoute(seed, 12);
            var root = new GameObject("KayKitSeededMaze_" + seed);
            int missing = 0;
            for (int i = 0; i < route.Count; i++)
            {
                string id = PieceFor(route, i, out float yaw);
                if (!chunks.TryGetValue(id, out Chunk chunk)) continue;
                GameObject piece = BuildChunk(catalog, chunk, theme, ref missing);
                if (piece == null) continue;
                piece.name = $"{i:00}_{id}";
                piece.transform.SetParent(root.transform, false);
                piece.transform.localPosition = new Vector3(route[i].x * catalog.Grid.CellSize, 0f,
                                                             route[i].y * catalog.Grid.CellSize);
                piece.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
            }

            string path = catalog.OutputDir + "/SeededMaze_" + seed + ".prefab";
            PrefabUtility.SaveAsPrefabAsset(root, path);
            UnityEngine.Object.DestroyImmediate(root);
            AssetDatabase.SaveAssets();
            Debug.Log($"DUNGEON_KIT_COMPOSE_OK seed={seed} cells={route.Count} selfAvoiding=true missingParts={missing} output='{path}'");
        }

        private static GameObject BuildChunk(Catalog catalog, Chunk chunk, Material theme, ref int missing)
        {
            if (chunk == null || string.IsNullOrWhiteSpace(chunk.Id) ||
                chunk.Cells == null || chunk.Cells.Length != 2) return null;

            var root = new GameObject(chunk.Id);
            var meta = root.AddComponent<RoomPrefabMeta>();
            meta.roomId = chunk.Id;
            meta.archetype = chunk.Type ?? "room";
            meta.themePalette = catalog.Themes.Default;
            meta.footprintCells = new Vector2Int(chunk.Cells[0], chunk.Cells[1]);
            meta.cellSize = catalog.Grid.CellSize;
            meta.occupiedLevels = Math.Abs(chunk.LevelDeltaY) > 0.01f ? 2 : 1;

            foreach (Part part in chunk.Parts ?? new List<Part>())
                InstantiateModel(catalog, part.Fbx, V3(part.Pos), part.Yaw, root.transform, theme, chunk.Id, ref missing);

            BuildPerimeter(catalog, chunk, root.transform, theme, ref missing);
            BuildSockets(catalog, chunk, root.transform);
            if (string.Equals(chunk.Custom, "moving-platform-script", StringComparison.OrdinalIgnoreCase))
                root.AddComponent<DungeonKitMovingPlatform>()
                    .Configure(Vector3.zero, Vector3.up * catalog.Grid.LevelStepY);
            return root;
        }

        private static void BuildPerimeter(Catalog catalog, Chunk chunk, Transform root,
                                           Material theme, ref int missing)
        {
            float cell = catalog.Grid.CellSize;
            int width = chunk.Cells[0], depth = chunk.Cells[1];
            for (int x = 0; x < width; x++)
            {
                float px = (x - (width - 1) * 0.5f) * cell;
                AddBoundary(catalog, chunk, root, theme, "S", px, -depth * cell * 0.5f, 180f, x, width, ref missing);
                AddBoundary(catalog, chunk, root, theme, "N", px,  depth * cell * 0.5f,   0f, x, width, ref missing);
            }
            for (int z = 0; z < depth; z++)
            {
                float pz = (z - (depth - 1) * 0.5f) * cell;
                AddBoundary(catalog, chunk, root, theme, "W", -width * cell * 0.5f, pz, 270f, z, depth, ref missing);
                AddBoundary(catalog, chunk, root, theme, "E",  width * cell * 0.5f, pz,  90f, z, depth, ref missing);
            }
        }

        private static void AddBoundary(Catalog catalog, Chunk chunk, Transform root, Material theme,
                                        string side, float x, float z, float yaw, int index, int count,
                                        ref int missing)
        {
            bool opening = IsOpen(chunk, side) && index == count / 2;
            string model = opening ? "wall_doorway" : "wall";
            InstantiateModel(catalog, model, new Vector3(x, 0f, z), yaw, root, theme,
                             chunk.Id + "/" + side, ref missing);
        }

        private static void InstantiateModel(Catalog catalog, string model, Vector3 pos, float yaw,
                                             Transform root, Material theme, string owner, ref int missing)
        {
            string path = catalog.ModelDir + model + ".fbx";
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (source == null)
            {
                missing++;
                Debug.LogWarning($"[{Sys}] missing KayKit FBX '{path}' for '{owner}'");
                return;
            }
            var go = (GameObject)PrefabUtility.InstantiatePrefab(source);
            go.name = model;
            go.transform.SetParent(root, false);
            go.transform.localPosition = pos;
            go.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
            ApplyThemeAndCollision(go, theme);
        }

        private static void BuildSockets(Catalog catalog, Chunk chunk, Transform root)
        {
            foreach (string side in new[] { "N", "E", "S", "W" })
            {
                if (!IsOpen(chunk, side)) continue;
                var go = new GameObject("Socket_" + side);
                go.transform.SetParent(root, false);
                float hx = chunk.Cells[0] * catalog.Grid.CellSize * 0.5f;
                float hz = chunk.Cells[1] * catalog.Grid.CellSize * 0.5f;
                go.transform.localPosition = side == "N" ? new Vector3(0f, 0f, hz) :
                                             side == "S" ? new Vector3(0f, 0f, -hz) :
                                             side == "E" ? new Vector3(hx, 0f, 0f) :
                                                           new Vector3(-hx, 0f, 0f);
                go.transform.localRotation = Quaternion.LookRotation(Direction(side));
                var socket = go.AddComponent<RoomSocket>();
                socket.id = chunk.Id + "_" + side.ToLowerInvariant();
                socket.facing = side;
                socket.type = chunk.Type != null && chunk.Type.StartsWith("stairs", StringComparison.OrdinalIgnoreCase)
                    ? (chunk.LevelDeltaY >= 0f ? RoomSocketType.StairUp : RoomSocketType.StairDown)
                    : RoomSocketType.Arch;
                socket.halfWidth = catalog.Grid.DoorWidth * 0.5f;
                socket.commonDoor = false;
            }
        }

        private static void ApplyThemeAndCollision(GameObject root, Material theme)
        {
            foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                if (theme != null)
                {
                    Material[] mats = renderer.sharedMaterials;
                    for (int i = 0; i < mats.Length; i++) mats[i] = theme;
                    renderer.sharedMaterials = mats;
                }
            }
            foreach (MeshFilter filter in root.GetComponentsInChildren<MeshFilter>(true))
            {
                if (filter.sharedMesh == null || filter.GetComponent<Collider>() != null) continue;
                filter.gameObject.AddComponent<MeshCollider>().sharedMesh = filter.sharedMesh;
            }

            // WO-1837: the loop above gives EVERY kit mesh a collider — walls, floors and props
            // alike — so the layer pass is a separate, name-discriminated sweep rather than a
            // blanket assignment inside it. Only vertical wall chunks belong on "Structure";
            // putting a kit floor there would hand SmartMobileCamera a horizontal occluder.
            // Without this the whole kit pipeline reproduces the WO-1829/WO-1837 defect: solid
            // wall meshes invisible to HeroTargetIndicator.HasLoS's Structure-masked linecast.
            DeNelle.Dungeons.RoomForge.DungeonStructureLayer.ApplyToWalls(
                root.transform, "dungeon-kit chunk walls must block hero line-of-sight");
        }

        private static Material ResolveTheme(Catalog catalog)
        {
            string texturePath = catalog.Themes.AtlasDir + catalog.Themes.Default + ".png";
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
            if (texture == null)
            {
                Debug.LogWarning($"[{Sys}] theme atlas missing at '{texturePath}'; keeping FBX materials");
                return null;
            }
            string path = catalog.OutputDir + "/Materials/" + catalog.Themes.Default + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                Shader shader = Shader.Find("Universal Render Pipeline/Lit");
                if (shader == null) { Debug.LogWarning($"[{Sys}] URP/Lit missing; keeping FBX materials"); return null; }
                material = new Material(shader) { name = "DungeonKit_" + catalog.Themes.Default };
                AssetDatabase.CreateAsset(material, path);
            }
            if (material.HasProperty("_BaseMap")) material.SetTexture("_BaseMap", texture);
            if (material.HasProperty("_MainTex")) material.SetTexture("_MainTex", texture);
            EditorUtility.SetDirty(material);
            return material;
        }

        private static Catalog Load()
        {
            try
            {
                var catalog = JsonConvert.DeserializeObject<Catalog>(File.ReadAllText(CatalogPath));
                if (catalog?.Grid == null || catalog.Themes == null || catalog.Chunks == null)
                    throw new InvalidDataException("catalog missing grid/themes/chunks");
                return catalog;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[{Sys}] catalog load failed: {ex.GetType().Name}: {ex.Message}");
                return null;
            }
        }

        internal static List<Vector2Int> PlanRoute(int seed, int length)
        {
            var random = new System.Random(seed);
            var route = new List<Vector2Int> { Vector2Int.zero };
            var used = new HashSet<Vector2Int> { Vector2Int.zero };
            Vector2Int direction = Vector2Int.up;
            while (route.Count < length)
            {
                var choices = new List<Vector2Int> { direction, Left(direction), Right(direction) };
                for (int i = choices.Count - 1; i > 0; i--)
                {
                    int j = random.Next(i + 1);
                    Vector2Int swap = choices[i]; choices[i] = choices[j]; choices[j] = swap;
                }
                bool advanced = false;
                foreach (Vector2Int candidate in choices)
                {
                    Vector2Int next = route[route.Count - 1] + candidate;
                    if (used.Contains(next)) continue;
                    route.Add(next); used.Add(next); direction = candidate; advanced = true; break;
                }
                if (!advanced) break;
            }
            return route;
        }

        private static string PieceFor(List<Vector2Int> route, int index, out float yaw)
        {
            yaw = 0f;
            if (index == 0 || index == route.Count - 1) return "dead_end";
            Vector2Int incoming = route[index - 1] - route[index];
            Vector2Int outgoing = route[index + 1] - route[index];
            if (incoming + outgoing == Vector2Int.zero)
            {
                yaw = incoming.x != 0 ? 90f : 0f;
                return "room_small_2door";
            }
            var needed = new HashSet<Vector2Int> { incoming, outgoing };
            for (int quarter = 0; quarter < 4; quarter++)
            {
                if (needed.Contains(Rotate(Vector2Int.up, quarter)) &&
                    needed.Contains(Rotate(Vector2Int.right, quarter)))
                { yaw = quarter * 90f; break; }
            }
            return "hall_corner_L";
        }

        private static Vector2Int Rotate(Vector2Int value, int quarter)
        {
            for (int i = 0; i < quarter; i++) value = new Vector2Int(value.y, -value.x);
            return value;
        }

        private static Vector2Int Left(Vector2Int value) => new Vector2Int(-value.y, value.x);
        private static Vector2Int Right(Vector2Int value) => new Vector2Int(value.y, -value.x);
        private static bool IsOpen(Chunk chunk, string side) =>
            chunk.Sockets != null && chunk.Sockets.TryGetValue(side, out string value) &&
            string.Equals(value, "open", StringComparison.OrdinalIgnoreCase);
        private static Vector3 V3(float[] value) => value != null && value.Length >= 3
            ? new Vector3(value[0], value[1], value[2]) : Vector3.zero;
        private static Vector3 Direction(string side) => side == "N" ? Vector3.forward :
            side == "S" ? Vector3.back : side == "E" ? Vector3.right : Vector3.left;

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            int slash = path.LastIndexOf('/');
            string parent = path.Substring(0, slash);
            if (!AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, path.Substring(slash + 1));
        }
    }
}

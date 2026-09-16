using System;
using System.Collections.Generic;
using System.Reflection;
using DeNelle.Core.State;
using DeNelle.Village;
using Newtonsoft.Json;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DeNelle.Editor
{
    /// <summary>Explicit adoption versus legacy ownership, plus exact pose save round trips.</summary>
    public static class AuthoredBarracksProvenanceRegression
    {
        public static void RunStandalone()
        {
            typeof(CatalogBootstrap).GetMethod("Register", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, null);
            if (Run(out string reason)) Debug.Log("AUTHORED_BARRACKS_OK " + reason);
            else Debug.LogError("AUTHORED_BARRACKS_FAIL " + reason);
        }

        public static bool Run(out string reason)
        {
            var instanceField = typeof(GameStateService).GetField("_instance", BindingFlags.NonPublic | BindingFlags.Static);
            var stateField = typeof(GameStateService).GetField("_state", BindingFlags.NonPublic | BindingFlags.Instance);
            if (instanceField == null || stateField == null)
            { reason = "GameStateService fixture seams missing; no test executed."; return false; }
            var priorService = instanceField.GetValue(null);
            var priorScene = SceneManager.GetActiveScene();
            Scene fixture = default;
            bool ownsScene = false;
            string priorName = priorScene.name;
            GameState state = null;
            try
            {
                if (string.IsNullOrEmpty(priorScene.path))
                {
                    if (!Application.isBatchMode || priorScene.isDirty || SceneManager.sceneCount != 1 || priorScene.GetRootGameObjects().Length != 0)
                        throw new InvalidOperationException("Fixture requires a pristine empty batch scene or a saved scene.");
                    fixture = priorScene;
                }
                else
                {
                    fixture = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
                    ownsScene = true;
                }
                SceneManager.SetActiveScene(fixture);
                var serviceHost = new GameObject("AuthoredBarracks_StateFixture");
                serviceHost.SetActive(false);
                var service = serviceHost.AddComponent<GameStateService>();
                state = ScriptableObject.CreateInstance<GameState>();
                state.BaseLayout = new List<PlacedStructureData>();
                stateField.SetValue(service, state);
                instanceField.SetValue(null, service);

                var root = new GameObject("OwnerNamedBarracks");
                var marker = root.AddComponent<AuthoredCastleStorefront>();
                marker.Configure("barracks", "CastleBarracks");
                root.transform.SetPositionAndRotation(new Vector3(13.25f, 1.75f, -9.125f), Quaternion.Euler(90f, 37f, 12f));
                root.transform.localScale = new Vector3(1.25f, 0.8f, 2.1f);

                // Same canonical id/coordinates are NOT adoption evidence.
                var legacy = JsonConvert.DeserializeObject<PlacedStructureData>(
                    "{\"itemId\":\"barracks\",\"cellX\":4,\"cellZ\":2,\"yawSteps\":1,\"level\":1}");
                state.BaseLayout.Add(legacy);
                Require(legacy.authoredSourceId == null && legacy.authoredPose == null, "Legacy defaults were not null.");
                Require(!AuthoredCastleStorefront.IsBoundAuthoredRoot(root.transform, "barracks"), "Legacy player record was stolen by authored root.");

                var placed = root.AddComponent<PlacedStructure>();
                placed.itemId = "barracks";
                placed.gridCell = new Vector2Int(4, 2);
                placed.authoredSourceId = "barracks";
                var adopted = RoundTrip(placed.ToSaveData());
                state.BaseLayout[0] = adopted;
                Require(AuthoredCastleStorefront.IsBoundAuthoredRoot(root.transform, "barracks"), "Explicit adoption did not bind.");
                RequirePose(adopted.authoredPose, root.transform);

                // Movement and upgrade snapshots retain provenance and exact, non-grid TRS.
                root.transform.SetPositionAndRotation(new Vector3(-3.125f, 2.5f, 18.75f), Quaternion.Euler(83f, 128f, -17f));
                placed.gridCell = new Vector2Int(7, 8);
                placed.level = 3;
                var moved = RoundTrip(placed.ToSaveData());
                Require(moved.authoredSourceId == "barracks" && moved.cellX == 7 && moved.level == 3, "Move/upgrade snapshot dropped ownership or level.");
                RequirePose(moved.authoredPose, root.transform);
                state.BaseLayout[0] = moved;
                Require(AuthoredCastleStorefront.IsBoundAuthoredRoot(root.transform, "barracks"), "Moved authored record lost binding.");

                state.BaseLayout.Add(legacy);
                Require(!AuthoredCastleStorefront.IsBoundAuthoredRoot(root.transform, "barracks"), "Conflicting player record was accepted as self ownership.");
                state.BaseLayout.RemoveAt(1);
                var malformed = moved;
                malformed.authoredPose = null;
                state.BaseLayout[0] = malformed;
                Require(!AuthoredCastleStorefront.IsBoundAuthoredRoot(root.transform, "barracks"), "Missing pose was accepted as adoption.");
                malformed = moved;
                malformed.authoredSourceId = "other-source";
                Require(!AuthoredCastleStorefront.IsAuthoredBarracksRecord(malformed), "Unknown source was accepted.");
                UnityEngine.Object.DestroyImmediate(root);
                fixture.name = DeNelle.Core.SceneRouter.Castle;
                RunBindingLifecycle(state, service);
                reason = "provenance/pose round trips, real bind/rebuild/state replacement, occupancy, tier preservation, and explicit template-grant authorization passed";
                return true;
            }
            catch (Exception error) { reason = error.ToString(); return false; }
            finally
            {
                if (ownsScene) EditorSceneManager.CloseScene(fixture, true);
                else if (fixture.IsValid())
                {
                    foreach (var fixtureRoot in fixture.GetRootGameObjects()) UnityEngine.Object.DestroyImmediate(fixtureRoot);
                    fixture.name = priorName;
                }
                if (priorScene.IsValid() && priorScene.isLoaded) SceneManager.SetActiveScene(priorScene);
                instanceField.SetValue(null, priorService);
                if (state != null) UnityEngine.Object.DestroyImmediate(state);
            }
        }

        private static void RunBindingLifecycle(GameState state, GameStateService service)
        {
            var flags = BindingFlags.Static | BindingFlags.NonPublic;
            var loaderField = typeof(BaseLayoutLoader).GetField("<Instance>k__BackingField", flags);
            var gridField = typeof(PlacementGrid).GetField("<Instance>k__BackingField", flags);
            Require(loaderField != null && gridField != null, "Loader/grid fixture seams unavailable.");
            var oldLoader = loaderField.GetValue(null);
            var oldGrid = gridField.GetValue(null);
            var loaderHost = new GameObject("AuthoredBinding_Loader");
            var gridHost = new GameObject("AuthoredBinding_Grid");
            loaderHost.SetActive(false);
            gridHost.SetActive(false);
            try
            {
                var loader = loaderHost.AddComponent<BaseLayoutLoader>();
                var grid = gridHost.AddComponent<PlacementGrid>();
                loaderField.SetValue(null, loader);
                gridField.SetValue(null, grid);
                grid.origin = new Vector3(-45f, 0f, -45f);
                grid.gridWidth = grid.gridHeight = 60;
                grid.cellSize = 3f;
                var root = new GameObject("OriginalAuthoredBarracks");
                root.AddComponent<AuthoredCastleStorefront>().Configure("barracks", "CastleBarracks");
                root.AddComponent<Building>().Configure(BuildingType.CrystalMine, "barracks", "Barracks");
                root.transform.SetPositionAndRotation(new Vector3(13.25f, 1.75f, -9.125f), Quaternion.Euler(90f, 37f, 12f));
                root.transform.localScale = new Vector3(1.25f, 0.8f, 2.1f);
                var geometry = GameObject.CreatePrimitive(PrimitiveType.Cube);
                geometry.transform.SetParent(root.transform, false);
                geometry.transform.localPosition = new Vector3(-3.2f, 1.5f, 2.3f);
                geometry.transform.localScale = new Vector3(4.2f, 2.6f, 3.8f);
                var mesh = geometry.GetComponent<MeshFilter>().sharedMesh;
                var initial = PlacedStructure.CaptureAuthoredPose(root.transform);
                var savedPose = PlacedStructure.CaptureAuthoredPose(root.transform);
                savedPose.x += 12.25f;
                savedPose.z -= 3.75f;
                Require(BaseLayoutLoader.TryGetAuthoredFootprint(root.transform, grid, savedPose, out var origin, out var size), "Prospective authored footprint failed.");
                RequirePose(initial, root.transform); // preflight must not move geometry
                var record = new PlacedStructureData("barracks", origin.x, origin.y, 0, 1)
                { authoredSourceId = "barracks", authoredPose = savedPose };
                state.BaseLayout = new List<PlacedStructureData> { RoundTrip(record) };
                state.Onboarded = true;
                state.EverBuiltStructureIds = new List<string> { "barracks" };
                var placed = loader.Spawn(state.BaseLayout[0], grid);
                Require(placed != null && placed.gameObject == root && loader.Loaded.Count == 0, "Binding replaced geometry or entered destroy-owned list.");
                RequirePose(savedPose, root.transform);
                var actual = geometry.GetComponent<Renderer>().bounds;
                Require(grid.WorldToCell(actual.min) == placed.gridCell, "Footprint starts at pivot rather than actual bounds minimum.");
                for (int x = origin.x; x < origin.x + size.x; x++)
                    for (int z = origin.y; z < origin.y + size.y; z++)
                        Require(grid.OccupantAt(new Vector2Int(x, z)) == "barracks", "Authored geometry cell not reserved.");
                loader.ClearLoaded();
                Require(root != null && geometry.GetComponent<MeshFilter>().sharedMesh == mesh, "ClearLoaded destroyed authored geometry.");
                loader.Rebuild(state.BaseLayout);
                loader.Rebuild(state.BaseLayout);
                Require(root.GetComponents<PlacedStructure>().Length == 1, "Rebuild duplicated authored metadata.");
                RequirePose(savedPose, root.transform);

                var upgrade = typeof(BuildModeController).GetMethod("ApplyUpgradeLevel", BindingFlags.Static | BindingFlags.NonPublic);
                Require(upgrade != null, "Upgrade application seam missing.");
                upgrade.Invoke(null, new object[] { placed, 2 });
                Require(state.BaseLayout[0].level == 2 && placed.level == 2, "Authored upgrade did not persist level.");
                loader.Rebuild(state.BaseLayout);
                RequirePose(savedPose, root.transform);
                Require(geometry.GetComponent<MeshFilter>().sharedMesh == mesh, "Upgrade/reload replaced authored mesh.");

                // Expected negative inputs: mute only their diagnostic emission; every
                // rejection and unchanged-pose outcome remains asserted below.
                bool trace = DeNelle.Core.Diagnostics.FlowTrace.Enabled;
                try
                {
                    DeNelle.Core.Diagnostics.FlowTrace.Enabled = false;
                    var invalid = state.BaseLayout[0];
                    invalid.authoredPose = new AuthoredStructurePose { x = float.NaN, qw = 1, sx = 1, sy = 1, sz = 1 };
                    state.BaseLayout[0] = invalid;
                    Require(loader.Spawn(invalid, grid) == null, "Malformed pose was accepted.");
                    RequirePose(savedPose, root.transform);
                    state.BaseLayout[0] = record;
                    var duplicate = new GameObject("DuplicateAuthoredIdentity");
                    duplicate.AddComponent<AuthoredCastleStorefront>().Configure("barracks", "CastleBarracks");
                    Require(loader.Spawn(record, grid) == null, "Duplicate semantic hosts were accepted.");
                    UnityEngine.Object.DestroyImmediate(duplicate);
                    RequirePose(savedPose, root.transform);
                }
                finally { DeNelle.Core.Diagnostics.FlowTrace.Enabled = trace; }

                state.BaseLayout.Clear();
                state.EverBuiltStructureIds.Clear();
                service.StateReplaced.Invoke();
                Require(root.GetComponent<PlacedStructure>() == null, "State replacement left stale placement ownership.");
                RequirePose(initial, root.transform);
                Require(grid.OccupantAt(origin) == null, "State replacement left phantom occupancy.");
                Require(geometry.GetComponent<MeshFilter>().sharedMesh == mesh, "Unbinding destroyed original geometry.");
                state.BaseLayout.Add(record);
                state.EverBuiltStructureIds.Add("barracks");
                service.StateReplaced.Invoke();
                Require(root.GetComponent<PlacedStructure>() != null, "Later state replacement failed to rebind after an empty state.");
                RequirePose(savedPose, root.transform);

                state.SeenTutorials.Clear();
                Require(!StrategicPlacementMigration.HasExplicitDefaultTownSelection(state), "Unproven/Build-Your-Own state authorized template top-up.");
                state.SeenTutorials[StarterSettlementCompletion.SelectedKey] = true;
                Require(StrategicPlacementMigration.HasExplicitDefaultTownSelection(state), "Persisted Default Town choice was ignored.");
                new GameObject("ApprovedIronMine").AddComponent<AuthoredCastleStorefront>().Configure("collector_forge", "AuthoredIronMine");
                new GameObject("ApprovedCrafting").AddComponent<AuthoredCastleStorefront>().Configure("workshop", "AuthoredCrafting");
                var grant = typeof(StrategicPlacementMigration).GetMethod("GrantAuthoredTemplateIds", BindingFlags.Static | BindingFlags.NonPublic);
                Require(grant != null, "Template grant seam missing.");
                int recordsBefore = state.BaseLayout.Count;
                Require((int)grant.Invoke(null, new object[] { state }) == 2, "Newly authored iron/crafting identities were not granted exactly once.");
                Require((int)grant.Invoke(null, new object[] { state }) == 0 && state.BaseLayout.Count == recordsBefore, "Template grant duplicated rights or wrote replay records.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(loaderHost);
                UnityEngine.Object.DestroyImmediate(gridHost);
                loaderField.SetValue(null, oldLoader);
                gridField.SetValue(null, oldGrid);
            }
        }

        private static PlacedStructureData RoundTrip(PlacedStructureData data) =>
            JsonConvert.DeserializeObject<PlacedStructureData>(JsonConvert.SerializeObject(data, SaveSchema.JsonSettings), SaveSchema.JsonSettings);

        private static void RequirePose(AuthoredStructurePose pose, Transform expected)
        {
            Require(pose != null, "Pose missing after serialization.");
            Require(Vector3.Distance(new Vector3(pose.x, pose.y, pose.z), expected.position) < 0.00001f, "Position changed.");
            Require(Quaternion.Angle(new Quaternion(pose.qx, pose.qy, pose.qz, pose.qw), expected.rotation) < 0.01f, "Rotation changed.");
            Require(Vector3.Distance(new Vector3(pose.sx, pose.sy, pose.sz), expected.localScale) < 0.00001f, "Scale changed.");
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}

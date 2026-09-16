using System;
using System.Collections;
using System.Collections.Generic;
using DeNelle.Core.State;
using DeNelle.Village.World.Camps;
using Newtonsoft.Json;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

namespace DeNelle.Editor
{
    public static class OwnedTownMovePlayProof
    {
        internal const string Arm = "OwnedTownMovePlayProof.Armed";
        internal const string MainSave = "OwnedTownMovePlayProof.MainSave";
        internal const string EntryFailureOnly = "OwnedTownMovePlayProof.EntryFailureOnly";
        internal static MemoryProvider Provider;

        public static void RunPracticeEntryFailure()
        {
            SessionState.SetBool(EntryFailureOnly, true);
            Run();
        }

        public static void Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Play proof requires a closed Play session.");
            SessionState.SetString(MainSave, new LocalSaveProvider().Read(SaveSchema.PlayerPrefsKey) ?? "");
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            SessionState.SetBool(Arm, true);
            EditorApplication.EnterPlaymode();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void IsolateSaves()
        {
            if (!SessionState.GetBool(Arm, false)) return;
            Provider = new MemoryProvider();
            GameStateService.Provider = Provider;
            GameStateService.SuppressCloudForIsolatedProof = true;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void StartDriver()
        {
            if (!SessionState.GetBool(Arm, false)) return;
            SessionState.SetBool(Arm, false);
            new GameObject("OwnedTownMovePlayProof").AddComponent<OwnedTownMovePlayDriver>();
        }

        internal sealed class MemoryProvider : ISaveProvider
        {
            private readonly Dictionary<string, string> _data = new Dictionary<string, string>();
            public bool FailWrites;
            public int FailedWriteCount;
            public bool Exists(string slot) => _data.ContainsKey(slot);
            public string Read(string slot) => _data.TryGetValue(slot, out var data) ? data : "";
            public void Write(string slot, string data)
            { if (FailWrites) { FailedWriteCount++; throw new System.IO.IOException("Injected preview save failure"); } _data[slot] = data; }
            public void Delete(string slot) => _data.Remove(slot);
        }
    }

    public sealed class OwnedTownMovePlayDriver : MonoBehaviour
    {
        private NavMeshDataInstance _navigation;
        private IEnumerator Start()
        {
            bool entryOnly = SessionState.GetBool(OwnedTownMovePlayProof.EntryFailureOnly, false);
            SessionState.SetBool(OwnedTownMovePlayProof.EntryFailureOnly, false);
            var test = entryOnly ? ExercisePracticeEntryFailure() : Exercise();
            while (true)
            {
                bool more;
                try { more = test.MoveNext(); }
                catch (Exception ex)
                {
                    Debug.LogError((entryOnly ? "OWNED_TOWN_PRACTICE_ENTRY_FAIL " : "OWNED_TOWN_MOVE_PLAY_FAIL ") + ex);
                    if (_navigation.valid) _navigation.Remove(); EditorApplication.Exit(1); yield break;
                }
                if (!more) break;
                yield return test.Current;
            }
            if (_navigation.valid) _navigation.Remove();
            Debug.Log(entryOnly ? "OWNED_TOWN_PRACTICE_ENTRY_OK failed startup return preserves uncaptured hero health; main save unchanged" :
                "OWNED_TOWN_MOVE_PLAY_OK actual carve update; blocked route, save failure and cancellation roll back; real town damage restored; main save unchanged");
            EditorApplication.Exit(0);
        }

        private static IEnumerator ExercisePracticeEntryFailure()
        {
            yield return null;
            Require(GameStateService.Provider == OwnedTownMovePlayProof.Provider && OwnedTownMovePlayProof.Provider != null,
                "Entry failure proof must isolate the owner save before runtime startup.");
            var service = GameStateService.Instance;
            Require(service != null, "Runtime save service is missing.");
            // No owned property deliberately prevents the real return router from loading another scene.
            // The production controller still executes its genuine missing-session startup and return path.
            service.State.OwnedBase = null;
            var previous = SceneManager.GetActiveScene();
            var scene = SceneManager.CreateScene(DeNelle.Core.Combat.PracticeCombatPolicy.SceneName);
            SceneManager.SetActiveScene(scene);
            var hero = new GameObject("PracticeEntryFailureHero").AddComponent<DeNelle.Village.HeroHealth>();
            var controller = new GameObject("PracticeEntryFailureController")
                .AddComponent<DeNelle.Village.Arena.OwnedTownPracticeController>();
            yield return null; // Real Start callbacks: hero initializes; controller refuses missing pending session.
            Require(!controller.Running && !string.IsNullOrEmpty(controller.Failure), "Missing-session startup did not refuse.");
            hero.RestoreAfterPractice(Mathf.Min(73f, hero.MaxHp * .73f));
            float before = hero.Hp;
            Require(before > 1f, "Failure fixture requires positive nontrivial hero HP.");
            controller.ReturnToTown();
            Require(Mathf.Approximately(hero.Hp, before), "Unstarted practice changed hero HP from " + before + " to " + hero.Hp + ".");
            controller.ReturnToTown();
            Require(Mathf.Approximately(hero.Hp, before), "Repeated failed-entry return changed hero HP.");
            Destroy(controller.gameObject); Destroy(hero.gameObject);
            SceneManager.SetActiveScene(previous);
            var unload = SceneManager.UnloadSceneAsync(scene);
            float deadline = Time.realtimeSinceStartup + 10f;
            while (unload != null && !unload.isDone && Time.realtimeSinceStartup < deadline) yield return null;
            Require(unload == null || unload.isDone, "Failure fixture scene did not unload within ten seconds.");
            Require((new LocalSaveProvider().Read(SaveSchema.PlayerPrefsKey) ?? "") == SessionState.GetString(OwnedTownMovePlayProof.MainSave, ""),
                "Main player save changed during the entry failure proof.");
        }

        private IEnumerator Exercise()
        {
            yield return null;
            Require(GameStateService.Provider == OwnedTownMovePlayProof.Provider && OwnedTownMovePlayProof.Provider != null,
                "Main save provider was not isolated before runtime startup.");
            var service = GameStateService.Instance;
            Require(service != null, "Runtime save service is missing.");
            service.State.Onboarded = true; // This fixture represents a player who reached final-raid ownership.
            service.State.HeroClass = HeroClassOpt.Knight;
            var scene = SceneManager.CreateScene(OwnedTownScenePose.SceneName);
            SceneManager.SetActiveScene(scene);
            var settings = NavMesh.GetSettingsByIndex(0);
            var source = new NavMeshBuildSource {
                shape = NavMeshBuildSourceShape.Box, size = new Vector3(40, .2f, 40),
                transform = Matrix4x4.TRS(new Vector3(0, -.1f, 0), Quaternion.identity, Vector3.one), area = 0
            };
            var data = NavMeshBuilder.BuildNavMeshData(settings, new List<NavMeshBuildSource> { source },
                new Bounds(Vector3.zero, new Vector3(44, 10, 44)), Vector3.zero, Quaternion.identity);
            Require(data != null, "Fixture navigation bake failed.");
            _navigation = NavMesh.AddNavMeshData(data);
            Marker("HeroStartPoint_PlayerSpawn", new Vector3(-10, 0, 0));
            Marker("RaidStagingPoint", new Vector3(-8, 0, 0));
            Marker("BossSpawn", new Vector3(10, 0, 0));
            var agentHost = new GameObject("ActualNavigationFilter"); agentHost.transform.position = new Vector3(-10, 0, 0);
            var agent = agentHost.AddComponent<NavMeshAgent>(); agent.agentTypeID = settings.agentTypeID;
            var target = new GameObject("MovePreviewTower"); target.transform.position = new Vector3(-4, 0, 0);
            var obstacle = target.AddComponent<NavMeshObstacle>(); obstacle.shape = NavMeshObstacleShape.Box;
            obstacle.center = Vector3.up; obstacle.size = new Vector3(1, 3, 1);
            obstacle.carving = true; obstacle.carveOnlyStationary = true; obstacle.carvingTimeToStationary = 0;
            service.State.OwnedBase = new OwnedBaseState {
                baseId = "move-proof", sourceRaidId = "iron_bastion", templateVersion = "proof-v1",
                captureReceiptId = "proof-receipt", suppliesReceiptId = "proof-receipt",
                structures = new List<OwnedBaseStructure> {
                    new OwnedBaseStructure { instanceId = "tower-proof", placement = new PlacedStructureData("wall_wood", 0, 0, 0, 1) }
                }
            };
            Require(service.TrySave(out var saveReason), saveReason);
            for (int frame = 0; frame < 5; frame++) yield return null;
            Require(!NavMesh.SamplePosition(target.transform.position, out _, .1f, NavMesh.AllAreas), "Fixture obstacle did not carve.");
            Require(OwnedTownNavigation.Validate(scene, out var baselineReason), baselineReason);
            foreach (string mode in new[] { "success", "blocked", "save-failure", "cancel" })
            {
                obstacle.size = mode == "blocked" ? new Vector3(1, 3, 50) : new Vector3(1, 3, 1);
                target.transform.position = mode == "blocked" ? new Vector3(-25, 0, 0) : new Vector3(-4, 0, 0);
                for (int frame = 0; frame < 5; frame++) yield return null;
                string before = JsonConvert.SerializeObject(service.State.OwnedBase);
                Vector3 origin = target.transform.position;
                var proposal = service.State.OwnedBase.Clone(); proposal.revision++;
                bool done = false, accepted = false; int callbacks = 0; string refusal = null;
                OwnedTownMovePlayProof.Provider.FailWrites = mode == "save-failure";
                var host = new GameObject("MoveOperation_" + mode);
                var operation = host.AddComponent<OwnedTownMoveOperation>();
                operation.Begin(target.transform, mode == "blocked" ? Vector3.zero : new Vector3(4, 0, 0), service, proposal,
                    (ok, reason) => { done = true; accepted = ok; callbacks++; refusal = reason; });
                if (mode == "cancel") host.SetActive(false);
                float deadline = Time.realtimeSinceStartup + 5f;
                while (!done && Time.realtimeSinceStartup < deadline) yield return null;
                OwnedTownMovePlayProof.Provider.FailWrites = false;
                Require(done && callbacks == 1, mode + " did not finish exactly once.");
                if (mode == "success")
                {
                    Require(accepted && service.State.OwnedBase.revision == proposal.revision, "Valid move did not commit: " + refusal);
                    Require(NavMesh.SamplePosition(origin, out _, .1f, NavMesh.AllAreas) &&
                        !NavMesh.SamplePosition(target.transform.position, out _, .1f, NavMesh.AllAreas), "Saved before the obstacle carve moved.");
                }
                else Require(!accepted && target.transform.position == origin && JsonConvert.SerializeObject(service.State.OwnedBase) == before,
                    mode + " did not restore preview and preserve saved state.");
                Debug.Log("[OwnedTownMovePlay] " + mode + " passed");
            }
            _navigation.Remove();
            DontDestroyOnLoad(gameObject);
            var manifest = OwnedTownTemplateManifest.Load();
            // 2026-09-16 (WO-1767): the census is derived from the raid scene by the chain (169 after the
            // WO-1732 partition; 221 before). A literal here is the CLAUDE.md §8 duplicated-state failure -
            // the authority is the manifest the chain wrote, so only emptiness is a defect.
            Require(manifest != null && manifest.entries != null && manifest.entries.Count > 0, "Shipped town manifest is missing.");
            var property = new OwnedBaseState {
                baseId = "play-town", sourceRaidId = "iron_bastion", templateVersion = manifest.templateVersion,
                captureReceiptId = "play-town-receipt", suppliesReceiptId = "play-town-receipt",
                milestoneFlags = OwnedBaseMilestones.OwnershipRevealed | OwnedBaseMilestones.EssentialRepairCompleted | OwnedBaseMilestones.LayoutChoiceCompleted
            };
            bool ruinedTower = false, ruinedWall = false;
            foreach (var entry in manifest.entries)
            {
                var record = entry.structure.Clone(); record.condition01 = .375f;
                if (entry.movableTower && !ruinedTower) { record.condition01 = 0f; ruinedTower = true; }
                if (record.placement.itemId == "wall_stone" && !ruinedWall) { record.condition01 = 0f; ruinedWall = true; }
                if (!entry.movableTower && record.placement.itemId != "wall_stone") record.condition01 = 0f;
                property.structures.Add(record);
            }
            var retiredTower = property.structures.Find(s => s.condition01 == 0f && manifest.entries.Exists(e => e.movableTower && e.structure.instanceId == s.instanceId));
            Require(OwnedBaseConstruction.TryRetire(property, property.revision, retiredTower.instanceId, out var soldLayout, out var constructionReason), constructionReason);
            property = soldLayout;
            var upgradedTower = property.structures.Find(s => !s.retired && manifest.entries.Exists(e => e.movableTower && e.structure.instanceId == s.instanceId));
            upgradedTower.condition01 = 1f;
            Require(OwnedBaseConstruction.TryUpgrade(property, property.revision, upgradedTower.instanceId, 1, 3, out var upgradedLayout, out constructionReason), constructionReason);
            property = upgradedLayout;
            bool addedTower = false;
            for (int x = 10; x < 21 && !addedTower; x += 2)
                for (int z = 10; z < 21 && !addedTower; z += 2)
                {
                    Require(OwnedBaseConstruction.TryAdd(property, property.revision, "play-new-tower",
                        new PlacedStructureData("tower_ground_archer", x, z, 0, 1), out var candidate, out constructionReason), constructionReason);
                    if (!OwnedTownLayoutSnapshot.TryCreate(candidate, service.State.HeroClass, out _, out constructionReason)) continue;
                    property = candidate; addedTower = true;
                }
            Require(addedTower, "No legal authored-grid construction candidate was found: " + constructionReason);
            service.State.OwnedBase = property;
            string structuresBefore = JsonConvert.SerializeObject(property.structures);
            Require(service.TrySave(out saveReason), saveReason);
            DeNelle.Core.SceneRouter.GoCastle();
            float castleDeadline = Time.realtimeSinceStartup + 75f;
            while (SceneManager.GetActiveScene().name != DeNelle.Core.SceneRouter.Castle && Time.realtimeSinceStartup < castleDeadline) yield return null;
            Require(SceneManager.GetActiveScene().name == DeNelle.Core.SceneRouter.Castle, "Production castle route did not finish.");
            DeNelle.Village.HeroLocomotion castleHero = null;
            while (Time.realtimeSinceStartup < castleDeadline)
            {
                castleHero = UnityEngine.Object.FindAnyObjectByType<DeNelle.Village.HeroLocomotion>();
                if (castleHero != null && castleHero.gameObject.scene == SceneManager.GetActiveScene()) break;
                yield return null;
            }
            Require(castleHero != null, "The real castle did not supply a hero.");
            // Allow body/equipment startup to finish before the production pre-load hook.
            float settleUntil = Time.realtimeSinceStartup + 3f;
            while (Time.realtimeSinceStartup < settleUntil) yield return null;
            int carriedId = castleHero.GetInstanceID();
            DeNelle.Core.SceneRouter.GoRaid("RaidBase_IronBastion");
            float raidDeadline = Time.realtimeSinceStartup + 60f;
            while (SceneManager.GetActiveScene().name != "RaidBase_IronBastion" && Time.realtimeSinceStartup < raidDeadline) yield return null;
            for (int frame = 0; frame < 8; frame++) yield return null;
            Require(SceneManager.GetActiveScene().name == "RaidBase_IronBastion" && castleHero != null &&
                castleHero.GetInstanceID() == carriedId && castleHero.transform.root.gameObject.scene == SceneManager.GetActiveScene(),
                "The real hero did not arrive in the final raid through the production carry route.");
            DeNelle.Core.SceneRouter.GoOwnedTown();
            float enterDeadline = Time.realtimeSinceStartup + 60f;
            while (SceneManager.GetActiveScene().name != OwnedTownScenePose.SceneName && Time.realtimeSinceStartup < enterDeadline) yield return null;
            Require(SceneManager.GetActiveScene().name == OwnedTownScenePose.SceneName, "Production owned-town route did not finish.");
            float townDeadline = Time.realtimeSinceStartup + 10f;
            OwnedTownController town = null;
            while (Time.realtimeSinceStartup < townDeadline)
            {
                town = UnityEngine.Object.FindAnyObjectByType<OwnedTownController>();
                if (town != null && town.Reconstructed && service.State.OwnedBase.reenteredLayoutRevision > 0) break;
                yield return null;
            }
            Require(town != null && town.Reconstructed, "Actual town did not reconstruct: " + town?.Failure);
            for (int frame = 0; frame < 120; frame++) yield return null;
            Require(castleHero != null && castleHero.GetInstanceID() == carriedId &&
                castleHero.transform.root.gameObject.scene == town.gameObject.scene,
                "Town entry lost the actual castle hero or left it in DontDestroyOnLoad.");
            Require(UnityEngine.Object.FindObjectsByType<DeNelle.Village.HeroLocomotion>(FindObjectsSortMode.None).Length == 1,
                "Town entry duplicated the hero.");
            Require(service.State.OwnedBase.reenteredLayoutRevision > 0, "Actual town entry did not persist reentry.");
            Require(JsonConvert.SerializeObject(service.State.OwnedBase.structures) == structuresBefore, "Play-mode restoration rewrote the capture ledger.");
            foreach (var record in property.structures)
            {
                if (record.condition01 == 0f)
                {
                    if (OwnedTownScenePose.TryResolveStructure(town.gameObject.scene, record, out var ruin, out _))
                    {
                        if (record.retired) { Require(!ruin.gameObject.activeInHierarchy, "A sold captured structure reappeared."); continue; }
                        var ruinWall = ruin.GetComponent<DeNelle.Village.WallSegment>();
                        var ruinTower = ruin.GetComponent<DeNelle.Village.DefenseTower>();
                        var ruinSpire = ruin.GetComponent<RaidSpire>();
                        float condition = ruinWall != null ? ruinWall.HpFraction : ruinTower != null ? ruinTower.HpFraction : ruinSpire.HpFraction;
                        Require(condition == 0f, "A destroyed captured structure revived during Start.");
                        var blocker = ruin.GetComponent<NavMeshObstacle>();
                        Require(blocker == null || !blocker.enabled || !ruin.gameObject.activeInHierarchy, "A destroyed structure still carves navigation.");
                    }
                    continue;
                }
                Require(OwnedTownScenePose.TryResolveStructure(town.gameObject.scene, record, out var standing, out var reason), reason);
                var wall = standing.GetComponent<DeNelle.Village.WallSegment>();
                var tower = standing.GetComponent<DeNelle.Village.DefenseTower>();
                float hp = wall != null ? wall.HpFraction : tower != null ? tower.HpFraction : standing.GetComponent<RaidSpire>().HpFraction;
                Require(Mathf.Abs(hp - record.condition01) < .0001f, "Runtime lifecycle reset saved structure health.");
            }
            Require(OwnedTownNavigation.Validate(town.gameObject.scene, out var townPathReason), "Actual town paths: " + townPathReason);
            Require(OwnedTownScenePose.TryResolveStructure(town.gameObject.scene, property.structures.Find(s => s.instanceId == "play-new-tower"), out var newBody, out constructionReason), constructionReason);
            Require(newBody.GetComponent<DeNelle.Village.PlacedStructure>()?.level == 1, "New construction did not replay its level.");
            Require(OwnedTownScenePose.TryResolveStructure(town.gameObject.scene, property.structures.Find(s => s.instanceId == upgradedTower.instanceId), out var upgradedBody, out constructionReason), constructionReason);
            Require(upgradedBody.GetComponent<DeNelle.Village.PlacedStructure>()?.level == 2, "Inherited upgrade did not replay its level.");
            Debug.Log("OWNED_TOWN_CONSTRUCTION_IMPORT_OK new tower, inherited upgrade, retained sold identity, unchanged layout and valid paths; UI construction/payment not exercised");
            Debug.Log("OWNED_TOWN_REAL_PLAY_OK damaged structures, destruction lifecycle, reentry save and active-agent courtyard paths");
            var townPanel = UnityEngine.Object.FindAnyObjectByType<OwnedTownPanel>();
            var privateInstance = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            typeof(OwnedTownPanel).GetField("_selected", privateInstance).SetValue(townPanel, "play-new-tower");
            townPanel.Show();
            ClickTownButton(townPanel, "upgrade");
            yield return null;
            var upgradePanel = UnityEngine.Object.FindAnyObjectByType<DeNelle.Village.Buildings.Progression.BuildingUpgradePanelMvvm>();
            Require(upgradePanel != null && upgradePanel.IsOpen, "Actual town Upgrade button did not open the existing page.");
            var upgradeVm = (DeNelle.Village.Buildings.Progression.BuildingUpgradeVM)upgradePanel.GetType().GetField("_vm", privateInstance).GetValue(upgradePanel);
            Require(upgradeVm.BuildingId == OwnedTownJobKey.Compose(service.State.OwnedBase.baseId, "play-new-tower") && upgradeVm.CurrentTier == 1,
                "Town Upgrade button opened a story ladder instead of the selected owned instance.");
            upgradePanel.GetType().GetMethod("Close", privateInstance).Invoke(upgradePanel, null);
            Debug.Log("OWNED_TOWN_UPGRADE_DOOR_OK actual button opens existing upgrade page for the selected owned identity");
            var gridMoveTest = VerifyGridMove(service, newBody);
            while (gridMoveTest.MoveNext()) yield return gridMoveTest.Current;
            var priorSceneHandle = town.gameObject.scene.handle;
            int savedRevision = service.State.OwnedBase.revision;
            DeNelle.Core.SceneRouter.GoOwnedTown();
            float reloadDeadline = Time.realtimeSinceStartup + 30f;
            while (SceneManager.GetActiveScene().handle == priorSceneHandle && Time.realtimeSinceStartup < reloadDeadline) yield return null;
            for (int frame = 0; frame < 120; frame++) yield return null;
            Require(SceneManager.GetActiveScene().handle != priorSceneHandle && castleHero != null && castleHero.GetInstanceID() == carriedId &&
                castleHero.transform.root.gameObject.scene == SceneManager.GetActiveScene(), "Town revisit did not preserve and rehome the carried hero.");
            Require(service.State.OwnedBase.revision == savedRevision, "Town revisit repeatedly advanced the completed reentry milestone.");
            var movedRecord = service.State.OwnedBase.structures.Find(s => s.instanceId == "play-new-tower");
            Require(OwnedTownScenePose.TryResolveStructure(SceneManager.GetActiveScene(), movedRecord, out var movedBody, out var movedReason), movedReason);
            Require(movedBody.GetComponent<DeNelle.Village.PlacedStructure>().gridCell ==
                new Vector2Int(movedRecord.placement.cellX, movedRecord.placement.cellZ), "Reentry lost moved construction grid metadata.");
            Debug.Log("OWNED_TOWN_HERO_CARRY_OK real castle hero crosses final raid to owned town and revisits via SceneRouter; no duplicate or DDOL leak; victory settlement not invoked");
            var repairTest = VerifyRepairButtons(service, castleHero);
            while (repairTest.MoveNext()) yield return repairTest.Current;
            VerifyOwnedUpgradeTimer(service);
            var saleTest = VerifyOwnedSale(service);
            while (saleTest.MoveNext()) yield return saleTest.Current;
            var placementTest = VerifyOwnedPlacement(service);
            while (placementTest.MoveNext()) yield return placementTest.Current;
            var inputTest = VerifyOwnedPlacementInput(service);
            while (inputTest.MoveNext()) yield return inputTest.Current;
            var wallTest = VerifyOwnedWall(service);
            while (wallTest.MoveNext()) yield return wallTest.Current;
            var buildTest = VerifyOwnedNewBuild(service);
            while (buildTest.MoveNext()) yield return buildTest.Current;
            VerifyPracticeKill(service, castleHero.transform.position);
            var practiceTest = VerifyPracticeMatch(service);
            while (practiceTest.MoveNext()) yield return practiceTest.Current;
            Require((new LocalSaveProvider().Read(SaveSchema.PlayerPrefsKey) ?? "") == SessionState.GetString(OwnedTownMovePlayProof.MainSave, ""),
                "Main player save changed during the isolated proof.");
        }

        private static IEnumerator VerifyOwnedWall(GameStateService service)
        {
            var source = service.State.OwnedBase.structures.Find(s => s.instanceId == "play-new-tower");
            var placement = new PlacedStructureData("wall_wood", source.placement.cellX, source.placement.cellZ, 0, 1);
            string story = JsonConvert.SerializeObject(service.State.BaseLayout);
            bool done = false, saved = false;
            string refusal = null;
            Require(OwnedTownConstructionService.TryBeginBuild(placement, (ok, reason) => { done = true; saved = ok; refusal = reason; }, out var failure), failure);
            float deadline = Time.realtimeSinceStartup + 6f;
            while (!done && Time.realtimeSinceStartup < deadline) yield return null;
            Require(done && saved, "Player wall construction failed: " + refusal);
            var record = service.State.OwnedBase.structures.FindLast(s => !s.retired && s.inheritedPose == null && s.placement.itemId == "wall_wood");
            Require(record != null, "Wall construction did not save an owned record.");
            var timer = DeNelle.Village.BuildTimerService.Instance;
            if (record.constructionPending) timer.CompleteJob(OwnedTownJobKey.Compose(service.State.OwnedBase.baseId, record.instanceId));
            yield return null;
            record = service.State.OwnedBase.structures.Find(s => s.instanceId == record.instanceId);
            Require(!record.constructionPending && OwnedTownTemplateManifest.Load().IsEditableStructure(record), "Completed player wall is not editable.");
            Require(OwnedTownScenePose.TryResolveStructure(SceneManager.GetActiveScene(), record, out var body, out failure), failure);
            var panel = UnityEngine.Object.FindAnyObjectByType<OwnedTownPanel>();
            panel.Show(); ClickTownButton(panel, "build");
            var builder = DeNelle.Village.BuildModeController.EnsureExists();
            var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            builder.GetType().GetMethod("SelectStructure", flags).Invoke(builder, new object[] { body.GetComponent<DeNelle.Village.PlacedStructure>() });
            Require(!builder.IsActive && (string)typeof(OwnedTownPanel).GetField("_selected", flags).GetValue(panel) == record.instanceId,
                "Player wall selection opened a different structure.");
            int revision = service.State.OwnedBase.revision;
            var destination = body.position + Vector3.right * 3f;
            done = false; saved = false;
            Require(OwnedTownDesignService.TryBeginMove(record.instanceId, destination,
                (ok, reason) => { done = true; saved = ok; refusal = reason; }, out failure), failure);
            deadline = Time.realtimeSinceStartup + 6f;
            while (!done && Time.realtimeSinceStartup < deadline) yield return null;
            Require(done && saved && service.State.OwnedBase.revision == revision + 1 && Vector3.Distance(body.position, destination) < .01f,
                "Player wall move did not save its live grid position: " + refusal);
            var fixedWall = service.State.OwnedBase.structures.Find(s => s.inheritedPose != null && s.placement.itemId == "wall_stone");
            Require(fixedWall != null && !OwnedTownTemplateManifest.Load().IsEditableStructure(fixedWall), "Inherited perimeter became editable.");
            Require(OwnedTownConstructionService.TrySell(record.instanceId, service.State.OwnedBase.revision, out _, out failure), failure);
            Require(service.Load() && service.State.OwnedBase.structures.Find(s => s.instanceId == record.instanceId).retired &&
                JsonConvert.SerializeObject(service.State.BaseLayout) == story, "Wall sale/reload changed story or lost the tombstone.");
            Debug.Log("OWNED_TOWN_WALL_EDIT_OK actual wall construction, completed selection, navigation-validated move, sale and cold load; fitted perimeter protected");
        }

        private sealed class PlacementInput : DeNelle.Village.IBuildInput
        {
            public Vector2 Point;
            public bool Tap;
            public Vector2 ScreenPoint => Point;
            public bool PlaceOrSelect { get { bool value = Tap; Tap = false; return value; } }
            public bool Cancel => false;
            public bool Rotate => false;
        }

        private static IEnumerator VerifyOwnedPlacementInput(GameStateService service)
        {
            var panel = UnityEngine.Object.FindAnyObjectByType<OwnedTownPanel>();
            panel.Show(); ClickTownButton(panel, "build");
            var builder = DeNelle.Village.BuildModeController.Instance;
            var input = new PlacementInput(); builder.SetInput(input);
            int supported = 0, blocked = 0;
            string unchanged = JsonConvert.SerializeObject(service.State.OwnedBase);
            foreach (DeNelle.Core.Catalog.BuildType type in Enum.GetValues(typeof(DeNelle.Core.Catalog.BuildType)))
            {
                using (var palette = DeNelle.Village.BuildPaletteVM.CreateDefault(type, null))
                    foreach (var card in palette.Cards)
                    {
                        if (OwnedTownLayoutSnapshot.SupportsConstruction(card.Entry)) { supported++; continue; }
                        blocked++;
                        Require(card.Locked && !string.IsNullOrEmpty(card.LockReason) && !builder.ArmById(card.Id),
                            "Unsupported town card can arm or has no explanation: " + card.Id);
                    }
            }
            Require(supported > 0 && blocked > 0 && JsonConvert.SerializeObject(service.State.OwnedBase) == unchanged,
                "Town palette proof did not cover both supported and blocked cards, or mutated ownership.");
            var record = service.State.OwnedBase.structures.Find(s => s.instanceId == "play-new-tower");
            Require(builder.ArmById(record.placement.itemId), "Could not arm the input placement proof.");
            var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            var camera = (Camera)builder.GetType().GetField("_camera", flags).GetValue(builder);
            var grid = OwnedTownConstructionService.FindGrid();
            var position = grid.CellToWorld(new Vector2Int(record.placement.cellX, record.placement.cellZ));
            for (int i = 0; i < 5; i++) yield return null;
            input.Point = camera.WorldToScreenPoint(position);
            Require(builder.ProbeArmedPlacementAt(input.Point, out var rejection),
                "Actual owned ghost rejected the previously validated construction cell: " + rejection + " at " + input.Point);
            var outside = new OwnedBaseStructure { placement = new PlacedStructureData(record.placement.itemId, 34, 34, 0, 1) };
            Require(!OwnedTownLayoutSnapshot.TryGridBounds(outside, out _, out _), "Shared preview/save rule accepted a footprint outside the town plot.");
            Debug.Log("OWNED_TOWN_PALETTE_RULES_OK supported cards retained, unavailable cards visibly locked and arm-refused; shared plot bounds reject outside footprint");
            int count = service.State.OwnedBase.structures.Count;
            string wallet = JsonConvert.SerializeObject(service.State.Resources);
            input.Tap = true;
            for (int i = 0; i < 3; i++) yield return null;
            Require((bool)builder.GetType().GetField("_dropPending", flags).GetValue(builder) &&
                service.State.OwnedBase.structures.Count == count && JsonConvert.SerializeObject(service.State.Resources) == wallet,
                "World input did not drop the ghost without committing or charging.");
            var hud = builder.GetType().GetField("_hud", flags).GetValue(builder);
            var confirm = (UnityEngine.UI.Button)hud.GetType().GetField("_okChip", flags).GetValue(hud);
            Require(confirm != null && confirm.interactable && confirm.gameObject.activeInHierarchy, "The real Place button is unavailable.");
            confirm.onClick.Invoke();
            float deadline = Time.realtimeSinceStartup + 6f;
            while (service.State.OwnedBase.structures.Count == count && Time.realtimeSinceStartup < deadline) yield return null;
            Require(service.State.OwnedBase.structures.Count == count + 1 && !OwnedTownConstructionService.IsBusy,
                "The real Place listener did not pass the dropped-ghost validation and commit one owned building.");
            var added = service.State.OwnedBase.structures[count];
            Require(added.placement.cellX == record.placement.cellX && added.placement.cellZ == record.placement.cellZ,
                "World drop and confirmation placed a different cell.");
            builder.Exit();
            Require(DeNelle.Village.BuildTimerService.Instance.TryCancelOwnedTownUpgrade(DeNelle.Core.Jobs.ChannelId.Builder,
                OwnedTownJobKey.Compose(service.State.OwnedBase.baseId, added.instanceId), out _, out var reason), reason);
            yield return null;
            Debug.Log("OWNED_TOWN_PLACEMENT_INPUT_OK injected screen-space world tap, actual ray/ghost validity, drop without payment and real Place listener commit; physical device not exercised");
        }

        private static IEnumerator VerifyOwnedPlacement(GameStateService service)
        {
            var panel = UnityEngine.Object.FindAnyObjectByType<OwnedTownPanel>();
            string story = JsonConvert.SerializeObject(service.State.BaseLayout);
            panel.Show(); ClickTownButton(panel, "build");
            var builder = DeNelle.Village.BuildModeController.Instance;
            var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            var grid = OwnedTownConstructionService.FindGrid();
            Require(builder != null && builder.IsActive && ReferenceEquals(builder.GetType().GetField("_grid", flags).GetValue(builder), grid),
                "Actual Build button did not open the existing builder on the owned grid.");
            var old = service.State.OwnedBase.structures.Find(s => s.instanceId == "play-new-tower");
            var entry = DeNelle.Core.Catalog.CatalogRegistry.Get(old.placement.itemId);
            var cell = new Vector2Int(old.placement.cellX, old.placement.cellZ);
            var footprint = grid.FootprintCells(DeNelle.Village.StructureFactory.MeasureClaimFootprintXZ(entry), 0f);
            var position = grid.CellToWorld(cell);
            Require(builder.ArmById(entry.id), "Existing palette arm path refused the owned tower.");
            var place = builder.GetType().GetMethod("Place", flags);
            var args = new object[] { cell, footprint, position, false };
            string before = JsonConvert.SerializeObject(service.State.OwnedBase);
            string wallet = JsonConvert.SerializeObject(service.State.Resources);
            string everBuilt = JsonConvert.SerializeObject(service.State.EverBuiltStructureIds);
            int wood = service.State.Wood, iron = service.State.Iron;
            int failures = OwnedTownMovePlayProof.Provider.FailedWriteCount;
            OwnedTownMovePlayProof.Provider.FailWrites = true;
            place.Invoke(builder, args);
            float deadline = Time.realtimeSinceStartup + 6f;
            while (OwnedTownConstructionService.IsBusy && Time.realtimeSinceStartup < deadline) yield return null;
            OwnedTownMovePlayProof.Provider.FailWrites = false;
            Require(!OwnedTownConstructionService.IsBusy && OwnedTownMovePlayProof.Provider.FailedWriteCount > failures && grid.CanPlace(cell, footprint) &&
                JsonConvert.SerializeObject(service.State.OwnedBase) == before && JsonConvert.SerializeObject(service.State.Resources) == wallet &&
                service.State.Wood == wood && service.State.Iron == iron && JsonConvert.SerializeObject(service.State.EverBuiltStructureIds) == everBuilt,
                "Failed real placement did not restore preview occupancy/property/payment/history.");
            for (int i = 0; i < 4; i++) yield return null;
            place.Invoke(builder, args);
            Require(OwnedTownConstructionService.IsBusy, "Cancellation probe did not start a placement preview.");
            builder.Exit();
            Require(!OwnedTownConstructionService.IsBusy && grid.CanPlace(cell, footprint) &&
                JsonConvert.SerializeObject(service.State.OwnedBase) == before &&
                JsonConvert.SerializeObject(service.State.Resources) == wallet,
                "Exiting build mode did not cancel its unsaved placement preview.");
            for (int i = 0; i < 4; i++) yield return null;
            panel.Show(); ClickTownButton(panel, "build");
            Require(builder.ArmById(entry.id), "Could not rearm after cancelling the preview.");
            int count = service.State.OwnedBase.structures.Count;
            place.Invoke(builder, args);
            deadline = Time.realtimeSinceStartup + 6f;
            while (OwnedTownConstructionService.IsBusy && Time.realtimeSinceStartup < deadline) yield return null;
            Require(!OwnedTownConstructionService.IsBusy && service.State.OwnedBase.structures.Count == count + 1,
                "Existing Place commit did not create one saved owned structure.");
            var added = service.State.OwnedBase.structures[count];
            Require(added.inheritedPose == null && added.constructionPending && service.State.HasEverBuilt(entry.id) &&
                OwnedTownScenePose.TryResolveStructure(SceneManager.GetActiveScene(), added, out var body, out _),
                "Placement did not persist construction/history or its live identity.");
            Require(!grid.CanPlace(cell, footprint), "Committed preview did not retain occupancy.");
            Require(!OwnedTownConstructionService.TryBeginBuild(added.placement, null, out _), "Occupied placement was accepted twice.");
            Require(OwnedTownScenePose.TryResolveStructure(SceneManager.GetActiveScene(), added, out var pendingTarget, out _), "Placed body is missing.");
            var select = builder.GetType().GetMethod("SelectStructure", flags);
            select.Invoke(builder, new object[] { pendingTarget.GetComponent<DeNelle.Village.PlacedStructure>() });
            Require(!builder.IsActive, "Selecting unfinished construction did not leave placement mode.");
            var jobPanel = UnityEngine.Object.FindAnyObjectByType<DeNelle.Village.Buildings.Progression.BuildingUpgradePanelMvvm>();
            Require(jobPanel != null && jobPanel.IsOpen, "Unfinished selection did not open the construction page.");
            var jobVm = (DeNelle.Village.Buildings.Progression.BuildingUpgradeVM)jobPanel.GetType().GetField("_vm", flags).GetValue(jobPanel);
            Require(jobVm.BuildingId == OwnedTownJobKey.Compose(service.State.OwnedBase.baseId, added.instanceId) && jobVm.UnderConstruction,
                "Unfinished selection opened another structure or lost its live job.");
            jobPanel.GetType().GetMethod("Close", flags).Invoke(jobPanel, null);
            Debug.Log("OWNED_TOWN_PENDING_SELECTION_OK actual selection opens the existing page for the exact owned construction job");
            builder.Exit();
            Require(JsonConvert.SerializeObject(service.State.BaseLayout) == story, "Owned builder entry/place/exit mutated story BaseLayout.");
            var timer = DeNelle.Village.BuildTimerService.Instance;
            Require(timer.TryCancelOwnedTownUpgrade(DeNelle.Core.Jobs.ChannelId.Builder,
                OwnedTownJobKey.Compose(service.State.OwnedBase.baseId, added.instanceId), out _, out var reason), reason);
            Require(grid.CanPlace(cell, footprint) && service.State.OwnedBase.structures.Find(s => s.instanceId == added.instanceId).retired,
                "Cancelling the placed structure did not free its grid and retain the cancelled identity.");
            yield return null;
            var editable = service.State.OwnedBase.structures.Find(s => s.inheritedPose != null && s.condition01 > 0f &&
                OwnedTownTemplateManifest.Load().IsEditableTower(s));
            Require(OwnedTownScenePose.TryResolveStructure(SceneManager.GetActiveScene(), editable, out var editableBody, out _), "Captured editable tower is missing.");
            panel.Show(); ClickTownButton(panel, "build");
            select.Invoke(builder, new object[] { editableBody.GetComponent<DeNelle.Village.PlacedStructure>() });
            Require(!builder.IsActive && (string)typeof(OwnedTownPanel).GetField("_selected", flags).GetValue(panel) == editable.instanceId,
                "Existing builder selection did not open owned controls for the correct captured identity.");
            Debug.Log("OWNED_TOWN_PLACEMENT_OK actual Build doorway and palette arm/Place commit, live navigation, failed-save preview/grid/payment/history rollback, retry, cancellation and story isolation; physical pointer not exercised");
        }

        private static bool StartOwnedBuild(DeNelle.Village.BuildTimerService timer, string id, PlacedStructureData data,
            int revision, DeNelle.Core.Catalog.ResourceCost price, out string reason, bool free = false)
        {
            var args = new object[] { id, data, revision, price, free, DeNelle.Village.BuildGraceReason.None, null, null };
            bool result = (bool)timer.GetType().GetMethod("TryStartOwnedTownBuild",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Invoke(timer, args);
            reason = args[7] as string;
            return result;
        }

        private static IEnumerator VerifyOwnedNewBuild(GameStateService service)
        {
            var timer = DeNelle.Village.BuildTimerService.Instance;
            var old = service.State.OwnedBase.structures.Find(s => s.instanceId == "play-new-tower");
            var data = old.placement; data.level = 1;
            string id = "play-timed-construction";
            string key = OwnedTownJobKey.Compose(service.State.OwnedBase.baseId, id);
            var price = DeNelle.Village.BuildModeController.CostFor(DeNelle.Core.Catalog.CatalogRegistry.Get(data.itemId));
            Require(service.TrySave(out var reason), reason);
            string before = JsonConvert.SerializeObject(service.State.OwnedBase);
            string wallet = JsonConvert.SerializeObject(service.State.Resources);
            string queue = JsonConvert.SerializeObject(service.State.ObsidianQueue);
            string story = JsonConvert.SerializeObject(service.State.BaseLayout);
            int wood = service.State.Wood, iron = service.State.Iron;
            int failures = OwnedTownMovePlayProof.Provider.FailedWriteCount;
            OwnedTownMovePlayProof.Provider.FailWrites = true;
            Require(!StartOwnedBuild(timer, id, data, service.State.OwnedBase.revision, price, out _) &&
                OwnedTownMovePlayProof.Provider.FailedWriteCount > failures &&
                JsonConvert.SerializeObject(service.State.OwnedBase) == before && JsonConvert.SerializeObject(service.State.Resources) == wallet &&
                service.State.Wood == wood && service.State.Iron == iron && JsonConvert.SerializeObject(service.State.ObsidianQueue) == queue,
                "Failed construction start changed property, payment or queue.");
            OwnedTownMovePlayProof.Provider.FailWrites = false;
            Require(StartOwnedBuild(timer, id, data, service.State.OwnedBase.revision, price, out reason), reason);
            Require(service.State.Wood == wood - price.wood && service.State.Iron == iron - price.iron &&
                service.State.OwnedBase.structures.Find(s => s.instanceId == id).constructionPending && timer.IsBuilding(key),
                "Construction did not save payment, pending state and job together.");
            Require(service.Load() && service.State.OwnedBase.structures.Find(s => s.instanceId == id).constructionPending && timer.IsBuilding(key),
                "Cold load completed construction early or lost its job.");
            Require(OwnedTownLayoutSnapshot.TryCreate(service.State.OwnedBase, service.State.HeroClass, out var pendingSnapshot, out reason), reason);
            pendingSnapshot.rulesetId = OwnedTownLayoutSnapshot.ConstructionRuleset;
            Require(!pendingSnapshot.TryValidateAndCopy(new OwnedTownLayoutSnapshot(OwnedTownTemplateManifest.Load()), out _, out _),
                "Old snapshot rules accepted an unfinished structure they cannot interpret.");
            int handle = SceneManager.GetActiveScene().handle;
            DeNelle.Core.SceneRouter.GoOwnedTown();
            float deadline = Time.realtimeSinceStartup + 30f;
            while (SceneManager.GetActiveScene().handle == handle && Time.realtimeSinceStartup < deadline) yield return null;
            for (int i = 0; i < 120; i++) yield return null;
            Require(SceneManager.GetActiveScene().handle != handle, "Construction replay did not enter the town.");
            var pending = service.State.OwnedBase.structures.Find(s => s.instanceId == id);
            Require(OwnedTownScenePose.TryResolveStructure(SceneManager.GetActiveScene(), pending, out var body, out reason), reason);
            Require(DeNelle.Village.UnderConstructionVisual.IsUnderConstruction(body.gameObject) && !body.GetComponent<DeNelle.Village.DefenseTower>().enabled,
                "Replayed unfinished tower has no scaffold or can fight before completion.");
            CaptureConstruction(body);
            var job = service.State.ObsidianQueue.Channel(DeNelle.Core.Jobs.ChannelId.Builder).ActiveJobs.Find(j => j.StructureId == key);
            failures = OwnedTownMovePlayProof.Provider.FailedWriteCount;
            OwnedTownMovePlayProof.Provider.FailWrites = true;
            timer.CompleteJob(key);
            Require(OwnedTownMovePlayProof.Provider.FailedWriteCount > failures && timer.IsBuilding(key) &&
                service.State.OwnedBase.structures.Find(s => s.instanceId == id).constructionPending,
                "Failed completion removed the job or completed its saved structure.");
            OwnedTownMovePlayProof.Provider.FailWrites = false;
            timer.CompleteJob(key);
            yield return null;
            Require(!timer.IsBuilding(key) && !service.State.OwnedBase.structures.Find(s => s.instanceId == id).constructionPending &&
                body.GetComponent<DeNelle.Village.DefenseTower>().enabled, "Completed construction did not release combat and persist completion.");
            Require(OwnedTownUpgradeCompletion.TryPlan(job, out var replay, out reason) && replay == null,
                "A replayed construction completion did not recognize its durable receipt.");
            Require(service.Load() && !service.State.OwnedBase.structures.Find(s => s.instanceId == id).constructionPending && !timer.IsBuilding(key),
                "Completed construction did not cold-load.");
            Require(OwnedTownLayoutSnapshot.TryCreate(service.State.OwnedBase, service.State.HeroClass, out var completedSnapshot, out reason), reason);
            completedSnapshot.rulesetId = OwnedTownLayoutSnapshot.ConstructionRuleset;
            Require(completedSnapshot.TryValidateAndCopy(new OwnedTownLayoutSnapshot(OwnedTownTemplateManifest.Load()), out _, out reason),
                "Completed legacy construction snapshot no longer replays: " + reason);
            Require(OwnedTownConstructionService.TrySell(id, service.State.OwnedBase.revision, out _, out reason), reason);
            string cancelId = "play-cancel-construction";
            string cancelKey = OwnedTownJobKey.Compose(service.State.OwnedBase.baseId, cancelId);
            wallet = JsonConvert.SerializeObject(service.State.Resources); wood = service.State.Wood; iron = service.State.Iron;
            Require(StartOwnedBuild(timer, cancelId, data, service.State.OwnedBase.revision, price, out reason), reason);
            OwnedTownMovePlayProof.Provider.FailWrites = true;
            Require(!timer.TryCancelOwnedTownUpgrade(DeNelle.Core.Jobs.ChannelId.Builder, cancelKey, out _, out _) &&
                timer.IsBuilding(cancelKey) && service.State.OwnedBase.structures.Find(s => s.instanceId == cancelId).constructionPending,
                "Failed construction cancellation removed the job or pending record.");
            OwnedTownMovePlayProof.Provider.FailWrites = false;
            Require(timer.TryCancelOwnedTownUpgrade(DeNelle.Core.Jobs.ChannelId.Builder, cancelKey, out _, out reason), reason);
            Require(service.Load() && !timer.IsBuilding(cancelKey) &&
                service.State.OwnedBase.structures.Find(s => s.instanceId == cancelId).retired &&
                JsonConvert.SerializeObject(service.State.Resources) == wallet && service.State.Wood == wood && service.State.Iron == iron &&
                JsonConvert.SerializeObject(service.State.BaseLayout) == story,
                "Construction cancellation did not persist a tombstone with its exact full refund or changed story state.");
            service.State.FreeBuildsUsed = new List<string>();
            service.State.GearInventory["convenience:instant_build"] = 1;
            Require(service.TrySave(out reason), reason);
            int revision = service.State.OwnedBase.revision;
            OwnedTownMovePlayProof.Provider.FailWrites = true;
            Require(!StartOwnedBuild(timer, "play-instant-construction", data, revision, default, out _, true) &&
                service.State.FreeBuildsUsed.Count == 0 && service.State.GearInventory["convenience:instant_build"] == 1 &&
                service.State.OwnedBase.revision == revision,
                "Failed instant construction consumed a free build or token.");
            OwnedTownMovePlayProof.Provider.FailWrites = false;
            Require(StartOwnedBuild(timer, "play-instant-construction", data, revision, default, out reason, true), reason);
            Require(service.Load() && service.State.FreeBuildsUsed.Count == 1 && service.State.FreeBuildsUsed[0] == data.itemId &&
                service.State.GearInventory["convenience:instant_build"] == 0 &&
                !service.State.OwnedBase.structures.Find(s => s.instanceId == "play-instant-construction").constructionPending &&
                !timer.IsBuilding(OwnedTownJobKey.Compose(service.State.OwnedBase.baseId, "play-instant-construction")),
                "Instant construction did not persist the token/free-build receipt and completion together.");
            Debug.Log("OWNED_TOWN_NEW_BUILD_OK atomic scheduling/rollback/cold load, actual scaffold replay/combat gate, completion rollback/retry/receipt, cancellation full refund and tombstone");
        }

        private static void CaptureConstruction(Transform body)
        {
            var camera = Camera.main;
            Require(camera != null, "Construction capture needs the real town camera.");
            var position = camera.transform.position; var rotation = camera.transform.rotation;
            var previousTarget = camera.targetTexture; var previousActive = RenderTexture.active;
            var target = new RenderTexture(1920, 1080, 24, RenderTextureFormat.ARGB32);
            var texture = new Texture2D(1920, 1080, TextureFormat.RGB24, false);
            var visual = body.GetComponent<DeNelle.Village.UnderConstructionVisual>();
            var update = visual.GetType().GetMethod("Update", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            try
            {
                camera.transform.position = body.position + new Vector3(12f, 9f, -12f);
                camera.transform.LookAt(body.position + Vector3.up * 3f);
                update.Invoke(visual, null);
                camera.targetTexture = target;
                var request = new UnityEngine.Rendering.RenderPipeline.StandardRequest { destination = target };
                if (UnityEngine.Rendering.RenderPipeline.SupportsRenderRequest(camera, request)) camera.SubmitRenderRequest(request);
                else camera.Render();
                RenderTexture.active = target;
                texture.ReadPixels(new Rect(0, 0, 1920, 1080), 0, 0); texture.Apply();
                string path = System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "../Builds/owned-town-construction-live.png"));
                System.IO.File.WriteAllBytes(path, texture.EncodeToPNG());
                Require(new System.IO.FileInfo(path).Length > 4096, "Construction capture is empty.");
                Debug.Log("OWNED_TOWN_CONSTRUCTION_CAPTURE_OK " + path);
            }
            finally
            {
                camera.targetTexture = previousTarget; RenderTexture.active = previousActive;
                camera.transform.SetPositionAndRotation(position, rotation); update.Invoke(visual, null);
                UnityEngine.Object.DestroyImmediate(texture); target.Release(); UnityEngine.Object.DestroyImmediate(target);
            }
        }

        private static IEnumerator VerifyOwnedSale(GameStateService service)
        {
            var record = service.State.OwnedBase.structures.Find(s => s.instanceId == "play-new-tower");
            var scene = SceneManager.GetActiveScene();
            Require(OwnedTownScenePose.TryResolveStructure(scene, record, out var body, out var reason), reason);
            var fixedRecord = service.State.OwnedBase.structures.Find(s => !s.retired && s.inheritedPose != null &&
                !OwnedTownTemplateManifest.Load().IsEditableTower(s));
            Require(fixedRecord != null && !OwnedTownConstructionService.TryQuoteSale(fixedRecord.instanceId, out _, out _),
                "The town perimeter or objective can be sold.");
            string key = OwnedTownJobKey.Compose(service.State.OwnedBase.baseId, record.instanceId);
            var channel = service.State.ObsidianQueue.Channel(DeNelle.Core.Jobs.ChannelId.Builder);
            var busy = new BuildJobData { StructureId = key, StartMs = 0, DurationMs = 3600000 };
            channel.PendingQueue.Add(busy);
            Require(!OwnedTownConstructionService.TryQuoteSale(record.instanceId, out _, out _), "Sale ignored a queued upgrade.");
            channel.PendingQueue.Remove(busy);
            Require(OwnedTownConstructionService.TryQuoteSale(record.instanceId, out var quote, out reason), reason);
            var expected = DeNelle.Village.BuildModeController.RefundCostFor(record.placement.itemId, record.placement.level);
            Require(JsonConvert.SerializeObject(quote) == JsonConvert.SerializeObject(expected), "Sale did not use the existing invested-cost refund.");
            Require(service.TrySave(out reason), reason);
            string before = JsonConvert.SerializeObject(service.State.OwnedBase);
            string wallet = JsonConvert.SerializeObject(service.State.Resources);
            int wood = service.State.Wood, iron = service.State.Iron;
            string story = JsonConvert.SerializeObject(service.State.BaseLayout);
            string queue = JsonConvert.SerializeObject(service.State.ObsidianQueue);
            string durable = OwnedTownMovePlayProof.Provider.Read(SaveSchema.PlayerPrefsKey);
            int revision = service.State.OwnedBase.revision;
            int failures = OwnedTownMovePlayProof.Provider.FailedWriteCount;
            OwnedTownMovePlayProof.Provider.FailWrites = true;
            Require(!OwnedTownConstructionService.TrySell(record.instanceId, revision, out _, out _) &&
                OwnedTownMovePlayProof.Provider.FailedWriteCount > failures && body.gameObject.activeSelf &&
                JsonConvert.SerializeObject(service.State.OwnedBase) == before &&
                JsonConvert.SerializeObject(service.State.Resources) == wallet && service.State.Wood == wood && service.State.Iron == iron &&
                OwnedTownMovePlayProof.Provider.Read(SaveSchema.PlayerPrefsKey) == durable,
                "Failed sale changed the live body, property, refund or durable save.");
            OwnedTownMovePlayProof.Provider.FailWrites = false;
            Require(!OwnedTownConstructionService.TrySell(record.instanceId, revision - 1, out _, out _), "Stale sale was accepted.");
            var resources = service.State.Resources;
            var credited = new DeNelle.Core.Catalog.ResourceCost {
                wood = Math.Min(quote.wood, DeNelle.Core.Economy.TownBankCapacity.RoomFor(DeNelle.Core.Economy.BankResource.Wood, wood)),
                iron = Math.Min(quote.iron, DeNelle.Core.Economy.TownBankCapacity.RoomFor(DeNelle.Core.Economy.BankResource.Iron, iron)),
                stone = Math.Min(quote.stone, DeNelle.Core.Economy.TownBankCapacity.RoomFor(DeNelle.Core.Economy.BankResource.Stone, resources.Stone)),
                crystals = quote.crystals
            };
            var panel = UnityEngine.Object.FindAnyObjectByType<OwnedTownPanel>();
            var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            typeof(OwnedTownPanel).GetField("_selected", flags).SetValue(panel, record.instanceId);
            panel.Show();
            ClickTownButton(panel, "sell");
            var confirmation = (DeNelle.Core.UI.ElarionUiKit.ConfirmModal)typeof(OwnedTownPanel).GetField("_saleConfirm", flags).GetValue(panel);
            Require(confirmation != null && confirmation.message.text.Contains(quote.crystals.ToString()),
                "Sale confirmation omitted refund " + quote.crystals + ": " + confirmation?.message.text);
            confirmation.cancel.onClick.Invoke();
            Require(body.gameObject.activeSelf && JsonConvert.SerializeObject(service.State.OwnedBase) == before,
                "Cancelling confirmation sold the structure.");
            yield return null;
            ClickTownButton(panel, "sell");
            confirmation = (DeNelle.Core.UI.ElarionUiKit.ConfirmModal)typeof(OwnedTownPanel).GetField("_saleConfirm", flags).GetValue(panel);
            confirmation.confirm.onClick.Invoke();
            Require(!body.gameObject.activeSelf && service.State.OwnedBase.structures.Find(s => s.instanceId == record.instanceId).retired &&
                service.State.Wood == wood + credited.wood && service.State.Iron == iron + credited.iron &&
                service.State.Resources.Stone == resources.Stone + credited.stone && service.State.Resources.Crystals == resources.Crystals + credited.crystals,
                "Successful sale did not retire the body and credit the exact refund.");
            string sold = JsonConvert.SerializeObject(service.State.OwnedBase);
            string paid = JsonConvert.SerializeObject(service.State.Resources);
            Require(!OwnedTownConstructionService.TrySell(record.instanceId, service.State.OwnedBase.revision, out _, out _), "Repeated sale paid twice.");
            Require(service.Load() && JsonConvert.SerializeObject(service.State.OwnedBase) == sold &&
                JsonConvert.SerializeObject(service.State.Resources) == paid &&
                JsonConvert.SerializeObject(service.State.BaseLayout) == story && JsonConvert.SerializeObject(service.State.ObsidianQueue) == queue,
                "Sold identity/refund did not cold-load or changed story layout/queue.");
            for (int i = 0; i < 4; i++) yield return null;
            Require(OwnedTownNavigation.Validate(scene, out reason), reason);
            Debug.Log("OWNED_TOWN_SALE_OK actual sell/cancel/confirm buttons, fixed/queued/stale refusal, failed-save live rollback, exact shared refund, tombstone, duplicate refusal, cold load and navigation");
        }

        private static IEnumerator VerifyGridMove(GameStateService service, Transform body)
        {
            var placed = body.GetComponent<DeNelle.Village.PlacedStructure>();
            var originalPosition = body.position;
            var originalCell = placed.gridCell;
            var grid = body.parent.GetComponentInChildren<DeNelle.Village.PlacementGrid>(true);
            string before = JsonConvert.SerializeObject(service.State.OwnedBase);
            string story = JsonConvert.SerializeObject(service.State.BaseLayout);
            string wallet = JsonConvert.SerializeObject(service.State.Resources);
            string durable = OwnedTownMovePlayProof.Provider.Read(SaveSchema.PlayerPrefsKey);
            Vector3 chosen = default;
            bool found = false;
            foreach (var direction in new[] { Vector3.right, Vector3.left, Vector3.forward, Vector3.back })
            {
                bool done = false, accepted = false;
                int failures = OwnedTownMovePlayProof.Provider.FailedWriteCount;
                OwnedTownMovePlayProof.Provider.FailWrites = true;
                chosen = originalPosition + direction * 6f;
                if (!OwnedTownDesignService.TryBeginMove("play-new-tower", chosen,
                    (ok, why) => { done = true; accepted = ok; }, out _)) continue;
                float deadline = Time.realtimeSinceStartup + 6f;
                while (!done && Time.realtimeSinceStartup < deadline) yield return null;
                Require(done && !accepted && !OwnedTownDesignService.IsBusy, "Failed grid move did not settle safely.");
                Require(body.position == originalPosition && placed.gridCell == originalCell &&
                    JsonConvert.SerializeObject(service.State.OwnedBase) == before &&
                    OwnedTownMovePlayProof.Provider.Read(SaveSchema.PlayerPrefsKey) == durable,
                    "Failed grid move changed scene metadata or durable town.");
                if (OwnedTownMovePlayProof.Provider.FailedWriteCount > failures) { found = true; break; }
                for (int frame = 0; frame < 4; frame++) yield return null;
            }
            OwnedTownMovePlayProof.Provider.FailWrites = false;
            Require(found, "No navigable grid move reached the injected save failure.");
            for (int frame = 0; frame < 4; frame++) yield return null;
            bool finished = false, saved = false; string failure = null;
            Require(OwnedTownDesignService.TryBeginMove("play-new-tower", chosen,
                (ok, why) => { finished = true; saved = ok; failure = why; }, out var reason), reason);
            float retryDeadline = Time.realtimeSinceStartup + 6f;
            while (!finished && Time.realtimeSinceStartup < retryDeadline) yield return null;
            Require(finished && saved, "Grid move retry failed: " + failure);
            var record = service.State.OwnedBase.structures.Find(s => s.instanceId == "play-new-tower");
            Require(placed.gridCell == grid.WorldToCell(chosen) && placed.gridCell != originalCell &&
                record.placement.cellX == placed.gridCell.x && record.placement.cellZ == placed.gridCell.y &&
                body.position == grid.SnapToGrid(chosen), "Grid move did not align scene, metadata and saved placement.");
            string moved = JsonConvert.SerializeObject(service.State.OwnedBase);
            Require(service.Load() && JsonConvert.SerializeObject(service.State.OwnedBase) == moved &&
                JsonConvert.SerializeObject(service.State.BaseLayout) == story && JsonConvert.SerializeObject(service.State.Resources) == wallet,
                "Moved grid tower did not cold-load or changed the story town/wallet.");
            Require(!OwnedTownDesignService.TryBeginMove("play-new-tower", chosen, null, out _), "Same-cell move must not advance a revision.");
            Debug.Log("OWNED_TOWN_GRID_MOVE_OK live navigation, failed-save pose/metadata rollback, retry, cold load and story/wallet isolation");
        }

        private static void VerifyOwnedUpgradeTimer(GameStateService service)
        {
            Require(DeNelle.Core.FeatureFlags.BuildTimers, "Timed owned upgrade proof requires enabled build timers.");
            var timer = DeNelle.Village.BuildTimerService.Instance;
            Require(timer != null, "The live builder queue service is missing.");
            var property = service.State.OwnedBase;
            var record = property.structures.Find(s => s.instanceId == "play-new-tower");
            var price = DeNelle.Village.Buildings.Progression.PlacedStructureUpgradeService.CostForNext(
                DeNelle.Core.Catalog.CatalogRegistry.Get(record.placement.itemId), record.placement.level);
            service.State.Wood = price.wood + 31; service.State.Iron = price.iron + 37;
            service.State.Resources.Stone = price.stone + 41; service.State.Resources.Crystals = price.crystals + 43;
            Require(service.TrySave(out var reason), reason);
            string before = JsonConvert.SerializeObject(property);
            string queueBefore = JsonConvert.SerializeObject(service.State.ObsidianQueue);
            string durable = OwnedTownMovePlayProof.Provider.Read(SaveSchema.PlayerPrefsKey);
            OwnedTownMovePlayProof.Provider.FailWrites = true;
            string key = OwnedTownJobKey.Compose(property.baseId, record.instanceId);
            using (var vm = DeNelle.Village.Buildings.Progression.BuildingUpgradeVM.CreateDefault(key, null))
            {
                Require(vm.CurrentTier == record.placement.level && vm.PreviewId == record.placement.itemId && vm.MaxTier > vm.CurrentTier,
                    "Existing upgrade page did not resolve the owned structure's real level/catalog.");
                vm.UpgradeNext();
                Require(!timer.IsBuilding(key), "Failed save through the upgrade page started an owned upgrade.");
            }
            Require(before == JsonConvert.SerializeObject(service.State.OwnedBase) && queueBefore == JsonConvert.SerializeObject(service.State.ObsidianQueue) &&
                durable == OwnedTownMovePlayProof.Provider.Read(SaveSchema.PlayerPrefsKey) && service.State.Wood == price.wood + 31 &&
                service.State.Iron == price.iron + 37 && service.State.Resources.Stone == price.stone + 41 && service.State.Resources.Crystals == price.crystals + 43,
                "Failed upgrade scheduling changed property, queue, wallet or durable state.");
            OwnedTownMovePlayProof.Provider.FailWrites = false;
            using (var vm = DeNelle.Village.Buildings.Progression.BuildingUpgradeVM.CreateDefault(key, null)) vm.UpgradeNext();
            Require(timer.IsBuilding(key), "Existing upgrade page did not start the owned upgrade.");
            BuildJobData? scheduled = null;
            Require(OwnedTownJobKey.TryParse(key, out var baseId, out var instanceId) && baseId == property.baseId && instanceId == record.instanceId &&
                !DeNelle.Village.Buildings.Progression.PlacedUpgradeKey.IsPlacedKey(key), "Owned job collided with the story key grammar.");
            Require(service.State.OwnedBase.structures.Find(s => s.instanceId == instanceId).placement.level == 1 &&
                service.State.Wood == 31 && service.State.Iron == 37 && service.State.Resources.Stone == 41 && service.State.Resources.Crystals == 43,
                "Scheduling must charge once and leave the earned level pending.");
            Require(!timer.TryStartOwnedTownUpgrade(instanceId, out _, out _), "Duplicate upgrade was queued/charged.");
            foreach (bool pending in new[] { false, true })
            {
                var cancelChannel = service.State.ObsidianQueue.Channel(DeNelle.Core.Jobs.ChannelId.Builder);
                if (pending)
                {
                    int active = cancelChannel.ActiveJobs.FindIndex(j => j.StructureId == key);
                    Require(active >= 0, "Cancellation fixture lost its job.");
                    var queued = cancelChannel.ActiveJobs[active]; queued.StartMs = 0;
                    cancelChannel.ActiveJobs.RemoveAt(active); cancelChannel.PendingQueue.Add(queued);
                    Require(service.TrySave(out reason), reason);
                }
                string cancelBefore = JsonConvert.SerializeObject(service.State.ObsidianQueue);
                string cancelDurable = OwnedTownMovePlayProof.Provider.Read(SaveSchema.PlayerPrefsKey);
                OwnedTownMovePlayProof.Provider.FailWrites = true;
                Require(!timer.TryCancelOwnedTownUpgrade(DeNelle.Core.Jobs.ChannelId.Builder, key, out _, out _) &&
                    cancelBefore == JsonConvert.SerializeObject(service.State.ObsidianQueue) &&
                    cancelDurable == OwnedTownMovePlayProof.Provider.Read(SaveSchema.PlayerPrefsKey) &&
                    service.State.Wood == 31 && service.State.Iron == 37 && service.State.Resources.Stone == 41 && service.State.Resources.Crystals == 43,
                    "Failed active/pending cancellation changed the queue or credited a refund.");
                OwnedTownMovePlayProof.Provider.FailWrites = false;
                Require(timer.CancelChannelJobWithRefund(DeNelle.Core.Jobs.ChannelId.Builder, key, out var refund), "Owned cancellation doorway failed.");
                Require(refund.Wood == price.wood && refund.Iron == price.iron && refund.Stone == price.stone && refund.Crystals == price.crystals &&
                    service.State.Wood == price.wood + 31 && service.State.Iron == price.iron + 37 &&
                    service.State.Resources.Stone == price.stone + 41 && service.State.Resources.Crystals == price.crystals + 43,
                    "Cancellation did not return the exact paid basket.");
                Require(!timer.TryCancelOwnedTownUpgrade(DeNelle.Core.Jobs.ChannelId.Builder, key, out _, out _), "Repeated cancellation refunded twice.");
                Require(service.Load() && !timer.IsBuilding(key) && service.State.Resources.Crystals == price.crystals + 43,
                    "Cancellation/refund did not survive reload.");
                Require(timer.TryStartOwnedTownUpgrade(instanceId, out scheduled, out reason) && scheduled.HasValue, reason);
            }
            var ch = service.State.ObsidianQueue.Channel(DeNelle.Core.Jobs.ChannelId.Builder);
            int index = ch.ActiveJobs.FindIndex(j => j.StructureId == key);
            Require(index >= 0, "Fixture upgrade unexpectedly queued behind other work.");
            var due = ch.ActiveJobs[index]; due.StartMs = 1; due.DurationMs = 1; ch.ActiveJobs[index] = due;
            Require(service.TrySave(out reason), reason);
            var sweep = typeof(DeNelle.Village.BuildTimerService).GetMethod("SweepAllChannels", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            OwnedTownMovePlayProof.Provider.FailWrites = true;
            sweep.Invoke(timer, null);
            Require(ch.ActiveJobs.Exists(j => j.StructureId == key) && service.State.OwnedBase.structures.Find(s => s.instanceId == instanceId).placement.level == 1,
                "A failed completion save removed the retryable job or awarded the level.");
            OwnedTownMovePlayProof.Provider.FailWrites = false;
            Require(OwnedTownUpgradeCompletion.TryCommit(due, out reason), reason);
            int completedRevision = service.State.OwnedBase.revision;
            Require(!timer.TryCancelOwnedTownUpgrade(DeNelle.Core.Jobs.ChannelId.Builder, key, out _, out _),
                "An already-saved upgrade level must not also receive a cancellation refund.");
            Require(ch.ActiveJobs.Exists(j => j.StructureId == key), "Completion proof must retain the job across the cleanup crash window.");
            Require(service.Load(), "Upgrade completion did not cold reload.");
            Require(OwnedTownUpgradeCompletion.TryCommit(due, out reason) && service.State.OwnedBase.revision == completedRevision,
                "Reload/replay repeated the earned upgrade revision: " + reason);
            sweep.Invoke(timer, null);
            Require(!timer.IsBuilding(key) && service.State.OwnedBase.structures.Find(s => s.instanceId == instanceId).placement.level == 2,
                "Recovered completion did not remove its job and retain the earned level.");
            service.State.Wood = service.State.Iron = service.State.Resources.Stone = service.State.Resources.Crystals = 100000;
            Require(timer.TryStartOwnedTownUpgrade(instanceId, out var paidJob, out reason) && paidJob.HasValue, reason);
            int finishPrice = timer.InstantFinishPrice(DeNelle.Core.Jobs.ChannelId.Builder, key);
            Require(finishPrice > 0, "Paid-finish proof requires an actual priced timer.");
            int crystalsBeforeFinish = service.State.Resources.Crystals;
            string finishBefore = JsonConvert.SerializeObject(service.State.OwnedBase);
            string finishQueue = JsonConvert.SerializeObject(service.State.ObsidianQueue);
            string finishDurable = OwnedTownMovePlayProof.Provider.Read(SaveSchema.PlayerPrefsKey);
            OwnedTownMovePlayProof.Provider.FailWrites = true;
            Require(!timer.TryInstantFinish(DeNelle.Core.Jobs.ChannelId.Builder, key, out _) &&
                service.State.Resources.Crystals == crystalsBeforeFinish && finishBefore == JsonConvert.SerializeObject(service.State.OwnedBase) &&
                finishQueue == JsonConvert.SerializeObject(service.State.ObsidianQueue) && finishDurable == OwnedTownMovePlayProof.Provider.Read(SaveSchema.PlayerPrefsKey),
                "Failed paid finish spent crystals, removed its timer or changed the saved level.");
            OwnedTownMovePlayProof.Provider.FailWrites = false;
            Require(timer.TryInstantFinish(DeNelle.Core.Jobs.ChannelId.Builder, key, out reason), reason);
            Require(service.State.Resources.Crystals == crystalsBeforeFinish - finishPrice && !timer.IsBuilding(key) &&
                service.State.OwnedBase.structures.Find(s => s.instanceId == instanceId).placement.level == 3,
                "Paid finish must save one exact charge, level and job removal together.");
            Require(!timer.TryInstantFinish(DeNelle.Core.Jobs.ChannelId.Builder, key, out _), "Paid finish charged twice.");
            Require(service.Load() && service.State.Resources.Crystals == crystalsBeforeFinish - finishPrice && !timer.IsBuilding(key) &&
                service.State.OwnedBase.structures.Find(s => s.instanceId == instanceId).placement.level == 3, "Paid finish did not survive reload.");
            Debug.Log("OWNED_TOWN_PAID_FINISH_OK actual quoted crystals, save-failure rollback/retry, single charge with level/job removal and cold reload");
            VerifyOwnedInstantBuildToken(service, timer);
            Debug.Log("OWNED_TOWN_UPGRADE_TIMER_OK isolated job key, atomic schedule/payment and active/pending cancellation rollback/refund/reload, duplicate refusal, failed completion retry and saved-level-before-cleanup crash recovery");
        }

        private static void VerifyOwnedInstantBuildToken(GameStateService service, DeNelle.Village.BuildTimerService timer)
        {
            var record = service.State.OwnedBase.structures.Find(s => !s.retired && s.condition01 == 1f && s.placement.level == 2);
            Require(record != null, "Token proof needs a repaired level-two tower.");
            string key = OwnedTownJobKey.Compose(service.State.OwnedBase.baseId, record.instanceId);
            string tokenKey = "convenience:instant_build"; // legacy spelling must be returned exactly.
            service.State.GearInventory[tokenKey] = 1;
            var channel = service.State.ObsidianQueue.Channel(DeNelle.Core.Jobs.ChannelId.Builder);
            Require(channel.ActiveJobs.Count == 0 && channel.PendingQueue.Count == 0, "Token proof requires a clear builder queue.");
            for (int i = 0; i < timer.SlotCount(DeNelle.Core.Jobs.ChannelId.Builder); i++)
                channel.ActiveJobs.Add(new BuildJobData { StructureId = "token-proof-busy-" + i, StartMs = DeNelle.Village.TimeSource.NowUnixMs(), DurationMs = 3600000 });
            Require(service.TrySave(out var reason), reason);
            string original = JsonConvert.SerializeObject(service.State.OwnedBase);
            string queue = JsonConvert.SerializeObject(service.State.ObsidianQueue);
            string wallet = JsonConvert.SerializeObject(service.State.Resources);
            int wood = service.State.Wood, iron = service.State.Iron;
            string durable = OwnedTownMovePlayProof.Provider.Read(SaveSchema.PlayerPrefsKey);
            OwnedTownMovePlayProof.Provider.FailWrites = true;
            Require(!timer.TryStartOwnedTownUpgrade(record.instanceId, out _, out _) && service.State.GearInventory[tokenKey] == 1 &&
                JsonConvert.SerializeObject(service.State.OwnedBase) == original && JsonConvert.SerializeObject(service.State.ObsidianQueue) == queue &&
                OwnedTownMovePlayProof.Provider.Read(SaveSchema.PlayerPrefsKey) == durable, "Failed enqueue consumed the instant-build token.");
            OwnedTownMovePlayProof.Provider.FailWrites = false;
            Require(timer.TryStartOwnedTownUpgrade(record.instanceId, out var job, out reason) && job.HasValue &&
                job.Value.StartMs == 0 && job.Value.DurationMs == 0 && job.Value.InstantBuildTokenKey == tokenKey && service.State.GearInventory[tokenKey] == 0,
                "Queued instant-build did not persist one exact token receipt: " + reason);
            Require(service.Load() && service.State.GearInventory[tokenKey] == 0 && timer.IsBuilding(key), "Queued token charge did not reload.");
            OwnedTownMovePlayProof.Provider.FailWrites = true;
            Require(!timer.TryCancelOwnedTownUpgrade(DeNelle.Core.Jobs.ChannelId.Builder, key, out _, out _) &&
                service.State.GearInventory[tokenKey] == 0 && timer.IsBuilding(key), "Failed cancellation refunded the token or lost the job.");
            OwnedTownMovePlayProof.Provider.FailWrites = false;
            Require(timer.TryCancelOwnedTownUpgrade(DeNelle.Core.Jobs.ChannelId.Builder, key, out _, out reason), reason);
            Require(service.State.GearInventory[tokenKey] == 1 && !timer.IsBuilding(key) && service.State.Wood == wood && service.State.Iron == iron &&
                JsonConvert.SerializeObject(service.State.Resources) == wallet, "Queued cancellation did not refund the exact token and resource basket.");
            Require(!timer.TryCancelOwnedTownUpgrade(DeNelle.Core.Jobs.ChannelId.Builder, key, out _, out _) &&
                service.Load() && service.State.GearInventory[tokenKey] == 1, "Token refund duplicated or failed to cold-load.");
            service.State.ObsidianQueue.Channel(DeNelle.Core.Jobs.ChannelId.Builder).ActiveJobs.Clear();
            Require(service.TrySave(out reason), reason);
            Require(timer.TryStartOwnedTownUpgrade(record.instanceId, out _, out reason) && !timer.IsBuilding(key) &&
                service.State.GearInventory[tokenKey] == 0 && service.State.OwnedBase.structures.Find(s => s.instanceId == record.instanceId).placement.level == 3,
                "Available crew did not complete the token-skipped upgrade: " + reason);
            Require(service.Load() && service.State.GearInventory[tokenKey] == 0 && !timer.IsBuilding(key), "Completed instant-build token resurrected on reload.");
            Debug.Log("OWNED_TOWN_INSTANT_BUILD_TOKEN_OK queued consumption/refund atomicity, legacy key, failed saves, retry, cold reload and immediate completion");
        }

        private static IEnumerator VerifyRepairButtons(GameStateService service, DeNelle.Village.HeroLocomotion hero)
        {
            var panel = UnityEngine.Object.FindAnyObjectByType<OwnedTownPanel>();
            Require(panel != null, "Town repair panel is missing.");
            ClickTownButton(panel, "repairMore");
            yield return null;
            var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            string selected = (string)typeof(OwnedTownPanel).GetField("_repairSelected", flags).GetValue(panel);
            var property = service.State.OwnedBase;
            var record = property.structures.Find(s => s.instanceId == selected);
            Require(OwnedTownRepairService.TryQuote(record, out var quote, out var reason), reason);
            Require(quote.wood + quote.iron + quote.stone > 0, "Repair proof requires a nonzero actual catalog price.");
            var highlight = (DeNelle.Village.RepairHighlight)typeof(OwnedTownPanel).GetField("_highlight", flags).GetValue(panel);
            Require(highlight != null && highlight.gameObject.activeInHierarchy &&
                Array.Exists(highlight.GetComponentsInChildren<Renderer>(), r => r.enabled && r.sharedMaterial != null),
                "Selected repair has no enabled marker renderer/material.");
            var debit = OwnedBaseProgression.RepairWalletDebit(property.repairSupplies, quote);
            // Only the isolated in-memory fixture is funded; the main player envelope is checked below.
            service.State.Wood = debit.wood + 7;
            service.State.Iron = debit.iron + 11;
            service.State.Resources.Stone = debit.stone + 13;
            Require(service.TrySave(out reason), reason);
            string before = JsonConvert.SerializeObject(property);
            string durableBefore = OwnedTownMovePlayProof.Provider.Read(SaveSchema.PlayerPrefsKey);
            int sceneHandle = SceneManager.GetActiveScene().handle;
            OwnedTownMovePlayProof.Provider.FailWrites = true;
            ClickTownButton(panel, "repair");
            yield return null;
            Require(SceneManager.GetActiveScene().handle == sceneHandle && JsonConvert.SerializeObject(service.State.OwnedBase) == before &&
                service.State.Wood == debit.wood + 7 && service.State.Iron == debit.iron + 11 && service.State.Resources.Stone == debit.stone + 13 &&
                OwnedTownMovePlayProof.Provider.Read(SaveSchema.PlayerPrefsKey) == durableBefore,
                "Failed UI repair changed the live scene, property, wallet or durable save.");
            OwnedTownMovePlayProof.Provider.FailWrites = false;
            ClickTownButton(panel, "repair");
            float deadline = Time.realtimeSinceStartup + 45f;
            while (SceneManager.GetActiveScene().handle == sceneHandle && Time.realtimeSinceStartup < deadline) yield return null;
            Require(SceneManager.GetActiveScene().handle != sceneHandle, "Successful repair did not reload the owned town.");
            for (int frame = 0; frame < 120; frame++) yield return null;
            var town = UnityEngine.Object.FindAnyObjectByType<OwnedTownController>();
            Require(town != null && town.Reconstructed && hero != null && hero.transform.root.gameObject.scene == town.gameObject.scene,
                "Repair reload lost reconstruction or the carried hero.");
            Require(service.State.Wood == 7 && service.State.Iron == 11 && service.State.Resources.Stone == 13,
                "Repair button did not charge the exact quoted material debit.");
            var repaired = service.State.OwnedBase.structures.Find(s => s.instanceId == selected);
            Require(repaired.condition01 == 1f, "Repaired condition did not survive reentry.");
            foreach (var other in property.structures)
                if (other.instanceId != selected)
                    Require(JsonConvert.SerializeObject(other) == JsonConvert.SerializeObject(service.State.OwnedBase.structures.Find(s => s.instanceId == other.instanceId)),
                        "Repair changed an unrelated structure.");
            Require(OwnedTownScenePose.TryResolve(town.gameObject.scene, repaired.inheritedPose, out var target, out reason), reason);
            var wall = target.GetComponent<DeNelle.Village.WallSegment>();
            var tower = target.GetComponent<DeNelle.Village.DefenseTower>();
            float hp = wall != null ? wall.HpFraction : tower != null ? tower.HpFraction : target.GetComponent<RaidSpire>().HpFraction;
            Require(Mathf.Abs(hp - 1f) < .0001f, "Repair save did not restore the live structure health.");
            Require(OwnedTownNavigation.Validate(town.gameObject.scene, out reason), reason);
            Debug.Log("OWNED_TOWN_REPAIR_BUTTONS_OK actual selection marker, UI save-failure rollback/retry, exact wallet debit, live condition after reload, unrelated structures preserved, hero carry and navigation");
        }

        private static void ClickTownButton(OwnedTownPanel panel, string key)
        {
            var canvas = (GameObject)typeof(OwnedTownPanel).GetField("_ui",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).GetValue(panel);
            Require(canvas != null, "Town action canvas missing: " + key);
            string caption = DeNelle.Core.UI.LocalText.Get("ownedTown." + key);
            var button = Array.Find(canvas.GetComponentsInChildren<UnityEngine.UI.Button>(true), b =>
                Array.Exists(b.GetComponentsInChildren<TMPro.TMP_Text>(true), t => string.Equals(t.text, caption, StringComparison.OrdinalIgnoreCase)));
            Require(button != null && button.interactable, "Town action missing or disabled: " + key);
            button.onClick.Invoke();
        }

        private static IEnumerator VerifyPracticeMatch(GameStateService service)
        {
            // Wait for the preceding actor-only scene to finish unloading.
            yield return null;
            var instant = service.State.OwnedBase.structures.Find(s => s.instanceId == "play-instant-construction");
            Require(OwnedBaseConstruction.TryRetire(service.State.OwnedBase, service.State.OwnedBase.revision,
                instant.instanceId, out var retiredFixture, out var preparationReason) &&
                service.TryCommitOwnedBaseConstruction(service.State.OwnedBase.revision, retiredFixture, default, default, out _, out preparationReason), preparationReason);
            var timer = DeNelle.Village.BuildTimerService.Instance;
            string pendingId = "practice-pending-construction";
            string pendingKey = OwnedTownJobKey.Compose(service.State.OwnedBase.baseId, pendingId);
            Require(StartOwnedBuild(timer, pendingId, instant.placement, service.State.OwnedBase.revision,
                DeNelle.Village.BuildModeController.CostFor(DeNelle.Core.Catalog.CatalogRegistry.Get(instant.placement.itemId)), out preparationReason), preparationReason);
            var before = JsonConvert.SerializeObject(service.State.OwnedBase.structures);
            Require(DeNelle.Village.Arena.OwnedTownPracticeController.TryEnter(out var reason), reason);
            float deadline = Time.realtimeSinceStartup + 60f;
            DeNelle.Village.Arena.OwnedTownPracticeController match = null;
            while (Time.realtimeSinceStartup < deadline)
            {
                match = UnityEngine.Object.FindAnyObjectByType<DeNelle.Village.Arena.OwnedTownPracticeController>();
                if (match != null && match.Running) break;
                yield return null;
            }
            Require(match != null && match.Running && match.SpawnedCount == 3,
                "Practice failed to spawn a live three-attacker match: " + (match != null ? match.Failure : "controller missing"));
            Require(SceneManager.GetActiveScene().name == DeNelle.Core.Combat.PracticeCombatPolicy.SceneName, "Practice did not isolate its scene.");
            var pending = service.State.OwnedBase.structures.Find(s => s.instanceId == pendingId);
            Require(pending != null && pending.constructionPending, "Pending practice structure is missing or already complete.");
            Require(OwnedTownScenePose.TryResolveStructure(SceneManager.GetActiveScene(), pending,
                out var frozenBody, out var frozenReason), "Pending practice structure did not import.");
            Require(DeNelle.Village.UnderConstructionVisual.IsUnderConstruction(frozenBody.gameObject) &&
                !frozenBody.GetComponent<DeNelle.Village.DefenseTower>().enabled, "Unfinished practice tower can fight.");
            Require(timer.IsBuilding(pendingKey), "Practice construction job disappeared before completion.");
            timer.CompleteJob(pendingKey);
            for (int frame = 0; frame < 3; frame++) yield return null;
            Require(!service.State.OwnedBase.structures.Find(s => s.instanceId == pendingId).constructionPending,
                "Owner construction remains pending after completion; queue=" + JsonConvert.SerializeObject(service.State.ObsidianQueue));
            Require(!timer.IsBuilding(pendingKey), "Completed owner construction remains queued.");
            Require(DeNelle.Village.UnderConstructionVisual.IsUnderConstruction(frozenBody.gameObject), "Owner completion removed frozen practice scaffold.");
            Require(!frozenBody.GetComponent<DeNelle.Village.DefenseTower>().enabled, "Owner completion enabled frozen practice tower combat.");
            before = JsonConvert.SerializeObject(service.State.OwnedBase.structures);
            Require(!DeNelle.Village.Items.ConsumableUseService.TryUse("minor-heal-potion", true), "Practice permitted inventory consumption.");
            OwnedTownMovePlayProof.Provider.FailWrites = true;
            var enemies = UnityEngine.Object.FindObjectsByType<DeNelle.Village.Enemy>(FindObjectsSortMode.None);
            var positions = new Dictionary<DeNelle.Village.Enemy, Vector3>();
            foreach (var enemy in enemies)
                if (enemy.gameObject.scene == match.gameObject.scene && enemy.IsAlive) positions[enemy] = enemy.transform.position;
            Require(positions.Count == 3, "Practice did not create its complete live roster.");
            float startingHp = DeNelle.Village.HeroHealth.Instance.Hp;
            for (int frame = 0; frame < 60; frame++) yield return null;
            bool advanced = DeNelle.Village.HeroHealth.Instance.Hp < startingHp;
            foreach (var item in positions)
                advanced |= item.Key == null || !item.Key.IsAlive || Vector3.Distance(item.Key.transform.position, item.Value) > .2f;
            Require(advanced, "Practice AI neither advanced nor exchanged damage during live frames.");
            int killed = 0;
            foreach (var enemy in enemies)
                if (enemy.gameObject.scene == match.gameObject.scene && enemy.IsAlive) { enemy.TakeDamage(100000f); killed++; }
            yield return null;
            Require(match.Outcome == OwnedTownPracticeOutcome.Win &&
                (service.State.OwnedBase.milestoneFlags & OwnedBaseMilestones.PracticeCompleted) == 0,
                "Practice victory failed to retain a retryable result after save refusal.");
            Require(before == JsonConvert.SerializeObject(service.State.OwnedBase.structures), "Practice mutated the saved town layout or damage.");
            OwnedTownMovePlayProof.Provider.FailWrites = false;
            match.ReturnToTown();
            deadline = Time.realtimeSinceStartup + 40f;
            while (SceneManager.GetActiveScene().name != OwnedTownScenePose.SceneName && Time.realtimeSinceStartup < deadline) yield return null;
            Require(SceneManager.GetActiveScene().name == OwnedTownScenePose.SceneName &&
                (service.State.OwnedBase.milestoneFlags & OwnedBaseMilestones.PracticeCompleted) != 0,
                "Practice result retry did not save and return to town.");
            Require(before == JsonConvert.SerializeObject(service.State.OwnedBase.structures), "Returning from practice changed saved structures.");
            Require(service.Load() && !service.State.OwnedBase.structures.Find(s => s.instanceId == pendingId).constructionPending &&
                (service.State.OwnedBase.milestoneFlags & OwnedBaseMilestones.PracticeCompleted) != 0,
                "Practice result did not preserve completed construction and its own milestone on cold load.");
            Debug.Log("OWNED_TOWN_FROZEN_CONSTRUCTION_PRACTICE_OK owner timer completes independently; frozen opponent stays unfinished; result save retries and return preserve both updates");
            for (int frame = 0; frame < 15; frame++) yield return null;
            foreach (bool abandon in new[] { false, true })
            {
                Require(DeNelle.Village.Arena.OwnedTownPracticeController.TryEnter(out reason), reason);
                deadline = Time.realtimeSinceStartup + 40f;
                match = null;
                while (Time.realtimeSinceStartup < deadline)
                {
                    match = UnityEngine.Object.FindAnyObjectByType<DeNelle.Village.Arena.OwnedTownPracticeController>();
                    if (match != null && match.Running) break;
                    yield return null;
                }
                Require(match != null && match.Running, "Repeat practice did not start.");
                if (!abandon)
                {
                    int deathEvents = 0;
                    Action died = () => deathEvents++;
                    var health = DeNelle.Village.HeroHealth.Instance;
                    health.OnDied += died;
                    health.TakeDamage(100000f);
                    yield return null;
                    health.OnDied -= died;
                    Require(match.Outcome == OwnedTownPracticeOutcome.Loss && deathEvents == 0 && health.IsAlive,
                        "Practice defeat invoked normal death or failed to resolve loss.");
                }
                match.ReturnToTown();
                Require(match.Outcome == (abandon ? OwnedTownPracticeOutcome.Abandoned : OwnedTownPracticeOutcome.Loss),
                    "Practice leave rewrote its result.");
                deadline = Time.realtimeSinceStartup + 40f;
                while (SceneManager.GetActiveScene().name != OwnedTownScenePose.SceneName && Time.realtimeSinceStartup < deadline) yield return null;
                for (int frame = 0; frame < 15; frame++) yield return null;
                Require(SceneManager.GetActiveScene().name == OwnedTownScenePose.SceneName &&
                    !DeNelle.Village.HeroHealth.Instance.PracticeDefeated && before == JsonConvert.SerializeObject(service.State.OwnedBase.structures),
                    "Practice return leaked defeat state or changed saved property.");
            }
            Debug.Log("OWNED_TOWN_PRACTICE_PLAY_OK real scene import and AI movement/combat; win save refusal and retry; defeat without normal death; abandon and return; saved town unchanged");
        }

        private static void VerifyPracticeKill(GameStateService service, Vector3 position)
        {
            var practice = SceneManager.CreateScene(DeNelle.Core.Combat.PracticeCombatPolicy.SceneName);
            var host = new GameObject("PracticeKillProof");
            SceneManager.MoveGameObjectToScene(host, practice);
            host.transform.position = position;
            var enemy = host.AddComponent<DeNelle.Village.Enemy>();
            enemy.Configure("practice-kill-proof", new DeNelle.Village.EnemyDef {
                Id = "hollow-walker", Hp = 50f, XpReward = 1000, CoinReward = 1000
            }, null);
            var watcherHost = new GameObject("PracticeDropWatcherProof");
            var watcher = watcherHost.AddComponent<DeNelle.Village.Items.ItemDropWatcher>();
            var handler = typeof(DeNelle.Village.Items.ItemDropWatcher).GetMethod("OnEnemyDied",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            int deaths = 0;
            enemy.Died += victim => { deaths++; handler.Invoke(watcher, new object[] { victim }); };
            string before = JsonConvert.SerializeObject(service.Snapshot());
            Require(DeNelle.Village.HeroProgression.Instance != null, "Practice reward test requires the real hero XP receiver.");
            float xp = DeNelle.Village.HeroProgression.Instance.LifetimeXp;
            // Active scene remains the town: suppression must follow the actor, not active scene.
            enemy.TakeDamage(100000f);
            Require(deaths == 1, "Practice kill failed to emit its combat completion event.");
            Require(DeNelle.Village.HeroProgression.Instance.LifetimeXp == xp &&
                JsonConvert.SerializeObject(service.Snapshot()) == before, "Practice kill changed XP or persisted progression.");
            Destroy(watcherHost);
            SceneManager.UnloadSceneAsync(practice);
            Debug.Log("PRACTICE_KILL_ISOLATION_OK actual lethal damage emits death; hero XP and save snapshot unchanged; drop callback suppressed; actor scene governs policy");
        }

        private static void Marker(string name, Vector3 position) { new GameObject(name).transform.position = position; }
        private static void Require(bool value, string reason) { if (!value) throw new InvalidOperationException(reason); }
    }
}

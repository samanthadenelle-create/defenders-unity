using System.Linq;
using DeNelle.Core.Catalog;
using DeNelle.Core.Jobs;
using DeNelle.Core.State;
using UnityEngine;
using UnityEngine.SceneManagement;
using CoreCost = DeNelle.Core.Catalog.ResourceCost;

namespace DeNelle.Village.World.Camps
{
    /// <summary>Owned construction transactions; live bodies change only after durability.</summary>
    public static class OwnedTownConstructionService
    {
        private static OwnedTownBuildOperation _active;
        public static bool IsBusy => _active != null;
        internal static void Release(OwnedTownBuildOperation operation) { if (_active == operation) _active = null; }
        public static void CancelPreview() { if (_active != null) _active.Cancel(); }

        public static PlacementGrid FindGrid()
        {
            var scene = SceneManager.GetActiveScene();
            if (scene.name != OwnedTownScenePose.SceneName) return null;
            foreach (var root in scene.GetRootGameObjects())
                if (root.name == "OwnedTownConstruction") return root.GetComponentInChildren<PlacementGrid>(true);
            return null;
        }

        public static bool TryBeginBuild(PlacedStructureData placement, System.Action<bool, string> completed, out string reason)
        {
            if (IsBusy || OwnedTownDesignService.IsBusy)
            { reason = "Wait for the current town change to finish."; return false; }
            var scene = SceneManager.GetActiveScene();
            var service = GameStateService.Instance;
            var property = service?.State?.OwnedBase;
            var grid = FindGrid();
            var timer = BuildTimerService.Instance;
            if (grid == null || property == null || timer == null)
            { reason = "Enter your town with its builder service before placing a structure."; return false; }
            string id = System.Guid.NewGuid().ToString("N");
            if (!OwnedBaseConstruction.TryBeginBuild(property, property.revision, id, placement, out var proposed, out reason) ||
                !OwnedTownLayoutSnapshot.TryCreate(proposed, service.State.HeroClass, out _, out reason)) return false;
            var entry = CatalogRegistry.Get(placement.itemId);
            bool free = BuildModeController.FreeBuildAvailable(entry);
            // Quote before adding the preview body to the live tower census.
            BuildModeController.InvalidateTowerCount();
            var price = BuildModeController.EffectiveCostFor(entry);
            var grace = BuildModeController.GraceReasonFor(!service.State.HasEverBuilt(placement.itemId), !service.State.Onboarded,
                DeNelle.Core.Economy.TownBankCapacity.IsStorageContainer(entry.repo));
            var footprint = grid.FootprintCells(StructureFactory.MeasureClaimFootprintXZ(entry), placement.yawSteps * 90f + placement.yawOffset);
            if (!grid.CanPlace(new Vector2Int(placement.cellX, placement.cellZ), footprint))
            { reason = "That construction grid position is occupied."; return false; }
            var position = grid.CellToWorld(new Vector2Int(placement.cellX, placement.cellZ));
            if (!UnityEngine.AI.NavMesh.SamplePosition(position, out var seat, 1.5f, UnityEngine.AI.NavMesh.AllAreas) ||
                Mathf.Abs(seat.position.y - position.y) > .25f || !OwnedTownNavigation.Validate(scene, out reason))
            { reason = "Choose level, walkable ground inside your town."; return false; }
            var target = BaseLayoutLoader.SpawnForLayout(placement, grid, grid.transform.parent);
            if (target == null) { reason = "The structure could not be created; nothing was charged."; return false; }
            target.gameObject.AddComponent<OwnedTownPlacedIdentity>().InstanceId = id;
            var tower = target.GetComponent<DefenseTower>();
            if (tower != null) tower.enabled = false; // preview must not fight before payment
            var host = new GameObject("OwnedTownBuildValidation");
            SceneManager.MoveGameObjectToScene(host, scene);
            _active = host.AddComponent<OwnedTownBuildOperation>();
            _active.Begin(target, grid, service, timer, id, placement, price, free, grace, completed);
            reason = null; return true;
        }

        public static bool TryQuoteSale(string instanceId, out CoreCost refund, out string reason)
        {
            refund = default;
            var service = GameStateService.Instance;
            var property = service?.State?.OwnedBase;
            if (!OwnedBaseProgression.Validate(property, out reason)) return false;
            var record = property.structures.Find(s => s.instanceId == instanceId);
            if (record == null || record.retired || record.constructionPending)
            { reason = "This structure is missing or already sold."; return false; }
            var entry = CatalogRegistry.Get(record.placement.itemId);
            if (entry?.repo == null)
            { reason = "The structure catalog is unavailable."; return false; }
            // The snapshot validator is the shared authority for fixed perimeter/objective protection.
            if (!OwnedBaseConstruction.TryRetire(property, property.revision, instanceId, out var next, out reason) ||
                !OwnedTownLayoutSnapshot.TryCreate(next, service.State.HeroClass, out _, out reason)) return false;
            string key = OwnedTownJobKey.Compose(property.baseId, instanceId);
            var channel = service.State.ObsidianQueue?.Channel(ChannelId.Builder);
            if (channel != null && (channel.ActiveJobs.Any(j => j.StructureId == key) ||
                channel.PendingQueue.Any(j => j.StructureId == key)))
            { reason = "Finish or cancel this structure's upgrade before selling it."; return false; }
            refund = BuildModeController.RefundCostFor(record.placement.itemId, record.placement.level);
            reason = null;
            return true;
        }

        public static bool TrySell(string instanceId, int expectedRevision, out CoreCost credited, out string reason)
        {
            credited = default;
            if (IsBusy || OwnedTownDesignService.IsBusy)
            { reason = "Wait for the current tower move to finish."; return false; }
            var scene = SceneManager.GetActiveScene();
            var service = GameStateService.Instance;
            var property = service?.State?.OwnedBase;
            if (scene.name != OwnedTownScenePose.SceneName || property == null)
            { reason = "Enter your personal town to sell a structure."; return false; }
            if (property.revision != expectedRevision)
            { reason = "The town changed; review the sale again."; return false; }
            if (!TryQuoteSale(instanceId, out var refund, out reason)) return false;
            var record = property.structures.Find(s => s.instanceId == instanceId);
            if (!OwnedTownScenePose.TryResolveStructure(scene, record, out var target, out reason)) return false;
            var placed = target.GetComponent<PlacedStructure>();
            var grid = record.inheritedPose == null && target.parent != null
                ? target.parent.GetComponentInChildren<PlacementGrid>(true) : null;
            if (record.inheritedPose == null && (placed == null || grid == null))
            { reason = "The construction grid is unavailable; reenter your town."; return false; }
            if (!OwnedBaseConstruction.TryRetire(property, expectedRevision, instanceId, out var next, out reason) ||
                !service.TryCommitOwnedBaseConstruction(expectedRevision, next, default, refund, out credited, out reason)) return false;
            // Keep captured identities available for replay; never destroy template objects.
            if (grid != null) grid.Free(placed.gridCell, placed.footprint);
            target.gameObject.SetActive(false);
            Physics.SyncTransforms();
            return true;
        }

        public static void RefreshRetiredBody(string instanceId)
        {
            var scene = SceneManager.GetActiveScene();
            var record = GameStateService.Instance?.State?.OwnedBase?.structures.Find(s => s.instanceId == instanceId);
            if (scene.name != OwnedTownScenePose.SceneName || record == null || !record.retired ||
                !OwnedTownScenePose.TryResolveStructure(scene, record, out var target, out _)) return;
            var placed = target.GetComponent<PlacedStructure>();
            if (record.inheritedPose == null && placed != null && target.parent != null)
                target.parent.GetComponentInChildren<PlacementGrid>(true)?.Free(placed.gridCell, placed.footprint);
            target.gameObject.SetActive(false);
            Physics.SyncTransforms();
        }
    }
}

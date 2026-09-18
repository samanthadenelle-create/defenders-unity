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
            // WO-1872 — SELL and CLEAR are disjoint verbs on disjoint records. A ruin is cleared for
            // salvage (TryQuoteClear), a standing structure is sold for a refund. The split is
            // enforced HERE and at TryQuoteClear rather than in OwnedTownLayoutSnapshot, because a
            // retired record's condition is forced to 0 by OwnedBaseProgression.ValidateStructures, so
            // after the fact nothing can tell which verb produced it.
            if (record.condition01 <= 0f)
            { reason = "This is rubble. Clear it for salvage instead of selling it."; return false; }
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

        // =====================================================================
        //  WO-1872 — CLEARING THE RUBBLE OF THE CAPTURED CAMP
        //
        //  Owner, 2026-09-18: "I think we should load a destroyed camp and then clear the rubble and
        //  give them resources to make player designed layouts."
        //
        //  This is deliberately NOT a new system. It is TrySell's transaction with two substitutions:
        //  the gate is "this record is rubble" instead of "this record is standing", and the credit
        //  is SALVAGE (a tunable share of the catalog build cost) instead of the sale refund. The
        //  state edit, the wallet seam, the cap/overflow behaviour and the body teardown are the same
        //  OwnedBaseConstruction.TryRetire -> GameStateService.TryCommitOwnedBaseConstruction ->
        //  SetActive(false) path TrySell already uses and already proves.
        // =====================================================================

        /// <summary>
        /// The live salvage share, as an int percent. Clamped 0..100 HERE, at the one consumer:
        /// <c>RemoteTunables.Int</c> answers whatever a console row says, and a row above 100 would
        /// pay more for a ruin than the structure cost to build.
        /// </summary>
        public static int SalvagePct
        {
            get
            {
                int pct = DeNelle.Core.Ops.RemoteTunables.Int(DeNelle.Core.Ops.RemoteTunables.KeyTownCaptureSalvagePct);
                return pct < 0 ? 0 : pct > 100 ? 100 : pct;
            }
        }

        /// <summary>
        /// What clearing this ruin would pay. Read-only: quotes never move state, so the panel can
        /// price the button without committing anything.
        /// </summary>
        public static bool TryQuoteClear(string instanceId, out CoreCost salvage, out string reason)
        {
            salvage = default;
            var service = GameStateService.Instance;
            var property = service?.State?.OwnedBase;
            if (!OwnedBaseProgression.Validate(property, out reason)) return false;
            var record = property.structures.Find(s => s.instanceId == instanceId);
            if (!CapturedTownStanddown.IsClearableRubble(record))
            { reason = "Only the captured camp's rubble can be cleared."; return false; }
            var entry = CatalogRegistry.Get(record.placement.itemId);
            if (entry?.repo == null)
            { reason = "The structure catalog is unavailable."; return false; }
            // The snapshot validator is the shared authority for what may leave the layout at all -
            // it is what still refuses the town's objective (OwnedTownLayoutSnapshot's clearableWall
            // carve-out). Quoting through it means the button cannot offer a clear the commit refuses.
            if (!OwnedBaseConstruction.TryRetire(property, property.revision, instanceId, out var next, out reason) ||
                !OwnedTownLayoutSnapshot.TryCreate(next, service.State.HeroClass, out _, out reason)) return false;
            salvage = CapturedTownStanddown.Salvage(entry.repo.cost, SalvagePct);
            reason = null;
            return true;
        }

        /// <summary>
        /// Clears one ruin: retires the record, credits the salvage through the normal wallet seam
        /// (cap and overflow behaviour unchanged - <paramref name="credited"/> is what actually
        /// landed, which is not always what was quoted), and takes the body out of the scene.
        /// </summary>
        public static bool TryClearRubble(string instanceId, int expectedRevision, out CoreCost credited, out string reason)
        {
            credited = default;
            if (IsBusy || OwnedTownDesignService.IsBusy)
            { reason = "Wait for the current town change to finish."; return false; }
            var scene = SceneManager.GetActiveScene();
            var service = GameStateService.Instance;
            var property = service?.State?.OwnedBase;
            if (scene.name != OwnedTownScenePose.SceneName || property == null)
            { reason = "Enter your personal town to clear rubble."; return false; }
            if (property.revision != expectedRevision)
            { reason = "The town changed; review the clearing again."; return false; }
            if (!TryQuoteClear(instanceId, out var salvage, out reason)) return false;
            var record = property.structures.Find(s => s.instanceId == instanceId);
            string label = CatalogRegistry.Get(record.placement.itemId)?.displayName ?? record.placement.itemId;
            if (!OwnedTownScenePose.TryResolveStructure(scene, record, out var target, out reason)) return false;
            if (!OwnedBaseConstruction.TryRetire(property, expectedRevision, instanceId, out var next, out reason) ||
                !service.TryCommitOwnedBaseConstruction(expectedRevision, next, default, salvage, out credited, out reason))
            {
                DeNelle.Core.Diagnostics.FlowTrace.Warn("OwnedTown",
                    $"RUBBLE CLEAR refused for '{label}' ({instanceId}): {reason} — nothing was credited " +
                    "and the ruin is still standing in the save.");
                return false;
            }
            // Keep captured identities available for replay; never destroy template objects.
            target.gameObject.SetActive(false);
            Physics.SyncTransforms();
            DeNelle.Core.Diagnostics.FlowTrace.Step("OwnedTown",
                $"RUBBLE CLEARED (WO-1872) '{label}' ({instanceId}) at {SalvagePct}% of its build cost: " +
                $"quoted w{salvage.wood}/i{salvage.iron}/s{salvage.stone}/c{salvage.crystals}, " +
                $"CREDITED w{credited.wood}/i{credited.iron}/s{credited.stone}/c{credited.crystals} " +
                "(a gap is the storage cap, not a defect). Town revision=" + next.revision);
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

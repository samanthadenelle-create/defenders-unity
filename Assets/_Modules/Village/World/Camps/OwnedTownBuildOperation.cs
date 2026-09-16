using System;
using System.Collections;
using DeNelle.Core.State;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using CoreCost = DeNelle.Core.Catalog.ResourceCost;

namespace DeNelle.Village.World.Camps
{
    public sealed class OwnedTownBuildOperation : MonoBehaviour
    {
        private PlacedStructure _target;
        private PlacementGrid _grid;
        private GameStateService _service;
        private BuildTimerService _timer;
        private string _id, _before;
        private PlacedStructureData _placement;
        private CoreCost _price;
        private bool _free, _finished;
        private BuildGraceReason _grace;
        private Action<bool, string> _completed;
        private int _navUpdates, _revision;

        public void Begin(PlacedStructure target, PlacementGrid grid, GameStateService service, BuildTimerService timer,
            string id, PlacedStructureData placement, CoreCost price, bool free, BuildGraceReason grace, Action<bool, string> completed)
        {
            _target = target; _grid = grid; _service = service; _timer = timer; _id = id; _placement = placement;
            _price = price; _free = free; _grace = grace; _completed = completed;
            _revision = service.State.OwnedBase.revision; _before = JsonConvert.SerializeObject(service.State.OwnedBase);
            NavMesh.onPreUpdate += OnNavUpdate; Physics.SyncTransforms(); StartCoroutine(ValidateAndSave());
        }

        private void OnNavUpdate() => _navUpdates++;
        private IEnumerator ValidateAndSave()
        {
            float deadline = Time.realtimeSinceStartup + 3f;
            while (_navUpdates < 2 && Time.realtimeSinceStartup < deadline) yield return null;
            yield return null;
            if (_target == null || _service == null || _timer == null || SceneManager.GetActiveScene() != gameObject.scene)
            { Finish(false, "Construction was interrupted before saving."); yield break; }
            if (_navUpdates < 2) { Finish(false, "Navigation did not finish checking the building."); yield break; }
            if (!OwnedTownNavigation.Validate(gameObject.scene, out var reason)) { Finish(false, reason); yield break; }
            if (JsonConvert.SerializeObject(_service.State.OwnedBase) != _before)
            { Finish(false, "The town changed during placement. Please try again."); yield break; }
            if (!_timer.TryStartOwnedTownBuild(_id, _placement, _revision, _price, _free, _grace, out _, out reason))
            { Finish(false, reason); yield break; }
            var tower = _target.GetComponent<DefenseTower>();
            if (tower != null) tower.enabled = true;
            if (_service.State.OwnedBase.structures.Find(s => s.instanceId == _id).constructionPending)
                UnderConstructionVisual.Attach(_target, OwnedTownJobKey.Compose(_service.State.OwnedBase.baseId, _id));
            Finish(true, null);
        }

        public void Cancel() => Finish(false, "Construction cancelled before saving.");
        private void OnDisable() => Cancel();
        private void Finish(bool saved, string reason)
        {
            if (_finished) return;
            _finished = true; NavMesh.onPreUpdate -= OnNavUpdate;
            if (!saved && _target != null)
            {
                _grid?.Free(_target.gridCell, _target.footprint);
                _target.gameObject.SetActive(false); Destroy(_target.gameObject); Physics.SyncTransforms();
            }
            BuildModeController.InvalidateTowerCount(); OwnedTownConstructionService.Release(this);
            var completed = _completed; _completed = null;
            try { completed?.Invoke(saved, reason); }
            finally { if (this != null) Destroy(gameObject); }
        }
    }
}

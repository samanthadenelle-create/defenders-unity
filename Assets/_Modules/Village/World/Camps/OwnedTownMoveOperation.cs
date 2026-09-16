using System;
using System.Collections;
using DeNelle.Core.State;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

namespace DeNelle.Village.World.Camps
{
    public sealed class OwnedTownMoveOperation : MonoBehaviour
    {
        private Transform _target;
        private Vector3 _original;
        private GameStateService _service;
        private OwnedBaseState _proposal;
        private string _before;
        private Action<bool, string> _completed;
        private bool _finished;
        private int _navUpdates;

        public void Begin(Transform target, Vector3 destination, GameStateService service,
            OwnedBaseState proposal, Action<bool, string> completed)
        {
            _target = target; _original = target.localPosition; _service = service;
            _proposal = proposal; _before = JsonConvert.SerializeObject(service.State.OwnedBase);
            _completed = completed;
            NavMesh.onPreUpdate += OnNavigationUpdate;
            target.localPosition = destination;
            Physics.SyncTransforms();
            StartCoroutine(ValidateAndCommit());
        }

        private void OnNavigationUpdate() => _navUpdates++;

        private IEnumerator ValidateAndCommit()
        {
            float deadline = Time.realtimeSinceStartup + 3f;
            // onPreUpdate fires before the navigation update. Resume only after two later
            // frames, so a same-frame path through the old carve cannot authorize a save.
            while (_navUpdates < 2 && Time.realtimeSinceStartup < deadline) yield return null;
            yield return null;
            if (_target == null || _service == null || SceneManager.GetActiveScene() != gameObject.scene)
            { Finish(false, "Town movement was interrupted."); yield break; }
            if (_navUpdates < 2)
            { Finish(false, "Navigation did not update; the tower was returned to its saved position."); yield break; }
            if (!OwnedTownNavigation.Validate(gameObject.scene, out var reason))
            { Finish(false, reason); yield break; }
            if (JsonConvert.SerializeObject(_service.State.OwnedBase) != _before)
            { Finish(false, "Your town changed during the move. Please try again."); yield break; }
            if (!_service.TryCommitOwnedBaseRevision(_proposal, out reason))
            { Finish(false, reason); yield break; }
            // Keep the shared factory metadata/occupancy in step only after durability.
            // Failed previews never change either, so restoring the pose is sufficient.
            var identity = _target.GetComponent<OwnedTownPlacedIdentity>();
            var record = identity != null ? _proposal.structures.Find(s => s.instanceId == identity.InstanceId) : null;
            var placed = _target.GetComponent<PlacedStructure>();
            if (record != null && record.inheritedPose == null && placed != null)
            {
                var grid = _target.parent != null ? _target.parent.GetComponentInChildren<PlacementGrid>(true) : null;
                grid?.Free(placed.gridCell, placed.footprint);
                placed.gridCell = new Vector2Int(record.placement.cellX, record.placement.cellZ);
                grid?.Occupy(placed.gridCell, placed.footprint, placed.itemId);
            }
            Finish(true, null);
        }

        private void Finish(bool committed, string reason)
        {
            if (_finished) return;
            _finished = true;
            NavMesh.onPreUpdate -= OnNavigationUpdate;
            if (!committed && _target != null)
            {
                _target.localPosition = _original;
                Physics.SyncTransforms();
            }
            OwnedTownDesignService.Release(this);
            var completed = _completed; _completed = null;
            try { completed?.Invoke(committed, reason); }
            finally { if (this != null) Destroy(gameObject); }
        }

        private void OnDisable() => Finish(false, "Town movement was interrupted before saving.");
    }
}

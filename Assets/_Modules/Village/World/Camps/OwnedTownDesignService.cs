using DeNelle.Core.State;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

namespace DeNelle.Village.World.Camps
{
    /// <summary>Reversible move preview; save only after the live navigation update.</summary>
    public static class OwnedTownDesignService
    {
        private static OwnedTownMoveOperation _active;
        public static bool IsBusy => _active != null;
        internal static void Release(OwnedTownMoveOperation operation) { if (_active == operation) _active = null; }

        public static bool TryBeginMove(string instanceId, Vector3 worldDestination,
            System.Action<bool, string> completed, out string reason)
        {
            if (IsBusy || OwnedTownConstructionService.IsBusy) { reason = "Wait for the current town change to finish."; return false; }
            var scene = SceneManager.GetActiveScene();
            var service = GameStateService.Instance;
            var property = service?.State?.OwnedBase;
            if (scene.name != OwnedTownScenePose.SceneName || property == null)
            { reason = "Enter your personal town to change its design."; return false; }
            if (!OwnedBaseProgression.Validate(property, out reason)) return false;
            var record = property.structures.Find(s => s.instanceId == instanceId);
            if (record == null || record.retired || record.condition01 <= 0f ||
                !OwnedTownScenePose.TryResolveStructure(scene, record, out var target, out reason))
            { reason = "The selected structure is not present in this town."; return false; }
            // Player-built walls use the same saved grid; fitted perimeter walls remain fixed.
            if (OwnedTownTemplateManifest.Load()?.IsEditableStructure(record) != true)
            { reason = "Choose a defense tower or a wall you built to reposition."; return false; }
            if (!Finite(worldDestination) || new Vector2(worldDestination.x, worldDestination.z).magnitude > 54f)
            { reason = "Keep the tower inside your town."; return false; }
            PlacementGrid constructionGrid = null;
            if (record.inheritedPose == null)
            {
                constructionGrid = target.parent != null ? target.parent.GetComponentInChildren<PlacementGrid>(true) : null;
                if (constructionGrid == null) { reason = "The construction grid is unavailable; reenter your town."; return false; }
                worldDestination = constructionGrid.SnapToGrid(worldDestination);
            }
            if (!NavMesh.SamplePosition(worldDestination, out var seat, 1.5f, NavMesh.AllAreas) ||
                Mathf.Abs(seat.position.y - worldDestination.y) > .25f)
            { reason = "Choose a level, walkable place for the tower."; return false; }
            Vector3 delta = worldDestination - target.position;
            foreach (var collider in target.GetComponentsInChildren<Collider>(false))
            {
                if (!collider.enabled || collider.isTrigger) continue;
                var bounds = collider.bounds;
                foreach (var hit in Physics.OverlapBox(bounds.center + delta, bounds.extents * .95f,
                    Quaternion.identity, ~0, QueryTriggerInteraction.Ignore))
                {
                    if (hit.transform == target || hit.transform.IsChildOf(target)) continue;
                    if (hit.bounds.max.y <= bounds.min.y + delta.y + .1f) continue; // supporting floor
                    reason = "That place overlaps another structure or character."; return false;
                }
            }
            Vector3 local = target.parent != null ? target.parent.InverseTransformPoint(worldDestination) : worldDestination;
            OwnedBaseState next;
            if (record.inheritedPose != null)
            {
                var pose = record.inheritedPose.Clone();
                pose.x = local.x; pose.y = local.y; pose.z = local.z;
                if (!OwnedBaseProgression.TryChangeInheritedPose(property, instanceId, pose, out next, out reason)) return false;
            }
            else
            {
                var cell = constructionGrid.WorldToCell(worldDestination);
                if (!OwnedBaseConstruction.TryMoveGrid(property, property.revision, instanceId, cell.x, cell.y, out next, out reason)) return false;
            }
            if (!OwnedTownLayoutSnapshot.TryCreate(next, service.State.HeroClass, out _, out reason)) return false;
            if (!OwnedTownNavigation.Validate(scene, out reason)) return false;
            var host = new GameObject("OwnedTownMoveValidation");
            SceneManager.MoveGameObjectToScene(host, scene);
            _active = host.AddComponent<OwnedTownMoveOperation>();
            _active.Begin(target, local, service, next, completed);
            reason = null;
            return true;
        }

        private static bool Finite(Vector3 value) =>
            !float.IsNaN(value.x) && !float.IsInfinity(value.x) &&
            !float.IsNaN(value.y) && !float.IsInfinity(value.y) &&
            !float.IsNaN(value.z) && !float.IsInfinity(value.z);
    }
}

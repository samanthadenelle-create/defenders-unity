using System;
using System.Collections.Generic;
using System.Globalization;
using DeNelle.Core.State;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DeNelle.Village.World.Camps
{
    /// <summary>Preserves inherited scene geometry independently of the story build grid.</summary>
    public static class OwnedTownScenePose
    {
        public const string SceneName = DeNelle.Core.SceneRouter.OwnedTownIronBastion;
        public static bool TryResolveStructure(Scene scene, OwnedBaseStructure record, out Transform target, out string reason)
        {
            target = null;
            if (record == null) { reason = "The saved structure is missing."; return false; }
            if (record.inheritedPose != null) return TryResolve(scene, record.inheritedPose, out target, out reason);
            if (!scene.IsValid() || !scene.isLoaded) { reason = "The town scene is unavailable."; return false; }
            foreach (var root in scene.GetRootGameObjects())
                foreach (var identity in root.GetComponentsInChildren<OwnedTownPlacedIdentity>(true))
                    if (identity.InstanceId == record.instanceId)
                    {
                        if (target != null) { target = null; reason = "The live construction identity is duplicated."; return false; }
                        target = identity.transform;
                    }
            reason = target == null ? "The saved construction has no live body." : null;
            return target != null;
        }
        public static OwnedStructurePose Capture(Transform target)
        {
            if (target == null || !target.gameObject.scene.IsValid())
                throw new ArgumentException("Capture requires a scene structure.", nameof(target));
            var indices = new List<string>();
            for (var node = target; node != null; node = node.parent)
                indices.Add(node.GetSiblingIndex().ToString(CultureInfo.InvariantCulture));
            indices.Reverse();
            var p = target.localPosition; var q = target.localRotation; var s = target.localScale;
            var identity = target.GetComponent<OwnedTemplateIdentity>();
            var frame = target.parent != null ? target.parent.localToWorldMatrix : Matrix4x4.identity;
            var savedFrame = new float[16];
            for (int i = 0; i < 16; i++) savedFrame[i] = frame[i];
            var pose = new OwnedStructurePose {
                sourceScene = target.gameObject.scene.name, sourcePath = string.Join("/", indices),
                sourceName = target.name, x = p.x, y = p.y, z = p.z,
                templateStructureId = identity != null ? identity.TemplateId : null,
                parentFrame = identity != null ? savedFrame : null,
                qx = q.x, qy = q.y, qz = q.z, qw = q.w, sx = s.x, sy = s.y, sz = s.z
            };
            if (!OwnedBaseProgression.ValidatePose(pose, out var reason))
                throw new InvalidOperationException(reason);
            return pose;
        }

        public static bool TryResolve(Scene scene, OwnedStructurePose pose, out Transform target, out string reason)
        {
            target = null;
            if (!OwnedBaseProgression.ValidatePose(pose, out reason)) return false;
            bool ownedTemplate = (scene.name == SceneName || scene.name == DeNelle.Core.Combat.PracticeCombatPolicy.SceneName) && pose.sourceScene == "RaidBase_IronBastion";
            if (!scene.IsValid() || !scene.isLoaded || (scene.name != pose.sourceScene && !ownedTemplate))
            { reason = "The captured scene template is not loaded."; return false; }
            if (!string.IsNullOrEmpty(pose.templateStructureId))
            {
                Transform match = null;
                foreach (var root in scene.GetRootGameObjects())
                    foreach (var identity in root.GetComponentsInChildren<OwnedTemplateIdentity>(true))
                    {
                        if (identity.TemplateId != pose.templateStructureId) continue;
                        if (match != null) { reason = "Duplicate template structure identity."; return false; }
                        match = identity.transform;
                    }
                if (match == null) { reason = "Captured template structure is missing; migration is required."; return false; }
                var frame = match.parent != null ? match.parent.localToWorldMatrix : Matrix4x4.identity;
                for (int i = 0; i < 16; i++)
                    if (Mathf.Abs(frame[i] - pose.parentFrame[i]) > .0001f)
                    { reason = "Captured structure coordinate frame changed; migration is required."; return false; }
                target = match; reason = null; return true;
            }
            string[] parts = pose.sourcePath.Split('/');
            var roots = scene.GetRootGameObjects();
            int index = int.Parse(parts[0], CultureInfo.InvariantCulture);
            if (index >= roots.Length) { reason = "Captured scene root is missing."; return false; }
            var node = roots[index].transform;
            for (int i = 1; i < parts.Length; i++)
            {
                index = int.Parse(parts[i], CultureInfo.InvariantCulture);
                if (index >= node.childCount) { reason = "Captured structure is missing from its template."; return false; }
                node = node.GetChild(index);
            }
            if (node.name != pose.sourceName)
            { reason = "Captured structure identity differs from its template."; return false; }
            target = node; reason = null; return true;
        }

        // Resolve and validate every address before moving anything. A changed/missing template
        // leaves the save and scene untouched so it remains recoverable, never silently deleted.
        public static bool TryRestoreInherited(Scene scene, IReadOnlyList<OwnedBaseStructure> structures, out string reason)
        {
            if (structures == null || structures.Count == 0)
            { reason = "No inherited structures were supplied."; return false; }
            var targets = new List<Transform>(structures.Count);
            var unique = new HashSet<Transform>();
            foreach (var structure in structures)
            {
                if (structure == null) { reason = "Missing inherited structure record."; return false; }
                if (!TryResolve(scene, structure.inheritedPose, out var target, out reason)) return false;
                if (!unique.Add(target)) { reason = "Two inherited records address the same structure."; return false; }
                targets.Add(target);
            }
            for (int i = 0; i < structures.Count; i++)
            {
                var pose = structures[i].inheritedPose;
                targets[i].SetLocalPositionAndRotation(new Vector3(pose.x, pose.y, pose.z),
                    new Quaternion(pose.qx, pose.qy, pose.qz, pose.qw));
                targets[i].localScale = new Vector3(pose.sx, pose.sy, pose.sz);
            }
            reason = null; return true;
        }
    }
}

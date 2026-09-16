using System;
using System.Collections.Generic;
using DeNelle.Core.State;
using UnityEngine;

namespace DeNelle.Village.World.Camps
{
    [Serializable]
    public sealed class OwnedTownTemplateManifest
    {
        public const string ResourcePath = "OwnedTown/IronBastionTemplate";
        public const string Version = "iron-bastion-20260911";
        public string templateVersion;
        public List<Entry> entries = new List<Entry>();

        [Serializable]
        public sealed class Entry
        {
            public OwnedBaseStructure structure;
            public bool movableTower;
            public Vector3 localBoundsCenter;
            public Vector3 localBoundsExtents;
        }

        public static OwnedTownTemplateManifest Load()
        {
            var text = Resources.Load<TextAsset>(ResourcePath);
            return text == null ? null : JsonUtility.FromJson<OwnedTownTemplateManifest>(text.text);
        }

        public bool IsEditableStructure(OwnedBaseStructure record)
        {
            if (IsEditableTower(record)) return true;
            return record != null && !record.retired && !record.constructionPending && record.inheritedPose == null &&
                DeNelle.Core.Catalog.CatalogRegistry.Get(record.placement.itemId)?.repo?.behaviorId == "WallSegment";
        }

        public bool IsEditableTower(OwnedBaseStructure record)
        {
            if (record == null || record.retired || record.constructionPending) return false;
            var pose = record.inheritedPose;
            if (pose == null)
            {
                string behavior = DeNelle.Core.Catalog.CatalogRegistry.Get(record.placement.itemId)?.repo?.behaviorId;
                return behavior == "DefenseTower" || behavior == "ArcaneTower";
            }
            var entry = entries.Find(e => !string.IsNullOrEmpty(pose.templateStructureId)
                ? e.structure.inheritedPose.templateStructureId == pose.templateStructureId
                : e.structure.inheritedPose.sourcePath == pose.sourcePath && e.structure.inheritedPose.sourceName == pose.sourceName);
            return entry != null && entry.movableTower;
        }

        public static Matrix4x4 WorldMatrix(OwnedStructurePose pose)
        {
            var parent = Matrix4x4.identity;
            if (pose.parentFrame != null) for (int i = 0; i < 16; i++) parent[i] = pose.parentFrame[i];
            return parent * Matrix4x4.TRS(new Vector3(pose.x, pose.y, pose.z),
                new Quaternion(pose.qx, pose.qy, pose.qz, pose.qw), new Vector3(pose.sx, pose.sy, pose.sz));
        }

        public static Bounds WorldBounds(Entry entry, OwnedStructurePose pose)
        {
            var matrix = WorldMatrix(pose);
            var bounds = new Bounds(matrix.MultiplyPoint3x4(entry.localBoundsCenter), Vector3.zero);
            for (int corner = 0; corner < 8; corner++)
            {
                var sign = new Vector3((corner & 1) == 0 ? -1 : 1, (corner & 2) == 0 ? -1 : 1, (corner & 4) == 0 ? -1 : 1);
                bounds.Encapsulate(matrix.MultiplyPoint3x4(entry.localBoundsCenter + Vector3.Scale(sign, entry.localBoundsExtents)));
            }
            return bounds;
        }
    }
}

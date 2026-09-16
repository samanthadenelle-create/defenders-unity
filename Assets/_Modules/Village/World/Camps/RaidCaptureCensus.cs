using System;
using System.Collections.Generic;
using DeNelle.Core.Catalog;
using DeNelle.Core.State;
using UnityEngine;
using UnityEngine.SceneManagement;
using CoreCost = DeNelle.Core.Catalog.ResourceCost;

namespace DeNelle.Village.World.Camps
{
    /// <summary>Precombat identities survive destroyed objects; condition is measured at victory.</summary>
    public sealed class RaidCaptureCensus
    {
        private sealed class Entry
        {
            public OwnedBaseStructure Record;
            public Func<float> Condition;
        }
        private readonly List<Entry> _entries = new List<Entry>();
        private readonly string _receipt = "capture:" + Guid.NewGuid().ToString("N");
        private OwnedBaseState _settled;

        public RaidCaptureCensus(Scene scene)
        {
            if (scene.name != "RaidBase_IronBastion")
                throw new InvalidOperationException("Only the final raid supplies a personal town.");
            foreach (var root in scene.GetRootGameObjects())
                foreach (var node in root.GetComponentsInChildren<Transform>(true))
                {
                    var wall = node.GetComponent<WallSegment>();
                    var tower = node.GetComponent<DefenseTower>();
                    var spire = node.GetComponent<RaidSpire>();
                    if (wall == null && tower == null && spire == null) continue;
                    string id = wall != null ? "wall_stone" : tower != null ? tower.CatalogId : spire.CatalogId;
                    if (CatalogRegistry.Get(id)?.repo == null)
                        throw new InvalidOperationException("Captured structure lacks a catalog identity: " + node.name);
                    var pose = OwnedTownScenePose.Capture(node);
                    if (string.IsNullOrEmpty(pose.templateStructureId))
                        throw new InvalidOperationException("Captured structure lacks a baked stable identity: " + node.name);
                    _entries.Add(new Entry {
                        Record = new OwnedBaseStructure {
                            instanceId = "owned:" + _receipt + ":" + pose.templateStructureId, inheritedPose = pose,
                            placement = new PlacedStructureData(id, 0, 0, 0, wall != null ? wall.Tier : 1,
                                node.eulerAngles.y, node.position.y)
                        },
                        Condition = wall != null ? (Func<float>)(() => wall != null ? wall.HpFraction : 0f) :
                            tower != null ? (() => tower != null ? tower.HpFraction : 0f) :
                            (() => spire != null ? spire.HpFraction : 0f)
                    });
                }
            if (_entries.Count == 0) throw new InvalidOperationException("Final raid capture census is empty.");
        }

        public OwnedBaseState Settle()
        {
            if (_settled != null) return _settled.Clone();
            var template = new OwnedBaseState {
                baseId = "personal-iron-bastion", sourceRaidId = OwnedBaseProgression.FinalRaidId,
                templateVersion = "iron-bastion-20260911", captureReceiptId = _receipt, suppliesReceiptId = _receipt
            };
            foreach (var entry in _entries)
            {
                var record = entry.Record.Clone();
                record.condition01 = Mathf.Clamp01(entry.Condition());
                template.structures.Add(record);
            }
            if (template.structures.Exists(s => s.condition01 < 1f))
            {
                if (!OwnedTownRepairService.TryChooseFirstRepair(template, out _, out CoreCost quote, out var reason))
                    throw new InvalidOperationException(reason);
                template.repairSupplies = quote;
            }
            if (!OwnedBaseProgression.Validate(template, out var invalid)) throw new InvalidOperationException(invalid);
            _settled = template;
            return template.Clone();
        }

        public bool TryCommit(GameStateService service, int stars, out string reason)
        {
            // Freeze victory damage even when the save service is temporarily unavailable.
            var template = Settle();
            if (service == null || service.State == null) { reason = "Save service is unavailable."; return false; }
            // Subsequent final victories never replace or replenish the player's existing town.
            if (service.State.OwnedBase != null) return OwnedBaseProgression.Validate(service.State.OwnedBase, out reason);
            if (!OwnedBaseProgression.TryCapture(null, OwnedBaseProgression.FinalRaidId, _receipt, template, stars, out var captured, out reason))
                return false;
            return service.TryRecordPendingTownCapture(captured, out reason) &&
                service.TryRecoverPendingTownCapture(out reason);
        }
    }
}

using System;
using System.Collections.Generic;
using DeNelle.Core.Catalog;
using DeNelle.Core.Diagnostics;
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
            // WO-1778 §4.5 — this file carried ZERO FlowTrace calls, so every refusal reason the
            // contract produced was invisible unless a caller happened to echo it. The census that
            // SUCCEEDS must be as visible as the one that throws: a capture strand is diagnosed by
            // knowing whether the census existed at all.
            FlowTrace.Step("Raid", $"CAPTURE CENSUS built for '{scene.name}': {_entries.Count} structure " +
                                   $"identity/identities frozen, receipt '{_receipt}'.");
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
            if (service == null || service.State == null)
            {
                reason = "Save service is unavailable.";
                FlowTrace.Warn("Raid", "CAPTURE COMMIT refused: " + reason);
                return false;
            }
            // Subsequent final victories never replace or replenish the player's existing town.
            if (service.State.OwnedBase != null)
            {
                bool owned = OwnedBaseProgression.Validate(service.State.OwnedBase, out reason);
                FlowTrace.Step("Raid", owned
                    ? "CAPTURE COMMIT: a town is already owned and validates — this victory neither replaces nor replenishes it."
                    : "CAPTURE COMMIT refused: the already-owned town does not validate: " + reason);
                return owned;
            }
            if (!OwnedBaseProgression.TryCapture(null, OwnedBaseProgression.FinalRaidId, _receipt, template, stars, out var captured, out reason))
            {
                FlowTrace.Warn("Raid", $"CAPTURE COMMIT refused at TryCapture (stars={stars}, " +
                                       $"required={OwnedBaseProgression.CaptureStarsRequired}): {reason}");
                return false;
            }
            if (!service.TryRecordPendingTownCapture(captured, out reason))
            {
                FlowTrace.Warn("Raid", "CAPTURE COMMIT refused while PARKING the pending receipt: " + reason);
                return false;
            }
            if (!service.TryRecoverPendingTownCapture(out reason))
            {
                // The receipt IS parked — this is the one refusal a later load can still claim.
                FlowTrace.Warn("Raid", "CAPTURE COMMIT refused while CLAIMING the parked receipt: " + reason +
                                       " The pending capture stays on the save, so the next load claims the town.");
                return false;
            }
            FlowTrace.Step("Raid", $"CAPTURE COMMIT succeeded for receipt '{_receipt}'.");
            return true;
        }

        /// <summary>
        /// WO-1778 — park the capture as a PENDING receipt without claiming it, so the victory
        /// screen's forced exit (<c>RaidVictoryController.CanEnterCapturedTown</c>'s bound) defers
        /// the town rather than destroying it: <c>GameStateService</c> recovers a pending capture on
        /// its next load. Never throws — a parking failure must never be able to hold the player in
        /// the raid scene, which is the entire defect this ticket closes.
        /// </summary>
        public bool TryParkForLaterClaim(GameStateService service, int stars, out string reason)
        {
            try
            {
                if (service == null || service.State == null)
                { reason = "Save service is unavailable."; FlowTrace.Warn("Raid", "CAPTURE PARK refused: " + reason); return false; }
                if (service.State.OwnedBase != null)
                { reason = "A town is already owned; nothing to park."; FlowTrace.Step("Raid", "CAPTURE PARK skipped: " + reason); return true; }
                if (service.State.PendingTownCapture != null)
                { reason = "A pending capture is already parked."; FlowTrace.Step("Raid", "CAPTURE PARK skipped: " + reason); return true; }

                var template = Settle();
                if (!OwnedBaseProgression.TryCapture(null, OwnedBaseProgression.FinalRaidId, _receipt, template, stars, out var captured, out reason))
                { FlowTrace.Warn("Raid", "CAPTURE PARK refused at TryCapture: " + reason); return false; }
                if (!service.TryRecordPendingTownCapture(captured, out reason))
                { FlowTrace.Warn("Raid", "CAPTURE PARK refused: " + reason); return false; }

                reason = "receipt '" + _receipt + "' parked as a pending capture";
                FlowTrace.Step("Raid", "CAPTURE PARK succeeded: " + reason + " — the next load claims the town.");
                return true;
            }
            catch (Exception ex)
            {
                reason = ex.Message;
                FlowTrace.Fail("Raid", "CAPTURE PARK threw: " + reason);
                return false;
            }
        }
    }
}

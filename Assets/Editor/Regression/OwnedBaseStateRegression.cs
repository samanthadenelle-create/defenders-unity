// Owned-base local PvE contracts; standalone entry OwnedBaseStateRegression.RunAll.
// Registered in DataRegression.RunAll; standalone entry retained for focused checks.
using System;
using System.Collections.Generic;
using System.Reflection;
using DeNelle.Core.Catalog;
using DeNelle.Core.State;
using Newtonsoft.Json;
using UnityEngine;

namespace DeNelle.Editor.Regression
{
    public static class OwnedBaseStateRegression
    {
        public static void RunAll()
        {
            if (Run(out string report)) Debug.Log("OWNED_BASE_STATE_OK " + report);
            else Debug.LogError("OWNED_BASE_STATE_FAIL " + report);
        }

        public static bool Run(out string report)
        {
            var failures = new List<string>();
            try
            {
                var template = Fixture();
                CheckInheritedPose(failures);
                var pristine = template.Clone();
                foreach (var item in pristine.structures) item.condition01 = 1f;
                Check(!OwnedBaseProgression.TryInspectPristineTown(pristine, out _, out _),
                    "inspection cannot skip ownership reveal", failures);
                pristine.milestoneFlags = OwnedBaseMilestones.OwnershipRevealed;
                Check(OwnedBaseProgression.TryInspectPristineTown(pristine, out var inspected, out _),
                    "pristine town inspection advances restoration", failures);
                Check(inspected.structures.TrueForAll(s => s.condition01 == 1f) &&
                    JsonConvert.SerializeObject(inspected.repairSupplies) == JsonConvert.SerializeObject(pristine.repairSupplies),
                    "inspection invents neither damage nor supply spending", failures);
                Check(OwnedBaseProgression.TryInspectPristineTown(inspected, out var reinspected, out _) &&
                    reinspected.revision == inspected.revision, "inspection retry is idempotent", failures);
                pristine.structures[0].condition01 = .9f;
                Check(!OwnedBaseProgression.TryInspectPristineTown(pristine, out _, out _),
                    "inspection cannot bypass actual damage", failures);
                Check(!OwnedBaseProgression.TryCapture(null, "mage_enclave", "win-a", template, 3, out _, out _), "lower tier cannot capture", failures);
                Check(!OwnedBaseProgression.TryCapture(null, "iron_bastion", "", template, 3, out _, out _), "missing completion receipt refused", failures);
                Check(!OwnedBaseProgression.TryCapture(null, "iron_bastion", "win-a", template, 2, out _, out _), "two-star highest raid cannot capture", failures);
                Check(OwnedBaseProgression.TryCapture(null, "iron_bastion", "win-a", template, 3, out var captured, out _), "three-star final completion captures", failures);
                Check(template.captureReceiptId == "template-receipt", "capture leaves source template intact", failures);
                captured.repairSupplies = new ResourceCost { wood = 1 };
                Check(OwnedBaseProgression.TryCapture(captured, "iron_bastion", "win-a", template, 2, out var retry, out _), "same receipt retry accepted even if stars reread low", failures);
                Check(retry.repairSupplies.wood == 1, "retry does not replenish supplies", failures);
                Check(!OwnedBaseProgression.TryCapture(captured, "iron_bastion", "win-b", template, 3, out _, out _), "second win cannot replace base", failures);
                retry.structures[0].condition01 = 0;
                Check(captured.structures[0].condition01 == .5f, "clone isolates nested condition", failures);

                Check(!OwnedBaseProgression.TryCompleteMilestone(captured, OwnedBaseMilestones.PracticeCompleted, out _, out _), "practice cannot skip FTUE", failures);
                var progress = captured;
                Check(!OwnedBaseProgression.TryRepair(progress, "wall-1", new ResourceCost { wood = 1 }, out _, out _),
                    "repair cannot skip ownership reveal", failures);
                Check(OwnedBaseProgression.TryCompleteMilestone(progress, OwnedBaseMilestones.OwnershipRevealed, out var revealed, out _),
                    "repair fixture reveals ownership", failures);
                string repairId = revealed.structures[0].instanceId;
                var stoneTown = revealed.Clone();
                stoneTown.repairSupplies = new ResourceCost { stone = 3 };
                Check(!OwnedBaseProgression.TryRepair(stoneTown, repairId, new ResourceCost { stone = 4 }, out _, out _),
                    "Stone shortfall refuses repair without minting supplies", failures);
                Check(OwnedBaseProgression.TryRepair(stoneTown, repairId, new ResourceCost { stone = 2 }, out var stoneRepair, out _),
                    "Stone-funded repair succeeds", failures);
                Check(stoneRepair != null && stoneRepair.repairSupplies.stone == 1 &&
                    stoneRepair.structures[0].condition01 == 1f && stoneTown.repairSupplies.stone == 3,
                    "Stone repair spends exact allowance in proposal while preserving source", failures);
                Check(!OwnedBaseProgression.TryRepair(revealed, repairId, new ResourceCost { wood = 2 }, out _, out _),
                    "unaffordable repair cannot advance lesson", failures);
                Check(!OwnedBaseProgression.TryRepair(revealed, repairId, new ResourceCost { wood = -1 }, out _, out _),
                    "negative quote cannot mint supplies", failures);
                Check(OwnedBaseProgression.TryRepair(revealed, repairId, new ResourceCost { wood = 1 }, out var repaired, out _),
                    "affordable repair succeeds", failures);
                Check(repaired.repairSupplies.wood == 0 && repaired.structures[0].condition01 == 1f &&
                    (repaired.milestoneFlags & OwnedBaseMilestones.EssentialRepairCompleted) != 0,
                    "repair spend condition and lesson share one revision", failures);
                Check(revealed.repairSupplies.wood == 1 && revealed.structures[0].condition01 == .5f,
                    "repair proposal preserves source until commit", failures);
                Check(!OwnedBaseProgression.TryRepair(repaired, repairId, default, out _, out _),
                    "replayed repair cannot spend again", failures);
                foreach (var step in new[] { OwnedBaseMilestones.OwnershipRevealed, OwnedBaseMilestones.EssentialRepairCompleted,
                    OwnedBaseMilestones.LayoutChoiceCompleted, OwnedBaseMilestones.PracticeCompleted })
                {
                    if (step == OwnedBaseMilestones.PracticeCompleted)
                    {
                        Check(!OwnedBaseProgression.TryCompleteMilestone(progress, step, out _, out _),
                            "practice requires saved town reentry", failures);
                        var wrongRevision = progress.Clone(); wrongRevision.revision++;
                        Check(!OwnedBaseProgression.TryConfirmReentry(progress, wrongRevision, out _, out _),
                            "different reconstructed revision cannot complete reentry", failures);
                        Check(OwnedBaseProgression.TryConfirmReentry(progress, progress.Clone(), out var entered, out _),
                            "matching reconstructed town completes reentry", failures);
                        progress = entered;
                        Check(OwnedBaseProgression.TryConfirmReentry(progress, progress.Clone(), out var enteredAgain, out _) &&
                            enteredAgain.revision == progress.revision, "repeated reentry is idempotent", failures);
                    }
                    Check(OwnedBaseProgression.TryCompleteMilestone(progress, step, out var next, out _), "milestone " + step, failures);
                    progress = next;
                }
                Check(OwnedBaseProgression.IsAiArenaReady(progress), "completed practice opens AI arena", failures);
                Check(OwnedBaseProgression.TryCompleteMilestone(progress, OwnedBaseMilestones.PracticeCompleted, out var repeated, out _) && repeated.revision == progress.revision,
                    "replayed milestone keeps revision", failures);
                Check(!OwnedBaseProgression.TryAcceptRevision(progress, captured, out _, out _), "stale revision refused", failures);
                var conflict = progress.Clone(); conflict.structures[0].condition01 = .9f;
                Check(!OwnedBaseProgression.TryAcceptRevision(progress, conflict, out _, out _), "same revision content conflict refused", failures);

                var invalid = captured.Clone(); invalid.structures[0].condition01 = float.NaN;
                Check(!SaveSchema.Validate(new SaveSchema.PersistedState { OwnedBase = invalid }).Ok, "save rejects nonfinite condition", failures);
                invalid = captured.Clone(); invalid.structures.Add(invalid.structures[0].Clone());
                Check(!OwnedBaseProgression.Validate(invalid, out _), "duplicate instance IDs refused", failures);
                var old = new SaveSchema.PersistedState { RaidVictories = 999, BaseLayout = new List<PlacedStructureData> { template.structures[0].placement } };
                var migrated = SaveMigrator.MigrateForImport(old, 40);
                Check(migrated.Ok && migrated.Data.OwnedBase == null && migrated.Data.BaseLayout.Count == 1,
                    "old save preserves story and does not infer ownership", failures);
                var wire = new SaveSchema.PersistedState { OwnedBase = progress, BaseLayout = old.BaseLayout };
                var restored = JsonConvert.DeserializeObject<SaveSchema.PersistedState>(JsonConvert.SerializeObject(wire));
                Check(SaveSchema.Validate(restored).Ok && restored.OwnedBase.revision == progress.revision &&
                    restored.OwnedBase.structures[0].instanceId == "building-1" && restored.BaseLayout.Count == 1,
                    "JSON preserves separate property, condition, supplies and story layout", failures);
                var migrationWithProperty = SaveMigrator.MigrateForImport(restored, 40);
                Check(migrationWithProperty.Ok && migrationWithProperty.Data.OwnedBase.revision == progress.revision,
                    "historical migration preserves existing additive property", failures);

                var arena = new ArenaBuildSnapshot { buildId = "ai-1", revision = 1, rulesetId = "practice-v1", heroId = "knight",
                    structures = captured.Clone().structures };
                Check(!arena.TryValidateAndCopy(null, out _, out _), "missing catalog authority refuses publication", failures);
                Check(arena.TryValidateAndCopy(new FixtureAuthority(), out var frozen, out _), "valid AI snapshot accepted", failures);
                arena.structures[0].condition01 = 0;
                Check(frozen.structures[0].condition01 == .5f, "match copy does not follow live editing", failures);
                var badPlacement = arena.structures[0].placement; badPlacement.cellX = 1000; arena.structures[0].placement = badPlacement;
                Check(!arena.TryValidateAndCopy(new FixtureAuthority(), out _, out _), "plot authority rejects out-of-bounds geometry", failures);
                badPlacement.cellX = 0; badPlacement.itemId = "unknown"; arena.structures[0].placement = badPlacement;
                Check(!arena.TryValidateAndCopy(new FixtureAuthority(), out _, out _), "catalog authority rejects unknown IDs", failures);
                CheckService(failures, progress);
            }
            catch (Exception ex) { failures.Add(ex.GetType().Name + ": " + ex.Message); }
            report = failures.Count == 0 ? "capture/retry, FTUE, revisions, migration, JSON, snapshot/apply and arena authority" : string.Join("; ", failures);
            return failures.Count == 0;
        }

        private static void CheckInheritedPose(List<string> failures)
        {
            var property = Fixture();
            property.structures[0].inheritedPose = new OwnedStructurePose {
                sourceScene = "RaidBase_IronBastion", sourcePath = "0/2/13", sourceName = "Wall_13",
                x = 12.125f, y = .635f, z = -24.3f, qy = .70710677f, qw = .70710677f,
                sx = .14f, sy = 1.75f, sz = -.37f
            };
            Check(OwnedBaseProgression.Validate(property, out _), "fitted inherited pose accepted", failures);
            var restored = JsonConvert.DeserializeObject<OwnedBaseState>(JsonConvert.SerializeObject(property));
            Check(JsonConvert.SerializeObject(restored) == JsonConvert.SerializeObject(property),
                "inherited position rotation scale and source identity round-trip exactly", failures);
            var detached = property.Clone();
            detached.structures[0].inheritedPose.x++;
            Check(property.structures[0].inheritedPose.x == 12.125f, "inherited pose clone isolation", failures);
            detached.structures[0].inheritedPose.sx = 0;
            Check(!OwnedBaseProgression.Validate(detached, out _), "zero inherited scale refused", failures);
            detached = property.Clone(); detached.structures[0].inheritedPose.qw = 0;
            Check(!OwnedBaseProgression.Validate(detached, out _), "invalid inherited rotation refused", failures);
            detached = property.Clone(); detached.structures[0].inheritedPose.sourcePath = "0/../1";
            Check(!OwnedBaseProgression.Validate(detached, out _), "invalid hierarchy address refused", failures);
            detached = property.Clone(); detached.structures[0].inheritedPose.z = float.NaN;
            Check(!OwnedBaseProgression.Validate(detached, out _), "nonfinite inherited position refused", failures);
            var movedPose = property.structures[0].inheritedPose.Clone(); movedPose.x += 3f;
            Check(!OwnedBaseProgression.TryChangeInheritedPose(property, "building-1", movedPose, out _, out _),
                "design cannot skip the repair lesson", failures);
            property.milestoneFlags = OwnedBaseMilestones.OwnershipRevealed | OwnedBaseMilestones.EssentialRepairCompleted;
            Check(!OwnedBaseProgression.TryChangeInheritedPose(property, "building-1", property.structures[0].inheritedPose, out _, out _),
                "saving an unchanged layout cannot advance the lesson", failures);
            Check(OwnedBaseProgression.TryChangeInheritedPose(property, "building-1", movedPose, out var designed, out _),
                "meaningful design proposes a saved revision", failures);
            Check(designed.revision == property.revision + 1 && designed.reenteredLayoutRevision == 0 &&
                (designed.milestoneFlags & OwnedBaseMilestones.LayoutChoiceCompleted) != 0 &&
                property.structures[0].inheritedPose.x == 12.125f,
                "design and milestone are atomic, detached, and still require reentry", failures);
            movedPose.sx *= 2;
            Check(!OwnedBaseProgression.TryChangeInheritedPose(property, "building-1", movedPose, out _, out _),
                "design cannot resize inherited art", failures);
        }

        private static void CheckService(List<string> failures, OwnedBaseState property)
        {
            if (GameStateService.Instance != null)
                throw new InvalidOperationException("live GameStateService prevents isolated persistence test");
            var priorProvider = GameStateService.Provider;
            GameObject go = null;
            try
            {
                var provider = new InMemorySaveProvider();
                GameStateService.Provider = provider;
                go = new GameObject("OwnedBasePersistenceOracle");
                var service = go.AddComponent<GameStateService>();
                typeof(GameStateService).GetMethod("Awake", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(service, null);
                service.State.BaseLayout = new List<PlacedStructureData> { new PlacedStructureData("story-building", 8, 9, 0, 1) };
                var intent = Fixture();
                provider.FailWrites = true;
                Check(!service.TryRecordPendingTownCapture(intent, out _) && service.State.PendingTownCapture == null,
                    "failed intent write leaves no in-memory pending capture", failures);
                provider.FailWrites = false;
                Check(service.TryRecordPendingTownCapture(intent, out _), "settled capture intent persists", failures);
                string intentEnvelope = provider.Read(SaveSchema.PlayerPrefsKey);
                intent.structures[0].condition01 = 0f;
                Check(service.State.PendingTownCapture.capture.structures[0].condition01 == .5f,
                    "pending capture detaches its immutable census", failures);
                var conflicting = Fixture(); conflicting.captureReceiptId = conflicting.suppliesReceiptId = "different-win";
                Check(!service.TryRecordPendingTownCapture(conflicting, out _), "second receipt cannot replace pending victory", failures);
                service.State.PendingTownCapture = null;
                provider.FailWrites = true;
                Check(service.Load() && service.State.OwnedBase == null && service.State.PendingTownCapture != null &&
                    provider.Read(SaveSchema.PlayerPrefsKey) == intentEnvelope,
                    "load recovery failure retains durable intent without provisional ownership", failures);
                provider.FailWrites = false;
                Check(service.Load() && service.State.OwnedBase != null && service.State.PendingTownCapture == null &&
                    service.State.OwnedBase.structures[0].condition01 == .5f,
                    "actual load consumer restores frozen capture and consumes intent", failures);
                var recovered = service.State.OwnedBase.Clone();
                recovered.repairSupplies.wood = 0; recovered.revision++;
                Check(service.TryCommitOwnedBaseRevision(recovered, out _) && service.Load() &&
                    service.State.PendingTownCapture == null && service.State.OwnedBase.repairSupplies.wood == 0,
                    "repeated load cannot replenish consumed capture supplies", failures);
                service.State.OwnedBase = null;
                Check(service.TryApplyOwnedBaseRevision(property, out _), "local service accepts property", failures);
                Check(service.TrySave(out _), "initial property persists", failures);
                var durable = provider.Read(SaveSchema.PlayerPrefsKey);
                var priorClock = service.LastLocalSaveUnixMs;
                var changed = property.Clone(); changed.revision++;
                changed.repairSupplies.wood = 0;
                provider.FailWrites = true;
                Check(!service.TryCommitOwnedBaseRevision(changed, out var saveError) && !string.IsNullOrEmpty(saveError),
                    "failed property write reports retryable failure", failures);
                Check(service.State.OwnedBase.revision == property.revision &&
                    service.State.OwnedBase.repairSupplies.wood == property.repairSupplies.wood,
                    "failed commit restores live property and supplies", failures);
                Check(provider.Read(SaveSchema.PlayerPrefsKey) == durable && service.LastLocalSaveUnixMs == priorClock,
                    "failed write preserves durable envelope and recency", failures);
                provider.FailWrites = false;
                Check(service.TryCommitOwnedBaseRevision(changed, out _), "property write retry succeeds", failures);
                changed.structures[0].condition01 = 0;
                Check(service.State.OwnedBase.structures[0].condition01 == .5f, "commit isolates caller data", failures);
                service.State.OwnedBase = null;
                Check(service.Load() && service.State.OwnedBase != null &&
                    service.State.OwnedBase.revision == property.revision + 1 && service.State.OwnedBase.repairSupplies.wood == 0,
                    "load restores committed revision and spent supplies", failures);
                var snapshot = service.Snapshot();
                snapshot.OwnedBase.structures[0].condition01 = .2f;
                Check(service.State.OwnedBase.structures[0].condition01 == .5f, "Snapshot detached from live state", failures);
                snapshot.OwnedBase.revision++;
                typeof(GameStateService).GetMethod("ApplyPersisted", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(service, new object[] { snapshot });
                snapshot.OwnedBase.structures[0].condition01 = 0;
                Check(service.State.OwnedBase.structures[0].condition01 == .2f, "ApplyPersisted detaches received data", failures);
                Check(service.State.BaseLayout[0].itemId == "story-building", "property preserves story layout", failures);
                typeof(GameStateService).GetField("_lastSyncedSnapshot", BindingFlags.NonPublic | BindingFlags.Instance)
                    .SetValue(service, service.Snapshot());
                service.State.OwnedBase.revision++;
                var delta = (GameStateService.SyncDeltaPayload)typeof(GameStateService).GetMethod("BuildDeltaPayload",
                    BindingFlags.NonPublic | BindingFlags.Instance).Invoke(service, null);
                Check(delta != null && !string.IsNullOrEmpty(delta.OwnedBaseJson), "property-only change triggers cloud upload", failures);
                var wire = System.Text.Encoding.UTF8.GetString(GameStateService.BuildSaveBody(service.Snapshot(), "fixture-player"));
                Check(Newtonsoft.Json.Linq.JObject.Parse(wire)["ownedBase"]?["baseId"]?.ToObject<string>() == property.baseId,
                    "cloud body contains ownedBase", failures);
                double future = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + 60000d;
                var staleProperty = new SaveSchema.PersistedState { OwnedBase = property.Clone() };
                Check(service.ApplyBackendState(staleProperty, SaveSchema.CurrentVersion, future, service.State.ResetEpoch) ==
                    GameStateService.BackendApplyOutcome.RejectedValidation, "newer cloud timestamp cannot rewind base revision", failures);
                var cloud = service.Snapshot(); cloud.OwnedBase.revision++;
                Check(service.ApplyBackendState(cloud, SaveSchema.CurrentVersion, future + 1000d, service.State.ResetEpoch) ==
                    GameStateService.BackendApplyOutcome.Applied, "newer cloud property revision restores", failures);
                cloud.OwnedBase.structures[0].condition01 = 0;
                Check(service.State.OwnedBase.structures[0].condition01 == .2f, "cloud restore detaches source", failures);
                Check(service.ApplyBackendState(new SaveSchema.PersistedState(), SaveSchema.CurrentVersion,
                    future + 2000d, service.State.ResetEpoch) == GameStateService.BackendApplyOutcome.Applied && service.State.OwnedBase != null,
                    "older-client omission does not erase property", failures);
                service.State.PendingTownCapture = new PendingTownCapture { capture = Fixture() };
                Check(service.ApplyBackendState(new SaveSchema.PersistedState(), SaveSchema.CurrentVersion,
                    future + 3000d, service.State.ResetEpoch) == GameStateService.BackendApplyOutcome.RejectedValidation &&
                    service.State.PendingTownCapture != null, "cloud cannot displace an unresolved local capture", failures);
                service.State.PendingTownCapture = null;
                var remoteIntent = new SaveSchema.PersistedState { PendingTownCapture = new PendingTownCapture { capture = Fixture() } };
                Check(service.ApplyBackendState(remoteIntent, SaveSchema.CurrentVersion, future + 4000d, service.State.ResetEpoch) ==
                    GameStateService.BackendApplyOutcome.Applied && service.State.PendingTownCapture == null,
                    "remote payload cannot install a local capture replay command", failures);
                CheckPracticeSession(service, provider, failures);
            }
            finally
            {
                if (go != null) UnityEngine.Object.DestroyImmediate(go);
                GameStateService.Provider = priorProvider;
            }
        }

        private static void CheckPracticeSession(GameStateService service, InMemorySaveProvider provider, List<string> failures)
        {
            var property = Fixture();
            property.milestoneFlags = OwnedBaseMilestones.OwnershipRevealed |
                OwnedBaseMilestones.EssentialRepairCompleted | OwnedBaseMilestones.LayoutChoiceCompleted;
            property.reenteredLayoutRevision = 1;
            property.revision = 2; // Reentry confirmation is a later revision than the restored layout.
            var layout = new ArenaBuildSnapshot { buildId = property.baseId, revision = property.revision,
                templateVersion = property.templateVersion, rulesetId = "practice-fixture", heroId = "Knight",
                structures = property.Clone().structures };
            var authority = new FixtureAuthority();
            var notReentered = property.Clone(); notReentered.reenteredLayoutRevision = 0;
            Check(!OwnedTownPracticeSession.TryCreate(notReentered, layout, authority, out _, out _), "practice requires saved reentry", failures);
            var mismatch = layout.Clone(); mismatch.structures[0].condition01 = 1;
            Check(!OwnedTownPracticeSession.TryCreate(property, mismatch, authority, out _, out _), "practice cannot silently repair its saved build", failures);
            Check(OwnedTownPracticeSession.TryCreate(property, layout, authority, out var session, out var sessionReason),
                "practice accepts matching detached build: " + sessionReason, failures);
            if (session == null) return;
            layout.structures[0].condition01 = 0;
            var disposable = session.CopyLayout(); disposable.structures[0].condition01 = 0;
            Check(session.CopyLayout().structures[0].condition01 == .5f, "match layout is isolated from source and import mutations", failures);
            service.State.OwnedBase = property.Clone();
            service.TrySave(out _);
            string durable = provider.Read(SaveSchema.PlayerPrefsKey);
            Check(!session.TrySaveCompletion(service, out _), "unresolved practice cannot complete lesson", failures);
            Check(session.TryResolve(OwnedTownPracticeOutcome.Win, out _) &&
                !session.TryResolve(OwnedTownPracticeOutcome.Loss, out _), "practice outcome cannot be rewritten", failures);
            provider.FailWrites = true;
            Check(!session.TrySaveCompletion(service, out _) && !session.CompletionSaved &&
                provider.Read(SaveSchema.PlayerPrefsKey) == durable && service.State.OwnedBase.milestoneFlags == property.milestoneFlags,
                "failed practice completion preserves durable and live state for retry", failures);
            provider.FailWrites = false;
            Check(session.TrySaveCompletion(service, out _) && session.CompletionSaved, "practice result saves on retry", failures);
            int revision = service.State.OwnedBase.revision;
            Check(session.TrySaveCompletion(service, out _) && service.State.OwnedBase.revision == revision,
                "practice completion retry cannot advance revision again", failures);
            foreach (var outcome in new[] { OwnedTownPracticeOutcome.Loss, OwnedTownPracticeOutcome.Abandoned })
            {
                service.State.OwnedBase = property.Clone();
                OwnedTownPracticeSession.TryCreate(property, session.CopyLayout(), authority, out var another, out _);
                another.TryResolve(outcome, out _);
                Check(another.TrySaveCompletion(service, out _) == (outcome == OwnedTownPracticeOutcome.Loss),
                    "loss completes practice but abandon does not", failures);
            }
            service.State.OwnedBase = property.Clone();
            OwnedTownPracticeSession.TryCreate(property, session.CopyLayout(), authority, out var changed, out _);
            changed.TryResolve(OwnedTownPracticeOutcome.Win, out _);
            service.State.OwnedBase.revision++;
            service.State.OwnedBase.structures[0].placement.level++;
            string concurrent = JsonConvert.SerializeObject(service.State.OwnedBase.structures);
            Check(changed.TrySaveCompletion(service, out _) && JsonConvert.SerializeObject(service.State.OwnedBase.structures) == concurrent,
                "practice completion preserves concurrent construction instead of trapping the player", failures);
            service.State.OwnedBase = property.Clone();
            OwnedTownPracticeSession.TryCreate(property, session.CopyLayout(), authority, out var otherTown, out _);
            otherTown.TryResolve(OwnedTownPracticeOutcome.Win, out _);
            service.State.OwnedBase.captureReceiptId = "different-capture";
            service.State.OwnedBase.suppliesReceiptId = "different-capture";
            Check(!otherTown.TrySaveCompletion(service, out _), "practice cannot complete a different captured town", failures);
        }

        private static OwnedBaseState Fixture() => new OwnedBaseState
        {
            baseId = "base-1", sourceRaidId = "iron_bastion", templateVersion = "bastion-v1",
            captureReceiptId = "template-receipt", suppliesReceiptId = "template-receipt",
            repairSupplies = new ResourceCost { wood = 10 },
            structures = new List<OwnedBaseStructure> { new OwnedBaseStructure { instanceId = "building-1",
                placement = new PlacedStructureData("wall_wood", 0, 0, 0, 1), condition01 = .5f } }
        };

        // Test plot and catalog only; these bounds are fixture data, not production balance.
        private sealed class FixtureAuthority : IArenaBuildValidationAuthority
        {
            public bool Validate(ArenaBuildSnapshot snapshot, out string reason)
            {
                foreach (var item in snapshot.structures)
                    if (item.placement.itemId != "wall_wood" || Math.Abs((long)item.placement.cellX) > 20 || Math.Abs((long)item.placement.cellZ) > 20)
                    { reason = "fixture catalog/plot refusal"; return false; }
                reason = null; return true;
            }
        }
        private sealed class InMemorySaveProvider : ISaveProvider
        {
            public bool FailWrites;
            private readonly Dictionary<string, string> data = new Dictionary<string, string>();
            public bool Exists(string slot) => data.ContainsKey(slot);
            public string Read(string slot) => data.TryGetValue(slot, out var json) ? json : string.Empty;
            public void Write(string slot, string json)
            {
                if (FailWrites) throw new System.IO.IOException("Injected owned-base save failure");
                data[slot] = json;
            }
            public void Delete(string slot) => data.Remove(slot);
        }
        private static void Check(bool ok, string label, List<string> failures) { if (!ok) failures.Add(label); }
    }
}

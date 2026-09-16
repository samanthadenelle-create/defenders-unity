using System;
using System.Reflection;
using DeNelle.Core.State;
using DeNelle.Village.World.Camps;
using UnityEngine;

namespace DeNelle.Editor
{
    public static class OwnedTownConstructionRulesProof
    {
        public static void RunBatch()
        {
            bool hydrated = DeNelle.Core.Catalog.CatalogRegistry.Count == 0;
            try
            {
                if (hydrated) typeof(DeNelle.Village.CatalogBootstrap).GetMethod("Register", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, null);
                var manifest = OwnedTownTemplateManifest.Load();
                var property = new OwnedBaseState {
                    baseId = "construction-rules", sourceRaidId = "iron_bastion", templateVersion = manifest.templateVersion,
                    captureReceiptId = "rules-receipt", suppliesReceiptId = "rules-receipt",
                    milestoneFlags = OwnedBaseMilestones.OwnershipRevealed
                };
                foreach (var entry in manifest.entries)
                {
                    var record = entry.structure.Clone(); record.condition01 = entry.movableTower ? 0f : 1f;
                    property.structures.Add(record);
                }
                Require(OwnedTownLayoutSnapshot.TryCreate(property, HeroClassOpt.Knight, out var baseline, out var reason), reason);
                var legacy = baseline.Clone(); legacy.rulesetId = OwnedTownLayoutSnapshot.LegacyRuleset;
                var authority = new OwnedTownLayoutSnapshot(manifest);
                Require(legacy.TryValidateAndCopy(authority, out _, out reason), "Existing v1 layout failed: " + reason);
                Require(OwnedTownRepairService.TryChooseFirstRepair(property, out var first, out _, out reason), reason);
                Require(manifest.IsEditableTower(first), "A standing central objective must not substitute for a usable defense tower in the first repair lesson.");
                var objective = manifest.entries.Find(e => !e.movableTower && e.structure.placement.itemId == "tower_arcane_spire");
                Require(objective != null && !manifest.IsEditableTower(objective.structure), "Central objective was classified as an editable defense.");
                var edited = baseline.Clone();
                var victim = edited.structures.Find(s => s.instanceId == objective.structure.instanceId);
                victim.retired = true; victim.condition01 = 0f;
                Require(!edited.TryValidateAndCopy(authority, out _, out _), "The fixed objective must not be sellable.");
                edited = baseline.Clone(); edited.structures.RemoveAt(0);
                Require(!edited.TryValidateAndCopy(authority, out _, out _), "Captured identities must not disappear from the census.");
                edited = baseline.Clone();
                var tower = edited.structures.Find(s => manifest.IsEditableTower(s));
                tower.retired = true; tower.condition01 = 0f;
                Require(edited.TryValidateAndCopy(authority, out _, out reason), reason);
                edited.rulesetId = OwnedTownLayoutSnapshot.LegacyRuleset;
                Require(!edited.TryValidateAndCopy(authority, out _, out _), "Legacy rules must not accept construction tombstones.");
                edited = baseline.Clone(); edited.structures.Find(s => manifest.IsEditableTower(s)).placement.level = 99;
                Require(!edited.TryValidateAndCopy(authority, out _, out _), "Upgrade beyond the catalog ceiling must refuse.");
                property.milestoneFlags |= OwnedBaseMilestones.EssentialRepairCompleted;
                foreach (var record in property.structures)
                    if (manifest.IsEditableTower(record)) { record.retired = true; record.condition01 = 0f; }
                var wall = property.structures.Find(s => s.placement.itemId == "wall_stone"); wall.condition01 = .5f;
                Require(OwnedTownRepairService.TryChooseFirstRepair(property, out var later, out _, out reason) && later.instanceId == wall.instanceId,
                    "Later wall repairs must remain available after all editable towers are sold: " + reason);
                Debug.Log("OWNED_TOWN_CONSTRUCTION_RULES_OK legacy compatibility, immutable census/objective, sale tombstone versioning, upgrade ceiling, first repair defense eligibility and later repairs after tower sales");
            }
            finally { if (hydrated) DeNelle.Core.Catalog.CatalogRegistry.Clear(); }
        }

        private static void Require(bool value, string reason) { if (!value) throw new InvalidOperationException(reason); }
    }
}

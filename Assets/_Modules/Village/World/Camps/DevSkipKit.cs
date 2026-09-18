#if DEVELOPMENT_BUILD || UNITY_EDITOR || TESTER_BUILD
using System;
using DeNelle.Core;
using DeNelle.Core.Catalog;
using DeNelle.Core.Diagnostics;
using DeNelle.Core.State;
using UnityEngine;

namespace DeNelle.Village.World.Camps
{
    /// <summary>
    /// Tester skip for the raid → owned-town loop. Owner 2026-09-11: no grinding.
    /// Compiled out of release with this file's #if.
    /// </summary>
    public static class DevSkipKit
    {
        public static string PrepCastlePower()
        {
            var svc = GameStateService.Instance;
            if (svc?.State == null) return "No game state.";
            MaxBuildingTiers(svc);
            MaxPlacedStructureLevels(svc);
            int troops = MaxTroopTypes(svc);
            svc.Save();
            ModifierService.Recompute();
            FlowTrace.Step("DevSkip", "PREP castle: max building tiers + placed levels, " + troops + " troops.");
            return "Maxed buildings and troop types (" + troops + " bodies). Set hero to 15 from this panel.";
        }

        public static string GrantCapturedTownAndEnter()
        {
            var svc = GameStateService.Instance;
            if (svc?.State == null) return "No game state.";
            if (svc.State.OwnedBase != null)
            {
                SceneRouter.GoOwnedTown();
                return "Town already owned — entering.";
            }
            var manifest = OwnedTownTemplateManifest.Load();
            if (manifest == null || manifest.entries == null || manifest.entries.Count == 0)
                return "Owned-town template missing (Resources/OwnedTown/IronBastionTemplate).";
            string receipt = "dev-skip:" + Guid.NewGuid().ToString("N");
            var template = new OwnedBaseState
            {
                baseId = "personal-iron-bastion",
                sourceRaidId = OwnedBaseProgression.FinalRaidId,
                templateVersion = string.IsNullOrEmpty(manifest.templateVersion)
                    ? OwnedTownTemplateManifest.Version : manifest.templateVersion,
                captureReceiptId = receipt,
                suppliesReceiptId = receipt
            };
            bool damagedOne = false;
            foreach (var entry in manifest.entries)
            {
                if (entry?.structure == null) continue;
                var record = entry.structure.Clone();
                if (!damagedOne && entry.movableTower)
                {
                    record.condition01 = 0.4f;
                    damagedOne = true;
                }
                else record.condition01 = 1f;
                template.structures.Add(record);
            }
            if (template.structures.Count == 0) return "Template has no structures.";
            if (OwnedTownRepairService.TryChooseFirstRepair(template, out _, out var quote, out _))
                template.repairSupplies = quote;
            if (!OwnedBaseProgression.TryCapture(null, OwnedBaseProgression.FinalRaidId, receipt, template,
                    OwnedBaseProgression.CaptureStarsRequired, out var captured, out var reason))
                return "Capture refused: " + reason;
            if (!svc.TryRecordPendingTownCapture(captured, out reason) ||
                !svc.TryRecoverPendingTownCapture(out reason))
                return "Save refused: " + reason;
            svc.Save();
            FlowTrace.Step("DevSkip", "DEV 3-STAR CAPTURE granted receipt=" + receipt +
                           " structures=" + captured.structures.Count);
            SceneRouter.GoOwnedTown();
            return "Granted 3-star Iron Bastion town — entering.";
        }

        public static string EnterIronBastionRaid()
        {
            var svc = GameStateService.Instance;
            var army = svc != null && svc.State != null ? svc.State.Army : null;
            if (army != null && army.SlotsRemaining(_ => 1) > 0)
                MaxTroopTypes(svc);
            SceneRouter.GoRaid("RaidBase_IronBastion");
            return "Entering Iron Bastion.";
        }

        public static void MaxBuildingTiers(GameStateService svc)
        {
            if (svc?.State == null) return;
            if (svc.State.BuildingTiers == null)
                svc.State.BuildingTiers = new System.Collections.Generic.Dictionary<string, int>();
            foreach (var b in BuildingTierCatalog.All)
            {
                if (b == null || b.Id == null) continue;
                int max = BuildingTierCatalog.MaxTier(b.Id);
                svc.State.BuildingTiers[b.Id] = Mathf.Max(0, max);
            }
        }

        static void MaxPlacedStructureLevels(GameStateService svc)
        {
            var layout = svc.State.BaseLayout;
            if (layout == null) return;
            for (int i = 0; i < layout.Count; i++)
            {
                var row = layout[i];
                if (string.IsNullOrEmpty(row.itemId)) continue;
                var entry = CatalogRegistry.Get(row.itemId);
                int cap = 1;
                if (entry?.repo != null) cap = Mathf.Max(1, entry.repo.maxLevel);
                cap = Mathf.Min(cap, RepoProps.MaxStructureLevel);
                row.level = cap;
                layout[i] = row;
            }
        }

        public static int MaxTroopTypes(GameStateService svc)
        {
            if (svc?.State?.Army == null) return 0;
            var army = svc.State.Army;
            int added = 0;
            var all = TroopCatalog.All;
            if (all == null) return 0;
            foreach (var def in all)
            {
                if (def == null || string.IsNullOrEmpty(def.Id)) continue;
                if (army.SlotsRemaining(DeNelle.Village.TroopDialogueCommands.SlotOf) <= 0) break;
                bool have = false;
                if (army.Owned != null)
                    foreach (var t in army.Owned)
                        if (t != null && t.TroopDefId == def.Id) { have = true; break; }
                if (have) continue;
                if (army.TrainNow(def.Id, DeNelle.Village.TroopDialogueCommands.SlotOf, _ => true) != null) added++;
            }
            while (army.SlotsRemaining(DeNelle.Village.TroopDialogueCommands.SlotOf) > 0 && all.Count > 0)
            {
                var def = all[added % all.Count];
                if (def == null || army.TrainNow(def.Id, DeNelle.Village.TroopDialogueCommands.SlotOf, _ => true) == null) break;
                added++;
            }
            return army.Owned != null ? army.Owned.Count : 0;
        }

        /// <summary>
        /// WO-1775 §1.6 troops=N: trains up to EXACTLY <paramref name="n"/> total owned troops,
        /// cycling the same TroopCatalog roster + slot resolver MaxTroopTypes uses (Owner ruling:
        /// no grinding). Only ADDS — there is no release/disband verb in this kit, so a save that
        /// already owns more than <paramref name="n"/> troops is left as-is and the caller should
        /// FlowTrace.Warn that the target could not be reached downward.
        /// </summary>
        public static int TrainExactly(GameStateService svc, int n)
        {
            if (svc?.State?.Army == null || n <= 0) return svc?.State?.Army?.Owned?.Count ?? 0;
            var army = svc.State.Army;
            var all = TroopCatalog.All;
            if (all == null || all.Count == 0) return army.Owned != null ? army.Owned.Count : 0;
            int guard = 0;
            while ((army.Owned == null ? 0 : army.Owned.Count) < n &&
                   army.SlotsRemaining(DeNelle.Village.TroopDialogueCommands.SlotOf) > 0 &&
                   guard++ < 10000)
            {
                var def = all[guard % all.Count];
                if (def == null || string.IsNullOrEmpty(def.Id)) continue;
                if (army.TrainNow(def.Id, DeNelle.Village.TroopDialogueCommands.SlotOf, _ => true) == null) break;
            }
            return army.Owned != null ? army.Owned.Count : 0;
        }
    }
}
#endif

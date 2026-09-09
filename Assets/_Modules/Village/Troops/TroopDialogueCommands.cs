// =============================================================================
// TroopDialogueCommands — the Yarn ↔ army seam for the Barracks training flow
// (WO-453 troop-training flow; WO-778 timed queue flip). Holds the training logic the
// Barracks_MainMenu Yarn node drives: open the training panel, and enqueue N of a
// troop onto the Train channel (BarracksService.EnqueueTraining — timed CoC path).
// Instant ArmyStorage.TrainNow remains available for cheats/tests only.
// -----------------------------------------------------------------------------
// REGISTRATION (project canon — NOT [YarnCommand] attributes): every custom Yarn
// command in this project is registered on the ONE shared DialogueRunner via
// DialogueCommandBridge.RegisterCommands() → Reg(name, handler). The YarnSpinner
// source generator THROWS on a duplicate command name across IActionRegistration
// methods, so a SECOND attribute-based registrant would collide. The bridge therefore
// adds two entries that delegate here:
//     Reg("ShowTrainingUI", (Action)TroopDialogueCommands.ShowTrainingUI);
//     Reg("StartTraining",  (Action<string,int>)TroopDialogueCommands.StartTraining);
// This class is the single home for the logic; the bridge is just the wire.
//
// SlotOf is still the Village-side slot resolver seam for ArmyStorage / BarracksService
// (TroopDef.Slots). Resource spend for the live path lives in BarracksService.
// =============================================================================

using System;
using UnityEngine;

namespace DeNelle.Village
{
    /// <summary>
    /// The training logic the Barracks Yarn node drives (open the panel; train troops
    /// into <see cref="ArmyStorage"/>). Registered on the shared runner by
    /// <see cref="DialogueCommandBridge"/>; also called directly by the training panel.
    /// </summary>
    public static class TroopDialogueCommands
    {
        // <<ShowTrainingUI>> — open the code-built barracks training panel, self-healing
        // a host if none exists (the panel builds its own Canvas). Mirrors the bridge's
        // OpenShop/OpenCraft/OpenEquip verb pattern.
        public static void ShowTrainingUI()
        {
            // WO-724 regression guard (defense-in-depth): the barracks NPC/building are already
            // hidden when locked, so this verb is normally unreachable then - but never open the
            // train UI unless the Barracks is genuinely unlocked (ff.barracks ON + founding-complete).
            if (!BarracksUnlock.IsUnlocked)
            {
                DeNelle.Core.Diagnostics.FlowTrace.Step("Barracks",
                    $"ShowTrainingUI refused - Barracks locked (ff.barracks={DeNelle.Core.FeatureFlags.Barracks}, " +
                    $"foundingComplete={BarracksUnlock.FoundingComplete}).");
                // WO-737 toast coverage: the feature-locked case gets player feedback (Danger tone),
                // not a silent no-op — normally unreachable (the NPC/building are hidden when locked).
                DeNelle.Core.UI.ElarionUiKit.ShowToast("The Barracks is not built yet.",
                    DeNelle.Core.UI.ElarionUiKit.ToastTone.Danger);
                return;
            }
            DeNelle.Core.Diagnostics.FlowTrace.Step("Barracks", "ShowTrainingUI - opening the train panel (unlocked).");
            var panel = UnityEngine.Object.FindAnyObjectByType<DeNelle.Village.Hero.TroopTrainingPanel>();
            if (panel == null)
                panel = new GameObject("TroopTrainingPanelHost")
                    .AddComponent<DeNelle.Village.Hero.TroopTrainingPanel>();
            panel.Open();
            Debug.Log("[TroopDialogueCommands] ShowTrainingUI — opened the barracks training panel.");
        }

        // <<ShowMusterUI>> — WO-897: open the Armies panel, where a composition is authored
        // once and MUSTERED onto the existing Train queue in one action. The panel owns the
        // Barracks-locked refusal + toast (ArmyMusterPanel.Show), so this is a straight verb.
        public static void ShowMusterUI()
        {
            DeNelle.Core.Diagnostics.FlowTrace.Step("Muster", "ShowMusterUI - opening the armies/muster panel.");
            ArmyMusterPanel.Show();
        }

        // <<StartTraining troopId qty>> — train qty × troopId straight into the army
        // (used if a Yarn option trains without opening the panel). Returns nothing to
        // Yarn; the void shape matches the bridge's Action<string,int> registration.
        public static void StartTraining(string troopId, int qty)
        {
            int trained = Train(troopId, qty);
            Debug.Log($"[TroopDialogueCommands] StartTraining '{troopId}' x{qty} → enqueued {trained}.");
        }

        /// <summary>
        /// Enqueues up to <paramref name="qty"/> of <paramref name="troopId"/> on the
        /// Train channel via <see cref="BarracksService.EnqueueTraining"/> (WO-778 —
        /// CoC timed training). Spends resource cost at enqueue; the unit lands in the
        /// army when the job completes. Instant <see cref="ArmyStorage.TrainNow"/> is
        /// kept as a method for cheats/tests only — this live path is timed.
        /// Returns the number actually accepted (enqueued).
        /// </summary>
        public static int Train(string troopId, int qty)
        {
            if (string.IsNullOrEmpty(troopId) || qty <= 0) return 0;

            // WO-733 HARD UNLOCK GATE (before any spend): resolve the def and refuse a
            // troop the Barracks tier has not yet unlocked. TroopUnlock is the ONE tier
            // authority (no magic numbers here). A refused train mutates nothing + spends
            // nothing. Covers every path (panel, <<StartTraining>> Yarn verb).
            var gateDef = TroopCatalog.Find(troopId);
            if (gateDef == null)
            {
                DeNelle.Core.Diagnostics.FlowTrace.Warn("TroopTrain",
                    "refuse-unknown id=" + troopId);
                return 0;
            }
            if (!TroopUnlock.IsTrainable(gateDef))
            {
                DeNelle.Core.Diagnostics.FlowTrace.Warn("TroopTrain",
                    "refuse-locked id=" + troopId +
                    " needTier=" + gateDef.UnlockBarracksTier +
                    " haveTier=" + TroopUnlock.EffectiveBarracksTier());
                return 0;   // NO spend, NO army mutation
            }

            // WO-778: timed Train channel (spend + enqueue). BarracksService also re-checks
            // unlock + army room + affordability per unit.
            int enqueued = BarracksService.EnqueueTraining(troopId, qty);
            if (enqueued > 0)
                DeNelle.Core.Diagnostics.FlowTrace.Step("TroopTrain",
                    "train-queued id=" + troopId + " qty=" + enqueued);
            return enqueued;
        }

        /// <summary>Army-cap resolver: every known troop is one body/count. Tier and role
        /// are balanced by stats, cost, training time and explicit per-type caps, never by
        /// making an upgraded troop consume several of the player's ten places.</summary>
        public static int SlotOf(string id)
        {
            var d = TroopCatalog.Find(id);
            return d != null ? 1 : 0;
        }
    }
}

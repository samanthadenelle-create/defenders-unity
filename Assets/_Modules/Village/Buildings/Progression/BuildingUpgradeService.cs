// =============================================================================
// BuildingUpgradeService — the ONE shared execute path for a CITY building
// tier upgrade (WO: MVVM building-upgrade panel).
// -----------------------------------------------------------------------------
// Assembly: DeNelle.Village   Namespace: DeNelle.Village.Buildings.Progression
//
// EXTRACTED VERBATIM from DialogueCommandBridge.CmdTryUpgradeBuilding's execute
// body so BOTH the legacy Yarn command AND the new MVVM upgrade panel run the
// EXACT same model-side mechanics — no duplicated economy/save/recompute logic.
// The behaviour is byte-for-byte identical to the old inline body:
//   * read the catalog cost (Wood/Food/Crystal) for the target tier
//   * require targetTier == current + 1 and a real tier def
//   * spend atomically via EconomyService.TrySpend (false => no-op)
//   * write GameState.BuildingTiers[id] = tier, Save(), ModifierService.Recompute()
//
// This is the SOLE model-side touch of the MVVM slice: the Yarn command now CALLS
// this instead of carrying its own copy; the Yarn-var bookkeeping (the $<id>_Level
// gate var + $lastUpgradeOk) stays in the command (it is presentation-for-Yarn,
// not model mechanics). Village -> Core is a legal asmdef edge.
// =============================================================================

using System;
using UnityEngine;
using DeNelle.Core.State;
using DeNelle.Core.Diagnostics;

namespace DeNelle.Village.Buildings.Progression
{
    /// <summary>
    /// Shared, side-effecting execute for a single city-building tier upgrade. Pure
    /// static surface (no scene wiring) so the Yarn bridge and the MVVM panel both
    /// call ONE path. Returns true only when the spend succeeded and the tier was
    /// written/saved; false (no mutation) for an invalid tier or an unaffordable cost.
    /// </summary>
    public static class BuildingUpgradeService
    {
        /// <summary>
        /// Attempt to buy <paramref name="targetTier"/> of <paramref name="id"/>. Mirrors
        /// the old CmdTryUpgradeBuilding body EXACTLY: only the next tier (current+1) of a
        /// catalogued building is buyable; the cost is the catalog cost; the spend is atomic
        /// (EconomyService.TrySpend); on success it writes GameState.BuildingTiers, persists,
        /// and recomputes the active GameModifiers. No Yarn / UI side effects here.
        /// </summary>
        public static bool TryUpgrade(string id, int targetTier)
        {
            int current = ModifierService.TierOf(id);
            var def = BuildingTierCatalog.TierOf(id, targetTier);
            if (def != null && targetTier == current + 1)
            {
                // WO-432 TECH-GATE: a tier locked behind the Village/Stronghold Tier (Heart of Elarion)
                // can't be bought until the village reaches it — the WC3 "need a Keep for tier-2" rule.
                var gateState = GameStateService.Instance != null ? GameStateService.Instance.State : null;
                int villageTier = gateState != null ? gateState.VillageTier : 0;
                if (def.RequiresVillageTier > villageTier)
                {
                    // WO-842 no-silent-false: this refusal used to be a bare false — on the Yarn
                    // path (which has no VM mirror gate) it was indistinguishable from unaffordable.
                    FlowTrace.Step("Upgrade", id + " tier-" + targetTier
                        + " REJECTED: village-tier gate (requires " + def.RequiresVillageTier
                        + ", have " + villageTier + ")");
                    return false;
                }

                // ── F8-51 TIMER GATES (before the spend, so a rejection costs nothing) ──
                // A building with an ACTIVE build/upgrade timer is LOCKED; a full slot set
                // rejects. Flag OFF / no service = today's instant path. The VM mirrors this
                // check for an honest status line (this bool covers the Yarn path too).
                var timerSvc = DeNelle.Core.FeatureFlags.BuildTimers
                    ? DeNelle.Village.BuildTimerService.Instance : null;
                if (timerSvc != null)
                {
                    if (timerSvc.IsBuilding(id))
                    {
                        DeNelle.Core.Diagnostics.FlowTrace.Warn("BuildTimer",
                            $"upgrade '{id}' REJECTED (busy: {timerSvc.RemainingSeconds(id):0}s)");
                        return false;
                    }
                    // WO-895: a FULL crew set no longer rejects. The Obsidian Builder channel
                    // QUEUES the job (StartUpgrade -> ObsidianQueueEngine.Enqueue returns the
                    // pending job) and auto-pulls it when a crew frees. The old rejection here
                    // was a leftover from the pre-queue era and was the reason the panel could
                    // never reach a "Queued" state. The spend still happens exactly once, at
                    // enqueue, and the tier still applies at completion.
                    if (!timerSvc.HasFreeSlot)
                        DeNelle.Core.Diagnostics.FlowTrace.Step("BuildTimer",
                            $"upgrade '{id}' will QUEUE (no free build slot: {timerSvc.ActiveJobs.Count} active)");

                    // WO-911 (M1, ruling Q4) — a full SLOT set still queues (above); a full LINE
                    // refuses. Checked here, inside the before-the-spend block, so the refusal costs
                    // the player nothing. Depth and concurrency are different axes (WO-911 sec.2d).
                    if (timerSvc.IsLineFull(DeNelle.Core.Jobs.ChannelId.Builder))
                    {
                        DeNelle.Core.Diagnostics.FlowTrace.Warn("BuildTimer",
                            $"upgrade '{id}' REFUSED — Builders queue is full " +
                            $"({timerSvc.QueueDepth(DeNelle.Core.Jobs.ChannelId.Builder)}/" +
                            $"{timerSvc.QueueDepthLimit(DeNelle.Core.Jobs.ChannelId.Builder)}). Nothing charged.");
                        return false;
                    }
                }

                // SPEND - GameState-backed single-source wallet (building-upgrade blocker fix).
                // The city-tier cost (Wood/Food/Crystal) is charged via ResourceLedger, the SAME
                // GameState wallet the harvest economy, dev grants, and the resource-building
                // upgrade path (ResourceBuildingState) already use. The OLD path charged
                // EconomyService.TrySpend, whose Wood/Iron come from a DIVERGENT in-session pool
                // (default 200, reset every scene load) that the player's harvested/reloaded/
                // dev-granted wood NEVER reaches -> CanAfford saw ~200 wood and EVERY city tier
                // silently no-op'd "can't afford" against a FULL GameState wallet (the reported
                // Lumber Mill blocker: 997k wood on the HUD, but the in-session pool short). Food/
                // Crystals were already GameState-backed; routing Wood/Iron through the ledger too
                // makes the wallet the player SEES the wallet the tap SPENDS. City tiers never
                // charge Gold, so ResourceLedger (Wood/Food/Iron/Crystals) covers the whole cost.
                var state = GameStateService.Instance != null ? GameStateService.Instance.State : null;
                var cost = TierCost(def);
                int gold = Mathf.Max(0, def.CostGold);
                if (state != null && state.Resources.Coins >= gold && ResourceLedger.CanAfford(cost))
                {
                    state.Resources.Coins -= gold;
                    if (!ResourceLedger.TrySpend(cost))
                    {
                        state.Resources.Coins += gold;
                        return false;
                    }
                    // F8-51 — cost charged above; the TIER applies at timer COMPLETION
                    // (BuildTimerService.CompleteJob -> CompletedUpgradeApplier -> ApplyTier,
                    // offline-fair). A null job (raced) degrades to the instant apply so a
                    // paid charge is never lost.
                    // WO-911 (M2): `cost` is exactly what ResourceLedger.TrySpend just debited, so
                    // it rides the job and a cancel refunds 100% of it (ruling Q1).
                    if (timerSvc != null &&
                        timerSvc.StartUpgrade(id, targetTier,
                            BuildTimerService.ToJobCost(new DeNelle.Village.ResourceCost(
                                // WO-2005: the lane comes from BuildingTierChargeLane, the ONE
                                // authority (was a copy of the same three-branch rule as TierCost
                                // below). Identical output for every tier; the basket a cancel
                                // refunds is byte-for-byte what it was.
                                // `def.Tier >= 1 ? ... : none` preserves the old else-chain exactly
                                // for a Tier-0 def (which matched no branch and paid nothing).
                                wood: def.Tier >= 1 && BuildingTierChargeLane.For(def) == HarvestResource.Wood ? def.PrimaryMaterialCost : 0,
                                stone: def.Tier >= 1 && BuildingTierChargeLane.For(def) == HarvestResource.Stone ? def.PrimaryMaterialCost : 0,
                                iron: def.Tier >= 1 && BuildingTierChargeLane.For(def) == HarvestResource.Iron ? def.PrimaryMaterialCost : 0,
                                coins: gold))) != null)
                    {
                        // F8 (owner 2026-07-17 "an upgrade timer that doesn't tell"): show the
                        // CoC-style on-building countdown so the player SEES the upgrade working —
                        // the tabbed panel's status text is gone the moment it closes. Reuses the
                        // WO-612 scaffold + world countdown, keyed by the SAME id the job uses.
                        // Guard-wrapped inside; a no-match is a traced no-op, never blocks the buy.
                        DeNelle.Village.UnderConstructionVisual.AttachToBuildingId(id);
                        return true;
                    }

                    ApplyTier(id, targetTier);
                    return true;
                }

                // CLAUDE.md 12 - a full-wallet player must NEVER hit a silent no-op again: name WHY
                // the spend failed (the GameState wallet vs. the tier cost) on a [Flow:Upgrade]
                // line, so a future "tap does nothing" is one read, not a re-theorise.
                // SEVERITY: Capture, NOT Fail (2026-08-16). "Too poor to upgrade" is a NORMAL,
                // player-facing outcome - the VM answers it with "You can't afford that yet." - so
                // a broke player minted an F8 error (plus a screenshot) on every tier tap and buried
                // the real captures. Every sibling spend path already logs this non-error
                // (PlacedStructureUpgradeService, ResourceBuildingState, WallRepairController,
                // PartyShopVM). The state dump is UNCHANGED; only the severity is honest now.
                FlowTrace.Capture("Upgrade", id + " tier-" + targetTier + " spend REJECTED - need W"
                    + def.PrimaryMaterialCost + " primary/G" + def.CostGold + ", wallet W"
                    + ResourceLedger.Balance(HarvestResource.Wood) + "/S"
                    + ResourceLedger.Balance(HarvestResource.Stone) + "/I"
                    + ResourceLedger.Balance(HarvestResource.Iron) + "/G" + (state != null ? state.Resources.Coins : 0)
                    + (state == null ? " (no GameState)" : ""));
                return false;
            }

            // WO-842 no-silent-false (the captured F8 branch): a null def or a targetTier that is
            // not current+1 used to fall through to a BARE false, which the VM then misreported as
            // "can't afford" against a full wallet. Name the real reason so the next capture is
            // one read.
            if (def == null)
                FlowTrace.Fail("Upgrade", id + " tier-" + targetTier
                    + " REJECTED: no catalog tier def (unknown building/tier)");
            else
                FlowTrace.Fail("Upgrade", id + " tier-" + targetTier
                    + " REJECTED: not the next tier (current=" + current + ", buyable="
                    + (current + 1) + (targetTier <= current ? "; target already owned" : "") + ")");
            return false;
        }

        /// <summary>
        /// The city-tier cost as a GameState-backed harvest cost list (Wood/Food/Crystals). City
        /// tiers never charge Gold, so <see cref="ResourceLedger"/> - the single-source GameState
        /// wallet - covers the full cost. Zero-amount lines are omitted. Shared by
        /// <see cref="TryUpgrade"/> (the spend) and <see cref="CanAffordTier"/> (the panel's
        /// affordance) so ONE wallet drives both.
        /// </summary>
        private static System.Collections.Generic.List<ResourceCost> TierCost(BuildingTierDef def)
        {
            var list = new System.Collections.Generic.List<ResourceCost>(3);
            if (def == null) return list;
            int primary = def.PrimaryMaterialCost;
            // WO-2005 — the tier-index -> resource rule now lives in exactly one place
            // (BuildingTierChargeLane). Behaviour is unchanged: T1 Wood, T2 Food (= the Stone
            // wallet slot), T3+ Iron, and a zero primary still emits no line. The `Tier >= 1`
            // term preserves the old else-chain EXACTLY: a def with Tier 0 (unauthored) matched
            // none of the three branches and emitted no cost line, and it still emits none.
            if (primary > 0 && def.Tier >= 1) list.Add(new ResourceCost(BuildingTierChargeLane.For(def), primary));
            return list;
        }

        /// <summary>
        /// GameState-backed affordability for a city tier - the SAME wallet <see cref="TryUpgrade"/>
        /// charges, so the panel's gold affordance and the actual spend read ONE source of truth.
        /// (Replaces the old EconomyService.CanAfford check that read the divergent in-session
        /// Wood/Iron pool.) Public so BuildingUpgradeVM can light the tile honestly.
        /// </summary>
        public static bool CanAffordTier(BuildingTierDef def)
        {
            var state = GameStateService.Instance != null ? GameStateService.Instance.State : null;
            return def != null && state != null && state.Resources.Coins >= def.CostGold
                && ResourceLedger.CanAfford(TierCost(def));
        }

        /// <summary>
        /// Land a (charged) city-tier upgrade: write GameState.BuildingTiers, persist, and
        /// recompute the active GameModifiers. F8-51: shared by the instant path (flag OFF /
        /// no timer service) and the timer-completion path (CompletedUpgradeApplier), so both
        /// apply identically. Costs are NOT touched here — they were charged at commit.
        /// </summary>
        internal static void ApplyTier(string id, int targetTier)
        {
            var svc = GameStateService.Instance;
            var state = svc != null ? svc.State : null;
            if (state == null)
            {
                DeNelle.Core.Diagnostics.FlowTrace.Warn("BuildTimer",
                    $"ApplyTier '{id}' tier {targetTier}: no GameStateService — tier NOT applied");
                return;
            }
            if (state.BuildingTiers == null)
                state.BuildingTiers = new System.Collections.Generic.Dictionary<string, int>();
            state.BuildingTiers[id] = targetTier;
            svc.Save();
            ModifierService.Recompute();

            // Owner 2026-07-17 STRUCTURE-HP-per-tier: raise the LIVE building's max HP NOW (not next
            // scene load) so the tier's HP bonus is felt immediately. One generic path for all 6
            // upgradable buildings — data-driven off StructureHpMultFor; heals to full (an upgrade is
            // a completed construction) so the bigger bar is visible. Guarded so a bad entry logs+skips
            // and never zeroes a building's HP.
            ApplyStructureHp(id, targetTier);
        }

        /// <summary>
        /// Push the current-tier STRUCTURE-HP bonus onto every live <see cref="Building"/> whose
        /// <see cref="Building.UpgradeCatalogId"/> matches <paramref name="id"/>. Heals to full on the
        /// upgrade moment. Null-safe / Guard-wrapped; a no-match (building not spawned, or arcane-tower
        /// realised as a Tower rather than a Building) is a silent no-op — the spawn path (Building.Start)
        /// will apply it later. Traces each raise as [Flow:BuildHp] "<id> tier=N maxHp <old>-><new>".
        /// </summary>
        private static void ApplyStructureHp(string id, int targetTier)
        {
            Guard.Try("BuildHp", $"apply structure HP for '{id}' tier {targetTier}", () =>
            {
                float mult = ModifierService.StructureHpMultFor(id);
                var buildings = UnityEngine.Object.FindObjectsByType<Building>(FindObjectsSortMode.None);
                if (buildings == null) return;
                foreach (var b in buildings)
                {
                    if (b == null || !string.Equals(b.UpgradeCatalogId, id, StringComparison.Ordinal)) continue;
                    float oldMax = b.MaxHp;
                    b.ApplyStructureHpMultiplier(mult, healToFull: true);
                    FlowTrace.Step("BuildHp", $"{id} tier={targetTier} maxHp {oldMax:0}->{b.MaxHp:0}");
                }
            });
        }
    }
}

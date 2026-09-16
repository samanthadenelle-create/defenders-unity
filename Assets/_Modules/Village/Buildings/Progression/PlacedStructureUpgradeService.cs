// =============================================================================
// PlacedStructureUpgradeService — the ONE start path for a PLACED structure's
// level upgrade (tower / wall / container / mine / caravan).
// -----------------------------------------------------------------------------
// Assembly: DeNelle.Village   Namespace: DeNelle.Village.Buildings.Progression
//
// WHY THIS TYPE EXISTS (the defect it retires)
//   The charge -> busy-gate -> depth-gate -> StartUpgrade sequence lived INSIDE the
//   private BuildModeController.UpgradeSelected() and needed a live _selected. That
//   made it reachable from exactly ONE doorway: tapping the structure in build mode.
//   The Manage screen's Defense rows therefore could not start an upgrade at all —
//   they opened the enhancement panel with a BARE catalog id, which UpgradeFamily
//   Resolver classified as None, so the panel rendered "tier 0 of 0 - nothing left
//   to upgrade here" for a level-1-of-3 tower. Manage lied.
//
//   The fix is ONE behaviour with TWO callers, never two copies:
//     * BuildModeController.UpgradeSelected  (the in-world doorway) calls TryStart.
//     * BuildingUpgradeVM.UpgradeNext        (the panel doorway, opened from Manage
//                                             or from build mode) calls TryStart.
//   Duplicating the sequence would recreate the exact dual-authority bug
//   UpgradeFamilyResolver exists to prevent (see its header).
//
// CONTRACT
//   * Nothing is charged unless every gate passes (a refusal costs the player zero).
//   * The charged basket RIDES the job (WO-911 ruling Q1: cancel refunds 100%).
//   * The level LANDS at timer completion via CompletedUpgradeApplier — this service
//     never applies a level itself EXCEPT on the timers-off / service-raced-away path,
//     where it routes through the SAME CompletedUpgradeApplier.ApplyPlaced so the
//     instant path and the timer path are byte-identical in effect.
//   * Presentation-free: it returns a result + an ASCII message. Toasts, panel
//     re-shows and status lines belong to the callers (HP B2B layer separation).
// =============================================================================

using UnityEngine;
using DeNelle.Core.Catalog;
using DeNelle.Core.Diagnostics;
using DeNelle.Core.State;

namespace DeNelle.Village.Buildings.Progression
{
    /// <summary>What happened when a placed-structure upgrade was attempted.</summary>
    public enum PlacedUpgradeOutcome
    {
        /// <summary>A builder took it immediately; the level lands when the timer completes.</summary>
        Started = 0,
        /// <summary>Charged and enqueued on the Obsidian Builder channel; starts when a crew frees.</summary>
        Queued = 1,
        /// <summary>Timers off (or the service raced away): the level was applied on the spot.</summary>
        AppliedInstantly = 2,
        /// <summary>The id has no catalog row — nothing to price or upgrade.</summary>
        NoEntry = 3,
        /// <summary>Already at the catalog ceiling.</summary>
        Maxed = 4,
        /// <summary>The wallet cannot cover the next level's cost.</summary>
        Unaffordable = 5,
        /// <summary>This structure already has an in-flight build/upgrade job.</summary>
        Busy = 6,
        /// <summary>The whole Builders LINE is at its depth cap.</summary>
        LineFull = 7,
        /// <summary>The gates passed but the ledger declined the spend (nothing deducted).</summary>
        ChargeDeclined = 8,
        /// <summary>The key did not parse as "itemId@cellX_cellZ".</summary>
        BadKey = 9,
        /// <summary>The apply/start threw — see the Guard log. Charged; reported honestly.</summary>
        Threw = 10,
    }

    /// <summary>The outcome of one <see cref="PlacedStructureUpgradeService.TryStart"/> call.</summary>
    public struct PlacedUpgradeResult
    {
        /// <summary>Which branch was taken.</summary>
        public PlacedUpgradeOutcome Outcome;
        /// <summary>ASCII, player-facing one-liner for the toast / status row.</summary>
        public string Message;
        /// <summary>The level the upgrade targets (0 when nothing was attempted).</summary>
        public int TargetLevel;
        /// <summary>Whole seconds left on the started job (0 when queued / instant / refused).</summary>
        public int RemainingSeconds;
        /// <summary>The live structure the job belongs to, when one is spawned (may be null).</summary>
        public PlacedStructure Live;

        /// <summary>True when the player's tap actually moved the ladder (or a job for it).</summary>
        public bool Success =>
            Outcome == PlacedUpgradeOutcome.Started ||
            Outcome == PlacedUpgradeOutcome.Queued ||
            Outcome == PlacedUpgradeOutcome.AppliedInstantly;
    }

    /// <summary>
    /// The single authority on STARTING a placed structure's level upgrade. Keyed by the
    /// job key <see cref="PlacedUpgradeKey"/> composes, so it needs no selection and no
    /// scene object — the Manage drill-in and the in-world tap are the same call.
    /// </summary>
    public static class PlacedStructureUpgradeService
    {
        /// <summary>
        /// The catalog max level for an entry, clamped to the ONE named ceiling
        /// (<see cref="RepoProps.MaxStructureLevel"/>). This is the authority
        /// <c>BuildModeController.MaxLevelFor</c> delegates to, so the controller, the panel
        /// VM and the oracles can never disagree about where a ladder ends.
        /// </summary>
        public static int MaxLevelFor(CatalogEntry entry)
        {
            var repo = entry != null ? entry.repo : null;
            if (repo == null) return 1;
            return Mathf.Clamp(repo.maxLevel, 1, RepoProps.MaxStructureLevel);
        }

        /// <summary>True when this catalog row carries a per-instance level ladder.</summary>
        public static bool HasLevelLadder(CatalogEntry entry) => MaxLevelFor(entry) > 1;

        /// <summary>
        /// The persisted level of the placed structure at <paramref name="cellX"/>/<paramref name="cellZ"/>
        /// (1 when there is no record yet — a level-0 record is a pre-level save, not a locked
        /// building). Reads GameState.BaseLayout, the same per-town record the Manage rows read.
        /// </summary>
        public static int LevelOf(string itemId, int cellX, int cellZ)
        {
            var state = GameStateService.Instance != null ? GameStateService.Instance.State : null;
            var layout = state != null ? state.BaseLayout : null;
            if (layout == null || string.IsNullOrEmpty(itemId)) return 1;
            for (int i = 0; i < layout.Count; i++)
            {
                var p = layout[i];
                if (p.cellX == cellX && p.cellZ == cellZ
                    && string.Equals(p.itemId, itemId, System.StringComparison.Ordinal))
                    return Mathf.Max(1, p.level);
            }
            return 1;
        }

        /// <summary>Convenience: the persisted level behind a job key (1 when unknown).</summary>
        public static int LevelOfKey(string jobKey)
        {
            if (OwnedTownJobKey.TryParse(jobKey, out var baseId, out var instanceId))
            {
                var property = GameStateService.Instance?.State?.OwnedBase;
                var owned = property != null && property.baseId == baseId ? property.structures.Find(s => s.instanceId == instanceId && !s.retired) : null;
                return owned != null ? owned.placement.level : 1;
            }
            if (!PlacedUpgradeKey.TryParse(jobKey, out string itemId, out int cx, out int cz)) return 1;
            return LevelOf(itemId, cx, cz);
        }

        public static bool TryResolveKey(string key, out string itemId, out int cellX, out int cellZ)
        {
            itemId = null; cellX = cellZ = 0;
            if (!OwnedTownJobKey.TryParse(key, out var baseId, out var instanceId))
                return PlacedUpgradeKey.TryParse(key, out itemId, out cellX, out cellZ);
            var property = GameStateService.Instance?.State?.OwnedBase;
            var record = property != null && property.baseId == baseId ? property.structures.Find(s => s.instanceId == instanceId && !s.retired) : null;
            if (record == null) return false;
            itemId = record.placement.itemId; cellX = record.placement.cellX; cellZ = record.placement.cellZ;
            return true;
        }

        /// <summary>The next level's cost through the REAL resolver (L -> L+1). Null-safe.</summary>
        public static DeNelle.Core.Catalog.ResourceCost CostForNext(CatalogEntry entry, int fromLevel)
            => BuildModeController.UpgradeCostFor(entry, Mathf.Max(1, fromLevel));

        /// <summary>
        /// START (or queue) the next level for the placed structure named by
        /// <paramref name="jobKey"/>. Every gate runs BEFORE any charge. Never throws:
        /// the apply/start is Guarded and a throw is reported as
        /// <see cref="PlacedUpgradeOutcome.Threw"/>.
        /// </summary>
        public static PlacedUpgradeResult TryStart(string jobKey)
        {
            var result = new PlacedUpgradeResult { Outcome = PlacedUpgradeOutcome.BadKey };

            if (OwnedTownJobKey.TryParse(jobKey, out var baseId, out var instanceId))
            {
                var property = GameStateService.Instance?.State?.OwnedBase;
                var record = property != null && property.baseId == baseId ? property.structures.Find(s => s.instanceId == instanceId && !s.retired) : null;
                if (record == null) { result.Message = "That structure does not belong to this town."; return result; }
                var timer = BuildTimerService.Instance;
                if (timer == null) { result.Message = "The upgrade service is unavailable."; return result; }
                result.TargetLevel = record.placement.level + 1;
                if (!timer.TryStartOwnedTownUpgrade(instanceId, out var ownedJob, out var failure))
                { result.Outcome = PlacedUpgradeOutcome.ChargeDeclined; result.Message = failure; return result; }
                result.Outcome = !timer.IsBuilding(jobKey) ? PlacedUpgradeOutcome.AppliedInstantly :
                    ownedJob.HasValue && ownedJob.Value.StartMs > 0 ? PlacedUpgradeOutcome.Started : PlacedUpgradeOutcome.Queued;
                result.RemainingSeconds = (int)timer.RemainingSeconds(jobKey);
                result.Message = result.Outcome == PlacedUpgradeOutcome.AppliedInstantly ? "Upgrade completed." :
                    result.Outcome == PlacedUpgradeOutcome.Queued ? "Upgrade queued." : "Upgrade started.";
                return result;
            }

            if (!PlacedUpgradeKey.TryParse(jobKey, out string itemId, out int cx, out int cz))
            {
                result.Message = "That structure could not be identified.";
                FlowTrace.Fail("BuildUpgrade",
                    "placed upgrade REFUSED: key '" + (jobKey ?? "<null>") + "' is not itemId@cellX_cellZ");
                return result;
            }

            var cell = new Vector2Int(cx, cz);
            var entry = CatalogRegistry.Get(itemId);
            if (entry == null)
            {
                result.Outcome = PlacedUpgradeOutcome.NoEntry;
                result.Message = "That structure has no catalog entry.";
                FlowTrace.Fail("BuildUpgrade",
                    "placed upgrade REFUSED: '" + itemId + "' has no CatalogRegistry entry (key '" + jobKey + "')");
                return result;
            }

            int level = LevelOf(itemId, cx, cz);
            int maxLevel = MaxLevelFor(entry);
            result.Live = FindLive(itemId, cell);
            // The live object is the fresher authority when it is spawned (a same-session
            // upgrade lands on it first); fall back to the persisted record otherwise.
            if (result.Live != null) level = Mathf.Max(level, Mathf.Max(1, result.Live.level));

            FlowTrace.Step("BuildUpgrade", "placed upgrade IN: key='" + jobKey + "' lvl=" + level
                + "/" + maxLevel + " live=" + (result.Live != null));

            if (level >= maxLevel)
            {
                result.Outcome = PlacedUpgradeOutcome.Maxed;
                result.Message = "Max level reached.";
                FlowTrace.Step("BuildUpgrade", "'" + jobKey + "' already at max level (" + maxLevel + ") - no upgrade.");
                return result;
            }

            int newLevel = level + 1;
            result.TargetLevel = newLevel;

            var cost = CostForNext(entry, level);
            if (!BuildModeController.CanAfford(cost))
            {
                result.Outcome = PlacedUpgradeOutcome.Unaffordable;
                result.Message = BuildModeController.ShortfallMessage(cost);
                FlowTrace.Warn("BuildUpgrade", "'" + jobKey + "' UNAFFORDABLE at level " + newLevel + ".");
                return result;
            }

            // ── TIMER GATES (before any charge, so a rejection costs nothing) ──
            var timerSvc = DeNelle.Core.FeatureFlags.BuildTimers ? BuildTimerService.Instance : null;
            if (timerSvc != null)
            {
                if (timerSvc.IsBuilding(jobKey))
                {
                    int rem = (int)timerSvc.RemainingSeconds(jobKey);
                    result.Outcome = PlacedUpgradeOutcome.Busy;
                    result.RemainingSeconds = rem;
                    result.Message = "Under construction (" + rem + "s).";
                    FlowTrace.Warn("BuildUpgrade", "upgrade '" + jobKey + "' REJECTED (busy: " + rem + "s)");
                    return result;
                }

                // WO-911 (M1, ruling Q4) — DEPTH GATE. Distinct from the busy check above:
                // that one is "this structure is already working", this one is "the whole
                // Builders LINE is full".
                if (timerSvc.IsLineFull(DeNelle.Core.Jobs.ChannelId.Builder))
                {
                    result.Outcome = PlacedUpgradeOutcome.LineFull;
                    // WO-1045: quote the service's ONE sentence rather than re-composing it. This
                    // used to be a verbatim second copy; the panel's pre-tap greyed button now shows
                    // the SAME string, so what the player reads before the tap and what a refusal
                    // says after it cannot diverge.
                    result.Message = timerSvc.LineFullMessage(DeNelle.Core.Jobs.ChannelId.Builder);
                    FlowTrace.Warn("BuildUpgrade", "upgrade '" + jobKey + "' REFUSED - " + result.Message);
                    return result;
                }
            }

            // Charge ONLY after every gate passes (mirrors the place-charge rule).
            if (!BuildModeController.ChargeLedger(cost))
            {
                result.Outcome = PlacedUpgradeOutcome.ChargeDeclined;
                result.Message = BuildModeController.ShortfallMessage(cost);
                FlowTrace.Warn("BuildUpgrade", "upgrade '" + jobKey
                    + "' ABORTED: the ledger DECLINED the cost -- no level granted, nothing charged.");
                return result;
            }

            var outcome = PlacedUpgradeOutcome.Threw;
            string message = "Upgrade failed - see the log.";
            int remaining = 0;
            var live = result.Live;
            int target = newLevel;
            string key = jobKey;

            bool handled = Guard.Try("BuildUpgrade", "start/apply upgrade '" + key + "' -> level " + target, () =>
            {
                if (timerSvc != null)
                {
                    // WO-911 (M2): the basket just charged RIDES the job so a cancel refunds
                    // 100% of it (ruling Q1).
                    var job = timerSvc.StartUpgrade(key, target, BuildTimerService.ToJobCost(cost));
                    if (job != null)
                    {
                        if (live != null) UnderConstructionVisual.Attach(live, key);
                        bool queued = job.Value.StartMs <= 0;   // WO-773 — full Builder slot -> queued
                        remaining = (int)timerSvc.RemainingSeconds(key);
                        outcome = queued ? PlacedUpgradeOutcome.Queued : PlacedUpgradeOutcome.Started;
                        message = queued
                            ? "Queued for level " + target + " (builders busy)..."
                            : "Upgrading to level " + target + " (" + remaining + "s)...";
                        FlowTrace.Step("BuildUpgrade", "'" + key + "' level " + target + " timer "
                            + (queued ? "QUEUED" : "STARTED") + " (" + remaining + "s).");
                        return;
                    }
                }

                // Timers off (or the service raced away): land the level NOW through the SAME
                // completion apply the timer path uses, so a paid charge is never lost and the
                // persisted record + live visual move identically on both paths.
                CompletedUpgradeApplier.ApplyPlaced(key, target);
                outcome = PlacedUpgradeOutcome.AppliedInstantly;
                message = "Upgraded to level " + target + ".";
                FlowTrace.Step("BuildUpgrade", "'" + key + "' upgraded INSTANTLY to level " + target + ".");
            });

            if (!handled)
                FlowTrace.Fail("BuildUpgrade",
                    "upgrade apply for '" + key + "' THREW after charge - see Guard log.");

            result.Outcome = handled ? outcome : PlacedUpgradeOutcome.Threw;
            result.Message = message;
            result.RemainingSeconds = remaining;
            return result;
        }

        /// <summary>The spawned structure at a cell, or null when it is not in the scene.</summary>
        public static PlacedStructure FindLive(string itemId, Vector2Int cell)
        {
            var loader = BaseLayoutLoader.Instance;
            if (loader == null || string.IsNullOrEmpty(itemId)) return null;
            var loaded = loader.Loaded;
            if (loaded == null) return null;
            for (int i = 0; i < loaded.Count; i++)
            {
                var p = loaded[i];
                if (p != null && p.gridCell == cell
                    && string.Equals(p.itemId, itemId, System.StringComparison.Ordinal))
                    return p;
            }
            return null;
        }
    }
}

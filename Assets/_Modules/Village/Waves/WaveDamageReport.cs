// =============================================================================
// WaveDamageReport — post-wave structure-damage aggregation (F8-45 extension,
// owner directive 2026-07-11: "we need the damage report — if they did damage
// to collectors then those need to reduce economy").
// -----------------------------------------------------------------------------
// Assembly: DeNelle.Village   Namespace: DeNelle.Village
//
// Static, stateless MODEL-side helper: at wave clear it enumerates every
// damaged/destroyed structure the player cares about and emits a bounded,
// worst-first entry list. EndStateVM.FromWaveClear adapts the entries into
// SpoilRowVM rows (MVVM: the VM factory is the adapter; the View stays a dumb
// binder and reads no game state).
//
// Coverage (each through its EXISTING damage surface — no new damage model):
//   • WallSegment / Gate / Building — wrapped via RepairTarget (the proven
//     uniform repairable-structure view: DamageFraction + DisplayName), with
//     the IN-KIND MATERIALS repair cost (owner ruling 2026-07-11: damage
//     fraction x the structure's own catalog build cost in wood/iron/food;
//     destroyed = full build cost = the REBUILD price; crystals never charged)
//     read from the LIVE WallRepairController.CostFor when a controller exists
//     (the controller IS the one cost authority — data-driven off the catalog
//     rows; no controller => cost omitted, not faked).
//   • ResourceCollector — HpFraction damage + IsBroken + LastLootStolen (the
//     siege-raid steal), so the report shows the ECONOMY hit (accrual scales
//     with HP — see ResourceCollector.Accrue).
//   • Towers (Tower / DefenseTower / ArcaneTower) + HarvestSite — WO-672 closed
//     the old blind spot (they used to keep _hp private and Destroy(gameObject)
//     at 0 HP, so a post-wave scan saw neither a fraction nor the corpse): all
//     four now expose public HpFraction/IsBroken and persist as inoperable
//     BROKEN shells at 0 HP, so the scan reports damage AND the broken row.
//     EnemyOwned garrison turrets are skipped (an enemy asset, not a defence).
// =============================================================================

using System.Collections.Generic;
using UnityEngine;
using DeNelle.Core.Diagnostics;
using DeNelle.Village.Buildings.Progression;
using DeNelle.Village.World;

namespace DeNelle.Village
{
    /// <summary>
    /// Enumerates damaged/destroyed village structures at wave clear into a
    /// bounded, worst-first list for the wave-results damage report.
    /// </summary>
    public static class WaveDamageReport
    {
        /// <summary>Row cap for the compact wave-results banner (worst-first; truncation is logged, never silent).</summary>
        public const int MaxRows = 8;

        /// <summary>One damaged structure, normalised for the report.</summary>
        public sealed class Entry
        {
            /// <summary>Player-facing structure name ("North Gate", "Lumbermill", ...).</summary>
            public string Name;
            /// <summary>Normalised damage 0..1 (1 = destroyed). Sort key, worst-first.</summary>
            public float DamageFraction;
            /// <summary>True when the structure is fully destroyed / broken.</summary>
            public bool Destroyed;
            /// <summary>Collectors only: pending resources stolen when the collector broke (0 = none).</summary>
            public int LootStolen;
            /// <summary>
            /// In-kind materials cost of the repair (owner ruling 2026-07-11:
            /// damage fraction x the row's own catalog build cost; destroyed =
            /// full build cost = REBUILD). Only meaningful when <see cref="HasCost"/>.
            /// </summary>
            public DeNelle.Core.Catalog.ResourceCost RepairCost;
            /// <summary>False = no WallRepairController alive to price the row (cost omitted, not faked).</summary>
            public bool HasCost;
            /// <summary>True for ResourceCollector rows — the label carries the production hit.</summary>
            public bool IsCollector;

            // ── Diagnostics only (WO-1865). NEVER read by presentation. ──────────
            /// <summary>Which surface produced this row: "Collector" / "Tower" / "Repairable".
            /// Traced so a "destroyed" row can be attributed without re-deriving it.</summary>
            public string Source;
            /// <summary>The LIVE health fraction this row was classified from (1 = pristine).
            /// Traced because the owner's 2026-09-18 capture turned on the fact that a row can
            /// read Destroyed at hp=1.00 — a contradiction no count could ever show.</summary>
            public float LiveHpFraction;
            /// <summary>The LIVE broken flag this row was classified from (collectors/towers;
            /// false for RepairTarget rows, which classify off the damage fraction instead).</summary>
            public bool LiveBroken;
        }

        /// <summary>
        /// Scans the scene and returns at most <see cref="MaxRows"/> damaged-structure
        /// entries, worst-first. Never throws (Guard-wrapped); an empty list means a
        /// clean wave (the banner stays today's compact clean form).
        /// </summary>
        public static List<Entry> Collect()
        {
            return Guard.Try("WaveClear", "damage report aggregation", () =>
            {
                var all = new List<Entry>();

                // Wall / Gate / Building through the uniform RepairTarget view; the live
                // repair controller (when present) prices each row — the ONE cost seam.
                var repair = Object.FindFirstObjectByType<WallRepairController>();
                AddRepairables<WallSegment>(all, repair);
                AddRepairables<Gate>(all, repair);
                AddRepairables<Building>(all, repair);

                // Resource collectors — the economy layer (accrual scales with HP).
                // Owner ruling 2026-07-11: collectors are priced like everything else
                // (their Collector catalog row authors a real materials build cost).
                foreach (var c in ResourceCollectorRegistry.All)
                {
                    if (c == null) continue;
                    float frac = c.IsBroken ? 1f : 1f - c.HpFraction;
                    if (!c.IsBroken && frac <= 0.0001f) continue;   // pristine — no row
                    var def = ResourceBuildingProgression.Find(c.BuildingId);
                    all.Add(new Entry
                    {
                        Name = def != null && !string.IsNullOrEmpty(def.DisplayName)
                            ? def.DisplayName : c.BuildingId,
                        DamageFraction = frac,
                        Destroyed = c.IsBroken,
                        LootStolen = Mathf.RoundToInt(c.LastLootStolen),
                        IsCollector = true,
                        HasCost = repair != null,
                        RepairCost = repair != null ? repair.CostForStructure(c, frac) : default,
                        Source = "Collector",
                        LiveHpFraction = c.HpFraction,
                        LiveBroken = c.IsBroken,
                    });
                }

                // Towers + harvest sites (WO-672) — via the uniform HpFraction/IsBroken
                // surface each now exposes. A broken structure persists as a shell, so
                // the corpse IS scannable (Destroyed row, worst-first at fraction 1).
                foreach (var t in Object.FindObjectsByType<Tower>(FindObjectsSortMode.None))
                {
                    if (t == null) continue;
                    AddStructure(all,
                        t.Data != null && !string.IsNullOrEmpty(t.Data.towerName) ? t.Data.towerName : t.name,
                        t, t.HpFraction, t.IsBroken, repair);
                }
                foreach (var t in Object.FindObjectsByType<DefenseTower>(FindObjectsSortMode.None))
                {
                    // Garrison turrets are enemy assets — never a player damage-report row.
                    if (t == null || t.Allegiance != TowerAllegiance.PlayerOwned) continue;
                    AddStructure(all, t.name, t, t.HpFraction, t.IsBroken, repair);
                }
                foreach (var t in Object.FindObjectsByType<ArcaneTower>(FindObjectsSortMode.None))
                {
                    if (t == null) continue;
                    AddStructure(all, t.name, t, t.HpFraction, t.IsBroken, repair);
                }
                foreach (var h in Object.FindObjectsByType<HarvestSite>(FindObjectsSortMode.None))
                {
                    if (h == null || !h.IsClaimed) continue;   // unclaimed = not the player's yet
                    AddStructure(all, $"{h.ResourceType} Harvest Site", h, h.HpFraction, h.IsBroken, repair);
                }

                // Worst-first, bounded — truncation is LOGGED, never silent.
                all.Sort((a, b) => b.DamageFraction.CompareTo(a.DamageFraction));
                int destroyed = 0;
                for (int i = 0; i < all.Count; i++)
                    if (all[i].Destroyed) destroyed++;
                int damaged = all.Count - destroyed;
                int shown = Mathf.Min(MaxRows, all.Count);
                if (all.Count > MaxRows)
                    all.RemoveRange(MaxRows, all.Count - MaxRows);
                FlowTrace.Step("WaveClear",
                    $"damage report: {damaged} damaged, {destroyed} destroyed, {shown} rows shown" +
                    (damaged + destroyed > shown ? $" (of {damaged + destroyed} — worst-first cap)" : ""));

                // ── WO-1865: THE COUNT ABOVE IS NOT EVIDENCE. NAME EVERY ROW. ──────────
                // The owner's 2026-09-18 report ("the wave report said the Forge and Lumber Mill
                // were destroyed and they are standing") could not be triaged from the line above
                // at all: `0 damaged, 2 destroyed` names no structure, no surface and no health,
                // so the same count covers an honest fresh kill and a 31-minute-old zombie flag.
                // One line per row closes that: the ROW ITSELF now carries the live health it was
                // classified from, so a contradiction (Destroyed at hp=1.00 - the real defect,
                // ResourceCollector.SeedHpAfterLoad) is legible in one read instead of an hour of
                // static code reading. Wave-clear cadence, not a frame path, so the 3-arg form is
                // correct here (CLAUDE.md §12: the 4-arg Measure is for per-frame sites).
                for (int i = 0; i < all.Count; i++)
                {
                    var e = all[i];
                    string verdict = e.Destroyed ? "DESTROYED" : "damaged";
                    string contradiction = e.Destroyed && e.LiveHpFraction > 0.9999f
                        ? " ** CONTRADICTION: classified DESTROYED at FULL health - the structure's " +
                          "broken flag and its HP disagree, which is a persisted-state defect in the " +
                          "structure, NOT a defect in this report (WO-1865) **"
                        : string.Empty;
                    FlowTrace.Step("WaveClear",
                        $"  row {i + 1}/{all.Count} '{e.Name}' src={e.Source} {verdict} " +
                        $"damageFrac={e.DamageFraction:0.000} liveHp={e.LiveHpFraction:0.000} " +
                        $"liveBroken={e.LiveBroken} collector={e.IsCollector} priced={e.HasCost}" +
                        contradiction);
                }
                return all;
            }, new List<Entry>());
        }

        /// <summary>
        /// Adds one damaged/broken row from the uniform WO-672 surface
        /// (HpFraction + IsBroken — towers and harvest sites). A broken structure
        /// reads as fully destroyed (fraction 1, "Destroyed" per the existing row
        /// style); a pristine one adds no row. Priced (owner 2026-07-11) through
        /// WallRepairController.CostForStructure — the structure's own catalog
        /// row's materials scaled by damage; no controller => cost omitted.
        /// </summary>
        private static void AddStructure(List<Entry> into, string name, Component structure,
            float hpFraction, bool broken, WallRepairController repair)
        {
            float frac = broken ? 1f : 1f - Mathf.Clamp01(hpFraction);
            if (!broken && frac <= 0.0001f) return;   // pristine — no row
            into.Add(new Entry
            {
                Name = name,
                DamageFraction = frac,
                Destroyed = broken,
                HasCost = repair != null,
                RepairCost = repair != null ? repair.CostForStructure(structure, frac) : default,
                Source = "Tower",
                LiveHpFraction = Mathf.Clamp01(hpFraction),
                LiveBroken = broken,
            });
        }

        /// <summary>
        /// Adds one entry per damaged structure of type <typeparamref name="T"/>,
        /// wrapped through <see cref="RepairTarget.TryWrap"/> so name + damage come
        /// from the proven uniform surface (never re-branching on concrete types).
        /// </summary>
        private static void AddRepairables<T>(List<Entry> into, WallRepairController repair)
            where T : Component
        {
            foreach (var s in Object.FindObjectsByType<T>(FindObjectsSortMode.None))
            {
                var target = RepairTarget.TryWrap(s);
                if (target == null || !target.NeedsRepair) continue;
                float frac = target.DamageFraction;
                into.Add(new Entry
                {
                    Name = target.DisplayName,
                    DamageFraction = frac,
                    Destroyed = frac >= WallRepairController.DestroyedFraction,
                    HasCost = repair != null,
                    RepairCost = repair != null ? repair.CostFor(target) : default,
                    Source = "Repairable",
                    LiveHpFraction = 1f - Mathf.Clamp01(frac),
                    LiveBroken = false,
                });
            }
        }
    }
}

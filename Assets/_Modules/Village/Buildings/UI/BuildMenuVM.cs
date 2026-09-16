// =============================================================================
// BuildMenuVM — pure ViewModel for the village build menu (MVVM Silo C).
// -----------------------------------------------------------------------------
// Assembly: DeNelle.Village   Namespace: DeNelle.Village
//
// Strict-MVVM migration (UI_MVVM_MIGRATION_PLAN.md §1, Silo C): the three
// game-state seams BuildMenu read inline move HERE so the View is a dumb skin:
//   * the crystal balance (was GameStateService.State.Resources.Crystals) →
//     <see cref="Crystals"/> (IEconomy.Crystals is the SAME GameState-backed store);
//   * the placed-tower poll (was FindObjectsByType<Tower>()) → the shared
//     <see cref="Towers"/> (PlacedTowerListVM — the sanctioned resolution site);
//   * the Repair-Wall REFLECTION (AppDomain scan + FindAnyObjectByType + MethodInfo
//     invoke — the architect flagged it as NOT a sanctioned seam) → the typed
//     <see cref="RepairNearestWall"/> command on the real WallRepairController API.
//
// Owns the GameState.ResourcesChanged subscription for the open-menu live refresh
// and raises <see cref="Changed"/>. PURE C# (no uGUI types); the injectable ctor
// lets §2c tests drive Crystals + the tower list over fakes.
//
// WO-861 (owner Tier 0, 2026-08-02) — THE WALLET RULE FINALLY FOLLOWED THE VIEW HERE.
// BuildMenu still ran a FAKE wallet after the crystal read migrated: a private
// GetMaterialCount(id) returned the literals wood=20 / stone=5, and those literals were
// (a) SHOWN to the player as an on-hand balance, (b) what the Build button's afford gate
// compared against, and (c) never deducted -- so every tower priced in wood/stone was
// FREE. The View also carried its own 4-row TowerVariantDef balance table (crystal/wood/
// stone cost + build time + upgrade cost + DPS + HP), a SECOND cost authority divergent
// from the catalog. Both are DELETED. The economy surface now lives here:
//   * balances        -> <see cref="Wood"/>/<see cref="Iron"/>/<see cref="Stone"/>/
//                        <see cref="Crystals"/> + <see cref="MaterialCount"/>, all read
//                        straight off IEconomy (EconomyService == the ONE GameState-backed
//                        wallet since WO-842; there is no second material store --
//                        VillageInventory is the gear/consumable larder, not resources);
//   * priced towers   -> <see cref="TowerOptions"/>, sourced from CatalogRegistry rows via
//                        the REAL BuildModeController.CostFor (the same resolver Build Mode
//                        charges through), never a table in the View;
//   * affordability   -> <see cref="CanAfford"/> / <see cref="ShortfallFor"/>;
//   * the SPEND       -> <see cref="TrySpendBuild"/>, which HONOURS IEconomy.TrySpend's
//                        bool and returns false (with a reason) when the ledger declines.
//                        BuildMenu previously called BuildModeController.ChargeLedger, which
//                        DISCARDS TrySpend's return, then placed with prepaid:true -- a live
//                        free-tower path when the real ledger refused. Gone.
//
// 2026-08-04 (owner ruling: "fix the build menu to place the tower picked") -- THE PICK NOW
// REACHES THE PLACEMENT. WO-861 made the ROWS real (catalog ids, catalog names, catalog prices)
// but left the placement pinned to a const, `PlacedTowerResourcePath = "Towers/DevTower"`, that
// every build loaded: the player picked "Archer Tower" and a DevTower went up, with DevTower's
// 2s raise time printed on the screen. The selection was never carried past the price. It is
// carried now: <see cref="PlacedTowerDataFor"/> / <see cref="BuildSecondsFor"/> take the picked
// row's id and resolve THAT row's authored TowerData out of Resources/Towers, and the const is
// demoted to <see cref="FallbackTowerResourcePath"/> -- reached only by a catalog row nobody has
// authored a TowerData for, and TRACED when it is, because that case still raises a tower other
// than the one the player picked.
// =============================================================================

using System;
using System.Collections.Generic;
using DeNelle.Core.State;
using DeNelle.Core.UI.Mvvm;
using DeNelle.Village.UI;
using UnityEngine;
using CoreCost = DeNelle.Core.Catalog.ResourceCost;
using CatalogRegistry = DeNelle.Core.Catalog.CatalogRegistry;
using CatalogType = DeNelle.Core.Catalog.CatalogType;

namespace DeNelle.Village
{
    /// <summary>
    /// ViewModel for the build menu: exposes the live crystal balance, the placed-tower list
    /// (for the Upgrade screen), and a typed Repair-Wall command. Created fresh on Open and
    /// disposed on Close (the panel-VM lifecycle).
    /// </summary>
    public sealed class BuildMenuVM : IPanelViewModel, IDisposable
    {
        /// <summary>
        /// One priced tower row the Build-Tower screen can offer. Every field is CATALOG
        /// data (id / displayName / repo stats) with the cost resolved through the REAL
        /// <see cref="BuildModeController.CostFor"/> — the View never authors a number.
        /// </summary>
        public readonly struct TowerBuildOption
        {
            public readonly string   Id;
            public readonly string   DisplayName;
            public readonly CoreCost Cost;
            public readonly float    Damage;
            public readonly float    Range;

            public TowerBuildOption(string id, string displayName, CoreCost cost, float damage, float range)
            {
                Id = id;
                DisplayName = displayName;
                Cost = cost;
                Damage = damage;
                Range = range;
            }

            /// <summary>True for the default/no-row struct (nothing selectable).</summary>
            public bool IsEmpty => string.IsNullOrEmpty(Id);

            /// <summary>Sum of every cost slot — the cheap-first ordering key.</summary>
            public int CostTotal => Cost.wood + Cost.stone + Cost.iron + Cost.crystals;
        }

        /// <summary>How many catalog tower rows the Build-Tower radio offers (layout fits four).</summary>
        public const int MaxTowerOptions = 4;

        /// <summary>Resources folder holding the authored <see cref="DeNelle.Core.Data.TowerData"/>
        /// assets (ArcherTower / FrostTower / MageTower / DevTower).</summary>
        public const string TowerResourceFolder = "Towers";

        /// <summary>
        /// The TowerData raised when the picked catalog row has NO authored asset of its own.
        ///
        /// 2026-08-04 (owner ruling "fix the build menu to place the tower picked"): this path was
        /// a const named PlacedTowerResourcePath that EVERY menu-initiated placement loaded, so the
        /// player picked "Archer Tower" and a DevTower went up. It survived WO-861: that pass
        /// replaced the View's invented TowerVariantDef table with REAL catalog rows (real ids, real
        /// names, real prices) but left the placement side pinned to the one asset the pre-catalog
        /// menu had always raised — the stopgap the retired code documented as "the chosen element
        /// is remembered for when the variant system goes live". The rows went live; the placement
        /// did not follow. <see cref="PlacedTowerDataFor"/> now resolves the picked row's own asset
        /// and only lands here when the catalog offers a tower no TowerData has been authored for,
        /// so a placement can never resolve to nothing.
        /// </summary>
        public const string FallbackTowerResourcePath = "Towers/DevTower";

        private readonly IEconomy _economy;
        private readonly int _fallbackCrystals;
        private readonly Action _onClose;
        private readonly UnityEngine.Events.UnityAction _stateHandler;
        private readonly Action<ResourceSnapshot> _economyHandler;
        private WallRepairController _wallRepair;
        private bool _subscribed;
        private bool _disposed;

        private List<TowerBuildOption> _towerOptions;
        private DeNelle.Core.Data.TowerData[] _towerAssets;
        private bool _towerAssetsLoaded;
        private readonly Dictionary<string, DeNelle.Core.Data.TowerData> _placedTowerByRow
            = new Dictionary<string, DeNelle.Core.Data.TowerData>(StringComparer.OrdinalIgnoreCase);

        /// <summary>The shared placed-tower list VM (owns the FindObjectsByType&lt;Tower&gt; poll).</summary>
        public PlacedTowerListVM Towers { get; }

        /// <summary>Resolves EconomyService.Instance + WallRepairController + the tower list itself
        /// (the sole resolution site) and hooks the live ResourcesChanged feed.</summary>
        public static BuildMenuVM CreateDefault(Action onClose, int fallbackCrystals)
        {
            var vm = new BuildMenuVM(
                EconomyService.Instance,
                PlacedTowerListVM.CreateDefault(onClose),
                UnityEngine.Object.FindFirstObjectByType<WallRepairController>(),
                fallbackCrystals,
                onClose);
            vm.Subscribe();
            return vm;
        }

        public BuildMenuVM(IEconomy economy, PlacedTowerListVM towers,
            WallRepairController wallRepair, int fallbackCrystals, Action onClose)
        {
            _economy = economy;
            Towers = towers;
            _wallRepair = wallRepair;
            _fallbackCrystals = fallbackCrystals;
            _onClose = onClose;
            _stateHandler = Raise;
            _economyHandler = _ => Raise();
        }

        private void Subscribe()
        {
            if (_subscribed || _disposed) return;
            _subscribed = true;
            var gs = GameStateService.Instance;
            if (gs != null) gs.ResourcesChanged.AddListener(_stateHandler);
            // Also ride the economy's own change feed: a spend that lands on the
            // no-GameState FALLBACK pool (EditMode / headless boots) never raises
            // ResourcesChanged, and the open menu must still re-price.
            if (_economy != null) _economy.OnChanged += _economyHandler;
        }

        // ── IPanelViewModel ───────────────────────────────────────────────────
        public event Action Changed;
        public string Title => "Build";
        public void Close() => _onClose?.Invoke();

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (_subscribed)
            {
                var gs = GameStateService.Instance;
                if (gs != null && _stateHandler != null) gs.ResourcesChanged.RemoveListener(_stateHandler);
                if (_economy != null && _economyHandler != null) _economy.OnChanged -= _economyHandler;
            }
            Towers?.Dispose();
            Changed = null;
        }

        // ── Read-only data ────────────────────────────────────────────────────

        /// <summary>The live crystal balance the menu spends from (IEconomy.Crystals is the single
        /// GameState-backed store; falls back to the standalone-test value when no service).</summary>
        public int Crystals => _economy != null ? _economy.Crystals : _fallbackCrystals;

        /// <summary>Live Wood on hand (IEconomy — the ONE GameState-backed wallet, WO-842). 0 with no service.</summary>
        public int Wood => _economy != null ? _economy.Wood : 0;

        /// <summary>Live Iron on hand. The build menu labels this slot "Stone" historically —
        /// the catalog/ledger axis is IRON (the retired Stone axis became FOOD, DEF-121).</summary>
        public int Iron => _economy != null ? _economy.Iron : 0;

        /// <summary>Live Stone on hand.</summary>
        public int Stone => _economy != null ? _economy.Stone : 0;

        /// <summary>
        /// On-hand count of one resource axis, BY ID, straight off the live ledger. This
        /// replaces the deleted BuildMenu.GetMaterialCount stub, which returned the literals
        /// wood=20 / stone=5 and made every wood/stone-priced tower free (WO-861). There is
        /// no literal balance in this method and there must never be one again: an unknown id
        /// returns 0 (nothing is affordable) rather than a made-up number.
        /// </summary>
        public int MaterialCount(string resourceId)
        {
            if (string.IsNullOrEmpty(resourceId)) return 0;
            switch (resourceId.ToLowerInvariant())
            {
                case "wood":     return Wood;
                case "iron":
                case "stone":    return Iron;   // legacy UI label -> the real Iron axis
                case "food":     return Stone;
                case "crystal":
                case "crystals": return Crystals;
                default:         return 0;
            }
        }

        /// <summary>
        /// The tower rows this menu can price + build: every <see cref="CatalogType.Tower"/> entry
        /// in the registry, cheapest first, capped at <see cref="MaxTowerOptions"/>. Cost comes from
        /// the REAL <see cref="BuildModeController.SoftcappedCostFor"/> -- the same resolver the
        /// Build-Mode commit charges through -- so the menu can never show a price the ledger
        /// disagrees with. Empty (not fabricated) when the catalog has not been bootstrapped.
        ///
        /// WO-855 Phase 1: this used to read the raw <c>CostFor</c>, so the tower-spam softcap
        /// would have been invisible AND uncharged on this lane -- the Build Menu was a way to
        /// buy the 20th tower at the 1st tower's price. It reads SoftcappedCostFor and NOT
        /// <c>EffectiveCostFor</c> ON PURPOSE: EffectiveCostFor also zeroes the cost while a
        /// placement FREEBIE is live, but the freebie ledger is burned only by
        /// BuildModeController.Place -- this lane spends through TrySpendBuild and never touches
        /// FreeBuildsUsed, so pricing it freebie-aware would mint UNLIMITED free towers.
        /// </summary>
        public IReadOnlyList<TowerBuildOption> TowerOptions
        {
            get
            {
                if (_towerOptions == null) RefreshTowerOptions();
                return _towerOptions;
            }
        }

        /// <summary>Re-read the tower rows from the catalog (call after a late CatalogBootstrap).
        /// Drops the per-row TowerData cache with them: the rows a late bootstrap brings in carry
        /// different ids and display names, which is what the asset match is keyed on.</summary>
        public void RefreshTowerOptions()
        {
            _placedTowerByRow.Clear();
            var built = new List<TowerBuildOption>(MaxTowerOptions);
            var rows = CatalogRegistry.OfType(CatalogType.Tower);
            if (rows != null)
            {
                var all = new List<TowerBuildOption>(rows.Count);
                foreach (var e in rows)
                {
                    if (e == null || string.IsNullOrEmpty(e.id)) continue;
                    CoreCost cost = BuildModeController.SoftcappedCostFor(e);
                    string label = !string.IsNullOrEmpty(e.displayName) ? e.displayName : e.id;
                    float dmg = e.repo != null ? e.repo.damage : 0f;
                    float rng = e.repo != null ? e.repo.range  : 0f;
                    all.Add(new TowerBuildOption(e.id, label, cost, dmg, rng));
                }
                all.Sort((a, b) =>
                {
                    int byCost = a.CostTotal.CompareTo(b.CostTotal);
                    return byCost != 0 ? byCost : string.CompareOrdinal(a.Id, b.Id);
                });
                for (int i = 0; i < all.Count && built.Count < MaxTowerOptions; i++)
                    built.Add(all[i]);
            }
            _towerOptions = built;
        }

        /// <summary>The option matching <paramref name="id"/>, else the first (cheapest) row,
        /// else an empty option when the catalog offered nothing.</summary>
        public TowerBuildOption TowerOptionFor(string id)
        {
            var list = TowerOptions;
            if (list == null || list.Count == 0) return default;
            if (!string.IsNullOrEmpty(id))
                for (int i = 0; i < list.Count; i++)
                    if (string.Equals(list[i].Id, id, StringComparison.OrdinalIgnoreCase)) return list[i];
            return list[0];
        }

        // ── The picked row -> the TowerData that actually gets raised ─────────

        /// <summary>One authored TowerData asset offered to <see cref="ResolveTowerAssetName"/>:
        /// its file name and the display name authored inside it.</summary>
        public readonly struct TowerAssetCandidate
        {
            /// <summary>The .asset file name, e.g. "ArcherTower".</summary>
            public readonly string AssetName;
            /// <summary>The authored <c>TowerData.towerName</c>, e.g. "Archer Tower".</summary>
            public readonly string TowerName;

            public TowerAssetCandidate(string assetName, string towerName)
            {
                AssetName = assetName;
                TowerName = towerName;
            }
        }

        /// <summary>Shortest distinguishing token a catalog id may be matched on. "DevTower" reduces
        /// to "dev" (3), which stays UNDER this floor on purpose: the fallback asset is never allowed
        /// to win a token match and claim a row that named a different tower.</summary>
        private const int MinTowerToken = 4;

        /// <summary>
        /// Which authored TowerData asset is the catalog row's own tower, or null when none of them
        /// is (the caller then falls back to <see cref="FallbackTowerResourcePath"/>). Pure — it takes
        /// the candidate names rather than loading them, so the mapping is testable without Resources.
        ///
        /// Two passes, in order, so the strongest evidence always wins:
        ///   1. the row's display name IS the asset's authored tower name (or its file name), compared
        ///      with case + spacing + punctuation removed: "Archer Tower" == ArcherTower.towerName;
        ///   2. the row's catalog id or display name CONTAINS the asset's distinguishing token — the
        ///      file name with the word "tower" removed ("ArcherTower" -> "archer"), which is how the
        ///      id "tower_ground_archer" resolves to ArcherTower.
        /// </summary>
        public static string ResolveTowerAssetName(string catalogId, string displayName,
            IReadOnlyList<TowerAssetCandidate> candidates)
        {
            if (candidates == null || candidates.Count == 0) return null;

            string wantedName = Normalize(displayName);
            string wantedId   = Normalize(catalogId);

            if (wantedName.Length > 0)
                for (int i = 0; i < candidates.Count; i++)
                {
                    var c = candidates[i];
                    if (string.IsNullOrEmpty(c.AssetName)) continue;
                    if (wantedName == Normalize(c.TowerName) || wantedName == Normalize(c.AssetName))
                        return c.AssetName;
                }

            for (int i = 0; i < candidates.Count; i++)
            {
                var c = candidates[i];
                if (string.IsNullOrEmpty(c.AssetName)) continue;
                string token = DistinguishingToken(c.AssetName);
                if (token.Length < MinTowerToken) continue;
                if (wantedId.Contains(token) || wantedName.Contains(token)) return c.AssetName;
            }
            return null;
        }

        /// <summary>Lowercase letters and digits only — so "Archer Tower", "archer_tower" and
        /// "ArcherTower" all compare equal.</summary>
        private static string Normalize(string s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            var sb = new System.Text.StringBuilder(s.Length);
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (char.IsLetterOrDigit(c)) sb.Append(char.ToLowerInvariant(c));
            }
            return sb.ToString();
        }

        /// <summary>The asset name with the word "tower" stripped: "ArcherTower" -> "archer". That
        /// leftover is what identifies the tower inside a catalog id such as "tower_ground_archer",
        /// where the word "tower" itself carries no information.</summary>
        private static string DistinguishingToken(string assetName)
        {
            string n = Normalize(assetName);
            return n.Replace("tower", string.Empty);
        }

        /// <summary>The authored TowerData assets in <see cref="TowerResourceFolder"/>, loaded once.</summary>
        private DeNelle.Core.Data.TowerData[] TowerAssets()
        {
            if (!_towerAssetsLoaded)
            {
                _towerAssetsLoaded = true;
                _towerAssets = Resources.LoadAll<DeNelle.Core.Data.TowerData>(TowerResourceFolder);
            }
            return _towerAssets;
        }

        /// <summary>
        /// The TowerData a menu-initiated placement of <paramref name="towerId"/> raises — the PICKED
        /// row's own authored asset (DEF-76: TowerConstructionQueue times the raise off its
        /// <c>buildTime</c>, and Tower.Initialize reads its name, stats and upgrade ladder from it).
        /// Falls back to <see cref="FallbackTowerResourcePath"/> for a catalog row no TowerData has
        /// been authored for, so a placement never resolves to nothing; that fallback is TRACED,
        /// because it means the player is about to raise a tower other than the one they picked.
        /// Null only when even the fallback asset is missing (the View reports that instead of charging).
        /// </summary>
        public DeNelle.Core.Data.TowerData PlacedTowerDataFor(string towerId)
        {
            string key = towerId ?? string.Empty;
            if (_placedTowerByRow.TryGetValue(key, out var cached)) return cached;
            var resolved = ResolvePlacedTowerData(key);
            _placedTowerByRow[key] = resolved;
            return resolved;
        }

        private DeNelle.Core.Data.TowerData ResolvePlacedTowerData(string towerId)
        {
            // Nothing picked at all -> the fallback, with nothing to warn about: there is no row
            // whose tower we could have failed to raise.
            if (string.IsNullOrEmpty(towerId))
                return Resources.Load<DeNelle.Core.Data.TowerData>(FallbackTowerResourcePath);

            // The catalog row carries the display name the player tapped, which is the strongest
            // thing to match on. When the row is unknown (the catalog has not been bootstrapped)
            // the id ALONE is still matched, so a known tower id resolves its asset instead of
            // quietly becoming a DevTower.
            var option = TowerOptionFor(towerId);
            string rowId    = !option.IsEmpty ? option.Id : towerId;
            string rowLabel = !option.IsEmpty ? option.DisplayName : towerId;

            var assets = TowerAssets();
            if (assets != null && assets.Length > 0 && !string.IsNullOrEmpty(rowId))
            {
                var candidates = new List<TowerAssetCandidate>(assets.Length);
                for (int i = 0; i < assets.Length; i++)
                    if (assets[i] != null)
                        candidates.Add(new TowerAssetCandidate(assets[i].name, assets[i].towerName));

                string picked = ResolveTowerAssetName(rowId, rowLabel, candidates);
                if (!string.IsNullOrEmpty(picked))
                    for (int i = 0; i < assets.Length; i++)
                        if (assets[i] != null && string.Equals(assets[i].name, picked, StringComparison.Ordinal))
                            return assets[i];
            }

            var fallback = Resources.Load<DeNelle.Core.Data.TowerData>(FallbackTowerResourcePath);
            if (!string.IsNullOrEmpty(rowId))
                DeNelle.Core.Diagnostics.FlowTrace.Warn("Build",
                    $"BuildMenuVM: catalog tower '{rowId}' ({rowLabel}) has no authored TowerData in " +
                    $"Resources/{TowerResourceFolder} - falling back to {FallbackTowerResourcePath} " +
                    $"('{(fallback != null ? fallback.towerName : "<missing>")}'), so the raised tower is NOT the picked one.");
            return fallback;
        }

        /// <summary>Seconds the picked tower's body takes to raise (TowerConstructionQueue reads the
        /// SAME field off the SAME asset). 0 when the asset is missing — never a made-up duration.</summary>
        public int BuildSecondsFor(string towerId)
        {
            var d = PlacedTowerDataFor(towerId);
            return d != null ? Mathf.Max(0, Mathf.RoundToInt(d.buildTime)) : 0;
        }

        /// <summary>
        /// True when the LIVE ledger covers <paramref name="cost"/> on every axis. Routes through
        /// IEconomy.CanAfford (the same check the spend re-runs atomically); with no economy service
        /// it compares against this VM's own balances (crystals fall back, everything else is 0), so
        /// a service-less menu refuses a material cost instead of inventing stock.
        /// </summary>
        public bool CanAfford(CoreCost cost)
        {
            if (cost.IsZero) return true;
            if (_economy != null) return _economy.CanAfford(BuildModeController.ToEconomy(cost));
            return Crystals >= cost.crystals && Wood >= cost.wood
                   && Iron >= cost.iron && Stone >= cost.stone;
        }

        /// <summary>The concrete "Not enough &lt;Resource&gt; (N)" line for an unaffordable cost
        /// (the ONE shortfall message authority, shared with Build Mode).</summary>
        public string ShortfallFor(CoreCost cost) => BuildModeController.ShortfallMessage(cost);

        // ── Player-facing lines (WO-878: assembled HERE, never in the View) ───
        //
        // The View is layout + render only. Every string below used to be built inside
        // BuildMenu: the per-axis cost rows, the build-time line, the five-way upgrade
        // availability switch and the level/damage/range preview were all string-assembled in
        // presentation, which is how a View ends up "computing" what the VM owns. They read
        // the SAME live ledger (MaterialCount) and the SAME quote the CTA gates on, so the
        // number the player reads and the number the spend enforces can never diverge.

        /// <summary>
        /// One ASCII line stating what a build costs AND what is on hand, per axis:
        /// "Wood: 70 (+250), Iron: 40 (-12)". Zero axes are omitted. The +/- mark is the
        /// TEXT encoding of affordability - the owner is red/green colourblind, so a colour
        /// may only ever reinforce a state that is already spelled out.
        /// </summary>
        public string CostSummaryFor(CoreCost cost)
        {
            if (cost.IsZero) return "Costs nothing.";
            var sb = new System.Text.StringBuilder(64);
            AppendAxis(sb, "Crystals", cost.crystals, MaterialCount("crystals"));
            AppendAxis(sb, "Wood",     cost.wood,     MaterialCount("wood"));
            AppendAxis(sb, "Iron",     cost.iron,     MaterialCount("iron"));
            AppendAxis(sb, "Stone",    cost.stone,     MaterialCount("food"));
            return sb.Length > 0 ? sb.ToString() : "Costs nothing.";
        }

        private static void AppendAxis(System.Text.StringBuilder sb, string label, int required, int have)
        {
            if (required <= 0) return;
            if (sb.Length > 0) sb.Append(", ");
            sb.Append(label).Append(": ").Append(DeNelle.Core.UI.ElarionUi.CompactNumber(required))
              .Append(" (").Append(have >= required ? "+" : "-")
              .Append(DeNelle.Core.UI.ElarionUi.CompactNumber(have)).Append(')');
        }

        /// <summary>
        /// The second preview line on the Build screen: how long the PICKED tower's own asset
        /// takes to raise (the same field TowerConstructionQueue times the raise off) plus its
        /// catalog stats. Never a made-up duration - a missing asset simply drops that half.
        /// </summary>
        public string BuildDetailLineFor(TowerBuildOption option)
        {
            if (option.IsEmpty) return string.Empty;
            int seconds = BuildSecondsFor(option.Id);
            string time = seconds > 0 ? "Build time: " + FormatDuration(seconds) : string.Empty;
            string stats = $"dmg {option.Damage:0.#}, range {option.Range:0.#}m";
            return time.Length > 0 ? time + "  -  " + stats : stats;
        }

        /// <summary>The Build CTA's label: the verb when it can be paid for, the concrete reason
        /// when it cannot. The button STATES its state; the grey face only reinforces it.</summary>
        public string BuildCtaLabelFor(TowerBuildOption option)
        {
            if (option.IsEmpty) return "Build";
            return CanAfford(option.Cost) ? "Build" : ShortfallFor(option.Cost);
        }

        /// <summary>Line 1 of the upgrade preview - the price, or the reason there is no price.
        /// Five distinct states, because "cost == 0" collapsed a free upgrade, a maxed tower, a
        /// tower still being raised and an un-authored level into one developer-facing note.</summary>
        public string UpgradeCostLineFor(UpgradeQuote quote)
        {
            switch (quote.Availability)
            {
                case UpgradeAvailability.NotBuilt: return "The crew is still raising this tower.";
                case UpgradeAvailability.Maxed:    return $"Fully upgraded - Lvl {quote.Level} of {quote.MaxLevel}.";
                case UpgradeAvailability.Free:     return "Costs nothing - the next level is free.";
                case UpgradeAvailability.Unpriced: return "This tower cannot be upgraded any further.";
                default:                           return CostSummaryFor(quote.Cost);
            }
        }

        /// <summary>Line 2 of the upgrade preview - what the upgrade actually buys. Empty for a
        /// tower still under construction: it has no stats, and "0 dmg / 0m" is a fabricated
        /// reading rather than a real one.</summary>
        public string UpgradeStatLineFor(UpgradeQuote quote)
        {
            if (!quote.HasStats) return string.Empty;
            return quote.HasNextLevel
                ? $"Lvl {quote.Level} to {quote.Level + 1}:  dmg {quote.NowDamage:0.#} to {quote.NextDamage:0.#}," +
                  $"  range {quote.NowRange:0.#}m to {quote.NextRange:0.#}m"
                : $"Now:  dmg {quote.NowDamage:0.#},  range {quote.NowRange:0.#}m";
        }

        /// <summary>The Upgrade CTA's label - same colourblind law as the Build CTA.</summary>
        public string UpgradeCtaLabelFor(UpgradeQuote quote)
            => quote.CanUpgradeNow ? "Upgrade" : ShortfallFor(quote.Cost);

        /// <summary>Formats seconds as "Xm Ys" (or "Ys" under a minute). ASCII only.</summary>
        public static string FormatDuration(int seconds)
        {
            if (seconds < 0) seconds = 0;
            int m = seconds / 60, s = seconds % 60;
            return m > 0 ? $"{m}m {s}s" : $"{s}s";
        }

        // ── Commands ──────────────────────────────────────────────────────────

        /// <summary>
        /// THE build spend. Returns TRUE ONLY when the ledger actually deducted
        /// <paramref name="cost"/>; on false <paramref name="failure"/> carries the player-facing
        /// reason and NOTHING was mutated. The caller must not place on false.
        ///
        /// WO-861: BuildMenu used to call BuildModeController.ChargeLedger, which throws away
        /// IEconomy.TrySpend's bool, and then placed with prepaid:true regardless — so a DECLINED
        /// spend still produced a tower. Here TrySpend's return is the decision. A missing economy
        /// service REFUSES (it cannot prove a deduction); it never falls through to a silent yes.
        /// </summary>
        public bool TrySpendBuild(CoreCost cost, out string failure)
        {
            failure = null;
            if (cost.IsZero) return true;                      // catalog row with no price
            if (_economy == null)
            {
                failure = "Economy unavailable - the build was not charged, so nothing was placed.";
                Debug.LogWarning("[BuildMenuVM] TrySpendBuild refused: no IEconomy - a spend that cannot be proven is a spend we do not make.");
                return false;
            }
            var price = BuildModeController.ToEconomy(cost);
            if (!_economy.CanAfford(price)) { failure = ShortfallFor(cost); return false; }
            if (!_economy.TrySpend(price))                      // <- the bool is HONOURED
            {
                failure = ShortfallFor(cost);
                Debug.LogWarning("[BuildMenuVM] TrySpendBuild: the ledger DECLINED the spend (balance raced) - placement blocked.");
                return false;
            }
            Raise();
            return true;
        }

        /// <summary>
        /// The REAL price of the selected placed tower's next level. Tower.TryUpgrade and this quote
        /// both derive the basket from <c>Tower.UpgradePriceParts(NextUpgradeCost, ...)</c>, so the
        /// menu shows exactly what the transaction charges instead of the deleted variant table's
        /// invented upgrade numbers. (Until 2026-08-21 that basket was wood AND iron AND crystals,
        /// written out separately here and in Tower.cs; towers are REGULAR structures, so it is now
        /// wood + iron only per WO-947, computed in ONE place.) Zero when the tower
        /// is null / maxed / free / has no authored cost — callers that need to tell those apart
        /// must use <see cref="UpgradeQuoteFor"/>, which is the authority this delegates to.
        /// </summary>
        public CoreCost UpgradePriceFor(Tower tower) => UpgradeQuoteFor(tower).Cost;

        /// <summary>
        /// Why a placed tower can or cannot be upgraded right now. These are FIVE distinct states
        /// that a bare "price == 0" check collapsed into one, which is how a tower whose next level
        /// is authored at zero cost (it upgrades, for free) reported itself to the player as an
        /// un-authored tower.
        /// </summary>
        public enum UpgradeAvailability
        {
            /// <summary>Still being raised — it has no TowerData, so it has no level, stats or price.</summary>
            NotBuilt,
            /// <summary>Already at the last level.</summary>
            Maxed,
            /// <summary>The next level is authored, and authored at zero cost.</summary>
            Free,
            /// <summary>The next level is authored with a real price.</summary>
            Priced,
            /// <summary>The next level has no authored row at all — Tower.TryUpgrade refuses it.</summary>
            Unpriced,
        }

        /// <summary>Everything the upgrade screen needs about one placed tower's next level.</summary>
        public readonly struct UpgradeQuote
        {
            public readonly UpgradeAvailability Availability;
            public readonly int      Level;
            public readonly int      MaxLevel;
            public readonly CoreCost Cost;
            public readonly float    NowDamage;
            public readonly float    NowRange;
            public readonly float    NextDamage;
            public readonly float    NextRange;
            /// <summary>True when the live ledger covers <see cref="Cost"/> (always true when free).</summary>
            public readonly bool     Affordable;

            public UpgradeQuote(UpgradeAvailability availability, int level, int maxLevel, CoreCost cost,
                float nowDamage, float nowRange, float nextDamage, float nextRange, bool affordable)
            {
                Availability = availability;
                Level        = level;
                MaxLevel     = maxLevel;
                Cost         = cost;
                NowDamage    = nowDamage;
                NowRange     = nowRange;
                NextDamage   = nextDamage;
                NextRange    = nextRange;
                Affordable   = affordable;
            }

            /// <summary>True when a next level exists to advertise (so its stats can be shown).</summary>
            public bool HasNextLevel =>
                Availability == UpgradeAvailability.Priced || Availability == UpgradeAvailability.Free;

            /// <summary>True when tapping Upgrade would actually succeed.</summary>
            public bool CanUpgradeNow => HasNextLevel && Affordable;

            /// <summary>True when the tower is built, so its live stats are real numbers.</summary>
            public bool HasStats => Availability != UpgradeAvailability.NotBuilt;
        }

        /// <summary>
        /// Quote the selected tower's next level. Mirrors <see cref="Tower.TryUpgrade"/> exactly:
        /// the price is <c>upgrades[currentLevel].upgradeCost</c> charged on wood AND iron AND
        /// crystals, an un-authored row is refused, and a zero cost is a real (free) upgrade.
        ///
        /// The next level's stats are PROJECTED from the authored row rather than re-derived, so
        /// they carry the same village research + hero-talent modifiers Tower already folded into
        /// its live values. Tower.CurrentDamage is purely multiplicative in those modifiers, so the
        /// perk RATIO transfers exactly; Tower.CurrentRange adds a flat talent bonus after the
        /// multiplier, so the scaled perk DELTA transfers exactly.
        /// </summary>
        public UpgradeQuote UpgradeQuoteFor(Tower tower)
        {
            int max = Tower.MaxLevel;
            var data = tower != null ? tower.Data : null;
            if (data == null)
                return new UpgradeQuote(UpgradeAvailability.NotBuilt, 0, max, default, 0f, 0f, 0f, 0f, false);

            int level = tower.CurrentLevel;
            float nowDamage = tower.CurrentDamage;
            float nowRange  = tower.CurrentRange;

            if (level >= max)
                return new UpgradeQuote(UpgradeAvailability.Maxed, level, max, default,
                    nowDamage, nowRange, nowDamage, nowRange, false);

            var rows = data.upgrades;
            var nowRow  = RowAt(rows, level - 1);   // upgrades[level-1] == the level the tower is on
            var nextRow = RowAt(rows, level);       // upgrades[level]   == the level it would reach
            if (nextRow == null)
                return new UpgradeQuote(UpgradeAvailability.Unpriced, level, max, default,
                    nowDamage, nowRange, nowDamage, nowRange, false);

            int nowTier  = tower.EffectiveTier;
            int nextTier = level + 1;
            float perkDamageNow  = nowRow != null ? TowerPerkTable.EffectiveDamage(nowRow.damage, nowTier) : 0f;
            float perkDamageNext = TowerPerkTable.EffectiveDamage(nextRow.damage, nextTier);
            float nextDamage = perkDamageNow > 0.0001f
                ? nowDamage * (perkDamageNext / perkDamageNow)
                : perkDamageNext * ModifierService.Active.TowerDamageMult;

            float rangeMult     = ModifierService.Active.TowerRangeMult;
            float perkRangeNow  = nowRow != null ? TowerPerkTable.EffectiveRange(nowRow.range, nowTier) : 0f;
            float perkRangeNext = TowerPerkTable.EffectiveRange(nextRow.range, nextTier);
            float nextRange = nowRange + (perkRangeNext - perkRangeNow) * rangeMult;

            int each = nextRow.upgradeCost;
            if (each < 0 || each == int.MaxValue)
                return new UpgradeQuote(UpgradeAvailability.Unpriced, level, max, default,
                    nowDamage, nowRange, nextDamage, nextRange, false);
            if (each == 0)
                return new UpgradeQuote(UpgradeAvailability.Free, level, max, default,
                    nowDamage, nowRange, nextDamage, nextRange, true);

            // WO-947 basket law (owner ruling 2026-08-21): towers are REGULAR -> wood + iron, never
            // crystals. This line used to write the formula out a SECOND time as
            //     new CoreCost { wood = each, iron = each, crystals = each }
            // — a duplicate of Tower.TryUpgrade's basket. Two copies of one pricing rule is how the
            // three-resource bug survived, and fixing only the charge would have made the menu QUOTE
            // a price the transaction did not take. Both now call the one authority.
            Tower.UpgradePriceParts(each, out int quoteWood, out int quoteIron);
            var cost = new CoreCost { wood = quoteWood, iron = quoteIron };
            return new UpgradeQuote(UpgradeAvailability.Priced, level, max, cost,
                nowDamage, nowRange, nextDamage, nextRange, CanAfford(cost));
        }

        private static DeNelle.Core.Data.TowerUpgrade RowAt(DeNelle.Core.Data.TowerUpgrade[] rows, int index)
            => (rows != null && index >= 0 && index < rows.Length) ? rows[index] : null;

        /// <summary>Repair the most-damaged wall/structure through the sanctioned WallRepairController
        /// API (replaces the removed reflection seam). Surfaces the worst-damaged structure's repair
        /// prompt; no-op (warned) when the controller is absent.</summary>
        public void RepairNearestWall()
        {
            if (_wallRepair == null)
                _wallRepair = UnityEngine.Object.FindFirstObjectByType<WallRepairController>();
            if (_wallRepair != null) _wallRepair.SurfaceWorstRepair();
            else Debug.LogWarning("[BuildMenuVM] WallRepairController not in scene — Repair Wall no-op.");
        }

        private void Raise() { if (!_disposed) Changed?.Invoke(); }
    }
}

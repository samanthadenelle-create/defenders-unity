// =============================================================================
// BuildingUpgradeVM — the building ENHANCEMENT (perk-grid) panel's PURE ViewModel.
// -----------------------------------------------------------------------------
// Assembly: DeNelle.Village   Namespace: DeNelle.Village.Buildings.Progression
//
// Owner redo 2026-07-02: the panel is a Warcraft-3-style PERK GRID — every tier
// and research perk is a TILE the player taps to UNLOCK. VERBIAGE LAW: "Unlock
// perk" / "Enhancement" language only; the words "Upgrade Building" never appear.
//
// ALL grid STATE + LOGIC lives here, view-agnostic. Mirrors PartyShopVM:
//   * implements DeNelle.Core.UI.Mvvm.IPanelViewModel (Title / Changed / Close / Dispose)
//   * NO UnityEngine UI types; unit-testable without a scene (ARCHITECTURE_PRINCIPLES §2/§2c).
//   * the View binds it, re-renders on Changed, and routes taps back as commands;
//     the View NEVER reads game state (ui-mvvm-binding-seam rule).
//   * per-tile cost/effect strings are exposed via CostFor(id)/EffectFor(id); the
//     one-line concrete EFFECT comes from building-tiers.json (tier/perk "effect").
//
// TWO building families, decided EXACTLY like DialogueCommandBridge.CmdStructureStatus:
//   * CITY tier buildings  — BuildingTierCatalog.IsUpgradable(id): the WO-430 tier
//     ladder. Unlocking the next tier spends via BuildingUpgradeService.TryUpgrade.
//   * LEGACY resource buildings — ResourceBuildingProgression.IsResourceBuilding(id):
//     Farm/Lumbermill/Forge level curve via ResourceBuildingState.TryUpgrade(id).
//
// The model-side execute math is UNCHANGED — this VM only orchestrates + formats.
// =============================================================================

using System;
using System.Collections.Generic;
using DeNelle.Core.Diagnostics;
using DeNelle.Core.Jobs;
using DeNelle.Core.State;
using DeNelle.Core.UI.Mvvm;
// Disambiguate the two ResourceCost types in scope: the build-economy cost (Wood/Stone/
// Crystals — what IEconomy spends) vs. the legacy harvest cost (Resource/Amount). The
// unqualified `ResourceCost` in this Progression namespace is the harvest one.
using EcoCost = DeNelle.Village.ResourceCost;

namespace DeNelle.Village.Buildings.Progression
{
    /// <summary>
    /// WO-895 — the ONE action button's state on the building upgrade panel. The View is a pure
    /// reflection of this; it never invents a second state. Resolved from the SAME authorities the
    /// build/queue system uses (BuildTimerService active/pending jobs + the tier catalog gates +
    /// the GameState wallet), so the button can never disagree with the Obsidian queue.
    /// </summary>
    public enum UpgradeActionState
    {
        /// <summary>Affordable + not gated: tapping starts (or queues) the upgrade.</summary>
        Ready = 0,
        /// <summary>The wallet is short of the next tier's cost.</summary>
        MissingResources = 1,
        /// <summary>The job is in the Builder channel's PENDING queue, waiting for a free crew.</summary>
        Queued = 2,
        /// <summary>A crew is actively working this building's upgrade (live countdown).</summary>
        InProgress = 3,
        /// <summary>The next tier is behind the global Village Tier gate — the button raises THAT.</summary>
        VillageGated = 4,
        /// <summary>Every tier/level here is already owned.</summary>
        Maxed = 5,
        /// <summary>No ladder for this building (nothing to show).</summary>
        Unavailable = 6,
        /// <summary>
        /// WO-1045 — the Builders LINE is at its DEPTH cap, so no new work can be lined up at all.
        /// Affordable, ungated, nothing running here — and still refused. This state exists because
        /// its ABSENCE was the bug: every one of the three tap paths already refused a full line
        /// (<c>BuildingUpgradeService.TryUpgrade</c> :91, <c>ResourceBuildingState.TryUpgrade</c>
        /// :169, <c>PlacedStructureUpgradeService.TryStart</c> :219) while this enum had no way to
        /// say so, so <see cref="Ready"/> was returned and the View drew a bright, tappable, INERT
        /// button. The player read that as a broken game.
        /// <para>
        /// ⚠ This is the DEPTH axis (<c>BuildTimerConfig.queueDepthPerLine</c>, 5), NOT concurrency.
        /// A full CREW set (<c>freeBuildSlots</c>, 2) never reaches here — it queues, and shows as
        /// <see cref="Queued"/>. Never let this state's copy say "all builders are busy".
        /// </para>
        /// </summary>
        QueueFull = 7,
    }

    /// <summary>
    /// Pure ViewModel for the building enhancement (perk-grid) panel. Exposes every tier +
    /// research perk as <see cref="Perks"/> (one <see cref="ItemVM"/> tile each: owned=lit,
    /// next=gold affordance, locked=dim + requirement line) plus a status line. Raises
    /// <see cref="Changed"/> after each unlock and on any economy / modifier / level change.
    /// </summary>
    public sealed class BuildingUpgradeVM : IPanelViewModel, IDisposable
    {
        /// <summary>Row id for the synthetic "Unlock Village Tier" affordance (the Heart-of-Elarion
        /// tech-gate). <see cref="Select"/> on this id raises the global Village/Stronghold Tier that
        /// unlocks the WO-432 tier-2+ enhancements + research perks.
        /// <para>⚠ CORRECTED WO-1423: this said the row is "Injected at the TOP of every city/resource
        /// building's grid" so the player has "ONE place" to tap it. IT IS NOT DRAWN AS A TILE. Both of
        /// the View's render paths take <c>perk:</c> ids only (BuildingUpgradePanelMvvm.cs:817 and
        /// :1781), so <see cref="PrependVillageTierRow"/>'s row never reaches the screen. The LIVE
        /// control is the action band's VillageGated state (BuildingUpgradePanelMvvm.cs:1322-1338),
        /// which calls <c>Select(VillageTierRowId)</c> — and Select never consults <see cref="Perks"/>,
        /// so it works whether or not the row exists.</para></summary>
        public const string VillageTierRowId = "villagetier";

        /// <summary>Icon role key on each tier tile (the View maps it to art; no game state).</summary>
        public const string IconRoleTier = "tier";
        /// <summary>Icon role key on each research-perk tile (the View maps it to the perk's
        /// Resources/HudIcons/BuildingUpgrades/&lt;iconId&gt; sprite). WO-432.</summary>
        public const string IconRolePerk = "perk";

        private readonly string _buildingId;
        private readonly IEconomy _economy;
        private readonly Action _onClose;

        private readonly bool _isCity;
        private readonly bool _isResource;
        // The THIRD family (placed structures — towers, walls, containers, mines, caravans).
        // UpgradeFamily.PlacedStructure and the COMPLETION side have existed since the
        // dual-family fix; only this START/READ side was missing, which is why the Manage
        // screen's Defense rows rendered "tier 0 of 0 - nothing left to upgrade here" for a
        // level-1-of-3 tower (BuildUnknown's MaxTier = 0 -> the maxed card).
        private readonly bool _isPlaced;
        private readonly string _placedItemId;   // "" unless _isPlaced
        private readonly int _placedCellX;
        private readonly int _placedCellZ;

        private readonly Action<ResourceSnapshot> _ecoHandler;
        private readonly Action _modHandler;
        private readonly Action<string> _levelHandler;
        // The Obsidian queue's own change signal. The city/resource ladders get their refresh
        // from ModifierService/ResourceBuildingState, but a PLACED structure's level lands in
        // GameState.BaseLayout, which raises neither — without this the panel would sit on a
        // stale "Level 1 of 3" after its own upgrade completed while it was open.
        private readonly BuildTimerService _timerForEvents;
        private readonly Action _queueHandler;
        private bool _disposed;

        private readonly List<ItemVM> _perks = new List<ItemVM>();
        // Per-tile cost/effect strings, keyed by the tile's ItemVM.Id — the View reads them
        // through CostFor(id)/EffectFor(id) so it renders purely from VM data (no catalog re-pull).
        private readonly Dictionary<string, string> _costById = new Dictionary<string, string>();
        private readonly Dictionary<string, IReadOnlyList<DeNelle.Core.UI.CostPart>> _costPartsById = new Dictionary<string, IReadOnlyList<DeNelle.Core.UI.CostPart>>();
        private readonly Dictionary<string, string> _effectById = new Dictionary<string, string>();
        // WO-680 — per-tile "this is the building-upgrade KEY" sub-line ("UPGRADES FORGE TO
        // TIER 2") + which gate blocks the tile right now. Both composed HERE (data-driven,
        // never hardcoded in the View) so the View stays a dumb skin.
        private readonly Dictionary<string, string> _keyLineById = new Dictionary<string, string>();
        private readonly Dictionary<string, string> _gateById = new Dictionary<string, string>();

        /// <summary>WO-680 gate names for <see cref="GateFor"/> (the View seats the village
        /// Unlock CTA ONLY on a village-gated band; text composes from LockReason as ever).</summary>
        public const string GateVillage = "village";
        public const string GateBuildingTier = "building-tier";
        public const string GateCost = "cost";

        /// <summary>
        /// The View-side entry point (audit §3.1): resolves the economy handle + the default
        /// building HERE so the View never touches EconomyService/BuildingTierCatalog itself.
        /// A null/empty buildingId falls back to the first catalog building (generic open).
        /// </summary>
        public static BuildingUpgradeVM CreateDefault(string buildingId, Action onClose)
        {
            if (string.IsNullOrEmpty(buildingId)) buildingId = DefaultBuildingId();
            return new BuildingUpgradeVM(buildingId, EconomyService.Instance, onClose);
        }

        private static string DefaultBuildingId()
        {
            var all = BuildingTierCatalog.All;
            if (all != null && all.Count > 0 && all[0] != null) return all[0].Id;
            return ResourceBuildingProgression.FarmId;
        }

        public BuildingUpgradeVM(string buildingId, IEconomy economy, Action onClose)
        {
            // COLLECTOR ID RESOLUTION (collector bug fix): a placed collector opens this panel under
            // its catalog id ("collector_lumbermill"/"collector_farm"), but its tier/level ladder is
            // keyed on the bare collectorBuildingId ("lumbermill"/"farm"). Normalize HERE so EVERY
            // open path (BuildMode, CastleVendorNpc, HudKit) resolves to the right ladder -- without
            // it a collector id classified as neither city nor resource and rendered an empty grid.
            // Unchanged for every non-collector id (ResolveUpgradeId is a pass-through there).
            _buildingId = DeNelle.Core.Catalog.CatalogRegistry.ResolveUpgradeId(buildingId ?? "");
            _economy = economy;
            _onClose = onClose;

            // Decide the family through the SHARED resolver (city tiers win; else legacy) — the
            // SAME call CompletedUpgradeApplier makes at timer completion. These two sides used to
            // hand-derive the precedence in opposite orders, which started a dual-family upgrade
            // (farm/lumbermill/forge) on the city ladder and applied it to the resource one.
            var family = UpgradeFamilyResolver.Resolve(_buildingId);
            _isCity = family == UpgradeFamily.City;
            _isResource = family == UpgradeFamily.Resource;
            // A key that carries '@' but does NOT parse is not a placed structure — it falls
            // through to BuildUnknown rather than pretending to be a ladder it cannot read.
            string pid = "";
            int pcx = 0, pcz = 0;
            bool parsed = family == UpgradeFamily.PlacedStructure
                          && PlacedStructureUpgradeService.TryResolveKey(_buildingId, out pid, out pcx, out pcz);
            _isPlaced = parsed;
            _placedItemId = parsed ? pid : "";
            _placedCellX = parsed ? pcx : 0;
            _placedCellZ = parsed ? pcz : 0;

            // WO-842 (F8 2026-08-02, arcane-tower "TryUpgrade FALSE" with a full wallet):
            // these handlers used to call Raise() ONLY — the View re-rendered STALE tiles.
            // When a build timer completed while the panel sat open (ModifierService.
            // Recompute -> Changed), CurrentTier stayed stale, the owned tier still showed
            // as the gold "next" tile, and tapping it sent targetTier == the ALREADY-OWNED
            // tier into BuildingUpgradeService.TryUpgrade, whose next-tier guard silently
            // returned false — misreported as "can't afford" against 985k wood. Every
            // change now REBUILDS the grid (fresh CurrentTier + affordability) before Raise.
            if (_economy != null)
            {
                _ecoHandler = _ => { Rebuild(); Raise(); };
                _economy.OnChanged += _ecoHandler;
            }
            _modHandler = () => { Rebuild(); Raise(); };
            ModifierService.Changed += _modHandler;
            _levelHandler = _ => { Rebuild(); Raise(); };
            ResourceBuildingState.LevelChanged += _levelHandler;
            _timerForEvents = DeNelle.Core.FeatureFlags.BuildTimers ? BuildTimerService.Instance : null;
            if (_timerForEvents != null)
            {
                _queueHandler = () => { Rebuild(); Raise(); };
                _timerForEvents.QueueChanged += _queueHandler;
            }

            Rebuild();

            // §12 open trace: "[Flow:Upgrade] <building> grid: N perks, M owned, next=<id>".
            int owned = 0;
            string next = null;
            foreach (var p in _perks)
            {
                if (p.Equipped) owned++;
                else if (next == null && !p.Locked) next = p.Id;
            }
            FlowTrace.Step("Upgrade", _buildingId + " grid: " + _perks.Count + " perks, "
                + owned + " owned, next=" + (next ?? "none"));
        }

        // ── IPanelViewModel ───────────────────────────────────────────────────

        public event Action Changed;

        public string Title { get; private set; }

        /// <summary>The catalog building id this VM drives — lets the panel's CTA read the
        /// live timer gates (busy / no free crew) for a pre-tap reason (F8 2026-07-30).</summary>
        public string BuildingId => _buildingId;

        public void Close() => _onClose?.Invoke();

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (_economy != null && _ecoHandler != null) _economy.OnChanged -= _ecoHandler;
            if (_modHandler != null) ModifierService.Changed -= _modHandler;
            if (_levelHandler != null) ResourceBuildingState.LevelChanged -= _levelHandler;
            if (_timerForEvents != null && _queueHandler != null) _timerForEvents.QueueChanged -= _queueHandler;
            Changed = null;
        }

        // ── Read-only data the View renders ─────────────────────────────────────

        /// <summary>Current tier/level (0 = locked for a city building; 1.. for a resource building).</summary>
        public int CurrentTier { get; private set; }

        /// <summary>Highest authored tier/level.</summary>
        public int MaxTier { get; private set; }

        /// <summary>One TILE per tier + research perk (name + Affordable + owned/locked flags +
        /// LockReason requirement line). The View lays these out as the perk grid. Never null.</summary>
        public IReadOnlyList<ItemVM> Perks => _perks;

        /// <summary>Last action / hint line for the status row.</summary>
        public string Status { get; private set; }

        /// <summary>The cost string for a tile id (View renders it from here — no catalog re-pull).</summary>
        public string CostFor(string id) =>
            id != null && _costById.TryGetValue(id, out var c) ? c : "";

        public IReadOnlyList<DeNelle.Core.UI.CostPart> CostPartsFor(string id) =>
            id != null && _costPartsById.TryGetValue(id, out var c) ? c : Array.Empty<DeNelle.Core.UI.CostPart>();

        /// <summary>The one-line concrete EFFECT for a tile id ("Farm +25% yield" / "offline bucket
        /// holds more") — sourced from building-tiers.json "effect" (or derived for legacy levels).</summary>
        public string EffectFor(string id) =>
            id != null && _effectById.TryGetValue(id, out var e) ? e : "";

        /// <summary>WO-680 — the "this tile IS the building upgrade" sub-line for a tier/level tile
        /// ("UPGRADES FORGE TO TIER 2"), composed from the building's display name. Empty for perks.</summary>
        public string KeyLineFor(string id) =>
            id != null && _keyLineById.TryGetValue(id, out var k) ? k : "";

        /// <summary>WO-680 — which gate blocks a tile right now: <see cref="GateVillage"/> (Village
        /// Tier too low), <see cref="GateBuildingTier"/> (a lower tier tile is unowned),
        /// <see cref="GateCost"/> (next but unaffordable), or "" (owned / open). The View uses it to
        /// seat the village Unlock CTA ONLY where the VILLAGE gate is the blocker.</summary>
        public string GateFor(string id) =>
            id != null && _gateById.TryGetValue(id, out var g) ? g : "";

        // ── WO-841 live-countdown feed ──────────────────────────────────────────
        // The View's per-second "Under construction" tick reads THESE (MVVM: the VM
        // owns the queue-snapshot read; the View only renders). Same BuildTimerService
        // seam the tap-path mirrors above (F8-51) — pure reads, no state mutated, and
        // both return the idle shape (false/0) when the BuildTimers flag is off.

        /// <summary>True while a build/upgrade job is in flight for this building (WO-841).</summary>
        public bool UnderConstruction
        {
            get
            {
                var t = DeNelle.Core.FeatureFlags.BuildTimers ? BuildTimerService.Instance : null;
                return t != null && t.IsBuilding(_buildingId);
            }
        }

        /// <summary>Whole seconds left on this building's in-flight job; 0 when idle (WO-841).</summary>
        public int UnderConstructionSeconds
        {
            get
            {
                var t = DeNelle.Core.FeatureFlags.BuildTimers ? BuildTimerService.Instance : null;
                if (t == null || !t.IsBuilding(_buildingId)) return 0;
                return (int)t.RemainingSeconds(_buildingId);
            }
        }

        // ── WO-895 NEXT-UPGRADE surface (the "next only" card) ──────────────────
        // The panel no longer renders a 6-tier rail — it renders WHERE YOU ARE plus ONE
        // next-upgrade card. Everything that card needs is composed HERE (View stays a dumb
        // skin): the next tier's name, a plain description, its bonuses as SEPARATE lines,
        // its cost as STRUCTURED lines (so the short resource is flagged as TEXT, never a
        // red tint — colourblind law), and the ONE action button's state.

        /// <summary>One structured cost line for the next upgrade — amount, wallet balance, and
        /// whether the wallet is SHORT. The View renders the shortfall as words + a glyph; the
        /// owner is red/green colourblind, so a colour tint may never be the only signal.</summary>
        public struct UpgradeCostLine
        {
            public string ConceptId;
            /// <summary>ASCII resource name ("Wood", "Stone", "Crystals", "Iron", "Magic").</summary>
            public string Label;
            /// <summary>Amount the upgrade charges.</summary>
            public int Amount;
            /// <summary>Wallet balance for this resource right now.</summary>
            public int Have;
            /// <summary>True when <see cref="Have"/> &lt; <see cref="Amount"/>.</summary>
            public bool Short;
            /// <summary>How many more units are needed (0 when not short).</summary>
            public int Missing => Short ? Amount - Have : 0;
        }

        // ── WO-1391 (2026-09-05) — THE ONE AFFORDABILITY PREDICATE, and the sentence it feeds ──
        //
        // THE DEFECT THIS RETIRES (owner's Seeker capture 14-research-door-result.png, build 355952):
        // "Cathedral of Magic" showed "1280 Wood" against 4000 wood on the strip and STILL read
        // "Missing resources". PROVEN BY READING, not theorised: building-tiers.json 'arcane-tower'
        // tier 1 authors costGold 800 / costCrystal 1280 / costWood 0. ComposeNextCity emitted ONE
        // cost line (the PrimaryMaterialCost, 1280) and NO Gold line, while the predicate it trusted
        // - BuildingUpgradeService.CanAffordTier (:207-212) - ALSO requires state.Resources.Coins >=
        // CostGold, i.e. 706 >= 800, which is false. So the state was MissingResources, no cost
        // LINE was short (wood 4000 >= 1280), WorstShortMissing was 0, and BuildShortfallBand had
        // nothing to say beyond the bare "Missing resources". The strip and the predicate read the
        // SAME GameState (EconomyService.Coins => state.Resources.Coins; ResourceLedger.Balance =>
        // state.Wood / Resources.*) - the ledgers never disagreed. The Gold term was simply hidden.
        //
        // THE FIX: every term the tap will charge is a cost LINE (Gold included), and
        // _nextAffordable is DERIVED FROM THOSE LINES by AffordableFromLines - one predicate, one
        // ledger (GameState via ResourceLedger / EconomyService), so the sentence, the chips and
        // the button state cannot disagree again. The service predicate is still consulted as a
        // CROSS-CHECK and a disagreement is a traced Warn, never a silent divergence.

        /// <summary>WO-1391 — true when every cost line's HAVE covers its AMOUNT. Pure; the pin
        /// [shortfall-named] drives it with fixture lines.</summary>
        public static bool AffordableFromLines(IReadOnlyList<UpgradeCostLine> lines)
        {
            if (lines == null) return true;
            for (int i = 0; i < lines.Count; i++)
                if (lines[i].Short) return false;
            return true;
        }

        /// <summary>
        /// WO-1391 — the shortfall IN WORDS from the cost lines: "Short 94 Gold", or
        /// "Short 300 Iron, 120 Crystals" when several are short (largest gap first, then the
        /// panel's own left-to-right order). "" when nothing is short. ASCII only. Pure.
        /// </summary>
        public static string ShortfallSentence(IReadOnlyList<UpgradeCostLine> lines)
        {
            if (lines == null || lines.Count == 0) return "";
            int worstIdx = -1, worst = 0;
            for (int i = 0; i < lines.Count; i++)
                if (lines[i].Short && lines[i].Missing > worst) { worst = lines[i].Missing; worstIdx = i; }
            if (worstIdx < 0) return "";

            var sb = new System.Text.StringBuilder(48);
            sb.Append("Short ").Append(DeNelle.Core.UI.ElarionUi.CompactNumber(lines[worstIdx].Missing))
              .Append(' ').Append(Ascii(lines[worstIdx].Label ?? ""));
            for (int i = 0; i < lines.Count; i++)
            {
                if (i == worstIdx || !lines[i].Short || lines[i].Missing <= 0) continue;
                sb.Append(", ").Append(DeNelle.Core.UI.ElarionUi.CompactNumber(lines[i].Missing))
                  .Append(' ').Append(Ascii(lines[i].Label ?? ""));
            }
            return sb.ToString();
        }

        /// <summary>WO-1391 — the live shortfall sentence for the next upgrade ("" when affordable).
        /// The View puts THIS on the face - never a bare "Missing resources".</summary>
        public string NextShortfallSentence
        {
            get
            {
                // WO-1425 - a CEILING is not a shortfall. "Short 150 Wood" against a bank sitting
                // full at 3,000/3,000 reads exactly like a wall, and the owner hit precisely that:
                // "i couldnt tell Oh im missing gold, ohhh i need a foundry, whatever" (2026-09-06).
                // MEASURED: structures-catalog 'tower_ground_archer' upgradeCost[1] = 3150 wood
                // against MaxOf(Wood) = 3000 at lumberyard L1.
                // Checked HERE and NOT in ShortfallSentence(lines): that overload is pure and is
                // pinned by [shortfall-named] with fixture lines, so a capacity read there would
                // break it.
                string cap = CapBlockFrom(_nextCostLines);
                if (!string.IsNullOrEmpty(cap)) return cap;
                return ShortfallSentence(_nextCostLines);
            }
        }

        /// <summary>WO-1425 - the cap-block sentence for whichever cost line the town bank cannot
        /// hold, or "" when every line fits. "Magic" and "Gold" simply do not parse to a capped
        /// BankResource and are skipped; Crystals/Coins are uncapped by design and TownBankCapacity
        /// returns "" for them regardless.</summary>
        private static string CapBlockFrom(IReadOnlyList<UpgradeCostLine> lines)
        {
            if (lines == null) return "";
            for (int i = 0; i < lines.Count; i++)
            {
                if (!DeNelle.Core.Economy.TownBankCapacity.TryParseResource(lines[i].Label, out var r))
                    continue;
                string msg = DeNelle.Core.Economy.TownBankCapacity.StorageBlockMessage(r, lines[i].Amount);
                if (!string.IsNullOrEmpty(msg)) return Ascii(msg);
            }
            return "";
        }

        /// <summary>
        /// WO-1391 — the ONE action-state resolver, pure so a fixture can drive it. Same order the
        /// tap path checks: no ladder -> Unavailable; maxed -> Maxed; a job here -> Queued /
        /// InProgress; village gate -> VillageGated; cost -> MissingResources; full line -> QueueFull;
        /// else Ready. <see cref="ActionState"/> is a thin read of the live inputs into this.
        /// </summary>
        public static UpgradeActionState ResolveActionState(bool hasLadder, bool hasNext,
            bool jobHere, bool jobPending, int requiresVillageTier, int villageTierNow,
            bool affordable, bool lineFull)
        {
            if (!hasLadder) return UpgradeActionState.Unavailable;
            if (!hasNext) return UpgradeActionState.Maxed;
            if (jobHere) return jobPending ? UpgradeActionState.Queued : UpgradeActionState.InProgress;
            if (requiresVillageTier > villageTierNow) return UpgradeActionState.VillageGated;
            if (!affordable) return UpgradeActionState.MissingResources;
            if (lineFull) return UpgradeActionState.QueueFull;
            return UpgradeActionState.Ready;
        }

        private string _currentTierName = "";
        private string _nextTierName = "";
        private string _nextDescription = "";
        private readonly List<string> _nextBonuses = new List<string>();
        private readonly List<UpgradeCostLine> _nextCostLines = new List<UpgradeCostLine>();
        private bool _nextAffordable;
        private int _nextRequiresVillageTier;   // 0 = no village gate on the next tier
        private int _villageTierNow;

        /// <summary>"Tier" for a city building, "Level" for a legacy resource building. The card's
        /// copy is composed from this so ONE card serves both families (owner: a drill-in from the
        /// Manage screen lands here and must stand alone).</summary>
        public string TierWord => (_isResource || _isPlaced) ? "Level" : "Tier";

        /// <summary>
        /// The id the panel's 3D preview band resolves its MODEL from: the placed item id for a
        /// placed structure, else the ladder/building id. The View passes this straight to
        /// <see cref="StructurePreviewSource"/> — the View itself never touches the catalog.
        /// </summary>
        public string PreviewId => _isPlaced ? _placedItemId : _buildingId;

        /// <summary>The level whose model the preview should show (the CURRENT one — the band
        /// shows what stands in the town today, and the card beside it describes the next step).</summary>
        public int PreviewLevel => CurrentTier < 1 ? 1 : CurrentTier;

        /// <summary>True when there is a next tier/level to show (i.e. not maxed and a ladder exists).</summary>
        public bool HasNextUpgrade => MaxTier > 0 && CurrentTier < MaxTier;

        /// <summary>The tier/level number the next upgrade lands on (CurrentTier + 1).</summary>
        public int NextTier => CurrentTier + 1;

        /// <summary>Display name of the tier/level the player is on now ("" before the first tier).</summary>
        public string CurrentTierName => _currentTierName;

        /// <summary>Display name of the NEXT tier ("Drill Yard"). "" when maxed.</summary>
        public string NextTierName => _nextTierName;

        /// <summary>Plain sentence describing what the next upgrade does structurally
        /// ("Raises Barracks to Tier 3 of 6."). Never truncated by the View.</summary>
        public string NextDescription => _nextDescription;

        /// <summary>The next tier's bonuses as SEPARATE lines — each renders on its own row, so
        /// nothing is jammed into one clipped run-on string (WO-895 §1).</summary>
        public IReadOnlyList<string> NextBonuses => _nextBonuses;

        /// <summary>The next tier's cost, one structured line per resource.</summary>
        public IReadOnlyList<UpgradeCostLine> NextCostLines => _nextCostLines;

        /// <summary>True when the whole next-upgrade cost is affordable from the wallet the tap charges.</summary>
        public bool NextAffordable => _nextAffordable;

        // ── WO-1037 — the shortfall offer (READ-ONLY; no money path touched) ──────
        //
        // Composed HERE rather than in the View for the usual reason (View stays a dumb skin), and
        // resolved through ShortfallPackOffer — a legal reference, because it ships in the
        // RAIL-NEUTRAL DeNelle.Commerce assembly, which DeNelle.Village.asmdef lists. (The ban runs
        // the other way: Commerce/Wallet may not name Village, which is why PackStoreVM reaches
        // EconomyService by reflection.)
        //
        // ⚠ WO-1282 — DeNelle.Village.asmdef NO LONGER LISTS DeNelle.Wallet, and it must never
        // list it again: a Google Play artifact excludes the Solana rail whole, and that exclusion
        // is what GooglePlayPackagingGate.AssertSourceIsolation checks. The type is still spelled
        // DeNelle.Wallet.ShortfallOffer below because the NAMESPACE stayed put deliberately (see the
        // header of Assets/_Modules/Commerce/PackCatalog.cs) — the ASSEMBLY is what moved.
        //
        // ⛔ THIS RESOLVES A PackDef AND STOPS. There is no grant, no charge, no route into
        // ApplyPackContents from this property or from anything that reads it (WO-1037 §2 / WO-931).
        //
        // WHICH resource, when several are short: the LARGEST shortfall — the dominant blocker, the
        // one the player is furthest from clearing. WO-1037 §1 allows exactly ONE offer, so a choice
        // has to be made, and "the biggest gap" is the only one that is not arbitrary. Ties break to
        // the first cost line, which is the panel's own left-to-right order.

        /// <summary>
        /// The single relevant impulse-pack offer for this upgrade's shortfall, or an offer with
        /// <c>HasOffer == false</c> when the upgrade is affordable / the short resource has no pack
        /// family. Never surfaces on an affordable upgrade — <see cref="NextAffordable"/> short-
        /// circuits it, and ShortfallPackOffer.Resolve refuses a non-positive ask a second time.
        /// </summary>
        public DeNelle.Wallet.ShortfallOffer ShortfallOffer
        {
            get
            {
                if (_nextAffordable) return default;

                string label = null;
                int worst = 0;
                for (int i = 0; i < _nextCostLines.Count; i++)
                {
                    var line = _nextCostLines[i];
                    if (!line.Short) continue;
                    if (line.Missing > worst) { worst = line.Missing; label = line.Label; }
                }
                if (worst <= 0 || string.IsNullOrEmpty(label)) return default;

                return DeNelle.Wallet.ShortfallPackOffer.Resolve(label, worst);
            }
        }

        /// <summary>The resource word the player is furthest short of ("" when nothing is short).
        /// The panel names it in words — the shortfall is never signalled by colour alone.</summary>
        public string WorstShortLabel
        {
            get
            {
                string label = ""; int worst = 0;
                for (int i = 0; i < _nextCostLines.Count; i++)
                {
                    var line = _nextCostLines[i];
                    if (line.Short && line.Missing > worst) { worst = line.Missing; label = line.Label; }
                }
                return label;
            }
        }

        /// <summary>How many units of <see cref="WorstShortLabel"/> are still needed (0 when none).</summary>
        public int WorstShortMissing
        {
            get
            {
                int worst = 0;
                for (int i = 0; i < _nextCostLines.Count; i++)
                {
                    var line = _nextCostLines[i];
                    if (line.Short && line.Missing > worst) worst = line.Missing;
                }
                return worst;
            }
        }

        /// <summary>Village Tier required by the next tier (0 = ungated).</summary>
        public int NextRequiresVillageTier => _nextRequiresVillageTier;

        /// <summary>The Village Tier the player holds right now (the gate copy reads from this).</summary>
        public int VillageTierNow => _villageTierNow;

        /// <summary>
        /// WO-895 — the ONE action button's live state. Read from the REAL authorities in the same
        /// order the tap path checks them, so the button always states the true blocker:
        /// maxed -&gt; queue (pending = Queued, active = InProgress) -&gt; village gate -&gt; cost -&gt; Ready.
        /// A full crew set no longer blocks Ready: the tap QUEUES the job (Obsidian Builder channel).
        /// </summary>
        public UpgradeActionState ActionState
        {
            get
            {
                var t = DeNelle.Core.FeatureFlags.BuildTimers ? BuildTimerService.Instance : null;
                bool jobHere = t != null && t.IsBuilding(_buildingId);
                // WO-1045 — the DEPTH cap is THE LAST GATE, checked only when everything else passed
                // (affordable, ungated, nothing running here): a player who is also broke or
                // village-gated has a blocker they can act on sooner. ResolveActionState keeps that
                // order; this getter only feeds it the live inputs (WO-1391 made it pure so the
                // [shortfall-named] pin can prove Ready/MissingResources from fixture lines).
                bool lineFull = t != null && t.IsLineFull(ChannelId.Builder);
                return ResolveActionState(
                    hasLadder: _isCity || _isResource || _isPlaced,
                    hasNext: HasNextUpgrade,
                    jobHere: jobHere,
                    jobPending: jobHere && IsPendingInBuilderQueue(t),
                    requiresVillageTier: _nextRequiresVillageTier,
                    villageTierNow: _villageTierNow,
                    affordable: _nextAffordable,
                    lineFull: lineFull);
            }
        }

        // ── WO-1045 — the QUEUE-FULL surface (reason + offer) ────────────────────
        // The service already owned every word of this (BuildTimerService.LineFullMessage /
        // TryBuySlot's Echo-gate text). NOTHING here writes new refusal copy; it only routes the
        // service's own sentences to a View that previously consumed none of them.

        /// <summary>
        /// WO-1045 — why the action button is inert, in the SERVICE'S OWN WORDS, or "" when the
        /// button is not blocked by the queue. Today this is the depth-cap sentence
        /// (<see cref="BuildTimerService.LineFullMessage"/>) — the exact string a refusal would
        /// return, so the pre-tap explanation and the post-tap refusal cannot disagree.
        /// </summary>
        public string ActionBlockedReason
        {
            get
            {
                if (ActionState != UpgradeActionState.QueueFull) return "";
                var t = DeNelle.Core.FeatureFlags.BuildTimers ? BuildTimerService.Instance : null;
                return t != null ? Ascii(t.LineFullMessage(ChannelId.Builder)) : "";
            }
        }

        /// <summary>WO-1045 — items lined up on the Builders channel right now (active + pending).</summary>
        public int BuilderQueueDepth
        {
            get
            {
                var t = DeNelle.Core.FeatureFlags.BuildTimers ? BuildTimerService.Instance : null;
                return t != null ? t.QueueDepth(ChannelId.Builder) : 0;
            }
        }

        /// <summary>WO-1045 — the DEPTH cap on the Builders channel (queueDepthPerLine + bought slots).</summary>
        public int BuilderQueueLimit
        {
            get
            {
                var t = DeNelle.Core.FeatureFlags.BuildTimers ? BuildTimerService.Instance : null;
                return t != null ? t.QueueDepthLimit(ChannelId.Builder) : 0;
            }
        }

        /// <summary>
        /// WO-1045 — CONCURRENCY, shown beside <see cref="BuilderQueueDepth"/> so the two axes are
        /// visibly different numbers. Crews are how many jobs RUN AT ONCE (freeBuildSlots + bought);
        /// exhausting them queues rather than refuses, and never produces
        /// <see cref="UpgradeActionState.QueueFull"/>.
        /// </summary>
        public int BuilderCrewSlots
        {
            get
            {
                var t = DeNelle.Core.FeatureFlags.BuildTimers ? BuildTimerService.Instance : null;
                return t != null ? t.SlotCount(ChannelId.Builder) : 0;
            }
        }

        /// <summary>WO-1045 — crews working right now (0..<see cref="BuilderCrewSlots"/>).</summary>
        public int BuilderCrewsBusy
        {
            get
            {
                var t = DeNelle.Core.FeatureFlags.BuildTimers ? BuildTimerService.Instance : null;
                var active = t != null ? t.ActiveJobsOf(ChannelId.Builder) : null;
                return active != null ? active.Count : 0;
            }
        }

        /// <summary>
        /// WO-1045 — true when an extra queue slot may be OFFERED (the Echo gate passes and the sink
        /// is priced). Probed through <see cref="BuildTimerService.CanBuySlot"/>, which shares its
        /// gate text with <see cref="BuildTimerService.TryBuySlot"/>. Deliberately true even when the
        /// player is short of crystals: the offer stays visible and routes to the faucet.
        /// </summary>
        public bool CanBuyQueueSlot
        {
            get
            {
                var t = DeNelle.Core.FeatureFlags.BuildTimers ? BuildTimerService.Instance : null;
                return t != null && t.CanBuySlot(ChannelId.Builder, out _);
            }
        }

        /// <summary>
        /// WO-1045 — when <see cref="CanBuyQueueSlot"/> is false, the SERVICE'S sentence naming what
        /// unlocks the next slot ("Locked. Awaken a 3rd Echo..."). That is a real goal, not a wall —
        /// so it is shown instead of nothing. "" when a slot IS purchasable.
        /// </summary>
        public string QueueSlotLockReason
        {
            get
            {
                var t = DeNelle.Core.FeatureFlags.BuildTimers ? BuildTimerService.Instance : null;
                if (t == null) return "";
                return t.CanBuySlot(ChannelId.Builder, out string why) ? "" : Ascii(why ?? "");
            }
        }

        /// <summary>WO-1045 — crystal ask for the next Builder slot (0 when the sink is off).</summary>
        public int QueueSlotPrice
        {
            get
            {
                var t = DeNelle.Core.FeatureFlags.BuildTimers ? BuildTimerService.Instance : null;
                return t != null ? t.NextSlotPrice(ChannelId.Builder) : 0;
            }
        }

        /// <summary>
        /// WO-1045 — buy one extra Builder slot, which widens BOTH the crew pool and the line depth
        /// (<see cref="BuildTimerService.QueueDepthLimit"/> = authored + bought), so it genuinely
        /// unblocks a depth-capped queue.
        /// <para>
        /// ⛔ Routes through <see cref="BuildTimerService.TryBuySlot"/> and NEVER
        /// <c>GrantSlot</c>/<c>BuySlot</c>, which are the free grant and skip both the Echo gate and
        /// the crystal charge (WO-911 ruling Q6: Echoes unlock the RIGHT to buy, crystals complete
        /// it). A player-facing path into GrantSlot would hand out unlimited free parallelism.
        /// </para>
        /// </summary>
        /// <returns>True on purchase. On false, <see cref="Status"/> carries the service's reason —
        /// prefixed with <see cref="BuildTimerService.InsufficientCrystalsPrefix"/> when the player
        /// is merely broke, which is the caller's cue to route to the crystal store.</returns>
        public bool TryBuyQueueSlot()
        {
            var t = DeNelle.Core.FeatureFlags.BuildTimers ? BuildTimerService.Instance : null;
            if (t == null)
            {
                Status = "Build queues are not running right now.";
                FlowTrace.Warn("Upgrade", _buildingId
                    + " slot buy skipped: no BuildTimerService (ff.buildtimers off?).");
                Raise();
                return false;
            }

            bool ok = t.TryBuySlot(ChannelId.Builder, out string failure);
            Status = ok
                ? "Extra queue slot bought - the queue can take " + t.QueueDepthLimit(ChannelId.Builder) + " items."
                : Ascii(failure ?? "Could not buy a slot right now.");

            // §12 — the outcome AND the numbers that decided it, so a capture proves which of the
            // two gates (Echo entitlement vs crystals) refused, and never re-theorises it.
            FlowTrace.Step("Upgrade", _buildingId + " slot buy -> " + (ok ? "BOUGHT" : "REFUSED")
                + " (price=" + t.NextSlotPrice(ChannelId.Builder)
                + " depth=" + t.QueueDepth(ChannelId.Builder) + "/" + t.QueueDepthLimit(ChannelId.Builder)
                + " crews=" + t.SlotCount(ChannelId.Builder)
                + (ok ? "" : " reason='" + (failure ?? "") + "'") + ")");

            Rebuild();
            Raise();
            return ok;
        }

        /// <summary>Whole seconds left on this building's in-flight upgrade (0 when idle).
        /// While QUEUED this is the job's FULL duration — what it will take once a crew frees.</summary>
        public int ActionRemainingSeconds => UnderConstructionSeconds;

        /// <summary>0..1 progress of the in-flight job (0 while queued) — the button's fill bar
        /// reads this, so "in progress" is distinguishable by SHAPE, never by colour alone.</summary>
        public float ActionProgress
        {
            get
            {
                var t = DeNelle.Core.FeatureFlags.BuildTimers ? BuildTimerService.Instance : null;
                if (t == null || !t.IsBuilding(_buildingId)) return 0f;
                return t.Progress(_buildingId);
            }
        }

        /// <summary>True when this building's Builder job is waiting in the PENDING queue rather
        /// than running. The Obsidian queue is the sole authority — no second state is invented.</summary>
        private bool IsPendingInBuilderQueue(BuildTimerService t)
        {
            var pending = t.PendingJobsOf(ChannelId.Builder);
            if (pending == null) return false;
            for (int i = 0; i < pending.Count; i++)
                if (string.Equals(pending[i].StructureId, _buildingId, StringComparison.Ordinal)) return true;
            return false;
        }

        /// <summary>
        /// WO-895 — the ONE command behind the panel's one true button. Starts (or queues) the next
        /// tier/level. Routes to the SAME <see cref="UpgradeNext"/> execute path the old rail used,
        /// so there is exactly one spend/queue mechanism.
        /// </summary>
        public void StartNextUpgrade()
        {
            FlowTrace.Step("Upgrade", _buildingId + " StartNextUpgrade tapped: state=" + ActionState
                + " next=" + TierWord.ToLowerInvariant() + "-" + NextTier
                + " affordable=" + _nextAffordable);
            UpgradeNext();
        }

        /// <summary>Live wallet readout (View rebuilds its "Wood … Stone … Crystals" line from these).</summary>
        public int Wood     => _economy?.Wood ?? 0;
        public int Stone     => _economy?.Stone ?? 0;
        public int Iron     => _economy?.Iron ?? 0;
        public int Crystals => _economy?.Crystals ?? 0;
        public int Coins    => _economy?.Coins ?? 0;

        // ── Commands ────────────────────────────────────────────────────────────

        /// <summary>Unlock the NEXT tier/level (city -> BuildingUpgradeService, resource -> ResourceBuildingState).</summary>
        public void UpgradeNext()
        {
            if (_isCity)
            {
                // WO-842 stale-grid guard: if the LIVE tier moved since the last Rebuild
                // (timer completion / another surface upgraded), resync instead of sending
                // a mismatched targetTier into the service (whose next-tier guard returns a
                // bare false that read as "can't afford" — the captured F8). Honest + traced.
                int liveTier = ModifierService.TierOf(_buildingId);
                if (liveTier != CurrentTier)
                {
                    FlowTrace.Warn("Upgrade", _buildingId + " grid was STALE (vm tier=" + CurrentTier
                        + " live tier=" + liveTier + ") - resynced instead of a mismatched unlock (WO-842)");
                    Status = liveTier > CurrentTier
                        ? "Tier " + liveTier + " is already unlocked - grid refreshed."
                        : "Grid refreshed.";
                    Rebuild();
                    Raise();
                    return;
                }

                int next = CurrentTier + 1;
                if (next > MaxTier) { Status = "Every enhancement here is already unlocked."; Raise(); return; }

                // WO-432 TIER GATE — mirror BuildingUpgradeService's gate so a tier-locked
                // unlock reports an HONEST reason, not the generic "can't afford" (the service
                // returns a bare false for BOTH cases). Resources are fine; she's village-tier-gated.
                var nextDef = BuildingTierCatalog.TierOf(_buildingId, next);
                int villageTier = GameStateService.Instance?.State?.VillageTier ?? 0;
                if (nextDef != null)
                {
                    FlowTrace.Step("Upgrade", "unlock " + _buildingId + " tier=" + next
                        + " requiresVillageTier=" + nextDef.RequiresVillageTier
                        + " villageTier=" + villageTier
                        + " gated=" + (nextDef.RequiresVillageTier > villageTier));
                    if (nextDef.RequiresVillageTier > villageTier)
                    {
                        Status = "Requires Heart Level " + nextDef.RequiresVillageTier
                                 + " (you have " + villageTier + ").";
                        Raise();
                        return;
                    }
                }

                // F8-51 — mirror the service's timer gate for an HONEST status (the service
                // returns a bare false for busy AND unaffordable). Same one-timer service.
                var timerSvc = DeNelle.Core.FeatureFlags.BuildTimers ? BuildTimerService.Instance : null;
                if (timerSvc != null && timerSvc.IsBuilding(_buildingId))
                {
                    Status = "Under construction — " + (int)timerSvc.RemainingSeconds(_buildingId)
                             + "s until work here finishes.";
                    // F8 2026-07-30: these mirror gates emitted NO trace, so a busy refusal was
                    // invisible in the log while the resources-shaped Fail below got the blame.
                    FlowTrace.Step("Upgrade", _buildingId + " tier-" + next + " refused: under construction ("
                        + (int)timerSvc.RemainingSeconds(_buildingId) + "s left).");
                    Raise();
                    return;
                }
                // WO-895: a FULL crew set no longer REFUSES the tap — the Obsidian Builder
                // channel QUEUES the job and pulls it the moment a crew frees (the engine has
                // always done this; only these mirror gates rejected). The button then shows
                // "Queued" until the pull. No second state, no dead click.

                // WO-1045 — MIRROR THE DEPTH GATE, because the service reports it as a BARE FALSE
                // (BuildingUpgradeService.TryUpgrade :91) and the only status this method had for a
                // false was "You can't afford that yet." Against the owner's 49k wood that sentence
                // is not vague, it is WRONG — it sends the player to go and harvest more of a
                // resource they already have piles of. Say what actually refused, in the service's
                // own words. The button no longer reaches here (ActionState returns QueueFull), so
                // this is the guard for the other doorways into UpgradeNext (Yarn / Select()).
                if (timerSvc != null && timerSvc.IsLineFull(ChannelId.Builder))
                {
                    Status = Ascii(timerSvc.LineFullMessage(ChannelId.Builder));
                    FlowTrace.Warn("Upgrade", _buildingId + " tier-" + next + " refused: " + Status
                        + " (DEPTH cap, not affordability - wallet was not the blocker)");
                    Raise();
                    return;
                }

                bool ok = BuildingUpgradeService.TryUpgrade(_buildingId, next);
                if (ok)
                {
                    // F8-51 — with timers on, a successful buy STARTS the work; the tier
                    // lands at completion (the tile lights up then).
                    if (timerSvc != null && timerSvc.IsBuilding(_buildingId))
                    {
                        Status = IsPendingInBuilderQueue(timerSvc)
                            ? "Tier " + next + " queued - it starts when a builder frees up."
                            : "Tier " + next + " under construction - "
                              + (int)timerSvc.RemainingSeconds(_buildingId) + "s.";
                        FlowTrace.Step("Upgrade", _buildingId + " tier-" + next + " timer started");
                    }
                    else
                    {
                        Status = "Tier " + next + " unlocked.";
                        FlowTrace.Step("Upgrade", _buildingId + " unlocked tier-" + next);
                    }
                }
                else
                {
                    Status = "You can't afford that yet.";
                    // CLAUDE.md 12 - never a silent no-op again: the service logs wallet-vs-cost;
                    // mirror an honest [Flow:Upgrade] line here naming the tier cost that was short.
                    // SEVERITY: Capture, NOT Fail (2026-08-16) - the branch immediately above sets
                    // the player-facing "You can't afford that yet.", i.e. this is a NORMAL outcome.
                    // As a Fail it minted an F8 error + screenshot on every tap by a broke player.
                    if (nextDef != null)
                        FlowTrace.Capture("Upgrade", _buildingId + " tier-" + next
                            + " UpgradeNext -> TryUpgrade FALSE (needed W" + nextDef.CostWood
                            + "/G" + nextDef.CostGold
                            + ", have W" + ResourceLedger.Balance(HarvestResource.Wood)
                            + "/F" + ResourceLedger.Balance(HarvestResource.Stone)
                            + "/C" + ResourceLedger.Balance(HarvestResource.Crystals) + ")");
                }
                Rebuild();
                Raise();
                return;
            }

            if (_isResource)
            {
                // WO-1045 — same mirror as the city path above. ResourceBuildingState.TryUpgrade
                // (:169) collapses a full LINE into UpgradeResult.Insufficient, which this switch
                // renders as "You can't afford that yet." — a straight lie to a funded player. Catch
                // the depth cap before the call and quote the service's own sentence.
                {
                    var lineSvc = DeNelle.Core.FeatureFlags.BuildTimers ? BuildTimerService.Instance : null;
                    if (lineSvc != null && lineSvc.IsLineFull(ChannelId.Builder))
                    {
                        Status = Ascii(lineSvc.LineFullMessage(ChannelId.Builder));
                        FlowTrace.Warn("Upgrade", _buildingId + " level upgrade refused: " + Status
                            + " (DEPTH cap, not affordability)");
                        Raise();
                        return;
                    }
                }

                var result = ResourceBuildingState.TryUpgrade(_buildingId);
                switch (result)
                {
                    case UpgradeResult.Upgraded:
                        Status = "Level " + ResourceBuildingState.GetLevel(_buildingId) + " unlocked.";
                        FlowTrace.Step("Upgrade", _buildingId + " unlocked level-"
                            + ResourceBuildingState.GetLevel(_buildingId));
                        break;
                    case UpgradeResult.Insufficient: Status = "You can't afford that yet."; break;
                    case UpgradeResult.MaxLevel:     Status = "Every enhancement here is already unlocked."; break;
                    case UpgradeResult.NeedMagic:    Status = "That enhancement needs Magic to unlock."; break;
                    // F8-51 — timer states: work already running here (locked), or the buy
                    // just STARTED the work (level lands when the timer completes).
                    case UpgradeResult.InProgress:
                        Status = "Under construction — finish the current work first.";
                        break;
                    case UpgradeResult.Started:
                    {
                        var t = BuildTimerService.Instance;
                        int rem = t != null ? (int)t.RemainingSeconds(_buildingId) : 0;
                        // WO-895 — a full crew set QUEUES the job now (it no longer refuses),
                        // so say which of the two actually happened.
                        Status = t != null && IsPendingInBuilderQueue(t)
                            ? "Upgrade queued - it starts when a builder frees up."
                            : "Upgrade under construction - " + rem + "s.";
                        FlowTrace.Step("Upgrade", _buildingId + " level timer started");
                        break;
                    }
                    default:                         Status = "Nothing to unlock here."; break;
                }
                Rebuild();
                Raise();
                return;
            }

            if (_isPlaced)
            {
                // ONE start path, TWO doorways: this is the SAME service the in-world
                // BuildModeController.UpgradeSelected tap calls. The charge / busy gate / depth
                // gate / StartUpgrade sequence is never re-implemented here.
                var r = PlacedStructureUpgradeService.TryStart(_buildingId);
                Status = string.IsNullOrEmpty(r.Message) ? "Nothing to unlock here." : r.Message;
                FlowTrace.Step("Upgrade", _buildingId + " placed upgrade -> " + r.Outcome
                    + " (target level " + r.TargetLevel + ")");
                Rebuild();
                Raise();
                return;
            }

            Status = "Nothing to unlock here.";
            Raise();
        }

        /// <summary>Perk-tile tap. The ladder unlocks the NEXT tier only, so tapping the next
        /// tier tile unlocks it; tapping any other tier just re-states the hint (no model change).</summary>
        public void Select(string tierId)
        {
            if (string.IsNullOrEmpty(tierId)) return;
            // WO-481 — the "Unlock Village Tier" tile (the Heart-of-Elarion tech-gate). Raise the
            // global Village/Stronghold Tier with Crystals via VillageTierService; on success the
            // newly-gated tier-2+ enhancements + research perks open immediately (Rebuild repaints
            // the now-unlocked tiles because the per-tier RequiresVillageTier gate now passes).
            if (tierId == VillageTierRowId)
            {
                if (VillageTierService.IsMax)
                {
                    Status = "The Heart is already at its highest level.";
                    Raise();
                    return;
                }
                int crystals = _economy?.Crystals ?? 0;
                int cost = VillageTierService.NextCost();
                if (crystals < cost)
                {
                    Status = "Need " + DeNelle.Core.UI.ElarionUi.CompactNumber(cost)
                           + " Crystals to raise the Heart (you have "
                           + DeNelle.Core.UI.ElarionUi.CompactNumber(crystals) + ").";   // WO-697
                    Raise();
                    return;
                }
                if (VillageTierService.TryUpgrade())
                {
                    int n = VillageTierService.Current;
                    FlowTrace.Step("Upgrade", _buildingId + " unlocked villagetier-" + n
                        + " (unlocks tier-" + n + "+ enhancements).");
                    Status = "Heart Level raised to " + n + " — higher enhancements unlocked.";
                }
                else
                {
                    Status = "Couldn't raise the Heart right now.";
                }
                Rebuild();
                Raise();
                return;
            }
            // WO-432 — a research-perk tile ("perk:<perkId>"): START its research with Gold (Coins).
            // 2026-08-07: this no longer unlocks the perk on the spot. TryResearch charges the gold
            // and QUEUES a JobKind.BuildingResearch job on the Research channel; the perk lands when
            // the timer does. The status copy says "started", not "unlocked" — the old wording would
            // now be a straight lie about state the player can go and check on the Manage screen.
            if (tierId.StartsWith("perk:", StringComparison.Ordinal))
            {
                string perkId = tierId.Substring("perk:".Length);
                if (BuildingPerkService.TryResearch(_buildingId, perkId))
                {
                    Status = "Research started - check the Research queue.";
                    FlowTrace.Step("Upgrade", _buildingId + " started research on perk " + perkId);
                }
                else
                {
                    BuildingPerkService.CanResearch(_buildingId, perkId, out string why);
                    Status = !string.IsNullOrEmpty(why) ? why : "Can't unlock that perk yet.";
                }
                Rebuild();
                Raise();
                return;
            }
            if (tierId == NextTierId()) { UpgradeNext(); return; }
            Status = "Tap the gold tile to unlock the next enhancement.";
            Raise();
        }

        // ── Build the tiles + title/status (no Unity types) ─────────────────────

        private void Rebuild()
        {
            _perks.Clear();
            _costById.Clear();
            _costPartsById.Clear();
            _effectById.Clear();
            _keyLineById.Clear();
            _gateById.Clear();
            _nextBonuses.Clear();
            _nextCostLines.Clear();
            _currentTierName = "";
            _nextTierName = "";
            _nextDescription = "";
            _nextAffordable = false;
            _nextRequiresVillageTier = 0;
            _villageTierNow = GameStateService.Instance?.State?.VillageTier ?? 0;

            if (_isCity) BuildCity();
            else if (_isResource) BuildResource();
            else if (_isPlaced) BuildPlaced();
            else BuildUnknown();

            // WO-895 — compose the NEXT-upgrade card's content from whichever ladder just built.
            ComposeNextUpgrade();

            // A placed structure sits on the per-instance LEVEL ladder, which has no Village Tier
            // gate at all — so it gets no village-tier control (a tile that gates nothing here
            // would be a dead affordance).
            if (_isPlaced) return;

            // WO-481 — emit the synthetic Village-Tier ROW (cost/effect/gate data, not a drawn tile).
            // ⚠ CORRECTED WO-1423: this comment claimed the row is "the FIRST tile of every building's
            // grid" and "the SOLE caller of VillageTierService.TryUpgrade". Both were false and both
            // were load-bearing: the row is filtered out of every render path, and the tap that raises
            // the tier comes from the action band, not from a tile. What the row IS still for is named
            // on PrependVillageTierRow itself.
            PrependVillageTierRow();
        }

        /// <summary>
        /// Emits the synthetic "Unlock Village Tier" row at the top of <see cref="Perks"/> and its
        /// cost/effect/gate entries. Cost source = VillageTierService.NextCost() (Crystals, the premium
        /// progression currency; felt-tunable in VillageTierService).
        /// <para>⛔ THIS ROW IS NEVER DRAWN AS A TILE, and the old doc line ("The View renders it like
        /// any tile") was the lie WO-1423 caught. Both render paths take <c>perk:</c> ids only
        /// (BuildingUpgradePanelMvvm.cs:817 <c>HasSkills</c>, :1781 <c>RebuildSkills</c>), and this row's
        /// id is <c>villagetier</c>.</para>
        /// <para>It is KEPT, not deleted, because two live readers walk <see cref="Perks"/> WITHOUT the
        /// <c>perk:</c> filter: <c>DeriveSpendableCurrencies</c> (BuildingUpgradePanelMvvm.cs:758-770)
        /// scans each row's cost STRING for currency words, and this row's "N Crystals" is what puts
        /// the Crystal chip on the panel's currency strip — correct, since the village tier is bought
        /// with Crystals from this very panel; and the repaint hash (:544) folds every row in. Deleting
        /// the row would silently drop that chip.</para>
        /// <para>The LIVE control is the action band's VillageGated state
        /// (BuildingUpgradePanelMvvm.cs:1322-1338) -&gt; <see cref="Select"/>(<see cref="VillageTierRowId"/>)
        /// -&gt; VillageTierService.TryUpgrade. <see cref="Select"/> does not consult <see cref="Perks"/>,
        /// so the control does not depend on this row existing.</para>
        /// </summary>
        private void PrependVillageTierRow()
        {
            int cur = VillageTierService.Current;

            // WO-680 — at max Village Tier the village gate is OPEN: emit NO control at all.
            // The old "(Max)" tile fed the band-header CTA a "Maxed" cost string, and the View
            // composed the dead "Unlock Maxed" button from it. No tile -> no button, ever.
            if (VillageTierService.IsMax)
            {
                FlowTrace.Step("Upgrade", _buildingId + " villagetier gate=open (maxed at "
                    + cur + ") -> no unlock control emitted");
                return;
            }

            int cost = VillageTierService.NextCost();
            int crystals = _economy?.Crystals ?? 0;
            bool affordable = crystals >= cost;

            string name = "Raise Heart Level to " + (cur + 1);
            _costById[VillageTierRowId] = DeNelle.Core.UI.ElarionUi.CompactNumber(cost) + " Crystals";   // WO-697
            _costPartsById[VillageTierRowId] = DeNelle.Core.UI.CostFormat.Parts(new[] { ("crystal", "Crystals", cost) });
            _effectById[VillageTierRowId] = "Opens tier-" + (cur + 1) + " enhancements everywhere";
            _gateById[VillageTierRowId] = affordable ? "" : GateCost;

            // locked=false is INERT here - the row is never drawn, so nothing reads this flag to
            // decide tappability (corrected WO-1423; it used to say "so the control is always
            // tappable"). The action band owns the tap, and Select reports affordability honestly.
            _perks.Insert(0, new ItemVM(VillageTierRowId, name, IconRoleTier, VillageTierRowId, 0, "",
                                        affordable, rarity: null, equipped: false, locked: false));
        }

        private void BuildCity()
        {
            var def = BuildingTierCatalog.Find(_buildingId);
            Title = def != null && !string.IsNullOrEmpty(def.DisplayName) ? def.DisplayName : Titleize(_buildingId);
            CurrentTier = ModifierService.TierOf(_buildingId);
            MaxTier = BuildingTierCatalog.MaxTier(_buildingId);
            int villageTier = GameStateService.Instance?.State?.VillageTier ?? 0;

            // WO-680 §12 step-in: band-state resolution — the OUT line names WHICH gate blocks
            // the next tier (village vs building-tier vs cost) so "why is this locked" is one read.
            FlowTrace.Step("Upgrade", _buildingId + " band-state IN: curTier=" + CurrentTier
                + "/" + MaxTier + " villageTier=" + villageTier);
            string nextGate = "none";

            if (def != null && def.Tiers != null)
            {
                foreach (var t in def.Tiers)
                {
                    if (t == null) continue;
                    int tier = t.Tier;
                    bool isCurrent = tier <= CurrentTier;
                    bool isNext = tier == CurrentTier + 1;
                    bool gated = isNext && t.RequiresVillageTier > villageTier;
                    bool locked = tier > CurrentTier + 1 || gated;

                    var cost = new EcoCost {
                        Wood = t.Tier == 1 ? t.PrimaryMaterialCost : 0,
                        Stone = t.Tier == 2 ? t.PrimaryMaterialCost : 0,
                        Iron = t.Tier >= 3 ? t.PrimaryMaterialCost : 0,
                        Coins = t.CostGold
                    };
                    // Affordability reads the GameState-backed wallet the tap actually charges
                    // (building-upgrade blocker fix) -- NOT EconomyService's divergent in-session
                    // Wood/Iron pool. Mirrors BuildResource's ResourceLedger check below so the
                    // tile's gold affordance matches what BuildingUpgradeService.TryUpgrade will do.
                    bool affordable = isNext && !gated && BuildingUpgradeService.CanAffordTier(t);
                    string costStr = CostString(cost);

                    string lockReason = null;
                    if (gated) lockReason = "Requires Heart Level " + t.RequiresVillageTier;
                    // WO-680 gate copy names the ACTION: point at the PREVIOUS tier tile by its
                    // display name ("Unlock 'Ignite the Forge' to open Tier 2") so the thing to
                    // tap is unambiguous — composed from catalog data, never hardcoded.
                    else if (tier > CurrentTier + 1)
                        lockReason = "Unlock '" + TierDisplayName(def, tier - 1) + "' to open Tier " + tier;

                    string id = TierId(tier);
                    string name = "Tier " + tier + " — " + (!string.IsNullOrEmpty(t.Name) ? t.Name : ("Tier " + tier));
                    _costById[id] = isCurrent ? "Unlocked" : costStr;
                    if (!isCurrent) _costPartsById[id] = CostParts(cost);
                    _effectById[id] = t.Effect ?? "";
                    // WO-680 — mark the tier tile as THE building-upgrade key (View sub-line).
                    _keyLineById[id] = "UPGRADES " + Title.ToUpperInvariant() + " TO TIER " + tier;
                    _gateById[id] = isCurrent ? ""
                        : gated ? GateVillage
                        : tier > CurrentTier + 1 ? GateBuildingTier
                        : !affordable ? GateCost : "";
                    if (isNext) nextGate = string.IsNullOrEmpty(_gateById[id]) ? "open" : _gateById[id];
                    // Equipped flag carries "owned/lit"; Locked carries "not yet reachable" (+ reason).
                    _perks.Add(new ItemVM(id, name, IconRoleTier, id, 0, "", affordable,
                                          rarity: null, equipped: isCurrent, locked: locked,
                                          lockReason: lockReason));
                }
            }

            // WO-680 §12 step-out: the resolved blocker for the next reachable tier.
            FlowTrace.Step("Upgrade", _buildingId + " band-state OUT: next=tier-"
                + (CurrentTier + 1) + " gate=" + nextGate);

            // WO-432 RESEARCH TILES — every perk unlocked at a REACHED tier shows as a Gold-cost tile
            // in the grid (owned = lit; gate-not-met = dimmed + requirement; else gold affordance).
            // A perk-tile tap routes to BuildingPerkService via Select("perk:<id>"). Tiles are
            // View-agnostic (a future Blink prefab View binds the same data — WO-435).
            if (def != null && def.Tiers != null)
            {
                foreach (var t in def.Tiers)
                {
                    if (t == null || t.Perks == null) continue;
                    foreach (var p in t.Perks)
                    {
                        if (p == null || string.IsNullOrEmpty(p.Id)) continue;
                        bool owned = BuildingPerkService.IsOwned(_buildingId, p.Id);
                        // 2026-08-07 — research is TIMED, so a third state exists between "buyable"
                        // and "unlocked": in the Research queue. CanResearch already refuses an
                        // in-flight perk with "Research already in progress.", which would render it
                        // as a plain locked tile; reading the flag directly lets the cost cell say
                        // what is actually happening instead of still advertising a price.
                        bool researching = !owned && BuildingPerkService.IsResearching(_buildingId, p.Id);
                        string why = null;
                        bool can = !owned && BuildingPerkService.CanResearch(_buildingId, p.Id, out why);
                        bool affordable = can && (_economy == null || _economy.Coins >= p.GoldCost);
                        string rid = "perk:" + p.Id;
                        // Signature marker is ASCII "*" not "★" (tofu fix 2026-07-02): U+2605 is
                        // in NO project SDF font (scanned — zero m_Unicode:9733 hits), so ★
                        // rendered as a box in builds. A VM emits strings (no procedural Image
                        // like StarRatingRow/EndStateView), so the font-safe ASCII star it is.
                        string pname = (p.IsSignature ? "* " : "") + (!string.IsNullOrEmpty(p.Name) ? p.Name : p.Id);
                        _costById[rid] = owned ? "Unlocked"
                            : researching ? "Researching"
                            : (DeNelle.Core.UI.ElarionUi.CompactNumber(p.GoldCost) + " Gold");   // WO-697
                        if (!owned && !researching)
                            _costPartsById[rid] = DeNelle.Core.UI.CostFormat.Parts(new[] { ("gold", "Gold", p.GoldCost) });
                        _effectById[rid] = p.Effect ?? "";
                        string iconKey = string.IsNullOrEmpty(p.IconId) ? p.Id : p.IconId;
                        bool perkLocked = !owned && !can;
                        // WO-680 — the fallback gate copy names the tier TILE, not a bare number.
                        _perks.Add(new ItemVM(rid, pname, IconRolePerk, iconKey, 0, "", affordable,
                                              rarity: null, equipped: owned, locked: perkLocked,
                                              lockReason: perkLocked && !string.IsNullOrEmpty(why)
                                                  ? why : (perkLocked
                                                      ? "Unlock '" + TierDisplayName(def, t.Tier) + "' first" : null)));
                    }
                }
            }

            if (string.IsNullOrEmpty(Status))
                Status = CurrentTier >= MaxTier
                    ? "Every enhancement here is unlocked."
                    : "Tap the gold tile to unlock the next enhancement.";
        }

        private void BuildResource()
        {
            var def = ResourceBuildingProgression.Find(_buildingId);
            Title = def != null && !string.IsNullOrEmpty(def.DisplayName) ? def.DisplayName : Titleize(_buildingId);
            CurrentTier = ResourceBuildingState.GetLevel(_buildingId);
            MaxTier = def != null ? def.MaxLevel : CurrentTier;

            // WO-680 §12 step-in/out — mirror BuildCity's band-state trace for the level ladder
            // (no village gate here; the blockers are building-tier order and cost only).
            FlowTrace.Step("Upgrade", _buildingId + " band-state IN: curLevel=" + CurrentTier
                + "/" + MaxTier);
            string nextGate = "none";

            if (def != null && def.Levels != null)
            {
                for (int i = 0; i < def.Levels.Length; i++)
                {
                    var lvl = def.Levels[i];
                    if (lvl == null) continue;
                    int level = lvl.Level;
                    bool isCurrent = level <= CurrentTier;
                    bool isNext = level == CurrentTier + 1;
                    bool locked = level > CurrentTier + 1;

                    // A resource tile's cost is the cost FROM the previous level (the cost the
                    // player pays to reach THIS level). Level 1 is owned by default (no cost).
                    string costStr;
                    bool affordable = false;
                    if (level <= 1)
                    {
                        costStr = "Base";
                    }
                    else
                    {
                        var prev = def.LevelDef(level - 1);
                        costStr = prev != null ? ResourceCostString(prev.UpgradeCost, prev.MagicCost) : "";
                        if (isNext && prev != null)
                            affordable = ResourceLedger.CanAfford(prev.UpgradeCost)
                                         && (prev.MagicCost <= 0 || ResourceLedger.MagicBalance() >= prev.MagicCost);
                    }

                    string id = TierId(level);
                    string name = "Level " + level;
                    _costById[id] = isCurrent ? "Unlocked" : costStr;
                    if (!isCurrent && level > 1)
                    {
                        var prevCost = def.LevelDef(level - 1);
                        if (prevCost != null) _costPartsById[id] = ResourceCostParts(prevCost.UpgradeCost, prevCost.MagicCost);
                    }
                    // Legacy levels have no authored effect string — derive the concrete yield line
                    // ("+6 Stone per tick") from the level def, the owner's "Farm +25% yield" shape.
                    _effectById[id] = "+" + lvl.YieldPerTick + " "
                                      + ResourceBuildingProgression.LabelFor(lvl.Yields) + " per tick";
                    // WO-680 — the level tile is the building-upgrade key too (level 1 is owned
                    // by default, so no key line there); gate mirrors BuildCity minus the village gate.
                    if (level > 1)
                        _keyLineById[id] = "UPGRADES " + Title.ToUpperInvariant() + " TO LEVEL " + level;
                    _gateById[id] = isCurrent ? ""
                        : locked ? GateBuildingTier
                        : !affordable ? GateCost : "";
                    if (isNext) nextGate = string.IsNullOrEmpty(_gateById[id]) ? "open" : _gateById[id];
                    _perks.Add(new ItemVM(id, name, IconRoleTier, id, 0, "", affordable,
                                          rarity: null, equipped: isCurrent, locked: locked,
                                          lockReason: locked
                                              ? "Unlock 'Level " + (level - 1) + "' to open Level " + level : null));
                }
            }

            // WO-680 §12 step-out: the resolved blocker for the next reachable level.
            FlowTrace.Step("Upgrade", _buildingId + " band-state OUT: next=level-"
                + (CurrentTier + 1) + " gate=" + nextGate);

            if (string.IsNullOrEmpty(Status))
                Status = ResourceBuildingState.IsMaxLevel(_buildingId)
                    ? "Every enhancement here is unlocked."
                    : "Tap the gold tile to unlock the next enhancement.";
        }

        /// <summary>
        /// The THIRD ladder: a PLACED structure ("itemId@cellX_cellZ") — tower, wall, storage
        /// container, crystal mine, healing caravan. Level from the persisted BaseLayout record,
        /// ceiling from repo.maxLevel, cost from the SAME BuildModeController.UpgradeCostFor the
        /// in-world tap charges. No new cost math exists here and none may be added: a second
        /// price would be the dual-authority defect all over again.
        /// </summary>
        private void BuildPlaced()
        {
            var entry = DeNelle.Core.Catalog.CatalogRegistry.Get(_placedItemId);
            Title = entry != null && !string.IsNullOrEmpty(entry.displayName)
                ? entry.displayName : Titleize(_placedItemId);
            CurrentTier = PlacedStructureUpgradeService.LevelOfKey(_buildingId);
            MaxTier = PlacedStructureUpgradeService.MaxLevelFor(entry);

            FlowTrace.Step("Upgrade", _buildingId + " band-state IN: curLevel=" + CurrentTier
                + "/" + MaxTier + " (placed structure)");

            // One tile per level so the grid is never empty and the View's content signature
            // moves when the level does. Same row grammar as BuildResource's level tiles.
            for (int level = 1; level <= MaxTier; level++)
            {
                bool isCurrent = level <= CurrentTier;
                bool isNext = level == CurrentTier + 1;
                bool locked = level > CurrentTier + 1;

                string costStr;
                bool affordable = false;
                if (level <= 1)
                {
                    costStr = "Base";
                }
                else
                {
                    var stepCost = PlacedStructureUpgradeService.CostForNext(entry, level - 1);
                    costStr = CostString(new EcoCost
                    {
                        Wood = stepCost.wood,
                        Stone = stepCost.stone,
                        Iron = stepCost.iron,
                        Crystals = stepCost.crystals,
                    });
                    if (isNext) affordable = BuildModeController.CanAfford(stepCost);
                }

                string id = TierId(level);
                _costById[id] = isCurrent ? "Unlocked" : costStr;
                if (!isCurrent && level > 1)
                {
                    var stepParts = PlacedStructureUpgradeService.CostForNext(entry, level - 1);
                    _costPartsById[id] = DeNelle.Core.UI.CostFormat.Parts(new[] { ("wood", "Wood", stepParts.wood), ("stone", "Stone", stepParts.stone), ("iron", "Iron", stepParts.iron), ("crystal", "Crystals", stepParts.crystals) });
                }
                _effectById[id] = "Level " + level + " strength and durability";
                if (level > 1)
                    _keyLineById[id] = "UPGRADES " + Title.ToUpperInvariant() + " TO LEVEL " + level;
                _gateById[id] = isCurrent ? ""
                    : locked ? GateBuildingTier
                    : !affordable ? GateCost : "";
                _perks.Add(new ItemVM(id, "Level " + level, IconRoleTier, id, 0, "", affordable,
                                      rarity: null, equipped: isCurrent, locked: locked,
                                      lockReason: locked
                                          ? "Unlock 'Level " + (level - 1) + "' to open Level " + level : null));
            }

            FlowTrace.Step("Upgrade", _buildingId + " band-state OUT: next=level-"
                + (CurrentTier + 1) + " max=" + MaxTier);

            if (string.IsNullOrEmpty(Status))
                Status = CurrentTier >= MaxTier
                    ? "Every enhancement here is unlocked."
                    : "Tap upgrade to raise this structure a level.";
        }

        private void BuildUnknown()
        {
            Title = Titleize(_buildingId);
            CurrentTier = 0;
            MaxTier = 0;
            if (string.IsNullOrEmpty(Status)) Status = "This building has no enhancements.";
        }

        // ── WO-895 — compose the NEXT-upgrade card content ───────────────────────
        // Runs at the tail of every Rebuild, so the card is always in step with the ladder
        // that just built. Pure composition from catalog + wallet reads; no mutation.

        private void ComposeNextUpgrade()
        {
            if (_isCity) ComposeNextCity();
            else if (_isResource) ComposeNextResource();
            else if (_isPlaced) ComposeNextPlaced();

            FlowTrace.Step("Upgrade", _buildingId + " next-card: has=" + HasNextUpgrade
                + " " + TierWord.ToLowerInvariant() + "=" + CurrentTier + "/" + MaxTier
                + " name='" + _nextTierName + "' bonuses=" + _nextBonuses.Count
                + " costLines=" + _nextCostLines.Count + " affordable=" + _nextAffordable
                + " villageGate=" + _nextRequiresVillageTier + "/" + _villageTierNow);
        }

        private void ComposeNextCity()
        {
            var def = BuildingTierCatalog.Find(_buildingId);
            if (def == null) return;

            if (CurrentTier >= 1) _currentTierName = Ascii(TierDisplayName(def, CurrentTier));
            if (!HasNextUpgrade) return;

            int next = NextTier;
            var nextDef = BuildingTierCatalog.TierOf(_buildingId, next);
            if (nextDef == null) return;

            _nextTierName = Ascii(!string.IsNullOrEmpty(nextDef.Name) ? nextDef.Name : ("Tier " + next));
            _nextDescription = "Raises " + Ascii(Title) + " to Tier " + next + " of " + MaxTier + ".";
            _nextRequiresVillageTier = nextDef.RequiresVillageTier;

            AppendEffectClauses(nextDef.Effect, _nextBonuses);
            if (nextDef.Perks != null)
                foreach (var p in nextDef.Perks)
                {
                    if (p == null) continue;
                    string pn = !string.IsNullOrEmpty(p.Name) ? p.Name : p.Id;
                    if (!string.IsNullOrEmpty(pn)) _nextBonuses.Add("Opens research: " + Ascii(pn));
                }

            // The primary-material LANE is chosen by tier NUMBER because that is what the spend
            // does (BuildingUpgradeService.TierCost :195-197 / TryUpgrade :133-135): T1 Wood,
            // T2 Stone(Stone), T3+ Iron. The page shows what will be CHARGED.
            // WO-2005 — the branch itself moved to BuildingTierChargeLane, the ONE authority the
            // spend also reads. Output is identical for every authored tier (1..6); this file no
            // longer holds a copy that could drift from the charge it is describing.
            // ⚠ ONE case differs and it is unreachable: the old `else` also swallowed a Tier-0 def
            // and printed IRON for it, while BuildingTierChargeLane clamps tier<=1 to WOOD. No
            // ladder authors a tier 0 (building-tiers.json tiers run 1..6, read at source
            // 2026-09-06) and the SPEND clamped the other way anyway (TierCost's else-chain paid
            // NOTHING for a Tier-0 def), so the old line could only ever have printed a cost in a
            // lane the service would not charge. Named rather than preserved.
            AddCostLine(BuildingTierChargeLane.For(nextDef), nextDef.PrimaryMaterialCost);

            // WO-1391 (2026-09-05) — the AUTHORED lane vs the CHARGED lane. 'arcane-tower' T1 authors
            // costCrystal 1280 / costWood 0, and PrimaryMaterialCost = Max(CostWood, CostCrystal)
            // erases which one it was; the service then charges it as WOOD. The page is honest
            // about the charge; the data/service disagreement is surfaced here for the owner, not
            // silently resolved by either side (a catalog/service ruling, not a View fix).
            if (nextDef.CostCrystal > 0 && nextDef.CostWood <= 0 && nextDef.Tier <= 2)
                FlowTrace.Once("Upgrade", "lane-mismatch-" + _buildingId + "-" + next,
                    _buildingId + " tier-" + next + " authors costCrystal=" + nextDef.CostCrystal
                    + " (costWood=0) but BuildingUpgradeService.TierCost charges the primary as "
                    + (nextDef.Tier == 1 ? "WOOD" : "STONE") + " by tier number - the page shows the "
                    + "CHARGED lane. Data/service disagreement; needs a catalog ruling.");

            // WO-1391 — the GOLD term the tap charges (TryUpgrade :115-118) and CanAffordTier
            // requires (:210). It was never a cost line, which is the whole 'Missing resources
            // with 4000 wood' defect. Same GameState field the strip's Coins pill reads.
            AddCoinCostLine(nextDef.CostGold);

            // ONE predicate: the lines. The service's own answer is a cross-check, traced on
            // disagreement so a future divergence between the two is one read, not a re-theorise.
            _nextAffordable = AffordableFromLines(_nextCostLines);
            bool svcSays = BuildingUpgradeService.CanAffordTier(nextDef);
            if (svcSays != _nextAffordable)
                FlowTrace.Warn("Upgrade", _buildingId + " tier-" + next + " affordability DISAGREES: lines="
                    + _nextAffordable + " (" + ShortfallSentence(_nextCostLines) + ") vs CanAffordTier="
                    + svcSays + " - a cost term is missing from one side (WO-1391).");
        }

        /// <summary>WO-1391 — the Gold (Coins) cost line, read from the SAME GameState field
        /// <see cref="BuildingUpgradeService.CanAffordTier"/> checks and TryUpgrade debits
        /// (state.Resources.Coins), which is also what the strip's Coins pill shows via
        /// EconomyService.Coins. One ledger, three readers.</summary>
        private void AddCoinCostLine(int amount)
        {
            if (amount <= 0) return;
            int have = GameStateService.Instance?.State?.Resources.Coins ?? 0;
            _nextCostLines.Add(new UpgradeCostLine
            {
                ConceptId = "gold",
                Label = "Gold",
                Amount = amount,
                Have = have,
                Short = have < amount,
            });
        }

        private void ComposeNextResource()
        {
            var def = ResourceBuildingProgression.Find(_buildingId);
            if (def == null) return;

            _currentTierName = "Level " + CurrentTier;
            if (!HasNextUpgrade) return;

            int next = NextTier;
            var cur = def.LevelDef(CurrentTier);      // the cost to LEAVE the current level
            var nextLvl = def.LevelDef(next);
            _nextTierName = "Level " + next;
            _nextDescription = "Raises " + Ascii(Title) + " to Level " + next + " of " + MaxTier + ".";

            if (nextLvl != null)
            {
                _nextBonuses.Add("+" + nextLvl.YieldPerTick + " "
                    + ResourceBuildingProgression.LabelFor(nextLvl.Yields) + " per harvest");
                if (cur != null && nextLvl.HarvestInterval < cur.HarvestInterval - 0.01f)
                    _nextBonuses.Add("Harvests every " + nextLvl.HarvestInterval.ToString("0.#")
                        + "s (was " + cur.HarvestInterval.ToString("0.#") + "s)");
                if (nextLvl.YieldSizeMultiplier > 1.01f)
                    _nextBonuses.Add("Haul size x" + nextLvl.YieldSizeMultiplier.ToString("0.##"));
            }

            if (cur != null)
            {
                if (cur.UpgradeCost != null)
                    foreach (var c in cur.UpgradeCost) AddCostLine(c.Resource, c.Amount);
                if (cur.MagicCost > 0)
                    _nextCostLines.Add(new UpgradeCostLine
                    {
                        ConceptId = "magic",
                        Label = "Magic",
                        Amount = cur.MagicCost,
                        Have = ResourceLedger.MagicBalance(),
                        Short = ResourceLedger.MagicBalance() < cur.MagicCost,
                    });
                // WO-1391 — ONE predicate (the lines); the ledger's own answer is a cross-check.
                _nextAffordable = AffordableFromLines(_nextCostLines);
                bool svcSays = ResourceLedger.CanAfford(cur.UpgradeCost)
                               && (cur.MagicCost <= 0 || ResourceLedger.MagicBalance() >= cur.MagicCost);
                if (svcSays != _nextAffordable)
                    FlowTrace.Warn("Upgrade", _buildingId + " level-" + next + " affordability DISAGREES: lines="
                        + _nextAffordable + " (" + ShortfallSentence(_nextCostLines) + ") vs ResourceLedger="
                        + svcSays + " (WO-1391).");
            }
        }

        /// <summary>
        /// The next-upgrade card for a PLACED structure. Cost comes from the one resolver
        /// (<see cref="PlacedStructureUpgradeService.CostForNext"/>) and affordability from the
        /// wallet the tap actually charges (BuildModeController.CanAfford -> EconomyService), so
        /// the card can never advertise "Ready" against a wallet that will refuse.
        /// </summary>
        private void ComposeNextPlaced()
        {
            _currentTierName = "Level " + CurrentTier;
            if (!HasNextUpgrade) return;

            var entry = DeNelle.Core.Catalog.CatalogRegistry.Get(_placedItemId);
            int next = NextTier;
            _nextTierName = "Level " + next;
            _nextDescription = "Raises " + Ascii(Title) + " to Level " + next + " of " + MaxTier + ".";

            // ⚠ THIS USED TO BE THE ONE LINE:
            //       _nextBonuses.Add("Stronger " + Ascii(Title) + " at Level " + next);
            // For 225 wood + 100 iron the player was told only "Stronger Archer Tower at Level 3".
            // True, and useless — it names no number, so the player cannot tell a real upgrade from
            // a reskin, and cannot compare two towers. Owner ask 2026-08-17: show the real deltas.
            //
            // THE STATS WERE ALWAYS THERE. Tower range/damage step by tier via
            // BuildModeController.TowerStatMultiplier (L1 x1.0 / L2 x1.25 / L3 x1.55), applied to
            // the CATALOG BASE at placement so repeated upgrades never compound. We read that ONE
            // authority rather than re-declaring the ladder — a second copy of those numbers would
            // be the worst kind of duplicate authority, because the copy that drifts is the one
            // that TELLS THE PLAYER what they are buying.
            //
            // Fire rate is deliberately not scaled, so it is deliberately not claimed here.
            AddTowerStatDeltas(entry, next);

            var cost = PlacedStructureUpgradeService.CostForNext(entry, CurrentTier);
            AddWalletCostLine("Wood", cost.wood, _economy?.Wood ?? 0);
            AddWalletCostLine("Stone", cost.stone, _economy?.Stone ?? 0);
            AddWalletCostLine("Iron", cost.iron, _economy?.Iron ?? 0);
            AddWalletCostLine("Crystals", cost.crystals, _economy?.Crystals ?? 0);
            // WO-1391 — ONE predicate (the lines); the placer's own answer is a cross-check. With no
            // economy handle (headless suites) the lines read Have=0 and the cross-check is skipped:
            // there is no wallet to disagree with.
            _nextAffordable = AffordableFromLines(_nextCostLines);
            if (_economy != null)
            {
                bool svcSays = BuildModeController.CanAfford(cost);
                if (svcSays != _nextAffordable)
                    FlowTrace.Warn("Upgrade", _buildingId + " level-" + next + " affordability DISAGREES: lines="
                        + _nextAffordable + " (" + ShortfallSentence(_nextCostLines) + ") vs BuildModeController.CanAfford="
                        + svcSays + " (WO-1391).");
            }
        }

        /// <summary>
        /// Adds the REAL per-stat deltas for a tower upgrade ("Range 17.5 -> 21.7"), falling back to
        /// the old generic line for anything that is not a stat-stepping tower.
        /// </summary>
        /// <remarks>
        /// WO-1046 (owner ask 2026-08-17: *"all it says is stronger tower, can we check what
        /// changes"*). The numbers come from the ONE authority — the catalog base times
        /// <see cref="BuildModeController.TowerStatMultiplier"/>, which is exactly what the placer
        /// applies — so the card cannot advertise a number the game will not deliver.
        ///
        /// ⚠ WHY THE FALLBACK STAYS. Not every upgradeable row is a tower: storage containers step
        /// CAPACITY (six levels, StorageCapsCatalog), collectors step YIELD, walls step TOUGHNESS.
        /// Those ladders live elsewhere and are NOT this method's to claim. Printing "Range ->" for
        /// a granary would be worse than the vague line it replaces — a confident wrong number
        /// beats a vague right one only when it is right. Anything without a positive range/damage
        /// base keeps the generic wording until its own ladder is surfaced.
        /// </remarks>
        private void AddTowerStatDeltas(DeNelle.Core.Catalog.CatalogEntry entry, int next)
        {
            float baseRange = 0f, baseDamage = 0f;
            Guard.Try("Upgrade", "read tower stat base from catalog", () =>
            {
                if (entry?.repo == null) return;
                baseRange  = entry.repo.range;
                baseDamage = entry.repo.damage;
            });

            // Not a stat-stepping tower (or an unauthored row) — keep the old wording rather than
            // inventing a ladder this row does not have.
            if (baseRange <= 0f && baseDamage <= 0f)
            {
                _nextBonuses.Add("Stronger " + Ascii(Title) + " at Level " + next);
                FlowTrace.Step("Upgrade",
                    $"upgrade card for '{entry?.id}' has no range/damage base — generic bonus line kept.");
                return;
            }

            float curMul  = BuildModeController.TowerStatMultiplier(CurrentTier);
            float nextMul = BuildModeController.TowerStatMultiplier(next);

            if (baseRange > 0f)
                _nextBonuses.Add("Range " + Fmt(baseRange * curMul) + " -> " + Fmt(baseRange * nextMul));
            if (baseDamage > 0f)
                _nextBonuses.Add("Damage " + Fmt(baseDamage * curMul) + " -> " + Fmt(baseDamage * nextMul));

            FlowTrace.Step("Upgrade",
                $"upgrade card '{entry.id}' L{CurrentTier}->L{next}: range {baseRange * curMul:0.0}->" +
                $"{baseRange * nextMul:0.0}, damage {baseDamage * curMul:0.0}->{baseDamage * nextMul:0.0} " +
                $"(mul {curMul:0.00}->{nextMul:0.00}, the placer's own ladder).");
        }

        /// <summary>One decimal, no trailing ".0" — "17.5" and "14", never "14.0".</summary>
        private static string Fmt(float v) =>
            UnityEngine.Mathf.Approximately(v, UnityEngine.Mathf.Round(v)) ? UnityEngine.Mathf.RoundToInt(v).ToString()
                                                   : v.ToString("0.0");

        /// <summary>A cost line whose HAVE column is read from the SAME wallet the placed-structure
        /// charge debits (EconomyService), not the harvest ledger the city/resource ladders use.</summary>
        private void AddWalletCostLine(string label, int amount, int have)
        {
            if (amount <= 0) return;
            _nextCostLines.Add(new UpgradeCostLine
            {
                ConceptId = label.ToLowerInvariant() == "stone" ? "stone" : label.ToLowerInvariant().TrimEnd('s'),
                Label = label,
                Amount = amount,
                Have = have,
                Short = have < amount,
            });
        }

        private void AddCostLine(HarvestResource r, int amount)
        {
            if (amount <= 0) return;
            int have = ResourceLedger.Balance(r);
            _nextCostLines.Add(new UpgradeCostLine
            {
                ConceptId = ResourceBuildingProgression.LabelFor(r).ToLowerInvariant(),
                Label = ResourceBuildingProgression.LabelFor(r),
                Amount = amount,
                Have = have,
                Short = have < amount,
            });
        }

        /// <summary>Split an authored one-line effect ("Unlocks Spearman. Troop health +8%. Structure
        /// HP +45%") into SEPARATE bonus lines. Sentence boundaries only — commas inside a clause
        /// ("damage +15%, health +10%") belong together and are never split.</summary>
        private static void AppendEffectClauses(string effect, List<string> into)
        {
            if (string.IsNullOrEmpty(effect) || into == null) return;
            var parts = effect.Split(new[] { ". " }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var raw in parts)
            {
                string s = Ascii(raw).Trim().TrimEnd('.').Trim();
                if (s.Length > 0) into.Add(s);
            }
        }

        /// <summary>ASCII-fold the punctuation authored data carries (em/en dash, middle dot,
        /// ellipsis, curly quotes). TMP renders non-ASCII as tofu boxes (CLAUDE.md §7 law), and
        /// building-tiers.json does contain em dashes — so every string the card shows is folded
        /// HERE rather than trusting the data.</summary>
        private static string Ascii(string s)
        {
            if (string.IsNullOrEmpty(s)) return s ?? "";
            var sb = new System.Text.StringBuilder(s.Length + 4);
            foreach (char ch in s)
            {
                switch (ch)
                {
                    case '—': case '–': case '−': sb.Append('-'); break;   // em/en dash, minus
                    case '·': case '•': sb.Append('-'); break;                  // middle dot, bullet
                    case '…': sb.Append("..."); break;                               // ellipsis
                    case '‘': case '’': sb.Append('\''); break;                 // curly single quotes
                    case '“': case '”': sb.Append('"'); break;                  // curly double quotes
                    case ' ': sb.Append(' '); break;                                 // nbsp
                    default: sb.Append(ch <= 127 ? ch : '?'); break;
                }
            }
            return sb.ToString();
        }

        // ── Helpers (pure) ───────────────────────────────────────────────────────

        private string NextTierId() => TierId(CurrentTier + 1);

        private static string TierId(int tier) => "tier-" + tier;

        /// <summary>WO-680 — a tier's player-facing display name from the (already fetched) catalog
        /// def ("Ignite the Forge"); falls back to "Tier N" when unauthored. Pure data read.</summary>
        private static string TierDisplayName(BuildingUpgradeDef def, int tier)
        {
            if (def != null && def.Tiers != null)
                foreach (var t in def.Tiers)
                    if (t != null && t.Tier == tier)
                        return !string.IsNullOrEmpty(t.Name) ? t.Name : ("Tier " + tier);
            return "Tier " + tier;
        }

        // WO-697: cost numbers render through the ONE kit formatter (ElarionUi.CompactNumber
        // — verbatim below 10k, "98.6k"/"1.2m" above); the currency KEYWORDS stay intact so
        // the panel's DeriveSpendableCurrencies keyword scan keeps working.
        private string CostString(EcoCost c)
        {
            var parts = DeNelle.Core.UI.CostFormat.Parts(new[] { ("wood", "Wood", c.Wood), ("stone", "Stone", c.Stone), ("iron", "Iron", c.Iron), ("crystal", "Crystals", c.Crystals), ("gold", "Gold", c.Coins) });
            return parts.Count == 0 ? "Free" : DeNelle.Core.UI.CostFormat.Words(parts);
        }

        private static IReadOnlyList<DeNelle.Core.UI.CostPart> CostParts(EcoCost c) =>
            DeNelle.Core.UI.CostFormat.Parts(new[] { ("wood", "Wood", c.Wood), ("stone", "Stone", c.Stone), ("iron", "Iron", c.Iron), ("crystal", "Crystals", c.Crystals), ("gold", "Gold", c.Coins) });

        private static string ResourceCostString(IReadOnlyList<ResourceCost> costs, int magic)
        {
            var raw = new List<(string conceptId, string word, int amount)>();
            if (costs != null)
                foreach (var c in costs)
                {
                    string word = ResourceBuildingProgression.LabelFor(c.Resource);
                    raw.Add((word.ToLowerInvariant(), word, c.Amount));
                }
            raw.Add(("magic", "Magic", magic));
            var parts = DeNelle.Core.UI.CostFormat.Parts(raw);
            return parts.Count == 0 ? "Free" : DeNelle.Core.UI.CostFormat.Words(parts);
        }

        private static IReadOnlyList<DeNelle.Core.UI.CostPart> ResourceCostParts(IReadOnlyList<ResourceCost> costs, int magic)
        {
            var raw = new List<(string conceptId, string word, int amount)>();
            if (costs != null)
                foreach (var c in costs)
                {
                    string word = ResourceBuildingProgression.LabelFor(c.Resource);
                    raw.Add((word.ToLowerInvariant(), word, c.Amount));
                }
            raw.Add(("magic", "Magic", magic));
            return DeNelle.Core.UI.CostFormat.Parts(raw);
        }

        private static string Titleize(string id)
        {
            if (string.IsNullOrEmpty(id)) return "Building";
            id = id.Replace('-', ' ').Replace('_', ' ');
            return char.ToUpper(id[0]) + (id.Length > 1 ? id.Substring(1) : "");
        }

        private void Raise() { if (!_disposed) Changed?.Invoke(); }
    }
}

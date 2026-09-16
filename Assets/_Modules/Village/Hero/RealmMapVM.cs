// =============================================================================
// RealmMapVM — the PURE ViewModel behind RealmMapPanel (WO-826, strict MVVM).
// -----------------------------------------------------------------------------
// Assembly: DeNelle.Village   Namespace: DeNelle.Village.Hero
//
// ALL Realm Map state projection lives here (the ClanChatVM / RumorBoardVM
// pattern): implements IPanelViewModel, carries NO UnityEngine UI types, and is
// unit-testable without a scene. The View (RealmMapPanel) renders vm.* only.
//
// Data in:
//   * RealmMapCatalog (Core) — the dual-copy realm-map.json typed loader
//     (Elarion home + 5 regions with mapPoint / gate / description).
//   * ISource — the save-progress seam (fake in tests; GameStateService live):
//     BestWave + the persisted RegionProgress discovered/cleared ledger.
//
// State derivation (mirrors the file's own _schemaNotes: RegionState is DERIVED
// at runtime from RegionProgress, never stored):
//   * home                       -> Home (always visible, never locked).
//   * ledger Cleared[id]         -> Cleared.
//   * ledger Discovered[id] OR gate satisfied -> Discovered. Gate satisfaction is
//     derived live: bestWave gate vs GameState.BestWave; regionCleared gate vs
//     the Cleared ledger. Nothing WRITES the Discovered ledger yet — that is the
//     WO-827 discovery/travel ledger; a FlowTrace.Once documents the stub. On a
//     fresh save (BestWave 0) every region therefore renders LOCKED fog, except
//     the Thornwood lights up once the village has held to wave 3 (its authored
//     trivial gate — documented WO-826 §2 choice).
//   * else                       -> Locked.
//
// Travel is a DISABLED stub until WO-827 (TravelEnabled always false; the CTA
// label carries "coming with discovery"). Colorblind law: every state is TEXT
// (StateLabel / DetailState), never colour alone. ASCII-only player strings;
// Elarion never Avalon (titles come verbatim from the catalog).
// =============================================================================

using System;
using System.Collections.Generic;
using DeNelle.Core.Diagnostics;
using DeNelle.Core.State;
using DeNelle.Core.UI.Mvvm;
using DeNelle.Core.World;

namespace DeNelle.Village.Hero
{
    /// <summary>
    /// Pure ViewModel for the Realm Map parchment panel. Projects the catalog +
    /// save ledger into node rows and a selection detail; holds the selection.
    /// </summary>
    public sealed class RealmMapVM : IPanelViewModel, IDisposable
    {
        // ── Save-progress seam (fake in tests; GameStateService live). ──────────
        public interface ISource
        {
            int BestWave { get; }                    // GameState.BestWave
            bool IsDiscovered(string regionId);      // RegionProgress.Discovered ledger
            bool IsCleared(string regionId);         // RegionProgress.Cleared ledger
        }

        /// <summary>Derived node state (the React RegionState minus the transient
        /// 'threatened' overlay — its weeklyRealmThreat flag is authored false).</summary>
        public enum NodeState { Home, Locked, Discovered, Cleared }

        /// <summary>One projected map node: id + title + mapPoint percents + derived state.
        /// XPercent rightward, YPercent DOWNWARD from the top-left of the map rect
        /// (the React realm-map-layout.ts convention the file was authored in).</summary>
        public readonly struct NodeRow
        {
            public readonly string Id;
            public readonly string Title;
            public readonly float XPercent;
            public readonly float YPercent;
            public readonly NodeState State;
            /// <summary>ASCII state word ("Home" / "Locked" / "Discovered" / "Cleared") —
            /// text carries the state, never colour alone.</summary>
            public readonly string StateLabel;
            public readonly bool IsHome;
            /// <summary>
            /// The region's authored <c>biome</c> token from realm-map.json ("forest",
            /// "swamp", "ice", "fire", "cosmic"), or "home" for Elarion. Never null.
            ///
            /// WO-829 §1: the View needs this to look the node's treatment up in
            /// <c>RealmAtmosphereStyle</c> — the biome ring tint, the node glyph and the
            /// epithet. It is the RAW TOKEN, not a colour: the VM stays free of
            /// UnityEngine.UI types (strict MVVM), and the ONE presentation table is the
            /// only thing that turns a token into a look, so the parchment and the minimap
            /// cannot render the same swamp two different ways.
            /// </summary>
            public readonly string Biome;
            /// <summary>
            /// Whether this node may show SPOILERY detail (biome, epithet, content pins).
            /// Derived through <c>RealmPinBoard.RevealsDetail</c> — the ONE fog predicate,
            /// never a second rule. Fail-closed: a locked or unrecognised state hides.
            /// </summary>
            public readonly bool RevealsDetail;

            public NodeRow(string id, string title, float x, float y,
                           NodeState state, string stateLabel, bool isHome,
                           string biome, bool revealsDetail)
            {
                Id = id; Title = title; XPercent = x; YPercent = y;
                State = state; StateLabel = stateLabel; IsHome = isHome;
                Biome = biome ?? "";
                RevealsDetail = revealsDetail;
            }
        }

        private readonly ISource _source;
        private readonly HomeBaseDef _home;
        private readonly IReadOnlyList<RealmRegionDef> _regions;
        private readonly Action _onClose;
        private bool _disposed;

        private readonly List<NodeRow> _nodes = new List<NodeRow>();

        /// <summary>Live wiring: real catalog + the GameStateService-backed source.</summary>
        public static RealmMapVM CreateDefault(Action onClose)
            => new RealmMapVM(new StateSource(), RealmMapCatalog.Home, RealmMapCatalog.Regions, onClose);

        public RealmMapVM(ISource source, HomeBaseDef home,
                          IReadOnlyList<RealmRegionDef> regions, Action onClose)
        {
            _source = source;
            _home = home;
            _regions = regions ?? Array.Empty<RealmRegionDef>();
            _onClose = onClose;

            // WO-826 §2: the DISCOVERY ledger has no writer yet — derivation below reads
            // the persisted RegionProgress + live gates; writes land with WO-827.
            FlowTrace.Once("RealmMap", "progress-stub",
                "region discovery derivation is gate+ledger READ-ONLY (no writer until WO-827 travel/discovery)");

            Rebuild();
            // Open with the home base selected so the detail pane is never blank.
            SelectedId = _home != null ? _home.Id : (_nodes.Count > 0 ? _nodes[0].Id : null);
        }

        // ── IPanelViewModel ───────────────────────────────────────────────────
        public event Action Changed;
        public string Title => "REALM MAP";
        public void Close() => _onClose?.Invoke();

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Changed = null;
        }

        // ── Read-only data the View renders ──────────────────────────────────

        /// <summary>All map nodes (home first, then regions in mapOrder). Never null.</summary>
        public IReadOnlyList<NodeRow> Nodes => _nodes;

        /// <summary>The selected node id (home by default; never null while any node exists).</summary>
        public string SelectedId { get; private set; }

        /// <summary>Detail: gilt header title for the selection.</summary>
        public string DetailTitle
        {
            get
            {
                var n = FindNode(SelectedId);
                return n.HasValue ? n.Value.Title : "The Realm";
            }
        }

        /// <summary>Detail: the state line, TEXT-encoded (colorblind law). E.g.
        /// "Home Base - you are here" / "Region - Locked".</summary>
        public string DetailState
        {
            get
            {
                var n = FindNode(SelectedId);
                if (!n.HasValue) return "";
                if (n.Value.IsHome) return "Home Base - you are here";
                return "Region - " + n.Value.StateLabel;
            }
        }

        /// <summary>Detail: WHY a locked region is locked (gate reason), or a short
        /// standing line for other states. Never null.</summary>
        public string DetailGate
        {
            get
            {
                var n = FindNode(SelectedId);
                if (!n.HasValue || n.Value.IsHome) return "";
                var region = FindRegion(n.Value.Id);
                if (region == null) return "";
                switch (n.Value.State)
                {
                    case NodeState.Cleared:    return "Cleared - this land stands safe.";
                    case NodeState.Discovered: return "Discovered - travel opens with a coming update.";
                    default:                   return "Gate: " + GateReason(region.Gate);
                }
            }
        }

        /// <summary>WO-1396: the selected region's one-time clear reward as an ASCII list
        /// ("120 crystals, 40 food"), read off realm-map.json clearReward - the numbers are data,
        /// never typed here. "" for home, a fogged (Locked) region, or a region with no reward,
        /// so a locked node never leaks what it pays.</summary>
        public string DetailReward
        {
            get
            {
                var n = FindNode(SelectedId);
                if (!n.HasValue || n.Value.IsHome || n.Value.State == NodeState.Locked) return "";
                var region = FindRegion(n.Value.Id);
                var r = region != null ? region.ClearReward : null;
                if (r == null) return "";
                var parts = new List<string>(3);
                if (r.Crystals > 0) parts.Add(r.Crystals + " crystals");
                if (r.Stone > 0) parts.Add(r.Stone + " food");
                if (r.Coins > 0) parts.Add(r.Coins + " gold");
                return string.Join(", ", parts);
            }
        }

        /// <summary>Detail: the authored region/home description (scrolls in the View when long).</summary>
        public string DetailBody
        {
            get
            {
                var n = FindNode(SelectedId);
                if (!n.HasValue) return "";
                if (n.Value.IsHome)
                    return _home != null ? (_home.Description ?? "") : "";
                var region = FindRegion(n.Value.Id);
                return region != null ? (region.Description ?? "") : "";
            }
        }

        // ── WO-829 §1/§3: atmosphere + content the View paints ────────────────

        /// <summary>The selected node's biome token, or "" when the selection is fogged.
        /// FOG-GATED HERE, once: a locked region must not leak "this one is the fire one"
        /// through its ring tint or glyph. Goes through NodeRow.RevealsDetail, which is
        /// itself the shared RealmPinBoard predicate.</summary>
        public string SelectedBiome
        {
            get
            {
                var n = FindNode(SelectedId);
                if (!n.HasValue || !n.Value.RevealsDetail) return "";
                return n.Value.Biome;
            }
        }

        /// <summary>True when the Withering edge band should be painted on the parchment
        /// (realm-map.json's <c>withering.edgeBorder</c>). ATMOSPHERE ONLY — the data's own
        /// <c>weeklyRealmThreat</c> stays false and nothing here starts a timer.</summary>
        public bool ShowWithering
        {
            get
            {
                var w = RealmMapCatalog.Withering;
                return w != null && w.EdgeBorder;
            }
        }

        /// <summary>The one-line Withering cartouche inked across the top of the parchment.
        /// Empty when the band is off, so the View never draws a caption for nothing.</summary>
        public string WitheringLore
            => ShowWithering ? DeNelle.Core.UI.RealmAtmosphereStyle.WitheringLore : "";

        /// <summary>
        /// The published content pins that belong to <paramref name="regionId"/>, already
        /// FOG-FILTERED. Returns an EMPTY list for a region that does not reveal detail —
        /// so a locked node can never show "2 raid camps" and give away what is waiting.
        ///
        /// Reads <c>RealmPinBoard</c>, the ONE registry the corner minimap also reads: the
        /// map does not scan the world for content, it mirrors what producers published.
        ///
        /// Returns a REUSED buffer (the View consumes it before the next call) — a Repaint
        /// walks every node, and a fresh List per node per repaint is garbage for nothing.
        /// </summary>
        public IReadOnlyList<RealmPin> PinsFor(string regionId)
        {
            _pinScratch.Clear();
            if (string.IsNullOrEmpty(regionId)) return _pinScratch;

            var node = FindNode(regionId);
            if (!node.HasValue || !node.Value.RevealsDetail) return _pinScratch;

            var pins = RealmPinBoard.Pins;
            if (pins == null) return _pinScratch;
            for (int i = 0; i < pins.Count; i++)
                if (string.Equals(pins[i].RegionId, regionId, StringComparison.OrdinalIgnoreCase))
                    _pinScratch.Add(pins[i]);
            return _pinScratch;
        }

        private readonly List<RealmPin> _pinScratch = new List<RealmPin>();

        /// <summary>
        /// The selection's content pins as ONE plain sentence for the detail pane
        /// ("Here: 1 dungeon, 2 raid camps."), or "" when there is nothing to say.
        ///
        /// The map's in-disc pin strip is a glyph cluster; THIS is the half that survives
        /// full desaturation and a small screen, which is why the words are not optional
        /// decoration (colourblind law, CLAUDE.md §7). A raid camp named here is still only
        /// a MARKER — the army gate is checked when the player actually taps through.
        /// </summary>
        public string DetailPins
        {
            get
            {
                var pins = PinsFor(SelectedId);
                if (pins == null || pins.Count == 0) return "";

                var counts = new Dictionary<RealmPinKind, int>();
                for (int i = 0; i < pins.Count; i++)
                {
                    counts.TryGetValue(pins[i].Kind, out int c);
                    counts[pins[i].Kind] = c + (pins[i].Count > 0 ? pins[i].Count : 1);
                }

                var sb = new System.Text.StringBuilder("Here: ");
                bool first = true;
                foreach (var kv in counts)
                {
                    if (!first) sb.Append(", ");
                    first = false;
                    sb.Append(kv.Value).Append(' ').Append(PinNoun(kv.Key, kv.Value));
                }
                return sb.Append('.').ToString();
            }
        }

        private static string PinNoun(RealmPinKind kind, int count)
        {
            string one;
            switch (kind)
            {
                case RealmPinKind.Dungeon:    one = "dungeon";    break;
                case RealmPinKind.RaidTarget: one = "raid camp";  break;
                case RealmPinKind.Threat:     one = "threat";     break;
                case RealmPinKind.Army:       one = "muster";     break;
                case RealmPinKind.Rumor:      one = "rumor";      break;
                case RealmPinKind.Objective:  one = "objective";  break;
                default:                      one = "marker";     break;
            }
            return count == 1 ? one : one + "s";
        }

        /// <summary>True when the selection is a region (the Travel CTA slot renders).
        /// Home shows no CTA — the player is already here.</summary>
        public bool ShowTravel
        {
            get
            {
                var n = FindNode(SelectedId);
                return n.HasValue && !n.Value.IsHome;
            }
        }

        /// <summary>WO-826 stub: travel is DISABLED until the WO-827 discovery/travel ledger.</summary>
        public bool TravelEnabled => false;

        /// <summary>Disabled-CTA label — the word carries the state (colorblind law).</summary>
        public string TravelLabel => "Travel - coming with discovery";

        // ── Commands ──────────────────────────────────────────────────────────

        /// <summary>Select a node by id (no-op on unknown/no-change). Raises Changed.</summary>
        public void Select(string id)
        {
            if (string.IsNullOrEmpty(id) || id == SelectedId) return;
            if (!FindNode(id).HasValue) return;
            SelectedId = id;
            FlowTrace.Step("RealmMap", "select '" + id + "' -> state " +
                (FindNode(id).HasValue ? FindNode(id).Value.StateLabel : "?"));
            Raise();
        }

        // ── Projection ────────────────────────────────────────────────────────

        private void Rebuild()
        {
            _nodes.Clear();

            if (_home != null)
            {
                float hx = _home.MapPoint != null ? _home.MapPoint.X : 50f;
                float hy = _home.MapPoint != null ? _home.MapPoint.Y : 50f;
                _nodes.Add(new NodeRow(_home.Id,
                    string.IsNullOrEmpty(_home.Title) ? "Elarion" : _home.Title,
                    hx, hy, NodeState.Home, "Home", isHome: true,
                    biome: HomeBiomeToken, revealsDetail: true));
            }

            foreach (var r in _regions)
            {
                if (r == null || string.IsNullOrEmpty(r.Id)) continue;
                var state = StateFor(r);
                float x = r.MapPoint != null ? r.MapPoint.X : 50f;
                float y = r.MapPoint != null ? r.MapPoint.Y : 50f;
                // Fog goes through the ONE shared predicate (RealmPinBoard.RevealsDetail),
                // fed the same state literal a pin producer would use. No second fog rule.
                bool reveals = RealmPinBoard.RevealsDetail(StateLiteral(state));
                _nodes.Add(new NodeRow(r.Id, r.Title ?? r.Id, x, y,
                    state, StateWord(state), isHome: false,
                    biome: r.Biome ?? "", revealsDetail: reveals));
            }
        }

        /// <summary>The biome token the home base renders as (it carries no authored
        /// <c>biome</c> field — it is not a region).</summary>
        public const string HomeBiomeToken = "home";

        private NodeState StateFor(RealmRegionDef region) => DeriveState(_source, region);

        // ── THE SINGLE derivation site (see RegionStateFor) ────────────────────
        // Both the panel's own projection and the out-of-panel pin producers resolve a
        // region's state THROUGH here. A producer that re-derived "is this discovered?"
        // from the save ledger itself would be a second copy of the gate rules, and the
        // day a gate kind changes the map and the pins would disagree about the same
        // region -- WO-829 §6's "no duplicate game logic", applied to state and not just
        // to projection helpers.
        private static NodeState DeriveState(ISource source, RealmRegionDef region)
        {
            if (region == null || string.IsNullOrEmpty(region.Id)) return NodeState.Locked;
            if (source != null && source.IsCleared(region.Id)) return NodeState.Cleared;
            if (source != null && source.IsDiscovered(region.Id)) return NodeState.Discovered;
            if (GateSatisfied(source, region.Gate)) return NodeState.Discovered;
            return NodeState.Locked;
        }

        /// <summary>
        /// The live region state as the LITERAL <c>RealmPinBoard.RevealsDetail</c> accepts
        /// ("locked" | "discovered" | "cleared"), derived from the real save ledger.
        /// This is what a pin producer calls so a locked region's content never leaks.
        /// An unknown region id answers "locked" — fail-closed, same as the predicate.
        /// </summary>
        public static string RegionStateFor(string regionId)
        {
            var def = RealmMapCatalog.Find(regionId);
            if (def == null)
            {
                var home = RealmMapCatalog.Home;
                if (home != null && !string.IsNullOrEmpty(regionId) && home.Id == regionId)
                    return StateLiteral(NodeState.Home);
                return StateLiteral(NodeState.Locked);
            }
            return StateLiteral(DeriveState(new StateSource(), def));
        }

        /// <summary>The lowercase wire literal for a node state (the RegionState vocabulary
        /// realm-map.json documents). Home is "cleared" — the player is standing in it, so
        /// it can never be fogged.</summary>
        public static string StateLiteral(NodeState s)
        {
            switch (s)
            {
                case NodeState.Home:       return "cleared";
                case NodeState.Cleared:    return "cleared";
                case NodeState.Discovered: return "discovered";
                default:                   return "locked";
            }
        }

        private static bool GateSatisfied(ISource source, RealmRegionGate gate)
        {
            if (gate == null || source == null) return false;
            switch (gate.Kind)
            {
                case RealmRegionGate.KindBestWave:
                    return source.BestWave >= gate.Value;
                case RealmRegionGate.KindRegionCleared:
                    return !string.IsNullOrEmpty(gate.RegionId) && source.IsCleared(gate.RegionId);
                default:
                    FlowTrace.Warn("RealmMap", "unknown gate kind '" + (gate.Kind ?? "<null>") + "' — treated as locked");
                    return false;
            }
        }

        private string GateReason(RealmRegionGate gate)
        {
            if (gate == null) return "Unknown.";
            switch (gate.Kind)
            {
                case RealmRegionGate.KindBestWave:
                    int best = _source != null ? _source.BestWave : 0;
                    return "Hold the village to wave " + gate.Value + " (best so far: " + best + ").";
                case RealmRegionGate.KindRegionCleared:
                    return "Clear " + RealmMapCatalog.TitleFor(gate.RegionId) + " first.";
                default:
                    return "Sealed by forces unknown.";
            }
        }

        private static string StateWord(NodeState s)
        {
            switch (s)
            {
                case NodeState.Home:       return "Home";
                case NodeState.Cleared:    return "Cleared";
                case NodeState.Discovered: return "Discovered";
                default:                   return "Locked";
            }
        }

        private NodeRow? FindNode(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            foreach (var n in _nodes)
                if (n.Id == id) return n;
            return null;
        }

        private RealmRegionDef FindRegion(string id)
        {
            foreach (var r in _regions)
                if (r != null && r.Id == id) return r;
            return null;
        }

        private void Raise() { if (!_disposed) Changed?.Invoke(); }

        // ── Real seam: GameStateService-backed progress (SOLE live resolution site). ──
        private sealed class StateSource : ISource
        {
            public int BestWave
            {
                get
                {
                    var svc = GameStateService.Instance;
                    return svc != null && svc.State != null ? svc.State.BestWave : 0;
                }
            }

            public bool IsDiscovered(string regionId)
            {
                var ledger = Ledger();
                return ledger != null && ledger.Discovered != null &&
                       !string.IsNullOrEmpty(regionId) &&
                       ledger.Discovered.TryGetValue(regionId, out var d) && d;
            }

            public bool IsCleared(string regionId)
            {
                var ledger = Ledger();
                return ledger != null && ledger.Cleared != null &&
                       !string.IsNullOrEmpty(regionId) &&
                       ledger.Cleared.TryGetValue(regionId, out var c) && c;
            }

            private static RegionProgress Ledger()
            {
                var svc = GameStateService.Instance;
                return svc != null && svc.State != null ? svc.State.Regions : null;
            }
        }
    }
}

// =============================================================================
// WallRepairController — the player-facing wall / gate / building repair loop.
// -----------------------------------------------------------------------------
// Workstream B, feature 1. WallSegment / Gate / Building already expose a
// Repair(amount) primitive and raise DamageChanged / HpChanged / Collapsed
// events — the repair PRIMITIVE exists; this file builds the player INTERACTION
// LOOP that was entirely missing:
//
//   1. SCAN      — on a short timer, find the damaged structures in the village
//                  and keep a calm amber RepairHighlight pulsing over each one.
//   2. SELECT    — the player taps / clicks a structure; a camera raycast finds
//                  it, RepairTarget wraps it, and a bright violet highlight
//                  marks the selection.
//   3. PROMPT    — a MATERIALS cost is computed and shown (the HUD repair
//                  prompt). OWNER RULING 2026-07-11: repair cost = damage
//                  fraction x the structure's own BUILD cost in its own
//                  materials (wood/iron/food per its catalog row); a destroyed
//                  structure (fraction 1) is a REBUILD at full build cost.
//                  Crystals are NEVER spent on repair (resource-model canon:
//                  Wood/Iron/Food build structures; Crystals = the special arc).
//                  The prompt is MODAL: while it is open every tap belongs to
//                  the prompt's Confirm / Cancel buttons, never the world — the
//                  interaction stays deterministic.
//   4. CONFIRM   — on confirm: if the material wallets cover the cost, spend
//                  through the SAME construction-economy path build-mode
//                  placement charges (EconomyService.TrySpend — the
//                  BuildModeController.ChargeLedger seam) PLUS the GameState
//                  Wood/Iron mirror (the GrantSpendable both-sides pattern, see
//                  SpendMaterials) and call the structure's existing Repair();
//                  otherwise show an insufficient-materials message.
//
// MODULE ISOLATION (port spec Part 2): this file lives in DeNelle.Village and
// references only DeNelle.Core (GameStateService) + Village types. It CANNOT
// reference DeNelle.HUD, so it never touches VillageHudController directly.
// Instead it raises plain UnityEvents (PromptShown / PromptHidden /
// FeedbackShown / HighlightCountChanged) and exposes ConfirmRepair() /
// CancelRepair() / RequestRepair(target) public methods. The scene-setup editor
// file (WallRepairSceneSetup.cs) cross-wires those to the HUD by reflection —
// the same passive-display pattern VillageHudController already documents for
// its BuildRequested event.
//
// INPUT: the project uses the LEGACY Input Manager (UnityEngine.Input) — see the
// workstream constraints. Tap = Input.GetMouseButtonDown(0) OR a began touch.
// =============================================================================

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using DeNelle.Core.Catalog;
using DeNelle.Core.Diagnostics;
using DeNelle.Core.State;
using DeNelle.Village.Buildings.Progression;
// The multi-resource cost shape shared with build-mode placement. Aliased: the
// bare name 'ResourceCost' resolves to DeNelle.Village.ResourceCost (the
// EconomyService struct) inside this namespace.
using CoreCost = DeNelle.Core.Catalog.ResourceCost;

namespace DeNelle.Village
{
    /// <summary>
    /// Payload for <see cref="WallRepairController.PromptShown"/> — the data the
    /// HUD repair prompt needs to render one selection.
    /// </summary>
    [Serializable]
    public struct RepairPromptInfo
    {
        /// <summary>Player-facing structure name (e.g. "North Gate").</summary>
        public string StructureName;
        /// <summary>
        /// The fully-composed, ready-to-display prompt sub-line — e.g.
        /// "Repair the North Gate? Cost: 12 wood, 4 iron". Composed here (the
        /// HUD shows it verbatim), so the materials cost travels IN the text.
        /// </summary>
        public string Subtitle;
        /// <summary>
        /// LEGACY (owner 2026-07-11): crystals are NO LONGER spent on repair —
        /// always 0 now. Field kept so the WallRepairHudBridge payload shape is
        /// unchanged; the real cost is <see cref="CostText"/> (in the Subtitle).
        /// </summary>
        public int CrystalCost;
        /// <summary>The composed in-kind materials cost, e.g. "12 wood, 4 iron".</summary>
        public string CostText;
        /// <summary>True when the material wallets cover the repair cost.</summary>
        public bool Affordable;
        /// <summary>Damage fraction 0..1 of the selected structure.</summary>
        public float DamageFraction;
        /// <summary>True when fully destroyed — the prompt verb is "Rebuild" (full build cost).</summary>
        public bool Destroyed;
    }

    /// <summary>A UnityEvent carrying a <see cref="RepairPromptInfo"/>.</summary>
    [Serializable]
    public sealed class RepairPromptEvent : UnityEvent<RepairPromptInfo> { }

    /// <summary>A UnityEvent carrying a feedback message + an is-error flag.</summary>
    [Serializable]
    public sealed class RepairFeedbackEvent : UnityEvent<string, bool> { }

    /// <summary>A UnityEvent carrying an int count (damaged-structure tally).</summary>
    [Serializable]
    public sealed class RepairCountEvent : UnityEvent<int> { }

    /// <summary>
    /// Drives the player wall / gate / building repair loop: highlight damaged
    /// structures, tap-to-select, show an in-kind materials cost (damage
    /// fraction x the structure's own catalog build cost — full build cost =
    /// REBUILD when destroyed), spend + repair on confirm. A self-contained
    /// Village sub-system MonoBehaviour — the scene-setup editor file adds it
    /// to the built village scene.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class WallRepairController : MonoBehaviour
    {
        // ── Inspector wiring ─────────────────────────────────────────────────

        [Header("Scene refs (wire in the inspector, or leave blank to auto-find)")]
        [Tooltip("Camera the selection raycast is cast from. Blank: Camera.main.")]
        [SerializeField] private Camera _camera;

        [Tooltip("Layers the selection raycast may hit. Default: everything.")]
        [SerializeField] private LayerMask _selectableMask = ~0;

        // ── Repair cost (owner ruling 2026-07-11) ────────────────────────────
        // No serialized cost constants any more: repair is priced DATA-ONLY as
        // damage-fraction x the structure's own catalog build cost in its own
        // materials (wood/iron/food). Structures with no materials row anywhere
        // price from the 'repair_default' catalog row (structures-catalog.json,
        // dual-copy). Crystals are never charged. The old _fullRepairCost /
        // _minRepairCost / _useGameState / _localCrystalBalance fields are
        // removed — spends go through the construction economy (EconomyService).

        [Header("Scanning")]
        [Tooltip("Seconds between damaged-structure rescans. Keeps the highlight set fresh " +
                 "without scanning every frame.")]
        [SerializeField, Min(0.1f)] private float _rescanInterval = 0.75f;

        // DEF-226: the always-on amber repair disc the auto-scan pooled over every
        // damaged structure read as a confusing, unexplained green/gold ground
        // artifact (and in one screenshot floated near a hero). DECISION: suppress
        // the always-on repair highlight entirely. Default this OFF so no marker
        // auto-spawns during normal play; the repair MECHANIC stays intact and a
        // highlight is shown ONLY on an explicit selection (tap / RequestRepair /
        // SurfaceWorstRepair). A baked scene value is overridden at runtime in
        // Awake() so the build ships suppressed with no rebake.
        [Tooltip("DEF-226: leave OFF. When true the controller auto-scans the scene and shows an " +
                 "always-on repair disc over every damaged structure (the suppressed confusing artifact). " +
                 "When false, highlights appear only on an explicit repair selection.")]
        [SerializeField] private bool _autoFindStructures = false;

        // ── Events — the HUD bridge (the editor cross-wires these) ───────────

        [Header("Events (cross-wired to the HUD by WallRepairSceneSetup)")]
        [Tooltip("Raised when a structure is selected — carries the prompt data the HUD shows.")]
        public RepairPromptEvent PromptShown = new RepairPromptEvent();

        [Tooltip("Raised when the selection / prompt is cleared — the HUD hides the prompt.")]
        public UnityEvent PromptHidden = new UnityEvent();

        [Tooltip("Raised on a repair result — carries the message + an is-error flag for the HUD toast.")]
        public RepairFeedbackEvent FeedbackShown = new RepairFeedbackEvent();

        [Tooltip("Raised when the count of damaged structures changes — the HUD may show a badge.")]
        public RepairCountEvent DamagedCountChanged = new RepairCountEvent();

        // ── Runtime state ────────────────────────────────────────────────────

        private readonly List<RepairTarget> _damaged = new List<RepairTarget>();
        private readonly List<RepairHighlight> _highlightPool = new List<RepairHighlight>();
        private RepairHighlight _selectionHighlight;
        private RepairTarget _selected;
        private Transform _highlightRoot;
        private float _rescanTimer;
        private int _lastDamagedCount = -1;

        // Explicitly-registered structures (used when _autoFindStructures is off).
        private readonly List<GameObject> _registered = new List<GameObject>();

        // When the HUD repair prompt consumes a tap (Confirm / Cancel button),
        // the bridge calls SuppressNextWorldTap() so the SAME pointer-press is
        // not also read as a world tap by this controller's raycast. UI Toolkit
        // and this MonoBehaviour both see the one OS click; this is the seam.
        private int _suppressTapUntilFrame = -1;

        /// <summary>True while a structure is selected and the prompt is up.</summary>
        public bool HasSelection => _selected != null && _selected.IsValid;

        /// <summary>The currently-selected repair target, or null.</summary>
        public RepairTarget Selected => _selected;

        /// <summary>Count of damaged structures found by the most recent scan.</summary>
        public int DamagedCount => _damaged.Count;

        // =====================================================================
        //  Lifecycle
        // =====================================================================

        private void Awake()
        {
            if (_camera == null) _camera = Camera.main;

            // DEF-226: force the always-on auto-scan OFF at runtime, even if a baked
            // scene instance was serialized with _autoFindStructures = true. This is
            // the no-rebake override pattern used by other DEF-* fixes — the BUILD
            // ships with the confusing always-on repair disc suppressed without
            // touching / re-baking the scene. The repair mechanic is unaffected:
            // a highlight is still shown on an explicit selection (tap / RequestRepair).
            _autoFindStructures = false;
        }

        private void OnEnable()
        {
            EnsureHighlightRoot();
            Rescan();
        }

        private void OnDisable()
        {
            ClearSelection();
        }

        private void Update()
        {
            // Periodic rescan keeps the damaged-structure highlight set current
            // as enemies wear walls down / the player repairs them.
            _rescanTimer -= Time.deltaTime;
            if (_rescanTimer <= 0f)
            {
                _rescanTimer = _rescanInterval;
                Rescan();
            }

            // If the selected structure was destroyed / removed, drop the prompt.
            if (_selected != null && !_selected.IsValid)
                ClearSelection();

            if (TapPressedThisFrame())
                HandleTap();
        }

        // =====================================================================
        //  Structure registration (used when _autoFindStructures is off)
        // =====================================================================

        /// <summary>
        /// Registers an explicit set of structure GameObjects to consider for
        /// repair. Only consulted when <c>_autoFindStructures</c> is false — the
        /// default scans the whole scene. Each object should carry (on itself or
        /// a child) a <see cref="WallSegment"/>, <see cref="Gate"/> or
        /// <see cref="Building"/>.
        /// </summary>
        public void RegisterStructures(IEnumerable<GameObject> structures)
        {
            if (structures == null) return;
            foreach (var go in structures)
                if (go != null && !_registered.Contains(go)) _registered.Add(go);
            Rescan();
        }

        // =====================================================================
        //  Scanning + highlights
        // =====================================================================

        /// <summary>
        /// Rebuilds the damaged-structure list and refreshes the in-world
        /// highlight markers. Cheap enough to run a few times a second; called
        /// on a timer and after every repair.
        /// </summary>
        public void Rescan()
        {
            _damaged.Clear();
            CollectDamaged(_damaged);

            // DEF-226: only paint the always-on pooled "repairable" discs when the
            // (now default-OFF, runtime-forced-OFF) auto-scan is enabled. With it
            // suppressed we still keep _damaged + the damaged-count badge current,
            // but no unprompted ground disc is shown — a highlight appears ONLY on
            // an explicit selection (the bright selection marker, see Select()).
            EnsureHighlightRoot();
            int poolIndex = 0;
            if (_autoFindStructures)
            {
                // Keep the highlight pool sized to the damaged set, repositioning
                // each marker over its structure. The selected structure gets the
                // bright marker instead of a pool one.
                for (int i = 0; i < _damaged.Count; i++)
                {
                    var t = _damaged[i];
                    if (_selected != null && _selected.SameAs(t)) continue; // selection marker covers it

                    RepairHighlight hl = GetPooledHighlight(poolIndex++);
                    hl.SetVisible(true);
                    hl.SetSelected(false);
                    hl.FitTo(t);
                }
            }
            // Hide any surplus pooled markers (also hides ALL of them when suppressed).
            for (int i = poolIndex; i < _highlightPool.Count; i++)
                _highlightPool[i].SetVisible(false);

            if (_damaged.Count != _lastDamagedCount)
            {
                _lastDamagedCount = _damaged.Count;
                DamagedCountChanged?.Invoke(_damaged.Count);
            }
        }

        /// <summary>
        /// Fills <paramref name="into"/> with a RepairTarget for every damaged
        /// structure. When <c>_autoFindStructures</c> is off (DEF-226 default) the
        /// periodic scan only considers explicitly-registered structures, so no
        /// always-on disc is pooled. Explicit flows that need the full scene set
        /// (e.g. <see cref="SurfaceWorstRepair"/>) call <see cref="CollectAllDamaged"/>.
        /// </summary>
        private void CollectDamaged(List<RepairTarget> into)
        {
            if (_autoFindStructures)
            {
                CollectAllDamaged(into);
            }
            else
            {
                foreach (var go in _registered)
                {
                    if (go == null) continue;
                    var t = RepairTarget.TryWrap(go.transform);
                    if (t != null && t.IsValid && t.NeedsRepair) into.Add(t);
                }
            }
        }

        /// <summary>
        /// Scans the whole scene for damaged WallSegment / Gate / Building targets,
        /// independent of the always-on highlight suppression (DEF-226). Used by the
        /// explicit repair flow so "repair the worst structure" still works while the
        /// unprompted ground disc stays suppressed.
        /// </summary>
        private void CollectAllDamaged(List<RepairTarget> into)
        {
            AddDamagedOfType<WallSegment>(into);
            AddDamagedOfType<Gate>(into);
            AddDamagedOfType<Building>(into);
        }

        private static void AddDamagedOfType<T>(List<RepairTarget> into) where T : Component
        {
#if UNITY_2023_1_OR_NEWER
            var found = UnityEngine.Object.FindObjectsByType<T>();
#else
            var found = UnityEngine.Object.FindObjectsByType<T>();
#endif
            foreach (var c in found)
            {
                if (c == null) continue;
                var t = RepairTarget.TryWrap(c);
                if (t == null || !t.IsValid || !t.NeedsRepair) continue;
                // WO-753 ruling (owner 2026-07-19, SUPERSEDES WO-672): a DESTROYED structure is LOST
                // - it is NOT a Repair-All target (rebuild fresh at full cost). Skip anything at/over
                // the destroyed threshold so the "Repair All" offer never tries to repair-back-online
                // a destroyed WallSegment/Gate/Building.
                if (t.DamageFraction >= DestroyedFraction) continue;
                into.Add(t);
            }
        }

        private void EnsureHighlightRoot()
        {
            if (_highlightRoot != null) return;
            var go = new GameObject("RepairHighlights");
            go.transform.SetParent(transform, false);
            _highlightRoot = go.transform;
        }

        private RepairHighlight GetPooledHighlight(int index)
        {
            while (_highlightPool.Count <= index)
                _highlightPool.Add(RepairHighlight.Create(_highlightRoot));
            return _highlightPool[index];
        }

        // =====================================================================
        //  Selection — tap-to-select
        // =====================================================================

        private void HandleTap()
        {
            // A tap that the HUD repair prompt consumed (its Confirm / Cancel
            // button) is not a world tap — the bridge flags it via
            // SuppressNextWorldTap() so the same pointer-press is not also
            // raycast into the world here.
            if (Time.frameCount <= _suppressTapUntilFrame) return;

            // While the repair prompt is up it is MODAL — every tap belongs to
            // the prompt (Confirm / Cancel), never the world. This keeps the
            // interaction deterministic regardless of UI-Toolkit-vs-Update
            // event ordering: the player must use the prompt buttons. A fresh
            // world tap can only START a selection, never fight an open one.
            if (HasSelection) return;

            // WO-1708 criterion 1: a press that landed on a uGUI widget is NOT a world
            // tap. Before this guard existed the file contained ZERO references to
            // EventSystem (measured: grep -c "EventSystem" returned 0), so pressing ANY
            // on-screen button — the F8 FLAG button included — also raycast the world
            // behind it and could select/repair a structure the player never aimed at.
            if (PointerIsOverUi()) return;

            var cam = _camera != null ? _camera : Camera.main;
            if (cam == null) return;

            Vector2 screen = PointerScreenPosition();
            Ray ray = cam.ScreenPointToRay(screen);
            if (!Physics.Raycast(ray, out RaycastHit hit, 1000f, _selectableMask))
                return; // tapped empty space — nothing to select.

            var target = RepairTarget.TryWrap(hit.collider);
            if (target == null)
            {
                // NO SILENT FAILURE (CLAUDE.md section 12.2). This early return is the exact
                // path a player walks when they tap a BURNING tower / harvest site / collector
                // and nothing happens: RepairTarget wraps only WallSegment / Gate / Building,
                // so every other damageable surface taps to null here and is silently ignored.
                // Name what was actually hit so a capture distinguishes "tapped scenery" from
                // "tapped a damaged structure this controller cannot address".
                // WO-1708 criterion 2. The OLD line named only the hit object and then
                // asserted "RepairTarget covers WallSegment" — which read as a CONTRADICTION
                // every time a `Wall_*` / `Battlement_*` failed here (15 non-terrain failures
                // on the Seeker logcat). It named a symptom and no cause, so nothing captured
                // could say WHICH check refused the object. This line now dumps the hit's
                // parent chain with the components each level actually carries, which is the
                // one datum that separates the three possible causes:
                //   * chain carries NO WallSegment/Gate/Building  -> a non-repairable object
                //     sitting on the selectable mask (_selectableMask defaults to ~0, i.e.
                //     EVERY layer), e.g. Synty perimeter decor or a tower/collector;
                //   * chain is tagged Player                      -> RepairTarget's DELIBERATE
                //     hero reject (IsHeroOrPlayer), not a wrap defect;
                //   * chain DOES carry one of the three           -> a real wrap defect, and
                //     the only reading under which the old message's claim was wrong.
                // NOTE strings are built into locals first (CLAUDE.md §1: the compile gate's
                // brace scanner has no interpolated-string model, so nested quotes inside an
                // interpolation hole desynchronise its count).
                var hitGo = hit.collider != null ? hit.collider.gameObject : null;
                string hitName = hitGo != null ? hitGo.name : "<null>";
                string hitChain = DescribeHitChain(hit.collider != null ? hit.collider.transform : null, 3);
                string notRepairableLine =
                    $"tap hit '{hitName}' but RepairTarget could not wrap it - no repair prompt. " +
                    $"HIT CHAIN (up to 3 levels, hit object first): {hitChain}. " +
                    "RepairTarget.TryWrap walks GetComponentInParent<WallSegment>/<Gate>/<Building> " +
                    "(RepairTarget.cs:85-97) and rejects anything tagged Player first, so a chain " +
                    "showing NONE of those three components is a non-repairable object on the " +
                    "selectable mask - towers, harvest sites and collectors are reachable ONLY " +
                    "through Repair-All, by design.";
                FlowTrace.Throttle("Repair", "tap-not-repairable", 2f, notRepairableLine);
                return; // tapped something that is not a repairable structure.
            }

            Select(target);
        }

        /// <summary>
        /// WO-38: rescans, then surfaces the most-damaged structure's repair prompt
        /// (highest <see cref="RepairTarget.DamageFraction"/>). Returns true when a
        /// damaged structure was found and selected. The wave-clear director calls
        /// this to nudge the player to repair after surviving a wave.
        /// </summary>
        public bool SurfaceWorstRepair()
        {
            // DEF-226: scan the whole scene here regardless of the always-on
            // highlight suppression so the post-wave "repair your worst structure"
            // nudge still finds a target — this is an EXPLICIT repair request, which
            // is exactly the interaction the suppressed always-on disc is replaced by.
            var all = new List<RepairTarget>();
            CollectAllDamaged(all);
            if (all.Count == 0) return false;
            int best = 0;
            for (int i = 1; i < all.Count; i++)
                if (all[i].DamageFraction > all[best].DamageFraction) best = i;
            RequestRepair(all[best]);
            return true;
        }

        /// <summary>
        /// Selects <paramref name="target"/> for repair: marks it with the bright
        /// highlight and raises <see cref="PromptShown"/> so the HUD shows the
        /// materials cost. An undamaged structure is rejected with feedback.
        /// </summary>
        public void RequestRepair(RepairTarget target)
        {
            if (target != null && target.IsValid) Select(target);
        }

        private void Select(RepairTarget target)
        {
            if (!target.NeedsRepair)
            {
                ClearSelection();
                FlowTrace.Step("Repair", $"ignored intact structure tap: '{target.DisplayName}'.");
                return;
            }

            _selected = target;

            EnsureHighlightRoot();
            if (_selectionHighlight == null)
                _selectionHighlight = RepairHighlight.Create(_highlightRoot);
            _selectionHighlight.SetVisible(true);
            _selectionHighlight.SetSelected(true);
            _selectionHighlight.FitTo(target);

            // Re-run the scan so the pool markers no longer double up on the
            // newly-selected structure.
            Rescan();

            RaisePrompt();
        }

        /// <summary>Clears the current selection and hides the repair prompt.</summary>
        public void CancelRepair() => ClearSelection();

        private void ClearSelection()
        {
            bool had = _selected != null;
            _selected = null;
            if (_selectionHighlight != null) _selectionHighlight.SetVisible(false);
            if (had)
            {
                PromptHidden?.Invoke();
                Rescan(); // re-show the calm marker on the de-selected structure
            }
        }

        private void RaisePrompt()
        {
            if (_selected == null || !_selected.IsValid) return;
            CoreCost cost = CostFor(_selected);
            bool destroyed = _selected.DamageFraction >= DestroyedFraction;
            string name = _selected.DisplayName;
            string costText = DescribeMaterials(cost);
            CoreCost shortfall = MaterialShortfall(cost);
            string action = destroyed ? WallRepairStrings.RebuildLabel : WallRepairStrings.ConfirmLabel;
            string details = ComposePromptDetails(name, _selected.DamageFraction, costText,
                DescribeMaterials(shortfall), MaterialsZero(shortfall), action);

            // §12 — WO-1296 RECURRENCE (owner 2026-09-03: "yellow item not damaged is still
            // showing up"). Say, at the moment the prompt is raised, whether the structure it is
            // raised over shows the player ANY damage at all. NeedsRepair trips at DamageFraction
            // > 0.0001 (RepairTarget), but the first visible tell is the smolder loop at
            // HP <= damage-states.json 'smolder' (default 0.5). A prompt raised in the gap between
            // them is a prompt on a structure the player correctly reads as pristine — which is
            // the whole report, and until now nothing wrote it down. Reported, never acted on:
            // which of the two thresholds moves is an owner ruling.
            float promptHp = 1f - _selected.DamageFraction;
            float visibleAt = DamageStatesCatalog.Smolder(DamageTellKeyFor(_selected.Kind));
            if (promptHp > visibleAt)
                FlowTrace.Warn("Repair",
                    $"repair prompt raised on '{name}' while it shows NO damage tell: " +
                    $"hp={promptHp:0.000} > firstVisibleTellAtHp={visibleAt:0.00} " +
                    $"(damageFraction={_selected.DamageFraction:0.000}, cost={costText}). " +
                    "The player sees a pristine structure and is being asked to pay for it.");
            PromptShown?.Invoke(new RepairPromptInfo
            {
                StructureName = name,
                // The materials cost travels IN the sub-line (the HUD shows it
                // verbatim); destroyed rows read "Rebuild", damaged read "Repair".
                Subtitle = details,
                CrystalCost = 0,           // owner 2026-07-11: crystals never spent on repair
                CostText = costText,
                Affordable = CanAffordMaterials(cost),
                DamageFraction = _selected.DamageFraction,
                Destroyed = destroyed,
            });
        }

        /// <summary>
        /// The damage-states.json type key for a repair target's kind, so the prompt trace above
        /// reads the SAME per-type threshold StructureDamageVisuals uses rather than assuming the
        /// shared default. Keys mirror RepairAvailabilityProbe's scan exactly.
        /// </summary>
        private static string DamageTellKeyFor(RepairTargetKind kind)
        {
            switch (kind)
            {
                case RepairTargetKind.Wall: return "wall";
                case RepairTargetKind.Gate: return "gate";
                default: return "building";
            }
        }

        /// <summary>Phone-safe, complete repair copy. Newlines are intentional layout structure.</summary>
        public static string ComposePromptDetails(string name, float damageFraction, string costText,
            string shortfallText, bool affordable, string action)
        {
            int damage = Mathf.Clamp(Mathf.RoundToInt(damageFraction * 100f), 0, 100);
            int health = 100 - damage;
            string availability = affordable ? "Ready to " + action.ToLowerInvariant()
                : "Shortfall: " + shortfallText;
            return (string.IsNullOrWhiteSpace(name) ? WallRepairStrings.StructureGenericName : name) + "\n" +
                   "Health: " + health + "% | Damage: " + damage + "%\n" +
                   action + " cost: " + costText + "\n" + availability;
        }

        private static CoreCost MaterialShortfall(CoreCost cost)
        {
            var econ = EconomyService.Instance;
            return new CoreCost {
                wood = Mathf.Max(0, cost.wood - (econ != null ? econ.Wood : 0)),
                iron = Mathf.Max(0, cost.iron - (econ != null ? econ.Iron : 0)),
                stone = Mathf.Max(0, cost.stone - (econ != null ? econ.Stone : 0)),
                crystals = Mathf.Max(0, cost.crystals - (econ != null ? econ.Crystals : 0)),
            };
        }

        // =====================================================================
        //  Cost (owner ruling 2026-07-11 — in-kind materials, data-driven)
        // =====================================================================

        /// <summary>Damage fraction at/above which a structure counts as destroyed → REBUILD.</summary>
        public const float DestroyedFraction = 0.999f;

        /// <summary>The data-driven default cost row for structures with no materials row anywhere.</summary>
        private const string DefaultCostCatalogId = "repair_default";
        /// <summary>Scene-built village wall ring (never player-placed) prices as the canon wall row.</summary>
        private const string FallbackWallCatalogId = "wall_stone";
        /// <summary>Scene-built cardinal gates price as the canon gate row.</summary>
        private const string FallbackGateCatalogId = "gate_stone";

        /// <summary>
        /// Materials cost to repair <paramref name="target"/> — the structure's
        /// own catalog BUILD cost (wood/iron/food) scaled by its damage fraction.
        /// Destroyed (fraction ~1) = the full build cost = the REBUILD price.
        /// Crystals are never charged (owner 2026-07-11).
        /// </summary>
        public CoreCost CostFor(RepairTarget target)
        {
            if (target == null || !target.IsValid) return default;
            return CostForFraction(target.DamageFraction,
                BuildCostForComponent(target.Transform));
        }

        /// <summary>
        /// The same one cost authority for structures that are not RepairTarget-
        /// wrappable (towers / harvest sites / collectors — HpFraction/IsBroken
        /// surfaces): resolves <paramref name="structure"/>'s catalog build cost
        /// and scales it by <paramref name="damageFraction"/>.
        /// </summary>
        public CoreCost CostForStructure(Component structure, float damageFraction)
            => CostForFraction(damageFraction, BuildCostForComponent(structure));

        /// <summary>
        /// The one formula: per-material ceil(buildCost x damage fraction). A
        /// destroyed structure (fraction ≥ <see cref="DestroyedFraction"/>) pays
        /// the FULL build cost — that IS the rebuild option. Crystals slot is
        /// always 0 (never spent on repair). WO-676 STEWARD (Master Mason): the
        /// `repairCost` talent sum discounts the fraction here — the ONE pricing
        /// choke point every repair path (prompt, confirm, Repair-All, rebuild)
        /// already flows through. Identity at sum 0.
        /// </summary>
        public static CoreCost CostForFraction(float damageFraction, CoreCost buildCost)
        {
            float frac = Mathf.Clamp01(damageFraction);
            if (frac >= DestroyedFraction) frac = 1f;   // destroyed = full rebuild cost

            // ONE HeroTalentModifiers read (WO-676 §2b). StatSum is internally null-safe
            // (0 with no service/tree/nodes); clamped so a mis-authored node can never
            // make repairs free-negative. Applied after the destroyed normalization so
            // rebuilds are discounted the same as repairs.
            float discount = Mathf.Clamp01(DeNelle.Village.Talents.HeroTalentModifiers.StatSum(
                HeroTalentClassReader.Slug(), "repairCost"));
            if (discount > 0f)
            {
                frac *= 1f - discount;
                FlowTrace.Once("Talent", "repairCost",
                    $"repairCost -{discount:P0} applied to repair pricing (WO-676 Master Mason).");
            }

            return new CoreCost
            {
                wood     = Mathf.CeilToInt(buildCost.wood * frac),
                stone     = Mathf.CeilToInt(buildCost.stone * frac),
                iron     = Mathf.CeilToInt(buildCost.iron * frac),
                crystals = 0,   // owner 2026-07-11: crystals are never spent on repair.
                                // STILL TRUE, and deliberately so: the 2026-08-24 crystals-for-repair
                                // ruling is an OPT-IN top-up applied on top of this price (see
                                // CrystalPriceFor below), NEVER a crystal slot in the base cost. The
                                // ordinary WO-947 basket is untouched by that carve-out, and
                                // CostBasketSeparationRegression [repair-carve-out] fails if it stops
                                // being true.
            };
        }

        // =====================================================================
        //  CRYSTALS FOR REPAIR - a NAMED CARVE-OUT from WO-947. PROD-014 slice (d).
        // ---------------------------------------------------------------------
        //  WO-947 separates the baskets by what a structure IS: regular structures are
        //  BUILT and UPGRADED with wood + iron, magical ones with crystals. The owner
        //  AMENDED that on 2026-08-24 -- "REPAIR may be paid in crystals for anything" --
        //  and set the rate on 2026-08-26: 1.0 CRYSTAL PER IRON.
        //
        //  THIS IS A CARVE-OUT, NOT A LOOSENING, and the shape is what keeps it one:
        //    * The base price stays IN KIND. CostForFraction still emits crystals = 0 at
        //      every fraction, so no repair, rebuild or Repair-All sweep can put crystals
        //      into an ordinary basket on its own.
        //    * Crystals only ever enter as a TOP-UP the player chooses, covering exactly
        //      the part of the price the wallet cannot -- and only through this one method.
        //    * The rate is AUTHORED IN DATA on the 'repair_default' row and on no other row
        //      (structures-catalog.json repo.repairCrystalsPer). No C# literal, so it cannot
        //      be re-tuned in code, and no structure row can grow a rate of its own.
        //  CostBasketSeparationRegression's [repair-carve-out] case pins all three.
        //
        //  ZERO MEANS NOT CONVERTIBLE, NEVER FREE. The owner ruled ONE number, for iron.
        //  perWood/perStone are 0, so a wood-short repair simply cannot be paid in crystals
        //  and says so. Inventing rates for them would be economy policy, which is exactly
        //  why this slice sat blocked -- and a zero read as "costs nothing" would be the
        //  free-repair exploit MaterialsZero was fixed to close.
        // =====================================================================

        /// <summary>
        /// The measured NATURAL EXCHANGE FLOOR in crystals per iron: the best real cross-rate a
        /// player can already get, the $1.99 impulse rung (0.625). The owner's ruling prices the
        /// repair top-up ABOVE this on purpose -- crystals are a convenience for the player who
        /// has none, never a discount for the player who has iron. Named here so the regression
        /// that guards the ruled rate compares against a cited number rather than a magic one.
        /// </summary>
        public const float NaturalExchangeFloorCrystalsPerIron = 0.625f;

        /// <summary>
        /// The authored crystals-per-material rates, read off the 'repair_default' catalog row.
        /// All-zero when the row or the field is missing -- which disables the carve-out entirely
        /// rather than defaulting to a guess, and is warned once so a missing row is visible.
        /// </summary>
        public static RepairCrystalRate CrystalRate()
        {
            var entry = CatalogRegistry.Get(DefaultCostCatalogId);
            var repo = entry != null ? entry.repo : null;
            if (repo == null || repo.repairCrystalsPer.IsZero)
            {
                FlowTrace.Warn("Repair",
                    $"crystals-for-repair DISABLED - catalog row '{DefaultCostCatalogId}' authors no " +
                    "repairCrystalsPer rate. A refused repair can only be paid in materials until the " +
                    "data row is restored (PROD-014 slice d).");
                return default;
            }
            return repo.repairCrystalsPer;
        }

        /// <summary>
        /// The crystal price of a MATERIALS shortfall, at the authored rate. Pure and rate-injected
        /// so a regression can price a shortfall without a live catalog.
        /// <para><paramref name="convertible"/> is false when some part of the shortfall has NO
        /// authored rate -- the caller must then refuse rather than quietly charge for the rest,
        /// which would repair a structure the player cannot actually pay for.</para>
        /// </summary>
        public static CoreCost CrystalPriceFor(CoreCost shortfall, RepairCrystalRate rate, out bool convertible)
        {
            int wood = Mathf.Max(0, shortfall.wood);
            int food = Mathf.Max(0, shortfall.stone);
            int iron = Mathf.Max(0, shortfall.iron);

            convertible = (wood == 0 || rate.perWood > 0f)
                       && (food == 0 || rate.perStone > 0f)
                       && (iron == 0 || rate.perIron > 0f);

            float crystals = wood * Mathf.Max(0f, rate.perWood)
                           + food * Mathf.Max(0f, rate.perStone)
                           + iron * Mathf.Max(0f, rate.perIron);
            // Ceil, like every other repair price: the house never rounds a shortfall down to free.
            return new CoreCost { crystals = Mathf.CeilToInt(crystals) };
        }

        /// <summary>The crystal price of a shortfall at the LIVE authored rate.</summary>
        public static CoreCost CrystalPriceFor(CoreCost shortfall, out bool convertible)
            => CrystalPriceFor(shortfall, CrystalRate(), out convertible);

        /// <summary>Per-slot materials the wallet cannot cover for <paramref name="cost"/>. All-zero = affordable.</summary>
        public CoreCost ShortfallFor(CoreCost cost)
        {
            var econ = EconomyService.Instance;
            int wood = econ != null ? econ.Wood : 0;
            int food = econ != null ? econ.Stone : 0;
            int iron = econ != null ? econ.Iron : 0;
            return new CoreCost
            {
                wood = Mathf.Max(0, cost.wood - wood),
                stone = Mathf.Max(0, cost.stone - food),
                iron = Mathf.Max(0, cost.iron - iron),
                crystals = 0,
            };
        }

        /// <summary>
        /// <paramref name="cost"/> re-expressed as ONE cost the player can actually pay: every
        /// material the wallet covers stays IN KIND, and only the shortfall becomes crystals. The
        /// result goes through the SAME <see cref="SpendMaterials"/> path as any other repair, so
        /// it is a SINGLE atomic EconomyService.TrySpend and there is no second repair economy.
        /// <para>False = this repair cannot be paid in crystals (no authored rate for what is
        /// missing, or not enough crystals). Nothing is spent and nothing is repaired.</para>
        /// </summary>
        public bool TryBlendWithCrystals(CoreCost cost, out CoreCost blended, out string why)
        {
            blended = cost;
            why = null;

            var shortfall = ShortfallFor(cost);
            if (MaterialsZero(shortfall)) return true;   // affordable in kind; no crystals involved

            var price = CrystalPriceFor(shortfall, out bool convertible);
            if (!convertible)
            {
                why = "no crystal exchange rate is authored for " + DescribeMaterials(shortfall);
                FlowTrace.Step("Repair", $"crystal top-up REFUSED - {why} (PROD-014 slice d: an " +
                                         "unauthored rate is NOT CONVERTIBLE, never free).");
                return false;
            }

            blended = new CoreCost
            {
                wood     = cost.wood - shortfall.wood,
                stone     = cost.stone - shortfall.stone,
                iron     = cost.iron - shortfall.iron,
                crystals = cost.crystals + price.crystals,
            };

            if (!CanAffordMaterials(blended))
            {
                why = "short " + DescribeMaterials(shortfall) + " = " + price.crystals +
                      " crystals, and the crystal wallet does not cover it";
                FlowTrace.Step("Repair", $"crystal top-up REFUSED - {why}; wallet={WalletLine()}");
                return false;
            }

            FlowTrace.Step("Repair",
                $"crystal top-up: short {DescribeMaterials(shortfall)} -> {price.crystals} crystals; " +
                $"blended price {DescribeMaterials(blended)} (ONE TrySpend, WO-947 repair carve-out).");
            return true;
        }

        /// <summary>
        /// Repair-All, with the part of each price the wallet cannot cover paid in CRYSTALS at the
        /// authored rate (owner rulings 2026-08-24 + 2026-08-26). Identical to
        /// <see cref="RepairAll"/> in every other respect -- same worst-first sweep, same items,
        /// same single spend path -- because it IS that sweep with the blend switched on, not a
        /// second repair system. An item whose shortfall has no authored rate, or whose blended
        /// price exceeds the crystal wallet, is skipped exactly like an unaffordable one.
        /// </summary>
        public (int repairedCount, CoreCost spent, int remainingDamaged) TryRepairAllWithCrystals()
            => RepairAllInternal(payShortfallInCrystals: true);

        /// <summary>
        /// Resolves a structure's BUILD cost in materials from where that cost
        /// actually lives, in precedence order:
        ///   1. a PlacedStructure parent → its own catalog row (the id placement charged);
        ///   2. a Building → the structures-catalog row matching Building.BuildingId
        ///      (workshop / market / jeweler / forge / mill / lumbermill / arcane-tower /
        ///      pet-house — buildings.json authors crystalCost ONLY, so it cannot
        ///      supply materials and is not consulted);
        ///   3. a ResourceCollector → the Collector row whose repo.collectorBuildingId
        ///      matches (collector_farm / collector_lumbermill / collector_forge);
        ///   4. a scene-built WallSegment / Gate (never Occupy()'d, no PlacedStructure)
        ///      → the canon wall_stone / gate_stone rows;
        ///   5. anything else (runtime stations absent from every catalog — the
        ///      Apothecary case — and harvest sites) → the data-driven
        ///      'repair_default' catalog row (dual-copy structures-catalog.json).
        /// A row whose materials (wood/iron/food) are all zero does not count —
        /// resolution falls through (crystals-only rows can't price a repair).
        /// </summary>
        private static CoreCost BuildCostForComponent(Component structure)
        {
            if (structure == null) return DefaultBuildCost();

            var ps = structure.GetComponentInParent<PlacedStructure>();
            if (ps != null)
            {
                var c = MaterialsFromEntry(CatalogRegistry.Get(ps.itemId), out bool ok);
                if (ok) return c;
            }

            var building = structure.GetComponentInParent<Building>();
            if (building != null && !string.IsNullOrEmpty(building.BuildingId))
            {
                var c = MaterialsFromEntry(CatalogRegistry.Get(building.BuildingId), out bool ok);
                if (ok) return c;
            }

            var collector = structure.GetComponentInParent<ResourceCollector>();
            if (collector != null && !string.IsNullOrEmpty(collector.BuildingId))
            {
                foreach (var e in CatalogRegistry.OfType(CatalogType.Collector))
                {
                    if (e == null) continue;
                    string cid = e.repo != null && !string.IsNullOrEmpty(e.repo.collectorBuildingId)
                        ? e.repo.collectorBuildingId : e.id;
                    if (cid != collector.BuildingId) continue;
                    var c = MaterialsFromEntry(e, out bool ok);
                    if (ok) return c;
                }
            }

            if (structure.GetComponentInParent<WallSegment>() != null)
            {
                var c = MaterialsFromEntry(CatalogRegistry.Get(FallbackWallCatalogId), out bool ok);
                if (ok) return c;
            }
            if (structure.GetComponentInParent<Gate>() != null)
            {
                var c = MaterialsFromEntry(CatalogRegistry.Get(FallbackGateCatalogId), out bool ok);
                if (ok) return c;
            }

            return DefaultBuildCost();
        }

        /// <summary>
        /// The materials slice (wood/iron/food — crystals dropped per the ruling)
        /// of a catalog row's build cost. <paramref name="found"/> is false when
        /// the row is missing or authors no materials (crystals-only rows).
        /// </summary>
        private static CoreCost MaterialsFromEntry(CatalogEntry entry, out bool found)
        {
            found = false;
            var repo = entry != null ? entry.repo : null;
            if (repo == null) return default;
            var c = new CoreCost
            {
                wood = repo.cost.wood,
                stone = repo.cost.stone,
                iron = repo.cost.iron,
                crystals = 0,
            };
            found = c.wood > 0 || c.stone > 0 || c.iron > 0;
            return c;
        }

        /// <summary>
        /// The 'repair_default' catalog row's materials (data-driven, dual-copy
        /// structures-catalog.json). An emergency in-code constant only if the
        /// row is missing — warned, never silent.
        /// </summary>
        private static CoreCost DefaultBuildCost()
        {
            var c = MaterialsFromEntry(CatalogRegistry.Get(DefaultCostCatalogId), out bool found);
            if (found) return c;
            FlowTrace.Warn("Repair",
                $"catalog row '{DefaultCostCatalogId}' missing/material-less — " +
                "using the emergency in-code default (30 wood, 15 iron). Restore the data row.");
            return new CoreCost { wood = 30, iron = 15 };
        }

        /// <summary>
        /// True when EVERY wallet slot is zero — a genuinely free repair.
        ///
        /// ⛔ CRYSTALS ARE COUNTED, and that is the whole point of this method. It is not a
        /// display predicate: <see cref="SpendMaterials"/> and <see cref="CanAffordMaterials"/>
        /// both EARLY-RETURN TRUE on it, and two Repair-All sweeps skip the spend entirely when
        /// it holds. While it ignored the crystals slot, a crystals-only cost read as "free" and
        /// would have been GRANTED WITHOUT SPENDING ANYTHING — affordable regardless of wallet,
        /// charged nothing, repaired anyway.
        ///
        /// That was harmless only for as long as crystals could never appear on a repair. The
        /// owner ruled on 2026-08-24 that crystals ARE a universal repair currency (WO-947
        /// amendment, PROD-014), so the day that ruling lands in pricing, this predicate becomes
        /// a free-repair exploit. Fixed in the SAME pass, deliberately, rather than left as a
        /// landmine for the pricing change to step on.
        ///
        /// ⚠ The spend rail itself was always ready: BuildModeController.ToEconomy maps all
        /// four slots (<c>new ResourceCost(wood, food, iron, crystals)</c>) and
        /// EconomyService.CanAfford/TrySpend take crystals. ONLY this zero-check and
        /// DescribeMaterials were blind to them.
        /// </summary>
        public static bool MaterialsZero(CoreCost c)
            => c.wood == 0 && c.stone == 0 && c.iron == 0 && c.crystals == 0;

        /// <summary>
        /// Player-facing materials list, e.g. "12 wood, 4 iron" (skips zero slots;
        /// plain copy, no glyphs — tofu rule). "nothing" for an all-zero cost.
        /// </summary>
        public static string DescribeMaterials(CoreCost c)
        {
            // WO-697: currency amounts render through the ONE kit formatter
            // (ElarionUi.CompactNumber — verbatim below 10k, "98.6k"/"1.2m" above),
            // so a six-digit rebuild price can never clip a banner/prompt line.
            var parts = DeNelle.Core.UI.CostFormat.Parts(new[] { ("wood", "Wood", c.wood), ("iron", "Iron", c.iron), ("stone", "Stone", c.stone), ("crystal", "Crystals", c.crystals) });
            // ⚠ Crystals MUST be listed for the same reason MaterialsZero must count them:
            // without this line a crystals-only cost renders as "nothing" in the player's own
            // prompt WHILE BEING CHARGED. A price the UI calls nothing is worse than a wrong
            // price — the player cannot even dispute it. (PROD-014, owner ruling 2026-08-24.)
            return parts.Count > 0 ? DeNelle.Core.UI.CostFormat.Words(parts) : "nothing";
        }

        // =====================================================================
        //  WO-672 Slice E — Repair All (the damage-report CTA)
        // =====================================================================

        /// <summary>One repairable item in the Repair-All sweep (uniform view).</summary>
        private struct RepairAllItem
        {
            public string Name;
            public GameObject Target;
            public float DamageFraction;
            public CoreCost Cost;     // in-kind materials. ⚠ The "crystals slot always 0" note here was
                                      // retired 2026-08-24: the owner ruled crystals ARE a universal repair
                                      // currency (WO-947 amendment, PROD-014). MaterialsZero now counts them.
            public Action Fix;        // the structure's full-restore path (REP-1: RepairFull, not a magnitude)
            public Func<float> FractionNow; // live re-read of the damage fraction (post-fix proof line)
        }

        /// <summary>
        /// Materials cost of repairing EVERYTHING currently damaged (walls / gates /
        /// buildings via <see cref="CostFor"/>, towers / harvest sites / collectors
        /// via <see cref="CostForStructure"/> — every one priced from its own
        /// catalog row). The damage-report CTA shows this total; all-zero = clean.
        /// </summary>
        public CoreCost RepairAllCost()
        {
            var total = default(CoreCost);
            foreach (var item in CollectRepairAllSet())
            {
                total.wood += item.Cost.wood;
                total.stone += item.Cost.stone;
                total.iron += item.Cost.iron;
            }
            return total;
        }

        /// <summary>
        /// WO-672 Slice E + owner ruling 2026-07-11: repairs every damaged structure
        /// WORST-FIRST, spending in-kind materials through the SAME construction-
        /// economy path build-mode placement charges (<see cref="SpendMaterials"/> →
        /// EconomyService.TrySpend + the GameState Wood/Iron mirror — never a second
        /// wallet path). Greedy per-resource affordability: an unaffordable item is
        /// skipped (a cheaper, less-damaged one later in the sweep may still fit;
        /// partial repair is honest). Raises <see cref="FeedbackShown"/> with the
        /// summary (the existing HUD toast surface).
        /// Returns (repairedCount, spentMaterials, remainingDamaged).
        /// </summary>
        public (int repairedCount, CoreCost spent, int remainingDamaged) RepairAll()
            => RepairAllInternal(payShortfallInCrystals: false);

        /// <summary>One-time onboarding assistance invoked only by the plans Echo offer.
        /// It uses the same repair target set and fix delegates as paid repair, but does
        /// not touch the wallet. The caller owns the persisted once-ever entitlement.</summary>
        public int RepairAllComplimentary(string reason)
        {
            var items = CollectRepairAllSet();
            int repaired = 0;
            foreach (var item in items)
            {
                var fix = item.Fix;
                if (!Guard.Try("Repair", $"complimentary fix '{item.Name}'", () => fix?.Invoke()))
                    continue;
                repaired++;
                FlowTrace.Step("Repair",
                    $"complimentary Echo repair: '{item.Name}' restored from dmg " +
                    $"{item.DamageFraction:0.00}; spent NOTHING ({reason})");
            }
            Rescan();
            return repaired;
        }

        /// <summary>
        /// The ONE Repair-All sweep. <paramref name="payShortfallInCrystals"/> is the PROD-014
        /// slice (d) carve-out: with it on, each item's price is blended through
        /// <see cref="TryBlendWithCrystals"/> before the same single spend. Written as one method
        /// with a flag rather than two sweeps on purpose -- a second copy of this loop is a second
        /// repair economy, and the WO's own constraint is "no second repair system".
        /// </summary>
        private (int repairedCount, CoreCost spent, int remainingDamaged) RepairAllInternal(bool payShortfallInCrystals)
        {
            var items = CollectRepairAllSet();
            items.Sort((a, b) => b.DamageFraction.CompareTo(a.DamageFraction));   // worst-first
            FlowTrace.Step("Repair", $"RepairAll: {items.Count} damaged, wallet={WalletLine()}");

            int repaired = 0, remaining = 0;
            var spent = default(CoreCost);
            foreach (var item in items)
            {
                bool rebuild = item.DamageFraction >= DestroyedFraction;

                // PROD-014 slice (d): the price the player actually pays. Without the carve-out
                // this IS item.Cost, byte for byte - the blend only ever moves the UNAFFORDABLE
                // part into crystals, and only when the caller asked for it.
                var price = item.Cost;
                if (payShortfallInCrystals && !MaterialsZero(item.Cost) &&
                    !TryBlendWithCrystals(item.Cost, out price, out string why))
                {
                    remaining++;
                    FlowTrace.Step("Repair",
                        $"RepairAll(crystals): SKIPPED '{item.Name}' (dmg {item.DamageFraction:0.00}) - " +
                        $"cost {DescribeMaterials(item.Cost)}, {why}");
                    continue;
                }

                if (!MaterialsZero(price) && !SpendMaterials(price,
                        (rebuild ? "rebuild " : "repair ") + item.Name))
                {
                    remaining++;
                    FlowTrace.Step("Repair",
                        $"RepairAll: SKIPPED '{item.Name}' (dmg {item.DamageFraction:0.00}) — " +
                        $"cost {DescribeMaterials(price)} unaffordable, wallet={WalletLine()}");
                    continue;
                }
                var fix = item.Fix;
                Guard.Try("Repair", $"RepairAll fix '{item.Name}'", () => fix?.Invoke());
                repaired++;
                spent.wood += price.wood;
                spent.stone += price.stone;
                spent.iron += price.iron;
                spent.crystals += price.crystals;
                // REP-1 post-fix state: re-read the live fraction AFTER the fix ran —
                // a paid repair that leaves damage on the structure is a logged line.
                float postFrac = item.FractionNow != null ? item.FractionNow() : -1f;
                FlowTrace.Step("Repair",
                    $"RepairAll: {(rebuild ? "REBUILT" : "repaired")} '{item.Name}' " +
                    $"(dmg {item.DamageFraction:0.00} -> post-fix {postFrac:0.00}) " +
                    $"for {DescribeMaterials(price)}");
            }

            FlowTrace.Step("Repair",
                $"RepairAll SUMMARY: repaired={repaired} spent={DescribeMaterials(spent)} " +
                $"remaining={remaining} wallet={WalletLine()}");
            if (repaired > 0)
                FeedbackShown?.Invoke(remaining > 0
                    ? $"Repaired {repaired} structures for {DescribeMaterials(spent)} - {remaining} still damaged (out of materials)"
                    : $"Repaired {repaired} structures for {DescribeMaterials(spent)}", false);
            else if (items.Count > 0)
                FeedbackShown?.Invoke("Not enough materials to repair anything", true);

            ClearSelection();
            Rescan();
            return (repaired, spent, remaining);
        }

        /// <summary>
        /// DIAGNOSTIC SEAM (read-only, no mutation) - a one-line description of everything
        /// the Repair-All sweep currently considers repairable, as
        /// "name(dmg=frac,cost); ...". Exists because "there is no option to repair" is
        /// ambiguous from the outside: it can mean the backend never offered the structure
        /// (a LOGIC gap) or that it offered it and no surface showed it (a UI gap). Reading
        /// this line next to a burning structure's name settles which, with no guessing.
        /// Used by <see cref="RepairAvailabilityProbe"/>; safe to call at any time.
        /// </summary>
        public string DescribeRepairAllSet()
        {
            var items = CollectRepairAllSet();
            if (items.Count == 0) return "<empty - nothing is currently repairable>";
            var sb = new System.Text.StringBuilder();
            foreach (var item in items)
            {
                if (sb.Length > 0) sb.Append("; ");
                sb.Append(item.Name).Append("(dmg=").Append(item.DamageFraction.ToString("0.00"))
                  .Append(',').Append(DescribeMaterials(item.Cost)).Append(')');
            }
            return sb.ToString();
        }

        // =====================================================================
        //  WO-811 — single-worst-target seams (the Echo repair task's backend)
        // =====================================================================

        /// <summary>
        /// WO-811: peeks the single MOST-DAMAGED repairable structure (highest damage
        /// fraction in the same <see cref="CollectRepairAllSet"/> sweep Repair-All uses,
        /// so destroyed structures are already excluded — the WO-753 skip is inherited,
        /// never re-implemented). Read-only: no spend, no fix, no selection/prompt.
        /// Returns false with zeroed outs when nothing is repairable.
        /// The Echo repair consumer (EchoRepairService) reads this to pace its work
        /// budget against the worst target BEFORE committing a spend.
        /// </summary>
        public bool TryPeekWorstDamaged(out string name, out float damageFraction, out CoreCost cost)
        {
            return TryPeekWorstDamaged(out name, out damageFraction, out cost, out _);
        }

        /// <summary>Target-bearing overload for truthful world-space repair feedback.</summary>
        public bool TryPeekWorstDamaged(out string name, out float damageFraction,
                                        out CoreCost cost, out GameObject target)
        {
            var items = CollectRepairAllSet();
            if (items.Count == 0)
            {
                name = ""; damageFraction = 0f; cost = default; target = null;
                return false;
            }
            int best = 0;
            for (int i = 1; i < items.Count; i++)
                if (items[i].DamageFraction > items[best].DamageFraction) best = i;
            name = items[best].Name;
            damageFraction = items[best].DamageFraction;
            cost = items[best].Cost;
            target = items[best].Target;
            return true;
        }

        /// <summary>
        /// WO-811: repairs ONLY the single most-damaged structure — the Echo repair
        /// task's completion primitive (MOST-DAMAGED-FIRST is the documented priority
        /// choice; it matches the RepairAll worst-first sort and WO-701's triage
        /// instinct). Prices from the item's own catalog row (the same
        /// <see cref="CollectRepairAllSet"/> costing every hand-repair path uses,
        /// talent discount included) and spends through the SAME construction-economy
        /// path (<see cref="SpendMaterials"/> → EconomyService.TrySpend) — never a
        /// second wallet, never free hitpoints. Returns false (logged, never silent)
        /// when nothing is damaged or the wallet cannot cover the cost.
        /// </summary>
        public bool TryRepairWorst(string reason, out string repairedName, out float repairedFraction, out CoreCost spent)
        {
            repairedName = ""; repairedFraction = 0f; spent = default;

            var items = CollectRepairAllSet();
            if (items.Count == 0)
            {
                FlowTrace.Step("Repair", $"TryRepairWorst({reason}): nothing damaged — no-op.");
                return false;
            }
            int best = 0;
            for (int i = 1; i < items.Count; i++)
                if (items[i].DamageFraction > items[best].DamageFraction) best = i;
            var item = items[best];

            if (!MaterialsZero(item.Cost) && !SpendMaterials(item.Cost, reason + " '" + item.Name + "'"))
            {
                // SpendMaterials already traced the refusal (cost > wallet) — add the caller context.
                FlowTrace.Step("Repair",
                    $"TryRepairWorst({reason}): SKIPPED '{item.Name}' (dmg {item.DamageFraction:0.00}) — " +
                    $"cost {DescribeMaterials(item.Cost)} unaffordable, wallet={WalletLine()}");
                return false;
            }

            var fix = item.Fix;
            Guard.Try("Repair", $"TryRepairWorst fix '{item.Name}'", () => fix?.Invoke());
            repairedName = item.Name;
            repairedFraction = item.DamageFraction;
            spent = item.Cost;

            // REP-1 post-fix proof line: re-read the live fraction AFTER the fix ran.
            float postFrac = item.FractionNow != null ? item.FractionNow() : -1f;
            FlowTrace.Step("Repair",
                $"TryRepairWorst({reason}): repaired '{item.Name}' " +
                $"(dmg {item.DamageFraction:0.00} -> post-fix {postFrac:0.00}) " +
                $"for {DescribeMaterials(item.Cost)}; wallet={WalletLine()}");
            Rescan();
            return true;
        }

        /// <summary>Compact wallet line for FlowTrace (in-session EconomyService pools).</summary>
        private static string WalletLine()
        {
            var econ = EconomyService.Instance;
            if (econ == null) return "<no EconomyService>";
            return $"W{econ.Wood} I{econ.Iron} S{econ.Stone}";
        }

        /// <summary>
        /// The full damaged set as uniform Repair-All items: the RepairTarget scan
        /// (walls / gates / buildings — <see cref="CollectAllDamaged"/>, the same
        /// set the single-repair flow sees), the WO-672 Slice A tower surface
        /// (Tower / DefenseTower / ArcaneTower / HarvestSite: HpFraction / IsBroken
        /// / Repair()), and resource collectors. Owner ruling 2026-07-11: EVERY
        /// item is priced from its own catalog row's materials (collectors are no
        /// longer free — their Collector row authors a real build cost).
        /// </summary>
        private List<RepairAllItem> CollectRepairAllSet()
        {
            var items = new List<RepairAllItem>();

            var targets = new List<RepairTarget>();
            CollectAllDamaged(targets);
            foreach (var t in targets)
            {
                if (t == null || !t.IsValid || !t.NeedsRepair) continue;
                var tc = t;
                items.Add(new RepairAllItem
                {
                    Name = t.DisplayName,
                    Target = t.GameObject,
                    DamageFraction = t.DamageFraction,
                    Cost = CostFor(t),
                    Fix = () => tc.RepairFull(),   // REP-1: full BY CONTRACT (a fixed 100f under-repaired 120..240-MaxHp buildings), same as ConfirmRepair
                    FractionNow = () => tc.DamageFraction,
                });
            }

            // WO-753 ruling (owner 2026-07-19, SUPERSEDES WO-672's repair-back-online): a DESTROYED
            // (IsBroken) tower / spire / harvest site / collector is LOST - it is NOT a Repair-All
            // target; it returns ONLY via a full-cost build-mode placement. Skip every broken one so
            // the offer only lists still-standing DAMAGED structures.
            foreach (var t in UnityEngine.Object.FindObjectsByType<Tower>(FindObjectsSortMode.None))
            {
                if (t == null || t.IsBroken) continue;
                AddHpItem(items, t.gameObject.name, t, t.HpFraction, t.IsBroken, t.Repair,
                    () => t.IsBroken ? 1f : 1f - Mathf.Clamp01(t.HpFraction));
            }
            foreach (var t in UnityEngine.Object.FindObjectsByType<DefenseTower>(FindObjectsSortMode.None))
            {
                if (t == null || t.IsBroken) continue;
                AddHpItem(items, t.gameObject.name, t, t.HpFraction, t.IsBroken, t.Repair,
                    () => t.IsBroken ? 1f : 1f - Mathf.Clamp01(t.HpFraction));
            }
            foreach (var t in UnityEngine.Object.FindObjectsByType<ArcaneTower>(FindObjectsSortMode.None))
            {
                if (t == null || t.IsBroken) continue;
                AddHpItem(items, t.gameObject.name, t, t.HpFraction, t.IsBroken, t.Repair,
                    () => t.IsBroken ? 1f : 1f - Mathf.Clamp01(t.HpFraction));
            }
            foreach (var t in UnityEngine.Object.FindObjectsByType<DeNelle.Village.World.HarvestSite>(FindObjectsSortMode.None))
            {
                if (t == null || t.IsBroken) continue;
                AddHpItem(items, t.gameObject.name, t, t.HpFraction, t.IsBroken, t.Repair,
                    () => t.IsBroken ? 1f : 1f - Mathf.Clamp01(t.HpFraction));
            }

            foreach (var c in ResourceCollectorRegistry.All)
            {
                if (c == null || c.IsBroken) continue;
                float frac = 1f - Mathf.Clamp01(c.HpFraction);
                if (frac <= 0.0001f) continue;
                var cc = c;
                items.Add(new RepairAllItem
                {
                    Name = c.BuildingId,
                    Target = c.gameObject,
                    DamageFraction = frac,
                    // Priced from its Collector catalog row (was free pre-ruling).
                    Cost = CostForStructure(c, frac),
                    Fix = () => cc.Repair(),
                    FractionNow = () => cc.IsBroken ? 1f : 1f - Mathf.Clamp01(cc.HpFraction),
                });
            }

            return items;
        }

        /// <summary>Adds one costed item for an HpFraction/IsBroken structure when damaged
        /// — priced from the structure's own catalog row via <see cref="CostForStructure"/>.</summary>
        private void AddHpItem(List<RepairAllItem> items, string name, Component structure,
            float hpFraction, bool broken, Action repair, Func<float> fractionNow)
        {
            float frac = broken ? 1f : 1f - Mathf.Clamp01(hpFraction);
            if (frac <= 0.0001f) return;
            items.Add(new RepairAllItem
            {
                Name = name,
                Target = structure != null ? structure.gameObject : null,
                DamageFraction = frac,
                Cost = CostForStructure(structure, frac),
                Fix = repair,
                FractionNow = fractionNow,
            });
        }

        // =====================================================================
        //  Confirm — spend materials + repair (owner ruling 2026-07-11)
        // =====================================================================

        /// <summary>
        /// Confirms the repair/rebuild of the selected structure. Checks the
        /// material wallets, spends the in-kind cost through the construction
        /// economy (<see cref="SpendMaterials"/> — the SAME EconomyService path
        /// build-mode placement charges), calls <see cref="RepairTarget.RepairFull"/>
        /// (full restore BY CONTRACT, REP-1) and raises success feedback.
        /// On a shortfall it raises an insufficient-materials message and leaves
        /// the prompt up so the player can cancel. Bound to the HUD prompt's
        /// Confirm button by the scene-setup editor file.
        /// </summary>
        public void ConfirmRepair()
        {
            if (_selected == null || !_selected.IsValid)
            {
                ClearSelection();
                return;
            }

            if (!_selected.NeedsRepair)
            {
                FlowTrace.Step("Repair", $"repair confirmation became a no-op because '{_selected.DisplayName}' is intact.");
                ClearSelection();
                return;
            }

            CoreCost cost = CostFor(_selected);
            bool rebuild = _selected.DamageFraction >= DestroyedFraction;
            if (!CanAffordMaterials(cost))
            {
                FeedbackShown?.Invoke(
                    string.Format(WallRepairStrings.InsufficientFormat, DescribeMaterials(cost)), true);
                // Keep the prompt up but refresh affordability so the HUD greys
                // the confirm button.
                RaisePrompt();
                return;
            }

            if (!SpendMaterials(cost, (rebuild ? "rebuild " : "repair ") + _selected.DisplayName))
            {
                FeedbackShown?.Invoke(
                    string.Format(WallRepairStrings.InsufficientFormat, DescribeMaterials(cost)), true);
                RaisePrompt();
                return;
            }

            // REP-1: full repair BY CONTRACT — RepairFull resolves each structure's
            // own full-restore magnitude. The old hardcoded Repair(100f) assumed
            // every structure was 0..100-scaled; buildings.json authors MaxHp
            // 120..240, so a paid repair left buildings visibly damaged.
            _selected.RepairFull();

            FeedbackShown?.Invoke(
                rebuild ? WallRepairStrings.RebuiltMessage : WallRepairStrings.SuccessMessage, false);
            ClearSelection();
            Rescan();
        }

        // =====================================================================
        //  Materials wallet — THE construction-economy spend path (WO-131 seam)
        // =====================================================================

        /// <summary>
        /// True when the material wallets cover <paramref name="cost"/> — the
        /// SAME EconomyService.CanAfford gate build-mode placement validates
        /// against (BuildModeController.CanAfford). A free cost is affordable.
        /// </summary>
        public bool CanAffordMaterials(CoreCost cost)
        {
            if (MaterialsZero(cost)) return true;
            var econ = EconomyService.Instance;
            return econ != null && econ.CanAfford(BuildModeController.ToEconomy(cost));
        }

        /// <summary>
        /// Spends an in-kind materials cost through the SAME construction-economy
        /// path build-mode placement charges: <see cref="EconomyService.TrySpend"/>
        /// (the atomic multi-resource debit BuildModeController.ChargeLedger
        /// drives at placement, WO-131). WO-842: TrySpend now debits the SINGLE
        /// GameState-backed wallet (GameState.Wood/Iron — the same fields
        /// ResourceLedger spends) and persists+announces it itself, so the old
        /// dual-wallet DEBIT mirror this method carried is GONE — re-debiting
        /// GameState here would charge the repair TWICE. FlowTrace on every
        /// spend + fail — no silent path.
        /// </summary>
        private bool SpendMaterials(CoreCost cost, string what)
        {
            if (MaterialsZero(cost)) return true;

            var econ = EconomyService.Instance;
            if (econ == null)
            {
                FlowTrace.Fail("Repair",
                    $"spend BLOCKED for '{what}' — EconomyService absent (cost {DescribeMaterials(cost)})");
                return false;
            }

            if (!econ.TrySpend(BuildModeController.ToEconomy(cost)))
            {
                FlowTrace.Step("Repair",
                    $"spend REFUSED for '{what}' — cost {DescribeMaterials(cost)} > wallet {WalletLine()}");
                return false;
            }

            FlowTrace.Step("Repair",
                $"SPENT {DescribeMaterials(cost)} on '{what}' (EconomyService.TrySpend, unified GameState wallet WO-842; wallet now {WalletLine()})");
            return true;
        }

        // =====================================================================
        //  Input — LEGACY Input Manager (workstream constraint)
        // =====================================================================

        private static bool TapPressedThisFrame()
        {
            if (Input.touchCount > 0)
            {
                Touch t = Input.GetTouch(0);
                return t.phase == TouchPhase.Began;
            }
            return Input.GetMouseButtonDown(0);
        }

        private static Vector2 PointerScreenPosition()
        {
            if (Input.touchCount > 0) return Input.GetTouch(0).position;
            return Input.mousePosition;
        }

        /// <summary>
        /// WO-1708 criterion 1 — true when this frame's pointer press is over a uGUI
        /// widget, so <see cref="HandleTap"/> must not also raycast the world behind it.
        ///
        /// TOUCH USES THE fingerId OVERLOAD, and that is not a style choice. Unity's
        /// EventSystem reference states, verbatim: "If you use IsPointerOverGameObject()
        /// without a parameter, it points to the 'left mouse button' (pointerId = -1);
        /// therefore when you use IsPointerOverGameObject for touch, you should consider
        /// passing a pointerId to it Note that for touch, IsPointerOverGameObject should be
        /// used with ''OnMouseDown()'' or ''Input.GetMouseButtonDown(0)'' or
        /// ''Input.GetTouch(0).phase == TouchPhase.Began''." The parameterless form would
        /// therefore answer about a mouse that does not exist on the Seeker and return
        /// false for every real finger — i.e. no guard at all on the one platform that
        /// produced this ticket. The documented call site is the TouchPhase.Began frame,
        /// which is exactly where <see cref="TapPressedThisFrame"/> gates this path.
        ///
        /// A null EventSystem means no uGUI event system is present at all, so nothing
        /// could have consumed the press: returns false (world tap allowed) rather than
        /// silently swallowing every tap in a scene that has no UI.
        /// </summary>
        private static bool PointerIsOverUi()
        {
            var es = EventSystem.current;
            if (es == null) return false;

            if (Input.touchCount > 0)
            {
                Touch t = Input.GetTouch(0);
                if (!es.IsPointerOverGameObject(t.fingerId)) return false;
                string touchLine =
                    $"world tap IGNORED - the press landed on UI (touch, fingerId={t.fingerId}, " +
                    $"screen {t.position.x:F0},{t.position.y:F0}). No world raycast, no repair " +
                    "selection. WO-1708 criterion 1: before this guard the same press was ALSO " +
                    "raycast into the world behind the button.";
                FlowTrace.Throttle("Repair", "tap-over-ui", 2f, touchLine);
                return true;
            }

            if (!es.IsPointerOverGameObject()) return false;
            Vector3 m = Input.mousePosition;
            string mouseLine =
                $"world tap IGNORED - the press landed on UI (mouse, pointerId=-1, " +
                $"screen {m.x:F0},{m.y:F0}). No world raycast, no repair selection. " +
                "WO-1708 criterion 1.";
            FlowTrace.Throttle("Repair", "tap-over-ui", 2f, mouseLine);
            return true;
        }

        /// <summary>
        /// WO-1708 criterion 2 — renders <paramref name="t"/> and up to
        /// <paramref name="levels"/> of its ancestors as
        /// <c>[0] name tag=Untagged {BoxCollider, MeshRenderer} &lt;- [1] ...</c>, so a
        /// capture can say WHY <see cref="RepairTarget.TryWrap"/> refused a hit instead of
        /// only that it did. Transform is omitted from every level (it is on all of them
        /// and carries no signal); a destroyed-script slot renders as
        /// <c>&lt;missing script&gt;</c> because a broken component reference is itself a
        /// candidate cause. Public + static so a regression can assert the shape without a
        /// controller instance or a play session.
        /// </summary>
        public static string DescribeHitChain(Transform t, int levels)
        {
            if (t == null) return "<null>";
            if (levels < 1) levels = 1;

            var sb = new System.Text.StringBuilder();
            int level = 0;
            for (var cur = t; cur != null && level < levels; cur = cur.parent, level++)
            {
                if (level > 0) sb.Append(" <- ");
                sb.Append("[").Append(level).Append("] ").Append(cur.name);
                sb.Append(" tag=").Append(cur.tag).Append(" ").Append("{");

                var comps = cur.GetComponents<Component>();
                bool wroteOne = false;
                for (int i = 0; i < comps.Length; i++)
                {
                    var c = comps[i];
                    // A null slot is a MISSING SCRIPT, not an empty one — report it.
                    string typeName = c == null ? "<missing script>" : c.GetType().Name;
                    if (c is Transform) continue;
                    if (wroteOne) sb.Append(", ");
                    sb.Append(typeName);
                    wroteOne = true;
                }
                if (!wroteOne) sb.Append("none");
                sb.Append("}");
            }
            return sb.ToString();
        }

        /// <summary>
        /// Tells the controller to ignore world taps for the current + next
        /// frame. The HUD bridge calls this when the repair prompt's Confirm /
        /// Cancel button consumes a pointer-press, so that same OS click is not
        /// also raycast into the world (UI Toolkit and this MonoBehaviour both
        /// see the one click). A two-frame window covers same-frame and
        /// next-frame ordering between the UI event and Update().
        /// </summary>
        public void SuppressNextWorldTap()
        {
            _suppressTapUntilFrame = Time.frameCount + 1;
        }
    }
}

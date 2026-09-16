// =============================================================================
// StructureCardVM — the PURE projection of ONE catalog structure (MVVM Silo C).
// -----------------------------------------------------------------------------
// Assembly: DeNelle.Village   Namespace: DeNelle.Village
//
// Strict-MVVM migration (UI_MVVM_MIGRATION_PLAN.md §1, Silo C): the shared
// ViewModel behind a build-palette CARD (BuildPaletteUI) AND the Structure Info
// preview (BuildStructureInfoPanel). ALL the cost / affordability / footprint /
// DPS-tier math that used to live in those two Views moves HERE, so the Views
// become dumb skins that render from these read-only fields (ui-mvvm rule).
//
// PURE C# — references only Core.Catalog data + the Village cost seams
// (BuildModeController static cost readers + IEconomy). Never a GameObject /
// Image / Sprite / RectTransform. The View resolves art from the exposed
// id/displayName/type (a Resources look-up is presentation, not state) and
// raises the existing OnEntrySelected/OnCardTapped events off <see cref="Entry"/>
// (the CatalogEntry is pure Core data, not a scene object).
//
// Behaviour is PRESERVED verbatim (§2c): the cost reader is the ONE
// BuildModeController.EffectiveCostFor seam every surface already agrees with,
// the next-tier math mirrors BuildStructureInfoPanel.RenderNextTierPreview, and
// the footprint mirrors its FootprintLabel. The View keeps its OWN cost-string
// formatter (pure presentation) and reads <see cref="EffectiveCost"/> +
// <see cref="Freebie"/> from here.
// =============================================================================

using System.Collections.Generic;
using UnityEngine;
using DeNelle.Core.Catalog;
using DeNelle.Core.Diagnostics;   // FlowTrace — the footprint label must be able to report FAILURE (§12 / §1.4b)
using CoreCost = DeNelle.Core.Catalog.ResourceCost;

namespace DeNelle.Village
{
    /// <summary>
    /// Read-only projection of a single <see cref="CatalogEntry"/> for the build palette
    /// card + the Structure Info preview. Constructed with an injected <see cref="IEconomy"/>
    /// and an explicit freebie flag (the sole game-state read stays in <see cref="CreateForEntry"/>),
    /// so it is unit-testable without a scene.
    /// </summary>
    public sealed class StructureCardVM
    {
        /// <summary>One current-tier stat row for the info panel (key + formatted value).</summary>
        public readonly struct StatRow
        {
            public readonly string Key;
            public readonly string Value;
            public StatRow(string key, string value) { Key = key; Value = value; }
        }

        /// <summary>The source def (pure Core data). The View raises OnEntrySelected/OnCardTapped
        /// off this and resolves the card art from it — it NEVER re-queries the catalog/economy.</summary>
        public CatalogEntry Entry { get; }

        public string Id { get; }
        public string DisplayName { get; }

        // ── Shared (palette card + info panel) ───────────────────────────────
        /// <summary>
        /// WO-1013 -- true when this card is VISIBLE-BUT-LOCKED (build-categories
        /// 'visibleLockedIds' with its persisted unlock flag still down). The card renders
        /// with its NORMAL cost plus <see cref="LockReason"/> in words, and can never be
        /// armed/placed while locked. A different axis from the hidden lockedIds filter.
        /// </summary>
        public bool Locked { get; }
        /// <summary>The lock reason IN WORDS (e.g. "Recover the plans"); null when not locked.
        /// Words carry the state -- never colour alone (colorblind law).</summary>
        public string LockReason { get; }

        /// <summary>True while the entry's first-build freebie is live (card/info shows "FREE").</summary>
        public bool Freebie { get; }
        /// <summary>The cost the player actually pays — freebie-aware (default/zero when free). The
        /// ONE BuildModeController.EffectiveCostFor value every build surface agrees with.</summary>
        public CoreCost EffectiveCost { get; }
        /// <summary>Whether the player can currently afford <see cref="EffectiveCost"/>.</summary>
        public bool Affordable { get; }

        /// <summary>Palette short targeting caption ("Land only"/"Land + Air"/"Air only"), or null (non-tower).</summary>
        public string TargetingTag { get; }
        /// <summary>Info-panel targeting line ("Targets: Land only" ...), or null (non-tower).</summary>
        public string TargetingLine { get; }

        // ── Info-panel only ──────────────────────────────────────────────────
        public int MaxLevel { get; }
        /// <summary>"Lv 1" or "Lv 1 / N".</summary>
        public string TierBadge { get; }
        public string Description { get; }
        /// <summary>Footprint in grid cells, e.g. "2x2 cells" (falls back to "1x1 cells"). Computed
        /// LAZILY on first read — the palette never reads it, so it never pays the measure cost;
        /// only the info panel does.</summary>
        public string FootprintLabel
        {
            get
            {
                if (!_footprintComputed) { _footprintLabel = FootprintFor(Entry); _footprintComputed = true; }
                return _footprintLabel;
            }
        }
        private string _footprintLabel;
        private bool _footprintComputed;

        /// <summary>
        /// WO-1425 — the cap-block note for <see cref="EffectiveCost"/>: "" unless some component of
        /// this cost is MORE THAN THE TOWN BANK CAN HOLD, in which case it names the container, the
        /// level and the capacity that unblocks it. The pill renders this UNDER the price so an
        /// unaffordable card explains WHICH kind of unaffordable it is.
        ///
        /// <para>An affordable card never has one (a cost you can pay is by definition one the bank
        /// held), so this is checked only when <see cref="Affordable"/> is false — the palette does
        /// not pay a capacity walk per card for the common case.</para>
        ///
        /// <para>LAZY for the same reason <see cref="FootprintLabel"/> is: the capacity read walks
        /// GameState.BaseLayout, and the palette builds many cards per open. Wrapped in Guard so a
        /// null-service or catalog-less context can never blank a card (§12 — never a silent catch:
        /// Guard logs through FlowTrace.Fail).</para>
        ///
        /// <para>NOTE on this VM's stated purity: the ctor takes an injected IEconomy so it is
        /// unit-testable without a scene. This property reaches TownBankCapacity (static, and
        /// GameState-reading) instead, which is a deliberate, documented exception — with no
        /// GameStateService it resolves to the base cap and cannot throw. It is NOT injected because
        /// the whole point of WO-1425 is that capacity has exactly ONE reader.</para>
        /// </summary>
        public string CapBlockNote
        {
            get
            {
                if (!_capNoteComputed)
                {
                    _capNote = !Affordable
                        ? Guard.Try("Build", "StructureCardVM cap-block note",
                            () => BuildModeController.CapBlockMessage(EffectiveCost) ?? "", "")
                        : "";
                    _capNoteComputed = true;
                }
                return _capNote;
            }
        }
        private string _capNote;
        private bool _capNoteComputed;

        /// <summary>WO-1425 — the same note for <see cref="NextTierCost"/>, so the info panel's
        /// upgrade preview cannot advertise a tier whose price the bank can never hold without
        /// saying so. "" when there is no next tier or the cost fits.</summary>
        public string NextTierCapBlockNote
        {
            get
            {
                if (!_nextCapNoteComputed)
                {
                    _nextCapNote = HasNextTier
                        ? Guard.Try("Build", "StructureCardVM next-tier cap-block note",
                            () => BuildModeController.CapBlockMessage(NextTierCost) ?? "", "")
                        : "";
                    _nextCapNoteComputed = true;
                }
                return _nextCapNote;
            }
        }
        private string _nextCapNote;
        private bool _nextCapNoteComputed;
        /// <summary>Current-tier stat rows (DPS / Range / Fire Rate — or a single "Type" row).</summary>
        public IReadOnlyList<StatRow> CurrentStats => _currentStats;
        private readonly List<StatRow> _currentStats = new List<StatRow>();

        public bool HasNextTier { get; }
        public string NextTierTitle { get; }
        public string NextTierStats { get; }
        /// <summary>The next-tier upgrade cost (the View formats it with its own cost formatter).</summary>
        public CoreCost NextTierCost { get; }

        // =====================================================================
        //  WO-1570 - WHAT A DETAIL LAYOUT NEEDS, SPELLED BY THE MODEL.
        //
        //  The captured defect (Logs/device/screens/owner-screen-20260907-004903.png)
        //  is a detail card printing "2600   970" with no resource words and an
        //  upgrade button truncated to "UPGRADE . STONE 2600 GOL...". That capture is
        //  the MANAGE detail (ManageVmProjection.cs:306 / ManageScreenVM.cs:4859) and
        //  its own model already carries the words - its View drops them. This block
        //  is the same shape for THIS VM's consumer (BuildStructureInfoPanel), which
        //  today formats the basket itself at BuildStructureInfoPanel.cs:347-350 with
        //  hardcoded ("stone", "Stone", c.stone) literals. A View re-spelling a
        //  resource name is how one screen ends up naming a slot differently from the
        //  wallet chip beside it.
        //
        //  STOP - NO WORD IS TYPED HERE. Every resource name comes from
        //  DeNelle.Core.Economy.TownBankCapacity.DisplayName(BankResource), the one
        //  authority the HUD chips, the bank and the cap-block copy already read -
        //  so "Stone" for the Food slot (canon sec.7; WO-1416 retired FOOD and the same
        //  persisted slot now holds Stone) is stated in exactly one place. A literal
        //  here would be a fifth copy of a word this repo has already paid for
        //  (CLAUDE.md sec.2 / sec.5 / sec.16, the duplicated-state failures).
        //
        //  STOP - NO NUMBER IS RE-DERIVED. The amounts are EffectiveCost / NextTierCost,
        //  which are BuildModeController's own values; the seconds come from the
        //  SAME two steps BuildTimerService.StartUpgrade runs (tier = targetLevel - 2,
        //  then the config curve) - grace deliberately not applied, because
        //  GraceAdjustedDurationMs returns early for upgrades.
        // =====================================================================

        /// <summary>One (label, current, next) row for an upgrade preview table.</summary>
        public readonly struct UpgradeStatRow
        {
            public readonly string Label;
            public readonly string Current;
            public readonly string Next;
            public UpgradeStatRow(string label, string current, string next)
            { Label = label; Current = current; Next = next; }
        }

        /// <summary>
        /// The level this projection describes. A palette / info-panel card is a
        /// PRE-PLACEMENT projection of a catalog row, so it is always 1 - the same
        /// fact <see cref="TierBadge"/> already spells ("Lv 1" / "Lv 1 / N"). Exposed
        /// as a number so a layout never has to parse that badge back apart.
        /// A PLACED structure's live level is a different authority
        /// (PlacedStructureUpgradeService / GameState.BaseLayout) and is NOT this.
        /// </summary>
        public int CurrentLevel => 1;

        /// <summary>The paid basket in (resource word, amount) rows - empty while the
        /// first-build freebie is live, which is the WO-1010 D20 no-price rule.</summary>
        public IReadOnlyList<DeNelle.Core.UI.CostPart> EffectiveCostParts => _effectiveCostParts;
        private readonly IReadOnlyList<DeNelle.Core.UI.CostPart> _effectiveCostParts;

        /// <summary>The next-tier basket in (resource word, amount) rows; empty when
        /// there is no next tier.</summary>
        public IReadOnlyList<DeNelle.Core.UI.CostPart> NextTierCostParts => _nextTierCostParts;
        private readonly IReadOnlyList<DeNelle.Core.UI.CostPart> _nextTierCostParts;

        /// <summary>Current-vs-next stat rows for the upgrade preview. Same numbers
        /// <see cref="NextTierStats"/> spells as one string - built once, read two ways,
        /// so a table and a sentence can never disagree.</summary>
        public IReadOnlyList<UpgradeStatRow> UpgradeStats => _upgradeStats;
        private readonly List<UpgradeStatRow> _upgradeStats = new List<UpgradeStatRow>();

        /// <summary>Seconds the Lv1 -&gt; Lv2 upgrade will actually run; 0 when the timer
        /// service is absent, in which case the layout omits the term rather than
        /// quoting a fiction (the same rule <see cref="PlacementBuildSeconds"/> follows).</summary>
        public int UpgradeSeconds { get; }

        /// <summary>The wait in words ("57s"), or "" when unknown.</summary>
        public string UpgradeTimeText { get; }

        /// <summary>The upgrade control's face. ONE WORD: the button is a fixed-width
        /// slot and the captured defect is a label that ran past its own edge because
        /// the price was concatenated into it. The price belongs on
        /// <see cref="NextTierCostParts"/>, beside the button, not inside it.</summary>
        public string UpgradeButtonLabel => "UPGRADE";

        /// <summary>The one game-state-touching factory (audit §3.1): resolves the economy handle +
        /// the freebie flag itself, then builds the pure projection. Views call THIS (they never
        /// name EconomyService / BuildModeController).</summary>
        public static StructureCardVM CreateForEntry(CatalogEntry entry)
            => new StructureCardVM(entry, EconomyService.Instance, BuildModeController.FreeBuildAvailable(entry));

        // =====================================================================
        //  WO-1411 — WHAT CAN I ACTUALLY BUILD BEHIND THIS DOOR?
        //
        //  The merged UI review (REVIEW_MERGED.md row 10): eight category cards and
        //  "no card says what is affordable". The player opens a door, reads five
        //  prices they cannot pay, and backs out — the whole browse costs them taps
        //  and tells them nothing.
        //
        //  ⛔ NO NEW AFFORDABILITY RULE IS INVENTED HERE. This is a FOLD over the two
        //  authorities that already decide what one card says:
        //    • BuildCollectionBrowser.IsCollectionItemVisible — the ONE offer authority
        //      (its own summary says so), so the count can never include a row the
        //      browser would not draw;
        //    • this VM's own Affordable/Locked, i.e. the SAME projection the item card
        //      renders, so the subtitle's number and the cards behind it are one fact.
        //  A second predicate here is exactly how a door promises two builds and opens
        //  onto five "Unaffordable" cards.
        //
        //  Built singletons are excluded for the same reason CollectionHasVisibleItems
        //  excludes them: a structure whose one allowed instance already stands is not
        //  a build choice, however affordable it is.
        // =====================================================================
        // Fully qualified rather than a file-wide `using DeNelle.Core;`: this file already
        // aliases CoreCost to disambiguate DeNelle.Core.Catalog, and opening a second
        // namespace for ONE parameter type is how an ambiguous-reference build break arrives
        // in a file nobody edited.
        public static int AffordableCount(DeNelle.Core.CardCollectionDefinition collection)
        {
            if (collection?.Items == null) return 0;
            int affordable = 0;
            foreach (var item in collection.Items)
            {
                if (item == null || !BuildCollectionBrowser.IsCollectionItemVisible(item.ItemId)) continue;
                var entry = CatalogRegistry.Get(item.ItemId);
                if (entry == null) continue;
                // WO-1572: IsPlayerBuilt, not IsBuilt. This count is the CATEGORY CARD's
                // subtitle and BuildCollectionBrowser.cs:186-190 states it must fold the same
                // authorities as the item cards behind that door. Those two sites moved off
                // the twin-counting predicate, so leaving this one on IsBuilt would promise
                // "nothing you can afford" over a door full of buildable rows.
                if (StructureSingleton.IsSingleton(entry.id) && StructureSingleton.IsPlayerBuilt(entry)) continue;
                bool progressionLocked = !string.IsNullOrEmpty(RewardedProgression.LockReasonFor(item.ItemId)) &&
                                         !ProgressionUnlocks.IsUnlocked(item.ItemId);
                var vm = new StructureCardVM(entry, EconomyService.Instance,
                    BuildModeController.FreeBuildAvailable(entry), progressionLocked,
                    RewardedProgression.LockReasonFor(item.ItemId));
                if (vm.Affordable && !vm.Locked) affordable++;
            }
            return affordable;
        }

        // =====================================================================
        //  WO-1411 — THE PLACEMENT SUMMARY: what this build costs, how long it takes,
        //  and whether a builder is free. MODEL-SIDE, and that is the RULING, not a
        //  preference: the UI-MVVM conformance oracle failed BuildPreviewModal for
        //  reading GameStateService directly (canon 9 — a View never touches game
        //  state, and derived text is model work). The View now paints ONE string it
        //  is handed.
        //
        //  ⚠ THIS WIDENS THE VM'S STATE READ, deliberately and in one place. The class
        //  header says "the sole game-state read stays in CreateForEntry" — that is now
        //  two seams, this one and that one, both HERE in the model. The alternative
        //  (leaving the read in the modal) is the violation the oracle just caught.
        //
        //  ⛔ NOT ONE NUMBER IS INVENTED OR RE-DERIVED. Each term comes from the seam
        //  that already owns it, so this line can never quote a price or a wait the
        //  placement then contradicts:
        //    • COST — FreeBuildAvailable + SoftcappedCostFor (the SOFTCAPPED value: it is
        //      what the ghost actually charges), spelled by the ONE shared CostFormat.
        //      While the first-build freebie is live the price slot shows NOTHING —
        //      owner ruling WO-1010 D20, the same rule the collection card obeys.
        //    • TIME — tier from CostFor (the INTRINSIC weight: a freebie does not make a
        //      build instant and the softcap surcharge does not stretch the timer), then
        //      the config curve, then GraceAdjustedDurationMs under GraceReasonFor — the
        //      exact three steps BuildModeController runs at commit. Quoting the raw
        //      curve would print minutes over a build the FTUE finishes in seconds, and a
        //      hardcoded duration would be a guess wearing a suffix.
        //    • CREW — free Builder slots = SlotCount(Builder) minus its active jobs.
        //
        //  ASCII separator (" . "): a typed middle-dot renders as tofu on the shipped TMP
        //  atlas — the same landmine the build HUD's ASCII rule records.
        // =====================================================================
        public readonly struct PlacementSummary
        {
            /// <summary>The paid basket in words; empty while the first-build freebie is live.</summary>
            public readonly string CostWords;
            /// <summary>The wait this placement will actually run, grace included; 0 = unknown.</summary>
            public readonly int Seconds;
            /// <summary>Crew availability in words (never a bare ratio).</summary>
            public readonly string CrewWords;
            /// <summary>The finished line the View paints, terms joined and empties dropped.</summary>
            public readonly string Line;

            public PlacementSummary(string costWords, int seconds, string crewWords, string line)
            { CostWords = costWords; Seconds = seconds; CrewWords = crewWords; Line = line; }
        }

        public static PlacementSummary PlacementSummaryFor(CatalogEntry entry)
        {
            string costWords = PlacementCostWords(entry);
            int seconds = PlacementBuildSeconds(entry);
            string crewWords = PlacementCrewWords();

            var terms = new List<string>();
            if (!string.IsNullOrEmpty(costWords)) terms.Add(costWords);
            if (seconds > 0) terms.Add(DeNelle.Core.UI.ElarionUi.Duration(seconds));
            if (!string.IsNullOrEmpty(crewWords)) terms.Add(crewWords);
            return new PlacementSummary(costWords, seconds, crewWords,
                                        string.Join(" . ", terms.ToArray()));
        }

        private static string PlacementCostWords(CatalogEntry entry)
        {
            if (entry == null) return string.Empty;
            if (BuildModeController.FreeBuildAvailable(entry)) return string.Empty;
            var c = BuildModeController.SoftcappedCostFor(entry);
            // WO-1570: was a hand-typed ("stone", "Stone", c.stone) tuple - the fifth copy of a
            // word the bank already spells. Re-pointed at PartsOf, which reads
            // TownBankCapacity.DisplayName. Output is IDENTICAL (Wood/Stone/Iron/Crystals both
            // ways), which matters: CostBasketSeparationRegression.cs:662 reads CostWords.
            return DeNelle.Core.UI.CostFormat.Words(PartsOf(c));
        }

        /// <summary>0 when the timer service is absent — the line then omits the term rather
        /// than quoting a fiction.</summary>
        private static int PlacementBuildSeconds(CatalogEntry entry)
        {
            var svc = BuildTimerService.Instance;
            if (entry == null || svc == null || svc.Config == null) return 0;
            int tier = svc.Config.TierForCost(BuildModeController.CostFor(entry));
            double ms = svc.Config.DurationSecondsForTier(tier, BuildJobKind.Build) * 1000.0;

            var state = DeNelle.Core.State.GameStateService.Instance != null
                ? DeNelle.Core.State.GameStateService.Instance.State : null;
            bool firstEverBuild = state == null || !state.HasEverBuilt(entry.id);
            bool notYetOnboarded = state != null && !state.Onboarded;
            bool isPallet = DeNelle.Core.Economy.TownBankCapacity.IsStorageContainer(entry.repo);
            var grace = BuildModeController.GraceReasonFor(firstEverBuild, notYetOnboarded, isPallet);
            ms = BuildTimerService.GraceAdjustedDurationMs(ms, grace, false, svc.Config.firstBuildSeconds);
            return Mathf.Max(0, Mathf.RoundToInt((float)(ms / 1000.0)));
        }

        /// <summary>WORDS, not "0 / 2": a bare ratio leaves the player to work out whether that
        /// is good news. The wait itself is already the term before this one.</summary>
        private static string PlacementCrewWords()
        {
            var svc = BuildTimerService.Instance;
            if (svc == null) return string.Empty;
            int slots = svc.SlotCount(DeNelle.Core.Jobs.ChannelId.Builder);
            var active = svc.ActiveJobsOf(DeNelle.Core.Jobs.ChannelId.Builder);
            int free = Mathf.Max(0, slots - (active != null ? active.Count : 0));
            return free > 0 ? "Builder free" : "Builders busy - joins the queue";
        }

        /// <summary>
        /// WO-1411 — the count IN PLAYER ENGLISH, owned by the VM so the browser card and
        /// any oracle read the same sentence. Never a bare number and never a colour: the
        /// state is the WORDS (colorblind law), and "nothing affordable yet" says the door
        /// is still worth remembering rather than reading as an error.
        /// </summary>
        public static string AffordabilityWords(int affordable)
        {
            if (affordable <= 0) return "nothing affordable yet";
            return affordable == 1 ? "1 you can build now" : affordable + " you can build now";
        }

        public StructureCardVM(CatalogEntry entry, IEconomy economy, bool freebie,
            bool locked = false, string lockReason = null)
        {
            // WO-1013: a locked card always shows its REAL cost (the aspiration is the
            // point), so the caller passes freebie=false for locked rows -- belt-and-braces
            // here too, because a zero "FREE" cost on a locked card would contradict both
            // the D20 no-FREE rule and the "normal cost displayed" acceptance line.
            if (locked) freebie = false;
            Locked = locked;
            LockReason = locked ? lockReason : null;

            Entry = entry;
            Id = entry != null ? entry.id : null;
            DisplayName = entry != null && !string.IsNullOrEmpty(entry.displayName) ? entry.displayName
                        : (entry != null ? entry.id : "");

            var repo = entry != null ? entry.repo : null;

            Freebie = freebie;
            // WO-855 Phase 1: the palette card / info panel is the price the player reads BEFORE
            // arming, so it must carry the tower-spam softcap or the ghost would reject
            // CannotAfford at a number this card never showed. SoftcappedCostFor (not
            // EffectiveCostFor) so the INJECTED `freebie` argument stays the single freebie
            // authority for this projection -- ResolveCost below was a private duplicate of
            // BuildModeController.CostFor and could not see the multiplier.
            EffectiveCost = freebie ? default : BuildModeController.SoftcappedCostFor(entry);
            Affordable = ComputeAffordable(economy, EffectiveCost);

            TargetingTag = TargetingTagFor(entry);
            TargetingLine = TargetingLineFor(entry);

            // Same ceiling BuildModeController.MaxLevelFor clamps to -- read off the ONE named
            // constant, never a literal, or the shop card advertises "Lv 1 / 3" for a container
            // the upgrade verb will happily take to 6 (WO-966).
            MaxLevel = repo == null ? 1 : Mathf.Clamp(repo.maxLevel, 1, DeNelle.Core.Catalog.RepoProps.MaxStructureLevel);
            TierBadge = MaxLevel > 1 ? "Lv 1 / " + MaxLevel : "Lv 1";
            Description = DescriptionFor(entry);

            BuildCurrentStats(entry, repo);

            // Next-tier preview (mirrors BuildStructureInfoPanel.RenderNextTierPreview) —
            // hidden for single-tier entries.
            if (repo == null || MaxLevel <= 1)
            {
                HasNextTier = false;
                NextTierTitle = null;
                NextTierStats = null;
                NextTierCost = default;
                UpgradeSeconds = 0;
                UpgradeTimeText = string.Empty;
            }
            else
            {
                HasNextTier = true;
                NextTierTitle = "Upgrade to Lv 2";
                const float l2Mul = 1.25f;   // matches BuildModeController.ApplyTierStats (L2 x1.25)
                var parts = new List<string>(3);
                if (repo.damage > 0f)
                {
                    float dps1 = repo.damage * (repo.fireRate > 0f ? repo.fireRate : 1f);
                    float dps2 = (repo.damage * l2Mul) * (repo.fireRate > 0f ? repo.fireRate : 1f);
                    _upgradeStats.Add(new UpgradeStatRow("DPS", FormatNum(dps1), FormatNum(dps2)));
                }
                if (repo.range > 0f)
                    _upgradeStats.Add(new UpgradeStatRow("Range", FormatNum(repo.range) + "m",
                                                         FormatNum(repo.range * l2Mul) + "m"));
                // The sentence is DERIVED from the rows, never authored a second time -
                // a table and a summary line that are typed separately drift.
                for (int i = 0; i < _upgradeStats.Count; i++)
                    parts.Add(_upgradeStats[i].Label + " " + _upgradeStats[i].Current
                              + " -> " + _upgradeStats[i].Next);
                // Nothing MEASURED to compare: the sentence stays, and UpgradeStats stays
                // EMPTY rather than inventing two player-facing words for a table row.
                // Empty is honest; fabricated copy is the owner's call, not this file's.
                if (parts.Count == 0)
                    parts.Add("Sturdier — higher durability tier");
                NextTierStats = string.Join("\n", parts);
                NextTierCost = BuildModeController.UpgradeCostFor(entry, 1);
                UpgradeSeconds = UpgradeSecondsFor();
                UpgradeTimeText = UpgradeSeconds > 0
                    ? DeNelle.Core.UI.ElarionUi.Duration(UpgradeSeconds) : string.Empty;
            }

            _effectiveCostParts = PartsOf(EffectiveCost);
            _nextTierCostParts = HasNextTier ? PartsOf(NextTierCost)
                                             : System.Array.Empty<DeNelle.Core.UI.CostPart>();

            // CLAUDE.md sec.12 - the upgrade line NAMES ITS KEYS AND THEIR SOURCE, once per row. The
            // captured defect was read as "a retired resource key reached the button";
            // proving which keys a basket actually carries, and which file authored them,
            // is a single log read instead of a catalog hunt. Fires only for a row that
            // HAS an upgrade rung, so the palette does not narrate 40 single-tier cards.
            if (HasNextTier && Id != null)
                FlowTrace.Once("Build", "upgrade-basket-" + Id,
                    "UPGRADE BASKET '" + Id + "' Lv1->Lv2 keys=[" + KeysOf(NextTierCost)
                    + "] words=[" + DeNelle.Core.UI.CostFormat.Words(_nextTierCostParts)
                    + "] seconds=" + UpgradeSeconds
                    + " | SOURCE: BuildModeController.UpgradeCostFor(entry,1) took the "
                    + (AuthoredFirstStep(entry)
                        ? "AUTHORED step - structures-catalog.json row 'repo.upgradeCost[0]' "
                          + "(both canonical twins)"
                        : "FALLBACK SCALER - this row authors no usable upgradeCost[0], so the "
                          + "basket is CostFor(entry) x fromLevel (BuildModeController.cs:2807-2828), "
                          + "NOT an authored table")
                    + "; words from TownBankCapacity.DisplayName. NOTE the building-tiers.json "
                    + "PERK ladder is a DIFFERENT authority with a DIFFERENT charge rule "
                    + "(BuildingTierChargeLane: T1 Wood / T2 Stone / T3+ Iron by tier NUMBER, "
                    + "owner ruling 22) - do not read one to explain the other.");
        }

        /// <summary>
        /// The basket as (word, amount) rows. The concept ids stay the lowercase tokens
        /// UiStyle.Icon already resolves; the WORDS come from the bank's own DisplayName
        /// so no surface re-spells a resource. Zero terms are dropped by CostFormat.Parts.
        /// </summary>
        private static IReadOnlyList<DeNelle.Core.UI.CostPart> PartsOf(CoreCost c)
        {
            return DeNelle.Core.UI.CostFormat.Parts(new[]
            {
                ("wood",    Word(DeNelle.Core.Economy.BankResource.Wood),     c.wood),
                ("stone",   Word(DeNelle.Core.Economy.BankResource.Stone),     c.stone),
                ("iron",    Word(DeNelle.Core.Economy.BankResource.Iron),     c.iron),
                ("crystal", Word(DeNelle.Core.Economy.BankResource.Crystals), c.crystals)
            });
        }

        private static string Word(DeNelle.Core.Economy.BankResource r)
            => DeNelle.Core.Economy.TownBankCapacity.DisplayName(r);

        /// <summary>
        /// True when UpgradeCostFor's AUTHORED branch supplies the L1-&gt;L2 step - the exact
        /// condition at BuildModeController.cs:2812-2816 (array present, index in range, step
        /// not all-zero). Mirrored rather than guessed, because a trace that names the wrong
        /// authoring file is worse than no trace: 'healing_caravan' (maxLevel 3, no array)
        /// takes the scaler, and BuildEconomyRegression :1193 records that as the ruled shape.
        /// </summary>
        private static bool AuthoredFirstStep(CatalogEntry e)
        {
            var repo = e != null ? e.repo : null;
            return repo != null && repo.upgradeCost != null && repo.upgradeCost.Length > 0
                   && !repo.upgradeCost[0].IsZero;
        }

        /// <summary>The NON-ZERO cost keys of a basket, for the trace. Names the keys the
        /// data actually carries - the question the capture could not answer.</summary>
        private static string KeysOf(CoreCost c)
        {
            var keys = new List<string>(4);
            if (c.wood > 0) keys.Add("wood=" + c.wood);
            if (c.stone > 0) keys.Add("food=" + c.stone);
            if (c.iron > 0) keys.Add("iron=" + c.iron);
            if (c.crystals > 0) keys.Add("crystals=" + c.crystals);
            return keys.Count == 0 ? "none" : string.Join(",", keys.ToArray());
        }

        /// <summary>
        /// The Lv1 -&gt; Lv2 wait, run through the SAME two steps BuildTimerService.StartUpgrade
        /// runs: tier = max(0, targetLevel - 2) (BuildTimerService.cs:662-664), then the config
        /// curve for BuildJobKind.Upgrade. Grace is deliberately NOT applied -
        /// GraceAdjustedDurationMs returns the duration unchanged for an upgrade.
        /// 0 when the service is absent, so the layout omits the term.
        /// </summary>
        private static int UpgradeSecondsFor()
        {
            var svc = BuildTimerService.Instance;
            if (svc == null || svc.Config == null) return 0;
            const int targetLevel = 2;                       // this VM previews Lv1 -> Lv2 only
            int tier = Mathf.Max(0, targetLevel - 2);
            float seconds = svc.Config.DurationSecondsForTier(tier, BuildJobKind.Upgrade);
            return Mathf.Max(0, Mathf.RoundToInt(seconds));
        }

        private void BuildCurrentStats(CatalogEntry entry, RepoProps repo)
        {
            if (repo == null) return;
            bool any = false;
            float dps = repo.damage * (repo.fireRate > 0f ? repo.fireRate : 1f);
            if (repo.damage > 0f) { _currentStats.Add(new StatRow("DPS", FormatNum(dps))); any = true; }
            if (repo.range  > 0f) { _currentStats.Add(new StatRow("Range", FormatNum(repo.range) + "m")); any = true; }
            if (repo.fireRate > 0f) { _currentStats.Add(new StatRow("Fire Rate", FormatNum(repo.fireRate) + "/s")); any = true; }
            if (!any && entry != null)
                _currentStats.Add(new StatRow("Type", entry.type.ToString()));
        }

        // ── Pure helpers (ported verbatim from the two Views) ────────────────

        // RETIRED (WO-855): the private ResolveCost duplicate of BuildModeController.CostFor
        // lived here and was the reason this card could not see the tower softcap. The ctor
        // now calls BuildModeController.SoftcappedCostFor -- the ONE resolver -- directly.

        private static bool ComputeAffordable(IEconomy economy, CoreCost cost)
        {
            if (economy != null) return economy.CanAfford(BuildModeController.ToEconomy(cost));
            // Service-less fallback: a free (all-zero) cost is affordable, otherwise not.
            return cost.IsZero;
        }

        /// <summary>Compact palette targeting caption from the repo flags, or null for non-towers.</summary>
        private static string TargetingTagFor(CatalogEntry e)
        {
            if (e == null || e.type != CatalogType.Tower) return null;
            var repo = e.repo;
            if (repo == null) return null;
            bool airOnly = repo.airOnly;
            bool canHitAir = repo.canHitAir || airOnly;
            if (airOnly) return "Air only";
            if (canHitAir) return "Land + Air";
            return "Land only";
        }

        /// <summary>Info-panel targeting line from the repo flags, or null for non-towers.</summary>
        private static string TargetingLineFor(CatalogEntry e)
        {
            if (e == null || e.type != CatalogType.Tower) return null;
            var repo = e.repo;
            if (repo == null) return null;
            bool airOnly = repo.airOnly;
            bool canHitAir = repo.canHitAir || airOnly;
            if (airOnly) return "Targets: Air only";
            if (canHitAir) return "Targets: Land + Air";
            return "Targets: Land only";
        }

        public static string DescriptionFor(CatalogEntry e)
        {
            if (e == null) return string.Empty;
            if (!string.IsNullOrWhiteSpace(e.description)) return e.description;
            // WO-1565: the type-level prose that used to live here is DELETED, not moved.
            // It painted a plausible sentence for any unauthored row, which is exactly why
            // the defect survived a capture: every Tower row rendered one identical sentence
            // about auto-firing on enemies in range, so the Catapult (a siege engine) and the
            // Sky Ballista (anti-air) both described themselves as a generic defence tower.
            // A fallback that looks right is worse than no fallback (CLAUDE.md sec.12, no silent
            // failures): the unauthored case is now a DATA DEFECT that BuildEconomyRegression's
            // [structure-descriptions] check FAILS on, so it cannot reach a build at all.
            // Empty is a value every consumer already sees (the e == null branch above).
            FlowTrace.Once("Build", "desc-unauthored-" + e.id,
                $"description UNAUTHORED id={e.id} type={e.type} -- author CatalogEntry.description " +
                "in structures-catalog.json (BOTH canonical copies). No fallback prose is painted.");
            return string.Empty;
        }

        /// <summary>
        /// WO-972 follow-through — the panel states the CLAIM, derived from the same authority
        /// placement claims with (StructureFactory.MeasureClaimFootprintMetres), NOT a second
        /// measure of its own. WO-972 decoupled a Wall's grid claim from its fitted mesh
        /// (BuildModeController.IsValidPlacement + BaseLayoutLoader both moved to the claim
        /// metric), but this label was still reading the raw mesh measure — so a wall whose
        /// 3.03 m body ceils to 2 cells would have told the player "2x2 cells" while placement
        /// claimed 1x1. Identical output for every non-Wall row (the claim metric IS the
        /// measured metric there); only a Wall's label changes, and it changes to the truth.
        /// "Derive, don't hand-author" (ARCHITECTURE_PRINCIPLES §4): one claim authority,
        /// read by everyone who reports it.
        /// </summary>
        private static string FootprintFor(CatalogEntry e)
        {
            var grid = PlacementGrid.Instance;
            if (grid != null && e != null)
            {
                // WO-986: report non-square claim cells (same authority as placement).
                Vector2 xz = StructureFactory.MeasureClaimFootprintXZ(e);
                if (xz.x > 0f && xz.y > 0f)
                {
                    Vector2Int f = grid.FootprintCells(xz);
                    int fx = Mathf.Max(1, f.x);
                    int fy = Mathf.Max(1, f.y);
                    return fx + "x" + fy + " cells";
                }
                FlowTrace.Once("Build", "footprint-label-unmeasured-" + (e.id ?? "<null>"),
                    $"FOOTPRINT LABEL '{e.id}': claim XZ returned ({xz.x:0.###},{xz.y:0.###})m (non-positive), " +
                    "so the info panel is showing the 1x1 DEFAULT, not a measured claim.");
            }
            else
            {
                // The other way this label can be wrong, and it reads IDENTICALLY to a real
                // 1x1 on screen: no PlacementGrid (info panel opened outside build mode) or no
                // entry at all. Distinct message per cause so a capture never has to guess.
                FlowTrace.Once("Build",
                    "footprint-label-nogrid-" + (e != null ? e.id : "<null-entry>"),
                    e == null
                        ? "FOOTPRINT LABEL: no CatalogEntry - showing the 1x1 DEFAULT, not a measured claim."
                        : $"FOOTPRINT LABEL '{e.id}': PlacementGrid.Instance is null (no grid to convert " +
                          "metres to cells) - showing the 1x1 DEFAULT, not a measured claim.");
            }
            return "1x1 cells";
        }

        private static string FormatNum(float v)
            => Mathf.Approximately(v, Mathf.Round(v)) ? Mathf.RoundToInt(v).ToString() : v.ToString("0.0");
    }
}

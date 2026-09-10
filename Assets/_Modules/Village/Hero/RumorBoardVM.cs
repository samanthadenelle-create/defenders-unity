// =============================================================================
// RumorBoardVM - the pure ViewModel behind RumorBoardPanel (Brom's rumor board).
// Strict-MVVM migration Silo D.  WO-1192 v3 REBUILD (owner-approved concept).
// -----------------------------------------------------------------------------
// Assembly: DeNelle.Village   Namespace: DeNelle.Village.Hero
//
// WHAT THIS VM IS NOW, AND WHAT IT DELIBERATELY STOPPED BEING (WO-1192, owner
// rulings 2026-08-26):
//
//   "The board is for ACCEPTING new quests." Tracking is the HUD tracker's job.
//   The approved v3 concept is THREE SELF-CONTAINED RUMOR POSTERS, no tabs, no
//   detail pane, no In-Progress section. NextPage / PrevPage page by three and
//   WRAP (owner felt-test 2026-08-27 asked for Previous). So the following are
//   RETIRED, not merely unused:
//
//     TabKeys / TabLabels / ActiveTab / SetTab / IsDailyTab   - there are no tabs.
//     ActiveQuests / Track / DailyQuests / DailyRow           - the board only OFFERS.
//
//   They are deleted rather than left dormant because a dormant projection is how
//   a retired surface quietly grows back (CLAUDE.md sec.15: a state change with no
//   canon update is an incomplete change; the same is true of a dead API).
//
// !! WO-1521 (owner report 2026-09-06) PARTLY REVERSES THE "ONLY OFFERS" RULING ABOVE,
//   AND THE REVERSAL IS NAMED HERE RATHER THAN SLIPPED IN.
//   Owner, verbatim: "quests say one quest to claim but no idea how or what to do to
//   complete it." The Journey card said 1 ready to claim; this board said "The board is
//   quiet."; and NOTHING in the game named the quest, its objective, or a way to claim it.
//   That is not a copy bug - it is TWO LISTS. The counter read DailyQuestService; the
//   board read QuestCatalog; neither could see the other.
//   So the board now carries THREE ROW KINDS off ONE list (see BoardRowKind):
//     CLAIMABLE - a daily whose reward was never latched. Objective, reward, CLAIM door.
//     ACTIVE    - a story quest underway. Its CURRENT objective and a GO TO door.
//     AVAILABLE - the offer posters the v3 board already had. ACCEPT door, unchanged.
//   STOP The 08-26 ruling's real point still stands and is NOT reversed: there is still no
//   second list, no tab band, no detail pane, no separate "In Progress" section. One list,
//   one poster shape, one verb per poster - the row's KIND picks the verb. A claimable or
//   active row is the SAME poster geometry as an offer, which is why the fixed-pixel band
//   law (and RumorBoardLayoutRegression) is untouched by this change.
//   "The board is quiet." now paints only when that ONE list is empty.
//
// WHAT REPLACED THEM:
//   * PAGING. Available rumors are windowed PageSize (3) at a time. NextPage()
//     advances and WRAPS at the end; PrevPage() steps back and WRAPS at the
//     start - the owner chose the keep-going form, so the board never dead-ends
//     on a short page.
//   * HookFor is now a SHORT hook derived at a SENTENCE boundary from the full
//     letter, and LetterFor carries the whole prose for the "Read the letter >"
//     overlay. (It read "ONE-LINE" here until WO-1636: the poster's hook band now
//     seats TWO lines, and HookMaxChars is measured from that band's width rather
//     than chosen. The method keeps the name OneLineHook, which is now about the
//     hook being ONE SENTENCE, not one rendered line.)
//     Both captures of the 2026-08-25/26 shots clipped this text MID-WORD
//     ("begun to sin", "wakes the lantern eels. Sh"); a hook cut at a sentence and
//     a letter that scrolls is what makes that unreachable rather than tuned away.
//   * RewardChipsFor projects READY-TO-DRAW reward chips carrying the reward KIND
//     and AMOUNT, so the View can render the WO-1195 icon+number chip. It still
//     sums through QuestRewardMath over QuestRewardLine (WO-1201/1202 stay the
//     reward authority) and there is NO fixed chip count anywhere.
//   * IsNew(id) reads a SEEN flag off the backend seam, so the NEW chip means
//     something instead of decorating every card forever.
//
// Also owns the PREREQUISITE gate: a quest whose QuestDef.RequiresQuestId names a
// quest the player has not completed is kept out of Available and refused by Accept,
// which is what makes the Forgemasters act chain (act1 -> act2 -> act3 -> act4) an
// order instead of a suggestion.
//
// PURE C#: no UnityEngine UI types; unit-testable over a fake IRumorBoardBackend.
// ASCII-ONLY (including comments) - the shipped LiberationSans SDF has no non-Latin
// glyphs and RumorBoardLayoutRegression asserts the whole file is ASCII.
// =============================================================================

using System;
using System.Collections.Generic;
using DeNelle.Core.Quests;
using DeNelle.Core.UI;
using DeNelle.Core.UI.Mvvm;
using DeNelle.Village.Items;

namespace DeNelle.Village.Hero
{
    /// <summary>
    /// The seam the RumorBoardVM resolves quest state through. The live implementation
    /// (<see cref="RumorBoardLiveBackend"/>) wires QuestService / QuestCatalog;
    /// tests supply a fake.
    /// </summary>
    public interface IRumorBoardBackend
    {
        IReadOnlyList<QuestDef> Catalog { get; }
        bool Ready { get; }
        bool IsActive(string id);
        bool IsCompleted(string id);
        void StartQuest(string id);
        /// <summary>True once this rumor has been PUT IN FRONT OF the player. Drives the
        /// NEW chip; without it "NEW" decorates every card forever and stops meaning
        /// anything (the WO-1192 law: a badge that is always on is chrome, not state).</summary>
        bool HasSeen(string id);
        /// <summary>Record that this rumor has been shown. Called by the View once per
        /// page paint, AFTER the page's NEW flags were read.</summary>
        void MarkSeen(string id);

        // -- WO-1521 - the two seams that make the board and the counter ONE list --

        /// <summary>Today's daily quests, in slot order. Never null. The VM filters this to the
        /// CLAIMABLE ones with <see cref="DailyQuestService.IsClaimable"/> - the SAME predicate
        /// <see cref="DeNelle.Core.HudModel.JourneyDeckSubtitleVM"/> counts with, which is what
        /// makes "1 ready to claim" and the board's claimable row the same fact.</summary>
        IReadOnlyList<DailyQuestInstance> Dailies { get; }

        /// <summary>The authored reward row for a daily slot, or null when unauthored.</summary>
        DailyQuestSlotReward DailyReward(string slot);

        /// <summary>Press CLAIM on a daily. True only when the reward actually LANDED - the
        /// live backend judges that by the payer's latch, never by "an event was raised".</summary>
        bool ClaimDaily(string dailyQuestId);

        /// <summary>The CURRENT objective line of an ACTIVE story quest ("" when unknown). The
        /// current stage, not stage 0 - a player three beats in is owed the beat she is on.</summary>
        string ActiveObjective(string questId);

        /// <summary>The GO TO door for an active story quest. True when a real destination panel
        /// was opened (the stage's `completeOn` names one); false when there is none, in which
        /// case the VM falls back to PINNING the quest to the HUD tracker. Two honest doors -
        /// never a button that routes nowhere.</summary>
        bool GoTo(string questId);

        /// <summary>Pin a quest to the HUD tracker (the fallback half of <see cref="GoTo"/>).</summary>
        void Track(string questId);

        event Action Changed;
    }

    /// <summary>Pure ViewModel for the rumor board.</summary>
    public sealed class RumorBoardVM : IPanelViewModel, IDisposable
    {
        /// <summary>What a reward chip IS, so the View can pick the WO-1195 icon+number
        /// chip for a currency and a WORD chip for XP / a granted item. Deliberately a
        /// VM-local enum: mapping it to the kit's CurrencyKind (and from there to the ONE
        /// concept-id translator, ElarionUiKit.ConceptIdFor) is the View's job. A second
        /// copy of that translator here would be a second registry.</summary>
        public enum RewardKind { Xp, Crystals, Wood, Iron, Stone, Magic, Item, Wisdom }

        /// <summary>WO-1521: what a board row IS, which is the ONLY thing that picks its verb.
        /// One list, one poster shape, three kinds - never three lists.</summary>
        public enum BoardRowKind
        {
            /// <summary>An offer. ACCEPT starts it. (The whole v3 board, before WO-1521.)</summary>
            Available,
            /// <summary>A story quest underway. GO TO opens where it is finished.</summary>
            Active,
            /// <summary>A finished daily whose reward was never latched. CLAIM pays it.</summary>
            Claimable,
        }

        /// <summary>The face on a CLAIMABLE row's door.</summary>
        public const string ClaimLabel = "Claim";
        /// <summary>The face on an ACTIVE row's door.</summary>
        public const string GoToLabel = "Go To";
        /// <summary>The face on an AVAILABLE row's door.</summary>
        public const string AcceptLabel = "Accept";

        /// <summary>One READY-TO-DRAW reward chip. <see cref="Text"/> is the full word form
        /// ("Crystals 220", "Relic Drowned Ledger") and is what a no-icon fallback renders;
        /// <see cref="Amount"/> is what an icon chip renders beside its icon.</summary>
        public readonly struct RewardChipVM
        {
            public readonly RewardKind Kind;
            public readonly int Amount;
            public readonly string Text;
            public RewardChipVM(RewardKind kind, int amount, string text)
            {
                Kind = kind;
                Amount = amount;
                Text = text;
            }
            /// <summary>True for the resource kinds that own a currency icon.</summary>
            public bool IsCurrency => Kind != RewardKind.Xp && Kind != RewardKind.Item;
        }

        /// <summary>Rumors shown per page. The owner-approved v3 board is three posters.</summary>
        public const int PageSize = 3;

        /// <summary>Longest hook before it is cut at a WORD boundary. A hook that ends mid-word
        /// reads as a bug; one that ends on a word reads as a summary.
        ///
        /// WO-1636 - THIS NUMBER IS NOW MEASURED, NOT CHOSEN. It was 72, a character count with no
        /// relation to the band that draws it, and the first glyph-oracle run (Builds/wave3-capture2)
        /// caught the consequence on eighteen labels: TMP swapped the tail for an ellipsis rather
        /// than the VM cutting on a word ("Carry the sealed ledger past the flooded stai...", 27 of
        /// 62 printable glyphs at 1920x1080). The narrowest hook band in that run measured 451.2 ref
        /// px and its widest measured advance was 15.04 px per character ("Done: Clear 3 waves at the
        /// western gate." drawing 30 characters in that width), so ONE line holds 30 characters and
        /// RumorBoardPanel.HookBandPx now seats TWO. 52 is the largest cap at which every objective
        /// and letter in quests.json + daily-quests.json (63 strings) and every fixture in that
        /// capture wraps to two lines or fewer - simulated over the shipped copy, not estimated.
        ///
        /// IT IS A REAL COPY CUT AND IT IS DECLARED: 48 of those 63 hooks lose words they kept at 72
        /// (and 39 of them were being ellipsized past TWO lines at the old cap anyway, so most of
        /// what the cut removes is text the player could not read).
        /// Nothing is lost to the player - the FULL letter is one tap away behind "Read the letter >",
        /// which is why RumorBoardPanel's own header says the board never shows dense copy. Raising
        /// this back above 52 without also re-budgeting HookBandPx puts the ellipsis straight back.</summary>
        public const int HookMaxChars = 52;

        private readonly IRumorBoardBackend _backend;
        private readonly Action _onClose;
        private readonly Action _changedHandler;

        private readonly List<ItemVM> _available = new List<ItemVM>();
        private readonly List<ItemVM> _rows = new List<ItemVM>();
        private readonly List<ItemVM> _page = new List<ItemVM>();
        private readonly Dictionary<string, QuestDef> _byId = new Dictionary<string, QuestDef>();
        private readonly Dictionary<string, BoardRowKind> _kindById = new Dictionary<string, BoardRowKind>();
        private readonly Dictionary<string, DailyQuestInstance> _dailyById = new Dictionary<string, DailyQuestInstance>();
        private readonly Dictionary<string, string> _objectiveById = new Dictionary<string, string>();
        private int _pageIndex;
        private bool _disposed;

        // -- IPanelViewModel ---------------------------------------------------

        public event Action Changed;

        public string Title => "Brom's Rumor Board";

        public void Close() => _onClose?.Invoke();

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (_backend != null && _changedHandler != null) _backend.Changed -= _changedHandler;
            Changed = null;
        }

        // -- Read-only data the View renders -----------------------------------

        /// <summary>Every available quest (not active, not completed, prerequisite met).
        /// Never null. The View renders <see cref="PageQuests"/>, not this. This is a SUBSET of
        /// <see cref="Rows"/> - the AVAILABLE kind only - kept because "what can I start" is a
        /// question worth its own name.</summary>
        public IReadOnlyList<ItemVM> AvailableQuests => _available;

        /// <summary>WO-1521 - THE ONE LIST the board pages over: claimable dailies first, then
        /// active story quests, then the offers. Never null. Claimable rows lead deliberately, so
        /// the Journey card's "N ready to claim" tap lands on page 0 with the claim in front of
        /// the player rather than three pages deep.</summary>
        public IReadOnlyList<ItemVM> Rows => _rows;

        /// <summary>How many rows are CLAIMABLE. THE number the Journey card also shows - both
        /// derive from <see cref="DailyQuestService.IsClaimable"/> over the same set, so they
        /// cannot disagree.</summary>
        public int ClaimableCount => CountOfKind(BoardRowKind.Claimable);

        /// <summary>How many rows are ACTIVE story work.</summary>
        public int ActiveCount => CountOfKind(BoardRowKind.Active);

        /// <summary>TRUE only when the ONE list is empty - i.e. exactly when "The board is quiet."
        /// is an honest sentence. The View asks this instead of testing the PAGE, because a page
        /// can be empty while rows exist (WO-1521: the quiet copy must never paint over work).</summary>
        public bool IsQuiet => _rows.Count == 0;

        /// <summary>What KIND of row this id is. Defaults to Available for an unknown id, which
        /// is the only kind whose verb is safe on a row the board does not own.</summary>
        public BoardRowKind KindOf(string id) =>
            id != null && _kindById.TryGetValue(id, out var k) ? k : BoardRowKind.Available;

        /// <summary>The word on this row's door. One map, one place.</summary>
        public string ActionLabelFor(string id)
        {
            switch (KindOf(id))
            {
                case BoardRowKind.Claimable: return ClaimLabel;
                case BoardRowKind.Active: return GoToLabel;
                default: return AcceptLabel;
            }
        }

        /// <summary>
        /// THE verb. The View routes every poster's door here and never branches on kind itself -
        /// a second copy of that branch in the skin is how a CLAIM face ends up calling Accept.
        /// </summary>
        public void Invoke(string id)
        {
            switch (KindOf(id))
            {
                case BoardRowKind.Claimable: ClaimDaily(id); return;
                case BoardRowKind.Active: GoTo(id); return;
                default: Accept(id); return;
            }
        }

        /// <summary>
        /// WHAT THE PLAYER HAS TO DO, in one line - the half of the owner's report that the
        /// counter never answered ("no idea how or what to do to complete it"). An ACTIVE row
        /// gives its CURRENT stage objective; a CLAIMABLE daily gives its label with progress;
        /// an offer falls back to the letter's hook, which is what the v3 poster always showed.
        /// </summary>
        public string ObjectiveFor(string id)
        {
            // Cut here, ONCE. The View renders this straight into the one-line hook band; if it
            // re-cut what came back, an Available row (whose fallback is already a cut hook)
            // would be hooked twice and a 73-char word-cut could lose another word to nothing.
            if (id != null && _objectiveById.TryGetValue(id, out var text) && !string.IsNullOrEmpty(text))
                return OneLineHook(text);
            return HookFor(id);
        }

        private int CountOfKind(BoardRowKind kind)
        {
            int n = 0;
            for (int i = 0; i < _rows.Count; i++)
                if (KindOf(_rows[i].Id) == kind) n++;
            return n;
        }

        /// <summary>The current window of at most <see cref="PageSize"/> rumors. Never null,
        /// and never longer than PageSize - a page with fewer is a real short page, not an
        /// error, and the View renders only the posters it is given.</summary>
        public IReadOnlyList<ItemVM> PageQuests => _page;

        /// <summary>How many pages of three the board's rows make. 1 when the board is
        /// empty, so "page 1 of 1" is always a truthful sentence.</summary>
        public int PageCount
        {
            get
            {
                int n = _rows.Count;
                if (n <= 0) return 1;
                return (n + PageSize - 1) / PageSize;
            }
        }

        /// <summary>Zero-based index of the shown page.</summary>
        public int PageIndex => _pageIndex;

        /// <summary>True when there is more than one page, i.e. Next / Previous actually go somewhere.</summary>
        public bool HasMultiplePages => PageCount > 1;

        /// <summary>Status line (the board's transient message).</summary>
        public string Status { get; private set; } = "The talk of Elarion. Accept what calls to you.";

        /// <summary>The FULL letter for a rumor - the paragraph the "Read the letter &gt;"
        /// overlay scrolls. Never null; an unauthored quest gets an honest short line rather
        /// than an empty well.</summary>
        public string LetterFor(string id)
        {
            // A claimable daily has no letter - it has a finished job and a reward. Say that.
            if (id != null && _dailyById.TryGetValue(id, out var daily) && daily != null)
                return "You finished this: " + DailyQuestCatalog.ResolveLabel(daily) +
                       ". Press Claim to take the reward.";
            var def = FindDef(id);
            if (def != null && def.Stages != null && def.Stages.Count > 0 && def.Stages[0] != null
                && !string.IsNullOrEmpty(def.Stages[0].ObjectiveText))
                return def.Stages[0].ObjectiveText;
            return "A new thread waits to be picked up.";
        }

        /// <summary>The ONE-SENTENCE hook: the letter's first sentence, cut at a WORD boundary if
        /// that sentence is itself longer than <see cref="HookMaxChars"/>. It can never end
        /// mid-word, which is the defect both the 2026-08-25 and 2026-08-26 captures showed
        /// ("begun to sin" / "lantern eels. Sh"). It is no longer one rendered LINE - the poster's
        /// hook band seats two of them since WO-1636.</summary>
        public string HookFor(string id) => OneLineHook(LetterFor(id));

        /// <summary>Pure, testable hook derivation (see <see cref="HookFor"/>).</summary>
        public static string OneLineHook(string letter)
        {
            if (string.IsNullOrEmpty(letter)) return "";
            string s = letter.Replace('\n', ' ').Replace('\r', ' ').Trim();
            if (s.Length == 0) return "";

            // First sentence, when there is one and it is not itself the whole paragraph.
            int cut = -1;
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c != '.' && c != '!' && c != '?') continue;
                // A period that is not followed by whitespace is an abbreviation or a
                // decimal, not a sentence end.
                if (i + 1 < s.Length && s[i + 1] != ' ') continue;
                cut = i + 1;
                break;
            }
            if (cut > 0 && cut <= HookMaxChars) return s.Substring(0, cut).Trim();

            if (s.Length <= HookMaxChars) return s;

            // Otherwise cut at the last word boundary that fits, and SAY it is cut.
            int space = s.LastIndexOf(' ', Math.Min(HookMaxChars, s.Length - 1));
            if (space <= 0) space = Math.Min(HookMaxChars, s.Length);
            return s.Substring(0, space).TrimEnd(' ', ',', ';', ':') + "...";
        }

        /// <summary>The quest's display TYPE for the poster's overhanging tag. The label
        /// wording is a display concern; this returns the canonical bucket in title case.</summary>
        public string TypeFor(string id)
        {
            if (id != null && _dailyById.ContainsKey(id)) return "Daily";
            var def = FindDef(id);
            string ty = NormalizedType(def);
            if (ty == "gear") return "Gear";
            if (ty == "endgame") return "Endgame";
            if (ty == "daily") return "Daily";
            if (ty == "side") return "Side";
            return "Main";
        }

        /// <summary>True while this rumor has never been shown to the player. Drives the NEW
        /// chip. False the moment the backend has recorded it as seen.</summary>
        public bool IsNew(string id)
        {
            if (string.IsNullOrEmpty(id) || _backend == null) return false;
            return !_backend.HasSeen(id);
        }

        /// <summary>Record every rumor on the CURRENT page as seen. The View calls this AFTER
        /// it has read the page's NEW flags, so a card is NEW exactly once.</summary>
        public void MarkPageSeen()
        {
            if (_backend == null) return;
            for (int i = 0; i < _page.Count; i++)
            {
                string id = _page[i].Id;
                if (!string.IsNullOrEmpty(id)) _backend.MarkSeen(id);
            }
        }

        /// <summary>
        /// The quest's TOTAL authored rewards across all stages, as READY-TO-DRAW chips.
        /// Empty when unrewarded - the View hides the row rather than drawing an empty rule.
        /// Sums through <see cref="QuestRewardMath"/> over <see cref="QuestRewardLine"/>
        /// (WO-1201/1202 remain the reward authority) and emits ONE chip per authored
        /// reward - there is no fixed chip count and no second reward schema.
        /// </summary>
        public IReadOnlyList<RewardChipVM> RewardChipsFor(string id)
        {
            var chips = new List<RewardChipVM>();

            // A CLAIMABLE daily's reward is authored on its SLOT row, not on quest stages. It is
            // read through the backend seam (not DailyQuestCatalog directly) so a claim row is
            // unit-testable without StreamingAssets - the same reason the rest of this VM is pure.
            if (id != null && _dailyById.TryGetValue(id, out var daily) && daily != null)
            {
                var slot = _backend != null ? _backend.DailyReward(daily.Slot) : null;
                if (slot == null) return chips;
                if (slot.RewardCrystals > 0)
                    chips.Add(new RewardChipVM(RewardKind.Crystals, slot.RewardCrystals, "Crystals " + slot.RewardCrystals));
                // Canon sec.7: the authored `food` slot IS Stone. Never label it Food.
                if (slot.RewardFood > 0)
                    chips.Add(new RewardChipVM(RewardKind.Stone, slot.RewardFood, "Stone " + slot.RewardFood));
                if (slot.RewardWisdom > 0)
                    chips.Add(new RewardChipVM(RewardKind.Wisdom, slot.RewardWisdom, "Wisdom " + slot.RewardWisdom));
                if (slot.RewardRandomItem)
                    chips.Add(new RewardChipVM(RewardKind.Item, 0, "A found item"));
                return chips;
            }

            var def = FindDef(id);
            if (def == null || def.Stages == null) return chips;

            int xp = 0, crystals = 0, wood = 0, iron = 0, food = 0, magic = 0;
            var items = new List<string>();
            foreach (var st in def.Stages)
            {
                if (st == null || st.Reward == null) continue;
                QuestRewardMath.Sum(st.Reward,
                    out int sXp, out int sC, out int sW, out int sIr, out int sF, out int sM, out var sItems);
                xp += sXp; crystals += sC; wood += sW; iron += sIr; food += sF; magic += sM;
                if (sItems != null) items.AddRange(sItems);
            }

            // XP first - owner ruling WO-1202: primary reward on the board slab.
            if (xp > 0) chips.Add(new RewardChipVM(RewardKind.Xp, xp, "XP " + xp));
            if (crystals > 0) chips.Add(new RewardChipVM(RewardKind.Crystals, crystals, "Crystals " + crystals));
            if (wood > 0) chips.Add(new RewardChipVM(RewardKind.Wood, wood, "Wood " + wood));
            if (iron > 0) chips.Add(new RewardChipVM(RewardKind.Iron, iron, "Iron " + iron));
            // Canon sec.7: the authored `food` slot IS Stone. Never label it Food.
            if (food > 0) chips.Add(new RewardChipVM(RewardKind.Stone, food, "Stone " + food));
            if (magic > 0) chips.Add(new RewardChipVM(RewardKind.Magic, magic, "Magic " + magic));
            // NAME the item, never key it - the chip sits in a rewards row under a named
            // quest, so the name IS the reward.
            foreach (var it in items)
                chips.Add(new RewardChipVM(RewardKind.Item, 0, ItemDisplayName(it)));
            return chips;
        }

        /// <summary>The same rewards as word-form parts, one per chip ("Crystals 20",
        /// "Iron Longsword"). Kept as the single source for any consumer that wants text.</summary>
        public IReadOnlyList<string> RewardPartsFor(string id)
        {
            var chips = RewardChipsFor(id);
            var parts = new List<string>(chips.Count);
            foreach (var c in chips) parts.Add(c.Text);
            return parts;
        }

        /// <summary>The same rewards joined ASCII for a single line. "" when unrewarded.</summary>
        public string RewardFor(string id) => string.Join(" | ", RewardPartsFor(id));

        /// <summary>
        /// Player-facing name for a granted item id, read off the SAME row the item resolves to:
        /// gear first (weapons/armor/accessories.json), then the non-gear identity catalogs
        /// (consumables / materials). An id NO shipped catalog owns is a CONTENT gap, not a code
        /// one, so the last resort is the kit's formatter (`relic_drowned_ledger` -> "Relic
        /// Drowned Ledger") - a raw snake_case key is never player-visible, and the row is never
        /// hidden either: a reward the player earns is always named.
        /// </summary>
        public static string ItemDisplayName(string itemId)
        {
            if (string.IsNullOrEmpty(itemId)) return "";

            var w = GearCatalog.FindWeapon(itemId);
            if (w != null && !string.IsNullOrEmpty(w.name)) return w.name;
            var a = GearCatalog.FindArmor(itemId);
            if (a != null && !string.IsNullOrEmpty(a.name)) return a.name;
            var ac = GearCatalog.FindAccessory(itemId);
            if (ac != null && !string.IsNullOrEmpty(ac.name)) return ac.name;

            var row = ItemIdentity.Resolve(itemId);
            if (row.IsKnown && !string.IsNullOrEmpty(row.DisplayName)) return row.DisplayName;

            return ElarionUiKit.SpacedDisplayName(itemId);
        }

        // -- Commands ----------------------------------------------------------

        /// <summary>Advance one page of three and WRAP at the end (owner ruling: the
        /// keep-going form, no dead end, no page dots). Raises Changed.</summary>
        public void NextPage()
        {
            int pages = PageCount;
            _pageIndex = pages <= 1 ? 0 : (_pageIndex + 1) % pages;
            BuildPage();
            Raise();
        }

        /// <summary>Step one page of three BACKWARD and WRAP at the start (the pair of
        /// <see cref="NextPage"/>; owner felt-test 2026-08-27: "A previous button would
        /// be nice"). Raises Changed.</summary>
        public void PrevPage()
        {
            int pages = PageCount;
            _pageIndex = pages <= 1 ? 0 : (_pageIndex - 1 + pages) % pages;
            BuildPage();
            Raise();
        }

        /// <summary>
        /// CLAIM a finished daily. Routes to the ONE payer (DailyQuestService.RequestClaim ->
        /// ClaimRequested -> DailyQuestRewardBridge) and reports what actually happened: a claim
        /// that credits nothing STAYS on the board and says why, because a row that silently
        /// disappears having paid nothing is the WO-978 defect wearing a button.
        /// </summary>
        public void ClaimDaily(string id)
        {
            if (string.IsNullOrEmpty(id)) return;
            if (_backend == null) { Status = "Quests aren't ready yet."; Raise(); return; }
            _dailyById.TryGetValue(id, out var daily);
            string name = daily != null ? DailyQuestCatalog.ResolveLabel(daily) : id;

            if (_backend.ClaimDaily(id))
            {
                Status = "Claimed: " + name + ".";
                Rebuild();
            }
            else
            {
                Status = "Nothing could be credited for " + name + " - your stores may be full. Make room, then claim again.";
            }
            Raise();
        }

        /// <summary>
        /// GO TO the place an active quest is finished. The backend opens the destination panel
        /// when the quest's current stage names one; when it does not, the honest fallback is to
        /// PIN the quest to the HUD tracker and say so - never a door that routes nowhere.
        /// </summary>
        public void GoTo(string id)
        {
            if (string.IsNullOrEmpty(id)) return;
            if (_backend == null) { Status = "Quests aren't ready yet."; Raise(); return; }
            var def = FindDef(id);
            string name = def != null && !string.IsNullOrEmpty(def.Title) ? def.Title : id;

            if (_backend.GoTo(id))
            {
                Status = "Opening " + name + ".";
                Raise();
                Close();
                return;
            }

            _backend.Track(id);
            Status = "Tracking " + name + " - the objective is pinned to your HUD.";
            Raise();
            Close();
        }

        /// <summary>Accept an available quest (StartQuest). It leaves the board - the board
        /// only OFFERS work. WO-1521: the accepted rumor does not VANISH any more - it comes back
        /// on the next Rebuild as an ACTIVE row carrying its objective and a GO TO door.</summary>
        public void Accept(string id)
        {
            if (string.IsNullOrEmpty(id)) return;
            if (_backend == null || !_backend.Ready) { Status = "Quests aren't ready yet."; Raise(); return; }
            var def = FindDef(id);
            // The Available list already hides a gated quest, but the refusal lives here too so
            // no caller (a stale poster, a test, a future view) can start an act out of order.
            if (def != null && !PrerequisiteMet(def))
            {
                Status = "Not yet: finish " + CatalogTitle(def.RequiresQuestId) + " first.";
                Raise();
                return;
            }
            _backend.StartQuest(id);
            string name = def != null && !string.IsNullOrEmpty(def.Title) ? def.Title : id;
            Status = "Accepted: " + name + ".";
            // The backend raises Changed on a successful start (-> Rebuild); rebuild defensively
            // if it did not become active (service wasn't up to fire the event).
            if (!_backend.IsActive(id)) Rebuild();
            Raise();
        }

        // -- Construction / resolution -----------------------------------------

        /// <summary>The ONLY resolution site: wires the live quest services/catalog.</summary>
        public static RumorBoardVM CreateDefault(Action onClose = null) =>
            new RumorBoardVM(new RumorBoardLiveBackend(), onClose);

        public RumorBoardVM(IRumorBoardBackend backend, Action onClose)
        {
            _backend = backend;
            _onClose = onClose;
            if (_backend != null)
            {
                _changedHandler = OnBackendChanged;
                _backend.Changed += _changedHandler;
            }
            Rebuild();
        }

        private void OnBackendChanged() { Rebuild(); Raise(); }

        private QuestDef FindDef(string id) =>
            id != null && _byId.TryGetValue(id, out var d) ? d : null;

        /// <summary>True when the quest carries no requiresQuestId, or when the quest it names
        /// is already COMPLETED. This is what enforces act ordering (forgemasters_act1 -> act2
        /// -> act3 -> act4); without it every act, including the terminal one that mints the
        /// aegis legendaries, is startable on a fresh save.</summary>
        private bool PrerequisiteMet(QuestDef def)
        {
            string prereq = def != null ? def.RequiresQuestId : null;
            if (string.IsNullOrEmpty(prereq)) return true;
            prereq = prereq.Trim();
            if (prereq.Length == 0) return true;
            return _backend != null && _backend.IsCompleted(prereq);
        }

        /// <summary>Display title for any catalog quest id (not just the ones the board
        /// indexed), so a refusal names the quest the player has to finish rather than a raw
        /// id. Falls back to the id when the catalog cannot answer.</summary>
        private string CatalogTitle(string id)
        {
            if (string.IsNullOrEmpty(id)) return "the quest before it";
            var catalog = _backend != null ? _backend.Catalog : null;
            if (catalog != null)
                foreach (var q in catalog)
                    if (q != null && q.Id == id && !string.IsNullOrEmpty(q.Title)) return q.Title;
            return id;
        }

        /// <summary>
        /// WO-1521 - THE ONE LIST IS COMPOSED HERE, AND NOWHERE ELSE.
        /// Order is CLAIMABLE, then ACTIVE, then AVAILABLE: what is owed to the player, then
        /// what she is in the middle of, then what she could take on. That order is also what
        /// puts a claim on page 0 for the Journey card's tap.
        /// </summary>
        private void Rebuild()
        {
            _available.Clear();
            _rows.Clear();
            _byId.Clear();
            _kindById.Clear();
            _dailyById.Clear();
            _objectiveById.Clear();

            var active = new List<ItemVM>();

            // 1. CLAIMABLE dailies. The predicate is DailyQuestService.IsClaimable - the SAME
            //    one JourneyDeckSubtitleVM counts with, which is the whole point of this ticket.
            var dailies = _backend != null ? _backend.Dailies : null;
            if (dailies != null)
            {
                foreach (var q in dailies)
                {
                    if (q == null || string.IsNullOrEmpty(q.Id)) continue;
                    if (!DailyQuestService.IsClaimable(q)) continue;
                    _dailyById[q.Id] = q;
                    _kindById[q.Id] = BoardRowKind.Claimable;
                    _objectiveById[q.Id] = "Done: " + DailyQuestCatalog.ResolveLabel(q) + ". Your reward is waiting.";
                    _rows.Add(new ItemVM(q.Id, DailyQuestCatalog.ResolveLabel(q), "daily", q.Id, 0, "", true));
                }
            }

            var catalog = _backend != null ? _backend.Catalog : null;
            if (catalog != null)
            {
                foreach (var def in catalog)
                {
                    if (def == null || string.IsNullOrEmpty(def.Id)) continue;
                    _byId[def.Id] = def;

                    string title = !string.IsNullOrEmpty(def.Title) ? def.Title : def.Id;

                    // 2. ACTIVE story work. This is the half the owner's "no idea how or what to
                    //    do" was asking for: the quest is named, its CURRENT objective is on the
                    //    card, and GO TO is the door to the place that finishes it. Before
                    //    WO-1521 an accepted quest simply vanished from every surface but the
                    //    small HUD tracker pin.
                    if (_backend.IsActive(def.Id))
                    {
                        _kindById[def.Id] = BoardRowKind.Active;
                        string objective = _backend.ActiveObjective(def.Id);
                        if (string.IsNullOrEmpty(objective)) objective = LetterFor(def.Id);
                        _objectiveById[def.Id] = objective;
                        active.Add(new ItemVM(def.Id, title, "quest", def.Id, 0, "", true));
                        continue;
                    }

                    if (_backend.IsCompleted(def.Id)) continue;   // done - off the board
                    // A quest whose requiresQuestId names an unfinished quest stays off the board
                    // entirely (see PrerequisiteMet). Hidden rather than shown locked: a v3 poster
                    // has no lock affordance, so a locked poster would look acceptable.
                    if (!PrerequisiteMet(def)) continue;

                    _kindById[def.Id] = BoardRowKind.Available;
                    _available.Add(new ItemVM(def.Id, title, "quest", def.Id, 0, "", true));
                }
            }

            _rows.AddRange(active);
            _rows.AddRange(_available);

            // A page that no longer exists (the last rumor on it was accepted) walks back to
            // the last real page rather than showing an empty board with rumors still on it.
            int pages = PageCount;
            if (_pageIndex >= pages) _pageIndex = pages - 1;
            if (_pageIndex < 0) _pageIndex = 0;
            BuildPage();
        }

        private void BuildPage()
        {
            _page.Clear();
            int start = _pageIndex * PageSize;
            for (int i = start; i < _rows.Count && i < start + PageSize; i++)
                _page.Add(_rows[i]);
        }

        // Normalize a quest's free-string Type -> a lowercase bucket; empty/null = "story".
        private static string NormalizedType(QuestDef def)
        {
            if (def == null || string.IsNullOrEmpty(def.Type)) return "story";
            return def.Type.Trim().ToLowerInvariant();
        }

        private void Raise() { if (!_disposed) Changed?.Invoke(); }
    }

    /// <summary>
    /// Live <see cref="IRumorBoardBackend"/> - the sole binding to QuestService /
    /// QuestCatalog. Kept out of the View so RumorBoardPanel stays a dumb skin.
    /// </summary>
    public sealed class RumorBoardLiveBackend : IRumorBoardBackend
    {
        /// <summary>PlayerPrefs key prefix for the NEW-chip seen flag. Deliberately NOT a save
        /// schema field: this is a cosmetic per-device badge, and adding a schema version for a
        /// chip would be a migration the player never sees.</summary>
        private const string SeenPrefix = "rumor.seen.";

        public IReadOnlyList<QuestDef> Catalog => QuestCatalog.Quests;
        public bool Ready => QuestService.Instance != null;

        public bool IsActive(string id) => QuestService.Instance != null && QuestService.Instance.IsActive(id);
        public bool IsCompleted(string id) => QuestService.Instance != null && QuestService.Instance.IsCompleted(id);

        public void StartQuest(string id) { if (QuestService.Instance != null) QuestService.Instance.StartQuest(id); }

        // -- WO-1521 - the claim / objective / go-to seams -----------------------

        public IReadOnlyList<DailyQuestInstance> Dailies =>
            DailyQuestService.Instance != null
                ? DailyQuestService.Instance.TodayQuests
                : System.Array.Empty<DailyQuestInstance>();

        public DailyQuestSlotReward DailyReward(string slot) => DailyQuestCatalog.RewardFor(slot);

        /// <summary>The ONE payer is re-entered through the service's claim seam; this returns
        /// its VERDICT (the latch landed), never "the call was made".</summary>
        public bool ClaimDaily(string dailyQuestId) =>
            DailyQuestService.Instance != null && DailyQuestService.Instance.RequestClaim(dailyQuestId);

        public string ActiveObjective(string questId)
        {
            var stage = QuestService.Instance != null ? QuestService.Instance.GetStage(questId) : null;
            return stage != null && !string.IsNullOrEmpty(stage.ObjectiveText) ? stage.ObjectiveText : "";
        }

        /// <summary>
        /// The GO TO door. A stage whose `completeOn` is kind `panel` names a PanelId VERBATIM
        /// (quests.json ships BuildingUpgrade / Crafting / Inventory / JewelerCrafting /
        /// RumorBoard), so the destination is routed through <see cref="PanelRouter"/> - the one
        /// door registry - and never through a hand-rolled opener. Any other completion kind
        /// (talk / build / wave / arena / pet ...) happens out in the world, where there is no
        /// panel to open: those return FALSE and the VM pins the quest to the HUD tracker
        /// instead. STOP Do not invent a destination for them here; an invented door is worse than
        /// a tracked objective, because it teaches the player the wrong place.
        /// </summary>
        public bool GoTo(string questId)
        {
            var stage = QuestService.Instance != null ? QuestService.Instance.GetStage(questId) : null;
            var completion = stage != null ? stage.CompleteOn : null;
            if (completion == null) return false;
            if (completion.NormalizedKind != QuestCompletion.KindPanel) return false;
            if (string.IsNullOrEmpty(completion.TargetId)) return false;
            if (!System.Enum.TryParse(completion.TargetId.Trim(), ignoreCase: true, out PanelId panel))
            {
                DeNelle.Core.Diagnostics.FlowTrace.Warn("Quest",
                    "Rumor board GO TO: quest '" + questId + "' names panel target '" + completion.TargetId +
                    "' which is not a PanelId - falling back to tracking the quest.");
                return false;
            }
            return PanelRouter.Open(panel);
        }

        public void Track(string questId)
        {
            if (QuestService.Instance != null) QuestService.Instance.SetTracked(questId);
        }

        public bool HasSeen(string id) =>
            !string.IsNullOrEmpty(id) && UnityEngine.PlayerPrefs.GetInt(SeenPrefix + id, 0) == 1;

        public void MarkSeen(string id)
        {
            if (string.IsNullOrEmpty(id)) return;
            if (UnityEngine.PlayerPrefs.GetInt(SeenPrefix + id, 0) == 1) return;
            UnityEngine.PlayerPrefs.SetInt(SeenPrefix + id, 1);
        }

        /// <summary>
        /// WO-1521: BOTH quest services, because the board now projects BOTH. Wiring only
        /// QuestService left a claimed daily sitting on the board until the panel was reopened -
        /// a row that has been paid must leave the moment it is paid. DailyQuestService.SetChanged
        /// fires on a fresh roll, a reroll and a claim, all of which change what the board shows.
        /// </summary>
        public event Action Changed
        {
            add
            {
                if (QuestService.Instance != null) QuestService.Instance.QuestChanged += value;
                if (DailyQuestService.Instance != null) DailyQuestService.Instance.SetChanged += value;
            }
            remove
            {
                if (QuestService.Instance != null) QuestService.Instance.QuestChanged -= value;
                if (DailyQuestService.Instance != null) DailyQuestService.Instance.SetChanged -= value;
            }
        }
    }
}

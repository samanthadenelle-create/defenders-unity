// =============================================================================
// RaidSelectionVM — the pure ViewModel behind RaidSelectionScreen (the raid grid).
// Strict-MVVM migration Silo D.
// -----------------------------------------------------------------------------
// Assembly: DeNelle.Village   Namespace: DeNelle.Village.Hero
//
// Owns the SceneConfigCatalog projection: the FOUR raid targets (fallback to all
// enemy raids) as ItemVM cards + per-id helpers (difficulty / target time / reward
// hint / description / unlockVictories). The View (RaidSelectionScreen) binds this,
// renders the card grid from vm.Raids + the helpers, and routes a card tap through
// vm.DefFor(id) to open the deploy screen — it never touches the gameplay catalog.
//
// 2026-09-04 — THE ESCALATION GATE (economy map §4). Before today this VM hard-listed
// three ids and emitted EVERY card unlocked at EVERY victory count: ItemVM has always
// carried Locked + LockReason and the VM passed neither, so "upgrade -> unlock a harder
// raid" had no reader. It now compares each def's authored unlockVictories (0/3/10/20)
// against an INJECTED victory count, and refuses a target whose scene this build cannot
// load. Names/copy: docs/CREATIVE_CANON_ELARION_2026-09-04.md §3.
//
// 2026-09-05 - WO-1402: THE ROWS SAY WHAT A RAID PAYS. Every row exposes a spoils
// ESTIMATE ("Spoils: ~1800 wood, ~1100 iron, ~2200 gold") produced by the settle
// payout's OWN formula (RaidScoring.EstimateSpoils -> ProjectLoot -> ComputeLoot, the
// chain RaidScoring.LootFor pays through) - there is no second loot table and no
// literal. The three identical gold pips are hidden until per-camp star ratings VARY
// (no producer records them today, so they stay hidden by data). A camp whose
// garrison exceeds the fieldable army carries the WARNING "Outmatched - Army N advised"
// (WO-1389 compare rule: garrison bodies vs deployable bodies). VM owns every string;
// RaidSelectionScreen only renders them. Pinned by RaidSelectionSpoilsRegression.
//
// SEPARATE from RaidDeployVM by design (different domain: this is the browse grid,
// that is the pre-raid deploy math). They only share the SceneConfigDef formatting.
// PURE C#: no UnityEngine UI types; unit-testable over a fake def list (§2c).
// =============================================================================

using System;
using System.Collections.Generic;
using DeNelle.Core.UI.Mvvm;
using DeNelle.Village;

namespace DeNelle.Village.Hero
{
    /// <summary>Pure ViewModel for the Raids-tab card grid.</summary>
    public sealed class RaidSelectionVM : IPanelViewModel, IDisposable
    {
        /// <summary>Icon role key on each raid card (the View maps it to art; no game state).</summary>
        public const string IconRoleRaid = "raid";

        // The FOUR raid targets, in escalation order (mirrors the View's grid).
        //
        // - THESE ARE LIVE SAVE KEYS. RaidClaimService persists PlayerPrefs
        // "dotr-raid-owner-<id>" / "dotr-raid-crystalday-<id>" keyed on exactly these
        // strings (RaidClaimService.cs:53,62). NEVER rename one to match a display name:
        // the creative canon (docs/CREATIVE_CANON_ELARION_2026-09-04.md §3) renames the
        // CARD, via scene-configs.json displayName, and says so in its own rule.
        //
        // iron_bastion joined the list on 2026-09-04 (economy map §4, tier 4). Its scene
        // was baked 2026-08-21 and was reachable by nothing - see the availability gate
        // below and the ORPHAN note deleted in the same commit.
        private static readonly string[] FlagshipRaidIds =
        {
            "raider_camp_small",
            "fortified_garrison",
            "mage_enclave",
            "iron_bastion",
        };

        /// <summary>
        /// THE ESCALATION INPUT: how many raid victories the player has banked. Compared
        /// against each def's authored <c>unlockVictories</c> (0 / 3 / 10 / 20, economy map §4).
        ///
        /// <para>WHY A PROVIDER AND NOT A DIRECT READ. At git HEAD this session NO victory
        /// counter existed anywhere in the tree (grepped raidsWon / raidVictories / victoryCount /
        /// totalRaidVictories across Assets/ and api/: zero hits; RaidClaimService persists only
        /// per-camp one-time flags, never a total). The counter is a SIBLING LANE's file in this
        /// same release, so this VM neither invents a PlayerPrefs key nor guesses a field name -
        /// it reads through ONE injectable seam and stays pure C#.</para>
        ///
        /// <para>MEASURED IN THE WORKING TREE 2026-09-04: that lane landed
        /// <c>GameState.RaidVictories</c> (GameState.cs:629), incremented by
        /// RaidVictoryController.RecordVictory with a one-shot backfill for older saves.
        /// RaidSelectionScreen.OpenInternal wires this provider to it, and that is the ONLY
        /// wiring site. Unwired (headless, EditMode, a stateless probe) the default is 0, which
        /// locks the gated tiers VISIBLY with a reason - never silently open.</para>
        /// </summary>
        public static Func<int> VictoryCountProvider;

        /// <summary>
        /// Second gate: can this def's scene actually be LOADED in this build? Injected so the
        /// VM stays pure C# (no UnityEngine); <see cref="CreateDefault"/> wires it to
        /// <c>SceneRouter.IsSceneInBuild</c>. Null = assume every scene is loadable, which is
        /// what the EditMode tests and headless projections want.
        ///
        /// <para>THIS EXISTS BECAUSE OF A MEASURED HOLE, not as defensive decoration.
        /// RaidBase_IronBastion.unity bakes 127 GameObjects and NO
        /// HeroStartPoint_PlayerSpawn marker (measured 2026-09-04 against
        /// RaidBase_mage_enclave's 270 GameObjects + 1 marker). HeroControlEnsurer seats the
        /// carried hero at that marker, so entering that scene today strands the hero at its
        /// TOWN world pose. The scene is therefore registered in Build Settings DISABLED,
        /// Application.CanStreamedLevelBeLoaded returns false for it, and this predicate turns
        /// that into a locked card with a sentence instead of a dead tap.</para>
        /// </summary>
        public static Func<string, bool> SceneAvailableProvider;

        /// <summary>
        /// WO-1402 - THE ARMY INPUT for the row's <c>Outmatched - Army N advised</c> word: how many
        /// troop BODIES the player can field right now. Compared against each camp's garrison
        /// headcount (<see cref="GarrisonCount"/>) - the SAME two numbers RaidDeployVM's scout
        /// report puts side by side ("Garrison: 9 defenders - you field 3", WO-1389 pressure
        /// point 2), so the row and the report can never disagree about which camp is above
        /// the army. Injected so the VM stays pure C#; RaidSelectionScreen.OpenInternal wires
        /// it to <c>GameState.Army.GetDeployable()</c> and that is the ONLY wiring site.
        /// Unwired / null / throwing = UNKNOWN (-1): no row carries the word, because a
        /// headless or pre-state frame must never print a lock it cannot prove.
        /// </summary>
        public static Func<int> DeployableTroopsProvider;

        /// <summary>
        /// WO-1402 - per-camp BEST STAR RATING, or -1 when unknown. The three gold pips on
        /// every row were identical on every camp on every frame (merged UI review row 1) and
        /// therefore carried nothing; they are drawn only once ratings actually VARY across
        /// the rows (<see cref="ShowStarPips"/>). MEASURED AT SOURCE 2026-09-05: no producer
        /// persists a per-camp star record (grepped BestStars / bestStars / StarsFor across
        /// Assets/_Modules: DungeonRunGrade and BattleStarRating only, neither per camp), so
        /// this stays null in the shipping wiring and the pips stay hidden - by data, not by
        /// deletion. The first lane that records stars per camp wires this and the pips return.
        /// </summary>
        public static Func<string, int> BestStarsProvider;

        /// <summary>
        /// WO-1562 PART 2 - HAS THIS CAMP ALREADY BEEN CLEARED? Wired in
        /// <c>RaidSelectionScreen.OpenInternal</c> to <c>RaidClaimService.IsClaimed</c>, and to
        /// NOTHING ELSE.
        ///
        /// <para>STOP - NEVER A SECOND CLAIM PREDICATE. <c>RaidClaimService</c> is the one place a
        /// clear is persisted (<c>MarkClaimed</c>, from the victory seam) and it must stay the one
        /// place a clear is READ. The WO-1521 lesson, recorded at
        /// <c>PlayerDeckWorkspace.cs:719-723</c>: "ONE rule, TWO surfaces... a second check would
        /// drift from the first, and the drift is the actual defect."</para>
        ///
        /// <para>Unwired / null / throwing = NOT CLEARED for every row, because a headless or
        /// pre-state frame must never claim a win it cannot prove. Same contract as
        /// <see cref="DeployableTroopsProvider"/>.</para>
        /// </summary>
        public static Func<string, bool> ClaimedProvider;

        /// <summary>Sentinel for "not known" on the army and star inputs.</summary>
        public const int Unknown = -1;

        // =====================================================================
        //  WO-1542 - THE ARMY WORD IS ADVICE, NOT A LOCK (owner ruling 2026-09-06)
        // =====================================================================
        //  RETIRED: `ArmyLockPrefix = "LOCKED - needs Army "`. That word was
        //  DISPLAY-ONLY - it appeared on the card face and in one log line and
        //  NOWHERE ELSE. OnCardTapped refuses on exactly two conditions (the
        //  escalation lock and Heartfire) and then falls through to
        //  RaidDeployScreen.Open, so a card reading LOCKED opened anyway, under a lit
        //  BEGIN ASSAULT. Two PNGs of the same build and the same camp show both
        //  halves (seeker-357453-raids.png / -raid-deploy.png). The word, the styling
        //  and the door disagreed three ways.
        //
        //  NEITHER SIDE WAS A BUG ALONE, WHICH IS WHY IT SURVIVED: WO-1402 authored
        //  the word as a row label, and WO-1403's RESULT deliberately decoupled the
        //  deploy footer from readiness so the first-raid soft gate stays at the ONE
        //  door. Both correct; nobody reconciled them.
        //
        //  OWNER RULING: "Warning, not a lock." The player may still attack (WWCD -
        //  Clash of Clans lets you attack an over-matched base and never calls it
        //  locked), and BEGIN ASSAULT asks ONCE via a confirm toast.
        //
        //  STOP - DO NOT TURN THIS BACK INTO A GATE. The tap must keep opening the
        //  deploy screen exactly as it does today. RaidDeployVM.CanDeploy and the
        //  deploy footer stay bound to scene + Build Settings, never to readiness;
        //  a readiness check inside the deploy screen is the second-gate shape
        //  WO-1379 forbids and HeartfireRegression PIN F reds the file for.
        // =====================================================================

        /// <summary>Leading half of the army warning; the number is the garrison headcount the
        /// player is advised to be able to field. ASCII (device tofu risk).</summary>
        public const string ArmyWarnPrefix = "Outmatched - Army ";
        /// <summary>Trailing half - "advised", never "required": the door honours no such
        /// requirement and the word must not claim one.</summary>
        public const string ArmyWarnSuffix = " advised";

        /// <summary>Prefix of every spoils line; the oracle asserts on it.</summary>
        public const string SpoilsPrefix = "Spoils: ";

        // =====================================================================
        //  WO-1461 - THE CARD QUOTES WHAT WILL ACTUALLY ARRIVE
        // =====================================================================
        //  EVIDENCE (troop-ai-blind-2026-09-06.log, 14:37:40): the card read
        //  "Spoils: ~1800 wood, ~1100 iron" and 25 wood banked. Two subtractions
        //  sat between the promise and the bank and the card knew about neither -
        //  the repeat-clear multiplier (1800 -> 450) and the town bank's headroom
        //  (450 -> 25). So the quote is now the SCALED estimate, and the part the
        //  bank has no room for is NAMED as going to the Raid Cache rather than
        //  silently missing.
        //
        //  THE LAW, and it is the one SpoilsAreBankableRegression pins:
        //      quoted == banked + cached
        //  Presentation only. This re-authors NO number: the multiplier is
        //  RaidClaimService's, the headroom is TownBankCapacity's, and the split
        //  is RaidClaimService.SplitAxis - the SAME method the settle path uses,
        //  so the card and the bank cannot derive two different answers.
        // =====================================================================

        /// <summary>Opening of the Raid Cache notice - the owner's "progression prompt"
        /// framing (2026-09-06 20:33), in WORDS because she is red/green colourblind.</summary>
        public const string CacheNoticePrefix = "Storage full - ";
        /// <summary>Closing of the Raid Cache notice.</summary>
        public const string CacheNoticeSuffix = " waits in the Raid Cache";

        /// <summary>
        /// WO-1461 - IS A CLEAR OF THIS CAMP A REPEAT INSIDE ITS COOLDOWN CYCLE? Wired in
        /// <c>RaidSelectionScreen.OpenInternal</c> to
        /// <c>RaidClaimService.IsRepeatClearInCycle</c>, and to NOTHING ELSE.
        ///
        /// <para>STOP - NEVER A SECOND REPEAT PREDICATE, for the same reason
        /// <see cref="ClaimedProvider"/> carries that warning. This is deliberately NOT
        /// <c>ClaimedProvider</c>: a claim is permanent, a cooldown cycle is not, and the
        /// owner's ruling resets the share to 100% when the cooldown expires. Deriving
        /// "repeat" from the cleared flag here would quote 60% forever on a camp that is
        /// about to pay full.</para>
        ///
        /// <para>Unwired / null / throwing = NOT a repeat, so the card quotes the FULL
        /// payout. That is the forgiving direction for a promise the settle can still keep;
        /// under-quoting a payout that arrives larger is a pleasant surprise, over-quoting
        /// one that arrives smaller is the defect this ticket is.</para>
        /// </summary>
        public static Func<string, bool> RepeatInCycleProvider;

        /// <summary>
        /// WO-1461 - HOW MUCH ROOM DOES THE TOWN BANK HAVE for one resource, right now?
        /// Wired in <c>RaidSelectionScreen.OpenInternal</c> to
        /// <c>TownBankCapacity.RoomFor</c>, the ONE capacity authority (its own
        /// [one-reader] guard exists so this arithmetic cannot be re-derived elsewhere and
        /// disagree).
        ///
        /// <para>Unwired / null / throwing = UNLIMITED room, so the card paints no cache
        /// notice at all. A headless or pre-state frame must never tell the player their
        /// storage is full when it cannot read the storage.</para>
        /// </summary>
        public static Func<DeNelle.Core.Economy.BankResource, int> BankRoomProvider;

        private readonly List<SceneConfigDef> _defs = new List<SceneConfigDef>();
        private readonly List<ItemVM> _raids = new List<ItemVM>();
        private readonly Dictionary<string, SceneConfigDef> _byId =
            new Dictionary<string, SceneConfigDef>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> _spoilsById =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, int> _starsById =
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        /// <summary>WO-1562: per-camp cleared flag, resolved once per <see cref="Rebuild"/>.</summary>
        private readonly Dictionary<string, bool> _clearedById =
            new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        private readonly Action _onClose;
        private readonly int _victories;
        private readonly int _deployableTroops;
        private readonly Func<string, bool> _sceneAvailable;
        private readonly Func<string, int> _bestStars;
        private readonly Func<string, bool> _claimed;
        private readonly Func<string, bool> _repeatInCycle;
        private readonly Func<DeNelle.Core.Economy.BankResource, int> _bankRoom;
        /// <summary>WO-1461: per-camp Raid Cache notice, resolved once per <see cref="Rebuild"/>.</summary>
        private readonly Dictionary<string, string> _cacheNoticeById =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private bool _showStarPips;
        private bool _disposed;

        // ── IPanelViewModel ───────────────────────────────────────────────────

        public event Action Changed;

        public string Title => "RAIDS";

        public void Close() => _onClose?.Invoke();

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Changed = null;
        }

        // ── Read-only data the View renders ─────────────────────────────────────

        /// <summary>One card per raid (Name = raw displayName, may be empty — the View
        /// falls back to a spaced id; Locked + LockReason carry the escalation gate).
        /// Never null.</summary>
        public IReadOnlyList<ItemVM> Raids => _raids;

        // -- WO-1442: how many camps there are, said in words -------------------

        /// <summary>Player-facing noun for the camp count - singular at one, never "camp(s)".</summary>
        public const string CampWordSingular = " camp";
        public const string CampWordPlural   = " camps";
        /// <summary>The instruction half of the overflow caption. ASCII only (device tofu risk).</summary>
        public const string CampScrollHint = " - drag the list to see them all.";

        /// <summary>
        /// WO-1442 - THE SCROLL AFFORDANCE, IN WORDS, WITH THE COUNT IN IT.
        /// -----------------------------------------------------------------------
        /// The owner had four camps, could see two and a half, and had no way to learn that a
        /// fourth existed: the list scrolled and its 7-device-px rail was the only thing that
        /// said so. This sentence is what a player actually reads - and because it counts
        /// <see cref="Raids"/>, it says "8 camps" the day she has earned eight, with no number
        /// typed in the View and none typed here.
        ///
        /// ⛔ WORDS, NOT A COLOUR AND NOT A GLYPH (the owner is red/green colourblind, and this
        /// screen's whole lock/difficulty language already carries its meaning in words). It is
        /// unchanged in greyscale because it never had a hue to lose.
        ///
        /// <paramref name="visibleCards"/> is the WHOLE cards the well seats, derived by the
        /// View from live geometry (<c>RaidSelectionScreen.VisibleCardCapacity</c>). It decides
        /// only whether the hint half is appended - never what the count says.
        /// Null when there are no camps at all: the View already paints its own empty state.
        /// </summary>
        public string CampCountLine(int visibleCards) =>
            CampCountLine(_raids != null ? _raids.Count : 0, visibleCards);

        /// <summary>
        /// The sentence itself, PURE — so RaidSelectionLayoutRegression can assert the exact
        /// words at four camps and at eight without standing up a catalog. The instance
        /// overload above is the only caller that decides <c>camps</c>; splitting it any other
        /// way would leave the suite proving a copy of the copy.
        /// </summary>
        public static string CampCountLine(int camps, int visibleCards)
        {
            if (camps <= 0) return null;
            string counted = camps + (camps == 1 ? CampWordSingular : CampWordPlural);
            return visibleCards >= camps ? counted + "." : counted + CampScrollHint;
        }

        /// <summary>The raw SceneConfigDef for a card id (the View forwards it to the deploy
        /// screen so it never re-pulls the catalog itself), or null.</summary>
        public SceneConfigDef DefFor(string id) =>
            id != null && _byId.TryGetValue(id, out var d) ? d : null;

        // Per-card presentation inputs (raw values; the View formats colour/time/hint).
        public string DifficultyFor(string id) { var d = DefFor(id); return d != null ? d.difficulty : null; }
        public float TargetTimeFor(string id) { var d = DefFor(id); return d != null ? d.recommendedClearTime : 0f; }
        public float RewardMultiplierFor(string id) { var d = DefFor(id); return d != null ? d.rewardMultiplier : 1f; }
        public float ShardChanceFor(string id) { var d = DefFor(id); return d != null ? d.shardDropChance : 0f; }
        /// <summary>The creative canon's one-line card copy for this target (may be null/empty).</summary>
        public string DescriptionFor(string id) { var d = DefFor(id); return d != null ? d.description : null; }
        /// <summary>Authored victory threshold for this target (0 = always available).</summary>
        public int UnlockVictoriesFor(string id) { var d = DefFor(id); return d != null ? d.unlockVictories : 0; }

        // -- WO-1402: what a raid PAYS, and whether the army can take it ----------

        /// <summary>
        /// The row's spoils line - <c>Spoils: ~1800 wood, ~1100 iron, ~2200 gold</c> - or null
        /// when the estimate is all zero (the View then paints no line and the trace says why).
        /// Computed ONCE per row in <see cref="Rebuild"/> from <see cref="EstimateSpoils"/>;
        /// the View only renders the string.
        /// </summary>
        public string SpoilsLineFor(string id) =>
            id != null && _spoilsById.TryGetValue(id, out var s) ? s : null;

        /// <summary>
        /// WO-1461 - the row's Raid Cache notice, or null when the town bank has room for the
        /// whole quote. Computed once per row in <see cref="Rebuild"/>; the View only renders it.
        /// </summary>
        public string CacheNoticeLineFor(string id) =>
            id != null && _cacheNoticeById.TryGetValue(id, out var s) ? s : null;

        /// <summary>
        /// WO-1461 - is a clear of this camp a repeat inside its cooldown cycle? Guarded, and a
        /// fault resolves FALSE (quote the full payout) with a log, never a swallow.
        /// </summary>
        private bool ResolveRepeatInCycle(string id)
        {
            if (string.IsNullOrEmpty(id) || _repeatInCycle == null) return false;
            try { return _repeatInCycle(id); }
            catch (Exception ex)
            {
                DeNelle.Core.Diagnostics.FlowTrace.Warn("Raid",
                    "RaidSelectionVM: RepeatInCycleProvider threw for '" + id + "' (" +
                    ex.GetType().Name + ": " + ex.Message + ") - the row quotes the FULL payout. " +
                    "Under-quoting a payout that arrives larger is the forgiving direction.");
                return false;
            }
        }

        /// <summary>
        /// WO-1461 - headroom in the town bank for one resource. Unwired / throwing resolves
        /// UNLIMITED, so a headless or pre-state frame paints no cache notice at all rather
        /// than telling the player their storage is full when it cannot read the storage.
        /// </summary>
        private int ResolveBankRoom(DeNelle.Core.Economy.BankResource r)
        {
            if (_bankRoom == null) return int.MaxValue;
            try { return _bankRoom(r); }
            catch (Exception ex)
            {
                DeNelle.Core.Diagnostics.FlowTrace.Warn("Raid",
                    "RaidSelectionVM: BankRoomProvider threw for " + r + " (" + ex.GetType().Name +
                    ": " + ex.Message + ") - treating the bank as UNLIMITED, so no cache notice " +
                    "is painted on any row.");
                return int.MaxValue;
            }
        }

        /// <summary>
        /// WO-1461 - room left in the Raid Cache, per resource. Read off the ONE cache
        /// authority. Guarded: a fault resolves ZERO room, so the notice under-promises rather
        /// than telling the player a full cache will hold their haul.
        /// </summary>
        private static int ResolveCacheRoom(DeNelle.Core.Economy.BankResource r)
        {
            try { return DeNelle.Village.World.Camps.RaidClaimService.CacheRoomFor(r); }
            catch (Exception ex)
            {
                DeNelle.Core.Diagnostics.FlowTrace.Warn("Raid",
                    "RaidSelectionVM: the Raid Cache would not report its room for " + r + " (" +
                    ex.GetType().Name + ": " + ex.Message + ") - quoting ZERO cache room, which " +
                    "under-promises rather than promising a hold that may not exist.");
                return 0;
            }
        }

        /// <summary>
        /// The ESTIMATE behind the spoils line - the settle payout's own formula
        /// (<c>RaidScoring.EstimateSpoils</c> -> <c>ProjectLoot</c> -> <c>ComputeLoot</c>, the
        /// same chain <c>RaidScoring.LootFor</c> pays through at settle), quoted at a clean
        /// 3-star clear. There is deliberately NO second loot table and NO literal here: the
        /// camp authors a <c>rewardMultiplier</c>, the tunable rail authors the bases, and the
        /// scorer owns the arithmetic. Static so the oracle can call it beside the VM.
        /// </summary>
        public static DeNelle.Village.ResourceCost EstimateSpoils(SceneConfigDef d)
        {
            if (d == null) return default(DeNelle.Village.ResourceCost);
            return RaidScoring.EstimateSpoils(d.id, d.rewardMultiplier);
        }

        /// <summary>
        /// Formats an estimate as the row's line. A RANGE-FEEL "~" on every number (owner
        /// ruling WO-1402: a range or estimate, never exact); zero currencies are dropped;
        /// all-zero returns null. Wood, iron, gold in the economy map's order. Pure, static,
        /// ASCII - the oracle asserts on this exact grammar.
        /// </summary>
        public static string FormatSpoils(DeNelle.Village.ResourceCost est)
        {
            var parts = new List<string>(3);
            if (est.Wood > 0) parts.Add("~" + Approx(est.Wood) + " wood");
            if (est.Iron > 0) parts.Add("~" + Approx(est.Iron) + " iron");
            if (est.Coins > 0) parts.Add("~" + Approx(est.Coins) + " gold");
            return parts.Count == 0 ? null : SpoilsPrefix + string.Join(", ", parts);
        }

        /// <summary>
        /// WO-1461 - the estimate AFTER the repeat-clear share, which is the figure the card
        /// must quote. Pure and static: it delegates to <c>RaidClaimService.ScaleLootForClear</c>,
        /// the SAME method <c>RaidVictoryController.ApplyFirstClearGate</c> pays through, so
        /// the card cannot advertise a rate the settle does not pay.
        ///
        /// <para><c>crystalsAlreadyPaidToday</c> is passed FALSE deliberately. The card is a
        /// pre-raid quote and the crystal day-stamp is decided at settle; quoting zero crystals
        /// because the player already raided today would be correct only until UTC midnight,
        /// and the spoils line does not render crystals at all (see <see cref="FormatSpoils"/>,
        /// wood / iron / gold only). Recorded rather than left as an unexplained literal.</para>
        /// </summary>
        public static DeNelle.Village.ResourceCost RepeatScaled(DeNelle.Village.ResourceCost est,
                                                                bool repeatInCycle)
            => DeNelle.Village.World.Camps.RaidClaimService.ScaleLootForClear(est, repeatInCycle, false);

        /// <summary>
        /// WO-1461 - the Raid Cache notice under the spoils line:
        /// <c>Storage full - ~1075 wood, ~275 iron waits in the Raid Cache</c>, or null when
        /// the bank has room for the whole quote (the common case, and the card then says
        /// nothing rather than teaching a worry the player does not have).
        ///
        /// <para>Pure and static - every ceiling arrives as an int, so the oracle asserts the
        /// exact grammar and the exact split with no scene, no save and no bank loaded. The
        /// split itself is <c>RaidClaimService.SplitAxis</c>, the settle path's own method.</para>
        ///
        /// <para>Rooms are per resource; <c>int.MaxValue</c> means "no ceiling", which is what
        /// an unwired <see cref="BankRoomProvider"/> supplies.</para>
        ///
        /// <para>⚠ THIS LINE CARRIES EXACT NUMBERS WHILE THE SPOILS LINE ABOVE IT CARRIES "~"
        /// ROUNDED ONES, AND THAT IS DELIBERATE, NOT AN INCONSISTENCY. The owner's own example
        /// message is exact - <i>"1,775 Wood held in Raid Cache - storage full"</i> - and the
        /// point of the line is that a specific quantity is WAITING for the player, which a
        /// number rounded to the nearest hundred stops meaning. The "~" on the spoils line is
        /// WO-1402's separate ruling about a payout nobody can predict, and it is untouched.
        /// The consequence to know: <c>quoted == banked + cached</c> holds EXACTLY on the
        /// numbers (<c>RaidClaimService.SplitAxis</c>, which is what
        /// <c>SpoilsAreBankableRegression</c> asserts) and only approximately on the two
        /// rendered strings, because one of them is rounded on purpose.</para>
        /// </summary>
        public static string CacheNotice(DeNelle.Village.ResourceCost scaled,
                                         int woodRoom, int ironRoom, int foodRoom,
                                         int woodCacheRoom, int ironCacheRoom, int foodCacheRoom)
        {
            var parts = new List<string>(3);
            AppendCachePart(parts, scaled.Wood, woodRoom, woodCacheRoom, "wood");
            AppendCachePart(parts, scaled.Iron, ironRoom, ironCacheRoom, "iron");
            AppendCachePart(parts, scaled.Food, foodRoom, foodCacheRoom, "stone");
            return parts.Count == 0 ? null : CacheNoticePrefix + string.Join(", ", parts) + CacheNoticeSuffix;
        }

        private static void AppendCachePart(List<string> parts, int amount, int bankRoom,
                                            int cacheRoom, string word)
        {
            var d = DeNelle.Village.World.Camps.RaidClaimService.SplitAxis(amount, bankRoom, cacheRoom);
            if (d.Cached > 0) parts.Add(d.Cached + " " + word);
        }

        /// <summary>
        /// Rounds an estimate to a number that READS as an estimate: to the nearest 50 below
        /// 1000, the nearest 100 at or above. 1980 -> 2000, 1650 -> 1700, 275 -> 300. Never 0
        /// for a positive input.
        /// </summary>
        public static int Approx(int amount)
        {
            if (amount <= 0) return 0;
            int step = amount < 1000 ? 50 : 100;
            int rounded = (int)(Math.Round(amount / (double)step) * step);
            return rounded < step ? step : rounded;
        }

        /// <summary>Fieldable troop bodies this projection was built against; <see cref="Unknown"/> when unwired.</summary>
        public int DeployableTroops => _deployableTroops;

        /// <summary>
        /// <c>Outmatched - Army N advised</c> when this camp's garrison headcount exceeds the army
        /// the player can field (WO-1389 compare rule: garrison bodies vs deployable bodies);
        /// null when the army covers it OR when the army is <see cref="Unknown"/>. The colour
        /// edge bar may keep painting the tier; this WORD is what carries the state, because
        /// the owner is red/green colourblind and a hue is not a sentence.
        ///
        /// <para>WO-1542: the card carrying this word STAYS AT FULL BRIGHTNESS, and that is now
        /// CORRECT rather than a second defect. <c>RaidSelectionScreen</c>'s <c>dimmed</c> is
        /// bound to the ESCALATION lock alone; dimming an over-matched camp would say
        /// "unavailable" about a camp the player may march on today.</para>
        /// </summary>
        public string ArmyWarnWordFor(string id) => ArmyWarnWord(DefFor(id), _deployableTroops);

        /// <summary>Static form so the oracle, the grid VM and the DEPLOY VM produce the
        /// identical word from the identical two numbers. <c>RaidDeployVM</c> calls this with its
        /// <c>DeployableCount</c> (the raw HEADCOUNT, the same axis WO-1389's "you field N"
        /// compare line uses) - never with the slot-weighted <c>Fielded</c>, which would let the
        /// grid say Outmatched while the deploy screen stayed silent. That drift is exactly the
        /// two-producer defect this ticket exists to close.</summary>
        public static string ArmyWarnWord(SceneConfigDef d, int deployableTroops)
        {
            if (d == null || deployableTroops < 0) return null;
            int garrison = GarrisonCount(d);
            return garrison > deployableTroops ? ArmyWarnPrefix + garrison + ArmyWarnSuffix : null;
        }

        /// <summary>
        /// WO-1542 (owner ruling appended 2026-09-06 22:20, "add the confirm toast") - the
        /// sentence BEGIN ASSAULT shows when the player marches on an over-matched camp.
        ///
        /// <para>NOTE - IT IS A CONFIRM STEP, NOT A GATE. It never refuses, it asks ONCE, and the
        /// second tap marches. The VM composes the words; the View only shows them. Null when the
        /// army covers the garrison or the army is <see cref="Unknown"/> - the same predicate as
        /// <see cref="ArmyWarnWord"/>, read from the same two numbers, so the grid warning and the
        /// footer confirm can never disagree about which camp is above the army.</para>
        /// </summary>
        public static string OutmatchConfirmToast(SceneConfigDef d, int deployableTroops)
        {
            if (ArmyWarnWord(d, deployableTroops) == null) return null;
            int garrison = GarrisonCount(d);
            return "Outmatched: " + garrison + " defenders against your " + deployableTroops +
                   ". Tap BEGIN ASSAULT again to march anyway.";
        }

        /// <summary>Instance form for the grid, same two numbers.</summary>
        public string OutmatchConfirmToastFor(string id) =>
            OutmatchConfirmToast(DefFor(id), _deployableTroops);

        // =====================================================================
        //  WO-1562 PART 2 - THE RETURN LEG OF THE LOOP GETS A MEMORY
        // =====================================================================
        //  The clear was persisted and never read back: grepping this file and
        //  RaidSelectionScreen for RaidClaimService / IsClaimed / Cleared returned
        //  COMMENTS ONLY. So a camp the player had already broken read exactly like
        //  one they had never fought, and nothing warned that a repeat clear pays a
        //  fraction - which the player then discovered AFTER committing.
        //
        //  NOT covered by WO-1461, which puts repeat-clear economics on the DEPLOY
        //  CARD. A player choosing among four camps chooses on the GRID, one screen
        //  earlier. This is DISCLOSURE ONLY - it re-authors no number.
        // =====================================================================

        /// <summary>Leading word of the cleared marker. ASCII; carried in WORDS because the owner
        /// is red/green colourblind and a tint would say nothing to her.</summary>
        public const string ClearedPrefix = "CLEARED";
        /// <summary>Joins the marker to the repeat-clear disclosure.</summary>
        public const string ClearedRepeatJoin = " - repeats pay ";

        /// <summary>True when this camp has already been broken (read from the claim service
        /// through <see cref="ClaimedProvider"/>, never from a second predicate).</summary>
        public bool IsClearedFor(string id) =>
            id != null && _clearedById.TryGetValue(id, out var c) && c;

        /// <summary>
        /// The grid row's cleared marker - <c>CLEARED - repeats pay 25%</c> - or null when the
        /// camp has not been broken.
        ///
        /// <para>STOP - THE PERCENTAGE IS READ, NEVER TYPED. It formats
        /// <c>RaidClaimService.RepeatClearLootMultiplier</c>, the same constant
        /// <c>RaidVictoryController.ApplyFirstClearGate</c> pays through, so this line states
        /// whatever WO-1461 lands and can never advertise a rate the settle does not pay.</para>
        ///
        /// <para>NOTE - CONTRADICTION RECORDED, NOT RESOLVED: WO-1562 and WO-1534 section A6 both say
        /// WO-1461 sets the repeat rate at 60%; the live constant read 0.25f on 2026-09-06. The
        /// number is not restated here precisely so this line follows the constant when that
        /// ticket lands, whichever way it lands.</para>
        /// </summary>
        public string ClearedWordFor(string id) => ClearedWord(IsClearedFor(id));

        /// <summary>Static form so the oracle asserts the exact grammar with no catalog and no
        /// save loaded.</summary>
        public static string ClearedWord(bool cleared)
        {
            if (!cleared) return null;
            int pct = (int)Math.Round(
                DeNelle.Village.World.Camps.RaidClaimService.RepeatClearLootMultiplier * 100.0);
            return ClearedPrefix + ClearedRepeatJoin + pct + "%";
        }

        /// <summary>Best star rating recorded for this camp, or <see cref="Unknown"/>.</summary>
        public int BestStarsFor(string id) =>
            id != null && _starsById.TryGetValue(id, out var s) ? s : Unknown;

        /// <summary>
        /// True only when at least two rows carry a KNOWN rating and those ratings DIFFER.
        /// Identical pips on every row say nothing (merged UI review row 1), so uniform or
        /// unknown ratings hide the row of pips entirely; the View reads this once per build.
        /// </summary>
        public bool ShowStarPips => _showStarPips;

        /// <summary>
        /// WO-1389 pressure point 4 - what the wins BUY, before the player can enter: the scout
        /// line for a card, "Iron walls . 15 defenders" (wall tier + garrison headcount from the
        /// def, the same two facts RaidDeployVM's scout report opens with). Null when the def
        /// authors neither, so an unauthored row paints nothing. Pure string work.
        /// </summary>
        public string ScoutLineFor(string id) => ScoutLine(DefFor(id));

        /// <summary>
        /// WO-1389 - the NEXT camp the ladder has not yet opened for <paramref name="victories"/>
        /// banked wins: the first FLAGSHIP def (the same ordered list CreateDefault renders) whose
        /// authored unlockVictories exceeds the count. Null when every camp is open (the ladder is
        /// climbed) or the catalog resolves nothing - the post-raid dialogue then drops its camp
        /// sentence rather than inventing one. ONE resolution site, shared by the dialogue text
        /// tokens (PostRaidBeatTokens) and the regression oracle, so the card and the sentence can
        /// never name different camps.
        /// </summary>
        public static SceneConfigDef NextLockedCamp(int victories)
        {
            if (victories < 0) victories = 0;
            SceneConfigDef best = null;
            foreach (var id in FlagshipRaidIds)
            {
                var def = SceneConfigCatalog.Find(id);
                if (def == null)
                {
                    DeNelle.Core.Diagnostics.FlowTrace.Warn("Raid",
                        "NextLockedCamp: flagship raid id '" + id + "' does not resolve in scene-configs.json - skipped.");
                    continue;
                }
                if (def.unlockVictories <= victories) continue;
                if (best == null || def.unlockVictories < best.unlockVictories) best = def;
            }
            DeNelle.Core.Diagnostics.FlowTrace.Step("Raid", "NextLockedCamp(victories=" + victories + ") -> " +
                (best != null ? "'" + best.id + "' at " + best.unlockVictories + " wins" : "<none - ladder climbed>"));
            return best;
        }

        // =====================================================================
        //  WO-1562 PART 1 - THE LADDER ANNOUNCEMENT, FROM THE ONE LADDER AUTHORITY
        // =====================================================================

        /// <summary>The camp the ladder OPENED at exactly <paramref name="victories"/> banked wins,
        /// or null when this win crossed nothing.
        ///
        /// <para><b>WHY THIS LIVES HERE AND NOT IN RaidVictoryController.</b>
        /// <c>ResolveUnlockLine</c> returned null unconditionally, and its own comment said why:
        /// naming a target there "would fork the ladder across two files, which is the duplicated
        /// state that makes this repo's most expensive bugs". So the announcement reads the SAME
        /// authority the grid's lock sentences read - <see cref="FlagshipRaidIds"/> plus each
        /// def's authored <c>unlockVictories</c>, the identical pair <see cref="ResolveLock"/> and
        /// <see cref="NextLockedCamp"/> consult. There is still ONE ladder and no second copy of
        /// the thresholds; WO-1562 acceptance 2.</para>
        ///
        /// <para><b>"CROSSED ON THIS WIN" IS EXACT, NOT APPROXIMATE, AND THE EQUALITY IS THE
        /// PROOF.</b> <c>RaidVictoryController.RecordVictory</c> increments
        /// <c>GameState.RaidVictories</c> by ONE per settled win, from the one <c>_handled</c>
        /// latch, and the counter is monotonic - so the count equals any given threshold on
        /// EXACTLY ONE win, ever. A repeat clear of an already-claimed camp still increments (the
        /// counter is wins, not claims), and it still cannot re-announce, because it lands on a
        /// count the ladder has already passed. No previous-count parameter is needed and none is
        /// taken: an extra input here would be a second thing to keep in sync.</para></summary>
        public static SceneConfigDef CampUnlockedAt(int victories)
        {
            if (victories <= 0) return null;
            SceneConfigDef crossed = null;
            foreach (var id in FlagshipRaidIds)
            {
                var def = SceneConfigCatalog.Find(id);
                if (def == null) continue;      // NextLockedCamp already warns by name for a missing id
                if (def.unlockVictories != victories) continue;
                // Deterministic on the (mis-authored) case of two camps sharing a threshold: the
                // FIRST in the flagship order wins, and the collision is reported rather than
                // silently picking one (CLAUDE.md section 12 - no silent failures).
                if (crossed == null) { crossed = def; continue; }
                DeNelle.Core.Diagnostics.FlowTrace.Warn("Raid",
                    "CampUnlockedAt(" + victories + "): '" + crossed.id + "' and '" + def.id +
                    "' both author unlockVictories=" + victories + ". Announcing the first in the " +
                    "flagship order; scene-configs.json should not put two camps on one rung.");
            }
            return crossed;
        }

        /// <summary>The sentence a victory screen announces when this win opened a camp, or null.
        /// Static and pure so the oracle asserts the exact words without a victory scene, and so
        /// the announcement can never name a camp the grid does not.</summary>
        public static string UnlockAnnouncementFor(int victories)
        {
            var crossed = CampUnlockedAt(victories);
            if (crossed == null) return null;
            string name = !string.IsNullOrEmpty(crossed.displayName)
                ? crossed.displayName : crossed.id;
            return UnlockPrefix + name + UnlockSuffix;
        }

        /// <summary>Leading half of the unlock announcement; the oracle asserts on it. ASCII.</summary>
        public const string UnlockPrefix = "New target unlocked: ";
        /// <summary>Trailing half - it names the DOOR, because an announcement the player cannot
        /// act on is only half a beat. ASCII.</summary>
        public const string UnlockSuffix = ". It is open on the raid board.";

        /// <summary>Static composer so the dialogue token and the oracle read the SAME sentence.</summary>
        public static string ScoutLine(SceneConfigDef d)
        {
            if (d == null) return null;
            var parts = new List<string>(2);
            if (!string.IsNullOrEmpty(d.wallTier)) parts.Add(SpaceCamelCase(d.wallTier) + " walls");
            int defenders = GarrisonCount(d);
            if (defenders > 0) parts.Add(defenders + (defenders == 1 ? " defender" : " defenders"));
            return parts.Count == 0 ? null : string.Join(" . ", parts);
        }

        /// <summary>Garrison headcount authored on a def (sum of composition counts), 0 when none.</summary>
        public static int GarrisonCount(SceneConfigDef d)
        {
            if (d == null || d.garrison == null || d.garrison.composition == null) return 0;
            int n = 0;
            foreach (var u in d.garrison.composition)
                if (u != null && u.count > 0) n += u.count;
            return n;
        }

        /// <summary>"ReinforcedSteel" -> "Reinforced Steel" (mirrors RaidDeployVM; no regex).</summary>
        private static string SpaceCamelCase(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var sb = new System.Text.StringBuilder(s.Length + 4);
            for (int i = 0; i < s.Length; i++)
            {
                if (i > 0 && char.IsUpper(s[i]) && !char.IsUpper(s[i - 1]) && s[i - 1] != ' ')
                    sb.Append(' ');
                sb.Append(s[i]);
            }
            return sb.ToString();
        }

        // ── Construction / resolution ───────────────────────────────────────────

        /// <summary>The ONLY resolution site: pulls the flagship raids (fallback to all
        /// enemy raids) from <see cref="SceneConfigCatalog"/> so the View never touches it.</summary>
        public static RaidSelectionVM CreateDefault(Action onClose = null)
        {
            var list = new List<SceneConfigDef>();
            foreach (var id in FlagshipRaidIds)
            {
                var def = SceneConfigCatalog.Find(id);
                if (def != null) list.Add(def);
            }
            if (list.Count == 0)
                foreach (var def in SceneConfigCatalog.All)
                    if (def != null && def.IsEnemy) list.Add(def);

            int victories = 0;
            var provider = VictoryCountProvider;
            if (provider != null)
            {
                // Guarded: a provider fault must never blank the raid grid, and must never be
                // swallowed without a log (CLAUDE.md §12).
                try { victories = provider(); }
                catch (Exception ex)
                {
                    DeNelle.Core.Diagnostics.FlowTrace.Warn("Raid",
                        "RaidSelectionVM: VictoryCountProvider threw (" + ex.GetType().Name + ": " +
                        ex.Message + ") - treating the player as 0 victories, so every gated camp " +
                        "shows LOCKED with its reason rather than silently unlocking.");
                    victories = 0;
                }
            }
            else
            {
                DeNelle.Core.Diagnostics.FlowTrace.Warn("Raid",
                    "RaidSelectionVM: no VictoryCountProvider wired - the raid-victory counter is " +
                    "another lane's file in this release. Treating the player as 0 victories, so " +
                    "camps gated above 0 read LOCKED with their reason. Wire it with " +
                    "RaidSelectionVM.VictoryCountProvider = () => theirCounter.");
            }

            // WO-1402 - the army input. Unknown (-1) when unwired or faulting, never 0: a 0
            // would print "Outmatched - Army N advised" on every camp of a headless frame, which
            // is advice the frame cannot prove.
            int deployable = Unknown;
            var armyProvider = DeployableTroopsProvider;
            if (armyProvider != null)
            {
                try { deployable = armyProvider(); }
                catch (Exception ex)
                {
                    DeNelle.Core.Diagnostics.FlowTrace.Warn("Raid",
                        "RaidSelectionVM: DeployableTroopsProvider threw (" + ex.GetType().Name + ": " +
                        ex.Message + ") - army UNKNOWN, so no row carries the army lock word.");
                    deployable = Unknown;
                }
            }
            else
            {
                DeNelle.Core.Diagnostics.FlowTrace.Step("Raid",
                    "RaidSelectionVM: no DeployableTroopsProvider wired - army UNKNOWN, no row " +
                    "carries 'Outmatched - Army N advised' (expected headless / EditMode).");
            }

            return new RaidSelectionVM(list, onClose, victories, SceneAvailableProvider,
                                       deployable, BestStarsProvider, ClaimedProvider,
                                       RepeatInCycleProvider, BankRoomProvider);
        }

        public RaidSelectionVM(IReadOnlyList<SceneConfigDef> defs, Action onClose,
                               int victories = 0, Func<string, bool> sceneAvailable = null,
                               int deployableTroops = Unknown, Func<string, int> bestStars = null,
                               Func<string, bool> claimed = null,
                               Func<string, bool> repeatInCycle = null,
                               Func<DeNelle.Core.Economy.BankResource, int> bankRoom = null)
        {
            _onClose = onClose;
            _victories = victories < 0 ? 0 : victories;
            _sceneAvailable = sceneAvailable;
            _deployableTroops = deployableTroops < 0 ? Unknown : deployableTroops;
            _bestStars = bestStars;
            _claimed = claimed;
            _repeatInCycle = repeatInCycle;
            _bankRoom = bankRoom;
            if (defs != null)
                foreach (var d in defs)
                {
                    if (d == null) continue;
                    _defs.Add(d);
                    if (!string.IsNullOrEmpty(d.id)) _byId[d.id] = d;
                }
            Rebuild();
        }

        /// <summary>Banked raid victories this projection was built against (read-only, for traces).</summary>
        public int Victories => _victories;

        /// <summary>True when this card is gated shut. Mirrors the ItemVM the View renders.</summary>
        public bool IsLocked(string id)
        {
            var d = DefFor(id);
            return d != null && ResolveLock(d) != null;
        }

        /// <summary>The player-facing sentence for a locked card, or null when it is open.</summary>
        public string LockReasonFor(string id)
        {
            var d = DefFor(id);
            return d != null ? ResolveLock(d) : null;
        }

        /// <summary>
        /// THE ONE LOCK RESOLVER - returns the player-facing reason, or null when the card is open.
        ///
        /// <para>Order matters and is deliberate: the PROGRESSION gate is checked first, so a
        /// player who has not earned a target is told to go earn it (the actionable sentence)
        /// rather than told the expedition is not ready (true, but nothing they can do).
        /// Availability is the fallback for a target the player HAS earned whose scene this
        /// build cannot load.</para>
        ///
        /// <para>NEVER A BARE "Locked". Both sentences name the missing thing AND the remedy,
        /// and both stand on their own in greyscale - the owner is red/green colourblind, so the
        /// state is carried by the WORDS (the same law RaidCooldownService's copy follows).
        /// Voice: docs/CREATIVE_CANON_ELARION_2026-09-04.md §0/§3. ASCII only.</para>
        /// </summary>
        private string ResolveLock(SceneConfigDef d)
        {
            int need = d.unlockVictories;
            if (need > 0 && _victories < need)
            {
                int remaining = need - _victories;
                return "The Heart cannot reach this far yet - win " + remaining +
                       (remaining == 1 ? " more raid" : " more raids") + " to press on.";
            }

            var avail = _sceneAvailable;
            if (avail != null && !string.IsNullOrEmpty(d.sceneName))
            {
                bool ok;
                try { ok = avail(d.sceneName); }
                catch (Exception ex)
                {
                    // Never swallow (CLAUDE.md §12). Fail OPEN on a probe fault: SceneRouter's own
                    // IsSceneRegistered gate still refuses an unloadable scene, so the worst case
                    // is a refusal one screen later - strictly better than hiding an earned target.
                    DeNelle.Core.Diagnostics.FlowTrace.Warn("Raid",
                        "RaidSelectionVM: scene-availability probe threw for '" + d.sceneName + "' (" +
                        ex.GetType().Name + ": " + ex.Message + ") - treating it as available.");
                    ok = true;
                }
                if (!ok)
                {
                    DeNelle.Core.Diagnostics.FlowTrace.Warn("Raid",
                        "RaidSelectionVM: '" + d.id + "' is EARNED (" + _victories + " of " + need +
                        " victories) but its scene '" + d.sceneName + "' cannot be loaded in this " +
                        "build - the card stays locked with a sentence instead of dead-ending the " +
                        "player. Register the scene ENABLED in Build Settings once it bakes a " +
                        "HeroStartPoint_PlayerSpawn marker.");
                    return "The Heart remembers no fortress here. This expedition is not ready.";
                }
            }

            return null;
        }

        private void Rebuild()
        {
            _raids.Clear();
            _spoilsById.Clear();
            _starsById.Clear();
            _clearedById.Clear();
            _cacheNoticeById.Clear();

            // WO-1562 - the CLEARED flag, resolved once per row from the ONE claim authority.
            // Guarded: a provider fault must never blank the grid and must never be swallowed
            // without a log (CLAUDE.md section 12). A fault resolves NOT CLEARED - the forgiving
            // direction, because claiming a win the save cannot prove is the worse lie.
            foreach (var d in _defs)
            {
                if (d == null || string.IsNullOrEmpty(d.id)) continue;
                bool cleared = false;
                if (_claimed != null)
                {
                    try { cleared = _claimed(d.id); }
                    catch (Exception ex)
                    {
                        DeNelle.Core.Diagnostics.FlowTrace.Warn("Raid",
                            "RaidSelectionVM: ClaimedProvider threw for '" + d.id + "' (" +
                            ex.GetType().Name + ": " + ex.Message + ") - the row reads NOT CLEARED " +
                            "rather than advertising a clear this save cannot prove.");
                        cleared = false;
                    }
                }
                _clearedById[d.id] = cleared;
            }
            if (_claimed == null)
                DeNelle.Core.Diagnostics.FlowTrace.Step("Raid",
                    "RaidSelectionVM: no ClaimedProvider wired - no row carries the CLEARED marker " +
                    "(expected headless / EditMode). Wire it with " +
                    "RaidSelectionVM.ClaimedProvider = RaidClaimService.IsClaimed.");

            // WO-1402 - star ratings first, because ShowStarPips is a property of the WHOLE
            // grid (do they vary?), not of one row.
            int knownStars = 0, minStars = int.MaxValue, maxStars = int.MinValue;
            foreach (var d in _defs)
            {
                if (d == null || string.IsNullOrEmpty(d.id)) continue;
                int stars = Unknown;
                if (_bestStars != null)
                {
                    try { stars = _bestStars(d.id); }
                    catch (Exception ex)
                    {
                        DeNelle.Core.Diagnostics.FlowTrace.Warn("Raid",
                            "RaidSelectionVM: BestStarsProvider threw for '" + d.id + "' (" +
                            ex.GetType().Name + ": " + ex.Message + ") - rating UNKNOWN for that row.");
                        stars = Unknown;
                    }
                }
                if (stars > 3) stars = 3;
                _starsById[d.id] = stars < 0 ? Unknown : stars;
                if (stars >= 0)
                {
                    knownStars++;
                    if (stars < minStars) minStars = stars;
                    if (stars > maxStars) maxStars = stars;
                }
            }
            _showStarPips = knownStars >= 2 && minStars != maxStars;

            foreach (var d in _defs)
            {
                if (d == null) continue;
                // Name carries the RAW displayName (may be empty); the View falls back to a
                // kit-spaced id so the VM never references the presentation kit.
                string name = string.IsNullOrEmpty(d.displayName) ? "" : d.displayName;
                string lockReason = ResolveLock(d);
                // ItemVM has ALWAYS carried Locked + LockReason (ItemVM.cs:32,35); this VM passed
                // neither, so every card shipped UNLOCKED at every victory count. Named args
                // because both sit behind rarity/equipped in the positional list.
                _raids.Add(new ItemVM(d.id, name, IconRoleRaid, d.id, 0, "", true,
                                      rarity: null, equipped: false,
                                      locked: lockReason != null, lockReason: lockReason));

                // WO-1402 - the spoils ESTIMATE, once per row, from the settle payout's own
                // formula. A null line is not silent: the trace below names the row.
                // WO-1461 - and then the two subtractions the card used to be blind to: the
                // repeat-clear share, and the town bank's headroom. The quote is the SCALED
                // figure; whatever the bank cannot take is NAMED as going to the Raid Cache.
                var est = EstimateSpoils(d);
                bool repeat = ResolveRepeatInCycle(d.id);
                var scaled = RepeatScaled(est, repeat);
                string spoils = FormatSpoils(scaled);
                string cacheNotice = CacheNotice(scaled,
                                                 ResolveBankRoom(DeNelle.Core.Economy.BankResource.Wood),
                                                 ResolveBankRoom(DeNelle.Core.Economy.BankResource.Iron),
                                                 ResolveBankRoom(DeNelle.Core.Economy.BankResource.Food),
                                                 ResolveCacheRoom(DeNelle.Core.Economy.BankResource.Wood),
                                                 ResolveCacheRoom(DeNelle.Core.Economy.BankResource.Iron),
                                                 ResolveCacheRoom(DeNelle.Core.Economy.BankResource.Food));
                if (!string.IsNullOrEmpty(d.id))
                {
                    _spoilsById[d.id] = spoils;
                    _cacheNoticeById[d.id] = cacheNotice;
                }
                string armyWord = ArmyWarnWord(d, _deployableTroops);
                string clearedWord = ClearedWordFor(d.id);

                DeNelle.Core.Diagnostics.FlowTrace.Step("Raid",
                    "row '" + d.id + "' spoils est=" + est.Wood + "w/" + est.Iron + "i/" + est.Coins +
                    "g x" + d.rewardMultiplier.ToString("0.##") +
                    " repeatInCycle=" + repeat + " (share " +
                    DeNelle.Village.World.Camps.RaidClaimService.RepeatClearPct + "%) quoted=" +
                    scaled.Wood + "w/" + scaled.Iron + "i/" + scaled.Coins + "g cache=" +
                    (cacheNotice != null ? "\"" + cacheNotice + "\"" : "<none - the bank has room>") +
                    " text=" +
                    (spoils != null ? "\"" + spoils + "\"" : "<none - estimate all zero>") +
                    " pips=" + (_showStarPips ? "shown" : "hidden") +
                    " lock=" + (lockReason != null ? "escalation" : armyWord != null ? "\"" + armyWord + "\"" : "none") +
                    " cleared=" + (clearedWord != null ? "\"" + clearedWord + "\"" : "no") +
                    " (garrison " + GarrisonCount(d) + ", army " +
                    (_deployableTroops < 0 ? "unknown" : _deployableTroops.ToString()) + ")");
            }

            DeNelle.Core.Diagnostics.FlowTrace.Step("Raid",
                "RaidSelectionVM projected " + _raids.Count + " raid card(s) at " + _victories +
                " victories; locked=" + CountLocked() + "; pips=" + (_showStarPips ? "shown" : "hidden") +
                " (" + knownStars + " rated); army=" +
                (_deployableTroops < 0 ? "unknown" : _deployableTroops.ToString()) + ".");
        }

        private int CountLocked()
        {
            int n = 0;
            for (int i = 0; i < _raids.Count; i++) if (_raids[i].Locked) n++;
            return n;
        }
    }
}

// =============================================================================
// BattlePlans -- WO-1804: the DOMAIN half of the two new plans kinds that ride the
// WO-1013 castle-plans seam. Pure rules + pure copy composition + the persisted
// key names. No Unity UI, no MonoBehaviour: every rule below is callable headless,
// which is what BattlePlansRegression asserts on.
// -----------------------------------------------------------------------------
// Assembly: DeNelle.Village   Namespace: DeNelle.Village
//
// OWNER RULING 2026-09-16, verbatim: "the way how we handle after the third wave we
// have that very special moment where they get the new plans why don't we after the
// second wave introduce raids the same similar way look you dropped detailed battle
// plans or something like that click here to you build your barracks and let's step
// into that".
//
// OWNER RULING 2026-09-16 (the same evening, the hackathon video's act 2): the video
// STARTS at the end of a dungeon -- the player kills the boss, the boss drops BASTION
// PLANS, the plans open the Iron Bastion, and the player goes to raid it. And,
// verbatim: "Make it the one that's the ember deep".
//
// -----------------------------------------------------------------------------
// TWO KINDS, ONE SEAM -- AND DELIBERATELY NOT A REFACTOR OF THE CASTLE PLANS.
// -----------------------------------------------------------------------------
// CastleDefensePlansService / CastleDefensePlansPickup / SpirePlansCelebration are
// NOT touched, generalised or re-parented. Hoisting a base class out of a shipping
// marquee moment to fit a second one is a structural refactor smuggled into
// player-facing work (docs/ARCHITECTURE_PRINCIPLES.md), and it would put the video's
// act 2 and the wave-3 spire beat on one blast radius three days before a deadline.
// These files MIRROR that idiom instead: the same 1 Hz spawn-from-persisted-state
// scan, the same walk-over pickup grammar, the same StoryIntroController-style
// full-screen beat. Where a number or a rule is shared it is READ from the other
// file's public const, never restated.
//
// -----------------------------------------------------------------------------
// WHY THE RULES ARE PURE STATICS HERE AND NOT INLINE IN THE SERVICE.
// -----------------------------------------------------------------------------
// CastleDefensePlansService.ShouldSpawnDrop is pure for exactly one reason -- so the
// truth table can be pinned headless instead of re-argued -- and that is the single
// most useful thing about the WO-1013 code. Same shape, carried further: the CTA
// branch, the never-relock gate and the body copy are pure too, so the regression
// asserts the WORDS and the BRANCHES without a scene, a camera or a save file.
//
// ⛔ NO DIGIT LITERAL DESCRIBES A PAYOUT IN THIS FILE. The spoils sentence is
// composed from RaidSelectionVM's own projection (RaidSelectionVM.FormatSpoils over
// RaidSelectionVM.EstimateSpoils -> RaidScoring.EstimateSpoils, the chain the settle
// pays through). The army sentence is composed from the LIVE ArmyStorage count. A
// hardcoded "~1800 wood" here would be the WO-1461 defect re-introduced on a new
// screen: the card quoting a rate the settle does not pay.
//
// ASCII only in every player-facing string (a non-ASCII glyph is tofu on device) and
// no meaning carried by colour anywhere (the owner is red/green colourblind -- memory
// owner-colorblind-delegate-visual-creative). The reveal's emphasis axes are SIZE,
// WEIGHT and POSITION, exactly as SpirePlansCelebration's are.
// =============================================================================

using System.Collections.Generic;
using DeNelle.Core.Diagnostics;
using DeNelle.Village.Hero;

namespace DeNelle.Village
{
    /// <summary>Which plans drop this is. Two kinds, one seam (WO-1804).</summary>
    public enum BattlePlansKind
    {
        /// <summary>"Enemy Battle Plans" -- dropped at the wave-2 clear beat in the
        /// defended town; exposes the first raid camp.</summary>
        EnemyCamp = 0,
        /// <summary>"Bastion Plans" -- dropped by the Ember Deep's boss on defeat;
        /// exposes (and is the ONLY key to) the Iron Bastion.</summary>
        Bastion = 1,
    }

    /// <summary>What the reveal's ONE call-to-action does.</summary>
    public enum BattlePlansCta
    {
        /// <summary>No Barracks stands: open build mode with the Barracks selected.</summary>
        BuildBarracks = 0,
        /// <summary>A Barracks stands: open the raid grid.</summary>
        OpenRaidGrid = 1,
        /// <summary>
        /// The reveal is playing OUTSIDE a hub (the Bastion beat in the boss room) and there is no
        /// Barracks to raid from: leave through the dungeon's own banked exit and let the WO-1802
        /// raid-door helper chain take it from there in town.
        ///
        /// <para>⛔ THIS FACE EXISTS TO STOP A WO-1542 MISMATCH, not to add a choice. "BUILD YOUR
        /// BARRACKS" in a boss room is a label whose door cannot open: build mode is a town verb.
        /// A card whose word and whose door disagree is the exact defect WO-1542 was opened for
        /// (a LOCKED word under a lit BEGIN ASSAULT), so the word changes with the place.</para>
        /// </summary>
        ReturnToCastle = 2,
    }

    /// <summary>
    /// WO-1804 domain: the persisted keys, the pure spawn/gate/CTA rules and the pure
    /// copy composition for both plans kinds. Nothing here touches a GameObject.
    /// </summary>
    public static class BattlePlans
    {
        /// <summary>FlowTrace system tag for every line this feature emits.</summary>
        public const string Sys = "BattlePlans";

        // =====================================================================
        //  THRESHOLD
        // =====================================================================

        /// <summary>
        /// Waves the player must have SURVIVED before the Enemy Battle Plans drop.
        /// OWNER RULING 2026-09-16: "after the second wave".
        ///
        /// <para>⛔ THIS CONST IS THE AUTHORITY AND THE REGRESSION READS IT, NEVER A 2.
        /// CastleDefensePlansService's own header records why in its first five lines: an
        /// owner ruling moved the spire threshold from 2 to 3 and the prose that restated
        /// the number went stale on the spot. So the number lives here once, the suite
        /// asserts against the const, and a future re-tune moves one token.</para>
        ///
        /// <para>The two thresholds are deliberately DIFFERENT and deliberately ADJACENT:
        /// raids are introduced one wave BEFORE the spire plans, so the player meets the
        /// offence verb first and the defence reward lands on a town that has somewhere to
        /// spend it. Read CastleDefensePlansService.RequiredWavesSurvived for the other
        /// one -- it is not copied here.</para>
        /// </summary>
        public const int RequiredWavesSurvived = 2;

        // =====================================================================
        //  IDS -- live save keys and live data ids, never display names
        // =====================================================================

        /// <summary>The camp the Enemy Battle Plans expose. A LIVE save key
        /// (RaidClaimService persists "dotr-raid-owner-&lt;id&gt;" on exactly this string)
        /// and a scene-configs.json row id -- never renamed to match a display name.
        /// The player-facing words come from that row's displayName.</summary>
        public const string CampConfigId = "raider_camp_small";

        /// <summary>The camp the Bastion Plans expose. Same live-save-key rule.</summary>
        public const string BastionConfigId = "iron_bastion";

        /// <summary>The Barracks catalog/structure id the CTA places. Spelled exactly as
        /// DeNelle.Core.HudModel.HudStateCopy and StructureSingleton.IsBuilt spell it
        /// ("barracks"); a third spelling of this id is how the CTA would place nothing.</summary>
        public const string BarracksItemId = "barracks";

        // =====================================================================
        //  PERSISTED FLAGS -- the SAME store the castle plans use. NO SCHEMA BUMP.
        // =====================================================================
        //  ProgressionUnlocks writes "unlock.<id>" into GameState.SeenTutorials via
        //  GameStateService.MarkTutorialSeen (idempotent + Save()). seenTutorials is an
        //  open string->bool map on the wire, so NO SaveSchema field and NO version bump
        //  is needed -- which is the same reason the castle plans did not bump either
        //  (ProgressionUnlocks.cs:11-13). The brief's rule holds literally: bump nothing
        //  unless the castle plans did, and they did not.
        // =====================================================================

        /// <summary>HELD flag for the Enemy Battle Plans (ProgressionUnlocks id).</summary>
        public const string CampPlansId = "enemy_battle_plans";

        /// <summary>HELD flag for the Bastion Plans (ProgressionUnlocks id). This flag IS
        /// the Iron Bastion's key -- see <see cref="ShouldGateBastion"/>.</summary>
        public const string BastionPlansId = "bastion_plans";

        /// <summary>Once-ever "the reveal screen has played" key for the camp plans, in the
        /// SAME SeenTutorials store (the SpirePlansCelebration.SeenKey idiom).</summary>
        public const string CampRevealSeenKey = "enemy_battle_plans_reveal";

        /// <summary>Once-ever reveal key for the Bastion plans.</summary>
        public const string BastionRevealSeenKey = "bastion_plans_reveal";

        /// <summary>The ProgressionUnlocks id whose HELD flag this kind writes.</summary>
        public static string PlansIdFor(BattlePlansKind kind)
            => kind == BattlePlansKind.Bastion ? BastionPlansId : CampPlansId;

        /// <summary>The SeenTutorials key this kind's reveal persists under.</summary>
        public static string RevealSeenKeyFor(BattlePlansKind kind)
            => kind == BattlePlansKind.Bastion ? BastionRevealSeenKey : CampRevealSeenKey;

        /// <summary>The scene-configs.json row id this kind exposes.</summary>
        public static string CampIdFor(BattlePlansKind kind)
            => kind == BattlePlansKind.Bastion ? BastionConfigId : CampConfigId;

        /// <summary>The drop prop's display label, on the prop and in the reveal's title.</summary>
        public static string TitleFor(BattlePlansKind kind)
            => kind == BattlePlansKind.Bastion ? "Bastion Plans" : "Enemy Battle Plans";

        // =====================================================================
        //  THE SPAWN RULES -- pure, so the truth table is pinned headless
        // =====================================================================

        /// <summary>
        /// The ONE spawn rule for the wave-2 Enemy Battle Plans drop. Mirrors
        /// <c>CastleDefensePlansService.ShouldSpawnDrop</c> and adds the two conditions the
        /// brief names, both of which the castle plans do NOT check:
        ///
        /// <para><b>NEVER DURING A LIVE WAVE.</b> The castle drop can grow while a wave is
        /// still running, because it is silent chrome -- it glints and waits. This one opens
        /// a FULL-SCREEN MODAL on walk-over, and a modal over a live wave is a death, not a
        /// moment. <c>BattleLock.IsInBattle()</c> is the existing battle authority
        /// (CastlePlansDiscoveryNudge already gates on it), so no second notion of "in a
        /// wave" is invented here.</para>
        ///
        /// <para><b>SKIPPED FOR A SAVE THAT ALREADY RAIDED.</b> The whole point is to
        /// INTRODUCE raids; a player who has been through the door does not need an
        /// introduction, and a marquee screen that teaches a thing already learned reads as
        /// a bug. The caller passes GameState.EverCompletedRaid, and the skip is TRACED
        /// rather than silent (section 12: "the drop did not spawn" and "the drop spawned
        /// fine" must never log the same nothing).</para>
        /// </summary>
        public static bool ShouldSpawnCampDrop(int wavesCompleted, bool plansHeld, bool propAlive,
                                              bool inBattle, bool everRaided)
            => !plansHeld && !propAlive && !inBattle && !everRaided
               && wavesCompleted >= RequiredWavesSurvived;

        /// <summary>
        /// The ONE spawn rule for the Bastion Plans drop at the dungeon boss. Pure for the
        /// same reason.
        ///
        /// <para><paramref name="bossDefeated"/> is the boss seam's own answer
        /// (OutpostEnemyGroupSpawner.IsBossCleared / its BossCleared event, which
        /// ComposedDungeonHost already listens to at :289-290 and turns into
        /// DungeonRuntimeState.MarkBossDefeated at :327). <paramref name="rightDungeon"/> is
        /// the DATA gate: this drop belongs to ONE authored dungeon (the owner's ruling:
        /// the Ember Deep), so a boss in any other dungeon spawns nothing.</para>
        ///
        /// <para>No in-battle term: a dungeon boss room IS combat, and the drop here is a
        /// silent prop with a shimmer -- its reveal is deferred to the hub (see
        /// BattlePlansService for the evidence that forced that, and it is the one place
        /// this feature deviates from "reveal where you pick it up").</para>
        /// </summary>
        public static bool ShouldSpawnBastionDrop(bool bossDefeated, bool rightDungeon,
                                                  bool plansHeld, bool propAlive)
            => bossDefeated && rightDungeon && !plansHeld && !propAlive;

        // =====================================================================
        //  THE BASTION LOCK -- and the never-relock rule
        // =====================================================================

        /// <summary>
        /// Is the Iron Bastion gated shut for want of the Bastion Plans?
        ///
        /// <para>⛔ <b>NEVER RE-LOCK A BASTION THE PLAYER HAS ALREADY BEEN TO.</b> This gate
        /// ships into live saves. A player who cleared or captured the Bastion before the
        /// plans existed must not open the grid and find their own conquest locked behind a
        /// dungeon they have no reason to run -- that is strictly worse than no gate at all.
        /// So the gate is refused on ANY evidence of prior contact.</para>
        ///
        /// <para><b>THE EVIDENCE, AND ITS ONE HONEST GAP.</b> Read at source 2026-09-16:
        /// <c>RaidClaimService.IsClaimed(configId)</c> is PERMANENT and set at a clear /
        /// capture (its own header: "a cleared-and-claimed base reads as the player's"), and
        /// <c>RaidCooldownService.IsOnCooldown(configId)</c> is set at every clear and
        /// EXPIRES. There is NO per-camp ATTEMPTED record anywhere in the tree
        /// (RaidFunnel's first-raid latch is install-wide PlayerPrefs, not per camp; see
        /// RaidDoorReadiness' own note on that distinction). So a save that ATTEMPTED the
        /// Bastion, LOST, and whose cooldown has since lapsed derives "no contact" and is
        /// gated once until the plans are found. That gap is recorded rather than closed by
        /// minting a new PlayerPrefs key for it -- it costs that player one dungeon run,
        /// never a lost conquest, and it is named in the WO-1804 RESULT as unproven-by-data.</para>
        /// </summary>
        public static bool ShouldGateBastion(bool plansHeld, bool everClaimed, bool onCooldown)
            => !plansHeld && !everClaimed && !onCooldown;

        /// <summary>The locked card's sentence. Names the missing thing AND where to get it,
        /// per the RaidSelectionVM.ResolveLock law ("NEVER A BARE 'Locked'"); stands on its
        /// own in greyscale because the state is carried by the WORDS. ASCII.</summary>
        public static string BastionLockSentence(string dungeonDisplayName)
        {
            string where = string.IsNullOrEmpty(dungeonDisplayName)
                ? "the deepest dungeon" : dungeonDisplayName;
            return BastionLockPrefix + where + ".";
        }

        /// <summary>Leading half of the Bastion lock sentence; the oracle asserts on it.</summary>
        public const string BastionLockPrefix = "Find the Bastion Plans in ";

        // =====================================================================
        //  THE CTA -- pure branch on Barracks presence
        // =====================================================================

        /// <summary>
        /// The reveal's ONE call-to-action. Pure: the branch is a function of whether a
        /// Barracks stands, nothing else.
        ///
        /// <para>ONE CTA, not two. The owner's words are "click here to you build your
        /// barracks and let's step into that" -- a single next thing. Offering both faces
        /// would make the player choose between a door they can use and a door they cannot,
        /// which is the WO-1542 shape (a card reading LOCKED under a lit BEGIN ASSAULT).</para>
        /// </summary>
        /// <remarks>
        /// <paramref name="inHub"/> is the second input because the Bastion reveal now plays in the
        /// BOSS ROOM (owner ruling 2026-09-16), and build mode is a town verb. Outside a hub with no
        /// Barracks the only honest next action is to go home, so the face becomes
        /// <see cref="BattlePlansCta.ReturnToCastle"/> rather than a button that cannot work.
        ///
        /// <para>The RAID face is NOT re-worded outside a hub, and that is deliberate rather than an
        /// oversight: the player's intent is honoured end to end (they leave through the dungeon's
        /// banked exit and the grid opens on arrival), and the dungeon's own "Leave dungeon?" confirm
        /// is what narrates the intermediate step. The word and the outcome agree; only the number of
        /// hops differs.</para>
        /// </remarks>
        public static BattlePlansCta ResolveCta(bool barracksBuilt, bool inHub)
        {
            if (barracksBuilt) return BattlePlansCta.OpenRaidGrid;
            return inHub ? BattlePlansCta.BuildBarracks : BattlePlansCta.ReturnToCastle;
        }

        /// <summary>The CTA's face label. ASCII, upper case to match the Obsidian button
        /// family the rest of the reveal uses.</summary>
        public static string CtaLabel(BattlePlansCta cta, string campDisplayName)
        {
            if (cta == BattlePlansCta.BuildBarracks) return "BUILD YOUR BARRACKS";
            if (cta == BattlePlansCta.ReturnToCastle) return "RETURN TO THE CASTLE";
            string name = string.IsNullOrEmpty(campDisplayName) ? "THE CAMP" : campDisplayName.ToUpperInvariant();
            return "RAID " + name;
        }

        // =====================================================================
        //  THE COPY -- composed from the projection and the live roster, never literals
        // =====================================================================

        /// <summary>
        /// The camp's player-facing name, READ from scene-configs.json. Falls back to the id
        /// (never an invented name) so a missing row is visible rather than papered over.
        /// </summary>
        public static string CampDisplayName(BattlePlansKind kind)
        {
            string id = CampIdFor(kind);
            var def = SceneConfigCatalog.Find(id);
            if (def == null || string.IsNullOrEmpty(def.displayName))
            {
                FlowTrace.Warn(Sys, "reveal copy: scene-configs.json row '" + id +
                    "' has no displayName - the beat names the raw id rather than inventing one.");
                return id;
            }
            return def.displayName;
        }

        /// <summary>
        /// WHAT IT PAYS -- the spoils sentence, composed by the settle payout's OWN formula
        /// through RaidSelectionVM's projection. There is no second loot table here and no
        /// literal: <c>RaidSelectionVM.EstimateSpoils</c> -> <c>RaidScoring.EstimateSpoils</c>
        /// is the chain <c>RaidScoring.LootFor</c> pays through, and
        /// <c>RaidSelectionVM.FormatSpoils</c> is the row's own grammar
        /// ("Spoils: ~1800 wood, ~1100 iron, ~2200 gold"), so this screen and the raid card
        /// cannot disagree about what a camp is worth.
        ///
        /// <para>Null when the projection resolves nothing (no row, or an all-zero estimate)
        /// -- the beat then simply drops the sentence instead of promising a payout it cannot
        /// name.</para>
        /// </summary>
        public static string SpoilsSentence(BattlePlansKind kind)
        {
            string id = CampIdFor(kind);
            var def = SceneConfigCatalog.Find(id);
            if (def == null) return null;
            string line = RaidSelectionVM.FormatSpoils(RaidSelectionVM.EstimateSpoils(def));
            if (string.IsNullOrEmpty(line))
                FlowTrace.Warn(Sys, "reveal copy: the spoils projection for '" + id +
                    "' resolved EMPTY - the beat drops its payout sentence rather than quoting a " +
                    "number it cannot source (never a literal).");
            return line;
        }

        /// <summary>
        /// THE ARMY SENTENCE -- and it BRANCHES, because it can be false. At the wave-2 beat
        /// a player may have no Barracks and therefore no troops at all
        /// (<paramref name="deployableBodies"/> = 0), and "your army is ready" over an empty
        /// roster is the HUD lying to a new player about the one thing the CTA is about to
        /// ask them to do. Three outcomes:
        /// <list type="bullet">
        /// <item>bodies &gt; 0  -> the army is named with its live count.</item>
        /// <item>bodies == 0 -> the sentence says the army must be raised first, which is
        ///       exactly what the BUILD YOUR BARRACKS CTA then offers.</item>
        /// <item>bodies &lt; 0 (UNKNOWN, the RaidSelectionVM.Unknown contract) -> NO
        ///       sentence at all: a headless or pre-state frame must never print a roster
        ///       it cannot prove.</item>
        /// </list>
        /// </summary>
        public static string ArmySentence(int deployableBodies)
        {
            if (deployableBodies < 0) return null;
            if (deployableBodies == 0)
                return "You have no soldiers yet. Raise a Barracks and the first squad is free.";
            return "Your free squad stands ready - " + deployableBodies +
                   (deployableBodies == 1 ? " soldier" : " soldiers") + " waiting on the word.";
        }

        // =====================================================================
        //  THE TWO CROSS-SCENE LATCHES (owner ruling 2026-09-16: "keep the reveal
        //  in the boss room")
        // =====================================================================
        //  The Bastion reveal now plays AT THE BOSS, so its CTA has to cross a scene load
        //  to reach the raid grid. Two latches carry it, and they are LATCHES, NOT EVENTS,
        //  for the reason StoreFocusRequest's header states in full: the consumer does not
        //  exist yet when the request is made, so an event fires into an empty room and is
        //  lost, while a latch survives the gap and is consumed exactly once.
        //
        //  ⛔ NEITHER LATCH IS A ROUTE. Nothing here loads a scene, and in particular
        //  NOTHING CALLS SceneRouter.GoRaid FROM THE BOSS ROOM. That was the whole hazard:
        //  a direct raid load out of a dungeon bypasses DungeonController.ExitToVillage and
        //  silently bins the run's crafting scatter. The CTA asks to LEAVE (latch 1, honoured
        //  by the dungeon's own sanctioned exit confirm, which still routes through
        //  ExecuteLeave -> _onLeave and still banks) and asks that the grid be opened ONCE
        //  THE HUB IS UP (latch 2, honoured by BattlePlansService's existing hub-side tick).
        //  The player still taps "Continue to exit": the request cannot skip their confirm.
        //
        //  ⚠ WHY THEY LIVE IN DeNelle.Village AND NOT IN Core OR Dungeons.
        //  DeNelle.Dungeons references DeNelle.Village (its asmdef, read 2026-09-16); the
        //  reverse edge does not exist and adding it would be a cycle. So Village cannot call
        //  into the exit directly, and the exit CAN read a Village latch. Putting these in
        //  Core would widen the most-referenced assembly for one feature; putting the
        //  dungeon-exit request in Dungeons would make it unreachable from the reveal. Village
        //  is the one place both sides can see, and it already owns this feature.
        // =====================================================================

        private static string _pendingDungeonExit;
        private static string _pendingRaidGridCampId;

        /// <summary>
        /// LATCH 1 -- "the player has asked to leave the dungeon through the sanctioned exit."
        /// Written by the reveal's CTA in the boss room; consumed by
        /// <c>DeNelle.Dungeons.DungeonExitInteractable</c> on its existing proximity tick, which
        /// then raises its own Continue/Cancel confirm. <paramref name="why"/> is carried for the
        /// trace only.
        /// </summary>
        public static void RequestDungeonExit(string why)
        {
            _pendingDungeonExit = string.IsNullOrEmpty(why) ? "unspecified" : why;
            FlowTrace.Step(Sys, "dungeon-exit REQUESTED (" + _pendingDungeonExit +
                ") -- latched for the scene's own exit to honour; nothing is routed here, and the " +
                "player still taps Continue to exit");
        }

        /// <summary>True when a dungeon-exit request is waiting. Does NOT consume it.</summary>
        public static bool HasPendingDungeonExit => !string.IsNullOrEmpty(_pendingDungeonExit);

        /// <summary>Take the pending dungeon-exit request and CLEAR it, so it is honoured exactly
        /// once no matter how many exits a dungeon authors. Null when nothing is pending, which is
        /// the normal case on every tick.</summary>
        public static string ConsumeDungeonExitRequest()
        {
            var why = _pendingDungeonExit;
            _pendingDungeonExit = null;
            return why;
        }

        /// <summary>
        /// LATCH 2 -- "open the raid grid once the hub is up." Written by the reveal's CTA;
        /// consumed by <see cref="BattlePlansService"/>'s hub-side tick, which is already a 1 Hz
        /// scan that knows when the active scene is a hub, so no new bootstrap and no new
        /// MonoBehaviour is introduced to carry it.
        /// </summary>
        public static void RequestRaidGridOnHubArrival(string campId)
        {
            _pendingRaidGridCampId = string.IsNullOrEmpty(campId) ? BastionConfigId : campId;
            FlowTrace.Step(Sys, "raid-grid-on-arrival REQUESTED for '" + _pendingRaidGridCampId +
                "' -- latched until the active scene is a hub (never a GoRaid from a dungeon)");
        }

        /// <summary>True when a raid-grid request is waiting. Does NOT consume it.</summary>
        public static bool HasPendingRaidGrid => !string.IsNullOrEmpty(_pendingRaidGridCampId);

        /// <summary>Take the pending raid-grid request and CLEAR it (honoured exactly once).</summary>
        public static string ConsumeRaidGridRequest()
        {
            var id = _pendingRaidGridCampId;
            _pendingRaidGridCampId = null;
            return id;
        }

        /// <summary>Drop both latches. For the regression's own teardown, so a suite can never
        /// leave a live request behind for the editor session that follows it.</summary>
        public static void ClearPendingRequests()
        {
            _pendingDungeonExit = null;
            _pendingRaidGridCampId = null;
        }

        // =====================================================================
        //  THE BEATS
        // =====================================================================

        /// <summary>One beat of the reveal: the line, how long it holds, whether it is the
        /// emphasised opener, and whether the Echo speaks it.</summary>
        public readonly struct Beat
        {
            public readonly string Text;
            public readonly float HoldSeconds;
            public readonly bool Emphasis;
            public readonly bool Speaker;
            public Beat(string text, float hold, bool emphasis = false, bool speaker = false)
            { Text = text; HoldSeconds = hold; Emphasis = emphasis; Speaker = speaker; }
        }

        /// <summary>
        /// Compose the reveal's beats. PURE over its inputs on purpose: the regression hands
        /// it a camp name, a spoils line and an army count and asserts the exact words, with
        /// no scene, no camera and no save.
        ///
        /// <para>Three beats, in the owner's own order: the drop, what it exposes, and the
        /// one thing to do next. Beat 2 is where "where it is / what it pays / your army is
        /// ready" all land, because that is the single sentence-block the player reads before
        /// the CTA.</para>
        /// </summary>
        public static List<Beat> BuildBeats(BattlePlansKind kind, string campName,
                                            string spoilsLine, string armyLine)
        {
            var beats = new List<Beat>(3);

            beats.Add(kind == BattlePlansKind.Bastion
                ? new Beat("The warlord fell in the dark, and his plans fell with him.", 4.6f, true)
                : new Beat("The last of the wave fell carrying orders.", 4.4f, true));

            var body = new System.Text.StringBuilder();
            body.Append(kind == BattlePlansKind.Bastion ? "Bastion Plans" : "Enemy Battle Plans");
            body.Append(" - the camp is drawn, the walls are drawn, the watch is drawn. ");
            body.Append(string.IsNullOrEmpty(campName) ? "Their stronghold" : campName);
            body.Append(" is exposed.");
            if (!string.IsNullOrEmpty(spoilsLine)) { body.Append(" "); body.Append(spoilsLine); body.Append("."); }
            if (!string.IsNullOrEmpty(armyLine)) { body.Append(" "); body.Append(armyLine); }
            beats.Add(new Beat(body.ToString(), 6.4f));

            beats.Add(kind == BattlePlansKind.Bastion
                ? new Beat("\"We have never struck this one. Take the army and take it back.\"", 6.0f, false, true)
                : new Beat("\"Elarion has held long enough. It is time we went to THEM.\"", 6.0f, false, true));

            return beats;
        }
    }
}

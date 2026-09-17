// =============================================================================
// TutorialSignals — the Tutorial V2 completion-signal bus (WO-T1, spec §2.1b).
// -----------------------------------------------------------------------------
// A thin adapter that maps events the game ALREADY emits to stable string ids
// ("build.tower_placed", "wave.cleared", "dialogue.ended:<id>", ...). The
// TutorialFlow interpreter awaits these ids; gameplay-side adapters
// (DeNelle.Village.TutorialSignalAdapters) subscribe the real C#/Unity events
// and Raise() here. Core-side sources (DialogueService, PanelRouter) are wired
// by TutorialCoreSignalAdapter below.
//
// Modeled on the proven DialogueEventBus (Core/Events): pure static, latching,
// case-insensitive, main-thread only. Latching matters — a completion signal
// that fires one frame before the interpreter arms its await must still count,
// so the interpreter Clear()s the id when it STARTS waiting and then accepts
// either the latch or a fresh raise.
//
// Every raise writes FlowTrace.Step("Tutorial", ...) — ONE instrumentation seam
// for humans, headless bots, and telemetry (spec §2.1b).
// =============================================================================

using System;
using System.Collections.Generic;
using DeNelle.Core.Diagnostics;

namespace DeNelle.Core.Tutorial
{
    /// <summary>
    /// Process-wide signal bus for Tutorial V2 step triggers/completions.
    /// Gameplay adapters <see cref="Raise"/> stable ids; the interpreter awaits
    /// them via <see cref="Raised"/> + the <see cref="HasFired"/> latch.
    /// </summary>
    public static class TutorialSignals
    {
        // ── Canonical signal ids (spec §2.1b) — keep in sync with tutorial-steps.json ──
        public const string BuildModeEntered = "build.mode_entered";
        public const string TowerPlaced      = "build.tower_placed";
        /// <summary>WO-702 per-item placement completion: "build.structure_placed:" +
        /// the placed CatalogEntry id (e.g. "build.structure_placed:pet-house") —
        /// raised ALONGSIDE the generic <see cref="TowerPlaced"/> by
        /// DeNelle.Village.TutorialSignalAdapters.OnStructurePlaced so a step can
        /// gate on a SPECIFIC structure (the founding-arc guided placements).</summary>
        public const string StructurePlacedPrefix = "build.structure_placed:";   // + CatalogEntry id
        public const string WaveCleared      = "wave.cleared";
        /// <summary>WO-1012 P3 (the arc, beat 7 ENEMIES AT THE GATE): the scripted
        /// TutorialWaveSpawner band (3-4 enemies) was repelled. DISTINCT from
        /// <see cref="WaveCleared"/> on purpose — the payoff beat must only ever be
        /// completed by ITS band, never by an ambient wave-loop clear (the loop is
        /// held closed by WaveLoopSuppressedForTutorial anyway; this makes the
        /// contract explicit in the signal vocabulary). Raised by
        /// TutorialFlow.TickScriptedWave when the step awaits this id.</summary>
        public const string TutorialBandRepelled = "wave.tutorial_band_repelled";
        public const string ArenaWin         = "arena.resolved:win";
        public const string ArenaLoss        = "arena.resolved:loss";
        public const string DialogueEndedPrefix = "dialogue.ended:";   // + dialogue id
        public const string HeroReachedPrefix   = "hero.reached:";     // + anchor id
        /// <summary>WO-1012 P3 (the arc, beat 2 WALK): follow-proximity — the hero,
        /// led by the pet-Echo guide (PetHeroLeash lead mode), reached the gate-side
        /// anchor ("guide_gate", resolved by Village's TutorialWorldAnchors to a spot
        /// pulled INSIDE the walls, never the spawn ring). Rides the existing
        /// hero.reached:* family — raised by TutorialFlow.TickProximityProbe.</summary>
        public const string GuideGateReached    = HeroReachedPrefix + "guide_gate";
        public const string PanelOpenedPrefix   = "panel.opened:";     // + PanelId
        /// <summary>WO-854 Silo E per-species bond completion: "pet.bonded:" + the
        /// pets.json species id (e.g. "pet.bonded:ice-wolf") -- raised by
        /// DeNelle.Pets.PetAcquisitionService.Acquire once a NEW species enters the
        /// roster, so a quest stage can gate on bonding a SPECIFIC companion. Lives
        /// here (not beside the completion DTO) because this is the emitter's own
        /// vocabulary: DeNelle.Core.Quests.QuestCompletion.PetBondedPrefix aliases
        /// this constant so the raiser and the matcher share one literal.</summary>
        public const string PetBondedPrefix     = "pet.bonded:";       // + pets.json species id
        // Contextual triggers (spec CREATIVE SCOPE) — sources noted per adapter.
        public const string CanAffordUpgrade = "economy.can_afford_upgrade";
        public const string EchoBornSecond   = "echo.born:2";
        public const string FirstGearAdded   = "inventory.gear_added:first";
        public const string FirstSkillPoint  = "skillpoint.earned:first";
        /// <summary>WO-1340 (the SPEND teach): a hero talent node was actually LEARNED -
        /// Wisdom debited and the node added to the unlocked set. Raised by
        /// <c>DeNelle.Village.Talents.WisdomCurrencyService.Unlock</c>, which is the SINGLE
        /// choke point every learn path funnels through (the legacy immediate
        /// <c>HeroSkillTreeVM.Unlock</c> AND the node-graph plan/CONFIRM flow's
        /// <c>Commit</c> both call it), so this signal cannot be raised from a path that
        /// did not move the player's tree.
        ///
        /// ⚠ THIS IS THE COMPANION TO, NOT A DUPLICATE OF, <see cref="FirstSkillPoint"/>.
        /// That one fires when a point is EARNED (hero level-up); this one fires when one
        /// is SPENT. The FTUE beat that teaches spending needs BOTH: earned is its trigger,
        /// spent is its completion. Raised on EVERY learn; the contextual one-shot's
        /// tutorial_ctx persistence dedupes to the first (same contract as FirstSkillPoint).
        ///
        /// ⚠ The talent tree's currency is WISDOM (WisdomCurrencyService), NOT
        /// SkillSystem.AvailablePoints - those are the separate CRAFT skills
        /// (Blacksmith/Woodworking/Arcane) that the panel merely displays alongside. A
        /// publisher hung off SkillSystem.SpendPoint would complete this beat without the
        /// player ever touching the talent tree.</summary>
        public const string FirstTalentLearned = "talent.learned:first";

        // -- WO-1389: the post-first-raid beat (WHY to train and upgrade, then HOW) --
        /// <summary>WO-1389 - the player is BACK IN TOWN after their FIRST raid (win or loss).
        /// Raised by DeNelle.Village.TutorialSignalAdapters from its 1 Hz discovery tick when the
        /// active scene is a hub, the save carries everCompletedRaid (the ONE writer is
        /// RaidDeployController.ReconcileRaidEnd) and no dialogue is running. Re-raised every
        /// 30 s while the post-raid beat is still unseen, so a hint that was live at the first
        /// raise cannot swallow the beat for the whole session; the beat's tutorial_ctx one-shot
        /// persistence dedupes.</summary>
        public const string FirstRaidCompleted = "raid.first_completed";
        /// <summary>WO-1389 - a TRAIN or UPGRADE job actually landed on a line (the real tap the
        /// HOW half of the post-raid beat waits for). Raised by BarracksService.UpgradeTroop and
        /// BarracksService.EnqueueTraining at their success points - the single choke points every
        /// train/upgrade path funnels through - alongside the per-troop
        /// <see cref="TroopJobQueuedPrefix"/> id.</summary>
        public const string TroopJobQueued = "troop.job_queued";
        /// <summary>"troop.job_queued:" + troop id - the per-troop twin of <see cref="TroopJobQueued"/>.</summary>
        public const string TroopJobQueuedPrefix = "troop.job_queued:";
        /// <summary>WO-1389 - the Train/Research line now HAS work (raised right AFTER
        /// <see cref="TroopJobQueued"/> by the same emitters). A separate id on purpose: contextual
        /// beats cannot chain off one another's completion (TutorialFlow.OnSignal returns after
        /// completing a live beat), so the TRAINING NOW coach-mark is its own beat triggered by
        /// this second raise, which arrives once the first beat has already closed.</summary>
        public const string TroopLineBusy = "troop.line_busy";
        /// <summary>WO-1389 - the Manage &gt; TROOPS workspace is on screen (ManageScreenPanel
        /// .ShowOperational for ManageTab.Troops - the card tap AND the dialogue door both funnel
        /// through it). The post-raid beat's FIRST route hop: it lights the Footman rail row. Used
        /// instead of panel.opened:Manage because that raise's ORDER relative to the workspace
        /// build is PanelRouter's business, while this one is raised after the rows exist and
        /// BEFORE any preselect raise - so a door that preselects a troop always walks
        /// row -&gt; UPGRADE face in that order.</summary>
        public const string ManageTroopsShown = "manage.troops_shown";
        /// <summary>"manage.troop_selected:" + troop id - a rail row on the Manage &gt; Troops screen
        /// was TAPPED (ManageScreenPanel.BuildTroopRailRow), or a door PRESELECTED it
        /// (ManageScreenPanel.Open(requestedTab) with "Troops:&lt;id&gt;" - the selection landed and
        /// the card is built, which is the same state a tap produces). A route hop, never a completion.</summary>
        public const string ManageTroopSelectedPrefix = "manage.troop_selected:";
        /// <summary>WO-1389 - the OPEN QUEUE face on the Manage screen was tapped and the drawer
        /// OPENED (ManageScreenPanel.ToggleQueueDrawer). Completion of the TRAINING NOW beat.</summary>
        public const string ManageQueueOpened = "manage.queue_opened";

        // -- WO-1415: the Heartfire introduction beat --
        /// <summary>WO-1415 - the RAIDS GRID is on screen (RaidSelectionScreen.OpenInternal, raised
        /// after the capability/army gates have passed and the panel has taken the modal arbiter).
        /// The moment Heartfire first means something: a new player looking at the camp list is
        /// about to spend a charge, while at founding they have nothing to spend one on.
        ///
        /// <para>(!) IT IS NOT panel.opened:&lt;PanelId&gt;. The raid grid registers with
        /// PanelManager ("Raids"), NOT PanelRouter, so no member of that family is ever raised for
        /// it - a beat authored against panel.opened:Raids would never fire once.</para>
        ///
        /// <para>RAISED ON EVERY OPEN, deliberately: the beat's tutorial_ctx one-shot latch dedupes,
        /// and TryTriggerContextual refuses while another hint is live (TutorialFlow.cs:2492), so a
        /// single raise could be swallowed by a hint that happened to be up. Re-raising per open
        /// makes the next visit the retry - no timer, no second mechanism.</para></summary>
        public const string RaidsGridOpened = "raids.grid_opened";

        // -- WO-1802: THE RAID DOOR, MADE OBVIOUS AFTER FOUNDING --------------------
        /// <summary>
        /// WO-1802 - the raid door is OPEN to a player who has never walked through it: a
        /// Barracks stands, the starter squad is in the roster, a Heartfire charge is lit, and
        /// RaidFunnel step 3 has never fired. Raised by
        /// DeNelle.Village.TutorialSignalAdapters.TickRaidDoorReady from its existing 1 Hz
        /// discovery tick, re-raised every 30 s while the beat is unseen.
        ///
        /// <para>⛔ IT IS A POLL AND NOT A HOOK ON THE GRANT, for two reasons. The state it
        /// watches is reachable by every road StarterArmyGrant's own header enumerates (a timed
        /// Builder job, the offline-fair sweep, the placement migration, a baked twin
        /// resurfacing), so a hook on one of them hands the other players nothing - the exact
        /// argument that made the grant itself a poll. And the whole predicate lives in ONE
        /// place, DeNelle.Core.HudModel.RaidDoorReadiness, which the two badge surfaces read
        /// too, so the beat and the badges cannot disagree about whether the door is open.</para>
        ///
        /// <para>NOT a raise at the grant's own edge for a third, smaller reason: the grant
        /// fires funnel steps 1 and 2 in the same second (observed in the live data), and at
        /// that instant the Heartfire and army rails have not necessarily published, so a beat
        /// armed there would be armed on defaults. See RaidDoorReadiness' fail-closed note.</para></summary>
        public const string RaidDoorReady = "raid.door_ready";

        /// <summary>
        /// WO-1802 - a raid was LAUNCHED. Raised by <c>DeNelle.Core.SceneRouter.GoRaid</c>
        /// immediately beside <c>RaidFunnel.RaidAttempted</c> - the SAME call site, on purpose:
        /// the raid-door beat's completion is then the literal event the owner's evidence
        /// counts (<c>raid_funnel_first_raid_attempted</c>), so the beat cannot be marked taught
        /// by anything that would not also move the metric.
        ///
        /// <para>⚠ THIS IS A COMPLETION, NOT A TAP. The WO-1340 rule: a beat that completes on
        /// its own dialogue closing proves only that the player closed a box. Opening the camp
        /// grid is not an attempt either - GoRaid is where the player has committed to the raid
        /// scene, which is why the funnel already measures there.</para>
        ///
        /// <para>Raised on EVERY raid launch; the beat's tutorial_ctx one-shot latch dedupes.</para></summary>
        public const string RaidAttempted = "raid.attempted";

        // -- WO-1804: THE PLANS DROPS THAT INTRODUCE RAIDING -------------------------
        /// <summary>
        /// WO-1804 - a plans drop was PICKED UP and its persisted HELD flag committed. Raised
        /// by <c>DeNelle.Village.BattlePlansPickup.TryCollect</c>, immediately after
        /// <c>ProgressionUnlocks.Unlock</c> succeeds and BEFORE any presentation runs - so the
        /// signal can never fire for a pickup whose flag did not persist, and can never be
        /// missed because the reveal screen failed to build.
        ///
        /// <para>⚠ THIS IS THE NAMED MEET POINT BETWEEN TWO LANES. WO-1804 owns the drop, the
        /// pickup and the reveal; WO-1802 owns the raid-door HELPER CHAIN (TutorialFlow /
        /// PlayerDeckWorkspace). Neither lane edits the other's files - they meet on this id.
        /// A step authored against <c>plans.revealed:battle</c> is the chain's cue that the
        /// player has just been TOLD about raiding, so the chain can stand down or advance
        /// instead of talking over the moment.</para>
        ///
        /// <para>One PREFIX, two ids, following this bus' own grammar
        /// (<see cref="StructurePlacedPrefix"/>, <see cref="PetBondedPrefix"/>,
        /// <see cref="ManageTroopSelectedPrefix"/>) rather than a flat one-off id, so a step can
        /// await one kind or match the family.</para>
        /// </summary>
        public const string PlansRevealedPrefix = "plans.revealed:";
        /// <summary>WO-1804 - the wave-2 "Enemy Battle Plans" were picked up (the raid
        /// introduction). Kind <c>BattlePlansKind.EnemyCamp</c>.</summary>
        public const string BattlePlansRevealed = PlansRevealedPrefix + "battle";
        /// <summary>WO-1804 - the "Bastion Plans" were picked up off the dungeon boss. Kind
        /// <c>BattlePlansKind.Bastion</c>; this is also the moment the Iron Bastion's lock
        /// lifts, because that same persisted flag IS the gate.</summary>
        public const string BastionPlansRevealed = PlansRevealedPrefix + "bastion";

        /// <summary>
        /// WO-1802 - HELPER 1 of the chain: this player has no Barracks, so a raid is not merely
        /// unfound, it is impossible. Owner direction 2026-09-16, verbatim: <i>"We should have
        /// some kind of helper that says try building a barracks or click here to put your
        /// barracks - something that we should assist them."</i>
        ///
        /// <para>Raised by the SAME tick as <see cref="RaidDoorReady"/>, from the SAME diagnosis
        /// (DeNelle.Core.HudModel.RaidDoorReadiness.Diagnose). ONE poll picks exactly one of
        /// these three ids, so the chain can never show two helpers at once or disagree with the
        /// badge about which blocker is current.</para></summary>
        public const string RaidHelperBarracks = "raid.helper:barracks";

        /// <summary>
        /// WO-1802 - HELPER 2: a Barracks stands but the army is under THE DOOR'S OWN BAR
        /// (RaidEntryGate.ArmyStatus.Ready, i.e. ArmyReadiness.RequiredSlots - 3 under the WO-823
        /// first-raid soft gate, the cap afterwards). Raised instead of
        /// <see cref="RaidDoorReady"/> so the player is sent to train rather than to a door that
        /// would toast and redirect them (RaidSelectionScreen.Open:342-402).
        ///
        /// <para>⚠ NOT a "first troops" state in the ordinary case: the starter-army grant fires
        /// on the Barracks edge, so a normal save passes this rung invisibly. It is reached by a
        /// small starterArmySize knob, a wounded roster, or losses - which is why the copy must
        /// never say "first".</para></summary>
        public const string RaidHelperArmy = "raid.helper:army";

        /// <summary>
        /// WO-1802 / WO-1804 - THE CROSS-LANE ENTRY POINT. The "Enemy Battle Plans" drop
        /// (WO-1804, Castle Defense Plans seam, after wave 2) raises this when its reveal CTA
        /// hands the player into the raid helper chain.
        ///
        /// <para>⛔ IT DOES NOT TRIGGER A BEAT, AND THAT IS DELIBERATE. No authored step is keyed
        /// on this id. TutorialSignalAdapters subscribes it and RE-ARMS its own re-raise timer, so
        /// the chain's CURRENT step - whichever of the three the live diagnosis names - raises on
        /// the very next 1 Hz tick. A beat keyed straight off this signal would have to re-derive
        /// which blocker is current, and that second derivation is exactly the drift CLAUDE.md
        /// sections 2/5/16 keep paying for.</para>
        ///
        /// <para>"Whichever comes first, once" therefore falls out for free: this entry point and
        /// founding completion converge on the same poll, and the per-save tutorial_ctx latch
        /// dedupes. WO-1804 needs only to Raise() one of these - it edits none of this lane's
        /// files.</para>
        ///
        /// <para>(!) THE IDS ARE DECLARED ABOVE, BY THE PRODUCING LANE, AND NOT AGAIN HERE.
        /// <see cref="PlansRevealedPrefix"/> / <see cref="BattlePlansRevealed"/> /
        /// <see cref="BastionPlansRevealed"/> are WO-1804's own block. This lane briefly declared a
        /// FLAT "battle_plans_revealed" - the name the coordinator relayed as PLANNED - and that was
        /// wrong twice over: the producer had settled on a PREFIX FAMILY that follows this bus'
        /// convention (<see cref="StructurePlacedPrefix"/>, <see cref="PetBondedPrefix"/>,
        /// <see cref="ManageTroopSelectedPrefix"/>), and it also references
        /// <see cref="BastionPlansRevealed"/>, which the flat version did not provide - so
        /// BattlePlansPickup could not have compiled. Two lanes both declaring the meet-point is
        /// itself the duplicated-state failure this file keeps warning about, so the consumer
        /// DEFERS to the producer's declaration and matches the PREFIX, which also means a third
        /// plans kind needs no edit on this side.</para></summary>
        public const string OwnedTownRevealed = "ownedTown.revealed";
        public const string OwnedTownRepaired = "ownedTown.repaired";
        public const string OwnedTownDesigned = "ownedTown.designed";
        public const string OwnedTownReentered = "ownedTown.reentered";
        public const string OwnedTownPracticed = "ownedTown.practiced";

        private static readonly HashSet<string> _fired =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Raised whenever a signal fires, with the signal id.</summary>
        public static event Action<string> Raised;

        /// <summary>Raise a named tutorial signal. No-op on null/empty. Never throws.</summary>
        public static void Raise(string signalId)
        {
            if (string.IsNullOrEmpty(signalId)) return;
            _fired.Add(signalId);
            FlowTrace.Step("Tutorial", $"signal '{signalId}' raised.");
            try { Raised?.Invoke(signalId); }
            catch (Exception ex)
            {
                // No silent failures (§12) — a throwing subscriber self-reports but
                // never breaks the raiser (gameplay must not fault on tutorial wiring).
                FlowTrace.Fail("Tutorial", $"signal '{signalId}' subscriber threw: {ex.GetType().Name}: {ex.Message}");
            }
        }

        /// <summary>True if <paramref name="signalId"/> has fired since the last Clear.</summary>
        public static bool HasFired(string signalId) =>
            !string.IsNullOrEmpty(signalId) && _fired.Contains(signalId);

        /// <summary>Clear one signal's latch — the interpreter calls this when it begins waiting.</summary>
        public static void Clear(string signalId)
        {
            if (!string.IsNullOrEmpty(signalId)) _fired.Remove(signalId);
        }

        /// <summary>Clear every latched signal (fresh tutorial run / New Game).</summary>
        public static void ClearAll() => _fired.Clear();
    }

    /// <summary>
    /// Wires the CORE-side signal sources (WO-T1): DialogueService end-of-dialogue
    /// (by id) and PanelRouter opens. Village-side sources (waves, towers, arena,
    /// economy) live in DeNelle.Village.TutorialSignalAdapters — Core never
    /// references gameplay. Registered once per process; the subscriptions are
    /// inert no-ops while ff.tutorialv2 content isn't running (raising into an
    /// un-awaited bus costs a hash-set add).
    /// </summary>
    internal static class TutorialCoreSignalAdapter
    {
        private static bool _wired;

        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Wire()
        {
            if (_wired) return;
            _wired = true;
            // dialogue.ended:<id> ← DialogueService.EndedWithId (DialogueService.cs).
            Dialogue.DialogueService.EndedWithId += id =>
                TutorialSignals.Raise(TutorialSignals.DialogueEndedPrefix + id);
            // panel.opened:<PanelId> ← PanelRouter.PanelOpened (PanelRouter.cs).
            UI.PanelRouter.PanelOpened += id =>
                TutorialSignals.Raise(TutorialSignals.PanelOpenedPrefix + id);
        }
    }
}

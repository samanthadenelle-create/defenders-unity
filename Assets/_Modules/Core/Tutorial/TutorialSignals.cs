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

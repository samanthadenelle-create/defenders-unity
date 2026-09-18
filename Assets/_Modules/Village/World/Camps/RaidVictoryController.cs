// =============================================================================
// RaidVictoryController — the MISSING subscriber that closes the core loop:
//   walk to a base -> CLEAR it -> CLAIM the base -> trigger the NEXT COMPANION
//   -> RETURN home (no soft-lock).
// -----------------------------------------------------------------------------
// Assembly: DeNelle.Village   Namespace: DeNelle.Village.World.Camps
//
// THE GAP THIS CLOSES (FeatureFlags.Raid was OFF because of exactly this):
//   "RaidGarrisonSpawner.OnCleared has no subscriber ... a cleared raid soft-locks."
// Everything UP TO clear already works (entry, garrison spawn, combat, RETREAT all
// reuse proven systems — see RaidGarrisonSpawner / RaidDeployController). What was
// missing was the VICTORY half: detect the clear, claim the base, hand the player
// the next companion, and route them home. This component is that half.
//
// SELF-INSTALL: mirrors RaidDeployController — a RuntimeInitialize hook adds ONE
// controller to any RaidBase_* scene (idempotent). It then finds the scene's
// RaidGarrisonSpawner and subscribes to OnCleared (or, if the garrison already
// cleared before we bound — e.g. an empty composition — handles it immediately).
//
// THE FOUR STEPS (each FlowTrace-instrumented, system "Raid"):
//   1. VICTORY  — OnCleared fires (last defender dead). Guard against double-fire.
//   2. CLAIM    — RaidClaimService.MarkClaimed(configId) persists the win, and
//                 SceneOwnership.SetEnemyOwned(false) flips the live scene PLAYER-
//                 owned (the inverse of the spawner's SetEnemyOwned(true)) so the
//                 base reads as YOURS for the rest of this session.
//   3. COMPANION— on a NEW claim only, unlock the next canon companion into the
//                 persisted party (GameStateService.AddToParty) — the rescue beat.
//   4. RETURN   — a code-built victory banner with a "Return to Castle" button
//                 (SceneRouter.GoCastle), plus an auto-return safety timer so the
//                 player is NEVER stranded on a cleared raid.
//
// SCOPE / STUBS (flagged): the full WO-431 star-scoring + reward-breakdown victory
// SCREEN and the WO-441 Phase-C special-node auto-harvest outpost are OUT of this
// spine — this builds the victory->claim->next-companion->return BACKBONE end-to-
// end (minimal but real), so RAID can flip ON without a soft-lock. See REPORT.
//
// Code-built uGUI (NO UXML — repo rule), via the shared ElarionUiKit so it matches
// the raid deploy HUD. ASCII-only runtime strings. Canon: Elarion (never Avalon).
// =============================================================================

using System.Collections;
using UnityEngine;
using DeNelle.Core;
using DeNelle.Core.State;
using DeNelle.Core.UI;
using DeNelle.Core.Diagnostics;
using DeNelle.Village.UI;

namespace DeNelle.Village.World.Camps
{
    /// <summary>
    /// Subscribes to <see cref="RaidGarrisonSpawner.OnCleared"/> and runs the raid
    /// victory flow: claim the base, unlock the next companion, and return the hero
    /// home (with a victory banner). Self-installs into any <c>RaidBase_*</c> scene.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class RaidVictoryController : MonoBehaviour
    {
        // WO-1543 (owner ruling 2026-09-06: "Hold on touch, longer guard") - 12f -> 30f.
        // TWELVE SECONDS DID NOT READ THIS SCREEN. The player has to take in the star result,
        // up to FIVE spoils rows, a companion-join line and, at a capped bank, "Some of the
        // reward could not be paid out" - the one message they must not miss. The guard STAYS
        // (an end state that never dismisses can strand a player, and that is what it is for);
        // it is now long enough to read, and EndStateVM.HoldOnInteraction re-arms it on every
        // touch, so a reading player is never yanked and an absent one still goes home.
        [Tooltip("Seconds after victory before the hero auto-returns to the castle if the " +
                 "player never taps the button (anti-soft-lock safety net). Re-armed by any " +
                 "interaction - see EndStateVM.HoldOnInteraction, WO-1543.")]
        [SerializeField] private float _autoReturnSeconds = 30f;

        private RaidGarrisonSpawner _spawner;
        private RaidSpire _spire;  // razing it still wins; garrison wipe also wins (owner 2026-09-09)
        private bool _handled;     // victory handled once (guards a double OnCleared)
        private bool _returning;   // a return is already in flight

        // WO-1768 — THE VICTORY SCREEN IS ACTUALLY ON SCREEN. Set immediately after a
        // SUCCESSFUL EndStateView.Show (inside ShowVictoryScreen's try), so a build that
        // threw on the way up never sets it and the catch's direct ReturnHome still owns
        // the exit.
        private bool _victoryScreenUp;

        /// <summary>
        /// WO-1768 — "the victory path owns the route home, so nothing else may route."
        ///
        /// <para>THE CAPTURED DEFECT (owner Seeker, build 2026.09.16.371701, logcat
        /// pull-20260916-143101, raid IronBastion): the spire fell 1.93 s AFTER the hero
        /// died, i.e. inside the 1.75 s down-beat. The victory screen opened at 13:26:05.398,
        /// the owner touched it at .594 (re-arming WO-1543's full 30 s hold), and
        /// HeroHealth's EVAC branch called SceneRouter.GoCastle() at .617 —
        /// "SCREEN CLOSED: EndState 'Victory!' by EndStateView.OnDestroy (torn down without
        /// firing)" at 06.180. The screen was readable for 0.78 s and the spoils were never
        /// read.</para>
        ///
        /// <para>⛔ WHY THIS AND NOT <c>_handled</c>: <c>_handled</c> latches on the FIRST
        /// statement of <see cref="HandleVictory"/>, before Finalize, before the army
        /// reconcile and before the screen. A throw in between leaves it true with NO screen
        /// up — and a death path that stood down on it would strand a dead hero on an
        /// enemy-owned field, which is the WO-1437 stranding the EVAC branch exists to
        /// prevent. <c>_returning</c> alone is equally wrong: it is set only in
        /// <see cref="ReturnHome"/>, i.e. AFTER the screen is dismissed — in the capture it
        /// was FALSE for the whole window that needed protecting. So the gate is the OR of
        /// "the screen is up" and "a return is already in flight".</para>
        ///
        /// <para>ONCE THIS IS TRUE A ROUTE HOME IS GUARANTEED FROM THREE INDEPENDENT PLACES:
        /// the screen's one primary action (Return to Castle -> <see cref="ReturnHome"/>),
        /// its <c>AutoDismissSeconds</c> anti-soft-lock guard, and
        /// <see cref="ShowVictoryScreen"/>'s own catch, which calls ReturnHome directly if
        /// the build threw. The caller still arms a watchdog on top of that.</para>
        ///
        /// <para>⛔ WO-1778 — THAT GUARANTEE WAS FALSE ON THE CAPTURE BRANCH, AND THE PARAGRAPH
        /// SAID SO WHILE IT WAS FALSE. All three routes funnel through <see cref="ReturnHome"/>,
        /// which returned early on <see cref="CanEnterCapturedTown"/>; that gate refused FOREVER
        /// when the precombat census was missing (it fired in the owner's own 2026-09-16 run:
        /// <c>[Flow:Raid] Precombat capture census failed: Captured structure lacks a baked
        /// stable identity: Wall_Outer_SS_3</c>). Its promised escape hatch,
        /// <c>RetryCaptureAfterDismissal</c>, had ZERO callers and <c>_waitingForCapture</c> was
        /// never set true — so three routes home were three copies of one refusal. Both are
        /// DELETED and the gate is now BOUNDED: see <see cref="CaptureRefusalsBeforeForcedExit"/>.
        /// A refusal that can only be retried through a button that refuses is not a route.</para>
        /// </summary>
        public bool VictoryOwnsTheReturn => _victoryScreenUp || _returning;
        private RaidCaptureCensus _captureCensus;
        private bool _captureRequired;
        private bool _captureCommitted;
        private int _victoryStars;

        /// <summary>WO-1783 — this win was on the CAPTURE raid, the town is not hers yet, and the
        /// clear fell short of <c>OwnedBaseProgression.CaptureStarsRequired</c>. Drives ONE sentence
        /// on the victory screen and nothing else; it never gates or routes anything.</summary>
        private bool _captureRaidShortOfStars;

        /// <summary>
        /// WO-1778 — how many times <see cref="CanEnterCapturedTown"/> may refuse before the
        /// victory screen STOPS asking and forces the castle route instead.
        ///
        /// <para>The first refusal is a real retry: the player gets the
        /// <c>ownedTown.captureRetry</c> toast and the screen stays up, because a transient
        /// save-service outage genuinely does clear on a second tap. The bounded one after it is
        /// the law this ticket exists for — the player always leaves the raid, and the capture is
        /// parked as a pending receipt (<see cref="RaidCaptureCensus.TryParkForLaterClaim"/>) so a
        /// later load can still claim the town (<c>GameStateService</c> recovers a pending capture
        /// on load).</para>
        /// </summary>
        internal const int CaptureRefusalsBeforeForcedExit = 2;
        private int _captureRefusals;
        private bool _captureForfeitedToCastle;

        // =====================================================================
        //  Self-install — one controller per RaidBase_* scene
        // =====================================================================

        /// <summary>
        /// On every scene load, if the active scene is a <c>RaidBase_*</c> the victory
        /// controller installs itself (idempotent). Mirrors RaidDeployController's hook,
        /// so the two command surfaces (deploy/retreat + victory) sit side by side.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void InstallHook()
        {
            UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnSceneLoaded;
            UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneLoaded;
            TryInstall(UnityEngine.SceneManagement.SceneManager.GetActiveScene().name);
        }

        private static void OnSceneLoaded(UnityEngine.SceneManagement.Scene scene,
                                          UnityEngine.SceneManagement.LoadSceneMode mode)
        {
            TryInstall(scene.name);
        }

        private static void TryInstall(string sceneName)
        {
            if (string.IsNullOrEmpty(sceneName)) return;
            if (!sceneName.StartsWith("RaidBase", System.StringComparison.OrdinalIgnoreCase)) return;
            if (FindAnyObjectByType<RaidVictoryController>() != null) return;

            var go = new GameObject("RaidVictoryController");
            var raidScene = UnityEngine.SceneManagement.SceneManager.GetSceneByName(sceneName);
            if (raidScene.IsValid()) UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(go, raidScene);
            go.AddComponent<RaidVictoryController>();
            FlowTrace.Step("Raid", $"RaidVictoryController self-installed in raid scene '{sceneName}'.");
        }

        // =====================================================================
        //  Bind to the garrison spawner (it spawns its garrison one frame after its
        //  own Start, so we poll a few frames for it rather than assuming Start order).
        // =====================================================================

        private void Start()
        {
            if (gameObject.scene.name == "RaidBase_IronBastion" && GameStateService.Instance?.State?.OwnedBase == null)
            {
                try { _captureCensus = new RaidCaptureCensus(gameObject.scene); }
                catch (System.Exception ex) { FlowTrace.Fail("Raid", "Precombat capture census failed: " + ex.Message); }
            }
            StartCoroutine(BindRoutine());
        }

        private void OnDestroy()
        {
            if (_spawner != null) _spawner.OnCleared -= HandleCleared;
            if (_spire != null) _spire.OnDestroyedEvent -= HandleSpireRazed;
        }

        private IEnumerator BindRoutine()
        {
            // ---- THE OBJECTIVE ------------------------------------------------
            // Spire raze still wins. Owner 2026-09-09 (felt, live raid): once the garrison
            // is dead the player must not be left chopping an empty camp - garrison wipe
            // ALSO wins. Either signal settles the raid; HandleVictory latches so the
            // second one is a no-op.
            _spire = RaidSpire.Active != null ? RaidSpire.Active : FindAnyObjectByType<RaidSpire>();
            if (_spire != null)
            {
                if (_spire.IsDestroyed)
                {
                    FlowTrace.Step("Raid", "RaidVictoryController: spire was ALREADY razed on bind — running victory immediately.");
                    HandleVictory("spire razed (already down at bind)");
                    yield break;
                }
                _spire.OnDestroyedEvent -= HandleSpireRazed;
                _spire.OnDestroyedEvent += HandleSpireRazed;
                FlowTrace.Step("Raid", $"RaidVictoryController bound to the OBJECTIVE: spire '{_spire.name}' " +
                                       $"({_spire.MaxHp:0} HP). Razing it OR wiping the garrison WINS the raid.");
            }
            else
            {
                FlowTrace.Warn("Raid", "RaidVictoryController: this raid scene has NO RaidSpire — falling back to " +
                                       "the legacy garrison-wipe win condition. Re-bake with " +
                                       "RaidBaseGenerator.BuildAllRaidScenes to get the spire objective.");
            }

            // The spawner lives on the RaidBase_<id> root and arms its garrison a frame
            // after Start; give it a handful of frames to appear, then bind.
            for (int i = 0; i < 10 && _spawner == null; i++)
            {
                _spawner = FindAnyObjectByType<RaidGarrisonSpawner>();
                if (_spawner != null) break;
                yield return null;
            }

            if (_spawner == null)
            {
                FlowTrace.Warn("Raid", "RaidVictoryController: no RaidGarrisonSpawner found in this raid scene — " +
                                       "victory cannot be detected (the loop would soft-lock). Leaving the deploy/retreat exit as the only out.");
                yield break;
            }

            // If the garrison already cleared before we bound (empty composition / no
            // navmesh path => MarkCleared in ActivateRoutine), handle it now; otherwise
            // subscribe for the live last-defender-dies event.
            if (_spawner.Cleared)
            {
                FlowTrace.Step("Raid", "RaidVictoryController: garrison was ALREADY cleared on bind — running victory immediately.");
                HandleCleared(_spawner);
            }
            else
            {
                _spawner.OnCleared -= HandleCleared;
                _spawner.OnCleared += HandleCleared;
                FlowTrace.Step("Raid", $"RaidVictoryController bound to OnCleared (garrison of {_spawner.TotalGarrison} defender(s)).");
            }
        }

        // =====================================================================
        //  STEP 1 — VICTORY. THE OBJECTIVE: the central spire falls.
        //  (Legacy fallback: the last defender died, in a scene with no spire.)
        // =====================================================================

        /// <summary>The spire was razed — this is the win, whatever the garrison is doing.</summary>
        private void HandleSpireRazed(RaidSpire spire)
        {
            if (spire != null) spire.OnDestroyedEvent -= HandleSpireRazed;
            HandleVictory("SPIRE RAZED");
        }

        /// <summary>
        /// The garrison was wiped. Owner 2026-09-09: that IS a win — do not leave the
        /// player standing on an empty camp hitting the spire. Spire raze remains a
        /// second, independent win (HandleSpireRazed); HandleVictory latches.
        /// </summary>
        private void HandleCleared(RaidGarrisonSpawner spawner)
        {
            if (_spawner != null) _spawner.OnCleared -= HandleCleared;
            HandleVictory("garrison wiped");
        }

        private void HandleVictory(string reason)
        {
            if (_handled) { FlowTrace.Step("Raid", "victory already handled — ignoring duplicate signal."); return; }
            _handled = true;

            // WO-1810 - DECLARE THE WIN NOW, NOT AT ReconcileArmy. The army settlement is priced on
            // the declared exit outcome, and an undeclared exit prices as a FAIL (which loses the
            // whole warband). HeroHealth settles a dead hero with ReconcileRaidEnd(0) and only stands
            // down once the victory SCREEN is up, so a hero dying between this latch and
            // ReconcileArmy below would otherwise wipe a WON warband. Declaring here closes that
            // window; the declaration is latched, so it cannot be overwritten afterwards.
            Guard.Try("Raid", "declare the victory exit outcome", () =>
            {
                var deployNow = FindAnyObjectByType<RaidDeployController>();
                if (deployNow != null)
                    deployNow.DeclareRaidExitOutcome(DeNelle.Village.RaidExitOutcome.Victory,
                                                     "base cleared: " + reason);
            });

            if (_spawner != null) _spawner.OnCleared -= HandleCleared;
            if (_spire != null) _spire.OnDestroyedEvent -= HandleSpireRazed;

            RaidGarrisonSpawner spawner = _spawner;
            string configId = ResolveConfigId(spawner);
            FlowTrace.Step("Raid", $"VICTORY — raid '{configId}' won ({reason}). Running claim -> next-companion -> return.");

            // Victory fanfare (reuse the audio service; null-safe cross-module call).
            // PlayMusic(Victory) is the clean cross-module call available on IAudioService
            // (PlaySfx takes a raw AudioClip the Village side can't see) — swaps the driving
            // Raid brass for the victory track.
            CoreServices.Audio?.PlayMusic(DeNelle.Core.Audio.MusicTrack.Victory);

            // STEP 1.5 — IS THIS A REPEAT CLEAR *INSIDE THE CAMP'S CURRENT CYCLE*?
            //
            // ⚠ CORRECTED 2026-09-09 (WO-1461, owner ruling 2026-09-06 20:33). This line read
            // RaidClaimService.IsClaimed, and the comment here used to describe the answer as
            // "have I EVER taken this camp" - which is exactly what IsClaimed means, and exactly
            // what the owner's ruling is NOT. Verbatim: "100% first clear after cooldown, 60%
            // repeat clear during the same cycle, then reset to 100% when the camp's cooldown
            // expires." Read alone, IsClaimed says "reduced FOREVER" - the behaviour the player
            // met. IsRepeatClearInCycle is the AND of the permanent claim and a still-running
            // cooldown window, so the share resets when the cooldown does. IsClaimed itself is
            // unchanged and still permanent on purpose (WO-1134: it also gates the one-time
            // companion unlock, and STEP 3's newClaim below still rides that flag).
            //
            // THE ORDER IS LOAD-BEARING ON *BOTH* SIDES NOW, not just one:
            //   * before ClaimBase (STEP 2), which flips the claim flag - query after it and
            //     every clear reads as a repeat;
            //   * before RaidCooldownService.BeginAfterClear (STEP 2.5), which STAMPS the
            //     cooldown this predicate reads - query after it and the window is always
            //     running, so again every clear reads as a repeat.
            // The answer feeds the first-clear loot gate at STEP 3.5.
            bool repeatClear = RaidClaimService.IsRepeatClearInCycle(configId);

            // STEP 1.6 (WO-1134) — HAVE THIS CAMP'S CRYSTALS ALREADY BEEN PAID TODAY (UTC)?
            // A SECOND, INDEPENDENT question from repeatClear, kept on its own flag on purpose:
            // repeatClear is "have I EVER taken this camp" (never expires, and also gates the
            // one-time companion unlock), while this is "have I taken it TODAY" (resets at UTC
            // midnight). They cross - the first clear of a NEW day is repeat:true, paid:false,
            // and pays reduced resources but FULL crystals. Read BEFORE the grant stamps it.
            bool crystalsPaidToday = RaidClaimService.CrystalsPaidToday(configId);

            // STEP 2 — claim the base (persist + flip ownership PLAYER-owned).
            bool newClaim = ClaimBase(configId);

            // STEP 2.5 (WO-728) — OPEN THE COOLDOWN. A clear is what starts the wait; this
            // runs on EVERY clear, first or repeat, because the entry gate is a different
            // question from the loot gate (RaidClaimService answers "have I ever taken this
            // camp"; the cooldown answers "may I raid it again yet"). Stamped from the
            // SERVER-ANCHORED clock inside the service — never DateTime.UtcNow here.
            // Placed before the presentation so a screen throw can never skip the wait, which
            // is the same reason STEP 3.6 settles the army before ShowVictoryScreen.
            RaidCooldownService.BeginAfterClear(configId);

            // STEP 3 — on a NEW claim, unlock the next companion (the rescue beat).
            // WO-1761 (owner felt-test 2026-09-15: "It doesn't make any sense why they join
            // the team if they don't offer any benefit"). Under SINGLE-HERO the recruit is
            // DROPPED, not just its banner line: BattleController, StoryCompanionInjector,
            // PartyHudBridge and HudModelProducers all already hide companions, so enrolling
            // one into the persisted roster only makes the save disagree with every screen.
            // The flag-OFF path below is untouched — ff.singlehero=0 restores the whole beat.
            string joined = null;
            if (newClaim && FeatureFlags.SingleHero)
                FlowTrace.Step("Raid", "NEXT COMPANION SKIPPED — SingleHero is ON, so a new claim " +
                    "recruits nobody and the victory banner carries no join line (WO-1761).");
            else if (newClaim)
                joined = UnlockNextCompanion();

            // STEP 3.5 (WO-771.6) — settle the V1 SCORE (0-3 stars from the real-time
            // clear/clock) and GRANT the loot. This is the win/stars/loot half that was
            // flagged OUT (this file :34). Null-safe: with no scorer the screen falls
            // back to the star-less banner and no loot is granted.
            RaidScoring scoring = RaidScoring.Instance;
            RaidResult result = scoring != null ? scoring.Finalize(true) : null;
            // Owner 2026-09-11: the personal town is a 3-star clear of the HIGHEST raid,
            // not a 20-win counter. WO-1526 still caps a hero-down settle at 2 stars, so
            // a death-win cannot capture. Stars are only known AFTER Finalize.
            string captureRaidId = ResolveConfigId(spawner);
            _victoryStars = result != null ? result.Stars : 0;
            _captureRequired = captureRaidId == OwnedBaseProgression.FinalRaidId &&
                GameStateService.Instance?.State?.OwnedBase == null &&
                _victoryStars >= OwnedBaseProgression.CaptureStarsRequired;
            if (_captureRequired)
            {
                FlowTrace.Step("Raid", "CAPTURE ELIGIBLE — " + _victoryStars + "-star clear of '" + captureRaidId + "'.");
                TryCommitCapturedTown(_victoryStars);
            }
            else if (captureRaidId == OwnedBaseProgression.FinalRaidId)
                FlowTrace.Step("Raid", "highest raid settled at " + _victoryStars + " star(s) — capture requires " +
                               OwnedBaseProgression.CaptureStarsRequired + ".");

            // WO-1783 — SHE MISSED THE CAPTURE AND WAS NEVER TOLD IT EXISTED.
            //
            // The branch above already KNEW this (it has traced the shortfall for a developer all
            // along) while the screen said nothing, so a player could 2-star the final raid, read
            // "Victory!", and never learn why the town did not become hers. This latch is the same
            // three facts the capture gate reads, and NOT just "the final raid": a player who
            // already OWNS the town is re-raiding it and must not be told to take it again.
            //
            // MESSAGING ONLY — the capture gate and the route out are WO-1778's, untouched here.
            _captureRaidShortOfStars = captureRaidId == OwnedBaseProgression.FinalRaidId &&
                GameStateService.Instance?.State?.OwnedBase == null &&
                _victoryStars < OwnedBaseProgression.CaptureStarsRequired;
            ResourceCost loot = scoring != null ? scoring.LootFor(result) : default(ResourceCost);
            loot = ApplyFirstClearGate(loot, repeatClear, crystalsPaidToday, configId);
            GrantLoot(loot);

            // STEP 3.5b (WO-1461) — RETAIN WHAT THE BANK REFUSED INSTEAD OF BURNING IT.
            // Owner ruling 2026-09-06 20:33, verbatim: "Never destroy raid loot because storage
            // is full. Put overflow into a temporary Raid Cache with a modest cap." The measured
            // shortfall is (what we asked for) minus (what the wallet actually moved), and
            // GrantLoot above has just written that measurement into _credited - so this must
            // stay immediately after it, and it must stay HERE rather than inside GrantLoot,
            // whose signature carries no configId while this scope's local does. Capped axes
            // only: crystals and gold have no ceiling (TownBankCapacity Law 1), so a shortfall
            // on those is not an overflow. Call exactly once per settle - it is not idempotent.
            ResourceCost retained = RaidClaimService.RetainOverflow(configId, loot, _credited);
            FlowTrace.Step("Raid",
                "RAID CACHE at settle: requested " + loot.Wood + "w " + loot.Iron + "i " + loot.Stone +
                "f, credited " + _credited.Wood + "w " + _credited.Iron + "i " + _credited.Stone +
                "f, RETAINED " + retained.Wood + "w " + retained.Iron + "i " + retained.Stone +
                "f for camp '" + (configId ?? "(none)") + "'. Anything the bank refused and the " +
                "cache could not hold is named by RaidClaimService's own line above this one.");

            // WO-1789 — AND NOW IT REACHES THE SCREEN. The two numbers the victory caption needs,
            // measured HERE because this is the only scope that holds all three baskets at once
            // (loot / _credited / retained). CAPPED AXES ONLY, matching RaidClaimService.RetainAxis
            // exactly: an uncapped axis is never cached, so counting it would invent a cache
            // shortfall that never existed.
            //
            // _overflowLost is RetainAxis's own `stillRefused`, recomputed rather than plumbed back
            // through RetainOverflow's signature: refused-by-bank minus retained-by-cache, summed.
            // It is the ONE quantity that makes "held in your Raid Cache, claim it later" a lie, so
            // the screen must have it before it picks a sentence.
            // ⛔ THE IsCapped GUARD IS RetainAxis's FIRST LINE, AND IT IS MIRRORED HERE ON PURPOSE.
            // All three of these are capped today (the owner's own log: "BANK FULL [Grant]
            // Wood/Stone/Iron"), so this changes nothing now - it PINS that the screen's arithmetic
            // cannot drift from the settle's. If an axis is ever uncapped, RetainAxis returns 0 for
            // it while an unguarded subtraction here would still count its shortfall, inflating
            // _overflowLost and firing "part of the haul could not be kept" over a haul that was
            // never at risk. That is the §11B lie the three-outcome split exists to prevent.
            int refusedByBank = RefusedOnCappedAxis(DeNelle.Core.Economy.BankResource.Wood,  loot.Wood,  _credited.Wood)
                              + RefusedOnCappedAxis(DeNelle.Core.Economy.BankResource.Iron,  loot.Iron,  _credited.Iron)
                              + RefusedOnCappedAxis(DeNelle.Core.Economy.BankResource.Stone, loot.Stone, _credited.Stone);
            _overflowCached = Mathf.Max(0, retained.Wood + retained.Iron + retained.Stone);
            _overflowLost   = Mathf.Max(0, refusedByBank - _overflowCached);
            FlowTrace.Step("Raid",
                "RAID CACHE -> SCREEN (WO-1789): the bank refused " + refusedByBank + " unit(s) across the " +
                "capped axes, the Raid Cache is HOLDING " + _overflowCached + " and " + _overflowLost +
                " was above both ceilings. Until this ticket the victory screen said only '" +
                EndStateVM.RewardShortSentence + "' and named neither the cache " +
                "nor whether any of it was recoverable.");

            // WO-1134 — stamp the crystal day AFTER the grant, and only when this payout
            // actually carried crystals. Stamping before the grant (or unconditionally) would
            // burn the player's one crystal clear of the day on a payout that paid none.
            if (loot.Crystals > 0) RaidClaimService.MarkCrystalsPaid(configId);

            // STEP 3.5c (WO-1373) — THE ROUGH STONE. Owner ruling 2026-09-09, verbatim: "there
            // is only one stone type till it gets to jeweler, and then its RND. So only top two
            // tiers of raids can drop stone and no more than 1 per day."
            //
            // ⛔ WHY IT IS A SEPARATE STEP AND NOT PART OF GrantLoot. The stone is an ITEM in
            // the larder, not an axis of ResourceCost, so it cannot ride the loot basket; and
            // it must land AFTER STEP 3.5b, because RetainOverflow measures `loot` against
            // `_credited` and neither of those baskets knows anything about items. Putting the
            // stone inside GrantLoot would put an item grant inside the resource wallet's
            // before/after measurement, which is how a credited-delta measurement starts lying.
            //
            // The whole step sits in ONE Guard: a throw here must never skip the victory screen
            // or the army settle below it. RaidScoring owns the RULE (pure, tier + day cap) and
            // the day LEDGER; this is the single grant site, beside the single resource grant.
            GrantRoughStoneIfEarned(configId, result != null ? result.Stars : 0);

            // STEP 3.6 - SETTLE THE ARMY. A WON raid must cost troops and pay veterancy
            // exactly as the retreat exit does. Before this, ReconcileAfterRaid had a single
            // caller (RaidDeployController.DoRetreat), so only LOSING an assault ever cost a
            // troop and AddVeterancy had ZERO callers repo-wide - winning was free. The deploy
            // HUD owns the deployed ledger (a fallen body is destroyed seconds after death, so
            // nothing here could reconstruct it), so the win routes through ITS one latched
            // reconcile. Runs BEFORE the screen so a presentation throw - which ShowVictoryScreen
            // catches - can never skip the settlement.
            ReconcileArmy(result);

            // STEP 3.7 (WO-1375) - COUNT THE WIN. The escalation ladder
            // (PROGRAM_RAID_ECONOMY_2026-09-04 section 4: target 2 after 3 victories, target 3
            // after 10, the Iron Bastion after 20) had NO input in the tree - nothing counted
            // raid wins. RaidClaimService's per-camp flags cannot answer it (clearing one camp
            // twice adds nothing to a SET) and EverCompletedRaid is a bool a RETREAT also sets.
            // Incremented here, once, AFTER the _handled latch above, because this is the one
            // de-duplicated settle seam - a second writer is the ladder skipping a tier.
            int victories = RecordVictory();

            // STEP 3.8 (WO-1374) - REPORT THE DAILY QUEST. The only ticker for combat.raid.*
            // was EnemyOutpost.cs:703 (the OuterWorld outpost), so clearing a baked raid camp
            // did not advance "Break a camp - clear 1 enemy outpost" - the daily whose own label
            // describes exactly what the player just did. Same event id and same shape as that
            // call site; DailyQuestService.Report prefix-matches, so this ONE report advances
            // both combat.raid.single and combat.raid.double, and there is exactly one of it.
            Guard.Try("Raid", "report combat.raid daily",
                () => DeNelle.Core.Quests.DailyQuestService.Instance?.Report(QuestRaidEventId, 1));

            // STEP 3.9 (WO-1375 / section 6) - PUBLISH TO THE SEASON PASS. Outcome-typed, never
            // an XP amount: the +50/+25/+25/+100 table resolves inside BattlePassService, behind
            // the one door owner ruling Q4 closed. ArenaOutcomeRelay's raid overload is
            // arity-separated from the arena one (4+ args vs at most 3), so this cannot bind to
            // the wrong publish. firstClear is the repeatClear read taken BEFORE ClaimBase -
            // re-deriving it now would report every clear as a repeat, because MarkClaimed has
            // already flipped the flag. Publish is Guard.Try'd inside the relay and an absent
            // handler is traced there, so a build with no battle pass loses nothing; this Guard
            // covers the argument marshalling on this side.
            Guard.Try("Raid", "publish raid outcome to the season pass", () =>
                DeNelle.Commerce.ArenaOutcomeRelay.Publish(
                    true,
                    result != null ? result.Stars : 0,
                    result != null ? result.DestructionPct : 0f,
                    !repeatClear,
                    configId));

            // WO-1374 — FUNNEL STEP 4 ("first raid won") and the ARM for step 5 ("raid
            // reward spent"). Placed AFTER the grant on purpose: a win that credited
            // nothing has not produced a reward for the player to spend, and arming step 5
            // before the money lands would let an unrelated spend complete the funnel.
            // Guarded so an analytics throw can never cost the player their victory screen.
            Guard.Try("Funnel", "raid won",
                () => DeNelle.Core.Analytics.RaidFunnel.RaidWon(configId, result != null ? result.Stars : 0));

            // STEP 4 — show the victory screen + route home (anti-soft-lock). The shared
            // Obsidian EndState template owns the presentation, the ONE primary action
            // (Return to Castle -> ReturnHome), the EventSystem, and the auto-dismiss
            // softlock guard (fed the same _autoReturnSeconds so the timing is unchanged).
            ShowVictoryScreen(configId, joined, result, loot, victories);
        }

        /// <summary>DailyQuests Report() id - the SAME literal EnemyOutpost.cs:112 uses, because
        /// the two raid surfaces must tick ONE channel. Report() prefix-matches, so this single
        /// id advances both <c>combat.raid.single</c> and <c>combat.raid.double</c>.</summary>
        private const string QuestRaidEventId = "combat.raid";

        // =====================================================================
        //  THE VICTORY COUNTER (WO-1375) - the ladder's missing input
        // =====================================================================

        /// <summary>
        /// Increments and persists <c>GameState.RaidVictories</c>, running the ONE-SHOT
        /// claim-flag backfill first so a veteran never restarts at 0. Returns the new count,
        /// or 0 when there is no state to write (reported, never silent). Called from
        /// <c>HandleVictory</c> only, after the <c>_handled</c> latch.
        /// </summary>
        private int RecordVictory()
        {
            var svc = GameStateService.Instance;
            var state = svc != null ? svc.State : null;
            if (state == null)
            {
                FlowTrace.Fail("Raid", "VICTORY COUNT LOST - no loaded GameState, so this win was not " +
                                       "counted toward the section-4 unlock ladder. The raid is unaffected.");
                return 0;
            }

            BackfillVictoriesFromClaims(state);
            state.RaidVictories++;
            svc.Save();
            FlowTrace.Step("Raid", $"VICTORY COUNT - raids won on this save: {state.RaidVictories} " +
                                   "(monotonic; the input to the section-4 escalation ladder). Persisted.");
            return state.RaidVictories;
        }

        /// <summary>
        /// ONE-SHOT: seed <c>RaidVictories</c> for a save that predates the counter, from the
        /// evidence a veteran's wins actually left behind - the per-camp
        /// <see cref="RaidClaimService"/> claim flags.
        ///
        /// <para>WHY IT IS HERE AND NOT IN <c>SaveMigrator</c>: the claim set lives in
        /// PlayerPrefs, not on the save wire, so a migrator step would have nothing to read.
        /// This runs where <c>RaidClaimService</c> is visible and latches on the persisted
        /// <c>RaidVictoriesBackfilled</c> flag, so it runs exactly once per save and can never
        /// inflate the count.</para>
        ///
        /// <para>IT IS A FLOOR, NOT A RECONSTRUCTION, AND THAT IS STATED RATHER THAN HIDDEN.
        /// One claimed camp proves at least one win, so all three claimed seeds 3. Repeat clears
        /// were never recorded anywhere and cannot be recovered - a veteran who farmed a single
        /// camp fifty times seeds 1. The under-count is fail-open (it delays a tier unlock, never
        /// revokes one) and self-heals from the next win onward.</para>
        ///
        /// <para>THE CAMP IDS ARE READ, NOT TYPED - they come from the scene-config catalog, so
        /// a fourth camp (the Iron Bastion, whose scene is baked and not yet switched on) is
        /// counted the day it is registered, with no edit to this file.</para>
        ///
        /// <para>Internal rather than private so the sibling ladder lane can force the seed
        /// before its FIRST read of the count, without a duplicate backfill of its own.</para>
        /// </summary>
        internal static void BackfillVictoriesFromClaims(DeNelle.Core.State.GameState state)
        {
            if (state == null || state.RaidVictoriesBackfilled) return;
            state.RaidVictoriesBackfilled = true;

            int seeded = 0;
            Guard.Try("Raid", "backfill raid victories from claim flags", () =>
            {
                foreach (string id in KnownRaidConfigIds())
                    if (RaidClaimService.IsClaimed(id)) seeded++;
            });

            if (seeded > state.RaidVictories) state.RaidVictories = seeded;

            FlowTrace.Step("Raid", $"VICTORY COUNT BACKFILL (one-shot) - {seeded} claimed camp(s) found in " +
                                   $"the persisted claim set; RaidVictories seeded to {state.RaidVictories}. " +
                                   "This is a FLOOR: repeat clears were never recorded and cannot be recovered, " +
                                   "so a heavy farmer may seed low. Fail-open (a tier unlocks later, never " +
                                   "sooner) and self-healing from the next win. The latch is now set.");
        }

        /// <summary>
        /// Every raid config id this build knows about, read from the scene-config catalog -
        /// never a hand-typed list, because a copied list is the duplicated state this repo's
        /// most expensive bugs are made of. An empty result is WARNED, never silently treated as
        /// "no claims".
        /// </summary>
        private static System.Collections.Generic.List<string> KnownRaidConfigIds()
        {
            var ids = new System.Collections.Generic.List<string>();
            Guard.Try("Raid", "enumerate raid config ids", () =>
            {
                foreach (var cfg in SceneConfigCatalog.All)
                {
                    if (cfg == null || string.IsNullOrEmpty(cfg.id) || string.IsNullOrEmpty(cfg.sceneName)) continue;
                    if (!cfg.sceneName.StartsWith("RaidBase", System.StringComparison.OrdinalIgnoreCase)) continue;
                    ids.Add(cfg.id);
                }
            });

            if (ids.Count == 0)
                FlowTrace.Warn("Raid", "victory-count backfill: the scene-config catalog yielded NO RaidBase_* " +
                                       "configs, so the claim scan has nothing to read and this save seeds 0. " +
                                       "Fail-open (the ladder simply starts counting from this win), never a lockout.");
            return ids;
        }

        // =====================================================================
        //  LOOT GRANT (WO-771.6) — reuse the village economy, never invent one.
        // =====================================================================

        /// <summary>
        /// Grants the raid loot into the player's economy. Prefers the canonical village
        /// <see cref="EconomyService"/> reward grant (the same path wave rewards use);
        /// falls back to the persistent <see cref="GameStateService"/> crystal/food
        /// mutators when a raid scene has no EconomyService (both target the SAME
        /// GameState.Resources wallet, so the grant lands and persists either way).
        ///
        /// <para>WO-978 — THIS TRACE REPORTS THE MEASURED CREDIT, NOT THE REQUEST. It used to
        /// print <c>loot.Crystals</c>/<c>loot.Food</c> — the numbers we ASKED for — as though they
        /// had landed. <c>EconomyService.Grant</c> returns <c>void</c> and routes to the
        /// <b>clampable</b> <c>BankGrantKind.EarnedIncome</c> kind (EconomyService.cs :363 → :396),
        /// so a town bank at its storage ceiling credits LESS than the raid awarded — possibly
        /// zero — while the old line still read "+500 crystals". That is exactly the shape of
        /// "I did the raid and got nothing" being unfalsifiable from a capture.
        /// <b>EconomyService itself is honest</b> (its own trace at :416 prints the post-clamp
        /// amount and the resulting total) — the bug was entirely caller-side, and it is fixed
        /// here, not there. Since the API hands back nothing, we take the only honest reading
        /// available: the wallet totals BEFORE and AFTER, and we log the DELTA — a measured
        /// quantity rather than a derived one.</para>
        /// </summary>
        // WO-978 follow-up: the MEASURED credit, kept so the VICTORY SCREEN can show what the
        // player actually received. Fixing only the log was half the ticket — at a capped town
        // bank the trace read "credited 0/500" while the screen still advertised "+500 crystals",
        // which is the same "I raided and got nothing" unfalsifiability one layer up. The sibling
        // ChallengeOutpostVictoryController already does this; the raid path did not.
        // WO-1374 - THE WHOLE CREDITED BASKET, not two of its five axes. This used to be
        // _crystalsCredited + _foodCredited only, and those two ints were the only thing the
        // victory screen was ever handed - so the screen could not report wood, iron or gold
        // even after GrantLoot started measuring all five (dw/di/dg were computed at :410-411
        // and thrown away). Raids pay all five (PROGRAM_RAID_ECONOMY_2026-09-04 section 1), so
        // the player was told about two fifths of the payout. Still the MEASURED delta, never
        // the requested amount - that is the WO-978 contract, unchanged and now widened.
        private ResourceCost _credited;
        private bool _rewardShort;

        /// <summary>
        /// WO-1789 — WHERE THE OVERFLOW ACTUALLY WENT, carried from the settle to the screen.
        ///
        /// <para>Until this ticket the Raid Cache existed ONLY in the log. The settle retained the
        /// bank's refusals (STEP 3.5b), traced them in full, and the screen then said the generic
        /// <c>"Some of the reward could not be paid out."</c> — which does not say where it went,
        /// nor that it is recoverable. A player who reads that has been told her haul was lost.</para>
        ///
        /// <para>⚠ TWO NUMBERS, NOT ONE, BECAUSE THERE ARE THREE OUTCOMES. <c>_overflowCached</c> is
        /// what the Raid Cache is HOLDING for her (claimable). <c>_overflowLost</c> is what was above
        /// BOTH the bank's headroom and the cache's stated ceiling — <c>RaidClaimService.RetainAxis</c>'s
        /// <c>stillRefused</c>, the ONE path on which a raid unit leaves the world. Collapsing them
        /// would put "you can claim it back" on a screen where part of it is genuinely gone, which is
        /// the §11B failure with a friendly face. <c>EndStateVM.RaidOverflowSentence</c> owns which
        /// sentence each case gets; this field only carries the measurement.</para>
        ///
        /// <para>CAPPED AXES ONLY (wood / iron / stone). Crystals and gold are never clamped
        /// (TownBankCapacity Law 1) so they are never cached, and a shortfall on one of them keeps
        /// the generic sentence — the cache had nothing to do with it.</para>
        /// </summary>
        private int _overflowCached;
        private int _overflowLost;

        /// <summary>
        /// WO-1789 — what the bank refused on ONE axis, or 0 when that axis has no ceiling.
        /// The <c>IsCapped</c> test is <c>RaidClaimService.RetainAxis</c>'s own first line, repeated
        /// here so the sentence the screen picks and the units the settle actually retained are
        /// derived from the SAME rule. A shortfall on an uncapped axis is not an overflow.
        /// </summary>
        private static int RefusedOnCappedAxis(DeNelle.Core.Economy.BankResource r, int requested, int credited)
        {
            if (!DeNelle.Core.Economy.TownBankCapacity.IsCapped(r)) return 0;
            if (requested <= 0) return 0;
            if (credited < 0) credited = 0;
            return Mathf.Max(0, requested - credited);
        }

        /// <summary>
        /// THE FIRST-CLEAR GATE (defect sweep 2026-08-15). A base pays its settled loot on
        /// the clear that CLAIMS it; a re-clear of an already-claimed base is scaled by
        /// <see cref="RaidClaimService.RepeatClearLootMultiplier"/>.
        ///
        /// <para>WO-1134 — CRYSTALS ARE NO LONGER ON THAT AXIS. This method used to be the
        /// whole story ("a claimed base never pays crystals again"); the owner ruling replaced
        /// that with a once-per-UTC-DAY stamp, so crystals are decided by
        /// <c>crystalsPaidToday</c> and reset every day even on a long-claimed base, while the
        /// multiplier keeps governing wood/food/iron/coins. Two flags, two questions.</para>
        ///
        /// <para>THE HOLE THIS CLOSES: loot was never gated on <c>newClaim</c> at all. The
        /// claim set was written and never read, so re-entering a cleared base and razing it
        /// again paid the FULL settled payout, every time, forever - and the raid catalog's
        /// Extreme tier carries rewardMultiplier 2.2, making the most lucrative base in the
        /// game an unbounded resource faucet. The companion unlock beside it was already
        /// gated on newClaim; the resources simply were not.</para>
        ///
        /// <para>Reports on BOTH branches: a player who re-clears a base and receives nothing
        /// must be able to see WHY in a capture, and a first clear must be able to prove it
        /// paid in full. Never silent.</para>
        /// </summary>
        private static ResourceCost ApplyFirstClearGate(ResourceCost loot, bool repeatClear,
                                                        bool crystalsPaidToday, string configId)
        {
            ResourceCost scaled = RaidClaimService.ScaleLootForClear(loot, repeatClear, crystalsPaidToday);

            // THE CRYSTAL DAY-STAMP DECISION (WO-1134) — reported on BOTH branches, because a
            // player who cleared a camp twice and got crystals only once must be able to see
            // WHY in a capture, and a paying clear must be able to prove it paid.
            if (loot.Crystals > 0)
            {
                if (crystalsPaidToday)
                    FlowTrace.Warn("Raid",
                        $"CRYSTAL DAY-STAMP: '{configId}' already paid crystals this UTC day - " +
                        $"withholding {loot.Crystals} crystals (paying {scaled.Crystals}). Crystals reset at " +
                        "UTC midnight, so the DAY is the crystal bound now, not the cooldown; the ordinary " +
                        "resources on this clear are unaffected by this axis.");
                else
                    FlowTrace.Step("Raid",
                        $"CRYSTAL DAY-STAMP: '{configId}' has NOT paid crystals this UTC day - paying " +
                        $"{scaled.Crystals} crystals IN FULL (repeatClear={repeatClear}).");
            }

            if (!repeatClear)
            {
                if (!loot.IsZero)
                    FlowTrace.Step("Raid", $"FIRST-CLEAR gate: '{configId}' was unclaimed - paying the settled " +
                                           $"ordinary loot IN FULL ({Describe(scaled)}).");
                return scaled;
            }

            FlowTrace.Warn("Raid",
                $"REPEAT CLEAR of '{configId}' (already claimed) - ordinary loot scaled by " +
                $"x{RaidClaimService.RepeatClearLootMultiplier:0.##}: {Describe(loot)} -> {Describe(scaled)}. " +
                "The reduced ordinary-resource payout keeps practice runs useful; crystals on a repeat " +
                "are decided by the UTC day-stamp above, NOT by this multiplier.");
            return scaled;
        }

        /// <summary>
        /// WO-1373 — THE SINGLE RAID ROUGH-STONE GRANT. Owner ruling 2026-09-09: only the top
        /// two camp tiers, at most one stone per UTC day across every camp.
        ///
        /// <para>The RULE is <see cref="RaidScoring.ShouldDropRoughStone"/> - pure, static, and
        /// asserted offline by <c>RaidRoughStoneDropRegression</c>. This method is only the
        /// plumbing: read the ledger, ask the rule, put the stone in the larder, stamp the day.
        /// It reports on BOTH branches, because a player who cleared the Iron Bastion and got no
        /// stone must be able to see WHY in a capture (CLAUDE.md section 12).</para>
        ///
        /// <para>⛔ THE GRANT ITSELF IS NOT PERFORMED HERE, AND MUST NOT BE. WO-1112's law, pinned
        /// by <c>ComposedDungeonRunRegression</c> case <c>[exit-pays]</c>, is that EXACTLY ONE site
        /// under <c>Assets/_Modules</c> may write <c>DungeonRunPayout.LastPolishScore</c> - that
        /// site is <c>DungeonController.BankRoughStone</c>, and everything that earns a stone must
        /// REACH it rather than copy it. This lane's first attempt inlined the bank here and the
        /// oracle caught it by name ("2 sites write ... the payout was DUPLICATED rather than
        /// shared"). It was right, and this is the corrected shape: the gates live here, the grant
        /// lives there.</para>
        ///
        /// <para>⚠ IT GOES THROUGH A DELEGATE BECAUSE THE ASSEMBLIES FORBID A DIRECT CALL, not as
        /// a matter of taste: <c>DeNelle.Dungeons</c> references <c>DeNelle.Village</c> (read at
        /// source in its <c>.asmdef</c>), so this file cannot name <c>DungeonController</c> without
        /// a circular reference. The seam is declared in <c>DeNelle.Core</c>
        /// (<c>DungeonRunPayout.GrantRoughStone</c>) and implemented in <c>DeNelle.Dungeons</c> -
        /// the same inversion CLAUDE.md section 5 mandates for every other cross-module call.</para>
        ///
        /// <para>⚠ THE POLISH GRADE IS AN INPUT, AND ITS MAPPING IS UNRULED. The FIFO carries one
        /// grade per un-polished stone and pays oldest-first, so a stone banked with no grade would
        /// silently consume a dungeon run's. The raid passes its settled STAR count - both scales
        /// are 0..3 (<c>DungeonRunGrade.MaxStars</c>) - as the DOCUMENTED DEFAULT, not as a table
        /// anyone invented: ⛔ WO-1373 section 5.2 asks whether the drop should scale by stars and
        /// the owner has NOT answered. Still flagged as open in the RESULT.</para>
        ///
        /// <para>⚠ The day ledger is stamped ONLY on a TRUE return - the authority reports whether
        /// a stone actually reached the larder, so a failed bank cannot burn the player's one
        /// stone of the day. Same lesson as <c>MarkCrystalsPaid</c>.</para>
        /// </summary>
        private void GrantRoughStoneIfEarned(string configId, int stars)
        {
            Guard.Try("Raid", "grant raid rough stone", () =>
            {
                int minTier = RaidScoring.RoughStoneMinTier;
                int perDayCap = RaidScoring.RoughStonePerDayCap;
                int today = RaidScoring.RoughStonesGrantedToday();

                if (!RaidScoring.ShouldDropRoughStone(configId, today, minTier, perDayCap, out string why))
                {
                    FlowTrace.Step("Raid", "ROUGH STONE withheld: " + why);
                    return;
                }

                bool granted = DeNelle.Core.Catalog.DungeonRunPayout.GrantRoughStone(
                    stars, "raid clear '" + (configId ?? "(none)") + "'");

                if (!granted)
                {
                    // Never silent, and never stamped: the authority already said WHY it could
                    // not bank, so this line adds only the consequence the reader needs.
                    FlowTrace.Warn("Raid",
                        "ROUGH STONE not banked for '" + (configId ?? "(none)") + "' although the " +
                        "rule said pay (" + why + ") - the authority reported failure above. The " +
                        "UTC day ledger is deliberately NOT stamped, so this clear has not spent " +
                        "the player's stone for today.");
                    return;
                }

                RaidScoring.MarkRoughStoneGranted();
                FlowTrace.Step("Raid",
                    "ROUGH STONE paid by raid '" + (configId ?? "(none)") + "' (" + why +
                    "). Polish grade supplied = " + stars + " star(s). The bank, the grade and " +
                    "the announce were all done by the ONE authority (WO-1112); this path owns " +
                    "only the tier gate and the per-UTC-day cap.");
            });
        }

        private void GrantLoot(ResourceCost loot)
        {
            // WO-1227 §12 — THIS is the raid's whole resource payout. Owner ruling 2026-08-26:
            // "raids only pay at end of raid". Its counterpart is the per-kill suppression trace
            // in Enemy's death grant ("KILL MATERIALS SUPPRESSED (raid active)"), and the two
            // lines are meant to be read together: N suppressed kills followed by exactly ONE of
            // these is the ruling working. Logged BEFORE the zero-check so a raid that pays
            // NOTHING still says so — a silent nothing is what a suppressed faucet looks like.
            DeNelle.Core.Diagnostics.FlowTrace.Step("Reward",
                $"RAID END PAYOUT (the ONE raid grant, WO-1227) crystals={loot.Crystals} " +
                $"stone={loot.Stone} wood={loot.Wood} iron={loot.Iron} coins={loot.Coins} " +
                $"zero={loot.IsZero} - per-kill materials were withheld for the whole raid on " +
                "purpose; this grant is the payout.");

            if (loot.IsZero) return;

            _credited = default(ResourceCost); _rewardShort = false;

            var gs = GameStateService.Instance;
            if (gs == null || gs.State == null)
            {
                _rewardShort = true;
                FlowTrace.Fail("Raid", "LOOT NOT CREDITED ? no loaded GameState; " + Describe(loot));
                return;
            }

            // Recreate the normal authority if scene/bootstrap ordering left it absent.
            // Every axis follows the same cap and notification rules as a normal raid payout.
            var eco = EconomyService.EnsureAvailable();
            int w0 = eco.Wood, s0 = eco.Stone, i0 = eco.Iron, c0 = eco.Crystals, g0 = eco.Coins;
            eco.Grant(loot);
            int dw = eco.Wood - w0, ds = eco.Stone - s0, di = eco.Iron - i0,
                dc = eco.Crystals - c0, dg = eco.Coins - g0;
            _credited = new ResourceCost(wood: dw, stone: ds, iron: di, crystals: dc, coins: dg);
            _rewardShort = dw < loot.Wood || ds < loot.Stone || di < loot.Iron
                        || dc < loot.Crystals || dg < loot.Coins;
            LogCredit("EconomyService", loot, dw, ds, di, dc, dg);
        }

        /// <summary>
        /// WO-978 — the one place a raid loot grant is reported, always as
        /// <c>credited/requested</c> per axis. A shortfall is a <see cref="FlowTrace.Warn"/>
        /// naming both numbers and the consequence, never a routine Step, so a capture SHOWS
        /// the clamp instead of agreeing with the payout that never happened.
        /// </summary>
        private static void LogCredit(string route, ResourceCost requested,
                                      int dWood, int dFood, int dIron, int dCrystals, int dCoins)
        {
            string measured =
                $"wood {dWood}/{requested.Wood}, food {dFood}/{requested.Stone}, iron {dIron}/{requested.Iron}, " +
                $"crystals {dCrystals}/{requested.Crystals}, gold {dCoins}/{requested.Coins} (credited/requested)";

            bool shortfall = dWood     < requested.Wood
                          || dFood     < requested.Stone
                          || dIron     < requested.Iron
                          || dCrystals < requested.Crystals
                          || dCoins    < requested.Coins;

            if (shortfall)
                FlowTrace.Warn("Raid",
                    $"LOOT SHORT via {route} — the wallet took LESS than the raid awarded: {measured}. " +
                    "Raid loot is EarnedIncome, which TownBankCapacity clamps against the town storage " +
                    "ceiling — the player earned this and did not receive it. (WO-978: what should happen " +
                    "at cap is an OPEN owner question; this line only stops the log from claiming payment.)");
            else
                FlowTrace.Step("Raid", $"LOOT credited via {route}: {measured}.");
        }

        /// <summary>Human-readable requested loot, for the never-credited failure lines.</summary>
        private static string Describe(ResourceCost loot)
            => $"requested wood {loot.Wood}, food {loot.Stone}, iron {loot.Iron}, " +
               $"crystals {loot.Crystals}, gold {loot.Coins}";

        // =====================================================================
        //  ARMY RECONCILE (the WIN half of the wounded / veterancy model)
        // =====================================================================

        /// <summary>
        /// Settles the army for a WON raid through the deploy HUD's single latched reconcile
        /// (RaidDeployController.ReconcileRaidEnd): every troop that was deployed but did not
        /// survive is marked wounded, and on a 3-star clear each survivor gains a veterancy
        /// rank. Called while the surviving bodies are still on the field - the victory path
        /// tears down no troops and the scene only unloads at ReturnHome - so the survivor set
        /// is real. Persists immediately so the cost and the reward cannot be lost if the
        /// player closes the app on the victory screen.
        /// </summary>
        // WO-1810 - what this WIN cost in troops, captured at the reconcile and stated on the
        // victory screen. -1 = no deploy ledger reconciled (a raid nothing was deployed into),
        // and the screen then says nothing rather than printing a zero it cannot prove.
        private int _troopsLostThisRaid = -1;

        private void ReconcileArmy(RaidResult result)
        {
            var deploy = FindAnyObjectByType<RaidDeployController>();
            if (deploy == null)
            {
                FlowTrace.Warn("Raid", "victory: no RaidDeployController in this raid scene - " +
                                       "there is no troop ledger to reconcile (nothing was deployed through the HUD).");
                return;
            }

            int stars = result != null ? result.Stars : 0;
            if (result == null)
                FlowTrace.Warn("Raid", "victory: no RaidResult (no scorer) - reconciling at 0 stars, no veterancy granted.");

            Guard.Try("Raid", "victory army reconcile", () => deploy.ReconcileRaidEnd(stars));

            // WO-1810 - CARRY THE COST TO THE SCREEN. A win now kills troops for good ("any troop
            // killed is dead"), and ShowVictoryScreen runs AFTER this method, so the count is read
            // here, from the one writer, rather than re-derived anywhere else.
            _troopsLostThisRaid = deploy.LastTroopsLost;

            GameStateService.Instance?.Save();
            FlowTrace.Step("Raid", $"army settled for the WIN (stars {stars}) and saved. " +
                                   $"Troops lost for good on this win: {_troopsLostThisRaid} " +
                                   "(-1 = this raid never reconciled a deploy ledger).");
        }

        // =====================================================================
        //  THE RAID'S SCENE-CONFIG ID (WO-1869)
        // =====================================================================
        //
        // ⛔ THIS USED TO BE STRING SURGERY ON THE SCENE NAME, AND IT COST THE OWNER THE
        // TOWN CAPTURE. The comment here said "prefer the spawner's stored id (via the
        // public garrison API)" while the body never read the spawner at all — it stripped
        // "RaidBase_" off the scene name and returned the remainder. Three of the four raid
        // scenes are named in snake_case (RaidBase_raider_camp_small,
        // RaidBase_fortified_garrison, RaidBase_mage_enclave) so the strip happened to
        // agree with the catalog id; the Bastion scene is PascalCase
        // (RaidBase_IronBastion), so the strip yielded "IronBastion" while
        // OwnedBaseProgression.FinalRaidId is "iron_bastion".
        //
        // MEASURED, on the owner's Seeker run of build 2026.09.18.374427
        // (Logs/device/logcat-bastion-victory-20260918.txt):
        //   :202350  [Flow:Raid] OBJECTIVE COMPLETE - RaidSpire ... (config 'iron_bastion') RAZED.
        //   :202352  [Flow:Raid] VICTORY - raid 'IronBastion' won (SPIRE RAZED).
        //   :202395  [Flow:EndState] RAID VICTORY composed: baseClaimed=False ... stars=3
        // The spawner had the right id in the SAME log line the controller got the wrong
        // one. A 3-star, 65-second clear of the highest raid therefore never satisfied the
        // ordinal compare at HandleVictory (:387), the capture never fired, and neither
        // "CAPTURE ELIGIBLE" nor the shortfall line printed even once in a 208k-line log.
        // The same wrong id kept the rough-stone faucet off ("camp 'IronBastion' is not on
        // the I..IV ladder").
        //
        // ⛔ THE SCENE IS NOT RENAMED, DELIBERATELY: the literal "RaidBase_IronBastion" is
        // load-bearing in live code — :191 of this file, SceneRouter.RaidBaseIronBastion
        // (Assets/_Modules/Core/SceneRouter.cs:195), RaidCaptureCensus.cs:26,
        // OwnedTownScenePose.cs:59, DevSkipKit.cs:87 — plus editor suites, and the .unity is
        // baked by name. Grep the literal for the current set; no count is written here,
        // because a hand-maintained tally in a comment is the duplicated state CLAUDE.md §5
        // retired a whole dependency table over. (Grepped 2026-09-18: the WO's own pointer at
        // "RaidSelectionScreen.cs:452" is a COMMENT, not a check.)
        // The catalog already carries the mapping (scene-configs.json: id
        // "iron_bastion", sceneName "RaidBase_IronBastion") — so the fix is to ASK THE
        // CATALOG instead of re-deriving an id that already exists. A derived copy of
        // authored state is the duplicated state CLAUDE.md §2/§5/§16 each describe.
        private string ResolveConfigId(RaidGarrisonSpawner spawner)
            => ResolveConfigId(gameObject.scene.name, spawner != null ? spawner.ConfigId : null);

        /// <summary>
        /// The raid's scene-config id, resolved from AUTHORED data rather than string surgery.
        /// Order: (1) the spawner's own stored config id, (2) the scene-config catalog row whose
        /// <c>sceneName</c> matches, (3) — last resort only — the legacy "RaidBase_" strip, which
        /// is <see cref="FlowTrace.Warn"/>ed so a future scene/catalog mismatch is visible in the
        /// log instead of silently mis-identifying the camp.
        ///
        /// <para>PURE and STATIC on purpose: the capture gate is the thing this feeds, and
        /// <c>RaidConfigIdResolveRegression</c> proves catalog parity by calling THIS function for
        /// every authored raid row. A regression that re-implemented the rule would only prove
        /// itself.</para>
        /// </summary>
        /// <param name="sceneName">The loaded raid scene's name (<c>gameObject.scene.name</c>).</param>
        /// <param name="spawnerConfigId">The garrison spawner's stored id, or null when unknown.</param>
        public static string ResolveConfigId(string sceneName, string spawnerConfigId)
        {
            string id;
            string via;
            // Computed into a local FIRST: a nested string literal inside an interpolation hole
            // ends the string as far as CompileGate.BraceBalanced is concerned (CLAUDE.md §1).
            string sceneLabel = string.IsNullOrEmpty(sceneName) ? "(none)" : sceneName;

            if (!string.IsNullOrWhiteSpace(spawnerConfigId))
            {
                id = spawnerConfigId;
                via = "spawner";
            }
            else
            {
                var cfg = SceneConfigCatalog.FindBySceneName(sceneName);
                if (cfg != null && !string.IsNullOrEmpty(cfg.id))
                {
                    id = cfg.id;
                    via = "catalog";
                }
                else
                {
                    // LAST RESORT — the legacy behaviour, byte for byte, so a scene with no
                    // catalog row still yields something usable instead of throwing.
                    const string prefix = "RaidBase_";
                    if (!string.IsNullOrEmpty(sceneName) &&
                        sceneName.StartsWith(prefix, System.StringComparison.OrdinalIgnoreCase))
                        id = sceneName.Substring(prefix.Length);
                    else
                        id = string.IsNullOrEmpty(sceneName) ? "unknown" : sceneName;
                    via = "strip";

                    FlowTrace.Warn("Raid", "config id FELL BACK to the legacy scene-name strip for scene '" +
                        sceneLabel + "' -> '" + id + "'. No " +
                        "garrison spawner id and no scene-configs.json row matched this sceneName, so the id " +
                        "is DERIVED, not authored - exactly the WO-1869 defect that cost a 3-star capture. " +
                        "Add the row (or set the spawner's id) rather than renaming the scene.");
                }
            }

            FlowTrace.Step("Raid", $"config id resolved: '{id}' via {via} for scene '{sceneLabel}'.");
            return id;
        }

        // =====================================================================
        //  STEP 2 — CLAIM. Persist the win + flip the live scene PLAYER-owned.
        // =====================================================================

        private bool ClaimBase(string configId)
        {
            bool newClaim = RaidClaimService.MarkClaimed(configId);

            // THE FLIP (WO-441 Phase-C payoff beat, the spine of it): the inverse of the
            // spawner's SceneOwnership.SetEnemyOwned(true) — the cleared base now reads as
            // the player's for the rest of this session (death no longer retreats as if in
            // enemy territory; build mode is permitted). Persisted ownership is in
            // RaidClaimService; this flips the LIVE runtime flag too.
            SceneOwnership.SetEnemyOwned(false);
            FlowTrace.Step("Raid", $"CLAIM — '{configId}' flipped ENEMY -> PLAYER-owned " +
                                   $"(newClaim={newClaim}). The base is yours.");

            // Persist immediately so the claim survives even if the player closes the app
            // before the return completes.
            GameStateService.Instance?.Save();
            return newClaim;
        }

        // =====================================================================
        //  STEP 3 — NEXT COMPANION. Unlock the next canon companion into the party.
        // =====================================================================

        /// <summary>
        /// Adds the NEXT canon companion (a class != the player's hero, not already in
        /// the party) to the persisted roster — the "rescue the held hero -> he joins"
        /// beat. Returns the joined companion's display name (for the banner), or null
        /// if the party is already full (all three companions recruited).
        /// </summary>
        private string UnlockNextCompanion()
        {
            var svc = GameStateService.Instance;
            if (svc == null || svc.State == null)
            {
                FlowTrace.Warn("Raid", "next-companion: no GameStateService — cannot enrol a companion.");
                return null;
            }

            HeroClass player = svc.State.HeroClass.ToNullable() ?? HeroClass.Knight;

            // The three companion classes are every class EXCEPT the player's own (the
            // player embodies their class on the field; the roster fills with the other
            // three). Canon join feel: Knight(Grom), Ranger(Sylas), Cleric(Elara),
            // Mage(Thrain) — we add the first one not yet recruited, in this stable order.
            HeroClass[] order = { HeroClass.Ranger, HeroClass.Cleric, HeroClass.Knight, HeroClass.Mage };
            foreach (var cls in order)
            {
                if (cls == player) continue;                 // never the player's own class
                if (svc.IsInParty(cls.ToString())) continue; // already recruited
                svc.AddToParty(cls.ToString());              // fires PlayerChanged -> StoryCompanionInjector spawns the body + Save
                string name = CompanionDialogue.NameFor(cls);
                FlowTrace.Step("Raid", $"NEXT COMPANION — rescued {name} ({cls}); enrolled into the party.");
                return name;
            }

            FlowTrace.Step("Raid", "next-companion: party already complete (all three companions recruited) — no new join.");
            return null;
        }

        // =====================================================================
        //  STEP 4 — RETURN. Victory banner + route home (never soft-lock).
        // =====================================================================

        private void ShowVictoryScreen(string configId, string joinedCompanionName,
                                       RaidResult result, ResourceCost loot, int victories)
        {
            try
            {
                // Route the win through the ONE shared Obsidian EndState template. Its
                // single primary action (Return to Castle) fires ReturnHome, and its
                // AutoDismissSeconds (fed _autoReturnSeconds) IS the anti-soft-lock guard
                // that previously lived in AutoReturnRoutine — same route, same timing.
                // WO-771.6: the win now carries stars + %-destruction + the loot breakdown.
                // WO-978 follow-up: show the CREDITED amounts, never the requested ones. At a
                // capped bank these differ, and the screen is what the player believes.
                // WO-1374 - the WHOLE CREDITED BASKET goes to the screen now. The retired call
                // handed it two ints (_crystalsCredited, _foodCredited) and the screen rendered
                // the second one under the label "Stone" - a currency retired as a balance
                // (GameState.cs:59 records the removal in-code). Wood, iron and gold were
                // measured in GrantLoot and then dropped on the floor. Still CREDITED, never
                // requested: at a capped town bank those differ and the screen is what the
                // player believes (the WO-978 contract).
                var vm = EndStateVM.FromRaidVictory(
                    joinedCompanionName, ReturnHome, _autoReturnSeconds,
                    result != null ? result.Stars : -1,
                    result != null ? result.DestructionPercent : -1,
                    result != null ? result.ElapsedSeconds : -1f,
                    _credited,
                    // The unlock line is a HAND-OFF, not a decision made here: this file knows
                    // the win count, and the sibling ladder lane knows what that count unlocks.
                    // Null until that lane fills it, and the VM renders nothing for null. The
                    // target NAMES it will use are CREATIVE_CANON_ELARION_2026-09-04 section 3
                    // ("The Broken Garrison"), never the superseded "Ironwatch Garrison" pass.
                    ResolveUnlockLine(victories),
                    // WO-1810 - the troops this win cost for good, read off the reconcile that
                    // already ran (ReconcileArmy). The VM states it only when it is above zero.
                    _troopsLostThisRaid,
                    // WO-1783 - WHICH WIN THIS IS. The CLAIMED line used to be the subtitle of every
                    // raid win, camp clears included; it now belongs to a capture alone, and the
                    // star requirement is stated in words on the one screen where missing it just
                    // cost her the town. Named arguments: both are messaging, and neither may ever
                    // be mistaken for the gate above them.
                    baseClaimed: _captureRequired,
                    captureStarsRequired: _captureRaidShortOfStars
                        ? OwnedBaseProgression.CaptureStarsRequired
                        : 0);

                if (_captureRequired && vm != null)
                {
                    vm.PrimaryLabel = LocalText.Get("ownedTown.enter");
                    vm.PrimaryGate = CanEnterCapturedTown;
                }

                // ── WO-1789 §3.1 — THE VETERANCY CAPTION, ON THE STAR ROW THAT DECIDED IT ──────
                // A win below RaidDeployController.VeterancyStarsRequired grants NO ranks and, until
                // this ticket, said so only in the log: the owner's own 2026-09-16 run produced
                // "veterancy: 2 star(s) - no ranks granted (3 stars required)." and the screen was
                // silent. The caption states what the missing star would have granted.
                //
                // ⛔ THE COUNT IS READ OFF THE GATE'S OWN CONST, NEVER TYPED. Retuning the gate
                // retunes the caption in the same edit - the duplicated-state failure §3.3 of the WO
                // exists to close. The copy itself lives on the VM (one home for the words).
                //
                // Gated on a REAL star result (>= 0): a raid that never scored has nothing to say
                // about a star it cannot prove, exactly like the -1 sentinels elsewhere on this screen.
                if (vm != null && vm.Stars >= 0 && vm.Stars < RaidDeployController.VeterancyStarsRequired)
                {
                    vm.StarCaption = EndStateVM.VeterancyDeniedCaption(
                        RaidDeployController.VeterancyStarsRequired);
                    FlowTrace.Step("Raid",
                        "VICTORY SCREEN: " + vm.Stars + " star(s) is short of " +
                        RaidDeployController.VeterancyStarsRequired + " - the star row now CARRIES the " +
                        "veterancy caption (WO-1789 §3.1), which before this ticket existed only in " +
                        "the [Flow:Raid] log.");
                }

                if (_rewardShort && vm != null)
                {
                    // WORDS, never colour alone — the owner is red/green colourblind, so a dimmed
                    // number would carry no information at all.
                    //
                    // ── WO-1789 §3.2 — NAME THE RAID CACHE ────────────────────────────────────
                    // The retired line was the literal "Some of the reward could not be paid out."
                    // twice over. It is TRUE and it is USELESS: it does not say where the overflow
                    // went, and it reads as "lost" on a screen where the settle has just RETAINED it
                    // for her (RaidClaimService's own log: "RETAINED, not burned ... Upgrade or
                    // spend, then claim it."). The VM picks between three sentences off the two
                    // numbers STEP 3.5b measured - held / partly gone / nothing to do with the cache
                    // - so "recoverable" is only ever said when it is true (§11B).
                    string line = EndStateVM.RaidOverflowSentence(_overflowCached, _overflowLost);
                    vm.Subtitle = string.IsNullOrEmpty(vm.Subtitle) ? line : vm.Subtitle + " " + line;
                    FlowTrace.Step("Raid",
                        "VICTORY SCREEN: reward was short, cached=" + _overflowCached + " lost=" +
                        _overflowLost + " -> the screen now says: " + line);
                }

                EndStateView.Show(vm);

                // WO-1768 — THE SCREEN IS UP, AND NOW IT SAYS SO. Set here and nowhere else:
                // after Show returned without throwing, and INSIDE this try, so a presentation
                // failure falls to the catch below with the latch still false and the death
                // path's EVAC keeps its job. Read by HeroHealth.HandleDeath through
                // VictoryOwnsTheReturn, which is the only reason this latch exists.
                _victoryScreenUp = true;

                FlowTrace.Step("Raid", $"RETURN — victory screen shown for '{configId}' " +
                    (joinedCompanionName != null ? $"(+{joinedCompanionName})" : "(party already full)") +
                    (_captureRequired ? "; continue to the captured town." : "; return to the castle."));
            }
            catch (System.Exception e)
            {
                // A presentation failure must NEVER strand the player — fall straight through to return.
                FlowTrace.Fail("Raid", "victory screen build threw — returning home directly: " + e.Message);
                ReturnHome();
            }
        }

        /// <summary>
        /// The optional "X unlocked" line for the victory screen, given the new victory count.
        ///
        /// <para>WO-1562 - THIS RETURNED NULL UNCONDITIONALLY, AND THE DEFERRAL IT NAMED HAD
        /// EXPIRED. Its retired body said the section-4 thresholds "belong to the ladder lane,
        /// not to this file". That lane is WO-1375, CLOSED 2026-09-06, and its RESULT does not
        /// claim this seam - so the announcement was not deferred, it was ORPHANED. Meanwhile the
        /// grid advertised the ladder at one end, the win was counted at the other
        /// (<see cref="RecordVictory"/>), and the screen in the middle said nothing; only the
        /// first-raid tutorial dialogue (PostRaidBeatTokens) ever spoke it, once, ever.</para>
        ///
        /// <para>THE ORIGINAL REASONING IS HONOURED, NOT OVERTURNED. Naming a target here would
        /// still fork the ladder across two files - so nothing is named here. It asks
        /// <c>RaidSelectionVM.UnlockAnnouncementFor</c>, which reads the SAME
        /// <c>FlagshipRaidIds</c> + authored <c>unlockVictories</c> pair the grid's own lock
        /// sentences read. ONE ladder, one set of thresholds, no copy (WO-1562 acceptance 2).</para>
        ///
        /// <para>STILL TRACED ON BOTH BRANCHES, which is the property the retired comment was
        /// written to preserve: a player crossing a threshold and seeing no line must stay
        /// distinguishable IN A CAPTURE from a player who crossed nothing. Never strip FlowTrace
        /// (CLAUDE.md section 12).</para>
        /// </summary>
        private static string ResolveUnlockLine(int victories)
        {
            string line = null;
            // Guarded: the catalog is legitimately absent in EditMode / headless, and a fault
            // resolving the ladder must never cost the player their victory screen.
            Guard.Try("Raid", "resolve the victory unlock line",
                () => { line = DeNelle.Village.Hero.RaidSelectionVM.UnlockAnnouncementFor(victories); });

            if (string.IsNullOrEmpty(line))
            {
                FlowTrace.Step("Raid", $"UNLOCK LINE: victories={victories} crossed NO ladder threshold - " +
                                       "the victory screen stays silent, which is correct. (A count that " +
                                       "DOES cross one logs the announcement instead, so the two cases are " +
                                       "distinguishable in a capture.)");
                return null;
            }

            FlowTrace.Step("Raid", $"UNLOCK LINE: victories={victories} CROSSED a ladder threshold - " +
                                   $"announcing \"{line}\". Thresholds are read from the raid catalog's " +
                                   "authored unlockVictories through RaidSelectionVM, the same authority " +
                                   "the grid's lock sentences read - never a second copy in this file.");
            return line;
        }

        private void ReturnHome()
        {
            if (_returning) return;
            if (!CanEnterCapturedTown()) return;
            _returning = true;
            FlowTrace.Step("Raid", _captureRequired ? "RETURN -> captured personal town."
                : _captureForfeitedToCastle ? "RETURN -> castle (FORCED: the captured-town gate refused past its bound; "
                                              + "WO-1778's guaranteed exit, not the normal capture route)."
                : "RETURN -> castle.");
            GameStateService.Instance?.Save();
            // Clear the runtime enemy-owned flag before we leave so the home hub never
            // inherits a stale enemy-owned read from this raid.
            SceneOwnership.SetEnemyOwned(false);
            if (_captureRequired) SceneRouter.GoOwnedTown();
            else SceneRouter.GoCastle();
        }

        /// <summary>
        /// WO-1778 — THE GATE IS BOUNDED, SO THE PLAYER ALWAYS LEAVES THE RAID.
        ///
        /// <para>Return true = <see cref="ReturnHome"/> proceeds. The old body returned false on
        /// every refusal with no bound and no other exit, which is the strand: the victory screen's
        /// ONLY CTA toasted "try entering again" at a button that would refuse again, forever, and
        /// <c>EndStateView</c>'s anti-softlock guard could not clear the screen either because
        /// <c>FirePrimary</c> returned before its own <c>Destroy</c>.</para>
        ///
        /// <para>ONE refusal is still a real retry (a save-service outage does clear on a second
        /// tap). The refusal AFTER the bound FORFEITS the captured town to a castle route rather
        /// than the player's evening: <c>_captureRequired</c> is cleared so <see cref="ReturnHome"/>
        /// routes to <c>GoCastle</c>, and the receipt is parked first so the town is DEFERRED, not
        /// destroyed — <c>GameStateService</c> recovers a pending capture on its next load.</para>
        /// </summary>
        private bool CanEnterCapturedTown()
        {
            if (!_captureRequired || _captureCommitted || TryCommitCapturedTown()) return true;

            _captureRefusals++;
            if (_captureRefusals < CaptureRefusalsBeforeForcedExit)
            {
                FlowTrace.Warn("Raid", $"CAPTURE ENTRY REFUSED ({_captureRefusals}/{CaptureRefusalsBeforeForcedExit}) — " +
                                       "offering the retry toast and holding the victory screen up. The NEXT refusal " +
                                       "forces the castle route instead of asking again.");
                ElarionUiKit.ShowToast(LocalText.Get("ownedTown.captureRetry"));
                return false;
            }

            // ---- FORCED ROUTE HOME (the whole point of WO-1778) -------------------
            string parked = ParkCaptureForLaterClaim();
            _captureRequired = false;
            _captureForfeitedToCastle = true;
            FlowTrace.Fail("Raid", $"FORCED ROUTE HOME: captured-town entry refused {_captureRefusals}x " +
                                   $"(bound={CaptureRefusalsBeforeForcedExit}) — routing to the CASTLE " +
                                   "(SceneRouter.GoCastle) instead of stranding the player on the victory " +
                                   $"screen. Capture receipt: {parked}");
            return true;
        }

        /// <summary>
        /// WO-1778 — park the capture receipt before the forced exit, so a refused capture is
        /// DEFERRED rather than lost. Returns a one-line status for the forced-exit trace; never
        /// throws, and never blocks the route home.
        /// </summary>
        private string ParkCaptureForLaterClaim()
        {
            if (_captureCensus == null)
                return "NOT PARKED (no precombat census — this run's capture is forfeit; the census " +
                       "identity fix is WO-1767's lane, not this one).";

            string status = "NOT PARKED (the park attempt itself did not run).";
            Guard.Try("Raid", "park the captured-town receipt for a later claim", () =>
            {
                status = _captureCensus.TryParkForLaterClaim(GameStateService.Instance, _victoryStars, out var reason)
                    ? "PARKED as a pending capture — the next load claims the town (" + reason + ")."
                    : "NOT PARKED: " + reason;
            });
            return status;
        }

        private bool TryCommitCapturedTown() => TryCommitCapturedTown(_victoryStars);

        private bool TryCommitCapturedTown(int stars)
        {
            if (_captureCommitted) return true;
            try
            {
                if (_captureCensus == null)
                { FlowTrace.Fail("Raid", "Final victory cannot capture: precombat census is missing."); return false; }
                if (!_captureCensus.TryCommit(GameStateService.Instance, stars, out var reason))
                { FlowTrace.Warn("Raid", "Captured town save awaits retry: " + reason); return false; }
                _captureCommitted = true;
                FlowTrace.Step("Raid", "OWNED_TOWN_CAPTURED: final victory, settled condition and one-time repair supplies saved.");
                return true;
            }
            catch (System.Exception ex)
            { FlowTrace.Fail("Raid", "Captured town save awaits retry: " + ex.Message); return false; }
        }
    }
}

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
            go.AddComponent<RaidVictoryController>();
            FlowTrace.Step("Raid", $"RaidVictoryController self-installed in raid scene '{sceneName}'.");
        }

        // =====================================================================
        //  Bind to the garrison spawner (it spawns its garrison one frame after its
        //  own Start, so we poll a few frames for it rather than assuming Start order).
        // =====================================================================

        private void Start()
        {
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

            // STEP 1.5 — IS THIS A REPEAT CLEAR? This read MUST happen BEFORE ClaimBase,
            // which flips the persisted flag: query it afterwards and every clear reads as
            // a repeat. The answer feeds the first-clear loot gate at STEP 3.5.
            bool repeatClear = RaidClaimService.IsClaimed(configId);

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
            string joined = newClaim ? UnlockNextCompanion() : null;

            // STEP 3.5 (WO-771.6) — settle the V1 SCORE (0-3 stars from the real-time
            // clear/clock) and GRANT the loot. This is the win/stars/loot half that was
            // flagged OUT (this file :34). Null-safe: with no scorer the screen falls
            // back to the star-less banner and no loot is granted.
            RaidScoring scoring = RaidScoring.Instance;
            RaidResult result = scoring != null ? scoring.Finalize(true) : null;
            ResourceCost loot = scoring != null ? scoring.LootFor(result) : default(ResourceCost);
            loot = ApplyFirstClearGate(loot, repeatClear, crystalsPaidToday, configId);
            GrantLoot(loot);

            // WO-1134 — stamp the crystal day AFTER the grant, and only when this payout
            // actually carried crystals. Stamping before the grant (or unconditionally) would
            // burn the player's one crystal clear of the day on a payout that paid none.
            if (loot.Crystals > 0) RaidClaimService.MarkCrystalsPaid(configId);

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
                $"food={loot.Food} wood={loot.Wood} iron={loot.Iron} coins={loot.Coins} " +
                $"zero={loot.IsZero} - per-kill materials were withheld for the whole raid on " +
                "purpose; this grant is the payout.");

            if (loot.IsZero) return;

            _credited = default(ResourceCost); _rewardShort = false;

            var eco = EconomyService.Instance;
            if (eco != null)
            {
                // The wallet properties read straight through to the single GameState-backed
                // store (WO-842), so before/after is a real measurement of what was credited.
                int w0 = eco.Wood, f0 = eco.Food, i0 = eco.Iron, c0 = eco.Crystals, g0 = eco.Coins;
                eco.Grant(loot);
                int dw = eco.Wood - w0, df = eco.Food - f0, di = eco.Iron - i0,
                    dc = eco.Crystals - c0, dg = eco.Coins - g0;
                _credited = new ResourceCost(wood: dw, food: df, iron: di, crystals: dc, coins: dg);
                _rewardShort = dw < loot.Wood || df < loot.Food || di < loot.Iron
                            || dc < loot.Crystals || dg < loot.Coins;
                LogCredit("EconomyService", loot, dw, df, di, dc, dg);
                return;
            }

            var gs = GameStateService.Instance;
            var state = gs != null ? gs.State : null;
            if (gs != null && state != null)
            {
                // AddCrystals/AddFood are void too — measure GameState.Resources either side.
                // Note this fallback route has NO wood/iron/gold mover at all, so any of those
                // axes in the loot are DROPPED; LogCredit will say so instead of hiding it.
                int c0 = state.Resources.Crystals, f0 = state.Resources.Food;
                if (loot.Crystals != 0) gs.AddCrystals(loot.Crystals);
                if (loot.Food != 0) gs.AddFood(loot.Food);
                int dcF = state.Resources.Crystals - c0, dfF = state.Resources.Food - f0;
                // This fallback route has NO wood/iron/gold mover, so those axes are genuinely
                // zero credited - the basket says so rather than leaving them unset.
                _credited = new ResourceCost(food: dfF, crystals: dcF);
                // This route has no wood/iron/gold mover, so those axes are dropped outright —
                // that counts as short for the player-facing caveat, not just for the log.
                _rewardShort = dcF < loot.Crystals || dfF < loot.Food
                            || loot.Wood != 0 || loot.Iron != 0 || loot.Coins != 0;
                LogCredit("GameStateService fallback", loot,
                          0, dfF, 0, dcF, 0);
            }
            else if (gs != null)
            {
                FlowTrace.Fail("Raid", "LOOT LOST — GameStateService is present but has no loaded State; " +
                                       $"the win awarded {Describe(loot)} and NONE of it was credited.");
            }
            else
            {
                FlowTrace.Fail("Raid", "LOOT LOST — no EconomyService and no GameStateService present; " +
                                       $"the win awarded {Describe(loot)} and NONE of it was credited.");
            }
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
                $"wood {dWood}/{requested.Wood}, food {dFood}/{requested.Food}, iron {dIron}/{requested.Iron}, " +
                $"crystals {dCrystals}/{requested.Crystals}, gold {dCoins}/{requested.Coins} (credited/requested)";

            bool shortfall = dWood     < requested.Wood
                          || dFood     < requested.Food
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
            => $"requested wood {loot.Wood}, food {loot.Food}, iron {loot.Iron}, " +
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
            GameStateService.Instance?.Save();
            FlowTrace.Step("Raid", $"army settled for the WIN (stars {stars}) and saved.");
        }

        // The raid's scene-config id: prefer the spawner's stored id (via the public
        // garrison API), else derive it from the baked scene name (RaidBase_<id>).
        private string ResolveConfigId(RaidGarrisonSpawner spawner)
        {
            // The baked scene is named RaidBase_<configId>; strip the prefix.
            string scene = gameObject.scene.name;
            const string prefix = "RaidBase_";
            if (!string.IsNullOrEmpty(scene) &&
                scene.StartsWith(prefix, System.StringComparison.OrdinalIgnoreCase))
                return scene.Substring(prefix.Length);
            return string.IsNullOrEmpty(scene) ? "unknown" : scene;
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
                    ResolveUnlockLine(victories));

                if (_rewardShort && vm != null)
                {
                    // WORDS, never colour alone — the owner is red/green colourblind, so a dimmed
                    // number would carry no information at all. Same sentence the outpost uses.
                    vm.Subtitle = string.IsNullOrEmpty(vm.Subtitle)
                        ? "Some of the reward could not be paid out."
                        : vm.Subtitle + " Some of the reward could not be paid out.";
                }

                EndStateView.Show(vm);

                FlowTrace.Step("Raid", $"RETURN — victory screen shown for '{configId}' " +
                    (joinedCompanionName != null ? $"(+{joinedCompanionName})" : "(party already full)") +
                    "; tap or auto-dismiss routes to the castle.");
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
            _returning = true;
            FlowTrace.Step("Raid", "RETURN -> SceneRouter.GoCastle() (loop continues, no soft-lock).");
            GameStateService.Instance?.Save();
            // Clear the runtime enemy-owned flag before we leave so the home hub never
            // inherits a stale enemy-owned read from this raid.
            SceneOwnership.SetEnemyOwned(false);
            SceneRouter.GoCastle();
        }
    }
}

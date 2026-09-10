// =============================================================================
// Village2RaidController — makes Village2 a PLAYABLE raid destination (WO-433 v1).
// -----------------------------------------------------------------------------
// Assembly: DeNelle.Village   Namespace: DeNelle.Village.World.Camps
//
// Village2 is "where they go" — the enemy stronghold the player reaches via the
// castle -> OuterWorld -> cave-portal flow (confirmed working 2026-06-20). It was
// BUILT + baked (EnemyStrongholdBuilder) with 6 spawn points (3 chokepoints + 2
// courtyard + 1 keep — see BuildSpawnPoints) + a GarrisonController
// wired, but nothing ACTIVATED it (no enemies) and nothing detected the CLEAR (no
// victory). This component closes both ends — mirrors the proven RaidVictoryController
// (RaidBase_* scenes) but for Village2 + its GarrisonController:
//
//   SELF-INSTALL  — a RuntimeInitialize hook adds ONE controller to the Village2 scene
//                   (idempotent), like RaidVictoryController does for RaidBase_*.
//   ACTIVATE      — find the scene's GarrisonController + Activate() it AFTER the
//                   navmesh is live, so the garrison (orc-berserker/orc-shaman/troll/
//                   hollow-warrior, per garrison-recipes.json "village2_stronghold")
//                   spawns + the watchtower turrets arm. Idempotent.
//   VICTORY       — subscribe to GarrisonController.OnCleared (last defender dies).
//   CLAIM/COMPANION/RETURN — reuse the EXACT raid-victory services: claim the base
//                   (RaidClaimService + flip PLAYER-owned), unlock the next companion,
//                   victory banner + "Return to Castle" (+ auto-return anti-soft-lock).
//
// v1 WIN-CONDITION = CLEAR THE GARRISON (the spec default; owner may later switch to
// kill-boss / destroy-altar). Boss is whatever the recipe authors (currently none);
// reward = claim + next companion (the documented default). Code-built uGUI, ASCII
// runtime strings, Elarion canon. Headless-verified by the autopilot Village2 phase.
// =============================================================================

using System.Collections;
using UnityEngine;
using DeNelle.Core;
using DeNelle.Core.State;
using DeNelle.Core.UI;
using DeNelle.Core.Diagnostics;

namespace DeNelle.Village.World.Camps
{
    /// <summary>
    /// Activates the Village2 garrison on scene load and runs the raid-victory flow
    /// (claim -> next companion -> return home) when the garrison is cleared.
    /// Self-installs into the <c>Village2</c> scene. Mirrors RaidVictoryController.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class Village2RaidController : MonoBehaviour
    {
        private const string SceneName = "Village2";
        // NOTE (WO-550, flagged for owner): the CLAIM key is the SCENE NAME "Village2", not the
        // scene-configs id "village2_enemy_outpost". It is self-consistent (this controller both
        // WRITES it - ClaimBase -> RaidClaimService.MarkClaimed - and READS it back in
        // HandleCleared via RaidClaimService.IsRepeatClearInCycle (the AND of that permanent
        // flag and a still-running cooldown; corrected from the bare IsClaimed 2026-09-09,
        // WO-1373 lane RAID-3), to tell a first clear from a repeat inside the same cycle;
        // persisted as dotr-raid-owner-Village2) and keys
        // on scene name like the rest of the ownership system (SceneOwnership / HubScenes). Nothing
        // external reads "village2_enemy_outpost" as a claim key, so it is left as-is — changing it
        // would only orphan any existing saved claim. Switch to the config id only on an owner call.
        private const string ConfigId  = "Village2";

        [Tooltip("Seconds after victory before the hero auto-returns to the castle if the " +
                 "player never taps the button (anti-soft-lock safety net).")]
        [SerializeField] private float _autoReturnSeconds = 12f;

        private GarrisonController _garrison;
        private GameObject _ui;
        private GameObject _retreatUi;   // WO-550: always-available "Retreat" affordance during the raid
        private bool _handled;     // victory handled once (guards a double OnCleared)
        private bool _returning;   // a return is already in flight

        // Single-modal arbiter handle for the top-band (32000) victory banner. Registered
        // battle-allowed so the terminal win banner is never rejected/force-closed by the
        // battle-lock. Close delegate = ReturnHome; isOpen tracks the live banner.
        private PanelHandle _panelHandle;

        // =====================================================================
        //  Self-install — one controller in the Village2 scene
        // =====================================================================

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
            if (!string.Equals(sceneName, SceneName, System.StringComparison.OrdinalIgnoreCase)) return;
            if (FindAnyObjectByType<Village2RaidController>() != null) return;

            var go = new GameObject("Village2RaidController");
            go.AddComponent<Village2RaidController>();
            FlowTrace.Step("Raid", $"Village2RaidController self-installed in '{sceneName}'.");
        }

        // =====================================================================
        //  Bind — find the garrison, ACTIVATE it (spawn enemies), subscribe to clear.
        //  The garrison is wired by EnemyStrongholdBuilder but never auto-activated
        //  (activateOnStart=false); we activate it here, once the navmesh is live.
        // =====================================================================

        private void Start()
        {
            StartCoroutine(BindRoutine());
        }

        private void OnDestroy()
        {
            if (_garrison != null) _garrison.OnCleared -= HandleCleared;
            // Don't leak the arbiter slot if destroyed while the banner is up (scene unload).
            if (_panelHandle != null) PanelManager.NotifyClosed(_panelHandle);
        }

        private IEnumerator BindRoutine()
        {
            // WO-550 (anti-soft-lock): show the RETREAT affordance FIRST, before we even find the
            // garrison. EnemyStrongholdBuilder deliberately omits the ReturnToOuterWorld_Seam (WO-480
            // "one-way outpost"), so a player who can't win — or simply wants to bail — otherwise has
            // NO exit until the garrison is cleared. The button routes home via the same GoCastle path
            // AutoReturnRoutine uses (but WITHOUT claiming — the base stays enemy-owned on a retreat).
            BuildRetreatButton();

            // The stronghold root + its GarrisonController exist at scene load; give a
            // few frames for additive load + navmesh to settle before we spawn.
            for (int i = 0; i < 10 && _garrison == null; i++)
            {
                _garrison = FindGarrisonInThisScene();
                if (_garrison != null) break;
                yield return null;
            }

            if (_garrison == null)
            {
                FlowTrace.Warn("Raid", "Village2RaidController: no GarrisonController found in Village2 — " +
                                       "cannot populate the stronghold (no enemies, no victory). Leaving the return seam as the only out.");
                yield break;
            }

            // NAVMESH SETTLE (owner-flagged timing fix): the garrison snaps each defender to the nearest
            // navmesh point, so the surface must be LIVE first — additive load + bake can lag a few frames.
            // Wait (bounded) until a spawn point samples onto the navmesh so the garrison doesn't spawn
            // empty and insta-clear. Falls through after the cap so we never hang.
            yield return WaitForNavMeshReady();

            // ACTIVATE — spawn the garrison + arm the turrets (idempotent; a no-op if already activated).
            _garrison.Activate();
            yield return null;   // let the synchronous spawn settle one frame before we read AliveCount
            FlowTrace.Step("Raid", $"Village2 garrison ACTIVATED — {_garrison.AliveCount}/{_garrison.TotalGarrison} defender(s) live.");

            // VICTORY binding. If the garrison is already cleared OR spawned empty (no defenders / no
            // navmesh path), run victory now (anti-soft-lock — never strand the player in an empty raid);
            // else subscribe for the last-defender-dies event. The AliveCount==0 force is belt-and-
            // suspenders against a missed OnCleared on an empty composition.
            if (_garrison.Cleared || _garrison.AliveCount == 0)
            {
                FlowTrace.Step("Raid", $"Village2 garrison empty/already-cleared on bind (alive={_garrison.AliveCount}) — running victory immediately.");
                HandleCleared(_garrison);
            }
            else
            {
                _garrison.OnCleared -= HandleCleared;
                _garrison.OnCleared += HandleCleared;
                FlowTrace.Step("Raid", $"Village2RaidController bound to OnCleared (garrison of {_garrison.TotalGarrison} defender(s)).");
            }
        }

        // Bounded wait until the navmesh under the stronghold is live enough that a spawn point samples
        // onto it — so the garrison spawns ON-mesh instead of empty. Caps at ~30 frames so we never hang.
        private IEnumerator WaitForNavMeshReady()
        {
            Vector3 probe = _garrison != null ? _garrison.transform.position : Vector3.zero;
            for (int i = 0; i < 30; i++)
            {
                if (UnityEngine.AI.NavMesh.SamplePosition(probe, out _, 8f, UnityEngine.AI.NavMesh.AllAreas))
                    yield break;
                yield return null;
            }
            FlowTrace.Warn("Raid", "Village2RaidController: navmesh did not settle within the cap — activating anyway (spawns will snap if/when mesh appears).");
        }

        // Find the GarrisonController that lives in THIS controller's scene (Village2),
        // not a garrison from any other additively-loaded scene.
        private GarrisonController FindGarrisonInThisScene()
        {
            var all = FindObjectsByType<GarrisonController>();
            if (all == null) return null;
            var myScene = gameObject.scene;
            for (int i = 0; i < all.Length; i++)
                if (all[i] != null && all[i].gameObject.scene == myScene) return all[i];
            // Fallback: any garrison (single-garrison project) if the scene match misses.
            return all.Length > 0 ? all[0] : null;
        }

        // =====================================================================
        //  VICTORY — last defender died (or the garrison was empty).
        // =====================================================================

        private void HandleCleared(GarrisonController garrison)
        {
            if (_handled) { FlowTrace.Step("Raid", "Village2 victory already handled — ignoring duplicate OnCleared."); return; }
            _handled = true;
            if (_garrison != null) _garrison.OnCleared -= HandleCleared;

            // WO-550: the raid is won — drop the in-raid Retreat button (the victory banner owns the exit now).
            if (_retreatUi != null) { Destroy(_retreatUi); _retreatUi = null; }

            FlowTrace.Step("Raid", $"VICTORY — Village2 stronghold garrison wiped. Running claim -> next-companion -> return.");

            CoreServices.Audio?.PlayMusic(DeNelle.Core.Audio.MusicTrack.Victory);

            // CORRECTED 2026-09-09 (WO-1373 lane RAID-3, owner ruling 2026-09-06 20:33 as
            // landed by WO-1461). This read was RaidClaimService.IsClaimed, which is PERMANENT
            // by design - "have I EVER taken this camp". Her ruling is a CYCLE: "100% first
            // clear after cooldown, 60% repeat clear during the same cycle, then reset to 100%
            // when the camp's cooldown expires." RaidVictoryController.HandleVictory was moved
            // to IsRepeatClearInCycle for exactly that reason; this path was left behind, so the
            // raid-VILLAGE clear kept narrating "already claimed, forever" while the raid-CAMP
            // clear narrated the cycle. One ruling, two answers, is the defect.
            //
            // THE ORDER IS LOAD-BEARING ON BOTH SIDES and is already correct here: this read
            // precedes RaidCooldownService.BeginAfterClear below (which STAMPS the window the
            // new predicate reads) AND ClaimBase (which flips the claim flag). Query after
            // either and every clear, including the first, reports as a repeat.
            //
            // Village2 grants no RESOURCE loot (it has no RaidScoring), so there is no payout
            // to gate here; the one-time payoff is the companion, already gated on newClaim
            // below. The read is kept because a silent repeat clear is exactly the state that
            // hid the write-only claim set: say which one this was.
            bool repeatClear = RaidClaimService.IsRepeatClearInCycle(ConfigId);
            if (repeatClear)
                FlowTrace.Warn("Raid", $"REPEAT CLEAR of '{ConfigId}' INSIDE ITS CURRENT CYCLE - already " +
                                       "claimed AND its cooldown from the previous clear is still running. " +
                                       "No re-grant: no companion, no resources (this raid pays no resource " +
                                       "loot at all). Once the cooldown expires this reads as a first clear " +
                                       "again, per the owner's cycle ruling.");

            // WO-728 — open the per-camp cooldown on EVERY clear, first or repeat. Village2
            // pays no resource loot, so the cooldown is the only thing that makes re-clearing
            // it a paced beat rather than a free re-run. Stamped from the server-anchored
            // clock inside the service; before the banner so a presentation throw cannot skip it.
            RaidCooldownService.BeginAfterClear(ConfigId);

            bool newClaim = ClaimBase();
            string joined = newClaim ? UnlockNextCompanion() : null;

            BuildVictoryBanner(joined);
            StartCoroutine(AutoReturnRoutine());
        }

        // =====================================================================
        //  CLAIM — persist the win + flip the live scene PLAYER-owned.
        // =====================================================================

        private bool ClaimBase()
        {
            bool newClaim = RaidClaimService.MarkClaimed(ConfigId);
            SceneOwnership.SetEnemyOwned(false);
            FlowTrace.Step("Raid", $"CLAIM — '{ConfigId}' flipped ENEMY -> PLAYER-owned (newClaim={newClaim}). The stronghold is yours.");
            GameStateService.Instance?.Save();
            return newClaim;
        }

        // =====================================================================
        //  NEXT COMPANION — unlock the next canon companion into the party.
        // =====================================================================

        private string UnlockNextCompanion()
        {
            var svc = GameStateService.Instance;
            if (svc == null || svc.State == null)
            {
                FlowTrace.Warn("Raid", "next-companion: no GameStateService — cannot enrol a companion.");
                return null;
            }

            HeroClass player = svc.State.HeroClass.ToNullable() ?? HeroClass.Knight;
            HeroClass[] order = { HeroClass.Ranger, HeroClass.Cleric, HeroClass.Knight, HeroClass.Mage };
            foreach (var cls in order)
            {
                if (cls == player) continue;
                if (svc.IsInParty(cls.ToString())) continue;
                svc.AddToParty(cls.ToString());
                string name = CompanionDialogue.NameFor(cls);
                FlowTrace.Step("Raid", $"NEXT COMPANION — rescued {name} ({cls}); enrolled into the party.");
                return name;
            }

            FlowTrace.Step("Raid", "next-companion: party already complete — no new join.");
            return null;
        }

        // =====================================================================
        //  RETURN — victory banner + route home (never soft-lock).
        // =====================================================================

        // =====================================================================
        //  RETREAT — WO-550 anti-soft-lock: a player can ALWAYS bail a Village2 raid.
        //  A small, unobtrusive bottom-left button (its own ScreenSpaceOverlay canvas, NO scrim so
        //  it never blocks gameplay touch input) that routes home via the SAME SceneRouter.GoCastle
        //  path AutoReturnRoutine/ReturnHome use — but WITHOUT a claim (you abandoned, not cleared,
        //  so the base stays enemy-owned). No retreat cost is applied (none is trivial here; a cost
        //  is an owner design call — flagged in WO-550).
        // =====================================================================

        private void BuildRetreatButton()
        {
            try
            {
                if (_retreatUi != null) return;
                // Low sort order: above gameplay HUD but well below the victory banner (32000).
                _retreatUi = ElarionUiKit.BuildModalCanvas("Village2RetreatButton", 9000);
                // NO Scrim — a scrim would block all gameplay input; only the button itself is interactive.
                ElarionUiKit.Button(_retreatUi.transform, "Retreat", ElarionUiKit.ButtonKind.Danger,
                    new Vector2(0.03f, 0.03f), new Vector2(0.27f, 0.10f), Retreat);
                FlowTrace.Step("Raid", "Village2 Retreat button shown (anti-soft-lock: the raid is never one-way).");
            }
            catch (System.Exception e)
            {
                FlowTrace.Warn("Raid", "Village2 Retreat button build threw (raid still playable, AutoReturn covers a cleared raid): " + e.Message);
            }
        }

        private void Retreat()
        {
            if (_returning) return;
            _returning = true;
            FlowTrace.Step("Raid", "RETREAT — player abandoned the Village2 raid; routing home WITHOUT claim (base stays enemy-owned).");
            if (_retreatUi != null) { Destroy(_retreatUi); _retreatUi = null; }
            GameStateService.Instance?.Save();
            // Deliberately NO SceneOwnership.SetEnemyOwned(false): a retreat is not a claim.
            SceneRouter.GoCastle();
        }

        private void BuildVictoryBanner(string joinedCompanionName)
        {
            try
            {
                if (_ui != null) Destroy(_ui);
                _ui = ElarionUiKit.BuildModalCanvas("Village2VictoryBanner", 32000);
                ElarionUiKit.Scrim(_ui.transform, onTapClose: null);

                var panel = ElarionUiKit.Panel(_ui.transform, new Vector2(0.22f, 0.34f), new Vector2(0.78f, 0.70f), deep: true);

                ElarionUiKit.Header(panel.transform, "STRONGHOLD CLEARED", x0: 0.06f, x1: 0.94f, y0: 0.74f, y1: 0.93f);

                string body = joinedCompanionName != null
                    ? $"The enemy stronghold is CLAIMED — it is yours now.\n\n{joinedCompanionName} joins your party."
                    : "The enemy stronghold is CLAIMED — it is yours now.";
                ElarionUiKit.Label(panel.transform, body, 0.10f, 0.40f,
                    ElarionUi.Parchment, ElarionUi.FontBody, TMPro.TextAlignmentOptions.Center, 0.06f, 0.94f);

                ElarionUiKit.Button(panel.transform, "Return to Castle", ElarionUiKit.ButtonKind.Gold,
                    new Vector2(0.28f, 0.10f), new Vector2(0.72f, 0.28f), ReturnHome);

                // Register the top-band victory banner with the single-modal arbiter (battle-allowed
                // — a terminal win banner must always show). The back button / arbiter close routes
                // home via ReturnHome; isOpen tracks the live banner canvas.
                if (_panelHandle == null)
                    _panelHandle = PanelManager.RegisterBattleAllowed("Village2Victory", ReturnHome, () => _ui != null);
                PanelManager.NotifyOpened(_panelHandle);

                FlowTrace.Step("Raid", "RETURN — Village2 victory banner shown" +
                    (joinedCompanionName != null ? $" (+{joinedCompanionName})" : "") + "; tap or auto-return routes to the castle.");
            }
            catch (System.Exception e)
            {
                FlowTrace.Fail("Raid", "Village2 victory banner build threw — returning home directly: " + e.Message);
                ReturnHome();
            }
        }

        private IEnumerator AutoReturnRoutine()
        {
            float t = Mathf.Max(2f, _autoReturnSeconds);
            yield return new WaitForSeconds(t);
            if (!_returning)
            {
                FlowTrace.Step("Raid", "Village2 auto-return timer elapsed — routing home (anti-soft-lock).");
                ReturnHome();
            }
        }

        private void ReturnHome()
        {
            if (_returning) return;
            _returning = true;
            // Release the arbiter slot as the banner routes home (no-op if already released).
            if (_panelHandle != null) PanelManager.NotifyClosed(_panelHandle);
            FlowTrace.Step("Raid", "RETURN -> SceneRouter.GoCastle() (loop continues, no soft-lock).");
            GameStateService.Instance?.Save();
            SceneOwnership.SetEnemyOwned(false);
            SceneRouter.GoCastle();
        }
    }
}

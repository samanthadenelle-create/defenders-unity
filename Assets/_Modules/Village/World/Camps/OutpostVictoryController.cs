// =============================================================================
// OutpostVictoryController — WO-449. The MISSING subscriber that closes the
// CONTINUOUS-WALK raid loop in the OVERWORLD (no teleport, no deploy screen):
//   walk to an overworld EnemyOutpost -> CLEAR it -> CLAIM the base ->
//   grant the NEXT COMPANION -> KEEP WALKING (the hero never leaves the world).
// -----------------------------------------------------------------------------
// Assembly: DeNelle.Village   Namespace: DeNelle.Village.World.Camps
//
// THE GAP THIS CLOSES: RaidVictoryController already does claim->companion->return,
// but it binds to RaidGarrisonSpawner.OnCleared INSIDE the baked RaidBase_* teleport
// scenes — the DEAD path under the continuous-walk loop (WO-449). The live path is
// the EnemyOutpost objects RaidOutpostSystem spawns in the OuterWorld; their
// EnemyOutpost.OnCleared pays loot but never CLAIMS the base or grants the next
// companion. This controller is that missing half for the open-world outposts.
//
// IT MIRRORS RaidVictoryController's claim + companion steps EXACTLY (same
// RaidClaimService.MarkClaimed new-claim gate so re-clears never re-grant; the same
// GameStateService.AddToParty + CompanionDialogue.NameFor companion path; a
// lightweight ElarionUiKit victory toast) — but it binds to EnemyOutpost.OnCleared
// and DELIBERATELY does NOT route home: a forced GoCastle would re-introduce the very
// teleport WO-449 removes. The hero stays in the open world and keeps walking.
//
// SELF-INSTALL: a RuntimeInitialize hook + sceneLoaded adds ONE controller per
// hub/OuterWorld load (idempotent), gated on FeatureFlags.RaidContinuousWalk. The
// outposts realize ~10s after the world loads (RaidOutpostSystem.SpawnDelaySeconds),
// so we POLL/re-subscribe across a few seconds (mirroring RaidVictoryController's
// BindRoutine shape) to subscribe to EVERY RaidOutpostSystem.Outposts entry's
// OnCleared — and ONLY those (never an Arena-configured outpost).
//
// Code-built uGUI via the shared ElarionUiKit (NO UXML — repo rule). Guard.Try +
// FlowTrace around the subscribe + claim (§12 no silent failures). ASCII-only
// runtime strings. Canon: the village is Elarion (never Avalon).
// =============================================================================

using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using DeNelle.Core;
using DeNelle.Core.State;
using DeNelle.Core.UI;
using DeNelle.Core.Diagnostics;
using DeNelle.Village.UI;

namespace DeNelle.Village.World.Camps
{
    /// <summary>
    /// Subscribes to every continuous-walk <see cref="EnemyOutpost.OnCleared"/> in the
    /// OuterWorld and runs the open-world victory flow: CLAIM the base
    /// (<see cref="RaidClaimService.MarkClaimed"/>) and, on a NEW claim, grant the next
    /// companion (<see cref="GameStateService.AddToParty"/>) + show a victory toast — WITHOUT
    /// any teleport/return (the hero stays in the open world). Self-installs once per
    /// hub/OuterWorld load when <see cref="FeatureFlags.RaidContinuousWalk"/> is ON.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class OutpostVictoryController : MonoBehaviour
    {

        // Poll window for the delayed outpost realize (RaidOutpostSystem delays ~10s); we
        // re-scan a touch longer so a late realize still gets subscribed.
        private const float BindPollSeconds   = 16f;
        private const float BindPollInterval  = 1f;

        // Each EnemyOutpost we've already wired (so a re-scan never double-subscribes).
        private readonly System.Collections.Generic.HashSet<EnemyOutpost> _bound =
            new System.Collections.Generic.HashSet<EnemyOutpost>();

        // =====================================================================
        //  Self-install — one controller per hub/OuterWorld load
        // =====================================================================

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void InstallHook()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
            TryInstall(SceneManager.GetActiveScene());
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode) => TryInstall(scene);

        private static void TryInstall(Scene scene)
        {
            Guard.Try("Raid", "OutpostVictoryController.TryInstall", () =>
            {
                // Continuous-walk loop only — when OFF the legacy RaidVictoryController
                // (teleport scenes) owns victory, so this open-world subscriber stays dormant.
                if (!FeatureFlags.RaidContinuousWalk) return;
                if (!scene.IsValid()) return;

                // Gate: the active scene is a home HUB (overworld streams in additively over it),
                // OR the overworld scene is loaded. Either way the walk-to outposts can exist.
                if (!HubScenes.IsHub(SceneManager.GetActiveScene().name) && !IsOverworldLoaded())
                    return;

                if (FindAnyObjectByType<OutpostVictoryController>() != null) return;

                var go = new GameObject("OutpostVictoryController");
                Object.DontDestroyOnLoad(go);
                go.AddComponent<OutpostVictoryController>();
                FlowTrace.Step("Raid", $"OutpostVictoryController self-installed (continuous-walk; trigger scene '{scene.name}').");
            });
        }

        private static bool IsOverworldLoaded()
        {
            int count = SceneManager.sceneCount;
            for (int i = 0; i < count; i++)
            {
                var s = SceneManager.GetSceneAt(i);
                // WO-608 merge: HubScenes.IsOverworld matches legacy "OuterWorld" AND the
                // merged "Main_Castle_Overworld" (correctness; the IsHub gate above already
                // covers the merged scene, so this is a belt-and-braces repoint).
                if (s.isLoaded && HubScenes.IsOverworld(s.name))
                    return true;
            }
            return false;
        }

        // =====================================================================
        //  Bind to every RaidOutpostSystem outpost (they realize ~10s after load,
        //  so poll/re-scan for a few seconds — mirror RaidVictoryController.BindRoutine).
        // =====================================================================

        private void Start() => StartCoroutine(BindRoutine());

        private void OnDestroy()
        {
            foreach (var o in _bound)
                if (o != null) o.OnCleared -= HandleCleared;
            _bound.Clear();
        }

        private IEnumerator BindRoutine()
        {
            float t0 = Time.realtimeSinceStartup;
            int subscribed = 0;
            while (Time.realtimeSinceStartup - t0 < BindPollSeconds)
            {
                subscribed += SweepAndSubscribe();
                yield return new WaitForSecondsRealtime(BindPollInterval);
            }

            // One final sweep so a realize that landed just inside the last interval is caught.
            subscribed += SweepAndSubscribe();
            FlowTrace.Step("Raid", $"OutpostVictoryController bound to {subscribed} outpost(s) OnCleared (continuous-walk victory armed).");
        }

        // Subscribe to any not-yet-bound RaidOutpostSystem outpost. ONLY the
        // RaidOutpostSystem outposts (NOT Arena-configured ones — the Arena owns its own
        // outpost lifetime + result; an Arena outpost is never in RaidOutpostSystem.Outposts).
        // Returns how many NEW subscriptions were made this sweep.
        private int SweepAndSubscribe()
        {
            int added = 0;
            Guard.Try("Raid", "OutpostVictoryController.SweepAndSubscribe", () =>
            {
                var outposts = RaidOutpostSystem.Outposts;   // clone; entries null until realized
                if (outposts == null) return;
                for (int i = 0; i < outposts.Length; i++)
                {
                    var o = outposts[i];
                    if (o == null) continue;            // not realized yet
                    if (_bound.Contains(o)) continue;   // already wired
                    o.OnCleared -= HandleCleared;
                    o.OnCleared += HandleCleared;
                    _bound.Add(o);
                    added++;
                    FlowTrace.Step("Raid", $"OutpostVictoryController subscribed to outpost '{o.OutpostId}' OnCleared.");
                }
            });
            return added;
        }

        // =====================================================================
        //  VICTORY — an OuterWorld outpost was cleared (its whole garrison died).
        //  CLAIM + (on a NEW claim) NEXT COMPANION + toast. NO teleport/return.
        // =====================================================================

        private void HandleCleared(EnemyOutpost outpost)
        {
            if (outpost == null) return;
            Guard.Try("Raid", "OutpostVictoryController.HandleCleared", () =>
            {
                string configId = outpost.OutpostId;
                FlowTrace.Step("Raid", $"VICTORY (continuous-walk) — outpost '{configId}' cleared. Running claim -> next-companion (no return).");

                // Victory fanfare (clean cross-module call; null-safe).
                CoreServices.Audio?.PlayMusic(DeNelle.Core.Audio.MusicTrack.Victory);

                // STEP — CLAIM. Persist the win. The new-claim signal gates the one-time
                // payoff (identical to RaidVictoryController.cs:171-174) so a re-clear of the
                // same outpost never re-grants a companion.
                bool newClaim = RaidClaimService.MarkClaimed(configId);

                // STEP — NEXT COMPANION (NEW claim only). Same path RaidVictoryController uses,
                // including its WO-1761 SINGLE-HERO gate: the recruit itself is dropped (not just
                // the toast line) because every companion consumer already hides them, so the
                // enrolment would persist a party member the player can never see or use.
                // ff.singlehero=0 restores the join exactly as before.
                string joined = null;
                if (newClaim && FeatureFlags.SingleHero)
                    FlowTrace.Step("Raid", "NEXT COMPANION SKIPPED — SingleHero is ON, so this outpost " +
                        "claim recruits nobody and the toast carries no join line (WO-1761).");
                else if (newClaim)
                    joined = UnlockNextCompanion();

                // Persist immediately so the claim/companion survive an app close.
                GameStateService.Instance?.Save();

                // STEP — TOAST. Lightweight victory banner; the hero KEEPS WALKING (no GoCastle,
                // no "Return to Castle" — a forced return is the teleport WO-449 removed).
                ShowVictoryToast(configId, joined, newClaim);
            });
        }

        /// <summary>
        /// Adds the NEXT canon companion to the persisted party — the same stable order +
        /// calls RaidVictoryController.UnlockNextCompanion uses. Returns the joined
        /// companion's display name (for the toast), or null if the party is already full.
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

            // Every class EXCEPT the player's own, in the same canon order as
            // RaidVictoryController; add the first not-yet-recruited one.
            HeroClass[] order = { HeroClass.Ranger, HeroClass.Cleric, HeroClass.Knight, HeroClass.Mage };
            foreach (var cls in order)
            {
                if (cls == player) continue;
                if (svc.IsInParty(cls.ToString())) continue;
                svc.AddToParty(cls.ToString());   // fires PlayerChanged -> StoryCompanionInjector spawns the body + Save
                string name = CompanionDialogue.NameFor(cls);
                FlowTrace.Step("Raid", $"NEXT COMPANION — rescued {name} ({cls}); enrolled into the party.");
                return name;
            }

            FlowTrace.Step("Raid", "next-companion: party already complete (all three companions recruited) — no new join.");
            return null;
        }

        // A lightweight, auto-dismissing victory toast — the hero is still in the open
        // world and keeps moving. Routes through the ONE shared Obsidian EndState template
        // in COMPACT mode (no scrim/backdrop, non-blocking); the template owns the
        // auto-dismiss (AutoDismissSeconds) + teardown, so the hero just keeps walking.
        // A build failure must never break the flow (Guard.Try).
        private void ShowVictoryToast(string configId, string joinedCompanionName, bool newClaim)
        {
            Guard.Try("Raid", "OutpostVictoryController.ShowVictoryToast", () =>
            {
                EndStateView.Show(EndStateVM.FromOutpostVictory(joinedCompanionName, newClaim));

                FlowTrace.Step("Raid", $"TOAST — outpost '{configId}' victory shown " +
                    (joinedCompanionName != null ? $"(+{joinedCompanionName})" : "(party full / re-claim)") +
                    "; hero stays in the open world (no return).");
            });
        }
    }
}

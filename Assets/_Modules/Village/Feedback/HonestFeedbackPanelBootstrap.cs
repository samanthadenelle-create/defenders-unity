// =============================================================================
// HonestFeedbackPanelBootstrap (WO-1432) - installs the one offer gate and the
// one panel host. Mirrors BenefactorsWallPanelBootstrap exactly.
// -----------------------------------------------------------------------------
// Assembly: DeNelle.Village   Namespace: DeNelle.Village.Feedback
//
// The panel registers PanelId.HonestFeedback in its own Awake, so the offer gate
// has something to open the moment the hub is up. The gate (HonestFeedbackService)
// is installed alongside it, because a panel with no gate is a screen nothing ever
// asks for and a gate with no panel is a trace line about a missing host.
//
// This file is HonestFeedbackPanel's D2 door for PanelDoorRegression: it is in the
// panel's home set AND carries [RuntimeInitializeOnLoadMethod], the shape
// HeartPanelBootstrap.Install established. The STRONG door is D1 -
// HonestFeedbackService.TryOpenOffer, which is outside the home set. Both exist on
// purpose; do not delete either and assume the other covers it.
//
// GATED TO HUB SCENES. The offer is a town moment. Spawning it in a dungeon or a
// raid would put a one-time modal in front of a player mid-fight, and the raid
// target Village2 counts as a hub by name but is enemy-owned (WO-550), where the
// town HUD stands down and so does this.
//
// ⛔ NO HOTKEY, NO ACTION-BAR FACE, NO MENU ITEM. The offer decides its own moment
// (HonestFeedbackService.IsEligible) and shows itself once. CLAUDE.md section 7
// caps the calm(town) bar and spends paragraphs on why; a one-time panel is the
// last thing that should take a slot.
//
// ASCII only. Instrumentation: FlowTrace tag "HonestFeedback". Never strip it.
// =============================================================================

using DeNelle.Core.Diagnostics;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DeNelle.Village.Feedback
{
    public static class HonestFeedbackPanelBootstrap
    {
        private const string Sys = HonestFeedbackGrant.Sys;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        public static void EnsureFirst()
        {
            SpawnInScene(SceneManager.GetActiveScene());
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode) => SpawnInScene(scene);

        private static void SpawnInScene(Scene scene)
        {
            if (!scene.IsValid()) return;
            if (!DeNelle.Core.HubScenes.IsHub(scene.name)) return;

            if (DeNelle.Core.HubScenes.SuppressTownHud(SceneManager.GetActiveScene().name))
            {
                FlowTrace.Warn(Sys, "honest-feedback offer suppressed in an enemy-owned scene (WO-550 rule).");
                return;
            }

            // The Settings door is permanent. Even after the one-time automatic offer stands
            // down, keep both the panel and submit service alive so players can write again.

            // GLOBAL dedupe across ALL loaded scenes - the HelpMenuBootstrap rule.
            var existingPanel = Object.FindFirstObjectByType<HonestFeedbackPanel>(FindObjectsInactive.Include);
            var existingGate = Object.FindFirstObjectByType<HonestFeedbackService>(FindObjectsInactive.Include);
            if (existingPanel != null && existingGate != null)
            {
                FlowTrace.Step(Sys, "duplicate honest-feedback install suppressed (host + gate already exist).");
                return;
            }

            // Title / HeroSelect carry no hero; there is no town moment to interrupt there.
            if (Object.FindAnyObjectByType<HeroLocomotion>() == null) return;

            if (existingPanel == null)
            {
                var panelGo = new GameObject("HonestFeedbackPanel");
                SceneManager.MoveGameObjectToScene(panelGo, scene);
                panelGo.AddComponent<HonestFeedbackPanel>();
                FlowTrace.Step(Sys, "HonestFeedbackPanel host created (single instance).");
            }

            if (existingGate == null)
            {
                var gateGo = new GameObject("HonestFeedbackService");
                SceneManager.MoveGameObjectToScene(gateGo, scene);
                gateGo.AddComponent<HonestFeedbackService>();
                FlowTrace.Step(Sys, "HonestFeedbackService offer gate created (single instance).");
            }
        }
    }
}

// =============================================================================
// VigilCeremonyPanelBootstrap - spawns exactly one VigilCeremonyPanel (WO-1874, D2).
// -----------------------------------------------------------------------------
// ClanFeatureGate.PlayerFacingEnabled is the LITERAL FIRST statement, before any
// GameObject exists (CircleScreenPanelBootstrap / ClanFeatureGateRegression).
// Raid scenes skip. Hub / overworld then Consider() the ceremony.
// =============================================================================

using UnityEngine;
using UnityEngine.SceneManagement;
using DeNelle.Core;
using DeNelle.Core.Diagnostics;
using DeNelle.Core.Services;

namespace DeNelle.HUD
{
    public static class VigilCeremonyPanelBootstrap
    {
        [RuntimeInitializeOnLoadMethod]
        public static void EnsureFirst()
        {
            SpawnInScene(SceneManager.GetActiveScene());
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
            => SpawnInScene(scene);

        private static void SpawnInScene(Scene scene)
        {
            if (!ClanFeatureGate.PlayerFacingEnabled) return;
            if (!scene.IsValid()) return;

            string active = SceneManager.GetActiveScene().name;
            if (HubScenes.IsRaid(active) || HubScenes.SuppressTownHud(active))
            {
                FlowTrace.Warn("VigilCeremony", "VigilCeremonyPanel suppressed in an enemy-owned scene");
                return;
            }

            foreach (var existing in UnityEngine.Object.FindObjectsByType<VigilCeremonyPanel>(
                         FindObjectsInactive.Include))
            {
                if (existing != null)
                {
                    ConsiderOnHub(existing);
                    return;
                }
            }

            if (FindHero() == null) return;

            var go = new GameObject("VigilCeremonyPanel");
            SceneManager.MoveGameObjectToScene(go, scene);
            var panel = go.AddComponent<VigilCeremonyPanel>();
            FlowTrace.Step("VigilCeremony", "VigilCeremonyPanel created (single instance, code-built kit modal)");
            ConsiderOnHub(panel);
        }

        private static void ConsiderOnHub(VigilCeremonyPanel panel)
        {
            if (panel == null) return;
            string active = SceneManager.GetActiveScene().name;
            if (!HubScenes.IsHub(active) && !HubScenes.IsOverworld(active)) return;
            panel.Consider();
        }

        private static Transform FindHero()
        {
            var t = System.Type.GetType("DeNelle.Village.HeroLocomotion, DeNelle.Village");
            if (t == null) return null;
            var obj = UnityEngine.Object.FindAnyObjectByType(t) as Component;
            return obj != null ? obj.transform : null;
        }
    }
}

// =============================================================================
// HomesSwitcherPanelBootstrap - WO-1884 D2 door for HomesSwitcherPanel.
// -----------------------------------------------------------------------------
// Spawns exactly one HomesSwitcherPanel on hub + owned-town scenes so
// PanelId.Homes is registered before the HUD chip taps it. Enemy-owned / raid
// scenes skip. PanelDoorRegression D2: RuntimeInitializeOnLoadMethod + AddComponent.
// =============================================================================

using DeNelle.Core;
using DeNelle.Core.Diagnostics;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DeNelle.HUD
{
    public static class HomesSwitcherPanelBootstrap
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
            if (!scene.IsValid()) return;

            string active = SceneManager.GetActiveScene().name;
            bool homeScene = HubScenes.IsHub(active) || HubScenes.IsOwnedTown(active);
            if (!homeScene || HubScenes.SuppressTownHud(active) || HubScenes.IsRaid(active))
            {
                return;
            }

            foreach (var existing in Object.FindObjectsByType<HomesSwitcherPanel>(
                         FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (existing != null)
                {
                    FlowTrace.Warn("Homes", "duplicate HomesSwitcherPanel suppressed (one already exists)");
                    return;
                }
            }

            if (FindHero() == null) return;

            var go = new GameObject("HomesSwitcherPanel");
            SceneManager.MoveGameObjectToScene(go, scene);
            go.AddComponent<HomesSwitcherPanel>();
            FlowTrace.Step("Homes", "HomesSwitcherPanel created (single instance, WO-1884)");
        }

        // Reflection on purpose: DeNelle.HUD must never reference DeNelle.Village.
        private static Transform FindHero()
        {
            var t = System.Type.GetType("DeNelle.Village.HeroLocomotion, DeNelle.Village");
            if (t == null) return null;
            var obj = Object.FindAnyObjectByType(t) as Component;
            return obj != null ? obj.transform : null;
        }
    }
}

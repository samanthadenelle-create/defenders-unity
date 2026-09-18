// =============================================================================
// CircleScreenPanelBootstrap - spawns exactly one CircleScreenPanel per gameplay
// scene (WO-1870 Lane B, door 2).
// -----------------------------------------------------------------------------
// This is the PanelDoorRegression D2 root: a MonoBehaviour under Assets/_Modules/
// whose type name ends in "Panel" needs a door outside its own View/VM/Bootstrap
// loop, and a [RuntimeInitializeOnLoadMethod] bootstrap is one of the two sanctioned
// shapes (ManageScreenBootstrap.cs:27 / ClanChatPanelBootstrap.cs:21). The other,
// D1, is ClanChatPanel naming PanelRouter.Open(PanelId.Circle).
//
// ⛔ THE FEATURE-GATE CHECK IS THE LITERAL FIRST STATEMENT, BEFORE ANY GameObject
// EXISTS. ClanChatPanelBootstrap.cs:34 holds that exact ordering and
// ClanFeatureGateRegression.cs:26,31-33 pins it: with the gate off, a panel that had
// already been constructed is a shipped surface nobody asked for.
//
// ⚠ The attribute is written BARE - [RuntimeInitializeOnLoadMethod] - not with the
// RuntimeInitializeLoadType.AfterSceneLoad argument the chat bootstrap spells out.
// AfterSceneLoad IS the attribute's default, so the behaviour is identical, and the
// bare form is the literal [circle-door] looks for.
// =============================================================================

using UnityEngine;
using UnityEngine.SceneManagement;
using DeNelle.Core.Diagnostics;
using DeNelle.Core.Services;

namespace DeNelle.HUD
{
    public static class CircleScreenPanelBootstrap
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

            // WO-550: social surfaces do NOT bootstrap in enemy-owned RAID scenes; the home hub is
            // unaffected. Gate on the ACTIVE scene, which is the player's context.
            if (DeNelle.Core.HubScenes.SuppressTownHud(SceneManager.GetActiveScene().name))
            {
                FlowTrace.Warn("Circle", "CircleScreenPanel suppressed in an enemy-owned scene (WO-550)");
                return;
            }

            // GLOBAL dedupe across every loaded scene - the HelpMenuBootstrap shape.
            foreach (var existing in UnityEngine.Object.FindObjectsByType<CircleScreenPanel>(
                         FindObjectsInactive.Include))
            {
                if (existing != null)
                {
                    FlowTrace.Warn("Circle", "duplicate CircleScreenPanel suppressed (one already exists)");
                    return;
                }
            }

            if (FindHero() == null) return;   // Title / HeroSelect skip.

            var go = new GameObject("CircleScreenPanel");
            SceneManager.MoveGameObjectToScene(go, scene);
            go.AddComponent<CircleScreenPanel>();
            FlowTrace.Step("Circle", "CircleScreenPanel created (single instance, code-built kit modal)");
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

// =============================================================================
// BattleArenaHud — the WO-482 battle overlay VIEW, P23-slimmed (HUD_OBSIDIAN A2).
// -----------------------------------------------------------------------------
// Assembly: DeNelle.Village   Namespace: DeNelle.Village.Arena
//
// Logic/presentation split (HP-B2B law): BattleArena (logic) pushes state in and
// wires the Flee handler; the view never reads game state.
//
// P23 DEMOLITION NOTES:
//   • The in-file Flee button (raw AddPanel/AddText + tap-to-confirm) is GONE —
//     FLEE now lives in the HUD kit's system area (hostile(activebattle) row):
//     SetFleeHandler forwards BattleArena's handler into the Core command sink
//     (HudCommands.RegisterFlee); the kit renders the red Obsidian Flee button.
//   • The engage INTRO CARD converts to the factory's shared ToastCard (§1.5) —
//     zero raw widget construction remains in this file.
//   • The legacy 9-zone spawn stays as the retired shim call (BattleHud9Zone.
//     Create() registers the default flee handler + returns null).
//   • RESULT still routes through the ONE shared Obsidian end-state template
//     (EndStateView), which now also drives hostile(postbattle) via
//     PostureSignals.SetEndState — the HUD kit stands down while it is up.
// =============================================================================

using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using DeNelle.Core;
using DeNelle.Core.HUD;
using DeNelle.Core.UI;
using DeNelle.Village.UI;   // EndStateVM/EndStateView — the ONE shared end-state template

namespace DeNelle.Village.Arena
{
    /// <summary>Battle overlay: intro card + result routing; Flee rides the HUD kit (see header).</summary>
    public sealed class BattleArenaHud : MonoBehaviour
    {
        private Canvas _canvas;
        private BattleHud9Zone _hud9;   // retired shim (Create returns null; kept for teardown shape)

        /// <summary>Build the overlay canvas (and an EventSystem if none exists) and return it.</summary>
        public static BattleArenaHud Create()
        {
            var go = new GameObject("BattleArenaHud");
            DontDestroyOnLoad(go);
            var hud = go.AddComponent<BattleArenaHud>();
            hud.Build();
            hud._hud9 = BattleHud9Zone.Create();   // retired shim: registers default flee, returns null
            return hud;
        }

        /// <summary>Forward BattleArena's flee handler into the Core sink — the HUD kit's
        /// system-area Flee button fires it (P23: no in-file flee button).</summary>
        public void SetFleeHandler(Action onFlee) => HudCommands.RegisterFlee(onFlee);

        /// <summary>
        /// ENGAGE INTRO CARD (encounter feedback): a brief centre toast naming the engaged
        /// foe ("Orc Warband - Battle!") — the factory's shared ToastCard (§1.5), no bespoke
        /// chrome. Self-destructs after <paramref name="seconds"/>.
        /// </summary>
        public void ShowIntro(string foeLabel, float seconds = 1.6f)
        {
            if (_canvas == null) return;
            var parts = ElarionUiKit.ToastCard(_canvas.transform, ElarionUiKit.ToastTone.Danger,
                                               accentLeft: false, align: TextAnchor.MiddleCenter);
            var rt = (RectTransform)parts.card.transform;
            rt.anchorMin = new Vector2(0.30f, 0.60f);
            rt.anchorMax = new Vector2(0.70f, 0.70f);
            rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
            parts.label.text = string.IsNullOrEmpty(foeLabel) ? LocalText.Get("arena.battle_hud.start") : foeLabel;
            parts.label.fontSize = 30;
            Destroy(parts.card, Mathf.Max(0.2f, seconds));
        }

        /// <summary>
        /// Route the battle result through the ONE shared Obsidian end-state template
        /// (EndStateView). WIN -> victory end-state (stars/time/spoils, ONE Continue);
        /// LOSS -> the brief defeat sting. All numbers pushed via the VM.
        /// NOTE(perfect-tier): no flawless signal is tracked yet — 'perfect' defaults false.
        /// </summary>
        public void ShowResult(bool won, int stars, float durationSeconds,
                               BattleRewardSummary rewards, Action onContinue, float autoTimeoutSeconds = 20f,
                               bool perfect = false,
                               // WO-969: the arena's HAND-BACK. onContinue is the player's CHOICE;
                               // onAbandon is the same transition re-claimed by the arena the instant
                               // this screen is destroyed without that choice being made. Passing both
                               // is what stops a Pause (or any modal) from eating the route home.
                               Action onAbandon = null)
        {
            var vm = won
                // WO-1104: gold + kill count now ride along so the victory screen shows the
                // coin the fight actually banked and how many bodies earned it.
                ? EndStateVM.FromBattleVictory(stars, durationSeconds,
                      rewards.Xp, rewards.Wisdom, rewards.Wood, rewards.Iron, rewards.GearName,
                      onContinue, autoTimeoutSeconds, perfect, primaryRoute: null, onAbandon: onAbandon,
                      gold: rewards.Gold, kills: rewards.Kills)
                : EndStateVM.FromBattleDefeat();
            EndStateView.Show(vm);

            // The end-state lives on its own canvas; this battle overlay is done.
            Close();
        }

        /// <summary>Tear the overlay down and clear the battle-scoped flee handler.</summary>
        public void Close()
        {
            HudCommands.RegisterFlee(null);
            if (_hud9 != null) { _hud9.Close(); _hud9 = null; }
            if (this != null && gameObject != null) Destroy(gameObject);
        }

        // ── build (canvas scaffolding only — widgets come from the factory) ────
        private void Build()
        {
            EnsureEventSystem();
            _canvas = gameObject.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 5000;  // above the gameplay HUD kit (4000)
            var scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080f, 1920f);
            gameObject.AddComponent<GraphicRaycaster>();
        }

        private static void EnsureEventSystem()
        {
            if (FindAnyObjectByType<EventSystem>() == null)
            {
                var es = new GameObject("EventSystem");
                es.AddComponent<EventSystem>();
                es.AddComponent<StandaloneInputModule>();
                DontDestroyOnLoad(es);
            }
        }
    }
}

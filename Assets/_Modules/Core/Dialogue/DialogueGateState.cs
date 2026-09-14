// =============================================================================
// DialogueGateState — Core cross-module seam for "is a LIVE dialogue actually on
// screen, and if not, WHY not?" (WO-1714).
// -----------------------------------------------------------------------------
// WHY THIS EXISTS (proven, not guessed — WO-1714 RCA, device capture
// docs/handoffs/movement_freeze_logcat_2026-09-14.txt):
//
//   HeroLocomotion.InputSuppressed is raised by DialogueService.Started and
//   cleared ONLY by DialogueService.Ended. DialogueView (HUD) has three "truces"
//   that HIDE a live dialogue's panel WITHOUT closing it — closing would fire
//   Ended and falsely complete a dialogue-gated tutorial step. Each of the three
//   logs "Ended NOT fired" by design.
//
//   Consequence: for the whole duration of a truce the hero is frozen, mute and
//   defenceless with NOTHING ON SCREEN to explain it. The capture proves a 35.5 s
//   freeze, 11.3 s of which was mid-wave (wave=True battleLock=True modal=False)
//   with an invisible panel — a genuine, shipping, player-facing combat softlock.
//
//   The BUILDER truce already learned this lesson and got a bypass seam:
//   BuildModeState.DialogueHiddenForBuilder (BuildModeState.cs:38), consumed at
//   BuildModeController.cs:772. The COMBAT and WO-795 MODAL truces never got one,
//   and nothing anywhere restored locomotion. This file is that missing seam,
//   generalised so a FOURTH truce cannot reproduce the same class of freeze.
//
// ASMDEF LAW (CLAUDE.md §5): DeNelle.HUD never references DeNelle.Village and
// vice versa. So — exactly like BuildModeState — the flag lives in Core:
//   * HUD (DialogueView.Update) WRITES all three fields, truthfully, EVERY FRAME.
//   * Village (HeroLocomotion) READS them to release / bound the input gate.
// Neither side ever writes the other's.
//
// PER-FRAME, NOT ON TRANSITION — deliberately. TickBuilderTruce's own comment
// says why: "Publishes DialogueHiddenForBuilder truthfully every frame
// (self-healing: a dialogue superseded/closed while hidden clears it)". A
// transition-only publish leaves a stale TRUE behind whenever the view is torn
// down mid-truce, which is the very bug class this seam exists to close.
// =============================================================================

namespace DeNelle.Core.Dialogue
{
    /// <summary>
    /// WO-1714 — cross-assembly visibility state for the dialogue panel. HUD writes,
    /// Village reads. See the file header for the captured defect this closes.
    /// </summary>
    public static class DialogueGateState
    {
        /// <summary>TRUE while the dialogue view is holding a LIVE (open, un-ended)
        /// dialogue hidden because combat owns the screen (DialogueView.TickCombatTruce /
        /// OnArbiterClose). Written by HUD only.</summary>
        public static bool HiddenForCombat { get; set; }

        /// <summary>TRUE while the dialogue view is holding a LIVE (open, un-ended)
        /// dialogue hidden because ANOTHER arbiter-tracked modal owns the screen
        /// (WO-795 truce, DialogueView.TickModalTruce). Written by HUD only.</summary>
        public static bool HiddenForModal { get; set; }

        /// <summary>TRUE while a dialogue panel is actually BUILT AND VISIBLE — the
        /// value of DialogueView.IsShowing (DialogueView.cs:106). Written by HUD only.
        /// This is the signal the locomotion watchdog trusts: suppression with a
        /// VISIBLE panel is a player reading a line and may last as long as she likes;
        /// suppression with NO panel and no known truce is the stuck state.</summary>
        public static bool PanelVisible { get; set; }

        /// <summary>TRUE while a live dialogue is hidden by one of the two truces this
        /// seam covers. Village's input gate releases immediately on this: a hidden
        /// dialogue cannot be mis-clicked, so there is nothing left to suppress — the
        /// same law BuildModeController.cs:772 already applies for the builder truce.
        /// The BUILDER truce is deliberately NOT folded in here: it has its own seam,
        /// its own consumer and its own shipped behaviour, and widening it would be an
        /// out-of-scope behaviour change (WO-1714 scope note).</summary>
        public static bool HiddenByTruce => HiddenForCombat || HiddenForModal;

        /// <summary>
        /// Human-readable state for the FlowTrace.Fail the locomotion watchdog emits.
        /// Pure — takes every flag explicitly so the regression can assert the wording
        /// without a PlayMode session (same shape as HeroLocomotion.TeleportGuardHeld).
        /// </summary>
        public static string DescribeGate(bool hiddenForCombat, bool hiddenForModal,
                                          bool hiddenForBuilder, bool panelVisible)
            => "hiddenForCombat=" + hiddenForCombat +
               " hiddenForModal=" + hiddenForModal +
               " hiddenForBuilder=" + hiddenForBuilder +
               " panelVisible=" + panelVisible;

        /// <summary>Clear every flag. Called by HUD on view disable/destroy so a torn-down
        /// view can never leave a stale truce latched.</summary>
        public static void Clear()
        {
            HiddenForCombat = false;
            HiddenForModal = false;
            PanelVisible = false;
        }

        // Domain reload disabled -> statics persist between Play sessions; reset at
        // subsystem registration so a session can never inherit a stale truce.
        // (Identical guard to BuildModeState.ResetStatics, BuildModeState.cs:44-50.)
        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Clear();
    }
}

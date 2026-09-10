// =============================================================================
// HeartboundEventInbox - the ONE seam between "the backend decided an Echo Event"
// and "the settlement felt it". Holds the pending event, owns once-only, and
// dispatches to exactly one registered applier.
// -----------------------------------------------------------------------------
// Assembly: DeNelle.Core   Namespace: DeNelle.Core.Heartbound
// WO-1678 (HEART-005).
//
// ⛔ WHY THIS EXISTS AT ALL, RATHER THAN THE CLIENT CALLING THE GAME DIRECTLY.
// The parser lives in DeNelle.Wallet, whose asmdef references Core / Commerce /
// Data and NOT DeNelle.Village - so the file that reads the server's answer
// structurally cannot touch inventory, Heartfire or the raid screen. That is the
// CoreServices.Hud shape and it is deliberate: an event arrives as DATA in Core,
// and the one module that owns the game objects registers itself as the applier.
// Presentation and application are separate layers that never reach across.
//
// ⛔ ONCE-ONLY IS TWO GUARDS AND ONLY ONE OF THEM IS AUTHORITATIVE. SAY SO.
//   1. THE SERVER's claim ledger (WO-1677 D2's primary key) is the real one.
//   2. The PlayerPrefs guard below is a LOCAL guard against the client applying
//      the same event twice in one install - a re-open of the screen, a second
//      status poll carrying the same pulse, a scene reload.
// It is NOT an anti-cheat control and must never be described as one: the same
// honest posture api/game/save.js:70-72 takes for soft currency. A wiped install
// re-applies; the server is what stops a second PAYMENT.
//
// ⚠ AND THERE IS NO SAVE FIELD. PlayerPrefs, not SaveSchema - the save blob is
// CLIENT-AUTHORED, so a claim recorded in it could be rewritten by a replayed
// blob, which is exactly the invariant the backend table exists to hold.
//
// #if DAPP_STORE: Seeker-only by owner ruling 2026-09-10 13:10.
// =============================================================================

#if DAPP_STORE

using System;
using DeNelle.Core.Diagnostics;
using UnityEngine;

namespace DeNelle.Core.Heartbound
{
    /// <summary>
    /// Applies one Echo Event to the running settlement. Implemented ONCE, by the
    /// module that owns the game objects (DeNelle.Village).
    /// </summary>
    public interface IHeartboundEventApplier
    {
        /// <summary>
        /// Apply <paramref name="evt"/>. Return false when nothing was applied, and
        /// FlowTrace the reason - a silent false is the failure §12 forbids.
        /// </summary>
        bool Apply(HeartboundEchoEvent evt);
    }

    /// <summary>
    /// The pending Echo Event and its dispatch. Static, because there is exactly one
    /// player and exactly one pulse in flight; a second instance would be a second
    /// authority on whether an event was already applied.
    /// </summary>
    public static class HeartboundEventInbox
    {
        /// <summary>FlowTrace system tag for the whole Heartbound event lane.</summary>
        public const string Sys = "HeartboundEvent";

        private const string ClaimPrefPrefix = "heartbound.claimed.";

        private static IHeartboundEventApplier _applier;
        private static HeartboundEchoEvent _pending;
        private static bool _hasPending;

        /// <summary>Raised when an event lands, for a reveal surface to paint. May be null.</summary>
        public static event Action<HeartboundEchoEvent> Arrived;

        /// <summary>Raised after an event was applied, so a surface can retire its card.</summary>
        public static event Action<HeartboundEchoEvent> Applied;

        /// <summary>The event waiting to be revealed, if any.</summary>
        public static bool TryPeek(out HeartboundEchoEvent evt)
        {
            evt = _pending;
            return _hasPending;
        }

        /// <summary>
        /// Register the one applier. A SECOND registration REPLACES the first and warns:
        /// two appliers would double-grant every event, and the repo's own scar tissue
        /// (one appearance owner, one grant authority, one committer) says the failure
        /// mode to design against is a second owner appearing quietly.
        /// </summary>
        public static void RegisterApplier(IHeartboundEventApplier applier)
        {
            if (applier == null)
            {
                FlowTrace.Warn(Sys, "RegisterApplier(null) ignored - the inbox keeps whatever it had. " +
                                    "An event with no applier is held, never dropped.");
                return;
            }

            if (_applier != null && !ReferenceEquals(_applier, applier))
            {
                FlowTrace.Warn(Sys, "a SECOND Echo Event applier registered (" + applier.GetType().Name +
                                    " replacing " + _applier.GetType().Name + "). There is exactly ONE " +
                                    "applier by design - two would grant every event twice.");
            }

            _applier = applier;
            FlowTrace.Step(Sys, "applier registered: " + applier.GetType().Name +
                                ". Pending event waiting = " + _hasPending + ".");

            if (_hasPending) TryApplyPending();
        }

        /// <summary>
        /// Accept the server's answer. `hasEvent=false` CLEARS the pending event rather
        /// than leaving the previous pulse's card on screen forever.
        /// </summary>
        public static void AcceptServerEvent(HeartboundEchoEvent evt)
        {
            if (!evt.HasEvent)
            {
                if (_hasPending)
                {
                    FlowTrace.Step(Sys, "the server reports no Echo Event this pulse - clearing the " +
                                        "pending card. No event is a NORMAL answer, not a failure.");
                }
                _pending = HeartboundEchoEvent.Empty;
                _hasPending = false;
                return;
            }

            if (IsClaimed(evt.ClaimId))
            {
                FlowTrace.Step(Sys, "server sent " + evt + " but this install already applied claim '" +
                                    evt.ClaimId + "' - ignoring. Re-opening a screen or a second status " +
                                    "poll must never re-apply an event.");
                return;
            }

            _pending = evt;
            _hasPending = true;
            FlowTrace.Step(Sys, "Echo Event received: " + evt + ". The server DECIDED it; this device " +
                                "applies it. Applier registered = " + (_applier != null) + ".");

            Guard.Try(Sys, "raise Arrived", () => Arrived?.Invoke(evt));
            TryApplyPending();
        }

        /// <summary>
        /// Apply the pending event, exactly once. Safe to call repeatedly - a repaint,
        /// a second poll and a scene load all land here and only the first one pays.
        /// </summary>
        public static bool TryApplyPending()
        {
            if (!_hasPending)
            {
                return false;
            }

            if (_applier == null)
            {
                FlowTrace.Step(Sys, "Echo Event " + _pending.EventId + " is HELD - no applier has " +
                                    "registered yet (the settlement module boots after the wallet poll). " +
                                    "It is not lost; registration re-drives this.");
                return false;
            }

            var evt = _pending;
            if (IsClaimed(evt.ClaimId))
            {
                _pending = HeartboundEchoEvent.Empty;
                _hasPending = false;
                return false;
            }

            bool applied = Guard.Try(Sys, "apply echo event", () => _applier.Apply(evt), false);
            if (!applied)
            {
                FlowTrace.Warn(Sys, "the applier declined " + evt + " - the claim is NOT marked, so a " +
                                    "later attempt can still pay it. A declined event must never be " +
                                    "silently consumed.");
                return false;
            }

            MarkClaimed(evt.ClaimId);
            _pending = HeartboundEchoEvent.Empty;
            _hasPending = false;
            FlowTrace.Step(Sys, "Echo Event " + evt.EventId + " applied and claim '" + evt.ClaimId +
                                "' recorded locally. The SERVER's ledger is the authority on payment; " +
                                "this record only stops this install applying it twice.");
            Guard.Try(Sys, "raise Applied", () => Applied?.Invoke(evt));
            return true;
        }

        /// <summary>Whether this install already applied that claim.</summary>
        public static bool IsClaimed(string claimId)
        {
            if (string.IsNullOrWhiteSpace(claimId)) return false;
            return PlayerPrefs.GetInt(ClaimPrefPrefix + claimId, 0) == 1;
        }

        private static void MarkClaimed(string claimId)
        {
            if (string.IsNullOrWhiteSpace(claimId)) return;
            Guard.Try(Sys, "record local claim", () =>
            {
                PlayerPrefs.SetInt(ClaimPrefPrefix + claimId, 1);
                PlayerPrefs.Save();
            });
        }

        /// <summary>
        /// Test/dev hook: forget one local claim so an oracle can re-drive the seam.
        /// Exercised by the regression - an unexercised hook proves nothing.
        /// Never called by gameplay.
        /// </summary>
        public static void DebugClearClaim(string claimId)
        {
            if (string.IsNullOrWhiteSpace(claimId)) return;
            Guard.Try(Sys, "clear local claim", () =>
            {
                PlayerPrefs.DeleteKey(ClaimPrefPrefix + claimId);
                PlayerPrefs.Save();
            });
            FlowTrace.Step(Sys, "DEBUG cleared local claim '" + claimId + "' (test hook).");
        }

        /// <summary>Test/dev hook: drop the registered applier and any pending event.</summary>
        public static void DebugReset()
        {
            _applier = null;
            _pending = HeartboundEchoEvent.Empty;
            _hasPending = false;
        }
    }
}

#endif

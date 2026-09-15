// =============================================================================
// EventTrackerIdentityRegression — [analytics-identity] (WO-1735).
// -----------------------------------------------------------------------------
// Assembly: DeNelle.Editor   Registered ONCE in DataRegression.RunAll.
//
// WHY THIS EXISTS
//
// From 2026-09-07 to 2026-09-15 EventTracker.SendBatch built its own
// UnityWebRequest and set exactly ONE header, Content-Type. api/events/track.js
// takes identity from X-Session / X-Guest-Id, so with neither present every event
// from every player collapsed into the single row id 'unverified' and the
// dashboard's COUNT(DISTINCT player_id) read 1. Nothing failed. Nothing logged.
// The only detector was someone reading the dashboard eight days later — exactly
// the silent-failure class CLAUDE.md §12 exists to eliminate.
//
// WHAT IS PINNED, AND HOW HONESTLY
//
//  CASE 1 — BEHAVIOURAL, and it is a REAL measurement, not a source read. It
//  builds an actual UnityWebRequest, calls the production seam
//  BackendRequestSigner.TryAttachCachedSession on it, and reads the header back
//  off the request object. Nothing is ever sent: UnityWebRequest headers are
//  settable and readable without SendWebRequest, so this runs headless with no
//  network, no wallet and no backend. This proves the SEAM does what EventTracker
//  now depends on it doing.
//
//  CASE 2 — SOURCE-TEXT, and it is declared as source-text on purpose. SendBatch
//  is a private async UniTask method on a DontDestroyOnLoad MonoBehaviour whose
//  only observable output is a request it immediately awaits; there is no seam to
//  call it headless without either standing up a live player loop or refactoring
//  the flush path, and refactoring a shipped fire-and-forget rail to make it
//  testable is a bigger risk than the pin is worth. So this case asserts the
//  source contract instead, and says so rather than dressing a grep up as a test.
//
//  ⛔ THE HALF THAT MATTERS MOST IS THE NEGATIVE ONE. Case 2 fails if
//  EventTracker.cs ever contains the literal string "X-Guest-Id" or "X-Session".
//  The fix for WO-1735 was to REUSE the save rail's header seam, never to copy the
//  header names into a second file. A copy would compile, pass every other check,
//  and then drift the day the header contract changes — the duplicated-state
//  failure CLAUDE.md §2, §5 and §16 each describe in their own words. The only way
//  to keep a copy from being reintroduced is to make its mere presence a FAILURE.
//
// ⚠ OUT OF SCOPE, RECORDED HERE BECAUSE IT IS A REAL GAP (server silo, WO-1735
//   hand-back): api/events/track.js:197 sets
//   Access-Control-Allow-Headers: 'Content-Type, X-Session, X-Guest-Id'.
//   TryAttachCachedSession also sets X-Wallet on the wallet rail
//   (BackendRequestSigner.cs:431). On WebGL a header absent from that list fails
//   the CORS preflight. This suite deliberately does NOT assert the server string —
//   api/ is a different gate and this file must not pretend to own it.
// =============================================================================

using System.Collections.Generic;
using System.IO;
using DeNelle.Core.Backend;
using UnityEngine;
using UnityEngine.Networking;

namespace DeNelle.Editor.Regression
{
    /// <summary>Oracle for WO-1735: the analytics POST carries an identity header.</summary>
    public static class EventTrackerIdentityRegression
    {
        // A well-formed guest id: the "guest-local-" prefix plus 64 lowercase hex, the
        // shape BackendRequestSigner.IsGuestIdentity and the server's
        // /^guest-local-[0-9a-f]{64}$/ both require. Deliberately a literal, not a
        // rebuild of the client's hashing — a test that recomputes the value it checks
        // proves only that the code agrees with itself.
        private const string GuestId =
            "guest-local-" +
            "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

        // Base58-shaped, wallet-like, and deliberately NOT a real address.
        private const string WalletId = "DeNe11eTestWa11etAddressNotRea1000000000000";

        private const string TrackerPath = "_Modules/Core/Analytics/EventTracker.cs";

        public static bool Run(out string reason)
        {
            var failures = new List<string>();

            // DataRegression.RunAll calls suites in the bare `if (!X.Run(...))` shape and
            // Guard.Try-wraps only one of them, so an exception escaping here would abort
            // the WHOLE gate run and the marker would simply never print. Contain it.
            try
            {
                SeamAttachesGuestHeaderCase(failures);
                SeamRefusesWalletWithoutSessionCase(failures);
                TrackerRoutesThroughTheSeamCase(failures);
            }
            catch (System.Exception ex)
            {
                failures.Add("threw while running (" + ex.GetType().Name + ": " + ex.Message + ")");
            }

            if (failures.Count > 0)
            {
                reason = "analytics-identity: " + string.Join(" | ", failures.ToArray());
                return false;
            }

            reason = "analytics-identity: the save rail's header seam attaches X-Guest-Id for a " +
                     "guest id and refuses a wallet with no cached session (measured on a real " +
                     "UnityWebRequest, nothing sent); EventTracker.SendBatch routes through that " +
                     "seam, resolves its id through BackendRequestSigner.CurrentPlayerId, and " +
                     "holds NO second copy of the header names (source-text).";
            return true;
        }

        // ── CASE 1a — behavioural: the guest rail actually sets the header ─────
        private static void SeamAttachesGuestHeaderCase(List<string> failures)
        {
            UnityWebRequest req = null;
            try
            {
                req = new UnityWebRequest("http://localhost/analytics-identity-probe", "POST");

                if (!BackendRequestSigner.IsGuestIdentity(GuestId))
                {
                    failures.Add("the fixture guest id is not recognised by " +
                                 "BackendRequestSigner.IsGuestIdentity - the fixture is wrong, " +
                                 "or the guest shape moved away from the server's " +
                                 "/^guest-local-[0-9a-f]{64}$/");
                    return;
                }

                bool attached = BackendRequestSigner.TryAttachCachedSession(req, GuestId);
                if (!attached)
                {
                    failures.Add("TryAttachCachedSession refused a well-formed guest id; the " +
                                 "guest rail is the one that must never need a session");
                    return;
                }

                string sent = req.GetRequestHeader("X-Guest-Id");
                if (sent != GuestId)
                {
                    string got = sent == null ? "<null>" : sent;
                    failures.Add("X-Guest-Id was not set to the guest id after the seam reported " +
                                 "success (got '" + got + "') - EventTracker would deliver rows " +
                                 "the server cannot attribute");
                }
            }
            finally
            {
                if (req != null) req.Dispose();
            }
        }

        // ── CASE 1b — behavioural: no session means no forged wallet header ────
        private static void SeamRefusesWalletWithoutSessionCase(List<string> failures)
        {
            UnityWebRequest req = null;
            try
            {
                req = new UnityWebRequest("http://localhost/analytics-identity-probe", "POST");

                bool attached = BackendRequestSigner.TryAttachCachedSession(req, WalletId);
                if (attached)
                {
                    failures.Add("TryAttachCachedSession claimed to attach a WALLET identity with " +
                                 "no cached session in a headless editor run - either a session " +
                                 "leaked into this process, or the seam stopped requiring one");
                }

                if (req.GetRequestHeader("X-Session") != null)
                {
                    failures.Add("X-Session was set for a wallet with no session. A wallet address " +
                                 "is PUBLIC, not a credential; asserting one without proof is the " +
                                 "hole WO-1506 closed");
                }
            }
            finally
            {
                if (req != null) req.Dispose();
            }
        }

        // ── CASE 2 — SOURCE-TEXT (declared): the tracker uses the seam, not a copy ──
        private static void TrackerRoutesThroughTheSeamCase(List<string> failures)
        {
            string path = Path.Combine(Application.dataPath,
                                       TrackerPath.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(path))
            {
                failures.Add("source-text: EventTracker.cs not found at Assets/" + TrackerPath +
                             " - if the file MOVED, move this pin with it; do not delete the pin");
                return;
            }

            string src;
            try { src = File.ReadAllText(path); }
            catch (System.Exception ex)
            {
                failures.Add("source-text: could not read EventTracker.cs (" + ex.Message + ")");
                return;
            }

            if (src.IndexOf("BackendRequestSigner.TryAttachCachedSession") < 0)
            {
                failures.Add("source-text: EventTracker.cs no longer calls " +
                             "BackendRequestSigner.TryAttachCachedSession - the analytics POST " +
                             "has lost its identity headers again and every row will collapse " +
                             "into 'unverified' SILENTLY (WO-1735)");
            }

            if (src.IndexOf("BackendRequestSigner.CurrentPlayerId") < 0)
            {
                failures.Add("source-text: EventTracker.cs no longer resolves its id through " +
                             "BackendRequestSigner.CurrentPlayerId - that is the ONE identity " +
                             "accessor the save rail uses, and a second one is how the body id " +
                             "and the header id drift apart");
            }

            // The negative half. Matched on the SETTING of an X- header, not on the mere
            // mention of one: the FlowTrace message legitimately names the header it sent,
            // and a pin that cannot tell a log line from a second implementation would be
            // fixed by deleting the instrumentation - the opposite of what we want.
            string copiedHeaderSet = "SetRequestHeader(\"X-";
            if (src.IndexOf(copiedHeaderSet) >= 0)
            {
                failures.Add("source-text: EventTracker.cs sets an X- header directly. The " +
                             "WO-1735 fix was to REUSE BackendRequestSigner's seam, not to copy " +
                             "the header logic into a second file - a copy compiles, passes, and " +
                             "then drifts the day the header contract changes (CLAUDE.md 2/5/16)");
            }

            // The abort guard. Every OTHER caller of this seam fails closed on false;
            // analytics must not, or a fixed attribution bug becomes dropped telemetry.
            if (src.IndexOf("identityAttached") < 0)
            {
                failures.Add("source-text: the named identityAttached result is gone from " +
                             "EventTracker.cs - check the flush still DELIVERS when no identity " +
                             "could be attached (analytics is fire-and-forget and must never " +
                             "fail closed)");
            }
        }
    }
}

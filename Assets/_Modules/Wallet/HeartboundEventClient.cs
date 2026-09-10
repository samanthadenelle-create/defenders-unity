// =============================================================================
// HeartboundEventClient - the ONE client-side PARSER of the backend's Echo Event
// answer, and the one caller of HeartboundEventInbox.AcceptServerEvent.
// -----------------------------------------------------------------------------
// Assembly: DeNelle.Wallet   Namespace: DeNelle.Wallet
// WO-1678 (HEART-005). Owner rulings 2026-09-10 13:10 / 13:12 / 13:20 / 13:36.
//
// ⛔ TWO INDEPENDENT COMPILE-TIME GATES, THE SAME PAIR HeartboundStatusClient.cs
// CARRIES, AND FOR THE SAME REASONS.
//   1. DeNelle.Wallet.asmdef "defineConstraints": ["!GOOGLE_PLAY"] - the WHOLE
//      assembly is absent from a Google Play artifact. Stronger than any flag: a
//      flag is a PlayerPrefs value a stored entry can beat; an absent assembly is
//      absent.
//   2. #if DAPP_STORE - the owner's Seeker-only ruling stated in code. The define
//      says WHERE the binary is going, and a plain Windows or WebGL build carries
//      neither define, so without it this parser would exist on surfaces the owner
//      ruled out of scope.
//
// ⛔ WHY A PARSER AND NOT A FETCHER. There is no endpoint in this ticket. The
// status route belongs to the HEART-001 lane and the pulse loop to HEART-004; this
// file is handed the JSON those lanes already receive. Adding a second poller here
// would be a second authority on when the client talks to the backend - the exact
// duplication HeartboundStatusClient's header refuses for the stake read.
//
// ⛔ AND IT DERIVES NOTHING. It reads the keys api/_lib/heartbound-events.js
// `toClientPayload` emits and copies them across verbatim. It must never pick an
// event, scale a reward, infer a tier or fill in a missing field with a plausible
// value - a client that can compute its own event is a client that can choose one.
// An unparseable payload applies NOTHING and says so (§12: no silent failures).
//
// ⚠ THE PAYLOAD CONTRACT, restated once so the two halves are one contract:
//     { hasEvent, eventId, kind, tableVersion, claimId,
//       reward: { kind, modifier?, magnitude?, durationSeconds?, charges?, itemId?, amount? } }
//   `seedHex` is deliberately NOT in it. A client holding the seed could enumerate
//   the table offline and know its next event.
// =============================================================================

using DeNelle.Core.Diagnostics;
#if DAPP_STORE
using System;
using DeNelle.Core.Heartbound;
using Newtonsoft.Json.Linq;
#endif

namespace DeNelle.Wallet
{
    /// <summary>
    /// Turns the backend's Echo Event JSON into a <see cref="DeNelle.Core.Heartbound.HeartboundEchoEvent"/>
    /// and posts it to the inbox. Parsing only - no network, no grant, no UI.
    /// </summary>
    public static class HeartboundEventClient
    {
        private const string Sys = "HeartboundEvent";

        /// <summary>
        /// Accept an Echo Event payload (the `echoEvent` object of a status or pulse
        /// response). Returns true when an event was posted to the inbox.
        ///
        /// <para>A null or empty payload is a NORMAL answer - a pulse that carried no
        /// event, or a build talking to a backend that has not shipped the field yet.
        /// It clears any pending card rather than throwing.</para>
        /// </summary>
        public static bool AcceptPayload(string json)
        {
#if !DAPP_STORE
            // Not the Seeker/dApp artifact - Heartbound does not exist here.
            // Compiled out rather than flagged off, per the owner's ruling.
            FlowTrace.Once(Sys, "heartbound-not-seeker",
                "an Echo Event payload reached a non-Seeker build and was ignored. Heartbound is " +
                "compiled out on every artifact but the dApp Store one.");
            return false;
#else
            if (string.IsNullOrWhiteSpace(json))
            {
                FlowTrace.Step(Sys, "no Echo Event payload in this response - the pulse carried none, " +
                                    "or this backend does not serve the field yet. Nothing is applied.");
                HeartboundEventInbox.AcceptServerEvent(HeartboundEchoEvent.Empty);
                return false;
            }

            // Explicit Func<bool> rather than a bare lambda: Guard exposes both
            // Try(string,string,Action) and Try<T>(string,string,Func<T>,T), and a
            // value-returning lambda is convertible to BOTH - the overload that wins
            // would be a compiler detail deciding whether the parse result is observed.
            Func<bool> parse = () => ParseAndPost(JObject.Parse(json));
            return Guard.Try(Sys, "parse echo event payload", parse, false);
#endif
        }

#if DAPP_STORE
        /// <summary>
        /// Accept an already-parsed payload object. The status lane calls this directly
        /// with the `echoEvent` child of its response, so the JSON is parsed once.
        /// </summary>
        public static bool AcceptPayload(JObject payload)
        {
            if (payload == null)
            {
                HeartboundEventInbox.AcceptServerEvent(HeartboundEchoEvent.Empty);
                return false;
            }
            Func<bool> parse = () => ParseAndPost(payload);
            return Guard.Try(Sys, "parse echo event payload", parse, false);
        }

        private static bool ParseAndPost(JObject payload)
        {
            bool hasEvent = (bool?)payload["hasEvent"] ?? false;
            if (!hasEvent)
            {
                FlowTrace.Step(Sys, "the server reports hasEvent=false. No Echo Event this pulse - a " +
                                    "normal answer, not a failure, and the pending card is cleared.");
                HeartboundEventInbox.AcceptServerEvent(HeartboundEchoEvent.Empty);
                return false;
            }

            string eventId = (string)payload["eventId"] ?? string.Empty;
            string claimId = (string)payload["claimId"] ?? string.Empty;
            int tableVersion = (int?)payload["tableVersion"] ?? 0;

            // ⛔ NO CLAIM KEY, NO APPLICATION. Once-only keys on this string; an event
            // without it could be applied forever. Refuse rather than invent one - a
            // client-minted key would be a client-controlled ledger.
            if (string.IsNullOrWhiteSpace(claimId))
            {
                FlowTrace.Fail(Sys, "the Echo Event payload for '" + eventId + "' carried NO claimId. " +
                                    "Once-only keys on that string, so nothing is applied. A client that " +
                                    "minted its own key would own the ledger.");
                return false;
            }

            JObject reward = payload["reward"] as JObject;
            string kindText = (string)payload["kind"] ?? (reward != null ? (string)reward["kind"] : null);
            var kind = HeartboundEchoEvent.ParseKind(kindText);
            if (kind == HeartboundRewardKind.None)
            {
                FlowTrace.Warn(Sys, "the Echo Event payload for '" + eventId + "' carried kind '" +
                                    (kindText ?? "null") + "', which this build does not understand. " +
                                    "Applying NOTHING - a build that met an unknown reward must never " +
                                    "apply the nearest thing it recognises.");
                return false;
            }

            var evt = new HeartboundEchoEvent
            {
                HasEvent = true,
                EventId = eventId,
                Kind = kind,
                TableVersion = tableVersion,
                ClaimId = claimId,
                Modifier = reward != null ? (string)reward["modifier"] ?? string.Empty : string.Empty,
                Magnitude = reward != null ? (double?)reward["magnitude"] ?? 0d : 0d,
                DurationSeconds = reward != null ? (double?)reward["durationSeconds"] ?? 0d : 0d,
                Charges = reward != null ? (int?)reward["charges"] ?? 0 : 0,
                ItemId = reward != null ? (string)reward["itemId"] ?? string.Empty : string.Empty,
                Amount = reward != null ? (int?)reward["amount"] ?? 0 : 0,
            };

            FlowTrace.Step(Sys, "parsed " + evt + " from the server payload. Every field is COPIED from " +
                                "the server's answer; this build derives none of them.");
            HeartboundEventInbox.AcceptServerEvent(evt);
            return true;
        }
#endif
    }
}

// =============================================================================
// VerifiedStakeSnapshot — the ONLY native SKR stake any reward path may read.
// -----------------------------------------------------------------------------
// Assembly: DeNelle.Core   Namespace: DeNelle.Core.Platform
// WO-1674 (HEART-001). Owner ruling 2026-09-10 13:10: BACKEND ONLY; an RPC
// outage serves the last-known verified state within a bounded grace window;
// Heartbound is a Seeker-artifact feature.
//
// ⛔⛔ THE ONE RULE THIS TYPE EXISTS TO ENFORCE:
//
//        **NO REWARD PATH READS A CLIENT-COMPUTED STAKE.**
//
// Product rule 7 (spec :22): "The Unity client must never be trusted to report
// the amount of SKR staked."
//
// WHAT WAS ACTUALLY WRONG (WO-1674 §0.1, proven at source 2026-09-10, not
// theorised): NativeSkrStakeQuery computed the amount ON THE DEVICE, and
// StakeRewardsResolver.Query is a PUBLIC SETTABLE PROPERTY
// (StakeRewardsResolver.cs:175-183). Anything in the process could assign an
// IStakeQuery reporting any number, and NativeSkrPolishBonus read it through
// Resolve(). That was tolerable only because the grant is extra ATTEMPTS —
// never odds, never resources. HEART-005 makes a stake grant RESOURCES, and the
// tolerance ends exactly there.
//
// ⚠ THE SEAM IS NOT DELETED, AND SAYING WHY MATTERS. StakeRewardsResolver.Query
// and its MockStakeQuery are still how the Seekerthon SHOWCASE panel renders a
// realistic ladder (StakeRewardsDemoBootstrap.cs:38, SkrShowcasePanel.cs:250).
// They are now DISPLAY-ONLY: this type is what the reward path reads, and it
// has exactly ONE writer. Setting Query can still change what a panel SHOWS; it
// can no longer change what anything GRANTS. That separation is the deliverable,
// not the deletion of a class.
//
// ⛔ THE GRACE WINDOW IS THE SERVER'S, NOT OURS. The client does not decide how
// long a stale snapshot may keep paying. The backend serves STALE with an
// ageSeconds and a graceWindowSeconds it computed, and already refuses to serve
// STALE past that window (api/_lib/skr-staking.js resolveServedState). This type
// re-checks the age it was handed rather than trusting the label, because a
// second cheap check costs nothing and a client that believed a label would be
// a client that could be told anything.
//
// ⛔ NEVER SYNTHESISE A ZERO. An unknown stake is Unknown, never "zero staked".
// The client-side query fails CLOSED to zero today (NativeSkrStakeQuery.cs:87-94)
// which is right for a one-off perk and wrong for a streak; this type keeps the
// two distinguishable so each consumer can choose (WO-1674 Q2).
// =============================================================================

using System;
using DeNelle.Core.Diagnostics;

namespace DeNelle.Core.Platform
{
    /// <summary>
    /// The seven verification states of api/_lib/skr-staking.js's VerificationStatus,
    /// mirrored for the client. ⚠ The SERVER is the authority on this list; the
    /// wire carries the name and <see cref="VerifiedStakeSnapshot.ParseStatus"/>
    /// maps an unknown name to <see cref="Unknown"/> rather than guessing.
    /// </summary>
    public enum StakeVerificationStatus
    {
        /// <summary>No answer has been received yet, or the name was not recognised.</summary>
        Unknown = 0,
        /// <summary>The chain was read and this wallet has an active stake.</summary>
        Verified = 1,
        /// <summary>The chain was read and this wallet has no active stake.</summary>
        NoStake = 2,
        /// <summary>The RPC could not be reached. NOT a stake of zero.</summary>
        RpcUnavailable = 3,
        /// <summary>The account did not exist at the derived address.</summary>
        AccountNotFound = 4,
        /// <summary>This rail has no wallet (guest / Google Play). Heartbound is Seeker-only.</summary>
        WalletNotLinked = 5,
        /// <summary>The RPC answered with something undecodable.</summary>
        InvalidResponse = 6,
        /// <summary>Serving the LAST VERIFIED state through an outage, inside the grace window.</summary>
        Stale = 7,
    }

    /// <summary>
    /// The backend-verified native SKR stake. Written by exactly one caller —
    /// <c>DeNelle.Wallet.HeartboundStatusClient</c>, which is the only thing that
    /// talks to <c>GET /api/heartbound/status</c>. StakingComplianceRegression
    /// fails if a second writer appears.
    /// </summary>
    public static class VerifiedStakeSnapshot
    {
        private const string Sys = "Heartbound";

        /// <summary>SKR is 6 decimals. Raw base units are kept; whole SKR is derived.</summary>
        public const long SkrBaseUnits = 1_000_000L;

        private static StakeVerificationStatus _status = StakeVerificationStatus.Unknown;
        private static long _activeStakedRaw;
        private static long _unstakingRaw;
        private static bool _unstakingReady;
        private static long _verifiedAtUnixMs;
        private static long _ageSeconds;
        private static long _graceWindowSeconds;
        private static bool _everAnswered;

        /// <summary>Raised whenever a verification answer changes what a consumer would read.</summary>
        public static event Action VerificationChanged;

        /// <summary>The status the backend last reported.</summary>
        public static StakeVerificationStatus Status => _status;

        /// <summary>True once any answer has arrived (an answer, not necessarily a stake).</summary>
        public static bool EverAnswered => _everAnswered;

        /// <summary>Age of the underlying VERIFIED read, in seconds, as the server measured it.</summary>
        public static long AgeSeconds => _ageSeconds;

        /// <summary>The server's bounded grace window for stale state, in seconds.</summary>
        public static long GraceWindowSeconds => _graceWindowSeconds;

        /// <summary>Unix ms of the last SUCCESSFUL chain read, or 0 when never verified.</summary>
        public static long VerifiedAtUnixMs => _verifiedAtUnixMs;

        /// <summary>Raw SKR base units currently unstaking. NEVER part of the active stake.</summary>
        public static long UnstakingRaw => _unstakingRaw;

        /// <summary>True when a pending unstake has passed its on-chain cooldown.</summary>
        public static bool UnstakingReady => _unstakingReady;

        /// <summary>
        /// ⛔ THE ONE PROPERTY A REWARD PATH MAY READ. Active stake in WHOLE SKR, and
        /// ZERO unless the backend actually vouched for it.
        ///
        /// Returns a positive number only when the status is Verified, or Stale AND
        /// still inside the server's grace window. Every other state — including an
        /// RPC outage, an unlinked wallet and "we have not asked yet" — reads 0,
        /// because a reward must never be paid on an amount nobody verified.
        ///
        /// ⚠ ZERO HERE MEANS "NOT ENTITLED", NOT "THE PLAYER HAS NO SKR". Use
        /// <see cref="Status"/> to tell a player WHY. Copy is a presentation
        /// decision and this type deliberately makes none.
        /// </summary>
        public static long RewardBearingStakeSkr
        {
            get
            {
                if (!IsRewardBearing) return 0L;
                long whole = _activeStakedRaw / SkrBaseUnits;
                return whole < 0L ? 0L : whole;
            }
        }

        /// <summary>Raw base units of the reward-bearing stake, unrounded (0 when not entitled).</summary>
        public static long RewardBearingStakeRaw => IsRewardBearing ? Math.Max(0L, _activeStakedRaw) : 0L;

        /// <summary>
        /// Is the current snapshot one a reward may be paid on? Verified always;
        /// Stale only while the server-measured age is still inside the server's
        /// own window. The window is re-checked rather than taken on trust.
        /// </summary>
        public static bool IsRewardBearing
        {
            get
            {
                if (_status == StakeVerificationStatus.Verified) return true;
                if (_status != StakeVerificationStatus.Stale) return false;
                if (_graceWindowSeconds <= 0L) return false;
                return _ageSeconds <= _graceWindowSeconds;
            }
        }

        /// <summary>
        /// ⛔ THE SINGLE WRITER. Called ONLY by DeNelle.Wallet.HeartboundStatusClient
        /// with a response from GET /api/heartbound/status. Nothing else may call it,
        /// and StakingComplianceRegression asserts there is exactly one caller.
        ///
        /// ⚠ Deliberately takes the SERVER'S fields verbatim rather than a friendly
        /// object: there is no overload that accepts "an amount", so there is no
        /// shape here that a gameplay call site could use to inject one.
        /// </summary>
        public static void AcceptServerVerification(
            string statusName, long activeStakedRaw, long unstakingRaw, bool unstakingReady,
            long verifiedAtUnixMs, long ageSeconds, long graceWindowSeconds)
        {
            StakeVerificationStatus status = ParseStatus(statusName);

            bool changed = _status != status ||
                           _activeStakedRaw != activeStakedRaw ||
                           _unstakingRaw != unstakingRaw ||
                           _unstakingReady != unstakingReady;

            bool wasRewardBearing = IsRewardBearing;

            _status = status;
            _activeStakedRaw = activeStakedRaw < 0L ? 0L : activeStakedRaw;
            _unstakingRaw = unstakingRaw < 0L ? 0L : unstakingRaw;
            _unstakingReady = unstakingReady;
            _verifiedAtUnixMs = verifiedAtUnixMs < 0L ? 0L : verifiedAtUnixMs;
            _ageSeconds = ageSeconds < 0L ? 0L : ageSeconds;
            _graceWindowSeconds = graceWindowSeconds < 0L ? 0L : graceWindowSeconds;
            _everAnswered = true;

            // §12: every verification transition is traceable, so the next
            // "why did my perk vanish" is one read and not a theory.
            string entitlement = IsRewardBearing ? "REWARD-BEARING" : "not reward-bearing";
            string amount = RewardBearingStakeSkr.ToString("N0");
            FlowTrace.Step(Sys, "server verification accepted: status=" + status +
                                ", " + entitlement + ", stake=" + amount + " SKR" +
                                ", age=" + _ageSeconds + "s, grace=" + _graceWindowSeconds + "s" +
                                ", unstaking=" + _unstakingRaw + " raw (ready=" + _unstakingReady + ").");

            if (status == StakeVerificationStatus.Stale)
            {
                FlowTrace.Warn(Sys, "serving LAST-KNOWN verified stake through an RPC outage: age=" +
                                    _ageSeconds + "s of a " + _graceWindowSeconds + "s grace window. " +
                                    "The chain has NOT been re-read; this is the owner's 2026-09-10 ruling, " +
                                    "not a successful verification.");
            }
            else if (status == StakeVerificationStatus.RpcUnavailable ||
                     status == StakeVerificationStatus.InvalidResponse)
            {
                FlowTrace.Warn(Sys, "verification FAILED (" + status + ") with no servable last-known state — " +
                                    "the stake reads as UNKNOWN and grants nothing. It is NOT recorded as zero.");
            }

            if (wasRewardBearing && !IsRewardBearing)
            {
                FlowTrace.Warn(Sys, "entitlement LOST: a previously reward-bearing stake is no longer " +
                                    "servable (status=" + status + "). Any staking perk stands down now.");
            }

            if (changed) VerificationChanged?.Invoke();
        }

        /// <summary>
        /// Record that a verification attempt could not even be made (offline, no
        /// wallet, request aborted). Distinct from a server answer, and it NEVER
        /// clears an existing snapshot: the last server answer, including its grace
        /// window, remains the authority until the server itself supersedes it.
        /// </summary>
        public static void NoteAttemptFailed(string why)
        {
            FlowTrace.Warn(Sys, "stake verification attempt did not reach the backend (" +
                                (why ?? "unspecified") + "). Holding the previous answer (status=" +
                                _status + ", reward-bearing=" + IsRewardBearing + "); nothing was zeroed.");
        }

        /// <summary>
        /// Wire status name -> enum. An UNRECOGNISED name becomes Unknown, which
        /// grants nothing. A newer backend naming a state this build has never
        /// heard of must fail to "no reward", never to a default that pays.
        /// </summary>
        public static StakeVerificationStatus ParseStatus(string name)
        {
            switch ((name ?? string.Empty).Trim().ToUpperInvariant())
            {
                case "VERIFIED": return StakeVerificationStatus.Verified;
                case "NO_STAKE": return StakeVerificationStatus.NoStake;
                case "RPC_UNAVAILABLE": return StakeVerificationStatus.RpcUnavailable;
                case "ACCOUNT_NOT_FOUND": return StakeVerificationStatus.AccountNotFound;
                case "WALLET_NOT_LINKED": return StakeVerificationStatus.WalletNotLinked;
                case "INVALID_RESPONSE": return StakeVerificationStatus.InvalidResponse;
                case "STALE": return StakeVerificationStatus.Stale;
                default: return StakeVerificationStatus.Unknown;
            }
        }

        /// <summary>Test/reset seam. Clears to Unknown — never to a zero-stake claim.</summary>
        public static void ResetForTests()
        {
            _status = StakeVerificationStatus.Unknown;
            _activeStakedRaw = 0L;
            _unstakingRaw = 0L;
            _unstakingReady = false;
            _verifiedAtUnixMs = 0L;
            _ageSeconds = 0L;
            _graceWindowSeconds = 0L;
            _everAnswered = false;
            VerificationChanged?.Invoke();
        }
    }
}

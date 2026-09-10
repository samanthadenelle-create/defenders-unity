// =============================================================================
// IHeartboundBonusProvider / HeartboundBonuses — the passive-benefit seam
// (WO-1679 / HEART-006; WO-1682 / HEART-009).
// -----------------------------------------------------------------------------
// Assembly: DeNelle.Core   Namespace: DeNelle.Core.Catalog
//
// ⛔⛔ THE ONE RULE THIS SEAM EXISTS TO ENFORCE:
//
//        **THE CLIENT IS TOLD ITS BENEFITS. IT NEVER DERIVES THEM.**
//
// There is no tier ladder in this file, no threshold, no percentage table and no
// arithmetic that turns anything into a tier. Every number below arrives from the
// backend, verbatim, through the single writer. The authority is the server
// (product rule 6), and a client-side copy of the ladder would be a SECOND
// authority on a number the server owns — the duplicated-state failure CLAUDE.md
// §2/§5/§8/§16 each record a separate scar from.
//
// The ladder and the benefit table live in api/_lib/heartbound-resonance-config.json
// and api/_lib/heartbound-tiers-config.json. HeartboundBenefitsRegression FAILS if
// any file under Assets/ grows a copy of either.
//
// -----------------------------------------------------------------------------
// ⭐ SHAPED ON IPolishBonusProvider, DELIBERATELY, INCLUDING ITS NARROWNESS.
//
// PolishBonusProvider.cs:20-23, verbatim: "This interface therefore exposes ONLY
// attempt-shaped grants. There is deliberately no member for odds, weights, luck,
// tier bias or a bonus table, and adding one would break the property the whole
// economy rests on."
//
// The same discipline applies here, and the member list below IS the contract:
//
//     Tier                        — what the server said, for display and gating
//     OfflineProductionRateBonus  — ONE fraction, already summed by the server
//     ExtraWeeklyRerolls          — attempts, never odds
//     RollCapDelta                — attempts, never odds
//     UnlockedEventIds            — which event rows exist, never their weights
//
// ⛔ THERE IS NO GENERAL-PURPOSE MULTIPLIER BAG, NO PER-ID LOOKUP AND NO DICTIONARY,
// and adding one is the thing to refuse. A `float Get(string id)` would let any call
// site invent a modifier the economic meter never measured — and the ceiling the
// whole of HEART-009 exists to defend is measured over a LIST THE SERVER SENT, not
// over whatever a call site asked for. A member here is a member the server
// computes and the meter has already counted.
//
// ⛔ AND NOTHING HERE IS A PROBABILITY. No odds, no weights, no drop chance, no luck.
// A benefit that changed an outcome's likelihood would break the fairness property
// recorded at PolishBonusProvider.cs:8-13 (a staker's roll is exactly as likely as a
// free player's), and DungeonGemExclusivityRegression fails if a roll consults a
// provider at all.
//
// -----------------------------------------------------------------------------
// ⚠ WHAT AN UNSTAKED, UNKNOWN OR OFFLINE PLAYER GETS: the ZERO provider, on every
// code path, with no branch at the call site. Tier 0, no bonus, no attempts, no
// events. That is also what a player gets before the first backend answer arrives,
// and what a player gets when the distribution flag is off. There is no default
// that pays.
//
// ⚠ PLATFORM-FLAGGED, AND THE FLAG IS READ IN EXACTLY ONE PLACE — the `Active`
// property below. Apple and Google both restrict gating gameplay functionality on
// token holdings. `Active` returns the zero provider whenever the flag is off, so a
// Play-store build behaves exactly as if none of this existed even if a provider
// were installed, and NO CALL SITE ANYWHERE HARDCODES A DISTRIBUTION CHECK. That
// single-read property is the one thing to preserve if this file is ever refactored.
//
// -----------------------------------------------------------------------------
// ⛔ THIS FILE NAMES NO CHAIN, NO COIN AND NO KEY-HOLDING APP — not in code, and
// not in a comment either. It compiles into DeNelle.Core, which ships in EVERY
// artifact including a Google Play one, and GooglePlayPackagingGate sweeps the built
// AAB for that vocabulary: those words survive into IL2CPP metadata as member names
// and into the binary as string literals, so a word typed into a comment HERE can
// fail the artifact gate. The membrane is deliberate — every one of those concepts
// lives on the far side of it, in the `!GOOGLE_PLAY`-constrained module (see
// HeartboundBenefitsRegression, which sweeps this file for the vocabulary and names
// the module it belongs in). Spec :1316-1322 states the rule as: a gathering system
// must never know what any of it is.
//
// -----------------------------------------------------------------------------
// ⚠ OfflineProductionRateBonus IS EXPOSED AND NOT YET CONSUMED, AND THAT IS
// RECORDED RATHER THAN HIDDEN. Offline accrual fans out to four consumers that each
// compute their own share inside IOfflineClaimConsumer.ApplyOfflineWindow
// (Assets/_Modules/Village/Harvest/OfflineClaimCoordinator.cs:107,163) — there is NO
// single accrual point to apply one multiplier at. Wiring it means touching four
// Village-side consumers, i.e. writing the same modifier in four places, which is
// the very duplication this codebase keeps paying for. The seam ships declared and
// unread, exactly as EchoLaneBonuses' CraftingMult / DefenseMult / ExplorationMult
// already do (EchoLaneBonuses.cs:14-23 states its own consumption status for the
// same reason). See WORK_ORDER_1679_...RESULT.md, "what was NOT done and why".
// =============================================================================

using System;
using System.Collections.Generic;
using DeNelle.Core.Diagnostics;

namespace DeNelle.Core.Catalog
{
    /// <summary>
    /// Supplies the passive benefits the backend has granted this player. ⚠ Every member is a
    /// value the SERVER computed. Nothing here derives anything, and nothing here is a
    /// probability — see the file header before adding a member.
    /// </summary>
    public interface IHeartboundBonusProvider
    {
        /// <summary>The tier the server reported. 0 for everyone by default.</summary>
        int Tier { get; }

        /// <summary>
        /// The combined recurring boost to offline resource yield, as a FRACTION (0.06 = +6%),
        /// already summed and already checked against its ceiling by the server. One number,
        /// not a table: the ceiling is measured over the list the server holds, so a call site
        /// must never be able to assemble its own.
        /// </summary>
        float OfflineProductionRateBonus { get; }

        /// <summary>Extra free polish re-rolls granted this week. Attempts, never odds.</summary>
        int ExtraWeeklyRerolls { get; }

        /// <summary>Amount to ADD to the per-stone polish roll cap. Attempts, never odds.</summary>
        int RollCapDelta { get; }

        /// <summary>Ids of the event rows this player has unlocked. Ids only — never weights.</summary>
        IReadOnlyList<string> UnlockedEventIds { get; }
    }

    /// <summary>The zero provider: what every player gets until the backend says otherwise.</summary>
    public sealed class NoHeartboundBonus : IHeartboundBonusProvider
    {
        private static readonly string[] None = new string[0];

        public int Tier => 0;
        public float OfflineProductionRateBonus => 0f;
        public int ExtraWeeklyRerolls => 0;
        public int RollCapDelta => 0;
        public IReadOnlyList<string> UnlockedEventIds => None;
    }

    /// <summary>
    /// The provider fed by the backend's status response. Holds only what it was told.
    /// </summary>
    /// <remarks>
    /// ⛔ IT HAS NO CONSTRUCTOR THAT TAKES A TIER ALONE, and that is on purpose: a tier without
    /// the benefits the server derived from it would force this class to derive them, which is
    /// the one thing the whole file exists to prevent. The server sends both or neither.
    /// </remarks>
    public sealed class ServerToldHeartboundBonus : IHeartboundBonusProvider
    {
        private static readonly string[] None = new string[0];

        private readonly int _tier;
        private readonly float _offlineRate;
        private readonly int _rerolls;
        private readonly int _rollCap;
        private readonly string[] _events;

        public ServerToldHeartboundBonus(
            int tier, float offlineProductionRateBonus, int extraWeeklyRerolls,
            int rollCapDelta, string[] unlockedEventIds)
        {
            _tier = tier < 0 ? 0 : tier;
            _offlineRate = offlineProductionRateBonus > 0f ? offlineProductionRateBonus : 0f;
            _rerolls = extraWeeklyRerolls < 0 ? 0 : extraWeeklyRerolls;
            _rollCap = rollCapDelta < 0 ? 0 : rollCapDelta;
            _events = unlockedEventIds ?? None;
        }

        public int Tier => _tier;
        public float OfflineProductionRateBonus => _offlineRate;
        public int ExtraWeeklyRerolls => _rerolls;
        public int RollCapDelta => _rollCap;
        public IReadOnlyList<string> UnlockedEventIds => _events;
    }

    /// <summary>
    /// The installed provider, plus the flag gate. Query through here, never through a provider
    /// reference held at a call site — that is what keeps the distribution flag in exactly one
    /// place.
    /// </summary>
    public static class HeartboundBonuses
    {
        private const string Sys = "Heartbound";

        private static readonly IHeartboundBonusProvider Zero = new NoHeartboundBonus();
        private static IHeartboundBonusProvider _installed;

        /// <summary>Raised when the installed benefits change what a consumer would read.</summary>
        public static event Action BenefitsChanged;

        /// <summary>
        /// ⛔ THE SINGLE WRITER'S ENTRY POINT. Called ONLY by the one client that reads the
        /// backend status response — HeartboundStatusClient, in the distribution-gated module;
        /// HeartboundBenefitsRegression asserts there is exactly one caller.
        ///
        /// ⚠ It takes the SERVER'S FIELDS VERBATIM rather than a friendly object, for the same
        /// reason the verified-stake snapshot's own accept method does: there is no overload here
        /// that accepts "a tier" or "a bonus", so there is no shape a gameplay call site could
        /// use to invent one.
        ///
        /// ⛔ AND THE SENTENCE ABOVE DELIBERATELY DOES NOT SPELL THAT METHOD'S FULL NAME.
        /// StakingComplianceRegression §A4 proves "exactly one writer of the verified stake" with a
        /// SUBSTRING SCAN over each runtime file, and it does not strip comments — so naming the
        /// method here, even to compare against it, reported THIS FILE as a second writer and
        /// turned a correct architecture into a RED gate (chain 48). The lint is right to be blunt:
        /// a static setter cannot be made unsettable, so counting callers by text is the only
        /// guarantee available, and a scan that tried to parse out comments would be a scan that
        /// could be fooled. This file writes nothing but its OWN provider; keep it that way, and
        /// keep that method's name out of this file.
        /// </summary>
        public static void AcceptServerBenefits(
            int tier, float offlineProductionRateBonus, int extraWeeklyRerolls,
            int rollCapDelta, string[] unlockedEventIds)
        {
            var next = new ServerToldHeartboundBonus(
                tier, offlineProductionRateBonus, extraWeeklyRerolls, rollCapDelta, unlockedEventIds);

            int wasTier = Tier;
            int wasRerolls = ExtraWeeklyRerolls;
            int wasRollCap = RollCapDelta;
            float wasRate = OfflineProductionRateBonus;

            _installed = next;

            // §12: every transition is traceable, so the next "why did my passive vanish" is
            // one read of the log and not a theory.
            FlowTrace.Step(Sys, "server benefits accepted: tier=" + next.Tier +
                                ", offlineRate=+" + (next.OfflineProductionRateBonus * 100f).ToString("0.##") +
                                "%, rerolls=" + next.ExtraWeeklyRerolls +
                                ", rollCap=+" + next.RollCapDelta +
                                ", events=" + next.UnlockedEventIds.Count +
                                (Enabled ? string.Empty : " (distribution flag OFF - every reader still sees zero)"));

            if (wasTier > 0 && next.Tier == 0)
            {
                FlowTrace.Warn(Sys, "tier LOST: the server now reports tier 0, so every passive " +
                                    "benefit stands down. This is the server's answer, not a local " +
                                    "decision, and nothing was inferred to reach it.");
            }

            if (Tier != wasTier || ExtraWeeklyRerolls != wasRerolls ||
                RollCapDelta != wasRollCap || Math.Abs(OfflineProductionRateBonus - wasRate) > 0.0001f)
            {
                BenefitsChanged?.Invoke();
            }
        }

        /// <summary>
        /// Record that no answer could be obtained. ⛔ IT NEVER CLEARS THE PREVIOUS ANSWER:
        /// a failed request is not a report of tier 0, and treating it as one would take a
        /// player's passives away every time their train went into a tunnel. The backend's
        /// last answer stands until the backend itself supersedes it.
        /// </summary>
        public static void NoteAttemptFailed(string why)
        {
            FlowTrace.Warn(Sys, "benefit refresh did not reach the backend (" + (why ?? "unspecified") +
                                "). Holding the previous answer (tier=" + Tier + "); nothing was zeroed.");
        }

        /// <summary>
        /// ⛔ THE DISTRIBUTION FLAG IS READ HERE AND NOWHERE ELSE. Returns the zero provider
        /// whenever the flag is off, so an artifact that may not gate gameplay on a holding
        /// behaves exactly as if this feature did not exist — even if a provider was installed.
        /// </summary>
        private static IHeartboundBonusProvider Active
        {
            get
            {
                if (!FlagOn) return Zero;
                return _installed ?? Zero;
            }
        }

        /// <summary>
        /// ⛔ THE ONE READ OF THE DISTRIBUTION FLAG IN THIS ENTIRE FEATURE. Everything else —
        /// <see cref="Active"/>, <see cref="Enabled"/>, and every consumer — goes through here.
        /// HeartboundBenefitsRegression counts the occurrences of the flag name in this file and
        /// FAILS at anything but one, so a second read cannot be added by accident.
        /// (It has already earned that check once: `Enabled` was written reading the flag
        /// directly, which made two, and the gate's own dry run caught it.)
        /// </summary>
        private static bool FlagOn => FeatureFlags.HeartboundPassives;

        /// <summary>True when this artifact is permitted to apply these benefits at all.</summary>
        public static bool Enabled => FlagOn;

        /// <summary>The server-reported tier. 0 unless answered AND the flag is on.</summary>
        public static int Tier => Active.Tier;

        /// <summary>
        /// Combined offline production bonus as a fraction. 0 unless answered AND the flag is on.
        /// ⚠ Declared and NOT YET CONSUMED — see the file header for why, and for the precedent.
        /// </summary>
        public static float OfflineProductionRateBonus => Active.OfflineProductionRateBonus;

        /// <summary>Extra weekly polish re-rolls. Attempts, never odds.</summary>
        public static int ExtraWeeklyRerolls => Active.ExtraWeeklyRerolls;

        /// <summary>Polish roll-cap bonus. Attempts, never odds.</summary>
        public static int RollCapDelta => Active.RollCapDelta;

        /// <summary>Unlocked event ids. Empty unless answered AND the flag is on.</summary>
        public static IReadOnlyList<string> UnlockedEventIds => Active.UnlockedEventIds;

        /// <summary>True when the server said this event row is unlocked for this player.</summary>
        public static bool HasUnlockedEvent(string eventId)
        {
            if (string.IsNullOrEmpty(eventId)) return false;
            var list = Active.UnlockedEventIds;
            for (int i = 0; i < list.Count; i++)
                if (string.Equals(list[i], eventId, StringComparison.Ordinal)) return true;
            return false;
        }

        /// <summary>Test/reset seam. Clears to the ZERO provider — never to a granting default.</summary>
        public static void ResetForTests()
        {
            _installed = null;
            BenefitsChanged?.Invoke();
        }
    }
}

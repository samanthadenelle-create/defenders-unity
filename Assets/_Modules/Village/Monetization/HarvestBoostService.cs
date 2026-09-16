// =============================================================================
// HarvestBoostService — WO-1119, the 2x harvest boost. VERSION B ONLY.
// -----------------------------------------------------------------------------
// Assembly: DeNelle.Village   Namespace: DeNelle.Village.Monetization
//
// ⛔ THE COVENANT, AND IT IS THE WHOLE POINT OF THE FILE (WO-1119 sec.1).
// There were two ways to build this and only one of them may ever exist:
//
//   VERSION A (FORBIDDEN) — fold the multiplier into
//       EchoBonusCalculator.AggregateHarvestMultiplier. That scales RATE **and**
//       SiloCapacity together (EchoService.SiloCapacity is authored off the same
//       rate basis), so an offline player comes back with 2x the RESOURCES. That
//       is selling AMOUNT, i.e. power. Never do it.
//
//   VERSION B (THIS FILE) — multiply only the RATE the accrual integrates, and
//       leave every CAP exactly where it was. The store/silo/away-cap fills in
//       half the time; the ceiling it fills to is untouched. The player buys
//       TIME, never goods. That is the only shape the convenience covenant
//       (docs/monetization-v2-spec.md sec.5.3) permits.
//
// Concretely, every consumer applies the boost as EXTRA INTEGRATION SECONDS and
// then clamps to its own pre-existing cap:
//       effectiveSeconds = min(windowSeconds + overlapSeconds * (mult - 1), capSeconds)
// The `min` is the covenant. Delete it and this becomes Version A.
//
// FOUR MORE BINDING RULES from the WO:
//   * Effective multiplier is HARD-CAPPED at 2.0x. A second grant EXTENDS THE
//     DURATION; it never multiplies (2x + 2x is 2x for twice as long, not 4x).
//   * CRYSTALS ARE NEVER BOOSTED. Crystals are the real-money on-ramp; a boost
//     that mints them turns a convenience item into a currency printer. Callers
//     ask ThisResourceIsBoostable(...) — the exclusion lives here, once.
//   * A grant is REFUSED when the town bank has no headroom, with a plain
//     player-readable reason. Silently burning a purchase into a full bank is the
//     single worst outcome this feature can produce.
//   * A partial-window offline claim integrates the OVERLAP of the boost window
//     with the claim window (WO-1119 sec.3b) — not "was it active when you got
//     back", which would pay a 10-hour window for a boost that ran for 20 minutes.
//
// CLOCK. Every instant here is unix-ms from TimeSource.NowUnixMs(), which is the
// WO-912 server-anchored clock when a handshake has happened this process. This
// is deliberate and matches BuildTimerService's ad window: a boost bought with
// crystals is real value on a timer, so a wall-clock edit must not extend it.
// Never stamp any field here from DateTime.UtcNow.
//
// PERSISTENCE — PlayerPrefs TODAY, SAVE FIELDS TOMORROW (declared limitation).
// WO-1119 sec.3a asks for HarvestBoostEndsAtMs / HarvestBoostMult on GameState,
// in the LastHarvestClaimMs clock family. That is a save-schema change and this
// seat does not own Assets/_Modules/Core/State. The state lives in PlayerPrefs
// meanwhile, which is honest about what it is: DEVICE-LOCAL and NOT carried by a
// cloud save or a reinstall. Migrating is a mechanical swap of the three
// Read/Write helpers at the bottom of this file and nothing else — every reader
// goes through them on purpose. See the RESULT hand-off note for the exact fields.
// =============================================================================
using System.Globalization;
using DeNelle.Core.Diagnostics;
using DeNelle.Core.Economy;
using DeNelle.Core.State;
using UnityEngine;

namespace DeNelle.Village.Monetization
{
    /// <summary>
    /// The ONE authority on whether a harvest-rate boost is running, how strong it is, and how
    /// much of a given accrual window it actually covered. Stateless consumers ask
    /// <see cref="BoostedSeconds"/>; nothing else may reason about the boost's timing.
    /// </summary>
    public static class HarvestBoostService
    {
        // ── Covenant constants ───────────────────────────────────────────────
        /// <summary>Hard ceiling on the effective multiplier. Stacking extends DURATION, never this.</summary>
        public const float MaxMultiplier = 2.0f;

        /// <summary>The product's standard strength (WO-1119 sec.2 — every tier is 2.0x).</summary>
        public const float StandardMultiplier = 2.0f;

        /// <summary>
        /// Crystal price of the 4-hour boost — the RECURRING SINK the whole ticket exists to create
        /// (WO-1119 sec.0: ~154 crystals sink the entire catalogue today against a 250-crystal fresh
        /// save, so impulse crystal packs have nothing to be spent on). Tunable; 4h is pinned to
        /// EchoService.SiloCapHours (4h) because a boost longer than the silo oversells itself to an
        /// AFK player, who banks nothing past the cap.
        /// </summary>
        public const int PurchasePriceCrystals = 120;

        /// <summary>Duration of the crystal-purchased boost, in seconds (4h — see above).</summary>
        public const double PurchaseDurationSeconds = 4.0 * 3600.0;

        // ── Persisted state (see the header's PERSISTENCE note) ──────────────
        private const string PrefEndsAt  = "harvestboost.endsatms";
        private const string PrefStarted = "harvestboost.startedatms";
        private const string PrefMult    = "harvestboost.mult";
        private const string PrefSource  = "harvestboost.source";

        /// <summary>Raised whenever the boost starts, extends or expires, so a HUD can re-read.</summary>
        public static event System.Action Changed;

        // =====================================================================
        //  Query
        // =====================================================================

        /// <summary>Unix-ms the running boost ends at; 0 when none is running.</summary>
        public static double EndsAtUnixMs => ExpireIfDue() ? 0.0 : ReadDouble(PrefEndsAt);

        /// <summary>Unix-ms the running boost started at; 0 when none is running.</summary>
        public static double StartedAtUnixMs => ExpireIfDue() ? 0.0 : ReadDouble(PrefStarted);

        /// <summary>True while a boost is running RIGHT NOW.</summary>
        public static bool IsActive => EndsAtUnixMs > TimeSource.NowUnixMs();

        /// <summary>
        /// The multiplier in force right now: 1.0 when nothing is running, otherwise the stored
        /// strength clamped to <see cref="MaxMultiplier"/>. Clamped on READ as well as on write so a
        /// hand-edited pref (or a future migrated save field) can never exceed the covenant ceiling.
        /// </summary>
        public static float MultiplierNow
        {
            get
            {
                if (!IsActive) return 1f;
                float m = (float)ReadDouble(PrefMult);
                if (m < 1f) return 1f;
                return Mathf.Min(m, MaxMultiplier);
            }
        }

        /// <summary>Seconds left on the running boost (0 when none).</summary>
        public static double SecondsRemaining
        {
            get
            {
                double ends = EndsAtUnixMs;
                if (ends <= 0.0) return 0.0;
                double left = (ends - TimeSource.NowUnixMs()) / 1000.0;
                return left > 0.0 ? left : 0.0;
            }
        }

        /// <summary>What started the running boost ("place.harvest.doubler", "crystals", a pack id...).</summary>
        public static string Source => IsActive ? PlayerPrefs.GetString(PrefSource, "") : "";

        /// <summary>
        /// ⛔ THE CRYSTAL EXCLUSION, in one place. A harvest boost may never accelerate the premium
        /// currency: crystals are what packs SELL, so a boost that mints them converts a time-saver
        /// into a money printer and breaks the same law that keeps crystals off every ad reward
        /// (ad-placements.json _LAW_1). Every accrual path asks HERE rather than testing the enum
        /// itself, so the rule cannot be half-applied by a path that forgot it.
        /// </summary>
        public static bool IsBoostable(MineResource resource) => resource != MineResource.AetherCrystal;

        /// <summary>
        /// VERSION B, THE ONLY MATH ANY CONSUMER SHOULD RUN. Given an accrual window that ENDS at
        /// <paramref name="windowEndUnixMs"/> and covers <paramref name="windowSeconds"/> of
        /// integration, returns how many seconds to integrate WITH the boost folded in — the boost's
        /// real OVERLAP with that window (WO-1119 sec.3b), never its whole duration — clamped to
        /// <paramref name="capSeconds"/>, the caller's own pre-existing ceiling.
        ///
        /// <para>The clamp is the covenant. It is what makes this "the cap fills twice as fast"
        /// rather than "you get twice as much": a player away long enough to reach their cap banks
        /// exactly what they always banked, they just reached it sooner. Pass the SAME cap the
        /// caller already enforced without a boost; never pass a widened one.</para>
        /// </summary>
        public static double BoostedSeconds(double windowEndUnixMs, double windowSeconds, double capSeconds)
        {
            if (windowSeconds <= 0.0) return 0.0;
            double plain = Mathf.Min((float)windowSeconds, (float)Mathf.Max(0f, (float)capSeconds));

            double ends = ReadDouble(PrefEndsAt);
            double started = ReadDouble(PrefStarted);
            float mult = (float)ReadDouble(PrefMult);
            if (ends <= 0.0 || started <= 0.0 || mult <= 1f) return plain;
            if (mult > MaxMultiplier) mult = MaxMultiplier;

            // Overlap of [started, ends] with [windowEnd - windowSeconds, windowEnd].
            double windowStart = windowEndUnixMs - (windowSeconds * 1000.0);
            double overlapStart = started > windowStart ? started : windowStart;
            double overlapEnd = ends < windowEndUnixMs ? ends : windowEndUnixMs;
            double overlapSec = (overlapEnd - overlapStart) / 1000.0;
            if (overlapSec <= 0.0)
            {
                FlowTrace.Step("HarvestBoost",
                    $"claim overlap = 0s (boost {started:F0}..{ends:F0} vs window {windowStart:F0}..{windowEndUnixMs:F0}) " +
                    "-> plain accrual. A boost that did not run DURING the away window pays nothing for it.");
                return plain;
            }

            double boosted = windowSeconds + (overlapSec * (mult - 1f));
            double clamped = boosted;
            bool hitCap = capSeconds > 0.0 && clamped > capSeconds;
            if (hitCap) clamped = capSeconds;

            FlowTrace.Step("HarvestBoost",
                $"claim-overlap: window {windowSeconds:F0}s, boost covered {overlapSec:F0}s at {mult:0.##}x " +
                $"-> integrate {clamped:F0}s (cap {capSeconds:F0}s{(hitCap ? ", CLAMPED - Version B: the cap filled sooner, it did not grow" : "")}).");
            return clamped;
        }

        // =====================================================================
        //  Grant
        // =====================================================================

        /// <summary>
        /// Starts (or EXTENDS) a boost. Returns false with a player-readable
        /// <paramref name="failure"/> and grants nothing when the town bank has no headroom.
        ///
        /// <para>STACKING IS EXTENSION, NEVER MULTIPLICATION (WO-1119 sec.1). A second 2x grant on a
        /// running 2x boost adds its seconds to the end and leaves the strength at 2x. The effective
        /// multiplier is also clamped to <see cref="MaxMultiplier"/> even for a single grant, so a
        /// mis-authored pack line asking for 5x quietly becomes 2x rather than shipping power.</para>
        /// </summary>
        public static bool TryStart(double seconds, float multiplier, string source, out string failure)
        {
            failure = null;
            if (seconds <= 0.0 || multiplier <= 1f)
            {
                failure = "That boost grants nothing.";
                FlowTrace.Warn("HarvestBoost",
                    $"start REFUSED from '{source}': inert grant (seconds={seconds:F0}, mult={multiplier:0.##}).");
                return false;
            }

            // BANK-FULL REFUSAL (WO-1119 sec.1). Faster income into a full bank is income LOST -
            // OfflineHarvestService.Grant clamps and logs "BANK FULL ... the surplus was LOST". A
            // purchase that lands there is a burnt purchase, so refuse in plain words instead.
            // Crystals are excluded from the boost entirely, so their (uncapped) headroom is not
            // the question: the question is whether ANY boostable resource can still take income.
            if (!AnyBoostableHeadroom())
            {
                failure = "Your storehouses are full. Spend or upgrade before starting a boost.";
                FlowTrace.Warn("HarvestBoost",
                    $"start REFUSED from '{source}': no headroom in wood/iron/food - a boost here would " +
                    "accelerate income straight into the bank cap and be silently lost. Nothing was spent.");
                return false;
            }

            double now = TimeSource.NowUnixMs();
            double currentEnds = ExpireIfDue() ? 0.0 : ReadDouble(PrefEndsAt);
            bool extending = currentEnds > now;

            float mult = Mathf.Min(multiplier, MaxMultiplier);
            if (extending)
            {
                float running = (float)ReadDouble(PrefMult);
                if (running > mult) mult = Mathf.Min(running, MaxMultiplier);
            }

            double newEnds = (extending ? currentEnds : now) + (seconds * 1000.0);
            double started = extending ? ReadDouble(PrefStarted) : now;

            WriteDouble(PrefStarted, started);
            WriteDouble(PrefEndsAt, newEnds);
            WriteDouble(PrefMult, mult);
            PlayerPrefs.SetString(PrefSource, source ?? "");
            PlayerPrefs.Save();

            FlowTrace.Step("HarvestBoost",
                $"{(extending ? "EXTENDED" : "STARTED")} from '{source}': {mult:0.##}x for +{seconds / 60.0:F0} min " +
                $"-> ends {newEnds:F0} ({SecondsRemaining / 60.0:F0} min left). serverAnchored={TimeSource.IsServerAnchored}. " +
                (multiplier > MaxMultiplier
                    ? $"Requested {multiplier:0.##}x was CLAMPED to the {MaxMultiplier:0.##}x covenant ceiling. "
                    : "") +
                "Version B: rate only - every cap is untouched.");

            Guard.Try("HarvestBoost", "raise Changed", () => Changed?.Invoke());
            return true;
        }

        /// <summary>
        /// WO-1119 sec.3c — THE RECURRING CRYSTAL SINK. Spends
        /// <see cref="PurchasePriceCrystals"/> and starts the 4-hour boost. Crystals are debited
        /// ONLY on a successful start, so a bank-full refusal costs the player nothing.
        /// </summary>
        public static bool TryPurchaseWithCrystals(out string failure)
        {
            failure = null;
            var svc = GameStateService.Instance;
            var state = svc != null ? svc.State : null;
            if (state == null)
            {
                failure = "Save not loaded.";
                FlowTrace.Fail("HarvestBoost", "purchase attempted with no GameState.");
                return false;
            }

            if (state.Resources.Crystals < PurchasePriceCrystals)
            {
                failure = $"Not enough crystals: {PurchasePriceCrystals} needed, {state.Resources.Crystals} held.";
                FlowTrace.Step("HarvestBoost",
                    $"purchase declined - broke ({state.Resources.Crystals}/{PurchasePriceCrystals}).");
                return false;
            }

            // ORDER MATTERS: start first, charge second. TryStart is the path that can refuse
            // (bank full), and a charge before a refusal is a crystal the player never got value
            // for - the exact failure WO-1121 sec.1 calls "charged + empty inventory".
            if (!TryStart(PurchaseDurationSeconds, StandardMultiplier, "crystals", out failure)) return false;

            svc.AddCrystals(-PurchasePriceCrystals);
            FlowTrace.Step("HarvestBoost",
                $"SOLD: 4h {StandardMultiplier:0.##}x harvest for {PurchasePriceCrystals} crystals " +
                $"({state.Resources.Crystals} left). This is the recurring sink WO-1119 exists to create.");
            return true;
        }

        /// <summary>Player-facing one-liner for the shop row / HUD chip.</summary>
        public static string OfferLabel() =>
            $"2x Harvest ({PurchaseDurationSeconds / 3600.0:0.#}h) - {PurchasePriceCrystals} crystals";

        /// <summary>Player-facing status line ("" when nothing is running).</summary>
        public static string StatusLabel()
        {
            if (!IsActive) return "";
            double mins = SecondsRemaining / 60.0;
            return mins >= 60.0
                ? $"2x Harvest active - {mins / 60.0:0.#}h left"
                : $"2x Harvest active - {mins:0} min left";
        }

        /// <summary>Test/QA reset. Never called by gameplay.</summary>
        public static void ClearForTests()
        {
            PlayerPrefs.DeleteKey(PrefEndsAt);
            PlayerPrefs.DeleteKey(PrefStarted);
            PlayerPrefs.DeleteKey(PrefMult);
            PlayerPrefs.DeleteKey(PrefSource);
            PlayerPrefs.Save();
        }

        // =====================================================================
        //  Internals
        // =====================================================================

        // Returns true when it JUST expired a boost (so callers read 0 rather than a stale end).
        private static bool ExpireIfDue()
        {
            double ends = ReadDouble(PrefEndsAt);
            if (ends <= 0.0) return true;
            if (ends > TimeSource.NowUnixMs()) return false;

            FlowTrace.Step("HarvestBoost",
                $"EXPIRED (ended {ends:F0}, source '{PlayerPrefs.GetString(PrefSource, "?")}'). " +
                "Rate returns to base; no cap ever moved.");
            ClearForTests();
            Guard.Try("HarvestBoost", "raise Changed(expire)", () => Changed?.Invoke());
            return true;
        }

        private static bool AnyBoostableHeadroom()
        {
            // TownBankCapacity is the ONE bank reader (WO-857 Phase F); do not re-derive a cap here.
            return TownBankCapacity.HasHeadroom(BankResource.Wood)
                || TownBankCapacity.HasHeadroom(BankResource.Iron)
                || TownBankCapacity.HasHeadroom(BankResource.Stone);
        }

        // The three-line seam the save-schema migration replaces (see header). Doubles are stored
        // as invariant strings because PlayerPrefs has no double slot and a float loses unix-ms
        // precision outright (a float cannot represent 1.7e12 to the millisecond).
        private static double ReadDouble(string key)
        {
            string raw = PlayerPrefs.GetString(key, "");
            if (string.IsNullOrEmpty(raw)) return 0.0;
            return double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out double v) ? v : 0.0;
        }

        private static void WriteDouble(string key, double value) =>
            PlayerPrefs.SetString(key, value.ToString("R", CultureInfo.InvariantCulture));
    }
}

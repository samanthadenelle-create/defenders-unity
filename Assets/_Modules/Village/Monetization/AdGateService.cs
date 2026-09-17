// =============================================================================
// AdGateService — WO-1120. The policy + ledger + grant layer over the placement
// table. This is the "AdGateService" every doc, regression comment and the data
// file's own _comment has referred to since 2026-08-07 and which did not exist.
// -----------------------------------------------------------------------------
// Assembly: DeNelle.Village   Namespace: DeNelle.Village.Monetization
//
// THE SHAPE OF THE SYSTEM, so nobody adds a fourth layer:
//   IAdService / AdServices        the NETWORK seam (LevelPlay). Knows nothing of us.
//   RewardedAdManager              the presentation seam + async contract (WO-1125).
//   AdPlacementCatalog             the validated, covenant-screened data.
//   AdGateService  (this file)     WHEN an offer may appear, WHAT it pays, and the
//                                  ledger that enforces cooldowns and daily caps.
//   call sites (build queue, harvest UI, daily chest)  ask Offer(), call Present().
//
// FIVE HARD LAWS, all enforced here (WO-1120 sec.2):
//   1. No ad reward may grant premium currency. Screened at LOAD by the catalog,
//      and screened AGAIN here before any grant runs. Defence in depth is right
//      for the one rule whose failure costs us the ad account, not just money.
//   2. The timeskip amount has ONE authority - BuildTimerConfig.adSkipSeconds.
//      We read the config; the JSON number is only checked for drift.
//   3. No revive / battle-continue. Deleted from the data in 2026-08-07 and there
//      is no grant path for it here. Never restore one.
//   4. GRANT ONLY ON THE GENUINE EARNED-REWARD CALLBACK. Never on show, never on
//      open, never on "the ad closed". This routes through
//      RewardedAdManager.RequestAd, whose reward action fires only from the SDK's
//      OnAdRewarded. Granting on show is fraud against the network.
//   5. No-fill / dismissed-early grants NOTHING and says so specifically -
//      NoFill is "no ads available right now" and is ORDINARY, not an error.
//
// THE LEDGER AND ITS CLOCK. Cooldowns and daily caps are stamped from
// TimeSource.NowUnixMs(), the WO-912 server-anchored clock, for exactly the reason
// BuildTimerService's ad window is: with a real network behind the button, a
// device-clock rollback that mints a fresh allowance is not free skips, it is
// FABRICATED IMPRESSIONS against a live ad account. A backwards clock CLOSES the
// day rather than opening one (refuse, don't punish - WO-912 sec.7.3).
//
// LEDGER STORAGE is PlayerPrefs, device-local, same declared limitation as
// HarvestBoostService: the save-schema fields are not this seat's to add. It is a
// deliberate floor, not a ceiling - see the RESULT hand-off note.
// =============================================================================
using System;
using System.Globalization;
using DeNelle.Core;
using DeNelle.Core.Ads;
using DeNelle.Core.Diagnostics;
using DeNelle.Core.Analytics;
using UnityEngine;

namespace DeNelle.Village.Monetization
{
    /// <summary>What a call site needs to decide whether (and how) to render an ad offer.</summary>
    public readonly struct AdOffer
    {
        /// <summary>True when an offer may be shown RIGHT NOW.</summary>
        public readonly bool Available;

        /// <summary>Why not, when <see cref="Available"/> is false. None when available.</summary>
        public readonly AdUnavailableReason Reason;

        /// <summary>Player-readable copy from the placement's prompt block.</summary>
        public readonly string Headline;
        public readonly string Body;
        public readonly string Cta;

        /// <summary>What the reward actually pays, from the data ("Take 10 minutes off this job").</summary>
        public readonly string RewardDescription;

        /// <summary>Watches left against the placement's own daily cap (int.MaxValue = unlimited).</summary>
        public readonly int RemainingToday;

        /// <summary>Seconds until this placement's cooldown clears (0 when clear).</summary>
        public readonly double CooldownRemaining;

        public AdOffer(bool available, AdUnavailableReason reason, string headline, string body,
                       string cta, string rewardDescription, int remainingToday, double cooldownRemaining)
        {
            Available = available;
            Reason = reason;
            Headline = headline;
            Body = body;
            Cta = cta;
            RewardDescription = rewardDescription;
            RemainingToday = remainingToday;
            CooldownRemaining = cooldownRemaining;
        }

        public static AdOffer No(AdUnavailableReason why) =>
            new AdOffer(false, why, null, null, null, null, 0, 0.0);

        /// <summary>
        /// Honest, specific copy for a refusal. NoFill is deliberately NOT phrased as an error:
        /// per-user frequency caps shrink the eligible pool as a day goes on, so "no ads right now"
        /// is ordinary market behaviour and must never read as "something went wrong".
        /// </summary>
        public string RefusalText()
        {
            switch (Reason)
            {
                case AdUnavailableReason.NoFill:        return "No ads available right now. Try again a little later.";
                case AdUnavailableReason.CappedByGame:  return "You have taken today's watches for this. It refreshes later.";
                case AdUnavailableReason.Disabled:      return "Ads are not available in this build.";
                case AdUnavailableReason.NotInitialised:return "Ads are still starting up. Try again in a moment.";
                case AdUnavailableReason.LoadFailed:    return "That ad could not load. Try again a little later.";
                case AdUnavailableReason.Abandoned:     return "No reward was earned - the ad was closed early.";
                default:                                return "Ads are not available right now.";
            }
        }
    }

    /// <summary>
    /// The one gate every rewarded-ad offer passes through. Static: it holds no scene state and a
    /// singleton MonoBehaviour would only add a null check to every call site.
    /// </summary>
    public static class AdGateService
    {
        private const string PrefPrefix = "adgate.";

        /// <summary>
        /// The DAILY-CAP AUTHORITY. Never a rolling window - the placement caps in the data are
        /// authored per local day ("max grants/placement/local-day"). BuildTimerService keeps its own
        /// SEPARATE four-hour rolling window for build skips (owner ruling 2026-08-06) and the two
        /// are ANDed, not merged: they answer different questions and merging them would silently
        /// retire one ruling. Both are conservative, so the stricter one binds.
        /// </summary>
        private static string DayKey()
        {
            // From TimeSource (server-anchored where possible), NOT DateTime.UtcNow - the whole
            // point of WO-912 is that the allowance clock is not the player's to edit.
            var when = DateTimeOffset.FromUnixTimeMilliseconds((long)TimeSource.NowUnixMs()).UtcDateTime;
            return when.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }

        // =====================================================================
        //  Offer
        // =====================================================================

        /// <summary>
        /// Everything a UI needs to decide whether to build an ad offer for this placement.
        /// LEADS WITH AVAILABILITY (WO-912 sec.8.3): the button is not offered unless an ad is
        /// really presentable, rather than failing after the tap.
        /// </summary>
        public static AdOffer Offer(string placementId)
        {
            var placement = AdPlacementCatalog.Placement(placementId);
            if (placement == null || !placement.enabled)
            {
                FlowTrace.Once("Ads", "offer-unknown-" + placementId,
                    $"AdGateService.Offer('{placementId}'): no such ENABLED placement in ad-placements.json. " +
                    "Refusing. A call site naming a placement the data does not carry is a wiring bug, " +
                    "not a player-facing state.");
                return AdOffer.No(AdUnavailableReason.Disabled);
            }

            var reward = AdPlacementCatalog.RewardFor(placement);
            if (reward == null) return AdOffer.No(AdUnavailableReason.Disabled);

            // requiresFlag - the data's own gate. place.build.skip carries "RewardedAdSkip", which
            // is the release blocker; without this check a wired interpreter would have offered ads
            // while ff.rewardedadskip was OFF and every other path refused them.
            if (!FlagAllows(placement.requiresFlag))
                return AdOffer.No(AdUnavailableReason.Disabled);

            int remaining = RemainingToday(placement);
            if (remaining <= 0) return AdOffer.No(AdUnavailableReason.CappedByGame);
            if (HardCapRemaining() <= 0) return AdOffer.No(AdUnavailableReason.CappedByGame);

            double cooldownLeft = CooldownRemaining(placement);
            if (cooldownLeft > 0.0) return AdOffer.No(AdUnavailableReason.CappedByGame);

            // Only now ask the network. Asking it first would spend a fill check on an offer our
            // own rules already refused, and would blur "we said no" with "the network said no" -
            // the distinction WO-912 sec.10.7 calls the single most important launch metric.
            if (!AdServices.Current.IsRewardedReadyFor(placementId))
            {
                AdUnavailableReason why = AdServices.Current.RewardedUnavailableReasonFor(placementId);
                AdServices.Current.PreloadRewarded(placementId);
                return AdOffer.No(why == AdUnavailableReason.None ? AdUnavailableReason.NoFill : why);
            }

            var prompt = placement.prompt;
            return new AdOffer(true, AdUnavailableReason.None,
                prompt != null ? prompt.headline : null,
                prompt != null ? prompt.body : null,
                prompt != null ? prompt.cta : "Watch",
                reward.description,
                remaining, 0.0);
        }

        /// <summary>Convenience for a UI that only wants the yes/no.</summary>
        public static bool IsOffered(string placementId) => Offer(placementId).Available;

        // =====================================================================
        //  Present + grant
        // =====================================================================

        /// <summary>
        /// Presents the placement's ad and, ONLY on a genuine earned-reward callback, applies its
        /// reward and records the watch in the ledger.
        ///
        /// <para><paramref name="contextGrant"/> is for rewards this service cannot apply on its own
        /// because they need a subject - a timeskip needs to know WHICH job. The caller supplies
        /// that action; the covenant, the caps, the ledger and the grant-on-callback rule stay HERE,
        /// so a call site can choose the subject but can never choose the policy. Context-free
        /// rewards (harvest boost, soft currency) are granted internally and ignore this argument.</para>
        ///
        /// <para>Returns true when presentation STARTED. It never means a reward was earned - that
        /// arrives through <paramref name="onComplete"/>, which fires on EVERY path including every
        /// refusal, so a button disabled on the call is never left stuck (CLAUDE.md sec.12).</para>
        /// </summary>
        public static bool Present(string placementId, Action contextGrant, Action<AdShowResult> onComplete)
        {
            AdOffer offer = Offer(placementId);
            if (!offer.Available)
            {
                FlowTrace.Step("Ads", $"Present('{placementId}') refused before presentation: {offer.Reason}.");
                EventTracker.Track("rewarded_ad_unavailable", new
                {
                    placement = placementId,
                    reason = offer.Reason.ToString()
                });
                onComplete?.Invoke(AdShowResult.Unavailable(offer.Reason));
                return false;
            }

            var placement = AdPlacementCatalog.Placement(placementId);
            var reward = AdPlacementCatalog.RewardFor(placement);

            var mgr = RewardedAdManager.Instance;
            if (mgr == null)
            {
                onComplete?.Invoke(AdShowResult.Unavailable(AdUnavailableReason.NotInitialised));
                return false;
            }

            bool rewardApplied = false;
            return mgr.RequestAd(placementId,
                // ── THE ONLY PLACE ANYTHING IS EVER GRANTED ──────────────────
                // RequestAd invokes this from the SDK's genuine earned-reward callback and from
                // nowhere else, and it is one-shot even if the SDK double-fires.
                () =>
                {
                    if (!ApplyReward(placement, reward, contextGrant)) return;
                    rewardApplied = true;
                    RecordWatch(placement);
                },
                result =>
                {
                    // ⛔ WO-1796 — WHICH RAIL DELIVERED THIS REWARD.
                    // This gate is provider-AGNOSTIC: it fires for LevelPlay on Android and
                    // for the Pi Developer Ad Network inside Pi Browser. Until this property
                    // existed the payload carried no network and no provider, so a completion
                    // could not be attributed to a rail at all — and comparing it against
                    // rewarded_ad_impression (emitted ONLY by LevelPlayInitializer) produced a
                    // cross-rail ratio that looked like a completion rate and was not one.
                    // AdServices.Current never returns null (IAdService.cs: `?? NullAdService
                    // .Instance`); the `?.` stays anyway per CLAUDE.md §10.
                    EventTracker.Track("rewarded_ad_completed", new
                    {
                        placement = placementId,
                        outcome = result.Outcome.ToString(),
                        reason = result.Reason.ToString(),
                        rewardApplied,
                        provider = AdServices.Current?.ProviderName ?? "<unknown>"
                    });
                    onComplete?.Invoke(result);
                });
        }

        /// <summary>
        /// Applies one reward. Returns false (granting nothing) when the reward cannot be paid, so
        /// the caller does not burn a ledger slot on a grant that did not happen.
        /// </summary>
        private static bool ApplyReward(AdPlacementDef placement, AdRewardDef reward, Action contextGrant)
        {
            if (reward == null || reward.grant == null)
            {
                FlowTrace.Fail("Ads", $"placement '{placement?.id}' earned a reward with no grant payload - nothing paid.");
                return false;
            }

            string kind = (reward.kind ?? "").ToLowerInvariant();
            var g = reward.grant;

            switch (kind)
            {
                case "timeskip":
                {
                    // _LAW_2: the config is the authority; the JSON number is a mirror we only
                    // check for drift. The caller owns WHICH job, because we cannot know it.
                    float authored = AdSkipSecondsAuthority();
                    if (authored > 0f && g.seconds > 0f && Mathf.Abs(authored - g.seconds) > 0.5f)
                        FlowTrace.Warn("Ads",
                            $"timeskip MIRROR DRIFT: ad-placements.json says {g.seconds:0}s, " +
                            $"BuildTimerConfig.adSkipSeconds says {authored:0}s. Granting the CONFIG value " +
                            "(_LAW_2 - one authority). Fix the JSON mirror; do not fix it here.");

                    if (contextGrant == null)
                    {
                        FlowTrace.Fail("Ads",
                            $"placement '{placement.id}' pays a TIMESKIP but the caller supplied no subject " +
                            "(contextGrant was null) - there is no job to shorten. Nothing granted. " +
                            "A timeskip call site must pass the action that applies it to ITS job.");
                        return false;
                    }
                    Guard.Try("Ads", "timeskip grant", contextGrant);
                    FlowTrace.Step("Ads", $"GRANTED timeskip from '{placement.id}' ({authored:0}s, config authority).");
                    return true;
                }

                case "harvest":
                {
                    float mult = g.multiplier > 1f ? g.multiplier : HarvestBoostService.StandardMultiplier;
                    double secs = g.durationSeconds > 0f ? g.durationSeconds : 3600.0;
                    if (!HarvestBoostService.TryStart(secs, mult, placement.id, out string failure))
                    {
                        FlowTrace.Warn("Ads",
                            $"placement '{placement.id}' earned a harvest boost but it was REFUSED: {failure} " +
                            "The watch is NOT recorded against the cap - the player must not lose an " +
                            "allowance for a reward they did not receive.");
                        return false;
                    }
                    FlowTrace.Step("Ads", $"GRANTED harvest boost from '{placement.id}': {mult:0.##}x for {secs / 60.0:F0} min.");
                    return true;
                }

                case "currency":
                {
                    // The catalog already dropped every banned currency at load. Re-checked here
                    // because this is the rule whose failure ends the ad account: a hand-edited
                    // data file, a hot Reload, or a future code path must all hit the same wall.
                    string currency = (g.currency ?? "").Trim().ToLowerInvariant();
                    if (currency != "coins" && currency != "gold")
                    {
                        FlowTrace.Fail("Ads",
                            $"⛔ _LAW_1: placement '{placement.id}' tried to pay '{currency}', which is not " +
                            "the soft currency. REFUSED at the grant seam. Only coins/gold - a currency with " +
                            "no purchase route - may ever come out of an ad.");
                        return false;
                    }
                    if (g.amount <= 0) return false;

                    var econ = EconomyService.Instance;
                    if (econ == null)
                    {
                        FlowTrace.Fail("Ads",
                            $"placement '{placement.id}' earned {g.amount} coins but EconomyService is not up - " +
                            "NOTHING was paid. The watch is not recorded, so the player can retry.");
                        return false;
                    }
                    econ.AddCoins(g.amount);
                    FlowTrace.Step("Ads", $"GRANTED {g.amount} coins from '{placement.id}'.");
                    return true;
                }

                default:
                    FlowTrace.Fail("Ads",
                        $"placement '{placement.id}' pays reward kind '{reward.kind}', which has no grant path. " +
                        "Nothing paid. (There is deliberately no revive/continue path - owner ruling D7: a " +
                        "battle-continue is COMBAT POWER and the covenant is convenience-only.)");
                    return false;
            }
        }

        // =====================================================================
        //  Ledger
        // =====================================================================

        /// <summary>
        /// Records one GRANTED watch: stamps the cooldown and increments the placement's daily
        /// counter and the global hard-cap counter. Called ONLY after a grant actually landed.
        /// </summary>
        public static void RecordWatch(AdPlacementDef placement)
        {
            if (placement == null) return;
            string day = DayKey();
            SetDouble(PrefPrefix + placement.id + ".lastms", TimeSource.NowUnixMs());
            PlayerPrefs.SetString(PrefPrefix + placement.id + ".day", day);
            PlayerPrefs.SetInt(PrefPrefix + placement.id + ".count", CountToday(placement.id) + 1);
            PlayerPrefs.SetString(PrefPrefix + "hard.day", day);
            PlayerPrefs.SetInt(PrefPrefix + "hard.count", HardCountToday() + 1);
            PlayerPrefs.Save();

            FlowTrace.Step("Ads",
                $"ledger: '{placement.id}' watch #{CountToday(placement.id)} of " +
                $"{(placement.dailyCap > 0 ? placement.dailyCap.ToString(CultureInfo.InvariantCulture) : "unlimited")} today; " +
                $"global {HardCountToday()}/{AdPlacementCatalog.Global.hardDailyCap}. " +
                $"serverAnchored={TimeSource.IsServerAnchored}.");
        }

        /// <summary>Public overload for a call site that holds only the id.</summary>
        public static void RecordWatch(string placementId) =>
            RecordWatch(AdPlacementCatalog.Placement(placementId));

        /// <summary>Watches left today against this placement's own cap. int.MaxValue = unlimited.</summary>
        public static int RemainingToday(AdPlacementDef placement)
        {
            if (placement == null) return 0;
            if (placement.dailyCap <= 0) return int.MaxValue;
            int used = CountToday(placement.id);
            return used >= placement.dailyCap ? 0 : placement.dailyCap - used;
        }

        /// <summary>Watches left today against the GLOBAL sum-across-placements cap.</summary>
        public static int HardCapRemaining()
        {
            int cap = AdPlacementCatalog.Global.hardDailyCap;
            if (cap <= 0) return int.MaxValue;
            int used = HardCountToday();
            return used >= cap ? 0 : cap - used;
        }

        /// <summary>Seconds until this placement's cooldown clears (0 when clear).</summary>
        public static double CooldownRemaining(AdPlacementDef placement)
        {
            if (placement == null) return 0.0;
            int cd = placement.cooldownSeconds > 0
                ? placement.cooldownSeconds
                : AdPlacementCatalog.Global.defaultCooldownSeconds;
            if (cd <= 0) return 0.0;

            double last = GetDouble(PrefPrefix + placement.id + ".lastms");
            if (last <= 0.0) return 0.0;

            double elapsed = (TimeSource.NowUnixMs() - last) / 1000.0;
            if (elapsed < 0.0)
            {
                // Clock moved backwards since the stamp. REFUSE, DON'T PUNISH (WO-912 sec.7.3):
                // clear the stamp so the player is not locked out forever, and log it - a rising
                // rate here is the signal to move the ledger fully server-side. A false positive
                // is ordinary life (timezone, DST, a corrected clock) and must not break a save.
                FlowTrace.Warn("Ads",
                    $"'{placement.id}' cooldown stamp is in the FUTURE by {(-elapsed):F0}s - clock moved " +
                    $"backwards (serverAnchored={TimeSource.IsServerAnchored}). Clearing the stamp rather " +
                    "than stranding the player. The DAILY cap still binds, so this cannot mint watches.");
                PlayerPrefs.DeleteKey(PrefPrefix + placement.id + ".lastms");
                return 0.0;
            }
            double left = cd - elapsed;
            return left > 0.0 ? left : 0.0;
        }

        private static int CountToday(string placementId)
        {
            string stored = PlayerPrefs.GetString(PrefPrefix + placementId + ".day", "");
            if (!string.Equals(stored, DayKey(), StringComparison.Ordinal)) return 0;
            return PlayerPrefs.GetInt(PrefPrefix + placementId + ".count", 0);
        }

        private static int HardCountToday()
        {
            string stored = PlayerPrefs.GetString(PrefPrefix + "hard.day", "");
            if (!string.Equals(stored, DayKey(), StringComparison.Ordinal)) return 0;
            return PlayerPrefs.GetInt(PrefPrefix + "hard.count", 0);
        }

        /// <summary>QA reset of the whole ledger. Never called by gameplay.</summary>
        public static void ClearLedgerForTests()
        {
            var placements = AdPlacementCatalog.Placements;
            for (int i = 0; i < placements.Count; i++)
            {
                PlayerPrefs.DeleteKey(PrefPrefix + placements[i].id + ".lastms");
                PlayerPrefs.DeleteKey(PrefPrefix + placements[i].id + ".day");
                PlayerPrefs.DeleteKey(PrefPrefix + placements[i].id + ".count");
            }
            PlayerPrefs.DeleteKey(PrefPrefix + "hard.day");
            PlayerPrefs.DeleteKey(PrefPrefix + "hard.count");
            PlayerPrefs.Save();
        }

        // =====================================================================
        //  Internals
        // =====================================================================

        /// <summary>
        /// Resolves a placement's requiresFlag by NAME. Only the flags an ad placement is allowed to
        /// depend on are resolvable - an unknown name REFUSES rather than defaulting to true, so a
        /// typo in the data can never open a gate. An EMPTY requiresFlag also refuses, because every
        /// live placement in the file carries one and a blank one is a missing gate, not "no gate".
        /// </summary>
        private static bool FlagAllows(string flagName)
        {
            switch ((flagName ?? "").Trim())
            {
                case "RewardedAdSkip": return FeatureFlags.RewardedAdSkip;
                case "":
                    FlowTrace.Once("Ads", "flag-empty",
                        "A placement carries an EMPTY requiresFlag. REFUSED. Every enabled placement in " +
                        "ad-placements.json names a flag on purpose; a blank one would offer ads while " +
                        "every other path in the game refuses them.");
                    return false;
                default:
                    FlowTrace.Once("Ads", "flag-unknown-" + flagName,
                        $"A placement requires flag '{flagName}', which AdGateService cannot resolve. " +
                        "REFUSED - an unresolvable gate is a closed gate, never an open one.");
                    return false;
            }
        }

        /// <summary>_LAW_2 - the one authority for the timeskip amount.</summary>
        private static float AdSkipSecondsAuthority()
        {
            var svc = BuildTimerService.Instance;
            var cfg = svc != null ? svc.Config : null;
            return cfg != null ? cfg.adSkipSeconds : 0f;
        }

        private static double GetDouble(string key)
        {
            string raw = PlayerPrefs.GetString(key, "");
            if (string.IsNullOrEmpty(raw)) return 0.0;
            return double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out double v) ? v : 0.0;
        }

        private static void SetDouble(string key, double value) =>
            PlayerPrefs.SetString(key, value.ToString("R", CultureInfo.InvariantCulture));
    }
}

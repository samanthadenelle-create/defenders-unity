// =============================================================================
// LevelPlayInitializer — apply privacy, THEN initialise LevelPlay. In that order,
// always.
// -----------------------------------------------------------------------------
// Assembly: DeNelle.Village.AdProviders — its OWN assembly, guarded by
// defineConstraints ["LEVELPLAY_PRESENT"] with a versionDefine on
// com.unity.services.levelplay.
//
// ⛔ WHY ITS OWN ASSEMBLY AND NOT A #if. An asmdef reference to a missing assembly is a
// hard error, so a `#if` inside DeNelle.Village would NOT survive the package being
// absent — the dangling `Unity.LevelPlay` reference errors before any preprocessor
// runs. An assembly whose defineConstraint is unsatisfied is skipped ENTIRELY, its
// references included, which is the only construct that makes this adapter genuinely
// optional. Add the package and the versionDefine fires, the constraint is satisfied,
// and this code compiles itself back in with nothing to remember.
//
// Folder is also load-bearing: AdServiceSeamRegression allows a concrete ad-vendor
// token only under /Ads/Providers/ or /Monetization/Providers/ (WO-912 §10.5). The folder is not cosmetic:
// Game code reaches ads through DeNelle.Core.Ads.IAdService; a vendor name loose in game
// code turns a forced provider swap into a rewrite. This file is the ADAPTER, so it is the
// one place that name belongs.
// This is the ONLY file in the project that knows LevelPlay exists as a concrete
// SDK; AdConsentService (Core) stays provider-agnostic so swapping mediators never
// means re-deciding privacy.
//
// SDK: com.unity.services.levelplay@9.5.1 ("Ads Mediation"), verified installed.
// API verified against the package source, not documentation:
//   Unity.Services.LevelPlay.LevelPlay.Init(string appKey, string userId = null)
//   LevelPlayPrivacySettings.SetGDPRConsent(bool)   <- current; the Dictionary
//   overload SetGDPRConsents is [Obsolete] in this version
//   LevelPlayPrivacySettings.SetCCPA(bool) / SetCOPPA(bool)
//
// ⛔ THE ORDERING RULE. Privacy state MUST be set before Init. Set it after and the
// first impressions have already gone out on the wrong basis, and they cannot be
// un-sent. So this withholds Init entirely until the player has answered, rather
// than initialising on a default and correcting afterwards. Showing no ads for one
// extra screen is fine; serving personalised ads to someone who refused them is not.
//
// GATED OFF BY DEFAULT. FeatureFlags.RewardedAdSkip is the existing ad gate and it
// defaults OFF, so nothing here runs for players until the owner flips it. That is
// deliberate: the app key is live, and a live key plus an accidental init is a real
// impression on a real account.
//
// APP KEY 27850b635 is not a secret. It ships inside the APK and identifies the
// app; it authorises nothing. Hard-coding it is correct - reading it from a config
// the store build cannot ship would just be a way to fail on device.
// =============================================================================

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using UnityEngine;
using Unity.Services.LevelPlay;
using DeNelle.Core;
using DeNelle.Core.Ads;
using DeNelle.Core.Diagnostics;
using DeNelle.Core.Monetization;
using DeNelle.Core.Platform;   // WO-1320: WebGLPiPlatform.IsPiBrowserEnvironment (the one-provider gate)
using DeNelle.Core.UI;
using DeNelle.Core.Analytics;

namespace DeNelle.Village.Monetization
{
    /// <summary>Initialises LevelPlay exactly once, after consent is resolved.</summary>
    public sealed class LevelPlayInitializer : MonoBehaviour, IAdService
    {
        private const string Sys = "LevelPlay";

        /// <summary>LevelPlay dashboard app key for com.denellestudios.echoesofelarion (Android).</summary>
        private const string AppKey = "27850b635";
        private const string RewardedBuildSkipAdUnitId = "2ibxid58jat3sxyd";
        private const string RewardedHarvestAdUnitId = "imk56dcdi5mym2wq";
        private const string RewardedDailyChestAdUnitId = "it6izgx1flbj5rce";
        private const float RetryFloorSeconds = 15f;
        private const float RetryCeilingSeconds = 120f;

        private static LevelPlayInitializer s_instance;
        private static bool s_initStarted;
        private readonly ConcurrentQueue<ImpressionEvent> _impressionRevenue = new ConcurrentQueue<ImpressionEvent>();
        private LevelPlayRewardedAd _rewarded;
        private readonly Dictionary<string, LevelPlayRewardedAd> _rewardedByPlacement = new Dictionary<string, LevelPlayRewardedAd>();
        private readonly Dictionary<string, string> _placementByAdUnit = new Dictionary<string, string>();
        private readonly HashSet<string> _loadingPlacements = new HashSet<string>();
        private string _activePlacement;
        private Action<AdShowResult> _pendingCompletion;
        private bool _presentationSettled;
        private bool _loadingRewarded;
        private bool _showingRewarded;
        private IDisposable _presentationScope;
        private float _retrySeconds = RetryFloorSeconds;
        private AdUnavailableReason _unavailableReason = AdUnavailableReason.NotInitialised;

        private readonly struct ImpressionEvent
        {
            public readonly string Network;
            public readonly string Format;
            public readonly string Unit;
            public readonly string Placement;
            public readonly double RevenueUsd;
            public readonly string Precision;

            public ImpressionEvent(string network, string format, string unit, string placement,
                                   double revenueUsd, string precision)
            {
                Network = network;
                Format = format;
                Unit = unit;
                Placement = placement;
                RevenueUsd = revenueUsd;
                Precision = precision;
            }
        }

        public string ProviderName => "UnityLevelPlay";
        public bool IsInitialised => Ready;
        public bool IsRewardedReady => Ready && !_showingRewarded && _rewarded != null && _rewarded.IsAdReady();
        public AdUnavailableReason RewardedUnavailableReason =>
            IsRewardedReady ? AdUnavailableReason.None : _unavailableReason;
        public bool IsRewardedReadyFor(string placementId) =>
            Ready && !_showingRewarded &&
            _rewardedByPlacement.TryGetValue(placementId ?? string.Empty, out var ad) &&
            ad != null && ad.IsAdReady();
        public AdUnavailableReason RewardedUnavailableReasonFor(string placementId) =>
            IsRewardedReadyFor(placementId) ? AdUnavailableReason.None : _unavailableReason;

        /// <summary>True once the SDK has reported a successful init. Ad call sites may gate on this.</summary>
        public static bool Ready { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (s_instance != null) return;

            // The ad gate owns whether ads exist at all. With it OFF we do not init, do not ask for
            // consent, and do not touch the SDK - an un-asked question is the honest state when the
            // feature is not live.
            if (!FeatureFlags.RewardedAdSkip)
            {
                FlowTrace.Step(Sys, "ads are flagged OFF (ff.rewardedadskip) - LevelPlay not initialised " +
                                    "and no consent prompt shown. Flip the flag to bring the path up.");
                return;
            }

            // ⛔ WO-1320 — THE OTHER HALF OF THE ONE-PROVIDER RULE.
            // AdServices holds exactly ONE Current. Inside Pi Browser the Pi Developer Ad Network
            // is the provider (PiAdProvider), and two initialisers both calling AdServices.Register
            // would make the winner a matter of RuntimeInitializeOnLoadMethod ordering — which is
            // undefined, so the game would serve whichever network happened to finish last. Worse,
            // LevelPlay is an Android-native mediator and there is no LevelPlay inventory inside a
            // Pi Browser WebView at all: it would win the race and then serve nothing.
            //
            // So the two gates are made mutually exclusive rather than arbitrated: PiAdProvider
            // refuses OUTSIDE Pi Browser, and this refuses INSIDE it. Neither can win a race that
            // cannot start, and neither needs to know the other's registration state.
            if (WebGLPiPlatform.IsPiBrowserEnvironment)
            {
                FlowTrace.Step(Sys, "inside Pi Browser - LevelPlay is NOT initialised. The Pi Developer " +
                                    "Ad Network (PiAdProvider) owns AdServices.Current here; two providers " +
                                    "registering would be decided by init order, i.e. by nothing.");
                return;
            }

            var go = new GameObject("LevelPlayInitializer");
            DontDestroyOnLoad(go);
            s_instance = go.AddComponent<LevelPlayInitializer>();
        }

        private void Start()
        {
            Guard.Try(Sys, "begin LevelPlay bring-up", BeginBringUp);
        }

        private void BeginBringUp()
        {
            if (s_initStarted) return;

            if (AdConsentService.IsDecided)
            {
                FlowTrace.Step(Sys, $"consent already on file ({AdConsentService.Describe()}) - " +
                                    "applying privacy then initialising.");
                ApplyPrivacyThenInit();
                return;
            }

            // NOT DECIDED: ask first, init second. Never the other way round.
            FlowTrace.Step(Sys, "no consent on file - showing the prompt BEFORE init. The SDK stays " +
                                "uninitialised until the player answers.");
            AdConsentPanel.Show(onDecided: () =>
            {
                if (!AdConsentService.IsDecided)
                {
                    // The prompt closed without an answer (arbiter swap). Do NOT init on a guess:
                    // an assumed consent is the one failure mode this whole file exists to prevent.
                    FlowTrace.Warn(Sys, "consent prompt closed with no answer - LevelPlay NOT initialised. " +
                                        "It will ask again next launch.");
                    return;
                }
                Guard.Try(Sys, "apply privacy + init after consent", ApplyPrivacyThenInit);
            });
        }

        private void ApplyPrivacyThenInit()
        {
            if (s_initStarted) return;
            s_initStarted = true;

            // ---- PRIVACY FIRST, every branch, no exceptions --------------------
            Guard.Try(Sys, "apply privacy settings", () =>
            {
                bool personalised = AdConsentService.Gdpr == ConsentState.Granted;
                LevelPlayPrivacySettings.SetGDPRConsent(personalised);
                LevelPlayPrivacySettings.SetCCPA(AdConsentService.CcpaOptOut);
                LevelPlayPrivacySettings.SetCOPPA(AdConsentService.ChildDirected);

                FlowTrace.Step(Sys, $"privacy applied BEFORE init: gdprConsent={personalised} " +
                                    $"ccpaOptOut={AdConsentService.CcpaOptOut} " +
                                    $"coppa={AdConsentService.ChildDirected}");
            });

            // ---- then, and only then, init -------------------------------------
            LevelPlay.OnInitSuccess += OnInitSuccess;
            LevelPlay.OnInitFailed  += OnInitFailed;

            FlowTrace.Step(Sys, $"LevelPlay.Init('{AppKey}') - app is set to Temporary in the dashboard " +
                                // WO-1755 / WO-1740 RCA item 6: this literal used to name the store's
                                // CHAIN as well as the store, and that word was one of the 4 live
                                // forbidden-token hits measured in the rejected Play AAB's
                                // global-metadata.dat (offset 928,532). "dApp Store" is not in the gate
                                // vocabulary; the chain name was. Dropping the one word removes the hit
                                // and changes nothing about what the sentence tells a reader.
                                "and live inventory is enabled manually by LevelPlay support (the dApp " +
                                "Store has no https listing URL for auto-verification).");
            LevelPlay.Init(AppKey);
        }

        private void Update()
        {
            // LevelPlay may raise ILRD away from Unity's main thread. Drain primitives here before
            // touching FlowTrace/EventTracker, both of which are Unity-facing infrastructure.
            while (_impressionRevenue.TryDequeue(out ImpressionEvent row))
            {
                FlowTrace.Step(Sys,
                    $"ILRD network={row.Network} format={row.Format} unit={row.Unit} " +
                    $"placement={row.Placement} revenueUsd={row.RevenueUsd:0.########} " +
                    $"precision={row.Precision}");
                EventTracker.Track("rewarded_ad_impression", new
                {
                    network = row.Network,
                    format = row.Format,
                    adUnit = row.Unit,
                    placement = row.Placement,
                    revenueUsd = row.RevenueUsd,
                    precision = row.Precision
                });
            }
        }

        private void OnImpressionDataReady(LevelPlayImpressionData data)
        {
            if (data == null)
            {
                _impressionRevenue.Enqueue(new ImpressionEvent(
                    "<unknown>", "<unknown>", "<unknown>", "<none>", 0d, "<unknown>"));
                return;
            }

            double revenue = data.Revenue ?? 0d;
            _impressionRevenue.Enqueue(new ImpressionEvent(
                data.AdNetwork ?? "<unknown>",
                data.AdFormat ?? "<unknown>",
                data.MediationAdUnitId ?? "<unknown>",
                data.Placement ?? "<none>",
                revenue,
                data.Precision ?? "<unknown>"));
        }

        private void OnInitSuccess(LevelPlayConfiguration config)
        {
            Ready = true;
            FlowTrace.Step(Sys, "LEVELPLAY_INIT_OK - SDK initialised; ad units may load.");
            AdServices.Register(this);
            CreateRewardedAd();
            PreloadRewarded();
        }

        private void OnInitFailed(LevelPlayInitError error)
        {
            Ready = false;
            // FAIL, not Warn: with ads flagged ON, an SDK that never initialised means every ad
            // offer in the game is a button that cannot pay out. That is player-facing.
            FlowTrace.Fail(Sys, $"LEVELPLAY_INIT_FAIL - {error?.ErrorMessage ?? "(no message)"}. " +
                                "No ad will serve this session; every ad-gated offer must degrade " +
                                "to its non-ad path rather than dangle a dead button.");
        }

        private void CreateRewardedAd()
        {
            if (_rewarded != null) return;
            AddRewarded("place.build.skip", RewardedBuildSkipAdUnitId);
            AddRewarded("place.harvest.doubler", RewardedHarvestAdUnitId);
            AddRewarded("place.daily.chest", RewardedDailyChestAdUnitId);
            _rewarded = _rewardedByPlacement["place.build.skip"];
        }

        private void AddRewarded(string placementId, string adUnitId)
        {
            var ad = new LevelPlayRewardedAd(adUnitId);
            ad.OnAdLoaded += OnRewardedLoaded;
            ad.OnAdLoadFailed += OnRewardedLoadFailed;
            ad.OnAdDisplayed += OnRewardedDisplayed;
            ad.OnAdDisplayFailed += OnRewardedDisplayFailed;
            ad.OnAdRewarded += OnRewardedEarned;
            ad.OnAdClosed += OnRewardedClosed;
            ad.OnAdImpressionDataReady += OnImpressionDataReady;
            _rewardedByPlacement[placementId] = ad;
            _placementByAdUnit[adUnitId] = placementId;
        }

        public void PreloadRewarded()
        {
            PreloadRewarded("place.build.skip");
            PreloadRewarded("place.harvest.doubler");
            PreloadRewarded("place.daily.chest");
        }

        public void PreloadRewarded(string placementId)
        {
            if (!Ready || !_rewardedByPlacement.TryGetValue(placementId ?? string.Empty, out var ad) ||
                ad == null || _loadingPlacements.Contains(placementId) || ad.IsAdReady()) return;
            _loadingPlacements.Add(placementId);
            ad.LoadAd();
            FlowTrace.Step(Sys, $"rewarded preload requested placement={placementId}.");
        }

        public void ShowRewarded(Action<AdShowResult> onComplete)
        {
            ShowRewarded("place.build.skip", onComplete);
        }

        public void ShowRewarded(string placementId, Action<AdShowResult> onComplete)
        {
            if (!IsRewardedReadyFor(placementId))
            {
                onComplete?.Invoke(AdShowResult.Unavailable(RewardedUnavailableReasonFor(placementId)));
                PreloadRewarded(placementId);
                return;
            }

            if (_pendingCompletion != null)
            {
                onComplete?.Invoke(AdShowResult.Unavailable(AdUnavailableReason.LoadFailed));
                return;
            }

            _pendingCompletion = onComplete;
            _activePlacement = placementId;
            _presentationSettled = false;
            _showingRewarded = true;
            _unavailableReason = AdUnavailableReason.None;
            // LevelPlay's native full-screen activity triggers OnApplicationPause(true) on
            // Android. Keep the invoking PanelManager caller alive beneath it; this scope owns no
            // navigation and is released on both close and display failure.
            _presentationScope = PauseGate.BeginExternalPresentation("rewarded ad " + placementId);
            try
            {
                _rewardedByPlacement[placementId].ShowAd();
            }
            catch (Exception ex)
            {
                SettleRewarded(AdShowResult.Unavailable(AdUnavailableReason.LoadFailed));
                EndRewardedPresentation();
                FlowTrace.Fail(Sys, "rewarded ShowAd threw placement=" + placementId + ": " + ex.Message);
                PreloadRewarded(placementId);
            }
        }

        private void OnRewardedLoaded(LevelPlayAdInfo info)
        {
            _loadingRewarded = false;
            RemoveLoadingByUnit(info?.AdUnitId);
            _retrySeconds = RetryFloorSeconds;
            _unavailableReason = AdUnavailableReason.None;
            FlowTrace.Step(Sys, $"REWARDED_READY unit={info?.AdUnitId ?? RewardedBuildSkipAdUnitId}");
        }

        private void OnRewardedLoadFailed(LevelPlayAdError error)
        {
            _loadingRewarded = false;
            RemoveLoadingByUnit(error?.AdUnitId);
            _unavailableReason = error != null && error.ErrorCode == 509
                ? AdUnavailableReason.NoFill
                : AdUnavailableReason.LoadFailed;
            FlowTrace.Warn(Sys, $"rewarded load failed code={error?.ErrorCode} " +
                                $"reason={_unavailableReason}: {error?.ErrorMessage ?? "<none>"}; " +
                                $"retry in {_retrySeconds:0}s.");
            Invoke(nameof(PreloadRewarded), _retrySeconds);
            _retrySeconds = Mathf.Min(_retrySeconds * 2f, RetryCeilingSeconds);
        }

        private void OnRewardedDisplayed(LevelPlayAdInfo info) =>
            FlowTrace.Step(Sys, $"rewarded displayed network={info?.AdNetwork ?? "<unknown>"}.");

        private void OnRewardedDisplayFailed(LevelPlayAdInfo info, LevelPlayAdError error)
        {
            if (!IsCallbackForActivePresentation(info, "display-failed")) return;
            string placement = _activePlacement;
            SettleRewarded(AdShowResult.Unavailable(AdUnavailableReason.LoadFailed));
            EndRewardedPresentation();
            FlowTrace.Fail(Sys, $"rewarded display failed code={error?.ErrorCode}: " +
                                (error?.ErrorMessage ?? "<none>"));
            PreloadRewarded(placement);
        }

        private void OnRewardedEarned(LevelPlayAdInfo info, LevelPlayReward reward)
        {
            if (!IsCallbackForActivePresentation(info, "earned")) return;
            if (_presentationSettled)
            {
                FlowTrace.Warn(Sys, $"duplicate rewarded callback ignored unit={info?.AdUnitId ?? "<unknown>"} " +
                                    $"placement={_activePlacement ?? "<none>"}. One presentation may settle once.");
                return;
            }
            FlowTrace.Step(Sys, $"REWARDED_EARNED network={info?.AdNetwork ?? "<unknown>"} " +
                                $"sdkReward={reward?.Amount} {reward?.Name ?? "<unnamed>"}.");
            SettleRewarded(AdShowResult.Earned());
        }

        private void OnRewardedClosed(LevelPlayAdInfo info)
        {
            if (!IsCallbackForActivePresentation(info, "closed")) return;
            if (!_presentationSettled) SettleRewarded(AdShowResult.Dismissed());
            string placement = _activePlacement;
            EndRewardedPresentation();
            PreloadRewarded(placement);
        }

        private bool IsCallbackForActivePresentation(LevelPlayAdInfo info, string callbackName)
        {
            if (!_showingRewarded || string.IsNullOrEmpty(_activePlacement))
            {
                FlowTrace.Warn(Sys, $"stale rewarded {callbackName} callback ignored " +
                                    $"unit={info?.AdUnitId ?? "<unknown>"}; no presentation is active.");
                return false;
            }

            string callbackPlacement = null;
            if (info == null || string.IsNullOrEmpty(info.AdUnitId) ||
                !_placementByAdUnit.TryGetValue(info.AdUnitId, out callbackPlacement) ||
                !string.Equals(callbackPlacement, _activePlacement, StringComparison.Ordinal))
            {
                FlowTrace.Warn(Sys, $"cross-unit rewarded {callbackName} callback ignored " +
                                    $"unit={info?.AdUnitId ?? "<unknown>"} " +
                                    $"callbackPlacement={callbackPlacement ?? "<unknown>"} " +
                                    $"activePlacement={_activePlacement}.");
                return false;
            }

            return true;
        }

        private void RemoveLoadingByUnit(string adUnitId)
        {
            if (string.IsNullOrEmpty(adUnitId)) { _loadingPlacements.Clear(); return; }
            if (_placementByAdUnit.TryGetValue(adUnitId, out string placement))
                _loadingPlacements.Remove(placement);
        }

        private void SettleRewarded(AdShowResult result)
        {
            if (_presentationSettled)
            {
                FlowTrace.Warn(Sys, $"duplicate rewarded settlement ignored outcome={result} " +
                                    $"placement={_activePlacement ?? "<none>"}.");
                return;
            }
            _presentationSettled = true;
            Action<AdShowResult> callback = _pendingCompletion;
            _pendingCompletion = null;
            Guard.Try(Sys, "settle rewarded callback", () => callback?.Invoke(result));
        }

        private void EndRewardedPresentation()
        {
            if (_presentationScope != null) { _presentationScope.Dispose(); _presentationScope = null; }
            _pendingCompletion = null;
            _showingRewarded = false;
            _presentationSettled = false;
            _activePlacement = null;
        }

        private void OnDestroy()
        {
            if (_presentationScope != null) { _presentationScope.Dispose(); _presentationScope = null; }
            AdServices.Unregister(this);
            CancelInvoke();
            foreach (LevelPlayRewardedAd ad in _rewardedByPlacement.Values)
            {
                if (ad == null) continue;
                ad.OnAdLoaded -= OnRewardedLoaded;
                ad.OnAdLoadFailed -= OnRewardedLoadFailed;
                ad.OnAdDisplayed -= OnRewardedDisplayed;
                ad.OnAdDisplayFailed -= OnRewardedDisplayFailed;
                ad.OnAdRewarded -= OnRewardedEarned;
                ad.OnAdClosed -= OnRewardedClosed;
                ad.OnAdImpressionDataReady -= OnImpressionDataReady;
            }
            LevelPlay.OnInitSuccess -= OnInitSuccess;
            LevelPlay.OnInitFailed  -= OnInitFailed;
            if (s_instance == this) s_instance = null;
        }
    }
}

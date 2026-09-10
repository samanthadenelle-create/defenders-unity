// =============================================================================
// CoreServices — cross-assembly service registry (WO-41 + WO-43).
// -----------------------------------------------------------------------------
// A static registry living in DeNelle.Core (referenced by every module) that
// lets implementing modules register concrete services behind Core-defined
// interfaces, so consumers never need an assembly reference to the implementor.
//
// Slots (8):
//   Hud               — IVillageHud          (WO-41, DeNelle.HUD)
//   HudModel          — IHudModel            (WO-541, HUD model layer)
//   Population        — IPopulationService   (DeNelle.Core.Population)
//   Audio             — IAudioService        (WO-41, DeNelle.Audio)
//   Jupiter           — IJupiterService      (WO-43, DeNelle.Web3)
//   WalletSigner      — IWalletSigner        (wallet signer service)
//   SceneLinkResolver — ISceneLinkResolver   (DeNelle.Core.World)
//   VillageBridge     — IVillageBridge       (WO-1510, DeNelle.Village)
//
// Each slot follows the same Register/Unregister pattern: the concrete
// MonoBehaviour calls Register in Awake and Unregister in OnDestroy.
// Callers MUST null-check (e.g. CoreServices.Hud?.SetWave(n)).
// =============================================================================

using DeNelle.Core.Audio;
using DeNelle.Core.HUD;
using DeNelle.Core.HudModel;
using DeNelle.Core.Population;
using DeNelle.Core.Web3;
using UnityEngine;

namespace DeNelle.Core
{
    /// <summary>
    /// A static service registry for cross-assembly access to game-wide
    /// services. Slots are populated at runtime by the implementing module
    /// and consumed through Core-defined interfaces.
    /// </summary>
    public static class CoreServices
    {
        // ── HUD (WO-41) ───────────────────────────────────────────────────────
        /// <summary>
        /// The active village HUD, or null when no VillageHudController is
        /// present in the loaded scenes. Always null-check before use.
        /// </summary>
        public static IVillageHud Hud { get; private set; }

        /// <summary>
        /// Raised right after a non-null <see cref="Hud"/> registers. Exists so a consumer that
        /// arrives BEFORE the HUD can wait for it instead of giving up (WO-1024).
        ///
        /// <para>The case that forced it: WaveFeedbackDirector.EnsureWallRepairInstalled deferred
        /// when the HUD had not registered yet, and its only retry was the wave-cleared event. In
        /// the HUB, where a wave may never run, that retry never came - so the enabled
        /// WallRepairController never installed and tap-to-repair did not exist for the whole
        /// session. A poll would have worked; an event is honest about what is actually being
        /// waited on.</para>
        ///
        /// <para>Subscribers MUST unsubscribe once satisfied - this is a static event and holds
        /// its handlers across scene loads.</para>
        /// </summary>
        public static event System.Action<IVillageHud> HudRegistered;

        /// <summary>Registers the village HUD. Called by VillageHudController.Awake.
        /// Main-thread only (no locking) — registrations happen in Awake/OnDestroy.</summary>
        public static void RegisterHud(IVillageHud hud)
        {
            if (Hud != null && !ReferenceEquals(Hud, hud))
            {
                DeNelle.Core.Diagnostics.FlowTrace.Warn("CoreSvc", "REPLACING existing IVillageHud registration (double-register / stale host?).");
                Debug.LogWarning("[CoreServices] Replacing existing IVillageHud registration.");
            }
            Hud = hud;
            DeNelle.Core.Diagnostics.FlowTrace.Step("CoreSvc", hud != null ? "IVillageHud registered." : "IVillageHud registered as NULL.");

            // Guarded: a throwing subscriber must never break HUD registration itself - the HUD
            // is load-bearing for every screen, the notification is a courtesy to late arrivals.
            if (hud != null)
            {
                DeNelle.Core.Diagnostics.Guard.Try("CoreSvc", "raise HudRegistered",
                    () => HudRegistered?.Invoke(hud));
            }
        }

        /// <summary>Unregisters the village HUD. Called by VillageHudController.OnDestroy.</summary>
        public static void UnregisterHud(IVillageHud hud) { if (ReferenceEquals(Hud, hud)) Hud = null; }

        // ── HUD model layer (WO-541) ──────────────────────────────────────────
        /// <summary>
        /// The active HUD model facade (read-only data + Changed events), or null
        /// when no HudModelHost is present in the loaded scenes. Producers write the
        /// models; views read them. Always null-check before use.
        /// </summary>
        public static IHudModel HudModel { get; private set; }

        /// <summary>Registers the HUD model facade. Called by HudModelHost.Awake (WO-541 Stage 2).
        /// Main-thread only (no locking) — registrations happen in Awake/OnDestroy.</summary>
        public static void RegisterHudModel(IHudModel m)
        {
            if (HudModel != null && !ReferenceEquals(HudModel, m))
                Debug.LogWarning("[CoreServices] Replacing existing IHudModel registration.");
            HudModel = m;
            DeNelle.Core.Diagnostics.FlowTrace.Step("HUD", "HudModel registered");
        }

        /// <summary>Unregisters the HUD model facade. Called by HudModelHost.OnDestroy.</summary>
        public static void UnregisterHudModel(IHudModel m) { if (ReferenceEquals(HudModel, m)) HudModel = null; }

        // ── Population growth (WO-587) ────────────────────────────────────────
        /// <summary>
        /// The active Population growth service, or null when no PopulationService is
        /// present (it self-bootstraps via PopulationBootstrap). Drives Echo workforce
        /// slot unlocks from milestones. Always null-check before use.
        /// </summary>
        public static IPopulationService Population { get; private set; }

        /// <summary>Registers the Population service. Called by PopulationService.Awake.
        /// Main-thread only (no locking) — registrations happen in Awake/OnDestroy.</summary>
        public static void RegisterPopulation(IPopulationService svc)
        {
            if (Population != null && !ReferenceEquals(Population, svc))
                Debug.LogWarning("[CoreServices] Replacing existing IPopulationService registration.");
            Population = svc;
        }

        /// <summary>Unregisters the Population service. Called by PopulationService.OnDestroy.</summary>
        public static void UnregisterPopulation(IPopulationService svc) { if (ReferenceEquals(Population, svc)) Population = null; }

        // ── Audio (WO-41) ─────────────────────────────────────────────────────
        /// <summary>
        /// The active audio service, or null before AudioBootstrap has run.
        /// Always null-check before use.
        /// </summary>
        public static IAudioService Audio { get; private set; }

        /// <summary>Registers the audio service. Called by AudioService.Awake.
        /// Main-thread only (no locking) — registrations happen in Awake/OnDestroy.</summary>
        public static void RegisterAudio(IAudioService audio)
        {
            if (Audio != null && !ReferenceEquals(Audio, audio))
            {
                DeNelle.Core.Diagnostics.FlowTrace.Warn("CoreSvc", "REPLACING existing IAudioService registration (double-register / stale host?).");
                Debug.LogWarning("[CoreServices] Replacing existing IAudioService registration.");
            }
            Audio = audio;
            DeNelle.Core.Diagnostics.FlowTrace.Step("CoreSvc", audio != null ? "IAudioService registered." : "IAudioService registered as NULL.");
        }

        /// <summary>Unregisters the audio service. Called by AudioService.OnDestroy.</summary>
        public static void UnregisterAudio(IAudioService audio) { if (ReferenceEquals(Audio, audio)) Audio = null; }

        // ── Jupiter swap service (WO-43) ─────────────────────────────────────
        // WO-1377 (2026-09-09): the SLOT ITSELF is compiled out under GOOGLE_PLAY, not
        // just its message text. IL2CPP ships member names, so `Jupiter`,
        // `RegisterJupiter` and `UnregisterJupiter` land in the Play AAB's
        // global-metadata.dat as identifiers even though the only registrant
        // (JupiterSwapService, DeNelle.Web3) is excluded by that assembly's
        // "!GOOGLE_PLAY" constraint. There is no caller of this slot in any assembly
        // that ships on Play, so removing it there costs nothing.
        // ⛔ NOT deleted — on the dApp/Seeker variant this slot is live and required.
#if !GOOGLE_PLAY
        /// <summary>
        /// The active Jupiter swap service, or null when no swap host is present
        /// in the loaded scenes. Always null-check before use.
        /// </summary>
        public static IJupiterService Jupiter { get; private set; }

        /// <summary>Registers the Jupiter swap service. Called by JupiterSwapService.Awake.</summary>
        public static void RegisterJupiter(IJupiterService svc)
        {
            if (Jupiter != null && Jupiter != svc)
                // WO-1363 added a channel-neutral GOOGLE_PLAY arm here so the swap-service
                // NAME would not appear in the Play artifact's log strings. WO-1377 makes
                // that arm unreachable by removing the whole slot on Play, so the single
                // remaining branch is the dApp-lane one. Still warns — never a silent
                // replace (§12).
                Debug.LogWarning("[CoreServices] Replacing existing IJupiterService registration.");
            Jupiter = svc;
        }

        /// <summary>Unregisters the Jupiter swap service. Called by JupiterSwapService.OnDestroy.</summary>
        public static void UnregisterJupiter(IJupiterService svc)
        {
            if (Jupiter == svc) Jupiter = null;
        }
#endif  // !GOOGLE_PLAY  (WO-1377 — the Jupiter slot, member names and all)

        // ── Wallet signer (WO-121 backend save-auth) ─────────────────────────
        /// <summary>
        /// The active wallet message-signer, or null when no wallet is
        /// connected. Registered by WalletService (DeNelle.Wallet) on Connect,
        /// unregistered on Disconnect. GameStateService resolves it to sign the
        /// backend save/load auth nonce. Always null-check; even when non-null,
        /// check <see cref="IWalletSigner.CanSign"/> before signing (the devnet
        /// stub registers but cannot sign).
        /// </summary>
        public static IWalletSigner WalletSigner { get; private set; }

        /// <summary>Registers the wallet signer. Called by WalletService when a wallet connects.</summary>
        public static void RegisterWalletSigner(IWalletSigner signer)
        {
            if (WalletSigner != null && WalletSigner != signer)
                Debug.Log("[CoreServices] Replacing existing IWalletSigner registration.");
            WalletSigner = signer;
        }

        /// <summary>Unregisters the wallet signer. Called by WalletService on disconnect.</summary>
        public static void UnregisterWalletSigner(IWalletSigner signer)
        {
            if (ReferenceEquals(WalletSigner, signer)) WalletSigner = null;
        }

        // ── Scene-link resolver (WO1) ─────────────────────────────────────────
        /// <summary>
        /// The active data-driven scene-link resolver, or null when no
        /// SceneLinkResolverHost is present (it self-bootstraps). Routes the hero
        /// across the world graph (Castle → Outpost1 → Dungeon →
        /// Outpost2 + portal). Always null-check before use
        /// (e.g. CoreServices.SceneLinkResolver?.TravelTo(id)).
        /// </summary>
        public static DeNelle.Core.World.ISceneLinkResolver SceneLinkResolver { get; private set; }

        /// <summary>Registers the scene-link resolver. Called by SceneLinkResolverHost.Awake.
        /// Main-thread only (no locking) — registrations happen in Awake/OnDestroy.</summary>
        public static void RegisterSceneLinkResolver(DeNelle.Core.World.ISceneLinkResolver resolver)
        {
            if (SceneLinkResolver != null && !ReferenceEquals(SceneLinkResolver, resolver))
            {
                DeNelle.Core.Diagnostics.FlowTrace.Warn("CoreSvc", "REPLACING existing ISceneLinkResolver registration (double-register / stale host?).");
                Debug.LogWarning("[CoreServices] Replacing existing ISceneLinkResolver registration.");
            }
            SceneLinkResolver = resolver;
            DeNelle.Core.Diagnostics.FlowTrace.Step("CoreSvc", resolver != null ? "ISceneLinkResolver registered." : "ISceneLinkResolver registered as NULL.");
        }

        /// <summary>Unregisters the scene-link resolver. Called by SceneLinkResolverHost.OnDestroy.</summary>
        public static void UnregisterSceneLinkResolver(DeNelle.Core.World.ISceneLinkResolver resolver)
        {
            if (ReferenceEquals(SceneLinkResolver, resolver)) SceneLinkResolver = null;
        }

        // ── Village bridge (WO-1510) ──────────────────────────────────────────
        /// <summary>
        /// The DeNelle.Village seam — hero pose, hero input suppression, wave-clear
        /// notification — or null when the Village assembly is not loaded (headless /
        /// Core-only contexts). This slot REPLACES the four
        /// <c>Type.GetType("DeNelle.Village…")</c> sites that used to live inside Core
        /// (SceneRouter x2, PersistenceBridge, BreakCaptureHarness); Core now names no
        /// Village type at all. Always null-check before use.
        /// </summary>
        public static DeNelle.Core.Bridging.IVillageBridge VillageBridge { get; private set; }

        /// <summary>Registers the Village bridge. Called by VillageBridgeService's
        /// RuntimeInitializeOnLoadMethod installer (DeNelle.Village).</summary>
        public static void RegisterVillageBridge(DeNelle.Core.Bridging.IVillageBridge bridge)
        {
            if (VillageBridge != null && !ReferenceEquals(VillageBridge, bridge))
            {
                DeNelle.Core.Diagnostics.FlowTrace.Warn("CoreSvc", "REPLACING existing IVillageBridge registration (double-register / stale host?).");
                Debug.LogWarning("[CoreServices] Replacing existing IVillageBridge registration.");
            }
            VillageBridge = bridge;
            DeNelle.Core.Diagnostics.FlowTrace.Step("CoreSvc", bridge != null ? "IVillageBridge registered." : "IVillageBridge registered as NULL.");
        }

        /// <summary>Unregisters the Village bridge.</summary>
        public static void UnregisterVillageBridge(DeNelle.Core.Bridging.IVillageBridge bridge)
        {
            if (ReferenceEquals(VillageBridge, bridge)) VillageBridge = null;
        }
    }
}

// =============================================================================
// SceneRouter — the Unity analog of React-Router's <Routes> / useNavigate
// -----------------------------------------------------------------------------
// Port of the App.tsx route table (spec §3.3). A static class. Per the v2
// port-spec Part 3, the four canonical Unity scenes are Title, Village,
// Dungeon_HealersCottage and ATBBattle. Week 1 ships Title and Village; the
// dungeon/battle entry points are stubbed for Weeks 2+.
//
// INTRO FLOW (owner-acceptance-checklist "Intro & first-run flow"): the path
// from the Title scene into the world runs through two onboarding select
// screens —
//   Title (studio bumper) -> HeroSelect -> PetSelect -> Village
// HeroSelect and PetSelect are their own scenes (DeNelle.Onboarding). The
// Title "Start" button calls GoHeroSelect; HeroSelect's confirm calls
// GoPetSelect; PetSelect's confirm calls GoVillage. A returning player whose
// save already has a hero + starter pet may skip straight to the village —
// each select controller checks GameState and self-skips (see those files).
//
//   LoadScene          — thin synchronous SceneManager.LoadScene wrapper.
//   LoadSceneWithFade  — async UniTask: fade a black overlay → LoadSceneAsync →
//                        fade back. Never `async void` (Part-3 mandate).
//
// Scene-transition MUSIC (the App.tsx AudioBootstrap) is intentionally NOT here
// — an Audio/Core director listens for scene loads (Village owns village BGM,
// every other scene gets the `title` track). Keeping that out of the router
// preserves module separation: the router loads scenes, the audio director
// reacts.
// =============================================================================

using System;
using System.Collections;          // IEnumerator — the hero-content prewarm pump (WO-1187)
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;
using DeNelle.Core.Diagnostics;

namespace DeNelle.Core
{
    /// <summary>
    /// WO-770.3 (fixes D4): a Core-level battle-result carrier. A module that only
    /// references <c>DeNelle.Core</c> (the dungeon) cannot see the engine's own
    /// <c>DeNelle.BattleATB.BattleOutcome</c>, so this Core mirror lets the dungeon resume
    /// tell a won ATB round-trip from a lost one. Named distinctly (NOT "BattleOutcome")
    /// on purpose — an identically-named Core enum would collide (CS0104) with the engine
    /// enum inside BattleController, which imports both namespaces.
    /// </summary>
    public enum BattleResultKind { None, Victory, Defeat }

    /// <summary>Hand-off parameters for the ATB battle scene (detailed in the Week-2 spec).</summary>
    [Serializable]
    public sealed class BattleParams
    {
        /// <summary>The wave that breached the town — victory loot scales off this.</summary>
        public int Wave;
        /// <summary>3D-layer ids of the enemies that breached and started the battle.</summary>
        public string[] BreachedIds = Array.Empty<string>();
        /// <summary>Engine unit-ids of the pets that fought (for the pity reward).</summary>
        public string[] ParticipatingPetIds = Array.Empty<string>();

        /// <summary>
        /// The scene the battle returns to once the result settles (BUG-008 fix).
        /// A village breach leaves this at the default (<see cref="SceneRouter.Village"/>);
        /// a dungeon encounter sets it to <see cref="SceneRouter.DungeonHealersCottage"/>
        /// so the ATB round-trip lands back in the dungeon, not the village.
        /// Empty / null also resolves to the village default.
        /// </summary>
        public string ReturnScene = SceneRouter.Village;

        /// <summary>
        /// WO-770.3 (fixes D4): the settled outcome of the ATB round-trip, stamped by
        /// <see cref="BattleParams"/>'s reader — actually written by
        /// <c>BattleController.HandleOutcome</c> just before the scene hand-back. The dungeon
        /// resume (<c>DungeonController.ResolvePendingEncounter</c>) reads it to END the run on
        /// a defeat instead of assuming victory. Default <see cref="BattleResultKind.None"/>;
        /// a village breach IGNORES it (the village return is by <see cref="ReturnScene"/>,
        /// and the Heart/building settle is driven off the ATB runtime result, not this).
        /// </summary>
        public BattleResultKind LastOutcome = BattleResultKind.None;
    }

    /// <summary>
    /// RETURN-POINT (return-point feature): the scene + hero pose the player left
    /// when a battle launched, so the ATB round-trip lands back exactly where they
    /// fought from — not the Village2 default. Stashed by
    /// <see cref="SceneRouter.GoBattle"/> on a STATIC (it must survive the ATB scene's
    /// Single load tearing the whole world down — same handoff lifetime as
    /// <see cref="SceneRouter.PendingBattle"/>), read by BattleController to choose the
    /// return scene, and consumed once on the far-side load to warp the hero home.
    /// </summary>
    [Serializable]
    public sealed class ReturnPoint
    {
        /// <summary>Active scene name the battle was launched from.</summary>
        public string Scene;
        /// <summary>Hero world position at launch (skipped/zero if no hero was found).</summary>
        public Vector3 Position;
        /// <summary>Hero heading (Y euler) at launch, for restoring facing.</summary>
        public float Yaw;
    }

    /// <summary>
    /// Hand-off parameters for the "Defend the Tower" (Patricia Light) scene.
    /// Stashed on <see cref="SceneRouter.PendingPatriciaLight"/> by
    /// <see cref="SceneRouter.GoPatriciaLight"/> and read by the scene's
    /// <c>PatriciaLightController</c> on the far side — exactly like
    /// <see cref="BattleParams"/> / <see cref="SceneRouter.PendingBattle"/>.
    /// </summary>
    [Serializable]
    public sealed class PatriciaLightParams
    {
        /// <summary>The wave that breached — difficulty / reward scale off this.</summary>
        public int Wave;

        /// <summary>The scene the mode returns to once it resolves (default: Village).</summary>
        public string ReturnScene = SceneRouter.Village;
    }

    /// <summary>Static scene-navigation surface — the React route table's port.</summary>
    public static class SceneRouter
    {
        // ── Canonical scene names (v2 port-spec Part 3) ──────────────────────
        /// <summary>The landing / onboarding scene (React `/`).</summary>
        public const string Title = "Title";
        /// <summary>
        /// The hero-select screen — pick Mage / Knight / Ranger. Shown once,
        /// between the Title scene's studio bumper and the Village, on the
        /// first-run intro path. See <see cref="GoHeroSelect"/>.
        /// </summary>
        public const string HeroSelect = "HeroSelect";
        /// <summary>
        /// The pet-select screen — pick one of the three starter Wardens.
        /// Shown right after <see cref="HeroSelect"/> on the intro path.
        /// </summary>
        public const string PetSelect = "PetSelect";
        /// <summary>The village tower-defense scene (React `/village`).</summary>
        public const string Village = "Village2";
        /// <summary>
        /// The Central Castle Hub (MainCastle_Hall or Main_Castle_Overworld) — the player's home base and the
        /// new first stop after onboarding. Built by
        /// <c>DeNelle.Editor.CastleHubBuilder.BuildCastleHub</c>. The player arrives
        /// here, then travels out to <see cref="Village"/> for the tower-defense loop.
        /// <para>
        /// WO-608: flag-aware. When <c>ff.MergedWorld</c> is ON the home hub is the
        /// single merged <c>Main_Castle_Overworld</c> scene (castle + outer world in one
        /// continuous navmesh, no additive stream / seam warp); when OFF this stays the
        /// legacy two-scene <c>MainCastle_Hall</c>. A property, not
        /// a const, so it can flip at runtime — verified nothing uses it in a const/case/
        /// attribute context (only GoCastle's fade-load + DevPanel JumpScene, both runtime).
        /// </para>
        /// </summary>
        public static string Castle => DeNelle.Core.FeatureFlags.MergedWorld
            ? CastleCandidates[0]
            : CastleCandidates[1];

        /// <summary>
        /// EVERY value <see cref="Castle"/> can resolve to — [0] merged (ff.MergedWorld ON),
        /// [1] the legacy two-scene hub. THE ONLY PLACE either name is spelled out.
        /// <para>
        /// WO-1112: exists because <see cref="Castle"/> is flag-dependent, so an oracle that
        /// asserts against the RESOLVED value only proves the branch the gate machine happens
        /// to be flagged into. A guard that must hold for the home hub in BOTH configurations
        /// (e.g. <c>TowerRespawnRegression</c> vs <c>BaseLayoutLoader._hubScenesNoBaseLayout</c>)
        /// iterates this instead of re-typing the names — which is exactly how three separate
        /// gates ended up pinned to the retired <c>MainCastle_Hall</c> literal while the live hub
        /// moved on. Add a hub-scene variant HERE and every gate follows for free.
        /// </para>
        /// </summary>
        public static readonly string[] CastleCandidates = { "Main_Castle_Overworld", "MainCastle_Hall" };
        /// <summary>The ATB Last-Stand battle scene (React global AtbBattleHost).</summary>
        public const string ATBBattle = "ATBBattle";

        /// <summary>
        /// The "Defend the Tower" real-time shooter scene (WO-47 "Patricia Light").
        /// The breach-time alternative to <see cref="ATBBattle"/>: a third-person
        /// tower-defense stand where the hero fires from the spire while pets
        /// attack or repair. Built by the editor scene-builder
        /// <c>DeNelle.Editor.PatriciaLightSceneBuilder.BuildScene</c> and routed via
        /// <see cref="GoPatriciaLight"/>.
        /// </summary>
        public const string PatriciaLight = "PatriciaLightMode";

        // ── Raid bases (WO-453 Step 4 — the troop DEPLOY/RALLY/RETREAT target) ──
        // Config-generated enemy garrisons baked to RaidBase_<id>.unity. The player
        // sails out from the castle hub to ASSAULT one with their trained army; the
        // raid HUD (RaidDeployController) deploys troops, and a loss/retreat evacs
        // home via GoCastle. These three first-playable raid scenes are registered
        // in EditorBuildSettings so IsSceneRegistered passes for GoRaid.
        /// <summary>Small raider camp — the easiest first-playable raid target.</summary>
        public const string RaidBaseRaiderCampSmall  = "RaidBase_raider_camp_small";
        /// <summary>A fortified garrison — mid-tier raid target.</summary>
        public const string RaidBaseFortifiedGarrison = "RaidBase_fortified_garrison";
        /// <summary>A mage enclave — the toughest of the three first-playable raids.</summary>
        public const string RaidBaseMageEnclave       = "RaidBase_mage_enclave";
        /// <summary>The highest raid — 3-star clear captures the personal town (owner 2026-09-11).</summary>
        public const string RaidBaseIronBastion       = "RaidBase_IronBastion";

        /// <summary>The Week-1 starter dungeon scene name.</summary>
        public const string DungeonHealersCottage   = "Dungeon_HealersCottage";
        public const string DungeonFolksGranary     = "Dungeon_FolksGranary";
        public const string DungeonSunkenBellTower  = "Dungeon_SunkenBellTower";
        public const string DungeonWolfwardensVigil = "Dungeon_WolfwardensVigil";
        public const string DungeonFrostStair       = "Dungeon_FrostStair";
        public const string DungeonGlassCathedral   = "Dungeon_GlassCathedral";
        public const string DungeonApothecarysVault = "Dungeon_ApothecarysVault";

        /// <summary>The default fade duration, in seconds, for <see cref="LoadSceneWithFade"/>.</summary>
        public const float DefaultFadeSeconds = 0.4f;

        /// <summary>
        /// The dungeon scene name for a dungeon id — React `/dungeon/:id`.
        /// Week 1 ships <c>Dungeon_HealersCottage</c>.
        /// </summary>
        public static string Dungeon(string dungeonId) => $"Dungeon_{dungeonId}";

        /// <summary>
        /// Optional full-screen fade overlay used by <see cref="LoadSceneWithFade"/>.
        /// Wired up by the Core bootstrap (a DontDestroyOnLoad canvas). When null,
        /// <see cref="LoadSceneWithFade"/> falls back to a plain async load.
        /// </summary>
        public static ISceneFader Fader { get; set; }

        // =====================================================================
        //  Synchronous load
        // =====================================================================

        /// <summary>
        /// Loads a scene synchronously. Unity scene names are compile-time, so an
        /// unknown name is a programmer error — this logs an error rather than
        /// silently failing (there is no React-style catch-all 404).
        /// </summary>
        public static void LoadScene(string sceneName)
        {
            FlowTrace.Step("SceneRouter", $"LoadScene(sync) name='{sceneName ?? "<null>"}'");
            if (!IsSceneRegistered(sceneName))
            {
                FlowTrace.Fail("SceneRouter", $"LoadScene ABORTED: '{sceneName ?? "<null>"}' not in Build Settings — hero stranded on current scene.");
                Debug.LogError($"[SceneRouter] Scene '{sceneName}' is not in Build Settings — load aborted.");
                return;
            }
            // WO1: local save before synchronous transitions (no await available here;
            // the background delta-sync timer will push the backend diff shortly after).
            DeNelle.Core.State.GameStateService.Instance?.Save();
            FlowTrace.Step("SceneRouter", $"LoadScene committing SceneManager.LoadScene('{sceneName}')");
            SceneManager.LoadScene(sceneName);
        }

        // =====================================================================
        //  Async load with fade
        // =====================================================================

        /// <summary>
        /// Fades a full-screen black overlay in, loads <paramref name="sceneName"/>
        /// asynchronously, then fades back out. Returns a <see cref="UniTask"/> —
        /// never <c>async void</c> (Part-3 mandate).
        /// <para>
        /// <paramref name="beforeLoad"/> (WO-1109, OPTIONAL — default null keeps every existing
        /// caller byte-for-byte unchanged) runs on the LAST line before
        /// <c>SceneManager.LoadSceneAsync</c>, i.e. AFTER the build-settings gate, the save
        /// flush and the fade-out. That position is the whole point: it is where a caller can
        /// mutate live scene objects knowing the load is definitely about to happen. The hero
        /// carry uses it so the hero spends ZERO frames detached-and-DontDestroyOnLoad while
        /// the old scene is still the live, playable one — the window an abort or a slow
        /// backend save would otherwise stretch open. It is Guard-wrapped: a throwing hook
        /// logs and the load still proceeds, because stranding the player on the current
        /// scene is strictly worse than a missed hook.
        /// </para>
        /// </summary>
        public static async UniTask LoadSceneWithFade(string sceneName, float fadeSeconds = DefaultFadeSeconds, Action beforeLoad = null)
        {
            FlowTrace.Step("SceneRouter", $"LoadSceneWithFade name='{sceneName ?? "<null>"}' fade={fadeSeconds:F2}s");
            if (!IsSceneRegistered(sceneName))
            {
                FlowTrace.Fail("SceneRouter", $"LoadSceneWithFade ABORTED: '{sceneName ?? "<null>"}' not in Build Settings — hero stranded on current scene.");
                Debug.LogError($"[SceneRouter] Scene '{sceneName}' is not in Build Settings — load aborted.");
                return;
            }

            // WO1: flush local + backend before the scene tears down. Runs during
            // the fade-out so there is no perceptible delay added to the transition.
            // WO-769 FIX (device data 2026-07-26): the backend flush must NEVER abort
            // navigation. A signed-in player's save-sync POSTs to Neon /api/game/save;
            // if that 401s (Neon not yet verifying the Firebase token) or the network
            // faults, the thrown UnityWebRequestException used to propagate out of this
            // .Forget()'d task and SILENTLY strand the player on the front-end scene
            // (LoadSceneAsync below never ran). The local save already persisted; a
            // failed cloud sync re-queues offline — it must not block the scene load.
            var svc = DeNelle.Core.State.GameStateService.Instance;
            if (svc != null)
            {
                try { await svc.SaveBeforeSceneChange(); }
                catch (System.Exception e)
                {
                    FlowTrace.Warn("SceneRouter",
                        $"SaveBeforeSceneChange failed — loading '{sceneName}' anyway (save re-queues): {e.Message}");
                }
            }

            if (Fader != null)
                await Fader.FadeOut(fadeSeconds);

            // ── WO-1187: the chosen hero's REMOTE art is fetched HERE, behind the load screen ──
            // The owner's ruling: "you're only gonna load one of them, so once they select that one,
            // while it's going to that load screen, it loads the model and the whole thing."
            // The screen is already covered (fade-out completed above) and LoadingOverlay is up, so
            // this is the free wait window; doing it later would stall the main thread mid-gameplay
            // because HeroAssetLoader resolves synchronously.
            //
            // ⛔ AND IT IS A GATE, NOT A BEST-EFFORT WARM. Hero art has NO local copy any more, and
            // §16 remote art fails SILENTLY — a bundle that was never pushed yields a tinted CAPSULE
            // with no error on screen. So a hard failure BLOCKS the scene load and holds the player
            // on a worded barrier. Never downgrade this to a warn-and-continue: that is precisely the
            // "owner's eyes are the only detector" outcome §14 exists to eliminate.
            if (!await EnsureHeroContentOrBlock())
                return;

            // RETURN-POINT (return-point feature): if a battle stashed a return point, arm a
            // one-shot sceneLoaded handler BEFORE the load so the hero is warped back to where
            // they fought the instant the destination scene is active. Self-clearing.
            ArmReturnPointRestore();

            // WO-1109: the last line before the load commits — see the beforeLoad docs above.
            if (beforeLoad != null)
                Guard.Try("SceneRouter", $"beforeLoad hook for '{sceneName}'", () => beforeLoad());

            var op = SceneManager.LoadSceneAsync(sceneName);
            if (op != null)
            {
                op.allowSceneActivation = true;
                await op.ToUniTask();
            }

            if (Fader != null)
                await Fader.FadeIn(fadeSeconds);
        }

        // =====================================================================
        //  HERO REMOTE CONTENT GATE (WO-1187)
        // =====================================================================

        /// <summary>
        /// Download the chosen hero's remote art before the destination scene loads. Returns true
        /// when it is safe to proceed, false when the player has been held on a WORDED barrier and
        /// the caller must NOT load the scene.
        /// <para>
        /// The failure text comes from <see cref="HeroContentPrewarmer.StatusText"/> and is always a
        /// SENTENCE — the owner is red/green colourblind, so a colour cue is not a failure state she
        /// can read (memory: owner-colorblind-delegate-visual-creative).
        /// </para>
        /// </summary>
        private static async UniTask<bool> EnsureHeroContentOrBlock()
        {
            bool ok = true;
            try
            {
                await PumpCoroutine(HeroContentPrewarmer.PrewarmChosenHero());
                ok = HeroContentPrewarmer.State == HeroPrewarmState.Ready;
            }
            catch (System.Exception e)
            {
                // Never let a fetch exception strand the player with no explanation — the WO-769
                // lesson one method up. Fall through to the worded barrier below.
                ok = false;
                FlowTrace.Fail("SceneRouter", $"hero content prewarm threw — blocking scene load: {e.Message}");
            }

            if (ok) return true;

            string message = string.IsNullOrEmpty(HeroContentPrewarmer.StatusText)
                ? "Could not download your hero's artwork. The game has stopped here rather than " +
                  "dropping you in without it. Check your internet connection and tap Retry."
                : HeroContentPrewarmer.StatusText;

            // Let the barrier's Retry re-run OUR download, not the offline content probe (which would
            // report success and dismiss with the hero art still missing).
            DeNelle.Core.UI.LoadingOverlay.SetRetryOverride(report => RetryHeroContent(report));
            DeNelle.Core.UI.LoadingOverlay.ShowConnectionRequired(message, "Retry");

            FlowTrace.Fail("SceneRouter",
                "scene load BLOCKED: the chosen hero's remote art is unavailable. Player is held on the " +
                "worded connection barrier instead of entering the world as an untextured placeholder. " +
                "If this fires on a build that has network, the hero bundles were almost certainly never " +
                "pushed to R2 for THIS build (CLAUDE.md §16 — bundle names are content-hashed).");
            return false;
        }

        /// <summary>Retry behaviour handed to the LoadingOverlay barrier: re-run the prewarm and report.</summary>
        private static IEnumerator RetryHeroContent(System.Action<bool> report)
        {
            HeroContentPrewarmer.Reset();
            yield return HeroContentPrewarmer.PrewarmChosenHero();
            bool recovered = HeroContentPrewarmer.State == HeroPrewarmState.Ready;
            report?.Invoke(recovered);
        }

        /// <summary>
        /// Drive an <see cref="IEnumerator"/> to completion from async code, one step per frame.
        /// Handles NESTED coroutines (a step that yields another IEnumerator) recursively — the
        /// prewarmer yields sub-coroutines, and a non-recursive pump would silently skip them,
        /// reporting success without ever having downloaded anything.
        /// </summary>
        private static async UniTask PumpCoroutine(IEnumerator routine)
        {
            if (routine == null) return;

            while (routine.MoveNext())
            {
                if (routine.Current is IEnumerator nested)
                {
                    await PumpCoroutine(nested);
                    continue;
                }
                await UniTask.Yield();
            }
        }

        // =====================================================================
        //  RETURN-POINT restore (return-point feature)
        // =====================================================================

        /// <summary>
        /// RETURN-POINT (return-point feature): if <see cref="Return"/> is set, subscribes a
        /// ONE-SHOT <see cref="SceneManager.sceneLoaded"/> handler that finds the hero in the
        /// freshly-loaded scene and warps it to the stashed pose (reusing HeroLocomotion.WarpTo
        /// — disables the agent, moves, re-warps onto the NavMesh, raises OnTeleported for the
        /// follow camera), then clears <see cref="Return"/> and unsubscribes. Null-guarded; a
        /// no-op when nothing was stashed.
        /// </summary>
        private static void ArmReturnPointRestore()
        {
            if (Return == null) { FlowTrace.Step("SceneRouter", "ArmReturnPointRestore: no Return stashed — nothing to arm."); return; }

            // Audit P1 fix (return-point double-subscribe): if GoBattle arms again
            // while a prior load is still pending, a second handler would subscribe —
            // handler#1 clears Return, handler#2 then sees null and skips the warp
            // (wrong scene on return). Always detach before attaching so exactly one
            // handler is ever active.
            FlowTrace.Step("SceneRouter", $"ArmReturnPointRestore: arming one-shot sceneLoaded handler for return scene '{Return.Scene ?? "<null>"}' (detach-before-attach de-dupe).");
            SceneManager.sceneLoaded -= OnReturnSceneLoaded;
            // Capture once; the handler runs after the load completes.
            SceneManager.sceneLoaded += OnReturnSceneLoaded;
        }

        private static void OnReturnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            FlowTrace.Step("SceneRouter", $"OnReturnSceneLoaded fired for scene '{scene.name}' (mode={mode}).");
            // One-shot: always detach, even on an early-out / failure path.
            SceneManager.sceneLoaded -= OnReturnSceneLoaded;

            ReturnPoint rp = Return;
            Return = null;            // consume regardless of outcome
            if (rp == null)
            {
                // DOUBLE-SUBSCRIBE STRAND: a prior handler already consumed Return (set it null).
                // This second handler now has nothing to warp with — the hero stays where the
                // load dropped them instead of returning to the launch pose.
                FlowTrace.Fail("SceneRouter", $"OnReturnSceneLoaded: Return already consumed (null) on scene '{scene.name}' — double-subscribe STRAND, hero NOT warped home.");
                return;
            }

            try
            {
                GameObject hero = null;
                try { hero = GameObject.FindWithTag("Player"); }
                catch (Exception e) { FlowTrace.Warn("SceneRouter", $"RestoreReturnPoint FindWithTag('Player') threw (tag may be undefined): {e.GetType().Name}: {e.Message}"); hero = null; }

                // WO-1510: the Village seam is CoreServices.VillageBridge, not reflection.
                var bridge = CoreServices.VillageBridge;
                if (bridge == null)
                {
                    FlowTrace.Fail("SceneRouter", $"Return-point restore: no IVillageBridge registered while loading '{scene.name}' — warp SKIPPED, hero not repositioned to launch pose {rp.Position}. (Expected only in a Core-only/headless context; in a Village build it means VillageBridgeService never installed.)");
                    Debug.LogWarning("[SceneRouter] Return-point restore: no IVillageBridge registered — warp skipped.");
                    return;
                }

                GameObject heroGo = (hero != null && bridge.HasHero(hero)) ? hero : bridge.FindHeroObject();
                if (heroGo == null)
                {
                    FlowTrace.Fail("SceneRouter", $"Return-point restore: no hero locomotion in '{scene.name}' — warp SKIPPED, hero not repositioned to launch pose {rp.Position}.");
                    Debug.LogWarning("[SceneRouter] Return-point restore: no hero locomotion found — warp skipped.");
                    return;
                }

                Quaternion rot = Quaternion.Euler(0f, rp.Yaw, 0f);

                // The bridge's warp disables the agent, moves, re-warps onto the NavMesh and
                // raises OnTeleported for the camera. It returns false only if the object we
                // just resolved has lost its locomotion component between the two calls.
                if (bridge.WarpHero(heroGo, rp.Position, rot))
                {
                    FlowTrace.Step("SceneRouter", $"Return-point restored hero via IVillageBridge.WarpHero to {rp.Position} yaw={rp.Yaw:F0} in '{scene.name}'.");
                    Debug.Log($"[SceneRouter] Return-point restored hero to {rp.Position} in '{scene.name}'.");
                }
                else
                {
                    // Last-ditch fallback: plain transform move so the player at least lands home.
                    FlowTrace.Warn("SceneRouter", $"Return-point: IVillageBridge.WarpHero refused (hero object carries no locomotion) — using transform fallback (no NavMesh re-warp/camera event) to {rp.Position}.");
                    heroGo.transform.SetPositionAndRotation(rp.Position, rot);
                    Debug.LogWarning("[SceneRouter] Return-point: WarpHero refused — used transform fallback.");
                }
            }
            catch (System.Exception e)
            {
                FlowTrace.Fail("SceneRouter", $"Return-point restore threw on '{scene.name}': {e.GetType().Name}: {e.Message} — hero NOT returned to launch pose.");
                Debug.LogWarning("[SceneRouter] Return-point restore threw (non-fatal): " + e.Message);
            }
        }

        /// <summary>
        /// RETURN-POINT helper: the GameObject hosting the active hero locomotion, or null.
        /// WO-1510: resolved through <see cref="CoreServices.VillageBridge"/> — the sanctioned
        /// seam — instead of the old <c>Type.GetType</c> reflection into DeNelle.Village. A null
        /// bridge is legitimate (Core-only/headless) but is TRACED, never swallowed (§12).
        /// </summary>
        private static GameObject FindHeroObject()
        {
            var bridge = CoreServices.VillageBridge;
            if (bridge == null)
            {
                FlowTrace.Warn("SceneRouter", "FindHeroObject: no IVillageBridge registered — hero lookup returns null (expected only in a Core-only/headless context).");
                return null;
            }

            try { return bridge.FindHeroObject(); }
            catch (Exception e) { FlowTrace.Warn("SceneRouter", $"FindHeroObject: IVillageBridge.FindHeroObject threw: {e.GetType().Name}: {e.Message}"); return null; }
        }

        // =====================================================================
        //  Typed entry points mirroring the React routes
        // =====================================================================

        /// <summary>Go to the Title scene (React `/`).</summary>
        public static void GoTitle() => LoadScene(Title);

        /// <summary>
        /// Go to the hero-select screen — the first step of the intro flow after
        /// the Title scene's studio bumper. The Title "Start" button routes here.
        /// </summary>
        public static void GoHeroSelect() => LoadScene(HeroSelect);

        /// <summary>
        /// Go to the pet-select screen — the second intro-flow step, entered from
        /// hero-select once the player confirms a hero.
        /// </summary>
        public static void GoPetSelect() => LoadScene(PetSelect);

        /// <summary>
        /// Go to the Village scene (React `/village`). Routes through
        /// <see cref="LoadVillageWithLoader"/> so the 3-6s village load shows a
        /// code-built loading overlay instead of a black stall. Fire-and-forget —
        /// the overlay + async load run on their own; callers don't await.
        /// </summary>
        public static void GoVillage() => LoadVillageWithLoader().Forget();

        /// <summary>
        /// Go to the Central Castle Hub (<see cref="Castle"/>) — the home base players
        /// land in after onboarding and on every session resume. Fire-and-forget with
        /// a fade, mirroring <see cref="GoVillage"/>. The castle is lighter than the
        /// village, so it uses the standard fade load rather than the village overlay.
        /// From the castle the player travels out to the village TD loop.
        /// </summary>
        public static void GoCastle()
        {
            var castle = Castle;
            FlowTrace.Step("SceneRouter", $"GoCastle -> '{castle}' (MergedWorld={DeNelle.Core.FeatureFlags.MergedWorld}).");
            LoadSceneWithFade(castle).Forget();
        }

        /// <summary>
        /// Go to a RAID BASE scene (WO-453 Step 4) — the troop DEPLOY/RALLY/RETREAT
        /// target the player assaults from the castle hub. Fire-and-forget with a fade,
        /// mirroring <see cref="GoCastle"/>; the raid scene's RaidGarrisonSpawner marks
        /// it enemy-owned on the far side and the RaidDeployController self-installs.
        /// <para>
        /// SHARED CONTRACT: this exact signature — <c>GoRaid(string sceneName)</c> — is
        /// what the raid-entry UI calls; pass one of the <c>RaidBase*</c> consts. An
        /// unregistered scene name is rejected by <see cref="LoadSceneWithFade"/> (logs,
        /// no load), so a bad name never silently strands the player.
        /// </para>
        /// <para>
        /// WO-1109 — HERO CARRY. A raid loads SINGLE, which destroys every root in the
        /// hub scene INCLUDING the hero. Until now nothing carried it, so
        /// <c>HeroControlEnsurer.TryRecoverCarriedHero</c> (keyed on the DontDestroyOnLoad
        /// scene) found nothing and every single raid entry fell through to
        /// <c>SpawnEmergencyHero()</c> — whose first line is a <c>FlowTrace.Fail</c>. A Fail
        /// that lands on EVERY raid entry trains every seat to ignore Hero Fails (§12/§14),
        /// and if the subsequent body swap ever missed, the player drove a lavender capsule.
        /// So the hero is marked DontDestroyOnLoad here, exactly as
        /// <c>SceneTransitionTrigger</c> does for every other Single-load seam (outposts,
        /// dungeons, arenas, Village2) — the raid hero is now literally the town hero, and
        /// the ensurer re-homes + seats it at the raid's baked
        /// <c>HeroStartPoint_PlayerSpawn</c>. The emergency path stays exactly as it was: it
        /// is still reached (and still Fails loudly) whenever the carry genuinely produced
        /// no hero, which is now a REAL defect rather than the normal path.
        /// </para>
        /// </summary>
        public const string OwnedTownIronBastion = "OwnedTown_IronBastion";

        public static void GoTownPractice()
        {
            const string scene = DeNelle.Core.Combat.PracticeCombatPolicy.SceneName;
            LoadSceneWithFade(scene, beforeLoad: () => CarryHeroAcrossSingleLoad("GoTownPractice", scene)).Forget();
        }

        public static void GoOwnedTown()
        {
            var property = DeNelle.Core.State.GameStateService.Instance?.State?.OwnedBase;
            if (!DeNelle.Core.State.OwnedBaseProgression.Validate(property, out var reason))
            { FlowTrace.Warn("SceneRouter", "Personal town entry refused: " + reason); return; }
            LoadSceneWithFade(OwnedTownIronBastion,
                beforeLoad: () => CarryHeroAcrossSingleLoad("GoOwnedTown", OwnedTownIronBastion)).Forget();
        }

        public static void GoRaid(string sceneName)
        {
            FlowTrace.Step("SceneRouter", $"GoRaid name='{sceneName ?? "<null>"}' — hero carry armed as the pre-load hook.");

            // WO-1374 — FUNNEL STEP 3 (and, on a return visit, step 6: "second raid
            // attempted within 24h", which the north-star map calls the gold nugget).
            // THIS is the right seam and the selection screen is not: opening a camp list
            // and backing out is not an attempt, whereas reaching GoRaid means the player
            // committed to the raid scene. It is also the SHARED contract every raid entry
            // already funnels through (the Herald, the Journey card, the dev panel), so no
            // future entry point can be added that quietly skips the measurement.
            // Guarded: instrumentation must never be able to stop a scene load.
            DeNelle.Core.Diagnostics.Guard.Try("Funnel", "raid attempted",
                () => DeNelle.Core.Analytics.RaidFunnel.RaidAttempted(sceneName));
            // TIMING IS THE CONTRACT, not a detail. The carry is handed to LoadSceneWithFade as
            // its beforeLoad hook rather than run inline here, so it fires on the last line
            // before SceneManager.LoadSceneAsync — AFTER the IsSceneRegistered gate, the save
            // flush and the fade-out. Two things that buys, both of which an inline call gets
            // WRONG: (a) an unregistered scene aborts the load, and an inline carry would have
            // already detached the hero from CastleHubRoot and DDOL'd it in a town that never
            // unloads — an orphan the NEXT Single load drags somewhere it does not belong; and
            // (b) the fade + backend save can take hundreds of ms, during which an inline carry
            // leaves the player driving a detached, DontDestroyOnLoad hero around a live town.
            LoadSceneWithFade(sceneName, beforeLoad: () => CarryHeroAcrossSingleLoad("GoRaid", sceneName)).Forget();
        }

        /// <summary>
        /// WO-1109: marks the live hero <see cref="UnityEngine.Object.DontDestroyOnLoad"/> so it
        /// SURVIVES a Single scene load, and returns true when a hero was actually carried.
        /// The receiving half is <c>HeroControlEnsurer</c>, which re-homes the carried root out
        /// of the special DontDestroyOnLoad scene into the freshly-loaded scene and seats it at
        /// that scene's <c>HeroStartPoint_PlayerSpawn</c> marker — so nothing is left parked in
        /// DDOL to leak or duplicate on the NEXT transition.
        /// <para>
        /// The hero is DETACHED from its parent first (keeping world pose). This is not
        /// cosmetic: in the merged overworld the hero is nested under <c>CastleHubRoot</c>,
        /// which also holds WaveManager + HeartController + the Tree of Life, and DDOL-ing the
        /// ROOT once dragged the whole hub into the destination scene (owner F8 2026-07-10,
        /// "why is there a tree of life in map"). <c>SceneTransitionTrigger</c> learned that the
        /// hard way; this carry uses the same shape deliberately.
        /// </para>
        /// <para>
        /// Resolution order is hero-locomotion-first (through
        /// <see cref="CoreServices.VillageBridge"/> — Core must never reference DeNelle.Village),
        /// then the 'Player' tag, because the locomotion component is the exact thing the
        /// receiving ensurer keys on.
        /// </para>
        /// </summary>
        private static bool CarryHeroAcrossSingleLoad(string via, string targetScene)
        {
            GameObject heroGo = FindHeroObject();
            if (heroGo == null)
            {
                try { heroGo = GameObject.FindWithTag("Player"); }
                catch (Exception e)
                {
                    FlowTrace.Warn("Hero", $"{via} carry: FindWithTag('Player') threw ({e.GetType().Name}: {e.Message}) — no tag-based hero.");
                    heroGo = null;
                }
            }

            if (heroGo == null)
            {
                // NOT silent (§12): this is the exact condition that makes the destination fall
                // back to the EMERGENCY hero, so the trace must say so BEFORE the Fail lands.
                FlowTrace.Warn("Hero",
                    $"{via} carry: no live hero found to carry into '{targetScene}' — the destination will fall back to " +
                    "HeroControlEnsurer's EMERGENCY spawn (expect an 'EMERGENCY pill spawned' Fail).");
                return false;
            }

            string priorParent = heroGo.transform.parent != null ? heroGo.transform.parent.name : "<none>";
            bool carried = Guard.Try("Hero", $"{via} carry DontDestroyOnLoad('{heroGo.name}')", () =>
            {
                if (heroGo.transform.parent != null)
                    heroGo.transform.SetParent(null, true);   // detach, keep world pose
                UnityEngine.Object.DontDestroyOnLoad(heroGo);
            });

            if (carried)
            {
                FlowTrace.Step("Hero",
                    $"{via} carry ARMED: DontDestroyOnLoad hero '{heroGo.name}' (detached from '{priorParent}') " +
                    $"across the Single load to '{targetScene}' — the destination hero IS the town hero.");
            }
            return carried;
        }

        /// <summary>
        /// Enters a DUNGEON scene by its already-resolved scene name, carrying the town hero
        /// across the Single load when — and only when — the destination is a COMPOSED
        /// (<c>dg_*</c>) dungeon. The dungeon twin of <see cref="GoRaid"/>, and deliberately
        /// the SAME mechanism: it hands <see cref="CarryHeroAcrossSingleLoad"/> to
        /// <see cref="LoadSceneWithFade"/> as its <c>beforeLoad</c> hook, so there is exactly
        /// one carry implementation in the project.
        /// <para>
        /// WO-1112 — WHY THE CARRY IS NEEDED AT ALL. DungeonBaker.PopulateForPlay bakes the
        /// composed Keeper with HeroLocomotion + HeroBodySwapper and NOTHING ELSE, so it has
        /// no <c>HeroAbilities</c>; <c>HeroAbilityInput</c> is [RequireComponent(HeroAbilities)]
        /// and never attaches, and <c>AssignableSkillBar</c>'s ability ref stays null. Net
        /// effect for the player: in every composed dungeon Q/W/E/R did nothing, SILENTLY —
        /// not one trace line was emitted, which is why it survived nightly play. Carrying the
        /// real town hero brings its abilities, gear, loadout, progression and HP with it,
        /// exactly as WO-1109 did for raids.
        /// </para>
        /// <para>
        /// ⚠ THE GATE IS LOAD-BEARING — DO NOT WIDEN IT TO <c>HubScenes.IsDungeon</c>. A
        /// HAND-BUILT dungeon (<c>Dungeon_HealersCottage</c> etc.) bakes a hero its
        /// DungeonController owns through SERIALIZED references. Carrying a second hero in
        /// there makes HeroControlEnsurer.DedupeHeroes destroy the baked one (it keeps the
        /// DontDestroyOnLoad instance by design), which nulls those refs and breaks the rich
        /// pipeline that works today. Composed scenes have no such owner — their baked rig is
        /// the bare one described above, and losing it to the dedupe is the POINT.
        /// </para>
        /// </summary>
        public static void GoDungeonScene(string sceneName)
        {
            bool composed = DeNelle.Core.HubScenes.IsComposedDungeon(sceneName);
            FlowTrace.Step("SceneRouter",
                $"GoDungeonScene name='{sceneName ?? "<null>"}' composed={composed} " +
                (composed
                    ? "— hero carry armed as the pre-load hook (WO-1112)."
                    : "— hand-built pipeline: NO carry (that scene's DungeonController owns its own baked hero)."));

            // Same timing contract as GoRaid: the carry rides the beforeLoad hook so it fires
            // after the build-settings gate + save flush + fade, on the last line before the
            // load commits. Inline would orphan a detached DDOL hero on an aborted load and
            // leave the player driving a detached hero around a live town during the fade.
            if (composed)
                LoadSceneWithFade(sceneName, beforeLoad: () => CarryHeroAcrossSingleLoad("GoDungeonScene", sceneName)).Forget();
            else
                LoadSceneWithFade(sceneName).Forget();
        }

        /// <summary>
        /// Loads the Village scene ASYNCHRONOUSLY behind a full-screen, code-built
        /// loading overlay ("Loading Elarion…" + spinner + progress + rotating lore).
        /// Fixes the black-screen stall: the synchronous SceneManager.LoadScene gave
        /// the engine no chance to render feedback during the multi-second load;
        /// LoadSceneAsync yields each frame so the overlay animates while the village
        /// streams in, then the overlay tears down once the scene is active.
        ///
        /// The overlay is uGUI (no UXML, no PanelSettings) so it renders in player
        /// builds — see <see cref="UI.VillageLoadOverlay"/>. Returns a UniTask; never
        /// async void (Part-3 mandate).
        /// </summary>
        public static async UniTask LoadVillageWithLoader()
        {
            FlowTrace.Step("SceneRouter", $"LoadVillageWithLoader -> '{Village}' (overlay load).");
            if (!IsSceneRegistered(Village))
            {
                FlowTrace.Fail("SceneRouter", $"LoadVillageWithLoader ABORTED: '{Village}' not in Build Settings — village load aborted.");
                Debug.LogError($"[SceneRouter] Scene '{Village}' is not in Build Settings — load aborted.");
                return;
            }

            // Persist the save before tearing the old scene down (same contract as
            // LoadScene). No await needed — the background delta-sync pushes shortly.
            DeNelle.Core.State.GameStateService.Instance?.Save();

            // Put the loader up FIRST so the player sees feedback immediately, before
            // the (heavy) scene load begins.
            var overlay = UI.VillageLoadOverlay.Show();

            // A frame so the overlay actually paints before we start the load.
            await UniTask.Yield();

            var op = SceneManager.LoadSceneAsync(Village);
            if (op != null)
            {
                // Hold activation until ~90% so the bar fills smoothly, then flip it.
                op.allowSceneActivation = false;
                while (!op.isDone)
                {
                    // Unity caps progress at 0.9 until allowSceneActivation flips true.
                    float p = Mathf.Clamp01(op.progress / 0.9f);
                    overlay?.SetProgress(p);

                    if (op.progress >= 0.9f)
                    {
                        overlay?.SetProgress(1f);
                        op.allowSceneActivation = true;
                    }
                    await UniTask.Yield();
                }
            }

            // A couple of frames after activation so the new scene's first frame is up
            // (NavMesh/HUD/hero settling) before we pull the overlay — avoids a flash
            // of an un-lit village.
            await UniTask.DelayFrame(2);

            // LAST word on village load: plant the decorative Tree of Life at a fixed
            // (0, -0.25, 0) so its roots sit just under the ground (owner-requested).
            // This runs AFTER every Awake/Start (incl. SeatOnGroundOnStart), so it is
            // the final position. NB: this is the *decorative* centrepiece (it carries
            // the Core-side TreeOfLifeMaterialFixer) — NOT the gameplay Heart anchor,
            // which stays at (0,0,0) for spawns/camera/grid and the regression gate.
            SeatTreeOfLifeRootsUnderground();

            overlay?.HideAndDestroy();
        }

        /// <summary>
        /// Forces the decorative Tree of Life centrepiece to (0, -0.25, 0) so its roots
        /// read as planted under the ground. Found via its Core-side
        /// <c>TreeOfLifeMaterialFixer</c> (no Core→Village reference). Non-fatal.
        /// </summary>
        private static void SeatTreeOfLifeRootsUnderground()
        {
            try
            {
                var tree = UnityEngine.Object.FindAnyObjectByType<DeNelle.Core.TreeOfLifeMaterialFixer>();
                if (tree != null)
                {
                    tree.transform.position = new Vector3(0f, -0.25f, 0f);
                    FlowTrace.Step("SceneRouter", $"Tree of Life planted at {tree.transform.position} (roots underground).");
                    Debug.Log($"[SceneRouter] Tree of Life planted at {tree.transform.position} (roots underground).");
                }
                else
                {
                    FlowTrace.Warn("SceneRouter", "Tree of Life (TreeOfLifeMaterialFixer) not found on village load — root-seat skipped.");
                    Debug.LogWarning("[SceneRouter] Tree of Life (TreeOfLifeMaterialFixer) not found on village load — root-seat skipped.");
                }
            }
            catch (System.Exception e)
            {
                FlowTrace.Fail("SceneRouter", $"Tree root-seat threw: {e.GetType().Name}: {e.Message}");
                Debug.LogWarning("[SceneRouter] Tree root-seat threw (non-fatal): " + e.Message);
            }
        }

        /// <summary>
        /// Go to a dungeon scene with a fade (React `/dungeon/:id`). Week 1 ships
        /// <c>Dungeon_HealersCottage</c>.
        /// </summary>
        public static UniTask GoDungeon(string dungeonId) => LoadSceneWithFade(Dungeon(dungeonId));

        /// <summary>
        /// Go to the ATB battle scene with a fade, handing off <paramref name="p"/>.
        /// The hand-off mechanism (runtime SO / static field) is detailed in the
        /// Week-2 BattleATB spec; this is the signature stub.
        /// </summary>
        public static UniTask GoBattle(BattleParams p)
        {
            FlowTrace.Step("SceneRouter", $"GoBattle wave={p?.Wave ?? -1} breachedIds={p?.BreachedIds?.Length ?? 0} -> ATBBattle");
            PendingBattle = p;

            // RETURN-POINT (return-point feature): stash the scene + hero pose we are
            // leaving BEFORE the ATB scene loads Single and tears this world down, so the
            // round-trip returns to where the player fought instead of the Village2 default.
            StashReturnPoint(p);

            return LoadSceneWithFade(ATBBattle);
        }

        /// <summary>The last <see cref="BattleParams"/> handed to <see cref="GoBattle"/>.</summary>
        public static BattleParams PendingBattle { get; private set; }

        /// <summary>
        /// RETURN-POINT (return-point feature): the scene + hero pose to restore after a
        /// battle resolves. Set by <see cref="GoBattle"/>, read by BattleController to pick
        /// the return scene, and consumed once by <see cref="RestoreReturnPointOnLoad"/>.
        /// </summary>
        public static ReturnPoint Return { get; set; }

        /// <summary>
        /// RETURN-POINT (return-point feature): captures the active scene name and the hero's
        /// current world pose into <see cref="Return"/>, and pins
        /// <see cref="BattleParams.ReturnScene"/> to that scene so the Village2 default can
        /// never win on the far side. Fully null-guarded — if no hero is found the position is
        /// skipped but the scene is still recorded. SceneRouter lives in DeNelle.Core, which
        /// must not reference DeNelle.Village, so the hero is reached by the "Player" tag and
        /// (fallback) through <see cref="CoreServices.VillageBridge"/> — never a direct
        /// HeroLocomotion type reference, and (since WO-1510) never by reflection either.
        /// </summary>
        private static void StashReturnPoint(BattleParams p)
        {
            string activeScene = SceneManager.GetActiveScene().name;
            FlowTrace.Step("SceneRouter", $"StashReturnPoint from active scene '{activeScene}'.");
            var rp = new ReturnPoint { Scene = activeScene };

            try
            {
                GameObject hero = null;
                try { hero = GameObject.FindWithTag("Player"); }
                catch (Exception e) { FlowTrace.Warn("SceneRouter", $"StashReturnPoint FindWithTag('Player') threw (tag may be undefined in some scenes): {e.GetType().Name}: {e.Message}"); hero = null; }

                if (hero == null)
                {
                    // Fallback: locate the hero-locomotion host through the Village bridge
                    // (CoreServices seam — no Core→Village asmdef reference, no reflection).
                    FlowTrace.Warn("SceneRouter", "StashReturnPoint: no 'Player'-tagged hero — falling back to IVillageBridge hero lookup.");
                    hero = FindHeroObject();
                }

                if (hero != null)
                {
                    rp.Position = hero.transform.position;
                    rp.Yaw = hero.transform.eulerAngles.y;
                    FlowTrace.Step("SceneRouter", $"StashReturnPoint captured hero pose pos={rp.Position} yaw={rp.Yaw:F0}.");
                }
                else
                {
                    FlowTrace.Warn("SceneRouter", $"StashReturnPoint: no hero found in '{activeScene}' — scene stashed, position defaults to {rp.Position}.");
                    Debug.LogWarning("[SceneRouter] Return-point: no hero found — scene stashed, position skipped.");
                }
            }
            catch (System.Exception e)
            {
                FlowTrace.Fail("SceneRouter", $"StashReturnPoint threw on '{activeScene}': {e.GetType().Name}: {e.Message} — return pose may be wrong on the round-trip.");
                Debug.LogWarning("[SceneRouter] Return-point stash threw (non-fatal): " + e.Message);
            }

            Return = rp;

            // Pin the return scene so BattleParams' Village2 default can never win.
            if (p != null) p.ReturnScene = activeScene;
        }

        /// <summary>
        /// Go to the "Defend the Tower" (Patricia Light) shooter scene with a
        /// fade, stashing <paramref name="p"/> on <see cref="PendingPatriciaLight"/>.
        /// Mirrors <see cref="GoBattle"/>: the scene's PatriciaLightController reads
        /// the pending params on the far side and, on resolve, returns via
        /// <see cref="GoVillage"/>. Fire-and-forget — never await in a hot path.
        /// </summary>
        public static UniTask GoPatriciaLight(PatriciaLightParams p)
        {
            PendingPatriciaLight = p;
            return LoadSceneWithFade(PatriciaLight);
        }

        /// <summary>The last <see cref="PatriciaLightParams"/> handed to <see cref="GoPatriciaLight"/>.</summary>
        public static PatriciaLightParams PendingPatriciaLight { get; private set; }

        // ── Helpers ──────────────────────────────────────────────────────────

        /// <summary>
        /// WO-932: public probe for raid deploy CTAs — true when <paramref name="sceneName"/>
        /// is in the player Build Settings (same gate <see cref="GoRaid"/> uses). False =
        /// toast "under construction", never a silent strand.
        /// </summary>
        public static bool IsSceneInBuild(string sceneName) => IsSceneRegistered(sceneName);

        private static bool IsSceneRegistered(string sceneName)
        {
            if (string.IsNullOrEmpty(sceneName)) return false;
            return Application.CanStreamedLevelBeLoaded(sceneName);
        }
    }

    /// <summary>
    /// A full-screen fade overlay — implemented by the Core bootstrap. Kept as an
    /// interface so <see cref="SceneRouter"/> stays free of any UI dependency.
    /// </summary>
    public interface ISceneFader
    {
        /// <summary>Fade the overlay to fully opaque (black) over <paramref name="seconds"/>.</summary>
        UniTask FadeOut(float seconds);

        /// <summary>Fade the overlay to fully transparent over <paramref name="seconds"/>.</summary>
        UniTask FadeIn(float seconds);
    }
}

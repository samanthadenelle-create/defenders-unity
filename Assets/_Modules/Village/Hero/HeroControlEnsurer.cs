// =============================================================================
// HeroControlEnsurer - keeps the village hero controllable, and (new) recovers
// when something DESTROYS the hero root early in the village load.
// -----------------------------------------------------------------------------
// Player.log proved the baked "Hero (Blaise)" is present + healthy at load but
// DESTROYED within the first frame (before HeroLocomotion.Start runs) by an
// as-yet-unidentified third party (not VillageNpcInjector, not HeroBodySwapper,
// not HeroProgression - all ruled out). With no hero, there's nothing to re-enable,
// so this:
//   1. Ensures a present-but-disabled hero is active + its HeroLocomotion enabled.
//   2. Attaches HeroDeathLogger to the live hero so its OnDestroy logs WHEN (frame/
//      time) + a stack trace - which names the destroyer if it used DestroyImmediate.
//   3. Watches; if the hero vanishes it spawns an EMERGENCY movable capsule-hero at
//      the build spawn point and wires SmartMobileCamera (primary) + legacy fallback
//      so the camera follows instead of staying stuck on the tree. (PatriciaLight descoped.)
// Self-bootstrapping DDOL; no Village.unity edit.
// =============================================================================

using System.Collections;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DeNelle.Village
{
    /// <summary>Ensures / recovers the village hero so the player can always move.</summary>
    public sealed class HeroControlEnsurer : MonoBehaviour
    {
        public static HeroControlEnsurer Instance { get; private set; }
        private const int MaxEmergencySpawns = 8;

        // PatriciaLight (DTT) is descoped. Activate for Village* scenes AND the new Castle Hub
        // (MainCastle_Hall, CastleHub*, etc.) so the ensurer + SmartMobileCamera target wiring
        // and emergency recovery works when the Castle is the project start / primary world scene.
        //
        // RAID (WO-431/453): also activate for RaidBase_* scenes — the raid is HERO-LED, but no
        // hero is baked into the generated raid base, so the ensurer must wire the camera + control
        // there too.
        //
        // ⚠ WO-1109 — THE COMMENT THAT USED TO SIT HERE WAS A LIE, AND THE LIE WAS THE BUG.
        // It claimed: "RaidHeroSpawner builds the REAL class body on a hero root one frame after
        // load; this ensurer then finds it (FindLoco)". THERE IS NO RaidHeroSpawner. No such type
        // ever existed anywhere in the repo — the only mention of the name was that sentence. A
        // seat reading it concluded the raid hero path was designed and wired, and was wrong
        // (CLAUDE.md MANDATORY FIRST STEP: "verified from the actual code, NOT from comments").
        // What ACTUALLY happened on every single raid entry: SceneRouter.GoRaid carried nothing,
        // TryRecoverCarriedHero (keyed on the DontDestroyOnLoad scene) found nothing, and Ensure()
        // fell straight through to SpawnEmergencyHero() — whose FIRST line is a FlowTrace.Fail. So
        // a Hero Fail landed in the F8 break-log on EVERY raid, and a trace that always fires is
        // worse than no trace.
        //
        // WHAT HAPPENS NOW (the carry, WO-1109 Option A): SceneRouter.GoRaid marks the live town
        // hero DontDestroyOnLoad BEFORE the Single load (same shape SceneTransitionTrigger already
        // uses for outposts / dungeons / arenas / Village2). Ensure() then finds that carried hero,
        // re-homes it out of the DDOL scene into the raid scene, and seats it at the raid's baked
        // HeroStartPoint_PlayerSpawn marker — so the raid hero IS the town hero, with its real class
        // body, gear, loadout and HP. The emergency spawn is still wired and still reachable; it is
        // now what its name says — an EMERGENCY. Its FlowTrace.Fail firing in a raid means a real
        // defect again, which is the whole point of restoring it.
        private static bool IsVillageScene(string name) =>
            !string.IsNullOrEmpty(name) && (
                name == "Village" || name.StartsWith("Village") || name.Contains("Village") ||
                name.Contains("Castle") || name.Contains("MainCastle") || name.Contains("CastleHub") ||
                DeNelle.Core.HubScenes.IsRaid(name) || name == DeNelle.Core.SceneRouter.OwnedTownIronBastion || name == DeNelle.Core.Combat.PracticeCombatPolicy.SceneName
            );

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (Instance != null) return;
            new GameObject("HeroControlEnsurer").AddComponent<HeroControlEnsurer>();
        }

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            DontDestroyOnLoad(gameObject);
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
            Begin();  // always attempt - Ensure will early-out if nothing to do; IsVillageScene only gates the Watch emergency loop
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
            SceneManager.sceneLoaded -= OnSceneLoaded;
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            Begin();  // always attempt for robustness on loads (web, title->village, editor play of Village2 etc.)
        }

        private void Begin()
        {
            Ensure();
            StopAllCoroutines();
            StartCoroutine(Watch());
        }

        private static HeroLocomotion FindLoco() =>
            FindObjectsByType<HeroLocomotion>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .FirstOrDefault();

        // Duplicate-hero guard (owner 2026-07-01): the return-to-town Single load can yield TWO heroes —
        // the carried DontDestroyOnLoad hero (SceneTransitionTrigger warps it in) PLUS the town's own baked
        // "Hero (Blaise)" (CastleHubBuilder bakes one into MainCastle_Hall). FindLoco() only ever grabs the
        // FIRST, so without this both persist. Keep ONE (prefer the carried DDOL instance = player
        // continuity; else the first) and destroy the extra root(s). Ensure() re-applies tag/components/
        // loadout to the kept hero, so keeping either is functionally safe. Runs on every sceneLoaded.
        //
        /// <summary>
        /// What the ONE hero query resolved: the hero that SURVIVED the dedupe, plus the seat
        /// freed by whatever it displaced. Both are read off the SAME snapshot, which is the
        /// whole point — see DedupeHeroes' header (WO-1131).
        /// </summary>
        private readonly struct HeroDedupeResult
        {
            public readonly HeroLocomotion Survivor;
            public readonly Vector3? DisplacedSeat;
            public HeroDedupeResult(HeroLocomotion survivor, Vector3? displacedSeat)
            {
                Survivor = survivor;
                DisplacedSeat = displacedSeat;
            }
        }

        // WO-1131 — RETURNS THE SURVIVOR, NOT JUST THE SEAT, AND THAT IS THE FIX.
        // Ensure() used to call DedupeHeroes() and then FindLoco() — asking the world for the
        // hero a SECOND time, one line after mutating it. UnityEngine.Object.Destroy is
        // DEFERRED to end of frame, so FindObjectsByType still returns the object this method
        // just destroyed, and FirstOrDefault has unspecified ordering: on the owner's
        // dg_hollow_roads entry (F8 seq 3587) the two lines read
        //     [Flow:Hero] Duplicate hero removed — destroying 'Hero (Blaise)' (scene=dg_hollow_roads)
        //     [Flow:Hero] Ensure: found existing hero 'Hero (Blaise)' (via HeroLocomotion)
        // i.e. Ensure went on to operate on the DOOMED object. Its root scene is the dungeon,
        // not "DontDestroyOnLoad", so the carried-hero guard below evaluated FALSE and
        // TryRecoverCarriedHero — the ONLY code that seats a carried hero — never ran. The
        // real (carried) hero kept its TOWN world pose, x≈-143 at the hub's portal mouth,
        // while the dungeon spans x∈[-10,10]: the hero arrived ~130m outside the composition
        // and the camera honestly reported room=none size=(0x0).
        // The cure is NOT to change how Destroy behaves (DestroyImmediate would alter
        // destruction semantics project-wide) nor to scene-filter the second query (that
        // leaves the second query, and the race shape, intact). It is to STOP ASKING TWICE:
        // this method already computed the survivor as its local `keep`, so hand that back.
        // One query, one owner, no window in which a mid-mutation world can be re-read.
        //
        // WO-1112 — ALSO RETURNS THE DISPLACED SEAT. When this destroys an IN-SCENE hero in favour
        // of a carried one, the position it destroyed is the only record of where THAT SCENE
        // wanted a hero to stand, and the carried hero still holds its TOWN world pose. A
        // composed dungeon bakes NO HeroStartPoint_PlayerSpawn marker (DungeonBaker seats its
        // rig directly at the entry room), so without this the carried hero would arrive at
        // its town coordinates inside the dungeon — outside the shell, in the dark, with the
        // exit unreachable. Returns null when nothing was displaced (the overwhelmingly common
        // case), which leaves every existing caller's behaviour byte-for-byte unchanged.
        private static HeroDedupeResult DedupeHeroes()
        {
            var heroes = FindObjectsByType<HeroLocomotion>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            if (heroes == null || heroes.Length == 0) return new HeroDedupeResult(null, null);

            HeroLocomotion keep = null;
            foreach (var h in heroes)
                if (h != null && h.gameObject.scene.name == "DontDestroyOnLoad") { keep = h; break; }
            if (keep == null)
                foreach (var h in heroes)
                    if (h != null) { keep = h; break; }
            if (keep == null) return new HeroDedupeResult(null, null);

            // Nothing to destroy — but STILL return the survivor. This is the single hero query
            // of the frame; a caller that re-queried here would re-open the very window this
            // method exists to close.
            if (heroes.Length == 1) return new HeroDedupeResult(keep, null);

            Vector3? displacedSeat = null;
            var keepRoot = keep.transform.root.gameObject;
            foreach (var h in heroes)
            {
                if (h == null) continue;
                var root = h.transform.root.gameObject;
                if (root == keepRoot) continue;
                // Record the seat of the FIRST destroyed in-scene hero (a DDOL duplicate has no
                // scene seat to donate) before the object goes away.
                if (displacedSeat == null && root.scene.name != "DontDestroyOnLoad")
                    displacedSeat = root.transform.position;
                DeNelle.Core.Diagnostics.FlowTrace.Warn("Hero",
                    $"Duplicate hero removed — destroying '{root.name}' (scene={h.gameObject.scene.name}); kept '{keepRoot.name}'.");
                Destroy(root);
            }
            // `keep` is never one of the roots destroyed above (the loop skips keepRoot), so the
            // survivor handed back here is guaranteed to be a live object — unlike anything a
            // post-Destroy FindObjectsByType would hand back this frame.
            return new HeroDedupeResult(keep, displacedSeat);
        }

        // A1 (recover, don't fabricate): re-home a REAL hero that survived a Single-scene load parked
        // in the special DontDestroyOnLoad scene. Mirrors DedupeHeroes' DDOL keying: SceneTransitionTrigger
        // marks the hero root DontDestroyOnLoad before a Single load and re-homes it into the target scene
        // LATER; and the carried root can be tag-only / renamed so FindLoco()+FindHeroByName() miss it while
        // it still lives. Move the carried root into the active scene and seat it at the hero-start marker so
        // combat reuses the real hero instead of a stand-in pill. Returns the recovered root, or null when
        // there is genuinely nothing carried to recover (the caller then spawns the emergency hero). Fail-safe:
        // every risky op is guarded so a bad object logs + is skipped, never NREs.
        //
        // WO-1112: <paramref name="fallbackSeat"/> is the seat DedupeHeroes just freed by
        // destroying this scene's own baked hero in favour of the carried one. It is used ONLY
        // when the scene bakes no hero-start marker — which is every composed (dg_*) dungeon,
        // where the baked rig's own position IS the entry seat. Without it the carried hero
        // keeps its TOWN world pose and arrives outside the dungeon shell.
        private GameObject TryRecoverCarriedHero(string activeSceneName, Vector3? fallbackSeat = null)
        {
            GameObject carried = null;

            // Prefer a carried REAL hero rig (HeroLocomotion) still parked in DDOL.
            foreach (var h in FindObjectsByType<HeroLocomotion>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (h == null) continue;
                var root = h.transform.root.gameObject;
                if (root != null && root.scene.name == "DontDestroyOnLoad") { carried = root; break; }
            }
            // Else any Player-tagged / "Hero (" root still parked in DDOL - the tag-only match that
            // FindLoco() (component) and FindHeroByName() (name prefix) would both miss.
            if (carried == null)
            {
                foreach (var t in FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                {
                    if (t == null) continue;
                    var root = t.transform.root.gameObject;
                    if (root == null || root.scene.name != "DontDestroyOnLoad") continue;
                    bool isHero = false;
                    try { isHero = root.CompareTag("Player"); } catch (UnityEngine.UnityException) { isHero = false; }
                    if (!isHero && root.name != null && root.name.StartsWith("Hero (")) isHero = true;
                    if (isHero) { carried = root; break; }
                }
            }
            if (carried == null) return null;

            // Re-home the carried root out of the special DDOL scene into the active scene so it unloads
            // normally on the next transition and combat resolves it in-scene (guarded - a MoveGameObject
            // throw must not NRE the load).
            var active = SceneManager.GetActiveScene();
            if (active.IsValid() && active.isLoaded && carried.scene != active)
            {
                try { SceneManager.MoveGameObjectToScene(carried, active); }
                catch (System.Exception e)
                {
                    DeNelle.Core.Diagnostics.FlowTrace.Warn("Hero", $"recover: MoveGameObjectToScene failed: {e.Message}");
                }
            }
            if (!carried.activeSelf) carried.SetActive(true);

            // Seat it at the scene's hero-start marker when present (Village2/raid seat the entry away from
            // the carry pose); else leave its carried world pose. Prefer the teleport-aware WarpTo so the
            // NavMeshAgent re-warps onto the destination navmesh instead of fighting a hard transform set.
            var marker = FindSpawnMarkerPosition() ?? fallbackSeat;
            if (marker.HasValue)
            {
                var loco = carried.GetComponentInChildren<HeroLocomotion>(true);
                if (loco != null)
                {
                    try { loco.WarpTo(marker.Value); }
                    catch (System.Exception) { carried.transform.position = marker.Value; }
                }
                else carried.transform.position = marker.Value;
            }

            DeNelle.Core.Diagnostics.FlowTrace.Step("Hero",
                $"recover: re-homed carried hero '{carried.name}' into active scene '{activeSceneName}' at {carried.transform.position} " +
                $"(seat={(FindSpawnMarkerPosition().HasValue ? (GameObject.Find(RaidStagingMarkerName) != null ? "STAGING marker (WO-1520)" : "baked marker") : fallbackSeat.HasValue ? "displaced baked-hero seat" : "carried pose - NO seat found")}).");
            if (!FindSpawnMarkerPosition().HasValue && !fallbackSeat.HasValue)
                DeNelle.Core.Diagnostics.FlowTrace.Warn("Hero",
                    $"recover: '{activeSceneName}' offered NO seat (no HeroStartPoint_PlayerSpawn marker and nothing displaced) - " +
                    "the carried hero keeps its ORIGIN-SCENE world pose, which in an enclosed scene means arriving outside the shell.");
            return carried;
        }

        private void Ensure()
        {
            string scene = SceneManager.GetActiveScene().name;
            DeNelle.Core.Diagnostics.FlowTrace.Step("Hero", $"Ensure begin scene='{scene}' isVillage={IsVillageScene(scene)}");
            // WO-1112: the seat a destroyed in-scene hero was standing on, if the dedupe just
            // kept a carried hero over this scene's own baked one. It is the composed dungeon's
            // only entry seat (no HeroStartPoint_PlayerSpawn is baked there).
            //
            // WO-1131: take BOTH the survivor and the seat from the ONE dedupe query. There is
            // deliberately NO FindLoco() call after this line — Destroy is deferred to end of
            // frame, so a second query here returns the hero DedupeHeroes just destroyed, and
            // Ensure then runs the whole rest of this method (including the DontDestroyOnLoad
            // carried-hero test) against a doomed object in the WRONG scene. That is exactly
            // how the dg_hollow_roads portal dropped the player 130m outside the dungeon.
            var dedupe = DedupeHeroes();
            Vector3? displacedSeat = dedupe.DisplacedSeat;
            var loco = dedupe.Survivor;
            GameObject hero = loco != null ? loco.gameObject : FindHeroByName();

            // ── WO-1109: RE-HOME THE HERO GoRaid CARRIED ─────────────────────────────
            // FindLoco()/FindHeroByName() call FindObjectsByType, which ALSO returns objects
            // parked in the special DontDestroyOnLoad scene. So a hero carried across a Single
            // load is FOUND right here and control takes the hero!=null branch below — meaning
            // TryRecoverCarriedHero, the ONLY code that re-homes + seats a carried hero, would
            // never run. Two consequences if we left it: the hero keeps its town world-pose in
            // the raid (it is never seated at HeroStartPoint_PlayerSpawn), AND it stays parked
            // in DDOL, which is a LEAK — a DDOL root survives the NEXT Single load too, so on
            // the return hop it stacks against the hub's own baked "Hero (Blaise)" and
            // DedupeHeroes has to destroy one every single transition.
            //
            // SCOPED TO RAID SCENES ON PURPOSE: this ensurer is the receiving half of the
            // GoRaid carry specifically. Every OTHER Single-load seam (outposts, dungeons,
            // arenas, Village2) is crossed by SceneTransitionTrigger, which already owns its
            // own re-home step inside RepositionPlayerAfterLoad; re-homing those from here too
            // would race a warp this file does not own. Idempotent — a hero already living in
            // the active scene fails the DDOL test and this is a no-op.
            //
            // WO-1112 — THE SAME RECEIVING HALF NOW SERVES COMPOSED (dg_*) DUNGEONS, because
            // SceneRouter.GoDungeonScene arms the SAME carry for them. It is deliberately
            // IsComposedDungeon and NOT IsDungeon: a hand-built Dungeon_* scene is crossed with
            // no carry at all (its DungeonController owns the baked hero through serialized
            // refs), so widening this test would be re-homing a hero nothing ever carried.
            // In a composed scene the carried hero also has to WIN over the baker's own bare
            // rig — DedupeHeroes above already resolves that in the carried hero's favour (it
            // keeps the DontDestroyOnLoad instance), which is exactly what we want here: the
            // baked composed Keeper has no HeroAbilities, the carried town hero does.
            bool carriedSeam = DeNelle.Core.HubScenes.IsRaid(scene)
                            || scene == DeNelle.Core.SceneRouter.OwnedTownIronBastion
                            || scene == DeNelle.Core.Combat.PracticeCombatPolicy.SceneName
                            || DeNelle.Core.HubScenes.IsComposedDungeon(scene);
            if (hero != null && carriedSeam
                && hero.transform.root.gameObject.scene.name == "DontDestroyOnLoad")
            {
                DeNelle.Core.Diagnostics.FlowTrace.Step("Hero",
                    $"Ensure: carried hero '{hero.name}' is still parked in the DontDestroyOnLoad scene on entry to '{scene}' — re-homing + seating it.");
                var rehomed = TryRecoverCarriedHero(scene, displacedSeat);
                if (rehomed != null) hero = rehomed;
                else
                    DeNelle.Core.Diagnostics.FlowTrace.Warn("Hero",
                        $"Ensure: re-home of carried hero '{hero.name}' into '{scene}' returned nothing — hero stays in DDOL (it will be deduped on the next hop).");
            }

            if (hero == null)
            {
                if (IsVillageScene(scene))
                {
                    // A1 (RECOVER before FABRICATE): before spawning a stand-in pill, try to recover a
                    // REAL hero that carried into the special DontDestroyOnLoad scene. SceneTransitionTrigger
                    // marks the hero root DontDestroyOnLoad ahead of a Single load (outpost/raid/Village2) and
                    // re-homes it into the target scene LATER (RepositionPlayerAfterLoad, after a fade + waits);
                    // if this Ensure runs first, or the carried root is tag-only / renamed so FindLoco()+
                    // FindHeroByName() miss it, we would fabricate a pill on top of a live carried hero. Re-home
                    // the carried hero into the active scene instead so combat reuses the real Knight, not a pill.
                    hero = TryRecoverCarriedHero(scene);
                    if (hero != null)
                    {
                        DeNelle.Core.Diagnostics.FlowTrace.Step("Hero",
                            $"Ensure: RECOVERED carried hero '{hero.name}' into '{scene}' - no emergency pill spawned.");
                    }
                    else
                    {
                        // In primary scene (Village2), ensure a hero exists immediately on load so
                        // camera can acquire target and not stay stuck on the tree. Watch() still
                        // monitors for later destruction.
                        DeNelle.Core.Diagnostics.FlowTrace.Warn("Hero", $"Ensure: no hero found in village scene '{scene}' - spawning emergency hero.");
                        SpawnEmergencyHero();
                        hero = FindLoco()?.gameObject ?? FindHeroByName();
                    }
                }
                if (hero == null)
                {
                    DeNelle.Core.Diagnostics.FlowTrace.Warn("Hero", $"Ensure: no hero in non-village scene '{scene}' — nothing to ensure (skipping).");
                    return;
                }
            }
            else
            {
                DeNelle.Core.Diagnostics.FlowTrace.Step("Hero", $"Ensure: found existing hero '{hero.name}' (via {(loco != null ? "HeroLocomotion" : "name")}).");
            }

            if (!hero.activeSelf) hero.SetActive(true);
            // WO-450: canonical hero tag = "Player" (now declared in TagManager). This is the
            // runtime convergence point every hero variant flows through (real/swapped/emergency),
            // so tag the root here to activate all FindWithTag("Player") consumers (camera, HUD,
            // triggers). A GameObject has ONE tag — enemy AI no longer relies on a "HeroTarget"
            // tag; it resolves the hero by component (HeroLocomotion) instead.
            if (!hero.CompareTag("Player")) hero.tag = "Player";
            if (!hero.TryGetComponent(out HeroLocomotion l)) l = hero.AddComponent<HeroLocomotion>();
            l.enabled = true;

            // The fight-capable component set (attack / health / gear / loadout). Extracted so a
            // NON-village scene owner that builds its own hero rig can provision it explicitly —
            // see EnsureHeroCombatComponents' header for the dungeon softlock that forced the split.
            EnsureHeroCombatComponents(hero, $"HeroControlEnsurer.Ensure scene='{scene}'");

            // DUNGEON GUARD (audit 2026-08-01, latent race): a scene carrying a DungeonCameraRig
            // owns its own camera (the Cinemachine iso/FPV rig driven by DungeonController).
            // Everything below this point is CAMERA TAKEOVER — SmartMobileCamera attach,
            // CinemachineBrain disable, EnforceSoleCamera, and a hard camera-transform write —
            // and would stomp that rig; today it only misses in dungeons by load-order timing.
            // Make the skip deterministic: all hero combat/gear ensures above still ran, ONLY
            // the camera mutation is skipped. The rig type is resolved by reflection because
            // DeNelle.Village cannot reference DeNelle.Dungeons (Dungeons already references
            // Village; a direct type ref would be a circular asmdef dependency).
            if (DungeonCameraRigPresent())
            {
                DeNelle.Core.Diagnostics.FlowTrace.Step("HeroEnsure",
                    "dungeon camera rig present -- camera takeover skipped");
                Debug.Log($"[HeroControlEnsurer] ensured hero='{hero.name}' active={hero.activeInHierarchy} locoEnabled={l.enabled} (dungeon rig owns the camera).");
                return;
            }

            // Find the primary gameplay camera (prefer tagged MainCamera or "main"/"game" in name, enabled, no render texture).
            // This is more reliable than Camera.main alone in editor or complex Village2 setups.
            Camera cam = Camera.main;
            if (cam == null || cam.GetComponent<SmartMobileCamera>() != null)
            {
                foreach (var c in FindObjectsByType<Camera>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
                {
                    if (c == null || !c.enabled || c.targetTexture != null) continue;
                    if (c.CompareTag("MainCamera") || c.name.ToLower().Contains("main") || c.name.ToLower().Contains("game") || c.name.ToLower().Contains("camera"))
                    {
                        cam = c;
                        break;
                    }
                }
            }
            // If STILL no gameplay camera (Village2 + any builder scene with none baked), CREATE one so the
            // hero is never left camera-less ("structure but no camera" — the Village2 arrival symptom).
            // Tagged MainCamera; the SmartMobileCamera wiring below makes it follow the hero on arrival.
            // Add an AudioListener only if the scene lacks one (avoid the two-listeners warning).
            if (cam == null)
            {
                var camGo = new GameObject("GameplayCamera (ensured)");
                cam = camGo.AddComponent<Camera>();
                camGo.tag = "MainCamera";
                if (FindFirstObjectByType<AudioListener>() == null) camGo.AddComponent<AudioListener>();
                Debug.Log("[HeroControlEnsurer] no gameplay camera in scene — created one (Village2 etc.) so the hero is followed.");
            }

            // WO-920 — DUNGEON CLEAR COLOUR. Reaching this line in a DUNGEON scene means the
            // scene baked no camera of its own (the composed dg_* bake and the hand-coded
            // outpost both deliberately seat none — DungeonBaker L230-237,
            // KayKitChallengeOutpostBuilder L77-78 — and a scene that DOES carry a
            // DungeonCameraRig already returned at L256 above). So this runtime camera IS the
            // dungeon's camera, and until now it kept Unity's defaults: clearFlags=Skybox with
            // backgroundColor #314D79. WO-919 then nulled RenderSettings.skybox to kill the sky
            // dome (DungeonBaker L238) — and a null skybox makes CameraClearFlags.Skybox fall
            // back to clearing with backgroundColor, i.e. that DEFAULT BLUE. Net effect: every
            // enclosed, relit dungeon still cleared to daylight blue behind its geometry, so any
            // hairline in the shell read as "sky" and the whole relight was undersold.
            // SCOPED TO DUNGEONS ON PURPOSE: the overworld/hub/raid scenes need their real
            // skybox, so this must never run there. Colour is not re-typed here — it comes from
            // the one authority (DungeonCameraProfile.ClearColor, itself sourced from the proven
            // hand-built dungeon background at DungeonSceneBuilder L2067).
            if (cam != null && DeNelle.Core.HubScenes.IsDungeon(scene))
            {
                var priorFlags = cam.clearFlags;
                var priorColor = cam.backgroundColor;
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = DeNelle.Core.World.DungeonCameraProfile.ClearColor;
                DeNelle.Core.Diagnostics.FlowTrace.Step("DungeonCam",
                    $"clear: scene='{scene}' cam='{cam.name}' {priorFlags}/{ColorUtility.ToHtmlStringRGB(priorColor)} " +
                    $"-> SolidColor/#{ColorUtility.ToHtmlStringRGB(cam.backgroundColor)} " +
                    $"(skybox={(RenderSettings.skybox != null ? RenderSettings.skybox.name : "null")})");
            }

            if (cam != null && cam.GetComponent<SmartMobileCamera>() == null)
            {
                cam.gameObject.AddComponent<SmartMobileCamera>();
                Debug.Log($"[HeroControlEnsurer] runtime-attached SmartMobileCamera to gameplay camera '{cam.name}'");
            }

            // Disable CinemachineBrain if present (can override transform in some Village2 setups).
            // Our SmartMobile follow + ForceFollowImmediate should control the view.
            var brain = cam != null ? cam.GetComponent("CinemachineBrain") as Behaviour : null;
            if (brain != null && brain.enabled)
            {
                brain.enabled = false;
                Debug.Log("[HeroControlEnsurer] disabled CinemachineBrain on gameplay camera");
            }

            // Force sole camera and high depth so this is the one used in Game view.
            var smc2 = cam != null ? cam.GetComponent<SmartMobileCamera>() : null;
            if (smc2 != null)
            {
                smc2.EnforceSoleCamera();
            }

            // Wire the authoritative camera (SmartMobileCamera) so it follows the hero.
            // Legacy VillageCamera is a fallback (SmartMobileCamera disables it at runtime).
            // This ensures the camera is never left "stuck on tree" (origin) when hero is
            // ensured/recovered — especially important on WebGL clean loads where timing/tags
            // can cause SmartMobileCamera's own TryFindHero fallback to miss on first frames.
            var smc = cam != null ? cam.GetComponent<SmartMobileCamera>() : FindFirstObjectByType<SmartMobileCamera>();
            if (smc != null)
            {
                smc.SetTarget(hero.transform);
                smc.ForceFollowImmediate();  // ensure instant snap off the tree on load
            }
            else
            {
                var legacyCam = FindFirstObjectByType<VillageCamera>();
                if (legacyCam != null) legacyCam.SetTarget(hero.transform);
            }

            // Quick direct force (easier workaround while full follow is stabilizing):
            // Explicitly move the gameplay camera to a follow position behind the hero.
            // This guarantees the view leaves the tree immediately on load in Village2,
            // even if LateUpdate follow or additive scene is interfering.
            if (cam != null && hero != null)
            {
                Vector3 followOffset = new Vector3(0f, 5f, -10f); // simple behind/above the hero
                cam.transform.position = hero.transform.position + followOffset;
                cam.transform.LookAt(hero.transform.position + Vector3.up * 1.8f); // look at hero chest/head
                Debug.Log("[HeroControlEnsurer] direct force: camera moved to follow hero at " + cam.transform.position);
            }

            Debug.Log($"[HeroControlEnsurer] ensured hero='{hero.name}' active={hero.activeInHierarchy} locoEnabled={l.enabled}.");
        }

        // ── Fight-capable provisioning (the component-attach half of Ensure) ──────
        /// <summary>
        /// Attaches the components that make a hero rig FIGHT-CAPABLE — the melee swing
        /// (<see cref="PlayerAttackController"/>), the damage sink (<see cref="HeroHealth"/>),
        /// gear, loadout and the combat-readability bits. NO camera takeover, NO locomotion
        /// injection, NO emergency spawn, NO scene-name gate: it provisions exactly the rig it
        /// is handed.
        ///
        /// WHY THIS IS PUBLIC + SPLIT OUT (F8 2026-08-05, dungeon unplayable from the first
        /// encounter — PROVEN from device capture, not inferred):
        ///   15:25:37.019 [Flow:Hero] Ensure: no hero in non-village scene
        ///                            'Dungeon_HealersCottage' - nothing to ensure (skipping).
        ///   15:25:56.x  [Flow:HudKit] attack fired but no PlayerAttackController in scene (x5)
        ///   enemy: 77x Idle_A while 69x inRange=True — aware, in range, idle, nothing to hit.
        /// The Keeper staged into <c>BattleArena</c> as a PARTIAL hero: 'Player'-tagged but with
        /// no attack controller (she could not damage the enemy) and no HeroHealth (EnemyBrain
        /// deals damage ONLY through HeroHealth, so the enemy could not damage her). A mutual
        /// null-target deadlock: the fight could never resolve, so BattleLock never released.
        ///
        /// TWO compounding causes, which is why a scene-name clause alone does NOT fix it:
        ///   (a) <see cref="IsVillageScene"/> does not match 'Dungeon_*', so <see cref="Ensure"/>
        ///       early-returns; AND
        ///   (b) at sceneLoaded the dungeon Keeper has no <see cref="HeroLocomotion"/> yet —
        ///       HeroBodySwapper injects it ~160ms later — so even a widened scene gate would
        ///       find no hero (FindLoco) and skip anyway, and Ensure only re-runs on the next
        ///       sceneLoaded, which never comes (the arena stages ADDITIVELY).
        /// So the dungeon owns the call: DungeonController invokes this ONCE, after the body
        /// swap has landed. Widening IsVillageScene is deliberately NOT done — it also gates the
        /// Watch() emergency-respawn loop, which would fabricate a lavender emergency pill in
        /// every dungeon during the ~160ms window where FindLoco() is legitimately null.
        ///
        /// IDEMPOTENT: every attach is behind a GetComponent null-check and the types that matter
        /// are [DisallowMultipleComponent], so re-calling this is a no-op, never a duplicate-attach.
        /// </summary>
        /// <param name="hero">The hero ROOT to provision (the transform the camera follows).</param>
        /// <param name="reason">Caller context — appears verbatim in the FlowTrace line.</param>
        public static void EnsureHeroCombatComponents(GameObject hero, string reason)
        {
            // No silent failures (CLAUDE.md §12): a null rig means NOTHING was provisioned, and a
            // fight staged on it can never resolve. Say so loudly instead of returning quietly.
            if (hero == null)
            {
                DeNelle.Core.Diagnostics.FlowTrace.Fail("HeroEnsure",
                    $"EnsureHeroCombatComponents({reason}): hero rig is NULL — nothing provisioned. " +
                    "Any fight staged against this rig would be unwinnable.");
                return;
            }

            // WO-450: canonical hero tag. Re-asserted here (not only in Ensure) because this entry
            // point is reached by rigs Ensure never sees — e.g. the dungeon Keeper.
            if (!hero.CompareTag("Player")) hero.tag = "Player";

            if (hero.GetComponent<HeroDeathLogger>() == null) hero.AddComponent<HeroDeathLogger>();
            // Open-world combat readability: reticle over the nearest hostile target.
            if (hero.GetComponent<HeroTargetIndicator>() == null) hero.AddComponent<HeroTargetIndicator>();
            // DEF (combat feel): wire the melee swing that was BUILT but never attached.
            // PlayerAttackController.Awake self-configures (_enemyLayer -> "Enemy", animator/audio),
            // so a bare AddComponent is safe. Melee fires on Space / gamepad-South. NOTE: added for
            // EVERY class right now (the Knight's sword was the ask); Mage/Ranger get a melee with no
            // swing anim (their animators lack the Attack trigger — damage still lands). Restrict to
            // Knight later if desired.
            // ORDERING NOTE: its Awake caches HeroLocomotion + ActorAnimator off this rig, so a
            // caller that builds its body asynchronously MUST call this AFTER the body swap.
            bool addedAttack = hero.GetComponent<PlayerAttackController>() == null;
            if (addedAttack) hero.AddComponent<PlayerAttackController>();
            // WO-VFX-WEAPON-TRAILS: the shared blade-trail flash on every swing/cast (self-drives off
            // ActorAnimator.AttackStarted). PlayerAttackController.Awake also ensures it; this explicit
            // add guarantees it on the hero rig even for a class/path that skips the attack controller.
            // DisallowMultipleComponent makes a double-add a no-op.
            if (hero.GetComponent<WeaponTrailController>() == null) hero.AddComponent<WeaponTrailController>();
            // Default gear stats (even for emergency capsule or non-swapped heroes): GearLoadout
            // pulls level-1 starters from GearCatalog (now populated) and drives WeaponMult/ArmorDefense.
            if (hero.GetComponent<GearLoadout>() == null) hero.AddComponent<GearLoadout>();
            // ARMOR RENDER (HeroArmorVisual): universal registration so EVERY hero variant shows
            // equipped Blink armor on the body — including the Mage/default body (which HeroBodySwapper
            // skips) and a non-swapped hero. It self-guards: with no humanoid "HeroBody" (e.g. the
            // emergency capsule) it simply keeps the existing body (never naked). Subscribes to
            // GearLoadout.OnGearChanged; [DisallowMultipleComponent] makes a double-add a no-op.
            if (hero.GetComponent<HeroArmorVisual>() == null) hero.AddComponent<HeroArmorVisual>();
            // LOADOUT (HeroLoadout): the per-hero W/E/R equipped-ability map (Knight skill-tree
            // spine). It was BUILT (HeroLoadout + HeroLoadoutAccess + the chooser VM/View) but NEVER
            // attached to the hero — so HeroLoadoutAccess.Current resolved null and every Equip(W/E/R)
            // silently no-op'd (owner: "can't assign unlocked weapon skills"). Adding it here makes
            // its Awake() run -> Load() from PlayerPrefs -> HeroLoadoutAccess.Current resolves, and
            // all three W/E/R slots equip + persist. Re-added each Ensure() so a recreated/scene-
            // reloaded hero restores its saved loadout from PlayerPrefs (DisallowMultipleComponent
            // makes a double-add a no-op).
            if (hero.GetComponent<HeroLoadout>() == null) hero.AddComponent<HeroLoadout>();
            // Persistence belt-and-suspenders: a freshly-added HeroLoadout restores via its own
            // Awake->Load, but a hero that PERSISTS across the scene load (carried/DDOL) won't re-run
            // Awake — so replay the PlayerPrefs load here to guarantee the saved W/E/R loadout is
            // restored after every (re)ensure. PlayerPrefs is the source of truth (Equip saves it
            // immediately), so this is idempotent.
            var heroLoadout = hero.GetComponent<HeroLoadout>();
            heroLoadout?.ReloadFromPrefs();

            // HOT-SWAP BAR (AssignableSkillBar, WO-1019): the same guarantee for the OTHER
            // persisted ability rail. It used to be reached only lazily via
            // AssignableSkillBarAccess (which auto-adds it), so a hero that PERSISTS across a
            // scene load never re-ran its Awake->Load and kept whatever the previous hero left in
            // memory - the owner's "he inherits the hotswap from previous character". Its key is
            // now per-class, so this reload lands on THIS hero's bar.
            // DisallowMultipleComponent makes the add a no-op when present.
            if (hero.GetComponent<AssignableSkillBar>() == null) hero.AddComponent<AssignableSkillBar>();
            hero.GetComponent<AssignableSkillBar>()?.ReloadFromPrefs();

            // DAMAGE SINK (F8 2026-08-05): HeroHealth is the ONLY way anything hurts the hero —
            // EnemyBrain/Enemy resolve the player as IDamageableStructure through this component.
            // Until now it was attached exclusively by HeroHealthBootstrap, which keys off a
            // HeroAbilities component; a composed dungeon Keeper deliberately carries NO
            // HeroAbilities (see GearLoadout.CurrentJob's header), so it NEVER got HeroHealth and
            // was literally invulnerable — captured as
            //   "SeedHeroVitalsFromLiveHero: no live HeroHealth on 'Keeper' nor HeroHealth.Instance
            //    - falling back to the 120 HP placeholder."
            // Attaching it here makes every provisioned rig damageable. In the village this is the
            // SAME GameObject the bootstrap would have used (VillageSceneBuilder.BuildHero puts
            // HeroAbilities on the hero root), so the bootstrap simply finds it already present and
            // skips — no duplicate ([DisallowMultipleComponent] would reject one anyway).
            bool addedHealth = hero.GetComponent<HeroHealth>() == null;
            if (addedHealth) hero.AddComponent<HeroHealth>();
            // HeroHitReaction rode along with the bootstrap's HeroHealth attach (damage screen
            // flash + death slow-mo). The bootstrap early-returns as soon as HeroHealth.Instance is
            // set, so it must be attached HERE too or provisioning HeroHealth above would silently
            // cost the village its hit feedback.
            if (hero.GetComponent<HeroHitReaction>() == null) hero.AddComponent<HeroHitReaction>();

            // DEF-205: the always-on blue ground "reach ring" read as a mystery indicator
            // while walking (players couldn't tell what it meant). Removed — do NOT attach
            // HeroReachRing. The class is kept (HeroReachRing.cs) in case a gated, opt-in
            // reach hint is wanted later, but it must NOT render during normal play.
            // (Intentionally not adding HeroReachRing here.)

            // PROOF LINE (§12): the next capture must be able to answer "did the hero get
            // provisioned, and could this fight ever have been won?" without re-reading code.
            DeNelle.Core.Diagnostics.FlowTrace.Step("HeroEnsure",
                $"combat components ensured on '{hero.name}' ({reason}) — " +
                $"attack={(addedAttack ? "ADDED" : "present")} health={(addedHealth ? "ADDED" : "present")} " +
                $"loco={(hero.GetComponent<HeroLocomotion>() != null)} " +
                $"scene='{SceneManager.GetActiveScene().name}'.");
        }

        // Re-check while in the village; if the hero is gone, spawn an emergency one.
        private IEnumerator Watch()
        {
            int spawns = 0;
            while (IsVillageScene(SceneManager.GetActiveScene().name))
            {
                yield return new WaitForSeconds(0.5f);
                if (FindLoco() == null && spawns < MaxEmergencySpawns)
                {
                    DeNelle.Core.Diagnostics.FlowTrace.Warn("Hero", $"Watch: hero vanished mid-scene '{SceneManager.GetActiveScene().name}' — emergency respawn #{spawns + 1}/{MaxEmergencySpawns}.");
                    SpawnEmergencyHero();
                    spawns++;
                }
            }
        }

        // TKT-8: the scene's hero-start marker (raid/Village2 scenes seat the entry away from the hub
        // spot). Returns the marker position (capsule centre) or null so the caller keeps its hub fallback.
        // WO-1520: a raid base bakes a STAGING marker ("RaidStagingPoint") whose distance
        // RaidBaseGenerator COMPUTES to sit outside every turret's reach and every defender's
        // awareness radius, and it is preferred over HeroStartPoint_PlayerSpawn wherever it
        // exists. It is a SEPARATE marker rather than a moved one on purpose: the old entry
        // point stays where it is for anything that reads it (and for non-raid scenes, which
        // bake no staging marker and are unaffected by this line).
        //
        // MEASURED BEFORE: [Flow:Hero] "recover: re-homed carried hero (0.00, 0.08, -39.00)
        // (seat=baked marker)" - the raider_camp_small entry marker at -39 m, which is 6.4 m
        // INSIDE the outer turret band's ~45.4 m reach. The hero was under fire on frame one.
        private const string RaidStagingMarkerName = "RaidStagingPoint";

        // TKT-8: the scene's hero-start marker (raid/Village2 scenes seat the entry away from the hub
        // spot). Returns the marker position (capsule centre) or null so the caller keeps its hub fallback.
        private static Vector3? FindSpawnMarkerPosition()
        {
            var marker = GameObject.Find(RaidStagingMarkerName);
            if (marker == null) marker = GameObject.Find("HeroStartPoint_PlayerSpawn");
            if (marker == null) marker = GameObject.Find("HeroStartPoint_InsidePersonalQuarters");
            if (marker == null) return null;
            return marker.transform.position + Vector3.up * 0.9f;
        }

        private void SpawnEmergencyHero()
        {
            // §12 ticket #2: the purple emergency pill (no collider, falls through ground) IS this object.
            // Fail-log WHICH scene loses the carried hero so we know if the garrison warp drops it (vs the
            // pill being the normal arrival). If this fires in a Garrison_* scene, the carry/warp is the bug.
            DeNelle.Core.Diagnostics.FlowTrace.Fail("Hero",
                $"EMERGENCY pill spawned in scene '{UnityEngine.SceneManagement.SceneManager.GetActiveScene().name}' — carried hero not found.");

            // A2 (no bare pill): build the emergency hero as a baked hero does - an EMPTY root with a
            // child named "HeroBody". The attached HeroBodySwapper (below) finds + REPLACES that "HeroBody"
            // child with the player's real animated class FBX (Knight -> ff.knightv3 -> KnightV3.fbx ->
            // KnightMocap.controller), exactly like DungeonSceneBuilder / FolksGranaryBuilder do. So even on
            // the fabricate path the hero becomes the real Knight, not a capsule. HeroLocomotion also looks
            // for a "HeroBody" child, so this structure is the canonical one.
            var go = new GameObject("Hero (Blaise)"); // so camera / NPCs find it by name
            go.tag = "Player";                        // WO-450: canonical hero tag for all consumers
            // TKT-8: seat at the scene's hero-start marker when present (Village2 / raid scenes put the
            // entry point elsewhere — the old hardcoded (6,1,4) put the hero off-map there). Falls back
            // to MainCastle_Hall's courtyard spot (6, liftY+1, 4): WO-593 raised the castle onto its
            // plinth (PlayerPrefs "castle.liftY", default 3), so the old fixed y=1 was 2m UNDER the
            // raised floor. The fallback rides the same tunable base the builder authors from.
            go.transform.position = FindSpawnMarkerPosition()
                ?? new Vector3(6f, UnityEngine.PlayerPrefs.GetFloat("castle.liftY", 3f) + 1f, 4f);

            // The interim visual stand-in (only shown until HeroBodySwapper swaps in the Knight, or if that
            // swap can't load an FBX). Child named "HeroBody" so the swapper destroys + replaces it.
            var body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            body.name = "HeroBody";
            body.transform.SetParent(go.transform, false);

            // Drop the primitive collider so it doesn't block the hero. In a raid the NAVMESH is the
            // only thing that blocks the hero - colliders do not stop her movement (HeroLocomotion is a
            // kinematically driven NavMeshAgent with NoObstacleAvoidance, containing zero Physics/Raycast
            // /CapsuleCast references; see HeroLocomotion.cs:1488-1489, :999).
            var col = body.GetComponent<Collider>();
            if (col != null) Destroy(col);

            // B3 (NEVER magenta): do NOT degrade to Shader.Find("Standard") - a Standard material renders
            // MAGENTA under URP in a stripped player build (the exact bug). Route through MagentaGuard's
            // robust URP/Lit builder, which resolves the shader via Shader.Find and, if that returns null in
            // a stripped build, BORROWS a URP/Lit shader already serialized in the loaded scene. Keep the
            // intended lavender tint. If no URP shader can be resolved at all it returns null; leave the
            // primitive's own material rather than force a magenta Standard.
            var mr = body.GetComponent<Renderer>();
            var lavender = new Color(0.60f, 0.45f, 0.85f);
            var m = DeNelle.Core.MagentaGuard.BuildUrpLitMaterial(lavender);
            if (m != null && mr != null) mr.sharedMaterial = m;

            go.AddComponent<HeroLocomotion>();
            go.AddComponent<HeroDeathLogger>();   // catch it too, in case the destroyer is periodic
            go.AddComponent<HeroTargetIndicator>();
            go.AddComponent<HeroLoadout>();        // emergency hero also gets the W/E/R loadout (loads from PlayerPrefs)

            // A2 (belt-and-suspenders): attach HeroBodySwapper so the "HeroBody" capsule child above is
            // replaced by the player's real animated class body at runtime - the same component the baked
            // dungeon/granary/hub heroes carry. Direct AddComponent (both types are in DeNelle.Village, so no
            // reflection is needed); guarded so a throw leaves the lavender stand-in in place rather than NRE.
            try
            {
                if (go.GetComponent<HeroBodySwapper>() == null)
                    go.AddComponent<HeroBodySwapper>();
            }
            catch (System.Exception e)
            {
                DeNelle.Core.Diagnostics.FlowTrace.Warn("Hero",
                    $"emergency hero: HeroBodySwapper attach failed ({e.Message}) - keeping the lavender stand-in body.");
            }

            // Wire camera for emergency hero (prefer modern SmartMobileCamera; legacy fallback).
            // Prevents "camera stuck on tree" in recovery scenarios on WebGL / clean loads.
            var smc = FindFirstObjectByType<SmartMobileCamera>();
            Transform camTarget = null;
            if (smc != null)
            {
                smc.SetTarget(go.transform);
                camTarget = go.transform;
            }
            else
            {
                var legacyCam = FindObjectsByType<VillageCamera>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                                .FirstOrDefault();
                if (legacyCam != null)
                {
                    legacyCam.SetTarget(go.transform);
                    camTarget = go.transform;
                }
            }

            Debug.LogWarning($"[HeroControlEnsurer] real hero missing — spawned EMERGENCY movable hero at " +
                             $"{go.transform.position}; camera retargeted={(camTarget != null)}.");
        }

        private static GameObject FindHeroByName()
        {
            foreach (var t in FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (t != null && t.name.StartsWith("Hero (")) return t.gameObject;
            return null;
        }

        // ── Dungeon camera-rig detection (audit 2026-08-01) ──────────────────
        // Cached reflection lookup for DeNelle.Dungeons.DungeonCameraRig — same idiom as
        // the CinemachineBrain string-typed GetComponent above: this assembly must not
        // reference DeNelle.Dungeons. The type search runs once per domain (a null result
        // is cached too, so non-dungeon builds pay one scan, then a bool check).
        private static System.Type _dungeonCameraRigType;
        private static bool _dungeonCameraRigTypeResolved;

        private static bool DungeonCameraRigPresent()
        {
            if (!_dungeonCameraRigTypeResolved)
            {
                _dungeonCameraRigTypeResolved = true;
                foreach (var asm in System.AppDomain.CurrentDomain.GetAssemblies())
                {
                    _dungeonCameraRigType = asm.GetType("DeNelle.Dungeons.DungeonCameraRig");
                    if (_dungeonCameraRigType != null) break;
                }
            }
            if (_dungeonCameraRigType == null) return false;
            return FindAnyObjectByType(_dungeonCameraRigType, FindObjectsInactive.Exclude) != null;
        }
    }

    /// <summary>Diagnostic: logs when (and from where, if DestroyImmediate) the hero dies.</summary>
    public sealed class HeroDeathLogger : MonoBehaviour
    {
        private void OnDestroy()
        {
            // Diagnostic retired: the hero-deletion bug is fixed, and this fired
            // (harmlessly) on every normal scene-unload. Only warn if the hero dies
            // while the Village is still the active scene (i.e. an unexpected delete).
            if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().name == "Village2")
                Debug.LogWarning($"[HeroDeathLogger] '{gameObject.name}' destroyed while in Village " +
                                 $"(frame={Time.frameCount}) — unexpected; investigate.");
        }
    }
}

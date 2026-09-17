// =============================================================================
// DungeonExitInteractable + DungeonExitSpawner - the composed-dungeon RETURN exit.
// -----------------------------------------------------------------------------
// SHIP-BLOCKER DG-01: a composed playable dungeon (GraphDungeonComposer /
// DungeonBaker.PopulateForPlay) seats a Player-tagged hero + hero-aggro enemy
// spawners but NO way out - the player is trapped ("roach motel", must force-quit).
// The rich Dungeon_HealersCottage scene leaves via DungeonController.ExitToVillage;
// the COMPOSED scenes (DungeonCompose_*) carry no DungeonController, so nothing
// routes home.
//
// FIX (runtime bootstrap - NO re-bake required): DungeonExitSpawner hooks
// SceneManager.sceneLoaded once (RuntimeInitializeOnLoadMethod) and, for every
// scene whose root is "DungeonCompose_*" (the composer's root name, DungeonBaker
// line ~118), injects a DungeonExitInteractable at the entry room. This covers the
// ALREADY-BAKED dg_starter_loop.unity on disk with no re-bake, and every future
// composed dungeon automatically. It is idempotent - if an exit already exists in
// the scene (a future bake-time exit, or a prior inject) it skips.
//
// The exit routes HOME exactly like DungeonController.ExitToVillage:
//   SceneRouter.LoadSceneWithFade(SceneRouter.Castle)  (the merged overworld hub).
//
// Instrumented per CLAUDE.md sec.12 - every step/branch emits [Flow:DungeonExit].
// =============================================================================

using System;
using Cysharp.Threading.Tasks;
using DeNelle.Core;
using DeNelle.Core.Diagnostics;
using DeNelle.Core.UI;
using DeNelle.Core.World;
using DeNelle.Dungeons.RoomForge;
using DeNelle.Village;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.SceneManagement;

namespace DeNelle.Dungeons
{
    /// <summary>
    /// Auto-installer: injects a <see cref="DungeonExitInteractable"/> into every
    /// composed ("DungeonCompose_*") dungeon scene at load, so a portal-loaded
    /// composed dungeon is never a trap. Self-arming, idempotent, no re-bake.
    /// </summary>
    internal static class DungeonExitSpawner
    {
        private const string Sys = "DungeonExit";
        private static bool s_hooked;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (s_hooked) return;
            s_hooked = true;
            SceneManager.sceneLoaded += OnSceneLoaded;
            // A build that boots straight into a composed dungeon has already loaded
            // its scene before this hook - process the active scene once, too.
            TryInject(SceneManager.GetActiveScene());
            FlowTrace.Step(Sys, "installed sceneLoaded hook (composed-dungeon exit auto-inject)");
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode) => TryInject(scene);

        private static void TryInject(Scene scene)
        {
            if (!scene.IsValid() || !scene.isLoaded) return;

            GameObject[] roots;
            try { roots = scene.GetRootGameObjects(); }
            catch (Exception ex) { FlowTrace.Warn(Sys, $"GetRootGameObjects failed for '{scene.name}': {ex.Message}"); return; }

            Transform composeRoot = null;
            for (int i = 0; i < roots.Length; i++)
            {
                var go = roots[i];
                if (go != null && go.name.StartsWith("DungeonCompose_", StringComparison.Ordinal))
                {
                    composeRoot = go.transform;
                    break;
                }
            }
            // Not a composed dungeon (Dungeon_HealersCottage and the hub scenes carry
            // their own exit) - nothing to do.
            if (composeRoot == null) return;

            // Idempotent: never add a SECOND return exit. WO-1001 slice 8 made this subtle - baked
            // per-floor EXTRACT PADS are also DungeonExitInteractables, so a bare
            // FindAnyObjectByType made the injector skip on every composed dungeon that authors
            // extracts. Those pads sit on the stair landings, all BELOW floor 0 (dg_ember_deep has
            // five, none on the entry floor), so the entry room was left with no way out at all.
            // Match only a previously-INJECTED return exit, which Spawn names "DungeonExit ...";
            // the baker renames pads to "Extract_<id>" (DungeonBaker.PlaceComposeExtracts).
            var existing = UnityEngine.Object.FindObjectsByType<DungeonExitInteractable>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (int i = 0; i < existing.Length; i++)
            {
                if (existing[i] == null) continue;
                if (!existing[i].name.StartsWith("Extract_", System.StringComparison.Ordinal))
                {
                    FlowTrace.Step(Sys, $"return exit already present in '{scene.name}' ('{existing[i].name}') - skip inject");
                    return;
                }
            }
            if (existing.Length > 0)
                FlowTrace.Step(Sys, $"'{scene.name}' has {existing.Length} extract pad(s) but no return exit - injecting one at the entry");

            Vector3 pos = ResolveExitPosition(composeRoot);
            var exit = DungeonExitInteractable.Spawn(pos);
            // Push the Player-tagged hero (canon §7) so the prompt/walk-in never depends on a
            // HeroLocomotion mover-type lookup (which excludes a disabled/neutralized component).
            var taggedHero = GameObject.FindGameObjectWithTag("Player");
            if (taggedHero != null) exit.SetHero(taggedHero.transform);
            FlowTrace.Step(Sys, $"injected RETURN exit into composed scene '{scene.name}' at {pos} " +
                $"(routes -> SceneRouter.Castle, hero={(taggedHero != null ? "tagged" : "unresolved")})");
        }

        // WO-957: the exit seats at the layout's DESIGNATED true-exit room (exitRoomId,
        // schema v2) - the ONE place that wears the full arch + beacon. An unauthored /
        // unresolvable designation falls back to the ENTRY room (where the hero spawns),
        // the pre-multi-floor behavior, nudged a few metres off the exact hero seat so
        // the walk-in trigger does not fire on the spawn frame.
        private static Vector3 ResolveExitPosition(Transform composeRoot)
        {
            Vector3 basePos;
            string exitRoomId = LoadExitRoomId(composeRoot.name);
            Transform seat = FindRoomChild(composeRoot, exitRoomId);
            if (seat != null)
            {
                FlowTrace.Step(Sys, $"true exit seats at designated room '{exitRoomId}' (layout exitRoomId)");
            }
            else
            {
                if (!string.IsNullOrEmpty(exitRoomId))
                    FlowTrace.Warn(Sys, $"designated exit room '{exitRoomId}' not found under " +
                        $"'{composeRoot.name}' - falling back to the entry room");
                seat = FindRoomChild(composeRoot, "entry");
            }
            if (seat != null)
            {
                basePos = seat.position;
            }
            else
            {
                // Fallback: the Player-tagged hero seat, then origin.
                var hero = GameObject.FindGameObjectWithTag("Player");
                basePos = hero != null ? hero.transform.position : composeRoot.position;
                FlowTrace.Warn(Sys, "no 'entry' room found - seating exit at hero/root fallback");
            }
            // Nudge off the hero seat (entry node sits at origin; hero is seated on it).
            return basePos + new Vector3(0f, 0f, -2.6f);
        }

        private static Transform FindRoomChild(Transform composeRoot, string roomId)
        {
            if (string.IsNullOrEmpty(roomId)) return null;
            foreach (Transform child in composeRoot)
            {
                if (child != null && string.Equals(child.name, roomId, StringComparison.OrdinalIgnoreCase))
                    return child;
            }
            return null;
        }

        // Layout JSON (Resources dual-copy, same load path as DungeonRoomBinder) ->
        // exitRoomId. Null-safe: a missing/unparseable/pre-v2 layout just means the
        // entry-room default (never blanks the exit).
        private static string LoadExitRoomId(string composeRootName)
        {
            string dungeonId = composeRootName.Substring("DungeonCompose_".Length);
            var text = Guard.Try(Sys, $"load layout '{dungeonId}' for exitRoomId",
                () => Resources.Load<TextAsset>("Data/Canonical/dungeon-layouts/" + dungeonId), null);
            if (text == null)
            {
                FlowTrace.Step(Sys, $"layout '{dungeonId}' not in Resources - true exit uses the entry-room default");
                return null;
            }
            var layout = Guard.Try(Sys, $"parse layout '{dungeonId}' for exitRoomId",
                () => JsonConvert.DeserializeObject<DungeonComposeLayout>(text.text), null);
            return layout != null ? layout.exitRoomId : null;
        }
    }

    /// <summary>
    /// The in-world RETURN affordance for a composed dungeon: a glowing home-arch the
    /// hero can walk into (or tap the shared Interact button) to route back to the
    /// castle hub. Mirrors DungeonPortal's mobile-first proximity/button pattern.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class DungeonExitInteractable : MonoBehaviour
    {
        private const string Sys = "DungeonExit";
        // WO-797/F8 seq 622: prompt range widened 3.0 -> 4.5 so the Interact button arms
        // before the hero is standing in the arch (MinTouch-friendly reach; the mob camp
        // made a tight radius unreachable). Walk-in radius unchanged - enlarging IT would
        // yank players home by accident.
        private const float ActivateRadius = 4.5f;   // shared-button prompt range
        private const float TriggerRadius = 2.0f;    // walk-in trigger radius
        private const float CheckInterval = 0.15f;
        // WO-995: boot-in self-evict. The hero can spawn inside (or inches outside) the exit
        // sphere; _armed alone was not enough when the first proximity sample was already
        // "clear" and the next physics step shoved the collider back in. A short scene-load
        // grace plus a sustained-clear arm stops Leave() on spawn without breaking a real walk-in.
        private const float BootGraceSeconds = 2.0f;
        private const float ClearHoldSeconds = 0.35f;

        private Transform _hero;
        private bool _heroFound;
        private bool _isInRange;
        private bool _leaving;
        private bool _armed;                          // walk-in only after hero first steps clear
        private float _nextProximityCheck;
        private float _clearSince = -1f;              // realtime when first observed clear of the volume
        private bool _bootTraceEmitted;
        // WO-987: touch raises an Obsidian confirm; Leave only after "Continue to exit".
        private bool _confirmOpen;
        private GameObject _confirmCanvas;
        // Top-band modal (34000): the arbiter must see it or back-button/battle-lock route
        // around an invisible dialog. Register lazily; NotifyOpened can REJECT (WO-437) and
        // invokes the handle's Close on its way out (the DungeonTreasurePanel precedent).
        private PanelHandle _confirmHandle;
        private DungeonRuntimeState _bossGateState;
        private GameObject _bossSealVisual;

        // WO-770.1: a RICH dungeon (DungeonController) supplies a leave action so the exit routes
        // through ExitToVillage (banks the run's crafting scatter + ends the run cleanly) instead
        // of the composed-scene default (direct SceneRouter.Castle). Null => composed-scene behavior.
        private System.Action _onLeave;               // delegate - intentionally NOT serializable
        // WO-1001 slice 8: [SerializeField] is load-bearing. Extract pads are Spawn()ed at BAKE
        // time and the scene is then saved, so a plain private silently reverted every authored
        // label to "Leave Dungeon" - dg_descent_probe authors "Extract (deep)" and the string is
        // absent from the baked scene entirely.
        [SerializeField] private string _label = "Leave Dungeon";
        // WO-957: which presentation this exit wears. TRUE = the layout's ONE true exit
        // (full arch + light beacon + "EXIT"); FALSE = a per-floor leave pad (quiet flat
        // pad + small "Leave" label - word+shape carry the distinction, never hue: the
        // owner is red/green colourblind). [SerializeField] for the same bake-then-save
        // reason as _label above.
        [SerializeField] private bool _isTrueExit = true;

        // The Addressables key, the hero-derived height and the load/normalize/re-seat routine
        // MOVED to DeNelle.Core.World.PortalStructure (owner 2026-08-14: "all of the portals
        // should be this"). They are not duplicated here on purpose: the overworld gate needs
        // the identical structure, and a second copy of an async-load-plus-Tripo-re-seat is
        // exactly the drift CLAUDE.md keeps having to un-rot.

        /// <summary>Placeholder geometry retired once the Portal loads. The beacon children
        /// (Beacon_Beam / Beacon_Label) are deliberately ABSENT - WO-1008 rules the shaft of
        /// light stays, and DungeonRoomOwnershipRegression finds Beacon_Beam by name.</summary>
        private static readonly string[] ArchChildNames =
        {
            "Arch_KayKit", "Pillar_L", "Pillar_R", "Lintel", "Sheet"
        };

        /// <summary>Create the exit at <paramref name="position"/> and build its visual.</summary>
        /// <param name="onLeave">Optional rich-scene leave action (ExitToVillage). Null => Castle route.</param>
        /// <param name="label">Interact-button prompt text.</param>
        /// <param name="trueExit">WO-957: TRUE = the full arch+beacon true-exit presentation;
        /// FALSE = the quiet per-floor leave-pad presentation. The baker passes FALSE for
        /// every extract pad (reflection Invoke passes all four args - defaults do not apply).</param>
        public static DungeonExitInteractable Spawn(Vector3 position, System.Action onLeave = null,
                                                   string label = "Leave Dungeon", bool trueExit = true)
        {
            var go = new GameObject("DungeonExit (Return)");
            go.transform.position = position;
            var exit = go.AddComponent<DungeonExitInteractable>();
            exit._onLeave = onLeave;
            exit._label = string.IsNullOrEmpty(label) ? "Leave Dungeon" : label;
            exit._isTrueExit = trueExit;
            exit.BuildVisual();
            return exit;
        }

        private void BuildVisual()
        {
            // Walk-in trigger volume (the only collider on the root).
            var trigger = gameObject.AddComponent<SphereCollider>();
            trigger.isTrigger = true;
            trigger.radius = TriggerRadius;

            var frame = new Color(0.20f, 0.55f, 0.30f, 1f);   // emerald stone
            var glow = new Color(0.55f, 0.95f, 0.55f, 0.72f); // green-gold sheet

            // WO-957: TWO presentations, distinguished by SHAPE and WORD, never hue
            // (colourblind law) - the full arch/beacon marks ONLY the layout's true exit;
            // per-floor extract pads get the quiet flat-pad affordance below.
            if (!_isTrueExit)
            {
                BuildLeavePad(glow);
                return;
            }

            // WO-1007 (owner pick: Option C, freestanding decorated arch): the exit is a
            // real KayKit monument arch, not primitive emerald cubes. Missing-asset safety:
            // fall back to the old primitive arch with a Warn - a lost exit is a softlock.
            bool kaykitBuilt = Guard.Try(Sys, "build KayKit exit arch", () => TryBuildKayKitArch(), false);
            if (!kaykitBuilt)
            {
                FlowTrace.Warn(Sys, "KayKit exit arch unresolved - falling back to the primitive arch " +
                    "(never an invisible exit)");
                AddDecor("Pillar_L", new Vector3(-1.1f, 1.3f, 0f), new Vector3(0.35f, 2.6f, 0.35f), frame, false);
                AddDecor("Pillar_R", new Vector3(1.1f, 1.3f, 0f), new Vector3(0.35f, 2.6f, 0.35f), frame, false);
                AddDecor("Lintel", new Vector3(0f, 2.75f, 0f), new Vector3(2.7f, 0.35f, 0.35f), frame, false);
            }
            FlowTrace.Step(Sys, $"exit arch variant built: {(kaykitBuilt ? "kaykit-optionC" : "primitive-fallback")}");

            // Green-gold glow plane filling the opening ("you may pass home") - kept in
            // BOTH variants, and kept DISTINCT from the purple entry portal (WO-869).
            AddDecor("Sheet", new Vector3(0f, 1.3f, 0f), new Vector3(1.9f, 2.5f, 1f), glow, true);

            // WO-797 / F8 seq 622 exit discoverability: the arch alone read as "no way to
            // exit" once a mob camped it. Add a BEACON per the house checkpoint-crystal
            // pattern (Checkpoint.cs: point light = the "follow the light" cue from afar,
            // pulsed while it wants attention) - a pulsing green-gold point light, a tall
            // glow beam that reads over enemy heads and from the corridor mouth, and a
            // billboarded ASCII "EXIT" label. All decorative (no colliders).
            BuildBeacon(glow);

            // Owner direction 2026-08-14: the exit is her Tripo Portal, not a stone arch.
            // Kicked ASYNC and deliberately AFTER the arch is already standing, so the exit is
            // never empty for a single frame - if the bundle is missing the player keeps the
            // KayKit arch instead of walking into an invisible softlock.
            SwapInPortalAsync().Forget();
        }

        // ── Owner's Portal art, loaded through Addressables ───────────────────────
        // NOT Resources.Load: everything under Resources/ is force-included in every player
        // build whether referenced or not, and this asset is 5.3 MB of FBX + 2.2 MB of
        // embedded textures. The web payload already grew 42% (165 -> 234 MB) on 2026-08-10.
        //
        // NOT WaitForCompletion either, despite the call site being synchronous: there is ZERO
        // precedent for it in this tree (verified), and a blocking Addressables wait on WebGL -
        // the primary platform - is a known hazard. The three existing Addressables consumers
        // (EquipmentController, HeroArmorVisual, HeroBodySwapper) all load ASYNC; this matches
        // them rather than inventing a second pattern.
        //
        // ⚠ Depends on WO-974: until an explicit BuildPlayerContent ran in every player-build
        // seam, this key resolved HERE and resolved to nothing on a clone or CI.
        private PortalStructure.SwapResult _portalSwap;

        /// <summary>The threshold vortex held while this exit exists. A LOOP played
        /// fire-and-forget permanently consumes one of VFXManager's 20 global slots, so it is
        /// held by ONE handle and stopped in OnDestroy - the WO-893 discipline, and the WO-753
        /// one-owner rule (no aura outlives the thing it belongs to).</summary>
        private VFXHandle _thresholdVfx;
        private GameObject _portalFace;

        private async UniTaskVoid SwapInPortalAsync()
        {
            // The load, the collider strip, the hero-derived normalize and the Tripo re-seat all
            // live in PortalStructure now, so the overworld gate wears the identical structure
            // from the identical code path (owner 2026-08-14: "all of the portals should be this").
            _portalSwap = await PortalStructure.SwapInAsync(transform, PortalStructure.InteriorHeight);
            if (!_portalSwap.Ok || this == null || gameObject == null) return;   // Warn already emitted

            // Retire the placeholder geometry now that real art is standing, but keep the
            // BEACON and its label: WO-1008's ruling is that the shaft of light STAYS.
            foreach (var childName in ArchChildNames)
            {
                var t = transform.Find(childName);
                if (t != null) t.gameObject.SetActive(false);
            }

            AttachThresholdVfx();

            FlowTrace.Step(Sys, $"PORTAL swapped in from '{PortalStructure.Address}' - arch geometry retired, " +
                                "beacon kept, threshold vortex held.");
        }

        // ── The vortex that says "this is a portal" (owner 2026-08-14) ────────────
        // The owner's felt-test: "this portal and all portals like this should have a vfx that
        // help tell its a portal smoke or vortex or anything" - the exit was a handsome stone
        // ruin with a HOLE in it and nothing else, so it read as scenery, not as a way out.
        //
        // Seated from the loaded art's MEASURED bounds rather than a hardcoded height, because
        // the structure is normalized at runtime and any literal here silently goes wrong the
        // next time the art or the height multiple is retuned. 45% of the opening height is the
        // visual middle of a doorway (the lintel mass sits above the geometric middle).
        private void AttachThresholdVfx()
        {
            if (_thresholdVfx != null && _thresholdVfx.IsAlive) return;   // idempotent

            Bounds b = PortalStructure.MeasureBounds(_portalSwap.Instance);
            if (b.size.y <= 0.001f)
            {
                FlowTrace.Warn(Sys, "threshold VFX NOT seated - the loaded portal measured degenerate bounds, " +
                                    "so there is no opening to put a vortex in.");
                return;
            }

            Vector3 pos = new Vector3(b.center.x, b.min.y + b.size.y * 0.45f, b.center.z);
            // Fill the opening: the effect is authored around a unit disc, so the opening WIDTH
            // is the honest scale reference - a fixed number would be lost inside a big arch and
            // would overflow a small one.
            float scale = Mathf.Max(0.5f, b.size.x * 0.9f);

            // The owner's portal reference shows a visible mystical FACE in the arch, not an
            // empty doorway with a few particles nearby. Reuse the catalogued dark-star mirror
            // already used by overworld entrances. It is a child (no global loop slot), and the
            // richer blue vortex remains the moving depth layer when its quality budget permits.
            if (_portalFace == null)
            {
                var facePrefab = DeNelle.Core.VfxAssetLoader.LoadVfxPrefab("VFX/Portal/PortalCircleDarkStar");
                if (facePrefab != null)
                {
                    float faceScale = VFXManager.ResolveFitScale(facePrefab, b.size.x * 0.82f, 0.05f, 4f);
                    _portalFace = Instantiate(facePrefab, pos, transform.rotation * Quaternion.Euler(90f, 0f, 0f), transform);
                    _portalFace.name = "[PortalFace_DarkStar]";
                    _portalFace.transform.localScale = Vector3.one * faceScale;
                    FlowTrace.Step(Sys, $"mystical portal face seated in arch: dark-star scale={faceScale:0.000}.");
                }
                else
                    FlowTrace.Once(Sys, "portal-face-unresolved", "dark-star portal face unresolved; procedural glow remains as fallback.");
            }

            _thresholdVfx = VFXManager.PlayKey(PortalStructure.AuraKey, pos, transform.rotation, transform,
                                               null, scale);
            if (_thresholdVfx != null)
            {
                FlowTrace.Step(Sys, $"threshold vortex '{PortalStructure.AuraKey}' HELD at ({pos.x:F1}, {pos.y:F1}, " +
                                    $"{pos.z:F1}) scale={scale:0.0} (opening {b.size.x:0.0} x {b.size.y:0.0} m).");
                return;
            }

            // Say WHY it is absent, once, so a capture reads this as a deliberate hold rather
            // than a broken effect someone re-debugs later (§12).
            FlowTrace.Once(Sys, "threshold-aura-unresolved",
                $"threshold vortex '{PortalStructure.AuraKey}' did NOT resolve - either the global loop cap is " +
                "hit or the Hovl catalog has not been regenerated since the key was tagged in " +
                "Assets/Editor/VfxManualPicks.json (Defenders/VFX/Generate Hovl VFX Catalog). The arch + beacon " +
                "still carry the exit, so this degrades the flourish and never the affordance.");
        }

        private void OnDestroy()
        {
            // Release the handle or the bundle stays resident for the session.
            PortalStructure.Release(ref _portalSwap);
            // WO-753 one-owner rule: no looping aura outlives the thing it belongs to. A held
            // loop that is never stopped permanently consumes one of VFXManager's 20 global
            // slots, and an orphaned vortex with no portal under it is the exact artefact that
            // rule exists to prevent.
            _thresholdVfx?.Stop();
            _thresholdVfx = null;
        }

        // ── WO-1007: the KayKit Option C monument arch ────────────────────────────
        // wall_arched + two pillar_decorated, freestanding, sharing the kit texture so
        // it themes with the dungeon. Resolution order:
        //   1. Resources copies (Assets/Resources/Dungeon/Exit/ - tracked, so the
        //      runtime-INJECTED return exit resolves in a player build),
        //   2. editor-only AssetDatabase from the gitignored kit (bake/editor coverage),
        //   3. caller falls back to the primitive arch (Warn, never invisible).
        private bool TryBuildKayKitArch()
        {
            GameObject archModel = ResolveExitProp("wall_arched");
            GameObject pillarModel = ResolveExitProp("pillar_decorated");
            if (archModel == null || pillarModel == null) return false;

            Material mat = ResolveKayKitMaterial();
            // The arch piece is authored on the kit grid (~4m wall). Base sits on the
            // floor at the trigger's seat; pillars flank just outside the wall edges.
            AddProp("Arch_KayKit", archModel, Vector3.zero, 0f, mat);
            AddProp("Pillar_L", pillarModel, new Vector3(-2.3f, 0f, 0f), 0f, mat);
            AddProp("Pillar_R", pillarModel, new Vector3(2.3f, 0f, 0f), 0f, mat);
            return true;
        }

        private static GameObject ResolveExitProp(string stem)
        {
            var fromResources = Guard.Try(Sys, $"resolve exit prop '{stem}' (Resources)",
                () => Resources.Load<GameObject>("Dungeon/Exit/" + stem), null);
            if (fromResources != null) return fromResources;
#if UNITY_EDITOR
            var fromKit = Guard.Try(Sys, $"resolve exit prop '{stem}' (editor kit)",
                () => UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(
                    "Assets/Models/KayKit/dungeon/" + stem + ".fbx"), null);
            if (fromKit != null) return fromKit;
#endif
            FlowTrace.Warn(Sys, $"exit prop '{stem}' unresolved (no Resources copy" +
#if UNITY_EDITOR
                ", no kit asset" +
#endif
                ")");
            return null;
        }

        // The kit's shared URP material look. In a player build the .mat asset is not
        // addressable, so build a URP/Lit (MAT-02-pinned) material from the tracked
        // texture copy; in the editor prefer the kit's own dungeon_texture_URP.mat.
        private static Material ResolveKayKitMaterial()
        {
#if UNITY_EDITOR
            var kitMat = Guard.Try(Sys, "resolve kit material (editor)",
                () => UnityEditor.AssetDatabase.LoadAssetAtPath<Material>(
                    "Assets/Models/KayKit/dungeon/dungeon_texture_URP.mat"), null);
            if (kitMat != null) return kitMat;
#endif
            Shader lit = Shader.Find("Universal Render Pipeline/Lit");
            if (lit == null)
            {
                FlowTrace.Warn(Sys, "URP/Lit shader unresolved - exit arch keeps its imported materials");
                return null;
            }
            var mat = new Material(lit);
            var tex = Guard.Try(Sys, "resolve kit texture (Resources)",
                () => Resources.Load<Texture2D>("Dungeon/Exit/dungeon_texture"), null);
            if (tex != null && mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", tex);
            else FlowTrace.Warn(Sys, "kit texture unresolved - exit arch renders plain-lit (still visible)");
            return mat;
        }

        // Instantiate a decorative prop child: colliders stripped (never trap the hero),
        // every renderer on the shared kit material so the piece themes with the room.
        private void AddProp(string childName, GameObject model, Vector3 localPos, float yawDeg, Material mat)
        {
            var prop = Instantiate(model);
            prop.name = childName;
            prop.transform.SetParent(transform, false);
            prop.transform.localPosition = localPos;
            prop.transform.localRotation = Quaternion.Euler(0f, yawDeg, 0f);
            foreach (var col in prop.GetComponentsInChildren<Collider>(true))
                if (col != null) UnityEngine.Object.Destroy(col);
            if (mat != null)
                foreach (var rend in prop.GetComponentsInChildren<Renderer>(true))
                    if (rend != null) rend.sharedMaterial = mat;
        }

        // ── WO-957: the QUIET per-floor leave pad ─────────────────────────────────
        // A flat translucent floor disc + a small billboarded label carrying the word
        // ("Leave"). Deliberately subordinate to the true exit: no arch, no tall beam,
        // no "EXIT" text, and NO real Light (the stairwell candles already spend 3 of
        // the URP 4-per-object realtime light budget - a light per pad would evict the
        // ones that matter). Shape (flat disc vs tall arch/shaft) + word ("Leave" vs
        // "EXIT") carry the distinction without hue (colourblind law).
        private void BuildLeavePad(Color glow)
        {
            var disc = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            disc.name = "Pad_Marker";
            var discCol = disc.GetComponent<Collider>();
            if (discCol != null) UnityEngine.Object.Destroy(discCol); // decorative - never trap
            disc.transform.SetParent(transform, false);
            disc.transform.localPosition = new Vector3(0f, 0.03f, 0f);
            disc.transform.localScale = new Vector3(1.8f, 0.02f, 1.8f);
            var discRend = disc.GetComponent<Renderer>();
            if (discRend != null) ApplyDecorMaterial(discRend, glow, translucent: true);

            Transform label = BuildWorldLabel("Pad_Label", _label,
                new Vector3(0f, 1.2f, 0f), fontSize: 40, characterSize: 0.09f);
            if (label != null)
            {
                // Reuse the beacon component for billboarding only - null light, no pulse.
                var beacon = gameObject.AddComponent<DungeonExitBeacon>();
                beacon.Bind(null, label);
            }
            FlowTrace.Step(Sys, $"leave pad built at {transform.position} " +
                $"(quiet affordance, label='{_label}', no light/beam)");
        }

        // Builds the DungeonExitBeacon child: light + vertical beam + "EXIT" label.
        private void BuildBeacon(Color glow)
        {
            var beaconGo = new GameObject("ExitBeacon");
            beaconGo.transform.SetParent(transform, false);

            var light = beaconGo.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = glow;
            light.intensity = 2.4f;   // Checkpoint's unvisited "come here" intensity
            light.range = 14f;
            beaconGo.transform.localPosition = new Vector3(0f, 3.2f, 0f);

            // Glow beam above the lintel - visible over a mob standing on the exit.
            //
            // WO-1008 (owner felt-test 2026-08-08: "big green bar doesnt make sense"). Was
            // localPos y 6.2 x scale y 6.4 - i.e. spanning world y 3.0 to 9.4 - as an OPAQUE
            // Unlit cube. Two defects in one object:
            //
            //   1. HEIGHT. RoomForgeCanon.WallHeight is 4 m. A 6.4 m beam starting at 3.0
            //      cleared the ceiling by 5.4 m. In a dungeon where the exits are seated in
            //      STAIR rooms (all five of dg_ember_deep's extracts sit in stair_up_*), it
            //      punched up through the floor above and stood proud of it - which is the
            //      "green bar rising out of the descent hole" the owner photographed. It was
            //      an exit marker from the floor BELOW, showing through.
            //   2. OPACITY. AddDecor only sets _Surface=1 (transparent) when asQuad is true,
            //      and this passes false - so it was a SOLID box on an Unlit shader, ignoring
            //      every light in the scene. Invisible as a defect while dungeons were bright;
            //      screaming since WO-919/1004 dropped ambient to #0a0a10.
            //
            // Now: translucent, and capped to sit between the lintel top (2.925) and the
            // ceiling (4.0). The point light above is what carries the "come here" cue from
            // range - the beam only has to read as a shaft, not as architecture.
            //
            // ⚠ THE NAME IS PINNED. DungeonRoomOwnershipRegression.cs:366 does
            // exit.transform.Find("Beacon_Beam") and fails the suite without it. Change the
            // look, never the name.
            //
            // ⚠ Colour is deliberately UNCHANGED - it stays `glow`, the owner's. She is
            // red/green colourblind, so the beacon must not depend on hue to be identified;
            // it reads by SHAPE and POSITION (a vertical shaft over an arch). Any recolour is
            // her call, not this fix's.
            AddDecor("Beacon_Beam", new Vector3(0f, 3.45f, 0f), new Vector3(0.28f, 1.1f, 0.28f),
                     glow, asQuad: false, translucent: true);

            // ASCII label. Legacy TextMesh (code-built UI law - no UXML); billboarded by
            // the beacon component. Skipped with a Warn if no built-in font resolves.
            // ⚠ THE NAME IS PINNED the same way Beacon_Beam is - the ownership regression
            // finds this child by name. Build it through the shared BuildWorldLabel so the
            // true exit and the WO-957 leave pad can never drift apart.
            // ⛔ THE WORD IS REMOVED — owner ruling 2026-08-14, verbatim: "remove the word
            // completely". Not renamed to "Leave": REMOVED. A billboarded ASCII "EXIT" floating
            // in a dungeon read as debug text; the portal and the beacon now carry the meaning
            // by SHAPE and LIGHT, which is what the affordance was always meant to do.
            // Deliberately NOT deleting BuildWorldLabel — the WO-957 leave pads still use it and
            // it is the one world-label path (no UXML). Verified before removing: nothing under
            // Assets/Editor pins "Beacon_Label" or the string "EXIT", so no suite goes red.
            // Beacon_Beam is a DIFFERENT child and IS pinned by the ownership regression — kept.
            Transform label = null;

            var beacon = beaconGo.AddComponent<DungeonExitBeacon>();
            beacon.Bind(light, label);
            FlowTrace.Step(Sys, $"exit beacon armed at {transform.position} " +
                $"(light range {light.range:F0}, beam, label=REMOVED per owner ruling)");
        }

        /// <param name="translucent">WO-1008: force the transparent surface on a NON-quad too.
        /// Previously only <paramref name="asQuad"/> got _Surface=1, so a beam built as a cube was
        /// opaque - a solid box on an Unlit shader reads as debug geometry, not as light.</param>
        private void AddDecor(string label, Vector3 localPos, Vector3 localScale, Color color, bool asQuad,
                              bool translucent = false)
        {
            var prim = GameObject.CreatePrimitive(asQuad ? PrimitiveType.Quad : PrimitiveType.Cube);
            prim.name = label;
            var col = prim.GetComponent<Collider>();
            if (col != null) UnityEngine.Object.Destroy(col); // purely visual - never trap the hero
            prim.transform.SetParent(transform, false);
            prim.transform.localPosition = localPos;
            prim.transform.localScale = localScale;

            var rend = prim.GetComponent<Renderer>();
            if (rend != null) ApplyDecorMaterial(rend, color, asQuad || translucent);
        }

        /// <summary>
        /// The ONE material path for every code-built decor surface here (beam, sheet, leave pad).
        /// Extracted from <see cref="AddDecor"/> so the WO-957 pad cannot drift from the WO-1008
        /// beam: a second copy of this block is how one of them ends up opaque again.
        /// <paramref name="translucent"/> forces the transparent surface — URP keeps a material in
        /// the OPAQUE queue unless the render state matches, so setting only the alpha does nothing.
        /// </summary>
        private void ApplyDecorMaterial(Renderer rend, Color color, bool translucent)
        {
            if (rend == null) return;
            Shader sh = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color");
            if (sh == null)
            {
                FlowTrace.Warn(Sys, "no URP/Unlit shader resolved - decor surface keeps its primitive " +
                                    "material (magenta risk in a stripped player build)");
                return;
            }

            var mat = new Material(sh);
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", color);
            if (translucent && mat.HasProperty("_Surface"))
            {
                mat.SetFloat("_Surface", 1f);
                mat.SetOverrideTag("RenderType", "Transparent");
                mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
                if (mat.HasProperty("_SrcBlend"))
                    mat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
                if (mat.HasProperty("_DstBlend"))
                    mat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                if (mat.HasProperty("_ZWrite")) mat.SetFloat("_ZWrite", 0f);
                mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            }
            rend.sharedMaterial = mat;
        }

        /// <summary>
        /// The ONE world-label path (legacy <see cref="TextMesh"/> — code-built UI law, no UXML).
        /// Extracted from <see cref="BuildBeacon"/> so the WO-957 leave pad and the true exit's
        /// "EXIT" build the same way. Returns null (with a Warn, never silently) when no built-in
        /// font resolves — the caller then ships without a label rather than throwing.
        /// Parented to the exit root, not to the beacon, because the beacon component billboards
        /// the label transform it is handed.
        /// </summary>
        private Transform BuildWorldLabel(string name, string text, Vector3 localPos,
                                          int fontSize, float characterSize, Color? color = null)
        {
            var font = Guard.Try(Sys, "resolve builtin font",
                () => Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"), null);
            if (font == null)
                font = Guard.Try(Sys, "resolve builtin font (Arial fallback)",
                    () => Resources.GetBuiltinResource<Font>("Arial.ttf"), null);
            if (font == null)
            {
                FlowTrace.Warn(Sys, $"no builtin font resolved - '{name}' ships without its world label");
                return null;
            }

            var labelGo = new GameObject(name);
            labelGo.transform.SetParent(transform, false);
            labelGo.transform.localPosition = localPos;
            var tm = labelGo.AddComponent<TextMesh>();
            tm.text = text;                       // ASCII only - a non-ASCII glyph renders as tofu
            tm.font = font;
            tm.fontSize = fontSize;
            tm.characterSize = characterSize;
            tm.anchor = TextAnchor.MiddleCenter;
            tm.alignment = TextAlignment.Center;
            tm.color = color ?? new Color(0.75f, 1f, 0.75f, 1f);
            // ⚠ KNOWN DEFECT, DELIBERATELY NOT FIXED IN THIS LANE — owner F8 seq 2508
            // (2026-08-15): "see all the leave signs that leak through everything".
            // A built-in font's shared material runs Unity's "GUI/Text Shader", which is
            // declared ZTest Always, so the WORD draws on top of every wall between it and
            // the camera. The pad DISC (an ordinary lit primitive) occludes correctly —
            // that asymmetry IS the reported defect: discs behave, words do not.
            //
            // The egress trim cut the dungeon label count from 13 to 3 (one back exit per
            // content dungeon; the beacon's own label was already removed 2026-08-14), so
            // the symptom is now rare rather than pervasive. The remaining fix is a
            // MATERIAL change that needs screenshot verification (a font atlas keeps its
            // glyph in the ALPHA channel, so naively re-hosting it on Unlit/Transparent can
            // render black or blank), and this lane is edit-only with no capture. Left as a
            // flagged, sourced defect rather than an unverified visual guess.
            //
            // THE SAME font.material LINE EXISTS TOWN-SIDE and leaks identically:
            //   Assets/_Modules/Village/Buildings/BuildingSign.cs:155
            //   Assets/_Modules/Village/Buildings/StructureAttackAlert.cs:184
            //   Assets/_Modules/Village/Vfx/StructureDamageVisuals.cs:903
            // Fix all four together, with a capture, or none.
            var tr = labelGo.GetComponent<MeshRenderer>();
            if (tr != null && font.material != null) tr.sharedMaterial = font.material;
            return labelGo.transform;
        }

        /// <summary>
        /// Push the hero rig explicitly so the prompt never depends on HeroLocomotion's enabled
        /// state (rich scene: DungeonController._hero; composed scene: the Player-tagged hero).
        /// The dungeon neutralizes the injected HeroLocomotion but KEEPS it enabled — this seam
        /// makes the exit robust either way, and independent of a mover-type lookup entirely.
        /// </summary>
        public void SetHero(Transform hero)
        {
            if (hero == null) return;
            _hero = hero;
            _heroFound = true;
        }

        /// <summary>Require the composed run's authored boss to be defeated before this exit works.</summary>
        public void SetBossGate(DungeonRuntimeState state)
        {
            if (_bossGateState != null) _bossGateState.RunStateChanged.RemoveListener(RefreshBossGateVisual);
            _bossGateState = state;
            if (_bossGateState != null) _bossGateState.RunStateChanged.AddListener(RefreshBossGateVisual);
            BuildBossSealVisual();
            RefreshBossGateVisual();
            FlowTrace.Step(Sys, $"boss gate armed on exit '{name}' (unlocked={state != null && state.BossDefeated})");
        }

        public bool IsBossGated => _bossGateState != null;
        public bool IsBossGateUnlocked => _bossGateState == null || _bossGateState.BossDefeated;

        private void BuildBossSealVisual()
        {
            if (_bossSealVisual != null) return;
            _bossSealVisual = new GameObject("BossGateSeal_X");
            _bossSealVisual.transform.SetParent(transform, false);
            Color seal = new Color(0.82f, 0.62f, 0.18f, 1f);
            AddDecor("BossGateSlash_A", new Vector3(0f, 1.35f, 0.1f),
                new Vector3(0.22f, 3.1f, 0.22f), seal, false);
            AddDecor("BossGateSlash_B", new Vector3(0f, 1.35f, 0.1f),
                new Vector3(0.22f, 3.1f, 0.22f), seal, false);
            Transform a = transform.Find("BossGateSlash_A");
            Transform b = transform.Find("BossGateSlash_B");
            if (a != null) a.localRotation = Quaternion.Euler(0f, 0f, 48f);
            if (b != null) b.localRotation = Quaternion.Euler(0f, 0f, -48f);
            if (a != null) a.SetParent(_bossSealVisual.transform, true);
            if (b != null) b.SetParent(_bossSealVisual.transform, true);
        }

        private void RefreshBossGateVisual()
        {
            bool locked = !IsBossGateUnlocked;
            if (_bossSealVisual != null) _bossSealVisual.SetActive(locked);
            var labels = GetComponentsInChildren<TextMesh>(true);
            for (int i = 0; i < labels.Length; i++)
            {
                if (labels[i] == null) continue;
                if (labels[i].name.IndexOf("Label", StringComparison.OrdinalIgnoreCase) < 0) continue;
                labels[i].text = locked ? "SEALED\nDEFEAT BOSS" : (_isTrueExit ? "EXIT" : "LEAVE");
            }
        }

        private void ResolveHero()
        {
            if (_heroFound) return;
            // Prefer the Player-tagged hero (canon §7) — independent of HeroLocomotion being
            // enabled. Fall back to HeroLocomotion only if the tag is somehow unset.
            var tagged = GameObject.FindGameObjectWithTag("Player");
            if (tagged != null) { _hero = tagged.transform; _heroFound = true; return; }
            var hero = UnityEngine.Object.FindAnyObjectByType<HeroLocomotion>();
            if (hero != null) { _hero = hero.transform; _heroFound = true; }
        }

        private void Update()
        {
            if (_leaving) return;

            // =================================================================
            //  WO-1804 -- THE ONE HUNK THIS TICKET ADDS TO THIS FILE.
            // =================================================================
            //  OWNER RULING 2026-09-16, verbatim: "keep the reveal in the boss room". The Bastion
            //  Plans reveal therefore plays AT the boss, and its "Raid the Iron Bastion" CTA has to
            //  get the player home. It must NOT load a raid scene from here: ExecuteLeave routes a
            //  rich dungeon through DungeonController.ExitToVillage, which BANKS the run's crafting
            //  scatter, and a direct SceneRouter.GoRaid would silently bin everything the player
            //  carried down.
            //
            //  ⛔ SO THE CTA DOES NOT CALL ANYTHING HERE. It sets a latch and this, the scene's own
            //  sanctioned exit, claims it and raises its OWN Continue/Cancel confirm - so the exit
            //  still runs CanLeave, the player still taps "Continue to exit", and the banking path
            //  below is untouched. A request cannot skip the confirm and cannot skip the bank.
            //
            //  ⚠ AND IT IS WHY NO METHOD IN THIS FILE WAS MADE PUBLIC. The work order named
            //  "make RequestExitConfirm public and point the CTA at it" as the cheap fix. That fix
            //  CANNOT COMPILE: DeNelle.Dungeons references DeNelle.Village and not the reverse
            //  (both asmdefs read 2026-09-16), so the reveal - a Village type - can never call a
            //  Dungeons method however visible it is, and widening one would have added public
            //  surface that nothing in the tree could reach. The latch respects the existing
            //  dependency direction instead, and every member of this class stays private.
            //
            //  Placed BEFORE the hero resolution below on purpose: the claim must not be gated on
            //  the rig having been cached this frame. Consume() clears atomically, so the FIRST
            //  exit that can actually leave claims it - a boss-gated exit whose CanLeave still
            //  refuses cannot swallow the request and strand it.
            if (DeNelle.Village.BattlePlans.HasPendingDungeonExit && !_confirmOpen)
            {
                if (CanLeave(out string exitRefuse))
                {
                    string why = DeNelle.Village.BattlePlans.ConsumeDungeonExitRequest();
                    FlowTrace.Step(Sys, "exit request CLAIMED (" + why + ") by '" + gameObject.name +
                        "' - raising the ordinary Continue/Cancel confirm, so the run still banks " +
                        "through ExecuteLeave (WO-1804)");
                    RequestExitConfirm();
                }
                else
                {
                    FlowTrace.Throttle(Sys, "wo1804-exit-request-refused", 5f,
                        "exit request PENDING but '" + gameObject.name + "' cannot leave (" +
                        exitRefuse + ") - left latched for an exit that can, never consumed here");
                }
            }

            if (!_heroFound) { ResolveHero(); if (!_heroFound) return; }
            // The hero rig can be replaced (body-swap) after we first cached it - re-resolve
            // rather than dereferencing a destroyed Transform (DungeonPortal DEF-40 lesson).
            if (_hero == null) { _heroFound = false; return; }

            // Build/authoring mode: release the shared button and do nothing.
            if (MobileInteractButton.Suppressed)
            {
                MobileInteractButton.Release(this);
                return;
            }

            if (Time.time >= _nextProximityCheck)
            {
                _nextProximityCheck = Time.time + CheckInterval;
                float dist = Vector3.Distance(_hero.position, transform.position);
                float distSqr = dist * dist;
                _isInRange = distSqr <= ActivateRadius * ActivateRadius;
                // WO-995: arm only after the hero has been OUTSIDE the walk-in volume for a
                // sustained hold (and after boot grace). Instant "clear" samples on a jittering
                // spawn are not enough.
                float clearRadius = TriggerRadius + 0.75f;
                bool clear = dist > clearRadius;
                if (!_armed)
                {
                    if (clear)
                    {
                        if (_clearSince < 0f) _clearSince = Time.realtimeSinceStartup;
                        bool graceDone = Time.timeSinceLevelLoad >= BootGraceSeconds;
                        bool held = (Time.realtimeSinceStartup - _clearSince) >= ClearHoldSeconds;
                        if (graceDone && held)
                        {
                            _armed = true;
                            FlowTrace.Step(Sys,
                                $"exit ARMED after clear-hold: heroDist={dist:F2}m clearR={clearRadius:F2}m " +
                                $"levelT={Time.timeSinceLevelLoad:F2}s exitPos={transform.position} heroPos={_hero.position}");
                        }
                    }
                    else
                    {
                        _clearSince = -1f;
                        if (!_bootTraceEmitted)
                        {
                            _bootTraceEmitted = true;
                            FlowTrace.Warn(Sys,
                                $"WO-995 spawn INSIDE/near exit volume: heroDist={dist:F2}m " +
                                $"triggerR={TriggerRadius:F2}m exitPos={transform.position} heroPos={_hero.position} " +
                                $"- Leave() blocked until clear-hold + {BootGraceSeconds:0.#}s boot grace.");
                        }
                    }
                }
            }

            // WO-987: touch is the trigger (OnTriggerEnter). While confirming, hide the
            // proximity button so a second tap cannot race past the dialog.
            if (_confirmOpen || _leaving)
            {
                MobileInteractButton.Release(this);
                return;
            }
            // Optional secondary: in-range button also opens the SAME confirm (not a raw Leave).
            if (_isInRange)
                MobileInteractButton.Request(this,
                    IsBossGateUnlocked ? _label : "Defeat the boss to unlock", RequestExitConfirm);
            else
                MobileInteractButton.Release(this);
        }

        private void OnTriggerEnter(Collider other)
        {
            if (_leaving || _confirmOpen || other == null) return;
            if (!IsHeroCollider(other)) return;
            if (!CanLeave(out string refuse))
            {
                FlowTrace.Warn(Sys, $"OnTriggerEnter REFUSED exit confirm: {refuse}");
                return;
            }
            FlowTrace.Step(Sys, "hero TOUCHED the RETURN exit — opening confirm (WO-987)");
            RequestExitConfirm();
        }

        /// <summary>
        /// WO-987: present Continue-to-exit / Cancel. Stray dismiss = Cancel. Never exits
        /// without an explicit Continue face.
        /// </summary>
        private void RequestExitConfirm()
        {
            if (_leaving || _confirmOpen) return;
            if (!CanLeave(out string refuse))
            {
                FlowTrace.Warn(Sys, $"RequestExitConfirm REFUSED: {refuse}");
                return;
            }

            _confirmOpen = true;
            MobileInteractButton.Release(this);

            // Gold = primary irreversible CTA (not green Confirm — owner is red/green colourblind;
            // faces distinguished by position LEFT cancel / RIGHT continue + text, never hue alone).
            try
            {
                var modal = ElarionUiKit.BuildConfirmModal(
                    name: "DungeonExitConfirm",
                    title: "Leave dungeon?",
                    message: "Continue to exit returns you to town. Cancel keeps you in the dungeon.",
                    confirmLabel: "Continue to exit",
                    cancelLabel: "Cancel",
                    onConfirm: OnConfirmContinueToExit,
                    onCancel: OnConfirmCancel,
                    confirmKind: ElarionUiKit.ButtonKind.Gold,
                    sortingOrder: 34000);
                _confirmCanvas = modal.canvas;

                if (_confirmHandle == null)
                    _confirmHandle = PanelManager.Register("DungeonExitConfirm",
                        OnConfirmCancel, () => _confirmOpen);
                if (!PanelManager.NotifyOpened(_confirmHandle))
                {
                    // Rejection already ran OnConfirmCancel (arbiter closes on its way out),
                    // so the canvas is torn down and _confirmOpen is false here.
                    FlowTrace.Warn(Sys,
                        "PanelManager REJECTED the exit confirm (battle-lock) — confirm not shown; " +
                        "portal stays armed for a retry after the fight.");
                    return;
                }

                FlowTrace.Step(Sys,
                    "exit CONFIRM SHOWN faces=[Continue to exit | Cancel] default=Cancel " +
                    $"(portal='{name}' trueExit={_isTrueExit})");
            }
            catch (Exception ex)
            {
                _confirmOpen = false;
                FlowTrace.Warn(Sys,
                    $"exit CONFIRM FAILED TO APPEAR: {ex.GetType().Name}: {ex.Message} — " +
                    "portal touch will feel like a no-op until this is fixed.");
            }
        }

        private void OnConfirmContinueToExit()
        {
            FlowTrace.Step(Sys, "exit CONFIRM RESOLVED face=continue-to-exit");
            DismissConfirmUi();
            ExecuteLeave();
        }

        private void OnConfirmCancel()
        {
            FlowTrace.Step(Sys,
                "exit CONFIRM RESOLVED face=cancel — run state UNCHANGED; hero remains in dungeon");
            DismissConfirmUi();
            // Stay armed so a second walk-in reopens the same confirm.
        }

        private void DismissConfirmUi()
        {
            _confirmOpen = false;
            if (_confirmCanvas != null)
            {
                UnityEngine.Object.Destroy(_confirmCanvas);
                _confirmCanvas = null;
            }
            if (_confirmHandle != null) PanelManager.NotifyClosed(_confirmHandle);
        }

        /// <summary>WO-995: walk-in / button leave is only legal once armed past boot grace.</summary>
        private bool CanLeave(out string refuseReason)
        {
            if (!IsBossGateUnlocked)
            {
                refuseReason = "boss gate locked (defeat the dungeon boss first)";
                return false;
            }
            if (Time.timeSinceLevelLoad < BootGraceSeconds)
            {
                refuseReason = $"boot grace ({Time.timeSinceLevelLoad:F2}s < {BootGraceSeconds:0.#}s)";
                return false;
            }
            if (!_armed)
            {
                refuseReason = "exit not armed (hero has not held clear of the volume)";
                return false;
            }
            refuseReason = null;
            return true;
        }

        // True when `other` belongs to the hero rig — WITHOUT depending on HeroLocomotion's enabled
        // state: the pushed/known hero first, then the Player tag up the parent chain (canon §7),
        // then HeroLocomotion as a last resort (composed scenes where nothing pushed a hero).
        private bool IsHeroCollider(Collider other)
        {
            if (_hero != null && (other.transform == _hero || other.transform.IsChildOf(_hero))) return true;
            for (Transform t = other.transform; t != null; t = t.parent)
                if (t.CompareTag("Player")) return true;
            return other.GetComponentInParent<HeroLocomotion>() != null;
        }

        /// <summary>
        /// Legacy name kept for callers; routes through the WO-987 confirm so nothing
        /// can exit without an explicit "Continue to exit".
        /// </summary>
        private void Leave() => RequestExitConfirm();

        /// <summary>Actually leave — only after confirm Continue (or internal re-entry).</summary>
        private void ExecuteLeave()
        {
            if (_leaving) return;
            // Defensive re-check (confirm may have been open across a long pause).
            if (!CanLeave(out string refuse))
            {
                FlowTrace.Warn(Sys, $"ExecuteLeave REFUSED: {refuse}");
                return;
            }
            _leaving = true;
            MobileInteractButton.Release(this);
            DismissConfirmUi();

            // WO-893: arm the MATERIALISE beat for the hub. PortalVFXController.OnHeroExit
            // existed but was called by nothing, so a portal round trip burst on the way IN
            // and was silent on the way BACK - an asymmetric transition reads as a bug. The
            // mirror beat belongs on the ARRIVAL side, which is a different scene from this
            // one, so it cannot be a direct call: this stamps a short window and the first
            // portal the hero surfaces near in the hub claims it. Both exit routes below
            // (the rich ExitToVillage and the direct Castle load) pass through here, so the
            // stamp covers both. If the hero comes up nowhere near a portal the stamp simply
            // lapses - a missed flourish, never a stuck flag.
            //
            // ⚠ WO-1596 MOVED THE STAMP OFF THIS LINE for the composed route. The window is
            // ReturnWindowSeconds = 12s (PortalVFXController.cs:149, read at source 2026-09-07),
            // and the rough-stone fanfare now sits between here and the load waiting for a human
            // tap - so stamping HERE would let the window lapse while the player reads, and the
            // materialise beat would silently stop playing. It is stamped inside RouteHomeNow
            // instead, i.e. at the moment the fade actually starts, which is what the window was
            // always measuring from. The RICH route below keeps the stamp on this path because it
            // loads immediately.
            //
            // WO-770.1: a RICH dungeon supplies ExitToVillage (banks the run's crafting scatter to
            // the larder + ends the run), so prefer it. A composed scene has no DungeonRuntimeState /
            // crafting inventory to bank, so it falls through to the direct Castle route below.
            if (_onLeave != null)
            {
                DeNelle.Village.PortalVFXController.NotifyReturnedThroughPortal();
                // ⚠ THE RICH COTTAGE ROUTE DOES NOT WAIT FOR THE FANFARE, and that is a KNOWN GAP,
                // not an oversight: ExitToVillage grants and starts LoadSceneWithFade in the same
                // synchronous body (DungeonController.cs:461-493), so a panel opened from the
                // event would be destroyed with the scene. Making it wait means restructuring that
                // UniTask around the dismiss, which is outside WO-1596's stated file region and
                // needs a lead ruling. The device evidence (2026-09-07 09:44) is the COMPOSED exit,
                // which is the route that ships.
                FlowTrace.Step(Sys, "taking RETURN exit -> DungeonController.ExitToVillage (rich scene) " +
                                    "- NOTE: the rough-stone fanfare does not gate this route (WO-1596 open gap)");
                _onLeave.Invoke();
                return;
            }
            // ── WO-1112: PAY THE COMPOSED RUN BEFORE LEAVING ────────────────────────────
            // THE DEFECT: DungeonController.GrantRunPayout - whose own doc says "EVERY COMPLETED
            // RUN PAYS" - had exactly ONE caller, inside the cottage-pipeline ExitToVillage. A
            // composed exit fell straight through to the Castle load below and paid NOTHING. And
            // because DungeonRunPayout.LastPolishScore is written nowhere else, JewelPolishService
            // scored EVERY composed run 0, so the whole rough-stone / polish economy was inert in
            // exactly the dungeons that actually get played.
            //
            // ONE AUTHORITY, NOT A COPY: this calls the SAME static GrantRunPayout the cottage
            // uses, handing it the composed run state that ComposedDungeonHost owns. The two exits
            // must never grow separate payout logic - the "engaged" bar, the grade rubric and the
            // stone id all live in that one method. The comment that used to sit here ("a composed
            // scene has no DungeonRuntimeState") was TRUE ONLY BY ACCIDENT: the bootstrap did
            // create one, then dropped it on the floor in a local variable.
            var host = ComposedDungeonHost.Current;
            RoughStoneFanfareVM fanfare = null;
            if (host == null)
            {
                FlowTrace.Warn(Sys, "composed exit: no ComposedDungeonHost - this run CANNOT be paid out " +
                                    "(no run state to judge). The player leaves with nothing; that is a defect, not a design.");
            }
            else
            {
                // ── WO-1596: LISTEN WHILE THE ONE AUTHORITY PAYS ────────────────────────────
                // The subscription is scoped to this single call and removed in a finally, so the
                // exit can never accumulate handlers across runs and a payout raised by some other
                // exit can never be mistaken for ours. We compose the VM here and RENDER after
                // EndRun, because the panel must not be alive while the run record is torn down.
                Action<string, int, bool> onGranted = (stoneId, score, firstEver) =>
                {
                    fanfare = RoughStoneFanfareVM.For(stoneId, score, firstEver);
                };
                DungeonController.RoughStoneGranted += onGranted;
                try { DungeonController.GrantRunPayout(host.RunState, "composed exit"); }
                finally { DungeonController.RoughStoneGranted -= onGranted; }
                host.EndRun();
            }

            // ── WO-1596: THE EXIT WAITS FOR THE MOMENT ──────────────────────────────────
            // Owner, 2026-09-07: "that scren need to be a big moment fanfare full screen, the
            // user needs to know that this is a BIG deal". The stone is ALREADY banked at this
            // point - the panel owns nothing but the continuation, so the worst case is a missed
            // flourish, never a lost reward.
            //
            // GUARDED, BECAUSE A DEAD EXIT IS WORSE THAN A MISSING BEAT. Show returns false when
            // it refuses to open (arbiter rejection, unusable chrome) and Guard.Try returns false
            // when it THROWS - both fall through to the immediate route below, which is exactly
            // the pre-WO-1596 behaviour. There is no branch here that leaves the player in the
            // dungeon with no way home.
            if (fanfare != null)
            {
                bool owned = Guard.Try(Sys, "show rough stone fanfare",
                    () => RoughStoneFanfarePanel.Show(fanfare, RouteHomeNow), false);
                if (owned)
                {
                    FlowTrace.Step(Sys, "RETURN exit HELD for the rough-stone fanfare (" +
                                        fanfare.TraceSummary + ") - the route runs on dismiss.");
                    return;
                }
                FlowTrace.Warn(Sys, "rough-stone fanfare did not take the screen (" + fanfare.TraceSummary +
                                    ") - routing home immediately so the exit is never dead. " +
                                    "The stone is already banked either way.");
            }

            RouteHomeNow();
        }

        /// <summary>
        /// The composed route home, extracted (WO-1596) so it can be handed to the fanfare as its
        /// continuation. Called EXACTLY ONCE per leave: either directly, or by the panel's
        /// consume-first dismiss - which nulls its pending action before invoking, so a re-entrant
        /// close cannot start a second scene load.
        /// </summary>
        private void RouteHomeNow()
        {
            // WO-893 stamp lives HERE, not at the top of ExecuteLeave: its 12s window measures
            // from the fade, and the fanfare may have held the exit for longer than that.
            DeNelle.Village.PortalVFXController.NotifyReturnedThroughPortal();

            // Route HOME exactly like DungeonController.ExitToVillage - the merged
            // overworld hub (SceneRouter.Castle). A composed scene has no crafting
            // inventory to bank, so the payout above plus this load is the whole exit.
            FlowTrace.Step(Sys, $"taking RETURN exit -> SceneRouter.Castle ('{SceneRouter.Castle}')");
            SceneRouter.LoadSceneWithFade(SceneRouter.Castle).Forget();
        }

        private void OnDisable()
        {
            if (_bossGateState != null) _bossGateState.RunStateChanged.RemoveListener(RefreshBossGateVisual);
            MobileInteractButton.Release(this);
            DismissConfirmUi();
        }
    }

    /// <summary>
    /// WO-797 exit-discoverability beacon: pulses the exit's point light (the house
    /// checkpoint-crystal "follow the light" idiom, Checkpoint.cs) and billboards the
    /// ASCII "EXIT" label at the camera. Purely presentational - owns no routing logic;
    /// the regression contract asserts an injected exit carries this component + a Light.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class DungeonExitBeacon : MonoBehaviour
    {
        private const float PulseSpeed = 2.6f;        // rad/sec sine input (Checkpoint-like)
        private const float IntensityBase = 2.4f;
        private const float IntensityAmp = 0.6f;

        private Light _light;
        private Transform _label;

        /// <summary>Wire the pulsing light and the (optional) billboard label.</summary>
        public void Bind(Light light, Transform label)
        {
            _light = light;
            _label = label;
        }

        private void Update()
        {
            if (_light != null)
                _light.intensity = IntensityBase + Mathf.Sin(Time.time * PulseSpeed) * IntensityAmp;

            if (_label != null)
            {
                var cam = Camera.main;
                if (cam != null)
                {
                    // Face the camera (billboard) - flat yaw+pitch, no roll.
                    _label.rotation = Quaternion.LookRotation(_label.position - cam.transform.position);
                }
            }
        }
    }
}

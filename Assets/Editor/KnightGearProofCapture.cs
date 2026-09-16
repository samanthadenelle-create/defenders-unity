// =============================================================================
// KnightGearProofCapture — photograph the KNIGHT'S STARTER SWORD + SHIELD on the
// real hero, DRAWN and SHEATHED, and MEASURE the seat rather than eyeballing it.
// -----------------------------------------------------------------------------
// Assembly: DeNelle.Editor (editor-only)   Namespace: DeNelle.Editor
// Menu:  Defenders/QA/Capture Knight Gear Proof
// Batch: -executeMethod DeNelle.Editor.KnightGearProofCapture.Run   (NO -quit —
//        this harness ENTERS PLAY MODE and exits the editor itself; see below)
// Marker: KNIGHT_GEAR_PROOF_OK <n> file=<paths>  |  KNIGHT_GEAR_PROOF_FAIL: <reason>
// Output: Builds/KnightGearProof/*.png + Builds/KnightGearProof/_summary.txt
//
// THE OWNER'S REQUEST (2026-08-20, verbatim):
//   "prove with screenshots ... that the shield and sword both load to the player
//    as knight on start and that they seat visually both sheathed and unsheathed.
//    Your job is to test, then take screenshots. If they do not confirm the hilt in
//    the grip and the shield in the correct location, you do not guess, you use data."
// and, on the sheathed pose:
//   "sheathed should sit inverted with the longest mesh (y) up and down attached to
//    hip bone"
//
// ─────────────────────────────────────────────────────────────────────────────
//  WHY THIS RUNS IN PLAY MODE AND NOT IN EDIT MODE  (the load-bearing decision)
// ─────────────────────────────────────────────────────────────────────────────
//  A harness that hand-parents a sword to a bone proves nothing about the shipped
//  game, so this drives the REAL chain end to end:
//     GameStateService.ChooseHero(Knight)
//        -> HeroBodySwapper.Start -> ff.knightv3 -> BuildKnightV3Body
//        -> WireHeroBody -> AddComponent<GearLoadout> -> Refresh()
//                        -> AddComponent<EquipmentController>  (OnEnable -> EquipBestForHero)
//                        -> equipLoadout.EquipOffHandById(StarterLoadout.OffHandFor("knight"))
//        -> EquipmentController.EquipOffHand -> Addressables 'gear/weapon/ShieldWithItemLogic'
//        -> AttachOffHandProp / ApplyHoldPose
//  TWO facts make edit mode impossible for that chain:
//    1. EquipmentController has no [ExecuteAlways], so AddComponent fires no
//       Awake/OnEnable outside play mode — the first equip would never happen.
//       (SheathePoseRegression relies on exactly that to poke its math in isolation;
//        it is a UNIT check of the pose maths, NOT a proof of the shipped path.)
//    2. weapons.json gives knight_shield_starter "loadVia":"addressable"
//       (prefabPath gear/weapon/ShieldWithItemLogic). The controller owns that handle
//       and completes it on a CALLBACK — with no player loop there is nothing to pump
//       it, so the shield would be absent for reasons that have nothing to do with the bug.
//  So: Run() arms a SessionState flag and calls EnterPlaymode; the driver below
//  builds, measures, shoots, writes, and calls EditorApplication.Exit itself.
//  ⚠ THE CALLER MUST NOT PASS -quit. run-unity-method.ps1 does, which is why this
//  harness ships with its own launcher (tools/run-unity-playmode.ps1).
//
// ─────────────────────────────────────────────────────────────────────────────
//  WHAT MAKES THE PNGs EVIDENCE AND NOT DECORATION  (CLAUDE.md §12)
// ─────────────────────────────────────────────────────────────────────────────
//  * EVERY SHOT IS MEASURED, and the marker FAILS on the measurement, never on the
//    picture. Precedent: QuestCastBodyCapture's bone-delta check passed green over a
//    T-posed body until a silhouette assertion was added. A harness that prints OK
//    beside a wrong image is worse than no harness.
//  * THE HILT TEST IS GEOMETRIC, NOT NAMED, AND USES NO VERTICES. The live props ship
//    with Read/Write OFF, so every vertex-based test — including the shipped
//    TryResolveSwordHiltEnd — is inert at runtime (see the FINDINGS note below). The
//    long axis comes from the largest-extent axis of the renderer's OWN LOCAL bounds
//    (NOT an assumed +Y: knight_starter is Native(...), so NormalizeInto is skipped and
//    the prop keeps its authored axes; and NOT Renderer.bounds, which is a world AABB).
//    The hilt is then the end sitting ON THE MESH ORIGIN, which is the shipped NATIVE
//    path's own contract ("trust grip-at-origin"). Finally: distance(hand bone -> hilt
//    end) must be SMALLER than distance(hand bone -> tip end). If the tip is nearer,
//    the blade is in the fist — the defect, named.
//  * THE MEASUREMENT CODE IS INDEPENDENT of the production seat code on purpose. If
//    it re-used the shipped helper's own idea of which end is which, a wrong shipped
//    answer would validate itself. WeaponOrientHelper.TryResolveSwordHiltEnd is still
//    called, but only as a CROSS-CHECK line in the report.
//
//  ⚠ STANDING FINDING, surfaced by this harness and NOT fixed here (it is the owner's
//    call, because the fix costs runtime memory): both live props ship with Read/Write
//    DISABLED, so the shipped derivations that read vertices silently degrade. The
//    trace says it outright — "ShieldHandleSide 'EquipmentProp_OffHand': only 0
//    readable vertices (Read/Write disabled on the mesh?) — the smooth-vs-handle face
//    cannot be measured, so NO flip is applied. The shield may be worn strap-outward."
//    WeaponPropReadablePostprocessor already forces Read/Write ON for props under
//    Assets/Resources/Heroes/Props/Weapons/ for exactly this reason, but neither the
//    Blink sword prefab nor the Addressable shield lives under that path, so the rule
//    never reaches the two items the Knight actually carries.
//  * THE BACKDROP IS MID-GREY, deliberately not green and not red — the owner is
//    red/green colourblind (memory: owner-colorblind-delegate-visual-creative).
//  * BLANK FRAMES ARE REFUSED. Coverage is measured before each PNG is written; a
//    frame of pure backdrop is a finding, not a capture.
//
//  PLAYERPREFS ARE SNAPSHOTTED AND RESTORED. The harness deletes the knight equip
//  keys so the STARTER SEED path actually runs (its guard is EquippedOffHand == null
//  — i.e. exactly the "on start" case the owner asked about) and puts the machine's
//  keys back afterwards. It never touches a device save.
// =============================================================================

using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using DeNelle.Core.Geometry;
using DeNelle.Core.State;
using DeNelle.Village;

namespace DeNelle.Editor
{
    /// <summary>Batch entry + play-mode arming for the Knight gear proof capture.</summary>
    public static class KnightGearProofCapture
    {
        internal const string OutDir = "Builds/KnightGearProof";

        /// <summary>Survives the domain reload that entering play mode triggers.</summary>
        private const string ArmKey = "KnightGearProof.Armed";
        private const string IsolationKey = "KnightGearProof.Isolated";
        private const string MainSaveKey = "KnightGearProof.MainSave";
        private static ISaveProvider _priorProvider;
        private static bool _priorCloudSuppression;
        internal static ISaveProvider IsolatedProvider;

        // BeforeSceneLoad creates/loads GameStateService. Isolate earlier, exactly as the
        // OwnedTownMovePlayProof harness does, so ChooseHero and shutdown autosaves stay in RAM.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void IsolateSaves()
        {
            if (!SessionState.GetBool(ArmKey, false)) return;
            _priorProvider = GameStateService.Provider;
            _priorCloudSuppression = GameStateService.SuppressCloudForIsolatedProof;
            IsolatedProvider = new MemorySaveProvider();
            GameStateService.Provider = IsolatedProvider;
            GameStateService.SuppressCloudForIsolatedProof = true;
            SessionState.SetBool(IsolationKey, true);
        }

        internal static bool MainSaveUnchanged() =>
            (new LocalSaveProvider().Read(SaveSchema.PlayerPrefsKey) ?? string.Empty) ==
            SessionState.GetString(MainSaveKey, string.Empty);

        // Never restore while the proof's service is alive: its teardown may save once more.
        private static void RestoreIsolationAfterPlay(PlayModeStateChange change)
        {
            if (change != PlayModeStateChange.EnteredEditMode ||
                !SessionState.GetBool(IsolationKey, false)) return;
            GameStateService.Provider = _priorProvider ?? new LocalSaveProvider();
            GameStateService.SuppressCloudForIsolatedProof = _priorCloudSuppression;
            IsolatedProvider = null;
            _priorProvider = null;
            SessionState.SetBool(IsolationKey, false);
            EditorApplication.playModeStateChanged -= RestoreIsolationAfterPlay;
        }

        private sealed class MemorySaveProvider : ISaveProvider
        {
            private readonly Dictionary<string, string> _slots = new Dictionary<string, string>();
            public bool Exists(string slot) => _slots.ContainsKey(slot);
            public string Read(string slot) => _slots.TryGetValue(slot, out var data) ? data : string.Empty;
            public void Write(string slot, string data) => _slots[slot] = data;
            public void Delete(string slot) => _slots.Remove(slot);
        }

        [MenuItem("Defenders/QA/Capture Knight Gear Proof")]
        public static void RunMenu() => Run();

        /// <summary>
        /// Batch entry. Opens a scratch scene, arms the driver and enters play mode.
        /// Returns IMMEDIATELY — the work happens in the driver and the editor is exited there.
        /// </summary>
        public static void Run()
        {
            Directory.CreateDirectory(OutDir);
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogError("KNIGHT_GEAR_PROOF_FAIL: the editor was already entering/​in play mode when Run() was called.");
                return;
            }
            // A scratch scene, NEVER one of the curated ones (CLAUDE.md §3) and never saved.
            SessionState.SetString(MainSaveKey,
                new LocalSaveProvider().Read(SaveSchema.PlayerPrefsKey) ?? string.Empty);
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            SessionState.SetBool(ArmKey, true);
            // TWO PLANTING PATHS, because which one fires depends on a PROJECT SETTING this harness
            // does not own. Entering play mode normally reloads the domain (so [InitializeOnLoadMethod]
            // Boot re-runs and plants the driver) — but EditorSettings can turn that reload OFF, and
            // then Boot never fires again and the run would sit there doing nothing until the
            // wrapper's timeout, which reads exactly like a hang. This subscription covers the
            // no-reload case. Both paths go through the SAME one-shot SessionState flag, so exactly
            // one driver is ever planted.
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
            Debug.Log("[KnightGearProof] armed; entering play mode (the caller must NOT have passed -quit).");
            EditorApplication.EnterPlaymode();
        }

        private static void OnPlayModeChanged(PlayModeStateChange change)
        {
            if (change != PlayModeStateChange.EnteredPlayMode) return;
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            Plant();
        }

        /// <summary>
        /// Runs on every domain load. After EnterPlaymode the domain reloads INSIDE play mode,
        /// so this is where the driver is actually planted — SessionState is what carries the
        /// arm flag across that reload.
        /// </summary>
        /// <summary>
        /// ⚠ MEASURED, not assumed (first run, 2026-08-20): entering play mode reloads the domain
        /// DURING ExitingEditMode, so this method runs while <c>EditorApplication.isPlaying</c> is
        /// still FALSE. The first cut returned early on that check, nothing was ever planted, and
        /// the run sat in a live play session doing nothing until the wrapper's timeout — a hang
        /// that looks exactly like a broken harness. So: plant NOW if we are already playing, and
        /// otherwise SUBSCRIBE and plant when play mode is actually entered.
        /// </summary>
        [InitializeOnLoadMethod]
        private static void Boot()
        {
            EditorApplication.playModeStateChanged -= RestoreIsolationAfterPlay;
            EditorApplication.playModeStateChanged += RestoreIsolationAfterPlay;
            if (!SessionState.GetBool(ArmKey, false)) return;
            if (EditorApplication.isPlaying) { Plant(); return; }
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
        }

        /// <summary>Plant the driver exactly once. The SessionState flag is the one-shot.</summary>
        private static void Plant()
        {
            if (!SessionState.GetBool(ArmKey, false)) return;
            SessionState.SetBool(ArmKey, false);
            var go = new GameObject("~KnightGearProofDriver");
            go.AddComponent<KnightGearProofDriver>();
            Debug.Log("[KnightGearProof] driver planted in play mode.");
        }
    }

    /// <summary>
    /// The play-mode driver: builds the Knight through the shipped body+gear path, measures
    /// the seat, shoots the PNGs, writes the summary and the marker, then exits the editor.
    /// </summary>
    public sealed class KnightGearProofDriver : MonoBehaviour
    {
        // ── Capture rig ──────────────────────────────────────────────────────
        private const int ResX = 1200;
        private const int ResY = 1600;
        /// <summary>Spare culling layer so nothing can photobomb the subject
        /// (StorefrontOrientationCapture / QuestCastBodyCapture use the same trick).</summary>
        private const int IsolationLayer = 31;
        /// <summary>Under this fraction of non-backdrop pixels the frame is blank — refused, not written.</summary>
        private const float BlankCoverageFloor = 0.004f;

        // ── Assertion thresholds ─────────────────────────────────────────────
        /// <summary>How much closer the hilt must be to the hand than the tip, as a fraction of the
        /// prop's own length. 0.15 is far under any real seat (a correctly-held sword puts the hand
        /// AT the hilt, ~1.0 of the length apart) and far over float noise.</summary>
        private const float HiltMarginFraction = 0.15f;
        /// <summary>Degrees the SHEATHED long axis may lean off world vertical. The owner's rule is
        /// "up and down"; the retired baldric diagonal was 28 and the photographed horizontal ~90,
        /// so 20 rejects both while leaving room for the hip bone's animated lean.</summary>
        private const float SheathVerticalTolDeg = 20f;
        /// <summary>How far the belt-carried part of a sheathed prop may sit from the HIPS BONE
        /// before it is "off the body" rather than "on the hip". A hip carry puts it within a hand's
        /// breadth of the pelvis plus the outward stand-off (~0.26 m); 0.45 m allows that and still
        /// rejects the chest-height float the 2026-08-20 capture photographed (~0.5 m).</summary>
        private const float SheathHipSeatMaxM = 0.45f;
        /// <summary>Where the weapon hand may project onto the GRIP SEGMENT (0 = pommel,
        /// 1 = crossguard). A real fist sits in the middle of the handle; the band is generous at
        /// both ends because the guard position is an estimate, but it still rejects the two things
        /// that matter — a hand up on the BLADE (&gt;1) and a hand past the POMMEL (&lt;0).</summary>
        private const float GripFracMin = 0.15f;
        private const float GripFracMax = 1.00f;
        // A centre/strap seat belongs within a forearm's breadth of the off-hand.
        private const float DrawnShieldCentreMaxM = 0.18f;

        /// <summary>Shots the run must produce: 3 drawn + 1 marked diagnostic + 5 sheathed,
        /// including the normal rear-gameplay view and its shield-only projected-area control.</summary>
        private const int ExpectedPngs = 9;

        private static readonly string[] KnightPrefKeys =
        {
            EquipPrefKeys.Weapon + "knight",
            EquipPrefKeys.OffHand + "knight",
            EquipPrefKeys.Armor + "knight",
            EquipPrefKeys.Ring + "knight",
            EquipPrefKeys.Amulet + "knight",
        };

        private readonly StringBuilder _log = new StringBuilder();
        private readonly List<string> _written = new List<string>();
        private readonly List<string> _failures = new List<string>();
        private readonly Dictionary<string, string> _prefSnapshot = new Dictionary<string, string>();

        private Camera _cam;
        private RenderTexture _rt;

        private IEnumerator Start()
        {
            DontDestroyOnLoad(gameObject);
            bool finished = true;
            try
            {
                Directory.CreateDirectory(KnightGearProofCapture.OutDir);
                SnapshotPrefs();
            }
            catch (System.Exception ex)
            {
                _failures.Add($"setup threw {ex.GetType().Name}: {ex.Message}");
            }

            IEnumerator body = Body();
            while (true)
            {
                object current;
                try
                {
                    if (!body.MoveNext()) break;
                    current = body.Current;
                }
                catch (System.Exception ex)
                {
                    _failures.Add($"EXCEPTION {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}");
                    finished = false;
                    break;
                }
                yield return current;
            }

            try { RestorePrefs(); } catch { /* restoring prefs must never mask the finding */ }
            Finish(finished);
        }

        // =====================================================================
        //  THE RUN
        // =====================================================================
        private IEnumerator Body()
        {
            if (KnightGearProofCapture.IsolatedProvider == null ||
                !ReferenceEquals(GameStateService.Provider, KnightGearProofCapture.IsolatedProvider) ||
                !GameStateService.SuppressCloudForIsolatedProof)
            {
                _failures.Add("save isolation was not installed before runtime startup; refusing to choose a hero");
                yield break;
            }
            _log.AppendLine("KNIGHT GEAR PROOF — the starter sword + shield on the real Knight, drawn and sheathed.");
            _log.AppendLine($"mode=PLAY (batchmode={Application.isBatchMode}) res={ResX}x{ResY} backdrop=mid-grey(0.46)");
            _log.AppendLine($"graphicsDevice={SystemInfo.graphicsDeviceType} screen={Screen.width}x{Screen.height}");
            _log.AppendLine(new string('-', 112));

            // ── FRESH-START STATE ────────────────────────────────────────────
            // The owner asked about "on start". The starter shield seed in
            // HeroBodySwapper is guarded on EquippedOffHand == null, which on a real
            // fresh game is true because the New-Game flow clears these keys. Clear
            // them here for the same reason — otherwise this harness would prove the
            // PERSISTED loadout, not the starter kit.
            foreach (string k in KnightPrefKeys) PlayerPrefs.DeleteKey(k);
            PlayerPrefs.Save();

            yield return null;   // let RuntimeInitializeOnLoadMethod install GameStateService

            var gss = GameStateService.Instance;
            if (gss == null)
            {
                _failures.Add("GameStateService.Instance is null in play mode — cannot choose the Knight.");
                yield break;
            }
            gss.ChooseHero(HeroClass.Knight);
            _log.AppendLine($"STATE   GameStateService.ChooseHero(Knight) -> HeroClass='{gss.State?.HeroClass}'");
            _log.AppendLine($"FLAGS   ff.knightv3={DeNelle.Core.FeatureFlags.KnightV3} ff.heropackage={DeNelle.Core.FeatureFlags.HeroPackage}");

            var kit = StarterLoadout.For("knight");
            _log.AppendLine($"KIT     StarterLoadout['knight'] main='{kit?.MainHand ?? "<null>"}' off='{kit?.OffHand ?? "<null>"}'");

            // The seat is registry-refined, and the registry OVERLAYS persistentDataPath user
            // settings on top of the shipped defaults. So the same code can seat differently on
            // two machines — name the rows in the report or a reader cannot reproduce the numbers.
            AttachmentOffsetRegistry.Reload();
            _log.AppendLine($"OFFSETS registry rows={AttachmentOffsetRegistry.Count} userFile='{AttachmentOffsetRegistry.DevPath}'");
            foreach (string k in new[] { "sword_A", "sword_A@sheathed", "shield_A", "shield_A@sheathed",
                                         "ShieldWithItemLogic", "ShieldWithItemLogic@sheathed" })
            {
                string row = "<no row>";
                if (AttachmentOffsetRegistry.TryGetOffset(k, out AttachmentOffset fo))
                    row = $"pos={V(fo.pos)} rot={V(fo.eulerRot)} scale={fo.scale:0.###} fullOverride={fo.fullOverride}";
                _log.AppendLine($"{"",-8}  {k,-30} {row}");
            }

            // ── BUILD THE HERO THROUGH THE SHIPPED PATH ──────────────────────
            var hero = new GameObject("Hero (Grom)");
            hero.transform.position = Vector3.zero;
            hero.AddComponent<HeroBodySwapper>();     // Start() runs next frame: KnightV3 -> WireHeroBody -> gear

            // Let the body build, the controller enable, and the Addressable shield land.
            Transform weaponProp = null, offHandProp = null;
            for (int i = 0; i < 420 && (weaponProp == null || offHandProp == null); i++)
            {
                yield return null;
                DisableNavAgents(hero);
                hero.transform.position = Vector3.zero;
                weaponProp = FindByName(hero.transform, "EquipmentProp_Weapon");
                offHandProp = FindByName(hero.transform, "EquipmentProp_OffHand");
            }
            // A few more frames so any late re-seat (LateAttachRetry / ApplyHoldPose) settles.
            for (int i = 0; i < 20; i++) { yield return null; DisableNavAgents(hero); hero.transform.position = Vector3.zero; }

            var loadout = hero.GetComponent<GearLoadout>();
            var equip = hero.GetComponent<EquipmentController>();
            Transform bodyT = hero.transform.Find("HeroBody");
            Animator anim = bodyT != null ? bodyT.GetComponentInChildren<Animator>() : null;

            _log.AppendLine($"BUILD   body='{(bodyT != null ? bodyT.name : "<none>")}' " +
                            $"animator={(anim != null ? (anim.isHuman ? "HUMANOID" : "generic") : "<null>")} " +
                            $"gearLoadout={(loadout != null)} equipmentController={(equip != null)} " +
                            $"packageBakedMarker={(hero.GetComponent<PackageBakedGearMarker>() != null)}");
            _log.AppendLine($"LOADOUT EquippedWeapon='{loadout?.EquippedWeapon?.id ?? "<null>"}' " +
                            $"EquippedOffHand='{loadout?.EquippedOffHand?.id ?? "<null>"}'");
            _log.AppendLine($"PROPS   weaponProp={(weaponProp != null ? "PRESENT" : "ABSENT")} " +
                            $"offHandProp={(offHandProp != null ? "PRESENT" : "ABSENT")}");

            if (loadout == null || equip == null || anim == null || !anim.isHuman)
            {
                _failures.Add("the shipped body/gear path did not produce a humanoid hero with a GearLoadout + EquipmentController");
                yield break;
            }
            if (loadout.EquippedWeapon == null)
                _failures.Add("EquippedWeapon is NULL after the starter path — the Knight loaded with NO sword.");
            if (loadout.EquippedOffHand == null)
                _failures.Add("EquippedOffHand is NULL after the starter path — the Knight loaded with NO shield.");
            if (weaponProp == null)
                _failures.Add("no 'EquipmentProp_Weapon' in the hierarchy — the sword MESH never attached.");
            if (offHandProp == null)
                _failures.Add("no 'EquipmentProp_OffHand' in the hierarchy — the shield MESH never attached.");

            Transform rHand = anim.GetBoneTransform(HumanBodyBones.RightHand);
            Transform lHand = anim.GetBoneTransform(HumanBodyBones.LeftHand);
            Transform hips = anim.GetBoneTransform(HumanBodyBones.Hips);
            _log.AppendLine($"BONES   RightHand='{Nm(rHand)}' LeftHand='{Nm(lHand)}' Hips='{Nm(hips)}'");

            BuildRig(anim.transform);
            DumpProp("SWORD ", weaponProp);
            DumpProp("SHIELD", offHandProp);

            // ── DRAWN ────────────────────────────────────────────────────────
            // ⚠ MEASURED, not assumed (first run, 2026-08-20): SetCombatActive(true) alone did NOT
            // hold. The trace showed "carry state -> DRAWN (hand)" immediately followed by
            // "carry state -> SHEATHED (hip)" and both capture sets came out sheathed. The cause is
            // HeroLocomotion, which drives `equip.SetCombatActive(engaged)` EVERY Update from the
            // canonical in-combat signal (a live wave / BattleLock) — and in an empty scratch scene
            // that signal is permanently false, so it re-sheathed the props the very next frame.
            // Suspending that one driver holds the state the game holds during a wave. It changes
            // NOTHING about the seat: the drawn pose still comes from the same
            // EquipmentController.SetCombatActive -> ApplyHoldPose call HeroLocomotion itself makes.
            var loco = hero.GetComponent<HeroLocomotion>();
            if (loco != null) loco.enabled = false;
            equip.SetCombatActive(true);
            for (int i = 0; i < 4; i++) yield return null;
            _log.AppendLine();
            _log.AppendLine("== DRAWN (combat) ==========================================================");
            _log.AppendLine($"HeroLocomotion suspended for this half (it force-sheathes out of combat): {(loco != null)}");
            _log.AppendLine($"IsWeaponDrawn={equip.IsWeaponDrawn} CombatActive={equip.CombatActive}");
            if (!equip.IsWeaponDrawn)
                _failures.Add("the DRAWN half never reached the drawn state — every 'DRAWN' PNG below is really the sheathed pose and proves nothing about the grip.");
            MeasureSlot("DRAWN sword ", weaponProp, rHand, anim.transform, hips, drawn: true, offHand: false);
            MeasureSlot("DRAWN shield", offHandProp, lHand, anim.transform, hips, drawn: true, offHand: true);
            Transform drawnShieldParent = offHandProp.parent;
            Vector3 drawnShieldPosition = offHandProp.localPosition;
            Quaternion drawnShieldRotation = offHandProp.localRotation;
            Vector3 drawnShieldScale = offHandProp.localScale;
            yield return Shoot("01_DRAWN_front34", hero, FullBodyAim(hero), FullBodyRadius(hero), ThreeQuarter(anim.transform, 0.75f, 0.18f));
            // The hand close-ups frame ON THE BONE with a radius wide enough to hold the hand AND
            // the near end of the prop in one shot — the first cut aimed at a lerp toward the prop
            // and cropped the fist right out of the picture, which is the one thing the shot exists
            // to show.
            yield return Shoot("02_DRAWN_weaponhand_closeup", hero, rHand != null ? rHand.position : FullBodyAim(hero), 0.42f, ThreeQuarter(anim.transform, 1.05f, 0.12f));
            // ── THE SAME SHOT WITH THE HAND BONE MARKED ────────────────────────────────────
            // The unmarked close-up is genuinely ambiguous to a human eye: a black gauntlet on
            // black armour against a dark tabard, and a reviewer reading it concluded the fist was
            // up on the blade with the hilt dangling. The MEASUREMENT can settle that, but the
            // owner reviews pictures — so put the measurement IN the picture. This is the identical
            // frame with a small bright marker at the weapon-hand bone, so "where is the hand
            // relative to the hilt" stops being an inference. It is a diagnostic overlay, labelled
            // as one in the filename; the unmarked 02 stays the clean image.
            // Colour is bright cyan-white, NOT red and NOT green — the owner is red/green
            // colourblind, so the marker must read on luminance alone.
            var marker = MakeBoneMarker(rHand);
            yield return Shoot("02b_DRAWN_weaponhand_MARKED", hero, rHand != null ? rHand.position : FullBodyAim(hero), 0.42f, ThreeQuarter(anim.transform, 1.05f, 0.12f), marker);
            if (marker != null) Object.DestroyImmediate(marker);
            yield return Shoot("03_DRAWN_offhand_closeup", hero, lHand != null ? lHand.position : FullBodyAim(hero), 0.55f, ThreeQuarter(anim.transform, -0.85f, 0.12f));

            // ── SHEATHED ─────────────────────────────────────────────────────
            equip.SetCombatActive(false);
            for (int i = 0; i < 4; i++) yield return null;
            _log.AppendLine();
            _log.AppendLine("== SHEATHED (town) =========================================================");
            _log.AppendLine($"IsWeaponDrawn={equip.IsWeaponDrawn} CombatActive={equip.CombatActive}");
            if (equip.IsWeaponDrawn)
                _failures.Add("the SHEATHED half never left the drawn state — the sheathed PNGs are not the sheathed pose.");
            var mainSheath = MeasureSlot("SHEATHED sword ", weaponProp, rHand, anim.transform, hips, drawn: false, offHand: false);
            var offSheath = MeasureSlot("SHEATHED shield", offHandProp, lHand, anim.transform, hips, drawn: false, offHand: true);
            AssertDistinctForearmMount(mainSheath, offSheath, offHandProp, anim);
            // Locked heater canon (2026-08-30): the same authored strap pose on LeftLowerArm
            // in both states. The retired vertical-at-hip shield test judged the wrong contract.
            if (offHandProp.parent != drawnShieldParent ||
                Vector3.Distance(offHandProp.localPosition, drawnShieldPosition) > .001f ||
                Quaternion.Angle(offHandProp.localRotation, drawnShieldRotation) > .1f ||
                Vector3.Distance(offHandProp.localScale, drawnShieldScale) > .001f)
                _failures.Add("locked shield strap pose changed between drawn and sheathed states");
            _log.AppendLine("SHIELD STRAP: comparing parent/local position/rotation/scale to the captured drawn pose.");
            yield return Shoot("04_SHEATHED_front34", hero, FullBodyAim(hero), FullBodyRadius(hero), ThreeQuarter(anim.transform, 0.75f, 0.18f));
            yield return Shoot("05_SHEATHED_left34", hero, FullBodyAim(hero), FullBodyRadius(hero), ThreeQuarter(anim.transform, -0.75f, 0.18f));
            yield return Shoot("06_SHEATHED_hips_closeup", hero, HipsAim(hips, weaponProp, offHandProp), 0.55f, ThreeQuarter(anim.transform, 0.75f, 0.05f));
            Vector3 gameplayRear = RearThreeQuarter(anim.transform, -0.28f, 0.16f);
            yield return Shoot("07_SHEATHED_gameplay_rear", hero, FullBodyAim(hero), FullBodyRadius(hero), gameplayRear);
            // Same camera/framing, but only the shield is allowed through the culling mask.
            // This is the projected-area control: nonzero world bounds are insufficient; the
            // plate itself must occupy a measurable set of pixels from the player's view.
            yield return Shoot("08_SHEATHED_gameplay_rear_SHIELD_ONLY", offHandProp.gameObject,
                FullBodyAim(hero), FullBodyRadius(hero), gameplayRear);
        }

        // =====================================================================
        //  MEASUREMENT — the part that makes this data
        // =====================================================================
        private struct SlotMeasure
        {
            public bool valid;
            public string label;
            public Vector3 hiltWorld;
            public Vector3 tipWorld;
            public Vector3 centreWorld;
            public float lengthM;
            public float sideOfBody;      // signed metres along body.right, relative to the hips
            public string parentName;
        }

        private SlotMeasure MeasureSlot(string label, Transform gripRoot, Transform handBone,
                                        Transform bodyT, Transform hips, bool drawn, bool offHand)
        {
            var m = new SlotMeasure { label = label };
            if (gripRoot == null)
            {
                _log.AppendLine($"{label}: ABSENT — nothing to measure.");
                return m;
            }
            m.parentName = Nm(gripRoot.parent);

            if (!TryLongAxis(gripRoot, out Vector3 axisLocal, out float loT, out float hiT,
                             out Vector3 loCentroid, out Vector3 hiCentroid,
                             out Vector3 originAlongAxisLocal,
                             out float loWidth, out float hiWidth, out int rendererCount))
            {
                _log.AppendLine($"{label}: parent='{m.parentName}' — NO MEASURABLE BOUNDS (renderers={rendererCount}); " +
                                "the prop has no renderer this harness can measure.");
                _failures.Add($"{label}: prop has no measurable renderer bounds — cannot be proven either way.");
                return m;
            }

            // WHICH END IS THE HILT (see TryLongAxis for the full reasoning). Both props take the
            // shipped NATIVE path, whose contract is "the artist put the GRIP AT THE MESH ORIGIN" —
            // so the hilt is whichever end of the long axis sits on that origin. loWidth/hiWidth
            // carry each end's OPPOSITE-end distance, so the bigger one names the origin end.
            // Decisive only when the two distances are genuinely lopsided; a centre-origin prop
            // makes them equal, and then we SAY the hilt could not be identified rather than
            // picking an end — an unproven claim printed as a measurement is what this exists to stop.
            float dHiEnd = loWidth, dLoEnd = hiWidth;
            bool hiltIdentified = Mathf.Min(dLoEnd, dHiEnd) < 0.6f * Mathf.Max(dLoEnd, dHiEnd);
            bool hiltAtLo = dLoEnd < dHiEnd;
            Vector3 hiltLocal = hiltAtLo ? loCentroid : hiCentroid;
            Vector3 tipLocal = hiltAtLo ? hiCentroid : loCentroid;

            m.hiltWorld = gripRoot.TransformPoint(hiltLocal);
            m.tipWorld = gripRoot.TransformPoint(tipLocal);
            m.centreWorld = gripRoot.TransformPoint((hiltLocal + tipLocal) * 0.5f);
            m.lengthM = Vector3.Distance(m.hiltWorld, m.tipWorld);
            m.valid = true;

            Vector3 longAxisWorld = (m.tipWorld - m.hiltWorld).normalized;
            float angleFromUp = Vector3.Angle(longAxisWorld, Vector3.up);
            float offVertical = Mathf.Min(angleFromUp, 180f - angleFromUp);
            if (hips != null)
                m.sideOfBody = Vector3.Dot(m.centreWorld - hips.position, bodyT.right);

            _log.AppendLine($"{label}: parent='{m.parentName}' renderers={rendererCount} length={m.lengthM:0.###}m " +
                            $"| mesh origin sits {dLoEnd:0.###}m from the LOW end and {dHiEnd:0.###}m from the HIGH end " +
                            $"=> hilt end {(hiltIdentified ? (hiltAtLo ? "= the LOW end (grip-at-origin)" : "= the HIGH end (grip-at-origin)") : "COULD NOT BE IDENTIFIED (origin is centred — neither end is the grip)")}");
            _log.AppendLine($"{"",-16}  world hilt={V(m.hiltWorld)} tip={V(m.tipWorld)}");

            if (handBone != null)
            {
                float dHilt = Vector3.Distance(handBone.position, m.hiltWorld);
                float dTip = Vector3.Distance(handBone.position, m.tipWorld);
                bool hiltNearer = dHilt < dTip - m.lengthM * HiltMarginFraction;
                _log.AppendLine($"{"",-16}  bone '{Nm(handBone)}' -> hilt-end={dHilt:0.####}m  tip-end={dTip:0.####}m  " +
                                $"=> {(hiltNearer ? "hilt end is the nearer one" : (dTip < dHilt ? "⛔ TIP IS NEARER — THE BLADE IS IN THE FIST" : "⚠ AMBIGUOUS — neither end is clearly nearer"))}");

                // ── THE GRIP-SEGMENT TEST — the one that actually answers the question ──────
                // ⚠ THE NEAREST-END TEST ABOVE IS KEPT BUT IS NO LONGER THE ASSERTION, and the
                // reason is the whole lesson of this harness. "The hilt end is nearer than the tip
                // end" stays TRUE however far the sword slides along its own axis, right up until
                // the pommel passes the fist — so it cannot tell "held by the grip" from "held at
                // the crossguard with the whole hilt dangling free below the hand". That is the
                // same class of error as a bone-delta check passing over a T-posed body: correct,
                // and answering a different question than the one asked.
                //
                // So project the HAND BONE onto the prop's own long axis and ask WHERE it lands,
                // as a 0..1 fraction of the grip segment: 0 = the pommel, 1 = the crossguard.
                // Below 0 the hand is past the pommel (off the end); above 1 it is up on the BLADE.
                //
                // ⚠ NAMED APPROXIMATION — the crossguard's position is ESTIMATED, not measured.
                // Locating it for real needs the vertex width profile, and these meshes ship with
                // Read/Write OFF. The estimate: a NATIVE prop is authored grip-at-origin, so the
                // mesh origin IS the intended grip point, and the guard is taken to sit as far
                // ABOVE the origin as the pommel sits below it. For Sword1h_01 the mesh's own
                // bounds put 0.157 of its 0.888 span below the origin, so the segment is that
                // 0.157 doubled — which is also a sanity check on the convention, since a prop
                // authored to any other convention would not have a short stub below its origin.
                if (!offHand && hiltIdentified)
                {
                    Vector3 axisWorld = (m.tipWorld - m.hiltWorld).normalized;
                    Vector3 originWorld = gripRoot.TransformPoint(originAlongAxisLocal);
                    float tPommel = Vector3.Dot(m.hiltWorld - originWorld, axisWorld);   // negative
                    float tGuard = -tPommel;                                              // mirrored estimate
                    float tHand = Vector3.Dot(handBone.position - originWorld, axisWorld);
                    float span = Mathf.Max(1e-5f, tGuard - tPommel);
                    float gripFrac = (tHand - tPommel) / span;
                    float lateral = Vector3.Distance(handBone.position,
                        originWorld + axisWorld * tHand);   // how far off the axis line the fist sits
                    _log.AppendLine($"{"",-16}  GRIP SEGMENT (pommel=0 .. crossguard=1, guard position ESTIMATED " +
                                    $"by mirroring the pommel about the authored grip-at-origin point): " +
                                    $"segment={span:0.###}m, hand projects at {gripFrac:0.###} " +
                                    $"({(tHand - tPommel):0.###}m from the pommel), off-axis={lateral:0.###}m " +
                                    $"=> {(gripFrac >= GripFracMin && gripFrac <= GripFracMax ? "HAND IS ON THE GRIP ✓" : (gripFrac > GripFracMax ? "⛔ HAND IS UP ON THE BLADE — the hilt dangles free below the fist" : "⛔ HAND IS PAST THE POMMEL — off the end of the weapon"))}");
                    if (drawn && (gripFrac < GripFracMin || gripFrac > GripFracMax))
                        _failures.Add($"{label}: the weapon hand projects at {gripFrac:0.###} of the grip segment " +
                                      $"(acceptable {GripFracMin:0.##}-{GripFracMax:0.##}; 0=pommel, 1=crossguard) — " +
                                      (gripFrac > GripFracMax
                                        ? "the fist is up on the BLADE and the whole hilt hangs free below it."
                                        : "the fist is off the end of the weapon, past the pommel."));
                }
                if (drawn && !offHand && !hiltIdentified)
                    _failures.Add($"{label}: the mesh origin is centred, so neither end reads as the grip and the " +
                                  "grip-segment test cannot run (the mesh ships with Read/Write OFF, so no vertex " +
                                  "taper test is available either). The hand-to-end distances are reported but the " +
                                  "HILT-IN-THE-GRIP claim is UNPROVEN — it is not being asserted.");
                if (drawn && offHand)
                {
                    // A shield has no hilt/tip; what matters is that it is ON the off arm.
                    float dCentre = Vector3.Distance(handBone.position, m.centreWorld);
                    _log.AppendLine($"{"",-16}  shield centre is {dCentre:0.####}m from '{Nm(handBone)}' " +
                                    $"(maximum {DrawnShieldCentreMaxM:0.##}m for a centre/strap seat)");
                    if (dCentre > DrawnShieldCentreMaxM)
                        _failures.Add($"{label}: shield centre is {dCentre:0.####}m from the off hand — further than its own " +
                                      $"{m.lengthM:0.###}m span. It is not on the arm.");
                }
            }
            else if (drawn)
            {
                _failures.Add($"{label}: the hand bone could not be resolved on this rig — the drawn seat cannot be proven.");
            }

            if (!drawn && !offHand)
            {
                _log.AppendLine($"{"",-16}  SHEATHED long axis: {angleFromUp:0.#}° from world UP " +
                                $"({offVertical:0.#}° off vertical) => {(offVertical <= SheathVerticalTolDeg ? "VERTICAL ✓" : "⛔ NOT VERTICAL")}; " +
                                $"{(angleFromUp > 90f ? "TIP DOWN (inverted, hilt up at the belt)" : "TIP UP (hilt down)")}");
                _log.AppendLine($"{"",-16}  hip side (signed metres along body.right from '{Nm(hips)}'): {m.sideOfBody:+0.###;-0.###} " +
                                $"=> hero's {(m.sideOfBody < 0f ? "LEFT" : "RIGHT")} hip");
                if (offVertical > SheathVerticalTolDeg)
                    _failures.Add($"{label}: sheathed long axis is {offVertical:0.#}° off vertical (tolerance {SheathVerticalTolDeg:0}°) — " +
                                  "the owner's ruling is that the longest mesh axis runs up and down at the hip.");
                if (!offHand && angleFromUp <= 90f)
                    _failures.Add($"{label}: sheathed sword is TIP UP ({angleFromUp:0.#}° from world up) — the ruling is INVERTED (tip down).");
                float hiltSide = hips != null ? Vector3.Dot(m.hiltWorld - hips.position, bodyT.right) : 0f;
                float tipDownAngle = Vector3.Angle(m.tipWorld - m.hiltWorld, -bodyT.up);
                _log.AppendLine($"SWORD MAIN HIP: measured hiltSide={hiltSide:0.###}m centreSide={m.sideOfBody:0.###}m tipDownAngle={tipDownAngle:0.#}deg");
                if (!hiltIdentified || hips == null || hiltSide <= 0f || m.sideOfBody <= 0f ||
                    tipDownAngle > SheathVerticalTolDeg)
                    _failures.Add("sheathed sword must have measured hilt on the main-hand/right hip and blade down (owner 2026-09-13)");
                // ── IS IT ACTUALLY ON THE HIP? ────────────────────────────────────────────
                // Angles are not position. The 2026-08-20 shield read "faceOffOutward=0deg
                // longTiltFromVertical=0deg" — a perfect score — while floating at chest height
                // with backdrop visible between it and the torso. So measure the thing the eye
                // objects to: how far the part that should be AT the belt actually is from the
                // hips bone. For a sword that part is the HILT (it hangs from the belt, blade
                // down); for a shield it is the plate's CENTRE (it has no end to hang by).
                if (hips != null)
                {
                    Vector3 anchorPoint = offHand ? m.centreWorld : m.hiltWorld;
                    float dHip = Vector3.Distance(anchorPoint, hips.position);
                    _log.AppendLine($"{"",-16}  hip seat: {(offHand ? "plate centre" : "hilt end")} is {dHip:0.###}m from " +
                                    $"'{Nm(hips)}' at {V(hips.position)} => {(dHip <= SheathHipSeatMaxM ? "ON THE HIP ✓" : "⛔ OFF THE BODY")}");
                    if (dHip > SheathHipSeatMaxM)
                        _failures.Add($"{label}: the {(offHand ? "plate centre" : "hilt")} sits {dHip:0.###}m from the hips bone " +
                                      $"(limit {SheathHipSeatMaxM:0.##}m) — the prop is hanging off the body, not carried at the hip.");
                }
                if (hips != null && !IsUnder(gripRoot, hips))
                    _failures.Add($"{label}: sheathed prop is NOT parented under the Hips bone '{Nm(hips)}' " +
                                  $"(its parent chain is '{m.parentName}') — the ruling is a hip carry.");
            }

            // CROSS-CHECK against the shipped helper. Reported, never asserted on: if the shipped
            // answer were the oracle, a wrong shipped answer would validate itself.
            var propGo = gripRoot.childCount > 0 ? gripRoot.GetChild(0).gameObject : gripRoot.gameObject;
            if (WeaponOrientHelper.TryResolveSwordHiltEnd(propGo, gripRoot, out bool shippedHiltAtMinY,
                                                          out float sMin, out float sMax))
                _log.AppendLine($"{"",-16}  cross-check WeaponOrientHelper.TryResolveSwordHiltEnd: hilt at " +
                                $"{(shippedHiltAtMinY ? "-Y" : "+Y")} (endWidth -Y={sMin:0.#####} +Y={sMax:0.#####})");
            else
                _log.AppendLine($"{"",-16}  cross-check WeaponOrientHelper.TryResolveSwordHiltEnd: DECLINED " +
                                "(ambiguous taper / unreadable mesh) — this harness's own taper test stands.");
            return m;
        }

        private void AssertDistinctForearmMount(SlotMeasure main, SlotMeasure off, Transform offGrip, Animator animator)
        {
            if (!main.valid || !off.valid)
            {
                _log.AppendLine("MOUNTS  one or both sheathed props could not be measured — distinct sword/forearm mounts are UNPROVEN.");
                return;
            }
            float sep = Mathf.Abs(main.sideOfBody - off.sideOfBody);
            Transform forearmBone = animator != null ? animator.GetBoneTransform(HumanBodyBones.LeftLowerArm) : null;
            bool forearm = offGrip != null && forearmBone != null && IsUnder(offGrip, forearmBone);
            _log.AppendLine($"MOUNTS  sword side={main.sideOfBody:+0.###;-0.###}m  shield side={off.sideOfBody:+0.###;-0.###}m  separation={sep:0.###}m");
            _log.AppendLine($"MOUNTS  sword parent='{main.parentName}' shield parent='{off.parentName}' " +
                            $"=> {(main.parentName != off.parentName ? "DISTINCT ✓" : "⛔ SHARED SOCKET")}; " +
                            $"shield forearm={(forearm ? "YES ✓" : "NO ⛔")}");
            if (!forearm)
                _failures.Add($"sheathed shield is not on the owner-approved forearm socket (parent '{off.parentName}').");
            if (main.parentName == off.parentName)
                _failures.Add($"sheathed props share ONE socket transform ('{main.parentName}').");
            if (main.sideOfBody <= 0f || off.sideOfBody >= 0f)
                _failures.Add("sheathed sword and shield must occupy main/right and off/left sides respectively");
        }

        /// <summary>
        /// The prop's own long axis, derived from its vertices in the GRIP ROOT frame. Deliberately
        /// does NOT assume +Y: the knight_starter row is Native(...), so NormalizeInto (which is what
        /// puts the blade on +Y) is SKIPPED for it and the prop keeps its authored axes.
        /// Returns the two end-band centroids and each band's max off-axis width (the taper signal).
        /// </summary>
        private static bool TryLongAxis(Transform gripRoot, out Vector3 axisLocal,
                                        out float loT, out float hiT,
                                        out Vector3 loCentroid, out Vector3 hiCentroid,
                                        out Vector3 originOnAxisLocal,
                                        out float loWidth, out float hiWidth, out int vertCount)
        {
            axisLocal = Vector3.up; loT = hiT = 0f;
            loCentroid = hiCentroid = originOnAxisLocal = Vector3.zero; loWidth = hiWidth = 0f;

            // ⛔ NO VERTEX ACCESS. MEASURED, 2026-08-20 first run: the live props' meshes ship with
            // Read/Write OFF ("Not allowed to access vertices on mesh 'fantasy_shield'"), and the
            // fix for that is NOT to flip isReadable on shipped art to satisfy a test — that doubles
            // those meshes' runtime memory to buy a harness a convenience. Everything the owner's
            // question needs is in the BOUNDS, which are available without Read/Write.
            //
            // ⚠ AND NOT `Renderer.bounds`, EITHER. That is a WORLD-space AABB; re-expressing its
            // extents in another basis (`parent.InverseTransformVector(r.bounds.extents)`) smears a
            // rotated box into a bigger axis-aligned one and silently reorders which axis is
            // "longest" — a real bug fixed in WeaponOrientHelper.TryLocalBounds earlier the same day.
            // So: take each renderer's OWN LOCAL bounds, transform its EIGHT CORNERS into the grip
            // root's frame, and union those. Same recipe as the fixed helper.
            var pts = new List<Vector3>();
            int rendererCount = 0;
            Transform meshOwner = null;
            foreach (var r in gripRoot.GetComponentsInChildren<Renderer>(true))
            {
                if (r == null) continue;
                if (!TryRendererLocalBounds(r, out Bounds rb)) continue;
                rendererCount++;
                if (meshOwner == null) meshOwner = r.transform;
                Vector3 c = rb.center, e = rb.extents;
                for (int corner = 0; corner < 8; corner++)
                {
                    var p = new Vector3(
                        c.x + ((corner & 1) == 0 ? -e.x : e.x),
                        c.y + ((corner & 2) == 0 ? -e.y : e.y),
                        c.z + ((corner & 4) == 0 ? -e.z : e.z));
                    pts.Add(gripRoot.InverseTransformPoint(r.transform.TransformPoint(p)));
                }
            }
            vertCount = rendererCount;
            if (pts.Count < 8) return false;

            // Largest-extent axis of those local bounds. A weapon prop is a long thin thing, so the
            // dominant bounds axis IS its length.
            Vector3 min = pts[0], max = pts[0];
            for (int i = 1; i < pts.Count; i++) { min = Vector3.Min(min, pts[i]); max = Vector3.Max(max, pts[i]); }
            Vector3 size = max - min;
            int a = (size.x >= size.y && size.x >= size.z) ? 0 : (size.y >= size.z ? 1 : 2);
            axisLocal = a == 0 ? Vector3.right : (a == 1 ? Vector3.up : Vector3.forward);
            float span = size[a];
            if (span < 1e-4f) return false;
            loT = min[a]; hiT = max[a];

            // The two END POINTS on the prop's own long axis, taken on the axis line through the
            // box centre. These are what the hand-distance test compares.
            Vector3 mid = (min + max) * 0.5f;
            loCentroid = mid; loCentroid[a] = loT;
            hiCentroid = mid; hiCentroid[a] = hiT;

            // ── WHICH END IS THE HILT: THE GRIP-AT-ORIGIN CONTRACT ──────────────────────────
            // ⚠ FIRST ATTEMPT, RETIRED IN THE SAME NIGHT: this scored the two ends by SUBMESH
            // clustering (short submeshes = guard/pommel = hilt). Measured, it answered NOTHING —
            // both live props are ONE submesh ("Sword1h_01 ... subMeshes=1", "fantasy_shield ...
            // subMeshes=1"), so the score came back 0 vs 0 and the harness correctly refused to
            // claim a hilt. A rule that cannot fire on the only two props in question is not a rule.
            //
            // The signal that IS there comes from the shipped code's own contract. Both props take
            // the NATIVE path — the trace says so in as many words: "seat: NATIVE melee (WO-478
            // trust grip-at-origin + scale)" and "off-hand seat: NATIVE (trust authored
            // grip-at-origin, scale-only)". NATIVE means the artist authored the prop with THE GRIP
            // AT THE MESH ORIGIN, which is precisely why NormalizeInto and the hilt-seat are skipped
            // for it. So the hilt end is the end of the long axis SITTING ON THE ORIGIN.
            //
            // And the bounds agree, unambiguously:
            //   Sword1h_01     localBounds c=(0, 0.287, 0)  s=(0.157, 0.888, 0.053)
            //                  -> spans Y [-0.157 .. +0.731]: one end 0.157 m from the origin, the
            //                     other 0.731 m. 4.7x. The blade runs +Y, the hilt sits on 0.
            //   fantasy_shield localBounds c=(0, 0.315, 0.035) s=(0.512, 0.63, 0.161)
            //                  -> spans Y [0 .. +0.63]: the handle end IS the origin, exactly.
            // This is measured per-run from the live mesh, never hard-coded — the numbers above are
            // only here so a reader can see why the test is decisive rather than take it on trust.
            // If a future prop centres its origin, the ratio approaches 1, the margin below rejects
            // it, and the harness says the hilt could not be identified instead of inventing one.
            // The origin that matters is the MESH OWNER's, not the grip root's — they coincide today
            // (the seat dump reads prop.localPos=(0,0,0)) but a future prop offset inside its grip
            // root would silently move the answer, and this test must follow the mesh.
            Vector3 originLocal = gripRoot.InverseTransformPoint(
                meshOwner != null ? meshOwner.position : gripRoot.position);
            originOnAxisLocal = mid; originOnAxisLocal[a] = originLocal[a];
            float dLo = Mathf.Abs(loT - originLocal[a]);
            float dHi = Mathf.Abs(hiT - originLocal[a]);
            loWidth = dHi;   // reported as "hiltScore": bigger = this end is the one AT the origin
            hiWidth = dLo;
            return true;
        }

        /// <summary>The renderer's bounds in ITS OWN local space (never world) — the same
        /// distinction WeaponOrientHelper.TryRendererLocalBounds draws, and for the same reason.</summary>
        private static bool TryRendererLocalBounds(Renderer r, out Bounds local)
        {
            local = default;
            if (r is SkinnedMeshRenderer smr) { local = smr.localBounds; return local.size.sqrMagnitude > 1e-9f; }
            Mesh m = MeshOf(r);
            if (m == null) return false;
            local = m.bounds;
            return local.size.sqrMagnitude > 1e-9f;
        }

        private static Mesh MeshOf(Renderer r)
        {
            if (r is SkinnedMeshRenderer smr) return smr.sharedMesh;
            var mf = r.GetComponent<MeshFilter>();
            return mf != null ? mf.sharedMesh : null;
        }

        /// <summary>Everything we can say about a prop's meshes WITHOUT touching vertices. Printed
        /// verbatim so the next reader can reproduce the hilt decision instead of trusting it.</summary>
        private void DumpProp(string label, Transform gripRoot)
        {
            if (gripRoot == null) { _log.AppendLine($"{label} MESH: <no prop>"); return; }
            foreach (var r in gripRoot.GetComponentsInChildren<Renderer>(true))
            {
                if (r == null) continue;
                Mesh m = MeshOf(r);
                string mats = "";
                var sm = r.sharedMaterials;
                for (int i = 0; i < sm.Length; i++) mats += (i > 0 ? "," : "") + (sm[i] != null ? sm[i].name : "<null>");
                if (m == null) { _log.AppendLine($"{label} MESH: renderer '{r.name}' ({r.GetType().Name}) has NO mesh; mats=[{mats}]"); continue; }
                _log.AppendLine($"{label} MESH: '{m.name}' on '{r.name}' ({r.GetType().Name}) " +
                                $"isReadable={m.isReadable} subMeshes={m.subMeshCount} " +
                                $"localBounds c={V(m.bounds.center)} s={V(m.bounds.size)} mats=[{mats}]");
                for (int s = 0; s < m.subMeshCount; s++)
                {
                    Bounds sb;
                    try { sb = m.GetSubMesh(s).bounds; } catch { continue; }
                    _log.AppendLine($"{"",-8}    submesh[{s}] c={V(sb.center)} s={V(sb.size)}");
                }
            }
        }

        private static bool IsUnder(Transform t, Transform ancestor)
        {
            for (Transform p = t; p != null; p = p.parent) if (p == ancestor) return true;
            return false;
        }

        // =====================================================================
        //  CAPTURE RIG
        // =====================================================================
        private void BuildRig(Transform bodyT)
        {
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.48f, 0.48f, 0.50f, 1f);

            var camGo = new GameObject("~KnightProofCam");
            DontDestroyOnLoad(camGo);

            // ⛔ TAGGED MainCamera ON PURPOSE, and not for rendering — this camera renders fine
            // either way. It is tagged so HeroLocomotion.ResolveMovementBasisYaw can find a
            // Camera.main. Without it that method emits a FlowTrace.Fail every frame-ish:
            //   "[Flow:HeroLoco] NO MOVEMENT BASIS in scene '': there is no SmartMobileCamera and
            //    no Camera.main with a usable planar forward"
            // which is CORRECT for a camera-less scene and completely useless here — the harness
            // never moves the hero with a stick. On 2026-08-20 that one line put FIVE captures into
            // the owner's F8 triage inbox from a single night of harness runs, all of them noise
            // she had to have surfaced to her because the passive listener cannot tell a harness
            // scene from her device.
            // The fix belongs HERE, in the harness, NOT in HeroLocomotion: that Fail is a real
            // defect signal in a real scene (the stick's "forward" silently degrades to world +Z),
            // and softening product instrumentation to quieten a test rig would trade a genuine
            // alarm for a tidy log. Give the harness the camera it was always missing instead.
            camGo.tag = "MainCamera";

            _cam = camGo.AddComponent<Camera>();
            _cam.clearFlags = CameraClearFlags.SolidColor;
            // Plain mid-grey. NOT green, NOT red — the owner is red/green colourblind, so a tinted
            // backdrop would cost her the one channel she can actually read: contrast.
            _cam.backgroundColor = new Color(0.46f, 0.46f, 0.47f, 1f);
            _cam.fieldOfView = 32f;
            _cam.cullingMask = 1 << IsolationLayer;

            var keyGo = new GameObject("~KnightProofKey");
            DontDestroyOnLoad(keyGo);
            var key = keyGo.AddComponent<Light>();
            key.type = LightType.Directional;
            key.intensity = 1.45f;
            key.color = Color.white;
            keyGo.transform.rotation = Quaternion.LookRotation(
                (-bodyT.forward - bodyT.right * 0.6f + Vector3.down * 0.5f).normalized, Vector3.up);

            var fillGo = new GameObject("~KnightProofFill");
            DontDestroyOnLoad(fillGo);
            var fill = fillGo.AddComponent<Light>();
            fill.type = LightType.Directional;
            fill.intensity = 0.8f;
            fill.color = new Color(0.90f, 0.92f, 1f, 1f);
            fillGo.transform.rotation = Quaternion.LookRotation(
                (bodyT.forward * 0.4f + bodyT.right - Vector3.up * 0.2f).normalized, Vector3.up);

            _rt = new RenderTexture(ResX, ResY, 24, RenderTextureFormat.ARGB32);
            _cam.targetTexture = _rt;
        }

        /// <summary>Camera direction (target -> camera): in FRONT of the body, off to one side,
        /// a touch above. sideFactor &gt; 0 = the hero's right side of frame.</summary>
        private static Vector3 ThreeQuarter(Transform bodyT, float sideFactor, float up) =>
            (bodyT.forward + bodyT.right * sideFactor + Vector3.up * up).normalized;

        /// <summary>Normal third-person view: camera behind the hero, slightly offset.</summary>
        private static Vector3 RearThreeQuarter(Transform bodyT, float sideFactor, float up) =>
            (-bodyT.forward + bodyT.right * sideFactor + Vector3.up * up).normalized;

        private static Vector3 FullBodyAim(GameObject hero)
        {
            if (!TryWorldBounds(hero, out Bounds b)) return hero.transform.position + Vector3.up;
            return new Vector3(b.center.x, b.min.y + b.size.y * 0.55f, b.center.z);
        }

        private static float FullBodyRadius(GameObject hero) =>
            TryWorldBounds(hero, out Bounds b) ? Mathf.Max(0.5f, b.size.y * 0.62f) : 1.2f;

        private static Vector3 HandAim(Transform hand, Transform prop)
        {
            if (hand == null) return prop != null ? prop.position : Vector3.up;
            if (prop == null) return hand.position;
            return Vector3.Lerp(hand.position, prop.position, 0.25f);
        }

        private static Vector3 HipsAim(Transform hips, Transform a, Transform b)
        {
            if (hips != null) return hips.position;
            if (a != null) return a.position;
            return b != null ? b.position : Vector3.up;
        }

        /// <summary>Frame on a point + radius, isolate the subject on the spare layer, let the
        /// pipeline render a frame, then read back. Refuses (and reports) a blank frame.</summary>
        private IEnumerator Shoot(string name, GameObject subject, Vector3 aim, float radius, Vector3 camDir,
                                  GameObject extra = null)
        {
            string path = Path.Combine(KnightGearProofCapture.OutDir, name + ".png").Replace('\\', '/');
            if (_cam == null || _rt == null)
            {
                _failures.Add($"{name}: no capture rig — nothing was shot.");
                yield break;
            }

            float halfFov = _cam.fieldOfView * 0.5f * Mathf.Deg2Rad;
            float dist = Mathf.Max(0.12f, radius / Mathf.Tan(halfFov));
            _cam.transform.position = aim + camDir * dist;
            _cam.transform.LookAt(aim);
            _cam.nearClipPlane = 0.02f;
            _cam.farClipPlane = dist * 12f;

            var saved = new Dictionary<Transform, int>();
            MoveToLayer(subject.transform, IsolationLayer, saved);
            // An optional diagnostic overlay (e.g. the hand-bone marker) rides the same isolation
            // layer so it is photographed with the subject and nothing else can photobomb either.
            if (extra != null) MoveToLayer(extra.transform, IsolationLayer, saved);

            // ⚠ NOT WaitForEndOfFrame. It does not fire reliably in -batchmode, which would hang the
            // whole run — and a harness that hangs teaches nothing. Drive the pipeline explicitly
            // instead: Unity 6 URP answers Camera.Render() with an error, so ASK the pipeline via
            // SubmitRenderRequest and keep the legacy call only as the fallback for a machine whose
            // pipeline declines the request.
            yield return null;
            var request = new UnityEngine.Rendering.RenderPipeline.StandardRequest { destination = _rt };
            if (UnityEngine.Rendering.RenderPipeline.SupportsRenderRequest(_cam, request))
                UnityEngine.Rendering.RenderPipeline.SubmitRenderRequest(_cam, request);
            else
                _cam.Render();

            RenderTexture prev = RenderTexture.active;
            RenderTexture.active = _rt;
            var tex = new Texture2D(ResX, ResY, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, ResX, ResY), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;

            foreach (var kv in saved) if (kv.Key != null) kv.Key.gameObject.layer = kv.Value;

            float coverage = Coverage(tex, _cam.backgroundColor);
            if (coverage < BlankCoverageFloor)
            {
                _failures.Add($"{name}: BLANK FRAME (coverage {coverage:P3}) — nothing rendered; the PNG is not evidence and was still written for inspection.");
            }
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            if (coverage >= BlankCoverageFloor) _written.Add(path);
            _log.AppendLine($"SHOT    {name,-28} coverage={coverage:P2} dist={dist:0.###}m -> {path}");
        }

        private static float Coverage(Texture2D tex, Color bg)
        {
            var px = tex.GetPixels32();
            int lit = 0, n = 0;
            for (int i = 0; i < px.Length; i += 7)
            {
                n++;
                float rr = Mathf.Abs(px[i].r / 255f - bg.r);
                float gg = Mathf.Abs(px[i].g / 255f - bg.g);
                float bb = Mathf.Abs(px[i].b / 255f - bg.b);
                if (rr + gg + bb > 0.06f) lit++;
            }
            return n == 0 ? 0f : lit / (float)n;
        }

        // =====================================================================
        //  PLUMBING
        // =====================================================================
        /// <summary>A small unlit-bright sphere sitting exactly on a bone, for the diagnostic
        /// overlay shot. 3 cm across — big enough to find, small enough that it cannot hide the
        /// thing it is pointing at. Its collider is stripped so it can never disturb the scene.</summary>
        private static GameObject MakeBoneMarker(Transform bone)
        {
            if (bone == null) return null;
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = "~HandBoneMarker";
            var col = go.GetComponent<Collider>();
            if (col != null) Object.DestroyImmediate(col);
            go.transform.position = bone.position;
            go.transform.localScale = Vector3.one * 0.04f;
            var rend = go.GetComponent<Renderer>();
            if (rend != null)
            {
                // Cyan-white and UNLIT, so it reads against black armour without depending on the
                // key light. Never red, never green (the owner is red/green colourblind).
                Shader sh = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color");
                if (sh != null)
                {
                    var mat = new Material(sh);
                    if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", new Color(0.60f, 0.95f, 1f, 1f));
                    if (mat.HasProperty("_Color")) mat.SetColor("_Color", new Color(0.60f, 0.95f, 1f, 1f));
                    // ⚠ MEASURED, not assumed: the first cut of this marker was INVISIBLE in the
                    // render. The hand BONE sits inside the closed fist, so a solid sphere at it is
                    // swallowed by the gauntlet mesh — a diagnostic that answers nothing. Draw it
                    // THROUGH the geometry (depth test always, last queue) so the picture shows
                    // where the bone is even when the bone is inside something.
                    if (mat.HasProperty("_ZTest"))
                        mat.SetInt("_ZTest", (int)UnityEngine.Rendering.CompareFunction.Always);
                    mat.renderQueue = 5000;
                    rend.sharedMaterial = mat;
                }
            }
            return go;
        }

        private static void DisableNavAgents(GameObject go)
        {
            // A NavMeshAgent with no navmesh warps the root out from under the framing and has
            // nothing to do with the gear seat.
            foreach (var a in go.GetComponentsInChildren<UnityEngine.AI.NavMeshAgent>(true))
                if (a != null && a.enabled) a.enabled = false;
        }

        private static Transform FindByName(Transform root, string name)
        {
            if (root.name == name) return root;
            for (int i = 0; i < root.childCount; i++)
            {
                var hit = FindByName(root.GetChild(i), name);
                if (hit != null) return hit;
            }
            return null;
        }

        private static void MoveToLayer(Transform t, int layer, Dictionary<Transform, int> saved)
        {
            saved[t] = t.gameObject.layer;
            t.gameObject.layer = layer;
            for (int i = 0; i < t.childCount; i++) MoveToLayer(t.GetChild(i), layer, saved);
        }

        private static bool TryWorldBounds(GameObject go, out Bounds b)
        {
            b = default;
            bool any = false;
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            {
                if (r == null || !r.enabled) continue;
                if (!any) { b = r.bounds; any = true; }
                else b.Encapsulate(r.bounds);
            }
            return any;
        }

        private static string Nm(Transform t) => t != null ? t.name : "<null>";
        private static string V(Vector3 v) => $"({v.x:0.###},{v.y:0.###},{v.z:0.###})";

        private void SnapshotPrefs()
        {
            foreach (string k in KnightPrefKeys)
                _prefSnapshot[k] = PlayerPrefs.HasKey(k) ? PlayerPrefs.GetString(k, null) : null;
        }

        private void RestorePrefs()
        {
            foreach (var kv in _prefSnapshot)
            {
                if (kv.Value == null) PlayerPrefs.DeleteKey(kv.Key);
                else PlayerPrefs.SetString(kv.Key, kv.Value);
            }
            PlayerPrefs.Save();
        }

        private void Finish(bool ranToCompletion)
        {
            if (!ranToCompletion) _failures.Add("the driver did not run to completion.");
            if (!KnightGearProofCapture.MainSaveUnchanged())
                _failures.Add("the original main save changed during the isolated gear proof");
            else _log.AppendLine("SAVE ISOLATION: original main save unchanged; proof writes stayed in memory.");

            _log.AppendLine();
            _log.AppendLine(new string('-', 112));
            _log.AppendLine($"pngs={_written.Count} failures={_failures.Count}");
            foreach (var f in _failures) _log.AppendLine("  FAIL: " + f);

            string summary = Path.Combine(KnightGearProofCapture.OutDir, "_summary.txt").Replace('\\', '/');
            try { File.WriteAllText(summary, _log.ToString()); } catch { /* the console copy below still carries it */ }
            Debug.Log("[KnightGearProof]\n" + _log);

            if (_failures.Count > 0 || _written.Count < ExpectedPngs)
            {
                string reason = _failures.Count > 0
                    ? string.Join(" ; ", _failures)
                    : $"only {_written.Count} non-blank PNGs written, expected {ExpectedPngs}";
                // Deliberately Debug.Log, NOT Debug.LogError. This harness is meant to be RE-RUN
                // until the pictures and the numbers agree, and an error-level line in a headless
                // play session trips the owner's F8 passive listener and lands each iteration in
                // her triage queue (it did, seq 2543-2547, 2026-08-20). The MARKER STRING is the
                // verdict — the wrapper greps for KNIGHT_GEAR_PROOF_OK and fails closed without it,
                // so nothing is softened except the noise.
                Debug.Log($"KNIGHT_GEAR_PROOF_FAIL: {reason}");
            }
            else
            {
                Debug.Log($"KNIGHT_GEAR_PROOF_OK {_written.Count} file={string.Join(",", _written)}");
            }

            if (_cam != null) _cam.targetTexture = null;
            if (_rt != null) Object.DestroyImmediate(_rt);
            EditorApplication.Exit(0);
        }
    }
}

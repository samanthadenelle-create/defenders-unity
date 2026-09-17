// =============================================================================
// VfxProofCapture -- PROOF that a named VFX actually RENDERS, as a picture.
// -----------------------------------------------------------------------------
// Assembly: DeNelle.Editor   Namespace: DeNelle.Editor   (Editor-only)
//
// WHY THIS EXISTS
//   Compile-green never proved a particle system draws anything, and neither does
//   a scene screenshot: every effect in this game is spawned at RUNTIME by
//   VFXManager, so a still of an authored scene contains none of them. The owner's
//   acceptance criteria for the VFX batch is a PICTURE of each specified effect
//   rendering, indexed, and confirmed free of magenta / missing shaders / visual
//   defects. Nothing in the repo did that: VfxGalleryBuilder lays every catalogued
//   key out in a Play-mode scene, but writes no pixels and verifies nothing.
//
// WHY EDIT MODE, NOT PLAY MODE (this is the load-bearing design decision)
//   VFXManager self-creates via [RuntimeInitializeOnLoadMethod(AfterSceneLoad)]
//   (VFXManager.cs:81). That attribute does NOT fire in an editor -executeMethod
//   batch, so VFXManager.Instance is null here and every Play/PlayKey call is a
//   null-safe no-op. Entering Play mode from -executeMethod is also not headless-
//   reliable (UICaptureLaunch.cs:397-406 says so in as many words, and ships a
//   synchronous edit-mode renderer, RunCaptureHeadless, as the batch path).
//
//   So this harness does NOT drive VFXManager. It resolves the SAME prefab through
//   the SAME two catalogs VFXManager resolves through --
//       Resources/VFX/VFXCatalog      (VFXType   -> prefab, VFXManager.cs:112)
//       Resources/VFX/HovlVfxCatalog  (string key-> prefab, VFXManager.Hovl.cs:139)
//   -- instantiates it at the exact world offset the production call site passes,
//   applies the SAME URP legacy-shader proof VFXManager applies to every pooled
//   instance (VFXManager.cs:596 -> ProofUrpParticleShaders), simulates the particle
//   systems to a chosen time so the frame is DETERMINISTIC, and renders it.
//   Same prefab, same shader treatment, same anchor: the pixels are the pixels.
//
// THE THREE FAILURE MODES, ALL CHECKED (a magenta scan alone is NOT enough --
// a missing shader renders magenta, but it just as easily renders BLACK,
// UNTEXTURED WHITE, or nothing at all, and a magenta scan sails past all three)
//   1. MAGENTA   -- pixel count near Unity's error magenta (R>0.9, B>0.9, G<0.3),
//                   reported as an absolute count and a percentage.
//   2. NOT DRAWN -- the primary test is DIFFERENTIAL: the same camera renders the
//                   stage twice, once with the effect and once with it deactivated.
//                   If the two frames are effectively identical the effect
//                   contributed no pixels -- it did not render. That catches the
//                   invisible case AND the black-on-dark case, which a uniformity
//                   test on a lit stage would miss. A uniformity/variance measure
//                   of the effect frame is ALSO reported, so a wholly dead stage
//                   (nothing lit, nothing drawn) is caught even if the diff is
//                   degenerate.
//   3. SHADERS   -- every material on the instantiated effect is inspected for a
//                   null material, a null shader, or Hidden/InternalErrorShader,
//                   and reported BY NAME.
//
// LIGHTING
//   The stage builds and OWNS a single directional light parented to the stage
//   root, so it dies with the stage. Ambient is never touched and never relied on
//   -- an unlit stage renders every opaque subject black and would manufacture
//   false failures. (Tonight's arena-prefab leak of a scene-wide directional light
//   into dungeons is exactly the cost of a stage light that outlives its stage.)
//
// RUN
//   -executeMethod DeNelle.Editor.VfxProofCapture.Run
//   or menu: Defenders/VFX/Capture VFX Proof
// OUTPUT
//   Builds/vfx-proof/*.png  +  Builds/vfx-proof/INDEX.md (read the index first)
// MARKERS (both new -- neither token appears anywhere else in the tree)
//   VFX_PROOF_OK   <pass>/<total> shots     (Debug.Log,      all shots passed)
//   VFX_PROOF_FAIL <fail>/<total> shots     (Debug.LogError, ANY shot failed --
//                                            and NO success marker is emitted)
// =============================================================================

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using DeNelle.Core.Catalog;
using DeNelle.Core.Combat;
using DeNelle.Village;

namespace DeNelle.Editor
{
    /// <summary>
    /// Editor-only proof harness: stages each named VFX on a controlled, self-lit
    /// stage, simulates it to a fixed time, renders a PNG, and verifies the frame
    /// against the three ways a broken effect can look (magenta / not drawn /
    /// error shader). Writes an INDEX.md the owner reads first.
    /// </summary>
    public static class VfxProofCapture
    {
        // ---------------------------------------------------------------------
        //  Constants
        // ---------------------------------------------------------------------

        private const string DefaultOutDir = "Builds/vfx-proof";

        /// <summary>Where this run writes. A field, not a const, so the WO-1813 white-quad
        /// hunt (<see cref="RunWhiteQuadHunt"/>) can put its own evidence in its own folder
        /// without a second copy of the staging/render/verify machinery existing anywhere
        /// (CLAUDE.md §5/§16: the copy is the bug). Reset by every entry point.</summary>
        private static string OutDir = DefaultOutDir;

        // The Seeker's real surface. Every shot is taken at this size unless a row
        // says otherwise in the index (none do today -- every subject reads at full
        // width, because the stage camera already frames tight to the subject).
        private const int ShotW = 2670;
        private const int ShotH = 1200;

        // Unity's error magenta is (1,0,1). Band it loosely so a tinted/bloomed
        // error pixel still counts, but a legitimately violet arcane particle
        // (which carries real green) does not.
        private const float MagentaR = 0.90f;
        private const float MagentaB = 0.90f;
        private const float MagentaG = 0.30f;

        // A pixel counts as "changed by the effect" when any channel moves this far
        // from the baseline. 2/255 is above 8-bit rounding noise and well under any
        // visible contribution.
        private const float DiffEpsilon = 2f / 255f;

        // "NOT DRAWN" is judged INSIDE the effect's own screen footprint, not against
        // the whole 3.2-megapixel frame.
        //
        // WHY (coordinator's first run, 2026-08-06): the old test was a fraction of the
        // FULL frame, so shot 1 changed 468 px (0.015%) and FAILED while shot 2 changed
        // 789 px (0.025%) and PASSED. Both are the same muzzle flash a few pixels across.
        // A 0.01%-of-frame gap deciding pass/fail is measurement noise, not evidence: the
        // threshold was doing more work than the effect. A muzzle flash is SUPPOSED to be
        // small; what it must not be is absent.
        //
        // So the diff is now cropped to the effect's projected screen bounds (the ROI) and
        // judged two ways, whichever is larger:
        //   * an ABSOLUTE floor, because the real question is binary -- a drawn effect
        //     paints hundreds of pixels, an undrawn one paints zero. 200 px is roughly a
        //     14x14 blob: unmistakably present, and nowhere near 0.
        //   * a fraction of the ROI, which keeps a LARGE effect honest -- a fog volume
        //     that fills a 1500x800 footprint but paints 300 px has not really drawn.
        private const int   MinChangedAbsolute = 200;
        private const float MinChangedRoiFraction = 0.0025f;

        // Padding (fraction of each axis) added around the projected effect bounds, so a
        // particle that drifts a little past its reported bounds is still inside the ROI.
        private const float RoiPadding = 0.15f;

        // Below this luminance spread the WHOLE frame is flat -- nothing rendered
        // at all (dead stage), regardless of what the diff says.
        private const float MinFrameSpread = 0.02f;

        // Structure fit-to-height base, mirroring StructureFactory.YHeightVariable
        // (StructureFactory.cs:59). Read as a local const rather than the field so a
        // change there is visible here as a mismatch instead of silently re-scaling
        // proof shots -- the shots are evidence, not gameplay.
        private const float StructureHeightBase = 4f;

        private const string CatalogJsonPath = "Assets/Resources/Data/Canonical/structures-catalog.json";

        // ---------------------------------------------------------------------
        //  Shot model
        // ---------------------------------------------------------------------

        /// <summary>One VFX instance to stage: either a VFXType (VFXCatalog path) or
        /// a string key (HovlVfxCatalog path), at an offset from the subject origin.</summary>
        private sealed class Layer
        {
            public VFXType Type = VFXType.None;   // VFXCatalog path when != None
            public string  Key;                   // HovlVfxCatalog path when non-null
            public Vector3 Offset;                // world offset from the stage origin
            public float   Scale = 1f;            // uniform scale the call site passes
            public string  Why;                   // source citation for the index

            public string Label => Type != VFXType.None ? ("VFXType." + Type) : ("key:" + Key);
        }

        /// <summary>One PNG: a subject prop (optional) plus the VFX layers the
        /// production code plays on it, simulated to <see cref="SimTime"/>.</summary>
        private sealed class Shot
        {
            public string  FileName;
            public string  Subject;
            public string  Level = "-";
            public string  SubjectResourcePath;   // Resources path of the context prop (may be null)
            public float   SubjectHeight;         // fit-to-height for the prop (0 = leave authored)
            public Vector3 SubjectEuler;          // orientation correction for the prop
            public float   SimTime = 0.2f;
            public string  SimWhy = "";
            public List<Layer> Layers = new List<Layer>();
            public string  Notes = "";

            /// <summary>WO-1813: pin the camera instead of auto-framing. The owner's evidence
            /// frame is a fixed third-person seat (~3.5 m back, 60° vfov, 2670x1200), and
            /// FrameCamera's auto-fit changes distance per subject — which makes a measured
            /// "the quad is ~455 px / ~1.5 m" comparison impossible across shots. With this
            /// set, every shot is taken from the SAME seat, so pixel sizes are comparable to
            /// the owner's Screenshot_20260916-205131.png directly.</summary>
            /// <summary>WO-1813: stage the prefab exactly as authored, with NO runtime repair
            /// pass. That is the BEFORE frame — without it there is no way to show that the
            /// repair changed anything, and "it looks fine now" is not evidence.</summary>
            public bool    SkipRepair;

            public bool    FixedCamera;
            public Vector3 CamPos    = new Vector3(0f, 1.40f, -3.50f);
            public Vector3 CamLookAt = new Vector3(0f, 1.00f, 0f);
            public float   CamFov    = 60f;
        }

        /// <summary>Per-shot verdict rows for INDEX.md.</summary>
        private sealed class Result
        {
            public Shot   Shot;
            public bool   Pass;
            public string Png = "(none)";
            public long   MagentaCount;
            public float  MagentaPct;
            public string Uniformity = "-";
            public string ShaderVerdict = "-";
            public string Reason = "";
            public float  SimUsed;
            public string Played = "";
            public int    TrailSlotsSkipped;
            /// <summary>Content hash of the written PNG. Two shots with the same
            /// fingerprint rendered the SAME picture -- which for a per-level ladder is
            /// a finding about the game, not a harness fault, so it is surfaced rather
            /// than left to be noticed.</summary>
            public string Fingerprint = "-";
            /// <summary>Every simulate time tried, with what each contributed. Makes the
            /// retry ladder auditable instead of asking the reader to trust it.</summary>
            public string Ladder = "-";
        }

        // Informational rows: things the shot list names that are DELIBERATELY not
        // wired yet. They are not failures and they get no PNG -- a stated missing
        // row is honest; a mislabelled PNG is worse than nothing.
        private static readonly List<string> _heldNotes = new List<string>();

        private static VFXCatalog     _vfxCatalog;
        private static HovlVfxCatalog _hovlCatalog;
        private static readonly Dictionary<string, CatalogEntry> _entries =
            new Dictionary<string, CatalogEntry>(StringComparer.Ordinal);

        // ---------------------------------------------------------------------
        //  Entry point
        // ---------------------------------------------------------------------

        [MenuItem("Defenders/VFX/Capture VFX Proof")]
        public static void Run()
        {
            OutDir = DefaultOutDir;
            Execute(null, "VFX_PROOF_OK", "VFX_PROOF_FAIL");
        }

        // =====================================================================
        //  WO-1813 -- THE WHITE-QUAD HUNT
        // ---------------------------------------------------------------------
        // The owner's frame (logs/device/owner-fireball-20260916/
        // Screenshot_20260916-205131.png) shows three overlapping, razor-sharp,
        // screen-axis-aligned, fully occluding warm-white rectangles ~1.5 m across on
        // the hero. A screenshot cannot say WHICH renderer drew them, and the previous
        // lane closed with three unfalsifiable candidates for exactly that reason.
        //
        // This entry point stages each effect that was ALIVE in that window, one per
        // frame, ALONE, from the SAME catalog the game resolves, from a FIXED camera at
        // the owner's own seat (3.5 m back, 1.4 m up, 60 deg vfov, 2670x1200) so a quad
        // measured here is directly comparable in pixels to the quad she photographed.
        // Every effect is shot TWICE -- once exactly as authored (SkipRepair), once
        // through the runtime repair -- so the pair is the evidence, not an assertion.
        //
        // Judge by the PNGs, then by the markers:
        //   VFX_WHITEQUAD_OK / VFX_WHITEQUAD_FAIL   (distinct tokens, CLAUDE.md section 8)
        // Output: Builds/vfx-whitequad/*.png + INDEX.md
        // =====================================================================
        [MenuItem("Defenders/VFX/Capture White-Quad Hunt (WO-1813)")]
        public static void RunWhiteQuadHunt()
        {
            OutDir = "Builds/vfx-whitequad";
            Execute(BuildWhiteQuadShotList(), "VFX_WHITEQUAD_OK", "VFX_WHITEQUAD_FAIL");
        }

        /// <summary>Shared run body. <paramref name="preplanned"/> null = the standard shot list.</summary>
        private static void Execute(List<Shot> preplanned, string okMarker, string failMarker)
        {
            var results = new List<Result>();
            _heldNotes.Clear();

            try
            {
                Directory.CreateDirectory(OutDir);

                Debug.Log("[VfxProof] start (batchmode=" + Application.isBatchMode +
                          ", graphicsDevice=" + SystemInfo.graphicsDeviceType +
                          ", out=" + Path.GetFullPath(OutDir) + ")");

                if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
                {
                    // -nographics produces blank frames. Say so LOUD rather than let a
                    // reviewer read a wall of "not drawn" as a VFX regression.
                    Debug.LogError("[VfxProof] NO graphics device (-nographics). Every frame will be " +
                                   "blank and every shot will read NOT DRAWN. Re-run WITHOUT -nographics.");
                }

                LoadCatalogs();
                LoadStructureEntries();

                var shots = preplanned ?? BuildShotList();
                Debug.Log("[VfxProof] " + shots.Count + " shot(s) planned.");

                foreach (var shot in shots)
                    results.Add(CaptureShot(shot));

                WriteIndex(results);
            }
            catch (Exception e)
            {
                Debug.LogError("[VfxProof] run threw: " + e);
                // A throw is a failed run. Fall through so the FAIL marker still prints
                // and no caller can read the absence of an error as a pass.
                results.Add(new Result
                {
                    Shot = new Shot { FileName = "(run)", Subject = "harness run" },
                    Pass = false,
                    Reason = "harness threw: " + e.Message,
                });
            }

            int total = results.Count;
            int pass = 0;
            foreach (var r in results) if (r.Pass) pass++;
            int fail = total - pass;

            foreach (var r in results)
            {
                if (r.Pass) continue;
                Debug.LogError("[VfxProof] FAIL " + r.Shot.Subject + " (" + r.Shot.Level + ") -- " + r.Reason);
            }

            // Two DISTINCT markers, one per outcome (CLAUDE.md section 8: never share a
            // token between entry points -- that is how a partial pass once read as a
            // full pass). On ANY failure the success marker is withheld entirely.
            if (fail > 0)
                Debug.LogError(failMarker + " " + fail + "/" + total + " shots -- see " +
                               Path.GetFullPath(Path.Combine(OutDir, "INDEX.md")));
            else
                Debug.Log(okMarker + " " + pass + "/" + total + " shots -- see " +
                          Path.GetFullPath(Path.Combine(OutDir, "INDEX.md")));
        }

        /// <summary>
        /// WO-1813: one BEFORE (as authored) and one AFTER (runtime repair) shot for every
        /// effect that was alive in the owner's 2026-09-16 20:49-20:52 window, plus the
        /// top-of-inventory keys by play count. All from the same fixed seat.
        /// Names are the catalog's own: a VFXType where the catalog holds one, a Hovl key
        /// otherwise -- checked against both catalogs before this list was written, so a
        /// typo shows up as "no row for", never as a silently missing shot.
        /// </summary>
        private static List<Shot> BuildWhiteQuadShotList()
        {
            // (label, VFXType name or null, Hovl key or null, why it is in the hunt)
            var subjects = new (string Name, VFXType Type, string Key, string Why)[]
            {
                // ---- At the hero in the capture window (ranked by proximity) ----
                ("PP_FleshImpacts",       VFXType.None,                   "PP_FleshImpacts",
                 "20:51:29.429 at the hero's EXACT position (0.20,1.08,-4.15), lifetime 8.30 s -- still alive at 20:51:31"),
                ("Juice_LevelUp",         VFXType.Juice_LevelUp,          null,
                 "20:51:30.481 at the hero, lifetime 5.00 s"),
                ("Death_Brute",           VFXType.Death_Brute,            null,
                 "20:51:30.478 at the troll's death spot (2.20,0.84,-2.80)"),
                ("Impact_Physical",       VFXType.Impact_Physical,        null,
                 "20:51:31.116 at the same death spot"),
                ("Cast_FireCharge",       VFXType.Cast_FireCharge,        null,
                 "the fireball wind-up, 11 plays across both windows"),
                ("Impact_Flame",          VFXType.Impact_Flame,           null,
                 "the fireball detonation, 16 plays"),
                // ---- Named in the WO-1813 sweep ----
                ("SimpleCast_Cast",       VFXType.None,                   "SimpleCast_Cast",       "arcane tower muzzle"),
                ("Spear_Impact",          VFXType.None,                   "Spear_Impact",          "4 plays (raid)"),
                ("PP_MuzzleFlash",        VFXType.None,                   "PP_MuzzleFlash",        "4 plays (raid)"),
                ("ArcherTowerLevel2_Projectile", VFXType.None,            "ArcherTowerLevel2_Projectile", "1 play"),
                ("PP_PlasmaExplosionEffect", VFXType.None,                "PP_PlasmaExplosionEffect", "aether tower impact"),
                ("Explosion_Arcane",      VFXType.Impact_ExplosionAether, null,
                 "22 plays -- the single most-played key in the inventory"),
                ("Projectile_Arcane",     VFXType.Projectile_ArcaneBolt,  null, "enemy caster orb"),
                // ---- Remainder of the top-10 by play count ----
                ("Cast_MuzzleFlash",      VFXType.Cast_MuzzleFlash,       null, "13 plays"),
                ("Death_Skeleton",        VFXType.Death_Skeleton,         null, "6 plays"),
                ("Env_DestructionDust",   VFXType.Env_DestructionDust,    null, "5 plays"),
                ("Impact_Aether",         VFXType.Impact_Aether,          null, "5 plays"),
                ("Lightningspellmaybe_Cast", VFXType.None,                "Lightningspellmaybe_Cast", "4 plays"),
                ("lighteningOnSpellLand_Impact", VFXType.None,            "lighteningOnSpellLand_Impact", "4 plays"),
                ("ArcherTower_Projectile", VFXType.None,                  "ArcherTower_Projectile", "4 plays"),
                ("Poi_NodeAura",          VFXType.None,                   "Poi_NodeAura",          "4 plays"),
            };

            var shots = new List<Shot>(subjects.Length * 2);
            foreach (var s in subjects)
            {
                for (int pass = 0; pass < 2; pass++)
                {
                    bool before = pass == 0;
                    var shot = new Shot
                    {
                        FileName    = s.Name + (before ? "__BEFORE" : "__AFTER"),
                        Subject     = s.Name + (before ? " (as authored)" : " (runtime repair applied)"),
                        Level       = before ? "before" : "after",
                        SkipRepair  = before,
                        FixedCamera = true,
                        // ~1 s after spawn: the owner's frame is 1.6 s into PP_FleshImpacts and
                        // ~1 s into Juice_LevelUp, so this is the moment she actually saw.
                        SimTime     = 1.0f,
                        SimWhy      = "1.0 s -- the offset of the owner's 20:51:31 frame from the spawns at 20:51:29-30",
                        Notes       = s.Why,
                    };
                    shot.Layers.Add(new Layer
                    {
                        Type   = s.Type,
                        Key    = s.Key,
                        Offset = new Vector3(0f, 1.0f, 0f),   // chest height, where the hero effects sit
                        Scale  = 1f,
                        Why    = s.Why,
                    });
                    shots.Add(shot);
                }
            }
            return shots;
        }

        // ---------------------------------------------------------------------
        //  Catalog loading -- the SAME two catalogs VFXManager loads
        // ---------------------------------------------------------------------

        private static void LoadCatalogs()
        {
            // VFXManager.EnsureCatalog (VFXManager.cs) and EnsureHovlCatalog (VFXManager.Hovl.cs)
            // resolve these exact keys through VfxAssetLoader — so this harness MUST use the same
            // seam, or it stops seeing what the game sees, which is its entire purpose.
            //
            // ⚠ This was a raw Resources.Load and that made it a migration landmine: Resources/VFX
            // is moving to Addressables, and the catalogs MUST move (they hold hard GUID refs to
            // every effect prefab — left behind, they would drag all of them back into the
            // force-included block and the migration would win zero bytes). The moment they moved,
            // both loads here would have returned null and every VFX proof shot would have gone
            // silently blank — evidence loss with no failing gate.
            _vfxCatalog  = DeNelle.Core.VfxAssetLoader.LoadVfxAsset<VFXCatalog>("VFX/VFXCatalog");
            _hovlCatalog = DeNelle.Core.VfxAssetLoader.LoadVfxAsset<HovlVfxCatalog>("VFX/HovlVfxCatalog");

            if (_vfxCatalog == null)
                Debug.LogError("[VfxProof] Resources/VFX/VFXCatalog NOT FOUND -- every VFXType shot " +
                               "will fail to resolve. Run DeNelle.Editor.VFXCatalogGenerator.Generate.");
            else
                _vfxCatalog.BuildLookup();

            if (_hovlCatalog == null)
                Debug.LogError("[VfxProof] Resources/VFX/HovlVfxCatalog NOT FOUND -- every string-key " +
                               "shot will fail to resolve. Run Defenders/VFX/Generate Hovl VFX Catalog.");
            else
                _hovlCatalog.BuildLookup();
        }

        /// <summary>
        /// Parse structures-catalog.json into CatalogEntry with the SAME serializer
        /// settings CatalogBootstrap.LoadFromJson uses (StringEnumConverter so
        /// "Aether"/"Tower" parse; NullValueHandling.Ignore so a sparse row keeps its
        /// defaults). CatalogBootstrap.Register is private + RuntimeInitialize-gated,
        /// so it cannot be called from an editor batch -- this is the same parse, not
        /// a second source of truth.
        /// </summary>
        private static void LoadStructureEntries()
        {
            _entries.Clear();
            if (!File.Exists(CatalogJsonPath))
            {
                Debug.LogError("[VfxProof] " + CatalogJsonPath + " not found -- tower subject props " +
                               "cannot be staged (the VFX layers still can).");
                return;
            }

            var settings = new JsonSerializerSettings
            {
                Converters = { new StringEnumConverter() },
                NullValueHandling = NullValueHandling.Ignore,
                MissingMemberHandling = MissingMemberHandling.Ignore,
            };
            var file = JsonConvert.DeserializeObject<CatalogFile>(File.ReadAllText(CatalogJsonPath), settings);
            if (file == null || file.Entries == null) return;

            foreach (var e in file.Entries)
            {
                if (e == null || string.IsNullOrEmpty(e.id)) continue;
                if (e.repo == null) e.repo = new RepoProps();
                _entries[e.id] = e;
            }
            Debug.Log("[VfxProof] parsed " + _entries.Count + " catalog entrie(s) for subject props.");
        }

        [Serializable]
        private sealed class CatalogFile
        {
            [JsonProperty("entries")] public List<CatalogEntry> Entries = new List<CatalogEntry>();
        }

        // ---------------------------------------------------------------------
        //  THE SHOT LIST
        // ---------------------------------------------------------------------

        private static List<Shot> BuildShotList()
        {
            var shots = new List<Shot>();

            // === 1..15  Each tower type at each of its 3 levels, FIRING ==========
            //
            // VERIFIED AT SOURCE. The five catalog towers all carry
            // repo.behaviorId "DefenseTower" (or "ArcaneTower" for the spire), so
            // StructureFactory.AttachBehaviorImpl (StructureFactory.cs:693/723)
            // attaches DefenseTower / ArcaneTower -- NOT the older Tower+TowerCombat
            // pair. DefenseTower.Fire (DefenseTower.cs:796) is the live fire path:
            //
            //   muzzle = transform.position + Vector3.up * 2f          (:802)
            //   SpawnProjectileVisual(...) -> PlayKey(projKey, muzzle, ...)  (:952)
            //   PlayFireVfx(muzzle, ...)                               (:816)
            //     Spell style : Play(VFXType.Cast_MageCharge, muzzle)  (:1064)
            //                 + PlayKey(CastKeyFor(Element), muzzle)   (:1065)
            //     otherwise   : Play(MuzzleVfxFor(Element), muzzle)    (:1068)
            //                 + PlayKey(CastKeyFor(Element), muzzle)   (:1069)
            //
            // WHAT ACTUALLY VARIES BY LEVEL -- state it plainly, because 15 shots
            // that look alike read as a harness bug when they are not:
            //   * the tower MODEL changes per level ONLY where the catalog authors
            //     repo.upgradeVisualPath (archer + arcane spire do; ballista,
            //     catapult and sky ballista do NOT -- one model at all three levels).
            //   * the PROJECTILE key changes per level ONLY for the ground archer
            //     (DefenseTower.cs:1156-1161, tier 1/2/3 arrows). Every other tower
            //     resolves one key for all tiers.
            //   * the MUZZLE / CAST layer is level-INVARIANT on DefenseTower. The
            //     level-scaled muzzle (TierVfxScale 1.0/1.3/1.7) lives on TowerCombat
            //     (TowerCombat.cs:392-397), which no catalog tower attaches.
            // So a level-to-level diff on rows 1-15 is expected to be the model and,
            // for the archer, the arrow. That is what the code does.

            AddTowerShots(shots, "tower_ground_archer", "Archer Tower");
            AddTowerShots(shots, "tower_arcane_spire", "Arcane Spire");
            AddTowerShots(shots, "tower_catapult", "Catapult");
            AddTowerShots(shots, "tower_siege_tower", "Sky Ballista (Anti-Air)");
            AddTowerShots(shots, "tower_ballista", "Ballista");

            // === 16  A building on fire -- the LIVE path ========================
            // StructureBurn is the one owner of a burning structure (StructureBurn.cs
            // header). StartFireVfx plays the owner-tagged loop
            //   PlayKey(_fireVfxKey = "BurningStructure_Aura", anchor)   (:77, :287)
            // on an anchor at localPosition (0, _fireVfxYOffset = 0.6, 0)  (:80, :340).
            // The ignite flare "BurningStructure_Impact" at scale 1.3 (:81, :316) is
            // layered in as well, because ignition is the moment the owner asked for.
            shots.Add(new Shot
            {
                FileName = "16_building_on_fire",
                Subject = "Building on fire (StructureBurn, live path)",
                SubjectResourcePath = ResourcePathFor("armorer"),
                SubjectHeight = StructureHeightBase * HeightMulFor("armorer"),
                SimTime = 1.5f,
                SimWhy = "1.50 s -- a fire LOOP, not a burst: the column needs about a second " +
                         "to reach steady state. At t=0 it is a single spawn ring and proves nothing.",
                Layers =
                {
                    new Layer { Key = "BurningStructure_Aura", Offset = new Vector3(0f, 0.6f, 0f), Scale = 1f,
                                Why = "StructureBurn.cs:77 + :287 + :340 (anchor y = _fireVfxYOffset 0.6)" },
                    new Layer { Key = "BurningStructure_Impact", Offset = new Vector3(0f, 0.6f, 0f), Scale = 1.3f,
                                Why = "StructureBurn.cs:81 + :316 (ignite flare, scale 1.3)" },
                },
                Notes = "Subject prop is the 'armorer' catalog visual (Structures/House_Medieval_Medium).",
            });

            // === 17  A building on fire -- the SECOND, DEAD path (DIAGNOSTIC) ====
            // StructureDamageVisuals.cs:508 plays PlayKey("Ember_Burn", ...) for its
            // smolder/fire damage tell. There is NO "Ember_Burn" row in
            // Resources/VFX/HovlVfxCatalog.asset (135 rows; the generator authors it at
            // HovlVfxCatalogGenerator.cs:175 but the shipped asset does not carry it).
            // PlayKey therefore no-ops on the hovl-nokey path (VFXManager.Hovl.cs:205)
            // and that damage tell renders NOTHING in the shipped game.
            // This row is staged against the key the code ACTUALLY plays. It is
            // expected to FAIL, and that failure is the finding -- not a harness fault.
            shots.Add(new Shot
            {
                FileName = "17_building_on_fire_damagevisuals",
                Subject = "Building on fire (StructureDamageVisuals damage tell) -- DIAGNOSTIC",
                SubjectResourcePath = ResourcePathFor("armorer"),
                SubjectHeight = StructureHeightBase * HeightMulFor("armorer"),
                SimTime = 1.5f,
                SimWhy = "1.50 s -- same loop framing as row 16, so the two are directly comparable.",
                Layers =
                {
                    new Layer { Key = "Ember_Burn", Offset = new Vector3(0f, 0.6f, 0f), Scale = 1f,
                                Why = "StructureDamageVisuals.cs:508 (scale 0.55 smolder / 1.0 fire)" },
                },
                Notes = "Expected to fail: no Ember_Burn row in HovlVfxCatalog. Not substituted.",
            });

            // === 18  An enemy dying ============================================
            // Enemy.Die -> deathPos = transform.position + Vector3.up * 0.5f (:2557).
            // With no per-prefab _deathVFXOverride and no EnemyVfxSet death prefab,
            // SpeciesDeathVfx (:2770) maps family "hollow" -> VFXType.Death_Skeleton
            // (:2784) and Play()s it at deathPos (:2601). WO-886 repointed the whole
            // Death_* ladder; Death_Skeleton carries a prefab in VFXCatalog.asset.
            shots.Add(new Shot
            {
                FileName = "18_enemy_death",
                Subject = "Enemy death burst",
                SubjectResourcePath = "Enemies/Blink/Blink_Orc_Warrior",
                SimTime = 0.35f,
                SimWhy = "0.35 s -- a death BURST emits its whole payload at t=0; by 0.35 s the shards " +
                         "and smoke have expanded to full read but nothing has faded out yet.",
                Layers =
                {
                    new Layer { Type = VFXType.Death_Skeleton, Offset = new Vector3(0f, 0.5f, 0f),
                                Why = "Enemy.cs:2557 deathPos (+0.5 y) + :2784 family hollow -> Death_Skeleton" },
                },
            });

            // === 19  An enemy casting ==========================================
            // Enemy ranged release (:1800-1802):
            //   PlayKey(castKey, pos + up*1.2 + forward*0.6, rotation, null, castTint)
            // castKey falls back to DefaultCastVfxKey = "Fire_Cast" (:1708) when the
            // enemy carries no EnemyVfxSet cast key -- and EnemyVfxSet_Default.asset
            // authors none, so "Fire_Cast" is what the shipped enemies play.
            shots.Add(new Shot
            {
                FileName = "19_enemy_cast",
                Subject = "Enemy cast (ranged release muzzle)",
                SubjectResourcePath = "Enemies/Blink/Blink_Orc_Warlock",
                SimTime = 0.18f,
                SimWhy = "0.18 s -- a release FLASH. It peaks in the first fifth of a second; " +
                         "later than that and the flash has already collapsed.",
                Layers =
                {
                    new Layer { Key = "Fire_Cast", Offset = new Vector3(0f, 1.2f, 0.6f),
                                Why = "Enemy.cs:1708 DefaultCastVfxKey + :1800-1802 (up 1.2, forward 0.6)" },
                },
                Notes = "Tint (1, 0.55, 0.15) is NOT applied -- see the tint note under the table.",
            });

            // === 20  The aura around the portals ================================
            // DungeonWorldPortalSpawner.AttachGateVfx:
            //   PlayKey("PP_GroundFog", root + up*0.05, identity, root, GateTint, 3.4f)
            // (:659 key, :660 scale, :670-673 call). This is the soft mist aura that
            // spills out of the portal base -- the live "aura around the portals".
            shots.Add(new Shot
            {
                FileName = "20_portal_aura",
                Subject = "Portal aura (dungeon portal gate mist)",
                SubjectResourcePath = null,   // arch is built in code at runtime -- see Notes
                SimTime = 2.5f,
                SimWhy = "2.50 s -- a slow, wide ground mist. It is the slowest effect in the list; " +
                         "under about two seconds the volume has not filled and the shot understates it.",
                Layers =
                {
                    new Layer { Key = "PP_GroundFog", Offset = new Vector3(0f, 0.05f, 0f), Scale = 3.4f,
                                Why = "DungeonWorldPortalSpawner.cs:659 key, :660 scale 3.4, :670 pos +0.05 y" },
                },
                Notes = "No arch prop: the portal arch is BUILT IN CODE by the spawner at runtime, " +
                        "so there is no prefab to stage. The shot is the aura itself over a ground plane.",
            });

            // Held-by-design, stated rather than faked.
            _heldNotes.Add(
                "`Portal_Threshold_Aura` (DungeonWorldPortalSpawner.cs:702) -- the WO-869 threshold " +
                "aura seam is wired but the prefab pick is deliberately HELD for the owner's tag " +
                "(the file says so at :685-700). It has no HovlVfxCatalog row, so PlayKey is a clean " +
                "no-op. NOT captured and NOT counted as a failure: it is an owner decision, not a defect.");
            _heldNotes.Add(
                "`VFXType.Projectile_TowerArcane` -- DefenseTower.MuzzleVfxFor returns this for every " +
                "non-Flame/Ice tower (DefenseTower.cs:1091), but it has NO row in VFXCatalog.asset, so " +
                "PlayOneshot falls through to the PROCEDURAL AbilityVfxKit path (VFXManager.cs:469). " +
                "That layer is therefore absent from the tower shots; the Hovl cast key carries the " +
                "muzzle read. Reported, not substituted.");

            return shots;
        }

        private static void AddTowerShots(List<Shot> shots, string catalogId, string displayName)
        {
            _entries.TryGetValue(catalogId, out var entry);
            var repo = entry != null ? entry.repo : null;

            DamageElement element = repo != null ? repo.element : DamageElement.None;
            string style = repo != null ? repo.projectileStyle : null;
            bool airOnly = repo != null && repo.airOnly;
            float heightMul = HeightMulFor(catalogId);

            bool spell = string.Equals((style ?? string.Empty).Trim(), "spell",
                                       StringComparison.OrdinalIgnoreCase);

            for (int level = 1; level <= 3; level++)
            {
                var shot = new Shot
                {
                    FileName = string.Format(CultureInfo.InvariantCulture,
                                             "{0:00}_{1}_L{2}_firing",
                                             shots.Count + 1, catalogId, level),
                    Subject = displayName + " firing",
                    Level = "L" + level,
                    SubjectResourcePath = VisualPathForLevel(entry, level),
                    SubjectHeight = StructureHeightBase * heightMul,
                    SubjectEuler = ManualEulerFor(entry, level),
                    SimTime = 0.12f,
                    SimWhy = "0.12 s -- the moment of fire. A muzzle flash is a sub-quarter-second " +
                             "burst; at t=0 it is one particle and by 0.3 s it is gone.",
                };

                // Muzzle / cast layer, exactly as PlayFireVfx picks it.
                if (spell)
                {
                    shot.Layers.Add(new Layer
                    {
                        Type = VFXType.Cast_MageCharge,
                        Offset = new Vector3(0f, 2f, 0f),
                        Why = "DefenseTower.cs:1064 (spell style) at muzzle = root + up*2 (:802)",
                    });
                }
                else
                {
                    // MuzzleVfxFor(None/Aether) -> Projectile_TowerArcane, which has NO
                    // VFXCatalog row. Staging it anyway would guarantee a false FAIL on
                    // every bolt/pellet tower, so the absence is reported once as a held
                    // note above and the layer is not added. The Hovl cast key below IS
                    // the layer that renders.
                }

                shot.Layers.Add(new Layer
                {
                    Key = CastKeyFor(element),
                    Offset = new Vector3(0f, 2f, 0f),
                    Why = "DefenseTower.cs:" + (spell ? "1065" : "1069") + " CastKeyFor(" + element + ")",
                });

                shot.Layers.Add(new Layer
                {
                    Key = ProjectileKeyFor(catalogId, element, style, airOnly, level),
                    Offset = new Vector3(0f, 2f, 0f),
                    Why = "DefenseTower.cs:930 + :952 ProjectileKeyFor(tier " + level + ") at the muzzle",
                });

                shot.Notes = "element=" + element + ", style=" + (string.IsNullOrEmpty(style) ? "pellet" : style) +
                             (airOnly ? ", airOnly" : "");

                shots.Add(shot);
            }
        }

        // -- mirrors of the production key pickers (verbatim, cited) ------------

        /// <summary>DefenseTower.cs:1099-1108, verbatim.</summary>
        private static string CastKeyFor(DamageElement element)
        {
            switch (element)
            {
                case DamageElement.Flame:  return "Fire_Cast";
                case DamageElement.Aether: return "SimpleCast_Cast";
                case DamageElement.Ice:    return "Freezing_Projectile";
                default:                   return "PP_MuzzleFlash";
            }
        }

        /// <summary>
        /// DefenseTower.ProjectileKeyFor (:1126-1181) + OwnerTaggedProjectileKey
        /// (:1196-1204), verbatim. Tier is the tower level.
        /// </summary>
        private static string ProjectileKeyFor(string catalogId, DamageElement element,
                                               string style, bool airOnly, int tier)
        {
            if (string.Equals(catalogId, "tower_ballista", StringComparison.Ordinal)
                || string.Equals(catalogId, "tower_wall_wizard", StringComparison.Ordinal))
                return "SimpleCast_Projectile";   // owner tag, :1201

            string s = (style ?? string.Empty).Trim().ToLowerInvariant();
            bool isBolt = s == "bolt";
            bool isSpell = s == "spell";

            if (isBolt || airOnly)
            {
                switch (element)
                {
                    case DamageElement.Flame:  return "ArcherTower-Fire_Projectile";
                    case DamageElement.Ice:    return "ArcherTower-Ice_Projectile";
                    case DamageElement.Aether: return "RangerTowerUpgraded_Projectile";
                    default:
                        if (airOnly) return "RangerTowerBaseProjectile_Projectile";
                        switch (tier)
                        {
                            case 1:  return "ArcherTowerLevel1_Projectile";
                            case 2:  return "ArcherTowerLevel2_Projectile";
                            default: return "ArcherTower_Projectile";
                        }
                }
            }
            if (isSpell)
            {
                switch (element)
                {
                    case DamageElement.Flame: return "FireballTower_Projectile";
                    case DamageElement.Ice:   return "icebasedprojectile_Projectile";
                    default:                  return "ARcaneTower_Projectile";
                }
            }
            switch (element)   // pellet fallback
            {
                case DamageElement.Flame:  return "FireballTower_Projectile";
                case DamageElement.Ice:    return "ArcherTower-Ice_Projectile";
                case DamageElement.Aether: return "ARcaneTower_Projectile";
                default:                   return "ArcherTower_Projectile";
            }
        }

        /// <summary>StructureFactory.VisualPathForLevel (:267-275), verbatim.</summary>
        private static string VisualPathForLevel(CatalogEntry entry, int level)
        {
            if (entry == null) return null;
            var ladder = entry.repo != null ? entry.repo.upgradeVisualPath : null;
            if (level >= 2 && ladder != null && ladder.Length >= level - 1
                && !string.IsNullOrEmpty(ladder[level - 2]))
                return ladder[level - 2];
            return entry.visualPrefabPath;
        }

        private static float HeightMulFor(string catalogId)
        {
            if (_entries.TryGetValue(catalogId, out var e) && e.repo != null && e.repo.heightMul > 0f)
                return e.repo.heightMul;
            return 1f;
        }

        private static string ResourcePathFor(string catalogId)
            => _entries.TryGetValue(catalogId, out var e) ? e.visualPrefabPath : null;

        /// <summary>
        /// The manual orientation correction StructureFactory applies (StructureFactory.cs:145-152):
        /// ONLY when orientation.manual is true. And ONLY at level 1 -- ReskinForLevel
        /// skips base-authored orientation for an upgraded model (the catalog's own
        /// tower_wall_wizard note says exactly that), so applying it to an L2/L3 mesh
        /// would tip a good model over.
        /// </summary>
        private static Vector3 ManualEulerFor(CatalogEntry entry, int level)
        {
            if (level != 1) return Vector3.zero;
            if (entry == null || entry.orientation == null || !entry.orientation.manual) return Vector3.zero;
            return entry.orientation.Euler;
        }

        // ---------------------------------------------------------------------
        //  Capture one shot
        // ---------------------------------------------------------------------

        private static Result CaptureShot(Shot shot)
        {
            var result = new Result { Shot = shot, SimUsed = shot.SimTime };

            GameObject stage = null;
            RenderTexture rt = null;
            Texture2D withFx = null;
            Texture2D without = null;
            RenderTexture prevActive = RenderTexture.active;

            try
            {
                stage = new GameObject("~VfxProofStage");
                stage.hideFlags = HideFlags.DontSave;

                // -- OWN LIGHT, scoped to the stage. Parented to the stage root so it
                // is destroyed with it. Never ambient: an unlit stage renders every
                // opaque subject black, which manufactures false failures -- and a
                // light that outlives its stage leaks into whatever loads next.
                var lightGo = new GameObject("~VfxProofKeyLight");
                lightGo.transform.SetParent(stage.transform, false);
                var keyLight = lightGo.AddComponent<Light>();
                keyLight.type = LightType.Directional;
                keyLight.color = new Color(1f, 0.97f, 0.92f);
                keyLight.intensity = 1.35f;
                keyLight.shadows = LightShadows.None;   // no shadow map needed; keeps the batch cheap
                lightGo.transform.rotation = Quaternion.Euler(48f, 35f, 0f);

                // -- Subject prop (context only). Missing prop is NOT a failure: the
                // shot is proof of the EFFECT, and the effect is staged either way.
                var subjectNotes = new StringBuilder();
                GameObject subject = null;
                if (!string.IsNullOrEmpty(shot.SubjectResourcePath))
                    subject = StageSubject(stage.transform, shot, subjectNotes);
                else
                    StageGroundPlane(stage.transform);

                // -- Effect layers, resolved through the real catalogs.
                var fxRoots = new List<GameObject>();
                var played = new List<string>();
                var layerProblems = new List<string>();
                var shaderProblems = new List<string>();

                foreach (var layer in shot.Layers)
                {
                    GameObject prefab = ResolveLayerPrefab(layer, out string resolveNote, out float catalogScale);
                    if (prefab == null)
                    {
                        layerProblems.Add(layer.Label + ": " + resolveNote);
                        continue;
                    }

                    var inst = UnityEngine.Object.Instantiate(prefab, stage.transform);
                    inst.name = "~fx_" + layer.Label;
                    inst.transform.localPosition = layer.Offset;
                    inst.transform.localRotation = Quaternion.identity;
                    float s = layer.Scale > 0f ? layer.Scale : (catalogScale > 0f ? catalogScale : 1f);
                    inst.transform.localScale = Vector3.one * s;
                    inst.SetActive(true);

                    // The VFXType path is the one VFXManager re-shades on every pooled
                    // instance (VFXManager.cs:596). Mirror it here or a legacy Lana/Spells
                    // prefab renders magenta in the proof and clean in the game -- a lie in
                    // the other direction. The Hovl path is deliberately NOT re-shaded:
                    // VFXManager.Hovl.cs:360 says the Hovl packs ship URP-clean, so touching
                    // them here would diverge from what ships.
                    if (!shot.SkipRepair)
                    {
                        if (layer.Type != VFXType.None)
                            ProofUrpParticleShaders(inst);

                        // WO-1813: MIRROR THE RUNTIME PER PATH, or a green capture proves
                        // nothing. VFXManager.Hovl.CreateHovlInstance now runs exactly this
                        // narrow pair on every PlayKey instance (it runs no legacy remap, and
                        // that is deliberate — the HS_* graphs ship URP-clean). The VFXType
                        // path reaches the same helper from inside ProofUrpParticleShaders, so
                        // calling it again here is idempotent: the repaired clone is already
                        // transparent and no longer matches the predicate.
                        AbilityVfxKit.RepairOpaqueDrawingParticleSlots(inst, layer.Label);
                        AbilityVfxKit.RepairMagentaFixParticleSlots(inst, layer.Label);
                        AbilityVfxKit.AuditParticleSlotsAfterRepair(inst, layer.Label);
                        AbilityVfxKit.AuditDrawingBillboardCensus(inst, layer.Label);
                    }

                    result.TrailSlotsSkipped += CollectShaderProblems(inst, layer.Label, shaderProblems);
                    fxRoots.Add(inst);
                    played.Add(layer.Label + " (x" + s.ToString("0.##", CultureInfo.InvariantCulture) + ")");
                }

                result.Played = played.Count > 0 ? string.Join(" + ", played.ToArray()) : "(none resolved)";
                result.ShaderVerdict = shaderProblems.Count == 0
                    ? "clean"
                    : string.Join("; ", shaderProblems.ToArray());

                if (fxRoots.Count == 0)
                {
                    result.Pass = false;
                    result.Reason = "no effect layer resolved to a prefab -- " +
                                    string.Join("; ", layerProblems.ToArray());
                    return result;
                }

                // -- Deterministic simulation. Try the chosen time first; if the effect
                // contributed nothing, walk a short ladder before calling it dead, so a
                // mis-estimated peak is not reported as a missing effect.
                //
                // EVERY RUNG IS PROPORTIONAL TO THE SHOT'S OWN TIME. The first version
                // ended in a fixed 0.05 s rung, which is meaningless for a slow effect --
                // it made the portal's PP_GroundFog (a 2.5 s volumetric) report "0 px at
                // t=0.05" as if that were the harness's considered answer. It was just the
                // last rung of a ladder that had already given up. A slow effect now probes
                // LATER (4x), not into milliseconds.
                float[] ladder = { shot.SimTime, shot.SimTime * 0.4f, shot.SimTime * 2.5f, shot.SimTime * 4f };

                // Frame the camera once, off the fully simulated stage at the requested
                // time, so every retry renders from the SAME viewpoint and the baseline
                // diff stays valid.
                SimulateAll(fxRoots, shot.SimTime);
                var camGo = new GameObject("~VfxProofCamera");
                camGo.transform.SetParent(stage.transform, false);
                var cam = camGo.AddComponent<Camera>();
                cam.clearFlags = CameraClearFlags.SolidColor;
                // A near-black but non-black clear: pure black hides a dark effect from
                // the eye, and a bright clear washes out an additive one. This is dark
                // enough to read a glow and light enough to read a silhouette.
                cam.backgroundColor = new Color(0.06f, 0.065f, 0.08f, 1f);
                cam.nearClipPlane = 0.05f;
                cam.farClipPlane = 500f;
                cam.fieldOfView = shot.FixedCamera ? shot.CamFov : 40f;
                cam.cullingMask = ~0;
                // FRAME ON THE SUBJECT + THE EFFECT ONLY. The ground plane is a 12 m
                // backdrop; letting it into the framing bounds would push the camera far
                // enough back to turn a muzzle flash into two pixels -- a shot that proves
                // nothing, which is the one outcome worse than no shot.
                var framingRoots = new List<GameObject>(fxRoots);
                if (subject != null) framingRoots.Add(subject);
                if (shot.FixedCamera)
                {
                    // WO-1813: the owner's seat, not a fit. See Shot.FixedCamera.
                    cam.transform.localPosition = shot.CamPos;
                    cam.transform.LookAt(stage.transform.position + shot.CamLookAt);
                }
                else FrameCamera(cam, framingRoots, stage.transform.position);

                rt = new RenderTexture(ShotW, ShotH, 24, RenderTextureFormat.ARGB32) { antiAliasing = 1 };
                rt.Create();
                cam.targetTexture = rt;

                // THE REGION OF INTEREST: the effect's own projected screen footprint.
                // "Did it draw" is judged in here, not against 3.2 megapixels of mostly
                // empty frame. Computed once, at the framing time, so every rung of the
                // ladder is measured against the same window.
                RectInt roi = ComputeEffectRoi(cam, fxRoots);
                long roiArea = (long)roi.width * roi.height;
                long needed = Math.Max(MinChangedAbsolute, (long)(MinChangedRoiFraction * roiArea));

                long changed = 0;
                var ladderLog = new List<string>();

                // KEEP THE BEST ATTEMPT, NOT THE LAST. The first version overwrote SimUsed
                // on every rung, so a shot that failed at all four reported the LAST time
                // tried as though it were the chosen one -- which is how the portal came to
                // claim t=0.05 in a table whose own text promised "the time that produced
                // the picture". Now the rung with the largest contribution is the one that
                // is kept, written, and reported, and the whole ladder is printed so the
                // claim is auditable rather than trusted.
                Texture2D best = null;
                long bestChanged = -1;
                float bestT = shot.SimTime;

                for (int attempt = 0; attempt < ladder.Length; attempt++)
                {
                    float t = Mathf.Max(0.01f, ladder[attempt]);
                    SimulateAll(fxRoots, t);

                    var frame = RenderToTexture(cam, rt);

                    if (without == null)
                    {
                        // Baseline: identical camera, effects switched off. Rendered once
                        // (the stage is otherwise static, so it never changes).
                        foreach (var go in fxRoots) go.SetActive(false);
                        without = RenderToTexture(cam, rt);
                        foreach (var go in fxRoots) go.SetActive(true);
                        SimulateAll(fxRoots, t);   // re-simulate: SetActive resets the systems
                        UnityEngine.Object.DestroyImmediate(frame);
                        frame = RenderToTexture(cam, rt);
                    }

                    long c = CountChangedPixels(frame, without, roi);
                    int aliveNow = 0;
                    foreach (var go in fxRoots)
                    {
                        if (go == null) continue;
                        foreach (var p in go.GetComponentsInChildren<ParticleSystem>(true))
                            if (p != null) aliveNow += p.particleCount;
                    }
                    // WO-1813: the live particle count separates "emitted but invisible" from
                    // "never emitted". Without it a NOT DRAWN row is unactionable.
                    ladderLog.Add(t.ToString("0.###", CultureInfo.InvariantCulture) + "s=" + c +
                                  "px/" + aliveNow + "particles");

                    if (c > bestChanged)
                    {
                        if (best != null) UnityEngine.Object.DestroyImmediate(best);
                        best = frame;
                        bestChanged = c;
                        bestT = t;
                    }
                    else UnityEngine.Object.DestroyImmediate(frame);

                    if (c >= needed) break;
                }

                withFx = best;
                changed = Math.Max(0, bestChanged);
                result.SimUsed = bestT;
                result.Ladder = string.Join(", ", ladderLog.ToArray());

                // -- FAILURE MODE 1: magenta.
                result.MagentaCount = CountMagenta(withFx);
                result.MagentaPct = 100f * result.MagentaCount / (float)(ShotW * ShotH);

                // -- FAILURE MODE 2: not drawn. Two independent measures.
                float spread = LuminanceSpread(withFx);
                bool drewSomething = changed >= needed;
                bool frameAlive = spread >= MinFrameSpread;
                result.Uniformity = string.Format(CultureInfo.InvariantCulture,
                    "{0} px changed in a {1}x{2} ROI ({3:0.00}% of it, needed {4}); frame luminance spread {5:0.000}",
                    changed, roi.width, roi.height,
                    roiArea > 0 ? 100f * changed / roiArea : 0f, needed, spread);

                // -- Verdict.
                var reasons = new List<string>();
                if (layerProblems.Count > 0) reasons.Add("layer(s) unresolved: " + string.Join("; ", layerProblems.ToArray()));
                if (result.MagentaCount > 0) reasons.Add("MAGENTA: " + result.MagentaCount + " px (" +
                                                         result.MagentaPct.ToString("0.000", CultureInfo.InvariantCulture) + "%)");
                if (!frameAlive) reasons.Add("EMPTY FRAME: luminance spread " +
                                             spread.ToString("0.000", CultureInfo.InvariantCulture) +
                                             " -- nothing rendered at all (dead stage / no graphics device)");
                else if (!drewSomething) reasons.Add("NOT DRAWN: the effect changed " + changed +
                                                     " px inside its own screen footprint (needed " + needed +
                                                     "), across every simulate time tried [" + result.Ladder +
                                                     "] -- it contributed nothing visible");
                if (shaderProblems.Count > 0) reasons.Add("SHADER: " + string.Join("; ", shaderProblems.ToArray()));

                result.Pass = reasons.Count == 0;
                result.Reason = result.Pass ? "" : string.Join(" | ", reasons.ToArray());

                // Write the PNG even on a failure: the picture IS the evidence of what
                // went wrong. The only thing never written is a frame that does not exist.
                string path = Path.Combine(OutDir, shot.FileName + ".png");
                byte[] png = withFx.EncodeToPNG();
                if (png != null && png.Length > 0)
                {
                    File.WriteAllBytes(path, png);
                    result.Png = shot.FileName + ".png";
                    result.Fingerprint = Fingerprint(png);
                    Debug.Log("[VfxProof] " + (result.Pass ? "PASS " : "FAIL ") + shot.FileName +
                              " -> " + Path.GetFullPath(path) + " (" + png.Length + " bytes, t=" +
                              result.SimUsed.ToString("0.###", CultureInfo.InvariantCulture) +
                              "s, fp=" + result.Fingerprint + ")");
                }
                else
                {
                    result.Pass = false;
                    result.Reason = (result.Reason.Length > 0 ? result.Reason + " | " : "") +
                                    "EncodeToPNG produced no bytes";
                }

                if (subjectNotes.Length > 0)
                    shot.Notes = (shot.Notes.Length > 0 ? shot.Notes + " " : "") + subjectNotes.ToString();

                return result;
            }
            catch (Exception e)
            {
                result.Pass = false;
                result.Reason = "capture threw: " + e.Message;
                Debug.LogError("[VfxProof] " + shot.FileName + " threw: " + e);
                return result;
            }
            finally
            {
                RenderTexture.active = prevActive;
                if (withFx != null) UnityEngine.Object.DestroyImmediate(withFx);
                if (without != null) UnityEngine.Object.DestroyImmediate(without);
                if (rt != null) { rt.Release(); UnityEngine.Object.DestroyImmediate(rt); }
                // The stage owns the light, the camera, the subject and every effect
                // instance, so one destroy cleans up everything this shot created.
                if (stage != null) UnityEngine.Object.DestroyImmediate(stage);
            }
        }

        // ---------------------------------------------------------------------
        //  Staging helpers
        // ---------------------------------------------------------------------

        private static GameObject StageSubject(Transform parent, Shot shot, StringBuilder notes)
        {
            // Enemy props go through the runtime seam (Addressables-first, Resources-fallback)
            // so the enemy-subject shots keep staging a real body after Assets/EnemyContent
            // migrates out of Resources; every other key is a plain Resources path.
            var prefab = shot.SubjectResourcePath.StartsWith(DeNelle.Core.EnemyAssetLoader.EnemyAddrPrefix, System.StringComparison.Ordinal)
                ? DeNelle.Core.EnemyAssetLoader.LoadEnemyAsset<GameObject>(shot.SubjectResourcePath)
                : Resources.Load<GameObject>(shot.SubjectResourcePath);
            if (prefab == null)
            {
                notes.Append("Subject prop '").Append(shot.SubjectResourcePath)
                     .Append("' did not load (Addressables/Resources) -- the effect is staged alone (not a failure).");
                StageGroundPlane(parent);
                return null;
            }

            var go = UnityEngine.Object.Instantiate(prefab, parent);
            go.name = "~subject";
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;

            if (shot.SubjectEuler != Vector3.zero)
                go.transform.localRotation = Quaternion.Euler(shot.SubjectEuler) * go.transform.localRotation;

            // Fit to height + seat on y=0, mirroring the intent of StructureFactory's
            // fit-to-height pass (StructureFactory.cs:121-123) without dragging in the
            // whole VisualFactory/TripoMaterialFixer chain, none of which runs its
            // Start() in edit mode. The prop is CONTEXT; the effect is the evidence.
            if (shot.SubjectHeight > 0f && TryGetBounds(go, out Bounds b) && b.size.y > 0.0001f)
            {
                float k = shot.SubjectHeight / b.size.y;
                go.transform.localScale = go.transform.localScale * k;
            }
            if (TryGetBounds(go, out Bounds seated))
                go.transform.localPosition -= new Vector3(0f, seated.min.y - parent.position.y, 0f);

            StageGroundPlane(parent);
            return go;
        }

        /// <summary>A neutral matte floor so a ground-hugging effect has something to
        /// read against and the frame is never a void.</summary>
        private static void StageGroundPlane(Transform parent)
        {
            var plane = GameObject.CreatePrimitive(PrimitiveType.Plane);
            plane.name = "~ground";
            plane.transform.SetParent(parent, false);
            plane.transform.localPosition = new Vector3(0f, -0.01f, 0f);
            plane.transform.localScale = Vector3.one * 1.2f;   // Plane primitive is 10 m -> 12 m backdrop
            var col = plane.GetComponent<Collider>();
            if (col != null) UnityEngine.Object.DestroyImmediate(col);

            var sh = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            if (sh == null) return;
            var m = new Material(sh) { hideFlags = HideFlags.DontSave };
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", new Color(0.24f, 0.24f, 0.26f));
            if (m.HasProperty("_Color")) m.SetColor("_Color", new Color(0.24f, 0.24f, 0.26f));
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.05f);
            var r = plane.GetComponent<Renderer>();
            if (r != null) r.sharedMaterial = m;
        }

        private static GameObject ResolveLayerPrefab(Layer layer, out string note, out float catalogScale)
        {
            note = "";
            catalogScale = 0f;

            if (layer.Type != VFXType.None)
            {
                if (_vfxCatalog == null) { note = "VFXCatalog not loaded"; return null; }
                if (!_vfxCatalog.TryGet(layer.Type, out var entry))
                {
                    note = "no VFXCatalog row for " + layer.Type +
                           " (runtime falls through to the procedural AbilityVfxKit path)";
                    return null;
                }
                if (entry.Prefab == null)
                {
                    note = "VFXCatalog row for " + layer.Type + " has a NULL prefab";
                    return null;
                }
                return entry.Prefab;
            }

            if (string.IsNullOrEmpty(layer.Key)) { note = "layer has neither a VFXType nor a key"; return null; }
            if (_hovlCatalog == null) { note = "HovlVfxCatalog not loaded"; return null; }
            if (!_hovlCatalog.TryGet(layer.Key, out var row))
            {
                note = "no HovlVfxCatalog row for key '" + layer.Key +
                       "' -- VFXManager.PlayKey no-ops on this key in the shipped game " +
                       "(VFXManager.Hovl.cs:205)";
                return null;
            }
            if (row.Prefab == null)
            {
                note = "HovlVfxCatalog row '" + layer.Key + "' has a NULL prefab (pack not imported?)";
                return null;
            }
            catalogScale = row.DefaultScale;
            return row.Prefab;
        }

        // ---------------------------------------------------------------------
        //  Deterministic simulation
        // ---------------------------------------------------------------------

        /// <summary>
        /// Drive every root ParticleSystem to exactly <paramref name="t"/> seconds.
        /// Simulate(t, withChildren:true, restart:true, fixedTimeStep:true) is what
        /// makes the frame reproducible: without it the render shows whatever the
        /// first frame happens to be, which for a burst is a single particle.
        /// The random seed is pinned so two runs produce the same picture.
        /// </summary>
        private static void SimulateAll(List<GameObject> roots, float t)
        {
            SimulateAllCounted(roots, t);
        }

        /// <summary>
        /// WO-1813: same simulate, but it REPORTS how many particles are actually alive
        /// afterwards. "NOT DRAWN" has two very different causes — the system emitted and the
        /// pixels are invisible, or the system never emitted at all — and a verdict that cannot
        /// tell them apart sends the next reader hunting a rendering bug that is not there
        /// (CLAUDE.md §12: split data-empty from built-but-invisible BEFORE touching code).
        /// </summary>
        private static int SimulateAllCounted(List<GameObject> roots, float t)
        {
            int alive = 0;
            SimulateAllCore(roots, t);
            foreach (var root in roots)
            {
                if (root == null) continue;
                foreach (var ps in root.GetComponentsInChildren<ParticleSystem>(true))
                    if (ps != null) alive += ps.particleCount;
            }
            return alive;
        }

        private static void SimulateAllCore(List<GameObject> roots, float t)
        {
            foreach (var root in roots)
            {
                if (root == null) continue;
                foreach (var ps in root.GetComponentsInChildren<ParticleSystem>(true))
                {
                    if (ps == null) continue;
                    // Only drive ROOT systems -- withChildren:true carries the rest, and
                    // simulating a child directly would double-advance it.
                    var parentPs = ps.transform.parent != null
                        ? ps.transform.parent.GetComponentInParent<ParticleSystem>()
                        : null;
                    if (parentPs != null) continue;

                    ps.useAutoRandomSeed = false;
                    ps.randomSeed = 20260806;
                    ps.Simulate(t, true, true, true);

                    // WO-1813: a BURST-only system authored playOnAwake:0 can come back from
                    // Simulate with zero live particles in edit mode, and the harness then
                    // reports NOT DRAWN for an effect that is perfectly fine in game --
                    // measured 2026-09-17: PP_FleshImpacts, PP_MuzzleFlash, Cast_MuzzleFlash
                    // and ArcherTower_Projectile all rendered the bare stage (identical
                    // fingerprint 7fd9aace) at every rung of the ladder. A harness that cannot
                    // make the subject appear proves nothing about it -- which is worse than a
                    // failing shot, because it reads like one.
                    //
                    // So: only when the system is still EMPTY, emit its authored burst count
                    // by hand and re-settle. Nothing is invented -- the count comes from the
                    // prefab's own emission bursts, and a system that DID simulate is never
                    // touched, so no existing shot changes.
                    if (ps.particleCount == 0)
                        ForceAuthoredBurst(ps, t);
                }
            }
        }

        /// <summary>
        /// WO-1813: emit each child system's own authored burst count and re-settle to
        /// <paramref name="t"/>. Only called when Simulate left the system empty.
        /// </summary>
        private static void ForceAuthoredBurst(ParticleSystem root, float t)
        {
            foreach (var ps in root.GetComponentsInChildren<ParticleSystem>(true))
            {
                if (ps == null || ps.particleCount > 0) continue;

                int count = 0;
                var em = ps.emission;
                if (em.enabled)
                {
                    int n = em.burstCount;
                    for (int i = 0; i < n; i++)
                        count += Mathf.Max(0, (int)em.GetBurst(i).count.constantMax);
                    // A rate-only system with no bursts: one second's worth is a fair sample.
                    if (count == 0)
                        count = Mathf.Max(0, (int)em.rateOverTime.constantMax);
                }
                if (count <= 0) continue;

                ps.Emit(Mathf.Min(count, 200));   // bounded: a proof frame, not a stress test
                // Re-settle so size/colour-over-lifetime have advanced to the same instant
                // the rest of the stage is showing. Fixed step, no restart -- restarting here
                // would throw the particles we just emitted away.
                ps.Simulate(Mathf.Min(t, ps.main.startLifetime.constantMax * 0.5f), false, false, true);
            }
        }

        // ---------------------------------------------------------------------
        //  Camera framing + render
        // ---------------------------------------------------------------------

        private static void FrameCamera(Camera cam, List<GameObject> roots, Vector3 origin)
        {
            Bounds b = default;
            bool any = false;
            foreach (var go in roots)
            {
                if (go == null) continue;
                if (!TryGetBounds(go, out Bounds gb)) continue;
                if (!any) { b = gb; any = true; }
                else b.Encapsulate(gb);
            }
            if (!any)
                b = new Bounds(origin + Vector3.up * 1.5f, Vector3.one * 4f);

            // Clamp: a ParticleSystemRenderer can report an enormous world-space bound
            // that would push the camera so far back the effect is a speck.
            float radius = Mathf.Clamp(b.extents.magnitude, 1.2f, 14f);
            float dist = radius / Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) * 1.25f;

            // Three-quarter view, slightly above eye line: reads a tower silhouette and
            // a ground-hugging mist in the same framing.
            Vector3 dir = new Vector3(0.62f, 0.34f, -1f).normalized;
            Vector3 centre = new Vector3(b.center.x, Mathf.Clamp(b.center.y, 0.4f, 12f), b.center.z);
            cam.transform.position = centre + dir * dist;
            cam.transform.LookAt(centre);
        }

        private static bool TryGetBounds(GameObject go, out Bounds bounds)
        {
            bounds = default;
            if (go == null) return false;
            bool any = false;
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            {
                if (r == null || !r.enabled) continue;
                var rb = r.bounds;
                if (rb.size.sqrMagnitude <= 0.0000001f) continue;
                if (!any) { bounds = rb; any = true; }
                else bounds.Encapsulate(rb);
            }
            return any;
        }

        private static Texture2D RenderToTexture(Camera cam, RenderTexture rt)
        {
            var prev = RenderTexture.active;
            try
            {
                // SRP-correct request first, legacy Camera.Render as the fallback --
                // the same order UICaptureLaunch.cs:2075-2077 uses.
                var req = new RenderPipeline.StandardRequest { destination = rt };
                if (RenderPipeline.SupportsRenderRequest(cam, req)) cam.SubmitRenderRequest(req);
                else cam.Render();

                RenderTexture.active = rt;
                var tex = new Texture2D(ShotW, ShotH, TextureFormat.RGBA32, false);
                tex.ReadPixels(new Rect(0f, 0f, ShotW, ShotH), 0, 0);
                tex.Apply(false);
                return tex;
            }
            finally
            {
                RenderTexture.active = prev;
            }
        }

        // ---------------------------------------------------------------------
        //  FAILURE MODE 1 -- magenta
        // ---------------------------------------------------------------------

        private static long CountMagenta(Texture2D tex)
        {
            long n = 0;
            var px = tex.GetPixels32();
            for (int i = 0; i < px.Length; i++)
            {
                float r = px[i].r / 255f, g = px[i].g / 255f, b = px[i].b / 255f;
                if (r > MagentaR && b > MagentaB && g < MagentaG) n++;
            }
            return n;
        }

        // ---------------------------------------------------------------------
        //  FAILURE MODE 2 -- not drawn (differential + whole-frame uniformity)
        // ---------------------------------------------------------------------

        /// <summary>
        /// The effect's own screen footprint: the projected screen-space bounding box of
        /// every effect renderer, padded, clamped to the frame. This is the window the
        /// "did it draw" test is measured in -- a muzzle flash is SUPPOSED to be a small
        /// part of a 2670x1200 frame, so judging it against the whole frame measures the
        /// camera distance, not the effect. Degenerate cases fall back to the full frame,
        /// which is the conservative direction (harder to pass, never falsely lenient).
        /// </summary>
        private static RectInt ComputeEffectRoi(Camera cam, List<GameObject> fxRoots)
        {
            var full = new RectInt(0, 0, ShotW, ShotH);
            if (cam == null || fxRoots == null || fxRoots.Count == 0) return full;

            bool any = false;
            Bounds b = default;
            foreach (var go in fxRoots)
            {
                if (go == null) continue;
                if (!TryGetBounds(go, out Bounds gb)) continue;
                if (!any) { b = gb; any = true; }
                else b.Encapsulate(gb);
            }
            if (!any) return full;

            // Project all eight corners: a box that is cheap to compute and never
            // under-covers the effect the way a centre-plus-radius estimate would.
            float minX = float.MaxValue, minY = float.MaxValue;
            float maxX = float.MinValue, maxY = float.MinValue;
            Vector3 c = b.center, e = b.extents;
            for (int i = 0; i < 8; i++)
            {
                var corner = c + new Vector3(
                    ((i & 1) == 0 ? -e.x : e.x),
                    ((i & 2) == 0 ? -e.y : e.y),
                    ((i & 4) == 0 ? -e.z : e.z));
                Vector3 sp = cam.WorldToScreenPoint(corner);
                if (sp.z <= 0f) return full;   // straddles the camera plane -- do not guess
                if (sp.x < minX) minX = sp.x;
                if (sp.x > maxX) maxX = sp.x;
                if (sp.y < minY) minY = sp.y;
                if (sp.y > maxY) maxY = sp.y;
            }

            float padX = (maxX - minX) * RoiPadding;
            float padY = (maxY - minY) * RoiPadding;
            int x0 = Mathf.Clamp(Mathf.FloorToInt(minX - padX), 0, ShotW - 1);
            int y0 = Mathf.Clamp(Mathf.FloorToInt(minY - padY), 0, ShotH - 1);
            int x1 = Mathf.Clamp(Mathf.CeilToInt(maxX + padX), 0, ShotW);
            int y1 = Mathf.Clamp(Mathf.CeilToInt(maxY + padY), 0, ShotH);
            int w = x1 - x0, h = y1 - y0;
            if (w < 8 || h < 8) return full;   // nonsense box -- fall back rather than invent one
            return new RectInt(x0, y0, w, h);
        }

        /// <summary>
        /// How many pixels the effect changed, inside <paramref name="roi"/>, relative to
        /// the identical frame rendered without it. This is the check a magenta scan
        /// cannot do: an effect that renders BLACK on a dark stage, or does not render at
        /// all, moves zero pixels here while looking perfectly ordinary to a colour test.
        /// </summary>
        private static long CountChangedPixels(Texture2D withFx, Texture2D without, RectInt roi)
        {
            if (withFx == null || without == null) return 0;
            var a = withFx.GetPixels32();
            var b = without.GetPixels32();
            if (a.Length != b.Length) return 0;

            int eps = Mathf.Max(1, Mathf.RoundToInt(DiffEpsilon * 255f));
            long changed = 0;
            int yEnd = Mathf.Min(roi.y + roi.height, ShotH);
            int xEnd = Mathf.Min(roi.x + roi.width, ShotW);
            for (int y = roi.y; y < yEnd; y++)
            {
                int row = y * ShotW;
                for (int x = roi.x; x < xEnd; x++)
                {
                    int i = row + x;
                    if (i < 0 || i >= a.Length) continue;
                    if (Mathf.Abs(a[i].r - b[i].r) >= eps ||
                        Mathf.Abs(a[i].g - b[i].g) >= eps ||
                        Mathf.Abs(a[i].b - b[i].b) >= eps) changed++;
                }
            }
            return changed;
        }

        /// <summary>
        /// Content hash of a written PNG (FNV-1a, 8 hex chars). Two shots sharing a
        /// fingerprint rendered the IDENTICAL picture. For a per-level ladder that is a
        /// statement about the game, not a harness fault -- surfacing it turns "three
        /// suspiciously equal numbers" into a documented finding.
        /// </summary>
        private static string Fingerprint(byte[] bytes)
        {
            unchecked
            {
                uint h = 2166136261u;
                for (int i = 0; i < bytes.Length; i++)
                {
                    h ^= bytes[i];
                    h *= 16777619u;
                }
                return h.ToString("x8", CultureInfo.InvariantCulture);
            }
        }

        /// <summary>
        /// Luminance max-min over the frame. Near zero means the frame is essentially
        /// one flat colour -- nothing rendered at all (no graphics device, or a stage
        /// that failed to build). Independent of the diff, so a degenerate diff cannot
        /// mask a dead frame.
        /// </summary>
        private static float LuminanceSpread(Texture2D tex)
        {
            if (tex == null) return 0f;
            var px = tex.GetPixels32();
            float lo = 1f, hi = 0f;
            for (int i = 0; i < px.Length; i++)
            {
                float l = (0.2126f * px[i].r + 0.7152f * px[i].g + 0.0722f * px[i].b) / 255f;
                if (l < lo) lo = l;
                if (l > hi) hi = l;
            }
            return hi - lo;
        }

        // ---------------------------------------------------------------------
        //  FAILURE MODE 3 -- error / missing shaders
        // ---------------------------------------------------------------------

        /// <summary>
        /// Walk every material on the staged effect and report, by name, any null
        /// material, null shader, or Unity error shader. A missing shader can render
        /// magenta, black, white or nothing -- so this asks the materials directly
        /// rather than inferring the answer from pixels.
        ///
        /// THE TRAIL SLOT (coordinator's first run, 2026-08-06 -- this check failed 14
        /// of 20 shots for a non-defect). A ParticleSystemRenderer ALWAYS exposes TWO
        /// material slots through sharedMaterials: [0] is `material` and [1] is
        /// `trailMaterial`. Slot 1 is legitimately EMPTY on every particle system that
        /// does not draw trails, which is most of them -- so reading it as a broken
        /// material condemned healthy art (`ArcherTower_Projectile/Sparks[1]`,
        /// `SimpleCast_Cast/Flash[1]`, `Fire_Cast/BigSparks[1]` ...). The Hovl pack was
        /// verified fully present and its HS_Blend_CG shader graph resolves; there was
        /// nothing wrong.
        ///
        /// So slot 1 on a ParticleSystemRenderer is only a defect when the system's
        /// Trails module is actually ENABLED. It is skipped otherwise -- and the skips
        /// are COUNTED and reported, so this stays a narrow, visible exclusion rather
        /// than a blanket "ignore index 1" that could hide a real trail defect later.
        /// Slot 0 is never excluded: a null there is the particle material itself, which
        /// is exactly the genuine finding on BurningStructure_Aura. Non-particle
        /// renderers keep every slot checked -- a trailing null there is the white-slab
        /// defect and must still fail.
        /// </summary>
        private static int CollectShaderProblems(GameObject go, string label, List<string> into)
        {
            if (go == null) return 0;
            int trailSlotsSkipped = 0;

            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            {
                if (r == null) continue;
                var mats = r.sharedMaterials;
                if (mats == null || mats.Length == 0)
                {
                    into.Add(label + "/" + r.name + ": no materials");
                    continue;
                }

                var psr = r as ParticleSystemRenderer;
                bool trailsOn = false;
                if (psr != null)
                {
                    var ps = psr.GetComponent<ParticleSystem>();
                    trailsOn = ps != null && ps.trails.enabled;
                }

                for (int i = 0; i < mats.Length; i++)
                {
                    // The one exclusion: an unused trail slot on a non-trailing system.
                    if (psr != null && i == 1 && !trailsOn && mats[i] == null)
                    {
                        trailSlotsSkipped++;
                        continue;
                    }

                    var m = mats[i];
                    string slot = psr != null ? (i == 0 ? "[0 material]" : i == 1 ? "[1 trailMaterial]" : "[" + i + "]")
                                              : "[" + i + "]";
                    if (m == null) { into.Add(label + "/" + r.name + slot + ": NULL material"); continue; }
                    var sh = m.shader;
                    if (sh == null) { into.Add(label + "/" + r.name + slot + " '" + m.name + "': NULL shader"); continue; }
                    string n = sh.name ?? string.Empty;
                    if (n.IndexOf("InternalErrorShader", StringComparison.Ordinal) >= 0 ||
                        n.IndexOf("Hidden/InternalError", StringComparison.Ordinal) >= 0)
                        into.Add(label + "/" + r.name + slot + " '" + m.name + "': ERROR SHADER '" + n + "'");
                }
            }
            return trailSlotsSkipped;
        }

        // ---------------------------------------------------------------------
        //  URP particle-shader proof -- mirrors VFXManager.ProofUrpParticleShaders
        // ---------------------------------------------------------------------

        // WHY A MIRROR AND NOT A CALL: VFXManager.ProofUrpParticleShaders is private,
        // and it runs on EVERY pooled instance (VFXManager.cs:596). Without the same
        // pass here, a legacy-shaded Lana/Spells prefab would render magenta in the
        // proof and clean in the game -- a false failure, which is just as damaging as
        // a false pass. The blend-mode / texture / tint carry-over below is the same
        // logic, and it delegates the shader lookup and the half-upgraded-material
        // heal to the SAME public AbilityVfxKit helpers the runtime uses, so there is
        // one source of truth for both.

        private const int BLEND_ONE = 1;
        private const int BLEND_SRC_ALPHA = 5;
        private const int BLEND_ONE_MINUS_SRC_ALPHA = 10;

        private static void ProofUrpParticleShaders(GameObject go)
        {
            if (go == null) return;
            var renderers = go.GetComponentsInChildren<Renderer>(true);
            if (renderers == null || renderers.Length == 0) return;

            Shader urp = AbilityVfxKit.ResolveParticleShader();
            if (urp == null || urp.name.IndexOf("Universal Render Pipeline", StringComparison.Ordinal) < 0)
                return;   // leave authored materials alone -- same call the runtime makes

            foreach (var r in renderers)
            {
                if (r == null) continue;
                var mats = r.sharedMaterials;
                if (mats == null) continue;
                bool changed = false;

                for (int i = 0; i < mats.Length; i++)
                {
                    var src = mats[i];
                    if (src == null) continue;

                    if (!IsLegacyParticleShader(src.shader))
                    {
                        AbilityVfxKit.HealHalfUpgradedParticleMaterial(src);
                        // WO-1806: the capture must repair EXACTLY what the runtime repairs,
                        // or a green capture proves nothing about the shipped frame. This was
                        // the THIRD copy of the `i > 0` MagentaFix branch (VFXManager and
                        // ProjectileVFXCatalog held the other two) and therefore the third
                        // place that could not see a SLOT-0 occurrence. One owner now.
                        if (AbilityVfxKit.TryRepairOpaqueLitParticleSlot(r, mats, i))
                            changed = true;
                        continue;
                    }

                    bool additive = SourceWantsAdditive(src);
                    Texture mainTex = SafeGetMainTexture(src);
                    Color tint = SafeGetTintColor(src);

                    var nm = new Material(urp) { name = src.name + "_URPProofed", hideFlags = HideFlags.DontSave };
                    if (nm.HasProperty("_Surface")) nm.SetFloat("_Surface", 1f);
                    if (nm.HasProperty("_Blend")) nm.SetFloat("_Blend", additive ? 2f : 0f);
                    if (nm.HasProperty("_SrcBlend")) nm.SetFloat("_SrcBlend", BLEND_SRC_ALPHA);
                    if (nm.HasProperty("_DstBlend")) nm.SetFloat("_DstBlend", additive ? BLEND_ONE : BLEND_ONE_MINUS_SRC_ALPHA);
                    if (nm.HasProperty("_ZWrite")) nm.SetFloat("_ZWrite", 0f);
                    nm.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                    nm.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                    nm.DisableKeyword("_ALPHAMODULATE_ON");
                    nm.SetOverrideTag("RenderType", "Transparent");
                    nm.renderQueue = (int)RenderQueue.Transparent;
                    if (mainTex != null)
                    {
                        if (nm.HasProperty("_BaseMap")) nm.SetTexture("_BaseMap", mainTex);
                        else nm.mainTexture = mainTex;
                    }
                    if (nm.HasProperty("_BaseColor")) nm.SetColor("_BaseColor", tint);
                    if (nm.HasProperty("_Color")) nm.SetColor("_Color", tint);

                    mats[i] = nm;
                    changed = true;
                }

                if (changed) r.sharedMaterials = mats;
            }

            // WO-1806: same read-back census the runtime now runs, so a capture that LOOKS
            // clean also SAYS so in the log — and a capture that hides a slab names it.
            AbilityVfxKit.AuditParticleSlotsAfterRepair(go, go.name);
        }

        /// <summary>VFXManager.IsLegacyParticleShader (:752-761), verbatim.</summary>
        private static bool IsLegacyParticleShader(Shader sh)
        {
            if (sh == null) return true;
            string n = sh.name ?? string.Empty;
            if (n.IndexOf("Universal Render Pipeline", StringComparison.Ordinal) >= 0) return false;
            if (n == "Hidden/InternalErrorShader") return true;
            if (n.StartsWith("Legacy Shaders/", StringComparison.Ordinal)) return true;
            if (n.IndexOf("Particles/", StringComparison.Ordinal) >= 0) return true;
            return false;
        }

        /// <summary>VFXManager.SourceWantsAdditive (:769-786), verbatim.</summary>
        private static bool SourceWantsAdditive(Material src)
        {
            if (src == null) return true;
            string n = src.shader != null ? (src.shader.name ?? string.Empty) : string.Empty;
            if (n.IndexOf("Additive", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (n.IndexOf("Alpha Blended", StringComparison.OrdinalIgnoreCase) >= 0) return false;
            if (n.IndexOf("AlphaBlend", StringComparison.OrdinalIgnoreCase) >= 0) return false;
            if (src.HasProperty("_DstBlend"))
            {
                int dst = (int)src.GetFloat("_DstBlend");
                if (dst == BLEND_ONE) return true;
                if (dst == BLEND_ONE_MINUS_SRC_ALPHA) return false;
            }
            return true;
        }

        private static Texture SafeGetMainTexture(Material src)
        {
            if (src == null) return null;
            if (src.HasProperty("_MainTex")) return src.GetTexture("_MainTex");
            if (src.HasProperty("_BaseMap")) return src.GetTexture("_BaseMap");
            return null;
        }

        private static Color SafeGetTintColor(Material src)
        {
            if (src == null) return Color.white;
            if (src.HasProperty("_TintColor")) return src.GetColor("_TintColor");
            if (src.HasProperty("_Color")) return src.GetColor("_Color");
            if (src.HasProperty("_BaseColor")) return src.GetColor("_BaseColor");
            return Color.white;
        }

        // ---------------------------------------------------------------------
        //  INDEX.md -- the thing the owner reads first
        // ---------------------------------------------------------------------

        private static void WriteIndex(List<Result> results)
        {
            int pass = 0;
            foreach (var r in results) if (r.Pass) pass++;

            var sb = new StringBuilder();
            sb.AppendLine("# VFX Proof Capture");
            sb.AppendLine();
            sb.AppendLine("Generated by `DeNelle.Editor.VfxProofCapture.Run` on " +
                          DateTime.Now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) + ".");
            sb.AppendLine();
            sb.AppendLine("**" + pass + " of " + results.Count + " shots PASS.** Every PNG in this folder is a " +
                          ShotW + "x" + ShotH + " render of one effect, staged from the same catalog prefab the " +
                          "game plays, simulated to a fixed time so the frame is reproducible.");
            sb.AppendLine();

            sb.AppendLine("## What each verdict column means");
            sb.AppendLine();
            sb.AppendLine("A missing shader does not only render magenta -- it can equally render BLACK, " +
                          "UNTEXTURED WHITE, or nothing at all. So each shot is checked three ways:");
            sb.AppendLine();
            sb.AppendLine("| Check | What it catches | How |");
            sb.AppendLine("|---|---|---|");
            sb.AppendLine("| **Magenta px** | Unity's error-shader pink | pixels with R>0.90, B>0.90, G<0.30 |");
            sb.AppendLine("| **Drawn?** | the invisible and the black-on-dark cases | the SAME camera renders the stage twice, with and without the effect, and the difference is counted **inside the effect's own projected screen footprint** -- not against the whole frame. It must move at least " +
                          MinChangedAbsolute + " px, or " +
                          (MinChangedRoiFraction * 100f).ToString("0.0##", CultureInfo.InvariantCulture) +
                          "% of that footprint, whichever is larger. A whole-frame luminance spread is reported alongside it, so a dead stage is caught even if the diff is degenerate |");
            sb.AppendLine("| **Shaders** | null material, null shader, `Hidden/InternalErrorShader` | the materials are asked directly, and named |");
            sb.AppendLine();
            sb.AppendLine("Two calibrations, both corrections to the first run of this harness (2026-08-06):");
            sb.AppendLine();
            sb.AppendLine("- **The \"drawn\" test is cropped to the effect, not the frame.** It used to be a " +
                          "fraction of all 3.2 megapixels, so one muzzle flash failed at 468 px (0.015%) and the " +
                          "next passed at 789 px (0.025%). Both were the same effect a few pixels across; a " +
                          "0.01%-of-frame gap deciding pass/fail is measurement noise, not evidence. A muzzle " +
                          "flash is *supposed* to be small -- what it must not be is absent, and an absent " +
                          "effect moves zero pixels, not four hundred.");
            sb.AppendLine("- **An empty trail slot is not a broken material.** A `ParticleSystemRenderer` always " +
                          "exposes two material slots -- `[0] material` and `[1] trailMaterial` -- and slot 1 is " +
                          "legitimately empty on every system that does not draw trails, which is most of them. " +
                          "Reading it as a defect failed 14 of 20 shots against perfectly healthy art. Slot 1 is " +
                          "now only checked when the system's Trails module is actually enabled, the skips are " +
                          "counted below so the exclusion stays visible, and **slot 0 is never excluded** -- a " +
                          "null there is the particle material itself and still fails.");
            sb.AppendLine();

            sb.AppendLine("## Shots");
            sb.AppendLine();
            sb.AppendLine("| # | Subject | Level | VFX played | Sim t | PNG | Frame id | Magenta px | Drawn? | Shaders | Verdict |");
            sb.AppendLine("|---|---|---|---|---|---|---|---|---|---|---|");
            int n = 0;
            int totalTrailSkips = 0;
            foreach (var r in results)
            {
                n++;
                totalTrailSkips += r.TrailSlotsSkipped;
                sb.Append("| ").Append(n)
                  .Append(" | ").Append(Cell(r.Shot.Subject))
                  .Append(" | ").Append(Cell(r.Shot.Level))
                  .Append(" | ").Append(Cell(r.Played))
                  .Append(" | ").Append(r.SimUsed.ToString("0.###", CultureInfo.InvariantCulture)).Append(" s")
                  .Append(" | ").Append(Cell(r.Png))
                  .Append(" | `").Append(Cell(r.Fingerprint)).Append("`")
                  .Append(" | ").Append(r.MagentaCount).Append(" (")
                  .Append(r.MagentaPct.ToString("0.000", CultureInfo.InvariantCulture)).Append("%)")
                  .Append(" | ").Append(Cell(r.Uniformity))
                  .Append(" | ").Append(Cell(r.ShaderVerdict))
                  .Append(" | ").Append(r.Pass ? "**PASS**" : "**FAIL** -- " + Cell(r.Reason))
                  .AppendLine(" |");
            }
            sb.AppendLine();
            sb.AppendLine("Unused `trailMaterial` slots skipped across this run: **" + totalTrailSkips +
                          "**. Each one is a particle system that draws no trails; none is a defect. " +
                          "The count is printed so the exclusion is visible and can be re-audited.");
            sb.AppendLine();

            // -- IDENTICAL FRAMES -------------------------------------------------
            // Three shots of the Sky Ballista at L1/L2/L3 came back with byte-identical
            // measurements on the first run, which reads as a harness fault until you can
            // see that it is not one. Group by content hash and say which it is.
            var byFingerprint = new Dictionary<string, List<Result>>(StringComparer.Ordinal);
            foreach (var r in results)
            {
                if (r.Fingerprint == "-" ) continue;
                if (!byFingerprint.TryGetValue(r.Fingerprint, out var group))
                {
                    group = new List<Result>();
                    byFingerprint[r.Fingerprint] = group;
                }
                group.Add(r);
            }
            var dupes = new List<KeyValuePair<string, List<Result>>>();
            foreach (var kv in byFingerprint) if (kv.Value.Count > 1) dupes.Add(kv);

            if (dupes.Count > 0)
            {
                sb.AppendLine("### Shots that rendered the IDENTICAL picture");
                sb.AppendLine();
                sb.AppendLine("Each stage is torn down and rebuilt from scratch per shot (one `GameObject` root " +
                              "created at the top of the capture and `DestroyImmediate`d in the `finally`), and " +
                              "the particle seed is pinned, so two shots match byte-for-byte only when they " +
                              "genuinely play the same thing. **These are findings about the game, not repeats " +
                              "of a stale frame:**");
                sb.AppendLine();
                foreach (var kv in dupes)
                {
                    var names = new List<string>();
                    foreach (var r in kv.Value) names.Add(r.Shot.Subject + " " + r.Shot.Level);
                    sb.AppendLine("- `" + kv.Key + "` -- " + Cell(string.Join(" == ", names.ToArray())));
                }
                sb.AppendLine();
                sb.AppendLine("For a tower across L1/L2/L3 this is expected wherever the catalog row authors no " +
                              "`repo.upgradeVisualPath` (so one model serves all three levels) AND the tower is " +
                              "not the ground archer (the only tower whose projectile key reads the tier, " +
                              "DefenseTower.cs:1156-1161) -- because `PlayFireVfx` (DefenseTower.cs:1050) never " +
                              "reads the tier at all. Sky Ballista, Ballista and Catapult all meet both " +
                              "conditions. **Upgrading those towers changes nothing the player can see at the " +
                              "moment of fire.** That is a real gap in the tower ladder and belongs on the board.");
                sb.AppendLine();
            }

            bool anyNote = false;
            foreach (var r in results) if (!string.IsNullOrEmpty(r.Shot.Notes)) { anyNote = true; break; }
            if (anyNote)
            {
                sb.AppendLine("### Per-shot notes");
                sb.AppendLine();
                foreach (var r in results)
                {
                    if (string.IsNullOrEmpty(r.Shot.Notes)) continue;
                    sb.AppendLine("- **" + Cell(r.Shot.Subject) + " " + Cell(r.Shot.Level) + "** -- " + Cell(r.Shot.Notes));
                }
                sb.AppendLine();
            }

            sb.AppendLine("## Why each simulate time");
            sb.AppendLine();
            sb.AppendLine("A burst captured at t=0 is one particle -- a shot that proves nothing. " +
                          "Each effect is driven with `ParticleSystem.Simulate(t, true, true, true)` " +
                          "to a time chosen for its own shape. Where the first choice produced no visible " +
                          "contribution the harness retries a ladder of `t`, `0.4t`, `2.5t`, `4t` -- every " +
                          "rung PROPORTIONAL to the effect's own time, so a slow volumetric probes LATER " +
                          "rather than being asked again at a few milliseconds. The **best** rung is the one " +
                          "kept, written and reported; the full ladder is printed below so that claim is " +
                          "auditable instead of merely asserted.");
            sb.AppendLine();
            sb.AppendLine("| Shot | Ladder tried (time = px changed in ROI) | Kept |");
            sb.AppendLine("|---|---|---|");
            foreach (var r in results)
                sb.Append("| ").Append(Cell(r.Shot.Subject + " " + r.Shot.Level))
                  .Append(" | ").Append(Cell(r.Ladder))
                  .Append(" | ").Append(r.SimUsed.ToString("0.###", CultureInfo.InvariantCulture))
                  .AppendLine(" s |");
            sb.AppendLine();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var r in results)
            {
                if (string.IsNullOrEmpty(r.Shot.SimWhy)) continue;
                if (!seen.Add(r.Shot.SimWhy)) continue;
                sb.AppendLine("- " + r.Shot.SimWhy);
            }
            sb.AppendLine();

            sb.AppendLine("## Source of every effect in this run");
            sb.AppendLine();
            sb.AppendLine("| Subject | Layer | Cited at |");
            sb.AppendLine("|---|---|---|");
            foreach (var r in results)
            {
                foreach (var l in r.Shot.Layers)
                    sb.Append("| ").Append(Cell(r.Shot.Subject + " " + r.Shot.Level))
                      .Append(" | ").Append(Cell(l.Label))
                      .Append(" | ").Append(Cell(l.Why)).AppendLine(" |");
            }
            sb.AppendLine();

            sb.AppendLine("## Notes and caveats");
            sb.AppendLine();
            sb.AppendLine("- **Tower level differences.** The muzzle/cast layer on a catalog tower is " +
                          "level-INVARIANT: `DefenseTower.PlayFireVfx` (DefenseTower.cs:1050) does not read the " +
                          "tier at all. What changes across L1/L2/L3 is (a) the tower MODEL, but only for the two " +
                          "towers whose catalog row authors `repo.upgradeVisualPath` (archer, arcane spire), and " +
                          "(b) the PROJECTILE key, but only for the ground archer (DefenseTower.cs:1156-1161). " +
                          "The level-scaled muzzle burst (TierVfxScale 1.0/1.3/1.7) lives on `TowerCombat` " +
                          "(TowerCombat.cs:392-397), which no catalog tower attaches. So near-identical L1/L2/L3 " +
                          "frames for the catapult, ballista and sky ballista are the code, not the harness.");
            sb.AppendLine("- **Colour tints are not applied.** `VFXManager.ApplyStartColor` " +
                          "(VFXManager.Hovl.cs:441) hue-shifts a recolorable row's particle start colour while " +
                          "preserving its authored saturation and value. It changes the HUE of an effect that is " +
                          "already rendering; it cannot make a broken effect render or an intact one not. " +
                          "Leaving it out keeps this harness a proof of rendering, not a colour reference.");
            sb.AppendLine("- **Projectile layers are shown at authored scale.** The runtime fits a travelling " +
                          "projectile to its tower's range (`ResolveFitScale`, DefenseTower.cs:938), which needs " +
                          "a live `VFXManager.Instance`. That is a SIZE decision, not a rendering one.");
            sb.AppendLine("- **Subject props are context, not evidence.** Tower/building/enemy models are " +
                          "instantiated straight from the per-level Resources path " +
                          "(`StructureFactory.VisualPathForLevel`, StructureFactory.cs:267) and fit to height. " +
                          "The full runtime skin chain (`VisualFactory.Skin` + `TripoMaterialFixer`) needs a " +
                          "`Start()` that edit mode never calls, so a prop may read flatter or paler here than " +
                          "in game. Judge the EFFECT.");
            sb.AppendLine("- **Lighting.** Each stage builds and owns one directional light parented to the " +
                          "stage root and destroyed with it. Ambient is never touched and never relied on.");
            sb.AppendLine("- **What a `[0 material]` null means.** Slot 0 of a `ParticleSystemRenderer` is the " +
                          "particle material itself. A null there is not a spare slot -- the system has nothing " +
                          "to draw its particles with, and Unity will substitute the error shader or draw " +
                          "nothing at all depending on the path. It is always reported and always fails. Slot " +
                          "`[1 trailMaterial]` is the one that is legitimately empty on a non-trailing system.");
            sb.AppendLine("- **Every stage is rebuilt from scratch.** One root `GameObject` per shot, created " +
                          "at the top of the capture and `DestroyImmediate`d in the `finally` -- light, camera, " +
                          "subject prop and every effect instance hang off it. No state carries between shots, " +
                          "so two shots that measure identically are playing identical content.");
            sb.AppendLine();

            if (_heldNotes.Count > 0)
            {
                sb.AppendLine("## Named in the shot list but deliberately NOT captured");
                sb.AppendLine();
                sb.AppendLine("A missing row with a stated reason is honest; a mislabelled PNG is worse than nothing. " +
                              "None of the following was substituted with a different effect.");
                sb.AppendLine();
                foreach (var note in _heldNotes) sb.AppendLine("- " + note);
                sb.AppendLine();
            }

            string path = Path.Combine(OutDir, "INDEX.md");
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
            Debug.Log("[VfxProof] index -> " + Path.GetFullPath(path));
        }

        /// <summary>Markdown table cells cannot contain a raw pipe or newline.</summary>
        private static string Cell(string s)
        {
            if (string.IsNullOrEmpty(s)) return "-";
            return s.Replace("|", "/").Replace("\r", " ").Replace("\n", " ");
        }
    }
}

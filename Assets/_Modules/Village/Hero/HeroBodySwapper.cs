// =============================================================================
// HeroBodySwapper — at scene start, swap the hero's placeholder Wizard body
// for the FBX matching the player's chosen HeroClass (Knight / Ranger / Mage).
// -----------------------------------------------------------------------------
// VillageSceneBuilder bakes the scene with a Wizard placeholder (since the
// builder runs at edit time and doesn't see runtime state). This component
// runs at runtime, reads GameStateService.State.HeroClass, destroys the old
// "HeroBody" child, and instantiates the new mesh in its place. Idempotent —
// only runs once per scene.
//
// THE BODY CHAIN, in order (each step falls to the next; the chain NEVER ends
// with nothing instantiated — Start() has already destroyed the placeholder):
//   1. Knight only: KnightV3.fbx (ff.knightv3) -> KnightPackage.prefab (ff.heropackage)
//   2. any class whose Resources/Heroes/<slug> body RESOLVES: the legacy Resources path.
//      (F8 2026-08-09: Ranger.fbx + Mage.fbx are git-TRACKED since f18b66b4, but the
//      Blink base used to be kicked unconditionally with success terminal — Sylas/Thrain
//      shipped as the bare Blink body while their real models sat unreachable in the tree.)
//   3. remaining classes: the Blink LowPoly base via Addressables ("hero/base/HumanMale"),
//      falling to the legacy Resources path on any failure
//   4. BuildTrackedFallbackBody — the TRACKED KayKit humanoid stage (the floor)
//
// WHY STEP 4 EXISTS (latent P0, fixed 2026-08-05): steps 2 and 3 can both depend on
// assets a given machine lacks (Assets/Blink is gitignored; an FBX import may not have
// synced). On a fresh clone / CI, a Ranger or Mage once fell out of the chain with the
// old body already destroyed: an INVISIBLE hero. A missing art pack must degrade to a
// visible wrong-looking hero, never to nothing.
//
// Resources is the pickup path for the legacy + fallback bodies because it is
// auto-included in player builds (no Addressables wiring needed).
// =============================================================================

using DeNelle.Core.Combat; // ActorAnimator for post-swap battle ready / full anim drive (Village + World)
using DeNelle.Core.State;
using DeNelle.Core.Diagnostics;          // FlowTrace / Guard — §12 instrument the async body-build
using UnityEngine;
using UnityEngine.AddressableAssets;      // Blink base body lives OUTSIDE Resources (gitignored) → Addressables
using UnityEngine.ResourceManagement.AsyncOperations;

namespace DeNelle.Village
{
    [DisallowMultipleComponent]
    public sealed class HeroBodySwapper : MonoBehaviour
    {
        private const float TargetHeightMeters = 1.75f;
        // Global animator playback multiplier for the hero.
        // CLEANUP 2026-07-03: was 0.5f — a band-aid that HALVED all animator playback to hide the
        // OLD bad rig's fast Mixamo clips. With proper AccuRIG/ActorCore mocap (authored at correct
        // speed) that global halving now FIGHTS the good animation. Restored to NATURAL 1.0× so
        // clips play at their authored rate. (old value: 0.5f — restore for the legacy fast rig.)
        private const float HeroAnimSpeed = 1f;

        // WO-376 / WO-557: the swapped hero's guarded animator. The hero re-asserts a clean
        // idle when a conversation starts/ends; we subscribe to OUR dialogue stack's
        // Started/Ended signals (Yarn removed) — no runner to find. Guarded so we hook once.
        private ActorAnimator _heroActor;
        private bool _dialogueHooked;

        // Blink LowPoly base body address (set on the prefab by BlinkAddressableMarker.MarkBlinkGear).
        // The gitignored base body lives OUTSIDE Resources, so it loads via Addressables — NOT
        // Resources.Load. Every class shares this one rig; the class only picks the DEFAULT armor.
        private const string BlinkBaseBodyAddress = "hero/base/HumanMale";

        // The async base-body load handle — ONE owner (this component); released on OnDestroy so the
        // Blink base prefab never leaks. Open-flag guards a double-release.
        private AsyncOperationHandle<GameObject> _baseBodyHandle;
        private bool _baseBodyHandleOpen;

        private void Start()
        {
            using var _ = FlowTrace.Enter("HeroBody", "Start (Blink base body)");

            HeroClass cls = ResolveHeroClass();
            string slug = SlugFor(cls) ?? "Mage"; // Mage maps to null in SlugFor → controller/abilities default

            // Remove the old (baked placeholder) body FIRST and snapshot its controller as a
            // last-ditch fallback. The Blink base loads async, so we destroy the placeholder now
            // and the new body appears a frame later (the hero root + camera persist).
            RuntimeAnimatorController controllerSnapshot = null;
            var old = transform.Find("HeroBody");
            if (old != null)
            {
                var oldAnim = old.GetComponentInChildren<Animator>();
                if (oldAnim != null) controllerSnapshot = oldAnim.runtimeAnimatorController;
                Destroy(old.gameObject);
            }

            // KNIGHT routes to the existing Tripo ARMORED body via the legacy Resources path
            // (Resources/Heroes/Knight.fbx + Knight.controller + -90° yaw + Tripo material pipeline).
            // The Blink LowPoly base is the BARE male body — wrong for the armored Knight. Skip the
            // Blink base load entirely for the Knight and build the armored body directly.
            if (cls == HeroClass.Knight)
            {
                // OWNER "try this" 2026-07-03: the NEW KnightV3.fbx body (CC/AccuRIG humanoid) supersedes
                // the Paladin package for the Knight. Checked FIRST — when ff.knightv3 is ON (default) load
                // Resources/Heroes/KnightV3.fbx, retarget the shared Knight anims via Knight.controller, and
                // keep its OWN embedded texture. A failed V3 load degrades to the Paladin package / legacy
                // Tripo Knight below (never bodyless; the whole pre-V3 path stays byte-for-byte reachable).
                if (DeNelle.Core.FeatureFlags.KnightV3)
                {
                    FlowTrace.Step("HeroBody", $"class={cls} — ff.knightv3 ON: routing to NEW KnightV3.fbx body.");
                    BuildKnightV3Body(cls, "KnightV3", controllerSnapshot);
                    return;
                }
                // OWNER RULING 2026-07-03: the PALADIN hero package is the new Knight body. When
                // ff.heropackage is ON (default) load the published KnightPackage prefab (Paladin
                // variant that binds KnightPackage.controller + bakes sword/shield/helmet). OFF (or a
                // failed package load) routes to the legacy Tripo armored Knight so the hero is never
                // bodyless and the legacy path stays byte-for-byte reachable.
                if (DeNelle.Core.FeatureFlags.HeroPackage)
                {
                    FlowTrace.Step("HeroBody", $"class={cls} — ff.heropackage ON: routing to PALADIN package body 'KnightPackage'.");
                    BuildPackageHeroBody(cls, "KnightPackage", controllerSnapshot);
                    return;
                }
                FlowTrace.Step("HeroBody", $"class={cls} slug={slug} — ff.heropackage OFF: legacy Tripo armored body (skipping Blink base).");
                BuildLegacyResourcesBody(cls, slug, controllerSnapshot);
                return;
            }

            // F8 2026-08-09 ("Sylas is coming through as a blink"): probe the legacy Resources
            // body FIRST for the non-Knight classes. Ranger.fbx + Mage.fbx landed git-TRACKED on
            // 2026-08-06 (f18b66b4) but the Blink base load below was kicked unconditionally and
            // its SUCCESS was terminal — so the imported bodies were dead code on any machine
            // that has the Blink pack. The probe is synchronous and cheap (HeroAssetLoader is
            // Addressables-first with WaitForCompletion, Resources.Load fallback, both cached)
            // and preserves the old behaviour byte-for-byte on a machine WITHOUT the FBX: the
            // probe misses and the Blink -> legacy -> KayKit chain below runs exactly as before.
            if (DeNelle.Core.HeroAssetLoader.LoadHeroPrefab(slug) != null)
            {
                FlowTrace.Step("HeroBody",
                    $"class={cls} slug={slug} — Resources/Heroes/{slug} RESOLVES: routing to the real class body (Blink base skipped).");
                BuildLegacyResourcesBody(cls, slug, controllerSnapshot);
                return;
            }

            FlowTrace.Step("HeroBody", $"class={cls} slug={slug} — kicking Blink base load '{BlinkBaseBodyAddress}'.");
            BeginBlinkBaseLoad(cls, slug, controllerSnapshot);
        }

        // Kick off the async Addressables load of the Blink LowPoly base body. GUARDED (§12, no
        // silent failure): a throw or a failed handle logs via FlowTrace.Fail and falls back to the
        // legacy Resources/Heroes body so the hero is NEVER left bodyless.
        private void BeginBlinkBaseLoad(HeroClass cls, string slug, RuntimeAnimatorController controllerSnapshot)
        {
            AsyncOperationHandle<GameObject> handle;
            try
            {
                handle = Addressables.LoadAssetAsync<GameObject>(BlinkBaseBodyAddress);
            }
            catch (System.Exception ex)
            {
                FlowTrace.Fail("HeroBody",
                    $"Blink base load threw for '{BlinkBaseBodyAddress}': {ex.GetType().Name}: {ex.Message} — " +
                    "falling back to legacy Resources/Heroes body (no bodyless hero).");
                BuildLegacyResourcesBody(cls, slug, controllerSnapshot);
                return;
            }

            _baseBodyHandle = handle;
            _baseBodyHandleOpen = true;

            handle.Completed += op =>
            {
                if (this == null) return; // destroyed mid-load
                if (op.Status != AsyncOperationStatus.Succeeded || op.Result == null)
                {
                    FlowTrace.Fail("HeroBody",
                        $"Blink base load FAILED for '{BlinkBaseBodyAddress}' (status={op.Status}) — " +
                        "falling back to legacy Resources/Heroes body (no bodyless hero).");
                    ReleaseBaseBodyHandle();
                    BuildLegacyResourcesBody(cls, slug, controllerSnapshot);
                    return;
                }

                // CRITICAL async-ordering (the "sliding statue / null animator" class): ALL post-load
                // wiring runs INSIDE this callback, never synchronously after the async kickoff.
                BuildBlinkHeroBody(op.Result, cls, slug, controllerSnapshot);
            };
        }

        private void ReleaseBaseBodyHandle()
        {
            if (!_baseBodyHandleOpen) return;
            _baseBodyHandleOpen = false;
            if (_baseBodyHandle.IsValid()) Addressables.Release(_baseBodyHandle);
            _baseBodyHandle = default;
        }

        // ─────────────────────────────────────────────────────────────────────────────
        // BLINK PATH: build the playable hero body from the Blink LowPoly base prefab, then run
        // the full post-load wiring. Blink ships CLEAN URP materials + a CLEAN bind pose, so the
        // Tripo/CC5 workarounds (RetargetMaterialsToUrp, ApplyExtractedTexture, ApplyFlatSteelStopgap,
        // ApplyClassTint, the embedded camera/listener strip, PlantFeetOnGround's CC5 sole nudge) are
        // BYPASSED — those methods stay in the file (dead for Blink, reachable for the legacy path).
        // ─────────────────────────────────────────────────────────────────────────────
        private void BuildBlinkHeroBody(GameObject prefab, HeroClass cls, string slug,
                                        RuntimeAnimatorController controllerSnapshot)
        {
            using var _ = FlowTrace.Enter("HeroBody", $"BuildBlinkHeroBody class={cls}");

            // Build through the ONE shared factory (same path the companion + enemies use). Blink
            // ships a clean bind pose, so FixTripoMaterials stays OFF and we do NOT apply a forward
            // yaw guess — the Blink rig exports facing +Z already; AlignBodyFacingToRoot below
            // self-corrects from the skeleton if any residual remains (no hard-coded -90° hack).
            float targetH = (cls == HeroClass.Knight) ? TargetHeightMeters * 1.0286f : TargetHeightMeters;
            GameObject body = null;
            Guard.Try("HeroBody", "VisualFactory.Skin (Blink base)", () =>
            {
                body = VisualFactory.Skin(transform, prefab, new SkinOptions
                {
                    FitHeight = targetH,
                    StripColliders = true,
                    SeatOnGround = true,
                    FixTripoMaterials = false,
                });
            });
            if (body == null)
            {
                FlowTrace.Fail("HeroBody",
                    "VisualFactory.Skin returned null for the Blink base — falling back to legacy Resources body.");
                ReleaseBaseBodyHandle();
                BuildLegacyResourcesBody(cls, slug, controllerSnapshot);
                return;
            }
            body.name = "HeroBody"; // EquipmentController.CacheRig finds it by transform.Find("HeroBody")
            FlowTrace.Step("HeroBody", "Blink base body skinned + named 'HeroBody'.");

            WireHeroBody(body, cls, slug, controllerSnapshot, isBlink: true);
        }

        // LEGACY FALLBACK: the pre-Blink Resources/Heroes/<slug>.fbx body, kept so a missing Blink
        // pack (gitignored / not yet marked Addressable) never leaves the hero bodyless. Runs the
        // full Tripo/CC5 material + facing pipeline as before.
        private void BuildLegacyResourcesBody(HeroClass cls, string slug,
                                              RuntimeAnimatorController controllerSnapshot)
        {
            using var _ = FlowTrace.Enter("HeroBody", $"BuildLegacyResourcesBody slug={slug}");

            // §12: the Resources.Load + Skin were un-Guarded (unlike the Blink path) and a null
            // body bailed SILENTLY — the playable hero went bodyless with no self-report. Guard both
            // and Fail-loud on null so a run pinpoints WHICH step left the hero without a body.
            GameObject prefab = null;
            // WO-545 Tier-1 seam: Addressables-first, Resources-fallback (V1-safe — an unregistered
            // hero address degrades to the exact same Resources/Heroes/<slug> load as before).
            Guard.Try("HeroBody", $"HeroAssetLoader.LoadHeroPrefab {slug}", () =>
            {
                prefab = DeNelle.Core.HeroAssetLoader.LoadHeroPrefab(slug);
            });
            if (prefab == null)
            {
                FlowTrace.Fail("HeroBody",
                    "DEF-229 — Blink base unavailable AND Resources/Heroes/" + slug +
                    ".fbx is MISSING. Degrading to the TRACKED KayKit fallback body (never bodyless). " +
                    "Mark the Blink base Addressable (Defenders -> Catalog -> Mark Blink Gear Addressable) " +
                    "or import the legacy FBX.");
                BuildTrackedFallbackBody(cls, slug, controllerSnapshot);
                return;
            }

            float targetH = (cls == HeroClass.Knight) ? TargetHeightMeters * 1.0286f : TargetHeightMeters;
            // WO-326 / WO-966: Knight LOCKED via Offset Forge 2026-06-23: forwardYaw +15
            // (owner felt-verified). Mage/Ranger legacy FBXs used a guessed -90 that HeroFacingAudit
            // measured as ~94° off travel heading. They skin at identity yaw; WireHeroBody then runs
            // AlignBodyFacingToRoot (shoulder axis = same measure as the audit) to derive residual.
            // Knight keeps the hardcoded seat and SKIPS self-correct (hip-derived mis-reads Tripo).
            float forwardYaw = (cls == HeroClass.Knight) ? 15f : 0f;
            GameObject body = null;
            Guard.Try("HeroBody", "VisualFactory.Skin (legacy Resources)", () =>
            {
                body = VisualFactory.Skin(transform, prefab, new SkinOptions
                {
                    FitHeight = targetH,
                    StripColliders = true,
                    SeatOnGround = true,
                    FixTripoMaterials = false,
                    LocalRotation = Quaternion.Euler(0f, forwardYaw, 0f),
                });
            });
            if (body == null)
            {
                FlowTrace.Fail("HeroBody",
                    $"VisualFactory.Skin returned null for legacy Resources/Heroes/{slug} - degrading to the " +
                    "TRACKED KayKit fallback body (never bodyless).");
                BuildTrackedFallbackBody(cls, slug, controllerSnapshot);
                return;
            }
            body.name = "HeroBody";

            // WO-966: non-Knight legacy needs skeleton-derived facing (shared with dungeon Keeper
            // via the same HeroBody path). Knight stays on the locked +15 with no self-correct.
            WireHeroBody(body, cls, slug, controllerSnapshot, isBlink: false,
                alignFacingToRoot: cls != HeroClass.Knight);
        }

        // ─────────────────────────────────────────────────────────────────────────────
        // TERMINAL FALLBACK (WO-910 latent P0): the LAST body in the chain, and the only one
        // built from assets that are GIT-TRACKED.
        //
        // WHY THIS EXISTS. Every earlier body in the chain lives in a pack that is gitignored
        // (Assets/Blink — .gitignore) or in an FBX a given machine may not have synced
        // (Resources/Heroes bodies — Ranger.fbx and Mage.fbx ARE git-TRACKED since f18b66b4,
        // 2026-08-09 correction; historically they were absent). On a fresh clone / CI / any
        // machine without the Blink pack, a Ranger or Mage once reached BuildLegacyResourcesBody,
        // missed, and RETURNED WITH NOTHING INSTANTIATED — while Start() had ALREADY destroyed
        // the baked placeholder body. The player got an INVISIBLE hero, not a wrong-looking one.
        //
        // A missing art pack must degrade to a VISIBLE WRONG-LOOKING hero, never to nothing.
        // Assets/Resources/NPCs/KayKit/*.fbx is the only humanoid body set that is actually
        // tracked in git (staged by KayKitNpcImporter, Humanoid rig — animationType: 3), so it
        // is the floor. It is deliberately NOT pretty: it is the "your pack is missing" body,
        // and the FlowTrace.Fail above is what tells you so.
        //
        // No further fallback exists below this one, and this method never calls back into
        // BuildLegacyResourcesBody — the chain terminates here.
        // ─────────────────────────────────────────────────────────────────────────────
        private void BuildTrackedFallbackBody(HeroClass cls, string slug,
                                              RuntimeAnimatorController controllerSnapshot)
        {
            using var _ = FlowTrace.Enter("HeroBody", $"BuildTrackedFallbackBody class={cls}");

            string res = KayKitNpcBody.ResourcesRoot + KayKitFallbackSlug(cls);
            GameObject prefab = null;
            Guard.Try("HeroBody", $"load tracked fallback body '{res}'", () =>
            {
                prefab = Resources.Load<GameObject>(res);
            });
            if (prefab == null)
            {
                FlowTrace.Fail("HeroBody",
                    $"TRACKED FALLBACK MISSING at Resources/{res} - the hero IS bodyless and there is no " +
                    "body below this one. The staged KayKit humanoids are git-tracked, so this means the " +
                    "Assets/Resources/NPCs/KayKit stage was deleted; restore it (Defenders/Art KayKit NPC importer).");
                return;
            }

            // KayKit's visual forward is local +Z (same convention AtbCombatantSwapper aims by), and
            // HeroLocomotion drives the root's +Z — so identity rotation is already correct here; no
            // per-rig yaw guess. FitHeight normalizes the KayKit import scale to the hero's height.
            GameObject body = null;
            Guard.Try("HeroBody", $"VisualFactory.Skin (tracked fallback {res})", () =>
            {
                body = VisualFactory.Skin(transform, prefab, new SkinOptions
                {
                    FitHeight = TargetHeightMeters,
                    StripColliders = true,
                    SeatOnGround = true,
                    FixTripoMaterials = false,
                });
            });
            if (body == null)
            {
                FlowTrace.Fail("HeroBody",
                    $"VisualFactory.Skin returned null for the tracked fallback '{res}' - hero left bodyless " +
                    "(nothing below this body in the chain).");
                return;
            }
            body.name = "HeroBody"; // EquipmentController.CacheRig finds it by transform.Find("HeroBody")
            FlowTrace.Step("HeroBody",
                $"TRACKED FALLBACK body '{res}' skinned + named 'HeroBody' - visible degraded hero " +
                $"(class={cls}); the class art pack is absent on this machine.");

            // Wire exactly like the legacy path so the fallback still gets the class controller
            // (Resources/Heroes/<slug>.controller — all four exist and are tracked), the ActorAnimator
            // and the ability loadout. The KayKit rig is Humanoid, so those humanoid clips retarget.
            WireHeroBody(body, cls, slug, controllerSnapshot, isBlink: false);
        }

        // The tracked KayKit stage slug that stands in for each class when its own art pack is
        // absent. Chosen for closest silhouette, and every one of these is git-tracked under
        // Assets/Resources/NPCs/KayKit/ (verified: Paladin_with_Helmet / Ranger / Mage / Cleric).
        private static string KayKitFallbackSlug(HeroClass cls) => cls switch
        {
            HeroClass.Knight => "Paladin_with_Helmet",
            HeroClass.Ranger => "Ranger",
            HeroClass.Mage   => "Mage",
            HeroClass.Cleric => "Cleric",
            _                => "Paladin_with_Helmet",
        };

        // ─────────────────────────────────────────────────────────────────────────────
        // PACKAGE PATH (owner ruling 2026-07-03): build the Knight from the published PALADIN hero
        // package. HeroAssetLoader.LoadHeroPrefab("KnightPackage") resolves Resources/Heroes/
        // KnightPackage.prefab — a variant of Knight_Hero.fbx that already BINDS KnightPackage.controller
        // (full posture-tree) and bakes sword/shield/helmet. VisualFactory.Skin instantiates the prefab
        // WHOLE (Object.Instantiate — verified), so the skinned body's Animator keeps that bound
        // controller; WireHeroBody(usePackage:true) must therefore NOT overwrite it. On ANY load miss we
        // fall back to the legacy Tripo Knight (never bodyless, ff.heropackage=0 parity).
        // ─────────────────────────────────────────────────────────────────────────────
        private void BuildPackageHeroBody(HeroClass cls, string slug,
                                          RuntimeAnimatorController controllerSnapshot)
        {
            using var _ = FlowTrace.Enter("HeroBody", $"BuildPackageHeroBody slug={slug}");

            GameObject prefab = null;
            Guard.Try("HeroBody", $"HeroAssetLoader.LoadHeroPrefab {slug}", () =>
            {
                prefab = DeNelle.Core.HeroAssetLoader.LoadHeroPrefab(slug);
            });
            if (prefab == null)
            {
                FlowTrace.Fail("HeroBody",
                    $"PACKAGE MISS — Paladin package prefab '{slug}' failed to load (Resources/Heroes/" +
                    $"{slug}.prefab missing/unregistered). Falling back to the legacy Tripo Knight body " +
                    "(no bodyless hero). Publish the package prefab or set ff.heropackage=0.");
                BuildLegacyResourcesBody(cls, "Knight", controllerSnapshot);
                return;
            }

            // FACING/HEIGHT (owner felt-tune needed): the Paladin is a DIFFERENT rig from the Tripo
            // Knight (Mixamo, Z-forward) — its correct forward yaw is UNKNOWN. Start at 0 (identity) and
            // FlowTrace it LOUDLY so the owner corrects it after seeing the body. Height starts at the
            // base TargetHeightMeters (the legacy Knight used x1.0286 — a Tripo-specific tweak that does
            // NOT transfer to this rig); Skin's FitHeight normalizes to it regardless of import scale.
            const float PackageForwardYaw = 0f;
            float targetH = TargetHeightMeters;
            FlowTrace.Step("HeroBody",
                $"PACKAGE FORWARD YAW = {PackageForwardYaw} (owner felt-tune needed) | PACKAGE HEIGHT = {targetH}m " +
                "(owner felt-tune; legacy Knight used x1.0286, a Tripo-only tweak not carried to the Paladin rig).");

            GameObject body = null;
            Guard.Try("HeroBody", "VisualFactory.Skin (KnightPackage / Paladin)", () =>
            {
                body = VisualFactory.Skin(transform, prefab, new SkinOptions
                {
                    FitHeight = targetH,
                    StripColliders = true,
                    SeatOnGround = true,
                    FixTripoMaterials = false,
                    LocalRotation = Quaternion.Euler(0f, PackageForwardYaw, 0f),
                });
            });
            if (body == null)
            {
                FlowTrace.Fail("HeroBody",
                    "VisualFactory.Skin returned null for the Paladin package 'KnightPackage' — falling " +
                    "back to the legacy Tripo Knight body (no bodyless hero).");
                BuildLegacyResourcesBody(cls, "Knight", controllerSnapshot);
                return;
            }
            body.name = "HeroBody";
            FlowTrace.Step("HeroBody", "Paladin package body skinned + named 'HeroBody'.");

            WireHeroBody(body, cls, slug, controllerSnapshot, isBlink: false, usePackage: true);
        }

        // ─────────────────────────────────────────────────────────────────────────────
        // KNIGHTV3 PATH (owner "try this" 2026-07-03): build the Knight from the owner's NEW
        // Resources/Heroes/KnightV3.fbx — a Character-Creator / AccuRIG export whose CC_Base_* skeleton
        // Unity auto-maps to a STANDARD humanoid avatar, so it retargets the shared Knight animations via
        // the proven Knight.controller (locomotion + injured + cast states). Unlike the Paladin package,
        // KnightV3 is a RAW FBX (no bound controller), so we LOAD Knight.controller (via the slug we pass
        // to WireHeroBody). It ships its OWN embedded 'Material_Pbr' diffuse (extracted to KnightV3.fbm on
        // import); RetargetMaterialsToUrp (run in WireHeroBody's !isBlink block) converts that to URP and
        // PRESERVES the texture, and ColorPackageBodyIfNullAlbedo only tints slots with NO albedo (texture
        // WINS). On ANY load miss we fall back to the Paladin package / legacy Knight (never bodyless).
        // ─────────────────────────────────────────────────────────────────────────────
        private void BuildKnightV3Body(HeroClass cls, string bodySlug,
                                       RuntimeAnimatorController controllerSnapshot)
        {
            using var _ = FlowTrace.Enter("HeroBody", $"BuildKnightV3Body bodySlug={bodySlug}");

            GameObject prefab = null;
            Guard.Try("HeroBody", $"HeroAssetLoader.LoadHeroPrefab {bodySlug}", () =>
            {
                prefab = DeNelle.Core.HeroAssetLoader.LoadHeroPrefab(bodySlug);
            });
            if (prefab == null)
            {
                FlowTrace.Fail("HeroBody",
                    $"KNIGHTV3 MISS — Resources/Heroes/{bodySlug}.fbx failed to load (not imported / bad meta). " +
                    "Falling back to the Paladin package / legacy Tripo Knight (no bodyless hero). Import KnightV3.fbx " +
                    "(Humanoid) or set ff.knightv3=0.");
                if (DeNelle.Core.FeatureFlags.HeroPackage)
                    BuildPackageHeroBody(cls, "KnightPackage", controllerSnapshot);
                else
                    BuildLegacyResourcesBody(cls, "Knight", controllerSnapshot);
                return;
            }

            // FACING/HEIGHT (owner felt-tune): KnightV3 is a CC/AccuRIG rig. The legacy Tripo Knight's
            // proven forward yaw was +15 and height x1.0286 — start THERE (closest prior Knight feel) and
            // FlowTrace LOUD so the owner corrects it after seeing the body. FitHeight normalizes scale
            // regardless of the FBX's import units, so height is a pure feel knob.
            const float KnightV3ForwardYaw = 15f;
            float targetH = TargetHeightMeters * 1.0286f;
            FlowTrace.Step("HeroBody",
                $"KNIGHTV3 forward yaw={KnightV3ForwardYaw} + height={targetH:0.###}m (owner felt-tune — " +
                "seeded from the legacy Knight; a CC/AccuRIG rig may need a different yaw).");

            GameObject body = null;
            Guard.Try("HeroBody", "VisualFactory.Skin (KnightV3)", () =>
            {
                body = VisualFactory.Skin(transform, prefab, new SkinOptions
                {
                    FitHeight = targetH,
                    StripColliders = true,
                    SeatOnGround = true,
                    FixTripoMaterials = false,
                    LocalRotation = Quaternion.Euler(0f, KnightV3ForwardYaw, 0f),
                });
            });
            if (body == null)
            {
                FlowTrace.Fail("HeroBody",
                    "VisualFactory.Skin returned null for KnightV3 — falling back to the legacy Tripo Knight (no bodyless hero).");
                BuildLegacyResourcesBody(cls, "Knight", controllerSnapshot);
                return;
            }
            body.name = "HeroBody";
            FlowTrace.Step("HeroBody", "KnightV3 body skinned + named 'HeroBody'.");

            // Pass the ability/controller slug "Knight" (NOT the body slug "KnightV3"): the controller +
            // abilities.json loadout resolve under "Knight" (Knight.controller carries the injured/locomotion/
            // cast states). useKnightV3:true keeps the embedded texture (color only fills null-albedo slots)
            // and suppresses the Blink-armor overlay so KnightV3 stays the VISIBLE body.
            WireHeroBody(body, cls, "Knight", controllerSnapshot, isBlink: false, usePackage: false, useKnightV3: true);
        }

        // Shared post-load wiring for BOTH the Blink and legacy body. On the Blink path the
        // Tripo/CC5 material + embedded-strip + sole-nudge steps are skipped (clean assets).
        private void WireHeroBody(GameObject body, HeroClass cls, string slug,
                                  RuntimeAnimatorController controllerSnapshot, bool isBlink,
                                  bool usePackage = false, bool useKnightV3 = false,
                                  bool alignFacingToRoot = false)
        {
            // DEF-232 spawn-time validation: the camera (SmartMobileCamera) follows the hero
            // ROOT by tag; HeroLocomotion drives that same root. The visible body must sit
            // centred over the root (no horizontal offset) or the camera frames empty space
            // beside her. Log LOUDLY if Skin left the body off-centre so this regression is
            // caught at the source instead of re-surfacing as a "camera stays to her right" bug.
            ValidateBodyCentredOverRoot(body);

            // DEF-235: plant the feet on the ground (LEGACY ONLY). The CC5/AccuRIG bodies float a
            // few cm because their SkinnedMeshRenderer bounds carry bind-pose padding. Blink ships a
            // clean bind pose seated by VisualFactory.SeatOnGround, so the CC5 sole nudge is BYPASSED.
            StripRigidbodies(body);
            if (!isBlink)
            {
                PlantFeetOnGround(body);
                // Tripo FBXs embed a CAMERA / AudioListener; strip both so only the hero's follow
                // camera drives the display. Blink prefabs are clean — no embedded artifacts.
                foreach (var cam in body.GetComponentsInChildren<Camera>(true))
                    if (cam != null) Destroy(cam);
                foreach (var al in body.GetComponentsInChildren<AudioListener>(true))
                    if (al != null) Destroy(al);
                // Tripo/CC5 materials are Phong/Lambert/null → retarget to URP. Blink ships clean
                // URP materials already, so this pass is BYPASSED (no double-process).
                RetargetMaterialsToUrp(body);
            }
            // Owner 2026-05-25 "the rig exists, the animation exists" — wire the
            // REAL Walk/Cast animation. The per-class controller is generated by
            // HeroAnimatorSetup (Defenders → Animation → Setup <Class> Animator),
            // which extracts the FBX's embedded NLA takes (Walk = longer take,
            // Cast = shorter) and builds an Idle/Walk/Cast machine on the Speed
            // (float) + Cast (trigger) params HeroLocomotion/HeroAbilities drive.
            //
            // The OLD path snapshotted the *placeholder* body's controller — but
            // the baked placeholder (Wizard.fbx) is MISSING, so its Animator and
            // the snapshot were always null and this whole block was skipped,
            // leaving the hero un-animated (the "sliding statue"). Load the
            // controller directly from Resources/Heroes/<slug>.controller instead;
            // fall back to any carried-over snapshot only if that asset is absent.
            // WO-545 Tier-1 seam: Addressables-first, Resources-fallback (V1-safe).
            var anim = body.GetComponentInChildren<Animator>();
            if (anim == null) anim = body.AddComponent<Animator>();

            RuntimeAnimatorController controller;
            if (usePackage)
            {
                // PACKAGE: the KnightPackage (Paladin) prefab already BINDS KnightPackage.controller and
                // VisualFactory.Skin instantiates the prefab WHOLE (Object.Instantiate), so the live
                // animator already carries that posture-tree controller. It is NOT in Resources, so a
                // LoadHeroController("KnightPackage") would miss — KEEP the prefab's bound controller and
                // do NOT overwrite it below (guarded by usePackage at the assignment).
                controller = anim.runtimeAnimatorController;
                FlowTrace.Step("HeroBody",
                    $"PACKAGE controller kept from prefab Animator: {(controller != null ? controller.name : "<null>")}.");
                if (controller == null)
                    FlowTrace.Fail("HeroBody",
                        "PACKAGE prefab Animator carried NO controller — KnightPackage.prefab must bind " +
                        "KnightPackage.controller. Hero will not animate.");
            }
            else
            {
                // MOCAP LOCOMOTION (owner 2026-07-04, ff.mocaploco): for the KnightV3 body, bind the
                // studio-mocap locomotion twin KnightMocap.controller instead of Knight.controller. It is
                // IDENTICAL except its Locomotion Idle/Walk/Run come from the CC_Base studio-mocap clips
                // (~1:1 retarget vs the lossy cross-rig Mixamo Shared clips — the "off" walk the owner
                // flagged). OFF (or any non-KnightV3 body) binds exactly as before; a missing KnightMocap
                // controller falls back to Knight.controller so the hero is never left un-animated.
                string controllerSlug = slug;
                // DATA CAPTURE (owner 2026-07-04, §12): prove exactly WHY KnightMocap binds or not —
                // log both branch conditions + the raw persisted pref BEFORE the decision, and the
                // resolved controller AFTER the load, so one headless run pinpoints flag-false vs load-null.
                int  mocapPrefRaw = UnityEngine.PlayerPrefs.GetInt("ff.mocaploco", -1);
                bool mocapFlag    = DeNelle.Core.FeatureFlags.MocapLocomotion;
                FlowTrace.Step("HeroBody",
                    $"MOCAP-DECISION: useKnightV3={useKnightV3} mocapFlag={mocapFlag} prefRaw={mocapPrefRaw} slug={slug}");
                if (useKnightV3 && mocapFlag)
                {
                    controllerSlug = "KnightMocap";
                    FlowTrace.Step("HeroBody",
                        "ff.mocaploco ON + KnightV3 — binding KnightMocap.controller (studio-mocap locomotion) instead of Knight.");
                }
                var mocapLoaded = DeNelle.Core.HeroAssetLoader.LoadHeroController(controllerSlug);
                string mocapLoadName = mocapLoaded != null ? mocapLoaded.name : "<null>";
                FlowTrace.Step("HeroBody",
                    $"MOCAP-LOAD: requestedSlug={controllerSlug} loaded={mocapLoadName}");
                controller = mocapLoaded
                             ?? (controllerSlug != slug
                                     ? DeNelle.Core.HeroAssetLoader.LoadHeroController(slug)  // mocap twin absent → Knight
                                     : null)
                             ?? controllerSnapshot;
            }
            // CRITICAL (WO-174 "no walk / statue"): the per-class controllers are built from
            // HUMANOID Mixamo/iClone clips (HeroAnimatorFactory). A Humanoid clip can ONLY pose the
            // rig through an Avatar — with NO avatar the Animator freezes in its bind/T-pose. The
            // Blink base body (and the legacy FBX) carry their generated Humanoid avatar through
            // VisualFactory.Skin (it instantiates the prefab root), so the live Animator normally
            // has a valid avatar here. Self-report if it does NOT, so a run pinpoints the
            // missing-avatar case (the "sliding statue") instead of silently posing the T-pose.
            // GOLD STANDARD (mirror HeroArmorVisual.RetargetToBaseAnimator / VerifyArmorRendersNow):
            // the armor OVERLAY self-protects against a no-avatar / no-controller body, but the BASE
            // body it sits on did NOT — it Warn'd then BOUND ANYWAY onto a guaranteed-T-pose rig. A
            // Humanoid clip can ONLY pose through a valid avatar; with NO avatar OR no controller the
            // playable hero ships as a sliding statue. Treat that as a hard FAIL (not a warn-proceed):
            // self-report loudly, then trigger the legacy Resources fallback (a different body whose
            // own avatar may be valid) ONLY on the Blink path — never bind a known-T-pose rig silently.
            bool avatarOk = anim.avatar != null && anim.avatar.isValid;
            if (!avatarOk || controller == null)
            {
                FlowTrace.Fail("HeroBody",
                    $"body '{body.name}' is un-animatable after Skin (isBlink={isBlink}): " +
                    $"avatarValid={avatarOk}, controller={(controller != null ? controller.name : "<null>")} — " +
                    "a humanoid clip would hold the bind/T-pose (the playable-hero sliding-statue ship path).");

                if (isBlink && avatarOk == false)
                {
                    // The Blink base resolved but carries no valid Humanoid avatar — fall back to the
                    // legacy Resources/Heroes body, whose own generated avatar may bind cleanly. Tear
                    // down the half-built Blink body + release its handle first (no two bodies, no leak).
                    FlowTrace.Warn("HeroBody",
                        "Blink base has no valid avatar — falling back to the legacy Resources body (no T-pose ship).");
                    if (body != null) Destroy(body);
                    ReleaseBaseBodyHandle();
                    BuildLegacyResourcesBody(cls, slug, controllerSnapshot);
                    return;
                }
                // Legacy path (or Blink with a valid avatar but no controller): we have no second body
                // to fall back to. Do NOT silently bind a guaranteed-T-pose. Self-report has fired
                // above; continue only far enough to bind whatever controller exists so a later
                // HeroAnimatorSetup re-run can recover — the deferred verify below still self-reports.
                if (controller == null)
                    FlowTrace.Fail("HeroBody",
                        "No controller at Resources/Heroes/" + slug + ".controller — run " +
                        "Defenders → Animation → Setup " + slug + " Animator. Hero will not animate.");
            }

            // DEF-232 / WO-966 (facing) self-correct.
            // Blink: always (no authored forward yaw on the skin).
            // Legacy Mage/Ranger: also (guessed -90 was ~94° off per HeroFacingAudit); they skin at
            // identity and derive residual here via the SAME shoulder axis the audit uses.
            // Legacy Knight / KnightV3: NEVER — locked +15 seat; hip-derived self-correct was the
            // "walking NORTH but FACING EAST" regression.
            if (isBlink || alignFacingToRoot)
                AlignBodyFacingToRoot(body, anim);
            // PACKAGE: do NOT overwrite — the prefab's bound KnightPackage.controller is already live on
            // this animator (Skin preserved it). Re-assigning would be a no-op here but the guard makes
            // the "baked controller wins" contract explicit. Legacy/Blink still bind their loaded controller.
            if (controller != null && !usePackage)
            {
                anim.runtimeAnimatorController = controller;
            }
            // applyRootMotion=false: the Walk clip's baked root curves would fight
            // HeroLocomotion's Slerp on the parent and the hero would never appear
            // to turn. Root motion off → HeroLocomotion owns movement/rotation; the
            // clip only drives the visible mesh.
            anim.applyRootMotion = false;
            // CLEANUP 2026-07-03: proper AccuRIG/ActorCore mocap is authored at correct speed, so the
            // hero plays at NATURAL 1.0× on EVERY body path (package/knightV3/legacy). HeroAnimSpeed is
            // now 1f, so this resolves to 1f regardless of usePackage — no global multiplier fights the
            // authored animation. (Was: usePackage ? 1f : HeroAnimSpeed, where HeroAnimSpeed=0.5 halved
            // the legacy Mixamo path to hide the old fast rig.)
            float animSpeed = HeroAnimSpeed;
            anim.speed = animSpeed;
            // CLEANUP 2026-07-03: the HeroLocomotionCadence enforcer existed ONLY to felt-tune a knob
            // layered on top of the 0.5× global (its BakedNetRunCadence=1.5 assumed the walk×2/run×3
            // compensation that cancelled the halving). With natural 1.0× playback and the locomotion
            // band timeScales reset to 1.0, there is nothing to compensate — so we do NOT attach it;
            // nothing multiplies playback (anim.speed stays 1.0). Re-enable if a runtime cadence knob is
            // wanted again on the new mocap.
            // HeroLocomotionCadence.Attach(gameObject, anim, animSpeed);
            // Keep animating even when the follow camera frames just past the hero
            // edge, else Unity freezes the rig and it T-poses on re-entry to view.
            anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            anim.keepAnimatorStateOnDisable = true;
            // Rebind reconnects bone references after the FBX instantiate finishes,
            // else the new mesh T-poses for ~10 frames after a swap.
            anim.Rebind();

            // DEFERRED POSE-VERIFY (gold standard: mirror HeroArmorVisual.VerifyPoseThenMaybeRollback):
            // a bound avatar + controller can STILL freeze the playable hero in bind/T-pose if the
            // controller drives no clip for THIS rig. The animator hasn't evaluated yet this frame, so
            // watch the next several frames; if the rig never animates, FlowTrace.Fail so the run
            // self-reports the "playable hero T-pose" — the owner must never be the one to notice.
            if (isActiveAndEnabled && anim != null)
                StartCoroutine(VerifyHeroPoseThenReport(anim, body != null ? body.name : "<null>", isBlink));

            // EquipmentController — activates the real KayKit weapon-mesh equip on the
            // swapped hero. It lives on the hero ROOT (same object as GearLoadout): its
            // CacheRig() finds the Animator via transform.Find("HeroBody") (the body we
            // just swapped/rebound), and Awake/OnEnable resolve GearLoadout via
            // GetComponent<GearLoadout>() — BOTH only resolve from the root. Ensure the
            // GearLoadout exists FIRST (lazy-add, same idiom as line ~274 below) so the
            // controller's OnEnable reads a live loadout and equips on enable. Guard so a
            // re-run never double-adds ([DisallowMultipleComponent] would also reject it).
            if (!TryGetComponent(out GearLoadout equipLoadout)) equipLoadout = gameObject.AddComponent<GearLoadout>();
            equipLoadout.Refresh();
            // PACKAGE (baked sword/shield/helmet — owner F8 2026-07-03 "holding two swords, shield 180°"):
            // tag the hero ROOT with PackageBakedGearMarker BEFORE adding EquipmentController. AddComponent
            // fires the controller's OnEnable→EquipBestForHero SYNCHRONOUSLY, so the marker must already be
            // present for even that FIRST equip to be skipped — otherwise the duplicate KayKit sword attaches
            // before any later flag could stop it. With the marker, EquipmentController SKIPS the weapon-mesh
            // + shield-mesh prop attach entirely (baked Paladin gear wins — no second sword, no wrong-oriented
            // attached shield). Stat/loadout/armor-tint on the controller are unaffected. Legacy Tripo Knight
            // (usePackage=false) is never marked → its attach path stays byte-for-byte intact.
            // KNIGHTV3 (WEAPONS-IN-HANDS 2026-07-04): KnightV3 is a BARE AccuRIG body with NO baked
            // gear — unlike the Paladin package, it WANTS the equipped props. So we do NOT tag it with
            // PackageBakedGearMarker; EquipmentController (added below) attaches the equipped sword
            // (sword_A) to CC_Base_R_Hand and the shield (shield_A) to CC_Base_L_Hand through the
            // humanoid avatar (GetBoneTransform(RightHand/LeftHand) maps to the CC_Base_* grips),
            // seated by the Offset Forge 'sword_A'/'shield_A' entries (AttachmentOffsetRegistry). Only
            // the ARMOR OVERLAY + primitive GearVisualApplier stay suppressed below (they would hide or
            // duplicate the body). Set ff.knightv3=0 to restore the package/legacy gear behavior.
            // The marker describes the CURRENT body, not the hero root forever. A package fallback
            // can be replaced by the bare KnightV3 body on a later rebuild/load. Leaving the old
            // marker on the persistent root makes EquipmentController believe KnightV3 still has a
            // baked shield, so it suppresses the equipped off-hand even though the inventory row is
            // restored correctly. Clear stale package authority before EquipmentController re-seats.
            if (!usePackage && TryGetComponent(out PackageBakedGearMarker staleBakedGearMarker))
            {
                // Destroy is end-of-frame in play mode. Disable synchronously so an existing
                // EquipmentController and the seed's immediate OnGearChanged see the bare-body
                // truth during this same call.
                staleBakedGearMarker.enabled = false;
                Destroy(staleBakedGearMarker);
                FlowTrace.Step("HeroBody",
                    "cleared stale PackageBakedGearMarker for a bare body - equipped weapon/shield props may attach. " +
                    $"frame={Time.frameCount}");
            }
            if (usePackage && GetComponent<PackageBakedGearMarker>() == null)
            {
                gameObject.AddComponent<PackageBakedGearMarker>();
                FlowTrace.Step("HeroBody",
                    "PACKAGE: tagged hero root with PackageBakedGearMarker BEFORE EquipmentController — " +
                    "the controller will SKIP weapon/shield prop attach (baked Paladin gear; de-dupes the second sword). " +
                    $"frame={Time.frameCount}");   // WO-994 probe 2: orders the bake vs the scene-load re-seat
            }
            if (GetComponent<EquipmentController>() == null)
                gameObject.AddComponent<EquipmentController>();
            // Seed the Knight's default SHIELD (off-hand) the first time a Knight body is built —
            // only if nothing's persisted, so it never overrides a player's later choice. The data
            // row 'knight_shield_starter' (category "shield") resolves to the shield_A visual and
            // EquipmentController attaches it to LeftHand on the OnGearChanged this fires (the
            // controller is already added above, so it's subscribed). Knight-specific, like the
            // height tweak — Tripo donor carries no weapon/shield, so we attach them as gear.
            // PACKAGE: the Paladin package BAKES its own sword/shield/helmet into the mesh, so the
            // separate shield prop is NOT seeded (baked wins — item e). Legacy Tripo Knight AND the
            // bare KnightV3 body (WEAPONS-IN-HANDS 2026-07-04) both get it — KnightV3 carries no baked
            // shield, so EquipmentController attaches shield_A to CC_Base_L_Hand from this seed.
            // WO-860 Part A3 — the seed is now DATA-DRIVEN off the shared per-class starter
            // loadout (StarterLoadout.OffHandFor) instead of a hardcoded "if Knight then
            // knight_shield_starter", so WO-861's Ranger (offhand dagger) inherits it with a
            // data row and no code change. Same three guards, same order, unchanged semantics:
            //   * an AUTHORED off-hand must exist for the class (Knight today; null = no seed);
            //   * !usePackage — the Paladin package BAKES its shield into the mesh (a seeded
            //     prop would be a second, wrongly-oriented shield). NOTE the LIVE Knight path
            //     is BuildKnightV3Body -> WireHeroBody(usePackage:false), so this guard PASSES
            //     on the shipped build (ff.knightv3 defaults ON and is checked BEFORE
            //     ff.heropackage in Start()); only a V3 load-miss with ff.heropackage ON reaches
            //     the baked-shield body, where skipping the seed is the correct behaviour;
            //   * EquippedOffHand == null — never override a player's persisted choice. After
            //     WO-860 A1's New-Game prefs clear this is TRUE on a fresh Knight:
            //     GearLoadout.ApplyPersistedEquip reads dotr-equip-offhand-knight, the key was
            //     just deleted so GetString returns null, neither restore branch runs, and the
            //     field is left at its freshly-constructed null. (A2's starter deliberately
            //     seeds only the MAIN hand, so it cannot pre-fill this slot and skip the seed.)
            string starterOffHand = StarterLoadout.OffHandFor(PlayableHeroes.JobKey(cls));
            if (!string.IsNullOrEmpty(starterOffHand) && !usePackage && equipLoadout.EquippedOffHand == null)
                Guard.Try("HeroBody", "seed class default off-hand",
                    () => equipLoadout.EquipOffHandById(starterOffHand));
            else if (!string.IsNullOrEmpty(starterOffHand) && !usePackage)
                FlowTrace.Step("HeroBody",
                    $"off-hand seed SKIPPED - '{equipLoadout.EquippedOffHand.id}' already equipped " +
                    "(persisted player choice wins).");
            // ARMOR RENDER (HeroArmorVisual): show EQUIPPED Blink armor on the BODY by swapping
            // in the full-body armored skinned-mesh and humanoid-retargeting it to THIS hero's
            // animator (hiding the base "HeroBody" while armored, restoring it on unequip). It
            // subscribes to GearLoadout.OnGearChanged and reflects the current EquippedArmor on
            // enable, so the body lights up the moment armor is equipped. Added on the same root
            // as GearLoadout/EquipmentController; [DisallowMultipleComponent] guards a re-run.
            // (The Mage/default body skips the swap above and is registered by HeroControlEnsurer.)
            if (GetComponent<HeroArmorVisual>() == null)
                gameObject.AddComponent<HeroArmorVisual>();

            if (controller != null)
            {
                bool hasSpeedParam = false;
                foreach (var p in anim.parameters)
                {
                    if (p.name == "Speed" && p.type == AnimatorControllerParameterType.Float)
                    { hasSpeedParam = true; break; }
                }
                if (!hasSpeedParam)
                {
                    Debug.LogWarning(
                        "[HeroBodySwapper] Controller '" + controller.name +
                        "' has no Speed (float) parameter — Walk transitions won't fire.");
                }
                else
                {
                    anim.SetFloat("Speed", 0f); // open in Idle, not mid-Walk
                }
            }
            // WO-376 HERO POSE INIT: drive the controller into a clean, UPRIGHT IDLE on
            // scene load (and thus during the dialogue intro that plays right after). Two
            // things were leaving the hero in a bad/hunched non-idle pose on entry:
            //   1. Animator state ENTRY is deferred — right after Rebind()+SetFloat the
            //      controller may not have evaluated into "Locomotion" yet, so the rig
            //      shows its raw resting/bind-adjacent pose for the first frames (the
            //      "hunched/drooping" look the owner sees on load and in dialogue).
            //   2. Stray Any-State combat triggers (Cast/Attack/Victory/WindUp/Hit) left
            //      latched from a prior scene/playthrough yank the hero straight into a
            //      combat/cast pose on spawn.
            // Force the issue: clear those triggers, pin Speed=0, and PLAY the Locomotion
            // (Idle) state at normalized time 0 so the hero is deterministically standing
            // in the relaxed idle from frame one. Guarded by name so a controller without
            // these params/state is a safe no-op. Does NOT touch the Walk/Cast/combat
            // transitions — those still fire from gameplay as before.
            DriveIdlePose(anim);
            // WO-581: hand the LIVE swapped animator to the components that drive it — DIRECTLY,
            // no reflection. HeroBodySwapper, HeroLocomotion and HeroAbilities all live in the
            // DeNelle.Village assembly, so the old name-based reflection (GetField("_animator"))
            // was never necessary — and it silently wrote 0 components whenever the targets weren't
            // on the hero root yet at swap time. The castle-hub hero gets HeroLocomotion from
            // HeroControlEnsurer (whose Ensure() can run AFTER this synchronous Knight swap) and
            // never gets HeroAbilities there at all → the reflection loop matched nothing →
            // "re-cache wrote 0 components" FAIL + a non-animating hero. Replaced with an explicit
            // public SetAnimator(Animator) on both components, called by type (eliminates the whole
            // name-mismatch / timing failure class). The fields were NOT renamed — the loop just
            // ran before the components existed.
            //
            // DEF-221: the BODY slug and the ABILITY slug can differ. The Cleric loads
            // its own body/controller (slug "Cleric") but fires the Mage loadout until a
            // dedicated cleric/healer kit lands — so route its abilities to "Mage".
            // PACKAGE: the body slug is "KnightPackage" but the ability LOADOUT is still "Knight"
            // (abilities.json has no "knightpackage" job) — route it back to "Knight".
            string abilitySlug = (cls == HeroClass.Cleric) ? "Mage"
                               : (usePackage ? "Knight" : slug);
            int recached = 0;
            // HeroLocomotion drives Speed→Walk and is the animation-critical component. ENSURE it
            // exists now (idempotent — HeroControlEnsurer also adds it) so the live animator binds
            // deterministically at swap time, not whenever the ensurer happens to run later.
            if (!TryGetComponent(out HeroLocomotion loco)) loco = gameObject.AddComponent<HeroLocomotion>();
            Guard.Try("HeroBody", "HeroLocomotion.SetAnimator", () => loco.SetAnimator(anim));
            recached++;
            // HeroAbilities fires Cast. It's baked into combat/village scenes but NOT every hub,
            // so recache it only when present (its own CastResolved self-heals the animator too).
            bool hasAbilities = TryGetComponent(out HeroAbilities abilities);
            if (hasAbilities)
            {
                Guard.Try("HeroBody", "HeroAbilities.SetAnimator", () => abilities.SetAnimator(anim));
                // WO-36: bind the hero class so a Knight/Ranger casts its OWN abilities.json
                // loadout (the field defaults to "mage"). abilitySlug routes Cleric → "Mage".
                Guard.Try("HeroBody", "HeroAbilities.SetHeroClass", () => abilities.SetHeroClass(abilitySlug));
                recached++;
            }
            // §12 self-report: recached should never be 0 now (locomotion is ensured above). Keep
            // the guard as defense-in-depth — a 0 here means the locomotion ensure itself failed.
            if (recached == 0)
                FlowTrace.Fail("HeroBody",
                    "Animator re-cache wrote 0 components — HeroLocomotion could not be ensured on the " +
                    "hero root; the hero will not animate.");
            else
                FlowTrace.Step("HeroBody",
                    $"Animator re-cache wrote {recached} components (HeroLocomotion" +
                    (hasAbilities ? " + HeroAbilities" : "") + ") — direct SetAnimator, no reflection.");
            FlowTrace.Step("HeroBody",
                "Animator wired: controller=" +
                $"{(anim.runtimeAnimatorController != null ? anim.runtimeAnimatorController.name : "NULL")}, " +
                $"avatar={(anim.avatar != null ? anim.avatar.name : "none")}, " +
                $"clips={anim.runtimeAnimatorController?.animationClips?.Length ?? 0}, " +
                $"re-cached {recached} component(s) (HeroLocomotion/HeroAbilities).");
            // Owner 2026-05-20: Tripo's Send To Unity feature extracted the
            // Knight basecolor PNG. Copied into Resources/Textures/Knight.png
            // so the runtime can apply the real texture rather than the
            // stale class tint. Same hook works for Ranger if/when its
            // Send To Unity export lands.
            // WO (2026-06-10) KNIGHT SPECKLE — PROPER FIX:
            //   The Knight now routes through the SAME normal texture path as every other class.
            //   The speckle was a wrong-UV atlas (medievalknight3dmodel_basecolor = a different
            //   model); ApplyExtractedTexture now binds the mesh's OWN UV-matched bake
            //   (remesh_12_combined_Bake_Diffuse), so each UV island samples its matching region
            //   and the speckle is gone. The flat-steel stopgap is no longer the routed Knight
            //   path — it is kept ONLY as a fallback for the (unexpected) case where the correct
            //   basecolor fails to load, so the Knight degrades to solid steel rather than the
            //   cyan/grey/null-slot fallback. ApplyExtractedTexture returns false when no diffuse
            //   was bound (load failed / no texPath).
            // TEXTURE / TINT (LEGACY ONLY): the Tripo/CC5 bodies need a runtime material pass
            // (ApplyExtractedTexture → flat-steel/class-tint fallback). Blink bodies ship clean URP
            // materials + their own basecolor, so this whole block is BYPASSED — the Blink hero is
            // textured by its prefab. The methods stay in the file for the legacy path.
            // PACKAGE COLORING ROOT CAUSE (owner F8 2026-07-03 "coloring seems wrong"): for a Knight,
            // ApplyExtractedTexture binds the TRIPO atlas "Heroes/Textures/KnightArmored_basecolor" and
            // (isCc5Combined path) REPLACES every renderer slot with a material built from it. The Paladin
            // is a DIFFERENT mesh with DIFFERENT UVs, so that Tripo atlas sampled through Paladin UVs
            // renders garbage/wrong color. The Paladin ships its OWN materials; RetargetMaterialsToUrp
            // (run above in the !isBlink block) already converts those to URP AND preserves their own
            // textures. So the package must NOT run the Tripo texture/tint pass at all — skip it like Blink.
            if (!isBlink && !usePackage && !useKnightV3)
            {
                bool textured = ApplyExtractedTexture(body, cls);
                if (cls == HeroClass.Knight)
                {
                    // Knight: real human_tank basecolor binds via ApplyExtractedTexture; flat steel
                    // is the load-failed fallback only (degrades to solid armour grey, not cyan).
                    if (!textured)
                        ApplyFlatSteelStopgap(body);
                }
                else
                {
                    // Safety net: untextured legacy body gets a class tint so it isn't solid white.
                    ApplyClassTint(body, cls);
                }
            }
            else if (useKnightV3)
            {
                // KNIGHTV3 (owner "try this"): KnightV3 ships its OWN embedded 'Material_Pbr' diffuse
                // (extracted to KnightV3.fbm on import). RetargetMaterialsToUrp (run above in the !isBlink
                // block) already converted it to URP AND preserved that texture — so the Tripo atlas pass
                // (ApplyExtractedTexture, wrong UVs) is SKIPPED exactly like the package. TEXTURE WINS:
                // ColorPackageBodyIfNullAlbedo only assigns a flat _BaseColor to slots whose albedo is NULL
                // (import/texture load failed), so a correctly-textured KnightV3 is never over-painted.
                FlowTrace.Step("HeroBody",
                    "KNIGHTV3: skipped Tripo texture pipeline — KnightV3 keeps its OWN embedded diffuse " +
                    "(RetargetMaterialsToUrp preserved it). Color fallback only fills null-albedo slots (texture wins).");
                ColorPackageBodyIfNullAlbedo(body);
                AuditPackageAlbedo(body);
            }
            else if (usePackage)
            {
                FlowTrace.Step("HeroBody",
                    "PACKAGE: skipped Tripo texture pipeline (ApplyExtractedTexture/FlatSteel/ClassTint) — " +
                    "the Paladin keeps its OWN materials (RetargetMaterialsToUrp above converts them to URP). " +
                    "Binding the Tripo KnightArmored atlas onto Paladin UVs was the wrong-coloring root.");
                // WO-604 WHITE-HERO FIX (owner F8 2026-07-03): the package path keeps the FBX's OWN imported
                // materials. Knight_Hero.fbx ships ONE material "Paladin_MAT" that references Paladin_diffuse/
                // normal/specular.png — but those textures are NOT shipped (no sibling .fbm/, no external .mat),
                // so the imported URP/Lit material carries a NULL _BaseMap albedo and renders solid WHITE. There
                // is NO texture to load. Instead of texturing, give the material(s) SOLID BASE COLORS (flat
                // low-poly shading — matches the KayKit/Quaternius art direction, reads as intentional). This is
                // a reversible RUNTIME tint (instanced materials, never touches the on-disk asset, needs no bake)
                // and is WebGL-safe (pure _BaseColor, zero texture load). Colors come from the owner-tunable
                // PaladinPalette table below (matched by slot NAME, else per-index fallback).
                // WHITE-HERO ASSET FIX (fleet ticket 2026-07-07): Knight_Hero.fbx actually EMBEDS its
                // 3 Paladin PNGs (binary-scan verified) — TripoAssetPostprocessor.ExtractKnightHeroPackage
                // extracts + remaps them into the FBX materials, so the package body can now arrive
                // TEXTURED. Use the TEXTURE-WINS variant (same as KnightV3): slots with a bound albedo
                // keep it; only null-albedo slots get the PaladinPalette tint. Before the extraction runs
                // this behaves EXACTLY like ColorPackageBody (all slots are null-albedo → all tinted).
                ColorPackageBodyIfNullAlbedo(body);
                // AUDIT (read-only diagnostic): pre-extraction it still reports NULL albedo BY DESIGN — the
                // tint sets _BaseColor, not _BaseMap. Post-extraction the audit proves the texture bound.
                AuditPackageAlbedo(body);
            }
            // DEF: the Ranger/Archer fires arrows via the projectile system but held
            // NOTHING — give him a visible bow. Bow goes in the LEFT (off/bow) hand
            // since HeroAimIK draws with the RightHand IK goal. Cosmetic only; the
            // component null-guards a missing bone and never blocks the hero.
            // Attachment: uses LeftHand bone (via GetBoneTransform); sizing via
            // HeroBowAttachment.BowHeldLength + NormalizeInto (reviewed/fixed to 0.92m).
            if (cls == HeroClass.Ranger)
                HeroBowAttachment.AttachTo(gameObject, body);

            // Default gear + visuals (Chunk default gear + shop): ensure GearLoadout so
            // level-1 catalog entries (knight sword+plate, ranger bow+leather, mage staff+robes,
            // cleric mace+light) are applied at spawn. Then attach visuals via the applier
            // (sword/staff/mace on RightHand; bow skipped in applier -- ranger uses the
            // dedicated HeroBowAttachment on LeftHand to avoid duplicate back+held).
            // Re-apply later from shop equip flows. Applier now uses corrected scales/pos/euler
            // proportional to ~1.8m hero (see GearVisualApplier for exact values).
            if (!TryGetComponent(out GearLoadout loadout)) loadout = gameObject.AddComponent<GearLoadout>();
            loadout.Refresh();

            // CLASS DEFAULT ARMOR (owner pick 2026-06-18): the hero spawns already WEARING the
            // picked Blink set so HeroArmorVisual swaps it onto the playable body at spawn — Knight=
            // Centurion, Ranger=BeastHunter, Mage=Dragonic. It is just the LEVEL-1 default: seeded
            // only when the player has NOT already chosen armor for this class (the per-class equip
            // PlayerPrefs key is absent), so a later manual equip is never overridden on respawn.
            // EquipArmorById persists the choice + fires OnGearChanged (HeroArmorVisual reacts), and
            // intentionally bypasses the weight-class gate (Dragonic is heavy but the Mage default) —
            // this is the owner's explicit cosmetic default, not an auto-best pick.
            // PACKAGE (item e — "baked wins"): the Paladin package IS the hero body with sword/shield/
            // helmet baked into the mesh. Do NOT seed the default Blink armor set (HeroArmorVisual would
            // SWAP OUT the Paladin body for a Blink armored skinned-mesh, hiding the package) and do NOT
            // run the GearVisualApplier weapon-prop attach (it seats a duplicate sword on RightHand).
            // Legacy Tripo Knight keeps both (its donor carries no weapon/armor). NOTE: EquipmentController
            // (added above; logic in EquipmentController.cs, OUTSIDE this silo) still reacts to
            // GearLoadout default gear via OnGearChanged and would attach a KayKit weapon mesh — a
            // follow-up must make that component package-aware for full de-duplication.
            // KNIGHTV3 (owner "try this"): also SKIP the default Blink-armor seed + GearVisualApplier.
            // SeedClassDefaultArmor equips blink_armor_centurion, which HeroArmorVisual would SWAP the
            // KnightV3 body OUT for a Blink armored skinned-mesh — HIDING the very body we are trying to
            // show. Skipping it keeps KnightV3 the VISIBLE hero. Gear (weapon/armor) mapping to KnightV3's
            // CC_Base grip joints is the documented next step; the owner is refining that approach.
            if (!usePackage && !useKnightV3)
            {
                SeedClassDefaultArmor(loadout, cls);
                GearVisualApplier.Apply(body.transform, loadout);
            }
            else
            {
                FlowTrace.Step("HeroBody",
                    (useKnightV3 ? "KNIGHTV3" : "PACKAGE") + ": skipped SeedClassDefaultArmor + GearVisualApplier " +
                    "(keeps the new body VISIBLE — no Blink-armor overlay swap, no duplicate weapon prop). " +
                    "FOLLOW-UP: map gear to the body's grip joints via AttachmentOffsetRegistry.");
            }

            // Ensure the guarded ActorAnimator is on the hero root (finds the Animator on the
            // new "HeroBody" child). This wires the full shared animation pipeline (same as
            // DTT/PatriciaLight): SetLocomotion (speed for idle/move), SetCombatStance (battle
            // ready idle stance vs casual), PlayAttack/PlayCast (class-specific), PlayHit,
            // Die. HeroLocomotion/Abilities already resolve and drive it; re-attach here post-swap
            // so Village + World scenes get correct hero anims (Idle/BattleReady, Movement,
            // Attack/Cast per class, Hit, Death) without falling back to T-pose or legacy only.
            if (!TryGetComponent(out ActorAnimator actor)) actor = gameObject.AddComponent<ActorAnimator>();
            // WO-376: spawn RELAXED, not in a combat/battle-ready stance. The hero loads
            // into a town/dialogue context, not a fight — forcing the combat stance here
            // was the wrong default (the WO is explicit: "Don't force SetPose(Combat) by
            // default"). InCombat is flipped to true by the wave/battle flow when a real
            // threat starts (WaveManager) and back to false on victory; at scene load and
            // during the dialogue intro the hero holds the clean upright idle. Re-assert
            // the idle pose through the guarded ActorAnimator too, in case the body-swap
            // timing means the actor only just resolved its Animator.
            actor.SetCombatStance(false); // relaxed idle on load (waves flip this true)
            actor.SetLocomotion(0f);      // ensure the locomotion blend opens at Idle
            DriveIdlePose(actor.Animator);
            _heroActor = actor;

            // WO-376: HOLD the idle during the dialogue intro. The Yarn DialogueRunner may
            // already be hosted (intro/FTUE) or may be hosted lazily by DialogueService on the
            // first Play; subscribe now if present, else retry next frame so a runner that
            // spins up just after the swap is still hooked. On dialogue start (and complete) we
            // re-pin the relaxed idle so the hero never holds a stale combat/hunched pose while
            // a story line is on screen.
            HookDialogueIdle();

            // ── COSMETICS (WO-992 fix, 2026-08-21) ────────────────────────────────────────
            // THE SEAM THAT DID NOT EXIST. Until today an equipped hero skin — a
            // currency that is earned in play AND SOLD FOR REAL MONEY (packs.json grants 25
            // with Hearth Spark, 50 with Starter's Hand) — changed a save flag and NOTHING the
            // player could see: CosmeticApplier.ApplyCosmetic was called from nowhere and its
            // GUID sat on zero prefabs. This is the call that makes the purchase land.
            //
            // Bound to the hero ROOT, not to `body`, and deliberately: HeroArmorVisual can swap
            // the entire visible mesh out from under us on an armour equip, and the applier
            // re-resolves its renderer set on every Refresh. One appearance owner still owns the
            // BODY (this class / HeroArmorVisual); the applier only re-decorates what it finds.
            //
            // appliesTo is the LOWERCASE class name because that is what cosmetics.json authors
            // ("mage" / "knight" / "ranger") — NOT SlugFor(), whose values are Pascal-case and
            // whose Cleric→Mage ability routing would put the wrong skin on the wrong hero.
            Guard.Try("HeroBody", "attach cosmetic applier",
                () => DeNelle.Cosmetics.CosmeticApplier.Attach(
                          gameObject, "hero", cls.ToString().ToLowerInvariant()));

            FlowTrace.Step("HeroBody",
                $"Hero body wired complete: class={cls} slug={slug} isBlink={isBlink} body='{body.name}'.");
            Debug.Log($"[HeroBodySwapper] Hero body ready ({(isBlink ? "Blink base" : "legacy " + slug + ".fbx")}).");
        }

        /// <summary>
        /// Seed the per-class DEFAULT Blink armor so the hero spawns wearing it (Knight=Centurion,
        /// Ranger=BeastHunter, Mage=Dragonic, Cleric=Centurion). Only applied when the player has NOT
        /// already chosen armor for this class (the GearLoadout per-class equip PlayerPrefs key is
        /// absent) — so a manual equip from the equip UI is never overridden on respawn. Delegates to
        /// GearLoadout.EquipArmorById (persists + fires OnGearChanged for HeroArmorVisual). It is the
        /// LEVEL-1 default only; the player can change armor at any time.
        /// </summary>
        private static void SeedClassDefaultArmor(GearLoadout loadout, HeroClass cls)
        {
            if (loadout == null) return;

            string armorId = DefaultArmorIdFor(cls);
            if (string.IsNullOrEmpty(armorId)) return;

            // Mirror GearLoadout's per-class persistence key (stable contract): if the player has
            // already interacted with this class's armor slot (any value, incl. the none-sentinel),
            // leave their choice alone. Absent key => first spawn => seed the owner's default.
            // Use the SAME job string GearLoadout persists under (HeroAbilities.HeroClass — which
            // routes Cleric→"mage"); fall back to the enum name when no HeroAbilities is present.
            var abilities = loadout.GetComponent<HeroAbilities>();
            string jobKey = abilities != null && !string.IsNullOrEmpty(abilities.HeroClass)
                ? abilities.HeroClass.ToLowerInvariant()
                : ClassKey(cls);
            string key = "dotr-equip-armor-" + jobKey;
            if (PlayerPrefs.HasKey(key))
            {
                FlowTrace.Step("HeroBody",
                    $"SeedClassDefaultArmor: '{key}' already set — keeping the player's armor choice (no reseed).");
                return;
            }

            FlowTrace.Step("HeroBody", $"SeedClassDefaultArmor: equipping default '{armorId}' for {cls}.");
            Guard.Try("HeroBody", $"equip default armor '{armorId}'", () => loadout.EquipArmorById(armorId));
        }

        /// <summary>The owner-picked DEFAULT Blink armor catalog id per class (cosmetic level-1 look).
        /// These blink_armor default ids live in the StreamingAssets gear library
        /// (Assets/StreamingAssets/Data/Canonical/armor.json) and are MERGED into the runtime
        /// Resources copy by DeNelle.Editor.GearCurationExporter (WO-747, additive) so they resolve
        /// at runtime; the visual equips via the Blink Addressables path (loadVia="addressable").</summary>
        private static string DefaultArmorIdFor(HeroClass cls) => cls switch
        {
            HeroClass.Knight => "blink_armor_centurion",    // gear/armor/Centurion_Male
            HeroClass.Ranger => "blink_armor_beasthunter",  // gear/armor/BeastHunter_Male
            HeroClass.Mage   => "blink_armor_dragonic",     // gear/armor/Dragonic_Male
            HeroClass.Cleric => "blink_armor_centurion",    // heavy default (shares the Knight plate)
            _                => null,
        };

        // Lower-cased class key matching GearLoadout.PrefJobKey() (HeroAbilities.HeroClass lower-cased).
        private static string ClassKey(HeroClass cls) => cls.ToString().ToLowerInvariant();

        /// <summary>
        /// WO-557: subscribe to OUR dialogue stack's Started/Ended signals so the hero
        /// re-asserts a clean upright idle whenever a conversation is on screen. Idempotent —
        /// the static events are always available (no runner to find, no retry coroutine).
        /// </summary>
        private void HookDialogueIdle()
        {
            if (_dialogueHooked) return; // already hooked
            DeNelle.Core.Dialogue.DialogueService.Started += OnDialogueIdle;
            DeNelle.Core.Dialogue.DialogueService.Ended   += OnDialogueIdle;
            _dialogueHooked = true;
        }

        /// <summary>WO-376: re-pin the relaxed idle pose when a dialogue starts/completes.</summary>
        private void OnDialogueIdle()
        {
            if (_heroActor == null) return;
            _heroActor.SetCombatStance(false);
            _heroActor.SetLocomotion(0f);
            DriveIdlePose(_heroActor.Animator);
        }

        private void OnDestroy()
        {
            if (_dialogueHooked)
            {
                DeNelle.Core.Dialogue.DialogueService.Started -= OnDialogueIdle;
                DeNelle.Core.Dialogue.DialogueService.Ended   -= OnDialogueIdle;
                _dialogueHooked = false;
            }
            // Release the Blink base-body Addressables handle (ONE owner) so the prefab never leaks.
            ReleaseBaseBodyHandle();
        }

        /// <summary>
        /// WO-376: deterministically pose the freshly-swapped hero in a clean, upright IDLE
        /// for scene load + the dialogue intro that follows. Clears any latched Any-State
        /// combat triggers (so a stale Cast/Attack/Victory from a prior scene can't pull the
        /// hero into a combat pose on spawn), pins the locomotion blend to Speed=0 (Idle clip),
        /// and PLAYs the "Locomotion" state at normalized time 0 so the rig is standing in idle
        /// from the first frame instead of showing its deferred-entry resting/hunched pose.
        /// Every access is name/param guarded — a controller missing a param or the state is a
        /// safe no-op. Leaves the Walk/Cast/combat transitions intact (they fire from gameplay).
        /// </summary>
        /// <summary>
        /// DEFERRED pose-verify for the PLAYABLE BASE BODY (gold standard: mirrors
        /// HeroArmorVisual.VerifyPoseThenMaybeRollback). Over the next several frames confirm the
        /// hero rig is actually PLAYING a clip on layer 0 (GetCurrentAnimatorClipInfoCount(0) > 0),
        /// i.e. NOT frozen in the bind/T-pose. The armor overlay self-protects + rolls back; the base
        /// body has no second overlay to swap to, so here we cannot roll back — instead we FAIL LOUD
        /// (FlowTrace.Fail → break-log) so the run self-reports the "playable hero T-pose" and the
        /// owner is never the one to notice. Robust against a transient first-frame 0 (only reports
        /// when NOTHING ever drives the rig within the window).
        /// </summary>
        private System.Collections.IEnumerator VerifyHeroPoseThenReport(Animator anim, string bodyName, bool isBlink)
        {
            const int MaxPoseFrames = 6;
            bool everPlayed = false;
            int lastCount = -1;
            for (int i = 0; i < MaxPoseFrames; i++)
            {
                yield return null;
                if (this == null || anim == null) yield break;
                if (anim.isActiveAndEnabled && anim.runtimeAnimatorController != null)
                {
                    lastCount = anim.GetCurrentAnimatorClipInfoCount(0);
                    if (lastCount > 0) { everPlayed = true; break; }
                }
            }

            FlowTrace.Step("HeroBody",
                $"VerifyHeroPose body='{bodyName}' isBlink={isBlink}: everPlayed={everPlayed} " +
                $"lastClipCount={lastCount} (>=1 means an animation drives the rig; otherwise frozen T-pose).");

            if (!everPlayed)
                FlowTrace.Fail("HeroBody",
                    $"PLAYABLE HERO T-POSE: body '{bodyName}' (isBlink={isBlink}) never animated within " +
                    $"{MaxPoseFrames} frames (lastClipCount={lastCount}) — the controller drives no clip for " +
                    "this rig (missing/invalid avatar, no Locomotion state, or wrong controller). The hero " +
                    "ships as a sliding statue. Re-run Defenders → Animation → Setup <Class> Animator / verify the rig's avatar.");
        }

        private static void DriveIdlePose(Animator anim)
        {
            if (anim == null || anim.runtimeAnimatorController == null) return;

            // Snapshot which trigger/float params this controller actually declares so we
            // never poke a missing parameter (the project's well-documented param-spam pitfall).
            bool hasSpeed = false, hasCast = false, hasAttack = false,
                 hasVictory = false, hasWindUp = false, hasHit = false;
            foreach (var p in anim.parameters)
            {
                switch (p.name)
                {
                    case "Speed":   if (p.type == AnimatorControllerParameterType.Float)   hasSpeed   = true; break;
                    case "Cast":    if (p.type == AnimatorControllerParameterType.Trigger) hasCast    = true; break;
                    case "Attack":  if (p.type == AnimatorControllerParameterType.Trigger) hasAttack  = true; break;
                    case "Victory": if (p.type == AnimatorControllerParameterType.Trigger) hasVictory = true; break;
                    case "WindUp":  if (p.type == AnimatorControllerParameterType.Trigger) hasWindUp  = true; break;
                    case "Hit":     if (p.type == AnimatorControllerParameterType.Trigger) hasHit     = true; break;
                }
            }

            // Clear stray Any-State combat triggers so none of them yanks the hero out of
            // Locomotion the moment the controller starts evaluating.
            if (hasCast)    anim.ResetTrigger("Cast");
            if (hasAttack)  anim.ResetTrigger("Attack");
            if (hasVictory) anim.ResetTrigger("Victory");
            if (hasWindUp)  anim.ResetTrigger("WindUp");
            if (hasHit)     anim.ResetTrigger("Hit");

            // Pin the locomotion blend to Idle and force the state to be entered NOW, so the
            // rig stands in the idle pose from frame zero instead of its deferred-entry pose.
            if (hasSpeed) anim.SetFloat("Speed", 0f);
            int locoHash = Animator.StringToHash("Locomotion");
            if (anim.HasState(0, locoHash))
                anim.Play(locoHash, 0, 0f);
            anim.Update(0f); // evaluate immediately so the idle pose shows this frame
        }

        private static HeroClass ResolveHeroClass()
        {
            // The body builder TRUSTS the persisted HeroClass — the SOURCE layer guarantees it:
            // a hero pick is persisted at SELECTION (HeroSelectController.OnCardClicked →
            // GameStateService.ChooseHero) and at the start-flow entry, before any bypass routing.
            // No KnightOnly force here (band-aid removed 2026-06-23) — if the persisted value is
            // ever unset at body-build, that is a SOURCE regression: we scream (FlowTrace.Fail) and
            // fall back to V1's single hero (Knight) rather than silently rendering a wrong class.
            var svc = GameStateService.Instance;
            var opt = svc != null ? svc.State?.HeroClass.ToNullable() : null;
            if (opt.HasValue) return opt.Value;
            DeNelle.Core.Diagnostics.FlowTrace.Fail("HeroBody",
                "ResolveHeroClass: HeroClass was UNSET at body-build — source (selection/start-flow) " +
                "failed to persist it. Defaulting to Knight (V1). This is a SOURCE regression to fix.");
            return HeroClass.Knight;
        }

        /// <summary>
        /// Tripo FBXs come in with Phong / Lambert / Blinn materials that URP
        /// can't render — the mesh shows as a transparent magenta ghost. Walk
        /// the renderers, pull each material's diffuse / normal / emission
        /// (whichever property names happen to be present on the source), and
        /// rebuild under URP/Lit. Falls through to URP/Simple Lit (cheaper on
        /// mobile) then Standard as a final safety net so the hero is never
        /// rendered with a null shader.
        /// </summary>
        private static void RetargetMaterialsToUrp(GameObject body)
        {
            Shader litShader = Shader.Find("Universal Render Pipeline/Lit")
                            ?? Shader.Find("Universal Render Pipeline/Simple Lit")
                            ?? Shader.Find("Standard");
            if (litShader == null)
            {
                Debug.LogWarning("[HeroBodySwapper] No URP/Lit or Standard shader available — material fix skipped.");
                return;
            }

            int converted = 0, skipped = 0;
            foreach (var r in body.GetComponentsInChildren<Renderer>(true))
            {
                if (r == null) continue;
                var mats = r.sharedMaterials;
                if (mats == null) continue;
                for (int i = 0; i < mats.Length; i++)
                {
                    var src = mats[i];
                    if (src == null) continue;
                    // Already a URP shader? Preserve — upstream art may have
                    // intentionally placed URP/Unlit on emissive accents etc.
                    string srcShaderName = src.shader != null ? src.shader.name : "";
                    if (srcShaderName.StartsWith("Universal Render Pipeline/", System.StringComparison.Ordinal))
                    { skipped++; continue; }

                    // Diffuse / base texture — SHEET read first (_BaseMap/_MainTex without the
                    // HasProperty gate; see SheetAlbedo header): the gate is shader-dependent and a
                    // shaderless (-nographics / stripped) source would otherwise convert to a
                    // textureless white URP material, silently DESTROYING a bound albedo.
                    Texture baseTex = SheetAlbedo(src);
                    if (baseTex == null && src.HasProperty("_BaseColorMap")) baseTex = src.GetTexture("_BaseColorMap");

                    // Base colour — try URP first, then Built-in.
                    Color baseColor = Color.white;
                    if (src.HasProperty("_BaseColor"))      baseColor = src.GetColor("_BaseColor");
                    else if (src.HasProperty("_Color"))     baseColor = src.GetColor("_Color");

                    // Normal + emission, if the source authored them.
                    Texture normalTex = null;
                    if (src.HasProperty("_BumpMap"))                   normalTex = src.GetTexture("_BumpMap");
                    if (normalTex == null && src.HasProperty("_NormalMap")) normalTex = src.GetTexture("_NormalMap");
                    Texture emissionTex = null;
                    Color   emissionColor = Color.black;
                    if (src.HasProperty("_EmissionMap"))   emissionTex   = src.GetTexture("_EmissionMap");
                    if (src.HasProperty("_EmissionColor")) emissionColor = src.GetColor("_EmissionColor");

                    // Tiling / offset for whichever map slot held the diffuse.
                    Vector2 tiling = Vector2.one, offset = Vector2.zero;
                    if (src.HasProperty("_MainTex"))
                    { tiling = src.GetTextureScale("_MainTex"); offset = src.GetTextureOffset("_MainTex"); }
                    else if (src.HasProperty("_BaseMap"))
                    { tiling = src.GetTextureScale("_BaseMap"); offset = src.GetTextureOffset("_BaseMap"); }

                    var newMat = new Material(litShader);
                    newMat.name = (src.name ?? "Tripo") + " (URP)";
                    if (newMat.HasProperty("_BaseColor")) newMat.SetColor("_BaseColor", baseColor);
                    if (newMat.HasProperty("_Color"))     newMat.SetColor("_Color",     baseColor);
                    if (baseTex != null)
                    {
                        if (newMat.HasProperty("_BaseMap"))
                        { newMat.SetTexture("_BaseMap", baseTex); newMat.SetTextureScale("_BaseMap", tiling); newMat.SetTextureOffset("_BaseMap", offset); }
                        if (newMat.HasProperty("_MainTex"))
                        { newMat.SetTexture("_MainTex", baseTex); newMat.SetTextureScale("_MainTex", tiling); newMat.SetTextureOffset("_MainTex", offset); }
                    }
                    if (normalTex != null && newMat.HasProperty("_BumpMap"))
                    {
                        newMat.SetTexture("_BumpMap", normalTex);
                        newMat.EnableKeyword("_NORMALMAP");
                    }
                    if (emissionTex != null || emissionColor.maxColorComponent > 0.001f)
                    {
                        if (newMat.HasProperty("_EmissionColor")) newMat.SetColor("_EmissionColor", emissionColor);
                        if (emissionTex != null && newMat.HasProperty("_EmissionMap"))
                            newMat.SetTexture("_EmissionMap", emissionTex);
                        newMat.EnableKeyword("_EMISSION");
                        newMat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
                    }
                    // DEF-6: Tripo FBXs frequently export with inverted face
                    // normals. URP's default back-face cull (Cull=2) renders
                    // the inside surfaces, producing the "dark purple spiky
                    // creature" seen in screenshots 1-3. Disabling culling
                    // (Cull=0 = Off) renders both sides so the mesh is visible
                    // regardless of which way its normals point.
                    if (newMat.HasProperty("_Cull"))
                        newMat.SetFloat("_Cull", 0f); // 0 = Off (double-sided)

                    if (newMat.HasProperty("_Smoothness")) newMat.SetFloat("_Smoothness", 0.15f);
                    if (newMat.HasProperty("_Metallic"))   newMat.SetFloat("_Metallic",   0f);
                    mats[i] = newMat;
                    converted++;
                }
                r.sharedMaterials = mats;
            }
            Debug.Log("[HeroBodySwapper] RetargetMaterialsToUrp: converted=" +
                      converted + ", skipped (already URP)=" + skipped);
        }

        /// <summary>
        /// WO-604 §12 white-hero audit for the PACKAGE (baked Paladin) path. Scans every renderer
        /// material on the built body and counts how many carry a NON-NULL albedo (_BaseMap / _MainTex).
        /// The package path keeps the FBX's OWN imported materials; if Knight_Hero.fbx ships textureless
        /// materials (no extracted .fbm/, no external .mat) the albedo is NULL and the hero renders solid
        /// WHITE. FlowTrace.Fail LOUDLY when zero materials have an albedo so a capture PROVES the
        /// textureless-material cause (the fix is asset-side: extract the Paladin albedo to a reliably
        /// Resources-loadable folder and bind it, mirroring ApplyExtractedTexture). Read-only — never
        /// mutates a material, so it cannot regress a correctly-textured package body.
        /// </summary>
        // ── WHITE-PALADIN PROBE SEAMS (AutoPilot 'AssertHeroHasAlbedo') — results of the
        // LAST AuditPackageAlbedo run, readable by the fleet probe. The WHITE HERO ROOT
        // check already runs AFTER the binding applies (audit is called immediately after
        // ColorPackageBodyIfNullAlbedo on both the KnightV3 + package paths) and early-outs
        // when a texture OR tint is bound — these seams let a probe PROVE that each run.
        /// <summary>True once the audit has run at least once this session.</summary>
        public static bool LastAlbedoAuditRan { get; private set; }
        /// <summary>Materials with a bound _BaseMap/_MainTex in the last audit.</summary>
        public static int LastAlbedoAuditBound { get; private set; }
        /// <summary>Total materials scanned in the last audit.</summary>
        public static int LastAlbedoAuditTotal { get; private set; }
        /// <summary>True if the last audit emitted the WHITE HERO ROOT Fail (no texture AND no tint).</summary>
        public static bool LastAuditWhiteHeroRootFired { get; private set; }

        // ─────────────────────────────────────────────────────────────────────────────
        // -NOGRAPHICS-SAFE MATERIAL READS (fleet WHITE-HERO RCA 2026-07-08, §12-proven).
        // The AutoPilot fleet launches the player with -nographics (run-autopilot-fleet.ps1:80).
        // With no graphics device, SHADERS DO NOT RESOLVE in that player (same break-log run:
        // "Could not find video decode shader pass Default in shader <not found>",
        // "RenderTexture.Create failed") — and Material.HasProperty consults the SHADER's
        // property table, so EVERY HasProperty-gated read/write in this file was dead in the
        // fleet. That is the whole "albedo 0/19 + default-white _BaseColor" 4/4 capture: the
        // very same imported KnightV3 materials read 19/19 in the windowed player of the same
        // import state (Player.log 2026-07-07 22:34 — "RetargetMaterialsToUrp: converted=0,
        // skipped (already URP)=17" + "PACKAGE albedo audit: 19/19"; KnightV3 assets unchanged
        // since commit f4397b7b 2026-07-03, no reimports in any session log). Proof it was the
        // property gate and not a lost binding: had the final materials carried a live shader,
        // ColorPackageBodyIfNullAlbedo would have tinted them and the audit would have logged
        // the "textureless but _BaseColor-tinted" Warn — instead the Fail fired, which requires
        // texture reads AND tint reads AND tint writes to ALL be dead at once.
        //
        // Material.GetTexture/GetColor/SetColor/SetFloat operate on the material's OWN
        // serialized property sheet, loaded with or without a graphics device — so reading the
        // sheet measures the actual shipped BINDING. A genuinely unbound slot still reads null
        // and still FAILS the audit: the check is environment-robust, not weakened.
        // ─────────────────────────────────────────────────────────────────────────────
        private static Texture SheetAlbedo(Material m)
        {
            if (m == null) return null;
            // GUARD the benign console error (owner device capture 2026-07-16): a built-in Standard/Legacy
            // shader material has no '_BaseMap', so Material.GetTexture("_BaseMap") logs
            // "Material '<name>' with Shader 'Standard' doesn't have a texture property '_BaseMap'" on a
            // real GPU. This fires when an un-recovered weapon prop (e.g. 'LowPolyWeaponMegaPack' on the
            // Standard shader) is swept into the body hierarchy by GetComponentsInChildren. Short-circuit
            // those by shader NAME (name is serialized, so it holds in the -nographics fleet too) — returns
            // null exactly like an unbound URP slot. The URP sheet-read below is UNCHANGED, so the
            // -nographics TEXTURE-WINS contract for real body materials is preserved. (Note: with the
            // EquipmentController weapon-material recovery, the weapon arrives as URP/Lit and never trips
            // this guard — this is defense-in-depth for any residual Standard-shader prop.)
            Shader sh = m.shader;
            if (sh != null)
            {
                string sn = sh.name;
                if (sn == "Standard" || sn == "Standard (Specular setup)"
                    || sn.StartsWith("Legacy Shaders/")
                    || sn.IndexOf("InternalError", System.StringComparison.Ordinal) >= 0)
                    return null;
            }
            Texture t = m.GetTexture("_BaseMap");   // sheet read — deliberately NOT HasProperty-gated
            if (t == null) t = m.GetTexture("_MainTex");
            return t;
        }

        /// <summary>Sheet-based tint probe: non-white with any alpha counts as a deliberate tint.
        /// An absent sheet entry reads back as clear (a=0), so an untinted material can never
        /// masquerade as tinted.</summary>
        private static bool IsSheetTinted(Material m, string prop)
        {
            if (m == null) return false;
            Color c = m.GetColor(prop);             // sheet read — returns clear when absent
            return c.a > 0f && c != Color.white;
        }

        public static void AuditPackageAlbedo(GameObject body)
        {
            if (body == null) return;
            LastAlbedoAuditRan = true;
            LastAuditWhiteHeroRootFired = false;
            int mats = 0, withAlbedo = 0;
            string firstMatDiag = null; // §12 capture: shader state proves headless vs real-GPU per run
            var missing = new System.Text.StringBuilder();
            foreach (var r in body.GetComponentsInChildren<Renderer>(true))
            {
                if (r == null) continue;
                foreach (var m in r.sharedMaterials)
                {
                    if (m == null) continue;
                    mats++;
                    if (firstMatDiag == null)
                        firstMatDiag = $"first-mat '{m.name}' shader=" +
                                       (m.shader != null ? $"'{m.shader.name}'" : "<null — headless/-nographics>");
                    // Sheet read, NOT HasProperty-gated: HasProperty consults the SHADER, which does
                    // not resolve in the -nographics fleet player (the 4/4 false "0/19" root, 2026-07-08).
                    Texture albedo = SheetAlbedo(m);
                    if (albedo != null) withAlbedo++;
                    else if (missing.Length < 200)
                    { if (missing.Length > 0) missing.Append(", "); missing.Append(m.name); }
                }
            }

            LastAlbedoAuditBound = withAlbedo;
            LastAlbedoAuditTotal = mats;

            FlowTrace.Step("HeroBody",
                $"PACKAGE albedo audit: {withAlbedo}/{mats} material(s) carry a _BaseMap/_MainTex texture" +
                (missing.Length > 0 ? $" (textureless: [{missing}])" : "") +
                (firstMatDiag != null ? $" | {firstMatDiag}" : "") + ".");

            // NULL-albedo package materials are EXPECTED — ColorPackageBody tints them via
            // _BaseColor (PaladinPalette), so the hero renders COLORED, not white. Only a
            // break-log Fail if the tint ALSO failed (a material with neither texture nor a
            // non-default base color would truly render white). Was an unconditional Fail —
            // polluted every fleet run 12/12 with a false WHITE HERO ROOT (2026-07-06 audit).
            if (mats > 0 && withAlbedo == 0)
            {
                bool anyTinted = false;
                foreach (var r in body.GetComponentsInChildren<Renderer>(true))
                {
                    if (r == null) continue;
                    foreach (var m in r.sharedMaterials)
                        // Sheet-based tint read (see SheetAlbedo header): the HasProperty gate is
                        // shader-dependent and dead in the -nographics fleet, which made a TINTED
                        // body read as untinted and escalated the Warn into the false WHITE HERO ROOT.
                        if (m != null && (IsSheetTinted(m, "_BaseColor") || IsSheetTinted(m, "_Color")))
                        { anyTinted = true; break; }
                    if (anyTinted) break;
                }
                if (anyTinted)
                    FlowTrace.Warn("HeroBody",
                        $"PACKAGE body: all {mats} material(s) are textureless but _BaseColor-tinted " +
                        "(PaladinPalette) — colored-not-textured is the WO-604 interim look, not a white hero.");
                else
                {
                    LastAuditWhiteHeroRootFired = true;   // probe-readable — proves the residual false-alarm class stays dead
                    FlowTrace.Fail("HeroBody",
                        $"WHITE HERO ROOT: all {mats} package-body material(s) have NULL albedo AND default-white " +
                        "_BaseColor — the Paladin genuinely renders solid white. FIX (asset-side): extract the " +
                        "Paladin albedo to a Resources-loadable folder and bind it here (mirror " +
                        "ApplyExtractedTexture), or give KnightPackage.prefab materials a shipped albedo texture.");
                }
            }
        }

        // ─────────────────────────────────────────────────────────────────────────────
        // OWNER-TUNABLE PALADIN PALETTE (WO-604 white-hero fix). The Paladin package FBX
        // (Knight_Hero.fbx) ships ONE material "Paladin_MAT" with a NULL albedo → solid WHITE.
        // ColorPackageBody gives every body material a SOLID _BaseColor (flat low-poly shading,
        // KayKit/Quaternius art direction) instead of a texture — WebGL-safe, reversible runtime
        // tint, no bake. TUNE the RGB values here; nothing else to touch.
        //
        // Matching order per slot: material NAME keyword first (future-proof if the Paladin is
        // ever split into skin/plate/leather/tabard slots), else the per-index fallback palette,
        // else PaladinDefaultColor. The current single "Paladin_MAT" slot matches "paladin" →
        // steel-plate, so the one-material Paladin reads as clean plate armor.
        // ─────────────────────────────────────────────────────────────────────────────
        private struct PaladinColorSlot { public string[] Keywords; public Color Color; public string Label; }

        private static readonly PaladinColorSlot[] PaladinPalette =
        {
            new PaladinColorSlot { Label = "steel-plate", Color = (Color)new Color32(0x74, 0x7C, 0x88, 0xFF),
                Keywords = new[] { "plate", "armor", "armour", "metal", "steel", "paladin", "body", "main" } },
            new PaladinColorSlot { Label = "flesh-skin",  Color = (Color)new Color32(0xD8, 0xA0, 0x7C, 0xFF),
                Keywords = new[] { "skin", "face", "head", "flesh", "hand" } },
            new PaladinColorSlot { Label = "leather",     Color = (Color)new Color32(0x6B, 0x47, 0x2E, 0xFF),
                Keywords = new[] { "leather", "belt", "strap", "boot", "glove" } },
            new PaladinColorSlot { Label = "tabard",      Color = (Color)new Color32(0x8C, 0x2B, 0x2B, 0xFF),
                Keywords = new[] { "tabard", "cloth", "cloak", "cape", "tunic", "fabric", "surcoat" } },
            new PaladinColorSlot { Label = "dark-trim",   Color = (Color)new Color32(0x33, 0x37, 0x40, 0xFF),
                Keywords = new[] { "trim", "iron", "dark", "accent" } },
            new PaladinColorSlot { Label = "gold",        Color = (Color)new Color32(0xC9, 0x9B, 0x3E, 0xFF),
                Keywords = new[] { "gold", "brass", "emblem", "crest" } },
        };

        // Per-index fallback when a slot NAME gives no hint (cycles). Steel-plate first so a
        // single-slot Paladin reads as clean plate; accents follow for any extra split slots.
        private static readonly Color[] PaladinIndexFallback =
        {
            (Color)new Color32(0x74, 0x7C, 0x88, 0xFF), // steel plate
            (Color)new Color32(0x8C, 0x2B, 0x2B, 0xFF), // tabard crimson accent
            (Color)new Color32(0x6B, 0x47, 0x2E, 0xFF), // leather brown
            (Color)new Color32(0x33, 0x37, 0x40, 0xFF), // dark metal trim
        };

        private static readonly Color PaladinDefaultColor = (Color)new Color32(0x74, 0x7C, 0x88, 0xFF); // steel

        /// <summary>
        /// WO-604 WHITE-HERO FIX: color the textureless Paladin package body by assigning a SOLID
        /// _BaseColor to each material slot (flat low-poly shading — the fix is COLOR, not texture).
        /// Operates on INSTANCED material copies (new Material(src)) so the imported on-disk asset is
        /// never mutated — the tint is a reversible RUNTIME change with no asset bake. WebGL-safe:
        /// pure _BaseColor, no texture load. Colors resolve from <see cref="PaladinPalette"/> by slot
        /// NAME, else the per-index fallback. Null-guarded on every renderer/material access (§10).
        /// </summary>
        private static void ColorPackageBody(GameObject body)
        {
            if (body == null) return;
            int slotIndex = 0, colored = 0;
            var log = new System.Text.StringBuilder();
            foreach (var r in body.GetComponentsInChildren<Renderer>(true))
            {
                if (r == null) continue;
                var mats = r.sharedMaterials;
                if (mats == null) continue;
                for (int i = 0; i < mats.Length; i++)
                {
                    var src = mats[i];
                    if (src == null) continue;
                    // Instance copy — tint the live material, never the imported asset on disk
                    // (reversible, bake-free). Mirrors RetargetMaterialsToUrp's new-Material pattern.
                    var inst = new Material(src);
                    string slotName = src.name ?? "";
                    Color c = ResolvePaladinColor(slotName, slotIndex, out string label);
                    if (inst.HasProperty("_BaseColor")) inst.SetColor("_BaseColor", c);
                    if (inst.HasProperty("_Color"))     inst.SetColor("_Color", c);
                    // Flat matte low-poly read (no texture → pure color); non-metallic so it doesn't
                    // blow out to white under the scene lights the way a default metallic slot would.
                    if (inst.HasProperty("_Smoothness")) inst.SetFloat("_Smoothness", 0.1f);
                    if (inst.HasProperty("_Metallic"))   inst.SetFloat("_Metallic", 0f);
                    mats[i] = inst;
                    colored++;
                    if (log.Length < 300)
                    { if (log.Length > 0) log.Append(", "); log.Append($"[{slotIndex}] '{slotName}'→{label}"); }
                    slotIndex++;
                }
                r.sharedMaterials = mats;
            }
            FlowTrace.Step("HeroBody",
                $"PACKAGE coloring: set solid _BaseColor on {colored} material slot(s) — {log}. " +
                "The white Paladin now reads as a flat-colored hero (WebGL-safe, no texture load).");
        }

        /// <summary>
        /// KNIGHTV3 (owner "try this") TEXTURE-WINS color fallback. Unlike <see cref="ColorPackageBody"/>
        /// (which paints EVERY slot because the Paladin is textureless), this ONLY assigns a flat
        /// _BaseColor to material slots whose albedo (_BaseMap / _MainTex) is NULL — i.e. slots where the
        /// KnightV3 embedded diffuse failed to import/bind. A correctly-textured KnightV3 therefore keeps
        /// its OWN texture untouched; only a broken slot degrades to a solid color instead of solid white.
        /// Operates on INSTANCED material copies (reversible runtime tint, no on-disk mutation, no bake) and
        /// is WebGL-safe (pure _BaseColor). Colors resolve via <see cref="ResolvePaladinColor"/>.
        /// </summary>
        private static void ColorPackageBodyIfNullAlbedo(GameObject body)
        {
            if (body == null) return;
            int slotIndex = 0, colored = 0, keptTextured = 0;
            var log = new System.Text.StringBuilder();
            foreach (var r in body.GetComponentsInChildren<Renderer>(true))
            {
                if (r == null) continue;
                var mats = r.sharedMaterials;
                if (mats == null) continue;
                for (int i = 0; i < mats.Length; i++)
                {
                    var src = mats[i];
                    if (src == null) continue;
                    // Sheet read (see SheetAlbedo header): TEXTURE-WINS must hold in the -nographics
                    // fleet too — the HasProperty-gated read saw NULL there and over-painted textured slots.
                    Texture albedo = SheetAlbedo(src);
                    if (albedo != null) { keptTextured++; slotIndex++; continue; } // TEXTURE WINS — leave it alone

                    var inst = new Material(src);
                    string slotName = src.name ?? "";
                    Color c = ResolvePaladinColor(slotName, slotIndex, out string label);
                    // Ungated sheet WRITES (Set* stores to the material's property sheet even when the
                    // shader is unresolvable): a genuinely textureless slot therefore reads back as
                    // TINTED in the audit in EVERY environment — Warn "colored-not-textured", never a
                    // false WHITE HERO ROOT. Extra sheet entries on shaders lacking a prop are inert.
                    inst.SetColor("_BaseColor", c);
                    inst.SetColor("_Color", c);
                    inst.SetFloat("_Smoothness", 0.1f);
                    inst.SetFloat("_Metallic", 0f);
                    mats[i] = inst;
                    colored++;
                    if (log.Length < 300)
                    { if (log.Length > 0) log.Append(", "); log.Append($"[{slotIndex}] '{slotName}'→{label}"); }
                    slotIndex++;
                }
                r.sharedMaterials = mats;
            }
            FlowTrace.Step("HeroBody",
                $"KNIGHTV3 color fallback: kept {keptTextured} textured slot(s) as-is (texture wins); " +
                $"filled {colored} null-albedo slot(s) with a flat color{(colored > 0 ? " — " + log : "")}.");
        }

        /// <summary>Resolve a Paladin slot color: material NAME keyword first, then the per-index
        /// fallback palette (cycles), then the steel default. <paramref name="label"/> names the pick
        /// for the FlowTrace so a capture shows exactly which slot got which color.</summary>
        private static Color ResolvePaladinColor(string slotName, int index, out string label)
        {
            string n = (slotName ?? "").ToLowerInvariant();
            foreach (var e in PaladinPalette)
            {
                if (e.Keywords == null) continue;
                foreach (var kw in e.Keywords)
                {
                    if (!string.IsNullOrEmpty(kw) && n.Contains(kw))
                    { label = e.Label; return e.Color; }
                }
            }
            if (PaladinIndexFallback.Length > 0)
            {
                int fi = index % PaladinIndexFallback.Length;
                label = $"index#{fi}";
                return PaladinIndexFallback[fi];
            }
            label = "default-steel";
            return PaladinDefaultColor;
        }

        private static string SlugFor(HeroClass cls) => cls switch
        {
            HeroClass.Knight => "Knight",
            // CANON RE-CORRECTION 2026-08-09 (F8 "Not sylas, this is a blink"): the 08-05 note
            // here said Ranger.fbx / Mage.fbx do NOT exist. True then, FALSE now — both are
            // git-TRACKED since f18b66b4 (2026-08-06) with Humanoid rigs, Materials/Ranger.mat and
            // basecolor atlases. Start() now probes the legacy Resources body FIRST for non-Knight
            // classes, so these slugs name real meshes again, not just controller + loadout.
            // (The 125-byte `<slug>.fbx.tripo-extracted` sentinels are Editor/TripoAssetPostprocessor
            // markers, unrelated to whether a real mesh exists.)
            HeroClass.Ranger => "Ranger",
            HeroClass.Mage   => "Mage",
            // DEF-221: the Cleric now has a DEDICATED body (Resources/Heroes/Cleric.fbx,
            // from People/Human/human_Cleric) + Cleric.controller (a copy of the Mage
            // caster kit). It still fires the Mage ability LOADOUT — abilitySlug below
            // routes Cleric → "Mage" for SetHeroClass — until a cleric/healer kit exists.
            HeroClass.Cleric => "Cleric",
            _ => null,
        };

        // (Removed NeedsForwardFlip — the per-class -Z/+Z guess was superseded by
        // the single consistent +X→+Z ForwardYaw correction at swap time, WO-174.)

        /// <summary>
        /// Loads a per-class basecolor PNG out of Resources/Textures/
        /// (extracted via Tripo's Send-To-Unity flow on the owner's side)
        /// and assigns it to every URP material on the body. When this
        /// returns true a real diffuse was bound and the subsequent
        /// <see cref="ApplyClassTint"/> is a no-op for that material (it
        /// preserves textures). Returns FALSE when no diffuse was bound
        /// (no texPath for the class, or the texture failed to load) so the
        /// caller can fall back to a flat material instead of an untextured slot.
        /// </summary>
        private static bool ApplyExtractedTexture(GameObject body, HeroClass cls)
        {
            // DEF-267: separate the two body pipelines.
            //  • CC5 bodies (Ranger / Cleric) = ONE combined mesh + ONE baked PBR atlas, imported
            //    with materialImportMode=None → renderer slots come in null/default. The right fix
            //    is to build ONE URP/Lit from the single atlas and REPLACE every slot.
            //  • Legacy Tripo bodies (Mage / Knight) = multi-material exports whose slots DO import
            //    (materialImportMode=2) but with NO base-color texture bound, so they render GREY in
            //    the build (the DEF-267 symptom: "URP-but-untextured"). Their Send-To-Unity diffuse
            //    PNG lives in Resources/Heroes/Textures/*. PAINT that diffuse onto the EXISTING slots
            //    in place — replacing them all with one shared material would collapse the body's
            //    separate UV islands onto a single material and mis-sample. Each slot already maps
            //    its own region of the single Tripo atlas, so painting the same atlas onto each is
            //    correct and preserves the per-slot setup.
            // WO-286: ALL four heroes are now re-rigged single-atlas meshes (AccuRIG,
            // 2026-06-06). Each FBX shipped EXTRA texture sets (Knight a stray
            // remesh_*_Bake, Ranger a Motion_Dummy_Female PBR set) plus metallic/normal
            // maps the import bound into the slots — that's the blotchy purple-camo Mage +
            // patchy-red Knight (wrong albedo + a normal/metallic read as colour). The
            // "paint _BaseMap onto existing slots" path LEFT those stray maps in place, so
            // it never cleaned up. Build ONE fresh URP/Lit from JUST the real basecolor and
            // REPLACE every slot (no metallic/normal assigned → no colour-space artefact).
            // Each slot's own UVs still sample the single atlas correctly. So: combined
            // path for ALL classes now, not only the old CC5 Ranger/Cleric.
            bool isCc5Combined = true;
            // WO-1701 (2026-09-17): the per-class atlas ADDRESSES moved to
            // DeNelle.Core.HeroTextureLoader.BasecolorAddressFor / NormalAddressFor. They had to:
            // HeroContentPrewarmer (Core) must warm exactly the atlas this method later binds, and
            // Core cannot see this Village file, so the addresses living only here meant the
            // prewarm could not hold them and the WebGL hero rendered as a flat class tint. The
            // VALUES are unchanged - only their home moved - and the provenance comments below stay
            // here with the mesh they explain. Never re-type an atlas name at a call site again;
            // change it in the Core map, which the prewarm and this binding both read.
            string texPath = DeNelle.Core.HeroTextureLoader.BasecolorAddressFor(cls);
            // ── THE ART LEDGER for the addresses that map now holds. Read it for WHY each class
            //    binds what it binds; the live VALUES are in HeroTextureLoader, not below. ──
            //
                // DEF-267: the Mage is a LEGACY Tripo body. The build log showed Mage rendering
                // GREY because (a) its FBX material remap points at an EXTERNAL material that
                // doesn't resolve in the build → no _BaseMap, and (b) NO texPath existed here so
                // ApplyExtractedTexture returned early and never bound a diffuse. Bind the Mage's
                // basecolor atlas from the NORMAL Resources/Heroes/Textures/ folder. We use a
                // normal folder (not the sibling *.fbm/ extraction) deliberately: the working
                // Ranger/Cleric pipeline copies its atlas into a plain Heroes/<x>_tex/ folder
                // because textures inside an FBX's *.fbm import-artifact folder are NOT reliably
                // Resources.Load-able in a player build. Heroes/Textures/* is a guaranteed-loadable
                // plain folder. This is the same wizard atlas, just from the reliable location.
                // WO-286: the re-rigged Mage mesh's own basecolor is the "witch" atlas
                // (it shipped base-color only); the old "wizard" path bound a different
                // mesh's texture → wrong colors. Point at the mesh's own basecolor.
                // FIX (2026-06-06): the PROJECT Mage is a legacy Tripo mesh — its UV-matched
                // atlas is its OWN embedded diffuse (Mage.fbm/tripo_mat_9b343081, copied into
                // Textures/), NOT the actors/ dwarf basecolor (a different mesh). CC5 heroes
                // (Ranger/Cleric) use the actors originals; Tripo heroes use their own.
                // RE-POINTED 2026-08-06. This was "tripo_mat_9b343081_Pbr_Diffuse" - the
                // atlas of a PREVIOUS mage model, named after that model's Tripo material
                // id. The file is still on disk, so when the owner's new AccuRIG Wizard
                // body landed the swapper would have painted the OLD mage's skin onto the
                // NEW mesh and it would have looked like a texturing bug rather than a
                // stale key. ImportEmbeddedHero clears the slug's own _tex folder but NOT
                // Heroes/Textures/, so nothing else would have caught it.
                // The name now describes the CLASS, not whichever model happened to be
                // current, so the next body swap does not need a code change - matching
                // the Ranger's "ranger_basecolor" convention.
                //   -> HeroTextureLoader.BasecolorAddressFor(HeroClass.Mage)
                // DEF-267: the Knight is also a LEGACY Tripo body whose FBX material remap doesn't
                // resolve → grey. WO-35 had returned null (to dodge a blood-splatter look the owner
                // disliked), but that left the Knight GREY in the build, which is worse. Bind the
                // clean medieval-knight atlas from the plain (reliably loadable) Heroes/Textures/
                // folder — textured beats grey for go-live. ApplyClassTint's steel fallback now
                // only fires on any slot this atlas genuinely doesn't cover.
                // WO-286: the re-rigged Knight mesh ships TWO sets — its real
                // knight_basecolor and a stray remesh_12_combined_Bake raw bake. Bind the
                // knight basecolor (the old medievalknight path was a different mesh).
                // PARKED (2026-06-06): the Knight FBX is a broken combo — its mesh material
                // is 'tripo_mat_18e58c3e', which has NO matching atlas in-project (tripo_1afe2e5e,
                // remesh_12, knight_basecolor, HumanTank all read wrong). This needs a CLEAN
                // Knight model re-import, not a texture pick.
                // WO (2026-06-10) KNIGHT SPECKLE — PROPER FIX:
                //   The speckle was a WRONG-UV atlas: 'medievalknight3dmodel_basecolor' is a
                //   DIFFERENT model's texture, so each UV island of THIS mesh sampled an unrelated
                //   region → blotchy/patchy/"speckled". The Knight.fbx is a Tripo model that was
                //   REMESHED (remesh_12) and baked; its CORRECT, UV-matched basecolor is the bake
                //   that ships in the FBX's sibling Knight.fbm/ folder: remesh_12_combined_Bake_Diffuse.
                //   That PNG is already copied into the reliably-loadable plain Heroes/Textures/
                //   folder (textures inside an *.fbm/ import-artifact folder don't Resources.Load
                //   reliably in a player build — Textures/ is guaranteed-loadable, confirmed
                //   textureType=Default, sRGB, valid Texture2D). Binding the mesh's OWN bake means
                //   every UV island samples its matching region → no speckle. If this load fails,
                //   ApplyExtractedTexture warns + returns and the Start() path falls back to the
                //   flat-steel stopgap, so the Knight is never left speckled.
                // WO (2026-06-10) KNIGHT BODY SWAP → human_tank:
                //   The Knight body is now the clean, textured human_tank export (copied over
                //   Resources/Heroes/Knight.fbx, re-imported Humanoid). Its UV-matched atlas is
                //   HumanTank_basecolor, already copied into the reliably-loadable plain
                //   Heroes/Textures/ folder (textureType=Default, sRGB, valid Texture2D). Binding
                //   the mesh's OWN basecolor means every UV island samples its matching region —
                //   a real textured Knight, not the flat-steel stopgap. If this load fails,
                //   ApplyExtractedTexture returns false and Start() falls back to flat steel.
                // WO-481 Slice 4 (2026-06-23) ARMORED KNIGHT PROMOTION: the playable Knight is now
                //   the ARMORED Tripo model (armor baked into the mesh — the pivot's static-armor body),
                //   promoted over Resources/Heroes/Knight.fbx. Its UV-matched basecolor is the Tripo
                //   PBR export medieval_knight_3d_model_basecolor, copied (distinct name) into the
                //   reliably-loadable plain Heroes/Textures/ folder as KnightArmored_basecolor. Binding
                //   the mesh's OWN basecolor means every UV island samples its matching region — a real
                //   textured armored Knight, not the flat-steel stopgap. If this load fails,
                //   ApplyExtractedTexture returns false and Start() falls back to flat steel.
                //   -> HeroTextureLoader.BasecolorAddressFor(HeroClass.Knight)
                // STALE-COMMENT CORRECTION 2026-08-05: the DEF-229 note here described a CC5/CC_Base
                // archer "imported into Resources/Heroes/Ranger.fbx". That FBX is NOT in the tree
                // (`git ls-files Assets/Resources/Heroes/Ranger.fbx` returns EMPTY) — the mesh half of
                // DEF-229 never landed. This atlas path is kept because it still applies to whatever
                // body DOES get built for the Ranger (Blink base, or the tracked KayKit fallback), and
                // it is load-guarded below: a miss returns false and the class tint takes over.
                //   -> HeroTextureLoader.BasecolorAddressFor(HeroClass.Ranger)
                // DEF-232/229 (2026-06-03): the Cleric (Healer/Elara body) is now the
                // owner's fresh CC5/CC_Base adult cleric, imported Humanoid by
                // PeopleCharacterImporter.ImportClericCC5 with its baked basecolor copied
                // to Resources/Heroes/Cleric_tex/. Bind that diffuse so the Cleric reads
                // textured (was falling through to a flat class tint before).
                // WO-286: the re-rigged Cleric is the only clean set — its own basecolor
                // is Cleric_basecolor (the old HumanCleric_tex path was the prior CC5 body).
                //   -> HeroTextureLoader.BasecolorAddressFor(HeroClass.Cleric)
                // (an unmapped class returns null, same as the old `_ => null` arm)
            if (string.IsNullOrEmpty(texPath)) return false;
            // WO-545: route through the Addressables-first/Resources-fallback seam so this
            // atlas still loads once Heroes/Textures leaves Resources (was Resources.Load).
            var tex = DeNelle.Core.HeroTextureLoader.Load(texPath);
            if (tex == null)
            {
                Debug.LogWarning("[HeroBodySwapper] DEF-267 baked/basecolor diffuse not found at Resources/" +
                                 texPath + " for " + cls + " — body will fall back to a class tint.");
                return false;
            }

            // For CC5 combined bodies: build ONE URP/Lit from the single baked atlas and assign it
            // to every slot (slots came in null/default). Runtime-built URP/Lit renders in WebGL
            // (RetargetMaterialsToUrp uses the same path).
            Shader lit = Shader.Find("Universal Render Pipeline/Lit")
                         ?? Shader.Find("Universal Render Pipeline/Simple Lit")
                         ?? Shader.Find("Sprites/Default");
            Material baked = (isCc5Combined && lit != null) ? new Material(lit) { name = cls + " (CC5 baked)" } : null;
            if (baked != null)
            {
                if (baked.HasProperty("_BaseMap"))   baked.SetTexture("_BaseMap", tex);
                if (baked.HasProperty("_MainTex"))   baked.SetTexture("_MainTex", tex);
                if (baked.HasProperty("_BaseColor")) baked.SetColor("_BaseColor", Color.white);
                if (baked.HasProperty("_Color"))     baked.SetColor("_Color", Color.white);

                // Clear stray normal/metallic/occlusion maps the FBX import bound into the shader —
                // a normal or metallic map sampled in the wrong colour space reads as "speckled"/patchy
                // colour (the patchy Knight, blotchy Mage). Drive colour from the basecolor atlas ONLY.
                if (baked.HasProperty("_BumpMap"))          baked.SetTexture("_BumpMap", null);
                if (baked.HasProperty("_NormalMap"))        baked.SetTexture("_NormalMap", null);
                if (baked.HasProperty("_MetallicGlossMap")) baked.SetTexture("_MetallicGlossMap", null);
                if (baked.HasProperty("_SpecGlossMap"))     baked.SetTexture("_SpecGlossMap", null);
                if (baked.HasProperty("_OcclusionMap"))     baked.SetTexture("_OcclusionMap", null);
                baked.DisableKeyword("_NORMALMAP");
                baked.DisableKeyword("_METALLICSPECGLOSSMAP");
                baked.DisableKeyword("_OCCLUSIONMAP");

                // SEE-THROUGH JOINTS FIX (owner ticket 2026-07-02 "I can see through parts of the
                // hero: shoulders, knees, elbows"): the Tripo self-rigged bodies are OPEN SHELLS —
                // Knight.fbx is 26 separate armor parts (tripo_part_0..25 in its import skeleton),
                // each a single-sided surface. `new Material(URP/Lit)` defaults to back-face cull
                // (_Cull=2), so when skinning bends a joint and the shells separate, the camera
                // looks straight through the open shell interior = a hole in the hero. Render
                // double-sided (same DEF-6 fix RetargetMaterialsToUrp already applies) so the
                // shell's inner face shows instead of a see-through gap. Trivial fill cost on a
                // low-poly body.
                if (baked.HasProperty("_Cull")) baked.SetFloat("_Cull", 0f); // 0 = Off (double-sided)

                // Knight armour sheen — keep metallic LOW so the basecolor atlas drives the
                // visible colour. ROOT CAUSE of the "colorless Knight" in the WebGL build:
                // metallic=0.7 on a flat basecolor (no metallic map) makes ~70% of the surface
                // colour come from ENVIRONMENT reflection, not albedo. The editor/Standalone
                // have rich skybox+reflection so the steel read fine; the WebGL build has weak/
                // flat environment reflection (reduced reflection probes + URP StripUnusedVariants),
                // so the metal had almost nothing to reflect and rendered as a desaturated grey/
                // white "colorless" hero. The companion Knight (StoryCompanionInjector.BindClassDiffuse)
                // never sets metallic and renders fine — match that. A small metallic + moderate
                // smoothness still gives an armour highlight without washing out the basecolor.
                if (cls == HeroClass.Knight)
                {
                    if (baked.HasProperty("_Metallic"))   baked.SetFloat("_Metallic", 0.1f);
                    if (baked.HasProperty("_Smoothness")) baked.SetFloat("_Smoothness", 0.35f);

                    // WO (2026-06-10) KNIGHT BODY SWAP → hero-tank: the rigged AccuRIG CC_Base
                    // Knight ships a UV-matched NORMAL map alongside its basecolor (copied to
                    // Heroes/Textures/HeroTank_Normal, imported textureType=NormalMap). Unlike the
                    // stray import-bound maps cleared above (which read as colour artefacts on the
                    // OTHER classes), this is the mesh's OWN tangent-space normal — bind it so the
                    // armour plates/cloth catch surface detail. Knight-ONLY: the other classes ship
                    // base-color only and keep _BumpMap cleared. Null-guarded — a missing normal
                    // is a clean no-op (the basecolor still drives the look).
                    // WO-481 Slice 4: the armored Tripo Knight ships a UV-matched NORMAL map alongside
                    // its basecolor (medieval_knight_3d_model_normal, copied to Heroes/Textures/
                    // KnightArmored_normal, imported textureType=NormalMap). Bind it so the armour
                    // plates/cloth catch surface detail. Null-guarded — a missing normal is a clean no-op.
                    // WO-1701 (2026-09-17): the address comes from the Core map so the prewarm warms
                    // the same string this binds (it was the literal "Heroes/Textures/KnightArmored_normal").
                    var knightArmoredNormal = DeNelle.Core.HeroTextureLoader.Load(
                        DeNelle.Core.HeroTextureLoader.NormalAddressFor(cls)); // WO-545 Addressables seam
                    if (knightArmoredNormal != null && baked.HasProperty("_BumpMap"))
                    {
                        baked.SetTexture("_BumpMap", knightArmoredNormal);
                        baked.EnableKeyword("_NORMALMAP");
                    }
                }
            }

            foreach (var r in body.GetComponentsInChildren<Renderer>(true))
            {
                if (r == null) continue;
                var mats = r.sharedMaterials;
                if (mats == null || mats.Length == 0)
                {
                    if (baked != null) r.sharedMaterial = baked;
                    continue;
                }
                for (int i = 0; i < mats.Length; i++)
                {
                    if (baked != null)
                    {
                        // For all (unified CC5 path): always replace slots with the baked material that has
                        // the correct class basecolor atlas bound to _BaseMap and white _BaseColor.
                        // This ensures vibrant correct textures regardless of what the FBX import remapped
                        // (or left null). Stray metallic/normal from import are discarded (they were causing
                        // color artefacts like patchy red/purple).
                        mats[i] = baked;
                    }
                    else if (mats[i] != null)
                    {
                        // Legacy Tripo (Mage/Knight): paint the diffuse onto the EXISTING URP slot.
                        // These slots are already URP/Lit (RetargetMaterialsToUrp ran first) but with
                        // no _BaseMap bound → grey. Bind the atlas and clear any dark/zero _BaseColor
                        // so the texture (not a leftover tint) drives the look.
                        if (mats[i].HasProperty("_BaseMap")) mats[i].SetTexture("_BaseMap", tex);
                        if (mats[i].HasProperty("_MainTex")) mats[i].SetTexture("_MainTex", tex);
                        if (mats[i].HasProperty("_BaseColor")) mats[i].SetColor("_BaseColor", Color.white);
                        if (mats[i].HasProperty("_Color"))     mats[i].SetColor("_Color", Color.white);
                    }
                }
                r.sharedMaterials = mats;
            }
            Debug.Log("[HeroBodySwapper] DEF-267 ApplyExtractedTexture: " + cls +
                      " diffuse '" + texPath + "' bound (mode=" + (isCc5Combined ? "CC5-replace" : "Tripo-paint") + ").");
            return true;
        }

        /// <summary>
        /// KNIGHT STOPGAP (2026-06-09): replace EVERY renderer slot on the Knight body with one
        /// flat, solid steel-grey URP/Lit material — NO basecolor/normal/metallic/occlusion maps.
        /// The Knight FBX has no UV-matched atlas in-project (mesh material 'tripo_mat_18e58c3e';
        /// every candidate atlas mis-samples → the speckled/patchy look). A clean solid armour grey
        /// is presentable for go-live; a correct Knight re-import can restore textures later.
        /// Knight-ONLY — never called for Ranger/Mage/Cleric, whose textured paths work.
        /// </summary>
        private static void ApplyFlatSteelStopgap(GameObject body)
        {
            if (body == null) return;
            Shader lit = Shader.Find("Universal Render Pipeline/Lit")
                         ?? Shader.Find("Universal Render Pipeline/Simple Lit")
                         ?? Shader.Find("Standard");
            if (lit == null)
            {
                Debug.LogWarning("[HeroBodySwapper] Knight stopgap: no URP/Lit or Standard shader — skipped.");
                return;
            }

            // Believable plate-armour grey (#7A7E85), low metallic so the basecolor (not the
            // environment reflection) drives the look, moderate smoothness for a soft steel sheen.
            Color steel = new Color(0.478f, 0.494f, 0.522f, 1f); // #7A7E85
            var mat = new Material(lit) { name = "Knight (flat steel stopgap)" };
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", steel);
            if (mat.HasProperty("_Color"))     mat.SetColor("_Color", steel);
            // Explicitly NO maps — clear any the shader defaults in and disable their keywords so
            // nothing samples a wrong-UV texture and re-introduces the speckle.
            if (mat.HasProperty("_BaseMap"))          mat.SetTexture("_BaseMap", null);
            if (mat.HasProperty("_MainTex"))          mat.SetTexture("_MainTex", null);
            if (mat.HasProperty("_BumpMap"))          mat.SetTexture("_BumpMap", null);
            if (mat.HasProperty("_NormalMap"))        mat.SetTexture("_NormalMap", null);
            if (mat.HasProperty("_MetallicGlossMap")) mat.SetTexture("_MetallicGlossMap", null);
            if (mat.HasProperty("_SpecGlossMap"))     mat.SetTexture("_SpecGlossMap", null);
            if (mat.HasProperty("_OcclusionMap"))     mat.SetTexture("_OcclusionMap", null);
            if (mat.HasProperty("_EmissionMap"))      mat.SetTexture("_EmissionMap", null);
            mat.DisableKeyword("_NORMALMAP");
            mat.DisableKeyword("_METALLICSPECGLOSSMAP");
            mat.DisableKeyword("_OCCLUSIONMAP");
            mat.DisableKeyword("_EMISSION");
            if (mat.HasProperty("_Metallic"))   mat.SetFloat("_Metallic", 0.1f);
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 0.35f);

            int slots = 0;
            foreach (var r in body.GetComponentsInChildren<Renderer>(true))
            {
                if (r == null) continue;
                int count = (r.sharedMaterials != null && r.sharedMaterials.Length > 0)
                    ? r.sharedMaterials.Length : 1;
                var mats = new Material[count];
                for (int i = 0; i < count; i++) { mats[i] = mat; slots++; }
                r.sharedMaterials = mats;
            }
            Debug.Log("[HeroBodySwapper] Knight flat-steel stopgap applied to " + slots +
                      " slot(s) — solid #7A7E85, no maps (speckle fix; awaiting clean Knight re-import).");
        }

        /// <summary>
        /// If the URP material on the body still ended up white (Tripo's
        /// embedded textures sometimes don't survive import), paint each
        /// material with a class tint so the body reads coloured anyway.
        /// Skipped when a real diffuse texture is already bound (e.g. the
        /// extracted basecolor PNG from Tripo's Send-To-Unity flow).
        /// </summary>
        private static void ApplyClassTint(GameObject body, HeroClass cls)
        {
            Color tint = cls switch
            {
                // DEF-231: the Knight (Grom) is a LEGACY Tripo body with NO baked
                // basecolor (ApplyExtractedTexture returns null for Knight), so this
                // flat tint IS his armour colour. The old steel (0.78,0.80,0.86) read
                // dark/muddy/desaturated in scene light. Lift the value to a brighter,
                // cleaner cool steel (keep it cool — blue-biased, NOT muddy brown) so the
                // armour reads crisp without going flat-white.
                HeroClass.Knight => new Color(0.92f, 0.94f, 1.00f),   // bright cool steel
                HeroClass.Ranger => new Color(0.40f, 0.50f, 0.34f),   // hunter / leaf green
                _                => new Color(0.60f, 0.45f, 0.85f),   // mage fallback
            };

            // Owner 2026-05-26: forcing a flat tint "lost the Knight's colour" — it
            // cleared the textured detail. Restore the original rule: paint the tint
            // ONLY when a material has no diffuse texture. That preserves every real
            // texture — the Ranger's fresh v2 basecolor, the Mage's embedded texture,
            // and whatever the Knight imported — and the tint is just the fallback so
            // an untextured mesh still reads coloured instead of solid white.
            // WO-330 anti-cyan: a CC5/AccuRIG combined mesh can import its renderer slots as
            // NULL (or Unity's Default-Material), which URP renders as a bright cyan/magenta
            // UNLIT error shape — the "solid cyan silhouette". A null slot must be REPLACED with
            // a real URP/Lit material (tinted), not skipped, or the hero stays cyan. Build the
            // lit shader once for that case.
            Shader lit = Shader.Find("Universal Render Pipeline/Lit")
                         ?? Shader.Find("Universal Render Pipeline/Simple Lit")
                         ?? Shader.Find("Standard");

            foreach (var r in body.GetComponentsInChildren<Renderer>(true))
            {
                if (r == null) continue;
                var mats = r.sharedMaterials;
                if (mats == null) continue;
                for (int i = 0; i < mats.Length; i++)
                {
                    var m = mats[i];
                    // WO-330: a null/default slot is the cyan culprit — give it a fresh lit,
                    // tinted material so it is never left on URP's error/default shader.
                    if (m == null)
                    {
                        if (lit == null) continue;
                        var fill = new Material(lit) { name = cls + " (tint fill)" };
                        if (fill.HasProperty("_BaseColor")) fill.SetColor("_BaseColor", tint);
                        if (fill.HasProperty("_Color"))     fill.SetColor("_Color", tint);
                        if (cls == HeroClass.Knight)
                        {
                            if (fill.HasProperty("_Metallic"))   fill.SetFloat("_Metallic", 0.35f);
                            if (fill.HasProperty("_Smoothness")) fill.SetFloat("_Smoothness", 0.45f);
                        }
                        mats[i] = fill;
                        continue;
                    }
                    Texture tex = null;
                    if (m.HasProperty("_BaseMap")) tex = m.GetTexture("_BaseMap");
                    if (tex == null && m.HasProperty("_MainTex")) tex = m.GetTexture("_MainTex");
                    if (tex != null) continue;   // preserve real textures
                    if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", tint);
                    if (m.HasProperty("_Color"))     m.SetColor("_Color", tint);
                    // DEF-231 (KNIGHT ONLY): the tinted Knight armour was reading dark
                    // because the runtime URP material's matte 0.15 smoothness / 0 metallic
                    // gives it no specular lift, so flat steel sits dim in scene light. Give
                    // the untextured Knight a touch of metallic + higher smoothness so the
                    // armour catches a soft highlight and reads as crisp steel — NOT a mirror
                    // (kept well under chrome) and NOT muddy. Scoped to Knight so Mage/Ranger/
                    // Cleric keep their existing matte look.
                    if (cls == HeroClass.Knight)
                    {
                        if (m.HasProperty("_Metallic"))   m.SetFloat("_Metallic", 0.35f);
                        if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.45f);
                    }
                }
                r.sharedMaterials = mats;
            }
        }

        private static void NormalizeHeight(GameObject go, float targetHeight)
        {
            var renderers = go.GetComponentsInChildren<Renderer>();
            if (renderers == null || renderers.Length == 0) return;
            Bounds b = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) b.Encapsulate(renderers[i].bounds);
            if (b.size.y <= 0.01f) return;
            float scale = targetHeight / b.size.y;
            go.transform.localScale *= scale;

            // Owner 2026-05-20 ("archer appears halfway under surface"): the
            // Tripo Ranger FBX pivots near the mesh centre, so after scaling
            // the lower half drops below the hero root's Y=0. Recompute the
            // post-scale bounds and lift the body so the feet land at y=0.
            Bounds b2 = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) b2.Encapsulate(renderers[i].bounds);
            float feetOffset = b2.min.y - go.transform.position.y;
            if (feetOffset < 0f)
                go.transform.localPosition -= new Vector3(0f, feetOffset, 0f);
        }

        private static void StripColliders(GameObject go)
        {
            foreach (var c in go.GetComponentsInChildren<Collider>(true))
                if (c != null) Destroy(c);
        }

        private static void StripRigidbodies(GameObject go)
        {
            foreach (var rb in go.GetComponentsInChildren<Rigidbody>(true))
                if (rb != null) Destroy(rb);
        }

        /// <summary>
        /// DEF-232 guard: assert the swapped body's render bounds sit centred (XZ) over the
        /// hero ROOT — the transform the follow camera tracks and HeroLocomotion drives. A
        /// horizontal offset is the exact "camera stays to her right / body pivots in place"
        /// regression (an off-pivot mesh rotated after seating). Logs LOUDLY so it is caught at
        /// the spawn source rather than re-surfacing as a camera complaint, and re-centres as a
        /// runtime safety net so the player is never left with the body off to one side.
        /// </summary>
        private void ValidateBodyCentredOverRoot(GameObject body)
        {
            if (body == null) return;
            var rends = body.GetComponentsInChildren<Renderer>();
            if (rends == null || rends.Length == 0) return;

            Bounds b = rends[0].bounds;
            for (int i = 1; i < rends.Length; i++) b.Encapsulate(rends[i].bounds);

            Vector3 root = transform.position;
            float dx = b.center.x - root.x;
            float dz = b.center.z - root.z;
            float planarOffset = Mathf.Sqrt(dx * dx + dz * dz);

            // ~0.35 m tolerance: a hand/weapon prop can pull the encapsulated centre slightly
            // off the body's own midline without it reading as "offset to the side".
            const float MaxPlanarOffset = 0.35f;
            if (planarOffset > MaxPlanarOffset)
            {
                Debug.LogError(
                    "[HeroBodySwapper] DEF-232 — swapped body '" + body.name + "' is OFF-CENTRE " +
                    planarOffset.ToString("F2") + "m from the hero root (dx=" + dx.ToString("F2") +
                    ", dz=" + dz.ToString("F2") + "). The follow camera tracks the ROOT, so the " +
                    "body would appear offset to one side and 'pivot in place'. Re-centring as a " +
                    "safety net — investigate the mesh pivot / Skin seating.");
                // Safety-net re-centre: shift the body so its bounds centre sits over the root's XZ.
                body.transform.position -= new Vector3(dx, 0f, dz);
            }
        }

        /// <summary>
        /// DEF-232 / WO-966 (facing) self-correct. DERIVE the body's true forward from the skeleton
        /// then rotate only the RESIDUAL so mesh forward matches the root heading.
        ///
        /// ⚠ MEASURE AXIS = SHOULDERS (LeftUpperArm → RightUpperArm), the SAME maths as
        /// <c>RangerBodyBuilder.MeasureForwardYawNeeded</c> / <c>HeroFacingAudit</c>. An earlier
        /// hip-axis version disagreed with the audit deadzone and could not be trusted to land
        /// the number the audit reported. Hips remain a last-resort fallback only.
        ///
        /// Residual + 5° deadzone ⇒ already-aligned body is a NO-OP. Non-humanoid / unmappable
        /// rigs leave the base seat untouched.
        /// </summary>
        private void AlignBodyFacingToRoot(GameObject body, Animator anim)
        {
            if (body == null || anim == null) return;
            if (!anim.isHuman || anim.avatar == null || !anim.avatar.isValid) return;

            // WO-966: prefer shoulders (audit parity); hips only if arms missing.
            Transform left  = anim.GetBoneTransform(HumanBodyBones.LeftUpperArm);
            Transform right = anim.GetBoneTransform(HumanBodyBones.RightUpperArm);
            string axis = "shoulder";
            if (left == null || right == null)
            {
                left  = anim.GetBoneTransform(HumanBodyBones.LeftUpperLeg);
                right = anim.GetBoneTransform(HumanBodyBones.RightUpperLeg);
                axis = "hip-fallback";
            }
            if (left == null || right == null) return;

            // Character RIGHT = left → right lateral, ground-projected.
            Vector3 rightDir = right.position - left.position;
            rightDir.y = 0f;
            if (rightDir.sqrMagnitude < 1e-6f) return;
            rightDir.Normalize();

            // FORWARD = right × up (Unity LH: Cross(+X,+Y) = +Z).
            Vector3 bodyForward = Vector3.Cross(rightDir, Vector3.up);

            Vector3 rootForward = transform.forward;
            rootForward.y = 0f;
            if (rootForward.sqrMagnitude < 1e-6f) rootForward = Vector3.forward;
            rootForward.Normalize();

            float curYaw = Mathf.Atan2(bodyForward.x, bodyForward.z) * Mathf.Rad2Deg;
            float tgtYaw = Mathf.Atan2(rootForward.x, rootForward.z) * Mathf.Rad2Deg;
            float delta  = Mathf.DeltaAngle(curYaw, tgtYaw);

            const float DeadzoneDeg = 5f;
            if (Mathf.Abs(delta) <= DeadzoneDeg)
            {
                FlowTrace.Step("HeroBody",
                    $"facing OK — body within {delta:F1}° of root heading (axis={axis}); no correction.");
                return;
            }

            body.transform.rotation = Quaternion.AngleAxis(delta, Vector3.up) * body.transform.rotation;
            FlowTrace.Step("HeroBody",
                $"facing self-correct: skeleton forward {curYaw:F0}° vs root {tgtYaw:F0}° → " +
                $"rotated body {delta:F0}° (axis={axis}, WO-966 shoulder-parity).");

            ValidateBodyCentredOverRoot(body);
            PlantFeetOnGround(body);
        }

        /// <summary>
        /// DEF-235: drop the swapped body so the lowest VISIBLE skinned vertex (the feet/soles)
        /// rests exactly at the hero root's Y. VisualFactory.SeatOnGround already aligned the
        /// RENDER-BOUNDS base to root.y, but a SkinnedMeshRenderer's bounds carry bind-pose
        /// padding — the bounds floor sits a few cm BELOW the actual posed soles — so the feet
        /// hover. We bake each skinned mesh into its current pose, walk its world-space vertices
        /// to find the true lowest point, and nudge the body DOWN by (lowestVertexY - root.y).
        ///
        /// Y-ONLY (does not touch XZ, so it never disturbs DEF-232's centring) and HERO-SCOPED
        /// (VisualFactory.SeatOnGround stays unchanged, so enemies/structures are unaffected).
        /// Clamped: only ever corrects a small UPWARD float — it never LIFTS the body, and it
        /// never sinks more than MaxDrop, so a bad measurement (e.g. a stray low vert on a cape)
        /// can't bury the hero under the terrain.
        /// </summary>
        private void PlantFeetOnGround(GameObject body)
        {
            if (body == null) return;

            float rootY = transform.position.y;
            float lowestY = float.PositiveInfinity;
            bool measured = false;

            // Prefer skinned meshes baked into their CURRENT pose — the static prefab mesh is in
            // bind pose and its lowest vertex won't match the posed soles. Bake into a scratch
            // mesh, transform each vertex to world space, and track the minimum Y.
            var skinned = body.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            if (skinned != null && skinned.Length > 0)
            {
                var baked = new Mesh();
                foreach (var smr in skinned)
                {
                    if (smr == null || smr.sharedMesh == null) continue;
                    smr.BakeMesh(baked); // baked is in the SMR's local space, current pose
                    var verts = baked.vertices;
                    Transform t = smr.transform;
                    for (int i = 0; i < verts.Length; i++)
                    {
                        float wy = t.TransformPoint(verts[i]).y;
                        if (wy < lowestY) lowestY = wy;
                    }
                    measured = true;
                }
                Object.Destroy(baked);
            }

            // Fallback for any non-skinned (static MeshRenderer) body: walk its shared meshes'
            // vertices directly in world space. Rare for the hero, but keeps this robust.
            if (!measured)
            {
                foreach (var mf in body.GetComponentsInChildren<MeshFilter>(true))
                {
                    if (mf == null || mf.sharedMesh == null) continue;
                    // BUILD-SAFE: sharedMesh.vertices THROWS on an FBX mesh that isn't
                    // Read/Write-enabled (the import default in a player build). Skip those —
                    // PlantFeetOnGround then degrades to "not measured" and leaves the body
                    // where SeatOnGround placed it, rather than crashing.
                    if (!mf.sharedMesh.isReadable) continue;
                    var verts = mf.sharedMesh.vertices;
                    Transform t = mf.transform;
                    for (int i = 0; i < verts.Length; i++)
                    {
                        float wy = t.TransformPoint(verts[i]).y;
                        if (wy < lowestY) lowestY = wy;
                    }
                    measured = true;
                }
            }

            if (!measured || float.IsInfinity(lowestY)) return;

            // gap > 0 means the soles float ABOVE the ground (the DEF-235 symptom). gap <= 0 means
            // the feet already touch or sit below — leave those alone (never LIFT the body; sinking
            // is handled by SeatOnGround / terrain follow elsewhere).
            float gap = lowestY - rootY;
            const float MinFloat = 0.001f; // ignore sub-mm noise
            const float MaxDrop  = 0.30f;  // never sink more than 30 cm on a bad measurement
            if (gap <= MinFloat) return;
            float drop = Mathf.Min(gap, MaxDrop);
            body.transform.position -= new Vector3(0f, drop, 0f);
            Debug.Log("[HeroBodySwapper] DEF-235 planted feet: lowered body " +
                      drop.ToString("F3") + "m (measured float gap " + gap.ToString("F3") + "m).");
        }
    }
}

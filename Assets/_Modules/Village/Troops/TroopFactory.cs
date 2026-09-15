// =============================================================================
// TroopFactory — the ONE place a skinned, damageable friendly troop is built
// (WO-453 Step 1). Mirrors EnemyFactory's recipe so the project keeps a SINGLE
// troop-creation path.
// -----------------------------------------------------------------------------
// Assembly: DeNelle.Village   Namespace: DeNelle.Village
//
// A bare UNIT-SCALE root carries a NON-TRIGGER capsule collider + a NavMeshAgent
// + the TroopController; the mesh is a fit visual child via VisualFactory, with a
// tinted-capsule fallback ONLY if the model is missing (same as EnemyFactory).
//
// TWO deliberate differences from EnemyFactory:
//   1. The root sits on a DEFAULT-ish (probe-visible) layer, NOT "Enemy" — the
//      enemy contact-attack probe (Enemy.ProbeForStructure) uses
//      QueryTriggerInteraction.Ignore and resolves IDamageableStructure, so the
//      troop must be on a layer that probe sees and carry a NON-TRIGGER collider
//      for the enemy to find + damage it (same requirement StoryCompanion meets).
//   2. The collider is NON-trigger (enemies hit it); the troop's OWN hunt scan
//      uses QueryTriggerInteraction.Collide on the enemy mask, so the enemies'
//      trigger capsules are still found by the troop.
//
// The factory builds the BODY; the caller (TroopDeployer) owns SetEnemyMask().
// =============================================================================

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using DeNelle.Core.Combat;        // AnimParams - the canonical animator parameter vocabulary
using DeNelle.Core.Diagnostics;   // FlowTrace - [Flow:TroopVisual]

namespace DeNelle.Village
{
    /// <summary>Single skinned-troop builder shared by every troop spawner.</summary>
    public static class TroopFactory
    {
        /// <summary>
        /// How far (m) a requested spawn point may be from baked NavMesh and still be snapped
        /// onto it. Beyond this the spawn is OFF-MESH: the agent can never path and the body
        /// stands where it was dropped. Public + shared so the DEPLOY TAP gate
        /// (RaidDeployController.HandleDeployTap) refuses exactly the taps this factory would
        /// have had to strand - one number, one meaning, no drift between the check and the snap.
        /// </summary>
        public const float NavSampleRadius = 6f;

        /// <summary>
        /// Builds a skinned, damageable troop at <paramref name="pos"/> and returns its
        /// <see cref="TroopController"/> (already carrying a non-trigger collider + a
        /// NavMeshAgent on a probe-visible layer). The caller calls SetEnemyMask().
        /// </summary>
        public static TroopController Build(TroopDef def, Vector3 pos, Quaternion rot, Transform parent)
        {
            // Snap the spawn to the nearest navmesh point BEFORE adding the agent so it
            // always lands on a valid surface (mirrors EnemyFactory — a NavMeshAgent
            // AddComponent'd off-mesh never paths). A far miss is logged once.
            if (NavMesh.SamplePosition(pos, out NavMeshHit navHit, NavSampleRadius, NavMesh.AllAreas))
            {
                pos = navHit.position;
            }
            else
            {
                // WAS A BARE Debug.LogWarning (defect sweep 2026-08-15). This branch builds an
                // INERT troop - no agent path, so it never fights, never dies, and then counts
                // as a SURVIVOR at raid reconcile, inflating SurvivalPct and buying 3-star
                // clears. A Debug.LogWarning is invisible to the F8 break-capture harness, so
                // the single most consequential spawn failure in the raid loop produced no
                // evidence at all. FlowTrace.Fail so a capture SHOWS it. The spawn is still
                // completed on purpose: non-tap spawners (garrison/scripted musters) must not
                // lose a body over a thin bake, and the DEPLOY TAP - the path the player drives
                // - is now refused UPSTREAM in RaidDeployController.HandleDeployTap, so a
                // player action can no longer reach this branch at all.
                FlowTrace.Fail("TroopVisual",
                    $"OFF-MESH SPAWN: no baked NavMesh within {NavSampleRadius}m of {pos} for " +
                    $"'{(def != null ? def.Id : "troop")}'. The agent cannot path - this body will hold " +
                    "position, take no part in the fight, and still count as a survivor at reconcile. " +
                    "Check the spawn point / the bake.");
            }

            var go = new GameObject(def != null ? $"Troop ({def.Id})" : "Troop");
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(pos, rot);

            // Layer: a probe-visible DEFAULT-ish layer (NOT "Enemy"), so the enemy
            // contact-attack probe (QueryTriggerInteraction.Ignore) hits the troop's
            // non-trigger collider — the same requirement StoryCompanion's hitbox meets.
            // Leave the GameObject on layer 0 (Default) explicitly.
            go.layer = 0;

            // WO-933 siege machines need a wider footprint than humanoids.
            bool isSiege = def != null
                && string.Equals(def.Role, "siege", System.StringComparison.OrdinalIgnoreCase);
            float bodyHeight = isSiege ? 2.4f : 1.8f;
            float bodyRadius = isSiege ? 0.85f : 0.42f;

            // NON-TRIGGER capsule so enemies can physically reach + damage the troop.
            // Offset up to wrap the body; root stays unit-scale (scaling a NavMeshAgent
            // root misbehaves) — only the visual is fit bigger.
            var col = go.AddComponent<CapsuleCollider>();
            col.isTrigger = false;
            col.radius = bodyRadius;
            col.height = bodyHeight;
            col.center = new Vector3(0f, bodyHeight * 0.5f, 0f);

            // Skin (load -> fit -> strip colliders) + animator; tinted-capsule fallback
            // only if the model is genuinely missing (so a troop still spawns damageable).
            // Hero/companion bodies import facing +X and need a -90° yaw to face +Z
            // (DEF-232 — set on SkinOptions so it's applied BEFORE fit/seat).
            // WO-933: model may be a full Resources path ("Structures/Catapult") or a
            // bare Heroes name ("SC_Footman" / "Knight").
            string model = def != null ? def.Model : null;
            string resourcesPath = ResolveModelResourcesPath(model);
            GameObject vis = null;
            string troopId = def != null ? def.Id : "troop";
            FlowTrace.Step("TroopVisual",
                $"id={troopId}: model resolved from def = '{model ?? "<none>"}' (yaw={(def != null ? def.ModelYaw : 0f)}) " +
                $"-> Resources '{resourcesPath ?? "<none>"}' siege={isSiege}.");
            if (!string.IsNullOrEmpty(resourcesPath))
            {
                var skinOpts = VisualOptionsFor(isSiege, bodyHeight);
                skinOpts.StripColliders = true;
                // Body facing is per-pack and authored on the def (Tripo bodies face +X → -90;
                // Supercyan humanoids face +Z → 0). Default -90 keeps legacy bodies correct.
                if (def != null)
                    skinOpts.LocalRotation = Quaternion.Euler(0f, def.ModelYaw, 0f);
                vis = VisualFactory.Skin(go.transform, resourcesPath, skinOpts);
            }

            // ANIMATOR (owner defect 2026-08-02: "raid troops slide / T-pose instead of walking
            // and attacking"). MUST run BEFORE the TroopController AddComponent below — AddComponent
            // runs Awake SYNCHRONOUSLY, and TroopController.Awake caches which of Speed/Attack/Hit/
            // Dead the bound controller declares. Bind after that and every param write is skipped
            // for the life of the troop. Siege machines are static props — skip humanoid bind.
            if (vis != null && !isSiege) ApplyTroopAnimator(vis, def, model);

            // WO-troop-gear: seat weapon / offhand so roles do not share bare skins.
            // Runs AFTER animator bind so Humanoid bones resolve. Siege = no hand sockets.
            if (vis != null && def != null && !isSiege)
                TroopGearApplier.Apply(vis, def);

            GameObject fallbackVisual = null;
            if (vis == null)
            {
                // A body must remain visible while remote art arrives (or is unavailable).
                // Siege cannot use the humanoid capsule: that vertical oversized silhouette
                // was the apparent "catapult standing on end" captured for WO-1143.
                // WO-1748: WAS A BARE Debug.LogWarning. A LogWarning is INVISIBLE to the F8
                // break-capture harness (same reasoning already recorded for the off-mesh branch
                // at :60-75 of this file), so the one line that names WHICH model failed to load
                // never reached a capture — and the only line that DID reach one
                // (TroopController.Awake's "NO Animator anywhere") could not name the troop.
                // FlowTrace.Fail so a capture SHOWS the address, and the spawn still completes
                // with a readable body on purpose.
                FlowTrace.Fail("TroopVisual",
                    $"id={troopId}: model '{resourcesPath ?? model ?? "<none>"}' had NO loadable mesh - " +
                    $"FALLBACK to a {(isSiege ? "siege-machine proxy" : "tinted capsule")}. " +
                    "The fallback body carries no Animator, so this troop cannot animate.");
                fallbackVisual = isSiege
                    ? BuildSiegeFallback(go.transform)
                    : BuildHumanoidFallback(go.transform, bodyHeight);

                // A synchronous Addressables miss means "not resident this frame". Reapply after
                // the shared warmer settles for every path-form troop model, not just Catapult.
                // The readable fallback stays until a verified renderer replaces it.
                if (IsAddressableModelPath(resourcesPath))
                {
                    GameObject capturedFallback = fallbackVisual;
                    DeNelle.Core.StructureContentWarmer.WhenSettled(() =>
                    {
                        if (go == null || capturedFallback == null) return;
                        var retryOpts = VisualOptionsFor(isSiege, bodyHeight);
                        retryOpts.StripColliders = true;
                        if (def != null)
                            retryOpts.LocalRotation = Quaternion.Euler(0f, def.ModelYaw, 0f);
                        GameObject arrived = VisualFactory.Skin(go.transform, resourcesPath, retryOpts);
                        if (arrived == null)
                        {
                            FlowTrace.Warn("TroopVisual",
                                $"id={troopId}: addressable retry settled but '{resourcesPath}' still " +
                                "has no verified renderer; retaining the readable fallback.");
                            return;
                        }

                        Object.Destroy(capturedFallback);
                        FlowTrace.Step("TroopVisual",
                            $"id={troopId}: addressable body arrived; replaced fallback with '{arrived.name}'.");
                    });
                }
            }

            // NavMeshAgent — share the hero's agent type (0) so the troop traverses the
            // SAME NavMeshLinks the hero/enemies use. TroopController.Awake re-asserts
            // these (radius/height/Move-driven) but set them here too so it's path-ready.
            var agent = go.AddComponent<NavMeshAgent>();
            agent.agentTypeID = 0;
            agent.radius = isSiege ? 0.8f : 0.4f;
            agent.height = bodyHeight;

            // WO-1748 SPAWN IDENTITY LINE. This is the ONLY AddComponent<TroopController>() site
            // in the game, and AddComponent runs Awake SYNCHRONOUSLY - so every diagnostic Awake
            // emits is printed BEFORE Configure() on the next line assigns _troopId. That is why
            // eight device captures (seq 5057..5258) all read "[Flow:TroopVisual] id=: NO Animator
            // anywhere ..." with an EMPTY id: the id was structurally unset, not missing. The line
            // below runs IMMEDIATELY BEFORE the AddComponent so the id, the resolved address, the
            // siege flag and the body's actual animator state are on the log one line above any
            // Awake failure, whatever the caller. Per-spawn Step, not Once: every troop goes
            // through here, and Once would name only the first one.
            var bodyAnimator = go.GetComponentInChildren<Animator>();
            string bodyKind = vis != null ? "skinned" : (isSiege ? "siege-proxy-fallback" : "capsule-fallback");
            string animatorState = bodyAnimator == null
                ? "NONE"
                : (bodyAnimator.runtimeAnimatorController == null
                    ? "present-but-unbound"
                    : "bound:" + bodyAnimator.runtimeAnimatorController.name);
            FlowTrace.Step("TroopVisual",
                $"id={troopId} role='{(def != null ? def.Role : "<none>")}' siege={isSiege}: about to " +
                $"AddComponent<TroopController> - address='{resourcesPath ?? model ?? "<none>"}' " +
                $"body={bodyKind} animator={animatorState}. Any TroopVisual line printed after this " +
                "one and before the next deploy belongs to THIS troop.");

            var troop = go.AddComponent<TroopController>();
            troop.Configure(def, pos);
            return troop;
        }

        // =====================================================================
        //  ANIMATOR — the missing half of the troop body (owner defect 2026-08-02)
        // =====================================================================
        //
        // THE STRUCTURAL DIFFERENCE vs the paths that DO animate (this is the evidence, not a theory):
        //
        //   ENEMY      EnemyFactory.cs:174  -> EnemyAnimatorFactory.Apply(vis, model), which loads
        //                                     Resources/Enemies/<rig>.controller and assigns
        //                                     anim.runtimeAnimatorController (EnemyAnimatorFactory.cs:163).
        //   COMPANION  StoryCompanionInjector.cs:560-566 -> HeroAssetLoader.LoadHeroController(slug)
        //                                     then anim.runtimeAnimatorController = ctrl.
        //   HERO       HeroBodySwapper.cs:547 -> anim.runtimeAnimatorController = controller.
        //   TROOP      TroopFactory.Build     -> NOTHING. A grep for `runtimeAnimatorController =`
        //                                     across Assets/_Modules returns ZERO hits under Troops/.
        //
        // That gap produces the symptom two different ways, one per art pack in troops.json:
        //
        //   (A) model "Knight"  (troop-shieldguard, troop-echo-legionnaire)
        //       Resources/Heroes/Knight.fbx is a MODEL prefab. Its Animator carries the generated
        //       Humanoid avatar but m_Controller is None — a model prefab never ships a controller.
        //       So runtimeAnimatorController == null, TroopController.cs:292 short-circuits the
        //       parameter scan, and _hasSpeed/_hasAttack/_hasHit/_hasDead all stay FALSE. The rig
        //       holds its bind pose while the NavMeshAgent slides the root. Meanwhile
        //       Resources/Heroes/Knight.controller EXISTS and declares exactly Speed/Attack/Hit/Dead
        //       — it was simply never loaded on this path.
        //
        //   (B) model "SC_Footman" / "SC_Archer" (footman, archer, spearman, outrider, battlemage)
        //       These are prefab VARIANTS of the Supercyan pack bodies (SupercyanResourceWire.cs),
        //       so they DO ship an Animator with a bound controller — Supercyan's own
        //       StrafeMovement.controller. Its parameter list is MoveHorizontal / MoveVertical /
        //       Grounded / MoveState / WeaponFire / IsDead / TakingHit1 ... It declares NO "Speed",
        //       NO "Attack", NO "Hit" and NO "Dead". Same outcome by a different route: the four
        //       flags in TroopController.Awake stay false, TroopController.cs:330 never writes a
        //       float, TroopController.cs:425/442/458 never fire a trigger, and the state machine
        //       sits in whatever its default state is (with Grounded defaulting to false) while the
        //       agent slides the body. SupercyanResourceWire.cs:16 asserts "the idle/walk/attack
        //       clips play once the NavMeshAgent drives position" — that assertion is FALSE, nothing
        //       drives that controller's parameters.
        //
        /// <summary>
        /// Bare model names load under <c>Heroes/</c>. Paths that already contain a slash
        /// (e.g. <c>Structures/Catapult</c>) are full Resources paths (WO-933 siege machines).
        /// </summary>
        private static string ResolveModelResourcesPath(string model)
        {
            if (string.IsNullOrEmpty(model)) return null;
            if (model.IndexOf('/') >= 0 || model.IndexOf('\\') >= 0)
                return model.Replace('\\', '/');
            return "Heroes/" + model;
        }

        private static bool IsAddressableModelPath(string resourcesPath)
            => !string.IsNullOrEmpty(resourcesPath) && resourcesPath.IndexOf('/') >= 0;

        /// <summary>
        /// Selects the visual fit contract for a troop body. Siege art still needs the
        /// structure material/ground-seating policy, but its gameplay body height is a
        /// HEIGHT target, not a largest-dimension target. Using Structure(bodyHeight)
        /// made a wide horizontal catapult fit its width to 2.4 m and rendered it shorter
        /// than the adjacent footman. Keep this shared by the first skin and async retry.
        /// </summary>
        public static SkinOptions VisualOptionsFor(bool isSiege, float bodyHeight)
        {
            if (!isSiege)
                return SkinOptions.Enemy(bodyHeight);

            var options = SkinOptions.Structure(0f); // retain seating/material policy; clear FitLargest
            options.FitHeight = bodyHeight;
            return options;
        }

        private static GameObject BuildHumanoidFallback(Transform host, float bodyHeight)
        {
            var capsule = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            capsule.name = "TroopFallback_Humanoid";
            StripPrimitiveCollider(capsule);
            capsule.transform.SetParent(host, false);
            capsule.transform.localPosition = new Vector3(0f, bodyHeight * 0.5f, 0f);
            TintCapsule(capsule.GetComponent<Renderer>());
            return capsule;
        }

        /// <summary>Low, horizontal, collision-free siege silhouette used only while art is absent.</summary>
        private static GameObject BuildSiegeFallback(Transform host)
        {
            var root = new GameObject("TroopFallback_SiegeMachine");
            root.transform.SetParent(host, false);

            AddSiegePart(root.transform, PrimitiveType.Cube, "Chassis",
                new Vector3(0f, 0.72f, 0f), new Vector3(1.35f, 0.36f, 1.75f), Quaternion.identity);
            AddSiegePart(root.transform, PrimitiveType.Cube, "ThrowingArm",
                new Vector3(0f, 1.18f, 0.08f), new Vector3(0.20f, 0.20f, 1.75f),
                Quaternion.Euler(-24f, 0f, 0f));
            AddSiegePart(root.transform, PrimitiveType.Sphere, "Counterweight",
                new Vector3(0f, 1.48f, -0.62f), Vector3.one * 0.48f, Quaternion.identity);

            for (int side = -1; side <= 1; side += 2)
            for (int end = -1; end <= 1; end += 2)
                AddSiegePart(root.transform, PrimitiveType.Cylinder, $"Wheel_{side}_{end}",
                    new Vector3(side * 0.76f, 0.48f, end * 0.62f),
                    new Vector3(0.58f, 0.16f, 0.58f), Quaternion.Euler(0f, 0f, 90f));

            return root;
        }

        private static void AddSiegePart(Transform parent, PrimitiveType type, string name,
            Vector3 position, Vector3 scale, Quaternion rotation)
        {
            var part = GameObject.CreatePrimitive(type);
            part.name = name;
            StripPrimitiveCollider(part);
            part.transform.SetParent(parent, false);
            part.transform.localPosition = position;
            part.transform.localRotation = rotation;
            part.transform.localScale = scale;
            TintCapsule(part.GetComponent<Renderer>());
        }

        private static void StripPrimitiveCollider(GameObject primitive)
        {
            if (primitive != null && primitive.TryGetComponent(out Collider collider))
                Object.Destroy(collider);
        }

        // THE FIX binds a controller whose parameters ARE the vocabulary TroopController already
        // speaks (AnimParams). All four Resources/Heroes controllers (Knight/Ranger/Cleric/Mage)
        // declare the full Speed/InCombat/Attack/Combo/Cast/WindUp/Block/Hit/HitDir/Dead/DeathDir/
        // Victory/Injured set, and their clips are Humanoid — which retargets onto ANY Humanoid
        // avatar, exactly how EnemyAnimatorFactory shares one Mixamo controller across the whole orc
        // family. The Supercyan rig is Humanoid (its FBX meta is animationType: 3), so a hero
        // controller poses it. An already-driveable controller is KEPT, never clobbered.
        private static void ApplyTroopAnimator(GameObject vis, TroopDef def, string model)
        {
            if (vis == null) return;
            string id = def != null ? def.Id : "troop";
            string what = $"id={id} model='{model ?? "<none>"}'";

            try
            {
                var anim = vis.GetComponentInChildren<Animator>(true);
                if (anim == null)
                {
                    anim = vis.AddComponent<Animator>();
                    FlowTrace.Warn("TroopVisual",
                        $"{what}: skinned body shipped NO Animator - added one on the visual root " +
                        "(a rig-less prop model would end up here; the troop still fights).");
                }
                var avatar = anim.avatar;
                bool avatarValid = avatar != null && avatar.isValid;
                FlowTrace.Step("TroopVisual",
                    $"{what}: animator present on '{anim.gameObject.name}' isHuman={anim.isHuman} " +
                    $"avatar='{(avatar != null ? avatar.name : "<null>")}' avatarValid={avatarValid} " +
                    $"boundController='{(anim.runtimeAnimatorController != null ? anim.runtimeAnimatorController.name : "<null>")}'.");

                // Root motion OFF: the locomotion clips carry baked root curves that would fight the
                // NavMeshAgent.Move() displacement TroopController.MoveToward applies to the ROOT
                // (the same reason HeroBodySwapper.cs:553 and EnemyAnimatorFactory.cs:129 clear it).
                // ELIMINATED as a cause of this defect, not a fix for it: both packs already ship
                // applyRootMotion = 0 — asserted here so it can never silently regress.
                anim.applyRootMotion = false;
                // Off-screen culling, matching every other spawned body (EnemyAnimatorFactory.cs:138).
                // NOT CullCompletely — that can desync gameplay-driven anim events.
                anim.cullingMode = AnimatorCullingMode.CullUpdateTransforms;

                // Already driveable? Keep it — never clobber a controller that speaks our vocabulary.
                if (HasDriveParams(anim))
                {
                    FlowTrace.Step("TroopVisual",
                        $"{what}: bound controller '{anim.runtimeAnimatorController.name}' already declares " +
                        $"'{AnimParams.Speed}' - kept as-is (no rebind needed).");
                    return;
                }

                string rejected = anim.runtimeAnimatorController != null
                    ? anim.runtimeAnimatorController.name
                    : "<null>";

                foreach (string cand in ControllerCandidates(def, model))
                {
                    var ctrl = Resources.Load<RuntimeAnimatorController>("Heroes/" + cand);
                    FlowTrace.Step("TroopVisual",
                        $"{what}: controller candidate 'Heroes/{cand}' -> {(ctrl == null ? "NULL" : ctrl.name)}.");
                    if (ctrl == null) continue;

                    anim.runtimeAnimatorController = ctrl;
                    if (!HasDriveParams(anim))
                    {
                        // Loaded, but it does not speak AnimParams either — do not ship a controller
                        // nothing can drive; say so and keep looking.
                        FlowTrace.Warn("TroopVisual",
                            $"{what}: candidate 'Heroes/{cand}' loaded but declares no '{AnimParams.Speed}' " +
                            "parameter - TroopController could not drive it; trying the next candidate.");
                        continue;
                    }

                    // Rebind reconnects the bone references after the runtime Instantiate, else the
                    // mesh T-poses for ~10 frames after the swap (HeroBodySwapper.cs:574).
                    anim.Rebind();

                    FlowTrace.Step("TroopVisual",
                        $"{what}: controller ASSIGNED 'Heroes/{cand}' (replaced '{rejected}' which declared no " +
                        $"'{AnimParams.Speed}'); applyRootMotion=false, culling=CullUpdateTransforms.");

                    // AVATAR VALIDITY — the second half of "why it T-poses". A Humanoid clip can ONLY
                    // pose a rig through a valid Humanoid avatar; with none, the Animator holds the
                    // bind pose while the agent slides it. Self-report LOUDLY (do NOT hide the troop —
                    // it must still spawn damageable). Mirrors EnemyAnimatorFactory.cs:172-192.
                    bool humanoidClips = ControllerHasHumanoidClips(ctrl);
                    if (anim.isHuman && !avatarValid)
                        FlowTrace.Fail("TroopVisual",
                            $"{what}: controller 'Heroes/{cand}' bound but the Animator has NO valid Humanoid " +
                            $"avatar (avatar={(anim.avatar == null ? "<null>" : "invalid")}) - a humanoid clip " +
                            "will hold the bind/T-pose while the agent slides it (the sliding-statue path). " +
                            "Re-import the model as Humanoid with a valid avatar.");
                    else if (!anim.isHuman && humanoidClips)
                        FlowTrace.Fail("TroopVisual",
                            $"{what}: rig is GENERIC but controller 'Heroes/{cand}' carries Humanoid clips - " +
                            "a Humanoid clip cannot pose a Generic rig at all, so this troop WILL T-pose. " +
                            "Re-import the model as Humanoid.");
                    else
                        FlowTrace.Step("TroopVisual",
                            $"{what}: avatar OK for 'Heroes/{cand}' (isHuman={anim.isHuman} " +
                            $"humanoidClips={humanoidClips}) - rig should pose.");

                    // Deferred pose-verify: samples a real bone over ~8 frames and FAILS LOUD under
                    // [Flow:EnemyPose] if nothing ever drives the rig. Shared with the enemy path
                    // (same assembly, same failure mode) rather than duplicated — so "troop bound a
                    // controller but is still frozen" self-reports instead of costing an owner cycle.
                    EnemyPoseVerifier.Attach(vis, anim, model, cand, humanoidClips);
                    return;
                }

                // Nothing resolved. This is the state the troop shipped in BEFORE this fix, so name it
                // exactly — a headless raid run must never leave "no animation" un-attributed again.
                FlowTrace.Fail("TroopVisual",
                    $"{what}: NO animator controller resolved from Resources/Heroes (tried " +
                    $"{string.Join(", ", ControllerCandidates(def, model))}); the bound controller " +
                    $"'{rejected}' declares no '{AnimParams.Speed}'. This troop WILL slide with no walk/" +
                    "attack/death animation. Run the hero animator setup so Resources/Heroes/*.controller exist.");
            }
            catch (System.Exception e)
            {
                // A missing/gitignored art pack must never take the spawn down — the troop still
                // needs to exist and fight. Warn + continue, never an error, never a throw.
                FlowTrace.Warn("TroopVisual",
                    $"{what}: animator setup threw {e.GetType().Name}: {e.Message} - troop spawns un-animated but playable.");
            }
        }

        /// <summary>
        /// Controllers to try, most specific first. Order:
        /// authored <c>TroopDef.Animator</c> → model-stem match → role/body heuristic
        /// (melee=Knight Attack, ranged=Ranger bow Attack, caster/Mage=Mage Cast) → Knight last resort.
        /// All four hero controllers declare AnimParams (Speed/Attack/Cast/Hit/Dead).
        /// </summary>
        private static string[] ControllerCandidates(TroopDef def, string model)
        {
            var list = new List<string>(5);
            void Add(string s)
            {
                if (string.IsNullOrEmpty(s)) return;
                s = s.Trim();
                // Full Resources paths are not controller stems.
                if (s.IndexOf('/') >= 0 || s.IndexOf('\\') >= 0) return;
                if (!list.Contains(s)) list.Add(s);
            }

            if (def != null) Add(def.Animator);
            // Bare model name only (SC_Archer has no matching controller — skips harmlessly).
            Add(model);
            Add(ResolveRoleController(def, model));
            Add("Knight");   // always a driveable Humanoid fallback
            return list.ToArray();
        }

        /// <summary>
        /// Primary combat controller for a troop: casters/Mage bodies → Mage (Cast);
        /// ranged → Ranger (bow Attack); melee/siege → Knight (melee Attack).
        /// </summary>
        public static string ResolveRoleController(TroopDef def, string model)
        {
            string authored = def != null ? def.Animator : null;
            if (!string.IsNullOrEmpty(authored)
                && authored.IndexOf('/') < 0 && authored.IndexOf('\\') < 0)
                return authored.Trim();

            string m = model ?? "";
            string role = (def != null && def.Role != null) ? def.Role.Trim().ToLowerInvariant() : "melee";
            string id = def != null && def.Id != null ? def.Id.ToLowerInvariant() : "";

            // Caster first: battlemage is combat-role "ranged" but must CAST, not bow-draw.
            if (role == "caster" || role == "mage"
                || id.IndexOf("mage", System.StringComparison.Ordinal) >= 0
                || id.IndexOf("wizard", System.StringComparison.Ordinal) >= 0
                || m.IndexOf("Mage", System.StringComparison.OrdinalIgnoreCase) >= 0
                || m.IndexOf("Wizard", System.StringComparison.OrdinalIgnoreCase) >= 0)
                return "Mage";

            if (role == "ranged"
                || m.IndexOf("Archer", System.StringComparison.OrdinalIgnoreCase) >= 0
                || m.IndexOf("Ranger", System.StringComparison.OrdinalIgnoreCase) >= 0)
                return "Ranger";

            // Melee (and siege if it ever binds) — Knight Attack/stab chain.
            return "Knight";
        }

        /// <summary>True when this troop should fire the Cast trigger (not Attack) on strike.</summary>
        public static bool UsesCastStrike(TroopDef def, string model)
        {
            string ctrl = ResolveRoleController(def, model);
            return string.Equals(ctrl, "Mage", System.StringComparison.OrdinalIgnoreCase)
                || string.Equals(ctrl, "Cleric", System.StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// WO-935 Phase 3 (archer row) - true when this troop's strike should read as a BOW
        /// SHOT rather than a melee connect.
        ///
        /// Deliberately derived from <see cref="ResolveRoleController"/>, the SAME resolver
        /// <see cref="UsesCastStrike"/> uses, rather than from a second reading of def.Role or
        /// def.AttackRange. Role alone would be wrong twice over: the battlemage is authored
        /// role "ranged" but resolves to Mage (it casts, and already has its presentation), and
        /// an archer body can be selected by MODEL name with no role at all. One resolver, three
        /// mutually exclusive strike reads - cast, bow, melee.
        /// </summary>
        public static bool UsesBowShot(TroopDef def, string model)
        {
            return string.Equals(ResolveRoleController(def, model), "Ranger",
                System.StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// True when the animator's CURRENTLY BOUND controller declares the canonical
        /// <see cref="AnimParams.Speed"/> float — i.e. TroopController can actually drive it.
        /// Speed is the load-bearing one: it is the only per-frame write (TroopController.cs:330),
        /// and every controller the project builds that declares Speed also declares Attack/Hit/Dead.
        /// </summary>
        private static bool HasDriveParams(Animator anim)
        {
            if (anim == null || anim.runtimeAnimatorController == null) return false;
            var ps = anim.parameters;
            if (ps == null) return false;
            for (int i = 0; i < ps.Length; i++)
                if (ps[i] != null && ps[i].nameHash == AnimParams.SpeedHash) return true;
            return false;
        }

        /// <summary>True if any clip on the controller is Humanoid motion — such a clip can only
        /// pose the rig through a valid Humanoid avatar (a Generic rig T-poses).</summary>
        private static bool ControllerHasHumanoidClips(RuntimeAnimatorController ctrl)
        {
            if (ctrl == null) return false;
            var clips = ctrl.animationClips;
            if (clips == null) return false;
            for (int i = 0; i < clips.Length; i++)
                if (clips[i] != null && clips[i].humanMotion) return true;
            return false;
        }

        private static void TintCapsule(Renderer mr)
        {
            if (mr == null) return;
            Shader sh = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            if (sh == null) return;
            var m = new Material(sh);
            // Friendly blue tint, to read distinct from the enemy fallback's red-brown.
            var tint = new Color(0.30f, 0.45f, 0.75f);
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", tint); else m.color = tint;
            mr.sharedMaterial = m;
        }
    }
}

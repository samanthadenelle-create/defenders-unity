// =============================================================================
// BarracksNpcInjector — runtime, NON-DESTRUCTIVE placement of the drillmaster NPC
// at the castle Barracks (WO-453 troop-training flow). Mirrors
// CastleVendorNpcInjector's self-bootstrap, but keys off the single 'CastleBarracks'
// building GameObject (placed by CastleBarracksPlacer) instead of the storefront
// "NPC_<Role>_Interactable" markers — the barracks prefab has no such marker child.
// -----------------------------------------------------------------------------
// WHY a runtime injector (not a scene edit / regen):
//   The barracks is a polyperfect prefab dropped into MainCastle_Hall by the
//   CastleBarracksPlacer editor tool. Re-saving / regenerating that scene to add a
//   real NPC body carries the project's known scene-resave corruption risk
//   (CLAUDE.md §3). So this self-bootstrapping DDOL singleton, on every
//   MainCastle_Hall load, FINDS the 'CastleBarracks' root and spawns a static
//   drillmaster NPC in FRONT of it — WITHOUT ever touching the .unity file.
//   Idempotent per load (a re-load nukes the prior runtime holder).
//
// STATIC, not townsfolk: reuses the same Resources/NPCs People-pack body source +
//   AmbientNPC.Configure(arch, wander:FALSE, ...) the vendor injector uses, so the
//   drillmaster stands his ground (idle sway only, no roam/follow).
//
// INTERACTION: the SAME slim CastleNpcInteractable the vendors use, configured with
//   structureId "barracks". Its Talk opens DialogueService.PlayStructure("barracks",
//   "Barracks") → the Barracks_MainMenu Yarn node (PlayStructure routes "barracks"
//   to that node, mirroring the pet-house branch). No reflection, no cross-asmdef ref.
// =============================================================================

using DeNelle.Core.Diagnostics;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

namespace DeNelle.Village
{
    /// <summary>Runtime, non-destructive placement of the drillmaster NPC at the castle Barracks.</summary>
    public sealed class BarracksNpcInjector : MonoBehaviour
    {
        public static BarracksNpcInjector Instance { get; private set; }

        private const string TargetScene = "MainCastle_Hall";
        // WO-608 merge: castle-hub chrome must fire on the merged Main_Castle_Overworld too,
        // while staying castle-only. Mirrors CastleBeamHider / CastleVendorNpcInjector.
        private const string MergedTargetScene = "Main_Castle_Overworld";
        private static bool IsCastleHubScene(string n) => n == TargetScene || n == MergedTargetScene;

        // The building root CastleBarracksPlacer drops into the scene.
        private const string BarracksRootName = "CastleBarracks";

        // Resources/NPCs People-pack body — the smith reads as a fitting drillmaster
        // (the vendor injector uses the same body source). Merchant is the safe fallback.
        private const string BodyDrillmaster = "NPCs/NPC_Blacksmith";
        private const string BodyFallback    = "NPCs/NPC_Merchant";

        private const string StructureId = "barracks";
        private const string Label        = "Barracks";

        // How far IN FRONT of the building origin the drillmaster stands (toward the
        // plaza / approaching hero). The barracks faces castle centre, so place the NPC
        // along the building's forward and let the navmesh snap settle the exact spot.
        private const float FrontOffset = 4.5f;

        private const string HolderName = "BarracksNPC (runtime)";

        // WO-724: the unlock (ff.barracks + founding-complete) can flip true LIVE while the
        // player is standing in the hub - the FTUE completes IN-scene (the town wave loop
        // kicks with no reload; TutorialFlow.FinishFlow). A cheap 1 Hz poll surfaces the
        // Barracks the moment founding completes, without waiting for the next hub load.
        private const float UnlockPollInterval = 1f;
        private float _nextPollAt;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (Instance != null) return;
            new GameObject("BarracksNpcInjector").AddComponent<BarracksNpcInjector>();
        }

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            DontDestroyOnLoad(gameObject);

            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
            // StructureSingleton v2: react to a placed barracks via the singleton
            // authority's event instead of polling FindObjectsByType every second
            // (same subscribe/unsubscribe shape as sceneLoaded above).
            StructureSingleton.SingletonResolved -= OnSingletonResolved;
            StructureSingleton.SingletonResolved += OnSingletonResolved;
            if (IsCastleHubScene(SceneManager.GetActiveScene().name)) Inject();
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
            SceneManager.sceneLoaded -= OnSceneLoaded;
            StructureSingleton.SingletonResolved -= OnSingletonResolved;
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (IsCastleHubScene(scene.name)) Inject();
        }

        // WO-724: 1 Hz watch for the unlock flipping true LIVE (founding completes in-hub
        // with no scene reload). When it does, surface the baked Barracks building (the
        // visual injector reactivates + skins it) and then place the drillmaster. Guarded
        // so it only fires once per surfacing (no re-spawn while the holder already stands).
        // StructureSingleton v2: the reseat watch (placed-barracks detection scan) moved to
        // the OnSingletonResolved subscription below - this poll is ONLY the original
        // WO-724 unlock-flip surfacing again.
        private void Update()
        {
            if (Time.unscaledTime < _nextPollAt) return;
            _nextPollAt = Time.unscaledTime + UnlockPollInterval;

            if (!IsCastleHubScene(SceneManager.GetActiveScene().name)) return;
            if (!DeNelle.Village.BarracksUnlock.IsUnlocked) return;
            // WO-834 blank-town gate: on a Build-Your-Own (migrated, never-built) save the
            // baked barracks stays down at unlock — the drillmaster arrives when the player
            // PLACES a barracks (SingletonResolved reseat below). Without this early-return
            // the poll would re-surface the bake + log every second on a blank town.
            // WO-950: the gate now runs BEFORE the holder check so the poll can also REAP an
            // orphan — a drillmaster the sceneLoaded ordering seated against the bake before
            // the deferred EnforceAll sweep stood that bake down (WO-950 mechanism b). Gate
            // closed + nothing placed means any standing holder is an NPC at an invisible
            // building; reap it and re-arm the mis-burned once-teach.
            if (!StructureSingleton.MayBakedTwinSurface(StructureId))
            {
                var orphan = GameObject.Find(HolderName);
                if (orphan != null && !StructureSingleton.IsPlayerBuilt(StructureId))
                {
                    FlowTrace.Warn("Barracks",
                        "WO-950 poll: reaped an ORPHANED drillmaster - the blank-town gate is closed and " +
                        "nothing is placed, so it was seated against the suppressed bake (sceneLoaded race).");
                    Destroy(orphan);
                    ResetMisburnedOnceTeach();
                }
                return;
            }
            if (GameObject.Find(HolderName) != null) return;   // already surfaced this scene

            FlowTrace.Step("Barracks",
                "unlock flipped true in-hub (ff.barracks + founding-complete) - surfacing the Barracks live (1 Hz poll).");
            // Reactivate + skin the baked CastleBarracks the lock had deactivated, then place the NPC.
            HubStructureVisualInjector.EnsureBarracksSurfaced();
            Inject();
        }

        // StructureSingleton v2 (replaces the F8 seq528 reseat-watch scan): the singleton
        // authority raises SingletonResolved right after a PLACED barracks is enforced
        // (baked twin stood down) - re-Inject so the drillmaster reseats at the placed
        // instance (Inject is idempotent: nukes the holder, anchors placed-first).
        private void OnSingletonResolved(string itemId, GameObject placed)
        {
            if (!string.Equals(itemId, StructureId, System.StringComparison.OrdinalIgnoreCase)) return;
            if (!IsCastleHubScene(SceneManager.GetActiveScene().name)) return;
            FlowTrace.Step("Barracks",
                "SingletonResolved('barracks') - reseating the drillmaster onto the PLACED barracks (placed wins).");
            Inject();
        }

        private void Inject()
        {
            using var _ = FlowTrace.Enter("Village", "BarracksNpcInjector.Inject");

            // WO-724 unlock rule (charter OPTION A): the drillmaster only exists when the
            // feature flag is ON (ff.barracks, default OFF) AND founding is complete
            // (GameState.Onboarded). Single source of truth = BarracksUnlock.IsUnlocked;
            // ff.barracks OFF => fully hidden (regression), founding-incomplete => not yet.
            if (!DeNelle.Village.BarracksUnlock.IsUnlocked)
            {
                FlowTrace.Step("Barracks",
                    $"Inject stand-down - Barracks locked (ff.barracks={DeNelle.Core.FeatureFlags.Barracks}, " +
                    $"foundingComplete={DeNelle.Village.BarracksUnlock.FoundingComplete}); no drillmaster.");
                return;
            }
            FlowTrace.Step("Barracks", "Inject - Barracks unlocked (flag ON + founding-complete); placing the drillmaster.");

            // Idempotent: nuke any prior runtime holder so a re-load doesn't double-spawn.
            var prior = GameObject.Find(HolderName);
            if (prior != null) Destroy(prior);

            // WO-812 + owner F8 seq528 ("Barracks has no NPC" — at the barracks SHE placed):
            // a PLACED/replayed catalog barracks (Building id "barracks") anchors the
            // drillmaster FIRST — placed wins, same rule as the vendor eviction; the legacy
            // baked CastleBarracks is the fallback when nothing is placed. Exactly ONE holder
            // spawns (idempotent nuke above), so never two drillmasters. Reseat rides the
            // StructureSingleton.SingletonResolved subscription (OnSingletonResolved above).
            // StructureSingleton v2: the bespoke NO-DOUBLES baked-twin standdown that lived
            // here is DELETED - StructureSingleton.Enforce("barracks") owns the twin
            // standdown/resurface via the catalog's repo.bakedTwins ("CastleBarracks").
            GameObject barracks = null;
            foreach (var b in Object.FindObjectsByType<Building>(FindObjectsSortMode.None))
                if (b != null && b.IsAlive &&
                    string.Equals(b.BuildingId, StructureId, System.StringComparison.OrdinalIgnoreCase))
                {
                    barracks = b.gameObject;
                    FlowTrace.Step("Barracks",
                        "BarracksNpcInjector: anchoring the drillmaster to the PLACED catalog barracks (placed wins, WO-812).");
                    break;
                }

            if (barracks == null)
            {
                // WO-950 item 1: the BAKED fallback carries the SAME blank-town gate the 1 Hz
                // poll already carries (WO-834, StructureSingleton.MayBakedTwinSurface). This
                // path runs on sceneLoaded — BEFORE the deferred StructureSingleton.EnforceAll
                // sweep — so without the gate it could find the still-active bake, seat the
                // drillmaster, and burn the once-teach on a save where the barracks was never
                // built (owner felt-report 2026-08-10). A PLACED barracks is exempt by
                // construction: the placed-scan above already claimed it and never reaches
                // here. ONE owner per concern: the rule itself lives in StructureSingleton
                // (the swept 'barracks' catalog row authors bakedTwins 'CastleBarracks');
                // this seam only QUERIES the authority earlier than its deferred sweep runs.
                if (!StructureSingleton.MayBakedTwinSurface(StructureId))
                {
                    FlowTrace.Step("Barracks",
                        "Inject refused the BAKED CastleBarracks - blank-town gate closed (WO-834 via WO-950): " +
                        "'barracks' was never built on this save. The drillmaster seats only at a PLACED " +
                        "barracks; the once-teach stays unburned.");
                    ResetMisburnedOnceTeach();
                    return;
                }
                barracks = AuthoredCastleStorefront.Find(BarracksRootName)?.gameObject;
            }

            if (barracks == null)
            {
                // Expected pre-placement — Warn (not Fail), still self-reports. WO-812: the fix
                // for this state is now in the player's hands (Build menu -> Barracks).
                FlowTrace.Warn("Village",
                    "BarracksNpcInjector: no baked 'CastleBarracks' AND no placed catalog barracks — drillmaster not placed (build one from the Town palette).");
                Debug.Log("[BarracksNpcInjector] no baked or placed barracks in scene — drillmaster not placed " +
                          "(Build menu -> Barracks places one; first is free).");
                return;
            }

            var holder = new GameObject(HolderName);
            Transform hero = ResolveHero();

            if (SpawnDrillmaster(barracks.transform, hero, holder.transform))
            {
                FlowTrace.Step("Village", "BarracksNpcInjector: placed the drillmaster NPC at the Barracks.");
                Debug.Log("[BarracksNpcInjector] placed the drillmaster NPC at the Barracks.");

                // WO-813 ONCE-TEACH (owner: "some dialogue and raid tutorial"): the first time
                // the Barracks surfaces with its drillmaster after founding, tell the player
                // where the army comes from. One-shot via the SeenTutorials ledger; the full
                // Sylas Yarn beat + the Train-3 task ride the UI seat's copy pass (WO-813 §1).
                // ⛔ THE ONCE-TEACH TOAST IS RETIRED (owner ruling PROD-002, option (a), 2026-08-18).
                // It read: "Elarion needs soldiers. The drillmaster at the Barracks trains them."
                // That sentence was FALSE. The drillmaster's Talk opens only
                // DialogueService.PlayStructure("barracks", …) — structure dialogue, never a
                // training panel; Manage owns troop training. So the one piece of copy whose whole
                // job was teaching the player where the army comes from pointed them at an NPC that
                // cannot do it.
                // Retired rather than re-pointed at Manage (option (b)) by owner ruling: the
                // barracks door itself is closing in the same change, so a toast introducing a door
                // that no longer exists would be teaching a flow the player cannot follow.
                // ⚠ The 'barracks_intro' SeenTutorials key is deliberately left in the schema and in
                // the WO-950 gate-refusal reset below. Removing a key that shipped saves carry buys
                // nothing and risks the migration path; it simply stops being written.
                FlowTrace.Step("Barracks", "drillmaster placed; once-teach toast RETIRED (PROD-002 a) — " +
                                           "training lives in Manage, not at this NPC.");
            }
            else
            {
                FlowTrace.Fail("Village", "BarracksNpcInjector: failed to place the drillmaster NPC.");
                Debug.LogWarning("[BarracksNpcInjector] failed to place the drillmaster NPC.");
            }
        }

        // =====================================================================
        // WO-950 item 3 — the smallest honest reset of a MIS-BURNED once-teach.
        // The owner's 2026-08-10 save burned 'barracks_intro' via the ungated
        // sceneLoaded Inject (toast pointed at a building she never built). Rather
        // than a dev-only flag flip, the GATE-REFUSAL path clears the seen flag
        // itself: the gate being closed (migrated save, 'barracks' not in the
        // monotonic EverBuiltStructureIds) PROVES the drillmaster never
        // legitimately seated on this save — a placement would have opened the
        // gate forever via MarkEverBuilt at the commit seam. So the clear can
        // never touch a legitimate burn (Default-Town/legacy saves have the
        // template grant; a placed barracks keeps the gate open), and the
        // owner's already-burned save self-heals on its next hub load: the teach
        // re-arms and fires once, honestly, at her first real barracks.
        // Self-guarding + public so the regression suite can pin both directions.
        // =====================================================================
        /// <summary>Clears a 'barracks_intro' once-teach that was burned while the
        /// blank-town gate was closed (an illegitimate burn). Refuses when the gate
        /// is open or a placed barracks exists. Returns true when it cleared.</summary>
        public static bool ResetMisburnedOnceTeach()
        {
            // A legit burn is untouchable: gate open, or a player-owned barracks standing.
            if (StructureSingleton.MayBakedTwinSurface(StructureId)) return false;
            if (StructureSingleton.IsPlayerBuilt(StructureId)) return false;

            var svc = DeNelle.Core.State.GameStateService.Instance;
            var st = svc != null ? svc.State : null;
            if (st == null || st.SeenTutorials == null) return false;
            if (!(st.SeenTutorials.TryGetValue("barracks_intro", out bool seen) && seen)) return false;

            st.SeenTutorials.Remove("barracks_intro");
            // Persist only in play mode: the headless regression seats a THROWAWAY
            // GameStateService, and Save() writes the real save file — a suite run
            // must never clobber a genuine save with fixture state.
            if (Application.isPlaying) svc.Save();
            FlowTrace.Step("Barracks",
                "WO-950: cleared a MIS-BURNED 'barracks_intro' once-teach - the blank-town gate is closed " +
                "and nothing is placed, so the drillmaster never legitimately seated on this save; the " +
                "teach re-arms for the first real barracks.");
            return true;
        }

        private bool SpawnDrillmaster(Transform barracks, Transform hero, Transform parent)
        {
            using var _ = FlowTrace.Enter("Village", "BarracksNpcInjector.SpawnDrillmaster");

            // CENTER-FACING PLACEMENT (owner 2026-06-21): stand the drillmaster on the barracks' side
            // FACING THE HEART (the tree at castle centre), so it's always between the barracks and the
            // tree ("easier to find"). Was placed along barracks.forward, which didn't point at the tree —
            // that's why the barracks NPC read as "missing". Mirrors CastleVendorNpcInjector.
            Vector3 center = HeartCenter();
            Vector3 toHeart = new Vector3(center.x - barracks.position.x, 0f, center.z - barracks.position.z);
            toHeart = toHeart.sqrMagnitude < 0.01f ? barracks.forward : toHeart.normalized;
            Vector3 pos = barracks.position + toHeart * FrontOffset;
            if (NavMesh.SamplePosition(pos, out var hit, 6f, NavMesh.AllAreas))
                pos = hit.position;
            // Face the Heart / approaching hero.
            Quaternion rot = Quaternion.LookRotation(toHeart, Vector3.up);

            // WO-818: the catalog's repo.npcModel (KayKit slug, owner mapping table:
            // barracks -> Paladin_with_Helmet) is the FIRST body source, so a data retag
            // swaps the drillmaster's body with zero code. A missing/bad slug degrades
            // (one Warn inside the resolver) to the legacy People chain below.
            var prefab = KayKitNpcBody.Load(StructureId, "Village", out string kayKitRes)
                         ?? Resources.Load<GameObject>(BodyDrillmaster)
                         ?? Resources.Load<GameObject>(BodyFallback);
            string bodyRes = kayKitRes ?? BodyDrillmaster;
            if (prefab == null)
            {
                // T/U: load-miss — fall back to a placeholder so the barracks still gets a drillmaster,
                // and self-report (Warn -> break-log).
                FlowTrace.Warn("Village",
                    $"BarracksNpcInjector: no body prefab (missing Resources/{BodyDrillmaster}) — placeholder used.");
                Debug.LogWarning($"[BarracksNpcInjector] no body prefab (missing Resources/{BodyDrillmaster}) — placeholder used.");
                return SpawnPlaceholder(pos, rot, hero, parent);
            }

            GameObject go = null;
            Guard.Try("Village", "instantiate drillmaster body", () =>
            {
                go = Instantiate(prefab, pos, rot, parent);
            });
            if (go == null)
            {
                // G/R: Instantiate returned/threw null — fall back to a placeholder, self-report.
                FlowTrace.Fail("Village",
                    $"BarracksNpcInjector: Instantiate returned null for '{bodyRes}' — placeholder used.");
                return SpawnPlaceholder(pos, rot, hero, parent);
            }
            go.name = "BarracksDrillmaster";

            // V (render-verify): a body with no enabled mesh reads as an invisible drillmaster. Prove
            // it renders; on failure drop it and fall back to the placeholder.
            if (!VerifyNpcRenders(go, bodyRes))
            {
                FlowTrace.Fail("Village",
                    $"BarracksNpcInjector: drillmaster body '{bodyRes}' has no visible mesh — dropping, placeholder used.");
                Destroy(go);
                return SpawnPlaceholder(pos, rot, hero, parent);
            }

            // WO-833: a KayKit body ships an Animator + Humanoid avatar but NO controller,
            // so it renders its bind pose (owner F8 "NPC Stuck in T Pose") - arm the shared
            // retargeted idle. ⛔ ONLY a KayKit body: Load() now returns a non-null resolvedRes for
            // a PROD-002 "CraftPixPeople/..." slug too, and a CraftPix person already carries the
            // townsfolk controller (civilian idle) - arming it would overwrite that with
            // KayKitNpcIdle's m-standby-idle, the Knight's COMBAT stance (owner 2026-08-20: "they
            // need to use ide not combat idle"). Guard on the PATH so a slug retag cannot undo it.
            if (KayKitNpcBody.IsKayKitPath(kayKitRes)) KayKitNpcBody.ArmIdle(go, kayKitRes, "Village");

            NormalizeToHeroHeight(go);
            NpcGroundSeat.Seat(go, pos.y);

            // STATIC: wander=FALSE → AmbientNPC disables its NavMeshAgent and stands its
            // ground (idle sway only). Blacksmith archetype reads as a gruff drillmaster.
            var npc = go.GetComponent<AmbientNPC>();
            if (npc != null) npc.Configure(TownsfolkDialogue.Archetype.Blacksmith, /*wander*/ false, pos);

            var agent = go.GetComponent<NavMeshAgent>();
            if (agent != null) agent.enabled = false;

            AttachInteraction(go, hero);
            return true;
        }

        // Minimal capsule fallback if the People-pack body is absent (Models gitignored
        // on a fresh clone). Getting the INTERACTION working is the priority.
        private bool SpawnPlaceholder(Vector3 pos, Quaternion rot, Transform hero, Transform parent)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            go.name = "BarracksDrillmaster_Placeholder";
            go.transform.SetParent(parent, false);
            go.transform.position = pos + Vector3.up * 1f;
            go.transform.rotation = rot;

            // Proximity-based interaction → don't let the capsule collider block the hero.
            var col = go.GetComponent<Collider>();
            if (col != null) col.isTrigger = true;

            AttachInteraction(go, hero);
            return true;
        }

        private void AttachInteraction(GameObject body, Transform hero)
        {
            // G: a throw while wiring the interaction would otherwise spawn a mute, uninteractable
            // drillmaster with no log. Guard it so the failure self-reports (Fail -> break-log).
            Guard.Try("Village", $"attach barracks interaction '{StructureId}'", () =>
            {
                var interact = body.AddComponent<CastleNpcInteractable>();
                interact.Configure(StructureId, Label, hero);
                BuildingInteractable.MarkNpcCovered(StructureId);   // the building defers — NPC owns the talk

                // Always-visible type sign above the drillmaster (same as the vendor NPCs).
                float localHeadClear = SignHeightAboveHead(body);
                InteractableSign.ForStructureId(body, StructureId, localHeadClear);
            });
        }

        // V (render-verify): the spawned body must carry >=1 ENABLED Renderer with an actual mesh.
        // Traces the counts so a capture splits "no mesh" from a real spawn. Returns false => caller
        // drops it + uses a placeholder (never an invisible drillmaster).
        private static bool VerifyNpcRenders(GameObject go, string res)
        {
            if (go == null) return false;
            int total = 0, enabledWithMesh = 0;
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            {
                if (r == null) continue;
                total++;
                if (!r.enabled) continue;
                bool hasMesh =
                    (r is SkinnedMeshRenderer smr && smr.sharedMesh != null) ||
                    (r.TryGetComponent<MeshFilter>(out var mf) && mf.sharedMesh != null);
                if (hasMesh) enabledWithMesh++;
            }
            bool ok = enabledWithMesh > 0;
            if (!ok)
                FlowTrace.Warn("Village",
                    $"VerifyNpcRenders '{res}': {total} renderer(s), {enabledWithMesh} enabled-with-mesh — reads invisible.");
            return ok;
        }

        private static float SignHeightAboveHead(GameObject body)
        {
            const float WorldClearAboveOrigin = 2.6f;
            float scaleY = body.transform.lossyScale.y;
            return scaleY > 0.01f ? WorldClearAboveOrigin / scaleY : WorldClearAboveOrigin;
        }

        // Reuse the vendor injector's height normalization so the People-pack body sits
        // at ~hero height instead of towering (packs import at varying native scales).
        private static void NormalizeToHeroHeight(GameObject go)
        {
            float npcScale = 1f;
            var rends = go.GetComponentsInChildren<Renderer>();
            if (rends.Length > 0)
            {
                Bounds b = rends[0].bounds;
                for (int i = 1; i < rends.Length; i++) b.Encapsulate(rends[i].bounds);
                if (b.size.y > 0.01f) npcScale = 1.95f / b.size.y;
            }
            if (npcScale > 0.01f && !Mathf.Approximately(npcScale, 1f))
            {
                go.transform.localScale *= npcScale;
                var bubbleRoot = go.transform.Find("BubbleRoot");
                if (bubbleRoot != null) bubbleRoot.localScale = Vector3.one / Mathf.Max(0.01f, npcScale);
            }
        }

        // The castle centre to face the drillmaster toward — the Heart (world-tree). Runtime-found;
        // CastleHubBuilder places it at (0,0,12), the fallback if the controller isn't up yet.
        private static Vector3 HeartCenter()
        {
            var h = FindAnyObjectByType<HeartController>();
            return h != null ? h.transform.position : new Vector3(0f, 0f, 12f);
        }

        private static Transform ResolveHero()
        {
            var tagged = GameObject.FindWithTag("Player");
            if (tagged != null) return tagged.transform;
            foreach (var t in FindObjectsByType<Transform>())
                if (t != null && t.name.StartsWith("Hero")) return t;
            return null;
        }
    }
}

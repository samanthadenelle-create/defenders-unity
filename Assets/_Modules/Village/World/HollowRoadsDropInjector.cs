// =============================================================================
// HollowRoadsDropInjector — seats the FOUR BIOME DROPS at the ends of the Hollow
// Roads tunnel arms, and proves the hero actually arrived.
// -----------------------------------------------------------------------------
// OWNER DIRECTIVE (2026-08-16): "place a portal to simple tunnel system that will
// drop into the new biomes". This is the far half of that spoke:
//
//   hub --[one derived portal, DungeonWorldPortalSpawner]--> dg_hollow_roads
//        --[THIS FILE: four drops, one per arm]--> the four cardinal biomes
//
// WHY A RUNTIME INJECTOR AND NOT SCENE CONTENT (CLAUDE.md sec.3):
//   dg_hollow_roads.unity is a BAKED, binary-serialized artifact, and this repo has
//   a NUL-corruption history on exactly that folder (project memory
//   `dungeon-scene-shared-tree-corruption`). Baking drops INTO the scene would also
//   freeze their world coordinates at bake time, which is the one thing they must
//   not be: the drop points are DERIVED from measured terrain bounds and have to be
//   recomputed against the world as it actually is on the day. So the tunnel scene
//   stays pure geometry from the graph JSON, and the drops are injected at load —
//   the same proven shape HubFoliageInjector and DungeonWorldPortalSpawner already
//   use for hub content.
//
// WHY THE DROPS ARE NOT `extracts` (the [dungeon-egress] law, 2026-08-15):
//   An extract is a way HOME. These are OUTBOUND doors to somewhere else, which is a
//   different kind of thing wearing a similar shape. Authoring them as extracts would
//   have put four extraction pads in one layout and pushed straight against an oracle
//   whose whole point is "one entry, one back exit". The tunnel's graph therefore
//   carries `extracts: []`, DungeonExitSpawner still injects exactly ONE front exit at
//   the mouth, and these four are their own component. Nothing about the egress
//   assertion is loosened, and dg_hollow_roads is not in its ContentLayouts list.
//
// NO SILENTLY-DEAD DOORS (the standing rule, and the defect class that bit three
// times on 2026-08-15 — the raid button, the spire plans, the treasure crate):
//   * A missing arm room, an underived drop, or an unloadable destination each mean
//     NO drop is built there, announced with FlowTrace.Fail. An arm that dead-ends is
//     visibly a dead end; it is never a portal that swallows a tap.
//   * Every drop that IS built announces its destination in its own prompt label,
//     including the region's danger tier in words.
//   * ARRIVAL IS VERIFIED. Crossing arms a one-shot check on the far side that
//     confirms the hero really landed, on navmesh, in the region we promised. A drop
//     that lands the hero in the wrong biome or off the mesh says so in the capture
//     instead of reading as a successful trip.
//
// WO-1604 (2026-09-07) — TWO CHANGES, AND THE SECOND IS THE ONE THAT MATTERED:
//   1. FAIL-CLOSED BEFORE THE DOOR. BiomeRoads.ResolveDrops now asks ZoneManager where
//      each region actually begins and REFUSES any drop whose derived point does not
//      classify as its own region. A refused road is a visible dead end plus a Fail and
//      a player-facing Notify naming it here — never a labelled door that teleports the
//      hero and complains afterwards.
//   2. THE ARRIVAL FAIL NOW SAYS WHICH HALF BROKE. It used to compute the drift from the
//      promised point and print it only on SUCCESS, so the failure line named a landing
//      position and nothing else. F8 seq 4703 ("promised Ashwood, landed (0,0.08,50),
//      classified Elarion") therefore read as a derivation defect and was ticketed as
//      one — but the world this system MEASURED for itself, in its own trace line
//      (Builds/starter-settlement-proof-r4.log:19075, "world bounds MEASURED from 1
//      terrain(s): centre (0.00, 17.00, 0.00) size (1000.00, 42.00, 1000.00)"), gives
//      a north reach of 500m and so derives the Ashwood point at z=400. The hero had
//      never been moved at all. An alarm that
//      cannot distinguish "the warp did not happen" from "the warp went to the wrong
//      biome" points the next reader at the wrong system, which is the specific way this
//      one cost a ticket. Both branches now carry promised point, drift and settle time.
//
// WO-1606 (2026-09-09) — THE UNHONOURED CONTRACT, AND THE ALARM THAT NAMED THE WRONG OWNER:
//   1. THE DROP POINT IS NOW GROUNDED BEFORE THE SEAM GETS IT. BiomeRoads.ResolveDrops is pure and
//      documents (BiomeRoads.cs:322-332) that the CALLER must ground-probe and must fail loudly if
//      it cannot. This file did neither: it handed drop.Point to SceneTransitionTrigger verbatim,
//      and that point carries worldBounds.center.y (BiomeRoads.cs:420) = 17m of open air. One
//      NavMesh.SamplePosition at seat time closes it, and a miss refuses the drop outright.
//   2. THE SETTLE FAILURE KNOWS THREE CASES NOW. It asserted "THE WARP DID NOT HAPPEN" and blamed
//      SceneTransitionTrigger / HeroLocomotion.WarpTo. On F8 seq 4706 that was FALSE: the warp
//      landed on target at (-400, 17, 0) and the hero's OWN ±50 off-mesh clamp reverted it a frame
//      later. The branch now distinguishes never-warped from warped-then-relocated and points the
//      reader at the HeroLocomotion clamp Warn that proves which. Same lesson as change 2 above,
//      one layer further down: an alarm that cannot tell two defects apart sends the reader to the
//      wrong file, and this one did it twice.
//
// WO-1692 (2026-09-10) — THE RIGHT QUESTION, ASKED IN THE WRONG SCENE:
//   WO-1606's seat-time navmesh sample of drop.Point ran INSIDE dg_hollow_roads and asked
//   about a coordinate in Main_Castle_Overworld. SceneRouter loads the tunnel SINGLE
//   (SceneManager.LoadSceneAsync, default mode, SceneRouter.cs:323), so the hub and its baked navmesh
//   are unloaded and the only mesh in memory is this corridor's. The probe could not hit on any
//   terrain in any build: F8 seq 5003-5006 (Seeker, 2026-09-10) refused ALL FOUR drops with identical
//   text and seq 5007 read "seated 0 of 4 biome drops - every tunnel arm dead-ends", whose own
//   trailing hint blamed the arm room ids. That hint was WRONG (the ids match, and
//   BiomeRoadsRegression Case 2 has been green throughout) -- a static suspicion printed by a
//   zero-count branch read as a finding, which is the same "alarm names the wrong owner" failure the
//   two entries above are about, a third time.
//   THE FIX: the hub grounds the four destinations while it is still loaded (RememberHubBounds ->
//   GroundDropsInHub: terrain height for Y, then its OWN navmesh for walkability) and the tunnel
//   consumes that memo -- exactly the shape the hub BOUNDS already used, and for the same reason.
//   The tunnel never probes for a destination it cannot see. Pinned by BiomeRoadsRegression Case 8.
// =============================================================================

using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using DeNelle.Core;
using DeNelle.Core.Diagnostics;
using DeNelle.Core.World;

namespace DeNelle.Village.World
{
    /// <summary>
    /// Injects the four biome drops into the Hollow Roads tunnel scene, and verifies the
    /// hero's arrival on the far side. Self-bootstrapping, idempotent, never throws out of
    /// a sceneLoaded handler.
    /// </summary>
    public sealed class HollowRoadsDropInjector : MonoBehaviour
    {
        private const string Sys = "BiomeRoads";

        /// <summary>Holder for the injected drops — found by name so a re-load never doubles them.</summary>
        private const string HolderName = "HollowRoadsDrops (runtime)";

        /// <summary>
        /// Root-name prefix the composed-dungeon baker gives the tunnel's geometry root
        /// (<c>DungeonCompose_&lt;graphId&gt;</c>). The arm rooms are direct children of it, named
        /// for their graph node ids — the same lookup DungeonExitSpawner uses to find its exit room.
        /// </summary>
        private const string ComposeRootPrefix = "DungeonCompose_";

        /// <summary>
        /// How far along the arm's own forward axis the drop sits, as a fraction of the arm room's
        /// MEASURED bounds — the far end, not a typed metre count, so a retuned corridor prefab
        /// carries. 0.35 of the half-depth past centre keeps the drop clear of the doorway the hero
        /// walks in through, which would otherwise fire the prompt the instant they enter the arm.
        /// </summary>
        private const float ArmEndFraction = 0.35f;

        /// <summary>
        /// Trigger radius for a drop prompt. Deliberately small and AUTHORED: SceneTransitionTrigger
        /// only honours an authored radius when the seam carries a promptOverride (its IsWalkUpEntry
        /// test), otherwise it snaps to the 40m castle-gate floor — which inside a corridor would put
        /// every one of the four drops permanently in range of the hero at once.
        /// </summary>
        private const float DropPromptRadius = 3.5f;

        /// <summary>Metres of navmesh search allowed when grounding a drop point on arrival.</summary>
        private const float ArrivalSampleRadius = 12f;

        /// <summary>
        /// How long the arrival check waits for the crossing to settle before judging it. MUST
        /// comfortably exceed SceneTransitionTrigger's own pre-warp sequence — fade-to-black 0.25s +
        /// WaitForSeconds(0.15f) + a safety frame — because the warp happens AFTER all of that. 3s
        /// leaves room for a slow load without leaving a false alarm hanging on a fast one.
        /// </summary>
        private const float ArrivalSettleBudget = 3f;

        /// <summary>How near the promised point counts as "the warp has landed". Generous, because
        /// HeroLocomotion.WarpTo re-samples onto the NavMesh and may legitimately settle a few metres
        /// off the requested point; the region assertion is what actually judges correctness.</summary>
        private const float ArrivalSettleRadius = 8f;

        private static HollowRoadsDropInjector s_instance;

        /// <summary>The region a pending crossing promised, so arrival can be checked against it.
        /// Static because it must survive the Single scene load the crossing performs.</summary>
        private static RegionId s_pendingRegion;
        private static bool s_arrivalPending;
        private static Vector3 s_promisedPoint;

        // =====================================================================
        //  Bootstrap — mirrors HubFoliageInjector exactly.
        // =====================================================================

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (s_instance != null) return;
            new GameObject(nameof(HollowRoadsDropInjector)).AddComponent<HollowRoadsDropInjector>();
        }

        private void Awake()
        {
            if (s_instance != null && s_instance != this) { Destroy(gameObject); return; }
            s_instance = this;
            DontDestroyOnLoad(gameObject);

            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
            HandleScene(SceneManager.GetActiveScene().name);
        }

        private void OnDestroy()
        {
            if (s_instance == this) s_instance = null;
            SceneManager.sceneLoaded -= OnSceneLoaded;
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode) => HandleScene(scene.name);

        // An uncaught throw out of a sceneLoaded handler halts the WebGL player — the same guard
        // HubFoliageInjector and CavePortalRepointInjector carry, for the same reason.
        private void HandleScene(string sceneName)
        {
            if (string.IsNullOrEmpty(sceneName)) return;

            if (string.Equals(sceneName, BiomeRoads.TunnelSceneId, StringComparison.OrdinalIgnoreCase))
            {
                try { InjectDrops(); }
                catch (Exception e)
                {
                    FlowTrace.Fail(Sys, $"Hollow Roads drop injection threw ({e.GetType().Name}: {e.Message}) - " +
                                        "the tunnel stands with NO biome drops. Its arms dead-end, which is " +
                                        "visibly broken rather than silently broken.");
                }
                return;
            }

            // Any other scene: if a crossing is in flight, this is the arrival.
            if (!s_arrivalPending) return;

            // ⚠ THE DESTINATION TEST IS DELIBERATELY NOT HubScenes.IsOverworld.
            //
            // The drop targets SceneRouter.Castle, which is a FLAG-DEPENDENT property: it resolves to
            // "Main_Castle_Overworld" only while ff.mergedworld is ON, and to "MainCastle_Hall"
            // otherwise. IsOverworld matches the FIRST name only. Testing with it would mean that on
            // a non-merged build the arrival never matches, s_arrivalPending stays latched forever,
            // and the NEXT unrelated overworld load runs a stale verification against a long-dead
            // promise -- a false Fail, arriving somewhere with no connection to the drop that armed
            // it. Comparing against the same SceneRouter.Castle the seam actually targets keeps the
            // two ends resolving through one authority.
            if (string.Equals(sceneName, SceneRouter.Castle, StringComparison.OrdinalIgnoreCase))
            {
                StartCoroutine(VerifyArrivalWhenSettled(sceneName));
                return;
            }

            // Any OTHER scene while a crossing was pending means the trip did not go where the drop
            // said it would. Clear the latch rather than leave it armed to mis-fire later.
            FlowTrace.Warn(Sys, $"a {BiomeRoads.ZoneName(s_pendingRegion)} drop was armed but the next scene " +
                                $"loaded was '{sceneName}', not '{SceneRouter.Castle}' - clearing the pending " +
                                "arrival check so it cannot mis-fire against an unrelated later load.");
            s_arrivalPending = false;
        }

        // =====================================================================
        //  Inject the four drops
        // =====================================================================

        private void InjectDrops()
        {
            using var _ = FlowTrace.Enter(Sys, "InjectDrops (Hollow Roads tunnel)");

            if (!FeatureFlags.BiomeRoads)
            {
                FlowTrace.Step(Sys, "ff.biomeroads is OFF - no biome drops seated. The tunnel keeps its one " +
                                    "front exit home, and its arms are plain dead ends (no dead doors).");
                return;
            }

            // Idempotent: a re-load must never stack a second set of drops.
            var prior = GameObject.Find(HolderName);
            if (prior != null) Destroy(prior);

            Transform composeRoot = FindComposeRoot();
            if (composeRoot == null)
            {
                FlowTrace.Fail(Sys, $"no '{ComposeRootPrefix}{BiomeRoads.TunnelSceneId}' root in the loaded " +
                                    "tunnel scene - the scene may not have been composed/baked from " +
                                    "dg_hollow_roads.json yet. NO drops seated.");
                return;
            }

            // The destination world must be measurable, or every drop below is a guess.
            //
            // ORDER MATTERS, AND IT IS THE MEMO FIRST — NOT the live measurement. The tunnel scene
            // carries no terrain, so calling TryMeasureWorldBounds here would emit a FlowTrace.Fail
            // on the completely normal path, every single time the player walks in. That is the
            // failure CLAUDE.md sec.14 calls out by name: a Fail that lands on every entry trains
            // every seat to ignore Fails from this system, and then the real one goes unread. The
            // hub recorded what it measured on the way past; that memo is the primary source here,
            // and the live measurement is only the fallback for the case where the tunnel somehow
            // was entered with terrain present.
            if (!TryRecallHubBounds(out Bounds worldBounds)
                && !BiomeRoads.TryMeasureWorldBounds(out worldBounds))
            {
                FlowTrace.Fail(Sys, "no recorded hub world bounds and no measurable terrain here - the hero " +
                                    "reached the tunnel without the hub ever measuring itself. NO drops seated " +
                                    "(a drop into an unmeasured world would be a guessed coordinate, and a " +
                                    "guessed door is the thing this feature refuses to ship).");
                return;
            }

            List<BiomeRoads.Drop> drops = BiomeRoads.ResolveDrops(worldBounds);
            if (drops.Count == 0)
            {
                FlowTrace.Fail(Sys, "BiomeRoads.ResolveDrops derived NO drop points - the tunnel arms dead-end.");
                // The tunnel's player-facing name is READ from its one authored home, never retyped
                // here - WO-1044 ruled that word and BiomeRoadsRegression Case 7 pins it.
                Notify($"The roads out of {BiomeRoads.TunnelDisplayName} are closed.");
                return;
            }

            // WO-1604 — THE ESCALATION FOR A REFUSED ROAD LIVES HERE, NOT IN THE RESOLVER.
            //
            // ResolveDrops now refuses (at Warn) any drop whose derived point ZoneManager does not
            // classify as its own region -- fail-closed, before the door exists, instead of the old
            // shape where the door was built, the player walked through it, the hero was teleported,
            // and only then did the arrival check discover the prompt had lied. A refusal is a
            // MISSING ROAD, which is a player-visible consequence, so it is announced here: this is
            // the layer that knows a tunnel arm is about to dead-end, and it is the layer that can
            // put a sentence in front of the player instead of only in a log nobody is reading.
            if (drops.Count < BiomeRoads.DropRegions.Length)
            {
                var missing = new List<string>();
                for (int i = 0; i < BiomeRoads.DropRegions.Length; i++)
                {
                    RegionId want = BiomeRoads.DropRegions[i];
                    bool found = false;
                    for (int j = 0; j < drops.Count; j++)
                        if (drops[j].Region == want) { found = true; break; }
                    if (!found) missing.Add(BiomeRoads.ZoneName(want));
                }

                string names = string.Join(", ", missing.ToArray());
                FlowTrace.Fail(Sys, $"{missing.Count} of {BiomeRoads.DropRegions.Length} biome roads have NO drop: " +
                                    $"{names}. Their derived points were refused because ZoneManager does not " +
                                    "classify them as their own region (the per-region Warn above carries the " +
                                    "boundary, the clearance and the classification). Those arms dead-end visibly " +
                                    "rather than promising a biome they cannot deliver.");
                Notify(missing.Count == 1
                    ? $"The road to {names} is closed."
                    : $"These roads are closed: {names}.");
            }

            var holder = new GameObject(HolderName);
            holder.transform.SetParent(composeRoot, false);

            int seated = 0;
            for (int i = 0; i < drops.Count; i++)
            {
                if (TrySeatDrop(drops[i], composeRoot, holder.transform)) seated++;
            }

            if (seated == 0)
            {
                FlowTrace.Fail(Sys, $"seated 0 of {drops.Count} biome drops - every tunnel arm dead-ends. " +
                                    "Suspect the arm room ids in dg_hollow_roads.json no longer match " +
                                    "BiomeRoads.ArmRoomIdFor.");
            }
            else
            {
                FlowTrace.Step(Sys, $"seated {seated}/{drops.Count} biome drops in the Hollow Roads. Any arm " +
                                    "not listed above has NO drop and is a visible dead end.");
            }
        }

        /// <summary>Seat one drop at the far end of its arm room. Returns false (loudly) on any gap.</summary>
        private bool TrySeatDrop(BiomeRoads.Drop drop, Transform composeRoot, Transform holder)
        {
            if (string.IsNullOrEmpty(drop.ArmRoomId))
            {
                FlowTrace.Fail(Sys, $"drop for '{drop.Region}' has no arm room id - not seated.");
                return false;
            }

            Transform arm = composeRoot.Find(drop.ArmRoomId);
            if (arm == null)
            {
                FlowTrace.Fail(Sys, $"tunnel arm '{drop.ArmRoomId}' (for {BiomeRoads.ZoneName(drop.Region)}) is " +
                                    "NOT a child of the compose root - the graph node id and this lookup have " +
                                    "drifted apart. That biome has NO drop.");
                return false;
            }

            // Seat at the arm's far end, derived from the room's MEASURED renderer bounds.
            if (!TryMeasureBounds(arm, out Bounds armBounds))
            {
                FlowTrace.Fail(Sys, $"tunnel arm '{drop.ArmRoomId}' has no renderers to measure - cannot derive " +
                                    $"a seat for the {BiomeRoads.ZoneName(drop.Region)} drop. Not seated.");
                return false;
            }

            Vector3 seat = armBounds.center + arm.forward * (armBounds.extents.z * ArmEndFraction);
            seat.y = armBounds.min.y;

            // ⚠ WO-1692 — THE DESTINATION IS GROUNDED BY THE HUB, AND THIS FILE MUST NOT RE-ASK.
            //
            // WO-1606 was right that BiomeRoads.ResolveDrops is a PURE derivation which documents
            // (BiomeRoads.cs:322-332) that the CALLER must ground-probe -- Y is left at the bounds
            // centre height, which on the live 1000x42x1000 world is SEVENTEEN METRES OF OPEN AIR,
            // and on F8 seq 4706 the hero was warped straight into it.
            //
            // But it put the probe HERE, and here cannot answer. drop.Point is a coordinate in
            // Main_Castle_Overworld; this code runs inside dg_hollow_roads, which SceneRouter loads
            // SINGLE (SceneManager.LoadSceneAsync with the default mode, SceneRouter.cs:323). The hub
            // and its baked navmesh are UNLOADED. NavMesh.SamplePosition here interrogates the
            // tunnel's own nine-room corridor about a point 400m outside it, so it misses every time,
            // on every terrain, in every build -- F8 seq 5003-5006 refused all four drops with
            // identical text and seq 5007 read "seated 0 of 4 biome drops".
            //
            // The grounding therefore happens in RememberHubBounds, called from the hub while the
            // terrain and the navmesh that own the answer are both loaded, and this file CONSUMES it.
            // Same shape the bounds already used, for the same reason.
            //
            // ⛔ DO NOT PUT A NAVMESH SAMPLE OF drop.Point BACK IN THIS METHOD. The refusal
            //    below is not a weaker check -- it is the same check, asked of the only scene that can
            //    answer it. BiomeRoadsRegression Case 8 fails if the probe returns here.
            if (!TryRecallGroundedDrop(drop.Region, out Vector3 groundedPoint))
            {
                string why = !s_hubGroundingRan
                    ? "the hub NEVER RAN its grounding pass - RememberHubBounds was not called, so the hero " +
                      "reached the tunnel without the hub ever probing its own navmesh (suspect the portal " +
                      "spawner path that calls it)"
                    : s_hubNavMeshQueryable
                        ? "the hub ran the pass with a queryable navmesh and still could not ground this road - " +
                          "the bake does not reach the derived point (the per-region Warn from the hub carries " +
                          "the derived point, the terrain-grounded point and the radius)"
                        : "the hub ran the pass but NO navmesh was queryable there at all - the probe was asked " +
                          "before the surface registered, or the hub scene carries no loaded mesh";

                FlowTrace.Fail(Sys, $"REFUSED the {BiomeRoads.ZoneName(drop.Region)} drop at seat time: the hub has " +
                                    $"no GROUNDED destination for it (derived point was {drop.Point}). Reason: {why}. " +
                                    "This file does NOT probe for itself: the destination lives in " +
                                    $"{SceneRouter.Castle}, which is unloaded while the tunnel is active " +
                                    "(SceneRouter.cs:323 loads Single), so the only navmesh in memory here is this " +
                                    "corridor's and it cannot answer for that world. The arm dead-ends VISIBLY " +
                                    "instead of teleporting the hero off the walkable world. NOT seated.");
                Notify($"The road to {BiomeRoads.ZoneName(drop.Region)} is closed.");
                return false;
            }

            var go = new GameObject($"BiomeDrop_{drop.Region}");
            go.transform.SetParent(holder, false);
            go.transform.position = seat;

            // The trigger volume. Kinematic RB so a CharacterController/agent hero trips it, matching
            // the DungeonPortal idiom.
            var col = go.AddComponent<SphereCollider>();
            col.isTrigger = true;
            col.radius = DropPromptRadius;
            var rb = go.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.useGravity = false;

            // REUSE the existing crossing machinery — no fifth transition system.
            var seam = go.AddComponent<SceneTransitionTrigger>();
            seam.targetSceneName = SceneRouter.Castle;
            seam.targetPosition = groundedPoint;
            seam.loadAdditive = false;
            seam.ProximityRadius = DropPromptRadius;
            // A non-empty promptOverride is what marks this a WALK-UP entry, which is what keeps
            // SceneTransitionTrigger from widening the radius to its 40m castle-gate floor.
            seam.promptOverride = BiomeRoads.TravelLabel(drop.Region);
            seam.suppressPrompt = false;

            // Wear the ONE shared portal look. Async and failure-tolerant: the bare trigger stands
            // (and still works) if the content build is missing, which is why the art swap can never
            // be the thing that decides whether the door functions.
            var wearer = go.AddComponent<PortalArtWearer>();
            wearer.Begin(go.transform);

            // Arm the arrival check the moment this drop is taken.
            var announce = go.AddComponent<BiomeDropAnnouncer>();
            announce.Region = drop.Region;
            announce.PromisedPoint = groundedPoint;

            FlowTrace.Step(Sys, $"drop seated: {BiomeRoads.ZoneName(drop.Region)} (tier " +
                                $"{BiomeRoads.DangerTier(drop.Region)}, {BiomeRoads.Cardinal(drop.Region)}) at arm " +
                                $"'{drop.ArmRoomId}' seat {seat} -> {SceneRouter.Castle} @ {groundedPoint} " +
                                $"(HUB-GROUNDED from derived {drop.Point}, terrain+navmesh moved it " +
                                $"{Vector3.Distance(drop.Point, groundedPoint):F1}m). " +
                                $"Derivation: {drop.Derivation}");
            return true;
        }

        private static Transform FindComposeRoot()
        {
            string want = ComposeRootPrefix + BiomeRoads.TunnelSceneId;
            var scene = SceneManager.GetActiveScene();
            if (!scene.IsValid()) return null;

            var roots = scene.GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++)
            {
                if (roots[i] == null) continue;
                if (string.Equals(roots[i].name, want, StringComparison.OrdinalIgnoreCase))
                    return roots[i].transform;
            }
            // Prefix-only fallback: the baker's suffix convention has moved before.
            for (int i = 0; i < roots.Length; i++)
            {
                if (roots[i] == null) continue;
                if (roots[i].name.StartsWith(ComposeRootPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    FlowTrace.Warn(Sys, $"compose root '{roots[i].name}' does not match the expected " +
                                        $"'{want}' - using it anyway, but the naming convention has drifted.");
                    return roots[i].transform;
                }
            }
            return null;
        }

        private static bool TryMeasureBounds(Transform root, out Bounds bounds)
        {
            bounds = default;
            bool any = false;
            var rends = root.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < rends.Length; i++)
            {
                var r = rends[i];
                if (r == null) continue;
                if (!any) { bounds = r.bounds; any = true; } else bounds.Encapsulate(r.bounds);
            }
            return any;
        }

        // =====================================================================
        //  Hub bounds memo — measured in the hub, recalled inside the tunnel
        // =====================================================================
        // The tunnel scene has no terrain, so the world it drops INTO cannot be measured from
        // inside it. Rather than fall back to a typed extent (which would make the whole
        // "derived, never typed" property a fiction the moment the hero is underground), the
        // hub records what it measured on the way past and the tunnel recalls it.

        private static bool s_hubBoundsKnown;
        private static Bounds s_hubBounds;

        // ── WO-1692 — THE GROUNDING MEMO, AND WHY IT CANNOT LIVE IN THE TUNNEL ──────────────
        //
        // WO-1606 put a navmesh sample of drop.Point in TrySeatDrop. The intent was
        // right and the PLACE was not, and the difference cost every road: the tunnel is entered
        // through SceneRouter, whose loader is SceneManager.LoadSceneAsync(name) with the DEFAULT
        // mode -- LoadSceneMode.Single (SceneRouter.cs:323). By the time this file runs inside
        // dg_hollow_roads the hub scene is UNLOADED, and its baked navmesh
        // (Assets/Scenes/Main_Castle_Overworld/NavMesh-Main_Castle_Overworld.asset, referenced at
        // Main_Castle_Overworld.unity:17047) went with it. The only navmesh in memory is the
        // tunnel's own nine-room corridor, and the question being asked of it is about a point
        // 400m away in a different world. It cannot hit, on any terrain, in any build -- which is
        // why F8 seq 5003-5006 refused all FOUR drops with identical text and seq 5007 read
        // "seated 0 of 4 biome drops".
        //
        // The file already knew the hub was gone: it recalls the hub's BOUNDS from a memo for
        // exactly this reason (see the comment on TryRecallHubBounds's caller). The navmesh needed
        // the same treatment. So the probe moves to RememberHubBounds, which runs in the hub with
        // terrain AND navmesh live, and the tunnel consumes the answer instead of re-asking it.
        //
        // ⛔ DO NOT "RESTORE" A NAVMESH PROBE TO THE TUNNEL SIDE. A destination-scene coordinate
        //    cannot be validated from a scene that is not the destination. BiomeRoadsRegression
        //    Case 8 fails if one comes back.
        private static readonly Dictionary<RegionId, Vector3> s_hubGroundedDrops =
            new Dictionary<RegionId, Vector3>();

        /// <summary>True once the hub has RUN the grounding pass (whatever it concluded).</summary>
        private static bool s_hubGroundingRan;

        /// <summary>
        /// Control result: was ANY navmesh queryable near the world origin at memo time? This is the
        /// one measurement that separates the two causes the old seat-time message admitted it could
        /// not tell apart -- "the navmesh does not bake that far out" (origin hits, 400m misses) from
        /// "there was no queryable navmesh at all when we asked" (origin misses too, i.e. we asked
        /// before the surface registered). Without it, a reader of a refusal has to guess.
        /// </summary>
        private static bool s_hubNavMeshQueryable;

        /// <summary>
        /// Called from the hub while the terrain IS loaded, so the tunnel can derive against real
        /// measured geometry rather than a constant -- and (WO-1692) so the four destinations can be
        /// GROUNDED against the hub's own navmesh, which is the only navmesh that can answer for them.
        /// </summary>
        public static void RememberHubBounds(Bounds bounds)
        {
            s_hubBounds = bounds;
            s_hubBoundsKnown = true;
            FlowTrace.Step(Sys, $"hub world bounds remembered for the tunnel: centre {bounds.center} " +
                                $"size {bounds.size}.");

            GroundDropsInHub(bounds);
        }

        /// <summary>
        /// Ground the four derived drop points HERE, in the hub, where the terrain and the navmesh
        /// that own the answer are both loaded. Remembers a walkable point per region, or remembers
        /// nothing for a region it could not ground -- never a guess.
        /// </summary>
        private static void GroundDropsInHub(Bounds bounds)
        {
            s_hubGroundedDrops.Clear();
            s_hubGroundingRan = true;

            // CONTROL PROBE FIRST. Ask the same authority a question whose answer we already know
            // (the hero is standing in this world), so a later refusal can be read without guessing.
            Vector3 originProbe = GroundY(new Vector3(0f, bounds.center.y, 0f), bounds);
            s_hubNavMeshQueryable = NavMesh.SamplePosition(originProbe, out NavMeshHit originHit,
                                                           ArrivalSampleRadius, NavMesh.AllAreas);
            FlowTrace.Step(Sys, s_hubNavMeshQueryable
                ? $"hub navmesh control probe HIT at {originHit.position} (asked at {originProbe}) - the " +
                  "mesh is queryable, so any drop refused below is refused for REACH, not for absence."
                : $"hub navmesh control probe MISSED at {originProbe} within {ArrivalSampleRadius}m - NO " +
                  "navmesh was queryable in the hub at memo time. Any refusal below says nothing about " +
                  "how far the mesh reaches; it says the question was asked too early or the hub has no " +
                  "baked mesh loaded.");

            List<BiomeRoads.Drop> drops = BiomeRoads.ResolveDrops(bounds);
            for (int i = 0; i < drops.Count; i++)
            {
                // Drop is a STRUCT, so this is a local copy and re-seating its Y mutates nothing the
                // resolver owns.
                //
                // TWO CORRECTIONS, IN ORDER, AND THE FIRST IS WHY THE SECOND CAN SUCCEED.
                //
                // (a) Y. ResolveDrops is pure and leaves Y at worldBounds.center.y (BiomeRoads.cs:420)
                //     -- 17m of open air on the live 1000x42x1000 world. On a terrain 42m tall the
                //     vertical error alone can exceed the sample radius, so probing the raw point
                //     would miss even where the mesh is perfectly good. The terrain answers "where is
                //     the ground at this x/z" exactly, so it is asked first, and the answer is written
                //     back INTO drop.Point -- there is one point being probed, not a shadow copy.
                // (b) The navmesh probe, which is the actual walkability question. That is
                //     TryGroundDrop, and it is the WO-1091 fail-closed branch, moved to the scene that
                //     can answer it rather than weakened.
                BiomeRoads.Drop drop = drops[i];
                Vector3 derived = drop.Point;
                drop.Point = GroundY(drop.Point, bounds);

                if (!TryGroundDrop(drop, out Vector3 walkable)) continue;

                s_hubGroundedDrops[drop.Region] = walkable;
                FlowTrace.Step(Sys, $"hub grounded the {BiomeRoads.ZoneName(drop.Region)} drop: derived " +
                                    $"{derived} -> terrain {drop.Point} -> navmesh {walkable} " +
                                    $"(moved {Vector3.Distance(derived, walkable):F1}m). {drop.Derivation}");
            }

            if (s_hubGroundedDrops.Count != BiomeRoads.DropRegions.Length)
            {
                FlowTrace.Warn(Sys, $"hub grounded {s_hubGroundedDrops.Count} of {BiomeRoads.DropRegions.Length} " +
                                    "biome drops. The tunnel will seat exactly the grounded ones and name every " +
                                    "road it cannot open - it does NOT re-probe (its own scene's navmesh cannot " +
                                    "answer for the hub; SceneRouter.cs:323 loads it Single).");
            }
        }

        /// <summary>
        /// THE WO-1091 FAIL-CLOSED PROBE, run where it can be answered.
        /// <para>
        /// WO-1091 item 1 is one sentence: a drop that cannot be grounded seats NO door, and says so
        /// loudly -- FlowTrace.Fail for the capture, a player-facing Notify for the screen, and no
        /// seat. That contract is UNCHANGED here. What WO-1692 changed is WHERE it runs: the hub,
        /// while the terrain and the navmesh that own the answer are loaded, instead of the tunnel,
        /// where the destination scene is unloaded and the question is unanswerable. A probe that
        /// cannot hit is not a fail-closed guard, it is a closed door with a reason attached.
        /// </para>
        /// <para>
        /// The radius is <see cref="ArrivalSampleRadius"/>, the same figure the arrival judge uses --
        /// choosing the point more tightly than the trip is judged is how a door passes seating and
        /// fails arrival, so there is ONE radius in this file and this is it.
        /// </para>
        /// </summary>
        private static bool TryGroundDrop(BiomeRoads.Drop drop, out Vector3 walkable)
        {
            walkable = drop.Point;

            if (!NavMesh.SamplePosition(drop.Point, out NavMeshHit groundHit, ArrivalSampleRadius, NavMesh.AllAreas))
            {
                // Both verdict words are computed OUT of the interpolation holes on purpose:
                // CLAUDE.md sec.1 -- CompileGate's brace scanner has no interpolated-string model, so a
                // nested quote inside a {...} hole can make a balanced file read as unbalanced and
                // withhold COMPILE_GATE_OK.
                string controlWord = s_hubNavMeshQueryable ? "HIT" : "MISSED";
                string reading = s_hubNavMeshQueryable
                    ? "the bake not reaching that far out"
                    : "no queryable hub navmesh at all";

                FlowTrace.Fail(Sys, $"REFUSED the {BiomeRoads.ZoneName(drop.Region)} drop: the hub could not ground " +
                                    $"it. The point was terrain-seated to {drop.Point} and has NO navmesh within " +
                                    $"{ArrivalSampleRadius}m of THAT, so no walkable destination can be promised " +
                                    $"there. The origin control probe {controlWord}, which reads as {reading}. No " +
                                    "door is seated for this road: the tunnel arm dead-ends VISIBLY instead of " +
                                    "teleporting the hero off the walkable world.");
                Notify($"The road to {BiomeRoads.ZoneName(drop.Region)} is closed.");
                return false;
            }

            // The probe's OWN hit is the grounded point -- both consumers get this one, and nothing
            // downstream ever sees the derived coordinate again.
            Vector3 groundedPoint = groundHit.position;
            walkable = groundedPoint;
            return true;
        }

        /// <summary>
        /// Put <paramref name="point"/> on the ground: terrain height at that x/z if a terrain owns
        /// the spot, else a downward raycast from above the measured world, else the point unchanged
        /// (and said so). MEASURED every time -- no typed floor height anywhere in here.
        /// </summary>
        private static Vector3 GroundY(Vector3 point, Bounds bounds)
        {
            Terrain terrain = Terrain.activeTerrain;
            if (terrain != null)
            {
                // Terrain.SampleHeight is terrain-relative; the transform's own y makes it world.
                float y = terrain.SampleHeight(point) + terrain.transform.position.y;
                return new Vector3(point.x, y, point.z);
            }

            float top = bounds.max.y + 50f;
            if (Physics.Raycast(new Vector3(point.x, top, point.z), Vector3.down,
                                out RaycastHit rh, top - bounds.min.y + 100f, ~0, QueryTriggerInteraction.Ignore))
                return new Vector3(point.x, rh.point.y, point.z);

            FlowTrace.Warn(Sys, $"GroundY found neither an active Terrain nor a collider under {point} - the " +
                                "point keeps its derived (bounds-centre) height, which is metres in the air. The " +
                                "navmesh probe that follows will almost certainly refuse it, and that refusal is " +
                                "honest rather than a guessed floor.");
            return point;
        }

        private static bool TryRecallHubBounds(out Bounds bounds)
        {
            bounds = s_hubBounds;
            return s_hubBoundsKnown;
        }

        /// <summary>Recall the hub-grounded, walkable destination for a region. False = the hub never
        /// grounded that road, and the tunnel must refuse it rather than invent one.</summary>
        private static bool TryRecallGroundedDrop(RegionId region, out Vector3 grounded)
            => s_hubGroundedDrops.TryGetValue(region, out grounded);

        // =====================================================================
        //  Arrival verification
        // =====================================================================

        /// <summary>Arm the far-side check. Called by a drop as the player takes it.</summary>
        public static void ArmArrival(RegionId region, Vector3 promisedPoint)
        {
            s_pendingRegion = region;
            s_promisedPoint = promisedPoint;
            s_arrivalPending = true;
            FlowTrace.Step(Sys, $"crossing armed: promised {BiomeRoads.ZoneName(region)} @ {promisedPoint}. " +
                                "Arrival will be verified on the far side.");
        }

        /// <summary>
        /// Wait for the crossing to actually SETTLE, then verify. This coroutine exists because the
        /// obvious version -- checking inside the sceneLoaded handler -- is WRONG, and wrong in the
        /// worst way: it would have reported a false failure on essentially every crossing.
        /// <para>
        /// SceneTransitionTrigger does not warp on load. Its RepositionPlayerAfterLoad coroutine runs
        /// fade-to-black (0.25s) -> WaitForSeconds(0.15f) -> a safety frame -> and only THEN calls
        /// HeroLocomotion.WarpTo. So at sceneLoaded the hero is still standing at its pre-warp
        /// carried position, roughly 0.4s before it is moved to the promised point. A check at that
        /// instant reads the wrong position, concludes the wrong region, and shouts -- an alarm that
        /// fires every single time is worse than no alarm, because it trains the next reader to
        /// ignore this system (CLAUDE.md sec.14).
        /// </para>
        /// <para>
        /// So: poll until the hero is actually near the promised point, or until the settle budget
        /// expires. The budget must comfortably exceed the seam's own 0.25 + 0.15 + frames.
        /// </para>
        /// </summary>
        private System.Collections.IEnumerator VerifyArrivalWhenSettled(string sceneName)
        {
            // Consume the latch immediately: whatever happens from here, this promise is spent and
            // must not be able to fire again on a later load.
            s_arrivalPending = false;

            float waited = 0f;
            Transform hero = null;

            // WO-1606 — CLOSEST APPROACH, not just the final position.
            //
            // The failure branch below has to answer "did the hero ever reach the point", and the
            // FINAL position cannot answer it: a hero who arrives on target and is moved away one
            // frame later reads identically to a hero who was never moved at all. That ambiguity is
            // precisely what mis-routed F8 seq 4706. Tracking the nearest the hero ever got costs one
            // float and turns the question into a measurement instead of an inference.
            float closestDrift = float.PositiveInfinity;

            while (waited < ArrivalSettleBudget)
            {
                if (hero == null)
                {
                    GameObject found = null;
                    try { found = GameObject.FindWithTag("Player"); }
                    catch (UnityException) { found = null; }
                    if (found != null) hero = found.transform;
                }

                if (hero != null)
                {
                    float d = Vector3.Distance(new Vector3(hero.position.x, 0f, hero.position.z),
                                               new Vector3(s_promisedPoint.x, 0f, s_promisedPoint.z));
                    if (d < closestDrift) closestDrift = d;
                    // Settled: the warp has landed the hero at (or very near) the promised point.
                    if (d <= ArrivalSettleRadius) break;
                }

                waited += Time.unscaledDeltaTime;   // unscaled: survives a paused/faded timeScale
                yield return null;
            }

            VerifyArrival(sceneName, hero, waited, closestDrift);
        }

        /// <summary>
        /// Confirm the hero really landed: on navmesh, near the promised point, and classified into
        /// the region the prompt named. Every failure is LOUD — a drop that quietly puts the player
        /// somewhere else is the same defect as a door that does nothing, just harder to notice.
        /// </summary>
        private void VerifyArrival(string sceneName, Transform heroTransform, float settleSeconds,
                                   float closestDrift)
        {
            // The hero was resolved by the settle loop, which already retried across the whole budget
            // (the hero can be mid-carry and un-taggable for the first frames of a Single load).
            if (heroTransform == null)
            {
                FlowTrace.Fail(Sys, $"arrived in '{sceneName}' after a {BiomeRoads.ZoneName(s_pendingRegion)} drop " +
                                    $"but NO 'Player'-tagged hero appeared within {ArrivalSettleBudget:0.0}s - " +
                                    "cannot confirm the trip landed.");
                return;
            }

            Vector3 at = heroTransform.position;
            RegionId landed = ZoneManager.GetZone(at);
            bool onMesh = NavMesh.SamplePosition(at, out NavMeshHit hit, ArrivalSampleRadius, NavMesh.AllAreas);
            float drift = Vector3.Distance(new Vector3(at.x, 0f, at.z),
                                           new Vector3(s_promisedPoint.x, 0f, s_promisedPoint.z));

            if (!onMesh)
            {
                FlowTrace.Fail(Sys, $"{BiomeRoads.ZoneName(s_pendingRegion)} drop landed the hero at {at}, which is " +
                                    $"NOT within {ArrivalSampleRadius}m of any navmesh - the hero is stranded off " +
                                    "the walkable world. The drop point needs re-deriving or the navmesh does not " +
                                    $"reach that far out. Promised {s_promisedPoint}, drift {drift:F1}m, settled in " +
                                    $"{settleSeconds:F2}s (WO-1604: the numbers ride on EVERY arrival failure now, " +
                                    "so the capture never again forces the reader to infer which half broke).");
                Notify($"The road out of {BiomeRoads.ZoneName(s_pendingRegion)} is not walkable yet.");
                return;
            }

            // ⚠ WO-1604 — THE DRIFT TEST COMES FIRST, AND THE ORDER IS THE WHOLE FIX HERE.
            //
            // Until 2026-09-07 this method computed `drift` and then printed it ONLY on success,
            // while the mismatch Fail named just the landing position. That single omission cost a
            // ticket: F8 seq 4703 read "promised Ashwood, landed at (0.00, 0.08, 50.00), classified
            // Elarion", the only honest reading of which is "the derived point and the split
            // disagree" -- so WO-1604 was minted against the derivation. It was the wrong suspect.
            // The live terrain is 1000x1000 seated at (-500,-4,-500) (Main_Castle_Overworld,
            // ExteriorTerrain), so the Ashwood point derives ~400m out, nowhere near z=50; the hero
            // had simply never been moved, and the settle loop timed out and judged wherever they
            // happened to be standing.
            //
            // A DROP THAT NEVER MOVED THE HERO AND A DROP THAT MOVED THEM INTO THE WRONG BIOME ARE
            // DIFFERENT DEFECTS IN DIFFERENT SYSTEMS, and a message that cannot tell them apart
            // sends the next reader to the wrong file. Both branches now carry the promised point,
            // the drift and the settle time, so the capture answers the question by itself.
            if (drift > ArrivalSettleRadius)
            {
                // ⚠ WO-1606 — THERE ARE THREE CASES HERE, NOT TWO, AND THIS MESSAGE USED TO ASSERT
                //    THE WRONG ONE AS FACT.
                //
                // Until 2026-09-09 this branch ended with the flat sentence "THE WARP DID NOT
                // HAPPEN" and named SceneTransitionTrigger.RepositionPlayerAfterLoad /
                // HeroLocomotion.WarpTo as the culprits. On F8 seq 4706 that sentence was FALSE.
                // The warp happened, and it LANDED -- at (-400, 17, 0), exactly the point the drop
                // asked for. The hero was then relocated to the ±50 playable-bounds edge by
                // HeroLocomotion's own off-mesh clamp (HeroLocomotion.cs:1388-1409) on the very next
                // frame, because 17m in the air is off-mesh. A reader following this message went
                // hunting in the two systems that had done their jobs correctly. An alarm that names
                // the wrong owner costs more than no alarm.
                //
                // So the failure stays exactly as loud, and gets a THIRD verdict:
                //   never warped      -- closest approach stayed wide for the whole budget
                //   warped, relocated -- the hero WAS near the point at some sampled frame, or is
                //                        now sitting on a bound that the promised point is outside
                //   (and the mismatch case below, which is unchanged)
                //
                // Neither signal ASSERTS the clamp: the message names the one log line that proves
                // it, which HeroLocomotion already emits and this file must not duplicate.
                bool everReached = closestDrift <= ArrivalSettleRadius;

                // The clamp's half-extent, read from HeroLocomotion.cs:1391 (`const float
                // PlayableHalf = 50f`). It is quoted here ONLY to shape a diagnostic sentence -- no
                // behaviour keys off it, so a drift in that constant degrades this hint rather than
                // breaking a door. The exact-boundary test is deliberately a BAND, not an equality:
                // the clamp writes the bound bit-exactly, then ground-snap and the agent move the
                // hero a little off it before this check reads the position (seq 4706 landed at
                // x=-50.34 from a clamp to -50).
                const float ClampHalfHint = 50f;
                const float ClampBandHint = 2f;
                bool promisedOutsideClamp = Mathf.Abs(s_promisedPoint.x) > ClampHalfHint ||
                                            Mathf.Abs(s_promisedPoint.z) > ClampHalfHint;
                bool sittingOnClampEdge =
                    Mathf.Abs(Mathf.Abs(at.x) - ClampHalfHint) <= ClampBandHint ||
                    Mathf.Abs(Mathf.Abs(at.z) - ClampHalfHint) <= ClampBandHint;
                bool clampSignature = promisedOutsideClamp && sittingOnClampEdge;

                string verdict = everReached
                    ? "THE WARP LANDED AND SOMETHING THEN MOVED THE HERO. The settle poll SAW them " +
                      $"within {closestDrift:F1}m of the promised point before they ended up here, so " +
                      "the crossing did its job and the defect is downstream of it."
                    : clampSignature
                        ? "THE WARP MAY WELL HAVE LANDED AND BEEN REVERTED - the settle poll never " +
                          $"sampled the hero nearer than {closestDrift:F1}m, but the promised point lies " +
                          $"OUTSIDE the hero's ±{ClampHalfHint:0.#}m off-mesh playable-bounds clamp and the " +
                          "hero is now sitting ON that bound, which is that clamp's signature and not a " +
                          "warp that never fired."
                        : $"THE WARP DID NOT HAPPEN - the hero never came nearer than {closestDrift:F1}m " +
                          "to the promised point at any sampled frame of the whole budget.";

                string owner = (everReached || clampSignature)
                    ? "DO NOT START IN SceneTransitionTrigger OR HeroLocomotion.WarpTo. Grep this same " +
                      "capture for the line '[Flow:HeroLoco] playable-bounds CLAMP relocated the hero' " +
                      "(HeroLocomotion.cs:1401-1409, throttled to 1/sec) - it names the before/after and " +
                      "the agent state, and it is the proof of who moved them. If that line IS present, " +
                      "the owner is the clamp and the real question is why the destination was off-mesh; " +
                      "if it is ABSENT, look for a spawn placement that overrode the warp."
                    : "This is a CROSSING failure (SceneTransitionTrigger.RepositionPlayerAfterLoad / " +
                      "HeroLocomotion.WarpTo, or a spawn placement that overrode it), NOT a disagreement " +
                      "between the drop derivation and the region split - the drop point is refused " +
                      "before the door is built if it does not classify as its own region, and refused " +
                      "again at seat time if it has no navmesh under it.";

                FlowTrace.Fail(Sys, $"the {BiomeRoads.ZoneName(s_pendingRegion)} drop did not settle: after " +
                                    $"{settleSeconds:F2}s (budget {ArrivalSettleBudget:0.0}s) the hero is at {at}, " +
                                    $"{drift:F1}m from the promised point {s_promisedPoint} (closest approach " +
                                    $"{closestDrift:F1}m) - well outside the {ArrivalSettleRadius:0.#}m settle " +
                                    $"radius. ZoneManager classifies where they actually are as " +
                                    $"{BiomeRoads.ZoneName(landed)}, which says nothing about the derived point. " +
                                    $"VERDICT: {verdict} {owner}");
                Notify($"The road to {BiomeRoads.ZoneName(s_pendingRegion)} did not carry you through.");
                return;
            }

            if (landed != s_pendingRegion)
            {
                FlowTrace.Fail(Sys, $"drop promised {BiomeRoads.ZoneName(s_pendingRegion)} but the hero landed at " +
                                    $"{at}, which ZoneManager classifies as {BiomeRoads.ZoneName(landed)}. The " +
                                    $"promised point was {s_promisedPoint} and the hero settled {drift:F1}m from " +
                                    $"it in {settleSeconds:F2}s - i.e. the warp DID land, so this really is the " +
                                    "derived point and the region split disagreeing, and the prompt told the " +
                                    "player something untrue. That should now be unreachable: ResolveDrops " +
                                    "classifies every point against ZoneManager and refuses the drop before the " +
                                    "door is seated, so reaching this line means the classification changed " +
                                    "between injection and arrival, or the hero settled across a region edge.");
                Notify($"The road promised {BiomeRoads.ZoneName(s_pendingRegion)} but led to " +
                       $"{BiomeRoads.ZoneName(landed)}.");
                return;
            }

            FlowTrace.Step(Sys, $"arrival CONFIRMED: hero at {at} in {BiomeRoads.ZoneName(landed)} " +
                                $"(tier {BiomeRoads.DangerTier(landed)}), {drift:F1}m from the promised point, " +
                                $"navmesh hit at {hit.position}, settled in {settleSeconds:F2}s. " +
                                "The drop did what its label said.");
        }

        private static void Notify(string message)
        {
            // Cross-module, always null-conditional-safe: a missing UI kit must never turn a
            // diagnostic into a crash.
            try { DeNelle.Core.UI.ElarionUiKit.ShowToast(message, DeNelle.Core.UI.ElarionUiKit.ToastTone.Danger, 3.2f); }
            catch (Exception e) { FlowTrace.Warn(Sys, $"toast failed (non-fatal): {e.Message}"); }
        }
    }

    /// <summary>
    /// Arms the arrival check when its drop is actually taken. Split into its own tiny component
    /// rather than folded into the injector because the injector is DontDestroyOnLoad and the drop
    /// is not — the thing that knows "this specific door was used" has to live ON the door.
    /// </summary>
    public sealed class BiomeDropAnnouncer : MonoBehaviour
    {
        public RegionId Region;
        public Vector3 PromisedPoint;

        private bool _armed;
        private bool _heroInRange;

        // WHY PROXIMITY AND NOT THE TAP: SceneTransitionTrigger keeps its "_fired" latch private and
        // owns the MobileInteractButton callback, so a drop cannot observe its own tap without
        // reaching into the seam - and duplicating the seam to get at it would be the fifth
        // transition system this feature exists to avoid building. The scene UNLOAD is the reliable
        // signal that a door was taken; the in-range test is what makes it specific to THIS door.
        //
        // WITHOUT THE IN-RANGE TEST THIS WOULD BE A BUG, not a nicety: OnDisable fires on ALL FOUR
        // drops when the tunnel unloads - including when the player leaves by the front exit home -
        // so an unconditional arm would promise a biome arrival on every exit and then verify the
        // wrong one. The drop radius is 3.5m and each drop sits in its own arm room, so at most one
        // can hold the hero.
        private void OnTriggerEnter(Collider other)
        {
            if (other == null) return;
            if (other.CompareTag("Player")) _heroInRange = true;
        }

        private void OnTriggerExit(Collider other)
        {
            if (other == null) return;
            if (other.CompareTag("Player")) _heroInRange = false;
        }

        private void OnDisable()
        {
            if (_armed || !_heroInRange) return;
            if (!DeNelle.Core.FeatureFlags.BiomeRoads) return;
            _armed = true;
            HollowRoadsDropInjector.ArmArrival(Region, PromisedPoint);
        }
    }

    /// <summary>
    /// Wears the ONE shared portal look (<see cref="PortalStructure"/>) on a drop. Presentation
    /// only: it never touches the seam, the trigger or the routing, so a missing content build
    /// degrades the drop to a bare (still functional) door rather than an invisible one.
    /// </summary>
    public sealed class PortalArtWearer : MonoBehaviour
    {
        private PortalStructure.SwapResult _swap;
        private bool _started;

        public void Begin(Transform host)
        {
            if (_started || host == null) return;
            _started = true;
            SwapAsync(host).Forget();
        }

        private async Cysharp.Threading.Tasks.UniTaskVoid SwapAsync(Transform host)
        {
            // Interior height is derived from the hero, never a typed world scale.
            var swap = await PortalStructure.SwapInAsync(host, PortalStructure.InteriorHeight, "BiomeDrop_Portal");

            // ⚠ THE LOAD CAN OUTLIVE THIS COMPONENT, and the naive version LEAKS FOR THE SESSION.
            // Addressables is async; the player can leave the tunnel (or the scene can unload) while
            // the bundle is still in flight. OnDestroy then runs FIRST, and PortalStructure.Release
            // is a no-op on a still-default handle (its IsValid guard) -- so the await resumes
            // afterwards, assigns a VALID handle to a destroyed component, and nothing ever releases
            // it. Catching that here is the only place it can be caught: the result must be released
            // on the spot when there is no longer anything to hang it on.
            if (this == null || host == null)
            {
                PortalStructure.Release(ref swap);
                FlowTrace.Step("BiomeRoads", "portal art finished loading after its drop was torn down - " +
                                             "handle released immediately rather than leaked for the session.");
                return;
            }

            _swap = swap;
            if (!_swap.Ok)
            {
                FlowTrace.Warn("BiomeRoads", $"'{name}' could not wear the shared portal art - the drop still " +
                                             "works, it just looks bare.");
            }
        }

        private void OnDestroy() => PortalStructure.Release(ref _swap);
    }
}

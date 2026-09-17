// =============================================================================
// HubHuskFreeSceneRegression [hub-husk-free]
// -----------------------------------------------------------------------------
// Assembly: DeNelle.EditorRegression   Namespace: DeNelle.Editor.Regression
// Markers:  HUB_HUSK_FREE_OK / HUB_HUSK_FREE_FAIL
// Registered in DataRegression.RunAll.
//
// WHAT THIS IS, AND WHY IT IS A SECOND ORACLE RATHER THAN A CASE IN THE FIRST ONE.
//
//   StructureRemovalHuskRegression pins the CODE half of WO-1716 against fixtures:
//   StripHostVisual + EnsureNoHusk can no longer MINT an invisible-but-solid host. Its
//   header states, in so many words, that it deliberately does NOT assert the hub scene
//   is clean, because at the time it was written the scene still carried the proven
//   `CastleBarracks` ghost and a red marker for a known, ticketed reason trains readers
//   to ignore the marker.
//
//   WO-1809 owns the scene half. Its repair path is `StructureHuskCleanup.RemoveBatch`
//   followed by `NavMeshBakeFinal.Run`, and THIS oracle is what makes that repair
//   permanent: without it the fix is a one-time command whose result nothing watches, and
//   the next accidental strip-and-save puts the ghost straight back.
//
// THE CAPTURED DEFECT IT GUARDS (all three proofs, WO-1809, 2026-09-16)
//   YAML   Main_Castle_Overworld.unity PrefabInstance &1439394418 (polyperfect
//          Military_Barracks, m_Name CastleBarracks, local (16,0,-4), scale 0.6/0.9/0.6)
//          removed exactly the MeshRenderer (5621597281765024167) and the MeshFilter
//          (5621597281765991271) and kept the prefab's MeshCollider
//          (5621597281762736578) with m_AddedComponents: [] - invisible, solid, orphaned.
//   HEADLESS StructureHuskCleanup.PreviewBatch, Builds/wo1809-husk-preview.log:
//          "CANDIDATE 'CastleBarracks' renders nothing but keeps 1 enabled solid
//          collider(s) ... worldPos=(16.00, 0.00, -4.00)".
//   DEVICE logcat-after-372984.txt (SM02G4061955851, 2026-09-16): three
//          "[Flow:Camera] CAMERA SEAT EMBEDDED IN UNSEEN GEOMETRY ... overlaps collider
//          'CastleBarracks'" lines and six "[Flow:Repair] tap hit 'CastleBarracks' ...
//          [0] CastleBarracks tag=Untagged {MeshCollider}" lines. The player was walking
//          and tapping into it. HubStructureVisualInjector.SuppressBakedTwinPhysics
//          (WO-950) only disables it on the StoodDown branch; on LatchSkipped it stayed
//          live, which is why one runtime suppression path was not enough.
//
// WHAT IS ASSERTED, against the real saved scene - content, never prose
//   1 [opens]      The hub resolves from SceneRouter.Castle (never a typed-in literal -
//                  HubSceneLiteralRegression) and opens with roots. Zero roots is a FAIL,
//                  not a skip: an oracle that censused nothing has not run.
//   2 [named-pin]  No GameObject named `CastleBarracks` that renders nothing survives.
//                  Named explicitly because that object IS the owner's report.
//   3 [husk]       No *Barracks* host anywhere in the scene is an invisible-but-solid husk
//                  by the SHARED predicate (StructureVisualStrip.IsInvisibleNavBlockingHusk
//                  - the one owner; a second copy of the rule here is exactly the
//                  duplicated state CLAUDE.md sec.5 forbids).
//   4 [bounds]     A *Barracks* host that DOES render may not carry a solid collider
//                  reaching more than BoundsSlackMetres beyond what it renders on any
//                  axis. This is the half-ghost the predicate cannot see: a collider that
//                  is not orphaned but is still describing a mesh nobody swapped out.
//   5 [carve]      Scene-wide, no enabled CARVING NavMeshObstacle larger than
//                  MaxUnseenCarveMetres on any world axis sits on a subtree that renders
//                  nothing. The shipped ghost carve was 16.90712 x 5.08288 x 14.51318 -
//                  the owner's own captured Size - so the ceiling is set below it.
//
// ⛔ WHY CASES 3 AND 4 ARE SCOPED TO *BARRACKS* AND NOT TO THE WHOLE SCENE. Measured, not
//   assumed: the same Preview run above reported TWELVE other husk-shaped objects in this
//   scene and left every one of them alone - `ExteriorTerrain` (a TerrainCollider has no
//   Renderer by design), `Hero (Blaise)` (its rig is spawned at runtime), four
//   `Wall_DoorJamb_L/R` pairs (deliberate invisible blockers) and
//   `MainKeep_CastleWithTwoLevels_Home` / `PlayerHeroHall_PersonalQuarters_HomeSpace`
//   (grouping roots whose art hangs elsewhere). A scene-wide husk assertion would be RED
//   on all twelve on its first run, and a suite that is red for legitimate content gets
//   an allowlist, then gets ignored. Case 5 IS scene-wide, because a big invisible CARVE
//   has no legitimate instance - that is the axis that actually hurt the player.
//
// Deterministic: one scene open, no PlayMode, no fixtures, no writes.
//
// Standalone batch entry:
//   -Method DeNelle.Editor.Regression.HubHuskFreeSceneRegression.RunStandalone
// =============================================================================

using System;
using System.Collections.Generic;
using System.Text;
using DeNelle.Core;
using DeNelle.Core.Diagnostics;
using DeNelle.Village.World;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;

namespace DeNelle.Editor.Regression
{
    public static class HubHuskFreeSceneRegression
    {
        private const string FlowSys = "HubHuskFree";

        private const string MarkerOk   = "HUB_HUSK_FREE_OK";
        private const string MarkerFail = "HUB_HUSK_FREE_FAIL";

        /// <summary>The name substring that scopes cases 3 and 4. The owner's report, the ticket and
        /// the catalog bakedTwin all speak of the barracks; widening this without measuring the scene
        /// first is how a suite acquires an allowlist (see the header).</summary>
        private const string HostNameNeedle = "Barracks";

        /// <summary>How far a solid collider may legitimately reach past the rendered silhouette on
        /// one axis. A skinned Tripo visual and the host collider it re-used never agree exactly;
        /// a metre is the owner-scale tolerance from the ticket, and the ghost missed it by ten.</summary>
        private const float BoundsSlackMetres = 1f;

        /// <summary>The ceiling on an invisible carve, set BELOW the shipped ghost's own captured
        /// 16.90712 x 5.08288 x 14.51318 so that exact object could never pass.</summary>
        private const float MaxUnseenCarveMetres = 12f;

        /// <summary>Standalone batch entry.</summary>
        public static void RunStandalone()
        {
            if (Run(out string reason)) Debug.Log(reason);
            else Debug.LogError(MarkerFail + " - " + reason);
        }

        /// <summary>DataRegression-shaped contract. NEVER throws.</summary>
        public static bool Run(out string reason)
        {
            try
            {
                return RunCore(out reason);
            }
            catch (Exception ex)
            {
                reason = "hub-husk-free: oracle THREW " + ex.GetType().Name + ": " + ex.Message;
                Debug.LogError(MarkerFail + " - " + reason);
                return false;
            }
        }

        private static bool RunCore(out string reason)
        {
            using var _scope = FlowTrace.Enter(FlowSys, "HubHuskFreeSceneRegression.RunCore");

            var failures = new List<string>();
            var log = new StringBuilder();

            // ── 1. Open the live hub, resolved from the canonical property ──────
            string hubName = SceneRouter.Castle;
            if (string.IsNullOrEmpty(hubName))
            {
                reason = "hub-husk-free: SceneRouter.Castle is EMPTY - there is no hub to census, which is a " +
                         "FAILURE and not a skip.";
                Debug.LogError(MarkerFail + " - " + reason);
                return false;
            }
            string scenePath = "Assets/Scenes/" + hubName + ".unity";
            if (!System.IO.File.Exists(scenePath))
            {
                reason = "hub-husk-free: the hub scene resolved from SceneRouter.Castle does NOT exist on disk: '" +
                         scenePath + "'. Nothing censused.";
                Debug.LogError(MarkerFail + " - " + reason);
                return false;
            }

            var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
            if (!scene.IsValid() || !scene.isLoaded || scene.rootCount == 0)
            {
                reason = "hub-husk-free: could not open '" + scenePath + "' with any roots (valid=" +
                         scene.IsValid() + " loaded=" + scene.isLoaded + " roots=" + scene.rootCount +
                         ") - an oracle that censused zero objects has not passed.";
                Debug.LogError(MarkerFail + " - " + reason);
                return false;
            }
            log.AppendLine("[hub-husk-free] '" + scene.name + "' opened from SceneRouter.Castle (" +
                           scene.rootCount + " roots), path=" + scenePath);

            // ⛔ REFUSE THE LEGACY BRANCH. SceneRouter.Castle is FLAG-dependent
            // (FeatureFlags.MergedWorld); with the flag reading OFF in a batch context it resolves to
            // CastleCandidates[1] = the legacy hall, which is STILL ON DISK (CLAUDE.md sec.7). This
            // defect exists only in the merged hub, so censusing the hall would be a FALSE GREEN -
            // the exact failure HubSceneLiteralRegression and BlankStartCensusRegression's own
            // scene-name check were written for. Resolve through SceneRouter, then pin the branch.
            if (!string.Equals(scene.name, SceneRouter.CastleCandidates[0], StringComparison.Ordinal))
                failures.Add("opened '" + scene.name + "' but the merged home hub is '" +
                             SceneRouter.CastleCandidates[0] + "' (SceneRouter.Castle resolved the legacy branch, " +
                             "FeatureFlags.MergedWorld=" + FeatureFlags.MergedWorld + "). The WO-1716/WO-1809 husk " +
                             "lives in the merged hub, so a census of any other scene proves nothing and must not " +
                             "print a green marker.");

            // ── 2-5. One walk of the whole scene ────────────────────────────────
            int transforms = 0, hosts = 0, carves = 0;
            var ghostByName = new List<string>();

            foreach (var root in scene.GetRootGameObjects())
            {
                foreach (var t in root.GetComponentsInChildren<Transform>(true))
                {
                    var go = t.gameObject;
                    transforms++;

                    // ── 5. Scene-wide: a large carve nobody can see the source of ──
                    foreach (var obs in go.GetComponents<NavMeshObstacle>())
                    {
                        if (obs == null || !obs.enabled || !obs.carving) continue;
                        carves++;
                        Vector3 s = t.lossyScale;
                        Vector3 world = new Vector3(
                            Mathf.Abs(obs.size.x * s.x), Mathf.Abs(obs.size.y * s.y), Mathf.Abs(obs.size.z * s.z));
                        bool rendersNothing = go.GetComponentsInChildren<Renderer>(true).Length == 0;
                        float biggest = Mathf.Max(world.x, Mathf.Max(world.y, world.z));
                        log.AppendLine("[carve] '" + go.name + "' size=" + Fmt(world) + " rendersNothing=" +
                                       rendersNothing + " at " + Fmt(t.position));
                        if (rendersNothing && biggest > MaxUnseenCarveMetres)
                            failures.Add("'" + go.name + "' at " + Fmt(t.position) + " carves a " + Fmt(world) +
                                         " m hole in the navmesh while NOTHING under it renders (biggest axis " +
                                         biggest.ToString("0.0") + " m > " + MaxUnseenCarveMetres + " m). That is " +
                                         "the WO-1716 shape: the owner spent a session hunting a hole whose owner " +
                                         "nobody could see. Either the visual belongs back on it, or the carve and " +
                                         "the collision go with the visual (StructureVisualStrip.EnsureNoHusk).");
                    }

                    if (go.name.IndexOf(HostNameNeedle, StringComparison.OrdinalIgnoreCase) < 0) continue;
                    hosts++;

                    // ── 2. The named pin: the owner's own object ──────────────────
                    bool rendersAnything = go.GetComponentsInChildren<Renderer>(true).Length > 0;
                    if (!rendersAnything && string.Equals(go.name, "CastleBarracks", StringComparison.Ordinal))
                        ghostByName.Add(Fmt(t.position));

                    // ── 3. The shared predicate owns the husk rule ────────────────
                    if (StructureVisualStrip.IsInvisibleNavBlockingHusk(go, out string detail))
                    {
                        failures.Add("the hub scene carries an invisible-but-solid barracks husk: " + detail +
                                     " at " + Fmt(t.position) + " localScale=" + Fmt(t.localScale) + ". A structure " +
                                     "may not be invisible and solid at the same time (WO-1716/WO-1809). Run " +
                                     "Defenders/Castle/Remove invisible structure husks, then re-bake the navmesh.");
                        continue;   // case 4 cannot measure a silhouette that does not exist
                    }

                    // ── 4. A rendering host's collision must match what it renders ─
                    if (!rendersAnything)
                    {
                        log.AppendLine("[host] '" + go.name + "' renders nothing and blocks nothing at " +
                                       Fmt(t.position) + " - a marker/grouping transform, left alone.");
                        continue;
                    }

                    if (!TryUnion(go.GetComponentsInChildren<Renderer>(true), out Bounds rendered)) continue;

                    var solid = new List<Collider>();
                    foreach (var c in go.GetComponentsInChildren<Collider>(true))
                        if (c != null && c.enabled && !c.isTrigger) solid.Add(c);
                    if (solid.Count == 0)
                    {
                        log.AppendLine("[host] '" + go.name + "' renders and carries no solid collider at " +
                                       Fmt(t.position) + ".");
                        continue;
                    }

                    Bounds body = solid[0].bounds;
                    for (int i = 1; i < solid.Count; i++) body.Encapsulate(solid[i].bounds);

                    float overX = Excess(body.min.x, body.max.x, rendered.min.x, rendered.max.x);
                    float overY = Excess(body.min.y, body.max.y, rendered.min.y, rendered.max.y);
                    float overZ = Excess(body.min.z, body.max.z, rendered.min.z, rendered.max.z);
                    float over  = Mathf.Max(overX, Mathf.Max(overY, overZ));

                    log.AppendLine("[host] '" + go.name + "' at " + Fmt(t.position) + " renders " +
                                   Fmt(rendered.size) + " m, " + solid.Count + " solid collider(s) span " +
                                   Fmt(body.size) + " m, worst overhang " + over.ToString("0.00") + " m.");

                    if (over > BoundsSlackMetres)
                        failures.Add("'" + go.name + "' at " + Fmt(t.position) + " has solid collision reaching " +
                                     over.ToString("0.00") + " m beyond what it RENDERS (rendered " + Fmt(rendered.size) +
                                     " m vs collision " + Fmt(body.size) + " m; slack is " + BoundsSlackMetres +
                                     " m). That is a half-ghost: a collider still shaped by a mesh that is no longer " +
                                     "the one on screen, so the player is blocked by air next to the building.");
                }
            }

            foreach (var where in ghostByName)
                failures.Add("a GameObject named exactly 'CastleBarracks' that RENDERS NOTHING is still in the hub " +
                             "scene at " + where + " - this is the owner's 2026-09-16 report verbatim ('I think it " +
                             "still has the invisible barracks object') and the WO-1716 ghost. The visible barracks " +
                             "is a DIFFERENT object (Assets/StructureContent/barracks.fbx, named 'Barracks', under " +
                             "The8Structures_Storefronts_NPCPoints), so nothing needs this one.");

            // ── hollow-pass guards: finding nothing is never a pass ─────────────
            if (transforms < 100)
                failures.Add("censused only " + transforms + " transform(s) in the hub - the merged hub has " +
                             "hundreds, so this run looked at the wrong scene or an unloaded one and its green " +
                             "would mean nothing.");
            if (hosts == 0)
                failures.Add("no GameObject whose name contains '" + HostNameNeedle + "' exists anywhere in the hub " +
                             "scene. The owner's authored barracks is baked into this scene, so zero matches means " +
                             "this suite asserted against NOTHING (or the object was renamed and the scope needs " +
                             "re-pointing) - that is a failure, not a clean scene.");

            log.AppendLine("[hub-husk-free] censused " + transforms + " transform(s), " + hosts + " '" +
                           HostNameNeedle + "' host(s), " + carves + " enabled carving obstacle(s).");

            if (failures.Count > 0)
            {
                FlowTrace.Fail(FlowSys, "failures=" + failures.Count);
                Debug.Log(log.ToString().TrimEnd());
                reason = "hub-husk-free: " + failures.Count + " failure(s) - " + string.Join(" | ", failures);
                Debug.LogError(MarkerFail + " - " + reason);
                return false;
            }

            Debug.Log(log.ToString().TrimEnd());
            FlowTrace.Step(FlowSys, "hub scene holds no invisible-but-solid barracks and no unseen large carve (" +
                                    transforms + " transforms).");
            reason = MarkerOk + " '" + scene.name + "': " + hosts + " '" + HostNameNeedle + "' host(s) across " +
                     transforms + " transform(s) - none invisible-and-solid, none with collision more than " +
                     BoundsSlackMetres + " m past what it renders, and none of the " + carves +
                     " carving obstacle(s) cuts more than " + MaxUnseenCarveMetres + " m unseen.";
            Debug.Log(reason);
            return true;
        }

        /// <summary>How far [bMin,bMax] sticks out past [rMin,rMax] on one axis, either end.</summary>
        private static float Excess(float bMin, float bMax, float rMin, float rMax)
            => Mathf.Max(0f, Mathf.Max(rMin - bMin, bMax - rMax));

        private static bool TryUnion(Renderer[] renderers, out Bounds union)
        {
            union = default;
            bool any = false;
            foreach (var r in renderers)
            {
                if (r == null || r is ParticleSystemRenderer) continue;
                if (!any) { union = r.bounds; any = true; }
                else union.Encapsulate(r.bounds);
            }
            return any;
        }

        private static string Fmt(Vector3 v)
            => "(" + v.x.ToString("0.0") + "," + v.y.ToString("0.0") + "," + v.z.ToString("0.0") + ")";
    }
}

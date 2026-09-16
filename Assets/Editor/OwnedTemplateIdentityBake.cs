// =============================================================================
// OwnedTemplateIdentityBake — the baked stable identity, VERIFIED (no longer minted).
//
// WO-1767. This file used to be the SOLE writer of OwnedTemplateIdentity._templateId:
// it hand-stamped the already-built RaidBase_IronBastion scene with
// `Guid.NewGuid().ToString("N")` and then mapped the same ids onto
// OwnedTown_IronBastion by legacy sibling-path pose resolution.
//
// That is hand-maintained state living on a REGENERATED artifact, and it failed:
// WO-1732 (452fc14fd) added `iron_bastion` to RaidBaseGenerator.RaidConfigIdsFromCatalog,
// so BuildAllRaidScenes regenerated the raid scene for the first time and all 221 stamped
// components went with the old one. The owner's Seeker build 2026.09.16.371701 then logged
//   [Flow:Raid] Precombat capture census failed: Captured structure lacks a baked stable
//   identity: Wall_Outer_SS_0
// (RaidCaptureCensus.cs:38-39) 625 ms before RAID START — an empty census for the whole
// fight, which softlocks the victory screen on a 3-star clear.
//
// THE ID IS NOW EMITTED BY THE GENERATOR, AT CREATION, DETERMINISTICALLY:
// RaidBaseGenerator.StampIdentity writes `<configId>.<role>.<index>` on every
// WallSegment / DefenseTower / RaidSpire it builds. So a regen REPRODUCES the ids instead
// of churning them, and nothing can strip them again — they are the output, not a post-pass.
//
// ⛔ THIS FILE NO LONGER MINTS AN ID. A structure that reaches it without one is a
// GENERATOR defect, and it THROWS naming the chain rather than covering for it with a
// random GUID — a fresh GUID here would re-create the exact class of bug above and would
// churn every `templateStructureId` in every saved town (OwnedTownScenePose.TryResolve:72
// then answers "migration is required" for every structure the player owns).
//
// ⚠ TWO PHASES, AND THE ORDER MATTERS (WO-1767 deviation, recorded in the RESULT):
// WO-1767 §6b's step list put this whole method BEFORE OwnedTownSceneBuilder.Build. That
// order cannot run: the town phase legacy-resolves every raid pose against the EXISTING
// town scene, and the existing town carried the pre-WO-1723 210-wall partition, so it
// throws before the town is re-derived. Split:
//   StampRaid()  — raid scene only; every structure must already carry an id.
//   VerifyTown() — run AFTER OwnedTownSceneBuilder.Build; the derived copy inherits the ids
//                  verbatim, so this is a pure verification plus the three stability proofs.
// Run() keeps the old single-call shape for the menu / a manual batchmode call.
//
// ⛔ NO LITERAL CENSUS COUNT. The old `poses.Count != 221` check is gone: it is exactly
// CLAUDE.md §8's stale-count pattern living inside code, and re-running it against the
// 169-structure scene THREW. The census size is DERIVED — the raid count must equal the
// town count, and OwnedTownManifestBake derives the manifest entry count the same way.
//
// Batchmode: DeNelle.Editor.OwnedTemplateIdentityBake.Run
//            (normally reached through DeNelle.Editor.OwnedTownChain.RebuildFromRaid)
// Markers:   OWNED_TEMPLATE_IDS_OK / OWNED_TEMPLATE_IDS_FAIL (judge on a fresh log, §8)
// =============================================================================
using System;
using System.Collections.Generic;
using DeNelle.Core.State;
using DeNelle.Village;
using DeNelle.Village.World.Camps;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace DeNelle.Editor
{
    public static class OwnedTemplateIdentityBake
    {
        public const string RaidScenePath = "Assets/Scenes/RaidBase_IronBastion.unity";
        public const string TownScenePath = "Assets/Scenes/OwnedTown_IronBastion.unity";

        private const string Chain = "DeNelle.Editor.OwnedTownChain.RebuildFromRaid";

        /// <summary>The single-call shape (menu / manual batchmode). Assumes the town is already derived.</summary>
        public static void Run()
        {
            var poses = StampRaid();
            VerifyTown(poses);
        }

        /// <summary>
        /// Phase 1 — the RAID template. Enumerates every census structure in creation-independent
        /// hierarchy order (the same walk RaidCaptureCensus uses) and requires each one to carry a
        /// generator-emitted <c>_templateId</c>. Returns the captured poses for phase 2.
        /// Saves nothing: the ids are already serialized by the generator's own scene save.
        /// </summary>
        public static List<OwnedStructurePose> StampRaid()
        {
            var source = EditorSceneManager.OpenScene(RaidScenePath, OpenSceneMode.Single);
            var poses = new List<OwnedStructurePose>();
            var ids = new HashSet<string>(StringComparer.Ordinal);
            var missing = new List<string>();
            foreach (var root in source.GetRootGameObjects())
                foreach (var node in root.GetComponentsInChildren<Transform>(true))
                {
                    if (node.GetComponent<WallSegment>() == null && node.GetComponent<DefenseTower>() == null &&
                        node.GetComponent<RaidSpire>() == null) continue;
                    var identity = node.GetComponent<OwnedTemplateIdentity>();
                    string id = identity != null ? identity.TemplateId : null;
                    if (string.IsNullOrEmpty(id)) { missing.Add(node.name); continue; }
                    if (!ids.Add(id)) throw Fail("Duplicate baked template identity '" + id + "' on '" + node.name +
                        "'. The generator emits <configId>.<role>.<index>; a duplicate means two structures " +
                        "shared a role index, which is a RaidBaseGenerator.StampIdentity defect.");
                    poses.Add(OwnedTownScenePose.Capture(node));
                }
            if (missing.Count > 0)
                throw Fail(missing.Count + " captured structure(s) in " + RaidScenePath + " carry NO baked stable " +
                    "identity (first: '" + missing[0] + "'). This file no longer mints one - the id is emitted at " +
                    "creation by RaidBaseGenerator.StampIdentity. Regenerate the scene through " + Chain +
                    " instead of stamping it by hand.");
            if (poses.Count == 0)
                throw Fail(RaidScenePath + " holds NO WallSegment / DefenseTower / RaidSpire at all - refusing a " +
                    "silent empty census. Run " + Chain + ".");
            Debug.Log("OWNED_TEMPLATE_IDS_OK structures=" + poses.Count + " (DERIVED from " + RaidScenePath +
                ", never a literal); every id generator-emitted and unique; no GUID was minted here");
            return poses;
        }

        /// <summary>
        /// Phase 2 — the OWNED TOWN template, which OwnedTownSceneBuilder derives from the raid
        /// scene, so it inherits the same ids verbatim. Verifies the two id SETS agree, the census
        /// sizes agree (derived, not a literal), and re-runs the three stability proofs the identity
        /// was invented for: the id survives a sibling reorder that the legacy sibling-path branch
        /// cannot, duplicate ids are refused, and a changed coordinate frame is refused.
        /// </summary>
        public static void VerifyTown(List<OwnedStructurePose> poses)
        {
            if (poses == null || poses.Count == 0) throw Fail("VerifyTown was given no raid poses.");
            var town = EditorSceneManager.OpenScene(TownScenePath, OpenSceneMode.Single);

            // DERIVED census equality - this replaced `poses.Count != 221`.
            int townStructures = 0;
            var townIds = new HashSet<string>(StringComparer.Ordinal);
            var townMissing = new List<string>();
            foreach (var root in town.GetRootGameObjects())
                foreach (var node in root.GetComponentsInChildren<Transform>(true))
                {
                    if (node.GetComponent<WallSegment>() == null && node.GetComponent<DefenseTower>() == null &&
                        node.GetComponent<RaidSpire>() == null) continue;
                    townStructures++;
                    var identity = node.GetComponent<OwnedTemplateIdentity>();
                    if (identity == null || string.IsNullOrEmpty(identity.TemplateId)) { townMissing.Add(node.name); continue; }
                    if (!townIds.Add(identity.TemplateId))
                        throw Fail("Duplicate baked template identity '" + identity.TemplateId + "' in " + TownScenePath + ".");
                }
            if (townMissing.Count > 0)
                throw Fail(townMissing.Count + " structure(s) in " + TownScenePath + " carry no baked stable identity " +
                    "(first: '" + townMissing[0] + "'). The town is DERIVED from the raid scene and inherits its ids - " +
                    "re-derive it with " + Chain + ".");
            if (townStructures != poses.Count)
                throw Fail("The owned-town template and the raid template describe different buildings: " +
                    TownScenePath + " holds " + townStructures + " census structure(s), " + RaidScenePath +
                    " holds " + poses.Count + ". Re-derive the town with " + Chain +
                    " - never re-type a census literal (CLAUDE.md §8).");
            foreach (var pose in poses)
                if (!townIds.Contains(pose.templateStructureId))
                    throw Fail("The raid template id '" + pose.templateStructureId + "' ('" + pose.sourceName +
                        "') is absent from " + TownScenePath + ". The two id SETS must be equal. Run " + Chain + ".");

            // Every raid pose must resolve in the town by ID, frame included.
            foreach (var pose in poses)
                if (!OwnedTownScenePose.TryResolve(town, pose, out _, out var reason)) throw Fail(reason);

            // -- the three proofs the stable identity exists for --------------------
            var probe = poses[0];
            if (!OwnedTownScenePose.TryResolve(town, probe, out var selected, out var failure)) throw Fail(failure);
            int sibling = selected.GetSiblingIndex();
            selected.SetSiblingIndex(selected.parent.childCount - 1);
            if (!OwnedTownScenePose.TryResolve(town, probe, out var reordered, out failure) || reordered != selected)
                throw Fail("Stable identity did not survive sibling reordering.");
            selected.SetSiblingIndex(sibling);
            var duplicate = new GameObject("~DuplicateIdentityProof");
            Assign(duplicate.AddComponent<OwnedTemplateIdentity>(), probe.templateStructureId);
            if (OwnedTownScenePose.TryResolve(town, probe, out _, out _)) throw Fail("Duplicate ID was accepted.");
            UnityEngine.Object.DestroyImmediate(duplicate);
            var changed = probe.Clone(); changed.parentFrame[12] += 1f;
            if (OwnedTownScenePose.TryResolve(town, changed, out _, out _)) throw Fail("Changed coordinate frame was accepted.");

            Debug.Log("OWNED_TOWN_IDS_VERIFIED_OK structures=" + townStructures +
                "; raid and town id SETS equal and census sizes equal (both DERIVED); sibling reorder survives; " +
                "duplicate IDs and changed frames refused; neither scene was re-saved");
        }

        private static InvalidOperationException Fail(string reason)
        {
            Debug.LogError("OWNED_TEMPLATE_IDS_FAIL " + reason);
            return new InvalidOperationException(reason);
        }

        private static void Assign(OwnedTemplateIdentity identity, string id)
        {
            var serialized = new SerializedObject(identity);
            serialized.FindProperty("_templateId").stringValue = id;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}

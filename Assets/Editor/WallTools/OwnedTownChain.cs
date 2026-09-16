// =============================================================================
// OwnedTownChain — THE ONE SANCTIONED CHAIN that re-derives the Iron Bastion owned-town
// template pair from the raid scene. WO-1767.
//
// WHY IT IS ONE FILE AND NOT A RUNBOOK STEP LIST (CLAUDE.md §16's law, applied):
// the six steps below used to exist only as prose. They were run out of order, partially, or
// not at all, and the artifacts silently desynchronised:
//   - WO-1732 (452fc14fd) regenerated RaidBase_IronBastion onto the WO-1723 4.0 m partition
//     (210 walls -> 158 + 158 ruins) and DID NOT touch OwnedTown_IronBastion.unity. The raid
//     template, the owned town and the shipped manifest then described THREE different
//     buildings, and the regen also stripped all 221 hand-stamped OwnedTemplateIdentity
//     components (they lived on the old scene).
//   - The owner's Seeker build 2026.09.16.371701 logged, 625 ms before RAID START:
//       [Flow:Raid] Precombat capture census failed: Captured structure lacks a baked stable
//       identity: Wall_Outer_SS_0
//     (RaidCaptureCensus.cs:38-39). An empty census for the whole fight, which softlocks the
//     victory screen on a 3-star clear: RaidVictoryController:1021-1027 refuses the capture and
//     :990-1009 gates the ONLY route home behind the same predicate.
// A chain whose remedy is "a human remembers the other five commands" is not a chain.
//
// ⛔ OWNER RULING 2026-09-16 on WORK_ORDER_1767 §6b, VERBATIM:
//   "(A) re-derive OwnedTown_IronBastion and its manifest from the new 158-wall raid scene"
// keeping WO-1732's partition. That is what this file does, and it is why
// RaidBaseGenerator's "OWNER-AUTHORED AND PROTECTED - NOT regenerated" comment was corrected:
// Assets/Editor/OwnedTownSceneBuilder.cs has always DERIVED that scene from the raid scene.
//
// ⚠ THE ORDER DIFFERS FROM WO-1767 §6b's LIST, AND THE LIST COULD NOT RUN.
// §6b put OwnedTemplateIdentityBake BEFORE OwnedTownSceneBuilder. Its town phase legacy-resolves
// every raid pose against the EXISTING town scene, which carried the pre-WO-1723 210-wall
// partition - so it throws before the town is re-derived. The bake is split instead
// (StampRaid / VerifyTown) and the verification runs after the town exists. Recorded as a
// deliberate deviation per CLAUDE.md §11B.B rather than done silently.
//
// THE CHAIN:
//   0. lint the four artifacts as they stand       (expected RED before the rebuild)
//   1. RaidBaseGenerator.BuildSceneFor("iron_bastion")   -> the raid template, ids emitted at creation
//   2. OwnedTemplateIdentityBake.StampRaid()             -> OWNED_TEMPLATE_IDS_OK (verify, never mint)
//   3. OwnedTownSceneBuilder.Build()                     -> OWNED_TOWN_SCENE_OK (town derived FROM it)
//   4. OwnedTemplateIdentityBake.VerifyTown(poses)       -> OWNED_TOWN_IDS_VERIFIED_OK
//   5. OwnedTownManifestBake.Run()                       -> OWNED_TOWN_MANIFEST_OK
//   6. RaidBaseGenerator.ReseatOwnedTownSpire()          -> OWNED_TOWN_SPIRE_RESEAT_OK
//   7. RaidNavBake.BakeAll()                             -> RAID_NAV_BAKE_OK (+ RAID_NAV_REACH_OK)
//   8. OwnedTownPracticeSceneBuilder.Build()             -> OWNED_TOWN_PRACTICE_SCENE_OK
//                                                           (practice arena derived from the NEW town;
//                                                            AFTER the bake - see the note at the call)
//   9. lint again from disk                              (must be GREEN), plus a mutated-text RED
//
// Step 8's mutated-text leg is the §11B.A "prove the red" proof: one id is blanked IN MEMORY and
// the lint must refuse it. A .unity file is never hand-edited to test a regression (CLAUDE.md §3).
//
// ⛔ JUDGE THIS BY ITS MARKER ON A FRESH LOG, NEVER BY THE EXIT CODE (CLAUDE.md §8). Several
// steps report failure with Debug.LogError and do not throw, so this file listens to the Unity
// log while it runs and withholds its OK marker if any required step marker is missing or any
// failure marker appeared.
//   OWNED_TOWN_CHAIN_OK    ... every step marker seen, lint red-then-green proven
//   OWNED_TOWN_CHAIN_FAIL  ... <which step, and what was missing>
//
// ⚠ WHY THIS FILE LIVES IN Assets/Editor/WallTools AND NOT Assets/Editor (measured, not guessed):
// step 1 calls RaidBaseGenerator, which is in the DeNelle.EditorWallTools assembly, and that
// assembly REFERENCES DeNelle.Editor (Assets/Editor/WallTools/DeNelle.EditorWallTools.asmdef).
// A chain placed in DeNelle.Editor therefore cannot see RaidBaseGenerator, and the reverse
// reference would be a cycle. First attempt failed exactly there:
//   Assets\Editor\OwnedTownChain.cs(108,17): error CS0103: The name 'RaidBaseGenerator' does not
//   exist in the current context
// So the chain sits on the WallTools side, which can reach BOTH: DeNelle.Editor (the three owned-
// town bakes + RaidNavBake) and DeNelle.EditorRegression (the lint) - the latter added to that
// asmdef's references in this change. The NAMESPACE is unchanged (DeNelle.Editor), so the
// batchmode entry point below is the same string it would have been either way. Read the .asmdef,
// never this comment, for what may reference what (CLAUDE.md §5).
//
// Batchmode: DeNelle.Editor.OwnedTownChain.RebuildFromRaid
// Menu:      Defenders/Walls/Rebuild Owned Town From Raid (WO-1767)
// Never run while the Unity editor is open (project lock, CLAUDE.md §3).
// =============================================================================
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using DeNelle.Editor.Regression;
using UnityEditor;
using UnityEngine;

namespace DeNelle.Editor
{
    public static class OwnedTownChain
    {
        public const string ConfigId = "iron_bastion";
        private const string OkMarker = "OWNED_TOWN_CHAIN_OK";
        private const string FailMarker = "OWNED_TOWN_CHAIN_FAIL";

        /// <summary>Step markers every run must produce. Absence on a fresh log is a FAILURE, not an unknown.</summary>
        private static readonly string[] RequiredMarkers =
        {
            "OWNED_TEMPLATE_IDS_OK",
            "OWNED_TOWN_SCENE_OK",
            "OWNED_TOWN_IDS_VERIFIED_OK",
            "OWNED_TOWN_MANIFEST_OK",
            "OWNED_TOWN_SPIRE_RESEAT_OK",
            "RAID_NAV_BAKE_OK",
            "OWNED_TOWN_PRACTICE_SCENE_OK",
        };

        /// <summary>Markers that mean a step refused. Any one of them withholds the chain's OK.</summary>
        private static readonly string[] ForbiddenMarkers =
        {
            "OWNED_TEMPLATE_IDS_FAIL",
            "OWNED_TOWN_SPIRE_RESEAT_FAIL",
            "RAID_NAV_REACH_FAIL",
        };

        private static readonly List<string> _log = new List<string>();

        [MenuItem("Defenders/Walls/Rebuild Owned Town From Raid (WO-1767)")]
        public static void RebuildFromRaid()
        {
            _log.Clear();
            Application.logMessageReceived += Capture;
            try
            {
                // -- 0. the tree as it stands. RED is the expected reading before the rebuild.
                bool beforeGreen = OwnedTownTemplateIdentityRegression.Run(out string beforeReason);
                Debug.Log("[OwnedTownChain] BEFORE lint: " + (beforeGreen ? "GREEN" : "RED") + " - " + beforeReason);

                // -- 1. the raid template. Ids are emitted at creation by RaidBaseGenerator.StampIdentity,
                //       so this step IS the stamp; nothing hand-stamps the finished scene any more.
                RaidBaseGenerator.BuildSceneFor(ConfigId);

                // -- 2. verify every censused structure carries a generator id (never mint one).
                var poses = OwnedTemplateIdentityBake.StampRaid();

                // -- 3. derive the owned town FROM that raid scene (owner ruling A).
                OwnedTownSceneBuilder.Build();

                // -- 4. the derived copy must carry the SAME ids, and the stability proofs must hold.
                OwnedTemplateIdentityBake.VerifyTown(poses);

                // -- 5. re-bake the shipped manifest from the raid census.
                OwnedTownManifestBake.Run();

                // -- 6. WO-1749's spire seat on the town copy. A town derived from a generated raid
                //       scene arrives ALREADY seated (BuildConfigLayout reseats it), and that branch
                //       reports OK with a measured lift=0.00m rather than refusing a correct scene.
                RaidBaseGenerator.ReseatOwnedTownSpire();

                // -- 7. ground plane + legacy navmesh for every raid scene, the owned town included.
                RaidNavBake.BakeAll();

                // -- 8. the PRACTICE ARENA, derived from the town that was just rebuilt.
                //
                // Lead ruling 2026-09-16: the owner's ruling (A) covers the practice arena too, because
                // OwnedTownPracticeSceneBuilder.cs:16 derives ArenaPractice_IronBastion.unity FROM
                // OwnedTown_IronBastion.unity. Before this step joined the chain, the first WO-1767 bake
                // left it carrying 221 identities / 210 WallSegments and the OLD 32-hex GUID ids while the
                // town moved to 169 - and OwnedTownScenePose.TryResolve:59 accepts owned-template poses
                // inside PracticeCombatPolicy.SceneName, so a captured town could not resolve there.
                //
                // ⚠ IT RUNS AFTER RaidNavBake, NOT BEFORE IT - the lead's brief said "between the
                // manifest/reseat step and RaidNavBake", and that order is measurably wrong. Proof, read
                // off the artifacts 2026-09-16:
                //   - ArenaPractice_IronBastion.unity's NavMeshSettings m_NavMeshData points at guid
                //     819cc32d36ca7ed42af4986002cb0e8d, which is Assets/Scenes/OwnedTown_IronBastion/
                //     NavMesh.asset.meta's own guid. The practice scene SHARES the town's baked navmesh.
                //   - It also contains the RaidGround object, which only RaidNavBake.EnsureGround creates.
                // Both facts are only possible for a Save-As taken AFTER the town was nav-baked, which is
                // also why RaidNavBake.cs:33-40's five-scene list does not (and need not) name it. Derived
                // BEFORE the bake, the practice arena would have no ground to walk on and no navmesh.
                // Recorded as a deliberate one-step deviation (CLAUDE.md §11B.B), not done silently.
                OwnedTownPracticeSceneBuilder.Build();

                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();

                // -- 9a. the lint must now be GREEN, read back off DISK.
                if (!OwnedTownTemplateIdentityRegression.Run(out string afterReason))
                { Refuse("the artifact lint is still RED after the rebuild: " + afterReason); return; }
                Debug.Log("[OwnedTownChain] AFTER lint: GREEN - " + afterReason);

                // -- 9b. prove the lint can FAIL (CLAUDE.md §11B.A). One id blanked IN MEMORY only.
                if (!ProveLintGoesRed(out string redWhy))
                { Refuse("the artifact lint did not refuse a blanked _templateId, so it is not a gate: " + redWhy); return; }

                // -- marker audit. Several steps above report failure without throwing.
                var missing = new List<string>();
                foreach (string marker in RequiredMarkers)
                    if (!Saw(marker)) missing.Add(marker);
                if (missing.Count > 0)
                { Refuse("step marker(s) never appeared: " + string.Join(", ", missing)); return; }
                var refused = new List<string>();
                foreach (string marker in ForbiddenMarkers)
                    if (Saw(marker)) refused.Add(marker);
                if (refused.Count > 0)
                { Refuse("a step reported failure: " + string.Join(", ", refused)); return; }

                Debug.Log(OkMarker + " config=" + ConfigId + "; raid template regenerated with ids emitted at " +
                    "creation; owned town re-derived FROM it (owner ruling A, 2026-09-16); manifest re-baked; " +
                    "spire seat verified; navmesh baked; artifact lint proven RED before / GREEN after / RED on a " +
                    "blanked id. " + afterReason);
            }
            finally
            {
                Application.logMessageReceived -= Capture;
            }
        }

        /// <summary>
        /// Blank ONE _templateId value in a copy of the raid scene text and require the lint to refuse it.
        /// Text-only: the scene on disk is never touched.
        /// </summary>
        private static bool ProveLintGoesRed(out string why)
        {
            why = null;
            string raid = File.ReadAllText(OwnedTownTemplateIdentityRegression.RaidScenePath);
            string town = File.ReadAllText(OwnedTownTemplateIdentityRegression.TownScenePath);
            string practice = File.ReadAllText(OwnedTownTemplateIdentityRegression.PracticeScenePath);
            string manifest = File.ReadAllText(OwnedTownTemplateIdentityRegression.ManifestPath);
            // ⚠ `[^\S\r\n]` and `[^\r\n]`, NOT `\s`: `\s` matches '\n', so a `\s*$` tail with
            // RegexOptions.Multiline runs PAST the end of the id's own line and the replacement then
            // merges two YAML lines. The first run of this chain did exactly that: the lint still
            // went red, but for the wrong reason (a mangled node name appeared in the id SET
            // comparison) instead of naming the blanked field. A red proof has to fail for the
            // reason it claims.
            var match = Regex.Match(raid, @"^([^\S\r\n]*)_templateId:[^\r\n]*$", RegexOptions.Multiline);
            if (!match.Success) { why = "no _templateId line to blank - the scene is not stamped at all."; return false; }
            string mutated = raid.Remove(match.Index, match.Length)
                                 .Insert(match.Index, match.Groups[1].Value + "_templateId: ");
            if (mutated == raid) { why = "the mutation changed nothing."; return false; }
            if (OwnedTownTemplateIdentityRegression.Check(mutated, town, practice, manifest, out string mutatedReason))
            { why = "it PASSED the mutated text: " + mutatedReason; return false; }
            Debug.Log("[OwnedTownChain] RED proof: one blanked _templateId is refused - " + mutatedReason);
            return true;
        }

        private static void Refuse(string why)
        {
            Debug.LogError(FailMarker + " " + why + " Read the [OwnedTownChain] lines above, fix the named step, " +
                "and re-run DeNelle.Editor.OwnedTownChain.RebuildFromRaid. Do NOT hand-edit a .unity file or " +
                "re-type a census count (CLAUDE.md §3, §8).");
        }

        private static bool Saw(string marker)
        {
            foreach (string line in _log)
                if (line != null && line.Contains(marker)) return true;
            return false;
        }

        private static void Capture(string condition, string stackTrace, LogType type)
        {
            if (!string.IsNullOrEmpty(condition)) _log.Add(condition);
        }
    }
}

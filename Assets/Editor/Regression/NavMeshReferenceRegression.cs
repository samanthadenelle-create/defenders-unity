// =============================================================================
// NavMeshReferenceRegression [navmesh-reference] -- a SHIPPED SCENE MUST NOT POINT
// AT A NAVMESH ASSET THAT DOES NOT EXIST.
// -----------------------------------------------------------------------------
// Assembly: DeNelle.EditorRegression.  Markers: NAVMESH_REF_OK / NAVMESH_REF_FAIL.
//
// WHY THIS SUITE EXISTS (WO-1731, 2026-09-14 -- the class of bug, not one instance).
// A NavMeshSurface bake wrote a NEW data asset (named after the GameObject carrying
// the surface) and removed the old one, but the SCENE WAS NEVER SAVED -- so the scene
// on disk kept serializing the DELETED asset's guid. Main_Castle_Overworld therefore
// shipped with NO navmesh at all. That is silent: HeroLocomotion moves by
// `_agent.Move(step)` when on-mesh and falls back to `transform.position += step`
// when OFF-mesh (HeroLocomotion.cs:1488-1489) with zero Physics/Raycast/collider
// references, so in town the navmesh is the ONLY thing constraining the hero. No
// navmesh, no constraint: she walked through every wall and building. Nothing on
// screen said so. The only detector was the owner's eyes on a device, in build
// 2026.09.15.370139, and it burned a felt-test.
//
// A dangling navmesh reference must go RED IN A GATE, never reach a device.
//
// -----------------------------------------------------------------------------
// WHAT IS CHECKED, AND WHY IT IS A DELIBERATE SUPERSET OF THE WO'S WORDING
// -----------------------------------------------------------------------------
// The WO asks for "every shipped scene containing a NavMeshSurface". This suite
// scans EVERY `m_NavMeshData:` reference in every build-settings-enabled scene,
// which is strictly more: the token appears BOTH on Unity.AI.Navigation's
// NavMeshSurface component AND in the scene's own legacy NavMeshSettings block
// (what UnityEditor.AI.NavMeshBuilder.BuildNavMesh() writes -- the path
// CastleWalkable / RaidNavBake / CastleBuilderTester use). Both dangle in exactly
// the same way for exactly the same reason, and the parse cannot tell them apart
// without modelling the whole YAML document -- so it does not try, and asserts the
// invariant that is true of both: a NON-ZERO reference must resolve to a file that
// exists on disk.
//
//   * `m_NavMeshData: {fileID: 0}`  -- no navmesh assigned. NOT a failure. Many
//     shipped scenes (Title, HeroSelect, ATBBattle) legitimately have none.
//   * a non-zero fileID with a guid  -- MUST resolve via AssetDatabase.GUIDToAssetPath
//     AND the returned path must exist on disk. Both, because GUIDToAssetPath can
//     hand back a stale path for a guid the database has not re-scanned.
//   * a non-zero fileID with NO guid -- an object inside the same scene file; there
//     is no cross-asset reference to dangle. Counted, not asserted.
//
// -----------------------------------------------------------------------------
// THE SELF-TEST RUNS FIRST, AND IT IS THE PROOF THAT THE GUARD CAN GO RED
// -----------------------------------------------------------------------------
// A guard nobody has watched fail is not a guard. The tree is GREEN today (the
// WO-1731 mitigation restored the asset the scene points at), so a sweep alone
// would prove nothing about this suite's ability to detect anything. The detection
// rule therefore lives in ONE pure function -- ScanSceneText -- with no Unity or
// filesystem dependency, and SelfTest drives it over four fixtures BEFORE the sweep:
// a known-bad guid (must be reported), fileID 0 (must not be), a resolvable guid
// (must not be), and text with no reference at all (must not be). If the self-test
// does not behave, Run returns FALSE before scanning a single scene -- because a
// sweep by an unproven detector is a hollow pass with the whole tree inside it
// (the same reasoning HollowPassFixtures.SelfTest is built on, RULE 4).
//
// ⛔ NO SCENE IS EVER OPENED, WRITTEN OR HAND-EDITED HERE. The fixtures are string
// literals in this file. Hand-editing a .unity is forbidden (CLAUDE.md section 3),
// including "just to test a guard".
//
// -----------------------------------------------------------------------------
// BINARY SCENES ARE A NAMED PARTIAL SKIP, NOT A SILENT PASS
// -----------------------------------------------------------------------------
// The DungeonCompose scenes are committed BINARY on purpose (DungeonChainBuilder.cs
// records the proof: in batchmode EditorSceneManager.SaveScene writes a freshly
// created scene binary even under ForceText). A text regex cannot see into them.
// Passing them silently is precisely the hollow this project's ratchet exists to
// stop, so each one is reported through RegressionOutcome.PartialSkip by name: the
// suite still counts green, and the log names the hole.
//
// Wire (DataRegression.RunAll):
//   DeNelle.Core.Diagnostics.Guard.Try("Regression", "navmesh-reference suite", () => { if (!DeNelle.Editor.Regression.NavMeshReferenceRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[navmesh-reference] " + r); });
// =============================================================================
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace DeNelle.Editor.Regression
{
    public static class NavMeshReferenceRegression
    {
        private const string MarkerOk = "NAVMESH_REF_OK";
        private const string MarkerFail = "NAVMESH_REF_FAIL";

        // m_NavMeshData: {fileID: 23800000, guid: <32 hex>, type: 2}
        // m_NavMeshData: {fileID: 0}
        private static readonly Regex NavMeshDataRef = new Regex(
            // The tail consumes the rest of the inline map (", type: 2") and anchors the match on
            // the CLOSING brace -- the more precise parse, and written with a two-sided negated
            // class so the escaped braces in this literal stay balanced for the CLAUDE.md
            // section 1 raw count (the gate's own scanner skips string literals; the raw
            // one-liner does not, and an imbalance there costs the next seat a diagnosis).
            @"m_NavMeshData:\s*\{\s*fileID:\s*(-?\d+)(?:\s*,\s*guid:\s*([0-9a-fA-F]{32}))?[^{}]*\}",
            RegexOptions.Compiled);

        // ------------------------------------------------------------------
        //  THE PURE DETECTION RULE. No Unity, no disk -- so SelfTest can drive it.
        //  resolve(guid) returns the asset path for a guid that resolves to a real
        //  file, or null/empty for one that does not.
        //  Returns the number of NON-ZERO references seen; appends one line per
        //  dangling reference to problems.
        // ------------------------------------------------------------------
        public static int ScanSceneText(string sceneLabel, string sceneText, Func<string, string> resolve,
                                        List<string> problems, out int assignedRefs, out int localRefs)
        {
            assignedRefs = 0;
            localRefs = 0;
            if (string.IsNullOrEmpty(sceneText)) return 0;

            foreach (Match m in NavMeshDataRef.Matches(sceneText))
            {
                string fileId = m.Groups[1].Value;
                if (fileId == "0") continue;          // no navmesh assigned -- legitimate

                string guid = m.Groups[2].Success ? m.Groups[2].Value : null;
                if (string.IsNullOrEmpty(guid)) { localRefs++; continue; }  // in-scene object, nothing to dangle

                assignedRefs++;
                string path = resolve != null ? resolve(guid) : null;
                if (!string.IsNullOrEmpty(path)) continue;

                int line = LineOf(sceneText, m.Index);
                problems.Add(sceneLabel + " line " + line + ": m_NavMeshData points at guid " + guid +
                             " (fileID " + fileId + ") which resolves to NO FILE -- that scene has no navmesh at " +
                             "runtime, and nothing on screen says so (WO-1731).");
            }
            return assignedRefs + localRefs;
        }

        // ------------------------------------------------------------------
        //  SELF-TEST -- proves the rule reports the bad case and stays quiet on the
        //  three good ones. Runs BEFORE the sweep; a failure here fails the suite.
        // ------------------------------------------------------------------
        public static bool SelfTest(out string detail)
        {
            const string goodGuid = "6af7dcf896317734a986e2bcc349575d";
            const string badGuid = "00000000000000000000000000000bad";

            Func<string, string> stub = g => g == goodGuid ? "Assets/Scenes/Fixture/NavMesh-Fixture.asset" : null;

            string dangling = "  m_NavMeshData: {fileID: 23800000, guid: " + badGuid + ", type: 2}\n";
            string resolving = "  m_NavMeshData: {fileID: 23800000, guid: " + goodGuid + ", type: 2}\n";
            const string unassigned = "  m_NavMeshData: {fileID: 0}\n";
            const string noReference = "  m_Name: SomeGameObject\n  m_IsActive: 1\n";

            var cases = new List<string>();

            var p = new List<string>();
            int assigned, local;
            ScanSceneText("[fixture:dangling]", dangling, stub, p, out assigned, out local);
            if (p.Count != 1)
                cases.Add("a dangling guid produced " + p.Count + " problem(s), expected exactly 1 -- the detector is BLIND");
            else if (p[0].IndexOf(badGuid, StringComparison.Ordinal) < 0)
                cases.Add("the dangling report does not name the offending guid");

            p.Clear();
            ScanSceneText("[fixture:resolving]", resolving, stub, p, out assigned, out local);
            if (p.Count != 0) cases.Add("a RESOLVING guid was reported as dangling -- the detector cries wolf");
            if (assigned != 1) cases.Add("a resolving guid counted " + assigned + " assigned reference(s), expected 1");

            p.Clear();
            ScanSceneText("[fixture:unassigned]", unassigned, stub, p, out assigned, out local);
            if (p.Count != 0) cases.Add("fileID 0 (no navmesh assigned) was reported as dangling -- that is legitimate");
            if (assigned != 0) cases.Add("fileID 0 counted as an assigned reference");

            p.Clear();
            ScanSceneText("[fixture:none]", noReference, stub, p, out assigned, out local);
            if (p.Count != 0) cases.Add("text with no m_NavMeshData produced a problem");

            if (cases.Count > 0)
            {
                detail = "SELF-TEST FAILED (" + cases.Count + "): " + string.Join(" | ", cases.ToArray());
                return false;
            }
            detail = "self-test 4/4 fixtures (dangling reported, resolving quiet, fileID-0 quiet, no-reference quiet)";
            return true;
        }

        public static bool Run(out string reason)
        {
            var log = new StringBuilder();
            log.AppendLine("--- NAVMESH REFERENCE (every shipped scene's m_NavMeshData guid resolves to a real file) ---");

            try
            {
                string selfDetail;
                if (!SelfTest(out selfDetail))
                {
                    reason = "navmesh-reference: " + selfDetail + " -- refusing to sweep with an unproven detector.";
                    Debug.LogError(log.ToString() + MarkerFail + ": " + reason);
                    return false;
                }
                log.AppendLine("  " + selfDetail);

                string projectRoot = Directory.GetParent(Application.dataPath).FullName;

                Func<string, string> resolve = guid =>
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    if (string.IsNullOrEmpty(path)) return null;
                    string full = Path.Combine(projectRoot, path.Replace('/', Path.DirectorySeparatorChar));
                    return File.Exists(full) ? path : null;
                };

                var problems = new List<string>();
                var partials = new List<string>();
                int scenesScanned = 0, scenesShipped = 0, scenesWithRefs = 0;
                int assignedTotal = 0, localTotal = 0, binaryScenes = 0, missingScenes = 0;

                foreach (var entry in EditorBuildSettings.scenes)
                {
                    if (entry == null || !entry.enabled || string.IsNullOrEmpty(entry.path)) continue;
                    scenesShipped++;

                    string full = Path.Combine(projectRoot, entry.path.Replace('/', Path.DirectorySeparatorChar));
                    if (!File.Exists(full))
                    {
                        missingScenes++;
                        problems.Add(entry.path + " is enabled in EditorBuildSettings but the file does not exist.");
                        continue;
                    }

                    if (!IsYamlText(full))
                    {
                        binaryScenes++;
                        partials.Add(RegressionOutcome.PartialSkip("binary scene " + entry.path,
                            "serialized BINARY (builder-generated, see DungeonChainBuilder.cs) -- a text scan " +
                            "cannot read its m_NavMeshData guid, so this scene is NOT covered by this oracle"));
                        continue;
                    }

                    string text;
                    try { text = File.ReadAllText(full); }
                    catch (Exception readEx)
                    {
                        problems.Add(entry.path + " could not be read: " + readEx.Message);
                        continue;
                    }

                    scenesScanned++;
                    int assigned, local;
                    int refs = ScanSceneText(entry.path, text, resolve, problems, out assigned, out local);
                    assignedTotal += assigned;
                    localTotal += local;
                    if (refs > 0) scenesWithRefs++;
                }

                log.AppendLine("  shipped scenes (EditorBuildSettings enabled): " + scenesShipped +
                               "; text-scanned: " + scenesScanned +
                               "; binary (not covered): " + binaryScenes +
                               "; carrying an assigned navmesh: " + scenesWithRefs);
                log.AppendLine("  assigned m_NavMeshData references checked: " + assignedTotal +
                               " (plus " + localTotal + " in-scene reference(s), nothing to dangle)");

                // A sweep that checked NOTHING is not a pass. If no shipped scene carries a single
                // assigned navmesh reference, either the build list or this parse is broken -- and
                // silently reporting green would be the exact hollow this oracle was written against.
                if (assignedTotal == 0)
                {
                    reason = "navmesh-reference: ZERO assigned m_NavMeshData references found across " +
                             scenesScanned + " text-scanned shipped scene(s). Either every shipped scene lost " +
                             "its navmesh, or the parse no longer matches Unity's serialization -- both are " +
                             "failures, and neither may report green.";
                    Debug.LogError(log.ToString() + MarkerFail + ": " + reason);
                    return false;
                }

                if (problems.Count > 0)
                {
                    reason = "navmesh-reference: " + problems.Count + " DANGLING navmesh reference(s) -- " +
                             string.Join(" | ", problems.ToArray()) +
                             " FIX: re-bake that scene through its builder and let the builder SAVE the scene " +
                             "(EditorSceneManager.MarkSceneDirty + SaveScene, throwing on failure -- " +
                             "RaidNavBake.cs is the reference shape). Never leave a bake unsaved.";
                    Debug.LogError(log.ToString() + MarkerFail + ": " + reason);
                    return false;
                }

                string partialNote = partials.Count == 0 ? "" : " -- " + string.Join(" ", partials.ToArray());
                reason = "NAVMESH REFERENCE OK -- " + assignedTotal + " assigned m_NavMeshData reference(s) across " +
                         scenesWithRefs + " of " + scenesScanned + " text-scanned shipped scene(s) ALL resolve to files " +
                         "that exist; " + selfDetail + partialNote;
                Debug.Log(log.ToString() + MarkerOk + ": " + reason);
                return true;
            }
            catch (Exception ex)
            {
                reason = "navmesh-reference: threw " + ex.GetType().Name + " -- " + ex.Message;
                Debug.LogError(log.ToString() + MarkerFail + ": " + reason);
                return false;
            }
        }

        // A Unity text scene starts with "%YAML". A builder-generated batchmode scene is
        // binary and starts with a NUL. Read the first bytes rather than guessing.
        private static bool IsYamlText(string fullPath)
        {
            try
            {
                using (var fs = new FileStream(fullPath, FileMode.Open, FileAccess.Read))
                {
                    var head = new byte[5];
                    int read = fs.Read(head, 0, head.Length);
                    if (read < 5) return false;
                    return head[0] == (byte)'%' && head[1] == (byte)'Y' && head[2] == (byte)'A' &&
                           head[3] == (byte)'M' && head[4] == (byte)'L';
                }
            }
            catch { return false; }
        }

        private static int LineOf(string text, int index)
        {
            int line = 1;
            for (int i = 0; i < index && i < text.Length; i++)
                if (text[i] == '\n') line++;
            return line;
        }
    }
}

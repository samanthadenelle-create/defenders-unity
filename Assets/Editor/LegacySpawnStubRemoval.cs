// =============================================================================
// LegacySpawnStubRemoval (WO-1834) — deletes the 4 legacy `spawn-0`..`spawn-3`
// WaveSpawnPoint stubs baked into Main_Castle_Overworld.unity.
// -----------------------------------------------------------------------------
// WHY (root cause proven at source 2026-09-17, WO-1834 — do NOT re-derive):
//   CastleSpawnPointInjector.Inject() SELF-SUPPRESSES whenever ANY WaveSpawnPoint
//   already exists in the scene (CastleSpawnPointInjector.cs:126-131 — by design,
//   it refuses to compete with a baked set). The castle hub scene carries exactly
//   4 baked stubs — one dead-centre marker per wall side — so the injector's real
//   20-marker fan (5 per side, +/-22m along each wall) has NEVER been placed.
//   Every wave that attacks a side therefore releases its whole roster from one
//   single coordinate: the owner's "straight approach" instead of a wall fan.
//   Proving line, device logcat 2026-09-17 12:59:06:
//     [CastleSpawnPointInjector] 4 WaveSpawnPoint(s) already present - skipping injection.
//
// WHY A BATCHMODE METHOD AND NOT A TEXT EDIT: CLAUDE.md §3 — `.unity` files are
// NEVER hand-edited (corruption-on-resave history). The scene is opened, mutated
// and saved through EditorSceneManager, the same path CastleNavPlaneScrub uses.
//
// SAFETY: this removes ONLY components whose SpawnId matches the four legacy ids
// EXACTLY. It is never a blanket WaveSpawnPoint purge — the injector's own ids are
// shaped `spawn-castle-<dir>-<i>` (CastleSpawnPointInjector.cs:156) and would not
// match, and neither would anything else. If ANY WaveSpawnPoint survives the pass,
// the run FAILS LOUD and saves NOTHING: an unexpected marker means the scene is not
// in the state WO-1834 proved, and guessing past that is exactly what §11B forbids.
//
// Verified before authoring (2026-09-17, read out of the scene file, not a doc):
//   - the scene holds EXACTLY 4 WaveSpawnPoint components (guid 19a9b4c5...), ids
//     spawn-0/1/2/3, gate indices 0/3/1/2, dirs S/E/W/N — matching the WO table;
//   - WaveManager's serialized `_spawnPoints: []` is EMPTY, so deleting these
//     GameObjects leaves NO dangling element behind (and WaveManager.cs:1684-1688
//     then repopulates from FindObjectsByType at loop start — which is precisely
//     how the injector's 20 markers will be picked up once the stubs are gone);
//   - no other component in the scene references the 4 GameObject fileIDs (their
//     only refs are their own Transform/MonoBehaviour back-pointers).
//
// Batchmode: DeNelle.Editor.LegacySpawnStubRemoval.RemoveLegacyStubs
// Menu:      Defenders/Castle/Remove Legacy Spawn Stubs (WO-1834)
//
// Judge the run by the MARKER on a FRESH log, never the exit code (CLAUDE.md §8):
//   success -> LEGACY_SPAWN_STUBS_REMOVED n=4
// Anything else — including no marker at all — is a FAILURE.
// =============================================================================
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using DeNelle.Core.Diagnostics;
using DeNelle.Village;

namespace DeNelle.Editor
{
    public static class LegacySpawnStubRemoval
    {
        private const string CastleScene = "Assets/Scenes/Main_Castle_Overworld.unity";
        private const string Sys = "SpawnStub";

        /// <summary>The success marker. Plain Debug.Log so it survives FlowTrace.Enabled=false.</summary>
        private const string Marker = "LEGACY_SPAWN_STUBS_REMOVED n=";

        /// <summary>Expected stub count — the state WO-1834 proved in the scene file.</summary>
        private const int ExpectedStubs = 4;

        /// <summary>
        /// The legacy ids, matched EXACTLY. The live producer emits `spawn-castle-&lt;dir&gt;-&lt;i&gt;`,
        /// so no injected marker can collide with this set.
        /// </summary>
        private static readonly string[] LegacyIds = { "spawn-0", "spawn-1", "spawn-2", "spawn-3" };

        [MenuItem("Defenders/Castle/Remove Legacy Spawn Stubs (WO-1834)")]
        public static void RemoveLegacyStubs()
        {
            FlowTrace.Enabled = true;
            using var _ = FlowTrace.Enter(Sys, "WO-1834 remove legacy spawn stubs");

            var scene = EditorSceneManager.OpenScene(CastleScene, OpenSceneMode.Single);
            if (!scene.IsValid())
            {
                FlowTrace.Fail(Sys, $"OpenScene returned an INVALID scene for '{CastleScene}'. " +
                                    "Nothing was modified and nothing was saved.");
                return;
            }

            // Include inactive: a DISABLED marker still suppresses the injector (its check is
            // FindObjectsByType<WaveSpawnPoint>() over the scene), so a stub hiding behind a
            // disabled GameObject must be seen here rather than silently left in place.
            var found = Object.FindObjectsByType<WaveSpawnPoint>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);
            int total = found == null ? 0 : found.Length;
            FlowTrace.Step(Sys, $"opened '{CastleScene}' — {total} WaveSpawnPoint(s) present before the pass.");

            int removed = 0;
            var skipped = new List<string>();

            for (int i = 0; i < total; i++)
            {
                var sp = found[i];
                if (sp == null) continue;

                string id = sp.SpawnId;
                string path = HierarchyPath(sp.transform);

                if (!IsLegacyId(id))
                {
                    skipped.Add($"'{id}' at '{path}'");
                    FlowTrace.Warn(Sys, $"NOT a legacy stub — left in place: spawnId='{id}' " +
                                        $"gateIndex={sp.GateIndex} dir='{sp.Direction}' at '{path}'.");
                    continue;
                }

                FlowTrace.Step(Sys, $"removing legacy stub spawnId='{id}' gateIndex={sp.GateIndex} " +
                                    $"dir='{sp.Direction}' gatePos={sp.GatePosition} at '{path}'.");

                // DestroyImmediate, not Destroy: edit-mode Destroy is deferred and would NOT be
                // reflected in the SaveScene below (CastleHubBuilder.cs:2868 uses the same call).
                Object.DestroyImmediate(sp.gameObject);
                removed++;
            }

            // Re-scan: the ONLY acceptable post-state is zero WaveSpawnPoints, so the injector
            // stops self-suppressing and places its full 5-per-side fan on the next scene load.
            var remainingObjs = Object.FindObjectsByType<WaveSpawnPoint>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);
            int remaining = remainingObjs == null ? 0 : remainingObjs.Length;

            if (removed != ExpectedStubs || remaining != 0)
            {
                var sb = new StringBuilder();
                sb.Append($"ABORTED WITHOUT SAVING — expected to remove exactly {ExpectedStubs} legacy ")
                  .Append($"stub(s) and leave 0 behind, but removed={removed} remaining={remaining}. ")
                  .Append("The scene is NOT in the state WO-1834 proved, so it is left untouched on disk ")
                  .Append("rather than guessed at (CLAUDE.md §11B). Survivors: ");

                if (remaining == 0) sb.Append("(none)");
                for (int i = 0; i < remaining; i++)
                {
                    var sp = remainingObjs[i];
                    if (sp == null) continue;
                    sb.Append($"[spawnId='{sp.SpawnId}' gateIndex={sp.GateIndex} dir='{sp.Direction}' ")
                      .Append($"at '{HierarchyPath(sp.transform)}'] ");
                }
                if (skipped.Count > 0) sb.Append("| non-legacy ids seen: ").Append(string.Join(", ", skipped));

                FlowTrace.Fail(Sys, sb.ToString());
                return; // NO MarkSceneDirty, NO SaveScene — the .unity file is not written.
            }

            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene))
            {
                FlowTrace.Fail(Sys, $"SaveScene returned FALSE for '{CastleScene}' — the {removed} stub(s) " +
                                    "were destroyed in memory but the scene on disk is UNCHANGED. " +
                                    "Do not treat this run as applied.");
                return;
            }

            FlowTrace.Step(Sys, $"saved '{CastleScene}' with {removed} legacy stub(s) removed and " +
                                "0 WaveSpawnPoint(s) remaining — CastleSpawnPointInjector will now " +
                                "place its full 20-marker fan (5 per side) on the next scene load.");

            // The gate-judged marker. Plain Debug.Log on purpose: the lead judges this substring on
            // a fresh log, and it must not depend on FlowTrace's enabled state or category filters.
            Debug.Log($"{Marker}{removed}");
        }

        private static bool IsLegacyId(string id)
        {
            if (string.IsNullOrEmpty(id)) return false;
            for (int i = 0; i < LegacyIds.Length; i++)
            {
                // Ordinal, case-sensitive, whole-string: never a StartsWith/Contains, which would
                // also swallow an injected `spawn-0...`-prefixed id if the format ever changes.
                if (string.Equals(id, LegacyIds[i], System.StringComparison.Ordinal)) return true;
            }
            return false;
        }

        /// <summary>Full scene path of a transform, so each removal is auditable from the log alone.</summary>
        private static string HierarchyPath(Transform t)
        {
            if (t == null) return "(null)";
            var sb = new StringBuilder(t.name);
            var p = t.parent;
            while (p != null)
            {
                sb.Insert(0, p.name + "/");
                p = p.parent;
            }
            return sb.ToString();
        }
    }
}

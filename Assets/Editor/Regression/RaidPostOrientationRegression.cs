// =============================================================================
// RaidPostOrientationRegression [raid-post-orientation]
//   markers RAID_POST_ORIENTATION_OK / _FAIL
// -----------------------------------------------------------------------------
// Assembly: DeNelle.EditorRegression.  Edit mode, opens the BAKED raid scenes.
// Registered ONCE in DataRegression.RunAll.  NEVER throws.
//
// WHAT IT LOCKS (WO-1807, 2026-09-16 — owner: "corners seem upside down",
// "towers are inverted", RaidBase_fortified_garrison, build 2026.09.17.372984):
//
// RaidBaseDresser.ReplaceChildrenWith clads every CornerPost_* / Watchtower_* /
// RaidSpire with a "/Visual" child. It used to destroy CHILDREN only — and the
// prefabs it clads carry their mesh on the ROOT (Assets/StructureContent/
// Tower_Medieval_Wood.prefab is ONE GameObject with MeshFilter + MeshRenderer +
// MeshCollider and `m_Children: []`). So the clad was STACKED on the original and
// every corner post was TWO mismatched towers in one place.
//
// ⛔ THE OBVIOUS ASSERTIONS WOULD HAVE PASSED THE BROKEN BAKE. This is the whole
// reason this file is written the way it is. On the owner's build:
//   * the host rotation is pure-yaw — CornerPost_Outer_E is w=-0.38268 y=0.92388
//     (225°) in the baked YAML, x=z=0;
//   * the /Visual child's local rotation is IDENTITY (w=1);
//   * the clad's bounds are 4.3 x 7.5 x 4.3 m, tall-in-Y, min.y ≈ 0.05.
// So "up-vector dot Vector3.up > 0.9" and "bounds.min.y within 0.5 m of ground"
// — the two natural pins — were BOTH GREEN while the owner was looking at the
// defect. An oracle that only asks those questions is theatre.
//
// THEREFORE PIN 1 IS THE LOAD-BEARING ONE: no enabled Renderer on the host ROOT.
// A root renderer IS the second tower. Pins 2–4 stay, because they are cheap and
// they catch the OTHER ways this can break (a clad that fails to instantiate, a
// clad pitched by the wall-panel −90 X correction, a clad left floating by a bad
// seat) — but a green here means pin 1 held, not merely that nothing was tipped.
//
// ⚠ NO SECOND COPY OF THE RULE. DeNelle.EditorRegression cannot reference
// DeNelle.Editor, so this cannot call RaidPostAudit.Evaluate, and that separation
// is deliberate: an oracle that imports its subject's own helper cannot catch the
// subject changing it (the same reasoning RaidWallMaterialRegression's header
// records). The audit is the INSTRUMENT (it also takes the PNGs); this is the GATE.
//
// Scene list is DISCOVERED from disk — never copied from RaidNavBake.RaidScenes or
// from a doc (CLAUDE.md §2/§5/§16).
//
// Entry points:
//   Run(out report)  — DataRegression suite shape.
//   RunAll()         — standalone, prints the marker.
// =============================================================================

using System;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DeNelle.Editor.Regression
{
    public static class RaidPostOrientationRegression
    {
        /// <summary>
        /// PANCAKE threshold for the clad's bounds ratio (size.y / max(size.x, size.z)).
        ///
        /// ⚠ THIS IS DELIBERATELY **NOT** RaidBaseGenerator.EnsureUpright's 0.8, AND THE REASON IS
        /// MEASURED, NOT PREFERRED. The first cut of this file used 0.8 — EnsureUpright's own rule —
        /// and the post-fix audit (`Builds/raid-post-audit-after.log`, 2026-09-16 20:46) reported 14
        /// "renders FLAT" defects at **ratio 0.75 / 0.76**, every one of them a `hexagon-green` kit
        /// post clad with `building_watchtower_green`. Those posts were then OPENED as frames
        /// (`Builds/raid-post-audit/RaidBase_raider_camp_small_CornerPost_Outer_E.png`): a clean,
        /// upright, crenellated stone watchtower standing correctly on the corner. The art is simply
        /// SQUAT. EnsureUpright's own warning text says this in as many words — *"If the art is
        /// genuinely squat, this is a false positive."*
        ///
        /// So the two thresholds answer two different questions and must not share a number:
        ///   * EnsureUpright asks "should I ROTATE this model?" — it can afford to be eager,
        ///     because rotating a squat prop is a visible mistake someone will notice.
        ///   * this gate asks "is a clad PANCAKED?" — it must not red on shipped, correct art,
        ///     because a gate that cries wolf gets ignored (see RaidWallMaterialRegression's header).
        ///
        /// 0.65 is derived from the three measured populations, not tuned until green:
        ///   * a clad pitched -90 X — the failure this pin exists to catch — is 4.30 / 7.52 = **0.57**
        ///     (the Synty `SM_Bld_Castle_Wall_Tower_M_01` bounds, read off the same audit);
        ///   * legitimately squat shipped art measures **0.75 / 0.76**;
        ///   * correct upright clads measure **1.75, 1.84, 2.16**.
        /// 0.65 sits between 0.57 and 0.75 with margin on both sides. If a future clad lands between
        /// them, OPEN THE FRAME and judge it — do not move this number to make a red go away.
        ///
        /// The definitive inversion test is <see cref="MinUpDot"/> below, which is exact and has no
        /// such ambiguity; this pin is the backstop for a clad that is upright-transformed but
        /// pancaked (e.g. a height fit applied on the wrong axis).
        /// </summary>
        private const float UprightRatio = 0.65f;

        /// <summary>Minimum up-vector alignment for a clad that is not pitched or inverted.</summary>
        private const float MinUpDot = 0.9f;

        /// <summary>How far a seated post's lowest RENDERED point may sit from the ground.</summary>
        private const float SeatToleranceM = 0.5f;

        private const string Remedy =
            "REMEDY: the clad seam is RaidBaseDresser.ReplaceChildrenWith / StripRootArt " +
            "(Assets/Editor/WallTools/RaidBaseDresser.cs). After any change there the raid scenes " +
            "MUST be re-baked - they are baked files: run " +
            "DeNelle.Editor.RaidBaseGenerator.BuildAllRaidScenes then DeNelle.Editor.RaidNavBake.BakeAll. " +
            "For the frames, run DeNelle.Editor.RaidPostAudit.AuditAll and OPEN the PNGs under " +
            "Builds/raid-post-audit/.";

        // -- entry points -----------------------------------------------------

        /// <summary>Standalone batch entry - prints the RAID_POST_ORIENTATION_OK/_FAIL marker.</summary>
        public static void RunAll()
        {
            if (Run(out string report)) Debug.Log("RAID_POST_ORIENTATION_OK " + report);
            else Debug.LogError("RAID_POST_ORIENTATION_FAIL " + report);
        }

        public static bool Run(out string report)
        {
            try { return RunCore(out report); }
            catch (Exception ex)
            {
                report = "raid-post-orientation: oracle threw " + ex.GetType().Name + ": " + ex.Message;
                return false;
            }
        }

        // ---------------------------------------------------------------------

        private static bool RunCore(out string report)
        {
            var failures = new List<string>();
            var notes = new StringBuilder();
            string restore = SceneManager.GetActiveScene().path;

            var scenes = DiscoverScenes();
            if (scenes.Count == 0)
            {
                // A missing bake is a GAP, not a pass - say so and fail closed, exactly as
                // CLAUDE.md §8 says of an absent marker.
                report = "raid-post-orientation: NO RaidBase_*.unity under Assets/Scenes - the raid " +
                         "bake has never run on this clone, so nothing was checked. " + Remedy;
                return false;
            }

            int scanned = 0;
            int scenesWithPosts = 0;

            for (int i = 0; i < scenes.Count; i++)
            {
                string path = scenes[i];
                string sceneName = System.IO.Path.GetFileNameWithoutExtension(path);
                Scene scene;
                try
                {
                    // SINGLE, mirroring RaidKeepReachRegression: an additive open in batchmode
                    // gives non-deterministic results, and a flaky oracle trains everyone to
                    // ignore it.
                    scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
                }
                catch (Exception ex)
                {
                    failures.Add($"raid-post-orientation: {sceneName} would not open ({ex.GetType().Name}: {ex.Message})");
                    continue;
                }
                if (!scene.IsValid())
                {
                    failures.Add($"raid-post-orientation: {sceneName} opened INVALID");
                    continue;
                }

                int inScene = 0;
                var roots = scene.GetRootGameObjects();
                for (int r = 0; r < roots.Length; r++)
                {
                    var transforms = roots[r].GetComponentsInChildren<Transform>(true);
                    for (int t = 0; t < transforms.Length; t++)
                    {
                        var tr = transforms[t];
                        if (tr == null || tr.name == null) continue;
                        if (!tr.name.StartsWith("CornerPost_", StringComparison.Ordinal) &&
                            !tr.name.StartsWith("Watchtower_", StringComparison.Ordinal))
                            continue;

                        inScene++;
                        scanned++;
                        string fault = Judge(tr.gameObject);
                        if (fault != null) failures.Add($"raid-post-orientation: {sceneName}/{tr.name}: {fault}");
                    }
                }

                if (inScene > 0) scenesWithPosts++;
                notes.Append(sceneName).Append('=').Append(inScene).Append(' ');
            }

            RestoreScene(restore);

            if (scanned == 0)
            {
                report = $"raid-post-orientation: {scenes.Count} raid scene(s) opened but NOT ONE carries a " +
                         "CornerPost_* or Watchtower_* - the ring was never built. This is a gap, not a pass. " +
                         Remedy;
                return false;
            }

            if (failures.Count > 0)
            {
                report = $"{failures.Count} post(s) bad of {scanned} across {scenesWithPosts} scene(s): " +
                         string.Join(" | ", failures) + " -- " + Remedy;
                return false;
            }

            report = $"raid-post-orientation: {scanned} corner post(s)/watchtower(s) across {scenesWithPosts} " +
                     $"baked scene(s) each render through EXACTLY ONE upright, seated /Visual clad with no " +
                     $"stacked root mesh [{notes.ToString().TrimEnd()}]";
            return true;
        }

        /// <summary>
        /// The four pins on one post. Returns null when clean, else the sentence naming
        /// every fault found (all of them, not the first - a seat fixing this wants the
        /// whole list from one run).
        /// </summary>
        private static string Judge(GameObject host)
        {
            var faults = new List<string>();

            // ── PIN 1 (the load-bearing one) — no art on the host ROOT ──────────
            var rootRends = host.GetComponents<Renderer>();
            for (int i = 0; i < rootRends.Length; i++)
            {
                var rend = rootRends[i];
                if (rend == null || !rend.enabled) continue;
                var mf = host.GetComponent<MeshFilter>();
                string mesh = mf != null && mf.sharedMesh != null ? mf.sharedMesh.name : "?";
                string mat = rend.sharedMaterial != null ? rend.sharedMaterial.name : "?";
                var rb = rend.bounds;
                faults.Add($"the HOST ROOT still renders '{mesh}' (mat '{mat}', " +
                           $"{rb.size.x:0.#}x{rb.size.y:0.#}x{rb.size.z:0.#}m) STACKED on the clad - " +
                           "two towers in one spot, which is what reads as inverted from the ground");
                break;
            }

            // ── PIN 2 — exactly one /Visual clad, and it carries the renderers ───
            var vis = host.transform.Find("Visual");
            if (vis == null)
            {
                faults.Add("no '/Visual' clad child - ReplaceChildrenWith never ran or its model failed to load");
                return string.Join("; ", faults);
            }

            if (!Encapsulate(vis.gameObject, out Bounds b))
            {
                faults.Add("'/Visual' carries NO renderer - the clad is invisible");
                return string.Join("; ", faults);
            }

            // ── PIN 3 — the clad is UPRIGHT, both ways of asking ────────────────
            float upDot = Vector3.Dot(vis.up, Vector3.up);
            if (upDot <= MinUpDot)
                faults.Add($"clad up-vector dot Vector3.up = {upDot:0.###} (<= {MinUpDot:0.0}) - it is pitched/inverted " +
                           "(the -90 X wall-panel correction must never reach a post)");

            float widest = Mathf.Max(b.size.x, b.size.z);
            float ratio = widest <= 0.0001f ? 0f : b.size.y / widest;
            if (ratio < UprightRatio)
                faults.Add($"clad bounds ratio {ratio:0.##} < {UprightRatio:0.0} " +
                           $"({b.size.x:0.#}x{b.size.y:0.#}x{b.size.z:0.#}m) - it renders FLAT");

            // ── PIN 4 — the clad is SEATED on the ground ───────────────────────
            if (Mathf.Abs(b.min.y) > SeatToleranceM)
                faults.Add($"clad lowest rendered point is y={b.min.y:0.##}m, more than {SeatToleranceM:0.0}m " +
                           "off the ground - it floats or is sunk");

            return faults.Count == 0 ? null : string.Join("; ", faults);
        }

        private static bool Encapsulate(GameObject go, out Bounds bounds)
        {
            bounds = default;
            var rends = go.GetComponentsInChildren<Renderer>(true);
            bool any = false;
            for (int i = 0; i < rends.Length; i++)
            {
                if (rends[i] == null) continue;
                if (!any) { bounds = rends[i].bounds; any = true; }
                else bounds.Encapsulate(rends[i].bounds);
            }
            return any;
        }

        private static List<string> DiscoverScenes()
        {
            var found = new List<string>();
            string[] guids = AssetDatabase.FindAssets("t:Scene", new[] { "Assets/Scenes" });
            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                if (string.IsNullOrEmpty(path)) continue;
                string file = System.IO.Path.GetFileName(path);
                if (file.StartsWith("RaidBase_", StringComparison.Ordinal)) found.Add(path);
            }
            found.Sort(StringComparer.Ordinal);
            return found;
        }

        private static void RestoreScene(string path)
        {
            if (string.IsNullOrEmpty(path)) return;
            if (SceneManager.GetActiveScene().path == path) return;
            if (!System.IO.File.Exists(path)) return;
            try { EditorSceneManager.OpenScene(path, OpenSceneMode.Single); }
            catch (Exception ex)
            {
                Debug.LogWarning("[RaidPostOrientation] could not restore " + path + ": " + ex.Message);
            }
        }
    }
}

// =============================================================================
// RaidPostAudit - the corner-post / watchtower ORIENTATION + CLAD audit (WO-1807).
// Marker: RAID_POST_AUDIT_OK <n>   (distinct per CLAUDE.md s.8 - never REGRESSION_OK)
// -----------------------------------------------------------------------------
// Assembly: DeNelle.Editor (editor-only).
//
// WHY THIS FILE HAD TO EXIST (2026-09-16):
// The owner played build 2026.09.17.372984 and reported "corners seem upside down"
// and "towers are inverted" in RaidBase_fortified_garrison. Her screenshot never
// reached the seat. Nothing in this project could answer the question, because
// every existing instrument measured the WRONG AXIS:
//   * [Flow:RaidArt] reports SHADER and material, and its bounds line reads
//     4.3 x 7.5 x 4.3m - i.e. the Synty clad is tall-in-Y, so "the clad is
//     pitched -90" reads CLEAN on the broken build.
//   * The baked YAML agrees: CornerPost_Outer_E host rotation is pure-Y (225 deg,
//     w=-0.38268 y=0.92388) and its /Visual child is IDENTITY.
//   * So an audit that checked "host euler + /Visual local rotation" - the obvious
//     shape - would have PASSED the very bake the owner is looking at.
// The one thing neither instrument reported is that the HOST ROOT STILL CARRIES ITS
// OWN MeshFilter/MeshRenderer. Assets/StructureContent/Tower_Medieval_Wood.prefab is
// a SINGLE GameObject with the mesh on the root and NO children, and
// RaidBaseDresser.ReplaceChildrenWith only destroys CHILDREN - so cladding a corner
// post STACKS a polyperfect wooden tower and a Synty stone tower in the same spot.
//
// THEREFORE THIS AUDIT MEASURES PER RENDERER, NOT PER TRANSFORM: for every
// CornerPost_* / Watchtower_* it prints whether a renderer sits on the host ROOT,
// each renderer's world bounds size, its min.y against the ground, and
// size.y / max(size.x, size.z) - the SAME ratio RaidBaseGenerator.EnsureUpright
// uses to decide "imported flat". A number in the log beats squinting at a PNG.
//
// ...and it also takes the PNG, because a mesh authored upside down has the same
// bounds and the same transform as one authored upright. Only the frame settles it.
//
// EDIT-MODE AND SYNCHRONOUS, deliberately - same trap DungeonSceneCapture's header
// records: under `-batchmode -quit -executeMethod` a Play-mode capture returns
// before Play ticks and writes ZERO pngs while reporting success.
//
// INVOKE:
//   powershell -File .\run-unity-method.ps1 `
//     -Method DeNelle.Editor.RaidPostAudit.AuditAll -LogName raid-post-audit.log `
//     -ExpectMarker RAID_POST_AUDIT_OK
//
// OUTPUT: Builds/raid-post-audit/<scene>_<object>.png
// =============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace DeNelle.Editor
{
    public static class RaidPostAudit
    {
        private const string SceneFolder = "Assets/Scenes";
        private const string ScenePattern = "RaidBase_*.unity";
        private const string OutFolder = "Builds/raid-post-audit";

        private const int Width = 1280;
        private const int Height = 720;

        // Blank-frame test, same reasoning as DungeonSceneCapture: "wrote N PNGs" is worth
        // nothing if they are N flat rectangles. topShare is the load-bearing signal.
        private const int MinDistinctColours = 6;
        private const float TopShareBlank = 0.98f;

        /// <summary>
        /// PANCAKE threshold for a clad's bounds ratio (size.y / max(size.x, size.z)).
        ///
        /// ⚠ NOT EnsureUpright's 0.8, and the reason is measured. See the long derivation on
        /// RaidPostOrientationRegression.UprightRatio — in one line: a -90 X pitched Synty clad
        /// measures 0.57, legitimately squat shipped art (`building_watchtower_green`) measures
        /// 0.75-0.76 and was confirmed UPRIGHT by opening the frame, and correct clads measure
        /// 1.75-2.16. 0.65 separates the failure from the art with margin on both sides.
        /// The two constants are kept in step by that shared derivation, not by a reference:
        /// DeNelle.EditorRegression cannot reference DeNelle.Editor.
        /// </summary>
        public const float UprightRatio = 0.65f;

        /// <summary>How far above/below y=0 a seated post's lowest rendered point may sit.</summary>
        public const float SeatToleranceM = 0.5f;

        // ---------------------------------------------------------------------

        public static void AuditAll()
        {
            var log = new StringBuilder();
            log.AppendLine("=== RaidPostAudit (WO-1807): corner-post / watchtower orientation + clad audit ===");
            log.AppendLine("    ratio = boundsY / max(boundsX, boundsZ); < " + UprightRatio.ToString("0.0") +
                           " is EnsureUpright's own 'imported FLAT' verdict.");

            string[] scenes;
            try { scenes = Directory.GetFiles(SceneFolder, ScenePattern, SearchOption.TopDirectoryOnly); }
            catch (Exception ex)
            {
                Debug.LogError(log + $"RAID_POST_AUDIT_FAIL: cannot enumerate {SceneFolder}: {ex.Message}");
                return;
            }
            if (scenes.Length == 0)
            {
                Debug.LogError(log + $"RAID_POST_AUDIT_FAIL: no {ScenePattern} under {SceneFolder} - " +
                                     "nothing to audit. Has RaidBaseGenerator.BuildAllRaidScenes run?");
                return;
            }

            Directory.CreateDirectory(OutFolder);

            int shots = 0;
            int posts = 0;
            var defects = new List<string>();
            var blanks = new List<string>();

            foreach (var scenePath in scenes)
            {
                string sceneName = Path.GetFileNameWithoutExtension(scenePath);
                string norm = scenePath.Replace('\\', '/');
                try
                {
                    var scene = EditorSceneManager.OpenScene(norm, OpenSceneMode.Single);
                    if (!scene.IsValid())
                    {
                        log.AppendLine($"  [{sceneName}] scene failed to open - SKIPPED (this is a gap, not a pass)");
                        defects.Add($"{sceneName}: scene would not open");
                        continue;
                    }
                    log.AppendLine($"  [{sceneName}]");

                    var hosts = CollectPosts(scene);
                    if (hosts.Count == 0)
                    {
                        log.AppendLine("    NO CornerPost_* / Watchtower_* objects found.");
                        continue;
                    }

                    GameObject shotCorner = null, shotTower = null;

                    foreach (var host in hosts)
                    {
                        posts++;
                        string verdict = Describe(host, log);
                        if (verdict != null) defects.Add($"{sceneName}/{host.name}: {verdict}");

                        if (shotCorner == null && host.name.StartsWith("CornerPost_", StringComparison.Ordinal))
                            shotCorner = host;
                        if (shotTower == null && host.name.StartsWith("Watchtower_", StringComparison.Ordinal))
                            shotTower = host;
                    }

                    foreach (var subject in new[] { shotCorner, shotTower })
                    {
                        if (subject == null) continue;
                        string outPath = Path.Combine(OutFolder, $"{sceneName}_{subject.name}.png");
                        if (!GroundShot(subject, outPath, out string frame))
                            blanks.Add($"{sceneName}_{subject.name} ({frame})");
                        else
                            shots++;
                        log.AppendLine($"    PNG {subject.name,-22} -> {Path.GetFileName(outPath)}  {frame}");
                    }
                }
                catch (Exception ex)
                {
                    log.AppendLine($"  [{sceneName}] THREW: {ex.GetType().Name}: {ex.Message}");
                    defects.Add($"{sceneName}: threw {ex.GetType().Name}");
                }
            }

            log.AppendLine($"  audited {posts} post(s); wrote {shots} non-blank frame(s) to {OutFolder}/");

            if (blanks.Count > 0)
                log.AppendLine("  BLANK/FAILED FRAMES: " + string.Join(", ", blanks) +
                               " -- a uniform frame proves nothing rendered; it is NOT 'the post is fine'.");

            if (defects.Count > 0)
            {
                Debug.LogError(log + $"RAID_POST_AUDIT_FAIL: {defects.Count} defect(s):\n    " +
                                     string.Join("\n    ", defects));
                return;
            }
            if (blanks.Count > 0)
            {
                Debug.LogError(log + $"RAID_POST_AUDIT_FAIL: {blanks.Count} blank frame(s) - see list above.");
                return;
            }

            Debug.Log(log + $"RAID_POST_AUDIT_OK {posts}");
        }

        // ---------------------------------------------------------------------

        /// <summary>
        /// Walk the OPEN SCENE's own roots rather than FindObjectsByType: the sorted overloads of
        /// that API are deprecated in this editor, and walking the scene keeps the audit honest
        /// about which scene it just opened (a stray object from a previous Single open cannot
        /// leak into the count).
        /// </summary>
        private static List<GameObject> CollectPosts(UnityEngine.SceneManagement.Scene scene)
        {
            var found = new List<GameObject>();
            var roots = scene.GetRootGameObjects();
            for (int r = 0; r < roots.Length; r++)
            {
                var all = roots[r].GetComponentsInChildren<Transform>(true);
                for (int i = 0; i < all.Length; i++)
                {
                    var t = all[i];
                    if (t == null || t.name == null) continue;
                    if (t.name.StartsWith("CornerPost_", StringComparison.Ordinal) ||
                        t.name.StartsWith("Watchtower_", StringComparison.Ordinal))
                        found.Add(t.gameObject);
                }
            }
            found.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
            return found;
        }

        /// <summary>
        /// Print the per-renderer measurement for one post and return a defect string,
        /// or null when it is clean. This is the ONE place the pass/fail rule lives;
        /// RaidPostOrientationRegression calls <see cref="Evaluate"/> below so the
        /// regression and the audit cannot drift apart.
        /// </summary>
        private static string Describe(GameObject host, StringBuilder log)
        {
            var report = Evaluate(host);

            log.AppendLine($"    {host.name,-22} hostRot={Fmt(host.transform.rotation.eulerAngles)} " +
                           $"pos={Fmt(host.transform.position)} rootRenderer={(report.RootRenderer ? "YES" : "no")}");

            if (report.RootRenderer)
                log.AppendLine($"      ROOT   mesh='{report.RootMesh}' mat='{report.RootMaterial}' " +
                               $"size={Fmt(report.RootBounds.size)} minY={report.RootBounds.min.y:0.##} " +
                               $"ratio={report.RootRatio:0.##}");

            if (report.Visual == null)
                log.AppendLine("      VISUAL none");
            else
                log.AppendLine($"      VISUAL localRot={Fmt(report.Visual.transform.localRotation.eulerAngles)} " +
                               $"up={Fmt(report.VisualUp)} upDot={report.VisualUpDot:0.###} " +
                               $"size={Fmt(report.VisualBounds.size)} minY={report.VisualBounds.min.y:0.##} " +
                               $"ratio={report.VisualRatio:0.##}");

            return report.Defect;
        }

        /// <summary>Everything measured about one corner post / watchtower.</summary>
        public struct PostReport
        {
            public bool RootRenderer;
            public string RootMesh;
            public string RootMaterial;
            public Bounds RootBounds;
            public float RootRatio;

            public GameObject Visual;
            public Vector3 VisualUp;
            public float VisualUpDot;
            public Bounds VisualBounds;
            public float VisualRatio;

            /// <summary>Null when clean; otherwise the human sentence naming what is wrong.</summary>
            public string Defect;
        }

        /// <summary>
        /// Measure one post. SHARED with the regression on purpose (CLAUDE.md s.2/s.5/s.16:
        /// a second copy of a rule is the bug). The four things asserted:
        ///   1. NO enabled renderer on the host ROOT - a root renderer is the second, stacked
        ///      tower the owner reads as "inverted".
        ///   2. A /Visual child exists and carries the renderers.
        ///   3. The /Visual is UPRIGHT: its up-vector . Vector3.up &gt; 0.9 AND its bounds are
        ///      taller than wide (ratio &gt;= UprightRatio).
        ///   4. The /Visual is SEATED: bounds.min.y within SeatToleranceM of the ground.
        /// </summary>
        public static PostReport Evaluate(GameObject host)
        {
            var r = new PostReport { RootMesh = "-", RootMaterial = "-" };
            if (host == null) { r.Defect = "host is null"; return r; }

            var rootRends = host.GetComponents<Renderer>();
            for (int i = 0; i < rootRends.Length; i++)
            {
                var rend = rootRends[i];
                if (rend == null || !rend.enabled) continue;
                r.RootRenderer = true;
                r.RootBounds = rend.bounds;
                var mf = host.GetComponent<MeshFilter>();
                r.RootMesh = mf != null && mf.sharedMesh != null ? mf.sharedMesh.name : "?";
                r.RootMaterial = rend.sharedMaterial != null ? rend.sharedMaterial.name : "?";
                r.RootRatio = Ratio(rend.bounds);
                break;
            }

            var vis = host.transform.Find("Visual");
            if (vis != null)
            {
                r.Visual = vis.gameObject;
                r.VisualUp = vis.up;
                r.VisualUpDot = Vector3.Dot(vis.up, Vector3.up);
                if (Encapsulate(vis.gameObject, out Bounds vb))
                {
                    r.VisualBounds = vb;
                    r.VisualRatio = Ratio(vb);
                }
            }

            var faults = new List<string>();
            if (r.RootRenderer)
                faults.Add($"host ROOT still renders '{r.RootMesh}' (mat '{r.RootMaterial}', " +
                           $"{r.RootBounds.size.x:0.#}x{r.RootBounds.size.y:0.#}x{r.RootBounds.size.z:0.#}m) " +
                           "ON TOP OF the clad - two towers in one spot");
            if (r.Visual == null)
                faults.Add("no /Visual clad child");
            else if (r.VisualBounds.size == Vector3.zero)
                faults.Add("/Visual has no measurable renderer bounds");
            else
            {
                if (r.VisualUpDot <= 0.9f)
                    faults.Add($"/Visual up-vector dot Vector3.up = {r.VisualUpDot:0.###} (<= 0.9) - it is pitched/inverted");
                if (r.VisualRatio < UprightRatio)
                    faults.Add($"/Visual bounds ratio {r.VisualRatio:0.##} < {UprightRatio:0.0} - it renders FLAT");
                if (Mathf.Abs(r.VisualBounds.min.y) > SeatToleranceM)
                    faults.Add($"/Visual minY {r.VisualBounds.min.y:0.##}m is more than {SeatToleranceM:0.0}m off the ground");
            }

            if (faults.Count > 0) r.Defect = string.Join("; ", faults);
            return r;
        }

        private static float Ratio(Bounds b)
        {
            float widest = Mathf.Max(b.size.x, b.size.z);
            return widest <= 0.0001f ? 0f : b.size.y / widest;
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

        private static string Fmt(Vector3 v) => $"({v.x:0.##},{v.y:0.##},{v.z:0.##})";

        // -- the frame --------------------------------------------------------

        /// <summary>
        /// Photograph one post from HERO EYE HEIGHT, standing outside it looking in - the
        /// angle the owner is looking from when she calls it upside down. A top-down or
        /// orbit shot would hide exactly the stacked-silhouette defect.
        /// </summary>
        private static bool GroundShot(GameObject host, string outPath, out string verdict)
        {
            if (!Encapsulate(host, out Bounds b))
            {
                verdict = "no renderer bounds - nothing to frame";
                return false;
            }

            // Stand off along the post's own outward radial (its position from the arena
            // centre), so the camera is always OUTSIDE the ring looking at the post.
            var flat = new Vector3(host.transform.position.x, 0f, host.transform.position.z);
            var outward = flat.sqrMagnitude > 0.01f ? flat.normalized : Vector3.back;
            float dist = Mathf.Max(14f, b.size.magnitude * 1.6f);

            var camPos = b.center + outward * dist + Vector3.up * (1.7f - b.center.y);
            var look = Quaternion.LookRotation((b.center - camPos).normalized, Vector3.up);
            return RenderTo(outPath, camPos, look, 55f, out verdict);
        }

        private static bool RenderTo(string outPath, Vector3 pos, Quaternion rot, float fov, out string verdict)
        {
            GameObject camGo = null;
            RenderTexture rt = null;
            RenderTexture prevActive = RenderTexture.active;
            Texture2D shot = null;
            try
            {
                camGo = new GameObject("~RaidPostAuditCam") { hideFlags = HideFlags.HideAndDontSave };
                camGo.transform.SetPositionAndRotation(pos, rot);

                var cam = camGo.AddComponent<Camera>();
                cam.fieldOfView = fov;
                cam.nearClipPlane = 0.05f;
                cam.farClipPlane = 5000f;
                // Honour the bake's own sky. Forcing a background would hide a silhouette defect.
                cam.clearFlags = CameraClearFlags.Skybox;

                rt = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32) { antiAliasing = 1 };
                cam.targetTexture = rt;
                cam.Render();

                RenderTexture.active = rt;
                shot = new Texture2D(Width, Height, TextureFormat.RGB24, false);
                shot.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
                shot.Apply(false);

                int distinct = CountDistinct(shot.GetPixels32(), out float meanLuma, out float topShare);
                File.WriteAllBytes(outPath, shot.EncodeToPNG());

                bool blank = distinct < MinDistinctColours || topShare > TopShareBlank;
                verdict = $"luma={meanLuma:0.###} colours={distinct} top={topShare:P1}" + (blank ? "  BLANK" : "");
                return !blank;
            }
            catch (Exception ex)
            {
                verdict = $"threw {ex.GetType().Name}: {ex.Message}";
                return false;
            }
            finally
            {
                RenderTexture.active = prevActive;
                if (shot != null) UnityEngine.Object.DestroyImmediate(shot);
                if (rt != null) { rt.Release(); UnityEngine.Object.DestroyImmediate(rt); }
                if (camGo != null) UnityEngine.Object.DestroyImmediate(camGo);
            }
        }

        private static int CountDistinct(Color32[] px, out float meanLuma, out float topShare)
        {
            var counts = new Dictionary<int, int>(1024);
            double lumaSum = 0d;
            int top = 0;
            for (int i = 0; i < px.Length; i++)
            {
                Color32 p = px[i];
                lumaSum += (0.2126 * p.r + 0.7152 * p.g + 0.0722 * p.b) / 255d;
                int key = ((p.r >> 3) << 10) | ((p.g >> 3) << 5) | (p.b >> 3);
                counts.TryGetValue(key, out int n);
                n++;
                counts[key] = n;
                if (n > top) top = n;
            }
            meanLuma = px.Length == 0 ? 0f : (float)(lumaSum / px.Length);
            topShare = px.Length == 0 ? 1f : (float)top / px.Length;
            return counts.Count;
        }
    }
}

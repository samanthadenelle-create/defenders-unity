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
using DeNelle.Village;              // DefenseTower - the catalog id ArmTower stamped on each turret (WO-1817)
using DeNelle.Village.World.Camps;  // RaidSpire.VisualHeight - the bake's own fitted height (WO-1820)

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

        /// <summary>
        /// WO-1817 - how far a Watchtower_*'s CLAD height may sit from its authored turret cadence,
        /// as a FRACTION of that cadence. 0.10 = the ticket's "within 10%".
        ///
        /// A tolerance rather than an equality because the clad is fitted on its renderer BOUNDS and
        /// then re-measured on the same bounds after a uniform scale, so float round-trip and any
        /// bounds recompute can move the last centimetre. It is NOT slack for a wrong model: the
        /// defect this pin exists to catch spans 0.05 m to 17.96 m against a 4.80 m target - between
        /// 1% and 374% of it - so 10% separates the failure from the noise by two orders of magnitude.
        /// If a post ever lands just outside this band, OPEN THE FRAME; do not widen the number.
        /// </summary>
        public const float CladHeightTolerance = 0.10f;

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

            // A per-scene cache that outlives the run would measure one scene's walls against the
            // next scene's corner posts (WO-1822).
            _wallHeightScene = null;
            _wallHeightCache = 0f;

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

                    GameObject shotCorner = null, shotTower = null, shotSpire = null;

                    foreach (var host in hosts)
                    {
                        posts++;
                        string verdict = Describe(host, log);
                        if (verdict != null) defects.Add($"{sceneName}/{host.name}: {verdict}");

                        if (shotCorner == null && host.name.StartsWith("CornerPost_", StringComparison.Ordinal))
                            shotCorner = host;
                        if (shotTower == null && host.name.StartsWith("Watchtower_", StringComparison.Ordinal))
                            shotTower = host;
                        // WO-1820 - the win condition gets its own frame. A number in the log says the
                        // spire is 14.40 m; only the picture says it reads as a monument.
                        if (shotSpire == null && string.Equals(host.name, "RaidSpire", StringComparison.Ordinal))
                            shotSpire = host;
                    }

                    foreach (var subject in new[] { shotCorner, shotTower, shotSpire })
                    {
                        if (subject == null) continue;
                        string outPath = Path.Combine(OutFolder, $"{sceneName}_{subject.name}.png");
                        if (!GroundShot(subject, outPath, out string frame))
                            blanks.Add($"{sceneName}_{subject.name} ({frame})");
                        else
                            shots++;
                        log.AppendLine($"    PNG {subject.name,-22} -> {Path.GetFileName(outPath)}  {frame}");
                        ReportNeighbours(scene, subject, log);
                        ReportOwnMeshes(subject, log);
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
                    // WO-1820: RaidSpire joins the audit. It is clad by the SAME
                    // RaidBaseDresser.ReplaceChildrenWith seam as the posts, and it was rendering at
                    // 0.14 m in three of the four baked scenes - the raid's WIN CONDITION, at 1/100
                    // scale, for however long - precisely because nothing here ever looked at it.
                    if (t.name.StartsWith("CornerPost_", StringComparison.Ordinal) ||
                        t.name.StartsWith("Watchtower_", StringComparison.Ordinal) ||
                        string.Equals(t.name, "RaidSpire", StringComparison.Ordinal))
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
            {
                string cadence = report.CadenceH > 0.01f
                    ? $"cadence={report.CadenceH:0.##} ('{report.CatalogId}')"
                    : "cadence=none";
                log.AppendLine($"      VISUAL localRot={Fmt(report.Visual.transform.localRotation.eulerAngles)} " +
                               $"up={Fmt(report.VisualUp)} upDot={report.VisualUpDot:0.###} " +
                               $"size={Fmt(report.VisualBounds.size)} minY={report.VisualBounds.min.y:0.##} " +
                               $"ratio={report.VisualRatio:0.##} {cadence} " +
                               $"muzzleY={(host.transform.position.y + 2f):0.##}");
            }

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

            /// <summary>WO-1817: the authored turret cadence target, or 0 when this post has none.</summary>
            public float CadenceH;
            /// <summary>WO-1820: non-null when this host IS the raid objective (it stands on the
            /// KeepPlatform, not on the ground - see the seat pin).</summary>
            public RaidSpire Spire;
            /// <summary>WO-1821: true when this turret is an authored siege machine, which by ruling
            /// keeps its own art and receives NO tower clad.</summary>
            public bool SiegeHost;
            /// <summary>WO-1817: the catalog id read off the host's DefenseTower, or "-".</summary>
            public string CatalogId;

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
            var r = new PostReport { RootMesh = "-", RootMaterial = "-", CatalogId = "-" };
            if (host == null) { r.Defect = "host is null"; return r; }

            // WO-1817 - the authored cadence for this post, from the catalog id RaidBaseGenerator.
            // ArmTower stamped on it. CornerPost_* / RaidSpire carry no DefenseTower and so no
            // target; they stay at 0 and pin 5 below skips them (WO-1817 s.4 item 2).
            var tower = host.GetComponent<DefenseTower>();
            if (tower != null && !string.IsNullOrEmpty(tower.CatalogId))
            {
                r.CatalogId = tower.CatalogId;
                r.SiegeHost = RaidBaseDresser.IsSiegeHost(host);
                r.CadenceH = RaidBaseGenerator.TurretCadenceHeight(tower.CatalogId);
            }
            else
            {
                // WO-1820 - the spire's authored height is the one the BAKE recorded on it, which is
                // also what sizes its hero-contact collider (RaidSpire.EnsureHittable). Reading it
                // rather than re-deriving the monument clamp keeps art, collider and component on one
                // number; SpireMonumentMultiplier is internal to this assembly but the REGRESSION
                // cannot see it, so a formula here would have to be copied there (CLAUDE.md s.2).
                var spire = host.GetComponent<RaidSpire>();
                if (spire != null)
                {
                    r.Spire = spire;
                    r.CatalogId = string.IsNullOrEmpty(spire.CatalogId) ? "-" : spire.CatalogId;
                    r.CadenceH = spire.VisualHeight;
                }
                else if (host.name != null &&
                         host.name.StartsWith("CornerPost_", StringComparison.Ordinal))
                {
                    // WO-1822 - the corner cadence is wall-relative, so the audit measures the SCENE's
                    // own tallest wall rather than re-deriving a kit table. Same number the dresser
                    // fitted against (its `wallH`), taken from the built result instead of the recipe.
                    r.CatalogId = "corner";
                    r.CadenceH = RaidBaseGenerator.CornerCadenceHeight(SceneWallHeight(host));
                }
            }

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

            // ⛔ WO-1821 - A SIEGE HOST HAS NO CLAD BY DESIGN, so the clad pins below cannot apply to
            // it. It is NOT simply skipped: a catapult with neither clad nor authored art would be an
            // INVISIBLE turret that still shoots, and a silent skip would pass it green. The pin that
            // replaces them is "it renders SOMETHING".
            if (r.SiegeHost)
            {
                if (!Encapsulate(host, out Bounds sb) || sb.size.y <= 0.01f)
                    r.Defect = "it is an authored SIEGE turret (no tower clad, WO-1821) but carries NO " +
                               "renderer at all - an invisible turret that still fires. Its catalog art " +
                               "failed to load and nothing replaced it";
                return r;
            }

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
                // ⛔ THE SPIRE IS EXEMPT FROM THE GROUND-SEAT PIN, AND THAT IS NOT A WEAKENING
                // (WO-1820, 2026-09-17). It does not stand on the ground: RaidBaseGenerator's
                // ReseatSpireOnKeepPlatform deliberately lifts it onto the KeepPlatform slab -
                // "1.5 m on the castle kits, 0.8 m on dungeon-stone" (RaidBaseGenerator.cs:2184),
                // MEASURED off the slab, never hardcoded - because WO-1749 found the spire seated on
                // a ground that stopped existing later in the same build, putting
                // RaidSpire.WorldPosition inside solid geometry (1650 PathPartial, zero PathComplete
                // on the owner's Seeker run). This pin flagged exactly those three lifts - 1.5/1.5/0.8,
                // matching the documented per-kit slab to the centimetre - as "floating".
                //
                // It is not re-implemented against the platform here, because that invariant ALREADY
                // HAS AN OWNER that measures it properly: the generator's `SPIRE SEAT` step and the
                // chain's `OWNED_TOWN_SPIRE_RESEAT_OK` marker, which on this very bake read
                // "base y 1.500 vs KeepPlatform top y 1.500, delta 0.0000m". A second copy here would
                // be the duplicated state CLAUDE.md §2/§5/§16 keeps describing - and this time the
                // copy would have been the WRONG one.
                bool standsOnGround = r.Spire == null;
                if (standsOnGround && Mathf.Abs(r.VisualBounds.min.y) > SeatToleranceM)
                    faults.Add($"/Visual minY {r.VisualBounds.min.y:0.##}m is more than {SeatToleranceM:0.0}m off the ground");

                // ── PIN 5 (WO-1817) — the clad renders at the height the HOST was fitted to ──
                // The host's own art is gone (WO-1807 strips it), so "the tower is 4.80 m" is only
                // true of a model nobody can see unless this holds.
                if (r.CadenceH > 0.01f)
                {
                    float off = Mathf.Abs(r.VisualBounds.size.y - r.CadenceH) / r.CadenceH;
                    if (off > CladHeightTolerance)
                        faults.Add($"/Visual renders {r.VisualBounds.size.y:0.##}m but '{r.CatalogId}' authors a " +
                                   $"turret cadence of {r.CadenceH:0.##}m - {off:P0} off (> {CladHeightTolerance:P0}). " +
                                   "The HOST was fitted and the CLAD is what renders; " +
                                   "RaidBaseDresser.FitCladToCadence connects them");
                }
            }

            if (faults.Count > 0) r.Defect = string.Join("; ", faults);
            return r;
        }

        /// <summary>
        /// WO-1821 — name every OTHER renderer standing inside the photographed post's own footprint.
        ///
        /// ⛔ WHY THIS EXISTS: a frame shows a coloured shape; it cannot say what that shape IS. The
        /// 2026-09-17 Iron Bastion frames show bright GREEN bars standing inside the watchtower's
        /// legs, and three plausible culprits were proposed from reading code alone
        /// (`BuildFallbackTurret`'s primitive, a MagentaGuard placeholder, an unstripped host root).
        /// Static reading LOCATES candidates and never CONCLUDES (CLAUDE.md §12) — so this prints the
        /// GameObject name, mesh, material and bounds of whatever is actually standing there, and the
        /// log settles it in one read instead of a cycle of guesses.
        ///
        /// Radius is the subject's own XZ half-extent plus a metre, so it reports things INSIDE or
        /// touching the post, not the whole courtyard.
        /// </summary>
        private static void ReportNeighbours(UnityEngine.SceneManagement.Scene scene, GameObject subject,
                                             StringBuilder log)
        {
            if (!Encapsulate(subject, out Bounds sb)) return;
            float radius = Mathf.Max(sb.size.x, sb.size.z) * 0.5f + 1f;
            var centre = new Vector2(sb.center.x, sb.center.z);

            var hits = new List<string>();
            var roots = scene.GetRootGameObjects();
            for (int r = 0; r < roots.Length; r++)
            {
                var rends = roots[r].GetComponentsInChildren<Renderer>(true);
                for (int i = 0; i < rends.Length; i++)
                {
                    var rend = rends[i];
                    if (rend == null) continue;
                    if (rend.transform.IsChildOf(subject.transform)) continue;   // the post itself
                    var b = rend.bounds;
                    if (Vector2.Distance(new Vector2(b.center.x, b.center.z), centre) > radius) continue;
                    if (b.size.y > 12f) continue;        // walls/ground planes are context, not culprits

                    var mf = rend.GetComponent<MeshFilter>();
                    string mesh = mf != null && mf.sharedMesh != null ? mf.sharedMesh.name : "?";
                    string mat = rend.sharedMaterial != null ? rend.sharedMaterial.name : "?";
                    hits.Add($"'{rend.gameObject.name}' mesh='{mesh}' mat='{mat}' " +
                             $"size={Fmt(b.size)} minY={b.min.y:0.##}");
                    if (hits.Count >= 8) break;
                }
                if (hits.Count >= 8) break;
            }

            if (hits.Count == 0) log.AppendLine("      NEAR   (nothing else stands inside this post)");
            else foreach (string h in hits) log.AppendLine("      NEAR   " + h);
        }

        /// <summary>
        /// WO-1821 — every mesh and material the photographed post renders THROUGH ITSELF.
        ///
        /// <see cref="ReportNeighbours"/> deliberately excludes the post's own hierarchy, so it can
        /// prove nothing foreign is standing inside a post but CANNOT say what the post itself is made
        /// of. That left the "green pill" question at "almost certainly the kit art", which is an
        /// admission of a guess (CLAUDE.md §11B). This closes it: a green MATERIAL listed here, on a
        /// sub-mesh of the clad, settles the colour as authored art in one read.
        /// </summary>
        private static void ReportOwnMeshes(GameObject subject, StringBuilder log)
        {
            var rends = subject.GetComponentsInChildren<Renderer>(true);
            if (rends == null || rends.Length == 0) { log.AppendLine("      OWN    (no renderer)"); return; }

            var seen = new HashSet<string>();
            for (int i = 0; i < rends.Length && seen.Count < 10; i++)
            {
                var rend = rends[i];
                if (rend == null) continue;
                var mf = rend.GetComponent<MeshFilter>();
                string mesh = mf != null && mf.sharedMesh != null ? mf.sharedMesh.name : "?";
                var mats = rend.sharedMaterials;
                string mat = mats != null && mats.Length > 0 && mats[0] != null ? mats[0].name : "?";
                if (mats != null && mats.Length > 1) mat += $" (+{mats.Length - 1} more)";
                string line = $"'{rend.gameObject.name}' mesh='{mesh}' mat='{mat}'";
                if (seen.Add(line)) log.AppendLine("      OWN    " + line);
            }
        }

        /// <summary>
        /// WO-1822 - the scene's own wall ART height, measured off the tallest <c>Clad_Wall_*</c>
        /// renderer. Cached per scene: 60-plus panels x 28 corner posts would otherwise be re-walked
        /// for every post.
        ///
        /// Measured off the BUILT RESULT, not off a kit table, for the same reason the regression
        /// derives its own expectation: a table here would be a second copy of the dresser's
        /// `MeasureTallest(wallModel)` and would drift from it. Reads 4.00 m on hexagon-green and
        /// dungeon-stone, 5.00 m on synty-castle - matching this build's `[wo1817] … wallH=` trace.
        /// </summary>
        private static float SceneWallHeight(GameObject anyPost)
        {
            var scene = anyPost.scene;
            if (_wallHeightScene == scene.path && _wallHeightScene != null) return _wallHeightCache;

            float tallest = 0f;
            var roots = scene.GetRootGameObjects();
            for (int r = 0; r < roots.Length; r++)
            {
                var rends = roots[r].GetComponentsInChildren<Renderer>(true);
                for (int i = 0; i < rends.Length; i++)
                {
                    var rend = rends[i];
                    if (rend == null || rend.gameObject.name == null) continue;
                    if (!rend.gameObject.name.StartsWith("Clad_Wall_", StringComparison.Ordinal)) continue;
                    float h = rend.bounds.size.y;
                    if (h > tallest && h < 12f) tallest = h;   // 12 m guard: a mis-scaled panel is not the wall
                }
            }

            _wallHeightScene = scene.path;
            _wallHeightCache = tallest;
            return tallest;
        }

        private static string _wallHeightScene;
        private static float _wallHeightCache;

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
        /// Photograph one post from HERO EYE HEIGHT, standing off it at ground level - the
        /// angle the owner is looking from when she calls it upside down. A top-down or
        /// orbit shot would hide exactly the stacked-silhouette defect.
        ///
        /// ⛔ WHICH SIDE IS MEASURED, NOT ASSUMED (WO-1817, closing WO-1807 "NOT PROVEN" #3).
        /// This used to stand unconditionally along the post's OUTWARD radial, which puts the
        /// arena's own boundary wall between the camera and every wall-line watchtower. Both
        /// frames in the before set prove it: `RaidBase_fortified_garrison_Watchtower_Archer_0.png`
        /// is 70% stone wall with a roof peeking over it, and
        /// `RaidBase_raider_camp_small_Watchtower_Archer_0.png` is a rock pile - neither shows the
        /// tower, and both scored NON-BLANK, so the instrument reported success while showing
        /// nothing. A frame that proves nothing is worse than no frame; it gets read as a pass.
        ///
        /// So: build BOTH candidate stations (outward and inward), Linecast each back to the
        /// subject against everything, and take the first with a clear line - preferring outward,
        /// which is the player's approach. The chosen side is written into the verdict so the log
        /// says which way the camera was looking. Standoff is derived from the SUBJECT's own size
        /// (a 1.11 m tower framed from a flat 14 m is a speck) with a floor that keeps a large
        /// tower in frame.
        /// </summary>
        private static bool GroundShot(GameObject host, string outPath, out string verdict)
        {
            if (!Encapsulate(host, out Bounds b))
            {
                verdict = "no renderer bounds - nothing to frame";
                return false;
            }

            var flat = new Vector3(host.transform.position.x, 0f, host.transform.position.z);
            var outward = flat.sqrMagnitude > 0.01f ? flat.normalized : Vector3.back;

            // Frame the subject, not a fixed distance: at 55 deg vertical FOV a subject of height h
            // fills the frame at about h / (2 tan(27.5 deg)) = h * 0.96. Times 2.2 leaves it about
            // 45% of frame height with room for its surroundings. The 6 m floor keeps the near clip
            // and the ground plane sane for a sub-metre clad.
            float dist = Mathf.Max(6f, Mathf.Max(b.size.y, b.size.magnitude * 0.5f) * 2.2f);
            float eye = Mathf.Max(1.7f, b.center.y);

            // Edit mode does not step physics, so collider transforms can lag the scene we just
            // opened; without this the linecasts would query stale positions and "prove" a clear
            // line that is not clear.
            Physics.SyncTransforms();

            var tried = new List<string>(2);
            for (int side = 0; side < 2; side++)
            {
                var dir = side == 0 ? outward : -outward;
                string label = side == 0 ? "outward" : "inward";
                var camPos = new Vector3(b.center.x, 0f, b.center.z) + dir * dist + Vector3.up * eye;

                // Anything solid on the line hides the subject. ~AllLayers on purpose: the boundary
                // wall is on Structure, but a rock prop that blanked the camp frame may be on any.
                bool blocked = Physics.Linecast(camPos, b.center, out RaycastHit hit, ~0,
                                                QueryTriggerInteraction.Ignore);
                if (blocked && hit.transform != null && hit.transform.IsChildOf(host.transform))
                    blocked = false;                  // it hit the subject - that is the point

                if (!blocked)
                {
                    var look = Quaternion.LookRotation((b.center - camPos).normalized, Vector3.up);
                    bool ok = RenderTo(outPath, camPos, look, 55f, out verdict);
                    verdict += $" side={label} dist={dist:0.#}m";
                    return ok;
                }
                tried.Add($"{label} blocked by '{(hit.transform != null ? hit.transform.name : "?")}'");
            }

            // Both lines blocked: still SHOOT (from outward), and say so, so the frame is judged as
            // obstructed rather than silently trusted.
            var fallbackPos = new Vector3(b.center.x, 0f, b.center.z) + outward * dist + Vector3.up * eye;
            var fallbackLook = Quaternion.LookRotation((b.center - fallbackPos).normalized, Vector3.up);
            bool shot = RenderTo(outPath, fallbackPos, fallbackLook, 55f, out verdict);
            verdict += "  OBSTRUCTED (" + string.Join("; ", tried) + ")";
            return shot;
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

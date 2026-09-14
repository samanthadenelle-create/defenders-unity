// =============================================================================
// StructureRemovalHuskRegression [removal-husk]
// -----------------------------------------------------------------------------
// Assembly: DeNelle.EditorRegression   Namespace: DeNelle.Editor.Regression
// Markers:  REMOVAL_HUSK_OK / REMOVAL_HUSK_FAIL
// Registered in DataRegression.RunAll.
//
// THE DEFECT THIS ORACLE EXISTS TO KILL (WO-1716, owner-proven in the Editor 2026-09-14).
//
//   `Main_Castle_Overworld` carried a GameObject named `CastleBarracks` whose MeshFilter
//   and MeshRenderer were both gone — invisible in play — while its MeshCollider stayed
//   live and a carving NavMeshObstacle of Size 16.90712 x 5.08288 x 14.51318 punched a
//   huge, wrongly-placed hole in the courtyard navmesh. The owner deleted that one object
//   and re-baked: the oversized square vanished and every remaining structure carved
//   normally. That is the proof, and it cost a whole session of hypotheses to reach,
//   because the thing doing the blocking was the one thing nobody could see.
//
//   Mechanism, read at source: `CastleHubBuilder.SkinHostUpright` destroyed a host's own
//   Renderer + MeshFilter to re-skin it and never touched the MeshCollider shaped by that
//   same mesh; `NavMeshBakeFinal.PrepareBakedTwinsForDynamicCarving` then resolved the twin
//   BY NAME and sized a carving obstacle off that orphaned collider.
//
// THE INVARIANT, and it is one sentence:
//   ⛔ A STRUCTURE MAY NOT BE INVISIBLE AND SOLID AT THE SAME TIME. Removal either
//      destroys the GameObject, or takes its solid collision and its navmesh carve away
//      in the SAME operation as its visual — never one without the other.
//
// WHAT THIS ORACLE ASSERTS — behaviourally, against real GameObjects, not by reading prose
//   1. THE OLD SHAPE IS RECOGNISED. A fixture built to the exact ghost signature (renderer
//      and filter destroyed, MeshCollider + carving NavMeshObstacle left standing) must be
//      FLAGGED by the shared predicate. An oracle that cannot see the historical bug proves
//      nothing about the fix.
//   2. THE PAIR HOLDS, AND IT IS A PAIR. StripHostVisual takes the VISUAL ONLY and must
//      leave collision alone — SkinOptions.Structure sets StripColliders=true ("the host
//      owns its collider", VisualFactory.cs:53,112,368-370), so on a successful re-skin
//      that collider is the new building's body and taking it would let the player walk
//      through every re-skinned structure. EnsureNoHusk is the closing half: with nothing
//      rendering afterwards, the solid MeshCollider and the NavMeshObstacle go, and the
//      predicate clears the object.
//   3. THE CLOSE IS SURGICAL, AND A NO-OP WHEN THE RE-SKIN WORKED. With a rendering child
//      present (the successful path) EnsureNoHusk must change NOTHING — MeshCollider and
//      NavMeshObstacle survive. A BoxCollider survives regardless (it is the
//      Building._blocker seam — Building.EnsureBlocker re-adds one anyway, so destroying
//      it would fight that code every time Configure() runs), and so does a TRIGGER (an
//      NPC interact point is not "solid").
//   4. IT IS IDEMPOTENT. A second strip removes nothing, and a second EnsureNoHusk on an
//      already-cleared host reports no husk.
//   5. THE TWO CALL SITES STILL ROUTE THROUGH IT, source-linted with comments and string
//      literals STRIPPED FIRST (both files discuss this ticket at length in prose; an
//      unstripped match would pass on the comments after the code was gone).
//
// DELIBERATELY NOT ASSERTED HERE: that `Main_Castle_Overworld.unity` contains no husk.
//   The scene still holds the proven `CastleBarracks` ghost until the lead runs
//   Defenders/Castle/Remove invisible structure husks — a scene-content assertion would be
//   RED for a known, ticketed reason and would train the next reader to ignore this marker.
//   The scene repair is a one-time command, never a regression side effect.
//
// Deterministic: fixture GameObjects + editor source reads. No scene load, no PlayMode.
//
// Standalone batch entry:
//   -Method DeNelle.Editor.Regression.StructureRemovalHuskRegression.RunStandalone
// =============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using DeNelle.Core.Diagnostics;
using DeNelle.Village.World;
using UnityEngine;
using UnityEngine.AI;
using Object = UnityEngine.Object;

namespace DeNelle.Editor
{
    public static class StructureRemovalHuskRegression
    {
        private const string FlowSys = "RemovalHusk";

        private const string MarkerOk   = "REMOVAL_HUSK_OK";
        private const string MarkerFail = "REMOVAL_HUSK_FAIL";

        private const string StripSourcePath  = "Assets/_Modules/Village/World/StructureVisualStrip.cs";
        private const string BuilderSourcePath = "Assets/Editor/CastleHubBuilder.cs";
        private const string BakeSourcePath    = "Assets/Editor/NavMeshBakeFinal.cs";

        /// <summary>Standalone batch entry point.</summary>
        public static void RunStandalone()
        {
            string reason;
            bool pass = Run(out reason);
            Debug.Log("[removal-husk] standalone result: " + (pass ? "PASS" : "FAIL") + " - " + reason);
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
                reason = "removal-husk: oracle threw " + ex.GetType().Name + ": " + ex.Message;
                Debug.LogError(MarkerFail + " - " + reason);
                return false;
            }
        }

        // =====================================================================
        //  Body
        // =====================================================================
        private static bool RunCore(out string reason)
        {
            using var _scope = FlowTrace.Enter(FlowSys, "StructureRemovalHuskRegression.RunCore");

            var failures = new List<string>();
            var log = new StringBuilder();
            int cases = 0;

            // ── 1. The historical ghost shape must be RECOGNISED ────────────────
            cases++;
            GameObject ghost = null;
            try
            {
                ghost = BuildStructureFixture("HuskFixture_Ghost", out _);

                // Reproduce the pre-fix strip EXACTLY: renderer + filter gone, everything else stays.
                Object.DestroyImmediate(ghost.GetComponent<MeshRenderer>());
                Object.DestroyImmediate(ghost.GetComponent<MeshFilter>());

                if (!StructureVisualStrip.IsInvisibleNavBlockingHusk(ghost, out string ghostDetail))
                    failures.Add("the shipped ghost signature (renderer + filter destroyed, MeshCollider and " +
                                 "carving NavMeshObstacle still standing) is NOT flagged as a husk - the predicate " +
                                 "cannot see the very object the owner deleted to fix WO-1716.");
                else
                    log.AppendLine("[removal-husk] ghost shape flagged: " + ghostDetail);
            }
            finally
            {
                if (ghost != null) Object.DestroyImmediate(ghost);
            }

            // ── 2. StripHostVisual leaves no husk ───────────────────────────────
            cases++;
            GameObject host = null;
            try
            {
                host = BuildStructureFixture("HuskFixture_Stripped", out _);

                var report = StructureVisualStrip.StripHostVisual(host, "removal-husk oracle");

                if (report.Renderers < 1)   failures.Add("StripHostVisual removed no Renderer from a rendering host.");
                if (report.MeshFilters < 1) failures.Add("StripHostVisual removed no MeshFilter from a rendering host.");

                // THE HALF-OPERATION CONTRACT. StripHostVisual must NOT touch collision on its own:
                // SkinOptions.Structure sets StripColliders=true ("the host owns its collider",
                // VisualFactory.cs:53,112,368-370), so on a SUCCESSFUL re-skin this collider IS the
                // new building's body. Taking it here would let the player walk through every
                // re-skinned structure and send NavMeshBakeFinal down its "NO collider" branch.
                if (report.SolidColliders != 0 || report.NavObstacles != 0)
                    failures.Add("StripHostVisual cleared collision by itself (" + report + ") - it is the " +
                                 "VISUAL half only; clearing the host's collider before the replacement visual " +
                                 "resolves would strip body collision off every successfully re-skinned building.");
                if (host.GetComponent<MeshCollider>() == null)
                    failures.Add("StripHostVisual destroyed the host's MeshCollider - see above; the host owns " +
                                 "its collider across a re-skin.");

                // THE CLOSING HALF: with nothing rendering, EnsureNoHusk takes it.
                if (!StructureVisualStrip.EnsureNoHusk(host, "removal-husk oracle"))
                    failures.Add("EnsureNoHusk did not recognise the stripped host as a husk - the WO-1716 " +
                                 "ghost would survive the pair that is supposed to close it.");

                if (host.GetComponent<Renderer>() != null)     failures.Add("host still has a Renderer after the strip.");
                if (host.GetComponent<MeshFilter>() != null)   failures.Add("host still has a MeshFilter after the strip.");
                if (host.GetComponent<MeshCollider>() != null) failures.Add("host still has a MeshCollider after the strip.");
                if (host.GetComponent<NavMeshObstacle>() != null) failures.Add("host still has a NavMeshObstacle after the strip.");

                if (StructureVisualStrip.IsInvisibleNavBlockingHusk(host, out string stillHusk))
                    failures.Add("after StripHostVisual the host is STILL an invisible-but-solid husk: " + stillHusk);
                else
                    log.AppendLine("[removal-husk] strip result clean: " + report);

                // ── 4. Idempotent ───────────────────────────────────────────────
                cases++;
                var second = StructureVisualStrip.StripHostVisual(host, "removal-husk oracle (second pass)");
                if (second.Total != 0)
                    failures.Add("a second StripHostVisual removed " + second.Total + " more component(s) - " +
                                 "the operation is not idempotent, so a re-run of a scene builder would keep eating it.");
                else if (StructureVisualStrip.EnsureNoHusk(host, "removal-husk oracle (second pass)"))
                    failures.Add("a second EnsureNoHusk still reported a husk on an already-cleared host - " +
                                 "the predicate and the clear disagree, so a scene builder re-run would log " +
                                 "a fresh WO-1716 warning forever.");
                else
                    log.AppendLine("[removal-husk] second pass removed nothing (idempotent).");
            }
            finally
            {
                if (host != null) Object.DestroyImmediate(host);
            }

            // ── 3. The strip is SURGICAL ────────────────────────────────────────
            cases++;
            GameObject surgical = null;
            try
            {
                surgical = BuildStructureFixture("HuskFixture_Surgical", out Mesh mesh);

                surgical.AddComponent<BoxCollider>();                    // the Building._blocker seam
                var npcPoint = surgical.AddComponent<SphereCollider>();  // an NPC interact point
                npcPoint.isTrigger = true;

                var child = new GameObject("Visual_Child");
                child.transform.SetParent(surgical.transform, false);
                var childFilter = child.AddComponent<MeshFilter>();
                childFilter.sharedMesh = mesh;
                child.AddComponent<MeshRenderer>();

                StructureVisualStrip.StripHostVisual(surgical, "removal-husk oracle (surgical)");

                // The successful-re-skin shape: the host's own renderer is gone but the new visual
                // (here, the child) renders. EnsureNoHusk MUST be a no-op, or every re-skinned
                // building loses the body collision SkinOptions.Structure expects it to keep.
                if (StructureVisualStrip.EnsureNoHusk(surgical, "removal-husk oracle (surgical)"))
                    failures.Add("EnsureNoHusk cleared collision on a host whose replacement visual RENDERS - " +
                                 "that is the successful-re-skin path, and stripping its collider there lets the " +
                                 "player walk through the building and sends the bake down its 'NO collider' branch.");
                if (surgical.GetComponent<MeshCollider>() == null)
                    failures.Add("the host's MeshCollider was destroyed while its replacement visual renders - " +
                                 "the host owns its collider across a re-skin (VisualFactory StripColliders=true).");
                if (surgical.GetComponent<NavMeshObstacle>() == null)
                    failures.Add("the host's NavMeshObstacle was destroyed while its replacement visual renders - " +
                                 "a visible building's dynamic carve must survive a re-skin.");

                if (surgical.GetComponent<BoxCollider>() == null)
                    failures.Add("StripHostVisual destroyed the host's BoxCollider - that is Building._blocker " +
                                 "(Building.EnsureBlocker falls back to GetComponent<BoxCollider>() and re-Adds one), " +
                                 "so the strip would fight Configure() on every run.");
                if (surgical.GetComponent<SphereCollider>() == null)
                    failures.Add("StripHostVisual destroyed a TRIGGER collider - an NPC interact point is not solid " +
                                 "geometry and must survive a visual strip.");
                if (child == null || child.GetComponent<MeshRenderer>() == null)
                    failures.Add("StripHostVisual reached into a CHILD object's renderer - it owns the HOST's own " +
                                 "components only; callers destroy whole child objects when they want them gone.");

                if (StructureVisualStrip.IsInvisibleNavBlockingHusk(surgical, out _))
                    failures.Add("an object with a rendering child is reported as an invisible husk - the predicate " +
                                 "must look at the whole subtree, or it will condemn live buildings.");
                else
                    log.AppendLine("[removal-husk] blocker + trigger + rendering child all survived the strip.");
            }
            finally
            {
                if (surgical != null) Object.DestroyImmediate(surgical);
            }

            // ── 5. The call sites still route through the one owner ─────────────
            cases++;
            string stripSrc   = ReadStripped(StripSourcePath, failures);
            string builderSrc = ReadStripped(BuilderSourcePath, failures);
            string bakeSrc    = ReadStripped(BakeSourcePath, failures);

            if (stripSrc != null)
                RequireInSource(stripSrc, StripSourcePath, "IsInvisibleNavBlockingHusk", failures,
                    "the shared predicate is the single owner of the invariant and both other files lean on it");

            if (builderSrc != null)
            {
                RequireInSource(builderSrc, BuilderSourcePath, "StructureVisualStrip.StripHostVisual", failures,
                    "SkinHostUpright must strip through the one owner; a bare DestroyImmediate on the host's " +
                    "Renderer/MeshFilter is how the CastleBarracks ghost was made");
                RequireInSource(builderSrc, BuilderSourcePath, "StructureVisualStrip.EnsureNoHusk", failures,
                    "the strip is half an operation - without the closing call a failed Skin leaves exactly " +
                    "the invisible, solid, carving host this ticket is about");
            }

            if (bakeSrc != null)
                RequireInSource(bakeSrc, BakeSourcePath, "GetComponentsInChildren<Renderer>(true).Length == 0", failures,
                    "PrepareBakedTwinsForDynamicCarving must refuse to size a carve from a twin that renders " +
                    "nothing - that step is what turned the invisible husk into a building-sized hole");

            // ── verdict ─────────────────────────────────────────────────────────
            if (cases < 5)
            {
                reason = "removal-husk: only " + cases + " case(s) ran - an oracle that did not run has not passed.";
                Debug.LogError(MarkerFail + " - " + reason);
                return false;
            }

            if (failures.Count > 0)
            {
                FlowTrace.Fail(FlowSys, "failures=" + failures.Count);
                reason = "removal-husk: " + failures.Count + " failure(s) - " + string.Join(" | ", failures);
                Debug.LogError(MarkerFail + " - " + reason);
                return false;
            }

            Debug.Log(log.ToString().TrimEnd());
            FlowTrace.Step(FlowSys, "removal can no longer leave an invisible, solid, nav-blocking husk (" + cases + " cases).");
            reason = MarkerOk + " " + cases + " case(s): the ghost shape is recognised, StripHostVisual clears " +
                     "renderer+filter+solid mesh collider+nav obstacle together, the blocker/trigger/child survive, " +
                     "it is idempotent, and both call sites route through the one owner.";
            Debug.Log(reason);
            return true;
        }

        // =====================================================================
        //  Fixtures + helpers
        // =====================================================================

        /// <summary>A structure host in the exact shape the hub authors one: rendered mesh, a
        /// MeshCollider shaped by that same mesh, and a carving NavMeshObstacle over it.</summary>
        private static GameObject BuildStructureFixture(string name, out Mesh mesh)
        {
            var primitive = GameObject.CreatePrimitive(PrimitiveType.Cube);
            mesh = primitive.GetComponent<MeshFilter>().sharedMesh;
            Object.DestroyImmediate(primitive);

            var go = new GameObject(name);
            go.hideFlags = HideFlags.HideAndDontSave;

            var filter = go.AddComponent<MeshFilter>();
            filter.sharedMesh = mesh;
            go.AddComponent<MeshRenderer>();

            var collider = go.AddComponent<MeshCollider>();
            collider.sharedMesh = mesh;

            var obstacle = go.AddComponent<NavMeshObstacle>();
            obstacle.shape = NavMeshObstacleShape.Box;
            obstacle.carving = true;
            obstacle.carveOnlyStationary = true;
            obstacle.size = new Vector3(16.90712f, 5.08288f, 14.51318f);   // the owner's captured Size

            return go;
        }

        /// <summary>Read a source file with comments and string literals stripped. Adds a failure
        /// (never a silent pass) when the file is missing or unreadable.</summary>
        private static string ReadStripped(string path, List<string> failures)
        {
            if (!File.Exists(path))
            {
                failures.Add("source file ABSENT: " + path + " - the lint cannot run, so it has not passed.");
                return null;
            }

            string src = null;
            Guard.Try(FlowSys, "read " + path, () => { src = File.ReadAllText(path); });
            if (string.IsNullOrEmpty(src))
            {
                failures.Add("source file unreadable or empty: " + path);
                return null;
            }
            return StripCommentsAndStrings(src);
        }

        private static void RequireInSource(string stripped, string path, string token,
                                            List<string> failures, string why)
        {
            if (stripped.IndexOf(token, StringComparison.Ordinal) >= 0) return;
            FlowTrace.Fail(FlowSys, "source lint: '" + token + "' missing from " + path);
            failures.Add(path + " no longer contains '" + token + "' in CODE (comments and string literals " +
                         "stripped) - " + why + ".");
        }

        /// <summary>Blank out // and /* */ comments and the contents of string/char literals, so a
        /// lint matches CODE and never the prose that discusses it.</summary>
        private static string StripCommentsAndStrings(string src)
        {
            var sb = new StringBuilder(src.Length);
            int n = src.Length;
            for (int i = 0; i < n; i++)
            {
                char c = src[i];
                if (c == '/' && i + 1 < n && src[i + 1] == '/')
                {
                    while (i < n && src[i] != '\n') i++;
                    sb.Append('\n');
                    continue;
                }
                if (c == '/' && i + 1 < n && src[i + 1] == '*')
                {
                    i += 2;
                    while (i + 1 < n && !(src[i] == '*' && src[i + 1] == '/')) i++;
                    i++;
                    sb.Append(' ');
                    continue;
                }
                if (c == '"' || c == '\'')
                {
                    char quote = c;
                    i++;
                    while (i < n && src[i] != quote)
                    {
                        if (src[i] == '\\') i++;
                        i++;
                    }
                    sb.Append(' ');
                    continue;
                }
                sb.Append(c);
            }
            return sb.ToString();
        }
    }
}

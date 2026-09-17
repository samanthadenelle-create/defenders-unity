// =============================================================================
// RaidBaseMatDiag — WO-838 PHASE A probe. DATA, not guesses (CLAUDE.md §12).
// -----------------------------------------------------------------------------
// WHY THIS EXISTS: WO-838's RCA proves from disk that the raid-base wall FBXes
// (Assets/Resources/Walls/{wood,iron,steel}_wall.fbx) import with
// `materialImportMode: 2` + `externalObjects: {}` — i.e. FBX-EMBEDDED materials
// whose textures bind by ABSOLUTE PATH on another machine
// (C:\Users\Kayden-Laptop\...\steel_wall.fbm\...), a folder that does not exist
// in this repo. The INFERRED half of that RCA is the one thing disk cannot show:
// what the IMPORTED material actually looks like at runtime (shader name, is
// _BaseMap null, what is _BaseColor). This probe captures exactly that, so the
// Phase B material fix is earned by data instead of by a plausible theory.
//
// It is deliberately READ-ONLY. It opens scenes, dumps, and never writes an asset.
//
// EXPECTED PRE-FIX PROOF LINE (WO-838 acceptance #1):
//   the 86 steel_wall renderers in RaidBase_mage_enclave sitting on an embedded
//   lit material with baseMap=<null>.
// IF THE PROBE INSTEAD SHOWS A BOUND TEXTURE: STOP. The RCA's inferred link is
// wrong and the ticket must be re-triaged before any material edit lands.
//
// Headless:
//   powershell tools\run-unity-method.ps1 -Method DeNelle.Editor.RaidBaseMatDiag.Run `
//             -LogName raidbase-matdiag.log
// Menu: Defenders/Art/Diag Raid Base Materials
//
// JUDGE BY THE MARKER ON A FRESH LOG, NEVER THE EXIT CODE (§8; memory
// `gates-report-success-without-proving-it`):  RAIDBASE_MATDIAG_OK <scenes>
// =============================================================================
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DeNelle.Editor
{
    public static class RaidBaseMatDiag
    {
        // Same list RaidNavBake bakes — kept in step with it deliberately (one set of
        // raid scenes, two consumers). IronBastion is included even though it is not
        // registered in Build Settings: its art can drift exactly the same way, and a
        // probe that skipped it would go quiet the day the owner registers it.
        private static readonly string[] RaidScenes =
        {
            "Assets/Scenes/RaidBase_raider_camp_small.unity",
            "Assets/Scenes/RaidBase_fortified_garrison.unity",
            "Assets/Scenes/RaidBase_mage_enclave.unity",
            "Assets/Scenes/RaidBase_IronBastion.unity",
        };

        // Albedo slots worth probing, in the order URP/Lit, Shader Graph and the
        // legacy/Standard family expose them. First non-null wins.
        private static readonly string[] AlbedoProps =
        {
            "_BaseMap", "_MainTex", "_BaseColorMap", "_Base_Color", "_Albedo", "_AlbedoMap",
        };

        [MenuItem("Defenders/Art/Diag Raid Base Materials")]
        public static void Run()
        {
            var sb = new StringBuilder();
            sb.AppendLine("[Flow:RaidBaseMatDiag] ===== RAID BASE MATERIAL SURVIVABILITY — MEASURED =====");

            int scenesRead = 0;

            foreach (var scenePath in RaidScenes)
            {
                if (!System.IO.File.Exists(scenePath))
                {
                    sb.AppendLine($"[Flow:RaidBaseMatDiag] WARN scene ABSENT on disk: {scenePath} — skipped.");
                    continue;
                }

                Scene scene;
                try
                {
                    scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
                }
                catch (System.Exception e)
                {
                    sb.AppendLine($"[Flow:RaidBaseMatDiag] FAIL could not open {scenePath}: {e.Message}");
                    continue;
                }

                scenesRead++;
                sb.AppendLine($"[Flow:RaidBaseMatDiag] --- scene '{scene.name}' ---");

                // Per-material rollup so 86 identical wall slabs read as ONE line with a
                // count, not 86 lines that bury the finding (the raid scenes carry ~100+
                // renderers each; an unaggregated dump is unreadable and gets skimmed).
                var rollup = new Dictionary<string, MatFacts>();
                int renderers = 0, nullMats = 0;

                foreach (var root in scene.GetRootGameObjects())
                {
                    if (root == null) continue;
                    foreach (var r in root.GetComponentsInChildren<Renderer>(true))
                    {
                        if (r == null) continue;
                        renderers++;
                        var mats = r.sharedMaterials;
                        if (mats == null || mats.Length == 0) { nullMats++; continue; }

                        foreach (var m in mats)
                        {
                            if (m == null) { nullMats++; continue; }
                            var facts = Describe(m);
                            if (!rollup.TryGetValue(facts.Key, out var acc)) { acc = facts; }
                            acc.Count++;
                            if (acc.Example == null) acc.Example = Path(r.transform);
                            rollup[facts.Key] = acc;
                        }
                    }
                }

                sb.AppendLine($"[Flow:RaidBaseMatDiag] renderers={renderers} nullMaterialSlots={nullMats} " +
                              $"distinctMaterials={rollup.Count}");

                foreach (var kv in rollup)
                {
                    var f = kv.Value;
                    // THE PROOF LINE. Everything a Phase B decision needs is on it:
                    // who owns the material (an FBX = embedded, a .mat = tracked), the
                    // shader, whether an albedo actually resolved, and the base colour.
                    sb.AppendLine(
                        $"[Flow:RaidBaseMatDiag]   x{f.Count,-4} mat='{f.MatName}' shader='{f.Shader}' " +
                        $"albedoProp={f.AlbedoProp} albedoTex={f.AlbedoTex} baseColor={f.BaseColor} " +
                        $"source='{f.SourceAsset}' embedded={f.Embedded} example='{f.Example}'");

                    if (f.Embedded && f.AlbedoTex == "<null>")
                        sb.AppendLine($"[Flow:RaidBaseMatDiag]   ^^ WHITE-SLAB CLASS: FBX-embedded material with NO albedo " +
                                      "— textures did not survive import on this machine (WO-838 Finding 1).");
                    if (IsBrokenShaderName(f.Shader))
                        sb.AppendLine($"[Flow:RaidBaseMatDiag]   ^^ MAGENTA CLASS: '{f.Shader}' is not a URP shader " +
                                      "— renders magenta under URP unless MagentaGuard recovers it (WO-838 Findings 2/3).");
                }
            }

            sb.AppendLine($"[Flow:RaidBaseMatDiag] RAIDBASE_MATDIAG_OK {scenesRead}/{RaidScenes.Length} scenes");
            Debug.Log(sb.ToString());
        }

        // =====================================================================
        // WO-1747 §H — PER-RENDERER probe. `Run()`'s rollup (above) aggregates by
        // material identity and keeps only a COUNT + the FIRST example path — it
        // cannot say whether a given host (e.g. Watchtower_Archer_3) carries a
        // live renderer on the host transform itself CO-LOCATED with a live
        // renderer on a dressed child (RaidBaseDresser.ReplaceChildrenWith only
        // destroys children, never a renderer on the host — RaidBaseDresser.cs:
        // 1275-1291). This is a SEPARATE, unaggregated entry point so `Run()`'s
        // job (1237 renderers rolled into ~12 readable lines) stays intact.
        //
        // Read-only: opens scenes, dumps, never saves/marks dirty. Same contract
        // as Run().
        //
        // Headless:
        //   powershell tools\run-unity-method.ps1 -Method DeNelle.Editor.RaidBaseMatDiag.RunPerRenderer `
        //             -LogName raidbase-rendererdiag.log
        // Menu: Defenders/Art/Diag Raid Tower Renderers
        //
        // JUDGE BY THE MARKER ON A FRESH LOG, NEVER THE EXIT CODE:
        //   RAIDBASE_RENDERERDIAG_OK <scenes>
        // =====================================================================
        [MenuItem("Defenders/Art/Diag Raid Tower Renderers")]
        public static void RunPerRenderer()
        {
            var sb = new StringBuilder();
            sb.AppendLine("[Flow:RaidBaseMatDiag] ===== PER-RENDERER TOWER/CORNERPOST/SPIRE PROBE — MEASURED =====");

            int scenesRead = 0;

            foreach (var scenePath in RaidScenes)
            {
                if (!System.IO.File.Exists(scenePath))
                {
                    sb.AppendLine($"[Flow:RaidBaseMatDiag] WARN scene ABSENT on disk: {scenePath} — skipped.");
                    continue;
                }

                Scene scene;
                try
                {
                    scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
                }
                catch (System.Exception e)
                {
                    sb.AppendLine($"[Flow:RaidBaseMatDiag] FAIL could not open {scenePath}: {e.Message}");
                    continue;
                }

                scenesRead++;
                sb.AppendLine($"[Flow:RaidBaseMatDiag] --- scene '{scene.name}' ---");

                // The dresser's three ReplaceChildrenWith targets (RaidBaseDresser.cs:1205,
                // :1208-1210) — the only hosts that can carry a stale co-located inner mesh.
                var hosts = new List<Transform>();
                foreach (var root in scene.GetRootGameObjects())
                {
                    if (root == null) continue;
                    foreach (var t in root.GetComponentsInChildren<Transform>(true))
                    {
                        if (t == null) continue;
                        if (t.name.StartsWith("Watchtower_", System.StringComparison.Ordinal)
                            || t.name.StartsWith("CornerPost_", System.StringComparison.Ordinal)
                            || t.name == "RaidSpire")
                        {
                            hosts.Add(t);
                        }
                    }
                }

                foreach (var host in hosts)
                {
                    var hostRenderers = host.GetComponentsInChildren<Renderer>(true);
                    sb.AppendLine($"[Flow:RaidBaseMatDiag] HOST '{host.name}' childCount={host.childCount} " +
                                  $"rendererCount={hostRenderers.Length}");

                    bool hostHasLiveRenderer = false, childHasLiveRenderer = false;

                    foreach (var r in hostRenderers)
                    {
                        if (r == null) continue;

                        bool isOnHostItself = r.transform == host;
                        string relPath = RelativeToHost(host, r.transform);
                        bool live = r.enabled && r.gameObject.activeInHierarchy;
                        if (isOnHostItself && live) hostHasLiveRenderer = true;
                        if (!isOnHostItself && live) childHasLiveRenderer = true;

                        var mats = r.sharedMaterials;
                        bool haveFacts = mats != null && mats.Length > 0 && mats[0] != null;
                        var f = haveFacts ? Describe(mats[0]) : default;

                        sb.AppendLine(
                            $"[Flow:RaidBaseMatDiag]   RENDERER path='{relPath}' type={r.GetType().Name} " +
                            $"enabled={r.enabled} activeInHierarchy={r.gameObject.activeInHierarchy} " +
                            $"shader='{(haveFacts ? f.Shader : "<no material>")}' " +
                            $"albedoProp={(haveFacts ? f.AlbedoProp : "<none>")} " +
                            $"albedoTex={(haveFacts ? f.AlbedoTex : "<null>")} " +
                            $"baseColor={(haveFacts ? f.BaseColor : "<no _BaseColor/_Color>")} " +
                            $"boundsSize={r.bounds.size} " +
                            $"sharedMaterial='{(haveFacts ? f.SourceAsset : "<no material>")}'");
                    }

                    sb.AppendLine(
                        $"[Flow:RaidBaseMatDiag]   TWO_LIVE_RENDERERS host='{host.name}' " +
                        $"hostRenderer={hostHasLiveRenderer} childRenderer={childHasLiveRenderer}");
                }
            }

            sb.AppendLine($"[Flow:RaidBaseMatDiag] RAIDBASE_RENDERERDIAG_OK {scenesRead}/{RaidScenes.Length} scenes");
            Debug.Log(sb.ToString());
        }

        // Path of `t` relative to `host` — "<host>" for the host transform itself,
        // "<host>/Visual" for a direct child, etc. Distinguishes a renderer sitting
        // ON the host from one on a dressed child, which the aggregate rollup key
        // (source|name|shader|albedoTex) cannot do.
        private static string RelativeToHost(Transform host, Transform t)
        {
            if (t == host) return "<host>";
            var sb = new StringBuilder(t.name);
            var p = t.parent;
            while (p != null && p != host)
            {
                sb.Insert(0, p.name + "/");
                p = p.parent;
            }
            sb.Insert(0, "<host>/");
            return sb.ToString();
        }

        private struct MatFacts
        {
            public string Key, MatName, Shader, AlbedoProp, AlbedoTex, BaseColor, SourceAsset, Example;
            public bool Embedded;
            public int Count;
        }

        private static MatFacts Describe(Material m)
        {
            var f = new MatFacts
            {
                MatName = m.name,
                Shader = m.shader != null ? m.shader.name : "<null shader>",
                AlbedoProp = "<none>",
                AlbedoTex = "<null>",
                BaseColor = "<no _BaseColor/_Color>",
                SourceAsset = "<not an asset>",
            };

            foreach (var prop in AlbedoProps)
            {
                if (!m.HasProperty(prop)) continue;
                f.AlbedoProp = prop;
                var t = m.GetTexture(prop);
                if (t != null) { f.AlbedoTex = t.name; break; }
            }

            if (m.HasProperty("_BaseColor")) f.BaseColor = m.GetColor("_BaseColor").ToString();
            else if (m.HasProperty("_Color")) f.BaseColor = m.GetColor("_Color").ToString();

            string path = AssetDatabase.GetAssetPath(m);
            if (!string.IsNullOrEmpty(path))
            {
                f.SourceAsset = path;
                // An FBX/OBJ that OWNS a Material sub-asset is an importer-embedded
                // material: no tracked .mat exists, so nothing about it survives a
                // re-import on another machine. That is the whole WO-838 wall defect.
                f.Embedded = path.EndsWith(".fbx", System.StringComparison.OrdinalIgnoreCase)
                          || path.EndsWith(".obj", System.StringComparison.OrdinalIgnoreCase)
                          || path.EndsWith(".blend", System.StringComparison.OrdinalIgnoreCase);
            }

            f.Key = f.SourceAsset + "|" + f.MatName + "|" + f.Shader + "|" + f.AlbedoTex;
            return f;
        }

        // Mirrors the SPIRIT of MagentaGuard.IsBrokenShader for reporting only.
        // ⚠ This is a REPORT LABEL, not a second predicate: nothing here decides a
        // recovery, and no runtime path calls it. MagentaGuard.IsBrokenShader stays
        // the ONE broken-shader authority (CLAUDE.md — do not add a second predicate).
        private static bool IsBrokenShaderName(string shader)
        {
            if (string.IsNullOrEmpty(shader)) return true;
            return shader == "Standard"
                || shader.StartsWith("Legacy Shaders/", System.StringComparison.Ordinal)
                || shader.IndexOf("InternalError", System.StringComparison.OrdinalIgnoreCase) >= 0
                || shader == "Hidden/InternalErrorShader";
        }

        private static string Path(Transform t)
        {
            var sb = new StringBuilder(t.name);
            var p = t.parent;
            while (p != null) { sb.Insert(0, p.name + "/"); p = p.parent; }
            return sb.ToString();
        }
    }
}

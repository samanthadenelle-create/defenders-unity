// =============================================================================
// RaidUntexturedCensus — WO-1751 (A). Name the "giant untextured grey box".
// -----------------------------------------------------------------------------
// THE DEFECT (owner felt-test 2026-09-15, Seeker, tester build 2026.09.15.371127,
// scene RaidBase_IronBastion, screenshot Screenshot_20260915-135355.png): a flat,
// untextured light-grey box roughly wall-height and ~6 m long stands in the
// courtyard just inside the outer ring, with a troop clipping through it.
//
// WHY NOTHING IN THE LOG NAMED IT, AND WHY THIS FILE EXISTS AT ALL
// -----------------------------------------------------------------------------
// The project already owns TWO detectors for a badly-materialled renderer, and
// NEITHER of them can see this object. That gap — not the box itself — is the
// finding:
//
//   1. MagentaGuard (Assets/_Modules/Core/MagentaGuard.cs) hunts MAGENTA. Its
//      SweepRenderers only acts when `anyOffender` is true, and `anyOffender`
//      requires a NULL material slot or IsBrokenShader(m.shader)
//      (MagentaGuard.cs:576-580). Its stray-primitive HIDE is additionally gated
//      on `brokenMat != null` (MagentaGuard.cs:598). A material on a perfectly
//      VALID `Universal Render Pipeline/Lit` shader with NO albedo bound and a
//      white tint is therefore skipped ENTIRELY — and that combination renders
//      exactly as the owner's screenshot: flat, light grey, no texture.
//      (Secondary: MagentaGuard.ProtectPrimitiveArt stores editor-session
//      InstanceIDs. RaidBaseDresser registers KeepPlatform/KeepRamp at BAKE time,
//      so that static set is EMPTY in the player — bake-time registration
//      protects nothing at runtime.)
//
//   2. TripoMaterialFixer.VerifyAllRenderersUrp (TripoMaterialFixer.cs:497-575)
//      DOES catch exactly this class — it is the WO-1707 "flat-white-structure"
//      detector, and on the SAME device session it named the town's Jeweler:
//        [Flow:TripoMatFix] NO ALBEDO on 'Jeweler' renderer 'Rim' slot 0 ...
//        tint=(1.00,1.00,1.00) ... this is the flat-white-structure symptom.
//      (break-log.jsonl, 2026-09-15T18:55:30Z, scene Main_Castle_Overworld.)
//      But that verify is a PER-OBJECT MonoBehaviour: it only inspects the
//      subtree of a GameObject somebody attached a TripoMaterialFixer to. The
//      RaidBase_* scenes are BAKED geometry that nothing attaches one to, so in
//      the three raid loads in that same log (18:39, 18:44, 18:51 UTC) there is
//      NOT ONE [Flow:TripoMatFix] line. The one instrument that would have named
//      the box is simply not armed in the one scene that needs it.
//
// WHAT THIS DOES: a scene-wide, READ-ONLY census over raid scenes using the SAME
// criterion TripoMaterialFixer already proved on device (shader class +
// DependencyClosureTrace.GetAlbedo + tint), reported BIGGEST-FIRST by renderer
// bounds so the ~6 m offender is line one instead of line four hundred.
//
// ⛔ IT NEVER MODIFIES ANYTHING. No material swap, no renderer disable, no
// re-skin. Recovery belongs to MagentaGuard / TripoMaterialFixer at source; a
// second authority that also "fixes" is how MagentaGuard came to hide the
// dungeon portal (WO-869). This file produces EVIDENCE, nothing else.
//
// §12 discipline: the per-offender lines are Fail (a Warn never reaches
// break-log.jsonl / the F8 device bridge — both are error-level only; that is
// the WO-1707 lesson written into TripoMaterialFixer.cs:530-539), they are CAPPED
// so a bad scene cannot flood the logcat ring (memory
// `logcat-ring-buffer-destroys-evidence`), and the census runs a small BOUNDED
// ladder rather than every frame.
// =============================================================================

using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using DeNelle.Core;

namespace DeNelle.Core.Diagnostics
{
    public static class RaidUntexturedCensus
    {
        private const string Sys = "RaidArt";

        /// <summary>Most offenders printed individually per pass. Biggest-first, so the cap
        /// drops the noise and keeps the thing the owner can actually see.</summary>
        private const int MaxReportedPerPass = 12;

        /// <summary>A white-ish tint is what makes an unmapped URP material read as the flat
        /// grey slab. A deliberately DARK miss-tint is a designed degrade, not this defect —
        /// same distinction TripoMaterialFixer draws at :545.</summary>
        private const float FlatTintLuminanceFloor = 0.6f;

        /// <summary>
        /// Mirrors MagentaGuard.DeferredSweepDelays (3 s, 8 s) and then DELIBERATELY RUNS LONGER.
        /// <para>
        /// ⛔ 8 SECONDS IS NOT ENOUGH AND THE DEVICE LOG PROVES IT. In the raid load that the
        /// owner's 13:53:55 screenshot belongs to, the scene loaded at 18:51:54Z and the first
        /// untextured-art error arrived at <b>18:52:08Z — 14 s later</b>, from
        /// <c>RaidDeployController.DeployAll → TroopDeployer.SpawnFromArmy → TroopFactory.Build</c>
        /// (break-log.jsonl). Troop deployment is PLAYER-TRIGGERED, so the objects most likely to
        /// be the grey box do not exist during MagentaGuard's whole ladder. A census that stopped
        /// at 8 s would have been structurally blind to exactly the thing it was written to name —
        /// the same snapshot blindness MagentaGuard.cs:52-66 documents, one rung further out.
        /// </para>
        /// <para>
        /// Each pass is one <c>FindObjectsByType</c> over the scene's renderers, then it stops
        /// forever for that load. <see cref="RunPass"/> is public so a later change can hook the
        /// deploy seam directly if even this ladder misses.
        /// </para>
        /// </summary>
        private static readonly float[] DeferredPassDelays = { 3f, 8f, 20f, 45f, 90f };

        private static bool _hooked;
        private static GameObject _driver;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Arm()
        {
            if (_hooked) return;
            _hooked = true;
            try
            {
                SceneManager.sceneLoaded += OnSceneLoaded;
                OnSceneLoaded(SceneManager.GetActiveScene(), LoadSceneMode.Single);
            }
            catch (System.Exception e)
            {
                FlowTrace.Warn(Sys, "RaidUntexturedCensus.Arm threw " + e.GetType().Name + ": " + e.Message);
            }
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            try
            {
                if (!HubScenes.IsRaid(scene.name)) return;
                RunPass(scene.name, "sceneLoaded");
                EnsureDriver().StartCoroutine(DeferredPasses(scene.name));
            }
            catch (System.Exception e)
            {
                FlowTrace.Warn(Sys, "RaidUntexturedCensus.OnSceneLoaded threw " + e.GetType().Name + ": " + e.Message);
            }
        }

        private static MonoBehaviour EnsureDriver()
        {
            if (_driver == null)
            {
                _driver = new GameObject("RaidUntexturedCensusDriver");
                _driver.hideFlags = HideFlags.HideAndDontSave;
                Object.DontDestroyOnLoad(_driver);
            }
            var runner = _driver.GetComponent<CensusDriver>();
            if (runner == null) runner = _driver.AddComponent<CensusDriver>();
            return runner;
        }

        private static IEnumerator DeferredPasses(string sceneName)
        {
            float waited = 0f;
            for (int i = 0; i < DeferredPassDelays.Length; i++)
            {
                float delay = DeferredPassDelays[i] - waited;
                if (delay > 0f) yield return new WaitForSeconds(delay);
                waited = DeferredPassDelays[i];
                if (!HubScenes.IsRaid(SceneManager.GetActiveScene().name)) yield break;
                RunPass(sceneName, "deferred+" + DeferredPassDelays[i].ToString("0.#") + "s");
            }
        }

        /// <summary>
        /// ONE read-only census pass. Public so a headless proof / regression harness can call it
        /// without waiting on the scene-load ladder.
        /// </summary>
        public static void RunPass(string sceneName, string why)
        {
            var offenders = new List<Offender>();
            int renderersScanned = 0, slotsScanned = 0;

            try
            {
                var rends = Object.FindObjectsByType<Renderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
                if (rends == null) return;

                for (int ri = 0; ri < rends.Length; ri++)
                {
                    var r = rends[ri];
                    if (r == null || !r.enabled) continue;
                    // Particle / trail / UI renderers carry deliberate empty slots (the vendor
                    // convention MagentaGuard.IsVendorParticleNullSlot exists for) and never look
                    // like a grey slab. Only MESH geometry can be the box.
                    if (!(r is MeshRenderer || r is SkinnedMeshRenderer)) continue;

                    renderersScanned++;
                    var mats = r.sharedMaterials;
                    if (mats == null) continue;

                    for (int mi = 0; mi < mats.Length; mi++)
                    {
                        slotsScanned++;
                        string verdict = ClassifySlot(mats[mi]);
                        if (verdict == null) continue;
                        offenders.Add(BuildOffender(r, mats[mi], mi, verdict));
                    }
                }
            }
            catch (System.Exception e)
            {
                FlowTrace.Warn(Sys, "RaidUntexturedCensus.RunPass scan threw " + e.GetType().Name + ": " + e.Message);
                return;
            }

            Report(sceneName, why, renderersScanned, slotsScanned, offenders);
        }

        /// <summary>
        /// Null when the slot is fine; otherwise a short reason string. Deliberately the SAME
        /// three-way split TripoMaterialFixer.VerifyAllRenderersUrp uses (TripoMaterialFixer.cs:
        /// 507-520 for the shader classes, :530 for the albedo test) so the two instruments can
        /// never disagree about what "untextured" means.
        /// </summary>
        private static string ClassifySlot(Material m)
        {
            if (m == null) return "NULL material slot";

            var sh = m.shader;
            string sn = sh != null ? sh.name : null;
            if (string.IsNullOrEmpty(sn)) return "NULL shader";
            if (MagentaGuard.IsBrokenShader(sh)) return "error shader";
            if (!sh.isSupported) return "shader NOT SUPPORTED on this device";

            bool isUrp = sn.StartsWith("Universal Render Pipeline/") || sn.StartsWith("URP/");
            if (!isUrp) return "non-URP shader (renders as the URP fallback)";

            // THE CASE THIS FILE WAS WRITTEN FOR: a valid URP shader, no albedo bound, and a tint
            // light enough to read as flat grey/white. MagentaGuard skips it; nothing else in a
            // raid scene looks.
            if (DependencyClosureTrace.GetAlbedo(m) != null) return null;

            Color tint = TintOf(m);
            float lum = 0.299f * tint.r + 0.587f * tint.g + 0.114f * tint.b;
            if (lum < FlatTintLuminanceFloor) return null;   // a dark miss-tint is a designed degrade

            return "URP shader with NO ALBEDO bound and a light tint (flat grey slab)";
        }

        private static Color TintOf(Material m)
        {
            if (m == null) return Color.white;
            if (m.HasProperty("_BaseColor")) return m.GetColor("_BaseColor");
            if (m.HasProperty("_Color")) return m.GetColor("_Color");
            return Color.white;
        }

        private struct Offender
        {
            public string Path;
            public string Mesh;
            public string MaterialName;
            public string Shader;
            public string Verdict;
            public string AlbedoSlots;
            public Color Tint;
            public Vector3 Size;
            public Vector3 Center;
            public int Slot;
            public int Layer;
            public float Volume;
        }

        private static Offender BuildOffender(Renderer r, Material m, int slot, string verdict)
        {
            Bounds b = r.bounds;
            var o = new Offender
            {
                Path = HierarchyPath(r.transform),
                Mesh = MeshName(r),
                MaterialName = m == null || string.IsNullOrEmpty(m.name) ? "<none>" : m.name,
                Shader = m != null && m.shader != null ? m.shader.name : "<null>",
                Verdict = verdict,
                AlbedoSlots = SafeDescribeAlbedo(m),
                Tint = TintOf(m),
                Size = b.size,
                Center = b.center,
                Slot = slot,
                Layer = r.gameObject.layer,
                Volume = Mathf.Abs(b.size.x * b.size.y * b.size.z)
            };
            return o;
        }

        private static string SafeDescribeAlbedo(Material m)
        {
            if (m == null) return "<no material>";
            try { return DependencyClosureTrace.DescribeAlbedo(m); }
            catch (System.Exception e) { return "<DescribeAlbedo threw " + e.GetType().Name + ">"; }
        }

        private static void Report(string sceneName, string why, int renderersScanned, int slotsScanned,
                                   List<Offender> offenders)
        {
            if (offenders.Count == 0)
            {
                FlowTrace.Step(Sys, "UNTEXTURED CENSUS CLEAN (" + why + ") scene='" + sceneName
                    + "' renderers=" + renderersScanned + " slots=" + slotsScanned
                    + " - zero null/error/unsupported/no-albedo slots. The grey box, if present, is NOT a "
                    + "material defect on an active mesh renderer.");
                return;
            }

            // BIGGEST FIRST. The owner's report is about a thing she can SEE; a 6 m slab must not
            // be buried under four hundred one-slot props.
            offenders.Sort((a, b) => b.Volume.CompareTo(a.Volume));

            FlowTrace.Fail(Sys, "UNTEXTURED CENSUS (" + why + ") scene='" + sceneName + "': "
                + offenders.Count + " offending slot(s) across " + renderersScanned
                + " mesh renderer(s) / " + slotsScanned + " slot(s). Listing the "
                + Mathf.Min(offenders.Count, MaxReportedPerPass)
                + " LARGEST by renderer bounds - the first line is the best candidate for a "
                + "player-visible grey box. Read-only census; nothing was modified.");

            int n = Mathf.Min(offenders.Count, MaxReportedPerPass);
            for (int i = 0; i < n; i++)
            {
                var o = offenders[i];
                string size = o.Size.x.ToString("0.0") + "x" + o.Size.y.ToString("0.0") + "x" + o.Size.z.ToString("0.0");
                string pos = o.Center.x.ToString("0.0") + "," + o.Center.y.ToString("0.0") + "," + o.Center.z.ToString("0.0");
                string tint = o.Tint.r.ToString("0.00") + "," + o.Tint.g.ToString("0.00") + "," + o.Tint.b.ToString("0.00");
                FlowTrace.Fail(Sys, "  #" + (i + 1) + " " + o.Verdict
                    + " | path='" + o.Path + "' mesh='" + o.Mesh + "' layer=" + o.Layer
                    + " slot=" + o.Slot + " material='" + o.MaterialName + "' shader='" + o.Shader + "'"
                    + " tint=(" + tint + ") bounds=" + size + "m at (" + pos + ")"
                    + " albedoSlots=[" + o.AlbedoSlots + "]");
            }

            if (offenders.Count > n)
                FlowTrace.Step(Sys, "  ... " + (offenders.Count - n) + " further offending slot(s) not listed "
                    + "(cap " + MaxReportedPerPass + " per pass, biggest-first). The cap is deliberate: an "
                    + "unbounded list evicts the boot window out of the device logcat ring.");
        }

        private static string MeshName(Renderer r)
        {
            var smr = r as SkinnedMeshRenderer;
            if (smr != null && smr.sharedMesh != null) return smr.sharedMesh.name;
            var mf = r.GetComponent<MeshFilter>();
            return mf != null && mf.sharedMesh != null ? mf.sharedMesh.name : "<none>";
        }

        private static string HierarchyPath(Transform t)
        {
            var sb = new System.Text.StringBuilder(t.name);
            for (var p = t.parent; p != null; p = p.parent) sb.Insert(0, p.name + "/");
            return sb.ToString();
        }

        /// <summary>Coroutine host only — holds no state and makes no decisions.</summary>
        private sealed class CensusDriver : MonoBehaviour { }
    }
}

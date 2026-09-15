// =============================================================================
// BreachOrderMarker — "the warband is hitting THIS section" (WO-1723 Lane B, Q2).
// -----------------------------------------------------------------------------
// Assembly: DeNelle.Village   Namespace: DeNelle.Village
//
// OWNER RULING Q2, 2026-09-14 (WO-1723 §7): Breach mode STAYS ARMED after a
// successful order — `HandleBreachTap` keeps its current behaviour — and the
// currently-ordered panel gets a clear visual highlight, so the player can see
// which section the warband is actually on and that a re-tap MOVED it.
// The highlight is the fix for the silent re-point, not a behaviour change to the
// order. (The capture showed the order move twice inside ~1 s with nothing on
// screen to say so: v=3 SS_17 -> v=4 SS_16, v=6 SW_11 -> v=7 SW_13.)
//
// ⛔ COLOUR IS NEVER THE ONLY CARRIER OF MEANING.
// The owner is red/green colourblind (SAMANTHA.md rule 8; memory
// `owner-colorblind-delegate-visual-creative`). This marker carries its meaning in
// SHAPE and MOTION first: a ground footprint band plus FOUR vertical corner posts
// that form a bracket around the panel, rising and falling on a pulse. Strip all
// colour out and it still reads, because the bracket is a silhouette nothing else
// in a raid scene draws.
//
// WHY IT READS TroopBreachOrder AND NOT THE CONTROLLER.
// `TroopBreachOrder` already carries a Version bumped on every set / clear /
// self-clear, for exactly this purpose. Polling it means the marker follows the
// order through the two paths no writer would ever tell it about: the ordered
// panel COLLAPSING (the getter self-clears and the auto most-damaged rule takes
// back over) and scene teardown. No new coupling, no second source of truth.
//
// FIT COMES FROM THE SEGMENT'S BoxCollider, NOT ITS RENDERERS.
// A raid WallSegment's own meshes are DISABLED by RaidBaseDresser.HideWallRenderers
// and are the WRONG size anyway (WO-1722's hidden baked twins). Its BoxCollider is
// the panel — and after WO-1723's partition change it is exactly one clad panel
// wide, which is the whole point of the ruling.
//
// Built prefab-free on the RepairHighlight idiom (Village/Walls/RepairHighlight.cs)
// including its magenta guard: if no shader resolves we draw NOTHING rather than
// a magenta slab.
// =============================================================================

using UnityEngine;
using DeNelle.Core.Diagnostics;   // FlowTrace (CLAUDE.md §12)

namespace DeNelle.Village
{
    /// <summary>
    /// The in-world "ordered breach target" bracket. One instance per raid, created by
    /// <see cref="RaidDeployController"/>; it follows <see cref="TroopBreachOrder"/> on its own.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BreachOrderMarker : MonoBehaviour
    {
        private const string Sys = "RaidAI";

        // Amber-on-dark. Chosen for LUMINANCE separation from both the dirt ground and the stone
        // wall (greyscale-safe), and deliberately NOT red/green. Meaning still lives in the shape.
        private static readonly Color BracketColor = new Color(1.00f, 0.82f, 0.28f, 0.95f);
        private static readonly Color BandColor = new Color(1.00f, 0.82f, 0.28f, 0.42f);

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        /// <summary>Height of a corner post at rest, as a fraction of the panel's own height.</summary>
        private const float PostHeightFraction = 0.55f;
        private const float PostThickness = 0.28f;
        private const float BandLift = 0.07f;
        private const float PulseSpeed = 3.0f;

        private MaterialPropertyBlock _mpb;
        private Transform _band;
        private readonly Transform[] _posts = new Transform[4];
        private bool _built;
        private bool _drawable;

        private int _seenVersion = int.MinValue;
        private float _phase;
        private float _panelHeight = 3f;

        /// <summary>
        /// Creates the marker under <paramref name="parent"/> (or as a scene root when null).
        /// Idempotent per caller — the controller keeps the single reference.
        /// </summary>
        public static BreachOrderMarker Create(Transform parent)
        {
            var go = new GameObject("BreachOrderMarker");
            if (parent != null) go.transform.SetParent(parent, false);
            var m = go.AddComponent<BreachOrderMarker>();
            m.Build();
            return m;
        }

        private void Build()
        {
            if (_built) return;
            _built = true;
            _mpb = new MaterialPropertyBlock();

            var mat = BuildMarkerMaterial();
            _drawable = mat != null;

            _band = MakePart("Band", PrimitiveType.Cube, mat, BandColor);
            for (int i = 0; i < 4; i++)
                _posts[i] = MakePart("Post_" + i, PrimitiveType.Cube, mat, BracketColor);

            SetShown(false);
        }

        private Transform MakePart(string partName, PrimitiveType shape, Material mat, Color c)
        {
            var go = GameObject.CreatePrimitive(shape);
            go.name = partName;
            var col = go.GetComponent<Collider>();
            if (col != null)
            {
                if (Application.isPlaying) Destroy(col);
                else DestroyImmediate(col);
            }
            go.transform.SetParent(transform, false);
            var r = go.GetComponent<MeshRenderer>();
            if (r != null)
            {
                // FAIL-SAFE (magenta guard, RepairHighlight.cs:70-74): never leave the primitive's
                // DEFAULT material on it - under URP that renders magenta. Hide it instead.
                if (mat != null) r.sharedMaterial = mat;
                else r.enabled = false;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.receiveShadows = false;
                ApplyColorTo(r, c);
            }
            return go.transform;
        }

        /// <summary>
        /// Transparent unlit material with the FULL blend state written. RepairHighlight.cs:126-152
        /// records why every property here is required: setting `_Surface` alone at runtime does
        /// not re-run URP's ShaderGUI, so no blend state is ever written and the marker draws
        /// fully opaque despite its alpha. Returns null (draw nothing) rather than a magenta
        /// material when no shader resolves.
        /// </summary>
        private static Material BuildMarkerMaterial()
        {
            var shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) shader = Shader.Find("Unlit/Color");
            if (shader == null) shader = Shader.Find("Sprites/Default");
            if (shader == null) shader = DeNelle.Core.MagentaGuard.ResolveUrpLitShader();
            if (shader == null)
            {
                FlowTrace.Warn(Sys, "BreachOrderMarker: no marker shader resolved - drawing nothing " +
                                    "rather than a magenta bracket. The ordered panel will have no " +
                                    "highlight; the toast is the only tell.");
                return null;
            }
            var mat = new Material(shader) { name = "BreachOrderMarkerMat" };
            mat.SetFloat("_Surface", 1f);
            mat.SetFloat("_Blend", 0f);
            mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            mat.SetInt("_ZWrite", 0);
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.DisableKeyword("_ALPHATEST_ON");
            mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            FlowTrace.Step(Sys, "BreachOrderMarker material built on shader '" + shader.name +
                                "' (transparent: SrcAlpha/OneMinusSrcAlpha, ZWrite off).");
            return mat;
        }

        private void LateUpdate()
        {
            // The order's own Version is the change signal - it covers a player re-tap, the
            // Breach toggle clearing it, the panel COLLAPSING (self-clear) and teardown.
            if (_seenVersion != TroopBreachOrder.Version)
            {
                _seenVersion = TroopBreachOrder.Version;
                Refit();
            }

            if (!isActiveAndEnabled) return;
            _phase += Time.deltaTime * PulseSpeed;
            float pulse = 1f + Mathf.Sin(_phase) * 0.18f;
            for (int i = 0; i < _posts.Length; i++)
            {
                var p = _posts[i];
                if (p == null) continue;
                float h = _panelHeight * PostHeightFraction * pulse;
                p.localScale = new Vector3(PostThickness, h, PostThickness);
                var lp = p.localPosition;
                p.localPosition = new Vector3(lp.x, h * 0.5f, lp.z);
            }
        }

        /// <summary>
        /// Re-reads the standing order and re-shapes the bracket around it, or hides the marker
        /// when no order stands.
        /// </summary>
        private void Refit()
        {
            var target = TroopBreachOrder.Target;
            var seg = target as WallSegment;
            if (seg == null)
            {
                SetShown(false);
                FlowTrace.Step(Sys, "BREACH HIGHLIGHT cleared (v=" + _seenVersion +
                                    "): no live ordered WallSegment - the warband is back on the " +
                                    "automatic most-damaged pick.");
                return;
            }

            var box = seg.GetComponent<BoxCollider>();
            if (box == null)
            {
                SetShown(false);
                FlowTrace.Warn(Sys, "BREACH HIGHLIGHT: ordered wall '" + seg.name +
                                    "' has no BoxCollider - the panel footprint is unknowable, so " +
                                    "no bracket is drawn.");
                return;
            }

            var st = seg.transform;
            var lossy = st.lossyScale;
            float w = Mathf.Max(0.5f, Mathf.Abs(box.size.x * lossy.x));
            float d = Mathf.Max(0.5f, Mathf.Abs(box.size.z * lossy.z));
            float h = Mathf.Max(1f, Mathf.Abs(box.size.y * lossy.y));
            _panelHeight = h;

            transform.SetPositionAndRotation(
                new Vector3(st.position.x, 0f, st.position.z), st.rotation);

            if (_band != null)
            {
                _band.localScale = new Vector3(w + 0.5f, 0.06f, d + 0.5f);
                _band.localPosition = new Vector3(0f, BandLift, 0f);
            }
            float hx = (w + 0.5f) * 0.5f;
            float hz = (d + 0.5f) * 0.5f;
            SetPostXZ(0, -hx, -hz);
            SetPostXZ(1, hx, -hz);
            SetPostXZ(2, -hx, hz);
            SetPostXZ(3, hx, hz);

            SetShown(true);
            FlowTrace.Step(Sys, "BREACH HIGHLIGHT on '" + seg.name + "' (v=" + _seenVersion +
                                ") panel=" + w.ToString("F2") + "m x " + h.ToString("F2") +
                                "m hp=" + seg.Hp.ToString("F0") + " drawable=" +
                                (_drawable ? "yes" : "no-shader") +
                                " - bracket of 4 posts + ground band, pulsing; shape carries the " +
                                "meaning, colour does not (owner is red/green colourblind).");
        }

        private void SetPostXZ(int i, float x, float z)
        {
            var p = _posts[i];
            if (p == null) return;
            p.localPosition = new Vector3(x, 0f, z);
        }

        private void SetShown(bool shown)
        {
            if (_band != null) _band.gameObject.SetActive(shown && _drawable);
            for (int i = 0; i < _posts.Length; i++)
                if (_posts[i] != null) _posts[i].gameObject.SetActive(shown && _drawable);
        }

        private void ApplyColorTo(MeshRenderer r, Color c)
        {
            if (r == null) return;
            _mpb ??= new MaterialPropertyBlock();
            r.GetPropertyBlock(_mpb);
            _mpb.SetColor(BaseColorId, c);   // URP
            _mpb.SetColor(ColorId, c);       // built-in unlit
            r.SetPropertyBlock(_mpb);
        }
    }
}

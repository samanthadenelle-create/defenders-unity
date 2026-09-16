// =============================================================================
// RepairHighlight — the in-world highlight marker for a repairable structure.
// -----------------------------------------------------------------------------
// Workstream B — player wall-repair mechanic. A pure-code, prefab-free marker so
// the scene-setup editor file has nothing extra to wire: WallRepairController
// spawns one of these per damaged structure (a soft "this is repairable" pulse)
// and one brighter instance for the actively-selected structure.
//
// Built entirely at runtime from a Unity primitive + an unlit URP material so it
// needs no imported art. A soft ground disc pulses (scale + alpha) so damaged
// structures read at a glance without an edge-on floating strip or world text.
//
// Module isolation: DeNelle.Village only; no other-module / HUD coupling.
// =============================================================================

using DeNelle.Core.Diagnostics;   // FlowTrace - names which shader fallback resolved
using UnityEngine;

namespace DeNelle.Village
{
    /// <summary>
    /// A runtime-built, prefab-free ground highlight marker for a
    /// repairable village structure. Two intensities — a calm "repairable"
    /// pulse and a bright "selected" state — set through <see cref="SetSelected"/>.
    /// Spawned and pooled by <see cref="WallRepairController"/>.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class RepairHighlight : MonoBehaviour
    {
        private static readonly Color RepairableColor = new Color(0.96f, 0.77f, 0.35f, 0.55f); // amber
        private static readonly Color SelectedColor = new Color(0.49f, 0.23f, 0.93f, 0.85f);   // violet

        private MeshRenderer _discRenderer;
        private MaterialPropertyBlock _mpb;
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        private bool _selected;
        private float _radius = 2f;
        private float _phase;

        /// <summary>
        /// Builds a new highlight marker GameObject and returns its component.
        /// The marker is parented under <paramref name="parent"/> (kept off the
        /// structure transform so it survives a structure swap) and starts in the
        /// calm "repairable" state.
        /// </summary>
        public static RepairHighlight Create(Transform parent)
        {
            var go = new GameObject("RepairHighlight");
            if (parent != null) go.transform.SetParent(parent, false);
            var hl = go.AddComponent<RepairHighlight>();
            hl.Build();
            return hl;
        }

        private void Build()
        {
            _mpb = new MaterialPropertyBlock();
            var mat = BuildMarkerMaterial();

            // Ground disc — a flat quad lying on the structure footprint.
            var disc = GameObject.CreatePrimitive(PrimitiveType.Quad);
            disc.name = "Disc";
            DestroyCollider(disc);
            disc.transform.SetParent(transform, false);
            disc.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            disc.transform.localPosition = new Vector3(0f, 0.06f, 0f);
            _discRenderer = disc.GetComponent<MeshRenderer>();
            // FAIL-SAFE: mat is null only when no shader could be resolved. Never leave
            // the primitive's DEFAULT material on it (renders magenta under URP) - hide
            // the renderer instead so the marker is invisible, never magenta.
            if (mat != null) _discRenderer.sharedMaterial = mat;
            else _discRenderer.enabled = false;
            _discRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _discRenderer.receiveShadows = false;

            ApplyColor();
        }

        private static void DestroyCollider(GameObject go)
        {
            var col = go.GetComponent<Collider>();
            if (col != null)
            {
                if (Application.isPlaying) Destroy(col);
                else DestroyImmediate(col);
            }
        }

        /// <summary>
        /// Builds an unlit, transparent, additive-leaning material for the marker.
        /// Tries the URP unlit shader first, then the built-in unlit fallbacks so
        /// the marker still renders if the project is not on URP.
        /// FAIL-SAFE (magenta guard): if EVERY <see cref="Shader.Find"/> misses
        /// (the unlit shaders were STRIPPED from the player build) we borrow a URP
        /// shader guaranteed present in the build from a live scene material via
        /// <see cref="DeNelle.Core.MagentaGuard.ResolveUrpLitShader"/>; if even that
        /// resolves nothing we return NULL rather than <c>new Material(null)</c>
        /// (which renders opaque MAGENTA). A null result makes the caller SKIP
        /// drawing the marker - so it is either its intended translucent violet or
        /// invisible, NEVER magenta. Mirrors GroundZFightFixer's disable-rather-than
        /// -show-a-bad-material pattern.
        /// </summary>
        private static Material BuildMarkerMaterial()
        {
            var shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) shader = Shader.Find("Unlit/Color");
            if (shader == null) shader = Shader.Find("Sprites/Default");
            // Final fallback: borrow a URP shader that IS in the build from a live
            // scene material (the same runtime-resolve MagentaGuard uses for the
            // emergency hero pill). Guaranteed included because it is serialized in a
            // built scene - so it survives shader stripping.
            if (shader == null) shader = DeNelle.Core.MagentaGuard.ResolveUrpLitShader();
            if (shader == null)
            {
                // No shader resolved at all - do NOT build a magenta material.
                DeNelle.Core.Diagnostics.FlowTrace.Warn("Repair",
                    "RepairHighlight: no marker shader resolved (URP/Unlit + Unlit/Color + " +
                    "Sprites/Default + URP scene-borrow all missed) - skipping marker material " +
                    "to avoid a magenta quad.");
                return null;
            }
            var mat = new Material(shader) { name = "RepairHighlightMat" };

            // ⛔ THE ALPHA WAS NEVER ACTUALLY APPLIED (fixed 2026-08-24). This used to set only
            // `_Surface` and `_Blend` and then bump renderQueue. Setting `_Surface` AT RUNTIME does
            // not re-run URP's ShaderGUI, so NO BLEND STATE IS EVER WRITTEN — and `renderQueue`
            // alone changes sort order, not blending. The marker therefore drew fully OPAQUE
            // despite its 0.85 alpha, which is why the owner's screenshot shows a solid slab with
            // no grass texture visible through it (measured interior R-stddev 20.9 vs 51.2 on the
            // surrounding grass).
            //
            // ⚠ EVERY OTHER TRANSPARENCY SITE IN THIS REPO ALREADY WRITES THE FULL SET —
            // ExteriorTerrainBuilder, LanaUrpMaterialFix, MagentaMaterialFixer, VfxProofCapture and
            // VillageSceneBuilder.Fortify all set _SrcBlend/_DstBlend/_ZWrite plus the keyword. This
            // one site was the outlier, which is the tell: a lone "best-effort" variant of something
            // five other files do completely is usually incomplete, not deliberately minimal.
            //
            // ⚠ AND IT MATTERS MORE THAN IT LOOKS: two of the three shader fallbacks above
            // (Unlit/Color, and the URP/Lit scene-borrow) are OPAQUE BY CONSTRUCTION, so without an
            // explicit blend state the marker can never be see-through no matter what alpha the
            // caller sets.
            mat.SetFloat("_Surface", 1f);                       // 1 = Transparent
            mat.SetFloat("_Blend", 0f);                         // 0 = Alpha
            mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            mat.SetInt("_ZWrite", 0);
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.DisableKeyword("_ALPHATEST_ON");
            mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;

            // §12: name the shader that actually resolved. Three fallbacks can land here and they
            // behave DIFFERENTLY under transparency, so "which one" is the first question any future
            // look-wrong report needs answered — and it was previously unrecorded.
            FlowTrace.Step("Repair", $"marker material built on shader '{shader.name}' " +
                                     "(transparent: SrcAlpha/OneMinusSrcAlpha, ZWrite off).");
            return mat;
        }

        /// <summary>
        /// Positions and sizes the marker over <paramref name="target"/> from its
        /// renderer bounds, so one marker fits a small wall section or a large
        /// building alike.
        /// </summary>
        public void FitTo(RepairTarget target)
        {
            if (target == null || !target.IsValid) return;

            Vector3 center;
            float radius;
            if (target.TryGetWorldBounds(out var b))
            {
                center = new Vector3(b.center.x, b.min.y, b.center.z);
                radius = Mathf.Max(b.extents.x, b.extents.z) * 1.35f + 0.6f;
            }
            else
            {
                center = target.Transform != null ? target.Transform.position : Vector3.zero;
                radius = 2f;
            }

            radius = Mathf.Clamp(radius, 1f, 9f);
            _radius = radius;
            transform.position = center;

            ApplyScale(1f);
        }

        /// <summary>Switches the marker between the calm repairable and bright selected look.</summary>
        public void FitTo(Bounds bounds)
        {
            _radius = Mathf.Clamp(Mathf.Max(bounds.extents.x, bounds.extents.z) * 1.35f + .6f, 1f, 9f);
            transform.position = new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);
            ApplyScale(1f);
        }

        /// <summary>Switches the marker between the calm repairable and bright selected look.</summary>
        public void SetSelected(bool selected)
        {
            _selected = selected;
            ApplyColor();
        }

        /// <summary>Shows / hides the marker without destroying it (the controller pools markers).</summary>
        public void SetVisible(bool visible)
        {
            if (gameObject.activeSelf != visible) gameObject.SetActive(visible);
        }

        private void Update()
        {
            // Gentle pulse so damaged structures catch the eye. The selected
            // marker pulses faster + wider to read as "this one".
            float speed = _selected ? 3.4f : 1.7f;
            float amp = _selected ? 0.14f : 0.08f;
            _phase += Time.deltaTime * speed;
            float pulse = 1f + Mathf.Sin(_phase) * amp;
            ApplyScale(pulse);

        }

        private void ApplyScale(float pulse)
        {
            float d = _radius * 2f * pulse;
            if (_discRenderer != null)
                _discRenderer.transform.localScale = new Vector3(d, d, 1f);
        }

        private void ApplyColor()
        {
            Color c = _selected ? SelectedColor : RepairableColor;
            ApplyColorTo(_discRenderer, new Color(c.r, c.g, c.b, c.a * 0.32f));
        }

        private void ApplyColorTo(MeshRenderer r, Color c)
        {
            if (r == null) return;
            _mpb ??= new MaterialPropertyBlock();
            r.GetPropertyBlock(_mpb);
            _mpb.SetColor(BaseColorId, c); // URP
            _mpb.SetColor(ColorId, c);     // built-in unlit
            r.SetPropertyBlock(_mpb);
        }
    }
}

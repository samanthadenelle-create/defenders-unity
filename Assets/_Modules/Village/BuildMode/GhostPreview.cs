// =============================================================================
// GhostPreview — the translucent placement ghost for Build Mode (WO-108 P1).
// -----------------------------------------------------------------------------
// Assembly: DeNelle.Village   Namespace: DeNelle.Village
//
// Renders the selected CatalogEntry's visual as a semi-transparent preview that
// follows the cursor and tints green (valid) / red (blocked). Reuses the proven
// MaterialPropertyBlock tint approach from TowerPlacementSystem (never leaks a
// material instance per frame) and VisualFactory to skin the real prefab so the
// ghost looks like what will be placed (not a cylinder).
//
// The ghost host has NO collider, so it never blocks its own overlap test nor
// catches the placement ray.
// =============================================================================

using System.Collections.Generic;
using System;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using DeNelle.Core.Catalog;
using DeNelle.Core.Diagnostics;

namespace DeNelle.Village
{
    /// <summary>
    /// A throwaway translucent clone of the selected entry's visual that tracks the
    /// cursor and shows valid/blocked state. One ghost at a time; rebuilt when the
    /// armed entry changes.
    /// </summary>
    public sealed class GhostPreview : MonoBehaviour
    {
        private static readonly Color s_validColor = new Color(0.2f, 0.9f, 0.3f, 0.45f);
        private static readonly Color s_invalidColor = new Color(0.9f, 0.2f, 0.2f, 0.45f);

        private GameObject _visual;
        private string _builtForId;
        // The armed entry's upright correction (StructureFactory applies the SAME at
        // build time). Applied UNDER the ghost's yaw so the ghost stands upright exactly
        // like the placed structure (WYSIWYG) — yaw outermost, orientation inner.
        private OrientationFix _orientation;
        private readonly List<Renderer> _renderers = new List<Renderer>();
        // Audit P2 (build-mode): every transparent ghost material is a `new Material(shader)`
        // instance; track them so Clear() can Destroy() them instead of leaking one set per
        // re-arm for the rest of the session.
        private readonly List<Material> _createdMaterials = new List<Material>();
        private MaterialPropertyBlock _mpb;
        private Transform _authoredSource;
        private Transform _authoredRootCopy;
        private readonly List<AuthoredNode> _authoredNodes = new List<AuthoredNode>();
        private readonly List<Material> _authoredMaterialScratch = new List<Material>();

        // Entry snapshots are explicit: re-enter to refresh art/visibility. A changed
        // hierarchy invalidates the preview until then; pose updates never rebuild art.
        private sealed class AuthoredNode
        {
            public Transform source, parent;
            public Vector3 position, scale;
            public Quaternion rotation;
            public bool active, enabled;
            public int children;
            public MeshRenderer renderer;
            public MeshFilter filter;
            public Mesh mesh;
            public Material[] materials;
        }

        /// <summary>Build an exact, render-only snapshot. Refusal preserves the prior preview.
        /// Only static URP/Lit art without property-block overrides is supported.</summary>
        public bool SetAuthoredEntry(Transform source)
        {
            var nodes = new List<AuthoredNode>();
            try
            {
                if (!isActiveAndEnabled || source == null || !gameObject.scene.IsValid() ||
                    !gameObject.scene.isLoaded || transform.IsChildOf(source)) return false;
                var ancestors = new List<Transform>();
                for (var p = source.parent; p != null; p = p.parent) ancestors.Insert(0, p);
                foreach (var p in ancestors) nodes.Add(SnapshotAuthoredNode(p, false));
                foreach (var p in source.GetComponentsInChildren<Transform>(true))
                    nodes.Add(SnapshotAuthoredNode(p, true));
                if (!nodes.Exists(n => n.renderer != null)) return false;
            }
            catch (Exception e)
            {
                FlowTrace.Warn("Ghost", "Authored preview refused: " + e.Message);
                return false;
            }

            GameObject staged = null;
            var materials = new List<Material>();
            var renderers = new List<Renderer>();
            try
            {
                staged = new GameObject("AuthoredGhost") { hideFlags = HideFlags.DontSave };
                staged.SetActive(false);
                SceneManager.MoveGameObjectToScene(staged, gameObject.scene);
                var copies = new Dictionary<Transform, Transform>();
                foreach (var node in nodes)
                {
                    var copy = new GameObject(node.source.name) { hideFlags = HideFlags.DontSave };
                    copy.transform.SetParent(node.parent != null && copies.TryGetValue(node.parent, out var parent)
                        ? parent : staged.transform, false);
                    copy.transform.localPosition = node.position;
                    copy.transform.localRotation = node.rotation;
                    copy.transform.localScale = node.scale;
                    copy.SetActive(node.active);
                    copies.Add(node.source, copy.transform);
                    if (node.renderer == null) continue;
                    copy.AddComponent<MeshFilter>().sharedMesh = node.mesh;
                    var slots = new Material[node.materials.Length];
                    for (int i = 0; i < slots.Length; i++)
                    {
                        var material = new Material(node.materials[i]) { hideFlags = HideFlags.DontSave };
                        materials.Add(material);
                        material.SetFloat("_Surface", 1f);
                        material.SetFloat("_Blend", 0f);
                        material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
                        material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                        material.SetFloat("_ZWrite", 0f);
                        material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                        material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                        material.DisableKeyword("_ALPHAMODULATE_ON");
                        material.SetOverrideTag("RenderType", "Transparent");
                        material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
                        slots[i] = material;
                    }
                    var renderer = copy.AddComponent<MeshRenderer>();
                    renderer.sharedMaterials = slots;
                    renderer.enabled = node.enabled;
                    renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    renderer.receiveShadows = false;
                    renderers.Add(renderer);
                }
                var candidate = copies[source];
                bool refreshing = _authoredSource == source && _authoredRootCopy != null;
                bool visible = !refreshing || (_visual != null && _visual.activeSelf);
                if (refreshing) candidate.SetPositionAndRotation(_authoredRootCopy.position, _authoredRootCopy.rotation);
                string reason = BlockedReason;
                Clear();
                _visual = staged;
                _authoredSource = source;
                _authoredRootCopy = candidate;
                _authoredNodes.AddRange(nodes);
                _createdMaterials.AddRange(materials);
                _renderers.AddRange(renderers);
                staged = null;
                _visual.SetActive(visible);
                SetValid(IsValid);
                SetReason(reason);
                FlowTrace.Step("Ghost", $"Authored preview ready: {renderers.Count} static renderer(s).");
                return true;
            }
            catch (Exception e)
            {
                if (staged != null) DestroyOwned(staged);
                foreach (var material in materials) DestroyOwned(material);
                FlowTrace.Warn("Ghost", "Authored preview construction failed: " + e.Message);
                return false;
            }
        }

        private static AuthoredNode SnapshotAuthoredNode(Transform t, bool includeArt)
        {
            if (!FiniteMatrix(t.localToWorldMatrix) || !FiniteMatrix(t.worldToLocalMatrix) ||
                Mathf.Abs(t.localToWorldMatrix.determinant) < 1e-8f)
                throw new InvalidOperationException("Invalid or singular transform: " + t.name);
            var n = new AuthoredNode { source = t, parent = t.parent, position = t.localPosition,
                rotation = t.localRotation, scale = t.localScale, active = t.gameObject.activeSelf, children = t.childCount };
            if (!includeArt) return n; // Ancestors contribute transforms, never scene art.
            foreach (var r in t.GetComponents<Renderer>())
            {
                if (!(r is MeshRenderer mr) || n.renderer != null || r.HasPropertyBlock())
                    throw new InvalidOperationException("Unsupported renderer/override: " + t.name);
                var mf = t.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null || mf.sharedMesh.vertexCount == 0 ||
                    !FiniteBounds(mf.sharedMesh.bounds) || mf.sharedMesh.bounds.size.sqrMagnitude <= 0f ||
                    !FiniteBounds(r.localBounds))
                    throw new InvalidOperationException("Missing or invalid mesh: " + t.name);
                var slots = mr.sharedMaterials;
                if (slots.Length == 0) throw new InvalidOperationException("No materials: " + t.name);
                foreach (var m in slots)
                    if (m == null || DeNelle.Core.MagentaGuard.IsBrokenShader(m.shader) ||
                        m.shader.name != "Universal Render Pipeline/Lit" || !m.HasProperty("_Surface") ||
                        !m.HasProperty("_Blend") || !m.HasProperty("_SrcBlend") || !m.HasProperty("_DstBlend") ||
                        !m.HasProperty("_ZWrite"))
                        throw new InvalidOperationException("Authored preview requires supported URP/Lit materials.");
                n.renderer = mr; n.filter = mf; n.mesh = mf.sharedMesh; n.materials = slots; n.enabled = mr.enabled;
            }
            return n;
        }

        /// <summary>Candidate position/rotation are world-space; local scale stays authored.
        /// Source hierarchy changes invalidate the snapshot. Entry explicitly refreshes it.</summary>
        public bool MoveToAuthored(Vector3 position, Quaternion rotation)
        {
            float norm = rotation.x * rotation.x + rotation.y * rotation.y + rotation.z * rotation.z + rotation.w * rotation.w;
            if (!FiniteVector(position) || !Finite(norm) || norm < 0.0001f) return false;
            if (!isActiveAndEnabled || !AuthoredSnapshotCurrent()) { if (_authoredRootCopy != null) Clear(); return false; }
            rotation = rotation.normalized;
            var parent = _authoredRootCopy.parent;
            var proposed = parent.localToWorldMatrix * Matrix4x4.TRS(parent.InverseTransformPoint(position),
                Quaternion.Inverse(parent.rotation) * rotation, _authoredRootCopy.localScale);
            if (!FiniteMatrix(proposed) || Mathf.Abs(proposed.determinant) < 1e-8f) return false;
            var delta = proposed * _authoredRootCopy.worldToLocalMatrix;
            foreach (var r in _renderers)
            {
                var matrix = delta * r.localToWorldMatrix;
                var b = r.localBounds;
                for (int i = 0; i < 8; i++)
                    if (!FiniteVector(matrix.MultiplyPoint3x4(b.center + Vector3.Scale(b.extents,
                        new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1))))) return false;
            }
            _authoredRootCopy.SetPositionAndRotation(position, rotation);
            _visual.SetActive(true);
            return true;
        }

        private bool AuthoredSnapshotCurrent()
        {
            if (_authoredSource == null || _authoredRootCopy == null || _visual == null ||
                _visual.scene != gameObject.scene) return false;
            foreach (var n in _authoredNodes)
            {
                if (n.source == null || n.source.parent != n.parent || n.source.localPosition != n.position ||
                    n.source.localRotation != n.rotation || n.source.localScale != n.scale ||
                    n.source.gameObject.activeSelf != n.active || n.source.childCount != n.children) return false;
                if (n.materials == null) continue;
                if (n.renderer == null || n.filter == null || n.filter.sharedMesh != n.mesh || n.renderer.enabled != n.enabled) return false;
                n.renderer.GetSharedMaterials(_authoredMaterialScratch);
                if (_authoredMaterialScratch.Count != n.materials.Length) return false;
                for (int i = 0; i < n.materials.Length; i++)
                    if (_authoredMaterialScratch[i] != n.materials[i]) return false;
            }
            return true;
        }

        private static bool Finite(float v) => !float.IsNaN(v) && !float.IsInfinity(v);
        private static bool FiniteVector(Vector3 v) => Finite(v.x) && Finite(v.y) && Finite(v.z);
        private static bool FiniteBounds(Bounds b) => FiniteVector(b.center) && FiniteVector(b.size);
        private static bool FiniteMatrix(Matrix4x4 m)
        {
            for (int i = 0; i < 16; i++) if (!Finite(m[i])) return false;
            return true;
        }

        // ── Reject-reason label (owner 2026-07-24 "tell me why it's red") ────────
        // A silent world-space label that floats above the ghost showing WHY it can't be
        // placed while blocked. NON-buzzing + no toast spam: the place loop just feeds it
        // the reason string every frame (MessageFor / shortfall) and it shows/hides with
        // the red tint. Parented to the host (persists across re-arm); follows the tracked
        // visual and billboards to the camera each frame. Fail-safe: build/positioning is
        // guarded so a UI hiccup never throws into the place loop.
        private GameObject _reasonGo;
        private Text _reasonText;
        private Camera _reasonCam;
        private const float ReasonLabelHeight = 2.4f;   // world units above the ghost base

        /// <summary>
        /// (Re)build the ghost for <paramref name="entry"/>. No-op if the same entry
        /// is already shown. Falls back to a flat marker when the visual is absent.
        /// </summary>
        public void SetEntry(CatalogEntry entry)
        {
            if (entry == null) { FlowTrace.Warn("Ghost", "SetEntry(null) — hiding ghost"); Hide(); return; }
            if (_visual != null && _builtForId == entry.id) return;

            FlowTrace.Step("Ghost", $"SetEntry id='{entry.id}' prefabPath='{entry.visualPrefabPath ?? "<null>"}'");
            Clear();
            _builtForId = entry.id;
            _orientation = entry.orientation;   // applied UNDER the yaw on the skinned model
            _mpb = new MaterialPropertyBlock();

            _visual = new GameObject("BuildGhost");
            _visual.transform.SetParent(transform, false);

            float fit = entry.repo != null && entry.repo.placement != null
                ? Mathf.Max(1f, entry.repo.placement.footprint)
                : 3f;

            // WYSIWYG SKIN OPTIONS (TKT-12 → WO-928) — THE MATCH IS NOW GUARANTEED BY CALLING THE
            // SHARED BUILDER, NOT BY MIRRORING IT. `StructureFactory.OptsFor(entry)` is the SAME call
            // StructureFactory.Create and ReskinForLevel make, so the ghost cannot fit/rotate
            // differently from the thing it places — by construction, not by promise.
            //
            // WHY THE MIRROR HAD TO GO (this comment is the evidence; do not delete it): this site used
            // to hand-roll `SkinOptions.Structure(0f)` + `FitHeight = YHeightVariable * repo.heightMul`
            // under a comment insisting it matched Create "EXACTLY". A comment cannot keep that promise
            // — a second copy of a formula matches only until the first one grows, and there is no gate
            // that notices. It grew: WO-928 added the PER-ROW rotation policy
            // (`repo.preservePrefabRotation`) to OptsFor and the copy here never carried it, so the
            // `tower_ground_archer` ghost rendered LYING DOWN AND OVERSIZED (one defect, not two —
            // flattening the prefab's own upright 270 also makes VisualFactory.Fit measure the SHORT
            // axis to reach the height target) while the placed tower stood upright. The player aimed
            // with one shape and got another. Anything added to OptsFor from here on reaches the ghost
            // for free. DO NOT RE-DERIVE THESE OPTIONS HERE — re-deriving them IS the bug.
            SkinOptions opts = StructureFactory.OptsFor(entry);   // fit-to-height + per-row rotation policy + trace id

            // GHOST-ONLY DIVERGENCES — deliberate, and layered ON TOP of the shared options rather than
            // replacing them, so the next reader can tell an intentional difference from a drift:
            //   • translucent materials + valid/blocked tint (ApplyTransparentMaterials, below) — a
            //     preview must read through; the placed structure is opaque.
            //   • colliders stripped (StripColliders, below) — the ghost must not block its own overlap
            //     test nor catch the placement ray (see the file header).
            //   • flat-disc fallback on a missing pack (uses `fit`, below) — Create refuses to seat a
            //     meshless structure and returns null, but the ghost still has to show the player
            //     SOMETHING under the cursor rather than nothing.
            // All three are POST-skin presentation. Nothing about SHAPE — fit height, rotation policy —
            // is decided here; that all comes from OptsFor above.
            // (`fit`, computed above, stays method-scoped because the fallback disc consumes it.)

            GameObject skinned = null;
            if (!string.IsNullOrEmpty(entry.visualPrefabPath))
                skinned = VisualFactory.Skin(_visual.transform, entry.visualPrefabPath, opts);

            // WYSIWYG — euler is already in OptsFor → LocalRotation (pre-Fit), matching
            // StructureFactory.Create after GROK_BRIEF 2026-08-19. Only offset/scale remain
            // post-Skin; do NOT re-multiply euler (would tip twice / wrong height).
            if (skinned != null && _orientation != null && _orientation.manual)
            {
                var t = skinned.transform;
                bool moved = false;
                Vector3 off = _orientation.Offset;
                if (off.sqrMagnitude > 0.0001f)
                {
                    t.localPosition += off;
                    moved = true;
                }
                if (_orientation.scale > 0f && !Mathf.Approximately(_orientation.scale, 1f))
                {
                    t.localScale *= _orientation.scale;
                    moved = true;
                }
                if (moved)
                    ReseatCorrectedBottom(skinned, _visual.transform.position.y);
            }

            if (skinned == null)
            {
                FlowTrace.Warn("Ghost", $"VisualFactory.Skin returned null for '{entry.visualPrefabPath ?? "<none>"}' — falling back to a flat disc marker");
                // Pack-missing-safe: a flat translucent disc stands in for the mesh.
                var disc = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                disc.transform.SetParent(_visual.transform, false);
                disc.transform.localScale = new Vector3(fit, 0.05f, fit);
                var c = disc.GetComponent<Collider>();
                if (c != null) DestroyOwned(c);
            }

            // Collect renderers, strip colliders, and swap to a transparent material.
            CollectRenderers(_visual.transform);
            StripColliders(_visual.transform);
            ApplyTransparentMaterials();
        }

        /// <summary>
        /// (Re)build a simple PLACEHOLDER ghost (a translucent capsule on a disc) not
        /// tied to a CatalogEntry. Used by the Arena Defense setup screen (WO-389): a
        /// pre-placed troop has no CatalogEntry visual at setup time (full troop-body
        /// skinning happens at raid spawn), so the setup screen reuses this SAME ghost
        /// primitive — MoveTo / SetValid / Hide — with a generic marker. The
        /// <paramref name="key"/> dedupes rebuilds so re-arming the same troop is a no-op.
        /// </summary>
        public void SetPlaceholder(string key, float fit = 2.5f)
        {
            if (_visual != null && _builtForId == key) return;

            Clear();
            _builtForId = key;
            _mpb = new MaterialPropertyBlock();

            _visual = new GameObject("ArenaDefenseGhost");
            _visual.transform.SetParent(transform, false);

            // A capsule body (the unit) standing on a thin footprint disc — enough to
            // read placement + validity; the real troop body is skinned at spawn.
            var body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            body.transform.SetParent(_visual.transform, false);
            body.transform.localScale = new Vector3(0.9f, 1.1f, 0.9f);
            body.transform.localPosition = new Vector3(0f, 1.1f, 0f);

            var disc = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            disc.transform.SetParent(_visual.transform, false);
            disc.transform.localScale = new Vector3(Mathf.Max(1f, fit), 0.05f, Mathf.Max(1f, fit));

            CollectRenderers(_visual.transform);
            StripColliders(_visual.transform);
            ApplyTransparentMaterials();
        }

        /// <summary>Move the ghost to a snapped world position with a discrete 90° yaw
        /// (legacy quarter-step callers, e.g. the Arena defense setup screen).</summary>
        public void MoveTo(Vector3 snappedWorldPos, int yawSteps)
            => MoveTo(snappedWorldPos, yawSteps * 90f);

        /// <summary>Move the ghost to a snapped world position with an exact yaw in degrees
        /// (WO-673 L5 — Build Mode rotates in 45° steps, so the controller passes degrees).</summary>
        public void MoveTo(Vector3 snappedWorldPos, float yawDegrees)
        {
            if (_authoredRootCopy != null) return; // Authored geometry requires the full-pose API.
            if (_visual == null) return;
            _visual.transform.SetPositionAndRotation(
                snappedWorldPos, Quaternion.Euler(0f, yawDegrees, 0f));
            if (!_visual.activeSelf) _visual.SetActive(true);
        }

        /// <summary>
        /// Last validity set on this ghost. WO-1010: the tint below is the ONLY signal today,
        /// which fails colour-vision-deficient players outright — exposing it lets the HUD say
        /// "OK" or "Blocked" in WORDS on the confirm chip alongside the colour.
        /// </summary>
        public bool IsValid { get; private set; } = true;

        /// <summary>Last reason passed to <see cref="SetReason"/> (empty when unblocked).</summary>
        public string BlockedReason { get; private set; } = string.Empty;

        /// <summary>Tint the ghost green (valid) or red (blocked) via the shared MPB.</summary>
        public void SetValid(bool valid)
        {
            IsValid = valid;
            if (_mpb == null) _mpb = new MaterialPropertyBlock();
            var color = valid ? s_validColor : s_invalidColor;
            foreach (var r in _renderers)
            {
                if (r == null) continue;
                r.GetPropertyBlock(_mpb);
                _mpb.SetColor("_BaseColor", color);
                _mpb.SetColor("_Color", color);
                r.SetPropertyBlock(_mpb);
            }
        }

        /// <summary>
        /// Show the floating "why it's red" label above the ghost (owner 2026-07-24). Pass a
        /// message (e.g. "Ground is too uneven here", "Not enough Wood (70)") while the ghost
        /// is blocked; pass null/empty to clear it (valid placement, or ghost hidden). Silent
        /// (no buzz) and idempotent per frame so passive hover never spams a toast/sound.
        /// Fail-safe: any build hiccup just leaves the label hidden.
        /// </summary>
        public void SetReason(string message)
        {
            BlockedReason = message ?? string.Empty;
            bool show = !string.IsNullOrEmpty(message) && _visual != null && _visual.activeSelf;
            try
            {
                if (show && _reasonGo == null) BuildReasonLabel();
                if (_reasonGo == null) return;
                if (_reasonGo.activeSelf != show) _reasonGo.SetActive(show);
                if (show && _reasonText != null && _reasonText.text != message)
                {
                    _reasonText.text = message;
                    // WO-1252: grow the pill to the explicit line count so a next-step
                    // sentence is never half-cut. Width matches the place toast (500 px).
                    int lines = 1;
                    for (int i = 0; i < message.Length; i++)
                        if (message[i] == '\n') lines++;
                    var crt = (RectTransform)_reasonGo.transform;
                    crt.sizeDelta = new Vector2(500f, Mathf.Max(64f, 16f + lines * 32f));
                }
            }
            catch (System.Exception e)
            {
                FlowTrace.Warn("Ghost", $"SetReason failed (label hidden): {e.Message}");
                if (_reasonGo != null) _reasonGo.SetActive(false);
            }
        }

        /// <summary>
        /// The ghost's CURRENT world position — the TRACKED VISUAL's transform, not the
        /// host. WO-683 fleet RCA (run detail "stuck at (15, 15)" = WorldToCell(origin)):
        /// MoveTo drives the child <c>_visual</c>; the GhostPreview host GameObject never
        /// moves off world origin, so any probe reading <c>transform.position</c> sees a
        /// constant. Falls back to the host position when no visual is built.
        /// </summary>
        public Vector3 CurrentPosition => _authoredRootCopy != null ? _authoredRootCopy.position
            : _visual != null ? _visual.transform.position : transform.position;

        /// <summary>Hide (but keep) the ghost — re-shown on the next MoveTo.</summary>
        public void Hide()
        {
            if (_visual != null) _visual.SetActive(false);
            if (_reasonGo != null) _reasonGo.SetActive(false);   // no floating label on a hidden ghost
        }

        /// <summary>Destroy the ghost entirely (placement landed / cancelled).</summary>
        public void Clear()
        {
            if (_visual != null) _visual.SetActive(false); // Stop drawing before deferred destruction.
            _renderers.Clear();
            // Audit P2 (build-mode): destroy the ghost's owned material instances so re-arming
            // doesn't leak a Material set per entry.
            for (int i = 0; i < _createdMaterials.Count; i++)
                if (_createdMaterials[i] != null) DestroyOwned(_createdMaterials[i]);
            _createdMaterials.Clear();
            if (_visual != null) DestroyOwned(_visual);
            _visual = null;
            _builtForId = null;
            _orientation = null;
            _authoredSource = null;
            _authoredRootCopy = null;
            _authoredNodes.Clear();
            _authoredMaterialScratch.Clear();
            if (_reasonGo != null) _reasonGo.SetActive(false);   // keep the label object; just hide it
        }

        private void OnDestroy() => Clear();
        private void OnDisable() { if (_authoredRootCopy != null) Clear(); }
        private static void DestroyOwned(UnityEngine.Object obj)
        {
            if (obj == null) return;
            if (Application.isPlaying) Destroy(obj); else DestroyImmediate(obj);
        }

        /// <summary>Keep the reason label floating above the ghost + facing the camera.</summary>
        private void Update()
        {
            if (_authoredNodes.Count != 0 && !AuthoredSnapshotCurrent())
            {
                FlowTrace.Step("Ghost", "Authored source changed or disappeared; preview invalidated.");
                Clear();
            }
            if (_reasonGo == null || !_reasonGo.activeSelf) return;
            try
            {
                if (_reasonCam == null) _reasonCam = Camera.main;
                _reasonGo.transform.position = CurrentPosition + Vector3.up * ReasonLabelHeight;
                if (_reasonCam != null)
                    _reasonGo.transform.rotation = Quaternion.LookRotation(
                        _reasonGo.transform.position - _reasonCam.transform.position, Vector3.up);
            }
            catch (System.Exception e)
            {
                FlowTrace.Warn("Ghost", $"reason-label follow failed (hidden): {e.Message}");
                _reasonGo.SetActive(false);
            }
        }

        // ── helpers ────────────────────────────────────────────────────────────

        /// <summary>
        /// Lazily build the world-space reason label: a dark pill with soft-red WebGL-safe
        /// Text (LegacyRuntime.ttf), parented to the host so it survives ghost re-arms.
        /// </summary>
        private void BuildReasonLabel()
        {
            _reasonGo = new GameObject("GhostReasonLabel");
            _reasonGo.transform.SetParent(transform, false);

            var canvas = _reasonGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;

            var crt = (RectTransform)_reasonGo.transform;
            crt.sizeDelta = new Vector2(500f, 64f);
            crt.localScale = Vector3.one * 0.012f;   // 500px * 0.012 ~= 6.0 world units wide (WO-1252 wrap)

            // Dark pill so the text reads over any terrain colour.
            var bg = new GameObject("bg");
            bg.transform.SetParent(_reasonGo.transform, false);
            var bgImg = bg.AddComponent<Image>();
            bgImg.color = new Color(0f, 0f, 0f, 0.72f);
            var bgrt = (RectTransform)bg.transform;
            bgrt.anchorMin = Vector2.zero; bgrt.anchorMax = Vector2.one;
            bgrt.offsetMin = Vector2.zero; bgrt.offsetMax = Vector2.zero;

            var txtGo = new GameObject("text");
            txtGo.transform.SetParent(_reasonGo.transform, false);
            _reasonText = txtGo.AddComponent<Text>();
            _reasonText.font = ReasonFont();
            _reasonText.fontSize = 28;
            _reasonText.alignment = TextAnchor.MiddleCenter;
            _reasonText.horizontalOverflow = HorizontalWrapMode.Wrap;
            _reasonText.verticalOverflow = VerticalWrapMode.Overflow;
            _reasonText.color = new Color(1f, 0.55f, 0.5f, 1f);   // soft red, matches the blocked tint
            var trt = (RectTransform)txtGo.transform;
            trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one;
            trt.offsetMin = new Vector2(10f, 4f); trt.offsetMax = new Vector2(-10f, -4f);
        }

        private static Font ReasonFont() =>
            Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf")
            ?? Resources.GetBuiltinResource<Font>("Arial.ttf");

        private void CollectRenderers(Transform root)
        {
            _renderers.Clear();
            _renderers.AddRange(root.GetComponentsInChildren<Renderer>(true));
        }

        /// <summary>
        /// Drop <paramref name="go"/> so its current (post-correction) world-bounds base sits
        /// at <paramref name="groundY"/> — mirrors StructureFactory.ReseatCorrectedBottom so
        /// the ghost and the placed structure seat identically (WYSIWYG).
        /// </summary>
        private static void ReseatCorrectedBottom(GameObject go, float groundY)
        {
            if (go == null) return;
            var rends = go.GetComponentsInChildren<Renderer>(true);
            if (rends == null || rends.Length == 0) return;
            Bounds b = rends[0].bounds;
            for (int i = 1; i < rends.Length; i++) b.Encapsulate(rends[i].bounds);
            float dy = groundY - b.min.y;
            if (!Mathf.Approximately(dy, 0f))
                go.transform.position += new Vector3(0f, dy, 0f);
        }

        private void StripColliders(Transform root)
        {
            foreach (var c in root.GetComponentsInChildren<Collider>(true))
                if (c != null) DestroyOwned(c);
        }

        private void ApplyTransparentMaterials()
        {
            foreach (var r in _renderers)
            {
                if (r == null) continue;
                var src = r.sharedMaterial;
                // §12 (owner F8 build-mode top-down: solid PINK gate/wall ghost): a build strips the
                // polyperfect Standard/built-in variant, so src.shader resolves to
                // Hidden/InternalErrorShader (magenta) — NON-null, so the old `?? URP/Lit` fallback
                // never fired and we copied the error shader. Worse, InternalErrorShader has no
                // _Surface, so the transparency block below was skipped → an OPAQUE pink ghost.
                // Treat a null/broken/Standard/Legacy/error source shader as "unusable" and build the
                // ghost on URP/Lit so it renders translucent, never magenta.
                //
                // SINGLE AUTHORITY (2026-08-02): this used to carry a LOCAL copy of the predicate
                // (IsBrokenGhostShader). The copy had already DRIFTED - it was missing MagentaGuard's
                // `!sh.isSupported` branch, which is the ANDROID/ON-DEVICE case: a shader that compiles
                // in the editor and on desktop but fails to compile against the device's graphics API
                // keeps its NAME (so every name-only test below passes it as "fine") and renders MAGENTA.
                // A build ghost on the Seeker was therefore structurally undetectable. Route through
                // MagentaGuard.IsBrokenShader so there is exactly ONE definition of "would this render
                // magenta" in the runtime tree. DETECT ONLY - the ghost does not want MagentaGuard's
                // recovery sweep (that assigns FRESH OPAQUE URP/Lit materials into the renderer, which
                // is the exact opposite of a translucent preview, and would also disable a primitive
                // placeholder mesh). We only need the verdict; the transparent material is built below.
                Shader srcShader = src != null ? src.shader : null;
                bool broken = DeNelle.Core.MagentaGuard.IsBrokenShader(srcShader);
                if (broken) FlowTrace.Warn("Ghost", $"source shader '{(srcShader != null ? srcShader.name : "<null>")}' is broken/stripped for URP — rebuilding ghost on URP/Lit (pink-ghost guard)");
                Shader shader = !broken
                                ? srcShader
                                : (Shader.Find("Universal Render Pipeline/Lit")
                                   ?? Shader.Find("Sprites/Default"));
                var mat = new Material(shader);
                _createdMaterials.Add(mat);   // Audit P2: tracked for Destroy() in Clear()
                if (mat.HasProperty("_Surface"))
                {
                    mat.SetFloat("_Surface", 1f);   // transparent
                    mat.SetFloat("_Blend", 0f);
                    mat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
                    mat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                    mat.SetFloat("_ZWrite", 0f);
                    mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                    mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
                }
                r.sharedMaterial = mat;
            }
            SetValid(true);
        }

        // NOTE: the local IsBrokenGhostShader predicate was DELETED (2026-08-02) in favour of the
        // single authority DeNelle.Core.MagentaGuard.IsBrokenShader (see the call site above). Do not
        // re-add a local copy here - ShaderPredicateSingleAuthorityRegression FAILS the build gate if
        // a second broken-shader predicate reappears anywhere in the runtime tree.
    }
}

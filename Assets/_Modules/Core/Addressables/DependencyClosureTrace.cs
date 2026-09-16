// =============================================================================
// DependencyClosureTrace — step-in / step-out tracing for a loaded asset's
// DEPENDENCIES, shared by every *AssetLoader seam.
// -----------------------------------------------------------------------------
// OWNER DESIGN (2026-08-17): "then we track its dependency with step in step out",
// with the phone as the instrument — "with the raw data feed from the phone you
// can do this in a single session". This is CLAUDE.md §12 applied to asset
// resolution: Enter the thing, Step each part, Fail the part that missed, Exit
// with a count.
//
// ⛔ WHY THE DEPENDENCIES AND NOT JUST THE ASSET. A loader that reports
// "loaded Structures/Forge OK" is answering the easy question. The building still
// renders grey, untextured or magenta when a MATERIAL or a TEXTURE one level down
// failed — and today proved that exact gap twice: a Tripo FBX whose material
// carried a NULL albedo while the prefab loaded fine, and a navmesh bake that
// reported success having written nothing. A top-level "OK" is compatible with a
// broken asset. The closure is where the truth is.
//
// ⛔ AND IT MUST WRAP BOTH BRANCHES. The seams are Addressables-FIRST,
// Resources-FALLBACK. A half-migrated slice shows up as a silent fallback: the
// address is wrong, Resources still answers, the game looks fine and the bytes
// ship TWICE. So the fallback path is traced too, and a fallback on an asset that
// was supposed to have moved is reported as an anomaly, not a success.
//
// Tag is [Flow:<system>] so it is greppable straight off the device:
//   adb logcat | grep "\[Flow:StructureAssets\]"
// =============================================================================

using System.Collections.Generic;
using DeNelle.Core.Diagnostics;
using UnityEngine;

namespace DeNelle.Core
{
    /// <summary>Traces the dependency closure of a resolved asset (materials + their textures).</summary>
    public static class DependencyClosureTrace
    {
        /// <summary>
        /// Walks a loaded prefab's renderers, materials and albedo slots, emitting one Step per
        /// resolved dependency and a Fail per missing one. Returns true when the closure is whole.
        /// <para>
        /// Cheap by construction: it reads already-loaded references and allocates nothing beyond a
        /// small set. It is safe to call on every resolve — and it MUST be, because the failure this
        /// catches is intermittent (one building, one material) and a sampling trace would miss it.
        /// </para>
        /// </summary>
        /// <param name="system">FlowTrace system tag — the caller's, so the line greps with its seam.</param>
        /// <param name="address">The address/key that was resolved, for the log line.</param>
        /// <param name="asset">The loaded object. Null is reported by the CALLER, not here.</param>
        /// <param name="viaFallback">
        /// True when this came from the Resources fallback rather than Addressables. For a MIGRATED
        /// asset that is a defect (wrong address, bytes double-shipping), so it is surfaced.
        /// </param>
        public static bool Verify(string system, string address, Object asset, bool viaFallback)
        {
            if (asset == null) return false;

            var go = asset as GameObject;
            if (go == null)
            {
                // Non-prefab (texture, controller, ScriptableObject): nothing to walk. Still worth a
                // line so the trace shows the resolve happened at all.
                FlowTrace.Step(system, $"resolve '{address}' -> {asset.GetType().Name} " +
                                       $"({(viaFallback ? "Resources FALLBACK" : "Addressables")}), no closure to walk.");
                return true;
            }

            // STEP IN. FlowTrace has no Enter/Exit pair — Measure IS the scoped in/out primitive
            // (CLAUDE.md's "Enter/Step/Warn/Fail" is shorthand for the discipline, not the API), and
            // it adds the elapsed time for free, which turns a slow Addressables resolve into data
            // rather than a hunch.
            using var scope = FlowTrace.Measure(system,
                $"resolve '{address}' ({(viaFallback ? "Resources FALLBACK" : "Addressables")})");

            int ok = 0, missing = 0;
            var seen = new HashSet<int>();

            var renderers = go.GetComponentsInChildren<Renderer>(true);
            if (renderers == null || renderers.Length == 0)
            {
                FlowTrace.Step(system, $"'{address}' has NO renderers — nothing to verify (marker or logic-only prefab). deps 0/0");
                return true;
            }

            foreach (var r in renderers)
            {
                if (r == null) continue;
                foreach (var mat in r.sharedMaterials)
                {
                    if (mat == null)
                    {
                        missing++;
                        // A NULL material slot draws engine-default MAGENTA. Naming the renderer is
                        // what turns "something is pink" into a one-line fix.
                        FlowTrace.Fail(system, $"dep MISS on '{address}': renderer '{r.name}' has a NULL material slot " +
                                               "— that renderer draws engine-default MAGENTA in game.");
                        continue;
                    }
                    if (!seen.Add(mat.GetInstanceID())) continue;

                    // ⛔ ASK THE SHADER WHAT ITS ALBEDO SLOT IS CALLED — DO NOT ASSUME URP/Lit.
                    // WO-1302: this used to probe ONLY "_BaseMap" and "_MainTex", so every Synty
                    // shader-graph material (albedo slot "_Albedo_Map", tint left white) landed in
                    // the `missing` branch while being fully textured — 13 F8 error captures on one
                    // working watchtower. The project is mid-retheme onto Synty, so that surface was
                    // growing with every prefab swapped over. The fix is NOT a list of known material
                    // names (a hand-maintained exception list rots); it is to enumerate the shader's
                    // own texture properties and CLASSIFY them by token, so a shader nobody has
                    // written yet is handled correctly the first time it loads.
                    Texture albedo = FindAlbedo(mat, out _);

                    // A null albedo is only a defect when the material is ALSO untinted — the
                    // Polyperfect flat-colour materials legitimately carry no map. That distinction
                    // was learned the hard way today: the first version of this check reported 21
                    // working prefabs as broken, and an oracle that cries wolf gets ignored on the
                    // day it is right.
                    Color tint = Color.white;
                    if (mat.HasProperty("_BaseColor")) tint = mat.GetColor("_BaseColor");
                    else if (mat.HasProperty("_Color")) tint = mat.GetColor("_Color");
                    bool tinted = Mathf.Min(tint.r, Mathf.Min(tint.g, tint.b)) < 0.92f;

                    if (albedo != null || tinted) { ok++; }
                    else
                    {
                        missing++;
                        // Name the shader and every texture slot we looked at. A real miss then reads
                        // as one line ("the slot exists and is empty") instead of a hunt, and a NEW
                        // false positive would be self-evident ("the slot it lives in is not listed").
                        FlowTrace.Fail(system, $"dep MISS on '{address}': material '{mat.name}' has NO albedo and NO tint " +
                                               "— renders as an untextured grey blob. " +
                                               $"shader='{(mat.shader != null ? mat.shader.name : "<null>")}' " +
                                               $"albedo slots scanned: {DescribeAlbedoSlots(mat)}");
                    }
                }
            }

            if (viaFallback)
            {
                // ⛔ THIS BRANCH USED TO CRY WOLF, AND IT COST A TRIAGE CYCLE ON 2026-08-20.
                // It Warned on EVERY fallback with the words "its bytes are shipping TWICE",
                // hedged only by an "if this asset has been migrated" the reader cannot evaluate.
                // The device log then carried four of them — Harvest/crystals, /food, /iron, /wood
                // — which read as a shipping defect and are nothing of the kind: there is NO
                // Harvest/* key in Addressables (verified against
                // Assets/AddressableAssetsData/AssetGroups/*.asset: the only prefixes registered
                // are gear/, Enemies/, Structures/, hero, dungeon and the localization tables), and
                // those four FBXs are ~33-156 KB each, deliberately left in Resources for WebGL
                // (HarvestSite.cs:274). Nothing is duplicated. An oracle that cries wolf gets
                // ignored on the day it is right — the same lesson the albedo check above learned.
                //
                // So ASK, do not guess. A fallback is a double-ship ONLY when the address is also
                // registered with Addressables; otherwise Resources is the only place it lives.
                if (StructureContentWarmer.IsRegisteredAddress(address))
                {
                    FlowTrace.Fail(system, $"'{address}' is REGISTERED with Addressables but resolved from the " +
                                           "RESOURCES FALLBACK. Its bytes are shipping TWICE — once in the bundle and " +
                                           "once in the force-included Resources payload — and the Addressables copy " +
                                           "is the one nobody is reading. Delete the Resources copy or fix the address.");
                }
                else
                {
                    FlowTrace.Step(system, $"'{address}' resolved from Resources, which is the ONLY place it lives " +
                                           "(no Addressables key for it). Not a double-ship, not a migration gap — " +
                                           "content that was deliberately never moved.");
                }
            }

            // STEP OUT. The count is the whole point: "deps 7/7 ok" is a pass you can grep for on
            // the device, and "deps 6/7" names a partial closure that a top-level "loaded OK" hides.
            FlowTrace.Step(system, $"resolve '{address}' deps {ok}/{ok + missing} ok");
            return missing == 0;
        }

        // =====================================================================
        // ALBEDO SLOT RESOLUTION (WO-1302)
        // ---------------------------------------------------------------------
        // The question "is this material textured?" is shader-relative: URP/Lit
        // calls the slot _BaseMap, the built-in pipeline _MainTex, Synty's
        // Generic_Basic shader graph _Albedo_Map, Synty's newer graphs _Base_Map
        // or _Base_Texture, and a triplanar graph splits it three ways. There is
        // no fixed name to probe, so we ASK THE SHADER for its texture properties
        // and classify each by TOKEN.
        //
        // ⛔ THIS IS DELIBERATELY NOT A LIST OF KNOWN MATERIALS OR SHADERS.
        // An allowlist of names is one fact written twice and it rots the day a
        // new pack lands — which is the failure mode this whole file exists to
        // avoid ("an oracle that cries wolf gets ignored on the day it is right").
        // A token classifier generalises instead: it accepts an albedo slot it
        // has never seen, and it still REJECTS normal/emission/mask/detail maps,
        // so a material whose only populated texture is a normal map is still
        // correctly reported as a grey blob.
        // =====================================================================

        /// <summary>Substrings that mark a texture slot as NOT the base colour map. Checked first.</summary>
        private static readonly string[] NotAlbedoTokens =
        {
            "detail", "normal", "bump", "mask", "emission", "emissive", "occlusion",
            "metallic", "specular", "gloss", "smoothness", "rough", "height",
            "parallax", "lightmap", "shadow", "noise", "displacement", "opacity",
            "overlay", "curvature", "flow", "matcap"
        };

        /// <summary>Substrings that mark a texture slot as the base colour (albedo) map.</summary>
        private static readonly string[] AlbedoTokens =
        {
            "albedo", "basemap", "basetexture", "basecolor", "basecolour",
            "maintex", "maintexture", "diffuse", "colormap", "colourmap",
            "triplanartexture"
        };

        /// <summary>Lower-cases and strips separators so "_Albedo_Map" and "_AlbedoMap" compare equal.</summary>
        private static string Normalize(string propertyName)
        {
            if (string.IsNullOrEmpty(propertyName)) return string.Empty;
            var sb = new System.Text.StringBuilder(propertyName.Length);
            for (int i = 0; i < propertyName.Length; i++)
            {
                char c = propertyName[i];
                if (c == '_' || c == ' ' || c == '-') continue;
                sb.Append(char.ToLowerInvariant(c));
            }
            return sb.ToString();
        }

        /// <summary>
        /// Public so the regression suite can pin BOTH directions of the classifier
        /// (`StructureNullMaterialSlotRegression`): an albedo slot name must be accepted, and a
        /// normal/emission/mask/detail slot name must still be rejected. A detector proven only in
        /// the "stops complaining" direction is how a real defect walks through.
        /// </summary>
        public static bool IsAlbedoSlot(string shaderPropertyName) => IsAlbedoSlotName(shaderPropertyName);

        /// <summary>
        /// True when <paramref name="mat"/> carries a populated base-colour texture in ANY slot its
        /// shader exposes. Public for the same both-directions regression reason as above.
        /// </summary>
        public static bool HasAlbedo(Material mat) => FindAlbedo(mat, out _) != null;

        /// <summary>
        /// The populated base-colour texture on <paramref name="mat"/>, or null when none is bound.
        /// Public so TripoMaterialFixer / hub re-skin copy Synty <c>_Albedo_Map</c> (and any future
        /// pack slot) instead of hard-coding <c>_MainTex</c>/<c>_BaseMap</c>. Device 2026-09-11
        /// new-save: those two names were empty, VERIFY called OK, LightSkin storefronts rendered
        /// flat white.
        /// </summary>
        public static Texture GetAlbedo(Material mat) => FindAlbedo(mat, out _);

        /// <summary>Public evidence line: which albedo-classified slots exist and which are populated.</summary>
        public static string DescribeAlbedo(Material mat) => DescribeAlbedoSlots(mat);

        /// <summary>True when this shader property name names a base-colour (albedo) texture slot.</summary>
        private static bool IsAlbedoSlotName(string propertyName)
        {
            string n = Normalize(propertyName);
            if (n.Length == 0) return false;
            for (int i = 0; i < NotAlbedoTokens.Length; i++)
                if (n.Contains(NotAlbedoTokens[i])) return false;
            for (int i = 0; i < AlbedoTokens.Length; i++)
                if (n.Contains(AlbedoTokens[i])) return true;
            return false;
        }

        /// <summary>
        /// Returns the first POPULATED base-colour texture on <paramref name="mat"/>, or null when the
        /// material genuinely carries no albedo. <paramref name="slot"/> names the property it came
        /// from (or the last empty albedo slot inspected), so the trace can say WHERE it looked.
        /// </summary>
        private static Texture FindAlbedo(Material mat, out string slot)
        {
            slot = null;
            if (mat == null) return null;

            // Fast path: the two names that cover URP/Lit and the built-in pipeline, i.e. most of
            // the project. Kept explicit so the common case costs no shader reflection at all.
            if (mat.HasProperty("_BaseMap"))
            {
                var t = mat.GetTexture("_BaseMap");
                slot = "_BaseMap";
                if (t != null) return t;
            }
            if (mat.HasProperty("_MainTex"))
            {
                var t = mat.GetTexture("_MainTex");
                slot = "_MainTex";
                if (t != null) return t;
            }

            var shader = mat.shader;
            if (shader == null) return null;

            int count = shader.GetPropertyCount();
            for (int i = 0; i < count; i++)
            {
                if (shader.GetPropertyType(i) != UnityEngine.Rendering.ShaderPropertyType.Texture) continue;
                string name = shader.GetPropertyName(i);
                if (!IsAlbedoSlotName(name)) continue;

                slot = name;
                var t = mat.GetTexture(name);
                if (t != null) return t;
            }
            return null;
        }

        /// <summary>
        /// Lists the albedo-classified texture slots on this material and whether each is populated —
        /// the evidence line that makes a genuine miss readable and a future false positive obvious.
        /// </summary>
        private static string DescribeAlbedoSlots(Material mat)
        {
            if (mat == null) return "<null material>";
            var shader = mat.shader;
            if (shader == null) return "<null shader>";

            var parts = new List<string>();
            int count = shader.GetPropertyCount();
            for (int i = 0; i < count; i++)
            {
                if (shader.GetPropertyType(i) != UnityEngine.Rendering.ShaderPropertyType.Texture) continue;
                string name = shader.GetPropertyName(i);
                if (!IsAlbedoSlotName(name)) continue;
                parts.Add(name + "=" + (mat.GetTexture(name) != null ? "set" : "EMPTY"));
            }
            return parts.Count == 0
                ? "<none — this shader exposes no base-colour texture slot>"
                : string.Join(", ", parts);
        }
    }
}

// =============================================================================
// TripoSpriteOverlayRegression [tripo-sprite-overlay]
// -----------------------------------------------------------------------------
// Assembly: DeNelle.EditorRegression (references DeNelle.Core + DeNelle.Village).
//
// WO-1792. Pins that TripoMaterialFixer does NOT touch, and does NOT report on, a
// SpriteRenderer overlay — and that it still rebuilds real mesh slots.
//
// THE PROVING LINES (production Neon, read-only, 2026-09-16 UTC; 68 hits across 9
// distinct live player ids, every one in Main_Castle_Overworld, on the build
// 2026.09.16.371701 that 10 of that day's 17 attributable ids ran):
//
//   [Flow:TripoMatFix] NO ALBEDO on 'Jeweler' renderer 'Glass' slot 0:
//       material='Sprites-Default (URP)' shader='Universal Render Pipeline/Lit' tint=(1.00...
//   ...identically for renderer 'Icon' and renderer 'Rim', 14 hits each / 6 ids
//   [Flow:TripoMatFix] Jeweler: VERIFY UNTEXTURED - all 4 slot(s) on a URP shader,
//       but 3 slot(s) have NO albedo bound and no miss/fallback tint.
//
// HOW THE DATA NAMES THE CAUSE WITHOUT A GUESS. `material='Sprites-Default (URP)'` is
// TripoMaterialFixer's OWN srcName (`src.name + " (URP)"`), so the source material was
// Unity's builtin SpriteRenderer default, `Sprites-Default`. And "Rim"/"Glass"/"Icon"
// are exactly, and only, the three children InteractableSign.BuildQuad creates
// (InteractableSign.cs:199/201/206), each a SpriteRenderer, in that exact count. So the
// three "untextured" slots were never building art at all: they were the T-034 identity
// sign floating above the interactable.
//
// TWO DEFECTS IN ONE, BOTH CLOSED BY THE SKIP:
//   1. A FALSE error, 68x/day. A SpriteRenderer supplies its texture at DRAW time from
//      its `sprite`, never from a `_BaseMap` on its shared material, so GetAlbedo
//      correctly finds nothing. An error that is EXPECTED is an error nobody reads
//      (CLAUDE.md §12).
//   2. A REAL visual defect. The rebuild REPLACED `Sprites-Default` with an opaque
//      URP/Lit, stripping the sprite's alpha and shape — the sign's gold bezel, dark
//      glass backdrop and type icon all rendered as three plain white lit quads.
//
// CASES
//   1 [sprite-not-rebuilt]   A SpriteRenderer under a fixer host keeps its own material.
//   2 [mesh-still-rebuilt]   The mesh sibling IS rebuilt to URP/Lit. Without this, case 1
//                            would also pass for a Run() that no-op'd entirely — a suite
//                            that cannot fail is worse than no suite.
//   3 [one-predicate]        `IsSpriteOverlay` is defined ONCE and called from BOTH the
//                            rebuild loop and the verify loop. If only the rebuild skipped,
//                            verify would see the sprite still on 'Sprites/Default',
//                            score isUrp==false, and PROMOTE today's Warn to a hard
//                            VERIFY FAILED. If only verify skipped, the slot would still be
//                            mutated with nothing naming it (§12 stripped instrumentation).
//   4 [skip-is-named]        The skip emits a FlowTrace line naming the renderer + reason.
//                            AC2: a legitimately untextured renderer is classified BY NAME.
//   5 [scope-not-widened]    The skip covers SpriteRenderer ONLY. ParticleSystem/Line/Trail
//                            are the same CLASS of concern but NO capture shows one under a
//                            fixer host, so widening the skip would be a guess (§11B). Pinned
//                            so nobody widens it silently — and the fixer instead instruments
//                            non-mesh rebuilds so the next capture settles it with data.
//   6 [sign-quads-are-sprites] InteractableSign still builds Rim/Glass/Icon as SpriteRenderers.
//                            The moment the sign becomes MeshRenderers, the skip above stops
//                            covering it and the 68-hit false error returns — this case is the
//                            tripwire for that, so the fix cannot rot silently.
//
// Markers: TRIPO_SPRITE_OVERLAY_OK / TRIPO_SPRITE_OVERLAY_FAIL.
// Standalone: run-unity-method DeNelle.Editor.TripoSpriteOverlayRegression.RunAll
// WIRED into DataRegression.RunAll as [tripo-sprite-overlay] by the lead, 2026-09-17, so it
// counts toward REGRESSION_OK <n>/<n>. (This header previously said wiring was "left to the
// committer" — true when written, stale the moment it landed. Read DataRegression.cs, not this
// line, for whether it is registered.)
// =============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace DeNelle.Editor
{
    /// <summary>Oracle for the WO-1792 sprite-overlay exclusion in TripoMaterialFixer.</summary>
    public static class TripoSpriteOverlayRegression
    {
        private const string FixerSrc = "Assets/_Modules/Core/TripoMaterialFixer.cs";
        private const string SignSrc  = "Assets/_Modules/Village/Buildings/InteractableSign.cs";

        // ⛔ THE REBUILD TARGET, EXACTLY — never a 'Universal Render Pipeline/' family prefix.
        // TripoMaterialFixer rebuilds to `Shader.Find("Universal Render Pipeline/Lit")`
        // (TripoMaterialFixer.cs:262) and nothing else, so THIS string is the only sound
        // signature of "the rebuild touched this slot".
        //
        // ⚠ WHY THIS CONST EXISTS — A FALSE FAILURE THIS SUITE ITSELF PRODUCED (2026-09-17).
        // Case 1 originally asked `StartsWith("Universal Render Pipeline/")`. In a URP project the
        // BUILTIN SpriteRenderer material is ALREADY on a URP shader —
        // 'Universal Render Pipeline/2D/Sprite-Unlit-Default' — which starts with that prefix. So
        // the test reported the sprite as "rebuilt" while its material was provably UNTOUCHED: the
        // lead's combined-tree run printed the before and after shader as the SAME string and the
        // case still failed, which reads as "the fix is broken" when the fix was working. A prefix
        // test cannot tell an untouched URP sprite material from one rebuilt to URP/Lit.
        //
        // This is the same trap TripoMaterialFixer's own verify comment names in
        // VerifyAllRenderersUrp ("THE SHADER IS NOT THE WHOLE ANSWER" / the fix verified the wrong
        // property and reported success) — pointed the other way, at a false FAILURE instead of a
        // false pass. MATERIAL IDENTITY is the primary signal; the exact shader name is the belt.
        private const string UrpLitShader = "Universal Render Pipeline/Lit";

        /// <summary>Batchmode entry: writes the OK/FAIL marker, exits 1 on failure.</summary>
        public static void RunAll()
        {
            bool ok;
            string reason;
            try
            {
                ok = Run(out reason);
            }
            catch (Exception ex)
            {
                ok = false;
                reason = "threw " + ex.GetType().Name + ": " + ex.Message;
            }

            Debug.Log((ok ? "TRIPO_SPRITE_OVERLAY_OK " : "TRIPO_SPRITE_OVERLAY_FAIL ") + reason);
            if (!ok && Application.isBatchMode) EditorApplication.Exit(1);
        }

        /// <summary>
        /// DataRegression-shaped contract. True when every case passes; <paramref name="reason"/>
        /// always carries a human-readable summary. Never throws — an unexpected exception is
        /// folded into a failure by RunAll.
        /// </summary>
        public static bool Run(out string reason)
        {
            var failures = new List<string>();
            var log = new StringBuilder();

            RunBehaviourCases(failures, log);
            RunSourceCases(failures, log);

            if (failures.Count > 0)
            {
                reason = failures.Count + " failure(s): " + string.Join(" | ", failures);
                return false;
            }

            reason = "6/6 cases pass — " + log.ToString().TrimEnd(' ', ';');
            return true;
        }

        // ── Cases 1+2: build the real object graph and invoke the real Run() ──────────
        // Edit-mode invocation precedent: OwnerCastleRuntimeProof.cs:97-99 (reset the private
        // `_ran` guard, then Invoke the private `Run`). Start() never fires in edit mode, so
        // driving Run() directly is the ONLY way to prove the rebuild's real behaviour here
        // rather than re-asserting a copy of its rules.
        private static void RunBehaviourCases(List<string> failures, StringBuilder log)
        {
            GameObject host = null;
            Material sourceMat = null;
            Texture2D sourceTex = null;
            try
            {
                Shader lit = Shader.Find(UrpLitShader);
                if (lit == null)
                {
                    failures.Add("[urp-shader-missing] Shader.Find('" + UrpLitShader + "') returned null in the " +
                                 "editor, so TripoMaterialFixer.Run would bail at its own hard-fail guard and this " +
                                 "suite could prove nothing about the rebuild");
                    return;
                }

                host = new GameObject("Jeweler");

                // The real mesh slot. Deliberately started on a NON-URP shader so a rebuild is
                // observable; falls back to URP/Lit if 'Standard' is absent, and case 2 then still
                // proves Run() executed by the material INSTANCE changing.
                var meshChild = new GameObject("JewelerBody");
                meshChild.transform.SetParent(host.transform, false);
                var mr = meshChild.AddComponent<MeshRenderer>();
                meshChild.AddComponent<MeshFilter>();
                Shader legacy = Shader.Find("Standard") ?? lit;
                sourceMat = new Material(legacy);
                sourceMat.name = "JewelerStone";
                // ⚠ THE ALBEDO IS BOUND ON PURPOSE, AND IT IS NOT COSMETIC. An untextured mesh slot
                // makes VerifyAllRenderersUrp hit its `rendersPureWhite` branch and emit
                // FlowTrace.Fail (NO ALBEDO … Tint is WHITE) plus the VERIFY UNTEXTURED Fail — i.e.
                // Debug.LogError lines, from INSIDE a gate run. In this repo error-level lines feed
                // break-log.jsonl (BreakCaptureHarness) and run-unity-method.ps1's log scan, so a
                // green marker would ship with red noise under it and could raise a false F8 capture
                // on a seat. Binding a 1x1 texture also makes case 2 the REALISTIC assertion: a
                // textured mesh slot stays textured across the URP/Lit rebuild.
                sourceTex = new Texture2D(1, 1, TextureFormat.RGBA32, false);
                sourceTex.name = "JewelerStoneAlbedo";
                sourceTex.SetPixel(0, 0, new Color(0.6f, 0.5f, 0.4f, 1f));
                sourceTex.Apply();
                sourceMat.mainTexture = sourceTex;
                mr.sharedMaterial = sourceMat;

                // The InteractableSign quad: a SpriteRenderer carrying Unity's builtin
                // 'Sprites-Default' material, exactly as InteractableSign.BuildQuad leaves it.
                var spriteChild = new GameObject("Glass");
                spriteChild.transform.SetParent(host.transform, false);
                var sr = spriteChild.AddComponent<SpriteRenderer>();
                Material spriteMatBefore = sr.sharedMaterial;
                string spriteShaderBefore = (spriteMatBefore != null && spriteMatBefore.shader != null)
                    ? spriteMatBefore.shader.name : "<null>";

                var fixerType = typeof(DeNelle.Core.TripoMaterialFixer);
                var fixer = host.AddComponent<DeNelle.Core.TripoMaterialFixer>();
                const BindingFlags Priv = BindingFlags.Instance | BindingFlags.NonPublic;
                var ranField = fixerType.GetField("_ran", Priv);
                var runMethod = fixerType.GetMethod("Run", Priv);
                if (ranField == null || runMethod == null)
                {
                    failures.Add("[fixer-seam-renamed] TripoMaterialFixer no longer exposes the private '_ran' " +
                                 "field and/or 'Run' method this suite drives (the OwnerCastleRuntimeProof " +
                                 "precedent) — the behaviour cases cannot run, so the WO-1792 skip is unpinned");
                    return;
                }
                ranField.SetValue(fixer, false);
                runMethod.Invoke(fixer, null);

                // ── 1 [sprite-not-rebuilt] ───────────────────────────────────────────
                Material spriteMatAfter = sr.sharedMaterial;
                string spriteShaderAfter = (spriteMatAfter != null && spriteMatAfter.shader != null)
                    ? spriteMatAfter.shader.name : "<null>";
                // PRIMARY SIGNAL: material IDENTITY. A rebuild always assigns a different Material
                // instance (GetOrCreateSharedMaterial hands back a cached/new `new Material(lit)`,
                // never the builtin sprite material), so identity alone decides this. The exact
                // shader name is the belt, and it must be the REBUILD TARGET — see UrpLitShader for
                // the false failure a family-prefix test produced here.
                bool sameSpriteMat = spriteMatAfter != null && spriteMatBefore != null
                    && spriteMatAfter.GetInstanceID() == spriteMatBefore.GetInstanceID();
                bool spriteRebuilt = !sameSpriteMat
                    || string.Equals(spriteShaderAfter, UrpLitShader, StringComparison.Ordinal);
                if (spriteRebuilt)
                    failures.Add("[sprite-not-rebuilt] TripoMaterialFixer TOUCHED a SpriteRenderer's material: " +
                                 "shader before='" + spriteShaderBefore + "' after='" + spriteShaderAfter + "', " +
                                 "same material instance=" + sameSpriteMat + ". Rebuilding a sprite overlay strips " +
                                 "the sprite's alpha and paints the InteractableSign Rim/Glass/Icon plate as three " +
                                 "white lit quads AND emits the 68-hits-a-day false NO ALBEDO line (WO-1792). " +
                                 "Check IsSpriteOverlay is still consulted in the REBUILD loop before any material " +
                                 "swap, not only in the verify loop");
                else log.Append("[sprite-not-rebuilt] ok (same instance, kept " + spriteShaderBefore + "); ");

                // ── 2 [mesh-still-rebuilt] — guards a vacuous pass of case 1 ─────────
                Material meshMatAfter = mr.sharedMaterial;
                string meshShaderAfter = (meshMatAfter != null && meshMatAfter.shader != null)
                    ? meshMatAfter.shader.name : "<null>";
                // Assert the EXACT rebuild target, not the family prefix: a prefix test would also
                // accept a mesh left on some other URP shader, which is not what the rebuild does.
                if (!string.Equals(meshShaderAfter, UrpLitShader, StringComparison.Ordinal))
                    failures.Add("[mesh-still-rebuilt] the MESH slot ended on shader '" + meshShaderAfter +
                                 "', not the rebuild target '" + UrpLitShader + "' — the rebuild did not run at " +
                                 "all, so case 1 passed vacuously and structures would render on their unusable " +
                                 "source shaders");
                else if (meshMatAfter != null && meshMatAfter.GetInstanceID() == sourceMat.GetInstanceID())
                    failures.Add("[mesh-still-rebuilt] the MESH slot still references its ORIGINAL source material " +
                                 "instance — Run() skipped the mesh renderer too, so the WO-1792 skip is too wide");
                else log.Append("[mesh-still-rebuilt] ok (" + meshShaderAfter + "); ");
            }
            catch (Exception ex)
            {
                failures.Add("[behaviour-threw] driving TripoMaterialFixer.Run in edit mode threw " +
                             ex.GetType().Name + ": " + ex.Message);
            }
            finally
            {
                if (host != null) UnityEngine.Object.DestroyImmediate(host);
                // The transient source material + texture are NOT owned by the GameObject, so
                // destroying the host leaks them into the editor session. Clean both up explicitly.
                if (sourceMat != null) UnityEngine.Object.DestroyImmediate(sourceMat);
                if (sourceTex != null) UnityEngine.Object.DestroyImmediate(sourceTex);
            }
        }

        // ── Cases 3-6: source invariants the behaviour cases cannot see ───────────────
        private static void RunSourceCases(List<string> failures, StringBuilder log)
        {
            // ⛔ THE FIXTURE'S EXISTENCE IS ASSERTED, NOT ASSUMED — and the shape below is
            // deliberate (HollowPassScanner arm D, `D-vacuous-against-absent-fixture`).
            //
            // This method originally read BOTH sources and then wrapped every case in
            // `if (fixer != null) { ... }` / `if (sign != null) { ... }`. The scanner flagged it
            // and the scanner was RIGHT as a structural matter: every assertion in the method
            // hung off a positive-existence guard, so a reader of this method could not see any
            // floor. `ReadSource` did record a `[source-missing]` failure, so it would not
            // actually have greened — but "it happens to fail via a side effect two calls away"
            // is exactly the reasoning that let RaidCooldownRegression case 5 measure nothing
            // while reporting green on 2026-08-21.
            //
            // So: the PRODUCER records the miss and we return IMMEDIATELY on it — the shape the
            // scanner documents as the sanctioned exoneration (`var src = ReadCode(path,
            // failures); if (src == null) return;`). Every case below then sits at method depth,
            // unconditionally, with no existence guard over it at all. An absent source is a LOUD
            // FAILURE, never a silent skip. ⚠ Do NOT re-wrap these cases in a null guard to
            // "check both files even if one is missing": a missing source here is already fatal
            // to the verdict, and the wrap is the defect.
            string fixer = ReadSource(FixerSrc, failures);
            if (fixer == null) return;   // [source-missing] recorded by ReadSource one line above

            // ── 3 [one-predicate] ────────────────────────────────────────────────
            int defs = CountOccurrences(fixer, "private static bool IsSpriteOverlay");
            int calls = CountOccurrences(fixer, "IsSpriteOverlay(r)");
            if (defs != 1)
                failures.Add("[one-predicate] found " + defs + " definition(s) of IsSpriteOverlay in " +
                             FixerSrc + ", expected exactly 1 — two copies of the predicate is the drift this " +
                             "single method exists to prevent");
            else if (calls < 2)
                failures.Add("[one-predicate] IsSpriteOverlay(r) is called " + calls + " time(s), expected at " +
                             "least 2 (the rebuild loop AND the verify loop). If only the rebuild skips, verify " +
                             "sees the sprite still on Sprites/Default, scores isUrp==false and PROMOTES today's " +
                             "Warn to a hard VERIFY FAILED; if only verify skips, the slot is still mutated with " +
                             "nothing naming it");
            else log.Append("[one-predicate] ok (1 def, " + calls + " calls); ");

            // ── 4 [skip-is-named] ────────────────────────────────────────────────
            if (fixer.IndexOf("SKIPPED sprite overlay on", StringComparison.Ordinal) < 0)
                failures.Add("[skip-is-named] the sprite-overlay skip no longer emits a FlowTrace line naming " +
                             "the renderer and the reason. WO-1792 AC2: a renderer that is legitimately " +
                             "untextured must be classified BY NAME, or the skip is a silent suppression");
            else log.Append("[skip-is-named] ok; ");

            // ── 5 [scope-not-widened] ────────────────────────────────────────────
            bool widened = fixer.IndexOf("IsSpriteOverlay(Renderer r) => r is SpriteRenderer;",
                                         StringComparison.Ordinal) < 0;
            if (widened)
                failures.Add("[scope-not-widened] IsSpriteOverlay is no longer exactly 'r is SpriteRenderer'. " +
                             "SpriteRenderer is the ONE type a capture proved (68 hits / 9 live ids, " +
                             "2026-09-16). ParticleSystem/Line/Trail renderers are the same class of concern " +
                             "but NO capture shows one under a fixer host, so skipping them is a guess (§11B) — " +
                             "the fixer instruments them with FlowTrace.Once instead. Widen this only with a " +
                             "proving line, and update this case in the same change");
            else if (fixer.IndexOf("nonmesh-rebuilt-", StringComparison.Ordinal) < 0)
                failures.Add("[scope-not-widened] the FlowTrace.Once that NAMES a rebuilt non-mesh renderer is " +
                             "gone — that trace is the only thing turning the deliberately-unproven " +
                             "particle/line/trail question into data instead of an argument (§12)");
            else log.Append("[scope-not-widened] ok; ");

            string sign = ReadSource(SignSrc, failures);
            if (sign == null) return;    // [source-missing] recorded by ReadSource one line above

            // ── 6 [sign-quads-are-sprites] ───────────────────────────────────────
            bool isSprite = sign.IndexOf("AddComponent<SpriteRenderer>()", StringComparison.Ordinal) >= 0;
            bool hasRim   = sign.IndexOf("BuildQuad(\"Rim\"", StringComparison.Ordinal) >= 0;
            bool hasGlass = sign.IndexOf("BuildQuad(\"Glass\"", StringComparison.Ordinal) >= 0;
            bool hasIcon  = sign.IndexOf("BuildQuad(\"Icon\"", StringComparison.Ordinal) >= 0;
            if (!isSprite)
                failures.Add("[sign-quads-are-sprites] InteractableSign.BuildQuad no longer adds a " +
                             "SpriteRenderer — the WO-1792 skip keys on SpriteRenderer, so the sign's quads are " +
                             "back inside TripoMaterialFixer's rebuild and the 68-hit-a-day false NO ALBEDO " +
                             "line returns along with the three white plates");
            else if (!(hasRim && hasGlass && hasIcon))
                failures.Add("[sign-quads-are-sprites] InteractableSign no longer builds all three of " +
                             "Rim/Glass/Icon (rim=" + hasRim + " glass=" + hasGlass + " icon=" + hasIcon +
                             ") — those are the exact three renderer names in the WO-1792 proving lines, so " +
                             "this suite's subject and the captured evidence have diverged");
            else log.Append("[sign-quads-are-sprites] ok; ");
        }

        private static int CountOccurrences(string haystack, string needle)
        {
            int n = 0, i = 0;
            while ((i = haystack.IndexOf(needle, i, StringComparison.Ordinal)) >= 0) { n++; i += needle.Length; }
            return n;
        }

        /// <summary>Read a source file, recording a failure (and returning null) if it is gone.</summary>
        private static string ReadSource(string path, List<string> failures)
        {
            if (!File.Exists(path))
            {
                failures.Add("[source-missing] '" + path + "' does not exist — this suite cannot prove the " +
                             "WO-1792 sprite-overlay exclusion is wired, and a suite that cannot fail is worse " +
                             "than no suite");
                return null;
            }
            return File.ReadAllText(path);
        }
    }
}

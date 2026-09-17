// =============================================================================
// HeroTextureLoader — Tier-1 Addressables seam for the per-hero BASECOLOR / normal
// textures that the runtime binds by explicit path (WO-545, sibling of
// HeroAssetLoader).
// -----------------------------------------------------------------------------
// PROBLEM this closes: HeroAssetLoader routes the hero FBX + controller through
// Addressables, but the RENDERED look comes from a separate set of atlases in the
// plain folder Resources/Heroes/Textures/* that several systems load DIRECTLY via
// Resources.Load<Texture2D>("Heroes/Textures/<name>") and PAINT onto the body:
//   • HeroBodySwapper.ApplyExtractedTexture  (the playable hero — Knight basecolor+normal)
//   • StoryCompanionInjector.BindClassDiffuse (roster companions)
//   • TripoMaterialFixer                      (ATB hero + enemies, fallback atlas)
// If Heroes/Textures leaves Resources (so the ~84 MB stops shipping in WebGL.data)
// those bare Resources.Load calls return null → the hero renders flat/grey. This
// loader is the drop-in seam so those textures load from the Addressable bundle
// once grouped, and still fall back to Resources while they remain there.
//
// CONTRACT (identical shape to HeroAssetLoader, V1-SAFE): Addressables-FIRST,
// Resources-FALLBACK. The single argument is the Resources-relative path with NO
// extension (e.g. "Heroes/Textures/KnightArmored_basecolor"); it is used verbatim
// as BOTH the Addressable address AND the Resources.Load key (HeroAddressablesGrouper
// registers each texture at that exact address). Any path that is not registered
// (e.g. the enemy "Enemies/OrcTex/*" atlases, which this WO does NOT move) silently
// falls back to Resources.Load — so enemies and un-migrated content are unaffected.
//
// !! RESOLUTION ORDER SINCE WO-1701 (2026-09-17): WARM CACHE -> Addressables sync -> Resources.
// Identical to HeroAssetLoader's order, and for the identical reason.
//
// Synchronous surface (WaitForCompletion) so the existing sync call sites keep their
// shape. ⚠ WEBGL CAVEAT, CLOSED (WO-1701, 2026-09-14): WaitForCompletion is not merely
// slow on WebGL for an undownloaded bundle — Addressables THROWS unconditionally on the
// WebGL player, regardless of cache state ("WebGLPlayer does not support synchronous
// Addressable loading"). That is the exact same defect HeroAssetLoader.cs carried and
// closed at WO-1701 (2026-09-10/13): the captured session that forced this fix shows the
// identical throw one second after a successful download, for 'Enemies/OrcTex/
// Orc_Warrior_basecolor' via THIS file (HeroAssetLoader.cs uses the tag "HeroAssets" too,
// which is why the WO's own capture reads as one seam). The WaitForCompletion call below is
// now compiled ONLY off the WebGL player (mirrors HeroAssetLoader.cs's guard exactly,
// including UNITY_EDITOR so an editor session with the WebGL target selected still resolves
// through the AssetDatabase/local providers).
//
// !! WHY STOPPING THE THROW WAS NOT THE WHOLE FIX (WO-1701 second pass, 2026-09-17).
// Guarding the call stopped the red line and left the DEFECT: the hero's basecolor atlas
// lives in Assets/HeroContent/Textures/ in a REMOTE bundle with NO local copy
// (`Assets/Resources/Heroes/Textures` does not exist — checked at source 2026-09-17), so on
// WebGL the guarded branch is compiled out, Resources.Load returns null, and
// HeroBodySwapper.ApplyExtractedTexture falls back to a flat CLASS TINT. The player then
// gets the right BODY wearing no skin — a grey Mage instead of a naked Mage. Both are "the
// hero art fell back", which is this ticket's headline, so a guard alone could never have
// satisfied its section 4 acceptance.
// The close is the same shape as the prefab half: HeroContentPrewarmer LoadAssetAsync-es
// THIS CLASS'S atlases during the load screen and parks them, and the probe below reads that
// dictionary first — a dictionary read cannot block or throw on any platform.
// ⚠ ONLY THE CHOSEN CLASS'S ATLASES ARE WARMED, deliberately: Heroes/Textures/ is ONE shared
// 44 MB / 23-file bundle across every class (measured 2026-09-17; one PNG in it is 22 MB),
// and resident-loading all of it on a phone browser is the one platform least able to afford
// the heap. WarmableAddresses below is the authority on "this class's atlases", and it is
// THE SAME list HeroBodySwapper binds from — one list, built here, consumed there, so the
// prewarm can never hold a different string from the one the renderer later asks for
// (CLAUDE.md sections 2/5/16: a second hand-written copy is the bug, not the value in it).
// =============================================================================

using System.Collections.Generic;
using DeNelle.Core.Diagnostics;   // FlowTrace / Guard — §12 instrument the seam
using DeNelle.Core.State;         // HeroClass — the atlas map is keyed by the Core enum, not a string
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.ResourceManagement.ResourceLocations;

namespace DeNelle.Core
{
    /// <summary>
    /// Addressables-first / Resources-fallback loader for a hero atlas texture.
    /// Drop-in for <c>Resources.Load&lt;Texture2D&gt;(path)</c> where <c>path</c> is the
    /// Resources-relative, extension-less key (e.g. "Heroes/Textures/KnightArmored_basecolor").
    /// V1-safe: an unregistered address silently falls back to the shipping Resources copy.
    /// </summary>
    public static class HeroTextureLoader
    {
        // =====================================================================
        //  THE CLASS -> ATLAS AUTHORITY (WO-1701 second pass, 2026-09-17)
        // -----------------------------------------------------------------------------
        //  MOVED HERE FROM HeroBodySwapper.ApplyExtractedTexture, which is in the Village
        //  assembly and therefore invisible to HeroContentPrewarmer (Core). The prewarm has
        //  to know which atlases to hold; the renderer has to bind the same ones. Leaving the
        //  map in Village and copying it into Core would be exactly the duplicated state that
        //  CLAUDE.md sections 2/5/16 each describe going stale — so the map lives in ONE place
        //  (here, next to the loader that resolves it) and Village consumes it.
        //  The long per-class provenance comments (which model, which bake, why this atlas and
        //  not that one) deliberately STAY at the HeroBodySwapper call site: they are the art
        //  history of the body, and moving them would strand them from the mesh they explain.
        // =====================================================================

        /// <summary>
        /// The basecolor atlas address for <paramref name="cls"/>'s PLAYABLE body, or null when
        /// that class has none (the caller then falls back to a class tint, which is the
        /// long-standing design — see HeroBodySwapper.ApplyExtractedTexture).
        /// <para>These strings were the literals inside HeroBodySwapper's switch until
        /// 2026-09-17; the values are unchanged, only their home moved.</para>
        /// </summary>
        public static string BasecolorAddressFor(HeroClass cls)
        {
            // THE MAP SUFFIX COMES FROM EnemyArtPaths, NOT FROM A LITERAL HERE (2026-09-17).
            // AssetRootsRegression rule 3 [art-ledger] is a RATCHET: the set of files allowed to
            // re-type "_basecolor" was FROZEN on 2026-08-21 and may shrink, never grow. Moving the
            // hero atlas map out of HeroBodySwapper.cs (which IS on that ledger) into this new file
            // carried the token to a home the ratchet does not permit, and the suite failed - the
            // gate doing exactly its job. Asking EnemyArtPaths keeps the token in its one declared
            // home instead of buying this file a ledger entry, which would have grown the ratchet.
            //
            // ⚠ AND IT IS ONLY THE *SUFFIX TOKEN* THAT IS BORROWED - never a path. EnemyArtPaths'
            // path builders (ResourceCandidates / AtlasAssetCandidates / EmbeddedFolder) compose
            // under AssetRoots.EnemyContent, i.e. the ENEMY tree; calling one of them here would
            // return an enemy path for a hero atlas and break every hero's skin. "_basecolor" is a
            // project-wide FILENAME convention that EnemyArtPaths happens to declare (its own
            // ledger note at AssetRootsRegression.cs:78-82 says as much: several non-enemy trees
            // share this vocabulary). The hero PREFIX and the per-class STEMS stay owned here.
            string suffix = EnemyArtPaths.SuffixFor(EnemyArtMap.BaseColor);
            switch (cls)
            {
                case HeroClass.Mage:   return HeroContentPrewarmer.TexAddrPrefix + "mage" + suffix;
                case HeroClass.Knight: return HeroContentPrewarmer.TexAddrPrefix + "KnightArmored" + suffix;
                case HeroClass.Ranger: return HeroContentPrewarmer.TexAddrPrefix + "ranger" + suffix;
                case HeroClass.Cleric: return HeroContentPrewarmer.TexAddrPrefix + "Cleric" + suffix;
                default:               return null;
            }
        }

        /// <summary>
        /// The tangent-space NORMAL map for <paramref name="cls"/>, or null when the class ships
        /// base-color only. Knight-only today: the armored Tripo body is the one class whose
        /// export carries a UV-matched normal, and the other classes deliberately keep _BumpMap
        /// cleared (binding a stray import-bound normal is what read as "speckled" colour).
        /// <para>Suffix from EnemyArtPaths for the same reason as
        /// <see cref="BasecolorAddressFor"/> - read the note there. "_normal" is not currently one
        /// of AssetRootsRegression's gated tokens, so this line is compliance by CONVENTION rather
        /// than under duress: a basecolor composed from the authority beside a hand-typed normal
        /// would read as an oversight and invite the next author to un-do both.</para>
        /// </summary>
        public static string NormalAddressFor(HeroClass cls) =>
            cls == HeroClass.Knight
                ? HeroContentPrewarmer.TexAddrPrefix + "KnightArmored" + EnemyArtPaths.SuffixFor(EnemyArtMap.Normal)
                : null;

        /// <summary>
        /// Every atlas address the playable body of <paramref name="heroClassName"/> can be asked
        /// for — i.e. the exact set HeroContentPrewarmer must hold in its warm cache for a WebGL
        /// player to render this hero in its own skin rather than a flat class tint. ENUMERATE
        /// THIS; never re-type an atlas name at a call site.
        /// <para><paramref name="heroClassName"/> is the plain class name as the save stores it
        /// (<c>HeroClassOpt.ToString()</c>, which is what HeroContentPrewarmer.Prewarm receives).
        /// An unparseable or unknown name yields NOTHING rather than throwing — a brand-new class
        /// with no atlas authored yet must not break the load screen.</para>
        /// </summary>
        public static IEnumerable<string> WarmableAddresses(string heroClassName)
        {
            if (string.IsNullOrEmpty(heroClassName)) yield break;
            if (!System.Enum.TryParse<HeroClass>(heroClassName, true, out HeroClass cls)) yield break;

            string basecolor = BasecolorAddressFor(cls);
            if (!string.IsNullOrEmpty(basecolor)) yield return basecolor;

            string normal = NormalAddressFor(cls);
            if (!string.IsNullOrEmpty(normal)) yield return normal;
        }

        /// <summary>
        /// Load a hero atlas texture at <paramref name="resourcesRelativePath"/> (also the
        /// Addressable address). Addressables when that address is registered, else Resources.Load.
        /// Guarded — a throw at any step degrades to the Resources fallback so the caller never
        /// gets a hard exception; a total miss returns null (callers already handle a null atlas).
        /// </summary>
        /// <param name="optional">True when the caller treats a total miss as an EXPECTED,
        /// intentional state (e.g. the pet basecolor PNGs purged for size in 2774fb50 —
        /// the tint/extracted-material fallback is the design, not a failure). A miss then
        /// logs FlowTrace.Step instead of Fail, so it never lands in the break-log.</param>
        public static Texture2D Load(string resourcesRelativePath, bool optional = false)
        {
            if (string.IsNullOrEmpty(resourcesRelativePath)) return null;

            string address = resourcesRelativePath;
            Texture2D result = null;

            // -- 0. WARM CACHE FIRST (WO-1701, 2026-09-17) ---------------------------
            // A dictionary probe and nothing else: no catalog lookup, no handle, no wait, no
            // possibility of a throw, on ANY platform. On WebGL it is the ONLY branch that can
            // succeed for a remote atlas, because the block below is compiled out there and
            // Heroes/Textures/* has no Resources copy left to fall back to.
            if (HeroContentPrewarmer.TryGetWarm(address, out Texture2D warm) && warm != null)
            {
                FlowTrace.Step("HeroAssets",
                    $"warm-cache HIT texture '{address}' -> '{warm.name}' - served from the " +
                    "HeroContentPrewarmer dictionary, no Addressables call made.");
                return warm;
            }

            // ── Addressables FIRST (WO-1187) ────────────────────────────────────────
            // ⚠ Same inverted-order bug as HeroAssetLoader carried: this block used to sit
            // BELOW the Resources.Load call, so a grouped atlas was never consulted and the
            // 44 MB of Heroes/Textures kept shipping locally. Do not reorder.
            bool wasRegistered = false;
            Guard.Try("HeroAssets", $"Addressables resolve texture '{address}'", () =>
            {
                wasRegistered = AddressableRegistered(address);
                if (!wasRegistered) return; // non-hero / deliberately-local path — Resources below

                // WO-1701: compiled ONLY off the WebGL player. UnityEngine.AddressableAssets THROWS
                // "WebGLPlayer does not support synchronous Addressable loading" here regardless of
                // cache state (captured 2026-09-10, header). UNITY_EDITOR stays in the condition for
                // the same reason as HeroAssetLoader.cs: the Editor resolves through the
                // AssetDatabase/local providers and never runs the WebGL player. On WebGL, falling
                // through leaves result null and the Resources fallback below runs exactly as it does
                // for an unregistered address.
#if !UNITY_WEBGL || UNITY_EDITOR
                var handle = Addressables.LoadAssetAsync<Texture2D>(address);
                result = handle.WaitForCompletion();
                // Intentionally NOT released — parity with Resources.Load (never unloads); the atlas
                // must outlive the material it is painted onto. Tier-2 adds ref-counted release.
                if (result != null)
                    FlowTrace.Step("HeroAssets", $"Addressables HIT texture '{address}' -> '{result.name}'.");
#endif
            });
            if (result != null) return result;

            // ── Resources fallback (only for atlases that deliberately stay local) ──
            Guard.Try("HeroAssets", $"Resources.Load texture {address}", () =>
            {
                result = Resources.Load<Texture2D>(address);
            });

            if (wasRegistered)
                FlowTrace.Warn("HeroAssets",
                    $"Addressables texture '{address}' IS registered but resolved null - the prewarm did not " +
                    "hold this address either. CAUSE NOT DETERMINED FROM HERE. Candidates, in the order worth " +
                    "checking: (1) this platform refuses the synchronous load - WebGL always does, and the sync " +
                    "branch above is compiled out there, so a WebGL miss means the warm cache did not hold it " +
                    "(either HeroTextureLoader.WarmableAddresses does not list this address for the chosen " +
                    "class, or the warm load itself failed and said so under [Flow:HeroPrewarm]); (2) the " +
                    "bundle for THIS content build was never pushed (CLAUDE.md section 16 - names are " +
                    "content-hashed, so a previous push does not cover this build); (3) nothing at this " +
                    $"address provides Texture2D. Fell back to Resources.Load -> " +
                    $"{(result == null ? "ALSO NULL" : result.name)}.");
            else
                FlowTrace.Step("HeroAssets",
                    $"no Addressables entry for texture '{address}' (expected on a non-hero path) — using Resources.Load.");

            if (result == null)
            {
                if (optional)
                    // Owner F8 2026-07-02 (flame-pup): the pet basecolor PNGs were PURGED for size
                    // (2774fb50; flame-pup.png was a 16.4MB LFS asset) and the pets render from their
                    // extracted .fbm materials — a miss here is the intended state, not a break.
                    FlowTrace.Step("HeroAssets",
                        $"optional texture '{address}' absent (purged-for-size asset) — caller's tint fallback is intentional.");
                else
                    FlowTrace.Fail("HeroAssets",
                        $"hero texture '{address}' not found: the prewarm did not hold it, Addressables did not " +
                        "return it, and Resources has no copy - caller falls back to a flat class TINT, i.e. the " +
                        "player sees the right body wearing no skin (WO-1701's own symptom, one layer out).");
            }
            return result;
        }

        /// <summary>
        /// True when the Addressables catalog has at least one Texture2D location for
        /// <paramref name="address"/>. Silent (no error spam) on the common miss.
        /// </summary>
        private static bool AddressableRegistered(string address)
        {
            try
            {
                foreach (var locator in Addressables.ResourceLocators)
                {
                    if (locator.Locate(address, typeof(Texture2D), out IList<IResourceLocation> locations) &&
                        locations != null && locations.Count > 0)
                        return true;
                }
            }
            catch
            {
            }
            return false;
        }
    }
}

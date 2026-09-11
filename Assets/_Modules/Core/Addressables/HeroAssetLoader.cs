// =============================================================================
// HeroAssetLoader — Tier-1 Addressables seam for per-hero assets (WO-545).
// -----------------------------------------------------------------------------
// docs/DATA_ARCHITECTURE_DECISION_2026-06-27.md, Tier-1: heroes currently ALL
// ship in Resources/Heroes (~138 MB always in the build). This helper is the
// drop-in seam so a hero can be pulled per-selection via Addressables instead.
//
// ⚠ HISTORY — WHY THE ORDER IS COMMENTED SO HARD (WO-1187, 2026-09-03):
// this header declared "Addressables-FIRST" from day one while the CODE called
// Resources.Load first. So the hero art could be grouped into Addressables and
// NOTHING would change: the local copy won every resolve, the 100 MB kept shipping,
// and the move would have looked like it simply did not work. The order is now
// correct and is PINNED by Assets/Editor/Regression/HeroRemoteContentRegression.cs.
// Never trust this header again without reading Load<T> — that is the lesson.
//
// CONTRACT (NON-NEGOTIABLE): Addressables-FIRST, Resources-FALLBACK.
//   • Build the per-hero address "Heroes/<slug>" (same scheme for the prefab and
//     the controller — the asset TYPE disambiguates the two locations sharing the
//     address; the loader queries type-filtered).
//   • If that address is REGISTERED in the Addressables catalog, load it and use it.
//   • Otherwise fall back to Resources.Load<T>("Heroes/" + slug). Post-WO-1187 that
//     fallback covers ONLY what deliberately stays local — Heroes/Props/*,
//     Heroes/Emotes/*, Heroes/SC_*.prefab. The hero BODIES (fbx + .fbm + controller)
//     and Heroes/Textures/* now live in Assets/HeroContent/ in REMOTE R2 bundles and
//     have NO local copy: for those, Addressables is the only path that can succeed.
//
// !! RESOLUTION ORDER SINCE WO-1701 (2026-09-10): WARM CACHE -> Addressables sync -> Resources.
//
// STEP 0, THE WARM CACHE, IS THE ONLY BRANCH WEBGL CAN TAKE FOR A REMOTE HERO.
// HeroContentPrewarmer already downloads this hero's bundle on the post-class-select load
// screen; since WO-1701 it ALSO LoadAssetAsync-es every address this loader will later ask
// for (see WarmableAddresses below - one list, built here, consumed there) and keeps the
// loaded objects for the process. A warm hit is a dictionary read on every platform: it
// cannot block, cannot throw, and needs no Addressables call at all.
//
// Synchronous surface (WaitForCompletion) so the existing sync call sites keep their
// shape (AtbCombatantSwapper, HeroBodySwapper legacy path, StoryCompanionInjector).
// That branch is now compiled ONLY when the target is not WebGL, or when we are in the
// Editor (whose AssetDatabase/local providers resolve synchronously and never run the
// WebGL player). It is NOT the load-bearing path any more - the warm cache is.
//
// STOP - CAPTURED DATA THAT FORCED THE GUARD (Seeker, Pi Browser, 2026-09-10, WO-1701):
//   21:54:23Z [Flow:HeroPrewarm] 'Mage' art downloaded and cached on attempt 1 - Ready.
//   21:54:24Z error: [Flow:HeroAssets] Addressables resolve 'Heroes/Mage' (GameObject)
//             FAILED: Exception: WebGLPlayer does not support synchronous Addressable
//             loading. Please do not use WaitForCompletion on the WebGLPlayer platform.
//   21:54:24Z [Flow:HeroBody] class=Mage slug=Mage - kicking Blink base load 'hero/base/HumanMale'.
// The bundle WAS resident one second earlier. Addressables refuses WaitForCompletion on
// WebGLPlayer REGARDLESS of cache state; Guard.Try logged and swallowed the throw, the
// null fell through to a Resources copy the remote migration had deleted, and the player
// got the naked Blink placeholder body. Caching harder would never have fixed it - only
// not calling WaitForCompletion does.
// Pinned by Assets/Editor/Regression/HeroAssetLoaderWebGlRegression.cs (both halves: the
// guard on every WaitForCompletion occurrence, and warm-cache-before-Addressables).
//
// We deliberately check LoadResourceLocationsAsync FIRST rather than blindly calling
// LoadAssetAsync on a possibly-unregistered key: in V1 NO hero address is registered,
// and a blind LoadAssetAsync on a missing key spams a red Addressables error on EVERY
// hero load. The locations probe is silent — so the clean V1 path stays clean.
//
// NOTE on handle lifetime: like Resources.Load (which never unloads), we do NOT release
// the asset handle — the loaded prefab/controller must outlive the instantiated hero.
// A future Tier-2 can add ref-counted release keyed by the spawned instance.
// =============================================================================

using System.Collections.Generic;
using DeNelle.Core.Diagnostics;   // FlowTrace / Guard — §12 instrument the seam (Step hit / Warn fallback)
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.ResourceManagement.ResourceLocations;

namespace DeNelle.Core
{
    /// <summary>
    /// Addressables-first / Resources-fallback loader for per-hero prefab + animator controller.
    /// Drop-in for <c>Resources.Load&lt;T&gt;("Heroes/" + slug)</c>. V1-safe: an unregistered
    /// address silently falls back to the shipping Resources copy.
    /// </summary>
    public static class HeroAssetLoader
    {
        /// <summary>Resources sub-path prefix + Addressable address prefix (shared scheme).</summary>
        public const string HeroAddrPrefix = "Heroes/";

        /// <summary>Load the per-hero body prefab (Addressables-first, Resources-fallback).</summary>
        public static GameObject LoadHeroPrefab(string slug) => Load<GameObject>(slug);

        /// <summary>Load the per-hero animator controller (Addressables-first, Resources-fallback).</summary>
        public static RuntimeAnimatorController LoadHeroController(string slug) => Load<RuntimeAnimatorController>(slug);

        /// <summary>
        /// THE address builder. One expression, used by <see cref="Load{T}"/> and by
        /// <see cref="WarmableAddresses"/>, so the prewarm can never warm a different string
        /// from the one this loader later asks for. (WO-1701: a second hand-written list is
        /// exactly the duplicated state CLAUDE.md sections 2/5/16 each describe going stale.)
        /// </summary>
        public static string AddressFor(string slug) =>
            string.IsNullOrEmpty(slug) ? null : HeroAddrPrefix + slug;

        /// <summary>
        /// Every (asset type, address) pair this loader can be asked for on behalf of
        /// <paramref name="slug"/> - i.e. the exact set HeroContentPrewarmer must hold in its warm
        /// cache for a WebGL player to render this hero. Derived from the two public entry points
        /// (<see cref="LoadHeroPrefab"/> -> GameObject, <see cref="LoadHeroController"/> ->
        /// RuntimeAnimatorController) and from <see cref="AddressFor"/>; ENUMERATE THIS, never
        /// re-type the addresses at the call site.
        /// <para>Both pairs share one address on purpose - the asset TYPE disambiguates the two
        /// catalog locations, which is why the warm cache is keyed by type AND address.</para>
        /// </summary>
        public static IEnumerable<KeyValuePair<System.Type, string>> WarmableAddresses(string slug)
        {
            string address = AddressFor(slug);
            if (string.IsNullOrEmpty(address)) yield break;

            yield return new KeyValuePair<System.Type, string>(typeof(GameObject), address);
            yield return new KeyValuePair<System.Type, string>(typeof(RuntimeAnimatorController), address);
        }

        /// <summary>
        /// Build the address "Heroes/&lt;slug&gt;", try Addressables when that address (of type
        /// <typeparamref name="T"/>) is registered, else fall back to Resources.Load. Guarded — a
        /// throw at any step degrades to the Resources fallback so the hero is never left assetless.
        /// </summary>
        private static T Load<T>(string slug) where T : Object
        {
            if (string.IsNullOrEmpty(slug)) return null;

            string address = AddressFor(slug);
            T result = null;

            // -- 0. WARM CACHE FIRST (WO-1701) ---------------------------------------
            // A dictionary probe and nothing else: no catalog lookup, no handle, no wait,
            // no possibility of a throw, on ANY platform. On WebGL it is the only branch
            // that can succeed for a remote hero, because the block below is compiled out
            // there (Addressables refuses a synchronous load on WebGLPlayer even when the
            // bundle is already cached - see the header's captured lines).
            if (HeroContentPrewarmer.TryGetWarm(address, out T warm) && warm != null)
            {
                string warmName = warm.name;
                FlowTrace.Step("HeroAssets",
                    $"warm-cache HIT '{address}' ({typeof(T).Name}) -> '{warmName}' - served from the " +
                    "HeroContentPrewarmer dictionary, no Addressables call made.");
                return warm;
            }

            // ── Addressables FIRST (WO-1187) ────────────────────────────────────────
            // ⚠ ORDER IS THE WHOLE POINT OF THIS METHOD. Until WO-1187 this block sat
            // BELOW the Resources.Load call, so the header's "Addressables-FIRST" contract
            // was a lie in code: the local Resources copy always won and the remote bundle
            // was never consulted. That made grouping the heroes into Addressables a
            // NO-OP — the 100 MB kept shipping and the CDN copy was dead weight.
            // Do not reorder these two blocks.
            bool wasRegistered = false;
            Guard.Try("HeroAssets", $"Addressables resolve '{address}' ({typeof(T).Name})", () =>
            {
                wasRegistered = AddressableRegistered<T>(address);
                if (!wasRegistered) return; // un-grouped asset (Props/, Emotes/, SC_*) — Resources below

                // The prewarm has already DOWNLOADED this hero's bundle on the post-class-select
                // load screen, so this resolves from cache. WaitForCompletion on an UNCACHED
                // remote bundle would stall the main thread for the length of the download,
                // which is why the prewarm gate blocks entry into the world instead.
                //
                // WO-1701: compiled ONLY off the WebGL player. UnityEngine.AddressableAssets
                // THROWS "WebGLPlayer does not support synchronous Addressable loading" here
                // regardless of cache state (captured 2026-09-10, header). UNITY_EDITOR is kept
                // in the condition because the Editor resolves through the AssetDatabase/local
                // providers and never runs the WebGL player, so an editor session with the WebGL
                // build target selected keeps a working hero instead of silently losing one.
                // ONLY this guarded block may contain that call - HeroAssetLoaderWebGlRegression
                // walks the #if stack over this file and fails on any occurrence outside it.
#if !UNITY_WEBGL || UNITY_EDITOR
                var handle = Addressables.LoadAssetAsync<T>(address);
                result = handle.WaitForCompletion();
                // Intentionally NOT released — the asset must outlive the spawned hero (parity with
                // Resources.Load, which never unloads). Tier-2 adds ref-counted release.
                if (result != null)
                    FlowTrace.Step("HeroAssets", $"Addressables HIT '{address}' -> '{result.name}' ({typeof(T).Name}).");
#endif
            });
            if (result != null) return result;

            // ── Resources fallback (only what DELIBERATELY stays local: Props/, Emotes/, SC_*) ──
            Guard.Try("HeroAssets", $"Resources.Load {address} ({typeof(T).Name})", () =>
            {
                result = Resources.Load<T>(address);
            });

            // §12 hygiene: separate a clean "never grouped, lives in Resources by design" (Step)
            // from "the catalog HAS this address but it did not resolve" (a real anomaly).
            //
            // WO-1701 CORRECTION - this line used to assert "the bundle is likely missing from
            // the CDN (never pushed)". On 2026-09-10 it printed that sentence one second after
            // [Flow:HeroPrewarm] logged the very same bundle as downloaded and cached, and while
            // R2 parity was green for WebGL. The loader CANNOT see why the resolve returned null;
            // asserting a cause it has not measured is the CLAUDE.md section 11B failure, and it
            // sent the first reader of that log hunting a push that had already happened.
            // It now names what it DOES know and lists the candidates as candidates.
            string fellBackTo = result == null ? "ALSO NULL" : result.name;
            if (wasRegistered)
                FlowTrace.Warn("HeroAssets",
                    $"Addressables '{address}' IS registered but resolved null - the prewarm did not hold " +
                    $"'{address}' ({typeof(T).Name}) either. CAUSE NOT DETERMINED FROM HERE. Candidates, in " +
                    "the order worth checking: (1) this platform refuses the synchronous load - WebGL always " +
                    "does, and the sync branch is compiled out there, so a WebGL miss means the warm cache " +
                    "did not hold it; (2) the bundle for THIS content build was never pushed (CLAUDE.md " +
                    "section 16 - names are content-hashed, so a previous push does not cover this build); " +
                    "(3) nothing at this address provides " + typeof(T).Name + ". Fell back to " +
                    $"Resources.Load('{address}') -> {fellBackTo}.");
            else
                FlowTrace.Step("HeroAssets",
                    $"no Addressables entry for '{address}' (expected for the deliberately-local Props/Emotes/SC_ " +
                    $"assets) — using Resources.Load(\"{HeroAddrPrefix}{slug}\").");

            if (result == null)
                FlowTrace.Fail("HeroAssets",
                    $"hero asset '{address}' ({typeof(T).Name}) not found: the prewarm did not hold '{address}', " +
                    "Addressables did not return it, and Resources has no copy - caller falls back (on the " +
                    "playable hero that fallback is the Blink base body, i.e. the placeholder the player sees).");
            return result;
        }

        /// <summary>
        /// True when the Addressables catalog has at least one location for <paramref name="address"/>
        /// providing type <typeparamref name="T"/>. Silent (no error spam) on the common V1 miss.
        /// Type-filtered so the prefab vs controller locations sharing the same address resolve apart.
        /// <para>PUBLIC since WO-1701 so HeroContentPrewarmer's warm pass uses THIS probe rather than
        /// a second copy of the same rule - a blind LoadAssetAsync on an unregistered key is what
        /// spams a red Addressables error on every hero load, and the warm pass asks for more keys
        /// than the loader does. It is a catalog lookup only: it starts no operation and cannot
        /// block, so it is safe on WebGL and stays OUTSIDE the platform guard above.</para>
        /// </summary>
        public static bool AddressableRegistered<T>(string address) where T : Object
        {
            try
            {
                foreach (var locator in Addressables.ResourceLocators)
                {
                    if (locator.Locate(address, typeof(T), out IList<IResourceLocation> locations) &&
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

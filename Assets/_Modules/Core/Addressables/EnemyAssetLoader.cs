// =============================================================================
// EnemyAssetLoader — Tier-1 Addressables seam for per-enemy assets.
// Sibling of HeroAssetLoader (WO-545) / StructureAssetLoader / VfxAssetLoader /
// AudioAssetLoader; identical contract, enemy address space.
// -----------------------------------------------------------------------------
// WHY THIS EXISTS: `Assets/Resources/Enemies` was ~539 MB and Unity FORCE-INCLUDES
// everything under a Resources/ folder in EVERY build, whether or not a single
// enemy in it is ever spawned. The content has since MOVED (Assets/EnemyContent/**,
// addressed as "Enemies/<slug>") and Assets/Resources/Enemies NO LONGER EXISTS —
// verified on disk 2026-08-20. That matters below: the Resources tier is now empty
// for enemies, so a miss here is a CAPSULE, not a silent downgrade.
//
// =============================================================================
// ⛔⛔ 2026-08-20 — THIS FILE CARRIED THE P0 THAT HUNG THE GAME. READ BEFORE EDITING.
//
// The structure seam deadlocked a Seeker device returning from a dungeon:
//   08-20 10:25:14.917 [Flow:VisualFactory] -> Skin('Structures/barracks')
//       ... HubStructureVisualInjector:OnSceneLoaded(Scene, LoadSceneMode)
//   ...and then NOTHING, ever again. Device clock reached 10:28:35 while the last
//   game log line was still 10:25:14.917 — three minutes of total silence from a
//   process that was alive and foregrounded, showing a stale frame.
//
// IT WAS NOT THE NETWORK: the owner pinged the R2 CDN from the hung device, 2/2
// packets, 0% loss, 31.5 ms. A TIMEOUT WOULD NOT HAVE HELPED and Addressables 2.9.1
// does not offer one: AsyncOperationBase.WaitForCompletion is literally
//     while (!InvokeWaitForCompletion()) { }
// (AsyncOperations/AsyncOperationBase.cs:171) — no timeout, no yield, no exit — while
// AssetBundleResource.WaitForCompletionHandler (AssetBundleProvider.cs:543)
// Thread.Sleep()s the MAIN THREAD on progress the player loop must drive. Blocked
// inside an engine callback the player loop cannot re-enter, the thread that would
// finish the operation is the thread waiting on it. Deadlock, not slowness. The full
// three-part proof, cited to file and line, is in EnemyContentWarmer.cs's header.
//
// ⛔ AND THIS FILE HAD THE SAME TWO CALLS, UNFIXED, ON A SECOND SEAM: one blocking
// wait on the asset load and one on the registration probe, both reachable from an
// enemy spawn — and spawns happen from wave callbacks and scene-entry paths. Same
// bug, different door. Both are gone.
//
// THE RULE THIS FILE NOW KEEPS, and the gate that enforces it:
//   ⛔ ZERO occurrences of the blocking call in this file. Not one, not under an #if.
//   Assets/Editor/Regression/EnemyLoadBoundedRegression.cs fails the build if a single
//   one reappears, and it blanks comments AND string literals first so it cannot match
//   its own tombstone. The one allowlisted site in the whole seam is
//   EnemyEditorSyncResolver.cs, which is entirely inside #if UNITY_EDITOR.
// =============================================================================
//
// CONTRACT: RESIDENT-FIRST, ON-DEMAND-PER-FAMILY, NEVER BLOCKING.
//   • The address is the extension-less, Resources-relative key used VERBATIM as
//     BOTH the Addressable address AND the (now-empty) Resources.Load key — e.g.
//     "Enemies/Skeleton_Minion", "Enemies/OrcHumanoid", "Enemies/Boss_Dragon".
//     Do NOT invent a second address scheme; the grouper registers these exact
//     strings (same rule as Hero/Structure/Vfx/Audio). The asset TYPE disambiguates
//     the prefab from the controller when two locations share an address.
//   • "Addressables-first" is served by the RESIDENT CACHE (EnemyContentWarmer.TryGet)
//     rather than by a synchronous load. Same precedence, same addresses — the
//     difference is that a miss now costs a frame of capsule instead of the whole game.
//   • A miss REQUESTS the asset asynchronously and returns null. The request pulls
//     THAT FAMILY'S bundle only (owner ruling 2026-08-20: "I want this broken down to
//     each family of enemy"), not the whole enemy payload.
//   • A miss is LOUD and it distinguishes the two failure kinds, because they need
//     different fixes: NOT-YET-DOWNLOADED (transient, re-skins itself) versus
//     GENUINELY-MISSING-ASSET (permanent, someone must ship the address).
//   • Callers must treat null as "no art YET" and RE-SKIN later. EnemyFactory does
//     exactly that via EnemyLateSkinner — a permanent capsule because the player
//     spawned an enemy two seconds early is not acceptable.
//
// NOTE on handle lifetime: the warmer retains every asset handle for the process, on
// purpose (see its header (B)) — releasing is what lets a raid load evict enemy art and
// put the next spawn back on the cold path. The LOCATION handle the editor probe opens
// is always released.
// =============================================================================

using System.Collections.Generic;
using DeNelle.Core.Diagnostics;   // FlowTrace / Guard — §12 instrument the seam
using UnityEngine;
using Object = UnityEngine.Object;
// ⚠ EnemyAssetLoader declares `public const string System` (the FlowTrace tag), which SHADOWS the
// `System` namespace inside the class — `System.Type` will not compile in there. Alias it once here.
using SysType = System.Type;

namespace DeNelle.Core
{
    /// <summary>
    /// Resident-first, non-blocking loader for per-enemy prefabs, animator controllers and
    /// prefab-borne components. Drop-in for <c>Resources.Load&lt;T&gt;("Enemies/" + key)</c>.
    /// <para>⛔ EVERY PATH IN HERE IS NON-BLOCKING BY CONSTRUCTION. See the file header.</para>
    /// </summary>
    public static class EnemyAssetLoader
    {
        /// <summary>FlowTrace system tag for every line this seam emits. Shared with
        /// EnemyContentWarmer on purpose — one tag for one story.</summary>
        public const string System = "EnemyAssets";

        /// <summary>Resources sub-path prefix + Addressable address prefix (shared scheme).</summary>
        public const string EnemyAddrPrefix = "Enemies/";

        /// <summary>Addresses whose all-paths-missed failure has already been escalated (once per key).</summary>
        private static readonly HashSet<string> s_reportedMisses = new HashSet<string>();

        /// <summary>
        /// Load an enemy body prefab by slug. <paramref name="slug"/> is the bare file name,
        /// e.g. "Skeleton_Warrior". Null when nothing is resolvable RIGHT NOW — the caller shows a
        /// placeholder and re-skins when the content arrives. Never blocks.
        /// </summary>
        public static GameObject LoadEnemyPrefab(string slug) => LoadPrefixed<GameObject>(slug);

        /// <summary>
        /// Load a shared enemy animator controller by name, e.g. "OrcHumanoid". Null when it is
        /// not resident yet (the body still spawns; it just holds its bind pose until the
        /// controller lands). Never blocks.
        /// </summary>
        public static RuntimeAnimatorController LoadEnemyController(string name)
            => LoadPrefixed<RuntimeAnimatorController>(name);

        /// <summary>
        /// Generic escape hatch for enemy assets addressed by a FULL Resources-relative key
        /// (prefix included), e.g. <c>LoadEnemyAsset&lt;GameObject&gt;("Enemies/Boss_Dragon")</c>.
        /// <para>⛔ <b>T MUST BE A TYPE ADDRESSABLES ACTUALLY PUBLISHES</b> — a GameObject, a
        /// RuntimeAnimatorController, a Texture, a Material, a ScriptableObject. <b>NEVER a
        /// Component / MonoBehaviour.</b> A prefab is addressed as <c>UnityEngine.GameObject</c>
        /// and Addressables cannot hand back a Component type: the request is unsatisfiable by
        /// construction, on every platform, every time. That was WO-1097 — the apex wave asked
        /// for its boss by component type, the load could only ever fail, and the dragon silently
        /// never spawned. This seam now REFUSES such a request (see
        /// <see cref="IsUnsatisfiableComponentRequest"/>) and returns null after a
        /// <c>FlowTrace.Fail</c> naming the address and the requested type.</para>
        /// <para>The working shape — load the GameObject, then walk to the component:</para>
        /// <code>
        /// GameObject prefab = EnemyAssetLoader.LoadEnemyAsset&lt;GameObject&gt;("Enemies/Boss_Dragon");
        /// DragonBoss boss = prefab != null ? prefab.GetComponentInChildren&lt;DragonBoss&gt;(true) : null;
        /// </code>
        /// <para>(<c>LoadEnemyPrefab("Boss_Dragon")</c> is the same call with the prefix applied —
        /// prefer it for bodies. This is what <c>WaveManager</c> does at its apex-boss seam.)</para>
        /// <para>⚠ The <c>where T : Object</c> constraint CANNOT be tightened to exclude Components:
        /// C# cannot express "Object but not Component". The runtime screen below is the guard —
        /// the constraint staying loose is a language limit, not an omission.</para>
        /// </summary>
        public static T LoadEnemyAsset<T>(string key) where T : Object => Load<T>(key);

        /// <summary>
        /// Marker prefix on every type-screen refusal line. A <c>public const</c> on purpose: the
        /// gate matches against THIS string rather than a retyped copy (CLAUDE.md §2/§5 — a copied
        /// literal is how a pin and its producer drift apart).
        /// </summary>
        public const string TypeScreenMarker = "ENEMY_ASSET_TYPE_SCREEN";

        /// <summary>
        /// TRUE when <paramref name="requested"/> is a type this seam can NEVER return, because
        /// Addressables publishes a prefab as <c>UnityEngine.GameObject</c> and cannot hand back a
        /// <see cref="Component"/> living on it. Pure — no side effects, no load, safe to probe.
        /// <para>This is the WO-1097 class in one predicate. See <see cref="LoadEnemyAsset{T}"/>.</para>
        /// </summary>
        public static bool IsUnsatisfiableComponentRequest(SysType requested)
            => requested != null && typeof(Component).IsAssignableFrom(requested);

        /// <summary>Refusals already reported (address|type), so a per-frame caller cannot flood the
        /// log — a flooded log evicts the boot window and destroys the evidence (§12).</summary>
        private static readonly HashSet<string> s_reportedTypeScreen = new HashSet<string>();

        /// <summary>GATE-ONLY: forget which refusals have been reported, so a regression can assert
        /// the trace deterministically regardless of what ran before it in the same domain.</summary>
        public static void ResetTypeScreenReports() => s_reportedTypeScreen.Clear();

        /// <summary>
        /// Ask for an enemy FAMILY's content ahead of time without waiting for it — the on-demand
        /// half of the owner's 2026-08-20 per-family ruling. A spawner that knows the roster it is
        /// about to build should call this first so the bodies do not trickle in one at a time.
        /// Non-blocking and idempotent; safe from any callback.
        /// </summary>
        public static void PrewarmFamily(string modelOrAddress)
            => EnemyContentWarmer.WarmFamily(EnemyContentWarmer.FamilyOf(modelOrAddress));

        /// <summary>True when this exact address is already resident and a load will hit instantly.
        /// A dictionary probe — used by callers that want to avoid arming a re-skin they do not need.</summary>
        public static bool IsResident<T>(string address) where T : Object
            => EnemyContentWarmer.TryGet<T>(address, out _);

        /// <summary>Prefix a bare slug/name with <see cref="EnemyAddrPrefix"/>, then load.</summary>
        private static T LoadPrefixed<T>(string slug) where T : Object
        {
            if (string.IsNullOrEmpty(slug)) return null;
            return Load<T>(EnemyAddrPrefix + slug);
        }

        // ---------------------------------------------------------------------

        private static T Load<T>(string address) where T : Object
        {
            if (string.IsNullOrWhiteSpace(address)) return null;

            // ---- 0. TYPE SCREEN (WO-1097) ------------------------------------
            // Addressables addresses a prefab as UnityEngine.GameObject. A Component-typed request
            // is unsatisfiable BY CONSTRUCTION — Addressables answers it with
            //   InvalidKeyException: No Asset found for Key=<addr> with Type=<Component>.
            //   Key exists as Type=UnityEngine.GameObject, which is not assignable from ...
            // and the caller, which cannot distinguish that from "not downloaded yet", waits
            // forever. That is exactly how the apex wave shipped without its dragon (WO-1097,
            // F8 seq 4969-4971). REFUSE IT HERE, LOUDLY, and return null:
            //   • loud, because a silent null reads as "not resident yet" and self-heals never;
            //   • null and NOT a throw, because this runs on the player path from wave callbacks
            //     and scene entry — an exception there costs a frame's worth of spawns, and the
            //     whole contract of this file is that a content problem degrades, never stops.
            if (IsUnsatisfiableComponentRequest(typeof(T)))
            {
                if (s_reportedTypeScreen.Add(address + "|" + typeof(T).FullName))
                {
                    FlowTrace.Fail(System,
                        $"{TypeScreenMarker} REFUSED: '{address}' was requested as {typeof(T).FullName}, " +
                        "which derives from UnityEngine.Component. Addressables publishes a prefab as " +
                        "UnityEngine.GameObject and CANNOT return a component from it, so this request " +
                        "could never succeed on any platform — it is the WO-1097 defect, caught at the " +
                        $"seam instead of failing silently forever. FIX THE CALLER: load the GameObject " +
                        $"(LoadEnemyAsset<GameObject>(\"{address}\") or LoadEnemyPrefab) and then " +
                        $"GetComponentInChildren<{typeof(T).Name}>(true) on it. Returning null: the " +
                        "caller shows its placeholder; nothing stalls.");
                }
                return null;
            }

            T result = null;

            // ---- 1. RESIDENT CACHE (this is the Addressables tier now) -------
            // A dictionary probe. It cannot download, cannot pump, cannot sleep and cannot
            // deadlock — which is the entire difference between this file and the version of it
            // that carried the pattern that hung the game for three minutes on 2026-08-20.
            if (EnemyContentWarmer.TryGet(address, out result) && result != null)
            {
                FlowTrace.Once(System, "addr-hit-" + address,
                    $"'{address}' ({typeof(T).Name}) served RESIDENT from the enemy warm cache " +
                    $"(family '{EnemyContentWarmer.FamilyOf(address)}', off the blocking path).");
                return result;
            }

#if UNITY_EDITOR
            // ---- 2. EDITOR-ONLY synchronous Addressables ---------------------
            // Kept AHEAD of Resources so editor precedence matches the original Addressables-first
            // contract byte for byte. Safe only because the Editor uses the AssetDatabase provider —
            // no bundle, no UnityWebRequest, no player loop to starve. It is also what keeps the
            // batchmode enemy gates (DataRegression, EnemyResolverRegression, EnemyRigColorRegression)
            // able to assert that enemy art exists. See EnemyEditorSyncResolver.cs; it is the ONE
            // allowlisted blocking site in this seam.
            result = EnemyEditorSyncResolver.Resolve<T>(address);
            if (result != null) return result;
#endif

            // ---- 3. Resources fallback ---------------------------------------
            // Instant and local; kept for anything that never left Resources. ⚠ For enemies this
            // tier now answers NOTHING in a shipped build — Assets/Resources/Enemies was deleted by
            // the migration (verified on disk). It is not the safety net any more; the residency
            // cache plus the re-skin is.
            Guard.Try(System, $"Resources.Load {address} ({typeof(T).Name})", () =>
            {
                result = Resources.Load<T>(address);
            });
            if (result != null) return result;

            // ---- 4. NOT RESIDENT. SKIP THIS FRAME — DO NOT WAIT. -------------
            // Ask for it asynchronously (idempotent; the warmer dedupes and pulls only this
            // FAMILY's bundle) and return null. The caller degrades visibly-but-explicably:
            // EnemyFactory spawns the tinted capsule AND arms EnemyLateSkinner, which swaps in the
            // real body the moment the art lands.
            //
            // ⛔ THE REPORTING IS THE POINT, AND SO IS THE DISTINCTION. Three minutes were lost to
            // unexplained silence because nothing announced the wait. Every skip names the address,
            // the family, and how long it has been waiting — and says WHICH of the two failures it
            // is, because they need different fixes:
            //     NOT-YET-DOWNLOADED  -> transient. It re-skins itself. Nobody needs to do anything.
            //     GENUINELY MISSING   -> permanent. The address is not in the catalog or its load
            //                            failed outright; someone must ship it.
            bool knownAbsent  = EnemyContentWarmer.IsKnownAbsent<T>(address);
            bool registered   = EnemyContentWarmer.IsRegisteredAddress(address);
            bool catalogReady = EnemyContentWarmer.IsSettled;
            float waited      = EnemyContentWarmer.SecondsWaiting(address);
            string family     = EnemyContentWarmer.FamilyOf(address);

            if (!knownAbsent) EnemyContentWarmer.Request<T>(address);

            // MISSING is a hard defect: the catalog has settled and either the load already failed
            // outright, or the address simply is not registered anywhere. Waiting longer cannot fix
            // either one, so report it once, at error level, and name the remedy.
            bool genuinelyMissing = knownAbsent || (catalogReady && !registered);

            if (genuinelyMissing)
            {
                if (s_reportedMisses.Add(address))
                {
                    FlowTrace.Fail(System,
                        $"enemy asset '{address}' ({typeof(T).Name}, family '{family}') is GENUINELY MISSING — " +
                        $"not a download in progress. knownAbsent={knownAbsent}, registeredInCatalog={registered}, " +
                        $"catalogState={EnemyContentWarmer.State}, discovered={EnemyContentWarmer.DiscoveredAddressCount}, " +
                        $"resident={EnemyContentWarmer.ResidentCount}. Assets/Resources/Enemies no longer exists, so " +
                        "there is no fallback copy: this enemy renders as a TINTED CAPSULE for the rest of the " +
                        "launch and WILL NOT re-skin. Fix by shipping this exact address in the enemy Addressable " +
                        "group (run the grouper, rebuild + upload the bundle). NOTE: this is a VISUAL defect. The " +
                        "game did not stall — that is deliberate.");
                }
            }
            else if (waited > EnemyContentWarmer.MissEscalateSeconds)
            {
                if (s_reportedMisses.Add(address))
                {
                    FlowTrace.Fail(System,
                        $"enemy asset '{address}' ({typeof(T).Name}, family '{family}') has been NOT-YET-DOWNLOADED " +
                        $"for {waited:F1}s — past the {EnemyContentWarmer.MissEscalateSeconds}s point where a slow " +
                        $"fetch stops looking transient (familyDownloading={EnemyContentWarmer.IsFamilyDownloading(family)}, " +
                        $"familyLocal={EnemyContentWarmer.IsFamilyLocal(family)}, pending={EnemyContentWarmer.PendingRequests}, " +
                        $"catalogState={EnemyContentWarmer.State}). The body is still a capsule and STILL re-skins if " +
                        "the bytes arrive. Check the CDN reachability for this family's bundle.");
                }
            }
            else
            {
                FlowTrace.Throttle(System, "skip-" + address, 1f,
                    $"'{address}' ({typeof(T).Name}) is NOT YET DOWNLOADED — SKIPPING this frame after {waited:F1}s " +
                    $"and fetching family '{family}' asynchronously (familyDownloading=" +
                    $"{EnemyContentWarmer.IsFamilyDownloading(family)}, pending={EnemyContentWarmer.PendingRequests}, " +
                    $"catalogState={EnemyContentWarmer.State}). The caller shows a placeholder and RE-SKINS when it " +
                    "lands. This deliberately does NOT wait: waiting here is what deadlocked the game on 2026-08-20.");
            }

            return null;
        }
    }
}

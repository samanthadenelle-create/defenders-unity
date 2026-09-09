// =============================================================================
// EnemyLoadBoundedRegression — the enemy art load path CANNOT block the main
// thread, degrades to a placeholder that RE-SKINS, and says WHY it degraded.
// Markers: ENEMY_LOAD_BOUNDED_OK / ENEMY_LOAD_BOUNDED_FAIL.
// -----------------------------------------------------------------------------
// Assembly: DeNelle.EditorRegression.
// Standalone: run-unity-method -Method DeNelle.Editor.EnemyLoadBoundedRegression.RunAll
// ⚠ REGISTER ME in DeNelle.Editor.DataRegression.RunAll alongside
// StructureLoadBoundedRegression — this file's author did not own that file.
//
// ⛔ THE DEFECT THIS PINS — IT IS THE SAME P0, ON A SECOND SEAM (2026-08-20).
// The structure loader deadlocked a Seeker device returning from a dungeon: last log
// line 10:25:14.917, device clock 10:28:35, process alive and foregrounded on a stale
// frame, with a HEALTHY 31.5 ms ping to the R2 CDN taken FROM the hung device. Not a
// network fault — a deadlock. The mechanism, in Addressables 2.9.1
// (Library/PackageCache/com.unity.addressables@8460f1c9c927):
//     while (!InvokeWaitForCompletion()) { }      AsyncOperationBase.cs:171
// no timeout, no yield, no exit; ProviderOperation.InvokeWaitForCompletion
// (ProviderOperation.cs:66) returns false forever without a completion callback; and
// AssetBundleResource.WaitForCompletionHandler (AssetBundleProvider.cs:543)
// Thread.Sleep()s the MAIN THREAD on progress the player loop must drive. Called from
// an engine callback the player loop cannot re-enter, the thread that would finish the
// operation is the thread waiting on it.
//
// EnemyAssetLoader carried the EXACT SAME TWO CALLS, unfixed, reachable from an enemy
// spawn — one on the asset load, one on the registration probe. This suite exists so
// they cannot come back, on this seam or the next.
//
// ⛔ IT ALSO PINS THE LIVE ENEMY DEFECT THAT WAS CAPTURED THE SAME DAY:
//   [Flow:Enemy] model 'Skeleton_Minion' (id 'hollow-walker') had NO renderable mesh
//                at 'Enemies/Skeleton_Minion' — FALLBACK to tinted capsule
// with ZERO [Flow:EnemyAssets] lines in the whole session. The enemy seam was never
// entered: EnemyFactory skinned through VisualFactory's PATH overload, which resolves
// via StructureAssetLoader. Enemy addresses therefore missed the STRUCTURE residency
// cache (it only warms "Structures/"), missed Resources (Assets/Resources/Enemies is
// deleted), and every enemy fell through to a capsule. Group 4 makes that unrepeatable
// by asserting EnemyFactory resolves through the ENEMY loader and never the structure one.
//
// ⛔ WHY THIS IS A SOURCE LINT AND NOT A RUNTIME TEST.
// The property is "no code path can block", and the failure it guards is a DEADLOCK. A
// runtime test of a deadlock either hangs the gate (useless — that is the symptom) or
// proves nothing because the content happened to be resident that run. The only oracle
// that is both cheap and reliable is: THE BLOCKING CALL IS NOT ON THIS PATH. So this
// suite proves the absence structurally, and proves the GRACEFUL-SKIP + RE-SKIN +
// LOUD-REPORT contract that must hold in its place.
//
// ⛔ IT LINTS CODE ONLY — comments and string-literal contents are blanked (via
// ComposedDungeonRunRegression.StripCommentsAndStrings) before any match. The files it
// guards carry long tombstone comments naming the banned call precisely so the next
// author understands the ban; a rule that matched its own tombstone would punish
// exactly the documentation CLAUDE.md §12/§15 demands. Group 6 proves the blanking
// works rather than assuming it.
//
// ⛔ IT ASSERTS BOTH DIRECTIONS (group 6). A gate that does not FAIL the known-bad state
// is not a gate, and a gate that fails a clean state becomes a permanent red everyone
// learns to ignore. Group 6 runs the real detectors over synthetic sources built from
// the ACTUAL pre-fix EnemyAssetLoader body at HEAD.
// =============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace DeNelle.Editor
{
    /// <summary>
    /// Source-lint: the enemy asset load path is incapable of an unbounded main-thread block,
    /// degrades by SKIPPING to a placeholder that later RE-SKINS, and reports every skip loudly
    /// and with its CAUSE. Returns true (summary) / false (detail); never throws.
    /// </summary>
    public static class EnemyLoadBoundedRegression
    {
        public const string MarkerOk   = "ENEMY_LOAD_BOUNDED_OK";
        public const string MarkerFail = "ENEMY_LOAD_BOUNDED_FAIL";

        /// <summary>The blocking call. Assembled from parts so this constant is not itself a
        /// literal that a future, dumber grep of this repo would flag.</summary>
        private const string BlockingCall = "WaitFor" + "Completion";

        // Repo-relative paths of the seam under guard.
        private const string LoaderRel     = "_Modules/Core/Addressables/EnemyAssetLoader.cs";
        private const string WarmerRel     = "_Modules/Core/Addressables/EnemyContentWarmer.cs";
        private const string CacheHealthRel = "_Modules/Core/Addressables/AddressablesCacheHealth.cs";
        private const string ResolverRel   = "_Modules/Core/Addressables/EnemyEditorSyncResolver.cs";
        private const string FactoryRel    = "_Modules/Village/Enemies/EnemyFactory.cs";
        private const string SkinnerRel    = "_Modules/Village/Enemies/EnemyLateSkinner.cs";

        /// <summary>The ONE file in the enemy seam allowed to block, because it is entirely inside
        /// #if UNITY_EDITOR and the Editor resolves through the AssetDatabase provider — no bundle,
        /// no UnityWebRequest, no player loop to starve.</summary>
        private const string AllowedBlockingFile = "EnemyEditorSyncResolver.cs";

        public static bool Run(out string reason)
        {
            var failures = new List<string>();
            var log = new StringBuilder();
            log.AppendLine("--- ENEMY LOAD IS BOUNDED (P0 hang pattern, 2026-08-20) ---");

            string assets = null;
            try { assets = Application.dataPath; } catch { }
            if (string.IsNullOrEmpty(assets) || !Directory.Exists(assets))
            {
                // hollow-pass-ok: Assets/ cannot be absent in this project, so the skip is
                // unreachable rather than risky (same shape as the sibling source-lints).
                reason = null;
                Debug.Log(log + "  (skipped -- Assets/ not found)\n" + MarkerOk);
                return true;
            }

            string loaderPath   = Abs(assets, LoaderRel);
            string warmerPath   = Abs(assets, WarmerRel);
            string cacheHealthPath = Abs(assets, CacheHealthRel);
            string factoryPath  = Abs(assets, FactoryRel);
            string skinnerPath  = Abs(assets, SkinnerRel);

            // ── 1. THE LOADER DOES NOT BLOCK. AT ALL. ─────────────────────────
            string loaderRaw = ReadOrNull(loaderPath);
            if (loaderRaw == null)
            {
                failures.Add($"Assets/{LoaderRel} is missing — the enemy seam has moved or been deleted; " +
                             "this gate cannot protect a path it cannot find.");
            }
            else
            {
                string loader = Code(loaderRaw);

                if (Blocks(loader, out int at))
                {
                    failures.Add($"Assets/{LoaderRel} calls {BlockingCall}() (code offset {at}). That call is an " +
                                 "UNINTERRUPTIBLE, UNBOUNDED spin with no timeout API (Addressables 2.9.1 " +
                                 "AsyncOperationBase.cs:171). Reaching it from a spawn or a scene callback " +
                                 "DEADLOCKED the game for three minutes on a device with a healthy 31.5 ms link " +
                                 "to the CDN. Serve resident content through EnemyContentWarmer.TryGet and SKIP " +
                                 "when it is not resident.");
                }

                RequireToken(failures, loader, "EnemyContentWarmer.TryGet", LoaderRel,
                    "the resident-cache probe is the ONLY non-blocking way to serve an Addressables-backed asset " +
                    "synchronously; without it the loader has no fast path and will grow one that blocks");
                RequireToken(failures, loader, "EnemyContentWarmer.Request", LoaderRel,
                    "a miss must kick an ASYNC fetch and return, so the next attempt succeeds; without it a cold " +
                    "cache is a permanent miss and someone will 'fix' it by waiting");

                // LOUD REPORTING. Three minutes cost what they cost because NOTHING announced the wait.
                RequireToken(failures, loader, "FlowTrace.Fail", LoaderRel,
                    "an enemy with no art is a capsule the player can see and must be error-level once it stops " +
                    "looking transient");
                if (loader.IndexOf("FlowTrace.Throttle", StringComparison.Ordinal) < 0 &&
                    loader.IndexOf("FlowTrace.Warn", StringComparison.Ordinal) < 0)
                {
                    failures.Add($"Assets/{LoaderRel} no longer Warns/Throttles on the SKIP path — a silent skip is " +
                                 "how a three-minute stall went unexplained. Every skip must name the address and " +
                                 "how long it has waited.");
                }
                RequireToken(failures, loader, "SecondsWaiting", LoaderRel,
                    "the report must say HOW LONG it waited, not merely that it skipped");

                // ⛔ THE TWO CAUSES MUST READ DIFFERENTLY. 'not downloaded yet' fixes itself; 'the
                // asset does not exist' never does. One message for both sends triage the wrong way.
                if (loader.IndexOf("IsKnownAbsent", StringComparison.Ordinal) < 0 ||
                    loader.IndexOf("IsRegisteredAddress", StringComparison.Ordinal) < 0)
                {
                    failures.Add($"Assets/{LoaderRel} no longer distinguishes NOT-YET-DOWNLOADED from a GENUINELY " +
                                 "MISSING asset (it must consult both EnemyContentWarmer.IsKnownAbsent and " +
                                 ".IsRegisteredAddress). They need OPPOSITE fixes — one is transient and re-skins " +
                                 "itself, the other needs someone to ship the address — and a single message for " +
                                 "both is how the owner's capsule capture read as 'the art is broken' when the art " +
                                 "was fine and the seam was never entered.");
                }

                // PER-FAMILY, the owner's 2026-08-20 ruling: a miss fetches THAT FAMILY, not 64 MB.
                RequireToken(failures, loader, "FamilyOf", LoaderRel,
                    "the miss path must resolve and name the FAMILY it is fetching — owner ruling 2026-08-20, " +
                    "'I want this broken down to each family of enemy'");

                log.AppendLine($"  {LoaderRel}: no {BlockingCall}, resident-first, async per-family request, " +
                               "reports loudly and names the cause.");
            }

            // ── 2. NOTHING ELSE IN THE SEAM BLOCKS, except the editor-only site ──
            string addrDir = Abs(assets, "_Modules/Core/Addressables");
            string[] seamFiles;
            try { seamFiles = Directory.GetFiles(addrDir, "Enemy*.cs", SearchOption.TopDirectoryOnly); }
            catch (Exception ex) { seamFiles = Array.Empty<string>(); log.AppendLine("  (seam scan failed: " + ex.Message + ")"); }

            int seamScanned = 0;
            bool sawResolver = false;
            foreach (var path in seamFiles)
            {
                string file = Path.GetFileName(path);
                string raw = ReadOrNull(path);
                if (raw == null) continue;
                seamScanned++;
                if (string.Equals(file, AllowedBlockingFile, StringComparison.OrdinalIgnoreCase)) sawResolver = true;

                if (!Blocks(Code(raw), out _)) continue;

                if (!string.Equals(file, AllowedBlockingFile, StringComparison.OrdinalIgnoreCase))
                {
                    failures.Add($"Assets/_Modules/Core/Addressables/{file} calls {BlockingCall}(). The ONLY " +
                                 $"allowlisted blocking site in the enemy seam is {AllowedBlockingFile}, and only " +
                                 "because that whole file is inside #if UNITY_EDITOR. Adding a second one re-opens " +
                                 "the 2026-08-20 deadlock on this seam.");
                    continue;
                }

                // The allowlisted file EARNS its allowance only by being editor-only. Verified on the
                // RAW text: #if directives are code, and blanking never touches them, but they are
                // structural rather than token-ish so they are checked explicitly.
                if (FirstDirective(raw) != "#if UNITY_EDITOR")
                {
                    failures.Add($"{AllowedBlockingFile} is allowed to call {BlockingCall}() ONLY because it is " +
                                 "entirely editor-only, but its first preprocessor directive is no longer " +
                                 "'#if UNITY_EDITOR'. As written it would compile into a PLAYER, where that call " +
                                 "deadlocks. Either restore the guard or remove the call.");
                }
                if (raw.TrimEnd().EndsWith("#endif", StringComparison.Ordinal) == false)
                {
                    failures.Add($"{AllowedBlockingFile} does not END with '#endif' — the editor-only guard no " +
                                 "longer wraps the whole file, so part of it ships to the player.");
                }

                // HANDLE HYGIENE. The pre-fix loader opened a location handle on EVERY enemy load
                // (a wave spawns dozens). It DID release it in a finally — checked at source against
                // HEAD — and that must stay true wherever the probe now lives.
                string resolverCode = Code(raw);
                int opened   = Count(resolverCode, "LoadResourceLocationsAsync");
                int released = Count(resolverCode, "Addressables.Release");
                if (opened > 0 && released < opened)
                {
                    failures.Add($"{AllowedBlockingFile} opens {opened} location handle(s) but releases {released}. " +
                                 "A location handle leaked per enemy load is a leak per wave. Release every one.");
                }
            }
            if (seamScanned > 0 && !sawResolver)
            {
                failures.Add($"Assets/_Modules/Core/Addressables/{AllowedBlockingFile} is missing. The editor needs a " +
                             "synchronous resolve or every batchmode enemy-art gate (DataRegression, " +
                             "EnemyResolverRegression, EnemyRigColorRegression) silently stops checking anything — " +
                             "and someone will restore that capability by putting the blocking call back in the loader.");
            }
            log.AppendLine($"  scanned {seamScanned} Enemy*.cs seam file(s) under Core/Addressables.");

            // ── 3. THE WARMER IS ASYNC, RETAINS, PER-FAMILY, AND NEVER BLOCKS ──
            string warmerRaw = ReadOrNull(warmerPath);
            if (warmerRaw == null)
            {
                failures.Add($"Assets/{WarmerRel} is missing — it IS the non-blocking replacement for the " +
                             "synchronous load. Without it the loader has nowhere to serve resident content from.");
            }
            else
            {
                string warmer = Code(warmerRaw);

                if (Blocks(warmer, out _))
                    failures.Add($"Assets/{WarmerRel} calls {BlockingCall}(). The warmer exists precisely so that " +
                                 "nothing has to.");

                if (warmer.IndexOf("yield return", StringComparison.Ordinal) < 0)
                    failures.Add($"Assets/{WarmerRel} has no 'yield return' — the pass is no longer a coroutine, so " +
                                 "it is no longer running on the player loop. A blocking warm pass is the original " +
                                 "bug wearing a new name.");

                RequireToken(failures, warmer, "MonoBehaviour", WarmerRel,
                    "the coroutine needs a DontDestroyOnLoad host; without one there is no player loop to warm from");
                RequireToken(failures, warmer, "public static bool TryGet", WarmerRel,
                    "TryGet is the synchronous, non-blocking read the whole fix rests on");
                RequireToken(failures, warmer, "public static void Defer", WarmerRel,
                    "Defer is how a scene-load callback gets OFF the engine callback before touching content");
                RequireToken(failures, warmer, "WarmDeadlineSeconds", WarmerRel,
                    "the pass must have a reporting deadline so a stalled catalog is announced, not silent");

                // RETENTION. Releasing loaded enemy content lets a raid load evict it and puts the
                // next spawn back on the cold path.
                RequireToken(failures, warmer, "s_retained", WarmerRel,
                    "loaded enemy content must be retained for the process, or the owner's constant " +
                    "town -> raid -> town loop re-downloads and re-evicts it every cycle");
                if (warmer.IndexOf("s_retained.Clear", StringComparison.Ordinal) >= 0 ||
                    warmer.IndexOf("s_retained.Remove", StringComparison.Ordinal) >= 0)
                {
                    failures.Add($"Assets/{WarmerRel} clears/removes from s_retained. That list is the ONLY thing " +
                                 "keeping enemy content resident across a raid round-trip; draining it restores " +
                                 "the eviction cycle this fix exists to stop.");
                }

                // ── 3b. PER-FAMILY, ON DEMAND — the owner's ruling, pinned. ────
                RequireToken(failures, warmer, "public static void WarmFamily", WarmerRel,
                    "owner ruling 2026-08-20: 'I want this broken down to each family of enemy'. WarmFamily is that " +
                    "ruling in code — one family's bundles, on demand");
                RequireToken(failures, warmer, "s_afterFamilyDownload", WarmerRel,
                    "typed asset loads must queue behind an in-flight family download so raid spawn cannot make " +
                    "two providers write/read the same cache file concurrently");
                RequireToken(failures, warmer, "CompleteFamilyWaiters", WarmerRel,
                    "queued typed requests must resume only after the family cache write completes");
                RequireToken(failures, warmer, "HasTypedRequestInFlight", WarmerRel,
                    "the inverse race must also be closed: a late family prefetch cannot overlap an exact " +
                    "asset request that already owns the cache write");
                RequireToken(failures, warmer, "s_familyDownloadsInFlight", WarmerRel,
                    "historical family-request state must be distinct from a currently active cache writer");
                RequireToken(failures, warmer, "AddressablesCacheHealth.ReportDownloadFailure", WarmerRel,
                    "a failed family cache write must quarantine later remote loads for the session");

                string warmBody;
                if (!TryMethodBody(warmer, "IEnumerator WarmRoutine(", out warmBody))
                {
                    failures.Add($"Assets/{WarmerRel}: WarmRoutine() not found — the discovery pass has been renamed " +
                                 "or removed and this gate can no longer see what it does.");
                }
                else
                {
                    if (BulkWarms(warmBody))
                        failures.Add($"Assets/{WarmerRel}: WarmRoutine DOWNLOADS OR LOADS enemy content in bulk. " +
                                     "That is the structure warmer's design, and it is WRONG here: enemy content is " +
                                     "~64 MB across many families and an encounter uses one or two of them. Owner " +
                                     "ruling 2026-08-20 is per-family, on demand — discovery only in this pass, " +
                                     "fetches via WarmFamily/Request at spawn time.");

                    if (warmBody.IndexOf("DecideState(", StringComparison.Ordinal) < 0)
                        failures.Add($"Assets/{WarmerRel}: WarmRoutine no longer routes its final state through " +
                                     "DecideState(). That method is the single place Ready can be decided; " +
                                     "bypassing it is how the reported state drifts from the achieved one.");
                }

                // Ready may be RETURNED only by DecideState, never ASSIGNED. The assignment form is
                // what let the STRUCTURE warmer stamp 'Warm' without anyone checking it was true.
                if (warmer.IndexOf("State = EnemyContentState.Ready;", StringComparison.Ordinal) >= 0)
                {
                    failures.Add($"Assets/{WarmerRel} assigns State = Ready directly. Ready must be decided ONLY by " +
                                 "DecideState(discovered), which refuses it when the catalog yielded no enemy " +
                                 "addresses at all. A direct assignment reinstates the marker that claimed success " +
                                 "having achieved nothing — the exact 2026-08-20 'pills loading' defect class.");
                }

                string decideBody;
                if (!TryMethodBody(warmer, "EnemyContentState DecideState(", out decideBody))
                {
                    failures.Add($"Assets/{WarmerRel}: DecideState(discovered) is missing. It is the guard that makes " +
                                 "'Ready' falsifiable — without it the pass can report success having found no enemy " +
                                 "addresses at all, which means EVERY enemy is a capsule and nothing says so.");
                }
                else
                {
                    if (!DiscoveredZeroIsRefused(decideBody))
                        failures.Add($"Assets/{WarmerRel}: DecideState no longer refuses the discovered==0 case. It " +
                                     "must return Degraded AND FlowTrace.Fail when the catalog yielded no enemy " +
                                     "addresses: Assets/Resources/Enemies is deleted, so that state means every " +
                                     "enemy in the game renders as a capsule. Reporting Ready there is a lie.");
                    if (decideBody.IndexOf("EnemyContentState.Ready;", StringComparison.Ordinal) < 0)
                        failures.Add($"Assets/{WarmerRel}: DecideState never returns Ready — the fast path can never " +
                                     "be reported as available, which makes the whole residency cache dead code.");
                }

                log.AppendLine($"  {WarmerRel}: coroutine discovery pass, per-family on-demand fetch, retains, " +
                               "Ready gated on discovery.");
            }

            // 3c. Corrupt bundles can abort inside Unity's native serializer before a managed
            // failed handle exists. Existing installs need a pre-Addressables cache migration.
            string cacheRaw = ReadOrNull(cacheHealthPath);
            if (cacheRaw == null)
            {
                failures.Add($"Assets/{CacheHealthRel} is missing — existing players can retain a corrupt bundle " +
                             "cache and crash natively when a raid first opens that family.");
            }
            else
            {
                string cache = Code(cacheRaw);
                RequireToken(failures, cache, "RuntimeInitializeLoadType.BeforeSceneLoad", CacheHealthRel,
                    "cache repair must precede every AfterSceneLoad Addressables warmer");
                RequireToken(failures, cache, "Caching.ClearCache", CacheHealthRel,
                    "the migration must remove stale native AssetBundle cache entries");
                RequireToken(failures, cache, "PrefRepairEpoch", CacheHealthRel,
                    "repair must be one-time rather than forcing a download on every launch");
                RequireToken(failures, cache, "UnsafeThisSession", CacheHealthRel,
                    "if Unity refuses repair, later remote deserialization must fail closed this launch");
                log.AppendLine($"  {CacheHealthRel}: one-time pre-load repair plus fail-closed session quarantine.");
            }

            // ── 4. THE CALL SITE USES THE ENEMY SEAM (the capture's real bug) ──
            string factoryRaw = ReadOrNull(factoryPath);
            if (factoryRaw == null)
            {
                failures.Add($"Assets/{FactoryRel} is missing — it is the SINGLE enemy-creation path and the call " +
                             "site the capture named.");
            }
            else
            {
                string factory = Code(factoryRaw);

                RequireToken(failures, factory, "EnemyAssetLoader.LoadEnemyPrefab", FactoryRel,
                    "the enemy body must resolve through the ENEMY seam. On 2026-08-20 it resolved through " +
                    "VisualFactory's path overload (which calls StructureAssetLoader): enemy addresses missed the " +
                    "STRUCTURE residency cache, missed the deleted Resources/Enemies, and every enemy became a " +
                    "capsule — with ZERO [Flow:EnemyAssets] lines in the whole session, because the enemy seam was " +
                    "never entered at all");

                if (factory.IndexOf("StructureAssetLoader", StringComparison.Ordinal) >= 0)
                {
                    failures.Add($"Assets/{FactoryRel} references StructureAssetLoader. Enemies do not resolve " +
                                 "through the structure seam — that mis-route IS the captured capsule defect. " +
                                 "Use EnemyAssetLoader.");
                }

                RequireToken(failures, factory, "EnemyLateSkinner.Arm", FactoryRel,
                    "'never block' without 'and re-skin later' trades a hang for a permanent defect: an enemy " +
                    "spawned two seconds before its family bundle lands would wear a coloured capsule for the " +
                    "whole encounter, which is exactly the owner's captured symptom");

                if (Blocks(factory, out _))
                    failures.Add($"Assets/{FactoryRel} calls {BlockingCall}(). Enemy spawns are reachable from wave " +
                                 "callbacks and scene-entry paths — the same nesting that made the structure seam's " +
                                 "wait a DEADLOCK rather than a stall.");

                // The capsule report must say WHICH failure it is; the two need opposite fixes.
                if (factory.IndexOf("IsKnownAbsent", StringComparison.Ordinal) < 0 ||
                    factory.IndexOf("IsRegisteredAddress", StringComparison.Ordinal) < 0)
                {
                    failures.Add($"Assets/{FactoryRel}: the capsule-fallback report no longer distinguishes " +
                                 "NOT-YET-DOWNLOADED from a GENUINELY MISSING asset. The captured line " +
                                 "(\"had NO renderable mesh ... FALLBACK to tinted capsule\") read identically for " +
                                 "both, which is why it looked like broken art when the art was fine.");
                }

                log.AppendLine($"  {FactoryRel}: resolves through the enemy seam, arms a re-skin, names the cause.");
            }

            // ── 5. THE RE-SKIN EXISTS, IS NON-BLOCKING, AND GIVES UP HONESTLY ──
            string skinnerRaw = ReadOrNull(skinnerPath);
            if (skinnerRaw == null)
            {
                failures.Add($"Assets/{SkinnerRel} is missing — it is the half of the fix that stops 'we never wait' " +
                             "from meaning 'it is a capsule forever'.");
            }
            else
            {
                string skinner = Code(skinnerRaw);

                if (Blocks(skinner, out _))
                    failures.Add($"Assets/{SkinnerRel} calls {BlockingCall}(). The re-skin polls a dictionary; it " +
                                 "must never wait on content.");

                RequireToken(failures, skinner, "EnemyFactory.TrySkinBody", SkinnerRel,
                    "the re-skin must re-run the SAME body recipe as the spawn, or a late-skinned enemy quietly " +
                    "differs from an early one (rotation, materials, re-ground, proportion guard)");
                RequireToken(failures, skinner, "IsResident", SkinnerRel,
                    "the poll must be a residency probe — a dictionary lookup that cannot download, pump or sleep");
                RequireToken(failures, skinner, "GiveUpSeconds", SkinnerRel,
                    "an unbounded poller per enemy is its own silent failure; a stuck fetch must be reported once " +
                    "and abandoned");
                RequireToken(failures, skinner, "FlowTrace.Once", SkinnerRel,
                    "both outcomes — re-skinned, and abandoned-because-missing — must be reported exactly once");

                log.AppendLine($"  {SkinnerRel}: non-blocking poll, same recipe, honest give-up.");
            }

            // ── 6. THE ORACLES THEMSELVES — BOTH DIRECTIONS ───────────────────
            //  Everything above is only worth the bytes if the detectors discriminate.
            foreach (var c in OracleCases())
            {
                bool flagged = Blocks(Code(c.Source), out _);
                if (flagged == c.ShouldFlag) continue;
                failures.Add($"ORACLE SELF-TEST FAILED ({c.Name}): expected " +
                             (c.ShouldFlag ? "a FLAG" : "NO flag") + $" but got {(flagged ? "a FLAG" : "none")}. " +
                             "The detector no longer discriminates, so every PASS above is meaningless.");
            }
            log.AppendLine($"  blocking oracle: {OracleCases().Count} case(s), the two known-bad HEAD shapes flagged " +
                           "+ clean/tombstone clear.");

            foreach (var c in DiscoveryOracleCases())
            {
                bool refuses = DiscoveredZeroIsRefused(Code(c.Source));
                if (refuses == c.ShouldFlag) continue;
                failures.Add($"DISCOVERY ORACLE SELF-TEST FAILED ({c.Name}): expected " +
                             (c.ShouldFlag ? "REFUSES discovered==0" : "does NOT refuse") +
                             $" but got {(refuses ? "refuses" : "does not refuse")}. " +
                             "The state guard no longer discriminates, so 'Ready' is unchecked again.");
            }
            log.AppendLine($"  discovery oracle: {DiscoveryOracleCases().Count} case(s).");

            foreach (var c in BulkWarmOracleCases())
            {
                bool bulk = BulkWarms(Code(c.Source));
                if (bulk == c.ShouldFlag) continue;
                failures.Add($"PER-FAMILY ORACLE SELF-TEST FAILED ({c.Name}): expected " +
                             (c.ShouldFlag ? "BULK detected" : "no bulk") + $" but got {(bulk ? "bulk" : "none")}. " +
                             "The per-family guard no longer discriminates, so the owner's ruling is undefended.");
            }
            log.AppendLine($"  per-family oracle: {BulkWarmOracleCases().Count} case(s).");

            if (failures.Count == 0)
            {
                reason = null;
                Debug.Log(log + MarkerOk);
                return true;
            }

            reason = "enemy-load-bounded: " + string.Join("; ", failures);
            Debug.LogError(log + MarkerFail + ": " + reason);
            return false;
        }

        // =====================================================================
        //  The detectors, and the synthetic sources that prove they discriminate
        // =====================================================================

        /// <summary>
        /// True when <paramref name="code"/> (ALREADY comment- and string-blanked) contains the
        /// blocking call. Deliberately has NO '#if' escape hatch: a conditional block is exactly how
        /// such a ban erodes. The single editor-only exemption is granted by FILE PATH in group 2,
        /// where the guard can also be verified.
        /// </summary>
        private static bool Blocks(string code, out int offset)
        {
            offset = string.IsNullOrEmpty(code)
                ? -1
                : code.IndexOf(BlockingCall, StringComparison.Ordinal);
            return offset >= 0;
        }

        /// <summary>
        /// True when this DecideState body REFUSES to call a zero-discovery pass Ready: it must test
        /// the discovered count against zero, return Degraded, and report at error level. Either half
        /// alone is not enough — a silent Degraded is how the owner ends up reading the log instead of
        /// the game telling her.
        /// </summary>
        private static bool DiscoveredZeroIsRefused(string decideBody)
        {
            if (string.IsNullOrEmpty(decideBody)) return false;
            bool testsZero = decideBody.IndexOf("discovered == 0", StringComparison.Ordinal) >= 0 ||
                             decideBody.IndexOf("discovered <= 0", StringComparison.Ordinal) >= 0 ||
                             decideBody.IndexOf("discovered < 1", StringComparison.Ordinal) >= 0;
            bool degrades = decideBody.IndexOf("EnemyContentState.Degraded", StringComparison.Ordinal) >= 0;
            bool reports  = decideBody.IndexOf("FlowTrace.Fail", StringComparison.Ordinal) >= 0;
            return testsZero && degrades && reports;
        }

        /// <summary>
        /// True when a warm-pass body pulls enemy content IN BULK — a whole-catalog download or a
        /// load loop. Correct for structures, wrong for enemies (owner ruling 2026-08-20: per family,
        /// on demand). Discovery, and per-family fetches kicked from elsewhere, are not bulk.
        /// </summary>
        private static bool BulkWarms(string body)
        {
            if (string.IsNullOrEmpty(body)) return false;
            return body.IndexOf("DownloadDependenciesAsync", StringComparison.Ordinal) >= 0 ||
                   body.IndexOf("LoadAssetAsync", StringComparison.Ordinal) >= 0 ||
                   body.IndexOf("Request(keys", StringComparison.Ordinal) >= 0;
        }

        private struct OracleCase
        {
            public string Name;
            public string Source;
            public bool   ShouldFlag;
        }

        /// <summary>
        /// Synthetic sources covering both directions. The KNOWN-BAD cases are the two blocking calls
        /// that stood in EnemyAssetLoader at HEAD — reproduced closely enough that the gate provably
        /// would have caught them. The CLEAN and TOMBSTONE cases prove it will not become a permanent
        /// red, including the case that matters most for this repo's documentation style, where the
        /// banned token appears only in prose and in an error string.
        /// </summary>
        private static List<OracleCase> OracleCases()
        {
            // Assembled, never written as one literal: a source that CONTAINS the token as a literal
            // would be blanked by Code() before the detector ever saw it, and the known-bad case
            // would silently stop being known-bad.
            string call = BlockingCall + "()";

            return new List<OracleCase>
            {
                new OracleCase
                {
                    Name = "known-bad: HEAD EnemyAssetLoader.Load (the unfixed twin of the P0)",
                    ShouldFlag = true,
                    Source =
                        "class L { void M() {\n" +
                        "  if (!AddressableRegistered<T>(address)) return;\n" +
                        "  var handle = Addressables.LoadAssetAsync<T>(address);\n" +
                        "  result = handle." + call + ";\n" +
                        "} }",
                },
                new OracleCase
                {
                    Name = "known-bad: HEAD EnemyAssetLoader.AddressableRegistered (the probe)",
                    ShouldFlag = true,
                    Source =
                        "class L { bool M() {\n" +
                        "  locHandle = Addressables.LoadResourceLocationsAsync(address, typeof(T));\n" +
                        "  IList<IResourceLocation> locs = locHandle." + call + ";\n" +
                        "  found = locs != null && locs.Count > 0;\n" +
                        "} }",
                },
                new OracleCase
                {
                    Name = "known-bad: hidden behind a conditional (the erosion path)",
                    ShouldFlag = true,
                    Source =
                        "class L { void M() {\n" +
                        "#if UNITY_EDITOR\n" +
                        "  result = handle." + call + ";\n" +
                        "#endif\n" +
                        "} }",
                },
                new OracleCase
                {
                    Name = "clean: resident-first, async per-family request, skip",
                    ShouldFlag = false,
                    Source =
                        "class L { void M() {\n" +
                        "  if (EnemyContentWarmer.TryGet(address, out result)) return result;\n" +
                        "  EnemyContentWarmer.Request<T>(address);\n" +
                        "  return null;\n" +
                        "} }",
                },
                new OracleCase
                {
                    Name = "clean: the late re-skin poll",
                    ShouldFlag = false,
                    Source =
                        "class S { void U() {\n" +
                        "  if (!EnemyAssetLoader.IsResident<GameObject>(address)) return;\n" +
                        "  var vis = EnemyFactory.TrySkinBody(gameObject, def, model, height);\n" +
                        "} }",
                },
                new OracleCase
                {
                    Name = "tombstone: the token only in a line comment",
                    ShouldFlag = false,
                    Source =
                        "class L { void M() {\n" +
                        "  // Never call " + call + " here - it deadlocked the game on 2026-08-20.\n" +
                        "  return null;\n" +
                        "} }",
                },
                new OracleCase
                {
                    Name = "tombstone: the token only in a block comment",
                    ShouldFlag = false,
                    Source =
                        "class L { /* " + call + " is banned on this path. */ void M() { return null; } }",
                },
                new OracleCase
                {
                    Name = "tombstone: the token only inside a string literal (an error message)",
                    ShouldFlag = false,
                    Source =
                        "class L { void M() {\n" +
                        "  FlowTrace.Fail(Sys, \"do not use " + call + " on this path\");\n" +
                        "} }",
                },
                new OracleCase
                {
                    Name = "tombstone: the token only inside an interpolated string",
                    ShouldFlag = false,
                    Source =
                        "class L { void M() {\n" +
                        "  FlowTrace.Fail(Sys, $\"'{address}' must never reach " + call + "\");\n" +
                        "} }",
                },
            };
        }

        /// <summary>Both directions for <see cref="DiscoveredZeroIsRefused"/>.</summary>
        private static List<OracleCase> DiscoveryOracleCases()
        {
            return new List<OracleCase>
            {
                new OracleCase
                {
                    Name = "clean: guarded on discovery, Fails loudly",
                    ShouldFlag = true,
                    Source =
                        "if (discovered == 0) {\n" +
                        "  FlowTrace.Fail(System, \"no enemy addresses\");\n" +
                        "  return EnemyContentState.Degraded;\n" +
                        "}\n" +
                        "return EnemyContentState.Ready;",
                },
                new OracleCase
                {
                    Name = "known-bad: a ternary that claims Ready unconditionally",
                    ShouldFlag = false,
                    Source = "State = EnemyContentState.Ready;",
                },
                new OracleCase
                {
                    Name = "known-bad: degrades silently, never reports",
                    ShouldFlag = false,
                    Source =
                        "if (discovered == 0) return EnemyContentState.Degraded;\n" +
                        "return EnemyContentState.Ready;",
                },
                new OracleCase
                {
                    Name = "known-bad: tests discovery but still calls it Ready",
                    ShouldFlag = false,
                    Source =
                        "if (discovered == 0) FlowTrace.Warn(System, \"empty\");\n" +
                        "return EnemyContentState.Ready;",
                },
            };
        }

        /// <summary>Both directions for <see cref="BulkWarms"/> — the owner's per-family ruling.</summary>
        private static List<OracleCase> BulkWarmOracleCases()
        {
            return new List<OracleCase>
            {
                new OracleCase
                {
                    Name = "known-bad: the structure warmer's whole-catalog download",
                    ShouldFlag = true,
                    Source = "var dl = Addressables.DownloadDependenciesAsync(keys, Addressables.MergeMode.Union, false);",
                },
                new OracleCase
                {
                    Name = "known-bad: the structure warmer's load-everything loop",
                    ShouldFlag = true,
                    Source = "for (int i = 0; i < keys.Count; i++) Request(keys[i]);",
                },
                new OracleCase
                {
                    Name = "clean: discovery only, then decide",
                    ShouldFlag = false,
                    Source =
                        "foreach (var locator in Addressables.ResourceLocators) { keys.Add(k); }\n" +
                        "State = DecideState(keys.Count);",
                },
            };
        }

        // =====================================================================
        //  Plumbing
        // =====================================================================

        private static string Abs(string assets, string rel) =>
            Path.Combine(assets, rel.Replace('/', Path.DirectorySeparatorChar));

        /// <summary>
        /// CODE ONLY — comments and string-literal contents blanked. Shared with the rest of the
        /// suite family (ComposedDungeonRunRegression owns the scanner) so there is ONE stripper to
        /// be correct rather than six that drift.
        /// </summary>
        private static string Code(string source) =>
            ComposedDungeonRunRegression.StripCommentsAndStrings(source ?? string.Empty);

        private static void RequireToken(List<string> failures, string code, string token,
                                         string relPath, string why)
        {
            if (code.IndexOf(token, StringComparison.Ordinal) >= 0) return;
            failures.Add($"Assets/{relPath} no longer contains '{token}' — {why}.");
        }

        /// <summary>
        /// Balanced-brace body of the first method whose signature contains
        /// <paramref name="signatureNeedle"/>. Runs on ALREADY-BLANKED source, so no brace inside a
        /// string or comment can throw the depth count off. Brace chars come from code points
        /// (123/125) to keep this file's own brace balance clean under CLAUDE.md sec.1.
        /// </summary>
        private static bool TryMethodBody(string code, string signatureNeedle, out string body)
        {
            body = null;
            if (string.IsNullOrEmpty(code)) return false;
            char openBrace = (char)123;
            char closeBrace = (char)125;
            int sig = code.IndexOf(signatureNeedle, StringComparison.Ordinal);
            if (sig < 0) return false;
            int open = code.IndexOf(openBrace, sig);
            if (open < 0) return false;
            int depth = 0, i = open;
            for (; i < code.Length; i++)
            {
                if (code[i] == openBrace) depth++;
                else if (code[i] == closeBrace) { depth--; if (depth == 0) break; }
            }
            if (depth != 0) return false;
            body = code.Substring(open, i - open + 1);
            return true;
        }

        private static int Count(string haystack, string needle)
        {
            if (string.IsNullOrEmpty(haystack) || string.IsNullOrEmpty(needle)) return 0;
            int n = 0, i = 0;
            while ((i = haystack.IndexOf(needle, i, StringComparison.Ordinal)) >= 0) { n++; i += needle.Length; }
            return n;
        }

        /// <summary>
        /// The file's first preprocessor directive, trimmed — read from RAW text on purpose:
        /// directives are structure, not tokens, and the blanker does not touch them.
        /// </summary>
        private static string FirstDirective(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return string.Empty;
            foreach (var rawLine in raw.Split('\n'))
            {
                string line = rawLine.Trim();
                if (line.Length > 0 && line[0] == '#') return line;
            }
            return string.Empty;
        }

        private static string ReadOrNull(string path)
        {
            try { return File.Exists(path) ? File.ReadAllText(path) : null; }
            catch { return null; }
        }

        /// <summary>Standalone entry point (run-unity-method).</summary>
        public static void RunAll()
        {
            bool ok = Run(out string reason);
            if (!ok)
            {
                Debug.LogError(reason);
                EditorApplication.Exit(1);
                return;
            }
            Debug.Log(MarkerOk + " (enemy load path proven non-blocking, per-family, and self-re-skinning)");
        }
    }
}

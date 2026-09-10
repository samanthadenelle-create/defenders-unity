// =============================================================================
// VfxPickOverrides - WO-1348. THE RUNTIME SEAM that lets a VFX pick move from
// the Command Center instead of from a rebuild.
// -----------------------------------------------------------------------------
// Assembly: DeNelle.Core   Namespace: DeNelle.Core.Vfx
//
// Owner ask 2026-09-03, verbatim:
//   "is it possible to tag those from the command center? and then change
//    pointer on next town load?"   "realm.vfx(set)"   "that idea"
// answered YES, with her namespace proposal adopted as the key shape verbatim.
//
// Standing ruling behind it (2026-09-02):
//   "be smart, dont make it need a code change, make it tweakable from a db call"
//   "i have been screaming this for months."
//
// -----------------------------------------------------------------------------
// WHAT THIS CHANGES, AND WHAT IT DELIBERATELY DOES NOT.
// -----------------------------------------------------------------------------
// It changes WHICH SHIPPED EFFECT A KEY USES. It cannot change WHICH EFFECTS
// EXIST. Adding a new prefab to the pool is still a build, and the Command Center
// says so on the page - because CLAUDE.md section 16 records this project being
// bitten THREE TIMES by art that was addressable-but-never-pushed: the build
// installs, launches and plays with tinted capsules and NO ERROR ON SCREEN. A
// picker that could offer an unshipped prefab would rebuild that failure mode in
// a new place.
//
// The pool is therefore the owner's OWN TAGGED KEYS (Assets/Editor/
// VfxManualPicks.json -> Assets/Resources/VFX/vfx-pick-options.generated.json),
// every one of which the catalog generator has already resolved into
// Assets/Resources/VFX/HovlVfxCatalog.asset. Option N is SHIPPED BY CONSTRUCTION:
// "render key K with the prefab that key <option N> already renders with".
//
// -----------------------------------------------------------------------------
// (!) THE VALUE IS A STABLE ID, NOT A SORTED POSITION.
// -----------------------------------------------------------------------------
// The tunables rail is Int-only (DeNelle.Core.Ops.TunableKind is Bool|Int), so
// the row carries an integer. That integer is an APPEND-ONLY id assigned once by
// tools/gen-vfx-pick-options.mjs and never reassigned. A sorted position would
// shift the day anybody tags a new key, silently re-pointing a row she set last
// week while the trace said "override applied" - a LYING TRACE, which is the one
// thing this ticket's instrumentation section forbids by name.
//
// -----------------------------------------------------------------------------
// (!) THE INVARIANT THAT OUTRANKS EVERYTHING ELSE HERE:
//     NO ROW, NO NETWORK, NO PARSE, NO OPTIONS FILE => THE BUILD-TIME PICK,
//     BYTE FOR BYTE.
// -----------------------------------------------------------------------------
// Every path in this file that cannot complete returns "no override" and the
// caller keeps the row the catalog already had. Assets/Editor/VfxManualPicks.json
// remains the DEFAULT and the RECORD; nothing here deletes it, bypasses it, or
// writes to it. Id 0 - the shipping default of every realm.vfx.* knob - means
// "use the build-time pick", so an empty client_tunables table reproduces today's
// behaviour exactly.
//
// -----------------------------------------------------------------------------
// WHEN IT APPLIES: THE NEXT TOWN LOAD. Her boundary, and it is the right one.
// -----------------------------------------------------------------------------
// The snapshot is taken on scene load and NOT re-read per play. Already-spawned
// particle systems are never re-parented or hot-swapped - the work order forbids
// that outright. RemoteTunablesService applies a PlayerPrefs-CACHED payload at
// BeforeSceneLoad (RemoteTunablesService.cs:147), so a row fetched during one
// session is standing from frame zero of the next launch, which is exactly what
// "next town load" has to mean for it to be believable.
//
// -----------------------------------------------------------------------------
// NO SILENT ANYTHING (CLAUDE.md section 12). Every resolution traces the key, the
// id, the SOURCE (local-playerprefs / remote / remote-cached / build-default) and,
// on a fallback, WHY. Without the source field "the override did not work" and
// "the override worked and the art is subtle" are indistinguishable - the work
// order names that as the single most expensive ambiguity in this lane.
//
// ASCII only. FlowTrace tag "VfxPicks". Never strip it (CLAUDE.md section 12).
// =============================================================================

using System;
using System.Collections.Generic;
using DeNelle.Core.Diagnostics;
using DeNelle.Core.Ops;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DeNelle.Core.Vfx
{
    /// <summary>One row of the generated option pool: a stable id naming a tagged VFX key.</summary>
    [Serializable]
    public sealed class VfxPickOption
    {
        /// <summary>Stable, append-only. Never reassigned. 0 is reserved for "build-time pick".</summary>
        public int id;

        /// <summary>The VFX catalog key whose prefab this option lends.</summary>
        public string key;

        /// <summary>Prefab leaf name - display only, never used to resolve anything.</summary>
        public string prefab;

        /// <summary>True when the lent prefab is a looping effect.</summary>
        public bool isLoop;

        /// <summary>True when the key has left the tag file. Its id stays reserved, never reused.</summary>
        public bool retired;

        /// <summary>
        /// True when the key is still TAGGED by the owner but this build's HovlVfxCatalog cannot
        /// resolve it to a prefab - normally a gitignored art pack that was not imported when the
        /// catalog was baked. Never offered and never honoured: choosing it would render nothing
        /// with no error on screen (CLAUDE.md section 16). Its id stays reserved, so importing the
        /// pack and rebaking makes the SAME id offerable again.
        /// </summary>
        public bool unresolved;
    }

    /// <summary>Serialization shape of vfx-pick-options.generated.json.</summary>
    [Serializable]
    internal sealed class VfxPickOptionsFile
    {
        public string source;
        public List<VfxPickOption> options;
    }

    /// <summary>
    /// The result of asking "does the database want a different prefab for this key?".
    /// A struct so the hot path allocates nothing.
    /// </summary>
    public struct VfxPickResolution
    {
        /// <summary>True when a usable override was found and the caller should honour it.</summary>
        public bool HasOverride;

        /// <summary>The catalog key whose prefab should be borrowed. Null when no override.</summary>
        public string SourceKey;

        /// <summary>The id the row carried.</summary>
        public int OptionId;

        /// <summary>"local-playerprefs" | "remote" | "remote-cached" | "build-default".</summary>
        public string Source;

        /// <summary>Plain-English reason a present row was NOT honoured. Null when nothing fell back.</summary>
        public string FallbackReason;
    }

    /// <summary>
    /// Resolves a VFX catalog key to a possibly-overridden option, from the remote tunables
    /// rail. Always answers, never throws, and the failure answer is always "no override" -
    /// i.e. the build-time pick, unchanged.
    /// </summary>
    public static class VfxPickOverrides
    {
        /// <summary>FlowTrace system tag for the whole VFX pick lane.</summary>
        public const string Sys = "VfxPicks";

        /// <summary>Her namespace, adopted verbatim. A knob is "realm.vfx." + the VFX catalog key.</summary>
        public const string TunablePrefix = "realm.vfx.";

        /// <summary>Resources-relative key of the generated option pool (a TextAsset).</summary>
        public const string OptionsResourceKey = "VFX/vfx-pick-options.generated";

        /// <summary>The id meaning "no override - use the build-time pick". Never an option id.</summary>
        public const int BuildDefaultId = 0;

        // ---------------------------------------------------------------------
        //  STATE. Swapped atomically, never mutated in place.
        // ---------------------------------------------------------------------

        private static Dictionary<int, VfxPickOption> s_optionsById;
        private static bool s_optionsLoadAttempted;

        /// <summary>vfxKey -> the id the standing snapshot resolved for it. Only non-zero ids land here.</summary>
        private static Dictionary<string, int> s_snapshot;

        /// <summary>vfxKey -> the provenance word RemoteTunables reported for its knob.</summary>
        private static Dictionary<string, string> s_snapshotSource;

        /// <summary>Bumped on every snapshot that CHANGES the standing answer for at least one key.</summary>
        public static int Generation { get; private set; }

        /// <summary>
        /// Raised after a snapshot that changed at least one key's answer, carrying those keys.
        /// The VFX pool subscribes so idle pooled instances of a re-picked key are dropped -
        /// which is NOT the live hot-swap the work order forbids: nothing spawned is touched.
        /// </summary>
        public static event Action<IReadOnlyList<string>> SnapshotChanged;

        /// <summary>Number of keys currently carrying a non-default pick. 0 = today's behaviour.</summary>
        public static int OverrideCount => s_snapshot == null ? 0 : s_snapshot.Count;

        /// <summary>Options parsed from the generated pool. Empty when the file is missing.</summary>
        public static int OptionCount
        {
            get { EnsureOptions(); return s_optionsById == null ? 0 : s_optionsById.Count; }
        }

        // =====================================================================
        //  BOOT - snapshot on every scene load, and only on scene load.
        // =====================================================================

        /// <summary>
        /// Subscribe the snapshot to scene loads. AfterSceneLoad so
        /// <see cref="RemoteTunablesService"/>'s BeforeSceneLoad cached-payload apply has
        /// already run - otherwise the very first town of a launch would resolve every knob
        /// to its default and the override would look exactly like "it did not work".
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            Guard.Try(Sys, "subscribe VFX pick snapshot to scene loads", () =>
            {
                SceneManager.sceneLoaded -= OnSceneLoaded;
                SceneManager.sceneLoaded += OnSceneLoaded;
            });
            Snapshot("boot");
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
            => Snapshot("sceneLoaded:" + (scene.name ?? "?"));

        /// <summary>
        /// Re-read every <c>realm.vfx.*</c> knob and stand the answer up for the scene that just
        /// loaded. Cheap (one registry walk), never throws, and traces the whole configuration on
        /// one line so a felt-test capture always says which picks produced it.
        /// </summary>
        public static void Snapshot(string why)
        {
            Guard.Try(Sys, "snapshot VFX pick overrides (" + (why ?? "?") + ")", () =>
            {
                EnsureOptions();

                var next = new Dictionary<string, int>(StringComparer.Ordinal);
                var nextSource = new Dictionary<string, string>(StringComparer.Ordinal);

                var registry = RemoteTunables.Registry;
                for (int i = 0; i < registry.Length; i++)
                {
                    string tunableKey = registry[i].Key;
                    if (tunableKey == null || !tunableKey.StartsWith(TunablePrefix, StringComparison.Ordinal))
                        continue;

                    string vfxKey = tunableKey.Substring(TunablePrefix.Length);
                    int id = RemoteTunables.Int(tunableKey);
                    if (id == BuildDefaultId) continue;   // the shipping default: build-time pick

                    next[vfxKey] = id;
                    nextSource[vfxKey] = ProvenanceOf(tunableKey);
                }

                var changed = DiffKeys(s_snapshot, next);
                s_snapshot = next;
                s_snapshotSource = nextSource;

                if (changed.Count > 0)
                {
                    Generation++;
                    FlowTrace.Warn(Sys,
                        "SNAPSHOT (" + (why ?? "?") + "): generation=" + Generation + " overrides=" + next.Count +
                        " options=" + OptionCount + " changed=[" + string.Join(",", changed) + "] || at least one " +
                        "VFX key is NOT rendering its build-time pick. Quote this line in any felt-test report - " +
                        "it is the only record of which picks produced the run. Id 0 = the build-time pick.");
                    Guard.Try(Sys, "raise SnapshotChanged", () => SnapshotChanged?.Invoke(changed));
                }
                else
                {
                    FlowTrace.Step(Sys,
                        "SNAPSHOT (" + (why ?? "?") + "): generation=" + Generation + " overrides=" + next.Count +
                        " options=" + OptionCount + " || nothing changed since the last snapshot" +
                        (next.Count == 0
                            ? "; EVERY VFX key is at its build-time pick - this is TODAY'S BEHAVIOUR, unchanged."
                            : "."));
                }
            });
        }

        /// <summary>
        /// Which of the three layers actually produced this knob's value, in the words
        /// RemoteTunables itself prints. A PlayerPrefs override is the most specific layer and
        /// wins, exactly as <c>RemoteTunables.Int</c> resolves it - reporting "remote" for a value
        /// a human typed at the device would send the next reader to the database for a row that
        /// is not there.
        /// </summary>
        private static string ProvenanceOf(string tunableKey)
        {
            bool hasLocal = Guard.Try(Sys, "probe local override " + tunableKey,
                () => PlayerPrefs.HasKey(RemoteTunables.LocalPrefix + tunableKey), false);
            if (hasLocal) return RemoteTunables.ProvenanceLocal;
            return RemoteTunables.TableProvenance;
        }

        /// <summary>Keys whose standing answer differs between two snapshots. Never null.</summary>
        private static List<string> DiffKeys(Dictionary<string, int> before, Dictionary<string, int> after)
        {
            var changed = new List<string>();
            if (before != null)
            {
                foreach (var kv in before)
                    if (!after.TryGetValue(kv.Key, out int now) || now != kv.Value) changed.Add(kv.Key);
            }
            foreach (var kv in after)
            {
                if (before == null || !before.ContainsKey(kv.Key)) { if (!changed.Contains(kv.Key)) changed.Add(kv.Key); }
            }
            changed.Sort(StringComparer.Ordinal);
            return changed;
        }

        // =====================================================================
        //  THE OPTION POOL
        // =====================================================================

        /// <summary>
        /// Load and parse the generated option pool ONCE. A missing or unreadable file is a
        /// Warn and an EMPTY pool - which resolves every key to its build-time pick, i.e. the
        /// invariant holds even when this file is the thing that is broken.
        /// </summary>
        private static void EnsureOptions()
        {
            if (s_optionsLoadAttempted) return;
            s_optionsLoadAttempted = true;

            Guard.Try(Sys, "load " + OptionsResourceKey, () =>
            {
                var text = VfxAssetLoader.LoadVfxAsset<TextAsset>(OptionsResourceKey);
                if (text == null || string.IsNullOrEmpty(text.text))
                {
                    FlowTrace.Warn(Sys,
                        "option pool '" + OptionsResourceKey + "' did not load - EVERY realm.vfx.* row is " +
                        "ignored and every key renders its BUILD-TIME pick. Nothing is broken; the picker " +
                        "simply has nothing to offer. Regenerate with: node tools/gen-vfx-pick-options.mjs");
                    return;
                }

                var file = JsonConvert.DeserializeObject<VfxPickOptionsFile>(text.text);
                if (file == null || file.options == null || file.options.Count == 0)
                {
                    FlowTrace.Warn(Sys,
                        "option pool '" + OptionsResourceKey + "' parsed to no options - every key renders " +
                        "its BUILD-TIME pick.");
                    return;
                }

                var map = new Dictionary<int, VfxPickOption>(file.options.Count);
                foreach (var opt in file.options)
                {
                    if (opt == null || opt.id <= 0 || string.IsNullOrEmpty(opt.key)) continue;
                    map[opt.id] = opt;
                }
                s_optionsById = map;
                FlowTrace.Step(Sys,
                    "option pool loaded: " + map.Count + " shipped option(s) from '" + OptionsResourceKey +
                    "' (source " + (file.source ?? "?") + "). An option names a key the owner ALREADY tagged, " +
                    "so every offer is shipped by construction.");
            });
        }

        /// <summary>Look an option up by its stable id. False for 0, an unknown id, or no pool.</summary>
        public static bool TryGetOption(int id, out VfxPickOption option)
        {
            option = null;
            if (id <= BuildDefaultId) return false;
            EnsureOptions();
            return s_optionsById != null && s_optionsById.TryGetValue(id, out option);
        }

        // =====================================================================
        //  THE READ SIDE - always answers, and the failure answer is "no override"
        // =====================================================================

        /// <summary>
        /// Does the standing snapshot want a different prefab for <paramref name="vfxKey"/>?
        /// <para>
        /// NEVER throws. Returns <c>HasOverride=false</c> for: no snapshot, no row, id 0, an
        /// unknown id, a retired option, or an option naming the key itself. Every one of those
        /// is traced with the reason, because the caller silently keeping the build-time pick is
        /// exactly the ambiguity this lane exists to remove.
        /// </para>
        /// </summary>
        public static VfxPickResolution Resolve(string vfxKey)
        {
            var res = new VfxPickResolution
            {
                HasOverride = false,
                SourceKey = null,
                OptionId = BuildDefaultId,
                Source = RemoteTunables.ProvenanceDefault,
                FallbackReason = null,
            };
            if (string.IsNullOrEmpty(vfxKey)) return res;

            var snap = s_snapshot;
            if (snap == null || !snap.TryGetValue(vfxKey, out int id) || id == BuildDefaultId)
                return res;   // the overwhelmingly common path: today's behaviour, no trace noise

            res.OptionId = id;
            res.Source = ResolveSourceWord(vfxKey);

            if (!TryGetOption(id, out var option))
            {
                res.FallbackReason = "option id " + id + " is not in the shipped pool (" + OptionCount +
                                     " option(s)) - the row names something this BUILD cannot resolve";
                WarnFallback(vfxKey, res);
                return res;
            }
            if (option.retired)
            {
                res.FallbackReason = "option id " + id + " ('" + option.key + "') is RETIRED - its key left " +
                                     "the tag file, and its id is reserved rather than reused";
                WarnFallback(vfxKey, res);
                return res;
            }
            if (option.unresolved)
            {
                res.FallbackReason = "option id " + id + " ('" + option.key + "') is TAGGED by the owner but " +
                                     "UNRESOLVABLE in this build's HovlVfxCatalog - almost always a gitignored " +
                                     "art pack that was not imported when the catalog was baked. Import the " +
                                     "pack, re-run Defenders/VFX/Generate Hovl VFX Catalog, then " +
                                     "node tools/gen-vfx-pick-options.mjs, and this SAME id becomes offerable";
                WarnFallback(vfxKey, res);
                return res;
            }
            if (string.Equals(option.key, vfxKey, StringComparison.Ordinal))
            {
                res.FallbackReason = "option id " + id + " names the key itself, which is the build-time pick";
                WarnFallback(vfxKey, res);
                return res;
            }

            res.HasOverride = true;
            res.SourceKey = option.key;
            return res;
        }

        /// <summary>The provenance word RemoteTunables reported for this key's knob at snapshot time.</summary>
        private static string ResolveSourceWord(string vfxKey)
        {
            var src = s_snapshotSource;
            if (src != null && src.TryGetValue(vfxKey, out string word) && !string.IsNullOrEmpty(word))
                return word;
            return RemoteTunables.ProvenanceRemote;
        }

        /// <summary>
        /// THROTTLED, not Warn-per-call, and not Once either - deliberately, and it is the rail's
        /// own precedent (<c>RemoteTunables.Int</c> throttles its bad-row line at 30 s for exactly
        /// this reason). <see cref="Resolve"/> runs on EVERY play of the key, so a plain Warn would
        /// flood the log and evict the boot window out of the device logcat ring - destroying the
        /// evidence the instrumentation exists to capture (memory: logcat-ring-buffer-destroys-
        /// evidence). <c>Once</c> would be worse in the other direction: the row can be corrected
        /// live from the Command Center, and a reader needs to see that the bad value is STILL
        /// there rather than a single line from ten minutes ago.
        /// </summary>
        private static void WarnFallback(string vfxKey, VfxPickResolution res)
        {
            FlowTrace.Throttle(Sys, "fallback:" + vfxKey + "=" + res.OptionId, 30f,
                "FELL BACK for key '" + vfxKey + "': " + res.FallbackReason + ". source=" + res.Source +
                " || the BUILD-TIME pick is rendering, unchanged - nothing is missing on screen. Fix the row " +
                "in the Command Center; a prefab that was never shipped can never be reached from a database.");
        }

        /// <summary>
        /// Say, in one line, what a key actually resolved to and WHERE THAT CAME FROM. Called by the
        /// catalog after it has applied (or declined) an override, once per key per outcome.
        /// <para>
        /// (!) The SOURCE field is the load-bearing half. Without it, "the override did not work" and
        /// "the override worked and the art is subtle" are indistinguishable in a capture - the work
        /// order names that as the most expensive ambiguity in this whole lane.
        /// </para>
        /// </summary>
        public static void TraceApplied(string vfxKey, VfxPickResolution res, string prefabName, bool created)
        {
            FlowTrace.Once(Sys, "applied:" + vfxKey + "=" + res.OptionId + "@" + res.Source,
                "KEY " + vfxKey + " -> prefab '" + (prefabName ?? "null") + "' via option id " + res.OptionId +
                " ('" + (res.SourceKey ?? "?") + "')  source=" + res.Source +
                (created
                    ? " || this key had NO build-time entry - the pick CREATED it, borrowing the option's " +
                      "loop/scale/lifetime. It renders nothing at all without this row."
                    : " || this is an OVERRIDE of the build-time pick. Setting the row to 0 restores it.") +
                " Takes effect on town load only; already-spawned effects are never re-parented.");
        }

        /// <summary>Test seam: drop every cached decision so a suite can drive a fresh snapshot.</summary>
        public static void ResetForTests()
        {
            s_snapshot = null;
            s_snapshotSource = null;
            s_optionsById = null;
            s_optionsLoadAttempted = false;
            Generation = 0;
        }
    }
}

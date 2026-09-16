// =============================================================================
// CollectorStackPropCatalogRegression [collector-props]
// -----------------------------------------------------------------------------
// Assembly: DeNelle.EditorRegression   Namespace: DeNelle.Editor.Regression
// Markers:  COLLECTOR_PROPS_OK / COLLECTOR_PROPS_FAIL
//
// THE SILENT FAILURE THIS ORACLE EXISTS TO KILL.
//
// CollectorStackPropCatalog.cs shipped with WO-665a carrying a comment that says
// "Place the resulting asset at Assets/Resources/Collectors/CollectorStackPropCatalog",
// and for months nobody did. The folder did not exist. Git history shows the asset
// was never added and never deleted on any branch. So CollectorStackView.EnsureCatalog()
// resolved null on EVERY run and every farm / lumbermill / forge in the town drew the
// abstract fill bar - the diegetic prop pile that was the entire headline of that work
// order never rendered a single time, and NOTHING went red about it. The fallback is
// good engineering (a fresh clone is never blank) and it is exactly what made the
// defect invisible: a graceful degradation with no gate over it is indistinguishable
// from working software.
//
// SO THIS ORACLE PINS BOTH BRANCHES, which is the whole point:
//   1. THE ASSET IS THERE, at the exact path the runtime loads, with a row for each
//      resource the owner picked a prop for (Wood / Food / Iron, stated 2026-08-16:
//      "log sack of flour and iron bar").
//   2. THE PICKS ARE COMMITTED, proven from the .asset FILE TEXT - a non-zero prop
//      GUID per row. This is deliberately a TEXT assertion, not a loaded-reference
//      assertion: KayKit Resource Bits is gitignored (.gitignore:106), so on a machine
//      without the pack the GUID resolves to null and a loaded-reference check would
//      go red for a reason that is not a defect. The bytes in git are the thing that
//      must not regress; whether this particular machine can resolve them is not.
//   3. THE FALLBACK STILL WORKS, asserted behaviourally against a real catalog
//      instance: a row with a null Prop and an unmapped resource must BOTH make
//      TryGet return false, so a pack-less machine gets the fill bar rather than a
//      null-prop Instantiate throw. If someone "simplifies" TryGet to return true on
//      a matched row, this goes red the same day.
//   4. THE VIEW STILL HAS THE FALLBACK WIRING, source-linted with comments and string
//      literals STRIPPED FIRST. Unstripped matching is worthless here because both
//      files' comment blocks discuss the fallback at length - an oracle that matched
//      them would pass on the prose while the code was gone.
//
// Deterministic, editor-only asset + source reads. No scene, no PlayMode.
//
// Registered in DataRegression.RunAll (covenant style):
//   Guard.Try(... CollectorStackPropCatalogRegression.Run(out var r) ...)
// =============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using DeNelle.Core.Diagnostics;
using DeNelle.Village.Buildings.Progression;
using UnityEditor;
using UnityEngine;

namespace DeNelle.Editor.Regression
{
    public static class CollectorStackPropCatalogRegression
    {
        private const string FlowSys = "CollectorProps";

        private const string MarkerOk   = "COLLECTOR_PROPS_OK";
        private const string MarkerFail = "COLLECTOR_PROPS_FAIL";

        /// <summary>The one path the runtime loads. Derived from the runtime constant, never retyped.</summary>
        private static string CatalogAssetPath =>
            "Assets/Resources/" + CollectorStackPropCatalog.ResourcesPath + ".asset";

        private const string ViewSourcePath    = "Assets/_Modules/Village/Buildings/Progression/CollectorStackView.cs";
        private const string CatalogSourcePath = "Assets/_Modules/Village/Buildings/Progression/CollectorStackPropCatalog.cs";

        /// <summary>The resources the owner named a prop for on 2026-08-16 ("log sack of flour and iron bar").
        /// Crystals is NOT here on purpose - she named three props, not four, and an unwired resource
        /// legitimately takes the fill-bar fallback.</summary>
        private static readonly HarvestResource[] RequiredWired =
        {
            HarvestResource.Wood,
            HarvestResource.Stone,
            HarvestResource.Iron,
        };

        // =====================================================================
        //  Entry points
        // =====================================================================

        /// <summary>Standalone batch entry point.</summary>
        public static void RunStandalone()
        {
            string reason;
            bool pass = Run(out reason);
            Debug.Log("[collector-props] standalone result: " + (pass ? "PASS" : "FAIL") + " - " + reason);
        }

        /// <summary>DataRegression-shaped contract. NEVER throws.</summary>
        public static bool Run(out string reason)
        {
            try
            {
                return RunCore(out reason);
            }
            catch (Exception ex)
            {
                reason = "collector-props: oracle threw " + ex.GetType().Name + ": " + ex.Message;
                Debug.LogError(MarkerFail + " - " + reason);
                return false;
            }
        }

        // =====================================================================
        //  Body
        // =====================================================================
        private static bool RunCore(out string reason)
        {
            using var _scope = FlowTrace.Enter(FlowSys, "CollectorStackPropCatalogRegression.RunCore");

            var failures = new List<string>();
            var notes    = new List<string>();
            var log      = new StringBuilder();
            log.AppendLine("--- COLLECTOR STACK PROP CATALOG (asset present + picks committed + fallback intact) ---");

            // -- 1. the asset exists at the exact path Resources.Load resolves ---------
            string path = CatalogAssetPath;
            bool onDisk = File.Exists(path);
            log.Append("asset path=").Append(path).Append(" onDisk=").Append(onDisk).AppendLine();

            if (!onDisk)
            {
                FlowTrace.Fail(FlowSys, "catalog asset ABSENT at " + path);
                failures.Add("NO catalog asset at " + path + ". CollectorStackView.EnsureCatalog() therefore " +
                             "resolves null and EVERY collector in the town silently draws the abstract fill " +
                             "bar instead of its diegetic prop pile - the exact months-long invisible defect " +
                             "this oracle exists to stop recurring. Run " +
                             "DeNelle.Editor.CollectorStackPropCatalogBuilder.Build to create + wire it.");
            }

            var catalog = onDisk
                ? AssetDatabase.LoadAssetAtPath<CollectorStackPropCatalog>(path)
                : null;

            if (onDisk && catalog == null)
            {
                FlowTrace.Fail(FlowSys, "asset at " + path + " is not a CollectorStackPropCatalog");
                failures.Add("The file at " + path + " exists but does NOT load as a " +
                             "CollectorStackPropCatalog (wrong script GUID, or the asset is corrupt). " +
                             "Resources.Load returns null for it at runtime, which is the same blank-prop " +
                             "outcome as no asset at all - but silent, because the file is right there.");
            }

            // -- 2. every owner-picked resource has a row, with a COMMITTED prop GUID --
            // Parsed from the file TEXT so the assertion holds on a machine without the
            // gitignored KayKit pack (where the reference resolves to null but the bytes
            // in git are perfectly correct).
            Dictionary<int, string> textGuids = null;
            if (onDisk)
            {
                var parsed = new Dictionary<int, string>();
                Guard.Try(FlowSys, "parse catalog YAML", () => { parsed = ParsePropGuids(path); });
                textGuids = parsed;
                log.Append("rows in file text=").Append(textGuids.Count).AppendLine();

                for (int i = 0; i < RequiredWired.Length; i++)
                {
                    var res = RequiredWired[i];
                    string guid;
                    bool has = textGuids.TryGetValue((int)res, out guid) && !string.IsNullOrEmpty(guid);
                    log.Append("  ").Append(res).Append(" committedGuid=")
                       .Append(has ? guid : "<NONE>").AppendLine();

                    if (!has)
                    {
                        FlowTrace.Fail(FlowSys, "row " + res + " has no committed prop GUID");
                        failures.Add(res + " has NO prop wired in " + path + " (no non-zero GUID on its Prop " +
                                     "field). The owner picked a prop for it on 2026-08-16 (\"log sack of " +
                                     "flour and iron bar\"); an empty row means that collector renders the " +
                                     "abstract fill bar forever and nothing says so. Re-run " +
                                     "CollectorStackPropCatalogBuilder.Build on a machine with the KayKit " +
                                     "Resource Bits pack imported.");
                    }
                }
            }

            // -- 3. resolution on THIS machine is a NOTE, never a failure --------------
            // Canon: a missing gitignored pack asset is a warning, never an error.
            if (catalog != null)
            {
                int resolved = 0, unresolved = 0;
                for (int i = 0; i < RequiredWired.Length; i++)
                {
                    var res = RequiredWired[i];
                    CollectorStackPropCatalog.Entry entry;
                    bool live = catalog.TryGet(res, out entry);
                    if (live)
                    {
                        resolved++;
                        if (entry.PropScale <= 0f)
                            failures.Add(res + " has a wired prop but PropScale=" + entry.PropScale +
                                         " (<= 0). The view falls back to scale 1, which for a KayKit " +
                                         "Resource Bits model is far too large for one cell of the " +
                                         "4-column brick grid - the pile interpenetrates itself.");
                        if (entry.SlotSize.sqrMagnitude <= 0.0001f)
                            failures.Add(res + " has a wired prop but a zero SlotSize; the pile footprint " +
                                         "silently reverts to the view's hardcoded default, so the catalog " +
                                         "is not actually controlling the layout it claims to control.");
                    }
                    else
                    {
                        unresolved++;
                    }
                }
                log.Append("resolved on this machine=").Append(resolved)
                   .Append(" unresolved=").Append(unresolved).AppendLine();

                if (unresolved > 0)
                {
                    FlowTrace.Warn(FlowSys, unresolved + " owner-picked prop(s) do not resolve on this machine " +
                                            "(KayKit Resource Bits is gitignored) - fill-bar fallback in effect");
                    notes.Add(unresolved + " of " + RequiredWired.Length + " owner-picked prop(s) do not resolve " +
                              "on THIS machine because KayKit Resource Bits is gitignored (.gitignore:106). " +
                              "Those collectors take the abstract fill-bar fallback here. NOT a failure - the " +
                              "committed GUIDs above are the thing under gate.");
                }
            }

            // -- 4. the fallback branch is behaviourally intact ------------------------
            // A null-Prop row and an unmapped resource must BOTH make TryGet return false.
            // Without this, a pack-less machine reaches Instantiate(null) and throws.
            Guard.Try(FlowSys, "TryGet fallback semantics", () =>
            {
                var probe = ScriptableObject.CreateInstance<CollectorStackPropCatalog>();
                probe.Entries = new[]
                {
                    new CollectorStackPropCatalog.Entry
                    {
                        Resource = HarvestResource.Wood, Prop = null,
                        PropScale = 1f, SlotSize = new Vector3(1.2f, 1f, 0.6f),
                    },
                };

                CollectorStackPropCatalog.Entry got;
                if (probe.TryGet(HarvestResource.Wood, out got))
                    failures.Add("TryGet returned TRUE for a row whose Prop is NULL. CollectorStackView " +
                                 "gates the prop path on that return value, so this makes it Instantiate(null) " +
                                 "and throw on every machine without the gitignored pack - the exact case the " +
                                 "fill-bar fallback exists to absorb.");

                if (probe.TryGet(HarvestResource.Crystals, out got))
                    failures.Add("TryGet returned TRUE for a resource that has NO row at all. Crystals is " +
                                 "deliberately unwired (the owner named three props, not four) and MUST take " +
                                 "the fill-bar fallback.");

                probe.Entries = null;
                if (probe.TryGet(HarvestResource.Wood, out got))
                    failures.Add("TryGet returned TRUE against a NULL Entries array.");

                UnityEngine.Object.DestroyImmediate(probe);
            });

            // -- 5. the view still routes both branches (source lint, STRIPPED first) --
            // Comments and string literals are removed before any matching: both source
            // files discuss the fallback at length in prose, and an oracle that matched
            // the prose would pass while the code was gone. Three oracles gave false
            // positives that exact way on 2026-08-16.
            string viewSrc = StrippedSource(ViewSourcePath, failures);
            if (viewSrc != null)
            {
                RequireInSource(viewSrc, ViewSourcePath, "s_catalog != null", 1, failures,
                    "the no-catalog branch: Build() must null-check the loaded catalog before using it");
                RequireInSource(viewSrc, ViewSourcePath, "BuildFallbackBar", 3, failures,
                    "the declaration plus BOTH call sites (Build()'s else, and BuildProps()'s null-prop " +
                    "guard). Fewer than three means one of the two fallback routes was removed and some " +
                    "collector can now render nothing at all");
                RequireInSource(viewSrc, ViewSourcePath, "entry.Prop != null", 1, failures,
                    "Build() must re-check the prop is non-null even after TryGet says yes");
                RequireInSource(viewSrc, ViewSourcePath, "StripColliders", 2, failures,
                    "instanced props are decoration and must never carry live colliders that intercept " +
                    "siege contact or building clicks");
            }

            string catSrc = StrippedSource(CatalogSourcePath, failures);
            if (catSrc != null)
            {
                RequireInSource(catSrc, CatalogSourcePath, "entry.Prop != null", 1, failures,
                    "TryGet must report a prefab-less row as NOT FOUND, which is what routes a pack-less " +
                    "machine to the fill bar instead of Instantiate(null)");
            }

            // -- verdict --------------------------------------------------------------
            if (failures.Count > 0)
            {
                FlowTrace.Fail(FlowSys, "failures=" + failures.Count);
                reason = "collector-props FAIL: " + failures.Count + " problem(s). || " +
                         string.Join(" | ", failures.ToArray());
                Debug.LogError(log.ToString() + MarkerFail + " - " + reason);
                return false;
            }

            FlowTrace.Step(FlowSys, "catalog present + " + RequiredWired.Length +
                                    " owner pick(s) committed + fallback intact");
            reason = "collector-props OK - " + path + " exists, all " + RequiredWired.Length +
                     " owner-picked resource(s) (Wood/Food/Iron, selection stated 2026-08-16) carry a " +
                     "committed prop GUID, TryGet reports null-prop and unmapped rows as NOT FOUND so the " +
                     "abstract fill-bar fallback still absorbs a pack-less machine, and both fallback " +
                     "routes are still present in CollectorStackView (comment/string-stripped lint). " +
                     "Crystals is unwired ON PURPOSE and takes the bar." +
                     (notes.Count > 0 ? " NOTES: " + string.Join(" | ", notes.ToArray()) : string.Empty);
            Debug.Log(log.ToString() + MarkerOk + " - " + reason);
            return true;
        }

        // =====================================================================
        //  helpers
        // =====================================================================

        /// <summary>
        /// Map of HarvestResource ordinal -> the non-zero GUID on that row's Prop field, read
        /// straight out of the .asset YAML. Rows whose Prop is `{fileID: 0}` (unwired) are
        /// omitted, which is exactly the discriminator this oracle needs.
        /// </summary>
        private static Dictionary<int, string> ParsePropGuids(string assetPath)
        {
            var map = new Dictionary<int, string>();
            var lines = File.ReadAllLines(assetPath);

            var resRx  = new Regex(@"^\s*-?\s*Resource:\s*(-?\d+)\s*$");
            var propRx = new Regex(@"^\s*Prop:\s*\{.*\}\s*$");
            var guidRx = new Regex(@"guid:\s*([0-9a-fA-F]{32})");

            int current = int.MinValue;
            for (int i = 0; i < lines.Length; i++)
            {
                var rm = resRx.Match(lines[i]);
                if (rm.Success)
                {
                    int v;
                    current = int.TryParse(rm.Groups[1].Value, out v) ? v : int.MinValue;
                    continue;
                }

                if (current == int.MinValue) continue;
                if (!propRx.IsMatch(lines[i])) continue;

                var gm = guidRx.Match(lines[i]);
                if (gm.Success) map[current] = gm.Groups[1].Value;
                current = int.MinValue;   // one Prop per row
            }
            return map;
        }

        /// <summary>Source with // and /* */ comments and every string/char literal removed, so a
        /// lint can only ever match real CODE. Returns null (and records a failure) if unreadable.</summary>
        private static string StrippedSource(string sourcePath, List<string> failures)
        {
            if (!File.Exists(sourcePath))
            {
                failures.Add("source file MISSING: " + sourcePath +
                             " - this oracle cannot assert the fallback wiring it was written to pin, " +
                             "and a run that asserts nothing must not read green.");
                return null;
            }

            string src = null;
            Guard.Try(FlowSys, "read " + sourcePath, () => { src = File.ReadAllText(sourcePath); });
            if (src == null)
            {
                failures.Add("could not read " + sourcePath + ".");
                return null;
            }

            var sb = new StringBuilder(src.Length);
            int n = src.Length;
            for (int i = 0; i < n; i++)
            {
                char c = src[i];

                if (c == '/' && i + 1 < n && src[i + 1] == '/')
                {
                    while (i < n && src[i] != '\n') i++;
                    sb.Append('\n');
                    continue;
                }
                if (c == '/' && i + 1 < n && src[i + 1] == '*')
                {
                    i += 2;
                    while (i + 1 < n && !(src[i] == '*' && src[i + 1] == '/')) i++;
                    i++;                       // land on '/', the loop's i++ steps past it
                    sb.Append(' ');
                    continue;
                }
                if (c == '"' || c == '\'')
                {
                    char quote = c;
                    i++;
                    while (i < n && src[i] != quote)
                    {
                        if (src[i] == '\\') i++;   // skip the escaped char
                        i++;
                    }
                    sb.Append(' ');
                    continue;
                }
                sb.Append(c);
            }
            return sb.ToString();
        }

        /// <summary>Assert a code token appears at least <paramref name="min"/> times in stripped source.</summary>
        private static void RequireInSource(string stripped, string sourcePath, string token,
                                            int min, List<string> failures, string why)
        {
            int count = 0, idx = 0;
            while (true)
            {
                int hit = stripped.IndexOf(token, idx, StringComparison.Ordinal);
                if (hit < 0) break;
                count++;
                idx = hit + token.Length;
            }

            if (count >= min) return;

            FlowTrace.Fail(FlowSys, "source lint: '" + token + "' x" + count + " (need " + min + ") in " + sourcePath);
            failures.Add(sourcePath + " contains '" + token + "' only " + count + " time(s) in CODE " +
                         "(comments and string literals stripped); at least " + min + " required - " + why + ".");
        }
    }
}

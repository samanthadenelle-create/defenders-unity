// =============================================================================
// QueueJobCatalogCoverageRegression - WO-1655, 2026-09-10.
// EVERY id a Builder-channel queue job can carry RESOLVES A DISPLAY NAME in at
// least one catalog, so no queue row can ever again paint "Unknown structure".
// -----------------------------------------------------------------------------
// WHY THIS SUITE EXISTS
//
// Builds/wave5-manageflow1 (HEAD 2039e2c41), row 3 of the Builders queue, read
// "Unknown structure" with no thumbnail. The VM said so out loud in the same log:
//
//   [Flow:Manage] queue row catalog MISS: neither BuildingTierCatalog ('gate') nor
//   CatalogRegistry ('gate') has a display name for job 'gate:4:1' (channel Builder).
//   The player would otherwise read the raw id as a structure name
//
// ⛔ THE CATALOG WAS NOT THE DEFECT AND NOTHING WAS ADDED TO IT. The gate IS
// authored - structures-catalog.json row `gate_stone` / "Stone Gate" / type Gate -
// and WallRepairController.FallbackGateCatalogId is itself "gate_stone", so the
// repo already had exactly one canonical spelling. The FIXTURE used a different
// one: UICaptureLaunch.SeedManageCaptureQueue seeded the literal "gate:4:1", where
// "gate" is the damage-states TELL key (damage-states.json,
// WallRepairController.DamageTellKeyFor, RepairAvailabilityProbe.AddIfBurning<Gate>)
// - a different vocabulary that is not a catalog id anywhere. Its own sibling
// seeder, SeedManageFlowExtraQueue, already spoke the right language
// ("wall_wood:13:9"). Authoring a second `gate` row beside `gate_stone` would have
// minted a phantom palette tile and made CatalogRegistry.OfType(Gate) return two
// structures for one gate - the alias-by-another-name ManageArt.cs:306 forbids.
//
// ⚠ AND IT IS THE FOURTH TIME IN THAT ONE SEEDER. Its own comments record
// "tower_ground_archer:7:0" (a colon shape the game cannot produce, WO-1422), the
// invented perk 'warding' and the invented troop 'militia' (WO-1564). A fixture
// that speaks a language the game does not is not a test - and three prose warnings
// in a row did not stop the fourth. Only an oracle does.
//
// -----------------------------------------------------------------------------
// ⛔ RED PROOF - MEASURED BY DATA 2026-09-10, BEFORE THE FIX IN THE SAME CHANGE.
//
// Enumerated from the two authorities (no Unity needed; this lane had none):
//   building-tiers.json ladders  = arcane-tower, armorer, barracks, forge,
//                                  lumbermill, farm            -> no 'gate'
//   structures-catalog.json ids  = 29 rows incl. gate_stone,
//                                  wall_stone, wall_wood, ...  -> no 'gate'
// so the seeded literal "gate:4:1" resolved NEITHER catalog, which is precisely
// what the captured [Flow:Manage] line above says. Case [fixture-builder-job-id]
// therefore FAILS BY NAME against the tree as it stood, naming `gate` and the
// file:line that seeds it.
//
// MEASURED, not inferred: this file's exact rule (BuilderSeedRx + StripSuffixLikeVm
// + NormalizeLikeVm + the two JSON authorities) was ported line-for-line to python
// and run twice this session over the SAME tree - once against
// `git show HEAD:Assets/Editor/UICaptureLaunch.cs` and once against the working copy:
//
//   HEAD            ok 8168 tower_ground_archer -> Archer Tower
//                   ok 8169 barracks:2:0        -> Barracks
//                 FAIL 8170 gate:4:1            -> None          <-- RED, by name
//                   ok 9001 armorer:12:3        -> Armorer
//                   ok 9002 wall_wood:13:9      -> Wooden Palisade
//   working copy    ok 8198 gate_stone:4:1      -> Stone Gate     <-- GREEN
//                   (all five seeds resolve; 5 matched, 0 failures)
//
// ⚠ HONEST LIMIT (CLAUDE.md 11B.A): what ran was the PORT of the rule, not this C#.
// This lane runs no Unity, so `QUEUE_JOB_CATALOG_COVERAGE_OK` has never appeared on
// a log and must not be claimed until the gate produces one. To see the C# itself go
// red: put "gate:4:1" back at the SeedManageCaptureQueue Repair line and run
// DeNelle.Editor.Regression.DataRegression.RunAll.
//
// GREEN after the seeder was re-pointed to the canonical catalog id
// "gate_stone:4:1", which resolves "Stone Gate" AND, because
// ManageArt.BuildingPortraitKey takes the id VERBATIM, the portrait
// Assets/Resources/Portraits/Buildings/gate_stone.png that was already on disk.
//
// -----------------------------------------------------------------------------
// ⚠ THE WEAKER FORM, NAMED AS SUCH. Cases 1 and 3 read UICaptureLaunch.cs and
// ManageScreenVM.cs AS SOURCE TEXT rather than executing them. That is deliberate
// and it is a known limitation: BuildTimerService is a runtime MonoBehaviour whose
// seeders are private and live in a DIFFERENT assembly (DeNelle.Editor vs this
// file's DeNelle.EditorRegression), so calling them would mean widening visibility
// across an assembly boundary to satisfy a test. The precedent for reading source
// is ManagePortraitCoverageRegression's [vm-uses-building-portrait-key], which
// reads VmPath for the same reason. A source lint cannot see an id composed at
// runtime - but every Builder seed in that file is a LITERAL, and the four defects
// this file exists for were all literals.
// Case 2 needs no source: it reads the catalogs themselves.
//
// ⚠ REVERT RECIPE (if this suite blocks a lane and must come out in a hurry):
//   1. delete Assets/Editor/Regression/QueueJobCatalogCoverageRegression.cs (+ .meta)
//   2. delete the single registration line in Assets/Editor/Regression/DataRegression.cs
//      (search "queue-job-catalog-coverage")
// Nothing else references it. Pure verification: no runtime path, no data, no asset.
// =============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace DeNelle.Editor.Regression
{
    public static class QueueJobCatalogCoverageRegression
    {
        private const string CapturePath = "Assets/Editor/UICaptureLaunch.cs";
        private const string VmPath      = "Assets/_Modules/Village/UI/Manage/ManageScreenVM.cs";
        private const string TiersPath   = "Assets/Resources/Data/Canonical/building-tiers.json";
        private const string CatalogPath = "Assets/Resources/Data/Canonical/structures-catalog.json";

        /// <summary>
        /// Job target literals seeded on ChannelId.Builder in the capture fixtures. Both seeders'
        /// calls are matched: <c>queue.Enqueue(JobKind.X, ChannelId.Builder, "id", ...)</c> and
        /// <c>EnqueueFlowJob(queue, JobKind.X, ChannelId.Builder, "id", ...)</c>.
        /// <para>⚠ The tower row is seeded as <c>PlacedUpgradeKey.Compose("tower_ground_archer", 3, 7)</c>,
        /// not a bare literal, so the Compose form is matched too - otherwise the one Builder seed
        /// that was ALREADY fixed once (WO-1422) would be the one this suite could not see.</para>
        /// </summary>
        private static readonly Regex BuilderSeedRx = new Regex(
            @"ChannelId\.Builder\s*,\s*(?:""(?<id>[^""]+)""|PlacedUpgradeKey\.Compose\(\s*""(?<id>[^""]+)"")",
            RegexOptions.Compiled);

        public static bool Run(out string reason)
        {
            var failures = new List<string>();
            var log = new StringBuilder("=== QueueJobCatalogCoverageRegression ===\n");
            int checkedIds = 0;

            try
            {
                var ladders = LoadLadderIds(failures, log);
                var catalog = LoadCatalogNames(failures, log);
                checkedIds += CheckFixtureBuilderJobIds(ladders, catalog, failures, log);
                checkedIds += CheckCatalogsNameEveryRow(ladders, catalog, failures, log);
                CheckMissBranchStillLoud(failures, log);
            }
            catch (Exception ex)
            {
                failures.Add("[queue-job-catalog-coverage] suite threw " + ex.GetType().Name + ": " + ex.Message);
            }

            if (failures.Count == 0)
            {
                reason = "QUEUE_JOB_CATALOG_COVERAGE_OK " + checkedIds +
                         " queueable structure id(s) resolve a display name in at least one catalog. " +
                         "(Counts REPORTED, never asserted.)";
                Debug.Log(reason + "\n" + log);
                return true;
            }

            reason = "QUEUE_JOB_CATALOG_COVERAGE_FAIL\n" + string.Join("\n", failures);
            Debug.LogError(reason + "\n" + log);
            return false;
        }

        // ── authorities ───────────────────────────────────────────────────────

        /// <summary>
        /// building-tiers.json ladder ids -> displayName. The CIVIC ladder catalog: it holds the
        /// six upgradable buildings and, by design, no tower, wall or gate.
        /// </summary>
        private static Dictionary<string, string> LoadLadderIds(List<string> failures, StringBuilder log)
        {
            // ⛔ ORDINAL, NOT OrdinalIgnoreCase, AND THE DIFFERENCE IS A FALSE GREEN.
            // BuildingTierCatalog.Find compares `b.Id == id` - ordinal exact - against an input
            // NormalizeBuildingJobId has already lower-cased. So a ladder authored "Barracks" would
            // MISS at runtime while a case-insensitive map here reported it found. A lookup that is
            // more permissive than the one it mirrors fails in the ONE direction this file must
            // never fail in.
            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            if (!File.Exists(TiersPath))
            {
                failures.Add("[queue-job-catalog-coverage] authority missing: " + TiersPath);
                return map;
            }
            var root = JObject.Parse(File.ReadAllText(TiersPath));
            var rows = root["buildings"] as JArray;
            if (rows == null)
            {
                failures.Add("[queue-job-catalog-coverage] " + TiersPath + " has no 'buildings' array - " +
                             "the ladder authority changed shape and this suite is measuring nothing.");
                return map;
            }
            foreach (var row in rows)
            {
                string id = (string)row["id"];
                if (string.IsNullOrEmpty(id)) continue;
                map[id] = (string)row["displayName"] ?? "";
            }
            log.AppendLine("building-tiers ladders: " + string.Join(", ", new List<string>(map.Keys).ToArray()));
            return map;
        }

        /// <summary>
        /// structures-catalog.json ids -> displayName. Every tower, wall, gate and civic row the
        /// build palette offers. This is the file CatalogBootstrap registers into CatalogRegistry,
        /// so it is the same content the VM's CatalogRegistry.Get branch reads at runtime.
        /// </summary>
        private static Dictionary<string, string> LoadCatalogNames(List<string> failures, StringBuilder log)
        {
            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            if (!File.Exists(CatalogPath))
            {
                failures.Add("[queue-job-catalog-coverage] authority missing: " + CatalogPath);
                return map;
            }
            var root = JObject.Parse(File.ReadAllText(CatalogPath));
            var rows = root["entries"] as JArray;
            if (rows == null)
            {
                failures.Add("[queue-job-catalog-coverage] " + CatalogPath + " has no 'entries' array - " +
                             "the structures authority changed shape and this suite is measuring nothing.");
                return map;
            }
            foreach (var row in rows)
            {
                string id = (string)row["id"];
                if (string.IsNullOrEmpty(id)) continue;
                map[id] = (string)row["displayName"] ?? "";
            }
            log.AppendLine("structures-catalog rows: " + map.Count);
            return map;
        }

        // ── cases ─────────────────────────────────────────────────────────────

        /// <summary>
        /// [fixture-builder-job-id] - every Builder-channel job target the capture fixtures seed
        /// resolves a NON-EMPTY display name through the same two-catalog walk the VM performs
        /// (ManageScreenVM.cs:940-1007). Fails BY NAME, quoting the id, the file:line that seeds it
        /// and both catalogs it missed - so the next fake id is caught by CI and not by an audit.
        /// RED: restore "gate:4:1" at the SeedManageCaptureQueue Repair line.
        /// </summary>
        private static int CheckFixtureBuilderJobIds(
            Dictionary<string, string> ladders, Dictionary<string, string> catalog,
            List<string> failures, StringBuilder log)
        {
            if (!File.Exists(CapturePath))
            {
                failures.Add("[fixture-builder-job-id] " + CapturePath + " not found - the fixture " +
                             "moved and this case is measuring nothing.");
                return 0;
            }
            string[] lines = File.ReadAllLines(CapturePath);
            int seen = 0;
            for (int i = 0; i < lines.Length; i++)
            {
                var m = BuilderSeedRx.Match(lines[i]);
                if (!m.Success) continue;
                string jobId = m.Groups["id"].Value;
                seen++;

                string ladderId = NormalizeLikeVm(jobId);
                string catalogId = StripSuffixLikeVm(jobId);
                bool viaLadder = ladders.TryGetValue(ladderId, out var lname) && !string.IsNullOrWhiteSpace(lname);
                bool viaCatalog = catalog.TryGetValue(catalogId, out var cname) && !string.IsNullOrWhiteSpace(cname);

                if (viaLadder || viaCatalog)
                {
                    log.AppendLine("  ok  " + jobId + " -> \"" + (viaLadder ? lname : cname) + "\" (" +
                                   (viaLadder ? "building-tiers:" + ladderId : "structures-catalog:" + catalogId) + ")");
                    continue;
                }

                failures.Add("[fixture-builder-job-id] " + CapturePath + ":" + (i + 1) + " seeds Builder job '" +
                             jobId + "', and NEITHER catalog names it: building-tiers.json has no ladder '" +
                             ladderId + "' and structures-catalog.json has no id '" + catalogId + "'. The " +
                             "Manage queue row for this job paints \"Unknown structure\" with no thumbnail and " +
                             "fires [Flow:Manage] queue row catalog MISS. ⛔ THE REMEDY IS ALMOST NEVER TO ADD " +
                             "A CATALOG ROW - check first whether the structure is already authored under a " +
                             "DIFFERENT id (the gate is 'gate_stone', not 'gate') and fix the FIXTURE, exactly " +
                             "as WO-1655 did. A second row for one structure is an alias, and ManageArt.cs:306 " +
                             "forbids alias layers because they fail silently.");
            }

            if (seen == 0)
                failures.Add("[fixture-builder-job-id] matched ZERO Builder seeds in " + CapturePath +
                             " - the seeding call shape changed and this case is measuring nothing. " +
                             "Fix BuilderSeedRx; do not delete the case.");
            log.AppendLine("[fixture-builder-job-id] " + seen + " Builder-channel seed(s) checked.");
            return seen;
        }

        /// <summary>
        /// [catalog-display-name] - neither authority may ship a row with a BLANK display name.
        /// The VM treats empty exactly like absent (it tests <c>!string.IsNullOrEmpty(displayName)</c>),
        /// so a row authored with "" is the same "Unknown structure" defect arriving from the other
        /// direction - and it would sail past the fixture case above.
        /// RED: blank any row's displayName in either JSON.
        /// </summary>
        private static int CheckCatalogsNameEveryRow(
            Dictionary<string, string> ladders, Dictionary<string, string> catalog,
            List<string> failures, StringBuilder log)
        {
            int n = 0;
            foreach (var kv in ladders)
            {
                n++;
                if (string.IsNullOrWhiteSpace(kv.Value))
                    failures.Add("[catalog-display-name] " + TiersPath + " ladder '" + kv.Key +
                                 "' has a blank displayName. Any Builder job on it paints " +
                                 "\"Unknown structure\" - blank reads as absent to ManageScreenVM.");
            }
            foreach (var kv in catalog)
            {
                n++;
                if (string.IsNullOrWhiteSpace(kv.Value))
                    failures.Add("[catalog-display-name] " + CatalogPath + " row '" + kv.Key +
                                 "' has a blank displayName. Any Builder job on it paints " +
                                 "\"Unknown structure\" - blank reads as absent to ManageScreenVM.");
            }
            log.AppendLine("[catalog-display-name] " + n + " authored row(s) carry a display name.");
            return n;
        }

        /// <summary>
        /// [miss-branch-still-loud] - the placeholder and its FlowTrace.Fail STAY. WO-1655 section 6
        /// is explicit: title-casing the id, hiding the row or softening the string destroys the only
        /// signal that found this bug, and CLAUDE.md 12 makes instrumentation PERMANENT. The two
        /// cases above would go green on a tree where the VM had simply stopped complaining, so the
        /// detector needs its own pin.
        /// RED: delete either the FlowTrace.Fail or the "Unknown structure" literal from the VM.
        /// </summary>
        private static void CheckMissBranchStillLoud(List<string> failures, StringBuilder log)
        {
            if (!File.Exists(VmPath))
            {
                failures.Add("[miss-branch-still-loud] " + VmPath + " not found - the VM moved and " +
                             "this case is measuring nothing.");
                return;
            }
            string src = File.ReadAllText(VmPath);
            if (src.IndexOf("queue row catalog MISS", StringComparison.Ordinal) < 0)
                failures.Add("[miss-branch-still-loud] " + VmPath + " no longer emits the " +
                             "\"queue row catalog MISS\" FlowTrace.Fail. That trace is what found " +
                             "WO-1655 from a headless log; CLAUDE.md 12 says instrumentation is " +
                             "PERMANENT. Flag it off if it must go quiet - never delete it.");
            if (src.IndexOf("\"Unknown structure", StringComparison.Ordinal) < 0)
                failures.Add("[miss-branch-still-loud] " + VmPath + " no longer paints the honest " +
                             "\"Unknown structure\" placeholder on a catalog miss. Title-casing the " +
                             "raw id back into a name (the retired PrettyJobLabel behaviour) ships an " +
                             "internal identifier to the player and hides the data defect.");
            log.AppendLine("[miss-branch-still-loud] the loud placeholder and its FlowTrace.Fail are intact.");
        }

        // ── the VM's two id shapes, mirrored ──────────────────────────────────

        /// <summary>
        /// ManageScreenVM.NormalizeBuildingJobId (:2163-2174) for the BuildingTierCatalog lookup:
        /// trim, cut at the first ':' or '@', lower-case, '_' -> '-'.
        /// <para>⚠ The BarracksUpgrade special case is NOT mirrored: it keys off JobKind, which a
        /// source lint cannot see. It can only ever make MORE ids resolve, so omitting it can
        /// produce a false FAIL, never a false PASS - and the barracks row resolves through the
        /// plain path anyway ("barracks:2:0" -> "barracks").</para>
        /// </summary>
        private static string NormalizeLikeVm(string jobId)
        {
            return StripSuffixLikeVm(jobId).ToLowerInvariant().Replace('_', '-');
        }

        /// <summary>
        /// ManageScreenVM's structures-catalog lookup (:994-997): the RAW id minus its placement
        /// suffix. NOT lower-cased and NOT slugged - CatalogRegistry.Get is an exact match and every
        /// catalog id keeps its underscores ("tower_ground_archer", "gate_stone").
        /// </summary>
        private static string StripSuffixLikeVm(string jobId)
        {
            string id = (jobId ?? "").Trim();
            int suffix = id.IndexOfAny(new[] { ':', '@' });
            if (suffix > 0) id = id.Substring(0, suffix);
            return id.Trim();
        }
    }
}

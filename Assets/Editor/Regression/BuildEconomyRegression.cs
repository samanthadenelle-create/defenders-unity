// =============================================================================
// BuildEconomyRegression — headless oracle for the BUILD MODE + BUILD ECONOMY
// data spine (structures-catalog.json → CatalogRegistry → StructureFactory /
// BuildModeController cost boundary → PlacementGrid math → BaseLayout replay →
// BuildTimerConfig (WO-172/612) → damage-states.json (WO-672 D) → the
// CatalogBootstrap.RegisterFallback freshness gate on the JSON-failure path — WO-1137
// turned that path into CODEGEN, so gate 12 stopped proving field parity, which is now
// true by construction, and started proving the generated copy is not STALE).
// -----------------------------------------------------------------------------
// Assembly: DeNelle.EditorRegression (editor-only). Style/contract mirrors the
// other Run(out reason) oracles wired into DataRegression.RunAll:
//   public static bool Run(out string reason)
//   markers: BUILDECON_OK (Debug.Log) / BUILDECON_FAIL (Debug.LogError → lands
//   in break-log.jsonl per docs/INSTRUMENTATION_STANDARD.md §4/§5).
//
// Data + logic ONLY — no scene loads, no play mode. Real objects in, real
// response out: costs resolve through the REAL BuildModeController.CostFor /
// UpgradeCostFor / RefundCostFor (the actual charge/refund boundary), placement
// math through a REAL PlacementGrid instance, the replay probe through the REAL
// StructureFactory.Create (the ONE creation path).
//
// DELIBERATELY NOT DUPLICATED HERE (covered elsewhere — do not re-add):
//   • base visualPrefabPath Resources-load per entry → DataRegression.CheckStructures
//   • Wood/Iron dual-wallet + crystal SSOT              → VillageEconomyRegression (B2
//     is a fail-by-design oracle; duplicating it would double the known failure)
//   • 45° footprint claim inflation, migration round-trip, save v30, one-per-id,
//     yaw round-trip, repair chain                       → StrategicPlacementRegression
//   • live upgrade charge + ApplyTierStats on components → BuildingUpgradeRegression /
//     play-mode (needs live DefenseTower/WallSegment behaviours ticking)
//   • BaseLayoutLoader.Spawn full replay (footprint blocker + NavMeshObstacle +
//     UnderConstruction re-arm) — Spawn strips colliders via Object.Destroy (play-
//     mode-only) and gates on the live scene name/GameStateService, so the loader
//     half needs play mode; the DATA half + the real factory Create are proven here.
// =============================================================================
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using UnityEngine;
using DeNelle.Core.Catalog;
using DeNelle.Core.State;
using DeNelle.Village;
using CoreCost = DeNelle.Core.Catalog.ResourceCost;

namespace DeNelle.Editor
{
    public static class BuildEconomyRegression
    {
        private const string CatalogRelPath = "Data/Canonical/structures-catalog.json";
        private const string DamageRelPath  = "Data/Canonical/damage-states.json";

        [System.Serializable]
        private sealed class StructuresFile
        {
            [JsonProperty("version")] public int Version;
            [JsonProperty("entries")] public List<CatalogEntry> Entries = new List<CatalogEntry>();
        }

        public static bool Run(out string reason)
        {
            var failures = new List<string>();
            var log = new StringBuilder();
            log.AppendLine("=== BuildEconomyRegression: build-mode/economy data spine ===");

            var created = new List<GameObject>();
            try
            {
                // ── 1. CATALOG PARSE — the production parse (CatalogBootstrap.LoadFromJson
                //       settings verbatim: StringEnumConverter + ignore null/miss) ─────────
                var entries = ParseCatalog(failures, log);
                if (entries == null)
                {
                    // Parse-level break — nothing downstream is decidable.
                    return Verdict(failures, log, out reason);
                }

                // ── 2. DUAL-COPY BYTE-EQUAL (the catalog's own stated rule) ─────────────
                CheckDualCopy(CatalogRelPath, failures, log);
                CheckDualCopy(DamageRelPath, failures, log);
                CheckStructureDescriptions(entries, failures, log);
                CheckCardVmCostWordSource(failures, log);

                // Hydrate the registry so the REAL cost/refund/factory paths resolve ids
                // exactly as the game does (CatalogRegistry.Get). Mirrors CatalogBootstrap's
                // register loop over the SAME parsed entries.
                foreach (var e in entries)
                    if (e != null && !string.IsNullOrEmpty(e.id) && CatalogRegistry.Get(e.id) == null)
                        CatalogRegistry.Register(e);

                // ── 2b. REQUIRED IDS (WO-812): the Barracks must exist as a placeable row —
                // the army/raid ladder (train -> deploy) depends on it; the hub redesign
                // silently dropped it once, never again. Type Resource => the Town palette.
                bool hasBarracks = false;
                foreach (var e in entries)
                    if (e != null && string.Equals(e.id, "barracks", System.StringComparison.OrdinalIgnoreCase))
                    { hasBarracks = true; break; }
                if (!hasBarracks)
                    failures.Add("required id 'barracks' MISSING from structures-catalog (WO-812 — the train/raid ladder has no world entry)");
                else
                    log.AppendLine("  required id 'barracks' present (WO-812) OK");

                // ── 3. COST SANITY through the REAL resolver ────────────────────────────
                CheckCosts(entries, failures, log);

                // ── 4. UPGRADE LADDER — tier-monotonic costs + tier visuals resolve ─────
                CheckUpgradeLadder(entries, failures, log);

                // ── 5. TOWER BEHAVIOUR CONTRACT — stats + projectileStyle vocabulary ────
                CheckTowerContract(entries, failures, log);

                // ── 6. PLACEMENT MATH — real PlacementGrid invariants ───────────────────
                CheckPlacementMath(entries, created, failures, log);

                // ── 7. SELL REFUND = 50% (invested-cost-aware) via the REAL RefundCostFor ─
                CheckSellRefund(created, failures, log);

                // ── 8. BASELAYOUT REPLAY — data-half + the REAL StructureFactory.Create ──
                CheckBaseLayoutReplay(created, failures, log);

                // ── 9. BUILD-TIMER CONFIG (WO-172 / WO-612 rewarded-ad path) ─────────────
                CheckBuildTimerConfig(failures, log);

                // ── 9b. WO-945 — first-build + onboarding grace (decision + duration) ────
                CheckBuildGrace(failures, log);

                // ── 10. DAMAGE STATES (WO-672 D) — real DamageStatesCatalog loader ───────
                CheckDamageStates(failures, log);

                // -- 11. WO-855 -- tower spam softcap + the build-tier fix ----------------
                CheckTowerSoftcap(entries, created, failures, log);
                CheckBuildTierDerivation(entries, failures, log);

                // -- 11b. WO-1480 -- the NINTH hardcoded structure ceiling (WallSegment) ---
                CheckWallTierCeiling(failures, log);

                // -- 12. FALLBACK FRESHNESS -- the JSON-load-FAILURE path now EMBEDS the catalog
                //        (WO-1137 codegen), so the only thing left to prove is that the embedded
                //        copy is not STALE, and that it still registers every row.
                CheckFallbackParity(entries, failures, log);
            }
            catch (System.Exception ex)
            {
                failures.Add($"BuildEconomyRegression threw: {ex.GetType().Name}: {ex.Message}");
            }
            finally
            {
                foreach (var go in created)
                    if (go != null) Object.DestroyImmediate(go);
            }

            return Verdict(failures, log, out reason);
        }

        // =====================================================================
        //  1. CATALOG PARSE — ids unique + display fields present
        // =====================================================================
        private static List<CatalogEntry> ParseCatalog(List<string> failures, StringBuilder log)
        {
            string json = DeNelle.Core.CanonicalJson.Read(CatalogRelPath);
            if (string.IsNullOrEmpty(json))
            {
                failures.Add($"{CatalogRelPath} unreadable (CanonicalJson.Read returned empty)");
                return null;
            }

            StructuresFile file;
            try
            {
                var settings = new JsonSerializerSettings
                {
                    Converters = { new StringEnumConverter() },
                    NullValueHandling = NullValueHandling.Ignore,
                    MissingMemberHandling = MissingMemberHandling.Ignore,
                };
                file = JsonConvert.DeserializeObject<StructuresFile>(json, settings);
            }
            catch (System.Exception ex)
            {
                failures.Add($"structures-catalog.json failed to parse: {ex.Message}");
                return null;
            }

            if (file == null || file.Entries == null || file.Entries.Count == 0)
            {
                failures.Add("structures-catalog.json deserialized to 0 CatalogEntry objects (mapping break or empty 'entries')");
                return null;
            }

            log.AppendLine($"structures-catalog.json v{file.Version} -> {file.Entries.Count} CatalogEntry object(s)");

            var seen = new HashSet<string>();
            foreach (var e in file.Entries)
            {
                if (e == null || string.IsNullOrEmpty(e.id))
                { failures.Add("catalog entry with null/empty id"); continue; }
                if (!seen.Add(e.id))
                    failures.Add($"duplicate catalog id '{e.id}' — CatalogRegistry.Register would silently REPLACE the first row");
                if (string.IsNullOrEmpty(e.displayName))
                    failures.Add($"catalog entry '{e.id}' has no displayName (blank palette row)");
                if (e.repo == null)
                    failures.Add($"catalog entry '{e.id}' has null repo (behaviour half missing — deserializer default was lost)");
            }
            return file.Entries;
        }

        // =====================================================================
        //  2. DUAL-COPY — Resources copy must stay byte-identical to StreamingAssets
        //     (the WebGL-safe CanonicalJson contract stated in both files' notes).
        // =====================================================================
        private static void CheckDualCopy(string relPath, List<string> failures, StringBuilder log)
        {
            string res = Application.dataPath + "/Resources/" + relPath;
            string sa  = Application.dataPath + "/StreamingAssets/" + relPath;
            bool hasRes = System.IO.File.Exists(res);
            bool hasSa  = System.IO.File.Exists(sa);
            if (!hasRes || !hasSa)
            {
                failures.Add($"dual-copy '{relPath}': missing {(hasRes ? "" : "Resources copy ")}{(hasSa ? "" : "StreamingAssets copy")}".Trim());
                return;
            }
            byte[] a = System.IO.File.ReadAllBytes(res);
            byte[] b = System.IO.File.ReadAllBytes(sa);
            bool equal = a.Length == b.Length;
            if (equal)
                for (int i = 0; i < a.Length; i++)
                    if (a[i] != b[i]) { equal = false; break; }
            if (!equal)
                failures.Add($"dual-copy '{relPath}': Resources and StreamingAssets copies DIVERGED " +
                             $"({a.Length} vs {b.Length} bytes) — editor and WebGL would load different data");
            else
                log.AppendLine($"  dual-copy '{relPath}' byte-identical ({a.Length} bytes) OK");
        }

        // WO-1081: descriptions are authored catalog data, bounded to the one-line tile.
        private static void CheckStructureDescriptions(List<CatalogEntry> entries,
            List<string> failures, StringBuilder log)
        {
            int authored = 0;
            int exempt = 0;
            var exemptIds = new List<string>();
            CatalogEntry crystalMine = null;
            foreach (var e in entries)
            {
                if (e == null) continue;
                // WO-1565: EVERY buildable row must carry an authored description. It used to be
                // Resource/Collector only, and StructureCardVM.DescriptionFor painted type-level
                // prose for the rest - so all four Tower rows read "A defensive tower ...", the
                // Catapult called itself a tower and the anti-air Sky Ballista read the same as
                // the ground ones. The prose is now deleted; the unauthored case FAILS here.
                // EXEMPT: rows with no visualPrefabPath are DATA rows, never placeable and never
                // shown (repair_default's own _note states that convention, and
                // DataRegression.CheckStructures skips them for the same reason).
                bool buildable = !string.IsNullOrWhiteSpace(e.visualPrefabPath);
                if (!buildable) { exempt++; exemptIds.Add(e.id); continue; }
                if (string.IsNullOrWhiteSpace(e.description))
                {
                    failures.Add($"[structure-descriptions] '{e.id}' type={e.type} has no authored description " +
                                 "- author it in BOTH canonical copies of structures-catalog.json " +
                                 "(there is no fallback prose any more, WO-1565)");
                    continue;
                }
                authored++;
                // The 48-char tile cap stays scoped to Resource/Collector, as WO-1081 set it:
                // tower_siege_tower ships an owner-voice line well over the cap, and WO-1565 sec.6
                // forbids touching it, so widening the cap here would red an already-shipped row.
                // (No length literal here on purpose - a live count in a comment is duplicated state.)
                bool capped = e.type == CatalogType.Resource || e.type == CatalogType.Collector;
                if (capped && e.description.Length > 48)
                    failures.Add($"[structure-descriptions] '{e.id}' description is {e.description.Length} characters; max is 48");
                string projected = StructureCardVM.DescriptionFor(e);
                if (!string.Equals(projected, e.description, System.StringComparison.Ordinal))
                    failures.Add($"[structure-descriptions] '{e.id}' projection changed authored copy");
                if (projected.IndexOf("gathers materials over time", System.StringComparison.OrdinalIgnoreCase) >= 0)
                    failures.Add($"[structure-descriptions] '{e.id}' rendered the obsolete generic resource sentence");
                // WO-1565: the shipping font carries no em-dash, so a non-ASCII character in
                // player-facing copy renders as a gap (WO-1491 diagnosed one as a "triple space").
                foreach (char c in e.description)
                    if (c > 126 || c < 32)
                    {
                        failures.Add($"[structure-descriptions] '{e.id}' description carries the non-ASCII " +
                                     $"character U+{(int)c:X4}; the shipping font does not render it");
                        break;
                    }
                if (e.id == "mine_crystal") crystalMine = e;
            }

            const string mineCopy = "Yields Crystals each time a wave is cleared.";
            if (crystalMine == null || StructureCardVM.DescriptionFor(crystalMine) != mineCopy)
                failures.Add("[structure-descriptions] mine_crystal does not project the required copy verbatim");

            string repoRoot = System.IO.Path.GetDirectoryName(Application.dataPath);
            string vmPath = System.IO.Path.Combine(repoRoot,
                "Assets/_Modules/Village/BuildMode/StructureCardVM.cs".Replace('/', System.IO.Path.DirectorySeparatorChar));
            string source = System.IO.File.ReadAllText(vmPath);
            int start = source.IndexOf("DescriptionFor(CatalogEntry e)", System.StringComparison.Ordinal);
            int end = start < 0 ? -1 : source.IndexOf("private static string FootprintFor", start, System.StringComparison.Ordinal);
            string method = start >= 0 && end > start ? source.Substring(start, end - start) : string.Empty;
            if (method.Length == 0)
                failures.Add("[structure-descriptions] DescriptionFor source seam not found");
            else if (method.Contains("e.role") || method.Contains("e.id ==") || method.Contains("e.id !="))
                failures.Add("[structure-descriptions] DescriptionFor branches on role/id; it must project " +
                             "authored copy only, never derive per-row prose");
            // WO-1565: pin the DELETION. If type-level prose is ever re-added, every unauthored row
            // silently reads plausibly again and this whole defect class comes back invisible.
            else if (method.Contains("A defensive tower") || method.Contains("A village structure") ||
                     method.Contains("case CatalogType."))
                failures.Add("[structure-descriptions] DescriptionFor paints type-level fallback prose again " +
                             "(WO-1565 deleted it); an unauthored row must fail this gate, not render");

            log.AppendLine($"  [structure-descriptions] authored={authored} buildable rows, {exempt} prefab-less " +
                           $"data row(s) exempt [{string.Join(", ", exemptIds)}]; ASCII-only, 48-char cap on " +
                           "resource/collector, mine copy, authored projection, no fallback prose OK");
        }

        // =====================================================================
        //  3. COSTS — every affordable-gated entry resolves a NON-ZERO effective
        //     cost through the REAL BuildModeController.CostFor (multi-cost wins,
        //     crystals-only buildCost fallback), and no slot is negative.
        // =====================================================================
        private static void CheckCosts(List<CatalogEntry> entries, List<string> failures, StringBuilder log)
        {
            int checkedCount = 0;
            foreach (var e in entries)
            {
                if (e == null || string.IsNullOrEmpty(e.id) || e.repo == null) continue;
                CoreCost c = BuildModeController.CostFor(e);
                checkedCount++;

                if (c.wood < 0 || c.food < 0 || c.iron < 0 || c.crystals < 0)
                    failures.Add($"'{e.id}' resolves a NEGATIVE cost slot ({c.wood}/{c.food}/{c.iron}/{c.crystals})");

                bool affordGated = e.repo.placement == null || e.repo.placement.checkAffordable;
                if (affordGated && c.IsZero)
                    failures.Add($"'{e.id}' is affordability-gated but CostFor resolves ZERO " +
                                 "(no repo.cost AND buildCost 0 — placement would be free)");

                log.AppendLine($"  COST {e.id} -> w{c.wood} f{c.food} i{c.iron} c{c.crystals}" +
                               (affordGated ? "" : " (checkAffordable=false)"));
            }
            if (checkedCount == 0)
                failures.Add("cost check covered 0 entries (catalog empty after filtering?)");
        }

        // =====================================================================
        //  4. UPGRADE LADDER — for maxLevel > 1 rows: every step resolves a
        //     non-zero cost via the REAL UpgradeCostFor, step totals never
        //     DECREASE (the CoC sink escalates), the authored tier-visual ladder
        //     fits maxLevel and every tier prefab Resources-loads, maxLevel stays
        //     within the RepoProps.MaxStructureLevel ceiling, and the tower tier
        //     stat multiplier table is strictly increasing (tier-monotonic).
        // =====================================================================
        private static void CheckUpgradeLadder(List<CatalogEntry> entries, List<string> failures, StringBuilder log)
        {
            foreach (var e in entries)
            {
                if (e == null || e.repo == null) continue;
                // Mirrors BuildModeController.MaxLevelFor -- and now reads the SAME named constant
                // it clamps to, so raising the ceiling can never leave this oracle asserting the
                // old number (a hardcoded 3 in four files is what made WO-966's levels 4-6 fail
                // here while the controller happily refused them anyway).
                int ceiling = DeNelle.Core.Catalog.RepoProps.MaxStructureLevel;
                int maxLevel = Mathf.Clamp(e.repo.maxLevel, 1, ceiling);

                if (e.repo.maxLevel > ceiling)
                    failures.Add($"'{e.id}' authors maxLevel {e.repo.maxLevel} — above the RepoProps.MaxStructureLevel ceiling ({ceiling}); levels {ceiling + 1}+ are unreachable dead data (BuildModeController.MaxLevelFor clamps there)");

                var ladder = e.repo.upgradeVisualPath;
                if (ladder != null && ladder.Length > 0)
                {
                    if (maxLevel < 2)
                        failures.Add($"'{e.id}' authors upgradeVisualPath but maxLevel {maxLevel} — the tier models are unreachable");
                    if (ladder.Length > maxLevel - 1)
                        failures.Add($"'{e.id}' authors {ladder.Length} tier visual(s) but only {maxLevel - 1} upgrade step(s) exist");
                    for (int i = 0; i < ladder.Length; i++)
                    {
                        if (string.IsNullOrEmpty(ladder[i])) continue;   // "keep previous model" is legal
                        // The SAME resolve StructureFactory feeds to VisualFactory.Skin. A null
                        // here = the upgraded tower silently keeps its old look (F8 2026-07-06
                        // class of bug).
                        // ⛔ RESOLVES THROUGH StructureAssetLoader, NOT Resources.Load. Structure
                        // art migrated OUT of Resources into an Addressable group (2026-08-18), so
                        // a bare Resources.Load reports every tier model NULL on a perfectly
                        // healthy tree — it would be testing where the files USED to be, and it did
                        // exactly that: 8 false failures the moment the folder moved. The seam is
                        // what the game actually calls, so asking it is the check that survives the
                        // migration instead of being invalidated by it.
                        if (DeNelle.Core.StructureAssetLoader.LoadStructurePrefab(ladder[i]) == null)
                            failures.Add($"'{e.id}' upgradeVisualPath[{i}] '{ladder[i]}' (the L{i + 2} model) " +
                                         "does not resolve via StructureAssetLoader (Addressables, then Resources)");
                        else
                            log.AppendLine($"  TIER {e.id} L{i + 2} -> '{ladder[i]}' prefab OK");
                    }
                    // VisualPathForLevel through the REAL resolver: L1 = base, L2+ = ladder.
                    string l1 = StructureFactory.VisualPathForLevel(e, 1);
                    if (l1 != e.visualPrefabPath)
                        failures.Add($"'{e.id}' VisualPathForLevel(1) returned '{l1}' — must be the base visualPrefabPath");
                }

                if (maxLevel <= 1) continue;

                int prevTotal = -1;
                for (int from = 1; from < maxLevel; from++)
                {
                    CoreCost step = BuildModeController.UpgradeCostFor(e, from);
                    int total = step.wood + step.food + step.iron + step.crystals;
                    if (total <= 0)
                        failures.Add($"'{e.id}' upgrade step L{from}->L{from + 1} resolves ZERO cost (free upgrade — sink broken)");
                    if (step.wood < 0 || step.food < 0 || step.iron < 0 || step.crystals < 0)
                        failures.Add($"'{e.id}' upgrade step L{from}->L{from + 1} has a negative slot");
                    if (prevTotal >= 0 && total < prevTotal)
                        failures.Add($"'{e.id}' upgrade cost NOT tier-monotonic: L{from}->L{from + 1} total {total} < previous step {prevTotal}");
                    log.AppendLine($"  UP {e.id} L{from}->L{from + 1}: w{step.wood} f{step.food} i{step.iron} c{step.crystals} (total {total})");
                    prevTotal = total;
                }
            }

            // Tower tier stat multiplier (private table, read-only reflection): L1..L3
            // must be strictly increasing or an upgrade would not step the tower's power.
            var mulField = typeof(BuildModeController).GetField("s_towerTierMul",
                BindingFlags.NonPublic | BindingFlags.Static);
            if (mulField == null)
                failures.Add("BuildModeController.s_towerTierMul not found (renamed?) — tier stat monotonicity unverifiable");
            else if (mulField.GetValue(null) is float[] mul && mul.Length >= 4)
            {
                if (!(mul[1] < mul[2] && mul[2] < mul[3]))
                    failures.Add($"tower tier stat multipliers not strictly increasing: L1 x{mul[1]}, L2 x{mul[2]}, L3 x{mul[3]}");
                else
                    log.AppendLine($"  tower tier stat multipliers L1 x{mul[1]} < L2 x{mul[2]} < L3 x{mul[3]} OK");
            }
        }

        // =====================================================================
        //  5. TOWER CONTRACT — StructureFactory copies range/damage/fireRate
        //     VERBATIM onto DefenseTower/ArcaneTower; a zero stat is a tower
        //     that never fires. projectileStyle is a loose string — assert the
        //     known vocabulary (pellet|bolt|spell|empty) so a typo ('blot')
        //     can't silently fall back to the pellet.
        // =====================================================================
        private static void CheckTowerContract(List<CatalogEntry> entries, List<string> failures, StringBuilder log)
        {
            var knownStyles = new HashSet<string> { "pellet", "bolt", "spell" };
            foreach (var e in entries)
            {
                if (e == null || e.repo == null) continue;
                string b = e.repo.behaviorId;
                if (b != "DefenseTower" && b != "ArcaneTower") continue;

                if (e.repo.range <= 0f)    failures.Add($"tower '{e.id}' authors range {e.repo.range} — would never acquire a target");
                if (e.repo.damage <= 0f)   failures.Add($"tower '{e.id}' authors damage {e.repo.damage} — would deal nothing");
                if (e.repo.fireRate <= 0f) failures.Add($"tower '{e.id}' authors fireRate {e.repo.fireRate} — would never fire");

                string style = e.repo.projectileStyle;
                if (!string.IsNullOrEmpty(style) && !knownStyles.Contains(style))
                    failures.Add($"tower '{e.id}' projectileStyle '{style}' is not in the known vocabulary (pellet|bolt|spell) — silent pellet fallback");

                log.AppendLine($"  TW {e.id} range={e.repo.range} dmg={e.repo.damage} rof={e.repo.fireRate} " +
                               $"style='{style ?? "<null>"}' airOnly={e.repo.airOnly}");
            }
        }

        // =====================================================================
        //  6. PLACEMENT MATH — real PlacementGrid: cell↔world round-trip, snap,
        //     footprint occupancy, edge-allow bounds, metric→cell conversion,
        //     and gate-clearance / footprint rule constants from the catalog.
        //     (45° claim inflation is StrategicPlacementRegression gate 7.)
        // =====================================================================
        private static void CheckPlacementMath(List<CatalogEntry> entries, List<GameObject> created,
                                               List<string> failures, StringBuilder log)
        {
            var gridGo = new GameObject("BuildEconRegressionGrid");
            created.Add(gridGo);
            var grid = gridGo.AddComponent<PlacementGrid>();
            // Editmode AddComponent does not run Awake — apply the SAME origin-centering
            // Awake performs so cell↔world matches the runtime grid exactly.
            grid.origin = new Vector3(-grid.gridWidth * grid.cellSize * 0.5f, 0f,
                                      -grid.gridHeight * grid.cellSize * 0.5f);

            // Round-trip: WorldToCell(CellToWorld(cell)) == cell across the grid,
            // including the corner cells (edge-allow: margin 0 keeps them buildable).
            var samples = new[]
            {
                new Vector2Int(0, 0), new Vector2Int(grid.gridWidth - 1, grid.gridHeight - 1),
                new Vector2Int(grid.gridWidth / 2, grid.gridHeight / 2), new Vector2Int(3, 25),
            };
            foreach (var cell in samples)
            {
                var back = grid.WorldToCell(grid.CellToWorld(cell));
                if (back != cell)
                    failures.Add($"grid round-trip broke: cell ({cell.x},{cell.y}) -> world -> ({back.x},{back.y})");
            }

            // SnapToGrid preserves the surface Y and lands on the cell centre.
            var probe = new Vector3(4.2f, 7.31f, -8.9f);
            var snapped = grid.SnapToGrid(probe);
            if (!Mathf.Approximately(snapped.y, probe.y))
                failures.Add($"SnapToGrid did not preserve Y ({probe.y} -> {snapped.y}) — placed structures would lose their surface height");
            var expectCentre = grid.CellToWorld(grid.WorldToCell(probe));
            if (Mathf.Abs(snapped.x - expectCentre.x) > 0.001f || Mathf.Abs(snapped.z - expectCentre.z) > 0.001f)
                failures.Add("SnapToGrid XZ is not the cell centre");

            // Occupancy: a 2x2 claim blocks every overlapping placement; Free restores.
            var at = new Vector2Int(10, 10);
            var fp = new Vector2Int(2, 2);
            if (!grid.CanPlace(at, fp)) failures.Add("empty grid refused a legal 2x2 placement");
            grid.Occupy(at, fp, "oracle");
            if (grid.CanPlace(at, Vector2Int.one))
                failures.Add("Occupy did not block the anchor cell");
            if (grid.CanPlace(new Vector2Int(11, 11), Vector2Int.one))
                failures.Add("Occupy did not block the far corner of a 2x2 footprint");
            if (grid.CanPlace(new Vector2Int(9, 9), fp))
                failures.Add("a 2x2 at (9,9) overlapping one occupied cell was allowed — footprint overlap check broken");
            if (!grid.CanPlace(new Vector2Int(12, 10), Vector2Int.one))
                failures.Add("a free cell adjacent to the footprint was wrongly blocked");
            grid.Free(at, fp);
            if (!grid.CanPlace(at, fp)) failures.Add("Free did not release the occupied cells (sell/move would leak occupancy)");

            // Bounds: out-of-grid always refused; boundary cell allowed (edgeMargin 0 —
            // the owner's edge-allow rule so perimeter walls reach the map edge).
            if (grid.CanPlace(new Vector2Int(-1, 5), Vector2Int.one)) failures.Add("out-of-bounds cell (-1,5) was placeable");
            if (grid.CanPlace(new Vector2Int(grid.gridWidth - 1, 5), fp)) failures.Add("a 2x2 hanging off the +X edge was placeable");
            if (grid.edgeMargin == 0 && !grid.CanPlace(new Vector2Int(0, 5), Vector2Int.one))
                failures.Add("edge-allow broken: boundary cell (0,5) refused with edgeMargin 0");

            // Metric→cell: FootprintCells = Ceil(m / cellSize), floor 1.
            foreach (var m in new[] { 0.5f, 2.5f, 3f, 3.4f, 6f, 7.1f })
            {
                int expect = Mathf.Max(1, Mathf.CeilToInt(m / grid.cellSize));
                var got = grid.FootprintCells(m);
                if (got.x != expect || got.y != expect)
                    failures.Add($"FootprintCells({m}m) = {got.x}x{got.y}, expected {expect}x{expect}");
            }

            // Catalog placement-rule constants: every rule footprint positive, gate
            // clearance non-negative, and every Gate-type row carries a clearance rule
            // (the spawn→Heart lane must stay open).
            foreach (var e in entries)
            {
                var p = e != null && e.repo != null ? e.repo.placement : null;
                if (p == null) continue;
                if (p.footprint <= 0f)
                    failures.Add($"'{e.id}' placement.footprint {p.footprint} — non-positive footprint collapses the grid claim");
                if (p.minDistanceFromGate < 0f)
                    failures.Add($"'{e.id}' placement.minDistanceFromGate {p.minDistanceFromGate} is negative");
                if (e.type == CatalogType.Gate && p.minDistanceFromGate <= 0f)
                    failures.Add($"gate '{e.id}' has no minDistanceFromGate rule — gates could stack and wall off the lane");
            }
            log.AppendLine($"  placement math on {grid.gridWidth}x{grid.gridHeight} grid (cell {grid.cellSize}m) OK-checked");
        }

        // =====================================================================
        //  7. SELL REFUND — drive the REAL (private) BuildModeController
        //     .RefundCostFor on synthetic PlacedStructure components and assert
        //     the 50%-of-invested-cost rule: L1 = build/2 per slot (floor);
        //     L3 = (build + L1→L2 + L2→L3)/2 per slot. The WO-676 salvage bonus
        //     is read through the SAME public StatSum the production code calls,
        //     so a stray talent save cannot false-fail the oracle.
        // =====================================================================
        private static void CheckSellRefund(List<GameObject> created, List<string> failures, StringBuilder log)
        {
            var entry = CatalogRegistry.Get("tower_ground_archer");
            if (entry == null)
            {
                failures.Add("refund check: 'tower_ground_archer' not in registry (hydration failed)");
                return;
            }

            var refundMethod = typeof(BuildModeController).GetMethod("RefundCostFor",
                BindingFlags.NonPublic | BindingFlags.Static);
            if (refundMethod == null)
            {
                failures.Add("BuildModeController.RefundCostFor not found (renamed?) — the 50% sell rule is unverifiable");
                return;
            }

            // HeroTalentClassReader is INTERNAL to DeNelle.Village — resolve the SAME
            // slug production RefundCostFor uses via reflection (headless: no
            // GameStateService => "knight" => StatSum 0, the baseline identity).
            string slug = "knight";
            var readerType = typeof(EconomyService).Assembly.GetType("DeNelle.Village.HeroTalentClassReader");
            var slugMethod = readerType != null
                ? readerType.GetMethod("Slug", BindingFlags.Public | BindingFlags.Static) : null;
            if (slugMethod != null) slug = (string)slugMethod.Invoke(null, null) ?? "knight";
            float salvage = DeNelle.Village.Talents.HeroTalentModifiers.StatSum(slug, "salvage");
            if (salvage > 0f)
                log.AppendLine($"  note: salvage bonus {salvage:0.###} active in this environment — folded into expected refunds");

            // Local mirror of ApplySalvage (identity at salvage 0 — the headless norm).
            int Half(int slotTotal) => salvage > 0f
                ? Mathf.RoundToInt((slotTotal / 2) * (1f + salvage))
                : slotTotal / 2;

            foreach (int level in new[] { 1, 3 })
            {
                var go = new GameObject($"RefundOracle_L{level}");
                created.Add(go);
                var ps = go.AddComponent<PlacedStructure>();
                ps.itemId = entry.id;
                ps.level = level;
                ps.gridCell = new Vector2Int(5, 5);

                // Expected invested cost through the SAME public resolvers the game uses.
                CoreCost total = BuildModeController.CostFor(entry);
                for (int from = 1; from < level; from++)
                {
                    var step = BuildModeController.UpgradeCostFor(entry, from);
                    total.wood += step.wood; total.food += step.food;
                    total.iron += step.iron; total.crystals += step.crystals;
                }

                var refund = (CoreCost)refundMethod.Invoke(null, new object[] { ps });
                int ew = Half(total.wood), ef = Half(total.food), ei = Half(total.iron), ec = Half(total.crystals);
                if (refund.wood != ew || refund.food != ef || refund.iron != ei || refund.crystals != ec)
                    failures.Add($"sell refund L{level} '{entry.id}' = w{refund.wood} f{refund.food} i{refund.iron} c{refund.crystals}, " +
                                 $"expected 50% of invested (w{ew} f{ef} i{ei} c{ec}) — the half-back rule broke");
                else
                    log.AppendLine($"  REFUND {entry.id} L{level}: w{refund.wood} f{refund.food} i{refund.iron} c{refund.crystals} " +
                                   $"== 50% of invested (w{total.wood} f{total.food} i{total.iron} c{total.crystals}) OK");
            }
        }

        // =====================================================================
        //  8. BASELAYOUT REPLAY — (a) data-half: a synthetic PlacedStructureData
        //     list resolves ids, fits the grid, and its yaw/level round-trips
        //     inside the persisted contract; (b) factory-half: ONE record builds
        //     through the REAL StructureFactory.Create (the same call
        //     BaseLayoutLoader.Spawn makes) and comes back rendering. Loader-side
        //     collider/NavMesh wiring is play-mode (see header) — data half here.
        // =====================================================================
        private static void CheckBaseLayoutReplay(List<GameObject> created, List<string> failures, StringBuilder log)
        {
            var grid = Object.FindAnyObjectByType<PlacementGrid>();
            if (grid == null)
            {
                failures.Add("replay check: no PlacementGrid available (check 6 should have created one)");
                return;
            }

            var layout = new List<PlacedStructureData>
            {
                new PlacedStructureData("tower_ground_archer", 3, 5, 2, 1),
                new PlacedStructureData("wall_wood", 10, 10, 0, 2), // WO-948: wall_wood maxLevel is now 2 (wood->stone rung only)
                new PlacedStructureData("gate_stone", 20, 8, 1, 1, yawOffset: 45f),
            };

            foreach (var rec in layout)
            {
                var entry = CatalogRegistry.Get(rec.itemId);
                if (entry == null)
                { failures.Add($"replay record '{rec.itemId}' does not resolve in CatalogRegistry — the building would be lost on load"); continue; }

                // Cell claim fits the grid (the same InBounds gate placement validity uses).
                float metres = entry.repo != null && entry.repo.placement != null ? entry.repo.placement.footprint : 3f;
                float yawDeg = rec.yawSteps * 90f + rec.yawOffset;
                var fp = grid.FootprintCells(metres, yawDeg);
                if (!grid.InBounds(new Vector2Int(rec.cellX, rec.cellZ), fp))
                    failures.Add($"replay record '{rec.itemId}' at ({rec.cellX},{rec.cellZ}) footprint {fp.x}x{fp.y} falls outside the grid");

                // Persisted yaw contract: yawSteps 0..3 + a sub-90 offset reproduces the facing.
                if (rec.yawSteps < 0 || rec.yawSteps > 3)
                    failures.Add($"replay record '{rec.itemId}' yawSteps {rec.yawSteps} outside the 0..3 contract");
                if (rec.yawOffset < 0f || rec.yawOffset >= 90f)
                    failures.Add($"replay record '{rec.itemId}' yawOffset {rec.yawOffset} outside [0,90) — double-counts a quarter step");

                // Level within the entry's catalog ceiling.
                int maxLevel = entry.repo != null
                    ? Mathf.Clamp(entry.repo.maxLevel, 1, DeNelle.Core.Catalog.RepoProps.MaxStructureLevel) : 1;
                if (rec.level < 1 || rec.level > maxLevel)
                    failures.Add($"replay record '{rec.itemId}' level {rec.level} outside 1..{maxLevel}");
            }
            log.AppendLine($"  replay data-half: {layout.Count} synthetic record(s) validated against registry + grid");

            // Factory-half — the REAL create path (registry entry -> VisualFactory.Skin ->
            // render-verify -> AttachBehavior). Create returning null IS the data break
            // (missing/render-broken prefab); it Fail-logs the exact path itself.
            var archer = CatalogRegistry.Get("tower_ground_archer");
            var parent = new GameObject("BuildEconReplayRoot");
            created.Add(parent);
            GameObject builtRoot = null;
            try
            {
                builtRoot = StructureFactory.Create(archer,
                    new Pose(grid.CellToWorld(new Vector2Int(3, 5)), Quaternion.Euler(0f, 180f, 0f)),
                    parent.transform);
            }
            catch (System.Exception ex)
            {
                failures.Add($"StructureFactory.Create threw for 'tower_ground_archer': {ex.GetType().Name}: {ex.Message}");
            }
            if (builtRoot != null) created.Add(builtRoot);

            if (builtRoot == null)
            {
                failures.Add("StructureFactory.Create returned NULL for 'tower_ground_archer' — the replayed base would lose the tower (prefab missing/render-broken)");
            }
            else
            {
                if (builtRoot.GetComponentInChildren<Renderer>(true) == null)
                    failures.Add("replayed 'tower_ground_archer' has no Renderer — invisible structure");
                if (builtRoot.GetComponent<DefenseTower>() == null)
                    failures.Add("replayed 'tower_ground_archer' did not receive its DefenseTower behaviour (AttachBehavior broke)");
                else
                {
                    var dt = builtRoot.GetComponent<DefenseTower>();
                    if (!Mathf.Approximately(dt.Range, archer.repo.range) || !Mathf.Approximately(dt.Damage, archer.repo.damage))
                        failures.Add($"replayed tower stats diverge from catalog: range {dt.Range} vs {archer.repo.range}, damage {dt.Damage} vs {archer.repo.damage}");
                    else
                        log.AppendLine($"  replay factory-half: Create OK, DefenseTower carries catalog stats (range {dt.Range}, dmg {dt.Damage})");
                }
            }
        }

        // =====================================================================
        //  9. BUILD-TIMER CONFIG — the SAME resolve BuildTimerService performs
        //     (authored Resources asset, else code default). Curve + ad-skip +
        //     instant-finish + slot knobs must be sane and tier-monotonic.
        // =====================================================================
        private static void CheckBuildTimerConfig(List<string> failures, StringBuilder log)
        {
            var cfg = Resources.Load<BuildTimerConfig>(BuildTimerConfig.ResourcesPath);
            bool authored = cfg != null;
            if (cfg == null) cfg = BuildTimerConfig.CreateDefault();
            log.AppendLine($"  build-timer config: {(authored ? "authored asset" : "code default")} " +
                           $"(base {cfg.baseBuildSeconds}s, growth x{cfg.tierGrowth}, adSkip {cfg.adSkipSeconds}s, slots {cfg.freeBuildSlots})");

            if (cfg.baseBuildSeconds <= 0f) failures.Add("BuildTimerConfig.baseBuildSeconds <= 0 — every build finishes instantly (sink dead)");
            if (cfg.tierGrowth < 1f)        failures.Add($"BuildTimerConfig.tierGrowth {cfg.tierGrowth} < 1 — higher tiers get SHORTER (inverted curve)");
            if (cfg.maxDurationSeconds <= 0f) failures.Add("BuildTimerConfig.maxDurationSeconds <= 0 — the clamp zeroes every duration");
            if (cfg.freeBuildSlots < 1)     failures.Add("BuildTimerConfig.freeBuildSlots < 1 — nothing could ever build");
            if (cfg.adSkipSeconds <= 0f)    failures.Add("BuildTimerConfig.adSkipSeconds <= 0 — the WO-612 rewarded-ad skip does nothing");

            float prev = -1f;
            for (int tier = 0; tier <= 5; tier++)
            {
                float s = cfg.DurationSecondsForTier(tier, BuildJobKind.Build);
                if (s < 0f) failures.Add($"DurationSecondsForTier({tier}) negative ({s})");
                if (s > cfg.maxDurationSeconds + 0.01f)
                    failures.Add($"DurationSecondsForTier({tier}) = {s}s exceeds the {cfg.maxDurationSeconds}s ceiling — clamp broken");
                if (prev >= 0f && s < prev - 0.01f)
                    failures.Add($"build duration NOT tier-monotonic: tier {tier} = {s}s < tier {tier - 1} = {prev}s");
                prev = s;
            }
            float b1 = cfg.DurationSecondsForTier(1, BuildJobKind.Build);
            float u1 = cfg.DurationSecondsForTier(1, BuildJobKind.Upgrade);
            if (cfg.upgradeMultiplier >= 1f && u1 < b1 - 0.01f)
                failures.Add($"upgrade duration ({u1}s) shorter than build ({b1}s) despite multiplier x{cfg.upgradeMultiplier}");

            // Instant-finish: enabled → floor at the minimum price; disabled → always 0.
            if (cfg.instantFinishCrystalsPerMinute > 0)
            {
                int nearDone = cfg.InstantFinishPrice(1.0);
                if (nearDone < cfg.instantFinishMinCrystals)
                    failures.Add($"InstantFinishPrice(1s) = {nearDone} below the {cfg.instantFinishMinCrystals} minimum — near-done jobs finish for free");
                int hourJob = cfg.InstantFinishPrice(3600.0);
                if (hourJob < nearDone)
                    failures.Add($"InstantFinishPrice not monotonic: 1h job ({hourJob}) cheaper than a 1s job ({nearDone})");

                // ---------------------------------------------------------------
                //  THE SKIP CURVE'S **SHAPE** IS PINNED, NOT JUST ITS ENDPOINTS
                //  (owner ruling 2026-08-21 — convex skip pricing).
                //
                //  Endpoint checks alone (floor + monotonic, above) pass PERFECTLY on a
                //  linear curve, which is exactly the pricing the ruling replaced. So a
                //  future seat could set instantFinishCurveExponent back to 1, silently
                //  undo the ruling, and ship green. These three cases make that a GATE
                //  FAILURE. The defect they protect against is not a crash — it is the
                //  early ladder quietly becoming free again.
                // ---------------------------------------------------------------
                if (cfg.instantFinishCurveExponent >= 1f)
                    failures.Add($"[skip-curve] instantFinishCurveExponent is {cfg.instantFinishCurveExponent} — at 1 the " +
                                 "skip price is LINEAR in remaining time, which is the pricing the 2026-08-21 owner ruling " +
                                 "replaced (it let the whole early ladder skip for 3-31 crystals, so the paid skip was a " +
                                 "reflex rather than a decision). It must stay below 1.");
                if (cfg.instantFinishCurveExponent <= 0f)
                    failures.Add($"[skip-curve] instantFinishCurveExponent is {cfg.instantFinishCurveExponent} — a zero/negative " +
                                 "exponent makes long skips cost the same as or less than short ones.");

                // 0. THE FLOOR IS PART OF THE SHAPE AT THE BOTTOM OF THE LADDER, so it is
                //    pinned too. MEASURED, not assumed: at the authored exponent the raw curve
                //    prices a tier-0 build (45s) at FIVE crystals, so what actually makes the
                //    bottom rung cost anything is instantFinishMinCrystals, not the exponent.
                //    Dropping the floor back to its pre-ruling 3 restores a 5-crystal tier-0
                //    skip — and every OTHER check in this block still passes green, because
                //    they all sample at 10 minutes or longer. The exponent was therefore only
                //    half the ruling's protection; this is the other half. The constant is
                //    deliberately absolute: comparing the floor to cfg's own floor would be
                //    self-referential and could never fail.
                const int RuledMinSkipCrystals = 10;   // owner ruling 2026-08-21
                if (cfg.instantFinishMinCrystals < RuledMinSkipCrystals)
                    failures.Add($"[skip-curve] instantFinishMinCrystals is {cfg.instantFinishMinCrystals}, below the " +
                                 $"{RuledMinSkipCrystals} the 2026-08-21 ruling authored. The curve alone prices a tier-0 " +
                                 "build at ~5 crystals, so the FLOOR is what keeps the shortest timers from being free — " +
                                 "lowering it undoes the ruling in exactly the zone the ruling was written about, and the " +
                                 "exponent checks below cannot see it (they sample at 10+ minutes).");

                // 1. THE PER-MINUTE RATE MUST STRICTLY FALL as the wait grows. That is the
                //    whole ruling in one assertion: a short skip pays a premium RATE. On a
                //    linear curve these rates are equal, so this fails there too.
                double shortMin = 10.0, longMin = 600.0;
                double rateShort = cfg.InstantFinishPrice(shortMin * 60.0) / shortMin;
                double rateLong  = cfg.InstantFinishPrice(longMin * 60.0) / longMin;
                if (rateShort <= rateLong * 1.05)
                    failures.Add($"[skip-curve] the price PER MINUTE does not fall with wait length " +
                                 $"({rateShort:0.00} cr/min at {shortMin:0}m vs {rateLong:0.00} at {longMin:0}m). The curve has " +
                                 "been flattened toward linear, so short skips are cheap again and the tier ladder stops " +
                                 "being felt.");

                // 2. A MID-LENGTH SKIP MUST SIT ABOVE THE STRAIGHT LINE from origin to the
                //    anchor -- the geometric statement of "sub-linear total / falling rate".
                double anchorMin = Mathf.Max(1f, cfg.maxDurationSeconds) / 60.0;
                double midMin = anchorMin * 0.25;
                int midPrice = cfg.InstantFinishPrice(midMin * 60.0);
                double linearAtMid = cfg.instantFinishCrystalsPerMinute * midMin;
                if (midPrice <= linearAtMid * 1.10)
                    failures.Add($"[skip-curve] a quarter-length skip prices at {midPrice} crystals, essentially the LINEAR " +
                                 $"price ({linearAtMid:0}) — the convex curve is gone.");

                // 3. THE ANCHOR IS UNMOVED. The owner called the long end already sane and
                //    said to fix the shape, NOT the ceiling; this catches a "retune" that
                //    lifts the whole curve instead of reshaping it.
                int anchorPrice = cfg.InstantFinishPrice(cfg.maxDurationSeconds);
                int anchorExpected = Mathf.CeilToInt((float)(cfg.instantFinishCrystalsPerMinute * anchorMin));
                if (Mathf.Abs(anchorPrice - anchorExpected) > 1)
                    failures.Add($"[skip-curve] a full-length ({anchorMin / 60.0:0.#}h) skip prices at {anchorPrice} crystals, " +
                                 $"but the anchor must stay at crystalsPerMinute x minutes = {anchorExpected}. The owner ruled " +
                                 "the long end sane and the curve must reshape BELOW it, never inflate it.");
            }
            else if (cfg.InstantFinishPrice(3600.0) != 0)
            {
                failures.Add("instant-finish disabled (perMinute 0) but InstantFinishPrice(1h) != 0");
            }
        }

        // =====================================================================
        //  9b. WO-945 — FIRST-BUILD + ONBOARDING GRACE.
        //  ---------------------------------------------------------------------
        //  The owner ruling 2026-08-06 ("onboarding never stalls on a timer") was
        //  scoped per-structure-id, so the tutorial's SECOND tower of the same id
        //  ran the real ~90s curve straight into the scripted teaching wave
        //  (owner felt-report 2026-08-10). WO-945 makes the intent literal: while
        //  NOT Onboarded, every qualifying build gets firstBuildSeconds. Driven
        //  through the two REAL pure seams the placement path composes —
        //  BuildModeController.GraceReasonFor (the caller decision, which owns
        //  the pallets carve-out) and BuildTimerService.GraceAdjustedDurationMs
        //  (the duration math StartBuilderJob applies). The live Onboarded flag
        //  itself is a GameState read; driving the full MonoBehaviour Place()
        //  path needs play mode, so the decision seam takes the flag as input —
        //  the same data+logic-only contract as the rest of this oracle.
        // =====================================================================
        private static void CheckBuildGrace(List<string> failures, StringBuilder log)
        {
            int before = failures.Count;

            var cfg = Resources.Load<BuildTimerConfig>(BuildTimerConfig.ResourcesPath);
            if (cfg == null) cfg = BuildTimerConfig.CreateDefault();

            // -- A. THE DECISION (the real caller seam, pallets carve-out included) ----
            // (i) WO-945 headline: SECOND build of an already-built id (firstEverBuild
            //     false) while NOT Onboarded -> Onboarding grace.
            if (BuildModeController.GraceReasonFor(false, true, false) != BuildGraceReason.Onboarding)
                failures.Add("[grace] second build while NOT Onboarded got no Onboarding grace — WO-945 regressed " +
                             "(tutorial tower #2 runs the full curve into the teaching wave)");
            // (ii) Onboarded veteran, second build -> NO grace (the tier curve IS the economy).
            if (BuildModeController.GraceReasonFor(false, false, false) != BuildGraceReason.None)
                failures.Add("[grace] an ONBOARDED second build received a grace — the tier-curve economy is bypassed for veterans");
            // First-ever builds keep the FirstBuild reason in BOTH states (trace-baseline stability).
            if (BuildModeController.GraceReasonFor(true, false, false) != BuildGraceReason.FirstBuild)
                failures.Add("[grace] first-ever build (onboarded) lost its FirstBuild grace (owner ruling 2026-08-06)");
            if (BuildModeController.GraceReasonFor(true, true, false) != BuildGraceReason.FirstBuild)
                failures.Add("[grace] first-ever build during onboarding must keep the FirstBuild reason (trace precedence, WO-945)");
            // (iii) The pallets carve-out beats BOTH rules, in BOTH states.
            if (BuildModeController.GraceReasonFor(true,  true,  true) != BuildGraceReason.None ||
                BuildModeController.GraceReasonFor(false, true,  true) != BuildGraceReason.None ||
                BuildModeController.GraceReasonFor(true,  false, true) != BuildGraceReason.None)
                failures.Add("[grace] a pallet (storage container) received a build grace — the owner carve-out (2026-08-06) is broken");

            // -- B. THE DURATION (the pure math StartBuilderJob applies at job start) --
            double tier1Ms = cfg.DurationSecondsForTier(1, BuildJobKind.Build) * 1000.0;
            double graceMs = cfg.firstBuildSeconds * 1000.0;
            if (cfg.firstBuildSeconds > 0f && tier1Ms > graceMs)
            {
                double dOnboard = BuildTimerService.GraceAdjustedDurationMs(tier1Ms, BuildGraceReason.Onboarding, false, cfg.firstBuildSeconds);
                if (System.Math.Abs(dOnboard - graceMs) > 0.5)
                    failures.Add($"[grace] Onboarding grace produced {dOnboard}ms, expected firstBuildSeconds = {graceMs}ms");
                double dFirst = BuildTimerService.GraceAdjustedDurationMs(tier1Ms, BuildGraceReason.FirstBuild, false, cfg.firstBuildSeconds);
                if (System.Math.Abs(dFirst - graceMs) > 0.5)
                    failures.Add($"[grace] FirstBuild grace produced {dFirst}ms, expected firstBuildSeconds = {graceMs}ms");
            }
            else
            {
                log.AppendLine($"  [grace] curve/config leaves no shortening headroom (tier1 {tier1Ms}ms, grace {graceMs}ms) — shorten cases N/A");
            }
            double dNone = BuildTimerService.GraceAdjustedDurationMs(tier1Ms, BuildGraceReason.None, false, cfg.firstBuildSeconds);
            if (System.Math.Abs(dNone - tier1Ms) > 0.5)
                failures.Add($"[grace] reason None changed the duration ({tier1Ms}ms -> {dNone}ms) — the tier curve must be untouched without a grace");
            double dUp = BuildTimerService.GraceAdjustedDurationMs(tier1Ms, BuildGraceReason.Onboarding, true, cfg.firstBuildSeconds);
            if (System.Math.Abs(dUp - tier1Ms) > 0.5)
                failures.Add("[grace] an UPGRADE received the build grace — 'first build' means the first BUILD");
            // The only-ever-SHORTENS invariant: a 2s curve with a 5s grace stays 2s.
            double dShort = BuildTimerService.GraceAdjustedDurationMs(2000.0, BuildGraceReason.Onboarding, false, 5f);
            if (dShort != 2000.0)
                failures.Add($"[grace] grace LENGTHENED a 2s curve to {dShort}ms — the only-ever-shorten invariant is broken");
            // firstBuildSeconds = 0 disables the rule entirely (the config's stated contract).
            if (BuildTimerService.GraceAdjustedDurationMs(tier1Ms, BuildGraceReason.Onboarding, false, 0f) != tier1Ms)
                failures.Add("[grace] firstBuildSeconds=0 did not disable the grace");

            // -- C. COMPOSED (WO-945 acceptance 1) — both real seams end-to-end --------
            if (cfg.firstBuildSeconds > 0f && tier1Ms > graceMs)
            {
                double dTut = BuildTimerService.GraceAdjustedDurationMs(tier1Ms,
                    BuildModeController.GraceReasonFor(false, true, false), false, cfg.firstBuildSeconds);
                if (System.Math.Abs(dTut - graceMs) > 0.5)
                    failures.Add($"[grace] COMPOSED WO-945 case: not-yet-onboarded second build ran {dTut}ms, expected {graceMs}ms");
                double dVet = BuildTimerService.GraceAdjustedDurationMs(tier1Ms,
                    BuildModeController.GraceReasonFor(false, false, false), false, cfg.firstBuildSeconds);
                if (System.Math.Abs(dVet - tier1Ms) > 0.5)
                    failures.Add($"[grace] COMPOSED veteran case: onboarded second build ran {dVet}ms, expected the tier curve {tier1Ms}ms");
                double dPal = BuildTimerService.GraceAdjustedDurationMs(tier1Ms,
                    BuildModeController.GraceReasonFor(false, true, true), false, cfg.firstBuildSeconds);
                if (System.Math.Abs(dPal - tier1Ms) > 0.5)
                    failures.Add($"[grace] COMPOSED pallet case: a pallet during onboarding ran {dPal}ms, expected its real timer {tier1Ms}ms");
            }

            if (failures.Count == before)
                log.AppendLine($"  [grace] WO-945 first-build + onboarding grace OK (decision + duration + carve-out + " +
                               $"only-shortens; grace {cfg.firstBuildSeconds}s vs tier1 {tier1Ms / 1000.0}s)");
        }

        // =====================================================================
        //  10. DAMAGE STATES — the REAL DamageStatesCatalog loader: thresholds
        //     parse + stay ordered (0 < fire <= smolder < 1), every perType key
        //     resolves through the same Resolve path the visuals read, and the
        //     authored optOuts (gate/heart bespoke tells) hold.
        // =====================================================================
        private static void CheckDamageStates(List<string> failures, StringBuilder log)
        {
            DamageStatesCatalog.Invalidate();   // force a fresh read through CanonicalJson

            float smolder = DamageStatesCatalog.Smolder("wall");   // any non-override key = defaults
            float fire    = DamageStatesCatalog.Fire("wall");
            float bar     = DamageStatesCatalog.BarOffset("wall");
            int   loops   = DamageStatesCatalog.MaxBurnLoops;
            log.AppendLine($"  damage-states defaults: smolder {smolder}, fire {fire}, barOffset {bar}, maxBurnLoops {loops}");

            if (!(fire > 0f && fire < 1f))      failures.Add($"damage-states fire threshold {fire} outside (0,1)");
            if (!(smolder > 0f && smolder < 1f)) failures.Add($"damage-states smolder threshold {smolder} outside (0,1)");
            if (fire > smolder)
                failures.Add($"damage-states thresholds INVERTED: fire {fire} > smolder {smolder} — the full-burn tell would show before the smolder");
            if (bar <= 0f)   failures.Add($"damage-states barOffset {bar} <= 0 — the health bar spawns inside the mesh");
            if (loops < 1)   failures.Add($"damage-states maxBurnLoops {loops} < 1");

            // -- WO-892: the critical-save beacon dials --------------------------
            // The beacon is the "repair me NOW before it is destroyed" alarm. Two things
            // must hold or it stops meaning anything:
            //   * it must arm no EARLIER than the fire tell, or the alarm becomes the
            //     normal state of a lightly damaged base and the player learns to ignore it;
            //   * its cap must be a REAL cap and must leave headroom under VFXManager's
            //     global 20-loop budget once maxBurnLoops is also spent, or the beacon and
            //     the burn loops starve each other and whichever asks second shows nothing.
            float beacon  = DamageStatesCatalog.CriticalBeacon("wall");
            int   beacons = DamageStatesCatalog.MaxCriticalBeacons;
            log.AppendLine($"  damage-states beacon: criticalBeacon {beacon}, maxCriticalBeacons {beacons} " +
                           $"(worst-case held loops = maxBurnLoops + maxCriticalBeacons = {loops + beacons})");

            if (!(beacon > 0f && beacon < 1f))
                failures.Add($"damage-states criticalBeacon threshold {beacon} outside (0,1)");
            if (beacon > fire)
                failures.Add($"damage-states criticalBeacon {beacon} > fire {fire} - the save-me alarm would " +
                             "arm BEFORE the building is even on fire, so it would be the normal state of a " +
                             "damaged base rather than a call to act");
            if (beacons < 1)
                failures.Add($"damage-states maxCriticalBeacons {beacons} < 1 - the critical tell could never show");
            if (loops + beacons > 16)
                failures.Add($"damage-states maxBurnLoops {loops} + maxCriticalBeacons {beacons} = {loops + beacons} " +
                             "leaves under 4 of VFXManager's 20 global loop slots for the whole rest of the game " +
                             "(hero HP aura, boss aura, pet auras, portals, harvest nodes) - structure damage " +
                             "would silently starve every other held effect");

            // Every authored perType key must map through the loader (parse-level check
            // over the raw JSON — the loader swallows unknown keys silently), and each
            // override that authors both thresholds must keep them ordered.
            string json = DeNelle.Core.CanonicalJson.Read("Data/Canonical/damage-states.json");
            if (string.IsNullOrEmpty(json))
            { failures.Add("damage-states.json unreadable"); return; }
            try
            {
                var raw = JsonConvert.DeserializeObject<Dictionary<string, object>>(json);
                if (raw == null || !raw.ContainsKey("defaults"))
                    failures.Add("damage-states.json has no 'defaults' block (loader would run on code defaults silently)");
                if (raw != null && raw.TryGetValue("perType", out var pt) && pt is Newtonsoft.Json.Linq.JObject perType)
                {
                    foreach (var kv in perType)
                    {
                        string key = kv.Key;
                        if (key != key.ToLowerInvariant())
                            failures.Add($"damage-states perType key '{key}' is not lowercase — the stated lowercase-kind contract");
                        float s = DamageStatesCatalog.Smolder(key);
                        float f = DamageStatesCatalog.Fire(key);
                        if (f > s)
                            failures.Add($"damage-states perType '{key}' resolves inverted thresholds (fire {f} > smolder {s})");
                        log.AppendLine($"  damage-states perType '{key}': optOut={DamageStatesCatalog.OptOut(key)} smolder={s} fire={f}");
                    }
                }
            }
            catch (System.Exception ex)
            {
                failures.Add($"damage-states.json raw parse failed: {ex.Message}");
            }
        }

        // =====================================================================
        //  11a. WO-855 PHASE 1 -- TOWER SPAM SOFTCAP
        //  ---------------------------------------------------------------------
        //  Phase 0 measured that NOTHING limited tower count: no cap, no singleton
        //  flag, flat cost -- the 1st and the 50th archer cost the same. This gate
        //  proves the one multiplier is (a) correctly shaped, (b) wired into the
        //  NEW-PLACEMENT path only, and (c) unable to touch the owner-locked
        //  freebies that ARE a fresh save's entire starting budget.
        // =====================================================================
        private static void CheckTowerSoftcap(List<CatalogEntry> entries, List<GameObject> created,
                                              List<string> failures, StringBuilder log)
        {
            var archer = CatalogRegistry.Get("tower_ground_archer");
            if (archer == null)
            {
                failures.Add("[softcap] 'tower_ground_archer' not in registry -- the softcap gate cannot run");
                return;
            }

            // -- A. PURE CURVE -- monotonic, flat below the start ordinal, capped --
            int start = BuildModeController.TowerSoftcapStartAtOrdinal;
            float per  = BuildModeController.TowerSoftcapMultPerExtra;
            float cap  = BuildModeController.TowerSoftcapMaxMult;
            if (start < 2)  failures.Add($"[softcap] startAtOrdinal {start} < 2 -- the very FIRST tower would be surcharged");
            if (per <= 0f)  failures.Add($"[softcap] multPerExtra {per} <= 0 -- tower spam is not self-limiting at all");
            if (cap <= 1f)  failures.Add($"[softcap] maxMult {cap} <= 1 -- the ceiling cancels the whole curve");

            for (int ordinal = 1; ordinal < start; ordinal++)
            {
                float m = BuildModeController.TowerSoftcapMultiplier(ordinal);
                if (!Mathf.Approximately(m, 1f))
                    failures.Add($"[softcap] tower #{ordinal} multiplier {m} != 1.0 -- the first {start - 1} towers must be un-surcharged");
            }

            float expectFirstSurcharge = Mathf.Min(cap, 1f + per);
            float gotFirstSurcharge = BuildModeController.TowerSoftcapMultiplier(start);
            if (!Mathf.Approximately(gotFirstSurcharge, expectFirstSurcharge))
                failures.Add($"[softcap] tower #{start} multiplier {gotFirstSurcharge} != expected {expectFirstSurcharge} (1 + multPerExtra)");

            float prevMult = 0f;
            bool everRose = false;
            for (int ordinal = 1; ordinal <= 60; ordinal++)
            {
                float m = BuildModeController.TowerSoftcapMultiplier(ordinal);
                if (m < prevMult - 0.0001f)
                    failures.Add($"[softcap] multiplier NOT monotonic: #{ordinal} = {m} < #{ordinal - 1} = {prevMult}");
                if (m > cap + 0.0001f)
                    failures.Add($"[softcap] multiplier {m} at #{ordinal} exceeds the {cap} ceiling -- the clamp broke");
                if (m > prevMult + 0.0001f && ordinal > 1) everRose = true;
                prevMult = m;
            }
            if (!everRose)
                failures.Add("[softcap] multiplier never rose across 60 placements -- tower spam is still free");
            if (!Mathf.Approximately(BuildModeController.TowerSoftcapMultiplier(60), cap))
                failures.Add($"[softcap] multiplier at #60 = {BuildModeController.TowerSoftcapMultiplier(60)} -- expected the {cap} ceiling");
            log.AppendLine($"  [softcap] curve: #1..#{start - 1} x1.00, #{start} x{gotFirstSurcharge:0.##}, " +
                           $"#8 x{BuildModeController.TowerSoftcapMultiplier(8):0.##}, ceiling x{cap:0.##} OK");

            // -- B. PURE APPLICATION -- a tower row escalates, slot by slot --------
            CoreCost archerBase = BuildModeController.CostFor(archer);
            CoreCost at0 = BuildModeController.ApplyTowerSoftcap(archer, archerBase, 0);
            CoreCost at4 = BuildModeController.ApplyTowerSoftcap(archer, archerBase, start - 1);   // placing #start
            CoreCost at7 = BuildModeController.ApplyTowerSoftcap(archer, archerBase, start + 2);   // three later
            if (Total(at0) != Total(archerBase))
                failures.Add($"[softcap] cost with 0 live towers ({Total(at0)}) != the authored cost ({Total(archerBase)}) -- the softcap leaked onto tower #1");
            if (Total(at4) <= Total(archerBase))
                failures.Add($"[softcap] tower #{start} total {Total(at4)} did NOT rise above the authored {Total(archerBase)} -- the multiplier is not applied");
            if (Total(at7) <= Total(at4))
                failures.Add($"[softcap] tower #{start + 3} total {Total(at7)} did not exceed tower #{start} total {Total(at4)} -- the escalation is flat");
            log.AppendLine($"  [softcap] '{archer.id}' basket-total: #1={Total(at0)} -> #{start}={Total(at4)} -> #{start + 3}={Total(at7)} OK");

            // -- C. NON-TOWER IMMUNITY -- a building row is never surcharged -------
            CatalogEntry nonTower = null;
            foreach (var e in entries)
            {
                if (e == null || e.repo == null || BuildModeController.IsTowerEntry(e)) continue;
                if (BuildModeController.CostFor(e).IsZero) continue;
                nonTower = e; break;
            }
            if (nonTower == null)
            {
                failures.Add("[softcap] no non-tower priced row found -- the non-tower immunity gate could not run");
            }
            else
            {
                CoreCost nBase = BuildModeController.CostFor(nonTower);
                CoreCost nAt50 = BuildModeController.ApplyTowerSoftcap(nonTower, nBase, 50);
                if (Total(nAt50) != Total(nBase))
                    failures.Add($"[softcap] non-tower '{nonTower.id}' was surcharged at 50 live towers " +
                                 $"({Total(nBase)} -> {Total(nAt50)}) -- the softcap must be tower-class only");
                else
                    log.AppendLine($"  [softcap] non-tower '{nonTower.id}' immune at 50 live towers OK");
            }

            // -- D. LIVE WIRING -- real PlacedStructure towers in the world raise the
            //      PLACE cost, and leave UPGRADE + REFUND untouched (WO-855 sec.5:
            //      "does not apply to upgrades of existing towers, only new place").
            //      Baseline first: check 7 leaves its own tower PlacedStructures alive
            //      until the shared finally, so we must measure, not assume, zero.
            BuildModeController.InvalidateTowerCount();
            int baseline = BuildModeController.LiveTowerCount();
            int want = start + 3;                       // enough to be well past the surcharge start
            for (int i = baseline; i < want; i++)
            {
                var go = new GameObject($"SoftcapOracleTower_{i}");
                created.Add(go);
                var ps = go.AddComponent<PlacedStructure>();
                ps.itemId = archer.id;
                ps.level = 1;
                ps.gridCell = new Vector2Int(40 + i, 40);
            }
            BuildModeController.InvalidateTowerCount();
            int live = BuildModeController.LiveTowerCount();
            if (live < want)
            {
                failures.Add($"[softcap] LiveTowerCount() returned {live} after seeding {want} tower PlacedStructure(s) " +
                             "-- the live census does not see catalog-placed towers (the softcap would never arm)");
            }
            else
            {
                CoreCost livePlace = BuildModeController.SoftcappedCostFor(archer);
                if (Total(livePlace) <= Total(archerBase))
                    failures.Add($"[softcap] SoftcappedCostFor with {live} live towers = {Total(livePlace)}, " +
                                 $"not above the authored {Total(archerBase)} -- the softcap is NOT wired into the place path");
                else
                    log.AppendLine($"  [softcap] live wiring: {live} towers standing -> place cost {Total(archerBase)} -> {Total(livePlace)} OK");

                // UPGRADE must be identical to the pure fallback off the RAW build cost.
                CoreCost upStep = BuildModeController.UpgradeCostFor(archer, 1);
                CoreCost expectUp = ExpectedUpgradeStep(archer, 1, archerBase);
                if (Total(upStep) != Total(expectUp))
                    failures.Add($"[softcap] UpgradeCostFor(L1->L2) = {Total(upStep)} with {live} live towers, " +
                                 $"expected {Total(expectUp)} off the RAW cost -- the softcap leaked into UPGRADES");
                else
                    log.AppendLine($"  [softcap] UpgradeCostFor unchanged at {live} live towers ({Total(upStep)}) OK");

                // REFUND must be identical too (RefundCostFor sums CostFor + upgrade steps).
                var refundMethod = typeof(BuildModeController).GetMethod("RefundCostFor",
                    BindingFlags.NonPublic | BindingFlags.Static);
                if (refundMethod == null)
                {
                    failures.Add("[softcap] RefundCostFor not found -- the refund-immunity gate could not run");
                }
                else
                {
                    var rgo = new GameObject("SoftcapRefundProbe");
                    created.Add(rgo);
                    var rps = rgo.AddComponent<PlacedStructure>();
                    rps.itemId = archer.id;
                    rps.level = 1;
                    rps.gridCell = new Vector2Int(2, 2);
                    // NOTE: this probe is itself a tower PlacedStructure, so it nudges the live
                    // count by one -- which is precisely the point: the refund must not move.
                    BuildModeController.InvalidateTowerCount();
                    var refund = (CoreCost)refundMethod.Invoke(null, new object[] { rps });
                    if (refund.wood != archerBase.wood / 2 || refund.iron != archerBase.iron / 2 ||
                        refund.food != archerBase.food / 2 || refund.crystals != archerBase.crystals / 2)
                    {
                        // Only a genuine leak fails here; the WO-676 salvage talent is 0 headless.
                        float salvage = DeNelle.Village.Talents.HeroTalentModifiers.StatSum("knight", "salvage");
                        if (salvage <= 0f)
                            failures.Add($"[softcap] RefundCostFor L1 = w{refund.wood}/f{refund.food}/i{refund.iron}/c{refund.crystals} " +
                                         $"with towers standing, expected 50% of the RAW cost " +
                                         $"(w{archerBase.wood / 2}/f{archerBase.food / 2}/i{archerBase.iron / 2}/c{archerBase.crystals / 2}) " +
                                         "-- the softcap leaked into REFUNDS");
                    }
                    else
                    {
                        log.AppendLine("  [softcap] RefundCostFor unchanged with towers standing OK");
                    }
                }
            }

            // -- E. FREEBIES SURVIVE THE SOFTCAP -- the owner-locked free placements ARE
            //      the starting budget on a v32 zero-resource save (starting wood/iron
            //      are 0), so a softcap that charged placement #1 or #2 would soft-lock
            //      a fresh run. Driven on a THROWAWAY GameState with the softcap fully
            //      armed (towers still standing from D).
            CheckFreebiesUnderSoftcap(archer, failures, log);

            BuildModeController.InvalidateTowerCount();   // leave no stale census for later suites
        }

        /// <summary>The pure UpgradeCostFor expectation off a RAW build cost (authored table wins, else base x fromLevel).</summary>
        private static CoreCost ExpectedUpgradeStep(CatalogEntry entry, int fromLevel, CoreCost rawBase)
        {
            var repo = entry != null ? entry.repo : null;
            int idx = Mathf.Max(0, fromLevel - 1);
            if (repo != null && repo.upgradeCost != null && idx < repo.upgradeCost.Length && !repo.upgradeCost[idx].IsZero)
                return repo.upgradeCost[idx];
            int scale = Mathf.Max(1, fromLevel);
            return new CoreCost
            {
                wood     = rawBase.wood     * scale,
                food     = rawBase.food     * scale,
                iron     = rawBase.iron     * scale,
                crystals = rawBase.crystals * scale,
            };
        }

        // =====================================================================
        //  11b. FREEBIES UNDER THE ARMED SOFTCAP + the zero-resource founding walk.
        //  Installs a throwaway GameState through the SAME private seams the
        //  FoundingReachabilityRegression oracle uses, then drives the REAL
        //  BuildModeController.FreeBuildAvailable / EffectiveCostFor.
        // =====================================================================
        private static void CheckFreebiesUnderSoftcap(CatalogEntry archer, List<string> failures, StringBuilder log)
        {
            var stateField = typeof(GameStateService).GetField("_state", BindingFlags.NonPublic | BindingFlags.Instance);
            var instField  = typeof(GameStateService).GetField("_instance", BindingFlags.NonPublic | BindingFlags.Static);
            if (stateField == null || instField == null)
            {
                failures.Add("[softcap] GameStateService _state/_instance seams not reflectable -- the freebie gate could not run");
                return;
            }

            var priorInstance = GameStateService.Instance;
            GameObject gssGo = null;
            GameState throwaway = null;
            try
            {
                // A v32 fresh save exactly as GameStateService.ResetToNewGame leaves it:
                // ZERO wood / ZERO iron / empty freebie ledger. The freebies literally ARE
                // the starting budget, which is why they are load-bearing here.
                throwaway = ScriptableObject.CreateInstance<GameState>();
                throwaway.Resources = ResourceBalance.Zero;
                throwaway.Wood = StartingBudget.StrategicWood;
                throwaway.Iron = StartingBudget.StrategicIron;
                throwaway.FreeBuildsUsed = new List<string>();
                gssGo = new GameObject("GSS (softcap freebie oracle)");
                var gss = gssGo.AddComponent<GameStateService>();
                stateField.SetValue(gss, throwaway);
                instField.SetValue(null, gss);

                if (StartingBudget.StrategicWood != 0 || StartingBudget.StrategicIron != 0)
                    log.AppendLine($"  [softcap] note: StartingBudget is no longer zero " +
                                   $"(wood {StartingBudget.StrategicWood}, iron {StartingBudget.StrategicIron}) -- " +
                                   "the freebies are no longer the only route through founding");
                else
                    log.AppendLine("  [softcap] fresh save: 0 wood / 0 iron -- the placement freebies ARE the starting budget");

                BuildModeController.InvalidateTowerCount();
                int liveNow = BuildModeController.LiveTowerCount();
                if (liveNow < BuildModeController.TowerSoftcapStartAtOrdinal)
                    log.AppendLine($"  [softcap] note: only {liveNow} live tower(s) while checking freebies -- surcharge may not be armed");

                // The two WOODEN archer freebies must both be EXACTLY zero with the
                // softcap armed. This is the soft-lock guard: charging either one on a
                // 0-wood / 0-iron save leaves the player with no way to found.
                for (int placement = 1; placement <= 2; placement++)
                {
                    if (!BuildModeController.FreeBuildAvailable(archer))
                    {
                        failures.Add($"[softcap] archer placement #{placement} is NOT free -- the owner-locked " +
                                     "2 wooden-tower freebie broke (a fresh 0-resource save cannot found)");
                        break;
                    }
                    CoreCost eff = BuildModeController.EffectiveCostFor(archer);
                    if (!eff.IsZero)
                    {
                        failures.Add($"[softcap] archer placement #{placement} costs " +
                                     $"w{eff.wood}/f{eff.food}/i{eff.iron}/c{eff.crystals} with the softcap armed -- " +
                                     "the freebie MUST short-circuit before the multiplier");
                        break;
                    }
                    throwaway.FreeBuildsUsed.Add(archer.id);   // burn it, exactly as Place() does
                }
                log.AppendLine("  [softcap] both wooden archer freebies still cost ZERO with the softcap armed OK");

                // The THIRD archer is charged -- and, with the softcap armed, charged MORE
                // than the authored cost. This is the whole point of Phase 1.
                if (BuildModeController.FreeBuildAvailable(archer))
                {
                    failures.Add("[softcap] archer placement #3 is still FREE -- the 2-cap wooden freebie is not being burned");
                }
                else
                {
                    CoreCost third = BuildModeController.EffectiveCostFor(archer);
                    CoreCost raw = BuildModeController.CostFor(archer);
                    if (third.IsZero)
                        failures.Add("[softcap] archer placement #3 resolves ZERO cost -- placements past the freebie must be charged");
                    else if (liveNow >= BuildModeController.TowerSoftcapStartAtOrdinal && Total(third) <= Total(raw))
                        failures.Add($"[softcap] archer placement #3 costs {Total(third)} with {liveNow} towers standing -- " +
                                     $"not above the authored {Total(raw)}; EffectiveCostFor is not routed through the softcap");
                    else
                        log.AppendLine($"  [softcap] archer placement #3 charged {Total(third)} (authored {Total(raw)}, {liveNow} towers standing) OK");
                }

                // FOUNDING WALK on a 0-wood / 0-iron save: every id the founding tutorial
                // forces must resolve a ZERO effective cost at its first placement, in
                // sequence, with the softcap armed. Total must be exactly 0.
                throwaway.FreeBuildsUsed.Clear();
                var foundingWalk = new[] { "pet-house", "collector_lumbermill", "tower_ground_archer" };
                int walkTotal = 0;
                foreach (var id in foundingWalk)
                {
                    var e = CatalogRegistry.Get(id);
                    if (e == null)
                    {
                        failures.Add($"[softcap] founding-walk id '{id}' is not in the catalog -- the founding sequence cannot complete");
                        continue;
                    }
                    CoreCost c = BuildModeController.EffectiveCostFor(e);
                    walkTotal += Total(c);
                    if (!c.IsZero)
                        failures.Add($"[softcap] founding step '{id}' costs w{c.wood}/f{c.food}/i{c.iron}/c{c.crystals} " +
                                     "on a fresh 0-wood/0-iron save -- the founding sequence SOFT-LOCKS");
                    throwaway.FreeBuildsUsed.Add(id);
                }
                if (walkTotal == 0)
                    log.AppendLine($"  [softcap] founding walk ({string.Join(" -> ", foundingWalk)}) totals 0 on a fresh save OK");
            }
            catch (System.Exception ex)
            {
                failures.Add($"[softcap] freebie gate threw: {ex.GetType().Name}: {ex.Message}");
            }
            finally
            {
                if (gssGo != null) Object.DestroyImmediate(gssGo);
                if (throwaway != null) Object.DestroyImmediate(throwaway);
                instField.SetValue(null, priorInstance);
            }
        }

        // =====================================================================
        //  11c. WO-855 PHASE 4 -- BUILD TIER IS NO LONGER A CONSTANT.
        //  ---------------------------------------------------------------------
        //  Phase 0 measured BuildModeController passing a hard-coded literal 0 as the
        //  tier to BuildTimerService.StartBuild, so EVERY structure in the game built
        //  in exactly baseBuildSeconds and BuildTimerConfig.tierGrowth was unreachable
        //  dead tuning. The FIRST gate below FAILS against the pre-fix tree by design.
        // =====================================================================
        private static void CheckBuildTierDerivation(List<CatalogEntry> entries, List<string> failures, StringBuilder log)
        {
            // -- A. THE CALL SITE -- this gate is RED on the pre-WO-855 tree -------
            string bmcPath = System.IO.Path.Combine(Application.dataPath,
                "_Modules", "Village", "BuildMode", "BuildModeController.cs");
            if (!System.IO.File.Exists(bmcPath))
            {
                failures.Add("[build-tier] BuildModeController.cs not found -- the hard-coded-tier gate cannot run");
            }
            else
            {
                string src;
                try { src = System.IO.File.ReadAllText(bmcPath); }
                catch (System.Exception ex) { src = null; failures.Add($"[build-tier] BuildModeController.cs unreadable ({ex.Message})"); }
                if (src != null)
                {
                    if (src.IndexOf("StartBuild(jobKey, 0)", System.StringComparison.Ordinal) >= 0)
                        failures.Add("[build-tier] BuildModeController still calls StartBuild(jobKey, 0) -- the HARD-CODED tier is back, " +
                                     "so every structure in the game builds in exactly baseBuildSeconds and tierGrowth is dead tuning");
                    if (src.IndexOf("TierForCost(", System.StringComparison.Ordinal) < 0)
                        failures.Add("[build-tier] BuildModeController does not call BuildTimerConfig.TierForCost -- the placement path " +
                                     "is not deriving a real build tier");
                    else
                        log.AppendLine("  [build-tier] placement path derives its tier via TierForCost (no hard-coded 0) OK");
                }
            }

            // Same resolve BuildTimerService performs (authored asset, else code default).
            // Explicit null test, NOT ??, so Unity's fake-null can never slip a dead object through.
            var cfg = Resources.Load<BuildTimerConfig>(BuildTimerConfig.ResourcesPath);
            if (cfg == null) cfg = BuildTimerConfig.CreateDefault();

            // -- B. THE BANDS -- ascending, positive, and actually reachable -------
            var bands = cfg.tierCostThresholds;
            if (bands == null || bands.Length == 0)
            {
                failures.Add("[build-tier] tierCostThresholds is empty -- every structure collapses back to tier 0 (a flat timer)");
                return;
            }
            for (int i = 0; i < bands.Length; i++)
            {
                if (bands[i] <= 0f)
                    failures.Add($"[build-tier] tierCostThresholds[{i}] = {bands[i]} <= 0 -- a non-positive band makes the tier meaningless");
                if (i > 0 && bands[i] <= bands[i - 1])
                    failures.Add($"[build-tier] tierCostThresholds not ascending: [{i}] {bands[i]} <= [{i - 1}] {bands[i - 1]}");
            }

            // -- C. TWO REAL CATALOG ROWS PRODUCE TWO DIFFERENT DURATIONS --------
            //      (the whole point of the fix -- pre-WO-855 every row produced 15s).
            var tierHistogram = new Dictionary<int, int>();
            var byTier = new Dictionary<int, string>();
            foreach (var e in entries)
            {
                if (e == null || e.repo == null) continue;
                CoreCost c = BuildModeController.CostFor(e);
                if (c.IsZero) continue;
                int t = cfg.TierForCost(c);
                tierHistogram.TryGetValue(t, out int n);
                tierHistogram[t] = n + 1;
                if (!byTier.ContainsKey(t)) byTier[t] = e.id;
            }
            if (tierHistogram.Count < 2)
            {
                failures.Add($"[build-tier] every priced catalog row resolves the SAME tier ({tierHistogram.Count} distinct) -- " +
                             "build duration is still a constant; the cost bands do not split the catalog");
            }
            else
            {
                var tiersSorted = new List<int>(byTier.Keys);
                tiersSorted.Sort();
                int lo = tiersSorted[0], hi = tiersSorted[tiersSorted.Count - 1];
                float dLo = cfg.DurationSecondsForTier(lo, BuildJobKind.Build);
                float dHi = cfg.DurationSecondsForTier(hi, BuildJobKind.Build);
                if (dHi <= dLo)
                    failures.Add($"[build-tier] '{byTier[hi]}' (tier {hi}, {dHi}s) does not build LONGER than '{byTier[lo]}' (tier {lo}, {dLo}s)");
                else
                    log.AppendLine($"  [build-tier] '{byTier[lo]}' tier {lo} = {dLo:0}s vs '{byTier[hi]}' tier {hi} = {dHi:0}s -- " +
                                   "two rows, two durations OK");
                var histo = new List<string>();
                foreach (var t in tiersSorted)
                    histo.Add($"t{t}x{tierHistogram[t]}({cfg.DurationSecondsForTier(t, BuildJobKind.Build):0}s)");
                log.AppendLine("  [build-tier] catalog tier histogram: " + string.Join(" ", histo));
            }

            // -- D. MOBILE SHAPE -- snappy early, long endgame (WO-855 sec.4.6) ----
            float tier0 = cfg.DurationSecondsForTier(0, BuildJobKind.Build);
            if (tier0 > 180f)
                failures.Add($"[build-tier] tier-0 build is {tier0}s -- the first 10 minutes of a new save would be gated " +
                             "(owner: early builds stay snappy)");
            int top = cfg.MaxReachableTier;
            float topSec = cfg.DurationSecondsForTier(top, BuildJobKind.Build);
            if (topSec < 30f * 60f)
                failures.Add($"[build-tier] the top reachable tier ({top}) builds in {topSec}s -- the endgame has no wall-clock drag " +
                             "(owner: endgame long, hours)");
            log.AppendLine($"  [build-tier] shape: tier0 {tier0:0}s .. top reachable tier {top} {topSec / 3600f:0.##}h " +
                           $"(base {cfg.baseBuildSeconds}s, growth x{cfg.tierGrowth}, upgradeMult x{cfg.upgradeMultiplier}, slots {cfg.freeBuildSlots})");

            // -- E. THE CLAMP HOLDS AT (AND PAST) THE HIGHEST REACHABLE TIER -----
            //      A builder-queue job that becomes reachable at high tiers must still
            //      respect maxDurationSeconds -- including the upgrade multiplier and a
            //      few tiers of headroom past the top band.
            for (int t = top; t <= top + 3; t++)
            {
                float b = cfg.DurationSecondsForTier(t, BuildJobKind.Build);
                float u = cfg.DurationSecondsForTier(t, BuildJobKind.Upgrade);
                if (b > cfg.maxDurationSeconds + 0.01f)
                    failures.Add($"[build-tier] BUILD duration at tier {t} = {b}s exceeds maxDurationSeconds {cfg.maxDurationSeconds}s");
                if (u > cfg.maxDurationSeconds + 0.01f)
                    failures.Add($"[build-tier] UPGRADE duration at tier {t} = {u}s exceeds maxDurationSeconds {cfg.maxDurationSeconds}s " +
                                 "(the upgradeMultiplier escapes the clamp)");
            }
            if (cfg.freeBuildSlots != 2)
                failures.Add($"[build-tier] freeBuildSlots is {cfg.freeBuildSlots} -- WO-855 sec.4.6 keeps it at 2 (scarcity)");
            log.AppendLine($"  [build-tier] maxDurationSeconds {cfg.maxDurationSeconds / 3600f:0.#}h clamp holds through tier {top + 3} OK");
        }

        // =====================================================================
        //  11b. WO-1480 — WallSegment's tier ceiling, widened from the WO-1108b oracle.
        //
        //  WO-1108b made RepoProps.MaxStructureLevel the SINGLE structure ceiling by
        //  replacing eight hardcoded 3s. WallSegment.SetTier was a NINTH that was missed:
        //  it clamped 1..3, and that clamp fed a per-tier damage divisor table with exactly
        //  four slots — so this was a GAMEPLAY ceiling, not the documented 1..3 VISUAL tier
        //  selector (StructureTierVisual / BuildModeController / DefenseTower, deliberately
        //  left alone). Latent only because wall_wood authors maxLevel 2; the moment a wall
        //  is authored past 3, a level-4 wall silently takes level-3 damage reduction.
        //
        //  RED PROOF — against the pre-WO-1480 tree every one of these fails:
        //    * WallSegment.MaxTier did not exist (compile-red is the strongest RED there is);
        //    * SetTier(6) yielded Tier 3, not 6;
        //    * the source still carried the literal "Mathf.Clamp(tier, 1, 3)".
        //  It is asserted BEHAVIOURALLY (a real component through the real SetTier) AND by
        //  source scan, because a future seat can re-introduce the literal without changing
        //  today's numbers while wall_wood still stops at 2.
        // =====================================================================
        private static void CheckWallTierCeiling(List<string> failures, StringBuilder log)
        {
            int ceiling = RepoProps.MaxStructureLevel;

            if (WallSegment.MaxTier != ceiling)
                failures.Add($"[wall-tier-ceiling] WallSegment.MaxTier = {WallSegment.MaxTier} but the single structure " +
                             $"ceiling RepoProps.MaxStructureLevel = {ceiling} — the wall tier bound is a second, drifting copy");

            // -- A. THE DIVISOR IS DEFINED FOR EVERY ADMISSIBLE LEVEL -------------
            //    Tier 1 is x1 (no reduction) and every step is strictly tougher, all the way
            //    to the ceiling. A tabled divisor would return the same value for 4/5/6.
            float prev = 0f;
            for (int t = 1; t <= ceiling; t++)
            {
                float f = WallSegment.ToughnessFor(t);
                if (!(f > 0f) || float.IsNaN(f) || float.IsInfinity(f))
                {
                    failures.Add($"[wall-tier-ceiling] toughness divisor at tier {t} is {f} — a level the clamp admits has no " +
                                 "usable divisor, so its contact damage is undefined (divide-by-zero / NaN on the damage track)");
                    continue;
                }
                if (t == 1 && !Mathf.Approximately(f, 1f))
                    failures.Add($"[wall-tier-ceiling] tier 1 toughness is x{f} — a base wall must take damage unreduced (x1)");
                if (t > 1 && !(f > prev + 0.0001f))
                    failures.Add($"[wall-tier-ceiling] toughness at tier {t} (x{f}) is not greater than tier {t - 1} (x{prev}) — " +
                                 "the upgrade buys the player nothing at that rung (the 1..3 table repeating its top value)");
                prev = f;
            }

            // -- B. LEGACY PARITY -- the derived curve must reproduce the old table ---
            //    { 1f, 1.6f, 2.56f } for tiers 1..3, so this fix re-tunes NOTHING that ships.
            float[] legacy = { 1f, 1.6f, 2.56f };
            for (int i = 0; i < legacy.Length && i + 1 <= ceiling; i++)
            {
                float got = WallSegment.ToughnessFor(i + 1);
                if (Mathf.Abs(got - legacy[i]) > 0.01f)
                    failures.Add($"[wall-tier-ceiling] tier {i + 1} toughness is x{got}, was x{legacy[i]} before WO-1480 — " +
                                 "deriving the divisor must not re-tune the tiers that already shipped");
            }

            // -- C. THE REAL CLAMP, THROUGH THE REAL COMPONENT ---------------------
            var go = new GameObject("WO1480_WallTierProbe");
            try
            {
                var wall = go.AddComponent<WallSegment>();
                wall.SetTier(ceiling);
                if (wall.Tier != ceiling)
                    failures.Add($"[wall-tier-ceiling] SetTier({ceiling}) left Tier at {wall.Tier} — WallSegment clamps below the " +
                                 "structure ceiling, so an upgraded wall keeps a lower tier's damage reduction and nothing reports it");
                wall.SetTier(ceiling + 5);
                if (wall.Tier != ceiling)
                    failures.Add($"[wall-tier-ceiling] SetTier({ceiling + 5}) left Tier at {wall.Tier} — the clamp must still hold AT " +
                                 $"the ceiling ({ceiling}); an unbounded tier indexes past every per-tier curve in the game");
                wall.SetTier(0);
                if (wall.Tier != 1)
                    failures.Add($"[wall-tier-ceiling] SetTier(0) left Tier at {wall.Tier} — the floor must stay 1");
            }
            finally { Object.DestroyImmediate(go); }

            // -- D. THE LITERAL MUST NOT COME BACK ---------------------------------
            string wallPath = System.IO.Path.Combine(Application.dataPath,
                "_Modules", "Village", "Walls", "WallSegment.cs");
            if (!System.IO.File.Exists(wallPath))
            {
                failures.Add("[wall-tier-ceiling] WallSegment.cs not found — the literal-ceiling scan cannot run");
            }
            else
            {
                string src = null;
                try { src = System.IO.File.ReadAllText(wallPath); }
                catch (System.Exception ex) { failures.Add($"[wall-tier-ceiling] WallSegment.cs unreadable ({ex.Message})"); }
                if (src != null)
                {
                    if (src.IndexOf("Mathf.Clamp(tier, 1, 3)", System.StringComparison.Ordinal) >= 0)
                        failures.Add("[wall-tier-ceiling] WallSegment still clamps the tier with the literal Mathf.Clamp(tier, 1, 3) — " +
                                     "the ninth hardcoded structure ceiling is back; clamp to RepoProps.MaxStructureLevel, never a literal");
                    if (src.IndexOf("RepoProps.MaxStructureLevel", System.StringComparison.Ordinal) < 0)
                        failures.Add("[wall-tier-ceiling] WallSegment.cs no longer names RepoProps.MaxStructureLevel — the wall tier " +
                                     "bound has stopped reading the single ceiling");
                }
            }

            log.AppendLine($"  [wall-tier-ceiling] WallSegment clamps 1..{ceiling} off RepoProps.MaxStructureLevel; divisor derived and " +
                           $"strictly increasing to x{WallSegment.ToughnessFor(ceiling):0.00} (legacy x1/x1.6/x2.56 preserved) OK");
        }

        // =====================================================================
        //  12. FALLBACK FRESHNESS  (was: FALLBACK / CATALOG PARITY)
        //  ---------------------------------------------------------------------
        //  CatalogBootstrap.RegisterFallback is the JSON-load-FAILURE path: when
        //  structures-catalog.json cannot be READ, its rows ARE the game's content.
        //
        //  WHAT THIS GATE USED TO BE, AND WHY IT CHANGED (WO-1137, owner ruling
        //  2026-08-23). RegisterFallback used to hand-construct THREE of the catalog's
        //  twenty-eight rows in C# object initializers, and this gate deep-compared every
        //  public field of those constructed objects against the parsed catalog rows. It
        //  worked -- it caught real drift, repeatedly:
        //    - placement.footprint 2.5 vs the catalog's 1.75   (fixed 0ac59581)
        //    - visualPrefabPath "PatriciaLight/tower2" -- art from the Defend-the-Tower
        //      module REMOVED 2026-06-09 -- on two of the three rows
        //    - a missing upgradeTexturePath array, so a fallback-path L3 tower rendered
        //      untextured
        //  But it could only ever catch drift AFTER a human authored it, and it was blind
        //  to the larger defect: three rows out of twenty-eight meant the failure path
        //  shipped a fundamentally smaller game, silently. A gate cannot fix that.
        //
        //  The owner's ruling removed the authoring instead. RegisterFallback now parses
        //  CatalogFallbackData.Json -- the catalog embedded byte-for-byte as a string
        //  constant by DeNelle.Editor.CatalogFallbackGenerator -- through the SAME
        //  ParseAndRegister method the file path uses. Field-level parity is now true BY
        //  CONSTRUCTION and there is nothing left for a field-compare to disagree about.
        //
        //  SO THIS GATE BECAME THE ONE THING CODEGEN CAN STILL GET WRONG: FRESHNESS.
        //  A generated file is only correct until someone edits the source and forgets to
        //  regenerate. That is the whole remaining failure mode, and it is what this now
        //  proves, at the same seam, under the same "[fallback-parity]" tag:
        //    A. the two canonical copies (Resources + StreamingAssets) are byte-identical;
        //    B. CatalogFallbackData.SourceSha256 equals the SHA-256 of the catalog on disk
        //       -- i.e. the generated file is NOT STALE;
        //    C. the embedded string itself still hashes to that same SHA -- i.e. nobody
        //       hand-edited the generated file (its banner says not to);
        //    D. the declared row count / schema version match the file; and
        //    E. RegisterFallback actually REGISTERS every one of those rows, by id, when
        //       invoked -- so a fallback that compiles but parses to nothing cannot pass.
        //
        //  Every failure message names the regeneration command verbatim. A gate whose
        //  remedy the reader has to go look up is a gate people route around.
        // =====================================================================

        /// <summary>Repo-relative canonical copy the generator reads (CanonicalJson resolves it first).</summary>
        private const string CanonicalResourcesCopy =
            "Assets/Resources/Data/Canonical/structures-catalog.json";

        /// <summary>Repo-relative authoring copy. Must stay BYTE-IDENTICAL to the Resources copy.</summary>
        private const string CanonicalStreamingCopy =
            "Assets/StreamingAssets/Data/Canonical/structures-catalog.json";

        private static void CheckFallbackParity(List<CatalogEntry> entries, List<string> failures, StringBuilder log)
        {
            string regen = CatalogFallbackData.RegenerateCommand;
            string repoRoot = System.IO.Path.GetDirectoryName(Application.dataPath);

            string resAbs    = System.IO.Path.Combine(repoRoot, CanonicalResourcesCopy.Replace('/', System.IO.Path.DirectorySeparatorChar));
            string streamAbs = System.IO.Path.Combine(repoRoot, CanonicalStreamingCopy.Replace('/', System.IO.Path.DirectorySeparatorChar));

            if (!System.IO.File.Exists(resAbs))
            {
                failures.Add($"[fallback-parity] {CanonicalResourcesCopy} is MISSING -- the freshness of the " +
                             "generated fallback cannot be proven, and the runtime read path has nothing to read");
                return;
            }
            if (!System.IO.File.Exists(streamAbs))
            {
                failures.Add($"[fallback-parity] {CanonicalStreamingCopy} is MISSING -- the dual-copy invariant " +
                             "is broken; both canonical copies must exist and be byte-identical");
                return;
            }

            byte[] resBytes    = System.IO.File.ReadAllBytes(resAbs);
            byte[] streamBytes = System.IO.File.ReadAllBytes(streamAbs);
            string resSha      = Sha256Hex(resBytes);
            string streamSha   = Sha256Hex(streamBytes);

            // A. dual-copy.
            if (resSha != streamSha)
            {
                failures.Add($"[fallback-parity] the two canonical catalog copies are NOT byte-identical: " +
                             $"{CanonicalResourcesCopy} sha256={resSha} ({resBytes.Length} bytes) vs " +
                             $"{CanonicalStreamingCopy} sha256={streamSha} ({streamBytes.Length} bytes). " +
                             "Reconcile them, then regenerate the fallback: " + regen);
            }

            // B. STALENESS -- the one thing codegen can still get wrong.
            if (CatalogFallbackData.SourceSha256 != resSha)
            {
                failures.Add($"[fallback-parity] THE GENERATED FALLBACK IS STALE. " +
                             $"CatalogFallbackData.g.cs was generated from a catalog with " +
                             $"sha256={CatalogFallbackData.SourceSha256} ({CatalogFallbackData.SourceByteLength} bytes), " +
                             $"but {CanonicalResourcesCopy} now hashes to {resSha} ({resBytes.Length} bytes). " +
                             "Until it is regenerated, a runtime failure to READ the catalog would boot the game on " +
                             "OLD content -- old prices, old models, old footprints -- with nothing on screen saying " +
                             "so. FIX: run " + regen);
            }

            // C. the generated file was hand-edited (its banner says DO NOT EDIT).
            string embedded = CatalogFallbackData.Json;
            byte[] embeddedBytes = new System.Text.UTF8Encoding(false).GetBytes(embedded);
            string embeddedSha = Sha256Hex(embeddedBytes);
            if (embeddedSha != CatalogFallbackData.SourceSha256)
            {
                failures.Add($"[fallback-parity] CatalogFallbackData.Json does not hash to its own declared " +
                             $"SourceSha256 (embedded={embeddedSha} {embeddedBytes.Length} bytes vs declared=" +
                             $"{CatalogFallbackData.SourceSha256} {CatalogFallbackData.SourceByteLength} bytes) -- " +
                             "the GENERATED file has been hand-edited, which is exactly what its DO-NOT-EDIT banner " +
                             "forbids. Edit " + CanonicalResourcesCopy + " instead, then run " + regen);
            }

            // D. declared shape.
            if (CatalogFallbackData.SourceRowCount != entries.Count)
            {
                failures.Add($"[fallback-parity] CatalogFallbackData declares {CatalogFallbackData.SourceRowCount} " +
                             $"row(s) but the catalog on disk parses to {entries.Count} -- the generated fallback " +
                             "does not describe the current catalog. FIX: run " + regen);
            }

            // E. it actually WORKS. A fallback that compiles but parses to nothing is the
            //    failure this whole path exists to prevent, so prove it end to end against a
            //    cleared registry, exactly as the runtime would.
            var method = typeof(CatalogBootstrap).GetMethod(
                "RegisterFallback", BindingFlags.NonPublic | BindingFlags.Static);
            if (method == null)
            {
                failures.Add("[fallback-parity] CatalogBootstrap.RegisterFallback is not reflectable " +
                             "(renamed/removed) -- the JSON-failure path is UNPROVEN");
                LogFallbackVerdict(failures, log, 0, entries.Count);
                return;
            }

            // Snapshot + restore the live registry: earlier gates hydrated it and later
            // suites read it, so this gate must leave it byte-for-byte as it found it.
            var snapshot = new List<CatalogEntry>(CatalogRegistry.All());
            List<CatalogEntry> fallbackRows = null;
            int declaredRows = 0;
            try
            {
                CatalogRegistry.Clear();
                object[] args = new object[] { 0 };
                method.Invoke(null, args);
                declaredRows = (int)args[0];
                fallbackRows = new List<CatalogEntry>(CatalogRegistry.All());
            }
            catch (System.Exception ex)
            {
                var inner = ex.InnerException ?? ex;
                failures.Add($"[fallback-parity] RegisterFallback threw: {inner.GetType().Name}: {inner.Message}");
            }
            finally
            {
                CatalogRegistry.Clear();
                foreach (var e in snapshot) CatalogRegistry.Register(e);
            }

            if (fallbackRows == null)
            {
                LogFallbackVerdict(failures, log, 0, entries.Count);
                return;
            }

            if (fallbackRows.Count == 0)
            {
                failures.Add("[fallback-parity] RegisterFallback registered ZERO entries -- " +
                             "a JSON read failure would leave the build palette EMPTY. FIX: run " + regen);
                LogFallbackVerdict(failures, log, 0, entries.Count);
                return;
            }

            if (declaredRows != fallbackRows.Count)
            {
                failures.Add($"[fallback-parity] the embedded copy holds {declaredRows} row(s) but only " +
                             $"{fallbackRows.Count} registered -- row(s) were DROPPED while parsing the " +
                             "EMBEDDED catalog. See the [Flow:Catalog] 'dropped row' lines.");
            }

            // Id-for-id, both directions. The values cannot differ (same bytes, same parser),
            // so the ids are the whole remaining surface.
            var catalogIds  = new HashSet<string>();
            foreach (var e in entries)
                if (e != null && !string.IsNullOrEmpty(e.id)) catalogIds.Add(e.id);

            var fallbackIds = new HashSet<string>();
            foreach (var fb in fallbackRows)
            {
                if (fb == null || string.IsNullOrEmpty(fb.id))
                {
                    failures.Add("[fallback-parity] RegisterFallback registered a null / id-less entry");
                    continue;
                }
                fallbackIds.Add(fb.id);
            }

            foreach (var id in catalogIds)
                if (!fallbackIds.Contains(id))
                    failures.Add($"[fallback-parity] catalog row '{id}' is ABSENT from the embedded fallback -- " +
                                 "the failure path would offer a SMALLER game than the loaded one. FIX: run " + regen);

            foreach (var id in fallbackIds)
                if (!catalogIds.Contains(id))
                    failures.Add($"[fallback-parity] embedded fallback row '{id}' has NO counterpart in the catalog -- " +
                                 "the failure path would offer a structure the loaded game does not have. FIX: run " + regen);

            LogFallbackVerdict(failures, log, fallbackRows.Count, entries.Count);
        }

        // =====================================================================
        //  [card-cost-words] WO-1570 -- THE DETAIL LAYOUT'S FIELDS EXIST, AND THE
        //  RESOURCE WORDS COME FROM THE BANK, NOT FROM A LITERAL.
        //
        //  THE DEFECT THIS IS WRITTEN FROM, stated honestly: the capture
        //  (Logs/device/screens/owner-screen-20260907-004903.png) showed a Lumber Mill
        //  upgrade reading "UPGRADE . STONE 2600 GOL..." over bare numbers "2600  970".
        //  It was read as "a RETIRED resource key reached the button". It is not:
        //    * building-tiers.json:67 authors costWood 2600 / costGold 970 -- both LIVE
        //      keys (BuildingTierCatalog.cs:61-62); costFood appears ZERO times in either
        //      canonical twin, so no row is silently priced through the legacy alias at
        //      BuildingTierCatalog.cs:64;
        //      [SUPERSEDED 2026-09-10, WO-1657 item A] the costGold HALF of that reading is
        //      now history: the owner ruled WO-947 over the upgrade ladder too, so all 26
        //      tier rows (this one included) author costGold 0 in building-tiers.json v8.
        //      costWood 2600 and the Stone charge lane below are UNCHANGED. Pinned by
        //      BuildingUpgradeRegression [shortfall-named][gold-line]. The narrative above is
        //      kept verbatim because it records how the capture was mis-read at the time;
        //    * "Stone" is the LIVE player-facing word for the Food wallet slot
        //      (TownBankCapacity.DisplayName, HudKitController.cs:3024) -- WO-1416 retired
        //      FOOD and Stone reuses that persisted slot;
        //    * the tier-2 -> Stone charge lane is OWNER RULING 22 (WO-2005, 2026-09-06):
        //      "the CHARGE is right and the AUTHORING is wrong". The button names the
        //      lane the player is actually debited in.
        //  The DATA oracle that "should have caught it" already exists and already covers
        //  every structure's every upgrade step (CostBasketSeparationRegression
        //  [invariant]/[regular] :759-828 over repo.upgradeCost, plus [tiers-basket] :384
        //  over building-tiers.json). Re-walking those rows here would be the duplicated-
        //  oracle shape this repo keeps paying for, so this case does NOT.
        //
        //  What was genuinely unguarded is the LABEL SOURCE. Five surfaces spell the
        //  resource words by hand (StructureCardVM.PlacementCostWords,
        //  BuildStructureInfoPanel.cs:347-350, NPCUpgradeStation.cs:196,
        //  BuildingUpgradeVM.cs:1427, ManageScreenVM.cs:2046), and a sixth was about to be
        //  added by the fields below. This case pins the new fields to the ONE authority
        //  so the palette/info card can never disagree with the wallet chip beside it.
        // =====================================================================
        private const string CardVmRelPath = "Assets/_Modules/Village/BuildMode/StructureCardVM.cs";

        private static void CheckCardVmCostWordSource(List<string> failures, StringBuilder log)
        {
            string path = CardVmRelPath.Replace('/', System.IO.Path.DirectorySeparatorChar);
            if (!System.IO.File.Exists(path))
            {
                failures.Add("[card-cost-words] " + CardVmRelPath + " not found - the build card's cost " +
                             "projection cannot be checked, so a hand-typed resource word would go unseen.");
                return;
            }

            string src = System.IO.File.ReadAllText(path);

            // The fields a detail layout needs from the MODEL (canon 9: a View never
            // derives text). Named individually so a failure says WHICH one went missing.
            string[] required =
            {
                "public int CurrentLevel",
                "EffectiveCostParts",
                "NextTierCostParts",
                "UpgradeStats",
                "public int UpgradeSeconds",
                "UpgradeTimeText",
                "UpgradeButtonLabel"
            };
            var missing = new List<string>();
            foreach (var member in required)
                if (src.IndexOf(member, System.StringComparison.Ordinal) < 0) missing.Add(member);
            if (missing.Count > 0)
                failures.Add("[card-cost-words] StructureCardVM no longer exposes " + string.Join(", ", missing.ToArray()) +
                             " - the detail layout then has to derive it in the View, which is how the captured card " +
                             "ended up printing bare numbers with no resource words.");

            // The word source. TownBankCapacity.DisplayName is the ONE speller; a literal
            // here is the sixth copy of a word this repo has already paid for four times.
            if (src.IndexOf("TownBankCapacity.DisplayName", System.StringComparison.Ordinal) < 0)
                failures.Add("[card-cost-words] StructureCardVM's cost rows no longer read " +
                             "DeNelle.Core.Economy.TownBankCapacity.DisplayName. Every resource word must come " +
                             "from the bank's own speller - 'Stone' for the Food slot is stated in exactly one " +
                             "place (canon sec.7 / WO-1416), never re-typed on a screen.");

            // The button face stays ONE WORD. The captured defect is a label that ran past
            // its own edge because the price was concatenated into it.
            int face = src.IndexOf("UpgradeButtonLabel", System.StringComparison.Ordinal);
            if (face >= 0)
            {
                int eol = src.IndexOf('\n', face);
                string line = eol > face ? src.Substring(face, eol - face) : src.Substring(face);
                if (line.IndexOf("\"UPGRADE\"", System.StringComparison.Ordinal) < 0)
                    failures.Add("[card-cost-words] UpgradeButtonLabel is no longer the bare word \"UPGRADE\". " +
                                 "The price belongs on NextTierCostParts BESIDE the button, not inside its label - " +
                                 "concatenating it is exactly the 'UPGRADE . STONE 2600 GOL...' truncation the " +
                                 "2026-09-07 capture shows.");
            }

            log.AppendLine("  [card-cost-words] StructureCardVM exposes the detail-layout fields and spells every " +
                           "resource word through TownBankCapacity.DisplayName OK");
        }

        private static void LogFallbackVerdict(List<string> failures, StringBuilder log, int fallbackRows, int catalogRows)
        {
            bool clean = true;
            foreach (var f in failures)
                if (f.StartsWith("[fallback-parity]")) { clean = false; break; }

            if (clean)
                log.AppendLine($"  [fallback-parity] embedded fallback is FRESH: {fallbackRows} of {catalogRows} " +
                               $"catalog row(s) register from CatalogFallbackData " +
                               $"(sha256={CatalogFallbackData.SourceSha256}, v{CatalogFallbackData.SourceVersion}), " +
                               "and both canonical copies are byte-identical OK");
            else
                log.AppendLine($"  [fallback-parity] FRESHNESS FAILED -- see failures; regenerate with: " +
                               CatalogFallbackData.RegenerateCommand);
        }

        private static string Sha256Hex(byte[] bytes)
        {
            using (var sha = System.Security.Cryptography.SHA256.Create())
            {
                var hash = sha.ComputeHash(bytes);
                var sb = new StringBuilder(hash.Length * 2);
                foreach (var b in hash) sb.Append(b.ToString("x2"));
                return sb.ToString();
            }
        }

        /// <summary>Flat basket total of a cost (all four slots) -- the comparison scalar for the softcap gates.</summary>
        private static int Total(CoreCost c) => c.wood + c.food + c.iron + c.crystals;

        // =====================================================================
        //  Verdict + markers
        // =====================================================================
        private static bool Verdict(List<string> failures, StringBuilder log, out string reason)
        {
            if (failures.Count == 0)
            {
                reason = "BUILD ECONOMY OK — catalog parse/ids + dual-copy + cost sanity + tier-monotonic upgrades " +
                         "+ tower contract + placement math + 50% sell refund + BaseLayout replay (data + real factory) " +
                         "+ build-timer curve + damage-states thresholds + embedded-fallback freshness all hold";
                Debug.Log("BUILDECON_OK\n" + log);
                return true;
            }
            reason = $"BUILD ECONOMY: {failures.Count} failure(s): " + string.Join(" | ", failures);
            Debug.LogError($"BUILDECON_FAIL: {failures.Count} failure(s)\n" + log +
                           "\n - " + string.Join("\n - ", failures));
            return false;
        }
    }
}

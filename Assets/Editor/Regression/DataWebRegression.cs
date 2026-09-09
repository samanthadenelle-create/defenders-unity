// =============================================================================
// DataWebRegression — the data-catalog / web-platform sync gate.
// -----------------------------------------------------------------------------
// Assembly: DeNelle.EditorRegression. Headless, no-scene, no-PlayMode, data-only.
// Markers: DATAWEB_OK / DATAWEB_FAIL (FAIL via Debug.LogError so it lands in
// break-log.jsonl per docs/INSTRUMENTATION_STANDARD.md §4/§5).
//
// The canonical catalogs live in TWO copies (CanonicalJson.cs header + docs/
// MASTER_CATALOG/data-catalogs.md §1): Assets/Resources/Data/Canonical (WebGL-safe,
// WINS at load) and Assets/StreamingAssets/Data/Canonical (desktop fallback +
// source). "Keep them in sync" is the documented-but-UNENFORCED law — this file
// enforces it. What CoreDataHubRegression already covers is deliberately NOT
// duplicated here (top-level Resources-copy EXISTENCE + non-empty CanonicalJson
// reads); this gate adds the four gaps:
//
//   (1) DUAL-COPY DRIFT — every *.json present in BOTH roots (recursive, incl.
//       dialogue/ dungeons/ tutorial/) is content-compared (BOM-stripped,
//       CRLF-normalized). Drift = FAIL naming the file + both sizes. An EOL/BOM-
//       only difference is logged as a note, not failed (semantically identical).
//       KNOWN RED AT AUTHORING (2026-07-12): 6 files drift — weapons.json
//       (256KB streaming vs 19KB Resources!), armor.json, daily-quests.json,
//       skin.json, stake-rewards.json, tower-perks.json. Since Resources WINS at
//       runtime, the game is silently playing the SMALLER/older copy. That is the
//       exact defect class this gate exists to catch — sync the copies to go green.
//
//   (2) WEBGL-BROKEN-BY-OMISSION — two arms:
//       (2a) every "Data/Canonical/….json" path literal in RUNTIME code (all *.cs
//            under Assets/ excluding any Editor/ folder — editor code never ships)
//            must have a Resources copy on disk, because on WebGL StreamingAssets
//            File IO throws and CanonicalJson returns null (empty catalog, the
//            "loads but combat won't play" class). Constructed-path PREFIX
//            literals (no .json suffix, e.g. "Data/Canonical/dungeons") are
//            logged as skipped — they can't be statically resolved.
//       (2b) the historically StreamingAssets-only six (enemy-roles / towers /
//            walls / realm-map / heart / audio-mix — data-catalogs.md §2 flagged
//            them as "WebGL would get null") are pinned by name: each must keep
//            its Resources mirror. They were mirrored since the doc was written,
//            so this is green today and flips red if any is ever un-mirrored.
//       Plus: every StreamingAssets SUBDIRECTORY json must have a Resources
//       mirror (CoreDataHubRegression asserts top-level only).
//
//   (3) PARSE — every *.json under BOTH canonical roots (recursive) parses via
//       Newtonsoft JToken.Parse. *.jsonl is excluded by extension (the documented
//       orientation-recipes case; it lives under Resources/Data, outside the
//       canonical roots anyway). Note: CanonicalJsonIntegrityTest (NUnit, not part
//       of this headless suite) parses the StreamingAssets side only — the
//       RESOURCES side (the copy that actually wins at runtime) had no parse gate
//       until this one.
//
//   (4) VERSION — every top-level-object catalog in the StreamingAssets root must
//       carry a top-level "version" field, EXCEPT the verified versionless-by-
//       design set (canon-strings.json, en.json, garrison-recipes.json,
//       themes.json — flat string maps / recipe lists that never carried one).
//       NOTE: armor.json + weapons.json were once flagged as version-less
//       (data-catalogs.md §2 table) — verified 2026-07-12 they NOW carry
//       version, so they are asserted like every other catalog (stale doc, not a
//       stale check). A cross-copy VERSION MISMATCH is also failed by name (a
//       clearer message than the raw byte drift it always accompanies).
//
// Allowlists (each verified from code/disk, see inline comments):
//   • VersionlessByDesign — canon-strings/en/garrison-recipes/themes.
//   • NonDualCopyByDesign — skr_*, battle_*, *.sample.json (read on a
//     StreamingAssets-direct path per CoreDataHubRegression's exclusion; no
//     Resources mirror expected).
//   • LiteralAllowlist — "Data/Canonical/__does_not_exist__.json" (the negative-
//     path oracle literal in CoreCatalogRegression; editor-only anyway) and
//     castle-south-recipe (documented editor-only recipe — it actually lives at
//     Resources/Data/castle-south-recipe.json OUTSIDE the canonical root, so it
//     can never match the literal regex; kept for explicitness).
//
// Wire into the suite from DataRegression.RunAll (one line — orchestrator does it):
//   if (!DataWebRegression.Run(out var dataWebReason)) failures.Add(dataWebReason); else log.AppendLine("[data-web] " + dataWebReason);
// =============================================================================
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using DeNelle.Core.Diagnostics;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;

namespace DeNelle.Editor
{
    public static class DataWebRegression
    {
        // ── Allowlists (verified, see header) ───────────────────────────────────

        // Catalogs that never carried a top-level "version" (flat string maps /
        // plain recipe lists) — verified by parsing 2026-07-12.
        // RESOURCES-ONLY BY DESIGN (F23, 2026-08-09).
        // Declared with a REASON each, not pattern-matched: IsNonDualCopyByDesign keys off
        // filename prefixes (skr_/battle_/.sample.json), which cannot express "this one
        // file is deliberately unpaired and here is why". A reason string makes each entry
        // falsifiable later; a prefix rule silently absorbs any future file that happens to
        // match it.
        //
        // These three surfaced the moment the Resources direction was walked for the first
        // time - they had been unpaired, unversioned and unchecked while being the copy that
        // WINS at runtime. Declaring them is not silencing the gate: the gate's job is to
        // make unpaired state DECLARED rather than accidental, and an undeclared arrival
        // still hard-fails.
        private static readonly Dictionary<string, string> ResourcesOnlyByDesign =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "ad-placements.json",
              "ad covenant data; read at runtime from Resources only and pinned by " +
              "AdPlacementCovenantRegression. No StreamingAssets consumer exists." },
            { "widget-params.json",
              "HUD widget tuning params, Resources-only. NOTE: carries no version field, " +
              "which is why the StreamingAssets-only version walk never saw it." },
            { "ad-creatives.json",
              "DEBT, NOT A DESIGN: audit F61 found ZERO readers repo-wide - no .cs, editor " +
              "or runtime, references it. Declared here so the gate stays honest about the " +
              "rest, but this file is a REMOVAL CANDIDATE, not a sanctioned exception." },
        };

        private static readonly HashSet<string> VersionlessByDesign =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "canon-strings.json",
            "en.json",
            // Flat locale string maps share en.json's schema. These files are content-versioned
            // by the StringTable collection/import, not by a top-level gameplay-catalog version.
            "ar.json",
            "de.json",
            "es.json",
            "fr.json",
            "ja.json",
            "ko.json",
            "pt-BR.json",
            "ru.json",
            "zh-Hans.json",
            "garrison-recipes.json",
            "themes.json",
            // GENERATED, not authored: carries its own "generated" timestamp + "source" prefab path
            // (Blink Obsidian_UI). A hand-bumped version on a generator output is duplicated state
            // that rots on the next regenerate; the timestamp is its version. (2026-09-04)
            "widget-params.json",
        };

        // Files read on a NON-dual-copy path (StreamingAssets-direct) — same
        // exclusion CoreDataHubRegression uses; no Resources mirror expected.
        private static bool IsNonDualCopyByDesign(string fileName)
        {
            var n = fileName.ToLowerInvariant();
            return n.EndsWith(".sample.json") || n.StartsWith("skr_") || n.StartsWith("battle_");
        }

        // Path literals in code that must NOT be asserted as WebGL load surface.
        // WO-1495 2026-09-06 remove-by 2026-12-06 - path literals that exist only as negative-path
        // probe fixtures (CoreCatalogRegression's __does_not_exist__.json) and must never be
        // asserted as WebGL load surface; re-read then to confirm each fixture still exists.
        private static readonly HashSet<string> LiteralAllowlist =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            // CoreCatalogRegression's deliberate negative-path probe (editor-only,
            // but pinned here so a future runtime copy of that probe can't fail us).
            "__does_not_exist__.json",
            // Documented editor-only recipe; lives OUTSIDE the canonical root
            // (Resources/Data/castle-south-recipe.json) — explicitness only.
            "castle-south-recipe.json",
        };

        // The historically StreamingAssets-only six (data-catalogs.md §2: "WebGL
        // would get null"). Mirrored since; pinned so un-mirroring flips red.
        private static readonly string[] KnownSixMustStayMirrored =
        {
            "enemy-roles.json",
            "towers.json",
            "walls.json",
            "realm-map.json",
            "heart.json",
            "audio-mix.json",
        };

        // ── WO-747: curation-aware catalogs (ADDITIVE model) ────────────────────
        // weapons.json + armor.json are the runtime-winning Resources copies that the
        // Gear Caster curation ADDS to. The exporter is ADDITIVE (never drops): the
        // Resources copy = ALL current Resources rows UNION the curated library rows.
        // So the Resources copy is deliberately DIFFERENT from (a superset of) the
        // StreamingAssets copy — byte-identity can never be green — and it may also
        // hold authored ids that exist ONLY in Resources (the class-tier progression
        // armor + loot/vendor weapons). These two files are therefore EXEMPTED from
        // the dual-copy drift check (1) and the cross-copy version comparison (4), and
        // asserted by CheckGearCuration below (curation REACHED runtime, catalog well-
        // formed) — NOT by an exact-projection equality (which would demand drops).
        private static readonly HashSet<string> CurationAwareCatalogs =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "weapons.json",
            "armor.json",
        };

        // The blink_armor class-default ids referenced by HeroBodySwapper.DefaultArmorIdFor
        // (centurion/beasthunter/dragonic) + the SaveIntegrityRegression seed (basic1).
        // They live ONLY in the StreamingAssets library, not the current Resources copy,
        // so the additive exporter MERGES their full rows into Resources — which is what
        // fixes the HeroBodySwapper default-armor no-op (the ids now resolve at runtime).
        // KEEP IN SYNC with GearCurationExporter (single source of truth).
        public static readonly string[] ReferencedDefaultArmorIds =
        {
            "blink_armor_centurion", "blink_armor_beasthunter", "blink_armor_dragonic", "blink_armor_basic1",
        };

        // ── Entry point ─────────────────────────────────────────────────────────

        /// <summary>
        /// Proves the dual-copy canonical-JSON law holds for the web platform:
        /// no content drift between the two roots, no WebGL-broken-by-omission
        /// catalog, every file parses, version fields present where owed.
        /// Deterministic, self-contained, no scene / no PlayMode.
        /// </summary>
        public static bool Run(out string reason)
        {
            var failures = new List<string>();
            var log = new StringBuilder();
            log.AppendLine("--- DATA/WEB (canonical dual-copy sync + WebGL load surface) ---");

            string streamingRoot = Path.Combine(Application.streamingAssetsPath, "Data/Canonical");
            string resourcesRoot = Path.Combine(Application.dataPath, "Resources/Data/Canonical");

            if (!Directory.Exists(streamingRoot))
                failures.Add($"StreamingAssets canonical root missing: {streamingRoot}");
            if (!Directory.Exists(resourcesRoot))
                failures.Add($"Resources canonical root missing: {resourcesRoot}");

            if (failures.Count == 0)
            {
                CheckDualCopyDrift(streamingRoot, resourcesRoot, failures, log);
                CheckWebglOmission(streamingRoot, resourcesRoot, failures, log);
                CheckAllParse(streamingRoot, resourcesRoot, failures, log);
                CheckVersionFields(streamingRoot, resourcesRoot, failures, log);
                CheckResourcesOnlyFiles(streamingRoot, resourcesRoot, failures, log);   // F23
                CheckGearCuration(streamingRoot, resourcesRoot, failures, log);   // WO-747
            }

            if (failures.Count == 0)
            {
                Debug.Log(log.ToString() + "DATAWEB_OK");
                reason = "DATA/WEB OK — dual copies in sync, WebGL load surface complete, all canonical JSON parses, version fields present";
                return true;
            }
            reason = "data-web: " + string.Join("; ", failures);
            Debug.LogError(log.ToString() + "DATAWEB_FAIL: " + reason);
            return false;
        }

        // ── (1) Dual-copy content diff ──────────────────────────────────────────

        private static void CheckDualCopyDrift(string streamingRoot, string resourcesRoot,
                                               List<string> failures, StringBuilder log)
        {
            int compared = 0, drifted = 0;
            foreach (var sPath in CanonicalJsonFiles(streamingRoot))
            {
                string rel = RelativePath(streamingRoot, sPath);
                // WO-747: weapons.json/armor.json are a curated SUBSET of the library by
                // design (Resources != StreamingAssets on purpose) — CheckGearCuration
                // asserts them instead. Skipping keeps them out of the byte-drift check.
                if (rel.IndexOf('/') < 0 && CurationAwareCatalogs.Contains(Path.GetFileName(rel)))
                {
                    log.AppendLine($"  '{rel}' curation-aware (WO-747) — drift check delegated to CheckGearCuration");
                    continue;
                }
                string rPath = Path.Combine(resourcesRoot, rel);
                if (!File.Exists(rPath)) continue;   // existence is (2)'s / CoreDataHub's job

                compared++;
                byte[] sRaw = File.ReadAllBytes(sPath);
                byte[] rRaw = File.ReadAllBytes(rPath);
                string sNorm = Normalize(sRaw);
                string rNorm = Normalize(rRaw);

                if (!string.Equals(sNorm, rNorm, StringComparison.Ordinal))
                {
                    drifted++;
                    failures.Add($"DUAL-COPY DRIFT '{rel}': StreamingAssets ({sRaw.Length} B) != Resources ({rRaw.Length} B) — " +
                                 "Resources WINS at runtime, so the game plays the Resources copy; sync the two (the documented CanonicalJson sync rule)");
                    log.AppendLine($"  DRIFT {rel} | streaming={sRaw.Length}B resources={rRaw.Length}B");
                }
                else if (!BytesEqual(sRaw, rRaw))
                {
                    // Same content, different bytes: EOL/BOM only — semantically identical.
                    log.AppendLine($"  note: '{rel}' differs only in EOL/BOM (content identical) — not failed");
                }
            }
            log.AppendLine($"dual-copy diff: compared {compared} paired file(s), {drifted} drifted");
        }

        // ── (2) WebGL broken-by-omission ────────────────────────────────────────

        private static void CheckWebglOmission(string streamingRoot, string resourcesRoot,
                                               List<string> failures, StringBuilder log)
        {
            // (2a) Runtime call-site literal scan: every "Data/Canonical/….json"
            // literal in non-Editor code must resolve on the WebGL (Resources) surface.
            var literalRegex = new Regex("\"Data/Canonical/([A-Za-z0-9_\\-./]+)\"", RegexOptions.Compiled);
            var referenced = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            var prefixLiterals = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            int scannedFiles = 0;

            foreach (var cs in Directory.GetFiles(Application.dataPath, "*.cs", SearchOption.AllDirectories))
            {
                string norm = cs.Replace('\\', '/');
                // Editor-only code never ships to WebGL — any folder named Editor
                // (asmdef convention: Assets/Editor, <Module>/Editor) is out of scope.
                if (norm.Contains("/Editor/")) continue;
                scannedFiles++;

                string text;
                try { text = File.ReadAllText(cs); }
                catch (Exception ex)
                {
                    failures.Add($"call-site scan could not read '{norm}': {ex.GetType().Name}: {ex.Message}");
                    continue;
                }
                foreach (Match m in literalRegex.Matches(text))
                {
                    string rel = m.Groups[1].Value;
                    if (rel.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) referenced.Add(rel);
                    else prefixLiterals.Add(rel);   // constructed path — statically unresolvable
                }
            }

            foreach (var rel in referenced)
            {
                string fileName = Path.GetFileName(rel);
                if (LiteralAllowlist.Contains(fileName))
                { log.AppendLine($"  literal '{rel}' allowlisted (documented editor-only / negative probe) — skipped"); continue; }
                if (IsNonDualCopyByDesign(fileName))
                { log.AppendLine($"  literal '{rel}' non-dual-copy by design (skr_/battle_/sample) — skipped"); continue; }

                string rPath = Path.Combine(resourcesRoot, rel);
                if (!File.Exists(rPath))
                    failures.Add($"WEBGL OMISSION: runtime code loads 'Data/Canonical/{rel}' via CanonicalJson but NO Resources copy exists " +
                                 $"({rPath}) — CanonicalJson.Read returns null on WebGL (empty catalog, the 'loads but combat won't play' class)");
                else
                    log.AppendLine($"  literal '{rel}' -> Resources copy OK");
            }
            foreach (var p in prefixLiterals)
                log.AppendLine($"  prefix literal 'Data/Canonical/{p}' (path constructed at runtime) — cannot assert statically, skipped");
            log.AppendLine($"call-site scan: {scannedFiles} runtime .cs file(s), {referenced.Count} .json literal(s), {prefixLiterals.Count} prefix literal(s)");

            // (2b) The known six stay mirrored (green today; red if un-mirrored).
            foreach (var six in KnownSixMustStayMirrored)
            {
                string rPath = Path.Combine(resourcesRoot, six);
                if (!File.Exists(rPath))
                    failures.Add($"WEBGL OMISSION: '{six}' lost its Resources mirror ({rPath}) — it regresses to the documented " +
                                 "StreamingAssets-only state (null on WebGL, data-catalogs.md §2)");
                else
                    log.AppendLine($"  known-six '{six}' mirrored OK");
            }

            // Subdirectory mirrors (CoreDataHubRegression asserts TOP-LEVEL only).
            int subChecked = 0;
            foreach (var sPath in CanonicalJsonFiles(streamingRoot))
            {
                string rel = RelativePath(streamingRoot, sPath);
                if (rel.IndexOf('/') < 0) continue;   // top-level = CoreDataHub's lane
                string fileName = Path.GetFileName(rel);
                if (IsNonDualCopyByDesign(fileName)) continue;
                subChecked++;
                string rPath = Path.Combine(resourcesRoot, rel);
                if (!File.Exists(rPath))
                    failures.Add($"WEBGL OMISSION: subdirectory catalog '{rel}' has a StreamingAssets source but NO Resources mirror " +
                                 $"({rPath}) — null on WebGL (subdirs are outside CoreDataHubRegression's top-level sweep)");
            }
            log.AppendLine($"subdirectory mirror check: {subChecked} file(s)");
        }

        // ── (3) Every canonical *.json parses ───────────────────────────────────

        private static void CheckAllParse(string streamingRoot, string resourcesRoot,
                                          List<string> failures, StringBuilder log)
        {
            int parsed = 0, broken = 0;
            foreach (var root in new[] { streamingRoot, resourcesRoot })
            {
                string label = root == streamingRoot ? "StreamingAssets" : "Resources";
                foreach (var path in CanonicalJsonFiles(root))
                {
                    string rel = RelativePath(root, path);
                    parsed++;
                    try { JToken.Parse(Normalize(File.ReadAllBytes(path))); }
                    catch (Exception ex)
                    {
                        broken++;
                        failures.Add($"PARSE FAIL {label}/'{rel}': {ex.GetType().Name}: {FirstLine(ex.Message)}");
                    }
                }
            }
            log.AppendLine($"parse: {parsed} file(s) across both roots, {broken} broken (*.jsonl excluded by extension)");
        }

        // ── (4) Version-field presence + cross-copy agreement ───────────────────

        private static void CheckVersionFields(string streamingRoot, string resourcesRoot,
                                               List<string> failures, StringBuilder log)
        {
            int checkedCount = 0;
            foreach (var sPath in CanonicalJsonFiles(streamingRoot))
            {
                string rel = RelativePath(streamingRoot, sPath);
                string fileName = Path.GetFileName(rel);
                if (IsNonDualCopyByDesign(fileName)) continue;
                if (VersionlessByDesign.Contains(fileName))
                { log.AppendLine($"  '{rel}' versionless by design — skipped"); continue; }

                JObject sObj = TryParseObject(sPath);
                if (sObj == null) continue;   // parse failures already reported by (3)
                checkedCount++;

                var sVer = sObj["version"];
                if (sVer == null)
                {
                    failures.Add($"VERSION MISSING '{rel}': no top-level \"version\" field — every canonical catalog outside the " +
                                 "versionless-by-design set carries one (add it, or extend VersionlessByDesign with a reason)");
                    continue;
                }

                // WO-747: the curated catalogs may legitimately carry a DIFFERENT version
                // across copies (armor's curated Resources copy stays v2 while the full
                // library is v1) — skip the cross-copy version comparison for them. The
                // presence check above still applies to the StreamingAssets side.
                if (rel.IndexOf('/') < 0 && CurationAwareCatalogs.Contains(fileName))
                {
                    log.AppendLine($"  '{rel}' curation-aware (WO-747) — cross-copy version compare skipped (intentional divergence)");
                    continue;
                }

                // Cross-copy agreement (a clearer name for the drift it accompanies).
                string rPath = Path.Combine(resourcesRoot, rel);
                if (File.Exists(rPath))
                {
                    JObject rObj = TryParseObject(rPath);
                    var rVer = rObj != null ? rObj["version"] : null;
                    if (rObj != null && !JToken.DeepEquals(sVer, rVer))
                        failures.Add($"VERSION MISMATCH '{rel}': StreamingAssets version={sVer} vs Resources version={(rVer != null ? rVer.ToString() : "<missing>")} " +
                                     "— the copies diverged (Resources wins at runtime)");
                }
            }
            log.AppendLine($"version fields: {checkedCount} catalog(s) checked, {VersionlessByDesign.Count} allowlisted");
        }

        // ── (4b) RESOURCES-ONLY files: the side that WINS, never walked ─────────
        // Audit finding F23. CheckDualCopyDrift and CheckVersionFields both iterate
        // CanonicalJsonFiles(streamingRoot) ONLY. Every assertion about drift and about
        // version fields is therefore blind to a file that exists solely under
        // Resources/Data/Canonical - and Resources is the copy that WINS at runtime
        // (LocalJsonCatalogSource probes Resources first). The gate was walking the
        // losing side and reporting on the whole contract.
        //
        // This is the audit's headline pattern once more: an assertion that proves the
        // part that was never broken. A StreamingAssets-only file is inert; a
        // Resources-only file is what the player actually loads.
        //
        // Two distinct failures live here:
        //   * an UNDECLARED Resources-only file - it wins at runtime with no recorded
        //     reason for being unpaired, which is how a stray copy comes to shadow the
        //     canonical pair unnoticed.
        //   * a declared-by-design Resources-only file with NO version field - invisible
        //     to CheckVersionFields for its whole life (widget-params.json is exactly
        //     this today).
        private static void CheckResourcesOnlyFiles(string streamingRoot, string resourcesRoot,
                                                    List<string> failures, StringBuilder log)
        {
            using var _ = FlowTrace.Enter("DataWeb", "CheckResourcesOnlyFiles");

            int resourcesOnly = 0, versionChecked = 0;
            foreach (var rPath in CanonicalJsonFiles(resourcesRoot))
            {
                string rel = RelativePath(resourcesRoot, rPath);
                string fileName = Path.GetFileName(rel);

                string sPath = Path.Combine(streamingRoot, rel);
                if (File.Exists(sPath)) continue;   // paired - the StreamingAssets walk covers it

                resourcesOnly++;

                string why;
                bool declared = ResourcesOnlyByDesign.TryGetValue(fileName, out why);

                if (!declared && !IsNonDualCopyByDesign(fileName))
                {
                    FlowTrace.Fail("DataWeb", "undeclared Resources-only file: " + rel);
                    failures.Add($"RESOURCES-ONLY, UNDECLARED '{rel}': this file exists under Resources but NOT under " +
                                 "StreamingAssets, and is not declared in ResourcesOnlyByDesign. Resources WINS at " +
                                 "runtime, so the player loads a catalog that has no counterpart and no recorded reason " +
                                 "for being unpaired - either mirror it or declare it by design WITH A REASON.");
                    continue;
                }

                if (declared) log.AppendLine($"  '{rel}' Resources-only by design - {why}");

                // Declared unpaired is fine. Being versionless is still reported - but as a
                // NOTE on a declared file rather than a failure, because the declaration
                // already carries the reason. An UNDECLARED versionless file still fails above.
                if (VersionlessByDesign.Contains(fileName))
                { log.AppendLine($"  '{rel}' Resources-only + versionless by design - skipped"); continue; }

                JObject rObj = TryParseObject(rPath);
                if (rObj == null) continue;   // parse failures already reported by (3)
                versionChecked++;

                if (rObj["version"] == null)
                {
                    FlowTrace.Warn("DataWeb", "declared Resources-only file has no version field: " + rel);
                    log.AppendLine($"  NOTE '{rel}' has no \"version\" field - invisible to the version walk for its " +
                                   "entire life because that walk only ever visited StreamingAssets (F23). Declared, " +
                                   "so not failed; recorded so it cannot be forgotten.");
                }
            }

            FlowTrace.Step("DataWeb", "resources-only=" + resourcesOnly + " version-checked=" + versionChecked);
            log.AppendLine($"resources-only files: {resourcesOnly} found, {versionChecked} version-checked " +
                           "(this direction was unwalked before 2026-08-09 - F23)");
        }

        // ── (5) Gear curation (WO-747, ADDITIVE model) ──────────────────────────
        // Replaces the byte-drift check for weapons.json + armor.json. The exporter is
        // ADDITIVE (Resources = current-Resources UNION curated-library-rows, never
        // drops), so the gate does NOT assert exact projection equality (that would
        // demand drops of the authored Resources-only content). It asserts the two
        // things that matter:
        //   (a) CURATION REACHED RUNTIME — every included pick id (weapons + armor) +
        //       every referenced blink_armor default id is PRESENT in the Resources
        //       catalog (so the Gear Caster picks + HeroBodySwapper defaults resolve).
        //   (b) CATALOG RESOLVES — every Resources row is well-formed: a non-empty id,
        //       no duplicate ids within a catalog (a dup = ambiguous GearCatalog lookup).
        //   (c) ART RESOLVES (added 2026-08-14) — every row that DECLARES a prefabPath walks
        //       to a real asset: an Addressable address present in a group whose asset is
        //       still on disk, or a Resources.Load that returns non-null. Without this,
        //       GEAR_CURATION_OK stayed green with all 65 art-bearing weapons missing their
        //       art (Assets/Blink/ is gitignored — that IS the fresh-clone state).
        //   (d) SUBSET (added 2026-08-14) — Resources weapon ids ⊆ StreamingAssets weapon ids.
        //       Resources is a curated projection; a Resources-only id means it drifted off
        //       its source. WEAPONS ONLY — armor legitimately has 15 Resources-only ids today
        //       and is logged, not failed.
        // Emits GEAR_CURATION_OK / GEAR_CURATION_FAIL. Absent picks file = WARN + skip.
        // NOTE (authoring 2026-07-18): EXPECTED RED until GearCurationExporter is run —
        // the pre-export Resources copies (34 weapons / 20 armor, no blink) do not yet
        // hold the curated picks or the blink_armor defaults.

        private static void CheckGearCuration(string streamingRoot, string resourcesRoot,
                                              List<string> failures, StringBuilder log)
        {
            string picksPath = Path.Combine(Application.dataPath, "Editor/GearCurationPicks.json");
            if (!File.Exists(picksPath))
            {
                log.AppendLine("  GEAR_CURATION skipped — no GearCurationPicks.json (curation is opt-in editor tooling; " +
                               "the runtime curated copies are only asserted once the owner has curated + exported)");
                return;
            }

            var included = ReadIncludedPickIds(picksPath, failures, log);
            if (included == null) return;   // read failure already recorded

            // The runtime-winning Resources copies (raw id lists, nulls kept to catch empties).
            var weaponIds = ReadCatalogIds(Path.Combine(resourcesRoot, "weapons.json"), "weapons", failures, log);
            var armorIds  = ReadCatalogIds(Path.Combine(resourcesRoot, "armor.json"),  "armor",  failures, log);
            if (weaponIds == null || armorIds == null) return;   // read failure already recorded

            // (b) CATALOG RESOLVES — well-formed rows (non-empty + unique ids).
            bool resolveOk = true;
            resolveOk &= ResolveCheck("weapons.json", weaponIds, failures, log);
            resolveOk &= ResolveCheck("armor.json",  armorIds,  failures, log);

            // (a) CURATION REACHED RUNTIME — presence in the combined Resources id set.
            var resourcesAll = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var id in weaponIds) if (!string.IsNullOrEmpty(id)) resourcesAll.Add(id);
            foreach (var id in armorIds)  if (!string.IsNullOrEmpty(id)) resourcesAll.Add(id);

            var missingPicks = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var id in included) if (!resourcesAll.Contains(id)) missingPicks.Add(id);

            var missingDefaults = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var id in ReferencedDefaultArmorIds) if (!resourcesAll.Contains(id)) missingDefaults.Add(id);

            log.AppendLine($"  curation(additive): {included.Count} picked, {ReferencedDefaultArmorIds.Length} referenced-default; " +
                           $"Resources has {resourcesAll.Count} distinct id(s) — missingPicks={missingPicks.Count} missingDefaults={missingDefaults.Count}");

            if (missingPicks.Count > 0)
                failures.Add($"GEAR_CURATION_FAIL: {missingPicks.Count} curated pick id(s) NOT present in the Resources catalog " +
                             $"(curation did not reach runtime — run GearCurationExporter): {Preview(missingPicks)}");
            if (missingDefaults.Count > 0)
                failures.Add($"GEAR_CURATION_FAIL: referenced default armor id(s) NOT present in Resources " +
                             $"(HeroBodySwapper class-default armor would no-op): {Preview(missingDefaults)}");

            // (c) ART RESOLVES (2026-08-14, WEAPONS_DEEP_DIVE §3f).
            // Until today GEAR_CURATION_OK asserted ONLY that picked ids existed as rows and
            // that ids were non-empty + unique. That is compatible with all 65 art-bearing
            // curated weapons having NO ART AT ALL — which is the state on any fresh clone,
            // because Assets/Blink/ is gitignored. A green marker that survives the entire
            // weapon art library going missing is not a gate. This walks each declared
            // prefabPath to a real asset.
            bool artOk = true;
            artOk &= CheckPrefabPathsResolve("weapons.json", Path.Combine(resourcesRoot, "weapons.json"),
                                             "weapons", failures, log);
            artOk &= CheckPrefabPathsResolve("armor.json", Path.Combine(resourcesRoot, "armor.json"),
                                             "armor", failures, log);

            // (d) SUBSET ORACLE — the Resources weapons id set must be a SUBSET of the
            // StreamingAssets one. Verified true today (Resources-only weapon ids = 0), which
            // is what makes it assertable: Resources is a curated PROJECTION of the library,
            // so an id that exists only in Resources means the projection was hand-edited
            // away from its source (or a landmine re-inflated one copy and not the other).
            // ⚠ WEAPONS ONLY, DELIBERATELY. armor.json does NOT satisfy this today — it has
            // 15 Resources-only ids (armor_knight_*, the authored class defaults). Asserting
            // armor here would ship a check that is red on arrival, so armor's Resources-only
            // count is LOGGED as information and not failed. If armor is ever reconciled to a
            // pure projection, promote the log line to a failure.
            bool subsetOk = CheckResourcesIsSubsetOfStreaming("weapons.json", "weapons",
                                                              resourcesRoot, streamingRoot,
                                                              failures, log, assertIt: true);
            CheckResourcesIsSubsetOfStreaming("armor.json", "armor",
                                              resourcesRoot, streamingRoot,
                                              failures, log, assertIt: false);

            if (resolveOk && artOk && subsetOk && missingPicks.Count == 0 && missingDefaults.Count == 0)
                log.AppendLine($"GEAR_CURATION_OK — all {included.Count} curated pick id(s) + {ReferencedDefaultArmorIds.Length} referenced " +
                               "default armor id(s) present in the Resources catalog; every Resources row resolves (non-empty, unique id); " +
                               "every declared prefabPath resolves to a real asset (Addressable entry with an on-disk asset, or a loadable " +
                               "Resources prefab); Resources weapon ids are a subset of StreamingAssets");
        }

        /// <summary>
        /// (c) Every row that DECLARES a prefabPath must resolve to a real asset:
        ///   • Addressable rows (loadVia=="addressable", or a "gear/" address) — the address must
        ///     exist as an AddressableAssetEntry AND that entry's asset must still be on disk.
        ///     A dangling entry (the .asset file keeps the GUID after the source folder is gone)
        ///     is treated as MISSING, which is the whole point: gitignored art must go red.
        ///   • Resources rows — Resources.Load&lt;GameObject&gt; must return non-null.
        /// Rows with NO prefabPath are skipped and counted (they are the designed-but-unarted
        /// half; that is a design gap tracked by WO-500, not a broken reference).
        /// </summary>
        private static bool CheckPrefabPathsResolve(string label, string resourcesPath, string arrayKey,
                                                    List<string> failures, StringBuilder log)
        {
            if (!File.Exists(resourcesPath)) return true;   // absence already failed by ReadCatalogIds
            JObject robj = TryParseObject(resourcesPath);
            var arr = robj?[arrayKey] as JArray;
            if (arr == null) return true;

            AddressableAssetSettings settings = AddressableAssetSettingsDefaultObject.Settings;
            var addresses = new HashSet<string>(StringComparer.Ordinal);
            var danglingAddresses = new HashSet<string>(StringComparer.Ordinal);
            if (settings != null)
            {
                foreach (var group in settings.groups)
                {
                    if (group == null) continue;
                    foreach (var entry in group.entries)
                    {
                        if (entry == null || string.IsNullOrEmpty(entry.address)) continue;
                        addresses.Add(entry.address);
                        string assetPath = AssetDatabase.GUIDToAssetPath(entry.guid);
                        if (string.IsNullOrEmpty(assetPath) ||
                            (!File.Exists(assetPath) && !Directory.Exists(assetPath)))
                            danglingAddresses.Add(entry.address);
                    }
                }
            }
            else
            {
                log.AppendLine($"  art[{label}]: AddressableAssetSettingsDefaultObject.Settings == null — " +
                               "Addressable rows cannot be resolved, skipped (Resources rows still checked)");
            }

            int noPath = 0, addrOk = 0, resOk = 0, skippedAddr = 0;
            var missing = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var tok in arr)
            {
                var row = tok as JObject;
                if (row == null) continue;
                string id = (string)row["id"] ?? "<no id>";
                string prefabPath = (string)row["prefabPath"];
                if (string.IsNullOrEmpty(prefabPath)) { noPath++; continue; }

                string loadVia = (string)row["loadVia"];
                bool viaAddressable =
                    (!string.IsNullOrEmpty(loadVia) && loadVia.Equals("addressable", StringComparison.OrdinalIgnoreCase)) ||
                    prefabPath.StartsWith("gear/", StringComparison.OrdinalIgnoreCase);

                if (viaAddressable)
                {
                    if (settings == null) { skippedAddr++; continue; }
                    if (!addresses.Contains(prefabPath))
                        missing.Add($"{id} -> address '{prefabPath}' NOT in any Addressable group");
                    else if (danglingAddresses.Contains(prefabPath))
                        missing.Add($"{id} -> address '{prefabPath}' is a DANGLING entry (asset not on disk — gitignored/moved art)");
                    else addrOk++;
                }
                else
                {
                    if (Resources.Load<GameObject>(prefabPath) == null)
                        missing.Add($"{id} -> Resources.Load('{prefabPath}') returned null");
                    else resOk++;
                }
            }

            log.AppendLine($"  art[{label}]: {addrOk} addressable OK, {resOk} Resources OK, {noPath} row(s) declare no prefabPath, " +
                           $"{skippedAddr} addressable row(s) skipped, {missing.Count} UNRESOLVABLE");

            if (missing.Count > 0)
            {
                failures.Add($"GEAR_CURATION_FAIL: {missing.Count} curated row(s) have an unresolvable prefabPath in '{label}' " +
                             $"(the weapon has NO ART — the player is handed a generic sword or a grey primitive, and nothing else " +
                             $"reports it): {Preview(missing)}");
                return false;
            }
            return true;
        }

        /// <summary>
        /// (d) Resources ids ⊆ StreamingAssets ids. Resources is a curated PROJECTION of the
        /// StreamingAssets library, so a Resources-only id means the projection drifted off its
        /// source. Failed only when <paramref name="assertIt"/> (see the armor exemption above).
        /// </summary>
        private static bool CheckResourcesIsSubsetOfStreaming(string label, string arrayKey,
                                                              string resourcesRoot, string streamingRoot,
                                                              List<string> failures, StringBuilder log,
                                                              bool assertIt)
        {
            string rPath = Path.Combine(resourcesRoot, label);
            string sPath = Path.Combine(streamingRoot, label);
            if (!File.Exists(rPath) || !File.Exists(sPath))
            {
                log.AppendLine("  subset[" + label + "]: " + DeNelle.Editor.Regression.RegressionOutcome.PartialSkip(
                    "subset " + label, $"one copy missing (r={File.Exists(rPath)} s={File.Exists(sPath)}) - subset NOT compared"));
                return true;
            }

            var rArr = TryParseObject(rPath)?[arrayKey] as JArray;
            var sArr = TryParseObject(sPath)?[arrayKey] as JArray;
            if (rArr == null || sArr == null)
            {
                log.AppendLine("  subset[" + label + "]: " + DeNelle.Editor.Regression.RegressionOutcome.PartialSkip(
                    "subset " + label, "'" + arrayKey + "' array absent in one copy - subset NOT compared"));
                return true;
            }

            var streamingIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var tok in sArr)
            {
                string sid = (tok as JObject)?["id"]?.ToString();
                if (!string.IsNullOrEmpty(sid)) streamingIds.Add(sid);
            }

            var resourcesOnly = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            int rCount = 0;
            foreach (var tok in rArr)
            {
                string rid = (tok as JObject)?["id"]?.ToString();
                if (string.IsNullOrEmpty(rid)) continue;
                rCount++;
                if (!streamingIds.Contains(rid)) resourcesOnly.Add(rid);
            }

            log.AppendLine($"  subset[{label}]: {rCount} Resources id(s) vs {streamingIds.Count} StreamingAssets id(s) — " +
                           $"Resources-only={resourcesOnly.Count}{(assertIt ? "" : " (INFORMATIONAL — not asserted, see armor exemption)")}");

            if (resourcesOnly.Count > 0 && assertIt)
            {
                failures.Add($"GEAR_CURATION_FAIL: '{label}' Resources copy holds {resourcesOnly.Count} id(s) NOT in StreamingAssets " +
                             $"— the curated projection drifted off its library source: {Preview(resourcesOnly)}");
                return false;
            }
            return true;
        }

        /// <summary>Asserts one Resources catalog is well-formed: no empty ids, no duplicate ids
        /// (a dup makes GearCatalog's id lookup ambiguous). Returns true when clean.</summary>
        private static bool ResolveCheck(string label, List<string> ids, List<string> failures, StringBuilder log)
        {
            int empties = 0;
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var dups  = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var id in ids)
            {
                if (string.IsNullOrEmpty(id)) { empties++; continue; }
                if (!seen.Add(id)) dups.Add(id);
            }
            bool ok = true;
            if (empties > 0)
            {
                ok = false;
                failures.Add($"GEAR_CURATION_FAIL: '{label}' has {empties} row(s) with an empty/missing id — will not resolve at runtime");
            }
            if (dups.Count > 0)
            {
                ok = false;
                failures.Add($"GEAR_CURATION_FAIL: '{label}' has {dups.Count} duplicate id(s) (ambiguous GearCatalog lookup): {Preview(dups)}");
            }
            log.AppendLine($"  resolve[{label}]: {ids.Count} row(s), {empties} empty id(s), {dups.Count} duplicate id(s)");
            return ok;
        }

        /// <summary>Raw id list from a Resources catalog array (nulls kept so empties are visible
        /// to ResolveCheck). Returns null on a missing/unparseable file (failure already recorded).</summary>
        private static List<string> ReadCatalogIds(string resourcesPath, string arrayKey,
                                                   List<string> failures, StringBuilder log)
        {
            if (!File.Exists(resourcesPath))
            {
                failures.Add($"GEAR_CURATION_FAIL: Resources '{Path.GetFileName(resourcesPath)}' missing ({resourcesPath}) — run GearCurationExporter");
                return null;
            }
            JObject robj = TryParseObject(resourcesPath);
            if (robj == null)
            {
                failures.Add($"GEAR_CURATION_FAIL: Resources '{Path.GetFileName(resourcesPath)}' did not parse ({resourcesPath})");
                return null;
            }
            var ids = new List<string>();
            var arr = robj[arrayKey] as JArray;
            if (arr != null)
                foreach (var tok in arr)
                {
                    var row = tok as JObject;
                    ids.Add(row != null ? (string)row["id"] : null);
                }
            return ids;
        }

        private static List<string> ReadIncludedPickIds(string picksPath, List<string> failures, StringBuilder log)
        {
            JObject obj = TryParseObject(picksPath);
            if (obj == null)
            {
                failures.Add($"GEAR_CURATION_FAIL: GearCurationPicks.json present but did not parse ({picksPath})");
                return null;
            }
            var ids = new List<string>();
            var arr = obj["picks"] as JArray;
            if (arr != null)
                foreach (var tok in arr)
                {
                    var p = tok as JObject;
                    if (p == null) continue;
                    bool included = p["included"] != null && p["included"].Type == JTokenType.Boolean && (bool)p["included"];
                    string id = (string)p["id"];
                    if (included && !string.IsNullOrEmpty(id)) ids.Add(id);
                }
            log.AppendLine($"  curation: {ids.Count} picked id(s) included:true");
            return ids;
        }

        private static string Preview(IEnumerable<string> ids)
        {
            var list = new List<string>(ids);
            const int cap = 12;
            if (list.Count <= cap) return string.Join(", ", list);
            return string.Join(", ", list.GetRange(0, cap)) + $", ... (+{list.Count - cap} more)";
        }

        // ── Helpers ─────────────────────────────────────────────────────────────

        // Recursive *.json under a canonical root. Explicit extension filter so a
        // *.jsonl (orientation-recipes class) can never slip in via pattern quirks.
        private static IEnumerable<string> CanonicalJsonFiles(string root)
        {
            foreach (var path in Directory.GetFiles(root, "*.json", SearchOption.AllDirectories))
                if (path.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                    yield return path;
        }

        private static string RelativePath(string root, string fullPath)
        {
            string rel = fullPath.Substring(root.Length).TrimStart('\\', '/');
            return rel.Replace('\\', '/');
        }

        // UTF-8 decode with BOM strip + CRLF -> LF: the semantic content of the file.
        private static string Normalize(byte[] raw)
        {
            int offset = (raw.Length >= 3 && raw[0] == 0xEF && raw[1] == 0xBB && raw[2] == 0xBF) ? 3 : 0;
            string text = Encoding.UTF8.GetString(raw, offset, raw.Length - offset);
            return text.Replace("\r\n", "\n");
        }

        private static bool BytesEqual(byte[] a, byte[] b)
        {
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
            return true;
        }

        private static JObject TryParseObject(string path)
        {
            try { return JToken.Parse(Normalize(File.ReadAllBytes(path))) as JObject; }
            catch { return null; }   // (3) already reported the parse failure with detail
        }

        private static string FirstLine(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            int nl = s.IndexOf('\n');
            return nl < 0 ? s : s.Substring(0, nl).TrimEnd('\r');
        }
    }
}

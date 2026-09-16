// =============================================================================
// CatalogBootstrap â€” populates the CatalogRegistry at startup (WO-148 P0 fix).
// -----------------------------------------------------------------------------
// Assembly: DeNelle.Village   Namespace: DeNelle.Village
//
// THE P0: CatalogRegistry (DeNelle.Core.Catalog) had ZERO Register() callers â€”
// it was empty at runtime, so OfType()/Get() returned nothing and the catalog
// data path was unproven. This registrar fills it, so StructureFactory + future
// build-mode UI have real "buckets" to read.
//
// DATA-DRIVEN (Build Mode S3): the catalog content is no longer hardcoded C#.
// It is loaded from a canonical JSON row-set â€”
//   Assets/StreamingAssets/Data/Canonical/structures-catalog.json   (source)
//   Assets/Resources/Data/Canonical/structures-catalog.json         (WebGL copy, WINS)
// â€” through DeNelle.Core.CanonicalJson (Resources.Load first, WebGL-safe; the
// proven pattern CosmeticCatalog / PetCatalog / Theme already use). Adding a
// wall / mine / gate / tower is now a JSON row, not a code change.
//
// A fallback registers ONLY if the JSON fails to load/parse, so the build palette
// is never empty. It is now the CODEGENERATED EMBEDDED COPY of the same catalog
// (CatalogFallbackData.Json) -- see the banner on RegisterFallback. ⚠ It was
// "byte-for-byte" until WO-1755 (2026-09-15), which strips the '_'-prefixed
// authoring notes at emission; every row and value is still the file's.
//
// WO-1137 PART TWO (owner ruling 2026-08-23) - CODEGEN THE FALLBACK.
// RegisterFallback used to be ~190 lines of hand-written C# object initializers
// mirroring THREE of the catalog's 28 rows, field for field. Two things were wrong
// with that and only one of them was gated:
//   * it could DRIFT (footprint 2.5 vs the catalog's 1.75; a visualPrefabPath
//     pointing at PatriciaLight art DELETED 2026-06-09) -- a regression gate caught
//     drift only AFTER someone authored it; and
//   * even drift-free it was a 3-of-28 palette, i.e. the failure path shipped a
//     DIFFERENT, much smaller game than the authored content, silently.
// Both are gone: the fallback is the catalog itself, embedded as a string constant
// by DeNelle.Editor.CatalogFallbackGenerator and parsed through the SAME method the
// file path uses (ParseAndRegister). Drift is not gated, it is impossible -- and the
// failure path now ships all 28 rows.
//
// WHY A STRING CONSTANT AND NOT EMITTED OBJECT INITIALIZERS: emitted initializers
// would have to track every RepoProps schema change forever, so a field added to
// RepoProps tomorrow silently stops being mirrored -- exactly the fragility being
// removed. A constant is schema-agnostic. And because it is compiled INTO the
// assembly it survives the only failure class the fallback has ever existed for:
// the catalog file could not be READ (missing Resources entry, unresolvable
// StreamingAssets path, truncated file, WebGL fetch that never lands).
//
// WO-1137 PART ONE (2026-08-21) - INSTRUMENTATION. This file is no longer blind:
//   * success is judged on the ROW COUNT (registered vs rows in the parsed file),
//     not on `loaded > 0` -- one good row out of 28 used to read as a healthy boot;
//   * every previously-silent path (dropped row, defaulted RepoProps, empty read,
//     empty parse) now names the id and the reason via FlowTrace;
//   * the fallback's own announcement is FlowTrace.Fail, not a Debug.LogWarning
//     that neither break-log.jsonl nor the Flow tail keeps;
//   * every boot emits "CATALOG BOOT COUNT registered=<n> sourceRows=<n> path=<json|
//     fallback>", so a 28-row boot is finally distinguishable from a 3- or 1-row one.
// The FlowTrace calls are PERMANENT (CLAUDE.md 12): flag them off if a system ever
// goes quiet, never delete them.
//
// Pattern mirrors WaveSystemBridgeBootstrap / AudioBootstrap: a
// [RuntimeInitializeOnLoadMethod] that Clear()s then registers, guarded so a
// domain-reload-off second Play re-registers cleanly. behaviorId strings resolve
// to Village components in StructureFactory.AttachBehavior (the Core/Village
// boundary â€” a switch, no reflection).
// =============================================================================

using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using UnityEngine;
using DeNelle.Core;
using DeNelle.Core.Catalog;
using DeNelle.Core.Combat;
using DeNelle.Core.Diagnostics; // FlowTrace - WO-1137 instrumentation (CLAUDE.md 12). PERMANENT.

namespace DeNelle.Village
{
    /// <summary>
    /// Registers the build-mode catalog entries into <see cref="CatalogRegistry"/>
    /// at startup by LOADING structures-catalog.json (data-driven). Idempotent
    /// across play sessions (Clear-then-register), so it survives domain-reload-off
    /// like the other bootstrappers. Falls back to an EMBEDDED copy of that same file if the
    /// JSON cannot be loaded, so the palette is never empty â€” that fallback is the
    /// CODEGENERATED embedded copy of the same catalog, authoring notes stripped (WO-1755;
    /// see RegisterFallback).
    /// </summary>
    public static class CatalogBootstrap
    {
        /// <summary>StreamingAssets-relative path of the catalog JSON (CanonicalJson resolves Resources first).</summary>
        private const string CatalogRelativePath = "Data/Canonical/structures-catalog.json";

        /// <summary>Parsed root of structures-catalog.json.</summary>
        [System.Serializable]
        private sealed class CatalogFile
        {
            [JsonProperty("version")] public int Version;
            [JsonProperty("entries")] public List<CatalogEntry> Entries = new List<CatalogEntry>();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Register()
        {
            // Clear first so a domain-reload-off second Play doesn't double-register.
            CatalogRegistry.Clear();

            // WO-1137 INSTRUMENTATION. `loaded > 0` USED TO BE THE SUCCESS TEST, and that is the
            // real defect behind this ticket: LoadFromJson silently `continue`s past any rejected
            // row, so ONE good row out of 28 returned 1, `loaded > 0` was true, the fallback never
            // fired, and the cheerful "data-driven path is live" log printed over a 27-row content
            // hole. A failure path that prints success text. The test is now the ROW COUNT: rows
            // registered vs rows present in the parsed source file. A shortfall is FlowTrace.Fail,
            // which is error severity AND "[Flow:"-prefixed, so it survives BOTH capture filters
            // (BreakCaptureHarness.cs:250 keeps only Error/Exception/Assert; :686-688 keeps only
            // lines containing "[Flow:"). A bare Debug.LogWarning reaches NEITHER artifact -- it is
            // functionally silent on a device, which is how a wrong-game boot stayed invisible.
            // No behaviour changes here: the same rows load and the same fallback fires. Only
            // visibility changes. This instrumentation is PERMANENT (CLAUDE.md 12) -- never
            // #if-gate it out, never strip it.
            int rowsInFile;
            int loaded = LoadFromJson(out rowsInFile);
            int registered = CatalogRegistry.Count;
            if (loaded > 0)
            {
                if (rowsInFile > loaded)
                {
                    FlowTrace.Fail("Catalog",
                        $"CATALOG SHORTFALL: registered {loaded} of {rowsInFile} row(s) from " +
                        $"{CatalogRelativePath} -- {rowsInFile - loaded} row(s) were DROPPED. The " +
                        "build palette is INCOMPLETE: the player gets a smaller game than the " +
                        "authored content with nothing on screen saying so. See the preceding " +
                        "[Flow:Catalog] 'dropped row' lines for the id and reason of each.");
                }
                else
                {
                    FlowTrace.Step("Catalog",
                        $"catalog load OK: registered {loaded} of {rowsInFile} row(s) from " +
                        $"{CatalogRelativePath} -- data-driven path is live.");
                }

                // Accepted-but-not-present means two rows shared an id and the later one REPLACED
                // the earlier (CatalogRegistry.Register logs that replacement). Content loss the
                // count-vs-count check above cannot see, so it gets its own assertion.
                if (registered != loaded)
                {
                    FlowTrace.Fail("Catalog",
                        $"CATALOG ID COLLISION: {loaded} row(s) were accepted but CatalogRegistry " +
                        $"holds {registered} -- {loaded - registered} row(s) overwrote an earlier " +
                        "id. The [Flow:Catalog] REPLACED lines above name the duplicates.");
                }

                // The one line that makes a 28-row boot distinguishable from a 3-row or a 1-row
                // boot. NOTHING could tell them apart before WO-1137 -- that is the heart of the
                // ticket. Emitted on EVERY boot, on BOTH paths, in a fixed greppable shape.
                FlowTrace.Step("Catalog",
                    $"CATALOG BOOT COUNT registered={registered} sourceRows={rowsInFile} path=json");

                // The legacy "data-driven path is live" line is now gated on a COMPLETE load, so it
                // can never again narrate a partial one as success.
                if (rowsInFile <= loaded)
                {
                Debug.Log($"[CatalogBootstrap] Registered {registered} of {rowsInFile} catalog " +
                          $"entrie(s) from structures-catalog.json â€” data-driven path is live.");
                }

                // Owner-dialed poses saved by the Orient tool overlay the shipped data
                // (local wins â€” the 2026-07-08 "save locally" directive; gear-offsets pattern).
                StructureOrientationLocalStore.ApplyAll();
                return;
            }

            // JSON missing / empty / unparseable â€” keep the palette alive from the EMBEDDED
            // copy of that same catalog, so the build path never dead-ends AND never silently
            // becomes a different, smaller game.
            int embeddedRows;
            int embeddedLoaded = RegisterFallback(out embeddedRows);
            Debug.LogWarning($"[CatalogBootstrap] structures-catalog.json unavailable â€” " +
                             $"registered {CatalogRegistry.Count} of {embeddedRows} EMBEDDED fallback entrie(s).");

            // WO-1137: booting off the embedded copy is NOT a warning-level event, even now that
            // the copy is complete. The Debug.LogWarning above announces it into a severity that
            // NEITHER capture artifact keeps (BreakCaptureHarness.cs:250 error-only; :686-688
            // "[Flow:"-only), so on a device it shipped announced-but-unheard. FlowTrace.Fail is
            // error severity AND "[Flow:"-prefixed, so it survives both.
            // It stays a FAIL and not a Warn on purpose: the CONTENT is now right, but the
            // authored catalog still FAILED TO LOAD and the player is on a path nobody chose --
            // an embedded snapshot that is only as current as the last codegen. A read failure
            // that stops being an error stops being fixed. PERMANENT (CLAUDE.md 12) -- never strip.
            FlowTrace.Fail("Catalog",
                $"FALLBACK PALETTE ACTIVE: {CatalogRelativePath} yielded 0 rows -- registered " +
                $"{CatalogRegistry.Count} of {embeddedRows} row(s) from the EMBEDDED catalog copy " +
                $"(CatalogFallbackData, generated from {CatalogFallbackData.SourcePath}, " +
                $"sha256={CatalogFallbackData.SourceSha256}) INSTEAD OF the authored catalog file. " +
                "The palette is complete, so this is no longer a different game -- but the content " +
                "the player sees is a COMPILED-IN SNAPSHOT, not the shipped data, and nothing on " +
                "screen says the file failed to load. The preceding [Flow:Catalog] read/parse/empty " +
                "lines name the cause. Fix the read; do not treat the embedded copy as the fix.");
            if (embeddedLoaded < embeddedRows)
            {
                FlowTrace.Fail("Catalog",
                    $"EMBEDDED FALLBACK SHORTFALL: registered {embeddedLoaded} of {embeddedRows} " +
                    "embedded row(s) -- rows were DROPPED from the compiled-in copy too. See the " +
                    "preceding [Flow:Catalog] 'dropped row' lines.");
            }

            // Same fixed, greppable shape as the json path, so a 28-row fallback boot is
            // distinguishable from a 3-row or a 1-row one. sourceRows reports the EMBEDDED row
            // count: it used to be hardcoded 0, which was true of the file and a lie about the
            // palette. path=fallback still separates the two paths.
            FlowTrace.Step("Catalog",
                $"CATALOG BOOT COUNT registered={CatalogRegistry.Count} sourceRows={embeddedRows} path=fallback");
        }

        /// <summary>
        /// Reads structures-catalog.json via the WebGL-safe loader, parses each row
        /// into a <see cref="CatalogEntry"/>, and registers it. Returns the number of
        /// entries registered (0 = load/parse failure, caller falls back).
        /// </summary>
        /// <param name="rowsInFile">
        /// WO-1137: rows PRESENT in the parsed source file, so the caller can assert the row COUNT
        /// instead of `loaded > 0`. 0 when the file could not be read or parsed at all.
        /// </param>
        private static int LoadFromJson(out int rowsInFile)
        {
            rowsInFile = 0;

            string json;
            try
            {
                json = CanonicalJson.Read(CatalogRelativePath);
            }
            catch (System.Exception ex)
            {
                // WO-1137: was Debug.LogWarning -- a severity neither capture artifact keeps, so a
                // hard read failure was invisible on device. Fail is error + "[Flow:"-prefixed.
                FlowTrace.Fail("Catalog",
                    $"catalog READ THREW for {CatalogRelativePath}: {ex.GetType().Name}: {ex.Message} " +
                    "-- zero rows load and the hardcoded fallback palette will take over.");
                Debug.LogWarning($"[CatalogBootstrap] read of {CatalogRelativePath} threw: {ex.Message}");
                return 0;
            }

            if (string.IsNullOrEmpty(json))
            {
                // WO-1137: this path was COMPLETELY silent -- an empty resolve returned 0 with no
                // log of any kind, and the fallback then took over unexplained.
                FlowTrace.Fail("Catalog",
                    $"catalog READ EMPTY for {CatalogRelativePath}: CanonicalJson.Read returned " +
                    "null/empty (neither the Resources copy nor the StreamingAssets copy resolved) " +
                    "-- zero rows load and the hardcoded fallback palette will take over.");
                return 0;
            }

            return ParseAndRegister(json, CatalogRelativePath, out rowsInFile);
        }

        /// <summary>
        /// THE ONE parse + row-validate + register path, shared by BOTH the file path
        /// (<see cref="LoadFromJson"/>) and the embedded fallback (<see cref="RegisterFallback"/>).
        ///
        /// <para>WO-1137: this method being SHARED is the point of the ticket. The fallback used to
        /// be a separate hand-written construction path, so the two could disagree about what a row
        /// is -- and did. Now a dropped row, a null 'repo' and a null 'repo.placement' are handled
        /// identically, logged identically, and counted identically on both paths. Do NOT duplicate
        /// this loop for a third caller; pass a different <paramref name="origin"/> instead.</para>
        /// </summary>
        /// <param name="json">The raw catalog JSON (from the file, or from the embedded constant).</param>
        /// <param name="origin">Human-readable source name, quoted in every diagnostic.</param>
        /// <param name="rowsInFile">Rows PRESENT in the parsed source. 0 when it could not be parsed.</param>
        /// <returns>Rows actually registered.</returns>
        private static int ParseAndRegister(string json, string origin, out int rowsInFile)
        {
            rowsInFile = 0;

            CatalogFile file;
            try
            {
                // StringEnumConverter so "Tower"/"Ground"/"Aether"/etc. parse to the
                // Core enums; null-handling so a sparse row keeps RepoProps defaults.
                var settings = new JsonSerializerSettings
                {
                    Converters = { new StringEnumConverter() },
                    NullValueHandling = NullValueHandling.Ignore,
                    MissingMemberHandling = MissingMemberHandling.Ignore,
                };
                file = JsonConvert.DeserializeObject<CatalogFile>(json, settings);
            }
            catch (System.Exception ex)
            {
                // WO-1137: was Debug.LogWarning -- invisible to both capture artifacts.
                FlowTrace.Fail("Catalog",
                    $"catalog PARSE FAILED for {origin}: {ex.GetType().Name}: {ex.Message} " +
                    $"(json length={json.Length}) -- zero rows load from this source.");
                Debug.LogWarning($"[CatalogBootstrap] parse of {origin} failed: {ex.Message}");
                return 0;
            }

            if (file == null || file.Entries == null || file.Entries.Count == 0)
            {
                // WO-1137: another path that was COMPLETELY silent. A structurally valid but
                // entry-less catalog looked exactly like a missing file, with no log either way.
                FlowTrace.Fail("Catalog",
                    $"catalog PARSED BUT EMPTY for {origin}: " +
                    $"file={(file == null ? "null" : "ok")} " +
                    $"entries={(file == null || file.Entries == null ? "null" : file.Entries.Count.ToString())} " +
                    "-- zero rows load from this source.");
                return 0;
            }

            rowsInFile = file.Entries.Count;

            int count = 0;
            int index = -1;
            foreach (var entry in file.Entries)
            {
                index++;

                // WO-1137: this `continue` was PURE SILENT CONTENT LOSS -- a dropped catalog row
                // emitted no log of any kind, and because the caller only tested `loaded > 0` the
                // drop never surfaced anywhere. Name the row and the reason.
                if (entry == null || string.IsNullOrEmpty(entry.id))
                {
                    FlowTrace.Warn("Catalog",
                        $"dropped row at index {index} of {rowsInFile} in {origin}: " +
                        (entry == null
                            ? "the row deserialized to null (malformed JSON object)."
                            : "the row has a null/empty 'id', which is the registry key.") +
                        " This row's content is NOT in the build palette.");
                    continue;
                }

                // WO-1137: a malformed row silently degraded to an ALL-DEFAULT placeholder -- zero
                // cost, zero range, zero damage, footprint 0 -- and was then registered as a real
                // catalog entry. Unbuildable/unbalanced content that looked like a successful load.
                if (entry.repo == null)
                {
                    FlowTrace.Warn("Catalog",
                        $"row id='{entry.id}' (index {index} of {rowsInFile}) has NO 'repo' block: " +
                        "substituting an all-default RepoProps placeholder (cost 0, range 0, " +
                        "damage 0, footprint 0). It registers as a real entry but carries no " +
                        "authored values -- fix the catalog row.");
                    entry.repo = new RepoProps();
                }
                if (entry.repo.placement == null)
                {
                    FlowTrace.Warn("Catalog",
                        $"row id='{entry.id}' (index {index} of {rowsInFile}) has NO " +
                        "'repo.placement' block: substituting default PlacementRules (footprint 0, " +
                        "no surface constraint), so placement validation for this row is inert.");
                    entry.repo.placement = new PlacementRules();
                }

                CatalogRegistry.Register(entry);
                count++;
            }
            return count;
        }

        // === Codegenerated fallback -- the WHOLE catalog, embedded ==================
        //
        // WO-1137, owner ruling 2026-08-23: CODEGEN THE FALLBACK (not delete it).
        //
        // WHAT USED TO BE HERE: ~190 lines of hand-written C# object initializers that
        // mirrored THREE of the catalog's 28 rows, field for field, and had to be edited in
        // the same breath as every structures-catalog.json edit. Two defects, and only one
        // of them was gated:
        //   1. DRIFT. Every value that diverged from its catalog counterpart silently
        //      shipped different content on the failure path -- a different model, a
        //      different price, a different footprint. Historic catches: footprint 2.5 vs
        //      the catalog's 1.75 (commit 0ac59581), and visualPrefabPath
        //      "PatriciaLight/tower2", art from the Defend-the-Tower module DELETED on
        //      2026-06-09. The parity gate found drift only AFTER a human authored it.
        //   2. INCOMPLETENESS, which NOTHING gated. Three rows out of twenty-eight meant
        //      the failure path shipped a fundamentally smaller game, silently, with
        //      nothing on screen saying so.
        //
        // WHAT IS HERE NOW: the catalog itself. CatalogFallbackData.Json is the canonical
        // structures-catalog.json embedded as a string constant, emitted by
        // DeNelle.Editor.CatalogFallbackGenerator, and parsed through ParseAndRegister --
        // ⚠ WO-1755 (2026-09-15): the embedding was BYTE-FOR-BYTE until that date. It now
        // omits every '_'-prefixed AUTHORING NOTE, because the '_quarryNote' shipped a
        // store-rail brand name into the Google Play AAB as a compiled literal (WO-1740 RCA,
        // global-metadata.dat offset 1,671,082) where the Play neutral sweep -- which
        // rewrites FILES -- could never reach it. No row, id, cost or player-facing key is
        // affected; the notes are addressed to whoever edits the JSON and are read by no
        // runtime type. --
        // the SAME method the file path uses. Dropped rows, a null 'repo' and a null
        // 'repo.placement' are therefore handled, logged and counted identically on both
        // paths, because they ARE the same code.
        //
        // Drift is no longer GATED, it is IMPOSSIBLE: there is nothing here to author.
        // BuildEconomyRegression's "[fallback-parity]" gate accordingly became a FRESHNESS
        // gate -- it proves the embedded SHA-256 still matches the catalog on disk, and
        // names the regeneration command when it does not.
        //
        // NEVER hand-write a row here again, and NEVER hand-edit CatalogFallbackData.g.cs.
        // Edit Assets/Resources/Data/Canonical/structures-catalog.json (and its
        // StreamingAssets twin, which must stay byte-identical) and re-run the generator:
        //   powershell -NoProfile -File .\run-unity-method.ps1 `
        //     -Method DeNelle.Editor.CatalogFallbackGenerator.Generate `
        //     -LogName catalog-fallback-gen.log -ExpectMarker CATALOG_FALLBACK_GEN_OK
        //
        // WHY A STRING CONSTANT RATHER THAN EMITTED OBJECT INITIALIZERS: emitted
        // initializers would have to track every RepoProps schema change forever, so a
        // field added to RepoProps tomorrow would silently stop being mirrored -- the exact
        // fragility this ticket removes, merely automated. A constant is schema-agnostic.
        // And being compiled INTO the assembly is what makes it answer the only failure
        // class this path has ever existed for: the catalog file could not be READ (no
        // Resources entry, unresolvable StreamingAssets path, truncated file, a WebGL fetch
        // that never lands). A constant needs none of those to resolve.
        //
        // Used ONLY when the JSON cannot be read, so the palette is never empty.
        /// <returns>Rows actually registered from the embedded copy.</returns>
        /// <param name="rowsInFile">Rows PRESENT in the embedded copy (reported as sourceRows).</param>
        private static int RegisterFallback(out int rowsInFile)
        {
            return ParseAndRegister(
                CatalogFallbackData.Json,
                "<embedded " + CatalogFallbackData.SourcePath +
                " v" + CatalogFallbackData.SourceVersion + ">",
                out rowsInFile);
        }
    }
}

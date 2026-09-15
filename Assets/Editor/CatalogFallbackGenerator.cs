// =============================================================================
// CatalogFallbackGenerator - WO-1137 (owner ruling 2026-08-23: CODEGEN the fallback).
// -----------------------------------------------------------------------------
// Assembly: DeNelle.Editor   Namespace: DeNelle.Editor
//
// WHAT THIS REPLACES.
// CatalogBootstrap.RegisterFallback used to be ~190 lines of HAND-MAINTAINED C#
// object initializers mirroring THREE of the catalog's 28 rows, field for field.
// It is the JSON-load-FAILURE path, so on that path those three rows WERE the
// game's whole build palette - a 3-of-28 content hole with nothing on screen
// saying so. Every value that drifted from its structures-catalog.json
// counterpart silently shipped different content. Two historic drifts are on the
// record in that method's own banner: footprint 2.5 vs the catalog's 1.75, and a
// visualPrefabPath pointing at PatriciaLight art DELETED on 2026-06-09.
//
// A regression gate (BuildEconomyRegression "[fallback-parity]") caught drift
// AFTER it was authored. The owner's ruling makes drift STRUCTURALLY IMPOSSIBLE
// instead: the fallback is no longer authored at all.
//
// WHAT IT GENERATES, AND WHY THIS SHAPE.
// It emits Assets/_Modules/Village/Catalog/Generated/CatalogFallbackData.g.cs -
// the canonical catalog JSON as an ASCII-escaped string constant, plus its
// SHA-256, row count and schema version. CatalogBootstrap.RegisterFallback then
// parses that constant through the SAME parse + row-validation path LoadFromJson
// uses, so both paths are one method and cannot diverge.
//
// It deliberately does NOT emit 28 field-by-field object initializers. That would
// recreate the exact fragility being removed: emitted initializers must track
// every RepoProps schema change forever, and a field added to RepoProps tomorrow
// silently stops being mirrored. A string constant is schema-agnostic - it is the
// catalog itself. (WO-1755, 2026-09-15: it was byte for byte until that date; the
// emitted payload is now the catalog with every '_'-prefixed AUTHORING NOTE
// stripped - see ProjectForFallback / CatalogFallbackProjection.Strip. Every row,
// id, cost and player-facing key is still the file's, unchanged, so the
// schema-agnostic property this paragraph is about is untouched.)
//
// A string constant is compiled INTO the assembly, so it survives every failure
// mode RegisterFallback exists for: a missing Resources entry, an unresolvable
// StreamingAssets path, a truncated file, a WebGL fetch that never lands. That is
// the only class of failure the fallback has ever guarded against.
//
// HOW IT IS JUDGED (CLAUDE.md s8): the MARKER on a FRESH log, never the exit code.
//   CATALOG_FALLBACK_GEN_OK   / CATALOG_FALLBACK_GEN_FAIL
//
// Batchmode:
//   powershell -NoProfile -File .\run-unity-method.ps1 `
//       -Method DeNelle.Editor.CatalogFallbackGenerator.Generate `
//       -LogName catalog-fallback-gen.log `
//       -ExpectMarker CATALOG_FALLBACK_GEN_OK
// =============================================================================

using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace DeNelle.Editor
{
    public static class CatalogFallbackGenerator
    {
        public const string MarkerOk   = "CATALOG_FALLBACK_GEN_OK";
        public const string MarkerFail = "CATALOG_FALLBACK_GEN_FAIL";

        /// <summary>The copy CanonicalJson.Read resolves FIRST, so it is the copy we generate from.</summary>
        public const string ResourcesCopy      = "Assets/Resources/Data/Canonical/structures-catalog.json";

        /// <summary>The authoring copy. Must stay BYTE-IDENTICAL to the Resources copy (CLAUDE.md / WO-1137).</summary>
        public const string StreamingAssetsCopy = "Assets/StreamingAssets/Data/Canonical/structures-catalog.json";

        /// <summary>Generated output. Lives under Assets/_Modules/Village so DeNelle.Village can see it.</summary>
        public const string OutputPath = "Assets/_Modules/Village/Catalog/Generated/CatalogFallbackData.g.cs";

        /// <summary>Regeneration command quoted in the freshness gate's failure message.</summary>
        public const string RegenCommand =
            "powershell -NoProfile -File .\\run-unity-method.ps1 " +
            "-Method DeNelle.Editor.CatalogFallbackGenerator.Generate " +
            "-LogName catalog-fallback-gen.log -ExpectMarker CATALOG_FALLBACK_GEN_OK";

        /// <summary>Source chars per emitted string literal. Keeps every literal well under any
        /// metadata/expression size cliff and keeps the generated file diff-readable.</summary>
        private const int ChunkChars = 2048;

        [MenuItem("Defenders/Catalog/Regenerate Fallback Data (WO-1137)")]
        public static void GenerateMenu() => Generate();

        public static void Generate()
        {
            try
            {
                string root = Directory.GetCurrentDirectory();
                string resAbs    = Path.Combine(root, ResourcesCopy.Replace('/', Path.DirectorySeparatorChar));
                string streamAbs = Path.Combine(root, StreamingAssetsCopy.Replace('/', Path.DirectorySeparatorChar));

                if (!File.Exists(resAbs))
                {
                    Fail($"the Resources copy is MISSING at {ResourcesCopy} - nothing to embed.");
                    return;
                }
                if (!File.Exists(streamAbs))
                {
                    Fail($"the StreamingAssets copy is MISSING at {StreamingAssetsCopy}. Both canonical " +
                         "copies must exist and be byte-identical.");
                    return;
                }

                byte[] resBytes    = File.ReadAllBytes(resAbs);
                byte[] streamBytes = File.ReadAllBytes(streamAbs);

                string resHash    = Sha256(resBytes);
                string streamHash = Sha256(streamBytes);

                // The dual-copy invariant, checked HERE rather than trusted: generating from a
                // Resources copy that has drifted from the authoring copy would bake the drift in.
                if (resHash != streamHash)
                {
                    Fail($"the two canonical copies are NOT byte-identical - " +
                         $"{ResourcesCopy} sha256={resHash} ({resBytes.Length} bytes) vs " +
                         $"{StreamingAssetsCopy} sha256={streamHash} ({streamBytes.Length} bytes). " +
                         "Reconcile them before generating; embedding a drifted copy would make the " +
                         "failure path ship content the authoring copy does not have.");
                    return;
                }

                // Decode as UTF-8 WITHOUT a BOM so the round-trip (embed -> Encoding.UTF8.GetBytes)
                // is byte-exact against the file we hashed. new UTF8Encoding(false) rather than
                // Encoding.UTF8 because the latter emits a preamble on GetPreamble().
                string json = new UTF8Encoding(false).GetString(resBytes);
                byte[] roundTrip = new UTF8Encoding(false).GetBytes(json);
                if (roundTrip.Length != resBytes.Length || Sha256(roundTrip) != resHash)
                {
                    Fail("the catalog JSON does not round-trip through UTF-8 byte-exactly (a BOM or an " +
                         "invalid byte sequence?). Refusing to embed a copy that is not the file.");
                    return;
                }

                int rowCount;
                int version;
                try
                {
                    var root2 = JObject.Parse(json);
                    var entries = root2["entries"] as JArray;
                    if (entries == null)
                    {
                        Fail($"{ResourcesCopy} has no 'entries' array - refusing to embed a catalog with " +
                             "no rows, because the fallback would then be an EMPTY build palette.");
                        return;
                    }
                    rowCount = entries.Count;
                    version  = root2["version"] != null ? (int)root2["version"] : 0;
                }
                catch (Exception pex)
                {
                    Fail($"{ResourcesCopy} did not parse: {pex.GetType().Name}: {pex.Message}. Refusing to " +
                         "embed an unparseable catalog - the fallback would be dead on arrival.");
                    return;
                }

                if (rowCount <= 0)
                {
                    Fail($"{ResourcesCopy} parsed to ZERO rows - refusing to embed an empty build palette.");
                    return;
                }

                // WO-1755. What gets EMBEDDED is the catalog with every '_'-prefixed key removed.
                // WHY, measured: WO-1740's RCA read the rejected AAB's global-metadata.dat and found
                // "the game is live on the Solana dApp Store, so renaming it orphans every existing
                // town" at offset 1,671,082 -- the '_quarryNote' authoring note, shipped into the
                // Google Play artifact as a COMPILED C# LITERAL out of this file. The Play neutral
                // sweep (GooglePlayContentExclusion.PlayNeutralMirrorPairs) rewrites the Resources
                // and StreamingAssets copies and provably works (aab-build.log:9164), but it cannot
                // reach a third copy that the C# compiler baked into the assembly.
                //
                // Stripping at EMISSION closes the door for EVERY future note rather than the one
                // WO-1416 happened to author -- the sweep's own reasoning, applied one layer down.
                // It is also correct on its own terms: authoring notes are addressed to whoever
                // edits the JSON, and the fallback blob is read by CatalogBootstrap.ParseAndRegister
                // and by nothing else. Measured 2026-09-15: all 61 '_'-prefixed keys in the catalog
                // carry STRING values, and no runtime type reads one -- the only reader anywhere is
                // CollectorIncomeRegression.cs:1357, an Editor suite that reads the JSON FILE.
                //
                // SourceSha256 / SourceByteLength below stay the SOURCE FILE's, so the staleness
                // gate (BuildEconomyRegression case B) is unchanged. Case C -- "nobody hand-edited
                // the generated file" -- can no longer compare the embedded string to that hash, so
                // it re-derives the expectation by calling THIS method. One projection, two callers,
                // no second copy of the rule.
                string embed = ProjectForFallback(json);

                string source = BuildSource(embed, resHash, rowCount, version, resBytes.Length);

                string outAbs = Path.Combine(root, OutputPath.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(outAbs));

                // Write LF-normalised UTF-8 without BOM. Deterministic bytes for the same input, so
                // a no-op regeneration produces a no-op diff.
                File.WriteAllBytes(outAbs, new UTF8Encoding(false).GetBytes(source));

                AssetDatabase.ImportAsset(OutputPath, ImportAssetOptions.ForceUpdate);

                Debug.Log($"{MarkerOk} wrote {OutputPath} from {ResourcesCopy} " +
                          $"(rows={rowCount} version={version} bytes={resBytes.Length} sha256={resHash}); " +
                          $"both canonical copies verified byte-identical; embedded payload is the " +
                          $"authoring-note-stripped projection ({embed.Length} chars, WO-1755).");
            }
            catch (Exception ex)
            {
                Fail($"{ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}");
            }
        }

        private static void Fail(string why)
        {
            Debug.LogError($"{MarkerFail} {why}");
        }

        // ---------------------------------------------------------------------
        //  WO-1755 - the projection that is embedded, and the SINGLE definition of it
        // ---------------------------------------------------------------------

        /// <summary>
        /// The projection that is EMBEDDED: the catalog with every '_'-prefixed authoring note
        /// removed. Delegates to <see cref="CatalogFallbackProjection.Strip"/> — the ONE
        /// definition of the rule, shared with BuildEconomyRegression's [fallback-parity] case C.
        /// <para>
        /// ⛔ It lives in DeNelle.EditorRegression, not here, because the suite that must agree
        /// with it cannot reference this assembly: DeNelle.Editor.asmdef already references
        /// DeNelle.EditorRegression, so the reverse reference would be a CYCLE. Do not
        /// re-implement the strip here to "keep it local" — a second copy is the duplicated-state
        /// failure CLAUDE.md §2/§5/§16 each describe, and it would let the gate certify a file
        /// this generator would not produce.
        /// </para>
        /// </summary>
        internal static string ProjectForFallback(string json) => CatalogFallbackProjection.Strip(json);

        private static string Sha256(byte[] bytes)
        {
            using (var sha = SHA256.Create())
            {
                var hash = sha.ComputeHash(bytes);
                var sb = new StringBuilder(hash.Length * 2);
                foreach (var b in hash) sb.Append(b.ToString("x2"));
                return sb.ToString();
            }
        }

        // ---------------------------------------------------------------------
        //  Emission
        // ---------------------------------------------------------------------
        /// <param name="json">The payload to embed — the ProjectForFallback projection, NOT the raw file.</param>
        /// <param name="sha">SHA-256 of the SOURCE FILE's bytes. The staleness gate's evidence; unrelated to the payload.</param>
        private static string BuildSource(string json, string sha, int rowCount, int version, int byteLen)
        {
            var sb = new StringBuilder(json.Length * 3);

            sb.Append("// <auto-generated>\n");
            sb.Append("// =============================================================================\n");
            sb.Append("//  !!  DO NOT EDIT THIS FILE  !!   IT IS GENERATED.  ANY HAND EDIT IS LOST.\n");
            sb.Append("// -----------------------------------------------------------------------------\n");
            sb.Append("//  Generator : DeNelle.Editor.CatalogFallbackGenerator.Generate\n");
            sb.Append("//              (menu: Defenders > Catalog > Regenerate Fallback Data (WO-1137))\n");
            sb.Append("//  Source    : " + ResourcesCopy + "\n");
            sb.Append("//  Regenerate:\n");
            sb.Append("//    " + RegenCommand + "\n");
            sb.Append("//\n");
            sb.Append("//  WO-1137 (owner ruling 2026-08-23). The CatalogBootstrap JSON-load-FAILURE\n");
            sb.Append("//  path used to be a hand-written 3-row mirror of a 28-row catalog, and drift\n");
            sb.Append("//  between the two silently shipped different content. It is now THIS: the\n");
            sb.Append("//  catalog itself, embedded and parsed through the same code path as the\n");
            sb.Append("//  file. Drift is not gated, it is impossible.\n");
            sb.Append("//\n");
            sb.Append("//  WO-1755: the embedded payload is the catalog with every '_'-prefixed\n");
            sb.Append("//  AUTHORING NOTE removed (CatalogFallbackGenerator.ProjectForFallback).\n");
            sb.Append("//  Those notes are addressed to whoever edits the JSON and are read by no\n");
            sb.Append("//  runtime type; one of them shipped \"the Solana dApp Store\" into the\n");
            sb.Append("//  Google Play AAB as a compiled literal, which the Play neutral sweep\n");
            sb.Append("//  cannot reach because it rewrites FILES. Values, ids, costs and every\n");
            sb.Append("//  player-facing key are untouched.\n");
            sb.Append("//\n");
            sb.Append("//  If a value here looks wrong, EDIT " + ResourcesCopy + "\n");
            sb.Append("//  (and its StreamingAssets twin) and re-run the generator. Editing this file\n");
            sb.Append("//  instead makes the two disagree, and BuildEconomyRegression's\n");
            sb.Append("//  [fallback-parity] freshness gate will go RED on the SHA mismatch.\n");
            sb.Append("// =============================================================================\n");
            sb.Append("\n");
            sb.Append("namespace DeNelle.Village\n");
            sb.Append("{\n");
            sb.Append("    /// <summary>\n");
            sb.Append("    /// GENERATED. The canonical structures catalog, compiled into the assembly so the\n");
            sb.Append("    /// build palette survives any failure to READ the catalog file at runtime.\n");
            sb.Append("    /// Consumed by <see cref=\"CatalogBootstrap\"/>; freshness-gated by\n");
            sb.Append("    /// BuildEconomyRegression's [fallback-parity] check.\n");
            sb.Append("    /// </summary>\n");
            sb.Append("    public static class CatalogFallbackData\n");
            sb.Append("    {\n");
            sb.Append("        /// <summary>Repo-relative path of the file this was generated from.</summary>\n");
            sb.Append("        public const string SourcePath = \"" + ResourcesCopy + "\";\n");
            sb.Append("\n");
            sb.Append("        /// <summary>SHA-256 of that file's exact bytes at generation time. The freshness gate's evidence.</summary>\n");
            sb.Append("        public const string SourceSha256 = \"" + sha + "\";\n");
            sb.Append("\n");
            sb.Append("        /// <summary>Byte length of the source file at generation time.</summary>\n");
            sb.Append("        public const int SourceByteLength = " + byteLen + ";\n");
            sb.Append("\n");
            sb.Append("        /// <summary>Rows in the embedded catalog. Reported as sourceRows on the fallback boot-count line.</summary>\n");
            sb.Append("        public const int SourceRowCount = " + rowCount + ";\n");
            sb.Append("\n");
            sb.Append("        /// <summary>The catalog's own schema version field.</summary>\n");
            sb.Append("        public const int SourceVersion = " + version + ";\n");
            sb.Append("\n");
            sb.Append("        /// <summary>The exact command that regenerates this file. Quoted verbatim by the freshness gate.</summary>\n");
            sb.Append("        public const string RegenerateCommand =\n");
            sb.Append("            \"" + Escape(RegenCommand) + "\";\n");
            sb.Append("\n");
            sb.Append("        private static string _json;\n");
            sb.Append("\n");
            sb.Append("        /// <summary>\n");
            sb.Append("        /// The catalog JSON from <see cref=\"SourcePath\"/> with every '_'-prefixed authoring\n");
            sb.Append("        /// note stripped (WO-1755). Every row, id, cost and player-facing key is unchanged.\n");
            sb.Append("        /// Split into literals purely so the generated file stays diff-readable.\n");
            sb.Append("        /// </summary>\n");
            sb.Append("        public static string Json\n");
            sb.Append("        {\n");
            sb.Append("            get { return _json ?? (_json = string.Concat(Parts)); }\n");
            sb.Append("        }\n");
            sb.Append("\n");
            sb.Append("        private static readonly string[] Parts = new string[]\n");
            sb.Append("        {\n");

            int i = 0;
            while (i < json.Length)
            {
                int len = System.Math.Min(ChunkChars, json.Length - i);
                // Never split a surrogate pair across two literals.
                if (i + len < json.Length && char.IsHighSurrogate(json[i + len - 1])) len--;
                sb.Append("            \"").Append(Escape(json.Substring(i, len))).Append("\",\n");
                i += len;
            }

            sb.Append("        };\n");
            sb.Append("    }\n");
            sb.Append("}\n");

            return sb.ToString();
        }

        /// <summary>
        /// Escapes to a PURE-ASCII C# string literal body. Non-ASCII becomes \uXXXX deliberately:
        /// the repo has been bitten by cp1252/UTF-8 confusion on .cs files (CLAUDE.md s0), and an
        /// ASCII-only generated file cannot be mis-decoded by any tool in the chain.
        /// </summary>
        private static string Escape(string s)
        {
            var sb = new StringBuilder(s.Length + 32);
            foreach (char c in s)
            {
                switch (c)
                {
                    case '\\': sb.Append("\\\\"); break;
                    case '"':  sb.Append("\\\""); break;
                    case '\r': sb.Append("\\r");  break;
                    case '\n': sb.Append("\\n");  break;
                    case '\t': sb.Append("\\t");  break;
                    default:
                        if (c < 0x20 || c > 0x7E) sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            }
            return sb.ToString();
        }
    }
}

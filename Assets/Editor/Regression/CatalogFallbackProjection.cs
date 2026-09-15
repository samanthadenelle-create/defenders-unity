// =============================================================================
// CatalogFallbackProjection - WO-1755.
// -----------------------------------------------------------------------------
// Assembly: DeNelle.EditorRegression   Namespace: DeNelle.Editor
//
// THE ONE DEFINITION of "what gets compiled into the player as the catalog
// fallback": the canonical catalog JSON with every '_'-prefixed AUTHORING NOTE
// removed, at every depth.
//
// WHY IT EXISTS AT ALL.
// WO-1740's RCA read the rejected Google Play AAB's global-metadata.dat and found
// "the game is live on the Solana dApp Store, so renaming it orphans every
// existing town" at offset 1,671,082. That is the '_quarryNote' authoring note
// from structures-catalog.json, shipped as a COMPILED C# STRING LITERAL out of
// Assets/_Modules/Village/Catalog/Generated/CatalogFallbackData.g.cs.
//
// There are THREE copies of that catalog. GooglePlayContentExclusion's neutral
// sweep rewrites copies 1 and 2 (Resources + StreamingAssets) and provably works
// - aab-build.log:9164 of the 2026-09-15 run says so. Copy 3 is CODE, and a sweep
// that rewrites FILES can never reach it. Stripping at EMISSION closes the door
// for EVERY future note rather than the one WO-1416 happened to author.
//
// WHY IT LIVES HERE, IN THE *REGRESSION* ASSEMBLY, AND NOT NEXT TO THE GENERATOR.
// Two callers need it and they are in different assemblies:
//   * DeNelle.Editor            - CatalogFallbackGenerator, which EMITS the projection
//   * DeNelle.EditorRegression  - BuildEconomyRegression's [fallback-parity] case C,
//                                 which must know what the emitted payload should be
// The dependency runs ONE WAY: DeNelle.Editor.asmdef references
// DeNelle.EditorRegression. The reverse reference would be a CYCLE, so the shared
// rule has to sit in the lower assembly. Putting it beside the generator and
// copying it into the suite would be the duplicated-state failure CLAUDE.md s2,
// s5 and s16 each describe - and the copy that drifted would let the gate certify
// a file the generator would not produce.
//
// ⛔ DO NOT RE-IMPLEMENT THE STRIP ANYWHERE ELSE. Call this.
//
// SAFETY, MEASURED 2026-09-15 rather than assumed:
//   * all 61 '_'-prefixed keys in structures-catalog.json carry STRING values
//   * no runtime type reads one; the only reader in the repo is
//     CollectorIncomeRegression.cs:1357, an Editor suite reading the JSON FILE
//   * the projection preserves all 29 rows and "version": 42
//   * it contains ZERO tokens from GooglePlayPackagingGate.ForbiddenTokens
// =============================================================================

using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace DeNelle.Editor
{
    public static class CatalogFallbackProjection
    {
        /// <summary>
        /// Returns <paramref name="json"/> with every '_'-prefixed property removed, at every
        /// depth, serialised deterministically.
        /// </summary>
        /// <remarks>
        /// TWO DELIBERATE PARSER SETTINGS, both of which would otherwise change a value this
        /// method never meant to touch:
        /// <list type="bullet">
        /// <item><c>DateParseHandling.None</c> - Newtonsoft's DEFAULT is
        /// <c>DateParseHandling.DateTime</c>, which silently converts any string value that
        /// looks like an ISO date and re-emits it in a different form ("2026-08-26" becomes
        /// "2026-08-26T00:00:00"). The old generator never re-serialised, so this hazard is new
        /// with WO-1755; the catalog carries date-shaped strings today, so it is not theoretical.</item>
        /// <item>Newlines normalised to "\n" - the writer emits <c>Environment.NewLine</c>, which
        /// would make the generated file's bytes, and therefore the freshness gate's hash, depend
        /// on which machine ran the generator.</item>
        /// </list>
        /// </remarks>
        public static string Strip(string json)
        {
            JObject root;
            using (var reader = new JsonTextReader(new StringReader(json)))
            {
                reader.DateParseHandling = DateParseHandling.None;
                root = JObject.Load(reader);
            }

            StripAuthoringNotes(root);

            return root.ToString(Formatting.Indented)
                       .Replace("\r\n", "\n")
                       .Replace("\r", "\n");
        }

        /// <summary>Removes every '_'-prefixed property from this token and its descendants.</summary>
        private static void StripAuthoringNotes(JToken token)
        {
            if (token is JObject obj)
            {
                var doomed = new List<JProperty>();
                foreach (JProperty prop in obj.Properties())
                {
                    if (prop.Name.Length > 0 && prop.Name[0] == '_') doomed.Add(prop);
                    else StripAuthoringNotes(prop.Value);
                }
                foreach (JProperty prop in doomed) prop.Remove();
                return;
            }

            if (token is JArray arr)
            {
                foreach (JToken child in arr) StripAuthoringNotes(child);
            }
        }
    }
}

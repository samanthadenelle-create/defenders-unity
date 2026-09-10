// =============================================================================
// BuildCardArtRegression — every build card the player can see resolves to REAL
// art, or is recorded as known debt. Nothing silently degrades to a letter.
// -----------------------------------------------------------------------------
// THE INVARIANT. For every row in structures-catalog.json, BuildPaletteUI's own art
// resolver must return a Sprite. When it returns null the card falls back to a single
// LETTER glyph on a dark plate — which is what the 2026-08-09 WO-1010 capture caught:
// "Lumberyard" rendered as a bare "L" sitting among fully illustrated neighbours.
//
// WHY THIS MATTERS MORE THAN IT LOOKS. WO-1010 exists because external testers could
// not read the build screen. A card carrying a letter instead of a picture is exactly
// the thing that makes a shop hard to scan: the eye cannot tell a real building from a
// placeholder, so the whole row reads as unfinished. It is a CONTENT gap, invisible to
// every gate in this repo, and it grows silently every time a catalog row lands before
// its portrait does.
//
// ASKS THE REAL RESOLVER, NOT A FILENAME GUESS. The check calls
// BuildPaletteUI.ResolveEntryArtPublic — the exact method the card builder uses, which
// tries Portraits/<id>, then Portraits/<display-name-slug>, then
// ConceptIconResolver.ResolveAny. A directory listing would MISCOUNT, because the
// concept resolver legitimately rescues rows that own no portrait file of their own.
// Re-deriving the lookup here is how a gate and the game come to disagree while both
// report success (the same rule EnemyRigControllerCoherenceRegression keeps).
//
// RATCHETED, NOT RETROACTIVE. The rows missing art TODAY are recorded below as debt so
// this suite can go green on a tree it did not break, while any NEW artless row fails
// the gate immediately. Shrinking the list is the fix; growing it is the failure.
//
// Registered in DataRegression.RunAll (covenant style).
// =============================================================================

using System;
using System.Collections.Generic;
using System.Text;
using DeNelle.Core.Catalog;
using DeNelle.Village;
using UnityEngine;

namespace DeNelle.Editor.Regression
{
    public static class BuildCardArtRegression
    {
        /// <summary>
        /// Catalog ids whose card had NO resolvable art on 2026-08-09, when this oracle was
        /// written. This is a DEBT LEDGER, not an allowlist: every entry is a card that
        /// renders as a letter glyph in the shop. Delete ids as portraits land. Adding one
        /// means shipping another placeholder into the screen WO-1010 was raised to fix.
        /// </summary>
        private static readonly HashSet<string> KnownArtlessIds =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                // -- 2026-08-09: SIX ids left this list when the owner authored the stockpile
                //    and wall art and the aliases landed. The ratchet tightened; it must not
                //    loosen again. Removed: lumberyard, foundry, silo, wall_wood, wall_stone,
                //    collector_lumbermill.
                // ⚠ NONE OF THESE IS PLAYER-VISIBLE. Every id below is filtered out of its
                // verb's palette by BuildCategoryRegistry.LockedIds, or belongs to a
                // CatalogType no verb maps to. Rows stay in the catalog because saves replay
                // them (WO-707), but no card is ever drawn — so NO ART IS NEEDED for any of
                // them. Do not commission portraits off this list; it is a debt ledger for
                // content that is retired or gated, not a work queue.
                "mill",                // RETIRED (WO-707): Farm is the food producer
                "gate_stone",          // Defense LockedIds - gated
                // (2026-08-14, WO-990: the Healer Tower id left this list because the ROW ITSELF
                //  was retired from structures-catalog.json v20 by owner ruling. Its entry here
                //  read "legacy Support verb only - not reachable from Town/Defenses", which was
                //  the very evidence that the row had never been buildable. See the retained
                //  StructureFactory "HealerTower" case for the behaviour that deliberately stays.)
                // Type 'Decoration', and NO build verb maps to Decoration.
                "deco_torch",
                "repair_default",      // a repair-economy DATA row, not a building
                // 2026-09-10, WO-1619 (owner ruling, same date): the Forsaken Camp raid base's
                // spire art. It is a RAID row, not a build card - type 'Decoration' (so no build
                // verb maps to it, the same reason the two ids above are here), no manageFilters
                // (so BuildInventoryModel scores it BuildAvailability.NotPlayerContent), in no
                // card collection, and placement.checkAffordable false. RaidBaseGenerator.PlaceSpire
                // instantiates it directly from visualPrefabPath at BAKE time; it never reaches a
                // palette, so NO CARD IS EVER DRAWN and no portrait is owed. Per this list's own
                // header this is debt-ledger content that is "retired or gated", NOT a work queue -
                // do not commission a portrait for it, and do not delete this line expecting art.
                "tower_ruined_watchtower",
            };

        /// <summary>Shape of Data/Canonical/structures-catalog.json.</summary>
        private sealed class CatalogFile
        {
            [Newtonsoft.Json.JsonProperty("version")] public int Version;
            [Newtonsoft.Json.JsonProperty("entries")] public List<CatalogEntry> Entries = new List<CatalogEntry>();
        }

        public static bool Run(out string reason)
        {
            var newlyArtless = new List<string>();
            var fixedIds     = new List<string>();
            int resolved = 0, checkedCount = 0;

            // READ THE SHIPPED CATALOG, NOT THE SHARED STATIC REGISTRY. The first version of
            // this suite walked CatalogRegistry.All() and immediately failed on
            // "test-fa48bb7a963b4b70b8ca13b6247aa9ec" — a synthetic fixture some EARLIER suite
            // registered into the process-wide static and never removed. That made this
            // oracle's verdict depend on suite ORDER and on other suites' hygiene, which is
            // not a property a gate may have. Filtering "test-" ids would have hidden the
            // coupling rather than removed it. The canonical file is what ships, so it is what
            // gets judged — deterministic, order-independent, and immune to fixture leakage.
            string json = DeNelle.Core.CanonicalJson.Read("Data/Canonical/structures-catalog.json");
            if (string.IsNullOrEmpty(json))
            {
                reason = "FAIL: Data/Canonical/structures-catalog.json unreadable — this oracle " +
                         "checked ZERO cards. A silent zero-check is how coverage disappears.";
                return false;
            }

            CatalogFile file;
            try
            {
                file = Newtonsoft.Json.JsonConvert.DeserializeObject<CatalogFile>(json,
                    new Newtonsoft.Json.JsonSerializerSettings
                    {
                        Converters = { new Newtonsoft.Json.Converters.StringEnumConverter() },
                        NullValueHandling = Newtonsoft.Json.NullValueHandling.Ignore,
                        MissingMemberHandling = Newtonsoft.Json.MissingMemberHandling.Ignore,
                    });
            }
            catch (Exception ex)
            {
                reason = "FAIL: structures-catalog.json failed to parse: " + ex.Message;
                return false;
            }

            var all = file != null && file.Entries != null
                ? (IReadOnlyList<CatalogEntry>)file.Entries
                : new List<CatalogEntry>();
            if (all.Count == 0)
            {
                reason = "FAIL: structures-catalog.json deserialized to ZERO entries — this oracle " +
                         "checked no cards at all.";
                return false;
            }

            foreach (var e in all)
            {
                if (e == null || string.IsNullOrEmpty(e.id)) continue;
                checkedCount++;

                Sprite art = null;
                try { art = BuildPaletteUI.ResolveEntryArtPublic(e); }
                catch (Exception ex)
                {
                    newlyArtless.Add(e.id + " (resolver THREW: " + ex.Message + ")");
                    continue;
                }

                bool known = KnownArtlessIds.Contains(e.id);
                if (art == null)
                {
                    if (!known) newlyArtless.Add(e.id);
                }
                else
                {
                    resolved++;
                    if (known) fixedIds.Add(e.id);
                }
            }

            if (newlyArtless.Count > 0)
            {
                var sb = new StringBuilder();
                sb.AppendLine("FAIL: " + newlyArtless.Count + " build card(s) have NO resolvable art and are " +
                              "NOT in the recorded debt list — each renders as a bare letter glyph in the shop:");
                foreach (var id in newlyArtless) sb.AppendLine("    - " + id);
                sb.Append("    Add a Portraits/<id> (or Portraits/<display-name-slug>) image, or a " +
                          "ConceptIconResolver mapping. If the placeholder is genuinely intended for now, " +
                          "add the id to KnownArtlessIds WITH a reason — never silently.");
                reason = sb.ToString();
                return false;
            }

            var ok = new StringBuilder();
            ok.Append("OK (WITH RECORDED DEBT) — " + resolved + " of " + checkedCount +
                      " build card(s) resolve real art; " + KnownArtlessIds.Count +
                      " id(s) are recorded as artless and still render as LETTER GLYPHS. " +
                      "This is NOT a clean shop.");
            if (fixedIds.Count > 0)
                ok.Append(Environment.NewLine + "    " + fixedIds.Count +
                          " recorded id(s) now RESOLVE — remove them from KnownArtlessIds so the " +
                          "ratchet tightens: " + string.Join(", ", fixedIds.ToArray()));
            reason = ok.ToString();
            return true;
        }
    }
}

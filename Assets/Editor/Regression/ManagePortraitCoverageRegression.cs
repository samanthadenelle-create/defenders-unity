// =============================================================================
// ManagePortraitCoverageRegression - 2026-09-06, the Manage portrait lane.
// Every id a Manage tab can DISPLAY resolves to a sprite, or is named on a
// DATED exemption list. Nothing blanks silently and nothing passes silently.
// -----------------------------------------------------------------------------
// WHY THIS SUITE EXISTS
// The owner captured Logs/device/screens/owner-screen-144143.png (MANAGE / BUILD):
// four civic tiles with the warm-tan PLACEHOLDER DISC where a portrait belongs.
// The whole class of defect - "a tile renders empty and nobody finds out until she
// plays it" - is what WorkOrders/ManageRedesign/CAPTURE_LOOP_GOAL.md section 3b
// asks to be closed, and the closure has to be an ORACLE, not a report: an art gap
// that only lives in a markdown file goes stale the day the art lands.
//
// ⛔ NO SUBSTITUTE ICONS ANYWHERE IN THIS PROGRAM. A blank frame that logs once is
// honest; a wrong icon is a lie the capture loop cannot see. So this suite's remedy
// for a miss is always one of exactly two things - fix the KEY, or request the ART -
// and the exemption list below is how the second one stays visible.
//
// -----------------------------------------------------------------------------
// ⛔ RED PROOF - MEASURED 2026-09-06, BEFORE THE FIX IN THE SAME CHANGE.
//
// Case [building-tier-portrait] FAILED on TWENTY keys against the tree as it stood.
// ManageScreenVM.BuildBuildingChoices read
//     IconKey = ResolveBuildingPortraitKey(entry, id, level)
// which emits "Portraits/<ladder>[-N]" - the MIXED root folder. Enumerated from
// building-tiers.json (six ladders, 26 authored tiers) against the filesystem:
//
//     missing under Portraits/            missing under Portraits/Buildings/
//     arcane-tower-2,-3,-4                (none)
//     armorer-2,-3,-4                     (none)
//     barracks-2,-3,-4,-5,-6              (none)
//     farm-2,-3,-4                        (none)
//     forge-2,-3,-4                       (none)
//     lumbermill-2,-3,-4                  (none)
//     ---------------------------------   ---------------------------------
//     20 keys MISS                        0 keys miss (all 26 present)
//
// i.e. every owned building above level 1 painted a BLANK tile in the Manage BUILD
// grid while its art sat one folder away. Level 1 escaped only because six legacy
// JPGs (forge.jpg, barracks.jpg, farm.jpg ...) happen to sit in the root - and the
// root is a MIXED namespace, which is exactly why ManageScreenPanel keeps a
// ManageBuildingPortraitGaps list naming those same six ids as the ones whose root
// route resolves a PERSON rather than a building.
//
// GREEN after BuildBuildingChoices was re-pointed to ManageArt.BuildingPortraitKey.
//
// TO RE-RED IT, either:
//   (a) change ManageArt.BuildingPortraitFolder to "Portraits/"
//       -> [building-tier-portrait] fires on all 20, or
//   (b) put ResolveBuildingPortraitKey(entry, id, level) back in BuildBuildingChoices
//       -> [vm-uses-building-portrait-key] fires.
// ⚠ (b) NEEDS ITS OWN CASE AND THAT IS WHY THE CASE EXISTS. [building-tier-portrait]
// builds its keys by CALLING ManageArt.BuildingPortraitKey, so it pins the FOLDER
// CONSTANT and not the VM's use of it - on its own it would stay GREEN while every
// tier tile went blank again, which is the precise regression this file was written
// to catch. Same shape as the troop case, which reads its folder out of the VM
// source rather than retyping it.
//
// -----------------------------------------------------------------------------
// THE SECOND RED - the four CIVIC tiles the owner actually photographed.
//
// They are NOT BUILT, so they route through ComposeUnplacedItem, which keyed off
// row.ArtKey = manageArtKey - a BARE Sheet-A tile name ("building-store") with no
// Resources folder. Resources.Load therefore searched the Resources ROOT, where no
// Manage art lives, and every not-yet-built tile painted the placeholder disc. That
// is a CODE defect independent of delivery: dropping all 24 PNGs in would not have
// made "building-store" resolve.
//
// OWNER RULING 2026-09-06 - OPTION A: building art is ONE folder, Portraits/Buildings/,
// keyed by CATALOG ID. ComposeUnplacedItem now emits ManageArt.BuildingPortraitKey(row.Id, 0)
// and manageArtKey goes back to being what the catalog note always called it - the
// art-to-id join label, never a key. Pinned by [unplaced-uses-building-portrait-key];
// RED = restore `IconId = row.ArtKey`.
//
// ⭐ AND THE EXEMPTIONS WERE RE-POINTED IN THE SAME CHANGE, WHICH IS THE POINT.
// They used to be the 24 Sheet-A names. Nobody will ever author "building-store.png";
// they will author "market.png" - so those exemptions could never expire and would have
// become permanent silent skips. Keyed off the catalog id they are now the EXACT key the
// code emits and the EXACT filename the artist writes, so delivering the art makes the key
// resolve and [exemption-still-accurate] FAILS until the line is deleted. 20 ids remain;
// the other 4 of the 24 (arcane-tower, armorer, barracks, forge) already have art under
// their own catalog id and need nothing.
// -----------------------------------------------------------------------------
//
// ⚠ REVERT RECIPE (if this suite blocks a lane and must come out in a hurry):
//   1. delete Assets/Editor/Regression/ManagePortraitCoverageRegression.cs (+ .meta)
//   2. delete the single registration line in Assets/Editor/Regression/DataRegression.cs
//      (search "manage-portrait-coverage")
//   Nothing else references it. Pure verification: no runtime path, no data, no asset.
// =============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using DeNelle.Core.Manage;
using UnityEngine;

namespace DeNelle.Editor.Regression
{
    public static class ManagePortraitCoverageRegression
    {
        private const string TroopsPath   = "Assets/Resources/Data/Canonical/troops.json";
        private const string TiersPath    = "Assets/Resources/Data/Canonical/building-tiers.json";
        private const string CatalogPath  = "Assets/Resources/Data/Canonical/structures-catalog.json";
        private const string VmPath       = "Assets/_Modules/Village/UI/Manage/ManageScreenVM.cs";

        /// <summary>
        /// DATED and CITED art exemptions - the same mechanism
        /// BuildInventoryFilterRegression.UnnamedLockIds uses, and for the same reason: a gap that
        /// lives only in prose goes stale, and a suite that skips quietly guards nothing.
        /// (⚠ NOT self-cleaning under Option A - see the second para; that is a named hole, not a
        /// property this list currently has.)
        ///
        /// <para>EVERY entry here is a key that CANNOT resolve today and that the owner must close
        /// with ART, not with code. Case [exemption-still-accurate] FAILS if one of them starts
        /// resolving - so a delivered asset FORCES its line out of this list rather than leaving a
        /// dead exemption behind that would mask the next gap.</para>
        ///
        /// <para>⛔ NEVER ADD A KEY HERE TO MAKE THE SUITE GREEN. A key earns a line only with the
        /// measurement that put it there, and only when the remedy is genuinely an art request.
        /// A key that is MIS-KEYED (the art exists somewhere else) is a CODE defect and must be
        /// fixed, never exempted - that is the whole distinction the 2026-09-06 pass turned on.</para>
        /// </summary>
        // WO-1495 2026-09-06 remove-by 2026-12-06 (origin WO-1487) - art keys the owner must close
        // with art, not code. MEASURED EMPTY on 2026-09-06; the summary above already dates the
        // mechanism, and this line adds the expiry the WO-1495 ratchet requires.
        private static readonly Dictionary<string, string> ArtExemptions =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// CATALOG IDS whose building portrait has not been delivered. Under OWNER RULING 2026-09-06
        /// (Option A) building art is ONE folder keyed by catalog id, so an entry here becomes the
        /// key <c>Portraits/Buildings/&lt;id&gt;</c> - EXACTLY the key ComposeUnplacedItem emits, and
        /// exactly the filename the artist will author.
        ///
        /// <para>⭐ THAT EQUALITY IS THE WHOLE POINT AND IT WAS NOT TRUE AN HOUR AGO. This list used to
        /// hold the 24 Sheet-A tile names (`building-store` ...). Nobody will ever author
        /// `building-store.png` - they will author `market.png` - so `LoadSprite("building-store")`
        /// would have stayed null forever, [exemption-still-accurate] would never have fired, and
        /// these exemptions would have quietly become PERMANENT. A dated exemption that can never
        /// expire is just a silent skip with a comment on it, which is this repo's dominant failure
        /// mode (CLAUDE.md 2 / 5 / 16). Keyed off the catalog id, dropping `market.png` into
        /// Portraits/Buildings/ makes that key resolve and FORCES its line out of this list.</para>
        ///
        /// <para>MEASURED 2026-09-06: no `building-*` file exists anywhere under Assets/ in any
        /// format (docs/ART_DELIVERY_2026-09-06_manage_assets.md section F - *"Sheet A draws 24
        /// building tiles; one arrived"*), and of the 24 catalog rows that carry a manageArtKey,
        /// exactly FOUR already have art under their own catalog id in Portraits/Buildings/ -
        /// arcane-tower, armorer, barracks, forge. The other TWENTY are listed below and are the
        /// art request. ⚠ collector_farm / collector_forge / collector_lumbermill are NOT covered by
        /// the existing farm/forge/lumbermill files: those are LADDER ids and these are CATALOG ids.
        /// See section 7 of the art request for why that distinction is load-bearing.</para>
        ///
        /// <para>⛔ NEVER ADD AN ID HERE TO MAKE THE SUITE GREEN. An id earns a line only when its
        /// art genuinely does not exist. An id that is MIS-KEYED (the art exists elsewhere) is a
        /// CODE defect and must be fixed - that is the distinction the whole 2026-09-06 pass turned
        /// on, and it is what separated the 20 tier keys (fixed) from these 20 ids (art).</para>
        ///
        /// <para>⭐ CLOSED 2026-09-06, THE SAME DAY IT OPENED, AND THE MECHANISM IS WHY. All TWENTY
        /// ids listed here were delivered as 1024x1024 PNGs by commit <c>ad808ecf3</c>, and the very
        /// next run of this suite FAILED on all twenty at once with [exemption-still-accurate]
        /// (<c>Builds/reg-wave3b.log:9525-9544</c>) - exactly the forced expiry the paragraph above
        /// promised. The list is now EMPTY, and the matching rows are gone from section 7b of
        /// docs/ART_REQUEST_2026-09-06_manage_tab_portraits.md. ⛔ Do NOT delete the array or the
        /// static ctor with it: the empty list IS the current measurement (zero building portraits
        /// outstanding), and the mechanism is what makes the NEXT undelivered id fail loudly instead
        /// of painting a silent blank tile. The history above stands on purpose - it records why the
        /// list is keyed off the catalog id, which is the only reason it could ever expire.</para>
        /// </summary>
        private static readonly string[] CatalogIdsWithNoBuildingArt = { };

        static ManagePortraitCoverageRegression()
        {
            foreach (string id in CatalogIdsWithNoBuildingArt)
                ArtExemptions[ManageArt.BuildingPortraitKey(id, 0)] =
                    "2026-09-06 ART REQUEST (owner ruling Option A): no building portrait delivered for " +
                    "catalog id '" + id + "'. Close it by authoring Assets/Resources/Portraits/Buildings/" +
                    id + ".png - at which point this exemption FAILS as stale and must be deleted, here " +
                    "and in docs/ART_REQUEST_2026-09-06_manage_tab_portraits.md section 7.";
        }

        public static bool Run(out string reason)
        {
            var failures = new List<string>();
            var log = new StringBuilder("=== ManagePortraitCoverageRegression ===\n");
            int checkedKeys = 0;

            try
            {
                ManageArt.ClearCache();
                checkedKeys += CheckChrome(failures, log);
                checkedKeys += CheckStatGlyphs(failures, log);
                checkedKeys += CheckTroopPortraits(failures, log);
                checkedKeys += CheckBuildingTierPortraits(failures, log);
                CheckVmUsesBuildingPortraitKey(failures, log);
                checkedKeys += CheckUnplacedArtKeys(failures, log);
                CheckUnplacedUsesBuildingPortraitKey(failures, log);
                CheckExemptionsStillAccurate(failures, log);
            }
            catch (Exception ex)
            {
                failures.Add("[manage-portrait-coverage] suite threw " + ex.GetType().Name + ": " + ex.Message);
            }

            if (failures.Count == 0)
            {
                reason = "MANAGE_PORTRAIT_COVERAGE_OK " + checkedKeys + " Manage portrait key(s) resolve; " +
                         ArtExemptions.Count + " dated art exemption(s) still genuinely absent. " +
                         "(Counts REPORTED, never asserted.)";
                Debug.Log(reason + "\n" + log);
                return true;
            }

            reason = "MANAGE_PORTRAIT_COVERAGE_FAIL\n" + string.Join("\n", failures);
            Debug.LogError(reason + "\n" + log);
            return false;
        }

        // ── cases ─────────────────────────────────────────────────────────────

        /// <summary>
        /// Every frame and medallion ManageVmProjection can put on a tile. Enumerated over the
        /// ENUM, not over a hand list, so a sixth visual state cannot ship with no art.
        /// RED: rename any file under Assets/Resources/RpgUi/manage/.
        /// </summary>
        private static int CheckChrome(List<string> failures, StringBuilder log)
        {
            var keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (ManageTileVisualState state in Enum.GetValues(typeof(ManageTileVisualState)))
            {
                keys.Add(ManageArt.FrameFor(state));
                keys.Add(ManageArt.StatusFor(state));
            }
            // frame-selected is the ONE manage PNG no state maps to: FrameFor never returns it,
            // because the renderer carries it on a SEPARATE, LARGER rect so its glow has somewhere
            // to bleed (ManageArt's header). Enumerating the enum alone would leave it unguarded.
            keys.Add(ManageArt.FrameSelected);
            foreach (string key in keys) Require(key, "chrome", "manage frame/medallion", failures);
            log.AppendLine("chrome keys checked=" + keys.Count);
            return keys.Count;
        }

        /// <summary>
        /// [stat-glyphs-resolve] WO-1654 row 5.3 - the four troop-stat glyphs the detail card
        /// paints against Health / Attack / Range / Speed.
        ///
        /// <para>⛔ THIS IS A RESOLUTION ORACLE, NOT A SOURCE LINT, AND THE DIFFERENCE IS THE
        /// WHOLE POINT. A case that only proved the four constants exist would pass with all four
        /// PNGs deleted - and it would pass LOUDLY, because ManageArt.LoadSprite degrades an
        /// unresolvable key to a transparent Image (ManageWorkspacePanel.PaintSprite sets alpha 0
        /// when the sprite is null). The failure mode is therefore INVISIBLE on a device: no pink
        /// square, no exception, just a stat column with no glyphs and nobody's eyes on it but the
        /// owner's - which is the thing CLAUDE.md section 14 exists to never rely on. So every key
        /// is pushed through the PRODUCTION loader, exactly as the portrait keys are.</para>
        ///
        /// <para>THE LIST IS ENUMERATED OFF <see cref="ManageArt.StatIconKeys"/>, NOT RETYPED
        /// HERE. A second copy of the four keys would keep passing on the day a fifth stat gained
        /// a glyph and nobody added it - the duplicated-state shape CLAUDE.md 2 / 5 / 16 records
        /// three times over.</para>
        ///
        /// <para>RED PROOF: delete any one of
        /// <c>Assets/Resources/UI/ElarionMedieval/Manage/stat-{health,attack,range,speed}.png</c>.</para>
        /// </summary>
        private static int CheckStatGlyphs(List<string> failures, StringBuilder log)
        {
            var keys = ManageArt.StatIconKeys;
            if (keys == null || keys.Length == 0)
            {
                failures.Add("[stat-glyphs-resolve] ManageArt.StatIconKeys is empty, so this case would " +
                             "assert on nothing. The four troop-stat glyphs mockup panel 5 draws have no " +
                             "enumerable home any more. FAIL, not a skip.");
                return 0;
            }
            for (int i = 0; i < keys.Length; i++)
                Require(keys[i], "stat-glyphs-resolve", "troop stat row glyph (mockup panel 5, row 5.3)", failures);
            log.AppendLine("stat glyph keys checked=" + keys.Length);
            return keys.Length;
        }

        /// <summary>
        /// The nine troop portraits.
        ///
        /// <para>⚠ THE FOLDER IS READ OUT OF THE VM SOURCE, NOT RETYPED. ManageScreenVM composes the
        /// key as <c>"RpgUi/troop/" + c.IconId</c> and that literal is the only place the folder is
        /// named; a copy of it here would be duplicated state and would keep passing after the VM
        /// moved the folder - the exact way this repo's most expensive bugs are all shaped
        /// (CLAUDE.md 2 / 5 / 16).</para>
        ///
        /// <para>⛔ THE IDS ALREADY CARRY THE `troop-` PREFIX AND MUST NOT BE PREPENDED TO.
        /// Measured 2026-09-06: troops.json authors <c>"iconId": "troop-footman"</c> for all nine,
        /// and the files are <c>troop-footman.png</c> ... so the composed key is
        /// <c>RpgUi/troop/troop-footman</c>, which is exactly the file. A seat "fixing" a missing
        /// prefix here would produce <c>troop-troop-footman</c> and break all nine at once. The
        /// 2026-09-06 blank-troop-tile report was a frame LAYER-ORDER defect (frame-tile's centre
        /// alpha measures 253/255 and was painted OVER the portrait), never a key defect - which is
        /// why the device log carried no art-miss line for a single troop.</para>
        ///
        /// RED: rename any file under Assets/Resources/RpgUi/troop/, or change an iconId.
        /// </summary>
        private static int CheckTroopPortraits(List<string> failures, StringBuilder log)
        {
            string vm = ReadText(VmPath, failures);
            if (vm == null) return 0;

            var match = Regex.Match(vm, @"IconId\s*=\s*string\.IsNullOrEmpty\(c\.IconId\)\s*\?\s*null\s*:\s*""([^""]+)""\s*\+\s*c\.IconId");
            if (!match.Success)
            {
                failures.Add("[troop-portrait] could not read the troop art folder out of " + VmPath +
                             " (ComposeTroopItem's `\"<folder>\" + c.IconId`). The suite refuses to GUESS the " +
                             "folder - a hardcoded copy here would keep passing after the VM moved it. Either " +
                             "the composition changed shape (update this regex in the same change) or the " +
                             "folder is no longer named in one place. FAIL, not a skip.");
                return 0;
            }
            string folder = match.Groups[1].Value;

            var troops = ReadJson(TroopsPath, failures);
            if (troops == null) return 0;
            var rows = troops["troops"] as JArray;
            if (rows == null || rows.Count == 0)
            {
                failures.Add("[troop-portrait] " + TroopsPath + " holds no troops[] - every Army tile would be blank");
                return 0;
            }

            int n = 0;
            foreach (var t in rows)
            {
                string id = (string)t["id"] ?? "?";
                string iconId = (string)t["iconId"];
                if (string.IsNullOrEmpty(iconId))
                {
                    failures.Add("[troop-portrait] troop '" + id + "' authors no iconId, so ComposeTroopItem " +
                                 "sets IconId=null and its Army tile renders the placeholder disc forever. " +
                                 "Author the iconId; do not let the View substitute a role glyph.");
                    continue;
                }
                Require(folder + iconId, "troop-portrait", "troop '" + id + "'", failures);
                n++;
            }
            log.AppendLine("troop keys checked=" + n + " under '" + folder + "'");
            return n;
        }

        /// <summary>
        /// Every ladder x every authored tier, keyed through the SAME function the VM calls, so a
        /// folder change moves both together and this suite can never assert a shape the game does
        /// not emit. Enumerated from building-tiers.json - no id list and no max-level array here
        /// (ManageBuildingsCardRegression hardcodes both, which is a second copy that must be
        /// hand-edited when a ladder grows; this one follows the data).
        ///
        /// ⛔ THIS IS THE CASE THAT WAS RED. See the RED PROOF block in this file's header: 20 of
        /// these 26 keys missed before BuildBuildingChoices was re-pointed.
        /// </summary>
        private static int CheckBuildingTierPortraits(List<string> failures, StringBuilder log)
        {
            var tiers = ReadJson(TiersPath, failures);
            if (tiers == null) return 0;
            var buildings = tiers["buildings"] as JArray;
            if (buildings == null || buildings.Count == 0)
            {
                failures.Add("[building-tier-portrait] " + TiersPath + " holds no buildings[]");
                return 0;
            }

            int n = 0;
            foreach (var b in buildings)
            {
                string id = (string)b["id"];
                if (string.IsNullOrEmpty(id)) continue;
                int max = 1;
                if (b["tiers"] is JArray ts)
                    foreach (var t in ts)
                    {
                        int tier = (int?)t["tier"] ?? 0;
                        if (tier > max) max = tier;
                    }

                for (int level = 1; level <= max; level++)
                {
                    Require(ManageArt.BuildingPortraitKey(id, level), "building-tier-portrait",
                            "'" + id + "' at level " + level + " of " + max, failures);
                    n++;
                }
            }
            log.AppendLine("building tier keys checked=" + n + " across " + buildings.Count + " ladder(s)");
            return n;
        }

        /// <summary>
        /// The placed-building projection must key its portrait through
        /// <see cref="ManageArt.BuildingPortraitKey"/> and must NOT have gone back to the mixed-root
        /// resolver.
        ///
        /// <para>⛔ WITHOUT THIS CASE THE SUITE CANNOT SEE THE BUG IT WAS WRITTEN FOR.
        /// [building-tier-portrait] calls <c>BuildingPortraitKey</c> itself, so it proves the FOLDER
        /// has art - never that the VM asks for that folder. Reverting the one line in
        /// BuildBuildingChoices would blank all 20 tier tiles again and leave that case green.</para>
        ///
        /// <para>Scoped to the BuildBuildingChoices BODY, not the whole file: ResolveBuildingPortraitKey
        /// legitimately survives elsewhere (the DEFENCE projection uses it, and tower art really does
        /// live in the root), so a file-wide Contains would fail on working code.</para>
        ///
        /// RED: restore `IconKey = ResolveBuildingPortraitKey(entry, id, level)`.
        /// </summary>
        private static void CheckVmUsesBuildingPortraitKey(List<string> failures, StringBuilder log)
        {
            string vm = ReadText(VmPath, failures);
            if (vm == null) return;

            string body = Body(vm, "private void BuildBuildingChoices()", "private static bool HasBuilderJob(");
            if (body == null)
            {
                failures.Add("[vm-uses-building-portrait-key] BuildBuildingChoices was not found in " + VmPath +
                             ", so the portrait-key pin could not be scoped. FAIL, not a skip - an unscoped pin " +
                             "silently guards nothing.");
                return;
            }

            if (!body.Contains("ManageArt.BuildingPortraitKey("))
                failures.Add("[vm-uses-building-portrait-key] BuildBuildingChoices no longer keys its portrait " +
                             "through ManageArt.BuildingPortraitKey. Every owned building above level 1 renders a " +
                             "BLANK tile in the Manage BUILD grid when it does not: the tile grid loads IconKey " +
                             "through ManageArt.LoadSprite with NO probe chain, and only Portraits/Buildings/ " +
                             "carries the tier sheets.");

            if (body.Contains("ResolveBuildingPortraitKey("))
                failures.Add("[vm-uses-building-portrait-key] BuildBuildingChoices is back on " +
                             "ResolveBuildingPortraitKey, which emits the MIXED \"Portraits/\" root. Measured " +
                             "2026-09-06: that root is missing ALL TWENTY tier keys (barracks-2..6, " +
                             "arcane-tower/armorer/farm/forge/lumbermill -2..-4) while Portraits/Buildings/ holds " +
                             "all 26. The root also mixes NPC art with structure art - see " +
                             "ManageScreenPanel.ManageBuildingPortraitGaps, which exists because six ids resolve a " +
                             "PERSON there. The resolver itself is fine and still serves the DEFENCE projection; " +
                             "it is wrong for BUILDINGS.");

            log.AppendLine("BuildBuildingChoices portrait-key pin checked");
        }

        /// <summary>Substring of <paramref name="source"/> between two markers; null if either is absent.</summary>
        private static string Body(string source, string from, string until)
        {
            int start = source.IndexOf(from, StringComparison.Ordinal);
            if (start < 0) return null;
            int end = source.IndexOf(until, start + from.Length, StringComparison.Ordinal);
            return end < 0 ? null : source.Substring(start, end - start);
        }

        /// <summary>
        /// The key an UNPLACED build row carries. BuildInventoryModel.Reconcile sets
        /// <c>ArtKey = e.manageArtKey</c> verbatim and ManageScreenVM.ComposeUnplacedItem passes
        /// it straight through to IconId, so the catalog value IS the Resources key - unmodified,
        /// unprefixed, unslugged. Reading the catalog therefore reads exactly what the tile loads.
        ///
        /// <para>⛔ OWNER RULING 2026-09-06 (Option A): the key is
        /// <c>Portraits/Buildings/&lt;catalog id&gt;</c>, built through the SAME function the VM calls,
        /// so this suite can never assert a shape the game does not emit. The catalog row is read only
        /// to enumerate WHICH ids the BUILD tab can offer a tile for.</para>
        ///
        /// <para>Every id carrying a manageArtKey is a row the tab can display, so every one of them
        /// must resolve or be on the dated exemption list. manageArtKey itself is no longer a key -
        /// it is the art-to-id join label the catalog note always said it was - and
        /// [unplaced-uses-building-portrait-key] is what stops it becoming one again.</para>
        /// </summary>
        private static int CheckUnplacedArtKeys(List<string> failures, StringBuilder log)
        {
            var catalog = ReadJson(CatalogPath, failures);
            if (catalog == null) return 0;
            var entries = catalog["entries"] as JArray;
            if (entries == null) { failures.Add("[unplaced-portrait] structures-catalog.json holds no entries[]"); return 0; }

            int n = 0, exempt = 0;
            foreach (var e in entries)
            {
                string id = (string)e["id"];
                // No manageArtKey => not player content (deco_torch, repair_default) => no Manage tile.
                if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty((string)e["manageArtKey"])) continue;
                n++;

                string key = ManageArt.BuildingPortraitKey(id, 0);
                if (ArtExemptions.ContainsKey(key)) { exempt++; continue; }
                Require(key, "unplaced-portrait", "'" + id + "' (BUILD tile, not yet placed)", failures);
            }
            log.AppendLine("build tile ids=" + n + " (" + exempt + " on the dated art exemption list)");
            return n - exempt;
        }

        /// <summary>
        /// ComposeUnplacedItem must key its portrait through <see cref="ManageArt.BuildingPortraitKey"/>
        /// and must NOT have gone back to <c>row.ArtKey</c>.
        ///
        /// <para>⛔ WITHOUT THIS CASE THE RULING IS UNENFORCED. CheckUnplacedArtKeys builds its keys by
        /// calling BuildingPortraitKey itself, so it proves the FOLDER has art - never that the VM asks
        /// for that folder. Restoring `IconId = row.ArtKey` would send every not-yet-built tile back to
        /// a bare Sheet-A name in the Resources root and leave that case green. Exactly the hole the
        /// building-tier case needed its own pin for; same shape, same reason.</para>
        ///
        /// RED: restore `IconId = row.ArtKey` in ComposeUnplacedItem.
        /// </summary>
        private static void CheckUnplacedUsesBuildingPortraitKey(List<string> failures, StringBuilder log)
        {
            string vm = ReadText(VmPath, failures);
            if (vm == null) return;

            string body = Body(vm, "private ManageItemState ComposeUnplacedItem(", "private ManageItemState ComposeBuildingItem(");
            if (body == null)
            {
                failures.Add("[unplaced-uses-building-portrait-key] ComposeUnplacedItem was not found in " + VmPath +
                             ", so the Option A pin could not be scoped. FAIL, not a skip.");
                return;
            }

            if (!body.Contains("ManageArt.BuildingPortraitKey("))
                failures.Add("[unplaced-uses-building-portrait-key] ComposeUnplacedItem no longer keys its portrait " +
                             "through ManageArt.BuildingPortraitKey, so a not-yet-built BUILD tile renders the " +
                             "placeholder disc. OWNER RULING 2026-09-06 (Option A): building art is ONE folder, " +
                             "Portraits/Buildings/, keyed by CATALOG ID.");

            if (body.Contains("row.ArtKey"))
                failures.Add("[unplaced-uses-building-portrait-key] ComposeUnplacedItem is back on row.ArtKey " +
                             "(= manageArtKey), a BARE Sheet-A tile name with no Resources folder. The catalog's " +
                             "own note calls it \"the Sheet A tile name for this row\" - a DELIVERY LABEL, not a " +
                             "Resources key - so Resources.Load looks in the Resources root, where no Manage art " +
                             "lives or should, and the tile blanks. This is the defect the owner captured on " +
                             "2026-09-06 (Healing Caravan / Echo Hollow / Crafting Station / Store).");

            log.AppendLine("ComposeUnplacedItem portrait-key pin checked");
        }

        /// <summary>
        /// Both directions, so the list cannot rot. An exemption whose key now RESOLVES is stale and
        /// must come out - a dead exemption is how the NEXT gap hides behind a green suite.
        /// RED: drop any building-*.png into a Resources folder that makes one of these resolve, and
        /// this case fires until its line (and the art request that owns it) is deleted.
        /// </summary>
        private static void CheckExemptionsStillAccurate(List<string> failures, StringBuilder log)
        {
            int stillAbsent = 0;
            foreach (var kv in ArtExemptions)
            {
                if (ManageArt.LoadSprite(kv.Key) == null) { stillAbsent++; continue; }
                failures.Add("[exemption-still-accurate] \"" + kv.Key + "\" is listed as a missing-art exemption " +
                             "but it now RESOLVES. Good news - delete the line, and delete it from the art request " +
                             "it came from. A stale exemption guards nothing and hides the next gap. (Recorded " +
                             "reason was: " + kv.Value + ")");
            }
            log.AppendLine("art exemptions still genuinely absent=" + stillAbsent + "/" + ArtExemptions.Count);
        }

        // ── helpers ───────────────────────────────────────────────────────────

        /// <summary>
        /// Resolves through <see cref="ManageArt.LoadSprite"/> - the PRODUCTION loader, including its
        /// Texture2D fallback - so a pass here means the game's own call succeeds, not that a file
        /// happens to sit on disk. On a miss the message says WHICH of the two remedies applies, by
        /// looking for a file with the same stem: present on disk means the IMPORT is wrong (a
        /// sprite/texture the loader cannot read); absent means it is an art request.
        /// </summary>
        private static void Require(string key, string caseName, string what, List<string> failures)
        {
            if (string.IsNullOrEmpty(key))
            {
                failures.Add("[" + caseName + "] " + what + " produced a NULL/EMPTY portrait key, so the tile " +
                             "renders the placeholder disc and ManageArt logs nothing (LoadSprite returns early " +
                             "on an empty key). A key that is never asked for is the one miss that does not log.");
                return;
            }
            if (ManageArt.LoadSprite(key) != null) return;

            failures.Add("[" + caseName + "] " + what + " -> Resources/" + key + " does NOT resolve. " +
                         OnDiskHint(key) +
                         " ⛔ The remedy is to fix the KEY or to request the ART - never to substitute another " +
                         "icon. A blank frame that logs once is honest; a wrong icon is a lie.");
        }

        /// <summary>Is there a file with this stem under Assets/Resources, in any format?</summary>
        private static string OnDiskHint(string key)
        {
            try
            {
                string root = Path.Combine(Directory.GetCurrentDirectory(), "Assets", "Resources");
                string rel = key.Replace('/', Path.DirectorySeparatorChar);
                string dir = Path.GetDirectoryName(Path.Combine(root, rel));
                string stem = Path.GetFileName(rel);
                if (dir != null && Directory.Exists(dir))
                {
                    var hits = Directory.GetFiles(dir, stem + ".*");
                    foreach (string hit in hits)
                    {
                        if (hit.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)) continue;
                        return "A FILE IS PRESENT (" + Path.GetFileName(hit) + ") but the loader cannot read it - " +
                               "this is an IMPORT defect (check the .meta textureType/spriteMode), not missing art.";
                    }
                    return "No file with that stem exists in that folder - either the KEY names the wrong folder " +
                           "(check whether the art sits elsewhere before concluding it is missing) or it is an " +
                           "ART REQUEST for the owner.";
                }
                return "The folder Assets/Resources/" + Path.GetDirectoryName(key)?.Replace('\\', '/') +
                       " does not exist, so this key could never resolve - the FOLDER is wrong, not the art.";
            }
            catch (Exception ex)
            {
                return "(on-disk hint unavailable: " + ex.Message + ")";
            }
        }

        private static JObject ReadJson(string relativePath, List<string> failures)
        {
            string text = ReadText(relativePath, failures);
            if (text == null) return null;
            try { return JObject.Parse(text); }
            catch (Exception ex)
            {
                failures.Add("[manage-portrait-coverage] could not parse " + relativePath + ": " + ex.Message);
                return null;
            }
        }

        private static string ReadText(string relativePath, List<string> failures)
        {
            string full = Path.Combine(Directory.GetCurrentDirectory(),
                relativePath.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(full)) return File.ReadAllText(full);
            failures.Add("[manage-portrait-coverage] source file missing: " + relativePath);
            return null;
        }
    }
}

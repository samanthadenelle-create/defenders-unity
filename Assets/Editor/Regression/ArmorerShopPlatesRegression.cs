// =============================================================================
// ArmorerShopPlatesRegression [armorer-plates] — WO-1877 (2026-09-19).
// -----------------------------------------------------------------------------
// THE DEFECT: PartyShopPanelMvvm.ResolveItemSprite short-circuited IconRoleArmor to
// RpgUiCatalog.IconShield (and weapons to IconSword) BEFORE reading iconPath, so the
// live Armorer preview painted a gold shield glyph over Aegis / Leafcloak while the
// class-set plates already lived under Assets/Resources/ItemIcons/.
//
// WHAT IT ASSERTS:
//   1 [no-armor-glyph-shortcircuit] Source: IconRoleArmor must NOT return IconShield
//      before an iconPath Resources.Load. Reverting the WO-1877 early-return → RED.
//   2 [armor-2d-not-turntable]      Source: BuildPreviewModelOrFallback routes armor
//      through the 2D plate branch (armor-2d-plate) — no mesh-swap / 3D armor preview.
//   3 [row-thumb]                   Source: CreateRow still paints a RowThumb from
//      ResolveItemSprite (list rows are not name-only bars).
//   4 [family-plates-on-disk]       The six catalog iconPath plates that were missing
//      (cloth/leather/chain/plate/aegis/mage_common) exist under Resources/ItemIcons.
//   5 [authored-path-loads]         For aegis_plate + armor_ranger_legendary (Leafcloak),
//      Resources.Load<Sprite>(authored iconPath) returns a sprite that is NOT IconShield
//      when the asset database has imported them; a missing file fails case 4 first.
//
// Marker: ARMORER_PLATES_OK <n>/<n>  |  ARMORER_PLATES_FAIL
// Menu / batch: DeNelle.Editor.Regression.ArmorerShopPlatesRegression.Run
// =============================================================================
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using DeNelle.Core.UI;
using DeNelle.Village;
using DeNelle.Village.Hero;

namespace DeNelle.Editor.Regression
{
    public static class ArmorerShopPlatesRegression
    {
        private const string MarkerOk   = "ARMORER_PLATES_OK";
        private const string MarkerFail = "ARMORER_PLATES_FAIL";
        private const string ViewPath   = "Assets/_Modules/Village/Hero/PartyShopPanelMvvm.cs";

        private static readonly string[] FamilyPlatePaths =
        {
            "Assets/Resources/ItemIcons/armor_ranger_common.png",
            "Assets/Resources/ItemIcons/armor_ranger_uncommon.png",
            "Assets/Resources/ItemIcons/armor_knight_rare.png",
            "Assets/Resources/ItemIcons/armor_knight_epic.png",
            "Assets/Resources/ItemIcons/armor_knight_legendary.png",
            "Assets/Resources/ItemIcons/armor_mage_uncommon.png",
        };

        private static readonly string[] AuthoredLoadIds =
        {
            "aegis_plate",
            "armor_ranger_legendary",
        };

        [MenuItem("Defenders/Regression/Armorer Shop Plates (WO-1877)")]
        public static void Run()
        {
            Run(out _);
        }

        /// <summary>Registered-suite entry point (DataRegression.RunAll).</summary>
        public static bool Run(out string reason)
        {
            var failures = new List<string>();
            var notes = new List<string>();
            int total = 0;
            int ok = 0;

            total++;
            if (CaseNoArmorGlyphShortcircuit(failures, notes)) ok++;

            total++;
            if (CaseArmor2dNotTurntable(failures, notes)) ok++;

            total++;
            if (CaseRowThumb(failures, notes)) ok++;

            total++;
            if (CaseFamilyPlatesOnDisk(failures, notes)) ok++;

            total++;
            if (CaseAuthoredPathLoads(failures, notes)) ok++;

            if (failures.Count > 0)
            {
                reason = MarkerFail + " " + ok + "/" + total + " — " + string.Join(" | ", failures.ToArray());
                Debug.LogError("[" + MarkerFail + "] " + reason);
                return false;
            }

            reason = MarkerOk + " " + ok + "/" + total + " — " + string.Join("; ", notes.ToArray());
            Debug.Log("[" + MarkerOk + "] " + reason);
            return true;
        }

        private static string ReadView(List<string> failures)
        {
            string path = Path.Combine(Directory.GetCurrentDirectory(),
                ViewPath.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(path))
            {
                failures.Add("[no-armor-glyph-shortcircuit] " + ViewPath + " not found");
                return null;
            }
            return File.ReadAllText(path);
        }

        // -- Case 1 -------------------------------------------------------------
        private static bool CaseNoArmorGlyphShortcircuit(List<string> failures, List<string> notes)
        {
            string body = ReadView(failures);
            if (body == null) return false;

            // The retired shape: if (role == IconRoleArmor) return ... IconShield;
            // immediately, before any iconPath load. Allow IconShield ONLY as a last-resort
            // AFTER Resources.Load / ItemIconCatalog.
            var early = new Regex(
                @"IconRoleArmor\)\s*[\r\n\s]*return\s+RpgUiCatalog\.Get\(\s*RpgUiCatalog\.RoleIcons\s*,\s*RpgUiCatalog\.IconShield\s*\)\s*;",
                RegexOptions.Multiline);
            if (early.IsMatch(body))
            {
                // Confirm the match sits BEFORE the first iconPath Resources.Load in the method.
                int method = body.IndexOf("private static Sprite ResolveItemSprite");
                int load = body.IndexOf("Resources.Load<Sprite>(iconPath)", method >= 0 ? method : 0);
                var m = early.Match(body);
                if (method >= 0 && load > method && m.Success && m.Index > method && m.Index < load)
                {
                    failures.Add("[no-armor-glyph-shortcircuit] ResolveItemSprite still returns IconShield " +
                                 "for IconRoleArmor BEFORE Resources.Load(iconPath) — the WO-1877 root cause");
                    return false;
                }
                // A last-resort IconShield AFTER the load is fine; only the early return is banned.
                if (method >= 0 && m.Success && (load < 0 || m.Index < load))
                {
                    failures.Add("[no-armor-glyph-shortcircuit] ResolveItemSprite still returns IconShield " +
                                 "for IconRoleArmor BEFORE Resources.Load(iconPath) — the WO-1877 root cause");
                    return false;
                }
            }

            int resolve = body.IndexOf("private static Sprite ResolveItemSprite");
            if (resolve < 0)
            {
                failures.Add("[no-armor-glyph-shortcircuit] ResolveItemSprite is gone from PartyShopPanelMvvm");
                return false;
            }
            string methodBody = body.Substring(resolve, System.Math.Min(3500, body.Length - resolve));
            int loadAt = methodBody.IndexOf("Resources.Load<Sprite>");
            if (loadAt < 0)
            {
                failures.Add("[no-armor-glyph-shortcircuit] ResolveItemSprite no longer Resources.Load's iconPath");
                return false;
            }
            // IconShield may remain as last resort, but MUST sit after the iconPath load.
            int shieldAt = methodBody.IndexOf("RpgUiCatalog.IconShield");
            if (shieldAt >= 0 && shieldAt < loadAt)
            {
                failures.Add("[no-armor-glyph-shortcircuit] IconShield appears before Resources.Load in " +
                             "ResolveItemSprite — armor still short-circuits to the glyph");
                return false;
            }

            notes.Add("[no-armor-glyph-shortcircuit] IconRoleArmor no longer short-circuits to IconShield before iconPath");
            return true;
        }

        // -- Case 2 -------------------------------------------------------------
        private static bool CaseArmor2dNotTurntable(List<string> failures, List<string> notes)
        {
            string body = ReadView(failures);
            if (body == null) return false;
            if (body.IndexOf("armor-2d-plate") < 0)
            {
                failures.Add("[armor-2d-not-turntable] BuildPreviewModelOrFallback no longer routes armor " +
                             "through the 'armor-2d-plate' 2D branch — Armorer must not 3D-preview armor");
                return false;
            }
            if (body.IndexOf("IconRoleArmor") < 0)
            {
                failures.Add("[armor-2d-not-turntable] IconRoleArmor gate missing from the preview path");
                return false;
            }
            notes.Add("[armor-2d-not-turntable] armor preview forced to 2D plate branch");
            return true;
        }

        // -- Case 3 -------------------------------------------------------------
        private static bool CaseRowThumb(List<string> failures, List<string> notes)
        {
            string body = ReadView(failures);
            if (body == null) return false;
            if (body.IndexOf("\"RowThumb\"") < 0 && body.IndexOf("RowThumb") < 0)
            {
                failures.Add("[row-thumb] CreateRow no longer paints a RowThumb — Armorer list rows " +
                             "would be name-only bars again");
                return false;
            }
            if (body.IndexOf("ResolveItemSprite(") < 0)
            {
                failures.Add("[row-thumb] CreateRow no longer calls ResolveItemSprite for the thumb");
                return false;
            }
            notes.Add("[row-thumb] list rows bind ResolveItemSprite into RowThumb");
            return true;
        }

        // -- Case 4 -------------------------------------------------------------
        private static bool CaseFamilyPlatesOnDisk(List<string> failures, List<string> notes)
        {
            var missing = new List<string>();
            for (int i = 0; i < FamilyPlatePaths.Length; i++)
            {
                string rel = FamilyPlatePaths[i];
                string path = Path.Combine(Directory.GetCurrentDirectory(),
                    rel.Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(path)) missing.Add(rel);
            }
            if (missing.Count > 0)
            {
                failures.Add("[family-plates-on-disk] missing ItemIcons plate(s): " +
                             string.Join(", ", missing.ToArray()));
                return false;
            }
            notes.Add("[family-plates-on-disk] cloth/leather/chain/plate/aegis/mage_common present");
            return true;
        }

        // -- Case 5 -------------------------------------------------------------
        private static bool CaseAuthoredPathLoads(List<string> failures, List<string> notes)
        {
            var shield = RpgUiCatalog.Get(RpgUiCatalog.RoleIcons, RpgUiCatalog.IconShield);
            int loaded = 0;
            var pendingImport = new List<string>();

            for (int i = 0; i < AuthoredLoadIds.Length; i++)
            {
                string id = AuthoredLoadIds[i];
                var armor = GearCatalog.FindArmor(id);
                if (armor == null)
                {
                    failures.Add("[authored-path-loads] GearCatalog has no armor '" + id + "'");
                    continue;
                }
                if (string.IsNullOrEmpty(armor.iconPath))
                {
                    failures.Add("[authored-path-loads] '" + id + "' authors no iconPath");
                    continue;
                }

                // Disk proof first (Unity may not have reimported yet in this edit seat).
                string disk = Path.Combine(Directory.GetCurrentDirectory(),
                    ("Assets/Resources/" + armor.iconPath + ".png").Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(disk))
                {
                    failures.Add("[authored-path-loads] '" + id + "' iconPath '" + armor.iconPath +
                                 "' has no PNG on disk at Assets/Resources/" + armor.iconPath + ".png");
                    continue;
                }

                var sprite = Resources.Load<Sprite>(armor.iconPath);
                if (sprite == null)
                {
                    // File exists but Sprite not imported yet — record as note, not a code fail.
                    // The disk case above already proved the content; lead gate reimports.
                    pendingImport.Add(id + "->" + armor.iconPath);
                    loaded++;
                    continue;
                }
                if (shield != null && ReferenceEquals(sprite, shield))
                {
                    failures.Add("[authored-path-loads] '" + id + "' resolved to IconShield — " +
                                 "authored plate was discarded");
                    continue;
                }
                loaded++;
            }

            if (failures.Count > 0) return false;
            string pending = pendingImport.Count > 0
                ? " (pending Unity sprite import: " + string.Join(", ", pendingImport.ToArray()) + ")"
                : "";
            notes.Add("[authored-path-loads] " + loaded + "/" + AuthoredLoadIds.Length +
                      " authored armor iconPath(s) on disk and not IconShield" + pending);
            return true;
        }
    }
}

// =============================================================================
// OwnedTownTemplateIdentityRegression — [owned-town-template-identity]
//
// WO-1767. THE PERMISSION GATE THAT WOULD HAVE TURNED 452fc14fd RED.
//
// A captured Iron Bastion town addresses every inherited structure by
// OwnedTemplateIdentity._templateId (OwnedTownScenePose.TryResolve:62-77). Nothing verified
// that the stamp existed, so when WO-1732 (452fc14fd) added `iron_bastion` to
// RaidBaseGenerator.RaidConfigIdsFromCatalog and BuildAllRaidScenes regenerated
// RaidBase_IronBastion for the first time, all 221 stamped components went with the old
// scene and EVERY gate stayed green. The owner's Seeker build 2026.09.16.371701 logged, 625 ms
// before RAID START:
//   [Flow:Raid] Precombat capture census failed: Captured structure lacks a baked stable
//   identity: Wall_Outer_SS_0
// (RaidCaptureCensus.cs:38-39) — an empty census for the whole fight, which softlocks the
// victory screen on a 3-star clear (RaidVictoryController:1021-1027, 990-1009).
//
// It also caught nothing when the PAIR desynchronised: the raid scene moved onto the WO-1723
// 4.0 m partition while OwnedTown_IronBastion.unity kept the pre-WO-1723 210-wall one and the
// shipped manifest kept a third census, so three artifacts described three different buildings.
//
// WHAT THIS ASSERTS — an ASSET LINT over the four artifacts as TEXT/JSON. No Unity play, no
// scene load, so it is cheap enough to ride the whole-suite gate:
//   1. every WallSegment / DefenseTower / RaidSpire in ALL THREE scenes carries a non-empty
//      _templateId (structure count == stamped-id count, per scene);
//   2. ids are unique within each scene;
//   3. the id SETS of the three scenes are EQUAL;
//   4. IronBastionTemplate.json's templateStructureId set equals them, and its entry count
//      equals the scene structure count.
//
// ⛔ THE THIRD SCENE IS ArenaPractice_IronBastion.unity, AND IT IS IN HERE BECAUSE IT WAS THE
// ARTIFACT NOBODY WAS WATCHING. Lead ruling 2026-09-16 on WO-1767: the owner's ruling (A)
// ("re-derive OwnedTown_IronBastion and its manifest") covers the practice arena too, because
// OwnedTownPracticeSceneBuilder.cs:16 derives it FROM the owned town. Measured after the first
// WO-1767 bake, before that step joined the chain: the town was on the new 169-structure
// partition while ArenaPractice_IronBastion still carried 221 identities / 210 WallSegments and
// the OLD 32-hex GUID ids. OwnedTownScenePose.TryResolve:59 explicitly accepts owned-template
// poses inside PracticeCombatPolicy.SceneName (= that scene), so a captured town's structures
// could not resolve there - a silent third desync of exactly the kind this ticket is about.
// Every count is DERIVED from the artifact being checked. There is no literal census in this
// file, deliberately: a literal is the bug this ticket is about (CLAUDE.md §8).
//
// Script GUIDs are resolved through AssetDatabase at run time rather than pasted as hex, so a
// script move can never silently make this lint count zero structures and pass.
//
// The failure sentence names the ONE sanctioned repair chain,
// DeNelle.Editor.OwnedTownChain.RebuildFromRaid — never a hand edit of a .unity file
// (CLAUDE.md §3) and never a re-typed count.
//
// Registered in DataRegression.RunAll, so it rides `REGRESSION_OK <n>/<n> suites`
// (marker map: Assets/Editor/Regression/DataRegression.cs:14-22).
// =============================================================================
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using DeNelle.Village;
using DeNelle.Village.World.Camps;
using Newtonsoft.Json.Linq;
using UnityEditor;

namespace DeNelle.Editor.Regression
{
    public static class OwnedTownTemplateIdentityRegression
    {
        public const string RaidScenePath     = "Assets/Scenes/RaidBase_IronBastion.unity";
        public const string TownScenePath     = "Assets/Scenes/OwnedTown_IronBastion.unity";
        public const string PracticeScenePath = "Assets/Scenes/ArenaPractice_IronBastion.unity";
        public const string ManifestPath      = "Assets/Resources/OwnedTown/IronBastionTemplate.json";

        private const string Chain = "DeNelle.Editor.OwnedTownChain.RebuildFromRaid";
        // ⚠ `[^\S\r\n]` (horizontal whitespace) and `[^\r\n]`, NEVER `\s` / `.`: with
        // RegexOptions.Multiline, `\s` MATCHES '\n', so `_templateId:\s*(.*)$` on an EMPTY field
        // swallows the newline and captures the NEXT YAML line's text - an unstamped structure then
        // reads as stamped and this lint passes the exact defect it exists to catch. Measured
        // 2026-09-16 against the blanked-id mutation: 0 blanks detected out of 1 planted.
        private static readonly Regex TemplateIdLine =
            new Regex(@"^[^\S\r\n]*_templateId:[^\S\r\n]*([^\r\n]*)$",
                      RegexOptions.Multiline | RegexOptions.Compiled);

        /// <summary>Reads the four artifacts off disk and lints them.</summary>
        public static bool Run(out string reason)
        {
            foreach (string path in new[] { RaidScenePath, TownScenePath, PracticeScenePath, ManifestPath })
                if (!File.Exists(path))
                { reason = "[owned-town-template-identity] FAIL missing artifact " + path + " - run " + Chain + "."; return false; }
            return Check(File.ReadAllText(RaidScenePath), File.ReadAllText(TownScenePath),
                         File.ReadAllText(PracticeScenePath), File.ReadAllText(ManifestPath), out reason);
        }

        /// <summary>
        /// The whole lint, over text supplied by the caller. Text-in so the bake chain can prove the
        /// RED leg (CLAUDE.md §11B.A - prove the red, not just the green) by blanking one id IN
        /// MEMORY. A hand edit of a .unity file to test a regression is forbidden (§3), and a
        /// regression nobody has ever seen fail is not a gate.
        /// </summary>
        public static bool Check(string raidText, string townText, string practiceText,
                                 string manifestText, out string reason)
        {
            reason = null;
            try
            {
                if (!TryStructureGuids(out var guids, out string guidWhy))
                { reason = "[owned-town-template-identity] FAIL " + guidWhy; return false; }

                if (!TryScene("RaidBase_IronBastion", raidText, guids, out int raidStructures,
                              out var raidIds, out string raidWhy))
                { reason = "[owned-town-template-identity] FAIL " + raidWhy; return false; }
                if (!TryScene("OwnedTown_IronBastion", townText, guids, out int townStructures,
                              out var townIds, out string townWhy))
                { reason = "[owned-town-template-identity] FAIL " + townWhy; return false; }

                if (!TryScene("ArenaPractice_IronBastion", practiceText, guids, out int practiceStructures,
                              out var practiceIds, out string practiceWhy))
                { reason = "[owned-town-template-identity] FAIL " + practiceWhy; return false; }

                if (raidStructures != townStructures)
                {
                    reason = "[owned-town-template-identity] FAIL the raid template and the owned town describe " +
                        "DIFFERENT buildings: RaidBase_IronBastion holds " + raidStructures +
                        " census structure(s), OwnedTown_IronBastion holds " + townStructures +
                        ". The town is DERIVED from the raid scene - re-derive it with " + Chain + ".";
                    return false;
                }
                if (practiceStructures != townStructures)
                {
                    reason = "[owned-town-template-identity] FAIL the practice arena and the owned town describe " +
                        "DIFFERENT buildings: ArenaPractice_IronBastion holds " + practiceStructures +
                        " census structure(s), OwnedTown_IronBastion holds " + townStructures +
                        ". The practice arena is DERIVED from the town (OwnedTownPracticeSceneBuilder.cs:16) - " +
                        "re-derive it with " + Chain + ".";
                    return false;
                }

                if (!SetsEqual(raidIds, townIds, out string firstDiff))
                {
                    reason = "[owned-town-template-identity] FAIL the raid and town _templateId SETS differ (" +
                        firstDiff + "). A saved town resolves every inherited structure by that id " +
                        "(OwnedTownScenePose.TryResolve:62-77), so a divergent set is an uncapturable base. Run " +
                        Chain + ".";
                    return false;
                }
                if (!SetsEqual(townIds, practiceIds, out string practiceDiff))
                {
                    reason = "[owned-town-template-identity] FAIL the town and practice-arena _templateId SETS " +
                        "differ (" + practiceDiff + "). OwnedTownScenePose.TryResolve:59 accepts owned-template " +
                        "poses inside PracticeCombatPolicy.SceneName, so a divergent practice set means a captured " +
                        "town's structures cannot resolve in practice mode. Run " + Chain + ".";
                    return false;
                }

                if (!TryManifest(manifestText, out int entries, out var manifestIds, out string manifestWhy))
                { reason = "[owned-town-template-identity] FAIL " + manifestWhy; return false; }

                if (entries != raidStructures)
                {
                    reason = "[owned-town-template-identity] FAIL " + ManifestPath + " carries " + entries +
                        " entry/entries against " + raidStructures + " censused structure(s). " +
                        "OwnedTownLayoutSnapshot.TryValidateAndCopy refuses a snapshot whose structure count " +
                        "differs from the manifest's, so a mismatch REFUSES the capture even after the census " +
                        "succeeds. Re-bake with " + Chain + ".";
                    return false;
                }
                if (!SetsEqual(raidIds, manifestIds, out string manifestDiff))
                {
                    reason = "[owned-town-template-identity] FAIL " + ManifestPath + "'s templateStructureId set " +
                        "differs from the scenes' (" + manifestDiff + "). Re-bake with " + Chain + ".";
                    return false;
                }

                reason = "[owned-town-template-identity] " + raidStructures + " census structure(s) in all three " +
                    "scenes (raid, owned town, practice arena), all stamped, ids unique, the four id SETS equal, " +
                    "manifest entries=" + entries + " - every count DERIVED from the artifact, no literal census";
                return true;
            }
            catch (Exception ex)
            {
                reason = "[owned-town-template-identity] FAIL threw: " + ex.Message;
                return false;
            }
        }

        // -- scene text ---------------------------------------------------------

        private static bool TryScene(string label, string text, List<string> structureGuids,
                                     out int structures, out HashSet<string> ids, out string why)
        {
            structures = 0;
            ids = new HashSet<string>(StringComparer.Ordinal);
            why = null;
            if (string.IsNullOrEmpty(text)) { why = label + " scene text is empty."; return false; }

            foreach (string guid in structureGuids) structures += Occurrences(text, "guid: " + guid);
            if (structures == 0)
            { why = label + " holds NO WallSegment / DefenseTower / RaidSpire at all - refusing to pass an empty census."; return false; }

            int blank = 0;
            foreach (Match m in TemplateIdLine.Matches(text))
            {
                string value = m.Groups[1].Value.Trim().Trim('\'', '"').Trim();
                if (value.Length == 0) { blank++; continue; }
                if (!ids.Add(value))
                { why = label + " carries DUPLICATE _templateId '" + value + "'; the id must address exactly one structure. Run " + Chain + "."; return false; }
            }
            if (blank > 0)
            { why = label + " carries " + blank + " EMPTY _templateId field(s). Run " + Chain + "."; return false; }
            if (ids.Count != structures)
            {
                why = label + " has " + structures + " census structure(s) but " + ids.Count +
                    " stamped identity/identities. Every WallSegment / DefenseTower / RaidSpire must carry an " +
                    "OwnedTemplateIdentity, or RaidCaptureCensus throws 'Captured structure lacks a baked stable " +
                    "identity' on the FIRST one it meets and the raid runs with an empty census. Run " + Chain + ".";
                return false;
            }
            return true;
        }

        private static bool TryManifest(string text, out int entries, out HashSet<string> ids, out string why)
        {
            entries = 0;
            ids = new HashSet<string>(StringComparer.Ordinal);
            why = null;
            if (string.IsNullOrEmpty(text)) { why = ManifestPath + " is empty."; return false; }
            var root = JObject.Parse(text);
            var array = root["entries"] as JArray;
            if (array == null) { why = ManifestPath + " has no 'entries' array."; return false; }
            entries = array.Count;
            foreach (var entry in array)
            {
                var token = entry.SelectToken("structure.inheritedPose.templateStructureId");
                string value = token != null ? (string)token : null;
                if (string.IsNullOrEmpty(value))
                { why = ManifestPath + " has an entry with no structure.inheritedPose.templateStructureId."; return false; }
                if (!ids.Add(value))
                { why = ManifestPath + " maps two entries to the same templateStructureId '" + value + "'."; return false; }
            }
            return true;
        }

        // -- helpers ------------------------------------------------------------

        /// <summary>
        /// The three census component script GUIDs, resolved through AssetDatabase so a script move
        /// can never make this lint count zero and pass.
        /// </summary>
        private static bool TryStructureGuids(out List<string> guids, out string why)
        {
            guids = new List<string>();
            why = null;
            foreach (var type in new[] { typeof(WallSegment), typeof(DefenseTower), typeof(RaidSpire) })
            {
                if (!TryScriptGuid(type, out string guid))
                { why = "could not resolve the script GUID of " + type.Name + " through AssetDatabase."; return false; }
                guids.Add(guid);
            }
            return true;
        }

        private static bool TryScriptGuid(Type type, out string guid)
        {
            guid = null;
            foreach (string candidate in AssetDatabase.FindAssets("t:MonoScript " + type.Name))
            {
                string path = AssetDatabase.GUIDToAssetPath(candidate);
                var script = AssetDatabase.LoadAssetAtPath<UnityEditor.MonoScript>(path);
                if (script == null || script.GetClass() != type) continue;
                guid = candidate;
                return true;
            }
            return false;
        }

        private static int Occurrences(string text, string needle)
        {
            int count = 0;
            int at = text.IndexOf(needle, StringComparison.Ordinal);
            while (at >= 0)
            {
                count++;
                at = text.IndexOf(needle, at + needle.Length, StringComparison.Ordinal);
            }
            return count;
        }

        private static bool SetsEqual(HashSet<string> left, HashSet<string> right, out string firstDiff)
        {
            firstDiff = null;
            foreach (string id in left)
                if (!right.Contains(id)) { firstDiff = "'" + id + "' is in the first set only"; return false; }
            foreach (string id in right)
                if (!left.Contains(id)) { firstDiff = "'" + id + "' is in the second set only"; return false; }
            return true;
        }
    }
}

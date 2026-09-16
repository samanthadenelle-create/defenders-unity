// =============================================================================
// ArcaneSpireArtAuthorityRegression [arcane-spire-art]
// -----------------------------------------------------------------------------
// Assembly: DeNelle.EditorRegression. Namespace: DeNelle.Editor.Regression.
// Markers: ARCANE_SPIRE_ART_OK / ARCANE_SPIRE_ART_FAIL.
// Registered ONCE in DataRegression.RunAll. NEVER throws.
//
// THE INCIDENT THIS GUARDS (owner APK felt-test, 2026-09-15). Verbatim:
//   "The arcane spire is using the backups, not the actual correct ones.
//    The middle tier was upside down; top and bottom tiers were fine."
//
// MECHANISM -- the same shape as the Tripo watchtower masquerade that commit
// 1fec556d3 fixed on 2026-09-02, one structure over:
//   * SyntyStructureRetheme's Map re-pointed ArcaneSpire_1/_2/_3 at Synty presets
//     (a tower preset, a castle WALL tower, a church), and the generated wrapper
//     prefabs under Assets/StructureContent/Synty/ reused the owner's filenames
//     verbatim. Structure_Art.asset therefore carried the SAME address twice --
//     her .fbx and the wrapper -- and Addressables resolved to whichever the built
//     catalog listed first.
//   * A group edit dated 2026-09-13 21:33 removed 24 duplicate addresses and kept
//     the WRAPPER half of each spire pair, so the 2026-09-15 APK had nothing left
//     to resolve to but the stand-ins.
//   * The MIDDLE tier read upside down specifically because ArcaneSpire_2 was
//     mapped onto a castle wall SEGMENT tower, whose authored pose is not a
//     free-standing tower's. Tiers 1 and 3 were merely the wrong building.
// There is no local fallback for structure art (CLAUDE.md section 16), so this
// failed SILENTLY: the build installed, launched and played, wearing other art.
//
// WHAT THIS ASSERTS (four independent rules, none of which needs Unity to run a
// scene -- they are deterministic editor-only asset and source reads):
//   RULE 1  Every Addressable address whose leaf starts with "ArcaneSpire" has
//           EXACTLY ONE claimant across ALL groups. The double-claim IS the bug:
//           two owners means the shipped catalog picks, not the repo.
//   RULE 2  That claimant resolves to the OWNER'S asset under
//           Assets/StructureContent -- <leaf>.fbx for the three model addresses,
//           <leaf>.png for the three albedo addresses -- and to nothing else.
//           Anything under .../Synty/ or outside StructureContent is a FAIL.
//   RULE 3  The three addresses structures-catalog.json actually loads for
//           tower_arcane_spire (visualPrefabPath + upgradeVisualPath[0..1]) are
//           present in that set, so the rule cannot pass by checking nothing.
//   RULE 4  SyntyStructureRetheme.cs does not name ArcaneSpire in Map, Composed
//           or CompletionLeaves (a re-run would re-create the wrappers and
//           silently revert this fix -- no error, no gate failure), AND still
//           pins the three leaves in OriginalStorefrontGuids with the GUIDs the
//           owner's .fbx files actually carry.
//   RULE 5  Assets/StructureContent/Synty/ArcaneSpire_{1,2,3}.prefab are GONE
//           from disk, so nothing can re-claim an address by being re-added.
//
// SCOPE IS DELIBERATELY NARROW. "Structures/arcane tower" (the Cathedral of
// Learning storefront, note the space) and "Structures/ArcaneTower_Albedo" are a
// DIFFERENT structure and are NOT in scope -- the leaf filter is an Ordinal
// StartsWith on "ArcaneSpire", which excludes both. Widening it would put this
// suite in the way of the owner's separately-ruled storefront art.
//
// WHY RULE 4 IS A SOURCE-TEXT SCAN AND NOT A CALL. DeNelle.EditorRegression's
// asmdef does not reference DeNelle.Editor (read at
// Assets/Editor/Regression/DeNelle.EditorRegression.asmdef this session), so
// SyntyStructureRetheme's private tables are not reachable from here. The scan is
// therefore structural, not a grep for a word: it extracts each initializer body
// by brace matching and looks only inside Map / Composed / CompletionLeaves --
// which is why the RCA comment block in that file (which quotes the deleted rows
// verbatim, on purpose) does not trip it, and why the OriginalStorefrontGuids
// pin, which DOES name the leaves, is asserted in the opposite direction.
//
// HOLLOW-PASS GUARD: if AddressableAssetSettings is missing, or the retheme source
// cannot be read, the suite stands down via RegressionOutcome.Skip and says it
// ASSERTED NOTHING. A pass on an empty address set would be a hollow green.
//
// POSITIVE CONTROL (prove it can go red): re-point Structures/ArcaneSpire_2 in
// Structure_Art.asset at GUID e7a9f1020edfad347be71f29521bc5ed (the deleted Synty
// wrapper) -- RULE 2 must name the address and the offending path. Re-adding the
// Map row { "ArcaneSpire_2", "Castle/SM_Bld_Castle_Wall_Tower_L_01.prefab" } must
// make RULE 4 red.
//
// SHIPPING: this change re-points Addressable content, so the build CANNOT ship
// without tools\r2-ship.ps1 (CLAUDE.md section 16 -- bundle names are
// content-hashed, a missing push fails SILENTLY). Judge by R2_PUSH_OK +
// R2_PARITY_OK on a FRESH log, never an exit code.
// =============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using DeNelle.Core;
using DeNelle.Core.Diagnostics;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;

namespace DeNelle.Editor.Regression
{
    public static class ArcaneSpireArtAuthorityRegression
    {
        private const string FlowSys    = "ArcaneSpireArt";
        private const string MarkerOk   = "ARCANE_SPIRE_ART_OK";
        private const string MarkerFail = "ARCANE_SPIRE_ART_FAIL";
        private const string Tag        = "arcane-spire-art";

        private const string AddressPrefix = "Structures/";
        private const string SpireLeafPrefix = "ArcaneSpire";

        private const string RethemeRelPath = "Assets/Editor/SyntyStructureRetheme.cs";
        /// <summary>StreamingAssets-relative, read through CanonicalJson like every other
        /// catalog reader -- NOT a re-typed "Assets/..." path literal (AssetRootsRegression
        /// RULE 1 forbids those, and the dual-copy means a raw path is wrong anyway).</summary>
        private const string CatalogRel     = "Data/Canonical/structures-catalog.json";
        private const string WrapperDir     = AssetRoots.StructureContent + "/Synty";

        /// <summary>The addresses structures-catalog.json loads for tower_arcane_spire.
        /// Read at source 2026-09-15: visualPrefabPath plus upgradeVisualPath[0..1].
        /// RULE 3 proves these are still the live ones by re-reading the catalog.</summary>
        private static readonly string[] ModelAddresses =
        {
            "Structures/ArcaneSpire_1",
            "Structures/ArcaneSpire_2",
            "Structures/ArcaneSpire_3"
        };

        /// <summary>Leaf -> the GUID the owner's .fbx carries, read from
        /// Assets/StructureContent/ArcaneSpire_{1,2,3}.fbx.meta on 2026-09-15.
        /// SyntyStructureRetheme.OriginalStorefrontGuids must still carry these,
        /// or its restore path no longer knows how to repair the group.</summary>
        private static readonly Dictionary<string, string> OwnerFbxGuids =
            new Dictionary<string, string>(StringComparer.Ordinal)
        {
            { "ArcaneSpire_1", "fdbdc8e2c3cad234eb116332a5148b42" },
            { "ArcaneSpire_2", "d8478f8e20a9a3c44a3a22e5405764d0" },
            { "ArcaneSpire_3", "0d652e1697cad5143b40baa0d598bf49" }
        };

        public static void RunStandalone()
        {
            string reason;
            bool pass = Run(out reason);
            Debug.Log("[" + Tag + "] standalone result: " + (pass ? "PASS" : "FAIL") + " - " + reason);
        }

        public static bool Run(out string reason)
        {
            try { return RunCore(out reason); }
            catch (Exception ex)
            {
                reason = Tag + ": oracle threw " + ex.GetType().Name + ": " + ex.Message;
                Debug.LogError(MarkerFail + " - " + reason);
                return false;
            }
        }

        private static bool RunCore(out string reason)
        {
            using var _scope = FlowTrace.Enter(FlowSys, "ArcaneSpireArtAuthority.RunCore");

            var failures = new List<string>();
            var partials = new List<string>();
            int assertions = 0;

            string projectRoot = Directory.GetParent(Application.dataPath).FullName;

            // ---- gather every claimant of every Structures/ArcaneSpire* address ----
            var settings = AddressableAssetSettingsDefaultObject.Settings;
            if (settings == null)
            {
                FlowTrace.Warn(FlowSys, "AddressableAssetSettings missing");
                return RegressionOutcome.Skip(out reason, Tag,
                    "AddressableAssetSettings missing -- the spire address owners could not be enumerated");
            }

            // address -> list of "guid => assetPath (group)" claimants, across ALL groups.
            var claimants = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            var claimantPaths = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            var groups = settings.groups;
            if (groups != null)
            {
                foreach (var group in groups)
                {
                    if (group == null || group.entries == null) continue;
                    foreach (var entry in group.entries)
                    {
                        if (entry == null || string.IsNullOrEmpty(entry.address)) continue;
                        if (!entry.address.StartsWith(AddressPrefix, StringComparison.Ordinal)) continue;
                        string leaf = entry.address.Substring(AddressPrefix.Length);
                        if (!leaf.StartsWith(SpireLeafPrefix, StringComparison.Ordinal)) continue;

                        string path = AssetDatabase.GUIDToAssetPath(entry.guid);
                        if (!claimants.ContainsKey(entry.address))
                        {
                            claimants[entry.address] = new List<string>();
                            claimantPaths[entry.address] = new List<string>();
                        }
                        claimants[entry.address].Add(entry.guid + " => " +
                            (string.IsNullOrEmpty(path) ? "<unresolved guid>" : path) +
                            " (group " + group.Name + ")");
                        claimantPaths[entry.address].Add(path ?? string.Empty);
                    }
                }
            }

            // ---- RULE 3 runs BEFORE the empty-set stand-down, on purpose -----------
            // The case that matters most is "another dedup removed the spire addresses
            // entirely" -- which produces an EMPTY claimant set. If the stand-down came
            // first, that real defect would report as a Skip. So: if the catalog still
            // authors an address and nothing claims it, that is a FAIL, whatever the set
            // size. Only "the catalog does not author it either" reaches the Skip below.
            bool catalogReadable = false;
            string catalogText = ReadCatalogOrEmpty(out catalogReadable);
            if (!catalogReadable)
            {
                partials.Add(RegressionOutcome.PartialSkip("structures-catalog",
                    CatalogRel + " unreadable -- could not confirm the live spire addresses"));
            }
            else
            {
                foreach (string address in ModelAddresses)
                {
                    assertions++;
                    if (catalogText.IndexOf("\"" + address + "\"", StringComparison.Ordinal) < 0)
                        failures.Add("RULE 3 structures-catalog.json no longer authors '" + address +
                                     "'. The catalog moved and this suite is now guarding a dead address -- " +
                                     "re-read tower_arcane_spire's visualPrefabPath / upgradeVisualPath and " +
                                     "update ModelAddresses, do NOT delete the rule.");
                    else if (!claimants.ContainsKey(address))
                        failures.Add("RULE 3 the catalog loads '" + address +
                                     "' but no Addressable group claims it -- it would resolve to nothing " +
                                     "on device, and with no local fallback for structure art that fails " +
                                     "SILENTLY (CLAUDE.md section 16).");
                }
            }

            if (claimants.Count == 0)
            {
                if (failures.Count > 0)
                {
                    FlowTrace.Fail(FlowSys, "no spire address claimed, and the catalog still asks for them");
                    reason = Tag + " FAIL (" + failures.Count + " finding(s); NO Structures/ArcaneSpire* " +
                             "address is claimed by any Addressable group): " +
                             string.Join(" | ", failures.ToArray());
                    Debug.LogError(MarkerFail + " - " + reason);
                    return false;
                }
                FlowTrace.Warn(FlowSys, "no Structures/ArcaneSpire* address found at all");
                return RegressionOutcome.Skip(out reason, Tag,
                    "no Structures/ArcaneSpire* address exists in any Addressable group AND the catalog " +
                    "no longer authors one -- asserting on an empty set would be a hollow green");
            }

            // ---- RULE 1 + RULE 2 ------------------------------------------------
            foreach (var pair in claimants)
            {
                string address = pair.Key;
                string leaf = address.Substring(AddressPrefix.Length);
                var owners = pair.Value;
                assertions++;

                if (owners.Count != 1)
                {
                    failures.Add("RULE 1 '" + address + "' has " + owners.Count +
                                 " claimant(s), must have exactly 1 -- a double-claim lets the BUILT catalog " +
                                 "pick the art instead of the repo (this is how the Synty stand-in shipped): " +
                                 string.Join(" | ", owners.ToArray()));
                    continue;
                }

                string actual = claimantPaths[address][0];
                string expectedFbx = AssetRoots.StructureContent + "/" + leaf + ".fbx";
                string expectedPng = AssetRoots.StructureContent + "/" + leaf + ".png";
                bool isAlbedo = leaf.EndsWith("_Albedo", StringComparison.Ordinal);
                string expected = isAlbedo ? expectedPng : expectedFbx;

                if (!string.Equals(actual, expected, StringComparison.Ordinal))
                {
                    failures.Add("RULE 2 '" + address + "' resolves to '" +
                                 (string.IsNullOrEmpty(actual) ? "<unresolved>" : actual) +
                                 "' but the owner's art is '" + expected +
                                 "'. The Arcane Spire is HER Tripo art (owner ruling 2026-09-15); " +
                                 "a Synty wrapper or any other asset behind this address is the defect, " +
                                 "not a substitution.");
                    continue;
                }

                if (!File.Exists(Path.Combine(projectRoot, expected)))
                    failures.Add("RULE 2 '" + address + "' names '" + expected +
                                 "' but that file is not on disk");
            }

            // ---- RULE 4: the retheme cannot re-create the wrappers ---------------
            string rethemeAbs = Path.Combine(projectRoot, RethemeRelPath);
            if (!File.Exists(rethemeAbs))
            {
                partials.Add(RegressionOutcome.PartialSkip("retheme-source",
                    RethemeRelPath + " not found -- could not assert the re-theme map"));
            }
            else
            {
                string src = ReadOrEmpty(rethemeAbs);
                assertions += CheckRethemeSource(src, failures);
            }

            // ---- RULE 5: the wrapper prefabs are gone from disk ------------------
            for (int i = 1; i <= 3; i++)
            {
                assertions++;
                string rel = WrapperDir + "/" + SpireLeafPrefix + "_" + i + ".prefab";
                if (File.Exists(Path.Combine(projectRoot, rel)))
                    failures.Add("RULE 5 '" + rel + "' still exists. It was deleted on the owner's " +
                                 "2026-09-15 ruling; while it is on disk it can re-claim the address.");
            }

            // ---- verdict ---------------------------------------------------------
            if (failures.Count > 0)
            {
                FlowTrace.Fail(FlowSys, "offenders=" + failures.Count + " across " + claimants.Count + " address(es)");
                reason = Tag + " FAIL (" + failures.Count + " finding(s); " + claimants.Count +
                         " spire address(es), " + assertions + " assertion(s)): " +
                         string.Join(" | ", failures.ToArray());
                Debug.LogError(MarkerFail + " - " + reason);
                return false;
            }

            string extra = partials.Count > 0 ? " " + string.Join(" ", partials.ToArray()) : "";
            FlowTrace.Step(FlowSys, "clean: addresses=" + claimants.Count + " assertions=" + assertions +
                                    " partials=" + partials.Count);
            reason = Tag + " OK - " + claimants.Count + " Structures/ArcaneSpire* address(es), " +
                     assertions + " assertion(s): each has exactly one claimant, each resolves to the " +
                     "owner's asset under " + AssetRoots.StructureContent +
                     ", the re-theme names none of them, and the Synty wrappers are gone." + extra;
            Debug.Log(MarkerOk + " - " + reason);
            return true;
        }

        // =====================================================================
        // RULE 4 -- structural scan of SyntyStructureRetheme.cs
        // ---------------------------------------------------------------------
        // NOT a grep for the word. That file deliberately QUOTES the deleted rows
        // in its RCA comment (the precedent, commit 1fec556d3, records the deleted
        // watchtower rows the same way) so nobody re-adds them by reflex, and it
        // NAMES the three leaves in OriginalStorefrontGuids because that table is
        // the pin. A word-grep would fire on both. So: extract each initializer
        // body by brace matching from its declaration, and assert per table.
        // =====================================================================
        private static int CheckRethemeSource(string src, List<string> failures)
        {
            int assertions = 0;

            assertions += CheckTableExcludes(src, "Dictionary<string, string> Map", "Map", failures);
            assertions += CheckTableExcludes(src, "Dictionary<string, ComposedPart[]> Composed", "Composed", failures);
            assertions += CheckTableExcludes(src, "string[] CompletionLeaves", "CompletionLeaves", failures);

            // The positive half: the pin must still be there, with the right GUIDs.
            string pin = ExtractInitializer(src, "Dictionary<string, string> OriginalStorefrontGuids");
            if (pin == null)
            {
                failures.Add("RULE 4 could not locate OriginalStorefrontGuids in " + RethemeRelPath +
                             " -- the table that pins the owner's art is gone or was renamed");
                return assertions + 1;
            }

            foreach (var pair in OwnerFbxGuids)
            {
                assertions++;
                var row = new Regex("\\{\\s*\"" + Regex.Escape(pair.Key) + "\"\\s*,\\s*\"([0-9a-f]{32})\"\\s*\\}",
                                    RegexOptions.CultureInvariant);
                var m = row.Match(pin);
                if (!m.Success)
                {
                    failures.Add("RULE 4 OriginalStorefrontGuids no longer pins '" + pair.Key +
                                 "'. Without that row AssertOriginalStorefrontExclusions stops throwing on a " +
                                 "re-add and RestoreOriginalStorefronts can no longer repair Structure_Art.");
                    continue;
                }
                if (!string.Equals(m.Groups[1].Value, pair.Value, StringComparison.Ordinal))
                    failures.Add("RULE 4 OriginalStorefrontGuids pins '" + pair.Key + "' to GUID '" +
                                 m.Groups[1].Value + "' but the owner's " + pair.Key + ".fbx carries '" +
                                 pair.Value + "' -- the pin would fail closed on a source it cannot find.");
            }

            // And the identity the pin claims must still be true of the project.
            foreach (var pair in OwnerFbxGuids)
            {
                assertions++;
                string path = AssetRoots.StructureContent + "/" + pair.Key + ".fbx";
                string guid = AssetDatabase.AssetPathToGUID(path);
                if (!string.Equals(guid, pair.Value, StringComparison.Ordinal))
                    failures.Add("RULE 4 '" + path + "' now carries GUID '" +
                                 (string.IsNullOrEmpty(guid) ? "<none>" : guid) +
                                 "', not the pinned '" + pair.Value +
                                 "' -- the owner's FBX was replaced, moved or re-imported.");
            }

            return assertions;
        }

        private static int CheckTableExcludes(string src, string declaration, string label, List<string> failures)
        {
            string body = ExtractInitializer(src, declaration);
            if (body == null)
            {
                failures.Add("RULE 4 could not locate " + label + " in " + RethemeRelPath +
                             " -- the re-theme table was renamed, so this rule is no longer guarding it");
                return 1;
            }
            // Strip comments FIRST. The file deliberately quotes the deleted rows in-comment
            // (see the method header); only a live string literal is a re-add.
            if (StripComments(body).IndexOf("\"" + SpireLeafPrefix, StringComparison.Ordinal) >= 0)
                failures.Add("RULE 4 " + label + " in " + RethemeRelPath + " names " + SpireLeafPrefix +
                             " again. A run would re-generate the Synty wrapper prefabs and silently revert " +
                             "the owner's art (no error, no gate failure) -- exactly how the 2026-09-15 APK " +
                             "shipped a castle wall tower as the middle tier.");
            return 1;
        }

        /// <summary>The text between the first '{' after <paramref name="declaration"/> and its
        /// matching '}'. Brace-matched, comment- and string-aware enough for these tables
        /// (they contain no braces inside literals). Null when the declaration is absent.</summary>
        private static string ExtractInitializer(string src, string declaration)
        {
            if (string.IsNullOrEmpty(src)) return null;
            int decl = src.IndexOf(declaration, StringComparison.Ordinal);
            if (decl < 0) return null;
            // The pair is declared together so the naive whole-file brace counter in CLAUDE.md
            // section 1 (which has no char-literal model) still reads this file balanced.
            const char OpenBrace = '{', CloseBrace = '}';
            int open = src.IndexOf(OpenBrace, decl);
            if (open < 0) return null;

            int depth = 0;
            bool inLine = false, inBlock = false, inString = false;
            for (int i = open; i < src.Length; i++)
            {
                char c = src[i];
                char next = i + 1 < src.Length ? src[i + 1] : '\0';

                if (inLine) { if (c == '\n') inLine = false; continue; }
                if (inBlock) { if (c == '*' && next == '/') { inBlock = false; i++; } continue; }
                if (inString)
                {
                    if (c == '\\') { i++; continue; }
                    if (c == '"') inString = false;
                    continue;
                }
                if (c == '/' && next == '/') { inLine = true; i++; continue; }
                if (c == '/' && next == '*') { inBlock = true; i++; continue; }
                if (c == '"') { inString = true; continue; }

                if (c == OpenBrace) depth++;
                else if (c == CloseBrace)
                {
                    depth--;
                    if (depth == 0) return src.Substring(open, i - open + 1);
                }
            }
            return null;
        }

        /// <summary>The same state machine as ExtractInitializer, emitting everything that is
        /// NOT a // or /* */ comment. String literals survive intact, which is the point:
        /// a re-added table row is a literal, an RCA note is not.</summary>
        private static string StripComments(string src)
        {
            if (string.IsNullOrEmpty(src)) return string.Empty;
            var sb = new System.Text.StringBuilder(src.Length);
            bool inLine = false, inBlock = false, inString = false;
            for (int i = 0; i < src.Length; i++)
            {
                char c = src[i];
                char next = i + 1 < src.Length ? src[i + 1] : '\0';

                if (inLine) { if (c == '\n') { inLine = false; sb.Append(c); } continue; }
                if (inBlock) { if (c == '*' && next == '/') { inBlock = false; i++; } continue; }
                if (inString)
                {
                    sb.Append(c);
                    if (c == '\\') { if (i + 1 < src.Length) { sb.Append(next); i++; } continue; }
                    if (c == '"') inString = false;
                    continue;
                }
                if (c == '/' && next == '/') { inLine = true; i++; continue; }
                if (c == '/' && next == '*') { inBlock = true; i++; continue; }
                if (c == '"') { inString = true; sb.Append(c); continue; }
                sb.Append(c);
            }
            return sb.ToString();
        }

        private static string ReadOrEmpty(string path)
        {
            try { return File.ReadAllText(path); }
            catch { return string.Empty; }
        }

        /// <summary>The canonical structures catalog, read through the same seam every other
        /// catalog reader uses (Resources dual-copy or StreamingAssets). <paramref name="readable"/>
        /// is false when it resolved to nothing, so an unreadable catalog reports as a
        /// PARTIAL-SKIP rather than silently asserting nothing.</summary>
        private static string ReadCatalogOrEmpty(out bool readable)
        {
            readable = false;
            try
            {
                string text = CanonicalJson.Read(CatalogRel);
                readable = !string.IsNullOrEmpty(text);
                return text ?? string.Empty;
            }
            catch { return string.Empty; }
        }
    }
}

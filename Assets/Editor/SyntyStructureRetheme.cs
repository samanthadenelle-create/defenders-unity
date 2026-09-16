// =============================================================================
// SyntyStructureRetheme — WO-1291. Swap the ART behind each Structures/* address.
// -----------------------------------------------------------------------------
// Owner ruling 2026-09-13: the original Tripo castle storefronts are correct.
// The HubStructureVisualInjector swap addresses are protected below; earlier
// claims of approval for their Synty substitutions are superseded by this ruling.
// Owner ruling 2026-09-15: the ArcaneSpire_{1,2,3} tiers join that protected set.
// ⛔ DO NOT WRITE THE SIZE OF THAT SET HERE — read OriginalStorefrontGuids. This line
// said "nine" until the spire leaves landed, and the count rotted the same day.
//
// ⛔ THE KEY DECISION, AND WHY IT IS THE SAFE ONE.
// We do NOT touch structures-catalog.json. Its 27 `visualPrefabPath` values and — far
// more importantly — its `id` strings are LIVE SAVE KEYS (memory
// structure-role-enum-and-format-normalization); renaming one silently orphans every
// player's building. Instead we keep every `Structures/*` ADDRESS exactly as it is and
// re-point the address at a new prefab. The catalog, the save format, VisualFactory and
// every caller are untouched; only the mesh behind the address changes.
//
// ⚠ THE ADDRESS SET IS THE AUTHORITY, NOT THIS TABLE. Structure_Art holds 38 addresses;
// seven of them are TEXTURES (*_Albedo, *_Tex/*) and are deliberately absent below — a
// texture has no prefab to swap. Anything unmapped is REPORTED, never silently skipped,
// so the gap is visible rather than discovered on a device.
//
// ASSIGNMENT PROVENANCE (updated 2026-09-13): the original storefront substitutions
// are removed. Watermill and GenericContainer / CrystalMine / IronMine remain in
// completion scope. Change approved recipes, never the live address strings.
//
// ⛔ SHIPPING: every run of this re-hashes the Addressable content, so the build CANNOT
// ship without tools\r2-ship.ps1 (CLAUDE.md §16 — content-hashed bundles, a missing push
// fails SILENTLY with placeholder buildings and no on-screen error; it has happened four
// times). Judge by R2_PUSH_OK + R2_PARITY_OK on a FRESH log, never the exit code.
//
// Batchmode: DeNelle.Editor.SyntyStructureRetheme.Run
// Menu:      Defenders/Art/Re-theme Structures to Synty
// Marker:    STRUCTURE_RETHEME_OK / STRUCTURE_RETHEME_FAIL
// =============================================================================
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System;
using System.Security.Cryptography;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;
using DeNelle.Core;
using Object = UnityEngine.Object;

namespace DeNelle.Editor
{
    public static class SyntyStructureRetheme
    {
        private const string Synty   = "Assets/Synty/PolygonFantasyKingdom/Prefabs/";
        // ⚠ DERIVED FROM AssetRoots, never re-typed. A second copy of a relocatable root is
        // how a relocation misses a call site, and the miss is SILENT — the builder just
        // quietly loads nothing. AssetRootsRegression enforces this and caught the literal.
        private static readonly string OutDir = AssetRoots.StructureContent + "/Synty";
        private const string StructureLayerName = "Structure";

        /// <summary>address leaf -> Synty prefab, relative to <see cref="Synty"/>.</summary>
        private static readonly Dictionary<string, string> Map = new Dictionary<string, string>
        {
            // ── storefronts / town buildings ───────────────────────────────────
            // OriginalStorefrontGuids owns the nine original Tripo storefronts.
            { "ShopAndCrafting",      "Buildings/Presets/SM_Bld_Preset_Tavern_01_Optimized.prefab" },
            { "House_Medieval_Medium","Buildings/Presets/SM_Bld_Preset_House_01_A_Optimized.prefab" },
            { "Windmill_Medieval",    "Buildings/Presets/SM_Bld_Preset_House_Windmill_01_Optimized.prefab" },
            // Watermill_Medieval is COMPOSED (house + waterwheel) -- see Composed below,
            // not this table. It was sharing the Windmill preset, which read as a duplicate.

            // -- previously-unmapped set, closed 2026-09-01 (owner ruling: wood pallet
            //    for the generic container; KayKit mine for both mines -- differentiating
            //    dressing for IronMine is deferred to WO-1292). These sources live OUTSIDE
            //    the Synty root, so they are authored as absolute "Assets/..." paths -- see
            //    ResolveSourcePath. Both KayKit families import with bakeAxisConversion:0
            //    and remap to URP/Lit materials (verified at source 2026-09-01).
            { "GenericContainer",     "Assets/Models/KayKit/KayKit Resource Bits 1.0/Assets/fbx(unity)/Pallet_Wood_Covered_A.fbx" },
            { "CrystalMine",          "Assets/Models/KayKit/KayKit Medieval Hexagon Pack 1.0.1/Assets/fbx(unity)/buildings/green/building_mine_green.fbx" },
            { "IronMine",             "Assets/Models/KayKit/KayKit Medieval Hexagon Pack 1.0.1/Assets/fbx(unity)/buildings/green/building_mine_green.fbx" },

            // ── arcane line: the spire tiers are the OWNER'S OWN ART. DO NOT RE-THEME THEM. ──
            //
            // ⛔ THE THREE ROWS THAT USED TO LIVE HERE ARE DELETED ON AN OWNER RULING, and
            // re-adding them silently reverts her art. They were:
            //     ArcaneSpire_1 -> Buildings/Presets/SM_Bld_Preset_Tower_01_Optimized.prefab
            //     ArcaneSpire_2 -> Castle/SM_Bld_Castle_Wall_Tower_L_01.prefab
            //     ArcaneSpire_3 -> Buildings/Presets/SM_Bld_Preset_Church_01_B_Optimized.prefab
            //
            // OWNER RULING 2026-09-15 (APK felt-test), verbatim: "The arcane spire is using the
            // backups, not the actual correct ones. The middle tier was upside down; top and
            // bottom tiers were fine."
            //
            // THE MIDDLE TIER IS THE TELL. ArcaneSpire_2 was mapped onto a Synty castle WALL
            // TOWER — a wall SEGMENT piece whose authored pose is not a free-standing tower's —
            // so tier 2 read upside down while tiers 1 and 3 (a tower preset and a church) read
            // merely wrong rather than inverted. That is the symptom she reported, in order.
            //
            // MECHANISM, identical to the watchtower incident below: the generated wrapper
            // prefabs reused her filenames verbatim, so Structure_Art.asset carried the SAME
            // address twice — her FBX and the Synty wrapper — and Addressables resolved to
            // whichever the built catalog listed first. An UNCOMMITTED group edit dated
            // 2026-09-13 21:33 then removed 24 duplicate addresses and kept the WRAPPER half of
            // each spire pair, so the 2026-09-15 APK shipped the stand-ins with nothing to
            // resolve against. The group is repaired and the three leaves are pinned in
            // OriginalStorefrontGuids below, so AssertOriginalStorefrontExclusions THROWS if
            // anyone re-adds a row here.
            //
            // ⚠ THE KEY WITH A SPACE IN IT IS A DIFFERENT STRUCTURE: the live address
            // "Structures/arcane tower" is an original storefront ('Cathedral of Learning'),
            // not one of these spire tiers. A first pass keyed it "arcane" because the
            // diagnostic that dumped the address list split on whitespace and silently
            // truncated it. Addresses are free text — never assume they are token-shaped.

            // ── defence: the archer tower is the OWNER'S OWN ART. DO NOT RE-THEME IT. ──
            //
            // ⛔ THE THREE ROWS THAT USED TO LIVE HERE ARE DELETED ON AN OWNER RULING, and
            // re-adding them silently reverts her art. They were:
            //     Tower_Wooden_Watchtower    -> Castle/SM_Bld_Castle_Wall_Tower_S_01.prefab
            //     Tower_Wooden_Watchtower_L2 -> Castle/SM_Bld_Castle_Wall_Tower_M_01.prefab
            //     Tower_Wooden_Watchtower_L3 -> Castle/SM_Bld_Castle_Wall_Tower_L_01.prefab
            //
            // OWNER RULING 2026-09-02, verbatim: "one thing i hate is the changes to the archer
            // towers. can you bring my wooden towers i created in tripo?" and, on the replacements
            // specifically: "yes i hate those round towers".
            //
            // Tower_Wooden_Watchtower{,_L2,_L3} are HER assets - Tripo-authored, each .fbx carrying
            // a sibling .fbx.tripo-extracted marker. This table mapped them onto a Synty stone
            // castle WALL TOWER size ladder, and because the generated wrapper prefabs reused her
            // filenames verbatim, the swap was invisible: Structure_Art.asset ended up with the
            // SAME address claimed twice (her prefab and the stone wrapper), so Addressables
            // resolved to whichever the built catalog listed first. That is precisely how a stone
            // tower shipped wearing her wooden tower's name.
            //
            // ⚠ THIS FILE IS THE SOURCE OF THAT DEFECT, NOT A VICTIM OF IT. Re-running
            // SyntyStructureRetheme.Run with those rows present re-creates the wrappers and undoes
            // the fix in structures-catalog.json + Structure_Art.asset, with no error and no gate
            // failure - the owner would find it herself in a felt-test, which is the outcome the
            // whole F8/oracle apparatus exists to prevent.
            //
            // The catalog now points tower_ground_archer at Structures/Tower_Wooden_Watchtower{,_L2,
            // _L3}, and FoundingReachabilityRegression asserts that in BOTH directions - it fails if
            // her ladder goes missing AND fails if the Polyperfect stone family reappears. The three
            // Synty stone towers survive under their own honest addresses
            // (Structures/Synty_Tower_Castle_Wall_S/_M/_L) and remain available to anything that
            // genuinely wants a stone wall tower.
            //
            // ⭐ Her ruling of the same day - "the other synty were on purpose" - means the REST of
            // this table stood THEN. The 2026-09-13 storefront ruling above supersedes it.

            // ── perimeter pieces (same kit as the WO-1290 castle ring) ─────────
            { "Wall_Medieval_Stone",  "Castle/SM_Bld_Castle_Wall_01.prefab" },
            { "Wall_Medieval_Wood",   "Castle/SM_Bld_Castle_Hoarding_Wood_Wall_01.prefab" },
            { "Gate_Medieval_Medium", "Castle/SM_Bld_Castle_Wall_Gate_01.prefab" },

            // ── siege: real art, replacing the polyperfect stand-ins ───────────
            { "Catapult",             "SiegeEngines/SM_Wep_Catapult_01.prefab" },
            { "Ballista",             "SiegeEngines/SM_Wep_Ballista_Mobile_01.prefab" },
            { "Ballista_L1",          "SiegeEngines/SM_Wep_Ballista_Mobile_01.prefab" },
            { "Ballista_L2",          "SiegeEngines/SM_Wep_Ballista_Mounted_01.prefab" },
            { "Ballista_L3",          "SiegeEngines/SM_Wep_Trebuchet_01.prefab" },

            // ── props ──────────────────────────────────────────────────────────
            { "Well",                 "Props/SM_Prop_Well_01.prefab" },
            { "Torche_Wall",          "Props/SM_Prop_Torch_01.prefab" },
            { "HealingCaravan",       "Vehicles/SM_Veh_TraderWagon_01.prefab" },
        };

        /// <summary>One child of a composed wrapper. Path resolves like a Map value
        /// (Synty-relative, or absolute when it starts with "Assets/").</summary>
        private sealed class ComposedPart
        {
            public readonly string  Path;
            public readonly Vector3 LocalPos;
            public readonly Vector3 LocalEuler;
            /// <summary>When true the part ignores LocalPos and is wall-mounted on the
            /// FIRST part's +X face from measured bounds -- see MountOnSide.</summary>
            public readonly bool    AutoMountSide;
            public ComposedPart(string path, Vector3 pos, Vector3 euler, bool autoMountSide = false)
            { Path = path; LocalPos = pos; LocalEuler = euler; AutoMountSide = autoMountSide; }
        }

        /// <summary>
        /// address leaf -> multi-part wrapper recipe. Checked BEFORE Map: an address is
        /// either single-model or composed, never both. Part 0 is the anchor at the origin.
        /// </summary>
        private static readonly Dictionary<string, ComposedPart[]> Composed = new Dictionary<string, ComposedPart[]>
        {
            // Watermill (owner re-pick 2026-09-01): House_08 body + the Castle kit
            // waterwheel hung on one wall, replacing the Windmill-preset duplicate.
            // PLACEHOLDER MOUNT pending screenshot verify: the wheel's seat is COMPUTED
            // from measured renderer bounds at build time (vertical wheel flat against the
            // +X wall face, slightly embedded, bottom near ground) rather than authored as
            // literals -- no seat has eyeballed this composition yet. If the screenshots
            // read wrong, author explicit LocalPos/LocalEuler here and drop AutoMountSide.
            { "Watermill_Medieval", new[]
                {
                    new ComposedPart("Buildings/Presets/SM_Bld_Preset_House_08_Optimized.prefab", Vector3.zero, Vector3.zero),
                    new ComposedPart("Castle/SM_Bld_Waterwheel_01.prefab", Vector3.zero, Vector3.zero, autoMountSide: true),
                } },
        };

        /// <summary>Map/Composed values are Synty-relative by default; a value that already
        /// starts with "Assets/" is an absolute project path (the KayKit sources).</summary>
        private static string ResolveSourcePath(string rel)
            => rel.StartsWith("Assets/") ? rel : Synty + rel;

        /// <summary>
        /// True when the address currently points at something that is NOT a prefab — a
        /// texture, a material. There is nothing to swap for those.
        /// ⚠ DETECTED BY ASSET TYPE, NOT BY A NAME LIST. A hand-written list of texture
        /// addresses had to spell out a base-colour texture suffix, re-typing EnemyArtPaths'
        /// BaseColorSuffix token — the art-ledger oracle rejects a re-typed naming token
        /// because a literal at a call site cannot be re-pointed, traced, or asserted. It
        /// would also go stale the moment a new texture address was added. Asking the
        /// AssetDatabase what the thing IS has neither problem.
        /// </summary>
        private static bool IsNonPrefabAddress(AddressableAssetEntry entry)
        {
            if (entry == null) return true;
            string path = AssetDatabase.GUIDToAssetPath(entry.guid);
            if (string.IsNullOrEmpty(path)) return true;
            return AssetDatabase.LoadAssetAtPath<GameObject>(path) == null;
        }

        // WO-1291 completion, narrowed by the 2026-09-13 original-storefront ruling.
        // Only the missing wrappers and composed Watermill remain. Recipes stay in Map/Composed.
        // Do not add watchtowers: the owner's Tripo ladder and the separately addressed Synty
        // stone towers are protected by the unchanged-address contract below.
        private static readonly string[] CompletionLeaves =
        {
            "GenericContainer", "CrystalMine", "IronMine", "Watermill_Medieval"
        };

        // Identity pins verified against HubStructureVisualInjector.Swaps and the original
        // StructureContent/*.fbx.meta files on 2026-09-13. A replaced/moved source fails closed.
        // ⛔ DO NOT WRITE A COUNT FOR THIS TABLE ANYWHERE — read the table. It said "nine" in the
        // file header and in RestoreOriginalStorefronts' summary until 2026-09-15, when the three
        // ArcaneSpire leaves were added and both copies went stale in the same edit.
        private static readonly Dictionary<string, string> OriginalStorefrontGuids = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            // Owner ruling 2026-09-15 (APK felt-test): the Arcane Spire tiers are her Tripo art,
            // not the Synty stand-ins. GUIDs read from Assets/StructureContent/ArcaneSpire_{1,2,3}
            // .fbx.meta this session. Pinning them here makes AssertOriginalStorefrontExclusions
            // throw on any re-add to Map/Composed/CompletionLeaves, and lets
            // RestoreOriginalStorefronts repair Structure_Art if an address is ever re-hijacked.
            { "ArcaneSpire_1", "fdbdc8e2c3cad234eb116332a5148b42" },
            { "ArcaneSpire_2", "d8478f8e20a9a3c44a3a22e5405764d0" },
            { "ArcaneSpire_3", "0d652e1697cad5143b40baa0d598bf49" },
            { "PetHouse2", "82950f9e84e76ce478bc6d84967bf5d9" },
            { "arcane tower", "f70a5ba1f6656fe4fb83272ea292424e" },
            { "Forge", "edc9e21a5960f7e4f8d84543eae60a5b" },
            { "armorer", "dd1a409803987a94791b857d7e72f003" },
            { "store", "ba5bdaa758c7ee94db84dcb7269162d7" },
            { "jeweler", "0a7c9cb08bbabf746b74f27fb8340631" },
            { "lumbermill", "0225e10fc70c9dd49a8548d2a016cf50" },
            { "farm", "21dfa815591a82f44987371dda478366" },
            { "barracks", "5a258590045b55041aa22a4362144c9d" }
        };

        private static void AssertOriginalStorefrontExclusions()
        {
            foreach (string leaf in OriginalStorefrontGuids.Keys)
                if (Map.ContainsKey(leaf) || Composed.ContainsKey(leaf) || CompletionLeaves.Contains(leaf))
                    throw new InvalidOperationException("original storefront cannot be re-themed: Structures/" + leaf);
        }

        private static readonly string[] ProtectedTowerLeaves =
        {
            "Tower_Wooden_Watchtower", "Tower_Wooden_Watchtower_L2", "Tower_Wooden_Watchtower_L3",
            "Synty_Tower_Castle_Wall_S", "Synty_Tower_Castle_Wall_M", "Synty_Tower_Castle_Wall_L"
        };

        private static string WrapperPath(string leaf) => OutDir + "/" + leaf.Replace('/', '_') + ".prefab";

        private static Dictionary<string, string[]> Snapshot(AddressableAssetGroup group)
        {
            return group.entries.GroupBy(e => e.address, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.Select(e => e.guid).OrderBy(x => x, StringComparer.Ordinal).ToArray(),
                              StringComparer.Ordinal);
        }

        /// <summary>Pure postcondition shared by completion and its adversarial oracle.</summary>
        public static void AssertCompletionResolution(Dictionary<string, string[]> before,
            Dictionary<string, string[]> after, Dictionary<string, string> owned)
        {
            if (ProtectedTowerLeaves.Any(leaf => owned.ContainsKey("Structures/" + leaf)))
                throw new InvalidOperationException("completion cannot own protected tower addresses");
            if (OriginalStorefrontGuids.Keys.Any(leaf => owned.ContainsKey("Structures/" + leaf)))
                throw new InvalidOperationException("completion cannot own original storefront addresses");
            if (!before.Keys.OrderBy(x => x, StringComparer.Ordinal).SequenceEqual(
                    after.Keys.OrderBy(x => x, StringComparer.Ordinal)))
                throw new InvalidOperationException("address keys changed");
            foreach (var pair in after)
            {
                if (pair.Value == null || pair.Value.Length != 1 || string.IsNullOrEmpty(pair.Value[0]))
                    throw new InvalidOperationException("address is not uniquely resolved: " + pair.Key);
                if (owned.TryGetValue(pair.Key, out string guid))
                {
                    if (pair.Value[0] != guid)
                        throw new InvalidOperationException("wrong completion wrapper: " + pair.Key);
                }
                else if (!before[pair.Key].SequenceEqual(pair.Value))
                    throw new InvalidOperationException("untouched address changed: " + pair.Key);
            }
            if (owned.Keys.Any(key => !before.ContainsKey(key)))
                throw new InvalidOperationException("completion claimed an unknown address");
        }

        private static string FileDigest(string path)
        {
            using (var sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path)));
        }

        /// <summary>Restore only the keys listed in OriginalStorefrontGuids. No prefab/model,
        /// scene, catalog, importer or gameplay state is authored by this operation.</summary>
        [MenuItem("Defenders/Art/Restore original Tripo storefront addresses")]
        public static void RestoreOriginalStorefronts()
        {
            int changed = 0;
            bool writing = false;
            try
            {
                AssertOriginalStorefrontExclusions();
                var settings = AddressableAssetSettingsDefaultObject.Settings;
                if (settings == null) throw new InvalidOperationException("Addressable settings missing");
                var groups = settings.groups.Where(g => g != null).ToArray();
                var group = groups.SingleOrDefault(g => g.Name == "Structure_Art");
                if (group == null) throw new InvalidOperationException("Structure_Art missing");
                // Do not accidentally persist another editor task's pending group/settings edits.
                if (EditorUtility.IsDirty(group) || EditorUtility.IsDirty(settings))
                    throw new InvalidOperationException("Structure_Art/settings already dirty; save or resolve those edits first");
                var before = groups.ToDictionary(g => g, Snapshot);
                var desired = OriginalStorefrontGuids.ToDictionary(p => "Structures/" + p.Key, p => p.Value, StringComparer.Ordinal);
                var labels = new Dictionary<string, string[]>(StringComparer.Ordinal);
                foreach (var pair in OriginalStorefrontGuids)
                {
                    string key = "Structures/" + pair.Key;
                    string path = AssetRoots.StructureContent + "/" + pair.Key + ".fbx";
                    if (!File.Exists(path) || !File.Exists(path + ".meta") ||
                        AssetDatabase.AssetPathToGUID(path) != pair.Value || AssetDatabase.GUIDToAssetPath(pair.Value) != path)
                        throw new InvalidOperationException("original FBX identity missing/mismatched: " + path);
                    var model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                    if (model == null || model.GetComponentsInChildren<Renderer>(true).Length == 0)
                        throw new InvalidOperationException("original FBX has no renderable model: " + path);
                    var claimants = groups.SelectMany(g => g.entries).Where(e => e.address == key).ToArray();
                    if (claimants.Length == 0 || claimants.Any(e => e.parentGroup != group || e.ReadOnly))
                        throw new InvalidOperationException("key missing, read-only or claimed across groups: " + key);
                    // A GUID already assigned to another key must never be moved or renamed.
                    if (groups.SelectMany(g => g.entries).Any(e => e.guid == pair.Value &&
                            (e.parentGroup != group || e.address != key || e.ReadOnly)))
                        throw new InvalidOperationException("original GUID already owns another key/group: " + key);
                    // RemoveAssetEntry searches by GUID: disallow duplicate GUID owners anywhere.
                    foreach (var entry in claimants)
                        if (groups.SelectMany(g => g.entries).Count(e => e.guid == entry.guid) != 1)
                            throw new InvalidOperationException("ambiguous claimant GUID: " + key + " / " + entry.guid);
                    labels[key] = claimants.SelectMany(e => e.labels).Distinct(StringComparer.Ordinal).ToArray();
                }
                var pending = desired.Where(p => !before[group][p.Key].SequenceEqual(new[] { p.Value })).ToArray();
                if (pending.Length > 0 && group.ReadOnly)
                    throw new InvalidOperationException("Structure_Art is read-only");
                Debug.Log("RESTORE_ORIGINAL_STOREFRONTS_PREFLIGHT addresses=" + desired.Count + " pending=" + pending.Length);
                foreach (var pair in pending)
                {
                    writing = true;
                    var entry = settings.CreateOrMoveEntry(pair.Value, group, false, false);
                    if (entry == null) throw new InvalidOperationException("cannot assign original: " + pair.Key);
                    entry.SetAddress(pair.Key, false);
                    foreach (string label in labels[pair.Key]) entry.SetLabel(label, true, false, false);
                    foreach (var old in group.entries.Where(e => e.address == pair.Key && e.guid != pair.Value).ToArray())
                        if (!settings.RemoveAssetEntry(old.guid, false))
                            throw new InvalidOperationException("cannot remove superseded address claimant: " + pair.Key);
                    changed++;
                }
                AssertOriginalStorefrontResolution(before, groups, group, desired);
                if (changed > 0)
                {
                    EditorUtility.SetDirty(group);
                    EditorUtility.SetDirty(settings);
                    AssetDatabase.SaveAssetIfDirty(group);
                    AssetDatabase.SaveAssetIfDirty(settings);
                }
                AssertOriginalStorefrontResolution(before, groups, group, desired);
                Debug.Log("RESTORE_ORIGINAL_STOREFRONTS_OK changed=" + changed + " addresses=" + desired.Count +
                          "; stable keys and all untouched address owners verified");
            }
            catch (Exception ex)
            {
                Debug.LogError("RESTORE_ORIGINAL_STOREFRONTS_FAIL changed=" + changed + " " + ex.GetType().Name + ": " + ex.Message +
                    (writing ? "; partial address edits may remain; review Structure_Art/settings before retry" : "; preflight refused, no writes started"));
            }
        }

        private static void AssertOriginalStorefrontResolution(Dictionary<AddressableAssetGroup, Dictionary<string, string[]>> before,
            AddressableAssetGroup[] groups, AddressableAssetGroup target, Dictionary<string, string> desired)
        {
            foreach (var group in groups)
            {
                var after = Snapshot(group);
                if (!before[group].Keys.OrderBy(k => k, StringComparer.Ordinal).SequenceEqual(after.Keys.OrderBy(k => k, StringComparer.Ordinal)))
                    throw new InvalidOperationException("address keys changed in " + group.Name);
                foreach (var pair in after)
                {
                    var expected = group == target && desired.TryGetValue(pair.Key, out string guid)
                        ? new[] { guid } : before[group][pair.Key];
                    if (!pair.Value.SequenceEqual(expected))
                        throw new InvalidOperationException("unexpected address ownership: " + group.Name + " / " + pair.Key);
                }
            }
        }

        /// <summary>Finish only the known unfinished recipes and remove duplicate ownership.
        /// All source and ownership checks precede the first write. A failed build reports partial
        /// output explicitly and never emits a success; the CLI reviews those paths before gating.
        /// No scene, source asset, catalog id or unrelated generated wrapper is rewritten.</summary>
        [MenuItem("Defenders/Art/Complete approved structure re-theme")]
        public static void RunCompletion()
        {
            bool writing = false;
            try
            {
                AssertOriginalStorefrontExclusions();
                var settings = AddressableAssetSettingsDefaultObject.Settings;
                if (settings == null) throw new InvalidOperationException("Addressable settings missing");
                var group = settings.groups.SingleOrDefault(g => g != null && g.Name == "Structure_Art");
                if (group == null) throw new InvalidOperationException("Structure_Art missing");
                int layer = LayerMask.NameToLayer(StructureLayerName);
                if (layer < 0) throw new InvalidOperationException("Structure layer missing");
                if (!AssetDatabase.IsValidFolder(OutDir)) throw new InvalidOperationException("wrapper folder missing: " + OutDir);
                var before = Snapshot(group);
                var selected = new HashSet<string>(CompletionLeaves, StringComparer.Ordinal);
                foreach (string leaf in ProtectedTowerLeaves)
                {
                    string key = "Structures/" + leaf;
                    if (!before.ContainsKey(key) || before[key].Length != 1 || selected.Contains(leaf) || Map.ContainsKey(leaf) || Composed.ContainsKey(leaf))
                        throw new InvalidOperationException("protected tower contract changed: " + key);
                }
                // Preflight EVERY source before generating even the first wrapper.
                foreach (string leaf in CompletionLeaves)
                {
                    if (!before.ContainsKey("Structures/" + leaf))
                        throw new InvalidOperationException("completion address missing: " + leaf);
                    string[] paths;
                    if (Composed.TryGetValue(leaf, out var parts)) paths = parts.Select(p => p.Path).ToArray();
                    else if (Map.TryGetValue(leaf, out string single)) paths = new[] { single };
                    else throw new InvalidOperationException("approved recipe missing: " + leaf);
                    foreach (string rel in paths)
                    {
                        string path = ResolveSourcePath(rel);
                        var source = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                        if (source == null || source.GetComponentsInChildren<Renderer>(true).Length == 0)
                            throw new InvalidOperationException("source has no renderable model: " + path);
                    }
                }
                // Only duplicate addresses already owned by an approved wrapper are deduplicated.
                // No last-write-wins choice: the wrapper's asset GUID must already be one claimant.
                var owned = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var pair in before.Where(p => p.Value.Length > 1))
                {
                    if (!pair.Key.StartsWith("Structures/", StringComparison.Ordinal))
                        throw new InvalidOperationException("unowned duplicate address: " + pair.Key);
                    string leaf = pair.Key.Substring("Structures/".Length);
                    if (!Map.ContainsKey(leaf) && !Composed.ContainsKey(leaf))
                        throw new InvalidOperationException("unapproved duplicate address: " + pair.Key);
                    string path = WrapperPath(leaf);
                    string guid = AssetDatabase.AssetPathToGUID(path);
                    if (string.IsNullOrEmpty(guid) || !pair.Value.Contains(guid) ||
                        AssetDatabase.LoadAssetAtPath<GameObject>(path) == null)
                        throw new InvalidOperationException("duplicate lacks canonical wrapper claimant: " + pair.Key);
                    owned.Add(pair.Key, guid);
                }
                var existingSelectedGuids = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (string leaf in CompletionLeaves)
                {
                    string path = WrapperPath(leaf);
                    string guid = AssetDatabase.AssetPathToGUID(path);
                    if (File.Exists(path) && (string.IsNullOrEmpty(guid) || !before["Structures/" + leaf].Contains(guid)))
                        throw new InvalidOperationException("existing completion wrapper is not an address claimant: " + leaf);
                    if (File.Exists(path)) existingSelectedGuids[leaf] = guid;
                }
                var catalogs = new[] { "Assets/Resources/Data/Canonical/structures-catalog.json",
                                       "Assets/StreamingAssets/Data/Canonical/structures-catalog.json" };
                var preserved = catalogs.ToDictionary(p => p, FileDigest, StringComparer.Ordinal);
                foreach (string path in Directory.GetFiles(OutDir, "*.prefab*"))
                {
                    string file = Path.GetFileName(path);
                    if (CompletionLeaves.Any(leaf => file == leaf + ".prefab" || file == leaf + ".prefab.meta")) continue;
                    preserved[path] = FileDigest(path);
                }
                Debug.Log("STRUCTURE_COMPLETION_PREFLIGHT entries=" + group.entries.Count +
                          " addresses=" + before.Count + " recipes=" + CompletionLeaves.Length +
                          " duplicateAddresses=" + owned.Count + "; sources and ownership verified before writes");
                writing = true;
                foreach (string leaf in CompletionLeaves)
                {
                    string path = WrapperPath(leaf);
                    GameObject built;
                    if (Composed.TryGetValue(leaf, out var parts)) built = BuildComposedWrapper(parts, path, layer);
                    else built = BuildWrapper(AssetDatabase.LoadAssetAtPath<GameObject>(ResolveSourcePath(Map[leaf])), path, layer);
                    if (built == null || built.GetComponent<BoxCollider>() == null || built.layer != layer)
                        throw new InvalidOperationException("generated wrapper lacks collider/layer: " + path);
                    string guid = AssetDatabase.AssetPathToGUID(path);
                    if (string.IsNullOrEmpty(guid)) throw new InvalidOperationException("generated wrapper has no GUID: " + path);
                    if (existingSelectedGuids.TryGetValue(leaf, out string priorGuid) && priorGuid != guid)
                        throw new InvalidOperationException("existing wrapper GUID changed: " + path);
                    owned["Structures/" + leaf] = guid;
                }
                foreach (var pair in owned)
                {
                    var entry = settings.CreateOrMoveEntry(pair.Value, group, false, false);
                    if (entry == null) throw new InvalidOperationException("cannot assign wrapper: " + pair.Key);
                    entry.address = pair.Key;
                    foreach (var old in group.entries.Where(e => e.address == pair.Key && e.guid != pair.Value).ToList())
                        settings.RemoveAssetEntry(old.guid, false);
                }
                AssertCompletionResolution(before, Snapshot(group), owned);
                foreach (var pair in preserved)
                    if (FileDigest(pair.Key) != pair.Value)
                        throw new InvalidOperationException("out-of-scope bytes changed: " + pair.Key);
                EditorUtility.SetDirty(group);
                EditorUtility.SetDirty(settings);
                AssetDatabase.SaveAssets();
                AssertCompletionResolution(before, Snapshot(group), owned);
                foreach (var pair in preserved)
                    if (FileDigest(pair.Key) != pair.Value)
                        throw new InvalidOperationException("out-of-scope bytes changed during save: " + pair.Key);
                Debug.Log("STRUCTURE_COMPLETION_OK recipes=" + CompletionLeaves.Length +
                          " addresses=" + before.Count + " entries=" + group.entries.Count +
                          " duplicates=0 protected towers/catalogs/other wrappers unchanged; content build and R2 still required");
            }
            catch (Exception ex)
            {
                Debug.LogError("STRUCTURE_COMPLETION_FAIL " + ex.GetType().Name + ": " + ex.Message +
                               (writing ? "; partial approved output may exist: review wrapper/group diff before retry" : "; preflight refused, no writes started"));
            }
        }

        [MenuItem("Defenders/Art/Re-theme Structures to Synty")]
        public static void Run()
        {
            try { AssertOriginalStorefrontExclusions(); }
            catch (Exception ex) { Debug.LogError("STRUCTURE_RETHEME_FAIL " + ex.Message); return; }
            var settings = AddressableAssetSettingsDefaultObject.Settings;
            if (settings == null) { Debug.LogError("STRUCTURE_RETHEME_FAIL Addressable settings not found."); return; }

            var group = settings.groups.FirstOrDefault(g => g != null && g.Name == "Structure_Art");
            if (group == null) { Debug.LogError("STRUCTURE_RETHEME_FAIL group 'Structure_Art' not found."); return; }

            // ⚠ CREATE THE FOLDER THROUGH THE ASSETDATABASE, NOT Directory.CreateDirectory.
            // A bare mkdir puts the folder on disk but leaves the AssetDatabase unaware of it,
            // and PrefabUtility.SaveAsPrefabAsset then THROWS on the first save into it
            // (observed 2026-09-01: the whole run aborted at the first BuildWrapper call).
            if (!AssetDatabase.IsValidFolder(OutDir))
            {
                Directory.CreateDirectory(OutDir);
                AssetDatabase.Refresh();
                if (!AssetDatabase.IsValidFolder(OutDir))
                {
                    Debug.LogError("STRUCTURE_RETHEME_FAIL could not create asset folder " + OutDir);
                    return;
                }
            }
            int layer = LayerMask.NameToLayer(StructureLayerName);
            if (layer < 0)
                Debug.LogWarning("[SyntyRetheme] '" + StructureLayerName + "' layer missing — structures " +
                                 "left on Default; tower line-of-sight and nav carving will degrade.");

            // Snapshot the live addresses BEFORE touching anything: the address set is the
            // authority, this table is not.
            var liveEntries = group.entries.ToList();
            var swapped = new List<string>();
            var missingArt = new List<string>();
            var unmapped = new List<string>();
            // address -> the wrapper GUID that now owns it, for the purge pass below.
            var ownedNewGuid = new Dictionary<string, string>();
            // THE GROUP CAN HOLD DUPLICATE ADDRESSES. CreateOrMoveEntry re-points the
            // WRAPPER's entry but cannot remove the OLD source asset's entry, which keeps
            // carrying the same address -- verified 2026-09-01: 68 entries over 38 addresses,
            // every previously-swapped address present twice (old FBX + wrapper). So each
            // ADDRESS is processed once, not each entry, or a re-run double-counts and
            // rebuilds every wrapper twice.
            var processed = new HashSet<string>();

            foreach (var live in liveEntries)
            {
                string address = live.address;
                if (string.IsNullOrEmpty(address) || !address.StartsWith("Structures/")) continue;
                if (processed.Contains(address)) continue;
                string leaf = address.Substring("Structures/".Length);

                if (OriginalStorefrontGuids.ContainsKey(leaf)) { processed.Add(address); continue; }

                if (IsNonPrefabAddress(live)) continue;              // texture/material/dangling, see method docs

                bool isComposed = Composed.TryGetValue(leaf, out var parts);
                string rel = null;
                if (!isComposed && !Map.TryGetValue(leaf, out rel))
                { processed.Add(address); unmapped.Add(leaf); continue; }
                processed.Add(address);

                GameObject source = null;
                if (!isComposed)
                {
                    source = AssetDatabase.LoadAssetAtPath<GameObject>(ResolveSourcePath(rel));
                    if (source == null) { missingArt.Add(leaf + " -> " + rel); continue; }
                }

                string outPath = OutDir + "/" + leaf.Replace('/', '_') + ".prefab";
                // Guarded per CLAUDE.md §12: one bad source asset is LOGGED and skipped, never
                // allowed to abort the pass and leave the address set half-swapped.
                GameObject built = null;
                try { built = isComposed ? BuildComposedWrapper(parts, outPath, layer)
                                         : BuildWrapper(source, outPath, layer); }
                catch (System.Exception ex) { missingArt.Add(leaf + " (wrapper threw: " + ex.Message + ")"); continue; }
                if (built == null) { missingArt.Add(leaf + " (wrapper returned null)"); continue; }

                // Move the address onto the new prefab. CreateOrMoveEntry re-points an
                // existing address rather than duplicating it, so the catalog key is stable.
                string guid = AssetDatabase.AssetPathToGUID(outPath);
                var entry = settings.CreateOrMoveEntry(guid, group, false, false);
                if (entry == null) { missingArt.Add(leaf + " (entry move failed)"); continue; }
                entry.address = address;
                ownedNewGuid[address] = guid;
                swapped.Add(leaf);
            }

            // ---- purge superseded / dangling entries ------------------------------------
            // For every address THIS run re-pointed, exactly ONE entry -- the wrapper's --
            // may keep it. Everything else under that address (the old source asset's
            // entry, or a dangling GUID that resolves to nothing) is removed FROM THE
            // GROUP ONLY; the asset itself stays on disk. Duplicate addresses make the
            // built catalog's key resolution ambiguous, and the orientation oracle's
            // address map silently last-write-wins over them. Scoped to swapped addresses
            // so a skipped/missing-art address never loses its only live entry.
            int purged = 0;
            foreach (var e in group.entries.ToList())
            {
                if (e == null || string.IsNullOrEmpty(e.address)) continue;
                if (!ownedNewGuid.TryGetValue(e.address, out string keepGuid)) continue;
                if (e.guid == keepGuid) continue;
                if (settings.RemoveAssetEntry(e.guid, false)) purged++;
            }

            AssetDatabase.SaveAssets();
            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssets();

            Debug.Log($"[SyntyRetheme] swapped {swapped.Count}: {string.Join(", ", swapped)}");
            if (purged > 0)
                Debug.Log($"[SyntyRetheme] purged {purged} superseded/dangling group entr(ies) whose address " +
                          "a wrapper now owns (assets untouched on disk).");
            if (unmapped.Count > 0)
                Debug.LogWarning($"[SyntyRetheme] UNMAPPED {unmapped.Count} address(es) still on the OLD art — " +
                                 $"{string.Join(", ", unmapped)}. Not a silent skip: add them to Map or rule " +
                                 "them out explicitly.");
            if (missingArt.Count > 0)
                Debug.LogWarning($"[SyntyRetheme] ART MISSING {missingArt.Count}: {string.Join("; ", missingArt)}. " +
                                 "Is the Synty pack imported? It is gitignored (see .gitignore).");

            if (swapped.Count == 0) { Debug.LogError("STRUCTURE_RETHEME_FAIL nothing was swapped."); return; }
            Debug.Log($"STRUCTURE_RETHEME_OK swapped={swapped.Count} unmapped={unmapped.Count} " +
                      $"missing={missingArt.Count} purged={purged} -> {OutDir}");
        }

        /// <summary>Wrap a Synty source prefab in a tracked prefab carrying a fitted BoxCollider
        /// on the Structure layer. The wrapper exists so the gitignored pack is referenced from
        /// exactly one tracked place per address, the way the polyperfect walls always were.</summary>
        private static GameObject BuildWrapper(GameObject source, string outPath, int layer)
        {
            var root = (GameObject)PrefabUtility.InstantiatePrefab(source);
            if (root == null) root = Object.Instantiate(source);
            if (root == null) return null;
            try
            {
                root.name = Path.GetFileNameWithoutExtension(outPath);
                root.transform.position = Vector3.zero;
                root.transform.rotation = Quaternion.identity;
                root.transform.localScale = Vector3.one;
                return FinishWrapper(root, outPath, layer);
            }
            finally { Object.DestroyImmediate(root); }
        }

        /// <summary>Multi-part wrapper (WO-1291, e.g. the Watermill): a plain root with each
        /// part instantiated as a child at its authored offset (or auto-mounted, see
        /// MountOnSide), then the same collider/layer/save treatment as every single-model
        /// wrapper. Part 0 is the anchor and always sits at the root origin.</summary>
        private static GameObject BuildComposedWrapper(ComposedPart[] parts, string outPath, int layer)
        {
            if (parts == null || parts.Length == 0) return null;
            var root = new GameObject(Path.GetFileNameWithoutExtension(outPath));
            try
            {
                GameObject anchor = null;
                foreach (var part in parts)
                {
                    string srcPath = ResolveSourcePath(part.Path);
                    var source = AssetDatabase.LoadAssetAtPath<GameObject>(srcPath);
                    if (source == null)
                        throw new FileNotFoundException("composed part missing: " + srcPath);
                    var child = (GameObject)PrefabUtility.InstantiatePrefab(source);
                    if (child == null) child = Object.Instantiate(source);
                    child.transform.SetParent(root.transform, false);
                    child.transform.localPosition = part.LocalPos;
                    child.transform.localRotation = Quaternion.Euler(part.LocalEuler);
                    child.transform.localScale    = Vector3.one;
                    if (anchor == null) anchor = child;
                    else if (part.AutoMountSide) MountOnSide(anchor, child);
                }
                return FinishWrapper(root, outPath, layer);
            }
            finally { Object.DestroyImmediate(root); }
        }

        /// <summary>
        /// PLACEHOLDER MOUNT (WO-1291, pending screenshot verify). Hangs
        /// <paramref name="part"/> on the anchor's +X wall face: the part's thin horizontal
        /// axis is yawed to point out of the wall (a vertical wheel disc then lies flat
        /// against it), the part is embedded 0.10 m into the face so no air gap shows, and
        /// its bottom seats 0.05 m above local ground. The NUMBERS are measured from
        /// renderer bounds at build time so they cannot go stale, but the CHOICE of face,
        /// embed and clearance has not been eyeballed -- verify on the RunCaptureHeadless
        /// screenshots and, if wrong, author explicit offsets in Composed instead.
        /// </summary>
        private static void MountOnSide(GameObject anchor, GameObject part)
        {
            var aRends = anchor.GetComponentsInChildren<Renderer>(true);
            var pRends = part.GetComponentsInChildren<Renderer>(true);
            if (aRends.Length == 0 || pRends.Length == 0) return;

            Bounds a = WorldBounds(aRends);
            Bounds p = WorldBounds(pRends);

            // A wheel hanging flat on an X-facing wall must be THIN along X. If the source
            // is authored thin along Z instead, a 90-degree yaw swaps the horizontal axes.
            if (p.size.x > p.size.z)
            {
                part.transform.localRotation = Quaternion.Euler(0f, 90f, 0f) * part.transform.localRotation;
                p = WorldBounds(pRends);   // re-measure in the new orientation
            }

            const float embedM  = 0.10f;   // wheel sunk into the wall face
            const float groundClearanceM = 0.05f;
            Vector3 delta;
            delta.x = (a.max.x + p.extents.x - embedM) - p.center.x;
            delta.y = groundClearanceM - p.min.y;
            delta.z = a.center.z - p.center.z;
            part.transform.localPosition += delta;
        }

        private static Bounds WorldBounds(Renderer[] rends)
        {
            Bounds b = rends[0].bounds;
            for (int i = 1; i < rends.Length; i++) b.Encapsulate(rends[i].bounds);
            return b;
        }

        /// <summary>Shared tail of every wrapper build: fitted BoxCollider, Structure layer,
        /// save. Never destroys <paramref name="root"/> -- the caller owns the instance.</summary>
        private static GameObject FinishWrapper(GameObject root, string outPath, int layer)
        {
            // BoxCollider fitted to the MEASURED bounds, not a MeshCollider: the Structure
            // layer is what every tower/hero line-of-sight linecast tests against, and a box
            // is both cheaper and stable under the nav carve.
            var rends = root.GetComponentsInChildren<Renderer>(true);
            if (rends.Length > 0)
            {
                Bounds b = WorldBounds(rends);
                // ⚠ NO ?? HERE. GetComponent returns Unity's FAKE-NULL, which is not C# null,
                // so `GetComponent<T>() ?? AddComponent<T>()` hands back the fake-null and the
                // very next line throws "There is no 'BoxCollider' attached ... but a script is
                // trying to access it". That one operator failed 27 of 29 structures on the
                // 2026-09-01 first run. Explicit == null is the only correct test.
                var box = root.GetComponent<BoxCollider>();
                if (box == null) box = root.AddComponent<BoxCollider>();
                box.center = b.center - root.transform.position;
                box.size   = b.size;
            }
            if (layer >= 0) SetLayerRecursively(root, layer);

            return PrefabUtility.SaveAsPrefabAsset(root, outPath);
        }

        private static void SetLayerRecursively(GameObject go, int layer)
        {
            if (go == null) return;
            go.layer = layer;
            foreach (Transform child in go.transform) SetLayerRecursively(child.gameObject, layer);
        }
    }
}

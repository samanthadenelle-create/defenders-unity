// =============================================================================
// GearCatalogGenerator — reproducible, headless, RE-RUNNABLE generator that scans
// the owned, runtime-loadable gear models and emits/refreshes weapons.json (and
// armor.json when outfit sets are sourced). Sibling of ModelCatalogGenerator.
// -----------------------------------------------------------------------------
// WO-Item-2 (docs/ITEM_MODEL.md §5). THE high-leverage move: drop a new pack into
// a scanned source, regenerate, and the catalog (→ store/inventory/equip) grows
// with ZERO hand-typing of rows. The dynamism IS the point.
//
// WHAT IT DERIVES (deterministic, from name + path; docs/WEAPON_ARMOR_ORIENT_LOGIC
// §4 derive-from-name law):
//   id (slug)  displayName  category(sword/axe/bow/dagger/hammer/shield/staff/wand)
//   kind(Weapon|Gear)  job/classFit  hand(1h|2h)  damageType(melee|ranged)
//   prefabPath (Resources-relative, e.g. Heroes/Props/Weapons/sword_A)
//   capabilities (kind default: Weapon/Gear = Carriable|Equippable)
//
// WHAT IT STUBS (rarity-TEMPLATED placeholders, FLAGGED generated:true for human
// authoring — NEVER fabricated as final balance):
//   damageMult  defense  hpBonus  req.level  buyWood/buyStone/buyIron/buyCrystals
// A human authors these, then sets manual:true on the row to lock it forever.
//
// IDEMPOTENT + MANUAL-PRESERVING (§4):
//   - rows with "manual": true are NEVER touched (canon — like the orient baker).
//   - rows WITHOUT a "generated": true marker are treated as hand-authored (the v1
//     catalog) and are preserved untouched too.
//   - only "generated": true rows are refreshed (look re-derived, stubs left as-is
//     unless the asset's derivation changed).
//   - second run with no new assets = no-op (same bytes out).
//
// ASSET REALITY: only emits entries for gear that is committed AND Resources-
// loadable (Assets/Resources/Heroes/Props/Weapons). The big gitignored bundle
// (Blink ~805 weapons / KayKit ~9k) lives OUTSIDE Resources → not Resources-
// loadable → NOT emitted (no phantom entries). See docs/GEAR_GENERATOR_COVERAGE.md:
// those need the Addressables enabler first.
//
// Run from the menu (Defenders > Catalog > Generate Gear Catalog) or headless:
//   -executeMethod DeNelle.Editor.Catalog.GearCatalogGenerator.Generate
// READ-ONLY over assets (AssetDatabase.FindAssets + path reads); writes JSON +
// one coverage markdown. Does NOT run Unity gameplay; does NOT commit.
// -----------------------------------------------------------------------------
// ⛔⛔ READ THIS BEFORE CHANGING ANY WRITE PATH BELOW (2026-08-14, Step 1 of
//     docs/WEAPONS_DEEP_DIVE_2026-08-14.md §1 — "Landmine A"):
//
//   THE TWO weapons.json COPIES ARE NOT MIRRORS. THEY HAVE DIFFERENT ROLES.
//     Assets/Resources/Data/Canonical/weapons.json      = the CURATED runtime set (96 rows)
//     Assets/StreamingAssets/Data/Canonical/weapons.json = the LIBRARY          (431 rows)
//   (armor.json is asymmetric the same way: 24 curated / 30 library.)
//
//   ⚠ THE 96 IS IRREPLACEABLE STATE, NOT REGENERABLE OUTPUT. It is the residue of a
//   ONE-TIME HAND PRUNE — git: b78c81cfd (434 rows) -> 0d8185d1a "curate catalog —
//   434->34 weapons" -> walked back up to 96 by GearCurationExporter. StreamingAssets
//   was never pruned. **NO TOOL IN THIS REPO CAN REPRODUCE THE 96.** If it is ever
//   overwritten, the only recovery is git. GearCurationExporter cannot undo it either
//   — it is additive-only and never drops a row.
//
//   WHAT USED TO BE HERE (the landmine): MergeAndWrite read `Resources ?? Streaming`
//   into ONE root, appended every scanned row, and wrote that SAME json to BOTH paths.
//   Running this menu item would have re-inflated Resources 96 -> 431, publishing 335
//   dormant placeholder weapons into the shipped catalog. It survived only because
//   nobody had run the command since the prune.
//
//   THE INVARIANTS, now enforced in code and FAILING CLOSED:
//     1. Resources may only have EXISTING rows REFRESHED. It never gains a row.
//     2. StreamingAssets is the library and is the only copy that may grow.
//     3. A write is REFUSED (FlowTrace.Fail, nothing written) if either file's row
//        count would move beyond the rows this pass actually touched.
//     4. Resources ids must remain a SUBSET of StreamingAssets ids (subset oracle).
//   Do not "simplify" this back into a single shared root. See also GearIconRenderer.cs.
// =============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using DeNelle.Core.Diagnostics;

namespace DeNelle.Editor.Catalog
{
    /// <summary>Scans runtime-loadable gear models and emits/refreshes the canonical
    /// weapons.json / armor.json (idempotent, manual-preserving). WO-Item-2.</summary>
    public static class GearCatalogGenerator
    {
        // Canonical output — BOTH copies written in sync (the CanonicalJson law).
        private const string WeaponsResources = "Assets/Resources/Data/Canonical/weapons.json";
        private const string WeaponsStreaming = "Assets/StreamingAssets/Data/Canonical/weapons.json";
        private const string ArmorResources    = "Assets/Resources/Data/Canonical/armor.json";
        private const string ArmorStreaming     = "Assets/StreamingAssets/Data/Canonical/armor.json";
        private const string CoverageDoc        = "docs/GEAR_GENERATOR_COVERAGE.md";

        // FlowTrace system tag (§12 — instrumentation is PERMANENT, never stripped).
        private const string FlowSystem = "GearCatalog";

        // Set true by any hard refusal in MergeAndWrite so Generate withholds the
        // success marker. Fail closed: a refused run must never read as a pass.
        private static bool Refused;

        // Set by WriteIfChanged when a post-write disk verification failed and the file
        // was rolled back. Folded into Refused by MergeAndWrite.
        private static bool WriteRefused;

        // weapons.json / armor.json schema version. Bump ONLY when the row shape
        // changes; the generator preserves the existing value otherwise.
        private const int SchemaVersion = 1;

        // ── Gear sources (the extensibility seam) ────────────────────────────────
        // A source yields ScannedModel rows. Today: one Resources-folder source.
        // To add Addressables later, implement IGearSource over the Addressables
        // catalog and add it to Sources — NO rewrite of the derive/merge/emit code.
        private interface IGearSource
        {
            string Name { get; }
            IEnumerable<ScannedModel> Scan();
        }

        /// <summary>A model found on disk, with the address the runtime will load it by.</summary>
        private struct ScannedModel
        {
            public string fileNameNoExt;   // e.g. "sword_A"
            public string loadPath;         // runtime address, e.g. "Heroes/Props/Weapons/sword_A"

            // ── Addressables-backed source extras (Blink). NULL for a Resources source,
            //    in which case the legacy name-derivation + "tripo_" id slug applies. ──
            // loadVia: how the runtime resolves loadPath — null/"resources" (default) vs
            //          "addressable". A future equip loader branches on this.
            public string loadVia;
            // Pre-derived facts the Blink source carries (its filenames encode category +
            // hand directly). When set they OVERRIDE the name-substring derivation so a
            // Blink "Sword1h_01" classifies deterministically. Null => derive from name.
            public string idOverride;       // e.g. "blink_sword1h_01"
            public string displayOverride;  // e.g. "Sword1h 01 (Blink)"
            public string categoryOverride; // e.g. "sword"
            public string kindOverride;     // "Weapon" | "Gear"
            public string handOverride;     // "1h" | "2h"
            public string jobOverride;      // e.g. "knight" | "any"
            public string slotOverride;     // armor only, e.g. "Body"
            public string weightOverride;   // armor only, "light" | "heavy" | "any"
            public string damageTypeOverride; // "melee" | "ranged" | "magic"
        }

        // The gear sources scanned this run.
        // PRIMARY = the Blink RPG bundle via Addressables (docs/BLINK_NOTES.md — "the
        // Addressables gear enabler"): the largest owned weapon/armor collection. It
        // produces the generated:true rows that fill the catalog. The Resources source
        // stays for the small committed Tripo weapon set (different id namespace,
        // "tripo_*", so the two never collide). The merge preserves authored/manual rows.
        private static readonly IGearSource[] Sources =
        {
            new BlinkGearSource(),
            new ResourcesFolderSource(
                resFolder: "Heroes/Props/Weapons",
                assetFolder: "Assets/Resources/Heroes/Props/Weapons",
                exts: new[] { ".prefab", ".fbx" }),
        };

        [MenuItem("Defenders/Catalog/Generate Gear Catalog")]
        public static void Generate()
        {
            Refused = false;
            WriteRefused = false;
            FlowTrace.Step(FlowSystem, "Generate — scanning gear sources");

            // 1. SCAN every source → derive a generated row per model.
            var generatedWeapons = new List<JObject>();
            var generatedArmor   = new List<JObject>();
            int scanned = 0, skipped = 0;

            foreach (var src in Sources)
            {
                foreach (var model in src.Scan())
                {
                    // Skip backup/working copies (Tripo leaves "_tripobak_*").
                    if (model.fileNameNoExt.StartsWith("_", StringComparison.Ordinal))
                    {
                        skipped++;
                        continue;
                    }

                    // A source may pre-derive the category (Blink encodes it in the
                    // filename + folder); otherwise derive from the name (Tripo/Resources).
                    string category = model.categoryOverride ?? DeriveCategory(model.fileNameNoExt);
                    if (category == null)
                    {
                        Debug.LogWarning($"[GearCatalogGenerator] '{model.fileNameNoExt}' — no known category in name; skipped (add a category keyword or author manually).");
                        skipped++;
                        continue;
                    }

                    string kind = model.kindOverride ?? DeriveKind(category); // "Weapon" | "Gear"
                    JObject row = BuildGeneratedRow(model, category, kind);
                    if (kind == "Gear") generatedArmor.Add(row);
                    else                generatedWeapons.Add(row);
                    scanned++;
                }
            }

            // 2. MERGE generated rows onto the existing catalogs (manual-preserving),
            //    then 3. EMIT both copies in sync.
            int wEmitted = MergeAndWrite("weapons", "weapons", generatedWeapons,
                                         WeaponsResources, WeaponsStreaming);
            int aEmitted = MergeAndWrite("armor", "armor", generatedArmor,
                                         ArmorResources, ArmorStreaming);

            // 4. Coverage manifest (the gitignored bundle + the Addressables flag).
            WriteCoverageDoc(scanned, skipped, wEmitted, aEmitted);

            Debug.Log($"[GearCatalogGenerator] Scanned {scanned} loadable models " +
                      $"({skipped} skipped). weapons.json (curated) now {wEmitted} rows, " +
                      $"armor.json (curated) now {aEmitted} rows. Coverage → {CoverageDoc}.");
            AssetDatabase.Refresh();

            // WO-984 evidence marker. Withheld on ANY refusal — a fail-closed run must
            // never be readable as a pass.
            if (Refused)
                Debug.LogError("GEAR_CATALOG_GEN_REFUSED — a row-count guard fired; see the FlowTrace Fail line above. No catalog file was written.");
            else
                Debug.Log($"GEAR_CATALOG_GEN_OK curatedWeapons={wEmitted} curatedArmor={aEmitted}");
        }

        // =====================================================================
        // DERIVE (§4 — from name + path, never hand-typed)
        // =====================================================================

        // Category keyword → canonical category. Longest/most-specific first.
        private static readonly (string keyword, string category)[] CategoryRules =
        {
            ("dagger", "dagger"),
            ("sword",  "sword"),
            ("axe",    "axe"),
            ("bow",    "bow"),
            ("hammer", "hammer"),
            ("mace",   "mace"),
            ("censer", "censer"),
            ("shield", "shield"),
            ("staff",  "staff"),
            ("wand",   "wand"),
        };

        /// <summary>Category from the filename (case-insensitive substring), or null if unknown.</summary>
        private static string DeriveCategory(string fileNameNoExt)
        {
            string lower = fileNameNoExt.ToLowerInvariant();
            foreach (var (keyword, category) in CategoryRules)
                if (lower.Contains(keyword)) return category;
            return null;
        }

        /// <summary>kind from category: full-body outfit sets → "Gear"; everything else a "Weapon".
        /// (No outfit categories source from the current Weapons folder, so this returns
        /// "Weapon" today; the branch exists for when an outfit/armor source is added.)</summary>
        private static string DeriveKind(string category)
        {
            switch (category)
            {
                case "outfit":
                case "armor":
                case "robe":
                case "plate":
                    return "Gear";
                default:
                    return "Weapon";
            }
        }

        /// <summary>classFit/job by category convention (docs/ITEM_MODEL.md §3):
        /// sword/axe/hammer → knight; bow/dagger → ranger; staff/wand → mage;
        /// mace/censer → cleric; shield → any (off-hand, no class lock).</summary>
        private static string DeriveJob(string category)
        {
            switch (category)
            {
                case "sword":
                case "axe":
                case "hammer": return "knight";
                case "bow":
                case "dagger": return "ranger";
                case "staff":
                case "wand":   return "mage";
                case "mace":
                case "censer": return "cleric";
                case "shield":  return "any";
                default:        return "any";
            }
        }

        /// <summary>hand grip: 2h for the big two-handers; 1h otherwise.</summary>
        private static string DeriveHand(string category)
        {
            switch (category)
            {
                case "bow":
                case "staff":
                case "hammer": return "2h";
                default:        return "1h";
            }
        }

        /// <summary>damageType from category: bow → ranged; everything else melee.
        /// (staff/wand cast via AbilityDef.Range; their weapon contributes a mult, so
        /// we tag them "magic" to distinguish from a thrown/fired projectile.)</summary>
        private static string DeriveDamageType(string category)
        {
            switch (category)
            {
                case "bow":   return "ranged";
                case "staff":
                case "wand":  return "magic";
                default:       return "melee";
            }
        }

        /// <summary>Slug id: prefix "tripo_" (the source pack) + category + lettered variant
        /// from the filename, e.g. "sword_A" → "tripo_sword_a". Stable + collision-free
        /// per asset, so re-runs key onto the same row.</summary>
        private static string DeriveId(string fileNameNoExt)
        {
            var sb = new StringBuilder("tripo_");
            foreach (char c in fileNameNoExt.ToLowerInvariant())
                sb.Append(char.IsLetterOrDigit(c) ? c : '_');
            return sb.ToString();
        }

        /// <summary>Prettified display name: "sword_A" → "Sword A" (Tripo). Title-cases tokens.</summary>
        private static string DeriveDisplayName(string fileNameNoExt)
        {
            string[] tokens = fileNameNoExt.Replace('-', '_')
                                           .Split(new[] { '_' }, StringSplitOptions.RemoveEmptyEntries);
            var pretty = tokens.Select(t =>
                t.Length == 1 ? t.ToUpperInvariant()
                              : char.ToUpperInvariant(t[0]) + t.Substring(1).ToLowerInvariant());
            return string.Join(" ", pretty) + " (Tripo)";
        }

        // =====================================================================
        // ROW BUILD — derived fields + rarity-templated, FLAGGED stat stubs
        // =====================================================================

        /// <summary>Per-rarity placeholder stat template. CLEARLY a stub for authoring —
        /// the generator stamps these on a fresh generated row so the entry is wearable
        /// immediately, but they are NOT final balance (generated:true flags that).</summary>
        private static readonly Dictionary<string, (float dmg, float def, float hp, int level,
            int wood, int food, int iron, int crystals)> RarityTemplate =
            new Dictionary<string, (float, float, float, int, int, int, int, int)>
            {
                { "common",    (1.00f, 0.04f,  10f, 1,  20,  20,  20,   0) },
                { "uncommon",  (1.25f, 0.08f,  25f, 3,  60,  50,  80,   5) },
                { "rare",      (1.60f, 0.14f,  45f, 6, 150,  60, 250,  20) },
                { "epic",      (2.10f, 0.20f,  75f, 10, 400, 100, 600,  60) },
                { "legendary", (2.40f, 0.28f, 100f, 10, 800, 200,1200, 150) },
            };

        // Fresh generated rows default to "common" — the authoring floor. A human
        // re-rarities + retunes, then sets manual:true.
        private const string DefaultRarity = "common";

        private static JObject BuildGeneratedRow(ScannedModel model, string category, string kind)
        {
            // A source may pre-derive these (Blink). Otherwise derive from name/category.
            string id         = model.idOverride        ?? DeriveId(model.fileNameNoExt);
            string job        = model.jobOverride       ?? DeriveJob(category);
            string hand       = model.handOverride      ?? DeriveHand(category);
            string damageType = model.damageTypeOverride ?? DeriveDamageType(category);
            string name       = model.displayOverride   ?? DeriveDisplayName(model.fileNameNoExt);
            var t             = RarityTemplate[DefaultRarity];

            var row = new JObject
            {
                ["id"]          = id,
                ["name"]        = name,
                ["icon"]        = CategoryEmoji(category),     // legacy emoji placeholder
                ["kind"]        = kind,                         // derived
                ["category"]    = category,                     // derived
                ["job"]         = job,                          // derived classFit
                ["hand"]        = hand,                          // derived
                ["damageType"]  = damageType,                   // derived
                ["rarity"]      = DefaultRarity,                // STUB — author retunes
                ["prefabPath"]  = model.loadPath,               // derived: Resources path OR Addressable address
                // capabilities omitted → loader applies the kind default
                // (Carriable|Equippable). An author may add an explicit override.

                // ── FLAGS (idempotency + authoring) ──
                ["generated"]   = true,    // distinguishes generated from authored
                ["manual"]      = false,   // set true by hand to lock the row forever
            };

            // How the runtime resolves prefabPath: "addressable" (Blink) vs Resources
            // (default; field omitted so existing tripo rows are byte-identical).
            if (!string.IsNullOrEmpty(model.loadVia))
                row["loadVia"] = model.loadVia;

            // Armor slot/weight (Blink full-body sets carry these; ITEM_MODEL §6 — slot=Body).
            if (!string.IsNullOrEmpty(model.slotOverride))   row["slot"]   = model.slotOverride;
            if (!string.IsNullOrEmpty(model.weightOverride)) row["weight"] = model.weightOverride;

            // ── STAT STUBS (rarity-templated placeholders; NOT final balance) ──
            if (kind == "Gear")
            {
                row["defense"] = t.def;
                row["hpBonus"] = t.hp;
            }
            else
            {
                row["damageMult"] = t.dmg;
                if (damageType == "melee")
                    row["reach"] = 0f; // 0 = unset (PlayerAttackController default); author tunes
            }

            row["req"]         = new JObject { ["level"] = t.level };
            row["buyWood"]     = t.wood;
            row["buyFood"]     = t.food;
            row["buyIron"]     = t.iron;
            row["buyCrystals"] = t.crystals;

            return row;
        }

        private static string CategoryEmoji(string category)
        {
            switch (category)
            {
                case "sword":  return "\U0001F5E1"; // 🗡
                case "axe":    return "\U0001FA93"; // 🪓
                case "bow":    return "\U0001F3F9"; // 🏹
                case "dagger": return "\U0001F52A"; // 🔪
                case "hammer": return "\U0001F528"; // 🔨
                case "mace":   return "\U0001F528";
                case "shield": return "\U0001F6E1"; // 🛡
                case "staff":  return "\U0001FA84"; // 🪄
                case "wand":   return "\U0001FA84";
                case "censer": return "\U0001F56F"; // 🕯
                case "crossbow": return "\U0001F3F9"; // 🏹
                case "polearm":
                case "scythe":   return "\U0001F531"; // 🔱
                case "spellbook": return "\U0001F4D5"; // 📕
                case "claws":    return "\U0001F43E"; // 🐾
                case "outfit":
                case "armor":    return "\U0001F9E5"; // 🧥
                default:        return "⚔";     // ⚔
            }
        }

        // =====================================================================
        // MERGE (idempotent, manual-preserving) + EMIT (both copies in sync)
        // =====================================================================

        /// <summary>Merge the generated rows into BOTH catalog copies, each in its OWN role:
        /// StreamingAssets is the LIBRARY (may gain rows), Resources is the CURATED runtime set
        /// (may only have existing rows refreshed — NEVER gains a row). Fails closed if either
        /// file's row count would move beyond what this pass legitimately touched.
        /// Returns the final CURATED (Resources) row count.</summary>
        private static int MergeAndWrite(string arrayKey, string label,
            List<JObject> generated, string resourcesPath, string streamingPath)
        {
            // ── READ EACH COPY INDEPENDENTLY ─────────────────────────────────────
            // The OLD code read `Resources ?? StreamingAssets` into ONE root and wrote
            // that single root to BOTH paths. That is the landmine: the 96-row curated
            // Resources copy became the content of the 431-row StreamingAssets library,
            // and every scanned row was appended back onto Resources (96 -> 431).
            JObject curatedRoot = ReadJsonObject(resourcesPath);   // Resources      = CURATED
            JObject libraryRoot = ReadJsonObject(streamingPath);   // StreamingAssets = LIBRARY

            if (libraryRoot == null)
            {
                // No library on disk yet. Seeding the LIBRARY from the CURATED copy is the
                // safe direction (the library is a superset; growing it is allowed).
                libraryRoot = curatedRoot != null
                    ? (JObject)curatedRoot.DeepClone()
                    : new JObject { ["version"] = SchemaVersion };
                FlowTrace.Warn(FlowSystem, $"{label}: no library copy at {streamingPath} — seeding it from the curated copy.");
            }
            if (libraryRoot["version"] == null) libraryRoot["version"] = SchemaVersion;

            // ── PASS 1 — LIBRARY (StreamingAssets): refresh + APPEND new rows ────
            JArray libExisting = libraryRoot[arrayKey] as JArray ?? new JArray();
            int libBefore = libExisting.Count;

            var libById = new Dictionary<string, JObject>(StringComparer.OrdinalIgnoreCase);
            var libOrder = new List<string>();
            IndexRows(libExisting, libById, libOrder);

            int libRefreshed = 0, libAdded = 0, libPreserved = 0;
            foreach (var gen in generated)
            {
                string id = gen["id"].ToString();
                if (libById.TryGetValue(id, out var prev))
                {
                    if (IsUntouchable(prev)) { libPreserved++; continue; }
                    RefreshGeneratedRow(prev, gen);
                    libRefreshed++;
                }
                else
                {
                    libById[id] = gen;
                    libOrder.Add(id);
                    libAdded++;
                }
            }

            var libOut = new JArray();
            foreach (var id in libOrder) libOut.Add(libById[id]);
            libraryRoot[arrayKey] = libOut;

            // ── PASS 2 — CURATED (Resources): REFRESH ONLY, never add ────────────
            // The curated set is IRREPLACEABLE hand-pruned state (see the file header).
            // A generated row whose id is not already curated is deliberately DROPPED
            // here — that drop is the whole point of the curation.
            int curBefore = 0, curAfter = 0, curRefreshed = 0, curPreserved = 0, curNotCurated = 0;
            JArray curOut = null;

            if (curatedRoot == null)
            {
                // Fail closed: do NOT manufacture a curated file out of the library.
                // That is exactly the 96 -> 431 inflation this rewrite exists to prevent.
                FlowTrace.Fail(FlowSystem,
                    $"{label}: curated copy MISSING at {resourcesPath}. REFUSING to create it from the library " +
                    "(that would publish every dormant placeholder into the runtime catalog). Restore the file from git.");
                Debug.LogError($"[GearCatalogGenerator] {label}: curated copy missing at {resourcesPath} — REFUSED. No file written.");
                Refused = true;
                return 0;
            }

            if (curatedRoot["version"] == null) curatedRoot["version"] = SchemaVersion;
            JArray curExisting = curatedRoot[arrayKey] as JArray ?? new JArray();
            curBefore = curExisting.Count;

            var curById = new Dictionary<string, JObject>(StringComparer.OrdinalIgnoreCase);
            var curOrder = new List<string>();
            IndexRows(curExisting, curById, curOrder);

            foreach (var gen in generated)
            {
                string id = gen["id"].ToString();
                if (!curById.TryGetValue(id, out var prev)) { curNotCurated++; continue; }
                if (IsUntouchable(prev)) { curPreserved++; continue; }
                // DeepClone: the same `gen` JObject may already have been parented into the
                // library array above; never re-parent a token across two documents.
                RefreshGeneratedRow(prev, (JObject)gen.DeepClone());
                curRefreshed++;
            }

            curOut = new JArray();
            foreach (var id in curOrder) curOut.Add(curById[id]);
            curatedRoot[arrayKey] = curOut;
            curAfter = curOut.Count;

            // ── THE HARD REFUSAL — fail closed on any unexplained row-count move ──
            int libAfter = libOut.Count;
            int libExpected = libBefore + libAdded;
            if (curAfter != curBefore)
            {
                FlowTrace.Fail(FlowSystem,
                    $"{label}: REFUSED — curated (Resources) row count would move {curBefore} -> {curAfter}. " +
                    "This pass may only REFRESH curated rows, never add or drop one. Nothing was written.");
                Debug.LogError($"[GearCatalogGenerator] {label}: REFUSED (curated {curBefore} -> {curAfter}). No file written.");
                Refused = true;
                return curBefore;
            }
            if (libAfter != libExpected)
            {
                FlowTrace.Fail(FlowSystem,
                    $"{label}: REFUSED — library (StreamingAssets) row count would move {libBefore} -> {libAfter}, " +
                    $"but this pass only added {libAdded} row(s) (expected {libExpected}). Nothing was written.");
                Debug.LogError($"[GearCatalogGenerator] {label}: REFUSED (library {libBefore} -> {libAfter}, expected {libExpected}). No file written.");
                Refused = true;
                return curBefore;
            }

            // ── SUBSET ORACLE — curated ids must be a subset of the library ───────
            // Verified 2026-08-14: Resources-only ids = 0 today. A violation means the two
            // files have diverged in a way no tool here can reconcile; warn loudly, do not
            // silently "fix" it by copying rows across.
            var libIds = new HashSet<string>(libById.Keys, StringComparer.OrdinalIgnoreCase);
            var orphans = new List<string>();
            foreach (var id in curOrder) if (!libIds.Contains(id)) orphans.Add(id);
            if (orphans.Count > 0)
            {
                FlowTrace.Warn(FlowSystem,
                    $"{label}: SUBSET ORACLE violated — {orphans.Count} curated id(s) are absent from the library: " +
                    string.Join(", ", orphans.GetRange(0, Math.Min(10, orphans.Count))) +
                    (orphans.Count > 10 ? ", ..." : ""));
            }

            // ── WRITE (each file its own content; skip semantically-identical writes) ──
            // Each write is VERIFIED AGAINST DISK afterwards and rolled back on a row-count
            // move — an in-memory-only check would be hollow (nothing after this point can
            // change the counts). The FILE is the oracle.
            bool wroteCur = WriteIfChanged(resourcesPath, curatedRoot.ToString(Formatting.Indented), arrayKey, curAfter);
            bool wroteLib = WriteIfChanged(streamingPath, libraryRoot.ToString(Formatting.Indented), arrayKey, libAfter);
            if (WriteRefused) Refused = true;

            Debug.Log($"[GearCatalogGenerator] {label}: +{libAdded} new, {curRefreshed} refreshed, " +
                      $"{curAfter} preserved (curated rows, count unchanged). " +
                      $"library {libBefore} -> {libAfter} ({libRefreshed} refreshed, {libPreserved} untouchable); " +
                      $"{curNotCurated} scanned row(s) not curated (left in the library only). " +
                      $"wrote: curated={wroteCur} library={wroteLib}.");
            return curAfter;
        }

        /// <summary>Index rows by id (case-insensitive, first occurrence wins) preserving order.</summary>
        private static void IndexRows(JArray rows, Dictionary<string, JObject> byId, List<string> order)
        {
            foreach (var tok in rows)
            {
                if (tok is JObject obj && obj["id"] != null)
                {
                    string id = obj["id"].ToString();
                    if (!byId.ContainsKey(id)) { byId[id] = obj; order.Add(id); }
                }
            }
        }

        /// <summary>RULE: never touch a manual row, and never touch a row that was hand-authored
        /// (no `generated` marker = the v1 catalog).</summary>
        private static bool IsUntouchable(JObject row)
        {
            bool isManual     = row.Value<bool?>("manual") == true;
            bool wasGenerated = row.Value<bool?>("generated") == true;
            return isManual || !wasGenerated;
        }

        /// <summary>Write only when the on-disk JSON differs SEMANTICALLY, so a no-op run leaves an
        /// EMPTY `git diff` (the Step-1 proof). The comparison is JToken.DeepEquals, NOT bytes:
        /// a byte comparison re-indents any hand-edited row and produces a churn diff that would
        /// collide with whatever lane is authoring the balance curve. Formatting is THEIRS to own;
        /// this generator only ever writes when a VALUE actually changed.</summary>
        private static bool WriteIfChanged(string assetPath, string contents, string arrayKey, int expectedRows)
        {
            string normalized = contents.Replace("\r\n", "\n");
            string full = Path.GetFullPath(assetPath);
            string original = null;
            try
            {
                if (File.Exists(full))
                {
                    original = File.ReadAllText(full);
                    if (original.Replace("\r\n", "\n") == normalized) return false;      // identical bytes
                    if (JToken.DeepEquals(JObject.Parse(original), JObject.Parse(normalized)))
                    {
                        FlowTrace.Step(FlowSystem, $"{assetPath}: semantically unchanged — write SKIPPED (no formatting churn).");
                        return false;
                    }
                }
            }
            catch (Exception ex)
            {
                FlowTrace.Warn(FlowSystem, $"could not compare {assetPath} before write: {ex.Message}");
            }

            WriteUtf8NoBom(assetPath, normalized);

            // ── VERIFY AGAINST DISK, roll back on any row-count move ────────────
            int onDiskRows = -1;
            try { onDiskRows = (ReadJsonObject(assetPath)?[arrayKey] as JArray)?.Count ?? -1; }
            catch (Exception ex) { FlowTrace.Fail(FlowSystem, $"{assetPath}: could not re-read after write: {ex.Message}"); }

            if (onDiskRows != expectedRows)
            {
                if (original != null) File.WriteAllText(full, original, new UTF8Encoding(false));
                FlowTrace.Fail(FlowSystem,
                    $"{assetPath}: REFUSED — on-disk '{arrayKey}' row count is {onDiskRows} after the write, " +
                    $"expected {expectedRows}. The original bytes have been RESTORED.");
                Debug.LogError($"[GearCatalogGenerator] {assetPath}: REFUSED (on-disk rows {onDiskRows}, expected {expectedRows}). File rolled back.");
                WriteRefused = true;
                return false;
            }
            return true;
        }

        /// <summary>Refresh the generator-owned/derived fields on an existing generated row,
        /// without clobbering human edits to the stat stubs. Derived LOOK fields (always
        /// generator-owned) are re-derived; stat stubs are only reset if still at the
        /// generated template default for the row's current rarity.</summary>
        private static void RefreshGeneratedRow(JObject prev, JObject gen)
        {
            // Always re-derive the LOOK / classification fields (generator owns these).
            // loadVia/slot are generator-owned look facts too (the asset-link half).
            foreach (var key in new[] { "name", "kind", "category", "job", "hand",
                                        "damageType", "prefabPath", "icon", "loadVia", "slot" })
            {
                if (gen[key] != null) prev[key] = gen[key];
            }
            prev["generated"] = true;
            if (prev["manual"] == null) prev["manual"] = false;

            // Stat stubs: keep whatever is on prev (could be a partial human tune). If a
            // stub is entirely absent on prev, seed it from the freshly generated row.
            foreach (var key in new[] { "rarity", "weight", "damageMult", "defense", "hpBonus",
                                        "reach", "buyWood", "buyFood", "buyIron",
                                        "buyCrystals" })
            {
                if (prev[key] == null && gen[key] != null) prev[key] = gen[key];
            }
            if (prev["req"] == null && gen["req"] != null) prev["req"] = gen["req"];
        }

        // =====================================================================
        // I/O helpers
        // =====================================================================

        private static JObject ReadJsonObject(string assetPath)
        {
            try
            {
                string full = Path.GetFullPath(assetPath);
                if (!File.Exists(full)) return null;
                string text = File.ReadAllText(full);
                if (string.IsNullOrWhiteSpace(text)) return null;
                return JObject.Parse(text);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[GearCatalogGenerator] could not read {assetPath}: {ex.Message}");
                return null;
            }
        }

        private static void WriteUtf8NoBom(string assetPath, string contents)
        {
            string full = Path.GetFullPath(assetPath);
            Directory.CreateDirectory(Path.GetDirectoryName(full));
            // UTF-8 without BOM, LF — matches the project's canonical JSON convention
            // and keeps the NUL-byte/compile gate happy.
            File.WriteAllText(full, contents.Replace("\r\n", "\n"), new UTF8Encoding(false));
        }

        // =====================================================================
        // COVERAGE MANIFEST — the gitignored bundle + the Addressables flag
        // =====================================================================

        private static void WriteCoverageDoc(int scanned, int skipped, int weaponRows, int armorRows)
        {
            var sb = new StringBuilder();
            sb.AppendLine("# GEAR GENERATOR — COVERAGE MANIFEST");
            sb.AppendLine();
            sb.AppendLine("> AUTO-GENERATED by `DeNelle.Editor.Catalog.GearCatalogGenerator.Generate` (WO-Item-2).");
            sb.AppendLine("> Do NOT hand-edit — re-run *Defenders > Catalog > Generate Gear Catalog*.");
            sb.AppendLine();
            sb.AppendLine($"**Generated:** {DateTime.Now:yyyy-MM-dd HH:mm}");
            sb.AppendLine();
            sb.AppendLine("## Catalogued this run (committed + Resources-loadable)");
            sb.AppendLine();
            sb.AppendLine($"- Models scanned: **{scanned}** (skipped {skipped} backups/uncategorized)");
            sb.AppendLine($"- weapons.json rows now: **{weaponRows}**");
            sb.AppendLine($"- armor.json rows now: **{armorRows}**");
            sb.AppendLine();
            sb.AppendLine("Source folders scanned:");
            foreach (var src in Sources) sb.AppendLine($"- `{src.Name}` (Resources-loadable)");
            sb.AppendLine();
            sb.AppendLine("Only gear that is **committed AND Resources-loadable** is catalogued. Backup");
            sb.AppendLine("files (prefixed `_`) and models whose name carries no known category keyword");
            sb.AppendLine("are skipped (no phantom entries).");
            sb.AppendLine();
            sb.AppendLine("## NOT catalogued — gitignored bundle (BLOCKED on the Addressables enabler)");
            sb.AppendLine();
            sb.AppendLine("The bulk gear bundle is **gitignored AND outside `Assets/Resources/`**, so it is");
            sb.AppendLine("**NOT runtime-loadable by `Resources.Load`** → the generator deliberately emits");
            sb.AppendLine("**no entries** for it (a row whose `prefabPath` can't resolve would blank the");
            sb.AppendLine("store/equip view — the BUG#22 class). Approximate inventory on disk:");
            sb.AppendLine();
            sb.AppendLine("| Pack | Location | Approx. meshes | Status |");
            sb.AppendLine("|---|---|---|---|");
            sb.AppendLine("| Blink — Weapons (MegaWeaponPack) | `Assets/Blink/Art/Weapons` | ~805 | gitignored, outside Resources |");
            sb.AppendLine("| Blink — Characters/outfits | `Assets/Blink/Art/Characters` | ~551 | gitignored, outside Resources |");
            sb.AppendLine("| KayKit (adventurers + dungeon) | `Assets/Models/KayKit*` | ~9000 fbx | gitignored, outside Resources |");
            sb.AppendLine();
            sb.AppendLine("### Recommendation — Addressables, NOT a Resources mirror");
            sb.AppendLine();
            sb.AppendLine("To catalogue the bundle, add an **Addressables-backed gear source** to this");
            sb.AppendLine("generator (`IGearSource` over the Addressables catalog — the code is already");
            sb.AppendLine("structured for it; `Sources[]` takes another entry with **no rewrite**). Do NOT");
            sb.AppendLine("mirror the packs into `Assets/Resources/` — Resources is force-included in every");
            sb.AppendLine("build and would bloat the WebGL download with thousands of unused meshes. Mark");
            sb.AppendLine("the gear group Addressable, then the generator emits `prefabPath` as the");
            sb.AppendLine("Addressables address and the runtime loads on demand. **This is the enabler that");
            sb.AppendLine("unblocks the ~800/~9000-asset catalog.**");

            string full = Path.GetFullPath(CoverageDoc);
            Directory.CreateDirectory(Path.GetDirectoryName(full));
            File.WriteAllText(full, sb.ToString().Replace("\r\n", "\n"), new UTF8Encoding(false));
        }

        // =====================================================================
        // SOURCE — Resources folder (the one source for now)
        // =====================================================================

        /// <summary>Scans a single Resources folder (top level only, matching Resources.Load
        /// addressing) and yields each loadable model with its runtime load path.</summary>
        private sealed class ResourcesFolderSource : IGearSource
        {
            private readonly string _resFolder;
            private readonly string _assetFolder;
            private readonly string[] _exts;

            public ResourcesFolderSource(string resFolder, string assetFolder, string[] exts)
            {
                _resFolder = resFolder;
                _assetFolder = assetFolder;
                _exts = exts;
            }

            public string Name => $"Resources/{_resFolder}";

            public IEnumerable<ScannedModel> Scan()
            {
                if (!AssetDatabase.IsValidFolder(_assetFolder))
                {
                    Debug.LogWarning($"[GearCatalogGenerator] source folder '{_assetFolder}' not found — skipped.");
                    yield break;
                }

                string[] guids = AssetDatabase.FindAssets(string.Empty, new[] { _assetFolder });
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var rows = new List<ScannedModel>();

                foreach (string guid in guids)
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    if (string.IsNullOrEmpty(path)) continue;

                    // Top level only: parent must equal the target folder.
                    string parent = (Path.GetDirectoryName(path) ?? string.Empty).Replace('\\', '/');
                    if (!string.Equals(parent, _assetFolder, StringComparison.OrdinalIgnoreCase))
                        continue;

                    string ext = Path.GetExtension(path).ToLowerInvariant();
                    if (Array.IndexOf(_exts, ext) < 0) continue;

                    string nameNoExt = Path.GetFileNameWithoutExtension(path);
                    // A prefab and an fbx of the same name address the same load path —
                    // prefer the first seen (prefab sorts before fbx by the ext order is
                    // not guaranteed, so de-dupe by name and let either win deterministically).
                    if (!seen.Add(nameNoExt)) continue;

                    rows.Add(new ScannedModel
                    {
                        fileNameNoExt = nameNoExt,
                        loadPath = $"{_resFolder}/{nameNoExt}",
                    });
                }

                rows.Sort((a, b) => string.Compare(a.fileNameNoExt, b.fileNameNoExt,
                                                   StringComparison.OrdinalIgnoreCase));
                foreach (var r in rows) yield return r;
            }
        }

        // =====================================================================
        // SOURCE — Blink RPG bundle via Addressables (THE primary gear source)
        // =====================================================================

        /// <summary>Scans the gitignored Blink RPG bundle (weapons + full-body armor outfit
        /// sets) and yields a ScannedModel per asset whose <c>loadPath</c> is the ADDRESSABLE
        /// ADDRESS (not a Resources path) and whose <c>loadVia</c> = "addressable". It encodes
        /// the category/kind/hand/job/damageType/slot/weight directly from the Blink filename +
        /// folder (§4 derive-from-name law), so the row classifies deterministically without
        /// the legacy name-substring guess. The addresses MATCH BlinkAddressableMarker's
        /// scheme exactly (gear/weapon/&lt;name&gt;, gear/armor/&lt;set&gt;_Male) so prefabPath
        /// resolves once the marker has run. Absent pack (fresh clone) ⇒ folders invalid ⇒
        /// yields nothing (no phantom rows).</summary>
        private sealed class BlinkGearSource : IGearSource
        {
            public string Name => "Blink (Addressables) — Weapons + Armor sets";

            public IEnumerable<ScannedModel> Scan()
            {
                foreach (var m in ScanWeapons()) yield return m;
                foreach (var m in ScanArmorSets()) yield return m;
            }

            // ── Weapons: 16 categories × 25, filename encodes category + hand. ──
            private IEnumerable<ScannedModel> ScanWeapons()
            {
                foreach (var (guid, address) in BlinkAddressableMarker.EnumerateWeaponPrefabs())
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    if (string.IsNullOrEmpty(path)) continue;
                    string fileName = Path.GetFileNameWithoutExtension(path); // e.g. "Sword1h_01"

                    string category = BlinkWeaponCategory(fileName);
                    if (category == null)
                    {
                        Debug.LogWarning($"[GearCatalogGenerator] Blink weapon '{fileName}' — unrecognised category prefix; skipped.");
                        continue;
                    }

                    yield return new ScannedModel
                    {
                        fileNameNoExt      = fileName,
                        loadPath           = address,                 // the Addressable address
                        loadVia            = "addressable",
                        idOverride         = BlinkSlug("blink_", fileName),
                        displayOverride    = BlinkDisplayName(fileName),
                        categoryOverride   = category,
                        kindOverride       = "Weapon",
                        handOverride       = BlinkWeaponHand(fileName),
                        jobOverride        = BlinkWeaponJob(category),
                        damageTypeOverride = BlinkWeaponDamageType(category),
                    };
                }
            }

            // ── Armor: ONE entry per SET (canonical = the Male variant). slot=Body. ──
            private IEnumerable<ScannedModel> ScanArmorSets()
            {
                foreach (var (guid, address) in BlinkAddressableMarker.EnumerateArmorSetPrefabs())
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    if (string.IsNullOrEmpty(path)) continue;
                    string fileName = Path.GetFileNameWithoutExtension(path); // e.g. "Centurion_HumanMale"

                    if (!BlinkAddressableMarker.TryParseArmorSet(fileName, out string setName, out _))
                        continue;

                    yield return new ScannedModel
                    {
                        fileNameNoExt    = setName,                  // "Centurion"
                        loadPath         = address,                  // "gear/armor/Centurion_Male"
                        loadVia          = "addressable",
                        idOverride       = BlinkSlug("blink_armor_", setName),
                        displayOverride  = BlinkDisplayName(setName),
                        categoryOverride = "outfit",                 // maps to kind=Gear via DeriveKind
                        kindOverride     = "Gear",
                        jobOverride      = "any",                    // armor gates by weight, not job
                        slotOverride     = "Body",                   // full-body (ITEM_MODEL §6)
                        weightOverride   = BlinkArmorWeight(setName),
                    };
                }
            }

            // ── Blink weapon category vocabulary (filename PREFIX before the digits) ──
            // Names: Axe1h, Axe2h, Sword1h, Sword2h, Dagger1h, Bow2h, Crossbow2h,
            // Shield1h, Mace1h, Polearm2h, Scythe2h, Hammer2h, Staff2h, Wand1h,
            // SpellBook1h, Claws1h. Map each to a canonical catalog category.
            private static string BlinkWeaponCategory(string fileName)
            {
                string lower = (fileName ?? string.Empty).ToLowerInvariant();
                // Order: most-specific first (e.g. "crossbow" before "bow").
                if (lower.StartsWith("crossbow"))  return "crossbow";
                if (lower.StartsWith("bow"))        return "bow";
                if (lower.StartsWith("sword"))      return "sword";
                if (lower.StartsWith("axe"))        return "axe";
                if (lower.StartsWith("dagger"))     return "dagger";
                if (lower.StartsWith("shield"))     return "shield";
                if (lower.StartsWith("mace"))       return "mace";
                if (lower.StartsWith("polearm"))    return "polearm";
                if (lower.StartsWith("scythe"))     return "scythe";
                if (lower.StartsWith("hammer"))     return "hammer";
                if (lower.StartsWith("staff"))      return "staff";
                if (lower.StartsWith("wand"))       return "wand";
                if (lower.StartsWith("spellbook"))  return "spellbook";
                if (lower.StartsWith("claws") || lower.StartsWith("claw")) return "claws";
                return null;
            }

            // hand from the "1h"/"2h" token in the filename; fall back by category.
            private static string BlinkWeaponHand(string fileName)
            {
                string lower = (fileName ?? string.Empty).ToLowerInvariant();
                if (lower.Contains("2h")) return "2h";
                if (lower.Contains("1h")) return "1h";
                return "1h";
            }

            // classFit by category (ITEM_MODEL §3): sword/axe/hammer→knight;
            // bow/crossbow/dagger→ranger; staff/wand/spellbook→mage; mace→cleric;
            // shield→any (off-hand); polearm/scythe/claws default knight (melee front-line).
            private static string BlinkWeaponJob(string category)
            {
                switch (category)
                {
                    case "sword":
                    case "axe":
                    case "hammer":
                    case "polearm":
                    case "scythe":
                    case "claws":     return "knight";
                    case "bow":
                    case "crossbow":
                    case "dagger":    return "ranger";
                    case "staff":
                    case "wand":
                    case "spellbook": return "mage";
                    case "mace":      return "cleric";
                    case "shield":    return "any";
                    default:          return "any";
                }
            }

            private static string BlinkWeaponDamageType(string category)
            {
                switch (category)
                {
                    case "bow":
                    case "crossbow":  return "ranged";
                    case "staff":
                    case "wand":
                    case "spellbook": return "magic";
                    default:          return "melee";
                }
            }

            // Armor weight tier from the set name. Basic + cloth/light themed sets → light
            // (Ranger/Mage); plate/knight/beast-armour themed sets → heavy (Knight/Cleric).
            // Unknown ⇒ "any" (universal). Author retunes; weight is seed-once (not clobbered).
            private static string BlinkArmorWeight(string setName)
            {
                string lower = (setName ?? string.Empty).ToLowerInvariant();
                // Heavy / plate-class themed sets.
                if (lower.Contains("centurion") || lower.Contains("knight") ||
                    lower.Contains("guard")     || lower.Contains("minotaur") ||
                    lower.Contains("dragon")    || lower.Contains("hydra") ||
                    lower.Contains("bear")      || lower.Contains("boar") ||
                    lower.Contains("land"))
                    return "heavy";
                // Light / cloth-leather class.
                if (lower.Contains("basic")   || lower.Contains("cloth") ||
                    lower.Contains("leather") || lower.Contains("hunter") ||
                    lower.Contains("savage")  || lower.Contains("bird") ||
                    lower.Contains("engineer"))
                    return "light";
                return "any";
            }

            // ── Shared name helpers ──

            /// <summary>Slug: prefix + lowercased, non-alnum→'_'. e.g. "Sword1h_01" → "blink_sword1h_01".</summary>
            private static string BlinkSlug(string prefix, string fileNameNoExt)
            {
                var sb = new StringBuilder(prefix);
                foreach (char c in (fileNameNoExt ?? string.Empty).ToLowerInvariant())
                    sb.Append(char.IsLetterOrDigit(c) ? c : '_');
                return sb.ToString();
            }

            /// <summary>Prettified display name + " (Blink)" tag. "Sword1h_01" → "Sword1h 01 (Blink)".</summary>
            private static string BlinkDisplayName(string fileNameNoExt)
            {
                string[] tokens = (fileNameNoExt ?? string.Empty).Replace('-', '_')
                    .Split(new[] { '_' }, StringSplitOptions.RemoveEmptyEntries);
                var pretty = tokens.Select(tk =>
                    tk.Length == 1 ? tk.ToUpperInvariant()
                                   : char.ToUpperInvariant(tk[0]) + tk.Substring(1));
                return string.Join(" ", pretty) + " (Blink)";
            }
        }
    }
}

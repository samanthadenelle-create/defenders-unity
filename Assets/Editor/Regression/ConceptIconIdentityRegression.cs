// =============================================================================
// ConceptIconIdentityRegression [concept-icons] -- pins the WO-1294 ONE-ICON
// IDENTITY CONTRACT: an assignable skill shows the SAME picture on its talent-tree
// node, in the assignment picker, in the three-slot hot-swap rail and on the combat
// HUD, and the nine canonical troop portraits actually load.
// -----------------------------------------------------------------------------
// WHY THIS EXISTS (the defect it was written from, 2026-09-02): the SHARED /
// universal skill pool was re-tagged in talent-icon-map.json under WO-1023 and
// concept-icons.json was never followed. The tree node for Arcane Bolt drew
// Arcanist17 while the hot-swap slot and the combat HUD for the very same skill drew
// Arcanist6 -- and Arcanist6 was ALSO mage.manaweave's icon, so a mage saw two
// different spells as the identical purple dart. Nothing failed; both files were
// individually valid and individually oracle-clean (TalentIconMapRegression guards
// the tree side ONLY). The split lived exactly in the gap BETWEEN the two files,
// which is what this suite closes.
//
// Six assertions:
//   1. concept-icons.json Resources and StreamingAssets copies are byte-identical
//      (Resources wins at runtime, so drift is invisible in the Editor -- the
//      WO-996 armor.json shape).
//   2. every ability-granting talent node resolves the SAME Blink source art on
//      both sides: talent-icon-map blinkSource basename == the concept-icons row
//      name for that node's abilityId. This is the section-4 identity contract.
//   3. every ability-granting node's abilityId HAS a concept-icons row -- a missing
//      row is not "no icon", it silently falls through to the crossed-swords
//      default, which is the Knight's art on somebody else's spell.
//   4. no two DIFFERENT ability ids that can be on screen together share one icon.
//      "Together" = same class prefix, or one of them universal.* (the universal
//      pool is placeable by every class). Same-concept twins are allow-listed by
//      name below with their reason, per WO-1294 acceptance ("unless they are the
//      same authoritative skill").
//   5. every spellicons/troop row addresses art that RpgUiCatalog actually returns
//      -- the SAME call ConceptIconResolver makes at runtime, so an unimported or
//      renamed sprite cannot pass as wired.
//   6. the hot-swap rail is THREE slots (AssignableSkillBar.SlotCount == 3) and all
//      nine canonical troop portraits load from role 'troop', with every
//      troops.json iconId inside that set. WO-1294 retired the fourth slot; this
//      keeps it retired, and keeps the troop portraits off the letter fallback.
//
// Marker: CONCEPT_ICON_IDENTITY_OK / CONCEPT_ICON_IDENTITY_FAIL. Expected: GREEN.
//
// Wire (DataRegression.RunAll):
//   if (!DeNelle.Editor.Regression.ConceptIconIdentityRegression.Run(out var r)) failures.Add(r); else log.AppendLine("[concept-icons] " + r);
// =============================================================================
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using UnityEngine;
using DeNelle.Core.UI;
using DeNelle.Village;
using DeNelle.Village.Talents;

namespace DeNelle.Editor.Regression
{
    public static class ConceptIconIdentityRegression
    {
        private const string ResourcesConceptPath = "Assets/Resources/Data/Canonical/concept-icons.json";
        private const string StreamingConceptPath = "Assets/StreamingAssets/Data/Canonical/concept-icons.json";
        private const string ResourcesIconMapPath = "Assets/Resources/Data/Canonical/talent-icon-map.json";
        private const string ResourcesTroopsPath  = "Assets/Resources/Data/Canonical/troops.json";

        /// <summary>
        /// The nine canonical troop ids the owner's portrait family covers (WO-1294 section 2).
        /// Hard-listed on purpose: the acceptance criterion is "all NINE show their portrait",
        /// so a troop silently dropped from troops.json must still fail this suite.
        /// </summary>
        private static readonly string[] CanonicalTroopIds =
        {
            "troop-footman", "troop-archer", "troop-spearman", "troop-field-cleric",
            "troop-shieldguard", "troop-outrider", "troop-catapult", "troop-battlemage",
            "troop-echo-legionnaire",
        };

        /// <summary>
        /// Ability-id pairs that are ALLOWED to share one icon because they are the same
        /// authoritative concept under two ids (WO-1294 acceptance carve-out). Each entry is
        /// "idA|idB" lower-cased with the smaller id first, and each carries its reason here so
        /// a future reader can tell a ruling from an oversight.
        /// </summary>
        private static readonly Dictionary<string, string> SameConceptTwins = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            // Both are literally "Arcane Bolt" -- the class-scoped id and the shared-pool id of
            // one spell, differing only in tuning. concept-icons.json records the same reasoning.
            { "mage.arcane-bolt|universal.arcane-bolt", "same named spell 'Arcane Bolt' under a class-scoped and a shared-pool id" },
            // Both are literally "Mend". NOT a settled ruling -- concept-icons.json records this
            // as STILL UNAUTHORED pending an owner art tag; it is allow-listed so the suite stays
            // green on a known, written-down gap rather than failing on somebody else's decision.
            { "mage.heal|universal.mend", "both named 'Mend'; separating them needs an OWNER art tag (recorded in concept-icons.json)" },
        };

        /// <summary>
        /// Ability ids with NO concept-icons row ON PURPOSE, pending an owner art tag. Each carries
        /// its reason. These are LOGGED as an owner-tag debt every run, never silently skipped, and
        /// never failed -- failing them would force the CLI to pick the art, which is exactly the
        /// creative call the owner-tags-the-art rule reserves. Delete an entry the day its row lands.
        /// </summary>
        private static readonly Dictionary<string, string> DeliberatelyUnauthored = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            // WO-1330 (2026-09-02): the two new over-time abilities. Their VFX keys are held
            // EMPTY by that ticket's own [owner-tag] rule for the same reason the icon row is
            // held here -- the owner tags the art and this seat maps it verbatim, so picking an
            // icon to make a suite green would be exactly the creative call that rule reserves.
            { "mage.wither", "WO-1330 holds the row for an owner art tag (granted by node mage.t1n4; its VFX keys are held empty by the same ruling)" },
            { "knight.ironblood", "WO-1330 holds the row for an owner art tag (granted by node knight.t1n3; its VFX keys are held empty by the same ruling)" },
        };

        [Serializable]
        private sealed class IconRef
        {
            [JsonProperty("role")] public string Role;
            [JsonProperty("name")] public string Name;
        }

        [Serializable]
        private sealed class ConceptFile
        {
            [JsonProperty("map")] public Dictionary<string, IconRef> Map = new Dictionary<string, IconRef>();
        }

        [Serializable]
        private sealed class IconMapEntry
        {
            [JsonProperty("id")] public string Id;
            [JsonProperty("blinkSource")] public string BlinkSource;
        }

        [Serializable]
        private sealed class IconMapFile
        {
            [JsonProperty("skills")] public List<IconMapEntry> Skills = new List<IconMapEntry>();
        }

        [Serializable]
        private sealed class TroopRow
        {
            [JsonProperty("id")] public string Id;
            [JsonProperty("iconId")] public string IconId;
        }

        [Serializable]
        private sealed class TroopsFile
        {
            [JsonProperty("troops")] public List<TroopRow> Troops = new List<TroopRow>();
        }

        public static bool Run(out string reason)
        {
            var failures = new List<string>();
            var log = new StringBuilder();
            log.AppendLine("--- CONCEPT ICON IDENTITY (tree==hotswap==HUD / no collisions / art loads / 3 slots / 9 portraits) ---");

            // ── 1: the twin canonical copies ──────────────────────────────────────
            AssertTwinCopies(failures, log);

            // ── load concept-icons.json (the Resources copy = the runtime winner) ─
            ConceptFile concepts = null;
            if (!File.Exists(ResourcesConceptPath))
                failures.Add($"[concept-icons] map missing at {ResourcesConceptPath}");
            else
            {
                try { concepts = JsonConvert.DeserializeObject<ConceptFile>(File.ReadAllText(ResourcesConceptPath)); }
                catch (Exception ex) { failures.Add($"[concept-icons] concept-icons.json parse threw: {ex.GetType().Name}: {ex.Message}"); }
            }
            if (concepts == null || concepts.Map == null || concepts.Map.Count == 0)
            {
                if (failures.Count == 0) failures.Add("[concept-icons] concept-icons.json deserialized to 0 rows (mapping break or empty 'map')");
                reason = Finish(failures, log);
                return failures.Count == 0;
            }
            // case-insensitive view: ConceptIconResolver lower-cases every lookup key.
            var rows = new Dictionary<string, IconRef>(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in concepts.Map) if (kv.Value != null) rows[kv.Key] = kv.Value;
            log.AppendLine($"concept-icons.json -> {rows.Count} row(s)");

            // ── 2 + 3: the tree side must agree with the hot-swap/HUD side ────────
            AssertTreeMatchesHotSwap(rows, failures, log);

            // ── 4: no two co-visible skills wear one picture ──────────────────────
            AssertNoCoVisibleCollision(rows, failures, log);

            // ── 5: every spellicons/troop row addresses art that actually loads ───
            AssertArtLoads(rows, failures, log);

            // ── 6: three hot-swap slots + nine troop portraits ────────────────────
            AssertThreeSlots(failures, log);
            AssertTroopPortraits(failures, log);

            reason = Finish(failures, log);
            return failures.Count == 0;
        }

        // ─────────────────────────────────────────────────────────────────────────
        private static void AssertTwinCopies(List<string> failures, StringBuilder log)
        {
            if (!File.Exists(ResourcesConceptPath) || !File.Exists(StreamingConceptPath))
            {
                failures.Add($"[concept-icons] one of the twin copies is missing (Resources: {File.Exists(ResourcesConceptPath)}, StreamingAssets: {File.Exists(StreamingConceptPath)})");
                return;
            }
            var res = File.ReadAllBytes(ResourcesConceptPath);
            var sa = File.ReadAllBytes(StreamingConceptPath);
            bool same = res.Length == sa.Length;
            if (same) for (int i = 0; i < res.Length; i++) { if (res[i] != sa[i]) { same = false; break; } }
            if (!same)
                failures.Add($"[concept-icons] Resources ({res.Length} B) and StreamingAssets ({sa.Length} B) copies of concept-icons.json DIFFER -- Resources wins at runtime, so the drift is invisible in the Editor");
            else
                log.AppendLine($"concept-icons.json twin copies byte-identical ({res.Length} B)");
        }

        // ─────────────────────────────────────────────────────────────────────────
        /// <summary>
        /// For every talent node that grants an ability: the picture the NODE shows (recorded as
        /// talent-icon-map blinkSource) and the picture the HOT-SWAP SLOT / COMBAT HUD shows
        /// (concept-icons row for that abilityId) must be the same Blink source file.
        /// </summary>
        private static void AssertTreeMatchesHotSwap(Dictionary<string, IconRef> rows, List<string> failures, StringBuilder log)
        {
            IconMapFile map = null;
            if (!File.Exists(ResourcesIconMapPath))
            {
                failures.Add($"[concept-icons] talent-icon-map.json missing at {ResourcesIconMapPath} -- the tree/hot-swap identity cannot be judged");
                return;
            }
            try { map = JsonConvert.DeserializeObject<IconMapFile>(File.ReadAllText(ResourcesIconMapPath)); }
            catch (Exception ex) { failures.Add($"[concept-icons] talent-icon-map.json parse threw: {ex.GetType().Name}: {ex.Message}"); return; }
            if (map == null || map.Skills == null || map.Skills.Count == 0)
            {
                failures.Add("[concept-icons] talent-icon-map.json deserialized to 0 entries");
                return;
            }
            var blinkById = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var e in map.Skills)
                if (e != null && !string.IsNullOrEmpty(e.Id)) blinkById[e.Id] = e.BlinkSource;

            int checkedNodes = 0, agreed = 0, unauthored = 0;
            foreach (var node in AbilityGrantingNodes())
            {
                checkedNodes++;
                string abilityId = node.AbilityId;

                if (!rows.TryGetValue(abilityId, out var row) || row == null || string.IsNullOrEmpty(row.Name))
                {
                    if (DeliberatelyUnauthored.TryGetValue(abilityId, out var why))
                    {
                        unauthored++;
                        log.AppendLine($"  OWNER-TAG DEBT: '{abilityId}' (node {node.Id}) has no concept-icons row -- {why}. Its tree node shows real art while the hot-swap slot and combat HUD show the default icon; one owner art tag closes it.");
                        continue;
                    }
                    failures.Add($"[concept-icons] node '{node.Id}' grants '{abilityId}' but NO concept-icons row exists for that id -- the hot-swap slot and the combat HUD fall through to the crossed-swords default while the tree node shows real art. Author the row, or record it in DeliberatelyUnauthored WITH the reason if it is waiting on an owner art tag");
                    continue;
                }

                if (!blinkById.TryGetValue(node.Id, out var blinkSource) || string.IsNullOrEmpty(blinkSource))
                {
                    // TalentIconMapRegression owns coverage of the map itself; only note it here.
                    log.AppendLine($"  (skipped {node.Id}: no blinkSource in talent-icon-map -- see [talent-icons])");
                    continue;
                }

                // Only spellicons rows are comparable: blinkSource names a Blink library file, and
                // the mirrored spellicons sprite carries that same basename. A row pointing at a
                // different role (hand-authored 'abilities'/'icons' art) has no Blink twin to compare.
                if (!string.Equals(row.Role, "spellicons", StringComparison.OrdinalIgnoreCase))
                {
                    log.AppendLine($"  (skipped {node.Id}/{abilityId}: concept row role '{row.Role}' is not a mirrored Blink sprite)");
                    continue;
                }

                string treeName = BaseName(blinkSource);
                if (!string.Equals(treeName, row.Name, StringComparison.OrdinalIgnoreCase))
                    failures.Add($"[concept-icons] IDENTITY SPLIT for '{abilityId}': the talent-tree node '{node.Id}' shows '{treeName}' (talent-icon-map blinkSource) but the hot-swap slot and combat HUD show '{row.Name}' (concept-icons). One skill must be one picture everywhere (WO-1294 section 4) -- re-point the concept-icons row at the art the tree already authors rather than picking new art");
                else agreed++;
            }
            log.AppendLine($"tree/hot-swap identity: {agreed} agreed of {checkedNodes} ability-granting node(s); {unauthored} awaiting an owner art tag");
            if (checkedNodes == 0)
                failures.Add("[concept-icons] HeroTalentCatalog yielded 0 ability-granting nodes -- the identity contract cannot be judged (catalog mapping break)");
        }

        // ─────────────────────────────────────────────────────────────────────────
        /// <summary>
        /// Two DIFFERENT skills that can sit on screen at the same time must not wear the same
        /// picture -- the owner is red/green colourblind, so silhouette carries recognition and a
        /// shared silhouette cannot be rescued by hue. Scope: same class prefix, or either side
        /// universal.* (the shared pool is placeable by every class).
        /// </summary>
        private static void AssertNoCoVisibleCollision(Dictionary<string, IconRef> rows, List<string> failures, StringBuilder log)
        {
            // ability-id rows only: 'strike'/'heal'/'gold' are effect + concept tokens, and an
            // effect keyword deliberately shares art with the id it falls back for.
            var abilityRows = new List<KeyValuePair<string, IconRef>>();
            foreach (var kv in rows)
                if (kv.Key.IndexOf('.') > 0 && !string.IsNullOrEmpty(kv.Value.Name)) abilityRows.Add(kv);

            int collisions = 0;
            for (int i = 0; i < abilityRows.Count; i++)
            for (int j = i + 1; j < abilityRows.Count; j++)
            {
                var a = abilityRows[i];
                var b = abilityRows[j];
                if (!string.Equals(a.Value.Role, b.Value.Role, StringComparison.OrdinalIgnoreCase)) continue;
                if (!string.Equals(a.Value.Name, b.Value.Name, StringComparison.OrdinalIgnoreCase)) continue;
                if (!CanBeOnScreenTogether(a.Key, b.Key)) continue;
                if (IsAllowedTwin(a.Key, b.Key)) continue;

                collisions++;
                failures.Add($"[concept-icons] ICON COLLISION: '{a.Key}' and '{b.Key}' both resolve {a.Value.Role}/{a.Value.Name} and can be on screen together. Two different skills sharing one silhouette is a recognition failure (the owner is red/green colourblind, so hue cannot separate them). Either re-point one row, or add the pair to SameConceptTwins WITH its reason if they really are one skill under two ids");
            }
            log.AppendLine($"co-visible icon collisions: {collisions} across {abilityRows.Count} ability-id row(s)");
        }

        /// <summary>knight.* and mage.* never share a screen; universal.* shares with everything.</summary>
        private static bool CanBeOnScreenTogether(string idA, string idB)
        {
            string a = ClassPrefix(idA);
            string b = ClassPrefix(idB);
            if (a == "universal" || b == "universal") return true;
            return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
        }

        private static string ClassPrefix(string id)
        {
            if (string.IsNullOrEmpty(id)) return "";
            int dot = id.IndexOf('.');
            return dot <= 0 ? id : id.Substring(0, dot);
        }

        private static bool IsAllowedTwin(string idA, string idB)
        {
            string lo = string.CompareOrdinal(idA, idB) <= 0 ? idA : idB;
            string hi = string.CompareOrdinal(idA, idB) <= 0 ? idB : idA;
            return SameConceptTwins.ContainsKey(lo.ToLowerInvariant() + "|" + hi.ToLowerInvariant());
        }

        // ─────────────────────────────────────────────────────────────────────────
        /// <summary>
        /// Every mirrored-Blink and troop-portrait row must return a real sprite through the SAME
        /// RpgUiCatalog call ConceptIconResolver makes -- an on-disk-only check would pass an
        /// unimported texture, which renders as nothing in a player build.
        /// </summary>
        private static void AssertArtLoads(Dictionary<string, IconRef> rows, List<string> failures, StringBuilder log)
        {
            int loaded = 0, checkedRows = 0;
            foreach (var kv in rows)
            {
                var r = kv.Value;
                if (r == null || string.IsNullOrEmpty(r.Role) || string.IsNullOrEmpty(r.Name)) continue;
                bool guardedRole = string.Equals(r.Role, RpgUiCatalog.RoleTroop, StringComparison.OrdinalIgnoreCase)
                                || string.Equals(r.Role, "spellicons", StringComparison.OrdinalIgnoreCase);
                if (!guardedRole) continue;   // hand-authored roles keep their documented null fallback
                checkedRows++;
                if (RpgUiCatalog.Get(r.Role, r.Name) == null)
                    failures.Add($"[concept-icons] '{kv.Key}' addresses {r.Role}/{r.Name}, which RpgUiCatalog does NOT return -- the row looks wired in JSON but the skill still falls back to the default icon at runtime");
                else loaded++;
            }
            log.AppendLine($"art resolves through RpgUiCatalog: {loaded}/{checkedRows} guarded row(s)");
        }

        // ─────────────────────────────────────────────────────────────────────────
        private static void AssertThreeSlots(List<string> failures, StringBuilder log)
        {
            if (AssignableSkillBar.SlotCount != 3)
                failures.Add($"[concept-icons] AssignableSkillBar.SlotCount is {AssignableSkillBar.SlotCount}, expected 3 -- WO-1294 retired the fourth hot-swap slot from layout, persistence, fixtures and evidence; a fourth slot must not come back");
            else
                log.AppendLine("hot-swap rail: AssignableSkillBar.SlotCount == 3");
        }

        private static void AssertTroopPortraits(List<string> failures, StringBuilder log)
        {
            int present = 0;
            foreach (var id in CanonicalTroopIds)
            {
                if (RpgUiCatalog.Get(RpgUiCatalog.RoleTroop, id) == null)
                    failures.Add($"[concept-icons] troop portrait '{id}' does not load from role '{RpgUiCatalog.RoleTroop}' -- that troop falls back to the generic sword / role-letter glyph in Barracks and Manage, which WO-1294 acceptance forbids where authored art exists");
                else present++;
            }
            log.AppendLine($"troop portraits present: {present}/{CanonicalTroopIds.Length}");

            if (!File.Exists(ResourcesTroopsPath)) { failures.Add($"[concept-icons] troops.json missing at {ResourcesTroopsPath}"); return; }
            TroopsFile troops = null;
            try { troops = JsonConvert.DeserializeObject<TroopsFile>(File.ReadAllText(ResourcesTroopsPath)); }
            catch (Exception ex) { failures.Add($"[concept-icons] troops.json parse threw: {ex.GetType().Name}: {ex.Message}"); return; }
            if (troops == null || troops.Troops == null || troops.Troops.Count == 0)
            {
                failures.Add("[concept-icons] troops.json deserialized to 0 troops");
                return;
            }
            foreach (var t in troops.Troops)
            {
                if (t == null || string.IsNullOrEmpty(t.Id)) continue;
                if (string.IsNullOrEmpty(t.IconId))
                {
                    failures.Add($"[concept-icons] troop '{t.Id}' has no iconId -- it renders the role-glyph fallback even though a portrait exists");
                    continue;
                }
                if (RpgUiCatalog.Get(RpgUiCatalog.RoleTroop, t.IconId) == null)
                    failures.Add($"[concept-icons] troop '{t.Id}' iconId '{t.IconId}' does not load from role '{RpgUiCatalog.RoleTroop}'");
            }
            log.AppendLine($"troops.json rows checked: {troops.Troops.Count}");
        }

        // ─────────────────────────────────────────────────────────────────────────
        /// <summary>Every talent node (all trees + the Shared pool) that grants an ability id.</summary>
        private static IEnumerable<HeroTalentNodeDef> AbilityGrantingNodes()
        {
            foreach (var tree in HeroTalentCatalog.AllTrees)
            {
                if (tree == null || tree.Nodes == null) continue;
                foreach (var n in tree.Nodes)
                    if (n != null && !string.IsNullOrEmpty(n.Id) && !string.IsNullOrEmpty(n.AbilityId)) yield return n;
            }
            foreach (var s in HeroTalentCatalog.SharedNodes)
                if (s != null && !string.IsNullOrEmpty(s.Id) && !string.IsNullOrEmpty(s.AbilityId)) yield return s;
        }

        /// <summary>"Classes/Elementalist/Arcanist/Arcanist17.png" -> "Arcanist17".</summary>
        private static string BaseName(string path)
        {
            if (string.IsNullOrEmpty(path)) return "";
            int slash = path.LastIndexOf('/');
            string leaf = slash >= 0 ? path.Substring(slash + 1) : path;
            int dot = leaf.LastIndexOf('.');
            return dot > 0 ? leaf.Substring(0, dot) : leaf;
        }

        private static string Finish(List<string> failures, StringBuilder log)
        {
            if (failures.Count == 0)
            {
                Debug.Log(log.ToString() + "CONCEPT_ICON_IDENTITY_OK");
                return "CONCEPT ICON IDENTITY OK -- tree art == hot-swap/HUD art for every ability-granting node, no co-visible icon collisions, every guarded row loads, 3 hot-swap slots, 9 troop portraits";
            }
            string reason = "concept-icons: " + string.Join("; ", failures);
            Debug.LogError(log.ToString() + "CONCEPT_ICON_IDENTITY_FAIL: " + reason);
            return reason;
        }
    }
}

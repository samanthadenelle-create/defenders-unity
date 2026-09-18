// =============================================================================
// DungeonTreasureRegression [dungeon-treasure] (WO-850) - the deepest-room cache.
// -----------------------------------------------------------------------------
// Assembly: DeNelle.EditorRegression (references DeNelle.Village + DeNelle.Dungeons).
//
// Pins the WO-850 contract: "treasure at the deepest room, a simple crafting
// supply", with the two owner rulings baked in (FIXED bundle, no roll; recipe
// unlock on FIRST CLEAR only; prompt -> confirm panel with ONE Take CTA).
//
//   1 [bundle]        Every id in DungeonTreasureCache.FixedBundle resolves to a REAL
//                     row in materials.json - in BOTH the StreamingAssets and Resources
//                     dual-copies, which must be content-identical - and every Count > 0.
//                     A typo id is the whole point: it would deposit a nameless, icon-less
//                     item into the larder and the payout panel would print a raw id.
//   2 [deepest]       DeepestRoomId against the REAL dg_starter_loop layout returns a
//                     non-null room that is NOT the entry, EXISTS in rooms[], matches an
//                     INDEPENDENT BFS oracle computed here from the JSON, and is
//                     DETERMINISTIC across calls (a random / furthest-euclidean pick would
//                     make the chest un-regressable - see the cache's header note that
//                     furthest-euclidean and furthest-by-hops disagree on this layout).
//   3 [deepest-math]  Synthetic layouts pin the PURE math: chain -> the tail; NO
//                     connections -> null (never seat the reward on the entry); equal depth
//                     -> lowest ordinal id; a REVERSED connection still traverses (the
//                     undirected law); depth beats ordinal; missing/empty/null -> null.
//   4 [oneshot]       FirstClearKey is namespaced per dungeon (two dungeons cannot collide)
//                     and rides SeenTutorials (free-form string->bool) - i.e. NO save-schema
//                     bump. A new persisted field would be a silent schema break.
//   5 [panel]         DungeonTreasurePanel source law: built through ElarionUiKit (no
//                     hand-rolled uGUI), the shared Close is RETIRED (single-exit law, owner
//                     F8 seq 628), a "Take" CTA exists, it registers with PanelManager, it is
//                     ASCII-only, and it NEVER grants directly (no VillageInventory).
//   6 [grant-seam]    The cache grants ONLY through DungeonLootGrant and only via the
//                     panel's Take callback - never VillageInventory directly.
//   7 [spawner]       DungeonTreasureSpawner exists and carries its
//                     [RuntimeInitializeOnLoadMethod] hook - without it the chest is never
//                     injected and the deepest room is empty in every shipped build.
//
//   8 [layout-bands]   WO-1228: the FIVE authored bands (title/subtitle/well/note/cta) are
//                     pairwise NON-INTERSECTING, ordered top-down, the title band still mirrors
//                     the kit's FrameCore header zone (parsed from ElarionUiKit.cs), and the
//                     Take CTA meets MinTouchPx at 2670x1200 WITHOUT a ClampMinTouch rescue that
//                     would grow it into the first-clear band.
//   9 [layout-overflow] SIX lines then scroll (owner ruling 2026-08-26, shared with WO-1230):
//                     six rows fit the well viewport at the capture device, ten do not, the
//                     panel builds a real kit scroll zone, and the affordance reads exactly
//                     "+ N more (scroll)" - the same words WO-1230's roster uses.
//  10 [layout-control] THE ANTI-HOLLOW CONTROL (WO-1138): the same intersection oracle is run
//                     over the FROZEN pre-fix geometry and must find all THREE collisions the
//                     owner photographed. If it finds none, case 8 is measuring nothing.
//
// Markers: DUNGEON_TREASURE_OK / DUNGEON_TREASURE_FAIL.
// Standalone: run-unity-method DeNelle.Editor.Regression.DungeonTreasureRegression.RunAll
// Covenant contract Run(out reason) is DataRegression-shaped; wiring into
// DataRegression.RunAll is left to the committer (that file is lane-fenced).
//
// ACCESS NOTE: DungeonTreasureCache's math surface (DeepestRoomId / FixedBundle /
// FirstClearKey / EntryRoomId) is `internal` to DeNelle.Dungeons and there is NO
// InternalsVisibleTo anywhere in the tree, so this oracle binds them by REFLECTION.
// A rename therefore FAILS the suite loudly instead of failing to compile - which is
// the behaviour we want from a gate, but see the RESULT note: widening them to public
// would let this file (and the EditMode tests) call them directly.
// =============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using UnityEngine;
using DeNelle.Dungeons.RoomForge;
using DeNelle.Village.Items;

namespace DeNelle.Editor.Regression
{
    public static class DungeonTreasureRegression
    {
        private const string LayoutSA = "Assets/StreamingAssets/Data/Canonical/dungeon-layouts/dg_starter_loop.json";
        private const string MaterialsSA = "Assets/StreamingAssets/Data/Canonical/materials.json";
        private const string MaterialsRes = "Assets/Resources/Data/Canonical/materials.json";
        private const string CacheSrc = "Assets/_Modules/Dungeons/DungeonTreasureCache.cs";
        private const string PanelSrc = "Assets/_Modules/Dungeons/DungeonTreasurePanel.cs";

        private const string CacheType = "DeNelle.Dungeons.DungeonTreasureCache";
        private const string SpawnerType = "DeNelle.Dungeons.DungeonTreasureSpawner";

        // The captured value at authoring time (2026-08-02) for dg_starter_loop:
        // entry(0) -> corr1(1) -> junction(2) -> loop1(3) -> turn1(4) -> loop2(5)
        // -> turn2(6) -> loop3(7) -> turn3(8). Reported as a NOTE, not a failure -
        // re-authoring the layout legitimately moves the chest; the structural
        // assertions below are what must never break.
        private const string CapturedDeepest = "turn3";
        private const int CapturedDepth = 8;

        /// <summary>Standalone batch entry - prints the marker.</summary>
        public static void RunAll()
        {
            if (Run(out string reason)) Debug.Log("DUNGEON_TREASURE_OK - " + reason);
            else Debug.LogError("DUNGEON_TREASURE_FAIL: " + reason);
        }

        /// <summary>Covenant contract (DataRegression-shaped). Never throws.</summary>
        public static bool Run(out string reason)
        {
            var failures = new List<string>();
            var notes = new List<string>();
            try
            {
                Case(failures, "bundle", () => Case1_Bundle(failures, notes));
                Case(failures, "deepest", () => Case2_DeepestRealLayout(failures, notes));
                Case(failures, "deepest-math", () => Case3_DeepestMath(failures));
                Case(failures, "oneshot", () => Case4_OneShotKey(failures));
                Case(failures, "panel", () => Case5_PanelLaws(failures));
                Case(failures, "grant-seam", () => Case6_GrantSeam(failures));
                Case(failures, "spawner", () => Case7_SpawnerHook(failures));
                Case(failures, "layout-bands", () => Case8_LayoutBands(failures, notes));
                Case(failures, "layout-overflow", () => Case9_OverflowScrolls(failures, notes));
                Case(failures, "layout-control", () => Case10_LegacyGeometryControl(failures, notes));
            }
            catch (Exception ex)
            {
                failures.Add($"[suite] THREW {ex.GetType().Name}: {ex.Message}");
            }

            string noteStr = notes.Count > 0 ? " [notes: " + string.Join("; ", notes) + "]" : "";
            if (failures.Count == 0)
            {
                reason = "DUNGEON TREASURE OK - fixed bundle resolves in both materials.json copies, " +
                         "deepest-room BFS deterministic + matches an independent oracle, pure math " +
                         "(chain/tie/undirected/degenerate), per-dungeon one-shot on SeenTutorials, " +
                         "panel kit+single-exit+Take, single granting seam, spawner hooked, WO-1228 five " +
                         "exclusive bands pairwise disjoint + six-then-scroll + the legacy-geometry control" + noteStr;
                return true;
            }
            reason = "dungeon-treasure FAIL x" + failures.Count + ": " + string.Join(" | ", failures) + noteStr;
            return false;
        }

        private static void Case(List<string> failures, string name, Action body)
        {
            try { body(); }
            catch (Exception ex) { failures.Add($"[{name}] THREW {ex.GetType().Name}: {ex.Message}"); }
        }

        // =====================================================================
        //  CASE 1 - the FIXED bundle is real, payable loot
        // =====================================================================
        private static void Case1_Bundle(List<string> failures, List<string> notes)
        {
            var bundle = GetFixedBundle(failures);
            if (bundle == null) return;

            if (bundle.Length == 0)
            {
                failures.Add("[bundle] FixedBundle is EMPTY - the deepest room would pay nothing, " +
                             "and the reward panel would render '(empty)' after a full dungeon run");
                return;
            }

            // Dual-copy law: the Resources copy is what a build loads, the StreamingAssets copy
            // is what the tools edit. Drift means the editor and the player disagree on loot.
            var saIds = LoadMaterialIds(MaterialsSA, "StreamingAssets", failures);
            var resIds = LoadMaterialIds(MaterialsRes, "Resources", failures);
            if (saIds != null && resIds != null)
            {
                string na = Normalize(File.ReadAllText(MaterialsSA));
                string nb = Normalize(File.ReadAllText(MaterialsRes));
                if (na != nb)
                    failures.Add($"[bundle] materials.json dual-copy DRIFT: StreamingAssets({na.Length}b) != " +
                                 $"Resources({nb.Length}b) - the editor and the shipped player would disagree " +
                                 "on what the cache pays");
            }

            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var entry in bundle)
            {
                string id = entry.Item1;
                int count = entry.Item2;

                if (string.IsNullOrEmpty(id))
                {
                    failures.Add("[bundle] a FixedBundle row has an EMPTY id - it would be silently skipped, " +
                                 "shrinking the advertised payout without anyone noticing");
                    continue;
                }
                if (!seen.Add(id))
                    failures.Add($"[bundle] duplicate id '{id}' in FixedBundle - the panel would print the same " +
                                 "line twice while the larder receives one merged pile (payout reads dishonest)");
                if (count <= 0)
                    failures.Add($"[bundle] '{id}' has Count {count} - a non-positive count is dropped by the panel " +
                                 "AND by the grant, so the row is dead weight pretending to be loot");

                if (saIds != null && !saIds.Contains(id))
                    failures.Add($"[bundle] '{id}' is NOT a row in {MaterialsSA} - a typo'd id deposits a larder key " +
                                 "with no display name and no icon; the reward panel falls back to printing the raw id");
                if (resIds != null && !resIds.Contains(id))
                    failures.Add($"[bundle] '{id}' is NOT a row in {MaterialsRes} - the SHIPPED build (which loads the " +
                                 "Resources copy) could not name or icon this reward");

                // The live loader must agree with the raw JSON - this catches a catalog that
                // parses but drops rows (schema drift), not just a missing file.
                var def = MaterialCatalog.Find(id);
                if (def == null)
                    failures.Add($"[bundle] MaterialCatalog.Find('{id}') returned null - the runtime catalog cannot " +
                                 "resolve a bundle id even though it may exist in the JSON (loader/schema drift)");
                else if (string.IsNullOrEmpty(def.DisplayName))
                    failures.Add($"[bundle] material '{id}' has no displayName - the reward panel would print a blank " +
                                 "or raw-id line for a reward the player just earned a whole dungeon for");
            }

            notes.Add($"bundle = {bundle.Length} row(s)");
        }

        private static HashSet<string> LoadMaterialIds(string path, string label, List<string> failures)
        {
            if (!File.Exists(path))
            {
                failures.Add($"[bundle] materials.json {label} copy missing: {path} - the cache's loot cannot be validated");
                return null;
            }
            MaterialData data = null;
            try { data = JsonConvert.DeserializeObject<MaterialData>(File.ReadAllText(path)); }
            catch (Exception ex)
            {
                failures.Add($"[bundle] materials.json {label} copy failed to parse ({ex.GetType().Name}: {ex.Message})");
                return null;
            }
            if (data == null || data.Materials == null || data.Materials.Count == 0)
            {
                failures.Add($"[bundle] materials.json {label} copy deserialized EMPTY - every bundle id would look like a typo");
                return null;
            }
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var m in data.Materials) if (m != null && !string.IsNullOrEmpty(m.Id)) ids.Add(m.Id);
            return ids;
        }

        // =====================================================================
        //  CASE 2 - the REAL dg_starter_loop layout
        // =====================================================================
        private static void Case2_DeepestRealLayout(List<string> failures, List<string> notes)
        {
            if (!File.Exists(LayoutSA))
            {
                failures.Add("[deepest] layout missing: " + LayoutSA + " - cannot prove the chest lands past the entrance");
                return;
            }
            DungeonComposeLayout layout;
            try { layout = JsonConvert.DeserializeObject<DungeonComposeLayout>(File.ReadAllText(LayoutSA)); }
            catch (Exception ex)
            {
                failures.Add($"[deepest] dg_starter_loop failed to parse ({ex.GetType().Name}: {ex.Message})");
                return;
            }
            if (layout == null || layout.rooms == null || layout.rooms.Count == 0)
            {
                failures.Add("[deepest] dg_starter_loop deserialized EMPTY");
                return;
            }

            string entry = GetEntryRoomId(failures);
            if (entry == null) return;

            string got = InvokeDeepest(layout, entry, failures);
            if (string.IsNullOrEmpty(got))
            {
                failures.Add($"[deepest] DeepestRoomId returned null for dg_starter_loop (entry='{entry}') - " +
                             "the treasure would NEVER be injected in the one dungeon we ship");
                return;
            }
            if (string.Equals(got, entry, StringComparison.Ordinal))
            {
                failures.Add("[deepest] DeepestRoomId returned the ENTRY room - the reward would sit two metres " +
                             "from the door, which is the exact thing WO-850 exists to prevent");
                return;
            }
            var ids = RoomIds(layout);
            if (!ids.Contains(got))
            {
                failures.Add($"[deepest] DeepestRoomId returned '{got}', which is NOT in the layout's rooms[] - " +
                             "the spawner's FindChild would miss it and the chest would be silently skipped");
                return;
            }

            // DETERMINISM: a second call must give the same answer, or the chest moves between
            // runs and no oracle (or player memory) can ever pin it.
            string again = InvokeDeepest(layout, entry, failures);
            if (!string.Equals(got, again, StringComparison.Ordinal))
                failures.Add($"[deepest] NON-DETERMINISTIC: two calls returned '{got}' then '{again}' - the chest " +
                             "would move between runs (dictionary/enumeration order leaked into the pick)");

            // INDEPENDENT ORACLE: a BFS written here, straight from the JSON, must agree.
            string expected = OracleDeepest(layout, entry, out int depth);
            if (expected == null)
                failures.Add("[deepest] the independent BFS oracle found nothing beyond the entry in dg_starter_loop - " +
                             "the layout's connections[] were lost");
            else if (!string.Equals(expected, got, StringComparison.Ordinal))
                failures.Add($"[deepest] DeepestRoomId returned '{got}' but the independent hop-BFS oracle says " +
                             $"'{expected}' (depth {depth}) - the implementation drifted from 'furthest by hops from " +
                             "the entry, ties on lowest ordinal id'");

            if (!string.Equals(got, CapturedDeepest, StringComparison.Ordinal))
                notes.Add($"dg_starter_loop deepest is now '{got}' (was '{CapturedDeepest}' @depth {CapturedDepth} " +
                          "when WO-850 landed) - expected if the layout was re-authored");
        }

        // =====================================================================
        //  CASE 3 - the PURE math, on synthetic layouts
        // =====================================================================
        private static void Case3_DeepestMath(List<string> failures)
        {
            // (a) straight chain: entry -> a -> b -> c. The tail wins.
            var chain = Layout("entry", "a", "b", "c");
            Connect(chain, "entry", "a"); Connect(chain, "a", "b"); Connect(chain, "b", "c");
            Expect(failures, chain, "entry", "c",
                "a straight chain must seat the reward at the TAIL - anything else means hop distance is not being measured");

            // (b) NO connections at all: the reward must NOT land on the entry.
            var island = Layout("entry", "a", "b");
            Expect(failures, island, "entry", null,
                "a layout with no connections must return NULL - seating the reward ON the entry is worse than " +
                "not seating it at all (the player would find the chest before the dungeon)");

            // (c) equal depth -> LOWEST ordinal id. Built so a naive enumeration order would
            //     answer 'zulu' (it is reached through the first-sorted parent).
            var tie = Layout("entry", "p1", "p2", "zulu", "alpha");
            Connect(tie, "entry", "p1"); Connect(tie, "entry", "p2");
            Connect(tie, "p1", "zulu"); Connect(tie, "p2", "alpha");
            Expect(failures, tie, "entry", "alpha",
                "an equal-depth tie must break on the LOWEST ordinal id - otherwise the chest hops between rooms " +
                "when the layout is re-serialized and no regression can pin it");

            // (d) UNDIRECTED law: the only connection is authored CHILD -> ENTRY. A corridor is
            //     walkable both ways; a directed BFS would return null here.
            var reversed = Layout("entry", "far");
            Connect(reversed, "far", "entry");
            Expect(failures, reversed, "entry", "far",
                "connections are UNDIRECTED - a corridor authored child->parent must still be traversed, or " +
                "half the shipped layouts would report no depth at all");

            // (e) depth BEATS ordinal: a deep branch with high-ordinal ids must beat a shallow
            //     low-ordinal room.
            var branch = Layout("entry", "a1", "z1", "z2", "z3");
            Connect(branch, "entry", "a1");
            Connect(branch, "entry", "z1"); Connect(branch, "z1", "z2"); Connect(branch, "z2", "z3");
            Expect(failures, branch, "entry", "z3",
                "the DEEPER branch must win even when its ids sort last - ordinal is only the tie-breaker");

            // (f) a loop: both directions reach 'c' at depth 2 either way; the far side of the
            //     ring is the answer and it must not depend on which way BFS walks first.
            var loop = Layout("entry", "a", "b", "c");
            Connect(loop, "entry", "a"); Connect(loop, "a", "b");
            Connect(loop, "b", "c"); Connect(loop, "c", "entry");
            Expect(failures, loop, "entry", "b",
                "on a ring the far side (2 hops either way) is the deepest room - a loop must not be walked as a chain");

            // (g) degenerate inputs never crash and never guess.
            Expect(failures, null, "entry", null, "a NULL layout must return null, not throw into the spawner");
            Expect(failures, new DungeonComposeLayout(), "entry", null, "an EMPTY layout must return null");
            var noEntry = Layout("a", "b"); Connect(noEntry, "a", "b");
            Expect(failures, noEntry, "entry", null,
                "an entry room that is not in rooms[] must return null - guessing a source room would seat the " +
                "chest somewhere arbitrary");
            Expect(failures, chain, null, null, "a null entryId must return null");
            Expect(failures, chain, "", null, "an empty entryId must return null");

            // (h) a connection naming a room that does not exist must be IGNORED, not followed.
            var ghost = Layout("entry", "a");
            Connect(ghost, "entry", "a"); Connect(ghost, "a", "ghost_room");
            Expect(failures, ghost, "entry", "a",
                "a connection to a room that is not in rooms[] must be ignored - the spawner cannot find a room " +
                "that was never placed, and the chest would vanish");
        }

        // =====================================================================
        //  CASE 4 - the first-clear one-shot key
        // =====================================================================
        private static void Case4_OneShotKey(List<string> failures)
        {
            string a = InvokeFirstClearKey("dg_starter_loop", failures);
            string b = InvokeFirstClearKey("d4_sunken_crypt_spine", failures);
            if (a == null || b == null) return;

            if (string.IsNullOrEmpty(a))
                failures.Add("[oneshot] FirstClearKey returned an EMPTY key - every dungeon would share the blank " +
                             "SeenTutorials slot and only the FIRST dungeon ever cleared would grant a recipe");
            if (string.Equals(a, b, StringComparison.Ordinal))
                failures.Add($"[oneshot] FirstClearKey COLLIDES across dungeons ('{a}') - clearing dg_starter_loop " +
                             "would silently consume the crypt's first-clear recipe unlock");
            if (a.IndexOf("dg_starter_loop", StringComparison.Ordinal) < 0)
                failures.Add($"[oneshot] FirstClearKey('dg_starter_loop') = '{a}' does not carry the dungeon id - " +
                             "the key is not actually namespaced per dungeon");
            if (!string.Equals(a, InvokeFirstClearKey("dg_starter_loop", failures), StringComparison.Ordinal))
                failures.Add("[oneshot] FirstClearKey is not STABLE across calls - the one-shot would re-grant forever");

            string nullKey = InvokeFirstClearKey(null, failures);
            if (string.IsNullOrEmpty(nullKey))
                failures.Add("[oneshot] FirstClearKey(null) produced an empty key - an unnamed dungeon would write a " +
                             "blank SeenTutorials entry and poison every other dungeon's one-shot");

            // SeenTutorials is a free-form string->bool map, so a new one-shot needs NO save-schema
            // bump. If this ever stops being true, the "no schema bump" claim in the WO is a lie.
            var gsT = FindType("DeNelle.Core.State.GameState");
            if (gsT == null) { failures.Add("[oneshot] GameState type not found - cannot prove the no-schema-bump claim"); return; }
            var field = gsT.GetField("SeenTutorials", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (field == null)
            {
                failures.Add("[oneshot] GameState.SeenTutorials is GONE - the WO-850 one-shot rides it, so the " +
                             "first-clear recipe unlock would need a save-schema bump nobody authored");
            }
            else
            {
                var args = field.FieldType.IsGenericType ? field.FieldType.GetGenericArguments() : Type.EmptyTypes;
                bool freeform = args.Length == 2 && args[0] == typeof(string) && args[1] == typeof(bool);
                if (!freeform)
                    failures.Add($"[oneshot] GameState.SeenTutorials is {field.FieldType.Name}, not a free-form " +
                                 "string->bool map - an arbitrary per-dungeon key can no longer be stored without a schema change");
            }

            var svcT = FindType("DeNelle.Core.State.GameStateService");
            if (svcT == null || svcT.GetMethod("MarkTutorialSeen", new[] { typeof(string) }) == null)
                failures.Add("[oneshot] GameStateService.MarkTutorialSeen(string) not found - the persist-the-one-shot " +
                             "idiom (TorchWardenDress.GrantTorchOnce) the cache copies is gone");

            string cache = ReadSource(CacheSrc, failures);
            if (cache == null) return;
            if (cache.IndexOf("MarkTutorialSeen", StringComparison.Ordinal) < 0)
                failures.Add("[oneshot] DungeonTreasureCache no longer calls MarkTutorialSeen - the first-clear unlock " +
                             "would not PERSIST, so it would re-grant on every load");
        }

        // =====================================================================
        //  CASE 5 - the reward panel's UI laws (source lint)
        // =====================================================================
        private static void Case5_PanelLaws(List<string> failures)
        {
            string src = ReadSource(PanelSrc, failures);
            if (src == null) return;

            if (src.IndexOf("ElarionUiKit", StringComparison.Ordinal) < 0)
                failures.Add("[panel] DungeonTreasurePanel does not go through ElarionUiKit - the style-everything-" +
                             "obsidian law; a hand-rolled panel would read as a different game mid-dungeon");

            // Hand-rolled uGUI: the shape UiObsidianConformanceRegression HardFailOnNew rejects.
            var handRolled = new Regex(@"new\s+GameObject\s*\([^;]*typeof\s*\(", RegexOptions.Singleline);
            if (handRolled.IsMatch(src))
                failures.Add("[panel] DungeonTreasurePanel hand-rolls uGUI (new GameObject(..., typeof(...))) - " +
                             "kit-bypassing UI is exactly what the obsidian conformance gate exists to stop");
            foreach (var comp in new[] { "AddComponent<Image>", "AddComponent<RawImage>", "AddComponent<Text>",
                                         "AddComponent<Button>", "AddComponent<Canvas>" })
            {
                if (src.IndexOf(comp, StringComparison.Ordinal) >= 0)
                    failures.Add($"[panel] DungeonTreasurePanel calls {comp}(...) directly - build it through the kit " +
                                 "or the panel drifts off the shared frame/typography");
            }

            // Single-exit law (owner F8 seq 628): the shared Close is retired, Take is the ONE way out.
            bool retiresClose = new Regex(@"close[^;\r\n]*SetActive\s*\(\s*false\s*\)",
                                          RegexOptions.IgnoreCase).IsMatch(src);
            if (!retiresClose)
                failures.Add("[panel] the shared Close is NOT retired - two exits on a linear reward beat read as " +
                             "one choice offered twice (owner F8 seq 628), and a Close-dismiss risks eating the reward");

            // WO-1857: the CTA is now a LocalizedText resolve (dungeon.treasure.take), not a bare
            // "Take" literal - match the resolved key instead of the old literal text.
            if (!new Regex("dungeon\\.treasure\\.take").IsMatch(src))
                failures.Add("[panel] no dungeon.treasure.take CTA found - the confirm beat has no button, so the owner ruling " +
                             "'prompt then confirm' is not implemented");

            if (src.IndexOf("PanelManager.Register", StringComparison.Ordinal) < 0)
                failures.Add("[panel] the panel does not register with PanelManager - the shared Interact button " +
                             "would stay armed under the modal (the modal-arbiter law)");

            if (src.IndexOf("VillageInventory", StringComparison.Ordinal) >= 0)
                failures.Add("[panel] DungeonTreasurePanel references VillageInventory - presentation must NEVER " +
                             "grant; the ONLY grant path is the caller's onTake callback (HP B2B: presentation is a " +
                             "separate layer that never touches the objects)");

            int nonAscii = FirstNonAsciiLine(src);
            if (nonAscii > 0)
                failures.Add($"[panel] non-ASCII character at line {nonAscii} - the TMP font atlas renders it as tofu " +
                             "on device (the HudUiRegression tofu law)");
        }

        // =====================================================================
        //  CASE 6 - ONE granting seam
        // =====================================================================
        private static void Case6_GrantSeam(List<string> failures)
        {
            string src = ReadSource(CacheSrc, failures);
            if (src == null) return;

            if (src.IndexOf("VillageInventory", StringComparison.Ordinal) >= 0)
                failures.Add("[grant-seam] DungeonTreasureCache touches VillageInventory directly - dungeon loot has " +
                             "ONE seam (DungeonLootGrant); a second path means loot that skips its logging, its " +
                             "capacity rules and its save flush");
            if (src.IndexOf("DungeonLootGrant", StringComparison.Ordinal) < 0)
                failures.Add("[grant-seam] DungeonTreasureCache no longer routes through DungeonLootGrant - the cache " +
                             "is granting through some other path");
            if (src.IndexOf("DungeonTreasurePanel", StringComparison.Ordinal) < 0)
                failures.Add("[grant-seam] DungeonTreasureCache no longer shows DungeonTreasurePanel - the owner's " +
                             "'prompt then confirm' beat has been replaced by a walk-in auto-claim");

            int nonAscii = FirstNonAsciiLine(src);
            if (nonAscii > 0)
                failures.Add($"[grant-seam] non-ASCII character at line {nonAscii} in DungeonTreasureCache");
        }

        // =====================================================================
        //  CASE 7 - the injector actually arms
        // =====================================================================
        private static void Case7_SpawnerHook(List<string> failures)
        {
            var t = FindType(SpawnerType);
            if (t == null)
            {
                failures.Add("[spawner] DungeonTreasureSpawner not found - nothing injects the cache, so every " +
                             "composed dungeon ships with an empty deepest room");
                return;
            }
            var install = t.GetMethod("Install", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static);
            if (install == null)
            {
                failures.Add("[spawner] DungeonTreasureSpawner.Install not found - the auto-inject entry point is gone");
                return;
            }
            if (install.GetCustomAttributes(typeof(RuntimeInitializeOnLoadMethodAttribute), false).Length == 0)
                failures.Add("[spawner] DungeonTreasureSpawner.Install lacks [RuntimeInitializeOnLoadMethod] - it would " +
                             "never arm, and the treasure would exist only in this suite");

            if (t.GetMethod("ResolveDeepestRoomId", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static) == null)
                failures.Add("[spawner] DungeonTreasureSpawner.ResolveDeepestRoomId not found - the seat-the-chest " +
                             "path this suite pins no longer exists");
        }

        // =====================================================================
        //  REFLECTION BRIDGE (DungeonTreasureCache's math surface is `internal`)
        // =====================================================================

        private static Type CacheT() => FindType(CacheType);

        private static (string, int)[] GetFixedBundle(List<string> failures)
        {
            var t = CacheT();
            if (t == null) { failures.Add("[bundle] DungeonTreasureCache type not found - WO-850 is not in this tree"); return null; }
            var f = t.GetField("FixedBundle", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static);
            if (f == null)
            {
                failures.Add("[bundle] DungeonTreasureCache.FixedBundle not found - the ONE tuning point for the " +
                             "cache payout was renamed or removed, so nothing pins what the chest pays");
                return null;
            }
            var value = f.GetValue(null) as (string, int)[];
            if (value == null)
                failures.Add($"[bundle] FixedBundle is not a (string,int)[] (got {f.FieldType.Name}) - the oracle " +
                             "cannot read the payout, so it is unpinned");
            return value;
        }

        private static string GetEntryRoomId(List<string> failures)
        {
            var t = CacheT();
            if (t == null) { failures.Add("[deepest] DungeonTreasureCache type not found"); return null; }
            var f = t.GetField("EntryRoomId", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static);
            if (f == null)
            {
                failures.Add("[deepest] DungeonTreasureCache.EntryRoomId not found - the BFS source room is unpinned");
                return null;
            }
            return f.GetValue(null) as string;
        }

        private static MethodInfo s_deepest;
        private static MethodInfo DeepestMethod(List<string> failures)
        {
            if (s_deepest != null) return s_deepest;
            var t = CacheT();
            if (t == null) { failures.Add("[deepest] DungeonTreasureCache type not found"); return null; }
            s_deepest = t.GetMethod("DeepestRoomId", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static,
                                    null, new[] { typeof(DungeonComposeLayout), typeof(string) }, null);
            if (s_deepest == null)
                failures.Add("[deepest] DungeonTreasureCache.DeepestRoomId(DungeonComposeLayout,string) not found - " +
                             "the deepest-room math this whole feature stands on was renamed or re-signed");
            return s_deepest;
        }

        private static string InvokeDeepest(DungeonComposeLayout layout, string entryId, List<string> failures)
        {
            var m = DeepestMethod(failures);
            if (m == null) return null;
            try { return m.Invoke(null, new object[] { layout, entryId }) as string; }
            catch (TargetInvocationException tie)
            {
                var inner = tie.InnerException ?? tie;
                failures.Add($"[deepest] DeepestRoomId THREW {inner.GetType().Name}: {inner.Message} - the spawner " +
                             "calls this during scene load, so a throw here kills the injection silently");
                return null;
            }
        }

        private static string InvokeFirstClearKey(string dungeonId, List<string> failures)
        {
            var t = CacheT();
            if (t == null) { failures.Add("[oneshot] DungeonTreasureCache type not found"); return null; }
            var m = t.GetMethod("FirstClearKey", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static,
                                null, new[] { typeof(string) }, null);
            if (m == null)
            {
                failures.Add("[oneshot] DungeonTreasureCache.FirstClearKey(string) not found - the per-dungeon " +
                             "first-clear one-shot has no key, so the recipe unlock would fire every single run");
                return null;
            }
            try { return m.Invoke(null, new object[] { dungeonId }) as string; }
            catch (TargetInvocationException tie)
            {
                var inner = tie.InnerException ?? tie;
                failures.Add($"[oneshot] FirstClearKey THREW {inner.GetType().Name}: {inner.Message}");
                return null;
            }
        }

        // =====================================================================
        //  HELPERS
        // =====================================================================

        private static void Expect(List<string> failures, DungeonComposeLayout layout, string entryId,
                                   string expected, string why)
        {
            string got = InvokeDeepest(layout, entryId, failures);
            if (string.Equals(got, expected, StringComparison.Ordinal)) return;
            failures.Add($"[deepest-math] expected '{expected ?? "<null>"}' got '{got ?? "<null>"}' - {why}");
        }

        private static DungeonComposeLayout Layout(params string[] roomIds)
        {
            // cellSize is inert here - this fixture exercises the deepest-room GRAPH walk, and no
            // room is ever instantiated - but keep it on the kit canon so it does not read as a
            // stale 6u claim (WO-922).
            var l = new DungeonComposeLayout { dungeonId = "synthetic", cellSize = RoomForgeCanon.Cell };
            l.rooms = new List<ComposeRoomPlacement>();
            l.connections = new List<ComposeConnection>();
            foreach (var id in roomIds)
                l.rooms.Add(new ComposeRoomPlacement { prefab = "Straight", instanceId = id });
            return l;
        }

        private static void Connect(DungeonComposeLayout l, string from, string to)
        {
            l.connections.Add(new ComposeConnection
            {
                fromInstance = from,
                fromSocket = "n_door_01",
                toInstance = to,
                toSocket = "s_door_01"
            });
        }

        private static HashSet<string> RoomIds(DungeonComposeLayout layout)
        {
            var ids = new HashSet<string>(StringComparer.Ordinal);
            if (layout?.rooms == null) return ids;
            foreach (var r in layout.rooms)
            {
                if (r == null) continue;
                string id = string.IsNullOrEmpty(r.instanceId) ? r.prefab : r.instanceId;
                if (!string.IsNullOrEmpty(id)) ids.Add(id);
            }
            return ids;
        }

        /// <summary>
        /// An INDEPENDENT hop-BFS: furthest room from the entry over UNDIRECTED connections,
        /// ties on lowest ordinal id. Deliberately written straight from the JSON so it can
        /// disagree with the implementation instead of inheriting its bugs.
        /// </summary>
        private static string OracleDeepest(DungeonComposeLayout layout, string entryId, out int maxDepth)
        {
            maxDepth = 0;
            var ids = RoomIds(layout);
            if (!ids.Contains(entryId)) return null;

            var adj = new Dictionary<string, SortedSet<string>>(StringComparer.Ordinal);
            if (layout.connections != null)
            {
                foreach (var c in layout.connections)
                {
                    if (c == null) continue;
                    if (!ids.Contains(c.fromInstance) || !ids.Contains(c.toInstance)) continue;
                    if (!adj.TryGetValue(c.fromInstance, out var fa)) { fa = new SortedSet<string>(StringComparer.Ordinal); adj[c.fromInstance] = fa; }
                    if (!adj.TryGetValue(c.toInstance, out var ta)) { ta = new SortedSet<string>(StringComparer.Ordinal); adj[c.toInstance] = ta; }
                    fa.Add(c.toInstance);
                    ta.Add(c.fromInstance);
                }
            }

            var dist = new Dictionary<string, int>(StringComparer.Ordinal) { [entryId] = 0 };
            var q = new Queue<string>();
            q.Enqueue(entryId);
            while (q.Count > 0)
            {
                string cur = q.Dequeue();
                if (!adj.TryGetValue(cur, out var ns)) continue;
                foreach (var n in ns)
                {
                    if (dist.ContainsKey(n)) continue;
                    dist[n] = dist[cur] + 1;
                    q.Enqueue(n);
                }
            }

            string best = null;
            foreach (var kv in dist)
            {
                if (kv.Value <= 0) continue;
                if (kv.Value > maxDepth || (kv.Value == maxDepth && string.CompareOrdinal(kv.Key, best) < 0))
                {
                    maxDepth = kv.Value;
                    best = kv.Key;
                }
            }
            return best;
        }

        // =====================================================================
        //  WO-1228 GEOMETRY ORACLE - cases 8, 9, 10
        // ---------------------------------------------------------------------
        //  The owner's device capture (Seeker 2026.08.26.342290, 2670x1200) showed the title
        //  drawn over the subtitle, the last cache line clipped, and "Take" painted over the
        //  first-clear sentence. These three cases pin the fix AND, in case 10, keep the
        //  PRE-FIX geometry executable as a CONTROL so the oracle can never go hollow
        //  (WO-1138): a detector that reports "no collisions" on a layout we KNOW collided is
        //  a detector that would report "no collisions" on the next broken one too.
        // =====================================================================

        /// <summary>The Seeker's screen (the device the defect was captured on).</summary>
        private const float RefScreenW = 2670f;
        private const float RefScreenH = 1200f;

        /// <summary>
        /// Replicate ElarionUiKit.PostScaleCanvasHeight for a ScaleWithScreenSize modal canvas
        /// (reference 1080x1920, MatchWidthOrHeight 0.5 - see ElarionUiKit.BuildModalCanvas).
        /// Reading a live rect is NOT an option in a batch oracle, and is wrong on a canvas's
        /// creation frame anyway (the F8-5 DlgLayout capture: raw screen px, not post-scale).
        /// </summary>
        private static float ReferenceCanvasHeight()
        {
            const float refW = 1080f, refH = 1920f;
            double logW = Math.Log(RefScreenW / refW, 2d);
            double logH = Math.Log(RefScreenH / refH, 2d);
            double scale = Math.Pow(2d, 0.5d * logW + 0.5d * logH);
            return (float)(RefScreenH / scale);
        }

        private static bool Intersect(Vector4 a, Vector4 b)
        {
            return a.x < b.z && b.x < a.z && a.y < b.w && b.y < a.w;
        }

        // =====================================================================
        //  CASE 8 - the five bands are EXCLUSIVE, and the CTA meets the touch floor
        //           without growing into the band above it
        // =====================================================================
        private static void Case8_LayoutBands(List<string> failures, List<string> notes)
        {
            var bands = DeNelle.Dungeons.DungeonTreasurePanel.Layout.Bands();
            var names = DeNelle.Dungeons.DungeonTreasurePanel.Layout.BandNames();
            if (bands.Length != names.Length || bands.Length != 5)
            {
                failures.Add($"[layout-bands] expected 5 named bands, got {bands.Length} rect(s) / " +
                             $"{names.Length} name(s) - the band table and its labels have drifted apart");
                return;
            }

            for (int i = 0; i < bands.Length; i++)
            {
                if (bands[i].z <= bands[i].x || bands[i].w <= bands[i].y)
                    failures.Add($"[layout-bands] band '{names[i]}' is degenerate " +
                                 $"({bands[i].x:F3},{bands[i].y:F3})-({bands[i].z:F3},{bands[i].w:F3}) - " +
                                 "a zero/negative rect renders nothing and cannot be seen to collide");
            }

            // THE assertion the ticket asks for: no two named elements' rects intersect.
            for (int i = 0; i < bands.Length; i++)
                for (int j = i + 1; j < bands.Length; j++)
                    if (Intersect(bands[i], bands[j]))
                        failures.Add($"[layout-bands] '{names[i]}' and '{names[j]}' INTERSECT " +
                                     $"({bands[i].x:F3},{bands[i].y:F3})-({bands[i].z:F3},{bands[i].w:F3}) vs " +
                                     $"({bands[j].x:F3},{bands[j].y:F3})-({bands[j].z:F3},{bands[j].w:F3}) - " +
                                     "this is the WO-1228 defect class: two elements sharing one band");

            // Reading order top-to-bottom: title, subtitle, well, note, cta.
            for (int i = 0; i < bands.Length - 1; i++)
                if (bands[i].y < bands[i + 1].w)
                    failures.Add($"[layout-bands] '{names[i]}' (yMin {bands[i].y:F3}) does not sit ABOVE " +
                                 $"'{names[i + 1]}' (yMax {bands[i + 1].w:F3}) - the reading order is authored " +
                                 "top-down and a re-ordered table would put the CTA in the middle of the copy");

            // The kit OWNS band 1: BuildObsidianPanel seats chrome.title in FrameCore's header
            // zone. Re-read that zone from the kit source so the panel's mirror can never drift.
            var kitTitle = FrameCoreHeaderZoneFromKitSource(failures);
            if (kitTitle.HasValue)
            {
                var t = DeNelle.Dungeons.DungeonTreasurePanel.Layout.TitleBand;
                var k = kitTitle.Value;
                if (Mathf.Abs(t.x - k.x) > 0.0005f || Mathf.Abs(t.y - k.y) > 0.0005f ||
                    Mathf.Abs(t.z - k.z) > 0.0005f || Mathf.Abs(t.w - k.w) > 0.0005f)
                    failures.Add($"[layout-bands] TitleBand ({t.x:F3},{t.y:F3},{t.z:F3},{t.w:F3}) no longer mirrors " +
                                 $"ElarionUiKit's FrameCore header zone ({k.x:F3},{k.y:F3},{k.z:F3},{k.w:F3}) - the " +
                                 "kit draws the title THERE, so a stale mirror means the panel is asserting " +
                                 "non-collision against a rect the title does not occupy (exactly how the original " +
                                 "title-over-subtitle overlap went unseen)");
            }

            // Touch floor, measured at the capture device - and the CTA must still clear the
            // band above it AFTER any ClampMinTouch rescue, which is what broke hero-select.
            float canvasH = ReferenceCanvasHeight();
            float ctaH = DeNelle.Dungeons.DungeonTreasurePanel.Layout.CtaHeightPx(canvasH);
            float modalH = DeNelle.Dungeons.DungeonTreasurePanel.Layout.ModalHeightPx(canvasH);
            if (ctaH < DeNelle.Core.UI.ElarionUiKit.MinTouchPx)
                failures.Add($"[layout-bands] Take is {ctaH:F1} canvas px tall at {RefScreenW:0}x{RefScreenH:0}, " +
                             $"under the {DeNelle.Core.UI.ElarionUiKit.MinTouchPx:F0}px floor - ClampMinTouch would " +
                             "GROW it symmetrically into the first-clear band above (the hero-select failure). " +
                             "Author CtaBand tall enough; do not rely on the rescue");

            float grow = Mathf.Max(0f, DeNelle.Core.UI.ElarionUiKit.MinTouchPx - ctaH) * 0.5f;
            var cta = DeNelle.Dungeons.DungeonTreasurePanel.Layout.CtaBand;
            var note = DeNelle.Dungeons.DungeonTreasurePanel.Layout.NoteBand;
            float ctaTopAfterClamp = cta.w + (modalH > 1f ? grow / modalH : 0f);
            if (ctaTopAfterClamp >= note.y)
                failures.Add($"[layout-bands] after a MinTouchPx rescue the Take band would top out at " +
                             $"{ctaTopAfterClamp:F3}, at or above the first-clear band's floor {note.y:F3} - " +
                             "satisfying the touch floor may NOT create a new overlap (WO-1228 constraint)");

            notes.Add($"bands@{RefScreenW:0}x{RefScreenH:0}: canvasH={canvasH:F0} modalH={modalH:F0} " +
                      $"ctaH={ctaH:F1} (floor {DeNelle.Core.UI.ElarionUiKit.MinTouchPx:F0})");
        }

        /// <summary>Parse FrameCore's header zone out of the kit source (ZonesFor is private and
        /// there is no InternalsVisibleTo - the same reflection-or-source bind case 5 uses).</summary>
        private static Vector4? FrameCoreHeaderZoneFromKitSource(List<string> failures)
        {
            const string KitSrc = "Assets/_Modules/Core/UI/ElarionUiKit.cs";
            string src = ReadSource(KitSrc, failures);
            if (src == null) return null;
            int at = src.IndexOf("case RpgUiCatalog.FrameCore:", StringComparison.Ordinal);
            if (at < 0)
            {
                failures.Add("[layout-bands] no 'case RpgUiCatalog.FrameCore:' in ElarionUiKit.ZonesFor - the frame " +
                             "the treasure modal names has no zone case, so the title lands wherever the default puts it");
                return null;
            }
            int end = src.IndexOf("break;", at, StringComparison.Ordinal);
            if (end < 0) end = Math.Min(src.Length, at + 4000);
            var m = new Regex(@"z\.header\s*=\s*new\s+Vector4\(\s*([-0-9.]+)f\s*,\s*([-0-9.]+)f\s*,\s*([-0-9.]+)f\s*,\s*([-0-9.]+)f\s*\)")
                .Match(src.Substring(at, end - at));
            if (!m.Success)
            {
                failures.Add("[layout-bands] could not parse z.header from the FrameCore zone case - the oracle cannot " +
                             "prove the panel's TitleBand still matches where the kit draws the title");
                return null;
            }
            return new Vector4(
                float.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture),
                float.Parse(m.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture),
                float.Parse(m.Groups[3].Value, System.Globalization.CultureInfo.InvariantCulture),
                float.Parse(m.Groups[4].Value, System.Globalization.CultureInfo.InvariantCulture));
        }

        // =====================================================================
        //  CASE 9 - SIX LINES THEN SCROLL (owner ruling 2026-08-26). A 10-line cache
        //           SCROLLS inside the same well; it never clips and never grows the modal.
        // =====================================================================
        private static void Case9_OverflowScrolls(List<string> failures, List<string> notes)
        {
            if (DeNelle.Dungeons.DungeonTreasurePanel.Layout.VisibleRows != 6)
                failures.Add($"[layout-overflow] VisibleRows is " +
                             $"{DeNelle.Dungeons.DungeonTreasurePanel.Layout.VisibleRows}, not 6 - the owner ruled " +
                             "SIX lines then scroll on 2026-08-26 and WO-1230's roster adopts the same rule; two " +
                             "list surfaces with two conventions is what these tickets were written together to prevent");

            if (DeNelle.Dungeons.DungeonTreasurePanel.Layout.Overflows(6))
                failures.Add("[layout-overflow] six lines are reported as OVERFLOWING - the sixth line must be " +
                             "visible, not hinted at");
            if (!DeNelle.Dungeons.DungeonTreasurePanel.Layout.Overflows(7))
                failures.Add("[layout-overflow] seven lines are NOT reported as overflowing - the seventh line " +
                             "would render outside the well or be silently dropped");

            string none = DeNelle.Dungeons.DungeonTreasurePanel.Layout.OverflowHint(5);
            if (!string.IsNullOrEmpty(none))
                failures.Add($"[layout-overflow] a 5-line cache renders the hint '{none}' - the affordance must be " +
                             "absent when everything fits");

            // The WO-1230 affordance, word for word.
            string ten = DeNelle.Dungeons.DungeonTreasurePanel.Layout.OverflowHint(10);
            if (ten != "+ 4 more (scroll)")
                failures.Add($"[layout-overflow] a 10-line cache hints '{ten}', expected '+ 4 more (scroll)' - " +
                             "WO-1230's Army roster uses that exact affordance and the two panels must not diverge");
            if (!new Regex(@"^\+ \d+ more \(scroll\)$").IsMatch(ten))
                failures.Add($"[layout-overflow] the hint '{ten}' does not match the shared '+ N more (scroll)' shape");
            foreach (char c in ten)
                if (c > (char)126)
                {
                    failures.Add("[layout-overflow] the overflow hint carries a non-ASCII character - TMP renders it " +
                                 "as tofu on device");
                    break;
                }

            // Geometry: six rows really do fit the viewport at the capture device, and ten rows
            // really do exceed it (so the ScrollRect has something to scroll).
            float canvasH = ReferenceCanvasHeight();
            float viewport = DeNelle.Dungeons.DungeonTreasurePanel.Layout.ViewportHeightPx(canvasH);
            float rowPx = DeNelle.Dungeons.DungeonTreasurePanel.Layout.RowHeightPx(canvasH);
            float chrome = 2f * DeNelle.Dungeons.DungeonTreasurePanel.Layout.ScrollPaddingPx
                         + (DeNelle.Dungeons.DungeonTreasurePanel.Layout.VisibleRows - 1)
                           * DeNelle.Dungeons.DungeonTreasurePanel.Layout.ScrollSpacingPx;
            float sixTall = DeNelle.Dungeons.DungeonTreasurePanel.Layout.VisibleRows * rowPx + chrome;

            if (rowPx < DeNelle.Dungeons.DungeonTreasurePanel.Layout.MinRowPx)
                failures.Add($"[layout-overflow] the row pitch is {rowPx:F1}px, below the " +
                             $"{DeNelle.Dungeons.DungeonTreasurePanel.Layout.MinRowPx:F0}px legibility floor - TMP " +
                             "CULLS a whole line when the floor font's line height exceeds the cell, so the row " +
                             "would render blank rather than small");
            if (sixTall > viewport + 0.5f)
                failures.Add($"[layout-overflow] six rows need {sixTall:F1}px but the well viewport is only " +
                             $"{viewport:F1}px at {RefScreenW:0}x{RefScreenH:0} - the sixth line would be CLIPPED, " +
                             "which is the exact defect reported ('Spring Water x1' cut off at five)");

            float tenTall = 10f * rowPx + chrome;
            if (tenTall <= viewport)
                failures.Add($"[layout-overflow] ten rows ({tenTall:F1}px) fit the viewport ({viewport:F1}px), so " +
                             "the overflow path is never exercised - the assertion would be vacuous");

            // A hint alone is not a scroll: the panel must build a REAL kit scroll zone.
            string panel = ReadSource(PanelSrc, failures);
            if (panel != null)
            {
                if (panel.IndexOf("MakeScrollZone", StringComparison.Ordinal) < 0)
                    failures.Add("[layout-overflow] the panel does not call ElarionUiKit.MakeScrollZone - a fixed " +
                                 "well with a hint line and no ScrollRect is still clipping, just politely");
                if (panel.IndexOf("BuildObsidianModal", StringComparison.Ordinal) < 0)
                    failures.Add("[layout-overflow] the panel no longer builds through BuildObsidianModal");
            }

            notes.Add($"well@{RefScreenW:0}x{RefScreenH:0}: viewport={viewport:F0}px rowPx={rowPx:F1} " +
                      $"six={sixTall:F0}px ten={tenTall:F0}px");
        }

        // =====================================================================
        //  CASE 10 - THE CONTROL (WO-1138 anti-hollow). Run the SAME intersection oracle
        //            over the PRE-FIX geometry and require it to find the three collisions
        //            the owner photographed. If this case ever passes silently, case 8 is
        //            measuring nothing.
        // =====================================================================
        private static void Case10_LegacyGeometryControl(List<string> failures, List<string> notes)
        {
            // The WO-1041 pixel flow, frozen: modal 0.20,0.24-0.80,0.78; body hung from
            // chrome.content's TOP edge at StackTopPx=24, HeadingPx=66, LinePx=60 per payout
            // line (five lines in the captured cache), StackGapPx=14; CTA 0.34,0.05-0.66,0.245.
            float canvasH = ReferenceCanvasHeight();
            float modalH = (0.78f - 0.24f) * canvasH;
            if (modalH <= 1f)
            {
                failures.Add("[layout-control] the reference canvas resolved to zero height - the control cannot run");
                return;
            }

            float cursor = 24f;
            Vector4 legacySubtitle = LegacyBand(0.08f, 0.92f, cursor, 66f, modalH);
            cursor += 66f + 14f;
            Vector4 legacyPayout = LegacyBand(0.08f, 0.92f, cursor, 60f * 5f, modalH);
            cursor += 60f * 5f + 14f;
            Vector4 legacyNote = LegacyBand(0.06f, 0.94f, cursor, 66f, modalH);
            Vector4 legacyTitle = DeNelle.Dungeons.DungeonTreasurePanel.Layout.TitleBand;   // kit-owned, unchanged
            Vector4 legacyCta = new Vector4(0.34f, 0.05f, 0.66f, 0.245f);

            int found = 0;
            if (Intersect(legacyTitle, legacySubtitle)) found++;
            else failures.Add("[layout-control] the oracle does NOT see the captured title-over-subtitle overlap in " +
                              "the pre-fix geometry - it is therefore blind to the defect it is supposed to catch " +
                              "(WO-1138 hollow-test class)");
            if (Intersect(legacyPayout, legacyCta)) found++;
            else failures.Add("[layout-control] the oracle does NOT see the captured payout-under-Take clip in the " +
                              "pre-fix geometry - it would pass a panel whose last loot line is behind the button");
            if (Intersect(legacyNote, legacyCta)) found++;
            else failures.Add("[layout-control] the oracle does NOT see the captured 'First clear -- [Take] membered' " +
                              "overlap in the pre-fix geometry - it would pass a panel whose footer is under the CTA");

            if (found < 3)
                failures.Add($"[layout-control] only {found}/3 of the photographed collisions were detected - case 8 " +
                             "is not measuring what the owner saw");

            notes.Add($"control: {found}/3 pre-fix collisions detected (oracle proven non-vacuous)");
        }

        /// <summary>A legacy top-down pixel band, converted to modal fractions (y bottom-to-top).</summary>
        private static Vector4 LegacyBand(float x0, float x1, float topPx, float heightPx, float modalHeightPx)
        {
            float yMax = 1f - (topPx / modalHeightPx);
            float yMin = 1f - ((topPx + heightPx) / modalHeightPx);
            return new Vector4(x0, yMin, x1, yMax);
        }

        private static string ReadSource(string path, List<string> failures)
        {
            if (!File.Exists(path))
            {
                failures.Add($"[source] {path} not found - WO-850 is not in this tree (or the file moved without " +
                             "updating this oracle)");
                return null;
            }
            try { return File.ReadAllText(path); }
            catch (Exception ex)
            {
                failures.Add($"[source] could not read {path}: {ex.GetType().Name}: {ex.Message}");
                return null;
            }
        }

        private static int FirstNonAsciiLine(string src)
        {
            int line = 1;
            for (int i = 0; i < src.Length; i++)
            {
                char c = src[i];
                if (c == '\n') { line++; continue; }
                if (c > (char)126 && c != '\r') return line;
            }
            return 0;
        }

        private static string Normalize(string s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            if (s.Length > 0 && s[0] == (char)0xFEFF) s = s.Substring(1);
            return s.Replace("\r\n", "\n").Replace("\r", "\n");
        }

        private static Type FindType(string full)
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                var t = asm.GetType(full, false);
                if (t != null) return t;
            }
            return null;
        }
    }
}

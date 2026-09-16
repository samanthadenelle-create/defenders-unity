// =============================================================================
// ManageDefenseCardRegression - WO-1422 section 5: the Manage DEFENSE tab takes
// the WO-1418 Buildings shape (portrait rail + one selected card + BUILDING NOW
// + one footer row), and the paged list it replaces cannot come back.
// Marker: MANAGE_DEFENSE_CARD_OK / MANAGE_DEFENSE_CARD_FAIL <case>.
// -----------------------------------------------------------------------------
// Assembly: DeNelle.EditorRegression (editor-only). Registered in DataRegression.RunAll.
// Style/contract mirrors ManageBuildingsCardRegression, which this is modelled on:
// a LIVE half that stands up a GameState fixture and drives ManageScreenVM, and a
// SOURCE half that reads ManageScreenPanel.cs / ManageScreenVM.cs as text.
//
// WHY THIS SUITE EXISTS (measured, WO-1422 section 1):
//   RunManageOperationalCaptureHeadless, Builds/capman4, 2026-09-06 01:26 -
//   ManageDefense_2670x1200.png shows a tab with ONE placed archer tower and
//   nothing to do: two thirds of the panel is empty black, no portrait, no
//   selected item, no sense of a ladder.
//
// THE LOAD-BEARING RULING THIS GUARDS (WO-1422 ruling 3.1):
//   the rail lists ONE ROW PER TYPE, never per placed instance. `wall_wood` is
//   upgradable and a town has many segments; a per-instance rail is UNBOUNDED.
//   The pre-existing comment at ManageScreenVM.BuildDefenseBrowse (:840-844)
//   warns about exactly this, which is why [walls-do-not-explode] exists.
//
// EVERY case carries a one-line REVERT RECIPE so the CLI can prove RED, restore,
// and prove GREEN. A missing fixture is a FAIL that names itself - never a skip,
// never a hollow pass (the UpgradeQueueFullSurfaceRegression ruling).
// =============================================================================
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using DeNelle.Core.Catalog;
using DeNelle.Core.Jobs;
using DeNelle.Core.Manage;   // ManageArt.BuildingPortraitKey - the ONE portrait-key producer
using DeNelle.Core.State;
using DeNelle.Core.UI;
using DeNelle.Village.Buildings.Progression;
using DeNelle.Village.UI;
using UnityEngine;

namespace DeNelle.Editor
{
    /// <summary>Headless contract for WO-1422's compact Defense destination.</summary>
    public static class ManageDefenseCardRegression
    {
        private const string PanelPath = "Assets/_Modules/Village/UI/Manage/ManageScreenPanel.cs";
        private const string VmPath = "Assets/_Modules/Village/UI/Manage/ManageScreenVM.cs";
        // ⛔ PortraitRoot ("Assets/Resources/Portraits") WAS HERE AND IS DELETED. It named the MIXED
        // ROOT folder the retired display-name slugs addressed. Every path this suite checks is now
        // built from ManageArt.BuildingPortraitKey, so the folder is the producer's to decide and
        // there is nothing here to keep in sync with it.

        // The fixture ids and cells. Declared once so the assertions and the log
        // agree, and so the "first instance at the LOWEST level" claim is provable
        // rather than incidental: the L2 tower is placed FIRST in BaseLayout, so a
        // naive "first placed instance" implementation targets (3,2) and
        // [lowest-level-targeted] goes RED. Only "lowest" yields (2,2).
        private const string ArcherId = "tower_ground_archer";
        private const string WallId = "wall_wood";
        private const string BallistaId = "tower_ballista";

        public static bool Run(out string reason)
        {
            var failures = new List<string>();
            var log = new StringBuilder("=== ManageDefenseCardRegression (WO-1422) ===\n");
            try
            {
                CheckLiveModel(failures, log);
                CheckPanelSource(failures, log);
                CheckDefenseTierPortraitCoverage(failures, log);
            }
            catch (Exception ex)
            {
                failures.Add("suite threw " + ex.GetType().Name + ": " + ex.Message);
            }

            if (failures.Count == 0)
            {
                reason = "MANAGE_DEFENSE_CARD_OK Defense rail is one row per TYPE, targets the lowest placed " +
                         "instance, shares the Builder band, reaches its tier art, and keeps the Build-defense door";
                Debug.Log(reason + "\n" + log);
                return true;
            }

            reason = "MANAGE_DEFENSE_CARD_FAIL " + string.Join(" | ", failures);
            Debug.LogError(reason + "\n" + log);
            return false;
        }

        // =====================================================================
        //  LIVE half - drive ManageScreenVM against a real GameState fixture.
        // =====================================================================
        private static void CheckLiveModel(List<string> failures, StringBuilder log)
        {
            GameStateService prior = GameStateService.Instance;
            GameObject host = null;
            GameState fixture = null;
            try
            {
                fixture = ScriptableObject.CreateInstance<GameState>();
                fixture.Onboarded = true;
                fixture.VillageTier = 4;

                // ORDER IS PART OF THE TEST. Archer L2 is placed BEFORE archer L1.
                fixture.BaseLayout = new List<PlacedStructureData>
                {
                    new PlacedStructureData(ArcherId, 3, 2, 0, 2),   // higher level, listed FIRST
                    new PlacedStructureData(ArcherId, 2, 2, 0, 1),   // the one the CTA must target
                    new PlacedStructureData(BallistaId, 9, 9, 0, 3), // at its ceiling -> "Max"
                };
                // Eight wall segments: the unbounded-rail trap (ruling 3.1).
                for (int i = 0; i < 8; i++)
                    fixture.BaseLayout.Add(new PlacedStructureData(WallId, 10 + i, 4, 0, 1));

                fixture.Wood = 100000;
                fixture.Iron = 100000;
                var balances = fixture.Resources;
                balances.Stone = 100000;
                balances.Coins = 100000;
                balances.Crystals = 100000;
                fixture.Resources = balances;
                fixture.ObsidianQueue = ObsidianQueueState.Empty();

                host = new GameObject("GSS (manage-defense-card oracle)");
                var service = host.AddComponent<GameStateService>();
                if (!InstallState(service, fixture))
                {
                    // NOT A SKIP: a suite that green-passes on an unreachable seam
                    // asserts nothing, most eagerly on the day the seam breaks.
                    failures.Add("[fixture] GameStateService state seam is not reflectable, so the LIVE Defense " +
                                 "cases (one row per type, lowest-level target, wall explosion) could not run. " +
                                 "This is a FAIL, not a skip.");
                    return;
                }

                // The fixture must be able to EXERCISE the thing under test. If the
                // catalog cannot resolve the ids, the suite names itself rather than
                // reporting a green nothing (WO-1422 lane D found the shipped capture
                // fixture could not exercise the card at all).
                if (CatalogRegistry.Get(ArcherId) == null || CatalogRegistry.Get(WallId) == null ||
                    CatalogRegistry.Get(BallistaId) == null)
                {
                    failures.Add("[fixture] the catalog cannot resolve '" + ArcherId + "' / '" + WallId + "' / '" +
                                 BallistaId + "', so no Defense case below could have been exercised. FAIL, not a skip.");
                    return;
                }

                var vm = new ManageScreenVM();
                vm.SelectTab(ManageTab.Defense);
                vm.Rebuild();

                var choices = vm.DefenseChoices;
                if (choices == null)
                {
                    failures.Add("[one-choice-per-type] ManageScreenVM.DefenseChoices is null - the Defense " +
                                 "destination has no model to paint.");
                    return;
                }

                log.AppendLine("Defense choices projected = " + choices.Count);
                for (int i = 0; i < choices.Count; i++)
                {
                    var c = choices[i];
                    log.AppendLine("  [" + (c == null ? "<null>" : c.StateWord) + "] " +
                                   (c == null ? "" : c.Id + " x" + c.PlacedCount + " L" + c.Level + "/" + c.MaxLevel +
                                                     " name='" + c.Name + "' key='" + c.JobKey + "'"));
                }

                // -------------------------------------------------------------
                // CASE 1  [one-choice-per-type]
                // REVERT RECIPE (RED): in BuildDefenseChoices, key the tally on
                //   `placed.itemId + "#" + placed.level` instead of `placed.itemId`.
                // -------------------------------------------------------------
                var archers = FindAll(choices, ArcherId);
                if (archers.Count != 1)
                    failures.Add("[one-choice-per-type] two placed '" + ArcherId + "' at DIFFERENT levels produced " +
                                 archers.Count + " rail row(s); the rail must list ONE ROW PER TYPE (ruling 3.1), " +
                                 "not one per placed instance.");
                else if (archers[0].PlacedCount != 2)
                    failures.Add("[one-choice-per-type] the '" + ArcherId + "' row reports PlacedCount=" +
                                 archers[0].PlacedCount + "; two are standing, so the card cannot say how many.");

                // Exactly three TYPES are placed (archer, wall, ballista). A maxed
                // type stays visible - the same deliberate delta WO-1418 made when
                // it stopped hiding maxed buildings.
                if (choices.Count != 3)
                    failures.Add("[one-choice-per-type] expected exactly 3 Defense rows (archer + wall + the MAXED " +
                                 "ballista) for 11 placed structures of 3 types; got " + choices.Count + ".");

                // -------------------------------------------------------------
                // CASE 2  [lowest-level-targeted]
                // REVERT RECIPE (RED): in BuildDefenseChoices, take the HIGHEST
                //   placed level for the tally (Mathf.Max instead of Mathf.Min)
                //   and compose JobKey from that instance.
                // -------------------------------------------------------------
                if (archers.Count == 1)
                {
                    var archer = archers[0];
                    if (archer.Level != 1)
                        failures.Add("[lowest-level-targeted] the '" + ArcherId + "' row reports Level=" + archer.Level +
                                     "; L1 and L2 are placed, so the row must speak for the LOWEST (L1) - that is the " +
                                     "instance its CTA upgrades.");
                    string expectedKey = PlacedUpgradeKey.Compose(ArcherId, 2, 2);
                    if (!string.Equals(archer.JobKey, expectedKey, StringComparison.Ordinal))
                        failures.Add("[lowest-level-targeted] JobKey='" + archer.JobKey + "' but the FIRST instance at " +
                                     "the lowest level stands at grid 2,2 -> '" + expectedKey + "'. The CTA would " +
                                     "upgrade the wrong tower.");
                    if (archer.Activate == null)
                        failures.Add("[lowest-level-targeted] the '" + ArcherId + "' row has a null Activate - a row " +
                                     "that does nothing is not a door.");
                }

                // -------------------------------------------------------------
                // CASE 3  [no-grid-labels]
                // REVERT RECIPE (RED): set Name to
                //   `NameOf(entry, id) + " - grid " + cellX + ", " + cellZ + " - L" + level + " -> L" + (level+1)`
                //   (the retired BuildDefenseBrowse label composition, VM:849-851).
                // -------------------------------------------------------------
                for (int i = 0; i < choices.Count; i++)
                {
                    string name = choices[i] != null ? (choices[i].Name ?? "") : "";
                    if (name.Contains("grid ") || name.Contains("->"))
                        failures.Add("[no-grid-labels] Defense row name '" + name + "' carries developer copy " +
                                     "(a grid coordinate or an arrow). Ruling 3.1: a grid coordinate never reaches " +
                                     "the player, because the rail is per TYPE.");
                }

                // -------------------------------------------------------------
                // CASE 4  [every-choice-speaks]
                // REVERT RECIPE (RED): assign `Description = ""` (or `StateWord = ""`)
                //   in BuildDefenseChoices.
                // -------------------------------------------------------------
                var allowed = new HashSet<string>(StringComparer.Ordinal) { "Upgradable", "Max", "Building" };
                for (int i = 0; i < choices.Count; i++)
                {
                    var c = choices[i];
                    if (c == null)
                    {
                        failures.Add("[every-choice-speaks] null Defense choice at index " + i);
                        continue;
                    }
                    if (string.IsNullOrWhiteSpace(c.Description))
                        failures.Add("[every-choice-speaks] '" + c.Id + "' has no Description - the card would paint " +
                                     "a nameless slab.");
                    if (!allowed.Contains(c.StateWord ?? ""))
                        failures.Add("[every-choice-speaks] '" + c.Id + "' StateWord='" + c.StateWord + "' is outside " +
                                     "{Upgradable, Max, Building}. The owner is red/green colourblind - the WORD is " +
                                     "the only carrier of state.");
                    if (string.IsNullOrWhiteSpace(c.PlacedText))
                        failures.Add("[every-choice-speaks] '" + c.Id + "' has no PlacedText - the card cannot say how " +
                                     "many are placed and at what level (ruling 3.1).");
                }

                // The maxed ballista must actually be REACHED, or CASE 4's Max arm
                // and the "no CTA at max" contract were never exercised.
                var ballista = FindAll(choices, BallistaId);
                if (ballista.Count != 1 || !string.Equals(ballista.Count == 1 ? ballista[0].StateWord : null,
                                                          "Max", StringComparison.Ordinal))
                    failures.Add("[every-choice-speaks] the ballista placed at its ceiling (L3 of 3) did not project " +
                                 "exactly one row reading StateWord='Max'; the Max arm of this case was never " +
                                 "exercised, so a green here would be hollow.");
                else if (ballista[0].Activate != null)
                    failures.Add("[every-choice-speaks] the MAXED ballista carries a non-null Activate - a card at its " +
                                 "ceiling must not offer an upgrade it cannot perform.");

                // -------------------------------------------------------------
                // CASE 5  [walls-do-not-explode]  ** guards ruling 3.1 **
                // REVERT RECIPE (RED): emit one choice per placed instance
                //   (drop the tally and add a DefenseChoiceVM inside the
                //   BaseLayout loop).
                // -------------------------------------------------------------
                var walls = FindAll(choices, WallId);
                if (walls.Count != 1)
                    failures.Add("[walls-do-not-explode] 8 placed '" + WallId + "' segments produced " + walls.Count +
                                 " rail row(s). A per-instance rail is UNBOUNDED - a real town has dozens of wall " +
                                 "segments and the rail would scroll forever. Ruling 3.1: ONE row per TYPE.");
                else if (walls[0].PlacedCount != 8)
                    failures.Add("[walls-do-not-explode] the wall row reports PlacedCount=" + walls[0].PlacedCount +
                                 " for 8 placed segments - the tally is not counting instances.");
            }
            finally
            {
                SetGssInstance(prior);
                if (host != null) UnityEngine.Object.DestroyImmediate(host);
                if (fixture != null) UnityEngine.Object.DestroyImmediate(fixture);
            }
        }

        // =====================================================================
        //  SOURCE half - the panel/VM contracts that no fixture can observe.
        // =====================================================================
        private static void CheckPanelSource(List<string> failures, StringBuilder log)
        {
            string panel = ReadSource(PanelPath, failures);
            string vm = ReadSource(VmPath, failures);
            if (panel == null || vm == null) return;

            // -------------------------------------------------------------
            // CASE 6  [defense-band-is-builder]
            // REVERT RECIPE (RED): replace the AddBuildingNowBand() call in
            //   RenderDefenseDestination with a bespoke AddDefenseNowBand()
            //   headed "DEFENDING NOW".
            // -------------------------------------------------------------
            string destination = MethodBody(panel, "private void RenderDefenseDestination(");
            if (destination == null)
            {
                failures.Add("[defense-band-is-builder] RenderDefenseDestination was not found in " + PanelPath +
                             " - the Defense workspace does not exist, so nothing below could be checked. " +
                             "FAIL, not a skip.");
            }
            else
            {
                if (!destination.Contains("AddBuildingNowBand()"))
                    failures.Add("[defense-band-is-builder] RenderDefenseDestination does not call " +
                                 "AddBuildingNowBand(). ChannelOf(Defense) == ChannelId.Builder and " +
                                 "ManageScreenVM:539 states the canon - Defence and Buildings share the ONE Builder " +
                                 "rail. A second band for one queue is duplicated state (ruling 3.3).");
                // Scanned with LINE COMMENTS STRIPPED. The panel lane leaves TOMBSTONE comments
                // where it deletes things (ManageScreenPanel.cs:1739, :3697 name the retired pager
                // helpers in prose, deliberately). A ban that reads prose fires on the very comment
                // recording that the thing is gone - the trap this whole suite family has to dodge.
                if (StripLineComments(panel).Contains("DEFENDING NOW"))
                    failures.Add("[defense-band-is-builder] the panel carries the invented header word " +
                                 "\"DEFENDING NOW\". The Defense band IS the Buildings band and is named " +
                                 "BUILDING NOW (ruling 3.3).");
                // Presence half, so this case cannot pass vacuously on a body that
                // was reduced to nothing: the destination must still build a rail
                // and a card.
                if (!destination.Contains("AddDefenseWorkspaceRow"))
                    failures.Add("[defense-band-is-builder] RenderDefenseDestination does not build the workspace row " +
                                 "(AddDefenseWorkspaceRow) - the band check above would pass on an empty method.");
            }

            // -------------------------------------------------------------
            // CASE 7  [defense-art-tiers-reachable]
            // REVERT RECIPE (RED): in DefenseSprite, load
            //   `Resources.Load<Sprite>("Portraits/" + choice.Id)` and delete the
            //   choice.PortraitKey probe.  (Do NOT edit ResolveBuildingPortraitKey -
            //   it is SHARED with Buildings.)
            // -------------------------------------------------------------
            string art = MethodBody(panel, "private static Sprite DefenseSprite(");
            if (art == null)
            {
                failures.Add("[defense-art-tiers-reachable] DefenseSprite was not found in " + PanelPath +
                             ". Assets/Resources/Portraits holds archer-tower-2/-3 and 14 more tier portraits that " +
                             "NO code path can reach without it (ruling 3.8). FAIL, not a skip.");
            }
            else
            {
                if (!art.Contains("choice.PortraitKey"))
                    failures.Add("[defense-art-tiers-reachable] DefenseSprite ignores choice.PortraitKey, which is the " +
                                 "ONLY level-suffixed key in the model. archer-tower-2.png is on disk and unreachable " +
                                 "again (ruling 3.8).");
                if (!art.Contains("ResolveEntryArtPublic"))
                    failures.Add("[defense-art-tiers-reachable] DefenseSprite skips " +
                                 "BuildPaletteUI.ResolveEntryArtPublic, which owns the alias table " +
                                 "(tower_siege_tower->Sky_Ballista, wall_wood->Wooden_Wall, lumberyard->storage_wood, " +
                                 "...). Those types would paint the hammer.");
                if (!art.Contains("ConceptIconResolver"))
                    failures.Add("[defense-art-tiers-reachable] DefenseSprite has no ConceptIconResolver step before " +
                                 "its fallback.");
                if (!art.Contains("FlowTrace.Warn"))
                    failures.Add("[defense-art-tiers-reachable] DefenseSprite falls back to the hammer SILENTLY. " +
                                 "CLAUDE.md section 12: a catch/fallback that swallows without logging is forbidden - " +
                                 "the next art gap would leave no evidence.");
            }

            // ⭐ RE-POINTED 2026-09-06, WITH the change that moved the producer - not deleted.
            //
            // This pin used to require the "-<level>" suffix inside
            // ManageScreenVM.ResolveBuildingPortraitKey. That method IS GONE, and deliberately: it
            // composed "Portraits/<display-name-slug>[-N]" against the MIXED ROOT folder, a SECOND
            // producer of a key ManageArt.BuildingPortraitKey already owned from the catalog ID
            // against Portraits/Buildings/. MEASURED cause of the blank tan ovals on
            // ManageFlow_BUILD_gridtop_2670x1200.png (Wooden Palisade, Crystal Mine): the slug keys
            // 'Portraits/wooden-palisade' and 'Portraits/crystal-mine-2' exist nowhere.
            //
            // ⛔ WHAT THIS CASE DEFENDS IS UNCHANGED - a Defense card must be able to name its TIER
            // sheet, or every card paints its level-1 art forever. Only the SEAM moved, so the pin
            // moved with it: the suffix now has to come out of ManageArt, and the VM has to be
            // ASKING ManageArt rather than spelling a key itself.
            // ⛔ SCAN COMMENT-STRIPPED SOURCE, NOT RAW. MEASURED 2026-09-07
            // (Builds/reg-wave3g.log): the absence pin below fired on the VM's OWN do-not-
            // reintroduce notes at ManageScreenVM.cs:1701 and :2045 - the comments written to stop
            // the slug composer coming back were read AS the composer coming back. A source pin
            // that a comment can flip is not a pin, and it fails BOTH ways: prose could equally
            // satisfy a required-substring check on a call that no longer exists.
            // All three pins in this block therefore read `vmCode`. StripComments is quote-aware,
            // so a string literal mentioning the identifier still counts as code.
            string vmCode = StripComments(vm);

            if (!ManageArtEmitsTierSuffix())
                failures.Add("[defense-art-tiers-reachable] ManageArt.BuildingPortraitKey no longer appends " +
                             "\"-<level>\" for level >= 2, so PortraitKey can never name a tier sheet and every " +
                             "Defense card paints its level-1 art forever.");
            if (!vmCode.Contains("ManageArt.BuildingPortraitKey"))
                failures.Add("[defense-art-tiers-reachable] ManageScreenVM no longer calls " +
                             "ManageArt.BuildingPortraitKey - the defence PortraitKey is being spelled somewhere " +
                             "else again, which is the duplicated-state defect that blanked Wooden Palisade and " +
                             "Crystal Mine.");
            if (vmCode.Contains("ResolveBuildingPortraitKey"))
                failures.Add("[defense-art-tiers-reachable] the retired slug composer " +
                             "ManageScreenVM.ResolveBuildingPortraitKey is back. It addresses the MIXED ROOT " +
                             "folder; ManageArt.BuildingPortraitKey and Portraits/Buildings/ are the one producer.");

            // -------------------------------------------------------------
            // CASE 8  [touch-floor]
            // REVERT RECIPE (RED): lower TroopCtaY1 to 0.40f in the panel.
            // (Ruling 3.11 / 3.10: these three constants are read by name by
            //  ManageQueueDrawerRegression:205,230 - do not touch them.)
            // -------------------------------------------------------------
            float y0 = Const(panel, "TroopCtaY0"), y1 = Const(panel, "TroopCtaY1"), px = Const(panel, "TroopWorkspacePx");
            if (y0 < 0f || y1 < 0f || px < 0f)
            {
                failures.Add("[touch-floor] TroopCtaY0/TroopCtaY1/TroopWorkspacePx could not be read from " + PanelPath +
                             " (got " + y0 + "/" + y1 + "/" + px + "). The CTA band height cannot be replayed, so this " +
                             "case would otherwise pass on a panel with no constants at all. FAIL, not a skip.");
            }
            else
            {
                float bandPx = (y1 - y0) * px;
                log.AppendLine("CTA band replay: (" + y1 + " - " + y0 + ") * " + px + " = " + bandPx + "px vs floor " +
                               ElarionUiKit.MinTouchPx + "px");
                if (bandPx < ElarionUiKit.MinTouchPx)
                    failures.Add("[touch-floor] the shared CTA band replays to " + bandPx + " reference px, under " +
                                 "ElarionUiKit.MinTouchPx (" + ElarionUiKit.MinTouchPx + "). If three faces cannot fit, " +
                                 "drop the SECOND door (it is nullable, ruling 3.5) - never the touch height.");
            }

            // -------------------------------------------------------------
            // CASE 9  [build-defense-door-survives]
            // REVERT RECIPE (RED): delete the
            //   AddActionNoteRow("Need another tower?", "Build defense", OpenDefenseBuilder)
            //   footer row from RenderDefenseDestination.
            // -------------------------------------------------------------
            if (!panel.Contains("\"Build defense\", OpenDefenseBuilder"))
                failures.Add("[build-defense-door-survives] the exact call `\"Build defense\", OpenDefenseBuilder` is " +
                             "gone from " + PanelPath + ". It is the Defense destination's footer door and TWO other " +
                             "suites pin it (BuildCollectionPlayerRegression:119, ManageApprovedLauncherRegression:52); " +
                             "the paged heading retired, this did NOT (ruling 3.4).");
            if (!panel.Contains("Need another tower?"))
                failures.Add("[build-defense-door-survives] the footer's player sentence \"Need another tower?\" is " +
                             "gone - the door has no label, so the check above could pass on a bare call site.");

            log.AppendLine("source: destination/band/art/touch-floor/footer checks complete");
        }

        // =====================================================================
        //  The tier portraits ruling 3.8 says are on disk. If they are not, the
        //  DefenseSprite chain above is pinned against art that does not exist.
        // =====================================================================
        private static void CheckDefenseTierPortraitCoverage(List<string> failures, StringBuilder log)
        {
            // ⭐ RE-POINTED 2026-09-06 FROM DISPLAY-NAME SLUGS IN THE ROOT FOLDER TO CATALOG IDS
            //    UNDER Portraits/Buildings/ - the folder ManageArt.BuildingPortraitKey addresses.
            //
            // The old list enumerated "archer-tower", "ballista", "catapult", "arcane-spire",
            // "wizard-tower" x 3 levels against Assets/Resources/Portraits. Those stems are the
            // retired DISPLAY-NAME spelling; the shipped key is the catalog ID
            // (tower_ground_archer, tower_ballista, ...). Checking the old stems proved a folder
            // the game no longer reads.
            //
            // ⛔ THE PATH IS BUILT BY THE SEAM ITSELF, NOT RETYPED. RequirePortrait now takes the
            // key ManageArt.BuildingPortraitKey returns, so this case cannot drift from the
            // producer the way the old list did - if the folder or the suffix rule ever changes,
            // this coverage check follows it automatically.
            //
            // REVERT RECIPE (RED): delete any listed portrait or its .meta, or point
            // ManageArt.BuildingPortraitFolder back at "Portraits/".
            string[] tiered = { "tower_ground_archer", "tower_ballista", "tower_catapult",
                                "tower_arcane_spire", "tower_siege_tower" };
            // Non-laddered defence/economy ids, base sheet only.
            string[] flat = { "wall_wood", "wall_stone", "mine_crystal", "healing_caravan",
                              "lumberyard", "foundry", "silo" };
            int checkedCount = 0;

            // ⛔ THE RETIRED DISPLAY-NAME STEM, PER ID. This is a MISPLACEMENT DETECTOR and nothing
            // else: it is never used to build a load key, never handed to Resources, and never
            // consulted by shipped code - so it is not a second producer. It answers exactly one
            // question, "does this tier sheet exist somewhere under the OLD spelling", which is the
            // difference between art that is MISPLACED (a real defect, and the one this case is
            // shaped around) and art that was never commissioned (not a defect at all).
            // ⚠ tower_siege_tower has NO entry on purpose. structures-catalog.json names it
            // "Sky Ballista (Anti-Air)", so its slug would be sky-ballista - which has never
            // existed at any tier under any spelling. wizard-tower-*.png in the root folder is
            // UNRELATED legacy art and is NOT this tower's sheet; treating it as one would have
            // demanded a move that silently swapped in the wrong picture.
            var retiredStem = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                { "tower_ground_archer", "archer-tower" },
                { "tower_ballista",      "ballista" },
                { "tower_catapult",      "catapult" },
                { "tower_arcane_spire",  "arcane-spire" },
            };

            for (int i = 0; i < tiered.Length; i++)
            {
                string id = tiered[i];
                // The BASE sheet is unconditionally required - every defence type must be able to
                // paint itself at level 1.
                checkedCount += RequirePortrait(ManageArt.BuildingPortraitKey(id, 1), failures);

                for (int level = 2; level <= 3; level++)
                {
                    string key = ManageArt.BuildingPortraitKey(id, level);
                    if (PortraitExists(key)) { checkedCount++; continue; }

                    // Not under the id spelling. Is it anywhere ELSE? If so it is MISPLACED, which
                    // is the defect class - the card asks for the id key and paints the placeholder
                    // while the picture sits in the root folder under its retired name.
                    string stem;
                    bool legacyExists = retiredStem.TryGetValue(id, out stem) &&
                                        (PortraitExists("Portraits/" + stem + "-" + level) ||
                                         PortraitExists(ManageArt.BuildingPortraitFolder + stem + "-" + level));
                    if (legacyExists)
                    {
                        checkedCount++;
                        failures.Add("[defense-art-tiers-reachable] tier sheet for '" + id + "' level " + level +
                                     " exists under the RETIRED spelling '" + stem + "-" + level + "' but not at '" +
                                     key + "', which is the key ManageArt.BuildingPortraitKey composes. The card " +
                                     "asks for the id spelling and paints the placeholder disc. MOVE the file - do " +
                                     "not add a second key producer to reach it.");
                    }
                    else
                    {
                        // ⛔ NOT A FAILURE. No sheet exists at this tier under ANY spelling, so
                        // nothing is misplaced and nothing regressed - the art was never
                        // commissioned. Failing here would demand a picture that has never existed
                        // and would block the gate on an art ask. It is NAMED instead, and carried
                        // as an art ask in WO-1567 section 5.
                        log.AppendLine("  ART ASK (not a failure): no sheet for " + key +
                                       " under any spelling - tier " + level + " of '" + id +
                                       "' is uncommissioned art, so that card paints its base sheet.");
                    }
                }
            }
            for (int i = 0; i < flat.Length; i++)
                checkedCount += RequirePortrait(ManageArt.BuildingPortraitKey(flat[i], 1), failures);

            log.AppendLine("defense portrait coverage=" + checkedCount + "/" + (tiered.Length * 3 + flat.Length) +
                           " files verified on disk under " + ManageArt.BuildingPortraitFolder);
        }

        /// <summary>Resources.Load is EXTENSION-AGNOSTIC, and this folder is mixed
        /// (Healing_Caravan.jpg and storage_food.jpg sit beside archer-tower.png,
        /// measured 2026-09-06). So the pin is on the STEM: exactly one of the
        /// importable extensions must exist, with its .meta.</summary>
        /// <param name="resourceKey">A Resources-relative key as
        /// <see cref="ManageArt.BuildingPortraitKey"/> returns it, e.g.
        /// "Portraits/Buildings/tower_catapult-3". ⛔ Take the key from the SEAM - retyping a
        /// folder here is how the old slug list ended up proving a folder the game stopped
        /// reading.</param>
        private static int RequirePortrait(string resourceKey, List<string> failures)
        {
            if (string.IsNullOrEmpty(resourceKey))
            {
                failures.Add("[defense-art-tiers-reachable] ManageArt.BuildingPortraitKey returned an EMPTY key - " +
                             "the one producer cannot name this portrait at all.");
                return 1;
            }
            string[] extensions = { ".png", ".jpg", ".jpeg" };
            for (int i = 0; i < extensions.Length; i++)
            {
                string relative = "Assets/Resources/" + resourceKey + extensions[i];
                string full = Path.Combine(Directory.GetCurrentDirectory(),
                    relative.Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(full)) continue;
                if (!File.Exists(full + ".meta"))
                    failures.Add("[defense-art-tiers-reachable] missing " + relative +
                                 ".meta - Unity will not import it, so Resources.Load returns null at runtime.");
                return 1;
            }
            // (falls through to the not-on-disk failure below)
            failures.Add("[defense-art-tiers-reachable] no Assets/Resources/" + resourceKey +
                         ".{png,jpg,jpeg} on disk. This is the key ManageArt.BuildingPortraitKey composes, so the " +
                         "Defense card asks for it and paints the placeholder disc. If the sheet exists under the " +
                         "retired display-name spelling in Assets/Resources/Portraits/, it needs MOVING to the id " +
                         "spelling - not a second key producer.");
            return 1;
        }

        /// <summary>
        /// True when a sheet exists on disk for this Resources key under any importable extension.
        /// <para>Resources.Load is extension-agnostic and this project's portrait folders are mixed
        /// (Healing_Caravan.jpg sits beside archer-tower.png, measured 2026-09-06), so the probe is
        /// on the STEM. Existence only - the .meta requirement stays in
        /// <see cref="RequirePortrait"/>, which is the one that reports.</para>
        /// </summary>
        private static bool PortraitExists(string resourceKey)
        {
            if (string.IsNullOrEmpty(resourceKey)) return false;
            string[] extensions = { ".png", ".jpg", ".jpeg" };
            for (int i = 0; i < extensions.Length; i++)
            {
                string full = Path.Combine(Directory.GetCurrentDirectory(),
                    ("Assets/Resources/" + resourceKey + extensions[i]).Replace('/', Path.DirectorySeparatorChar));
                if (File.Exists(full)) return true;
            }
            return false;
        }

        /// <summary>
        /// The tier suffix rule, asked of the ONE producer rather than pattern-matched out of its
        /// source. <see cref="ManageArt.BuildingPortraitKey"/> must leave level 1 unsuffixed and
        /// append "-N" from level 2 up, or no Defense card can ever name its tier sheet.
        /// </summary>
        private static bool ManageArtEmitsTierSuffix()
        {
            string baseKey = ManageArt.BuildingPortraitKey("probe_id", 1);
            string tierKey = ManageArt.BuildingPortraitKey("probe_id", 3);
            return !string.IsNullOrEmpty(baseKey) && !string.IsNullOrEmpty(tierKey) &&
                   baseKey.EndsWith("probe_id", StringComparison.Ordinal) &&
                   tierKey.EndsWith("probe_id-3", StringComparison.Ordinal);
        }

        // =====================================================================
        //  Helpers (mirroring ManageBuildingsCardRegression's).
        // =====================================================================
        private static List<DefenseChoiceVM> FindAll(IReadOnlyList<DefenseChoiceVM> choices, string id)
        {
            var found = new List<DefenseChoiceVM>();
            if (choices == null) return found;
            for (int i = 0; i < choices.Count; i++)
                if (choices[i] != null && string.Equals(choices[i].Id, id, StringComparison.OrdinalIgnoreCase))
                    found.Add(choices[i]);
            return found;
        }

        private static bool InstallState(GameStateService service, GameState state)
        {
            var stateField = typeof(GameStateService).GetField("_state", BindingFlags.NonPublic | BindingFlags.Instance);
            if (stateField == null) return false;
            stateField.SetValue(service, state);
            return SetGssInstance(service);
        }

        private static bool SetGssInstance(GameStateService service)
        {
            var instance = typeof(GameStateService).GetField("_instance", BindingFlags.NonPublic | BindingFlags.Static);
            if (instance == null) return false;
            instance.SetValue(null, service);
            return true;
        }

        private static string ReadSource(string relativePath, List<string> failures)
        {
            string full = Path.Combine(Directory.GetCurrentDirectory(), relativePath.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(full)) return File.ReadAllText(full);
            failures.Add("source file missing: " + relativePath);
            return null;
        }

        /// <summary>The text of one method, from its signature to the next member
        /// declaration at type indentation. Terminating on "\n        private "
        /// rather than a NAMED next method (the Body(from, until) shape used by
        /// ManageBuildingsCardRegression) so that inserting a new sibling method
        /// between two anchors cannot silently widen the window - C# forbids
        /// `private` on a local, so a deeper-indented false match is impossible.</summary>
        private static string MethodBody(string source, string signature)
        {
            if (source == null) return null;
            int start = source.IndexOf(signature, StringComparison.Ordinal);
            if (start < 0) return null;
            int end = source.IndexOf("\n        private ", start + signature.Length, StringComparison.Ordinal);
            if (end < 0) end = source.IndexOf("\n        public ", start + signature.Length, StringComparison.Ordinal);
            return end < 0 ? source.Substring(start) : source.Substring(start, end - start);
        }

        /// <summary>
        /// Comments blanked, string and char literals KEPT - mirrored from
        /// <c>DefenseReportLayoutRegression.StripComments</c>, which was added the same night for
        /// exactly this failure.
        ///
        /// <para>⛔ WHY NOT <see cref="StripLineComments"/>: that one cuts at the first "//" on a
        /// line, so it also truncates code whose STRING contains "//" and it never handles a
        /// block comment. A pin that a comment can flip is not a pin - and it fails both ways: an
        /// absence check reds on the tombstone that records the retirement (measured
        /// 2026-09-07, Builds/reg-wave3g.log, on ManageScreenVM.cs:1701 and :2045), and a
        /// required-substring check can be satisfied by prose describing a call that is gone.
        /// Blanking rather than deleting keeps every offset and line number intact, so a failure
        /// message can still name a real position.</para>
        /// </summary>
        private static string StripComments(string src)
        {
            if (string.IsNullOrEmpty(src)) return src ?? string.Empty;
            var buf = src.ToCharArray();
            int i = 0, n = src.Length;
            while (i < n)
            {
                char c = src[i];
                if (c == '/' && i + 1 < n && src[i + 1] == '/')
                {
                    while (i < n && src[i] != '\n') { buf[i] = ' '; i++; }
                }
                else if (c == '/' && i + 1 < n && src[i + 1] == '*')
                {
                    while (i < n && !(src[i] == '*' && i + 1 < n && src[i + 1] == '/'))
                    {
                        if (src[i] != '\n') buf[i] = ' ';
                        i++;
                    }
                    if (i < n) { buf[i] = ' '; i++; }
                    if (i < n) { buf[i] = ' '; i++; }
                }
                else if (c == '"' || c == '\'')
                {
                    char quote = c;
                    i++;                       // keep the opening quote
                    while (i < n && src[i] != quote)
                    {
                        if (src[i] == '\\' && i + 1 < n) { i += 2; continue; }
                        if (src[i] == '\n') break;
                        i++;
                    }
                    if (i < n && src[i] == quote) i++;
                }
                else i++;
            }
            return new string(buf);
        }

        /// <summary>Everything from "//" to end of line removed, line by line. Used only so a
        /// banned LITERAL cannot match the tombstone comment that records the literal's
        /// retirement.</summary>
        private static string StripLineComments(string source)
        {
            if (string.IsNullOrEmpty(source)) return string.Empty;
            var sb = new StringBuilder(source.Length);
            foreach (string line in source.Split('\n'))
            {
                int slash = line.IndexOf("//", StringComparison.Ordinal);
                sb.Append(slash >= 0 ? line.Substring(0, slash) : line).Append('\n');
            }
            return sb.ToString();
        }

        private static float Const(string source, string name)
        {
            var match = Regex.Match(source, @"\b" + name + @"\s*=\s*([0-9]+(?:\.[0-9]+)?)f");
            return match.Success && float.TryParse(match.Groups[1].Value,
                System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture,
                out float value) ? value : -1f;
        }
    }
}

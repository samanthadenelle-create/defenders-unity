// =============================================================================
// ManageRowBenefitRegression - WO-1405: every Manage row PRICES the tap and must
// also SAY WHAT IT BUYS, and no player-facing string may carry a developer grid
// coordinate.
// Marker: MANAGE_ROW_BENEFIT_OK / MANAGE_ROW_BENEFIT_FAIL <case>.
// -----------------------------------------------------------------------------
// Assembly: DeNelle.EditorRegression (editor-only). Registered in DataRegression.RunAll.
// Shape mirrors ManageDefenseCardRegression / ManageBuildingsCardRegression: a
// LIVE half that stands up a GameState fixture and drives ManageScreenVM, and a
// SOURCE half for the one contract no fixture can observe.
//
// THE MEASURED DEFECT (WO-1405 evidence, device build 355952):
//   docs/qa/UI_REVIEW_2026-09-05/04-manage-defense.png reads
//     "Arcane Spire - grid 5, 16 - L1 -> L2 / Iron 540"
//   Cost and wait on every row, benefit on none, and a cell index on a player
//   screen. The upgrade PAGE already renders the benefit sentence
//   (14-research-door-result.png: "Mage spell power +5%, arcane tower damage
//   +5%"), so the string existed and the list did not surface it.
//
// RED PROOF (this suite fails on the pre-WO-1405 tree):
//   [no-developer-coordinate] - restore
//       string location = "grid " + placed.cellX + ", " + placed.cellZ;
//   in ManageScreenVM.BuildDefenseBrowse and every Defense BrowseRow label
//   carries "grid " again.
//   [location-is-words] - the same revert deletes the only caller of
//   CompassSideOf; deleting the helper itself fires this case directly.
//
// ⚠ SCOPE. This suite asserts the MODEL, never the pixels: the benefit string is
// composed in ManageScreenVM and the View is a skin over it (MVVM strict, the
// standing Manage rule). A View that stops painting a non-empty Benefit is
// ManageDefenseCardRegression / ManageBuildingsCardRegression's ground.
//
// A missing fixture is a FAIL that names itself - never a skip, never a hollow
// pass (the UpgradeQueueFullSurfaceRegression ruling).
// =============================================================================
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using DeNelle.Core.Catalog;
using DeNelle.Core.Jobs;
using DeNelle.Core.State;
using DeNelle.Village;
using DeNelle.Village.UI;
using UnityEngine;

namespace DeNelle.Editor.Regression
{
    /// <summary>Headless contract for WO-1405's per-row benefit line and worded location.</summary>
    public static class ManageRowBenefitRegression
    {
        private const string VmPath = "Assets/_Modules/Village/UI/Manage/ManageScreenVM.cs";

        // Defense fixture ids, borrowed verbatim from ManageDefenseCardRegression so
        // the two suites cannot disagree about what a town looks like.
        private const string ArcherId = "tower_ground_archer";
        private const string WallId = "wall_wood";

        public static bool Run(out string reason)
        {
            var failures = new List<string>();
            var log = new StringBuilder("=== ManageRowBenefitRegression (WO-1405) ===\n");
            try
            {
                CheckLiveModel(failures, log);
                CheckCompassIsWords(failures, log);
                CheckVmSource(failures, log);
            }
            catch (Exception ex)
            {
                failures.Add("suite threw " + ex.GetType().Name + ": " + ex.Message);
            }

            if (failures.Count == 0)
            {
                reason = "MANAGE_ROW_BENEFIT_OK every Defense/Buildings/Research row names what the upgrade buys, " +
                         "the Troops upgrade line names its effect, and no row string carries a grid coordinate";
                Debug.Log(reason + "\n" + log);
                return true;
            }

            reason = "MANAGE_ROW_BENEFIT_FAIL " + string.Join(" | ", failures);
            Debug.LogError(reason + "\n" + log);
            return false;
        }

        // =====================================================================
        //  LIVE half - drive ManageScreenVM against a real GameState fixture.
        // =====================================================================
        private static void CheckLiveModel(List<string> failures, StringBuilder log)
        {
            GameStateService prior = GameStateService.Instance;
            // The WO-1518 case navigates, and OpenSchool persists the last-used tab. A regression
            // run never moves a developer's editor state.
            bool hadTabPref = PlayerPrefs.HasKey(ManageScreenVM.LastTabPrefKey);
            int priorTabPref = PlayerPrefs.GetInt(ManageScreenVM.LastTabPrefKey, 0);
            GameObject host = null;
            GameState fixture = null;
            try
            {
                fixture = ScriptableObject.CreateInstance<GameState>();
                fixture.Onboarded = true;
                fixture.VillageTier = 4;

                // One town that can exercise all four tabs at once: ladder BUILDINGS with
                // authored perks (Research), a BARRACKS (Troops), and placed DEFENCE.
                fixture.BaseLayout = new List<PlacedStructureData>
                {
                    new PlacedStructureData("forge", 2, 2, 0, 1),
                    new PlacedStructureData("lumbermill", 4, 2, 0, 1),
                    new PlacedStructureData("barracks", 6, 2, 0, 1),
                    new PlacedStructureData(ArcherId, 20, 30, 0, 1),   // north-east of the Heart
                    new PlacedStructureData(WallId, 4, 4, 0, 1),       // south-west of the Heart
                };
                fixture.BuildingTiers["forge"] = 1;
                fixture.BuildingTiers["lumbermill"] = 1;
                fixture.BuildingTiers["barracks"] = 2;
                fixture.Wood = 100000;
                fixture.Iron = 100000;
                var balances = fixture.Resources;
                balances.Stone = 100000;
                balances.Coins = 100000;
                balances.Crystals = 100000;
                fixture.Resources = balances;
                fixture.ObsidianQueue = ObsidianQueueState.Empty();

                host = new GameObject("GSS (manage-row-benefit oracle)");
                var service = host.AddComponent<GameStateService>();
                if (!InstallState(service, fixture))
                {
                    // NOT A SKIP: a suite that green-passes on an unreachable seam asserts
                    // nothing, most eagerly on the day the seam breaks.
                    failures.Add("[fixture] GameStateService state seam is not reflectable, so no LIVE benefit case " +
                                 "below could run. This is a FAIL, not a skip.");
                    return;
                }
                if (CatalogRegistry.Get(ArcherId) == null || CatalogRegistry.Get(WallId) == null)
                {
                    failures.Add("[fixture] the catalog cannot resolve '" + ArcherId + "' / '" + WallId +
                                 "', so the Defense cases could not have been exercised. FAIL, not a skip.");
                    return;
                }

                // ── DEFENSE ────────────────────────────────────────────────
                var vm = new ManageScreenVM();
                vm.SelectTab(ManageTab.Defense);
                vm.Rebuild();

                if (vm.DefenseChoices == null || vm.DefenseChoices.Count == 0)
                {
                    failures.Add("[defense-row-names-a-benefit] the fixture produced NO Defense choices, so the " +
                                 "benefit assertion below would have passed on an empty list. FAIL, not a skip.");
                }
                else
                {
                    log.AppendLine("Defense choices = " + vm.DefenseChoices.Count);
                    for (int i = 0; i < vm.DefenseChoices.Count; i++)
                    {
                        var c = vm.DefenseChoices[i];
                        if (c == null) { failures.Add("[defense-row-names-a-benefit] a null Defense choice."); continue; }
                        log.AppendLine("  [" + c.StateWord + "] " + c.Id + " benefit='" + c.AfterUpgradeText +
                                       "' placed='" + c.PlacedText + "'");
                        // A MAX row has nothing left to buy - the only state allowed an empty benefit.
                        if (string.Equals(c.StateWord, "Max", StringComparison.Ordinal)) continue;
                        if (string.IsNullOrWhiteSpace(c.AfterUpgradeText))
                            failures.Add("[defense-row-names-a-benefit] '" + c.Id + "' is upgradable and carries no " +
                                         "AfterUpgradeText, so the card prices the tap and never says what it buys " +
                                         "(WO-1405). See ManageScreenVM.BuildDefenseChoices.");
                    }
                }

                // The retired paged list is still BUILT by the VM (three suites drive it and the
                // Troops 'Saved army compositions' row reads it), so its labels are still strings a
                // future surface could paint - and they are where the coordinate used to live.
                ScanForCoordinate(vm, "Defense", failures, log);

                // ── BUILDINGS ──────────────────────────────────────────────
                vm.SelectTab(ManageTab.Buildings);
                vm.Rebuild();
                if (vm.BuildingChoices == null || vm.BuildingChoices.Count == 0)
                {
                    failures.Add("[building-row-names-a-benefit] the fixture produced NO Buildings choices. " +
                                 "FAIL, not a skip.");
                }
                else
                {
                    log.AppendLine("Building choices = " + vm.BuildingChoices.Count);
                    for (int i = 0; i < vm.BuildingChoices.Count; i++)
                    {
                        var c = vm.BuildingChoices[i];
                        if (c == null) { failures.Add("[building-row-names-a-benefit] a null Buildings choice."); continue; }
                        log.AppendLine("  [" + c.StateWord + "] " + c.Id + " benefit='" + c.AfterUpgradeText + "'");
                        if (string.Equals(c.StateWord, "Max", StringComparison.Ordinal)) continue;
                        if (string.IsNullOrWhiteSpace(c.AfterUpgradeText))
                            failures.Add("[building-row-names-a-benefit] '" + c.Id + "' has a next tier and no " +
                                         "AfterUpgradeText - building-tiers.json authors an Effect for it and the " +
                                         "card is not reading it (WO-1405).");
                    }
                }
                ScanForCoordinate(vm, "Buildings", failures, log);

                // ── RESEARCH ───────────────────────────────────────────────
                vm.SelectTab(ManageTab.Research);
                vm.Rebuild();
                if (vm.ResearchChoices == null || vm.ResearchChoices.Count == 0)
                {
                    failures.Add("[research-row-names-a-benefit] the fixture produced NO Research choices, so the " +
                                 "perk-effect assertion could not run. FAIL, not a skip.");
                }
                else
                {
                    log.AppendLine("Research choices = " + vm.ResearchChoices.Count);
                    for (int i = 0; i < vm.ResearchChoices.Count; i++)
                    {
                        var c = vm.ResearchChoices[i];
                        if (c == null) { failures.Add("[research-row-names-a-benefit] a null Research choice."); continue; }
                        log.AppendLine("  [" + c.StateWord + "] " + c.BuildingId + ":" + c.PerkId +
                                       " benefit='" + c.Description + "'");
                        // EVERY research state keeps its sentence, including Researched and Locked:
                        // a perk the player cannot reach yet is exactly the one whose reason to want
                        // it must be on screen.
                        if (string.IsNullOrWhiteSpace(c.Description))
                            failures.Add("[research-row-names-a-benefit] perk '" + c.BuildingId + ":" + c.PerkId +
                                         "' carries no Description, so its card is a price with no reason attached " +
                                         "(WO-1405).");
                    }
                }
                ScanForCoordinate(vm, "Research", failures, log);

                // ── TROOPS ─────────────────────────────────────────────────
                vm.SelectTab(ManageTab.Troops);
                vm.Rebuild();
                if (vm.TroopChoices == null || vm.TroopChoices.Count == 0)
                {
                    failures.Add("[troop-upgrade-names-an-effect] the fixture placed a barracks and the Troops tab " +
                                 "produced NO choices. FAIL, not a skip.");
                }
                else
                {
                    int upgradable = 0, named = 0;
                    log.AppendLine("Troop choices = " + vm.TroopChoices.Count);
                    for (int i = 0; i < vm.TroopChoices.Count; i++)
                    {
                        var c = vm.TroopChoices[i];
                        if (c == null) { failures.Add("[troop-upgrade-names-an-effect] a null Troops choice."); continue; }
                        log.AppendLine("  [" + c.StateWord + "] " + c.Id + " hasNext=" + c.HasNextLevel +
                                       " fact='" + c.UpgradeFactText + "' unlock='" + c.NextUnlockText + "'");
                        if (!c.Unlocked || !c.HasNextLevel) continue;
                        upgradable++;
                        // The UPGRADE line must at minimum PRICE itself in words; the ticket's
                        // "names an effect" is NextUnlockText, which troop-upgrades.json authors
                        // only where an ability sits above this level (ManageScreenVM traces the
                        // gap). So the per-row assertion is the fact sentence, and the roster-wide
                        // assertion below is the effect.
                        if (string.IsNullOrWhiteSpace(c.UpgradeFactText))
                            failures.Add("[troop-upgrade-names-an-effect] '" + c.Id + "' has a next level and an " +
                                         "empty UpgradeFactText - the UPGRADE face is a bare button.");
                        if (!string.IsNullOrWhiteSpace(c.NextUnlockText)) named++;
                    }
                    log.AppendLine("  upgradable troops=" + upgradable + ", of which named an effect=" + named);
                    if (upgradable > 0 && named == 0)
                        failures.Add("[troop-upgrade-names-an-effect] not one of the " + upgradable + " upgradable " +
                                     "troops carries a NextUnlockText, so the whole roster's UPGRADE face names no " +
                                     "effect. Either BarracksProgression.NextAbilityLine stopped being read or " +
                                     "troop-upgrades.json lost its ability lines (WO-1389/WO-1405).");
                }
                ScanForCoordinate(vm, "Troops", failures, log);

                // ── WO-1518: SHORT names WHAT, LOCKED names WHY and offers the tap ──
                // Runs LAST because it EMPTIES the purse, which no earlier case may see.
                CheckResearchRowsSayWhatAndWhy(vm, fixture, failures, log);
            }
            finally
            {
                SetGssInstance(prior);
                if (host != null) UnityEngine.Object.DestroyImmediate(host);
                if (fixture != null) UnityEngine.Object.DestroyImmediate(fixture);
                if (hadTabPref) PlayerPrefs.SetInt(ManageScreenVM.LastTabPrefKey, priorTabPref);
                else PlayerPrefs.DeleteKey(ManageScreenVM.LastTabPrefKey);
            }
        }

        // =====================================================================
        //  WO-1518 - [research-short-names-what] / [research-locked-names-why]
        // ---------------------------------------------------------------------
        //  Owner rulings 2026-09-06 20:12, verbatim:
        //      "see screen, short doesnt help, i need to know waht im short"
        //      "if locked what is blocking and link to it"
        //  and at 20:19, on the door that already worked:
        //      "the logic is there on some if i click takes me there but should tell them that"
        //  Evidence: Logs/device/screens/owner-screen-20260906-201242.png, the Armorer school -
        //  "Reinforced Plating / Troop health +5% / [green arrow] SHORT" and two rows reading a
        //  bare "LOCKED" with a padlock, naming no blocker and announcing no navigation.
        //
        //  ⚠ THE PURSE IS EMPTIED HERE, deliberately and last. The fixture above is deliberately
        //  rich ("every gate wide open"), and a rich purse can never produce a SHORT row - so a
        //  case written against it would assert nothing about the very word the ruling is about.
        //
        //  RED PROOF: put `item.BadgeText = c.Ready ? "READY" : "SHORT";` back in
        //  ManageScreenVM.ComposeResearchItem -> [research-short-names-what] fires. Put
        //  `item.BadgeText = "LOCKED";` back (dropping the tap word) -> the door half of
        //  [research-locked-names-why] fires. Delete the RequirementText assignment from
        //  ManageVmProjection.ProjectTile -> the blocker half fires. Restore
        //  `item.NextRungLine + " . " + item.LockReason` -> the JOIN half fires (added 2026-09-07;
        //  see the case body for why the blocker moved off the benefit line).
        // =====================================================================
        private static void CheckResearchRowsSayWhatAndWhy(ManageScreenVM vm, GameState fixture,
            List<string> failures, StringBuilder log)
        {
            // ⚠ AvailableTabIds is refreshed ONLY by the navigation writers - EnterTab /
            // OpenDefaultScreen / ComposeWorkspace (ManageScreenVM.RefreshAvailableTabs). Every case
            // above drives the LEGACY pair SelectTab + Rebuild, which fills VisibleTabs and never
            // touches _availableTabs - so without this call the list is still EMPTY here and
            // RESEARCH reads "not available" on a fixture that places two perk-authoring ladder
            // buildings. EnterTab is also the screen this case wants: the RESEARCH grid, which is
            // where the player's own route into OpenSchool below starts.
            vm.EnterTab(DeNelle.Core.Manage.ManageTabId.Research);

            if (!vm.AvailableTabIds.Contains(DeNelle.Core.Manage.ManageTabId.Research))
            {
                // FAIL, not a skip: the fixture places two ladder buildings that author perks.
                failures.Add("[research-locked-names-why] the RESEARCH tab is not available on a fixture that " +
                             "places a forge and a lumbermill, so no perk row could be composed and WO-1518's " +
                             "rulings are unmeasured.");
                return;
            }

            // Empty the purse so an AVAILABLE perk is genuinely unaffordable.
            fixture.Wood = 0;
            fixture.Iron = 0;
            var broke = fixture.Resources;
            broke.Stone = 0;
            broke.Coins = 0;
            broke.Crystals = 0;
            fixture.Resources = broke;

            var schools = new List<string>();
            for (int i = 0; i < vm.ResearchChoices.Count; i++)
            {
                var c = vm.ResearchChoices[i];
                if (c != null && !string.IsNullOrEmpty(c.BuildingId) && !schools.Contains(c.BuildingId))
                    schools.Add(c.BuildingId);
            }
            if (schools.Count == 0)
            {
                failures.Add("[research-locked-names-why] the fixture produced no research school, so nothing " +
                             "below asserts anything. FAIL, not a skip.");
                return;
            }

            int shortRows = 0, lockedRows = 0, measured = 0;
            for (int s = 0; s < schools.Count; s++)
            {
                vm.OpenSchool(schools[s], null);
                var ws = vm.ComposeWorkspace();
                if (ws == null || ws.Tabs == null || ws.Tabs.Count == 0) continue;
                int index = Mathf.Clamp(ws.ActiveTabIndex, 0, ws.Tabs.Count - 1);
                var tiles = ws.Tabs[index].Tiles;
                if (tiles == null) continue;

                for (int t = 0; t < tiles.Count; t++)
                {
                    var tile = tiles[t];
                    if (tile == null) { failures.Add("[research-locked-names-why] a null perk tile."); continue; }
                    measured++;
                    string word = tile.StateText ?? string.Empty;
                    log.AppendLine("  perk " + schools[s] + ":" + tile.Id + " state='" + word +
                                   "' sub='" + tile.Subtitle + "' medallion='" + tile.StateIconKey + "'");

                    if (tile.VisualState == DeNelle.Core.Manage.ManageTileVisualState.Locked)
                    {
                        lockedRows++;
                        // The face must SAY WHAT THE TAP WILL DO. Activate is already wired on
                        // every locked perk (ManageScreenVM.ComposeResearchItem routes it to the
                        // school's own BUILD card), and a row that navigates without announcing it
                        // is the whole of the 20:19 note.
                        if (word.IndexOf("TAP", StringComparison.OrdinalIgnoreCase) < 0)
                            failures.Add("[research-locked-names-why] locked perk '" + tile.Id + "' reads \"" + word +
                                         "\" - it carries a door (Activate is wired) and no tap affordance, so it " +
                                         "teleports the player somewhere with no warning (owner, 20:19).");
                        // ...and the row must NAME THE BLOCKER, which is the 20:12 ruling.
                        //
                        // ⛔ RE-POINTED 2026-09-07 (WO-1567 panel row 8), WITH THE RETIRED READING
                        // KEPT SO IT IS NOT MOVED BACK. It read:
                        //     tile.Subtitle.IndexOf(blocker, ...) < 0   ->  "its row paints ..."
                        // i.e. it required the blocker to be INSIDE THE BENEFIT LINE, and the
                        // comment beside it explained why: "the sentence rides the row's SECOND
                        // LINE because the state column is a quarter of the row wide and would cull
                        // it". That reasoning was right about the STATE COLUMN and wrong about the
                        // only alternative - so ComposeResearchItem joined the two facts with
                        // `NextRungLine + " . " + LockReason`, and the owner's device
                        // (Logs/device/screens/owner-screen-20260907-010151.png) shows the result:
                        //     "Wood +8%, offline bucket +8% . Upgrade the building to Tier 3 f..."
                        // A benefit and a requirement in one string, a floating period between
                        // them, and the half she needs in order to act ellipsised off the end. The
                        // pin was holding the join in place.
                        //
                        // ⭐ THE RULING IS UNCHANGED AND STILL ENFORCED: the blocker MUST reach a
                        // face. It now has a face of its own - ManageTileVM.RequirementText, drawn
                        // by ManageWorkspacePanel.BuildListRow as a padlock row under the benefit
                        // (mockup panel 7's shape). This asserts the SAME invariant against the
                        // channel that now carries it, and additionally that the join has not come
                        // back - a locked row whose benefit line swallowed the blocker again would
                        // pass the first half and fail the second.
                        var choice = FindPerk(vm, schools[s], tile.Id);
                        string blocker = choice != null ? choice.LockReason : null;
                        if (!string.IsNullOrWhiteSpace(blocker))
                        {
                            if (string.IsNullOrEmpty(tile.RequirementText) ||
                                tile.RequirementText.IndexOf(blocker, StringComparison.Ordinal) < 0)
                                failures.Add("[research-locked-names-why] locked perk '" + tile.Id + "' has the blocker \"" +
                                             blocker + "\" composed on ResearchChoiceVM.LockReason and its row carries " +
                                             "RequirementText \"" + tile.RequirementText + "\" - the reason reaches no " +
                                             "face, which is exactly the composed-but-unpainted defect " +
                                             "(WO-1518 section 1).");
                            if (!string.IsNullOrEmpty(tile.Subtitle) &&
                                tile.Subtitle.IndexOf(blocker, StringComparison.Ordinal) >= 0)
                                failures.Add("[research-locked-names-why] locked perk '" + tile.Id + "' has the blocker " +
                                             "back inside its BENEFIT line (\"" + tile.Subtitle + "\"). That join is the " +
                                             "captured defect: the benefit is the line the player decides on, and the " +
                                             "requirement pushed off its end is the fact she needed. Two facts, two rows.");
                        }
                        continue;
                    }

                    if (word.StartsWith("SHORT", StringComparison.Ordinal))
                    {
                        shortRows++;
                        if (!HasDigit(word))
                            failures.Add("[research-short-names-what] perk '" + tile.Id + "' reads \"" + word +
                                         "\" - a bare SHORT names neither the resource nor the amount. Owner, " +
                                         "20:12: \"short doesnt help, i need to know waht im short\".");
                        // WO-1518 section 2 last bullet: "The green arrow badge on a SHORT row
                        // states nothing; remove it."
                        if (!string.IsNullOrEmpty(tile.StateIconKey))
                            failures.Add("[research-short-names-what] perk '" + tile.Id + "' reads \"" + word +
                                         "\" and still carries the status medallion '" + tile.StateIconKey +
                                         "' - the green up-arrow on a row the player cannot afford states nothing.");
                    }
                }
            }

            log.AppendLine("  WO-1518: measured " + measured + " perk tile(s), " + shortRows + " SHORT, " +
                           lockedRows + " LOCKED (purse emptied)");
            if (measured == 0)
                failures.Add("[research-short-names-what] no perk tile was composed at all, so both WO-1518 cases " +
                             "passed on an empty grid. FAIL, not a skip.");
            else if (shortRows == 0 && lockedRows == 0)
                failures.Add("[research-short-names-what] with an EMPTY purse the fixture produced neither a SHORT " +
                             "row nor a LOCKED one across " + measured + " perk tile(s). Both of the owner's 20:12 " +
                             "rulings are therefore unmeasured - FAIL, not a skip.");
        }

        private static ResearchChoiceVM FindPerk(ManageScreenVM vm, string buildingId, string perkId)
        {
            for (int i = 0; i < vm.ResearchChoices.Count; i++)
            {
                var c = vm.ResearchChoices[i];
                if (c == null) continue;
                if (!string.Equals(c.PerkId, perkId, StringComparison.Ordinal)) continue;
                if (!string.Equals(c.BuildingId, buildingId, StringComparison.OrdinalIgnoreCase)) continue;
                return c;
            }
            return null;
        }

        private static bool HasDigit(string s)
        {
            for (int i = 0; i < s.Length; i++) if (char.IsDigit(s[i])) return true;
            return false;
        }

        /// <summary>
        /// CASE [no-developer-coordinate] - no string the player could read on this tab spells a
        /// grid cell. Scans the browse-row labels (where "grid 5, 16" was authored) plus every
        /// prose field of the four card models.
        /// </summary>
        private static void ScanForCoordinate(ManageScreenVM vm, string tab, List<string> failures, StringBuilder log)
        {
            int scanned = 0;
            Action<string, string> check = (where, text) =>
            {
                if (string.IsNullOrEmpty(text)) return;
                scanned++;
                if (text.IndexOf("grid ", StringComparison.OrdinalIgnoreCase) >= 0)
                    failures.Add("[no-developer-coordinate] " + tab + " " + where + " reads \"" + text +
                                 "\". A cell index is an internal address; WO-1405 ruling section 2 #5 says a row " +
                                 "names a display name and a compass side, never a coordinate.");
            };

            if (vm.BrowseRows != null)
                for (int i = 0; i < vm.BrowseRows.Count; i++)
                    if (vm.BrowseRows[i] != null) check("browse row label", vm.BrowseRows[i].Label);

            if (vm.DefenseChoices != null)
                for (int i = 0; i < vm.DefenseChoices.Count; i++)
                {
                    var c = vm.DefenseChoices[i];
                    if (c == null) continue;
                    check("defense name", c.Name);
                    check("defense placed line", c.PlacedText);
                    check("defense description", c.Description);
                    check("defense benefit", c.AfterUpgradeText);
                }

            if (vm.BuildingChoices != null)
                for (int i = 0; i < vm.BuildingChoices.Count; i++)
                {
                    var c = vm.BuildingChoices[i];
                    if (c == null) continue;
                    check("building name", c.Name);
                    check("building description", c.Description);
                    check("building benefit", c.AfterUpgradeText);
                }

            if (vm.ResearchChoices != null)
                for (int i = 0; i < vm.ResearchChoices.Count; i++)
                {
                    var c = vm.ResearchChoices[i];
                    if (c == null) continue;
                    check("research name", c.Name);
                    check("research description", c.Description);
                }

            if (vm.TroopChoices != null)
                for (int i = 0; i < vm.TroopChoices.Count; i++)
                {
                    var c = vm.TroopChoices[i];
                    if (c == null) continue;
                    check("troop name", c.Name);
                    check("troop description", c.Description);
                    check("troop next-unlock", c.NextUnlockText);
                }

            log.AppendLine(tab + ": scanned " + scanned + " player-facing string(s) for a grid coordinate");
        }

        // =====================================================================
        //  CASE [location-is-words] - the replacement is a COMPASS SIDE, and it
        //  is derived, not decorative: opposite cells must answer opposite sides.
        //  RED: delete ManageScreenVM.CompassSideOf.
        // =====================================================================
        private static void CheckCompassIsWords(List<string> failures, StringBuilder log)
        {
            var method = typeof(ManageScreenVM).GetMethod("CompassSideOf",
                BindingFlags.NonPublic | BindingFlags.Static);
            if (method == null)
            {
                failures.Add("[location-is-words] ManageScreenVM has no CompassSideOf(int,int). The Defense row's " +
                             "location clause has nothing to say but a coordinate. FAIL, not a skip.");
                return;
            }

            // A live PlacementGrid would move the origin under these expectations, and a
            // regression run has no scene - so say so rather than asserting against a grid
            // this suite did not build.
            if (PlacementGrid.Instance != null)
            {
                log.AppendLine("[location-is-words] a live PlacementGrid is in the scene; asserting only that " +
                               "opposite cells disagree, not the exact words.");
            }

            Func<int, int, string> side = (x, z) => (string)method.Invoke(null, new object[] { x, z });

            // Cells read against PlacementGrid's shipped geometry (cellSize 3 m, origin
            // (-45, -45), the Heart at world 0,0,0): cell 15 straddles the centre, so 30 is
            // deep north, 2 deep south, 28 far east, 1 far west.
            string north = side(15, 30), south = side(15, 2), east = side(28, 15), west = side(1, 15);
            string centre = side(15, 15);
            log.AppendLine("compass: (15,30)='" + north + "' (15,2)='" + south + "' (28,15)='" + east +
                           "' (1,15)='" + west + "' (15,15)='" + centre + "'");

            string[] words = { north, south, east, west, centre };
            for (int i = 0; i < words.Length; i++)
            {
                if (string.IsNullOrWhiteSpace(words[i]))
                    failures.Add("[location-is-words] CompassSideOf returned an EMPTY location for one of the probe " +
                                 "cells; a row would silently lose its location clause.");
                else if (words[i].IndexOf("grid ", StringComparison.OrdinalIgnoreCase) >= 0)
                    failures.Add("[location-is-words] CompassSideOf still speaks a coordinate: \"" + words[i] + "\".");
                else
                {
                    // Words, never digits: a compass side that carries a number is a coordinate
                    // wearing a hat.
                    for (int d = 0; d < words[i].Length; d++)
                        if (char.IsDigit(words[i][d]))
                        {
                            failures.Add("[location-is-words] the location \"" + words[i] + "\" contains a digit. " +
                                         "WO-1405 asks for words, never a coordinate.");
                            break;
                        }
                }
            }

            if (PlacementGrid.Instance == null)
            {
                if (!string.Equals(north, "north side", StringComparison.Ordinal) ||
                    !string.Equals(south, "south side", StringComparison.Ordinal) ||
                    !string.Equals(east, "east side", StringComparison.Ordinal) ||
                    !string.Equals(west, "west side", StringComparison.Ordinal))
                    failures.Add("[location-is-words] the compass is not derived from the grid: expected " +
                                 "(15,30)->north side, (15,2)->south side, (28,15)->east side, (1,15)->west side " +
                                 "against PlacementGrid's shipped geometry (cellSize 3, origin -45/-45, +Z north, " +
                                 "+X east); got '" + north + "' / '" + south + "' / '" + east + "' / '" + west +
                                 "'. If PlacementGrid's origin or axes moved, CompassSideOf's mirrored defaults " +
                                 "must move with them.");
            }
            else if (string.Equals(north, south, StringComparison.Ordinal) ||
                     string.Equals(east, west, StringComparison.Ordinal))
            {
                failures.Add("[location-is-words] opposite cells answer the SAME side ('" + north + "'/'" + south +
                             "', '" + east + "'/'" + west + "'), so the location is not derived from the placement.");
            }
        }

        // =====================================================================
        //  SOURCE half - the one contract no fixture can observe: the retired
        //  literal must not come back through a path this fixture never walks.
        //  RED: restore `"grid " + placed.cellX` in BuildDefenseBrowse.
        // =====================================================================
        private static void CheckVmSource(List<string> failures, StringBuilder log)
        {
            string full = Path.Combine(Directory.GetCurrentDirectory(), VmPath.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(full))
            {
                failures.Add("[coordinate-literal-retired] source file missing: " + VmPath + ". FAIL, not a skip.");
                return;
            }
            // Scanned with LINE COMMENTS STRIPPED. This lane leaves a TOMBSTONE comment quoting
            // the retired literal exactly where it was deleted, deliberately - a ban that reads
            // prose fires on the very comment recording that the thing is gone.
            string source = StripLineComments(File.ReadAllText(full));
            if (source.Contains("\"grid \" + placed.cell"))
                failures.Add("[coordinate-literal-retired] ManageScreenVM still composes a row location as " +
                             "\"grid \" + placed.cell... . That literal IS the defect WO-1405 closes; the cell stays " +
                             "the row's identity through PlacedUpgradeKey.Compose and is never spoken.");
            if (!source.Contains("CompassSideOf("))
                failures.Add("[coordinate-literal-retired] nothing in ManageScreenVM calls CompassSideOf, so the " +
                             "location clause has no worded producer at all.");
            log.AppendLine("VM source scanned (" + source.Length + " chars, line comments stripped)");
        }

        // =====================================================================
        //  Helpers (mirroring ManageDefenseCardRegression's).
        // =====================================================================
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

        /// <summary>Everything from "//" to end of line removed, line by line, so a banned
        /// LITERAL cannot match the tombstone comment recording its retirement.</summary>
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
    }
}

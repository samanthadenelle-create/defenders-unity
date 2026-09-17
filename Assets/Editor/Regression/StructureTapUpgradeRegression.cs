// =============================================================================
// StructureTapUpgradeRegression - WO-1712 (tap a building in the world to enter
// its upgrade screen).
// -----------------------------------------------------------------------------
// Assembly: DeNelle.EditorRegression. Shape: public static bool Run(out string reason)
// - registered into DataRegression.RunAll by the orchestrator.
//
// HONEST SCOPE, STATED UP FRONT (the WO-1494 lesson: six suites claimed to MEASURE
// and were source-text lint). THIS SUITE IS TWO HALVES AND EACH SAYS WHICH IT IS:
//
//   * A LIVE half - [compose] and [slop] CALL the real static rules
//     (StructureTapUpgradeController.TryComposePanelId / TapSlopPixels) and assert the
//     values they return. No scene, no catalog, no play session: these are pure
//     functions, so this half is a genuine proof of the routing decision, not a lint.
//
//   * A SOURCE half - [guards], [door] and [existing-doors] open files and assert
//     text. It CANNOT prove a tap opens a page on a device; that is a capture
//     (WO-1712 acceptance 4's other half, which the lead runs). What a lint CAN do is
//     fail the day a later edit deletes a guard, which is the outcome that turns this
//     feature back into the WO-1708 defect it was written around.
//
// WHAT THIS PINS, AND WHY EACH ONE IS HERE
//   1. [compose]        the routing rule itself: a City/Resource upgradeId opens under the
//                       LADDER id; anything else opens under the '@' JOB KEY, and only when
//                       the catalog ceiling is above 1. A ladder id leaking into the job-key
//                       branch (or the reverse) opens the WRONG page for a whole structure
//                       family - silently, because both strings are valid panel contexts.
//   2. [slop]           the tap/drag threshold scales with screen width and never drops below
//                       the authored reference budget. A raw-pixel budget would be ~3x
//                       stricter on a Seeker than in the editor, i.e. wrong on the one
//                       platform the owner asked for this on.
//   3. [guards]         ALL FIVE pointer/ownership guards are still present in the controller.
//                       ⛔ THE UI TOOLKIT CLAUSE IS THE LOAD-BEARING ONE: the shipped town
//                       bottom bar is the UITK adaptive peaceful dock (CLAUDE.md sec.7), and
//                       UITK elements appear in NO graphic raycast. A future seat "simplifying"
//                       the guard down to the uGUI EventSystem probe - the WO-1708 pattern
//                       taken literally - would let every dock press also open an upgrade page
//                       behind it. That deletion is what this case exists to fail on.
//   4. [door]           the controller routes through PanelRouter.Open(PanelId.BuildingUpgrade,
//                       ...) and does NOT charge, queue or mutate anything: no TryStart, no
//                       TrySpend, no ChargeLedger. A second upgrade economy behind a new door
//                       is the failure this case forbids.
//   5. [existing-doors] the Build screen and the Manage screen still reach the same panel id.
//                       WO-1712 acceptance 3 is "existing paths unchanged", and a lint is the
//                       only thing that can notice if this lane's door quietly became the ONLY
//                       door later on.
// =============================================================================
using System.Collections.Generic;
using System.IO;
using DeNelle.Core.State;                      // BuildingTierCatalog - the City ladder authority
using DeNelle.Village.Buildings.Progression;   // StructureTapUpgradeController (the subject)

namespace DeNelle.Editor.Regression
{
    public static class StructureTapUpgradeRegression
    {
        private const string ControllerPath =
            "Assets/_Modules/Village/Buildings/Progression/StructureTapUpgradeController.cs";
        private const string BuildModePath =
            "Assets/_Modules/Village/BuildMode/BuildModeController.cs";
        private const string ManageVmPath =
            "Assets/_Modules/Village/UI/Manage/ManageScreenVM.cs";

        public static bool Run(out string reason)
        {
            var failures = new List<string>();
            var notes = new List<string>();

            CheckCompose(failures, notes);
            CheckSlop(failures, notes);
            CheckGuards(failures, notes);
            CheckDoor(failures, notes);
            CheckExistingDoors(failures, notes);

            if (failures.Count > 0)
            {
                reason = "STRUCTURE_TAP_UPGRADE_FAIL: " + string.Join(" | ", failures);
                return false;
            }
            reason = "STRUCTURE_TAP_UPGRADE_OK: " + string.Join(" ", notes);
            return true;
        }

        // =====================================================================
        //  LIVE half - the pure routing rule, actually executed
        // =====================================================================

        /// <summary>
        /// [compose] A City/Resource ladder id must open under ITSELF; anything else must open
        /// under its '@' job key, and only when the ceiling is above 1.
        /// <para>⚠ THE SYNTHETIC ID IS DELIBERATE. A real id like "tower_arrow" would make this
        /// case's verdict depend on whether the catalogs happen to be loaded in this batchmode
        /// process and on what building-tiers.json says today - i.e. the case could start
        /// failing because DATA moved, while the rule it guards is still correct. The synthetic
        /// id cannot be in either ladder, so the non-ladder branch is pinned by construction.
        /// The ladder branch is then proven against a REAL id discovered from
        /// BuildingTierCatalog at runtime, and is reported as UNPROVEN rather than passed when
        /// no catalog is loaded (CLAUDE.md sec.11B: an unproven thing named as unproven is
        /// useful; named as a pass it is a lie).</para>
        /// </summary>
        private static void CheckCompose(List<string> failures, List<string> notes)
        {
            const string SyntheticId = "wo1712_synthetic_not_in_any_ladder";
            const string JobKey = SyntheticId + "@3_7";

            // Non-ladder id with a real ceiling -> the job key, NOT the bare id.
            if (!StructureTapUpgradeController.TryComposePanelId(
                    SyntheticId, JobKey, 5, out string placedId, out string placedWhy))
            {
                failures.Add("[compose] a placed structure with maxLevel=5 was REFUSED a panel id (" +
                             placedWhy + ") - a tap on a real upgradeable tower would open nothing.");
            }
            else if (placedId != JobKey)
            {
                failures.Add("[compose] a placed structure resolved to '" + placedId + "' but must " +
                             "resolve to its job key '" + JobKey + "': UpgradeFamilyResolver keys " +
                             "UpgradeFamily.PlacedStructure off the '@', so a bare id opens the " +
                             "WRONG page (an empty ladder) instead of this structure's levels.");
            }

            // A ceiling of 1 has nothing to upgrade to: refuse, do not open an empty page.
            if (StructureTapUpgradeController.TryComposePanelId(
                    SyntheticId, JobKey, 1, out string flatId, out string flatWhy))
            {
                failures.Add("[compose] a structure whose catalog ceiling is 1 was GRANTED panel id '" +
                             flatId + "'. There is no level to buy, so the tap must refuse rather " +
                             "than open a page with an empty ladder.");
            }
            else if (string.IsNullOrEmpty(flatWhy))
            {
                failures.Add("[compose] the refusal path returned an EMPTY reason. A tap that opens " +
                             "nothing and says nothing is the silent failure CLAUDE.md sec.12.2 forbids.");
            }

            // The LADDER branch, against a real city id when one is loaded.
            string cityId = FirstCityLadderId();
            if (string.IsNullOrEmpty(cityId))
            {
                notes.Add("[compose] the City-ladder branch is UNPROVEN in this run: " +
                          "BuildingTierCatalog exposed no upgradable id, so no real ladder id was " +
                          "available to route. Stated, not silently passed.");
            }
            else if (!StructureTapUpgradeController.TryComposePanelId(
                         cityId, cityId + "@0_0", 5, out string ladderId, out string ladderWhy))
            {
                failures.Add("[compose] city ladder id '" + cityId + "' was REFUSED a panel id (" +
                             ladderWhy + ") - tapping that building in the world would open nothing.");
            }
            else if (ladderId != cityId)
            {
                failures.Add("[compose] city ladder id '" + cityId + "' resolved to '" + ladderId +
                             "' but must resolve to ITSELF. Routing a city building under a job key " +
                             "sends it to the PlacedStructure family, whose BaseLayout record does " +
                             "not exist for it - an empty page, not its tier ladder.");
            }
            else
            {
                notes.Add("[compose] city ladder id routes to itself (proved with '" + cityId + "').");
            }

            notes.Add("[compose] non-ladder->jobKey, ceiling<=1 refused with a stated reason.");
        }

        /// <summary>
        /// Any id BuildingTierCatalog currently calls upgradable, or null when no catalog is
        /// loaded in this process. Read live so this suite never hardcodes a building id that a
        /// later data change can retire (the duplicated-state failure CLAUDE.md sec.2/5/16 describe).
        /// </summary>
        private static string FirstCityLadderId()
        {
            return DeNelle.Core.Diagnostics.Guard.Try<string>("Regression",
                "probe BuildingTierCatalog for an upgradable id", () =>
                {
                    var all = BuildingTierCatalog.All;
                    if (all == null) return null;
                    foreach (var row in all)
                    {
                        if (row == null || string.IsNullOrEmpty(row.Id)) continue;
                        if (BuildingTierCatalog.IsUpgradable(row.Id)) return row.Id;
                    }
                    return null;
                }, null);
        }

        /// <summary>
        /// [slop] The tap/drag budget must scale UP with screen width and never fall below the
        /// authored reference value.
        /// </summary>
        private static void CheckSlop(List<string> failures, List<string> notes)
        {
            float reference = StructureTapUpgradeController.TapSlopPixels(
                StructureTapUpgradeController.ReferenceScreenWidth);
            if (!Approximately(reference, StructureTapUpgradeController.TapSlopReferencePx))
            {
                failures.Add("[slop] at the reference width the budget is " + reference +
                             "px but must equal the authored " +
                             StructureTapUpgradeController.TapSlopReferencePx + "px.");
            }

            float wide = StructureTapUpgradeController.TapSlopPixels(
                StructureTapUpgradeController.ReferenceScreenWidth * 2f);
            if (wide <= reference)
            {
                failures.Add("[slop] a screen twice the reference width returned " + wide +
                             "px, not more than the reference " + reference + "px. An unscaled " +
                             "budget is ~3x stricter on a high-density phone than in the editor - " +
                             "wrong on the one platform WO-1712 was raised from.");
            }

            float narrow = StructureTapUpgradeController.TapSlopPixels(320f);
            if (narrow < StructureTapUpgradeController.TapSlopReferencePx)
            {
                failures.Add("[slop] a narrow screen returned " + narrow + "px, below the authored " +
                             "floor - a tap would be harder to register than authored.");
            }

            float zero = StructureTapUpgradeController.TapSlopPixels(0f);
            if (zero <= 0f)
            {
                failures.Add("[slop] a zero/unknown screen width returned " + zero +
                             "px, which makes every tap a drag and disables the door entirely.");
            }

            if (StructureTapUpgradeController.TapMaxSeconds <= 0f
                || StructureTapUpgradeController.TapMaxSeconds > 2f)
            {
                failures.Add("[slop] TapMaxSeconds=" + StructureTapUpgradeController.TapMaxSeconds +
                             " is not a tap budget: at or below 0 no tap ever completes, and above " +
                             "~2s a long camera hold counts as a tap.");
            }

            notes.Add("[slop] budget scales with width, floors at the authored value, time budget sane.");
        }

        // =====================================================================
        //  SOURCE half - explicitly a lint
        // =====================================================================

        /// <summary>[guards] All five pointer/ownership guards still exist in the controller.</summary>
        private static void CheckGuards(List<string> failures, List<string> notes)
        {
            if (!TryRead(ControllerPath, out string src, out string readWhy))
            {
                failures.Add("[guards] " + readWhy);
                return;
            }

            var required = new (string Token, string Why)[]
            {
                ("_suppressTapUntilFrame",
                 "the two-frame suppression window a UI surface uses to claim a press"),
                ("PanelManager.AnyOpen",
                 "the open-panel guard - taps belong to an open panel, never the world behind it"),
                ("PanelManager.InCloseGrace",
                 "the close-grace clause - without it the press that CLOSES a page re-opens one"),
                ("BuildModeController.Instance",
                 "the build-mode stand-down - build mode owns world taps while it is active"),
                ("WallRepairController",
                 "the repair stand-down - the repair loop raycasts the PRESS half of this same tap"),
                ("BuildModeController.IsPointOverUi",
                 "the uGUI guard (WO-1712 acceptance 2)"),
                ("RuntimePanelUtils.ScreenToPanel",
                 "the UI TOOLKIT guard. NOT redundant with the uGUI probe: the shipped town " +
                 "bottom bar is the UITK adaptive peaceful dock (CLAUDE.md sec.7) and UITK elements " +
                 "are in NO graphic raycast, so without this clause every dock press also opens an " +
                 "upgrade page behind it - the WO-1708 defect, one toolkit over"),
                ("IsOwnedTown",
                 "the owned-town stand-down - OwnedTownPanel owns selection there"),
            };

            foreach (var (token, why) in required)
            {
                if (src.Contains(token)) continue;
                failures.Add("[guards] '" + token + "' is GONE from " + ControllerPath +
                             " - that removes " + why + ".");
            }

            // The tap must be measured on RELEASE with a travel budget, or a camera pan across a
            // tower opens its page on the first frame of the drag.
            if (!src.Contains("PressEndedThisFrame") || !src.Contains("_pressPoint"))
            {
                failures.Add("[guards] the press/release + travel-budget shape is gone " +
                             "(PressEndedThisFrame / _pressPoint). Firing on the PRESS makes the " +
                             "first frame of a camera pan across a structure open its upgrade page.");
            }

            if (failures.Count == 0) notes.Add("[guards] all five guards + the release/slop shape present.");
        }

        /// <summary>[door] The controller opens the shared page and spends nothing.</summary>
        private static void CheckDoor(List<string> failures, List<string> notes)
        {
            if (!TryRead(ControllerPath, out string src, out string readWhy))
            {
                failures.Add("[door] " + readWhy);
                return;
            }

            if (!src.Contains("PanelRouter.Open(PanelId.BuildingUpgrade"))
            {
                failures.Add("[door] the controller no longer calls " +
                             "PanelRouter.Open(PanelId.BuildingUpgrade, ...). That router call IS " +
                             "the shared destination the Build and Manage screens use; anything " +
                             "else is a second upgrade screen, which WO-1712 explicitly is not.");
            }

            // No second economy behind the new door: the page's own CTA owns every spend.
            var banned = new (string Token, string Why)[]
            {
                ("PlacedStructureUpgradeService.TryStart",
                 "starting the upgrade job here would make this door a second commit point; the " +
                 "page's CTA owns the charge"),
                ("EconomyService", "no wallet read or spend belongs on a navigation door"),
                ("ChargeLedger", "no ledger charge belongs on a navigation door"),
            };
            foreach (var (token, why) in banned)
            {
                if (!src.Contains(token)) continue;
                failures.Add("[door] '" + token + "' appears in " + ControllerPath +
                             " - " + why + ". WO-1712 adds a DOORWAY, not a transaction.");
            }

            if (!src.Contains("FlowTrace.Fail"))
            {
                failures.Add("[door] no FlowTrace.Fail on the router's false return. A page that " +
                             "silently does not open is exactly the uncapturable 'tap does nothing' " +
                             "report this feature would generate (CLAUDE.md sec.12).");
            }

            notes.Add("[door] routes through the shared panel router, spends nothing, fails loudly.");
        }

        /// <summary>[existing-doors] Build screen + Manage screen still reach the same page.</summary>
        private static void CheckExistingDoors(List<string> failures, List<string> notes)
        {
            if (TryRead(BuildModePath, out string build, out string buildWhy))
            {
                if (!build.Contains("PanelId.BuildingUpgrade"))
                    failures.Add("[existing-doors] BuildModeController no longer reaches " +
                                 "PanelId.BuildingUpgrade. WO-1712 acceptance 3 is that the Build " +
                                 "screen path is UNCHANGED - this lane adds a door, it does not " +
                                 "become the only one.");
            }
            else failures.Add("[existing-doors] " + buildWhy);

            if (TryRead(ManageVmPath, out string manage, out string manageWhy))
            {
                if (!manage.Contains("PanelId.BuildingUpgrade"))
                    failures.Add("[existing-doors] ManageScreenVM no longer reaches " +
                                 "PanelId.BuildingUpgrade. The Manage screen is the door canon " +
                                 "names as THE entrance (CLAUDE.md sec.7); it must survive.");
            }
            else failures.Add("[existing-doors] " + manageWhy);

            notes.Add("[existing-doors] Build + Manage still reach the same panel id.");
        }

        // =====================================================================
        //  Helpers
        // =====================================================================

        private static bool TryRead(string relativePath, out string text, out string why)
        {
            text = null;
            why = null;
            try
            {
                if (!File.Exists(relativePath))
                {
                    why = "file NOT FOUND: " + relativePath +
                          " (moved or deleted - this suite's subject is gone).";
                    return false;
                }
                text = File.ReadAllText(relativePath);
                return true;
            }
            catch (System.Exception ex)
            {
                why = "could not read " + relativePath + ": " + ex.Message;
                return false;
            }
        }

        private static bool Approximately(float a, float b) => UnityEngine.Mathf.Abs(a - b) < 0.001f;
    }
}

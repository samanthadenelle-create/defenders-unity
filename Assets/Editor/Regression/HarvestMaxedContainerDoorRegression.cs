// =============================================================================
// HarvestMaxedContainerDoorRegression [harvest-maxed-door]          WO-1863
// -----------------------------------------------------------------------------
// Assembly: DeNelle.EditorRegression (references DeNelle.Core + DeNelle.Village).
//
// THE MEASURED DEFECT (owner, 2026-09-18, verbatim):
//
//     "when you go to harvest it says upgrade lumber, mill foundry in stoneyard,
//      but if you're at max level, it shouldn't do that"
//
// The HARVEST RESULT chip read "UPGRADE LUMBERYARD" on a Lumberyard that was
// already at its authored ceiling. The reason was NOT a wrong comparison and NOT
// an off-by-one: HarvestResultVM.Build's one live signal was a container COUNT
// (Func<BankResource,int>), so a level-1 container and a level-6 container were
// the SAME NUMBER by the time the verb was chosen. The level existed one field
// away (TownBankCapacity.StorageSlot.Level, written at TownBankCapacity.cs:1020)
// and was thrown away on the way in.
//
// =============================================================================
//  (!) THIS SUITE DRIVES THE REAL SEAMS, NOT A STUB - THAT IS THE POINT.
// =============================================================================
// A pure-seam fixture cannot prove this ticket, because the bug is that the
// PRODUCING side never published the level. So this suite seeds a REAL GameState
// (BaseLayout + the WO-834 ever-built co-gate), registers the REAL catalog, and
// calls the REAL HarvestOverflowModal.StorageGrowthFor / TownBankCapacity.Apportion
// before handing the VM its signal. No canvas and no PlayMode: Apportion is pure
// over GameState, and the VM is pure over the signal.
//
// THE RED/GREEN PAIR IS THE WHOLE ACCEPTANCE CRITERION. A check that simply
// stopped saying UPGRADE everywhere would pass the owner's sentence and break the
// screen, so every case below is asserted against a SIBLING in the same fixture:
//
//   wood  -> Lumberyard at level 6 == maxLevel  -> MAXED  -> door must NOT say UPGRADE
//   iron  -> Foundry    at level 2 <  maxLevel  -> room   -> door MUST still say UPGRADE
//   stone -> no Stoneyard built at all          -> unbuilt-> door MUST still say BUILD
//
// !! ONE CONTAINER PER RESOURCE - OWNER RULING 23 (2026-09-06), recorded verbatim in
// the `_singletonNote` on the lumberyard row of structures-catalog.json: "also cap
// only one of each storage type, the idea is they should level them". That is why a
// maxed container's door is SPEND and not "build another one": there is no second
// container to build, so BUILD would be the same class of lie pointed elsewhere.
// SPEND is the verb the over-cap branch already uses for "storage is not the fix"
// (HarvestResultVM.cs, the WO-1099 block).
//
// !! THE CEILING IS NEVER A LITERAL HERE. maxLevel is read from the catalog row via
// TownBankCapacity.TryGetContainerRow, which already clamps to
// RepoProps.MaxStructureLevel. Case [ceiling-is-authored] fails if the fixture and
// the catalog disagree, so a data edit that adds a rung cannot silently rot this
// suite into testing a number nobody authored.
//
// Markers: HARVEST_MAXED_DOOR_OK / HARVEST_MAXED_DOOR_FAIL.
// Standalone: run-unity-method DeNelle.Editor.Regression.HarvestMaxedContainerDoorRegression.RunAll
// =============================================================================

using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using UnityEngine;
using DeNelle.Core.Economy;
using DeNelle.Core.State;
using DeNelle.Core.UI;
using DeNelle.Village;

namespace DeNelle.Editor.Regression
{
    public static class HarvestMaxedContainerDoorRegression
    {
        private const string WoodContainerId = "lumberyard";
        private const string IronContainerId = "foundry";
        private const string StoneContainerId = "silo";   // displayName "Stoneyard"

        /// <summary>The level the iron container sits at - deliberately low, so [sibling-still-upgrades]
        /// is a real control and not an accident of the ceiling.</summary>
        private const int IronLevel = 2;

        public static void RunAll()
        {
            if (Run(out string reason)) Debug.Log("HARVEST_MAXED_DOOR_OK - " + reason);
            else Debug.LogError("HARVEST_MAXED_DOOR_FAIL: " + reason);
        }

        public static bool Run(out string reason)
        {
            var failures = new List<string>();
            var log = new StringBuilder("=== HarvestMaxedContainerDoorRegression ===\n");
            GameStateService prior = GameStateService.Instance;
            GameObject host = null;

            try
            {
                // -- the REAL catalog. CatalogBootstrap.Register is [RuntimeInitializeOnLoadMethod],
                // so it never fires in an editor batch; every existing suite that needs the catalog
                // invokes it the same way (AuthoredBarracksProvenanceRegression:18, OwnedTownManifestBake:25).
                var register = typeof(CatalogBootstrap)
                    .GetMethod("Register", BindingFlags.Static | BindingFlags.NonPublic);
                if (register == null)
                {
                    reason = "HARVEST_MAXED_DOOR_FAIL CatalogBootstrap.Register not found - the catalog " +
                             "cannot be populated, so no ceiling can be read and this suite would prove nothing";
                    Debug.LogError(reason);
                    return false;
                }
                register.Invoke(null, null);

                // -- the ceiling, AUTHORED. Read before the fixture is built, because the fixture's
                // maxed level IS this number.
                if (!TownBankCapacity.TryGetContainerRow(BankResource.Wood, out string woodName,
                                                         out int woodUnit, out int woodMax))
                {
                    reason = "HARVEST_MAXED_DOOR_FAIL TownBankCapacity.TryGetContainerRow(Wood) returned " +
                             "false after CatalogBootstrap.Register - the wood storage row is missing, so " +
                             "the maxed-container case cannot be set up (this is a DATA failure, not a pass)";
                    Debug.LogError(reason);
                    return false;
                }
                TownBankCapacity.TryGetContainerRow(BankResource.Iron, out string ironName,
                                                    out int _, out int ironMax);
                TownBankCapacity.TryGetContainerRow(BankResource.Stone, out string stoneName,
                                                    out int _, out int stoneMax);
                log.AppendLine($"authored ceilings: {woodName} max={woodMax} unit={woodUnit} | " +
                               $"{ironName} max={ironMax} | {stoneName} max={stoneMax}");

                // -- 0 [ceiling-is-authored] ---------------------------------------
                // The fixture below places the wood container AT woodMax. If the catalog ever
                // authors a ceiling of 1 there is no "below max" rung and the sibling control
                // collapses - say so instead of passing vacuously.
                if (woodMax < 2 || ironMax < IronLevel + 1)
                    failures.Add($"[ceiling-is-authored] the authored ceilings (wood={woodMax}, iron={ironMax}) " +
                                 $"leave no room for the fixture: the suite needs wood>=2 so a maxed level " +
                                 $"exists, and iron>={IronLevel + 1} so the sibling can sit below its own max");

                // -- the REAL state -----------------------------------------------
                var fixture = ScriptableObject.CreateInstance<GameState>();
                fixture.Onboarded = true;
                fixture.BaseLayout = new List<PlacedStructureData>
                {
                    // WOOD: the owner's case - the single container at its authored ceiling.
                    new PlacedStructureData(WoodContainerId, 4, 4, 0, woodMax),
                    // IRON: the control - built, and with rungs left. TWO of them, one AT the ceiling
                    // and one below it, because TownBankCapacity.BuildSlots enforces no singleton and
                    // the owner's own 2026-09-06 save carried two Foundries. A signal that keyed off
                    // the HIGHEST level would call this maxed and retire a door the player can buy.
                    new PlacedStructureData(IronContainerId, 6, 4, 0, ironMax),
                    new PlacedStructureData(IronContainerId, 8, 4, 0, IronLevel),
                    // STONE: deliberately ABSENT, so BUILD stays provable.
                };
                fixture.MarkEverBuilt(WoodContainerId);   // TownBankCapacity.BuildSlots' existence co-gate
                fixture.MarkEverBuilt(IronContainerId);

                host = new GameObject("GSS (harvest-maxed-door oracle)");
                var service = host.AddComponent<GameStateService>();
                if (!InstallState(service, fixture))
                {
                    reason = "HARVEST_MAXED_DOOR_FAIL GameStateService state seam unavailable - the live " +
                             "Apportion cannot be driven, so nothing below would be evidence";
                    Debug.LogError(reason);
                    return false;
                }

                // -- THE PROVING READ. The real signal, off the real slots. -------
                var wood = HarvestOverflowModal.StorageGrowthFor(BankResource.Wood);
                var iron = HarvestOverflowModal.StorageGrowthFor(BankResource.Iron);
                var stone = HarvestOverflowModal.StorageGrowthFor(BankResource.Stone);
                log.AppendLine("signal wood  : " + wood);
                log.AppendLine("signal iron  : " + iron);
                log.AppendLine("signal stone : " + stone);

                // -- 1 [signal-carries-the-level] ---------------------------------
                // RED on HEAD before WO-1863: there was no signal to read at all, only a count.
                if (wood.Built != 1 || wood.TopLevel != woodMax)
                    failures.Add($"[signal-carries-the-level] wood signal read built={wood.Built} " +
                                 $"topLevel={wood.TopLevel}; the fixture placed ONE {woodName} at level " +
                                 $"{woodMax}, so the slot level is not reaching the signal");
                if (!wood.MaxedOut)
                    failures.Add($"[signal-carries-the-level] a {woodName} at level {wood.TopLevel} against " +
                                 $"an authored ceiling of {wood.MaxLevel} is NOT reported as maxed out");
                // [lowest-rung-decides] - two Foundries, one AT the ceiling and one below it. RED if
                // the signal keys off the highest level: it would report maxed and kill a live door.
                if (iron.Built != 2)
                    failures.Add($"[lowest-rung-decides] two {ironName} rows were placed but the signal " +
                                 $"counted {iron.Built} - BuildSlots is not walking both layout records, so " +
                                 $"the multi-container case below is not actually being exercised");
                if (iron.TopLevel != IronLevel)
                    failures.Add($"[lowest-rung-decides] with {ironName} levels {ironMax} and {IronLevel} the " +
                                 $"signal must carry the LOWEST rung ({IronLevel}); it carries {iron.TopLevel}");
                if (!iron.CanUpgrade || iron.MaxedOut)
                    failures.Add($"[lowest-rung-decides] the {ironName} at level {iron.TopLevel} of " +
                                 $"{iron.MaxLevel} reports canUpgrade={iron.CanUpgrade} maxedOut={iron.MaxedOut} " +
                                 $"- one container of the pair is upgradeable, so the door must stay live");
                if (!stone.CanBuild || stone.Built != 0)
                    failures.Add($"[signal-carries-the-level] no {stoneName} was placed, yet the signal reads " +
                                 $"built={stone.Built} canBuild={stone.CanBuild}");

                // -- THE SCREEN. Real VM, real signal, three FULL stores. ---------
                var vm = HarvestResultVM.Build(FullFrame(woodName, ironName, stoneName),
                                               HarvestOverflowModal.StorageGrowthFor);
                log.AppendLine("screen: " + vm.ScreenText);

                var woodRow = RowFor(vm, "Wood");
                var ironRow = RowFor(vm, "Iron");
                var stoneRow = RowFor(vm, "Stone");
                if (woodRow == null || ironRow == null || stoneRow == null)
                {
                    failures.Add("[three-rows] the fixture's three FULL resources did not all produce a row " +
                                 "(wood=" + (woodRow != null) + " iron=" + (ironRow != null) +
                                 " stone=" + (stoneRow != null) + ")");
                }
                else
                {
                    log.AppendLine($"door wood  : '{woodRow.ActionText}'");
                    log.AppendLine($"door iron  : '{ironRow.ActionText}'");
                    log.AppendLine($"door stone : '{stoneRow.ActionText}'");

                    // -- 2 [maxed-never-says-upgrade] -- THE OWNER'S SENTENCE -----
                    // RED on HEAD: this read "UPGRADE LUMBERYARD".
                    if (string.Equals(woodRow.ActionVerb, "UPGRADE", StringComparison.Ordinal))
                        failures.Add($"[maxed-never-says-upgrade] the {woodName} is at level {wood.TopLevel} of " +
                                     $"{wood.MaxLevel} - its ceiling - and the harvest door still reads " +
                                     $"'{woodRow.ActionText}'. There is nothing to upgrade (owner, 2026-09-18).");

                    // -- 3 [maxed-says-spend] ------------------------------------
                    // Owner ruling 23: one container per resource, so BUILD is not available either.
                    if (!string.Equals(woodRow.ActionVerb, "SPEND", StringComparison.Ordinal))
                        failures.Add($"[maxed-says-spend] a maxed container has no growth left on EITHER axis " +
                                     $"(one per resource, owner ruling 23), so the only honest verb is SPEND; " +
                                     $"the door reads '{woodRow.ActionText}'");
                    if (!woodRow.HasAction)
                        failures.Add("[maxed-says-spend] the maxed row lost its door entirely - a blocked row " +
                                     "with no instruction is the WO-1525 dead end, not a fix");

                    // -- 4 [sibling-still-upgrades] -- THE GREEN HALF -------------
                    // Without this, "return false always" would pass case 2.
                    if (!string.Equals(ironRow.ActionVerb, "UPGRADE", StringComparison.Ordinal))
                        failures.Add($"[sibling-still-upgrades] the {ironName} sits at level {iron.TopLevel} of " +
                                     $"{iron.MaxLevel} and CAN be upgraded, but its door reads " +
                                     $"'{ironRow.ActionText}' - the maxed check has swallowed a live door");

                    // -- 5 [unbuilt-still-builds] --------------------------------
                    if (!string.Equals(stoneRow.ActionVerb, "BUILD", StringComparison.Ordinal))
                        failures.Add($"[unbuilt-still-builds] no {stoneName} exists, so the door must offer to " +
                                     $"BUILD one; it reads '{stoneRow.ActionText}'");

                    // -- 6 [target-is-the-thing-named] ---------------------------
                    // SPEND names the RESOURCE (the WO-1099 rule: the container is the ceiling you are
                    // above, not a thing to buy); BUILD/UPGRADE name the CONTAINER.
                    if (woodRow.ActionTarget != "WOOD")
                        failures.Add($"[target-is-the-thing-named] the SPEND door must name the resource to " +
                                     $"spend, not a building; target='{woodRow.ActionTarget}'");
                    if (ironRow.ActionTarget != ironName.ToUpperInvariant())
                        failures.Add($"[target-is-the-thing-named] the UPGRADE door must name the container; " +
                                     $"target='{ironRow.ActionTarget}' expected '{ironName.ToUpperInvariant()}'");
                }

                // -- 7 [unknown-ceiling-keeps-the-door] --------------------------
                // A batch with no catalog cannot know a ceiling. Retiring a real upgrade door on that
                // ignorance would be worse than the bug: pure-seam check, no state needed.
                // -- 7b [throw-path-degrades-to-build] ---------------------------
                // StorageGrowthFor seeds `From(0,0,0)` BEFORE its Guard.Try, because Guard swallows
                // and logs by design: if Apportion throws, this struct is what the player gets. A bare
                // `default(StorageGrowthSignal)` would read Built=0 with CanBuild=FALSE, which the VM
                // resolves to SPEND and targets the CONTAINER - the nonsense chip "SPEND LUMBERYARD".
                var seed = StorageGrowthSignal.From(0, 0, 0);
                if (!seed.CanBuild || seed.MaxedOut || seed.CanUpgrade)
                    failures.Add("[throw-path-degrades-to-build] the zero signal (the Guard.Try seed and " +
                                 "the documented null-signal answer) must mean BUILD; got " + seed);

                var unknown = StorageGrowthSignal.From(built: 1, topLevel: 3, maxLevel: 0);
                if (!unknown.CanUpgrade || unknown.MaxedOut)
                    failures.Add("[unknown-ceiling-keeps-the-door] an UNKNOWN ceiling (maxLevel 0, the " +
                                 "no-catalog answer TryGetContainerRow traces) must keep the legacy UPGRADE " +
                                 "door, not silently report the container as maxed; got " + unknown);

                // -- 8 [overcap-still-spends] ------------------------------------
                // The WO-1099 branch must be untouched by this change: OverCap outranks every growth
                // answer, including a container with rungs left.
                var over = HarvestResultVM.Build(new List<BankOverflowStatus>
                {
                    Status("Iron", ironName, BankResource.Iron, granted: 0, requested: 9000,
                           current: 99999, max: 10000, overCap: true),
                }, HarvestOverflowModal.StorageGrowthFor);
                var overRow = over.Rows.Count > 0 ? over.Rows[0] : null;
                if (overRow == null || !string.Equals(overRow.ActionVerb, "SPEND", StringComparison.Ordinal))
                    failures.Add("[overcap-still-spends] an OVER-cap row must keep the WO-1099 SPEND door even " +
                                 "though its container can still be upgraded; got '" +
                                 (overRow != null ? overRow.ActionText : "<no row>") + "'");
            }
            catch (Exception ex)
            {
                failures.Add("suite threw " + ex.GetType().Name + ": " + ex.Message);
            }
            finally
            {
                if (host != null) UnityEngine.Object.DestroyImmediate(host);
                RestoreGssInstance(prior);
            }

            if (failures.Count == 0)
            {
                reason = "HARVEST_MAXED_DOOR_OK a maxed storage container's harvest door says SPEND, a " +
                         "container with rungs left still says UPGRADE, and an unbuilt one still says BUILD";
                Debug.Log(reason + "\n" + log);
                return true;
            }

            reason = "HARVEST_MAXED_DOOR_FAIL " + string.Join(" | ", failures);
            Debug.LogError(reason + "\n" + log);
            return false;
        }

        /// <summary>Three FULL stores, one per cappable resource - the state the owner was in when the
        /// harvest result offered him an upgrade he could not buy.</summary>
        private static List<BankOverflowStatus> FullFrame(string woodContainer, string ironContainer,
                                                          string stoneContainer) =>
            new List<BankOverflowStatus>
            {
                Status("Wood",  woodContainer,  BankResource.Wood,  0, 5000, 26000, 26000),
                Status("Iron",  ironContainer,  BankResource.Iron,  0, 4000, 10000, 10000),
                Status("Stone", stoneContainer, BankResource.Stone, 0, 3000,  3000,  3000),
            };

        private static BankOverflowStatus Status(string name, string container, BankResource res,
                                                 int granted, int requested, int current, int max,
                                                 bool overCap = false)
            => new BankOverflowStatus
            {
                Available = true,
                Resource = res,
                ResourceName = name,
                ContainerName = container,
                Requested = requested,
                Granted = granted,
                Lost = requested - granted,
                Current = current,
                Max = max,
                OverCap = overCap,
                Source = HarvestOverflowModal.CollectorSource,
            };

        private static HarvestResultRow RowFor(HarvestResultVM vm, string resourceName)
        {
            for (int i = 0; i < vm.Rows.Count; i++)
                if (vm.Rows[i] != null &&
                    string.Equals(vm.Rows[i].ResourceName, resourceName, StringComparison.OrdinalIgnoreCase))
                    return vm.Rows[i];
            return null;
        }

        private static bool InstallState(GameStateService service, GameState state)
        {
            var stateField = typeof(GameStateService)
                .GetField("_state", BindingFlags.NonPublic | BindingFlags.Instance);
            if (stateField == null) return false;
            stateField.SetValue(service, state);
            var instance = typeof(GameStateService)
                .GetField("_instance", BindingFlags.NonPublic | BindingFlags.Static);
            if (instance == null) return false;
            instance.SetValue(null, service);
            return true;
        }

        private static void RestoreGssInstance(GameStateService prior)
        {
            var instance = typeof(GameStateService)
                .GetField("_instance", BindingFlags.NonPublic | BindingFlags.Static);
            if (instance != null) instance.SetValue(null, prior);
        }
    }
}

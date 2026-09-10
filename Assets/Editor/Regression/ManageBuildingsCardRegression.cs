using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using DeNelle.Core.Catalog; // WO-1657 item B - CatalogRegistry.ResolveUpgradeId (collector_farm -> farm)
using DeNelle.Core.Jobs;
using DeNelle.Core.Manage;   // WO-1657 item B - ManageSelectionVM / ManageTabId, the composed detail
using DeNelle.Core.State;
using DeNelle.Village;
using DeNelle.Village.UI;
using UnityEngine;

namespace DeNelle.Editor
{
    /// <summary>Headless contract for WO-1418's compact Buildings destination.</summary>
    public static class ManageBuildingsCardRegression
    {
        private const string PanelPath = "Assets/_Modules/Village/UI/Manage/ManageScreenPanel.cs";
        private const string BuildingPortraitRoot = "Assets/Resources/Portraits/Buildings";

        public static bool Run(out string reason)
        {
            var failures = new List<string>();
            var log = new StringBuilder("=== ManageBuildingsCardRegression ===\n");
            try
            {
                CheckLiveModel(failures, log);
                CheckFoundingLevelLine(failures, log);
                CheckPanelSource(failures, log);
                CheckBuildingPortraitCoverage(failures, log);
            }
            catch (Exception ex)
            {
                failures.Add("suite threw " + ex.GetType().Name + ": " + ex.Message);
            }

            if (failures.Count == 0)
            {
                reason = "MANAGE_BUILDINGS_CARD_OK compact building rail/card/band contract holds";
                Debug.Log(reason + "\n" + log);
                return true;
            }

            reason = "MANAGE_BUILDINGS_CARD_FAIL " + string.Join(" | ", failures);
            Debug.LogError(reason + "\n" + log);
            return false;
        }

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
                fixture.BaseLayout = new List<PlacedStructureData>
                {
                    new PlacedStructureData("forge", 2, 2, 0, 1),
                    new PlacedStructureData("forge", 3, 2, 0, 1),
                    new PlacedStructureData("lumbermill", 4, 2, 0, 1),
                    new PlacedStructureData("barracks", 6, 2, 0, 1),
                };
                fixture.BuildingTiers["forge"] = BuildingTierCatalog.MaxTier("forge");
                fixture.BuildingTiers["lumbermill"] = 1;
                fixture.BuildingTiers["barracks"] = 2;
                fixture.Wood = 100000;
                fixture.Iron = 100000;
                var balances = fixture.Resources;
                balances.Food = 100000;
                balances.Coins = 100000;
                balances.Crystals = 100000;
                fixture.Resources = balances;
                fixture.ObsidianQueue = ObsidianQueueState.Empty();

                host = new GameObject("GSS (manage-buildings-card oracle)");
                var service = host.AddComponent<GameStateService>();
                if (!InstallState(service, fixture))
                {
                    failures.Add("[fixture] GameStateService state seam is unavailable; live cases cannot run");
                    return;
                }

                var vm = new ManageScreenVM();
                vm.SelectTab(ManageTab.Buildings);
                vm.Rebuild();

                // RED: insert `if (level >= maxLevel) continue;` after maxLevel in BuildBuildingChoices.
                if (vm.BuildingChoices.Count != 3)
                    failures.Add("[one-choice-per-building] expected 3 placed ladder ids including maxed; got " +
                                 vm.BuildingChoices.Count);

                var allowed = new HashSet<string>(StringComparer.Ordinal)
                    { "Upgradable", "Max", "Locked", "Building" };
                for (int i = 0; i < vm.BuildingChoices.Count; i++)
                {
                    var choice = vm.BuildingChoices[i];
                    if (choice == null)
                    {
                        failures.Add("[one-choice-per-building] null choice at index " + i);
                        continue;
                    }
                    // RED: insert `description = "";` immediately before `var cost` in BuildBuildingChoices.
                    if (string.IsNullOrWhiteSpace(choice.Description) || !allowed.Contains(choice.StateWord))
                        failures.Add("[every-choice-speaks] " + choice.Id + " description/state is empty or outside the four words");
                    // RED: restore `Ascii(name) + " -> T" + targetTier` at the choice source.
                    if ((choice.Name ?? "").Contains("->"))
                        failures.Add("[no-arrow-labels] " + choice.Id + " carries developer arrow copy");
                    // RED: assign `AfterUpgradeText = ""` in BuildBuildingChoices.
                    if (!string.Equals(choice.StateWord, "Max", StringComparison.Ordinal) &&
                        string.IsNullOrWhiteSpace(choice.AfterUpgradeText))
                        failures.Add("[benefit-line] " + choice.Id + " has no next-tier benefit");
                }

                // RED: remove ChannelSummary.Describe's Busy == 0 branch.
                string idle = new ChannelSummary { Name = "Builders", Busy = 0, Slots = 2, Depth = 0, DepthCap = 5 }.Describe();
                if (idle.IndexOf("idle", StringComparison.OrdinalIgnoreCase) < 0)
                    failures.Add("[idle-chip-word] zero-busy channel does not say idle: " + idle);

                log.AppendLine("live choices=" + vm.BuildingChoices.Count + " idle='" + idle + "'");
            }
            finally
            {
                SetGssInstance(prior);
                if (host != null) UnityEngine.Object.DestroyImmediate(host);
                if (fixture != null) UnityEngine.Object.DestroyImmediate(fixture);
            }
        }

        // =====================================================================
        //  [founding-building-is-not-level-zero] - WO-1657 ITEM B
        // ---------------------------------------------------------------------
        //  THE DEFECT, from a device frame the owner can open:
        //  Builds/device-frames/2026-09-10_0915b_363722_build_detail_quarry_placed.png reads
        //  "Level 0 of 4" on the QUARRY card while the chip says READY, a live UPGRADE face is
        //  offered, and the stats table draws "Production / hr  1,872 -> 2,016". A placed,
        //  producing building presenting as level ZERO reads as un-built.
        //
        //  THE PRODUCER, PROVEN - not grepped (CLAUDE.md section 12):
        //  ModifierService.TierOf returns 0 on a GameState.BuildingTiers DICTIONARY MISS
        //  (ModifierService.cs:44-47) -> BuildingChoiceVM.Level -> ManageItemState.Level ->
        //  ManageVmProjection's LevelText. The deciding line is on the fresh flow-map run
        //  Builds/wave6-manageflow1: `building choice id=farm level=0/4 state=Upgradable next=1`
        //  whose benefit string is the frame's "Next level" line verbatim.
        //
        //  ⛔ THE NUMBER IS RIGHT AND THIS CASE MUST NEVER BE "FIXED" BY MAKING IT 1.
        //  building-tiers.json authors the farm ladder as tiers 1..4, tier 1 buying
        //  foodProductionMult 1.1 - the "+10%" the card offers to PURCHASE - and
        //  ModifierService.TierProductionMult states in its own code that "a tier below 1
        //  contributes identity" (:106). The founding state genuinely sits BELOW the ladder. A
        //  fixture that seeded a tier would be asserting on the very state that is not the bug.
        //  So THIS FIXTURE DELIBERATELY LEAVES BuildingTiers WITHOUT A "farm" KEY - that dictionary
        //  miss IS the device's state, and it is the whole point of the case.
        //
        //  THE TWO PARTS:
        //    1. FOUNDING. A placed farm with no tier entry composes a level line that does NOT
        //       claim a level, and that still states the ceiling so the player sees the ladder.
        //       (Ruling 3.7 already forbids painting a level zero - ManageResearchCardRegression's
        //       [no-level-zero] pins it on the other surface that owns this slot.)
        //    2. THE UPGRADED PATH IS UNTOUCHED. The same fixture with BuildingTiers["farm"] = 2
        //       still composes exactly "Level 2 of 4", byte for byte, so the founding branch
        //       cannot bleed into the ordinary case.
        //  ⛔ It pins the COMPOSED VM (ManageSelectionVM.LevelText), never a rendered string and
        //  never the word itself - the WORDING is an owner call isolated behind
        //  ManageVmProjection.FoundingLevelWord, so this case asserts the CONTRACT (no level
        //  claimed, ceiling stated) and lets the owner change the words without going RED.
        //
        //  RED PROOF against the pre-fix tree: restore
        //  `LevelText = item.MaxLevel > 0 ? "Level " + item.Level + " of " + item.MaxLevel : ...`
        //  in ManageVmProjection (it was :319-322) -> part 1 fires, because the line then opens
        //  with the literal this case forbids.
        // =====================================================================
        private static void CheckFoundingLevelLine(List<string> failures, StringBuilder log)
        {
            const string Tag = "[founding-building-is-not-level-zero] ";
            // The word part 3 hunts for. It is a PROBE, not the wording contract: the wording is
            // an owner call behind ManageVmProjection.FoundingLevelWord, and if the owner changes
            // it this probe is updated with it. Parts 1 and 2 - the assertions that actually pin
            // the contract - deliberately do not mention the words at all.
            const string FoundingWordProbe = "upgraded";
            // ⛔ TWO IDS, AND THEY ARE NOT INTERCHANGEABLE. "collector_farm" is the CATALOG /
            // BaseLayout id (displayName "Quarry"); "farm" is the LADDER id that
            // CatalogRegistry.ResolveUpgradeId maps it onto, and it is what CountPlacedThisTown
            // keys the tally on, what BuildingChoiceVM.Id carries, and what the device trace
            // prints (`building choice id=farm level=0/4`). There is NO "farm" row in
            // structures-catalog.json (verified 2026-09-10), so placing "farm" would place a
            // GHOST: nothing would count it, BuildingChoices would be empty, and this case would
            // FAIL after the fix as loudly as before it - a pin that proves nothing twice.
            // ⛔ NEVER rename collector_farm (WO-1657 section 5: it is a LIVE SAVE KEY).
            const string PlacedId = "collector_farm";
            const string LadderId = "farm";
            const int SeededTier = 2;
            const string UnplacedId = "barracks";  // deliberately NOT in this fixture's BaseLayout

            GameStateService prior = GameStateService.Instance;
            GameObject host = null;
            GameState fixture = null;
            bool hadTabPref = PlayerPrefs.HasKey(ManageScreenVM.LastTabPrefKey);
            int priorTabPref = PlayerPrefs.GetInt(ManageScreenVM.LastTabPrefKey, 0);
            try
            {
                // The fixture must be able to EXERCISE the thing under test. If the catalog no
                // longer maps the placement id onto the ladder id, everything below asserts on an
                // empty model - so it is checked FIRST, and it is a FAIL, never a skip.
                if (CatalogRegistry.Get(PlacedId) == null)
                {
                    failures.Add(Tag + "the catalog cannot resolve '" + PlacedId + "', so nothing could " +
                                 "have been placed and no level line composed. FAIL, not a skip.");
                    return;
                }
                string resolved = CatalogRegistry.ResolveUpgradeId(PlacedId);
                if (!string.Equals(resolved, LadderId, StringComparison.OrdinalIgnoreCase))
                {
                    failures.Add(Tag + "CatalogRegistry.ResolveUpgradeId('" + PlacedId + "') now returns '" +
                                 (resolved ?? "<null>") + "', not '" + LadderId + "'. CountPlacedThisTown keys " +
                                 "its tally on that mapping, so this fixture would place a structure no " +
                                 "BuildingChoice counts and the case would assert on nothing. FAIL, not a skip.");
                    return;
                }

                int ceiling = BuildingTierCatalog.MaxTier(LadderId);
                if (ceiling < 2)
                {
                    failures.Add(Tag + "building-tiers.json no longer authors a ladder of 2+ tiers for '" +
                                 LadderId + "' (MaxTier=" + ceiling + "), so neither half of this case " +
                                 "could have been exercised. FAIL, not a skip.");
                    return;
                }

                fixture = ScriptableObject.CreateInstance<GameState>();
                fixture.Onboarded = true;
                fixture.VillageTier = 5;
                fixture.BaseLayout = new List<PlacedStructureData>
                {
                    new PlacedStructureData(PlacedId, 4, 2, 0, 1),
                };
                // ⛔ NO fixture.BuildingTiers[LadderId] ENTRY, DELIBERATELY. The dictionary miss is
                // the state the device is in and the state under test. Seeding one here would
                // quietly test the case that already worked.
                fixture.Wood = 1000000;
                fixture.Iron = 1000000;
                var balances = fixture.Resources;
                balances.Food = 1000000;
                balances.Coins = 1000000;
                balances.Crystals = 1000000;
                fixture.Resources = balances;
                fixture.ObsidianQueue = ObsidianQueueState.Empty();

                host = new GameObject("GSS (manage-founding-level oracle)");
                var service = host.AddComponent<GameStateService>();
                if (!InstallState(service, fixture))
                {
                    failures.Add(Tag + "GameStateService state seam is unavailable, so the level line " +
                                 "could not be composed. FAIL, not a skip.");
                    return;
                }

                // ── PART 1: the founding building ────────────────────────────
                string founding = ComposeLevelText(LadderId);
                if (founding == null)
                {
                    failures.Add(Tag + "OpenDetail(Build, " + LadderId + ") composed no visible selection, " +
                                 "so the level line could not be read. FAIL, not a skip.");
                    return;
                }

                // The forbidden opening is built from parts so this message can never itself trip
                // a source scan that hunts the literal (ManageResearchCardRegression's
                // [no-level-zero] scans a panel body for exactly that shape).
                string forbidden = "Level " + 0;
                if (founding.StartsWith(forbidden, StringComparison.Ordinal))
                    failures.Add(Tag + "a PLACED, producing '" + LadderId + "' with no tier entry composes " +
                                 "the level line \"" + founding + "\". ModifierService.TierOf returned a " +
                                 "DICTIONARY MISS, not a stored level, and the card painted the sentinel as " +
                                 "though it were a rung - which reads as un-built beside a READY chip and a " +
                                 "live upgrade face. Ruling 3.7 already forbids this on the Research card.");

                if (founding.IndexOf(ceiling.ToString(), StringComparison.Ordinal) < 0)
                    failures.Add(Tag + "the founding level line \"" + founding + "\" never states the ladder " +
                                 "ceiling (" + ceiling + "). Dropping the number is not a fix - the player " +
                                 "then cannot see there is a ladder at all, which loses more than the wrong " +
                                 "number cost.");

                log.AppendLine("founding level line='" + founding + "' ceiling=" + ceiling);

                // ── PART 2: the ordinary upgraded path is byte-identical ─────
                fixture.BuildingTiers[LadderId] = SeededTier;
                string upgraded = ComposeLevelText(LadderId);
                string expected = "Level " + SeededTier + " of " + ceiling;
                if (upgraded == null)
                {
                    failures.Add(Tag + "with BuildingTiers[" + LadderId + "]=" + SeededTier + " the detail " +
                                 "composed no visible selection. FAIL, not a skip.");
                }
                else if (!string.Equals(upgraded, expected, StringComparison.Ordinal))
                {
                    failures.Add(Tag + "an UPGRADED '" + LadderId + "' composes \"" + upgraded + "\" but must " +
                                 "still read exactly \"" + expected + "\". The founding branch has bled into " +
                                 "the ordinary path - the WO-1657 change is display-only and must leave every " +
                                 "level >= 1 byte-identical.");
                }
                log.AppendLine("upgraded level line='" + (upgraded ?? "<null>") + "'");

                // ── PART 3: an UNBUILT building must not claim "not yet upgraded" ─────
                // ⚠ WHY THIS IS HERE. ProjectSelection is shared by EVERY tab, so the founding
                // branch would be a NEW wrong claim if an unbuilt row could reach it - "not yet
                // upgraded" asserts the thing EXISTS. Today it cannot: ComposeUnplacedItem,
                // ComposeOwnedNoUpgradeItem and ComposeTroopItem all set MaxLevel = 0, and the
                // branch is gated on MaxLevel > 0 (ComposeDefenseItem cannot reach it either -
                // BuildDefenseChoices floors its level with Mathf.Clamp(.., 1, ceiling)). That
                // guarantee is STRUCTURAL and invisible, which is exactly the kind that gets
                // deleted by accident, so it is pinned rather than trusted.
                string unbuilt = ComposeLevelText(UnplacedId);
                if (unbuilt != null && unbuilt.IndexOf(FoundingWordProbe, StringComparison.OrdinalIgnoreCase) >= 0)
                    failures.Add(Tag + "the UNBUILT '" + UnplacedId + "' composes the level line \"" + unbuilt +
                                 "\". The founding line means \"placed, producing, no rung bought\" - on a " +
                                 "building that does not exist it is a new false claim replacing the old one. " +
                                 "Something has started handing unplaced rows a ceiling.");
                log.AppendLine("unbuilt level line='" + (unbuilt ?? "<null>") + "'");
            }
            catch (Exception ex)
            {
                failures.Add(Tag + "threw " + ex.GetType().Name + ": " + ex.Message);
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

        /// <summary>Opens the BUILD detail for <paramref name="itemId"/> on a FRESH model and
        /// returns the composed <c>ManageSelectionVM.LevelText</c>, or null when no selection was
        /// composed. A fresh VM per read so a cached rebuild cannot mask a state change.</summary>
        private static string ComposeLevelText(string itemId)
        {
            var model = new ManageScreenVM();
            model.EnterTab(ManageTabId.Build);
            model.OpenDetail(ManageTabId.Build, itemId, null, null);
            var ws = model.ComposeWorkspace();
            if (ws == null || ws.Tabs == null) return null;
            for (int i = 0; i < ws.Tabs.Count; i++)
                if (ws.Tabs[i] != null && ws.Tabs[i].Selection != null && ws.Tabs[i].Selection.Visible)
                    return ws.Tabs[i].Selection.LevelText ?? string.Empty;
            return null;
        }

        private static void CheckPanelSource(List<string> failures, StringBuilder log)
        {
            string panel = ReadSource(PanelPath, failures);
            if (panel == null) return;

            string card = Body(panel, "private void BuildBuildingCard(", "private static void BuildDisabledBuildingFace(");
            // RED: replace selected.StateWord with a colour-only state treatment.
            if (card == null || !card.Contains("selected.StateWord"))
                failures.Add("[card-paints-the-word] BuildBuildingCard does not paint selected.StateWord");

            string destination = Body(panel, "private void RenderBuildingsDestination(", "private void AddBuildingWorkspaceRow(");
            // ⭐ RE-POINTED 2026-09-06 (WO-1422 ruling 3.4). The SECOND half of this check used to
            // require `panel.Contains("Showing \" + (first + 1)")` — i.e. it asserted the paged
            // Defense/Research list was STILL ALIVE, and its message said so in those words. WO-1422
            // retires that path entirely: Defense and Research were AddBrowseRow's last two readers,
            // so the pager, AddBrowseRow and BuildBrowseRowContent are DELETED. A pin that demands a
            // deleted method is a doc arguing for a design the code no longer has (CLAUDE.md §15),
            // and it would have made this suite the thing BLOCKING the ruling.
            //
            // The FIRST half is the half that was ever about Buildings, and it is unchanged: this
            // destination fits its rail on one screen and must never page. The retirement itself is
            // now pinned once, in ManageProgressiveDisclosureRegression's [pager-retired] case —
            // one owner, not a copy here as well.
            // RED PROOF: add an `AddNoteRow("Showing " + first + ...)` line to
            // RenderBuildingsDestination.
            if (destination == null)
                failures.Add("[no-paging-when-it-fits] RenderBuildingsDestination was not found - the Buildings " +
                             "workspace does not exist, so the no-paging pin could not be scoped. FAIL, not a skip");
            else if (destination.Contains("Showing "))
                failures.Add("[no-paging-when-it-fits] the Buildings destination has re-grown a \"Showing n-m of N\" " +
                             "page-count sentence. All four Manage tabs render ONE workspace - portrait rail, one " +
                             "selected card, a NOW band, one footer row - and nothing pages (WO-1418, WO-1422 3.4)");

            // RED: lower TroopCtaY1 until the replayed height is under 112px.
            float y0 = Const(panel, "TroopCtaY0"), y1 = Const(panel, "TroopCtaY1"), px = Const(panel, "TroopWorkspacePx");
            if (y0 < 0f || y1 < 0f || px < 0f || (y1 - y0) * px < 112f)
                failures.Add("[touch-floor] building CTA line is below 112 reference px");

            string placement = Body(panel, "private void ApplyDrawerPlacement()", "private void SyncQueueToggleFace()");
            // WO-2019: Queue is a full opaque modal everywhere. The retired short-band shape
            // clipped ARMY horizontally; safety now means the workspace is not actionable under
            // the modal and is restored from the same state on close.
            if (placement == null || !placement.Contains("bool band = false;") ||
                !placement.Contains("_workspaceHost.gameObject.SetActive(WorkspaceActive && !_queueDrawerOpen)"))
                failures.Add("[queue-modal-covers-workspace] Queue is not one full modal with the selected " +
                             "workspace disabled underneath it");

            string renderList = Body(panel, "private void RenderList()", "private string FindSummary");
            // RED: leave the former Buildings else-if in RenderList instead of moving this exact footer.
            if (destination == null || !destination.Contains("Need another town structure?") ||
                renderList == null || renderList.Contains("Need another town structure?"))
                failures.Add("[footer-moved-not-lost] Open-build footer is missing or still in the paged path");

            string strip = Body(panel, "private void RenderStrip()", "private void RenderSlotOffer()");
            // RED: replace the two Describe() calls in RenderStrip with Name + Busy/Slots.
            if (strip == null || !strip.Contains("_vm.Channels[i].Describe()") || !strip.Contains("s.Describe()"))
                failures.Add("[idle-chip-word] the VM's idle wording is not painted in both Manage chip surfaces");

            // RED: restore the loop which makes BuildingNowRow_n siblings below the dark band.
            string buildingNow = Body(panel, "private void AddBuildingNowBand()", "private void RenderTroopsDestination(");
            if (buildingNow == null || buildingNow.Contains("MakeRowHost(\"BuildingNowRow_") ||
                !buildingNow.Contains("+\" + hiddenJobs + \" more") ||
                !buildingNow.Contains("BuildingSprite(FindBuildingChoice(first.BuildingId))"))
                failures.Add("[building-now-stays-in-band] queue rows escape the band, lack +N more, or use troop art");

            // ⭐ RE-POINTED 2026-09-06 (WO-1423). THIS CASE USED TO PIN THE DEAD END.
            // It read:
            //     if (card == null || !card.Contains("UNLOCKS AT VILLAGE LEVEL ") ||
            //         !card.Contains("selected.RequiresVillageTier"))
            //         failures.Add("[locked-cta-names-village-level] locked CTA does not name its
            //                       village-tier gate");
            // i.e. it FAILED unless the locked card painted its requirement on a DISABLED FACE - the
            // very shape the owner reported as a progression dead end ("some items are locked till
            // village level 1, which there is no way to trigger"). The card that named the gate was
            // the only card with no route to the control that opens it, and this suite was holding
            // that shape in place. A suite that proves a lock is NAMED but never that it is
            // ESCAPABLE is the exact hole that let this ship.
            //
            // THE RULING (WO-1423): a locked card must do BOTH - name the gate in words AND offer a
            // live door to the control that opens it. The gate sentence is BODY TEXT (a sentence
            // never fits a button face - the WO-1422 3.7 lesson) and the door is ONE full-width live
            // button into ViewDetails -> OpenUpgradePanel, whose action band carries the
            // VillageGated "Raise Village Tier N" control. Same shape as the locked RESEARCH card.
            //
            // Scoped to the Locked BRANCH, not the whole method: the card also reads selected.Locked
            // up top for the portrait dim + lock badge, and a whole-body Contains could be satisfied
            // by those.
            // REVERT RECIPE (RED): put `BuildDisabledBuildingFace(card, "BuildingCta_Locked",
            // "UNLOCKS AT VILLAGE LEVEL" + selected.RequiresVillageTier); return;` back as the whole
            // Locked branch -> the door/prose halves all fire.
            string lockedBranch = card == null ? null
                : Body(card, "// Locked and in-progress choices", "if (string.Equals(selected.StateWord, \"Building\"");
            if (lockedBranch == null)
                failures.Add("[locked-card-names-the-gate-and-opens-it] the Locked branch of BuildBuildingCard was " +
                             "not found, so the dead-end pin could not be scoped. FAIL, not a skip");
            else
            {
                if (!lockedBranch.Contains("selected.LockReason") || !lockedBranch.Contains("BuildingLockReason"))
                    failures.Add("[locked-card-names-the-gate-and-opens-it] the locked building card does not paint " +
                                 "the VM's LockReason as a named body line, so the player is never told WHICH gate " +
                                 "holds the upgrade");
                if (!lockedBranch.Contains("selected.ViewDetails") ||
                    !lockedBranch.Contains("new Vector2(0.98f, TroopCtaY1)"))
                    failures.Add("[locked-card-names-the-gate-and-opens-it] the locked building card has no ONE " +
                                 "full-width LIVE door into ViewDetails -> OpenUpgradePanel. A named lock with no " +
                                 "route to the control that opens it is the WO-1423 progression dead end");
                if (lockedBranch.Contains("BuildDisabledBuildingFace"))
                    failures.Add("[locked-card-names-the-gate-and-opens-it] the locked building card is back on a " +
                                 "DISABLED face. The gate sentence belongs in body text and the CTA line belongs to " +
                                 "a live door (WO-1423); a dead face as the only affordance is the defect itself");
            }
            // The VM, not the View, must author both strings (the Research precedent).
            // REVERT RECIPE (RED): delete the LockReason/LockCtaLabel assignments in BuildBuildingChoices.
            string vmSource = ReadSource("Assets/_Modules/Village/UI/Manage/ManageScreenVM.cs", failures);
            if (vmSource != null && (!vmSource.Contains("LockReason = isLocked") || !vmSource.Contains("LockCtaLabel = isLocked")))
                failures.Add("[locked-card-names-the-gate-and-opens-it] BuildBuildingChoices does not author the " +
                             "locked card's sentence and door word in the VM, so the View is inventing copy");

            // RED: remove the tail spacer and restore count-normalized scrolling.
            string workspace = Body(panel, "private void AddBuildingWorkspaceRow(", "private void BuildBuildingRailRow(");
            if (workspace == null || !workspace.Contains("BuildingRailTailSpacer") ||
                !workspace.Contains("selectedTopPx") || !workspace.Contains("StopMovement()"))
                failures.Add("[building-rail-whole-row] selected rail row cannot align flush to the viewport top");

            // RED: load the VM's Portraits IconKey before asking the palette resolver.
            string art = Body(panel, "private static Sprite BuildingSprite(BuildingChoiceVM", "private void BuildBuildingCard(");
            if (art == null || !art.Contains("ResolveEntryArtPublic") || art.Contains("choice.IconKey") ||
                !art.Contains("CatalogRegistry.Get(choice.CatalogEntryId)") ||
                !art.Contains("LoadManageBuildingSprite(choice.Id, choice.Level)") ||
                !art.Contains("ManageBuildingPortraitGaps.Contains(choice.Id)") ||
                !art.Contains("ConceptIconResolver.ResolveAny"))
                failures.Add("[building-art-palette-first] Manage can paint NPC Portraits art or ignores the typed catalog id");

            if (strip == null || !strip.Contains("FitSingleLine(cell, ElarionUiKit.FontHardFloor"))
                failures.Add("[chip-fit-floor] operational chips are not fitted after their live text is assigned");

            log.AppendLine("source card/destination/touch/drawer/footer/idle-paint/polish checks complete");
        }

        private static void CheckBuildingPortraitCoverage(List<string> failures, StringBuilder log)
        {
            // RED: delete any required tier portrait or its .meta file.
            string[] ids = { "arcane-tower", "armorer", "barracks", "farm", "forge", "lumbermill" };
            int[] maxLevels = { 4, 4, 6, 4, 4, 4 };
            int checkedCount = 0;

            for (int i = 0; i < ids.Length; i++)
            {
                for (int level = 1; level <= maxLevels[i]; level++)
                {
                    string suffix = level == 1 ? "" : "-" + level;
                    string relative = BuildingPortraitRoot + "/" + ids[i] + suffix + ".png";
                    string full = Path.Combine(Directory.GetCurrentDirectory(),
                        relative.Replace('/', Path.DirectorySeparatorChar));
                    if (!File.Exists(full)) failures.Add("[building-portrait-coverage] missing " + relative);
                    if (!File.Exists(full + ".meta")) failures.Add("[building-portrait-coverage] missing " + relative + ".meta");
                    checkedCount++;
                }
            }

            log.AppendLine("building portrait coverage=" + checkedCount + "/26 tiers");
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

        private static string Body(string source, string from, string until)
        {
            int start = source.IndexOf(from, StringComparison.Ordinal);
            if (start < 0) return null;
            int end = source.IndexOf(until, start + from.Length, StringComparison.Ordinal);
            return end < 0 ? null : source.Substring(start, end - start);
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

// =============================================================================
// ManageResearchCardRegression - WO-1422 section 5: the Manage RESEARCH tab takes
// the WO-1418 Buildings shape (portrait rail + one selected card + RESEARCHING NOW
// + one footer row), the paged text list is retired, and the whole perk tree is
// visible - including the states the old list HID.
// Marker: MANAGE_RESEARCH_CARD_OK / MANAGE_RESEARCH_CARD_FAIL <case>.
// -----------------------------------------------------------------------------
// Assembly: DeNelle.EditorRegression (editor-only). Registered in DataRegression.RunAll.
// Style/contract mirrors ManageBuildingsCardRegression: a LIVE half driving
// ManageScreenVM against a real GameState fixture, and a SOURCE half reading
// ManageScreenPanel.cs as text.
//
// WHY THIS SUITE EXISTS (measured, WO-1422 section 1):
//   ManageResearch_2670x1200.png (Builds/capman4, 2026-09-06 01:26) shows
//     "Showing 1-4 of 14 - page 1 of 4"
//     "Lumber Mill - Improved Logging   Ready - takes 11m 0s   [RESEARCH]"
//     "1200 gold"                       <- CLIPPED by the CLOSE bar
//   A paging sentence, developer-shaped `Building - Perk` labels, word-costs, and
//   row two cut in half. That is the exact shape WO-1418 removed from Buildings.
//
// AND WHY [perk-icon-path] IS HERE: BuildingPerkDef.IconId's own doc comment
//   (Assets/_Modules/Core/State/BuildingTierCatalog.cs:41) names
//   "Resources/HudItems/BuildingUpgrades/" - A FOLDER THAT DOES NOT EXIST.
//   The real loader is BuildingUpgradePanelMvvm.cs:2025, which reads
//   "HudIcons/BuildingUpgrades/". A lane that trusts the comment ships a card
//   with no art and no error (CLAUDE.md section 11B: a doc is hearsay).
//
// EVERY case carries a one-line REVERT RECIPE so the CLI can prove RED, restore,
// and prove GREEN. A missing fixture is a FAIL that names itself.
// =============================================================================
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using DeNelle.Core.Jobs;
using DeNelle.Core.State;
using DeNelle.Core.UI;
using DeNelle.Village;
using DeNelle.Village.Buildings.Progression;
using DeNelle.Village.UI;
using UnityEngine;

namespace DeNelle.Editor
{
    /// <summary>Headless contract for WO-1422's compact Research destination.</summary>
    public static class ManageResearchCardRegression
    {
        private const string PanelPath = "Assets/_Modules/Village/UI/Manage/ManageScreenPanel.cs";
        private const string IconRoot = "Assets/Resources/HudIcons/BuildingUpgrades";

        // The five perk-carrying buildings, measured from building-tiers.json on
        // 2026-09-06: arcane-tower 4, lumbermill 4, armorer 3, barracks 3, forge 3
        // = 17 perks. `farm` (displayName "Quarry") authors ZERO and is not a
        // placeable catalog id, so it contributes nothing either way.
        private static readonly string[] PerkBuildings = { "arcane-tower", "armorer", "barracks", "forge", "lumbermill" };

        // The four fixture perks, one per state word. Chosen so each state is
        // reached by a DIFFERENT mechanism, not by four spellings of one branch.
        private const string OwnedBuilding = "lumbermill";
        private const string OwnedPerk = "lumber-improved-logging";      // -> Researched (OwnedBuildingPerks)
        private const string BusyBuilding = "forge";
        private const string BusyPerk = "forge-efficient-smelting";      // -> Researching (live Research job)
        private const string OpenBuilding = "armorer";
        private const string OpenPerk = "blacksmith-reinforced-plating"; // -> Available (tier 1, building at 1)
        private const string LockedBuilding = "barracks";
        private const string LockedPerk = "barracks-expanded-capacity";  // -> Locked (tier 3, building at 1)

        public static bool Run(out string reason)
        {
            var failures = new List<string>();
            var log = new StringBuilder("=== ManageResearchCardRegression (WO-1422) ===\n");
            try
            {
                CheckLiveModel(failures, log);
                CheckPanelSource(failures, log);
                CheckPerkIconCoverage(failures, log);
            }
            catch (Exception ex)
            {
                failures.Add("suite threw " + ex.GetType().Name + ": " + ex.Message);
            }

            if (failures.Count == 0)
            {
                reason = "MANAGE_RESEARCH_CARD_OK Research projects one choice per PERK across the whole tree, " +
                         "carries all four state words with the CanResearch reason verbatim, prices in gold parts, " +
                         "shows TIER not LEVEL, and loads its art from HudIcons/BuildingUpgrades";
                Debug.Log(reason + "\n" + log);
                return true;
            }

            reason = "MANAGE_RESEARCH_CARD_FAIL " + string.Join(" | ", failures);
            Debug.LogError(reason + "\n" + log);
            return false;
        }

        // =====================================================================
        //  LIVE half - drive ManageScreenVM against a real GameState fixture.
        // =====================================================================
        private static void CheckLiveModel(List<string> failures, StringBuilder log)
        {
            GameStateService priorGss = GameStateService.Instance;
            BuildTimerService priorQueue = BuildTimerService.Instance;
            GameObject gssHost = null, queueHost = null;
            GameState fixture = null;
            try
            {
                fixture = ScriptableObject.CreateInstance<GameState>();
                fixture.Onboarded = true;
                // Village Tier 2: high enough that a tier-1/tier-2 perk is not
                // VILLAGE-locked, low enough that the tier-3 fixture perk stays
                // locked on the BUILDING gate - so [locked-reason-verbatim] pins
                // the building sentence, which is the one the owner reported.
                fixture.VillageTier = 2;

                fixture.BaseLayout = new List<PlacedStructureData>();
                for (int i = 0; i < PerkBuildings.Length; i++)
                    fixture.BaseLayout.Add(new PlacedStructureData(PerkBuildings[i], 2 + i * 2, 2, 0, 1));

                fixture.BuildingTiers["lumbermill"] = 2;
                fixture.BuildingTiers["arcane-tower"] = 1;
                fixture.BuildingTiers["armorer"] = 1;
                fixture.BuildingTiers["barracks"] = 1;
                fixture.BuildingTiers["forge"] = 1;

                fixture.OwnedBuildingPerks.Add(BuildingPerkService.Key(OwnedBuilding, OwnedPerk));

                fixture.Wood = 100000;
                fixture.Iron = 100000;
                var balances = fixture.Resources;
                balances.Stone = 100000;
                balances.Coins = 100000;     // gold - the only currency research spends
                balances.Crystals = 100000;
                fixture.Resources = balances;

                // The Researching state is only reachable through a LIVE research
                // job on the Research channel (BuildingPerkService.IsResearching
                // reads BuildTimerService, and returns false with no service - the
                // correct answer for a headless read, and a silent hollow pass here
                // if the queue were left uninstalled).
                fixture.ObsidianQueue = ObsidianQueueState.Empty();
                fixture.ObsidianQueue.Channel(ChannelId.Research).ActiveJobs.Add(new BuildJobData
                {
                    StructureId = BuildingPerkService.JobId(BusyBuilding, BusyPerk),
                    Kind = (int)JobKind.BuildingResearch,
                    Channel = (int)ChannelId.Research,
                    StartMs = 1d,
                    DurationMs = 1000000000d,
                });

                gssHost = new GameObject("GSS (manage-research-card oracle)");
                var service = gssHost.AddComponent<GameStateService>();
                if (!InstallState(service, fixture))
                {
                    // NOT A SKIP (the UpgradeQueueFullSurfaceRegression ruling).
                    failures.Add("[fixture] GameStateService state seam is not reflectable, so the LIVE Research " +
                                 "cases (17 choices, four state words, verbatim lock reason, gold parts) could not " +
                                 "run. This is a FAIL, not a skip.");
                    return;
                }

                queueHost = new GameObject("BuildTimerService (manage-research-card oracle)");
                var queue = queueHost.AddComponent<BuildTimerService>();
                // Awake does not run on AddComponent outside play mode, so the
                // singleton is installed explicitly.
                if (!InstallQueueInstance(queue))
                {
                    failures.Add("[fixture] BuildTimerService.Instance backing field is not reflectable, so the " +
                                 "Researching state cannot be reached and [all-four-states] would pass on three. " +
                                 "FAIL, not a skip.");
                    return;
                }

                var vm = new ManageScreenVM();
                vm.SelectTab(ManageTab.Research);
                vm.Rebuild();

                var choices = vm.ResearchChoices;
                if (choices == null)
                {
                    failures.Add("[one-choice-per-perk] ManageScreenVM.ResearchChoices is null - the Research " +
                                 "destination has no model to paint.");
                    return;
                }

                log.AppendLine("Research choices projected = " + choices.Count);
                for (int i = 0; i < choices.Count; i++)
                {
                    var c = choices[i];
                    log.AppendLine("  [" + (c == null ? "<null>" : c.StateWord) + "] " +
                                   (c == null ? "" : c.BuildingId + ":" + c.PerkId + " '" + c.Name + "' (" +
                                                     c.BuildingName + ") " + c.TierText + " cta='" + c.CtaLabel + "'"));
                }

                // -------------------------------------------------------------
                // CASE 1  [one-choice-per-perk]
                // REVERT RECIPE (RED): in BuildResearchChoices, emit ONE choice per
                //   owned BUILDING (break out of the tier/perk loops after the first
                //   perk) instead of one per perk.
                // -------------------------------------------------------------
                int authored = CountAuthoredPerks();
                if (authored != 17)
                    failures.Add("[one-choice-per-perk] building-tiers.json now authors " + authored + " perks across " +
                                 "the five perk-carrying buildings, not the 17 measured for WO-1422. Re-read ruling " +
                                 "3.6 before relaxing this - the rail is one row per PERK and the count moved.");
                if (choices.Count != authored)
                    failures.Add("[one-choice-per-perk] all five perk-carrying buildings are placed, so the rail must " +
                                 "list every authored perk: expected " + authored + " choices, got " + choices.Count +
                                 ". A per-BUILDING rail (5 rows) would need 3-4 verbs in one CTA band, which no card " +
                                 "grammar supports (ruling 3.6).");

                // -------------------------------------------------------------
                // CASE 2  [all-four-states]
                // REVERT RECIPE (RED): restore `if (IsOwned(bId, pId)) continue;`
                //   in BuildResearchChoices (the retired BuildResearchBrowse line
                //   at ManageScreenVM:1546) - the Researched arm vanishes.
                // -------------------------------------------------------------
                var owned = Find(choices, OwnedBuilding, OwnedPerk, "all-four-states", failures);
                var busy = Find(choices, BusyBuilding, BusyPerk, "all-four-states", failures);
                var open = Find(choices, OpenBuilding, OpenPerk, "all-four-states", failures);
                var locked = Find(choices, LockedBuilding, LockedPerk, "all-four-states", failures);

                RequireState(owned, "Researched", OwnedBuilding + ":" + OwnedPerk,
                             "it is in OwnedBuildingPerks. The old list emitted NO ROW for an owned perk " +
                             "(ManageScreenVM:1546) - ruling 3.7 makes the whole tree visible.", failures);
                RequireState(busy, "Researching", BusyBuilding + ":" + BusyPerk,
                             "a BuildingResearch job for it is ACTIVE on the Research channel. The old list " +
                             "emitted no row for an in-progress perk (ManageScreenVM:1570).", failures);
                RequireState(open, "Available", OpenBuilding + ":" + OpenPerk,
                             "it is a tier-1 perk on a tier-1 building at Village Tier 2.", failures);
                RequireState(locked, "Locked", LockedBuilding + ":" + LockedPerk,
                             "it is a tier-3 perk on a tier-1 building.", failures);

                if (owned != null && owned.Activate != null)
                    failures.Add("[all-four-states] the RESEARCHED perk still carries an Activate - a card for work " +
                                 "already done must offer no CTA (ruling 3.7).");
                if (locked != null && !locked.Locked)
                    failures.Add("[all-four-states] the LOCKED perk reports Locked=false, so the card's lock treatment " +
                                 "([research-locked-visible]) can never fire.");
                if (open != null && !string.Equals(open.CtaLabel, "RESEARCH", StringComparison.Ordinal))
                    failures.Add("[all-four-states] an Available perk's CtaLabel is '" + open.CtaLabel +
                                 "', not \"RESEARCH\".");

                // -------------------------------------------------------------
                // CASE 3  [locked-reason-verbatim]
                // REVERT RECIPE (RED): assign `LockReason = "Locked."` in
                //   BuildResearchChoices instead of CanResearch's out reason.
                // -------------------------------------------------------------
                if (locked != null)
                {
                    BuildingPerkService.CanResearch(LockedBuilding, LockedPerk, out string authoritative);
                    if (string.IsNullOrWhiteSpace(authoritative))
                        failures.Add("[locked-reason-verbatim] BuildingPerkService.CanResearch returned no reason for " +
                                     "the locked fixture perk, so this case has nothing to compare against and could " +
                                     "not have been exercised. FAIL, not a skip.");
                    else if (!string.Equals(locked.LockReason, authoritative, StringComparison.Ordinal))
                        failures.Add("[locked-reason-verbatim] LockReason='" + locked.LockReason + "' but " +
                                     "CanResearch says '" + authoritative + "'. That sentence is the ONE line that " +
                                     "teaches the loop (WO-1390, owner on the Seeker: \"show locked with a link to " +
                                     "upgrade the prerequisite\") - a generic \"Locked.\" teaches nothing.");
                    if (string.IsNullOrWhiteSpace(locked.CtaLabel) ||
                        (locked.CtaLabel.IndexOf("UPGRADE", StringComparison.Ordinal) < 0))
                        failures.Add("[locked-reason-verbatim] the locked perk's CtaLabel is '" + locked.CtaLabel +
                                     "', not an \"UPGRADE ...\" door. A locked card must be a DOOR to the " +
                                     "prerequisite, never a dead button.");
                    if (locked.Activate == null)
                        failures.Add("[locked-reason-verbatim] the locked perk has a null Activate - the door does " +
                                     "not open.");
                }

                // -------------------------------------------------------------
                // CASE 5  [no-level-zero]  (live half; source half below)
                // REVERT RECIPE (RED): assign `TierText = "LEVEL " + UnlockTier`.
                // -------------------------------------------------------------
                for (int i = 0; i < choices.Count; i++)
                {
                    var c = choices[i];
                    if (c == null) { failures.Add("[no-level-zero] null Research choice at index " + i); continue; }
                    if (string.IsNullOrWhiteSpace(c.TierText) ||
                        !c.TierText.StartsWith("TIER ", StringComparison.Ordinal))
                        failures.Add("[no-level-zero] '" + c.BuildingId + ":" + c.PerkId + "' TierText='" + c.TierText +
                                     "'. Research has NO LEVEL; the card's level slot carries the owning building's " +
                                     "tier requirement, \"TIER n\" (ruling 3.7). Never paint LEVEL 0.");
                }

                // -------------------------------------------------------------
                // CASE 6  [gold-cost-parts]
                // REVERT RECIPE (RED): drop CostParts and format the price by hand
                //   (`price + " gold"`) the way the retired BuildResearchBrowse did.
                // -------------------------------------------------------------
                if (open != null)
                {
                    var perk = BuildingTierCatalog.FindPerk(OpenBuilding, OpenPerk);
                    if (perk == null)
                    {
                        failures.Add("[gold-cost-parts] building-tiers.json no longer authors '" + OpenBuilding + ":" +
                                     OpenPerk + "', so the authored price could not be read and this case could not " +
                                     "have been exercised. FAIL, not a skip.");
                    }
                    else
                    {
                        bool found = false;
                        var parts = open.CostParts;
                        for (int i = 0; parts != null && i < parts.Count; i++)
                            if (string.Equals(parts[i].ConceptId, "gold", StringComparison.Ordinal))
                            {
                                found = true;
                                if (parts[i].Amount != perk.GoldCost)
                                    failures.Add("[gold-cost-parts] the gold CostPart reads " + parts[i].Amount +
                                                 " but building-tiers.json authors " + perk.GoldCost + " for '" +
                                                 OpenBuilding + ":" + OpenPerk + "'. The card would lie about a price.");
                            }
                        if (!found)
                            failures.Add("[gold-cost-parts] an Available perk carries no CostPart with ConceptId " +
                                         "\"gold\" (" + (parts == null ? "null" : parts.Count + " part(s)") + "). " +
                                         "ResourceCost has no gold field (RepoProps.cs:21-30), which is exactly why " +
                                         "this must run through CostFormat.Parts with an explicit gold part - not a " +
                                         "second private wording for a price.");
                    }
                }

                // -------------------------------------------------------------
                // CASE 7  [no-dash-labels]
                // REVERT RECIPE (RED): set Name to `buildingName + " - " + perk.Name`
                //   (the retired BuildResearchBrowse label, which the capture shows
                //   as "Lumber Mill - Improved Logging").
                // -------------------------------------------------------------
                for (int i = 0; i < choices.Count; i++)
                {
                    var c = choices[i];
                    if (c == null) continue;
                    if ((c.Name ?? "").Contains(" - "))
                        failures.Add("[no-dash-labels] Research row name '" + c.Name + "' still glues the building to " +
                                     "the perk with \" - \". Ruling 3.6: the perk is the NAME and the building is the " +
                                     "SUB-LINE.");
                    if (string.IsNullOrWhiteSpace(c.BuildingName))
                        failures.Add("[no-dash-labels] '" + c.BuildingId + ":" + c.PerkId + "' has no BuildingName, so " +
                                     "the sub-line that replaces the dash does not exist and the ban above would pass " +
                                     "on a row that simply lost its owner.");
                }
            }
            finally
            {
                SetGssInstance(priorGss);
                InstallQueueInstance(priorQueue);
                if (gssHost != null) UnityEngine.Object.DestroyImmediate(gssHost);
                if (queueHost != null) UnityEngine.Object.DestroyImmediate(queueHost);
                if (fixture != null) UnityEngine.Object.DestroyImmediate(fixture);
            }
        }

        // =====================================================================
        //  SOURCE half.
        // =====================================================================
        private static void CheckPanelSource(List<string> failures, StringBuilder log)
        {
            string panel = ReadSource(PanelPath, failures);
            if (panel == null) return;

            string card = MethodBody(panel, "private void BuildResearchCard(");
            if (card == null)
            {
                failures.Add("[research-locked-visible] BuildResearchCard was not found in " + PanelPath +
                             " - the Research card does not exist, so neither the migrated lock treatment nor the " +
                             "TIER slot could be checked. FAIL, not a skip.");
            }
            else
            {
                // -------------------------------------------------------------
                // CASE 4  [research-locked-visible]   ** the MIGRATED case **
                // Was ManageProgressiveDisclosureRegression:90-94, scoped to
                // BuildBrowseRowContent - which ruling 3.4 DELETES. The lock
                // treatment moves to the card, so the pin moves with it (WO-1159
                // precedent, CLAUDE.md section 15).
                // REVERT RECIPE (RED): paint the locked card exactly like an
                //   available one - delete the `.Locked` branch and the
                //   BuildLockBadge( call from BuildResearchCard.
                // -------------------------------------------------------------
                // WO section 5.4 words this pin as `choice.Locked`. The parameter
                // NAME is tolerated as either `choice` or `selected` because the
                // Buildings precedent this card mirrors uses `choice` in the RAIL
                // row (Panel:1878) and `selected` in the CARD (Panel:1975); pinning
                // one spelling would go RED on correct code. The FIELD and the
                // BADGE - which is what the ruling is about - are pinned exactly.
                if (!card.Contains("choice.Locked") && !card.Contains("selected.Locked"))
                    failures.Add("[research-locked-visible] BuildResearchCard never reads the choice's .Locked flag, " +
                                 "so a tier-locked perk paints identically to an available one and the player has no " +
                                 "way to tell the card is a door to a prerequisite.");
                if (!card.Contains("BuildLockBadge("))
                    failures.Add("[research-locked-visible] BuildResearchCard does not seat BuildLockBadge on a locked " +
                                 "perk. Manage's rule since WO-1390 is the Troops rule: a locked choice wears the " +
                                 "badge and states its reason in WORDS (the owner is red/green colourblind).");
                if (!card.Contains("LockReason"))
                    failures.Add("[research-locked-visible] BuildResearchCard never paints LockReason, so the " +
                                 "CanResearch sentence the VM carries verbatim never reaches the screen.");

                // -------------------------------------------------------------
                // CASE 5  [no-level-zero]  (source half)
                // REVERT RECIPE (RED): copy the Buildings line
                //   `"LEVEL " + selected.Level` into BuildResearchCard.
                // -------------------------------------------------------------
                // Scanned with LINE COMMENTS STRIPPED. BuildResearchCard's own block comment
                // reads `TierText, never "LEVEL " + n`, and a raw Contains would fire on the
                // very comment that documents the rule - a pin that goes RED on correct code.
                if (StripLineComments(card).Contains("\"LEVEL "))
                    failures.Add("[no-level-zero] BuildResearchCard emits a \"LEVEL \" literal. Research has no level - " +
                                 "reusing the Buildings line paints \"LEVEL 0\" on every card (ruling 3.7).");
                if (!card.Contains("TierText"))
                    failures.Add("[no-level-zero] BuildResearchCard never paints TierText, so the slot the tier " +
                                 "requirement was supposed to occupy is empty and the LEVEL ban above passes on a " +
                                 "card that says nothing at all.");
            }

            // -------------------------------------------------------------
            // CASE 8  [perk-icon-path]  ** the stale-doc case **
            // REVERT RECIPE (RED): change ResearchSprite's key to
            //   "HudItems/BuildingUpgrades/" - the path named by
            //   BuildingTierCatalog.cs:41's doc comment, which points at a folder
            //   that has never existed.
            // -------------------------------------------------------------
            string sprite = MethodBody(panel, "private static Sprite ResearchSprite(");
            if (sprite == null)
            {
                failures.Add("[perk-icon-path] ResearchSprite was not found in " + PanelPath +
                             " - the Research card has no art loader at all, so the path could not be checked. " +
                             "FAIL, not a skip.");
            }
            else
            {
                if (!sprite.Contains("HudIcons/BuildingUpgrades/"))
                    failures.Add("[perk-icon-path] ResearchSprite does not load from \"HudIcons/BuildingUpgrades/\". " +
                                 "That is the path the LIVE loader uses (BuildingUpgradePanelMvvm.cs:2025) and the " +
                                 "only one with art behind it.");
                if (sprite.Contains("HudItems/BuildingUpgrades"))
                    failures.Add("[perk-icon-path] ResearchSprite loads from \"HudItems/BuildingUpgrades\" - the path " +
                                 "named by BuildingPerkDef.IconId's doc comment (BuildingTierCatalog.cs:41). THAT " +
                                 "FOLDER DOES NOT EXIST; every perk card would paint nothing, with no error. The " +
                                 "comment is the bug (CLAUDE.md section 11B: a path copied from a doc is hearsay).");
            }

            log.AppendLine("source: card lock treatment / TIER slot / sprite path checks complete");
        }

        // =====================================================================
        //  Every authored IconId must have a file behind it. This is the half
        //  that PROVES the doc comment wrong rather than merely asserting it.
        // =====================================================================
        private static void CheckPerkIconCoverage(List<string> failures, StringBuilder log)
        {
            // REVERT RECIPE (RED): delete any perk icon under
            //   Assets/Resources/HudIcons/BuildingUpgrades/ or its .meta.
            string stale = Path.Combine(Directory.GetCurrentDirectory(),
                "Assets/Resources/HudItems/BuildingUpgrades".Replace('/', Path.DirectorySeparatorChar));
            if (Directory.Exists(stale))
                failures.Add("[perk-icon-path] Assets/Resources/HudItems/BuildingUpgrades now EXISTS. This suite was " +
                             "written because it did not, and BuildingTierCatalog.cs:41's comment named it. If art " +
                             "genuinely moved there, re-point ResearchSprite and this case together - do not have two " +
                             "folders.");

            var all = BuildingTierCatalog.All;
            if (all == null || all.Count == 0)
            {
                failures.Add("[perk-icon-path] BuildingTierCatalog.All is empty, so no authored IconId could be " +
                             "checked against disk. FAIL, not a skip.");
                return;
            }

            int checkedCount = 0;
            for (int i = 0; i < all.Count; i++)
            {
                var def = all[i];
                if (def?.Tiers == null) continue;
                for (int t = 0; t < def.Tiers.Count; t++)
                {
                    var tier = def.Tiers[t];
                    if (tier?.Perks == null) continue;
                    for (int p = 0; p < tier.Perks.Count; p++)
                    {
                        var perk = tier.Perks[p];
                        if (perk == null || string.IsNullOrEmpty(perk.Id)) continue;
                        // IconId defaults to the perk id when unauthored - the same
                        // rule BuildingUpgradePanelMvvm applies.
                        string icon = string.IsNullOrEmpty(perk.IconId) ? perk.Id : perk.IconId;
                        checkedCount++;
                        if (!IconExists(icon))
                            failures.Add("[perk-icon-path] no " + IconRoot + "/" + icon + ".{jpg,png} (+ .meta) for " +
                                         "perk '" + def.Id + ":" + perk.Id + "'. ResearchSprite is pinned to that " +
                                         "folder, so this card would paint nothing.");
                    }
                }
            }

            log.AppendLine("perk icon coverage=" + checkedCount + " authored IconId(s) verified under " + IconRoot);
            if (checkedCount == 0)
                failures.Add("[perk-icon-path] zero authored perks were walked, so the coverage check above asserted " +
                             "nothing. FAIL, not a skip.");
        }

        private static bool IconExists(string iconName)
        {
            string[] extensions = { ".jpg", ".png", ".jpeg" };
            for (int i = 0; i < extensions.Length; i++)
            {
                string relative = IconRoot + "/" + iconName + extensions[i];
                string full = Path.Combine(Directory.GetCurrentDirectory(),
                    relative.Replace('/', Path.DirectorySeparatorChar));
                if (File.Exists(full) && File.Exists(full + ".meta")) return true;
            }
            return false;
        }

        // =====================================================================
        //  Helpers.
        // =====================================================================
        private static int CountAuthoredPerks()
        {
            var all = BuildingTierCatalog.All;
            if (all == null) return -1;
            int total = 0;
            for (int i = 0; i < all.Count; i++)
            {
                var def = all[i];
                if (def?.Tiers == null) continue;
                if (Array.IndexOf(PerkBuildings, def.Id) < 0) continue;
                for (int t = 0; t < def.Tiers.Count; t++)
                {
                    var tier = def.Tiers[t];
                    if (tier?.Perks != null) total += tier.Perks.Count;
                }
            }
            return total;
        }

        private static ResearchChoiceVM Find(IReadOnlyList<ResearchChoiceVM> choices, string buildingId, string perkId,
                                             string caseName, List<string> failures)
        {
            if (choices != null)
                for (int i = 0; i < choices.Count; i++)
                {
                    var c = choices[i];
                    if (c != null &&
                        string.Equals(c.BuildingId, buildingId, StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(c.PerkId, perkId, StringComparison.OrdinalIgnoreCase))
                        return c;
                }
            failures.Add("[" + caseName + "] the fixture perk '" + buildingId + ":" + perkId + "' produced NO choice. " +
                         "Ruling 3.7 shows the WHOLE tree - a state that emits no row is the defect this case exists " +
                         "to catch, and it also means every assertion about that state was skipped.");
            return null;
        }

        private static void RequireState(ResearchChoiceVM choice, string expected, string label, string why,
                                         List<string> failures)
        {
            if (choice == null) return;   // Find() already named it
            if (!string.Equals(choice.StateWord, expected, StringComparison.Ordinal))
                failures.Add("[all-four-states] '" + label + "' reports StateWord='" + choice.StateWord + "' but must " +
                             "read \"" + expected + "\" - " + why + " The state WORD is the only carrier of state " +
                             "(the owner is red/green colourblind; hue is decoration).");
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

        /// <summary>Mirrors ManageTroopsTrainDoorRegression:370-382.</summary>
        private static bool InstallQueueInstance(BuildTimerService svc)
        {
            var t = typeof(BuildTimerService);
            var prop = t.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static);
            if (prop != null && prop.GetSetMethod(true) != null)
            {
                prop.GetSetMethod(true).Invoke(null, new object[] { svc });
                return ReferenceEquals(BuildTimerService.Instance, svc);
            }
            var f = t.GetField("<Instance>k__BackingField", BindingFlags.NonPublic | BindingFlags.Static)
                    ?? t.GetField("_instance", BindingFlags.NonPublic | BindingFlags.Static);
            if (f == null) return false;
            f.SetValue(null, svc);
            return ReferenceEquals(BuildTimerService.Instance, svc);
        }

        private static string ReadSource(string relativePath, List<string> failures)
        {
            string full = Path.Combine(Directory.GetCurrentDirectory(), relativePath.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(full)) return File.ReadAllText(full);
            failures.Add("source file missing: " + relativePath);
            return null;
        }

        /// <summary>Everything from "//" to end of line removed, line by line. Deliberately
        /// naive - it is used only to keep a banned LITERAL from matching the comment that
        /// documents why the literal is banned, and this file's own commentary is the only
        /// place that collision arises.</summary>
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

        /// <summary>The text of one method, from its signature to the next member
        /// declaration at type indentation. See ManageDefenseCardRegression for why
        /// this terminates on indentation rather than a NAMED next method.</summary>
        private static string MethodBody(string source, string signature)
        {
            if (source == null) return null;
            int start = source.IndexOf(signature, StringComparison.Ordinal);
            if (start < 0) return null;
            int end = source.IndexOf("\n        private ", start + signature.Length, StringComparison.Ordinal);
            if (end < 0) end = source.IndexOf("\n        public ", start + signature.Length, StringComparison.Ordinal);
            return end < 0 ? source.Substring(start) : source.Substring(start, end - start);
        }
    }
}

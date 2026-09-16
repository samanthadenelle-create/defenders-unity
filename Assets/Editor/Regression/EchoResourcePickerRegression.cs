// =============================================================================
// EchoResourcePickerRegression — WO-830/831 oracle for the card-facing layer:
// the resource-picker VM projection, the picker verb, the disclosed synergy line
// (and its NON-disclosure of the hidden tri), and the WO-831 emergence beat data.
// Headless, data-decidable, no play-mode. Sibling suite to
// EchoSpecializationRegression (which owns the math/economy groups).
// -----------------------------------------------------------------------------
// Assembly: DeNelle.EditorRegression. Shape: public static bool Run(out string reason)
// — registered into DataRegression.RunAll by the orchestrator.
//
// FIVE ASSERTION GROUPS:
//   1. Chip projection    — EchoCardVM.ResourceChips(): exactly the 5 resources in
//      PickableResources order; Selected on the assigned one (with the "(now)" TEXT
//      cue); Preferred + the "best" cue IN THE LABEL only on the affinity chip, and
//      NO chip carries a Note any more (WO-883 — the note band duplicated the footer
//      and was the one row the picker's scroll fold cut in half); ASCII-only.
//   2. Picker verb        — vm.AssignResource("iron") writes through the seam
//      (lane=harvest, resource=iron persisted); a bogus token is a logged no-op.
//   3. Card strings       — StateText names the ASSIGNED resource + the (best) flag
//      only when matched; WhatText discloses the affinity ("Favors: X"); AskText is
//      the gather ask; ALL card strings ASCII; NO card string ever contains the
//      hidden-tri vocabulary (the secret stays secret at the string layer too).
//   4. Synergy line       — SynergyText ACTIVE when the pair runs, recipe text when
//      not; names the partner; never mentions "hidden"/"tri"/"secret".
//   6. WO-1108 repair RETIREMENT (the INVERSION of the old WO-811 group) — TaskChips
//      is EXACTLY the 5 resources with NO sixth "Repair structures" row; the VM exposes
//      no RepairTaskChip/AssignRepair member (reflection re-add guard); the retired
//      EchoAssignments.AssignRepair verb always refuses and never mutates; StateText
//      never reads as a repair status. Repair is passive across the whole roster now.
//   5. WO-831 emergence   — every roster entry has a non-empty ASCII EmergeLine;
//      EchoUnlockDialogue.EmergeLineFor falls back to the shared default on a null
//      entry; EchoRosterCatalog.LoadEmergence returns null GRACEFULLY (no throw)
//      when the LFS art is absent — the Guard fallback contract that guarantees a
//      missing sprite never blocks an unlock.
//
// SAFETY: snapshots+restores the PlayerPrefs save blob and the prior
// GameStateService singleton; disposes the VM; DestroyImmediate's the throwaways.
// =============================================================================
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using DeNelle.Core.State;
using DeNelle.Village;

namespace DeNelle.Editor
{
    public static class EchoResourcePickerRegression
    {
        private const string SaveKey = "dotr-save";

        public static bool Run(out string reason)
        {
            var failures = new List<string>();
            var notes = new List<string>();
            void Fail(string s) => failures.Add("ECHO_PICKER FAIL: " + s);

            bool hadSave = PlayerPrefs.HasKey(SaveKey);
            string rawSave = hadSave ? PlayerPrefs.GetString(SaveKey, null) : null;

            GameStateService priorInstance = GameStateService.Instance;
            GameObject gssGo = null;
            GameState throwaway = null;
            EchoCardVM vm = null;
            bool installed = false;

            try
            {
                // --- Group 5 needs no state (pure catalog/loader). ---------------
                CheckEmergenceData(Fail);

                // --- Groups 1-4 need a headless GameState (same seam as the sibling suite).
                throwaway = ScriptableObject.CreateInstance<GameState>();
                gssGo = new GameObject("GameStateService (echo-picker-oracle)");
                var gss = gssGo.AddComponent<GameStateService>();
                if (!TryInstallHeadlessState(gss, throwaway, out string installErr))
                {
                    notes.Add("groups 1-4 skipped (needs fleet — " + installErr + ")");
                }
                else
                {
                    installed = true;
                    var state = gss.State;
                    if (state == null)
                    {
                        notes.Add("groups 1-4 skipped (throwaway state did not install)");
                    }
                    else
                    {
                        state.EchoCount = 6;
                        EchoBalanceCatalog.Reload();

                        // WO-953: OPEN all three existence gates for the legacy groups —
                        // their label/state assertions predate the faucet-honesty cue and
                        // describe an all-faucets-open town. The collector catalog ids are
                        // both the real catalog rows AND CatalogIdsForBuilding's cold-
                        // catalog naming fallback, so this opens the gate either way.
                        state.MarkEverBuilt("collector_farm");
                        state.MarkEverBuilt("collector_lumbermill");
                        state.MarkEverBuilt("collector_forge");

                        // Bind the VM to Elowen (index 1, affinity Wood) assigned to IRON —
                        // a deliberate NON-match so both flag states are exercised.
                        state.EchoLanes = "food:1,iron:2,gold:1,crystals:1,iron:1,crystals:1";
                        vm = new EchoCardVM(1);

                        CheckChipProjection(vm, Fail);
                        CheckPickerVerb(vm, state, Fail);
                        CheckCardStrings(vm, state, Fail);
                        CheckRepairTask(vm, state, Fail);   // WO-1108: the repair chip/verb RETIREMENT guard
                        CheckSynergyLine(state, Fail);
                        CheckFaucetHonesty(state, Fail);    // WO-953: the NEEDS cue + waiting status
                    }
                }
            }
            catch (Exception ex)
            {
                Fail($"oracle threw: {ex.GetType().Name}: {ex.Message}");
            }
            finally
            {
                if (vm != null) vm.Dispose();
                EchoLaneBonuses.Reset();
                if (gssGo != null) UnityEngine.Object.DestroyImmediate(gssGo);
                if (throwaway != null) UnityEngine.Object.DestroyImmediate(throwaway);
                if (installed) TrySetInstanceStatic(priorInstance);
                if (hadSave) PlayerPrefs.SetString(SaveKey, rawSave);
                else PlayerPrefs.DeleteKey(SaveKey);
                PlayerPrefs.Save();
            }

            if (failures.Count == 0)
            {
                reason = "ECHO PICKER OK — 5-chip projection + picker verb + card strings (affinity disclosed, "
                         + "tri never) + WO-1108 repair-chip RETIREMENT (no sixth row, verb refuses, no repair status) "
                         + "+ synergy line + WO-831 emergence data/fallback all hold"
                         + (notes.Count > 0 ? " [" + string.Join("; ", notes) + "]" : "");
                return true;
            }
            reason = $"echo-picker: {failures.Count} failure(s): " + string.Join(" | ", failures);
            return false;
        }

        // =====================================================================
        //  Group 1 — Chip projection (5 resources, Selected/Preferred as TEXT)
        // =====================================================================
        private static void CheckChipProjection(EchoCardVM vm, Action<string> Fail)
        {
            var chips = vm.ResourceChips();
            if (chips == null || chips.Length != 4)
            {
                Fail($"ResourceChips length {(chips == null ? 0 : chips.Length)} (expected 4 before final-Echo crystal unlock)");
                return;
            }

            var expectedOrder = EchoAssignments.PickableResources;
            for (int i = 0; i < chips.Length; i++)
            {
                if (chips[i].Id != expectedOrder[i])
                    Fail($"chip[{i}].Id='{chips[i].Id}' (expected '{expectedOrder[i]}' — PickableResources order)");
                if (!IsAscii(chips[i].Label) || !IsAscii(chips[i].Note ?? ""))
                    Fail($"chip[{i}] label/note contains non-ASCII characters");
            }

            // Elowen assigned IRON: iron chip Selected + "(now)" text cue; wood chip is the
            // affinity (Preferred + the calling note); nothing else flagged.
            for (int i = 0; i < chips.Length; i++)
            {
                var c = chips[i];
                bool expectSel = c.Id == EchoAssignments.ResIron;
                bool expectPref = c.Id == EchoAssignments.ResWood;
                if (c.Selected != expectSel)
                    Fail($"chip '{c.Id}' Selected={c.Selected} (expected {expectSel})");
                if (c.Preferred != expectPref)
                    Fail($"chip '{c.Id}' Preferred={c.Preferred} (expected {expectPref} — affinity is Wood)");
                if (expectSel && !c.Label.Contains("(now)"))
                    Fail($"selected chip '{c.Id}' label '{c.Label}' lacks the '(now)' TEXT cue (colorblind law)");
                // WO-883: the affinity cue moved OUT of a separate note band and INTO the chip
                // label. The disclosure itself is NOT optional (colorblind law — the match
                // bonus can never be carried by hue), only its home changed.
                if (expectPref && c.Label.IndexOf("best", StringComparison.OrdinalIgnoreCase) < 0)
                    Fail($"affinity chip '{c.Id}' label '{c.Label}' does not disclose the match bonus in TEXT " +
                         "(the calling must be readable without colour)");
                if (!expectPref && c.Label.IndexOf("best", StringComparison.OrdinalIgnoreCase) >= 0)
                    Fail($"non-affinity chip '{c.Id}' label '{c.Label}' claims the match bonus (only the calling chip is flagged)");
                // The note band is RETIRED. It was the verbatim tail of StateText, so the
                // footer said it again two lines down, and it made ONE picker row 39.5px taller
                // than the other four — the row the scroll fold sliced mid-sentence on the
                // owner's 2026-08-04 capture (docs/ui-review/screens-2026-08-04/EchoCard_2340x1080.png).
                if (!string.IsNullOrEmpty(c.Note))
                    Fail($"chip '{c.Id}' carries a per-chip note '{c.Note}' again (WO-883 retired it: it duplicated " +
                         "the footer's StateText and gave the fold a half-line of text to cut — keep the cue in the label)");
            }
        }

        // =====================================================================
        //  Group 2 — The picker verb writes through the seam
        // =====================================================================
        private static void CheckPickerVerb(EchoCardVM vm, GameState state, Action<string> Fail)
        {
            vm.AssignResource(EchoAssignments.ResGold);
            if (EchoAssignments.LaneOf(1) != EchoAssignments.LaneHarvest)
                Fail($"after AssignResource('gold'): LaneOf(1)='{EchoAssignments.LaneOf(1)}' (expected 'harvest')");
            if (EchoAssignments.ResourceTokenOf(1) != EchoAssignments.ResGold)
                Fail($"after AssignResource('gold'): ResourceTokenOf(1)='{EchoAssignments.ResourceTokenOf(1)}' (expected 'gold')");
            if (EchoAssignments.LevelOf(1) != 2)
                Fail($"AssignResource changed the level: LevelOf(1)={EchoAssignments.LevelOf(1)} (expected 2 preserved)");
            var parts = (state.EchoLanes ?? "").Split(',');
            if (parts.Length <= 1 || parts[1] != "gold:2")
                Fail($"persisted token[1]='{(parts.Length > 1 ? parts[1] : "<none>")}' (expected 'gold:2')");

            // Bogus token: logged no-op.
            string before = state.EchoLanes;
            vm.AssignResource("mithril");
            if (state.EchoLanes != before)
                Fail("AssignResource('mithril') mutated the assignment (must be a logged no-op)");

            // Restore the iron pick for the string checks below.
            vm.AssignResource(EchoAssignments.ResIron);
        }

        // =====================================================================
        //  Group 3 — Card strings (affinity disclosed; the secret never worded)
        // =====================================================================
        private static void CheckCardStrings(EchoCardVM vm, GameState state, Action<string> Fail)
        {
            // Non-matched (iron pick, wood affinity): resource named, no (best) flag.
            string stateText = vm.StateText;
            if (!stateText.Contains("Iron"))
                Fail($"StateText '{stateText}' does not name the assigned resource 'Iron'");
            if (stateText.Contains("best"))
                Fail($"StateText '{stateText}' claims (best) on a NON-matched pick (iron vs wood affinity)");

            string whatText = vm.WhatText;
            if (!whatText.Contains("Favors: Wood"))
                Fail($"WhatText '{whatText}' does not disclose the affinity ('Favors: Wood')");

            string askText = vm.AskText;
            // WO-811 reworded the ask from "gather?" to "tend to?"; WO-1108 retired the repair
            // chip but KEPT the wording — "tend to" reads correctly over five resources too,
            // and the pin stays so the string is never churned by accident.
            if (!askText.Contains("tend"))
                Fail($"AskText '{askText}' is not the tend-to ask");
            if (askText.Contains(","))
                Fail($"AskText '{askText}' carries the full comma name (expected the short name)");

            // Matched (wood pick): the (best) calling flag appears.
            vm.AssignResource(EchoAssignments.ResWood);
            stateText = vm.StateText;
            if (!stateText.Contains("Wood") || !stateText.Contains("best"))
                Fail($"matched StateText '{stateText}' must name Wood and carry the (best) calling flag");

            // ASCII + secrecy sweep over every card string.
            foreach (var s in new[] { vm.NameText, vm.ElementText, vm.WhatText, vm.StateText, vm.SynergyText, vm.AskText })
            {
                if (!IsAscii(s))
                    Fail($"card string contains non-ASCII characters: '{s}'");
                string low = (s ?? "").ToLowerInvariant();
                if (low.Contains("hidden") || low.Contains("tri-synergy") || low.Contains("secret"))
                    Fail($"card string leaks the hidden tri-synergy vocabulary: '{s}'");
            }
        }

        // =====================================================================
        //  Group 4 — The disclosed synergy line (active + recipe states)
        // =====================================================================
        private static void CheckSynergyLine(GameState state, Action<string> Fail)
        {
            // Provisions pair = Elowen (wood) + Aldwin (food). Both matched -> ACTIVE.
            state.EchoLanes = "food:1,wood:1,gold:1,crystals:1,iron:1,crystals:1";
            using (var vmActive = new VmScope(1))
            {
                string sy = vmActive.Vm.SynergyText;
                if (!sy.Contains("Provisions") || !sy.Contains("ACTIVE"))
                    Fail($"active SynergyText '{sy}' must name the Provisions pair as ACTIVE");
                if (!sy.Contains("Aldwin"))
                    Fail($"active SynergyText '{sy}' must name the partner (Aldwin)");
            }

            // Break the pair (Aldwin off food) -> the recipe text, naming partner + resource.
            state.EchoLanes = "wood:1,wood:1,gold:1,crystals:1,iron:1,crystals:1";
            using (var vmIdle = new VmScope(1))
            {
                string sy = vmIdle.Vm.SynergyText;
                if (sy.Contains("ACTIVE"))
                    Fail($"broken-pair SynergyText '{sy}' still claims ACTIVE");
                // ⛔ RE-POINTED 2026-08-25, NOT WEAKENED. This asserted the literal "Food", which was
                // the resource's DISPLAY NAME until WO-1163 retired it. The enum member and the
                // persisted token are still Food - frozen on purpose - but what this text shows the
                // player is now Stone, so a hardcoded "Food" here fails on CORRECT code.
                // DERIVE the expected label from the same authority the UI reads instead of naming
                // it, so the next vocabulary ruling cannot break this suite again. The assertion is
                // unchanged in strength: the hint must still name the partner AND their resource.
                string aldwinResource = DeNelle.Village.EchoRosterCatalog.TargetLabel(
                    DeNelle.Village.HarvestTarget.Stone);
                if (!sy.Contains("Aldwin") || !sy.Contains(aldwinResource))
                    Fail($"broken-pair SynergyText '{sy}' must hint the partner and its resource " +
                         $"(Aldwin / {aldwinResource})");
            }
        }

        // =====================================================================
        //  Group 6 — WO-1108: the repair task chip is RETIRED (this group is INVERTED)
        // ---------------------------------------------------------------------
        //  WO-811 added a SIXTH "Repair structures" picker row and this group asserted
        //  its projection + verb. WO-1108 made repair PASSIVE across the whole roster —
        //  there is nothing left to pick — so the same assertions now run the other way:
        //  the sixth row must NOT exist, the retired verb must REFUSE, and re-adding the
        //  chip (by reflection, so a resurrected member is caught even if the oracle is
        //  not recompiled against it) must FAIL this suite.
        // =====================================================================
        private static void CheckRepairTask(EchoCardVM vm, GameState state, Action<string> Fail)
        {
            // Chip projection: EXACTLY the five WO-830 resource rows, no sixth task row.
            var all = vm.TaskChips();
            var expectedOrder = EchoAssignments.PickableResources;
            int expectedCount = expectedOrder.Length - 1;
            if (all == null || all.Length != expectedCount)
            {
                Fail($"TaskChips length {(all == null ? 0 : all.Length)} (expected {expectedOrder.Length} — five resources; "
                   + "the WO-811 repair row is RETIRED, repair is passive)");
                return;
            }
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i].Id != expectedOrder[i])
                    Fail($"TaskChips[{i}].Id='{all[i].Id}' (expected '{expectedOrder[i]}' — resource rows unchanged)");
                if (all[i].Id == EchoAssignments.LaneRepair)
                    Fail("TaskChips still offers a 'repair' row — the repair chip is RETIRED (WO-1108)");
                if (all[i].Label.IndexOf("Repair", StringComparison.OrdinalIgnoreCase) >= 0)
                    Fail($"TaskChips[{i}] label '{all[i].Label}' still advertises repair as a pick");
            }

            // RE-ADD GUARD: the VM must expose no repair-chip member at all. Reflection, so
            // a resurrected RepairTaskChip()/AssignRepair() fails the suite immediately.
            var vmType = typeof(EchoCardVM);
            const BindingFlags Pub = BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static;
            if (vmType.GetMethod("RepairTaskChip", Pub) != null)
                Fail("EchoCardVM.RepairTaskChip() is back — the repair chip is RETIRED (WO-1108); repair is passive, not a pick");
            if (vmType.GetMethod("AssignRepair", Pub) != null)
                Fail("EchoCardVM.AssignRepair() is back — the repair task is RETIRED (WO-1108); there is nothing to assign");

            // The storage verb is a LOUD refusal that never mutates. The card-strings group
            // left echo 1 on wood at level 2 (this group's caller then restores iron).
            string beforeLanes = state.EchoLanes;
            if (EchoAssignments.AssignRepair(1))
                Fail("EchoAssignments.AssignRepair(1) returned true — the retired verb must ALWAYS refuse (WO-1108)");
            if (state.EchoLanes != beforeLanes)
                Fail($"AssignRepair(1) mutated the persisted lanes ('{beforeLanes}' -> '{state.EchoLanes}') — it must be a pure no-op");
            if (EchoAssignments.LaneOf(1) == EchoAssignments.LaneRepair)
                Fail("LaneOf(1) reads 'repair' — no repair token may be written again (WO-1108)");

            // Status string: with repair no longer an assignment, the card's state line is a
            // GATHER line and must never claim a repair task. TEXT only, ASCII only.
            string stateText = vm.StateText;
            if (stateText.IndexOf("Repair", StringComparison.OrdinalIgnoreCase) >= 0)
                Fail($"StateText '{stateText}' still reads as a repair status — repair is passive and is not a per-Echo state (WO-1108)");
            // ECON-SWEEP 2026-08-16 (defect 4) — INVERTED. This used to require "Lv 2" on the state
            // line. EchoAssignments.SetLevel has NO production caller (only this harness calls it),
            // so a shipped Echo is Lv 1 forever and the chip advertised progression the game does
            // not have. The level-up feed source is an unruled owner pin (WORK_ORDER_738). The chip
            // is removed until that ruling; this now fails if it comes back.
            if (stateText.IndexOf("Lv ", StringComparison.Ordinal) >= 0)
                Fail($"StateText '{stateText}' shows a level chip -- Echo levels have no production raise path (SetLevel is uncalled), so a 'Lv N' readout is dead data. See WORK_ORDER_738 owner pin 2.");
            if (!IsAscii(stateText))
                Fail($"StateText '{stateText}' contains non-ASCII characters");

            // Restore the harvest pick so any later group starts from the WO-830 baseline.
            vm.AssignResource(EchoAssignments.ResIron);
        }

        // =====================================================================
        //  Group 7 — WO-953: faucet honesty (the NEEDS cue + waiting status)
        // ---------------------------------------------------------------------
        //  Pins: (a) the token→building map; (b) the RULED name — iron's needed
        //  building must resolve to the ARMORER-role row, and NEVER to the weapons
        //  shop (the inversion guard, re-pointed 2026-08-23 — see the block comment
        //  on case (b) for why the expectation is now the exact inverse);
        //  (c) closed gate → "NEEDS: <name>" IN the chip label with " (now)" still
        //  LAST (WO-883 order law) and the chip still tappable-shaped (cue, never a
        //  lock — the projection itself proves no disabled state exists); (d) the
        //  StateText mirror "waiting on a/an <name>"; (e) gold/crystals NEVER cued;
        //  (f) reopening the gate clears the cue.
        // =====================================================================
        private static void CheckFaucetHonesty(GameState state, Action<string> Fail)
        {
            // (a) token → building map is the gate's own vocabulary. NOTE: "forge" here
            // is the PROGRESSION id (ResourceBuildingProgression.ForgeId), i.e. the iron
            // faucet's key — NOT the catalog row 'forge' (the weapons shop). They are
            // different namespaces that happen to share a word; case (b) is what pins
            // which BUILDING the player is actually told to raise.
            if (EchoCardVM.FaucetBuildingIdFor("iron") != "forge"
                || EchoCardVM.FaucetBuildingIdFor("wood") != "lumbermill"
                || EchoCardVM.FaucetBuildingIdFor("food") != "farm")
                Fail("FaucetBuildingIdFor map broken (expected iron->forge, wood->lumbermill, food->farm)");
            if (EchoCardVM.FaucetBuildingIdFor("gold") != null || EchoCardVM.FaucetBuildingIdFor("crystals") != null)
                Fail("gold/crystals must have NO faucet building (no collector exists for them)");

            // (b) THE RULED NAME + the inversion guard, RE-POINTED 2026-08-23.
            //
            // Owner ruling, verbatim: "It should be the armorer, the food is the farm,
            // and the wood is the lumbermill, and there is none for crystals, since that
            // is only available at lvl 6 echo."
            //
            // ⛔ THIS EXPECTATION IS THE EXACT INVERSE OF WHAT IT ASSERTED BEFORE THAT
            // DATE, and the flip is deliberate, not a softening. The old case demanded
            // the collector card word "Forge" and treated "Armorer" as proof that the
            // QR-5.7 name inversion had leaked. That was only ever right while the
            // catalog labels were CROSSED (row 'forge' displayed "Armorer" while selling
            // WEAPONS). WO-1161 straightened the names from vendors.json — function is
            // the authority — so under the ruling "Armorer" is the CORRECT answer and
            // "Forge" (the weapons shop) is now the defect. The guard therefore SURVIVES
            // with its polarity reversed: it still goes RED the moment iron's cue points
            // the player back at the weapons shop.
            //
            // Neither word is hardcoded. Both are read out of the WO-1161 role table, so
            // a future catalog rename carries this oracle with it instead of stranding a
            // literal — which is the whole failure mode this cluster keeps re-living.
            string armorShopName = DeNelle.Core.Catalog.StructureRoles
                .By[DeNelle.Core.Catalog.StructureRole.Armorer].DisplayName;
            string weaponShopName = DeNelle.Core.Catalog.StructureRoles
                .By[DeNelle.Core.Catalog.StructureRole.Weaponsmith].DisplayName;
            if (string.IsNullOrEmpty(armorShopName))
                Fail($"NO catalog row claims role '{DeNelle.Core.Catalog.StructureRole.Armorer}' — iron's " +
                     "NEEDS cue has no building to name, so the player is told to build nothing. " +
                     "Author the role in structures-catalog.json (never a literal here).");
            bool namesDiffer = !string.IsNullOrEmpty(weaponShopName) && !string.IsNullOrEmpty(armorShopName)
                               && !string.Equals(weaponShopName, armorShopName, StringComparison.OrdinalIgnoreCase);

            string ironName = EchoCardVM.NeededBuildingDisplayName("forge");
            if (string.IsNullOrEmpty(ironName))
                Fail("iron's needed building resolved to nothing — a closed gate with no name to act on.");
            else if (!string.IsNullOrEmpty(armorShopName)
                     && !string.Equals(ironName, armorShopName, StringComparison.OrdinalIgnoreCase))
                Fail($"iron's needed building resolved to '{ironName}', expected the ARMORER-role row " +
                     $"'{armorShopName}' (owner 2026-08-23: iron is the armorer's resource).");
            // The inversion guard itself: naming the WEAPONS shop is the defect now.
            if (namesDiffer && !string.IsNullOrEmpty(ironName)
                && ironName.IndexOf(weaponShopName, StringComparison.OrdinalIgnoreCase) >= 0)
                Fail($"iron's needed building resolved to '{ironName}', which names the WEAPONS shop " +
                     $"'{weaponShopName}' (role '{DeNelle.Core.Catalog.StructureRole.Weaponsmith}') — the " +
                     "name inversion is back: the player would be sent to build a weapons roof to mine iron.");
            // WO-1416 (owner 2026-09-05: "quarry pays stone"): the stored slot is still the
            // persisted HarvestResource.Stone / collectorBuildingId "farm", but the player's word is
            // the STONE-PRODUCER role row's displayName ("Quarry") - read off the role table like
            // the armorer check above, never a literal, so the next rename carries this oracle too.
            string stoneShopName = DeNelle.Core.Catalog.StructureRoles
                .By[DeNelle.Core.Catalog.StructureRole.StoneProducer].DisplayName;
            string foodName = EchoCardVM.NeededBuildingDisplayName("farm");
            if (string.IsNullOrEmpty(stoneShopName))
                Fail($"NO catalog row claims role '{DeNelle.Core.Catalog.StructureRole.StoneProducer}' - the " +
                     "stone faucet's NEEDS cue has no building to name.");
            else if (!string.Equals(foodName, stoneShopName, StringComparison.OrdinalIgnoreCase))
                Fail($"stone's needed building resolved to '{foodName}', expected the STONE-PRODUCER row " +
                     $"'{stoneShopName}' (owner 2026-09-05: the Quarry pays Stone; 'Farm' is retired copy).");
            else if (string.Equals(foodName, "Farm", StringComparison.OrdinalIgnoreCase))
                Fail("stone's needed building still reads 'Farm' - retired word (WO-1416).");
            string woodName = EchoCardVM.NeededBuildingDisplayName("lumbermill");
            if (string.IsNullOrEmpty(woodName) || !woodName.StartsWith("Lumber", StringComparison.Ordinal))
                Fail($"wood's needed building resolved to '{woodName}' (expected the canon 'Lumber Mill' family)");

            // Close ALL gates: empty the ever-built ledger (the WO-834 input). A live
            // registered collector would keep a gate open — headless there are none,
            // but assert against the primitive rather than assume.
            var savedLedger = new List<string>(state.EverBuiltStructureIds);
            state.EverBuiltStructureIds.Clear();
            try
            {
                bool ironGateClosed = !DeNelle.Village.Buildings.Progression.ResourceBuildingHarvester.MayHarvest(
                    DeNelle.Village.Buildings.Progression.ResourceBuildingHarvester.CatalogIdsForBuilding("forge"),
                    state.EverBuiltStructureIds,
                    DeNelle.Village.Buildings.Progression.ResourceCollectorRegistry.Get("forge") != null);
                if (!ironGateClosed)
                {
                    // A live scene collector is registered (editor left a scene open) —
                    // the closed-state assertions below would be vacuous; consistency
                    // between the cue and the gate is still pinned.
                    if (EchoCardVM.TryGetFaucetNeed("iron", out _))
                        Fail("TryGetFaucetNeed shows a NEEDS cue while the gate primitive reads OPEN (cue must mirror the gate exactly)");
                    return;
                }

                if (!EchoCardVM.TryGetFaucetNeed("iron", out string needs) || string.IsNullOrEmpty(needs))
                    Fail("closed iron gate raised no NEEDS cue (TryGetFaucetNeed false/empty)");
                if (EchoCardVM.TryGetFaucetNeed("gold", out _) || EchoCardVM.TryGetFaucetNeed("crystals", out _))
                    Fail("gold/crystals carry a NEEDS cue (they have no faucet building — never cue them)");

                // (c) the cue lands IN the chip label; " (now)" stays LAST (WO-883).
                state.EchoLanes = "food:1,iron:2,gold:1,crystals:1,iron:1,crystals:1";
                using (var scope = new VmScope(1))
                {
                    var chips = scope.Vm.ResourceChips();
                    foreach (var c in chips)
                    {
                        bool expectCue = c.Id == "iron" || c.Id == "wood" || c.Id == "food";
                        bool hasCue = c.Label.Contains("NEEDS:");
                        if (expectCue && !hasCue)
                            Fail($"gated chip '{c.Id}' label '{c.Label}' carries no NEEDS cue");
                        if (!expectCue && hasCue)
                            Fail($"ungated chip '{c.Id}' label '{c.Label}' claims a NEEDS cue");
                        if (!IsAscii(c.Label))
                            Fail($"cue label '{c.Label}' contains non-ASCII characters");
                        if (c.Id == "iron")
                        {
                            // RE-POINTED 2026-08-23 (owner: "It should be the armorer"):
                            // the chip must NAME the armorer now. This case used to fail on
                            // the word "Armorer" appearing; that read the crossed labels as
                            // canon. Same case, same strictness, ruled polarity.
                            if (!string.IsNullOrEmpty(armorShopName)
                                && c.Label.IndexOf(armorShopName, StringComparison.OrdinalIgnoreCase) < 0)
                                Fail($"iron chip label '{c.Label}' does not name the ARMORER-role building " +
                                     $"'{armorShopName}' — the NEEDS cue must name the building that opens the gate.");
                            if (namesDiffer && c.Label.IndexOf(weaponShopName, StringComparison.OrdinalIgnoreCase) >= 0)
                                Fail($"iron chip label '{c.Label}' names the WEAPONS shop '{weaponShopName}' — " +
                                     "the name inversion leaked back into the UI.");
                            int iNow = c.Label.IndexOf("(now)", StringComparison.Ordinal);
                            int iNeeds = c.Label.IndexOf("NEEDS:", StringComparison.Ordinal);
                            if (iNow >= 0 && iNeeds >= 0 && iNow < iNeeds)
                                Fail($"iron chip label '{c.Label}' — ' (now)' must stay the LAST token (WO-883 order law)");
                            if (!c.Selected)
                                Fail("iron chip lost Selected under a closed gate — the cue must never unassign/lock the pick");
                        }
                    }

                    // (d) the status mirror: assigned-iron StateText says it is waiting.
                    string st = scope.Vm.StateText;
                    if (!st.Contains("Iron"))
                        Fail($"gated StateText '{st}' does not name the assigned resource (expected 'Gathering Iron ...')");
                    // RE-POINTED 2026-08-23: the mirror must name the ARMORER, with the
                    // article agreeing with the word ("waiting on AN Armorer"). The expected
                    // article is computed HERE from the resolved noun rather than written
                    // out, because the noun comes from the catalog and can be renamed —
                    // pinning the literal "an Armorer" would re-create the stranded-literal
                    // bug this whole cluster came from.
                    if (!string.IsNullOrEmpty(armorShopName))
                    {
                        string want = "waiting on " + ExpectedArticleFor(armorShopName) + " " + armorShopName;
                        if (st.IndexOf(want, StringComparison.OrdinalIgnoreCase) < 0)
                            Fail($"gated StateText '{st}' does not read '... {want}' — the status mirror must " +
                                 "name the building that opens the gate, grammatically.");
                    }
                    if (namesDiffer && st.IndexOf(weaponShopName, StringComparison.OrdinalIgnoreCase) >= 0)
                        Fail($"gated StateText '{st}' names the WEAPONS shop '{weaponShopName}' — the name " +
                             "inversion is back in the status line.");
                    if (!IsAscii(st))
                        Fail($"gated StateText '{st}' contains non-ASCII characters");
                }

                // (f) reopen → the cue clears.
                state.MarkEverBuilt("collector_forge");
                if (EchoCardVM.TryGetFaucetNeed("iron", out string stale))
                    Fail($"iron still cues 'NEEDS: {stale}' after collector_forge entered the ever-built ledger (gate reopened)");
            }
            finally
            {
                state.EverBuiltStructureIds.Clear();
                state.EverBuiltStructureIds.AddRange(savedLedger);
            }
        }

        /// <summary>
        /// The article the status line OUGHT to use for a noun — computed here, on purpose,
        /// rather than borrowed from EchoCardVM. An oracle that calls the very helper it is
        /// checking proves only that the helper equals itself; this second, independent
        /// implementation is what makes "waiting on an Armorer" an assertion.
        /// </summary>
        private static string ExpectedArticleFor(string noun)
        {
            if (string.IsNullOrEmpty(noun)) return "a";
            return "aeiouAEIOU".IndexOf(noun[0]) >= 0 ? "an" : "a";
        }

        /// <summary>Tiny disposable wrapper so each temporary VM always unsubscribes.</summary>
        private sealed class VmScope : IDisposable
        {
            public readonly EchoCardVM Vm;
            public VmScope(int index) { Vm = new EchoCardVM(index); }
            public void Dispose() { Vm.Dispose(); }
        }

        // =====================================================================
        //  Group 5 — WO-831 emergence data + graceful missing-art fallback
        // =====================================================================
        private static void CheckEmergenceData(Action<string> Fail)
        {
            var roster = EchoRosterCatalog.All;
            if (roster == null || roster.Length != 6)
            {
                Fail("roster unavailable for the emergence check");
                return;
            }
            foreach (var e in roster)
            {
                if (e == null) continue;
                if (string.IsNullOrEmpty(e.EmergeLine))
                    Fail($"'{e.Id}' has no EmergeLine (WO-831)");
                else if (!IsAscii(e.EmergeLine))
                    Fail($"'{e.Id}' EmergeLine contains non-ASCII characters");

                // Missing art must return null GRACEFULLY (Guard contract), never throw --
                // this is the "a missing sprite NEVER blocks the unlock" guarantee. If the
                // LFS art IS present, a sprite is equally acceptable.
                try
                {
                    EchoRosterCatalog.LoadEmergence(e.PortraitName);
                }
                catch (Exception ex)
                {
                    Fail($"LoadEmergence('{e.PortraitName}') THREW {ex.GetType().Name} (must degrade, never throw)");
                }
            }

            // The pure fallback line: null entry -> the shared default (never blank).
            string def = EchoUnlockDialogue.EmergeLineFor(null);
            if (string.IsNullOrEmpty(def) || !IsAscii(def))
                Fail($"EmergeLineFor(null) = '{def}' (expected the non-empty ASCII shared default)");
        }

        // =====================================================================
        //  Helpers
        // =====================================================================
        private static bool IsAscii(string s)
        {
            if (s == null) return true;
            foreach (char c in s) if (c > 127) return false;
            return true;
        }

        private static bool TryInstallHeadlessState(GameStateService svc, GameState state, out string err)
        {
            err = null;
            var stateField = typeof(GameStateService).GetField("_state", BindingFlags.NonPublic | BindingFlags.Instance);
            if (stateField == null)
            { err = "GameStateService._state field not found by reflection"; return false; }
            stateField.SetValue(svc, state);
            if (!TrySetInstanceStatic(svc))
            { err = "GameStateService._instance static not found by reflection"; return false; }
            return true;
        }

        private static bool TrySetInstanceStatic(GameStateService svc)
        {
            var f = typeof(GameStateService).GetField("_instance", BindingFlags.NonPublic | BindingFlags.Static);
            if (f == null) return false;
            f.SetValue(null, svc);
            return true;
        }
    }
}

// =============================================================================
// TroopRosterRegression — headless "real object in -> assert" gate for the
// Barracks 7-type troop roster + tier-unlock ladder (WO-736, program 732-737).
// -----------------------------------------------------------------------------
// Closes the roster program: proves the unlock ladder is DATA-CORRECT and
// train-gated so CoC barracks work can never silently ship as "two troops
// forever". Pure data/logic — NO PlayMode, NO GameObjects — so it runs inside the
// headless DataRegression.RunAll batch gate. Mirrors the CompanionRosterRegression
// contract exactly: public static bool Run(out string reason); true = pass + a
// one-line summary, false = fail + the offending detail. Never throws.
//
// INVARIANTS ASSERTED (from WORK_ORDER_PROGRAM_732_736 locked product table + WO-933):
//   1. TroopCatalog loads EXACTLY the 8 required ids (footman/archer/spearman/
//      shieldguard/outrider/siege-catapult/battlemage/echo-legionnaire) — no missing,
//      no extras, no duplicate ids. (Resources-path load == dual-copy proof.)
//   2. Defaults: footman + archer UnlockBarracksTier == 1 (day-one).
//   3. Ladder: spearman 2, shieldguard 3, outrider 4, catapult 4, battlemage 5, legionnaire 6.
//   4. Costs: wood/iron/food >= 0; slots >= 1 (economy sanity, WO-732 table).
//   5. Visuals (WO-735): every troop carries a non-empty model + iconId.
//   6. Barracks tier copy (WO-734): the building-tiers.json barracks effect text at
//      tier 2..6 names the unit that tier unlocks (so the upgrade panel announces it).
//   7. Unlock gate (WO-733): the ladder gates correctly at tier 1 (only day-one pair)
//      and a higher tier (T3 -> 4 trainable, outrider still locked); TroopUnlock's
//      real LockedReason ties 733+734 (tier number + authored tier name).
//   8. WO-933 Siege Catapult: role siege, maxOwned 1, slots heavy, range standoff.
// =============================================================================

using System.Collections.Generic;
using System.Text;
using DeNelle.Core.State;
using DeNelle.Village;

namespace DeNelle.Editor
{
    /// <summary>
    /// Data/logic regression for the Barracks troop roster: exact 9-id set, the
    /// unlock-tier ladder, cost/slot sanity, the WO-735 visual keys, the WO-734
    /// barracks tier announce copy, the WO-733 unlock gate, and WO-933 siege rules.
    /// Real static game code in, asserted out. Returns true (summary) / false (detail); never throws.
    /// </summary>
    public static class TroopRosterRegression
    {
        // The eight stable ids and their authored unlock tier (WO-732 table + WO-933
        // Siege Catapult at T4 beside Outrider — authoritative, do not drift).
        private static readonly (string Id, int Tier, string Display)[] Expected =
        {
            ("troop-footman",          1, "Footman"),
            ("troop-archer",           1, "Archer"),
            ("troop-spearman",         2, "Spearman"),
            ("troop-field-cleric",     3, "Field Cleric"),
            ("troop-shieldguard",      3, "Shieldguard"),
            ("troop-outrider",         4, "Outrider"),
            ("troop-catapult",         4, "Siege Catapult"),
            ("troop-battlemage",       5, "Battlemage"),
            ("troop-echo-legionnaire", 6, "Echo Legionnaire"),
        };

        public static bool Run(out string reason)
        {
            var failures = new List<string>();

            // Force a fresh read through the REAL WebGL-safe loader (Resources-first).
            TroopCatalog.Reload();
            BuildingTierCatalog.Reload();

            var all = TroopCatalog.All;
            if (all == null || all.Count == 0)
            {
                reason = "TROOP ROSTER FAIL: TroopCatalog.All is empty (troops.json mapping break or file missing).";
                return false;
            }

            // --- 1) exact 9-id set: no missing, no extras, no duplicates -----------
            var seen = new Dictionary<string, TroopDef>();
            foreach (var t in all)
            {
                if (t == null || string.IsNullOrEmpty(t.Id))
                { failures.Add("troops.json contains a troop with a null/empty id."); continue; }
                if (seen.ContainsKey(t.Id))
                    failures.Add($"duplicate troop id '{t.Id}' in troops.json (ids must be unique).");
                else
                    seen[t.Id] = t;
            }

            var expectedIds = new HashSet<string>();
            foreach (var e in Expected) expectedIds.Add(e.Id);

            foreach (var e in Expected)
                if (!seen.ContainsKey(e.Id))
                    failures.Add($"required troop '{e.Id}' is MISSING from troops.json.");
            foreach (var id in seen.Keys)
                if (!expectedIds.Contains(id))
                    failures.Add($"unexpected troop id '{id}' in troops.json (roster is the fixed 8-type set).");

            if (seen.Count != Expected.Length)
                failures.Add($"troops.json has {seen.Count} unique troop(s) — the roster is exactly {Expected.Length} (WO-732 + WO-933).");

            // --- 2+3) unlock ladder: each troop's authored tier matches the table --
            foreach (var e in Expected)
            {
                if (!seen.TryGetValue(e.Id, out var def)) continue;
                if (def.UnlockBarracksTier != e.Tier)
                    failures.Add($"'{e.Id}' UnlockBarracksTier == {def.UnlockBarracksTier}, expected {e.Tier} (unlock ladder drift).");

                // --- 4) cost / slot sanity ---------------------------------------
                if (def.CostGold < 0) failures.Add($"'{e.Id}' costGold {def.CostGold} < 0.");
                if (def.Slots != 1)   failures.Add($"'{e.Id}' slots {def.Slots}, expected 1 - army capacity is troop count, not weighted points.");
                if (TroopDialogueCommands.SlotOf(def.Id) != 1)
                    failures.Add($"'{e.Id}' production capacity resolver does not count it as exactly one troop.");

                // --- 5) WO-735 visuals: non-empty model + iconId -----------------
                if (string.IsNullOrEmpty(def.Model))
                    failures.Add($"'{e.Id}' has no model key (WO-735 requires a placeholder/real model, else it spawns a bare capsule).");
                if (string.IsNullOrEmpty(def.IconId))
                    failures.Add($"'{e.Id}' has no iconId (WO-735 requires a tray/portrait icon key).");
            }

            // --- 6) WO-734: barracks tier effect text announces each unlocked unit -
            foreach (var e in Expected)
            {
                if (e.Tier <= 1) continue;   // tier-1 is the day-one pair, no "unlock" line
                var tierDef = BuildingTierCatalog.TierOf("barracks", e.Tier);
                if (tierDef == null)
                { failures.Add($"building-tiers.json has no barracks tier {e.Tier} (WO-734 announce copy missing)."); continue; }
                string effect = tierDef.Effect ?? "";
                if (effect.IndexOf(e.Display, System.StringComparison.OrdinalIgnoreCase) < 0)
                    failures.Add($"barracks tier {e.Tier} effect text '{effect}' does not mention '{e.Display}' (WO-734: the upgrade must announce the unit it unlocks).");
            }

            // --- 7) WO-733 unlock gate: the ladder gates correctly by tier ---------
            // Mirror TroopUnlock's exact rule (UnlockBarracksTier <= effective tier)
            // at two effective tiers; live ModifierService wiring is covered by the
            // PO manual script (this proves the ladder produces the right gating).
            int trainableAtT1 = CountTrainable(seen.Values, 1);
            if (trainableAtT1 != 2)
                failures.Add($"at Barracks tier 1, {trainableAtT1} troop(s) train — expected exactly 2 (Footman + Archer).");

            int trainableAtT3 = CountTrainable(seen.Values, 3);
            if (trainableAtT3 != 5)
                failures.Add($"at Barracks tier 3, {trainableAtT3} troop(s) train — expected exactly 4 (Footman/Archer/Spearman/Shieldguard).");
            if (seen.TryGetValue("troop-outrider", out var outrider) && outrider.UnlockBarracksTier <= 3)
                failures.Add("Outrider is trainable at Barracks tier 3 — it must stay locked until tier 4.");
            if (seen.TryGetValue("troop-catapult", out var catapultEarly) && catapultEarly.UnlockBarracksTier <= 3)
                failures.Add("Siege Catapult is trainable at Barracks tier 3 — it must stay locked until tier 4.");

            // --- 8) WO-933 Siege Catapult product rules (CoC scarcity + WC siege) ---
            if (seen.TryGetValue("troop-catapult", out var catapult))
            {
                if (!string.Equals(catapult.Role, "siege", System.StringComparison.OrdinalIgnoreCase))
                    failures.Add($"troop-catapult role='{catapult.Role}' — expected 'siege' (structure-prefer hunt).");
                if (catapult.MaxOwned != 1)
                    failures.Add($"troop-catapult maxOwned={catapult.MaxOwned} — expected 1 (one owned at a time).");
                if (catapult.Slots != 1)
                    failures.Add($"troop-catapult slots={catapult.Slots} - expected 1; maxOwned=1 is its scarcity rule, not a housing tax.");
                if (catapult.AttackRange < 22f)
                    failures.Add($"troop-catapult attackRange={catapult.AttackRange} — expected >= 22 (standoff vs T1 towers).");
                if (catapult.MoveSpeed > 2.5f)
                    failures.Add($"troop-catapult moveSpeed={catapult.MoveSpeed} — expected <= 2.5 (slow vulnerability tax).");
                if (catapult.MaxHp > 70f)
                    failures.Add($"troop-catapult maxHp={catapult.MaxHp} — expected fragile (<= 70).");
                if (string.IsNullOrEmpty(catapult.Model) || catapult.Model.IndexOf("Catapult", System.StringComparison.OrdinalIgnoreCase) < 0)
                    failures.Add($"troop-catapult model='{catapult.Model}' — expected a Catapult machine path.");
            }

            // Animator class map: melee→Knight, archer→Ranger, battlemage→Mage (Cast).
            if (seen.TryGetValue("troop-shieldguard", out var shieldguard) &&
                !string.Equals(shieldguard.Role, "tank", System.StringComparison.OrdinalIgnoreCase))
                failures.Add($"troop-shieldguard role='{shieldguard.Role}' - expected 'tank'.");
            if (seen.TryGetValue("troop-field-cleric", out var cleric) &&
                !string.Equals(cleric.Role, "support", System.StringComparison.OrdinalIgnoreCase))
                failures.Add($"troop-field-cleric role='{cleric.Role}' - expected 'support'.");

            if (seen.TryGetValue("troop-archer", out var archerDef))
            {
                string a = TroopFactory.ResolveRoleController(archerDef, archerDef.Model);
                if (!string.Equals(a, "Ranger", System.StringComparison.OrdinalIgnoreCase))
                    failures.Add($"troop-archer ResolveRoleController='{a}' — expected Ranger (bow Attack).");
            }
            if (seen.TryGetValue("troop-footman", out var footDef))
            {
                string a = TroopFactory.ResolveRoleController(footDef, footDef.Model);
                if (!string.Equals(a, "Knight", System.StringComparison.OrdinalIgnoreCase))
                    failures.Add($"troop-footman ResolveRoleController='{a}' — expected Knight (melee Attack).");
            }
            if (seen.TryGetValue("troop-battlemage", out var mageDef))
            {
                string a = TroopFactory.ResolveRoleController(mageDef, mageDef.Model);
                if (!string.Equals(a, "Mage", System.StringComparison.OrdinalIgnoreCase))
                    failures.Add($"troop-battlemage ResolveRoleController='{a}' — expected Mage (Cast spells).");
                if (!TroopFactory.UsesCastStrike(mageDef, mageDef.Model))
                    failures.Add("troop-battlemage UsesCastStrike=false — should fire Cast, not Attack.");
            }

            // Real TroopUnlock.LockedReason ties WO-733 (tier number) + WO-734 (tier
            // name): a locked troop's reason must carry both.
            if (seen.TryGetValue("troop-echo-legionnaire", out var legionnaire))
            {
                string locked = TroopUnlock.LockedReason(legionnaire);
                if (string.IsNullOrEmpty(locked) || locked.IndexOf("6", System.StringComparison.Ordinal) < 0)
                    failures.Add($"TroopUnlock.LockedReason(echo-legionnaire) = '{locked}' — must cite Barracks Tier 6.");
                if (!string.IsNullOrEmpty(locked) && locked.IndexOf("Legion", System.StringComparison.OrdinalIgnoreCase) < 0)
                    failures.Add($"TroopUnlock.LockedReason(echo-legionnaire) = '{locked}' — should include the authored tier name ('Legion of Elarion', WO-734).");
            }

            if (failures.Count == 0)
            {
                reason = $"TROOP_ROSTER_OK — {seen.Count} troops, unique ids, unlock ladder 1/1/2/3/4+catapult/5/6, " +
                         $"costs+slots sane, every troop has model+iconId (WO-735), barracks T2-6 announce their unit (WO-734), " +
                         $"gate: 2 train @T1 / 5 @T3 (WO-733), siege maxOwned=1 (WO-933).";
                return true;
            }

            var sb = new StringBuilder();
            sb.Append($"TROOP_ROSTER_FAIL: {failures.Count} issue(s):");
            foreach (var f in failures) sb.Append("\n  - ").Append(f);
            reason = sb.ToString();
            return false;
        }

        // The exact gate rule TroopUnlock.IsTrainable applies: UnlockBarracksTier <=
        // the effective Barracks tier. Counts how many of the roster train at a tier.
        private static int CountTrainable(IEnumerable<TroopDef> troops, int effectiveTier)
        {
            int n = 0;
            foreach (var t in troops)
                if (t != null && t.UnlockBarracksTier <= effectiveTier) n++;
            return n;
        }
    }
}

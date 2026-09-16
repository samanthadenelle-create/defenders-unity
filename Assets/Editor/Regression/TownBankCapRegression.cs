// =============================================================================
// TownBankCapRegression -- the permission gate for the TOWN BANK CAP
// (WO-857 / WO-901 Phase F). ARCHITECTURE_PRINCIPLES Sec.2c: this suite is what makes
// putting an upper clamp on EconomyService.Grant -- the single path EVERY income
// source in the game flows through -- a safe thing to have done.
// -----------------------------------------------------------------------------
// Assembly: DeNelle.EditorRegression   Namespace: DeNelle.Editor.Regression
// Contract: public static bool Run(out string reason). Registered in DataRegression.RunAll
// as [town-bank-cap]. No System.Reflection -- TownBankCapacity lives in DeNelle.Core,
// which this asmdef already references, so every assertion drives the REAL code.
//
// WHAT THIS PINS, AND WHY EACH CASE EXISTS (each maps to a way this feature breaks
// the game -- these are not coverage, they are the specific catastrophes):
//
//   [no-crystal-cap]              Owner ruling 2026-08-04: crystals + coins are UNCAPPED by
//                                 design. This case FAILS if a crystal cap is ever introduced
//                                 -- the durable enforcement the ruling asked for, a test and
//                                 not a comment.
//   [basecap-never-zero]          A baseCap of 0 with no storage built clamps every grant to
//                                 nothing and the game is unplayable. Guarded STRUCTURALLY by
//                                 AbsoluteMinBaseCap, not by hoping the JSON is right.
//   [fresh-save-founding]         Starting wood and iron are 0 (NestedTypes.cs:78,80). The base
//                                 cap must hold the founding sequence's first income and its
//                                 first non-free purchase.
//   [spend-never-clamped]         Grant is reached with non-positive values; an UPPER clamp must
//                                 never touch a spend.
//   [clamped-grant-warns]         The warn is LOAD-BEARING (WO-901 Sec.5) -- it is the only thing
//                                 between the player and silently vaporised resources. Asserts
//                                 the warn PATH FIRES, not merely that the clamp happened.
//   [capacity-scales-with-level]  lumberyard/foundry/silo are progression buildings (WO-837).
//   [storage-ladder-6]            WO-966: the owner's SIX-level ladder stated as numbers --
//                                 1000/2000/4000/8000/16000/32000 held at levels 1..6, the ladder
//                                 reachable (maxLevel 6, inside RepoProps.MaxStructureLevel, every
//                                 rung priced), and every rung priced in the WO-947 regular-structure
//                                 basket (wood+iron, never crystals).
//   [order-ascending-capacity]    Deterministic order; ties never shuffle (the props would flicker).
//   [fill-smallest-first]         FAILS if the fill order ever flips to largest-first.
//   [largest-drains-first]        The drain-order invariant, swept over every total.
//   [order-intent-pallets-last]   The owner's OUTCOME ("pallets drain last") holds only while the
//                                 containers are SMALLER than baseCap. HARD-FAILS if a container
//                                 outgrows baseCap at LEVEL 1 (an inversion the player sees on the
//                                 day they build it); NOTES the level-2+ inversion, which WO-966's
//                                 six-level ladder makes true BY OWNER RULING -- see the case body
//                                 for why that half is a note and not a failure.
//   [over-cap-save-not-drained]   A live save already holding more than the new cap is
//                                 GRANDFATHERED. Retroactively deleting a player's resources on
//                                 load is not a cap, it is a bug.
//   [one-reader]                  Capacity math exists in exactly ONE place, and that place never
//                                 writes a wallet (no per-container balances -- WO-842).
//   [caps-data]                   storage-caps.json parses + dual-copy byte-identical.
//   [container-rows]              The three catalog containers still carry the seam this reads.
// =============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json.Linq;
using UnityEngine;
using DeNelle.Core.Economy;

namespace DeNelle.Editor.Regression
{
    public static class TownBankCapRegression
    {
        private const string CapsRelative = "Data/Canonical/storage-caps.json";
        private const string CatalogRelative = "Data/Canonical/structures-catalog.json";

        private static readonly string[] ContainerIds = { "lumberyard", "foundry", "silo" };

        public static bool Run(out string reason)
        {
            var failures = new List<string>();
            var notes = new List<string>();

            CheckCapsData(failures, notes);
            CheckCrystalsAndCoinsUncapped(failures, notes);
            CheckBaseCapNeverZero(failures, notes);
            CheckFreshSaveFounding(failures, notes);
            CheckSpendNeverClamped(failures, notes);
            CheckPurchasedGrantNeverClamped(failures, notes);
            CheckClampedGrantWarns(failures, notes);
            CheckCapacityScalesWithLevel(failures, notes);
            CheckStorageLadderSixLevels(failures, notes);
            CheckOrderingAndFill(failures, notes);
            CheckOrderIntentPalletsLast(failures, notes);
            CheckOverCapSaveNotDrained(failures, notes);
            CheckOneReader(failures, notes);
            CheckContainerRows(failures, notes);

            if (failures.Count == 0)
            {
                reason = "TOWN BANK CAP OK -- crystals+coins uncapped by design, baseCap floored, founding sequence holds, "
                       + "spends never upper-clamped, every clamped grant warns, capacity scales with level, "
                       + "fill/drain is one pure capacity-ascending function, over-cap saves grandfathered"
                       + (notes.Count > 0 ? $" [notes: {string.Join("; ", notes)}]" : "");
                return true;
            }

            reason = $"TOWN BANK CAP FAIL x{failures.Count}: " + string.Join(" | ", failures)
                   + (notes.Count > 0 ? $" [notes: {string.Join("; ", notes)}]" : "");
            return false;
        }

        // =====================================================================
        //  [caps-data] -- storage-caps.json parses, and the dual copy matches
        // =====================================================================
        private static void CheckCapsData(List<string> failures, List<string> notes)
        {
            string res = Path.Combine(Application.dataPath, "Resources/" + CapsRelative);
            string stream = Path.Combine(Application.dataPath, "StreamingAssets/" + CapsRelative);

            if (!File.Exists(res)) { failures.Add("[caps-data] Assets/Resources/" + CapsRelative + " is MISSING"); return; }
            if (!File.Exists(stream)) { failures.Add("[caps-data] Assets/StreamingAssets/" + CapsRelative + " is MISSING (dual-copy)"); return; }

            byte[] a = File.ReadAllBytes(res);
            byte[] b = File.ReadAllBytes(stream);
            if (a.Length != b.Length) failures.Add($"[caps-data] dual-copy differs in length ({a.Length} vs {b.Length})");
            else
            {
                for (int i = 0; i < a.Length; i++)
                    if (a[i] != b[i]) { failures.Add($"[caps-data] dual-copy is NOT byte-identical (first diff at byte {i})"); break; }
            }

            JObject root;
            try { root = JObject.Parse(File.ReadAllText(res)); }
            catch (Exception ex) { failures.Add("[caps-data] storage-caps.json does not parse: " + ex.Message); return; }

            var baseCap = root["baseCap"] as JObject;
            if (baseCap == null) { failures.Add("[caps-data] storage-caps.json has no 'baseCap' object"); return; }

            // Law 1 is enforced in code, but an authored crystals/coins key is a design smell that
            // reads as if crystals were capped. Fail it here so the ruling stays visible in the data.
            if (baseCap["crystals"] != null)
                failures.Add("[caps-data] storage-caps.json authors a 'crystals' baseCap -- crystals are UNCAPPED by owner ruling (WO-901 §6). Remove the key.");
            if (baseCap["coins"] != null)
                failures.Add("[caps-data] storage-caps.json authors a 'coins' baseCap -- coins/gold are UNCAPPED. Remove the key.");

            foreach (var word in new[] { "wood", "iron", "stone" })
            {
                if (baseCap[word] == null) { failures.Add($"[caps-data] storage-caps.json has no baseCap for '{word}'"); continue; }
                int v = baseCap[word].Value<int>();
                if (v <= 0) failures.Add($"[caps-data] baseCap.{word} is {v} -- a cap of zero clamps every grant to nothing");
            }

            var mults = root["levelCapacityMultipliers"] as JArray;
            if (mults == null || mults.Count == 0)
                failures.Add("[caps-data] levelCapacityMultipliers is missing/empty -- container levels would not scale");
            else
            {
                float prev = 0f;
                for (int i = 0; i < mults.Count; i++)
                {
                    float m = mults[i].Value<float>();
                    if (m < 1f) failures.Add($"[caps-data] levelCapacityMultipliers[{i}] = {m} -- a container may never hold LESS than its level-1 size");
                    if (m < prev) failures.Add($"[caps-data] levelCapacityMultipliers is not monotonic at index {i} ({prev} -> {m}) -- upgrading would SHRINK capacity");
                    prev = m;
                }
            }
        }

        // =====================================================================
        //  [no-crystal-cap] -- owner ruling 2026-08-04, enforced as a test
        // =====================================================================
        private static void CheckCrystalsAndCoinsUncapped(List<string> failures, List<string> notes)
        {
            foreach (var r in new[] { BankResource.Crystals, BankResource.Coins })
            {
                string n = TownBankCapacity.DisplayName(r);

                bool listed = false;
                for (int i = 0; i < TownBankCapacity.UncappableResources.Length; i++)
                    if (TownBankCapacity.UncappableResources[i] == r) listed = true;
                if (!listed)
                    failures.Add($"[no-crystal-cap] {r} is NOT in TownBankCapacity.UncappableResources -- the owner ruling (WO-901 §6: premium currency is uncapped) has been reversed in code");

                if (TownBankCapacity.IsCapped(r))
                    failures.Add($"[no-crystal-cap] IsCapped({r}) is TRUE -- {n} must never be storage-gated");

                if (TownBankCapacity.MaxOf(r) != int.MaxValue)
                    failures.Add($"[no-crystal-cap] MaxOf({r}) = {TownBankCapacity.MaxOf(r)} (expected int.MaxValue) -- a cap has been introduced on {n}");

                if (TownBankCapacity.RoomFor(r, int.MaxValue / 2) != int.MaxValue)
                    failures.Add($"[no-crystal-cap] RoomFor({r}) is finite -- a cap has been introduced on {n}");

                if (!TownBankCapacity.HasHeadroom(r, int.MaxValue / 2))
                    failures.Add($"[no-crystal-cap] HasHeadroom({r}) refused a huge amount -- {n} must always accept income");

                // The behavioural proof: a colossal grant on top of a colossal balance is untouched.
                int granted = TownBankCapacity.ClampGrant(r, 999999999, 1000000, "regression", out int lost);
                if (granted != 1000000 || lost != 0)
                    failures.Add($"[no-crystal-cap] ClampGrant({r}, current 999999999, +1000000) returned {granted} losing {lost} -- {n} WAS CLAMPED");
            }
        }

        // =====================================================================
        //  [basecap-never-zero] -- the structural guard, not a hoped-for number
        // =====================================================================
        private static void CheckBaseCapNeverZero(List<string> failures, List<string> notes)
        {
            if (TownBankCapacity.AbsoluteMinBaseCap <= 0)
            {
                failures.Add($"[basecap-never-zero] AbsoluteMinBaseCap is {TownBankCapacity.AbsoluteMinBaseCap} -- the floor that makes a missing/zeroed storage-caps.json survivable has been removed");
                return;
            }

            foreach (var r in new[] { BankResource.Wood, BankResource.Iron, BankResource.Stone })
            {
                int b = TownBankCapacity.BaseCapOf(r);
                if (b < TownBankCapacity.AbsoluteMinBaseCap)
                    failures.Add($"[basecap-never-zero] BaseCapOf({r}) = {b} < AbsoluteMinBaseCap {TownBankCapacity.AbsoluteMinBaseCap} -- the floor is not being applied");
                if (TownBankCapacity.MaxOf(r) <= 0)
                    failures.Add($"[basecap-never-zero] MaxOf({r}) = {TownBankCapacity.MaxOf(r)} -- every grant would clamp to nothing and the save is unplayable");
            }

            // The fallback object the loader returns when the file is missing/corrupt must carry NO
            // baseCap rows, so the ONLY thing standing between a bad file and a cap of zero is the
            // floor asserted above. If someone quietly adds defaults here, the floor stops being the
            // guard and this case is the warning.
            var builtIn = new StorageCapsData();
            if (builtIn.BaseCap == null)
                failures.Add("[basecap-never-zero] StorageCapsData.BaseCap defaults to null -- RawBaseCap would throw on a missing file");
            else if (builtIn.BaseCap.Count != 0)
                notes.Add($"StorageCapsData now ships {builtIn.BaseCap.Count} built-in baseCap default(s); AbsoluteMinBaseCap is still the hard floor");
        }

        // =====================================================================
        //  [fresh-save-founding] -- 0 wood / 0 iron, no storage built
        // =====================================================================
        private static void CheckFreshSaveFounding(List<string> failures, List<string> notes)
        {
            // The cap a fresh save has: baseCap alone (BaseLayout is empty, so no container adds).
            int woodCap = TownBankCapacity.BaseCapOf(BankResource.Wood);
            int ironCap = TownBankCapacity.BaseCapOf(BankResource.Iron);
            int foodCap = TownBankCapacity.BaseCapOf(BankResource.Stone);

            if (!TryReadCatalogCosts(out var rows, out string err))
            {
                notes.Add(DeNelle.Editor.Regression.RegressionOutcome.PartialSkip(
                    "[fresh-save-founding] affordability check", "structures-catalog.json unreadable (" + err + ")"));
                return;
            }

            // 1. The single most expensive structure in the game must be HOLDABLE on a fresh save,
            //    or a player who has not yet built any storage can never save up for it.
            int maxWood = 0, maxIron = 0, maxFood = 0;
            string maxWoodId = "", maxIronId = "", maxFoodId = "";
            foreach (var row in rows)
            {
                if (row.Wood > maxWood) { maxWood = row.Wood; maxWoodId = row.Id; }
                if (row.Iron > maxIron) { maxIron = row.Iron; maxIronId = row.Id; }
                if (row.Food > maxFood) { maxFood = row.Food; maxFoodId = row.Id; }
            }
            if (maxWood > woodCap) failures.Add($"[fresh-save-founding] the costliest structure '{maxWoodId}' needs {maxWood} wood but a fresh save can only hold {woodCap} -- unreachable before any storage exists");
            if (maxIron > ironCap) failures.Add($"[fresh-save-founding] '{maxIronId}' needs {maxIron} iron but a fresh save can only hold {ironCap}");
            if (maxFood > foodCap) failures.Add($"[fresh-save-founding] '{maxFoodId}' needs {maxFood} food but a fresh save can only hold {foodCap}");

            // 2. THE ESCAPE HATCH. Whatever else is true, the player must always be able to afford a
            //    CONTAINER out of a fresh wallet -- that is the move that raises the cap, so if it is
            //    ever unaffordable within baseCap the economy has a genuine dead end.
            foreach (var id in ContainerIds)
            {
                var row = rows.Find(x => string.Equals(x.Id, id, StringComparison.OrdinalIgnoreCase));
                if (row == null) { notes.Add($"[fresh-save-founding] no catalog row for container '{id}'"); continue; }
                if (row.Wood > woodCap || row.Iron > ironCap || row.Food > foodCap)
                    failures.Add($"[fresh-save-founding] SOFT-LOCK: container '{id}' costs {row.Wood}w/{row.Iron}i/{row.Food}f but a fresh save caps at {woodCap}w/{ironCap}i/{foodCap}f -- the player could never build the thing that raises the cap");
            }

            // 3. The founding sequence itself starts at 0 wood / 0 iron (NestedTypes.cs:78,80) and its
            //    kit is FREE-first-build, so it spends nothing; the only requirement is that the cap
            //    can hold the first income. A cap below the cheapest container's cost would break that.
            int cheapestContainerWood = int.MaxValue;
            foreach (var id in ContainerIds)
            {
                var row = rows.Find(x => string.Equals(x.Id, id, StringComparison.OrdinalIgnoreCase));
                if (row != null) cheapestContainerWood = Mathf.Min(cheapestContainerWood, row.Wood);
            }
            if (cheapestContainerWood != int.MaxValue && cheapestContainerWood > woodCap)
                failures.Add($"[fresh-save-founding] cheapest container costs {cheapestContainerWood} wood > fresh cap {woodCap}");
        }

        // =====================================================================
        //  [spend-never-clamped] -- an upper clamp must never touch outgo
        // =====================================================================
        private static void CheckSpendNeverClamped(List<string> failures, List<string> notes)
        {
            foreach (var r in new[] { BankResource.Wood, BankResource.Iron, BankResource.Stone })
            {
                int max = TownBankCapacity.MaxOf(r);

                // A spend, at a FULL wallet -- the exact state where an upper clamp would bite.
                int spend = TownBankCapacity.ClampGrant(r, max, -500, "regression-spend", out int lost);
                if (spend != -500 || lost != 0)
                    failures.Add($"[spend-never-clamped] ClampGrant({r}, current {max}, -500) returned {spend} losing {lost} -- a SPEND was upper-clamped");

                // A spend, over cap (a grandfathered save paying for something).
                int spend2 = TownBankCapacity.ClampGrant(r, max + 5000, -1, "regression-spend", out int lost2);
                if (spend2 != -1 || lost2 != 0)
                    failures.Add($"[spend-never-clamped] ClampGrant({r}, over-cap, -1) returned {spend2} losing {lost2}");

                // Zero is a no-op, never a warn.
                int zero = TownBankCapacity.ClampGrant(r, max, 0, "regression-spend", out int lost3);
                if (zero != 0 || lost3 != 0)
                    failures.Add($"[spend-never-clamped] ClampGrant({r}, 0) returned {zero} losing {lost3}");
            }
        }

        // =====================================================================
        //  [purchased-grant-never-clamped] -- an advertised quantity ALWAYS arrives in full
        // =====================================================================
        private static void CheckPurchasedGrantNeverClamped(List<string> failures, List<string> notes)
        {
            // The named exemption axis. The owner's clamp-and-warn ruling governs EARNED income;
            // it was never a ruling about what the player BUYS. A pack that advertises 5,000 food
            // and delivers 1,920 is not balance, it is selling something and not delivering it.
            if (!TownBankCapacity.IsClampable(BankGrantKind.EarnedIncome))
                failures.Add("[purchased-grant-never-clamped] EarnedIncome is no longer clamped -- the town bank cap does not apply to gameplay income at all (the owner's ruling has been reversed)");
            if (TownBankCapacity.IsClampable(BankGrantKind.PurchasedOrPromised))
                failures.Add("[purchased-grant-never-clamped] PurchasedOrPromised is CLAMPED -- a store pack can now silently under-deliver the quantity the player PAID FOR (refund/chargeback + store-policy exposure)");
            if (TownBankCapacity.IsClampable(BankGrantKind.DevHarness))
                failures.Add("[purchased-grant-never-clamped] DevHarness is clamped -- dev/AutoPilot funding is now storage-gated");

            // The paid path must actually REACH the exemption. These are the two links that broke it
            // the first time: EconomyService gating the clamp on the kind, and PackStoreVM resolving
            // the PURCHASED seam rather than the capped one.
            string eco = Path.Combine(Application.dataPath, "_Modules/Village/EconomyService.cs");
            if (File.Exists(eco))
            {
                string src = File.ReadAllText(eco);
                if (src.IndexOf("IsClampable", StringComparison.Ordinal) < 0)
                    failures.Add("[purchased-grant-never-clamped] EconomyService no longer gates the clamp on TownBankCapacity.IsClampable(kind) -- the exemption is not an explicit named axis any more");
                if (src.IndexOf("GrantSpendablePurchased", StringComparison.Ordinal) < 0)
                    failures.Add("[purchased-grant-never-clamped] EconomyService.GrantSpendablePurchased is GONE -- the pack entitlement bridge resolves it by name and would fall back to the capped grant");
            }

            string vm = Path.Combine(Application.dataPath, "_Modules/Wallet/PackStoreVM.cs");
            if (!File.Exists(vm)) { notes.Add("[purchased-grant-never-clamped] PackStoreVM.cs not found; entitlement-path check skipped"); }
            else
            {
                string src = File.ReadAllText(vm);
                if (src.IndexOf("GrantSpendablePurchased", StringComparison.Ordinal) < 0)
                    failures.Add("[purchased-grant-never-clamped] PackStoreVM does NOT resolve GrantSpendablePurchased -- a paid pack's resources go through the CAPPED grant and can under-deliver");
            }

            // And prove the exemption is LOAD-BEARING rather than vacuous: at least one real pack must
            // advertise more of a capped resource than a starter wallet could hold. If this stops
            // being true the case above is still correct but no longer guards anything real.
            string packs = Path.Combine(Application.dataPath, "Resources/Data/Canonical/packs.json");
            if (!File.Exists(packs)) { notes.Add("[purchased-grant-never-clamped] packs.json not found; load-bearing check skipped"); return; }

            JObject root;
            try { root = JObject.Parse(File.ReadAllText(packs)); }
            catch (Exception ex) { notes.Add("[purchased-grant-never-clamped] packs.json parse: " + ex.Message); return; }

            var arr = (root["packs"] as JArray) ?? (root["items"] as JArray);
            if (arr == null) { notes.Add("[purchased-grant-never-clamped] packs.json has no packs/items array"); return; }

            bool anyOverCap = false;
            string worst = null;
            foreach (var tok in arr)
            {
                var economy = (tok as JObject)?["contents"]?["economy"] as JObject;
                if (economy == null) continue;
                foreach (var pair in new[]
                {
                    new { Key = "wood", Res = BankResource.Wood },
                    new { Key = "iron", Res = BankResource.Iron },
                    new { Key = "food", Res = BankResource.Stone },
                })
                {
                    if (economy[pair.Key] == null) continue;
                    int amount = economy[pair.Key].Value<int>();
                    if (amount > TownBankCapacity.BaseCapOf(pair.Res))
                    {
                        anyOverCap = true;
                        worst = $"{tok["sku"]} advertises {amount} {pair.Key} vs a fresh cap of {TownBankCapacity.BaseCapOf(pair.Res)}";
                    }
                }
            }
            if (anyOverCap) notes.Add("purchased-grant exemption is load-bearing: " + worst);
            else notes.Add("no pack currently advertises more of a capped resource than baseCap -- the exemption is correct but not presently exercised");
        }

        // =====================================================================
        //  [clamped-grant-warns] -- the LOAD-BEARING warn actually fires
        // =====================================================================
        private static void CheckClampedGrantWarns(List<string> failures, List<string> notes)
        {
            var seen = new List<BankOverflowStatus>();
            Action<BankOverflowStatus> handler = s => seen.Add(s);

            int versionBefore = TownBankCapacity.LastOverflow.Version;
            TownBankCapacity.Overflowed += handler;
            try
            {
                int max = TownBankCapacity.MaxOf(BankResource.Wood);

                // A full wallet taking 500 more: 0 fits, 500 is lost.
                int granted = TownBankCapacity.ClampGrant(BankResource.Wood, max, 500, "regression", out int lost);
                if (granted != 0) failures.Add($"[clamped-grant-warns] a full wallet banked {granted} of 500 (expected 0)");
                if (lost != 500) failures.Add($"[clamped-grant-warns] reported {lost} lost (expected 500)");

                // A PARTIALLY full wallet: exactly the headroom fits and the rest is lost.
                int granted2 = TownBankCapacity.ClampGrant(BankResource.Wood, max - 200, 500, "regression", out int lost2);
                if (granted2 != 200) failures.Add($"[clamped-grant-warns] with 200 headroom, banked {granted2} of 500 (expected 200)");
                if (lost2 != 300) failures.Add($"[clamped-grant-warns] with 200 headroom, reported {lost2} lost (expected 300)");

                // A grant that FITS must not warn at all (no false alarms, or the toast is noise).
                int granted3 = TownBankCapacity.ClampGrant(BankResource.Wood, 0, 10, "regression", out int lost3);
                if (granted3 != 10 || lost3 != 0)
                    failures.Add($"[clamped-grant-warns] a grant that fits was clamped ({granted3}/10, lost {lost3})");

                if (seen.Count != 2)
                    failures.Add($"[clamped-grant-warns] the Overflowed event fired {seen.Count} time(s) for 2 clamped grants and 1 clean one -- "
                               + "the warn is the ONLY thing between the player and silently vaporised resources (WO-901 §5)");

                for (int i = 0; i < seen.Count; i++)
                {
                    var s = seen[i];
                    if (!s.Available) failures.Add("[clamped-grant-warns] published status has Available=false");
                    if (s.Lost <= 0) failures.Add($"[clamped-grant-warns] published status reports Lost={s.Lost}");
                    if (string.IsNullOrEmpty(s.ResourceName))
                        failures.Add("[clamped-grant-warns] published status does not NAME the resource -- the ruling requires the warn to name resource AND amount");
                    if (string.IsNullOrEmpty(s.ContainerName))
                        failures.Add("[clamped-grant-warns] published status does not name the container that fixes it");
                }

                if (TownBankCapacity.LastOverflow.Version <= versionBefore)
                    failures.Add("[clamped-grant-warns] LastOverflow.Version did not advance -- a polling HUD could never change-detect the event");
            }
            finally
            {
                TownBankCapacity.Overflowed -= handler;
            }

            // The ON-SCREEN half must exist and must route through the owned harvest-result modal --
            // a FlowTrace line in a log file is not a player warning.
            string presenter = Path.Combine(Application.dataPath, "_Modules/Core/UI/BankOverflowToastPresenter.cs");
            if (!File.Exists(presenter))
                failures.Add("[clamped-grant-warns] BankOverflowToastPresenter.cs is MISSING -- the clamp would lose resources with no on-screen warning");
            else
            {
                string src = File.ReadAllText(presenter);
                if (src.IndexOf("TownBankCapacity.Overflowed", StringComparison.Ordinal) < 0)
                    failures.Add("[clamped-grant-warns] BankOverflowToastPresenter does not subscribe to TownBankCapacity.Overflowed");
                if (src.IndexOf("HarvestOverflowModal.Present", StringComparison.Ordinal) < 0)
                    failures.Add("[clamped-grant-warns] BankOverflowToastPresenter does not open the harvest-result modal -- no player-facing warn");
                string modal = Path.Combine(Application.dataPath, "_Modules/Core/UI/HarvestOverflowModal.cs");
                if (!File.Exists(modal))
                    failures.Add("[clamped-grant-warns] HarvestOverflowModal.cs is MISSING");
                else
                {
                    string modalSrc = File.ReadAllText(modal);
                    if (modalSrc.IndexOf("BuildObsidianModal", StringComparison.Ordinal) < 0
                        || modalSrc.IndexOf("WorldHold.Acquire", StringComparison.Ordinal) < 0
                        || modalSrc.IndexOf("PanelManager.NotifyOpened", StringComparison.Ordinal) < 0)
                        failures.Add("[clamped-grant-warns] harvest warning is not an Obsidian, WorldHold-owned, arbiter-owned modal");
                    if (modalSrc.IndexOf("Collected: {s.Granted} of {s.Requested}", StringComparison.Ordinal) < 0
                        || modalSrc.IndexOf("Uncollected: {s.Lost}", StringComparison.Ordinal) < 0
                        || modalSrc.IndexOf("was not added to storage", StringComparison.Ordinal) < 0)
                        failures.Add("[clamped-grant-warns] harvest modal does not state authoritative collected/uncollected truth");
                    if (modalSrc.IndexOf("FitBlock(label, ElarionUi.FontFloorMobile", StringComparison.Ordinal) < 0
                        || modalSrc.IndexOf("TextOverflowModes.Ellipsis", StringComparison.Ordinal) >= 0)
                        failures.Add("[clamped-grant-warns] harvest modal lacks readable-floor fitting or uses ellipsis");
                }
                if (src.IndexOf("RuntimeInitializeOnLoadMethod", StringComparison.Ordinal) < 0)
                    failures.Add("[clamped-grant-warns] BankOverflowToastPresenter never self-attaches -- the warn would depend on someone remembering to wire it");
            }

            // And the clamp itself must be UNSWALLOWABLE: an unthrottled Warn on the loss path.
            string capSrc = Path.Combine(Application.dataPath, "_Modules/Core/Economy/TownBankCapacity.cs");
            if (File.Exists(capSrc))
            {
                string src = File.ReadAllText(capSrc);
                if (src.IndexOf("FlowTrace.Warn(\"Bank\",", StringComparison.Ordinal) < 0)
                    failures.Add("[clamped-grant-warns] TownBankCapacity no longer raises a FlowTrace.Warn on a clamped grant (§12: no silent failures)");
            }
        }

        // =====================================================================
        //  [capacity-scales-with-level] -- containers are PROGRESSION buildings (WO-837)
        // =====================================================================
        private static void CheckCapacityScalesWithLevel(List<string> failures, List<string> notes)
        {
            const int authored = 1000;
            int l1 = TownBankCapacity.CapacityAtLevel(authored, 1);
            int l2 = TownBankCapacity.CapacityAtLevel(authored, 2);
            int l3 = TownBankCapacity.CapacityAtLevel(authored, 3);

            if (l1 != authored)
                failures.Add($"[capacity-scales-with-level] level 1 capacity is {l1}, expected the authored {authored}");
            if (!(l2 > l1)) failures.Add($"[capacity-scales-with-level] L2 ({l2}) does not exceed L1 ({l1}) -- upgrading a container must hold MORE (WO-837: these are capacity-cap progression buildings)");
            if (!(l3 > l2)) failures.Add($"[capacity-scales-with-level] L3 ({l3}) does not exceed L2 ({l2})");

            // Degenerate inputs must never produce a negative or a phantom container.
            if (TownBankCapacity.CapacityAtLevel(0, 3) != 0)
                failures.Add("[capacity-scales-with-level] a non-container (storageCapacity 0) gained capacity from a level");
            if (TownBankCapacity.CapacityAtLevel(authored, 0) != l1)
                failures.Add("[capacity-scales-with-level] level 0 did not clamp to level 1");
            if (TownBankCapacity.CapacityAtLevel(authored, 99) < l3)
                failures.Add("[capacity-scales-with-level] a level past the multiplier table SHRANK the container");
        }

        // =====================================================================
        //  [storage-ladder-6] -- the OWNER'S NUMBERS, stated as numbers (WO-966)
        // ---------------------------------------------------------------------
        //  Owner ruling 2026-08-15, verbatim: "we need to make the storage containers
        //  upgradable, set 6 levels and each level adds 1k then next add 2k next 4k next 8k
        //  16k 32k" -- i.e. capacity AT level N = 1000/2000/4000/8000/16000/32000.
        //
        //  This case pins the RULING, not the mechanism: it drives the real CapacityAtLevel
        //  against the real catalog rows, so a tweak to storageCapacity, to
        //  levelCapacityMultipliers, or to maxLevel that breaks the stated curve fails the
        //  build with the owner's own numbers in the message. It also pins the reachability
        //  half -- a six-level ladder the upgrade verb refuses at level 3, or one whose steps
        //  have no authored price, is the exact "I tried, there is no way to upgrade them"
        //  the owner reported.
        // =====================================================================
        private static void CheckStorageLadderSixLevels(List<string> failures, List<string> notes)
        {
            // The owner's ladder, written as the owner wrote it.
            int[] expected = { 1000, 2000, 4000, 8000, 16000, 32000 };
            const int expectedMaxLevel = 6;

            var mults = StorageCapsCatalog.Data.LevelCapacityMultipliers;
            if (mults == null || mults.Count < expectedMaxLevel)
                failures.Add($"[storage-ladder-6] storage-caps.json authors {(mults == null ? 0 : mults.Count)} level multiplier(s) "
                           + $"but the owner ruled {expectedMaxLevel} container levels -- levels past the table all collapse onto the LAST "
                           + "multiplier, so the top rungs would cost real resources and grant no capacity.");

            if (!TryReadCatalogCosts(out var rows, out string err))
            {
                failures.Add("[storage-ladder-6] structures-catalog.json unreadable (" + err + ") -- the ladder cannot be verified at all");
                return;
            }

            foreach (var id in ContainerIds)
            {
                var row = rows.Find(x => string.Equals(x.Id, id, StringComparison.OrdinalIgnoreCase));
                if (row == null) { failures.Add($"[storage-ladder-6] no catalog row for container '{id}'"); continue; }

                if (row.MaxLevel != expectedMaxLevel)
                    failures.Add($"[storage-ladder-6] '{id}' authors maxLevel {row.MaxLevel}, expected {expectedMaxLevel} (owner ruling 2026-08-15)");

                if (row.MaxLevel > DeNelle.Core.Catalog.RepoProps.MaxStructureLevel)
                    failures.Add($"[storage-ladder-6] '{id}' authors maxLevel {row.MaxLevel} above RepoProps.MaxStructureLevel "
                               + $"{DeNelle.Core.Catalog.RepoProps.MaxStructureLevel} -- BuildModeController.MaxLevelFor clamps there, so the top "
                               + "levels are dead data and the Upgrade button reads 'Max Tier' early.");

                // THE CURVE, level by level, through the REAL reader.
                for (int lvl = 1; lvl <= expectedMaxLevel; lvl++)
                {
                    int got = TownBankCapacity.CapacityAtLevel(row.StorageCapacity, lvl);
                    if (got != expected[lvl - 1])
                        failures.Add($"[storage-ladder-6] '{id}' holds {got} at level {lvl}; the owner's ladder says {expected[lvl - 1]} "
                                   + $"(authored storageCapacity {row.StorageCapacity} x levelCapacityMultipliers[{lvl - 1}]). "
                                   + "The ruling is 1000/2000/4000/8000/16000/32000.");
                }

                // THE HEADLINE TOTAL: one maxed container of a resource + the base store.
                if (TownBankCapacity.TryParseResource(row.StorageResource, out var res))
                {
                    int maxed = TownBankCapacity.BaseCapOf(res) + TownBankCapacity.CapacityAtLevel(row.StorageCapacity, expectedMaxLevel);
                    int want = TownBankCapacity.BaseCapOf(res) + expected[expectedMaxLevel - 1];
                    if (maxed != want)
                        failures.Add($"[storage-ladder-6] a single maxed '{id}' tops the town out at {maxed} {row.StorageResource}, expected {want} "
                                   + $"(baseCap {TownBankCapacity.BaseCapOf(res)} + {expected[expectedMaxLevel - 1]}).");
                }

                // REACHABILITY: every rung above level 1 must have an authored price, and the
                // ladder must escalate. Without this a "6-level" container silently falls back to
                // the build-cost scaler (50 wood for +16000 capacity at the top rung).
                if (row.UpgradeCost == null || row.UpgradeCost.Count < expectedMaxLevel - 1)
                    failures.Add($"[storage-ladder-6] '{id}' authors {(row.UpgradeCost == null ? 0 : row.UpgradeCost.Count)} upgradeCost row(s) "
                               + $"but needs {expectedMaxLevel - 1} (L1->L2 .. L5->L6). A missing row falls back to the build cost x the level being "
                               + "left, which prices +16000 capacity at a founding-shed price.");
                else
                {
                    int prev = -1;
                    for (int i = 0; i < expectedMaxLevel - 1; i++)
                    {
                        var step = row.UpgradeCost[i];
                        int total = step.Wood + step.Iron + step.Food + step.Crystals;
                        if (total <= 0)
                            failures.Add($"[storage-ladder-6] '{id}' upgrade step L{i + 1}->L{i + 2} is FREE -- a capacity doubling with no sink");
                        if (total < prev)
                            failures.Add($"[storage-ladder-6] '{id}' upgrade step L{i + 1}->L{i + 2} ({total}) costs LESS than the previous step ({prev}) "
                                       + "-- each step doubles the capacity granted, so the price may never fall");
                        prev = total;

                        // WO-947 COST-BASKET RULING: a storage container is a REGULAR structure --
                        // wood + iron only. Crystals are the magical basket and must never appear here.
                        if (step.Crystals > 0)
                            failures.Add($"[storage-ladder-6] '{id}' upgrade step L{i + 1}->L{i + 2} charges {step.Crystals} CRYSTALS -- "
                                       + "WO-947: regular structures are priced in wood+iron; crystals are the magical basket");
                        if (step.Food > 0)
                            failures.Add($"[storage-ladder-6] '{id}' upgrade step L{i + 1}->L{i + 2} charges {step.Food} food -- "
                                       + "WO-947 keeps the regular-structure basket to wood+iron");
                    }
                }
            }
        }

        // =====================================================================
        //  [order-ascending-capacity] + [fill-smallest-first] + [largest-drains-first]
        // =====================================================================
        private static void CheckOrderingAndFill(List<string> failures, List<string> notes)
        {
            // Deliberately built OUT of order, with a capacity TIE, to prove the sort and the
            // tie-break both do their job.
            StorageSlot[] Make() => new[]
            {
                new StorageSlot { StructureId = "lumberyard", InstanceKey = "lumberyard@9,9",  Capacity = 2000, CellX = 9, CellZ = 9 },
                new StorageSlot { StructureId = "lumberyard", InstanceKey = "lumberyard@1,1",  Capacity = 500,  CellX = 1, CellZ = 1 },
                new StorageSlot { StructureId = "",           InstanceKey = "base",            Capacity = 500,  IsBaseStore = true },
                new StorageSlot { StructureId = "lumberyard", InstanceKey = "lumberyard@0,5",  Capacity = 1000, CellX = 0, CellZ = 5 },
            };

            var slots = Make();
            TownBankCapacity.OrderSlots(slots);

            for (int i = 1; i < slots.Length; i++)
                if (slots[i].Capacity < slots[i - 1].Capacity)
                    failures.Add($"[order-ascending-capacity] slot {i} (cap {slots[i].Capacity}) sorts after a LARGER slot (cap {slots[i - 1].Capacity}) -- the fill order has flipped to largest-first");

            if (!slots[0].IsBaseStore)
                failures.Add("[order-ascending-capacity] the tie between the base store and a same-capacity pallet did not break deterministically toward the base store");

            // Determinism: sorting a differently-shuffled copy must land on the same key order.
            var shuffled = Make();
            Array.Reverse(shuffled);
            TownBankCapacity.OrderSlots(shuffled);
            for (int i = 0; i < slots.Length; i++)
                if (!string.Equals(slots[i].InstanceKey, shuffled[i].InstanceKey, StringComparison.Ordinal))
                {
                    failures.Add("[order-ascending-capacity] the order depends on input order -- ties are NOT deterministic and the pallets would flicker frame to frame");
                    break;
                }

            // FILL SMALLEST FIRST. caps ordered 500,500,1000,2000; total 1300 ->
            // 500 + 500 + 300 + 0. Fails loudly if the fill ever flips to largest-first.
            TownBankCapacity.Fill(1300, slots, out int overflow);
            int[] expected = { 500, 500, 300, 0 };
            for (int i = 0; i < slots.Length; i++)
                if (slots[i].Contents != expected[i])
                {
                    failures.Add($"[fill-smallest-first] total 1300 over caps 500/500/1000/2000 filled [{slots[0].Contents},{slots[1].Contents},{slots[2].Contents},{slots[3].Contents}], expected [500,500,300,0] -- the fill order is not smallest-first");
                    break;
                }
            if (overflow != 0) failures.Add($"[fill-smallest-first] reported overflow {overflow} for a total well under capacity");

            // Boundary: at the cap every container reads FULL; at zero every container reads EMPTY.
            // This is the exact boundary the clamp must agree with.
            int sumCaps = 0;
            for (int i = 0; i < slots.Length; i++) sumCaps += slots[i].Capacity;
            TownBankCapacity.Fill(sumCaps, slots, out int atCapOverflow);
            for (int i = 0; i < slots.Length; i++)
                if (slots[i].Contents != slots[i].Capacity || slots[i].Fill01 < 0.999f)
                    failures.Add($"[fill-smallest-first] at the cap, slot {i} reads {slots[i].Contents}/{slots[i].Capacity} -- 'clamped' and 'every container full' must be the same state");
            if (atCapOverflow != 0) failures.Add($"[fill-smallest-first] overflow {atCapOverflow} exactly AT the cap");

            TownBankCapacity.Fill(0, slots, out int zeroOverflow);
            for (int i = 0; i < slots.Length; i++)
                if (slots[i].Contents != 0 || slots[i].Fill01 != 0f)
                    failures.Add($"[fill-smallest-first] at zero total, slot {i} reads {slots[i].Contents} -- must be empty");
            if (zeroOverflow != 0) failures.Add("[fill-smallest-first] overflow at a zero total");
            TownBankCapacity.Fill(-50, slots, out int negOverflow);
            if (negOverflow != 0) failures.Add("[fill-smallest-first] a negative total produced overflow instead of an empty bank");

            // THE DRAIN INVARIANT, swept over every total from the cap down to zero:
            // a slot may hold anything only while every SMALLER slot is completely full.
            // Equivalently -- the largest container empties FIRST, the smallest empties LAST.
            int lastNonEmptyIndexSeen = int.MaxValue;
            for (int total = sumCaps; total >= 0; total -= 37)
            {
                TownBankCapacity.Fill(total, slots, out _);
                int lastNonEmpty = -1;
                for (int i = 0; i < slots.Length; i++)
                {
                    if (slots[i].Contents > 0) lastNonEmpty = i;
                    if (slots[i].Contents > 0 && i > 0 && slots[i - 1].Contents != slots[i - 1].Capacity)
                        failures.Add($"[largest-drains-first] at total {total}, slot {i} holds {slots[i].Contents} while the smaller slot {i - 1} is only {slots[i - 1].Contents}/{slots[i - 1].Capacity} -- a larger container is being kept stocked ahead of a smaller one");
                }
                if (lastNonEmpty > lastNonEmptyIndexSeen)
                    failures.Add($"[largest-drains-first] at total {total} the frontier moved OUTWARD as the total fell -- draining is not the same monotone function as filling");
                lastNonEmptyIndexSeen = lastNonEmpty;
                if (total == 0) break;
            }

            // And the headline promise, stated directly: the SMALLEST container is the last one
            // still holding anything as the bank empties.
            TownBankCapacity.Fill(1, slots, out _);
            if (slots[0].Contents != 1)
                failures.Add("[largest-drains-first] with 1 unit left in the bank it is not in the SMALLEST container -- the smallest must be the last to empty");
            if (slots[slots.Length - 1].Contents != 0)
                failures.Add("[largest-drains-first] the LARGEST container still holds units when the bank is nearly empty");
        }

        // =====================================================================
        //  [order-intent-pallets-last] -- the OUTCOME the owner asked for
        // =====================================================================
        private static void CheckOrderIntentPalletsLast(List<string> failures, List<string> notes)
        {
            // Owner 2026-08-04: "By capacity. Fill smallest first, so pallets drain last."
            // Under an ascending fill the SMALLEST container is the last to drain, so the owner's
            // outcome holds ONLY while the pallets are smaller than the base store. If catalog
            // storageCapacity is ever raised above baseCap the look silently inverts -- the pallets
            // would start draining first, which the owner would see on the props. Fail it here
            // instead of shipping it as a visible bug.
            if (!TryReadCatalogCosts(out var rows, out string err))
            {
                notes.Add(DeNelle.Editor.Regression.RegressionOutcome.PartialSkip(
                    "[order-intent-pallets-last] intent check", "structures-catalog.json unreadable (" + err + ")"));
                return;
            }

            // ⚠ SCOPE CHANGED BY WO-966 (2026-08-15) -- READ THIS BEFORE "RESTORING" IT.
            //
            // This case used to HARD-FAIL whenever a container's capacity AT ITS MAX LEVEL reached
            // baseCap. The owner then ruled the containers up a six-level DOUBLING ladder topping out
            // at 32000 against a 2000 baseCap, so that condition is now true BY RULING, from container
            // level 2 up. Two owner rulings genuinely conflict and the NEWER one wins on capacity.
            //
            // The assertion is therefore SPLIT rather than deleted or softened:
            //   HARD FAIL at LEVEL 1 -- a container that outgrows the base store on the day it is
            //     BUILT inverts the look for every player immediately, which is a data mistake in any
            //     reading of either ruling.
            //   EXPLICIT NOTE for the level-2+ inversion -- the known, ruled consequence, reported on
            //     every run with the level it flips at so it can never quietly become folklore.
            // Restoring the old max-level failure would fail the build on the owner's own numbers.
            // If she wants the pallets-drain-last LOOK back at high levels, the fix is a presentation
            // ordering rule (base store fills last regardless of capacity) -- not a capacity change,
            // which would have to put baseCap above 32000 and make containers pointless.
            foreach (var row in rows)
            {
                if (row.StorageCapacity <= 0) continue;
                if (!TownBankCapacity.TryParseResource(row.StorageResource, out var res))
                {
                    failures.Add($"[order-intent-pallets-last] container '{row.Id}' authors storageCapacity {row.StorageCapacity} but storageResource '{row.StorageResource}' does not parse -- its capacity would be invisible to the bank");
                    continue;
                }

                int baseCap = TownBankCapacity.BaseCapOf(res);

                int capAtL1 = TownBankCapacity.CapacityAtLevel(row.StorageCapacity, 1);
                if (capAtL1 >= baseCap)
                    failures.Add($"[order-intent-pallets-last] '{row.Id}' holds {capAtL1} THE DAY IT IS BUILT (level 1), which is >= the "
                               + $"{TownBankCapacity.WordOf(res)} baseCap {baseCap}. Under the capacity-ascending fill law that makes the PALLET the "
                               + "last to fill and the FIRST to drain from the very first build -- the inverse of the owner's 2026-08-04 ruling "
                               + "('fill smallest first, so pallets drain last') with no upgrade even involved. Lower storageCapacity or raise baseCap.");

                // Where the ruled ladder crosses the base store -- reported, never silent.
                int levels = Mathf.Max(1, Mathf.Max(row.MaxLevel, StorageCapsCatalog.Data.LevelCapacityMultipliers.Count));
                int flipLevel = 0;
                for (int lvl = 1; lvl <= levels; lvl++)
                    if (TownBankCapacity.CapacityAtLevel(row.StorageCapacity, lvl) >= baseCap) { flipLevel = lvl; break; }
                if (flipLevel > 1)
                    notes.Add($"[order-intent-pallets-last] '{row.Id}' passes the {TownBankCapacity.WordOf(res)} baseCap {baseCap} at LEVEL {flipLevel} "
                            + $"(holds {TownBankCapacity.CapacityAtLevel(row.StorageCapacity, levels)} at level {levels}), so from that level the container "
                            + "drains BEFORE the base store -- the KNOWN, RULED consequence of the WO-966 six-level ladder, not a regression");
            }
        }

        // =====================================================================
        //  [over-cap-save-not-drained] -- grandfathering
        // =====================================================================
        private static void CheckOverCapSaveNotDrained(List<string> failures, List<string> notes)
        {
            var slots = new[]
            {
                new StorageSlot { InstanceKey = "base", Capacity = 2000, IsBaseStore = true },
                new StorageSlot { InstanceKey = "lumberyard@1,1", StructureId = "lumberyard", Capacity = 500 },
            };
            TownBankCapacity.OrderSlots(slots);

            const int legacyTotal = 9000;   // a live save from before the cap existed
            TownBankCapacity.Fill(legacyTotal, slots, out int overflow);

            int housed = 0;
            for (int i = 0; i < slots.Length; i++)
            {
                housed += slots[i].Contents;
                if (slots[i].Contents > slots[i].Capacity)
                    failures.Add($"[over-cap-save-not-drained] slot {i} was overfilled ({slots[i].Contents}/{slots[i].Capacity})");
            }
            if (housed + overflow != legacyTotal)
                failures.Add($"[over-cap-save-not-drained] {housed} housed + {overflow} overflow != the {legacyTotal} the save holds -- units were LOST just by LOOKING at the bank");
            if (overflow != legacyTotal - 2500)
                failures.Add($"[over-cap-save-not-drained] overflow {overflow}, expected {legacyTotal - 2500}");

            // RoomFor must read 0 for an over-cap save -- it must never go negative and it must
            // never be interpreted as "delete the difference".
            int room = TownBankCapacity.RoomFor(BankResource.Wood, TownBankCapacity.MaxOf(BankResource.Wood) + 5000);
            if (room != 0)
                failures.Add($"[over-cap-save-not-drained] RoomFor on an over-cap wallet returned {room}, expected 0");

            // And the load path must not clamp: nothing in the capacity reader may write a wallet.
            string capSrc = Path.Combine(Application.dataPath, "_Modules/Core/Economy/TownBankCapacity.cs");
            if (File.Exists(capSrc))
            {
                string src = File.ReadAllText(capSrc);
                foreach (var forbidden in new[] { "State.Wood =", "State.Iron =", "state.Wood =", "state.Iron =", ".Resources =" })
                    if (src.IndexOf(forbidden, StringComparison.Ordinal) >= 0)
                        failures.Add($"[over-cap-save-not-drained] TownBankCapacity contains '{forbidden}' -- the capacity READER must never write a wallet (that is how a load-time clamp deletes a player's resources)");
            }
        }

        // =====================================================================
        //  [one-reader] -- capacity math lives in exactly ONE place
        // =====================================================================
        private static void CheckOneReader(List<string> failures, List<string> notes)
        {
            string modules = Path.Combine(Application.dataPath, "_Modules");
            if (!Directory.Exists(modules)) { notes.Add("[one-reader] _Modules not found; scan skipped"); return; }

            string capFile = Path.Combine(modules, "Core/Economy/TownBankCapacity.cs").Replace('\\', '/');
            var offenders = new List<string>();
            foreach (var file in Directory.GetFiles(modules, "*.cs", SearchOption.AllDirectories))
            {
                string norm = file.Replace('\\', '/');
                if (string.Equals(norm, capFile, StringComparison.OrdinalIgnoreCase)) continue;
                if (norm.EndsWith("/Core/Catalog/RepoProps.cs", StringComparison.OrdinalIgnoreCase)) continue;   // the declaration itself
                string src;
                try { src = File.ReadAllText(file); } catch { continue; }
                // Anyone else READING the raw seam (member access, not a doc mention) is re-deriving
                // capacity outside the one reader -- that is how two ceilings come to disagree.
                //
                // BUT: calling the one reader's OWN accessor is the compliant thing to do, and the
                // substring scan cannot tell "repo.IsStorageContainer" (the violation) from
                // "TownBankCapacity.IsStorageContainer(repo)" (the fix) -- the sanctioned call
                // literally CONTAINS the forbidden substring. Caught 2026-08-07: the first-build
                // grace was routed through the accessor exactly as this guard intends, and the
                // guard failed it anyway, which would teach the next reader to route AROUND the
                // one reader instead of through it. Blank the sanctioned calls before scanning.
                var scan = src.Replace("TownBankCapacity.IsStorageContainer", "<sanctioned>")
                              .Replace("TownBankCapacity.storageCapacity", "<sanctioned>");
                if (scan.IndexOf(".storageCapacity", StringComparison.Ordinal) >= 0 ||
                    scan.IndexOf(".IsStorageContainer", StringComparison.Ordinal) >= 0)
                    offenders.Add(norm.Substring(norm.IndexOf("_Modules", StringComparison.Ordinal)));
            }
            if (offenders.Count > 0)
                failures.Add("[one-reader] repo.storageCapacity is read outside TownBankCapacity in: " + string.Join(", ", offenders)
                           + " -- capacity math must live in ONE place or two ceilings will disagree");

            // The clamp must be reachable from the ONE income choke.
            string eco = Path.Combine(modules, "Village/EconomyService.cs");
            if (!File.Exists(eco)) { failures.Add("[one-reader] EconomyService.cs not found"); return; }
            string ecoSrc = File.ReadAllText(eco);
            if (ecoSrc.IndexOf("TownBankCapacity.ClampGrant", StringComparison.Ordinal) < 0)
                failures.Add("[one-reader] EconomyService.Grant no longer applies TownBankCapacity.ClampGrant -- the town bank cap is not enforced on the single income choke");
            if (ecoSrc.IndexOf("GrantUncapped", StringComparison.Ordinal) < 0)
                notes.Add("EconomyService no longer exposes GrantUncapped; dev/AutoPilot funding is now storage-gated");

            // The one real income path that bypasses EconomyService must clamp for itself.
            string offline = Path.Combine(modules, "Village/Harvest/OfflineHarvestService.cs");
            if (File.Exists(offline) && File.ReadAllText(offline).IndexOf("TownBankCapacity.ClampGrant", StringComparison.Ordinal) < 0)
                failures.Add("[one-reader] OfflineHarvestService writes the wallet directly and does NOT clamp -- an away pool would bank hours of production straight past the cap");
        }

        // =====================================================================
        //  [container-rows] -- the catalog seam this reader depends on
        // =====================================================================
        private static void CheckContainerRows(List<string> failures, List<string> notes)
        {
            if (!TryReadCatalogCosts(out var rows, out string err))
            {
                notes.Add("[container-rows] structures-catalog.json unreadable (" + err + ")");
                return;
            }

            var expectedResource = new Dictionary<string, string>
            {
                { "lumberyard", "wood" }, { "foundry", "iron" }, { "silo", "stone" },
            };

            foreach (var kv in expectedResource)
            {
                var row = rows.Find(x => string.Equals(x.Id, kv.Key, StringComparison.OrdinalIgnoreCase));
                if (row == null) { failures.Add($"[container-rows] catalog row '{kv.Key}' is missing -- the bank would silently lose its {kv.Value} capacity source"); continue; }
                if (row.StorageCapacity <= 0)
                    failures.Add($"[container-rows] '{kv.Key}' has storageCapacity {row.StorageCapacity} -- it would stop counting as a container (IsStorageContainer is storageCapacity > 0)");
                if (!string.Equals(row.StorageResource, kv.Value, StringComparison.OrdinalIgnoreCase))
                    failures.Add($"[container-rows] '{kv.Key}' stores '{row.StorageResource}', expected '{kv.Value}'");
                if (row.MaxLevel < 1)
                    failures.Add($"[container-rows] '{kv.Key}' has maxLevel {row.MaxLevel}");
            }

            // The Jeweler is a shop, never storage (WO-857 binding product model).
            var jeweler = rows.Find(x => string.Equals(x.Id, "jeweler", StringComparison.OrdinalIgnoreCase));
            if (jeweler != null && jeweler.StorageCapacity > 0)
                failures.Add("[container-rows] 'jeweler' authors storageCapacity -- the Jeweler is a shop and must never raise the bank cap");
        }

        // =====================================================================
        //  helpers
        // =====================================================================

        private sealed class CatalogCostRow
        {
            public string Id;
            public int Wood, Iron, Food, Crystals;
            public int StorageCapacity;
            public string StorageResource;
            public int MaxLevel = 1;
            /// <summary>repo.upgradeCost, index 0 = L1-&gt;L2. Null when the row authors none (the
            /// build-cost scaler then prices every step -- see [storage-ladder-6]).</summary>
            public List<CatalogCostRow> UpgradeCost;
        }

        private static List<CatalogCostRow> _cachedRows;

        private static bool TryReadCatalogCosts(out List<CatalogCostRow> rows, out string err)
        {
            err = null;
            if (_cachedRows != null) { rows = _cachedRows; return true; }
            rows = new List<CatalogCostRow>();

            string path = Path.Combine(Application.dataPath, "Resources/" + CatalogRelative);
            if (!File.Exists(path)) { err = "not found at " + path; return false; }

            JObject root;
            try { root = JObject.Parse(File.ReadAllText(path)); }
            catch (Exception ex) { err = "parse: " + ex.Message; return false; }

            var entries = root["entries"] as JArray;
            if (entries == null) { err = "no 'entries' array"; return false; }

            foreach (var tok in entries)
            {
                if (!(tok is JObject o)) continue;
                var repo = o["repo"] as JObject;
                if (repo == null) continue;
                var cost = repo["cost"] as JObject;
                var row = new CatalogCostRow
                {
                    Id = o["id"]?.ToString(),
                    Wood = cost?["wood"] != null ? cost["wood"].Value<int>() : 0,
                    Iron = cost?["iron"] != null ? cost["iron"].Value<int>() : 0,
                    Food = cost?["food"] != null ? cost["food"].Value<int>() : 0,
                    Crystals = cost?["crystals"] != null ? cost["crystals"].Value<int>() : 0,
                    StorageCapacity = repo["storageCapacity"] != null ? repo["storageCapacity"].Value<int>() : 0,
                    StorageResource = repo["storageResource"]?.ToString(),
                    MaxLevel = repo["maxLevel"] != null ? repo["maxLevel"].Value<int>() : 1,
                };

                if (repo["upgradeCost"] is JArray steps)
                {
                    row.UpgradeCost = new List<CatalogCostRow>(steps.Count);
                    foreach (var stepTok in steps)
                    {
                        var s = stepTok as JObject;
                        if (s == null) continue;
                        row.UpgradeCost.Add(new CatalogCostRow
                        {
                            Id = row.Id,
                            Wood = s["wood"] != null ? s["wood"].Value<int>() : 0,
                            Iron = s["iron"] != null ? s["iron"].Value<int>() : 0,
                            Food = s["food"] != null ? s["food"].Value<int>() : 0,
                            Crystals = s["crystals"] != null ? s["crystals"].Value<int>() : 0,
                        });
                    }
                }

                if (!string.IsNullOrEmpty(row.Id)) rows.Add(row);
            }

            _cachedRows = rows;
            return true;
        }
    }
}

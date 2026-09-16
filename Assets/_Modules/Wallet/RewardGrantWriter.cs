// =============================================================================
// RewardGrantWriter - the ONE fulfillment writer for battle-pass tiers and
// monthly-card daily drips (WORK_ORDER_battle_and_monthly_packs section 4.3).
// -----------------------------------------------------------------------------
// Assembly: DeNelle.Wallet   Namespace: DeNelle.Wallet
//
// Both families converge here. A reward is a JSON row and this writer dispatches
// on its KIND - it NEVER switches on a SKU, a season id, a tier index or a day
// number. That is the whole reason a new reward is a data edit.
//
// WHAT IT WILL NOT DO, AND WHY THAT IS STRUCTURAL RATHER THAN POLICED:
// there is no code path in this file that can grant combat power, because there
// is no reward kind that expresses it (RewardKind has no Combat member) and the
// catalogue has already dropped anything unsanctioned at load
// (BattleMonthlyCatalog.EnforceFirewall). By the time a grant reaches this
// writer it has passed the covenant, the redeemer set and both deliverability
// gates. This writer's job is to make sure what survived actually LANDS.
//
// CAPPED vs PURCHASED - a real distinction, not a copy-paste slip:
//   * EARNED rewards (every battle-pass tier) route through the town-bank-capped
//     GrantSpendable. A season tier is income, and income obeys storage.
//   * PAID rewards (every monthly-card drip) route through GrantSpendablePurchased,
//     the same uncapped seam PackStoreVM uses. What a player bought lands in full;
//     silently shaving a paid drip against a full store is a refund problem.
// The caller states which it is. Neither name is guessed at a call site.
//
// &#9888; DUPLICATED MECHANISM, DECLARED. PackStoreVM carries its own private copies
// of these AppDomain reflection bridges. They are duplicated here rather than
// shared because PackStoreVM's are private and sit in another live lane; what
// must never be duplicated is a DECISION, and no decision lives in either copy -
// only the same mechanical hop across an asmdef boundary. If a seam is ever
// RENAMED, both copies must move in the same commit; the seam names are held as
// named constants below so a search finds them.
//
// WHY REFLECTION AT ALL: DeNelle.Wallet cannot reference DeNelle.Village (one-way
// asmdef guard) nor DeNelle.Cosmetics (Cosmetics -> Wallet already, so a back
// reference would be circular). Read the .asmdef - CLAUDE.md section 5.
//
// Every miss Fails LOUDLY. A reward the player earned and did not receive is the
// failure mode this whole file exists to make impossible to miss.
// ASCII-only strings. Never throws.
// =============================================================================

using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using DeNelle.Core.State;
using DeNelle.Core.Diagnostics;

namespace DeNelle.Wallet
{
    /// <summary>Whether a grant is EARNED (bank-capped) or PAID FOR (lands in full).</summary>
    public enum GrantOrigin
    {
        /// <summary>Climbed to by playing - a battle-pass tier. Obeys the town bank cap.</summary>
        Earned = 0,
        /// <summary>Bought - a monthly-card daily drip. Uncapped, like every pack entitlement.</summary>
        Purchased = 1,
    }

    /// <summary>Dispatches a <see cref="RewardGrant"/> onto the game's canonical, persisted seams.</summary>
    public static class RewardGrantWriter
    {
        // The seam names, held once so a rename is findable. Resolved by string because the
        // asmdef forbids the direct reference (see the header).
        private const string EconomyTypeName        = "DeNelle.Village.EconomyService";
        private const string CosmeticOwnershipTypeName = "DeNelle.Cosmetics.CosmeticOwnershipService";
        private const string GrantCappedMethod      = "GrantSpendable";
        private const string GrantPurchasedMethod   = "GrantSpendablePurchased";
        private const string AddCoinsMethod         = "AddCoins";
        private const string GrantCosmeticMethod    = "GrantAchievement";

        /// <summary>
        /// Pays out one grant. Returns true when everything in it landed.
        /// <para><paramref name="where"/> is a human-readable provenance ("season tier 12 free",
        /// "card monthly-keeper day 7") and appears in every trace line, so a failed grant names
        /// itself rather than leaving a reader to guess which row broke.</para>
        /// </summary>
        public static bool Grant(RewardGrant grant, GrantOrigin origin, string where)
        {
            if (grant == null) return true;   // an unauthored slot is not a failure
            return GrantInternal(grant, origin, where ?? "<unknown>", 0);
        }

        private static bool GrantInternal(RewardGrant grant, GrantOrigin origin, string where, int depth)
        {
            if (grant == null || depth > 8) return false;

            switch (grant.Kind)
            {
                case RewardKind.Economy:
                    return GrantEconomy(grant.Economy, origin, where);

                case RewardKind.ConvenienceToken:
                    return GrantConvenience(grant.Convenience, where);

                case RewardKind.CosmeticSku:
                    return GrantCosmetic(grant.CosmeticSku, where);

                case RewardKind.Skr:
                    // Unreachable while BattleMonthlyCatalog.SkrLedgerAvailable is false - the
                    // catalogue drops these at load. Kept as a LOUD refusal rather than a silent
                    // no-op so that if the gate is ever loosened without a writer, the missing
                    // half is named on the very first grant instead of quietly vanishing.
                    FlowTrace.Fail("BattlePass", where + ": an skr grant reached the writer, but there is no SKR " +
                                                 "ledger in this build to credit. NOTHING was granted. This grant " +
                                                 "should have been dropped at load - if it was not, the " +
                                                 "deliverability gate has been loosened without a writer behind it.");
                    return false;

                case RewardKind.Bundle:
                {
                    if (grant.Bundle == null) return false;
                    bool all = true;
                    for (int i = 0; i < grant.Bundle.Count; i++)
                        all &= GrantInternal(grant.Bundle[i], origin, where + " [bundle " + i + "]", depth + 1);
                    return all;
                }

                default:
                    FlowTrace.Fail("BattlePass", where + ": unsanctioned reward kind reached the writer ('" +
                                                 (grant.KindRaw ?? "<null>") + "') - NOTHING granted.");
                    return false;
            }
        }

        // =====================================================================
        //  Economy
        // =====================================================================

        private static bool GrantEconomy(RewardEconomy e, GrantOrigin origin, string where)
        {
            if (e == null || e.IsEmpty) return true;

            int wood = Mathf.Max(0, e.Wood), iron = Mathf.Max(0, e.Iron);
            int food = Mathf.Max(0, e.Stone), crystals = Mathf.Max(0, e.Crystals);
            int coins = Mathf.Max(0, e.Coins);

            bool ok = true;
            if (wood > 0 || iron > 0 || food > 0 || crystals > 0)
                ok &= GrantResources(wood, food, iron, crystals, origin, where);
            if (coins > 0)
                ok &= GrantCoins(coins, where);

            // The proof line. It states EXACTLY what was asked for on this row, so a shortfall can
            // never be inferred from silence.
            FlowTrace.Step("BattlePass", where + ": granted wood=" + wood + " iron=" + iron + " food=" + food +
                                         " crystals=" + crystals + " coins=" + coins +
                                         " (origin=" + origin + ", routed through EconomyService).");
            return ok;
        }

        private static bool GrantResources(int wood, int food, int iron, int crystals, GrantOrigin origin, string where)
        {
            var svc = ResolveInstance(EconomyTypeName, out var type);
            if (svc == null || type == null)
            {
                FlowTrace.Fail("BattlePass", where + ": EconomyService is not available - " + wood + " wood / " +
                                             iron + " iron / " + food + " food / " + crystals +
                                             " crystals were NOT granted. The player earned them and did not get them.");
                return false;
            }

            string methodName = origin == GrantOrigin.Purchased ? GrantPurchasedMethod : GrantCappedMethod;
            var method = type.GetMethod(methodName, new[] { typeof(int), typeof(int), typeof(int), typeof(int) });
            if (method == null)
            {
                FlowTrace.Fail("BattlePass", where + ": EconomyService." + methodName +
                                             "(int,int,int,int) not found - resources NOT granted. If that seam was " +
                                             "renamed, this writer must be re-pointed in the SAME change.");
                return false;
            }

            try
            {
                // Argument order is (wood, food, iron, crystals) - NOT alphabetical, and not the
                // order the JSON authors them in. Getting this wrong pays iron as food silently.
                method.Invoke(svc, new object[] { wood, food, iron, crystals });
                return true;
            }
            catch (Exception ex)
            {
                FlowTrace.Fail("BattlePass", where + ": EconomyService." + methodName + " THREW: " +
                                             ex.GetType().Name + ": " + ex.Message + " - resources NOT granted.");
                return false;
            }
        }

        private static bool GrantCoins(int coins, string where)
        {
            var svc = ResolveInstance(EconomyTypeName, out var type);
            if (svc == null || type == null)
            {
                FlowTrace.Fail("BattlePass", where + ": EconomyService unavailable - " + coins + " coins NOT granted.");
                return false;
            }
            var method = type.GetMethod(AddCoinsMethod, new[] { typeof(int) });
            if (method == null)
            {
                FlowTrace.Fail("BattlePass", where + ": EconomyService." + AddCoinsMethod +
                                             "(int) not found - " + coins + " coins NOT granted.");
                return false;
            }
            try { method.Invoke(svc, new object[] { coins }); return true; }
            catch (Exception ex)
            {
                FlowTrace.Fail("BattlePass", where + ": AddCoins THREW: " + ex.GetType().Name + ": " + ex.Message +
                                             " - " + coins + " coins NOT granted.");
                return false;
            }
        }

        // =====================================================================
        //  Convenience tokens
        // =====================================================================

        /// <summary>
        /// Accrues a convenience token into <c>GameState.GearInventory</c> under the SAME key shape
        /// PackStoreVM.ApplyPackContents writes ("convenience:&lt;kind&gt;"), so a token earned on
        /// the pass and a token bought in a pack are the same token to the redeemer. Two key shapes
        /// for one item would mean a pass token that Lantern.cs cannot see.
        /// </summary>
        private static bool GrantConvenience(RewardConvenience conv, string where)
        {
            if (conv == null || string.IsNullOrEmpty(conv.Kind) || conv.Count <= 0) return true;

            var state = GameStateService.Instance != null ? GameStateService.Instance.State : null;
            if (state == null)
            {
                FlowTrace.Fail("BattlePass", where + ": no GameState - " + conv.Count + "x '" + conv.Kind +
                                             "' NOT granted.");
                return false;
            }

            try
            {
                if (state.GearInventory == null) state.GearInventory = new Dictionary<string, int>();
                string key = "convenience:" + conv.Kind.Trim().ToLowerInvariant();
                state.GearInventory.TryGetValue(key, out int prior);
                state.GearInventory[key] = Mathf.Max(0, prior) + conv.Count;
                FlowTrace.Step("BattlePass", where + ": granted " + conv.Count + "x '" + key + "' (now " +
                                             state.GearInventory[key] + ").");
                return true;
            }
            catch (Exception ex)
            {
                FlowTrace.Fail("BattlePass", where + ": convenience grant THREW: " + ex.GetType().Name + ": " +
                                             ex.Message + " - nothing granted.");
                return false;
            }
        }

        // =====================================================================
        //  Cosmetics - unreachable today, kept honest for the day G1 lands
        // =====================================================================

        private static bool GrantCosmetic(string sku, string where)
        {
            if (string.IsNullOrEmpty(sku)) return true;

            // Write BOTH ownership stores, exactly as PackStoreVM does: GameState.OwnedItemIds is
            // what the pack/entitlement side reads, CosmeticOwnershipService.Owns is what the wardrobe
            // reads. Writing one and not the other is the split-brain that makes a cosmetic "owned"
            // and un-equippable at the same time.
            bool ok = true;

            var state = GameStateService.Instance != null ? GameStateService.Instance.State : null;
            if (state != null && state.OwnedItemIds != null)
            {
                if (!state.OwnedItemIds.Contains(sku)) state.OwnedItemIds.Add(sku);
            }
            else
            {
                FlowTrace.Fail("BattlePass", where + ": no GameState - cosmetic '" + sku +
                                             "' not recorded in OwnedItemIds.");
                ok = false;
            }

            var svc = ResolveInstance(CosmeticOwnershipTypeName, out var type);
            if (svc == null || type == null)
            {
                FlowTrace.Fail("BattlePass", where + ": CosmeticOwnershipService unavailable - cosmetic '" + sku +
                                             "' is not in the wardrobe's own-set, so the player cannot equip it.");
                return false;
            }
            var method = type.GetMethod(GrantCosmeticMethod, new[] { typeof(string) });
            if (method == null)
            {
                FlowTrace.Fail("BattlePass", where + ": " + GrantCosmeticMethod +
                                             "(string) not found - cosmetic '" + sku + "' not equippable.");
                return false;
            }
            try { method.Invoke(svc, new object[] { sku }); }
            catch (Exception ex)
            {
                FlowTrace.Fail("BattlePass", where + ": GrantAchievement THREW: " + ex.GetType().Name + ": " +
                                             ex.Message + " - cosmetic '" + sku + "' not equippable.");
                return false;
            }

            FlowTrace.Step("BattlePass", where + ": cosmetic '" + sku + "' recorded owned in BOTH stores.");
            return ok;
        }

        // =====================================================================
        //  Persistence + the reflection hop
        // =====================================================================

        /// <summary>Persists through the service so the grant round-trips. Best-effort, never throws.</summary>
        public static void Save(string where)
        {
            try
            {
                var svc = GameStateService.Instance;
                if (svc != null) svc.Save();
                else FlowTrace.Warn("BattlePass", where + ": no GameStateService to save through - the grant is " +
                                                 "live in memory but may not survive a restart.");
            }
            catch (Exception ex)
            {
                FlowTrace.Fail("BattlePass", where + ": save THREW: " + ex.GetType().Name + ": " + ex.Message);
            }
        }

        /// <summary>Resolves a singleton service's live Instance by type name across loaded assemblies.</summary>
        private static object ResolveInstance(string typeName, out Type type)
        {
            type = null;
            try
            {
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    if (asm == null) continue;
                    type = asm.GetType(typeName, false);
                    if (type != null) break;
                }
                if (type == null) return null;
                var prop = type.GetProperty("Instance", BindingFlags.Static | BindingFlags.Public);
                return prop != null ? prop.GetValue(null) : null;
            }
            catch (Exception ex)
            {
                FlowTrace.Fail("BattlePass", "resolving '" + typeName + "' THREW: " + ex.GetType().Name + ": " +
                                             ex.Message);
                return null;
            }
        }
    }
}

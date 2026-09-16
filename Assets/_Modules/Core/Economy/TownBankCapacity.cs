// =============================================================================
// TownBankCapacity -- THE town bank cap (WO-857 / WO-901 Phase F). ONE reader.
// -----------------------------------------------------------------------------
// Assembly: DeNelle.Core   Namespace: DeNelle.Core.Economy
//
// WHAT THIS IS
//   The single authority for "how much of resource R can the town hold, and how is
//   what it holds distributed across its containers". Every system that needs a cap,
//   a headroom check, or a per-container fill level asks THIS class. Nothing else
//   computes capacity -- scattering cap lookups is exactly how two ceilings disagree.
//
//   Max(R) = baseCap(R) + sum( storageCapacity of BUILT containers whose
//                              repo.storageResource == R, scaled by placed level )
//
//   This wires the seam RepoProps.cs:155 (storageCapacity) + :174 (IsStorageContainer)
//   have carried with ZERO consumers since WO-707 -- :169 literally reads
//   "TODO(WO-707/WO-672): wire this seam".
//
// THE FOUR LAWS THIS FILE ENFORCES STRUCTURALLY (not by comment, not by hope)
//
//   1. CRYSTALS AND COINS ARE UNCAPPED, BY DESIGN. Owner ruling 2026-08-04 (WO-901
//      Sec.6): premium/bottleneck currency is never storage-gated -- the CoC precedent
//      is gems uncapped, gold/elixir capped by storages. The exemption is the named
//      constant UncappableResources below; storage-caps.json CANNOT add crystals or
//      coins to the capped set (an authored key is ignored with a warn), and
//      TownBankCapRegression case [no-crystal-cap] FAILS if a crystal cap ever appears.
//
//   2. A CAP CAN NEVER RESOLVE TO ZERO. BaseCapOf floors every answer at
//      AbsoluteMinBaseCap regardless of what the data says, so a missing / truncated /
//      zeroed storage-caps.json degrades to a playable wallet instead of a save whose
//      every grant clamps to nothing. Guarded structurally, not by trusting a number.
//
//   3. A SPEND IS NEVER UPPER-CLAMPED. ClampGrant refuses to touch a non-positive
//      request and returns it unchanged. The cap is a ceiling on INCOME; the existing
//      Mathf.Max(0, ...) floors stay the only bound on outgo.
//
//   5. A PURCHASED OR PROMISED QUANTITY IS NEVER CLAMPED. The owner's clamp-and-warn
//      ruling was about the ECONOMY -- what the player EARNS. A pack that advertises
//      5,000 food and delivers 1,920 is not balance, it is selling something and not
//      delivering it. The exemption axis is the named enum BankGrantKind + IsClampable,
//      never an incidental code path, and TownBankCapRegression case
//      [purchased-grant-never-clamped] fails the build if a paid grant is ever clamped.
//
//   6. ABOVE THE CAP IS A LEGITIMATE STATE, AND THE COPY MUST NOT CALL IT A LOSS.
//      See `FOUNDATIONAL_RULINGS.md` section 7 -- read it there, it is deliberately not
//      restated here (a paraphrase in a second place is this repo's dominant failure).
//      MECHANICALLY nothing new was needed: ClampGrant's room = max(0, max - current)
//      already returns 0 above the cap, and IsClampable already exempts a paid grant
//      entirely, so the two behaviours the ruling names were ALREADY TRUE at WO-1191.
//      What was missing was the FRAMING -- above the cap this warn said "BANK FULL ...
//      LOST N ... build a bigger container", which after a purchase reads as a penalty
//      for having paid. BankOverflowStatus.OverCap is the named axis (never a resource
//      name, never a sourceTag string match), and WO1191OverCapIncomeRegression proves
//      the deltas by MEASUREMENT.
//
//   4. AN EXISTING SAVE OVER THE CAP IS GRANDFATHERED, NEVER DRAINED. Nothing here
//      writes a wallet. Over-cap totals are reported as Apportionment.Overflow and are
//      spent down normally; RoomFor simply reads 0 until the total falls under Max.
//      Retroactively deleting a player's resources on load is not a cap, it is a bug.
//
// THE FILL / DRAIN ORDER (owner ruling 2026-08-04)
//   "By capacity. Fill smallest first, so pallets drain last."
//
//   MECHANIC. Containers are ordered by CAPACITY ASCENDING -- never by current contents,
//   because capacity only moves when a building is placed or upgraded, so the order is
//   stable and the props cannot flicker frame to frame as balances move. Occupancy is a
//   PURE FUNCTION of the single authoritative total: fill each container to its capacity
//   in that order until the total is exhausted. DRAINING IS THE SAME FUNCTION evaluated at
//   a lower total -- there is no separate drain path, no per-container balance, and
//   therefore nothing that can disagree with the wallet.
//
//   THE CONSEQUENCE, STATED EXACTLY, because it is easy to get backwards: under a pure
//   ascending fill the SMALLEST container fills FIRST and therefore empties LAST (it only
//   gives anything up once everything above it is already empty), and the LARGEST fills
//   LAST and empties FIRST. "Fill smallest first, SO pallets drain last" is one sentence,
//   not two rules: it holds precisely when the PALLETS ARE THE SMALLER CONTAINERS.
//
//   THE DATA DEPENDENCY THAT MAKES THE OWNER'S OUTCOME TRUE. The pallets stay visibly
//   stocked while the abstract base store churns ONLY while a container's capacity stays
//   BELOW baseCap.
//
//   ** STATE AS OF WO-966 (owner ruling 2026-08-15) -- READ THIS, IT CHANGED. ** The owner
//   ruled the containers UPGRADABLE over SIX levels on a DOUBLING ladder: storageCapacity
//   1000 x levelCapacityMultipliers [1,2,4,8,16,32] = 1000/2000/4000/8000/16000/32000,
//   against an unchanged baseCap of 2000. So the "capacity below baseCap" dependency now
//   holds ONLY at container level 1 (1000 < 2000); from level 2 up the container equals and
//   then dwarfs the base store, and under the capacity-ascending law it therefore DRAINS
//   FIRST -- the inverse of the 2026-08-04 look. This is a KNOWN, FLAGGED consequence of the
//   newer ruling, not an accident: restoring the old look would require baseCap > 32000,
//   which would make the containers pointless. If the owner wants the look back, the fix is
//   a PRESENTATION ordering rule (base store fills last regardless of capacity), never a
//   capacity change -- and that is a design decision, not a bug fix.
//   TownBankCapRegression case [order-intent-pallets-last] still HARD-FAILS a level-1
//   inversion (a container that outgrows the base store on the day it is built) and reports
//   the level-2+ inversion as an explicit note naming the ruling.
//
//   ** THIS IS NOT A PER-CONTAINER WALLET. ** GameState.Resources / GameState.Wood /
//   GameState.Iron remain the SINGLE authority (WO-842 unified them after two authorities
//   produced the captured "985k can't afford 800"). Occupancy here is DERIVED and stored
//   nowhere.
//
// Sec.12: the cap resolution traces [Flow:Bank] (throttled -- it is a hot path) and every
// clamped grant raises an UNTHROTTLED FlowTrace.Warn naming the resource and the units
// lost, plus an Overflowed event for presentation. The warn is load-bearing: it is the
// only thing between the player and silently vaporised resources (WO-901 Sec.5).
// =============================================================================

using System;
using System.Collections.Generic;
using UnityEngine;
using DeNelle.Core.Catalog;
using DeNelle.Core.Diagnostics;
using DeNelle.Core.State;

namespace DeNelle.Core.Economy
{
    /// <summary>
    /// WHY a grant is happening. This is the NAMED exemption axis for the town bank cap -- not an
    /// incidental code path, so the rule can be read, tested, and never re-discovered by accident.
    /// <para>The owner's clamp-and-warn ruling (WO-901 §5) was given about the ECONOMY: what the
    /// player EARNS. It was never a ruling about what the player BUYS. A pack that advertises 5,000
    /// food and delivers 1,920 is not a balance decision, it is selling something and not delivering
    /// it -- a refund/chargeback and a store-policy problem, and no toast makes it acceptable.</para>
    /// </summary>
    public enum BankGrantKind
    {
        /// <summary>
        /// EARNED in-game income: wave rewards, collector collects, quest rewards, raid loot, Echo
        /// dumps, offline harvest, kill trickle. CLAMPED AND WARNED -- the owner's ruling, unchanged.
        /// This is the default and every new income path should use it.
        /// </summary>
        EarnedIncome = 0,

        /// <summary>
        /// A quantity the player PAID FOR or was PROMISED AN EXACT NUMBER OF: IAP / pack-store
        /// entitlements, promo-code redemptions, referral payouts, battle-pass tier rewards.
        /// NEVER CLAMPED -- an advertised quantity always arrives in full. Storage pressure is a
        /// gameplay dial; it must never become a mechanism that under-delivers a purchase.
        /// </summary>
        PurchasedOrPromised = 1,

        /// <summary>
        /// Dev tools and the headless AutoPilot fleet funding a gate they are only walking THROUGH.
        /// NEVER CLAMPED. Never reachable from a player-facing path.
        /// </summary>
        DevHarness = 2,
    }

    /// <summary>The town-bank resource axes. Wood/Iron/Food are storage-capped; Crystals and
    /// Coins are UNCAPPED by design (see <see cref="TownBankCapacity.UncappableResources"/>).</summary>
    public enum BankResource
    {
        Wood = 0,
        Iron = 1,
        [System.Runtime.Serialization.EnumMember(Value = "Food")]
        Stone = 2,
        Crystals = 3,
        Coins = 4,
    }

    /// <summary>
    /// One container in the ordered bank. Slot 0 is always the non-building BASE STORE
    /// (the <c>baseCap</c> baseline that exists before any pallet is built); the rest are
    /// built lumberyard / foundry / silo instances. <see cref="Contents"/> and
    /// <see cref="Fill01"/> are DERIVED from the one authoritative wallet total -- they are
    /// not stored anywhere and must never be written back.
    /// </summary>
    public struct StorageSlot
    {
        /// <summary>Catalog id ("lumberyard"), or empty for the base store.</summary>
        public string StructureId;
        /// <summary>Stable per-instance key: "lumberyard@12,-4" (id + grid cell), or "base".
        /// This is what WO-903's pallet fill-stack matches on to find ITS slot.</summary>
        public string InstanceKey;
        /// <summary>Placed upgrade level (1-based). 1 for the base store.</summary>
        public int Level;
        /// <summary>This container's capacity at its level (units).</summary>
        public int Capacity;
        /// <summary>Derived units currently housed here (0..Capacity).</summary>
        public int Contents;
        /// <summary>Derived Contents/Capacity in 0..1 (0 when Capacity is 0).</summary>
        public float Fill01;
        /// <summary>True for the single non-building baseline slot.</summary>
        public bool IsBaseStore;
        /// <summary>Grid cell of the placed structure (0,0 for the base store) -- part of the tie-break key.</summary>
        public int CellX;
        /// <summary>Grid cell of the placed structure (0,0 for the base store) -- part of the tie-break key.</summary>
        public int CellZ;
    }

    /// <summary>A full derived answer for one resource: the authoritative total, the cap, any
    /// grandfathered overflow, and the ordered container occupancy. Pure -- reading it mutates nothing.</summary>
    public struct BankApportionment
    {
        public BankResource Resource;
        /// <summary>The one authoritative wallet total (GameState).</summary>
        public int Total;
        /// <summary>baseCap + sum of built container capacities. int.MaxValue for an uncapped resource.</summary>
        public int Max;
        /// <summary>max(0, Total - sum of container capacities) -- a grandfathered legacy save that
        /// already held more than the cap. NEVER deleted; it drains by being spent.</summary>
        public int Overflow;
        /// <summary>Containers ordered by CAPACITY ASCENDING (the fill AND drain order), tie-broken
        /// deterministically. Never null; always at least the base store.</summary>
        public StorageSlot[] Slots;
    }

    /// <summary>A clamped-grant event: what was asked for, what fit, and what was lost.</summary>
    public struct BankOverflowStatus
    {
        /// <summary>False until the first clamped grant of the session.</summary>
        public bool Available;
        public BankResource Resource;
        /// <summary>Player-facing resource word ("Wood").</summary>
        public string ResourceName;
        /// <summary>Player-facing container to build ("Lumberyard").</summary>
        public string ContainerName;
        public int Requested;
        public int Granted;
        /// <summary>Units LOST to the cap (Requested - Granted). Always &gt; 0 on a real event.</summary>
        public int Lost;
        public int Max;
        /// <summary>The MEASURED wallet total the grant was weighed against, before anything was
        /// applied. Present so presentation can say "3,400 of 2,000" without re-deriving a number
        /// that could disagree with the one the clamp actually used.</summary>
        public int Current;
        /// <summary>
        /// True when the balance was ALREADY STRICTLY ABOVE the cap when this earned credit arrived
        /// -- the state a purchase legitimately creates under `FOUNDATIONAL_RULINGS.md` section 7.
        /// <para>This is a DIFFERENT SITUATION from a full bank, and the copy must not read the same.
        /// At <c>Current == Max</c> the player is simply full and the fix is more storage. Above
        /// <c>Max</c> the surplus is value they were given in full on purpose, none of it is being
        /// taken away, and the earned faucet resumes on its own once they spend back under. Framing
        /// that as "LOST -- build a bigger lumberyard" reads as a punishment for having paid.</para>
        /// <para>False on the ordinary at-or-below-cap partial fit, which keeps its existing words.</para>
        /// </summary>
        public bool OverCap;
        /// <summary>Short tag naming the income path that overflowed ("Grant", "OfflineHarvest").</summary>
        public string Source;
        /// <summary>Bumps on every publish -- cheap change-detect for a polling HUD
        /// (the ObsidianQueueGate snapshot pattern).</summary>
        public int Version;
    }

    /// <summary>The ONE reader for town bank capacity. Everything queries this; nothing duplicates it.</summary>
    public static class TownBankCapacity
    {
        // ── Law 2: a cap can never resolve to zero ────────────────────────────
        /// <summary>
        /// The absolute floor under any baseCap, applied AFTER the data is read and regardless of
        /// what it says. The most expensive single structure in structures-catalog costs 160 wood /
        /// 100 iron / 60 food, and the cheapest capacity-raising container (lumberyard) costs
        /// 50 wood + 20 iron -- so a wallet this size can always afford the next step out of any
        /// state. A missing, truncated, or zeroed storage-caps.json therefore degrades to a
        /// PLAYABLE save instead of one where every grant clamps to nothing.
        /// </summary>
        public const int AbsoluteMinBaseCap = 1000;

        // ── Law 1: crystals + coins are uncapped BY DESIGN ────────────────────
        /// <summary>
        /// Owner ruling 2026-08-04 (WO-901 Sec.6): premium / bottleneck currency is NEVER
        /// storage-gated. This is the named constant, not a comment -- <see cref="IsCapped"/> reads
        /// it, storage-caps.json cannot override it, and TownBankCapRegression [no-crystal-cap]
        /// fails the build if a crystal or coin cap is ever introduced.
        /// </summary>
        public static readonly BankResource[] UncappableResources =
        {
            BankResource.Crystals,
            BankResource.Coins,
        };

        /// <summary>The base-store slot key (slot 0 of every apportionment).</summary>
        public const string BaseStoreKey = "base";

        /// <summary>Raised on every clamped grant, immediately after the FlowTrace.Warn. Presentation
        /// subscribes (BankOverflowToastPresenter); gameplay never does.</summary>
        public static event Action<BankOverflowStatus> Overflowed;

        /// <summary>Latest clamped-grant snapshot (Available=false until the first one). Poll this
        /// from the HUD -- the ObsidianQueueGate pattern, no cross-assembly read.</summary>
        public static BankOverflowStatus LastOverflow { get; private set; }

        private static int _overflowVersion;

        // =====================================================================
        //  Resource identity
        // =====================================================================

        /// <summary>
        /// Law 5 -- THE ONE PLACE that decides whether a grant is subject to the cap at all.
        /// Only <see cref="BankGrantKind.EarnedIncome"/> is clamped. A purchased or promised
        /// quantity, and dev/harness funding, always land in full.
        /// <para>Pinned by TownBankCapRegression case [purchased-grant-never-clamped]: if this
        /// ever starts returning true for <see cref="BankGrantKind.PurchasedOrPromised"/>, a store
        /// pack can silently under-deliver what the player paid for and the build fails.</para>
        /// </summary>
        public static bool IsClampable(BankGrantKind kind) => kind == BankGrantKind.EarnedIncome;

        /// <summary>
        /// Is this structure a STORAGE CONTAINER (the owner's "pallets" - lumberyard / foundry /
        /// silo)? The one place outside this file that may ask.
        /// -------------------------------------------------------------------------------
        /// Added 2026-08-07 because the first-build 5s grace needs the owner's carve-out ("other
        /// than the pallets") and reached for repo.IsStorageContainer directly - which the
        /// [one-reader] guard correctly flagged. That guard exists so capacity math cannot be
        /// re-derived in two places and disagree.
        ///
        /// This is a CLASSIFICATION passthrough, not capacity math - it answers "is this a
        /// container", never "how much does it hold". Routing it here keeps the raw seam
        /// (repo.storageCapacity / repo.IsStorageContainer) read in exactly one file while letting
        /// callers ask the question they actually have. A future container needs no caller change.
        /// </summary>
        public static bool IsStorageContainer(DeNelle.Core.Catalog.RepoProps repo)
            => repo != null && repo.IsStorageContainer;

        /// <summary>True when this resource has a storage ceiling at all. False for Crystals/Coins,
        /// unconditionally and un-overridably (Law 1).</summary>
        public static bool IsCapped(BankResource r)
        {
            for (int i = 0; i < UncappableResources.Length; i++)
                if (UncappableResources[i] == r) return false;
            return true;
        }

        /// <summary>The lowercase catalog word for a resource -- the same token
        /// structures-catalog authors in <c>repo.storageResource</c>.</summary>
        public static string WordOf(BankResource r)
        {
            switch (r)
            {
                case BankResource.Wood:     return "wood";
                case BankResource.Iron:     return "iron";
                case BankResource.Stone:     return "stone";
                case BankResource.Crystals: return "crystals";
                case BankResource.Coins:    return "coins";
            }
            return "";
        }

        /// <summary>Parse a catalog/save resource word ("wood", "iron", "food", "crystals",
        /// "coins") into a BankResource. Case-insensitive; false when unknown or empty.</summary>
        public static bool TryParseResource(string word, out BankResource r)
        {
            r = BankResource.Wood;
            if (string.IsNullOrEmpty(word)) return false;
            switch (word.Trim().ToLowerInvariant())
            {
                case "wood":          r = BankResource.Wood;     return true;
                case "iron":          r = BankResource.Iron;     return true;
                case "food":          r = BankResource.Stone;     return true;
                case "grain":         r = BankResource.Stone;     return true;
                case "stone":         r = BankResource.Stone;     return true;
                case "crystal":
                case "crystals":
                case "aethercrystal": r = BankResource.Crystals; return true;
                case "coin":
                case "coins":
                case "gold":          r = BankResource.Coins;    return true;
            }
            return false;
        }

        /// <summary>Player-facing resource name (ASCII, text-encoded state -- never colour alone).</summary>
        public static string DisplayName(BankResource r)
        {
            switch (r)
            {
                case BankResource.Wood:     return "Wood";
                case BankResource.Iron:     return "Iron";
                case BankResource.Stone:     return "Stone";
                case BankResource.Crystals: return "Crystals";
                case BankResource.Coins:    return "Gold";
            }
            return "Resource";
        }

        /// <summary>
        /// The player-facing container that raises this resource's cap, resolved FROM THE CATALOG
        /// (the first row whose repo.storageResource matches) so a data rename never strands the
        /// copy. Falls back to the generic word rather than a wrong building name.
        /// </summary>
        public static string ContainerNameFor(BankResource r)
        {
            var entries = CatalogRegistry.All();
            if (entries != null)
            {
                string want = WordOf(r);
                for (int i = 0; i < entries.Count; i++)
                {
                    var e = entries[i];
                    var repo = e != null ? e.repo : null;
                    if (repo == null || !repo.IsStorageContainer) continue;
                    if (!string.Equals(repo.storageResource, want, StringComparison.OrdinalIgnoreCase)) continue;
                    return !string.IsNullOrEmpty(e.displayName) ? e.displayName : e.id;
                }
            }
            return "storage building";
        }

        // =====================================================================
        //  Caps
        // =====================================================================

        /// <summary>
        /// The baseline cap before any container is built. Reads storage-caps.json and then applies
        /// <see cref="AbsoluteMinBaseCap"/> as a hard floor (Law 2). Returns int.MaxValue for an
        /// uncapped resource so callers that forget to check <see cref="IsCapped"/> still cannot
        /// accidentally clamp crystals.
        /// </summary>
        public static int BaseCapOf(BankResource r)
        {
            if (!IsCapped(r)) return int.MaxValue;
            int authored = StorageCapsCatalog.RawBaseCap(WordOf(r));
            if (authored < AbsoluteMinBaseCap)
            {
                FlowTrace.Throttle("Bank", "basecap-floor-" + WordOf(r), 60f,
                    $"baseCap for {WordOf(r)} authored as {authored} -- FLOORED to AbsoluteMinBaseCap {AbsoluteMinBaseCap} " +
                    "(a cap of zero would clamp every grant to nothing and soft-lock the save).");
                return AbsoluteMinBaseCap;
            }
            return authored;
        }

        /// <summary>A container's capacity at a placed level (level is 1-based and clamped &gt;= 1).</summary>
        public static int CapacityAtLevel(int authoredStorageCapacity, int level)
        {
            if (authoredStorageCapacity <= 0) return 0;
            float mult = StorageCapsCatalog.LevelMultiplier(Mathf.Max(1, level));
            return Mathf.Max(authoredStorageCapacity, Mathf.RoundToInt(authoredStorageCapacity * mult));
        }

        /// <summary>
        /// A CATALOG ROW's capacity at a placed level - the overload a caller that holds a
        /// <see cref="DeNelle.Core.Catalog.RepoProps"/> should use. 0 for a null row or a row that
        /// is not a container, so a caller can ask unconditionally.
        /// -------------------------------------------------------------------------------
        /// Added 2026-09-07 for the same reason <see cref="IsStorageContainer(DeNelle.Core.Catalog.RepoProps)"/>
        /// was: the Manage building card needs a container's ceiling at its placed level and at the
        /// next rung, and the only way to reach the int overload above is to write
        /// <c>repo.storageCapacity</c> at the call site - which the [one-reader] guard in
        /// TownBankCapRegression correctly FAILS (Builds/reg-wave4a.log). Routing the field read
        /// back inside this file keeps the raw seam read in exactly ONE place while letting callers
        /// ask the question they actually have. This is not a second capacity formula: it forwards
        /// to the int overload above, which stays the single ladder.
        /// </summary>
        public static int CapacityAtLevel(DeNelle.Core.Catalog.RepoProps repo, int level)
        {
            if (repo == null) return 0;
            return CapacityAtLevel(repo.storageCapacity, level);
        }

        /// <summary>
        /// The town bank ceiling for a resource = baseCap + every BUILT container of that resource,
        /// scaled by its placed level. int.MaxValue when the resource is uncapped.
        /// </summary>
        public static int MaxOf(BankResource r)
        {
            if (!IsCapped(r)) return int.MaxValue;
            var slots = BuildSlots(r, out int containerCapacity);
            int max = BaseCapOf(r) + containerCapacity;
            FlowTrace.Throttle("Bank", "max-" + WordOf(r), 30f,
                $"MaxOf({WordOf(r)}) = base {BaseCapOf(r)} + containers {containerCapacity} across {slots.Length - 1} built container(s) = {max}.");
            return max;
        }

        /// <summary>The one authoritative wallet total for a resource (GameState -- WO-842).
        /// 0 when no save service exists.</summary>
        public static int CurrentOf(BankResource r)
        {
            var state = GameStateService.Instance != null ? GameStateService.Instance.State : null;
            if (state == null) return 0;
            switch (r)
            {
                case BankResource.Wood:     return state.Wood;
                case BankResource.Iron:     return state.Iron;
                case BankResource.Stone:     return state.Resources.Stone;
                case BankResource.Crystals: return state.Resources.Crystals;
                case BankResource.Coins:    return state.Resources.Coins;
            }
            return 0;
        }

        /// <summary>Units of headroom left for a resource given an explicit current total. Never
        /// negative (a grandfathered over-cap save reads 0, it is NOT drained). int.MaxValue when uncapped.</summary>
        public static int RoomFor(BankResource r, int current)
        {
            if (!IsCapped(r)) return int.MaxValue;
            return Mathf.Max(0, MaxOf(r) - Mathf.Max(0, current));
        }

        /// <summary>Live-state overload of <see cref="RoomFor(BankResource,int)"/>.</summary>
        public static int RoomFor(BankResource r) => RoomFor(r, CurrentOf(r));

        /// <summary>
        /// WO-900 SEAM (WO-901 Sec.4). True when the bank can accept at least
        /// <paramref name="amount"/> more units. The collector "tap to collect" tell MUST read this
        /// and say "Bank full" instead of "tap to collect" when it is false -- otherwise the tell
        /// invites a tap that vaporises the pending pool under the clamp-and-warn ruling.
        /// </summary>
        public static bool HasHeadroom(BankResource r, int amount = 1)
        {
            if (!IsCapped(r)) return true;
            return RoomFor(r) >= Mathf.Max(1, amount);
        }

        // =====================================================================
        //  WO-1425 -- THE HONEST REFUSAL. "You cannot hold enough" is a DIFFERENT
        //  situation from "you have not saved up yet", and until now the game said
        //  the same sentence for both.
        // ---------------------------------------------------------------------
        //  THE DEFECT (owner playtest, build 2026.09.06.357599): "some items you
        //  cannot upgrade cause you can not get enough resources to use because of
        //  ceilings". MEASURED at source this session: structures-catalog
        //  'tower_ground_archer' upgradeCost[1] (L2->L3) authors 3150 wood, while a
        //  save with a level-1 lumberyard has MaxOf(Wood) = baseCap 2000 + container
        //  1000 = 3000. The bar sits FULL at 3000/3000 and the refusal reads
        //  "Not enough Wood (3150)" -- indistinguishable from a permanent wall.
        //
        //  There is NO arithmetic dead-end: EconomySinkCapRegression [ceiling]
        //  proves every authored cost fits the L6 ceiling of 34000. The defect is
        //  DISCOVERABILITY -- nothing in the game names the container, the level, or
        //  the capacity that unblocks the cost. This is a PRESENTATION seam: it
        //  reads the ladder and returns words. It changes no cost, no cap, no
        //  multiplier, and it never touches a wallet.
        //
        //  WHY IT LIVES HERE and not in the build UI: this file already owns the
        //  ladder (BaseCapOf + CapacityAtLevel + StorageCapsCatalog.LevelMultiplier)
        //  and the [one-reader] guard exists precisely so capacity math is never
        //  re-derived in a second place. A copy of the ladder in BuildModeController
        //  is the duplicated-state failure this repo keeps paying for.
        // =====================================================================

        /// <summary>
        /// A cost that the town bank CANNOT CURRENTLY HOLD, and the way out. Produced by
        /// <see cref="TryDescribeStorageBlock"/>; rendered by <see cref="StorageBlockMessage"/>.
        /// Pure data -- reading it mutates nothing and reads no wallet.
        /// </summary>
        public struct StorageBlock
        {
            /// <summary>True only when the resource is capped AND <see cref="Amount"/> strictly
            /// exceeds <see cref="CurrentMax"/>. False for Crystals/Coins, unconditionally.</summary>
            public bool Blocked;
            public BankResource Resource;
            /// <summary>Player-facing resource word ("Wood"; "Stone" for BankResource.Stone).</summary>
            public string ResourceName;
            /// <summary>The cost that does not fit.</summary>
            public int Amount;
            /// <summary>MaxOf(Resource) as it stands right now -- what the bank tops out at today.</summary>
            public int CurrentMax;
            /// <summary>Lowest ONE-container level whose total bank cap holds <see cref="Amount"/>,
            /// or -1 when no level on the ladder does (a genuine dead end -- say so, never invent
            /// a level). 0 would mean "the base store already holds it", which cannot co-occur
            /// with Blocked.</summary>
            public int RequiredContainerLevel;
            /// <summary>Total bank capacity at <see cref="RequiredContainerLevel"/> (baseCap +
            /// that container's capacity). 0 when the level is -1.</summary>
            public int CapacityAtRequiredLevel;
            /// <summary>Total bank capacity with one container at the top of its ladder.</summary>
            public int LadderCeiling;
            /// <summary>Player-facing container name ("Lumberyard"), or null when the catalog is
            /// not loaded and the row cannot be resolved -- the copy degrades honestly.</summary>
            public string ContainerName;
        }

        /// <summary>
        /// PURE ladder math -- no catalog read, no game state, no allocation. The lowest ONE-container
        /// level (0 = none needed) whose TOTAL bank cap holds <paramref name="amount"/>, and that
        /// capacity. Returns -1 / 0 when no level on the ladder reaches it.
        ///
        /// <para>"ONE container" is the same conservative bound EconomySinkCapRegression uses: nothing
        /// stops a player placing a second lumberyard, so the true bank is unbounded -- but telling the
        /// player "build a second one" is an undiscoverable workaround, which is the very failure this
        /// helper exists to end. Naming the LEVEL of the container they already have is actionable.</para>
        ///
        /// <para>Driven directly by EconomySinkCapRegression [cap-copy-ladder] with explicit unit /
        /// maxLevel arguments, so the sentence the player reads and the number the gate proved come
        /// from ONE loop over ONE multiplier ladder.</para>
        /// </summary>
        public static int RequiredContainerLevel(BankResource r, int amount, int containerUnit,
                                                 int maxContainerLevel, out int capacityAtLevel)
        {
            capacityAtLevel = 0;
            if (!IsCapped(r)) return 0;                       // Law 1 -- never a cap story for crystals/coins
            int baseCap = BaseCapOf(r);
            if (amount <= baseCap) { capacityAtLevel = baseCap; return 0; }
            if (containerUnit <= 0) return -1;

            int top = Mathf.Clamp(maxContainerLevel, 1, RepoProps.MaxStructureLevel);
            for (int level = 1; level <= top; level++)
            {
                int cap = baseCap + CapacityAtLevel(containerUnit, level);
                if (cap >= amount) { capacityAtLevel = cap; return level; }
            }
            return -1;
        }

        /// <summary>
        /// Resolve the storage container row that backs a resource, FROM THE CATALOG (the same walk
        /// <see cref="ContainerNameFor"/> does) -- display name, authored storageCapacity, and the
        /// row's own maxLevel bounded by <see cref="RepoProps.MaxStructureLevel"/>.
        /// <para>False when the catalog is not loaded or authors no container for the resource. That
        /// is a real condition (an editor batch with an unpopulated CatalogRegistry), so it is TRACED
        /// and the caller degrades to a message with no container name -- never a guessed one.</para>
        /// </summary>
        public static bool TryGetContainerRow(BankResource r, out string displayName,
                                              out int storageCapacity, out int maxLevel)
        {
            displayName = null;
            storageCapacity = 0;
            maxLevel = 0;
            if (!IsCapped(r)) return false;

            var entries = CatalogRegistry.All();
            if (entries == null || entries.Count == 0)
            {
                FlowTrace.Throttle("Bank", "container-row-nocatalog-" + WordOf(r), 60f,
                    $"TryGetContainerRow({WordOf(r)}): CatalogRegistry is empty -- the cap-block copy " +
                    "will name no container. NOT a guess: a wrong building name is worse than none.");
                return false;
            }

            string want = WordOf(r);
            for (int i = 0; i < entries.Count; i++)
            {
                var e = entries[i];
                var repo = e != null ? e.repo : null;
                if (repo == null || !repo.IsStorageContainer) continue;
                if (!string.Equals(repo.storageResource, want, StringComparison.OrdinalIgnoreCase)) continue;
                if (repo.storageCapacity <= 0) continue;

                displayName = !string.IsNullOrEmpty(e.displayName) ? e.displayName : e.id;
                storageCapacity = repo.storageCapacity;
                maxLevel = Mathf.Clamp(repo.maxLevel, 1, RepoProps.MaxStructureLevel);
                return true;
            }

            FlowTrace.Throttle("Bank", "container-row-missing-" + WordOf(r), 60f,
                $"TryGetContainerRow({WordOf(r)}): no catalog row authors IsStorageContainer for this " +
                "resource, so no container level can be named.");
            return false;
        }

        /// <summary>
        /// THE SEAM every affordability refusal must consult. True when <paramref name="amount"/> of
        /// <paramref name="r"/> is MORE THAN THE BANK CAN CURRENTLY HOLD -- i.e. saving up can never
        /// close the gap, only more storage can. False for an ordinary shortfall (the player just has
        /// not banked it yet) and false, always, for Crystals/Coins.
        ///
        /// <para>PURE: reads caps and the catalog, writes nothing.</para>
        /// </summary>
        public static bool TryDescribeStorageBlock(BankResource r, int amount, out StorageBlock block)
        {
            block = default;
            block.Resource = r;
            block.ResourceName = DisplayName(r);
            block.Amount = amount;
            block.RequiredContainerLevel = -1;

            if (amount <= 0) return false;
            if (!IsCapped(r)) return false;                  // Law 1

            int max = MaxOf(r);
            block.CurrentMax = max;
            if (amount <= max) return false;                 // an ordinary shortfall -- keep the ordinary words

            block.Blocked = true;

            if (TryGetContainerRow(r, out string name, out int unit, out int rowMaxLevel))
            {
                block.ContainerName = name;
                block.RequiredContainerLevel =
                    RequiredContainerLevel(r, amount, unit, rowMaxLevel, out int capAtLevel);
                block.CapacityAtRequiredLevel = capAtLevel;
                block.LadderCeiling = BaseCapOf(r) + CapacityAtLevel(unit, rowMaxLevel);
            }

            FlowTrace.Throttle("Bank", "cap-block-" + WordOf(r), 15f,
                $"CAP BLOCK {block.ResourceName}: a cost of {amount} exceeds the bank ceiling of {max}. " +
                $"Way out = {(block.ContainerName ?? "a storage building")} at level " +
                $"{block.RequiredContainerLevel} (holds {block.CapacityAtRequiredLevel}). " +
                "This is a DISCOVERABILITY fix (WO-1425), not a balance change.");
            return true;
        }

        /// <summary>
        /// The player-facing sentence for a cap block, or "" when the cost is not cap-blocked (the
        /// caller keeps its ordinary shortfall copy). ASCII only; the WORDS carry the state, never a
        /// colour (the owner is red/green colourblind).
        /// <para>Three shapes, because three different things are true and one sentence cannot say
        /// all of them honestly:</para>
        /// <list type="bullet">
        /// <item>a level on the ladder holds it -> name the container, the level and the capacity;</item>
        /// <item>NO level holds it -> say so plainly; that is a real dead end and inventing a level
        /// would send the player up a ladder that does not reach;</item>
        /// <item>the container row is unresolvable -> state the ceiling and the cost without naming a
        /// building. A missing name is honest; a wrong one is not.</item>
        /// </list>
        /// </summary>
        public static string StorageBlockMessage(BankResource r, int amount)
        {
            if (!TryDescribeStorageBlock(r, amount, out var b)) return "";
            return DescribeStorageBlock(b);
        }

        /// <summary>Render an already-resolved <see cref="StorageBlock"/>. Pure, allocation-light, and
        /// separately testable from the resolution above.</summary>
        public static string DescribeStorageBlock(StorageBlock b)
        {
            if (!b.Blocked) return "";
            string res = b.ResourceName ?? "Resource";
            string tops = $"Your {res} storage tops out at {N(b.CurrentMax)}.";

            if (string.IsNullOrEmpty(b.ContainerName))
                return $"{tops} This costs {N(b.Amount)} - upgrade your storage to hold more.";

            if (b.RequiredContainerLevel <= 0)
                return $"{tops} This costs {N(b.Amount)}, more than any {b.ContainerName} level holds " +
                       $"(the ladder tops out at {N(b.LadderCeiling)}).";

            return $"Needs a {b.ContainerName} at level {b.RequiredContainerLevel} " +
                   $"(holds {N(b.CapacityAtRequiredLevel)}). {tops}";
        }

        /// <summary>Thousands-separated, culture-invariant ("10,000"). ASCII.</summary>
        private static string N(int v) => v.ToString("N0", System.Globalization.CultureInfo.InvariantCulture);

        // =====================================================================
        //  The clamp (Law 3 + the load-bearing warn)
        // =====================================================================

        /// <summary>
        /// THE clamp. Returns how much of <paramref name="requested"/> actually fits in the bank on
        /// top of <paramref name="current"/>, and reports the units LOST.
        ///
        /// <para>PURE with respect to the wallet -- it writes nothing. Callers apply the returned
        /// amount. <paramref name="current"/> is passed in rather than read so the EconomyService
        /// fallback pool (no GameState) clamps against its own store and so the function is
        /// unit-testable without a save service.</para>
        ///
        /// <para>A NON-POSITIVE request is returned UNCHANGED (Law 3) -- a spend routed through an
        /// "add" API must never be upper-clamped. An UNCAPPED resource is returned unchanged.</para>
        ///
        /// <para>When anything is lost this raises an UNTHROTTLED <c>FlowTrace.Warn</c> naming the
        /// resource and the amount, publishes <see cref="LastOverflow"/>, and fires
        /// <see cref="Overflowed"/>. That warn is the only thing standing between the player and
        /// silently vaporised resources (WO-901 Sec.5) -- it must never be swallowed or throttled.</para>
        /// </summary>
        public static int ClampGrant(BankResource r, int current, int requested, string sourceTag, out int lost)
        {
            lost = 0;
            if (requested <= 0) return requested;      // Law 3 -- spends and no-ops pass straight through
            if (!IsCapped(r)) return requested;        // Law 1 -- crystals/coins are never clamped

            // One capacity resolution for the whole call -- Grant is a hot path (per-kill trickle,
            // harvest ticks), so do NOT re-walk the layout for the room, the max and the trace.
            int max = MaxOf(r);
            int room = Mathf.Max(0, max - Mathf.Max(0, current));
            if (requested <= room) return requested;

            int granted = room;
            lost = requested - granted;

            string resName = DisplayName(r);
            string container = ContainerNameFor(r);

            // WO-1191 -- WHICH SITUATION IS THIS? Two different things reach this line and they must
            // not narrate the same. `FOUNDATIONAL_RULINGS.md` section 7 governs; cite it, never
            // restate it.
            //   current <= max : the ordinary full/partly-full bank. Existing words, unchanged.
            //   current >  max : the balance is ALREADY above capacity -- the state a purchase
            //                    legitimately creates. Nothing is being taken from the player here;
            //                    an earned credit simply does not add while they are up there, and
            //                    it starts adding again by itself once they spend back under. The
            //                    old "LOST N -- build a bigger container" sentence, aimed at that
            //                    state, reads as a penalty for having paid.
            bool overCap = Mathf.Max(0, current) > max;

            // Sec.12 -- NEVER throttled, NEVER swallowed. If this line is missing, resources
            // vanished silently and that is the defect the owner's ruling is one line away from.
            if (overCap)
            {
                FlowTrace.Warn("Bank",
                    $"OVER CAPACITY [{sourceTag ?? "?"}] {resName}: earned {requested}, added 0 " +
                    $"(wallet {current}/{max} -- {current - max} above capacity). Earned income does not add " +
                    $"while this resource is above its cap and NOTHING is held or queued; it resumes on its " +
                    $"own once the balance falls back under {max}. `FOUNDATIONAL_RULINGS.md` section 7.");
            }
            else
            {
                FlowTrace.Warn("Bank",
                    $"BANK FULL [{sourceTag ?? "?"}] {resName}: requested {requested}, banked {granted}, " +
                    $"LOST {lost} (wallet {current}/{max}). Build or upgrade a {container}, or spend {resName.ToLowerInvariant()}.");
            }

            var status = new BankOverflowStatus
            {
                Available = true,
                Resource = r,
                ResourceName = resName,
                ContainerName = container,
                Requested = requested,
                Granted = granted,
                Lost = lost,
                Max = max,
                Current = Mathf.Max(0, current),
                OverCap = overCap,
                Source = sourceTag ?? "?",
                Version = ++_overflowVersion,
            };
            LastOverflow = status;

            var handler = Overflowed;
            if (handler != null)
            {
                // Guard so a bad presentation subscriber can never swallow or abort the income path.
                Guard.Try("Bank", "raise Overflowed", () => handler(status));
            }

            return granted;
        }

        // =====================================================================
        //  Apportionment -- the derived per-container occupancy (WO-903 seam)
        // =====================================================================

        /// <summary>
        /// PURE fill -- and, because it is a pure function of the total, PURE DRAIN as well.
        /// Distributes <paramref name="total"/> across <paramref name="slots"/> in the order given
        /// -- which MUST already be capacity-ascending (<see cref="OrderSlots"/>) -- filling each to
        /// its capacity before moving outward, and reports what did not fit.
        ///
        /// <para>Draining is THIS SAME FUNCTION evaluated at a lower total. There is deliberately no
        /// separate drain path and no stored per-container balance: two code paths is exactly how the
        /// pallets end up showing a state the wallet does not agree with.</para>
        /// </summary>
        public static void Fill(int total, StorageSlot[] slots, out int overflow)
        {
            int remaining = Mathf.Max(0, total);
            if (slots != null)
            {
                for (int i = 0; i < slots.Length; i++)
                {
                    int cap = Mathf.Max(0, slots[i].Capacity);
                    int take = Mathf.Min(remaining, cap);
                    slots[i].Contents = take;
                    slots[i].Fill01 = cap > 0 ? (float)take / cap : 0f;
                    remaining -= take;
                }
            }
            overflow = remaining;
        }

        /// <summary>
        /// The ordering law (owner 2026-08-04): CAPACITY ASCENDING. The smallest container fills
        /// FIRST and therefore drains LAST; the largest fills last and drains first. Capacity --
        /// never current contents -- because capacity only changes when a building is placed or
        /// upgraded, so the order is stable and the props cannot flicker as balances move. Ties
        /// break on a stable key (base store first, then catalog id, then cell X, then cell Z),
        /// never on dictionary or FindObjectsByType iteration order.
        /// <para>The owner's stated outcome ("pallets drain last") therefore requires the pallets to
        /// be the SMALLER containers -- see the file header and case [order-intent-pallets-last].</para>
        /// </summary>
        public static void OrderSlots(StorageSlot[] slots)
        {
            if (slots == null || slots.Length < 2) return;
            Array.Sort(slots, CompareSlots);
        }

        private static int CompareSlots(StorageSlot a, StorageSlot b)
        {
            int c = a.Capacity.CompareTo(b.Capacity);
            if (c != 0) return c;
            // Deterministic tie-break -- the base store always sorts ahead of a same-capacity pallet.
            c = (a.IsBaseStore ? 0 : 1).CompareTo(b.IsBaseStore ? 0 : 1);
            if (c != 0) return c;
            c = string.CompareOrdinal(a.StructureId ?? "", b.StructureId ?? "");
            if (c != 0) return c;
            c = a.CellX.CompareTo(b.CellX);
            if (c != 0) return c;
            c = a.CellZ.CompareTo(b.CellZ);
            if (c != 0) return c;
            return string.CompareOrdinal(a.InstanceKey ?? "", b.InstanceKey ?? "");
        }

        /// <summary>
        /// WO-903 SEAM. The full derived answer for one resource: authoritative total, cap,
        /// grandfathered overflow, and per-container occupancy in fill/drain order.
        ///
        /// <para>A pallet fill-stack finds ITS slot by <see cref="StorageSlot.InstanceKey"/>
        /// (<c>"lumberyard@12,-4"</c>) or by <see cref="TryGetSlot"/>, then reads
        /// <see cref="StorageSlot.Fill01"/> -- e.g. <c>steps = Mathf.RoundToInt(slot.Fill01 * 20)</c>.
        /// Nothing is stored per container; call this again and it re-derives.</para>
        /// </summary>
        public static BankApportionment Apportion(BankResource r)
        {
            int total = CurrentOf(r);
            var slots = BuildSlots(r, out int containerCapacity);
            OrderSlots(slots);
            Fill(total, slots, out int overflow);
            return new BankApportionment
            {
                Resource = r,
                Total = total,
                Max = IsCapped(r) ? BaseCapOf(r) + containerCapacity : int.MaxValue,
                Overflow = overflow,
                Slots = slots,
            };
        }

        /// <summary>
        /// WHAT-IF seam. The same derived answer as <see cref="Apportion(BankResource)"/> but at a
        /// HYPOTHETICAL total (<c>current + delta</c>, floored at 0) -- so a caller can ask
        /// "if N units are removed, which containers empty and by how much?" without touching the
        /// wallet. Intended readers: the WO-903 pallet fill-stacks (to animate a drop) and a future
        /// raid-steal presentation pass.
        ///
        /// <para>IMPORTANT: this is a PREVIEW, never a mutation. An actual steal or spend must move
        /// the ONE authoritative total (GameState) exactly like any other spend --
        /// <c>total -= amount</c> -- and let the apportionment re-derive. There is no per-container
        /// balance to debit and none may be introduced (WO-842: two authorities produced the
        /// captured "985k can't afford 800").</para>
        /// </summary>
        public static BankApportionment Preview(BankResource r, int delta)
        {
            int hypothetical = Mathf.Max(0, CurrentOf(r) + delta);
            var slots = BuildSlots(r, out int containerCapacity);
            OrderSlots(slots);
            Fill(hypothetical, slots, out int overflow);
            return new BankApportionment
            {
                Resource = r,
                Total = hypothetical,
                Max = IsCapped(r) ? BaseCapOf(r) + containerCapacity : int.MaxValue,
                Overflow = overflow,
                Slots = slots,
            };
        }

        /// <summary>Find one container's derived state by its stable instance key. False when the
        /// key names no live container (sold / never built).</summary>
        public static bool TryGetSlot(BankResource r, string instanceKey, out StorageSlot slot)
        {
            slot = default;
            if (string.IsNullOrEmpty(instanceKey)) return false;
            var a = Apportion(r);
            for (int i = 0; i < a.Slots.Length; i++)
            {
                if (string.Equals(a.Slots[i].InstanceKey, instanceKey, StringComparison.OrdinalIgnoreCase))
                {
                    slot = a.Slots[i];
                    return true;
                }
            }
            return false;
        }

        /// <summary>Build the stable instance key for a placed container. Public so WO-903 can build
        /// the same key from its own PlacedStructure without duplicating the format.</summary>
        public static string InstanceKeyOf(string itemId, int cellX, int cellZ)
            => $"{itemId}@{cellX},{cellZ}";

        // =====================================================================
        //  Built-container enumeration -- the SAME authority the build/sell seam uses
        // =====================================================================

        /// <summary>
        /// The base store plus every BUILT storage container for this resource.
        ///
        /// <para>"BUILT" is <c>GameState.BaseLayout</c> (the placed-structure record the ONE commit
        /// seam writes at BuildModeController.cs:1888-1892, that
        /// StrategicPlacementMigration converts template towns into, and that sell REMOVES) AND
        /// <c>GameState.HasEverBuilt</c> (the monotonic WO-834 v36 ledger). Both are existing
        /// notions -- no third one is invented, and the P0 that let ResourceCollectorBootstrap
        /// create income for an unbuilt building by consulting only a registry is not repeated.
        /// BaseLayout is what makes the cap fall again when a container is SOLD; the ever-built
        /// co-gate is what stops anything that fabricates a layout row from opening the seam.</para>
        /// </summary>
        private static StorageSlot[] BuildSlots(BankResource r, out int containerCapacity)
        {
            containerCapacity = 0;
            var list = new List<StorageSlot>(4)
            {
                new StorageSlot
                {
                    StructureId = "",
                    InstanceKey = BaseStoreKey,
                    Level = 1,
                    Capacity = IsCapped(r) ? BaseCapOf(r) : 0,
                    IsBaseStore = true,
                },
            };

            if (!IsCapped(r)) return list.ToArray();

            var state = GameStateService.Instance != null ? GameStateService.Instance.State : null;
            var layout = state != null ? state.BaseLayout : null;
            if (layout == null) return list.ToArray();

            string want = WordOf(r);
            for (int i = 0; i < layout.Count; i++)
            {
                var d = layout[i];
                if (string.IsNullOrEmpty(d.itemId)) continue;

                var entry = CatalogRegistry.Get(d.itemId);
                var repo = entry != null ? entry.repo : null;
                if (repo == null || !repo.IsStorageContainer) continue;
                if (!string.Equals(repo.storageResource, want, StringComparison.OrdinalIgnoreCase)) continue;
                if (state != null && !state.HasEverBuilt(d.itemId)) continue;   // existence co-gate

                int level = Mathf.Max(1, d.level);
                int cap = CapacityAtLevel(repo.storageCapacity, level);
                if (cap <= 0) continue;

                containerCapacity += cap;
                list.Add(new StorageSlot
                {
                    StructureId = d.itemId,
                    InstanceKey = InstanceKeyOf(d.itemId, d.cellX, d.cellZ),
                    Level = level,
                    Capacity = cap,
                    IsBaseStore = false,
                    CellX = d.cellX,
                    CellZ = d.cellZ,
                });
            }

            return list.ToArray();
        }
    }
}

// =============================================================================
// BarracksProgression — the PURE, side-effect-free decision + mutation core for
// the WO-771.9 Barracks & troop-upgrade progression (integration half).
// -----------------------------------------------------------------------------
// Assembly: DeNelle.Village   Namespace: DeNelle.Village
//
// This is the headlessly UNIT-TESTABLE brain the live BarracksService (singletons)
// and the three IJobEffect handlers delegate to. It reads the committed catalogs
// (BarracksCatalog / TroopUpgradeCatalog / TroopCatalog) and mutates a passed
// GameState directly — NO GameStateService / EconomyService / BuildTimerService
// reference, so a test drives it with a `ScriptableObject.CreateInstance<GameState>()`
// and a FakeEconomy (mirrors ObsidianQueueEngine / TroopTrainingVM purity).
//
// GATING RECONCILE (WO-771.9 §3): a troop is unlocked when EITHER encoding says so —
//   • barracks.json  : the troop id appears in unlocksTroopIds for some level <= L, OR
//   • troops.json    : the troop's UnlockBarracksTier <= L.
// The two agree by construction (barracks level N unlocks the troop whose
// UnlockBarracksTier == N); the union is defensive against a one-sided authoring gap.
//
// ⛔ OWNER RULING 21 (2026-09-06) — WHERE `L` COMES FROM CHANGED, AND THAT IS THE WHOLE FIX.
// `L` is now EffectiveBarracksLevelOf(state) = MAX(the legacy stored GameState.BarracksLevel,
// GameState.BuildingTiers["barracks"]), floored to 1 — i.e. THE BARRACKS BUILDING TIER GATES
// TROOP UNLOCKS. Before the merge `L` was the stored field alone, which no reachable code path
// could ever raise, so seven of the nine troops were locked forever while the player upgraded a
// barracks BUILDING that did nothing for the army. Two numbers spelled the same way on different
// scales — identical in shape to the village-tier defect fixed the same day (WO-1423).
// The stored key is read-migrated, never deleted (CLAUDE.md §8); see EffectiveBarracksLevelOf.
//
// TROOP UPGRADE COST/TIME: troop-upgrades.json carries stat CURVES + abilities only,
// NOT per-level cost/time. The troop-upgrade TIME is DERIVED from the troop's base
// BuildSeconds scaled by the target level (placeholder curve; WO-771.14 owns balance).
//
// WO-1387 (owner ruling 2026-09-04 23:16): a troop upgrade has NO resource/gold cost -
// TroopUpgradeCost returns an EMPTY basket and TroopUpgradeSeconds is the ONLY price.
// "we agreed earlier training free ... just time ... and gold is to hire mercenaries if
// they dont want to wait". The previous CostGold*level curve (commit 281902df0) mis-read
// her; do not restore it. Gold is spent ONLY through BuildTimerService.TryInstantFinish
// (FinishPaysGold = TrainTroop) to skip the clock.
// =============================================================================

using System.Collections.Generic;
using UnityEngine;
using DeNelle.Core.State;

namespace DeNelle.Village
{
    /// <summary>
    /// Pure decision + mutation helpers for the Barracks / troop-upgrade progression.
    /// Every method is side-effect-free except the explicit <c>Apply*</c> / <c>Grant*</c>
    /// mutators, which mutate ONLY the <see cref="GameState"/> they are handed.
    /// </summary>
    public static class BarracksProgression
    {
        /// <summary>Day-one barracks level (unlocks the starter roster).</summary>
        public const int DefaultBarracksLevel = 1;
        /// <summary>Baseline troop upgrade level (pure baseline — no curve applied).</summary>
        public const int DefaultTroopLevel = 1;

        // ── GameState reads (default-on-read; null-safe) ──────────────────────

        /// <summary>
        /// The RAW stored <c>GameState.BarracksLevel</c>, floored to 1 (1 with no state).
        ///
        /// <para>⛔ OWNER RULING 21 (2026-09-06, OWNER_RULINGS_LOCKED.md §21): <b>this is NOT the
        /// troop-unlock gate any more.</b> It is a FLOOR that feeds
        /// <see cref="EffectiveBarracksLevelOf"/>, and nothing else may compare a troop against it.
        /// The stored field had exactly one writer - <see cref="ApplyBarracksUpgrade"/>, the
        /// completion effect of a BarracksUpgrade job composed only by BarracksPanelVM, reachable
        /// only from BarracksPanel.ShowBarracksUI, which had ZERO CALLERS (verified 2026-09-06 by
        /// source grep AND a script-GUID search of every .unity/.prefab/.asset: no hits for
        /// b245a5682900ee14cbff23be363845d3; BOTH FILES WERE DELETED the same day by WO-1430 Group A,
        /// so those names are history, not paths). So it sat at its founding value of 1 forever
        /// (GameStateService.cs:1235) and seven of the nine troops were unreachable by any player
        /// action. Ask <see cref="EffectiveBarracksLevelOf"/> instead.</para>
        /// </summary>
        public static int BarracksLevelOf(GameState state)
        {
            if (state == null) return DefaultBarracksLevel;
            return state.BarracksLevel < DefaultBarracksLevel ? DefaultBarracksLevel : state.BarracksLevel;
        }

        /// <summary>
        /// The BARRACKS BUILDING TIER held on <paramref name="state"/> - the ladder the player
        /// actually upgrades in Manage (<c>GameState.BuildingTiers["barracks"]</c>). 0 when the
        /// barracks is unbuilt or was never written a tier.
        ///
        /// <para>Read from the PASSED state rather than through
        /// <c>ModifierService.TierOf("barracks")</c> so this class stays pure and headlessly
        /// testable (its whole contract is "mutates only the GameState it is handed"). The two are
        /// the same number: <c>ModifierService.TierOf</c> (ModifierService.cs:42-48) reads exactly
        /// this dictionary off <c>GameStateService.Instance.State</c>, and the dev override affects
        /// <c>ModifierService.Active</c>, not <c>TierOf</c>.</para>
        /// </summary>
        public static int BarracksBuildingTierOf(GameState state)
        {
            if (state == null || state.BuildingTiers == null) return 0;
            return state.BuildingTiers.TryGetValue(BarracksBuildingId, out int tier) ? tier : 0;
        }

        /// <summary>The building id of the barracks ladder in building-tiers.json.</summary>
        public const string BarracksBuildingId = "barracks";

        /// <summary>
        /// ⭐ THE ONE TROOP-UNLOCK LEVEL (owner ruling 21, 2026-09-06: <i>"Merge them - the building
        /// tier gates troops."</i>). The barracks BUILDING TIER, floored by the legacy stored
        /// <see cref="BarracksLevelOf"/> value and by 1.
        ///
        /// <para><b>THE MIGRATION RULE, stated so it survives:</b> the legacy save key
        /// <c>barracksLevel</c> is <b>READ-MIGRATED, NEVER DELETED AND NEVER WRITTEN BACK</b>. We take
        /// <c>MAX(stored, buildingTier)</c>, so <b>a player who somehow holds a stored level keeps
        /// it</b> even if the building tier is lower. Chosen because:
        /// <list type="number">
        /// <item>it cannot regress ANY existing save - the only direction the number can move is up,
        /// which is the whole reason CLAUDE.md §8 forbids dropping a live key without a migration;</item>
        /// <item>no reachable writer ever raised the field, so on real saves the stored value is 1 and
        /// the MAX is a no-op - the merge is behaviour-neutral for every player who has ever played;</item>
        /// <item>the fixtures that DO set it to 3 (UICaptureLaunch.cs:6998/7085/7479 and three
        /// regression suites) keep working unchanged;</item>
        /// <item>writing the merged value back would be a silent save mutation on load, which is a
        /// far worse failure mode than carrying a dead-but-harmless key.</item>
        /// </list>
        /// The floor of 1 is unchanged behaviour: a usable Barracks always trains the day-one
        /// Footman + Archer even when its tier was never written.</para>
        /// </summary>
        public static int EffectiveBarracksLevelOf(GameState state)
        {
            int stored = BarracksLevelOf(state);          // already floored to 1
            int tier = BarracksBuildingTierOf(state);
            int effective = tier > stored ? tier : stored;
            return effective < DefaultBarracksLevel ? DefaultBarracksLevel : effective;
        }

        /// <summary>
        /// The top authored tier of the barracks BUILDING ladder (building-tiers.json), which after
        /// ruling 21 is the ceiling every troop unlock must sit at or below. 0 when the catalog
        /// failed to load. Pinned by TroopReachabilityRegression.
        /// </summary>
        public static int MaxBarracksBuildingTier => BuildingTierCatalog.MaxTier(BarracksBuildingId);

        /// <summary>A troop's upgrade level on <paramref name="state"/>, floored to 1 (baseline when absent).</summary>
        public static int TroopLevelOf(GameState state, string troopId)
        {
            if (state == null || state.TroopLevels == null || string.IsNullOrEmpty(troopId)) return DefaultTroopLevel;
            if (state.TroopLevels.TryGetValue(troopId, out int lvl))
                return lvl < DefaultTroopLevel ? DefaultTroopLevel : lvl;
            return DefaultTroopLevel;
        }

        // ── Barracks ladder ───────────────────────────────────────────────────

        /// <summary>The highest authored barracks level (0 when the catalog failed to load).</summary>
        public static int MaxBarracksLevel => BarracksCatalog.MaxLevel;

        /// <summary>The barracks def for the NEXT level above <paramref name="currentLevel"/>, or null at max.</summary>
        public static BarracksDef NextBarracksDef(int currentLevel) => BarracksCatalog.Find(currentLevel + 1);

        /// <summary>True when a higher barracks level is authored above <paramref name="currentLevel"/>.</summary>
        public static bool HasNextBarracksLevel(int currentLevel) => NextBarracksDef(currentLevel) != null;

        /// <summary>Resource cost to reach the next barracks level (zero when none/at max).</summary>
        public static ResourceCost BarracksUpgradeCost(int currentLevel)
        {
            var def = NextBarracksDef(currentLevel);
            return def != null ? def.Cost : new ResourceCost();
        }

        /// <summary>Wall-clock seconds the next barracks upgrade takes (0 when none/at max).</summary>
        public static float BarracksUpgradeSeconds(int currentLevel)
        {
            var def = NextBarracksDef(currentLevel);
            return def != null ? Mathf.Max(0f, def.BuildTimeSeconds) : 0f;
        }

        // ── Troop unlock gating (reconcile barracks.json ↔ troops.json) ────────

        /// <summary>
        /// True when <paramref name="troopId"/> is unlocked at barracks level
        /// <paramref name="barracksLevel"/> — by EITHER the troops.json UnlockBarracksTier OR the
        /// barracks.json unlocksTroopIds union (see the class header). Unknown id → false.
        /// </summary>
        public static bool IsTroopUnlocked(string troopId, int barracksLevel)
        {
            if (string.IsNullOrEmpty(troopId)) return false;

            // troops.json encoding.
            var def = TroopCatalog.Find(troopId);
            if (def != null && def.UnlockBarracksTier <= barracksLevel) return true;

            // barracks.json encoding — is it listed at any level we have reached?
            foreach (var lvl in BarracksCatalog.All)
            {
                if (lvl == null || lvl.Level > barracksLevel || lvl.UnlocksTroopIds == null) continue;
                foreach (var id in lvl.UnlocksTroopIds)
                    if (id == troopId) return true;
            }
            return false;
        }

        /// <summary>
        /// The barracks level that unlocks <paramref name="troopId"/> — troops.json
        /// UnlockBarracksTier first, else the lowest barracks.json level listing it, else 1.
        /// </summary>
        public static int UnlockLevelFor(string troopId)
        {
            var def = TroopCatalog.Find(troopId);
            if (def != null && def.UnlockBarracksTier > 0) return def.UnlockBarracksTier;

            int best = int.MaxValue;
            foreach (var lvl in BarracksCatalog.All)
            {
                if (lvl == null || lvl.UnlocksTroopIds == null) continue;
                foreach (var id in lvl.UnlocksTroopIds)
                    if (id == troopId && lvl.Level < best) best = lvl.Level;
            }
            return best == int.MaxValue ? 1 : best;
        }

        // ── Per-troop upgrade ladder ───────────────────────────────────────────

        /// <summary>The highest upgrade level a troop's curves define (1 when it has no upgrade row).</summary>
        public static int MaxTroopLevel(string troopId)
        {
            var upg = TroopUpgradeCatalog.Find(troopId);
            if (upg == null) return 1;
            int reachLen = upg.Reach != null && upg.Reach.Values != null ? upg.Reach.Values.Length : 0;
            int strLen = upg.Strength != null && upg.Strength.Values != null ? upg.Strength.Values.Length : 0;
            int max = reachLen > strLen ? reachLen : strLen;
            return max < 1 ? 1 : max;
        }

        /// <summary>True when <paramref name="troopId"/> can climb above <paramref name="currentLevel"/>.</summary>
        public static bool HasNextTroopLevel(string troopId, int currentLevel) =>
            currentLevel < MaxTroopLevel(troopId);

        /// <summary>
        /// The resource cost to upgrade <paramref name="troopId"/> TO <paramref name="targetLevel"/>:
        /// ALWAYS EMPTY. WO-1387, owner 2026-09-04 23:16, verbatim: "we agreed earlier training
        /// free" / "just time" / "and gold is to hire mercenaries if they dont want to wait".
        /// The only price of a troop upgrade is <see cref="TroopUpgradeSeconds"/>; the only gold
        /// spend is the instant-finish skip in BuildTimerService. This used to return
        /// <c>coins: CostGold * targetLevel</c> (commit 281902df0, "the last CLI did bad changes") -
        /// TroopDef.CostGold STAYS on the row as the raid-reward anchor / mercenary-hire basis and
        /// is never charged here. Pinned by TrainingCostsTimeOnlyRegression.
        /// </summary>
        public static ResourceCost TroopUpgradeCost(string troopId, int targetLevel)
        {
            return new ResourceCost();
        }

        /// <summary>
        /// DERIVED wall-clock seconds to upgrade <paramref name="troopId"/> to
        /// <paramref name="targetLevel"/> — base train time × target level × 2 (placeholder;
        /// WO-771.14 tunes). Floored to a small minimum so a card never shows an instant upgrade.
        /// </summary>
        public static float TroopUpgradeSeconds(string troopId, int targetLevel)
        {
            var def = TroopCatalog.Find(troopId);
            float baseSec = def != null && def.BuildSeconds > 0f ? def.BuildSeconds : 30f;
            int m = targetLevel < 1 ? 1 : targetLevel;
            return Mathf.Max(15f, baseSec * m * 2f);
        }

        /// <summary>
        /// The NEXT special ability <paramref name="troopId"/> will unlock above
        /// <paramref name="currentLevel"/> (lowest threshold strictly greater), or null when none remain.
        /// </summary>
        public static AbilityUnlock NextAbility(string troopId, int currentLevel)
        {
            var upg = TroopUpgradeCatalog.Find(troopId);
            if (upg == null || upg.SpecialAbilities == null) return null;
            AbilityUnlock best = null;
            foreach (var a in upg.SpecialAbilities)
            {
                if (a == null || a.LevelThreshold <= currentLevel) continue;
                if (best == null || a.LevelThreshold < best.LevelThreshold) best = a;
            }
            return best;
        }

        /// <summary>
        /// WO-1389 - the NEXT-UNLOCK SENTENCE for the Troops card ("L3 unlocks Sweeping Cut"):
        /// the ability name is the authored description up to its first " - " (troop-upgrades.json
        /// writes every description as "Name - what it does"), so the card names the ability and
        /// not the whole flavour line. Null when nothing remains above <paramref name="currentLevel"/>
        /// (the View then paints nothing extra). ONE composer, read by the Manage VM, the post-raid
        /// dialogue token and the regression oracle, so the three can never disagree.
        /// </summary>
        public static string NextAbilityLine(string troopId, int currentLevel)
        {
            var next = NextAbility(troopId, currentLevel);
            if (next == null) return null;
            string name = next.Description ?? "";
            int dash = name.IndexOf(" - ", System.StringComparison.Ordinal);
            if (dash > 0) name = name.Substring(0, dash);
            name = name.Trim();
            if (name.Length == 0) name = next.AbilityId ?? "a new ability";
            return "L" + next.LevelThreshold + " unlocks " + name;
        }

        // ── Mutators (mutate ONLY the passed GameState — the IJobEffect completion seams) ──

        /// <summary>
        /// Raise the LEGACY stored barracks level on <paramref name="state"/> by one (clamped to the
        /// authored max). The completion effect of a BarracksUpgrade job.
        ///
        /// <para>⚠ DEAD PATH, DELIBERATELY LEFT IN PLACE (owner ruling 21, 2026-09-06). The only
        /// composer of a <c>JobKind.BarracksUpgrade</c> job WAS BarracksPanelVM, reachable only from
        /// BarracksPanel.ShowBarracksUI, which had ZERO CALLERS - so nothing in the game calls this.
        /// ⛔ Both of those files were DELETED on 2026-09-06 (WO-1430 Group A); WO-2009's troop DETAIL
        /// surface is the Manage Army tab, not that panel. THIS METHOD IS NOT DELETED WITH THEM: a
        /// pre-existing save can still hold a queued JobKind.BarracksUpgrade job, and that job's
        /// completion effect must keep resolving. Retiring it needs a save migration, not an edit -
        /// tracked as the WO-1430 follow-up. It deliberately still increments the RAW
        /// stored value (<see cref="BarracksLevelOf"/>), NOT the merged one: incrementing the merge
        /// would let this path grant unlocks ABOVE the barracks the player actually built, which is
        /// the very split ruling 21 closes.</para>
        /// </summary>
        public static int ApplyBarracksUpgrade(GameState state)
        {
            if (state == null) return 0;
            int max = MaxBarracksLevel;
            int next = BarracksLevelOf(state) + 1;
            if (max > 0 && next > max) next = max;
            state.BarracksLevel = next;
            return next;
        }

        /// <summary>
        /// Raise <paramref name="troopId"/>'s upgrade level on <paramref name="state"/> by one
        /// (clamped to the troop's max). The completion effect of a TroopUpgrade job. Returns the
        /// new level (0 on a null state/id).
        /// </summary>
        public static int ApplyTroopUpgrade(GameState state, string troopId)
        {
            if (state == null || string.IsNullOrEmpty(troopId)) return 0;
            if (state.TroopLevels == null) state.TroopLevels = new Dictionary<string, int>();
            int max = MaxTroopLevel(troopId);
            int next = TroopLevelOf(state, troopId) + 1;
            if (next > max) next = max;
            state.TroopLevels[troopId] = next;
            return next;
        }

        /// <summary>
        /// Grant a freshly-trained troop into <paramref name="state"/>'s army (unconditionally —
        /// cost/cap were checked at enqueue). The completion effect of a TrainTroop job. Returns
        /// the new roster count (0 on a null state).
        ///
        /// <para>WO-1374 — this is the SINGLE OWNER of "a troop joins the roster", and so it is
        /// where funnel step 2 ("army trained") is emitted. Both real paths pass through here:
        /// the timed Train job's completion effect, and the free starter squad granted on
        /// Barracks completion. Instrumenting the two call sites separately would mean the step
        /// could be reached by one and missed by the other, which is how a funnel starts
        /// reporting a conversion nobody can reproduce.</para>
        /// </summary>
        /// <param name="source">Which seam granted it - trace/property only, never behaviour.</param>
        /// <param name="granted">
        /// WO-1794 - TRUE when this troop was HANDED to the player (the free starter squad), FALSE
        /// when they paid for it and waited out a Train job. Analytics only, never behaviour.
        ///
        /// <para>Defaults to FALSE because the DEFAULT path through this method is the timed
        /// Train job, which is earned. A future grant path must pass TRUE explicitly - it cannot
        /// be inferred from <paramref name="source"/>, because then a new grant would only have to
        /// invent a spelling to report itself as a player action, which is the exact defect
        /// WO-1794 fixed one layer up.</para>
        /// </param>
        public static int GrantTrainedTroop(GameState state, string troopId, string source = "train-job",
                                            bool granted = false)
        {
            if (state == null || string.IsNullOrEmpty(troopId)) return 0;
            if (state.Army == null) state.Army = new ArmyStorage();
            state.Army.GrantTrained(troopId);
            int count = state.Army.Owned != null ? state.Army.Owned.Count : 0;
            // Guarded: analytics must never be able to lose a troop the player paid for.
            DeNelle.Core.Diagnostics.Guard.Try("Funnel", "army trained",
                () => DeNelle.Core.Analytics.RaidFunnel.ArmyTrained(troopId, count, source, granted));
            return count;
        }
    }
}

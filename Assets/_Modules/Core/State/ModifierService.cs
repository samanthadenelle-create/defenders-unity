// =============================================================================
// ModifierService — the single source of the active GameModifiers (WO-430).
// -----------------------------------------------------------------------------
// Assembly: DeNelle.Core   Namespace: DeNelle.Core.State
//
// THE read-point every consumer + scene uses. Compiles GameState.BuildingTiers
// through BuildingTierCatalog into one GameModifiers (multiplying each building's
// CURRENT-tier contribution), OR returns an OVERRIDE when one is set (dev menu /
// scene-creation modifier JSON — owner: "all scene creations should allow an
// override accepting a modifier JSON that applies these perks").
//
// Consumers: DefenseTower/ArcaneTower (damage/range), TroopDeployer/TroopController
// (damage/health — this is what carries the perks INTO RAIDS), ResourceBuildingState
// (production/offline). They read ModifierService.Active. Village->Core is allowed,
// so every Village consumer can read this Core service.
//
// No internal cache: Active recomputes on each read (iterating <=5 buildings is
// trivial) so it is never stale after a save load / tier change. Listeners that
// want to react to a change (UI refresh) subscribe to Changed.
// =============================================================================

using System;
using Newtonsoft.Json;
using UnityEngine;

namespace DeNelle.Core.State
{
    public static class ModifierService
    {
        private static GameModifiers _override;

        /// <summary>Fired when the active modifiers may have changed (tier bought, override set/cleared).</summary>
        public static event Action Changed;

        /// <summary>True while a dev/scene override is in force (Active ignores the player's real tiers).</summary>
        public static bool HasOverride => _override != null;

        /// <summary>The active perk contract: the override if set, else compiled from the player's building tiers.</summary>
        public static GameModifiers Active => _override ?? Compute();

        /// <summary>The player's current tier for a building (0 = locked/none).</summary>
        public static int TierOf(string buildingId)
        {
            var tiers = GameStateService.Instance != null && GameStateService.Instance.State != null
                ? GameStateService.Instance.State.BuildingTiers : null;
            if (tiers != null && buildingId != null && tiers.TryGetValue(buildingId, out int t)) return t;
            return 0;
        }

        /// <summary>
        /// The production multiplier for a specific resource building (WO-430): maps the building
        /// to the relevant active mult (lumbermill→wood, farm→food, forge→efficiency).
        /// Returns 1.0 (no-op) for any building not in the city-upgrade set, so non-WO-430 yield
        /// is untouched.
        ///
        /// THE FOOD LADDER LIVES ON THE FARM (owner ruling, 2026-08-04). It used to be authored
        /// under "windmill" while the collector the harvester actually ticks is "farm"
        /// (ResourceBuildingProgression.FarmId) — so this lookup fell through to the default and
        /// every food perk, up to +45% and paid for, silently did nothing. Wood was never broken
        /// only because its ladder and its collector are BOTH spelled "lumbermill", so the lookup
        /// collided by luck rather than by design.
        ///
        /// The fix is the ID, not the value: Apply already aggregates BY KIND, so the grant was
        /// always landing in GameModifiers.StoneProductionMult with nothing asking for it. The
        /// ladder MOVED to "farm" in building-tiers.json rather than this switch gaining a second
        /// food case, because the windmill is not a separate building — it is the Farm's secondary
        /// prop (VillageSceneBuilder.Content.cs, WO-101: SM_Farm_House primary + Windmill_Medieval
        /// secondary). One building in the world, one id everywhere.
        ///
        /// NOTE: "windmill" is deliberately NOT kept as an alias. An alias would let a future
        /// ladder be authored under the dead id and appear to work, which is the exact silent
        /// failure this ruling removed. Guarded by [upgrader-reaches-receiver].
        /// </summary>
        public static float ProductionMultFor(string buildingId) => ProductionMultOf(Active, buildingId);

        /// <summary>
        /// WO-1567 panel row 3 - the production multiplier this building WOULD carry at
        /// <paramref name="tier"/>, i.e. the live multiplier with THIS building's own tier
        /// contribution swapped for the target tier's. Perks and every other building's tier stay
        /// exactly as they are, because an upgrade does not change them.
        ///
        /// <para>⛔ WHY IT LIVES HERE AND NOT IN THE SCREEN THAT ASKS: this file owns the ONE
        /// building-id -&gt; modifier-field mapping (see <see cref="ProductionMultOf"/>). A caller
        /// that fetched a raw tier multiplier and did the substitution itself would need that
        /// mapping too, and a second copy of it is the duplicated state CLAUDE.md section 5
        /// records. The View-Model does no arithmetic; it asks a question and gets a number.</para>
        ///
        /// <para>⚠ <see cref="Compute"/> applies each building's CURRENT tier def only (it does not
        /// compound the ladder), so a tier's authored mult is ABSOLUTE and the swap is a plain
        /// divide-and-multiply. A tier below 1 contributes identity there, so
        /// <see cref="TierProductionMult"/> returns 1 for it - matching Compute rather than
        /// guarding a zero into a one.</para>
        /// </summary>
        public static float ProductionMultForTier(string buildingId, int tier)
        {
            if (string.IsNullOrEmpty(buildingId)) return 1f;
            float live = ProductionMultOf(Active, buildingId);
            float current = TierProductionMult(buildingId, TierOf(buildingId));
            if (current <= 0f) current = 1f;
            return live / current * TierProductionMult(buildingId, tier);
        }

        /// <summary>The production multiplier authored on ONE tier def (identity for tier &lt; 1).</summary>
        private static float TierProductionMult(string buildingId, int tier)
        {
            if (tier < 1) return 1f;
            var def = BuildingTierCatalog.TierOf(buildingId, tier);
            var m = def != null ? def.Modifiers : null;
            return m != null ? ProductionMultOf(m, buildingId) : 1f;
        }

        /// <summary>
        /// THE ONE building-id -&gt; production-field mapping. Both public production reads call it,
        /// so lumbermill/farm/forge can never drift onto different fields in two places.
        /// </summary>
        private static float ProductionMultOf(GameModifiers m, string buildingId)
        {
            if (m == null) return 1f;
            switch (buildingId)
            {
                case "lumbermill": return m.WoodProductionMult;
                case "farm":       return m.StoneProductionMult;
                case "forge":      return m.ResourceEfficiencyMult;
                default:           return 1f;
            }
        }

        /// <summary>
        /// STRUCTURE-HP multiplier for a specific building at ITS current tier (owner 2026-07-17). Reads
        /// the building's own tier def's <see cref="BuildingTierDef.StructureHpBonusPct"/> (cumulative-
        /// absolute at that tier) and returns the max-HP multiplier (1 + pct); 1.0 (no-op) for a locked
        /// building (tier 0) or one not in the catalog. This is PER-BUILDING (unlike the global Active
        /// contract) — the generic HP applier (Building.ApplyStructureHpMultiplier) multiplies the
        /// building's base max HP by this. Data-driven: one path for all 6 upgradable buildings.
        /// </summary>
        public static float StructureHpMultFor(string buildingId)
        {
            if (string.IsNullOrEmpty(buildingId)) return 1f;
            int tier = TierOf(buildingId);
            if (tier < 1) return 1f;
            var def = BuildingTierCatalog.TierOf(buildingId, tier);
            float pct = def?.StructureHpBonusPct ?? 0f;
            return pct > 0f ? 1f + pct : 1f;
        }

        /// <summary>Force the active modifiers to a fixed contract (dev menu / scene override). Pass null to clear.</summary>
        public static void SetOverride(GameModifiers modifiers)
        {
            _override = modifiers;
            Changed?.Invoke();
        }

        /// <summary>Set the override from a JSON GameModifiers (scene-config / dev paste). Empty/invalid → no-op + cleared.</summary>
        public static void SetOverrideJson(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) { ClearOverride(); return; }
            try { SetOverride(JsonConvert.DeserializeObject<GameModifiers>(json) ?? GameModifiers.None.Clone()); }
            catch (Exception ex) { Debug.LogWarning($"[ModifierService] bad override JSON, cleared: {ex.Message}"); ClearOverride(); }
        }

        public static void ClearOverride() { _override = null; Changed?.Invoke(); }

        /// <summary>Signal that the underlying tiers changed (after an upgrade / load) so listeners refresh.</summary>
        public static void Recompute() => Changed?.Invoke();

        // ── Compile tiers → modifiers ────────────────────────────────────────
        private static GameModifiers Compute()
        {
            var result = new GameModifiers();
            var state = GameStateService.Instance != null ? GameStateService.Instance.State : null;
            var tiers = state != null ? state.BuildingTiers : null;

            if (tiers != null)
            {
                foreach (var kv in tiers)
                {
                    int tier = kv.Value;
                    if (tier < 1) continue;
                    var def = BuildingTierCatalog.TierOf(kv.Key, tier);
                    if (def != null && def.Modifiers != null) Apply(result, def.Modifiers);
                }
            }

            // WO-432 — owned research perks fold in ON TOP of the tier modifiers (a perk's effect IS a
            // GameModifiers, compiled identically). Key = "buildingId:perkId". This is what carries the
            // numerical research (damage/armor) + the signature ability flags into towers/troops/raids.
            var owned = state != null ? state.OwnedBuildingPerks : null;
            if (owned != null)
            {
                foreach (var key in owned)
                {
                    if (string.IsNullOrEmpty(key)) continue;
                    int sep = key.IndexOf(':');
                    if (sep <= 0 || sep >= key.Length - 1) continue;
                    var perk = BuildingTierCatalog.FindPerk(key.Substring(0, sep), key.Substring(sep + 1));
                    if (perk != null && perk.Modifiers != null) Apply(result, perk.Modifiers);
                }
            }
            return result;
        }

        // Multiply the mults, OR the ability flags (each building sets a near-disjoint slice).
        private static void Apply(GameModifiers r, GameModifiers m)
        {
            r.TowerDamageMult        *= m.TowerDamageMult;
            r.TowerRangeMult         *= m.TowerRangeMult;
            r.TroopDamageMult        *= m.TroopDamageMult;
            r.TroopHealthMult        *= m.TroopHealthMult;
            r.WoodProductionMult     *= m.WoodProductionMult;
            r.StoneProductionMult     *= m.StoneProductionMult;
            r.ResourceEfficiencyMult *= m.ResourceEfficiencyMult;
            r.OfflineBonusMult       *= m.OfflineBonusMult;
            r.ArmyCapBonus  += m.ArmyCapBonus;   // additive: +5 troops per owning perk/tier
            r.AutoCollect   |= m.AutoCollect;    // OR: any owning perk enables auto-gather
            r.ArcaneOverload |= m.ArcaneOverload;
            r.BattleForged   |= m.BattleForged;
            r.Forgefire      |= m.Forgefire;
            r.EternalGrove   |= m.EternalGrove;
            r.WindsOfPlenty  |= m.WindsOfPlenty;

            // WO-861 Phase 3 - Cathedral of Magic MAGE perks. Each aggregates by its KIND:
            //   * the four multipliers COMPOUND (tier x owned perks), like every other mult;
            //   * mageManaMax + mageHpBonusPct ADD (the building-tiers _comment pins mageManaMax
            //     as "ADDITIVE integer, not a mult"), like ArmyCapBonus;
            //   * unlockSpell UNIONS its comma list (never overwrites) - a later tier/perk must
            //     never revoke a spell an earlier one granted.
            // MulSafe: a 0/negative authored mult is treated as IDENTITY rather than wiping the
            // stat (a fat-fingered 0 would otherwise silently zero the mage's damage).
            r.MageSpellPowerMult    = MulSafe(r.MageSpellPowerMult,    m.MageSpellPowerMult);
            r.MageManaRegenMult     = MulSafe(r.MageManaRegenMult,     m.MageManaRegenMult);
            r.MageManaCostMult      = MulSafe(r.MageManaCostMult,      m.MageManaCostMult);
            r.MageShellStrengthMult = MulSafe(r.MageShellStrengthMult, m.MageShellStrengthMult);
            r.MageManaMax    += m.MageManaMax;
            r.MageHpBonusPct += m.MageHpBonusPct;
            r.UnlockSpell = GameModifiers.MergeSpellList(r.UnlockSpell, m.UnlockSpell);
        }

        /// <summary>Compound a multiplier, treating a non-positive (unauthored-as-0 / fat-fingered)
        /// factor as IDENTITY so bad data degrades to "no perk" instead of "stat wiped".</summary>
        private static float MulSafe(float acc, float factor)
        {
            if (factor <= 0f)
            {
                if (factor < 0f) Debug.LogWarning($"[ModifierService] negative modifier factor {factor} ignored (treated as identity).");
                return acc;
            }
            return acc * factor;
        }
    }
}

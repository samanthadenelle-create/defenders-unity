// =============================================================================
// GameModifiers — the flat, JSON-serializable PERK CONTRACT (WO-430).
// -----------------------------------------------------------------------------
// Assembly: DeNelle.Core   Namespace: DeNelle.Core.State
//
// The keystone of the city-upgrade system (owner 2026-06-14): building tiers do
// NOT apply themselves ad-hoc. They COMPILE into ONE GameModifiers object that
// every consumer reads — towers, troops, resource production — and that every
// SCENE CREATION (castle AND raids) can be handed as an override JSON ("all scene
// creations should allow an override accepting a modifier JSON that applies these
// perks"). This is what makes upgrades PERSIST INTO RAIDS: the raid scene reads
// the same Active modifiers the castle does, so a +troop-damage Armorer perk
// assists the raid automatically.
//
// DESIGN: every numeric field DEFAULTS to a no-op (mult = 1.0), every flag to
// false — so an EMPTY/default contract changes nothing. Consumers multiply by the
// mult / branch on the flag; a missing field is identity. Pure data (Newtonsoft),
// WebGL-safe via CanonicalJson. Core owns it (GameState/SaveSchema round-trip;
// Core may not reference Village — same rule as PlayerTroop).
//
// SOURCE: ModifierService.Compute() builds this from GameState.BuildingTiers via
// the building-tiers catalog. An override (dev menu / scene config) replaces it.
// =============================================================================

using System;
using Newtonsoft.Json;

namespace DeNelle.Core.State
{
    /// <summary>Flat perk contract compiled from building tiers; default = all no-op.</summary>
    [Serializable]
    public sealed class GameModifiers
    {
        // ── Tower perks (Arcane Tower focus) ────────────────────────────────
        [JsonProperty("towerDamageMult")] public float TowerDamageMult = 1f;
        [JsonProperty("towerRangeMult")]  public float TowerRangeMult  = 1f;

        // ── Troop perks (Armorer focus) — these are what carry into RAIDS ────
        [JsonProperty("troopDamageMult")] public float TroopDamageMult = 1f;
        [JsonProperty("troopHealthMult")] public float TroopHealthMult = 1f;

        // ── Economy perks (Lumber Mill / Windmill / Forge focus) ────────────
        [JsonProperty("woodProductionMult")] public float WoodProductionMult = 1f;
        [UnityEngine.Serialization.FormerlySerializedAs("FoodProductionMult")]
        [JsonProperty("foodProductionMult")] public float StoneProductionMult = 1f;
        [JsonProperty("resourceEfficiencyMult")] public float ResourceEfficiencyMult = 1f; // Forge
        [JsonProperty("offlineBonusMult")] public float OfflineBonusMult = 1f;

        // ── Phase-2 WC3 building capstones (owner-named cheap levers) ─────────
        // ArmyCapBonus: SUMMED across owned perks/tiers (int, default 0 = no-op);
        // folds into ArmyStorage.MaxArmySize (base 10 + bonus). "Barracks: more troops."
        [JsonProperty("armyCapBonus")] public int ArmyCapBonus = 0;
        // AutoCollect: OR-ed flag; when true a ticking service auto-taps CollectAll so
        // resources bank without a manual tap. "Lumbermill: auto-gather capstone."
        [JsonProperty("autoCollect")] public bool AutoCollect = false;

        // ── Tier-4 unique abilities (flags; the "wow" moment per building) ──
        [JsonProperty("arcaneOverload")] public bool ArcaneOverload = false; // once/wave empower all towers 15s
        [JsonProperty("battleForged")]   public bool BattleForged   = false; // deployed troops +25% stats 60s
        [JsonProperty("forgefire")]      public bool Forgefire       = false; // periodic free troop equipment
        [JsonProperty("eternalGrove")]   public bool EternalGrove    = false; // periodic wood burst
        [JsonProperty("windsOfPlenty")]  public bool WindsOfPlenty   = false; // periodic food windfall

        // == Cathedral of Magic - MAGE perks (WO-861 Phase 3) ===================
        // The arcane-tower rows in building-tiers.json were re-pointed from tower
        // stats to MAGE stats (owner 2026-08-02). Those keys were authored with NO
        // matching field here, so Newtonsoft SILENTLY DROPPED all seven (no throw,
        // no log - MissingMemberHandling.Ignore) and the Cathedral granted the mage
        // NOTHING. These fields close that hole. Neutral default = identity, so an
        // UNBUILT Cathedral changes nothing.
        //
        // ANY new key added to a `modifiers` block in building-tiers.json MUST get a
        // field here in the SAME change - see Assets/Editor/Regression/
        // ModifierKeyCoverageRegression.cs, the oracle that now fails the build if a
        // key has no field (this whole bug class can no longer recur silently).

        /// <summary>Mage outgoing spell damage multiplier. 1 = identity.</summary>
        [JsonProperty("mageSpellPowerMult")]    public float MageSpellPowerMult    = 1f;
        /// <summary>Mage mana-regen multiplier. 1 = identity.</summary>
        [JsonProperty("mageManaRegenMult")]     public float MageManaRegenMult     = 1f;
        /// <summary>Mage ability mana-COST multiplier (below 1 = cheaper). 1 = identity.</summary>
        [JsonProperty("mageManaCostMult")]      public float MageManaCostMult      = 1f;
        /// <summary>Arcane Shell mitigation-strength multiplier. 1 = identity.</summary>
        [JsonProperty("mageShellStrengthMult")] public float MageShellStrengthMult = 1f;
        /// <summary>ADDITIVE max-mana bonus (NOT a multiplier - the building-tiers _comment
        /// says so explicitly: "mageManaMax (ADDITIVE integer, not a mult)"). 0 = identity.</summary>
        [JsonProperty("mageManaMax")]           public float MageManaMax           = 0f;
        /// <summary>ADDITIVE fraction of the mage's base max HP (0.10 = +10%). 0 = identity.</summary>
        [JsonProperty("mageHpBonusPct")]        public float MageHpBonusPct        = 0f;
        /// <summary>COMMA-SEPARATED abilities.json ids the Cathedral makes learnable
        /// (tier 3 unlocks two). Same CSV convention HeroTalentModifiers.AbilityListContains
        /// already parses - deliberately NOT a second format. Empty = identity.</summary>
        [JsonProperty("unlockSpell")]           public string UnlockSpell          = string.Empty;

        /// <summary>A shared no-op instance (all mults 1, all flags false). Never mutate.</summary>
        public static readonly GameModifiers None = new GameModifiers();

        /// <summary>
        /// UNION of two comma-separated spell-id lists, order-preserving and
        /// case-insensitively de-duped. Used by ModifierService.Apply so unlockSpell
        /// ACCUMULATES across tiers/perks/buildings instead of last-one-wins (a tier-4
        /// Cathedral must not revoke the tier-2 spell). Null/empty inputs are identity.
        /// </summary>
        public static string MergeSpellList(string a, string b)
        {
            if (string.IsNullOrWhiteSpace(a)) return string.IsNullOrWhiteSpace(b) ? string.Empty : b.Trim();
            if (string.IsNullOrWhiteSpace(b)) return a.Trim();

            var seen = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var ordered = new System.Collections.Generic.List<string>();
            AppendCsv(a, seen, ordered);
            AppendCsv(b, seen, ordered);
            return string.Join(",", ordered.ToArray());
        }

        private static void AppendCsv(string csv, System.Collections.Generic.HashSet<string> seen,
                                      System.Collections.Generic.List<string> ordered)
        {
            if (string.IsNullOrEmpty(csv)) return;
            foreach (var raw in csv.Split(','))
            {
                string id = raw.Trim();
                if (id.Length == 0) continue;
                if (seen.Add(id)) ordered.Add(id);
            }
        }

        /// <summary>Deep copy (so callers can layer/override without mutating the source).
        /// HAND-WRITTEN: every field added above MUST get a line here, or it vanishes on any
        /// layered/override path - the same silent-loss class as the dropped-key defect.</summary>
        public GameModifiers Clone() => new GameModifiers
        {
            TowerDamageMult = TowerDamageMult, TowerRangeMult = TowerRangeMult,
            TroopDamageMult = TroopDamageMult, TroopHealthMult = TroopHealthMult,
            WoodProductionMult = WoodProductionMult, StoneProductionMult = StoneProductionMult,
            ResourceEfficiencyMult = ResourceEfficiencyMult, OfflineBonusMult = OfflineBonusMult,
            ArmyCapBonus = ArmyCapBonus, AutoCollect = AutoCollect,
            ArcaneOverload = ArcaneOverload, BattleForged = BattleForged, Forgefire = Forgefire,
            EternalGrove = EternalGrove, WindsOfPlenty = WindsOfPlenty,
            // WO-861 Phase 3 - Cathedral of Magic mage perks.
            MageSpellPowerMult = MageSpellPowerMult, MageManaRegenMult = MageManaRegenMult,
            MageManaCostMult = MageManaCostMult, MageShellStrengthMult = MageShellStrengthMult,
            MageManaMax = MageManaMax, MageHpBonusPct = MageHpBonusPct,
            UnlockSpell = UnlockSpell,
        };
    }
}

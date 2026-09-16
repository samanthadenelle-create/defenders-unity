// =============================================================================
// TroopDef — typed model for one buildable troop (WO-453 Step 1).
// -----------------------------------------------------------------------------
// Assembly: DeNelle.Village   Namespace: DeNelle.Village
//
// Troops are CONTENT, not code (the same call the pets make): TroopCatalog reads
// Assets/StreamingAssets/Data/Canonical/troops.json at load and hydrates these
// typed records. Modelled on PetDef — a flat [JsonProperty] def the factory +
// controller read their stats off, never typing troop names / numbers inline.
//
// Step-1 scope is COMBAT-ONLY: a troop is a lightweight friendly fighter that
// hunts the nearest hostile and is itself damageable. The cost / build-time
// fields (CostWood / CostIron / CostFood / BuildSeconds) are authored now so the
// later build-queue + army-storage steps (Step 2+) read them without a schema
// change — they are inert in Step 1.
// =============================================================================

using System;
using Newtonsoft.Json;

namespace DeNelle.Village
{
    /// <summary>
    /// One buildable troop's static definition — hydrated from troops.json by
    /// <see cref="TroopCatalog"/>, never constructed inline. Mirrors the flat
    /// <c>PetDef</c> shape so the loader / factory / controller read stats off it.
    /// </summary>
    [Serializable]
    public sealed class TroopDef
    {
        /// <summary>Stable id — e.g. <c>troop-footman</c>.</summary>
        [JsonProperty("id")] public string Id;
        /// <summary>Display name — verbatim (Footman / Archer).</summary>
        [JsonProperty("displayName")] public string DisplayName;
        /// <summary>
        /// Combat role — <c>melee</c>, <c>ranged</c> (reach, not projectile yet), or
        /// <c>siege</c> (WO-933: structure-preferring hunt, machine visual path).
        /// </summary>
        [JsonProperty("role")] public string Role;
        /// <summary>Army slots this troop occupies (population cost).</summary>
        [JsonProperty("slots")] public int Slots = 1;
        /// <summary>
        /// Hard ownership cap (CoC scarcity). <c>0</c> = unlimited (default, back-compat).
        /// <c>1</c> = at most one of this def may be owned (roster + in-flight train jobs,
        /// including wounded). Enforced at train enqueue — never only in UI.
        /// </summary>
        [JsonProperty("maxOwned")] public int MaxOwned;
        /// <summary>
        /// Multiplier applied when this troop damages a Hostile structure
        /// (walls/towers/gates/spire — targets that also implement <c>IDamageableStructure</c>).
        /// Default 1. Siege pieces author &gt;1 (WC Demolisher-style structure bias).
        /// </summary>
        [JsonProperty("structureDamageMult")] public float StructureDamageMult = 1f;
        /// <summary>
        /// Multiplier applied when this troop damages a Hostile non-structure (garrison units).
        /// Default 1. Siege pieces author &lt;1 so they are bad at anti-infantry.
        /// </summary>
        [JsonProperty("unitDamageMult")] public float UnitDamageMult = 1f;
        /// <summary>
        /// Resources model path the factory skins. Bare names (e.g. <c>Knight</c>,
        /// <c>SC_Footman</c>) load under <c>Heroes/</c>. Paths that already contain a
        /// slash (e.g. <c>Structures/Catapult</c>) load as a full Resources path.
        /// </summary>
        [JsonProperty("model")] public string Model;

        /// <summary>
        /// Optional Resources/Heroes controller stem (no path/ext), e.g. <c>Knight</c>,
        /// <c>Ranger</c>, <c>Mage</c>. Empty → resolved from role/model:
        /// melee → Knight (Attack/stab), ranged → Ranger (bow Attack), caster/Mage body → Mage (Cast).
        /// </summary>
        [JsonProperty("animator")] public string Animator;
        /// <summary>Yaw (deg) the factory applies so the body faces +Z (the move direction).
        /// Tripo/AccuRIG bodies import facing +X → <c>-90</c> (historic default). Supercyan
        /// humanoids already face +Z → set <c>0</c>. Data-driven so each art pack's facing is
        /// authored per-troop, not hard-coded.</summary>
        [JsonProperty("modelYaw")] public float ModelYaw = -90f;

        /// <summary>
        /// Optional main-hand gear Resources path (no extension), e.g. <c>TroopGear/Sword</c>.
        /// Attached to RightHand (bows → LeftHand) by <see cref="TroopGearApplier"/> after skin.
        /// Empty = unarmed body mesh only.
        /// </summary>
        [JsonProperty("weapon")] public string Weapon;

        /// <summary>
        /// Optional off-hand gear Resources path, e.g. <c>TroopGear/Shield</c>.
        /// Attached to LeftHand. Empty = none.
        /// </summary>
        [JsonProperty("offhand")] public string Offhand;

        /// <summary>Element — None for the Step-1 starter troops.</summary>
        [JsonProperty("element")] public string Element;
        /// <summary>Max HP — drives the damageable health pool.</summary>
        [JsonProperty("maxHp")] public float MaxHp = 100f;
        /// <summary>Per-hit attack damage.</summary>
        [JsonProperty("attackDamage")] public float AttackDamage = 12f;
        /// <summary>Seconds between attacks.</summary>
        [JsonProperty("attackCooldown")] public float AttackCooldown = 1.0f;
        /// <summary>Attack reach (units) — melee is short, ranged is long.</summary>
        [JsonProperty("attackRange")] public float AttackRange = 2.5f;
        /// <summary>Hunt move speed (units/sec).</summary>
        [JsonProperty("moveSpeed")] public float MoveSpeed = 4.0f;
        /// <summary>How far the troop scans for a hostile to hunt (units).</summary>
        [JsonProperty("huntScanRadius")] public float HuntScanRadius = 14f;

        // ── Build economy (authored now, inert in Step 1; consumed by Step 2+) ──
        /// <summary>Wood cost to build this troop.</summary>
        [JsonProperty("costWood")] private int LegacyCostWood { set { CostGold += Math.Max(0, value); } }
        /// <summary>Iron cost to build this troop.</summary>
        [JsonProperty("costIron")] private int LegacyCostIron { set { CostGold += Math.Max(0, value); } }
        /// <summary>Gold cost to train this troop. WO-1163 retires material-priced training.</summary>
        [JsonProperty("costGold")] public int CostGold;
        /// <summary>Legacy authored food cost; accepted on old catalogs but never player-facing.</summary>
        [JsonProperty("costFood")] private int LegacyCostFood { set { CostGold += Math.Max(0, value); } }
        /// <summary>Seconds to build this troop in the (later) training queue.</summary>
        [JsonProperty("buildSeconds")] public float BuildSeconds;

        // ── Barracks progression (WO-732) ──
        /// <summary>
        /// Minimum Barracks building tier required to train this troop.
        /// 1 = default/day-one. Compared to ModifierService.TierOf("barracks")
        /// (0 if barracks never upgraded — treat as tier 1 once barracks exists;
        /// see WO-733 for the exact tier resolution rule). Additive + defaults to 1
        /// so older troops.json (no field) keep resolving as day-one troops.
        /// </summary>
        [JsonProperty("unlockBarracksTier")] public int UnlockBarracksTier = 1;
        /// <summary>One-line blurb for the train detail pane (WO-733/735). Optional; may be empty day-one.</summary>
        [JsonProperty("shortDescription")] public string ShortDescription;
        /// <summary>Resources icon key for the tray/portrait (WO-735). Optional; may be empty day-one.</summary>
        [JsonProperty("iconId")] public string IconId;
        /// <summary>Previous saved roster IDs which resolve to this definition without rewriting owned troops.</summary>
        [JsonProperty("legacyIds")] public string[] LegacyIds;
    }
}

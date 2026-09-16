// =============================================================================
// AccessoryDef — typed model for accessories.json (rings + amulets, WO-543).
// -----------------------------------------------------------------------------
// Assembly: DeNelle.Village   Namespace: DeNelle.Village
//
// The THIRD gear category after WeaponDef / ArmorDef (GearCatalog.cs) — pure JSON
// stat modifiers with NO 3D mesh attachment and NO body-mesh swap. The only visual
// is the 2D shop icon (iconPath) + the rarity rim-light glow on the hero
// (ArmorVfxMap). An accessory's bonuses stack ADDITIVELY on top of weapon + armor
// (GearLoadout.ApplyStats):
//   • damageMult — additive bonus to the hero damage chain (0.08 = +8%).
//   • defense    — additive incoming-damage reduction stacked after armor.
//   • hpBonus    — flat HP added to the hero's max HP.
//
// Mirrors WeaponDef / ArmorDef exactly (capability resolution + Aegis predicate)
// so the existing item-model invariants (docs/ITEM_MODEL.md) hold for accessories.
// Sold exclusively at the Jeweler (Sable Vey, shop key "jeweler").
// =============================================================================

using System;
using DeNelle.Core.State;

namespace DeNelle.Village
{
    /// <summary>A ring or amulet: pure stat modifiers (damageMult / defense / hpBonus), all additive.</summary>
    [Serializable]
    public sealed class AccessoryDef
    {
        public string id;
        public string name;
        public string icon;        // emoji fallback (💍 / 📿)
        public string category;    // "ring" | "amulet"
        public string slot;        // "ring" | "amulet" (equip-slot key)
        public string job;         // "any" for all v1 accessories
        public string rarity;      // common / uncommon / rare / epic / legendary

        public float  damageMult;  // ADDITIVE bonus to the hero damage chain (0 = no bonus)
        public float  defense;     // ADDITIVE incoming-damage reduction (0 = no bonus)
        public int    hpBonus;     // flat HP add to the hero's max HP (0 = no bonus)

        // WO-888: OPTIONAL persistent-aura tag, the accessory twin of WeaponDef.element.
        // Empty/null (every authored row today) = no aura = unchanged behaviour. When an owner
        // tags a row with "aura": "heal", GearAuraMap.TryBodyAura resolves it to Aura_ItemHeal
        // and GearAura holds a soft RISING restoration column on the hero's body while it is
        // worn. Which relic deserves that aura is a CREATIVE call, so the seam is wired and the
        // tag is left for the owner rather than guessed at here (the standing VFX rule: map an
        // owner-tagged key verbatim, never pick one). Newtonsoft leaves it null when absent.
        public string aura;

        // WO-295 set linkage (see WeaponDef.setId). The Heartstone Locket carries "aegis".
        public string setId;

        // WO-300 lore (see WeaponDef). makersMark also drives the ArmorVfxMap rim tint.
        public string makersMark;
        public string flavor;
        public string saga;

        public GearReq req;

        // Shop integration (resource costs). Vendor SHOPS charge GOLD via GearAppraisal;
        // these legacy fields are carried for parity with WeaponDef / ArmorDef.
        public int buyWood;
        [UnityEngine.Serialization.FormerlySerializedAs("buyFood")]
        [Newtonsoft.Json.JsonProperty("buyFood")] public int buyStone;
        public int buyIron;
        public int buyCrystals;

        // WO-Item-1: the catalog⊥repo LOOK half (docs/ITEM_MODEL.md §3). iconPath = the
        // inventory/store sprite (Resources.Load<Sprite>); prefabPath stays null (accessories
        // have NO equippable model — they are pure data + a rim-light). The emoji `icon`
        // stays the placeholder.
        public string prefabPath;
        public string iconPath;
        public string loadVia;

        // WO-Item-1: OPTIONAL explicit capability override from JSON (null when absent).
        public ItemCapability? capabilities;

        /// <summary>WO-Item-1: the entry's resolved capability flags. An Accessory defaults to
        /// Carriable|Equippable (docs/ITEM_MODEL.md §2/§3); an explicit JSON `capabilities`
        /// override wins when present. Systems read THIS, never the catalog-of-origin.</summary>
        public ItemCapability Capabilities =>
            capabilities ?? (ItemCapability.Carriable | ItemCapability.Equippable);

        /// <summary>WO-295: part of the legendary Aegis of Elarion set (the Heartstone Locket).</summary>
        public bool IsAegis =>
            !string.IsNullOrEmpty(setId) && setId.Equals("aegis", StringComparison.OrdinalIgnoreCase);

        /// <summary>True when this accessory seats in the RING slot.</summary>
        public bool IsRing =>
            !string.IsNullOrEmpty(slot) && slot.Trim().Equals("ring", StringComparison.OrdinalIgnoreCase);

        /// <summary>True when this accessory seats in the AMULET slot.</summary>
        public bool IsAmulet =>
            !string.IsNullOrEmpty(slot) && slot.Trim().Equals("amulet", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>JSON root for accessories.json (top-level "accessories" array).</summary>
    [Serializable] public sealed class AccessoryCatalogData { public System.Collections.Generic.List<AccessoryDef> accessories; }
}

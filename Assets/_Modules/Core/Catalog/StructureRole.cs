// =============================================================================
// StructureRole — WHAT A BUILDING IS, as an OPEN vocabulary settled by the data.
// -----------------------------------------------------------------------------
// Assembly: DeNelle.Core   Namespace: DeNelle.Core.Catalog   (WO-1161)
//
// Owner rulings this file obeys, in the order they were given:
//   2026-08-17  "would it be better to set them with an enumeration so we can
//               reference as we want / so its not confusing"
//   2026-08-23  "just get at building.enum.displayname"
//   2026-08-23  "you could even point to a db table to settle them"
//   2026-08-23  "the idea is staying fluid" / "if we add a building we do not
//               want to have to manually code it"
//
// ⛔ WHY THIS IS A STRING VOCABULARY AND NOT A C# `enum`. The first draft of this
// file WAS an enum, and it was wrong for one specific reason: a real enum FREEZES
// the vocabulary, so adding a building with a new role would mean editing C# —
// exactly the manual coding the owner ruled out. The role is therefore an ORDINARY
// DATA VALUE (a string on the catalog row), and the members below are nothing more
// than compile-checked NAMES for the handful of roles that CODE happens to branch
// on today. Adding a building is a CATALOG edit: author the row, give it a role,
// done. It lists, it resolves, it is referenceable — with no code change anywhere.
// Only a role that needs BEHAVIOUR attached ever earns a constant here, and even
// then the constant is a convenience, never the registry.
//
// This is the project's own One Model law applied to naming (ARCHITECTURE_PRINCIPLES
// §3): "Behavior is the SUM of capabilities held, not inherited per type. Add by
// entry, not by code."
//
// ⛔ THE IDS STAY OPAQUE SAVE KEYS — that is why the role exists at all.
// `everBuiltStructureIds`, BaseLayout records, baked scenes, vendors.json,
// dialogues.json and the WO-695 migration rows all join on the id, and the game is
// LIVE on the Solana dApp Store: renaming `forge` -> `weaponsmith` orphans every
// existing player's building. The id never moves; the ROLE carries the meaning.
//
// ⛔ FUNCTION IS THE AUTHORITY (owner: "which sells weapons, that is the
// weaponsmith use the JSON data"). A row's role comes from what it DOES in
// vendors.json, never from the word currently printed on its tile — which is how
// three rows came to answer to two words:
//     id `forge`           displayed "Armorer"     but sells WEAPONS
//     id `armorer`         displayed "Blacksmith"  but sells ARMOUR
//     id `workshop`        displayed "Weaponsmith" but is not a vendor at all
//     id `collector_forge` displayed "Forge"       and is the iron faucet
// which is what told the owner "Iron - NEEDS: Forge" while `forge` already sat in
// her ever-built ledger — an instruction that could not be satisfied by obeying it.
//
// USAGE:
//     StructureRoles.DisplayName(StructureRole.Armorer)   // the word, from the table
//     StructureRoles.Id(StructureRole.Armorer)            // the save key
//     StructureRoles.DisplayName("some_new_role")         // works with NO code change
// =============================================================================

namespace DeNelle.Core.Catalog
{
    /// <summary>
    /// Compile-checked names for the roles CODE references. **Not the list of legal
    /// roles** — the catalog may author any role string it likes and it will resolve
    /// through <see cref="StructureRoles"/> without appearing here.
    /// <para>Values are the exact strings authored in the catalog's `role` field.
    /// Comparison is ordinal and case-insensitive, so casing in the data is free.</para>
    /// </summary>
    public static class StructureRole
    {
        /// <summary>Unauthored — the row declares no role. Every pre-existing row.</summary>
        public const string None = "";

        // ── The four vendors (vendors.json is the authority on which is which) ──
        /// <summary>Sells WEAPONS.</summary>
        public const string Weaponsmith = "weaponsmith";
        /// <summary>Sells ARMOUR.</summary>
        public const string Armorer = "armorer";
        /// <summary>Sells rings/amulets.</summary>
        public const string Jeweler = "jeweler";
        /// <summary>Sells consumables/materials.</summary>
        public const string Marketplace = "marketplace";

        /// <summary>Crafting station — NOT a vendor (holds no vendors.json row).</summary>
        public const string CraftingStation = "crafting_station";

        // -- The resource faucets. Code branches on these (the Echo harvest gate) --
        /// <summary>
        /// The STONE faucet - the row id <c>collector_farm</c>, displayName "Quarry".
        /// <para>WO-1416, owner ruling 2026-09-05, verbatim: "quarry pays stone" and
        /// "the farm was retired nothiong uses food, was replaced by stone which actually
        /// has uses". The catalog row already authored <c>"role": "stone_producer"</c>
        /// (structures-catalog.json:1087) while the ONLY constant here still said
        /// <c>food_producer</c> - a role NO row claims - so every call site resolved to
        /// null and fell back to a literal: BuildingInteractable printed the generic
        /// "Building" and CastleVendorNpcInjector printed the retired proper noun "Farm".
        /// That is why one building answered to three different words.</para>
        /// <para>STOP - THE CATALOG ID DOES NOT MOVE. <c>collector_farm</c> is a LIVE SAVE KEY
        /// (everBuiltStructureIds / BaseLayout / baked twins); only the ROLE and the
        /// RESOURCE changed. Likewise <c>HarvestResource.Stone</c> stays as the frozen
        /// persisted Stone wallet slot - see ResourceBuildingProgression.LabelFor.</para>
        /// <para>The retired <c>FoodProducer = "food_producer"</c> constant is DELETED
        /// rather than kept: a named role that no row claims is a trap that resolves to
        /// null in silence, which is the exact defect WO-1416 fixed.</para>
        /// </summary>
        public const string StoneProducer = "stone_producer";
        /// <summary>The WOOD faucet.</summary>
        public const string WoodProducer = "wood_producer";
        /// <summary>The IRON faucet.</summary>
        public const string IronProducer = "iron_producer";

        // ── The storage containers (WO-707: stock lives apart from the shop) ──
        /// <summary>Stores wood.</summary>
        public const string WoodStore = "wood_store";
        /// <summary>Stores iron.</summary>
        public const string IronStore = "iron_store";
        /// <summary>Stores food/grain.</summary>
        public const string FoodStore = "food_store";

        /// <summary>
        /// Watchtower lookout — earned early-warning intel (WO-1184). Code branches
        /// on this because a lookout buys information, not just damage. Catalog rows
        /// may author <c>role: "lookout"</c>; until they do, the live key is the
        /// catalog id <c>tower_ground_archer</c> (the wooden watchtower).
        /// </summary>
        public const string Lookout = "lookout";
    }
}

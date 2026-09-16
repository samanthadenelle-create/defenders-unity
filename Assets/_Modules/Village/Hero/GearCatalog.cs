// =============================================================================
// GearCatalog — typed model + loader for weapons.json / armor.json (Gear v1).
// -----------------------------------------------------------------------------
// Assembly: DeNelle.Village   Namespace: DeNelle.Village
//
// Mirrors AbilityCatalog.cs exactly: canonical JSON under StreamingAssets, read
// via Application.streamingAssetsPath, parsed by Newtonsoft.Json. Gear is CONTENT,
// not code — add/retune weapons + armor by editing the JSON, no recompile.
//
// A weapon contributes a damageMult to the hero's damage chain (base x talent x
// level x timing x WEAPON). Armor contributes a fractional incoming-damage
// reduction. Equip eligibility is gated by req (level v1; dex/arcane/might later).
//
// ANDROID NOTE: same StreamingAssets caveat as AbilityCatalog (a UnityWebRequest
// read is required on Android; synchronous File.ReadAllText is valid in Editor /
// Windows / macOS). To be revisited with the Seeker build.
// =============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using UnityEngine;
using DeNelle.Core.State;

namespace DeNelle.Village
{
    /// <summary>Equip requirement. Level-gated for v1; attribute keys (dex/arcane/might) are
    /// carried for a later pass and default to 0 (= no requirement).</summary>
    [Serializable]
    public sealed class GearReq
    {
        public int level = 1;
        public int dex;
        public int arcane;
        public int might;
    }

    /// <summary>A weapon: its damageMult multiplies the hero's outgoing ability damage.</summary>
    [Serializable]
    public sealed class WeaponDef
    {
        public string id;
        public string name;
        public string icon;
        public string job;        // "mage" | "knight" | "ranger" | "any"
        public string rarity;

        // ELEMENTAL brand (fire-VFX verification pipeline). OPTIONAL; empty/null = no element
        // (unchanged behavior). When set (e.g. "fire"), the melee on-hit path plays a full
        // multi-layer elemental impact VFX at the hit point via the shared VFXManager pool,
        // LAYERED on the existing weaponskill impact — so an elemental blade is VISUALLY read
        // in combat. Catalog-driven: WeaponVfxMap.ElementalOnHitKey is the ONE reader that maps
        // this string to a HovlVfxCatalog key, so ANY future element:"fire" weapon shows the
        // effect (never hardcoded to one weapon id). Newtonsoft leaves it null when the row omits it.
        public string element;

        // WO-1066: mechanical identity and presentation-neutral semantic VFX intent.
        // Catalog data never carries third-party prefab keys. Missing fields mean no effect/VFX.
        public string subtitle;
        public string effectKind;
        public float effectChance;
        public float effectDamagePct;
        public float effectDurationSeconds;
        public int effectMaxStacks;
        public float effectCooldownSeconds;
        public string vfxHeld;
        public string vfxAttack;
        public string vfxHit;
        public string vfxProc;

        // WO-1067 owner-authored certification. Only "ready" is sellable at the Forge once
        // certification is enabled by data; bounds/address registration alone never set it.
        public string visualReadiness;
        public string visualEvidence;
        public string visualFailureReason;

        public bool IsVisuallyReady => string.Equals(visualReadiness, "ready", StringComparison.OrdinalIgnoreCase);
        public bool HasVisualCertification => !string.IsNullOrWhiteSpace(visualReadiness);

        // HAND-SLOT model (owner 2026-06-18, docs/STORE_EQUIP_SPEC.md "Equip-slot rules").
        // These three fields exist in the canonical weapons.json but were previously DROPPED on
        // deserialize because WeaponDef didn't declare them. Now read so the equip layer can
        // enforce main-hand/off-hand rules without inventing data.
        //   hand       — "1h" (one-handed; may share with an off-hand) | "2h" (occupies BOTH hands).
        //                Empty/absent => treated as "1h" (the permissive default; never blocks).
        //   category   — weapon family: "sword"|"axe"|"bow"|"staff"|"shield"|... A "shield" is an
        //                OFF-HAND item (seats in the off hand, never the main hand).
        //   damageType — "melee"|"ranged"|"magic" (carried for shop deltas/buffs; not gameplay-gated yet).
        public string hand;
        public string category;
        public string damageType;

        public float damageMult = 1f;
        // MELEE reach (m): extends the basic-attack hitbox radius for a melee weapon
        // (greatsword/polearm/axe outreach a dagger). 0 = "unset" -> the hero keeps
        // PlayerAttackController's fixed AttackRange. Only meaningful for melee jobs
        // (knight); ranged jobs (mage/ranger) attack via AbilityDef.Range and leave
        // this 0, so their reach is unaffected.
        public float reach = 0f;

        // SHIELD MITIGATION (owner 2026-08-02: "make shields add different defense levels and
        // gate them by levels"). Fractional incoming-damage reduction the OFF-HAND contributes,
        // mirroring ArmorDef.defense below. Shields were DOUBLE-inert before this: every
        // category "shield" row carried no defense value at all, AND GearLoadout never summed
        // the equipped off-hand into ArmorDefense - so a shield was decoration on both halves.
        // Absent in JSON => 0 on every non-shield row, so ordinary weapons are untouched.
        // GearLoadout.ApplyStats sums it under the SAME 0.70 ceiling as armor + accessories.
        public float defense;     // 0..0.9 fractional damage reduction (shields only)

        public GearReq req;

        // WO-295: Legendary "Aegis of Elarion" set. setId groups items into a set
        // (the per-class Aegis weapons + the Aegis armor all carry "aegis"); the
        // AegisSetEffect detects a full set by this key. Empty for ordinary gear.
        public string setId;

        // WO-295: saga flavour text (names the four crafters). Optional; null for v1 gear.
        public string saga;

        // WO-300: Elarion weaponsmithing lore. Both optional + default empty so existing
        // items are unaffected. `flavor` = item flavour line (Bright Centuries tone);
        // `makersMark` = the forge stamp on the tang (Emberhand/Oathweld/Heartwood/Last-Pressing)
        // that the realm — and an appraiser — learns to read. Surfaced via GearAppraisal.
        public string flavor;
        public string makersMark;

        // Shop integration (basic): resource costs. Populated from canonical JSON.
        public int buyWood;
        [UnityEngine.Serialization.FormerlySerializedAs("buyFood")]
        [Newtonsoft.Json.JsonProperty("buyFood")] public int buyStone;
        public int buyIron;
        public int buyCrystals;

        // WO-Item-1: the catalog⊥repo LOOK half (docs/ITEM_MODEL.md §3). prefabPath =
        // the equippable model, iconPath = the inventory/store sprite. Both are NULL
        // for now — WO-Item-2's gear generator populates them per owned asset; do NOT
        // hand-author them here. The legacy emoji `icon` stays the v1 placeholder.
        public string prefabPath;
        public string iconPath;

        // WO-Item (Addressables equip): HOW prefabPath is loaded. The Blink generator
        // stamps "addressable" on every Blink row (its prefabPath is an Addressables
        // address, e.g. "gear/weapon/Sword1h_01"); legacy/Tripo rows leave it null/empty
        // and continue to resolve through the hardcoded Resources map in EquipmentController.
        // EquipmentController.LoadsViaAddressable(WeaponDef) reads THIS (or the "gear/"
        // address prefix) to pick the load path. Newtonsoft populates it when present.
        public string loadVia;

        // WO-Item-1: OPTIONAL explicit capability override from JSON (null when absent).
        // When present it wins; when absent the kind default applies (see Capabilities).
        // Nullable so a row without the field is unchanged (Newtonsoft leaves it null).
        public ItemCapability? capabilities;

        /// <summary>WO-Item-1: the entry's resolved capability flags. A Weapon defaults to
        /// Carriable|Equippable (docs/ITEM_MODEL.md §2/§3); an explicit JSON `capabilities`
        /// override wins when present. Systems read THIS, never the catalog-of-origin.</summary>
        public ItemCapability Capabilities =>
            capabilities ?? (ItemCapability.Carriable | ItemCapability.Equippable);

        // ── WO-861 ARROW RIDERS (Sylas's ammo-as-weapon) ────────────────────────────
        // The Ranger's "weapon" is the ARROW, so an equipped arrow carries an on-hit rider
        // its basic attack applies. Read by HeroAbilities.TryResolveAmmoRider and applied
        // ONLY on the Ranger's locked-Q basic (gated by IsArrowRiderEligible) -- Knight and
        // Mage basics are provably unaffected.
        // THIS FILE WAS ORPHANED: the effects lane and the data lane EACH believed the other
        // owned GearCatalog.cs, so these fields were never added and every rider sat inert
        // behind a hard `return false`. Both halves shipped correct and the SEAM between them
        // was empty -- the classic file-fence gap. Added by the orchestrator, who owns the seams.
        // Absent in JSON => null/0 => no rider, so every existing weapon row is unchanged.
        /// <summary>On-hit rider an equipped arrow applies: "burn" | "poison" | "slow". Empty/null = none.</summary>
        public string ammoEffect;
        /// <summary>Damage-per-second for a burn/poison rider.</summary>
        public float ammoDps;
        /// <summary>Duration (seconds) of the rider.</summary>
        public float ammoSeconds;
        /// <summary>Slow magnitude 0..1 for a "slow" rider (0.35 = -35% move speed).</summary>
        public float ammoSlowPct;

        // ── ORIENT CANON: `manual` (WO-1123 §1.2 — the flag that was authored 81 times and read
        //    ZERO times) ─────────────────────────────────────────────────────────────────────
        // ARCHITECTURE_PRINCIPLES.md §4: "A `manual=true` correction is CANON and is NEVER
        // overwritten by the auto pass." WEAPON_ARMOR_ORIENT_LOGIC.md repeats it twice. It is
        // honoured for STRUCTURES by three readers (CatalogOrientationBaker, StructureFactory,
        // GhostPreview) — and, until this field existed, by NOTHING on the gear side.
        //
        // 81 of the 96 rows in Assets/Resources/Data/Canonical/weapons.json carry it. WeaponDef
        // did not DECLARE it, so Newtonsoft dropped it on deserialize and every consumer that
        // might have honoured it saw nothing. That is not untidy, it is dangerous: a seat setting
        // manual:true on a weapon row believed it had locked that row against an automatic pass,
        // and had not. The first derived orientation pass over weapons would have silently
        // overwritten all 81 owner-dialled poses — the structure side already paid this exact bill
        // (2026-08-18: an axis-bake zeroed corrections it believed redundant; the town lay down).
        //
        // READ BY: EquipmentController's derived-seat gate, via WeaponOrientHelper.ResolveSource
        // (precedence: authored offset row -> manual -> derived -> archetype default). A
        // manual:true row is left EXACTLY as loaded — not normalized, not rotated, not shifted.
        // Absent in JSON => false => the row is eligible for derivation (the 15 hand-authored
        // rows, including knight_shield_starter and the starter sword/bow).
        /// <summary>Orient canon: this row's seat is owner-dialled. A derived/auto orientation pass
        /// MUST leave it untouched (ARCHITECTURE_PRINCIPLES.md §4, WO-1123).</summary>
        public bool manual;

        // ── WO-1215: `generated` — the term that tells `manual` apart from a machine stamp ──────
        // Assets/Editor/Catalog/GearCatalogGenerator.cs emits every stub row with
        //   ["generated"] = true,    // distinguishes generated from authored
        //   ["manual"]    = false,   // set true by hand to lock the row forever
        // (:386-387). So `generated:true` + `manual:true` on the same row is a CONTRADICTION: the
        // generator never writes that pair. 77 of the 96 rows in weapons.json carry it anyway,
        // because commit af96fe788 — a data-only WO-500 BALANCE pass — stamped manual on all 65
        // blink_ rows ("blink_ rows 65, manual=true 65/65", its own body). None of them was ever
        // seated by hand; 18 of the 19 shields have no Offset Forge row at all.
        //
        // Until this field existed Newtonsoft dropped `generated` on deserialize exactly as it once
        // dropped `manual` (see the note above), so the runtime had no way to tell an owner-dialled
        // row from a machine-stamped one and had to believe the stamp. The consequence was the
        // WO-1215 defect: `manual` vetoed the derived shield seat to protect a pose that did not
        // exist, leaving the shield at IDENTITY through the hero's body.
        //
        // READ BY: WeaponOrientHelper.ManualSeatIsSubstantiated, via EquipmentController's
        // IsGeneratedCatalogRow. It NARROWS nothing that a human authored — a `generated:false` row
        // is trusted unconditionally, and a generated row WITH an authored seat (tripo_shield_a ->
        // shield_A) stays canon.
        /// <summary>Catalog provenance: this row was machine-emitted by GearCatalogGenerator. A
        /// `manual: true` on such a row cannot record an owner-dialled seat (WO-1215).</summary>
        public bool generated;

        /// <summary>WO-295: part of the legendary Aegis of Elarion set.</summary>
        public bool IsAegis =>
            !string.IsNullOrEmpty(setId) && setId.Equals("aegis", StringComparison.OrdinalIgnoreCase);

        // ── HAND-SLOT predicates (docs/STORE_EQUIP_SPEC.md "Equip-slot rules") ──────
        /// <summary>True when this weapon occupies BOTH hands (hand=="2h"). Absent/empty => 1h.</summary>
        public bool IsTwoHanded =>
            !string.IsNullOrEmpty(hand) && hand.Trim().Equals("2h", StringComparison.OrdinalIgnoreCase);

        /// <summary>True when this is an OFF-HAND item (a shield). Seats in the off hand, never the main hand.</summary>
        public bool IsOffHandItem =>
            !string.IsNullOrEmpty(category) && category.Trim().Equals("shield", StringComparison.OrdinalIgnoreCase);

        /// <summary>True when this is a one-handed MAIN-HAND weapon (1h and not a shield/off-hand item).</summary>
        public bool IsOneHandedMain => !IsTwoHanded && !IsOffHandItem;
    }

    /// <summary>A piece of armor: defense = fractional incoming-damage reduction (0.04 = 4%).</summary>
    [Serializable]
    public sealed class ArmorDef
    {
        public string id;
        public string name;
        public string icon;
        public string job;
        // ARMOR WEIGHT CLASS (owner 2026-06-16 "armor lightweight heavy distinction"):
        // "light" = Ranger + Mage may wear it; "heavy" = Knight + Cleric. Empty / "any" =
        // no weight restriction (a universal starter / endgame set anyone can wear).
        // Weapons stay 1:1 by `job`; armor groups by this weight instead. See
        // GearCatalog.ClassWeight / ArmorFitsClass.
        public string weight;
        public string rarity;
        // WO-1067: armor is certified from its normalized 2D identity sheet + live stat proof.
        public string visualReadiness;
        public string visualEvidence;
        public string visualFailureReason;
        public bool IsVisuallyReady => string.Equals(visualReadiness, "ready", StringComparison.OrdinalIgnoreCase);
        public bool HasVisualCertification => !string.IsNullOrWhiteSpace(visualReadiness);
        public float defense;     // 0..0.9 fractional damage reduction
        public float hpBonus;     // carried for a later pass; v1 applies defense only
        public GearReq req;

        // WO-295: set key (see WeaponDef.setId). The Aegis armor carries "aegis";
        // AegisSetEffect needs BOTH an Aegis weapon and Aegis armor for the full set.
        public string setId;

        // WO-295: saga flavour text (names the four crafters). Optional; null for v1 gear.
        public string saga;

        // WO-300: Elarion weaponsmithing lore (see WeaponDef). Optional + default empty.
        public string flavor;
        public string makersMark;

        // Shop integration (basic): resource costs. Populated from canonical JSON.
        public int buyWood;
        [UnityEngine.Serialization.FormerlySerializedAs("buyFood")]
        [Newtonsoft.Json.JsonProperty("buyFood")] public int buyStone;
        public int buyIron;
        public int buyCrystals;

        // WO-Item-1: the catalog⊥repo LOOK half (docs/ITEM_MODEL.md §3). See WeaponDef.
        // NULL for now — WO-Item-2's generator populates them; do NOT hand-author here.
        public string prefabPath;
        public string iconPath;

        // WO-Item (Addressables): HOW prefabPath loads (mirrors WeaponDef.loadVia). The Blink
        // generator stamps "addressable" on every Blink armor row (prefabPath = a "gear/armor/.."
        // Addressables key); null/"resources" => a Resources path. HeroArmorVisual reads this.
        public string loadVia;

        // WO-Item-1: OPTIONAL explicit capability override from JSON (null when absent).
        public ItemCapability? capabilities;

        /// <summary>WO-Item-1: the entry's resolved capability flags. Gear/Armor defaults to
        /// Carriable|Equippable (docs/ITEM_MODEL.md §2/§3); an explicit JSON `capabilities`
        /// override wins when present. Systems read THIS, never the catalog-of-origin.</summary>
        public ItemCapability Capabilities =>
            capabilities ?? (ItemCapability.Carriable | ItemCapability.Equippable);

        /// <summary>WO-295: part of the legendary Aegis of Elarion set.</summary>
        public bool IsAegis =>
            !string.IsNullOrEmpty(setId) && setId.Equals("aegis", StringComparison.OrdinalIgnoreCase);
    }

    [Serializable] public sealed class WeaponCatalogData { public List<WeaponDef> weapons; }
    [Serializable] public sealed class ArmorCatalogData  { public List<ArmorDef> armor; }

    /// <summary>
    /// Static loader + query surface for the weapon / armor catalogs. Graceful: every
    /// query null-guards, so a missing/empty catalog simply yields no gear (the hero
    /// falls back to a 1.0 multiplier / 0 defense — existing combat is unchanged).
    /// </summary>
    public static class GearCatalog
    {
        private const string WeaponsPath     = "Data/Canonical/weapons.json";
        private const string ArmorPath       = "Data/Canonical/armor.json";
        private const string AccessoriesPath = "Data/Canonical/accessories.json";

        private static List<WeaponDef>    _weapons;
        private static List<ArmorDef>     _armor;
        private static List<AccessoryDef> _accessories;

        /// <summary>Forces a re-read of all catalogs (weapons + armor + accessories).</summary>
        public static void Reload()
        {
            _weapons = null;
            _armor = null;
            _accessories = null;
            EnsureLoaded();
        }

        /// <summary>
        /// The outcome of ONE main-hand auto-best query, together with the counts that EXPLAIN it.
        ///
        /// WHY THE COUNTS ARE PART OF THE RETURN (CLAUDE.md §12). A bare <see cref="WeaponDef"/>
        /// cannot tell a capture apart: "the class has no eligible rows at this level" and "the
        /// class has forty eligible rows but the player OWNS none of them" both read as a null,
        /// and they have two completely different fixes. Carrying the counts out of the ranking
        /// loop is the only place they can be measured without walking the catalog a second time.
        /// </summary>
        public readonly struct WeaponPick
        {
            /// <summary>The winning weapon, or null when nothing passed the gates.</summary>
            public readonly WeaponDef Weapon;
            /// <summary>Rows that passed the SLOT + class + level gates (before any ownership filter).</summary>
            public readonly int Eligible;
            /// <summary>Of those, how many survived the ownership filter (== Eligible when unfiltered).</summary>
            public readonly int Owned;
            /// <summary>True when an ownership predicate was supplied and applied.</summary>
            public readonly bool OwnershipApplied;

            public WeaponPick(WeaponDef weapon, int eligible, int owned, bool ownershipApplied)
            {
                Weapon = weapon;
                Eligible = eligible;
                Owned = owned;
                OwnershipApplied = ownershipApplied;
            }
        }

        /// <summary>
        /// THE one main-hand ranking loop. Highest-damageMult MAIN-HAND weapon the given class+level
        /// can equip, optionally restricted to the ids the player actually OWNS.
        ///
        /// EXCLUDES off-hand items (shields). weapons.json carries shields as rows too, so without
        /// this gate a shield could win the main-hand pick on a damageMult tie and be handed to
        /// EnforceHandSlots, which moves it to the off hand and leaves the MAIN HAND EMPTY. That is
        /// exactly what happened when mage_starter was removed on 2026-08-02: 'tripo_shield_a' tied
        /// 'tripo_staff_a' at damageMult 1.0, won on file order, and a level-1 Mage spawned unarmed.
        ///
        /// ── THE OWNERSHIP GATE (owner-felt defect, 2026-08-08) ────────────────────────────────
        /// <paramref name="owns"/> is the fix for the FREE-FORGE-ITEM leak. This loop ranks purely
        /// by damageMult, and it used to rank across the WHOLE CATALOG — so the moment a knight
        /// reached level 2 (where GearLoadout's authored starter kit stops applying, StarterKit.
        /// MainHandUpToLevel == 1) it handed over `knight_flameblade` (damageMult 1.2) instead of
        /// `knight_starter` (1.0). knight_flameblade is a PURCHASABLE Forge item — every knight who
        /// levelled once without ever shopping was given a paid weapon for free, which is why the
        /// owner opened her demo recording holding a flaming sword. Auto-upgrade-on-level-up is
        /// INTENDED (WO-860) and stays; what was wrong was its CANDIDATE SET.
        ///
        /// Pass <paramref name="owns"/> = null for the legacy CATALOG-WIDE behaviour. That is
        /// correct — and deliberately kept — for the callers that are NOT asking "what should the
        /// hero auto-equip": the arena / outpost LOOT rolls (they are choosing a prize to GRANT,
        /// so the player necessarily does not own it yet) and the armed-hero oracles (which assert
        /// the catalog can serve every class at every level, independent of any save).
        ///
        /// <paramref name="oneHandedOnly"/> narrows the slot gate from "not a shield" to
        /// "one-handed main hand" — the hand-slot fall-back case (see BestOneHandedWeapon).
        /// </summary>
        public static WeaponPick PickBestWeapon(string job, int level, Func<string, bool> owns,
                                                bool oneHandedOnly = false)
        {
            EnsureLoaded();
            WeaponDef best = null;
            int eligible = 0;
            int owned = 0;
            bool applied = owns != null;

            if (_weapons != null)
            {
                foreach (var w in _weapons)
                {
                    if (w == null) continue;
                    if (oneHandedOnly ? !w.IsOneHandedMain : w.IsOffHandItem) continue;
                    if (!JobMatches(w.job, job) || !MeetsReq(w.req, level)) continue;
                    eligible++;
                    if (applied && !owns(w.id)) continue;
                    owned++;
                    if (best == null || w.damageMult > best.damageMult) best = w;
                }
            }

            return new WeaponPick(best, eligible, owned, applied);
        }

        /// <summary>
        /// Highest-damageMult MAIN-HAND weapon the given class+level can equip, or null.
        /// CATALOG-WIDE (no ownership gate) — see <see cref="PickBestWeapon"/> for why that is
        /// correct here and wrong for the hero's auto-equip. Kept as the loot-roll / oracle entry
        /// point so those callers read exactly as before.
        /// </summary>
        public static WeaponDef BestWeapon(string job, int level)
        {
            return PickBestWeapon(job, level, null).Weapon;
        }

        /// <summary>
        /// The outcome of ONE armor auto-best query with the counts that EXPLAIN it — the armor
        /// twin of <see cref="WeaponPick"/>, and for the same §12 reason: "this class has no
        /// eligible armor at this level" and "this class has nine eligible rows and the player
        /// owns none of them" both read as a null, and they have two different fixes.
        /// </summary>
        public readonly struct ArmorPick
        {
            /// <summary>The winning armor, or null when nothing passed the gates.</summary>
            public readonly ArmorDef Armor;
            /// <summary>Rows that passed the job + weight + level gates (before any ownership filter).</summary>
            public readonly int Eligible;
            /// <summary>Of those, how many survived the ownership filter (== Eligible when unfiltered).</summary>
            public readonly int Owned;
            /// <summary>True when an ownership predicate was supplied and applied.</summary>
            public readonly bool OwnershipApplied;

            public ArmorPick(ArmorDef armor, int eligible, int owned, bool ownershipApplied)
            {
                Armor = armor;
                Eligible = eligible;
                Owned = owned;
                OwnershipApplied = ownershipApplied;
            }
        }

        /// <summary>
        /// THE one armor ranking loop. Highest-defense armor the given class+level can equip,
        /// optionally restricted to the ids the player actually OWNS.
        ///
        /// ── WO-1240: THE ARMOR HALF OF THE OWNERSHIP GATE ──────────────────────────────────
        /// The weapon half was closed on 2026-08-08; the armor half was left open ON PURPOSE and
        /// the reason was written into GearLoadout.Refresh: there was NO authored starter ARMOR,
        /// so gating auto-equip to owned gear resolved to null on a fresh save and dropped the
        /// hero to ArmorDefense 0 — trading an economy leak for a silent difficulty spike. The
        /// owner ruled BOTH halves (2026-08-26): author a starter armor row for every class FIRST
        /// (StarterLoadout.StarterKit.Armor), THEN gate. This overload is the second half; it is
        /// only safe because the first half exists.
        ///
        /// Pass <paramref name="owns"/> = null for the legacy CATALOG-WIDE behaviour — correct and
        /// deliberately kept for the callers that are NOT asking "what should the hero auto-equip":
        /// the arena / outpost LOOT rolls (choosing a prize to GRANT, which the player by
        /// definition does not own yet) and the never-zero floor in GearLoadout.
        ///
        /// BOTH class gates are asked, and they are NOT the same question: <see cref="JobMatches"/>
        /// is the authored `job` field (WO-1241 may now list several classes) and
        /// <see cref="ArmorFitsClass"/> is the light/heavy WEIGHT rule. A row can be legal by one
        /// and illegal by the other — that mismatch is exactly what WO-1241 exists to stop.
        /// </summary>
        public static ArmorPick PickBestArmor(string job, int level, Func<string, bool> owns)
        {
            EnsureLoaded();
            ArmorDef best = null;
            int eligible = 0;
            int owned = 0;
            bool applied = owns != null;

            if (_armor != null)
            {
                foreach (var a in _armor)
                {
                    if (a == null || !JobMatches(a.job, job) || !ArmorFitsClass(a, job) || !MeetsReq(a.req, level)) continue;
                    eligible++;
                    if (applied && !owns(a.id)) continue;
                    owned++;
                    if (best == null || a.defense > best.defense) best = a;
                }
            }

            return new ArmorPick(best, eligible, owned, applied);
        }

        /// <summary>Highest-defense armor the given class+level can equip, or null.
        /// Respects BOTH the legacy `job` gate and the new weight-class gate (light/heavy).
        /// CATALOG-WIDE (no ownership gate) — see <see cref="PickBestArmor"/> for why that is
        /// correct for loot rolls and WRONG for the hero's auto-equip.</summary>
        public static ArmorDef BestArmor(string job, int level)
        {
            return PickBestArmor(job, level, null).Armor;
        }

        /// <summary>
        /// Highest-damageMult ONE-HANDED MAIN-HAND weapon (1h, not a shield) the class+level can
        /// equip, or null. Used by the hand-slot enforcement as the main-hand fall-back when a 2H
        /// weapon is removed (equipping a shield while a 2H is held) — keeps the armed-hero invariant:
        /// a 1H can coexist with the off-hand, a 2H cannot.
        /// </summary>
        public static WeaponDef BestOneHandedWeapon(string job, int level)
        {
            return PickBestWeapon(job, level, null, oneHandedOnly: true).Weapon;
        }

        // ── Class restriction surface (equip UI + auto-equip) ────────────────────

        /// <summary>The armor WEIGHT a class wears: Ranger/Mage = "light", Knight/Cleric =
        /// "heavy" (owner 2026-06-16). Unknown classes default to "heavy" (front-line safe).</summary>
        public static string ClassWeight(string job)
        {
            switch ((job ?? string.Empty).ToLowerInvariant())
            {
                case "ranger":
                case "mage":   return "light";
                case "knight":
                case "cleric": return "heavy";
                default:       return "heavy";
            }
        }

        /// <summary>True when <paramref name="job"/> may wear armor <paramref name="a"/>: a
        /// piece with no weight (empty / "any") fits everyone; otherwise its weight must
        /// match the class's weight (light↔Ranger/Mage, heavy↔Knight/Cleric).</summary>
        public static bool ArmorFitsClass(ArmorDef a, string job)
        {
            if (a == null) return false;
            string w = (a.weight ?? string.Empty).Trim().ToLowerInvariant();
            if (w.Length == 0 || w == "any") return true;
            return w == ClassWeight(job);
        }

        /// <summary>
        /// True when armour <paramref name="a"/>'s authored `job` admits <paramref name="job"/> —
        /// the JOB half of armour eligibility ONLY, with the light/heavy weight half deliberately
        /// NOT asked. Public because WO-1240's never-naked floor has to ask both gates separately
        /// to report which one a broken starter row failed; re-implementing the job match in the
        /// caller is how the floor and the equip seam end up disagreeing about the same row.
        /// </summary>
        public static bool ArmorJobMatches(ArmorDef a, string job) => a != null && JobMatches(a.job, job);

        /// <summary>True when <paramref name="job"/> may wield weapon <paramref name="w"/>
        /// (its `job` is "any" or matches the class). Public wrapper over the job-match rule
        /// so the equip UI can filter the weapon list per selected party member.</summary>
        public static bool WeaponFitsClass(WeaponDef w, string job)
        {
            return w != null && JobMatches(w.job, job);
        }

        /// <summary>Find exact weapon by id (case-insensitive), or null. Used by shop/equip flows.</summary>
        public static WeaponDef FindWeapon(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            EnsureLoaded();
            if (_weapons == null) return null;
            foreach (var w in _weapons)
            {
                if (w != null && string.Equals(w.id, id, StringComparison.OrdinalIgnoreCase)) return w;
            }
            return null;
        }

        /// <summary>Find exact armor by id (case-insensitive), or null. Used by shop/equip flows.</summary>
        public static ArmorDef FindArmor(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            EnsureLoaded();
            if (_armor == null) return null;
            foreach (var a in _armor)
            {
                if (a != null && string.Equals(a.id, id, StringComparison.OrdinalIgnoreCase)) return a;
            }
            return null;
        }

        /// <summary>Find exact accessory (ring/amulet) by id (case-insensitive), or null. Used by shop/equip flows.</summary>
        public static AccessoryDef FindAccessory(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            EnsureLoaded();
            if (_accessories == null) return null;
            foreach (var ac in _accessories)
            {
                if (ac != null && string.Equals(ac.id, id, StringComparison.OrdinalIgnoreCase)) return ac;
            }
            return null;
        }

        /// <summary>All loaded accessory defs (for the Jeweler stock + EquipVM slot lists). Never null.</summary>
        public static IReadOnlyList<AccessoryDef> Accessories
        {
            get
            {
                EnsureLoaded();
                return _accessories ?? (IReadOnlyList<AccessoryDef>)System.Array.Empty<AccessoryDef>();
            }
        }

        /// <summary>All loaded accessory defs (alias of <see cref="Accessories"/> for vendor enumeration). Never null.</summary>
        public static IReadOnlyList<AccessoryDef> AllAccessories() => Accessories;

        /// <summary>Accessories that seat in <paramref name="slot"/> ("ring"/"amulet") and meet the level
        /// requirement. Job is "any" for v1 accessories, so only slot + level gate. Never null.</summary>
        public static IReadOnlyList<AccessoryDef> AccessoriesForSlot(string slot, int level)
        {
            EnsureLoaded();
            var list = new List<AccessoryDef>();
            if (_accessories == null || string.IsNullOrEmpty(slot)) return list;
            string s = slot.Trim().ToLowerInvariant();
            foreach (var ac in _accessories)
            {
                if (ac == null) continue;
                string acSlot = (ac.slot ?? string.Empty).Trim().ToLowerInvariant();
                if (acSlot != s) continue;
                if (!MeetsReq(ac.req, level)) continue;
                list.Add(ac);
            }
            return list;
        }

        /// <summary>All loaded weapon defs (for vendor stock enumeration). Never null.</summary>
        public static IReadOnlyList<WeaponDef> AllWeapons()
        {
            EnsureLoaded();
            return _weapons ?? (IReadOnlyList<WeaponDef>)System.Array.Empty<WeaponDef>();
        }

        /// <summary>All loaded armor defs (for vendor stock enumeration). Never null.</summary>
        public static IReadOnlyList<ArmorDef> AllArmors()
        {
            EnsureLoaded();
            return _armor ?? (IReadOnlyList<ArmorDef>)System.Array.Empty<ArmorDef>();
        }

        /// <summary>GOLD buy price for an accessory (for Economy.TrySpend). Mirrors the weapon/armor
        /// overloads: the price is GearAppraisal's estimated value, floored at 1. Null-safe.</summary>
        public static DeNelle.Village.ResourceCost GetBuyCost(AccessoryDef ac)
        {
            if (ac == null) return default;
            return new DeNelle.Village.ResourceCost(coins: GoldPrice(GearAppraisal.Appraise(ac)));
        }

        /// <summary>
        /// GOLD buy price for a weapon (for Economy.TrySpend). Vendor SHOPS now charge GOLD
        /// (Coins), not Wood/Iron/Crystals — the price is GearAppraisal's estimated value
        /// (tier + stats + Elarion-mark premium), the same number the buy label appraises.
        /// Floored at 1 so nothing is ever free. The legacy buy* JSON fields are retained on
        /// the def but no longer drive the shop cost. (Building UPGRADES stay on resources.)
        /// </summary>
        public static DeNelle.Village.ResourceCost GetBuyCost(WeaponDef w)
        {
            if (w == null) return default;
            return new DeNelle.Village.ResourceCost(coins: GoldPrice(GearAppraisal.Appraise(w)));
        }

        /// <summary>GOLD buy price for a piece of armor (for Economy.TrySpend). See the weapon overload.</summary>
        public static DeNelle.Village.ResourceCost GetBuyCost(ArmorDef a)
        {
            if (a == null) return default;
            return new DeNelle.Village.ResourceCost(coins: GoldPrice(GearAppraisal.Appraise(a)));
        }

        /// <summary>The gold price = the appraised estimated value, floored at 1. Null-safe.</summary>
        private static int GoldPrice(GearAppraisalResult appraisal)
        {
            int v = appraisal != null ? appraisal.estimatedValue : 0;
            return Mathf.Max(1, v);
        }

        /// <summary>
        /// THE job-gate authority for every catalog query (weapons, armor, accessories).
        ///
        /// WO-1241 — <paramref name="itemJob"/> may now be a COMMA-SEPARATED LIST of class keys
        /// ("knight,cleric"). WHY: before this it was a single token, so the only way to author
        /// "this piece is for the two LIGHT classes" was `job: "any"` — and `any` is not a
        /// narrowing at all, it is the absence of one. That is exactly how `blink_armor_dragonic`
        /// (job "any", weight "heavy") ended up OFFERED to a Mage that CanEquipArmorNow will
        /// always refuse: the weight gate was doing the real eligibility work while the job gate
        /// said "everyone". The list form lets the DATA say what the weight gate already enforces,
        /// so a job-based loot/shop filter can no longer disagree with the equip seam.
        ///
        /// A single token behaves EXACTLY as before (no comma => the old ordinal-ignore-case
        /// compare), so every existing weapon/armor/accessory row is byte-identical in effect.
        /// "any" (alone or inside a list) still fits every class — that is the deliberate,
        /// whitelisted universal, pinned by ArmourCatalogJobRegression.
        /// </summary>
        private static bool JobMatches(string itemJob, string heroJob)
        {
            if (string.IsNullOrEmpty(itemJob)) return true;
            string item = itemJob.Trim();
            if (item.Length == 0) return true;
            if (item.Equals("any", StringComparison.OrdinalIgnoreCase)) return true;

            string hero = (heroJob ?? string.Empty).Trim();
            if (item.IndexOf(',') < 0) return item.Equals(hero, StringComparison.OrdinalIgnoreCase);

            var parts = item.Split(',');
            for (int i = 0; i < parts.Length; i++)
            {
                string p = parts[i].Trim();
                if (p.Length == 0) continue;
                if (p.Equals("any", StringComparison.OrdinalIgnoreCase)) return true;
                if (p.Equals(hero, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        /// <summary>
        /// The `job` field as a player reads it, for the shop / inventory blurbs: "any class",
        /// "the knight", "the knight or cleric", "the knight, ranger or mage".
        ///
        /// PUBLIC and shared because WO-1241 made `job` a possible LIST, and the three
        /// DescribeGear copies (the deleted ShopVM / InventoryVM / PartyShopVM) each pasted the raw string
        /// straight into a sentence — which would have shipped "Suited to the knight,cleric."
        /// to the player. ASCII only (CLAUDE.md canon).
        /// </summary>
        public static string JobLabel(string itemJob)
        {
            string item = (itemJob ?? string.Empty).Trim();
            if (item.Length == 0 || item.Equals("any", StringComparison.OrdinalIgnoreCase)) return "any class";

            var names = new List<string>();
            var parts = item.Split(',');
            for (int i = 0; i < parts.Length; i++)
            {
                string p = parts[i].Trim();
                if (p.Length == 0) continue;
                if (p.Equals("any", StringComparison.OrdinalIgnoreCase)) return "any class";
                names.Add(p.ToLowerInvariant());
            }
            if (names.Count == 0) return "any class";
            if (names.Count == 1) return "the " + names[0];

            var sb = new System.Text.StringBuilder("the ");
            for (int i = 0; i < names.Count; i++)
            {
                if (i > 0) sb.Append(i == names.Count - 1 ? " or " : ", ");
                sb.Append(names[i]);
            }
            return sb.ToString();
        }

        /// <summary>
        /// True when a wearer at <paramref name="level"/> meets the item's equip requirement.
        ///
        /// PUBLIC (F8 seq-642 Fix B). This was `private static`, which meant the EQUIP SEAM
        /// (GearLoadout.EquipWeaponById / EquipOffHandById / EquipArmorById) physically could
        /// not ask the same question the auto-best queries ask - so a manual equip enforced
        /// NEITHER the class gate nor the level gate. The hole was masked only because the
        /// shop/equip UI pre-filters its lists; every non-UI caller (arena grants, outpost
        /// drops, story grants, AutoPilot) went straight through it.
        /// v1: level only. (dex/arcane/might carried but not yet enforced.)
        /// </summary>
        public static bool MeetsReq(GearReq req, int level)
        {
            return req == null || level >= req.level;
        }

        /// <summary>The level an item demands (1 when it carries no req block). For log text.</summary>
        public static int RequiredLevel(GearReq req) => req != null ? req.level : 1;

        /// <summary>
        /// The ONE equip-eligibility question for a weapon / off-hand item: does it fit the
        /// class AND does the wearer meet its level requirement? <paramref name="reason"/>
        /// carries a player-diagnosable explanation on refusal (never empty on false), so the
        /// caller can Warn with real detail instead of failing silently.
        /// </summary>
        public static bool CanEquipWeapon(WeaponDef w, string job, int level, out string reason)
        {
            if (w == null) { reason = "no WeaponDef"; return false; }
            if (!WeaponFitsClass(w, job))
            {
                reason = "class gate: item job='" + (w.job ?? "<null>") + "' does not fit wearer class '" +
                         (job ?? "<null>") + "'";
                return false;
            }
            if (!MeetsReq(w.req, level))
            {
                reason = "level gate: requires level " + RequiredLevel(w.req) + ", wearer is level " + level;
                return false;
            }
            reason = null;
            return true;
        }

        /// <summary>
        /// The ONE equip-eligibility question for armor. Mirrors <see cref="BestArmor"/> exactly
        /// - BOTH the legacy `job` gate and the weight-class gate (light/heavy) - plus the level
        /// requirement, so what a player may MANUALLY equip is precisely what auto-best may pick.
        /// </summary>
        public static bool CanEquipArmor(ArmorDef a, string job, int level, out string reason)
        {
            if (a == null) { reason = "no ArmorDef"; return false; }
            if (!JobMatches(a.job, job))
            {
                reason = "class gate: item job='" + (a.job ?? "<null>") + "' does not fit wearer class '" +
                         (job ?? "<null>") + "'";
                return false;
            }
            if (!ArmorFitsClass(a, job))
            {
                reason = "weight gate: item weight='" + (a.weight ?? "<null>") + "' but class '" +
                         (job ?? "<null>") + "' wears '" + ClassWeight(job) + "'";
                return false;
            }
            if (!MeetsReq(a.req, level))
            {
                reason = "level gate: requires level " + RequiredLevel(a.req) + ", wearer is level " + level;
                return false;
            }
            reason = null;
            return true;
        }

        // =====================================================================
        //  WO-1214 - THE PLAYER-FACING EQUIP QUESTION (Rulings 2, 3 and 4)
        // ---------------------------------------------------------------------
        //  CanEquipWeapon / CanEquipArmor above answer for the LOG: their reason
        //  strings name fields and gates. Ruling 2 requires the same refusal to
        //  reach the PLAYER in words ("Mages cannot use shields"), never as a
        //  greyed control and never by colour alone (the owner is red/green
        //  colourblind). Rather than let the UI re-derive the rule - which is how
        //  the arena and outpost loot rolls each grew their own copy of
        //  JobMatches and disagreed with the auto-best queries - the words are
        //  produced HERE, by the same authority, and both the equip seam
        //  (GearLoadout) and the equip UI (EquipVM / InventoryVM) call it.
        //
        //  ASCII ONLY in every player-facing string below (CLAUDE.md canon).
        // =====================================================================

        /// <summary>Display name for a weapon row (falls back to the id). Never null.</summary>
        public static string ItemLabel(WeaponDef w) =>
            w == null ? "that item" : (string.IsNullOrEmpty(w.name) ? (w.id ?? "that item") : w.name);

        /// <summary>Display name for an armor row (falls back to the id). Never null.</summary>
        public static string ItemLabel(ArmorDef a) =>
            a == null ? "that item" : (string.IsNullOrEmpty(a.name) ? (a.id ?? "that item") : a.name);

        /// <summary>The class name as a player reads it ("mage" -> "Mage"). "This hero" when unknown.</summary>
        public static string ClassLabel(string job)
        {
            string j = (job ?? string.Empty).Trim();
            if (j.Length == 0) return "This hero";
            return char.ToUpperInvariant(j[0]) + j.Substring(1).ToLowerInvariant();
        }

        /// <summary>
        /// True when the catalog serves this class ANY one-handed main-hand weapon at this level.
        ///
        /// Deliberately CATALOG-WIDE (owns = null): the question is "does the game contain a
        /// weapon that could keep this hero armed", not "does the player own one". A false here
        /// is the precondition of the WO-1214 disarm - a 2H main hand cannot coexist with an
        /// off-hand, so evicting it with nothing to put back leaves the hero holding a shield and
        /// nothing else, with no in-game way to recover.
        /// </summary>
        public static bool HasOneHandedMainHand(string job, int level) =>
            PickBestWeapon(job, level, null, oneHandedOnly: true).Weapon != null;

        /// <summary>
        /// THE full equip question for a weapon / off-hand item, in one call: class gate, level
        /// gate, and the WO-1214 Ruling 4 armed-hero gate that <see cref="CanEquipWeapon"/>
        /// cannot ask because it does not know what the wearer is already holding.
        ///
        /// <paramref name="currentMainHand"/> is the wearer's MAIN hand right now (null when
        /// empty). <paramref name="playerReason"/> is the sentence to SHOW; <paramref name="traceReason"/>
        /// is the detail to LOG. Both are null on success and never null on refusal.
        /// </summary>
        public static bool CanEquipWeaponNow(WeaponDef w, WeaponDef currentMainHand, string job, int level,
                                             out string playerReason, out string traceReason)
        {
            if (w == null)
            {
                playerReason = "That item is not in the armory.";
                traceReason = "no WeaponDef";
                return false;
            }

            if (!CanEquipWeapon(w, job, level, out traceReason))
            {
                playerReason = !WeaponFitsClass(w, job)
                    ? "A " + ClassLabel(job) + " cannot use " + ItemLabel(w) + ". It stays in your bag - you can sell it."
                    : ItemLabel(w) + " needs level " + RequiredLevel(w.req) + ". You are level " + level +
                      ". It stays in your bag until then.";
                return false;
            }

            // Ruling 4 - the armed-hero invariant FAILS CLOSED. Putting an off-hand item in the
            // off slot evicts a two-handed main hand (a 2H takes both hands). If the catalog has
            // no one-handed weapon this class could hold instead, that eviction disarms the hero
            // permanently. Refuse the OFF-HAND rather than ship an unarmed hero.
            if (w.IsOffHandItem && currentMainHand != null && currentMainHand.IsTwoHanded &&
                !HasOneHandedMainHand(job, level))
            {
                playerReason = ItemLabel(currentMainHand) + " needs both hands, and a " + ClassLabel(job) +
                               " has no one-handed weapon to hold instead. Equipping " + ItemLabel(w) +
                               " would leave you unarmed, so it stays in your bag - you can sell it.";
                traceReason = "armed-hero invariant (WO-1214 Ruling 4): off-hand '" + (w.id ?? "<null>") +
                              "' would evict 2H main '" + (currentMainHand.id ?? "<null>") +
                              "' and the catalog serves class '" + (job ?? "<null>") + "' NO one-handed " +
                              "main-hand at level " + level + " - the equip is REFUSED, not degraded to null";
                return false;
            }

            playerReason = null;
            traceReason = null;
            return true;
        }

        /// <summary>
        /// The full equip question for ARMOR, in player words. Same gates as
        /// <see cref="CanEquipArmor"/> (class + light/heavy weight + level); armor has no
        /// hand-slot interaction, so there is no Ruling 4 branch here.
        /// </summary>
        public static bool CanEquipArmorNow(ArmorDef a, string job, int level,
                                            out string playerReason, out string traceReason)
        {
            if (a == null)
            {
                playerReason = "That item is not in the armory.";
                traceReason = "no ArmorDef";
                return false;
            }

            if (CanEquipArmor(a, job, level, out traceReason))
            {
                playerReason = null;
                traceReason = null;
                return true;
            }

            if (!JobMatches(a.job, job) || !ArmorFitsClass(a, job))
            {
                playerReason = "A " + ClassLabel(job) + " cannot wear " + ItemLabel(a) + " (" +
                               ClassLabel(job) + "s wear " + ClassWeight(job) + " armor). It stays in your bag - " +
                               "you can sell it.";
            }
            else
            {
                playerReason = ItemLabel(a) + " needs level " + RequiredLevel(a.req) + ". You are level " +
                               level + ". It stays in your bag until then.";
            }
            return false;
        }

        private static void EnsureLoaded()
        {
            if (_weapons     == null) _weapons     = LoadWeapons();
            if (_armor       == null) _armor       = LoadArmor();
            if (_accessories == null) _accessories = LoadAccessories();
        }

        private static List<WeaponDef> LoadWeapons()
        {
            var data = LoadJson<WeaponCatalogData>(WeaponsPath, "weapons.json");
            return data?.weapons ?? new List<WeaponDef>();
        }

        private static List<ArmorDef> LoadArmor()
        {
            var data = LoadJson<ArmorCatalogData>(ArmorPath, "armor.json");
            return data?.armor ?? new List<ArmorDef>();
        }

        private static List<AccessoryDef> LoadAccessories()
        {
            var data = LoadJson<AccessoryCatalogData>(AccessoriesPath, "accessories.json");
            return data?.accessories ?? new List<AccessoryDef>();
        }

        private static T LoadJson<T>(string relativePath, string label) where T : class
        {
            // TODO adopt DataInjector: this is exactly DeNelle.Core.DataInjector.Inject<T>
            // (CanonicalJson.Read → JsonConvert.DeserializeObject<T>, WebGL-safe, guarded).
            // Kept inline for now so the gear-specific warning text ("gear disabled…") is
            // preserved; swap to DataInjector once a shared warning is acceptable.
            // WebGL-safe load via CanonicalJson (Resources first, StreamingAssets fallback).
            try
            {
                string json = DeNelle.Core.CanonicalJson.Read(relativePath);
                if (!string.IsNullOrEmpty(json))
                    return JsonConvert.DeserializeObject<T>(json);
                Debug.LogWarning($"[GearCatalog] {label} not found (Resources or StreamingAssets) — gear disabled (hero uses 1.0 mult / 0 defense).");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[GearCatalog] Failed to read {label}: {ex.Message}");
            }
            return null;
        }
    }
}

// =============================================================================
// EchoRosterCatalog -- the lightweight, data-driven Echo roster (owner felt-test
// 2026-07-17: "Echoes are portrait-card spirits, NOT 3D models... just let them
// live in the pet roster. When we unlock a new echo, they should start a dialogue").
// -----------------------------------------------------------------------------
// Assembly: DeNelle.Village   Namespace: DeNelle.Village
//
// WHY A CODE TABLE (not a Data/Canonical JSON): the roster is a FIXED, canonical
// 6-entry set with authored ASCII flavor -- there is nothing owner-tunable at
// runtime and no per-map variance. A static table is the LIGHTEST possible source
// (no JSON loader, no StreamingAssets read, no dual-copy/md5 dance, compile-time
// safe) and is still fully DATA-DRIVEN in the sense the owner asked for: the
// unlock dialogue + roster grid read WHICHEVER entry maps to the unlocked echo and
// fill portrait / name / element / flavor from it -- no per-echo hardcoded card.
// If the roster ever needs to grow owner-tunable, promote this to echoes.json under
// Data/Canonical then (dual-copy + md5) -- not before (LIGHTWEIGHT mandate).
//
// ORDER = the echo COUNT it corresponds to: echo #1 (Aldwin, the founding Ice Echo,
// owned from EchoCount==1) .. echo #6 (Maren, the Fire Echo). Each is the awakened
// ESSENCE of a named soul the Heart of Elarion guards (Aldwin, Elowen, Corvin, Bran,
// Doran, Maren -- memory echo-is-essence-of-guarded-person), NOT an elemental monster.
// PortraitName stays the old element file base (Frosthowl, VerdantStag, ...) so the
// Resources/Echoes/Portraits art keeps loading. EchoService.EchoCount 1..MaxEchoes
// indexes straight into this (order == count). A wave-unlock that raises the count
// to N fires the dialogue for ByCount(N) = the newly earned spirit.
//
// WO-830 AFFINITY MODEL (owner ruling 2026-08-02): every Echo prefers HARVEST and
// carries a distinct resource AFFINITY (Wood/Iron/Food/Gold/Crystals -- Crystals is
// the ONE deliberately doubled affinity, Bran + Maren). The PLAYER picks what each
// Echo harvests (EchoAssignments resource tokens); the affinity is a match BONUS
// when the pick lands on the Echo's calling, NEVER a lock. Pair synergies + the
// hidden tri-synergy (EchoBonusCalculator) key off the ACTUAL assignments.
// HarvestResource stays populated only for the three affinities that map to a real
// DeNelle.Core.ResourceType wallet split (Wood/Iron/Food); Gold routes to Coins and
// Crystals to the Aether wallet at DumpSilos time via the 5-way target weights.
//
// Portraits: Assets/Resources/Echoes/Portraits/<PortraitName>.(png|jpg). Loaded as
// Texture2D + Sprite.Create at runtime (owner: "no importer settings needed; guard
// null") -- see LoadPortrait, Guard-wrapped so a missing image logs + skips.
// Emergence art (WO-831): Assets/Resources/Echoes/Emergence/<PortraitName>_emerge.png
// (LFS; owner/art supplies) -- see LoadEmergence, Guard-wrapped with a portrait
// fallback so missing art NEVER blocks an unlock.
// =============================================================================
using UnityEngine;
using DeNelle.Core;
using DeNelle.Core.Diagnostics;

namespace DeNelle.Village
{
    /// <summary>The six canonical Echo elements (WO-738 identity axis). Distinct from the
    /// human-readable <see cref="EchoRosterEntry.Element"/> subtitle string, which stays for display.</summary>
    public enum ElementType
    {
        Nature,
        Shadow,
        Storm,
        Earth,
        Fire,
        Frost,
    }

    /// <summary>The functional Echo lanes (WO-738). Idle == unassigned. These are the AGENCY picks;
    /// the string lane vocabulary in <see cref="EchoAssignments"/> mirrors these tokens.</summary>
    public enum LaneType
    {
        Idle,
        Harvest,
        Crafting,
        Defense,
        Exploration,
        /// <summary>WO-811: the REPAIR task -- the Echo advances real repair on damaged
        /// structures (EchoRepairService through the WallRepairController backend).
        /// Appended LAST so no existing member's ordinal moves (the persisted grammar is
        /// string tokens, but ordinal stability costs nothing and prevents surprises).
        /// Deliberately NO affinity maps here: "Repairs" was REMOVED as an affinity
        /// (WO-830 owner ruling 2026-08-02 -- Maren harvests Crystals), so repair never
        /// earns a match bonus.</summary>
        Repair,
    }

    /// <summary>The five harvestable targets an Echo can be assigned to (WO-830). This is the
    /// resource-picker vocabulary AND the affinity axis: Wood/Iron/Food map to the classic
    /// <see cref="DeNelle.Core.ResourceType"/> silo split; Gold credits Coins and Crystals
    /// credits the Aether wallet at Dump time. Distinct from ResourceType on purpose --
    /// Gold is not a ResourceType, and extending the Core enum would ripple the save schema.</summary>
    public enum HarvestTarget
    {
        Wood = 0,
        Iron = 1,
        [System.Runtime.Serialization.EnumMember(Value = "Food")]
        Stone = 2,
        Gold = 3,
        Crystals = 4,
    }

    /// <summary>One canonical Echo spirit's card identity (immutable data row).</summary>
    public sealed class EchoRosterEntry
    {
        /// <summary>Stable id ("echo-frosthowl").</summary>
        public string Id;
        /// <summary>1-based order == the EchoCount this spirit corresponds to.</summary>
        public int Order;
        /// <summary>Card name, e.g. "Frosthowl (Ice Echo)".</summary>
        public string DisplayName;
        /// <summary>Element subtitle shown under the portrait, e.g. "Ice Elemental".</summary>
        public string Element;
        /// <summary>Portrait file base name under Resources/Echoes/Portraits/.</summary>
        public string PortraitName;
        /// <summary>The awakening flavor line (mockup tone, ASCII).</summary>
        public string Flavor;
        /// <summary>Extended lore revealed by the dialogue's "Tell me more" button (ASCII).</summary>
        public string Lore;
        /// <summary>WO-831: the one-line emergence intro shown BEFORE the awakening card
        /// (the 2D "rising from the Heart-tree" beat). ASCII, colorblind-safe TEXT.</summary>
        public string EmergeLine;

        // ── WO-738/830 specialization identity (derived, non-tunable -- element identity,
        //    NOT a balance knob; the tunable numbers live in echoes-balance.json). ──
        /// <summary>This spirit's element (WO-738 identity axis).</summary>
        public ElementType ElementType;
        /// <summary>The lane this spirit is best at. WO-830: ALL SIX prefer Harvest (every
        /// affinity is reachable); the per-resource calling lives in <see cref="Affinity"/>.</summary>
        public LaneType PreferredLane;
        /// <summary>WO-830: this spirit's harvest AFFINITY -- the resource pick that earns the
        /// match bonus. A guidance signal, never a lock (the player assigns freely).</summary>
        public HarvestTarget Affinity;
        /// <summary>For an affinity that maps to a real <see cref="ResourceType"/> wallet field
        /// (Wood/Iron/Food), that resource; null for Gold (Coins) and Crystals (Aether), which
        /// route through their own wallet movers at Dump time. Kept so pre-830 consumers of the
        /// classic three-way split keep working unchanged.</summary>
        public ResourceType? HarvestResource;
    }

    /// <summary>
    /// The fixed 6-spirit Echo roster (order == echo count). Read by the unlock
    /// dialogue (<see cref="EchoUnlockDialogue"/>) and the roster grid
    /// (<see cref="EchoRosterView"/>). Portrait sprites are created on demand.
    /// </summary>
    public static class EchoRosterCatalog
    {
        // ASCII-only flavor + lore (colorblind owner reads TEXT, not hue; glyph-safe TMP).
        // WO-830 affinity table (owner-approved 2026-08-01, amended 2026-08-02):
        //   Aldwin/Frost -> Food, Elowen/Nature -> Wood, Corvin/Shadow -> Gold,
        //   Bran/Storm -> Crystals, Doran/Earth -> Iron, Maren/Fire -> Crystals.
        // Crystals is deliberately DOUBLED (Bran + Maren); Repairs was removed 2026-08-02.
        private static readonly EchoRosterEntry[] s_all =
        {
            new EchoRosterEntry {
                Id = "echo-frosthowl", Order = 1,
                DisplayName = "Aldwin, the Ice Echo", Element = "Essence of a fallen keeper",
                PortraitName = "Frosthowl",
                // Founding-echo copy (WO-752). Order==1 is ONLY ever shown for the founding
                // spirit (waves unlock #2-6), so this row carries the awakening + the gather
                // teach -- the card EchoService.AnnounceFoundingEcho fires. An Echo is the
                // awakened ESSENCE of a soul the Heart of Elarion guards (memory
                // echo-is-essence-of-guarded-person), NOT an elemental monster. ASCII only.
                Flavor = "The Heart of Elarion remembers every soul it has guarded, and I was the first it kept -- Aldwin, a keeper of the old light, held safe in the tree until a new defender rose. I wake now as your Echo. While you rest I gather; while you fight I tend the fields. Name my task -- wood, iron, or grain -- and it is done.",
                Lore = "In life I kept the last lantern of Elarion burning through the long dark. When I fell, the Heart drew my essence down among its roots and held it close. Every keeper the tree remembers wakes one Echo before all others -- I am yours. Bring the light back to the Heart, and I grow stronger with it.",
                EmergeLine = "The Heart stirs -- its first-kept soul rises to meet you.",
                // WO-830: founding card "tend the fields... grain" = winter stores -> Food.
                ElementType = ElementType.Frost, PreferredLane = LaneType.Harvest,
                Affinity = HarvestTarget.Stone, HarvestResource = ResourceType.Stone,
            },
            new EchoRosterEntry {
                Id = "echo-verdant-stag", Order = 2,
                DisplayName = "Elowen, the Nature Echo", Element = "Essence of a grove-warden",
                PortraitName = "VerdantStag",
                Flavor = "Green light stirs among the roots, and Elowen lifts her head to your call -- the grove-warden who once walked Elarion's every furrow, wakened now from the tree that keeps her.",
                Lore = "Elowen tended the fields and the forest edge until her last season turned. The Heart could not let so gentle a hand go dark and drew her essence into the roots. Where her Echo walks the land gives freely -- growth answering your command.",
                EmergeLine = "Green light gathers among the roots -- a grove-warden wakes.",
                ElementType = ElementType.Nature, PreferredLane = LaneType.Harvest,
                Affinity = HarvestTarget.Wood, HarvestResource = ResourceType.Wood,
            },
            new EchoRosterEntry {
                Id = "echo-voidwing-raven", Order = 3,
                DisplayName = "Corvin, the Void Echo", Element = "Essence of a lost scout",
                PortraitName = "VoidwingRaven",
                Flavor = "A shadow unfolds where none stood, and Corvin steps from it -- the scout who ranged the far dark for Elarion and never came home, kept safe within the Heart of the tree.",
                Lore = "Corvin walked the paths between one light and the next, mapping the dark so others need not fear it. When the far road took him, the Heart gathered his essence back to Elarion. His Echo reaches what no other can, carrying spoils across the void.",
                EmergeLine = "A shadow slips free of the bark -- the lost scout comes home.",
                // WO-830: "carrying spoils across the void" = treasure -> Gold (Coins wallet;
                // no ResourceType maps to coins, so HarvestResource stays null).
                ElementType = ElementType.Shadow, PreferredLane = LaneType.Harvest,
                Affinity = HarvestTarget.Gold, HarvestResource = null,
            },
            new EchoRosterEntry {
                Id = "echo-stormcoil-serpent", Order = 4,
                DisplayName = "Bran, the Storm Echo", Element = "Essence of a fallen watchman",
                PortraitName = "StormcoilSerpent",
                Flavor = "Thunder gathers, and Bran stands within it -- the watchman who held Elarion's wall through every gale, wakened from the Heart that keeps him.",
                Lore = "Bran stood the parapet through storms that broke lesser souls, calling every alarm in time. When he fell at his post, the Heart would not lose so steady a guard and kept his essence in its roots. His Echo drives the whole workforce on, restless as the sky that made it.",
                EmergeLine = "Thunder rolls beneath the boughs -- the watchman takes his post.",
                // Crystal gathering is reserved for the final Echo, Maren. Bran's
                // watchman/scout identity instead supports the Gold lane.
                ElementType = ElementType.Storm, PreferredLane = LaneType.Harvest,
                Affinity = HarvestTarget.Gold, HarvestResource = null,
            },
            new EchoRosterEntry {
                Id = "echo-stonewarden-bear", Order = 5,
                DisplayName = "Doran, the Earth Echo", Element = "Essence of an old mason",
                PortraitName = "StonewardenBear",
                Flavor = "The ground shifts and rises, and Doran shakes the dust of ages from his shoulders -- the mason who raised Elarion's stones, kept whole within the tree.",
                Lore = "Doran laid the first stones of Elarion's walls and mended them all his life. When age took him, the Heart drew his essence down among the roots he had built upon. Tireless and unbreakable, his Echo hauls the heaviest loads without complaint.",
                EmergeLine = "The roots grind like millstones -- the old mason shoulders free.",
                // Owner-final map says "Stone", but Stone is RETIRED (DEF-121) and NOT in ResourceType
                // {Iron,Wood,Food,AetherCrystal}; the WO's reconciled table maps this Earth spirit to
                // Iron ("hauls the heaviest loads" = ore). Real-resource-only, no invented type.
                ElementType = ElementType.Earth, PreferredLane = LaneType.Harvest,
                Affinity = HarvestTarget.Iron, HarvestResource = ResourceType.Iron,
            },
            new EchoRosterEntry {
                Id = "echo-ember-phoenix", Order = 6,
                DisplayName = "Maren, the Fire Echo", Element = "Essence of a hearth-keeper",
                PortraitName = "EmberPhoenix",
                Flavor = "From a single spark a firebird rises, and Maren wakes within the flame -- the hearth-keeper whose forge never went cold, kept alight in the Heart of Elarion.",
                Lore = "Maren kept Elarion's forge and hearth burning so no one went without warmth or a mended blade. When her fire finally guttered, the Heart caught the last ember of her essence and held it. Her Echo sets the whole workforce alight -- fastest when the work is hardest.",
                EmergeLine = "An ember climbs out of the heartwood -- the forge-fire wakes.",
                // WO-830 owner ruling 2026-08-02: Repairs affinity REMOVED -- Maren's forge-fire
                // anneals raw aether into crystal. Second Crystals harvester (with Bran).
                ElementType = ElementType.Fire, PreferredLane = LaneType.Harvest,
                Affinity = HarvestTarget.Crystals, HarvestResource = null,
            },
        };

        /// <summary>The full roster in order (never null; length 6).</summary>
        public static EchoRosterEntry[] All => s_all;

        /// <summary>Total spirits in the canonical roster (6).</summary>
        public static int Count => s_all.Length;

        /// <summary>
        /// The entry for a given owned COUNT / order (1-based). Clamped: a count above
        /// the roster returns the last spirit, at/below 0 returns the first, so the
        /// unlock dialogue always has something to show (never null).
        /// </summary>
        public static EchoRosterEntry ByCount(int count)
        {
            if (s_all.Length == 0) return null;
            int idx = Mathf.Clamp(count - 1, 0, s_all.Length - 1);
            return s_all[idx];
        }

        /// <summary>The entry at 0-based index, or null if out of range.</summary>
        public static EchoRosterEntry ByIndex(int index)
        {
            if (index < 0 || index >= s_all.Length) return null;
            return s_all[index];
        }

        // -- harvest-target vocabulary (WO-830) --------------------------------
        // The token strings ARE the persisted echoLanes grammar (EchoAssignments);
        // keep them lowercase-stable forever (save-compat law).

        /// <summary>The persisted/UI token for a harvest target ("wood".."crystals").</summary>
        public static string TargetToken(HarvestTarget target)
        {
            switch (target)
            {
                case HarvestTarget.Wood:     return "wood";
                case HarvestTarget.Iron:     return "iron";
                case HarvestTarget.Stone:     return "food";
                case HarvestTarget.Gold:     return "gold";
                case HarvestTarget.Crystals: return "crystals";
                default:                     return "wood";
            }
        }

        /// <summary>ASCII display label for a harvest target ("Wood".."Crystals").</summary>
        public static string TargetLabel(HarvestTarget target)
        {
            switch (target)
            {
                case HarvestTarget.Wood:     return "Wood";
                case HarvestTarget.Iron:     return "Iron";
                // WO-1163: the ENUM member stays Food - it is frozen persistence vocabulary keyed by
            // saved assignment tokens - but what the PLAYER reads is Stone. Display moves, the
            // token does not (PROD-016 ruling, 2026-08-25).
            case HarvestTarget.Stone:     return "Stone";
                case HarvestTarget.Gold:     return "Gold";
                case HarvestTarget.Crystals: return "Crystals";
                default:                     return "Wood";
            }
        }

        /// <summary>Parse a harvest-target token back to the enum. Returns false on any
        /// non-target token (lane words, idle, garbage) -- caller decides the fallback.</summary>
        public static bool TryTargetFromToken(string token, out HarvestTarget target)
        {
            switch (token)
            {
                case "wood":     target = HarvestTarget.Wood;     return true;
                case "iron":     target = HarvestTarget.Iron;     return true;
                case "food":     target = HarvestTarget.Stone;     return true;
                case "gold":     target = HarvestTarget.Gold;     return true;
                case "crystals": target = HarvestTarget.Crystals; return true;
                default:         target = HarvestTarget.Wood;     return false;
            }
        }

        // -- portrait loader (Texture2D -> Sprite.Create, Guard-wrapped) -----------
        // Cache so re-opening the roster grid doesn't re-create six sprites each time.
        private static readonly System.Collections.Generic.Dictionary<string, Sprite> s_spriteCache =
            new System.Collections.Generic.Dictionary<string, Sprite>();

        /// <summary>
        /// Load a spirit's portrait as a runtime <see cref="Sprite"/> (Resources
        /// Texture2D + Sprite.Create). Returns null (logged, never throws) when the
        /// image is absent -- callers show a text fallback so the card is never blank.
        /// </summary>
        public static Sprite LoadPortrait(string portraitName)
        {
            if (string.IsNullOrEmpty(portraitName)) return null;
            if (s_spriteCache.TryGetValue(portraitName, out var cached) && cached != null)
                return cached;

            var sprite = Guard.Try("Echo", "load echo portrait " + portraitName, () =>
            {
                var tex = Resources.Load<Texture2D>("Echoes/Portraits/" + portraitName);
                if (tex == null)
                {
                    FlowTrace.Warn("Echo", $"LoadPortrait: Resources/Echoes/Portraits/{portraitName} missing -- card shows a text fallback.");
                    return (Sprite)null;
                }
                return Sprite.Create(tex, new Rect(0f, 0f, tex.width, tex.height),
                                     new Vector2(0.5f, 0.5f), 100f);
            }, fallback: null);

            if (sprite != null) s_spriteCache[portraitName] = sprite;
            return sprite;
        }

        /// <summary>
        /// WO-831: load a spirit's EMERGENCE sprite ("rising from the Heart-tree", 2D)
        /// from Resources/Echoes/Emergence/. Tries <c>&lt;PortraitName&gt;_emerge</c> first,
        /// then a bare <c>&lt;PortraitName&gt;</c> in the same folder. Returns null (logged
        /// Warn, never throws) when the art has not been supplied yet -- the caller falls
        /// back to the portrait / text so the unlock beat is NEVER blocked by missing art.
        /// </summary>
        public static Sprite LoadEmergence(string portraitName)
        {
            if (string.IsNullOrEmpty(portraitName)) return null;
            string key = "emerge:" + portraitName;
            if (s_spriteCache.TryGetValue(key, out var cached) && cached != null)
                return cached;

            var sprite = Guard.Try("Echo", "load echo emergence " + portraitName, () =>
            {
                var tex = Resources.Load<Texture2D>("Echoes/Emergence/" + portraitName + "_emerge");
                if (tex == null)
                    tex = Resources.Load<Texture2D>("Echoes/Emergence/" + portraitName);
                if (tex == null)
                {
                    FlowTrace.Warn("Echo", $"LoadEmergence: Resources/Echoes/Emergence/{portraitName}_emerge missing -- emergence beat falls back to the portrait.");
                    return (Sprite)null;
                }
                return Sprite.Create(tex, new Rect(0f, 0f, tex.width, tex.height),
                                     new Vector2(0.5f, 0.5f), 100f);
            }, fallback: null);

            if (sprite != null) s_spriteCache[key] = sprite;
            return sprite;
        }
    }
}

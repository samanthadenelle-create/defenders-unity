// =============================================================================
// HudModels — WO-541 Stage 1: the 11 Core HUD models + IHudModel facade + holder.
// -----------------------------------------------------------------------------
// DARK / ADDITIVE: each model is plain read-only data + a `Changed` event + a
// single producer-only mutator that ASSIGNS, FIRES Changed, then FlowTraces the
// transition ([Flow:HUD]). Views (Stage 3) READ props + subscribe Changed.
// Producers (Village, Stage 2) call the mutators. Nothing reads these yet, so
// this layer changes zero runtime behaviour.
//
// Assembly law: DeNelle.Core only — primitives + Core enums. NO UnityEngine UI,
// NO Village types. Contract: WorkOrders/WO541_MODEL_API.md (FROZEN).
// =============================================================================

using System;
using System.Collections.Generic;
using DeNelle.Core.Diagnostics;

namespace DeNelle.Core.HudModel
{
    // ── HeroVitals ────────────────────────────────────────────────────────────

    /// <summary>The controlled hero's vitals + progression (HP/MP/XP/level/class).</summary>
    public sealed class HeroVitalsModel
    {
        /// <summary>Current hit points.</summary>
        public int Hp { get; private set; }
        /// <summary>Maximum hit points.</summary>
        public int MaxHp { get; private set; }
        /// <summary>Current mana.</summary>
        public int Mana { get; private set; }
        /// <summary>Maximum mana.</summary>
        public int MaxMana { get; private set; }
        /// <summary>Current experience toward the next level.</summary>
        public int Xp { get; private set; }
        /// <summary>Experience required to reach the next level.</summary>
        public int XpToNext { get; private set; }
        /// <summary>Current level.</summary>
        public int Level { get; private set; }
        /// <summary>Hero class identifier.</summary>
        public string ClassId { get; private set; }
        /// <summary>
        /// Unspent Wisdom (skill points). P4 (HUD_OBSIDIAN §3.3): replaces the HUD's
        /// reflection pull of WisdomCurrencyService (VillageHudController.cs:1486-1497) —
        /// the producer reads the service directly (Village-side) and pushes it here.
        /// </summary>
        public int Wisdom { get; private set; }

        /// <summary>
        /// WO-997 §3b: EXACT (un-rounded) current mana for the HUD bar. The int
        /// <see cref="Mana"/> quantized a 10-point pool to 10% fill steps, which made
        /// sub-point regen invisible. Sentinel -1 = "not pushed" — readers fall back
        /// to the int fields, so every existing Set caller stays source-compatible.
        /// </summary>
        public float ManaExact { get; private set; } = -1f;
        /// <summary>WO-997 §3b: exact max mana partner of <see cref="ManaExact"/> (-1 = not pushed).</summary>
        public float MaxManaExact { get; private set; } = -1f;
        /// <summary>WO-999: class resource display name (Mana / Vigor / Focus). Empty = generic MP.</summary>
        public string ResourceDisplayName { get; private set; } = "";

        /// <summary>Raised after any field changes.</summary>
        public event Action Changed;

        /// <summary>Producer-only mutator: assign all fields, fire Changed, trace (throttled).
        /// The two OPTIONAL trailing floats (WO-997 §3b) carry the exact mana; the default -1
        /// means "not provided" and readers fall back to the int Mana/MaxMana.</summary>
        public void Set(int hp, int maxHp, int mana, int maxMana, int xp, int xpToNext, int level, string classId, int wisdom,
                        float manaExact = -1f, float maxManaExact = -1f, string resourceDisplayName = null)
        {
            Hp = hp;
            MaxHp = maxHp;
            Mana = mana;
            MaxMana = maxMana;
            ManaExact = manaExact;
            MaxManaExact = maxManaExact;
            Xp = xp;
            XpToNext = xpToNext;
            Level = level;
            ClassId = classId;
            Wisdom = wisdom;
            ResourceDisplayName = resourceDisplayName ?? "";
            Changed?.Invoke();
            FlowTrace.Throttle("HUD", "herovitals", 1f,
                $"HP {Hp}/{MaxHp} {ResourceDisplayName} {Mana}/{MaxMana} XP {Xp}/{XpToNext} Lv{Level} Wis{Wisdom} [{ClassId}]");
        }
    }

    // ── Party ─────────────────────────────────────────────────────────────────

    /// <summary>The party roster snapshot.</summary>
    public sealed class PartyModel
    {
        /// <summary>The current party members (never null).</summary>
        public IReadOnlyList<PartyMemberRecord> Members { get; private set; } = Array.Empty<PartyMemberRecord>();

        /// <summary>Raised after the roster changes.</summary>
        public event Action Changed;

        /// <summary>Producer-only mutator: replace the roster, fire Changed, trace (throttled — hot).</summary>
        public void SetMembers(IReadOnlyList<PartyMemberRecord> members)
        {
            Members = members ?? Array.Empty<PartyMemberRecord>();
            Changed?.Invoke();
            FlowTrace.Throttle("HUD", "party", 1f, $"party members={Members.Count}");
        }
    }

    // ── Economy ───────────────────────────────────────────────────────────────

    /// <summary>The player's resource balances.</summary>
    public sealed class EconomyModel
    {
        /// <summary>Gold balance.</summary>
        public int Gold { get; private set; }
        /// <summary>Wood balance.</summary>
        public int Wood { get; private set; }
        /// <summary>Iron balance.</summary>
        public int Iron { get; private set; }
        /// <summary>Stone balance.</summary>
        [Newtonsoft.Json.JsonProperty("Food")] public int Stone { get; private set; }
        /// <summary>Crystals balance.</summary>
        public int Crystals { get; private set; }

        /// <summary>Raised after any balance changes.</summary>
        public event Action Changed;

        /// <summary>Producer-only mutator: assign all balances, fire Changed, trace (throttled).</summary>
        public void Set(int gold, int wood, int iron, int food, int crystals)
        {
            Gold = gold;
            Wood = wood;
            Iron = iron;
            Stone = food;
            Crystals = crystals;
            Changed?.Invoke();
            FlowTrace.Throttle("HUD", "economy", 1f, $"G{Gold} W{Wood} I{Iron} F{Stone} C{Crystals}");
        }
    }

    // ── Wave ──────────────────────────────────────────────────────────────────

    /// <summary>The current/next wave state (phase, counts, lookout, banner).</summary>
    public sealed class WaveModel
    {
        /// <summary>Wave lifecycle phase.</summary>
        public WavePhase Phase { get; private set; }
        /// <summary>Current wave number.</summary>
        public int Number { get; private set; }
        /// <summary>Maximum wave number.</summary>
        public int Max { get; private set; }
        /// <summary>Seconds remaining on the pre-wave countdown.</summary>
        public float CountdownRemaining { get; private set; }
        /// <summary>Whether a wave is imminent.</summary>
        public bool Imminent { get; private set; }
        /// <summary>Lookout status text.</summary>
        public string LookoutStatus { get; private set; }
        /// <summary>
        /// WO-1864: enemies this wave still has LEFT — the bodies on the field PLUS the ones the
        /// concurrency cap is holding back as reinforcements (WaveManager.LiveEnemies.Count +
        /// WaveManager.HeldReinforcements).
        /// <para>
        /// ⛔ <b>THIS IS NOT THE FIELD COUNT, and it must never be reduced to one.</b> It was named
        /// <c>EnemiesLive</c> and fed by the field list alone until 2026-09-18, which is why the
        /// counter read a constant "8 enemies remain" for 101 seconds of wave 26 while 26 held
        /// bodies were still coming (owner report + the proving lines are cited on
        /// <c>WaveManager.HeldReinforcements</c>). The cap pins the field AT the cap by design, so
        /// the field count is the one number that cannot answer "how many are left".
        /// </para>
        /// </summary>
        public int EnemiesRemaining { get; private set; }
        /// <summary>
        /// WO-1864: this wave's ROSTER total — the high-water mark of <see cref="EnemiesRemaining"/>
        /// since the wave number last changed, which equals the composed roster
        /// (released + held) as published by WaveManager's "total roster unchanged at N" trace.
        /// Was the live FIELD list's length, so it tracked <see cref="EnemiesRemaining"/> one-for-one
        /// and the progress bar sat pinned at full for every capped wave.
        /// </summary>
        public int EnemiesTotal { get; private set; }
        /// <summary>Banner text shown on wave clear.</summary>
        public string ClearBanner { get; private set; }

        /// <summary>Raised after any field changes.</summary>
        public event Action Changed;

        /// <summary>Producer-only mutator: assign all fields, fire Changed, trace (throttled).</summary>
        /// <param name="remaining">
        /// WO-1864: live-on-field PLUS cap-held reinforcements. Passing the field count alone is the
        /// bug this parameter was renamed to prevent.
        /// </param>
        public void Set(WavePhase phase, int number, int max, float countdown, bool imminent,
            string lookout, int remaining, int total, string banner)
        {
            Phase = phase;
            Number = number;
            Max = max;
            CountdownRemaining = countdown;
            Imminent = imminent;
            LookoutStatus = lookout;
            EnemiesRemaining = remaining;
            EnemiesTotal = total;
            ClearBanner = banner;
            Changed?.Invoke();
            FlowTrace.Throttle("HUD", "wave", 1f, $"{Phase} wave {Number}/{Max} remaining {EnemiesRemaining}/{EnemiesTotal} cd{CountdownRemaining:F1}");
        }
    }

    // ── Target ────────────────────────────────────────────────────────────────

    /// <summary>The currently-locked single target's detail.</summary>
    public sealed class TargetModel
    {
        /// <summary>Whether a target is currently held.</summary>
        public bool HasTarget { get; private set; }
        /// <summary>Target display name.</summary>
        public string Name { get; private set; }
        /// <summary>
        /// WO-1232 (owner ruling 2026-08-26): the target's AUTHORED classification WORD — "BOSS",
        /// "ELITE", or empty for an ordinary enemy (which shows NOTHING, not a blank label).
        /// <para>
        /// This REPLACES the old <c>Level</c> int. That number was <c>maxHp / 25</c> wearing a level
        /// costume — a wave-scaled enemy read "Lv 68" beside a Lv 5 hero. Owner: <i>"HP / 25 is not a
        /// level system."</i> A number is not re-addable here until a real authored stat exists; the
        /// named replacement is a Combat Rating (Low/Even/High/Deadly), which is a separate spec.
        /// A WORD, never a tint — the owner is red/green colourblind. ASCII only (TMP glyph safety).
        /// </para>
        /// </summary>
        public string Badge { get; private set; }
        /// <summary>Target current HP.</summary>
        public int Hp { get; private set; }
        /// <summary>Target maximum HP.</summary>
        public int MaxHp { get; private set; }
        /// <summary>Normalised remaining HP (0..1).</summary>
        public float HpFraction { get; private set; }
        /// <summary>Target combat role.</summary>
        public HudRole Role { get; private set; }
        /// <summary>Whether the target is locked.</summary>
        public bool Locked { get; private set; }

        /// <summary>Raised after the target detail changes (set or cleared).</summary>
        public event Action Changed;

        /// <summary>Producer-only mutator: assign all fields, fire Changed, trace.</summary>
        public void Set(bool has, string name, string badge, int hp, int maxHp, float frac, HudRole role, bool locked)
        {
            HasTarget = has;
            Name = name;
            Badge = badge ?? string.Empty;
            Hp = hp;
            MaxHp = maxHp;
            HpFraction = frac;
            Role = role;
            Locked = locked;
            Changed?.Invoke();
            FlowTrace.Step("HUD", $"target changed: has={HasTarget} '{Name}' badge='{Badge}' {Hp}/{MaxHp} ({Role}) locked={Locked}");
        }

        /// <summary>Producer-only mutator: clear the target, fire Changed, trace.</summary>
        public void Clear()
        {
            HasTarget = false;
            Name = null;
            Badge = string.Empty;
            Hp = 0;
            MaxHp = 0;
            HpFraction = 0f;
            Role = HudRole.None;
            Locked = false;
            Changed?.Invoke();
            FlowTrace.Step("HUD", "target cleared");
        }
    }

    // ── TargetCycle ───────────────────────────────────────────────────────────

    /// <summary>The cyclable target list (scan + distance-sorted by the producer).</summary>
    public sealed class TargetCycleModel
    {
        /// <summary>The current cyclable targets (never null).</summary>
        public IReadOnlyList<TargetRecord> Targets { get; private set; } = Array.Empty<TargetRecord>();

        /// <summary>Raised after the target list changes.</summary>
        public event Action Changed;

        /// <summary>Producer-only mutator: replace the list, fire Changed, trace (throttled — hot).</summary>
        public void SetTargets(IReadOnlyList<TargetRecord> targets)
        {
            Targets = targets ?? Array.Empty<TargetRecord>();
            Changed?.Invoke();
            FlowTrace.Throttle("HUD", "targetcycle", 1f, $"cycle targets={Targets.Count}");
        }
    }

    // ── AssignableLoadout (WO-609) ────────────────────────────────────────────

    /// <summary>
    /// The bottom-middle hotswap bar: up to 4 player-assigned extras from the skill tree
    /// (<see cref="AssignableSkillBar"/>). Separate from the static W/E/R rail.
    /// </summary>
    public sealed class AssignableLoadoutModel
    {
        /// <summary>The four assignable slots (never null).</summary>
        public IReadOnlyList<AbilitySlotRecord> Slots { get; private set; } = Array.Empty<AbilitySlotRecord>();

        /// <summary>Raised after slots or cooldowns change.</summary>
        public event Action Changed;

        /// <summary>Producer-only mutator.</summary>
        public void SetSlots(IReadOnlyList<AbilitySlotRecord> slots)
        {
            Slots = slots ?? Array.Empty<AbilitySlotRecord>();
            Changed?.Invoke();
            FlowTrace.Step("HUD", $"assignable set: slots={Slots.Count}");
        }
    }

    // ── StatusEffects (WO-609 Phase 2) ────────────────────────────────────────

    /// <summary>Active buff/debuff icons for one combatant (player or locked target).</summary>
    public sealed class StatusEffectsModel
    {
        /// <summary>Active status icons, debuffs first then buffs (never null).</summary>
        public IReadOnlyList<StatusIconRecord> Icons { get; private set; } = Array.Empty<StatusIconRecord>();

        /// <summary>Raised after the icon list changes.</summary>
        public event Action Changed;

        /// <summary>Producer-only mutator.</summary>
        public void SetIcons(IReadOnlyList<StatusIconRecord> icons)
        {
            Icons = icons ?? Array.Empty<StatusIconRecord>();
            Changed?.Invoke();
            FlowTrace.Throttle("HUD", "statuseffects", 1f, $"status icons={Icons.Count}");
        }
    }

    // ── ConsumableHotbar (WO-609) ─────────────────────────────────────────────

    /// <summary>Battle potion slot counts (HP + mana draught).</summary>
    public sealed class ConsumableHotbarModel
    {
        /// <summary>Minor heal potions in the village larder.</summary>
        public int HpPotionCount { get; private set; }
        /// <summary>Mana draughts in the village larder.</summary>
        public int ManaPotionCount { get; private set; }

        // Owner directive (2026-07-24): ENFORCED use-cooldown, mirrored from the ability
        // loadout model. Producer pushes remaining/total each tick; the belt tile renders
        // the radial sweep + blocks taps while cooling. Runtime-only (never persisted).
        /// <summary>Seconds of use-cooldown left on the HP potion (0 = ready).</summary>
        public float HpCooldownRemaining { get; private set; }
        /// <summary>The HP potion's full authored use-cooldown (0 = spammable).</summary>
        public float HpCooldownTotal { get; private set; }
        /// <summary>Seconds of use-cooldown left on the mana draught (0 = ready).</summary>
        public float ManaCooldownRemaining { get; private set; }
        /// <summary>The mana draught's full authored use-cooldown (0 = spammable).</summary>
        public float ManaCooldownTotal { get; private set; }

        /// <summary>Raised when either count or a cooldown changes.</summary>
        public event Action Changed;

        /// <summary>Producer-only mutator.</summary>
        public void Set(int hpCount, int manaCount)
        {
            HpPotionCount = hpCount;
            ManaPotionCount = manaCount;
            Changed?.Invoke();
            FlowTrace.Throttle("HUD", "consumablehotbar", 1f, $"pots hp={HpPotionCount} mana={ManaPotionCount}");
        }

        /// <summary>Producer-only mutator: update both potion cooldowns in place, fire Changed,
        /// trace (throttled - hot per-tick cooldown sweep). Mirrors AbilityLoadoutModel.SetCooldown.</summary>
        public void SetCooldown(float hpRemaining, float hpTotal, float manaRemaining, float manaTotal)
        {
            HpCooldownRemaining = hpRemaining;
            HpCooldownTotal = hpTotal;
            ManaCooldownRemaining = manaRemaining;
            ManaCooldownTotal = manaTotal;
            Changed?.Invoke();
            FlowTrace.Throttle("HUD", "consumablecd", 1f, $"pot cd hp {hpRemaining:F1}/{hpTotal:F1} mana {manaRemaining:F1}/{manaTotal:F1}");
        }
    }

    // ── AbilityLoadout ────────────────────────────────────────────────────────

    /// <summary>
    /// The hero's 4 ability slots + their cooldowns.
    /// <para>
    /// ⭐ WO-1105 REVISION (owner 2026-08-16, verbatim: "change the bow and arrow attack to the
    /// action bar and leave the attack as the dagger attack"). The 08-16 morning pass added a
    /// PRIMARY-attack face here (PrimaryLabel / PrimaryIconKey / PrimaryCooldown*) because the bow
    /// had been seated on the primary input. The owner reversed that: the primary attack is the
    /// class-agnostic melee/dagger swing again, and the bow is an ACTION-BAR ABILITY — so its verb,
    /// its icon and its cooldown all ride the ordinary <see cref="AbilitySlotRecord"/> below and
    /// there is nothing left for a second, parallel face to publish.
    /// </para>
    /// </summary>
    public sealed class AbilityLoadoutModel
    {
        /// <summary>The current ability slots (4 expected; never null).</summary>
        public IReadOnlyList<AbilitySlotRecord> Slots { get; private set; } = Array.Empty<AbilitySlotRecord>();

        /// <summary>Raised after the slots or a cooldown changes.</summary>
        public event Action Changed;

        /// <summary>Producer-only mutator: replace all slots, fire Changed, trace.</summary>
        public void SetSlots(IReadOnlyList<AbilitySlotRecord> slots)
        {
            Slots = slots ?? Array.Empty<AbilitySlotRecord>();
            Changed?.Invoke();
            FlowTrace.Step("HUD", $"abilities set: slots={Slots.Count}");
        }

        /// <summary>
        /// Producer-only mutator: update one slot's cooldown in place, fire Changed, trace
        /// (throttled — hot per-frame cooldown tick). Out-of-range index is ignored (traced).
        /// </summary>
        public void SetCooldown(int index, float remaining, float total)
        {
            if (Slots == null || index < 0 || index >= Slots.Count)
            {
                FlowTrace.Step("HUD", $"ability cooldown ignored: index {index} out of range (slots={Slots?.Count ?? 0})");
                return;
            }

            // Rebuild the slot record with the new cooldown (records are immutable).
            var src = Slots[index];
            var updated = new AbilitySlotRecord(src.Key, src.Glyph, src.Name, src.Desc, src.IconKey,
                src.AccentHex, src.Equipped, remaining, total, src.ManaCost, src.Affordable, src.Verb);

            var copy = new AbilitySlotRecord[Slots.Count];
            for (int i = 0; i < Slots.Count; i++) copy[i] = Slots[i];
            copy[index] = updated;
            Slots = copy;

            Changed?.Invoke();
            FlowTrace.Throttle("HUD", "abilitycd", 1f, $"slot {index} cd {remaining:F1}/{total:F1}");
        }
    }

    // ── WorldMetrics ──────────────────────────────────────────────────────────

    /// <summary>Holistic world metrics (heart, towers, population, passives, wards, minimap).</summary>
    public sealed class WorldMetricsModel
    {
        /// <summary>Heart current HP.</summary>
        public int HeartHp { get; private set; }
        /// <summary>Heart maximum HP.</summary>
        public int HeartMaxHp { get; private set; }
        /// <summary>Normalised heart HP (0..1).</summary>
        public float HeartPct { get; private set; }
        /// <summary>Towers currently built.</summary>
        public int TowersBuilt { get; private set; }
        /// <summary>Maximum towers.</summary>
        public int TowersMax { get; private set; }
        /// <summary>Current population.</summary>
        public int Population { get; private set; }
        /// <summary>Passive XP gained per minute.</summary>
        public float PassiveXpPerMin { get; private set; }
        /// <summary>Count of passive-generating towers.</summary>
        public int PassiveTowerCount { get; private set; }
        /// <summary>Current "forgetting" level (decay metric).</summary>
        public int ForgettingLevel { get; private set; }
        /// <summary>Wards currently lit.</summary>
        public int WardsLit { get; private set; }
        /// <summary>Total wards.</summary>
        public int WardsTotal { get; private set; }
        /// <summary>Wards summary text.</summary>
        public string WardsSummary { get; private set; }
        /// <summary>The minimap points of interest (never null).</summary>
        public IReadOnlyList<MinimapPoiRecord> Minimap { get; private set; } = Array.Empty<MinimapPoiRecord>();

        /// <summary>Raised after metrics or the minimap change.</summary>
        public event Action Changed;

        /// <summary>Producer-only mutator: assign all scalar metric fields, fire Changed, trace (throttled).</summary>
        public void SetMetrics(int heartHp, int heartMaxHp, float heartPct, int towersBuilt, int towersMax,
            int population, float passiveXpPerMin, int passiveTowerCount, int forgettingLevel,
            int wardsLit, int wardsTotal, string wardsSummary)
        {
            HeartHp = heartHp;
            HeartMaxHp = heartMaxHp;
            HeartPct = heartPct;
            TowersBuilt = towersBuilt;
            TowersMax = towersMax;
            Population = population;
            PassiveXpPerMin = passiveXpPerMin;
            PassiveTowerCount = passiveTowerCount;
            ForgettingLevel = forgettingLevel;
            WardsLit = wardsLit;
            WardsTotal = wardsTotal;
            WardsSummary = wardsSummary;
            Changed?.Invoke();
            FlowTrace.Throttle("HUD", "worldmetrics", 1f, $"heart {HeartHp}/{HeartMaxHp} towers {TowersBuilt}/{TowersMax} pop {Population} wards {WardsLit}/{WardsTotal}");
        }

        /// <summary>Producer-only mutator: replace the minimap POIs, fire Changed, trace (throttled — hot).
        /// <para>⚠ SUPERSEDED BY <c>DeNelle.Core.World.RealmPinBoard</c> (WO-828/829, 2026-08-21).
        /// The live minimap (<c>HudMinimapWidget</c>) and the parchment Realm Map both read the
        /// pin BOARD, which carries what this record cannot: a label (the colourblind text
        /// channel), a region id (the fog rule), a count, per-source replacement and a visible
        /// cap. This seam has no producers and no readers — it is left in place rather than
        /// deleted, but do NOT wire a new producer to it: two minimap registries is precisely
        /// how the two map surfaces start disagreeing.</para></summary>
        public void SetMinimap(IReadOnlyList<MinimapPoiRecord> minimap)
        {
            Minimap = minimap ?? Array.Empty<MinimapPoiRecord>();
            Changed?.Invoke();
            FlowTrace.Throttle("HUD", "minimap", 1f, $"minimap pois={Minimap.Count}");
        }
    }

    // ── Momentum ──────────────────────────────────────────────────────────────

    /// <summary>Combat momentum (combo, kill-streak, stars, battle/keep-star timers).</summary>
    public sealed class MomentumModel
    {
        /// <summary>Current combo count.</summary>
        public int Combo { get; private set; }
        /// <summary>Current kill streak.</summary>
        public int KillStreak { get; private set; }
        /// <summary>Earned stars.</summary>
        public int Stars { get; private set; }
        /// <summary>Elapsed battle time in seconds.</summary>
        public float BattleElapsed { get; private set; }
        /// <summary>Seconds counting toward the keep-star bonus.</summary>
        public float KeepStarSeconds { get; private set; }

        /// <summary>Raised after any field changes.</summary>
        public event Action Changed;

        /// <summary>Producer-only mutator: assign all fields, fire Changed, trace (throttled).</summary>
        public void Set(int combo, int killStreak, int stars, float elapsed, float keepStar)
        {
            Combo = combo;
            KillStreak = killStreak;
            Stars = stars;
            BattleElapsed = elapsed;
            KeepStarSeconds = keepStar;
            Changed?.Invoke();
            FlowTrace.Throttle("HUD", "momentum", 1f, $"combo {Combo} streak {KillStreak} stars {Stars} t{BattleElapsed:F1}");
        }
    }

    // ── Echo ──────────────────────────────────────────────────────────────────

    /// <summary>The echo workforce state (count/cap + life-force silo fill).</summary>
    public sealed class EchoModel
    {
        /// <summary>Current echo count.</summary>
        public int EchoCount { get; private set; }
        /// <summary>Maximum echoes.</summary>
        public int MaxEchoes { get; private set; }
        /// <summary>Life-force silo amount.</summary>
        public float Silo { get; private set; }
        /// <summary>Normalised silo fill (0..1).</summary>
        public float FillFraction { get; private set; }

        /// <summary>Raised after any field changes.</summary>
        public event Action Changed;

        /// <summary>Producer-only mutator: assign all fields, fire Changed, trace (throttled).</summary>
        public void Set(int count, int max, float silo, float fill)
        {
            EchoCount = count;
            MaxEchoes = max;
            Silo = silo;
            FillFraction = fill;
            Changed?.Invoke();
            FlowTrace.Throttle("HUD", "echo", 1f, $"echoes {EchoCount}/{MaxEchoes} silo {Silo:F1} fill {FillFraction:F2}");
        }
    }

    // ── HudContext ────────────────────────────────────────────────────────────

    /// <summary>The consolidated HUD context (single context writer feeds this — see Stage 2).</summary>
    public sealed class HudContextModel
    {
        /// <summary>The active HUD context.</summary>
        public HudContext Context { get; private set; }
        /// <summary>Whether the hero is in the village.</summary>
        public bool InVillage { get; private set; }
        /// <summary>Whether combat is active.</summary>
        public bool CombatActive { get; private set; }
        /// <summary>Whether a modal is open.</summary>
        public bool ModalOpen { get; private set; }
        /// <summary>
        /// Whether a Build Mode edit session is live (P4, HUD_OBSIDIAN §3.3 — the 4th
        /// space type; replaces the BuildModeHudBridge hack as the HUD's source of truth).
        /// </summary>
        public bool BuildModeActive { get; private set; }

        /// <summary>Raised ONLY when <see cref="Context"/> actually changes value.</summary>
        public event Action Changed;

        /// <summary>
        /// Producer-only mutator: assign all fields. Fires Changed ONLY when the Context
        /// value actually changes, but ALWAYS FlowTraces the state. On a real context
        /// change it ALSO emits the fleet-assertable transition line
        /// "[Flow:HudModel] context A->B" (P4 acceptance: the autopilot transition matrix).
        /// </summary>
        public void Set(HudContext ctx, bool inVillage, bool combat, bool modal, bool buildMode)
        {
            bool contextChanged = Context != ctx;
            var prev = Context;

            Context = ctx;
            InVillage = inVillage;
            CombatActive = combat;
            ModalOpen = modal;
            BuildModeActive = buildMode;

            if (contextChanged)
            {
                // The transition-matrix line the fleet asserts on (P4 contract):
                // e.g. "[Flow:HudModel] context Town->BuildMode".
                FlowTrace.Step("HudModel", $"context {prev}->{ctx}");
                Changed?.Invoke();
            }
            FlowTrace.Step("HUD", $"context {(contextChanged ? prev + " -> " + ctx : ctx.ToString())} (village={InVillage} combat={CombatActive} modal={ModalOpen} build={BuildModeActive})");
        }
    }

    // ── Cast (enemy telegraph) ────────────────────────────────────────────────

    /// <summary>
    /// The currently-visible enemy cast telegraph (P4, HUD_OBSIDIAN §1.7/§3.4 — feeds
    /// BuildCastBar). One cast at a time (V1 single cast bar; latest cast wins). Fed by
    /// the CastProducer from the Enemy.RootedCast seam. Pure data — no scene refs.
    /// </summary>
    public sealed class CastModel
    {
        /// <summary>Display name of the casting enemy.</summary>
        public string CasterName { get; private set; }
        /// <summary>Display name of the ability being cast.</summary>
        public string AbilityName { get; private set; }
        /// <summary>Normalised cast progress (0..1).</summary>
        public float Progress01 { get; private set; }
        /// <summary>Whether a cast is live (the cast bar should show).</summary>
        public bool Visible { get; private set; }

        /// <summary>Raised after the cast state changes (set, progressed, or cleared).</summary>
        public event Action Changed;

        /// <summary>Producer-only mutator: assign the live cast, fire Changed, trace (throttled — hot).</summary>
        public void Set(string casterName, string abilityName, float progress01)
        {
            CasterName = casterName;
            AbilityName = abilityName;
            Progress01 = progress01 < 0f ? 0f : progress01 > 1f ? 1f : progress01;
            Visible = true;
            Changed?.Invoke();
            FlowTrace.Throttle("HudModel", "cast", 1f, $"cast '{CasterName}' {AbilityName} {Progress01:F2}");
        }

        /// <summary>Producer-only mutator: clear the cast (bar hides), fire Changed, trace.</summary>
        public void Clear()
        {
            CasterName = null;
            AbilityName = null;
            Progress01 = 0f;
            Visible = false;
            Changed?.Invoke();
            FlowTrace.Step("HudModel", "cast cleared");
        }
    }

    // ── Facade + holder ───────────────────────────────────────────────────────

    /// <summary>
    /// Read-only facade exposing every HUD model. Views resolve this via
    /// <see cref="CoreServices.HudModel"/>, read props, and subscribe each model's Changed.
    /// </summary>
    public interface IHudModel
    {
        /// <summary>The hero vitals model.</summary>
        HeroVitalsModel HeroVitals { get; }
        /// <summary>The party roster model.</summary>
        PartyModel Party { get; }
        /// <summary>The economy/resources model.</summary>
        EconomyModel Economy { get; }
        /// <summary>The wave-state model.</summary>
        WaveModel Wave { get; }
        /// <summary>The single-target detail model.</summary>
        TargetModel Target { get; }
        /// <summary>The cyclable-target-list model.</summary>
        TargetCycleModel TargetCycle { get; }
        /// <summary>The ability loadout model.</summary>
        AbilityLoadoutModel Abilities { get; }
        /// <summary>The assignable hotswap bar (WO-609).</summary>
        AssignableLoadoutModel Assignable { get; }
        /// <summary>Battle potion counts (WO-609).</summary>
        ConsumableHotbarModel Consumables { get; }
        /// <summary>Player buff/debuff row (WO-609 Phase 2).</summary>
        StatusEffectsModel PlayerStatus { get; }
        /// <summary>Locked-target buff/debuff row (WO-609 Phase 2).</summary>
        StatusEffectsModel TargetStatus { get; }
        /// <summary>The world-metrics model.</summary>
        WorldMetricsModel World { get; }
        /// <summary>The combat-momentum model.</summary>
        MomentumModel Momentum { get; }
        /// <summary>The echo-workforce model.</summary>
        EchoModel Echo { get; }
        /// <summary>The HUD-context model.</summary>
        HudContextModel Context { get; }
        /// <summary>The enemy cast-telegraph model (P4 — feeds BuildCastBar).</summary>
        CastModel Cast { get; }
    }

    /// <summary>
    /// Concrete <see cref="IHudModel"/> holder: constructs and holds exactly one
    /// instance of each model. Producers (Stage 2) write the models; the host
    /// registers this instance with <see cref="CoreServices.RegisterHudModel"/>.
    /// </summary>
    public sealed class HudModel : IHudModel
    {
        /// <inheritdoc/>
        public HeroVitalsModel HeroVitals { get; } = new HeroVitalsModel();
        /// <inheritdoc/>
        public PartyModel Party { get; } = new PartyModel();
        /// <inheritdoc/>
        public EconomyModel Economy { get; } = new EconomyModel();
        /// <inheritdoc/>
        public WaveModel Wave { get; } = new WaveModel();
        /// <inheritdoc/>
        public TargetModel Target { get; } = new TargetModel();
        /// <inheritdoc/>
        public TargetCycleModel TargetCycle { get; } = new TargetCycleModel();
        /// <inheritdoc/>
        public AbilityLoadoutModel Abilities { get; } = new AbilityLoadoutModel();
        /// <inheritdoc/>
        public AssignableLoadoutModel Assignable { get; } = new AssignableLoadoutModel();
        /// <inheritdoc/>
        public ConsumableHotbarModel Consumables { get; } = new ConsumableHotbarModel();
        /// <inheritdoc/>
        public StatusEffectsModel PlayerStatus { get; } = new StatusEffectsModel();
        /// <inheritdoc/>
        public StatusEffectsModel TargetStatus { get; } = new StatusEffectsModel();
        /// <inheritdoc/>
        public WorldMetricsModel World { get; } = new WorldMetricsModel();
        /// <inheritdoc/>
        public MomentumModel Momentum { get; } = new MomentumModel();
        /// <inheritdoc/>
        public EchoModel Echo { get; } = new EchoModel();
        /// <inheritdoc/>
        public HudContextModel Context { get; } = new HudContextModel();
        /// <inheritdoc/>
        public CastModel Cast { get; } = new CastModel();
    }
}

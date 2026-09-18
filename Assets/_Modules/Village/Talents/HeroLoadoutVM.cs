// =============================================================================
// HeroLoadoutVM — the HOT-SWAP skill-bar chooser's PURE ViewModel (MVVM slice).
// -----------------------------------------------------------------------------
// Assembly: DeNelle.Village   Namespace: DeNelle.Village.Talents
//
// ALL chooser STATE + LOGIC lives here, view-agnostic. Mirrors BuildingUpgradeVM:
//   * implements DeNelle.Core.UI.Mvvm.IPanelViewModel (Title / Changed / Close / Dispose)
//   * NO UnityEngine UI types; the View resolves all presentation. Unit-testable
//     without a scene (ARCHITECTURE_PRINCIPLES §2 / §2c).
//   * the View binds it, re-renders on Changed, routes user input back as commands.
//
// DESIGN (owner-correct, 2026-06-28): the hero's bottom-RIGHT bar is the STATIC class
// kit (thrust / parry / heal / shield-bash) and is NOT edited here. THIS chooser fills
// the player-assignable HOT-SWAP bar (the bottom-middle HUD row = AssignableSkillBar)
// from the SKILL-kind nodes unlocked in the skill tree. Assign(slotIndex, abilityId)
// routes to AssignableSkillBarAccess.Assign; tapping a filled slot with nothing picked
// clears it. State sources:
//   * unlocked SKILL ids = WisdomCurrencyService.Unlocked ∩ Skill-kind talent nodes,
//                          each resolving to an AbilityDef via AbilityCatalog.FindById,
//   * hot-swap slot map  = AssignableSkillBar (live hero, via AssignableSkillBarAccess).
// Raises Changed on AssignableSkillBar.Changed + WisdomCurrencyService.Changed.
// =============================================================================

using System;
using System.Collections.Generic;
using DeNelle.Core;
using DeNelle.Core.UI;
using DeNelle.Core.UI.Mvvm;

namespace DeNelle.Village.Talents
{
    /// <summary>One hot-swap bar slot (index 0..N-1) that may hold an assigned skill.</summary>
    public readonly struct LoadoutSlotVM
    {
        public readonly int SlotIndex;        // 0-based hot-swap slot
        public readonly string SlotKey;       // display label "1".."4"
        public readonly string AbilityId;     // assigned id, or "" when empty
        public readonly string AbilityName;   // display name of the assigned ability, or "" when empty

        public LoadoutSlotVM(int slotIndex, string slotKey, string abilityId, string abilityName)
        {
            SlotIndex = slotIndex;
            SlotKey = slotKey;
            AbilityId = abilityId ?? "";
            AbilityName = abilityName ?? "";
        }

        public bool IsEmpty => string.IsNullOrEmpty(AbilityId);
    }

    /// <summary>One unlocked, assignable skill the chooser can drop into a hot-swap slot.</summary>
    public readonly struct SkillChoiceVM
    {
        public readonly string AbilityId;
        public readonly string Name;
        public readonly bool IsEquipped;      // already on the hot-swap bar

        public SkillChoiceVM(string abilityId, string name, bool isEquipped)
        {
            AbilityId = abilityId ?? "";
            Name = name ?? "";
            IsEquipped = isEquipped;
        }
    }

    /// <summary>
    /// Pure ViewModel for the hot-swap chooser. Exposes the <see cref="Slots"/> (the
    /// player-assignable hot-swap bar), the grid of unlocked <see cref="UnlockedSkills"/>,
    /// a current selection, and the Assign/Clear commands. Raises <see cref="Changed"/> on
    /// any bar / unlock change.
    /// </summary>
    public sealed class HeroLoadoutVM : IPanelViewModel, IDisposable
    {
        private readonly Action _onClose;
        private readonly Action _wisdomHandler;
        private Action _barHandler;
        private AssignableSkillBar _barSub;   // the instance we attached _barHandler to (for clean detach)
        private bool _disposed;

        private readonly List<LoadoutSlotVM> _slots = new List<LoadoutSlotVM>(4);
        private readonly List<SkillChoiceVM> _choices = new List<SkillChoiceVM>();

        public HeroLoadoutVM(Action onClose)
        {
            _onClose = onClose;

            var wisdom = WisdomCurrencyService.Instance;
            if (wisdom != null)
            {
                _wisdomHandler = OnModelChanged;
                wisdom.Changed += _wisdomHandler;
            }
            SubscribeBar();

            Rebuild();
        }

        // ── IPanelViewModel ───────────────────────────────────────────────────

        public event Action Changed;

        public string Title => HudStrings.Get(HudStrings.KeyHeroLoadout);

        public void Close() => _onClose?.Invoke();

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            var wisdom = WisdomCurrencyService.Instance;
            if (wisdom != null && _wisdomHandler != null) wisdom.Changed -= _wisdomHandler;
            UnsubscribeBar();
            Changed = null;
        }

        // ── Read-only data the View renders ─────────────────────────────────────

        /// <summary>The player-assignable hot-swap slots (the bottom-middle battle bar). Never null.</summary>
        public IReadOnlyList<LoadoutSlotVM> Slots => _slots;

        /// <summary>Unlocked, assignable skills (Skill-kind talent nodes that resolve to an ability). Never null.</summary>
        public IReadOnlyList<SkillChoiceVM> UnlockedSkills => _choices;

        /// <summary>The currently picked skill id (the View's "tap a skill, then a slot" flow), or "" when none.</summary>
        public string SelectedAbilityId { get; private set; } = "";

        /// <summary>Last action / hint line for the status row.</summary>
        public string Status { get; private set; } = LocalText.Get("talents.loadout.initial_hint");

        // ── Commands ────────────────────────────────────────────────────────────

        /// <summary>Pick a skill from the grid (highlights it; the next slot tap assigns it).</summary>
        public void SelectSkill(string abilityId)
        {
            SelectedAbilityId = abilityId ?? "";
            Status = string.IsNullOrEmpty(SelectedAbilityId)
                ? LocalText.Get("talents.loadout.initial_hint")
                : LocalText.Get("talents.loadout.confirm_hint");
            Raise();
        }

        /// <summary>Slot-tap entry the View uses: assign the picked skill into this hot-swap slot,
        /// or — when nothing is picked — clear a filled slot.</summary>
        public void OnSlotTapped(int slotIndex)
        {
            if (string.IsNullOrEmpty(SelectedAbilityId))
            {
                // Nothing picked → tapping a filled slot removes it.
                if (AssignableSkillBarAccess.EditsLocked) { Status = LocalText.Get("talents.loadout.battle_locked"); Raise(); return; }
                bool cleared = AssignableSkillBarAccess.Clear(slotIndex);
                Status = cleared ? LocalText.Get("talents.loadout.slot_cleared") : LocalText.Get("talents.loadout.pick_first_then_slot");
                Rebuild();
                Raise();
                return;
            }
            Assign(slotIndex, SelectedAbilityId);
        }

        /// <summary>Assign the picked skill (or an explicit id) into hot-swap <paramref name="slotIndex"/>.</summary>
        public void Assign(int slotIndex, string abilityId = null)
        {
            string id = string.IsNullOrEmpty(abilityId) ? SelectedAbilityId : abilityId;
            if (string.IsNullOrEmpty(id)) { Status = LocalText.Get("talents.loadout.pick_first"); Raise(); return; }
            if (AssignableSkillBarAccess.Current == null) { Status = LocalText.Get("common.no_hero_equip"); Raise(); return; }
            if (AssignableSkillBarAccess.EditsLocked) { Status = LocalText.Get("talents.loadout.battle_locked"); Raise(); return; }

            bool ok = AssignableSkillBarAccess.Assign(slotIndex, id);
            if (ok)
            {
                Status = LocalText.Format("talents.loadout.assigned_format", slotIndex + 1);
                SelectedAbilityId = "";
            }
            else
            {
                // Assign returns false for a duplicate id or a redundant assign.
                Status = LocalText.Get("talents.loadout.already_on_bar");
            }
            Rebuild();
            Raise();
        }

        /// <summary>
        /// One-tap "add to bar": auto-assign the selected skill (or an explicit
        /// <paramref name="abilityId"/>) into the first free hot-swap slot, via
        /// <see cref="AssignableSkillBarAccess.TryAdd"/>. Battle-locked + persisted.
        /// </summary>
        public void TryAdd(string abilityId = null)
        {
            string id = string.IsNullOrEmpty(abilityId) ? SelectedAbilityId : abilityId;
            if (string.IsNullOrEmpty(id)) { Status = LocalText.Get("talents.loadout.pick_first"); Raise(); return; }
            if (AssignableSkillBarAccess.Current == null) { Status = LocalText.Get("common.no_hero_equip"); Raise(); return; }
            if (AssignableSkillBarAccess.EditsLocked) { Status = LocalText.Get("talents.loadout.battle_locked"); Raise(); return; }

            bool ok = AssignableSkillBarAccess.TryAdd(id);
            Status = ok ? LocalText.Get("talents.loadout.added_to_bar") : LocalText.Get("talents.loadout.no_free_slot");
            if (ok) SelectedAbilityId = "";
            Rebuild();
            Raise();
        }

        /// <summary>Clear a hot-swap slot directly.</summary>
        public void Clear(int slotIndex)
        {
            if (AssignableSkillBarAccess.EditsLocked) { Status = LocalText.Get("talents.loadout.battle_locked"); Raise(); return; }
            bool ok = AssignableSkillBarAccess.Clear(slotIndex);
            Status = ok ? LocalText.Get("talents.loadout.slot_cleared") : LocalText.Get("talents.loadout.slot_already_empty");
            Rebuild();
            Raise();
        }

        // ── Build (no Unity UI types) ────────────────────────────────────────────

        private void Rebuild()
        {
            BuildSlots();
            BuildChoices();
            // Drop a stale selection (the picked skill got assigned / cleared).
            if (!string.IsNullOrEmpty(SelectedAbilityId))
            {
                bool stillChoosable = false;
                foreach (var c in _choices)
                    if (string.Equals(c.AbilityId, SelectedAbilityId, StringComparison.OrdinalIgnoreCase) && !c.IsEquipped)
                    { stillChoosable = true; break; }
                if (!stillChoosable) SelectedAbilityId = "";
            }
        }

        private void BuildSlots()
        {
            _slots.Clear();
            var bar = AssignableSkillBarAccess.Current;
            for (int i = 0; i < AssignableSkillBar.SlotCount; i++)
            {
                string id = bar != null ? bar.AbilityIdForSlot(i) : null;
                string name = "";
                if (!string.IsNullOrEmpty(id))
                {
                    var def = AbilityCatalog.FindById(id);
                    name = def != null && !string.IsNullOrEmpty(def.Name) ? def.Name : id;
                }
                _slots.Add(new LoadoutSlotVM(i, (i + 1).ToString(), id ?? "", name));
            }
        }

        private void BuildChoices()
        {
            _choices.Clear();
            var wisdom = WisdomCurrencyService.Instance;
            if (wisdom == null || wisdom.Unlocked == null) return;

            // Unlocked talent nodes -> Skill-kind -> abilityId -> AbilityDef. Skip dupes.
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var nodeId in wisdom.Unlocked)
            {
                if (string.IsNullOrEmpty(nodeId)) continue;
                var node = HeroTalentCatalog.FindNode(nodeId);
                if (node == null) continue;

                string abilityId = AbilityIdOf(node);
                if (string.IsNullOrEmpty(abilityId)) continue;          // Stat nodes have none — skip
                if (!IsSkillKind(node, abilityId)) continue;
                if (!seen.Add(abilityId)) continue;

                var def = AbilityCatalog.FindById(abilityId);
                if (def == null) continue;                              // not a real equippable ability
                string name = !string.IsNullOrEmpty(def.Name) ? def.Name : abilityId;
                _choices.Add(new SkillChoiceVM(abilityId, name, AssignableSkillBarAccess.IsAssigned(abilityId)));
            }
        }

        // ── Node kind / ability id readers (data slice fields; safe if absent) ────

        private static bool IsSkillKind(HeroTalentNodeDef n, string abilityId)
        {
            string k = ReadStringField(n, "Kind");
            if (!string.IsNullOrEmpty(k)) return k.Trim().ToLowerInvariant() == "skill";
            // No explicit kind — a node that names an ability is a Skill.
            return !string.IsNullOrEmpty(abilityId);
        }

        private static string AbilityIdOf(HeroTalentNodeDef n)
        {
            if (n == null) return "";
            return ReadStringField(n, "AbilityId") ?? "";
        }

        private static string ReadStringField(object obj, string name)
        {
            if (obj == null) return null;
            var t = obj.GetType();
            var f = t.GetField(name);
            if (f != null) return f.GetValue(obj) as string;
            var p = t.GetProperty(name);
            if (p != null) return p.GetValue(obj, null) as string;
            return null;
        }

        // ── Subscriptions ────────────────────────────────────────────────────────

        private void SubscribeBar()
        {
            var bar = AssignableSkillBarAccess.Current;
            if (bar == null) return;
            _barHandler = OnModelChanged;
            bar.Changed += _barHandler;
            _barSub = bar;
        }

        private void UnsubscribeBar()
        {
            if (_barSub != null && _barHandler != null)
                _barSub.Changed -= _barHandler;
            _barSub = null;
            _barHandler = null;
        }

        private void OnModelChanged()
        {
            if (_disposed) return;
            Rebuild();
            Changed?.Invoke();
        }

        private void Raise() { if (!_disposed) Changed?.Invoke(); }
    }
}

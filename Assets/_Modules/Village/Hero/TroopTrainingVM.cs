// =============================================================================
// TroopTrainingVM — the Barracks "train troops" panel's PURE ViewModel (WO-744
// MVVM migration; extracted from TroopTrainingPanel). Mirrors PartyShopVM /
// BuildingUpgradeVM: ALL state + logic (catalog, unlock gate, army cap, cost,
// affordability, TRAIN) lives here; the View binds it, re-renders on Changed,
// and routes taps back as commands, reading NO game state.
// -----------------------------------------------------------------------------
// Assembly: DeNelle.Village   Namespace: DeNelle.Village.Hero
//
// Implements DeNelle.Core.UI.Mvvm.IPanelViewModel. PURE C# — no UnityEngine UI
// types (no GameObject/Image/Sprite/RectTransform), so it is unit-testable
// without a scene (ARCHITECTURE_PRINCIPLES.md §2 / §2c). Icons are carried as
// KEYS (IconRole/IconName on ItemVM + IconId on TroopDetail); the View resolves
// the actual Sprite from RpgUiCatalog.
//
// The train path (WO-778): CreateDefault wires BarracksService.EnqueueTraining so
// live training is TIMED on the Train channel (CoC parity). Tests inject a
// Func<string,int,int> trainAction, or pass null to fall back to the legacy
// ArmyStorage.TrainNow loop (instant grant — kept for capacity-edge unit tests).
// Outcome.Trained means "accepted" (enqueued OR instant). On success the VM
// pushes the wallet to the town HUD (CoreServices.Hud) and requests a Save.
// =============================================================================

using System;
using System.Collections.Generic;
using DeNelle.Core.State;      // ArmyStorage, GameStateService, ResourceSnapshot host
using DeNelle.Core.UI.Mvvm;    // IPanelViewModel, ItemVM

namespace DeNelle.Village.Hero
{
    /// <summary>
    /// Outcome of a <see cref="TroopTrainingVM.Train"/> command (the View toasts from it).
    /// WO-778: live CreateDefault path returns <see cref="Queued"/> (timed Train channel);
    /// null trainAction (tests / legacy) returns <see cref="Trained"/> on instant mint.
    /// </summary>
    public enum TrainOutcome { Locked, Trained, Failed, Queued }

    /// <summary>What a train attempt did — outcome + count actually trained + the troop's display name.</summary>
    public readonly struct TrainResult
    {
        public readonly TrainOutcome Outcome;
        public readonly int Count;
        public readonly string Name;
        /// <summary>Player-facing refuse reason when Outcome is Failed (maxOwned, army full, etc.).</summary>
        public readonly string Reason;

        public TrainResult(TrainOutcome outcome, int count, string name, string reason = null)
        {
            Outcome = outcome;
            Count = count;
            Name = name;
            Reason = reason;
        }
    }

    /// <summary>
    /// The selected troop's full detail projection — everything the detail card renders, computed
    /// once in the VM so the View is a pure projector (no service reads, no rule invented).
    /// </summary>
    public readonly struct TroopDetail
    {
        public readonly string Name;
        public readonly string Role;            // raw "melee"/"ranged" (View capitalizes / maps to glyph)
        public readonly int Slots;
        public readonly int UnlockBarracksTier;
        public readonly string IconId;          // authored icon key ("" -> role glyph fallback)

        public readonly bool Trainable;
        public readonly string LockedReason;    // "Unlocks at Barracks Tier N - Name"

        public readonly int OwnedCount;
        public readonly int WoundedCount;

        public readonly bool ArmyKnown;
        public readonly int SlotsUsed;
        public readonly int MaxArmySize;
        public readonly bool HasRoom;

        public readonly bool EconomyKnown;
        public readonly bool Affordable;
        public readonly bool CanTrain;

        public readonly int MaxHp;
        public readonly int AttackDamage;
        public readonly float AttackRange;
        public readonly string CostString;

        public TroopDetail(string name, string role, int slots, int unlockBarracksTier, string iconId,
                           bool trainable, string lockedReason, int ownedCount, int woundedCount,
                           bool armyKnown, int slotsUsed, int maxArmySize, bool hasRoom,
                           bool economyKnown, bool affordable, bool canTrain,
                           int maxHp, int attackDamage, float attackRange, string costString)
        {
            Name = name;
            Role = role;
            Slots = slots;
            UnlockBarracksTier = unlockBarracksTier;
            IconId = iconId;
            Trainable = trainable;
            LockedReason = lockedReason;
            OwnedCount = ownedCount;
            WoundedCount = woundedCount;
            ArmyKnown = armyKnown;
            SlotsUsed = slotsUsed;
            MaxArmySize = maxArmySize;
            HasRoom = hasRoom;
            EconomyKnown = economyKnown;
            Affordable = affordable;
            CanTrain = canTrain;
            MaxHp = maxHp;
            AttackDamage = attackDamage;
            AttackRange = attackRange;
            CostString = costString;
        }
    }

    public sealed class TroopTrainingVM : IPanelViewModel, IDisposable
    {
        /// <summary>Icon role key on each troop tile (the View maps it to art; no game state).</summary>
        public const string IconRoleTroop = "troop";

        private readonly IEconomy _economy;
        private readonly ArmyStorage _army;
        private readonly Action _onClose;
        private readonly Action _onSaved;   // GameState save seam (wired in CreateDefault)
        /// <summary>
        /// Optional train action (id, qty) → count accepted. Null = legacy instant
        /// <see cref="ArmyStorage.TrainNow"/> loop (tests). CreateDefault wires
        /// <see cref="BarracksService.EnqueueTraining"/>.
        /// </summary>
        private readonly Func<string, int, int> _trainAction;
        /// <summary>Last enqueue refuse reason from BarracksService (WO-933 maxOwned etc.).</summary>
        private string _lastTrainStopReason;

        private readonly Action<ResourceSnapshot> _ecoHandler;
        private bool _disposed;

        private readonly List<ItemVM> _troops = new List<ItemVM>();
        private readonly Dictionary<string, TroopDetail> _detailById =
            new Dictionary<string, TroopDetail>();

        /// <summary>
        /// The View-side entry point (audit §3.1): resolves the economy handle + the persisted army
        /// HERE so the View never touches EconomyService / GameStateService itself. The Save seam is
        /// wired to GameStateService too — the sole resolution site. Mirrored ShopVM.CreateDefault
        /// (ShopVM deleted 2026-09-06, WO-1430; PartyShopVM is the live shop VM).
        /// WO-778: live train path is timed via BarracksService.EnqueueTraining (Train channel).
        /// </summary>
        public static TroopTrainingVM CreateDefault(Action onClose)
        {
            var svc = GameStateService.Instance;
            var army = svc != null && svc.State != null ? svc.State.Army : null;
            return new TroopTrainingVM(EconomyService.Instance, army, onClose,
                                       () => GameStateService.Instance?.Save(),
                                       (id, qty) =>
                                       {
                                           int n = BarracksService.EnqueueTraining(id, qty, out string stop);
                                           // Stash on the static-less path: CreateDefault wires a
                                           // closure over the instance via the ctor after construct —
                                           // so stop is returned only through TrainInternal below.
                                           // EnqueueTraining out-param is the ONE refuse authority.
                                           s_lastEnqueueStopReason = stop;
                                           return n;
                                       });
        }

        // Capture stop reason from CreateDefault's EnqueueTraining out-param without
        // re-threading every trainAction injection site.
        private static string s_lastEnqueueStopReason;

        /// <param name="trainAction">
        /// Optional. When null, falls back to instant TrainNow (legacy / unit tests).
        /// CreateDefault passes BarracksService.EnqueueTraining.
        /// </param>
        public TroopTrainingVM(IEconomy economy, ArmyStorage army, Action onClose,
                               Action onSaved = null, Func<string, int, int> trainAction = null)
        {
            _economy = economy;
            _army = army;
            _onClose = onClose;
            _onSaved = onSaved;
            _trainAction = trainAction;

            if (_economy != null)
            {
                _ecoHandler = _ => Raise();
                _economy.OnChanged += _ecoHandler;
            }

            Rebuild();
        }

        // ── IPanelViewModel ───────────────────────────────────────────────────

        public event Action Changed;

        public string Title => "Barracks - Train";

        public void Close() => _onClose?.Invoke();

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (_economy != null && _ecoHandler != null) _economy.OnChanged -= _ecoHandler;
            Changed = null;
        }

        // ── Read-only data the View renders ─────────────────────────────────────

        /// <summary>The troop ladder in display order (UnlockBarracksTier ASC, then catalog order).
        /// One <see cref="ItemVM"/> tile each (Name / IconName=iconId / Affordable / Locked +
        /// LockReason). Locked troops stay in the list (ladder education). Never null.</summary>
        public IReadOnlyList<ItemVM> Troops => _troops;

        /// <summary>Live wallet readout (the View's footer chips rebuild from these).</summary>
        public int Gold     => _economy?.Coins ?? 0;
        public int Wood     => _economy?.Wood ?? 0;
        public int Iron     => _economy?.Iron ?? 0;
        public int Stone     => _economy?.Stone ?? 0;
        public int Crystals => _economy?.Crystals ?? 0;

        /// <summary>The selected troop's full detail projection; default(TroopDetail) for an unknown id.</summary>
        public TroopDetail Detail(string id) =>
            id != null && _detailById.TryGetValue(id, out var d) ? d : default;

        // ── Commands ────────────────────────────────────────────────────────────

        /// <summary>
        /// Accepts up to <paramref name="qty"/> of <paramref name="troopId"/> for training.
        /// WO-778: with a trainAction (CreateDefault → EnqueueTraining) this is timed and
        /// reports <see cref="TrainOutcome.Queued"/>; with null trainAction (unit tests)
        /// falls back to instant ArmyStorage.TrainNow and reports <see cref="TrainOutcome.Trained"/>.
        /// On success pushes wallet + Save.
        /// </summary>
        public TrainResult Train(string troopId, int qty)
        {
            var def = TroopCatalog.Find(troopId);
            string name = DisplayName(def, troopId);

            TrainResult result;
            if (def != null && !TroopUnlock.IsTrainable(def))
            {
                // Defensive belt-and-suspenders (the CTA is already disabled when locked) — no spend,
                // no army mutation. Mirrors TrainAndRefresh's refuse branch.
                result = new TrainResult(TrainOutcome.Locked, 0, name);
            }
            else
            {
                _lastTrainStopReason = null;
                s_lastEnqueueStopReason = null;
                int trained = TrainInternal(troopId, qty);
                if (trained > 0)
                {
                    PushHudResources();
                    _onSaved?.Invoke();
                    // Queued when a trainAction is wired (live / injected queue); Trained = instant mint.
                    var outcome = _trainAction != null ? TrainOutcome.Queued : TrainOutcome.Trained;
                    result = new TrainResult(outcome, trained, name);
                }
                else
                {
                    string reason = _lastTrainStopReason ?? s_lastEnqueueStopReason;
                    if (string.IsNullOrEmpty(reason))
                        reason = OwnedCapReason(def) ?? "Army cap full or not enough resources.";
                    result = new TrainResult(TrainOutcome.Failed, 0, name, reason);
                }
            }

            Rebuild();   // re-project owned counts / cap / affordability after the attempt
            Raise();
            return result;
        }

        // Queued path (injected) OR legacy instant TrainNow loop when trainAction is null.
        private int TrainInternal(string troopId, int qty)
        {
            if (string.IsNullOrEmpty(troopId) || qty <= 0) return 0;

            var def = TroopCatalog.Find(troopId);
            if (def == null) return 0;
            if (!TroopUnlock.IsTrainable(def)) return 0;   // NO spend, NO army mutation

            // WO-778: live path uses BarracksService.EnqueueTraining (spend + queue).
            // Injected fakes may spend via eco themselves; CreateDefault wires the service.
            if (_trainAction != null)
            {
                int n = _trainAction(troopId, qty);
                return n > 0 ? n : 0;
            }

            // Legacy instant path (null trainAction — EditMode capacity tests).
            if (_army == null) return 0;
            int trained = 0;
            for (int i = 0; i < qty; i++)
            {
                // slotOf -> TroopDef.Slots (Village-side); tryAfford -> injected economy spend.
                var t = _army.TrainNow(troopId, TroopDialogueCommands.SlotOf, TryAffordFor);
                if (t == null) break;   // cap full OR unaffordable — TrainNow mutated nothing
                trained++;
            }
            return trained;
        }

        // Affordability/spend seam — spend the troop's cost through the injected economy.
        // ResourceCost ctor order: (wood, food, iron, crystals, coins).
        private bool TryAffordFor(string id)
        {
            var d = TroopCatalog.Find(id);
            if (d == null || _economy == null) return false;
            return _economy.TrySpend(new ResourceCost(coins: d.CostGold));
        }

        // Mirror the panel's town-HUD push (owner: "sync on subtract"). Null-safe pure data call
        // through CoreServices.Hud — the sanctioned HUD PUSH seam, no Unity UI types involved.
        private void PushHudResources()
        {
            if (_economy == null) return;
            DeNelle.Core.CoreServices.Hud?.SetResources(_economy.Wood, _economy.Iron, _economy.Stone, _economy.Crystals);
        }

        // ── Build the ladder + per-troop detail (no Unity types) ─────────────────

        private void Rebuild()
        {
            _troops.Clear();
            _detailById.Clear();

            var roster = SortedRoster();
            foreach (var def in roster)
            {
                if (def == null) continue;
                string id = def.Id;
                bool trainable = TroopUnlock.IsTrainable(def);
                var cost = CostOf(def);
                bool affordable = _economy == null || _economy.CanAfford(cost);

                // Ladder tile — Locked/LockReason drive the row plate; icon carried as the id key.
                _troops.Add(new ItemVM(id, DisplayName(def, id), IconRoleTroop,
                                       string.IsNullOrEmpty(def.IconId) ? "" : def.IconId,
                                       0, "", affordable, rarity: null, equipped: false,
                                       locked: !trainable,
                                       lockReason: trainable ? null : TroopUnlock.LockedReason(def)));

                _detailById[id] = BuildDetail(def, trainable, cost, affordable);
            }
        }

        // ALL catalog troops, sorted by UnlockBarracksTier ASC then catalog order (stable insertion
        // sort — preserves catalog order for equal tiers). Locked troops are NEVER filtered out.
        private static List<TroopDef> SortedRoster()
        {
            var list = new List<TroopDef>();
            foreach (var d in TroopCatalog.All) if (d != null) list.Add(d);
            for (int i = 1; i < list.Count; i++)
            {
                var key = list[i];
                int j = i - 1;
                while (j >= 0 && list[j].UnlockBarracksTier > key.UnlockBarracksTier)
                {
                    list[j + 1] = list[j];
                    j--;
                }
                list[j + 1] = key;
            }
            return list;
        }

        private TroopDetail BuildDetail(TroopDef def, bool trainable, ResourceCost cost, bool affordable)
        {
            int owned   = OwnedCount(def.Id);
            int wounded = WoundedCount(def.Id);
            bool armyKnown = _army != null;
            int slotsUsed = armyKnown ? _army.SlotsUsed(TroopDialogueCommands.SlotOf) : 0;
            int maxArmy   = armyKnown ? _army.MaxArmySize : 0;
            // WO-933: capacity = army slots AND per-type maxOwned (incl. wounded + in-flight train).
            bool atOwnedCap = IsAtOwnedCap(def);
            bool hasRoom  = !atOwnedCap
                            && (_army == null
                                || _army.CanTrain(def.Id, TroopDialogueCommands.SlotOf, MaxOwnedOf));
            bool econKnown = _economy != null;
            bool canTrain = trainable && affordable && hasRoom;

            string lockedReason = null;
            if (!trainable) lockedReason = TroopUnlock.LockedReason(def);
            else if (atOwnedCap) lockedReason = OwnedCapReason(def);

            return new TroopDetail(
                DisplayName(def, def.Id),
                string.IsNullOrEmpty(def.Role) ? "" : def.Role,
                def.Slots,
                def.UnlockBarracksTier,
                string.IsNullOrEmpty(def.IconId) ? "" : def.IconId,
                trainable,
                lockedReason,
                owned,
                wounded,
                armyKnown,
                slotsUsed,
                maxArmy,
                hasRoom,
                econKnown,
                affordable,
                canTrain,
                RoundToInt(def.MaxHp),
                RoundToInt(def.AttackDamage),
                def.AttackRange,
                CostString(def));
        }

        /// <summary>TroopDef.MaxOwned lookup for ArmyStorage.CanTrain (0 = unlimited).</summary>
        private static int MaxOwnedOf(string troopId)
        {
            var d = TroopCatalog.Find(troopId);
            return d != null && d.MaxOwned > 0 ? d.MaxOwned : 0;
        }

        /// <summary>
        /// True when maxOwned is set and roster + in-flight train jobs already fill it
        /// (wounded still occupy the cap — CoC scarcity / WO-933 preferred ruling).
        /// </summary>
        private bool IsAtOwnedCap(TroopDef def)
        {
            if (def == null || def.MaxOwned <= 0) return false;
            int owned = _army != null ? _army.CountOfDef(def.Id) : 0;
            int inFlight = BarracksService.CountInFlightTrainOf(def.Id);
            return owned + inFlight >= def.MaxOwned;
        }

        private static string OwnedCapReason(TroopDef def)
        {
            if (def == null || def.MaxOwned <= 0) return null;
            string name = string.IsNullOrEmpty(def.DisplayName) ? def.Id : def.DisplayName;
            if (def.MaxOwned == 1)
                return "Only one " + name + " may be owned (including wounded).";
            return "Owned limit reached for " + name + " (" + def.MaxOwned + ").";
        }

        private int OwnedCount(string troopId)
        {
            if (_army == null || _army.Owned == null) return 0;
            int n = 0;
            foreach (var t in _army.Owned)
                if (t != null && t.TroopDefId == troopId) n++;
            return n;
        }

        private int WoundedCount(string troopId)
        {
            if (_army == null || _army.Owned == null) return 0;
            int n = 0;
            foreach (var t in _army.Owned)
                if (t != null && t.TroopDefId == troopId && t.Wounded) n++;
            return n;
        }

        // ── Pure helpers (System.Math — keeps the VM Unity-free) ─────────────────

        private static ResourceCost CostOf(TroopDef def)
        {
            // ResourceCost ctor order is (wood, food, iron, crystals, coins).
            return def == null ? new ResourceCost() : new ResourceCost(coins: def.CostGold);
        }

        private static string CostString(TroopDef def)
        {
            if (def == null) return "Free";
            var parts = DeNelle.Core.UI.CostFormat.Parts(new[] { ("gold", "Gold", def.CostGold) });
            return parts.Count == 0 ? "Free" : DeNelle.Core.UI.CostFormat.Words(parts);
        }

        private static int RoundToInt(float f) => (int)Math.Floor(f + 0.5f);

        // DisplayName only (a raw troop id is never player-visible). Authored DisplayName wins; the
        // rare unauthored case degrades to a spaced/title-cased id (kept Unity-free — no ElarionUiKit).
        private static string DisplayName(TroopDef def, string fallbackId)
        {
            if (def != null && !string.IsNullOrEmpty(def.DisplayName)) return def.DisplayName;
            string id = def != null && !string.IsNullOrEmpty(def.Id) ? def.Id : fallbackId;
            return SpacedName(id);
        }

        private static string SpacedName(string id)
        {
            if (string.IsNullOrEmpty(id)) return "";
            var raw = id.Replace('-', ' ').Replace('_', ' ').Trim();
            if (raw.Length == 0) return "";
            var parts = raw.Split(' ');
            for (int i = 0; i < parts.Length; i++)
            {
                if (parts[i].Length == 0) continue;
                parts[i] = char.ToUpperInvariant(parts[i][0]) + (parts[i].Length > 1 ? parts[i].Substring(1) : "");
            }
            return string.Join(" ", parts);
        }

        private void Raise() { if (!_disposed) Changed?.Invoke(); }
    }
}
